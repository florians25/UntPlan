using System.Text;
using System.Text.RegularExpressions;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Hausschrift der Oberfläche (z. B. AvenirNextPro): Die lizenzierten Schriftdateien werden nicht mit ausgeliefert, sondern von der
/// Administration hochgeladen und im Datenordner (Unterordner „schriften“) abgelegt. <c>/css/schrift.css</c> erzeugt daraus die
/// @font-face-Regeln (Schriftschnitt aus dem Dateinamen), damit die Schrift auf allen Geräten und in allen Browsern gleich aussieht.
/// Ohne hochgeladene Dateien bleibt es bei der freien Ersatzschrift.
/// </summary>
public static partial class SchriftApi
{
    public const string Familie = "AvenirNextPro";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,80}\.(otf|ttf|woff2?)$", RegexOptions.IgnoreCase)]
    private static partial Regex Dateiname();

    private static string Ordner(Konfiguration k) => Path.Combine(k.Datenordner, "schriften");

    /// <summary>Gewicht und Stil aus dem Dateinamen (AvenirNextLTPro-DemiIt.otf → 600 kursiv).</summary>
    public static (int Gewicht, bool Kursiv) Schnitt(string datei)
    {
        var n = Path.GetFileNameWithoutExtension(datei).ToLowerInvariant();
        var teil = n.Contains('-') ? n[(n.LastIndexOf('-') + 1)..] : n;
        var kursiv = teil.EndsWith("it") || teil.Contains("italic");
        teil = teil.Replace("italic", "");
        if (teil.EndsWith("it")) teil = teil[..^2];
        var gewicht = teil switch
        {
            var t when t.StartsWith("ultlt") || t.StartsWith("ultralight") || t.StartsWith("thin") => 200,
            var t when t.StartsWith("light") => 300,
            var t when t.StartsWith("medium") => 500,
            var t when t.StartsWith("demi") || t.StartsWith("semibold") => 600,
            var t when t.StartsWith("bold") => 700,
            var t when t.StartsWith("heavy") || t.StartsWith("black") || t.StartsWith("extrabold") => 800,
            _ => 400,
        };
        return (gewicht, kursiv);
    }

    /// <summary>CSS-Gewichte, für die ein Schriftschnitt gilt (eine Stufe leichter als üblich).</summary>
    public static int[] CssGewichte(int schnitt) => schnitt switch
    {
        200 => new[] { 200, 300 },
        500 => new[] { 500, 600 },
        600 => new[] { 700 },
        700 => new[] { 800 },
        800 => new[] { 900 },
        var x => new[] { x },
    };

    private static string Format(string datei) => Path.GetExtension(datei).ToLowerInvariant() switch
    {
        ".woff2" => "woff2", ".woff" => "woff", ".ttf" => "truetype", _ => "opentype",
    };

    private static List<string> Dateien(Konfiguration k) =>
        Directory.Exists(Ordner(k)) ? Directory.GetFiles(Ordner(k)).Select(Path.GetFileName).Where(n => n != null && Dateiname().IsMatch(n)).Select(n => n!).OrderBy(n => n).ToList() : new();

    public static void Abbilden(WebApplication app)
    {
        app.MapGet("/css/schrift.css", (Konfiguration k, HttpContext c, IWebHostEnvironment env) =>
        {
            c.Response.Headers.CacheControl = "no-cache";
            // ältere Installationen: Dateien unter wwwroot/fonts
            if (Dateien(k).Count == 0) return Results.Text(OberflaecheApi.SchriftCss(env.WebRootPath), "text/css; charset=utf-8");
            var sb = new StringBuilder("/* Hausschrift aus dem Datenordner (Administration → Ansichtseinstellungen → Schrift) */\n");
            var dateien = Dateien(k);
            // je Schnitt die kompakteste Datei (woff2 vor woff vor otf/ttf)
            foreach (var g in dateien.GroupBy(Schnitt))
            {
                var quellen = g.OrderBy(d => Format(d) switch { "woff2" => 0, "woff" => 1, _ => 2 })
                    .Select(d => $"url(\"/schriften/{Uri.EscapeDataString(d)}\") format(\"{Format(d)}\")");
                // Avenir Next ist in den fetten Schnitten deutlich kräftiger als die Ersatzschrift: eine Stufe leichter einordnen
                // (halbfett 600 → Medium, fett 700 → Demi, 800 → Bold) – so wirkt die Oberfläche wie in den Mustern, nicht zu fett
                foreach (var css in CssGewichte(g.Key.Gewicht))
                    sb.Append($"@font-face {{ font-family: \"{Familie}\"; font-weight: {css}; font-style: {(g.Key.Kursiv ? "italic" : "normal")}; font-display: swap; src: {string.Join(", ", quellen)}; }}\n");
            }
            if (dateien.Count > 0)
                sb.Append(":root { --u-schrift: \"" + Familie + "\", \"Nunito Sans\", system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif; }\n")
                  .Append("html { -webkit-font-smoothing: antialiased; -moz-osx-font-smoothing: grayscale; text-rendering: optimizeLegibility; font-synthesis: none; }\n");
            return Results.Text(sb.ToString(), "text/css; charset=utf-8");
        });

        app.MapGet("/schriften/{datei}", (string datei, Konfiguration k, HttpContext c) =>
        {
            if (!Dateiname().IsMatch(datei)) return Results.NotFound();
            var pfad = Path.Combine(Ordner(k), datei);
            if (!File.Exists(pfad)) return Results.NotFound();
            c.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(pfad, Format(datei) switch { "woff2" => "font/woff2", "woff" => "font/woff", "truetype" => "font/ttf", _ => "font/otf" });
        });

        var admin = app.MapGroup("/api/app/admin/schriften");
        admin.MapGet("", (HttpContext c, Konfiguration k) =>
        {
            Admin(c);
            return Results.Json(Dateien(k).Select(d => new { datei = d, gewicht = Schnitt(d).Gewicht, kursiv = Schnitt(d).Kursiv, groesse = new FileInfo(Path.Combine(Ordner(k), d)).Length }));
        });
        admin.MapPost("", async (HttpContext c, Konfiguration k) =>
        {
            Admin(c);
            var form = await c.Request.ReadFormAsync();
            if (form.Files.Count == 0) throw new ArgumentException("Bitte Schriftdateien (.otf, .ttf, .woff, .woff2) auswählen.");
            Directory.CreateDirectory(Ordner(k));
            var n = 0;
            foreach (var f in form.Files)
            {
                var name = Path.GetFileName(f.FileName);
                if (!Dateiname().IsMatch(name)) throw new ArgumentException($"„{name}“ ist keine zulässige Schriftdatei.");
                if (f.Length > 5 * 1024 * 1024) throw new ArgumentException($"„{name}“ ist zu groß (höchstens 5 MB).");
                await using var ziel = File.Create(Path.Combine(Ordner(k), name));
                await f.CopyToAsync(ziel);
                n++;
            }
            return Results.Json(new { anzahl = n });
        }).DisableAntiforgery();
        admin.MapDelete("/{datei}", (string datei, HttpContext c, Konfiguration k) =>
        {
            Admin(c);
            if (!Dateiname().IsMatch(datei)) return Results.NotFound();
            var pfad = Path.Combine(Ordner(k), datei);
            if (File.Exists(pfad)) File.Delete(pfad);
            return Results.Json(new { ok = true });
        });
    }

    private static Benutzer Admin(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.");
    }
}
