using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UntPlanMobile.Tests;

/// <summary>
/// UntPlanWeb und UntPlanMobile im Testprozess (Kestrel auf freien Ports). UntPlanWeb bekommt das Beispielpaket
/// aus web/demo (Woche ab 12.10.2026) und eine feste Uhr; UntPlanMobile verbindet sich über UNTPLANWEB_URL.
/// </summary>
public sealed class Umgebung : IAsyncDisposable
{
    public const string Passwort = "test-passwort";
    private const string SchulId = "testschule";
    private const string ApiSchluessel = "test-schluessel-1234567890";
    public static readonly DateTime Jetzt = new(2026, 10, 14, 10, 0, 0);

    private readonly WebApplication _web;
    private readonly WebApplication _mobile;
    private readonly string _ordner;

    public Uri WebAdresse { get; }
    public Uri MobilAdresse { get; }

    private Umgebung(WebApplication web, WebApplication mobile, string ordner)
    {
        _web = web;
        _mobile = mobile;
        _ordner = ordner;
        WebAdresse = Adresse(web);
        MobilAdresse = Adresse(mobile);
    }

    /// <summary>Dienste von UntPlanMobile (Kontenspeicher, Pushwache …).</summary>
    public IServiceProvider MobilDienste => _mobile.Services;

    /// <summary>Dienste von UntPlanWeb.</summary>
    public IServiceProvider WebDienste => _web.Services;

    private static Uri Adresse(WebApplication app) => new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

    /// <summary>Wurzel des Repositorys (enthält web/ und mobile/).</summary>
    public static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "web", "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public static async Task<Umgebung> StartenAsync()
    {
        var wurzel = Wurzel();
        var ordner = Path.Combine(Path.GetTempPath(), "untplanmobile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);

        var web = UntPlanWeb.Program.Erstellen(new[] { "--contentRoot", Path.Combine(wurzel, "web", "src", "UntPlanWeb") }, b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UNTPLANWEB_DATEN"] = ordner,
                ["UNTPLANWEB_ADMIN_PASSWORT"] = "admin-passwort",
                ["UNTPLANWEB_SCHUL_ID"] = SchulId,
                ["UNTPLANWEB_API_SCHLUESSEL"] = ApiSchluessel,
                ["Logging:LogLevel:Default"] = "Warning",
            });
            b.WebHost.UseUrls("http://127.0.0.1:0");
        });
        web.Services.GetRequiredService<UntPlanWeb.Dienste.Uhr>().Fest = Jetzt;
        await web.StartAsync();
        var webAdresse = Adresse(web);

        // Beispielpaket wie von UntPlan übertragen
        using (var sync = new HttpClient { BaseAddress = webAdresse })
        {
            sync.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiSchluessel);
            var paket = await File.ReadAllTextAsync(Path.Combine(wurzel, "web", "demo", "beispiel-paket.json"));
            var r = await sync.PutAsync($"/api/v1/schulen/{SchulId}/untplanweb", new StringContent(paket, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        var bd = web.Services.GetRequiredService<UntPlanWeb.Dienste.Benutzerdienst>();
        bd.Anlegen("sne", Passwort, "lehrer", "SNE", "Julia Schneider");
        bd.Anlegen("fis", Passwort, "lehrer", "FIS", "Michael Fischer");
        bd.Anlegen("5a", Passwort, "schueler", "5a", "Klasse 5a");

        var mobile = Program.Erstellen(new[] { "--contentRoot", Path.Combine(wurzel, "mobile", "src", "UntPlanMobile") }, b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UNTPLANWEB_URL"] = webAdresse.ToString(),
                ["UNTPLANMOBILE_DATEN"] = Path.Combine(ordner, "mobil"),
                // Die Pushwache prüft in den Tests nur auf Anforderung (RundeAsync)
                ["UNTPLANMOBILE_PUSH_INTERVALL"] = "0",
                ["Logging:LogLevel:Default"] = "Warning",
            });
            b.WebHost.UseUrls("http://127.0.0.1:0");
        });
        mobile.Services.GetRequiredService<Dienste.Zeitgeber>().Fest = Jetzt;
        await mobile.StartAsync();
        return new Umgebung(web, mobile, ordner);
    }

    /// <summary>Client für UntPlanMobile mit eigenem Cookie-Speicher (wie ein Browser).</summary>
    public HttpClient Client()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        return new HttpClient(handler) { BaseAddress = MobilAdresse };
    }

    public async Task<HttpClient> AngemeldetAsync(string login)
    {
        var c = Client();
        var r = await c.PostAsync("/api/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["action"] = "login", ["username"] = login, ["password"] = Passwort }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    public static async Task<JsonNode> JsonAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"HTTP {(int)r.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    public static async Task<JsonNode> HolenAsync(HttpClient c, string pfad) => await JsonAsync(await c.GetAsync(pfad));

    public static async Task<JsonNode> SendenAsync(HttpClient c, string pfad, Dictionary<string, string> formular) =>
        await JsonAsync(await c.PostAsync(pfad, new FormUrlEncodedContent(formular)));

    /// <summary>Offene (noch nicht von UntPlan übernommene) Raumbuchungen in UntPlanWeb.</summary>
    public int OffeneRaumbuchungen() =>
        _web.Services.GetRequiredService<UntPlanWeb.Dienste.Rueckmeldungsdienst>().Offene().Count(m => m.Typ == "raumbuchung");

    /// <summary>Klassendienst (z. B. Klassensprecher*in) für eine Schülerin bzw. einen Schüler im ganzen Schuljahr.</summary>
    public void KlassendienstAnlegen(string dienst, int schuelerId, string klasse)
    {
        var kv = _web.Services.GetRequiredService<UntPlanWeb.Dienste.Klassenbuchverwaltung>();
        var id = kv.Dienstarten().First(d => d.Name == dienst).Id;
        _web.Services.GetRequiredService<UntPlanWeb.Daten.Db>().Ausfuehren(
            "INSERT INTO klassendienst_zuordnungen (dienst_id, schueler_id, klasse, von, bis, text, erstellt, benutzer_id) VALUES (@d, @s, @k, '2026-08-01', '2027-07-31', '', '2026-10-14T08:00:00', NULL)",
            UntPlanWeb.Daten.Db.P("@d", id), UntPlanWeb.Daten.Db.P("@s", schuelerId), UntPlanWeb.Daten.Db.P("@k", klasse));
    }

    public async ValueTask DisposeAsync()
    {
        await _mobile.StopAsync();
        await _mobile.DisposeAsync();
        await _web.StopAsync();
        await _web.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_ordner, true); } catch (IOException) { }
    }
}
