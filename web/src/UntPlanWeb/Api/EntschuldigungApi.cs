using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Fachlehrer-Entschuldigungen und Entschuldigungen der Klassenleitung (Web und Apps) – Vertrag siehe vertrag_entschuldigungen.md:
/// <list type="bullet">
/// <item><c>GET /api/app/entschuldigungen/einstellungen</c> – was darf das Konto (Modul aktiv, Fachlehrer, Klassenleitung, Jahrgänge).</item>
/// <item><c>GET/POST /api/app/entschuldigungen/fehlzeiten</c> – Fachlehrer-Sicht: Fehlstunden des eigenen Unterrichts lesen bzw. entschuldigen.</item>
/// <item><c>GET /api/app/entschuldigungen/abwesenheiten</c>, <c>GET/POST /api/app/entschuldigungen/abwesenheit</c> – Klassenleitung: Abwesenheiten
/// mit Stand der Fachlehrer-Entschuldigungen, Detail (Seitenleiste), ganze Abwesenheit entschuldigen.</item>
/// <item><c>POST /api/app/entschuldigungen/zuruecknehmen</c> – Klassenleitung nimmt die Entschuldigung zurück.</item>
/// </list>
/// </summary>
public static class EntschuldigungApi
{
    public sealed record StundenDaten(List<int>? Ids, string? Status, string? Text, string? Datum);
    public sealed record AbwesenheitDaten(List<int>? Ids, string? Status, string? Text, string? Datum, string? Nachweis);
    public sealed record IdsDaten(List<int>? Ids);

    private static Benutzer Kollegium(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium.");
    }

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
    private static DateOnly Datum(string? s, DateOnly vorgabe) => DateOnly.TryParse(s, out var d) ? d : vorgabe;
    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Status(string? s) => Leer(s) is "offen" or "entschuldigt" or "unentschuldigt" ? s!.Trim() : null;

    private static List<int> IdsAus(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).Distinct().ToList();

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/entschuldigungen");

        api.MapGet("/einstellungen", (HttpContext c, Paketdienst p, Entschuldigungsdienst ed) => Results.Json(ed.Einstellungen(Kollegium(c), p.Aktuell)));

        api.MapGet("/fehlzeiten", (string? von, string? bis, string? klasse, string? status, bool? nurOffene, HttpContext c, Paketdienst p, Entschuldigungsdienst ed, Uhr uhr) =>
            Results.Json(ed.Fachlehrerliste(Kollegium(c), Paket(p), Datum(von, uhr.Heute.AddDays(-14)), Datum(bis, uhr.Heute), Leer(klasse), Status(status), nurOffene == true)));

        api.MapPost("/fehlzeiten", (StundenDaten d, HttpContext c, Paketdienst p, Entschuldigungsdienst ed, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var (n, gesperrt) = ed.StundenEntschuldigen(b, Paket(p), d.Ids ?? new(), d.Status, d.Text, d.Datum);
            if (n > 0) prot.Schreiben(b.Login, "Fehlstunden entschuldigt", $"{n} ({d.Status})");
            return Results.Json(new { geaendert = n, gesperrt });
        });

        api.MapGet("/abwesenheiten", (string? von, string? bis, string? klasse, string? status, HttpContext c, Paketdienst p, Entschuldigungsdienst ed, Uhr uhr) =>
            Results.Json(ed.Abwesenheiten(Kollegium(c), Paket(p), Datum(von, uhr.Heute.AddDays(-14)), Datum(bis, uhr.Heute), Leer(klasse), Status(status))));

        api.MapGet("/abwesenheit", (string? ids, HttpContext c, Paketdienst p, Entschuldigungsdienst ed) =>
            Results.Json(ed.Detail(Kollegium(c), Paket(p), IdsAus(ids))));

        api.MapPost("/abwesenheit", (AbwesenheitDaten d, HttpContext c, Paketdienst p, Entschuldigungsdienst ed, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var (n, ueber) = ed.AbwesenheitEntschuldigen(b, Paket(p), d.Ids ?? new(), d.Status, d.Text, d.Datum, d.Nachweis);
            prot.Schreiben(b.Login, d.Status == "offen" ? "Entschuldigung zurückgenommen" : "Abwesenheit entschuldigt", $"{n} Stunden ({d.Status})");
            return Results.Json(new { geaendert = n, ueberschrieben = ueber });
        });

        api.MapPost("/zuruecknehmen", (IdsDaten d, HttpContext c, Paketdienst p, Entschuldigungsdienst ed, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var n = ed.Zuruecknehmen(b, Paket(p), d.Ids ?? new());
            prot.Schreiben(b.Login, "Entschuldigung zurückgenommen", $"{n} Stunden");
            return Results.Json(new { geaendert = n });
        });
    }
}
