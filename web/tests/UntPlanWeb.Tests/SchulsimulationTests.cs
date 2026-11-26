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
        // frühestens am Vorabend (vorausplanende Lehrkräfte), sonst nach der Stunde
        Assert.All(kb.Where(k => k.Thema.Length > 0), k => Assert.True(string.CompareOrdinal(k.Geaendert, DateOnly.Parse(k.Datum).AddDays(-1).ToString("yyyy-MM-dd")) >= 0));
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
            // Hinweise an die Klasse und Lehrstoff am Vorabend
            Assert.True(Convert.ToInt32(s.Dienst<Db>().Wert($"SELECT COUNT(*) FROM klassenbuch WHERE notiz_schueler <> '' AND benutzer_id IN {Konten}")) > 0);
            Assert.True(Convert.ToInt32(s.Dienst<Db>().Wert($"SELECT COUNT(*) FROM klassenbuch WHERE thema <> '' AND substr(geaendert, 1, 10) < datum AND benutzer_id IN {Konten}")) > 0);
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
}
