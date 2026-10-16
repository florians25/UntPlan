using System.Globalization;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Stundenplan-Daten (Ersatz für api/mobile.php), Anmeldung (api/login.php) und Profilwechsel (api/accounts.php).
/// </summary>
public static class MobilApi
{
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    public static void Abbilden(WebApplication app)
    {
        // ---------------------------------------------------------------- Stundenplan

        app.MapGet("/api/mobile", async (HttpContext c, Kontodienst konten, Wochendienst wochen, UntPlanWebVerbindung web, Zeitgeber zeit,
            string? action, string? date, string? type, string? id) =>
        {
            switch (action ?? "week")
            {
                case "selftest":
                {
                    string erreichbar;
                    try { await web.AufrufAsync(c, HttpMethod.Get, "gesundheit", anmeldungNoetig: false); erreichbar = "ja"; }
                    catch (UntPlanWebFehler ex) { erreichbar = ex.Message; }
                    return Results.Json(new { ok = true, untplanweb = web.Adresse?.ToString(), erreichbar, angemeldet = UntPlanWebVerbindung.Sitzung(c) != null });
                }
                case "meta":
                {
                    var k = await konten.LadenAsync(c);
                    return Results.Json(new
                    {
                        ok = true, own = Wochendienst.Eigen(k), isStudent = k.IstSchueler, entities = Wochendienst.Entitaeten(k),
                        lessonTimes = Kachelbau.Stundenzeiten(k.Elemente.Stunden ?? new()), names = Wochendienst.Namen(k), school = k.Schule,
                        user = Wochendienst.Benutzer(k),
                    });
                }
                case "week":
                {
                    if (date != null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return Fehler("bad_date");
                    var k = await konten.LadenAsync(c);
                    var datum = date is null ? zeit.Heute : DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return Results.Json(await wochen.WocheAsync(c, k, type, id, datum));
                }
                default:
                    return Fehler("unknown_action");
            }
        });

        // ---------------------------------------------------------------- Anmeldung

        app.MapMethods("/api/login", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten) =>
        {
            var p = await Parameter.LesenAsync(c);
            var aktion = p.Text("action", HttpMethods.IsGet(c.Request.Method) ? "status" : "login");
            if (aktion == "status") return Results.Json(new { ok = true, loggedIn = UntPlanWebVerbindung.Sitzung(c) != null });
            if (HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);

            if (aktion == "logout")
            {
                konten.Vergessen(c);
                if (UntPlanWebVerbindung.Sitzung(c) != null)
                    try { await web.AufrufAsync(c, HttpMethod.Post, "api/app/abmelden", new { }); } catch (Exception ex) when (ex is UntPlanWebFehler or NichtAngemeldetException) { }
                UntPlanWebVerbindung.SitzungLoeschen(c);
                return Results.Json(new { ok = true });
            }
            if (aktion != "login") return Fehler("unknown_action");

            var benutzer = p["username"].Trim();
            var passwort = p["password"];
            if (benutzer.Length == 0 || passwort.Length == 0) return Fehler("Bitte Benutzername und Passwort eingeben.");
            try
            {
                await web.AufrufAsync(c, HttpMethod.Post, "api/app/anmelden", new { login = benutzer, passwort }, anmeldungNoetig: false);
                return Results.Json(new { ok = true });
            }
            catch (UntPlanWebFehler ex) when (ex.Status is 401 or 403 or 429)
            {
                return Results.Json(new { ok = false, error = ex.Message, retryIn = ex.Status == 429 ? 300 : (int?)null }, statusCode: ex.Status == 403 ? 401 : ex.Status);
            }
        });

        // ---------------------------------------------------------------- Profilwechsel

        // UntPlanWeb kennt nur eine Sitzung je Browser – mehrere Profile auf einem Gerät werden nicht angeboten.
        app.MapMethods("/api/accounts", new[] { "GET", "POST" }, async (HttpContext c) =>
        {
            var p = await Parameter.LesenAsync(c);
            return p.Text("action", "list") == "list"
                ? Results.Json(new { ok = true, accounts = Array.Empty<object>() })
                : Fehler("Mehrere Profile werden nicht unterstützt.");
        });
    }
}
