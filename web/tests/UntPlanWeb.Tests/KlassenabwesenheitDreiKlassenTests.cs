using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fall aus echten Daten: Freitag 5.–6. Stunde „ET13, INF13, ING13 / WW / B117“ bei HE (eine Karte), nur ET13 ist abwesend. Ende zu Ende
/// (Core → Vertretungen.Aktualisieren → echter Export → UntPlanWeb → /api/app/plan) in allen Formen (ein Unterricht mit drei Klassen,
/// Kopplung mit drei Zeilen – nur die erste mit Lehrkraft bzw. alle mit HE –, drei parallele Unterrichte) und mit einem Paket einer älteren
/// UntPlan-Version (Freisetzung der Zeile von ET13): Die Stunde läuft weiter – grüne Karte mit ET13 durchgestrichen, INF13 und ING13
/// schlicht; im Plan von ET13 „abwesend“ (Eckklammern), kein Entfall.
/// </summary>
public class KlassenabwesenheitDreiKlassenTests
{
    private readonly ITestOutputHelper _aus;
    public KlassenabwesenheitDreiKlassenTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private static readonly int[] Stunden = { 5, 6 };

    public enum Form { EinUnterricht, KopplungNurErsteZeileMitLehrkraft, KopplungAlleMitLehrkraft, Mitklassen }

    private static Schuldatei Schule(Form form)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW", Langname = "Wirtschaftswissenschaften" };
        d.Faecher.Add(ww);
        var klassen = new[] { "ET13", "INF13", "ING13" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var he = new Lehrer { Id = d.NeueId(), Kurzname = "HE" };
        var raum = new Raum { Id = d.NeueId(), Kurzname = "B117" };
        d.Klassen.AddRange(klassen);
        d.Lehrer.Add(he);
        d.Raeume.Add(raum);
        var tag = (int)Freitag.DayOfWeek - 1;
        void Neu(params Kopplungszeile[] zeilen)
        {
            var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = zeilen.ToList() };
            d.Unterrichte.Add(u);
            foreach (var st in Stunden)
                d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = st, Raeume = zeilen.Select(_ => (int?)raum.Id).ToList() });
        }
        Kopplungszeile Z(Lehrer? l, params Klasse[] k) => new() { LehrerId = l?.Id, FachId = ww.Id, KlassenIds = k.Select(x => x.Id).ToList(), FachraumId = raum.Id };
        switch (form)
        {
            case Form.EinUnterricht: Neu(Z(he, klassen)); break;
            case Form.KopplungNurErsteZeileMitLehrkraft: Neu(Z(he, klassen[0]), Z(null, klassen[1]), Z(null, klassen[2])); break;
            case Form.KopplungAlleMitLehrkraft: Neu(klassen.Select(k => Z(he, k)).ToArray()); break;
            case Form.Mitklassen: foreach (var k in klassen) Neu(Z(he, k)); break;
        }
        return d;
    }

    /// <summary>Wie ältere UntPlan-Versionen: Freisetzung der Zeile(n), deren eigene Klassen alle abwesend sind.</summary>
    private static void AlteFreisetzungen(Schuldatei d, Abwesenheit a)
    {
        foreach (var r in Tagesplan.Regulaer(d, Freitag).Where(r => r.KlassenIds.Count > 0 && r.KlassenIds.All(k => k == a.ElementId)))
            d.Vertretungen.Add(new Vertretung
            {
                Id = d.NeueId(), Datum = Freitag, Stunde = r.Stunde, UnterrichtId = r.Unterricht.Id, ZeilenIndex = r.ZeilenIndex, LehrerId = r.LehrerId, FachId = r.FachId,
                KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId, Art = VertretungsArt.Freisetzung, Quelle = VertretungsQuelle.Absenz, AbwesenheitId = a.Id,
                Grund = "Klasse abw.", Text = a.Text,
            });
    }

    private async Task<List<JsonNode>> KartenAsync(Testserver s, string typ, string kurz, int stunde)
    {
        var c = await s.AlsAsync("admin");
        var iso = Freitag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso && k["unterricht"] is not null
            && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde).ToList();
        foreach (var k in karten) _aus.WriteLine($"{typ} {kurz} {stunde}: {k.ToJsonString()}");
        return karten;
    }

    private static List<string> Kuerzel(JsonNode? liste, bool gestrichen) => liste?.AsArray()
        .Where(w => (w!["k"]!.GetValue<string>().Length == 0) == gestrichen)
        .Select(w => gestrichen ? w!["alt"]!.GetValue<string>() : w!["k"]!.GetValue<string>()).ToList() ?? new();

    [Theory]
    [InlineData(Form.EinUnterricht, false)]
    [InlineData(Form.KopplungNurErsteZeileMitLehrkraft, false)]
    [InlineData(Form.KopplungAlleMitLehrkraft, false)]
    [InlineData(Form.Mitklassen, false)]
    [InlineData(Form.EinUnterricht, true)]
    [InlineData(Form.KopplungNurErsteZeileMitLehrkraft, true)]
    [InlineData(Form.KopplungAlleMitLehrkraft, true)]
    [InlineData(Form.Mitklassen, true)]
    public async Task Nur_ET13_abwesend_Stunde_laeuft_weiter(Form form, bool altesPaket)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule(form);
        var et13 = d.Klassen.First(k => k.Kurzname == "ET13");
        var a = new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = et13.Id, Von = Freitag, Bis = Freitag, Text = "Exkursion" };
        d.Abwesenheiten.Add(a);
        Vertretungen.Aktualisieren(d, Freitag);
        // aktueller Desktop: keine Freisetzung – HE unterrichtet INF13 und ING13
        Assert.DoesNotContain(d.Vertretungen, v => v.UnterrichtId.HasValue);
        if (altesPaket) AlteFreisetzungen(d, a);
        foreach (var v in d.Vertretungen) _aus.WriteLine($"Zeile: {v.Art} U{v.UnterrichtId}/{v.ZeilenIndex} L{v.LehrerId} K[{string.Join(",", v.KlassenIds)}]");
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);

        foreach (var st in Stunden)
        {
            // Lehrkraft, Raum, anwesende Klassen: eine grüne Karte über die Doppelstunde, ET13 durchgestrichen, INF13 und ING13 schlicht
            foreach (var (typ, kurz) in new[] { ("lehrer", "HE"), ("raum", "B117"), ("klasse", "INF13"), ("klasse", "ING13") })
            {
                var k = Assert.Single(await KartenAsync(s, typ, kurz, st));
                Assert.Equal("geaendert", k["status"]!.GetValue<string>());
                Assert.Equal((5, 6), (k["von"]!.GetValue<int>(), k["bis"]!.GetValue<int>()));
                var abw = k["abwesendeKlassen"]!.AsArray().Select(x => x!.GetValue<string>()).Concat(Kuerzel(k["mitklassen"], true)).Distinct().ToList();
                Assert.Equal(new[] { "ET13" }, abw);
                var alle = Kuerzel(k["klassen"], false).Concat(Kuerzel(k["mitklassen"], false)).Order().ToList();
                if (typ == "klasse") Assert.Contains(kurz, alle);
                else Assert.Equal(new[] { "ET13", "INF13", "ING13" }, alle);
                Assert.Contains("aenderung", k["marker"]!.AsArray().Select(x => x!.GetValue<string>()));
            }
            // Plan von ET13: „abwesend“ wie bei abwesender Lehrkraft (Eckklammern), kein Entfall
            var eigen = Assert.Single(await KartenAsync(s, "klasse", "ET13", st));
            Assert.Equal("abwesend", eigen["status"]!.GetValue<string>());
            Assert.True(eigen["klasseAbwesend"]!.GetValue<bool>());
            Assert.DoesNotContain("entfall", eigen["marker"]!.AsArray().Select(x => x!.GetValue<string>()));
        }
    }

    [Theory]
    [InlineData(Form.EinUnterricht)]
    [InlineData(Form.KopplungNurErsteZeileMitLehrkraft)]
    [InlineData(Form.KopplungAlleMitLehrkraft)]
    [InlineData(Form.Mitklassen)]
    public async Task Alle_drei_abwesend_Entfall(Form form)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule(form);
        foreach (var k in d.Klassen)
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = k.Id, Von = Freitag, Bis = Freitag });
        Vertretungen.Aktualisieren(d, Freitag);
        Assert.Contains(d.Vertretungen, v => v.Art == VertretungsArt.Freisetzung);
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
        foreach (var (typ, kurz) in new[] { ("lehrer", "HE"), ("raum", "B117"), ("klasse", "ET13"), ("klasse", "INF13"), ("klasse", "ING13") })
        {
            var karten = await KartenAsync(s, typ, kurz, 5);
            Assert.NotEmpty(karten);
            Assert.All(karten, k => Assert.Equal("entfall", k["status"]!.GetValue<string>()));
        }
    }
}
