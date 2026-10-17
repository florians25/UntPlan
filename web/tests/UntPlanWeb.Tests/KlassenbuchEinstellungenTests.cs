using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenbuch | Einstellungen und Unterricht | Einstellungen: Standardwerte (unbegrenzt), Rechte, Prüfung der Eingaben und die Wirkung im
/// Klassenbuch (Eingabezeitraum, Lehrstoff, Löschsperre, Ende einer Abwesenheit, Standardgrund/-status, offene Stunden, Prüfungen),
/// Erinnerung an offene Stunden und automatische Entschuldigung.
/// </summary>
public class KlassenbuchEinstellungenTests
{
    private const string Url = "/api/app/klassenbuch/einstellungen";
    private static readonly DateOnly Heute = DateOnly.FromDateTime(Testserver.Jetzt);

    private static void Setzen(Testserver s, Func<KlassenbuchEinstellungen, KlassenbuchEinstellungen> aendern) =>
        Klassenbucheinstellungen.Speichern(s.Dienst<Db>(), aendern(Klassenbucheinstellungen.Laden(s.Dienst<Db>())));

    private static Karte KarteVon(Testserver s, DateOnly d, string typ, string element, Func<Karte, bool>? filter = null) =>
        s.Dienst<Plandienst>().Karten(s.Dienst<Paketdienst>().Aktuell!, d, typ, element)
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0 && (filter?.Invoke(x) ?? true)).OrderBy(x => x.Von).First();

    [Fact]
    public async Task Standardwerte_sind_unbegrenzt_und_nur_mit_Recht_aenderbar()
    {
        await using var s = await Testserver.StartenAsync();
        var lehrer = await s.AlsAsync("sne", "lehrer", "SNE");
        var r = await Testserver.HolenAsync(lehrer, Url);
        var w = r["einstellungen"]!;
        Assert.False(r["darf"]!.GetValue<bool>());
        Assert.Null(w["tageVergangenheit"]);
        Assert.Null(w["tageZukunft"]);
        Assert.Null(w["lehrstoffTage"]);
        Assert.True(w["lehrstoffZukunft"]!.GetValue<bool>());
        Assert.Equal(0, w["loeschsperre"]!.GetValue<int>());
        Assert.Equal("stunde", w["abwesenheitEnde"]!.GetValue<string>());
        Assert.Equal("woche", w["offenAb"]!.GetValue<string>());

        // Lehrkraft darf nicht ändern, Administration schon (Recht im Katalog, Standard nur Administration)
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PutAsJsonAsync(Url, new { loeschsperre = 5 })).StatusCode);
        Assert.Equal(new[] { Stufe.Ja, Stufe.Nein, Stufe.Nein, Stufe.Nein, Stufe.Nein }, Recht.NachId[Recht.KlassenbuchEinstellungen].Standard);
        var admin = await s.AlsAsync("admin");
        var neu = await Testserver.JsonAsync(await admin.PutAsJsonAsync(Url, new { loeschsperre = 48, tageVergangenheit = 7, sortierung = "vorname", abwesenheitEnde = "zeit", abwesenheitEndeZeit = "13:15", fehlstundeAb = "20 min" }));
        Assert.Equal((48, 7, "vorname", "20"), (neu["loeschsperre"]!.GetValue<int>(), neu["tageVergangenheit"]!.GetValue<int>(), neu["sortierung"]!.GetValue<string>(), neu["fehlstundeAb"]!.GetValue<string>()));
        Assert.Null(neu["tageZukunft"]);

        // ungültige Angaben
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new { loeschsperre = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new { abwesenheitEnde = "zeit", abwesenheitEndeZeit = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new { tageZukunft = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new { datumVon = "2026-12-01", datumBis = "2026-09-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new { fehltagAb = "150%" })).StatusCode);

        // die Hauptseite (Stundendetails) erhält die Vorgaben mit der Anwesenheitsliste
        var k = KarteVon(s, Heute, Elementtyp.Lehrer, "SNE");
        var anw = await Testserver.HolenAsync(lehrer, $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        Assert.Equal("vorname", anw["einstellungen"]!["sortierung"]!.GetValue<string>());
        Assert.Equal("13:15", anw["einstellungen"]!["abwesenheitEndeZeit"]!.GetValue<string>());
        Assert.Equal("2026-10-12", anw["einstellungen"]!["eintraegeAb"]!.GetValue<string>());
    }

    [Fact]
    public async Task Eingabezeitraum_und_Lehrstoffgrenzen_wirken_beim_Speichern()
    {
        await using var s = await Testserver.StartenAsync();
        // Lehrkraft mit Unterricht am Montag und am Freitag der Beispielwoche
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        bool Hat(string l, DateOnly d) => s.Dienst<Plandienst>().Karten(stand, d, Elementtyp.Lehrer, l).Any(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0);
        var kuerzel = stand.Paket.Elemente.Lehrer.Select(l => l.Kurz).First(l => Hat(l, new DateOnly(2026, 10, 12)) && Hat(l, new DateOnly(2026, 10, 16)));
        var c = await s.AlsAsync(kuerzel.ToLowerInvariant(), "lehrer", kuerzel);
        var montag = KarteVon(s, new DateOnly(2026, 10, 12), Elementtyp.Lehrer, kuerzel);
        var freitag = KarteVon(s, new DateOnly(2026, 10, 16), Elementtyp.Lehrer, kuerzel);
        Task<HttpResponseMessage> Lehrstoff(Karte k, string thema, HttpClient? wer = null) =>
            (wer ?? c).PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema });

        // Standard: unbegrenzt in beide Richtungen
        await Testserver.JsonAsync(await Lehrstoff(montag, "Rückblick"));
        await Testserver.JsonAsync(await Lehrstoff(freitag, "Vorschau"));

        // Lehrstoff nur bis 1 Tag zurück und nicht für die Zukunft
        Setzen(s, e => e with { LehrstoffTage = 1, LehrstoffZukunft = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await Lehrstoff(montag, "Rückblick 2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Lehrstoff(freitag, "Vorschau 2")).StatusCode);
        // unveränderter Lehrstoff (z. B. nur Notiz) bleibt speicherbar; die Administration ist ausgenommen
        await Testserver.JsonAsync(await Lehrstoff(montag, "Rückblick"));
        await Testserver.JsonAsync(await Lehrstoff(freitag, "Vorschau 3", await s.AlsAsync("admin")));

        // allgemeiner Eingabezeitraum gilt auch für Klassenbucheinträge
        Setzen(s, e => e with { LehrstoffTage = null, LehrstoffZukunft = true, TageVergangenheit = 0 });
        var kl = montag.Klassen[0].K;
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-12", klasse = kl, art = "Vermerk", text = "zu spät" })).StatusCode);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = kl, art = "Vermerk", text = "heute" }));

        // Datumsbereich
        Setzen(s, e => e with { TageVergangenheit = null, DatumVon = "2026-10-15" });
        Assert.Equal(HttpStatusCode.BadRequest, (await Lehrstoff(montag, "Neu")).StatusCode);
    }

    [Fact]
    public async Task Loeschsperre_nach_Stunden_ausser_fuer_die_Administration()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var kl = KarteVon(s, Heute, Elementtyp.Lehrer, "SNE").Klassen[0].K;
        async Task<int> Anlegen(string text) => (await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = kl, art = "Vermerk", text })))["id"]!.GetValue<int>();
        var a = await Anlegen("eins");
        var b = await Anlegen("zwei");
        Setzen(s, e => e with { Loeschsperre = 1 });
        s.Dienst<Uhr>().Fest = Testserver.Jetzt.AddHours(2);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/api/app/klassenbuch/vermerke/{a}")).StatusCode);
        // innerhalb der Frist erlaubt
        Setzen(s, e => e with { Loeschsperre = 3 });
        Assert.True((await c.DeleteAsync($"/api/app/klassenbuch/vermerke/{a}")).IsSuccessStatusCode);
        Setzen(s, e => e with { Loeschsperre = 1 });
        var admin = await s.AlsAsync("admin");
        Assert.True((await admin.DeleteAsync($"/api/app/klassenbuch/vermerke/{b}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Ende_der_Abwesenheit_Tagesende_mit_Verkuerzen_Standardgrund_und_Status()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var kb = s.Dienst<Klassenbuchdienst>();
        var karten = s.Dienst<Plandienst>().Karten(s.Dienst<Paketdienst>().Aktuell!, Heute, Elementtyp.Klasse, "7a")
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer").OrderBy(x => x.Von).ToList();
        var erste = karten[0];
        var spaeter = karten.Where(x => x.Von > erste.Bis).ToList();
        Assert.True(spaeter.Count >= 2);
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/gruende", new { kurz = "KX", name = "krank (Test)", art = "abwesenheit", entschuldigt = true, reihenfolge = 1, aktiv = true }));
        Setzen(s, e => e with { AbwesenheitEnde = "tag", GrundAbwesend = "KX", StandardStatus = "unentschuldigt" });
        var anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum={erste.Datum}&stunde={erste.Von}&unterricht={erste.Unterricht}");
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        Assert.Equal("KX", anw["standardgruende"]!["abwesend"]!.GetValue<string>());

        // Häkchen „abwesend“ in der ersten Stunde: gilt bis Tagesende, mit Standardgrund und Standardstatus
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = erste.Datum, stunde = erste.Von, unterricht = erste.Unterricht, eintraege = new[] { new { schuelerId = sid, art = "fehlt", minuten = 0 } } }));
        var fz = kb.FehlzeitenVon(sid, Heute, Heute);
        Assert.Contains(fz, f => f.Stunde == erste.Von);
        Assert.All(spaeter, k => Assert.Contains(fz, f => f.Stunde == k.Von));
        Assert.All(fz, f => Assert.Equal(("KX", "unentschuldigt"), (f.Grund, f.Status)));

        // in der vorletzten Stunde wieder da: verkürzen – frühere Stunden bleiben, spätere entfallen
        var zurueck = spaeter[^2];
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/loeschen", new { datum = zurueck.Datum, stunde = zurueck.Von, unterricht = zurueck.Unterricht, schuelerIds = new[] { sid } }));
        fz = kb.FehlzeitenVon(sid, Heute, Heute);
        Assert.Contains(fz, f => f.Stunde == erste.Von);
        Assert.DoesNotContain(fz, f => f.Stunde >= zurueck.Von);
        Assert.All(spaeter.Where(k => k.Bis < zurueck.Von), k => Assert.Contains(fz, f => f.Stunde == k.Von));

        // in der ersten Stunde doch anwesend: die ganze Abwesenheit des Tages entfällt
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/loeschen", new { datum = erste.Datum, stunde = erste.Von, unterricht = erste.Unterricht, schuelerIds = new[] { sid } }));
        Assert.Empty(kb.FehlzeitenVon(sid, Heute, Heute));

        // Vorgabe Stundenende (Standard): nur die Stunde selbst
        Setzen(s, e => e with { AbwesenheitEnde = "stunde" });
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = erste.Datum, stunde = erste.Von, unterricht = erste.Unterricht, eintraege = new[] { new { schuelerId = sid, art = "fehlt", minuten = 0 } } }));
        Assert.All(kb.FehlzeitenVon(sid, Heute, Heute), f => Assert.InRange(f.Stunde, erste.Von, erste.Bis));
    }

    [Fact]
    public async Task Abwesenheit_nur_waehrend_der_Stunde_fuer_Fachlehrkraefte()
    {
        await using var s = await Testserver.StartenAsync();
        var k = KarteVon(s, Heute, Elementtyp.Lehrer, "SNE");
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        Setzen(s, e => e with { AbwesenheitNurInStunde = true });
        var (beginn, _) = Klassenbuchdienst.StundenZeit(s.Dienst<Paketdienst>().Aktuell!, k.Von);
        object Daten() => new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = new[] { new { schuelerId = sid, art = "fehlt", minuten = 0 } } };
        s.Dienst<Uhr>().Fest = Heute.ToDateTime(beginn).AddMinutes(-30);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", Daten())).StatusCode);
        // die reine Abwesenheitskontrolle bleibt möglich
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = Array.Empty<object>() }));
        s.Dienst<Uhr>().Fest = Heute.ToDateTime(beginn).AddMinutes(5);
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", Daten()));
    }

    [Fact]
    public async Task Offene_Stunden_folgen_Vorgabe_und_Kriterium()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var k = KarteVon(s, new DateOnly(2026, 10, 12), Elementtyp.Lehrer, "SNE");
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Ballspiele" }));
        var vorher = await Testserver.HolenAsync(c, "/api/app/klassenbuch/offene-stunden");
        Assert.Equal("woche", vorher["bereich"]!.GetValue<string>());
        var offenVorher = vorher["offen"]!.GetValue<int>();

        // nur fehlender Lehrstoff zählt: die Stunde mit Lehrstoff ist nicht mehr offen
        Setzen(s, e => e with { OffenFuer = "lehrstoff", OffenAb = "schuljahr" });
        var r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/offene-stunden");
        Assert.Equal(("schuljahr", "lehrstoff"), (r["bereich"]!.GetValue<string>(), r["offenFuer"]!.GetValue<string>()));
        Assert.DoesNotContain(r["zeilen"]!.AsArray(), z => z!["datum"]!.GetValue<string>() == k.Datum && z["von"]!.GetValue<int>() == k.Von);

        // ohne Abwesenheitskontrolle gilt jede Stunde als kontrolliert
        Setzen(s, e => e with { OffenFuer = "beide", OffenAb = "woche", Abwesenheitskontrolle = false });
        r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/offene-stunden?art=alle");
        Assert.All(r["zeilen"]!.AsArray(), z => Assert.True(z!["geprueft"]!.GetValue<bool>()));
        Assert.Equal(offenVorher - 1, r["offen"]!.GetValue<int>());

        // Vorgabe „Tage zurück“ als individueller Zeitraum
        Setzen(s, e => e with { Abwesenheitskontrolle = true, OffenAb = "tage", OffenTage = 3 });
        r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/offene-stunden");
        Assert.Equal(("individuell", "2026-10-11"), (r["bereich"]!.GetValue<string>(), r["von"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Pruefungen_Datumsbereich_Raumkonflikt_und_Uebernahme()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        object Klausur(string raum, string datum = "2026-10-16") => new { datum, stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, raeume = new[] { raum }, aufsicht = new[] { "MUE" }, titel = "Deutsch GK" };

        // belegter Raum: abgelehnt, mit „Raumkonflikt zulassen“ erlaubt
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/app/meldungen/klausur", Klausur("R106"))).StatusCode);
        Setzen(s, e => e with { Raumkonflikt = true, PruefungBis = "2026-10-15" });
        // Datumsbereich für Prüfungseingaben
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/app/meldungen/klausur", Klausur("R106"))).StatusCode);
        Setzen(s, e => e with { PruefungBis = "" });
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Klausur("R106")));

        // Übernahme nach UntPlan nur bis 1 Tag in die Zukunft: die Prüfung am Freitag wird zurückgehalten
        string Pfad() => $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen";
        Assert.Contains((await Testserver.HolenAsync(s.SyncClient(), Pfad())).AsArray(), x => x!["typ"]!.GetValue<string>() == "klausur");
        Setzen(s, e => e with { UebernahmeTage = 1 });
        Assert.DoesNotContain((await Testserver.HolenAsync(s.SyncClient(), Pfad())).AsArray(), x => x!["typ"]!.GetValue<string>() == "klausur");
        Setzen(s, e => e with { UebernahmeTage = null, NurVertretungsrelevant = true });
        Assert.Contains((await Testserver.HolenAsync(s.SyncClient(), Pfad())).AsArray(), x => x!["typ"]!.GetValue<string>() == "klausur");
    }

    [Fact]
    public async Task Erinnerung_an_offene_Stunden_mit_Link_und_automatische_Entschuldigung()
    {
        await using var s = await Testserver.StartenAsync();
        await s.AlsAsync("sne", "lehrer", "SNE");
        var sne = s.Dienst<Benutzerdienst>().NachLogin("sne")!;
        Setzen(s, e => e with { Erinnerung = true, ErinnerungTag = Klassenbucherinnerung.Wochentag(Heute), OffenFuer = "lehrstoff" });
        var r = s.Dienst<Klassenbucherinnerung>().Senden();
        Assert.True(r.Lehrkraefte >= 1 && r.Stunden >= 1);
        var m = Assert.Single(s.Dienst<Mitteilungsdienst>().Posteingang(sne.Id), x => x.Betreff.StartsWith("Erinnerung"));
        Assert.Contains("](#/plan/lehrer/SNE/2026-10-", m.Text);
        Assert.Contains("fehlt: Lehrstoff", m.Text);
        Assert.DoesNotContain("Abwesenheitskontrolle\n", m.Text);

        // Hintergrunddienst: am eingestellten Wochentag nur einmal
        var dienst = s.Dienst<Klassenbucherinnerung>();
        dienst.Faellige();
        dienst.Faellige();
        Assert.Equal(2, s.Dienst<Mitteilungsdienst>().Posteingang(sne.Id).Count(x => x.Betreff.StartsWith("Erinnerung")));

        // automatische Entschuldigung offener Abwesenheiten nach 1 Tag
        var admin = await s.AlsAsync("admin");
        var montag = s.Dienst<Plandienst>().Karten(s.Dienst<Paketdienst>().Aktuell!, new DateOnly(2026, 10, 12), Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer");
        var anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum={montag.Datum}&stunde={montag.Von}&unterricht={montag.Unterricht}");
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = montag.Datum, stunde = montag.Von, unterricht = montag.Unterricht, eintraege = new[] { new { schuelerId = sid, art = "fehlt", minuten = 0 } } }));
        Setzen(s, e => e with { AutoTage = 1 });
        Assert.True(dienst.AutomatischEntschuldigen() >= 1);
        Assert.All(s.Dienst<Klassenbuchdienst>().FehlzeitenVon(sid, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)), f => Assert.Equal("entschuldigt", f.Status));
    }

    [Fact]
    public async Task Erinnerungstext_listet_hoechstens_vierzig_Stunden()
    {
        var zeilen = Enumerable.Range(1, 45).Select(i => new Stundenzeile("SNE", new() { "7a" }, "D", "Deutsch", "", "2026-10-12", 1, 2, "08:00", "09:30", "", "regulaer", new(), false, false, 7)).ToList();
        var text = Klassenbucherinnerung.Text("SNE", zeilen, new KlassenbuchEinstellungen());
        Assert.Equal(40, text.Split('\n').Count(z => z.StartsWith("- [")));
        Assert.Contains("… und 5 weitere", text);
        Assert.Contains("1.–2. Std.", text);
        Assert.Contains("fehlt: Lehrstoff, Abwesenheitskontrolle", text);
        await Task.CompletedTask;
    }
}
