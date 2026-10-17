using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Klassenbuch der Stundendetails (Ersatz für api/classbook.php der Vorlage), abgebildet auf UntPlanWeb:
/// <list type="bullet">
/// <item>Lehrstoff → Unterrichtsinhalt, Notizen für Schüler*innen → Notiz für Schüler*innen, Notizen für Lehrkräfte → interne Notiz,
/// Hausaufgaben → Hausaufgabenliste der Stunde (Text, Fälligkeit, Bemerkung).</item>
/// <item>Klassenbucheinträge → Vermerke zur Klasse am Tag, Bemerkungen zu Schüler*innen → Vermerke zur Person.</item>
/// <item>Schüler*innen → Anwesenheit (fehlt / verspätet mit Minuten); jede Änderung speichert die ganze Stunde.</item>
/// <item>Abwesenheit bearbeiten: minutengenau von–bis mit Grund, Text und „Erziehungsberechtigte benachrichtigt“
/// (UntPlanWeb: /klassenbuch/abwesenheit), Abwesenheiten je Schüler*in (action=absences), Gründe aus UntPlanWeb.</item>
/// </list>
/// </summary>
public static class KlassenbuchApi
{
    /// <param name="Einzelstunde">Einzelstunde in einer Doppel-/Blockstunde (UntPlanWeb: <c>einzelstunde</c>); <c>null</c> = ganzer Block.</param>
    private sealed record Schluessel(DateOnly Datum, int Stunde, int? Unterricht, string Klasse, string Lehrer = "", int? Einzelstunde = null);

    private static Schluessel? SchluesselLesen(Parameter p)
    {
        var datum = p.Datum("date");
        var stunde = p.Zahl("lesson");
        if (datum is null || stunde is null) return null;
        // Einzelstunde eines Blocks: nur gültig, wenn sie innerhalb des Blocks liegen kann (sonst ganzer Block)
        var einzel = p.Zahl("einzelstunde") is int e and > 0 ? e : (int?)null;
        return new Schluessel(datum.Value, stunde.Value, p.Zahl("unterricht") ?? p.Zahl("entry"), p["class"].Split(',')[0].Trim(), p["teacher"].Trim(), einzel);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Stunde(Schluessel k) => $"datum={Iso(k.Datum)}&stunde={k.Stunde}&unterricht={k.Unterricht}{(k.Lehrer.Length > 0 ? "&lehrer=" + Uri.EscapeDataString(k.Lehrer) : "")}{(k.Einzelstunde is int e ? $"&einzelstunde={e}" : "")}";

    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    private static readonly object LeereInhalte = new
    {
        lehrstoff = "", notes_students = "", notes_teachers = "", homework = Array.Empty<object>(), classbook = Array.Empty<object>(),
    };

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/classbook", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten) =>
        {
            var p = await Parameter.LesenAsync(c);
            var aktion = p.Text("action", "all");
            var k = SchluesselLesen(p);
            if (k is null) return Fehler("bad_key");
            var konto = await konten.LadenAsync(c);
            if (!HttpMethods.IsGet(c.Request.Method) && !konto.Kollegium) return Fehler("not_allowed", 403);

            // Stunden ohne Unterricht (Klausur, Veranstaltung) haben kein Klassenbuch
            if (k.Unterricht is null)
                return aktion switch
                {
                    "all" => Results.Json(new { ok = true, canEdit = false, online = false, data = LeereInhalte, students = Array.Empty<object>(), absentClasses = Array.Empty<string>(), dueHomework = await FaelligAsync(c, web, k), @checked = false, absent = 0, reasons = Array.Empty<object>() }),
                    "students" => Results.Json(new { ok = true, @checked = false, students = Array.Empty<object>(), absentClasses = Array.Empty<string>(), hint = "Zu dieser Stunde gibt es kein Klassenbuch." }),
                    "attSummary" => Results.Json(new { ok = true, @checked = false, absent = 0 }),
                    "reasons" => Results.Json(new { ok = true, reasons = Array.Empty<object>() }),
                    "remarkTypes" => Results.Json(new { ok = true, types = await ArtenAsync(c, web, k) }),
                    _ => Fehler("Zu dieser Stunde gibt es kein Klassenbuch."),
                };

            switch (aktion)
            {
                case "all":
                {
                    var (inhalte, darf, eintrag, roh) = await InhalteRohAsync(c, web, konto, k);
                    var online = eintrag.Online;
                    var onlineLink = online ? Kachelbau.OnlineLinkGueltig(eintrag.OnlineLink) ?? "" : "";
                    var (schueler, geprueft, _) = konto.Kollegium ? await SchuelerAsync(c, web, konto, k) : (new List<JsonObject>(), false, null);
                    return Results.Json(new
                    {
                        // canEdit: eigene Stunde (UntPlanWeb erlaubt das Klassenbuch), online: „Findet online statt“
                        ok = true, canEdit = darf && konto.Kollegium, online, onlineLink, data = inhalte, students = schueler, absentClasses = Array.Empty<string>(),
                        dueHomework = await FaelligAsync(c, web, k), @checked = geprueft,
                        absent = schueler.Count(s => s["state"]?.GetValue<string>() == "absent"), reasons = konto.Kollegium ? await GruendeAsync(c, web) : new List<object>(),
                        // Ganzer Block / Einzelstunde (Vertrag Einzelstunde)
                        lessonPart = Teilauswahl(roh, k.Einzelstunde, konto.Kollegium),
                    });
                }
                case "load":
                    return Results.Json(new { ok = true, data = (await InhalteAsync(c, web, konto, k)).Inhalte });
                case "students":
                {
                    var (schueler, geprueft, hinweis) = konto.Kollegium ? await SchuelerAsync(c, web, konto, k) : (new List<JsonObject>(), false, "Nur für das Kollegium.");
                    return Results.Json(new { ok = true, @checked = geprueft, students = schueler, absentClasses = Array.Empty<string>(), hint = schueler.Count > 0 ? null : hinweis ?? "Zu dieser Klasse sind keine Schüler*innen hinterlegt." });
                }
                case "attSummary":
                {
                    var (schueler, geprueft, _) = konto.Kollegium ? await SchuelerAsync(c, web, konto, k) : (new List<JsonObject>(), false, null);
                    return Results.Json(new { ok = true, @checked = geprueft, absent = schueler.Count(s => s["state"]?.GetValue<string>() == "absent") });
                }
                case "reasons":
                    // Abwesenheitsgründe aus UntPlanWeb (Kürzel → Name); ohne Verbindung nur „Ohne Angabe“
                    return Results.Json(new { ok = true, reasons = await GruendeAsync(c, web) });
                case "absences":
                    return await AbwesenheitenAsync(c, web, k, p.Zahl("student_id") ?? 0);
                case "absenceDelete":
                    return await AbwesenheitLoeschenAsync(c, web, k, p);
                case "remarkTypes":
                    return Results.Json(new { ok = true, types = await ArtenAsync(c, web, k) });
                case "online":
                {
                    // „Findet online statt“ (Schalter in den Stundendetails) → Klassenbuch der Stunde in UntPlanWeb
                    var an = p["value"] is "1" or "true" or "on";
                    // Link zum Online-Unterricht (nur wenn mitgeschickt; leer löscht ihn)
                    string? link = null;
                    if (p.Hat("link"))
                    {
                        link = p["link"].Trim();
                        if (link.Length > 0 && !link.Contains("://", StringComparison.Ordinal)) link = "https://" + link;
                        if (link.Length > 0 && Kachelbau.OnlineLinkGueltig(link) is null) return Fehler("Bitte einen gültigen Link (https://…) eingeben.");
                    }
                    var antwort = await web.PutAsync(c, "api/app/klassenbuch/online", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, online = an, onlineLink = link, einzelstunde = k.Einzelstunde });
                    var gespeichert = antwort?["onlineLink"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : link;
                    return Results.Json(new { ok = true, online = an, onlineLink = an ? gespeichert ?? "" : "" });
                }
                case "remarkMany":
                {
                    // Gemeinsamer Klassenbucheintrag für mehrere Schüler*innen (Mehrfachauswahl in der Schülerliste): je Person ein Vermerk
                    var text = p["text"].Trim();
                    if (text.Length == 0) return Fehler("Bitte einen Text eingeben.");
                    var ids = p["student_ids"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0).Where(i => i > 0).Distinct().ToList();
                    if (ids.Count == 0) return Fehler("Bitte mindestens eine Schülerin oder einen Schüler wählen.");
                    var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
                    foreach (var sid in ids)
                    {
                        var klasse = aw?["schueler"]?.AsArray().FirstOrDefault(s => s?["id"]?.GetValue<int>() == sid)?["klasse"]?.GetValue<string>() ?? k.Klasse;
                        await web.PostAsync(c, "api/app/klassenbuch/vermerke", new { datum = Iso(k.Datum), klasse, schuelerId = sid, art = p.Text("type", "Vermerk"), text });
                    }
                    return Results.Json(new { ok = true, count = ids.Count, remarks = await BemerkungenAsync(c, web, await KlassenAsync(c, web, k), k) });
                }
                case "save":
                    return await TextSpeichernAsync(c, web, konto, k, p["field"], p["value"]);
                case "addItem" or "setItem" or "delItem" when p["field"] == "homework":
                    return await HausaufgabeAsync(c, web, konto, k, aktion, p);
                case "addItem" or "setItem" or "delItem" when p["field"] == "classbook":
                    return await KlasseneintragAsync(c, web, k, aktion, p);
                case "attendance":
                    return await AnwesenheitAsync(c, web, konto, k, p);
                case "setChecked":
                {
                    var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
                    await web.PutAsync(c, "api/app/klassenbuch/anwesenheit", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, eintraege = Eintraege(aw), einzelstunde = k.Einzelstunde });
                    return Results.Json(new { ok = true, @checked = true });
                }
                case "remarkSave":
                case "remarkDel":
                {
                    if (aktion == "remarkSave" && string.IsNullOrWhiteSpace(p["text"])) return Fehler("Bitte einen Text eingeben.");
                    if (p.Zahl("id") is int alt and > 0) await web.DeleteAsync(c, $"api/app/klassenbuch/vermerke/{alt}");
                    if (aktion == "remarkSave")
                    {
                        var sid = p.Zahl("student_id") ?? 0;
                        var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
                        var klasse = aw?["schueler"]?.AsArray().FirstOrDefault(s => s?["id"]?.GetValue<int>() == sid)?["klasse"]?.GetValue<string>() ?? k.Klasse;
                        await web.PostAsync(c, "api/app/klassenbuch/vermerke", new { datum = Iso(k.Datum), klasse, schuelerId = sid, art = p.Text("type", "Vermerk"), text = p["text"].Trim() });
                    }
                    return Results.Json(new { ok = true, remarks = await BemerkungenAsync(c, web, await KlassenAsync(c, web, k), k) });
                }
                default:
                    return Fehler("unknown_action");
            }
        });
    }

    // ------------------------------------------------------------------------------------------ Inhalte

    private sealed record Eintrag(string Thema, string Hausaufgabe, string? Faellig, string Notiz, string NotizSchueler = "", bool Online = false, string OnlineLink = "");

    private static Eintrag EintragLesen(JsonObject? e) =>
        new(e?["thema"]?.GetValue<string>() ?? "", e?["hausaufgabe"]?.GetValue<string>() ?? "", e?["faellig"]?.GetValue<string>(),
            e?["notiz"]?.GetValue<string>() ?? "", e?["notizSchueler"]?.GetValue<string>() ?? "", e?["online"]?.GetValue<bool>() ?? false,
            e?["onlineLink"] is JsonValue ol && ol.GetValueKind() == JsonValueKind.String ? ol.GetValue<string>() : "");

    private static async Task<(Eintrag Eintrag, bool Darf)> EintragAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        var (e, darf, _) = await EintragRohAsync(c, web, k);
        return (e, darf);
    }

    /// <summary>Eintrag der Stunde samt Antwort von UntPlanWeb (Block, Einzelstunden, geerbter Blockeintrag).</summary>
    private static async Task<(Eintrag Eintrag, bool Darf, JsonNode? Roh)> EintragRohAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        var j = await web.GetAsync(c, $"api/app/klassenbuch/stunde?{Stunde(k)}");
        return (EintragLesen(j?["eintrag"] as JsonObject), j?["darf"]?.GetValue<bool>() ?? false, j);
    }

    private static int? Zahl(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : null;
    private static string Text(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";

    /// <summary>
    /// Auswahl „Ganzer Block“ / Einzelstunde für die Stundendetails (Vertrag Einzelstunde): gewählte Stunde (<c>part</c>), Block
    /// (<c>block</c>), Stunden des Blocks (<c>parts</c>, <c>own</c> = eigener Einzelstunden-Eintrag) und in der Einzelstunde der geerbte
    /// Blockeintrag (<c>inherited</c>, nur lesend).
    /// </summary>
    public static JsonObject Teilauswahl(JsonNode? roh, int? einzelstunde, bool kollegium)
    {
        var block = roh?["block"] as JsonObject;
        var teile = new JsonArray((roh?["einzelstunden"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .Select(t => (JsonNode)new JsonObject
            {
                ["lesson"] = Zahl(t["stunde"]), ["start"] = Text(t["beginn"]), ["end"] = Text(t["ende"]), ["own"] = t["eintrag"]?.GetValue<bool>() ?? false,
            }).ToArray());
        JsonObject? geerbt = null;
        var gewaehlt = Zahl(roh?["einzelstunde"]) ?? einzelstunde;
        if (gewaehlt is not null && roh?["blockEintrag"] is JsonObject be)
        {
            var e = EintragLesen(be);
            geerbt = new JsonObject
            {
                ["lehrstoff"] = e.Thema, ["notes_students"] = e.NotizSchueler, ["notes_teachers"] = kollegium ? e.Notiz : "", ["online"] = e.Online,
            };
        }
        return new JsonObject
        {
            ["part"] = gewaehlt,
            ["block"] = block is null ? null : new JsonObject
            {
                ["from"] = Zahl(block["von"]), ["to"] = Zahl(block["bis"]), ["start"] = Text(block["beginn"]), ["end"] = Text(block["ende"]),
            },
            ["parts"] = teile,
            ["inherited"] = geerbt,
        };
    }

    /// <summary>Hausaufgaben der Stunde aus der Liste von UntPlanWeb (mit Id, Fälligkeit und Bemerkung).</summary>
    private static async Task<List<JsonObject>> HausaufgabenListeAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        try
        {
            var j = await web.GetAsync(c, $"api/app/klassenbuch/hausaufgabenliste?{Stunde(k)}");
            return (j?["liste"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().ToList();
        }
        catch (UntPlanWebFehler) { return new(); }
    }

    // part: Einzelstunde der Hausaufgabe (null = gilt für den ganzen Block)
    private static List<object> Hausaufgaben(IEnumerable<JsonObject> liste) =>
        liste.Select(h => (object)new { text = h["text"]?.GetValue<string>() ?? "", due = h["faellig"]?.GetValue<string>(), note = h["bemerkung"]?.GetValue<string>() ?? "", done = h["erledigt"]?.GetValue<bool>() ?? false, part = Zahl(h["einzelstunde"]) }).ToList();

    private static async Task<(object Inhalte, bool Darf, Eintrag Eintrag)> InhalteAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k)
    {
        var (inhalte, darf, e, _) = await InhalteRohAsync(c, web, konto, k);
        return (inhalte, darf, e);
    }

    private static async Task<(object Inhalte, bool Darf, Eintrag Eintrag, JsonNode? Roh)> InhalteRohAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k)
    {
        var (e, darf, roh) = await EintragRohAsync(c, web, k);
        return (new
        {
            lehrstoff = e.Thema,
            notes_students = e.NotizSchueler,
            notes_teachers = konto.Kollegium ? e.Notiz : "",
            homework = Hausaufgaben(await HausaufgabenListeAsync(c, web, k)),
            classbook = konto.Kollegium ? await KlasseneintraegeAsync(c, web, k) : new List<object>(),
        }, darf, e, roh);
    }

    private static Task EintragSpeichernAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k, Eintrag e) =>
        web.PutAsync(c, "api/app/klassenbuch/stunde", new
        {
            datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht,
            thema = e.Thema, hausaufgabe = e.Hausaufgabe, faellig = string.IsNullOrEmpty(e.Faellig) ? null : e.Faellig, notiz = e.Notiz, notizSchueler = e.NotizSchueler,
            lehrer = k.Lehrer.Length > 0 ? k.Lehrer : null, einzelstunde = k.Einzelstunde,
        });

    private static async Task<IResult> TextSpeichernAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k, string feld, string wert)
    {
        var (e, _) = await EintragAsync(c, web, k);
        e = feld switch
        {
            "lehrstoff" => e with { Thema = wert.Trim() },
            "notes_teachers" => e with { Notiz = wert.Trim() },
            "notes_students" => e with { NotizSchueler = wert.Trim() },
            _ => throw new UntPlanWebFehler(400, "Unbekanntes Feld."),
        };
        await EintragSpeichernAsync(c, web, k, e);
        return Results.Json(new { ok = true });
    }

    /// <summary>Hausaufgaben: Liste der Stunde in UntPlanWeb – anlegen, ändern (Text, Fälligkeit, Bemerkung, erledigt) und löschen.</summary>
    private static async Task<IResult> HausaufgabeAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k, string aktion, Parameter p)
    {
        var liste = await HausaufgabenListeAsync(c, web, k);
        var index = p.Zahl("index") ?? -1;
        var id = index >= 0 && index < liste.Count ? liste[index]["id"]!.GetValue<int>() : (int?)null;
        if (aktion != "addItem" && id is null) return Fehler("Eintrag nicht gefunden.");
        if (aktion == "delItem") await web.DeleteAsync(c, $"api/app/klassenbuch/hausaufgabenliste/{id}");
        else
        {
            var neu = JsonNode.Parse(p.Text("value", "{}")) as JsonObject;
            var text = (neu?["text"]?.GetValue<string>() ?? "").Trim();
            if (text.Length == 0) return Fehler("Ohne Text lässt sich nichts speichern.");
            var due = neu?["due"]?.GetValue<string>() is { Length: > 0 } d ? d : null;
            var note = neu?["note"]?.GetValue<string>();
            if (aktion == "addItem")
                await web.PostAsync(c, "api/app/klassenbuch/hausaufgabenliste", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, text, faellig = due, bemerkung = note ?? "", einzelstunde = k.Einzelstunde });
            else
                await web.PutAsync(c, $"api/app/klassenbuch/hausaufgabenliste/{id}", new { text, faellig = due, bemerkung = note, erledigt = neu?["done"]?.GetValue<bool>() });
        }
        return Results.Json(new { ok = true, data = Hausaufgaben(await HausaufgabenListeAsync(c, web, k)) });
    }

    /// <summary>Hausaufgaben der Klasse, die am Tag der Stunde fällig sind.</summary>
    private static async Task<List<object>> FaelligAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        if (k.Klasse.Length == 0) return new();
        try
        {
            var liste = await web.GetAsync(c, $"api/app/klassenbuch/klasse?klasse={Uri.EscapeDataString(k.Klasse)}&von={Iso(k.Datum.AddDays(-60))}&bis={Iso(k.Datum)}");
            return (liste?.AsArray() ?? new JsonArray())
                .Where(x => x?["faellig"]?.GetValue<string>() == Iso(k.Datum) && !string.IsNullOrWhiteSpace(x?["hausaufgabe"]?.GetValue<string>()))
                .Select(x => (object)new { text = x!["hausaufgabe"]!.GetValue<string>(), since = x["datum"]?.GetValue<string>(), note = x["fach"]?.GetValue<string>() ?? "" })
                .ToList();
        }
        catch (UntPlanWebFehler) { return new(); }
    }

    // ------------------------------------------------------------------------------------------ Vermerke

    private static async Task<JsonObject?> VermerkeAsync(HttpContext c, UntPlanWebVerbindung web, string klasse, DateOnly datum)
    {
        if (klasse.Length == 0) return null;
        try { return await web.GetAsync(c, $"api/app/klassenbuch/vermerke?klasse={Uri.EscapeDataString(klasse)}&von={Iso(datum)}&bis={Iso(datum)}") as JsonObject; }
        catch (UntPlanWebFehler) { return null; }
    }

    private static async Task<List<object>> ArtenAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k) =>
        ((await VermerkeAsync(c, web, k.Klasse, k.Datum))?["arten"]?.AsArray() ?? new JsonArray())
        .Select(a => (object)new { code = a!.GetValue<string>(), name = a.GetValue<string>() }).ToList();

    private static async Task<List<JsonObject>> KlassenVermerkeAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k) =>
        ((await VermerkeAsync(c, web, k.Klasse, k.Datum))?["vermerke"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>().Where(v => v["schuelerId"] is null).ToList();

    private static async Task<List<object>> KlasseneintraegeAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k) =>
        (await KlassenVermerkeAsync(c, web, k)).Select(v => (object)new
        {
            id = v["id"]!.GetValue<int>(), text = v["text"]?.GetValue<string>() ?? "", cat = v["art"]?.GetValue<string>() ?? "",
            at = v["datum"]?.GetValue<string>(), author = v["lehrer"]?.GetValue<string>() ?? "",
        }).ToList();

    private static async Task<IResult> KlasseneintragAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k, string aktion, Parameter p)
    {
        var liste = await KlassenVermerkeAsync(c, web, k);
        var index = p.Zahl("index") ?? -1;
        var alt = index >= 0 && index < liste.Count ? liste[index]["id"]!.GetValue<int>() : (int?)null;
        if (aktion != "addItem" && alt is null) return Fehler("Eintrag nicht gefunden.");
        if (aktion != "delItem")
        {
            var neu = JsonNode.Parse(p.Text("value", "{}")) as JsonObject;
            var text = (neu?["text"]?.GetValue<string>() ?? "").Trim();
            if (text.Length == 0) return Fehler("Ohne Text lässt sich nichts speichern.");
            await web.PostAsync(c, "api/app/klassenbuch/vermerke", new { datum = Iso(k.Datum), klasse = k.Klasse, schuelerId = (int?)null, art = neu?["cat"]?.GetValue<string>() ?? "Vermerk", text });
        }
        if (alt is int id) await web.DeleteAsync(c, $"api/app/klassenbuch/vermerke/{id}");
        return Results.Json(new { ok = true, data = await KlasseneintraegeAsync(c, web, k) });
    }

    private static async Task<List<string>> KlassenAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        try
        {
            var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
            var liste = (aw?["schueler"]?.AsArray() ?? new JsonArray()).Select(s => s?["klasse"]?.GetValue<string>() ?? "").Where(x => x.Length > 0).Distinct().ToList();
            return liste.Count > 0 ? liste : new List<string> { k.Klasse };
        }
        catch (UntPlanWebFehler) { return new List<string> { k.Klasse }; }
    }

    /// <summary>Bemerkungen zu Schüler*innen (Vermerke mit Person) am Tag der Stunde, je Schüler-ID.</summary>
    private static async Task<Dictionary<string, List<object>>> BemerkungenAsync(HttpContext c, UntPlanWebVerbindung web, IEnumerable<string> klassen, Schluessel k)
    {
        var res = new Dictionary<string, List<object>>();
        foreach (var kl in klassen.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var v in ((await VermerkeAsync(c, web, kl, k.Datum))?["vermerke"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>())
            {
                if (v["schuelerId"]?.GetValue<int>() is not int sid) continue;
                var key = sid.ToString(CultureInfo.InvariantCulture);
                if (!res.TryGetValue(key, out var l)) res[key] = l = new List<object>();
                l.Add(new { id = v["id"]!.GetValue<int>(), type = v["art"]?.GetValue<string>() ?? "", text = v["text"]?.GetValue<string>() ?? "" });
            }
        return res;
    }

    // ------------------------------------------------------------------------------------------ Anwesenheit

    private static string Beginn(Konto konto, int stunde) => (konto.Elemente.Stunden ?? new()).FirstOrDefault(z => z.Nr == stunde)?.Beginn ?? "";

    private static async Task<(List<JsonObject> Liste, bool Geprueft, string? Hinweis)> SchuelerAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k)
    {
        JsonNode? aw;
        try { aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}"); }
        catch (UntPlanWebFehler ex) { return (new List<JsonObject>(), false, ex.Message); }
        var fehlzeiten = (aw?["fehlzeiten"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .GroupBy(f => f["schuelerId"]!.GetValue<int>()).ToDictionary(g => g.Key, g => g.OrderBy(f => f["stunde"]!.GetValue<int>()).First());
        var schueler = (aw?["schueler"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().ToList();
        // In einer früheren Stunde desselben Tages abwesend, hier (noch) nicht eingetragen → Hinweis „War in Vorstunde abwesend!“
        var vorstunde = (aw?["vorstunden"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .Where(v => v["schuelerId"] is not null).GroupBy(v => v["schuelerId"]!.GetValue<int>())
            .ToDictionary(g => g.Key, g => g.Max(v => v["stunde"]?.GetValue<int>() ?? 0));
        var bemerkungen = await BemerkungenAsync(c, web, schueler.Select(s => s["klasse"]?.GetValue<string>() ?? ""), k);
        var dienste = await DiensteAsync(c, web, schueler.Select(s => s["klasse"]?.GetValue<string>() ?? ""), k.Datum);
        var beginn = Beginn(konto, k.Stunde);
        var liste = schueler.Select(s =>
        {
            var id = s["id"]!.GetValue<int>();
            fehlzeiten.TryGetValue(id, out var f);
            var art = f?["art"]?.GetValue<string>();
            var minuten = f?["minuten"]?.GetValue<int>() ?? 0;
            string? Text(string feld) => f?[feld] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
            // Ankunft = Ende des Fehlfensters (minutengenau), sonst Stundenbeginn + Minuten
            var ankunft = art == "verspaetet" ? (Text("ende") is { Length: > 0 } en ? en : beginn.Length > 0 ? Kachelbau.Minus(beginn, -minuten) : "") : "";
            var gegangen = art == "vorzeitig" ? Text("beginn") ?? "" : "";
            return new JsonObject
            {
                ["id"] = id,
                // wie im Vorbild: „Nachname Vorname“, sortiert nach Nachnamen
                ["name"] = $"{s["name"]?.GetValue<string>()} {s["vorname"]?.GetValue<string>()}".Trim(),
                ["class"] = s["klasse"]?.GetValue<string>() ?? "",
                ["duties"] = new JsonArray(dienste.GetValueOrDefault(id, new List<string>()).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["state"] = art is "fehlt" or "vorzeitig" ? "absent" : art == "verspaetet" ? "late" : "present",
                ["note"] = Text("text") ?? f?["bemerkung"]?.GetValue<string>() ?? "",
                ["arrival"] = ankunft,
                ["gone"] = gegangen,
                ["reason"] = Text("grund") ?? "",
                // Zeitraum der ganzen Abwesenheit (Abwesenheit bearbeiten, minutengenau)
                ["from_date"] = Text("vonDatum") ?? "", ["from_time"] = Text("vonZeit") ?? "",
                ["to_date"] = Text("bisDatum") ?? "", ["to_time"] = Text("bisZeit") ?? "",
                ["seit"] = f is null && vorstunde.ContainsKey(id),
                ["prevLesson"] = vorstunde.TryGetValue(id, out var vs) ? vs : 0,
                ["remarks"] = JsonSerializer.SerializeToNode(bemerkungen.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture)) ?? new List<object>()),
                ["locked"] = false,
                ["excused"] = f?["status"]?.GetValue<string>() == "entschuldigt",
            };
        }).OrderBy(s => s["name"]!.GetValue<string>(), StringComparer.Create(new CultureInfo("de-DE"), true)).ToList();
        return (liste, aw?["geprueft"] is JsonValue g && g.GetValueKind() == JsonValueKind.String, null);
    }

    private static DateTime? Zeitpunkt(string datum, string zeit) =>
        DateTime.TryParseExact($"{datum.Trim()} {zeit.Trim()}", new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Abwesenheitsgründe aus UntPlanWeb (Art „abwesenheit“, aktiv) als Kürzel und Name.</summary>
    private static async Task<List<object>> GruendeAsync(HttpContext c, UntPlanWebVerbindung web)
    {
        try
        {
            return ((await web.GetAsync(c, "api/app/abwesenheitsgruende?art=abwesenheit"))?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                .Where(g => g["aktiv"]?.GetValue<bool>() ?? true)
                .Select(g => (object)new { code = g["kurz"]?.GetValue<string>() ?? "", name = g["name"]?.GetValue<string>() ?? "" })
                .ToList();
        }
        catch (UntPlanWebFehler) { return new(); }
    }

    /// <summary>
    /// Abwesenheiten einer Schülerin / eines Schülers (Ansicht „Abwesenheiten“ aus der Schülerliste): für die Klassenleitung alle Zeiträume
    /// der letzten Wochen aus UntPlanWeb, sonst die Abwesenheit in dieser Stunde. manage = Zeiträume über den Tag hinaus bearbeitbar.
    /// </summary>
    private static async Task<IResult> AbwesenheitenAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k, int sid)
    {
        if (sid <= 0) return Fehler("Bitte eine Schülerin bzw. einen Schüler wählen.");
        var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
        var klasse = aw?["schueler"]?.AsArray().FirstOrDefault(s => s?["id"]?.GetValue<int>() == sid)?["klasse"]?.GetValue<string>() ?? k.Klasse;
        var verwalten = aw?["verwalten"]?.GetValue<bool>() ?? false;
        var gruende = (await GruendeAsync(c, web)).Select(g => JsonSerializer.SerializeToNode(g)!).ToDictionary(g => g["code"]!.GetValue<string>(), g => g["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        string Name(string g) => gruende.GetValueOrDefault(g, g);
        string S(JsonNode? n, string feld) => n?[feld] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
        var liste = new List<object>();
        if (verwalten)
        {
            try
            {
                var zr = await web.GetAsync(c, $"api/app/abwesenheiten/zeitraeume?klasse={Uri.EscapeDataString(klasse)}&von={Iso(k.Datum.AddDays(-120))}&bis={Iso(k.Datum.AddDays(60))}");
                foreach (var z in (zr?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Where(z => z["schuelerId"]?.GetValue<int>() == sid))
                    liste.Add(new
                    {
                        key = S(z, "schluessel"), fromDate = S(z, "vonDatum"), fromTime = S(z, "vonZeit"), toDate = S(z, "bisDatum"), toTime = S(z, "bisZeit"),
                        reason = S(z, "grund"), reasonName = Name(S(z, "grund")), text = S(z, "text"), status = S(z, "status"), art = S(z, "art"),
                        notified = z["elternInformiert"]?.GetValue<bool>() ?? false,
                        ids = z["ids"]?.AsArray().Select(i => i!.GetValue<int>()).ToList() ?? new List<int>(),
                        current = (z["ids"]?.AsArray() ?? new JsonArray()).Any(i => (aw?["fehlzeiten"]?.AsArray() ?? new JsonArray()).Any(f => f?["id"]?.GetValue<int>() == i?.GetValue<int>())),
                    });
            }
            catch (UntPlanWebFehler) { verwalten = false; }
        }
        if (!verwalten)
            foreach (var f in (aw?["fehlzeiten"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Where(f => f["schuelerId"]?.GetValue<int>() == sid))
                liste.Add(new
                {
                    key = $"{sid}:{f["id"]}", fromDate = S(f, "vonDatum"), fromTime = S(f, "vonZeit"), toDate = S(f, "bisDatum"), toTime = S(f, "bisZeit"),
                    reason = S(f, "grund"), reasonName = Name(S(f, "grund")), text = S(f, "text"), status = S(f, "status"), art = S(f, "art"),
                    notified = false, ids = new List<int>(), current = true,
                });
        return Results.Json(new { ok = true, manage = verwalten, items = liste });
    }

    /// <summary>Abwesenheit löschen: in dieser Stunde (Lehrkraft) bzw. die angegebenen Fehlzeiten (Klassenleitung).</summary>
    private static async Task<IResult> AbwesenheitLoeschenAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k, Parameter p)
    {
        var sid = p.Zahl("student_id") ?? 0;
        var ids = p["ids"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0).Where(i => i > 0).Distinct().ToList();
        if (ids.Count > 0 && p["current"] != "1")
            foreach (var id in ids) await web.DeleteAsync(c, $"api/app/klassenbuch/fehlzeiten/{id}");
        else
            await web.PostAsync(c, "api/app/klassenbuch/abwesenheit/loeschen", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, schuelerIds = new[] { sid }, einzelstunde = k.Einzelstunde });
        return Results.Json(new { ok = true });
    }

    /// <summary>Klassendienste (z. B. Klassensprecher*in) am Tag der Stunde, je Schüler-ID – Zusatz in der Schülerliste.</summary>
    private static async Task<Dictionary<int, List<string>>> DiensteAsync(HttpContext c, UntPlanWebVerbindung web, IEnumerable<string> klassen, DateOnly datum)
    {
        var res = new Dictionary<int, List<string>>();
        foreach (var kl in klassen.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            JsonNode? j;
            try { j = await web.GetAsync(c, $"api/app/klassendienste?klasse={Uri.EscapeDataString(kl)}&von={Iso(datum)}&bis={Iso(datum)}"); }
            catch (UntPlanWebFehler) { continue; }
            foreach (var z in (j?["zuordnungen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>())
            {
                if (z["schuelerId"]?.GetValue<int>() is not int sid) continue;
                var name = z["dienst"]?.GetValue<string>() ?? "";
                if (name.Length == 0) continue;
                if (!res.TryGetValue(sid, out var l)) res[sid] = l = new List<string>();
                if (!l.Contains(name)) l.Add(name);
            }
        }
        return res;
    }

    private sealed record AnwesenheitEintrag(int SchuelerId, string Art, int Minuten, string? Bemerkung);

    /// <summary>Bisherige Abweichungen der Stunde (je Schüler*in eine, wie sie UntPlanWeb beim Speichern erwartet).</summary>
    private static List<AnwesenheitEintrag> Eintraege(JsonNode? aw) =>
        (aw?["fehlzeiten"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .GroupBy(f => f["schuelerId"]!.GetValue<int>())
            .Select(g => g.OrderBy(f => f["stunde"]!.GetValue<int>()).First())
            .Select(f => new AnwesenheitEintrag(f["schuelerId"]!.GetValue<int>(), f["art"]!.GetValue<string>(), f["minuten"]?.GetValue<int>() ?? 0, f["bemerkung"]?.GetValue<string>()))
            .ToList();

    private static async Task<IResult> AnwesenheitAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k, Parameter p)
    {
        var sid = p.Zahl("student_id") ?? 0;
        // Abwesenheit bearbeiten: Zeitraum von–bis (minutengenau) mit Grund, Text und Benachrichtigung
        if (p["state"] is "absent" or "late" && Zeitpunkt(p["from_date"], p["from_time"]) is DateTime von && Zeitpunkt(p["to_date"], p["to_time"]) is DateTime bis)
        {
            if (bis <= von) return Fehler("Das Ende der Abwesenheit muss nach dem Beginn liegen.");
            var ersetzen = p["replace"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0).Where(i => i > 0).ToList();
            var andere = p["scope"] == "other" && ersetzen.Count > 0;
            await web.PostAsync(c, "api/app/klassenbuch/abwesenheit", new
            {
                schuelerIds = new[] { sid }, von, bis, grund = p["reason"].Trim(), text = p["note"].Trim(),
                // scope=other: anderer Zeitraum der Klassenleitung – ohne Bezug zu dieser Stunde, ersetzt nur die angegebenen Fehlzeiten
                datum = andere ? null : Iso(k.Datum), stunde = andere ? (int?)null : k.Stunde, unterricht = andere ? null : k.Unterricht,
                ersetzen = ersetzen.Count > 0 ? ersetzen : null, einzelstunde = andere ? null : k.Einzelstunde,
                elternInformiert = p["notified"] is "1" or "true" ? true : p["notified"] is "0" or "false" ? false : (bool?)null,
            });
            return Results.Json(new { ok = true, @checked = true });
        }
        var aw = await web.GetAsync(c, $"api/app/klassenbuch/anwesenheit?{Stunde(k)}");
        var eintraege = Eintraege(aw).Where(e => e.SchuelerId != sid).ToList();
        var bemerkung = p["note"].Trim();
        switch (p["state"])
        {
            case "absent":
                eintraege.Add(new AnwesenheitEintrag(sid, "fehlt", 0, bemerkung));
                break;
            case "late":
            {
                var beginn = Beginn(konto, k.Stunde);
                var minuten = 5;
                if (TimeOnly.TryParse(beginn, CultureInfo.InvariantCulture, out var b) && TimeOnly.TryParse(p["arrival"], CultureInfo.InvariantCulture, out var a))
                    minuten = (int)Math.Clamp((a - b).TotalMinutes, 1, 90);
                eintraege.Add(new AnwesenheitEintrag(sid, "verspaetet", minuten, bemerkung));
                break;
            }
        }
        await web.PutAsync(c, "api/app/klassenbuch/anwesenheit", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, eintraege, einzelstunde = k.Einzelstunde });
        return Results.Json(new { ok = true, @checked = true });
    }
}
