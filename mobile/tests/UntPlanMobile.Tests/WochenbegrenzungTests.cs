using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Wochenbegrenzung je Rolle: UntPlanWeb meldet eine gesperrte Woche (gesperrt + sperrhinweis, ohne Stunden) und in der Sitzung
/// planWochenZurueck/planWochenVor. UntPlanMobile reicht beides an die Oberfläche durch.
/// </summary>
public class WochenbegrenzungTests
{
    /// <summary>UntPlanWeb-Ersatz: beantwortet jeden Aufruf mit dem festen JSON.</summary>
    private sealed class FesteAntwort(string json) : HttpMessageHandler
    {
        public List<string> Pfade { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pfade.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static readonly Konto Lehrkraft = new(
        new Sitzungsbenutzer(1, "sne", "Julia Schneider", "lehrer", "Lehrkraft", "SNE", true, false, "lehrer"), "Testschule", Elemente.Leer);

    private static (Wochendienst Dienst, HttpContext Kontext, FesteAntwort Web) Aufbau(string planJson)
    {
        var web = new FesteAntwort(planJson);
        var verbindung = new UntPlanWebVerbindung(new HttpClient(web) { BaseAddress = new Uri("http://untplanweb.test/") });
        var konten = new Kontodienst(verbindung, new MemoryCache(Options.Create(new MemoryCacheOptions())));
        var zeit = new Zeitgeber(TimeZoneInfo.Utc) { Fest = new DateTime(2026, 10, 14, 10, 0, 0) };
        var c = new DefaultHttpContext();
        c.Items[UntPlanWebVerbindung.Cookie] = "sitzung";
        return (new Wochendienst(verbindung, konten, zeit), c, web);
    }

    private const string GesperrteWoche = """
        { "typ":"lehrer", "element":"SNE", "name":"Julia Schneider", "montag":"2027-01-11", "woche":"A",
          "zeitraster":[], "tage":[], "karten":[], "aufsichten":[],
          "gesperrt":true, "sperrhinweis":"Der Plan ist nur 2 Wochen im Voraus sichtbar." }
        """;

    [Fact]
    public async Task Gesperrte_Woche_kommt_leer_mit_Sperrhinweis()
    {
        var (dienst, c, _) = Aufbau(GesperrteWoche);
        var w = await dienst.WocheAsync(c, Lehrkraft, "mine", null, new DateOnly(2027, 1, 13));
        Assert.True(w.Gesperrt);
        Assert.Equal("Der Plan ist nur 2 Wochen im Voraus sichtbar.", w.Sperrhinweis);
        Assert.Equal("2027-01-11", w.WeekStart);
        Assert.Equal(5, w.Days.Count);
        Assert.All(w.Days.Values, l => Assert.Empty(l));

        // Im JSON für die Oberfläche: gesperrt und sperrhinweis
        var json = JsonNode.Parse(JsonSerializer.Serialize(w, Program.JsonAusgabe))!;
        Assert.True(json["gesperrt"]!.GetValue<bool>());
        Assert.Equal("Der Plan ist nur 2 Wochen im Voraus sichtbar.", json["sperrhinweis"]!.GetValue<string>());
    }

    [Fact]
    public async Task Gesperrte_Woche_ohne_Hinweis_und_tolerante_Schreibweise()
    {
        var (dienst, c, _) = Aufbau(GesperrteWoche.Replace("\"gesperrt\":true", "\"Gesperrt\":true").Replace("\"Der Plan ist nur 2 Wochen im Voraus sichtbar.\"", "\"  \""));
        var w = await dienst.WocheAsync(c, Lehrkraft, "mine", null, new DateOnly(2027, 1, 13));
        Assert.True(w.Gesperrt);
        Assert.Null(w.Sperrhinweis);
    }

    [Fact]
    public async Task Freie_Woche_traegt_kein_Sperrkennzeichen()
    {
        var (dienst, c, _) = Aufbau("""
            { "typ":"lehrer", "element":"SNE", "name":"", "montag":"2026-10-12", "woche":"A",
              "zeitraster":[], "tage":[], "karten":[], "aufsichten":[] }
            """);
        var w = await dienst.WocheAsync(c, Lehrkraft, "mine", null, new DateOnly(2026, 10, 14));
        Assert.Null(w.Gesperrt);
        var json = JsonNode.Parse(JsonSerializer.Serialize(w, Program.JsonAusgabe))!;
        Assert.Null(json["gesperrt"]);
        Assert.Null(json["sperrhinweis"]);
    }

    [Theory]
    [InlineData("""{ "planWochenZurueck": 4, "planWochenVor": 2, "benutzer": {} }""", 4, 2)]
    [InlineData("""{ "benutzer": { "planWochenZurueck": 1, "planWochenVor": "3" } }""", 1, 3)]
    [InlineData("""{ "planWochenZurueck": null, "benutzer": { "planWochenVor": null } }""", null, null)]
    [InlineData("""{ "benutzer": {} }""", null, null)]
    [InlineData("""{ "planWochenZurueck": 0, "planWochenVor": -2, "benutzer": {} }""", 0, 0)]
    public void Wochenbegrenzung_aus_der_Sitzung(string sitzung, int? zurueck, int? vor)
    {
        var s = JsonNode.Parse(sitzung)!;
        Assert.Equal(zurueck, Kontodienst.Wochenzahl(s, s["benutzer"], "planWochenZurueck"));
        Assert.Equal(vor, Kontodienst.Wochenzahl(s, s["benutzer"], "planWochenVor"));
    }

    [Fact]
    public void Wochenbegrenzung_steht_beim_Benutzer_unbegrenzt_entfaellt_sie()
    {
        var begrenzt = Lehrkraft with { };
        begrenzt = begrenzt with { PlanWochenZurueck = 2, PlanWochenVor = 1 };
        var json = JsonNode.Parse(JsonSerializer.Serialize(Wochendienst.Benutzer(begrenzt), Program.JsonAusgabe))!;
        Assert.Equal(2, json["planWochenZurueck"]!.GetValue<int>());
        Assert.Equal(1, json["planWochenVor"]!.GetValue<int>());
        var frei = JsonNode.Parse(JsonSerializer.Serialize(Wochendienst.Benutzer(Lehrkraft), Program.JsonAusgabe))!;
        Assert.Null(frei["planWochenZurueck"]);
        Assert.Null(frei["planWochenVor"]);
    }

    /// <summary>Oberfläche: Schloss mit Text bei gesperrter Woche; Lehrstoff und Notizen ohne Begrenzung auf zwei Wochen.</summary>
    [Fact]
    public void Oberflaeche_zeigt_Sperre_und_kennt_keine_Eintragsgrenze()
    {
        var js = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
        Assert.Contains("Es werden hier keine Termine angezeigt.", js);
        Assert.Contains("sperrhinweis", js);
        Assert.DoesNotContain("CB_VORAUS_TAGE", js);
        Assert.DoesNotContain("zwei Wochen im Voraus", js);
        var wacht = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "planwacht.js"));
        Assert.Contains("gesperrt", wacht);
    }
}
