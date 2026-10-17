using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using UntPlanWeb.Api;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb;

public class Program
{
    public static int Main(string[] args)
    {
        // Gesundheitsprüfung für Docker (das Laufzeit-Image enthält weder curl noch wget)
        if (args.Contains("--gesundheitspruefung")) return Gesundheitspruefung();
        // Kommandozeile: SQLite-Daten nach MySQL/MariaDB übernehmen bzw. sichern (siehe README, „Datenmigration“ und „Update“)
        if (args.Contains(Datenmigration.Befehlsname) || args.Contains(Sicherung.Befehlsname))
        {
            var konfig = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
            return args.Contains(Sicherung.Befehlsname) ? Sicherung.Befehl(konfig) : Datenmigration.Befehl(args, konfig);
        }
        Erstellen(args).Run();
        return 0;
    }

    private static int Gesundheitspruefung()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8080";
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            return c.GetAsync($"http://127.0.0.1:{port}/gesundheit").Result.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception) { return 1; }
    }

    /// <summary>Anwendung aufbauen (auch für die Tests mit eigener Konfiguration und Uhr).</summary>
    public static WebApplication Erstellen(string[] args, Action<WebApplicationBuilder>? anpassen = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        anpassen?.Invoke(builder);
        var konfig = Konfiguration.Laden(builder.Configuration);
        // SQLite (Standard) oder MySQL/MariaDB über UNTPLANWEB_DB / UNTPLANWEB_DB_CONNECTION; Migrationen laufen beim Start
        var db = Datenbankkonfiguration.Laden(builder.Configuration).Oeffnen(konfig.Datenordner);

        builder.Services.AddSingleton(konfig);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<Uhr>();
        builder.Services.AddSingleton<Einstellungen>();
        builder.Services.AddSingleton<Protokoll>();
        builder.Services.AddSingleton<Benutzerdienst>();
        builder.Services.AddSingleton<Paketdienst>();
        builder.Services.AddSingleton<Plandienst>();
        builder.Services.AddSingleton<Vertretungsplandienst>();
        builder.Services.AddSingleton<Rueckmeldungsdienst>();
        builder.Services.AddSingleton<Mitteilungsdienst>();
        builder.Services.AddSingleton<Klassenbuchdienst>();
        builder.Services.AddSingleton<Listendienst>();
        builder.Services.AddSingleton<Monitordienst>();
        builder.Services.AddSingleton<Verwaltungsdienst>();
        builder.Services.AddSingleton<Klassenbuchverwaltung>();
        builder.Services.AddSingleton<Entschuldigungsdienst>();
        builder.Services.AddSingleton<Sitzplandienst>();
        builder.Services.AddHttpClient("update");
        builder.Services.AddSingleton(Updateeinstellungen.Laden(builder.Configuration));
        builder.Services.AddSingleton<Updatedienst>();
        builder.Services.AddSingleton<Schuelerverwaltung>();
        builder.Services.AddSingleton<Buchungsdienst>();
        builder.Services.AddSingleton<Personendienst>();
        builder.Services.AddSingleton<Sprechtagdienst>();
        builder.Services.AddSingleton<SchuelerImport>();
        builder.Services.AddSingleton<Pushdienst>();
        builder.Services.AddSingleton<Notendienst>();
        // Klassenbuch: Erinnerung an offene Stunden und automatische Entschuldigung (Hintergrunddienst)
        builder.Services.AddSingleton<Klassenbucherinnerung>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Klassenbucherinnerung>());
        builder.Services.AddHostedService<Sprechtagerinnerung>();
        builder.Services.AddSingleton<Unterrichtsgruppen>();
        builder.Services.AddSingleton<Systemwartung>();

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
            // leere Datumsfelder aus Formularen ("") als „kein Datum“ lesen statt die Anfrage mit 400 abzulehnen
            o.SerializerOptions.Converters.Add(new LeeresDatum());
        });
        // Schlüssel für Anmelde-Cookies im Datenordner, damit Anmeldungen einen Neustart des Containers überstehen
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(konfig.Datenordner, "schluessel"))).SetApplicationName("UntPlanWeb");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "untplanweb";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            // API: 401 statt Weiterleitung auf eine Anmeldeseite
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            // gesperrte bzw. gelöschte Benutzer verlieren ihre Sitzung sofort
            o.Events.OnValidatePrincipal = c =>
            {
                var dienst = c.HttpContext.RequestServices.GetRequiredService<Benutzerdienst>();
                if (!int.TryParse(c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || dienst.Holen(id) is not { Gesperrt: false })
                    c.RejectPrincipal();
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });
        // große Pakete (ganzes Schuljahr) zulassen
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024 * 1024);

        var app = builder.Build();
        // Sofort-Benachrichtigungen hängen sich an den Paketdienst (Versand nach jedem Paket-Import)
        app.Services.GetRequiredService<Pushdienst>();
        app.UseForwardedHeaders();
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            // App-Daten nie zwischenspeichern: Änderungen (z. B. Abwesenheiten ↔ Fehlzeiten) sind sofort überall sichtbar; Downloads setzen eigene Werte
            if (ctx.Request.Path.StartsWithSegments("/api/app")) h.CacheControl = "no-store";
            await next();
        });
        // Schutz gegen fremde Seiten (CSRF): ändernde App-Aufrufe brauchen einen eigenen Kopf, den nur unser Frontend setzt
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api/app") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) &&
                ctx.Request.Headers["X-UntPlanWeb"] != "1")
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { fehler = "Ungültige Anfrage." });
                return;
            }
            await next();
        });
        // Fachliche Fehler als JSON
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or FormatException or UnauthorizedAccessException or JsonException)
            {
                if (ctx.Response.HasStarted) throw;
                ctx.Response.Clear();
                ctx.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : 400;
                await ctx.Response.WriteAsJsonAsync(new { fehler = ex is JsonException ? "Ungültige Daten." : ex.Message });
            }
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // Oberfläche ohne Build-Schritt: HTML, Skripte, Stile und JSON (z. B. Neuigkeiten) immer neu prüfen (ETag), damit nach einem Update nichts Veraltetes läuft
            OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = c.File.Name.EndsWith(".html") || c.File.Name.EndsWith(".js") || c.File.Name.EndsWith(".css") || c.File.Name.EndsWith(".json") || c.File.Name.EndsWith(".webmanifest") ? "no-cache" : "public, max-age=3600",
        });
        app.UseAuthentication();
        app.UseAuthorization();

        SyncApi.Abbilden(app);
        AppApi.Abbilden(app);
        AdminApi.Abbilden(app);
        SsoApi.Abbilden(app);
        ErweiterungApi.Abbilden(app);
        UpdateApi.Abbilden(app);
        SchriftApi.Abbilden(app);
        MobilApi.Abbilden(app);
        OberflaecheApi.Abbilden(app);
        FamilieApi.Abbilden(app);
        MitteilungApi.Abbilden(app);
        BuchungApi.Abbilden(app);
        BenutzerverwaltungApi.Abbilden(app);
        InfocenterApi.Abbilden(app);
        SprechtagApi.Abbilden(app);
        HinweisApi.Abbilden(app);
        OffeneStundenApi.Abbilden(app);
        RechteApi.Abbilden(app);
        SchuelerImportApi.Abbilden(app);
        PushApi.Abbilden(app);
        KlassenbuchEinstellungenApi.Abbilden(app);
        EntschuldigungApi.Abbilden(app);
        UnterrichtKlassenApi.Abbilden(app);
        WartungApi.Abbilden(app);
        app.MapGet("/monitor/{token}", (string token, IWebHostEnvironment env) => env.WebRootFileProvider.GetFileInfo("monitor.html") is { Exists: true, PhysicalPath: { } pfad }
            ? Results.File(pfad, "text/html; charset=utf-8") : Results.NotFound());
        app.MapGet("/gesundheit", () => Results.Ok(new { ok = true }));

        Einrichten(app.Services, konfig);
        return app;
    }

    /// <summary>Automatische Einrichtung über Umgebungsvariablen und Beispielpaket.</summary>
    private static void Einrichten(IServiceProvider sp, Konfiguration k)
    {
        var e = sp.GetRequiredService<Einstellungen>();
        var benutzer = sp.GetRequiredService<Benutzerdienst>();
        if (k.SchulId != null) e.Schreiben("schul_id", Sicherheit.Slug(k.SchulId));
        if (k.ApiSchluessel != null) e.ApiSchluesselSetzen(k.ApiSchluessel);
        if (benutzer.Keiner && k.AdminPasswort != null)
        {
            benutzer.Anlegen("admin", k.AdminPasswort, Rollen.Admin, "", "Administration");
            if (e.SchulId.Length == 0) e.Schreiben("schul_id", "schule");
        }
        if (k.Demo)
        {
            var datei = Path.Combine(AppContext.BaseDirectory, "demo", "beispiel-paket.json");
            var pakete = sp.GetRequiredService<Paketdienst>();
            if (pakete.DemoLaden(datei) && e.Schulname.Length == 0) e.Schreiben("schulname", pakete.Aktuell!.Paket.Schule.Name);
            // Demo: Beispiel-Ressourcen (Beamer, Laptopwagen, Tablet-Koffer …) und Raumdaten (Gebäude, Abteilung), solange keine gepflegt sind
            if (pakete.Aktuell is { } demo)
            {
                var bu = sp.GetRequiredService<Buchungsdienst>();
                bu.BeispieleAnlegen(demo);
                bu.BeispielRaumdatenAnlegen(demo);
            }
        }
    }
}

/// <summary>Hilfen für die Endpunkte: angemeldeter Benutzer, Rechte, Anmeldung.</summary>
public static class Kontext
{
    public static Benutzer? Ich(this HttpContext c) =>
        int.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? c.RequestServices.GetRequiredService<Benutzerdienst>().Holen(id) is { Gesperrt: false } b ? b : null : null;

    public static Benutzer Pflicht(this HttpContext c) => c.Ich() ?? throw new UnauthorizedAccessException("Bitte anmelden.");

    public static async Task AnmeldenAsync(this HttpContext c, Benutzer b)
    {
        // Zugriff gesperrt (Wartungsmodus): neue Anmeldungen nur für die Administration
        c.RequestServices.GetService<Systemwartung>()?.AnmeldungPruefen(b);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, b.Id.ToString()), new(ClaimTypes.Name, b.Login), new(ClaimTypes.Role, b.Rolle),
            new(Systemwartung.SitzungsClaim, Guid.NewGuid().ToString("N")) };
        await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });
    }
}

/// <summary>DateOnly? aus JSON: "" bzw. Leerzeichen = null (Formularfelder), sonst ISO-Datum.</summary>
internal sealed class LeeresDatum : System.Text.Json.Serialization.JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref System.Text.Json.Utf8JsonReader r, Type t, System.Text.Json.JsonSerializerOptions o)
    {
        if (r.TokenType == System.Text.Json.JsonTokenType.Null) return null;
        var s = r.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : throw new System.Text.Json.JsonException("Ungültiges Datum.");
    }

    public override void Write(System.Text.Json.Utf8JsonWriter w, DateOnly? v, System.Text.Json.JsonSerializerOptions o)
    {
        if (v is DateOnly d) w.WriteStringValue(d.ToString("yyyy-MM-dd")); else w.WriteNullValue();
    }
}

