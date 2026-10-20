using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stunde mit mehreren Klassen bzw. mehreren Lehrkräften, eine davon abwesend: Die Stunde findet für die übrigen statt. In deren Plänen
/// (und im Raumplan) eine grüne Änderungskarte mit der abwesenden Klasse bzw. Lehrkraft durchgestrichen im Label; im Plan der abwesenden
/// Klasse bzw. Lehrkraft selbst die Darstellung „abwesend“ (Eck-Klammern) statt Entfall.
/// </summary>
public class AbwesenheitKopplungTests
{
    private static readonly DateOnly Tag = new(2026, 10, 14);

    private static Stand StandMit(IEnumerable<WebStunde> stunden, IEnumerable<WebAbsenz>? absenzen = null, IEnumerable<WebVertretung>? vertretungen = null) => new(new WebPaket
    {
        Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(1, "1.", "08:00", "08:45"), new WebStundenzeit(2, "2.", "08:50", "09:35") } },
        Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = stunden.ToList() } },
        Absenzen = absenzen?.ToList() ?? new(),
        Vertretungen = vertretungen?.ToList() ?? new(),
    }, "2026-10-14 07:00:00");

    private static WebAbsenz Absenz(string art, string element) => new() { Art = art, Element = element, Von = "2026-10-14", Bis = "2026-10-14", StundeVon = 1, StundeBis = 8 };

    [Fact]
    public void Eine_von_zwei_Klassen_einer_Stunde_abwesend()
    {
        // ein Unterricht mit INF12 und INF13; INF13 ist abwesend
        var s = StandMit(new[] { new WebStunde { Stunde = 1, Unterricht = 3, Fach = "INF", Klassen = { "INF12", "INF13" }, Lehrer = { "MUE" }, Raeume = { "EDV1" }, Status = "regulaer" } },
            new[] { Absenz("klasse", "INF13") });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Lehrer, "MUE"), (Elementtyp.Raum, "EDV1"), (Elementtyp.Klasse, "INF12") })
        {
            var k = Assert.Single(plan.Karten(s, Tag, typ, kurz));
            // grüne Änderung, INF13 durchgestrichen im Label (abwesendeKlassen); die Klassenliste bleibt vollständig (Klassenbuch)
            Assert.Equal("geaendert", k.Status);
            Assert.False(k.KlasseAbwesend);
            Assert.Equal(new[] { "INF13" }, k.AbwesendeKlassen);
            Assert.Equal(new[] { "INF12", "INF13" }, k.Klassen.Select(w => w.K));
            Assert.Equal(new[] { "aenderung" }, k.Marker);
        }
        // Plan von INF13: die Stunde läuft ohne die Klasse weiter – wie „Lehrkraft abwesend“, kein Entfall
        var eigen = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF13"));
        Assert.Equal("abwesend", eigen.Status);
        Assert.True(eigen.KlasseAbwesend);
        Assert.False(eigen.LehrerAbwesend);
        Assert.Empty(eigen.Marker);
        Assert.Null(eigen.Verlegung);
        Assert.Equal(new[] { "INF13" }, eigen.AbwesendeKlassen);
    }

    [Fact]
    public void Abwesende_Koppelklasse_im_Lehrer_und_Raumplan_gruen()
    {
        // parallel identischer Unterricht (zwei Unterrichte, gleiche Lehrkraft, gleiches Fach, gleicher Raum)
        WebStunde S(int u, string klasse) => new() { Stunde = 1, Unterricht = u, Fach = "INF", Klassen = { klasse }, Lehrer = { "MUE" }, Raeume = { "EDV1" }, Status = "regulaer" };
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13") }, new[] { Absenz("klasse", "INF13") });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Lehrer, "MUE"), (Elementtyp.Raum, "EDV1") })
        {
            var k = Assert.Single(plan.Karten(s, Tag, typ, kurz));
            Assert.Equal("geaendert", k.Status);
            Assert.Equal(new[] { "INF13" }, k.AbwesendeKlassen);
        }
    }

    [Fact]
    public void Alle_Klassen_abwesend_bleibt_Entfall()
    {
        WebStunde S(int u, string klasse) => new() { Stunde = 1, Unterricht = u, Fach = "INF", Klassen = { klasse }, Lehrer = { "MUE" }, Raeume = { "EDV1" }, Status = "regulaer" };
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13") }, new[] { Absenz("klasse", "INF12"), Absenz("klasse", "INF13") });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Lehrer, "MUE"), (Elementtyp.Raum, "EDV1"), (Elementtyp.Klasse, "INF12"), (Elementtyp.Klasse, "INF13") })
        {
            var k = Assert.Single(plan.Karten(s, Tag, typ, kurz));
            Assert.Equal("entfall", k.Status);
            Assert.Equal("Klasse abwesend", k.Art);
            Assert.False(k.KlasseAbwesend);
        }
    }

    private static WebStunde Kopplung(string lehrer, string status = "regulaer", int? vertretung = null) => new()
    {
        Stunde = 1, Unterricht = 7, Fach = "D", Klassen = { "5a" }, Raeume = { "A101" }, Status = status, Vertretung = vertretung,
        Lehrer = status == "regulaer" ? new() { lehrer } : new(), LehrerAlt = status == "regulaer" ? new() : new() { lehrer },
    };

    private static WebVertretung Vertretung(string lehrer, string vertreter) => new()
    {
        Id = 1, Datum = "2026-10-14", Stunde = 1, Art = "Vertretung", Lehrer = lehrer, Vertreter = vertreter, Fach = "D", Raum = "A101", NeuerRaum = "A101", Klassen = { "5a" },
    };

    [Theory]
    [InlineData("offen", "???")]
    [InlineData("vertreten", "+++")]
    public void Eine_von_zwei_Lehrkraeften_abwesend_ohne_Vertretung(string status, string vertreter)
    {
        // BAM und BIK halten die Stunde gemeinsam; BIK fehlt, niemand vertritt
        var s = StandMit(new[] { Kopplung("BAM"), Kopplung("BIK", status, 1) }, new[] { Absenz("lehrer", "BIK") }, new[] { Vertretung("BIK", vertreter) });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Lehrer, "BAM"), (Elementtyp.Klasse, "5a"), (Elementtyp.Raum, "A101") })
        {
            // grüne Karte: BAM schlicht, BIK durchgestrichen im Label ({ k: "", alt: "BIK", neu: true }) – BIK wird nicht weggelassen
            var k = Assert.Single(plan.Karten(s, Tag, typ, kurz));
            Assert.Contains(k.Status, new[] { "geaendert", "offen" });
            Assert.False(k.LehrerAbwesend);
            Assert.Equal(new[] { new Wert("BAM"), new Wert("", "BIK", true) }, k.Lehrer);
            Assert.Contains("aenderung", k.Marker);
        }
        // Plan von BIK: „Lehrkraft abwesend“ (Eck-Klammern), kein Entfall
        var eigen = Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, "BIK"));
        Assert.Equal("abwesend", eigen.Status);
        Assert.True(eigen.LehrerAbwesend);
        Assert.DoesNotContain("entfall", eigen.Marker);
    }

    [Fact]
    public void Eine_von_zwei_Lehrkraeften_vertreten_nur_die_Vertretung_im_Label()
    {
        var bik = Kopplung("BIK", "vertreten", 1) with { Lehrer = new() { "HOF" } };
        var s = StandMit(new[] { Kopplung("BAM"), bik }, new[] { Absenz("lehrer", "BIK") }, new[] { Vertretung("BIK", "HOF") });
        var k = Assert.Single(new Plandienst().Karten(s, Tag, Elementtyp.Klasse, "5a"));
        Assert.Equal("geaendert", k.Status);
        // die Vertretung steht im Label (HOF statt BIK), BIK nicht zusätzlich durchgestrichen
        Assert.Equal(new[] { new Wert("BAM"), new Wert("HOF", "BIK", true) }, k.Lehrer);
    }
}
