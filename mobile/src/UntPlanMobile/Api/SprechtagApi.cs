using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Sprechtag (Elternsprechtag) auf der Startseite: reicht <c>/api/sprechtag/…</c> an <c>/api/app/sprechtag/…</c> von UntPlanWeb weiter
/// (gleiche Pfade, JSON hin und zurück). Lehrkräfte sehen ihr Zeitraster (frei, gebucht, gesperrt), sperren bzw. geben Zeitfenster frei und
/// sagen Termine ab; Erziehungsberechtigte buchen freie Zeitfenster bei den Lehrkräften ihrer Kinder und sagen Termine ab.
/// Die Rechte prüft UntPlanWeb. PDF-Formulare gibt es in UntPlanWeb.
/// </summary>
public static class SprechtagApi
{
    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/sprechtag/{**pfad}", new[] { "GET", "POST", "PUT", "DELETE" }, async (string? pfad, HttpContext c, UntPlanWebVerbindung web) =>
        {
            pfad = (pfad ?? "").Trim('/');
            // nur Pfade aus Kennungen und bekannten Wörtern – keine Sprünge in andere Bereiche von UntPlanWeb
            if (pfad.Contains("..") || pfad.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(pfad, "^[A-Za-z0-9/_-]*$"))
                return Results.Json(new { ok = false, error = "Unbekannter Pfad." }, statusCode: 404);
            JsonNode? daten = null;
            if (!HttpMethods.IsGet(c.Request.Method) && !HttpMethods.IsDelete(c.Request.Method))
            {
                using var leser = new StreamReader(c.Request.Body);
                var text = await leser.ReadToEndAsync(c.RequestAborted);
                daten = text.Length > 0 ? JsonNode.Parse(text) : new JsonObject();
            }
            var ziel = "api/app/sprechtag" + (pfad.Length > 0 ? "/" + pfad : "") + c.Request.QueryString.Value;
            var methode = new HttpMethod(c.Request.Method);
            var antwort = await web.AufrufAsync(c, methode, ziel, daten ?? (methode == HttpMethod.Get || methode == HttpMethod.Delete ? null : new JsonObject()));
            return Results.Content(antwort?.ToJsonString() ?? "null", "application/json; charset=utf-8");
        });
    }
}
