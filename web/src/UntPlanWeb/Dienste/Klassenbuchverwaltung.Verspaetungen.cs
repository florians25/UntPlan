using System.Globalization;
using System.Text.RegularExpressions;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Verspätung bzw. vorzeitiges Gehen einer Fehlzeit für die Berichte: Art, Minuten und Uhrzeit von–bis (abwesend von Stundenbeginn bis zur
/// Ankunft bzw. ab dem Gehen bis Stundenende). <see cref="AlsFehlstunde"/> = ab der Schwelle der Verspätungsregeln als Fehlstunde gewertet.
/// </summary>
public sealed record Spaetangabe(string Art, int Minuten, string Von, string Bis, bool AlsFehlstunde)
{
    public string ArtText => Art == "vorzeitig" ? "vorzeitig gegangen" : "verspätet";
    public string Zeit => $"{Von}–{Bis}";
    /// <summary>„verspätet 12 Min.“</summary>
    public string Kurz => $"{ArtText} {Minuten} Min.";
    /// <summary>„verspätet 08:00–08:12 (12 Min.)“, ggf. „, als Fehlstunde gewertet“.</summary>
    public string Lang => $"{ArtText} {Zeit} ({Minuten} Min.){(AlsFehlstunde ? ", als Fehlstunde gewertet" : "")}";
}

public sealed partial class Klassenbuchverwaltung
{
    // ab der Schwelle als Fehlstunde gewertet: „verspätet 12 min“ bzw. „vorzeitig gegangen 10 min“ (ganz verpasste Stunden „(60 min)“ sind Fehlstunden)
    private static readonly Regex SpaetHinweis = new(@"^(?<art>verspätet|vorzeitig gegangen) (?<min>\d+) min", RegexOptions.CultureInvariant);

    /// <summary>Verspätung bzw. vorzeitiges Gehen einer Fehlzeit; null = ganze Fehlstunde.</summary>
    public static Spaetangabe? Spaet(Stand s, Fehlzeit f)
    {
        string art;
        int min;
        var alsFehlstunde = false;
        if (f.Art is "verspaetet" or "vorzeitig") { art = f.Art; min = f.Minuten; }
        else if (f.Art == "fehlt" && SpaetHinweis.Match(f.Bemerkung) is { Success: true } m)
        {
            art = m.Groups["art"].Value == "verspätet" ? "verspaetet" : "vorzeitig";
            min = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
            alsFehlstunde = true;
        }
        else return null;
        var (ab, bi) = Klassenbuchdienst.Fenster(s, f);
        var (sb, se) = Klassenbuchdienst.StundenZeit(s, f.Stunde);
        // Fehlstunde ab der Schwelle: Fenster steht ggf. nur als Hinweis (Minuten) im Text
        if (alsFehlstunde && ab == sb && bi == se && min > 0 && min < (se - sb).TotalMinutes)
            (ab, bi) = art == "verspaetet" ? (sb, sb.AddMinutes(min)) : (se.AddMinutes(-min), se);
        if (min <= 0) min = (int)Math.Round((bi - ab).TotalMinutes);
        return new Spaetangabe(art, min, Hm(ab), Hm(bi), alsFehlstunde);
    }

    /// <summary>Entschuldigungsstatus ausgeschrieben („offen“, „entschuldigt“, „nicht entschuldigt“ bzw. der Name wie „u-1Mahn“).</summary>
    private static string StatusText(string status, string name) => name.Length > 0 ? name : status switch
    {
        "entschuldigt" => "entschuldigt",
        "unentschuldigt" => "nicht entschuldigt",
        _ => "offen",
    };

    /// <summary>Summe der Verspätungen bzw. des vorzeitigen Gehens: „2 Verspätungen, 20 Min.“.</summary>
    private static string SpaetSumme(IEnumerable<Spaetangabe> liste, string art)
    {
        var l = liste.Where(a => a.Art == art).ToList();
        if (l.Count == 0) return "";
        var wort = art == "vorzeitig" ? (l.Count == 1 ? "1× vorzeitig gegangen" : $"{l.Count}× vorzeitig gegangen") : (l.Count == 1 ? "1 Verspätung" : $"{l.Count} Verspätungen");
        return $"{wort}, {l.Sum(a => a.Minuten)} Min.";
    }
}
