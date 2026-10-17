using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace UntPlanMobile.Dienste;

/// <summary>Auf einem Gerät angemeldetes Konto (Sitzung bei UntPlanWeb verschlüsselt abgelegt).</summary>
public sealed class GespeichertesKonto
{
    public string Id { get; set; } = "";
    public int BenutzerId { get; set; }
    public string Login { get; set; } = "";
    public string Name { get; set; } = "";
    public string Rolle { get; set; } = "";
    public string Rollenname { get; set; } = "";
    /// <summary>Sitzungscookie von UntPlanWeb, mit ASP.NET Data Protection verschlüsselt.</summary>
    public string Sitzung { get; set; } = "";
    /// <summary>SHA-256 der Sitzung zum Wiederfinden (z. B. wenn UntPlanWeb das Cookie erneuert).</summary>
    public string SitzungHash { get; set; } = "";
    public DateTimeOffset Ablauf { get; set; }
    public DateTimeOffset Angemeldet { get; set; }
    /// <summary>Benachrichtigungen bei Änderungen im eigenen Plan (Web-Push an die Abos des Geräts).</summary>
    public bool Push { get; set; }
    /// <summary>Zuletzt gesehener Stand des eigenen Plans (je Tag die Zeichen der Kacheln) – Grundlage für den Vergleich.</summary>
    public Dictionary<string, List<Planeintrag>>? Stand { get; set; }
    public DateTimeOffset? Geprueft { get; set; }
}

/// <summary>Gerät (Browser bzw. Homescreen-App), erkannt am Cookie „untplanmobile_geraet“.</summary>
public sealed class Geraet
{
    public List<GespeichertesKonto> Konten { get; set; } = new();
    public List<PushAbo> Abos { get; set; } = new();
    public DateTimeOffset Zuletzt { get; set; }
}

/// <summary>
/// Mehrere Profile je Gerät und Push-Abos: Die Sitzungen aller auf einem Gerät angemeldeten Konten liegen serverseitig
/// (verschlüsselt) im Datenordner, das Gerät trägt nur eine Zufallskennung als Cookie. Der Wechsel setzt das Sitzungscookie
/// auf die gespeicherte Sitzung des gewählten Kontos – ohne erneute Anmeldung. Gespeichert wird in <c>konten.json</c>.
/// </summary>
public sealed class Kontenspeicher
{
    public const string GeraeteCookie = "untplanmobile_geraet";
    public const int HoechstensKonten = 8;

    private readonly object _sperre = new();
    private readonly string _datei;
    private readonly IDataProtector _schutz;
    private readonly ILogger<Kontenspeicher> _log;
    private Dictionary<string, Geraet> _geraete;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public string Ordner { get; }

    public Kontenspeicher(Datenordner ordner, IDataProtectionProvider schutz, ILogger<Kontenspeicher> log)
    {
        Ordner = ordner.Pfad;
        _datei = Path.Combine(Ordner, "konten.json");
        _schutz = schutz.CreateProtector("UntPlanMobile.Sitzung");
        _log = log;
        _geraete = Laden();
    }

    private Dictionary<string, Geraet> Laden()
    {
        try
        {
            if (File.Exists(_datei))
                return JsonSerializer.Deserialize<Dictionary<string, Geraet>>(File.ReadAllText(_datei), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Kontenspeicher {Datei} nicht lesbar – beginne leer.", _datei);
        }
        return new();
    }

    private void Schreiben()
    {
        try
        {
            var tmp = _datei + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_geraete, Json));
            File.Move(tmp, _datei, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Kontenspeicher {Datei} nicht schreibbar.", _datei);
        }
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>Gerätekennung (Hash des Cookies); mit <paramref name="anlegen"/> wird bei Bedarf ein neues Cookie gesetzt.</summary>
    public static string? GeraetVon(HttpContext c, bool anlegen)
    {
        if (c.Items[GeraeteCookie] is string schon) return Hash(schon);
        if (c.Request.Cookies.TryGetValue(GeraeteCookie, out var g) && g.Length >= 32) return Hash(g);
        if (!anlegen || c.Response.HasStarted) return null;
        g = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        c.Items[GeraeteCookie] = g;
        c.Response.Cookies.Append(GeraeteCookie, g, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, Path = "/", MaxAge = TimeSpan.FromDays(400), IsEssential = true,
        });
        return Hash(g);
    }

    public string? SitzungKlar(GespeichertesKonto k)
    {
        try { return _schutz.Unprotect(k.Sitzung); }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Konto auf dem Gerät hinterlegen (gleiches Konto: Sitzung ersetzen). Liefert die Kennung des Eintrags.</summary>
    public string Merken(string geraet, Sitzungsbenutzer b, string sitzung, DateTimeOffset ablauf)
    {
        lock (_sperre)
        {
            if (!_geraete.TryGetValue(geraet, out var g)) _geraete[geraet] = g = new Geraet();
            var k = g.Konten.FirstOrDefault(x => x.BenutzerId == b.Id);
            if (k is null)
            {
                k = new GespeichertesKonto { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), Angemeldet = DateTimeOffset.UtcNow };
                g.Konten.Add(k);
                // älteste Einträge fallen heraus
                while (g.Konten.Count > HoechstensKonten) g.Konten.RemoveAt(0);
            }
            k.BenutzerId = b.Id;
            k.Login = b.Login;
            k.Name = b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login;
            k.Rolle = b.Rolle;
            k.Rollenname = b.Rollenname;
            k.Sitzung = _schutz.Protect(sitzung);
            k.SitzungHash = Hash(sitzung);
            k.Ablauf = ablauf;
            g.Zuletzt = DateTimeOffset.UtcNow;
            Schreiben();
            return k.Id;
        }
    }

    /// <summary>Kopie der Konten eines Geräts (abgelaufene fallen weg).</summary>
    public List<GespeichertesKonto> Konten(string? geraet)
    {
        if (geraet is null) return new();
        lock (_sperre)
        {
            if (!_geraete.TryGetValue(geraet, out var g)) return new();
            var weg = g.Konten.RemoveAll(k => k.Ablauf < DateTimeOffset.UtcNow);
            if (weg > 0) Schreiben();
            return g.Konten.Select(Kopie).ToList();
        }
    }

    public GespeichertesKonto? Finden(string? geraet, string id) => Konten(geraet).FirstOrDefault(k => k.Id == id);

    public GespeichertesKonto? MitSitzung(string? geraet, string? sitzung) =>
        sitzung is null ? null : Konten(geraet).FirstOrDefault(k => k.SitzungHash == Hash(sitzung));

    public void Entfernen(string geraet, string id)
    {
        lock (_sperre)
        {
            if (_geraete.TryGetValue(geraet, out var g) && g.Konten.RemoveAll(k => k.Id == id) > 0)
            {
                if (g.Konten.Count == 0 && g.Abos.Count == 0) _geraete.Remove(geraet);
                Schreiben();
            }
        }
    }

    /// <summary>UntPlanWeb hat die Sitzung erneuert (gleitender Ablauf): neue Sitzung bei allen Einträgen mit der alten hinterlegen.</summary>
    public void SitzungErneuert(string alt, string neu, DateTimeOffset ablauf)
    {
        if (alt == neu) return;
        var h = Hash(alt);
        lock (_sperre)
        {
            var geaendert = false;
            foreach (var k in _geraete.Values.SelectMany(g => g.Konten).Where(k => k.SitzungHash == h))
            {
                k.Sitzung = _schutz.Protect(neu);
                k.SitzungHash = Hash(neu);
                k.Ablauf = ablauf;
                geaendert = true;
            }
            if (geaendert) Schreiben();
        }
    }

    /// <summary>Sitzung ist bei UntPlanWeb abgelaufen bzw. abgemeldet: Einträge damit entfernen.</summary>
    public void SitzungUngueltig(string sitzung)
    {
        var h = Hash(sitzung);
        lock (_sperre)
        {
            var weg = 0;
            foreach (var g in _geraete.Values) weg += g.Konten.RemoveAll(k => k.SitzungHash == h);
            if (weg > 0) Schreiben();
        }
    }

    // ------------------------------------------------------------------------------------------ Push

    public void AboSetzen(string geraet, PushAbo abo)
    {
        lock (_sperre)
        {
            if (!_geraete.TryGetValue(geraet, out var g)) _geraete[geraet] = g = new Geraet();
            // ein Abo gehört genau einem Gerät
            foreach (var andere in _geraete.Values) andere.Abos.RemoveAll(a => a.Endpoint == abo.Endpoint);
            g.Abos.Add(abo);
            Schreiben();
        }
    }

    public void AboEntfernen(string endpoint)
    {
        lock (_sperre)
        {
            var weg = 0;
            foreach (var g in _geraete.Values) weg += g.Abos.RemoveAll(a => a.Endpoint == endpoint);
            if (weg > 0) Schreiben();
        }
    }

    public List<PushAbo> Abos(string? geraet)
    {
        if (geraet is null) return new();
        lock (_sperre) return _geraete.TryGetValue(geraet, out var g) ? g.Abos.ToList() : new();
    }

    /// <summary>Benachrichtigungen für ein Konto des Geräts ein- bzw. ausschalten (beim Einschalten beginnt der Vergleich neu).</summary>
    public bool PushSetzen(string geraet, string id, bool an)
    {
        lock (_sperre)
        {
            var k = _geraete.TryGetValue(geraet, out var g) ? g.Konten.FirstOrDefault(x => x.Id == id) : null;
            if (k is null) return false;
            if (k.Push != an) { k.Stand = null; k.Geprueft = null; }
            k.Push = an;
            Schreiben();
            return true;
        }
    }

    /// <summary>Alle Konten mit eingeschalteten Benachrichtigungen auf Geräten mit Abo.</summary>
    public List<(string Geraet, GespeichertesKonto Konto, List<PushAbo> Abos, int KontenAufGeraet)> PushKonten()
    {
        lock (_sperre)
            return _geraete.Where(g => g.Value.Abos.Count > 0)
                .SelectMany(g => g.Value.Konten.Where(k => k.Push && k.Ablauf > DateTimeOffset.UtcNow)
                    .Select(k => (g.Key, Kopie(k), g.Value.Abos.ToList(), g.Value.Konten.Count)))
                .ToList();
    }

    public void StandSetzen(string geraet, string id, Dictionary<string, List<Planeintrag>> stand)
    {
        lock (_sperre)
        {
            var k = _geraete.TryGetValue(geraet, out var g) ? g.Konten.FirstOrDefault(x => x.Id == id) : null;
            if (k is null) return;
            k.Stand = stand;
            k.Geprueft = DateTimeOffset.UtcNow;
            Schreiben();
        }
    }

    private static GespeichertesKonto Kopie(GespeichertesKonto k) => new()
    {
        Id = k.Id, BenutzerId = k.BenutzerId, Login = k.Login, Name = k.Name, Rolle = k.Rolle, Rollenname = k.Rollenname, Sitzung = k.Sitzung,
        SitzungHash = k.SitzungHash, Ablauf = k.Ablauf, Angemeldet = k.Angemeldet, Push = k.Push, Geprueft = k.Geprueft,
        Stand = k.Stand?.ToDictionary(x => x.Key, x => x.Value.ToList()),
    };
}

/// <summary>Datenordner von UntPlanMobile (UNTPLANMOBILE_DATEN, sonst „daten“ neben der Anwendung).</summary>
public sealed class Datenordner
{
    public string Pfad { get; }

    public Datenordner(IConfiguration konfig)
    {
        Pfad = konfig["UNTPLANMOBILE_DATEN"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "daten");
        Directory.CreateDirectory(Pfad);
    }
}
