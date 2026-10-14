using System.Security.Cryptography;
using System.Text;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Konfiguration aus Umgebungsvariablen (Docker) bzw. appsettings.</summary>
public sealed class Konfiguration
{
    /// <summary>Datenordner mit Datenbank und Schlüsseln (Docker: /data).</summary>
    public string Datenordner { get; init; } = "daten";
    /// <summary>Zeitzone der Schule (für „heute“, Vergangenheit, Monitor).</summary>
    public string Zeitzone { get; init; } = "Europe/Berlin";
    /// <summary>Beim ersten Start das Beispielpaket laden (Ausprobieren ohne UntPlan).</summary>
    public bool Demo { get; init; }
    /// <summary>Optional: Admin-Passwort für die automatische Einrichtung (Benutzer „admin“).</summary>
    public string? AdminPasswort { get; init; }
    /// <summary>Optional: fest vorgegebene Schul-ID und API-Schlüssel (sonst bei der Einrichtung erzeugt).</summary>
    public string? SchulId { get; init; }
    public string? ApiSchluessel { get; init; }
    /// <summary>
    /// Optional: öffentliche Adresse(n) von UntPlanMobile (kommagetrennt) für die Einmalanmeldung (SSO).
    /// Ohne Angabe sind Rücksprünge auf denselben Rechnernamen (anderer Port) erlaubt.
    /// </summary>
    public List<string> MobilAdressen { get; init; } = new();

    public static Konfiguration Laden(IConfiguration c) => new()
    {
        Datenordner = c["UNTPLANWEB_DATEN"] ?? c["Datenordner"] ?? (Directory.Exists("/data") ? "/data" : "daten"),
        Zeitzone = c["UNTPLANWEB_ZEITZONE"] ?? "Europe/Berlin",
        Demo = (c["UNTPLANWEB_DEMO"] ?? "") is "1" or "true" or "ja",
        AdminPasswort = Leer(c["UNTPLANWEB_ADMIN_PASSWORT"]),
        SchulId = Leer(c["UNTPLANWEB_SCHUL_ID"]),
        ApiSchluessel = Leer(c["UNTPLANWEB_API_SCHLUESSEL"]),
        MobilAdressen = (c["UNTPLANWEB_MOBILE_URL"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
    };

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Uhr in der Zeitzone der Schule.</summary>
public sealed class Uhr
{
    private readonly TimeZoneInfo _zone;

    public Uhr(Konfiguration k)
    {
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(k.Zeitzone); }
        catch (Exception) { _zone = TimeZoneInfo.Local; }
    }

    /// <summary>Für Tests: feste Zeit.</summary>
    public DateTime? Fest { get; set; }

    public DateTime Jetzt => Fest ?? TimeZoneInfo.ConvertTime(DateTime.UtcNow, _zone);
    public DateOnly Heute => DateOnly.FromDateTime(Jetzt);
}

/// <summary>Schlüssel-Wert-Einstellungen der Installation.</summary>
public sealed class Einstellungen
{
    private readonly Db _db;

    public Einstellungen(Db db) => _db = db;

    public string? Lesen(string schluessel) => _db.Wert("SELECT wert FROM einstellungen WHERE schluessel = @s", Db.P("@s", schluessel)) as string;

    public void Schreiben(string schluessel, string wert) =>
        _db.Ausfuehren("INSERT INTO einstellungen (schluessel, wert) VALUES (@s, @w) ON CONFLICT(schluessel) DO UPDATE SET wert = excluded.wert",
            Db.P("@s", schluessel), Db.P("@w", wert));

    public string Schulname => Lesen("schulname") ?? "";
    public string SchulId => Lesen("schul_id") ?? "";

    /// <summary>Nur der Hash des API-Schlüssels wird gespeichert.</summary>
    public bool ApiSchluesselPruefen(string? schluessel)
    {
        var hash = Lesen("api_schluessel");
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(schluessel)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Sicherheit.Hash(schluessel)));
    }

    /// <summary>Neuen API-Schlüssel erzeugen (bzw. vorgegebenen setzen) und im Klartext zurückgeben – er wird nur einmal angezeigt.</summary>
    public string ApiSchluesselSetzen(string? vorgabe = null)
    {
        var s = vorgabe ?? Sicherheit.Zufall(32);
        Schreiben("api_schluessel", Sicherheit.Hash(s));
        return s;
    }
}

public static class Sicherheit
{
    private const string Zeichen = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Zufallszeichenkette ohne verwechselbare Zeichen (0/O, 1/l/I).</summary>
    public static string Zufall(int laenge)
    {
        var b = RandomNumberGenerator.GetBytes(laenge);
        var sb = new StringBuilder(laenge);
        foreach (var x in b) sb.Append(Zeichen[x % Zeichen.Length]);
        return sb.ToString();
    }

    public static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>Kurzer, gut lesbarer Bezeichner aus einem Namen (Schul-ID).</summary>
    public static string Slug(string name)
    {
        var s = name.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (var c in s)
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var r = sb.ToString().Trim('-');
        return r.Length == 0 ? "schule" : r.Length > 40 ? r[..40].Trim('-') : r;
    }
}

/// <summary>Protokoll wichtiger Aktionen (Anmeldung, Übertragung, Verwaltung).</summary>
public sealed class Protokoll
{
    private readonly Db _db;
    private readonly Uhr _uhr;

    public Protokoll(Db db, Uhr uhr) { _db = db; _uhr = uhr; }

    public void Schreiben(string benutzer, string aktion, string details = "") =>
        _db.Ausfuehren("INSERT INTO protokoll (zeit, benutzer, aktion, details) VALUES (@z, @b, @a, @d)",
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")), Db.P("@b", benutzer), Db.P("@a", aktion), Db.P("@d", details));

    public List<object> Letzte(int anzahl) =>
        _db.Liste("SELECT zeit, benutzer, aktion, details FROM protokoll ORDER BY id DESC LIMIT @n",
            r => (object)new { zeit = r.Text("zeit"), benutzer = r.Text("benutzer"), aktion = r.Text("aktion"), details = r.Text("details") }, Db.P("@n", anzahl));
}
