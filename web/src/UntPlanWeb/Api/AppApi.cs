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
    public sealed record MitteilungDaten(List<string> Empfaenger, string Betreff, string Text, bool Wichtig, bool Lesebestaetigung = false, int? AntwortAuf = null,
        bool AntwortErlaubt = true);
    // Einzelstunde: optional Stunde eines Blocks, für die der Eintrag allein gilt (ohne Angabe: ganzer Block)
    public sealed record AnwesenheitDaten(DateOnly Datum, int Stunde, int Unterricht, List<AnwesenheitEintrag>? Eintraege, int? Einzelstunde = null);
    public sealed record FehlenDaten(int SchuelerId, DateOnly Von, DateOnly Bis, string? Status, string? Bemerkung);
    public sealed record FehlzeitStatusDaten(List<int> Ids, string Status);
    public sealed record VermerkDaten(DateOnly Datum, string Klasse, int? SchuelerId, string? Art, string Text, string? Zeit = null);
    public sealed record HausaufgabeDaten(DateOnly Datum, int Stunde, int Unterricht, string? Text, DateOnly? Faellig, string? Bemerkung, int? Einzelstunde = null);
    public sealed record HausaufgabeAenderDaten(string? Text, DateOnly? Faellig, string? Bemerkung, bool? Erledigt);
    /// <summary>Abwesenheitsdialog: Zeitraum (Datum + Uhrzeit, ISO „yyyy-MM-ddTHH:mm“), Grund, Text, Status; optional Stunde (Stundendetails) bzw. zu ersetzende Fehlzeiten.</summary>
    public sealed record AbwesenheitZeitraumDaten(List<int>? SchuelerIds, DateTime Von, DateTime Bis, string? Grund, string? Text, string? Status,
        DateOnly? Datum = null, int? Stunde = null, int? Unterricht = null, List<int>? Ersetzen = null,
        string? Nachweis = null, string? EntschuldigtAm = null, bool? ElternInformiert = null, string? NotizIntern = null, int? Einzelstunde = null,
        bool? Ganztaegig = null);
    public sealed record AbwesenheitEntfernenDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds, int? Einzelstunde = null);
    public sealed record AbwesenheitStatusDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds, string? Status, int? Einzelstunde = null);
    public sealed record VerlaengernDaten(DateOnly Datum, int Stunde, int Unterricht, List<int>? SchuelerIds, string? Bis, int? Einzelstunde = null);
    public sealed record KlassenbuchDaten(DateOnly Datum, int Stunde, int Unterricht, string? Thema, string? Hausaufgabe, DateOnly? Faellig, string? Notiz, string? NotizSchueler = null,
        string? Lehrform = null, string? Anmerkung = null, string? Lehrer = null, bool? Online = null, string? OnlineLink = null, int? Einzelstunde = null);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    /// <summary>
    /// Eigene Abwesenheiten der Woche, nur im eigenen Plan: Lehrkraft im eigenen Lehrerplan (Absenzen aus UntPlan, Grund nur wenn
    /// übertragen), Schüler*in bzw. Erziehungsberechtigte im Klassenplan des (gewählten) Kindes (Abwesenheiten aus dem Klassenbuch).
    /// </summary>
    public static List<PlanAbwesenheit> EigeneAbwesenheiten(Benutzer b, Stand s, string typ, string element, DateOnly tag, int? schueler,
        Klassenbuchdienst kb, Klassenbuchverwaltung kv)
    {
        var res = new List<PlanAbwesenheit>();
        var mo = Plandienst.Montag(tag);
        var so = mo.AddDays(6);
        var stunden = s.Paket.Zeitraster.Stunden.Where(z => z.Beginn.Length > 0 && z.Ende.Length > 0).OrderBy(z => z.Nr).ToList();
        if (stunden.Count == 0) return res;
        var gleich = StringComparer.OrdinalIgnoreCase;
        if (b.Kollegium && typ == Elementtyp.Lehrer && gleich.Equals(b.Element, element))
        {
            foreach (var a in s.Paket.Absenzen.Where(x => x.Art == "lehrer" && gleich.Equals(x.Element, element)))
            {
                if (!DateOnly.TryParse(a.Von, out var von) || !DateOnly.TryParse(a.Bis, out var bis)) continue;
                for (var d = von < mo ? mo : von; d <= bis && d <= so; d = d.AddDays(1))
                {
                    var erste = a.StundeVon > 0 ? stunden.FirstOrDefault(z => z.Nr >= a.StundeVon) : stunden[0];
                    var letzte = a.StundeBis >= 20 || a.StundeBis < a.StundeVon || a.StundeBis <= 0 ? stunden[^1] : stunden.LastOrDefault(z => z.Nr <= a.StundeBis);
                    if (erste == null || letzte == null || letzte.Nr < erste.Nr) continue;
                    res.Add(new PlanAbwesenheit(Plandienst.Iso(d), erste.Beginn, letzte.Ende, a.Grund ?? "", "fehlt"));
                }
            }
        }
        else if (b.Familie && typ == Elementtyp.Klasse)
        {
            var kinder = b.SchuelerIds.Select(i => s.Schueler.FirstOrDefault(x => x.Id == i)).Where(x => x != null && gleich.Equals(x.Klasse, element)).ToList();
            var kind = kinder.FirstOrDefault(x => x!.Id == schueler) ?? kinder.FirstOrDefault();
            if (kind == null) return res;
            var namen = kv.Gruende().GroupBy(g => g.Kurz, gleich).ToDictionary(g => g.Key, g => g.First().Name, gleich);
            foreach (var z in kb.Zeitraeume(s, kb.FehlzeitenVon(kind.Id, mo, so), _ => false))
            {
                if (!DateOnly.TryParse(z.VonDatum, out var von) || !DateOnly.TryParse(z.BisDatum, out var bis)) continue;
                for (var d = von; d <= bis; d = d.AddDays(1))
                    res.Add(new PlanAbwesenheit(Plandienst.Iso(d), d == von ? z.VonZeit : stunden[0].Beginn, d == bis ? z.BisZeit : stunden[^1].Ende,
                        namen.GetValueOrDefault(z.Grund, z.Grund), z.Art));
            }
        }
        return res.OrderBy(x => x.Datum, StringComparer.Ordinal).ThenBy(x => x.Von, StringComparer.Ordinal).ToList();
    }

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static DateOnly Datum(string? s, Uhr uhr) => DateOnly.TryParse(s, out var d) ? d : uhr.Heute;

    /// <summary>
    /// Darf der Benutzer den Plan dieses Elements sehen? Schüler-, Eltern- und Klassenkonten: eigene Klasse(n) bzw. die Klassen der Kinder
    /// und Räume – nie Pläne von Lehrkräften.
    /// </summary>
    public static void PlanErlaubt(Benutzer b, string typ, string element)
    {
        if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Planart.");
        // Rechte-Modell: eigener Plan, Pläne aller Klassen, der Lehrkräfte, Raumpläne
        var eigen = Rechtedienst.Darf(b, Recht.PlanEigen) && (typ == Elementtyp.Lehrer
            ? b.LehrerKuerzel is string l && string.Equals(l, element, StringComparison.OrdinalIgnoreCase)
            : typ == Elementtyp.Klasse && b.HatKlasse(element));
        if (eigen) return;
        if (typ == Elementtyp.Lehrer)
        {
            if (Rechtedienst.Darf(b, Recht.PlanLehrer)) return;
            throw new UnauthorizedAccessException("Pläne von Lehrkräften sind für Ihr Konto nicht freigegeben.");
        }
        if (typ == Elementtyp.Raum && Rechtedienst.Darf(b, Recht.PlanRaeume)) return;
        if (typ == Elementtyp.Klasse && Rechtedienst.Darf(b, Recht.PlanKlassen)) return;
        if (typ == Elementtyp.Klasse && AndereKlassen(b)) return;
        throw new UnauthorizedAccessException("Dieser Plan ist für Ihr Konto nicht freigegeben.");
    }

    /// <summary>Darf das Konto die Pläne aller Klassen sehen? Kollegium mit „Pläne aller Klassen“, Schüler/Eltern mit „Andere Klassenpläne“.</summary>
    public static bool AndereKlassen(Benutzer b) =>
        b.Kollegium ? Rechtedienst.Darf(b, Recht.PlanKlassen) : b.Familie && Rechtedienst.Darf(b, Recht.PlanKlassenSchueler);

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

        api.MapGet("/sitzung", (HttpContext c, Einstellungen e, Benutzerdienst bd, Paketdienst p, Mitteilungsdienst m, Verwaltungsdienst vw, Entschuldigungsdienst ed) =>
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
                    b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Kollegium, admin = b.IstAdmin, nachrichten = b.DarfNachrichten, b.Benachrichtigung,
                    passwortAendern = b.PasswortAendern, typ = b.Familie ? Elementtyp.Klasse : Elementtyp.Lehrer,
                    // Schüler- und Elternkonten: eigene Seiten (Abwesenheiten, Fehlzeiten, Hausaufgaben); Element = Klasse für „Mein Stundenplan“
                    familie = b.Familie, schuelerIds = b.SchuelerIds, klassen = b.Klassen, element = b.Kollegium ? b.Element : b.KlassenKuerzel ?? "",
                    // Fehlzeitenverwaltung (Abwesenheiten, Fehlzeiten) nur für Klassenleitungen, Schulleitung und Administration
                    fehlzeiten = Klassenbuchdienst.FehlzeitenVerwalten(b, p.Aktuell),
                    // Fachlehrer-Entschuldigungen (Modul eingeschaltet und Recht „Fehlstunden im eigenen Unterricht entschuldigen“)
                    fachlehrerEntschuldigungen = ed.FachlehrerAktiv(b),
                    // effektive Rechte (Rechte-Modell) für die Oberfläche: Recht → Stufe (1 = Lesen, 2 = Ja/Schreiben)
                    rechte = Rechtedienst.Liste(b),
                    // Planfenster der Rolle: Wochen zurück bzw. voraus (null = unbegrenzt, 0 = nur die aktuelle Woche)
                    planWochenZurueck = b.Planfenster.Zurueck, planWochenVor = b.Planfenster.Vor,
                    // Mitteilungen: Senderecht (Verfassen) und Antwortrecht für das Untermenü (Gesendet, Entwürfe, Verfassen)
                    mitteilungen = new { senden = m.DarfSenden(b), antworten = m.DarfAntworten(b) },
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

        // Datumswähler der Oberfläche: Ferien, Feiertage und freie Tage (dezent markiert) sowie das Schuljahr
        api.MapGet("/kalender", (HttpContext c, Paketdienst p, Verwaltungsdienst vw) =>
        {
            Ich(c);
            var s = p.Aktuell;
            var eigene = vw.FreieTage().Select(f => new { name = f.Name, von = f.Von, bis = f.Bis, art = f.Art });
            var tage = s is null ? eigene
                : s.Paket.Ferien.Select(f => new { name = f.Name, von = f.Von, bis = f.Bis, art = f.Feiertag ? "feiertag" : "ferien" }).Concat(eigene);
            return Results.Json(new
            {
                freieTage = tage.Where(f => f.von.Length == 10 && f.bis.Length == 10).OrderBy(f => f.von, StringComparer.Ordinal).ToList(),
                schuljahr = s is { } sj && sj.Paket.Schule.SchuljahrVon.Length >= 4 ? new { von = sj.Paket.Schule.SchuljahrVon, bis = sj.Paket.Schule.SchuljahrBis } : null,
            });
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
                klassen = s.Paket.Elemente.Klassen.Where(k => b.Kollegium || b.HatKlasse(k.Kurz) || AndereKlassen(b))
                    .OrderBy(k => k.Kurz, de).Select(k => new { kurz = k.Kurz, name = k.Name, k.Jahrgang, leitung = k.Klassenleitung }),
                lehrer = b.Kollegium ? s.Paket.Elemente.Lehrer.OrderBy(l => l.Kurz, de).Select(l => new { kurz = l.Kurz, name = string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => x.Length > 0)) }).Cast<object>().ToList() : new List<object>(),
                raeume = s.Paket.Elemente.Raeume.OrderBy(r => r.Kurz, de).Select(r => new { kurz = r.Kurz, name = r.Name }),
                faecher = s.Paket.Elemente.Faecher.Select(f => new { kurz = f.Kurz, name = f.Name, farbe = f.Farbe }),
                stunden = s.Paket.Zeitraster.Stunden,
                // Pläne anderer Klassen erlaubt (Kollegium bzw. Recht „Andere Klassenpläne“ bei Schüler- und Elternkonten)
                andereKlassen = AndereKlassen(b),
                // Ferien und Feiertage mit Zeitraum (z. B. für die Ferienfelder in UntPlanMobile)
                ferien = s.Paket.Ferien.Select(f => new { name = f.Name, von = f.Von, bis = f.Bis, feiertag = f.Feiertag }),
            });
        });

        api.MapGet("/plan", (string typ, string element, string? datum, int? schueler, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Klassenbuchdienst kb, Klassenbuchverwaltung kv, Uhr uhr) =>
        {
            var b = Ich(c);
            PlanErlaubt(b, typ, element);
            var s = Paket(p);
            var tag = Datum(datum, uhr);
            // Planfenster der Rolle: außerhalb nur das leere Raster mit Hinweis (gesperrt, sperrhinweis)
            if (!Rechtedienst.PlanSichtbar(b, tag, uhr.Heute)) return Results.Json(plan.Wochenplan(s, typ, element, tag, b.Kollegium).Sperren());
            var w = plan.Wochenplan(s, typ, element, tag, b.Kollegium, d => r.Vorgemerkt(d, s));
            // Notizen und fällige Hausaufgaben für die Symbole auf den Karten; eigene Abwesenheiten im eigenen Plan
            return Results.Json(w with { Karten = kb.Anreichern(w.Karten, b.Kollegium), Abwesenheiten = EigeneAbwesenheiten(b, s, typ, element, tag, schueler, kb, kv) });
        });

        api.MapGet("/tag", (string typ, string? datum, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Uhr uhr) =>
        {
            var b = Ich(c);
            if (!Elementtyp.Gueltig(typ)) throw new ArgumentException("Unbekannte Ansicht.");
            if (!Rechtedienst.Darf(b, Recht.PlanTag)) throw new UnauthorizedAccessException("Die Tagesübersicht steht nur dem Kollegium zur Verfügung.");
            var st = Paket(p);
            var tag = Datum(datum, uhr);
            var t = plan.Tag(st, typ, tag, b.Kollegium, d => r.Vorgemerkt(d, st));
            return Results.Json(Rechtedienst.PlanSichtbar(b, tag, uhr.Heute) ? t : t.Sperren());
        });

        api.MapGet("/vertretungen", (string? datum, int? tage, HttpContext c, Paketdienst p, Vertretungsplandienst vp, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var tageListe = vp.NaechsteTage(s, Datum(datum, uhr), Math.Clamp(tage ?? 1, 1, 7));
            // Rechte-Modell: Lehrerversion bzw. Schülerversion
            var lehrerVersion = Rechtedienst.Darf(b, Recht.VertretungLehrer);
            if (!lehrerVersion && !Rechtedienst.Darf(b, Recht.VertretungSchueler)) throw new UnauthorizedAccessException("Der Vertretungsplan ist für Ihr Konto nicht freigegeben.");
            var res = tageListe.Select(d => vp.Tag(s, d, lehrerVersion)).ToList();
            // Klassenkonten: nur die eigene Klasse
            if (!b.Kollegium)
                res = res.Select(t => t with { Zeilen = t.Zeilen.Where(z => z.Klassen.Split(", ").Any(b.HatKlasse)).ToList() }).ToList();
            return Results.Json(new { tage = res });
        });

        // ---------------------------------------------------------------- Startseite

        api.MapGet("/heute", (HttpContext c, Paketdienst p, Plandienst plan, Vertretungsplandienst vp, Rueckmeldungsdienst r, Mitteilungsdienst m, Klassenbuchdienst kb, Uhr uhr, Sprechtagdienst st) =>
        {
            var b = Ich(c);
            var s = p.Aktuell;
            // Sprechtag-Karten (angekündigt, Anmeldung, eigene Termine) wie /api/app/sprechtag/start
            var sprechtage = SprechtagApi.StartKarten(st, b);
            if (s is null) return Results.Json(new { paket = false, ungelesen = m.Ungelesen(b.Id), sprechtage });
            var heute = uhr.Heute;
            var tag = vp.NaechsteTage(s, heute, 1).FirstOrDefault(heute);
            var typ = b.KlassenKuerzel != null ? Elementtyp.Klasse : Elementtyp.Lehrer;
            // Planfenster der Rolle (z. B. Freitag → Montag bei „0 Wochen voraus“): keine Stunden
            var gesperrt = !Rechtedienst.PlanSichtbar(b, tag, heute);
            var element = b.KlassenKuerzel ?? b.LehrerKuerzel;
            var karten = element is null || gesperrt ? new List<Karte>() : kb.Anreichern(plan.Karten(s, tag, typ, element, r.Vorgemerkt(tag, s)), b.Kollegium);
            var aufsichten = element is not null && !gesperrt && typ == Elementtyp.Lehrer ? plan.Aufsichten(s, tag, element) : new List<Aufsicht>();
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
                gesperrt,
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
                sprechtage,
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
            // die zu verlegende Prüfung zählt für die Prüfungssperre nicht mit; Löschen und neu Eintragen als Einheit
            ErweiterungApi.PruefungssperrePruefen(vw, l, r, Paket(p), b, neu.Datum, neu.Klassen ?? new(), (d.Alt.Datum, d.Alt.StundeVon, d.Alt.StundeBis));
            var m = r.KlausurVerlegen(Paket(p), b, d.Alt.Datum, d.Alt.StundeVon, d.Alt.StundeBis, d.Alt.Klassen ?? new(), d.Alt.Titel,
                neu.Datum, neu.StundeVon, neu.StundeBis, neu.Klassen ?? new(), neu.Raeume ?? new(), neu.Aufsicht ?? new(), neu.Titel ?? "", neu.Text ?? "");
            prot.Schreiben(b.Login, "Prüfung verlegt", $"{d.Alt.Datum:yyyy-MM-dd} → {neu.Datum:yyyy-MM-dd}");
            return Results.Json(m);
        });

        api.MapDelete("/meldungen/{id}", (string id, HttpContext c, Rueckmeldungsdienst r) =>
            r.Zurueckziehen(id, Ich(c).Id) ? Results.Json(new { ok = true }) : Results.Json(new { fehler = "Die Meldung wurde bereits von UntPlan übernommen." }, statusCode: 409));

        // ---------------------------------------------------------------- Mitteilungen

        api.MapGet("/mitteilungen", (string? ordner, string? suche, HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            return Results.Json(ordner == "gesendet" ? m.Gesendet(b.Id, suche) : ordner == "entwuerfe" ? m.Entwuerfe(b.Id, suche) : m.Posteingang(b.Id, suche, b.Familie));
        });
        api.MapGet("/mitteilungen/empfaenger", (HttpContext c, Mitteilungsdienst m, Paketdienst p) => Results.Json(m.Empfaengerliste(Ich(c), p.Aktuell)));
        api.MapPost("/mitteilungen", (MitteilungDaten d, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(new { id = m.Senden(Ich(c), p.Aktuell, d.Empfaenger ?? new(), d.Betreff ?? "", d.Text ?? "", d.Wichtig, d.Lesebestaetigung, d.AntwortAuf, d.AntwortErlaubt) }));
        api.MapPost("/mitteilungen/{id:int}/gelesen", (int id, HttpContext c, Mitteilungsdienst m) => { m.Gelesen(Ich(c).Id, id); return Results.Json(new { ok = true }); });
        api.MapDelete("/mitteilungen/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) => { m.Loeschen(Ich(c).Id, id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Klassenbuch

        // Ohne einzelstunde: Eintrag des ganzen Blocks (wie bisher). Mit einzelstunde (Stunde eines Doppel-/Blocks): Eintrag nur dieser Stunde,
        // dazu immer blockEintrag (gilt für alle Stunden), block, einzelstunden (Auswahlmenü) und einzelEintraege.
        api.MapGet("/klassenbuch/stunde", (DateOnly datum, int stunde, int unterricht, string? lehrer, int? einzelstunde, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            Klassenbucheintrag Sichtbar(Klassenbucheintrag e) => b.Kollegium ? e : e with { Notiz = "", OnlineLink = e.Online ? e.OnlineLink : "" };
            // die nächstliegende Stunde (Blockbeginn) zuerst – bei Kopplungen mit Einzel- und Doppelstunden nicht den Eintrag einer anderen Lehrkraft
            var blockEintrag = kb.Zeitraum(datum, datum).Where(e => e.Unterricht == unterricht && stunde >= e.Stunde && (lehrer is null || e.Lehrer.Equals(lehrer, StringComparison.OrdinalIgnoreCase)) &&
                                                                         (b.Kollegium || e.Klassen.Any(b.HatKlasse)))
                .OrderByDescending(e => e.Stunde).FirstOrDefault() is { } be ? Sichtbar(be) : null;
            var karte = kb.Blockkarte(b, s, datum, stunde, unterricht, lehrer);
            var istBlock = karte != null && karte.Von < karte.Bis;
            int? einzel = istBlock && einzelstunde is int n ? n : null;
            if (einzel is int en && (en < karte!.Von || en > karte.Bis)) throw new ArgumentException("Die Einzelstunde gehört nicht zu diesem Block.");
            var einzelEintraege = istBlock
                ? kb.EinzelEintraege(datum, unterricht, karte!.Von, karte.Bis, lehrer).Where(e => b.Kollegium || e.Klassen.Any(b.HatKlasse)).OrderBy(e => e.Stunde).Select(Sichtbar).ToList()
                : new List<Klassenbucheintrag>();
            var eintrag = einzel is int x ? einzelEintraege.FirstOrDefault(e => e.Stunde == x) : blockEintrag;
            var einzelstunden = istBlock
                ? Enumerable.Range(karte!.Von, karte.Bis - karte.Von + 1).Select(nr => Klassenbuchdienst.Einzeln(s, karte, nr)).Select(k => new
                {
                    stunde = k.Von, beginn = k.Beginn, ende = k.Ende,
                    eintrag = einzelEintraege.Any(e => e.Stunde == k.Von && (e.Thema.Length > 0 || e.Notiz.Length > 0 || e.NotizSchueler.Length > 0 || e.Online || e.Anwesenheit != null)),
                }).ToList()
                : new();
            return Results.Json(new
            {
                eintrag, darf = b.Kollegium && kb.DarfSchreiben(b, s, datum, einzel ?? stunde, unterricht),
                einzelstunde = einzel, blockEintrag, block = karte is null ? null : Klassenbuchdienst.Block(s, karte), einzelstunden, einzelEintraege,
            });
        });
        // Lehrstoff, Hausaufgabe, Notizen (Lehrkräfte / Schüler*innen), Lehrform, Anmerkung einer Stunde speichern – PUT und POST (Apps).
        // Nicht mitgeschickte Felder (null) bleiben unverändert, z. B. { datum, stunde, unterricht, notizSchueler } ändert nur die Schülernotiz.
        IResult StundeSpeichern(KlassenbuchDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Klassenbuchverwaltung kv)
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur Lehrkräfte können das Klassenbuch bearbeiten.");
            if (d.OnlineLink is not null) Klassenbuchverwaltung.OnlineLinkPruefen(d.OnlineLink);   // vor dem Speichern prüfen
            var eigener = b.LehrerKuerzel ?? d.Lehrer;
            // Einzelstunde nur bei Blöcken (sonst gilt der Eintrag ohnehin genau dieser Stunde)
            int? einzel = d.Einzelstunde is int en && kb.Blockkarte(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Lehrer) is { } bk && bk.Von < bk.Bis ? en : null;
            var alt = einzel is int x
                ? kb.EinzelEintraege(d.Datum, d.Unterricht, x, x, eigener).FirstOrDefault()
                : kb.Zeitraum(d.Datum, d.Datum).Where(e => e.Unterricht == d.Unterricht && d.Stunde >= e.Stunde && (eigener is null || e.Lehrer.Equals(eigener, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Stunde).FirstOrDefault();
            DateOnly? faellig = d.Hausaufgabe is null && d.Faellig is null ? (DateOnly.TryParse(alt?.Faellig, out var f) ? f : null) : d.Faellig;
            var e = kb.Speichern(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Thema ?? alt?.Thema ?? "", d.Hausaufgabe ?? alt?.Hausaufgabe ?? "", faellig,
                d.Notiz ?? alt?.Notiz ?? "", d.NotizSchueler, d.Lehrform, d.Anmerkung, d.Lehrer, einzel);
            // „Findet online statt“ und Link zum Online-Unterricht (nur wenn mitgeschickt; Link: http/https, "" entfernt ihn)
            if (d.Online is not null || d.OnlineLink is not null)
            {
                kv.OnlineSetzen(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Online ?? alt?.Online ?? false, d.OnlineLink, einzel);
                e = kb.Holen(DateOnly.Parse(e.Datum), e.SpeicherStunde, e.Unterricht, e.Lehrer) ?? e;
            }
            return Results.Json(e);
        }
        api.MapPut("/klassenbuch/stunde", StundeSpeichern);
        api.MapPost("/klassenbuch/stunde", StundeSpeichern);
        // Hausaufgaben einer Stunde (Liste wie im Original) und die in dieser Stunde fälligen Hausaufgaben
        // Hausaufgaben des Blocks und seiner Einzelstunden (mit einzelstunde: nur die dieser Stunde); jede mit „einzelstunde“ (null = ganzer Block)
        api.MapGet("/klassenbuch/hausaufgabenliste", (DateOnly datum, int stunde, int unterricht, int? bis, string? fach, string? klassen, int? einzelstunde, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = RechtePruefung.Verlangen(Ich(c), Recht.HausaufgabenSehen);
            var s = Paket(p);
            var liste = kb.HausaufgabenMitEinzelstunden(datum, stunde, Math.Max(stunde, bis ?? stunde), unterricht, einzelstunde);
            var kl = (klassen ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!b.Kollegium) { kl = kl.Where(b.HatKlasse).ToList(); if (!liste.All(h => h.Klassen.Any(b.HatKlasse))) liste = new(); }
            var faellig = string.IsNullOrEmpty(fach) || kl.Count == 0 ? new List<Hausaufgabe>() : kb.FaelligeHausaufgaben(datum, fach, kl, unterricht);
            return Results.Json(new { liste, faellig, darf = b.Kollegium && kb.DarfSchreiben(b, s, datum, einzelstunde ?? stunde, unterricht) });
        });
        api.MapPost("/klassenbuch/hausaufgabenliste", (HausaufgabeDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.HausaufgabeAnlegen(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Text ?? "", d.Faellig, d.Bemerkung ?? "", d.Einzelstunde)));
        api.MapPut("/klassenbuch/hausaufgabenliste/{id:int}", (int id, HausaufgabeAenderDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.HausaufgabeAendern(Ich(c), Paket(p), id, d.Text, d.Faellig, d.Bemerkung, d.Erledigt)));
        api.MapDelete("/klassenbuch/hausaufgabenliste/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            kb.HausaufgabeLoeschen(Ich(c), Paket(p), id);
            return Results.Json(new { ok = true });
        });
        // Abwesenheiten verlängern (aus der Vorstunde bis Stundenende bzw. bis zum Tagesbeginn)
        api.MapPost("/klassenbuch/abwesenheit/verlaengern", (VerlaengernDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { stunden = kb.AbwesenheitVerlaengern(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.SchuelerIds ?? new(), d.Bis == "tagesbeginn" ? "tagesbeginn" : "stunde", d.Einzelstunde) }));
        // Abwesenheitsdialog (minutengenau von–bis; Verspätung = ab Stundenbeginn bis Ankunft, vorzeitig = ab Uhrzeit bis Stundenende)
        api.MapPost("/klassenbuch/abwesenheit", (AbwesenheitZeitraumDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            var s = Paket(p);
            var ids = d.SchuelerIds ?? new();
            if (ids.Count == 0) throw new ArgumentException("Bitte eine Schülerin bzw. einen Schüler wählen.");
            (DateOnly, int, int)? stunde = d.Datum is DateOnly dt && d.Stunde is int st && d.Unterricht is int u ? (dt, st, u) : null;
            // Ganztägig (z. B. Krankmeldung vor Unterrichtsbeginn): ganzer Schultag bzw. alle Tage von–bis, d. h. alle Unterrichtsstunden laut Plan
            var (von, bis) = d.Ganztaegig == true ? Klassenbuchdienst.Ganztaegig(d.Von, d.Bis) : (d.Von, d.Bis);
            var n = ids.Distinct().Sum(id => kb.AbwesenheitErfassen(b, s, id, von, bis, d.Grund, d.Text, d.Status, stunde, ids.Count == 1 ? d.Ersetzen : null,
                d.Nachweis, d.EntschuldigtAm, d.ElternInformiert, d.NotizIntern, d.Einzelstunde));
            return Results.Json(new { stunden = n });
        });
        // Entschuldigungsstatus direkt im Abwesenheitsdialog (Segment-Schalter) – nur Klassenleitung, Schulleitung, Administration
        api.MapPost("/klassenbuch/abwesenheit/status", (AbwesenheitStatusDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(new { geaendert = kb.AbwesenheitStatusSetzen(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.SchuelerIds ?? new(), d.Status ?? "", d.Einzelstunde) });
        });
        api.MapPost("/klassenbuch/abwesenheit/loeschen", (AbwesenheitEntfernenDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(new { geloescht = kb.AbwesenheitEntfernen(b, Paket(p), d.Datum, d.Stunde, d.Unterricht, d.SchuelerIds ?? new(), d.Einzelstunde) });
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
        api.MapGet("/klassenbuch/anwesenheit", (DateOnly datum, int stunde, int unterricht, int? einzelstunde, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            return Results.Json(kb.Anwesenheit(b, Paket(p), datum, stunde, unterricht, einzelstunde));
        });
        api.MapPut("/klassenbuch/anwesenheit", (AnwesenheitDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { fehlend = kb.AnwesenheitSpeichern(Ich(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Eintraege ?? new(), d.Einzelstunde) }));
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
        api.MapPut("/klassenbuch/vermerke/{id:int}", (int id, VermerkDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(kb.VermerkAendern(Ich(c), Paket(p), id, d.Datum, d.SchuelerId, d.Art ?? "", d.Text ?? "", d.Zeit)));
        api.MapDelete("/klassenbuch/vermerke/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { ok = kb.VermerkLoeschen(Ich(c), Paket(p), id) }));
        api.MapGet("/klassenbuch/schueler", (string klasse, string? datum, HttpContext c, Paketdienst p) =>
        {
            if (!Ich(c).Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
            // mit Datum: Klassenzugehörigkeit an diesem Tag (Klassenwechsel ab Datum), sonst von heute
            return Results.Json(DateOnly.TryParse(datum, out var d) ? Klassenzugehoerigkeit.Am(Paket(p), new[] { klasse }, d) : Klassenbuchdienst.Schueler(Paket(p), new[] { klasse }));
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
            var st = Paket(p);
            var liste = l.Pruefungen(st, r.OffeneKlausuren(st), Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(120)), k, b.Kollegium ? Leer(lehrer) : null, r.OffeneLoeschungen());
            // Löschen/Verlegen: Ersteller, Lehrkraft der Prüfung, Administration – nur heutige und künftige Prüfungen
            return Results.Json(b.Kollegium
                ? liste.Select(x => DateOnly.TryParse(x.Datum, out var pd) && pd >= uhr.Heute && r.DarfKlausurLoeschen(b, st, pd, x.StundeVon, x.StundeBis, x.Klassen) ? x with { DarfLoeschen = true } : x).ToList()
                : liste);
        });

        api.MapGet("/hausaufgaben", (string? von, string? bis, string? klasse, string? lehrer, HttpContext c, Listendienst l, Uhr uhr) =>
        {
            var b = RechtePruefung.Verlangen(Ich(c), Recht.HausaufgabenSehen);
            var k = b.Kollegium ? Leer(klasse) : Leer(klasse) is string w && b.HatKlasse(w) ? w : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
            var liste = l.Hausaufgaben(Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(60)), k, b.Kollegium ? Leer(lehrer) : null);
            return Results.Json(b.Kollegium ? liste : liste.Select(e => e with { Notiz = "" }).ToList());
        });

        // Hausaufgaben mit Id, Bemerkung, Erledigt und Bearbeitungsrecht (Liste und Detailfenster unter Klassenbuch → Hausaufgaben)
        api.MapGet("/hausaufgaben/eintraege", (string? von, string? bis, string? klasse, string? lehrer, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = RechtePruefung.Verlangen(Ich(c), Recht.HausaufgabenSehen);
            var k = b.Kollegium ? Leer(klasse) : Leer(klasse) is string w && b.HatKlasse(w) ? w : b.KlassenKuerzel ?? throw new UnauthorizedAccessException("Ihrem Konto ist keine Klasse zugeordnet.");
            var s = b.Kollegium ? Paket(p) : null;
            var liste = kb.Hausaufgabenliste(Von(von, uhr.Heute.AddDays(-14)), Von(bis, uhr.Heute.AddDays(60)), k, b.Kollegium ? Leer(lehrer) : null);
            return Results.Json(liste.Select(h => new
            {
                h.Id, h.Datum, h.Stunde, h.Unterricht, h.Klassen, h.Fach, h.Lehrer, h.Text, h.Faellig, h.Bemerkung, h.Erledigt, h.Erstellt,
                darf = s != null && h.Id > 0 && kb.DarfSchreiben(b, s, DateOnly.Parse(h.Datum), h.Stunde, h.Unterricht),
            }));
        });

        api.MapGet("/klassenbucheintraege", (string? von, string? bis, string? klasse, string? art, HttpContext c, Paketdienst p, Listendienst l, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            return Results.Json(new { arten = Klassenbuchdienst.VermerkArten, eintraege = l.Vermerke(Paket(p), Von(von, uhr.Heute.AddDays(-30)), Von(bis, uhr.Heute), Leer(klasse), Leer(art), b) });
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
            RechtePruefung.Verlangen(b, Recht.BerichteSehen);
            var st = Paket(p);
            if (!Klassenbuchdienst.FehlzeitenVerwalten(b, st)) throw new UnauthorizedAccessException("Fehlzeitenberichte stehen den Klassenleitungen zur Verfügung.");
            var v = Von(von, uhr.Heute.AddDays(-90));
            var bi = Von(bis, uhr.Heute);
            var liste = l.Fehlzeitenbericht(st, v, bi, Leer(klasse))
                .Where(z => Rechtedienst.Darf(b, Recht.AbwesenheitAlle) || Klassenbuchdienst.IstKlassenleitung(b, st, z.Klasse)).ToList();
            return format == "csv"
                ? Results.File(Listendienst.FehlzeitenCsv(liste), "text/csv; charset=utf-8", $"fehlzeiten-{Leer(klasse) ?? "alle"}-{v:yyyy-MM-dd}-{bi:yyyy-MM-dd}.csv")
                : Results.Json(new { von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = liste });
        });

        api.MapGet("/berichte/lehrstoff", (string klasse, string? von, string? bis, string? format, HttpContext c, Listendienst l, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Ich(c);
            NurKollegium(b);
            RechtePruefung.Verlangen(b, Recht.BerichteSehen);
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
                planWochenZurueck = b.Planfenster.Zurueck, planWochenVor = b.Planfenster.Vor,
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
            return Results.Text(Kalender(s, plan, e.Schulname, typ, element, uhr.Heute, d => Rechtedienst.PlanSichtbar(b, d, uhr.Heute)), "text/calendar; charset=utf-8");
        });
    }

    /// <summary>iCalendar mit den Stunden der nächsten vier Wochen (Entfälle als abgesagt), nur Tage im Planfenster der Rolle.</summary>
    private static string Kalender(Stand s, Plandienst plan, string schule, string typ, string? element, DateOnly heute, Func<DateOnly, bool> sichtbar)
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
                foreach (var k in sichtbar(d) ? plan.Karten(s, d, typ, element) : new List<Karte>())
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
