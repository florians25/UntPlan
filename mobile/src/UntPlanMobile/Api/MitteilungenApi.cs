using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Mitteilungen (Ersatz für api/messages.php): Posteingang, Gesendet, Lesen, Schreiben und Löschen über
/// /api/app/mitteilungen von UntPlanWeb. Entwürfe und Anhänge bietet UntPlanWeb nicht an.
/// </summary>
public static class MitteilungenApi
{
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    private static JsonObject Eintrag(JsonObject m, bool eingang)
    {
        var empfaenger = new JsonArray((m["empfaenger"]?.AsArray() ?? new JsonArray())
            .Select(n => (JsonNode)new JsonObject { ["label"] = n!.GetValue<string>(), ["token"] = "", ["kind"] = "" }).ToArray());
        return new JsonObject
        {
            ["id"] = m["id"]!.GetValue<int>(),
            ["status"] = "sent",
            ["sender"] = eingang ? m["absender"]?.GetValue<string>() ?? "" : "Ich",
            ["senderToken"] = eingang ? $"benutzer:{m["absenderId"]?.GetValue<int>()}" : "",
            ["subject"] = m["betreff"]?.GetValue<string>() ?? "",
            ["body"] = m["text"]?.GetValue<string>() ?? "",
            ["sentAt"] = m["erstellt"]?.GetValue<string>() ?? "",
            ["read"] = !eingang || m["gelesen"] is JsonValue,
            ["important"] = m["wichtig"]?.GetValue<bool>() ?? false,
            ["recipients"] = empfaenger,
            ["attachmentCount"] = 0,
            ["attachments"] = new JsonArray(),
        };
    }

    private static async Task<List<JsonObject>> ListeAsync(HttpContext c, UntPlanWebVerbindung web, bool eingang) =>
        ((await web.GetAsync(c, eingang ? "api/app/mitteilungen" : "api/app/mitteilungen?ordner=gesendet"))?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>().Select(m => Eintrag(m, eingang)).ToList();

    private static string Art(string art) => art switch
    {
        "gruppe" => "group",
        "klasse" or "Schüler*in / Klasse" => "student",
        _ => "teacher",
    };

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/messages", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web) =>
        {
            var p = await Parameter.LesenAsync(c);
            switch (p.Text("action", "list"))
            {
                case "list":
                {
                    var box = p.Text("box", "inbox");
                    if (box == "drafts") return Results.Json(new { ok = true, items = Array.Empty<object>() });
                    var liste = await ListeAsync(c, web, box != "sent");
                    var q = p["q"].Trim();
                    if (q.Length > 0)
                        liste = liste.Where(m => new[] { "sender", "subject", "body" }.Any(f => (m[f]?.GetValue<string>() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                                                 || (m["recipients"]?.AsArray() ?? new JsonArray()).Any(r => (r?["label"]?.GetValue<string>() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
                    return Results.Json(new { ok = true, items = liste });
                }
                case "detail":
                {
                    var id = p.Zahl("id") ?? 0;
                    var m = (await ListeAsync(c, web, true)).FirstOrDefault(x => x["id"]!.GetValue<int>() == id);
                    if (m != null && m["read"]?.GetValue<bool>() == false)
                        await web.PostAsync(c, $"api/app/mitteilungen/{id}/gelesen", new { });
                    m ??= (await ListeAsync(c, web, false)).FirstOrDefault(x => x["id"]!.GetValue<int>() == id);
                    return m is null ? Fehler("not_found", 404) : Results.Json(new { ok = true, item = m });
                }
                case "recipients":
                {
                    var q = p["q"].Trim();
                    var liste = ((await web.GetAsync(c, "api/app/mitteilungen/empfaenger"))?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                        .Where(e => q.Length == 0 || (e["name"]?.GetValue<string>() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                        .Select(e => new
                        {
                            token = e["id"]!.GetValue<string>(), label = e["name"]?.GetValue<string>() ?? "",
                            detail = e["art"]?.GetValue<string>() switch { "gruppe" => "Gruppe", "klasse" => "Klasse", var a => a ?? "" },
                            kind = Art(e["art"]?.GetValue<string>() ?? ""),
                        }).Take(60).ToList();
                    return Results.Json(new { ok = true, items = liste, lists = Array.Empty<object>() });
                }
                case "list_members":
                    return Results.Json(new { ok = true, items = Array.Empty<object>() });
                case "send":
                {
                    var empfaenger = new List<string>();
                    try { empfaenger = JsonSerializer.Deserialize<List<string>>(p.Text("recipients", "[]")) ?? new(); } catch (JsonException) { }
                    if (empfaenger.Count == 0) return Fehler("recipient_required");
                    if (string.IsNullOrWhiteSpace(p["subject"])) return Fehler("subject_required");
                    var r = await web.PostAsync(c, "api/app/mitteilungen", new { empfaenger, betreff = p["subject"].Trim(), text = p["body"], wichtig = false });
                    return Results.Json(new { ok = true, id = r?["id"]?.GetValue<int>() });
                }
                case "save_draft":
                    return Fehler("Entwürfe werden von UntPlanWeb nicht unterstützt.");
                case "delete":
                    await web.DeleteAsync(c, $"api/app/mitteilungen/{p.Zahl("id") ?? 0}");
                    return Results.Json(new { ok = true });
                case "unread":
                    return Results.Json(new { ok = true, count = (await ListeAsync(c, web, true)).Count(m => m["read"]?.GetValue<bool>() == false) });
                default:
                    return Fehler("unknown_action");
            }
        });
    }
}
