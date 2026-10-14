using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Schnittstelle für UntPlan (Desktop), Schema untplanweb/1 – siehe UntPlanWebSchema.Pfade.
/// <list type="bullet">
/// <item>Paket übertragen (PUT).</item>
/// <item>Rückmeldungen abholen (GET) und quittieren (POST).</item>
/// </list>
/// Authentifizierung: Bearer-API-Schlüssel der Schule.
/// </summary>
public static class SyncApi
{
    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", (Einstellungen e, Paketdienst p) => Results.Json(new
        {
            name = "UntPlanWeb",
            version = typeof(SyncApi).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            schema = UntPlanWebSchema.Schema,
            schule = e.Schulname,
            revision = p.Revision,
        }));

        api.MapPut("/schulen/{schule}/untplanweb", async (string schule, HttpContext ctx, Einstellungen e, Paketdienst p, Protokoll prot) =>
        {
            if (Pruefen(schule, ctx, e) is { } fehler) return fehler;
            WebPaket? paket;
            try { paket = await JsonSerializer.DeserializeAsync<WebPaket>(ctx.Request.Body, UntPlanWebSchema.Json); }
            catch (JsonException) { return Fehler(400, "Ungültiges JSON."); }
            if (paket is null || paket.Schema != UntPlanWebSchema.Schema) return Fehler(400, $"Schema {UntPlanWebSchema.Schema} erwartet.");
            try
            {
                var stand = p.Speichern(paket);
                if (e.Schulname.Length == 0 && stand.Paket.Schule.Name.Length > 0) e.Schreiben("schulname", stand.Paket.Schule.Name);
                prot.Schreiben("UntPlan", "Paket empfangen", $"Revision {paket.Revision}, Teile: {(paket.Teile.Count == 0 ? "alle" : string.Join(", ", paket.Teile))}");
                return Results.Json(new WebAntwort(true, paket.Revision, null), UntPlanWebSchema.Json);
            }
            catch (Paketdienst.VeralteteRevisionException ex)
            {
                return Results.Json(new WebAntwort(false, ex.Aktuell, ex.Message), UntPlanWebSchema.Json, statusCode: 409);
            }
        });

        api.MapGet("/schulen/{schule}/rueckmeldungen", (string schule, HttpContext ctx, Einstellungen e, Rueckmeldungsdienst r) =>
            Pruefen(schule, ctx, e) ?? Results.Json(r.Offene().Select(m => m.AlsVertrag()), UntPlanWebSchema.Json));

        api.MapPost("/schulen/{schule}/rueckmeldungen/quittieren", async (string schule, HttpContext ctx, Einstellungen e, Rueckmeldungsdienst r, Protokoll prot) =>
        {
            if (Pruefen(schule, ctx, e) is { } fehler) return fehler;
            // Liste der Ids – als Array oder als Objekt { "ids": [...] }
            var knoten = await JsonNode.ParseAsync(ctx.Request.Body);
            var ids = (knoten as JsonArray ?? knoten?["ids"] as JsonArray)?.Select(x => x?.GetValue<string>()).OfType<string>().ToList() ?? new();
            var n = r.Quittieren(ids);
            if (n > 0) prot.Schreiben("UntPlan", "Rückmeldungen quittiert", n.ToString());
            return Results.Json(new { ok = true, quittiert = n });
        });
    }

    private static IResult Fehler(int code, string meldung) => Results.Json(new WebAntwort(false, 0, meldung), UntPlanWebSchema.Json, statusCode: code);

    /// <summary>Schul-ID und API-Schlüssel prüfen.</summary>
    private static IResult? Pruefen(string schule, HttpContext ctx, Einstellungen e)
    {
        if (e.SchulId.Length == 0 || !string.Equals(schule, e.SchulId, StringComparison.OrdinalIgnoreCase)) return Fehler(404, "Unbekannte Schule.");
        var kopf = ctx.Request.Headers.Authorization.ToString();
        var schluessel = kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? kopf[7..].Trim() : null;
        return e.ApiSchluesselPruefen(schluessel) ? null : Fehler(401, "API-Schlüssel ungültig.");
    }
}
