using System.Globalization;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Sprechtag – alle Endpunkte unter <c>/api/app/sprechtag</c>:
/// <list type="bullet">
/// <item><c>GET /start</c>: Hinweiskarten für Startseite und Übersicht (auch in <c>/api/app/heute</c>).</item>
/// <item><c>GET /</c>: Sprechtage nach Schuljahr (Administration alle, Lehrkräfte veröffentlichte, Erziehungsberechtigte sichtbare) und die wählbaren Schuljahre.</item>
/// <item>Administration: <c>POST /</c> (Schuljahr), <c>PUT /{id}</c> (Konfiguration, laufend gespeichert), <c>POST /{id}/veroeffentlichen</c>,
/// <c>POST /{id}/duplizieren</c>, <c>DELETE /{id}</c>.</item>
/// <item><c>GET /{id}</c>: Sprechtag mit Daten je Rolle (Administration: alle Lehrkräfte; Lehrkraft: eigene Räume, Termine und Abwesenheiten;
/// Erziehungsberechtigte: Kinder, Terminanfragen, Einladungen und Termine).</item>
/// <item>Lehrkraft bzw. Administration (mit <c>lehrer</c>): <c>PUT /{id}/raum</c>, <c>POST /{id}/abwesenheit</c>, <c>DELETE /{id}/abwesenheit/{aid}</c>,
/// <c>GET /{id}/schueler</c>, <c>POST /{id}/einladung</c>, <c>POST /{id}/termin</c>.</item>
/// <item>Termine: <c>PUT /{id}/termin/{tid}</c> (ändern), <c>POST /{id}/termin/{tid}/absagen</c>, <c>/annehmen</c>, <c>/ablehnen</c>.</item>
/// <item>Erziehungsberechtigte: <c>GET /{id}/frei</c>, <c>POST /{id}/buchen</c>, <c>GET /{id}/elternplan.pdf</c>.</item>
/// <item>PDF: <c>GET /{id}/plan.pdf</c> (Lehrkraft bzw. Gesamtplan; <c>leer=true</c> mit freien Zeilen, <c>ausrichtung=hoch|quer</c>).</item>
/// </list>
/// </summary>
public static class SprechtagApi
{
    public sealed record AnlegeDaten(string? Schuljahr);
    public sealed record RaumDaten(string? Lehrer, int Abschnitt, string? Raum);
    public sealed record AbwesenheitDaten(int? Id, string? Lehrer, int Abschnitt, string? Beginn, string? Ende, string? Anmerkung);
    public sealed record EinladungDaten(string? Lehrer, List<int>? SchuelerIds, string? Text, bool? Verpflichtend);
    public sealed record TerminDaten(string? Lehrer, int Abschnitt, string? Beginn, string? Ende, int SchuelerId, string? Anmerkung, string? Link, bool? Verpflichtend);
    public sealed record AenderDaten(int? Abschnitt, string? Beginn, string? Ende, string? Anmerkung, string? Link);
    public sealed record AbsageDaten(string? Grund);
    public sealed record BuchDaten(string? Lehrer, int Abschnitt, string? Beginn, int SchuelerId, int? EinladungId);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true);

    /// <summary>Lehrkräfte, Schulleitung, Administration und Erziehungsberechtigte (Schüler*innen haben keinen Zugriff).</summary>
    private static Benutzer Ich(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!b.Kollegium && b.Rolle != Rollen.Eltern)
            throw new UnauthorizedAccessException("Der Sprechtag steht Lehrkräften, Administration und Erziehungsberechtigten zur Verfügung.");
        return b;
    }

    private static Benutzer Admin(HttpContext c)
    {
        var b = Ich(c);
        if (!Sprechtagdienst.IstVerwaltung(b)) throw new UnauthorizedAccessException("Nur für die Administration.");
        return b;
    }

    /// <summary>Sprechtag, den das Konto sehen darf: Administration alle, Lehrkräfte veröffentlichte, Erziehungsberechtigte nach Phase und Einladung.</summary>
    private static Sprechtag Sichtbar(Sprechtagdienst d, Benutzer b, int id)
    {
        var t = d.HolenOderFehler(id);
        if (!SiehtSprechtag(d, t, b)) throw new ArgumentException("Unbekannter Sprechtag.");
        return t;
    }

    private static bool SiehtSprechtag(Sprechtagdienst d, Sprechtag t, Benutzer b) =>
        Sprechtagdienst.IstVerwaltung(b) || (b.Kollegium ? t.IstVeroeffentlicht : b.Rolle == Rollen.Eltern && d.SichtbarFuerEltern(t, b));

    /// <summary>Lehrkraft, für die gehandelt wird (Administration: angegebene, sonst die eigene).</summary>
    private static string Lehrkraft(Benutzer b, string? lehrer)
    {
        if (!b.Kollegium && !Sprechtagdienst.IstVerwaltung(b)) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
        var l = string.IsNullOrWhiteSpace(lehrer) ? b.LehrerKuerzel : lehrer.Trim();
        if (string.IsNullOrEmpty(l)) throw new ArgumentException("Bitte eine Lehrkraft wählen.");
        if (!Sprechtagdienst.IstVerwaltung(b) && !Ci.Equals(l, b.LehrerKuerzel)) throw new UnauthorizedAccessException("Sie können nur Ihre eigenen Termine bearbeiten.");
        return l;
    }

    // ------------------------------------------------------------------------------------------ Schuljahre

    public sealed record Schuljahr(string Name, string Von, string Bis);

    /// <summary>Wählbare Schuljahre: aktuelles und folgendes (aus den Plandaten bzw. 01.08.–31.07.).</summary>
    public static List<Schuljahr> Schuljahre(Stand? s, DateOnly heute)
    {
        DateOnly von, bis;
        if (s != null && s.SchuljahrVon != default && s.SchuljahrBis != default) { von = s.SchuljahrVon; bis = s.SchuljahrBis; }
        else
        {
            var j = heute.Month >= 8 ? heute.Year : heute.Year - 1;
            von = new DateOnly(j, 8, 1); bis = new DateOnly(j + 1, 7, 31);
        }
        Schuljahr Sj(DateOnly v, DateOnly b) => new($"{v.Year}/{b.Year}", v.ToString("yyyy-MM-dd", Inv), b.ToString("yyyy-MM-dd", Inv));
        return new() { Sj(von, bis), Sj(von.AddYears(1), bis.AddYears(1)) };
    }

    // ------------------------------------------------------------------------------------------ JSON

    private static string Schule(Einstellungen e, Paketdienst p) => e.Schulname.Length > 0 ? e.Schulname : p.Aktuell?.Paket.Schule.Name ?? "";

    private static string Datum(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? d.ToString("dd.MM.yyyy", Inv) : "";
    private static string Wochentag(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? Plandienst.Wochentage[Plandienst.TagIndex(d)] : "";

    private static object SprechtagJson(Sprechtagdienst d, Sprechtag t)
    {
        var phase = d.Phase(t);
        return new
        {
            t.Id, name = t.K.Name, t.Schuljahr, schuljahrVon = t.SchuljahrVon, schuljahrBis = t.SchuljahrBis,
            schuljahrText = $"{t.Schuljahr} | {Datum(t.SchuljahrVon)} – {Datum(t.SchuljahrBis)}",
            konfig = t.K, phase, phaseText = Sprechtagdienst.PhasenText[phase], veroeffentlicht = t.IstVeroeffentlicht,
            datumText = Sprechtagdienst.DatumText(t), anmeldungText = Sprechtagdienst.AnmeldungText(t),
            bearbeitbar = d.Bearbeitbar(t), fehler = Sprechtagdienst.Pruefen(t),
            abschnitte = t.K.Abschnitte.Select((a, i) => new
            {
                index = i, a.Name, a.Datum, datumText = Datum(a.Datum), wochentag = Wochentag(a.Datum), a.Von, a.Bis,
                bloecke = Sprechtagdienst.Bloecke(t, a).Select(z => z.Von),
            }),
        };
    }

    private static object SchuelerJson(Stand? s, int id) =>
        s?.Schueler.FirstOrDefault(x => x.Id == id) is { } x ? new { x.Id, x.Vorname, x.Name, x.Klasse } : new { Id = id, Vorname = "", Name = "Unbekannt", Klasse = "" };

    /// <summary>Anzeige des Terminstatus: Terminiert (Anfrage vor der 1. Anmeldephase), Versendet, Akzeptiert, Eingeladen, Abgelehnt, Abgesagt.</summary>
    public static string StatusText(string phase, StTermin x) => x.Status switch
    {
        "einladung" => "Eingeladen",
        "angefragt" => Sprechtagdienst.Rang(phase) < Sprechtagdienst.Rang(Sprechtagdienst.Phase1) ? "Terminiert" : "Versendet",
        "bestaetigt" => "Akzeptiert",
        "abgelehnt" => "Abgelehnt",
        _ => "Abgesagt",
    };

    private static object TerminJson(Sprechtagdienst d, Sprechtag t, Stand? s, Dictionary<(string, int), string> raeume, string phase, StTermin x)
    {
        var a = x.HatZeit && x.Abschnitt < t.K.Abschnitte.Count ? t.K.Abschnitte[x.Abschnitt] : null;
        return new
        {
            x.Id, x.Lehrer, lehrerName = Sprechtagdienst.LehrerName(s, x.Lehrer), lehrerTitel = Sprechtagdienst.LehrerTitel(s, x.Lehrer),
            x.Abschnitt, abschnittName = a?.Name ?? "", datum = a?.Datum ?? "", datumText = a is null ? "" : Datum(a.Datum), wochentag = a is null ? "" : Wochentag(a.Datum),
            x.Beginn, x.Ende, schueler = SchuelerJson(s, x.SchuelerId), x.Art, x.Status, statusText = StatusText(phase, x), x.Verpflichtend,
            x.Link, x.Anmerkung, x.Grund, raum = t.K.Online || x.Abschnitt < 0 ? "" : d.Raum(raeume, x.Lehrer, x.Abschnitt), online = t.K.Online,
        };
    }

    private static object LehrkraftJson(Sprechtagdienst d, Sprechtag t, Stand? s, string kurz, List<StTermin> termine, List<StAbwesenheit> abw,
        Dictionary<(string, int), string> raeume, string phase)
    {
        var eigene = termine.Where(x => Ci.Equals(x.Lehrer, kurz) && x.Status is "einladung" or "angefragt" or "bestaetigt").ToList();
        var raeumeListe = Enumerable.Range(0, t.K.Abschnitte.Count).Select(i => d.Raum(raeume, kurz, i)).ToList();
        return new
        {
            kurz, name = Sprechtagdienst.LehrerName(s, kurz), titel = Sprechtagdienst.LehrerTitel(s, kurz),
            raeume = raeumeListe, keinRaum = !t.K.Online && raeumeListe.Any(r => r.Length == 0), keineTermine = eigene.Count == 0,
            termine = eigene.Select(x => TerminJson(d, t, s, raeume, phase, x)),
            abwesenheiten = abw.Where(x => Ci.Equals(x.Lehrer, kurz)).Select(x => new { x.Id, x.Abschnitt, x.Beginn, x.Ende, x.Anmerkung }),
        };
    }

    /// <summary>Hinweis für Erziehungsberechtigte je Phase (Infobanner der Vorlage).</summary>
    private static string ElternHinweis(Sprechtag t, string phase) => phase switch
    {
        Sprechtagdienst.Phase1 => "Buchung nur mit Einladung möglich",
        Sprechtagdienst.Phase1Beendet => $"Die allgemeine Anmeldung beginnt am {Sprechtagdienst.AnmeldungText(t).Split(' ').FirstOrDefault()}. Bis dahin ist eine Buchung nur mit Einladung möglich.",
        Sprechtagdienst.Phase2 => "Sie können jetzt bei allen Lehrkräften Ihrer Kinder Termine buchen.",
        Sprechtagdienst.Geschlossen or Sprechtagdienst.Durchfuehrung => "Die Anmeldung ist geschlossen.",
        _ => "",
    };

    // ------------------------------------------------------------------------------------------ Startseite

    /// <summary>
    /// Hinweiskarten „Sprechtag“ für Startseite und Übersicht: Lehrkräfte ab der Veröffentlichung, Erziehungsberechtigte, sobald sie den
    /// Sprechtag sehen (bis zum Abschluss). Felder kompatibel zu den Apps (id, name, datum, datumText, phase, status, text, …); dazu für die
    /// kompakte Karte: <c>tagText</c> („Fr 04.12.2026“), <c>zeitText</c> („14:00–19:00“), <c>rolle</c>, <c>gebucht</c>, <c>frei</c>
    /// (Lehrkraft), <c>offen</c> (Erziehungsberechtigte: unbeantwortete Einladungen und Anfragen), <c>heute</c>, <c>naechster</c>,
    /// <c>eintraege</c> (Eltern: gebuchte Termine; Lehrkraft: die nächsten Termine) und <c>knopf</c>. <c>text</c> ist die kurze Statuszeile
    /// (ohne die Phase zu wiederholen), z. B. „Anmeldung bis 02.12. 18:00 · 3 Termine gebucht“.
    /// </summary>
    public static List<object> StartKarten(Sprechtagdienst d, Benutzer b, Stand? s = null)
    {
        var karten = new List<object>();
        if (!b.Kollegium && b.Rolle != Rollen.Eltern) return karten;
        var jetzt = d.JetztZeit;
        var heuteIso = jetzt.ToString("yyyy-MM-dd", Inv);
        var uhrzeit = jetzt.ToString("HH:mm", Inv);
        foreach (var t in d.Alle().Where(t => t.IstVeroeffentlicht))
        {
            var phase = d.Phase(t);
            if (phase == Sprechtagdienst.Abgeschlossen) continue;
            if (b.Rolle == Rollen.Eltern && !d.SichtbarFuerEltern(t, b)) continue;
            var termine = d.Termine(t.Id);
            var raeume = d.Raeume(t.Id);
            var abschnitte = t.K.Abschnitte.Select((a, i) => (A: a, I: i)).Where(x => DateOnly.TryParse(x.A.Datum, Inv, out _))
                .OrderBy(x => x.A.Datum, StringComparer.Ordinal).ThenBy(x => x.A.Von, StringComparer.Ordinal).ToList();
            var erster = abschnitte.Select(x => x.A).FirstOrDefault();
            var ersterTag = abschnitte.Where(x => x.A.Datum == erster?.Datum).Select(x => x.A).ToList();
            var eltern = b.Rolle == Rollen.Eltern;
            var lehrkraft = !eltern && !string.IsNullOrEmpty(b.LehrerKuerzel);
            var rolle = eltern ? "eltern" : lehrkraft ? "lehrer" : "admin";
            string AbschnittDatum(StTermin x) => x.Abschnitt >= 0 && x.Abschnitt < t.K.Abschnitte.Count ? t.K.Abschnitte[x.Abschnitt].Datum : "";

            // eigene Termine: Eltern bestätigte, Lehrkraft aktive (angefragt oder bestätigt), Administration alle aktiven
            var familie = eltern ? termine.Where(x => b.SchuelerIds.Contains(x.SchuelerId)).ToList() : new List<StTermin>();
            var mein = lehrkraft ? termine.Where(x => Ci.Equals(x.Lehrer, b.LehrerKuerzel)).ToList() : new List<StTermin>();
            var relevant = (eltern ? familie.Where(x => x.Status == "bestaetigt") : lehrkraft ? mein.Where(x => x.Aktiv) : termine.Where(x => x.Aktiv))
                .Where(x => x.HatZeit).OrderBy(AbschnittDatum, StringComparer.Ordinal).ThenBy(x => x.Beginn, StringComparer.Ordinal).ToList();
            var eigene = eltern ? familie.Count(x => x.Status == "bestaetigt") : lehrkraft ? mein.Count(x => x.Aktiv) : relevant.Count;
            var einladungen = eltern ? familie.Count(x => x.Status is "einladung" or "angefragt") : mein.Count(x => x.Status == "einladung");
            var frei = lehrkraft ? FreieBloecke(d, t, b.LehrerKuerzel!, mein) : (int?)null;
            var kommend = relevant.Where(x => string.CompareOrdinal(AbschnittDatum(x), heuteIso) > 0
                || AbschnittDatum(x) == heuteIso && string.CompareOrdinal(x.Ende, uhrzeit) > 0).ToList();
            var heute = abschnitte.Any(x => x.A.Datum == heuteIso) && Sprechtagdienst.Rang(phase) >= Sprechtagdienst.Rang(Sprechtagdienst.Geschlossen);
            var naechster = heute ? kommend.FirstOrDefault(x => AbschnittDatum(x) == heuteIso) : null;

            string Anzahl(int n) => eltern || !lehrkraft
                ? (n == 0 ? "Noch keine Termine gebucht" : $"{Termine(n)} gebucht")
                : $"{n} gebucht · {frei} frei";
            var teile = new List<string>();
            if (heute)
            {
                teile.Add(naechster is null ? "Heute · keine weiteren Termine" : $"Heute · nächster Termin {naechster.Beginn}" + (eltern
                    ? $" bei {Sprechtagdienst.LehrerName(s, naechster.Lehrer)}"
                    : s?.Schueler.FirstOrDefault(y => y.Id == naechster.SchuelerId) is { } nks && lehrkraft ? $" mit {nks.Vorname} {nks.Name}" : ""));
                if (naechster is null && eigene > 0) teile.Add(Anzahl(eigene));
            }
            else if (phase == Sprechtagdienst.Verwaltung)
            {
                teile.Add($"Anmeldung ab {Kurz(t.K.Phase1Von)}");
                if (lehrkraft && !t.K.Online && Enumerable.Range(0, t.K.Abschnitte.Count).Any(i => d.Raum(raeume, b.LehrerKuerzel!, i).Length == 0)) teile.Add("Raum noch nicht gewählt");
                if (lehrkraft && eigene > 0) teile.Add(Anzahl(eigene));
            }
            else if (phase is Sprechtagdienst.Phase1 or Sprechtagdienst.Phase1Beendet or Sprechtagdienst.Phase2)
            {
                teile.Add($"Anmeldung bis {Kurz(t.K.Phase2Bis)}");
                if (eltern && einladungen > 0) teile.Add(einladungen == 1 ? "1 Einladung offen" : $"{einladungen} Einladungen offen");
                if (!eltern || eigene > 0 || einladungen == 0) teile.Add(Anzahl(eigene));
            }
            else
            {
                teile.Add("Anmeldung geschlossen");
                teile.Add(Anzahl(eigene));
            }
            var text = string.Join(" · ", teile);

            // Liste: Eltern alle gebuchten (kommenden) Termine, Lehrkraft die nächsten drei; Administration keine
            var liste = eltern ? kommend : lehrkraft ? kommend.Take(3).ToList() : new List<StTermin>();
            var eintraege = liste.Select(x =>
            {
                var k = s?.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
                var datum = AbschnittDatum(x);
                return new
                {
                    x.Id, datum, tagText = TagKurz(datum), x.Beginn, x.Ende, x.Status, x.Lehrer,
                    lehrerName = Sprechtagdienst.LehrerName(s, x.Lehrer), lehrerTitel = Sprechtagdienst.LehrerTitel(s, x.Lehrer),
                    raum = t.K.Online ? "online" : d.Raum(raeume, x.Lehrer, x.Abschnitt),
                    schueler = k is null ? "" : $"{k.Vorname} {k.Name}".Trim(), vorname = k?.Vorname ?? "", klasse = k?.Klasse ?? "",
                };
            }).ToList();

            var status = phase switch
            {
                Sprechtagdienst.Verwaltung => "angekuendigt",
                Sprechtagdienst.Phase1 or Sprechtagdienst.Phase1Beendet or Sprechtagdienst.Phase2 => "anmeldung",
                _ => "geschlossen",
            };
            var buchbar = eltern && (phase == Sprechtagdienst.Phase2 || (phase is Sprechtagdienst.Phase1 or Sprechtagdienst.Phase1Beendet && einladungen > 0));
            karten.Add(new
            {
                t.Id, name = t.K.Name, datum = erster?.Datum ?? "", datumText = Sprechtagdienst.DatumText(t), beginn = erster?.Von ?? "", ende = erster?.Bis ?? "", ort = "",
                phase, phaseText = Sprechtagdienst.PhasenText[phase], status, text, termine = eigene, einladungen,
                anmeldungVon = t.K.Phase1Von, anmeldungVonText = Zeit(t.K.Phase1Von), anmeldungBis = t.K.Phase2Bis, anmeldungBisText = Zeit(t.K.Phase2Bis),
                buchbar, online = t.K.Online,
                rolle, tagText = TagKurz(erster?.Datum ?? "") + (abschnitte.Select(x => x.A.Datum).Distinct().Count() > 1 ? $" – {TagKurz(abschnitte[^1].A.Datum)}" : ""),
                zeitText = ersterTag.Count == 0 ? "" : $"{ersterTag.Min(a => a.Von)}–{ersterTag.Max(a => a.Bis)}",
                gebucht = eigene, frei, offen = eltern ? einladungen : 0, heute,
                naechster = naechster is null ? null : new
                {
                    naechster.Beginn, naechster.Ende, lehrerName = Sprechtagdienst.LehrerName(s, naechster.Lehrer),
                    raum = t.K.Online ? "online" : d.Raum(raeume, naechster.Lehrer, naechster.Abschnitt),
                    schueler = s?.Schueler.FirstOrDefault(y => y.Id == naechster.SchuelerId) is { } nk ? $"{nk.Vorname} {nk.Name}" : "",
                },
                eintraege, weitere = eltern || lehrkraft ? Math.Max(0, kommend.Count - eintraege.Count) : 0,
                knopf = eltern ? (buchbar ? "Termine wählen" : "Termine ansehen") : "Termine verwalten",
            });
        }
        return karten;
    }

    /// <summary>Freie Zeitblöcke der Lehrkraft (ohne aktive Termine und Abwesenheiten des Sprechtags).</summary>
    private static int FreieBloecke(Sprechtagdienst d, Sprechtag t, string lehrer, List<StTermin> mein)
    {
        var abw = d.Abwesenheiten(t.Id).Where(x => Ci.Equals(x.Lehrer, lehrer)).ToList();
        var frei = 0;
        for (var i = 0; i < t.K.Abschnitte.Count; i++)
        {
            var belegt = mein.Where(x => x.Aktiv && x.HatZeit && x.Abschnitt == i).Select(x => (x.Beginn, x.Ende))
                .Concat(abw.Where(x => x.Abschnitt == i).Select(x => (x.Beginn, x.Ende)))
                .Where(x => x.Beginn.Length == 5 && x.Ende.Length == 5)
                .Select(x => (Von: Sprechtagdienst.Minuten(x.Beginn), Bis: Sprechtagdienst.Minuten(x.Ende))).ToList();
            frei += Sprechtagdienst.Bloecke(t, t.K.Abschnitte[i]).Count(z =>
            {
                int von = Sprechtagdienst.Minuten(z.Von), bis = Sprechtagdienst.Minuten(z.Bis);
                return !belegt.Any(x => x.Von < bis && von < x.Bis);
            });
        }
        return frei;
    }

    private static string Termine(int n) => $"{n} {(n == 1 ? "Termin" : "Termine")}";
    private static string Zeit(string iso) => DateTime.TryParse(iso, Inv, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy HH:mm", Inv) : "";
    /// <summary>„02.12. 18:00“ (kurz für die Statuszeile).</summary>
    private static string Kurz(string iso) => DateTime.TryParse(iso, Inv, DateTimeStyles.None, out var d) ? d.ToString("dd.MM. HH:mm", Inv) : "";
    /// <summary>„Fr 04.12.2026“.</summary>
    private static string TagKurz(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? $"{Plandienst.Wochentage[Plandienst.TagIndex(d)][..2]} {d:dd.MM.yyyy}" : "";

    // ------------------------------------------------------------------------------------------ Endpunkte

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/sprechtag");

        api.MapGet("/start", (HttpContext c, Sprechtagdienst d, Paketdienst p) => Results.Json(StartKarten(d, c.Ich() ?? throw new NichtAngemeldetException(), p.Aktuell)));

        api.MapGet("/", (HttpContext c, Sprechtagdienst d, Paketdienst p, Uhr uhr) =>
        {
            var b = Ich(c);
            var liste = d.Alle().Where(t => SiehtSprechtag(d, t, b)).ToList();
            return Results.Json(new
            {
                admin = Sprechtagdienst.IstVerwaltung(b), ich = b.LehrerKuerzel ?? "", eltern = b.Rolle == Rollen.Eltern,
                schuljahre = Schuljahre(p.Aktuell, uhr.Heute),
                phasen = Sprechtagdienst.PhasenText.Select(x => new { phase = x.Key, text = x.Value }),
                sprechtage = liste.OrderByDescending(t => t.Schuljahr, StringComparer.Ordinal)
                    .ThenBy(t => t.K.Abschnitte.Select(a => a.Datum).Where(x => x.Length > 0).DefaultIfEmpty("9999").Min(), StringComparer.Ordinal)
                    .Select(t => SprechtagJson(d, t)),
            });
        });

        api.MapPost("/", (AnlegeDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Uhr uhr, Protokoll prot) =>
        {
            var b = Admin(c);
            var sj = Schuljahre(p.Aktuell, uhr.Heute).FirstOrDefault(x => x.Name == (daten.Schuljahr ?? "").Trim()) ?? throw new ArgumentException("Bitte ein Schuljahr auswählen.");
            var t = d.Anlegen(sj.Name, sj.Von, sj.Bis);
            prot.Schreiben(b.Login, "Sprechtag angelegt", $"{t.K.Name} ({t.Schuljahr})");
            return Results.Json(SprechtagJson(d, t));
        });

        api.MapPut("/{id:int}", (int id, StKonfig konfig, HttpContext c, Sprechtagdienst d) =>
        {
            Admin(c);
            return Results.Json(SprechtagJson(d, d.KonfigSpeichern(d.HolenOderFehler(id), konfig)));
        });

        api.MapPost("/{id:int}/veroeffentlichen", (int id, HttpContext c, Sprechtagdienst d, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Veroeffentlichen(d.HolenOderFehler(id), b);
            prot.Schreiben(b.Login, "Sprechtag veröffentlicht", t.K.Name);
            return Results.Json(SprechtagJson(d, t));
        });

        api.MapPost("/{id:int}/duplizieren", (int id, HttpContext c, Sprechtagdienst d, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Duplizieren(d.HolenOderFehler(id));
            prot.Schreiben(b.Login, "Sprechtag dupliziert", t.K.Name);
            return Results.Json(SprechtagJson(d, t));
        });

        api.MapDelete("/{id:int}", (int id, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.HolenOderFehler(id);
            d.Loeschen(p.Aktuell, t, b);
            prot.Schreiben(b.Login, "Sprechtag gelöscht", t.K.Name);
            return Results.Json(new { ok = true });
        });

        api.MapGet("/{id:int}", (int id, HttpContext c, Sprechtagdienst d, Paketdienst p, Buchungsdienst bu) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var phase = d.Phase(t);
            var termine = d.Termine(t.Id);
            var raeume = d.Raeume(t.Id);
            var admin = Sprechtagdienst.IstVerwaltung(b);
            if (b.Rolle == Rollen.Eltern)
            {
                var kinder = (s?.Schueler ?? new()).Where(x => b.SchuelerIds.Contains(x.Id)).ToList();
                var familie = termine.Where(x => b.SchuelerIds.Contains(x.SchuelerId)).ToList();
                return Results.Json(new
                {
                    rolle = "eltern", sprechtag = SprechtagJson(d, t), hinweis = ElternHinweis(t, phase),
                    freiBuchbar = phase == Sprechtagdienst.Phase2,
                    aendern = phase is Sprechtagdienst.Phase1 or Sprechtagdienst.Phase2,
                    kinder = kinder.Select(k => new
                    {
                        k.Id, k.Vorname, k.Name, k.Klasse,
                        lehrkraefte = Sprechtagdienst.LehrkraefteDesKindes(s, k).Select(l => new { l.Kurz, l.Name, l.Faecher, l.Klassenleitung }),
                    }),
                    einladungen = familie.Where(x => x.Status == "einladung").Select(x => TerminJson(d, t, s, raeume, phase, x)),
                    anfragen = familie.Where(x => x.Status == "angefragt").Select(x => TerminJson(d, t, s, raeume, phase, x)),
                    termine = familie.Where(x => x.Status == "bestaetigt").OrderBy(x => x.Abschnitt < t.K.Abschnitte.Count ? t.K.Abschnitte[x.Abschnitt].Datum : "").ThenBy(x => x.Beginn)
                        .Select(x => TerminJson(d, t, s, raeume, phase, x)),
                });
            }
            var abw = d.Abwesenheiten(t.Id);
            var lehrer = admin
                ? d.Lehrkraefte(s).OrderBy(l => Sprechtagdienst.LehrerName(s, l), De).ToList()
                : new List<string> { b.LehrerKuerzel ?? "" };
            var raumListe = s is null || t.K.Online ? new List<Raumdaten>() : bu.Raeume(s).Where(r => r.Kurz.Length > 0).ToList();
            return Results.Json(new
            {
                rolle = admin ? "admin" : "lehrer", ich = b.LehrerKuerzel ?? "", sprechtag = SprechtagJson(d, t),
                rechte = new
                {
                    raeume = admin || t.K.Rechte.RaeumeWaehlen, abwesenheiten = admin || t.K.Rechte.AbwesenheitenEintragen,
                    bearbeiten = admin || t.K.Rechte.TermineBearbeiten, buchen = admin || t.K.Rechte.TermineBuchen, einladen = true,
                },
                lehrkraefte = lehrer.Where(l => l.Length > 0).Select(l => LehrkraftJson(d, t, s, l, termine, abw, raeume, phase)),
                raumListe = raumListe.OrderBy(r => r.Gebaeude, De).ThenBy(r => r.Kurz, De).Select(r => new { r.Kurz, r.Name, r.Gebaeude }),
            });
        });

        api.MapPut("/{id:int}/raum", (int id, RaumDaten daten, HttpContext c, Sprechtagdienst d) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            d.RaumSetzen(t, b, Lehrkraft(b, daten.Lehrer), daten.Abschnitt, daten.Raum);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/abwesenheit", (int id, AbwesenheitDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var a = d.AbwesenheitSpeichern(p.Aktuell, t, b, daten.Id, Lehrkraft(b, daten.Lehrer), daten.Abschnitt, daten.Beginn ?? "", daten.Ende ?? "", daten.Anmerkung);
            return Results.Json(new { a.Id, a.Abschnitt, a.Beginn, a.Ende, a.Anmerkung });
        });

        api.MapDelete("/{id:int}/abwesenheit/{aid:int}", (int id, int aid, HttpContext c, Sprechtagdienst d) =>
        {
            var b = Ich(c);
            d.AbwesenheitLoeschen(Sichtbar(d, b, id), b, aid);
            return Results.Json(new { ok = true });
        });

        api.MapGet("/{id:int}/schueler", (int id, string? lehrer, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var l = Lehrkraft(b, lehrer);
            var vorhanden = d.Termine(t.Id).Where(x => Ci.Equals(x.Lehrer, l) && (x.Aktiv || x.Status == "einladung")).Select(x => x.SchuelerId).ToHashSet();
            return Results.Json(Sprechtagdienst.SchuelerDerLehrkraft(p.Aktuell, l).Select(x => new { x.Id, x.Vorname, x.Name, x.Klasse, vorhanden = vorhanden.Contains(x.Id) }));
        });

        api.MapPost("/{id:int}/einladung", (int id, EinladungDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            if (daten.SchuelerIds is not { Count: > 0 }) throw new ArgumentException("Bitte mindestens eine Schülerin bzw. einen Schüler auswählen.");
            var n = d.Einladen(p.Aktuell, t, b, Lehrkraft(b, daten.Lehrer), daten.SchuelerIds, daten.Text, daten.Verpflichtend == true);
            return Results.Json(new { anzahl = n });
        });

        api.MapPost("/{id:int}/termin", (int id, TerminDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var x = d.TerminAnlegen(p.Aktuell, t, b, Lehrkraft(b, daten.Lehrer), daten.Abschnitt, daten.Beginn ?? "", daten.Ende ?? "", daten.SchuelerId,
                daten.Anmerkung, daten.Link, daten.Verpflichtend == true);
            return Results.Json(new { x.Id });
        });

        StTermin TerminVon(Sprechtagdienst d, Sprechtag t, int tid) =>
            d.Termin(tid) is { } x && x.SprechtagId == t.Id ? x : throw new ArgumentException("Unbekannter Termin.");

        api.MapPut("/{id:int}/termin/{tid:int}", (int id, int tid, AenderDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var x = d.TerminAendern(p.Aktuell, t, b, TerminVon(d, t, tid), daten.Abschnitt, daten.Beginn, daten.Ende, daten.Anmerkung, daten.Link);
            return Results.Json(new { x.Id });
        });

        api.MapPost("/{id:int}/termin/{tid:int}/absagen", (int id, int tid, AbsageDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            d.Absagen(p.Aktuell, t, b, TerminVon(d, t, tid), daten.Grund);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/termin/{tid:int}/annehmen", (int id, int tid, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            d.Beantworten(p.Aktuell, t, b, TerminVon(d, t, tid), true);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/termin/{tid:int}/ablehnen", (int id, int tid, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            d.Beantworten(p.Aktuell, t, b, TerminVon(d, t, tid), false);
            return Results.Json(new { ok = true });
        });

        api.MapGet("/{id:int}/frei", (int id, string? lehrer, int abschnitt, int? schuelerId, int? ausser, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            string l;
            int? kind = null;
            if (b.Rolle == Rollen.Eltern)
            {
                l = (lehrer ?? "").Trim();
                if (schuelerId is not int k || !b.SchuelerIds.Contains(k)) throw new UnauthorizedAccessException("Bitte ein eigenes Kind wählen.");
                kind = k;
            }
            else l = Lehrkraft(b, lehrer);
            return Results.Json(d.Frei(p.Aktuell, t, l, abschnitt, kind, ausser).Select(z => new { z.Von, z.Bis }));
        });

        // ---------------------------------------------------------------- PDF

        // Terminplan: Lehrkraft (eigener; Administration mit lehrer=) bzw. Gesamtplan aller Lehrkräfte (Administration ohne lehrer);
        // leer=true listet alle Zeitblöcke (freie Zeilen zum Eintragen). Ausrichtung: Lehrkraft hoch, Gesamtplan quer (umschaltbar).
        api.MapGet("/{id:int}/plan.pdf", (int id, string? lehrer, bool? leer, string? ausrichtung, HttpContext c, Sprechtagdienst d, Paketdienst p, Einstellungen e) =>
        {
            var b = Ich(c);
            if (b.Rolle == Rollen.Eltern) throw new UnauthorizedAccessException("Nur für Lehrkräfte und Administration.");
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var alle = Sprechtagdienst.IstVerwaltung(b) && string.IsNullOrWhiteSpace(lehrer);
            var termine = d.Termine(t.Id).Where(x => x.Aktiv && x.HatZeit).ToList();
            var abw = d.Abwesenheiten(t.Id);
            var raeume = d.Raeume(t.Id);
            var liste = alle
                ? d.Lehrkraefte(s).Where(l => termine.Any(x => Ci.Equals(x.Lehrer, l)) || leer == true)
                    .OrderBy(l => Sprechtagdienst.LehrerName(s, l), De).ToList()
                : new List<string> { Lehrkraft(b, lehrer) };
            var zeilen = new List<IReadOnlyList<string>>();
            foreach (var kurz in liste)
                for (var i = 0; i < t.K.Abschnitte.Count; i++)
                {
                    var a = t.K.Abschnitte[i];
                    var raum = t.K.Online ? "online" : d.Raum(raeume, kurz, i);
                    var eintraege = termine.Where(x => Ci.Equals(x.Lehrer, kurz) && x.Abschnitt == i).Select(x =>
                    {
                        var k = s?.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
                        return (Von: x.Beginn, Zeile: new List<string> { $"{x.Beginn}–{x.Ende}", k is null ? "Unbekannt" : $"{k.Name}, {k.Vorname}", k?.Klasse ?? "",
                            StatusText(d.Phase(t), x), t.K.Online && x.Link.Length > 0 ? $"{x.Anmerkung} {x.Link}".Trim() : x.Anmerkung, raum });
                    }).ToList();
                    eintraege.AddRange(abw.Where(x => Ci.Equals(x.Lehrer, kurz) && x.Abschnitt == i).Select(x =>
                        (Von: x.Beginn, Zeile: new List<string> { $"{x.Beginn}–{x.Ende}", "– abwesend –", "", "Abwesend", x.Anmerkung, raum })));
                    if (leer == true)
                        foreach (var z in Sprechtagdienst.Bloecke(t, a).Where(z => !eintraege.Any(x => Sprechtagdienst.Minuten(x.Von) <= Sprechtagdienst.Minuten(z.Von)
                                     && Sprechtagdienst.Minuten(z.Von) < Sprechtagdienst.Minuten(x.Zeile[0].Split('–')[1]))))
                            eintraege.Add((Von: z.Von, Zeile: new List<string> { $"{z.Von}–{z.Bis}", "", "", "", "", raum }));
                    foreach (var x in eintraege.OrderBy(x => x.Von, StringComparer.Ordinal))
                    {
                        var zeile = x.Zeile;
                        zeile.Insert(0, $"{Datum(a.Datum)} {a.Name}");
                        if (alle) zeile.Insert(0, Sprechtagdienst.LehrerTitel(s, kurz));
                        zeilen.Add(zeile);
                    }
                }
            var spalten = new List<string> { "Abschnitt", "Zeit", "Schüler*in", "Klasse", "Status", "Anmerkung", "Raum" };
            if (alle) spalten.Insert(0, "Lehrkraft");
            var titel = alle ? $"Terminplan {t.K.Name}" : $"Terminplan {Sprechtagdienst.LehrerTitel(s, liste[0])}";
            var pdf = PdfBericht.Erzeugen(Schule(e, p), titel, $"{t.K.Name} · {Sprechtagdienst.DatumText(t)}", spalten, zeilen,
                leer == true ? "Freie Zeilen: Erziehungsberechtigte ohne Termin können sich hier spontan eintragen." : null, b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login, false,
                PdfBericht.Hochformat(ausrichtung, !alle));
            return Results.File(pdf, "application/pdf", $"sprechtag-{t.Id}{(alle ? "" : "-" + liste[0].ToLowerInvariant())}.pdf");
        });

        // Eigene bestätigte Termine der Erziehungsberechtigten (hoch)
        api.MapGet("/{id:int}/elternplan.pdf", (int id, string? ausrichtung, HttpContext c, Sprechtagdienst d, Paketdienst p, Einstellungen e) =>
        {
            var b = Ich(c);
            if (b.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Nur für Erziehungsberechtigte.");
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var raeume = d.Raeume(t.Id);
            var zeilen = d.Termine(t.Id).Where(x => x.Status == "bestaetigt" && x.HatZeit && b.SchuelerIds.Contains(x.SchuelerId))
                .OrderBy(x => t.K.Abschnitte[x.Abschnitt].Datum, StringComparer.Ordinal).ThenBy(x => x.Beginn, StringComparer.Ordinal).Select(x =>
                {
                    var k = s?.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
                    var ort = t.K.Online ? (x.Link.Length > 0 ? $"online: {x.Link}" : "online") : d.Raum(raeume, x.Lehrer, x.Abschnitt) is { Length: > 0 } r ? r : "Unbekannter Raum";
                    return (IReadOnlyList<string>)new List<string> { $"{Wochentag(t.K.Abschnitte[x.Abschnitt].Datum)}, {Datum(t.K.Abschnitte[x.Abschnitt].Datum)}", $"{x.Beginn}–{x.Ende}",
                        Sprechtagdienst.LehrerTitel(s, x.Lehrer), ort, k is null ? "" : $"{k.Vorname} {k.Name}", k?.Klasse ?? "" };
                }).ToList();
            var pdf = PdfBericht.Erzeugen(Schule(e, p), "Meine Termine am Sprechtag", $"{t.K.Name} · {Sprechtagdienst.DatumText(t)}",
                new[] { "Datum", "Zeit", "Lehrkraft", "Raum", "Kind", "Klasse" }, zeilen, null, b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login, false, PdfBericht.Hochformat(ausrichtung, true));
            return Results.File(pdf, "application/pdf", $"sprechtag-{t.Id}-termine.pdf");
        });

        api.MapPost("/{id:int}/buchen", (int id, BuchDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            if (b.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Die Buchung steht Erziehungsberechtigten zur Verfügung.");
            var t = Sichtbar(d, b, id);
            var x = d.Buchen(p.Aktuell, t, b, (daten.Lehrer ?? "").Trim(), daten.Abschnitt, daten.Beginn ?? "", daten.SchuelerId, daten.EinladungId);
            return Results.Json(new { x.Id });
        });
    }
}
