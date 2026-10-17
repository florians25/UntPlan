using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record NeuerBenutzer(string Name, string Anzeigename, string Rolle, string Passwort);
public sealed record NeuesPasswort(string Passwort);

/// <summary>Benutzerverwaltung (nur Administratoren). Später erweitert um die MultiUser-Anbindung.</summary>
public sealed class BenutzerApi : IStudioModul
{
    public string Name => "benutzer";

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Api.MapGroup("/benutzer").RequireAuthorization(p => p.RequireRole(Rollen.Admin));
        g.MapGet("", (Benutzerdienst b) => b.Alle().Select(x => new { x.Name, x.Anzeigename, x.Rolle, x.Erstellt, x.LetzteAnmeldung }));
        g.MapPost("", (NeuerBenutzer n, Benutzerdienst b) => AnmeldungApi.Info(b.Anlegen(n.Name, n.Anzeigename, n.Rolle, n.Passwort)));
        g.MapDelete("/{name}", (string name, Benutzerdienst b) => { b.Loeschen(name); return Results.Ok(new { ok = true }); });
        g.MapPost("/{name}/passwort", (string name, NeuesPasswort n, Benutzerdienst b) => { b.PasswortSetzen(name, n.Passwort); return Results.Ok(new { ok = true }); });
        g.MapGet("/rollen", () => Rollen.Alle);
    }
}
