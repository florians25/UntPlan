using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record AnmeldeDaten(string Benutzer, string Passwort);
public sealed record PasswortAenderung(string Alt, string Neu);
public sealed record BenutzerInfo(string Name, string Anzeigename, string Rolle, bool DarfSchreiben);

/// <summary>Anmeldung (Cookie), Abmeldung, eigenes Konto, Systeminformation.</summary>
public sealed class AnmeldungApi : IStudioModul
{
    public string Name => "anmeldung";

    /// <summary>Fehlversuche je Benutzer: nach 5 Fehlversuchen 60 Sekunden Sperre.</summary>
    private static readonly ConcurrentDictionary<string, (int Anzahl, DateTime Bis)> Fehlversuche = new(StringComparer.OrdinalIgnoreCase);

    public static BenutzerInfo Info(StudioBenutzer b) => new(b.Name, b.Anzeigename, b.Rolle, b.Rolle != Rollen.Leser);

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        r.Offen.MapGet("/system", () => new
        {
            anwendung = Program.Anwendungsname,
            version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0",
            beta = true,
        });

        r.Offen.MapPost("/anmeldung", async (AnmeldeDaten daten, Benutzerdienst benutzer, HttpContext ctx) =>
        {
            var name = (daten.Benutzer ?? "").Trim();
            if (Fehlversuche.TryGetValue(name, out var f) && f.Anzahl >= 5 && f.Bis > DateTime.Now)
                throw new StudioFehler(429, "Zu viele Fehlversuche. Bitte eine Minute warten.");
            var b = benutzer.Pruefen(name, daten.Passwort ?? "");
            if (b is null)
            {
                Fehlversuche.AddOrUpdate(name, _ => (1, DateTime.Now.AddMinutes(1)), (_, alt) => (alt.Bis < DateTime.Now ? 1 : alt.Anzahl + 1, DateTime.Now.AddMinutes(1)));
                await Task.Delay(300);
                throw new StudioFehler(401, "Benutzername oder Passwort ist falsch.");
            }
            Fehlversuche.TryRemove(name, out _);
            var identitaet = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, b.Name),
                new Claim(ClaimTypes.GivenName, b.Anzeigename),
                new Claim(ClaimTypes.Role, b.Rolle),
            }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identitaet), new AuthenticationProperties { IsPersistent = true });
            return Info(b);
        });

        r.Offen.MapPost("/abmeldung", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        // Sitzung prüfen ohne 401 (Startseite): angemeldeter Benutzer oder null
        r.Offen.MapGet("/sitzung", (ClaimsPrincipal p, Benutzerdienst benutzer) =>
            new { benutzer = p.Identity?.IsAuthenticated == true && benutzer.Holen(p.Benutzername()) is { } b ? Info(b) : null });

        r.Api.MapGet("/ich", (ClaimsPrincipal p, Benutzerdienst benutzer) =>
            benutzer.Holen(p.Benutzername()) is { } b ? Results.Ok(Info(b)) : Results.Unauthorized());

        r.Api.MapPost("/ich/passwort", (PasswortAenderung a, ClaimsPrincipal p, Benutzerdienst benutzer) =>
        {
            if (benutzer.Pruefen(p.Benutzername(), a.Alt ?? "") is null) throw StudioFehler.Ungueltig("Das bisherige Passwort ist falsch.");
            benutzer.PasswortSetzen(p.Benutzername(), a.Neu ?? "");
            return Results.Ok(new { ok = true });
        });
    }
}
