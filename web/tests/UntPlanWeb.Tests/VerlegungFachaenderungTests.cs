using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fall aus echten Daten: Die Doppelstunde DEU von OU (1.–2. Stunde) wird in die 4.–5. Stunde verlegt; am frei gewordenen Ursprung wird
/// in UntPlan das Fach geändert (GID, ohne Lehrkraft bzw. noch offen). Die neue Stunde am Ursprung ist eine Zusatzstunde: Symbole „+“ und
/// Änderung (grünes Quadrat) – nicht das rote Verlegungssymbol, denn verlegt wurde die ursprüngliche Stunde. Daneben bleibt die
/// ursprüngliche Stunde als Entfall (rotes Dreieck), die verlegte Stunde am Ziel behält ihren grünen Verlegungspfeil. Ende zu Ende
/// (Core → echter Export → UntPlanWeb → /api/app/plan).
/// </summary>
public class VerlegungFachaenderungTests
{
    private readonly ITestOutputHelper _aus;
    public VerlegungFachaenderungTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private const string Iso = "2026-11-06";

    private static List<string> Marker(JsonNode k) => k["marker"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fachaenderung_am_Ursprung_einer_Verlegung_ist_Zusatzstunde_mit_Plus_und_Aenderung(bool offen)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.NeueDatei("Testschule", "GY");
        var deu = new Fach { Id = d.NeueId(), Kurzname = "DEU" };
        var gid = new Fach { Id = d.NeueId(), Kurzname = "GID" };
        d.Faecher.AddRange(new[] { deu, gid });
        var klasse = new Klasse { Id = d.NeueId(), Kurzname = "K1" };
        d.Klassen.Add(klasse);
        var ou = new Lehrer { Id = d.NeueId(), Kurzname = "OU" };
        d.Lehrer.Add(ou);
        var raum = new Raum { Id = d.NeueId(), Kurzname = "D004" };
        d.Raeume.Add(raum);
        var tag = (int)Freitag.DayOfWeek - 1;
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { LehrerId = ou.Id, FachId = deu.Id, KlassenIds = { klasse.Id }, FachraumId = raum.Id } } };
        d.Unterrichte.Add(u);
        foreach (var st in new[] { 1, 2 }) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = st, Raeume = { raum.Id } });
        Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Freitag, 1, u.Id, null, 2, Freitag, 4), VerlegeModus.Verlegen);
        // am Ursprung (offene Vertretung „???“): Fach geändert (Spalte „Fach“ im Vertretungsfenster), ggf. ohne Lehrkraft („+++“)
        foreach (var q in d.Vertretungen.Where(Vertretungen.IstOffenerUrsprung).ToList())
        {
            if (!offen) Vertretungen.OhneLehrer(q);
            q.VertretungsfachId = gid.Id;
            q.Bearbeitet = true;
        }
        foreach (var v in d.Vertretungen) _aus.WriteLine($"Zeile: {v.Art} U{v.UnterrichtId} {v.Stunde} Fach {v.FachId}/{v.VertretungsfachId} nach {v.NachDatum}/{v.NachStunde}");
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);

        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=klasse&element=K1&datum={Iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == Iso).ToList();
        foreach (var k in karten) _aus.WriteLine(k.ToJsonString());

        // neue Stunde am Ursprung: GID, Zusatzstunde, keine Verlegung
        var neu = karten.Where(k => k["fach"]!.GetValue<string>() == "GID").ToList();
        Assert.NotEmpty(neu);
        Assert.All(neu, k =>
        {
            Assert.Equal("DEU", k["fachAlt"]!.GetValue<string>());
            Assert.True(k["zusaetzlich"]!.GetValue<bool>());
            Assert.Null(k["verlegung"]?.GetValue<string>());
            Assert.Null(k["verlegungVon"]);
            Assert.Null(k["verlegungNach"]);
            Assert.DoesNotContain("verlegungVon", Marker(k));
            // „+“ (Zusatzstunde) und Änderung; offene Vertretung (Lehrkraft noch offen): Änderung
            Assert.Equal(offen ? new[] { "aenderung" } : new[] { "plus", "aenderung" }, Marker(k));
        });
        Assert.Equal(1, neu.Min(k => k["von"]!.GetValue<int>()));
        Assert.Equal(2, neu.Max(k => k["bis"]!.GetValue<int>()));
        // ursprüngliche Stunde: Entfall (rotes Dreieck)
        var entfall = karten.Where(k => k["status"]!.GetValue<string>() == "entfall").ToList();
        Assert.NotEmpty(entfall);
        Assert.All(entfall, k => { Assert.Equal("DEU", k["fach"]!.GetValue<string>()); Assert.Equal(new[] { "entfall" }, Marker(k)); });
        // verlegte Stunde am Ziel: grüner Verlegungspfeil
        var ziel = karten.Where(k => k["von"]!.GetValue<int>() >= 4).ToList();
        Assert.NotEmpty(ziel);
        Assert.All(ziel, k => { Assert.Equal("nach", k["verlegung"]!.GetValue<string>()); Assert.Equal(new[] { "verlegungNach" }, Marker(k)); });
    }
}
