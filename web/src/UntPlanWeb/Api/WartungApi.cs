using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Wartungsfunktionen der Administration (Administration bzw. Recht „Systemwartung“):
/// <list type="bullet">
/// <item><c>GET /api/app/admin/sitzungen?stunden=</c> – aktive Benutzer (angemeldete Sitzungen mit letzter Aktivität).</item>
/// <item><c>GET|PUT /api/app/admin/zugriff</c> – Zugriff ent-/sperren (Wartungsmodus) mit Hinweis.</item>
/// <item><c>GET /api/app/admin/ueber</c> – Über UntPlan: Version, Datenbank, letzte Übertragungen, Module.</item>
/// <item><c>GET /api/app/zugriff</c> (ohne Anmeldung) – Hinweis für die Anmeldeseite bei gesperrtem Zugriff.</item>
/// </list>
/// Jede Anfrage angemeldeter Benutzer an <c>/api</c> vermerkt die Aktivität der Sitzung; Abmelden entfernt sie.
/// </summary>
public static class WartungApi
{
    public sealed record ZugriffDaten(bool Gesperrt, string? Hinweis);

    private static Benutzer Wartung(HttpContext c) =>
        c.Ich() is { } b ? Systemwartung.Ausgenommen(b) ? b : throw new UnauthorizedAccessException("Nur für die Administration.") : throw new NichtAngemeldetException();

    public static void Abbilden(WebApplication app)
    {
        // Aktivität der Sitzungen (nach der Anmeldung im Ablauf, daher ist der Benutzer bekannt)
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var w = ctx.RequestServices.GetRequiredService<Systemwartung>();
                if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.Equals("/api/app/abmelden", StringComparison.OrdinalIgnoreCase)) w.Entfernen(ctx);
                else w.Merken(ctx, () => ctx.Ich());
            }
            await next();
        });

        var api = app.MapGroup("/api/app");

        api.MapGet("/zugriff", (Systemwartung w) =>
        {
            var z = w.Sperre();
            return Results.Json(new { gesperrt = z.Gesperrt, hinweis = z.Gesperrt ? Systemwartung.Meldung(z) : "" });
        });

        api.MapGet("/admin/sitzungen", (int? stunden, HttpContext c, Systemwartung w, Uhr uhr) =>
        {
            Wartung(c);
            var zeitraum = TimeSpan.FromHours(Math.Clamp(stunden ?? 24, 1, 24 * 14));
            var jetzt = uhr.Jetzt;
            var liste = w.Sitzungen(zeitraum);
            return Results.Json(new
            {
                aktivMinuten = (int)Systemwartung.Aktiv.TotalMinutes,
                stunden = (int)zeitraum.TotalHours,
                gesperrt = w.Sperre().Gesperrt,
                sitzungen = liste.Select(s => new
                {
                    s.BenutzerId, s.Login, s.Anzeigename, s.Rolle, rollenname = Rollen.Name(s.Rolle), s.Adresse, s.Geraet,
                    beginn = s.Beginn.ToString("yyyy-MM-dd HH:mm:ss"), letzte = s.Letzte.ToString("yyyy-MM-dd HH:mm:ss"),
                    aktiv = jetzt - s.Letzte <= Systemwartung.Aktiv, minuten = (int)Math.Max(0, (jetzt - s.Letzte).TotalMinutes),
                }),
            });
        });

        api.MapGet("/admin/zugriff", (HttpContext c, Systemwartung w, Uhr uhr) =>
        {
            Wartung(c);
            var z = w.Sperre();
            var angemeldet = w.Sitzungen(Systemwartung.Aktiv);
            return Results.Json(new
            {
                z.Gesperrt, z.Hinweis, z.Seit, z.Von, standardHinweis = Systemwartung.StandardHinweis,
                aktiv = angemeldet.Count, aktivOhneAdmin = angemeldet.Count(s => s.Rolle != Rollen.Admin),
                aktivMinuten = (int)Systemwartung.Aktiv.TotalMinutes,
            });
        });

        api.MapPut("/admin/zugriff", (ZugriffDaten d, HttpContext c, Systemwartung w, Protokoll prot) =>
        {
            var b = Wartung(c);
            var z = w.SperreSetzen(b, d.Gesperrt, d.Hinweis);
            prot.Schreiben(b.Login, z.Gesperrt ? "Zugriff gesperrt" : "Zugriff freigegeben", z.Hinweis);
            return Results.Json(new { z.Gesperrt, z.Hinweis, z.Seit, z.Von });
        });

        api.MapGet("/admin/ueber", (HttpContext c, Systemwartung w, Paketdienst p, Db db, Einstellungen e, Konfiguration k, Pushdienst push) =>
        {
            Wartung(c);
            var s = p.Aktuell;
            var module = new List<object>
            {
                new { name = "Stundenplan und Vertretungsplan", aktiv = true, hinweis = "Pläne aus dem Desktop-Programm" },
                new { name = "Klassenbuch", aktiv = true, hinweis = "Lehrstoff, Hausaufgaben, Abwesenheiten, Klassendienste" },
                new { name = "Unterricht Klassen und Schülergruppen", aktiv = true, hinweis = "Abmeldungen, befristeter Unterrichtswechsel" },
                new { name = "Mitteilungen und Infocenter", aktiv = true, hinweis = "" },
                new { name = "Raum- und Ressourcenbuchung", aktiv = true, hinweis = "" },
                new { name = "Sprechtag", aktiv = true, hinweis = "" },
                new { name = "Sitzplan", aktiv = true, hinweis = "" },
                new { name = "Sofort-Benachrichtigungen (iOS)", aktiv = push.Konfiguration().Vollstaendig, hinweis = "APNs-Zugang in der Administration" },
                new { name = "Einmalanmeldung für UntPlanMobile", aktiv = k.MobilAdressen.Count > 0, hinweis = "UNTPLANWEB_MOBILE_URL" },
                new { name = "Schnittstelle zum Desktop-Programm", aktiv = e.Lesen("api_schluessel") is { Length: > 0 }, hinweis = "API-Schlüssel" },
            };
            return Results.Json(new
            {
                produkt = "UntPlanWeb", version = Updatepruefung.VersionVon(typeof(WartungApi).Assembly),
                laufzeit = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                datenbank = db.Art == Datenbankart.MySql ? "MySQL/MariaDB" : "SQLite", schemastand = db.Version(),
                schule = e.Schulname.Length > 0 ? e.Schulname : s?.Paket.Schule.Name ?? "", schulId = e.SchulId,
                paket = s is null ? null : new
                {
                    s.Paket.Revision, s.Empfangen, s.Paket.Programmversion, schuljahrVon = s.Paket.Schule.SchuljahrVon, schuljahrBis = s.Paket.Schule.SchuljahrBis,
                    klassen = s.Paket.Elemente.Klassen.Count, lehrer = s.Paket.Elemente.Lehrer.Count, schueler = s.Schueler.Count, unterricht = s.Unterricht.Count,
                },
                importe = w.LetzteImporte(),
                module,
            });
        });
    }
}
