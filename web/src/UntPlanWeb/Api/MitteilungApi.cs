using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Mitteilungen – Erweiterungen unter <c>/api/app/mitteilungen</c> (Grundfunktionen in <see cref="AppApi"/> und <see cref="ErweiterungApi"/>):
/// <list type="bullet">
/// <item><c>GET /optionen</c>: darf senden, Grenzen für Anhänge, Lesebestätigung möglich.</item>
/// <item><c>POST /{id}/bestaetigen</c>: Lesebestätigung senden; <c>GET /{id}/lesebestaetigungen</c>: Lesebestätigungsliste (Absender).</item>
/// <item><c>GET /{id}/antwort?alle=</c>: Vorschlag für Antworten bzw. Allen antworten; <c>POST /{id}/weiterleiten</c>: Entwurf mit Anhängen.</item>
/// <item><c>POST /{id}/ungelesen</c>: wieder als ungelesen markieren.</item>
/// <item>Administration: <c>GET/PUT /einstellungen</c> – wer an wen schreiben darf, Sperren, Anhänge, Aufbewahrung.</item>
/// </list>
/// </summary>
public static class MitteilungApi
{
    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();
    private static Benutzer Admin(HttpContext c) { var b = Ich(c); return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration."); }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/mitteilungen");

        api.MapGet("/optionen", (HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            var e = m.Einstellungen();
            return Results.Json(new { darfSenden = m.DarfSenden(b, e), maxAnhangMb = e.MaxAnhangMb, maxAnhaenge = e.MaxAnhaenge, aufbewahrungTage = e.AufbewahrungTage,
                wichtig = b.Kollegium, listen = b.Kollegium, admin = b.IstAdmin });
        });

        api.MapPost("/{id:int}/bestaetigen", (int id, HttpContext c, Mitteilungsdienst m) => Results.Json(new { bestaetigt = m.Bestaetigen(Ich(c).Id, id) }));
        api.MapGet("/{id:int}/lesebestaetigungen", (int id, HttpContext c, Mitteilungsdienst m) =>
        {
            var liste = m.Lesestatus(Ich(c), id);
            return Results.Json(new { anzahl = liste.Count, gelesen = liste.Count(x => x.Gelesen != null), bestaetigt = liste.Count(x => x.Bestaetigt != null), empfaenger = liste });
        });
        api.MapPost("/{id:int}/ungelesen", (int id, HttpContext c, Mitteilungsdienst m) => { m.Ungelesen(Ich(c).Id, id); return Results.Json(new { ok = true }); });
        api.MapGet("/{id:int}/antwort", (int id, bool? alle, HttpContext c, Mitteilungsdienst m, Paketdienst p) => Results.Json(m.Antwort(Ich(c), p.Aktuell, id, alle == true)));
        api.MapPost("/{id:int}/weiterleiten", (int id, HttpContext c, Mitteilungsdienst m) => Results.Json(m.Weiterleiten(Ich(c), id)));

        api.MapGet("/einstellungen", (HttpContext c, Mitteilungsdienst m, Benutzerdienst bd) =>
        {
            Admin(c);
            return Results.Json(new
            {
                einstellungen = m.Einstellungen(),
                benutzer = bd.Alle().OrderBy(b => b.Rolle).ThenBy(b => b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login)
                    .Select(b => new { b.Id, name = b.Anzeigename.Length > 0 ? $"{b.Anzeigename} ({b.Login})" : b.Login, rolle = Rollen.Name(b.Rolle) }),
            });
        });
        api.MapPut("/einstellungen", (MitteilungsEinstellungen d, HttpContext c, Mitteilungsdienst m, Protokoll prot) =>
        {
            var b = Admin(c);
            var neu = m.EinstellungenSpeichern(d);
            prot.Schreiben(b.Login, "Mitteilungen: Einstellungen", $"Anhang {neu.MaxAnhangMb} MB, {neu.MaxAnhaenge} Anhänge, {neu.AufbewahrungTage} Tage, {neu.Gesperrt!.Count} gesperrt");
            return Results.Json(neu);
        });
    }
}
