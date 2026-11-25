using System.Net;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fall aus echten Daten: Eine Lehrkraft (Bereitschaft in der 5./6. Stunde) vertritt eine Doppelstunde (M, EF, 5./6.) und in der 5. Stunde
/// zusätzlich eine Einzelstunde einer anderen Klasse (PH, 7b). UntPlan vergibt die Vertretungsart je Stunde: in der 5. Stunde ist die
/// Doppelstunde ein Lehrertausch (die Lehrkraft hat dort schon eine Stunde), in der 6. eine Bereitschaftsvertretung. Die Doppelstunde bleibt
/// trotzdem EIN Block über beide Stunden (vorher: zwei Karten, die zweite links unter der Einzelstunde). Echte inhaltliche Unterschiede
/// zwischen den Stunden (anderer Raum) trennen den Block weiterhin.
/// </summary>
public class BlockParallelTests
{
    private const string D = "2026-10-13";

    private static JsonObject Vertretung(int id, int stunde, string art, string lehrer, string vertreter, string fach, string raum, string neuerRaum, string text, string klasse) => new()
    {
        ["id"] = id, ["datum"] = D, ["stunde"] = stunde, ["art"] = art, ["klassen"] = new JsonArray(klasse),
        ["lehrer"] = lehrer, ["vertreter"] = vertreter, ["fach"] = fach, ["neuesFach"] = fach, ["raum"] = raum, ["neuerRaum"] = neuerRaum,
        ["vertrVon"] = "", ["nach"] = "", ["text"] = text, ["nurLehrer"] = false,
    };

    private static JsonObject Zeile(int stunde, int unterricht, string fach, string klasse, string lehrer, string raum, string status, int? vertretung = null) => new()
    {
        ["stunde"] = stunde, ["unterricht"] = unterricht, ["fach"] = fach, ["klassen"] = new JsonArray(klasse.Length > 0 ? new JsonNode[] { klasse } : Array.Empty<JsonNode>()),
        ["lehrer"] = new JsonArray(lehrer), ["raeume"] = new JsonArray(raum.Length > 0 ? new JsonNode[] { raum } : Array.Empty<JsonNode>()),
        ["status"] = status, ["vertretung"] = vertretung, ["text"] = "",
    };

    /// <summary>LAN (am 13.10. sonst frei): Bereitschaft 5./6., vertritt SUL in M/EF 5./6. und KRU in PH/7b 5.</summary>
    private static JsonObject Paket(string raum6 = "R107")
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 95;
        var st = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == D)!["stunden"]!.AsArray();
        st.Add(Zeile(5, 184, "BER", "", "LAN", "", "regulaer"));
        st.Add(Zeile(6, 184, "BER", "", "LAN", "", "regulaer"));
        st.Add(Zeile(5, 171, "M", "EF", "LAN", "R107", "vertreten", 9501));
        st.Add(Zeile(6, 171, "M", "EF", "LAN", raum6, "vertreten", 9502));
        st.Add(Zeile(5, 162, "PH", "7b", "LAN", "PH1", "vertreten", 9503));
        var v = paket["vertretungen"]!.AsArray();
        v.Add(Vertretung(9501, 5, "Lehrertausch", "SUL", "LAN", "M", "R107", "R107", "", "EF"));
        v.Add(Vertretung(9502, 6, "Bereitschaftsvertretung", "SUL", "LAN", "M", "R107", raum6, "", "EF"));
        v.Add(Vertretung(9503, 5, "Vertretung", "KRU", "LAN", "PH", "PH1", "PH1", "Aufgaben", "7b"));
        return paket;
    }

    private static async Task<List<JsonNode>> KartenAsync(JsonObject paket, string typ, string element)
    {
        await using var s = await Testserver.StartenAsync(false);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={element}&datum={D}");
        return plan["karten"]!.AsArray().Where(k => k!["datum"]!.GetValue<string>() == D).Select(k => k!).ToList();
    }

    private static (int Von, int Bis) Lage(JsonNode k) => (k["von"]!.GetValue<int>(), k["bis"]!.GetValue<int>());

    [Fact]
    public async Task Doppelstunde_bleibt_ein_Block_auch_wenn_die_Vertretungsart_je_Stunde_wechselt()
    {
        var paket = Paket();
        var lehrer = await KartenAsync(paket, "lehrer", "LAN");
        var m = Assert.Single(lehrer, k => k["fach"]!.GetValue<string>() == "M");
        Assert.Equal((5, 6), Lage(m));
        Assert.Equal("geaendert", m["status"]!.GetValue<string>());
        // beide Arten bleiben für die Details erhalten
        Assert.Equal("Lehrertausch, Bereitschaftsvertretung", m["art"]!.GetValue<string>());
        Assert.Equal((5, 5), Lage(Assert.Single(lehrer, k => k["fach"]!.GetValue<string>() == "PH")));
        Assert.Equal((5, 6), Lage(Assert.Single(lehrer, k => k["fach"]!.GetValue<string>() == "BER")));
        // ebenso im Klassen- und Raumplan
        Assert.Equal((5, 6), Lage(Assert.Single(await KartenAsync(paket, "klasse", "EF"), k => k["fach"]!.GetValue<string>() == "M" && k["von"]!.GetValue<int>() >= 5)));
        Assert.Equal((5, 6), Lage(Assert.Single(await KartenAsync(paket, "raum", "R107"), k => k["fach"]!.GetValue<string>() == "M" && k["von"]!.GetValue<int>() >= 5)));
    }

    [Fact]
    public async Task Anderer_Raum_in_einer_Stunde_trennt_den_Block_weiterhin()
    {
        var lehrer = await KartenAsync(Paket(raum6: "R999"), "lehrer", "LAN");
        var m = lehrer.Where(k => k["fach"]!.GetValue<string>() == "M").Select(Lage).OrderBy(x => x).ToList();
        Assert.Equal(new[] { (5, 5), (6, 6) }, m);
    }
}
