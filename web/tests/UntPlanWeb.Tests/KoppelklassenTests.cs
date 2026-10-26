using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Koppelklassen: parallel identischer Unterricht zweier Klassen (gleiche Lehrkraft, gleiches Fach, gleicher Raum).</summary>
public class KoppelklassenTests
{
    private static readonly DateOnly Tag = new(2026, 10, 14);

    private static Stand StandMit(IEnumerable<WebStunde> stunden, IEnumerable<WebAbsenz>? absenzen = null) => new(new WebPaket
    {
        Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(1, "1.", "08:00", "08:45"), new WebStundenzeit(2, "2.", "08:50", "09:35") } },
        Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = stunden.ToList() } },
        Absenzen = absenzen?.ToList() ?? new(),
    }, "2026-10-14 07:00:00");

    private static WebStunde S(int u, string klasse, string status = "regulaer", int stunde = 1, string raum = "EDV1") => new()
    {
        Stunde = stunde, Unterricht = u, Fach = "INF", Klassen = { klasse }, Lehrer = status == "entfall" ? new() : new() { "MUE" },
        LehrerAlt = status == "entfall" ? new() { "MUE" } : new(), Raeume = { raum }, Status = status,
    };

    [Fact]
    public void Mitklasse_steht_auf_der_Karte_der_anderen_Klasse()
    {
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13"), S(3, "INF14", raum: "EDV2") });
        var plan = new Plandienst();
        var k12 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF12"));
        Assert.Equal("regulaer", k12.Status);
        Assert.Equal(new[] { "INF12" }, k12.Klassen.Select(w => w.K));
        // anderer Raum (INF14) ist keine Kopplung
        Assert.Equal(new[] { new Wert("INF13") }, k12.Mitklassen);
        var k13 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF13"));
        Assert.Equal(new[] { new Wert("INF12") }, k13.Mitklassen);
        // Lehrerplan: wie bisher eine Karte mit beiden Klassen, keine Mitklassen
        Assert.All(plan.Karten(s, Tag, Elementtyp.Lehrer, "MUE"), k => Assert.Empty(k.Mitklassen));
    }

    [Fact]
    public void Entfall_der_Mitklasse_ohne_Klassenabwesenheit_ist_keine_fehlende_Mitklasse()
    {
        // INF13 hat Entfall ohne Klassenabwesenheit: INF13 hat keinen Unterricht – bei INF12 die reguläre Stunde ohne Mitklasse (nichts
        // durchgestrichen); bei INF13 bleibt es der Entfall
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13", "entfall") });
        var plan = new Plandienst();
        var k12 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF12"));
        Assert.Equal("regulaer", k12.Status);
        Assert.Empty(k12.Mitklassen);
        var k13 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF13"));
        Assert.Equal("entfall", k13.Status);
    }

    [Fact]
    public void Entfall_der_Mitklasse_wegen_Klassenabwesenheit_macht_die_Stunde_geaendert()
    {
        // INF13 abwesend, ihre Zeile entfällt: bei INF12 grün, INF13 durchgestrichen
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13", "entfall") },
            new[] { new WebAbsenz { Art = "klasse", Element = "INF13", Von = "2026-10-14", Bis = "2026-10-14", StundeVon = 1, StundeBis = 8 } });
        var plan = new Plandienst();
        var k12 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF12"));
        Assert.Equal("geaendert", k12.Status);
        Assert.Equal(new[] { new Wert("", "INF13", true) }, k12.Mitklassen);
    }

    [Fact]
    public void Verlegte_Stunde_ist_keine_Mitklasse()
    {
        // INF13 (anderer Unterricht derselben Lehrkraft, gleiches Fach, gleicher Raum) ist in diese Stunde verlegt, INF12s Stunde ist der
        // Ursprung einer Verlegung: keine gemeinsame Stunde – weder durchgestrichen noch als Mitklasse
        var s = StandMit(new[] { S(1, "INF12") });
        var plan = new Plandienst();
        var verlegt = new Rohstunde(1, 2, "INF", new[] { "INF13" }, new[] { "MUE" }, new[] { "EDV1" }, "vertreten", null, "", "Verlegung");
        var k12 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF12", new[] { verlegt }), k => k.Unterricht == 1);
        Assert.Empty(k12.Mitklassen);
        Assert.Equal("regulaer", k12.Status);
        var k13 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF13", new[] { verlegt }), k => k.Unterricht == 2);
        Assert.Empty(k13.Mitklassen);
    }

    [Fact]
    public void Abwesende_Mitklasse_aus_Absenz()
    {
        var s = StandMit(new[] { S(1, "INF12"), S(2, "INF13") }, new[] { new WebAbsenz { Art = "klasse", Element = "INF13", Von = "2026-10-14", Bis = "2026-10-14", StundeVon = 1, StundeBis = 8 } });
        var plan = new Plandienst();
        var k12 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF12"));
        Assert.Equal("geaendert", k12.Status);
        Assert.Equal(new[] { new Wert("", "INF13", true) }, k12.Mitklassen);
        Assert.Empty(k12.AbwesendeKlassen);
        // im Plan der abwesenden Klasse: die Stunde läuft mit INF12 ohne sie weiter – wie „Lehrkraft abwesend“, kein Entfall
        var k13 = Assert.Single(plan.Karten(s, Tag, Elementtyp.Klasse, "INF13"));
        Assert.Equal("abwesend", k13.Status);
        Assert.True(k13.KlasseAbwesend);
        Assert.Empty(k13.Marker);
        Assert.Equal(new[] { "INF13" }, k13.AbwesendeKlassen);
        Assert.Equal(new[] { new Wert("INF12") }, k13.Mitklassen);
    }
}
