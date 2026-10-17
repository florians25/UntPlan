using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Kartenregeln Sprint 29: Fachänderung ohne Label, Zusatzstunden mit allen Lehrkräften als „+“-Label, Aufsichtsvertretung nur mit
/// Ort und „…“, entfallene Aufsicht im Entfall-Stil.
/// </summary>
public class AufsichtZusatzTests
{
    private static readonly List<PlanZeit> Zeiten = new()
    {
        new(1, "1", "07:55", "08:40"), new(2, "2", "08:45", "09:30"), new(3, "3", "09:50", "10:35"),
    };

    private static PlanKarte Karte(string status, string fach = "ZKonf", string art = "", int? unterricht = 106,
        List<PlanWert>? lehrer = null, string? fachAlt = null) =>
        new("2026-10-14", 3, 3, "", "", status, fach, fachAlt, fach, "", new() { new("5a") }, lehrer ?? new() { new("WEB") },
            new() { new("R101") }, art, "", null, unterricht);

    private static PlanAufsicht Aufsicht(string status, string lehrer, string? alt = null, string? grund = null) =>
        new("2026-10-14", 2, "09:30", "09:50", "Hof", "Hof", status, lehrer, alt, grund);

    [Fact]
    public void Zusatzstunde_zeigt_alle_Lehrkraefte_mit_Plus_und_ohne_Durchstreichung()
    {
        // wie im Fehlerbild: „CS, GA, ~~SN~~ | CS“ – richtig ist „+CS, +GA, +SN“
        var karte = Karte("geaendert", unterricht: null, lehrer: new() { new("CS"), new("GA"), new("CS", "SN", true) }) with { Zusaetzlich = true };
        foreach (var art in new[] { "teacher", "class", "room" })
        {
            var k = Kachelbau.AusKarte(karte, art);
            Assert.True(k.Zusatz);
            Assert.Equal("CS,GA,SN", k.Lehrkraft);
            Assert.Equal("", k.LehrkraftAlt);
            Assert.Equal("CS,GA,SN", k.LehrkraftPlus);
            Assert.NotNull(k.Kopplung);
            Assert.All(k.Kopplung!, e => { Assert.Equal("sub", e.Zustand); Assert.True(e.Plus); Assert.Null(e.Fuer); });
            Assert.Equal(new[] { "CS", "GA", "SN" }, k.Kopplung!.Select(e => e.Code));
            Assert.Null(k.LehrkraftOffen);
        }
        // eine Lehrkraft: ebenfalls „+“-Label (Plakette mit „+WEB“)
        var einzeln = Kachelbau.AusKarte(Karte("geaendert", unterricht: null) with { Zusaetzlich = true }, "class");
        Assert.Equal("WEB", einzeln.LehrkraftPlus);
        Assert.Contains("teacher", einzeln.Geaendert);
        // ohne Lehrkraft: „---“
        var ohne = Kachelbau.AusKarte(Karte("geaendert", unterricht: null, lehrer: new()) with { Zusaetzlich = true }, "class");
        Assert.Equal(Kachelbau.KeineLehrkraft, ohne.Lehrkraft);
        Assert.Null(ohne.LehrkraftPlus);
    }

    [Fact]
    public void Zusatzliste_dedupliziert_und_laesst_Platzhalter_weg()
    {
        var l = Kachelbau.ZusatzLehrer(new PlanWert[] { new("cs"), new("CS", "SN", true), new("---"), new("???"), new("GA", "ga", true) });
        Assert.Equal(new[] { "cs", "SN", "GA" }, l.Select(w => w.K));
        Assert.All(l, w => { Assert.True(w.Plus); Assert.True(w.Neu); Assert.Null(w.Alt); });
    }

    [Fact]
    public void Fachaenderung_ohne_Label()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", fach: "ZBesp", fachAlt: "ZKonf", art: "Vertretung"), "teacher");
        Assert.Equal("ZBesp", k.Fach);
        Assert.DoesNotContain("subject", k.Geaendert);
        // das alte Fach bleibt für die Details erhalten
        Assert.Equal("ZKonf", k.FachAlt);
    }

    [Fact]
    public void Aufsichtsvertretung_ohne_Kuerzel_auf_der_Kachel_Entfall_im_Entfall_Stil()
    {
        var abgegeben = Kachelbau.Aufsicht(Aufsicht("vertreten", "AD", "KE"), Zeiten);
        Assert.True(abgegeben.AufsichtAbgegeben);
        Assert.Null(abgegeben.AufsichtEntfall);
        Assert.Equal("Hof", abgegeben.Fach);

        var uebernommen = Kachelbau.Aufsicht(Aufsicht("vertretung", "AD", "KE"), Zeiten);
        Assert.True(uebernommen.AufsichtUebernommen);
        Assert.Equal("Vertretung für KE", uebernommen.Info);

        var entfall = Kachelbau.Aufsicht(Aufsicht("entfall", "KE", grund: "Abwesenheit"), Zeiten);
        Assert.True(entfall.AufsichtEntfall);
        Assert.Null(entfall.AufsichtAbgegeben);
        Assert.Null(entfall.AufsichtUebernommen);
        Assert.Empty(entfall.Geaendert);
        Assert.Equal("Aufsicht entfällt – Abwesenheit", entfall.Info);
        Assert.Equal("Aufsicht entfällt", Kachelbau.Aufsicht(Aufsicht("entfall", "KE"), Zeiten).Info);
    }

    [Fact]
    public void App_js_Aufsicht_ohne_Kuerzel_und_mit_Entfall_Banner()
    {
        var js = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
        Assert.Contains("const banner = c._supCancelled ? 'ENTFÄLLT'", js);
        Assert.Contains("? aufsichtInhalt(card)", js);
    }
}
