using System.Collections.Concurrent;
using System.Security.Cryptography;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Einmalanmeldung (SSO) für UntPlanMobile:
/// <list type="number">
/// <item>UntPlanMobile leitet den Browser auf <c>/sso/mobil?ruecksprung=…&amp;state=…</c>.</item>
/// <item>Ist der Benutzer hier angemeldet (sonst erst Anmeldeseite), entsteht ein Einmal-Code (2 Minuten gültig) und der
///   Browser springt mit <c>code</c> und <c>state</c> zurück – nur an erlaubte Adressen (kein offener Weiterleiter).</item>
/// <item>UntPlanMobile löst den Code serverseitig über <c>POST /api/app/sso/einloesen</c> ein und erhält eine eigene Sitzung.</item>
/// </list>
/// </summary>
public static class SsoApi
{
    private sealed record Eintrag(int BenutzerId, string Ruecksprung, DateTime Ablauf);

    private static readonly ConcurrentDictionary<string, Eintrag> Codes = new();
    private static readonly TimeSpan Gueltigkeit = TimeSpan.FromMinutes(2);

    public sealed record EinloeseDaten(string Code, string? Ruecksprung);

    /// <summary>Ist die Rücksprungadresse erlaubt? Konfigurierte Mobil-Adressen, sonst gleicher Rechnername wie UntPlanWeb.</summary>
    public static bool Erlaubt(string ruecksprung, HttpContext c, Konfiguration k)
    {
        if (!Uri.TryCreate(ruecksprung, UriKind.Absolute, out var ziel) || ziel.Scheme is not ("http" or "https")) return false;
        if (!string.IsNullOrEmpty(ziel.UserInfo) || !string.IsNullOrEmpty(ziel.Fragment)) return false;
        if (k.MobilAdressen.Count > 0)
            return k.MobilAdressen.Any(a => Uri.TryCreate(a, UriKind.Absolute, out var basis) &&
                                            string.Equals(basis.GetLeftPart(UriPartial.Authority), ziel.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
        return string.Equals(ziel.Host, c.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }

    public static void Abbilden(WebApplication app)
    {
        app.MapGet("/sso/mobil", (string? ruecksprung, string? state, HttpContext c, Konfiguration k) =>
        {
            if (string.IsNullOrEmpty(ruecksprung) || string.IsNullOrEmpty(state) || state.Length > 200 || !Erlaubt(ruecksprung, c, k))
                return Results.Text("Ungültige Anmeldeanfrage: Die Rücksprungadresse ist nicht freigegeben (UNTPLANWEB_MOBILE_URL).", "text/plain; charset=utf-8", statusCode: 400);
            var b = c.Ich();
            if (b is null || b.PasswortAendern)
            {
                // erst hier anmelden; die Anmeldeseite kehrt danach hierher zurück
                var weiter = $"/sso/mobil?ruecksprung={Uri.EscapeDataString(ruecksprung)}&state={Uri.EscapeDataString(state)}";
                return Results.Redirect("/?weiter=" + Uri.EscapeDataString(weiter));
            }
            Aufraeumen();
            var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            Codes[code] = new Eintrag(b.Id, ruecksprung, DateTime.UtcNow + Gueltigkeit);
            var trenner = ruecksprung.Contains('?') ? '&' : '?';
            return Results.Redirect($"{ruecksprung}{trenner}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        });

        app.MapPost("/api/app/sso/einloesen", async (EinloeseDaten d, HttpContext c, Benutzerdienst bd, Protokoll prot) =>
        {
            Aufraeumen();
            if (string.IsNullOrEmpty(d.Code) || !Codes.TryRemove(d.Code, out var e) || e.Ablauf < DateTime.UtcNow ||
                (d.Ruecksprung != null && d.Ruecksprung != e.Ruecksprung))
                return Results.Json(new { fehler = "Der Anmeldecode ist ungültig oder abgelaufen." }, statusCode: 401);
            if (bd.Holen(e.BenutzerId) is not { Gesperrt: false } b)
                return Results.Json(new { fehler = "Dieses Konto ist gesperrt." }, statusCode: 403);
            await c.AnmeldenAsync(b);
            prot.Schreiben(b.Login, "Anmeldung (UntPlanMobile, SSO)");
            return Results.Json(new { ok = true });
        });
    }

    private static void Aufraeumen()
    {
        var jetzt = DateTime.UtcNow;
        foreach (var (k, v) in Codes) if (v.Ablauf < jetzt) Codes.TryRemove(k, out _);
    }
}
