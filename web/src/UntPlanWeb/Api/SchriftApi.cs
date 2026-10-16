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

    /// <summary>
    /// Die Oberfläche verwendet nur zwei Schnitte: Regular (CSS 100–400) und Medium (CSS 500–900). Demi-/Bold-Dateien werden nicht
    /// geladen, solange ein Medium-Schnitt vorhanden ist; fehlt er, vertritt ihn der leichteste fettere Schnitt.
    /// </summary>
    public static int[] CssGewichte(int schnitt) => schnitt switch
    {
        <= 400 => new[] { 100, 200, 300, 400 },
        _ => new[] { 500, 600, 700, 800, 900 },
    };

    /// <summary>
    /// Vollständige Zuordnung CSS-Gewicht (100–900) → Schriftschnitt: 100–400 → Regular, 500–900 → Medium. So wählt jeder Browser
    /// (Chrome, Firefox, Safari unter macOS/iOS) für jedes Gewicht dieselbe Datei – keine abweichende Fallback-Auswahl, keine Kunstfetten,
    /// keine Demi-/Bold-Schnitte.
    /// </summary>
    public static List<(int Css, int Schnitt)> Zuordnung(IEnumerable<int> schnitte)
    {
        var alle = schnitte.Distinct().OrderBy(x => x).ToList();
        var res = new List<(int, int)>();
        if (alle.Count == 0) return res;
        int? regular = alle.Contains(400) ? 400 : alle.Where(x => x < 400).Select(x => (int?)x).LastOrDefault();
        int? medium = alle.Contains(500) ? 500 : alle.Where(x => x > 500).Select(x => (int?)x).FirstOrDefault()
            ?? alle.Where(x => x > 400 && x < 500).Select(x => (int?)x).FirstOrDefault();
        regular ??= medium;
        medium ??= regular;
        for (var w = 100; w <= 900; w += 100) res.Add((w, w <= 400 ? regular!.Value : medium!.Value));
        return res;
    }

    /// <summary>Schriftstapel mit metrisch ähnlichen Ersatzschriften (Avenir Next unter macOS/iOS, Nunito Sans, Segoe UI …).</summary>
    public const string Stapel = "\"" + Familie + "\", \"Avenir Next\", \"Nunito Sans\", \"Segoe UI\", system-ui, -apple-system, Roboto, \"Helvetica Neue\", Arial, sans-serif";

    /// <summary>
    /// Einheitliche Darstellung: keine künstlichen Fett-/Kursivschnitte. Bewusst KEIN -webkit-font-smoothing/-moz-osx-font-smoothing:
    /// „antialiased“/„grayscale“ lässt Schrift unter macOS deutlich dünner wirken als unter Windows/Linux – die Standard-Kantenglättung
    /// des Betriebssystems ergibt auf allen Geräten dieselbe Strichstärke.
    /// </summary>
    public const string Darstellung = "html { font-synthesis: none; font-synthesis-weight: none; font-synthesis-style: none; }\n";

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
            // nur Regular und Medium: CSS 100–400 → Regular, 500–900 → Medium (keine Demi-/Bold-Dateien) – jedes Gewicht bekommt eine feste Datei
            foreach (var stil in dateien.GroupBy(d => Schnitt(d).Kursiv))
            {
                var jeSchnitt = stil.GroupBy(d => Schnitt(d).Gewicht).ToDictionary(g => g.Key, g => string.Join(", ", g
                    .OrderBy(d => Format(d) switch { "woff2" => 0, "woff" => 1, _ => 2 })
                    .Select(d => $"url(\"/schriften/{Uri.EscapeDataString(d)}\") format(\"{Format(d)}\")")));
                foreach (var (css, schnitt) in Zuordnung(jeSchnitt.Keys))
                    sb.Append($"@font-face {{ font-family: \"{Familie}\"; font-weight: {css}; font-style: {(stil.Key ? "italic" : "normal")}; font-display: swap; src: {jeSchnitt[schnitt]}; }}\n");
            }
            if (dateien.Count > 0)
                sb.Append(":root { --u-schrift: " + Stapel + "; }\n").Append(Darstellung);
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
            return Results.Json(Dateien(k).Select(d => new { datei = d, gewicht = Schnitt(d).Gewicht, cssGewicht = CssGewichte(Schnitt(d).Gewicht)[^1], kursiv = Schnitt(d).Kursiv, groesse = new FileInfo(Path.Combine(Ordner(k), d)).Length }));
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
