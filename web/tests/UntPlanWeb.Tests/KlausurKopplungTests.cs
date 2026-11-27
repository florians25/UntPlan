using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>
/// Prüfungen in Kopplungen gelten für die ganze Kopplung: Eine Kopplung (ein Unterricht, je Klasse eine Zeile, z. B. WW in ET13, INF13 und ING13
/// bei einer Lehrkraft) mit einer Prüfung nur für eine Klasse zeigt in allen Plänen die Prüfung, keine Zeile erscheint als abgegeben.
/// </summary>
public class KlausurKopplungTests
{
    private static readonly DateOnly Tag = new(2026, 10, 15);

    /// <summary>Kopplung M (WEB) in 6a, 6b und 7b in der 9. Stunde am 15.10. (eigene Räume je Klasse).</summary>
    private static JsonObject Paket(bool pruefungAusDemDesktop)
    {
        var p = Testserver.Beispielpaket();
        var u = JsonNode.Parse("""{"id": 900, "wochenstunden": 1, "zeilen": [{"fach": "M", "lehrer": "WEB", "klassen": ["6a"], "gruppe": ""}, {"fach": "M", "lehrer": "WEB", "klassen": ["6b"], "gruppe": ""}, {"fach": "M", "lehrer": "WEB", "klassen": ["7b"], "gruppe": ""}], "wochen": "", "kurs": "", "text": "Kopplung"}""")!;
        p["perioden"]![0]!["unterricht"]!.AsArray().Add(u);
        var stunden = p["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-15")!["stunden"]!.AsArray();
        string Zeile(string klasse, string raum, string status, string lehrer, string alt, string text, int? vertretung) =>
            $$"""{"stunde": 9, "unterricht": 900, "fach": "M", "klassen": ["{{klasse}}"], "lehrer": [{{lehrer}}], "lehrerAlt": [{{alt}}], "raeume": ["{{raum}}"], "status": "{{status}}", "text": "{{text}}"{{(vertretung is int v ? $", \"vertretung\": {v}" : "")}}}""";
        if (!pruefungAusDemDesktop)
        {
            stunden.Add(JsonNode.Parse(Zeile("6a", "R103", "regulaer", "\"WEB\"", "", "", null)));
            stunden.Add(JsonNode.Parse(Zeile("6b", "R104", "regulaer", "\"WEB\"", "", "", null)));
            stunden.Add(JsonNode.Parse(Zeile("7b", "R106", "regulaer", "\"WEB\"", "", "", null)));
        }
        else
        {
            // wie vom Desktop-Programm übertragen: Prüfung nur für 6b (Aufsicht WEB, eigener Raum), der Unterricht der 6b entfällt wegen der Prüfung,
            // 6a und 7b ohne Lehrkraft (abgegeben), weil WEB in der Prüfung ist
            p["vertretungen"]!.AsArray().Add(JsonNode.Parse("""{"id": 9001, "datum": "2026-10-15", "stunde": 9, "art": "Klausur", "klassen": ["6b"], "lehrer": "", "vertreter": "WEB", "fach": "", "neuesFach": "", "raum": "", "neuerRaum": "R105", "vertrVon": "", "nach": "", "text": "Klassenarbeit M", "nurLehrer": false}"""));
            stunden.Add(JsonNode.Parse("""{"stunde": 9, "fach": "", "klassen": ["6b"], "lehrer": ["WEB"], "raeume": ["R105"], "status": "zusaetzlich", "vertretung": 9001, "text": "Klassenarbeit M"}"""));
            stunden.Add(JsonNode.Parse(Zeile("6b", "R104", "entfall", "\"WEB\"", "", "Klausur Klassenarbeit M", null)));
            foreach (var (id, kl) in new[] { (9002, "6a"), (9003, "7b") })
                p["vertretungen"]!.AsArray().Add(JsonNode.Parse($$"""{"id": {{id}}, "datum": "2026-10-15", "stunde": 9, "art": "Vertretung", "klassen": ["{{kl}}"], "lehrer": "WEB", "vertreter": "???", "fach": "M", "neuesFach": "M", "raum": "", "neuerRaum": "", "vertrVon": "", "nach": "", "text": "", "nurLehrer": false}"""));
            stunden.Add(JsonNode.Parse(Zeile("6a", "R103", "offen", "", "\"WEB\"", "", 9002)));
            stunden.Add(JsonNode.Parse(Zeile("7b", "R106", "offen", "", "\"WEB\"", "", 9003)));
        }
        p["revision"] = 2;
        return p;
    }

    private static void AllePruefung(Testserver s)
    {
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var rm = s.Dienst<Rueckmeldungsdienst>();
        List<Karte> Karten(string typ, string kurz) => plan.Karten(st, Tag, typ, kurz, rm.Vorgemerkt(Tag, st));
        var lehrer = Karten(Elementtyp.Lehrer, "WEB").Where(k => k.Von <= 9 && k.Bis >= 9).ToList();
        Assert.NotEmpty(lehrer);
        Assert.All(lehrer, k => Assert.Equal("klausur", k.Status));
        Assert.DoesNotContain(lehrer, k => k.Status == "abwesend");
        foreach (var kl in new[] { "6a", "6b", "7b" })
        {
            var karten = Karten(Elementtyp.Klasse, kl).Where(k => k.Von <= 9 && k.Bis >= 9).ToList();
            Assert.True(karten.Count > 0 && karten.All(k => k.Status == "klausur"), $"{kl}: {string.Join(", ", karten.Select(k => k.Status))}");
        }
    }

    [Fact]
    public async Task Pruefung_vom_Desktop_fuer_einen_Teil_der_Kopplung_gilt_fuer_alle_Karten()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        Assert.True((await s.PaketSendenAsync(Paket(true))).IsSuccessStatusCode);
        AllePruefung(s);
    }

    [Fact]
    public async Task Pruefung_im_Web_angelegt_umfasst_alle_Klassen_der_Kopplung()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        Assert.True((await s.PaketSendenAsync(Paket(false))).IsSuccessStatusCode);
        var bd = s.Dienst<Benutzerdienst>();
        var web = bd.Anlegen("web", "test-passwort-1", Rollen.Lehrer, "WEB", "Web");
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var m = s.Dienst<Rueckmeldungsdienst>().Klausur(st, bd.Holen(web.Id)!, Tag, 9, 9, new[] { "6b" }, Array.Empty<string>(), new[] { "WEB" }, "Klassenarbeit M", "");
        Assert.Equal(new[] { "6a", "6b", "7b" }, m.Klassen.Order());
        AllePruefung(s);
    }

    [Fact]
    public async Task Vorhandene_Teilpruefung_wird_ganz_angezeigt_und_simuliert_nachgebessert()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        Assert.True((await s.PaketSendenAsync(Paket(false))).IsSuccessStatusCode);
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var sim = s.Dienst<Schulsimulation>();
        var db = s.Dienst<UntPlanWeb.Daten.Db>();
        // simulierte Prüfung, die (aus einer älteren Version) nur eine Klasse der Kopplung enthält
        var m = s.Dienst<Rueckmeldungsdienst>().Klausur(st, sim.LehrerKonto(st, "WEB"), Tag, 9, 9, new[] { "6b" }, Array.Empty<string>(), Array.Empty<string>(), "Klassenarbeit M", "");
        db.Ausfuehren("UPDATE rueckmeldungen SET klassen = @k WHERE id = @i", UntPlanWeb.Daten.Db.P("@k", "[\"6b\"]"), UntPlanWeb.Daten.Db.P("@i", m.Id));
        // Anzeige trotzdem für die ganze Kopplung
        AllePruefung(s);
        // Nachbessern erweitert die simulierte Prüfung auf die ganze Kopplung
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 14, 12, 0, 0);
        var nb = sim.Nachbessern(st, sim.Laden(), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16));
        Assert.Contains(nb.Zusammenfassung, x => x.StartsWith("Prüfungen auf die ganze Kopplung erweitert"));
        var klassen = (string)db.Wert("SELECT klassen FROM rueckmeldungen WHERE id = @i", UntPlanWeb.Daten.Db.P("@i", m.Id))!;
        Assert.Equal(new[] { "6a", "6b", "7b" }, System.Text.Json.JsonSerializer.Deserialize<List<string>>(klassen)!.Order());
        Assert.Equal(0, sim.Nachbessern(st, sim.Laden(), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16)).Korrigiert);
    }

    [Fact]
    public async Task Simulation_plant_je_Kopplung_eine_Pruefung()
    {
        var s = await Testserver.StartenAsync();
        await using var _ = s;
        // Kopplung Spanisch (WEB) in 6a, 6b und 7b, zweimal je Woche im Stundenplan
        var p = Testserver.Beispielpaket();
        var per = p["perioden"]![0]!;
        per["unterricht"]!.AsArray().Add(JsonNode.Parse("""{"id": 901, "wochenstunden": 2, "zeilen": [{"fach": "S", "lehrer": "WEB", "klassen": ["6a"], "gruppe": ""}, {"fach": "S", "lehrer": "WEB", "klassen": ["6b"], "gruppe": ""}, {"fach": "S", "lehrer": "WEB", "klassen": ["7b"], "gruppe": ""}], "wochen": "", "kurs": "", "text": "Kopplung"}"""));
        foreach (var (t, r) in new[] { (1, "R103"), (3, "R104") })
            per["stundenplan"]!.AsArray().Add(JsonNode.Parse($$"""{"unterricht": 901, "tag": {{t}}, "stunde": 9, "raeume": ["{{r}}"]}"""));
        p["revision"] = 2;
        Assert.True((await s.PaketSendenAsync(p)).IsSuccessStatusCode);
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var sim = s.Dienst<Schulsimulation>();
        var uhr = s.Dienst<Uhr>();
        uhr.Fest = new DateTime(2026, 9, 2, 6, 30, 0);
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        sim.Speichern(s.Dienst<Benutzerdienst>().NachLogin("admin")!, new Simulationseinstellungen { Aktiv = true });
        var e = sim.Laden();
        var start = DateTime.Parse(e.Start);
        foreach (var (hv, hb) in Schulsimulation.Halbjahre(st))
        {
            var plan = sim.Pruefungsplanung(st, e, sim.Welt(st, e, start, start.AddDays(5)), hv, hb, start);
            var kopplung = plan.Where(x => x.Unterricht == 901).ToList();
            Assert.True(kopplung.Count(x => !x.Test) <= 1, string.Join(", ", kopplung.Select(x => x.Klasse)));
            Assert.True(kopplung.Count(x => x.Test) <= 1);
            Assert.Equal(kopplung.Count, plan.Count(x => x.Fach == "S" && x.Klasse is "6a" or "6b" or "7b"));
            // eingetragen wird sie für alle Klassen der Kopplung
            foreach (var x in kopplung)
                Assert.Equal(new[] { "6a", "6b", "7b" }, s.Dienst<Rueckmeldungsdienst>().KopplungsKlassen(st, x.Datum, x.Von, x.Bis, new List<string> { x.Klasse }, new[] { x.Lehrer }).Order());
        }
        Assert.Contains(Schulsimulation.Halbjahre(st), h => sim.Pruefungsplanung(st, e, sim.Welt(st, e, start, start.AddDays(5)), h.Von, h.Bis, start).Any(x => x.Unterricht == 901 && !x.Test));
    }
}
