using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Buchungsmodul (Menü „Buchen“) – alle Endpunkte unter <c>/api/app/buchen</c>, angemeldet als Lehrkraft, Schulleitung oder Administration.
/// <list type="bullet">
/// <item><c>GET  /optionen</c>: Räume mit Stammdaten (Raumart, Kapazität, Gebäude, Abteilung, Raumgruppe, feste Ressourcen), Ressourcenarten,
/// Ressourcen, Auswahllisten, Zeitraster.</item>
/// <item><c>POST /suche/raeume</c> { zeit, kriterien } → { termine: [{ termin, freieRaeume }], raeume: [{ kurz, …, belegt: [Terminindex], prozent }], uebersprungen }</item>
/// <item><c>POST /suche/ressourcen</c> { zeit, kriterien } → { termine, ressourcen: [{ id, name, art, anzahl, verfuegbar: [je Termin], frei, belegt, prozent }] }</item>
/// <item><c>POST /hinweise</c> { termine, lehrer, klassen } → [Text] (Lehrkraft bzw. Klasse hat zur gleichen Zeit schon Unterricht/Aktivität)</item>
/// <item><c>POST /</c> BuchungDaten → { serie, buchungen, hinweise }; belegter Raum bzw. Ressource → 400 mit allen Konflikten.</item>
/// <item><c>GET  /liste?von&amp;bis&amp;alle</c> (Meine Buchungen; alle nur Administration), <c>DELETE /{id}?serie=true</c> (Storno).</item>
/// <item><c>GET  /raumuebersicht?datum&amp;tage&amp;…Kriterien</c>, <c>GET /ressourcenuebersicht?datum&amp;arten=1,2</c>.</item>
/// <item><c>GET/POST/DELETE /unterricht</c>: in UntPlanWeb angelegter Unterricht (Unterricht | Neuer Unterricht).</item>
/// <item>Administration: <c>/ressourcenarten</c>, <c>/ressourcen</c> (GET, POST, PUT /{id}, DELETE /{id}), <c>PUT /raumdaten/{raum}</c>.</item>
/// </list>
/// </summary>
public static class BuchungApi
{
    public sealed record SuchAnfrage(Zeitangabe Zeit, Kriterien? Kriterien);
    public sealed record HinweisAnfrage(List<TerminWahl> Termine, string? Lehrer, List<string>? Klassen);
    public sealed record ArtDaten(string Name, int? Reihenfolge);
    public sealed record RessourceDaten(string Name, int? ArtId, string? Raum, int? Anzahl, string? Bemerkung, bool? Aktiv);
    public sealed record RaumDaten(int? Kapazitaet, string? Gebaeude, string? Abteilung, string? Raumgruppe);
    public sealed record UnterrichtDaten(string? Lehrer, string Fach, List<string>? Klassen, string? Gruppe, string? Text, int? Wochenstunden);

    private static Benutzer Ich(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!b.Kollegium) throw new UnauthorizedAccessException("Das Buchungsmodul steht Lehrkräften, Schulleitung und Administration zur Verfügung.");
        return b;
    }

    private static Benutzer Admin(HttpContext c)
    {
        var b = Ich(c);
        if (!b.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        return b;
    }

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static object Json(Buchung b) => new
    {
        b.Id, b.Serie, b.Art, artName = b.ArtName, b.Datum, b.StundeVon, b.StundeBis, b.Beginn, b.Ende, b.Raum, b.Klassen, b.Lehrer, b.Fach, b.Titel, b.Text,
        b.UnterrichtId, b.Benachrichtigen, b.Status, b.Uebertragen, b.BenutzerId, b.Absender, b.Erstellt, b.Storniert, b.Ressourcen,
        wochentag = DateOnly.TryParse(b.Datum, out var d) ? Plandienst.Wochentage[Plandienst.TagIndex(d)] : "",
    };

    private static List<int>? Zahlen(string? s) => string.IsNullOrWhiteSpace(s) ? null
        : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var i) ? i : -1).Where(i => i >= 0).ToList();

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/buchen");

        api.MapGet("/optionen", (HttpContext c, Paketdienst p, Buchungsdienst bu, Plandienst plan) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var raeume = bu.Raeume(s);
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            List<string> Werte(Func<Raumdaten, string> f) => raeume.Select(f).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, de).ToList();
            return Results.Json(new
            {
                admin = b.IstAdmin, ich = b.LehrerKuerzel ?? "",
                raeume, ressourcenarten = bu.Arten(), ressourcen = bu.Ressourcen(!b.IstAdmin),
                raumarten = Werte(r => r.Raumart), gebaeude = Werte(r => r.Gebaeude), abteilungen = Werte(r => r.Abteilung), raumgruppen = Werte(r => r.Raumgruppe),
                zeitraster = plan.Zeitraster(s), tageProWoche = Math.Max(5, s.Paket.Zeitraster.TageProWoche),
                maxTermine = Buchungsdienst.MaxTermine,
            });
        });

        api.MapPost("/suche/raeume", (SuchAnfrage d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
            Results.Json(bu.RaumSuche(Paket(p), Ich(c), d.Zeit ?? throw new ArgumentException("Bitte einen Termin angeben."), d.Kriterien)));

        api.MapPost("/suche/ressourcen", (SuchAnfrage d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
            Results.Json(bu.RessourcenSuche(Paket(p), Ich(c), d.Zeit ?? throw new ArgumentException("Bitte einen Termin angeben."), d.Kriterien)));

        api.MapPost("/termine", (Zeitangabe d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Ich(c);
            var s = Paket(p);
            var (termine, weg) = bu.Termine(s, d);
            var (_, _, beginn, ende) = Buchungsdienst.Stunden(s, d.StundeVon, d.StundeBis, d.Beginn, d.Ende);
            return Results.Json(new
            {
                termine = termine.Select(t => new { datum = Plandienst.Iso(t.Datum), stundeVon = t.StundeVon, stundeBis = t.StundeBis, wochentag = Plandienst.Wochentage[Plandienst.TagIndex(t.Datum)] }),
                uebersprungen = weg.Select(Plandienst.Iso), beginn, ende,
            });
        });

        api.MapPost("/hinweise", (HinweisAnfrage d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Ich(c);
            return Results.Json(bu.Hinweise(Paket(p), d.Termine ?? new(), d.Lehrer, d.Klassen));
        });

        api.MapPost("/", (BuchungDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu, Protokoll prot) =>
        {
            var b = Ich(c);
            var (liste, hinweise) = bu.Speichern(Paket(p), b, d);
            var erste = liste[0];
            prot.Schreiben(b.Login, "Gebucht", $"{erste.ArtName} {erste.Raum} {erste.Datum} {erste.StundeVon}.–{erste.StundeBis}." + (liste.Count > 1 ? $" (Serie, {liste.Count} Termine)" : ""));
            return Results.Json(new { serie = erste.Serie, buchungen = liste.Select(Json), hinweise });
        });

        api.MapGet("/liste", (string? von, string? bis, bool? alle, HttpContext c, Buchungsdienst bu, Uhr uhr) =>
        {
            var b = Ich(c);
            var v = DateOnly.TryParse(von, out var x) ? x : uhr.Heute.AddDays(-30);
            var e = DateOnly.TryParse(bis, out var y) ? y : v.AddDays(400);
            if (e < v) (v, e) = (e, v);
            return Results.Json(bu.Liste(b, alle == true, v, e).Select(Json));
        });

        api.MapGet("/{id:int}", (int id, HttpContext c, Buchungsdienst bu) =>
        {
            var b = Ich(c);
            var x = bu.Holen(id) ?? throw new ArgumentException("Unbekannte Buchung.");
            return Results.Json(Json(x));
        });

        api.MapDelete("/{id:int}", (int id, bool? serie, HttpContext c, Buchungsdienst bu, Protokoll prot) =>
        {
            var b = Ich(c);
            var n = bu.Stornieren(b, id, serie == true);
            prot.Schreiben(b.Login, "Buchung storniert", $"Nr. {id}{(serie == true ? $" (Serie, {n} Termine)" : "")}");
            return Results.Json(new { ok = true, storniert = n });
        });

        api.MapGet("/raumuebersicht", (string? datum, int? tage, string? ressourcenarten, string? raumart, int? kapazitaet, string? abteilung, string? gebaeude, string? raumgruppe,
            HttpContext c, Paketdienst p, Buchungsdienst bu, Uhr uhr) =>
        {
            Ich(c);
            var d = DateOnly.TryParse(datum, out var x) ? x : uhr.Heute;
            return Results.Json(bu.Raumuebersicht(Paket(p), d, tage ?? 1, new Kriterien(Zahlen(ressourcenarten), raumart, kapazitaet, null, abteilung, gebaeude, raumgruppe)));
        });

        api.MapGet("/ressourcenuebersicht", (string? datum, string? arten, HttpContext c, Paketdienst p, Buchungsdienst bu, Uhr uhr) =>
        {
            Ich(c);
            var d = DateOnly.TryParse(datum, out var x) ? x : uhr.Heute;
            return Results.Json(bu.Ressourcenuebersicht(Paket(p), d, Zahlen(arten)));
        });

        // ---------------------------------------------------------------- Neuer Unterricht (zusätzliche Stunden = Buchungen der Art „unterricht“)

        api.MapGet("/unterricht", (HttpContext c, Buchungsdienst bu) => Results.Json(bu.EigeneUnterrichte(Ich(c))));
        api.MapPost("/unterricht", (UnterrichtDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu, Protokoll prot) =>
        {
            var b = Ich(c);
            var u = bu.UnterrichtAnlegen(Paket(p), b, d.Lehrer, d.Fach, d.Klassen, d.Gruppe, d.Text, d.Wochenstunden);
            prot.Schreiben(b.Login, "Unterricht angelegt", $"{u.Fach} {string.Join(",", u.Klassen)} {u.Lehrer}");
            return Results.Json(u);
        });
        api.MapDelete("/unterricht/{id:int}", (int id, HttpContext c, Buchungsdienst bu) => { bu.UnterrichtLoeschen(Ich(c), id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Administration: Ressourcenarten, Ressourcen, Raum-Stammdaten

        api.MapGet("/ressourcenarten", (HttpContext c, Buchungsdienst bu) => { Ich(c); return Results.Json(bu.Arten()); });
        api.MapPost("/ressourcenarten", (ArtDaten d, HttpContext c, Buchungsdienst bu) => { Admin(c); return Results.Json(bu.ArtSpeichern(null, d.Name, d.Reihenfolge)); });
        api.MapPut("/ressourcenarten/{id:int}", (int id, ArtDaten d, HttpContext c, Buchungsdienst bu) => { Admin(c); return Results.Json(bu.ArtSpeichern(id, d.Name, d.Reihenfolge)); });
        api.MapDelete("/ressourcenarten/{id:int}", (int id, HttpContext c, Buchungsdienst bu) => { Admin(c); bu.ArtLoeschen(id); return Results.Json(new { ok = true }); });

        api.MapGet("/ressourcen", (HttpContext c, Buchungsdienst bu) => { var b = Ich(c); return Results.Json(bu.Ressourcen(!b.IstAdmin)); });
        api.MapPost("/ressourcen", (RessourceDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Admin(c);
            return Results.Json(bu.RessourceSpeichern(p.Aktuell, null, d.Name, d.ArtId, d.Raum, d.Anzahl, d.Bemerkung, d.Aktiv ?? true));
        });
        api.MapPut("/ressourcen/{id:int}", (int id, RessourceDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Admin(c);
            return Results.Json(bu.RessourceSpeichern(p.Aktuell, id, d.Name, d.ArtId, d.Raum, d.Anzahl, d.Bemerkung, d.Aktiv ?? true));
        });
        api.MapDelete("/ressourcen/{id:int}", (int id, HttpContext c, Buchungsdienst bu) => { Admin(c); bu.RessourceLoeschen(id); return Results.Json(new { ok = true }); });

        api.MapPut("/raumdaten/{raum}", (string raum, RaumDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu, Protokoll prot) =>
        {
            var b = Admin(c);
            bu.RaumdatenSpeichern(Paket(p), raum, d.Kapazitaet, d.Gebaeude, d.Abteilung, d.Raumgruppe);
            prot.Schreiben(b.Login, "Raumdaten geändert", raum);
            return Results.Json(new { ok = true });
        });
    }
}
