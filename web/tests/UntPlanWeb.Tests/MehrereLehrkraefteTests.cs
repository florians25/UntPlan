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
    public void Sondereinsatz_ohne_Fach_zeigt_im_eigenen_Plan_alle_Beteiligten()
    {
        // UntPlan legt je Lehrkraft eine eigene Zeile an, oft ohne Fach – zusammengehörig über Stunde, Klassen, Räume und Text
        WebStunde S(string l, string text = "Projektgruppe") => new() { Stunde = 2, Fach = "", Klassen = { "5a" }, Lehrer = { l }, Raeume = { "A101" }, Status = "zusaetzlich", Text = text };
        var s = StandMit(new[] { S("BAM"), S("BIK"), S("BRN"), S("CEL", "Elternabend") });
        var plan = new Plandienst();
        foreach (var kurz in new[] { "BAM", "BIK", "BRN" })
        {
            var k = Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, kurz));
            Assert.Equal(new[] { "BAM", "BIK", "BRN" }, k.Lehrer.Select(w => w.K).OrderBy(x => x));
        }
        // anderer Text: eigener Einsatz
        Assert.Equal(new[] { "CEL" }, Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, "CEL")).Lehrer.Select(w => w.K));
    }

    [Fact]
    public void Kopplung_zeigt_im_Lehrerplan_alle_Lehrkraefte()
    {
        WebStunde K(string l) => new() { Stunde = 1, Unterricht = 7, Fach = "ZKonf", Lehrer = { l }, Raeume = { "A101" }, Status = "regulaer" };
        var s = StandMit(new[] { K("BAM"), K("BIK"), K("BRN") });
        var k = Assert.Single(new Plandienst().Karten(s, Tag, Elementtyp.Lehrer, "BIK"));
        Assert.Equal(new[] { "BAM", "BIK", "BRN" }, k.Lehrer.Select(w => w.K).OrderBy(x => x));
    }

    // Konferenz als Kopplung (je Lehrkraft eine Zeile desselben Unterrichts): SN fehlt, CS – selbst Teilnehmer – vertritt SN
    private static Stand KonferenzMitVertretung()
    {
        WebStunde K(string l) => new() { Stunde = 1, Unterricht = 9, Fach = "ZKonf", Lehrer = { l }, Raeume = { "ZBesp" }, Status = "regulaer" };
        var paket = new WebPaket
        {
            Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(1, "1.", "08:00", "08:45") } },
            Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = { K("CS"), K("SZ"),
                new WebStunde { Stunde = 1, Unterricht = 9, Fach = "ZKonf", Lehrer = { "CS" }, Raeume = { "ZBesp" }, Status = "vertreten", Vertretung = 5 } } } },
            Vertretungen = { new WebVertretung { Id = 5, Datum = "2026-10-14", Stunde = 1, Art = "Vertretung", Lehrer = "SN", Vertreter = "CS", Fach = "ZKonf", Raum = "ZBesp", NeuerRaum = "ZBesp" } },
        };
        return new Stand(paket, "2026-10-14 07:00:00");
    }

    [Fact]
    public void Vertretende_Teilnehmerin_steht_nur_einmal_auf_der_Karte()
    {
        // vorher „CS, SZ [CS]“: CS schlicht (eigene Zeile) und als Vertretung für SN
        var k = Assert.Single(new Plandienst().Karten(KonferenzMitVertretung(), Tag, Elementtyp.Lehrer, "CS"));
        Assert.Equal(new[] { "CS", "SZ" }, k.Lehrer.Select(w => w.K));
        Assert.Equal("SN", k.Lehrer[0].Alt);
        Assert.Null(k.Lehrer[1].Alt);
    }

    [Fact]
    public void Abwesende_Lehrkraft_sieht_auf_ihrer_Karte_alle_Kollegen()
    {
        // vorher nur „[CS]“: die weiteren Lehrkräfte der Konferenz fehlten im Plan der abwesenden Lehrkraft
        var k = Assert.Single(new Plandienst().Karten(KonferenzMitVertretung(), Tag, Elementtyp.Lehrer, "SN"));
        Assert.Equal("abwesend", k.Status);
        Assert.True(k.LehrerAbwesend);
        Assert.Equal(new[] { "CS", "SZ" }, k.Lehrer.Select(w => w.K));
        Assert.Equal("SN", k.Lehrer[0].Alt);
        Assert.Null(k.Lehrer[1].Alt);
    }

    [Fact]
    public void Abwesende_Lehrkraft_einer_Zusatzstunde_sieht_alle_Kollegen()
    {
        var s = new Stand(new WebPaket
        {
            Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(2, "2.", "08:50", "09:35") } },
            Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = { Zusatz("BAM"), Zusatz("BIK"),
                new WebStunde { Stunde = 2, Fach = "ZKonf", Lehrer = { "CEL" }, Raeume = { "A101" }, Status = "zusaetzlich", Vertretung = 6 } } } },
            Vertretungen = { new WebVertretung { Id = 6, Datum = "2026-10-14", Stunde = 2, Art = "Vertretung", Lehrer = "BRN", Vertreter = "CEL", Fach = "ZKonf" } },
        }, "2026-10-14 07:00:00");
        var k = Assert.Single(new Plandienst().Karten(s, Tag, Elementtyp.Lehrer, "BRN"));
        Assert.Equal("abwesend", k.Status);
        Assert.Equal(new[] { "BAM", "BIK", "CEL" }, k.Lehrer.Select(w => w.K).OrderBy(x => x));
        Assert.Equal("BRN", k.Lehrer.Single(w => w.K == "CEL").Alt);
    }

    [Fact]
    public void Gekoppelte_Klassen_stehen_in_ihrer_Reihenfolge()
    {
        // je Klasse eine Zeile: auf der gemeinsamen Karte alphabetisch, nicht in der Reihenfolge der Zeilen
        WebStunde K(string kl) => new() { Stunde = 1, Unterricht = 3, Fach = "WW", Klassen = { kl }, Lehrer = { "HE" }, Raeume = { "B117" }, Status = "regulaer" };
        var s = StandMit(new[] { K("INF13"), K("ING13"), K("ET13") });
        var k = Assert.Single(new Plandienst().Karten(s, Tag, Elementtyp.Lehrer, "HE"));
        Assert.Equal(new[] { "ET13", "INF13", "ING13" }, k.Klassen.Select(w => w.K));
    }

    [Fact]
    public void Lehrer_einmal_bevorzugt_das_Label()
    {
        var l = Plandienst.LehrerEinmal(new[] { new Wert("CS"), new Wert("SZ"), new Wert("CS", "SN", true), new Wert("", "AB", true) });
        Assert.Equal(new[] { "CS", "SZ", "" }, l.Select(w => w.K));
        Assert.Equal("SN", l[0].Alt);
    }
}
