using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using UntPlanMobile.Api;
using UntPlanMobile.Dienste;

namespace UntPlanMobile;

public class Program
{
    /// <summary>JSON-Ausgabe an die Oberfläche: camelCase, leere (null) Felder werden weggelassen.</summary>
    public static readonly JsonSerializerOptions JsonAusgabe = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static int Main(string[] args)
    {
        // Gesundheitsprüfung für Docker (das Laufzeit-Image enthält weder curl noch wget)
        if (args.Contains("--gesundheitspruefung")) return Gesundheitspruefung();
        Erstellen(args).Run();
        return 0;
    }

    private static int Gesundheitspruefung()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8081";
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            return c.GetAsync($"http://127.0.0.1:{port}/gesundheit").Result.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception) { return 1; }
    }

    /// <summary>Anwendung aufbauen (auch für die Tests mit eigener Konfiguration).</summary>
    public static WebApplication Erstellen(string[] args, Action<WebApplicationBuilder>? anpassen = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        anpassen?.Invoke(builder);
        var adresse = builder.Configuration["UNTPLANWEB_URL"] is { Length: > 0 } u ? u : "http://localhost:8080";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["UNTPLANMOBILE_ZEITZONE"] is { Length: > 0 } z ? z : "Europe/Berlin");

        builder.Services.AddMemoryCache();
        // Datenordner: Profile je Gerät (verschlüsselte Sitzungen), Push-Abos, VAPID-Schlüssel und Schlüssel der Data Protection
        var daten = new Datenordner(builder.Configuration);
        builder.Services.AddSingleton(daten);
        builder.Services.AddDataProtection().SetApplicationName("UntPlanMobile")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(daten.Pfad, "schluessel")));
        builder.Services.AddSingleton<Kontenspeicher>();
        builder.Services.AddSingleton<Vapid>();
        builder.Services.AddSingleton<Pushwache>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Pushwache>());
        builder.Services.AddHttpClient("push", c => c.Timeout = TimeSpan.FromSeconds(20));
        builder.Services.AddSingleton(new Zeitgeber(zone));
        builder.Services.AddSingleton<Huelle>();
        builder.Services.AddTransient<Kontodienst>();
        builder.Services.AddTransient<Wochendienst>();
        builder.Services.AddHttpClient("update");
        builder.Services.AddSingleton(sp => VersionApi.PruefungErstellen(sp, builder.Configuration));
        builder.Services.AddHttpClient<UntPlanWebVerbindung>(c =>
            {
                c.BaseAddress = new Uri(adresse.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h["X-Frame-Options"] = "DENY";
            await next();
        });
        // Schutz gegen fremde Seiten: ändernde Aufrufe nur von derselben Herkunft
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
                && ctx.Request.Headers.Origin is { Count: > 0 } origin
                && !(Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var o) && string.Equals(o.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Ungültige Herkunft." });
                return;
            }
            await next();
        });
        // Fehler der Verbindung zu UntPlanWeb als JSON, wie die Oberfläche es erwartet
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (NichtAngemeldetException) when (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "not_authenticated" });
            }
            catch (UntPlanWebFehler ex) when (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = ex.Status is >= 400 and < 600 ? ex.Status : 502;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = ex.Message, detail = ex.Message });
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException && !ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Ungültige Daten." });
            }
        });
        // API-Antworten nie zwischenspeichern (der Service Worker hält selbst eine Kopie für den Offlinebetrieb)
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
            await next();
        });

        // App-Hülle mit Startdaten
        app.MapGet("/", Seite);
        app.MapGet("/index.html", Seite);
        app.MapGet("/sw.js", (Huelle h, HttpContext c) =>
        {
            c.Response.Headers.CacheControl = "no-cache";
            c.Response.Headers["Service-Worker-Allowed"] = "/";
            return Results.Text(h.ServiceWorker, "text/javascript; charset=utf-8");
        });
        app.MapGet("/gesundheit", () => Results.Ok(new { ok = true }));

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                var n = c.File.Name;
                // Skripte, Stile und Manifest immer nachfragen (ETag) – Symbole dürfen einen Tag liegen bleiben
                c.Context.Response.Headers.CacheControl = n.EndsWith(".png") || n.EndsWith(".svg") ? "public, max-age=86400" : "no-cache";
            },
        });

        MobilApi.Abbilden(app);
        KlassenbuchApi.Abbilden(app);
        RaumApi.Abbilden(app);
        StartseiteApi.Abbilden(app);
        MitteilungenApi.Abbilden(app);
        SprechtagApi.Abbilden(app);
        EntschuldigungenApi.Abbilden(app);
        PushApi.Abbilden(app);
        SsoApi.Abbilden(app);
        VersionApi.Abbilden(app);
        return app;
    }

    private static async Task<IResult> Seite(HttpContext c, Huelle huelle, Wochendienst wochen, string? type, string? id, string? date)
    {
        var boot = await wochen.BootAsync(c, type, id, date);
        var nonce = Huelle.NeueNonce();
        c.Response.Headers.CacheControl = "no-store";
        c.Response.Headers["Content-Security-Policy"] =
            $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; " +
            "manifest-src 'self'; worker-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        return Results.Content(huelle.Seite(boot, nonce, "UntPlanMobile"), "text/html; charset=utf-8");
    }
}
