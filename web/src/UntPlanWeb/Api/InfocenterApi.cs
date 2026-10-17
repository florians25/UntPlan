using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Infocenter (runder Knopf unten rechts): Infos der Schule und Neuigkeiten zu UntPlanWeb je Benutzer mit Gelesen-Vermerk.
/// Verwaltung der Infos (anlegen, ändern, löschen) nur mit Recht „Nachrichten“ (Administration, Schulleitung bzw. Zusatzrecht).
/// <list type="bullet">
/// <item>GET /api/app/infocenter – Einträge des Benutzers, Anzahl ungelesen, darfVerwalten</item>
/// <item>POST /api/app/infocenter/gelesen – { schluessel: [...] } bzw. { alle: true }</item>
/// <item>GET /api/app/infocenter/infos – alle Infos (Verwaltung)</item>
/// <item>POST /api/app/infocenter/infos, PUT /api/app/infocenter/infos/{id}, DELETE /api/app/infocenter/infos/{id}</item>
/// </list>
/// </summary>
public static class InfocenterApi
{
    public sealed record GelesenDaten(List<string>? Schluessel, bool Alle);
    public sealed record InfoDaten(string? Titel, string? Text, string? Zielgruppe, DateOnly? Von, DateOnly? Bis, bool Wichtig);

    private static Infocenterdienst Dienst(HttpContext c)
    {
        var sp = c.RequestServices;
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        var datei = env.WebRootPath is { Length: > 0 } w ? Path.Combine(w, "neuigkeiten.json") : null;
        return new Infocenterdienst(sp.GetRequiredService<Db>(), sp.GetRequiredService<Uhr>(), datei);
    }

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Benutzer Verwalter(HttpContext c)
    {
        var b = Ich(c);
        if (!Rechtedienst.Darf(b, Recht.InfocenterVerwalten)) throw new UnauthorizedAccessException("Infos dürfen nur Administration und Schulleitung verwalten.");
        return b;
    }

    private static object Json(Info i) => new
    {
        i.Id, i.Titel, i.Text, i.Zielgruppe, i.Von, i.Bis, i.Wichtig, i.Erstellt, i.Autor, i.Geaendert,
    };

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/infocenter");

        api.MapGet("", (HttpContext c) =>
        {
            var b = Ich(c);
            var l = Dienst(c).Fuer(b);
            return Results.Json(new { eintraege = l, ungelesen = l.Count(x => !x.Gelesen), darfVerwalten = Rechtedienst.Darf(b, Recht.InfocenterVerwalten) });
        });

        api.MapPost("/gelesen", (GelesenDaten d, HttpContext c) =>
        {
            var b = Ich(c);
            var n = Dienst(c).GelesenMarkieren(b, d.Alle ? null : d.Schluessel ?? new());
            return Results.Json(new { ok = true, markiert = n });
        });

        api.MapGet("/infos", (HttpContext c) =>
        {
            Verwalter(c);
            return Results.Json(Dienst(c).Alle().Select(Json));
        });

        api.MapPost("/infos", (InfoDaten d, HttpContext c, Protokoll prot) =>
        {
            var b = Verwalter(c);
            var i = Dienst(c).Anlegen(b, d.Titel, d.Text, d.Zielgruppe, d.Von, d.Bis, d.Wichtig);
            prot.Schreiben(b.Login, "Info angelegt", $"{i.Titel} ({i.Zielgruppe})");
            return Results.Json(Json(i));
        });

        api.MapPut("/infos/{id:int}", (int id, InfoDaten d, HttpContext c, Protokoll prot) =>
        {
            var b = Verwalter(c);
            var i = Dienst(c).Aendern(id, d.Titel, d.Text, d.Zielgruppe, d.Von, d.Bis, d.Wichtig);
            prot.Schreiben(b.Login, "Info geändert", $"{i.Titel} ({i.Zielgruppe})");
            return Results.Json(Json(i));
        });

        api.MapDelete("/infos/{id:int}", (int id, HttpContext c, Protokoll prot) =>
        {
            var b = Verwalter(c);
            if (!Dienst(c).Loeschen(id)) return Results.Json(new { fehler = "Die Info gibt es nicht (mehr)." }, statusCode: 404);
            prot.Schreiben(b.Login, "Info gelöscht", $"#{id}");
            return Results.Json(new { ok = true });
        });
    }
}
