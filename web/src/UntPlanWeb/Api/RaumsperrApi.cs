using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Raumsperren (Administration → Raumsperren) unter <c>/api/app/raumsperren</c>: <c>GET /</c> (Sperren mit Auswahllisten), <c>POST /</c>,
/// <c>PUT /{id}</c>, <c>DELETE /{id}</c>; Buchen → Buchungen ent-/sperren: <c>GET|PUT /buchungen</c> { gesperrt }.
/// Nur mit dem Recht „Buchungen verwalten“.
/// </summary>
public static class RaumsperrApi
{
    public sealed record SperrSchalter(bool Gesperrt);

    private static Benutzer Verwaltung(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!Rechtedienst.Darf(b, Recht.BuchungVerwalten)) throw new UnauthorizedAccessException("Nur für die Administration der Buchungen.");
        return b;
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/raumsperren");

        api.MapGet("/", (HttpContext c, Raumsperrdienst rs, Buchungsdienst bu, Paketdienst p) =>
        {
            Verwaltung(c);
            var s = p.Aktuell;
            var raeume = s is null ? new List<Raumdaten>() : bu.Raeume(s);
            var ressourcen = bu.Ressourcen();
            string Name(int id) => ressourcen.FirstOrDefault(r => r.Id == id)?.Name ?? $"Ressource {id}";
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            return Results.Json(new
            {
                sperren = rs.Alle().Select(x => new
                {
                    x.Id, x.Art, artText = Raumsperrdienst.ArtText.GetValueOrDefault(x.Art, x.Art), x.Ziel,
                    zielText = x.Art == Raumsperrdienst.Ressource ? Name(int.TryParse(x.Ziel, out var i) ? i : 0) : x.Ziel,
                    x.Von, x.Bis, x.Beginn, x.Ende, x.Kontinuierlich, x.Wochentag, x.Text, beschreibung = Raumsperrdienst.Beschreibung(x, Name),
                }),
                raeume = raeume.OrderBy(r => r.Kurz, de).Select(r => new { r.Kurz, r.Name }),
                raumgruppen = raeume.Select(r => r.Raumgruppe).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, de),
                ressourcen = ressourcen.OrderBy(r => r.Name, de).Select(r => new { r.Id, r.Name }),
                buchungenGesperrt = rs.BuchungenGesperrt,
            });
        });

        api.MapPost("/", (RaumsperreDaten d, HttpContext c, Raumsperrdienst rs, Protokoll prot) =>
        {
            var b = Verwaltung(c);
            var x = rs.Speichern(null, d, b);
            prot.Schreiben(b.Login, "Raumsperre angelegt", Raumsperrdienst.Beschreibung(x));
            return Results.Json(new { x.Id });
        });

        api.MapPut("/{id:int}", (int id, RaumsperreDaten d, HttpContext c, Raumsperrdienst rs, Protokoll prot) =>
        {
            var b = Verwaltung(c);
            var x = rs.Speichern(id, d, b);
            prot.Schreiben(b.Login, "Raumsperre geändert", Raumsperrdienst.Beschreibung(x));
            return Results.Json(new { x.Id });
        });

        api.MapDelete("/{id:int}", (int id, HttpContext c, Raumsperrdienst rs, Protokoll prot) =>
        {
            var b = Verwaltung(c);
            var x = rs.Holen(id) ?? throw new ArgumentException("Unbekannte Sperre.");
            rs.Loeschen(id);
            prot.Schreiben(b.Login, "Raumsperre gelöscht", Raumsperrdienst.Beschreibung(x));
            return Results.Json(new { ok = true });
        });

        api.MapGet("/buchungen", (HttpContext c, Raumsperrdienst rs) => { Verwaltung(c); return Results.Json(new { gesperrt = rs.BuchungenGesperrt }); });

        api.MapPut("/buchungen", (SperrSchalter d, HttpContext c, Raumsperrdienst rs, Protokoll prot) =>
        {
            var b = Verwaltung(c);
            rs.BuchungenSperren(d.Gesperrt);
            prot.Schreiben(b.Login, d.Gesperrt ? "Buchungen gesperrt" : "Buchungen entsperrt", "");
            return Results.Json(new { gesperrt = rs.BuchungenGesperrt });
        });
    }
}
