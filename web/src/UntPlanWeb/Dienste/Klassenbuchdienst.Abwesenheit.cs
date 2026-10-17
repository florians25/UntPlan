using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Fehlzeit als Zeile der Fehlzeitenliste (eine Unterrichtsstunde laut Stundenplan): Datum, Zeit der Stunde, Fach, Lehrkraft,
/// Fehltage (1 nur in der letzten Stunde eines ganz gefehlten Tages), Fehlstunden, Fehlminuten, „zählt“ (als Fehlstunde gewertet),
/// Abwesenheitsgrund, Status und Text der zugehörigen Abwesenheit.
/// </summary>
public sealed record Fehlzeile(int Id, int SchuelerId, string Schueler, string Klasse, string Datum, int Stunde, string Beginn, string Ende,
    string AbwesendVon, string AbwesendBis, string Fach, string Lehrer, int Fehltage, int Fehlstunden, int Fehlminuten, bool Zaehlt,
    string Art, string Grund, string Status, string Text, string Gruppe);

/// <summary>
/// Abwesenheit als Zeitraum (von Datum/Uhrzeit bis Datum/Uhrzeit) mit Grund, Status und Text. Sie fasst die Fehlzeiten zusammen,
/// die aus ihr laut Stundenplan entstehen (Ids), samt Summe der Fehlstunden und Fehlminuten.
/// </summary>
public sealed record Abwesenheitszeitraum(string Schluessel, int SchuelerId, string Schueler, string Klasse, string VonDatum, string VonZeit,
    string BisDatum, string BisZeit, string Art, int Minuten, string Grund, string Status, string Text, int Fehlstunden, int Fehlminuten,
    List<int> Ids, List<string> Faecher, List<string> Lehrer, bool Verwalten, string Gruppe, string Erstellt)
{
    public string Nachweis { get; init; } = "";
    public string EntschuldigtAm { get; init; } = "";
    public bool ElternInformiert { get; init; }
    /// <summary>Interne Notiz – nur für die Klassenleitung befüllt.</summary>
    public string NotizIntern { get; init; } = "";
    /// <summary>
    /// Die Fehlstunden der Abwesenheit haben verschiedene Status (z. B. eine Stunde in den Fehlzeiten einzeln entschuldigt) – Status ist dann
    /// „offen“, Anzahl je Status zeigen Entschuldigt/Unentschuldigt (gleiche Zählung wie die Fehlzeiten).
    /// </summary>
    public bool StatusGemischt { get; init; }
    public int Entschuldigt { get; init; }
    public int Unentschuldigt { get; init; }
}

/// <summary>
/// Abwesenheiten minutengenau: Eine Abwesenheit ist ein Zeitraum (z. B. Mittwoch 07:45 bis 14:45 oder 08:00 bis 08:12 bei einer
/// Verspätung); gespeichert werden die daraus entstehenden Fehlzeiten je Unterrichtsstunde mit dem Abwesenheitsfenster in der Stunde
/// (Spalten beginn/ende). Verspätung = abwesend von Stundenbeginn bis zur Ankunft, vorzeitig gegangen = abwesend ab der Uhrzeit bis
/// Stundenende. Bestehende Einträge ohne Uhrzeit (nur Art und Minuten) werden daraus abgeleitet.
/// </summary>
public sealed partial class Klassenbuchdienst
{
    private static readonly Regex Resthinweis = new(@"^(?<art>verspätet|vorzeitig gegangen) (?<min>\d+) min", RegexOptions.CultureInvariant);
    private static readonly Regex Hinweistext = new(@"^(?:(?:verspätet|vorzeitig gegangen)(?: \(\d+ min\)| \d+ min)|vorzeitig gegangen \(\d+\. Std\.\))(?: – (?<text>.*))?$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Gruppe einer Abwesenheit (über mehrere Stunden bzw. Tage), die in UntPlanWeb als Zeitraum erfasst wurde.</summary>
    public const string GruppenPraefix = "abw:";

    private static bool Zeitraumgruppe(string g) => g.Length > 0 && !g.StartsWith("vorzeitig:", StringComparison.Ordinal) && !g.StartsWith(TagesendePraefix, StringComparison.Ordinal);

    /// <summary>Beginn und Ende einer Stunde laut Zeitraster (ohne Angabe: 45 Minuten ab 08:00 je Stunde).</summary>
    public static (TimeOnly Beginn, TimeOnly Ende) StundenZeit(Stand s, int nr)
    {
        var z = s.Paket.Zeitraster.Stunden.FirstOrDefault(x => x.Nr == nr);
        if (z != null && TimeOnly.TryParse(z.Beginn, CultureInfo.InvariantCulture, out var b) && TimeOnly.TryParse(z.Ende, CultureInfo.InvariantCulture, out var e) && e > b) return (b, e);
        var start = new TimeOnly(8, 0).AddMinutes(Math.Max(0, nr - 1) * 45);
        return (start, start.AddMinutes(45));
    }

    private static string Hhmm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static TimeOnly? Uhrzeit(string? s) => TimeOnly.TryParseExact((s ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Abwesenheitsfenster einer Fehlzeit innerhalb ihrer Stunde.</summary>
    public static (TimeOnly Beginn, TimeOnly Ende) Fenster(Stand s, Fehlzeit f)
    {
        var (b, e) = StundenZeit(s, f.Stunde);
        if (Uhrzeit(f.Beginn) is TimeOnly fb && Uhrzeit(f.Ende) is TimeOnly fe && fe > fb)
            return (fb < b ? b : fb, fe > e ? e : fe) is var (x, y) && y > x ? (x, y) : (b, e);
        var art = f.Art;
        var min = f.Minuten;
        if (art == "fehlt" && Resthinweis.Match(f.Bemerkung) is { Success: true } m)
        {
            art = m.Groups["art"].Value == "verspätet" ? "verspaetet" : "vorzeitig";
            min = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
        }
        var laenge = (int)(e - b).TotalMinutes;
        if (art == "verspaetet" && min > 0 && min < laenge) return (b, b.AddMinutes(min));
        if (art == "vorzeitig" && min > 0 && min < laenge) return (e.AddMinutes(-min), e);
        return (b, e);
    }

    /// <summary>Text einer Abwesenheit ohne die automatischen Hinweise („verspätet 12 min – …“, „vorzeitig gegangen (3. Std.)“).</summary>
    public static string Klartext(string bemerkung) =>
        Hinweistext.Match(bemerkung ?? "") is { Success: true } m ? (m.Groups["text"].Success ? m.Groups["text"].Value : "") : bemerkung ?? "";

    /// <summary>Abschnitt einer Abwesenheit in einer Stunde: Art (fehlt/verspaetet/vorzeitig), Minuten, Fenster (leer = ganze Stunde), Hinweis.</summary>
    public readonly record struct Abschnitt(int Stunde, string Art, int Minuten, string Beginn, string Ende, string Hinweis);

    /// <summary>
    /// Zerlegt ein Abwesenheitsfenster (von–bis an einem Tag) auf die Stunden: ganz verpasste Stunden sind Fehlstunden, angebrochene
    /// Verspätung (abwesend ab Stundenbeginn) bzw. vorzeitiges Gehen – ab der Schwelle der Verspätungsregeln ebenfalls Fehlstunden.
    /// </summary>
    public static List<Abschnitt> AusFenster(Stand s, Verspaetungsregeln regeln, IEnumerable<int> stunden, TimeOnly von, TimeOnly bis)
    {
        var res = new List<Abschnitt>();
        foreach (var n in stunden.Distinct().OrderBy(x => x))
        {
            var (b, e) = StundenZeit(s, n);
            var ab = von > b ? von : b;
            var bi = bis < e ? bis : e;
            if (bi <= ab) continue;
            if (ab == b && bi == e) { res.Add(new(n, "fehlt", 0, "", "", "")); continue; }
            var min = (int)Math.Round((bi - ab).TotalMinutes);
            var art = ab == b ? "verspaetet" : "vorzeitig";
            var text = art == "verspaetet" ? "verspätet" : "vorzeitig gegangen";
            if (regeln.AbwesendAbMinuten > 0 && min >= regeln.AbwesendAbMinuten) res.Add(new(n, "fehlt", 0, Hhmm(ab), Hhmm(bi), $"{text} {min} min"));
            else res.Add(new(n, art, min, Hhmm(ab), Hhmm(bi), ""));
        }
        return res;
    }

    /// <summary>Fenster (HH:mm) zu einem Abschnitt aus Art und Minuten (Verspätung ab Stundenbeginn, vorzeitig bis Stundenende); ganze Stunde = leer.</summary>
    private static (string Beginn, string Ende) FensterAusMinuten(Stand s, int n, string art, int minuten, string hinweis)
    {
        if (art == "fehlt" && Resthinweis.Match(hinweis) is { Success: true } m)
        {
            art = m.Groups["art"].Value == "verspätet" ? "verspaetet" : "vorzeitig";
            minuten = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
        }
        if (art == "fehlt" || minuten <= 0) return ("", "");
        var (b, e) = StundenZeit(s, n);
        if (minuten >= (e - b).TotalMinutes) return ("", "");
        return art == "verspaetet" ? (Hhmm(b), Hhmm(b.AddMinutes(minuten))) : (Hhmm(e.AddMinutes(-minuten)), Hhmm(e));
    }

    // ------------------------------------------------------------------------------------------ Fehlzeiten (je Stunde)

    /// <summary>Lehrkraft einer Stunde laut Plan (bei mehreren kommagetrennt); ohne Treffer die erfassende Lehrkraft.</summary>
    private Func<Fehlzeit, string> LehrerFinder(Stand s)
    {
        var karten = new Dictionary<(string, string), List<Karte>>();
        return f =>
        {
            if (!karten.TryGetValue((f.Klasse, f.Datum), out var liste))
                karten[(f.Klasse, f.Datum)] = liste = DateOnly.TryParse(f.Datum, CultureInfo.InvariantCulture, out var d) ? _plan.Karten(s, d, Elementtyp.Klasse, f.Klasse) : new();
            var k = liste.FirstOrDefault(x => x.Unterricht == f.Unterricht && f.Stunde >= x.Von && f.Stunde <= x.Bis) ?? liste.FirstOrDefault(x => f.Stunde >= x.Von && f.Stunde <= x.Bis && Ci.Equals(x.Fach, f.Fach));
            var l = k?.Lehrer.Select(w => w.K).Where(w => w.Length > 0).ToList();
            return l is { Count: > 0 } ? string.Join(", ", l) : f.Lehrer;
        };
    }

    /// <summary>Unterrichtsstunden einer Klasse an einem Tag laut Plan (ohne Entfall und Veranstaltungen).</summary>
    private Func<string, string, HashSet<int>> StundenFinder(Stand s)
    {
        var cache = new Dictionary<(string, string), HashSet<int>>();
        return (klasse, datum) =>
        {
            if (cache.TryGetValue((klasse, datum), out var h)) return h;
            h = new HashSet<int>();
            if (DateOnly.TryParse(datum, CultureInfo.InvariantCulture, out var d) && !_plan.Frei(s, d).Frei)
                foreach (var k in _plan.Karten(s, d, Elementtyp.Klasse, klasse).Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung")))
                    for (var n = k.Von; n <= k.Bis; n++) h.Add(n);
            return cache[(klasse, datum)] = h;
        };
    }

    /// <summary>
    /// Fehlzeitenliste: je Fehlzeit (Unterrichtsstunde) eine Zeile mit Fehlstunden, Fehlminuten (minutengenau aus dem Abwesenheitsfenster)
    /// und Fehltagen – ein Fehltag zählt in der letzten Stunde eines Tages, an dem alle Unterrichtsstunden als Fehlstunden gewertet sind.
    /// </summary>
    public List<Fehlzeile> Fehlzeilen(Stand s, IEnumerable<Fehlzeit> liste)
    {
        var namen = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => $"{g.First().Name}, {g.First().Vorname}");
        var lehrer = LehrerFinder(s);
        var stunden = StundenFinder(s);
        var alle = liste.OrderBy(f => f.Datum, StringComparer.Ordinal).ThenBy(f => f.Stunde).ToList();
        // ganze Tage: alle Unterrichtsstunden des Tages als Fehlstunde gewertet → Fehltag in der letzten Stunde
        var fehltag = new HashSet<int>();
        foreach (var g in alle.GroupBy(f => (f.SchuelerId, f.Datum)))
        {
            var gewertet = g.Where(f => f.Art == "fehlt").ToList();
            if (gewertet.Count == 0) continue;
            var plan = stunden(g.First().Klasse, g.Key.Datum);
            var voll = plan.Count > 0 ? plan.All(n => gewertet.Any(f => f.Stunde == n)) : gewertet.Count == g.Count();
            if (voll) fehltag.Add(gewertet.OrderBy(f => f.Stunde).Last().Id);
        }
        return alle.Select(f =>
        {
            var (b, e) = StundenZeit(s, f.Stunde);
            var (ab, bi) = Fenster(s, f);
            var zaehlt = f.Art == "fehlt";
            return new Fehlzeile(f.Id, f.SchuelerId, namen.GetValueOrDefault(f.SchuelerId, $"#{f.SchuelerId}"), f.Klasse, f.Datum, f.Stunde, Hhmm(b), Hhmm(e),
                Hhmm(ab), Hhmm(bi), f.Fach, lehrer(f), fehltag.Contains(f.Id) ? 1 : 0, zaehlt ? 1 : 0, (int)Math.Round((bi - ab).TotalMinutes), zaehlt,
                f.Art, f.Grund, f.Status, Klartext(f.Bemerkung), f.Gruppe);
        }).ToList();
    }

    // ------------------------------------------------------------------------------------------ Abwesenheiten (Zeiträume)

    /// <summary>
    /// Fasst Fehlzeiten zu Abwesenheiten (Zeiträumen) zusammen: Einträge derselben Abwesenheitsgruppe (auch über mehrere Tage), sonst je Tag
    /// aufeinanderfolgende Stunden (Freistunden der Klasse dazwischen zählen nicht als Unterbrechung) mit gleichem Grund, Status und Text,
    /// wenn das Fenster lückenlos anschließt.
    /// </summary>
    public List<Abwesenheitszeitraum> Zeitraeume(Stand s, IEnumerable<Fehlzeit> liste, Func<string, bool> verwalten)
    {
        var namen = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => $"{g.First().Name}, {g.First().Vorname}");
        var lehrer = LehrerFinder(s);
        var stunden = StundenFinder(s);
        var bloecke = new List<List<Fehlzeit>>();
        foreach (var proSchueler in liste.GroupBy(f => f.SchuelerId))
        {
            var sortiert = proSchueler.OrderBy(f => f.Datum, StringComparer.Ordinal).ThenBy(f => f.Stunde).ToList();
            foreach (var g in sortiert.Where(f => Zeitraumgruppe(f.Gruppe)).GroupBy(f => f.Gruppe)) bloecke.Add(g.ToList());
            foreach (var tag in sortiert.Where(f => !Zeitraumgruppe(f.Gruppe)).GroupBy(f => f.Datum))
            {
                var plan = stunden(tag.First().Klasse, tag.Key);
                List<Fehlzeit>? block = null;
                foreach (var f in tag)
                {
                    var vorher = block?[^1];
                    var anschluss = vorher != null && vorher.Status == f.Status && vorher.Grund == f.Grund && Klartext(vorher.Bemerkung) == Klartext(f.Bemerkung)
                        && !plan.Any(n => n > vorher.Stunde && n < f.Stunde && !tag.Any(x => x.Stunde == n))
                        && Fenster(s, vorher).Ende == StundenZeit(s, vorher.Stunde).Ende && Fenster(s, f).Beginn == StundenZeit(s, f.Stunde).Beginn;
                    if (anschluss) block!.Add(f);
                    else bloecke.Add(block = new List<Fehlzeit> { f });
                }
            }
        }
        return bloecke.Select(blk =>
        {
            var f0 = blk[0];
            var fl = blk[^1];
            var fenster = blk.Select(f => (f, w: Fenster(s, f), z: StundenZeit(s, f.Stunde))).ToList();
            var minuten = fenster.Sum(x => (int)Math.Round((x.w.Ende - x.w.Beginn).TotalMinutes));
            // Art: verspätet = endet in einer angebrochenen Stunde, die ab Stundenbeginn fehlt; vorzeitig = beginnt in einer angebrochenen Stunde
            var erst = fenster[0];
            var letzt = fenster[^1];
            var teilErst = erst.w != erst.z;
            var teilLetzt = letzt.w != letzt.z;
            int Min((TimeOnly Beginn, TimeOnly Ende) w) => (int)Math.Round((w.Ende - w.Beginn).TotalMinutes);
            string art;
            int artMinuten;
            if (blk.Count == 1) (art, artMinuten) = !teilErst ? ("fehlt", 0) : erst.w.Beginn == erst.z.Beginn ? ("verspaetet", minuten) : ("vorzeitig", minuten);
            else if (teilErst && erst.w.Ende == erst.z.Ende && erst.w.Beginn != erst.z.Beginn) (art, artMinuten) = ("vorzeitig", Min(erst.w));
            else if (teilLetzt && letzt.w.Beginn == letzt.z.Beginn) (art, artMinuten) = ("verspaetet", minuten);
            else (art, artMinuten) = ("fehlt", 0);
            var status = blk.All(f => f.Status == f0.Status) ? f0.Status : "offen";
            var darf = verwalten(f0.Klasse);
            return new Abwesenheitszeitraum($"{f0.SchuelerId}:{f0.Id}", f0.SchuelerId, namen.GetValueOrDefault(f0.SchuelerId, $"#{f0.SchuelerId}"), f0.Klasse,
                f0.Datum, Hhmm(erst.w.Beginn), fl.Datum, Hhmm(letzt.w.Ende), art, artMinuten, f0.Grund, status,
                blk.Select(f => Klartext(f.Bemerkung)).FirstOrDefault(t => t.Length > 0) ?? "", blk.Count(f => f.Art == "fehlt"), minuten,
                blk.Select(f => f.Id).ToList(), blk.Select(f => f.Fach).Where(x => x.Length > 0).Distinct(Ci).ToList(),
                blk.Select(f => f.Lehrer).Where(x => x.Length > 0).Distinct(Ci).ToList(), darf, Zeitraumgruppe(f0.Gruppe) ? f0.Gruppe : "", f0.Erstellt)
            {
                Nachweis = f0.Nachweis, EntschuldigtAm = f0.EntschuldigtAm, ElternInformiert = f0.ElternInformiert, NotizIntern = darf ? f0.NotizIntern : "",
                StatusGemischt = blk.Any(f => f.Status != f0.Status),
                Entschuldigt = blk.Count(f => f.Art == "fehlt" && f.Status == "entschuldigt"), Unentschuldigt = blk.Count(f => f.Art == "fehlt" && f.Status == "unentschuldigt"),
            };
        }).OrderByDescending(z => z.VonDatum, StringComparer.Ordinal).ThenByDescending(z => z.VonZeit, StringComparer.Ordinal).ThenBy(z => z.Schueler, StringComparer.Create(new CultureInfo("de-DE"), true)).ToList();
    }

    /// <summary>Fehlzeiten einer Schülerin/eines Schülers im Zeitraum.</summary>
    public List<Fehlzeit> FehlzeitenVon(int schuelerId, DateOnly von, DateOnly bis) =>
        _db.Liste("SELECT * FROM fehlzeiten WHERE schueler_id = @s AND datum >= @v AND datum <= @b ORDER BY datum, stunde", LesenFehlzeit,
            Db.P("@s", schuelerId), Db.P("@v", von), Db.P("@b", bis));

    // ------------------------------------------------------------------------------------------ Abwesenheit erfassen (Abwesenheitsdialog)

    /// <summary>
    /// Zeitraum einer ganztägigen Abwesenheit: vom Beginn des ersten bis zum Ende des letzten Tages (00:00–23:59), sodass alle
    /// Unterrichtsstunden laut Plan – auch außerhalb des Zeitrasters – als ganze Fehlstunden zählen (Fehltag je Tag).
    /// </summary>
    public static (DateTime Von, DateTime Bis) Ganztaegig(DateTime von, DateTime bis)
    {
        var v = von.Date;
        var b = bis.Date < v ? v : bis.Date;
        return (v, b.AddDays(1).AddMinutes(-1));
    }

    private static string Status(string? st, string vorgabe) => st is "entschuldigt" or "unentschuldigt" or "offen" ? st : vorgabe;

    /// <summary>
    /// Abwesenheit als Zeitraum erfassen bzw. ändern (Abwesenheitsdialog): legt für alle Unterrichtsstunden der Klasse im Zeitraum die
    /// Fehlzeiten an – minutengenau, angebrochene Stunden als Verspätung bzw. vorzeitiges Gehen. Mit <paramref name="stunde"/> (Dialog in den
    /// Stundendetails) ersetzt sie die Abwesenheit der Schülerin/des Schülers in dieser Stunde samt der zugehörigen Abwesenheit; mit
    /// <paramref name="ersetzen"/> die angegebenen Fehlzeiten (Bearbeiten in der Abwesenheitsliste).
    /// Rechte: Klassenleitung, Schulleitung und Administration beliebig; die unterrichtende Lehrkraft nur am Tag der Stunde und ohne Status.
    /// </summary>
    public int AbwesenheitErfassen(Benutzer b, Stand s, int schuelerId, DateTime von, DateTime bis, string? grund, string? text, string? status,
        (DateOnly Datum, int Stunde, int Unterricht)? stunde = null, IEnumerable<int>? ersetzen = null,
        string? nachweis = null, string? entschuldigtAm = null, bool? elternInformiert = null, string? notizIntern = null, int? einzelstunde = null)
    {
        var sch = s.Schueler.FirstOrDefault(x => x.Id == schuelerId) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");
        if (bis <= von) throw new ArgumentException("Das Ende der Abwesenheit muss nach dem Beginn liegen.");
        if (bis.Date.Subtract(von.Date).TotalDays > 60) throw new ArgumentException("Bitte einen Zeitraum von höchstens 60 Tagen wählen.");
        if ((text ?? "").Length > 500) throw new ArgumentException("Der Text darf höchstens 500 Zeichen lang sein.");
        var kl = IstKlassenleitung(b, s, sch.Klasse) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle);
        RueckwirkendPruefen(b, DateOnly.FromDateTime(von), _uhr.Heute);
        var einst = Einstellungen();
        einst.EingabePruefen(b, DateOnly.FromDateTime(von), _uhr.Heute);
        einst.EingabePruefen(b, DateOnly.FromDateTime(bis.AddMinutes(-1)), _uhr.Heute);
        Karte? karte = null;
        var darfStunde = false;
        if (stunde is var (d, n, u))
        {
            karte = KarteZu(b, s, d, n, u, null, einzelstunde) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
            if (!karte.Klassen.Any(k => Ci.Equals(k.K, sch.Klasse))) throw new ArgumentException("Die Schülerin bzw. der Schüler gehört nicht zu dieser Stunde.");
            darfStunde = DarfSchreiben(b, s, d, n, u) && Rechtedienst.Darf(b, Recht.AbwesenheitErfassen);
            if (!kl && !darfStunde) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
            if (!kl && (DateOnly.FromDateTime(von) != d || DateOnly.FromDateTime(bis) != d))
                throw new UnauthorizedAccessException("Abwesenheiten über den Tag der Stunde hinaus trägt die Klassenleitung ein.");
            if (!kl && d > _uhr.Heute) throw new ArgumentException("Fehlzeiten können nicht im Voraus erfasst werden.");
            if (!kl) NurInStundePruefen(einst, b, s, d, karte);
        }
        else if (!kl) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann Abwesenheiten eintragen.");

        // bisherige Abwesenheit (wird ersetzt)
        var alt = AlteAbwesenheit(b, s, schuelerId, karte, stunde?.Datum, ersetzen, kl);
        var vorlage = alt.FirstOrDefault();
        var gruppe = alt.Select(f => f.Gruppe).FirstOrDefault(g => g.StartsWith(GruppenPraefix, StringComparison.Ordinal)) ?? GruppenPraefix + Sicherheit.Zufall(10);
        var gesperrt = einst.GrundTextGesperrt && !kl && !b.IstAdmin;
        var g = gesperrt ? vorlage?.Grund ?? einst.Standardgrund("fehlt") : (grund ?? vorlage?.Grund ?? "").Trim();
        var t = gesperrt || text is null ? (vorlage is null ? "" : Klartext(vorlage.Bemerkung)) : text.Trim();
        var st = kl ? Status(status, vorlage?.Status ?? StatusNeu(einst, g)) : vorlage is null ? StatusNeu(einst, g) : StatusNachGrund(einst, vorlage.Status, vorlage.Grund, g);
        var nw = kl && nachweis != null ? Klassenbuchverwaltung.Nachweis(nachweis) : vorlage?.Nachweis ?? "";
        var ea = kl && entschuldigtAm != null ? Klassenbuchverwaltung.DatumOderLeer(entschuldigtAm) : vorlage?.EntschuldigtAm ?? "";
        var ei = kl && elternInformiert != null ? elternInformiert.Value : vorlage?.ElternInformiert ?? false;
        var ni = kl && notizIntern != null ? notizIntern.Trim() : vorlage?.NotizIntern ?? "";
        var lehrer = b.LehrerKuerzel ?? karte?.Lehrer.FirstOrDefault()?.K ?? "";
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        var regeln = Regeln();
        var anzahl = 0;
        _db.Transaktion(c =>
        {
            foreach (var f in alt) Db.Ausfuehren(c, "DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", f.Id));
            for (var tag = DateOnly.FromDateTime(von); tag <= DateOnly.FromDateTime(bis); tag = tag.AddDays(1))
            {
                if (_plan.Frei(s, tag).Frei) continue;
                var ab = tag == DateOnly.FromDateTime(von) ? TimeOnly.FromDateTime(von) : TimeOnly.MinValue;
                var ende = tag == DateOnly.FromDateTime(bis) ? TimeOnly.FromDateTime(bis) : TimeOnly.MaxValue;
                foreach (var k in _plan.Karten(s, tag, Elementtyp.Klasse, sch.Klasse).Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung")))
                    foreach (var a in AusFenster(s, regeln, Enumerable.Range(k.Von, k.Bis - k.Von + 1), ab, ende))
                    {
                        Db.Ausfuehren(c, """
                            INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund, gruppe,
                                nachweis, entschuldigt_am, eltern_informiert, notiz_intern, beginn, ende)
                            VALUES (@d, @s, @u, @sid, @k, @f, @a, @m, @st, @bem, @l, @e, @b, @g, @gr, @nw, @ea, @ei, @ni, @be, @en)
                            ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET unterricht = excluded.unterricht, fach = excluded.fach, art = excluded.art, minuten = excluded.minuten,
                                status = excluded.status, bemerkung = excluded.bemerkung, lehrer = excluded.lehrer, grund = excluded.grund, gruppe = excluded.gruppe,
                                nachweis = excluded.nachweis, entschuldigt_am = excluded.entschuldigt_am, eltern_informiert = excluded.eltern_informiert,
                                notiz_intern = excluded.notiz_intern, beginn = excluded.beginn, ende = excluded.ende
                            """, Db.P("@d", tag), Db.P("@s", a.Stunde), Db.P("@u", k.Unterricht), Db.P("@sid", schuelerId), Db.P("@k", sch.Klasse), Db.P("@f", k.Fach),
                            Db.P("@a", a.Art), Db.P("@m", a.Minuten), Db.P("@st", st), Db.P("@bem", string.Join(" – ", new[] { a.Hinweis, t }.Where(x => x.Length > 0))),
                            Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id), Db.P("@g", g), Db.P("@gr", gruppe), Db.P("@nw", nw), Db.P("@ea", ea), Db.P("@ei", ei ? 1 : 0),
                            Db.P("@ni", ni), Db.P("@be", a.Beginn), Db.P("@en", a.Ende));
                        anzahl++;
                    }
            }
        });
        if (karte != null && darfStunde && stunde is var (sd, _, su)) GeprueftMarkieren(b, sd, karte, su, lehrer);
        return anzahl;
    }

    /// <summary>
    /// Bisherige Abwesenheit, die der Dialog ersetzt: Fehlzeiten der Stunde (bzw. die angegebenen) und alle Fehlzeiten derselben
    /// Abwesenheit (Gruppe, „Rest des Tages“). Lehrkräfte ohne Klassenleitung nur am Tag der Stunde.
    /// </summary>
    private List<Fehlzeit> AlteAbwesenheit(Benutzer b, Stand s, int schuelerId, Karte? karte, DateOnly? tag, IEnumerable<int>? ersetzen, bool kl)
    {
        var res = new Dictionary<int, Fehlzeit>();
        if (karte != null && tag is DateOnly d)
            foreach (var f in FehlzeitenVon(schuelerId, d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis)) res[f.Id] = f;
        foreach (var id in (ersetzen ?? Enumerable.Empty<int>()).Distinct())
        {
            var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", LesenFehlzeit, Db.P("@i", id));
            if (f is null) continue;
            if (f.SchuelerId != schuelerId) throw new ArgumentException("Die Fehlzeiten gehören zu einer anderen Schülerin bzw. einem anderen Schüler.");
            if (!IstKlassenleitung(b, s, f.Klasse)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann dies ändern.");
            res[f.Id] = f;
        }
        var gruppen = res.Values.Select(f => f.Gruppe).Where(g => g.Length > 0).ToHashSet();
        if (karte != null) gruppen.Add($"vorzeitig:{karte.Unterricht ?? 0}:{karte.Von}");
        if (karte != null) gruppen.Add(TagesendeGruppe(karte.Unterricht ?? 0, karte.Von));
        foreach (var g in gruppen)
            foreach (var f in _db.Liste("SELECT * FROM fehlzeiten WHERE schueler_id = @s AND gruppe = @g", LesenFehlzeit, Db.P("@s", schuelerId), Db.P("@g", g)))
            {
                // bis Tagesende fortgeschrieben: in einer Folgestunde verkürzen – frühere Stunden bleiben
                if (karte != null && g.StartsWith(TagesendePraefix, StringComparison.Ordinal) && f.Stunde < karte.Von) continue;
                if (kl || tag is DateOnly t2 && f.Datum == Plandienst.Iso(t2)) res[f.Id] = f;
            }
        return res.Values.OrderBy(f => f.Datum, StringComparer.Ordinal).ThenBy(f => f.Stunde).ToList();
    }

    /// <summary>
    /// Entschuldigungsstatus direkt aus den Stundendetails setzen (Segment-Schalter im Abwesenheitsdialog): ändert den Status der Fehlzeiten
    /// der Stunde samt der zugehörigen Abwesenheit (auch über mehrere Tage). Nur Klassenleitung, Schulleitung und Administration.
    /// </summary>
    public int AbwesenheitStatusSetzen(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, IEnumerable<int> schuelerIds, string status, int? einzelstunde = null)
    {
        if (status is not ("entschuldigt" or "unentschuldigt" or "offen")) throw new ArgumentException("Unbekannter Status.");
        var karte = KarteZu(b, s, d, stunde, unterricht, null, einzelstunde) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        var n = 0;
        foreach (var sid in schuelerIds.Distinct())
        {
            var sch = s.Schueler.FirstOrDefault(x => x.Id == sid && klassen.Contains(x.Klasse, Ci));
            if (sch is null) continue;
            if (!DarfEntschuldigen(b, s, sch.Klasse)) throw new UnauthorizedAccessException("Den Entschuldigungsstatus setzt die Klassenleitung.");
            RueckwirkendPruefen(b, d, _uhr.Heute);
            foreach (var f in AlteAbwesenheit(b, s, sid, karte, d, null, true))
                n += _db.Ausfuehren("UPDATE fehlzeiten SET status = @s WHERE id = @i", Db.P("@s", status), Db.P("@i", f.Id));
        }
        return n;
    }

    /// <summary>Abwesenheit (aus den Stundendetails) entfernen: Fehlzeiten der Stunde und der zugehörigen Abwesenheit.</summary>
    public int AbwesenheitEntfernen(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, IEnumerable<int> schuelerIds, int? einzelstunde = null)
    {
        var karte = KarteZu(b, s, d, stunde, unterricht, null, einzelstunde) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        var darfStunde = DarfSchreiben(b, s, d, stunde, unterricht) && Rechtedienst.Darf(b, Recht.AbwesenheitErfassen);
        RueckwirkendPruefen(b, d, _uhr.Heute);
        var einst = Einstellungen();
        einst.EingabePruefen(b, d, _uhr.Heute);
        var n = 0;
        foreach (var sid in schuelerIds.Distinct())
        {
            var sch = s.Schueler.FirstOrDefault(x => x.Id == sid && klassen.Contains(x.Klasse, Ci));
            if (sch is null) continue;
            var kl = IstKlassenleitung(b, s, sch.Klasse) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle);
            if (!kl && !darfStunde) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
            if (!kl) NurInStundePruefen(einst, b, s, d, karte);
            var weg = AlteAbwesenheit(b, s, sid, karte, d, null, kl);
            foreach (var f in weg) einst.LoeschenPruefen(b, f.Erstellt, _uhr.Jetzt);
            foreach (var f in weg) n += _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", f.Id));
        }
        if (darfStunde) GeprueftMarkieren(b, d, karte, unterricht, b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "");
        return n;
    }

    /// <summary>Stunde als „Abwesenheiten kontrolliert“ markieren.</summary>
    private void GeprueftMarkieren(Benutzer b, DateOnly d, Karte karte, int unterricht, string lehrer)
    {
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, geaendert, benutzer_id, anwesenheit)
            VALUES (@d, @s, @u, @k, @f, @l, @g, @b, @g)
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET anwesenheit = excluded.anwesenheit
            """, Db.P("@d", d), Db.P("@s", Schluessel(karte)), Db.P("@u", unterricht), Db.P("@k", JsonSerializer.Serialize(karte.Klassen.Select(k => k.K).ToList())), Db.P("@f", karte.Fach),
            Db.P("@l", lehrer), Db.P("@g", jetzt), Db.P("@b", b.Id));
    }
}
