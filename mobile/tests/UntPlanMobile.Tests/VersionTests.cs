using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UntPlanMobile.Tests;

/// <summary>UM-01: Versionsanzeige und Updateprüfung von UntPlanMobile, Anzeige in der Administration von UntPlanWeb.</summary>
public class VersionTests
{
    [Fact]
    public async Task Version_und_Update_werden_angezeigt()
    {
        // Ersatz für die Versionsquelle (GitHub-Releases)
        var qb = WebApplication.CreateBuilder();
        qb.Logging.ClearProviders();
        qb.WebHost.UseUrls("http://127.0.0.1:0");
        await using var quelle = qb.Build();
        quelle.MapGet("/release", () => Results.Text("""{"tag_name":"v99.0.0","html_url":"https://example.org/r"}""", "application/json"));
        await quelle.StartAsync();
        var quellAdresse = quelle.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Environment.SetEnvironmentVariable("UNTPLANMOBILE_UPDATE_URL", quellAdresse + "/release");
        try
        {
            await using var u = await Umgebung.StartenAsync();
            var v = await Umgebung.HolenAsync(u.Client(), "/api/version");
            Assert.Equal("UntPlanMobile", (string?)v["produkt"]);
            Assert.False(string.IsNullOrEmpty((string?)v["version"]));
            Assert.Equal("99.0.0", (string?)v["neueste"]);
            Assert.True((bool)v["updateVerfuegbar"]!);

            // UntPlanWeb zeigt die Version von UntPlanMobile in der Administration (UNTPLANWEB_MOBILE_INTERN_URL)
            var ordner = Path.Combine(Path.GetTempPath(), "untplanmobile-version-" + Guid.NewGuid().ToString("N"));
            var web = UntPlanWeb.Program.Erstellen(new[] { "--contentRoot", Path.Combine(Umgebung.Wurzel(), "web", "src", "UntPlanWeb") }, b =>
            {
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UNTPLANWEB_DATEN"] = ordner,
                    ["UNTPLANWEB_ADMIN_PASSWORT"] = "admin-passwort",
                    ["UNTPLANWEB_UPDATE_PRUEFEN"] = "0",
                    ["UNTPLANWEB_MOBILE_INTERN_URL"] = u.MobilAdresse.ToString(),
                    ["Logging:LogLevel:Default"] = "Warning",
                });
                b.WebHost.UseUrls("http://127.0.0.1:0");
            });
            await web.StartAsync();
            try
            {
                var adresse = new Uri(web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
                using var c = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = adresse };
                c.DefaultRequestHeaders.Add("X-UntPlanWeb", "1");
                (await c.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = "admin-passwort" })).EnsureSuccessStatusCode();
                var a = JsonNode.Parse(await c.GetStringAsync("/api/app/admin/update"))!;
                Assert.Equal((string?)v["version"], (string?)a["mobil"]?["version"]);
                Assert.True((bool)a["mobil"]!["updateVerfuegbar"]!);
            }
            finally
            {
                await web.StopAsync();
                await web.DisposeAsync();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(ordner, true); } catch (IOException) { }
            }
        }
        finally { Environment.SetEnvironmentVariable("UNTPLANMOBILE_UPDATE_URL", null); }
    }
}
