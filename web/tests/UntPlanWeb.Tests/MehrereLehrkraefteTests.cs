using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Karten mit mehreren Lehrkräften (Konferenz, Besprechung, Zusatzstunde, Kopplung): im Plan jeder beteiligten Lehrkraft stehen
/// alle Beteiligten auf einer gemeinsamen Karte – nicht nur die eigene Lehrkraft.
/// </summary>
public class MehrereLehrkraefteTests
{
    private static readonly DateOnly Tag = new(2026, 10, 14);

    private static Stand StandMit(IEnumerable<WebStunde> stunden) => new(new WebPaket
    {
        Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(1, "1.", "08:00", "08:45"), new WebStundenzeit(2, "2.", "08:50", "09:35") } },
        Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = stunden.ToList() } },
    }, "2026-10-14 07:00:00");

    private static WebStunde Zusatz(string lehrer, string fach = "ZKonf", string raum = "A101", int stunde = 2) => new()
    {
        Stunde = stunde, Fach = fach, Lehrer = { lehrer }, Raeume = { raum }, Status = "zusaetzlich",
    };

    [Fact]
    public void Zusatzstunde_mehrerer_Lehrkraefte_zeigt_im_eigenen_Plan_alle_Beteiligten()
    {
        var s = StandMit(new[] { Zusatz("BAM"), Zusatz("BIK"), Zusatz("BRN") });
        var plan = new Plandienst();
        foreach (var kurz in new[] { "BAM", "BIK", "BRN" })
        {
            var k = Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, kurz));
            Assert.True(k.Zusaetzlich);
            Assert.Equal(new[] { "BAM", "BIK", "BRN" }, k.Lehrer.Select(w => w.K).OrderBy(x => x));
        }
        // Raumplan: ebenfalls eine gemeinsame Karte statt einer je Lehrkraft
        var r = Assert.Single(plan.Karten(s, Tag, Elementtyp.Raum, "A101"));
        Assert.Equal(3, r.Lehrer.Count);
    }

    [Fact]
    public void Andere_Zusatzstunde_zur_selben_Zeit_bleibt_eine_eigene_Karte()
    {
        // anderes Fach bzw. anderer Raum: keine gemeinsame Karte, im Plan von BAM nur die eigene Stunde
        var s = StandMit(new[] { Zusatz("BAM"), Zusatz("BIK", fach: "ZBesp"), Zusatz("BRN", raum: "A102") });
        var plan = new Plandienst();
        var k = Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, "BAM"));
        Assert.Equal(new[] { "BAM" }, k.Lehrer.Select(w => w.K));
    }

    [Fact]
    public void Kopplung_zeigt_im_Lehrerplan_alle_Lehrkraefte()
    {
        WebStunde K(string l) => new() { Stunde = 1, Unterricht = 7, Fach = "ZKonf", Lehrer = { l }, Raeume = { "A101" }, Status = "regulaer" };
        var s = StandMit(new[] { K("BAM"), K("BIK"), K("BRN") });
        var k = Assert.Single(new Plandienst().Karten(s, Tag, Elementtyp.Lehrer, "BIK"));
        Assert.Equal(new[] { "BAM", "BIK", "BRN" }, k.Lehrer.Select(w => w.K).OrderBy(x => x));
    }
}
