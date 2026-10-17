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
    /// <summary>Offene Klausurmeldung steht schon als Klausurtermin in /api/app/heute (gleicher Tag, überlappende Stunden, gemeinsame Klasse).</summary>
    public static bool SchonAlsTermin(JsonNode? heute, JsonObject m)
    {
        static List<string> Liste(JsonNode? n) => (n as JsonArray ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();
        static int? Zahl(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var x) ? x : null;
        var von = m["von"]?.GetValue<string>() ?? "";
        var sv = Zahl(m["stundeVon"]) ?? 1;
        var sb = Zahl(m["stundeBis"]) ?? sv;
        var klassen = Liste(m["klassen"]);
        return (heute?["termine"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Any(t =>
        {
            if (t["art"]?.GetValue<string>() != "klausur" || (t["von"]?.GetValue<string>() ?? "") != von) return false;
            var tv = Zahl(t["stundeVon"]) ?? 1;
            var tb = Zahl(t["stundeBis"]) ?? tv;
            if (tb < tv) tb = tv;
            var tk = Liste(t["klassen"]);
            return tv <= sb && tb >= sv && (klassen.Count == 0 || tk.Count == 0 || tk.Any(x => klassen.Contains(x, StringComparer.OrdinalIgnoreCase)));
        });
    }

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

    /// <summary>Formatierte Tagestexte (HTML aus UntPlan) als schlichter Text.</summary>
    internal static string OhneHtml(string html)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(html ?? "", "<(br|/p)[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "");
        return System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(t), @"\s+", " ").Trim();
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
                    // Schüler*innen/Eltern: Summen aus UntPlanWeb (Fehltage laut Stundenplan statt Kalendertagen, Verspätungen zählen nicht als Tag)
                    (List<JsonObject> Zeilen, JsonObject? Summen) eigene = k.IstSchueler ? await EigeneAbwesenheitenRohAsync(c, web, Zeitraum(p, heute).Von, heute, p.Zahl("student")) : (new List<JsonObject>(), null);
                    var klassen = k.GeleiteteKlassen();
                    var offen = 0;
                    if (klassen.Count is > 0 and <= 3)
                        foreach (var kl in klassen) offen += (await FehlzeitenAsync(c, web, k, kl.Kurz, heute.AddDays(-60), heute)).Count(f => f.Status == "pending");
                    // Infos zum Tag für die Karten oben: Nachrichten zum Tag und heutige Veranstaltungen aus UntPlanWeb
                    var news = new List<object>();
                    // Sprechtage für die Startseite (gleiche Karten wie /api/app/sprechtag/start); null = älterer Server ohne das Feld
                    JsonNode? sprechtage = null;
                    try
                    {
                        var h = await web.GetAsync(c, "api/app/heute");
                        if (h?["sprechtage"] is JsonArray st) sprechtage = st.DeepClone();
                        foreach (var n in (h?["nachrichten"]?.AsArray() ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? ""))
                        {
                            var text = OhneHtml(n);
                            if (text.Length == 0) continue;
                            var teil = text.IndexOf(": ", StringComparison.Ordinal);
                            news.Add(teil is > 0 and < 60 ? new { kind = "news", title = text[..teil], text = text[(teil + 2)..] } : new { kind = "news", title = "Nachricht zum Tag", text });
                        }
                        foreach (var ev in (h?["ereignisse"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>())
                        {
                            var titel = ev["titel"]?.GetValue<string>() ?? "";
                            if (titel.Length == 0) continue;
                            var ganz = ev["ganztaegig"]?.GetValue<bool>() == true;
                            var v = ev["von"]?.GetValue<int>() ?? 0; var b = ev["bis"]?.GetValue<int>() ?? v;
                            news.Add(new { kind = "event", title = titel, text = ganz ? "Heute ganztägig" : $"Heute {Stunden(v, b)}" });
                        }
                    }
                    catch (Exception) { /* Startseite bleibt ohne Infokarten nutzbar */ }
                    return Results.Json(new
                    {
                        ok = true,
                        school = k.Schule,
                        news,
                        absence = k.IstSchueler
                            ? (object)Zusammenfassung(eigene.Zeilen, eigene.Summen)
                            : new
                            {
                                total = meldungen.Count,
                                days = meldungen.Sum(m => Tage(m)),
                                open = meldungen.Count(m => m["status"]?.GetValue<string>() == "offen"),
                            },
                        classes = klassen.Select(x => new { id = x.Kurz, name = x.Kurz }),
                        openExcuses = offen,
                        sprechtage,
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
                            // schon von UntPlan übernommen und übertragen (Klausurtermin am Tag, überlappende Stunden, gemeinsame Klasse):
                            // nicht ein zweites Mal als „vorgemerkt“ zeigen
                            if (SchonAlsTermin(h, m)) continue;
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
                    // Schüler*innen und Erziehungsberechtigte: eigene Abwesenheiten aus dem Klassenbuch (wie „Meine Abwesenheiten“ im Original)
                    if (k.IstSchueler)
                    {
                        // Erziehungsberechtigte mit mehreren Kindern wählen das Kind (student); Summen und Kinder für Kopf und Filter
                        var kinder = await KinderAsync(c, web);
                        var kind = p.Zahl("student") is int sid && kinder.Any(x => x.Id == sid) ? sid : kinder.FirstOrDefault()?.Id;
                        var (eigeneKarten, summen) = await EigeneAbwesenheitenAsync(c, web, von, bis, kind);
                        return Results.Json(new
                        {
                            ok = true, from = Iso(von), to = Iso(bis), items = eigeneKarten, student = true,
                            summary = Zusammenfassung(null, summen), children = kinder.Select(x => new { id = x.Id, name = x.Name, @class = x.Klasse }), selected = kind,
                        });
                    }
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
                        foreach (var f in await FehlzeitenAsync(c, web, k, kl.Kurz, von, bis))
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

    private static readonly string[] Wt = { "So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa." };

    /// <summary>Kind eines Schüler- bzw. Elternkontos (/api/app/familie/kinder).</summary>
    private sealed record Kind(int Id, string Name, string Klasse);

    private static async Task<List<Kind>> KinderAsync(HttpContext c, UntPlanWebVerbindung web)
    {
        try
        {
            return ((await web.GetAsync(c, "api/app/familie/kinder")) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(x => x["id"] is JsonValue)
                .Select(x => new Kind(x["id"]!.GetValue<int>(), $"{x["vorname"]?.GetValue<string>()} {x["name"]?.GetValue<string>()}".Trim(), x["klasse"]?.GetValue<string>() ?? ""))
                .ToList();
        }
        catch (UntPlanWebFehler) { return new(); }
    }

    /// <summary>
    /// Abwesenheitszeiträume der eigenen Person bzw. des (gewählten) Kindes aus UntPlanWeb (/api/app/familie/abwesenheiten) samt Summen
    /// (Fehltage, Fehlstunden je Status, Verspätungen; ältere Server ohne Summen: null).
    /// </summary>
    private static async Task<(List<JsonObject> Zeilen, JsonObject? Summen)> EigeneAbwesenheitenRohAsync(HttpContext c, UntPlanWebVerbindung web, DateOnly von, DateOnly bis, int? kind = null)
    {
        try
        {
            var j = await web.GetAsync(c, $"api/app/familie/abwesenheiten?von={Iso(von)}&bis={Iso(bis)}{(kind is int id ? $"&schueler={id}" : "")}");
            return ((j?["zeilen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().ToList(), j?["summen"] as JsonObject);
        }
        catch (UntPlanWebFehler) { return (new(), null); }
    }

    /// <summary>
    /// Zusammenfassung für Startseite und Liste: Abwesenheiten (total), Fehltage (days), Fehlstunden (hours), Fehlstunden entschuldigt/nicht
    /// entschuldigt/offen, Verspätungen (late, lateMinutes) und noch offene Abwesenheiten (open). Ohne Summen vom Server aus den Zeilen.
    /// </summary>
    internal static object Zusammenfassung(List<JsonObject>? zeilen, JsonObject? su)
    {
        int Z(string f) => su?[f] is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
        if (su is null)
        {
            var liste = zeilen ?? new List<JsonObject>();
            string S(JsonObject z, string f) => z[f] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
            int N(JsonObject z, string f) => z[f] is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
            return new
            {
                total = liste.Count, days = 0, hours = liste.Sum(z => N(z, "fehlstunden")),
                excused = liste.Where(z => S(z, "status") == "entschuldigt").Sum(z => N(z, "fehlstunden")),
                unexcused = liste.Where(z => S(z, "status") == "unentschuldigt").Sum(z => N(z, "fehlstunden")),
                pending = liste.Where(z => S(z, "status") is not ("entschuldigt" or "unentschuldigt")).Sum(z => N(z, "fehlstunden")),
                late = liste.Count(z => S(z, "art") == "verspaetet"), lateMinutes = liste.Where(z => S(z, "art") == "verspaetet").Sum(z => N(z, "minuten")),
                open = liste.Count(z => S(z, "status") == "offen"),
            };
        }
        return new
        {
            total = Z("abwesenheiten"), days = Z("fehltage"), hours = Z("fehlstunden"), excused = Z("entschuldigt"), unexcused = Z("unentschuldigt"), pending = Z("offen"),
            late = Z("verspaetet"), lateMinutes = Z("verspaetetMinuten"), open = Z("offeneAbwesenheiten"),
        };
    }

    /// <summary>
    /// Karten für „Meine Abwesenheiten“: Zeitraum, Uhrzeit (über mehrere Tage mit Wochentag), Grund, Fehlstunden und Entschuldigungsstand
    /// (einzeln entschuldigte Stunden: „teilweise entschuldigt“ mit Anzahl).
    /// </summary>
    private static async Task<(List<object> Items, JsonObject? Summen)> EigeneAbwesenheitenAsync(HttpContext c, UntPlanWebVerbindung web, DateOnly von, DateOnly bis, int? kind)
    {
        var (zeilen, summen) = await EigeneAbwesenheitenRohAsync(c, web, von, bis, kind);
        Dictionary<string, string> gruende;
        try
        {
            gruende = ((await web.GetAsync(c, "api/app/familie/gruende")) as JsonObject ?? new JsonObject())
                .ToDictionary(x => x.Key, x => x.Value?.GetValue<string>() ?? x.Key, StringComparer.OrdinalIgnoreCase);
        }
        catch (UntPlanWebFehler) { gruende = new(StringComparer.OrdinalIgnoreCase); }
        string S(JsonObject z, string f) => z[f] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : "";
        int N(JsonObject z, string f) => z[f] is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
        string Tag(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? Wt[(int)d.DayOfWeek] + ", " : "";
        return (zeilen.Select(z =>
        {
            var vd = S(z, "vonDatum"); var bd = S(z, "bisDatum"); if (bd.Length == 0) bd = vd;
            var mehr = bd != vd;
            var status = S(z, "status");
            var grund = S(z, "grund");
            var art = S(z, "art");
            var minuten = N(z, "minuten");
            var stunden = N(z, "fehlstunden");
            var gemischt = z["statusGemischt"] is JsonValue g && g.TryGetValue<bool>(out var gb) && gb;
            var entschuldigt = N(z, "entschuldigt");
            return (object)new
            {
                id = S(z, "schluessel"),
                from = vd, to = bd,
                timeFrom = (mehr ? Tag(vd) : "") + S(z, "vonZeit"),
                timeTo = (mehr ? Tag(bd) : "") + S(z, "bisZeit"),
                lessons = stunden > 0 ? $"{stunden} {(stunden == 1 ? "Fehlstunde" : "Fehlstunden")}" : "",
                hours = stunden,
                art = art == "verspaetet" ? (minuten > 0 ? $"Verspätung ({minuten} min)" : "Verspätung") : art == "vorzeitig" ? (minuten > 0 ? $"Vorzeitig gegangen ({minuten} min)" : "Vorzeitig gegangen") : "",
                reason = grund.Length > 0 ? gruende.GetValueOrDefault(grund, grund) : "",
                note = S(z, "text"),
                status = gemischt ? "teilweise" : status,
                tone = gemischt ? "open" : status switch { "entschuldigt" => "ok", "unentschuldigt" => "no", _ => "open" },
                label = gemischt ? $"teilweise entschuldigt ({entschuldigt}/{stunden})" : status switch { "entschuldigt" => "entschuldigt", "unentschuldigt" => "nicht entschuldigt", _ => "offen" },
                source = "",
            };
        }).ToList(), summen);
    }

    private static int Tage(JsonObject m) =>
        DateOnly.TryParse(m["von"]?.GetValue<string>(), CultureInfo.InvariantCulture, out var v) && DateOnly.TryParse(m["bis"]?.GetValue<string>(), CultureInfo.InvariantCulture, out var b)
            ? Math.Max(1, b.DayNumber - v.DayNumber + 1) : 1;

    private static async Task<List<JsonObject>> AbwesenheitsmeldungenAsync(HttpContext c, UntPlanWebVerbindung web) =>
        ((await web.GetAsync(c, "api/app/meldungen"))?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
        .Where(m => m["typ"]?.GetValue<string>() == "absenz").ToList();

    /// <summary>Fehlzeiten einer Klasse, je Schüler*in und Tag zu einer Karte zusammengefasst.</summary>
    private static async Task<List<(string Status, object Karte)>> FehlzeitenAsync(HttpContext c, UntPlanWebVerbindung web, Konto k, string klasse, DateOnly von, DateOnly bis)
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
                timeFrom = Zeit(k, stunden[0], false),
                timeTo = Zeit(k, stunden[^1], true),
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
