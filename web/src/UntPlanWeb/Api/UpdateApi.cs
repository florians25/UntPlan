using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Versionsanzeige und Updates in der Administration:
/// <list type="bullet">
/// <item><c>GET /api/app/admin/update[?neu=1]</c> – laufende Version, Datenbank, neueste Version, Updatehinweis, Updater.</item>
/// <item><c>POST /api/app/admin/update/installieren</c> – Update anstoßen (nur Administration, Kopf <c>X-UntPlanWeb: 1</c>).</item>
/// </list>
/// </summary>
public static class UpdateApi
{
    private static Benutzer Admin(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.");
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/admin/update");

        api.MapGet("", async (HttpContext c, Updatedienst u, Db db, bool? neu, CancellationToken ct) =>
        {
            Admin(c);
            var stand = await u.StandAsync(neu == true, ct);
            return Results.Json(new
            {
                version = stand.Version,
                datenbank = db.Art == Datenbankart.MySql ? "MySQL/MariaDB" : "SQLite",
                schemastand = db.Version(),
                pruefungAktiv = stand.PruefungAktiv,
                neueste = stand.Neueste,
                updateVerfuegbar = stand.UpdateVerfuegbar,
                hinweisurl = stand.Hinweisurl,
                quelle = stand.Quelle,
                geprueft = stand.Geprueft,
                fehler = stand.Fehler,
                modus = u.Einstellungen.Modus,
                installierbar = u.Installierbar,
                angefordert = u.Angefordert,
                befehl = Updatedienst.Befehl,
                mobil = await u.MobilAsync(ct),
            });
        });

        api.MapPost("/installieren", async (HttpContext c, Updatedienst u, CancellationToken ct) =>
        {
            var ich = Admin(c);
            // zusätzlich zur allgemeinen Prüfung in Program.cs: nur unser Frontend setzt diesen Kopf (CSRF)
            if (c.Request.Headers["X-UntPlanWeb"] != "1") return Results.BadRequest(new { fehler = "Ungültige Anfrage." });
            var r = await u.InstallierenAsync(ich.Login, ct);
            return r.Ok ? Results.Json(new { ok = true, meldung = r.Meldung, sicherung = r.Sicherung is null ? null : Path.GetFileName(r.Sicherung) })
                        : Results.Json(new { fehler = r.Meldung }, statusCode: 409);
        });
    }
}
