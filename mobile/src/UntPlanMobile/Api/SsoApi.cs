using System.Security.Cryptography;
using System.Text;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Einmalanmeldung (SSO) über UntPlanWeb: Wer in UntPlanWeb angemeldet ist, ist es ohne erneute Passworteingabe auch hier.
/// <list type="number">
/// <item><c>/sso/start</c>: Zufalls-„state“ als Cookie merken und den Browser zu UntPlanWeb <c>/sso/mobil</c> schicken.</item>
/// <item>UntPlanWeb meldet bei Bedarf erst an und springt mit Einmal-Code nach <c>/sso/rueckkehr</c> zurück.</item>
/// <item>Der Code wird serverseitig bei UntPlanWeb eingelöst; das dabei gesetzte Sitzungscookie wird zu unserem Cookie.</item>
/// </list>
/// Öffentliche Adresse von UntPlanWeb (für den Browser): <c>UNTPLANWEB_PUBLIC_URL</c>, sonst derselbe Rechnername mit Port 8080.
/// </summary>
public static class SsoApi
{
    private const string StateCookie = "untplanmobile_sso";

    public static void Abbilden(WebApplication app)
    {
        var oeffentlich = app.Configuration["UNTPLANWEB_PUBLIC_URL"] is { Length: > 0 } o ? o.TrimEnd('/') : null;

        app.MapGet("/sso/start", (HttpContext c) =>
        {
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            c.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, Path = "/sso", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true,
            });
            var web = oeffentlich ?? $"{c.Request.Scheme}://{c.Request.Host.Host}:8080";
            return Results.Redirect($"{web}/sso/mobil?ruecksprung={Uri.EscapeDataString(Ruecksprung(c))}&state={state}");
        });

        app.MapGet("/sso/rueckkehr", async (string? code, string? state, HttpContext c, UntPlanWebVerbindung web, Kontenspeicher speicher) =>
        {
            var erwartet = c.Request.Cookies[StateCookie];
            c.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/sso", Secure = c.Request.IsHttps, SameSite = SameSiteMode.Lax, HttpOnly = true });
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(erwartet) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state), Encoding.ASCII.GetBytes(erwartet)))
                return Results.Redirect("/?sso=fehler");
            try
            {
                if (UntPlanWebVerbindung.Sitzung(c) is { } bisher && speicher.MitSitzung(Kontenspeicher.GeraetVon(c, false), bisher) is null)
                    await MobilApi.ProfilMerkenAsync(c, web, speicher);
                await web.AufrufAsync(c, HttpMethod.Post, "api/app/sso/einloesen", new { code, ruecksprung = Ruecksprung(c) }, anmeldungNoetig: false);
                await MobilApi.ProfilMerkenAsync(c, web, speicher);
                return Results.Redirect("/");
            }
            catch (UntPlanWebFehler) { return Results.Redirect("/?sso=fehler"); }
        });
    }

    private static string Ruecksprung(HttpContext c) => $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}/sso/rueckkehr";
}
