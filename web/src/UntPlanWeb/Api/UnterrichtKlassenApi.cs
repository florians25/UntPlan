using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Unterricht → Unterricht Klassen, Schülergruppen und Klassenwechsel:
/// <list type="bullet">
/// <item><c>GET /api/app/unterrichtklassen?klasse=&amp;von=&amp;bis=</c> – Unterrichte der Klasse im Zeitraum (U-Nr, Klassen, Schülergruppe, Lehrkräfte, Fach, Stunden, Termine, Schülerzahl, Schülergruppen).</item>
/// <item><c>GET /api/app/unterrichtklassen/{unterricht}/gruppen?datum=</c> – Schülergruppen eines Unterrichts; <c>PUT|DELETE …/gruppen/{id}</c> – Gast-Schülergruppe ändern bzw. Gruppe löschen.</item>
/// <item><c>GET|PUT /api/app/unterrichtklassen/{unterricht}/gruppen/{id}/schueler?datum=</c> – Schüler*innen in der Schülergruppe (Auswahl, von/bis); Id 0 = Standardgruppe.</item>
/// <item><c>GET /api/app/unterrichtswechsel/unterricht?klasse=&amp;von=&amp;bis=</c>, <c>POST /api/app/unterrichtswechsel</c> – befristeter Unterrichtswechsel (Assistent).</item>
/// <item><c>GET|POST /api/app/schueler/{id}/klassenwechsel</c>, <c>DELETE …/klassenwechsel/{wechsel}</c> – Klassenwechsel ab Datum und Klassenverlauf.</item>
/// </list>
/// </summary>
public static class UnterrichtKlassenApi
{
    public sealed record MitgliederDaten(List<MitgliedEingabe>? Schueler);
    public sealed record GruppeDaten(string? Name, string? Von, string? Bis);
    public sealed record WechselDaten(string? Von, string? Bis, List<int>? SchuelerIds, string? Zielklasse, List<int>? Unterrichte);
    public sealed record KlassenwechselDaten(string? Klasse, string? Beginn);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static DateOnly Tag(string? s, DateOnly vorgabe) => DateOnly.TryParse(s, out var d) ? d : vorgabe;

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        api.MapGet("/unterrichtklassen", (string klasse, string? von, string? bis, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Uhr uhr) =>
        {
            var b = Ich(c);
            var v = Tag(von, Plandienst.Montag(uhr.Heute));
            return Results.Json(ug.Klassenliste(b, Paket(p), klasse, v, Tag(bis, v.AddDays(6))));
        });
        api.MapGet("/unterrichtklassen/{unterricht:int}/gruppen", (int unterricht, string? datum, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Uhr uhr) =>
            Results.Json(ug.Gruppen(Ich(c), Paket(p), unterricht, Tag(datum, uhr.Heute))));
        api.MapPut("/unterrichtklassen/{unterricht:int}/gruppen/{id:int}", (int unterricht, int id, GruppeDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            ug.GruppeAendern(b, Paket(p), unterricht, id, d.Name, d.Von, d.Bis);
            prot.Schreiben(b.Login, "Schülergruppe geändert", $"Unterricht {unterricht}, Gruppe {id}");
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/unterrichtklassen/{unterricht:int}/gruppen/{id:int}", (int unterricht, int id, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            ug.GruppeLoeschen(b, Paket(p), unterricht, id);
            prot.Schreiben(b.Login, "Schülergruppe gelöscht", $"Unterricht {unterricht}, Gruppe {id}");
            return Results.Json(new { ok = true });
        });
        api.MapGet("/unterrichtklassen/{unterricht:int}/gruppen/{id:int}/schueler", (int unterricht, int id, string? datum, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Uhr uhr) =>
            Results.Json(ug.Mitglieder(Ich(c), Paket(p), unterricht, id, Tag(datum, uhr.Heute))));
        api.MapPut("/unterrichtklassen/{unterricht:int}/gruppen/{id:int}/schueler", (int unterricht, int id, string? datum, MitgliederDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Uhr uhr, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = ug.MitgliederSpeichern(b, Paket(p), unterricht, id, d.Schueler ?? new(), Tag(datum, uhr.Heute));
            prot.Schreiben(b.Login, "Schülergruppe bearbeitet", $"Unterricht {unterricht}");
            return Results.Json(res);
        });

        api.MapGet("/unterrichtswechsel/unterricht", (string klasse, string? von, string? bis, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Uhr uhr) =>
        {
            var v = Tag(von, uhr.Heute);
            return Results.Json(ug.Zielunterrichte(Ich(c), Paket(p), klasse, v, Tag(bis, v)));
        });
        api.MapPost("/unterrichtswechsel", (WechselDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = ug.Unterrichtswechsel(b, Paket(p), d.Von, d.Bis, d.SchuelerIds, d.Zielklasse, d.Unterrichte);
            prot.Schreiben(b.Login, "Befristeter Unterrichtswechsel", $"{d.Von} – {d.Bis}, {d.SchuelerIds?.Count ?? 0} Schüler*innen → {d.Zielklasse}");
            return Results.Json(res);
        });

        api.MapGet("/schueler/{id:int}/klassenwechsel", (int id, HttpContext c, Paketdienst p, Unterrichtsgruppen ug) =>
        {
            var b = Ich(c);
            return Results.Json(new { verlauf = ug.Verlauf(b, Paket(p), id), bearbeiten = Schuelerverwaltung.Verwalten(b) });
        });
        api.MapPost("/schueler/{id:int}/klassenwechsel", (int id, KlassenwechselDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            var verlauf = ug.KlassenwechselAnlegen(b, Paket(p), id, d.Klasse, d.Beginn);
            prot.Schreiben(b.Login, "Klassenwechsel", $"Schüler*in {id} → {d.Klasse} ab {d.Beginn}");
            return Results.Json(new { verlauf, bearbeiten = true });
        });
        api.MapDelete("/schueler/{id:int}/klassenwechsel/{wechsel:int}", (int id, int wechsel, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            var verlauf = ug.KlassenwechselLoeschen(b, Paket(p), id, wechsel);
            prot.Schreiben(b.Login, "Klassenwechsel gelöscht", $"Schüler*in {id}");
            return Results.Json(new { verlauf, bearbeiten = true });
        });
    }
}
