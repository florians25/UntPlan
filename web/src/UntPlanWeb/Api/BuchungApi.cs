using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Buchungsmodul (Menü „Buchen“) – alle Endpunkte unter <c>/api/app/buchen</c>, angemeldet als Lehrkraft, Schulleitung oder Administration.
/// <list type="bullet">
/// <item><c>GET  /optionen</c>: Räume mit Stammdaten (Raumart, Kapazität, Gebäude, Abteilung, Raumgruppe, feste Ressourcen), Ressourcenarten,
/// Ressourcen, Auswahllisten, Zeitraster.</item>
/// <item><c>POST /suche/raeume</c> { zeit, kriterien } → { termine: [{ termin, freieRaeume }], raeume: [{ kurz, …, belegt: [Terminindex], prozent }], uebersprungen }</item>
/// <item><c>POST /suche/ressourcen</c> { zeit, kriterien } → { termine, ressourcen: [{ id, name, art, anzahl, verfuegbar: [je Termin], frei, belegt, prozent }] }</item>
/// <item><c>GET  /raumstatus?datum&amp;von&amp;bis&amp;unterricht</c> → [{ kurz, name, raumart, kapazitaet, gebaeude, abteilung, buchbar, eigen, frei, belegt: [{ stunde, art, text }] }]
/// (Raumwahl beim Eintragen einer Prüfung; eigene Stunde des Unterrichts zählt nicht).</item>
/// <item><c>POST /hinweise</c> { termine, lehrer, klassen } → [Text] (Lehrkraft bzw. Klasse hat zur gleichen Zeit schon Unterricht/Aktivität)</item>
/// <item><c>POST /</c> BuchungDaten → { serie, buchungen, hinweise }; belegter Raum bzw. Ressource → 400 mit allen Konflikten.</item>
/// <item><c>GET  /liste?von&amp;bis&amp;alle</c> (Meine Buchungen; alle nur Administration), <c>DELETE /{id}?serie=true</c> (Storno).</item>
/// <item><c>GET  /raumuebersicht?datum&amp;tage&amp;…Kriterien</c>, <c>GET /ressourcenuebersicht?datum&amp;arten=1,2&amp;tage=1</c> (Woche bzw. ein Tag, mit gebuchter Menge je Stunde).</item>
/// <item><c>GET/POST/DELETE /unterricht</c>: in UntPlanWeb angelegter Unterricht (Unterricht | Neuer Unterricht).</item>
/// <item>Administration: <c>/ressourcenarten</c> (mit Symbol, erlaubte Symbole: <c>GET /symbole</c>), <c>/ressourcen</c> (GET, POST, PUT /{id}, DELETE /{id};
/// mit Standort), <c>POST /ressourcen/beispiele</c> (Beispiel-Ressourcen, solange keine angelegt sind), <c>PUT /raumdaten/{raum}</c>.</item>
/// </list>
/// </summary>
public static class BuchungApi
{
    public sealed record SuchAnfrage(Zeitangabe Zeit, Kriterien? Kriterien);
    public sealed record HinweisAnfrage(List<TerminWahl> Termine, string? Lehrer, List<string>? Klassen);
    public sealed record ArtDaten(string Name, int? Reihenfolge, string? Symbol = null);
    public sealed record RessourceDaten(string Name, int? ArtId, string? Raum, int? Anzahl, string? Bemerkung, bool? Aktiv, string? Standort = null);
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
        if (!Rechtedienst.Darf(b, Recht.BuchungVerwalten)) throw new UnauthorizedAccessException("Nur für die Administration.");
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

        api.MapGet("/optionen", (HttpContext c, Paketdienst p, Buchungsdienst bu, Plandienst plan, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var raeume = bu.Raeume(s);
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            List<string> Werte(Func<Raumdaten, string> f) => raeume.Select(f).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, de).ToList();
            return Results.Json(new
            {
                admin = Rechtedienst.Darf(b, Recht.BuchungVerwalten), raumBuchen = Rechtedienst.Darf(b, Recht.BuchungRaum),
                ressourcenBuchen = Rechtedienst.Darf(b, Recht.BuchungRessourcen), andere = Rechtedienst.Darf(b, Recht.BuchungAndere), ich = b.LehrerKuerzel ?? "",
                raeume, ressourcenarten = bu.Arten(), ressourcen = bu.Ressourcen(!Rechtedienst.Darf(b, Recht.BuchungVerwalten)),
                raumarten = Werte(r => r.Raumart), gebaeude = Werte(r => r.Gebaeude), abteilungen = Werte(r => r.Abteilung), raumgruppen = Werte(r => r.Raumgruppe),
                zeitraster = plan.Zeitraster(s), tageProWoche = Math.Max(5, s.Paket.Zeitraster.TageProWoche),
                maxTermine = Buchungsdienst.MaxTermine,
                // erster Unterrichtstag ab heute (Vorbelegung des Datums, damit der Assistent nicht auf Wochenende oder Ferien startet)
                naechsterTag = Plandienst.Iso(Enumerable.Range(0, 120).Select(i => uhr.Heute.AddDays(i)).FirstOrDefault(t => !plan.Frei(s, t).Frei, uhr.Heute)),
            });
        });

        // Raumwahl mit Belegung (Prüfung eintragen): alle Räume mit frei/belegt, Kapazität, buchbar – ohne die eigene Stunde des Unterrichts
        api.MapGet("/raumstatus", (DateOnly datum, int von, int? bis, int? unterricht, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Ich(c);
            return Results.Json(bu.RaumStatus(Paket(p), datum, Math.Max(1, von), Math.Max(1, bis ?? von), unterricht));
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

        api.MapGet("/ressourcenuebersicht", (string? datum, string? arten, int? tage, HttpContext c, Paketdienst p, Buchungsdienst bu, Uhr uhr) =>
        {
            Ich(c);
            var d = DateOnly.TryParse(datum, out var x) ? x : uhr.Heute;
            return Results.Json(bu.Ressourcenuebersicht(Paket(p), d, Zahlen(arten), tage));
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
        api.MapPost("/ressourcenarten", (ArtDaten d, HttpContext c, Buchungsdienst bu) => { Admin(c); return Results.Json(bu.ArtSpeichern(null, d.Name, d.Reihenfolge, d.Symbol)); });
        api.MapPut("/ressourcenarten/{id:int}", (int id, ArtDaten d, HttpContext c, Buchungsdienst bu) => { Admin(c); return Results.Json(bu.ArtSpeichern(id, d.Name, d.Reihenfolge, d.Symbol)); });
        api.MapDelete("/ressourcenarten/{id:int}", (int id, HttpContext c, Buchungsdienst bu) => { Admin(c); bu.ArtLoeschen(id); return Results.Json(new { ok = true }); });

        api.MapGet("/ressourcen", (HttpContext c, Buchungsdienst bu) => { var b = Ich(c); return Results.Json(bu.Ressourcen(!Rechtedienst.Darf(b, Recht.BuchungVerwalten))); });
        api.MapPost("/ressourcen", (RessourceDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Admin(c);
            return Results.Json(bu.RessourceSpeichern(p.Aktuell, null, d.Name, d.ArtId, d.Raum, d.Anzahl, d.Bemerkung, d.Aktiv ?? true, d.Standort));
        });
        api.MapPut("/ressourcen/{id:int}", (int id, RessourceDaten d, HttpContext c, Paketdienst p, Buchungsdienst bu) =>
        {
            Admin(c);
            return Results.Json(bu.RessourceSpeichern(p.Aktuell, id, d.Name, d.ArtId, d.Raum, d.Anzahl, d.Bemerkung, d.Aktiv ?? true, d.Standort));
        });
        api.MapGet("/symbole", (HttpContext c) => { Admin(c); return Results.Json(Buchungsdienst.Symbole); });
        api.MapPost("/ressourcen/beispiele", (HttpContext c, Paketdienst p, Buchungsdienst bu, Protokoll prot) =>
        {
            var b = Admin(c);
            var n = bu.BeispieleAnlegen(p.Aktuell);
            if (n == 0) throw new ArgumentException("Es sind schon Ressourcen angelegt.");
            prot.Schreiben(b.Login, "Beispiel-Ressourcen angelegt", $"{n} Ressourcen");
            return Results.Json(new { ok = true, angelegt = n });
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
