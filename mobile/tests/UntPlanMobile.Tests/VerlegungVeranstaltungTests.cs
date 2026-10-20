using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Kacheln: verlegte Stunde ohne Zusatzstunden-Darstellung (keine Lehrer-Labels), Entfall wegen einer Veranstaltung ohne Text und
/// Info-Zeichen, Fachänderung nur in den Details, Fachwechsel als zwei Blöcke mit eigener Fachfarbe.
/// </summary>
public class VerlegungVeranstaltungTests
{
    private static PlanKarte Karte(string status, string art = "", string text = "", string fach = "M", string? fachAlt = null, string farbe = "#7BAAF7",
        List<PlanWert>? lehrer = null, List<PlanWert>? raeume = null, bool zusaetzlich = false, string? verlegung = null, bool wegenVeranstaltung = false,
        int? unterricht = 106) =>
        new("2026-10-14", 3, 3, "", "", status, fach, fachAlt, fach, farbe, new() { new("5a") }, lehrer ?? new() { new("WEB") },
            raeume ?? new() { new("R101") }, art, text, null, unterricht, Zusaetzlich: zusaetzlich, Verlegung: verlegung,
            Marker: new(), WegenVeranstaltung: wegenVeranstaltung);

    [Fact]
    public void Verlegte_Stunde_ist_keine_Zusatzstunde_und_zeigt_die_Lehrkraft_ohne_Label()
    {
        // älterer Server: Ziel einer Verlegung noch als zusätzlich gemeldet
        var k = Kachelbau.AusKarte(Karte("geaendert", "Verlegung", zusaetzlich: true, verlegung: "nach",
            raeume: new() { new("R105", "R101", true) }), "class");
        Assert.Equal("nach", k.Verlegung);
        Assert.Null(k.Zusatz);
        Assert.Null(k.LehrkraftPlus);
        Assert.DoesNotContain("teacher", k.Geaendert);
        Assert.Equal("WEB", k.Lehrkraft);
        // nur der geänderte Raum steht im Label
        Assert.Contains("room", k.Geaendert);
        // echte Zusatzstunde bleibt eine
        var z = Kachelbau.AusKarte(Karte("geaendert", "Zusatzunterricht", zusaetzlich: true), "class");
        Assert.True(z.Zusatz);
    }

    [Fact]
    public void Entfall_wegen_Veranstaltung_ohne_Text_und_ohne_Klasse_abwesend()
    {
        var k = Kachelbau.AusKarte(Karte("entfall", "Freisetzung", "Wandertag", wegenVeranstaltung: true), "class");
        Assert.Equal("cancelled", k.Art);
        Assert.Equal("", k.Info);
        // gewöhnliche Freisetzung (Klasse abwesend) behält ihren Hinweis
        var f = Kachelbau.AusKarte(Karte("entfall", "Freisetzung"), "class");
        Assert.Equal("Klasse abwesend", f.Info);
    }

    [Fact]
    public void Fachwechsel_neuer_Block_neue_Farbe_alter_Block_alte_Farbe_Fachaenderung_fuer_die_Details()
    {
        var neu = Kachelbau.AusKarte(Karte("geaendert", "Vertretung", fach: "M", fachAlt: "E", farbe: "#7BAAF7", lehrer: new() { new("KRU", "WAG", true) }), "class");
        var alt = Kachelbau.AusKarte(Karte("entfall", "Entfall", fach: "E", farbe: "#81C995", lehrer: new() { new("WAG") }, unterricht: null), "class");
        Assert.Equal("change", neu.Art);
        Assert.Equal("#7BAAF7", neu.Farbe);
        Assert.Equal("M", neu.Fach);
        Assert.Equal("E", neu.FachAlt);                  // Details: altes Fach durchgestrichen
        Assert.DoesNotContain("subject", neu.Geaendert); // Kachel: kein Label für das Fach
        Assert.Equal("cancelled", alt.Art);
        Assert.Equal("#81C995", alt.Farbe);
        Assert.Equal("", alt.FachAlt);
    }

    [Fact]
    public void Web_App_Details_streichen_das_alte_Fach_und_Karten_zeigen_kein_Plus_N()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanMobile", "UntPlanMobile.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
        Assert.Contains("|| ((key === 'teacher' || key === 'subject') && orig !== '' && now !== ''", js);
    }
}
