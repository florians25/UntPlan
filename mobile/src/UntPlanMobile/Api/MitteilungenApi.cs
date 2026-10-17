using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Mitteilungen (Ersatz für api/messages.php) über /api/app/mitteilungen von UntPlanWeb:
/// <list type="bullet">
/// <item>Posteingang, Gesendet, Entwürfe; Detail mit Anhängen (Download über <c>action=attachment</c>), gelesen markieren.</item>
/// <item>Empfängerauswahl „Kolleg*innen“ / „Individuell“ (<c>action=people</c>): Personen mit Rollen-Pills, Filter Liste, Klasse, Abteilung.</item>
/// <item>Senden mit Lesebestätigung, „Antwort nicht erlaubt“ und Anhängen (Kamera, Foto, Datei – über einen Entwurf), Antworten, Löschen.</item>
/// </list>
/// </summary>
public static class MitteilungenApi
{
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    private static string S(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
    private static int? Z(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : null;
    private static bool? B(JsonNode? n) => n is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;
    private static JsonArray Texte(IEnumerable<string> werte) => new(werte.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());

    /// <summary>Initialen für den Avatar: „NEU“ → „N“, „Anna Abbott“ → „AA“, „ALE_bkgl“ → „A“.</summary>
    public static string Initialen(string name)
    {
        var teile = new string(name.Select(ch => char.IsLetter(ch) ? ch : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length == 0) return "?";
        // Kürzel (ein Wort in Großbuchstaben) → nur der erste Buchstabe, wie in der iOS-App
        if (teile.Length == 1) return teile[0][..1].ToUpper(CultureInfo.GetCultureInfo("de-DE"));
        return (teile[0][..1] + teile[1][..1]).ToUpper(CultureInfo.GetCultureInfo("de-DE"));
    }

    private static JsonArray Anhaenge(JsonObject m) =>
        new((m["anhaenge"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(a => (JsonNode)new JsonObject
        {
            ["id"] = Z(a["id"]) ?? 0, ["name"] = S(a["name"]), ["type"] = S(a["typ"]), ["size"] = Z(a["groesse"]) ?? 0,
        }).ToArray());

    /// <summary>Mitteilung von UntPlanWeb in der Form der Oberfläche.</summary>
    public static JsonObject Eintrag(JsonObject m, string ordner)
    {
        var eingang = ordner == "posteingang";
        var entwurf = ordner == "entwuerfe";
        var empfaengerNamen = (m["empfaenger"]?.AsArray() ?? new JsonArray()).Select(n => S(n)).Where(x => x.Length > 0).ToList();
        var kennungen = (m["entwurfEmpfaenger"]?.AsArray() ?? new JsonArray()).Select(n => S(n)).ToList();
        var empfaenger = new JsonArray(empfaengerNamen.Select((n, i) => (JsonNode)new JsonObject
        {
            ["label"] = n, ["token"] = i < kennungen.Count ? kennungen[i] : "", ["kind"] = "",
        }).ToArray());
        var absender = eingang ? S(m["absender"]) : "Ich";
        var anhaenge = Anhaenge(m);
        return new JsonObject
        {
            ["id"] = Z(m["id"]) ?? 0,
            ["status"] = entwurf ? "draft" : "sent",
            ["box"] = ordner,
            ["sender"] = absender,
            ["senderRole"] = eingang ? S(m["absenderRolle"]) : "",
            ["senderToken"] = eingang && Z(m["absenderId"]) is int aid ? $"benutzer:{aid}" : "",
            ["initials"] = Initialen(eingang ? absender : empfaengerNamen.FirstOrDefault() ?? S(m["empfaengerText"])),
            ["subject"] = S(m["betreff"]),
            ["body"] = S(m["text"]),
            ["sentAt"] = S(m["erstellt"]),
            ["read"] = !eingang || m["gelesen"] is JsonValue,
            ["important"] = B(m["wichtig"]) ?? false,
            ["recipients"] = empfaenger,
            ["recipientsText"] = S(m["empfaengerText"]),
            ["readReceipt"] = B(m["lesebestaetigung"]) ?? false,
            ["confirmed"] = m["bestaetigt"] is JsonValue,
            // „Antwort nicht erlaubt“ des Absenders (fehlt bei älteren Servern → erlaubt)
            ["replyAllowed"] = B(m["antwortErlaubt"]) ?? true,
            ["canReply"] = eingang && (B(m["darfAntworten"]) ?? B(m["antwortErlaubt"]) ?? true) && S(m["absender"]).Length > 0,
            ["replyTo"] = Z(m["antwortAuf"]),
            ["attachmentCount"] = anhaenge.Count,
            ["attachments"] = anhaenge,
        };
    }

    private static string Ordner(string box) => box switch { "sent" => "gesendet", "drafts" => "entwuerfe", _ => "posteingang" };

    private static async Task<List<JsonObject>> ListeAsync(HttpContext c, UntPlanWebVerbindung web, string ordner) =>
        ((await web.GetAsync(c, $"api/app/mitteilungen?ordner={ordner}"))?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>().Select(m => Eintrag(m, ordner)).ToList();

    /// <summary>Detail einer Mitteilung (UntPlanWeb: GET /mitteilungen/{id}); ältere Server: Suche in den Ordnern.</summary>
    private static async Task<JsonObject?> DetailAsync(HttpContext c, UntPlanWebVerbindung web, int id)
    {
        try
        {
            if (await web.GetAsync(c, $"api/app/mitteilungen/{id}") is JsonObject m)
                return Eintrag(m, S(m["ordner"]) is { Length: > 0 } o ? o : "posteingang");
        }
        catch (UntPlanWebFehler ex) when (ex.Status is 404 or 405) { }
        foreach (var ordner in new[] { "posteingang", "gesendet", "entwuerfe" })
        {
            try
            {
                var m = (await ListeAsync(c, web, ordner)).FirstOrDefault(x => Z(x["id"]) == id);
                if (m != null) return m;
            }
            catch (UntPlanWebFehler) { }
        }
        return null;
    }

    private static string Art(string art) => art switch
    {
        "gruppe" => "group",
        "klasse" or "Schüler*in / Klasse" => "student",
        _ => "teacher",
    };

    // ------------------------------------------------------------------------------------------ Empfängerauswahl

    private static readonly Dictionary<string, string> RollenArt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lehrer"] = "lehrkraft", ["Lehrkraft"] = "lehrkraft", ["schulleitung"] = "schulleitung", ["Schulleitung"] = "schulleitung",
        ["admin"] = "administration", ["Administration"] = "administration", ["schueler"] = "schueler", ["Schüler*in"] = "schueler",
        ["eltern"] = "eltern", ["Erziehungsberechtigte"] = "eltern",
    };

    /// <summary>Personen der Empfängerauswahl in der Form der Oberfläche (aus /mitteilungen/personen).</summary>
    public static JsonObject Personen(JsonObject antwort, string typ)
    {
        var personen = new JsonArray((antwort["personen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(p =>
        {
            var name = S(p["name"]);
            return (JsonNode)new JsonObject
            {
                ["token"] = S(p["id"]),
                ["label"] = name,
                ["fullName"] = S(p["anzeigename"]),
                ["short"] = S(p["kuerzel"]),
                ["initials"] = S(p["initialen"]) is { Length: > 0 } i ? i : Initialen(name),
                ["category"] = S(p["kategorie"]),
                ["roles"] = new JsonArray((p["rollen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(r => (JsonNode)new JsonObject
                {
                    ["kind"] = S(r["art"]), ["text"] = S(r["text"]),
                }).ToArray()),
                ["classes"] = Texte((p["klassen"]?.AsArray() ?? new JsonArray()).Select(S)),
                ["departments"] = Texte((p["abteilungen"]?.AsArray() ?? new JsonArray()).Select(S)),
            };
        }).ToArray());
        var gruppen = new JsonArray((antwort["gruppen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(g => (JsonNode)new JsonObject
        {
            ["token"] = S(g["id"]), ["label"] = S(g["name"]), ["initials"] = Initialen(S(g["name"])), ["category"] = S(g["kategorie"]),
            ["roles"] = new JsonArray(new JsonObject { ["kind"] = "gruppe", ["text"] = S(g["art"]) is { Length: > 0 } a ? (a == "klasse" ? "Klasse" : a == "gruppe" ? "Gruppe" : a) : "Gruppe" }),
            ["classes"] = Texte(S(g["klasse"]) is { Length: > 0 } k ? new[] { k } : Array.Empty<string>()),
            ["departments"] = new JsonArray(),
        }).ToArray());
        var filter = antwort["filter"] as JsonObject;
        return new JsonObject
        {
            ["ok"] = true,
            ["type"] = S(antwort["typ"]) is { Length: > 0 } t ? t : typ,
            ["items"] = personen,
            ["groups"] = gruppen,
            ["lists"] = new JsonArray((filter?["listen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(l => (JsonNode)new JsonObject
            {
                ["token"] = S(l["id"]), ["name"] = S(l["name"]), ["members"] = Texte((l["mitglieder"]?.AsArray() ?? new JsonArray()).Select(S)),
            }).ToArray()),
            ["classes"] = Texte((filter?["klassen"]?.AsArray() ?? new JsonArray()).Select(S)),
            ["departments"] = Texte((filter?["abteilungen"]?.AsArray() ?? new JsonArray()).Select(S)),
        };
    }

    /// <summary>Ältere Server ohne /personen: Personen aus der bisherigen Empfängerliste.</summary>
    private static JsonObject PersonenAusEmpfaengern(JsonArray liste, string typ)
    {
        var personen = new JsonArray();
        var gruppen = new JsonArray();
        foreach (var e in liste.OfType<JsonObject>())
        {
            var id = S(e["id"]);
            var art = S(e["art"]);
            var kategorie = S(e["kategorie"]);
            if (typ == "kollegium" && kategorie != "lehrkraefte") continue;
            var eintrag = new JsonObject
            {
                ["id"] = id, ["name"] = S(e["name"]), ["kategorie"] = kategorie, ["art"] = art,
                ["klassen"] = Texte(S(e["klasse"]) is { Length: > 0 } k ? new[] { k } : Array.Empty<string>()),
                ["rollen"] = new JsonArray(new JsonObject { ["art"] = RollenArt.GetValueOrDefault(art, "lehrkraft"), ["text"] = art }),
            };
            if (id.StartsWith("benutzer:", StringComparison.Ordinal)) personen.Add(eintrag);
            else if (typ != "kollegium") gruppen.Add(new JsonObject { ["id"] = id, ["name"] = S(e["name"]), ["art"] = art, ["kategorie"] = kategorie, ["klasse"] = S(e["klasse"]) });
        }
        var klassen = personen.OfType<JsonObject>().SelectMany(p => p["klassen"]!.AsArray().Select(S)).Distinct().Order().ToList();
        return new JsonObject
        {
            ["typ"] = typ, ["personen"] = personen, ["gruppen"] = gruppen,
            ["filter"] = new JsonObject { ["listen"] = new JsonArray(), ["klassen"] = Texte(klassen), ["abteilungen"] = new JsonArray() },
        };
    }

    // ------------------------------------------------------------------------------------------ Senden

    private static List<string> Kennungen(Parameter p)
    {
        try { return (JsonSerializer.Deserialize<List<string>>(p.Text("recipients", "[]")) ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(); }
        catch (JsonException) { return new(); }
    }

    private static bool Ja(Parameter p, string name) => p[name] is "1" or "true" or "on";

    private static async Task<IResult> SendenAsync(HttpContext c, UntPlanWebVerbindung web, Parameter p, bool senden)
    {
        var empfaenger = Kennungen(p);
        if (senden && empfaenger.Count == 0) return Fehler("recipient_required");
        if (senden && string.IsNullOrWhiteSpace(p["subject"])) return Fehler("subject_required");
        var dateien = c.Request.HasFormContentType ? c.Request.Form.Files.ToList() : new List<IFormFile>();
        var entwurfId = p.Zahl("draft_id") is int d and > 0 ? d : (int?)null;
        var daten = new
        {
            id = entwurfId, empfaenger, betreff = p["subject"].Trim(), text = p["body"], wichtig = Ja(p, "important"),
            lesebestaetigung = Ja(p, "read_receipt"), antwortAuf = p.Zahl("reply_to") is int r and > 0 ? r : (int?)null,
            // „Antwort nicht erlaubt“ (Standard: Antwort erlaubt)
            antwortErlaubt = !Ja(p, "no_reply"),
        };
        // Ohne Anhänge und ohne Entwurf direkt senden, sonst über einen Entwurf (Anhänge hängen an einer gespeicherten Mitteilung)
        if (senden && dateien.Count == 0 && entwurfId is null)
        {
            var antwort = await web.PostAsync(c, "api/app/mitteilungen", new
            {
                daten.empfaenger, daten.betreff, daten.text, daten.wichtig, daten.lesebestaetigung, daten.antwortAuf, daten.antwortErlaubt,
            });
            return Results.Json(new { ok = true, id = Z(antwort?["id"]) });
        }
        var id = Z((await web.PostAsync(c, "api/app/mitteilungen/entwurf", daten))?["id"]) ?? throw new UntPlanWebFehler(502, "Der Entwurf ließ sich nicht speichern.");
        foreach (var datei in dateien)
        {
            await using var inhalt = datei.OpenReadStream();
            await web.HochladenAsync(c, $"api/app/mitteilungen/{id}/anhaenge", "datei", Path.GetFileName(datei.FileName), datei.ContentType ?? "application/octet-stream", inhalt);
        }
        if (senden) id = Z((await web.PostAsync(c, $"api/app/mitteilungen/{id}/senden", new { }))?["id"]) ?? id;
        return Results.Json(new { ok = true, id });
    }

    private static string Dateityp(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".heic" => "image/heic",
        ".txt" => "text/plain", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
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
                    var liste = await ListeAsync(c, web, Ordner(p.Text("box", "inbox")));
                    var q = p["q"].Trim();
                    if (q.Length > 0)
                        liste = liste.Where(m => new[] { "sender", "subject", "body" }.Any(f => (m[f]?.GetValue<string>() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                                                 || (m["recipients"]?.AsArray() ?? new JsonArray()).Any(r => (r?["label"]?.GetValue<string>() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
                    return Results.Json(new { ok = true, items = liste });
                }
                case "detail":
                {
                    var id = p.Zahl("id") ?? 0;
                    var m = await DetailAsync(c, web, id);
                    if (m is null) return Fehler("not_found", 404);
                    if (m["box"]?.GetValue<string>() == "posteingang" && m["read"]?.GetValue<bool>() == false)
                        await web.PostAsync(c, $"api/app/mitteilungen/{id}/gelesen", new { });
                    return Results.Json(new { ok = true, item = m });
                }
                case "attachment":
                {
                    var (daten, name) = await web.DateiAsync(c, $"api/app/mitteilungen/anhang/{p.Zahl("id") ?? 0}");
                    var datei = string.IsNullOrWhiteSpace(name) ? "Anhang" : name;
                    return Results.File(daten, Dateityp(datei), datei);
                }
                case "people":
                {
                    var typ = p.Text("type", "kollegium") == "individuell" ? "individuell" : "kollegium";
                    JsonObject antwort;
                    try { antwort = await web.GetAsync(c, $"api/app/mitteilungen/personen?typ={typ}") as JsonObject ?? new JsonObject(); }
                    catch (UntPlanWebFehler ex) when (ex.Status is 404 or 405)
                    {
                        antwort = PersonenAusEmpfaengern((await web.GetAsync(c, "api/app/mitteilungen/empfaenger"))?.AsArray() ?? new JsonArray(), typ);
                    }
                    return Results.Json(Personen(antwort, typ));
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
                case "reply":
                {
                    // Vorschlag für die Antwort (Empfänger, „AW: …“, zitierter Text); verweigert, wenn der Absender keine Antwort erlaubt
                    var id = p.Zahl("id") ?? 0;
                    var v = await web.GetAsync(c, $"api/app/mitteilungen/{id}/antwort?alle={(Ja(p, "all") ? "true" : "false")}");
                    return Results.Json(new
                    {
                        ok = true,
                        recipients = (v?["empfaenger"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Select(e => new { token = S(e["id"]), label = S(e["name"]), kind = "teacher" }),
                        replyTo = Z(v?["antwortAuf"]) ?? id, subject = S(v?["betreff"]), body = S(v?["text"]),
                    });
                }
                case "options":
                {
                    var o = await web.GetAsync(c, "api/app/mitteilungen/optionen");
                    return Results.Json(new
                    {
                        ok = true, canSend = B(o?["darfSenden"]) ?? true, maxAttachmentMb = Z(o?["maxAnhangMb"]) ?? 7, maxAttachments = Z(o?["maxAnhaenge"]) ?? 5,
                        readReceipt = B(o?["lesebestaetigung"]) ?? true, noReply = B(o?["antwortSperre"]) ?? false, important = B(o?["wichtig"]) ?? false,
                    });
                }
                case "send":
                    return await SendenAsync(c, web, p, true);
                case "save_draft":
                    return await SendenAsync(c, web, p, false);
                case "delete":
                    await web.DeleteAsync(c, $"api/app/mitteilungen/{p.Zahl("id") ?? 0}");
                    return Results.Json(new { ok = true });
                case "unread":
                    return Results.Json(new { ok = true, count = (await ListeAsync(c, web, "posteingang")).Count(m => m["read"]?.GetValue<bool>() == false) });
                default:
                    return Fehler("unknown_action");
            }
        });
    }
}
