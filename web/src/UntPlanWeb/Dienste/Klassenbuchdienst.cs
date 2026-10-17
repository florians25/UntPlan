using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Klassenbucheintrag(int Id, string Datum, int Stunde, int Unterricht, List<string> Klassen, string Fach, string Lehrer,
    string Thema, string Hausaufgabe, string? Faellig, string Notiz, string Geaendert, string? Anwesenheit = null, string NotizSchueler = "", bool Online = false)
{
    /// <summary>Lehrform zum Lehrstoff (Dialog „Lehrstoff“).</summary>
    public string Lehrform { get; init; } = "";
    /// <summary>Anmerkung zum Lehrstoff (Dialog „Lehrstoff“).</summary>
    public string Anmerkung { get; init; } = "";
    /// <summary>Wer zuletzt geändert hat (Lehrerkürzel bzw. Anmeldename).</summary>
    public string GeaendertVon { get; init; } = "";
    /// <summary>Link zum Online-Unterricht (Videokonferenz, http/https) – gilt, solange „Findet online statt“ eingeschaltet ist.</summary>
    public string OnlineLink { get; init; } = "";
    /// <summary>
    /// Einzelstunde eines Blocks, für die der Eintrag allein gilt (dann <see cref="Stunde"/> = diese Stunde); null = Eintrag des ganzen Blocks
    /// (gilt für alle Stunden des Blocks, <see cref="Stunde"/> = Blockbeginn).
    /// </summary>
    public int? Einzelstunde { get; init; }
    /// <summary>Stundennummer in der Tabelle (Einzelstunde negativ, siehe <see cref="Klassenbuchdienst.Schluessel"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int SpeicherStunde => Einzelstunde is int n ? -n : Stunde;
}

/// <summary>Fehlzeit einer Schülerin/eines Schülers in einer Stunde. Art „fehlt“, „verspaetet“ oder „vorzeitig“, Status offen/entschuldigt/unentschuldigt.</summary>
public sealed record Fehlzeit(int Id, string Datum, int Stunde, int Unterricht, int SchuelerId, string Klasse, string Fach, string Art, int Minuten,
    string Status, string Bemerkung, string Lehrer, string Erstellt, string Grund = "", string Gruppe = "")
{
    /// <summary>Art des Nachweises: attest, schriftlich, muendlich, telefonisch, email (leer = keiner).</summary>
    public string Nachweis { get; init; } = "";
    /// <summary>Datum, an dem die Entschuldigung eingegangen ist (yyyy-MM-dd).</summary>
    public string EntschuldigtAm { get; init; } = "";
    /// <summary>Eltern bzw. Erziehungsberechtigte wurden informiert.</summary>
    public bool ElternInformiert { get; init; }
    /// <summary>Interne Notiz der Klassenleitung (nicht für Schüler*innen sichtbar).</summary>
    public string NotizIntern { get; init; } = "";
    /// <summary>Minutengenau: abwesend ab (HH:mm) innerhalb der Stunde; leer = ab Stundenbeginn bzw. aus Art und Minuten abgeleitet.</summary>
    public string Beginn { get; init; } = "";
    /// <summary>Minutengenau: abwesend bis (HH:mm) innerhalb der Stunde; leer = bis Stundenende bzw. aus Art und Minuten abgeleitet.</summary>
    public string Ende { get; init; } = "";
}

/// <summary>Vermerk im Klassenbuch (zur Klasse oder zu einzelnen Schülern), z. B. Ordnungsmaßnahme, Elterngespräch, Lob.</summary>
public sealed record Vermerk(int Id, string Datum, string Klasse, int? SchuelerId, string Art, string Text, string Lehrer, string Erstellt)
{
    /// <summary>Uhrzeit des Eintrags (Dialog „Klassenbucheintrag“), leer = Erstellungszeit.</summary>
    public string Zeit { get; init; } = "";
    /// <summary>Konto, das den Eintrag angelegt hat (0 = unbekannt, ältere Einträge).</summary>
    public int BenutzerId { get; init; }
}

/// <summary>
/// Anwesenheitseintrag beim Speichern (nur Abweichungen: fehlt, verspätet oder vorzeitig gegangen; RestTag = für den Rest des Tages abwesend).
/// Von/Bis (HH:mm): minutengenau abwesend innerhalb des Blocks – haben Vorrang vor Art und Minuten.
/// </summary>
public sealed record AnwesenheitEintrag(int SchuelerId, string Art, int Minuten, string? Bemerkung, string? Grund = null, bool? RestTag = null, string? Von = null, string? Bis = null);

/// <summary>
/// Klassenbuch: je gehaltener Stunde Unterrichtsinhalt, Hausaufgabe (mit Fälligkeit) und interne Notiz.
/// <list type="bullet">
/// <item>Eintragen darf die unterrichtende Lehrkraft; Schulleitung und Administration dürfen alles.</item>
/// <item>Klassenkonten sehen Inhalte und Hausaufgaben ihrer Klasse, aber keine Notizen.</item>
/// </list>
/// </summary>
public sealed partial class Klassenbuchdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Plandienst _plan;

    public Klassenbuchdienst(Db db, Uhr uhr, Plandienst plan) { _db = db; _uhr = uhr; _plan = plan; }

    private static Klassenbucheintrag Lesen(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("datum"), Math.Abs(r.Zahl("stunde")), r.Zahl("unterricht"),
        JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new(), r.Text("fach"), r.Text("lehrer"), r.Text("thema"), r.Text("hausaufgabe"),
        r.TextOderNull("faellig"), r.Text("notiz"), r.Text("geaendert"), r.TextOderNull("anwesenheit"), r.Text("notiz_schueler"), r.Wahr("online"))
    {
        Lehrform = r.Text("lehrform"), Anmerkung = r.Text("anmerkung"), GeaendertVon = r.Text("lehrer"), OnlineLink = r.Text("online_link"),
        Einzelstunde = r.Zahl("stunde") < 0 ? -r.Zahl("stunde") : null,
    };

    // ------------------------------------------------------------------------------------------ Einzelstunden in Blöcken
    //
    // Einträge (Lehrstoff, Notizen, Online, Hausaufgaben, Abwesenheitskontrolle) gehören standardmäßig zum ganzen Block (Doppel-/Blockstunde)
    // und liegen unter der Nummer des Blockbeginns. Für eine Einzelstunde eines Blocks liegen sie unter der negativen Stundennummer
    // (−4 = nur 4. Stunde) – ohne Schemaänderung und ohne Konflikt mit dem Blockeintrag (UNIQUE datum, stunde, unterricht, lehrer).
    // Abfragen über Stundenbereiche (stunde BETWEEN von AND bis) sehen deshalb weiterhin nur Blockeinträge; Fehlzeiten liegen ohnehin je Stunde.

    /// <summary>Stundennummer, unter der Einträge zur Karte gespeichert werden: Blockbeginn bzw. −Einzelstunde.</summary>
    public static int Schluessel(Karte k) => k.Einzelstunde is int n ? -n : k.Von;

    /// <summary>
    /// Karte auf eine Einzelstunde des Blocks einschränken (Von = Bis = n, Zeiten der Stunde). Ohne Angabe bzw. bei einer Einzelstunde
    /// (Von = Bis) bleibt die Karte unverändert.
    /// </summary>
    public static Karte Einzeln(Stand s, Karte k, int? einzelstunde)
    {
        if (einzelstunde is not int n || k.Von == k.Bis) return k;
        if (n < k.Von || n > k.Bis) throw new ArgumentException("Die Einzelstunde gehört nicht zu diesem Block.");
        var (b, e) = StundenZeit(s, n);
        return k with { Von = n, Bis = n, Beginn = Hhmm(b), Ende = Hhmm(e), Einzelstunde = n };
    }

    /// <summary>Block der Karte mit seinen Einzelstunden (für die Auswahl „Ganzer Block“ / „3. Stunde 09:35–10:20“ in den Stundendetails).</summary>
    public static object Block(Stand s, Karte k) => new
    {
        von = k.Von, bis = k.Bis,
        beginn = k.Beginn.Length > 0 ? k.Beginn : Hhmm(StundenZeit(s, k.Von).Beginn),
        ende = k.Ende.Length > 0 ? k.Ende : Hhmm(StundenZeit(s, k.Bis).Ende),
    };

    /// <summary>Karte (Block) einer Stunde für die Stundendetails bzw. die App-API.</summary>
    public Karte? Blockkarte(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string? lehrerHinweis = null) => KarteZu(b, s, d, stunde, unterricht, lehrerHinweis);

    private static Fehlzeit LesenFehlzeit(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("datum"), r.Zahl("stunde"), r.Zahl("unterricht"),
        r.Zahl("schueler_id"), r.Text("klasse"), r.Text("fach"), r.Text("art"), r.Zahl("minuten"), r.Text("status"), r.Text("bemerkung"), r.Text("lehrer"), r.Text("erstellt"),
        r.Text("grund"), r.Text("gruppe"))
    {
        Nachweis = r.Text("nachweis"), EntschuldigtAm = r.Text("entschuldigt_am"), ElternInformiert = r.Zahl("eltern_informiert") != 0, NotizIntern = r.Text("notiz_intern"),
        Beginn = r.Text("beginn"), Ende = r.Text("ende"),
    };

    internal static Fehlzeit FehlzeitLesen(System.Data.Common.DbDataReader r) => LesenFehlzeit(r);

    private static Vermerk LesenVermerk(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("datum"), r.Text("klasse"), r.ZahlOderNull("schueler_id"),
        r.Text("art"), r.Text("text"), r.Text("lehrer"), r.Text("erstellt")) { Zeit = r.Text("zeit"), BenutzerId = r.ZahlOderNull("benutzer_id") ?? 0 };

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    public static readonly string[] VermerkArten = { "Vermerk", "Ordnungsmaßnahme", "Elterngespräch", "Lob", "Material vergessen", "Hausaufgabe fehlt" };

    /// <summary>Einträge im Zeitraum – ohne <paramref name="mitEinzelstunden"/> nur Blockeinträge (wie vor den Einzelstunden: Listen, Berichte, Abgleich mit UntPlan).</summary>
    public List<Klassenbucheintrag> Zeitraum(DateOnly von, DateOnly bis, bool mitEinzelstunden = false) =>
        _db.Liste("SELECT * FROM klassenbuch WHERE datum >= @v AND datum <= @b AND (@e = 1 OR stunde >= 0) ORDER BY datum, stunde", Lesen,
            Db.P("@v", von), Db.P("@b", bis), Db.P("@e", mitEinzelstunden ? 1 : 0)).OrderBy(e => e.Datum, StringComparer.Ordinal).ThenBy(e => e.Stunde).ThenBy(e => e.Einzelstunde.HasValue).ToList();

    /// <summary>Einzelstunden-Einträge eines Blocks (Unterricht, Tag, Stunden von–bis).</summary>
    public List<Klassenbucheintrag> EinzelEintraege(DateOnly d, int unterricht, int von, int bis, string? lehrer = null) =>
        _db.Liste("SELECT * FROM klassenbuch WHERE datum = @d AND unterricht = @u AND stunde <= @v AND stunde >= @b ORDER BY stunde DESC", Lesen,
                Db.P("@d", d), Db.P("@u", unterricht), Db.P("@v", -von), Db.P("@b", -bis))
            .Where(e => lehrer is null || Ci.Equals(e.Lehrer, lehrer)).ToList();

    /// <summary>Einträge einer Klasse (neueste zuerst).</summary>
    public List<Klassenbucheintrag> Klasse(string klasse, DateOnly von, DateOnly bis) =>
        Zeitraum(von, bis, true).Where(e => e.Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase)).OrderByDescending(e => e.Datum).ThenByDescending(e => e.Stunde).ToList();

    /// <summary>Offene Hausaufgaben einer Klasse (fällig ab heute bzw. in den letzten 14 Tagen erteilt).</summary>
    public List<Klassenbucheintrag> Hausaufgaben(string klasse)
    {
        var heute = _uhr.Heute;
        return Zeitraum(heute.AddDays(-30), heute.AddDays(60))
            .Where(e => e.Hausaufgabe.Length > 0 && e.Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase) &&
                        (e.Faellig is string f ? DateOnly.Parse(f) >= heute : DateOnly.Parse(e.Datum) >= heute.AddDays(-14)))
            .OrderBy(e => e.Faellig ?? e.Datum).ToList();
    }

    public Klassenbucheintrag? Holen(DateOnly d, int stunde, int unterricht, string lehrer) =>
        _db.Einzel("SELECT * FROM klassenbuch WHERE datum = @d AND stunde = @s AND unterricht = @u AND lehrer = @l", Lesen,
            Db.P("@d", d), Db.P("@s", stunde), Db.P("@u", unterricht), Db.P("@l", lehrer));

    /// <summary>Darf der Benutzer zu dieser Stunde schreiben? Lehrkraft muss die Stunde laut Plan halten.</summary>
    public bool DarfSchreiben(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht)
    {
        if (Rechtedienst.Darf(b, Recht.KlassenbuchAlle)) return true;
        if (b.LehrerKuerzel is not string l || !Rechtedienst.Darf(b, Recht.KlassenbuchEigene)) return false;
        return _plan.Karten(s, d, Elementtyp.Lehrer, l).Any(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis && k.Status is not ("entfall" or "abwesend"));
    }

    public Klassenbucheintrag Speichern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string thema, string hausaufgabe, DateOnly? faellig, string notiz, string? notizSchueler = null,
        string? lehrform = null, string? anmerkung = null, string? lehrerHinweis = null, int? einzelstunde = null)
    {
        if (!DarfSchreiben(b, s, d, stunde, unterricht)) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        // keine zeitliche Begrenzung: Lehrstoff, Notizen und „Findet online statt“ auch weit im Voraus (Stunde laut Plan vorausgesetzt)
        if (faellig is DateOnly f && f < d) throw new ArgumentException("Die Hausaufgabe kann nicht vor der Stunde fällig sein.");
        var karte = KarteZu(b, s, d, stunde, unterricht, lehrerHinweis, einzelstunde);
        if (karte is null) throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var schluessel = Schluessel(karte);
        // Schulleitung/Administration trägt für die Lehrkraft der Karte ein (bei Kopplungen die gewählte)
        var lehrer = b.LehrerKuerzel ?? (!string.IsNullOrEmpty(lehrerHinweis) && karte.Lehrer.Any(w => Ci.Equals(w.K, lehrerHinweis)) ? lehrerHinweis : karte.Lehrer.FirstOrDefault()?.K) ?? "";
        var klassen = JsonSerializer.Serialize(karte.Klassen.Select(k => k.K).ToList());
        var vorher = Holen(d, schluessel, unterricht, lehrer);
        // Klassenbuch-Einstellungen: Eingabezeitraum, Lehrstoff (Tage zurück, Zukunft) – Standard unbegrenzt
        var einst = Einstellungen();
        einst.EingabePruefen(b, d, _uhr.Heute);
        if ((thema ?? "").Trim().Length > 0 && (thema ?? "").Trim() != (vorher?.Thema ?? "")) einst.LehrstoffPruefen(b, d, _uhr.Heute);
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, thema, hausaufgabe, faellig, notiz, geaendert, benutzer_id, notiz_schueler)
            VALUES (@d, @s, @u, @k, @f, @l, @t, @h, @fa, @n, @g, @b, COALESCE(@ns, ''))
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET thema = excluded.thema, hausaufgabe = excluded.hausaufgabe, faellig = excluded.faellig,
                notiz = excluded.notiz, notiz_schueler = CASE WHEN @ns IS NULL THEN klassenbuch.notiz_schueler ELSE excluded.notiz_schueler END, klassen = excluded.klassen, fach = excluded.fach, geaendert = excluded.geaendert, benutzer_id = excluded.benutzer_id
            """, Db.P("@d", d), Db.P("@s", schluessel), Db.P("@u", unterricht), Db.P("@k", klassen), Db.P("@f", karte.Fach), Db.P("@l", lehrer),
            Db.P("@t", (thema ?? "").Trim()), Db.P("@h", (hausaufgabe ?? "").Trim()), Db.P("@fa", faellig), Db.P("@n", (notiz ?? "").Trim()), Db.P("@ns", notizSchueler?.Trim()),
            Db.P("@g", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
        // Einzelne Hausaufgabe über das alte Feld (z. B. UntPlanMobile): Liste der Hausaufgaben angleichen
        if ((vorher?.Hausaufgabe ?? "") != (hausaufgabe ?? "").Trim() || vorher?.Faellig != faellig?.ToString("yyyy-MM-dd"))
            HausaufgabeAusFeld(b, karte, d, lehrer, (hausaufgabe ?? "").Trim(), faellig);
        // Lehrform und Anmerkung (nur wenn mitgeschickt)
        if (lehrform != null || anmerkung != null)
            _db.Ausfuehren("UPDATE klassenbuch SET lehrform = COALESCE(@lf, lehrform), anmerkung = COALESCE(@an, anmerkung) WHERE datum = @d AND stunde = @s AND unterricht = @u AND lehrer = @l",
                Db.P("@lf", lehrform?.Trim()), Db.P("@an", anmerkung?.Trim()), Db.P("@d", d), Db.P("@s", schluessel), Db.P("@u", unterricht), Db.P("@l", lehrer));
        return Holen(d, schluessel, unterricht, lehrer)!;
    }

    /// <summary>Gehaltene Stunden einer Lehrkraft ohne Unterrichtsinhalt (standardmäßig letzte 14 Tage bis heute).</summary>
    public List<object> OffeneStunden(Stand s, string lehrer, int tage = 14)
    {
        var heute = _uhr.Heute;
        tage = Math.Clamp(tage, 1, 120);
        var alle = Zeitraum(heute.AddDays(-tage), heute, true).Where(e => e.Lehrer.Equals(lehrer, StringComparison.OrdinalIgnoreCase) && e.Thema.Length > 0).ToList();
        var eintraege = alle.Where(e => e.Einzelstunde is null).Select(e => (e.Datum, e.Stunde, e.Unterricht)).ToHashSet();
        var einzeln = alle.Where(e => e.Einzelstunde is not null).Select(e => (e.Datum, e.Stunde, e.Unterricht)).ToHashSet();
        var res = new List<object>();
        for (var d = heute.AddDays(-tage); d <= heute; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            foreach (var k in _plan.Karten(s, d, Elementtyp.Lehrer, lehrer).Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert"))
            {
                if (d == heute && string.CompareOrdinal(k.Ende, _uhr.Jetzt.ToString("HH:mm")) > 0) continue;
                // Lehrstoff des Blocks oder jeder einzelnen Stunde des Blocks
                if (!eintraege.Contains((k.Datum, k.Von, k.Unterricht!.Value)) && !Enumerable.Range(k.Von, k.Bis - k.Von + 1).All(n => einzeln.Contains((k.Datum, n, k.Unterricht!.Value))))
                    res.Add(new { k.Datum, k.Von, k.Bis, k.Fach, klassen = k.Klassen.Select(x => x.K), unterricht = k.Unterricht, k.Beginn, k.Ende, k.FachName, k.Farbe, raeume = k.Raeume.Select(x => x.K) });
            }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------ Anwesenheit & Fehlzeiten

    /// <summary>Schülerliste der Klassen (alphabetisch).</summary>
    public static List<WebSchueler> Schueler(Stand s, IEnumerable<string> klassen)
    {
        var k = klassen.ToHashSet(Ci);
        var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
        return s.Schueler.Where(x => k.Contains(x.Klasse)).OrderBy(x => x.Klasse, de).ThenBy(x => x.Name, de).ThenBy(x => x.Vorname, de).ToList();
    }

    /// <summary>Teilnehmer*innen einer Stunde am Tag: Klassenzugehörigkeit an diesem Tag, Schülergruppen des Unterrichts (Abmeldungen, Gäste).</summary>
    public static List<WebSchueler> SchuelerDerStunde(Stand s, Karte karte, DateOnly d) =>
        Klassenzugehoerigkeit.DerStunde(s, karte.Unterricht, karte.Klassen.Select(k => k.K), d);

    /// <summary>Klassenleitung, Schulleitung oder Administration?</summary>
    /// <summary>Darf die Fehlzeitenverwaltung (Abwesenheiten, Fehlzeiten, Fehlzeitenberichte) nutzen: Klassenleitung mindestens einer Klasse, Schulleitung, Administration.</summary>
    public static bool FehlzeitenVerwalten(Benutzer b, Stand? s) =>
        Rechtedienst.Darf(b, Recht.KlassenbuchAlle) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle) ||
        (s != null && b.LehrerKuerzel is string l && Rechtedienst.Darf(b, Recht.KlassenbuchKlasse) && s.Paket.Elemente.Klassen.Any(k => k.Klassenleitung.Contains(l, Ci)));

    /// <remarks>Rechte: „Klassenbuch aller Klassen“ (Schreiben) bzw. „Klassenbuch der eigenen Klassen“ (Schreiben) als Klassenleitung.</remarks>
    public static bool IstKlassenleitung(Benutzer b, Stand s, string klasse) =>
        Rechtedienst.Darf(b, Recht.KlassenbuchAlle) ||
        b.LehrerKuerzel is string l && Rechtedienst.Darf(b, Recht.KlassenbuchKlasse) && s.Paket.Elemente.Klassen.Any(k => Ci.Equals(k.Kurz, klasse) && k.Klassenleitung.Contains(l, Ci));

    /// <summary>Abwesenheiten der Klasse entschuldigen: Recht „entschuldigen“ und Klassenleitung bzw. Recht „Abwesenheiten aller Klassen“.</summary>
    public static bool DarfEntschuldigen(Benutzer b, Stand s, string klasse) =>
        Rechtedienst.Darf(b, Recht.AbwesenheitEntschuldigen) && (IstKlassenleitung(b, s, klasse) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle));

    /// <summary>Abwesenheiten, die länger als <see cref="Recht.RueckwirkendTage"/> Tage zurückliegen, nur mit dem Recht „rückwirkend bearbeiten“.</summary>
    public static void RueckwirkendPruefen(Benutzer b, DateOnly datum, DateOnly heute)
    {
        if (datum < heute.AddDays(-Recht.RueckwirkendTage) && !Rechtedienst.Darf(b, Recht.AbwesenheitRueckwirkend))
            throw new UnauthorizedAccessException($"Abwesenheiten, die länger als {Recht.RueckwirkendTage} Tage zurückliegen, darf Ihr Konto nicht bearbeiten.");
    }

    private Karte? KarteZu(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string? lehrerHinweis = null, int? einzelstunde = null) =>
        (Waehlen(_plan.Karten(s, d, Elementtyp.Lehrer, b.LehrerKuerzel ?? ""), stunde, unterricht, lehrerHinweis)
         ?? Waehlen(s.Paket.Elemente.Klassen.SelectMany(k => _plan.Karten(s, d, Elementtyp.Klasse, k.Kurz)), stunde, unterricht, lehrerHinweis)) is { } gefunden
            ? Einzeln(s, gefunden, einzelstunde) : null;

    /// <summary>
    /// Karte einer Stunde wählen. Bei Kopplungen mit mehreren Lehrkräften und unterschiedlichen Blöcken (z. B. KR als Doppelstunde, ER einzeln)
    /// zuerst die Karte der angegebenen Lehrkraft, dann die Karte, die genau in dieser Stunde beginnt.
    /// </summary>
    internal static Karte? Waehlen(IEnumerable<Karte> karten, int stunde, int unterricht, string? lehrerHinweis)
    {
        var passend = karten.Where(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis).ToList();
        if (passend.Count <= 1) return passend.FirstOrDefault();
        bool Lehrer(Karte k) => !string.IsNullOrEmpty(lehrerHinweis) && k.Lehrer.Any(w => Ci.Equals(w.K, lehrerHinweis) || Ci.Equals(w.Alt, lehrerHinweis));
        return passend.OrderByDescending(Lehrer).ThenByDescending(k => k.Von == stunde).ThenByDescending(k => k.Von).First();
    }

    public List<Fehlzeit> FehlzeitenZeitraum(DateOnly von, DateOnly bis, string? klasse = null) =>
        _db.Liste("SELECT * FROM fehlzeiten WHERE datum >= @v AND datum <= @b AND (@k IS NULL OR klasse = @k COLLATE NOCASE) ORDER BY datum, stunde", LesenFehlzeit,
            Db.P("@v", von), Db.P("@b", bis), Db.P("@k", klasse));

    /// <summary>Anwesenheitsliste einer Stunde: Schüler der Klassen mit erfassten Fehlzeiten.</summary>
    public object Anwesenheit(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, int? einzelstunde = null)
    {
        var block = KarteZu(b, s, d, stunde, unterricht) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var karte = Einzeln(s, block, einzelstunde);
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        var teilnehmer = SchuelerDerStunde(s, karte, d);
        var teilnehmerIds = teilnehmer.Select(x => x.Id).ToHashSet();
        var fz = FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis && teilnehmerIds.Contains(f.SchuelerId))
            .GroupBy(f => f.SchuelerId).Select(g => Zusammenfassen(g.ToList(), s, Gesamtabwesenheit(g.ToList(), karte))).ToList();
        var eintrag = Zeitraum(d, d).FirstOrDefault(e => e.Unterricht == unterricht && e.Stunde == block.Von);
        // Kontrolle des Blocks gilt für alle Stunden; Einzelstunde: eigene Kontrolle, Block: auch wenn jede Einzelstunde kontrolliert wurde
        var einzelKontrolle = EinzelEintraege(d, unterricht, block.Von, block.Bis).Where(e => e.Anwesenheit != null).ToList();
        var geprueft = karte.Einzelstunde is int en
            ? einzelKontrolle.Where(e => e.Stunde == en).Select(e => e.Anwesenheit).Max(StringComparer.Ordinal) ?? eintrag?.Anwesenheit
            : eintrag?.Anwesenheit ?? (block.Von < block.Bis && Enumerable.Range(block.Von, block.Bis - block.Von + 1).All(n => einzelKontrolle.Any(e => e.Stunde == n))
                ? einzelKontrolle.Select(e => e.Anwesenheit).Max(StringComparer.Ordinal) : null);
        var (bb, _) = StundenZeit(s, karte.Von);
        var (_, be) = StundenZeit(s, karte.Bis);
        var einst = Einstellungen();
        return new
        {
            schueler = teilnehmer, gaeste = Klassenzugehoerigkeit.Gaeste(s, karte.Unterricht, d), fehlzeiten = fz, geprueft, vorstunden = Vorstunden(s, d, karte),
            einstellungen = einst.Hauptseite(d, s.SchuljahrVon), standardgruende = new { abwesend = einst.GrundAbwesend, spaeter = einst.GrundSpaeter, frueher = einst.GrundFrueher },
            weitere = einst.AlleAbwesenheitenDesTages ? UebrigeDesTages(d, karte, klassen) : new List<Fehlzeit>(),
            einzelstunde = karte.Einzelstunde, block = Block(s, block),
            darf = DarfSchreiben(b, s, d, stunde, unterricht) || klassen.Any(k => IstKlassenleitung(b, s, k)),
            // Klassenleitung: Status und Zeiträume über den Tag hinaus im Abwesenheitsdialog
            verwalten = klassen.Any(k => IstKlassenleitung(b, s, k)),
            beginn = karte.Beginn.Length > 0 ? karte.Beginn : Hhmm(bb), ende = karte.Ende.Length > 0 ? karte.Ende : Hhmm(be),
            regeln = Regeln(), laengen = Enumerable.Range(karte.Von, karte.Bis - karte.Von + 1).Select(n => StundenLaenge(s, n)).ToList(),
        };
    }

    /// <summary>Anwesenheit speichern: ersetzt die Fehlzeiten der Stunde (Entschuldigungsstatus bleibt erhalten) und markiert die Stunde als geprüft.</summary>
    public int AnwesenheitSpeichern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, List<AnwesenheitEintrag> eintraege, int? einzelstunde = null)
    {
        var karte = KarteZu(b, s, d, stunde, unterricht, null, einzelstunde) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        if (!(DarfSchreiben(b, s, d, stunde, unterricht) && Rechtedienst.Darf(b, Recht.AbwesenheitErfassen)) && !klassen.Any(k => IstKlassenleitung(b, s, k)))
            throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (d > _uhr.Heute) throw new ArgumentException("Fehlzeiten können nicht im Voraus erfasst werden.");
        RueckwirkendPruefen(b, d, _uhr.Heute);
        var schueler = SchuelerDerStunde(s, karte, d).ToDictionary(x => x.Id);
        var alt = FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis && schueler.ContainsKey(f.SchuelerId)).ToList();
        // Klassenbuch-Einstellungen: nur bei Änderungen (die reine Abwesenheitskontrolle bleibt immer möglich)
        var einst = Einstellungen();
        var kl = klassen.Any(k => IstKlassenleitung(b, s, k));
        var neuIds = eintraege.Where(x => schueler.ContainsKey(x.SchuelerId)).Select(x => x.SchuelerId).ToHashSet();
        var entfernt = alt.Where(f => !neuIds.Contains(f.SchuelerId)).ToList();
        if (entfernt.Count > 0 || neuIds.Any(i => alt.All(f => f.SchuelerId != i)))
        {
            einst.EingabePruefen(b, d, _uhr.Heute);
            if (!kl) NurInStundePruefen(einst, b, s, d, karte);
        }
        foreach (var f in entfernt) einst.LoeschenPruefen(b, f.Erstellt, _uhr.Jetzt);
        foreach (var sid in entfernt.Select(f => f.SchuelerId).Distinct()) BisEndeEntfernen(d, karte, sid);
        var lehrer = b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "";
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        foreach (var f in alt) _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", f.Id));
        // „Rest des Tages abwesend“ aus einer früheren Speicherung dieser Stunde zurücknehmen (wird unten ggf. neu eingetragen)
        _db.Ausfuehren("DELETE FROM fehlzeiten WHERE datum = @d AND stunde > @s AND gruppe = @g", Db.P("@d", d), Db.P("@s", karte.Bis), Db.P("@g", RestTagGruppe(karte.Unterricht ?? unterricht, karte.Von)));
        var regeln = Regeln();
        var anzahl = 0;
        foreach (var e in eintraege.Where(x => schueler.ContainsKey(x.SchuelerId)).DistinctBy(x => x.SchuelerId))
        {
            var art = e.Art is "verspaetet" or "vorzeitig" ? e.Art : "fehlt";
            var altF = alt.FirstOrDefault(f => f.SchuelerId == e.SchuelerId);
            var grund = (e.Grund ?? altF?.Grund ?? "").Trim();
            // Grund und Text gesperrt (Fachlehrkräfte): bisherige Angaben bzw. Standardgrund; neue Abwesenheit ohne Grund: Standardgrund
            var gesperrt = einst.GrundTextGesperrt && !kl && !b.IstAdmin;
            if (gesperrt) grund = altF?.Grund ?? einst.Standardgrund(art);
            else if (altF is null && grund.Length == 0) grund = einst.Standardgrund(art);
            var text = gesperrt ? (altF is null ? "" : Klartext(altF.Bemerkung)) : (e.Bemerkung ?? "").Trim();
            var statusNeu = altF is null ? StatusNeu(einst, grund) : null;
            // Minutengenau (Von/Bis): abwesend im Fenster, angebrochene Stunden als Verspätung bzw. vorzeitiges Gehen.
            // Sonst: Fehlen gilt für alle Stunden des Blocks; Verspätung beginnt mit der ersten, vorzeitiges Gehen endet mit der letzten Stunde.
            // Ganz verpasste Stunden werden Fehlstunden, ab der Schwelle der Verspätungsregeln auch die angebrochene.
            var block = Enumerable.Range(karte.Von, karte.Bis - karte.Von + 1);
            var zeilen = Uhrzeit(e.Von) is TimeOnly fv && Uhrzeit(e.Bis) is TimeOnly fb && fb > fv
                ? AusFenster(s, regeln, block, fv, fb)
                : art == "fehlt"
                    ? block.Select(n => new Abschnitt(n, "fehlt", 0, "", "", "")).ToList()
                    : MinutenVerteilen(s, regeln, karte.Von, karte.Bis, art, Math.Clamp(e.Minuten, 1, 600))
                        .Select(z => { var (fb2, fe2) = FensterAusMinuten(s, z.Stunde, z.Art, z.Minuten, z.Hinweis); return new Abschnitt(z.Stunde, z.Art, z.Minuten, fb2, fe2, z.Hinweis); }).ToList();
            foreach (var (n, a, min, fBeginn, fEnde, hinweis) in zeilen)
            {
                var status = alt.FirstOrDefault(f => f.SchuelerId == e.SchuelerId && f.Stunde == n && f.Art == a)?.Status
                             ?? alt.FirstOrDefault(f => f.SchuelerId == e.SchuelerId && f.Art == a)?.Status ?? statusNeu ?? "offen";
                if (altF != null) status = StatusNachGrund(einst, status, altF.Grund, grund);
                var bem = string.Join(" – ", new[] { hinweis, text }.Where(x => x.Length > 0));
                var altZeile = alt.FirstOrDefault(f => f.SchuelerId == e.SchuelerId && f.Stunde == n) ?? altF;
                _db.Ausfuehren("""
                    INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund, gruppe,
                        nachweis, entschuldigt_am, eltern_informiert, notiz_intern, beginn, ende)
                    VALUES (@d, @s, @u, @sid, @k, @f, @a, @m, @st, @bem, @l, @e, @b, @g, @gr, @nw, @ea, @ei, @ni, @be, @en)
                    ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET unterricht = excluded.unterricht, fach = excluded.fach, art = excluded.art,
                        minuten = excluded.minuten, bemerkung = excluded.bemerkung, lehrer = excluded.lehrer, grund = excluded.grund, beginn = excluded.beginn, ende = excluded.ende
                    """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", unterricht), Db.P("@sid", e.SchuelerId), Db.P("@k", schueler[e.SchuelerId].Klasse), Db.P("@f", karte.Fach),
                    Db.P("@a", a), Db.P("@m", min), Db.P("@st", status), Db.P("@bem", bem),
                    Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id), Db.P("@g", grund), Db.P("@gr", altF?.Gruppe ?? ""),
                    Db.P("@nw", altZeile?.Nachweis ?? ""), Db.P("@ea", altZeile?.EntschuldigtAm ?? ""), Db.P("@ei", altZeile?.ElternInformiert == true ? 1 : 0),
                    Db.P("@ni", altZeile?.NotizIntern ?? ""), Db.P("@be", fBeginn), Db.P("@en", fEnde));
            }
            if (art == "vorzeitig" && (e.RestTag ?? regeln.VorzeitigRestTag))
                RestTagEintragen(b, s, d, karte, e.SchuelerId, schueler[e.SchuelerId].Klasse, lehrer, jetzt, grund);
            // Vorgabe „Tagesende“ bzw. fixer Zeitpunkt: neue Abwesenheit gilt auch für die folgenden Stunden (dort verkürzen)
            if (art == "fehlt" && altF is null && Uhrzeit(e.Von) is null && einst.AbwesenheitEnde is "tag" or "zeit")
                BisEndeEintragen(b, s, d, karte, e.SchuelerId, schueler[e.SchuelerId].Klasse, lehrer, jetzt, grund, statusNeu ?? "offen", einst.EndeZeit, regeln);
            anzahl++;
        }
        GeprueftMarkieren(b, d, karte, unterricht, lehrer);
        return anzahl;
    }

    /// <summary>Ganztägiges Fehlen (z. B. Krankmeldung über die Klassenleitung) für alle Unterrichtsstunden der Tage eintragen.</summary>
    public int FehlenEintragen(Benutzer b, Stand s, int schuelerId, DateOnly von, DateOnly bis, string status, string bemerkung,
        string grund = "", int stundeVon = 1, int stundeBis = 99, string? nachweis = null, string? entschuldigtAm = null, bool elternInformiert = false, string? notizIntern = null)
    {
        var sch = s.Schueler.FirstOrDefault(x => x.Id == schuelerId) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");
        if (!IstKlassenleitung(b, s, sch.Klasse) && !Rechtedienst.Darf(b, Recht.AbwesenheitAlle)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann ganze Tage eintragen.");
        if (bis < von || bis.DayNumber - von.DayNumber > 60) throw new ArgumentException("Bitte einen Zeitraum von höchstens 60 Tagen wählen.");
        RueckwirkendPruefen(b, von, _uhr.Heute);
        var anzahl = 0;
        // eine Abwesenheit über alle Tage (Zeitraum), nicht je Tag
        var gruppe = GruppenPraefix + Sicherheit.Zufall(10);
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            // Erster und letzter Tag ggf. nur ab bzw. bis zu einer Stunde
            var ab = d == von ? stundeVon : 1;
            var bis2 = d == bis ? stundeBis : 99;
            foreach (var k in _plan.Karten(s, d, Elementtyp.Klasse, sch.Klasse).Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung")))
                for (var n = Math.Max(k.Von, ab); n <= Math.Min(k.Bis, bis2); n++)
                {
                    _db.Ausfuehren("""
                        INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund,
                            nachweis, entschuldigt_am, eltern_informiert, notiz_intern, gruppe)
                        VALUES (@d, @s, @u, @sid, @k, @f, 'fehlt', 0, @st, @bem, @l, @e, @b, @g, @nw, @ea, @ei, @ni, @gr)
                        ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET art = 'fehlt', minuten = 0, status = excluded.status, bemerkung = excluded.bemerkung, grund = excluded.grund,
                            nachweis = excluded.nachweis, entschuldigt_am = excluded.entschuldigt_am, eltern_informiert = excluded.eltern_informiert, notiz_intern = excluded.notiz_intern,
                            gruppe = excluded.gruppe, beginn = '', ende = ''
                        """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", k.Unterricht), Db.P("@sid", schuelerId), Db.P("@k", sch.Klasse), Db.P("@f", k.Fach),
                        Db.P("@st", Status(status)), Db.P("@bem", (bemerkung ?? "").Trim()), Db.P("@l", b.LehrerKuerzel ?? ""), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id),
                        Db.P("@g", (grund ?? "").Trim()), Db.P("@nw", Klassenbuchverwaltung.Nachweis(nachweis)), Db.P("@ea", Klassenbuchverwaltung.DatumOderLeer(entschuldigtAm)),
                        Db.P("@ei", elternInformiert ? 1 : 0), Db.P("@ni", (notizIntern ?? "").Trim()), Db.P("@gr", gruppe));
                    anzahl++;
                }
        }
        return anzahl;
    }

    private static string Status(string? st) => st is "entschuldigt" or "unentschuldigt" ? st : "offen";

    /// <summary>Entschuldigungsstatus setzen (Klassenleitung, Schulleitung, Administration).</summary>
    public int StatusSetzen(Benutzer b, Stand s, IEnumerable<int> ids, string status)
    {
        var n = 0;
        foreach (var id in ids.Distinct())
        {
            var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", LesenFehlzeit, Db.P("@i", id));
            if (f is null) continue;
            if (!DarfEntschuldigen(b, s, f.Klasse)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann entschuldigen.");
            if (DateOnly.TryParse(f.Datum, out var fd)) RueckwirkendPruefen(b, fd, _uhr.Heute);
            n += _db.Ausfuehren("UPDATE fehlzeiten SET status = @s WHERE id = @i", Db.P("@s", Status(status)), Db.P("@i", id));
        }
        return n;
    }

    public bool FehlzeitLoeschen(Benutzer b, Stand s, int id)
    {
        var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", LesenFehlzeit, Db.P("@i", id));
        if (f is null) return false;
        if (!IstKlassenleitung(b, s, f.Klasse) && !Ci.Equals(f.Lehrer, b.LehrerKuerzel ?? "")) throw new UnauthorizedAccessException("Keine Berechtigung.");
        Einstellungen().LoeschenPruefen(b, f.Erstellt, _uhr.Jetzt);
        return _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", id)) > 0;
    }

    /// <summary>Fehlzeitenübersicht einer Klasse: Summen je Schüler und Einzeleinträge.</summary>
    public object Fehlzeitenuebersicht(Stand s, Benutzer b, string klasse, DateOnly von, DateOnly bis)
    {
        var liste = FehlzeitenZeitraum(von, bis, klasse);
        var schueler = Klassenzugehoerigkeit.ImZeitraum(s, new[] { klasse }, von, bis);
        var regeln = Regeln();
        var summen = schueler.Select(x =>
        {
            var f = liste.Where(y => y.SchuelerId == x.Id).ToList();
            var fehlt = f.Where(y => y.Art == "fehlt").ToList();
            var summe = Listendienst.Summe(regeln, x.Klasse, x.Id, x.Name, x.Vorname, f);
            return new
            {
                x.Id, x.Vorname, x.Name,
                stunden = fehlt.Count, tage = fehlt.Select(y => y.Datum).Distinct().Count(),
                entschuldigt = fehlt.Count(y => y.Status == "entschuldigt"), unentschuldigt = fehlt.Count(y => y.Status == "unentschuldigt"),
                offen = fehlt.Count(y => y.Status == "offen"),
                verspaetungen = f.Count(y => y.Art == "verspaetet"), minuten = f.Where(y => y.Art == "verspaetet").Sum(y => y.Minuten),
                vorzeitig = summe.Vorzeitig, vorzeitigMinuten = summe.VorzeitigMinuten, stundenAusMinuten = summe.StundenAusMinuten, hinweis = summe.Hinweis,
            };
        }).ToList();
        return new
        {
            schueler = summen, eintraege = liste.OrderByDescending(f => f.Datum).ThenBy(f => f.Stunde).ToList(), verwalten = IstKlassenleitung(b, s, klasse), regeln,
            // je Unterrichtsstunde mit Fehltagen, Fehlstunden und Fehlminuten (Fehlzeitenliste)
            zeilen = Fehlzeilen(s, liste),
        };
    }

    // ------------------------------------------------------------------------------------------ Vermerke

    public List<Vermerk> Vermerke(string klasse, DateOnly von, DateOnly bis) =>
        _db.Liste("SELECT * FROM vermerke WHERE klasse = @k COLLATE NOCASE AND datum >= @v AND datum <= @b ORDER BY datum DESC, id DESC", LesenVermerk,
            Db.P("@k", klasse), Db.P("@v", von), Db.P("@b", bis));

    public Vermerk VermerkAnlegen(Benutzer b, Stand s, DateOnly d, string klasse, int? schuelerId, string art, string text, string? zeit = null)
    {
        if (text?.Length > 1000) throw new ArgumentException("Der Klassenbucheintrag darf höchstens 1000 Zeichen lang sein.");
        if (!string.IsNullOrEmpty(zeit) && !System.Text.RegularExpressions.Regex.IsMatch(zeit, @"^\d{2}:\d{2}$")) throw new ArgumentException("Ungültige Uhrzeit.");
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur das Kollegium kann Vermerke eintragen.");
        Einstellungen().EingabePruefen(b, d, _uhr.Heute);
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (schuelerId is int sid && !s.Schueler.Any(x => x.Id == sid && Ci.Equals(x.Klasse, klasse))) throw new ArgumentException("Schüler gehört nicht zur Klasse.");
        var a = VermerkArten.FirstOrDefault(x => Ci.Equals(x, art)) ?? "Vermerk";
        var id = _db.Einfuegen("INSERT INTO vermerke (datum, klasse, schueler_id, art, text, lehrer, erstellt, benutzer_id, zeit) VALUES (@d, @k, @s, @a, @t, @l, @e, @b, @z)", Db.P("@d", d), Db.P("@k", s.Klassen[klasse].Kurz), Db.P("@s", schuelerId), Db.P("@a", a), Db.P("@t", text!.Trim()),
            Db.P("@l", b.LehrerKuerzel ?? b.Anzeigename), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id), Db.P("@z", zeit ?? ""));
        return _db.Einzel("SELECT * FROM vermerke WHERE id = @i", LesenVermerk, Db.P("@i", id))!;
    }

    public Vermerk? VermerkLesen(int id) => _db.Einzel("SELECT * FROM vermerke WHERE id = @i", LesenVermerk, Db.P("@i", id));

    /// <summary>
    /// Rechte an einem Klassenbucheintrag (Detaildialog): Bearbeiten für den Verfasser bzw. die Klassenleitung (Recht „Klassenbuch aller Klassen“
    /// eingeschlossen) – Löschen zusätzlich nur innerhalb der Löschsperre der Klassenbuch-Einstellungen.
    /// </summary>
    public (bool Bearbeiten, bool Loeschen) VermerkRechte(Benutzer b, Stand s, Vermerk v)
    {
        if (!b.Kollegium) return (false, false);
        var darf = b.IstAdmin || (v.BenutzerId != 0 && v.BenutzerId == b.Id) || IstKlassenleitung(b, s, v.Klasse);
        if (!darf) return (false, false);
        try { Einstellungen().LoeschenPruefen(b, v.Erstellt, _uhr.Jetzt); return (true, true); }
        catch (UnauthorizedAccessException) { return (true, false); }
    }

    /// <summary>Klassenbucheintrag ändern (Datum, Uhrzeit, betroffene Schüler*in, Art, Text) – Verfasser bzw. Klassenleitung.</summary>
    public Vermerk VermerkAendern(Benutzer b, Stand s, int id, DateOnly d, int? schuelerId, string art, string text, string? zeit = null)
    {
        var v = VermerkLesen(id) ?? throw new ArgumentException("Der Klassenbucheintrag existiert nicht mehr.");
        if (!VermerkRechte(b, s, v).Bearbeiten) throw new UnauthorizedAccessException("Keine Berechtigung.");
        if (text?.Length > 1000) throw new ArgumentException("Der Klassenbucheintrag darf höchstens 1000 Zeichen lang sein.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (!string.IsNullOrEmpty(zeit) && !System.Text.RegularExpressions.Regex.IsMatch(zeit, @"^\d{2}:\d{2}$")) throw new ArgumentException("Ungültige Uhrzeit.");
        Einstellungen().EingabePruefen(b, d, _uhr.Heute);
        if (schuelerId is int sid && !s.Schueler.Any(x => x.Id == sid && Ci.Equals(x.Klasse, v.Klasse))) throw new ArgumentException("Schüler gehört nicht zur Klasse.");
        var a = VermerkArten.FirstOrDefault(x => Ci.Equals(x, art)) ?? v.Art;
        _db.Ausfuehren("UPDATE vermerke SET datum = @d, schueler_id = @s, art = @a, text = @t, zeit = @z WHERE id = @i",
            Db.P("@d", d), Db.P("@s", schuelerId), Db.P("@a", a), Db.P("@t", text!.Trim()), Db.P("@z", zeit ?? v.Zeit), Db.P("@i", id));
        return VermerkLesen(id)!;
    }

    public bool VermerkLoeschen(Benutzer b, Stand s, int id)
    {
        var v = _db.Einzel("SELECT * FROM vermerke WHERE id = @i", LesenVermerk, Db.P("@i", id));
        if (v is null) return false;
        var eigen = Convert.ToInt64(_db.Wert("SELECT COALESCE(benutzer_id, 0) FROM vermerke WHERE id = @i", Db.P("@i", id)) ?? 0L) == b.Id;
        if (!eigen && !IstKlassenleitung(b, s, v.Klasse)) throw new UnauthorizedAccessException("Keine Berechtigung.");
        Einstellungen().LoeschenPruefen(b, v.Erstellt, _uhr.Jetzt);
        return _db.Ausfuehren("DELETE FROM vermerke WHERE id = @i", Db.P("@i", id)) > 0;
    }

    // ------------------------------------------------------------------------------------------ Wochenansicht

    /// <summary>Klassenbuch einer Woche: je Tag die Stunden der Klasse mit Inhalt, Hausaufgabe, Anwesenheitsprüfung und Fehlzeiten.</summary>
    public object Woche(Stand s, string klasse, DateOnly montag)
    {
        var bis = montag.AddDays(Math.Max(5, s.Paket.Zeitraster.TageProWoche) - 1);
        var eintraege = Zeitraum(montag, bis).Where(e => e.Klassen.Contains(klasse, Ci)).ToList();
        var fz = FehlzeitenZeitraum(montag, bis, klasse);
        var vermerke = Vermerke(klasse, montag, bis);
        var namen = s.Schueler.ToDictionary(x => x.Id, x => $"{x.Name}, {x.Vorname}");
        var tage = new List<object>();
        for (var d = montag; d <= bis; d = d.AddDays(1))
        {
            var iso = Plandienst.Iso(d);
            var frei = _plan.Frei(s, d);
            var stunden = frei.Frei ? new List<object>() : _plan.Karten(s, d, Elementtyp.Klasse, klasse).Where(k => k.Unterricht.HasValue || k.Status == "entfall")
                .Select(k =>
                {
                    var e = eintraege.FirstOrDefault(x => x.Datum == iso && x.Unterricht == k.Unterricht && x.Stunde == k.Von);
                    var f = fz.Where(x => x.Datum == iso && x.Stunde >= k.Von && x.Stunde <= k.Bis).ToList();
                    return (object)new
                    {
                        k.Von, k.Bis, k.Beginn, k.Ende, k.Fach, k.FachName, k.Farbe, k.Status, k.Art, k.Unterricht,
                        lehrer = k.Lehrer.Select(x => x.K), raeume = k.Raeume.Select(x => x.K),
                        thema = e?.Thema ?? "", hausaufgabe = e?.Hausaufgabe ?? "", faellig = e?.Faellig, geprueft = e?.Anwesenheit,
                        fehlend = f.Where(x => x.Art == "fehlt").Select(x => x.SchuelerId).Distinct().Select(i => namen.GetValueOrDefault(i, "?")).ToList(),
                        verspaetet = f.Where(x => x.Art == "verspaetet").Select(x => $"{namen.GetValueOrDefault(x.SchuelerId, "?")} ({x.Minuten} min)").ToList(),
                        vorzeitig = f.Where(x => x.Art == "vorzeitig").Select(x => $"{namen.GetValueOrDefault(x.SchuelerId, "?")} ({x.Minuten} min)").ToList(),
                        // Wochenraster: Hausaufgaben der Stunde und Klassenbucheinträge (Vermerke) mit Uhrzeit innerhalb der Stunde
                        hausaufgaben = k.Unterricht is int u ? HausaufgabenDerStunde(d, k.Von, k.Bis, u).Select(h => h.Text).ToList() : new List<string>(),
                        eintraege = vermerke.Count(v => v.Datum == iso && v.Zeit.Length == 5 && string.CompareOrdinal(v.Zeit, k.Beginn) >= 0 && string.CompareOrdinal(v.Zeit, k.Ende) < 0),
                    };
                }).ToList();
            tage.Add(new { datum = iso, frei = frei.Frei, freiGrund = frei.Grund, stunden, vermerke = vermerke.Where(v => v.Datum == iso).Select(v => new { v.Id, v.Art, v.Text, v.Lehrer, v.Zeit, schueler = v.SchuelerId is int i ? namen.GetValueOrDefault(i, "") : "" }) });
        }
        return new
        {
            klasse, von = Plandienst.Iso(montag), bis = Plandienst.Iso(bis), tage, schuelerAnzahl = s.Schueler.Count(x => Ci.Equals(x.Klasse, klasse)),
            raster = s.Paket.Zeitraster.Stunden.OrderBy(z => z.Nr).Select(z => new { nr = z.Nr, beginn = z.Beginn, ende = z.Ende }),
        };
    }
}
