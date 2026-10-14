using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UntPlan.Portal;

public sealed class PortalOptionen
{
    public string Name { get; set; } = "UntPlan WebPortal";
    public string Datenordner { get; set; } = "daten";
    public List<SchulKonfiguration> Schulen { get; set; } = new();
}

public sealed class SchulKonfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Schlüssel der Desktop-Anwendung (Hochladen, Meldungen abholen).</summary>
    public string ApiSchluessel { get; set; } = "";
    /// <summary>PIN für Lehrkräfte (Krankmeldung über das Portal).</summary>
    public string MeldeSchluessel { get; set; } = "";
    /// <summary>Vertretungsplan ohne Anmeldung sichtbar (Schülerversion).</summary>
    public bool Oeffentlich { get; set; } = true;
}

public sealed record Meldung(string Id, string Typ, DateTime Erstellt, string Absender, string? LehrerKuerzel, string? Von, string? Bis,
    int? StundeVon, int? StundeBis, string? Grund, string? Text, bool Quittiert = false,
    string? Titel = null, List<string>? Klassen = null, List<string>? Raeume = null, List<string>? WeitereLehrer = null);

/// <summary>
/// Referenzimplementierung des WebPortals: speichert den von UntPlan hochgeladenen Stand (Snapshot) je Schule,
/// zeigt Stunden- und Vertretungsplan als Webseite und nimmt Krankmeldungen von Lehrkräften entgegen.
/// Datenablage als JSON-Dateien im Datenordner.
/// </summary>
public static class PortalServer
{
    public const string Schema = "untplan-sync/1";
    public const string Version = "1.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static WebApplication Erstellen(string[] args, Action<PortalOptionen>? konfigurieren = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var opt = builder.Configuration.GetSection("Portal").Get<PortalOptionen>() ?? new PortalOptionen();
        konfigurieren?.Invoke(opt);
        var speicher = new Speicher(Path.GetFullPath(opt.Datenordner));
        var app = builder.Build();

        SchulKonfiguration? Schule(string id) => opt.Schulen.FirstOrDefault(s => s.Id == id);

        bool Berechtigt(HttpContext ctx, SchulKonfiguration s)
        {
            var h = ctx.Request.Headers.Authorization.ToString();
            return h.StartsWith("Bearer ", StringComparison.Ordinal) && Gleich(h[7..].Trim(), s.ApiSchluessel);
        }

        IResult Fehler(int status, string meldung) => Results.Json(new { ok = false, meldung }, Json, statusCode: status);

        var api = app.MapGroup("/api/v1");
        api.MapGet("/status", () => Results.Json(new { name = opt.Name, version = Version, schema = Schema }, Json));

        api.MapPut("/schulen/{id}/snapshot", async (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            JsonObject? paket;
            try { paket = await JsonNode.ParseAsync(ctx.Request.Body) as JsonObject; }
            catch (JsonException) { return Fehler(400, "Ungültiges JSON"); }
            if (paket?["schema"]?.GetValue<string>() != Schema) return Fehler(400, $"Schema {Schema} erwartet");
            var revision = speicher.SnapshotSpeichern(id, paket);
            return Results.Json(new { ok = true, revision, meldung = (string?)null }, Json);
        });

        api.MapGet("/schulen/{id}/snapshot", (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            return speicher.Snapshot(id) is { } snap ? Results.Text(snap.ToJsonString(Json), "application/json") : Fehler(404, "Noch keine Daten");
        });

        api.MapGet("/schulen/{id}/meldungen", (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            return Results.Json(new { meldungen = speicher.Meldungen(id).Where(m => !m.Quittiert) }, Json);
        });

        api.MapPost("/schulen/{id}/meldungen/quittieren", async (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            var body = await JsonNode.ParseAsync(ctx.Request.Body);
            var ids = body?["ids"]?.AsArray().Select(x => x!.GetValue<string>()).ToHashSet() ?? new HashSet<string>();
            return Results.Json(new { ok = true, quittiert = speicher.Quittieren(id, ids) }, Json);
        });

        // --- Schnittstelle untplanweb/1 (Verträge: UntPlan.Web.Contracts; UntPlanWeb nutzt dieselben Endpunkte)
        api.MapPut("/schulen/{id}/untplanweb", async (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            UntPlan.Web.Contracts.WebPaket? paket;
            try { paket = await JsonSerializer.DeserializeAsync<UntPlan.Web.Contracts.WebPaket>(ctx.Request.Body, UntPlan.Web.Contracts.UntPlanWebSchema.Json); }
            catch (JsonException) { return Fehler(400, "Ungültiges JSON"); }
            if (paket?.Schema != UntPlan.Web.Contracts.UntPlanWebSchema.Schema) return Fehler(400, $"Schema {UntPlan.Web.Contracts.UntPlanWebSchema.Schema} erwartet");
            var bisher = speicher.WebRevision(id);
            if (paket.Revision <= bisher) return Fehler(409, $"Veraltete Revision {paket.Revision} (aktuell {bisher})");
            var json = JsonSerializer.Serialize(paket, UntPlan.Web.Contracts.UntPlanWebSchema.Json);
            // Teilpaket: nur die übertragenen Teile ersetzen, die übrigen vom bisherigen Stand übernehmen
            if (paket.Teile.Count > 0 && speicher.WebPaket(id) is { } alt && JsonNode.Parse(json) is JsonObject neu)
            {
                foreach (var (teil, felder) in UntPlan.Web.Contracts.WebTeile.Felder)
                {
                    if (paket.Teile.Contains(teil)) continue;
                    foreach (var f in felder) neu[f] = alt[f]?.DeepClone();
                }
                json = neu.ToJsonString();
            }
            speicher.WebPaketSpeichern(id, json, paket.Revision);
            return Results.Json(new UntPlan.Web.Contracts.WebAntwort(true, paket.Revision, null), UntPlan.Web.Contracts.UntPlanWebSchema.Json);
        });

        api.MapGet("/schulen/{id}/rueckmeldungen", (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            var liste = speicher.Meldungen(id).Where(m => !m.Quittiert).Select(m => new UntPlan.Web.Contracts.WebRueckmeldung
            {
                Id = m.Id, Typ = m.Typ == "abwesenheit" ? "absenz" : m.Typ, Erstellt = m.Erstellt, Absender = m.Absender, Lehrer = m.LehrerKuerzel,
                Von = m.Von, Bis = m.Bis, StundeVon = m.StundeVon, StundeBis = m.StundeBis, Grund = m.Grund, Text = m.Text,
                Titel = m.Titel, Klassen = m.Klassen ?? new(), Raeume = m.Raeume ?? new(), WeitereLehrer = m.WeitereLehrer ?? new(),
            });
            return Results.Json(liste, UntPlan.Web.Contracts.UntPlanWebSchema.Json);
        });

        api.MapPost("/schulen/{id}/rueckmeldungen/quittieren", async (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            if (!Berechtigt(ctx, s)) return Fehler(401, "API-Schlüssel ungültig");
            var ids = await JsonSerializer.DeserializeAsync<string[]>(ctx.Request.Body, UntPlan.Web.Contracts.UntPlanWebSchema.Json) ?? Array.Empty<string>();
            return Results.Json(new { ok = true, quittiert = speicher.Quittieren(id, ids.ToHashSet()) }, Json);
        });

        // Meldung einer Lehrkraft (Formular oder App)
        api.MapPost("/schulen/{id}/meldungen", async (string id, HttpContext ctx) =>
        {
            if (Schule(id) is not { } s) return Fehler(404, "Unbekannte Schule");
            var form = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
            JsonNode? json = form is null ? await JsonNode.ParseAsync(ctx.Request.Body) : null;
            string? Wert(string k) => form != null ? form[k].ToString() : json?[k]?.ToString();
            if (!Gleich(Wert("pin") ?? "", s.MeldeSchluessel)) return Fehler(401, "PIN ungültig");
            var kuerzel = (Wert("kuerzel") ?? "").Trim();
            if (kuerzel.Length == 0 || !DateOnly.TryParse(Wert("von"), System.Globalization.CultureInfo.InvariantCulture, out var von)) return Fehler(400, "Kürzel und Datum erforderlich");
            var bis = DateOnly.TryParse(Wert("bis"), System.Globalization.CultureInfo.InvariantCulture, out var b) && b >= von ? b : von;
            var typ = Wert("typ") switch { "nachricht" => "nachricht", "raumbuchung" => "raumbuchung", "klausur" => "klausur", _ => "abwesenheit" };
            List<string> Liste(string k) => (Wert(k) ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var m = new Meldung(Guid.NewGuid().ToString("N"), typ, DateTime.Now, kuerzel, kuerzel, von.ToString("yyyy-MM-dd"), bis.ToString("yyyy-MM-dd"),
                int.TryParse(Wert("stundeVon"), out var sv) ? sv : null, int.TryParse(Wert("stundeBis"), out var sb) ? sb : null,
                typ == "abwesenheit" ? Wert("grund") ?? "Krankheit" : Wert("grund"), Wert("text"),
                Titel: Wert("titel"), Klassen: Liste("klassen"), Raeume: Liste("raeume").Concat(Liste("raum")).Distinct().ToList(), WeitereLehrer: Liste("aufsichten"));
            speicher.MeldungHinzufuegen(id, m);
            return form != null ? Results.Redirect($"/{id}/meldung?ok=1") : Results.Json(new { ok = true, id = m.Id }, Json);
        });

        // Webseiten
        app.MapGet("/", () => Results.Content(Seite(opt.Name,
            "<h1>" + H(opt.Name) + "</h1><ul>" + string.Concat(opt.Schulen.Select(s => $"<li><a href=\"/{H(s.Id)}/\">{H(s.Name)}</a></li>")) + "</ul>"), "text/html; charset=utf-8"));
        app.MapGet("/{id}/", (string id, string? klasse) =>
        {
            if (Schule(id) is not { } s || !s.Oeffentlich) return Results.NotFound();
            return Results.Content(Seite(s.Name, PlanHtml(s, speicher.Snapshot(id), klasse)), "text/html; charset=utf-8");
        });
        app.MapGet("/{id}/meldung", (string id, int? ok) =>
        {
            if (Schule(id) is not { } s) return Results.NotFound();
            var html = $"<h1>Abwesenheit melden – {H(s.Name)}</h1>" + (ok == 1 ? "<p class=\"ok\">Ihre Meldung wurde übermittelt.</p>" : "") +
                       $"<form method=\"post\" action=\"/api/v1/schulen/{H(id)}/meldungen\">" +
                       "<label>Kürzel <input name=\"kuerzel\" required maxlength=\"20\"></label>" +
                       "<label>PIN <input name=\"pin\" type=\"password\" required></label>" +
                       $"<label>von <input name=\"von\" type=\"date\" required value=\"{DateTime.Today:yyyy-MM-dd}\"></label>" +
                       "<label>bis <input name=\"bis\" type=\"date\"></label>" +
                       "<label>Grund <select name=\"grund\"><option>Krankheit</option><option>Fortbildung</option><option>Dienstlich</option><option>Sonstiges</option></select></label>" +
                       "<label>Bemerkung <input name=\"text\" maxlength=\"200\"></label><button>Melden</button></form>";
            return Results.Content(Seite("Abwesenheit melden", html), "text/html; charset=utf-8");
        });
        return app;
    }

    /// <summary>Vergleich in konstanter Zeit (API-Schlüssel, PIN).</summary>
    private static bool Gleich(string a, string b) =>
        b.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Seite(string titel, string body) =>
        "<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        $"<title>{H(titel)}</title><style>body{{font-family:Segoe UI,Arial,sans-serif;margin:16px;color:#222}}h1{{color:#2b579a;font-size:22px}}" +
        "table{border-collapse:collapse;margin:8px 0 18px;width:100%;max-width:1000px}th{background:#2b579a;color:#fff;text-align:left}th,td{border:1px solid #c8d1e0;padding:4px 8px}" +
        "tr:nth-child(even) td{background:#f3f6fb}.e{color:#c00}label{display:block;margin:8px 0}input,select{margin-left:6px}.ok{color:#080}nav a{margin-right:8px}</style></head>" +
        $"<body>{body}</body></html>";

    private static string PlanHtml(SchulKonfiguration s, JsonObject? snap, string? klasse)
    {
        if (snap is null) return $"<h1>{H(s.Name)}</h1><p>Es wurden noch keine Daten veröffentlicht.</p>";
        var sb = new StringBuilder($"<h1>{H(s.Name)}</h1><p>Stand: {H(snap["erzeugt"]?.ToString())} · Revision {H(snap["revision"]?.ToString())}</p>");
        var nachrichten = snap["nachrichten"]?.AsArray().Where(n => n?["intern"]?.GetValue<bool>() != true).ToList() ?? new();
        foreach (var n in nachrichten) sb.Append($"<p><b>{H(n?["datum"]?.ToString())}:</b> {H(n?["text"]?.ToString())}</p>");
        var vertretungen = snap["vertretungen"]?.AsArray().OfType<JsonNode>().ToList() ?? new();
        foreach (var tag in vertretungen.GroupBy(v => v["datum"]?.ToString()).OrderBy(g => g.Key))
        {
            sb.Append($"<h2>Vertretungen {H(tag.Key)}</h2><table><tr><th>Klasse(n)</th><th>Std.</th><th>Vertreter</th><th>Fach</th><th>Raum</th><th>Art</th><th>Text</th></tr>");
            foreach (var v in tag.OrderBy(v => string.Join(",", v["klassen"]!.AsArray())).ThenBy(v => v["stunde"]!.GetValue<int>()))
            {
                var art = v["art"]?.ToString();
                sb.Append($"<tr><td>{H(string.Join(", ", v["klassen"]!.AsArray()))}</td><td>{v["stunde"]}</td><td>{H(v["vertreter"]?.ToString())}</td><td>{H(v["fach"]?.ToString())}</td>" +
                          $"<td>{H(v["raum"]?.ToString())}</td><td class=\"{(art is "Entfall" or "Freisetzung" ? "e" : "")}\">{H(art)}</td><td>{H(v["text"]?.ToString())}</td></tr>");
            }
            sb.Append("</table>");
        }
        var klassen = snap["klassen"]?.AsArray().Select(k => k?["name"]?.ToString() ?? "").Where(k => k.Length > 0).ToList() ?? new();
        sb.Append("<h2>Stundenpläne</h2><nav>" + string.Concat(klassen.Select(k => $"<a href=\"?klasse={Uri.EscapeDataString(k)}\">{H(k)}</a>")) + "</nav>");
        if (klasse != null && klassen.Contains(klasse))
        {
            var tage = snap["zeitraster"]?["tage"]?.GetValue<int>() ?? 5;
            var stunden = snap["zeitraster"]?["stunden"]?.AsArray().Count ?? 8;
            var plan = snap["stundenplan"]?.AsArray().OfType<JsonNode>().Where(p => p["klassen"]!.AsArray().Any(k => k?.ToString() == klasse)).ToList() ?? new();
            string[] namen = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag" };
            sb.Append($"<table><tr><th>{H(klasse)}</th>" + string.Concat(Enumerable.Range(0, tage).Select(t => $"<th>{namen[Math.Min(t, 5)]}</th>")) + "</tr>");
            for (var st = 1; st <= stunden; st++)
            {
                sb.Append($"<tr><td>{st}.</td>");
                for (var t = 0; t < tage; t++)
                    sb.Append("<td>" + string.Join("<br>", plan.Where(p => p["tag"]!.GetValue<int>() == t && p["stunde"]!.GetValue<int>() == st)
                        .Select(p => H($"{p["fach"]} {string.Join(",", p["lehrer"]!.AsArray())} {p["raum"]} {p["woche"]}".Trim()))) + "</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }
        return sb.ToString();
    }

    /// <summary>Dateibasierte Ablage: daten/{schule}/snapshot.json und meldungen.json.</summary>
    private sealed class Speicher
    {
        private readonly string _ordner;
        private readonly ConcurrentDictionary<string, object> _sperren = new();

        public Speicher(string ordner)
        {
            _ordner = ordner;
            Directory.CreateDirectory(ordner);
        }

        private string Pfad(string schule, string datei)
        {
            var dir = Path.Combine(_ordner, string.Concat(schule.Where(char.IsLetterOrDigit)));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, datei);
        }

        private object Sperre(string schule) => _sperren.GetOrAdd(schule, _ => new object());

        public int SnapshotSpeichern(string schule, JsonObject paket)
        {
            lock (Sperre(schule))
            {
                var revision = (Snapshot(schule)?["revision"]?.GetValue<int>() ?? 0) + 1;
                paket["revision"] = revision;
                paket["empfangen"] = DateTime.Now;
                Schreiben(Pfad(schule, "snapshot.json"), paket.ToJsonString(Json));
                return revision;
            }
        }

        public int WebRevision(string schule)
        {
            var p = Pfad(schule, "untplanweb.revision");
            return File.Exists(p) && int.TryParse(File.ReadAllText(p), out var r) ? r : 0;
        }

        public JsonObject? WebPaket(string schule)
        {
            var p = Pfad(schule, "untplanweb.json");
            return File.Exists(p) ? JsonNode.Parse(File.ReadAllText(p)) as JsonObject : null;
        }

        public void WebPaketSpeichern(string schule, string json, int revision)
        {
            lock (Sperre(schule))
            {
                Schreiben(Pfad(schule, "untplanweb.json"), json);
                Schreiben(Pfad(schule, "untplanweb.revision"), revision.ToString());
            }
        }

        public JsonObject? Snapshot(string schule)
        {
            var p = Pfad(schule, "snapshot.json");
            return File.Exists(p) ? JsonNode.Parse(File.ReadAllText(p)) as JsonObject : null;
        }

        public List<Meldung> Meldungen(string schule)
        {
            var p = Pfad(schule, "meldungen.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<List<Meldung>>(File.ReadAllText(p), Json) ?? new() : new();
        }

        public void MeldungHinzufuegen(string schule, Meldung m)
        {
            lock (Sperre(schule))
            {
                var l = Meldungen(schule);
                l.Add(m);
                Schreiben(Pfad(schule, "meldungen.json"), JsonSerializer.Serialize(l, Json));
            }
        }

        public int Quittieren(string schule, HashSet<string> ids)
        {
            lock (Sperre(schule))
            {
                var l = Meldungen(schule);
                var n = 0;
                for (var i = 0; i < l.Count; i++)
                    if (ids.Contains(l[i].Id) && !l[i].Quittiert) { l[i] = l[i] with { Quittiert = true }; n++; }
                // Quittierte Meldungen nach 30 Tagen entfernen
                l.RemoveAll(m => m.Quittiert && m.Erstellt < DateTime.Now.AddDays(-30));
                Schreiben(Pfad(schule, "meldungen.json"), JsonSerializer.Serialize(l, Json));
                return n;
            }
        }

        private static void Schreiben(string pfad, string inhalt)
        {
            var tmp = pfad + ".tmp";
            File.WriteAllText(tmp, inhalt);
            File.Move(tmp, pfad, true);
        }
    }
}
