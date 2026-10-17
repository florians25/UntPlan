using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using UntPlanStudio.Api;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio;

/// <summary>
/// UntPlan Studio (Beta): Server für die Browser-Fassung der Desktop-Anwendung. Aufbau siehe ARCHITEKTUR.md.
/// </summary>
public partial class Program
{
    public const string Anwendungsname = "UntPlan Studio (Beta)";
    /// <summary>Kopfzeile, die jede ändernde API-Anfrage tragen muss (Schutz gegen Cross-Site-Anfragen).</summary>
    public const string Schutzkopf = "X-UntPlan-Studio";

    public static int Main(string[] args)
    {
        // Gesundheitsprüfung für Docker (das Laufzeit-Image enthält weder curl noch wget)
        if (args.Contains("--gesundheitspruefung")) return Gesundheitspruefung();
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

    /// <summary>Anwendung aufbauen. Die Konfiguration wird erst beim Start gelesen (damit Tests sie überschreiben können).</summary>
    public static WebApplication Erstellen(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var dienste = builder.Services;

        dienste.AddSingleton(sp => StudioKonfiguration.Laden(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath));
        dienste.AddSingleton<Benutzerdienst>();
        dienste.AddSingleton<Benutzereinstellungen>();
        dienste.AddSingleton<Dokumentdienst>();
        dienste.AddHostedService<Autospeicherung>();
        dienste.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        dienste.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            // ausdrücklich ohne text/event-stream (Ereignisse dürfen nicht gepuffert werden)
            o.MimeTypes = new[] { "application/json", "text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml" };
        });
        dienste.AddOptions<FormOptions>().Configure<StudioKonfiguration>((o, k) => o.MultipartBodyLengthLimit = k.MaxUploadMb * 1024L * 1024L);
        dienste.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });

        // Schlüssel für das Anmeldecookie im Datenordner (überstehen Neustarts und Container-Updates)
        dienste.AddDataProtection().SetApplicationName("UntPlanStudio");
        dienste.AddOptions<KeyManagementOptions>().Configure<StudioKonfiguration>((o, k) =>
            o.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
                new DirectoryInfo(Path.Combine(k.Datenordner, "schluessel")), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));

        dienste.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "UntPlanStudio";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;
            // API: 401/403 statt Weiterleitung auf eine Anmeldeseite
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        dienste.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<StudioKonfiguration>((o, k) =>
            o.Cookie.SecurePolicy = k.SichereCookies switch { true => CookieSecurePolicy.Always, false => CookieSecurePolicy.None, _ => CookieSecurePolicy.SameAsRequest });
        dienste.AddAuthorization();

        foreach (var modul in Modulregister.Alle) modul.DiensteRegistrieren(dienste);

        var app = builder.Build();
        Konfigurieren(app);
        return app;
    }

    private static void Konfigurieren(WebApplication app)
    {
        // Konfiguration früh laden (legt den Datenordner an, Admin-Konto beim ersten Start)
        app.Services.GetRequiredService<Benutzerdienst>();

        app.UseForwardedHeaders();
        app.UseMiddleware<Fehlerbehandlung>();
        app.Use(async (ctx, weiter) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await weiter();
        });
        app.UseResponseCompression();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // kein Build-Schritt: Skripte und Stile immer neu prüfen (ETag), damit Updates sofort ankommen
            OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
        });
        app.UseAuthentication();
        app.UseAuthorization();

        // Schutz für ändernde API-Anfragen: eigene Kopfzeile (Cross-Site-Formulare können sie nicht setzen) und Schreibrecht
        app.Use(async (ctx, weiter) =>
        {
            var r = ctx.Request;
            if (r.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(r.Method) && !HttpMethods.IsHead(r.Method) && !HttpMethods.IsOptions(r.Method))
            {
                if (!r.Headers.ContainsKey(Schutzkopf))
                    throw new StudioFehler(400, $"Die Kopfzeile {Schutzkopf} fehlt.");
                if (ctx.User.Identity?.IsAuthenticated == true && !Rollen.DarfSchreiben(ctx.User) && !Leserpfad(r.Path))
                    throw StudioFehler.Verboten("Ihr Konto darf nur lesen.");
            }
            await weiter();
        });

        app.MapGet("/gesundheit", () => Results.Ok(new { status = "ok", anwendung = Anwendungsname }));

        var offen = app.MapGroup("/api");
        var api = app.MapGroup("/api").RequireAuthorization();
        var dokument = api.MapGroup("/dokumente/{dok}");
        // jede Antwort eines Dokument-Endpunkts trägt den aktuellen Änderungsstand
        dokument.AddEndpointFilter(async (ctx, weiter) =>
        {
            var erg = await weiter(ctx);
            var id = ctx.HttpContext.Request.RouteValues["dok"] as string;
            if (!ctx.HttpContext.Response.HasStarted && Dokumentdienst.IdGueltig(id) && ctx.HttpContext.RequestServices.GetRequiredService<Dokumentdienst>().Offene.FirstOrDefault(d => d.Id == id) is { } a)
            {
                ctx.HttpContext.Response.Headers["X-Dokument-Version"] = a.Version.ToString();
                ctx.HttpContext.Response.Headers["X-Dokument-Bereich"] = a.LetzterBereich;
            }
            return erg;
        });
        var routen = new StudioRouten(offen, api, dokument);
        foreach (var modul in Modulregister.Alle) modul.EndpunkteRegistrieren(routen);
        app.MapGet("/api/module", () => Modulregister.Alle.Select(m => m.Name)).RequireAuthorization();
        app.MapFallbackToFile("index.html");
    }

    /// <summary>Ändernde Anfragen, die auch Nur-Lesen-Konten stellen dürfen (Abmelden, eigenes Passwort, eigene Einstellungen).</summary>
    private static bool Leserpfad(PathString p) =>
        p.StartsWithSegments("/api/abmeldung") || p.StartsWithSegments("/api/anmeldung") || p.StartsWithSegments("/api/ich") || p.StartsWithSegments("/api/einstellungen");
}
