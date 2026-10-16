using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UntPlanMobile.Dienste;

/// <summary>
/// App-Hülle (statt shell.php/index.php der Vorlage): Vorlagen/index.html mit eingebetteter Symbolsammlung und den
/// Startdaten (window.TT_BOOT), sowie der Service Worker mit der aktuellen Fassung im Cachenamen.
/// Die Fassung ist ein Prüfwert über alle Dateien der Oberfläche – jede Änderung erneuert den Cache auf den Geräten.
/// </summary>
public sealed class Huelle
{
    private readonly string _index;
    private readonly string _sw;

    public string Fassung { get; }

    public Huelle(IWebHostEnvironment env)
    {
        var vorlagen = Path.Combine(env.ContentRootPath, "Vorlagen");
        var sprite = File.ReadAllText(Path.Combine(env.WebRootPath, "assets", "icons", "symbole-sprite.svg"));
        Fassung = FassungBerechnen(env.WebRootPath, vorlagen);
        _index = File.ReadAllText(Path.Combine(vorlagen, "index.html")).Replace("<!--SYMBOLE-->", sprite).Replace("__FASSUNG__", Fassung);
        _sw = File.ReadAllText(Path.Combine(vorlagen, "sw.js")).Replace("__FASSUNG__", Fassung);
    }

    private static string FassungBerechnen(params string[] ordner)
    {
        using var sha = SHA256.Create();
        foreach (var datei in ordner.SelectMany(o => Directory.EnumerateFiles(o, "*", SearchOption.AllDirectories)).OrderBy(x => x, StringComparer.Ordinal))
        {
            var inhalt = File.ReadAllBytes(datei);
            sha.TransformBlock(inhalt, 0, inhalt.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var version = typeof(Huelle).Assembly.GetName().Version;
        return $"{version?.Major}.{version?.Minor}.{version?.Build}-{Convert.ToHexString(sha.Hash!)[..8].ToLowerInvariant()}";
    }

    /// <summary>Seite mit Startdaten; das Skript bekommt eine Nonce für die Content-Security-Policy.</summary>
    public string Seite(object boot, string nonce, string titel)
    {
        // Standardkodierung von System.Text.Json maskiert <, > und &, damit kann das JSON nicht aus dem Skript ausbrechen
        var json = JsonSerializer.Serialize(boot, Program.JsonAusgabe);
        return _index.Replace("__NONCE__", nonce).Replace("__TITEL__", System.Net.WebUtility.HtmlEncode(titel)).Replace("__BOOT__", json);
    }

    public string ServiceWorker => _sw;

    public static string NeueNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}
