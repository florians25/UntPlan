using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>Angemeldet sein ist Voraussetzung (wird zu HTTP 401).</summary>
public sealed class NichtAngemeldetException() : Exception("Bitte anmelden.");

public static class AppApi
{
    public sealed record AnmeldeDaten(string Login, string Passwort);
    public sealed record EinrichtungsDaten(string Schulname, string Login, string Passwort);
    public sealed record PasswortDaten(string Alt, string Neu);
    public sealed record AbwesenheitDaten(DateOnly Von, DateOnly Bis, int? StundeVon, int? StundeBis, string? Grund, string? Text);
    public sealed record BuchungDaten(DateOnly Datum, int? StundeVon, int? StundeBis, string Raum, List<string>? Klassen, string? Titel, string? Text);
    public sealed record KlausurDaten(DateOnly Datum, int? StundeVon, int? StundeBis, List<string> Klassen, List<string>? Raeume, List<string>? Aufsicht, string? Titel, string? Text);
    public sealed record KlausurLoeschenDaten(DateOnly Datum, int StundeVon, int StundeBis, List<string>? Klassen, string? Titel);
    public sealed record KlausurVerlegenDaten(KlausurLoeschenDaten Alt, KlausurDaten Neu);
    public sealed record MitteilungDaten(List<string> Empfaenger, string Betreff, string Text, bool Wichtig);
    public sealed record AnwesenheitDaten(DateOnly Datum, int Stunde, int Unterricht, List<AnwesenheitEintrag>? Eintraege);
    public sealed record FehlenDaten(int SchuelerId, DateOnly Von, DateOnly Bis, string? Status, string? Bemerkung);
    public sealed record FehlzeitStatusDaten(List<int> Ids, string Status);
    public sealed record VermerkDaten(DateOnly Datum, string Klasse, int? SchuelerId, string? Art, string Text, string? Zeit = null);
    public sealed record HausaufgabeDaten(DateOnly Datum, int Stunde, int Unterricht, string? Text, DateOnly? Faellig, string? Bemerkung);
    public sealed record HausaufgabeAenderDaten(string? Text, DateOnly? Faellig, string? Bemerkung, bool? Erledigt);
    /// <summary>Abwesenheitsdialog: Zeitraum (Datum + Uhrzeit, ISO „yyyy-MM-ddTHH:mm“), Grund, Text, Status; optional Stunde (Stundendetails) bzw. zu ersetzende Fehlzeiten.</summary>
    public sealed record AbwesenheitZeitraumDaten(List<int>? SchuelerIds, DateTime Von, DateTime Bis, string? Grund, string? Text, string? Status,
        DateOnly? Datum = null, int? Stunde = null, int? Unterricht = null, List<int>? Ersetzen = null,
        string? Nachweis = null, string? EntschuldigtAm = null, bool? ElternInformiert = null, string? NotizIntern = null);
    public sealed record AbwesenheitEntfernenDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds);
    public sealed record VerlaengernDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds, string? Bis);
    public sealed record KlassenbuchDaten(DateOnly Datum, int Stunde, int Unterricht, string? Thema, string? Hausaufgabe, DateOnly? Faellig, string? Notiz, string? NotizSchueler = null,
        string? Lehrform = null, string? Anmerkung = null, string? Lehrer = null, bool? Online = null);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static DateOnly Datum(string? s, Uhr uhr) => DateOnly.TryParse(s, out var d) ? d : uhr.Heute;

    /// <summary>
    /// Darf der Benutzer den Plan dieses Elements sehen? Schüler-, Eltern- und Klassenkonten: eigene Klasse(n) bzw. die Klassen der Kinder
    /// und Räume – nie Pläne von Lehrkräften.
    /// </summary>
    public static void PlanErlaubt(Benutzer b, string typ, string element)
    {
        if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Planart.");
        if (b.Kollegium) return;
        if (typ == Elementtyp.Lehrer) throw new UnauthorizedAccessException("Pläne von Lehrkräften sind für Ihr Konto nicht freigegeben.");
        if (typ == Elementtyp.Raum) return;
        if (typ == Elementtyp.Klasse && b.HatKlasse(element)) return;
        throw new UnauthorizedAccessException("Dieser Plan ist für Ihr Konto nicht freigegeben.");
    }

    public static void Abbilden(WebApplication app)
    {
        // 401 für nicht angemeldete Aufrufe
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (NichtAngemeldetException)
            {
                if (ctx.Response.HasStarted) throw;
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { fehler = "Bitte anmelden." });
            }
        });
        var api = app.MapGroup("/api/app");

        // ---------------------------------------------------------------- Sitzung und Einrichtung

        api.MapGet("/sitzung", (HttpContext c, Einstellungen e, Benutzerdienst bd, Paketdienst p, Mitteilungsdienst m, Verwaltungsdienst vw) =>
        {
            var b = c.Ich();
            return Results.Json(new
            {
                eingerichtet = !bd.Keiner,
                schule = new { name = e.Schulname.Length > 0 ? e.Schulname : p.Aktuell?.Paket.Schule.Name ?? "", schulId = b?.IstAdmin == true ? e.SchulId : null },
                paket = p.Aktuell is { } s ? new { vorhanden = true, revision = s.Paket.Revision, empfangen = s.Empfangen } : null,
                // Schuljahr für die Schuljahr-Pille in der Navigation
                schuljahr = p.Aktuell is { } sj && sj.Paket.Schule.SchuljahrVon.Length >= 4 ? new { von = sj.Paket.Schule.SchuljahrVon, bis = sj.Paket.Schule.SchuljahrBis } : null,
                benutzer = b is null ? null : new
                {
                    b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Kollegium, admin = b.IstAdmin, nachrichten = b.DarfNachrichten,
                    passwortAendern = b.PasswortAendern, typ = b.Familie ? Elementtyp.Klasse : Elementtyp.Lehrer,
                    // Schüler- und Elternkonten: eigene Seiten (Abwesenheiten, Fehlzeiten, Hausaufgaben); Element = Klasse für „Mein Stundenplan“
                    familie = b.Familie, schuelerIds = b.SchuelerIds, klassen = b.Klassen, element = b.Kollegium ? b.Element : b.KlassenKuerzel ?? "",
                    // Fehlzeitenverwaltung (Abwesenheiten, Fehlzeiten) nur für Klassenleitungen, Schulleitung und Administration
                    fehlzeiten = Klassenbuchdienst.FehlzeitenVerwalten(b, p.Aktuell),
                },
                ungelesen = b is null ? 0 : m.Ungelesen(b.Id),
                // Ansichtseinstellungen der Administration (Wochenende, Uhrzeiten auf Karten, Startseite)
                ansicht = b is null ? null : vw.Einstellung("ansicht"),
            });
        });

        api.MapPost("/anmelden", async (AnmeldeDaten d, HttpContext c, Benutzerdienst bd, Protokoll prot, Verwaltungsdienst vw) =>
        {
            var (ergebnis, b) = bd.Anmelden(d.Login ?? "", d.Passwort ?? "", c.Connection.RemoteIpAddress?.ToString(), c.Request.Headers.UserAgent.ToString());
            switch (ergebnis)
            {
                case Benutzerdienst.Anmeldeergebnis.Ok:
                    await c.AnmeldenAsync(b!);
                    vw.KontoMerken(ErweiterungApi.Geraet(c, true), b!.Id);
                    prot.Schreiben(b!.Login, "Anmeldung");
                    return Results.Json(new { ok = true });
                case Benutzerdienst.Anmeldeergebnis.Gesperrt:
                    return Results.Json(new { fehler = "Dieses Konto ist gesperrt." }, statusCode: 403);
                case Benutzerdienst.Anmeldeergebnis.VoruebergehendGesperrt:
                    return Results.Json(new { fehler = "Zu viele Fehlversuche – bitte in einigen Minuten erneut versuchen." }, statusCode: 429);
                default:
                    await Task.Delay(400);
                    return Results.Json(new { fehler = "Benutzername oder Passwort ist falsch." }, statusCode: 401);
            }
        });

        api.MapPost("/abmelden", async (HttpContext c) =>
        {
            await c.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/einrichten", async (EinrichtungsDaten d, HttpContext c, Benutzerdienst bd, Einstellungen e, Protokoll prot) =>
        {
            if (!bd.Keiner) return Results.Json(new { fehler = "UntPlanWeb ist bereits eingerichtet." }, statusCode: 409);
            if (string.IsNullOrWhiteSpace(d.Schulname)) throw new ArgumentException("Bitte den Namen der Schule eingeben.");
            var b = bd.Anlegen(string.IsNullOrWhiteSpace(d.Login) ? "admin" : d.Login, d.Passwort, Rollen.Admin, "", "Administration");
            e.Schreiben("schulname", d.Schulname.Trim());
            if (e.SchulId.Length == 0) e.Schreiben("schul_id", Sicherheit.Slug(d.Schulname));
            var schluessel = e.ApiSchluesselSetzen();
            await c.AnmeldenAsync(b);
            prot.Schreiben(b.Login, "Einrichtung");
            return Results.Json(new { ok = true, schulId = e.SchulId, apiSchluessel = schluessel });
        });

        api.MapPost("/passwort", (PasswortDaten d, HttpContext c, Benutzerdienst bd, Protokoll prot) =>
        {
            var b = Ich(c);
            if (!bd.PasswortStimmt(b.Id, d.Alt)) throw new ArgumentException("Das bisherige Passwort ist falsch.");
            if (d.Neu == d.Alt) throw new ArgumentException("Das neue Passwort muss sich vom bisherigen unterscheiden.");
            bd.RegelPruefen(d.Neu);
            bd.PasswortSetzen(b.Id, d.Neu);
            prot.Schreiben(b.Login, "Passwort geändert");
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Stammdaten und Pläne

        api.MapGet("/elemente", (HttpContext c, Paketdienst p) =>
        {
            var b = Ich(c);
            var s = p.Aktuell;
            if (s is null) return Results.Json(new { klassen = Array.Empty<object>(), lehrer = Array.Empty<object>(), raeume = Array.Empty<object>(), faecher = Array.Empty<object>(), stunden = Array.Empty<object>(), ferien = Array.Empty<object>() });
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            return Results.Json(new
            {
                klassen = s.Paket.Elemente.Klassen.Where(k => b.Kollegium || b.HatKlasse(k.Kurz))
                    .OrderBy(k => k.Kurz, de).Select(k => new { kurz = k.Kurz, name = k.Name, k.Jahrgang, leitung = k.Klassenleitung }),
                lehrer = b.Kollegium ? s.Paket.Elemente.Lehrer.OrderBy(l => l.Kurz, de).Select(l => new { kurz = l.Kurz, name = string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => x.Length > 0)) }).Cast<object>().ToList() : new List<object>(),
                raeume = s.Paket.Elemente.Raeume.OrderBy(r => r.Kurz, de).Select(r => new { kurz = r.Kurz, name = r.Name }),
                faecher = s.Paket.Elemente.Faecher.Select(f => new { kurz = f.Kurz, name = f.Name, farbe = f.Farbe }),
                stunden = s.Paket.Zeitraster.Stunden,
                // Ferien und Feiertage mit Zeitraum (z. B. für die Ferienfelder in UntPlanMobile)
                ferien = s.Paket.Ferien.Select(f => new { name = f.Name, von = f.Von, bis = f.Bis, feiertag = f.Feiertag }),
            });
        });

        api.MapGet("/plan", (string typ, string element, string? datum, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            PlanErlaubt(b, typ, element);
            var s = Paket(p);
            var w = plan.Wochenplan(s, typ, element, Datum(datum, uhr), b.Kollegium, d => r.Vorgemerkt(d, s));
            // Notizen und fällige Hausaufgaben für die Symbole auf den Karten
            return Results.Json(w with { Karten = kb.Anreichern(w.Karten, b.Kollegium) });
        });

        api.MapGet("/tag", (string typ, string? datum, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Uhr uhr) =>
        {
            var b = Ich(c);
            if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Ansicht.");
            if (!b.Kollegium) throw new UnauthorizedAccessException("Die Tagesübersicht steht nur dem Kollegium zur Verfügung.");
            var st = Paket(p);
            return Results.Json(plan.Tag(st, typ, Datum(datum, uhr), b.Kollegium, d => r.Vorgemerkt(d, st)));
        });

        api.MapGet("/vertretungen", (string? datum, int? tage, HttpContext c, Paketdienst p, Vertretungsplandienst vp, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var tageListe = vp.NaechsteTage(s, Datum(datum, uhr), Math.Clamp(tage ?? 1, 1, 7));
            var res = tageListe.Select(d => vp.Tag(s, d, b.Kollegium)).ToList();
            // Klassenkonten: nur die eigene Klasse
            if (!b.Kollegium)
                res = res.Select(t => t with { Zeilen = t.Zeilen.Where(z => z.Klassen.Split(", ").Any(b.HatKlasse)).ToList() }).ToList();
            return Results.Json(new { tage = res });
        });

        // ---------------------------------------------------------------- Startseite

        api.MapGet("/heute", (HttpContext c, Paketdienst p, Plandienst plan, Vertretungsplandienst vp, Rueckmeldungsdienst r, Mitteilungsdienst m, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = p.Aktuell;
            if (s is null) return Results.Json(new { paket = false, ungelesen = m.Ungelesen(b.Id) });
            var heute = uhr.Heute;
            var tag = vp.NaechsteTage(s, heute, 1).FirstOrDefault(heute);
            var typ = b.KlassenKuerzel != null ? Elementtyp.Klasse : Elementtyp.Lehrer;
            var element = b.KlassenKuerzel ?? b.LehrerKuerzel;
            var karten = element is null ? new List<Karte>() : kb.Anreichern(plan.Karten(s, tag, typ, element, r.Vorgemerkt(tag, s)), b.Kollegium);
            var aufsichten = element is not null && typ == Elementtyp.Lehrer ? plan.Aufsichten(s, tag, element) : new List<Aufsicht>();
            var termine = s.Paket.Termine.Where(t => DateOnly.TryParse(t.Von, out var v) && v >= heute && v <= heute.AddDays(14) &&
                    (element is null || (typ == Elementtyp.Klasse ? t.Klassen.Contains(element, StringComparer.OrdinalIgnoreCase) : t.Lehrer.Contains(element, StringComparer.OrdinalIgnoreCase))))
                .OrderBy(t => t.Von).Take(8).Select(t => new { t.Art, t.Titel, t.Von, t.Bis, t.StundeVon, t.StundeBis, klassen = t.Klassen });
            return Results.Json(new
            {
                paket = true,
                datum = Plandienst.Iso(tag),
                wochentag = Plandienst.Wochentage[Plandienst.TagIndex(tag)],
                woche = plan.Woche(s, tag),
                jetzt = uhr.Jetzt.ToString("HH:mm"),
                element,
                typ,
                karten,
                aufsichten,
                hinweise = element is null ? new List<string>() : plan.Hinweise(s, tag, typ, element, b.Kollegium),
                ereignisse = plan.Ereignisse(s, tag, typ, element ?? ""),
                nachrichten = vp.NachrichtenAm(s, tag, b.Kollegium, "startseite"),
                termine,
                aenderungen = karten.Count(k => k.Status is not ("regulaer" or "bereitschaft" or "klausur" or "buchung" or "vorgemerkt")),
                meldungen = r.VonBenutzer(b.Id).Take(5),
                ungelesen = m.Ungelesen(b.Id),
                offeneStunden = b.LehrerKuerzel is string l ? kb.OffeneStunden(s, l).Count : 0,
                hausaufgaben = b.KlassenKuerzel is string k ? kb.Hausaufgaben(k).Take(10).ToList() : new List<Klassenbucheintrag>(),
            });
        });

        // ---------------------------------------------------------------- Meldungen an UntPlan

        api.MapGet("/meldungen", (HttpContext c, Rueckmeldungsdienst r) => Results.Json(r.VonBenutzer(Ich(c).Id)));

        api.MapPost("/meldungen/abwesenheit", (AbwesenheitDaten d, HttpContext c, Rueckmeldungsdienst r, Protokoll prot) =>
        {
            var b = Ich(c);
            var m = r.Abwesenheit(b, d.Von, d.Bis, d.StundeVon, d.StundeBis, d.Grund ?? "", d.Text ?? "");
            prot.Schreiben(b.Login, "Abwesenheit gemeldet", $"{m.Von} – {m.Bis}");
            return Results.Json(m);
        });

        api.MapGet("/meldungen/freieraeume", (DateOnly datum, int von, int bis, int? unterricht, HttpContext c, Paketdienst p, Rueckmeldungsdienst r) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
            return Results.Json(r.FreieRaeume(Paket(p), datum, Math.Max(1, von), Math.Max(von, bis), unterricht));
        });

        api.MapPost("/meldungen/raumbuchung", (BuchungDaten d, HttpContext c, Paketdienst p, Rueckmeldungsdienst r, Protokoll prot) =>
        {
            var b = Ich(c);
            var m = r.Raumbuchung(Paket(p), b, d.Datum, d.StundeVon, d.StundeBis, d.Raum ?? "", d.Klassen ?? new(), d.Titel ?? "", d.Text ?? "");
            prot.Schreiben(b.Login, "Raum gebucht", $"{m.Raum} {m.Von} {m.StundeVon}.–{m.StundeBis}.");
            return Results.Json(m);
        });

        api.MapPost("/meldungen/klausur", (KlausurDaten d, HttpContext c, Paketdienst p, Rueckmeldungsdienst r, Protokoll prot, Verwaltungsdienst vw, Listendienst l) =>
        {
            var b = Ich(c);
            ErweiterungApi.PruefungssperrePruefen(vw, l, r, Paket(p), b, d.Datum, d.Klassen ?? new());
            var m = r.Klausur(Paket(p), b, d.Datum, d.StundeVon, d.StundeBis, d.Klassen ?? new(), d.Raeume ?? new(), d.Aufsicht ?? new(), d.Titel ?? "", d.Text ?? "");
            prot.Schreiben(b.Login, "Klausur eingetragen", $"{string.Join(",", m.Klassen)} {m.Von}");
            return Results.Json(m);
        });

        api.MapPost("/meldungen/klausur/loeschen", (KlausurLoeschenDaten d, HttpContext c, Paketdienst p, Rueckmeldungsdienst r, Protokoll prot) =>
        {
            var b = Ich(c);
            var m = r.KlausurLoeschen(Paket(p), b, d.Datum, d.StundeVon, d.StundeBis, d.Klassen ?? new(), d.Titel);
            prot.Schreiben(b.Login, "Prüfung gelöscht", $"{string.Join(",", d.Klassen ?? new())} {d.Datum:yyyy-MM-dd} {d.StundeVon}.–{d.StundeBis}.");
            return Results.Json(new { ok = true, meldung = m?.Id });
        });
        // Verlegen = löschen und neu eintragen (eine neue Prüfung wird sofort als vorgemerkt angezeigt)
        api.MapPost("/meldungen/klausur/verlegen", (KlausurVerlegenDaten d, HttpContext c, Paketdienst p, Rueckmeldungsdienst r, Protokoll prot, Verwaltungsdienst vw, Listendienst l) =>
        {
            var b = Ich(c);
            var neu = d.Neu;
            ErweiterungApi.PruefungssperrePruefen(vw, l, r, Paket(p), b, neu.Datum, neu.Klassen ?? new());
            r.KlausurLoeschen(Paket(p), b, d.Alt.Datum, d.Alt.StundeVon, d.Alt.StundeBis, d.Alt.Klassen ?? new(), d.Alt.Titel);
            var m = r.Klausur(Paket(p), b, neu.Datum, neu.StundeVon, neu.StundeBis, neu.Klassen ?? new(), neu.Raeume ?? new(), neu.Aufsicht ?? new(), neu.Titel ?? "", neu.Text ?? "");
            prot.Schreiben(b.Login, "Prüfung verlegt", $"{d.Alt.Datum:yyyy-MM-dd} → {neu.Datum:yyyy-MM-dd}");
            return Results.Json(m);
        });

        api.MapDelete("/meldungen/{id}", (string id, HttpContext c, Rueckmeldungsdienst r) =>
            r.Zurueckziehen(id, Ich(c).Id) ? Results.Json(new { ok = true }) : Results.Json(new { fehler = "Die Meldung wurde bereits von UntPlan übernommen." }, statusCode: 409));

        // ---------------------------------------------------------------- Mitteilungen

        api.MapGet("/mitteilungen", (string? ordner, HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            return Results.Json(ordner == "gesendet" ? m.Gesendet(b.Id) : ordner == "entwuerfe" ? m.Entwuerfe(b.Id) : m.Posteingang(b.Id));
        });
        api.MapGet("/mitteilungen/empfaenger", (HttpContext c, Mitteilungsdienst m, Paketdienst p) => Results.Json(m.Empfaengerliste(Ich(c), p.Aktuell)));
        api.MapPost("/mitteilungen", (MitteilungDaten d, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(new { id = m.Senden(Ich(c), p.Aktuell, d.Empfaenger ?? new(), d.Betreff ?? "", d.Text ?? "", d.Wichtig) }));
        api.MapPost("/mitteilungen/{id:int}/gelesen", (int id, HttpContext c, Mitteilungsdienst m) => { m.Gelesen(Ich(c).Id, id); return Results.Json(new { ok = true }); });
        api.MapDelete("/mitteilungen/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) => { m.Loeschen(Ich(c).Id, id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Klassenbuch

        api.MapGet("/klassenbuch/stunde", (DateOnly datum, int stunde, int unterricht, string? lehrer, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            // die nächstliegende Stunde (Blockbeginn) zuerst – bei Kopplungen mit Einzel- und Doppelstunden nicht den Eintrag einer anderen Lehrkraft
            var eintrag = kb.Zeitraum(datum, datum).Where(e => e.Unterricht == unterricht && stunde >= e.Stunde && (lehrer is null || e.Lehrer.Equals(lehrer, StringComparison.OrdinalIgnoreCase)) &&
                                                                         (b.Kollegium || e.Klassen.Any(b.HatKlasse)))
                .OrderByDescending(e => e.Stunde).FirstOrDefault();
            if (eintrag != null && !b.Kollegium) eintrag = eintrag with { Notiz = "" };
            return Results.Json(new { eintrag, darf = b.Kollegium && kb.DarfSchreiben(b, s, datum, stunde, unterricht) });
        });
        // Lehrstoff, Hausaufgabe, Notizen (Lehrkräfte / Schüler*innen), Lehrform, Anmerkung einer Stunde speichern – PUT und POST (Apps).
        // Nicht mitgeschickte Felder (null) bleiben unverändert, z. B. { datum, stunde, unterricht, notizSchueler } ändert nur die Schülernotiz.
        IResult StundeSpeichern(KlassenbuchDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Klassenbuchverwaltung kv)
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur Lehrkräfte können das Klassenbuch bearbeiten.");
            var eigener = b.LehrerKuerzel ?? d.Lehrer;
            var alt = kb.Zeitraum(d.Datum, d.Datum).Where(e => e.Unterricht == d.Unterricht && d.Stunde >= e.Stunde && (eigener is null || e.Lehrer.Equals(eigener, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.Stunde).FirstOrDefault();
            DateOnly? faellig = d.Hausaufgabe is null && d.Faellig is null ? (DateOnly.TryParse(alt?.Faellig, out var f) ? f : null) : d.Faellig;
            var e = kb.Speichern(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Thema ?? alt?.Thema ?? "", d.Hausaufgabe ?? alt?.Hausaufgabe ?? "", faellig,
                d.Notiz ?? alt?.Notiz ?? "", d.NotizSchueler, d.Lehrform, d.Anmerkung, d.Lehrer);
            // „Findet online statt“ (nur wenn mitgeschickt)
            if (d.Online is bool online)
            {
                kv.OnlineSetzen(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, online);
                e = kb.Holen(DateOnly.Parse(e.Datum), e.Stunde, e.Unterricht, e.Lehrer) ?? e;
            }
            return Results.Json(e);
        }
        api.MapPut("/klassenbuch/stunde", StundeSpeichern);
        api.MapPost("/klassenbuch/stunde", StundeSpeichern);
        // Hausaufgaben einer Stunde (Liste wie im Original) und die in dieser Stunde fälligen Hausaufgaben
        api.MapGet("/klassenbuch/hausaufgabenliste", (DateOnly datum, int stunde, int unterricht, int? bis, string? fach, string? klassen, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var liste = kb.HausaufgabenDerStunde(datum, stunde, Math.Max(stunde, bis ?? stunde), unterricht);
            var kl = (klassen ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!b.Kollegium) { kl = kl.Where(b.HatKlasse).ToList(); if (!liste.All(h => h.Klassen.Any(b.HatKlasse))) liste = new(); }
            var faellig = string.IsNullOrEmpty(fach) || kl.Count == 0 ? new List<Hausaufgabe>() : kb.FaelligeHausaufgaben(datum, fach, kl, unterricht);
            return Results.Json(new { liste, faellig, darf = b.Kollegium && kb.DarfSchreiben(b, s, datum, stunde, unterricht) });
        });
        api.MapPost("/klassenbuch/hausaufgabenliste", (HausaufgabeDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.HausaufgabeAnlegen(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Text ?? "", d.Faellig, d.Bemerkung ?? "")));
        api.MapPut("/klassenbuch/hausaufgabenliste/{id:int}", (int id, HausaufgabeAenderDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.HausaufgabeAendern(Ich(c), Paket(p), id, d.Text, d.Faellig, d.Bemerkung, d.Erledigt)));
        api.MapDelete("/klassenbuch/hausaufgabenliste/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            kb.HausaufgabeLoeschen(Ich(c), Paket(p), id);
            return Results.Json(new { ok = true });
        });
        // Abwesenheiten verlängern (aus der Vorstunde bis Stundenende bzw. bis zum Tagesbeginn)
        api.MapPost("/klassenbuch/abwesenheit/verlaengern", (VerlaengernDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { stunden = kb.AbwesenheitVerlaengern(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.SchuelerIds ?? new(), d.Bis == "tagesbeginn" ? "tagesbeginn" : "stunde") }));
        // Abwesenheitsdialog (minutengenau von–bis; Verspätung = ab Stundenbeginn bis Ankunft, vorzeitig = ab Uhrzeit bis Stundenende)
        api.MapPost("/klassenbuch/abwesenheit", (AbwesenheitZeitraumDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            var s = Paket(p);
            var ids = d.SchuelerIds ?? new();
            if (ids.Count == 0) throw new ArgumentException("Bitte eine Schülerin bzw. einen Schüler wählen.");
            (DateOnly, int, int)? stunde = d.Datum is DateOnly dt && d.Stunde is int st && d.Unterricht is int u ? (dt, st, u) : null;
            var n = ids.Distinct().Sum(id => kb.AbwesenheitErfassen(b, s, id, d.Von, d.Bis, d.Grund, d.Text, d.Status, stunde, ids.Count == 1 ? d.Ersetzen : null,
                d.Nachweis, d.EntschuldigtAm, d.ElternInformiert, d.NotizIntern));
            return Results.Json(new { stunden = n });
        });
        api.MapPost("/klassenbuch/abwesenheit/loeschen", (AbwesenheitEntfernenDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(new { geloescht = kb.AbwesenheitEntfernen(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.SchuelerIds ?? new()) });
        });
        api.MapGet("/klassenbuch/klasse", (string klasse, string? von, string? bis, HttpContext c, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            PlanErlaubt(b, Elementtyp.Klasse, klasse);
            var liste = kb.Klasse(klasse, DateOnly.TryParse(von, out var v) ? v : uhr.Heute.AddDays(-28), DateOnly.TryParse(bis, out var bi) ? bi : uhr.Heute.AddDays(14));
            return Results.Json(b.Kollegium ? liste : liste.Select(e => e with { Notiz = "" }).ToList());
        });
        api.MapGet("/klassenbuch/hausaufgaben", (string klasse, HttpContext c, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            PlanErlaubt(b, Elementtyp.Klasse, klasse);
            return Results.Json(kb.Hausaufgaben(klasse).Select(e => e with { Notiz = "" }));
        });
        api.MapGet("/klassenbuch/offen", (int? tage, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            return Results.Json(b.LehrerKuerzel is string l && p.Aktuell is { } s ? kb.OffeneStunden(s, l, tage ?? 14) : new List<object>());
        });

        api.MapGet("/klassenbuch/woche", (string klasse, string? datum, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            var d = Datum(datum, uhr);
            return Results.Json(kb.Woche(Paket(p), klasse, d.AddDays(-(((int)d.DayOfWeek + 6) % 7))));
        });
        api.MapGet("/klassenbuch/anwesenheit", (DateOnly datum, int stunde, int unterricht, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(kb.Anwesenheit(b, Paket(p), datum, stunde, unterricht));
        });
        api.MapPut("/klassenbuch/anwesenheit", (AnwesenheitDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { fehlend = kb.AnwesenheitSpeichern(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Eintraege ?? new()) }));
        api.MapGet("/klassenbuch/fehlzeiten", (string klasse, string? von, string? bis, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            if (!Klassenbuchdienst.IstKlassenleitung(b, Paket(p), klasse)) throw new UnauthorizedAccessException("Die Fehlzeiten einer Klasse sieht nur die Klassenleitung.");
            return Results.Json(kb.Fehlzeitenuebersicht(Paket(p), b, klasse, DateOnly.TryParse(von, out var v) ? v : uhr.Heute.AddDays(-90), DateOnly.TryParse(bis, out var bi) ? bi : uhr.Heute));
        });
        api.MapPost("/klassenbuch/fehlzeiten/tage", (FehlenDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { stunden = kb.FehlenEintragen(Ich(c), Paket(p), d.SchuelerId, d.Von, d.Bis, d.Status ?? "offen", d.Bemerkung ?? "") }));
        api.MapPost("/klassenbuch/fehlzeiten/status", (FehlzeitStatusDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { geaendert = kb.StatusSetzen(Ich(c), Paket(p), d.Ids ?? new(), d.Status ?? "offen") }));
        api.MapDelete("/klassenbuch/fehlzeiten/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { ok = kb.FehlzeitLoeschen(Ich(c), Paket(p), id) }));
        api.MapGet("/klassenbuch/vermerke", (string klasse, string? von, string? bis, HttpContext c, Klassenbuchdienst kb, Uhr uhr) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(new { arten = Klassenbuchdienst.VermerkArten, vermerke = kb.Vermerke(klasse, DateOnly.TryParse(von, out var v) ? v : uhr.Heute.AddDays(-90), DateOnly.TryParse(bis, out var bi) ? bi : uhr.Heute.AddDays(7)) });
        });
        api.MapPost("/klassenbuch/vermerke", (VermerkDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.VermerkAnlegen(Ich(c), Paket(p), d.Datum, d.Klasse ?? "", d.SchuelerId, d.Art ?? "Vermerk", d.Text ?? "", d.Zeit)));
        api.MapDelete("/klassenbuch/vermerke/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { ok = kb.VermerkLoeschen(Ich(c), Paket(p), id) }));
        api.MapGet("/klassenbuch/schueler", (string klasse, HttpContext c, Paketdienst p) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(Klassenbuchdienst.Schueler(Paket(p), new[] { klasse }));
        });

        // ---------------------------------------------------------------- Listen wie im Vorbild (Unterricht, Prüfungen, Hausaufgaben, Einträge, Abwesenheiten, Berichte)

        DateOnly Von(string? s, DateOnly vorgabe) => DateOnly.TryParse(s, out var d) ? d : vorgabe;
        static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static void NurKollegium(Benutzer b) { if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium."); }

        api.MapGet("/unterricht", (string? lehrer, string? klasse, string? datum, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var le = Leer(lehrer) ?? (Leer(klasse) is null ? b.LehrerKuerzel : null);
            return Results.Json(l.Unterrichte(Paket(p), Datum(datum, uhr), le, Leer(klasse)));
        });

        api.MapGet("/pruefungen", (string? von, string? bis, string? klasse, string? lehrer, HttpContext c, Paketdienst p, Listendienst l, Rueckmeldungsdienst r, Uhr uhr) =>
        {
            var b = Ich(c);
            // Klassenkonten: nur die eigene Klasse, keine Filter nach Lehrkräften
            var k = b.Kollegium ? Leer(klasse) : Leer(klasse) is string w && b.HatKlasse(w) ? w : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
            return Results.Json(l.Pruefungen(Paket(p), r.OffeneKlausuren(), Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(120)), k, b.Kollegium ? Leer(lehrer) : null));
        });

        api.MapGet("/hausaufgaben", (string? von, string? bis, string? klasse, string? lehrer, HttpContext c, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            var k = b.Kollegium ? Leer(klasse) : Leer(klasse) is string w && b.HatKlasse(w) ? w : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
            var liste = l.Hausaufgaben(Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(60)), k, b.Kollegium ? Leer(lehrer) : null);
            return Results.Json(b.Kollegium ? liste : liste.Select(e => e with { Notiz = "" }).ToList());
        });

        api.MapGet("/klassenbucheintraege", (string? von, string? bis, string? klasse, string? art, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            return Results.Json(new { arten = Klassenbuchdienst.VermerkArten, eintraege = l.Vermerke(Paket(p), Von(von, uhr.Heute.AddDays(-30)), Von(bis, uhr.Heute), Leer(klasse), Leer(art)) });
        });

        api.MapGet("/abwesenheiten", (string? von, string? bis, string? klasse, string? status, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var st = Paket(p);
            if (!Klassenbuchdienst.FehlzeitenVerwalten(b, st)) throw new UnauthorizedAccessException("Die Abwesenheitsverwaltung steht den Klassenleitungen zur Verfügung.");
            // Klassenleitungen sehen die Abwesenheiten ihrer eigenen Klassen
            var zeilen = l.Abwesenheiten(st, b, Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute), Leer(klasse), Leer(status));
            return Results.Json(b.Rolle is Rollen.Admin or Rollen.Leitung ? zeilen : zeilen.Where(z => z.Verwalten).ToList());
        });

        // Abwesenheiten als Zeiträume (von–bis) und Fehlzeiten je Unterrichtsstunde (Klassenleitung, Schulleitung, Administration)
        api.MapGet("/abwesenheiten/zeitraeume", (string? von, string? bis, string? klasse, string? status, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var st = Paket(p);
            if (!Klassenbuchdienst.FehlzeitenVerwalten(b, st)) throw new UnauthorizedAccessException("Die Abwesenheitsverwaltung steht den Klassenleitungen zur Verfügung.");
            var liste = kb.FehlzeitenZeitraum(Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute), Leer(klasse))
                .Where(f => Klassenbuchdienst.IstKlassenleitung(b, st, f.Klasse)).ToList();
            var zeilen = kb.Zeitraeume(st, liste, k => Klassenbuchdienst.IstKlassenleitung(b, st, k));
            return Results.Json(Leer(status) is string s2 ? zeilen.Where(z => z.Status == s2).ToList() : zeilen);
        });
        api.MapGet("/abwesenheiten/fehlzeiten", (string? von, string? bis, string? klasse, int? schueler, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var st = Paket(p);
            if (!Klassenbuchdienst.FehlzeitenVerwalten(b, st)) throw new UnauthorizedAccessException("Die Fehlzeiten stehen den Klassenleitungen zur Verfügung.");
            var liste = kb.FehlzeitenZeitraum(Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute), Leer(klasse))
                .Where(f => Klassenbuchdienst.IstKlassenleitung(b, st, f.Klasse) && (schueler is null || f.SchuelerId == schueler)).ToList();
            return Results.Json(kb.Fehlzeilen(st, liste));
        });

        api.MapGet("/berichte/fehlzeiten", (string? von, string? bis, string? klasse, string? format, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var st = Paket(p);
            if (!Klassenbuchdienst.FehlzeitenVerwalten(b, st)) throw new UnauthorizedAccessException("Fehlzeitenberichte stehen den Klassenleitungen zur Verfügung.");
            var v = Von(von, uhr.Heute.AddDays(-90));
            var bi = Von(bis, uhr.Heute);
            var liste = l.Fehlzeitenbericht(st, v, bi, Leer(klasse))
                .Where(z => b.Rolle is Rollen.Admin or Rollen.Leitung || Klassenbuchdienst.IstKlassenleitung(b, st, z.Klasse)).ToList();
            return format == "csv"
                ? Results.File(Listendienst.FehlzeitenCsv(liste), "text/csv; charset=utf-8", $"fehlzeiten-{Leer(klasse) ?? "alle"}-{v:yyyy-MM-dd}-{bi:yyyy-MM-dd}.csv")
                : Results.Json(new { von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = liste });
        });

        api.MapGet("/berichte/lehrstoff", (string klasse, string? von, string? bis, string? format, HttpContext c, Listendienst l, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var v = Von(von, uhr.Heute.AddDays(-28));
            var bi = Von(bis, uhr.Heute);
            var liste = kb.Klasse(klasse ?? "", v, bi).OrderBy(e => e.Datum).ThenBy(e => e.Stunde).ToList();
            return format == "csv"
                ? Results.File(Listendienst.LehrstoffCsv(liste), "text/csv; charset=utf-8", $"lehrstoff-{klasse}-{v:yyyy-MM-dd}-{bi:yyyy-MM-dd}.csv")
                : Results.Json(new { von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = liste });
        });

        // ---------------------------------------------------------------- Profil, Kalender, Monitor

        api.MapGet("/profil", (HttpContext c) =>
        {
            var b = Ich(c);
            var basis = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";
            return Results.Json(new
            {
                b.Login, b.Anzeigename, b.Email, rolle = Rollen.Name(b.Rolle), b.Element, b.LetzteAnmeldung, ical = $"{basis}/ical/{b.IcalToken}.ics",
                b.VorherigeAnmeldung, b.Sprache, b.Benachrichtigung, b.Kollegium,
            });
        });
        api.MapPost("/profil/ical", (HttpContext c, Benutzerdienst bd) =>
        {
            var b = Ich(c);
            var t = bd.IcalNeu(b.Id);
            return Results.Json(new { ical = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}/ical/{t}.ics" });
        });

        app.MapGet("/api/monitor/{token}", (string token, Monitordienst md, Paketdienst p, Vertretungsplandienst vp, Einstellungen e, Uhr uhr, Verwaltungsdienst vw) =>
        {
            if (md.NachToken(token) is not { } mon) return Results.NotFound();
            var s = p.Aktuell;
            var lehrer = mon.Version == "lehrer";
            return Results.Json(new
            {
                name = mon.Name,
                schule = e.Schulname,
                version = mon.Version,
                // Monitoransichten (Administration): Ticker, Kopfzeile, Aktualisierungsintervall
                anzeige = vw.Einstellung("monitor"),
                jetzt = uhr.Jetzt.ToString("yyyy-MM-ddTHH:mm:ss"),
                tage = s is null ? new List<Vertretungstag>() : vp.NaechsteTage(s, uhr.Heute, mon.Tage).Select(d => vp.Tag(s, d, lehrer, vw.Wahr("monitor", "nurOeffentlich") ? "monitor-oeffentlich" : "monitor")).ToList(),
            });
        });

        app.MapGet("/ical/{datei}", (string datei, Benutzerdienst bd, Paketdienst p, Plandienst plan, Einstellungen e, Uhr uhr) =>
        {
            var token = datei.EndsWith(".ics") ? datei[..^4] : datei;
            if (bd.NachIcal(token) is not { } b || p.Aktuell is not { } s) return Results.NotFound();
            var typ = b.KlassenKuerzel != null ? Elementtyp.Klasse : Elementtyp.Lehrer;
            var element = b.KlassenKuerzel ?? b.LehrerKuerzel;
            return Results.Text(Kalender(s, plan, e.Schulname, typ, element, uhr.Heute), "text/calendar; charset=utf-8");
        });
    }

    /// <summary>iCalendar mit den Stunden der nächsten vier Wochen (Entfälle als abgesagt).</summary>
    private static string Kalender(Stand s, Plandienst plan, string schule, string typ, string? element, DateOnly heute)
    {
        var sb = new StringBuilder();
        void Z(string zeile) => sb.Append(zeile).Append("\r\n");
        static string T(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        Z("BEGIN:VCALENDAR");
        Z("VERSION:2.0");
        Z("PRODID:-//UntPlanWeb//DE");
        Z($"X-WR-CALNAME:{T($"Stundenplan {element} – {schule}")}");
        Z("CALSCALE:GREGORIAN");
        if (element != null)
            for (var d = Plandienst.Montag(heute); d < heute.AddDays(28); d = d.AddDays(1))
                foreach (var k in plan.Karten(s, d, typ, element))
                {
                    if (k.Beginn.Length == 0 || k.Ende.Length == 0) continue;
                    var titel = $"{k.Fach} {(typ == Elementtyp.Lehrer ? string.Join(",", k.Klassen.Select(x => x.K)) : string.Join(",", k.Lehrer.Select(x => x.K)))}".Trim();
                    Z("BEGIN:VEVENT");
                    Z($"UID:{k.Datum}-{k.Von}-{k.Unterricht}-{k.Fach}@untplanweb");
                    Z($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                    Z($"DTSTART:{d:yyyyMMdd}T{k.Beginn.Replace(":", "")}00");
                    Z($"DTEND:{d:yyyyMMdd}T{k.Ende.Replace(":", "")}00");
                    Z($"SUMMARY:{T((k.Status == "entfall" ? "Entfall: " : k.Status is "geaendert" or "offen" ? "Änderung: " : "") + titel)}");
                    if (k.Raeume.Count > 0) Z($"LOCATION:{T(string.Join(", ", k.Raeume.Select(x => x.K)))}");
                    if (k.Text.Length > 0 || k.Art.Length > 0) Z($"DESCRIPTION:{T(string.Join(" – ", new[] { k.Art, k.Text }.Where(x => x.Length > 0)))}");
                    if (k.Status == "entfall") Z("STATUS:CANCELLED");
                    Z("END:VEVENT");
                }
        Z("END:VCALENDAR");
        return sb.ToString();
    }
}
