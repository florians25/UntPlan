using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// PWA: Veranstaltung ohne Platzhalter als Lehrkraft, Fachänderung mit bisherigem Fach für die Details, Aufsichten im Raumplan eines
/// Aufsichtsorts (wer führt die Aufsicht).
/// </summary>
public class PlanKorrekturenTests
{
    private static readonly List<PlanZeit> Zeiten = new()
    {
        new(1, "1", "08:00", "08:45"), new(2, "2", "08:50", "09:35"), new(3, "3", "09:55", "10:40"),
    };

    private static Wochenplan Plan(string typ, List<PlanKarte> karten, List<PlanAufsicht> aufsichten) =>
        new(typ, "X", "", "2026-10-12", "A", Zeiten, new() { new PlanTag("2026-10-14", "Mi", "A", false, null, true, new(), new()) }, karten, aufsichten);

    [Fact]
    public void Veranstaltung_ohne_Lehrkraefte_zeigt_keinen_Platzhalter()
    {
        foreach (var platz in new[] { "---", "???", "+", "+++" })
        {
            var karte = new PlanKarte("2026-10-14", 2, 3, "", "", "veranstaltung", "", null, "", "", new() { new("5a"), new("5b") }, new() { new(platz) },
                new(), "Ausflug", "Wandertag", null, null);
            var k = Assert.Single(Kachelbau.Tag(Plan("klasse", new() { karte }, new()), new PlanTag("2026-10-14", "Mi", "A", false, null, true, new(), new()), Array.Empty<Ferienzeit>()));
            Assert.Equal("", k.Lehrkraft);
            Assert.Equal("Wandertag", k.Fach);
        }
        var js = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
        Assert.Contains("filter(t => !['---', '???', '?', '+', '+++'].includes(t))", js);
    }

    [Fact]
    public void Fachaenderung_traegt_das_bisherige_Fach_fuer_die_Details()
    {
        var karte = new PlanKarte("2026-10-14", 2, 2, "", "", "geaendert", "SL", "MIK", "SL", "", new() { new("ING12") }, new() { new("EVA", "CK", true) },
            new() { new("B063") }, "Vertretung", "", 7, 106);
        var k = Kachelbau.AusKarte(karte, "class");
        Assert.Equal("SL", k.Fach);
        Assert.Equal("MIK", k.FachAlt);
    }

    [Fact]
    public void Raumplan_eines_Aufsichtsorts_nennt_wer_Aufsicht_fuehrt()
    {
        var aufsichten = new List<PlanAufsicht>
        {
            new("2026-10-14", 1, "08:45", "08:50", "EIN", "Einfahrt", "regulaer", "CS", null, null),
            new("2026-10-14", 2, "09:35", "09:55", "EIN", "Einfahrt", "vertretung", "VX", "HE", null),
        };
        var tag = Kachelbau.Tag(Plan("raum", new(), aufsichten), new PlanTag("2026-10-14", "Mi", "A", false, null, true, new(), new()), Array.Empty<Ferienzeit>());
        var a1 = Assert.Single(tag, k => k.Aufsicht == true && k.Stunde == 1);
        Assert.Equal("CS", a1.Fach);
        Assert.Equal("Einfahrt", a1.Raum);
        var a2 = Assert.Single(tag, k => k.Aufsicht == true && k.Stunde == 2);
        Assert.Equal("VX", a2.Fach);
        Assert.True(a2.AufsichtUebernommen);
        // im Lehrerplan weiter der Ort
        var lehrer = Kachelbau.Tag(Plan("lehrer", new(), aufsichten.Take(1).ToList()), new PlanTag("2026-10-14", "Mi", "A", false, null, true, new(), new()), Array.Empty<Ferienzeit>());
        Assert.Equal("Einfahrt", Assert.Single(lehrer).Fach);
    }
}
