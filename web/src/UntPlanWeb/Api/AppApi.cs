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
    public sealed record MitteilungDaten(List<string> Empfaenger, string Betreff, string Text, bool Wichtig);
    public sealed record AnwesenheitDaten(DateOnly Datum, int Stunde, int Unterricht, List<AnwesenheitEintrag>? Eintraege);
    public sealed record FehlenDaten(int SchuelerId, DateOnly Von, DateOnly Bis, string? Status, string? Bemerkung);
    public sealed record FehlzeitStatusDaten(List<int> Ids, string Status);
    public sealed record VermerkDaten(DateOnly Datum, string Klasse, int? SchuelerId, string? Art, string Text);
    public sealed record HausaufgabeDaten(DateOnly Datum, int Stunde, int Unterricht, string? Text, DateOnly? Faellig, string? Bemerkung);
    public sealed record HausaufgabeAenderDaten(string? Text, DateOnly? Faellig, string? Bemerkung, bool? Erledigt);
    public sealed record VerlaengernDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds, string? Bis);
    public sealed record KlassenbuchDaten(DateOnly Datum, int Stunde, int Unterricht, string? Thema, string? Hausaufgabe, DateOnly? Faellig, string? Notiz, string? NotizSchueler = null);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static DateOnly Datum(string? s, Uhr uhr) => DateOnly.TryParse(s, out var d) ? d : uhr.Heute;

    /// <summary>Darf der Benutzer den Plan dieses Elements sehen? Schüler-/Klassenkonten: eigene Klasse und Räume.</summary>
    private static void PlanErlaubt(Benutzer b, string typ, string element)
    {
        if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Planart.");
        if (b.Kollegium) return;
        if (typ == Elementtyp.Raum) return;
        if (typ == Elementtyp.Klasse && b.KlassenKuerzel is string k && k.Equals(element, StringComparison.OrdinalIgnoreCase)) return;
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
                    b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Element, b.Kollegium, admin = b.IstAdmin, nachrichten = b.DarfNachrichten,
                    passwortAendern = b.PasswortAendern, typ = b.Rolle == Rollen.Schueler ? Elementtyp.Klasse : Elementtyp.Lehrer,
                },
                ungelesen = b is null ? 0 : m.Ungelesen(b.Id),
                // Ansichtseinstellungen der Administration (Wochenende, Uhrzeiten auf Karten, Startseite)
                ansicht = b is null ? null : vw.Einstellung("ansicht"),
            });
        });

        api.MapPost("/anmelden", async (AnmeldeDaten d, HttpContext c, Benutzerdienst bd, Protokoll prot, Verwaltungsdienst vw) =>
        {
            var (ergebnis, b) = bd.Anmelden(d.Login ?? "", d.Passwort ?? "");
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
                klassen = s.Paket.Elemente.Klassen.Where(k => b.Kollegium || string.Equals(k.Kurz, b.KlassenKuerzel, StringComparison.OrdinalIgnoreCase))
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
            var w = plan.Wochenplan(s, typ, element, Datum(datum, uhr), b.Kollegium, r.Vorgemerkt);
            // Notizen und fällige Hausaufgaben für die Symbole auf den Karten
            return Results.Json(w with { Karten = kb.Anreichern(w.Karten, b.Kollegium) });
        });

        api.MapGet("/tag", (string typ, string? datum, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Uhr uhr) =>
        {
            var b = Ich(c);
            if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Ansicht.");
            if (!b.Kollegium) throw new UnauthorizedAccessException("Die Tagesübersicht steht nur dem Kollegium zur Verfügung.");
            return Results.Json(plan.Tag(Paket(p), typ, Datum(datum, uhr), b.Kollegium, r.Vorgemerkt));
        });

        api.MapGet("/vertretungen", (string? datum, int? tage, HttpContext c, Paketdienst p, Vertretungsplandienst vp, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var tageListe = vp.NaechsteTage(s, Datum(datum, uhr), Math.Clamp(tage ?? 1, 1, 7));
            var res = tageListe.Select(d => vp.Tag(s, d, b.Kollegium)).ToList();
            // Klassenkonten: nur die eigene Klasse
            if (b.KlassenKuerzel is string k)
                res = res.Select(t => t with { Zeilen = t.Zeilen.Where(z => z.Klassen.Split(", ").Contains(k, StringComparer.OrdinalIgnoreCase)).ToList() }).ToList();
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
            var karten = element is null ? new List<Karte>() : kb.Anreichern(plan.Karten(s, tag, typ, element, r.Vorgemerkt(tag)), b.Kollegium);
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
                aenderungen = karten.Count(k => k.Status is not ("regulaer" or "bereitschaft")),
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

        api.MapGet("/meldungen/freieraeume", (DateOnly datum, int von, int bis, HttpContext c, Paketdienst p, Rueckmeldungsdienst r) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
            return Results.Json(r.FreieRaeume(Paket(p), datum, Math.Max(1, von), Math.Max(von, bis)));
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
            var eintrag = kb.Zeitraum(datum, datum).FirstOrDefault(e => e.Unterricht == unterricht && stunde >= e.Stunde && (lehrer is null || e.Lehrer.Equals(lehrer, StringComparison.OrdinalIgnoreCase)) &&
                                                                         (b.Kollegium || e.Klassen.Contains(b.KlassenKuerzel ?? "", StringComparer.OrdinalIgnoreCase)));
            if (eintrag != null && !b.Kollegium) eintrag = eintrag with { Notiz = "" };
            return Results.Json(new { eintrag, darf = b.Kollegium && kb.DarfSchreiben(b, s, datum, stunde, unterricht) });
        });
        api.MapPut("/klassenbuch/stunde", (KlassenbuchDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.Speichern(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Thema ?? "", d.Hausaufgabe ?? "", d.Faellig, d.Notiz ?? "", d.NotizSchueler)));
        // Hausaufgaben einer Stunde (Liste wie im Original) und die in dieser Stunde fälligen Hausaufgaben
        api.MapGet("/klassenbuch/hausaufgabenliste", (DateOnly datum, int stunde, int unterricht, int? bis, string? fach, string? klassen, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var liste = kb.HausaufgabenDerStunde(datum, stunde, Math.Max(stunde, bis ?? stunde), unterricht);
            var kl = (klassen ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!b.Kollegium) { kl = kl.Where(k => k.Equals(b.KlassenKuerzel ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); if (!liste.All(h => h.Klassen.Contains(b.KlassenKuerzel ?? "", StringComparer.OrdinalIgnoreCase))) liste = new(); }
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
            Results.Json(kb.VermerkAnlegen(Ich(c), Paket(p), d.Datum, d.Klasse ?? "", d.SchuelerId, d.Art ?? "Vermerk", d.Text ?? "")));
        api.MapDelete("/klassenbuch/vermerke/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { ok = kb.VermerkLoeschen(Ich(c), Paket(p), id) }));
        api.MapGet("/klassenbuch/schueler", (string klasse, HttpContext c, Paketdienst p) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(Klassenbuchdienst.Schueler(Paket(p), new[] { klasse }));
        });

        // ---------------------------------------------------------------- Listen wie in WebUntis (Unterricht, Prüfungen, Hausaufgaben, Einträge, Abwesenheiten, Berichte)

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
            var k = b.Kollegium ? Leer(klasse) : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
            return Results.Json(l.Pruefungen(Paket(p), r.OffeneKlausuren(), Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(120)), k, b.Kollegium ? Leer(lehrer) : null));
        });

        api.MapGet("/hausaufgaben", (string? von, string? bis, string? klasse, string? lehrer, HttpContext c, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            var k = b.Kollegium ? Leer(klasse) : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
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
            return Results.Json(l.Abwesenheiten(Paket(p), b, Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute), Leer(klasse), Leer(status)));
        });

        api.MapGet("/berichte/fehlzeiten", (string? von, string? bis, string? klasse, string? format, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            var v = Von(von, uhr.Heute.AddDays(-90));
            var bi = Von(bis, uhr.Heute);
            var liste = l.Fehlzeitenbericht(Paket(p), v, bi, Leer(klasse));
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
