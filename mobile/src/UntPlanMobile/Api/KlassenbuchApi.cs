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
/// <item>Abwesenheitsgründe und Benachrichtigung bietet die App nicht an.</item>
/// </list>
/// </summary>
public static class KlassenbuchApi
{
    private sealed record Schluessel(DateOnly Datum, int Stunde, int? Unterricht, string Klasse);

    private static Schluessel? SchluesselLesen(Parameter p)
    {
        var datum = p.Datum("date");
        var stunde = p.Zahl("lesson");
        if (datum is null || stunde is null) return null;
        return new Schluessel(datum.Value, stunde.Value, p.Zahl("unterricht") ?? p.Zahl("entry"), p["class"].Split(',')[0].Trim());
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Stunde(Schluessel k) => $"datum={Iso(k.Datum)}&stunde={k.Stunde}&unterricht={k.Unterricht}";

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
                    var (inhalte, darf, online) = await InhalteAsync(c, web, konto, k);
                    var (schueler, geprueft, _) = konto.Kollegium ? await SchuelerAsync(c, web, konto, k) : (new List<JsonObject>(), false, null);
                    return Results.Json(new
                    {
                        // canEdit: eigene Stunde (UntPlanWeb erlaubt das Klassenbuch), online: „Findet online statt“
                        ok = true, canEdit = darf && konto.Kollegium, online, data = inhalte, students = schueler, absentClasses = Array.Empty<string>(),
                        dueHomework = await FaelligAsync(c, web, k), @checked = geprueft,
                        absent = schueler.Count(s => s["state"]?.GetValue<string>() == "absent"), reasons = Array.Empty<object>(),
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
                    // Abwesenheitsgründe kennt UntPlanWeb nicht – die Oberfläche bietet dann nur „Ohne Angabe“ an
                    return Results.Json(new { ok = true, reasons = Array.Empty<object>() });
                case "remarkTypes":
                    return Results.Json(new { ok = true, types = await ArtenAsync(c, web, k) });
                case "online":
                {
                    // „Findet online statt“ (Schalter in den Stundendetails) → Klassenbuch der Stunde in UntPlanWeb
                    var an = p["value"] is "1" or "true" or "on";
                    await web.PutAsync(c, "api/app/klassenbuch/online", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, online = an });
                    return Results.Json(new { ok = true, online = an });
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
                    await web.PutAsync(c, "api/app/klassenbuch/anwesenheit", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, eintraege = Eintraege(aw) });
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

    private sealed record Eintrag(string Thema, string Hausaufgabe, string? Faellig, string Notiz, string NotizSchueler = "", bool Online = false);

    private static async Task<(Eintrag Eintrag, bool Darf)> EintragAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k)
    {
        var j = await web.GetAsync(c, $"api/app/klassenbuch/stunde?{Stunde(k)}");
        var e = j?["eintrag"] as JsonObject;
        return (new Eintrag(e?["thema"]?.GetValue<string>() ?? "", e?["hausaufgabe"]?.GetValue<string>() ?? "", e?["faellig"]?.GetValue<string>(),
            e?["notiz"]?.GetValue<string>() ?? "", e?["notizSchueler"]?.GetValue<string>() ?? "", e?["online"]?.GetValue<bool>() ?? false), j?["darf"]?.GetValue<bool>() ?? false);
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

    private static List<object> Hausaufgaben(IEnumerable<JsonObject> liste) =>
        liste.Select(h => (object)new { text = h["text"]?.GetValue<string>() ?? "", due = h["faellig"]?.GetValue<string>(), note = h["bemerkung"]?.GetValue<string>() ?? "", done = h["erledigt"]?.GetValue<bool>() ?? false }).ToList();

    private static async Task<(object Inhalte, bool Darf, bool Online)> InhalteAsync(HttpContext c, UntPlanWebVerbindung web, Konto konto, Schluessel k)
    {
        var (e, darf) = await EintragAsync(c, web, k);
        return (new
        {
            lehrstoff = e.Thema,
            notes_students = e.NotizSchueler,
            notes_teachers = konto.Kollegium ? e.Notiz : "",
            homework = Hausaufgaben(await HausaufgabenListeAsync(c, web, k)),
            classbook = konto.Kollegium ? await KlasseneintraegeAsync(c, web, k) : new List<object>(),
        }, darf, e.Online);
    }

    private static Task EintragSpeichernAsync(HttpContext c, UntPlanWebVerbindung web, Schluessel k, Eintrag e) =>
        web.PutAsync(c, "api/app/klassenbuch/stunde", new
        {
            datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht,
            thema = e.Thema, hausaufgabe = e.Hausaufgabe, faellig = string.IsNullOrEmpty(e.Faellig) ? null : e.Faellig, notiz = e.Notiz, notizSchueler = e.NotizSchueler,
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
                await web.PostAsync(c, "api/app/klassenbuch/hausaufgabenliste", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, text, faellig = due, bemerkung = note ?? "" });
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
        var bemerkungen = await BemerkungenAsync(c, web, schueler.Select(s => s["klasse"]?.GetValue<string>() ?? ""), k);
        var dienste = await DiensteAsync(c, web, schueler.Select(s => s["klasse"]?.GetValue<string>() ?? ""), k.Datum);
        var beginn = Beginn(konto, k.Stunde);
        var liste = schueler.Select(s =>
        {
            var id = s["id"]!.GetValue<int>();
            fehlzeiten.TryGetValue(id, out var f);
            var art = f?["art"]?.GetValue<string>();
            var minuten = f?["minuten"]?.GetValue<int>() ?? 0;
            var ankunft = art == "verspaetet" && beginn.Length > 0 ? Kachelbau.Minus(beginn, -minuten) : "";
            return new JsonObject
            {
                ["id"] = id,
                // wie im Vorbild: „Nachname Vorname“, sortiert nach Nachnamen
                ["name"] = $"{s["name"]?.GetValue<string>()} {s["vorname"]?.GetValue<string>()}".Trim(),
                ["class"] = s["klasse"]?.GetValue<string>() ?? "",
                ["duties"] = new JsonArray(dienste.GetValueOrDefault(id, new List<string>()).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["state"] = art == "fehlt" ? "absent" : art == "verspaetet" ? "late" : "present",
                ["note"] = f?["bemerkung"]?.GetValue<string>() ?? "",
                ["arrival"] = ankunft,
                ["reason"] = "",
                ["remarks"] = JsonSerializer.SerializeToNode(bemerkungen.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture)) ?? new List<object>()),
                ["locked"] = false,
                ["excused"] = f?["status"]?.GetValue<string>() == "entschuldigt",
            };
        }).OrderBy(s => s["name"]!.GetValue<string>(), StringComparer.Create(new CultureInfo("de-DE"), true)).ToList();
        return (liste, aw?["geprueft"] is JsonValue g && g.GetValueKind() == JsonValueKind.String, null);
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
        await web.PutAsync(c, "api/app/klassenbuch/anwesenheit", new { datum = Iso(k.Datum), stunde = k.Stunde, unterricht = k.Unterricht, eintraege });
        return Results.Json(new { ok = true, @checked = true });
    }
}
