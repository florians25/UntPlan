using UntPlanWeb.Dienste;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Api;

/// <summary>
/// Mitteilungen – Erweiterungen unter <c>/api/app/mitteilungen</c> (Grundfunktionen in <see cref="AppApi"/> und <see cref="ErweiterungApi"/>):
/// <list type="bullet">
/// <item><c>GET /optionen</c>: darf senden, Grenzen für Anhänge, Lesebestätigung möglich.</item>
/// <item><c>POST /{id}/bestaetigen</c>: Lesebestätigung senden; <c>GET /{id}/lesebestaetigungen</c>: Lesebestätigungsliste (Absender).</item>
/// <item><c>GET /{id}/antwort?alle=</c>: Vorschlag für Antworten bzw. Allen antworten; <c>POST /{id}/weiterleiten</c>: Entwurf mit Anhängen.</item>
/// <item><c>POST /{id}/ungelesen</c>: wieder als ungelesen markieren.</item>
/// <item><c>GET /{id}</c>: eine Mitteilung (Detail mit Ordner, darf antworten, Download-Adressen der Anhänge).</item>
/// <item><c>GET /personen?typ=kollegium|individuell</c>: Empfängerauswahl der Apps mit Rollen-Pills und Filtern (Listen, Klassen, Abteilungen).</item>
/// <item>Administration: <c>GET/PUT /einstellungen</c> – wer an wen schreiben darf, Sperren, Anhänge, Aufbewahrung.</item>
/// </list>
/// </summary>
public static class MitteilungApi
{
    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();
    private static Benutzer Admin(HttpContext c) { var b = Ich(c); return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration."); }

    /// <summary>Empfängertypen der Apps („An wen soll die Mitteilung gesendet werden?“).</summary>
    private static object[] Typen(Benutzer b) => b.Kollegium
        ? new object[] { new { id = "kollegium", name = "Kolleg*innen" }, new { id = "individuell", name = "Individuell" } }
        : new object[] { new { id = "kollegium", name = "Lehrkräfte" } };

    private static readonly (string Id, string Name)[] RollenFolge =
    {
        ("lehrkraft", "Lehrkraft"), ("klassenlehrkraft", "Klassenlehrkraft"), ("schulleitung", "Schulleitung"), ("administration", "Administration"),
        ("schueler", "Schüler*in"), ("eltern", "Erziehungsberechtigte*r"),
    };
    private static readonly (string Id, string Name)[] GruppenFolge =
    {
        ("kollegium", "Kollegium"), ("leitung", "Schulleitung und Administration"), ("klassenleitungen", "Klassenlehrkräfte"),
        ("schueler", "Schüler*innen"), ("eltern", "Erziehungsberechtigte"),
    };

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/mitteilungen");

        api.MapGet("/optionen", (HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            var e = m.Einstellungen();
            return Results.Json(new { darfSenden = m.DarfSenden(b, e), darfAntworten = m.DarfAntworten(b, e), maxAnhangMb = e.MaxAnhangMb, maxAnhaenge = e.MaxAnhaenge, aufbewahrungTage = e.AufbewahrungTage,
                wichtig = b.Kollegium, listen = b.Kollegium, admin = b.IstAdmin, lesebestaetigung = true, antwortSperre = true, typen = Typen(b) });
        });

        api.MapGet("/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            if (m.Detail(b, id) is not var (x, ordner)) return Results.Json(new { fehler = "Unbekannte Mitteilung." }, statusCode: 404);
            return Results.Json(new
            {
                x.Id, x.AbsenderId, x.Absender, x.AbsenderRolle, x.Betreff, x.Text, x.Erstellt, x.Wichtig, x.Gelesen, x.Empfaenger, x.EmpfaengerText,
                x.Lesebestaetigung, x.Bestaetigt, x.Anzahl, x.GelesenAnzahl, x.BestaetigtAnzahl, x.AntwortAuf, x.EntwurfEmpfaenger, x.AntwortErlaubt,
                ordner, darfAntworten = ordner == "posteingang" && x.AntwortErlaubt && m.DarfAntworten(b),
                anhaenge = (x.Anhaenge ?? new()).Select(a => new { a.Id, a.Name, a.Typ, a.Groesse, url = $"/api/app/mitteilungen/anhang/{a.Id}" }),
            });
        });

        api.MapGet("/personen", (string? typ, HttpContext c, Mitteilungsdienst m, Paketdienst p, Db db) =>
        {
            var b = Ich(c);
            var art = typ == "individuell" ? "individuell" : "kollegium";
            var (personen, gruppen) = m.Personen(b, p.Aktuell, art);
            var listen = b.Kollegium ? m.Listen(b, p.Aktuell, false).Select(l => new { id = "liste:" + l.Id, l.Name, l.Mitglieder }).ToList() : new();
            var de = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), true);
            return Results.Json(new
            {
                typ = art, typen = Typen(b), personen, gruppen,
                filter = new
                {
                    listen,
                    klassen = personen.SelectMany(x => x.Klassen).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, de),
                    abteilungen = personen.SelectMany(x => x.Abteilungen).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, de),
                    // Rolle: Art der Rollen-Pills (Lehrkraft, Klassenlehrkraft, Schulleitung, …) in fester Reihenfolge
                    rollen = RollenFolge.Where(r => personen.Any(x => x.Rollen.Any(y => y.Art == r.Id))).Select(r => new { id = r.Id, name = r.Name }),
                    // Gruppe: feste Personengruppen und eigene Rechte-Profile (Name aus dem Rechte-Modell)
                    gruppen = GruppenFolge.Where(g => personen.Any(x => x.Gruppen?.Contains(g.Id) == true)).Select(g => new { id = g.Id, name = g.Name })
                        .Concat(personen.SelectMany(x => x.Gruppen ?? new()).Where(g => g.StartsWith("profil:")).Distinct()
                            .Select(g => new { id = g, name = Rechtedienst.Profil(db, g[7..])?.Name ?? g[7..] }).OrderBy(g => g.name, de)),
                },
            });
        });

        api.MapPost("/{id:int}/bestaetigen", (int id, HttpContext c, Mitteilungsdienst m) => Results.Json(new { bestaetigt = m.Bestaetigen(Ich(c).Id, id) }));
        api.MapGet("/{id:int}/lesebestaetigungen", (int id, HttpContext c, Mitteilungsdienst m) =>
        {
            var liste = m.Lesestatus(Ich(c), id);
            return Results.Json(new { anzahl = liste.Count, gelesen = liste.Count(x => x.Gelesen != null), bestaetigt = liste.Count(x => x.Bestaetigt != null), empfaenger = liste });
        });
        api.MapPost("/{id:int}/ungelesen", (int id, HttpContext c, Mitteilungsdienst m) => { m.Ungelesen(Ich(c).Id, id); return Results.Json(new { ok = true }); });
        api.MapGet("/{id:int}/antwort", (int id, bool? alle, HttpContext c, Mitteilungsdienst m, Paketdienst p) => Results.Json(m.Antwort(Ich(c), p.Aktuell, id, alle == true)));
        api.MapPost("/{id:int}/weiterleiten", (int id, HttpContext c, Mitteilungsdienst m) => Results.Json(m.Weiterleiten(Ich(c), id)));

        api.MapGet("/einstellungen", (HttpContext c, Mitteilungsdienst m, Benutzerdienst bd) =>
        {
            Admin(c);
            return Results.Json(new
            {
                einstellungen = m.Einstellungen(),
                benutzer = bd.Alle().OrderBy(b => b.Rolle).ThenBy(b => b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login)
                    .Select(b => new { b.Id, name = b.Anzeigename.Length > 0 ? $"{b.Anzeigename} ({b.Login})" : b.Login, rolle = Rollen.Name(b.Rolle) }),
            });
        });
        api.MapPut("/einstellungen", (MitteilungsEinstellungen d, HttpContext c, Mitteilungsdienst m, Protokoll prot) =>
        {
            var b = Admin(c);
            var neu = m.EinstellungenSpeichern(d);
            prot.Schreiben(b.Login, "Mitteilungen: Einstellungen", $"Anhang {neu.MaxAnhangMb} MB, {neu.MaxAnhaenge} Anhänge, {neu.AufbewahrungTage} Tage, {neu.Gesperrt!.Count} gesperrt");
            return Results.Json(neu);
        });
    }
}
