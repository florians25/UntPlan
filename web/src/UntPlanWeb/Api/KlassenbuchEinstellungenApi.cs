using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Klassenbuch | Einstellungen und Unterricht | Einstellungen (Prüfungen) sowie Stammdaten | Notenschemen und Prüfungsarten.
/// <list type="bullet">
/// <item><c>GET/PUT /api/app/klassenbuch/einstellungen</c> – Lesen für das Kollegium, Ändern mit dem Recht „Klassenbuch-Einstellungen“.</item>
/// <item><c>POST /api/app/klassenbuch/einstellungen/erinnerung</c> – Erinnerung an offene Stunden sofort senden.</item>
/// <item><c>/api/app/notenschemen</c> und <c>/api/app/pruefungsarten</c> – Lesen für das Kollegium, Pflege mit dem Recht „Stammdaten“.</item>
/// </list>
/// </summary>
public static class KlassenbuchEinstellungenApi
{
    public sealed record SchemaDaten(string? Name, bool Aktiv = true, List<Note>? Noten = null);
    public sealed record IdListe(List<int>? Ids);
    public sealed record GesamtDaten(int? Id);

    private static Benutzer Kollegium(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium.");
    }

    private static Benutzer Stammdaten(HttpContext c) => RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminStammdaten);

    public static void Abbilden(WebApplication app)
    {
        var kb = app.MapGroup("/api/app/klassenbuch/einstellungen");

        kb.MapGet("", (HttpContext c, Db db, Klassenbuchverwaltung kv) =>
        {
            var b = Kollegium(c);
            return Results.Json(new
            {
                einstellungen = Klassenbucheinstellungen.Laden(db),
                darf = Rechtedienst.Darf(b, Recht.KlassenbuchEinstellungen),
                gruende = kv.Gruende("abwesenheit").Where(g => g.Aktiv).Select(g => new { g.Kurz, g.Name, g.Entschuldigt }),
                loeschsperreHoechstens = Klassenbucheinstellungen.LoeschsperreHoechstens,
            });
        });

        kb.MapPut("", (KlassenbuchEinstellungen d, HttpContext c, Db db, Protokoll prot) =>
        {
            var b = RechtePruefung.Verlangen(Kollegium(c), Recht.KlassenbuchEinstellungen);
            var neu = Klassenbucheinstellungen.Speichern(db, d);
            prot.Schreiben(b.Login, "Klassenbuch-Einstellungen geändert");
            return Results.Json(neu);
        });

        kb.MapPost("/erinnerung", (HttpContext c, Klassenbucherinnerung er, Protokoll prot) =>
        {
            var b = RechtePruefung.Verlangen(Kollegium(c), Recht.KlassenbuchEinstellungen);
            var r = er.Senden();
            prot.Schreiben(b.Login, "Erinnerung an offene Stunden gesendet", $"{r.Lehrkraefte} Lehrkräfte, {r.Stunden} Stunden");
            return Results.Json(r);
        });

        // ---------------------------------------------------------------- Notenschemen

        var ns = app.MapGroup("/api/app/notenschemen");
        ns.MapGet("", (HttpContext c, Notendienst n) =>
        {
            var b = Kollegium(c);
            var alle = Rechtedienst.Darf(b, Recht.AdminStammdaten);
            return Results.Json(new
            {
                schemen = n.Schemen().Where(x => alle || x.Aktiv).Select(x => new { x.Id, x.Name, x.Aktiv, x.Reihenfolge, x.Noten, x.MitDurchschnitt }),
                gesamt = n.Gesamtschema(), darf = alle,
            });
        });
        ns.MapPost("", (SchemaDaten d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var x = n.SchemaSpeichern(null, d.Name, d.Aktiv, d.Noten);
            prot.Schreiben(b.Login, "Notenschema angelegt", x.Name);
            return Results.Json(x);
        });
        ns.MapPut("/{id:int}", (int id, SchemaDaten d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var x = n.SchemaSpeichern(id, d.Name, d.Aktiv, d.Noten);
            prot.Schreiben(b.Login, "Notenschema geändert", x.Name);
            return Results.Json(x);
        });
        ns.MapPost("/loeschen", (IdListe d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var anzahl = n.SchemenLoeschen(d.Ids ?? new());
            prot.Schreiben(b.Login, "Notenschemen gelöscht", anzahl.ToString());
            return Results.Json(new { anzahl });
        });
        ns.MapPut("/reihenfolge", (IdListe d, HttpContext c, Notendienst n) =>
        {
            Stammdaten(c);
            n.SchemenSortieren(d.Ids);
            return Results.Json(new { ok = true });
        });
        ns.MapPut("/gesamt", (GesamtDaten d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            n.GesamtschemaSetzen(d.Id);
            prot.Schreiben(b.Login, "Gesamtnotenschema gewählt", d.Id?.ToString() ?? "keines");
            return Results.Json(new { gesamt = n.Gesamtschema() });
        });

        // ---------------------------------------------------------------- Prüfungsarten

        var pa = app.MapGroup("/api/app/pruefungsarten");
        pa.MapGet("", (bool? alle, HttpContext c, Notendienst n) =>
        {
            var b = Kollegium(c);
            var darf = Rechtedienst.Darf(b, Recht.AdminStammdaten);
            return Results.Json(new { arten = n.Arten(!(alle == true && darf)), darf });
        });
        pa.MapPost("", (PruefungsartDaten d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var x = n.ArtSpeichern(null, d);
            prot.Schreiben(b.Login, "Prüfungsart angelegt", x.Kurz);
            return Results.Json(x);
        });
        pa.MapPut("/{id:int}", (int id, PruefungsartDaten d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var x = n.ArtSpeichern(id, d);
            prot.Schreiben(b.Login, "Prüfungsart geändert", x.Kurz);
            return Results.Json(x);
        });
        pa.MapPost("/loeschen", (IdListe d, HttpContext c, Notendienst n, Protokoll prot) =>
        {
            var b = Stammdaten(c);
            var anzahl = n.ArtenLoeschen(d.Ids ?? new());
            prot.Schreiben(b.Login, "Prüfungsarten gelöscht", anzahl.ToString());
            return Results.Json(new { anzahl });
        });
        pa.MapPut("/reihenfolge", (IdListe d, HttpContext c, Notendienst n) =>
        {
            Stammdaten(c);
            n.ArtenSortieren(d.Ids);
            return Results.Json(new { ok = true });
        });
    }
}
