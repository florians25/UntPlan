using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Entschuldigungen (Klassenleitung und Fachlehrkräfte): reicht <c>/api/entschuldigungen/…</c> an
/// <c>/api/app/entschuldigungen/…</c> von UntPlanWeb weiter (gleiche Pfade, JSON hin und zurück). Die Klassenleitung erfasst,
/// ändert und nimmt Entschuldigungen ganzer Abwesenheiten zurück; Fachlehrkräfte entschuldigen Fehlstunden im eigenen
/// Unterricht. Rechte, Einstellungen und Sperren prüft UntPlanWeb.
/// </summary>
public static class EntschuldigungenApi
{
    /// <summary>Weitergereichte Endpunkte (Vertrag Sprint 26).</summary>
    public static readonly string[] Pfade = { "einstellungen", "fehlzeiten", "abwesenheiten", "abwesenheit", "zuruecknehmen" };

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/entschuldigungen/{pfad}", new[] { "GET", "POST" }, async (string pfad, HttpContext c, UntPlanWebVerbindung web) =>
        {
            if (!Pfade.Contains(pfad)) return Results.Json(new { ok = false, error = "Unbekannter Pfad." }, statusCode: 404);
            JsonNode? daten = null;
            if (HttpMethods.IsPost(c.Request.Method))
            {
                using var leser = new StreamReader(c.Request.Body);
                var text = await leser.ReadToEndAsync(c.RequestAborted);
                daten = text.Length > 0 ? JsonNode.Parse(text) : new JsonObject();
            }
            var ziel = "api/app/entschuldigungen/" + pfad + c.Request.QueryString.Value;
            var antwort = await web.AufrufAsync(c, new HttpMethod(c.Request.Method), ziel, daten);
            return Results.Content(antwort?.ToJsonString() ?? "null", "application/json; charset=utf-8");
        });
    }
}
