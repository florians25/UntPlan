using System.Globalization;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Startseite (Ersatz für api/home.php): Schulname, „Meine Termine“ (Termine und Klausuren der nächsten zwei Wochen aus
/// UntPlanWeb), „Meine Abwesenheiten“ (eigene Abwesenheitsmeldungen an UntPlan) und für Klassenleitungen die
/// Fehlzeiten der Klasse mit Entschuldigen bzw. Ablehnen.
/// </summary>
public static class StartseiteApi
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    private static string Zeit(Konto k, int? stunde, bool ende) =>
        stunde is int s && (k.Elemente.Stunden ?? new()).FirstOrDefault(z => z.Nr == s) is { } z ? (ende ? z.Ende : z.Beginn) : "";

    private static string Stunden(int? von, int? bis) =>
        von is null ? "" : bis is null || bis == von ? $"{von}. Std." : bis >= 20 ? $"ab {von}. Std." : $"{von}.–{bis}. Std.";

    /// <summary>Zeitraum aus den Filtern der Liste; ohne Angabe das laufende Schuljahr bis heute (+ eine Woche).</summary>
    private static (DateOnly Von, DateOnly Bis) Zeitraum(Parameter p, DateOnly heute)
    {
        var jahr = heute.Month >= 8 ? heute.Year : heute.Year - 1;
        return (p.Datum("from") ?? new DateOnly(jahr, 8, 1), p.Datum("to") ?? heute.AddDays(7));
    }

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/home", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten, Zeitgeber zeit) =>
        {
            var p = await Parameter.LesenAsync(c);
            var k = await konten.LadenAsync(c);
            var heute = zeit.Heute;
            switch (p.Text("action", "home"))
            {
                case "home":
                {
                    var meldungen = k.IstSchueler ? new List<JsonObject>() : await AbwesenheitsmeldungenAsync(c, web);
                    var klassen = k.GeleiteteKlassen();
                    var offen = 0;
                    if (klassen.Count is > 0 and <= 3)
                        foreach (var kl in klassen) offen += (await FehlzeitenAsync(c, web, kl.Kurz, heute.AddDays(-60), heute)).Count(f => f.Status == "pending");
                    return Results.Json(new
                    {
                        ok = true,
                        school = k.Schule,
                        absence = new
                        {
                            total = meldungen.Count,
                            days = meldungen.Sum(m => Tage(m)),
                            open = meldungen.Count(m => m["status"]?.GetValue<string>() == "offen"),
                        },
                        classes = klassen.Select(x => new { id = x.Kurz, name = x.Kurz }),
                        openExcuses = offen,
                    });
                }
                case "exams":
                {
                    var bis = heute.AddDays(14);
                    var items = new List<(string Datum, int Stunde, object Eintrag)>();
                    var h = await web.GetAsync(c, "api/app/heute");
                    foreach (var t in (h?["termine"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>())
                    {
                        var von = t["von"]?.GetValue<string>() ?? "";
                        var bisT = t["bis"]?.GetValue<string>() ?? von;
                        var sv = t["stundeVon"]?.GetValue<int>();
                        var sb = t["stundeBis"]?.GetValue<int>();
                        items.Add((von, sv ?? 0, new
                        {
                            date = von,
                            timeFrom = Zeit(k, sv, false),
                            timeTo = Zeit(k, sb, true),
                            art = t["art"]?.GetValue<string>() == "klausur" ? "Klausur" : "Veranstaltung",
                            subject = t["titel"]?.GetValue<string>() ?? "",
                            @class = string.Join(", ", (t["klassen"]?.AsArray() ?? new JsonArray()).Select(x => x!.GetValue<string>())),
                            room = "",
                            text = bisT != von ? $"bis {DateOnly.Parse(bisT, CultureInfo.InvariantCulture):dd.MM.yyyy}" : "",
                        }));
                    }
                    // Eigene Klausuren, die UntPlan noch nicht übernommen hat
                    if (k.Kollegium)
                        foreach (var m in (await web.GetAsync(c, "api/app/meldungen"))?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                        {
                            if (m["typ"]?.GetValue<string>() != "klausur" || m["status"]?.GetValue<string>() != "offen") continue;
                            var von = m["von"]?.GetValue<string>() ?? "";
                            if (string.CompareOrdinal(von, Iso(heute)) < 0) continue;
                            items.Add((von, m["stundeVon"]?.GetValue<int>() ?? 0, new
                            {
                                date = von,
                                timeFrom = Zeit(k, m["stundeVon"]?.GetValue<int>(), false),
                                timeTo = Zeit(k, m["stundeBis"]?.GetValue<int>(), true),
                                art = "Klausur (vorgemerkt)",
                                subject = m["titel"]?.GetValue<string>() ?? "",
                                @class = string.Join(", ", (m["klassen"]?.AsArray() ?? new JsonArray()).Select(x => x!.GetValue<string>())),
                                room = string.Join(", ", (m["raeume"]?.AsArray() ?? new JsonArray()).Select(x => x!.GetValue<string>())),
                                text = m["text"]?.GetValue<string>() ?? "",
                            }));
                        }
                    return Results.Json(new { ok = true, to = Iso(bis), items = items.OrderBy(i => i.Datum).ThenBy(i => i.Stunde).Select(i => i.Eintrag) });
                }
                case "absences":
                {
                    var (von, bis) = Zeitraum(p, heute);
                    if (k.IstSchueler) return Results.Json(new { ok = true, to = Iso(bis), items = Array.Empty<object>(), student = true });
                    var items = (await AbwesenheitsmeldungenAsync(c, web))
                        .Where(m => string.CompareOrdinal(m["bis"]?.GetValue<string>() ?? "", Iso(von)) >= 0 && string.CompareOrdinal(m["von"]?.GetValue<string>() ?? "", Iso(bis.AddDays(60))) <= 0)
                        .Select(m =>
                        {
                            var uebernommen = m["status"]?.GetValue<string>() != "offen";
                            var sv = m["stundeVon"]?.GetValue<int>();
                            var sb = m["stundeBis"]?.GetValue<int>();
                            return new
                            {
                                id = m["id"]?.GetValue<string>(),
                                from = m["von"]?.GetValue<string>() ?? "",
                                to = m["bis"]?.GetValue<string>() ?? "",
                                timeFrom = Zeit(k, sv, false),
                                timeTo = Zeit(k, sb, true),
                                lessons = Stunden(sv, sb),
                                art = "Abwesenheit",
                                reason = m["grund"]?.GetValue<string>() ?? "",
                                note = m["text"]?.GetValue<string>() ?? "",
                                tone = uebernommen ? "ok" : "open",
                                label = uebernommen ? "Von UntPlan übernommen" : "Gemeldet – noch nicht übernommen",
                                source = "UntPlanWeb",
                            };
                        }).ToList();
                    return Results.Json(new { ok = true, to = Iso(bis), items, student = false });
                }
                case "leadabsent":
                case "classlead":
                {
                    var (von, bis) = Zeitraum(p, heute);
                    var geleitet = k.GeleiteteKlassen();
                    var gewaehlt = geleitet.Where(x => string.Equals(x.Kurz, p["class"], StringComparison.OrdinalIgnoreCase)).ToList();
                    var klassen = p.Text("scope", "mine") == "all" ? geleitet : gewaehlt.Count > 0 ? gewaehlt : geleitet.Take(1).ToList();
                    var gruppen = new Dictionary<string, List<object>> { ["pending"] = new(), ["approved"] = new(), ["rejected"] = new() };
                    foreach (var kl in klassen)
                        foreach (var f in await FehlzeitenAsync(c, web, kl.Kurz, von, bis))
                            gruppen[f.Status].Add(f.Karte);
                    return Results.Json(new { ok = true, to = Iso(bis), groups = gruppen, classes = geleitet.Select(x => new { id = x.Kurz, name = x.Kurz }) });
                }
                case "excuse":
                {
                    if (HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);
                    var ids = p["id"].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).ToList();
                    var status = p["status"] switch { "approved" => "entschuldigt", "rejected" => "unentschuldigt", _ => "offen" };
                    await web.PostAsync(c, "api/app/klassenbuch/fehlzeiten/status", new { ids, status });
                    return Results.Json(new { ok = true });
                }
                default:
                    return Fehler("unknown_action");
            }
        });
    }

    private static int Tage(JsonObject m) =>
        DateOnly.TryParse(m["von"]?.GetValue<string>(), CultureInfo.InvariantCulture, out var v) && DateOnly.TryParse(m["bis"]?.GetValue<string>(), CultureInfo.InvariantCulture, out var b)
            ? Math.Max(1, b.DayNumber - v.DayNumber + 1) : 1;

    private static async Task<List<JsonObject>> AbwesenheitsmeldungenAsync(HttpContext c, UntPlanWebVerbindung web) =>
        ((await web.GetAsync(c, "api/app/meldungen"))?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
        .Where(m => m["typ"]?.GetValue<string>() == "absenz").ToList();

    /// <summary>Fehlzeiten einer Klasse, je Schüler*in und Tag zu einer Karte zusammengefasst.</summary>
    private static async Task<List<(string Status, object Karte)>> FehlzeitenAsync(HttpContext c, UntPlanWebVerbindung web, string klasse, DateOnly von, DateOnly bis)
    {
        JsonNode? j;
        try { j = await web.GetAsync(c, $"api/app/klassenbuch/fehlzeiten?klasse={Uri.EscapeDataString(klasse)}&von={Iso(von)}&bis={Iso(bis)}"); }
        catch (UntPlanWebFehler) { return new(); }
        var namen = (j?["schueler"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .ToDictionary(s => s["id"]!.GetValue<int>(), s => $"{s["name"]?.GetValue<string>()}, {s["vorname"]?.GetValue<string>()}");
        var res = new List<(string, object)>();
        foreach (var g in (j?["eintraege"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                     .GroupBy(f => (Sid: f["schuelerId"]!.GetValue<int>(), Datum: f["datum"]!.GetValue<string>(), Art: f["art"]!.GetValue<string>(), Status: f["status"]!.GetValue<string>()))
                     .OrderByDescending(g => g.Key.Datum))
        {
            var stunden = g.Select(f => f["stunde"]!.GetValue<int>()).OrderBy(x => x).ToList();
            var status = g.Key.Status switch { "entschuldigt" => "approved", "unentschuldigt" => "rejected", _ => "pending" };
            var minuten = g.Sum(f => f["minuten"]?.GetValue<int>() ?? 0);
            res.Add((status, new
            {
                id = string.Join(",", g.Select(f => f["id"]!.GetValue<int>())),
                name = namen.GetValueOrDefault(g.Key.Sid, "?"),
                from = g.Key.Datum,
                to = g.Key.Datum,
                lessons = Stunden(stunden[0], stunden[^1]),
                art = g.Key.Art == "verspaetet" ? $"Verspätet ({minuten} min)" : "Abwesend",
                reason = "",
                note = string.Join(" · ", g.Select(f => f["bemerkung"]?.GetValue<string>() ?? "").Where(x => x.Length > 0).Distinct()),
                status,
                tone = status switch { "approved" => "ok", "rejected" => "no", _ => "open" },
                label = status switch { "approved" => "Entschuldigt", "rejected" => "Nicht entschuldigt", _ => "Offen" },
                source = $"Klasse {klasse}",
            }));
        }
        return res;
    }
}
