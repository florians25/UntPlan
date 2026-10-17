using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Model;
using Xunit.Abstractions;

namespace UntPlanStudio.Tests;

/// <summary>Module Optimierung (Hintergrundauftrag, Diagnose, Gewichtung), Planung (Raum, Tausch, Blöcke, Mehrfachauswahl) und Zeitwünsche.</summary>
public sealed class PlanungOptimierungTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;
    private readonly ITestOutputHelper _aus;

    public PlanungOptimierungTests(StudioFabrik f, ITestOutputHelper aus)
    {
        _f = f;
        _aus = aus;
    }

    private async Task<(HttpClient C, string Basis)> BeispielAsync()
    {
        var c = await _f.AngemeldetAsync();
        var id = (await c.PostJsonAsync("/api/dokumente/beispiel"))["id"]!.GetValue<string>();
        return (c, $"/api/dokumente/{id}");
    }

    private static async Task<JsonNode> WartenAsync(HttpClient c, string basis, int sekunden = 120)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var a = await c.GetJsonAsync($"{basis}/optimierung/auftrag");
            if (a["status"]!.GetValue<string>() != "laeuft") return a;
            Assert.True(sw.Elapsed.TotalSeconds < sekunden, "Optimierung endet nicht.");
            await Task.Delay(200);
        }
    }

    private static async Task<int> PlanstundenAsync(HttpClient c, string basis) =>
        (await c.GetJsonAsync(basis))["zaehler"]!["planstunden"]!.GetValue<int>();

    [Fact]
    public async Task OptimierungStartetEndetUndWirdUebernommen()
    {
        var (c, basis) = await BeispielAsync();
        Assert.Equal("keiner", (await c.GetJsonAsync($"{basis}/optimierung/auftrag"))["status"]!.GetValue<string>());
        await c.PostJsonAsync($"{basis}/planung/alle-entplanen", new { fixierteBehalten = false });
        Assert.Equal(0, await PlanstundenAsync(c, basis));

        var start = await c.PostJsonAsync($"{basis}/optimierung/start", new { optionen = new { strategie = "A", anzahlPlaene = 2, schritte = 1, seed = 7 } });
        Assert.Equal("laeuft", start["status"]!.GetValue<string>());
        // zweiter Start während des Laufs wird abgewiesen
        var doppelt = await c.PostAsJsonAsync($"{basis}/optimierung/start", new { optionen = new { strategie = "A" } });
        Assert.True(doppelt.StatusCode == HttpStatusCode.BadRequest || (await WartenAsync(c, basis))["status"]!.GetValue<string>() == "fertig");

        var fertig = await WartenAsync(c, basis);
        Assert.Equal("fertig", fertig["status"]!.GetValue<string>());
        var ergebnisse = fertig["ergebnisse"]!.AsArray();
        Assert.Equal(2, ergebnisse.Count);
        Assert.True(ergebnisse[0]!["kennzahlen"]!["bewertung"]!.GetValue<long>() <= ergebnisse[1]!["kennzahlen"]!["bewertung"]!.GetValue<long>());
        Assert.True(fertig["aktuell"]!["nichtVerplant"]!.GetValue<int>() > 0);
        var nummer = ergebnisse[0]!["nummer"]!.GetValue<int>();

        // Ergebnisvergleich: Klassenplan aus dem Ergebnis
        var kid = (await c.GetJsonAsync($"{basis}/elemente"))["klasse"]![0]!["id"]!.GetValue<int>();
        var vorschau = await c.GetJsonAsync($"{basis}/optimierung/auftrag/plan/{nummer}/klasse/{kid}");
        Assert.NotEmpty(vorschau["zellen"]!.AsArray());
        Assert.Equal(0, await PlanstundenAsync(c, basis)); // Vorschau ändert nichts

        var ueb = await c.PostJsonAsync($"{basis}/optimierung/auftrag/uebernehmen", new { nummer });
        Assert.True(ueb["planstunden"]!.GetValue<int>() > 0);
        Assert.Equal(ueb["planstunden"]!.GetValue<int>(), await PlanstundenAsync(c, basis));
        Assert.Equal("uebernommen", (await c.GetJsonAsync($"{basis}/optimierung/auftrag"))["status"]!.GetValue<string>());
        Assert.Contains("Optimierung", (await c.GetJsonAsync(basis))["rueckgaengigText"]!.GetValue<string>());

        // geändertes Dokument: Übernahme nur nach Bestätigung (409)
        await c.PostJsonAsync($"{basis}/planung/alle-fixieren", new { fixieren = false });
        var konflikt = await c.PostAsJsonAsync($"{basis}/optimierung/auftrag/uebernehmen", new { nummer });
        Assert.Equal(HttpStatusCode.Conflict, konflikt.StatusCode);
        await c.PostJsonAsync($"{basis}/optimierung/auftrag/uebernehmen", new { nummer, trotzAenderung = true });

        await (await c.DeleteAsync($"{basis}/optimierung/auftrag")).JsonAsync();
        Assert.Equal("keiner", (await c.GetJsonAsync($"{basis}/optimierung/auftrag"))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task OptimierungLaesstSichAbbrechen()
    {
        var (c, basis) = await BeispielAsync();
        await c.PostJsonAsync($"{basis}/optimierung/start", new { optionen = new { strategie = "E", anzahlPlaene = 1, schritte = 9 } });
        await Task.Delay(300);
        await c.PostJsonAsync($"{basis}/optimierung/auftrag/abbrechen");
        var a = await WartenAsync(c, basis, 60);
        Assert.Contains(a["status"]!.GetValue<string>(), new[] { "abgebrochen", "fertig" });
    }

    [Fact]
    public async Task DiagnoseUndGewichtungLiefernDaten()
    {
        var (c, basis) = await BeispielAsync();
        var d = await c.GetJsonAsync($"{basis}/optimierung/diagnose");
        Assert.NotEmpty(d["eingabedaten"]!.AsArray());
        Assert.NotNull(d["stundenplan"]);
        Assert.True(d["kennzahlen"]!["bewertung"]!.GetValue<long>() >= 0);
        var punkt = d["eingabedaten"]!.AsArray()[0]!;
        Assert.False(string.IsNullOrEmpty(punkt["titel"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(punkt["beschreibung"]!.GetValue<string>()));
        // nach dem Entplanen erscheinen nicht verplante Stunden in der Stundenplan-Diagnose
        await c.PostJsonAsync($"{basis}/planung/alle-entplanen", new { fixierteBehalten = false });
        var d2 = await c.GetJsonAsync($"{basis}/optimierung/diagnose?rubrik=stundenplan");
        Assert.Contains(d2["stundenplan"]!.AsArray(), p => p!["titel"]!.GetValue<string>() == "Nicht verplante Stunden" && p["faelle"]!.AsArray().Count > 0);

        var g = await c.GetJsonAsync($"{basis}/optimierung/gewichtung");
        var regler = g["regler"]!.AsArray();
        Assert.True(regler.Count > 20);
        await (await c.PutAsJsonAsync($"{basis}/optimierung/gewichtung", new { werte = new Dictionary<string, int> { ["K.Hohlstunden"] = 1 } })).JsonAsync();
        var g2 = await c.GetJsonAsync($"{basis}/optimierung/gewichtung");
        Assert.Equal(1, g2["regler"]!.AsArray().First(r => r!["schluessel"]!.GetValue<string>() == "K.Hohlstunden")!["wert"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{basis}/optimierung/gewichtung", new { werte = new Dictionary<string, int> { ["Gibts.Nicht"] = 1 } })).StatusCode);
        var analyse = await c.GetJsonAsync($"{basis}/optimierung/gewichtung/analyse");
        Assert.Equal(regler.Count, analyse.AsArray().Count);
    }

    [Fact]
    public async Task ZeitwunschSetzenUndLoeschen()
    {
        var (c, basis) = await BeispielAsync();
        var lehrer = await c.GetJsonAsync($"{basis}/zeitwuensche/lehrer");
        var lid = lehrer[0]!["id"]!.GetValue<int>();
        var z = await (await c.PutAsJsonAsync($"{basis}/zeitwuensche/lehrer/{lid}", new { aenderungen = new[] { new { tag = 0, stunde = 1, wert = -3 }, new { tag = 0, stunde = 2, wert = 2 } } })).JsonAsync();
        Assert.Equal(-3, z["werte"]![0]![0]!.GetValue<int>());
        Assert.Equal(2, z["werte"]![0]![1]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{basis}/zeitwuensche/lehrer/{lid}", new { aenderungen = new[] { new { tag = 0, stunde = 1, wert = 5 } } })).StatusCode);
        // unbestimmte Zeitwünsche
        var u = await (await c.PutAsJsonAsync($"{basis}/zeitwuensche/lehrer/{lid}/unbestimmte", new { liste = new[] { new { bereich = "Tage", anzahl = 1, wunsch = -3 } } })).JsonAsync();
        Assert.Single(u["unbestimmte"]!.AsArray());
        // Unterrichtszeitwünsche
        var uid = (await c.GetJsonAsync($"{basis}/zeitwuensche/unterricht"))[0]!["id"]!.GetValue<int>();
        var uz = await (await c.PutAsJsonAsync($"{basis}/zeitwuensche/unterricht/{uid}", new { aenderungen = new[] { new { tag = 1, stunde = 3, wert = 1 } } })).JsonAsync();
        Assert.Equal(1, uz["werte"]![1]![2]!.GetValue<int>());
        Assert.Null(uz["unbestimmte"]);
        var geloescht = await (await c.PutAsJsonAsync($"{basis}/zeitwuensche/lehrer/{lid}", new { alleLoeschen = true })).JsonAsync();
        Assert.Equal(0, geloescht["anzahl"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{basis}/zeitwuensche/gang/1")).StatusCode);
    }

    private static async Task<(int U, int Tag, int Stunde, int Klasse)> ErsteStundeAsync(HttpClient c, string basis)
    {
        var kid = (await c.GetJsonAsync($"{basis}/elemente"))["klasse"]![0]!["id"]!.GetValue<int>();
        var plan = await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}");
        var zelle = plan["zellen"]!.AsArray().First(z => z!["eintraege"]!.AsArray().Count == 1 && !z["eintraege"]![0]!["fix"]!.GetValue<bool>())!;
        return (zelle["eintraege"]![0]!["u"]!.GetValue<int>(), zelle["tag"]!.GetValue<int>(), zelle["stunde"]!.GetValue<int>(), kid);
    }

    [Fact]
    public async Task RaumZuordnenUndLoeschen()
    {
        var (c, basis) = await BeispielAsync();
        var (u, tag, stunde, _) = await ErsteStundeAsync(c, basis);
        var dlg = await c.GetJsonAsync($"{basis}/planung/raum?unterricht={u}&tag={tag}&stunde={stunde}&umfang=0");
        Assert.NotEmpty(dlg["raeume"]!.AsArray());
        Assert.NotEmpty(dlg["zeilen"]!.AsArray());
        var frei = dlg["raeume"]!.AsArray().First(r => !r!["besetzt"]!.GetValue<bool>() && r["gesperrt"]!.GetValue<int>() == 0)!;
        var rid = frei["id"]!.GetValue<int>();
        var nach = await c.PostJsonAsync($"{basis}/planung/raum", new { unterricht = u, tag, stunde, zeile = 0, umfang = 0, raum = rid });
        Assert.Equal(frei["kurz"]!.GetValue<string>(), nach["stunden"]![0]!["raum"]!.GetValue<string>());
        // belegter Raum: Rückfrage mit Optionen, dann verdrängen
        var belegt = nach["raeume"]!.AsArray().FirstOrDefault(r => r!["besetzt"]!.GetValue<bool>());
        if (belegt != null)
        {
            var f = await c.PostJsonAsync($"{basis}/planung/raum", new { unterricht = u, tag, stunde, zeile = 0, umfang = 0, raum = belegt["id"]!.GetValue<int>() });
            Assert.False(f["ok"]!.GetValue<bool>());
            Assert.Equal(3, f["optionen"]!.AsArray().Count);
            var v = await c.PostJsonAsync($"{basis}/planung/raum", new { unterricht = u, tag, stunde, zeile = 0, umfang = 0, raum = belegt["id"]!.GetValue<int>(), loesung = "verdraengen" });
            Assert.Equal(belegt["kurz"]!.GetValue<string>(), v["stunden"]![0]!["raum"]!.GetValue<string>());
        }
        var leer = await c.PostJsonAsync($"{basis}/planung/raum", new { unterricht = u, tag, stunde, zeile = 0, umfang = 0, raum = (int?)null });
        Assert.Equal("", leer["stunden"]![0]!["raum"]!.GetValue<string>());
    }

    [Fact]
    public async Task TauschvorschlaegeUndTausch()
    {
        var (c, basis) = await BeispielAsync();
        var (u, tag, stunde, kid) = await ErsteStundeAsync(c, basis);
        var liste = (await c.GetJsonAsync($"{basis}/planung/tausch?unterricht={u}&tag={tag}&stunde={stunde}")).AsArray();
        Assert.NotEmpty(liste);
        var v = liste[0]!;
        await c.PostJsonAsync($"{basis}/planung/tauschen", new { a = v["a"], b = v["b"] });
        var plan = await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}");
        var bTag = v["b"]!["tag"]!.GetValue<int>();
        var bStunde = v["b"]!["stunde"]!.GetValue<int>();
        Assert.Contains(plan["zellen"]!.AsArray(), z => z!["tag"]!.GetValue<int>() == bTag && z["stunde"]!.GetValue<int>() == bStunde && z["eintraege"]!.AsArray().Any(e => e!["u"]!.GetValue<int>() == u));
    }

    [Fact]
    public async Task ZielstatusVerschiebenMehrfachauswahl()
    {
        var (c, basis) = await BeispielAsync();
        var (u, tag, stunde, _) = await ErsteStundeAsync(c, basis);
        var z = await c.GetJsonAsync($"{basis}/planung/zielstatus?unterricht={u}&tag={tag}&stunde={stunde}");
        Assert.Equal(5, z["status"]!.AsArray().Count);
        Assert.Equal("aktuelle Position", z["gruende"]![tag]![stunde - 1]!.GetValue<string>());
        // irgendein Feld mit Kollision hat einen Grund
        var status = z["status"]!.AsArray();
        var gruende = z["gruende"]!.AsArray();
        Assert.Contains(Enumerable.Range(0, 5).SelectMany(t => Enumerable.Range(0, status[t]!.AsArray().Count).Select(s => (t, s))),
            p => status[p.t]![p.s]!.GetValue<string>() == "Kollision" && gruende[p.t]![p.s]!.GetValue<string>().Length > 0);

        // Verschieben auf ein freies Feld (oder Tausch), sonst Rückfrage und mit „entplanen“ ausführen
        var ziel = Enumerable.Range(0, 5).SelectMany(t => Enumerable.Range(0, status[t]!.AsArray().Count).Select(s => (t, s)))
            .First(p => status[p.t]![p.s]!.GetValue<string>() is "Frei" or "TauschMoeglich" or "VorgabeVerletzt" or "KeinRaum");
        var erg = await c.PostJsonAsync($"{basis}/planung/verschieben", new { unterricht = u, tag, stunde, zielTag = ziel.t, zielStunde = ziel.s + 1 });
        Assert.True(erg["ok"]!.GetValue<bool>());
        var kollision = Enumerable.Range(0, 5).SelectMany(t => Enumerable.Range(0, status[t]!.AsArray().Count).Select(s => (t, s)))
            .FirstOrDefault(p => status[p.t]![p.s]!.GetValue<string>() == "Kollision" && !gruende[p.t]![p.s]!.GetValue<string>().Contains("fixiert") && gruende[p.t]![p.s]!.GetValue<string>().StartsWith("Kollision"));
        await c.PostJsonAsync($"{basis}/rueckgaengig");

        // Mehrfachauswahl: zwei Stunden entplanen und fixieren in je einer Rückgängig-Stufe
        var kid = (await c.GetJsonAsync($"{basis}/elemente"))["klasse"]![0]!["id"]!.GetValue<int>();
        var zellen = (await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}"))["zellen"]!.AsArray().Where(x => x!["eintraege"]!.AsArray().Count == 1).Take(2).ToList();
        var stunden = zellen.Select(x => new { unterricht = x!["eintraege"]![0]!["u"]!.GetValue<int>(), tag = x["tag"]!.GetValue<int>(), stunde = x["stunde"]!.GetValue<int>() }).ToArray();
        await c.PostJsonAsync($"{basis}/planung/fixieren", new { stunden, fixieren = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"{basis}/planung/entplanen", new { stunden })).StatusCode);
        var vorher = await PlanstundenAsync(c, basis);
        await c.PostJsonAsync($"{basis}/planung/entplanen", new { stunden, auchFixierte = true });
        Assert.Equal(vorher - 2, await PlanstundenAsync(c, basis));
        await c.PostJsonAsync($"{basis}/rueckgaengig");
        Assert.Equal(vorher, await PlanstundenAsync(c, basis));

        // Rückfrage beim Verschieben auf eine Kollision
        await c.PostJsonAsync($"{basis}/planung/alle-fixieren", new { fixieren = false });
        if (kollision != default)
        {
            var frage = await c.PostJsonAsync($"{basis}/planung/verschieben", new { unterricht = u, tag, stunde, zielTag = kollision.t, zielStunde = kollision.s + 1, einzeln = true });
            if (frage["ok"]!.GetValue<bool>() == false)
            {
                Assert.Contains("behindernder Unterricht", frage["frage"]!.GetValue<string>());
                var mit = await c.PostJsonAsync($"{basis}/planung/verschieben", new { unterricht = u, tag, stunde, zielTag = kollision.t, zielStunde = kollision.s + 1, einzeln = true, aktion = "entplanen" });
                Assert.True(mit["ok"]!.GetValue<bool>());
            }
        }

        // alle entplanen behält fixierte
        await c.PostJsonAsync($"{basis}/planung/alle-fixieren", new { fixieren = false });
        var frisch = (await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}"))["zellen"]!.AsArray().First(x => x!["eintraege"]!.AsArray().Count == 1)!;
        await c.PostJsonAsync($"{basis}/planung/fixieren", new { stunden = new[] { new { unterricht = frisch["eintraege"]![0]!["u"]!.GetValue<int>(), tag = frisch["tag"]!.GetValue<int>(), stunde = frisch["stunde"]!.GetValue<int>() } }, fixieren = true });
        var e = await c.PostJsonAsync($"{basis}/planung/alle-entplanen", new { fixierteBehalten = true });
        Assert.Equal(1, e["verbleibend"]!.GetValue<int>());

        // Block setzen: nicht verplanter Unterricht mit 2 Stunden als Doppelstunde
        var nv = (await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}"))["nichtVerplant"]!.AsArray().First(n => n!["anzahl"]!.GetValue<int>() >= 2)!;
        var nu = nv["u"]!.GetValue<int>();
        var zs = await c.GetJsonAsync($"{basis}/planung/zielstatus?unterricht={nu}&laenge=2");
        Assert.Equal(2, zs["laenge"]!.GetValue<int>());
        var gesetzt = await c.PostJsonAsync($"{basis}/planung/verplanen", new { unterricht = nu, tag = 2, stunde = 3, laenge = 2, aktion = "entplanen" });
        Assert.True(gesetzt["ok"]!.GetValue<bool>());
        var plan = await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}");
        Assert.Equal(2, plan["zellen"]!.AsArray().Count(x => x!["tag"]!.GetValue<int>() == 2 && x["eintraege"]!.AsArray().Any(en => en!["u"]!.GetValue<int>() == nu)));
        // ganzen Block verschieben
        var b = await c.PostJsonAsync($"{basis}/planung/verschieben", new { unterricht = nu, tag = 2, stunde = 4, zielTag = 2, zielStunde = 6, aktion = "entplanen" });
        Assert.True(b["ok"]!.GetValue<bool>());
        plan = await c.GetJsonAsync($"{basis}/stundenplan/klasse/{kid}");
        var st = plan["zellen"]!.AsArray().Where(x => x!["tag"]!.GetValue<int>() == 2 && x["eintraege"]!.AsArray().Any(en => en!["u"]!.GetValue<int>() == nu)).Select(x => x!["stunde"]!.GetValue<int>()).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 5, 6 }, st);
    }

    /// <summary>Große Schule: Zielstatus, Raumliste, Diagnose und Tauschvorschläge bleiben benutzbar schnell.</summary>
    [Fact]
    public async Task GrosseSchulePlanungBleibtSchnell()
    {
        var d = Demo.NeueDatei("Große Schule", "GY");
        for (var i = 0; i < 150; i++) d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = $"L{i:000}", Langname = $"Lehrkraft {i:000}", Vorname = "V" });
        for (var i = 0; i < 80; i++)
        {
            var raum = new Raum { Id = d.NeueId(), Kurzname = $"R{i:000}" };
            d.Raeume.Add(raum);
            d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = $"K{i:00}", StammraumId = raum.Id });
        }
        var faecher = d.Faecher.Where(f => !f.Bereitschaft).Take(10).ToList();
        for (var k = 0; k < 80; k++)
            for (var j = 0; j < 40; j++)
            {
                var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = d.Lehrer[(k + j) % 150].Id, FachId = faecher[j % 10].Id, KlassenIds = { d.Klassen[k].Id } } } };
                d.Unterrichte.Add(u);
                d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = j % 5, Stunde = 1 + (j / 5) % 8, Raeume = { d.Klassen[k].StammraumId } });
            }
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync(d);
        var basis = $"/api/dokumente/{id}";
        var p = d.Stundenplan[0];
        var sw = Stopwatch.StartNew();
        await c.GetJsonAsync($"{basis}/planung/zielstatus?unterricht={p.UnterrichtId}&tag={p.Tag}&stunde={p.Stunde}");
        var ziel = sw.ElapsedMilliseconds;
        sw.Restart();
        await c.GetJsonAsync($"{basis}/planung/raum?unterricht={p.UnterrichtId}&tag={p.Tag}&stunde={p.Stunde}");
        var raumMs = sw.ElapsedMilliseconds;
        sw.Restart();
        await c.GetJsonAsync($"{basis}/zeitwuensche/lehrer/{d.Lehrer[0].Id}");
        var zw = sw.ElapsedMilliseconds;
        sw.Restart();
        await c.GetJsonAsync($"{basis}/optimierung/diagnose");
        var diag = sw.ElapsedMilliseconds;
        sw.Restart();
        await c.GetJsonAsync($"{basis}/planung/tausch?unterricht={p.UnterrichtId}&tag={p.Tag}&stunde={p.Stunde}");
        var tausch = sw.ElapsedMilliseconds;
        _aus.WriteLine($"Zielstatus {ziel} ms, Raumliste {raumMs} ms, Zeitwünsche {zw} ms, Diagnose {diag} ms, Tauschvorschläge {tausch} ms");
        Assert.True(ziel < 3000 && raumMs < 3000 && zw < 2000 && diag < 15000 && tausch < 30000, $"zu langsam: {ziel}/{raumMs}/{zw}/{diag}/{tausch} ms");
    }
}
