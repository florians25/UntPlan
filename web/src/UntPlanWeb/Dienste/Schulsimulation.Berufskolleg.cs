using System.Text.RegularExpressions;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Berufskolleg (NRW, APO-BK): Bildungsgänge aus Klassenname und Fächern, Ausbildungsjahr, Kammerprüfungen, Teilzeit und Vollzeit.
/// Bildungsgänge: bs (Berufsschule, Fachklassen des dualen Systems, Teilzeit), av (Ausbildungsvorbereitung), bfs (Berufsfachschule),
/// fos11, fos12, fos13 (Fachoberschule), bg (Berufliches Gymnasium), fs (Fachschule, z. B. Sozialpädagogik inkl. praxisintegriert),
/// bk (Berufskolleg ohne erkennbaren Bildungsgang); an allgemeinbildenden Schulen sek1u, sek1o und sek2.
/// </summary>
public sealed partial class Schulsimulation
{
    private static readonly Regex Zweistellig = new(@"(?<!\d)(\d{2})(?!\d)", RegexOptions.CultureInvariant);
    private static readonly string[] FachschulFaecher = { "SPP", "SPT", "EW", "ESO", "ErSo", "SOP", "TPH", "DM" };
    private static readonly HashSet<string> BeruflicheGruppen = new(StringComparer.Ordinal)
        { "anwendungsentwicklung", "datenbanken", "itsysteme", "elektrotechnik", "bautechnik", "holz", "textil", "ernaehrung", "pflege", "sozialpaedagogik", "fotografie", "gestaltung" };

    /// <summary>Bildungsgang der Klasse (siehe Klassenbeschreibung); Klassenname vor Fächern, sonst „bk“.</summary>
    public static string Bildungsgang(Stand s, string klasse)
    {
        var st = Stufe(s, klasse);
        if (st is "sek1u" or "sek1o") return st;
        var n = klasse.ToUpperInvariant();
        if (st == "sek2" && (n.StartsWith("EF") || (n.Length >= 2 && n[0] == 'Q' && char.IsDigit(n[1])))) return "sek2";
        bool Beginnt(params string[] p) => p.Any(x => n.StartsWith(x, StringComparison.Ordinal));
        if (Beginnt("AV", "IFK", "BVJ", "VKL")) return "av";
        if (Beginnt("FOS", "FO1", "FHR", "FO "))
            return n.Contains("13") ? "fos13" : n.Contains("11") ? "fos11" : "fos12";
        if (Beginnt("BG", "AHR", "BGY", "GY")) return "bg";
        if (Beginnt("FSP", "FS", "ESP", "PIA", "HEP", "ERZ", "SOP")) return "fs";
        if (Beginnt("BFS", "HBF", "BF", "HH", "ZBF")) return "bfs";
        if (st == "sek2") return "bg";
        // Fachklassen des dualen Systems: Kürzel des Berufs mit Einschulungsjahrgang (z. B. ITA25, ELG24, KFZ23B)
        if (Regex.IsMatch(n, @"^[A-ZÄÖÜ]{2,5}\s?\d{2}[A-Z]?$") && Ausbildungsjahr(s, klasse) is not null) return "bs";
        var faecher = s.Unterricht.Values.SelectMany(u => u.Zeilen).Where(z => z.Klassen.Contains(klasse, Ci)).Select(z => z.Fach).Distinct(Ci).ToList();
        if (faecher.Any(f => FachschulFaecher.Contains(f, Ci))) return "fs";
        if (faecher.Any(f => Zuordnung(f, s.Faecher.TryGetValue(f, out var x) ? x.Name : null) is { } g && BeruflicheGruppen.Contains(g.Id))) return "bs";
        return "bk";
    }

    /// <summary>Teilzeit (Berufsschule, Fachoberschule 11 mit Praktikum): weniger Fehlzeiten als in Vollzeit-Bildungsgängen.</summary>
    public static bool Teilzeit(string bildungsgang) => bildungsgang is "bs" or "fos11";

    /// <summary>Faktor für Fehlzeiten (Vollzeit-Bildungsgänge mehr, Teilzeit weniger, Ausbildungsvorbereitung am meisten).</summary>
    public static double Fehlfaktor(string bildungsgang) => Fehlquoten.Bildungsgang(bildungsgang);

    /// <summary>Überwiegend volljährig (Entschuldigung durch die Schülerin bzw. den Schüler selbst): Berufsschule, Fachschule, Fachoberschule 12 und 13.</summary>
    public static bool Volljaehrig(string bildungsgang, int schuelerId) => bildungsgang switch
    {
        "bs" or "fs" or "fos12" or "fos13" => schuelerId % 10 != 0,
        "bg" or "bfs" or "fos11" => schuelerId % 3 == 0,
        _ => false,
    };

    /// <summary>Ausbildungs- bzw. Schuljahr aus dem Klassennamen (z. B. „ITA24“ im Schuljahr 2026/27 = 3. Jahr); null = nicht erkennbar.</summary>
    public static int? Ausbildungsjahr(Stand s, string klasse)
    {
        var m = Zweistellig.Matches(klasse).Select(x => int.Parse(x.Value)).FirstOrDefault(x => x is >= 15 and <= 40);
        if (m == 0) return null;
        var beginn = Halbjahre(s)[0].Von;
        var jahr = (beginn.Month >= 8 ? beginn.Year : beginn.Year - 1) % 100 - m + 1;
        return jahr is >= 1 and <= 4 ? jahr : null;
    }

    /// <summary>Kammerprüfung einer Berufsschulklasse: schriftliche Abschlussprüfung Teil 1 (2. Jahr, März) bzw. Teil 2 (3. Jahr, Ende April bis Mai).</summary>
    public sealed record Kammerpruefung(DateOnly Tag, string Name);

    private readonly Dictionary<(string, string, long), Kammerpruefung?> _kammer = new();

    public Kammerpruefung? KammerpruefungDer(Stand s, long seed, string klasse)
    {
        lock (_kammer)
        {
            if (_kammer.TryGetValue((s.Empfangen, klasse, seed), out var k)) return k;
            k = null;
            if (Bildungsgang(s, klasse) == "bs" && Ausbildungsjahr(s, klasse) is int j and >= 2)
            {
                var z = Zufall.Fuer(seed, "kammer", klasse);
                var jahr = Halbjahre(s)[^1].Von.Year;
                var (von, bis, name) = j == 2 ? (new DateOnly(jahr, 3, 1), new DateOnly(jahr, 3, 25), "Abschlussprüfung Teil 1") : (new DateOnly(jahr, 4, 22), new DateOnly(jahr, 5, 20), "Abschlussprüfung Teil 2");
                // an einem Tag, an dem die Klasse laut Plan Unterricht hätte (Dienstag bis Donnerstag)
                var tage = s.Unterricht.Values.Where(u => u.Zeilen.Any(x => x.Klassen.Contains(klasse, Ci))).SelectMany(u => Bloecke(s, u.Id)).Select(b => b.Tag)
                    .Where(d => d >= von && d <= bis && d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday).Distinct().Order().ToList();
                if (tage.Count > 0) k = new(z.Wahl(tage), name);
            }
            return _kammer[(s.Empfangen, klasse, seed)] = k;
        }
    }
}

/// <summary>
/// Parameter der Schülerabwesenheiten an einer Stelle. Ziel: allgemeinbildend etwa 5 bis 8 % der Schüler*innen je Tag (im Winter mehr), Vollzeit-Bildungsgänge
/// des Berufskollegs und Ausbildungsvorbereitung etwa 10 bis 15 %, Teilzeit-Berufsschule etwas weniger. Mehrtägige Krankheiten (2 bis 5 Tage) überwiegen;
/// Montag, Freitag und die Tage vor Ferien leicht höher; gelegentlich eine Grippewelle in einer Klasse.
/// </summary>
public static class Fehlquoten
{
    /// <summary>Wahrscheinlichkeit, dass eine gesunde Schülerin bzw. ein gesunder Schüler an einem Schultag erkrankt (allgemeinbildend, ohne Saisonfaktor).</summary>
    public const double KrankBeginn = 0.019;
    /// <summary>Dauer einer Krankheit in Schultagen mit Gewicht.</summary>
    public static readonly (int Tage, double Gewicht)[] Dauer = { (1, 24), (2, 25), (3, 22), (4, 13), (5, 10), (7, 4), (10, 2) };
    public const double Arzttermin = 0.003, Verspaetung = 0.004, BetrieblicheGruende = 0.0025;
    /// <summary>Faktor während einer Grippewelle in der Klasse; Anteil der Klassen mit einer Welle je Winter; Länge in Schultagen.</summary>
    public const double Grippewelle = 2.6, GrippewelleAnteil = 0.3;
    public const int GrippewelleTage = 7;

    /// <summary>Faktor des Bildungsgangs (Vollzeit mehr, Teilzeit weniger, Ausbildungsvorbereitung am meisten).</summary>
    public static double Bildungsgang(string bildungsgang) => bildungsgang switch
    {
        "av" => 2.1, "bfs" => 1.9, "fos12" or "fos13" or "bg" => 1.45, "fs" => 1.35, "fos11" => 0.95, "bs" => 0.85, _ => 1.0,
    };

    /// <summary>Saison: Erkältungswellen November bis Februar, weniger im Spätsommer.</summary>
    public static double Saison(DateOnly d) => d.Month switch { 11 or 12 or 1 or 2 => 1.4, 3 => 1.15, 6 or 7 or 8 or 9 => 0.8, _ => 1.0 };

    /// <summary>Montag und Freitag leicht höher, der letzte Schultag vor Ferien ebenfalls.</summary>
    public static double Tag(DateOnly d, bool vorFerien) => (d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday ? 1.15 : 1.0) * (vorFerien ? 1.25 : 1.0);
}
