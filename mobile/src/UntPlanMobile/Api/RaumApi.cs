using System.Globalization;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Raumbuchung aus den Stundendetails (wie in UntPlanWeb): freie buchbare Räume einer Stunde abfragen und die Stunde in einen
/// davon verlegen. Die Buchung geht als Raumänderung an UntPlanWeb und steht dort sofort als Änderung im Plan.
/// <list type="bullet">
/// <item><c>GET /api/rooms?action=free&amp;date=…&amp;from=…&amp;to=…</c> → <c>api/app/meldungen/freieraeume</c></item>
/// <item><c>POST /api/rooms</c> (action=book, date, from, to, room, class, subject) → <c>api/app/meldungen/raumbuchung</c></item>
/// <item><c>POST /api/rooms</c> (action=change, op=replace|add|remove, date, from, to, room, old, class, unterricht) → <c>api/app/mobil/raum</c>:
/// Raum-Chip in den Stundendetails – Raum ersetzen, zusätzlichen Raum hinzufügen, Raum löschen.</item>
/// </list>
/// </summary>
public static class RaumApi
{
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/rooms", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten) =>
        {
            var p = await Parameter.LesenAsync(c);
            var konto = await konten.LadenAsync(c);
            if (!konto.Kollegium) return Fehler("Räume buchen können nur Lehrkräfte.", 403);
            var datum = p.Datum("date");
            var von = p.Zahl("from");
            if (datum is null || von is null) return Fehler("bad_key");
            var bis = Math.Max(von.Value, p.Zahl("to") ?? von.Value);
            var iso = datum.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            switch (p.Text("action", HttpMethods.IsGet(c.Request.Method) ? "free" : "book"))
            {
                case "free":
                {
                    var unterricht = int.TryParse(p["lesson"], out var u) ? $"&unterricht={u}" : "";
                    var liste = await web.GetAsync(c, $"api/app/meldungen/freieraeume?datum={iso}&von={von}&bis={bis}{unterricht}");
                    var raeume = (liste?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(r => new
                    {
                        code = r["kurz"]?.GetValue<string>() ?? "",
                        name = r["name"]?.GetValue<string>() ?? "",
                        kind = r["raumart"]?.GetValue<string>() ?? "",
                        seats = r["kapazitaet"]?.GetValue<int?>(),
                    }).Where(r => r.code.Length > 0).ToList();
                    return Results.Json(new { ok = true, rooms = raeume });
                }
                case "book":
                {
                    if (HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);
                    var raum = p["room"].Trim();
                    if (raum.Length == 0) return Fehler("Bitte einen Raum wählen.");
                    var klassen = p["class"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    var fach = p["subject"].Trim();
                    await web.PostAsync(c, "api/app/meldungen/raumbuchung", new
                    {
                        datum = iso, stundeVon = von, stundeBis = bis, raum, klassen, titel = $"{(fach.Length > 0 ? fach : "Unterricht")} – Raumänderung",
                    });
                    return Results.Json(new { ok = true, room = raum });
                }
                case "change":
                {
                    if (HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);
                    var aktion = p["op"] switch { "replace" => "ersetzen", "add" => "hinzufuegen", "remove" => "loeschen", _ => "" };
                    if (aktion.Length == 0) return Fehler("unknown_op");
                    var raum = p["room"].Trim();
                    if (raum.Length == 0) return Fehler("Bitte einen Raum wählen.");
                    var klassen = p["class"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    var antwort = await web.PostAsync(c, "api/app/mobil/raum", new
                    {
                        datum = iso, stundeVon = von, stundeBis = bis, unterricht = p.Zahl("unterricht"), klassen, aktion, raum, alt = p["old"].Trim(),
                        // „Raumbuchung“: UntPlanWeb übernimmt den Titel dann nicht als Vertretungstext der Stunde
                        titel = "Raumbuchung",
                    });
                    var raeume = (antwort?["raeume"]?.AsArray() ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();
                    return Results.Json(new { ok = true, rooms = raeume, reset = antwort?["zurueckgesetzt"]?.GetValue<bool>() ?? false });
                }
                default:
                    return Fehler("unknown_action");
            }
        });
    }
}
