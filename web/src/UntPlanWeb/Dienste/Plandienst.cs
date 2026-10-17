using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>Ein angezeigter Wert (Klasse, Lehrkraft, Raum); Alt = ursprünglicher Wert (durchgestrichen), Neu = geändert.</summary>
public sealed record Wert(string K, string? Alt = null, bool Neu = false)
{
    /// <summary>Hinzugefügtes Element (z. B. zusätzlicher Raum): neu, ohne ersetzten Wert – Anzeige „+B070“.</summary>
    public bool Plus => Neu && K.Length > 0 && string.IsNullOrEmpty(Alt);
}

/// <summary>Eine Seite einer Verlegung (Ursprung bzw. Ziel): Datum, Beginn/Ende des Blocks und dessen Stunden.</summary>
public sealed record Verlegungszeit(string Datum, string Beginn, string Ende, int Stunde, int BisStunde);

/// <summary>
/// Stundenkarte im Plan.
/// Status:
/// <list type="bullet">
/// <item>regulaer</item>
/// <item>geaendert (Vertretung, Raum- oder Fachänderung, zusätzliche Stunde)</item>
/// <item>offen (Vertretung noch ohne Lehrkraft)</item>
/// <item>entfall</item>
/// <item>abwesend (eigene Stunde wird von anderen übernommen; Lehrer- bzw. Raumplan)</item>
/// <item>klausur</item>
/// <item>bereitschaft</item>
/// <item>buchung (Raumbuchung)</item>
/// <item>vorgemerkt (in UntPlanWeb beantragt, von UntPlan noch nicht übernommen)</item>
/// </list>
/// </summary>
public sealed record Karte(
    string Datum, int Von, int Bis, string Beginn, string Ende, string Status,
    string Fach, string? FachAlt, string FachName, string Farbe,
    List<Wert> Klassen, List<Wert> Lehrer, List<Wert> Raeume,
    string Art, string Text, int? Vertretung, int? Unterricht, string Gruppe)
{
    /// <summary>Notizen für Schüler*innen aus dem Klassenbuch (Info-Symbol unten rechts auf der Karte).</summary>
    public string NotizSchueler { get; init; } = "";
    /// <summary>Notizen für Lehrkräfte (nur Kollegium).</summary>
    public string NotizLehrer { get; init; } = "";
    /// <summary>Zu dieser Stunde ist eine Hausaufgabe fällig (Hausaufgaben-Symbol auf der Karte).</summary>
    public bool HausaufgabeFaellig { get; init; }
    /// <summary>Die Stunde findet online statt (Klassenbuch, Schalter „Findet online statt“).</summary>
    public bool Online { get; init; }
    /// <summary>Link zum Online-Unterricht (Videokonferenz, http/https), nur bei <see cref="Online"/>; sonst leer.</summary>
    public string OnlineLink { get; init; } = "";
    /// <summary>Zusätzlicher Unterricht (Stunde nicht im Regelplan) – Badge „Zusätzl. Unterrichtsstunde“, nie ein Vertretungstext.</summary>
    public bool Zusaetzlich { get; init; }
    /// <summary>
    /// Verlegung: "von" = ursprüngliche Stunde (von hier weg verlegt; Symbol Pfeil nach rechts), "nach" = Zielstunde (hierher verlegt;
    /// Symbol Pfeil nach links), null = keine Verlegung. Aus der Vertretungsart („Verlegung“, „Statt-Vertretung“), nie bei Prüfungen.
    /// </summary>
    public string? Verlegung => !(IstVerlegungsArt(Art) || VerlegungsZeile) || Status == "klausur" ? null
        : Status == "entfall" || (Status == "abwesend" && !LehrerAbwesend) ? "von" : "nach";

    /// <summary>Die Vertretung gehört zu einer Verlegung (Desktop: „verlegt auf“ an der entfallenen Ursprungsstunde, „verlegt von“ am Ziel).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool VerlegungsZeile { get; init; }
    /// <summary>Verlegung: ursprüngliche Stunde (Datum, Zeiten) – bei <see cref="Verlegung"/> gesetzt, sonst null (Gegenseite unbekannt: null).</summary>
    public Verlegungszeit? VerlegungVon { get; init; }
    /// <summary>Verlegung: Zielstunde (Datum, Zeiten) – siehe <see cref="VerlegungVon"/>.</summary>
    public Verlegungszeit? VerlegungNach { get; init; }

    /// <summary>Zusätzliche Stunde (Marker „+“): Veranstaltung, zusätzlicher Unterricht, Sondereinsatz, Mitbetreuung – nicht bei Verlegungen.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool IstZusatz => Status == "veranstaltung" || (Status == "geaendert" && Verlegung is null &&
        (Zusaetzlich || System.Text.RegularExpressions.Regex.IsMatch(Art ?? "", "sondereinsatz|zusatz|mitbetreuung", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));

    /// <summary>Ein Wert der Stunde hat sich geändert (Fach alt → neu, Wert ersetzt, hinzugefügt oder entfernt).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool WertGeaendert => !string.IsNullOrEmpty(FachAlt) ||
        Klassen.Concat(Lehrer).Concat(Raeume).Concat(Mitklassen).Any(w => !string.IsNullOrEmpty(w.Alt) || w.Plus || (w.K.Length == 0 && w.Neu));

    /// <summary>
    /// Wie <see cref="WertGeaendert"/>, bei einer zusätzlichen Unterrichtsstunde (Marker „+“, keine Verlegung) aber ohne die Lehrkräfte: Dort stehen alle beteiligten
    /// Lehrkräfte als „+“-Label (hinzugekommen) – das ist keine Änderung eines Werts.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool ZusatzWertGeaendert => !string.IsNullOrEmpty(FachAlt) ||
        Klassen.Concat(Raeume).Concat(Mitklassen).Concat(Zusaetzlich ? new List<Wert>() : Lehrer)
            .Any(w => !string.IsNullOrEmpty(w.Alt) || w.Plus || (w.K.Length == 0 && w.Neu));

    /// <summary>
    /// Symbole oben rechts, untereinander in dieser Reihenfolge: verlegungVon (rot, Pfeil), verlegungNach (grün, Pfeil), entfall
    /// (rotes Dreieck), pruefung (gelber Kreis), plus (grünes „+“), aenderung (grünes Quadrat). Prüfung, Zusatzstunde und Zielstunde
    /// einer Verlegung bekommen zusätzlich „aenderung“, wenn sich ein Wert geändert hat (z. B. Prüfung mit Raumänderung).
    /// </summary>
    public List<string> Marker
    {
        get
        {
            var m = new List<string>();
            var verlegung = Verlegung;
            if (verlegung == "von") m.Add("verlegungVon");
            else if (verlegung == "nach") m.Add("verlegungNach");
            else if (Status == "entfall" || (Status == "abwesend" && !LehrerAbwesend)) m.Add("entfall");
            if (Status == "klausur") { m.Add("pruefung"); if (WertGeaendert) m.Add("aenderung"); }
            else if (verlegung == "nach") { if (WertGeaendert && Status is "geaendert" or "offen") m.Add("aenderung"); }
            else if (verlegung is null && IstZusatz) { m.Add("plus"); if (ZusatzWertGeaendert && Status != "veranstaltung") m.Add("aenderung"); }
            else if (verlegung is null && Status is "geaendert" or "offen" or "buchung") m.Add("aenderung");
            return m;
        }
    }

    /// <summary>Vertretungsart einer Verlegung (Verlegung, Wegverlegung, Statt-Vertretung).</summary>
    public static bool IstVerlegungsArt(string? art) => !string.IsNullOrEmpty(art) &&
        (art.Contains("verleg", StringComparison.OrdinalIgnoreCase) || art.Contains("statt", StringComparison.OrdinalIgnoreCase));
    /// <summary>Abwesende Klassen dieser Stunde (Klasse durchgestrichen, Label „abwesend“); die Stunde selbst findet laut Plan statt.</summary>
    public List<string> AbwesendeKlassen { get; init; } = new();
    /// <summary>Text des Labels bei abwesender Klasse, z. B. „abwesend“ bzw. „abwesend – Exkursion“ (Grund nur fürs Kollegium).</summary>
    public string AbwesendText { get; init; } = "";
    /// <summary>
    /// Darstellung „Lehrkraft abwesend“ (gestrichelter Rahmen mit Eck-Klammern, Lehrer-Label grün bzw. durchgestrichen) statt Entfall:
    /// eigene Stunde im Lehrerplan wird von anderen gehalten, oder die Bereitschaft einer abwesenden Lehrkraft fällt weg.
    /// </summary>
    public bool LehrerAbwesend { get; init; }
    /// <summary>Bereitschaft (Fach mit Bereitschaft) – wird nie vertreten; fehlt die Lehrkraft, gilt <see cref="LehrerAbwesend"/>.</summary>
    public bool Bereitschaft { get; init; }
    /// <summary>
    /// Offene Lehrervertretung: die eingeplante Lehrkraft ist abwesend und es ist noch keine Vertretungslehrkraft eingeteilt.
    /// Lehrer enthält dann je abwesende Lehrkraft einen Wert { K = "", Alt = Kürzel, Neu = true } – Darstellung: altes Kürzel
    /// durchgestrichen im grünen Label. Gilt auch, wenn der Status im eigenen Lehrerplan „abwesend“ lautet.
    /// </summary>
    public bool VertretungOffen { get; init; }
    /// <summary>
    /// Mitklassen (nur im Klassenplan): andere Klassen, die zur selben Zeit bei derselben Lehrkraft im selben Fach und Raum Unterricht
    /// haben (gekoppelt bzw. parallel identisch, z. B. INF13 auf der Karte von INF12). <see cref="Klassen"/> bleibt die eigene Klasse
    /// (Klassenbuch, Anwesenheit). Fehlt eine Mitklasse (Absenz bzw. Entfall ihrer Stunde): { K = "", Alt = "INF13", Neu = true }
    /// und die Karte gilt als geändert.
    /// </summary>
    public List<Wert> Mitklassen { get; init; } = new();
    /// <summary>Prüfung aus UntPlan (Klausur-Id), zu der die Karte gehört: Prüfungszeile bzw. wegen der Prüfung entfallener Unterricht.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int? Klausur { get; init; }
    /// <summary>
    /// Nur Klassenbuch: die Karte steht für eine Einzelstunde eines Blocks (Von = Bis = diese Stunde) – Einträge gelten nur für sie
    /// (Speicherung mit negativer Stundennummer, siehe <see cref="Klassenbuchdienst.Schluessel"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public int? Einzelstunde { get; init; }
}

/// <summary>
/// Pausenaufsicht im Lehrerplan. Status: regulaer, vertreten (fällt für diese Lehrkraft weg, Lehrer = Vertretung bzw. „???“, Alt = eigene
/// Lehrkraft), vertretung (zusätzlich übernommen, Alt = abwesende Lehrkraft), entfall (findet nicht statt, Lehrer = eingeteilte Lehrkraft;
/// Grund z. B. „Abwesenheit“ bzw. Titel der Veranstaltung, sonst null).
/// </summary>
public sealed record Aufsicht(string Datum, int NachStunde, string Beginn, string Ende, string Gang, string GangName, string Status, string Lehrer, string? Alt)
{
    /// <summary>Nur bei Status „entfall“: Anlass (Abwesenheit, Veranstaltung bzw. Text der Vertretung); sonst null.</summary>
    public string? Grund { get; init; }
}

public sealed record Tageskopf(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, bool Tagesaktuell, List<string> Hinweise, List<Ereignis> Ereignisse, string? FreiArt = null, string? FreiVon = null, string? FreiBis = null)
{
    /// <summary>Nur Datum und Wochentag (gesperrte Woche).</summary>
    public static Tageskopf Leer(Tageskopf k) => new(k.Datum, k.Wochentag, k.Woche, false, null, false, new(), new());
}

/// <summary>Veranstaltung an einem Tag: ganztägig (Leiste über dem Tag) oder in bestimmten Stunden (Karte im Raster).</summary>
public sealed record Ereignis(string Titel, string Art, int Von, int Bis, bool Ganztaegig, List<string> Klassen, List<string> Lehrer, List<string> Raeume);

public sealed record Zeit(int Nr, string Bezeichnung, string Beginn, string Ende);

/// <summary>Eigene Abwesenheit an einem Tag (Uhrzeiten HH:mm), Grund (Kürzel bzw. Name, sofern freigegeben) und Art (fehlt, verspaetet, vorzeitig).</summary>
public sealed record PlanAbwesenheit(string Datum, string Von, string Bis, string Grund, string Art);

public sealed record Wochenplan(string Typ, string Element, string Name, string Montag, string Woche, List<Zeit> Zeitraster, List<Tageskopf> Tage,
    List<Karte> Karten, List<Aufsicht> Aufsichten)
{
    /// <summary>Woche außerhalb des Planfensters der Rolle: leeres Raster (keine Karten, Aufsichten, Hinweise) mit <see cref="Sperrhinweis"/>.</summary>
    public bool Gesperrt { get; init; }
    public string? Sperrhinweis { get; init; }
    /// <summary>
    /// Eigene Abwesenheiten im eigenen Plan („Mein Stundenplan“): Lehrkraft (Absenzen aus UntPlan) bzw. Schüler*in (Abwesenheiten aus dem
    /// Klassenbuch). Der Plan zeigt sie als graue Fläche hinter den Stunden (Menü „⋮“ → „Abwesenheiten anzeigen“).
    /// </summary>
    public List<PlanAbwesenheit> Abwesenheiten { get; init; } = new();

    public Wochenplan Sperren() => this with
    {
        Tage = Tage.Select(Tageskopf.Leer).ToList(), Karten = new(), Aufsichten = new(), Gesperrt = true, Sperrhinweis = Planfenster.Sperrhinweis,
        Abwesenheiten = new(),
    };
}

public sealed record Tagesuebersicht(string Typ, string Datum, Tageskopf Kopf, List<Zeit> Zeitraster, List<Tageszeile> Zeilen)
{
    /// <summary>Tag außerhalb des Planfensters der Rolle (siehe <see cref="Wochenplan.Gesperrt"/>).</summary>
    public bool Gesperrt { get; init; }
    public string? Sperrhinweis { get; init; }

    public Tagesuebersicht Sperren() => this with { Kopf = Tageskopf.Leer(Kopf), Zeilen = new(), Gesperrt = true, Sperrhinweis = Planfenster.Sperrhinweis };
}
public sealed record Tageszeile(string Kurz, string Name, List<Karte> Karten);

/// <summary>Rohdaten einer Stunde (aus dem tagesaktuellen Plan, dem Regelplan oder einer Vormerkung).</summary>
public sealed record Rohstunde(int Stunde, int? Unterricht, string Fach, IReadOnlyList<string> Klassen, IReadOnlyList<string> Lehrer, IReadOnlyList<string> Raeume,
    string Status, int? Vertretung, string Text, string Art = "", IReadOnlyList<string>? LehrerAlt = null, bool Bereitschaft = false, int? Klausur = null);

public static class Elementtyp
{
    public const string Klasse = "klasse";
    public const string Lehrer = "lehrer";
    public const string Raum = "raum";
    public static bool Gueltig(string t) => t is Klasse or Lehrer or Raum;
}

/// <summary>Stundenpläne (Woche, Tag), Tagesübersichten aller Elemente und Hinweise aus dem übertragenen Paket.</summary>
public sealed class Plandienst
{
    /// <summary>Vertretungstext einer Vertretung ohne Lehrkraft („+++“), wenn kein anderer Text vorhanden ist.</summary>
    public const string OhneLehrkraftText = "Vertr. o. Lehrkr.";

    /// <summary>
    /// Älterer Hinweistext aus früheren UntPlan-Versionen → aktuelle Bezeichnung. Der automatische Hinweis „(Benutzer) Raumänderung“
    /// älterer Desktop-Versionen (Raumänderung aus einer Buchung) ist kein Vertretungstext und entfällt.
    /// </summary>
    public static string TextNormal(string text) =>
        string.Join(" · ", text.Replace("Vertr. o. Lehrkraft", OhneLehrkraftText, StringComparison.Ordinal)
            .Split(" · ").Where(t => !RaumaenderungsHinweis.IsMatch(t.Trim())));

    private static readonly System.Text.RegularExpressions.Regex RaumaenderungsHinweis = new(@"^\([^()]*\)\s*Raumänderung$", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Lehrkraft eines zusätzlichen Unterrichts, für den (noch) keine Lehrkraft eingetragen ist.</summary>
    public const string OhneLehrkraftZusatz = "---";
    public static readonly string[] Wochentage = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    public static DateOnly Montag(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    public static int TagIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    public List<Zeit> Zeitraster(Stand s) => s.Paket.Zeitraster.Stunden.Select(z => new Zeit(z.Nr, z.Bezeichnung.Length > 0 ? z.Bezeichnung : z.Nr.ToString(), z.Beginn, z.Ende)).ToList();

    public string Name(Stand s, string typ, string kurz) => typ switch
    {
        Elementtyp.Klasse => s.Klassen.TryGetValue(kurz, out var k) ? (k.Name.Length > 0 && k.Name != k.Kurz ? $"{k.Kurz} – {k.Name}" : $"Klasse {k.Kurz}") : kurz,
        Elementtyp.Lehrer => s.Lehrer.TryGetValue(kurz, out var l) ? $"{l.Kurz} – {string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => x.Length > 0))}" : kurz,
        Elementtyp.Raum => s.Raeume.TryGetValue(kurz, out var r) ? (r.Name.Length > 0 && r.Name != r.Kurz ? $"{r.Kurz} – {r.Name}" : $"Raum {r.Kurz}") : kurz,
        _ => kurz,
    };

    /// <summary>Wochentyp (A/B …) eines Datums: aus dem tagesaktuellen Plan, sonst aus dem Wochenzyklus ab Schuljahresbeginn.</summary>
    public string Woche(Stand s, DateOnly d)
    {
        if (s.Tage.TryGetValue(Iso(d), out var t) && t.Woche.Length > 0) return t.Woche;
        var n = Math.Max(1, s.Paket.Schule.Wochenperiodizitaet);
        if (n == 1 || s.SchuljahrVon == DateOnly.MinValue) return "";
        var wochen = (Montag(d).DayNumber - Montag(s.SchuljahrVon).DayNumber) / 7;
        return ((char)('A' + ((wochen % n) + n) % n)).ToString();
    }

    public WebFerien? Ferien(Stand s, DateOnly d) =>
        s.Zusatzferien.Concat(s.Paket.Ferien).FirstOrDefault(f => DateOnly.TryParse(f.Von, out var v) && DateOnly.TryParse(f.Bis, out var b) && d >= v && d <= b);

    /// <summary>In UntPlanWeb gepflegter freier Tag (gilt auch an tagesaktuellen Tagen).</summary>
    private static WebFerien? Zusatz(Stand s, DateOnly d) =>
        s.Zusatzferien.FirstOrDefault(f => DateOnly.TryParse(f.Von, out var v) && DateOnly.TryParse(f.Bis, out var b) && d >= v && d <= b);

    /// <summary>Unterrichtsfrei (Wochenende, Ferien, außerhalb des Schuljahres oder im Paket als unterrichtsfrei markiert)?</summary>
    public (bool Frei, string? Grund) Frei(Stand s, DateOnly d)
    {
        if (TagIndex(d) < Math.Max(1, s.Paket.Zeitraster.TageProWoche) && Zusatz(s, d) is { } z) return (true, z.Name);
        if (s.Tage.TryGetValue(Iso(d), out var t))
            return t.Unterrichtstag ? (false, null) : (true, Ferien(s, d)?.Name ?? (TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? null : "unterrichtsfrei"));
        if (TagIndex(d) >= Math.Max(1, s.Paket.Zeitraster.TageProWoche)) return (true, null);
        if (d < s.SchuljahrVon || d > s.SchuljahrBis) return (true, "außerhalb des Schuljahres");
        if (Ferien(s, d) is { } f) return (true, f.Name);
        return (false, null);
    }

    /// <summary>Alle Stunden eines Tages (tagesaktuell, sonst Regelplan der gültigen Periode).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Stand, Dictionary<int, List<string>>> KopplungsKlassenCache = new();

    /// <summary>Klassen je Unterricht über alle Kopplungszeilen (für Zeilen ohne eigene Klassen, z. B. zweite Lehrkraft).</summary>
    private static Dictionary<int, List<string>> KopplungsKlassen(Stand s) => KopplungsKlassenCache.GetValue(s, st =>
        st.Paket.Perioden.SelectMany(p => p.Unterricht).GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => g.SelectMany(u => u.Zeilen).SelectMany(z => z.Klassen).Where(k => k.Length > 0).Distinct(Ci).ToList()));

    private static IReadOnlyList<string> KlassenOderKopplung(Stand s, int? unterricht, IReadOnlyList<string> klassen) =>
        klassen.Count > 0 || unterricht is not int u ? klassen : KopplungsKlassen(s).TryGetValue(u, out var k) ? k : klassen;

    public List<Rohstunde> Rohstunden(Stand s, DateOnly d)
    {
        Rohstunde Aus(WebStunde x) => new(x.Stunde, x.Unterricht, x.Fach, KlassenOderKopplung(s, x.Unterricht, x.Klassen), x.Lehrer, x.Raeume, x.Status, x.Vertretung, x.Text, "", x.LehrerAlt, x.Bereitschaft, x.Klausur);
        if (s.Tage.TryGetValue(Iso(d), out var tag) && !tag.NurAenderungen)
            return tag.Unterrichtstag ? tag.Stunden.Select(Aus).ToList() : new List<Rohstunde>();
        if (tag is { NurAenderungen: true })
        {
            // nur Vertretungen übertragen: die geänderten Stunden über den zuletzt übertragenen Stundenplan legen
            if (!tag.Unterrichtstag) return new List<Rohstunde>();
            var basis = RegelStunden(s, d);
            // Prüfungen stehen als geänderte Stunden (Prüfungszeile, entfallener Unterricht) bereits im Tag: die Prüfungszeilen aus den
            // Terminen (RegelStunden) dann nicht zusätzlich – sonst erschiene die Prüfung als zweiter Block neben der Stunde
            basis.RemoveAll(b => b.Art == "Klausur" && b.Unterricht is null && tag.Stunden.Any(x => x.Stunde == b.Stunde && x.Klassen.Any(k => Enthaelt(b.Klassen, k))
                && (x.Klausur.HasValue || (x.Unterricht is null && x.Status is "zusaetzlich" or "vertreten"))));
            foreach (var x in tag.Stunden)
            {
                foreach (var b in ErsetzteZeilen(s, basis, x)) basis.Remove(b);
                basis.Add(Aus(x));
            }
            return basis.OrderBy(b => b.Stunde).ToList();
        }
        return RegelStunden(s, d);
    }

    /// <summary>
    /// „Nur Änderungen“: Zeilen des Stundenplans, die eine geänderte Stunde ersetzt. Bei Kopplungen (mehrere Zeilen desselben
    /// Unterrichts zur selben Zeit) genau die betroffene Zeile – alle anderen Blöcke bleiben nebeneinander stehen:
    /// zuerst die Zeile der abwesenden bzw. ursprünglichen Lehrkraft (LehrerAlt, Lehrkraft der Vertretung) – nie die Zeile einer
    /// Kollegin, nur weil sie vertritt –, dann die Zeile derselben Lehrkraft (z. B. Raumänderung), dann gleiches Fach und gleicher
    /// Raum, zuletzt die einzige Zeile mit gleichem Fach. Zusätzliche Stunden ersetzen ohne eindeutige Zuordnung nichts.
    /// </summary>
    internal static List<Rohstunde> ErsetzteZeilen(Stand s, IReadOnlyList<Rohstunde> basis, WebStunde x)
    {
        if (!x.Unterricht.HasValue) return new();
        var gleich = basis.Where(b => b.Stunde == x.Stunde && b.Unterricht == x.Unterricht && b.Art != "Klausur").ToList();
        if (gleich.Count == 0) return gleich;
        WebVertretung? v = x.Vertretung is int vid && s.Vertretungen.TryGetValue(vid, out var vx) ? vx : null;
        var alt = new HashSet<string>((x.LehrerAlt ?? new List<string>()).Where(l => l.Length > 0), Ci);
        if (v != null && v.Lehrer.Length > 0) alt.Add(v.Lehrer);
        var treffer = gleich.Where(b => b.Lehrer.Any(alt.Contains)).ToList();
        if (treffer.Count > 1 && treffer.Any(b => Ci.Equals(b.Fach, x.Fach))) treffer = treffer.Where(b => Ci.Equals(b.Fach, x.Fach)).ToList();
        if (treffer.Count == 0) treffer = gleich.Where(b => b.Lehrer.Any(l => Enthaelt(x.Lehrer, l))).ToList();
        if (treffer.Count == 0 && x.Status == "zusaetzlich") return treffer;
        static HashSet<string> R(IEnumerable<string> l) => l.SelectMany(RaumListe).ToHashSet(Ci);
        if (treffer.Count == 0)
        {
            var raeume = R(x.Raeume.Concat(v != null ? new[] { v.Raum } : Array.Empty<string>()));
            treffer = gleich.Where(b => Ci.Equals(b.Fach, x.Fach) && raeume.Count > 0 && R(b.Raeume).Overlaps(raeume)).Take(1).ToList();
        }
        if (treffer.Count == 0)
        {
            var fach = gleich.Where(b => Ci.Equals(b.Fach, x.Fach) || (v != null && Ci.Equals(b.Fach, v.Fach))).ToList();
            if (fach.Count == 1 || gleich.Count == 1) treffer = fach.Count == 1 ? fach : gleich;
        }
        return treffer;
    }

    /// <summary>Räume eines Unterrichts laut Stundenplan (vor Raumänderungen) – damit ein entfernter ursprünglicher Raum wieder gewählt werden kann.</summary>
    public List<string> UrspruenglicheRaeume(Stand s, DateOnly d, int unterricht, int von, int bis) =>
        RegelStunden(s, d).Where(r => r.Unterricht == unterricht && r.Stunde >= von && r.Stunde <= bis)
            .SelectMany(r => r.Raeume.SelectMany(RaumListe)).Where(x => x.Length > 0).Distinct(Ci).ToList();

    /// <summary>Stunden laut zuletzt übertragenem Periodenstundenplan (ohne Vertretungen).</summary>
    private List<Rohstunde> RegelStunden(Stand s, DateOnly d)
    {
        if (Frei(s, d).Frei) return new List<Rohstunde>();
        var periode = s.Paket.Perioden
            .Where(p => DateOnly.TryParse(p.Von, out var v) && v <= d && (!DateOnly.TryParse(p.Bis, out var b) || d <= b))
            .OrderByDescending(p => DateOnly.Parse(p.Von)).FirstOrDefault();
        if (periode is null) return new List<Rohstunde>();
        var unterricht = periode.Unterricht.ToDictionary(u => u.Id);
        var woche = Woche(s, d);
        var t = TagIndex(d);
        var res = new List<Rohstunde>();
        foreach (var ps in periode.Stundenplan.Where(p => p.Tag == t))
        {
            if (!unterricht.TryGetValue(ps.Unterricht, out var u)) continue;
            if (woche.Length > 0 && u.Wochen.Length > 0 && !u.Wochen.Contains(woche, StringComparison.OrdinalIgnoreCase)) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var raeume = ps.Raeume.Count == u.Zeilen.Count ? new List<string> { ps.Raeume[i] } : ps.Raeume;
                res.Add(new Rohstunde(ps.Stunde, u.Id, z.Fach, KlassenOderKopplung(s, u.Id, z.Klassen), z.Lehrer.Length > 0 ? new List<string> { z.Lehrer } : new List<string>(), raeume, "regulaer", null, ""));
            }
        }
        // Klausuren aus den Terminen (im tagesaktuellen Plan sind sie bereits enthalten)
        foreach (var k in s.Paket.Termine.Where(x => x.Art == "klausur" && DateOnly.TryParse(x.Von, out var v) && v == d)
                     .DistinctBy(x => (x.StundeVon, x.StundeBis, string.Join(",", x.Klassen.Order()), x.Titel)))
            for (var st = k.StundeVon; st <= Math.Max(k.StundeVon, k.StundeBis); st++)
                res.Add(new Rohstunde(st, null, "", k.Klassen, k.Lehrer, k.Raeume, "zusaetzlich", null, k.Titel, "Klausur"));
        return res;
    }

    private static bool Enthaelt(IReadOnlyList<string> liste, string kurz) => liste.Any(x => Ci.Equals(x, kurz));

    public static bool IstBereitschaft(Stand s, string fach) =>
        fach.Equals("BER", StringComparison.OrdinalIgnoreCase) || (s.Faecher.TryGetValue(fach, out var f) && f.Name.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase));

    /// <summary>Karten eines Elements an einem Tag; zusammenhängende gleiche Stunden werden zu Blöcken zusammengefasst.</summary>
    public List<Karte> Karten(Stand s, DateOnly d, string typ, string kurz, IEnumerable<Rohstunde>? zusaetzlich = null)
    {
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        var einzel = new List<Karte>();
        var vorgemerkt = new List<(Rohstunde R, Karte K)>();
        var roh = Rohstunden(s, d).Concat(zusaetzlich ?? Enumerable.Empty<Rohstunde>()).ToList();
        // Kopplung mit gleichem Fach und gleichem Raum: im Lehrerplan auch die Zeilen der Kolleg*innen (eine gemeinsame Karte)
        static bool GleicherRaum(Rohstunde a, Rohstunde b) => a.Raeume.Count == b.Raeume.Count && a.Raeume.All(x => Enthaelt(b.Raeume, x));
        bool Mitgekoppelt(Rohstunde r) => r.Unterricht is int u && r.Fach.Length > 0 && r.Status is not ("entfall" or "vorgemerkt") &&
            roh.Any(x => !ReferenceEquals(x, r) && x.Stunde == r.Stunde && x.Unterricht == u && x.Status is not ("entfall" or "vorgemerkt") && Ci.Equals(x.Fach, r.Fach) && GleicherRaum(x, r) && Enthaelt(x.Lehrer, kurz));
        // vorgemerkte Löschungen von Prüfungen (werden unten angewandt)
        var loeschungen = roh.Where(r => r.Status == "vorgemerkt" && r.Art == "KlausurLoeschen").ToList();
        foreach (var r in roh)
        {
            if (r.Art == "KlausurLoeschen") continue;
            WebVertretung? v = r.Vertretung is int vid && s.Vertretungen.TryGetValue(vid, out var vx) ? vx : null;
            var betrifft = typ switch
            {
                Elementtyp.Klasse => Enthaelt(r.Klassen, kurz),
                Elementtyp.Lehrer => Enthaelt(r.Lehrer, kurz) || (v != null && Ci.Equals(v.Lehrer, kurz)) || Mitgekoppelt(r),
                // Veranstaltung mit Raum: der Raum steht (bei älteren Paketen) nur in der Vertretung
                Elementtyp.Raum => Enthaelt(r.Raeume, kurz) || (v != null && (RaumListe(v.Raum).Contains(kurz, Ci) || (r.Raeume.Count == 0 && r.Status != "entfall" && RaumListe(v.NeuerRaum).Contains(kurz, Ci)))),
                _ => false,
            };
            // vorgemerkte Raumänderung eines Unterrichts: wird unten auf die Stunde gelegt, falls diese im Plan steht
            if (!betrifft && !(r.Status == "vorgemerkt" && r.Unterricht.HasValue && r.Art != "Klausur")) continue;
            var k = KarteBauen(s, d, typ, kurz, r, v, zeiten);
            if (typ == Elementtyp.Klasse && r.Status != "vorgemerkt") k = MitklassenSetzen(k, r, roh, kurz);
            if (r.Status == "vorgemerkt") vorgemerkt.Add((r, k));
            else
            {
                einzel.Add(k);
                if (Fachwechsel(k, typ, kurz) is { } alt) einzel.Add(alt);
            }
        }
        // zum Löschen vorgemerkte Prüfungen (auch der alte Termin einer verlegten Prüfung) ausblenden – vor dem Auflegen vorgemerkter
        // Prüfungen, damit der wieder reguläre Unterricht die neue Prüfung aufnehmen kann
        einzel = LoeschungenAnwenden(einzel, loeschungen, typ, kurz);
        // Vorgemerkte Klausuren und Raumbuchungen zu einer Unterrichtsstunde direkt auf die Stundenkarte legen.
        // Raumbuchung zu einem Unterricht (Stundendetails: Ersetzen, Löschen, zusätzlicher Raum) = reine Raumänderung:
        // nur die Räume ändern sich (alt → neu), kein Vertretungstext, kein zusätzlicher Eintrag im Stundenplan.
        foreach (var (r, k) in vorgemerkt)
        {
            var raumbuchung = r.Art != "Klausur";
            int i;
            if (raumbuchung && r.Unterricht is int uz)
            {
                i = einzel.FindIndex(x => x.Unterricht == uz && r.Stunde >= x.Von && r.Stunde <= x.Bis && x.Status is not ("entfall" or "abwesend"));
                // Raumplan des neuen Raums: die hierher verlegte Stunde anzeigen
                if (i < 0 && typ == Elementtyp.Raum && Enthaelt(r.Raeume, kurz) &&
                    roh.FirstOrDefault(x => x.Unterricht == uz && x.Stunde == r.Stunde && x.Status is not ("entfall" or "vorgemerkt")) is { } basis)
                {
                    WebVertretung? bv = basis.Vertretung is int bid && s.Vertretungen.TryGetValue(bid, out var bx) ? bx : null;
                    einzel.Add(KarteBauen(s, d, typ, kurz, basis, bv, zeiten));
                    i = einzel.Count - 1;
                }
                if (i < 0) continue;
            }
            else
            {
                bool Passt(Karte x) => x.Von == r.Stunde && x.Unterricht.HasValue && x.Status is not ("entfall" or "abwesend") && x.Klassen.Any(w => Enthaelt(r.Klassen, w.K));
                // paralleler Unterricht der Klasse (Kurse, Gruppen): bevorzugt die Stunde der Aufsicht – wie nach der Übernahme in UntPlan
                i = r.Klassen.Count == 0 ? -1 : einzel.FindIndex(x => Passt(x) && x.Lehrer.Any(w => Enthaelt(r.Lehrer, w.K)));
                if (i < 0 && r.Klassen.Count > 0) i = einzel.FindIndex(Passt);
            }
            if (i < 0)
            {
                // reine Raumbuchung ohne Unterricht: nur im Raumplan, kein zusätzlicher Eintrag im eigenen Stundenplan.
                // Vorgemerkte Prüfung ohne Stunde darunter (z. B. Raumplan des Prüfungsraums): gleich als gelbe Prüfungskarte
                if (!raumbuchung) einzel.Add(k with { Status = "klausur", Art = VorgemerktePruefung });
                else if (typ == Elementtyp.Raum) einzel.Add(k);
                continue;
            }
            var ziel = einzel[i];
            if (!raumbuchung)
            {
                var kr = r.Raeume.Count == 0 ? ziel.Raeume
                    : r.Raeume.Select(x => ziel.Raeume.FirstOrDefault(w => Ci.Equals(w.K, x)) ?? new Wert(x, ziel.Raeume.FirstOrDefault()?.K ?? "", true)).ToList();
                einzel[i] = ziel with { Status = "klausur", Art = VorgemerktePruefung, Text = r.Text, Raeume = kr };
                continue;
            }
            var raeume = RaeumeNachBuchung(ziel.Raeume, r.Raeume);
            // nur die Räume ändern sich (alt → neu): Status, Art und Text der Stunde bleiben – keine Vertretung
            var status = ziel.Status;
            // Raumplan des bisherigen Raums: die Stunde findet woanders statt
            if (typ == Elementtyp.Raum && !raeume.Any(w => Ci.Equals(w.K, kurz))) status = "abwesend";
            einzel[i] = ziel with { Status = status, Raeume = raeume };
        }
        einzel = PruefungenZusammenfuehren(einzel, loeschungen, typ, kurz, roh);
        // Raumplan: Stunden, die aus diesem Raum genommen wurden (Raumänderung bzw. Raumbuchung weg, Prüfung in einem anderen Raum),
        // erscheinen hier gar nicht mehr – weder als Entfall noch als „findet woanders statt“
        if (typ == Elementtyp.Raum) einzel.RemoveAll(k => AusRaumGenommen(k, kurz));
        // Teilnahmen mehrerer Lehrkräfte an derselben Veranstaltung: eine Karte mit allen Beteiligten
        var veranstaltungen = einzel.Where(k => k.Status == "veranstaltung").GroupBy(k => (k.Von, k.Text))
            .Select(g => g.First() with
            {
                Klassen = g.SelectMany(k => k.Klassen).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Lehrer = g.SelectMany(k => k.Lehrer).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Raeume = g.SelectMany(k => k.Raeume).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
            }).ToList();
        einzel = einzel.Where(k => k.Status != "veranstaltung").Concat(veranstaltungen.Select(k => MitTermin(s, d, k))).ToList();
        return KlassenAbwesenheit(s, d, Zusammenfassen(Kopplungen(KlassenVeranstaltung(s, d, einzel)), zeiten));
    }

    /// <summary>Stunde findet nicht (mehr) in diesem Raum statt (im Raumplan „abwesend“; weggefallene Bereitschaften ausgenommen).</summary>
    internal static bool AusRaumGenommen(Karte k, string raum) => k.Status == "abwesend" && !k.Bereitschaft;

    /// <summary>Art einer in UntPlanWeb eingetragenen, noch nicht aus UntPlan übertragenen Prüfung (Status „klausur“, gelb).</summary>
    public const string VorgemerktePruefung = "Klausur (vorgemerkt)";

    private static bool IstPruefungsKarte(Karte x) => x.Unterricht is null && (x.Status == "klausur" || Ci.Equals(x.Art, "Klausur"));

    /// <summary>
    /// Entfällt der Unterricht wegen dieser Prüfung? Mit Klausur-Id aus dem Paket eindeutig; ältere Pakete: Entfalltext
    /// „Klausur &lt;Name&gt;“ passend zum Text der Prüfung.
    /// </summary>
    private static bool WegenPruefung(Karte x, Karte pruefung)
    {
        if (x.Status != "entfall" || !x.Unterricht.HasValue) return false;
        if (x.Klausur.HasValue || pruefung.Klausur.HasValue) return x.Klausur == pruefung.Klausur;
        var t = x.Text.Trim();
        if (!t.StartsWith("Klausur ", StringComparison.OrdinalIgnoreCase)) return false;
        var name = t[8..].Trim();
        return name.Length > 0 && pruefung.Text.Trim().StartsWith(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prüfungskarten aufbereiten:
    /// <list type="bullet">
    /// <item>doppelt übertragene Prüfungen (ältere Mehrfachimporte, je Aufsicht eine Zeile) nur einmal zeigen,</item>
    /// <item>zum Löschen vorgemerkte Prüfungen ausblenden,</item>
    /// <item>übernommene Prüfungen auf die Unterrichtsstunden der Klasse legen: die Stundenkarte wird zur Prüfung (Lehrkraft und Fach
    ///   bleiben, Raum der Prüfung als „alt → neu“) – auch wenn der Unterricht wegen der Prüfung in UntPlan entfällt. So entsteht genau
    ///   eine Karte statt Prüfungskarte(n) plus Entfall.</item>
    /// <item>Raumplan des bisherigen Raums: der wegen der Prüfung entfallene Unterricht findet woanders statt („abwesend“).</item>
    /// </list>
    /// </summary>
    internal static List<Karte> PruefungenZusammenfuehren(List<Karte> einzel, IReadOnlyList<Rohstunde> loeschungen, string typ, string kurz, IReadOnlyList<Rohstunde> roh)
    {
        static string Klassen(Karte x) => string.Join(",", x.Klassen.Select(w => w.K.ToLowerInvariant()).Order());
        bool Gleich(Karte a, Karte b) => a.Von == b.Von && a.Bis == b.Bis && ((a.Klausur.HasValue && a.Klausur == b.Klausur)
            || (Ci.Equals(a.Text.Trim(), b.Text.Trim()) && Klassen(a) == Klassen(b)));
        var res = new List<Karte>();
        foreach (var x in einzel)
        {
            var i = IstPruefungsKarte(x) ? res.FindIndex(y => IstPruefungsKarte(y) && Gleich(x, y)) : -1;
            if (i < 0) { res.Add(x); continue; }
            // gleiche Prüfung mehrfach (z. B. je Aufsicht eine Zeile): eine Karte mit allen Lehrkräften und Räumen, Fach bevorzugt
            var y = res[i];
            res[i] = y with
            {
                Fach = y.Fach.Length > 0 ? y.Fach : x.Fach, FachName = y.Fach.Length > 0 ? y.FachName : x.FachName,
                Lehrer = y.Lehrer.Concat(x.Lehrer).DistinctBy(w => (w.K.ToLowerInvariant(), (w.Alt ?? "").ToLowerInvariant())).ToList(),
                Raeume = y.Raeume.Concat(x.Raeume).DistinctBy(w => (w.K.ToLowerInvariant(), (w.Alt ?? "").ToLowerInvariant())).ToList(),
            };
        }
        // zum Löschen vorgemerkte Prüfungen nicht mehr zeigen (vorgemerkte neue Prüfungen bleiben)
        if (loeschungen.Count > 0) res = LoeschungenAnwenden(res, loeschungen, typ, kurz);
        foreach (var kl in res.Where(IstPruefungsKarte).ToList())
        {
            var treffer = res.Select((x, i) => (x, i)).Where(t => t.x.Unterricht.HasValue && t.x.Von <= kl.Bis && t.x.Bis >= kl.Von
                && t.x.Klassen.Any(w => kl.Klassen.Any(z => Ci.Equals(z.K, w.K)))
                && (t.x.Status is not ("entfall" or "abwesend") || WegenPruefung(t.x, kl))).ToList();
            if (treffer.Count == 0) continue;
            // Prüfung aus dem Paket: nur der Unterricht dieser Prüfung (ihre Zeile bzw. wegen ihr entfallen) – ein paralleler Kurs der
            // Klasse, der stattfindet, wird nicht zur (zweiten) Prüfung
            if (kl.Klausur is int kid && treffer.Any(t => t.x.Klausur == kid)) treffer = treffer.Where(t => t.x.Klausur == kid).ToList();
            // mehrere Stunden der Klasse zur selben Zeit: nur die Stunde(n) der Aufsicht werden zur Prüfung; anderer Unterricht, der wegen
            // der Prüfung entfällt, erscheint im Klassenplan nicht daneben (genau ein Prüfungsblock), paralleler Unterricht bleibt
            static string Kurz(Wert w) => w.K.Length > 0 ? w.K : w.Alt ?? "";
            var weg = new List<Karte>();
            var eigene = treffer.Where(t => t.x.Lehrer.Any(w => kl.Lehrer.Any(z => Ci.Equals(z.K, Kurz(w))))).ToList();
            if (eigene.Count > 0 && eigene.Count < treffer.Count)
            {
                foreach (var (x, _) in treffer.Except(eigene))
                    if (typ == Elementtyp.Klasse && x.Status == "entfall" && WegenPruefung(x, kl)) weg.Add(x);
                treffer = eigene;
            }
            foreach (var (x, i) in treffer)
            {
                var raeume = x.Raeume;
                // Unterricht entfällt wegen der Prüfung: die Prüfung findet in ihrem Raum statt – bisheriger Raum als Änderung
                if (WegenPruefung(x, kl) && kl.Raeume.Count > 0)
                {
                    var bisher = x.Raeume.Select(w => w.Alt is { Length: > 0 } a && w.K.Length == 0 ? new Wert(a) : w).Where(w => w.K.Length > 0).ToList();
                    raeume = kl.Raeume.Where(w => w.K.Length > 0).Select(w => bisher.FirstOrDefault(b => Ci.Equals(b.K, w.K)) ?? new Wert(w.K, bisher.FirstOrDefault()?.K, true)).ToList();
                }
                res[i] = x with { Status = "klausur", Art = "Klausur", Text = kl.Text, Raeume = raeume, Klausur = x.Klausur ?? kl.Klausur };
            }
            res.RemoveAll(x => weg.Any(w => ReferenceEquals(w, x)));
            res.Remove(kl);
        }
        // Raumplan des bisherigen Raums: kein Entfall – die Stunde findet als Prüfung statt (hier, wenn die Prüfung keinen
        // eigenen Raum hat, sonst in einem anderen Raum)
        if (typ == Elementtyp.Raum)
            res = res.Select(x =>
            {
                if (x.Status != "entfall" || x.Klausur is not int kid || !x.Unterricht.HasValue) return x;
                var zeilen = roh.Where(r => r.Klausur == kid && r.Unterricht is null && r.Stunde >= x.Von && r.Stunde <= x.Bis).ToList();
                if (zeilen.Count == 0) return x;
                var raeume = zeilen.SelectMany(r => r.Raeume.SelectMany(RaumListe)).ToList();
                return raeume.Count == 0 || Enthaelt(raeume, kurz)
                    ? x with { Status = "klausur", Art = "Klausur", Text = zeilen[0].Text }
                    : x with { Status = "abwesend", Art = "Klausur", Text = zeilen[0].Text };
            }).ToList();
        return res;
    }

    /// <summary>
    /// Zum Löschen vorgemerkte Prüfungen anwenden: die Prüfungskarten aus dem Paket (nicht die vorgemerkten neuen) verschwinden – bei
    /// gleichem Raum bevorzugt genau dieser Termin –, und der Unterricht, der wegen dieser Prüfung entfiel bzw. wegen der Aufsicht
    /// vertreten wurde, erscheint wieder regulär (keine verwaisten Entfälle).
    /// </summary>
    internal static List<Karte> LoeschungenAnwenden(List<Karte> einzel, IReadOnlyList<Rohstunde> loeschungen, string typ, string kurz)
    {
        if (loeschungen.Count == 0) return einzel;
        static bool GleicheRaeume(IEnumerable<string> a, IReadOnlyList<string> b)
        {
            var x = a.Where(r => r.Length > 0).Distinct(Ci).ToList();
            return x.Count == b.Distinct(Ci).Count() && x.All(r => b.Contains(r, Ci));
        }
        var pruefungen = einzel.Where(x => IstPruefungsKarte(x) && x.Status != "vorgemerkt" && x.Art != VorgemerktePruefung).ToList();
        var weg = new HashSet<Karte>(ReferenceEqualityComparer.Instance);
        foreach (var g in loeschungen.GroupBy(l => (string.Join(",", l.Klassen), string.Join(",", l.Raeume), l.Text)))
        {
            var kandidaten = pruefungen.Where(x => g.Any(l => l.Stunde >= x.Von && l.Stunde <= x.Bis && (l.Klassen.Count == 0 || x.Klassen.Any(w => Enthaelt(l.Klassen, w.K))))).ToList();
            var genau = kandidaten.Where(x => GleicheRaeume(x.Raeume.Select(w => w.K), g.First().Raeume)).ToList();
            foreach (var x in genau.Count > 0 ? genau : kandidaten) weg.Add(x);
        }
        if (weg.Count == 0) return einzel;
        var ids = weg.Select(x => x.Klausur).OfType<int>().ToHashSet();
        bool Ueberlappt(Karte x, Karte p) => x.Von <= p.Bis && x.Bis >= p.Von;
        Wert Bisher(Wert w) => w.Alt is { Length: > 0 } a ? new Wert(a) : new Wert(w.K);
        Karte Regulaer(Karte x) => x with
        {
            Status = "regulaer", Art = "", Text = "", Vertretung = null, LehrerAbwesend = false, VertretungOffen = false, Klausur = null,
            Lehrer = x.Lehrer.Select(Bisher).Where(w => w.K.Length > 0).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
            Raeume = x.Raeume.Select(Bisher).Where(w => w.K.Length > 0).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
        };
        var res = new List<Karte>();
        foreach (var x in einzel)
        {
            if (weg.Contains(x)) continue;
            if (!x.Unterricht.HasValue) { res.Add(x); continue; }
            // wegen der gelöschten Prüfung entfallener Unterricht der Klasse
            if (x.Status == "entfall" && ((x.Klausur is int kid && ids.Contains(kid)) || weg.Any(p => Ueberlappt(x, p) && WegenPruefung(x, p))))
            {
                res.Add(Regulaer(x));
                continue;
            }
            // eigener Unterricht einer Aufsicht („Klausuraufsicht“): wieder bei der eingeplanten Lehrkraft
            if (x.Text.Contains("Klausuraufsicht", StringComparison.OrdinalIgnoreCase) && weg.Any(p => Ueberlappt(x, p)
                    && x.Lehrer.Any(w => (w.Alt ?? w.K) is { Length: > 0 } l && p.Lehrer.Any(z => Ci.Equals(z.K, l)))))
            {
                var r = Regulaer(x);
                // Lehrerplan der bisherigen Vertretung: die Stunde gehört nicht mehr hierher
                if (typ == Elementtyp.Lehrer && !Enthaelt(r.Lehrer.Select(w => w.K).ToList(), kurz)) continue;
                res.Add(r);
                continue;
            }
            res.Add(x);
        }
        return res;
    }

    /// <summary>
    /// Mitklassen einer Stunde im Klassenplan: Stunden anderer Klassen zur selben Zeit mit gleichem Fach, gemeinsamer Lehrkraft und
    /// gleichem Raum (Kopplung über getrennte Unterrichte bzw. parallel identischer Unterricht). Entfällt die Stunde der Mitklasse,
    /// fehlt die Mitklasse: sie steht durchgestrichen ({ K = "", Alt = Klasse }) und die eigene Stunde gilt als geändert.
    /// </summary>
    internal static Karte MitklassenSetzen(Karte k, Rohstunde r, List<Rohstunde> roh, string kurz)
    {
        if (!r.Unterricht.HasValue || r.Fach.Length == 0 || k.Status is "entfall" or "veranstaltung" or "vorgemerkt" or "buchung") return k;
        static HashSet<string> Personen(Rohstunde x) => new(x.Lehrer.Concat(x.LehrerAlt ?? Array.Empty<string>()).Where(l => l.Length > 0 && l is not ("???" or "---" or "+" or "+++")), Ci);
        var eigene = Personen(r);
        if (eigene.Count == 0) return k;
        var raeume = r.Raeume.SelectMany(RaumListe).ToHashSet(Ci);
        var da = new Dictionary<string, bool>(Ci);
        foreach (var x in roh)
        {
            if (ReferenceEquals(x, r) || x.Stunde != r.Stunde || x.Status == "vorgemerkt" || x.Art == "KlausurLoeschen" || x.Klassen.Count == 0 || Enthaelt(x.Klassen, kurz)) continue;
            if (!Ci.Equals(x.Fach, r.Fach) || !Personen(x).Overlaps(eigene)) continue;
            var xr = x.Raeume.SelectMany(RaumListe).ToHashSet(Ci);
            if (x.Status != "entfall" && !xr.SetEquals(raeume)) continue;
            if (x.Status == "entfall" && xr.Count > 0 && raeume.Count > 0 && !xr.Overlaps(raeume)) continue;
            foreach (var kl in x.Klassen.Where(c => c.Length > 0 && !Enthaelt(r.Klassen, c)))
                da[kl] = (da.TryGetValue(kl, out var vorher) && vorher) || x.Status != "entfall";
        }
        if (da.Count == 0) return k;
        var liste = da.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Value ? new Wert(x.Key) : new Wert("", x.Key, true)).ToList();
        return k with { Mitklassen = liste, Status = liste.Any(w => w.K.Length == 0) && k.Status == "regulaer" ? "geaendert" : k.Status };
    }

    /// <summary>
    /// Veranstaltung einer Klasse (Termin mit Klassen): regulärer Unterricht ohne Vertretung, dessen Klassen alle an der Veranstaltung
    /// teilnehmen, entfällt – auch wenn das Paket (z. B. aus einer älteren UntPlan-Version) dafür keinen Entfall enthält.
    /// </summary>
    internal static List<Karte> KlassenVeranstaltung(Stand s, DateOnly d, List<Karte> einzel)
    {
        var termine = s.Paket.Termine.Where(t => t.Art == "veranstaltung" && t.Klassen.Count > 0 && DateOnly.TryParse(t.Von, out var v) && DateOnly.TryParse(t.Bis, out var b)
            && d >= v && d <= b).ToList();
        if (termine.Count == 0) return einzel;
        bool Deckt(WebTermin t, int stunde)
        {
            // mehrtägig: am ersten Tag ab StundeVon, am letzten bis StundeBis, dazwischen ganztägig
            var erster = DateOnly.Parse(t.Von) == d;
            var letzter = DateOnly.Parse(t.Bis) == d;
            return (!erster || stunde >= t.StundeVon) && (!letzter || t.StundeBis < 1 || stunde <= t.StundeBis);
        }
        return einzel.Select(k =>
        {
            if (k.Status != "regulaer" || k.Vertretung.HasValue || k.Klassen.Count == 0 || k.Fach.Length == 0) return k;
            var t = termine.FirstOrDefault(t => Deckt(t, k.Von) && k.Klassen.All(w => Enthaelt(t.Klassen, w.K)));
            return t is null ? k : k with { Status = "entfall", Art = "Entfall", Text = t.Titel };
        }).ToList();
    }

    /// <summary>Abwesende Klassen (Absenzen aus UntPlan) auf den Karten markieren – echte Entfälle und Veranstaltungen bleiben unverändert.</summary>
    private static List<Karte> KlassenAbwesenheit(Stand s, DateOnly d, List<Karte> karten)
    {
        var absenzen = s.Paket.Absenzen.Where(a => a.Art == "klasse" && DateOnly.TryParse(a.Von, out var v) && DateOnly.TryParse(a.Bis, out var b) && d >= v && d <= b).ToList();
        if (absenzen.Count == 0) return karten;
        bool Trifft(WebAbsenz a, int von, int bis) => a.StundeVon <= bis && (a.StundeBis >= von || a.StundeBis < a.StundeVon || a.StundeBis >= 20);
        return karten.Select(k =>
        {
            if (k.Status is "entfall" or "veranstaltung" or "vorgemerkt" or "buchung") return k;
            // abwesende Mitklasse: im Plan der anderen Klasse durchgestrichen im grünen Label, Stunde geändert
            if (k.Mitklassen.Any(w => w.K.Length > 0 && absenzen.Any(a => Ci.Equals(a.Element, w.K) && Trifft(a, k.Von, k.Bis))))
                k = k with
                {
                    Mitklassen = k.Mitklassen.Select(w => w.K.Length > 0 && absenzen.Any(a => Ci.Equals(a.Element, w.K) && Trifft(a, k.Von, k.Bis)) ? new Wert("", w.K, true) : w).ToList(),
                    Status = k.Status == "regulaer" ? "geaendert" : k.Status,
                };
            var treffer = absenzen.Where(a => k.Klassen.Any(w => Ci.Equals(w.K, a.Element)) && Trifft(a, k.Von, k.Bis)).ToList();
            if (treffer.Count == 0) return k;
            var grund = treffer.Select(a => a.Grund).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));
            var weg = treffer.Select(a => a.Element).ToHashSet(Ci);
            // alle Klassen der Stunde abwesend: die Stunde entfällt (wie ein Entfall aus der Vertretungsplanung)
            if (k.Klassen.Count > 0 && k.Klassen.All(w => weg.Contains(w.K)))
                return k with
                {
                    Status = "entfall", Art = "Klasse abwesend",
                    AbwesendeKlassen = k.Klassen.Select(w => w.K).ToList(), AbwesendText = grund is null ? "Klasse abwesend" : $"Klasse abwesend – {grund}",
                };
            return k with
            {
                AbwesendeKlassen = treffer.Select(a => k.Klassen.First(w => Ci.Equals(w.K, a.Element)).K).Distinct(Ci).ToList(),
                AbwesendText = grund is null ? "abwesend" : $"abwesend – {grund}",
            };
        }).ToList();
    }

    /// <summary>
    /// Räume einer Stunde nach einer Raumbuchung (neu = vollständige neue Raumliste): ersetzte Räume als „alt → neu“,
    /// zusätzliche Räume als neuer Wert, entfernte Räume durchgestrichen.
    /// </summary>
    /// <summary>Räume aus einem Feld wie „D017,C151“ bzw. „D017, C151“.</summary>
    internal static List<string> RaumListe(string? text) =>
        (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(Ci).ToList();

    internal static List<Wert> RaeumeNachBuchung(List<Wert> bisher, IReadOnlyList<string> neu)
    {
        // ursprünglicher Raum je bisherigem Wert (bei schon geändertem Raum der Wert vor der Änderung)
        var entfernt = bisher.Where(w => w.K.Length > 0 && !Enthaelt(neu, w.K)).Select(w => w.Alt is { Length: > 0 } a ? a : w.K).ToList();
        var res = new List<Wert>();
        var j = 0;
        foreach (var x in neu.Where(x => x.Length > 0).Distinct(Ci))
            res.Add(bisher.FirstOrDefault(w => Ci.Equals(w.K, x)) ?? new Wert(x, j < entfernt.Count ? entfernt[j++] : null, true));
        for (; j < entfernt.Count; j++) res.Add(new Wert("", entfernt[j], true));
        return res;
    }

    /// <summary>
    /// Fachwechsel wie im Original: Zur geänderten Stunde mit neuem Fach erscheint die ursprüngliche Stunde als Entfall daneben
    /// (altes Fach, ursprüngliche Lehrkräfte und Räume). Im Lehrerplan nur, wenn die Lehrkraft die ursprüngliche Stunde gehalten hätte.
    /// </summary>
    private static Karte? Fachwechsel(Karte k, string typ, string kurz)
    {
        if (k.Status != "geaendert" || string.IsNullOrEmpty(k.FachAlt) || Ci.Equals(k.FachAlt, k.Fach)) return null;
        static List<Wert> Urspruenglich(IEnumerable<Wert> l) => l.Select(w => w.Alt is { Length: > 0 } a ? a : w.K).Where(x => x.Length > 0).Distinct(Ci).Select(x => new Wert(x)).ToList();
        var lehrer = Urspruenglich(k.Lehrer);
        var raeume = Urspruenglich(k.Raeume);
        if (typ == Elementtyp.Lehrer && !lehrer.Any(w => Ci.Equals(w.K, kurz))) return null;
        if (typ == Elementtyp.Raum && !raeume.Any(w => Ci.Equals(w.K, kurz))) return null;
        return k with
        {
            Status = "entfall", Fach = k.FachAlt!, FachAlt = null, FachName = k.FachAlt!, Farbe = k.Farbe, Lehrer = lehrer, Raeume = raeume,
            Klassen = k.Klassen.Select(w => new Wert(w.Alt is { Length: > 0 } a ? a : w.K)).Where(w => w.K.Length > 0).ToList(),
            Art = "Entfall", Text = "", Unterricht = null, VerlegungsZeile = false, VerlegungVon = null, VerlegungNach = null,
        };
    }

    /// <summary>
    /// Kopplungen: Stunden zur selben Zeit werden nur dann auf einer Karte zusammengefasst, wenn Fach UND Raum gleich sind –
    /// dann stehen alle Lehrkräfte und Klassen auf der Karte. Gekoppelte Stunden mit unterschiedlichen Fächern (z. B. KR und ER)
    /// oder in unterschiedlichen Räumen bleiben einzelne Karten. Entfall und abgegebene Stunden werden nur untereinander zusammengefasst.
    /// </summary>
    internal static List<Karte> Kopplungen(List<Karte> einzel)
    {
        static string Gruppe(string status) => status is "regulaer" or "geaendert" or "offen" ? "laeuft" : status;
        // Zuordnung nach Fach und Räumen laut Stundenplan (vor Änderungen): Blöcke einer Kopplung, die getrennt waren, bleiben auch nach
        // einer Vertretung bzw. Raumänderung getrennt nebeneinander (z. B. KR nach R106 zu ER verlegt) – es fällt nie ein Block weg
        static string Raeume(Karte k) => string.Join(",", k.Raeume.Select(w => (w.Alt is { Length: > 0 } a ? a : w.Plus ? "" : w.K).ToLowerInvariant()).Where(x => x.Length > 0).Distinct().OrderBy(x => x, StringComparer.Ordinal));
        static string Fach(Karte k) => (string.IsNullOrEmpty(k.FachAlt) ? k.Fach : k.FachAlt).ToLowerInvariant();
        static List<Wert> Vereinigen(IEnumerable<Wert> l) => l.GroupBy(w => (w.K.ToLowerInvariant(), (w.Alt ?? "").ToLowerInvariant())).Select(g => g.First()).ToList();
        var res = new List<Karte>();
        foreach (var g in einzel.GroupBy(k => k.Unterricht is int && k.Fach.Length > 0
                     ? $"{k.Datum}|{k.Von}|{k.Bis}|{Gruppe(k.Status)}|{Fach(k)}|{Raeume(k)}" : Guid.NewGuid().ToString()))
        {
            var liste = g.ToList();
            if (liste.Count == 1) { res.AddRange(liste); continue; }
            var f = liste[0];
            var texte = liste.Select(x => x.Text).Where(x => x.Length > 0).Distinct().ToList();
            res.Add(f with
            {
                Status = liste.Any(x => x.Status == "offen") ? "offen" : liste.Any(x => x.Status == "geaendert") ? "geaendert" : f.Status,
                FachAlt = liste.Select(x => x.FachAlt).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                Klassen = Vereinigen(liste.SelectMany(x => x.Klassen)),
                Lehrer = Vereinigen(liste.SelectMany(x => x.Lehrer)),
                Raeume = Vereinigen(liste.SelectMany(x => x.Raeume)),
                Art = liste.Select(x => x.Art).FirstOrDefault(x => x.Length > 0) ?? "",
                Text = string.Join(" · ", texte),
                Vertretung = liste.Select(x => x.Vertretung).FirstOrDefault(x => x.HasValue),
                VertretungOffen = liste.Any(x => x.VertretungOffen),
                Mitklassen = Vereinigen(liste.SelectMany(x => x.Mitklassen)).Where(w => !liste.Any(x => x.Klassen.Any(kl => Ci.Equals(kl.K, w.K.Length > 0 ? w.K : w.Alt)))).ToList(),
            });
        }
        return res;
    }

    /// <summary>Veranstaltungskarte um Räume, Lehrkräfte (und Klassen) der zugehörigen Veranstaltung aus den Terminen ergänzen.</summary>
    private static Karte MitTermin(Stand s, DateOnly d, Karte k)
    {
        var t = s.Paket.Termine.FirstOrDefault(x => x.Art != "klausur" && Ci.Equals(x.Titel.Trim(), k.Text.Trim()) &&
                                                    DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b);
        if (t is null) return k;
        var raeume = k.Raeume.Concat(t.Raeume.Where(r => !k.Raeume.Any(w => Ci.Equals(w.K, r))).Select(r => new Wert(r))).ToList();
        var klassen = k.Klassen.Count > 0 ? k.Klassen : t.Klassen.Select(x => new Wert(x)).ToList();
        // alle teilnehmenden Lehrkräfte der Veranstaltung (nicht nur die des angezeigten Plans)
        var lehrer = k.Lehrer.Concat(t.Lehrer.Where(l => !k.Lehrer.Any(w => Ci.Equals(w.K, l))).Select(l => new Wert(l))).ToList();
        return k with { Raeume = raeume, Klassen = klassen, Lehrer = lehrer };
    }

    /// <summary>
    /// Veranstaltungen, die ausdrücklich für dieses Element gelten (nicht schulweit) und noch nicht als Karte vorhanden sind –
    /// für die Tagesübersicht, damit z. B. ein Raum mit Veranstaltung dort erscheint.
    /// </summary>
    public List<Karte> EreignisKarten(Stand s, DateOnly d, string typ, string kurz, IReadOnlyCollection<Karte> vorhanden)
    {
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        return Ereignisse(s, d, typ, kurz)
            .Where(e => e.Klassen.Count + e.Lehrer.Count + e.Raeume.Count > 0 && !vorhanden.Any(k => k.Status == "veranstaltung" && Ci.Equals(k.Text, e.Titel)))
            .Select(e => new Karte(Iso(d), e.Von, e.Bis, zeiten.TryGetValue(e.Von, out var a) ? a.Beginn : "", zeiten.TryGetValue(e.Bis, out var b) ? b.Ende : "",
                "veranstaltung", "", null, "", "", e.Klassen.Select(x => new Wert(x)).ToList(),
                e.Lehrer.Select(x => new Wert(x)).ToList(), e.Raeume.Select(x => new Wert(x)).ToList(),
                "Veranstaltung", e.Titel, null, null, ""))
            .ToList();
    }

    /// <summary>Raumbuchung bzw. Raumänderung aus einer Buchung – erzeugt nie eine Vertretung, einen Vertretungstext oder eine Änderungsart.</summary>
    public static bool IstBuchung(string? art) => !string.IsNullOrEmpty(art) &&
        (art.StartsWith("Raumbuchung", StringComparison.OrdinalIgnoreCase) || art.StartsWith("Buchung", StringComparison.OrdinalIgnoreCase) ||
         art.StartsWith("Raumänderung (vorgemerkt)", StringComparison.OrdinalIgnoreCase));

    /// <summary>Klausur bzw. Prüfung – eigener Status „klausur“, nie eine Vertretung.</summary>
    public static bool IstKlausur(string? art) => !string.IsNullOrEmpty(art) &&
        (art.StartsWith("Klausur", StringComparison.OrdinalIgnoreCase) || art.StartsWith("Prüfung", StringComparison.OrdinalIgnoreCase));

    private Karte KarteBauen(Stand s, DateOnly d, string typ, string kurz, Rohstunde r, WebVertretung? v, Dictionary<int, WebStundenzeit> zeiten)
    {
        var art = r.Art.Length > 0 ? r.Art : v?.Art ?? "";
        // Buchungen (Raum) und Klausuren sind keine Vertretungen: kein Status „geändert“, keine Vertretungsart, kein Vertretungstext
        var buchung = r.Status == "vertreten" && IstBuchung(art);
        var klausur = r.Status == "vertreten" && IstKlausur(art);
        var status = buchung ? (IstBereitschaft(s, r.Fach) ? "bereitschaft" : "regulaer") : klausur ? "klausur" : r.Status switch
        {
            "entfall" => "entfall",
            "offen" => "offen",
            "vertreten" => "geaendert",
            "zusaetzlich" when art.StartsWith("Klausur", StringComparison.OrdinalIgnoreCase) => "klausur",
            "zusaetzlich" when art.StartsWith("Raumbuchung", StringComparison.OrdinalIgnoreCase) => "buchung",
            "zusaetzlich" when art.StartsWith("Veranst", StringComparison.OrdinalIgnoreCase) => "veranstaltung",
            "zusaetzlich" => "geaendert",
            "vorgemerkt" => "vorgemerkt",
            _ => IstBereitschaft(s, r.Fach) ? "bereitschaft" : "regulaer",
        };
        var fach = r.Fach;
        string? fachAlt = null;
        if (v != null && v.NeuesFach.Length > 0 && v.Fach.Length > 0 && !Ci.Equals(v.NeuesFach, v.Fach) && status != "entfall")
        {
            fach = v.NeuesFach;
            fachAlt = v.Fach;
        }
        if (fach.Length == 0 && v != null) fach = v.NeuesFach.Length > 0 ? v.NeuesFach : v.Fach;

        var lehrer = r.Lehrer.Select(l => new Wert(l)).ToList();
        var raeume = r.Raeume.Select(x => new Wert(x)).ToList();
        // Vertretung ohne Lehrkraft („+++“): die Stunde findet statt, die bisherige Lehrkraft steht durchgestrichen in der grünen Plakette
        var bisher = v != null && v.Lehrer.Length > 0 ? new List<string> { v.Lehrer } : r.LehrerAlt?.Where(x => x.Length > 0).ToList() ?? new List<string>();
        var ohneLehrer = r.Status == "vertreten" && r.Lehrer.Count == 0 && bisher.Count > 0 && (v is null || v.Vertreter is "" or "+" or "+++");
        if (ohneLehrer)
            lehrer = bisher.Select(x => new Wert("", x, true)).ToList();
        // offene Lehrervertretung: die abwesende Lehrkraft steht durchgestrichen im grünen Label ({ k: "", alt: "FIS", neu: true })
        var lehrerOffen = status == "offen" && (v != null || bisher.Count > 0);
        if (lehrerOffen)
        {
            var abwesend = bisher.Count > 0 ? bisher : r.Lehrer.Where(x => x.Length > 0 && x != "???").ToList();
            var weg = abwesend.Count > 0 ? abwesend.Distinct(Ci).Select(x => new Wert("", x, true)).ToList() : new List<Wert> { new("", null, true) };
            // Koppelstunde: anwesende Lehrkräfte derselben Stunde bleiben erhalten (nur die abwesende steht durchgestrichen)
            var anwesend = bisher.Count > 0 ? r.Lehrer.Where(x => x.Length > 0 && x is not ("???" or "---" or "+" or "+++") && !Enthaelt(abwesend, x)).Distinct(Ci).Select(x => new Wert(x)).ToList() : new List<Wert>();
            lehrer = anwesend.Concat(weg).ToList();
        }
        if (v != null)
        {
            if (lehrerOffen) { }
            else if (ohneLehrer) { }
            else if (status == "geaendert" && v.Lehrer.Length > 0 && v.Vertreter.Length > 0 && v.Vertreter is not ("+" or "+++" or "---" or "???") && !Ci.Equals(v.Lehrer, v.Vertreter))
            {
                lehrer = lehrer.Select(l => Ci.Equals(l.K, v.Vertreter) ? new Wert(l.K, v.Lehrer, true) : l).ToList();
                if (!lehrer.Any(l => l.Neu)) lehrer.Insert(0, new Wert(v.Vertreter, v.Lehrer, true));
            }
            else if (status == "entfall" && lehrer.Count == 0 && v.Lehrer.Length > 0)
                lehrer.Add(new Wert(v.Lehrer));
            // mehrere Räume kommen als „D017,C151“: einzeln vergleichen – unveränderte Räume bleiben, ersetzte als „alt → neu“,
            // zusätzliche als neuer Raum, entfernte durchgestrichen (keine doppelten Räume)
            var alteR = RaumListe(v.Raum);
            var neueR = RaumListe(v.NeuerRaum);
            if (status != "entfall" && neueR.Count > 0 && alteR.Count > 0 && !(alteR.Count == neueR.Count && alteR.All(x => neueR.Contains(x, Ci))) && v.NeuerRaum != "???")
            {
                if (neueR.Count == 1 && alteR.Count == 1)
                {
                    raeume = raeume.Select(x => Ci.Equals(x.K, neueR[0]) ? new Wert(x.K, alteR[0], true) : x).ToList();
                    if (!raeume.Any(x => x.Neu)) raeume.Insert(0, new Wert(neueR[0], alteR[0], true));
                }
                else raeume = RaeumeNachBuchung(alteR.Select(x => new Wert(x)).ToList(), neueR);
            }
            else if (raeume.Count == 0 && (v.Raum.Length > 0 || v.NeuerRaum.Length > 0))
                raeume.Add(new Wert(v.NeuerRaum.Length > 0 && status != "entfall" ? v.NeuerRaum : v.Raum));
            // Raum gelöscht bzw. für einen anderen Unterricht freigegeben (ältere Exporte ohne „???“): die vertretene Stunde hat keinen
            // Raum mehr, die Vertretung nennt aber weiter den bisherigen Raum als neuen – bisheriger Raum durchgestrichen im grünen Label
            var raumWeg = status == "geaendert" && r.Status == "vertreten" && r.Raeume.Count == 0 && alteR.Count > 0
                          && alteR.Count == neueR.Count && alteR.All(x => neueR.Contains(x, Ci));
            if (raumWeg) raeume = alteR.Select(x => new Wert("", x, true)).ToList();
            // offene Raumvertretung (Raum „???“): wie eine offene Lehrervertretung – bisheriger Raum durchgestrichen
            if (status != "entfall" && (v.NeuerRaum == "???" || raeume.Any(x => x.K == "???")))
            {
                var bisherRaum = v.Raum.Length > 0 && v.Raum != "???" ? v.Raum : null;
                raeume = raeume.Where(x => x.K != "???").ToList();
                raeume.RemoveAll(x => bisherRaum != null && Ci.Equals(x.K, bisherRaum));
                raeume.Insert(0, new Wert("", bisherRaum ?? "???", true));
                if (status is "regulaer" or "geaendert") status = "offen";
            }
            // Eigene Stunde wird von jemand anderem gehalten bzw. findet in einem anderen Raum statt
            if (typ == Elementtyp.Lehrer && Ci.Equals(v.Lehrer, kurz) && !Enthaelt(r.Lehrer, kurz) && status is "geaendert" or "offen")
                status = "abwesend";
            // (Raumplan: nur bei echter Raumänderung – fehlen die Räume der Stunde und bleibt der Raum laut Vertretung gleich, findet die
            // vertretene Stunde weiter hier statt und erscheint wie im Klassenplan als Änderung)
            var raumBleibt = !raumWeg && r.Raeume.Count == 0 && (v.NeuerRaum.Length == 0 || RaumListe(v.NeuerRaum).Contains(kurz, Ci));
            if (typ == Elementtyp.Raum && RaumListe(v.Raum).Contains(kurz, Ci) && !Enthaelt(r.Raeume, kurz) && ((status is "geaendert" && !raumBleibt) || buchung))
                status = "abwesend";
        }
        // Bereitschaft fällt weg: kein Entfall, sondern Abwesenheit der eingeteilten Lehrkraft (Kürzel durchgestrichen)
        var bereitschaft = r.Bereitschaft || IstBereitschaft(s, fach) || (v != null && v.Art.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase));
        if (bereitschaft && status == "entfall")
        {
            var eingeteilt = lehrer.Select(w => w.K.Length > 0 ? w.K : w.Alt ?? "").Where(x => x.Length > 0).ToList();
            if (eingeteilt.Count == 0 && v != null && v.Lehrer.Length > 0) eingeteilt.Add(v.Lehrer);
            // Raumplan: wie im Klassenplan eine grüne Änderungskarte, die abwesende Lehrkraft durchgestrichen im grünen Label
            // ({ k: "", alt: "MUE", neu: true }) – die Darstellung „Lehrkraft abwesend“ gilt nur im Plan der Lehrkraft selbst
            if (typ == Elementtyp.Raum)
            {
                status = "geaendert";
                lehrer = eingeteilt.Distinct(Ci).Select(x => new Wert("", x, true)).ToList();
            }
            else
            {
                status = "abwesend";
                lehrer = eingeteilt.Distinct(Ci).Select(x => new Wert("", x)).ToList();
            }
        }
        // zusätzlicher Unterricht ohne Lehrkraft: „---“
        var zusatz = r.Status == "zusaetzlich" && status == "geaendert";
        if (zusatz && lehrer.Count == 0) lehrer.Add(new Wert(OhneLehrkraftZusatz));
        var text = buchung ? "" : string.Join(" · ", new[] { r.Text, v != null && v.Text != r.Text ? v.Text : "" }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        if (buchung) art = "";
        // Vertretung ohne Lehrkraft ohne eigenen Text: Hinweis als Vertretungstext
        if (ohneLehrer && text.Length == 0) text = OhneLehrkraftText;
        text = TextNormal(text);
        // zusätzliche Unterrichtsstunde: kein automatisch erzeugter Vertretungstext (Badge „Zusätzl. Unterrichtsstunde“ statt Text);
        // von Hand eingegebene Texte bleiben (Info-Symbol auf der Karte)
        if (zusatz && fach.Length > 0) text = ZusatzText(text, art);
        s.Faecher.TryGetValue(fach, out var fe);
        var zb = zeiten.TryGetValue(r.Stunde, out var z) ? z : null;
        var verlegungsZeile = v != null && (v.Nach.Trim().Length > 0 || v.VertrVon.Trim().Length > 0) && status != "klausur";
        var karte = new Karte(Iso(d), r.Stunde, r.Stunde, zb?.Beginn ?? "", zb?.Ende ?? "", status, fach, fachAlt, fe?.Name ?? fach, fe?.Farbe ?? "",
            r.Klassen.Select(k => new Wert(k)).ToList(), lehrer, raeume, art, text, v?.Id ?? r.Vertretung, r.Unterricht, Gruppe(s, r))
        {
            Zusaetzlich = zusatz, Bereitschaft = bereitschaft, Klausur = r.Klausur,
            // abwesende Lehrkraft: eigene Stunde im Lehrerplan von anderen gehalten bzw. weggefallene Bereitschaft (Lehrer- und
            // Tagesansichten); im Raumplan nie – dort ist die Stunde wie im Klassenplan „geaendert“ bzw. „offen“
            LehrerAbwesend = status == "abwesend" && typ != Elementtyp.Raum && (typ == Elementtyp.Lehrer || bereitschaft),
            VertretungOffen = lehrerOffen,
            VerlegungsZeile = verlegungsZeile,
        };
        // zusätzliche Unterrichtsstunde (nicht die Zielstunde einer Verlegung): alle beteiligten Lehrkräfte als „+“-Label
        if (zusatz && karte.Verlegung is null) karte = karte with { Lehrer = ZusatzLehrer(karte.Lehrer) };
        return karte.Verlegung is null ? karte : VerlegungSetzen(karte, d, v, zeiten);
    }

    /// <summary>
    /// Ursprung und Ziel einer Verlegung: die eigene Seite aus der Karte, die Gegenseite aus der Vertretung („VertrVon“ bzw. „Nach“,
    /// Format „Fr-09.10. / 4“). Unbekannte Gegenseite: null.
    /// </summary>
    internal static Karte VerlegungSetzen(Karte k, DateOnly d, WebVertretung? v, IReadOnlyDictionary<int, WebStundenzeit> zeiten)
    {
        var eigen = new Verlegungszeit(k.Datum, k.Beginn, k.Ende, k.Von, k.Bis);
        var von = VerlegungsZeitLesen(v?.VertrVon, d, zeiten);
        var nach = VerlegungsZeitLesen(v?.Nach, d, zeiten);
        return k.Verlegung == "von"
            ? k with { VerlegungVon = eigen, VerlegungNach = nach }
            : k with { VerlegungVon = von, VerlegungNach = eigen };
    }

    private static readonly System.Text.RegularExpressions.Regex VerlegungsAngabe = new(@"(\d{1,2})\.(\d{1,2})\.?(\d{2,4})?\s*(?:/\s*(\d+))?", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>„Fr-09.10. / 4“ → Datum (Jahr: das dem Bezugsdatum nächstliegende) mit Beginn und Ende der Stunde; null, wenn leer bzw. unlesbar.</summary>
    public static Verlegungszeit? VerlegungsZeitLesen(string? text, DateOnly bezug, IReadOnlyDictionary<int, WebStundenzeit> zeiten)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = VerlegungsAngabe.Match(text);
        if (!m.Success) return null;
        var tag = int.Parse(m.Groups[1].Value);
        var monat = int.Parse(m.Groups[2].Value);
        if (monat is < 1 or > 12 || tag < 1) return null;
        DateOnly? datum = null;
        if (m.Groups[3].Success)
        {
            var j = int.Parse(m.Groups[3].Value);
            if (j < 100) j += 2000;
            if (tag <= DateTime.DaysInMonth(j, monat)) datum = new DateOnly(j, monat, tag);
        }
        else
            datum = new[] { bezug.Year - 1, bezug.Year, bezug.Year + 1 }.Where(j => tag <= DateTime.DaysInMonth(j, monat))
                .Select(j => new DateOnly(j, monat, tag)).OrderBy(x => Math.Abs(x.DayNumber - bezug.DayNumber)).Cast<DateOnly?>().FirstOrDefault();
        if (datum is not DateOnly dt) return null;
        var stunde = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
        var z = zeiten.TryGetValue(stunde, out var x) ? x : null;
        return new Verlegungszeit(Iso(dt), z?.Beginn ?? "", z?.Ende ?? "", stunde, stunde);
    }

    /// <summary>Verlegung über mehrere Stunden (Block): Gegenseite um die nächste Stunde verlängern, wenn sie direkt anschließt.</summary>
    private static Verlegungszeit? VerlegungVerbinden(Verlegungszeit? a, Verlegungszeit? b) =>
        a is null || b is null ? a : a.Datum == b.Datum && b.Stunde == a.BisStunde + 1 ? a with { BisStunde = b.BisStunde, Ende = b.Ende } : a;

    /// <summary>
    /// Automatisch erzeugte Standardtexte (Desktop bzw. Web), die bei einer zusätzlichen Unterrichtsstunde nicht als Vertretungstext
    /// erscheinen – dort steht das Badge „Zusätzl. Unterrichtsstunde“. Von Hand eingegebene Texte bleiben erhalten.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ZusatzStandardtext = new(
        @"^(wegen sondereinsatz|sondereinsatz|vertretung durch verlegung|vorziehung|vertr\. o\. lehrkr\.?|vertr\. o\. lehrkraft|zusatzunterricht|zusätzlicher unterricht|zusätzl\. unterrichtsstunde|zusätzliche unterrichtsstunde|mitbetreuung|aus vertretung \d+.*)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Vertretungstext einer zusätzlichen Unterrichtsstunde: nur von Hand eingegebene Teile (ohne Standardtexte und ohne die Art selbst).</summary>
    public static string ZusatzText(string text, string art) =>
        string.Join(" · ", text.Split(" · ").Select(t => t.Trim())
            .Where(t => t.Length > 0 && !ZusatzStandardtext.IsMatch(t) && !string.Equals(t, art.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>Schülergruppe des Unterrichts (Kopplungszeile mit gleichem Fach bzw. gleicher Lehrkraft), sonst leer.</summary>
    private static string Gruppe(Stand s, Rohstunde r)
    {
        if (r.Unterricht is not int u || !s.Unterricht.TryGetValue(u, out var un)) return "";
        var zeile = un.Zeilen.FirstOrDefault(z => Ci.Equals(z.Fach, r.Fach) && r.Lehrer.Any(l => Ci.Equals(l, z.Lehrer)))
                    ?? un.Zeilen.FirstOrDefault(z => Ci.Equals(z.Fach, r.Fach)) ?? (un.Zeilen.Count == 1 ? un.Zeilen[0] : null);
        return zeile?.Gruppe ?? "";
    }

    private static string Schluessel(Karte k) =>
        $"{k.Unterricht}|{k.Status}|{k.Fach}|{k.FachAlt}|{k.Art}|{k.Text}|{string.Join(",", k.Klassen.Select(x => x.K))}|" +
        $"{string.Join(",", k.Lehrer.Select(x => x.K + "/" + x.Alt))}|{string.Join(",", k.Raeume.Select(x => x.K + "/" + x.Alt))}|{string.Join(",", k.Mitklassen.Select(x => x.K + "/" + x.Alt))}";

    private static List<Karte> Zusammenfassen(List<Karte> einzel, Dictionary<int, WebStundenzeit> zeiten)
    {
        var res = new List<Karte>();
        foreach (var g in einzel.GroupBy(Schluessel))
        {
            Karte? offen = null;
            foreach (var k in g.OrderBy(x => x.Von))
            {
                if (offen != null && k.Von == offen.Bis + 1)
                {
                    offen = offen with
                    {
                        Bis = k.Bis, Ende = k.Ende,
                        VerlegungVon = offen.Verlegung == "von" && offen.VerlegungVon is { } ev ? ev with { BisStunde = k.Bis, Ende = k.Ende } : VerlegungVerbinden(offen.VerlegungVon, k.VerlegungVon),
                        VerlegungNach = offen.Verlegung == "nach" && offen.VerlegungNach is { } en ? en with { BisStunde = k.Bis, Ende = k.Ende } : VerlegungVerbinden(offen.VerlegungNach, k.VerlegungNach),
                    };
                    continue;
                }
                if (offen != null) res.Add(offen);
                offen = k;
            }
            if (offen != null) res.Add(offen);
        }
        // doppelte Karten (gleiche Stunde, gleicher Inhalt aus mehreren Kopplungszeilen) entfernen
        return res.GroupBy(k => (k.Von, k.Bis, Schluessel(k))).Select(g => g.First()).OrderBy(k => k.Von).ThenBy(k => k.Fach).ToList();
    }

    /// <summary>
    /// Lehrkräfte einer zusätzlichen Unterrichtsstunde: alle Beteiligten (auch ein als „ersetzt“ gemeldeter alter Wert) je einmal als
    /// hinzugekommener Wert ({ k, neu } → „+CS“) – nie durchgestrichen und nie „alt → neu“. Ohne Lehrkraft: „---“.
    /// </summary>
    public static List<Wert> ZusatzLehrer(IEnumerable<Wert> lehrer)
    {
        var alle = lehrer.SelectMany(w => new[] { w.K, w.Alt ?? "" }).Select(x => x.Trim())
            .Where(x => x.Length > 0 && x is not ("---" or "???" or "+" or "+++")).Distinct(Ci).ToList();
        return alle.Count == 0 ? new List<Wert> { new(OhneLehrkraftZusatz) } : alle.Select(x => new Wert(x, null, true)).ToList();
    }

    /// <summary>Nimmt eine Abwesenheit (Lehrkraft) bzw. Veranstaltung am Tag d die Pause nach Stunde <paramref name="nach"/> ein?</summary>
    private static bool PauseBelegt(DateOnly d, string von, string bis, int stundeVon, int stundeBis, int nach)
    {
        if (!DateOnly.TryParse(von, out var v) || !DateOnly.TryParse(bis, out var b) || d < v || d > b) return false;
        // erster Tag ab StundeVon, letzter Tag bis StundeBis (0 bzw. ≥ 20 = bis Tagesende), dazwischen ganztägig
        var ab = d == v ? Math.Max(1, stundeVon) : 1;
        var ende = d == b && stundeBis > 0 && stundeBis < 20 ? stundeBis : int.MaxValue;
        // die Pause nach Stunde n liegt zwischen n und n + 1 (vor der 1. Stunde: n = 0)
        return ab <= Math.Max(1, nach) && ende >= nach + 1;
    }

    /// <summary>
    /// Grund, aus dem eine Aufsicht ohne Vertretung entfällt: die Lehrkraft ist abwesend bzw. nimmt an einer Veranstaltung teil, die die
    /// Pause einschließt; null = findet statt.
    /// </summary>
    private static string? AufsichtEntfallGrund(Stand s, DateOnly d, string lehrer, int nach)
    {
        if (s.Paket.Absenzen.Any(a => a.Art == "lehrer" && Ci.Equals(a.Element, lehrer) && PauseBelegt(d, a.Von, a.Bis, a.StundeVon, a.StundeBis, nach)))
            return "Abwesenheit";
        var t = s.Paket.Termine.FirstOrDefault(x => x.Art == "veranstaltung" && (x.AlleLehrer || Enthaelt(x.Lehrer, lehrer))
                                                    && PauseBelegt(d, x.Von, x.Bis, x.StundeVon, x.StundeBis, nach));
        return t is null ? null : t.Titel.Trim().Length > 0 ? t.Titel.Trim() : "Veranstaltung";
    }

    /// <summary>
    /// Pausenaufsichten einer Lehrkraft an einem Tag (regelmäßige und Vertretungen). Status: regulaer, vertreten (jemand anderes bzw.
    /// noch niemand – „???“ – übernimmt), vertretung (zusätzlich übernommen), entfall (findet nicht statt: Vertretung „---“ bzw. ohne
    /// Vertretung, weil die Lehrkraft abwesend ist oder an einer Veranstaltung teilnimmt – Grund in <see cref="Aufsicht.Grund"/>).
    /// </summary>
    public List<Aufsicht> Aufsichten(Stand s, DateOnly d, string lehrer)
    {
        var res = new List<Aufsicht>();
        if (Frei(s, d).Frei) return res;
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        (string, string) Pause(int nach) => (zeiten.TryGetValue(nach, out var a) ? a.Ende : "", zeiten.TryGetValue(nach + 1, out var b) ? b.Beginn : "");
        string GangName(string g) => s.Gaenge.TryGetValue(g, out var x) && x.Name.Length > 0 ? x.Name : g;
        var woche = Woche(s, d);
        var t = TagIndex(d);
        var vertretungen = s.Paket.Vertretungen.Where(v => v.Datum == Iso(d) && v.NurLehrer && v.Art.Contains("Pausenaufsicht", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var a in s.Paket.Pausenaufsichten.Where(a => a.Tag == t && Ci.Equals(a.Lehrer, lehrer) && (woche.Length == 0 || a.Wochen.Length == 0 || a.Wochen.Contains(woche))))
        {
            var (b, e) = Pause(a.NachStunde);
            var v = vertretungen.FirstOrDefault(x => x.Stunde == a.NachStunde && Ci.Equals(x.Lehrer, lehrer));
            // ausdrücklich entfallen („---“ bzw. Art „Entfall“) oder ohne Vertretung bei Abwesenheit bzw. Veranstaltung
            var entfall = v != null
                ? (v.Vertreter.Trim() == "---" || v.Art.Contains("Entfall", StringComparison.OrdinalIgnoreCase) ? (v.Text.Trim().Length > 0 ? v.Text.Trim() : "") : null)
                : AufsichtEntfallGrund(s, d, lehrer, a.NachStunde);
            res.Add(entfall is not null
                ? new Aufsicht(Iso(d), a.NachStunde, b, e, a.Gang, GangName(a.Gang), "entfall", lehrer, null) { Grund = entfall.Length > 0 ? entfall : null }
                : v is null
                ? new Aufsicht(Iso(d), a.NachStunde, b, e, a.Gang, GangName(a.Gang), "regulaer", lehrer, null)
                : new Aufsicht(Iso(d), a.NachStunde, b, e, a.Gang, GangName(a.Gang), "vertreten", v.Vertreter is "" or "???" ? "???" : v.Vertreter, lehrer));
        }
        foreach (var v in vertretungen.Where(x => Ci.Equals(x.Vertreter, lehrer) && !Ci.Equals(x.Lehrer, lehrer)))
        {
            var (b, e) = Pause(v.Stunde);
            var gang = v.NeuerRaum.Length > 0 ? v.NeuerRaum : v.Raum;
            res.Add(new Aufsicht(Iso(d), v.Stunde, b, e, gang, GangName(gang), "vertretung", lehrer, v.Lehrer));
        }
        return res.OrderBy(x => x.NachStunde).ToList();
    }

    /// <summary>Hinweise im Tageskopf: Veranstaltungen, Abwesenheiten (Kollegium), Raumsperren.</summary>
    public List<string> Hinweise(Stand s, DateOnly d, string typ, string kurz, bool kollegium)
    {
        var res = new List<string>();
        string Std(int von, int bis) => bis >= 20 || bis < von ? $"ab {von}. Std." : von == bis ? $"{von}. Std." : $"{von}.–{bis}. Std.";
        foreach (var a in s.Paket.Absenzen.Where(x => DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b && Ci.Equals(x.Element, kurz)))
        {
            if (a.Art == "lehrer" && typ == Elementtyp.Lehrer && kollegium) res.Add($"abwesend {Std(a.StundeVon, a.StundeBis)}{(string.IsNullOrEmpty(a.Grund) ? "" : " – " + a.Grund)}");
            if (a.Art == "klasse" && typ == Elementtyp.Klasse) res.Add($"Klasse abwesend {Std(a.StundeVon, a.StundeBis)}{(string.IsNullOrEmpty(a.Grund) || !kollegium ? "" : " – " + a.Grund)}");
            if (a.Art == "raum" && typ == Elementtyp.Raum) res.Add($"Raum gesperrt {Std(a.StundeVon, a.StundeBis)}");
        }
        return res;
    }

    public Tageskopf Kopf(Stand s, DateOnly d, string typ, string kurz, bool kollegium)
    {
        var (frei, grund) = Frei(s, d);
        return new Tageskopf(Iso(d), Wochentage[TagIndex(d)], Woche(s, d), frei, grund, s.Tage.ContainsKey(Iso(d)),
            frei ? new List<string>() : Hinweise(s, d, typ, kurz, kollegium), frei ? new List<Ereignis>() : Ereignisse(s, d, typ, kurz),
            !frei ? null : Ferien(s, d) is { } f ? (f.Feiertag ? "feiertag" : "ferien") : TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? "wochenende" : "frei",
            frei ? Ferien(s, d)?.Von : null, frei ? Ferien(s, d)?.Bis : null);
    }

    /// <summary>
    /// Veranstaltungen eines Tages für ein Element (ohne Klausuren, die als Karten erscheinen). Veranstaltungen ohne
    /// zugeordnete Elemente gelten für die ganze Schule. Mehrtägige Veranstaltungen beginnen am ersten Tag mit
    /// StundeVon und enden am letzten Tag mit StundeBis; dazwischen sind sie ganztägig.
    /// </summary>
    public List<Ereignis> Ereignisse(Stand s, DateOnly d, string typ, string kurz)
    {
        var letzte = s.Paket.Zeitraster.Stunden.Count > 0 ? s.Paket.Zeitraster.Stunden.Max(z => z.Nr) : 10;
        var res = new List<Ereignis>();
        foreach (var t in s.Paket.Termine.Where(x => x.Art != "klausur" && DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b))
        {
            var schulweit = t.Klassen.Count == 0 && t.Lehrer.Count == 0 && t.Raeume.Count == 0;
            var betrifft = schulweit || typ switch
            {
                Elementtyp.Klasse => Enthaelt(t.Klassen, kurz),
                Elementtyp.Lehrer => Enthaelt(t.Lehrer, kurz),
                Elementtyp.Raum => Enthaelt(t.Raeume, kurz),
                _ => kurz.Length == 0,
            };
            if (!betrifft) continue;
            var von = DateOnly.Parse(t.Von) == d ? Math.Max(1, t.StundeVon) : 1;
            var bis = DateOnly.Parse(t.Bis) == d && t.StundeBis > 0 ? Math.Min(t.StundeBis, letzte) : letzte;
            if (bis < von) bis = von;
            res.Add(new Ereignis(t.Titel, t.Art, von, bis, von <= 1 && bis >= letzte, t.Klassen, t.Lehrer, t.Raeume));
        }
        return res.OrderBy(e => !e.Ganztaegig).ThenBy(e => e.Von).ToList();
    }

    /// <summary>Wochenplan eines Elements ab Montag.</summary>
    public Wochenplan Wochenplan(Stand s, string typ, string kurz, DateOnly datum, bool kollegium, Func<DateOnly, IEnumerable<Rohstunde>>? zusaetzlich = null)
    {
        var montag = Montag(datum);
        var tage = Math.Clamp(s.Paket.Zeitraster.TageProWoche, 1, 7);
        var koepfe = new List<Tageskopf>();
        var karten = new List<Karte>();
        var aufsichten = new List<Aufsicht>();
        for (var i = 0; i < tage; i++)
        {
            var d = montag.AddDays(i);
            var kopf = Kopf(s, d, typ, kurz, kollegium);
            koepfe.Add(kopf);
            if (kopf.Frei) continue;
            karten.AddRange(Karten(s, d, typ, kurz, zusaetzlich?.Invoke(d)));
            if (typ == Elementtyp.Lehrer && kollegium) aufsichten.AddRange(Aufsichten(s, d, kurz));
        }
        return new Wochenplan(typ, kurz, Name(s, typ, kurz), Iso(montag), Woche(s, montag), Zeitraster(s), koepfe, karten, aufsichten);
    }

    /// <summary>Tagesübersicht aller Klassen, Lehrkräfte bzw. Räume (nur Elemente mit Stunden).</summary>
    public Tagesuebersicht Tag(Stand s, string typ, DateOnly d, bool kollegium, Func<DateOnly, IEnumerable<Rohstunde>>? zusaetzlich = null)
    {
        var kopf = Kopf(s, d, "", "", kollegium);
        var zeilen = new List<Tageszeile>();
        if (!kopf.Frei)
        {
            var extra = zusaetzlich?.Invoke(d).ToList();
            IEnumerable<WebElement> elemente = typ switch
            {
                Elementtyp.Klasse => s.Paket.Elemente.Klassen,
                Elementtyp.Lehrer => s.Paket.Elemente.Lehrer,
                _ => s.Paket.Elemente.Raeume,
            };
            foreach (var e in elemente.OrderBy(e => e.Kurz, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true)))
            {
                var k = Karten(s, d, typ, e.Kurz, extra);
                k = k.Concat(EreignisKarten(s, d, typ, e.Kurz, k)).OrderBy(x => x.Von).ToList();
                if (k.Count > 0) zeilen.Add(new Tageszeile(e.Kurz, e.Name, k));
            }
        }
        return new Tagesuebersicht(typ, Iso(d), kopf, Zeitraster(s), zeilen);
    }
}
