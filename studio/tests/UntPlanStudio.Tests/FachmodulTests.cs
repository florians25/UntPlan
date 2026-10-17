using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Model;
using Xunit.Abstractions;

namespace UntPlanStudio.Tests;

/// <summary>Stammdaten (CRUD), Unterricht und Stundenplan über die API.</summary>
public sealed class FachmodulTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;
    private readonly ITestOutputHelper _aus;

    public FachmodulTests(StudioFabrik f, ITestOutputHelper aus)
    {
        _f = f;
        _aus = aus;
    }

    private static int Spalte(JsonNode tabelle, string schluessel) =>
        tabelle["felder"]!.AsArray().Select((x, i) => (x, i)).First(t => t.x!["schluessel"]!.GetValue<string>() == schluessel).i;

    private static string Wert(JsonNode zeile, int i) => zeile["werte"]![i]!.GetValue<string>();

    [Fact]
    public async Task StammdatenAnlegenAendernLoeschen()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var basis = $"/api/dokumente/{id}/stammdaten/lehrer";

        var tabelle = await c.GetJsonAsync(basis);
        Assert.Equal("Lehrkräfte", tabelle["titel"]!.GetValue<string>());
        var anzahl = tabelle["zeilen"]!.AsArray().Count;
        var nachname = Spalte(tabelle, "Langname");
        var faecher = Spalte(tabelle, "Faecher");
        var soll = Spalte(tabelle, "Wochensoll");

        // Anlegen mit Werten
        var neu = await c.PostJsonAsync(basis, new { kurzname = "TST", werte = new Dictionary<string, string> { ["Langname"] = "Tester", ["Faecher"] = "D,M" } });
        var lid = neu["id"]!.GetValue<int>();
        Assert.Equal("Tester", Wert(neu, nachname));
        Assert.Equal("D,M", Wert(neu, faecher));

        // doppelter Kurzname
        var doppelt = await c.PostAsJsonAsync(basis, new { kurzname = "tst" });
        Assert.Equal(HttpStatusCode.BadRequest, doppelt.StatusCode);
        Assert.Contains("bereits vergeben", await doppelt.Content.ReadAsStringAsync());

        // Ändern einzeln (Dezimal im deutschen Format) und Umbenennen
        var geaendert = await c.PatchJsonAsync($"{basis}/{lid}", new { feld = "Wochensoll", wert = "12,5" });
        Assert.Equal("12,5", Wert(geaendert, soll));
        var umbenannt = await c.PatchJsonAsync($"{basis}/{lid}", new { feld = "Kurzname", wert = "TS2" });
        Assert.Equal("TS2", Wert(umbenannt, 0));

        // ungültige Eingaben wie im Desktop abgewiesen
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/{lid}", new { feld = "Hohlstunden", wert = "x-y" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/{lid}", new { feld = "Faecher", wert = "GIBTSNICHT" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/{lid}", new { feld = "Soll", wert = "3" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/dokumente/{id}/stammdaten/gibtsnicht")).StatusCode);

        // mehrere Werte in einer Rückgängig-Stufe
        var zeilen = await c.PostJsonAsync($"{basis}/aendern", new { aenderungen = new[] { new { id = lid, feld = "Langname", wert = "Mehrfach" }, new { id = lid, feld = "Vorname", wert = "Max" } } });
        Assert.Equal("Mehrfach", Wert(zeilen[0]!, nachname));
        Assert.Contains("2 Werte", (await c.GetJsonAsync($"/api/dokumente/{id}"))["rueckgaengigText"]!.GetValue<string>());

        // Auswahllisten enthalten Kurz- und Langname
        var elemente = await c.GetJsonAsync($"/api/dokumente/{id}/elemente?typen=lehrer");
        Assert.Contains(elemente["lehrer"]!.AsArray(), e => e!["k"]!.GetValue<string>() == "TS2" && e["l"]!.GetValue<string>() == "Mehrfach, Max");

        // Löschen und Rückgängig
        var verwendet = await c.GetJsonAsync($"{basis}/verwendung?ids={lid}");
        Assert.Equal(0, verwendet["unterrichte"]!.GetValue<int>());
        await c.PostJsonAsync($"{basis}/loeschen", new { ids = new[] { lid } });
        Assert.Equal(anzahl, (await c.GetJsonAsync(basis))["zeilen"]!.AsArray().Count);
        await c.PostJsonAsync($"/api/dokumente/{id}/rueckgaengig");
        Assert.Equal(anzahl + 1, (await c.GetJsonAsync(basis))["zeilen"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("klasse", "Klassen")]
    [InlineData("raum", "Räume")]
    [InlineData("fach", "Fächer")]
    [InlineData("raumgruppe", "Raumgruppen")]
    [InlineData("gang", "Gänge")]
    [InlineData("abteilung", "Abteilungen")]
    [InlineData("abwesenheitsgrund", "Abwesenheitsgründe")]
    [InlineData("schueler", "Schüler")]
    public async Task AlleStammdatentypenSindLesbarUndAnlegbar(string typ, string titel)
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var t = await c.GetJsonAsync($"/api/dokumente/{id}/stammdaten/{typ}");
        Assert.Equal(titel, t["titel"]!.GetValue<string>());
        Assert.NotEmpty(t["felder"]!.AsArray());
        var neu = await c.PostJsonAsync($"/api/dokumente/{id}/stammdaten/{typ}", new { kurzname = "Neu1" });
        Assert.Equal("Neu1", neu["werte"]![0]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/dokumente/{id}/stammdaten/{typ}/{neu["id"]!.GetValue<int>()}")).StatusCode);
    }

    [Fact]
    public async Task UnterrichtAnlegenKoppelnUndLoeschen()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var elemente = await c.GetJsonAsync($"/api/dokumente/{id}/elemente");
        var klasse = elemente["klasse"]![0]!;
        var kid = klasse["id"]!.GetValue<int>();
        var basis = $"/api/dokumente/{id}/unterricht";

        var vorher = await c.GetJsonAsync($"{basis}?sicht=klasse&element={kid}");
        Assert.StartsWith($"Klasse {klasse["k"]!.GetValue<string>()}", vorher["titel"]!.GetValue<string>());
        var spalten = vorher["spalten"]!.AsArray().Select(s => s!["schluessel"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "UNr", "KlLe", "Nvpl", "Wst", "Jst", "Fach", "Lehrer", "Klassen", "Fachraum", "Stammraum" }, spalten.Take(10));
        var anzahl = vorher["unterrichte"]!.AsArray().Count;

        // Neuer Unterricht aus der Klassensicht: Klasse wird vorbelegt, Fachraum aus dem Fach übernommen
        var u = await c.PostJsonAsync(basis, new { sicht = "klasse", element = kid, werte = new Dictionary<string, string> { ["Fach"] = "PH", ["Lehrer"] = "KRU", ["Wst"] = "2" } });
        var uid = u["id"]!.GetValue<int>();
        var z0 = u["zeilen"]![0]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Equal("2", z0[spalten.IndexOf("Wst")]);
        Assert.Equal(klasse["k"]!.GetValue<string>(), z0[spalten.IndexOf("Klassen")]);
        Assert.Equal("PH1", z0[spalten.IndexOf("Fachraum")]);
        Assert.Equal(2, u["nvpl"]!.GetValue<int>());

        // Kopplungszeile: weitere Lehrkraft
        var gekoppelt = await c.PostJsonAsync($"{basis}/{uid}/zeilen");
        Assert.Equal(2, gekoppelt["zeilen"]!.AsArray().Count);
        var mitLehrer = await c.PatchJsonAsync($"{basis}/{uid}", new { feld = "Lehrer", wert = "MEY", zeile = 1 });
        Assert.Equal("MEY", mitLehrer["zeilen"]![1]![spalten.IndexOf("Lehrer")]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/{uid}", new { feld = "Lehrer", wert = "NIEMAND", zeile = 1 })).StatusCode);
        var entkoppelt = await (await c.DeleteAsync($"{basis}/{uid}/zeilen/1")).JsonAsync();
        Assert.Single(entkoppelt["zeilen"]!.AsArray());

        var kopie = await c.PostJsonAsync($"{basis}/{uid}/kopieren");
        Assert.NotEqual(uid, kopie["id"]!.GetValue<int>());
        Assert.Equal(anzahl + 2, (await c.GetJsonAsync($"{basis}?sicht=klasse&element={kid}"))["unterrichte"]!.AsArray().Count);
        await c.PostJsonAsync($"{basis}/loeschen", new { ids = new[] { uid, kopie["id"]!.GetValue<int>() } });
        Assert.Equal(anzahl, (await c.GetJsonAsync($"{basis}?sicht=klasse&element={kid}"))["unterrichte"]!.AsArray().Count);
        Assert.True((await c.GetJsonAsync(basis))["unterrichte"]!.AsArray().Count > anzahl);
    }

    [Fact]
    public async Task StundenplanAbrufenUndManuellPlanen()
    {
        var c = await _f.AngemeldetAsync();
        var id = (await c.PostJsonAsync("/api/dokumente/beispiel"))["id"]!.GetValue<string>();
        var elemente = await c.GetJsonAsync($"/api/dokumente/{id}/elemente");
        var kid = elemente["klasse"]![0]!["id"]!.GetValue<int>();

        var plan = await c.GetJsonAsync($"/api/dokumente/{id}/stundenplan/klasse/{kid}");
        Assert.Equal(5, plan["zeitraster"]!["tage"]!.AsArray().Count);
        var zellen = plan["zellen"]!.AsArray();
        Assert.NotEmpty(zellen);
        var eintrag = zellen[0]!["eintraege"]![0]!;
        Assert.False(string.IsNullOrEmpty(eintrag["fach"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(eintrag["lehrer"]!.GetValue<string>()));
        // gekoppelter Religionsunterricht erscheint nebeneinander (zwei Einträge in einem Feld)
        Assert.Contains(zellen, z => z!["eintraege"]!.AsArray().Count == 2 && z["eintraege"]!.AsArray().All(e => e!["gekoppelt"]!.GetValue<bool>()));

        foreach (var typ in new[] { "lehrer", "raum", "fach" })
        {
            var eid = elemente[typ]![0]!["id"]!.GetValue<int>();
            var p = await c.GetJsonAsync($"/api/dokumente/{id}/stundenplan/{typ}/{eid}");
            Assert.Equal(typ, p["element"]!["typ"]!.GetValue<string>());
        }

        // Zielstatus beim Ziehen und Entplanen/Verplanen einer Stunde
        var u = eintrag["u"]!.GetValue<int>();
        var tag = zellen[0]!["tag"]!.GetValue<int>();
        var stunde = zellen[0]!["stunde"]!.GetValue<int>();
        var status = await c.GetJsonAsync($"/api/dokumente/{id}/stundenplan/zielstatus?unterricht={u}&tag={tag}&stunde={stunde}");
        Assert.Equal(5, status.AsArray().Count);
        Assert.Equal(10, status[0]!.AsArray().Count);

        var vorher = (await c.GetJsonAsync($"/api/dokumente/{id}"))["zaehler"]!["planstunden"]!.GetValue<int>();
        await c.PostJsonAsync($"/api/dokumente/{id}/stundenplan/entplanen", new { unterricht = u, tag, stunde });
        var nachEntplanen = await c.GetJsonAsync($"/api/dokumente/{id}/stundenplan/klasse/{kid}");
        Assert.Contains(nachEntplanen["nichtVerplant"]!.AsArray(), n => n!["u"]!.GetValue<int>() == u);
        await c.PostJsonAsync($"/api/dokumente/{id}/stundenplan/verplanen", new { unterricht = u, tag, stunde });
        Assert.Equal(vorher, (await c.GetJsonAsync($"/api/dokumente/{id}"))["zaehler"]!["planstunden"]!.GetValue<int>());

        // Fixieren verhindert Verschieben
        await c.PostJsonAsync($"/api/dokumente/{id}/stundenplan/fixieren", new { unterricht = u, tag, stunde, fixieren = true });
        var r = await c.PostAsJsonAsync($"/api/dokumente/{id}/stundenplan/verschieben", new { unterricht = u, tag, stunde, zielTag = tag, zielStunde = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>Große Schule (150 Lehrkräfte, 80 Klassen, 3200 Unterrichte): Tabellen und Pläne bleiben schnell.</summary>
    [Fact]
    public async Task GrosseSchuleBleibtSchnell()
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
        var sw = Stopwatch.StartNew();
        var id = await c.BeispielHochladenAsync(d);
        var hochladen = sw.ElapsedMilliseconds;
        sw.Restart();
        var alle = await c.GetJsonAsync($"/api/dokumente/{id}/unterricht");
        var unterricht = sw.ElapsedMilliseconds;
        Assert.Equal(3200, alle["unterrichte"]!.AsArray().Count);
        sw.Restart();
        var lehrer = await c.GetJsonAsync($"/api/dokumente/{id}/stammdaten/lehrer");
        var stammdaten = sw.ElapsedMilliseconds;
        Assert.Equal(150, lehrer["zeilen"]!.AsArray().Count);
        sw.Restart();
        var plan = await c.GetJsonAsync($"/api/dokumente/{id}/stundenplan/lehrer/{d.Lehrer[0].Id}");
        var stundenplan = sw.ElapsedMilliseconds;
        Assert.NotEmpty(plan["zellen"]!.AsArray());
        sw.Restart();
        await c.PatchJsonAsync($"/api/dokumente/{id}/stammdaten/lehrer/{d.Lehrer[0].Id}", new { feld = "Langname", wert = "Geändert" });
        var aendern = sw.ElapsedMilliseconds;
        _aus.WriteLine($"Hochladen {hochladen} ms, alle Unterrichte {unterricht} ms, Lehrkräfte {stammdaten} ms, Lehrerplan {stundenplan} ms, Wert ändern {aendern} ms");
        Assert.True(unterricht < 5000 && stammdaten < 2000 && stundenplan < 2000 && aendern < 5000, $"zu langsam: {unterricht}/{stammdaten}/{stundenplan}/{aendern} ms");
    }
}
