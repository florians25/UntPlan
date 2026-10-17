using System.Globalization;
using System.Text.Json;
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

        app.MapMethods("/api/login", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten, Kontenspeicher speicher) =>
        {
            var p = await Parameter.LesenAsync(c);
            var aktion = p.Text("action", HttpMethods.IsGet(c.Request.Method) ? "status" : "login");
            if (aktion == "status") return Results.Json(new { ok = true, loggedIn = UntPlanWebVerbindung.Sitzung(c) != null });
            if (HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);

            if (aktion == "logout")
            {
                konten.Vergessen(c);
                var geraet = Kontenspeicher.GeraetVon(c, false);
                var eigen = speicher.MitSitzung(geraet, UntPlanWebVerbindung.Sitzung(c));
                if (UntPlanWebVerbindung.Sitzung(c) != null)
                    try { await web.AufrufAsync(c, HttpMethod.Post, "api/app/abmelden", new { }); } catch (Exception ex) when (ex is UntPlanWebFehler or NichtAngemeldetException) { }
                UntPlanWebVerbindung.SitzungLoeschen(c);
                if (geraet != null && eigen != null) speicher.Entfernen(geraet, eigen.Id);
                // Weitere Profile auf dem Gerät bleiben angemeldet: das zuletzt angemeldete übernimmt.
                foreach (var weiter in speicher.Konten(geraet).OrderByDescending(k => k.Angemeldet))
                    if (await WechselnAsync(c, web, speicher, geraet!, weiter)) return Results.Json(new { ok = true, switched = true });
                return Results.Json(new { ok = true });
            }
            if (aktion != "login") return Fehler("unknown_action");

            var benutzer = p["username"].Trim();
            var passwort = p["password"];
            if (benutzer.Length == 0 || passwort.Length == 0) return Fehler("Bitte Benutzername und Passwort eingeben.");
            // Weiteres Profil: das bisherige bleibt auf dem Gerät hinterlegt (auch wenn es noch aus der Zeit vor den Profilen stammt).
            if (UntPlanWebVerbindung.Sitzung(c) is { } bisher && speicher.MitSitzung(Kontenspeicher.GeraetVon(c, false), bisher) is null)
                await ProfilMerkenAsync(c, web, speicher);
            try
            {
                await web.AufrufAsync(c, HttpMethod.Post, "api/app/anmelden", new { login = benutzer, passwort }, anmeldungNoetig: false);
                await ProfilMerkenAsync(c, web, speicher);
                return Results.Json(new { ok = true });
            }
            catch (UntPlanWebFehler ex) when (ex.Status is 401 or 403 or 429)
            {
                return Results.Json(new { ok = false, error = ex.Message, retryIn = ex.Status == 429 ? 300 : (int?)null }, statusCode: ex.Status == 403 ? 401 : ex.Status);
            }
        });

        // ---------------------------------------------------------------- Profilwechsel

        // Mehrere Profile je Gerät: Die Sitzungen liegen im Kontenspeicher, der Wechsel setzt das Sitzungscookie um – ohne Abmelden.
        app.MapMethods("/api/accounts", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten, Kontenspeicher speicher) =>
        {
            var p = await Parameter.LesenAsync(c);
            var aktion = p.Text("action", "list");
            if (aktion != "list" && HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);
            var aktuell = UntPlanWebVerbindung.Sitzung(c);
            var geraet = Kontenspeicher.GeraetVon(c, aktuell != null);
            switch (aktion)
            {
                case "list":
                {
                    if (aktuell != null && speicher.MitSitzung(geraet, aktuell) is null) { await ProfilMerkenAsync(c, web, speicher); geraet = Kontenspeicher.GeraetVon(c, false); }
                    aktuell = UntPlanWebVerbindung.Sitzung(c);
                    var ich = speicher.MitSitzung(geraet, aktuell);
                    return Results.Json(new
                    {
                        ok = true,
                        accounts = speicher.Konten(geraet).OrderBy(k => k.Angemeldet).Select(k => new
                        {
                            token = k.Id, label = k.Name, info = string.Join(" · ", new[] { k.Rollenname, k.Login }.Where(x => x.Length > 0)),
                            current = ich?.Id == k.Id, push = k.Push,
                        }),
                    });
                }
                case "switch":
                {
                    var ziel = speicher.Finden(geraet, p["token"]);
                    if (geraet is null || ziel is null) return Fehler("Dieses Profil ist auf dem Gerät nicht mehr angemeldet.", 404);
                    konten.Vergessen(c);
                    if (!await WechselnAsync(c, web, speicher, geraet, ziel))
                        return Fehler("Die Anmeldung dieses Profils ist abgelaufen. Bitte erneut anmelden.", 409);
                    return Results.Json(new { ok = true });
                }
                case "remove":
                {
                    var ziel = speicher.Finden(geraet, p["token"]);
                    if (geraet is null || ziel is null) return Results.Json(new { ok = true });
                    var warAktuell = ziel.SitzungHash == speicher.MitSitzung(geraet, aktuell)?.SitzungHash;
                    speicher.Entfernen(geraet, ziel.Id);
                    if (!warAktuell) return Results.Json(new { ok = true });
                    konten.Vergessen(c);
                    try { await web.AufrufAsync(c, HttpMethod.Post, "api/app/abmelden", new { }); } catch (Exception ex) when (ex is UntPlanWebFehler or NichtAngemeldetException) { }
                    UntPlanWebVerbindung.SitzungLoeschen(c);
                    foreach (var weiter in speicher.Konten(geraet).OrderByDescending(k => k.Angemeldet))
                        if (await WechselnAsync(c, web, speicher, geraet, weiter)) return Results.Json(new { ok = true, switched = true });
                    return Results.Json(new { ok = true, loggedOut = true });
                }
                default:
                    return Fehler("unknown_action");
            }
        });
    }

    /// <summary>Gerade angemeldetes Konto (Sitzung dieser Anfrage) auf dem Gerät hinterlegen.</summary>
    public static async Task ProfilMerkenAsync(HttpContext c, UntPlanWebVerbindung web, Kontenspeicher speicher)
    {
        var sitzung = UntPlanWebVerbindung.Sitzung(c);
        if (sitzung is null) return;
        try
        {
            var s = await web.AufrufAsync(c, HttpMethod.Get, "api/app/sitzung", anmeldungNoetig: false);
            if (s?["benutzer"] is not JsonObject b) return;
            sitzung = UntPlanWebVerbindung.Sitzung(c) ?? sitzung;
            var ablauf = c.Items[UntPlanWebVerbindung.CookieAblauf] as DateTimeOffset? ?? DateTimeOffset.UtcNow.AddDays(14);
            speicher.Merken(Kontenspeicher.GeraetVon(c, true)!, b.Deserialize<Sitzungsbenutzer>(UntPlanWebVerbindung.Json)!, sitzung, ablauf);
        }
        catch (UntPlanWebFehler) { /* ohne Profilliste weiter */ }
    }

    /// <summary>Auf ein gespeichertes Profil umschalten; prüft vorher bei UntPlanWeb, ob dessen Sitzung noch gilt.</summary>
    private static async Task<bool> WechselnAsync(HttpContext c, UntPlanWebVerbindung web, Kontenspeicher speicher, string geraet, GespeichertesKonto ziel)
    {
        var sitzung = speicher.SitzungKlar(ziel);
        var vorher = c.Items.TryGetValue(UntPlanWebVerbindung.Cookie, out var v) ? v : null;
        if (sitzung != null)
        {
            c.Items[UntPlanWebVerbindung.Cookie] = sitzung;
            try
            {
                var s = await web.AufrufAsync(c, HttpMethod.Get, "api/app/sitzung", anmeldungNoetig: false);
                if (s?["benutzer"] is JsonObject b && b["id"]?.GetValue<int>() == ziel.BenutzerId)
                {
                    // ggf. hat UntPlanWeb die Sitzung dabei erneuert – dann ist das Cookie schon gesetzt
                    var gueltig = UntPlanWebVerbindung.Sitzung(c) ?? sitzung;
                    UntPlanWebVerbindung.SitzungSetzen(c, gueltig, c.Items[UntPlanWebVerbindung.CookieAblauf] as DateTimeOffset? ?? ziel.Ablauf);
                    return true;
                }
            }
            catch (UntPlanWebFehler) { }
        }
        if (vorher is null) c.Items.Remove(UntPlanWebVerbindung.Cookie); else c.Items[UntPlanWebVerbindung.Cookie] = vorher;
        speicher.Entfernen(geraet, ziel.Id);
        return false;
    }
}
