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
/// <item>Klassenbuch-Daten abholen (GET): Schüler-Fehlzeiten („Studenten*“) und Lehrstoff/Hausaufgaben je Unterricht („Kurse“) ab einem Datum.</item>
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

        // Rückmeldungen und noch nicht übertragene Buchungen (Buchungsmodul, Typ „raumbuchung“, Id „buchung-…“)
        // Prüfungen nach Unterricht | Einstellungen (Tage in die Zukunft, nur vertretungsrelevante); zurückgehaltene folgen später
        api.MapGet("/schulen/{schule}/rueckmeldungen", (string schule, HttpContext ctx, Einstellungen e, Rueckmeldungsdienst r, Buchungsdienst bu, Daten.Db db, Uhr uhr) =>
            Pruefen(schule, ctx, e) ?? Results.Json(r.Offene().Where(Klassenbucheinstellungen.Laden(db).Uebergabefilter(uhr.Heute)).Select(m => m.AlsVertrag()).Concat(bu.Rueckmeldungen()), UntPlanWebSchema.Json));

        api.MapPost("/schulen/{schule}/rueckmeldungen/quittieren", async (string schule, HttpContext ctx, Einstellungen e, Rueckmeldungsdienst r, Buchungsdienst bu, Protokoll prot) =>
        {
            if (Pruefen(schule, ctx, e) is { } fehler) return fehler;
            // Liste der Ids – als Array oder als Objekt { "ids": [...] }
            var knoten = await JsonNode.ParseAsync(ctx.Request.Body);
            var ids = (knoten as JsonArray ?? knoten?["ids"] as JsonArray)?.Select(x => x?.GetValue<string>()).OfType<string>().ToList() ?? new();
            var n = r.Quittieren(ids) + bu.Quittieren(ids);
            if (n > 0) prot.Schreiben("UntPlan", "Rückmeldungen quittiert", n.ToString());
            return Results.Json(new { ok = true, quittiert = n });
        });

        // Fehlzeiten aus dem Klassenbuch (UntPlan: Import „Studenten*“)
        api.MapGet("/schulen/{schule}/fehlzeiten", (string schule, string? ab, string? bis, HttpContext ctx, Einstellungen e, Klassenbuchdienst kb, Paketdienst p) =>
        {
            if (Pruefen(schule, ctx, e) is { } fehler) return fehler;
            if (Zeitraum(ab, bis) is not { } z) return Fehler(400, "Parameter ab (und bis) im Format yyyy-MM-dd erwartet, höchstens 400 Tage.");
            var (von, ende) = z;
            var namen = (p.Aktuell?.Schueler ?? new()).ToDictionary(x => x.Id, x => $"{x.Name}, {x.Vorname}");
            return Results.Json(kb.FehlzeitenZeitraum(von, ende).Select(f => new WebFehlzeit
            {
                Id = f.Id, Datum = f.Datum, Stunde = f.Stunde, Unterricht = f.Unterricht, SchuelerId = f.SchuelerId, Schueler = namen.GetValueOrDefault(f.SchuelerId, ""),
                Klasse = f.Klasse, Fach = f.Fach, Art = f.Art, Minuten = f.Minuten, Status = f.Status, Bemerkung = f.Bemerkung, Lehrer = f.Lehrer,
            }), UntPlanWebSchema.Json);
        });

        // Klassenbuch je Unterricht: Lehrstoff und Hausaufgaben (UntPlan: Import „Kurse“); interne Notizen bleiben auf dem Server
        api.MapGet("/schulen/{schule}/klassenbuch", (string schule, string? ab, string? bis, HttpContext ctx, Einstellungen e, Klassenbuchdienst kb) =>
        {
            if (Pruefen(schule, ctx, e) is { } fehler) return fehler;
            if (Zeitraum(ab, bis) is not { } z) return Fehler(400, "Parameter ab (und bis) im Format yyyy-MM-dd erwartet, höchstens 400 Tage.");
            var (von, ende) = z;
            return Results.Json(kb.Zeitraum(von, ende).Select(k => new WebKlassenbucheintrag
            {
                Id = k.Id, Datum = k.Datum, Stunde = k.Stunde, Unterricht = k.Unterricht, Klassen = k.Klassen, Fach = k.Fach, Lehrer = k.Lehrer,
                Lehrstoff = k.Thema, Hausaufgabe = k.Hausaufgabe, Faellig = k.Faellig, Anwesenheit = k.Anwesenheit, Geaendert = k.Geaendert,
            }), UntPlanWebSchema.Json);
        });
    }

    /// <summary>Zeitraum ab–bis aus der Abfrage (bis ohne Angabe: ein Jahr nach ab, höchstens 400 Tage).</summary>
    private static (DateOnly Von, DateOnly Bis)? Zeitraum(string? ab, string? bis)
    {
        if (!DateOnly.TryParseExact(ab ?? "", "yyyy-MM-dd", out var von)) return null;
        var ende = von.AddDays(370);
        if (bis != null && !DateOnly.TryParseExact(bis, "yyyy-MM-dd", out ende)) return null;
        if (ende < von || ende.DayNumber - von.DayNumber > 400) return null;
        return (von, ende);
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
