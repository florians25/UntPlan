using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Tests;

/// <summary>
/// Schülergruppen Ende zu Ende: im Desktop automatisch angelegt („Fach_Klassen_Lehrkraft“) → echter Export → UntPlanWeb → /api/app/plan:
/// jede Karte trägt die Schülergruppe (Kopf der Stundendetails „Klasse | Schülergruppe | Fach“).
/// </summary>
public class SchuelergruppenWebTests
{
    private static readonly DateOnly Donnerstag = new(2026, 10, 15);
    private static int Tag(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    /// <summary>Beispielschule ohne Stundenplan; die beiden Unterrichte liegen Donnerstag in der 1. bzw. 2. Stunde.</summary>
    private static (Schuldatei D, Unterricht U, Unterricht W) Schule()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan.Clear();
        var einzeln = d.Unterrichte.Where(x => x.Zeilen.Count == 1 && x.KlassenIds.Count() == 1 && x.LehrerIds.Any() && x.HauptfachId.HasValue).ToList();
        var u = einzeln.First();
        var w = einzeln.First(x => x.KlassenIds.First() == u.KlassenIds.First() && x.HauptfachId != u.HauptfachId && x.LehrerIds.First() != u.LehrerIds.First());
        d.AktivePeriode.Plan.Add(new Planstunde { UnterrichtId = u.Id, Tag = Tag(Donnerstag), Stunde = 1, Raeume = { null } });
        d.AktivePeriode.Plan.Add(new Planstunde { UnterrichtId = w.Id, Tag = Tag(Donnerstag), Stunde = 2, Raeume = { null } });
        Assert.True(Schuelergruppen.Ergaenzen(d) > 0);
        return (d, u, w);
    }

    private static async Task HochladenAsync(Testserver s, Schuldatei d)
    {
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Donnerstag.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
    }

    private static async Task<List<JsonNode>> KartenAsync(Testserver s, string klasse, int stunde)
    {
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=klasse&element={Uri.EscapeDataString(klasse)}&datum={Donnerstag:yyyy-MM-dd}");
        return plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == $"{Donnerstag:yyyy-MM-dd}"
            && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde).ToList();
    }

    [Fact]
    public async Task Karte_traegt_die_Schuelergruppe_des_Unterrichts()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var (d, u, _) = Schule();
        var ix = new DatenIndex(d);
        var name = $"{ix.KFach(u.HauptfachId)}_{ix.KKlassen(u.KlassenIds).Replace(" ", "")}_{ix.KLehrer(u.LehrerIds.First())}";
        Assert.Equal(name, u.Schuelergruppe);
        await HochladenAsync(s, d);
        var k = Assert.Single(await KartenAsync(s, ix.K(ElementTyp.Klasse, u.KlassenIds.First()), 1));
        Assert.Equal(u.Id, k["unterricht"]!.GetValue<int>());
        Assert.Equal(name, k["gruppe"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fremdvertretung_mit_Fach_und_Schuelergruppe_eines_anderen_Unterrichts_ist_dessen_zusaetzliche_Stunde()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var (d, u, w) = Schule();
        var ix = new DatenIndex(d);
        // Lehrkraft von W übernimmt die 1. Stunde und hält W statt U (Fach und Schülergruppe von W)
        d.Vertretungen.Add(new Vertretung
        {
            Id = d.NeueId(), Datum = Donnerstag, Stunde = 1, UnterrichtId = u.Id, ZeilenIndex = 0, LehrerId = u.LehrerIds.First(), FachId = u.HauptfachId,
            VertretungsfachId = w.HauptfachId, KlassenIds = u.KlassenIds.ToList(), Art = VertretungsArt.Vertretung, Quelle = VertretungsQuelle.Manuell,
            VertreterId = w.LehrerIds.First(), Schuelergruppe = w.Schuelergruppe, Bearbeitet = true,
        });
        await HochladenAsync(s, d);
        var karten = await KartenAsync(s, ix.K(ElementTyp.Klasse, u.KlassenIds.First()), 1);
        var neu = Assert.Single(karten, k => k["fach"]!.GetValue<string>() == ix.KFach(w.HauptfachId));
        Assert.Equal(w.Schuelergruppe, neu["gruppe"]!.GetValue<string>());
        Assert.Equal(w.Id, neu["unterricht"]!.GetValue<int>());
        // die ursprüngliche Stunde entfällt und nennt ihre eigene Schülergruppe
        var alt = Assert.Single(karten, k => k["fach"]!.GetValue<string>() == ix.KFach(u.HauptfachId));
        Assert.Equal("entfall", alt["status"]!.GetValue<string>());
        Assert.Equal(u.Schuelergruppe, alt["gruppe"]!.GetValue<string>());
    }
}
