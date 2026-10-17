using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Rechte und Rollen (nur Administration): Rechte-Katalog, Rollen als Rechte-Profile (anlegen, kopieren, bearbeiten, löschen),
/// Profil und Überschreibungen je Konto sowie die Vorschau der effektiven Rechte. Eigene Rechte: <c>GET /api/app/rechte</c>.
/// </summary>
public static class RechteApi
{
    /// <summary>Planfenster: Wochen zurück bzw. voraus, die der Stundenplan angezeigt wird (null = unbegrenzt, 0 = nur die aktuelle Woche).</summary>
    public sealed record ProfilDaten(string? Name, string? Basis, string? Beschreibung, Dictionary<string, int>? Rechte, int? PlanWochenZurueck = null, int? PlanWochenVor = null);
    public sealed record KopieDaten(string? Name);
    public sealed record KontoRechteDaten(string? Profil, Dictionary<string, int?>? Ueberschreibungen);

    private static Benutzer Admin(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        // Rechte und Rollen bleiben fest bei der Administration (kein Aussperren über Rechte)
        return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.");
    }

    private static object ProfilJson(Rechteprofil p) => new
    {
        p.Id, p.Name, p.Basis, basisName = Rollen.Name(p.Basis), p.Beschreibung, p.System, aenderbar = p.Id != Rollen.Admin, p.Rechte, p.Benutzer,
        p.PlanWochenZurueck, p.PlanWochenVor,
    };

    private static object VorschauJson(Db db, Benutzer b, List<EffektivesRecht> liste, string profil, Dictionary<string, int> ueber) => new
    {
        benutzer = new { b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Zusatzrechte },
        profil,
        profile = Rechtedienst.ProfilListe(db).Where(p => p.Basis == b.Rolle).Select(p => new { id = p.System ? "" : p.Id, p.Name, p.System }),
        ueberschreibungen = ueber,
        rechte = liste,
        anzahl = liste.Count(x => x.Effektiv > Stufe.Nein),
    };

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        // eigene effektive Rechte (für die Oberfläche)
        api.MapGet("/rechte", (HttpContext c) =>
        {
            var b = c.Ich() ?? throw new NichtAngemeldetException();
            return Results.Json(new { rolle = b.Rolle, rechte = Rechtedienst.Liste(b) });
        });

        var admin = app.MapGroup("/api/app/admin/rechte");

        admin.MapGet("/katalog", (HttpContext c, Db db) =>
        {
            Admin(c);
            return Results.Json(new
            {
                gruppen = Recht.Gruppen,
                rechte = Recht.Katalog.Select(r => new { r.Id, r.Gruppe, r.Name, r.Beschreibung, r.Zweistufig, r.NurKollegium, standard = Rollen.Alle.Zip(r.Standard).ToDictionary(x => x.First, x => x.Second) }),
                basisrollen = Rollen.Alle.Select(r => new { id = r, name = Rollen.Name(r) }),
                profile = Rechtedienst.ProfilListe(db).Select(ProfilJson),
                rueckwirkendTage = Recht.RueckwirkendTage,
                planWochenHoechstens = Planfenster.Hoechstens,
            });
        });

        admin.MapPost("/profile", (ProfilDaten d, HttpContext c, Db db, Uhr uhr, Protokoll prot) =>
        {
            var ich = Admin(c);
            var p = Rechtedienst.Speichern(db, null, d.Name, d.Basis, d.Beschreibung, d.Rechte, uhr.Jetzt.ToString("yyyy-MM-dd HH:mm"), d.PlanWochenZurueck, d.PlanWochenVor);
            prot.Schreiben(ich.Login, "Rolle angelegt", p.Name);
            return Results.Json(ProfilJson(p));
        });
        admin.MapPut("/profile/{id}", (string id, ProfilDaten d, HttpContext c, Db db, Uhr uhr, Protokoll prot) =>
        {
            var ich = Admin(c);
            var p = Rechtedienst.Speichern(db, id, d.Name, null, d.Beschreibung, d.Rechte, uhr.Jetzt.ToString("yyyy-MM-dd HH:mm"), d.PlanWochenZurueck, d.PlanWochenVor);
            prot.Schreiben(ich.Login, "Rolle geändert", p.Name);
            return Results.Json(ProfilJson(p));
        });
        admin.MapPost("/profile/{id}/kopieren", (string id, KopieDaten d, HttpContext c, Db db, Uhr uhr, Protokoll prot) =>
        {
            var ich = Admin(c);
            var p = Rechtedienst.Kopieren(db, id, d.Name, uhr.Jetzt.ToString("yyyy-MM-dd HH:mm"));
            prot.Schreiben(ich.Login, "Rolle kopiert", $"{id} → {p.Name}");
            return Results.Json(ProfilJson(p));
        });
        admin.MapPost("/profile/{id}/zuruecksetzen", (string id, HttpContext c, Db db, Protokoll prot) =>
        {
            var ich = Admin(c);
            Rechtedienst.Zuruecksetzen(db, id);
            prot.Schreiben(ich.Login, "Rolle zurückgesetzt", id);
            return Results.Json(ProfilJson(Rechtedienst.Profil(db, id)!));
        });
        admin.MapDelete("/profile/{id}", (string id, HttpContext c, Db db, Protokoll prot) =>
        {
            var ich = Admin(c);
            Rechtedienst.Loeschen(db, id);
            prot.Schreiben(ich.Login, "Rolle gelöscht", id);
            return Results.Json(new { ok = true });
        });

        // Rechte eines Kontos: Profil, Überschreibungen, effektive Rechte mit Herkunft
        admin.MapGet("/benutzer/{id:int}", (int id, HttpContext c, Db db, Benutzerdienst bd) =>
        {
            Admin(c);
            var b = bd.Holen(id) ?? throw new ArgumentException("Unbekannter Benutzer.");
            var profil = Rechtedienst.ProfilVon(db, id);
            var ueber = Rechtedienst.Ueberschreibungen(db, id);
            return Results.Json(VorschauJson(db, b, Rechtedienst.Vorschau(db, b), profil, ueber));
        });
        // Vorschau mit noch nicht gespeicherten Angaben
        admin.MapPost("/benutzer/{id:int}/vorschau", (int id, KontoRechteDaten d, HttpContext c, Db db, Benutzerdienst bd) =>
        {
            Admin(c);
            var b = bd.Holen(id) ?? throw new ArgumentException("Unbekannter Benutzer.");
            var ueber = (d.Ueberschreibungen ?? new()).Where(x => x.Value is int && Recht.NachId.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => Recht.Begrenzen(x.Key, x.Value!.Value));
            return Results.Json(VorschauJson(db, b, Rechtedienst.Vorschau(db, b, d.Profil ?? "", d.Ueberschreibungen ?? new()), d.Profil ?? "", ueber));
        });
        admin.MapPut("/benutzer/{id:int}", (int id, KontoRechteDaten d, HttpContext c, Db db, Benutzerdienst bd, Protokoll prot) =>
        {
            var ich = Admin(c);
            var b = bd.Holen(id) ?? throw new ArgumentException("Unbekannter Benutzer.");
            Rechtedienst.ProfilSetzen(db, b, d.Profil);
            Rechtedienst.UeberschreibungenSetzen(db, id, d.Ueberschreibungen);
            prot.Schreiben(ich.Login, "Rechte geändert", $"{b.Login}: {d.Profil}, {d.Ueberschreibungen?.Count(x => x.Value != null) ?? 0} Überschreibungen");
            b = bd.Holen(id)!;
            return Results.Json(VorschauJson(db, b, Rechtedienst.Vorschau(db, b), Rechtedienst.ProfilVon(db, id), Rechtedienst.Ueberschreibungen(db, id)));
        });
    }
}
