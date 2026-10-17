using System.Text.Json.Nodes;
using UntPlanMobile.Api;

namespace UntPlanMobile.Tests;

/// <summary>Prüfungsliste der Startseite: eine von UntPlan übernommene eigene Prüfung erscheint nicht zusätzlich als „vorgemerkt“.</summary>
public class KlausurlisteTests
{
    private static JsonObject Heute(params object[] termine) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new { termine }))!.AsObject();

    private static JsonObject Meldung(string von, int sv, int sb, params string[] klassen) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new { typ = "klausur", status = "offen", von, stundeVon = sv, stundeBis = sb, klassen }))!.AsObject();

    [Fact]
    public void Uebernommene_Pruefung_nur_einmal()
    {
        var heute = Heute(new { art = "klausur", titel = "M", von = "2026-10-15", bis = "2026-10-15", stundeVon = 1, stundeBis = 2, klassen = new[] { "5b" } });
        Assert.True(StartseiteApi.SchonAlsTermin(heute, Meldung("2026-10-15", 1, 2, "5b")));
        Assert.True(StartseiteApi.SchonAlsTermin(heute, Meldung("2026-10-15", 2, 2, "5B")));
        // anderer Tag, andere Stunden, andere Klasse oder Veranstaltung: weiterhin vorgemerkt
        Assert.False(StartseiteApi.SchonAlsTermin(heute, Meldung("2026-10-16", 1, 2, "5b")));
        Assert.False(StartseiteApi.SchonAlsTermin(heute, Meldung("2026-10-15", 3, 4, "5b")));
        Assert.False(StartseiteApi.SchonAlsTermin(heute, Meldung("2026-10-15", 1, 2, "6a")));
        Assert.False(StartseiteApi.SchonAlsTermin(Heute(new { art = "veranstaltung", titel = "Ausflug", von = "2026-10-15", bis = "2026-10-15", stundeVon = 1, stundeBis = 6, klassen = new[] { "5b" } }),
            Meldung("2026-10-15", 1, 2, "5b")));
        Assert.False(StartseiteApi.SchonAlsTermin(null, Meldung("2026-10-15", 1, 2, "5b")));
    }
}
