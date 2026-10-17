using System.Security.Claims;
using System.Text.Json.Nodes;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>
/// Benutzereinstellungen als JSON unter einem Schlüssel: <c>GET/PUT/DELETE /api/einstellungen/{schluessel}</c>.
/// Verwendet z. B. für Fensteranordnungen (<c>fenster.anordnungen</c>) und Tabellenspalten (<c>tabelle.&lt;fenster&gt;</c>).
/// </summary>
public sealed class EinstellungenApi : IStudioModul
{
    public string Name => "einstellungen";

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        r.Api.MapGet("/einstellungen/{schluessel}", (string schluessel, ClaimsPrincipal p, Benutzereinstellungen e) =>
        {
            if (!Benutzereinstellungen.SchluesselGueltig(schluessel)) throw StudioFehler.Ungueltig("Ungültiger Schlüssel.");
            return Results.Json(new { schluessel, wert = e.Lesen(p.Benutzername(), schluessel) });
        });
        r.Api.MapPut("/einstellungen/{schluessel}", (string schluessel, JsonNode? wert, ClaimsPrincipal p, Benutzereinstellungen e) =>
        {
            e.Schreiben(p.Benutzername(), schluessel, wert);
            return Results.Ok(new { ok = true });
        });
        r.Api.MapDelete("/einstellungen/{schluessel}", (string schluessel, ClaimsPrincipal p, Benutzereinstellungen e) =>
        {
            e.Schreiben(p.Benutzername(), schluessel, null);
            return Results.Ok(new { ok = true });
        });
    }
}
