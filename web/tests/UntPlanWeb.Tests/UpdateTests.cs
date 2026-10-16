using Microsoft.Extensions.Configuration;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Updateanzeige und -auslösung (UW-02): Versionsquelle, Admin-Endpunkte, Updater-Modi.</summary>
public class UpdateTests
{
    /// <summary>Kleiner Ersatz für GitHub und Watchtower im Testprozess.</summary>
    private sealed class Gegenstelle : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public int WatchtowerAufrufe;
        public string Adresse { get; }

        public Gegenstelle()
        {
            var b = WebApplication.CreateBuilder();
            b.Logging.ClearProviders();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            _app = b.Build();
            _app.MapGet("/release", () => Results.Text("""{"tag_name":"v99.1.0","html_url":"https://example.org/release","draft":false,"prerelease":false}""", "application/json"));
            _app.MapPost("/v1/update", (HttpContext c) =>
            {
                if (c.Request.Headers.Authorization != "Bearer geheim") return Results.Unauthorized();
                Interlocked.Increment(ref WatchtowerAufrufe);
                return Results.Ok();
            });
            _app.StartAsync().GetAwaiter().GetResult();
            Adresse = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    [Fact]
    public void Versionsquelle_wird_ausgewertet()
    {
        Assert.Equal(("1.2.3", "https://x/r"), Updatepruefung.Auswerten("""{"tag_name":"v1.2.3","html_url":"https://x/r"}"""));
        Assert.Equal("2.0.0", Updatepruefung.Auswerten("""[{"tag_name":"v1.9.0"},{"tag_name":"v2.0.0"},{"tag_name":"v3.0.0","prerelease":true}]""").Version);
        Assert.Equal("1.0.12", Updatepruefung.Auswerten("""{"name":"untplanweb","tags":["latest","1.0.9","1.0.12","1.0.10"]}""").Version);
        Assert.Equal("1.0.7", Updatepruefung.Auswerten("""[{"name":"v1.0.54"},{"name":"web-v1.0.7"}]""", "web-v").Version);
        Assert.Equal("4.5.6", Updatepruefung.Auswerten("4.5.6\n").Version);
        Assert.Null(Updatepruefung.Auswerten("[]").Version);
        Assert.True(Updatepruefung.Vergleichen("1.0.10", "1.0.9") > 0);
        Assert.Equal(0, Updatepruefung.Vergleichen("v1.0", "1.0.0"));
        Assert.False(Updatepruefung.Schalter("0", true));
        Assert.True(Updatepruefung.Schalter(null, true));
        // Registry-Seiten (Link-Kopf) und Standardquelle des Web-Images (nicht die Desktop-Releases)
        Assert.Equal("https://ghcr.io/v2/a/b/tags/list?last=1.0.99&n=1000", Updatepruefung.NaechsteSeite("</v2/a/b/tags/list?last=1.0.99&n=1000>; rel=\"next\""));
        Assert.Null(Updatepruefung.NaechsteSeite(null));
        Assert.Equal("ghcr://florians25/untplanweb", Updateeinstellungen.Laden(new ConfigurationBuilder().Build()).Url);
    }

    [Fact]
    public async Task Updatehinweis_nur_fuer_Administration()
    {
        await using var g = new Gegenstelle();
        Testzusatz.Mit(new() { ["UNTPLANWEB_UPDATE_URL"] = g.Adresse + "/release" });
        await using var s = await Testserver.StartenAsync(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync("/api/app/admin/update")).StatusCode);
        var lehrer = await s.AlsAsync("mue", "lehrer", "MUE");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/update")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsync("/api/app/admin/update/installieren", null)).StatusCode);

        var admin = await s.AlsAsync("admin");
        var u = await Testserver.HolenAsync(admin, "/api/app/admin/update");
        Assert.Equal("99.1.0", (string?)u["neueste"]);
        Assert.True((bool)u["updateVerfuegbar"]!);
        Assert.Equal(s.Dienst<UntPlanWeb.Daten.Db>().Art == UntPlanWeb.Daten.Datenbankart.MySql ? "MySQL/MariaDB" : "SQLite", (string?)u["datenbank"]);
        Assert.False(string.IsNullOrEmpty((string?)u["version"]));
        Assert.Equal("aus", (string?)u["modus"]);
        Assert.False((bool)u["installierbar"]!);
        // ohne Updater: Hinweis auf den Befehl
        var r = await admin.PostAsync("/api/app/admin/update/installieren", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("docker compose pull", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pruefung_abschaltbar()
    {
        Testzusatz.Mit(new() { ["UNTPLANWEB_UPDATE_PRUEFEN"] = "0" });
        await using var s = await Testserver.StartenAsync(false);
        var u = await Testserver.HolenAsync(await s.AlsAsync("admin"), "/api/app/admin/update");
        Assert.False((bool)u["pruefungAktiv"]!);
        Assert.False((bool)u["updateVerfuegbar"]!);
    }

    [Fact]
    public async Task Update_per_Anforderungsdatei_mit_Sicherung()
    {
        Testzusatz.Mit(new() { ["UNTPLANWEB_UPDATE_PRUEFEN"] = "0", ["UNTPLANWEB_UPDATE_MODUS"] = "datei" });
        await using var s = await Testserver.StartenAsync(false);
        var admin = await s.AlsAsync("admin");
        // ohne CSRF-Kopf abgelehnt
        var ohne = s.Client(false);
        ohne.DefaultRequestHeaders.Remove("X-UntPlanWeb");
        Assert.Equal(HttpStatusCode.BadRequest, (await ohne.PostAsync("/api/app/admin/update/installieren", null)).StatusCode);

        var r = await Testserver.JsonAsync(await admin.PostAsync("/api/app/admin/update/installieren", null));
        Assert.True((bool)r["ok"]!);
        var ordner = s.Dienst<Konfiguration>().Datenordner;
        Assert.True(File.Exists(Path.Combine(ordner, "update", "angefordert.json")));
        // SQLite wird vorher gesichert (MySQL/MariaDB: mariadb-dump über update.sh)
        if (s.Dienst<UntPlanWeb.Daten.Db>().Art == UntPlanWeb.Daten.Datenbankart.Sqlite)
            Assert.Single(Directory.GetFiles(Path.Combine(ordner, "sicherungen"), "untplanweb-*.db"));
        var u = await Testserver.HolenAsync(admin, "/api/app/admin/update");
        Assert.NotNull((string?)u["angefordert"]);
    }

    [Fact]
    public async Task Update_per_Watchtower()
    {
        await using var g = new Gegenstelle();
        Testzusatz.Mit(new() { ["UNTPLANWEB_UPDATE_PRUEFEN"] = "0", ["UNTPLANWEB_UPDATE_WATCHTOWER_URL"] = g.Adresse + "/v1/update", ["UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN"] = "geheim" });
        await using (var s = await Testserver.StartenAsync(false))
        {
            var admin = await s.AlsAsync("admin");
            var u = await Testserver.HolenAsync(admin, "/api/app/admin/update");
            Assert.Equal("watchtower", (string?)u["modus"]);
            await Testserver.JsonAsync(await admin.PostAsync("/api/app/admin/update/installieren", null));
            Assert.Equal(1, g.WatchtowerAufrufe);
        }
        Testzusatz.Mit(new() { ["UNTPLANWEB_UPDATE_PRUEFEN"] = "0", ["UNTPLANWEB_UPDATE_WATCHTOWER_URL"] = g.Adresse + "/v1/update", ["UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN"] = "falsch" });
        await using (var s = await Testserver.StartenAsync(false))
        {
            var r = await (await s.AlsAsync("admin")).PostAsync("/api/app/admin/update/installieren", null);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("Token", await r.Content.ReadAsStringAsync());
        }
    }
}
