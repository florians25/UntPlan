using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Schulsimulation (Administration → Schulbetrieb simulieren): Klassenbuch, Abwesenheiten, Verspätungen nur in der 1. bis 3. Stunde, Entschuldigungen
/// der Klassenleitung, Krankmeldungen an die Verteilerliste „Vertretung“ (jeden Schultag, meist nachmittags/abends), Prüfungen (eine je Fach und
/// Halbjahr, nicht in Sport), keine Einträge kranker bzw. ausgenommener Lehrkräfte, Nachholen ohne Doppelungen, Entfernen.
/// </summary>
public class SchulsimulationTests
{
    /// <summary>Montag der Beispielwoche, 06:30: Simulation einschalten (Schulleitung „MUE“ ausgenommen), dann bis Freitagabend laufen lassen.</summary>
    private static async Task<(Testserver S, Schulsimulation Sim, Benutzer Admin)> StartenAsync(DateTime bis, Action<Simulationseinstellungen>? pruefen = null)
    {
        var s = await Testserver.StartenAsync();
        var uhr = s.Dienst<Uhr>();
        var bd = s.Dienst<Benutzerdienst>();
        bd.Anlegen("schulleitung", "test-passwort-1", Rollen.Leitung, "MUE", "Anna Müller");
        var admin = bd.NachLogin("admin")!;
        var sim = s.Dienst<Schulsimulation>();
        uhr.Fest = new DateTime(2026, 10, 12, 6, 30, 0);
        // fester Startwert, damit der Test immer denselben Ablauf sieht (sonst je Installation zufällig)
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        var e = sim.Speichern(admin, new Simulationseinstellungen { Aktiv = true });
        pruefen?.Invoke(e);
        uhr.Fest = bis;
        sim.Lauf();
        return (s, sim, admin);
    }

    private static List<T> Liste<T>(Testserver s, string sql, Func<System.Data.Common.DbDataReader, T> f) => s.Dienst<Db>().Liste(sql, f);
    private const string Konten = "(SELECT id FROM benutzer WHERE login LIKE 'sim~%')";

    [Fact]
    public async Task Simulierte_Woche_wie_im_Schulbetrieb()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 16, 23, 30, 0), e => Assert.Contains("MUE", e.Ausgenommen ?? new()));
        await using var _ = s;
        var db = s.Dienst<Db>();

        // Klassenbuch: Lehrstoff und Abwesenheitskontrolle zu den meisten gehaltenen Stunden, nie von der ausgenommenen Schulleitung
        var kb = Liste(s, $"SELECT datum, stunde, lehrer, thema, geaendert, anwesenheit FROM klassenbuch WHERE benutzer_id IN {Konten}",
            r => (Datum: r.Text("datum"), Stunde: r.Zahl("stunde"), Lehrer: r.Text("lehrer"), Thema: r.Text("thema"), Geaendert: r.Text("geaendert"), Anwesenheit: r.TextOderNull("anwesenheit")));
        Assert.True(kb.Count(k => k.Thema.Length > 0) > 100, $"Lehrstoff: {kb.Count(k => k.Thema.Length > 0)}");
        Assert.DoesNotContain(kb, k => k.Lehrer == "MUE");
        // frühestens zwei Tage vorher (im Voraus), sonst zu Beginn bzw. nach der Stunde
        Assert.All(kb.Where(k => k.Thema.Length > 0), k => Assert.True(string.CompareOrdinal(k.Geaendert, DateOnly.Parse(k.Datum).AddDays(-2).ToString("yyyy-MM-dd")) >= 0));
        // Einträge mit der Uhrzeit der Simulation (nach Stundenbeginn, nicht alle zur selben Zeit)
        Assert.True(kb.Select(k => k.Geaendert).Distinct().Count() > 50);

        // Abwesenheiten: Verspätungen nur in der 1. bis 3. Stunde; Fehlen über mehrere Stunden eines Tages (keine zufälligen Einzelstunden)
        var fz = Liste(s, $"SELECT schueler_id, datum, stunde, art, status, minuten FROM fehlzeiten WHERE benutzer_id IN {Konten}",
            r => (Id: r.Zahl("schueler_id"), Datum: r.Text("datum"), Stunde: r.Zahl("stunde"), Art: r.Text("art"), Status: r.Text("status")));
        Assert.NotEmpty(fz);
        Assert.All(fz.Where(f => f.Art == "verspaetet"), f => Assert.InRange(f.Stunde, 1, 3));
        var tageJeSchueler = fz.Where(f => f.Art == "fehlt").GroupBy(f => (f.Id, f.Datum)).ToList();
        Assert.True(tageJeSchueler.Average(g => g.Count()) >= 2, "Krankheit gilt für den ganzen Tag");
        // Anteil fehlender Schüler*innen realistisch klein (184 Schüler*innen, 5 Tage)
        Assert.InRange(tageJeSchueler.Count, 5, 184 * 5 / 8);

        // Krankmeldungen an die Verteilerliste „Vertretung“: jeden Schultag mindestens eine, meist nachmittags oder abends
        var liste = Liste(s, "SELECT besitzer_id, name FROM mitteilung_listen", r => r.Text("name"));
        Assert.Contains("Vertretung", liste);
        var meldungen = Liste(s, $"SELECT betreff, text, erstellt, empfaenger_text FROM mitteilungen WHERE absender_id IN {Konten}",
            r => (Betreff: r.Text("betreff"), Text: r.Text("text"), Erstellt: r.Text("erstellt"), An: r.Text("empfaenger_text")));
        Assert.NotEmpty(meldungen);
        Assert.All(meldungen, m => Assert.Equal("Liste: Vertretung", m.An));
        Assert.All(meldungen, m => Assert.DoesNotContain(" – ", m.Text));
        // die Schulleitung (Mitglied der Liste) erhält die Meldungen
        var sl = s.Dienst<Benutzerdienst>().NachLogin("schulleitung")!;
        Assert.Equal(meldungen.Count, s.Dienst<Mitteilungsdienst>().Posteingang(sl.Id).Count);

        // Klassenbucheinträge nur in Klassen, die die Lehrkraft unterrichtet bzw. vertritt
        var vermerke = Liste(s, $"SELECT klasse, lehrer FROM vermerke WHERE benutzer_id IN {Konten}", r => (Klasse: r.Text("klasse"), Lehrer: r.Text("lehrer")));
        Assert.NotEmpty(vermerke);
        // Status der Seite: Zahlen und Ausnahmen
        var status = System.Text.Json.JsonSerializer.SerializeToNode(sim.Status(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.True(status["zahlen"]!["lehrstoff"]!.GetValue<int>() > 100);
    }

    [Fact]
    public async Task Jeden_Schultag_eine_Krankmeldung_mit_Schwerpunkt_am_Abend()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var sim = s.Dienst<Schulsimulation>();
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var e = new Simulationseinstellungen { Aktiv = true, Seed = 4711, Start = "2026-10-12 06:00", Ausgenommen = new() { "MUE" } };
        var start = new DateTime(2026, 10, 12, 6, 0, 0);
        var w = sim.Welt(paket, e, start, new DateTime(2027, 1, 20));
        var tage = w.Tagesliste.Where(d => d > new DateOnly(2026, 10, 12) && d < new DateOnly(2027, 1, 15)).ToList();
        // jeden Schultag beginnt mindestens ein Ausfall
        Assert.All(tage, d => Assert.Contains(w.Ausfaelle, a => a.Von == d));
        Assert.DoesNotContain(w.Ausfaelle, a => a.Lehrer == "MUE");
        // nie zwei Ausfälle derselben Lehrkraft gleichzeitig
        foreach (var g in w.Ausfaelle.GroupBy(a => a.Lehrer))
        {
            var l = g.OrderBy(a => a.Von).ToList();
            for (var i = 1; i < l.Count; i++) Assert.True(l[i].Von > l[i - 1].Bis);
        }
        // Uhrzeiten der ersten Meldungen: überwiegend nachmittags/abends, einige nachts bzw. früh morgens
        var ereignisse = sim.Ereignisse(paket, e, start, start, new DateTime(2027, 1, 15)).Where(x => x.Schluessel.StartsWith("km|") && x.Schluessel.EndsWith("|start")).ToList();
        Assert.True(ereignisse.Count >= tage.Count);
        double Anteil(Func<int, bool> f) => ereignisse.Count(x => f(x.Zeit.Hour)) / (double)ereignisse.Count;
        Assert.True(Anteil(h => h >= 13 && h < 23) > 0.55, $"nachmittags/abends {Anteil(h => h >= 13 && h < 23):P0}");
        Assert.True(Anteil(h => h < 5 || h >= 23) > 0.02, "nachts");
        Assert.True(Anteil(h => h >= 5 && h < 8) > 0.1, "früh morgens");
        // gleicher Startwert, gleiche Abläufe (Neustart ohne Doppelungen)
        var w2 = new Schulsimulation(s.Dienst<Db>(), s.Dienst<Uhr>(), s.Dienst<Einstellungen>(), s.Dienst<Paketdienst>(), s.Dienst<Plandienst>(), s.Dienst<Klassenbuchdienst>(),
            s.Dienst<Entschuldigungsdienst>(), s.Dienst<Mitteilungsdienst>(), s.Dienst<Benutzerdienst>(), s.Dienst<Rueckmeldungsdienst>(), s.Dienst<Listendienst>(),
            s.Dienst<Verwaltungsdienst>(), s.Dienst<Sprechtagdienst>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Schulsimulation>.Instance).Welt(paket, e, start, new DateTime(2027, 1, 20));
        Assert.Equal(w.Ausfaelle.Select(a => $"{a.Lehrer}{a.Von}{a.Bis}"), w2.Ausfaelle.Select(a => $"{a.Lehrer}{a.Von}{a.Bis}"));
        Assert.Equal(w.Schuelerausfaelle.Count, w2.Schuelerausfaelle.Count);
    }

    [Fact]
    public async Task Kranke_und_ausgenommene_Lehrkraefte_tragen_nichts_ein_Pruefungen_je_Fach_und_Halbjahr()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 16, 23, 30, 0));
        await using var _ = s;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var e = sim.Laden();
        var start = DateTime.Parse(e.Start);
        var w = sim.Welt(paket, e, start, new DateTime(2026, 10, 16, 23, 30, 0));
        var kb = Liste(s, $"SELECT datum, stunde, lehrer FROM klassenbuch WHERE benutzer_id IN {Konten}", r => (Datum: DateOnly.Parse(r.Text("datum")), Stunde: Math.Abs(r.Zahl("stunde")), Lehrer: r.Text("lehrer")));
        var krank = w.Ausfaelle.Where(a => a.Von <= new DateOnly(2026, 10, 16)).ToList();
        Assert.NotEmpty(krank);
        Assert.All(kb, k => Assert.False(w.Krank(k.Lehrer, k.Datum, k.Stunde), $"{k.Lehrer} ist am {k.Datum} krank"));
        Assert.DoesNotContain(kb, k => k.Lehrer == "MUE");
        Assert.DoesNotContain(Liste(s, $"SELECT absender_id FROM mitteilungen m JOIN benutzer b ON b.id = m.absender_id WHERE b.element = 'MUE' AND b.login LIKE 'sim~%'", r => r.Zahl("absender_id")), _ => true);

        // Prüfungen: höchstens eine je Klasse und Fach im Halbjahr, keine in Sport/Religion/Kunst/Musik, höchstens eine je Tag und zwei je Woche
        var (hv, hb) = Schulsimulation.Halbjahre(paket)[0];
        var plan = sim.Pruefungsplanung(paket, e, w, hv, hb, start);
        Assert.NotEmpty(plan);
        Assert.All(plan.GroupBy(p => (p.Klasse, p.Fach)), g => Assert.Single(g));
        Assert.DoesNotContain(plan, p => Schulsimulation.StandardOhnePruefung.Contains(p.Fach));
        Assert.DoesNotContain(plan, p => p.Lehrer == "MUE");
        Assert.All(plan.GroupBy(p => (p.Klasse, p.Datum)), g => Assert.Single(g));
        Assert.All(plan.GroupBy(p => (p.Klasse, Plandienst.Montag(p.Datum))), g => Assert.True(g.Count() <= 2));
        Assert.All(plan, p => Assert.True(p.Datum >= DateOnly.FromDateTime(start).AddDays(7) && p.Datum <= hb.AddDays(-14)));
        Assert.All(plan, p => Assert.True(p.Eintragen < p.Datum.ToDateTime(TimeOnly.MinValue)));
        var eingetragen = Liste(s, $"SELECT titel, klassen FROM rueckmeldungen WHERE typ = 'klausur' AND benutzer_id IN {Konten}", r => r.Text("titel") + r.Text("klassen"));
        Assert.NotEmpty(eingetragen);
        Assert.Equal(eingetragen.Count, eingetragen.Distinct().Count());
        var ef = Liste(s, "SELECT schluessel, zeit, ergebnis FROM simulation_ereignisse WHERE schluessel LIKE 'pr|%|EF|%'", r => $"{r.Text("schluessel")} {r.Text("zeit")} {r.Text("ergebnis")}");
        Assert.True(eingetragen.Any(t => t.StartsWith("Klausur ") && t.Contains("EF")), string.Join("\n", ef) + "\nPLAN " + string.Join(";", plan.Where(p => p.Klasse == "EF").Select(p => $"{p.Fach} {p.Datum} {p.Eintragen}")));
        Assert.Contains(eingetragen, t => t.StartsWith("Klassenarbeit "));
    }

    [Fact]
    public async Task Nachholen_ohne_Doppelungen_und_Entfernen()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 13, 12, 0, 0));
        await using var _ = s;
        var db = s.Dienst<Db>();
        int Zahl(string sql) => Convert.ToInt32(db.Wert(sql));
        var vorher = (Zahl("SELECT COUNT(*) FROM klassenbuch"), Zahl("SELECT COUNT(*) FROM fehlzeiten"), Zahl("SELECT COUNT(*) FROM mitteilungen"), Zahl("SELECT COUNT(*) FROM rueckmeldungen"));
        Assert.True(vorher.Item1 > 0);
        // erneuter Lauf (wie nach einem Neustart: neue Instanz, Stand zurückgesetzt) trägt nichts doppelt ein
        s.Dienst<Einstellungen>().Schreiben("simulation.stand", sim.Laden().Start);
        var zweite = new Schulsimulation(db, s.Dienst<Uhr>(), s.Dienst<Einstellungen>(), s.Dienst<Paketdienst>(), s.Dienst<Plandienst>(), s.Dienst<Klassenbuchdienst>(),
            s.Dienst<Entschuldigungsdienst>(), s.Dienst<Mitteilungsdienst>(), s.Dienst<Benutzerdienst>(), s.Dienst<Rueckmeldungsdienst>(), s.Dienst<Listendienst>(),
            s.Dienst<Verwaltungsdienst>(), s.Dienst<Sprechtagdienst>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Schulsimulation>.Instance);
        Assert.Equal(0, zweite.Lauf().Ausgefuehrt);
        Assert.Equal(vorher, (Zahl("SELECT COUNT(*) FROM klassenbuch"), Zahl("SELECT COUNT(*) FROM fehlzeiten"), Zahl("SELECT COUNT(*) FROM mitteilungen"), Zahl("SELECT COUNT(*) FROM rueckmeldungen")));
        // Fortsetzung: nur neue Ereignisse
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 13, 18, 0, 0);
        Assert.True(sim.Lauf().Ausgefuehrt > 0);

        // simulierte Konten erscheinen in keiner Benutzerliste; die Schulleitung kann auf eine Krankmeldung antworten
        Assert.DoesNotContain(s.Dienst<Benutzerdienst>().Alle(), b => b.Login.StartsWith("sim~"));
        var sl = s.Dienst<Benutzerdienst>().NachLogin("schulleitung")!;
        var meldung = s.Dienst<Mitteilungsdienst>().Posteingang(sl.Id).First();
        var absender = Convert.ToInt32(db.Wert("SELECT absender_id FROM mitteilungen WHERE id = @m", Db.P("@m", meldung.Id)));
        s.Dienst<Mitteilungsdienst>().Senden(sl, s.Dienst<Paketdienst>().Aktuell, new[] { $"benutzer:{absender}" }, "AW: " + meldung.Betreff, "Gute Besserung!", false, antwortAuf: meldung.Id);

        // eigene Einträge der Schulleitung bleiben beim Entfernen erhalten
        var eigen = s.Dienst<Klassenbuchdienst>().VermerkAnlegen(admin, s.Dienst<Paketdienst>().Aktuell!, new DateOnly(2026, 10, 13), "5a", null, "Vermerk", "Eigener Eintrag");
        var api = await s.AlsAsync("admin");
        var status = await Testserver.HolenAsync(api, "/api/app/admin/simulation");
        Assert.True(status["zahlen"]!["lehrstoff"]!.GetValue<int>() > 0);
        var r = await Testserver.JsonAsync(await api.PostAsJsonAsync("/api/app/admin/simulation/entfernen", new { }));
        Assert.True(r["entfernt"]!.GetValue<int>() > 0);
        Assert.Equal(0, Zahl($"SELECT COUNT(*) FROM klassenbuch WHERE benutzer_id IN {Konten}"));
        Assert.Equal(0, Zahl("SELECT COUNT(*) FROM benutzer WHERE login LIKE 'sim~%'"));
        Assert.Equal(0, Zahl("SELECT COUNT(*) FROM fehlzeiten"));
        Assert.Equal(0, Zahl("SELECT COUNT(*) FROM rueckmeldungen WHERE typ = 'klausur'"));
        Assert.Equal(1, Zahl($"SELECT COUNT(*) FROM vermerke WHERE id = {eigen.Id}"));
        Assert.False(sim.Laden().Aktiv);
        Assert.Equal(0, sim.Lauf().Ausgefuehrt);
    }

    [Fact]
    public async Task Nur_die_Administration_schaltet_die_Simulation()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var lehrer = await s.AlsAsync("lk", "lehrer", "SCH");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/simulation")).StatusCode);
        var admin = await s.AlsAsync("admin");
        var d = await Testserver.HolenAsync(admin, "/api/app/admin/simulation");
        Assert.False(d["einstellungen"]!["aktiv"]!.GetValue<bool>());
        Assert.Contains(d["einstellungen"]!["ohnePruefung"]!.AsArray(), x => x!.GetValue<string>() == "SP");
        var neu = await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/simulation", new { aktiv = true, ausgenommen = new[] { "SCH" }, ohnePruefung = new[] { "SP", "KU" }, verteiler = "Vertretung",
            klassenbuch = true, abwesenheiten = true, entschuldigungen = true, krankmeldungen = true, pruefungen = false, sprechtag = false }));
        Assert.True(neu["einstellungen"]!["aktiv"]!.GetValue<bool>());
        Assert.Equal("SCH", neu["einstellungen"]!["ausgenommen"]![0]!.GetValue<string>());
        Assert.True(neu["verteilerVorhanden"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Sprechtag_Einladungen_und_Buchungen_der_Eltern()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var admin = await s.AlsAsync("admin");
        var uhr = s.Dienst<Uhr>();
        uhr.Fest = new DateTime(2026, 10, 12, 6, 0, 0);
        var sj = (await Testserver.HolenAsync(admin, "/api/app/sprechtag"))["schuljahre"]!.AsArray()[0]!["name"]!.GetValue<string>();
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { schuljahr = sj })))["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", new
        {
            name = "Elternsprechtag Herbst", dauer = 10, online = false,
            abschnitte = new[] { new { name = "Nachmittag", datum = "2026-10-22", von = "15:00", bis = "19:00" } },
            phase1Von = "2026-10-13T08:00", phase1Bis = "2026-10-14T18:00", phase2Von = "2026-10-15T08:00", phase2Bis = "2026-10-20T18:00",
        }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { }));
        var sim = s.Dienst<Schulsimulation>();
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        sim.Speichern(s.Dienst<Benutzerdienst>().NachLogin("admin")!, new Simulationseinstellungen { Aktiv = true, Klassenbuch = false, Abwesenheiten = false, Pruefungen = false, Krankmeldungen = false });
        uhr.Fest = new DateTime(2026, 10, 20, 19, 0, 0);
        sim.Lauf();
        var termine = s.Dienst<Sprechtagdienst>().Termine(id);
        Assert.Contains(termine, t => t.ErstelltVon.StartsWith("sim~l.") && t.Art == "einladung");
        Assert.Contains(termine, t => t.Status == "bestaetigt" && t.ErstelltVon.StartsWith("sim~l."));
        Assert.Contains(termine, t => t.Status == "bestaetigt" && t.ErstelltVon.StartsWith("sim~e."));
        // je Lehrkraft und Zeit höchstens ein Termin
        Assert.All(termine.Where(t => t.Aktiv).GroupBy(t => (t.Lehrer, t.Abschnitt, t.Beginn)), g => Assert.Single(g));
    }

    [Fact]
    public async Task Pruefungsfaecher_getrennt_nach_Sek_I_und_Oberstufe_mit_Uebernahme_der_alten_Einstellung()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var (sekI, sekII) = Schulsimulation.Pruefungsfaecher(paket, new Simulationseinstellungen());
        Assert.Equal(new[] { "D", "E", "F", "L", "M", "S" }, sekI.Order());
        Assert.Contains("BI", sekII);
        Assert.Contains("D", sekII);
        Assert.DoesNotContain("SP", sekII);
        Assert.DoesNotContain("KR", sekII);
        Assert.DoesNotContain("BER", sekII);
        // alte Einstellung „Fächer ohne Prüfungen“ (gespeichert vor den Prüfungsfächern) wird übernommen
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"aktiv":false,"ohnePruefung":["SP","F","CH"]}""");
        var sim = s.Dienst<Schulsimulation>();
        var (altI, altII) = Schulsimulation.Pruefungsfaecher(paket, sim.Laden());
        Assert.DoesNotContain("F", altI);
        Assert.DoesNotContain("CH", altII);
        Assert.DoesNotContain("KR", altII);
        Assert.Contains("M", altI);
        // festgelegte Listen gelten; Planung nur in diesen Fächern, Sek I nur Klassenarbeiten, Oberstufe nur Klausuren
        var admin = s.Dienst<Benutzerdienst>().NachLogin("admin")!;
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 12, 6, 30, 0);
        var e = sim.Speichern(admin, sim.Laden() with { Aktiv = true, PruefungSekI = new() { "M", "D" }, PruefungSekII = new() { "E", "BI" } });
        var start = DateTime.Parse(e.Start);
        var w = sim.Welt(paket, e, start, start.AddDays(5));
        var (hv, hb) = Schulsimulation.Halbjahre(paket)[0];
        var plan = sim.Pruefungsplanung(paket, e, w, hv, hb, start);
        Assert.NotEmpty(plan);
        Assert.All(plan.Where(p => p.Klasse is "EF" or "Q1" or "Q2"), p => Assert.Contains(p.Fach, new[] { "E", "BI" }));
        Assert.All(plan.Where(p => p.Klasse is not ("EF" or "Q1" or "Q2")), p => Assert.Contains(p.Fach, new[] { "M", "D" }));
        var status = System.Text.Json.JsonSerializer.SerializeToNode(sim.Status(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(new[] { "D", "M" }, status["einstellungen"]!["pruefungSekI"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Lehrkraefte_aendern_gelegentlich_Raeume_nur_frei_und_im_Voraus()
    {
        var alt = Schulsimulation.Raumaenderungsanteil;
        Schulsimulation.Raumaenderungsanteil = 0.25;
        try
        {
            var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 16, 23, 30, 0));
            await using var _ = s;
            var paket = s.Dienst<Paketdienst>().Aktuell!;
            var rm = Liste(s, $"SELECT von, stunde_von, stunde_bis, raeume, unterricht, erstellt, absender FROM rueckmeldungen WHERE typ = 'raumbuchung' AND benutzer_id IN {Konten}",
                r => (Datum: r.Text("von"), Von: r.Zahl("stunde_von"), Bis: r.Zahl("stunde_bis"), Raeume: System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.Text("raeume"))!,
                    Unterricht: r.Zahl("unterricht"), Erstellt: r.Text("erstellt"), Absender: r.Text("absender")));
            Assert.True(rm.Count >= 3, $"Raumänderungen: {rm.Count}");
            var zeiten = paket.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr, z => z.Beginn);
            // vor Stundenbeginn eingetragen; kein Raum doppelt belegt
            Assert.All(rm, x => Assert.True(string.CompareOrdinal(x.Erstellt.Replace('T', ' ')[..16], $"{x.Datum} {zeiten[x.Von]}") < 0, $"{x.Erstellt} {x.Datum} {x.Von}"));
            foreach (var g in rm.GroupBy(x => x.Datum))
                foreach (var raum in g.SelectMany(x => x.Raeume).Distinct())
                {
                    var belegt = g.Where(x => x.Raeume.Contains(raum)).ToList();
                    for (var i = 0; i < belegt.Count; i++)
                        for (var j = i + 1; j < belegt.Count; j++)
                            Assert.False(belegt[i].Von <= belegt[j].Bis && belegt[j].Von <= belegt[i].Bis && belegt[i].Unterricht != belegt[j].Unterricht, $"{raum} doppelt am {g.Key}");
                }
            Assert.All(rm, x => Assert.NotEqual("MUE", x.Absender));
            // Hinweise an die Klasse (Lehrstoff im Voraus prüft der Test der Zeitpunkte)
            Assert.True(Convert.ToInt32(s.Dienst<Db>().Wert($"SELECT COUNT(*) FROM klassenbuch WHERE notiz_schueler <> '' AND benutzer_id IN {Konten}")) > 0);
            // Entfernen macht die Raumänderungen rückgängig
            sim.Entfernen(admin);
            Assert.Equal(0, Convert.ToInt32(s.Dienst<Db>().Wert("SELECT COUNT(*) FROM rueckmeldungen WHERE typ = 'raumbuchung'")));
        }
        finally { Schulsimulation.Raumaenderungsanteil = alt; }
    }

    [Fact]
    public async Task Pruefungsraum_bei_einem_Teil_der_Klausuren_nur_frei_und_passend()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 12, 6, 30, 0));
        await using var _ = s;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var rm = s.Dienst<Rueckmeldungsdienst>();
        var e = sim.Laden();
        Assert.Equal(30, e.Klausurraumanteil);
        var start = DateTime.Parse(e.Start);
        var w = sim.Welt(paket, e, start, start.AddDays(5));
        var plan = Schulsimulation.Halbjahre(paket).SelectMany(h => sim.Pruefungsplanung(paket, e, w, h.Von, h.Bis, start)).ToList();
        var ef = plan.Where(p => p.Klasse == "EF").ToList();
        Assert.NotEmpty(ef);
        // 0 %: immer der Raum der Stunde; 100 %: in der Oberstufe ein eigener freier Klassenraum mindestens gleicher Größe
        Assert.All(ef, p => Assert.Equal(p.Raeume, sim.Pruefungsraeume(paket, e with { Klausurraumanteil = 0 }, p, new Zufall(1))));
        var mit = ef.Select(p => (P: p, R: sim.Pruefungsraeume(paket, e with { Klausurraumanteil = 100 }, p, new Zufall((ulong)p.Datum.DayNumber)))).ToList();
        Assert.Contains(mit, x => !x.R.SequenceEqual(x.P.Raeume));
        foreach (var (p, r) in mit.Where(x => !x.R.SequenceEqual(x.P.Raeume)))
            Assert.All(r, raum =>
            {
                Assert.DoesNotContain(raum, p.Raeume);
                Assert.Equal("Klassenraum", paket.Raeume[raum].Raumart);
                Assert.True(rm.RaumFrei(paket, p.Datum, raum, p.Von, p.Bis));
            });
        // Anteil realistisch: bei 30 % hat nur ein Teil der Oberstufenklausuren einen eigenen Raum
        var beispiel = mit.First(x => !x.R.SequenceEqual(x.P.Raeume)).P;
        var anteil = Enumerable.Range(0, 400).Count(i => !sim.Pruefungsraeume(paket, e, beispiel, new Zufall((ulong)i * 7919 + 3)).SequenceEqual(beispiel.Raeume)) / 400.0;
        Assert.InRange(anteil, 0.15, 0.45);
        // über die Administration einstellbar
        var api = await s.AlsAsync("admin");
        var neu = await Testserver.JsonAsync(await api.PutAsJsonAsync("/api/app/admin/simulation", new { aktiv = true, klausurraumanteil = 150, klassenbuch = true, abwesenheiten = true,
            entschuldigungen = true, krankmeldungen = true, pruefungen = true, sprechtag = true, lehreraktionen = true, verteiler = "Vertretung" }));
        Assert.Equal(100, neu["einstellungen"]!["klausurraumanteil"]!.GetValue<int>());
    }

    /// <summary>Fächer einer echten Schule (technisches Berufskolleg): Kürzel und Langname, wie sie aus dem Desktop-Programm kommen.</summary>
    private static readonly (string Kurz, string Name)[] Unterrichtsfaecher =
    {
        ("D", "Deutsch"), ("M", "Mathematik"), ("E", "Englisch"), ("F", "Fotografie"), ("L", "Latein"), ("S", "Spanisch"), ("BI", "Biologie"), ("CH", "Chemie"),
        ("PH", "Physik"), ("IF", "Informatik"), ("EK", "Erdkunde"), ("GE", "Geschichte"), ("PK", "Politik"), ("WI", "Wirtschaft-Politik"), ("SW", "Sozialwissenschaften"),
        ("PPL", "Praktische Philosophie"), ("KR", "Kath. Religion"), ("ER", "Evang. Religion"), ("KU", "Kunsterziehung"), ("MU", "Musik"), ("SP", "Sport"),
        ("NW", "Naturwissenschaften"), ("GL", "Gesellschaftslehre"), ("SU", "Sachunterricht"), ("FÖ", "Förderunterricht"), ("AERO", "Aerobic"),
        ("AOR", "Arbeitsorganisation und Selbstmanagement"), ("AWE", "Anwendungsentwicklung"), ("BAU", "Bautechnik"), ("BEB", "Bewegungsbildung"),
        ("BS/NW", "Betriebssysteme/Netzwerke"), ("DB", "Datenbanken"), ("DEU", "Deutsch"), ("DM", "Didaktik/Methodik"), ("EO", "Elektronik"), ("EOT", "Elektronik THEORIE"),
        ("ESO", "Erziehung und Soziales"), ("ET", "Elektrotechnik"), ("EW", "Erziehungswissenschaften"), ("ErSo", "Erziehung und Soziales"),
        ("EvP", "Entwicklung vernetzter Prozesse"), ("FEN", "Fachliches Englisch"), ("FPEH", "Fachpraxis Ernährung und Hauswirtschaft"), ("FPEHT", "Fachpraxis Ernährung THEORIE"),
        ("FPH", "Fachpraxis Holz"), ("FPHT", "Fachpraxis Holz THEORIE"), ("FPTX", "Fachpraxis Textil"), ("FU0", "Fachunterricht 0 (IT-Berufe)"), ("FU1", "Fachunterricht 1 (IT-Berufe)"),
        ("GAM", "Gaming"), ("GF", "Gesundheitsförderung"), ("GID", "Gestaltung von IT-Diensten"), ("GS", "Gesundheitswissenschaften"), ("GT", "Gestaltung"),
        ("ING", "Ingenieurtechnik"), ("IT", "Informationstechnik"), ("ITG", "IT-Grundschutz"), ("KB", "Körperbildung"), ("LIT", "Literatur"), ("MB", "Medienbildung"),
        ("MED", "Med. Grundlagen"), ("MIK", "Mikroprozessortechnik"), ("MP", "Mess- und Prozesstechnik"), ("NH-ET", "Nachhilfe Elektrotechnik"), ("NH-M", "Nachhilfe Mathematik"),
        ("PD", "Produktdesign"), ("PE", "Philosophie/Ethik"), ("PHY", "Physik"), ("PK/WW", "Politik/Wirtschaft"), ("POL", "Politik"), ("PRÜ", "Prüfungsvorbereitung"),
        ("PS", "Psychologie/Psychatrie"), ("PSM", "Psychomotorik"), ("PeAr", "Personal- und Arbeitsorganisation"), ("PfGe", "Pflege und Gesundheit"), ("REL", "Religion"),
        ("SEH", "SW-Pkt. Ernähr. u. Hauswirtschaft"), ("SELM", "Selbstmanagement"), ("SOFT", "Software"), ("SOZ", "Soziologie"), ("SPA", "Spanisch"),
        ("SPK", "Sprache/Kommunikation"), ("SPO", "Sport"), ("SPP", "Sozialpäd. Praxis"), ("SPT", "Sozialpäd. Theorie"), ("SOP", "Sozialpädagogik"), ("SuD", "Software und Daten"),
        ("TI", "Technische Informatik"), ("TL", "Trainingslehre"), ("TPH", "HEP Theorie u. Praxis"), ("WW", "Wirtschaftslehre"), ("D-FÖ", "Deutsch - Förderung"),
        ("MDQ", "Mathe (Doppelqualifikation)"), ("DDQ", "Deutsch (Doppelqualifikation)"), ("BZO", "Berufliche Zukunftsorientierung"), ("PL", "Klassenleitungsstunde"),
        ("KL", "Klassenleitungsstunde"), ("KL-ST", "Klassenleiter-Stunde"), ("SOL", "Selbst organisiertes Lernen"), ("VT", "Vertiefungsbereich"),
    };

    private static readonly (string Kurz, string Name)[] KeinUnterricht =
    {
        ("BER", "Bereitschaft"), ("BEREI", "Vertretungsbereitschaft"), ("SPRE", "SPRECHSTUNDE"), ("STDPL", "STUNDENPLANUNG"), ("V-PLA", "VERTRETUNGSPLANUNG"),
        ("VERAN", "Veranstaltung"), ("SL", "Besprechnug"), ("ZBesp", "Besprechnug"), ("ZUSA", "Besprechnug"), ("ZfsL", "Seminar"), ("Beratung", "Beratung"),
        ("HS", "Homeschooling"), ("MPT", "Multiprofessionelles Team"), ("MPTIK", "MPT-Inklusion"), ("MPTIN", "MPT Integration"), ("ZF_BT", "Betreuung"),
        ("ONLIN", "Online/Distanzunterricht"), ("Onl.", "Online"), ("Onl.1", "Online"), ("Onl.2", "Online"),
    };

    [Fact]
    public void Jedes_Unterrichtsfach_hat_Lehrstoff_kein_Lehrstoff_fuer_Nicht_Unterricht()
    {
        foreach (var (k, n) in Unterrichtsfaecher)
        {
            var g = Schulsimulation.Zuordnung(k, n);
            Assert.True(g is not null, $"{k} {n}");
            Assert.True(g!.Id != "allgemein", $"{k} {n} hat keine eigene Fachgruppe");
            Assert.Contains(g.Stufen.Values, l => l.Count > 0);
        }
        foreach (var (k, n) in KeinUnterricht) Assert.Null(Schulsimulation.Zuordnung(k, n));
        // gleiche Kürzel, andere Fächer: der Langname entscheidet
        Assert.Equal("fotografie", Schulsimulation.Zuordnung("F", "Fotografie")!.Id);
        Assert.Equal("franzoesisch", Schulsimulation.Zuordnung("F", "Französisch")!.Id);
        Assert.Equal("klassenleitung", Schulsimulation.Zuordnung("PL", "Klassenleitungsstunde")!.Id);
        Assert.Equal("philosophie", Schulsimulation.Zuordnung("PL", "Philosophie")!.Id);
        Assert.Equal("elektrotechnik", Schulsimulation.Zuordnung("TI", "Technische Informatik")!.Id);
        Assert.Equal("allgemein", Schulsimulation.Zuordnung("XYZ", "Werkstoffprüfung")!.Id);
    }

    [Fact]
    public async Task Lehrstoffreihe_laeuft_fortlaufend_durchs_Schuljahr_passend_zur_Stufe()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 12, 6, 30, 0));
        await using var _ = s;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var e = sim.Laden();
        var w = sim.Welt(paket, e, DateTime.Parse(e.Start), DateTime.Parse(e.Start));
        var geprueft = 0;
        foreach (var u in paket.Unterricht.Values.Where(u => u.Zeilen.Count > 0 && u.Zeilen[0].Klassen.Count > 0))
        {
            var z = u.Zeilen[0];
            if (Schulsimulation.Zuordnung(z.Fach, paket.Faecher.TryGetValue(z.Fach, out var f) ? f.Name : null) is not { } g) continue;
            var bloecke = sim.Bloecke(paket, u.Id);
            if (bloecke.Count < 20) continue;
            var texte = bloecke.Select(b => sim.Lehrstoff(paket, w, e, z.Fach, z.Klassen[0], u.Id, b.Tag, b.Von)!).ToList();
            Assert.All(texte, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.StartsWith(g.Zyklisch ? "Organisatorisches" : "Einstieg ins Schuljahr", texte[0]);
            if (!g.Zyklisch)
                // nach einer vertretenen bzw. entfallenen Stunde geht es mit derselben Stunde weiter (die Reihe rückt nicht weiter)
                for (var i = 1; i < texte.Count; i++)
                    Assert.True(texte[i] != texte[i - 1] || sim.Ausgefallen(paket, u.Id).Contains(bloecke[i - 1]), $"{z.Fach} {z.Klassen[0]}: gleiche Stunde {texte[i]}");
            Assert.DoesNotContain(texte, t => t.Contains('{'));
            // Stufe: Klasse 5 und 6 nie mit Oberstufenthemen
            if (z.Fach == "M" && z.Klassen[0].StartsWith("5")) Assert.DoesNotContain(texte, t => t.Contains("Integralrechnung") || t.Contains("Ableitung"));
            if (z.Fach == "M" && z.Klassen[0] == "EF") Assert.DoesNotContain(texte, t => t.Contains("Brüche"));
            geprueft++;
        }
        Assert.True(geprueft > 10);
        // Prüfungstermine: Wiederholung davor, Prüfung am Termin, Rückgabe danach
        var (hv, hb) = Schulsimulation.Halbjahre(paket)[0];
        var p = sim.Pruefungsplanung(paket, e, w, hv, hb, DateTime.Parse(e.Start)).First(x => x.Unterricht > 0);
        var bl = sim.Bloecke(paket, p.Unterricht);
        var pi = bl.IndexOf((p.Datum, p.Von));
        Assert.True(pi > 0);
        var klasse = paket.Unterricht[p.Unterricht].Zeilen[0].Klassen[0];
        string Am(int i) => sim.Lehrstoff(paket, w, e, p.Fach, klasse, p.Unterricht, bl[i].Tag, bl[i].Von)!;
        Assert.StartsWith("Klassenarbeit: ", Am(pi));
        Assert.Contains("Wiederholung zur Klassenarbeit", Am(pi - 1));
        Assert.Contains(Enumerable.Range(pi + 1, 6).Where(i => i < bl.Count), i => Am(i).StartsWith("Rückgabe"));
    }

    [Fact]
    public async Task Krankmeldung_nennt_nur_betroffene_Klassen()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 16, 23, 30, 0));
        await using var _ = s;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var e = sim.Laden();
        var w = sim.Welt(paket, e, DateTime.Parse(e.Start), new DateTime(2026, 10, 16, 23, 30, 0));
        var meldungen = Liste(s, $"SELECT b.element, m.betreff, m.text, m.erstellt FROM mitteilungen m JOIN benutzer b ON b.id = m.absender_id WHERE b.login LIKE 'sim~l.%'",
            r => (Lehrer: r.Text("element"), Betreff: r.Text("betreff"), Text: r.Text("text"), Erstellt: DateTime.Parse(r.Text("erstellt"))));
        Assert.NotEmpty(meldungen);
        var klassen = paket.Paket.Elemente.Klassen.Select(k => k.Kurz).ToList();
        var geprueft = 0;
        foreach (var m in meldungen.Where(m => !new[] { "gesund", "wieder", "verlänger", "folge" }.Any(x => m.Betreff.Contains(x, StringComparison.OrdinalIgnoreCase))))
        {
            // erste Meldung: vor dem ersten Ausfalltag bzw. früh morgens an ihm
            var a = w.Ausfaelle.Where(x => x.Lehrer == m.Lehrer && (DateOnly.FromDateTime(m.Erstellt) < x.Von || (DateOnly.FromDateTime(m.Erstellt) == x.Von && m.Erstellt.Hour < 8)))
                .OrderBy(x => x.Von).First();
            var betroffen = sim.BetroffeneStunden(paket, a, a.Tage[Math.Min(a.Tage.Count - 1, 4)]).SelectMany(b => b.Klassen.Split('/')).ToHashSet();
            var genannt = klassen.Where(k => System.Text.RegularExpressions.Regex.IsMatch(m.Text, $@"(?<![\w]){System.Text.RegularExpressions.Regex.Escape(k)}(?![\w])")).ToList();
            Assert.All(genannt, k => Assert.True(betroffen.Contains(k), $"{m.Lehrer}: {k} ist nicht betroffen ({string.Join(",", betroffen)}; {a.Art.Id} {a.Von}..{a.Bis})\n{m.Text.Replace("\n", " | ")}"));
            geprueft++;
        }
        Assert.True(geprueft > 0);
    }

    [Fact]
    public async Task Vergangenheit_nachpflegen_idempotent_mit_Ausnahmen()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var uhr = s.Dienst<Uhr>();
        var bd = s.Dienst<Benutzerdienst>();
        var sl = bd.Anlegen("schulleitung", "test-passwort-1", Rollen.Leitung, "MUE", "Anna Müller");
        var admin = bd.NachLogin("admin")!;
        var paket = s.Dienst<Paketdienst>().Aktuell!;
        var sim = s.Dienst<Schulsimulation>();
        uhr.Fest = new DateTime(2026, 10, 14, 10, 0, 0);
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        sim.Speichern(admin, new Simulationseinstellungen { Aktiv = true });
        // eigener Eintrag der Schulleitung (für eine andere Lehrkraft) vorab
        var plan = s.Dienst<Plandienst>();
        var tag = new DateOnly(2026, 10, 8);
        var karte = plan.Karten(paket, tag, Elementtyp.Lehrer, "SCH").First(k => k.Unterricht.HasValue && k.Status == "regulaer");
        s.Dienst<Klassenbuchdienst>().Speichern(sl, paket, tag, karte.Von, karte.Unterricht!.Value, "Eigener Eintrag der Schulleitung", "", null, "", lehrerHinweis: "SCH");
        var db = s.Dienst<Db>();
        int Zahl(string sql) => Convert.ToInt32(db.Wert(sql));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var st = sim.Nachpflegen(paket, sim.Laden(), new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 13));
        Assert.False(st.Laeuft);
        Assert.Equal(st.Tage, st.Fertig);
        Assert.True(st.Eintraege > 200, $"{st.Eintraege} Einträge in {sw.ElapsedMilliseconds} ms");
        var lehrstoff = Zahl($"SELECT COUNT(*) FROM klassenbuch WHERE thema <> '' AND datum < '2026-10-14' AND benutzer_id IN {Konten}");
        Assert.True(lehrstoff > 150);
        // Ausnahmen und eigene Einträge
        Assert.Equal(0, Zahl($"SELECT COUNT(*) FROM klassenbuch WHERE lehrer = 'MUE' AND benutzer_id IN {Konten}"));
        Assert.Equal("Eigener Eintrag der Schulleitung", db.Wert("SELECT thema FROM klassenbuch WHERE datum = '2026-10-08' AND unterricht = @u AND benutzer_id = @b", Db.P("@u", karte.Unterricht), Db.P("@b", sl.Id)));
        Assert.Equal(0, Convert.ToInt32(db.Wert($"SELECT COUNT(*) FROM klassenbuch WHERE datum = '2026-10-08' AND unterricht = @u AND thema <> '' AND benutzer_id IN {Konten}", Db.P("@u", karte.Unterricht))));
        // gesammelt nachgetragen, nie in der Zukunft
        var zeiten = Liste(s, $"SELECT datum, geaendert FROM klassenbuch WHERE benutzer_id IN {Konten} AND datum < '2026-10-14'", r => (D: r.Text("datum"), G: r.Text("geaendert")));
        // frühestens am Vorabend (vorausplanende Lehrkräfte), nie in der Zukunft
        Assert.All(zeiten, x => Assert.True(string.CompareOrdinal(x.G, "2026-10-14 10:00") < 0 && string.CompareOrdinal(x.G, DateOnly.Parse(x.D).AddDays(-2).ToString("yyyy-MM-dd")) >= 0));
        Assert.Contains(zeiten, x => DateTime.Parse(x.G).DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday && DateTime.Parse(x.G).Date > DateTime.Parse(x.D));
        // Entschuldigungen nachgepflegt
        Assert.True(Zahl($"SELECT COUNT(*) FROM fehlzeiten WHERE status <> 'offen' AND benutzer_id IN {Konten}") > 0);
        // idempotent: zweiter Lauf trägt nichts ein
        var vorher = (Zahl("SELECT COUNT(*) FROM klassenbuch"), Zahl("SELECT COUNT(*) FROM fehlzeiten"), Zahl("SELECT COUNT(*) FROM vermerke"));
        var zweiter = sim.Nachpflegen(paket, sim.Laden(), new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 13));
        Assert.Equal(0, zweiter.Eintraege + zweiter.Uebersprungen);
        Assert.Equal(vorher, (Zahl("SELECT COUNT(*) FROM klassenbuch"), Zahl("SELECT COUNT(*) FROM fehlzeiten"), Zahl("SELECT COUNT(*) FROM vermerke")));
        // über die Oberfläche als Hintergrundauftrag mit Fortschritt; Entfernen räumt alles weg
        var api = await s.AlsAsync("admin");
        var gestartet = await Testserver.JsonAsync(await api.PostAsJsonAsync("/api/app/admin/simulation/nachpflegen", new { von = "2026-10-01", bis = "2026-10-02" }));
        Assert.True(gestartet["laeuft"]!.GetValue<bool>());
        for (var i = 0; i < 100 && sim.Nachpflegestatus.Laeuft; i++) await Task.Delay(100);
        Assert.False(sim.Nachpflegestatus.Laeuft);
        sim.Entfernen(admin);
        Assert.Equal(0, Zahl($"SELECT COUNT(*) FROM klassenbuch WHERE benutzer_id IN {Konten}"));
        Assert.Equal(1, Zahl("SELECT COUNT(*) FROM klassenbuch WHERE thema = 'Eigener Eintrag der Schulleitung'"));
    }

    [Fact]
    public async Task Berufskolleg_nutzt_die_Liste_Sek_II_mit_den_Faechern_der_Schule()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var paket = Testserver.Beispielpaket();
        var faecher = paket["elemente"]!["faecher"]!.AsArray();
        var id = 9000;
        foreach (var (k, n) in new[] { ("DEU", "Deutsch"), ("POL", "Politik"), ("SPA", "Spanisch"), ("FEN", "Fachliches Englisch"), ("PHY", "Physik"), ("AWE", "Anwendungsentwicklung"),
                     ("ET", "Elektrotechnik"), ("SPO", "Sport"), ("REL", "Religion"), ("BEREI", "Vertretungsbereitschaft"), ("SPRE", "SPRECHSTUNDE"), ("KL-ST", "Klassenleiter-Stunde") })
            faecher.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id++, ["kurz"] = k, ["name"] = n, ["farbe"] = "" });
        paket["elemente"]!["klassen"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id++, ["kurz"] = "ITA21", ["name"] = "IT-Assistenten", ["jahrgang"] = "", ["klassenleitung"] = new System.Text.Json.Nodes.JsonArray(), ["stammraum"] = "", ["farbe"] = "" });
        paket["revision"] = 2;
        Assert.True((await s.PaketSendenAsync(paket)).IsSuccessStatusCode);
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        Assert.Equal("bk", Schulsimulation.Stufe(stand, "ITA21"));
        Assert.Equal("sek1o", Schulsimulation.Stufe(stand, "7a"));
        var (sekI, sekII) = Schulsimulation.Pruefungsfaecher(stand, new Simulationseinstellungen());
        Assert.All(new[] { "DEU", "POL", "SPA", "FEN", "PHY", "AWE", "ET", "D", "M" }, f => Assert.Contains(f, sekII));
        Assert.All(new[] { "SPO", "REL", "BEREI", "SPRE", "KL-ST", "SP", "KR", "BER" }, f => Assert.DoesNotContain(f, sekII));
        Assert.DoesNotContain("AWE", sekI);
        // gespeicherte Listen bleiben gültig
        var (eI, eII) = Schulsimulation.Pruefungsfaecher(stand, new Simulationseinstellungen { PruefungSekII = new() { "DEU" }, PruefungSekI = new() { "M" } });
        Assert.Equal(new[] { "DEU" }, eII);
        Assert.Equal(new[] { "M" }, eI);
    }

    [Fact]
    public async Task Klassenbucheintraege_nur_in_eigenen_bzw_vertretenen_Klassen()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        // BRA unterrichtet 7a, aber nicht 6a
        Assert.True(Klassenbuchdienst.UnterrichtetKlasse(stand, "BRA", "7a"));
        Assert.False(Klassenbuchdienst.UnterrichtetKlasse(stand, "BRA", "6a"));
        var bra = await s.AlsAsync("bra", "lehrer", "BRA");
        Assert.True((await bra.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "7a", art = "Vermerk", text = "Eigene Klasse" })).IsSuccessStatusCode);
        var fremd = await bra.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "6a", art = "Vermerk", text = "Fremde Klasse" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, fremd.StatusCode);
        // Schulleitung und Administration: alle Klassen
        var sl = await s.AlsAsync("sl", "leitung", "");
        Assert.True((await sl.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "6a", art = "Vermerk", text = "Schulleitung" })).IsSuccessStatusCode);
        var admin = await s.AlsAsync("admin");
        Assert.True((await admin.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "6a", art = "Vermerk", text = "Administration" })).IsSuccessStatusCode);
        // Klassenleitung der 6a
        var leitung = stand.Klassen["6a"].Klassenleitung[0];
        var kl = await s.AlsAsync("kl6a", "lehrer", leitung);
        Assert.True((await kl.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "6a", art = "Vermerk", text = "Klassenleitung" })).IsSuccessStatusCode);
        // Vertretung: am Tag der vertretenen Stunde auch in einer fremden Klasse
        var plan = s.Dienst<Plandienst>();
        var vertretung = Enumerable.Range(0, 14).Select(i => new DateOnly(2026, 10, 12).AddDays(i))
            .SelectMany(d => stand.Paket.Elemente.Lehrer.SelectMany(l => plan.Karten(stand, d, Elementtyp.Lehrer, l.Kurz)
                .SelectMany(k => k.Klassen.Where(x => x.K.Length > 0 && !Klassenbuchdienst.UnterrichtetKlasse(stand, l.Kurz, x.K)).Select(x => (Tag: d, Lehrer: l.Kurz, Klasse: x.K)))))
            .FirstOrDefault();
        if (vertretung.Lehrer is not null)
        {
            var v = await s.AlsAsync("vertretung", "lehrer", vertretung.Lehrer);
            Assert.True((await v.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = Plandienst.Iso(vertretung.Tag), klasse = vertretung.Klasse, art = "Vermerk", text = "Vertretung" })).IsSuccessStatusCode);
            var anderer = vertretung.Tag.AddDays(vertretung.Tag.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
            if (!plan.Karten(stand, anderer, Elementtyp.Lehrer, vertretung.Lehrer).Any(k => k.Klassen.Any(x => x.K == vertretung.Klasse)))
                Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await v.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = Plandienst.Iso(anderer), klasse = vertretung.Klasse, art = "Vermerk", text = "x" })).StatusCode);
        }
    }

    /// <summary>Abwesende laut Datenbank (Teilnehmer*innen der Stunde) = Schülerkarten der Stundendetails = Zahlen der Klassenbuchübersicht.</summary>
    private static async Task<List<string>> KartenPruefenAsync(Testserver s, HttpClient c, DateOnly d)
    {
        var plan = s.Dienst<Plandienst>(); var st = s.Dienst<Paketdienst>().Aktuell!; var kb = s.Dienst<Klassenbuchdienst>();
        var fehler = new List<string>();
        foreach (var kl in st.Paket.Elemente.Klassen)
        {
            var ue = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/uebersicht?von={Plandienst.Iso(d)}&bis={Plandienst.Iso(d)}&klasse={kl.Kurz}");
            foreach (var k in plan.Karten(st, d, Elementtyp.Klasse, kl.Kurz).Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert"))
            {
                var teil = kb.SchuelerDerStunde(st, k, d).Select(x => x.Id).ToHashSet();
                var db = kb.FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= k.Von && f.Stunde <= k.Bis && teil.Contains(f.SchuelerId)).Select(f => f.SchuelerId).ToHashSet();
                var a = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={Plandienst.Iso(d)}&stunde={k.Von}&unterricht={k.Unterricht}");
                var karten = a["fehlzeiten"]!.AsArray().Select(x => x!["schuelerId"]!.GetValue<int>()).ToHashSet();
                var std = ue["tage"]!.AsArray().SelectMany(t => t!["stunden"]!.AsArray()).FirstOrDefault(x => x!["unterricht"]!.GetValue<int>() == k.Unterricht && x["von"]!.GetValue<int>() == k.Von);
                var zahl = std is null ? db.Count : std["abwesend"]!.GetValue<int>() + std["verspaetet"]!.GetValue<int>();
                if (!karten.SetEquals(db) || zahl != db.Count) fehler.Add($"{kl.Kurz} {k.Fach} {k.Von}: DB [{string.Join(",", db)}], Karten [{string.Join(",", karten)}], Übersicht {zahl}");
            }
        }
        return fehler;
    }

    [Fact]
    public async Task Abwesenheiten_erscheinen_auf_den_Schuelerkarten_und_in_der_Uebersicht()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 14, 23, 0, 0));
        await using var _ = s;
        var api = await s.AlsAsync("admin");
        // simulierte Abwesenheiten (auch Kurse der Oberstufe: gespeichert unter dem Unterricht der Schülerin bzw. des Schülers)
        foreach (var d in new[] { new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14) })
            Assert.Empty(await KartenPruefenAsync(s, api, d));
        // echte Einträge: ganztägige Abwesenheit einer Schülerin der EF über den Abwesenheitsdialog und eine Verspätung in der Stunde
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tag = new DateOnly(2026, 10, 15);
        var ef = stand.Schueler.Where(x => x.Klasse == "EF").Select(x => x.Id).ToList();
        await Testserver.JsonAsync(await api.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new { schuelerIds = new[] { ef[0] }, von = "2026-10-15T00:00", bis = "2026-10-15T23:59", ganztaegig = true, grund = "K" }));
        var karte = stand.Paket.Elemente.Klassen.Where(k => k.Kurz != "EF").SelectMany(kl => s.Dienst<Plandienst>().Karten(stand, tag, Elementtyp.Klasse, kl.Kurz))
            .Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert").OrderBy(k => k.Von).First();
        var fuenf = s.Dienst<Klassenbuchdienst>().SchuelerDerStunde(stand, karte, tag)[0].Id;
        s.Dienst<Uhr>().Fest = Um(tag, karte.Beginn).AddMinutes(10);
        await Testserver.JsonAsync(await api.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = Plandienst.Iso(tag), stunde = karte.Von, unterricht = karte.Unterricht,
            eintraege = new[] { new { schuelerId = fuenf, art = "verspaetet", minuten = 10 } } }));
        var fehler = await KartenPruefenAsync(s, api, tag);
        Assert.Empty(fehler);
        var ue = await Testserver.HolenAsync(api, $"/api/app/klassenbuch/uebersicht?von=2026-10-15&bis=2026-10-15&klasse=EF");
        Assert.All(ue["tage"]![0]!["stunden"]!.AsArray(), x => Assert.True(x!["abwesend"]!.GetValue<int>() >= 1, $"{x["fach"]} {x["von"]}"));
    }

    private static DateTime Um(DateOnly d, string hhmm) => d.ToDateTime(TimeOnly.Parse(hhmm));

    [Fact]
    public async Task Zeitpunkte_Hausaufgaben_ohne_Unterrichtsform_und_Vertretungsstunden()
    {
        var (s, sim, admin) = await StartenAsync(new DateTime(2026, 10, 16, 23, 30, 0));
        await using var _ = s;
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var beginn = stand.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr, z => z.Beginn);
        var ereignisse = Liste(s, "SELECT schluessel, zeit, ergebnis FROM simulation_ereignisse WHERE ergebnis = 'ok' AND (schluessel LIKE 'an|%' OR schluessel LIKE 'kb|%')",
            r => (Art: r.Text("schluessel")[..2], Teile: r.Text("schluessel").Split('|'), Zeit: DateTime.Parse(r.Text("zeit"))));
        double Anteil(string art, Func<DateTime, DateTime, bool> f)
        {
            var l = ereignisse.Where(x => x.Art == art).Select(x => (Zeit: x.Zeit, Beginn: Um(DateOnly.Parse(x.Teile[1]), beginn[int.Parse(x.Teile[3])]))).ToList();
            return l.Count(x => f(x.Zeit, x.Beginn)) / (double)l.Count;
        }
        // Abwesenheitskontrolle zu 80 % zu Beginn der Stunde; Lehrstoff zu 70 % zu Beginn, zu 5 % im Voraus
        Assert.InRange(Anteil("an", (z, b) => z >= b && z <= b.AddMinutes(5)), 0.72, 0.9);
        Assert.InRange(Anteil("kb", (z, b) => z >= b && z <= b.AddMinutes(10)), 0.6, 0.82);
        Assert.InRange(Anteil("kb", (z, b) => z < b), 0.0, 0.12);
        // wenige Hausaufgaben, keine Unterrichtsform
        var kb = Liste(s, $"SELECT thema, hausaufgabe, lehrform FROM klassenbuch WHERE thema <> '' AND benutzer_id IN {Konten}", r => (Thema: r.Text("thema"), Ha: r.Text("hausaufgabe"), Lf: r.Text("lehrform")));
        Assert.InRange(kb.Count(x => x.Ha.Length > 0) / (double)kb.Count, 0.0, 0.1);
        Assert.All(kb, x => Assert.Equal("", x.Lf));
        Assert.DoesNotContain(kb, x => x.Thema.Contains("Gruppenarbeit") || x.Thema.Contains("Partnerarbeit") || x.Thema.Contains("Einzelarbeit"));
        Assert.DoesNotContain("Lehrform: ", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "UntPlanWeb", "wwwroot", "js", "stundendetails.js")));
        // Vertretungsstunden: Vertretungsstoff statt der Reihe, die Reihe der Lerngruppe rückt nicht weiter
        var plan = s.Dienst<Plandienst>();
        var vertretungen = Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 10, 12).AddDays(i)).SelectMany(d => stand.Paket.Elemente.Lehrer
            .SelectMany(l => plan.Karten(stand, d, Elementtyp.Lehrer, l.Kurz).Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert"
                && Schulsimulation.Vertretungslehrkraft(stand, k, l.Kurz) is not null).Select(k => (Tag: d, Lehrer: l.Kurz, Karte: k)))).ToList();
        Assert.NotEmpty(vertretungen);
        var texte = vertretungen.Select(v => s.Dienst<Db>().Wert("SELECT thema FROM klassenbuch WHERE datum = @d AND unterricht = @u AND lehrer = @l AND thema <> ''",
            Db.P("@d", v.Tag), Db.P("@u", v.Karte.Unterricht), Db.P("@l", v.Lehrer)) as string).Where(t => t is not null).ToList();
        Assert.NotEmpty(texte);
        Assert.All(texte, t => Assert.Matches("Vertretung|Stillarbeit|Arbeitsblatt der Fachlehrkraft", t!));
        var e = sim.Laden();
        var w = sim.Welt(stand, e, DateTime.Parse(e.Start), new DateTime(2026, 10, 16, 23, 30, 0));
        var vt = stand.Paket.Tage.SelectMany(t => t.Stunden.Where(x => x.Status == "vertreten" && x.Unterricht.HasValue).Select(x => (Tag: DateOnly.Parse(t.Datum), x.Stunde, U: x.Unterricht!.Value, x.Fach, Klasse: x.Klassen.FirstOrDefault() ?? "")))
            .First(x => x.Klasse.Length > 0 && sim.Bloecke(stand, x.U).Contains((x.Tag, x.Stunde)));
        var bl = sim.Bloecke(stand, vt.U);
        var j = bl.IndexOf((vt.Tag, vt.Stunde));
        if (j + 1 < bl.Count && bl[j + 1].Tag.DayNumber - bl[j].Tag.DayNumber <= 10)
            Assert.Equal(sim.Lehrstoff(stand, w, e, vt.Fach, vt.Klasse, vt.U, bl[j].Tag, bl[j].Von), sim.Lehrstoff(stand, w, e, vt.Fach, vt.Klasse, vt.U, bl[j + 1].Tag, bl[j + 1].Von));
    }
}
