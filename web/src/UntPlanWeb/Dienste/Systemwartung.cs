using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Angemeldete Sitzung mit letzter Aktivität (Administration → Aktive Benutzer).</summary>
public sealed record Sitzung(string Schluessel, int BenutzerId, string Login, string Anzeigename, string Rolle, string Adresse, string Geraet, DateTime Beginn, DateTime Letzte);

/// <summary>Zugriffssperre (Wartungsmodus): nur die Administration kann sich neu anmelden; Hinweis für die Anmeldeseite.</summary>
public sealed record Zugriffssperre(bool Gesperrt, string Hinweis, string Seit, string Von);

/// <summary>
/// Wartungsfunktionen der Administration:
/// <list type="bullet">
/// <item>Aktive Benutzer: Sitzungen mit letzter Aktivität (im Speicher, seit dem Start; Abmelden entfernt die Sitzung).</item>
/// <item>Zugriff ent-/sperren: bei gesperrtem Zugriff können sich nur Konten der Administration bzw. mit dem Recht „Systemwartung“ anmelden;
///   bereits angemeldete Benutzer bleiben angemeldet.</item>
/// <item>Über UntPlan: Version, Datenbank, letzte Übertragungen aus dem Desktop-Programm je Teil (Stammdaten, Unterricht, Vertretungen), Module.</item>
/// </list>
/// </summary>
public sealed class Systemwartung
{
    /// <summary>Claim mit der Sitzungskennung (wird bei jeder Anmeldung neu vergeben).</summary>
    public const string SitzungsClaim = "untplan:sitzung";
    /// <summary>Sitzungen mit Aktivität in diesem Zeitraum gelten als „gerade aktiv“.</summary>
    public static readonly TimeSpan Aktiv = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Aktualisieren = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Aufbewahren = TimeSpan.FromDays(14);
    private const string SperreSchluessel = "zugriff_sperre";

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Einstellungen _e;
    private readonly ConcurrentDictionary<string, Sitzung> _sitzungen = new();

    public Systemwartung(Db db, Uhr uhr, Einstellungen e) { _db = db; _uhr = uhr; _e = e; }

    // ------------------------------------------------------------------------------------------ Sitzungen

    /// <summary>Schlüssel der Sitzung: Sitzungskennung aus dem Anmelde-Cookie; ältere Cookies: Konto, Adresse und Gerät.</summary>
    public static string? Schluessel(HttpContext c)
    {
        if (c.User.Identity?.IsAuthenticated != true || c.User.FindFirstValue(ClaimTypes.NameIdentifier) is not string id) return null;
        return c.User.FindFirstValue(SitzungsClaim) is { Length: > 0 } s ? s : $"{id}|{c.Connection.RemoteIpAddress}|{c.Request.Headers.UserAgent}";
    }

    /// <summary>Aktivität vermerken (höchstens alle 30 Sekunden je Sitzung).</summary>
    public void Merken(HttpContext c, Func<Benutzer?> benutzer)
    {
        if (Schluessel(c) is not string k) return;
        var jetzt = _uhr.Jetzt;
        if (_sitzungen.TryGetValue(k, out var s) && jetzt - s.Letzte < Aktualisieren) return;
        if (s != null) { _sitzungen[k] = s with { Letzte = jetzt }; return; }
        if (benutzer() is not { } b) return;
        _sitzungen[k] = new Sitzung(k, b.Id, b.Login, b.Anzeigename, b.Rolle, c.Connection.RemoteIpAddress?.ToString() ?? "", Geraet(c.Request.Headers.UserAgent.ToString()), jetzt, jetzt);
        if (_sitzungen.Count > 2000) Aufraeumen();
    }

    public void Entfernen(HttpContext c)
    {
        if (Schluessel(c) is string k) _sitzungen.TryRemove(k, out _);
    }

    private void Aufraeumen()
    {
        var grenze = _uhr.Jetzt - Aufbewahren;
        foreach (var (k, v) in _sitzungen) if (v.Letzte < grenze) _sitzungen.TryRemove(k, out _);
    }

    /// <summary>Sitzungen mit Aktivität im Zeitraum (neueste zuerst).</summary>
    public List<Sitzung> Sitzungen(TimeSpan zeitraum)
    {
        Aufraeumen();
        var grenze = _uhr.Jetzt - zeitraum;
        return _sitzungen.Values.Where(s => s.Letzte >= grenze).OrderByDescending(s => s.Letzte).ToList();
    }

    /// <summary>Kurzbeschreibung des Geräts aus dem User-Agent (Browser bzw. App, Betriebssystem).</summary>
    public static string Geraet(string ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "";
        var app = ua.Contains("UntPlanMobile", StringComparison.OrdinalIgnoreCase) ? "UntPlanMobile"
            : ua.Contains("Edg/") ? "Edge" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "";
        var os = ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" : ua.Contains("Android") ? "Android" : ua.Contains("Windows") ? "Windows"
            : ua.Contains("Mac OS X") ? "macOS" : ua.Contains("Linux") ? "Linux" : "";
        var t = string.Join(" · ", new[] { app, os }.Where(x => x.Length > 0));
        return t.Length > 0 ? t : ua.Length > 60 ? ua[..60] : ua;
    }

    // ------------------------------------------------------------------------------------------ Zugriffssperre

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Zugriffssperre Sperre()
    {
        try { return _e.Lesen(SperreSchluessel) is { Length: > 0 } j ? JsonSerializer.Deserialize<Zugriffssperre>(j, Json) ?? Frei : Frei; }
        catch (JsonException) { return Frei; }
    }

    private static readonly Zugriffssperre Frei = new(false, "", "", "");

    public const string StandardHinweis = "Wegen Wartungsarbeiten ist die Anmeldung vorübergehend gesperrt. Bitte versuchen Sie es später erneut.";

    public Zugriffssperre SperreSetzen(Benutzer b, bool gesperrt, string? hinweis)
    {
        if (!Ausgenommen(b)) throw new UnauthorizedAccessException("Nur für die Administration.");
        var h = (hinweis ?? "").Trim();
        if (h.Length > 500) throw new ArgumentException("Der Hinweis ist zu lang (höchstens 500 Zeichen).");
        var alt = Sperre();
        var neu = gesperrt
            ? new Zugriffssperre(true, h, alt.Gesperrt ? alt.Seit : _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm"), alt.Gesperrt ? alt.Von : b.Login)
            : new Zugriffssperre(false, h, "", "");
        _e.Schreiben(SperreSchluessel, JsonSerializer.Serialize(neu, Json));
        return neu;
    }

    /// <summary>Darf sich trotz gesperrtem Zugriff anmelden und die Wartungsfunktionen nutzen.</summary>
    public static bool Ausgenommen(Benutzer b) => b.IstAdmin || Rechtedienst.Darf(b, Recht.AdminWartung);

    /// <summary>Text der Anmeldeseite bei gesperrtem Zugriff.</summary>
    public static string Meldung(Zugriffssperre z) => z.Hinweis.Length > 0 ? z.Hinweis : StandardHinweis;

    /// <summary>Vor jeder neuen Anmeldung (Passwort, Konto wechseln, Einmalanmeldung): bei gesperrtem Zugriff nur Administration.</summary>
    public void AnmeldungPruefen(Benutzer b)
    {
        var z = Sperre();
        if (z.Gesperrt && !Ausgenommen(b)) throw new UnauthorizedAccessException(Meldung(z));
    }

    // ------------------------------------------------------------------------------------------ Über UntPlan

    public sealed record Import(string Teil, string Name, string? Empfangen, int? Revision, string Programmversion);

    /// <summary>Letzte Übertragung je Teil (Pakete ohne Teile-Angabe enthalten alles).</summary>
    public List<Import> LetzteImporte()
    {
        var verlauf = _db.Liste("SELECT revision, empfangen, programmversion, teile FROM paket_verlauf ORDER BY id DESC LIMIT 500",
            r => (Revision: r.Zahl("revision"), Empfangen: r.Text("empfangen"), Programm: r.Text("programmversion"),
                Teile: r.Text("teile").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        var namen = new (string Teil, string Name)[] { (WebTeile.Stammdaten, "Stammdaten"), (WebTeile.Unterricht, "Unterricht"), (WebTeile.Vertretungen, "Vertretungen") };
        return namen.Select(n =>
        {
            var z = verlauf.FirstOrDefault(v => v.Teile.Length == 0 || v.Teile.Contains(n.Teil, StringComparer.OrdinalIgnoreCase));
            return z.Empfangen is null ? new Import(n.Teil, n.Name, null, null, "") : new Import(n.Teil, n.Name, z.Empfangen, z.Revision, z.Programm);
        }).ToList();
    }
}
