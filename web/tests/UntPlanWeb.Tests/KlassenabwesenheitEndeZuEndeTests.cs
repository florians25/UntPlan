using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenabsenz bei einer Stunde mit mehreren Klassen Ende zu Ende (wie im Desktop angelegt → Vertretungen.Aktualisieren → echter
/// Export → UntPlanWeb → /api/app/plan): Fehlt nur ein Teil der Klassen, bleibt die Stunde im Plan der Lehrkraft, der übrigen Klassen und
/// im Raumplan (grün, abwesende Klasse durchgestrichen im Label); im Plan der abwesenden Klasse ist sie nicht regulär. Fehlen alle Klassen,
/// entfällt die Stunde für alle.
/// </summary>
public class KlassenabwesenheitEndeZuEndeTests
{
    private readonly ITestOutputHelper _aus;
    public KlassenabwesenheitEndeZuEndeTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Donnerstag = new(2026, 10, 15);
    private const int Stunde = 3;

    public enum Form { EinUnterricht, Kopplung, KopplungEineLehrkraft, KopplungGleicheLehrkraft, Mitklassen }

    /// <summary>Beispielschule mit eigener Stunde (Klassen X1, X2, Lehrkräfte XA, XB, Raum XR) Donnerstag 3. Stunde.</summary>
    private static Schuldatei Schule(Form form)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var fach = d.Faecher.First(f => f.Kurzname == "INF" || f.Kurzname == "IF" || f.Kurzname == "D");
        var x1 = new Klasse { Id = d.NeueId(), Kurzname = "X1" };
        var x2 = new Klasse { Id = d.NeueId(), Kurzname = "X2" };
        var xa = new Lehrer { Id = d.NeueId(), Kurzname = "XA" };
        var xb = new Lehrer { Id = d.NeueId(), Kurzname = "XB" };
        var xr = new Raum { Id = d.NeueId(), Kurzname = "XR" };
        d.Klassen.AddRange(new[] { x1, x2 });
        d.Lehrer.AddRange(new[] { xa, xb });
        d.Raeume.Add(xr);
        var tag = (int)Donnerstag.DayOfWeek - 1;
        void Neu(params Kopplungszeile[] zeilen)
        {
            var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = zeilen.ToList() };
            d.Unterrichte.Add(u);
            d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = Stunde, Raeume = zeilen.Select(_ => (int?)xr.Id).ToList() });
        }
        Kopplungszeile Z(Lehrer l, params Klasse[] k) => new() { LehrerId = l.Id, FachId = fach.Id, KlassenIds = k.Select(x => x.Id).ToList(), FachraumId = xr.Id };
        switch (form)
        {
            case Form.EinUnterricht: Neu(Z(xa, x1, x2)); break;
            case Form.Kopplung: Neu(Z(xa, x1), Z(xb, x2)); break;
            case Form.KopplungEineLehrkraft: Neu(Z(xa, x1), new Kopplungszeile { LehrerId = null, FachId = fach.Id, KlassenIds = { x2.Id }, FachraumId = xr.Id }); break;
            case Form.KopplungGleicheLehrkraft: Neu(Z(xa, x1), Z(xa, x2)); break;
            case Form.Mitklassen: Neu(Z(xa, x1)); Neu(Z(xa, x2)); break;
        }
        return d;
    }

    private static async Task HochladenAsync(Testserver s, Schuldatei d)
    {
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Donnerstag.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
    }

    private async Task<List<JsonNode>> KartenAsync(Testserver s, string typ, string kurz)
    {
        var c = await s.AlsAsync("admin");
        var iso = Donnerstag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso
            && k["von"]!.GetValue<int>() <= Stunde && k["bis"]!.GetValue<int>() >= Stunde).ToList();
        foreach (var k in karten) _aus.WriteLine($"{typ} {kurz}: {k.ToJsonString()}");
        return karten;
    }

    /// <summary>Durchgestrichene Klassen einer Karte: abwesende Klassen der Stunde bzw. fehlende Mitklassen.</summary>
    private static List<string> Gestrichen(JsonNode k) =>
        (k["abwesendeKlassen"]?.AsArray().Select(x => x!.GetValue<string>()) ?? Enumerable.Empty<string>())
        .Concat(k["mitklassen"]?.AsArray().Where(w => w!["k"]!.GetValue<string>().Length == 0).Select(w => w!["alt"]!.GetValue<string>()) ?? Enumerable.Empty<string>())
        .Distinct().ToList();

    private static void Abwesenheit(Schuldatei d, Klasse k, bool veranstaltung)
    {
        if (veranstaltung)
            d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Donnerstag, Bis = Donnerstag, StundeVon = 1, StundeBis = 6, KlassenIds = { k.Id }, Text = "Wandertag " + k.Kurzname });
        else
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = k.Id, Von = Donnerstag, Bis = Donnerstag, Text = "Exkursion" });
    }

    [Theory]
    [InlineData(Form.EinUnterricht, false)]
    [InlineData(Form.Kopplung, false)]
    [InlineData(Form.KopplungEineLehrkraft, false)]
    [InlineData(Form.KopplungGleicheLehrkraft, false)]
    [InlineData(Form.Mitklassen, false)]
    [InlineData(Form.EinUnterricht, true)]
    [InlineData(Form.Kopplung, true)]
    [InlineData(Form.KopplungGleicheLehrkraft, true)]
    [InlineData(Form.Mitklassen, true)]
    public async Task Eine_Klasse_abwesend_Stunde_bleibt_fuer_alle_anderen(Form form, bool veranstaltung)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule(form);
        Abwesenheit(d, d.Klassen.First(k => k.Kurzname == "X2"), veranstaltung);
        Vertretungen.Aktualisieren(d, Donnerstag);
        // Desktop: die Stunde findet für X1 statt – keine Lehrkraft wird freigesetzt, kein Entfall
        foreach (var v in d.Vertretungen) _aus.WriteLine($"Zeile: {v.Art} U{v.UnterrichtId}/{v.ZeilenIndex} L{v.LehrerId} K[{string.Join(",", v.KlassenIds)}]");
        Assert.DoesNotContain(d.Vertretungen, v => v.UnterrichtId.HasValue);
        Assert.All(Tagesplan.Berechnen(d, Donnerstag).Stunde(Stunde).Where(e => e.Unterricht != null), e => Assert.Equal(TagesStatus.Regulaer, e.Status));
        await HochladenAsync(s, d);
        var elemente = new List<(string, string)> { ("lehrer", "XA"), ("raum", "XR"), ("klasse", "X1") };
        if (form == Form.Kopplung) elemente.Add(("lehrer", "XB"));
        foreach (var (typ, kurz) in elemente)
        {
            // Lehrer-, Raum- und Klassenplan der anwesenden Klasse: eine grüne Karte, X2 durchgestrichen
            var k = Assert.Single(await KartenAsync(s, typ, kurz), x => x["unterricht"] is not null);
            Assert.Equal("geaendert", k["status"]!.GetValue<string>());
            Assert.Equal(new[] { "X2" }, Gestrichen(k));
        }
        // Plan der abwesenden Klasse: Eckklammern „abwesend“ (wie bei abwesender Lehrkraft), kein Entfall
        var eigen = Assert.Single(await KartenAsync(s, "klasse", "X2"), x => x["unterricht"] is not null);
        Assert.Equal("abwesend", eigen["status"]!.GetValue<string>());
        Assert.True(eigen["klasseAbwesend"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(Form.EinUnterricht, false)]
    [InlineData(Form.Kopplung, false)]
    [InlineData(Form.KopplungEineLehrkraft, false)]
    [InlineData(Form.KopplungGleicheLehrkraft, false)]
    [InlineData(Form.Mitklassen, false)]
    [InlineData(Form.EinUnterricht, true)]
    [InlineData(Form.Kopplung, true)]
    [InlineData(Form.Mitklassen, true)]
    public async Task Alle_Klassen_abwesend_Entfall_fuer_alle(Form form, bool veranstaltung)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule(form);
        foreach (var k in d.Klassen) Abwesenheit(d, k, veranstaltung);
        Vertretungen.Aktualisieren(d, Donnerstag);
        // Desktop: jede Zeile wird freigesetzt (die Lehrkräfte sind frei)
        var tp = Tagesplan.Berechnen(d, Donnerstag);
        Assert.All(tp.Stunde(Stunde).Where(e => e.Unterricht != null), e => Assert.Equal(TagesStatus.Entfall, e.Status));
        Assert.Contains(d.Vertretungen, v => v.Art == VertretungsArt.Freisetzung && d.Lehrer.Any(l => l.Kurzname == "XA" && l.Id == v.LehrerId));
        await HochladenAsync(s, d);
        var elemente = new List<(string, string)> { ("lehrer", "XA"), ("raum", "XR"), ("klasse", "X1"), ("klasse", "X2") };
        if (form == Form.Kopplung) elemente.Add(("lehrer", "XB"));
        foreach (var (typ, kurz) in elemente)
        {
            var karten = (await KartenAsync(s, typ, kurz)).Where(x => x["unterricht"] is not null).ToList();
            Assert.NotEmpty(karten);
            Assert.All(karten, k => Assert.Equal("entfall", k["status"]!.GetValue<string>()));
        }
    }
}
