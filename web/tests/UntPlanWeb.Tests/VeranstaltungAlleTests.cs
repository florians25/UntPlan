using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>Veranstaltung mit allen Klassen bzw. Lehrkräften (Kennzeichen aus dem Export): Bezeichnung statt endloser Liste.</summary>
public class VeranstaltungAlleTests
{
    private static JsonObject PaketMitKonferenz(bool kennzeichen)
    {
        var p = Testserver.Beispielpaket();
        p["revision"] = 2;
        var klassen = new JsonArray(p["elemente"]!["klassen"]!.AsArray().Select(k => (JsonNode)JsonValue.Create(k!["kurz"]!.GetValue<string>())!).ToArray());
        var lehrer = new JsonArray(p["elemente"]!["lehrer"]!.AsArray().Select(k => (JsonNode)JsonValue.Create(k!["kurz"]!.GetValue<string>())!).ToArray());
        var termin = new JsonObject
        {
            ["art"] = "veranstaltung", ["titel"] = "Lehrkräftekonferenz", ["von"] = "2026-10-14", ["bis"] = "2026-10-14", ["stundeVon"] = 5, ["stundeBis"] = 99,
            ["klassen"] = klassen, ["lehrer"] = lehrer, ["raeume"] = new JsonArray(),
        };
        if (kennzeichen)
        {
            termin["id"] = 99001;
            termin["alleKlassen"] = true;
            termin["alleLehrer"] = true;
        }
        p["termine"]!.AsArray().Add(termin);
        return p;
    }

    private static async Task<string> Kopfzeile(bool kennzeichen)
    {
        await using var s = await Testserver.StartenAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(PaketMitKonferenz(kennzeichen))).StatusCode);
        var c = await s.AlsAsync("admin");
        var vt = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-14&tage=1");
        return vt["tage"]![0]!["veranstaltungen"]!.AsArray().Select(x => x!.GetValue<string>()).Single(x => x.StartsWith("Lehrkräftekonferenz"));
    }

    [Fact]
    public async Task Alle_Klassen_als_Bezeichnung()
    {
        var text = await Kopfzeile(true);
        Assert.Equal("Lehrkräftekonferenz (ab 5. Std., alle Klassen)", text);
    }

    [Fact]
    public async Task Aeltere_Pakete_ohne_Kennzeichen_listen_die_Klassen()
    {
        var text = await Kopfzeile(false);
        Assert.StartsWith("Lehrkräftekonferenz (ab 5. Std., ", text);
        Assert.DoesNotContain("alle Klassen", text);
        Assert.Contains("6a", text);
    }
}
