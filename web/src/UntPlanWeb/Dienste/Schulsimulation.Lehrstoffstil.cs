using System.Text.RegularExpressions;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Schreibstil der Lehrkräfte beim Lehrstoff: Jede Lehrkraft schreibt gleichbleibend (Stichwort, knapp, normal oder ausführlich; mit oder ohne
/// Seitenangaben; mit oder ohne Abkürzungen wie „AB“, „Wdh.“, „HA-Kontrolle“; mit oder ohne Reihentitel). Ausführliche Einträge nennen
/// Einstieg, Material und Ergebnissicherung. Selten ersetzt ein Exkurs, ein Film oder die Vorbereitung einer Exkursion die Stunde der Reihe.
/// Die Unterrichtsform steht nie als eigener Zusatz im Text; Methodenwörter kommen nur vor, wo sie Inhalt der Stunde sind.
/// </summary>
public sealed partial class Schulsimulation
{
    /// <summary>Stil der Lehrkraft: Länge (0 Stichwort, 1 knapp, 2 normal, 3 ausführlich), Seitenangaben, Abkürzungen, Reihentitel.</summary>
    public sealed record Lehrstoffstil(int Laenge, bool Seiten, bool Abkuerzungen, bool MitReihe);

    public static readonly string[] Laengen = { "Stichwort", "knapp", "normal", "ausführlich" };

    /// <summary>Stil der Lehrkraft (für dieselbe Lehrkraft immer gleich).</summary>
    /// <param name="ausfuehrlichkeit">Intensität in Prozent: mehr ausführliche und weniger Stichwort-Einträge (100 = Standard).</param>
    public static Lehrstoffstil LehrstoffstilDer(long seed, string lehrer, int ausfuehrlichkeit = 100)
    {
        var z = Zufall.Fuer(seed, "lstil", lehrer);
        var a = Simulationsintensitaet.F(ausfuehrlichkeit);
        var laenge = z.Gewichtet(new[] { (0, 15.0 * Math.Max(0, 2 - a)), (1, 25.0), (2, 35.0), (3, 25.0 * a) }, x => x.Item2).Item1;
        return new(laenge, z.Chance(0.55), z.Chance(0.4), laenge >= 2 || z.Chance(0.5));
    }

    private static readonly Regex Lernfeld = new(@"^(LF [^:]+, LS [^:]+): (.*)$", RegexOptions.Compiled);
    private static readonly Regex Seitenangabe = new(@",? ?(Übungen )?(im Buch )?S\. \d+( Nr\. \d+( (und|bis) \d+)?)?", RegexOptions.Compiled);

    /// <summary>Feste Einträge (Prüfung, Rückgabe, Schuljahresbeginn, Klassenleitung, Kammer, Vertretung): bleiben unverändert.</summary>
    private static bool Fest(string text) =>
        Regex.IsMatch(text, @"^(Klassenarbeit|Klausur|Test|Abschlussprüfung|Rückgabe|Einstieg ins Schuljahr|Organisatorisches|Klasse in der|Vertretung|Stillarbeit|Arbeitsblatt der Fachlehrkraft)")
        || text.Contains("Wiederholung zur ", StringComparison.Ordinal);

    /// <summary>Lehrstoff der Reihe im Stil der Lehrkraft (gleiche Eingaben ergeben denselben Text).</summary>
    public string Stilisieren(long seed, string lehrer, string fach, string? fachName, int u, DateOnly d, int von, string text, int ausfuehrlichkeit = 100)
    {
        if (Fest(text)) return text;
        var st = LehrstoffstilDer(seed, lehrer, ausfuehrlichkeit);
        var z = Zufall.Fuer(seed, "stilisieren", lehrer, u, d, von);
        var gruppe = Zuordnung(fach, fachName);
        if (gruppe?.Zyklisch == true) return st.Abkuerzungen ? Abkuerzen(text) : text;
        // Lernfeld und Lernsituation bleiben immer vorn
        var lf = Lernfeld.Match(text);
        var praefix = lf.Success ? lf.Groups[1].Value : "";
        var rest = lf.Success ? lf.Groups[2].Value : text;
        var titel = "";
        if (!lf.Success && rest.IndexOf(": ", StringComparison.Ordinal) is var i and > 0) { titel = rest[..i]; rest = rest[(i + 2)..]; }
        string Mit(string inhalt) => praefix.Length > 0 ? $"{praefix}: {inhalt}" : titel.Length > 0 && st.MitReihe ? $"{titel}: {inhalt}" : Gross(inhalt);

        // selten: Exkurs, Film oder Vorbereitung einer Exkursion
        if (gruppe is not null && Katalog.Extras.TryGetValue(gruppe.Id, out var extras) && z.Chance(0.03))
        {
            var arten = extras.Where(x => x.Value.Count > 0).ToList();
            if (arten.Count > 0)
            {
                var (art, liste) = z.Gewichtet(arten, x => x.Key switch { "exkurse" => 5.0, "filme" => 3.5, _ => 1.5 });
                var extra = z.Wahl(liste);
                if (art == "filme" && !extra.StartsWith("Film", StringComparison.Ordinal)) extra = $"Film: {extra}";
                return Abschluss(praefix.Length > 0 ? $"{praefix}: {extra}" : extra);
            }
        }
        if (!st.Seiten) rest = Seitenangabe.Replace(rest, m => m.Groups[1].Success ? ", Übungen" : "").Trim().TrimEnd(',');
        string ergebnis;
        switch (st.Laenge)
        {
            case 0:
            {
                // ein Stichwort: Inhalt ohne Zusätze
                var kern = Regex.Replace(rest, @"(^|: )(Fortsetzung|Vertiefung|Anwendungsaufgaben zu|Lernkontrolle) ", "$1").Split(',')[0].Trim();
                // Reihentitel mit Doppelpunkt („Family and friends: Role play: At home“): nur der Inhalt der Stunde
                var stuecke = kern.Split(": ");
                if (stuecke.Length > 1) kern = stuecke[^1].Length >= 8 ? stuecke[^1] : string.Join(": ", stuecke[^2..]);
                if (kern.Length < 4 || kern is "Übungen" or "Anwendungsaufgaben" or "Besprechung der Übungen") kern = titel.Length > 0 ? titel : kern;
                ergebnis = praefix.Length > 0 ? $"{praefix}: {kern}" : Gross(kern);
                break;
            }
            case 1:
                ergebnis = Mit(Regex.Replace(rest, @", (Vertiefung|Übungen)$", ""));
                break;
            case 2:
                ergebnis = Mit(rest);
                if (st.Seiten && !Regex.IsMatch(rest, @"S\. \d") && z.Chance(0.2)) ergebnis += z.Wahl(new[] { ", Buch S. {s}", " (S. {s} f.)", ", S. {s} Nr. {a}" });
                break;
            case 3 when gruppe?.Id is "sport" or "koerper":
                ergebnis = Mit(z.Chance(0.4) ? $"{rest}; {z.Wahl(new[] { "Aufwärmen mit Laufspiel", "Abschlussspiel", "Stationen aufgebaut und abgebaut", "Reflexion im Sitzkreis" })}" : rest);
                break;
            default:
            {
                // ausführlich: Einstieg, Material, Ergebnissicherung bzw. ein zweites Teilthema
                var teile = new List<string>();
                if (z.Chance(0.35)) teile.Add(z.Wahl(new[] { "Hausaufgabenkontrolle", "Wiederholung der letzten Stunde", "Einstieg mit Bildimpuls", "Einstieg über eine Alltagssituation", "Vokabeln bzw. Begriffe vom letzten Mal" }));
                teile.Add(rest);
                var material = st.Seiten
                    ? new[] { "Buch S. {s} Nr. {a} bis {b}", "Arbeitsblatt Nr. {a}", "Arbeitsheft S. {s}", "Buch S. {s} und Arbeitsblatt", "Folie und Buch S. {s}" }
                    : new[] { "Arbeitsblatt", "Material aus Teams", "Tafelbild", "Folie", "Arbeitsheft" };
                if (z.Chance(0.55)) teile.Add("Material: " + z.Wahl(material));
                if (z.Chance(0.45)) teile.Add(z.Wahl(new[] { "Ergebnissicherung im Heft", "Sicherung an der Tafel", "Merksatz ins Heft", "Ergebnisse im Plenum verglichen", "Tafelbild übernommen", "kurze Zusammenfassung zum Schluss" }));
                if (z.Chance(0.15)) teile.Add(z.Wahl(new[] { "anschließend Übungsphase", "zweiter Teil: Anwendungsaufgaben", "Ausblick auf die nächste Stunde" }));
                ergebnis = Mit(string.Join("; ", teile.Distinct()));
                break;
            }
        }
        return Abschluss(ergebnis);

        string Abschluss(string t)
        {
            t = z.Zahlen(t.Replace("{n}", "{a}"));
            return st.Abkuerzungen ? Abkuerzen(t) : t;
        }
    }

    /// <summary>Übliche Abkürzungen im Klassenbuch.</summary>
    private static string Abkuerzen(string t) => t
        .Replace("Hausaufgabenkontrolle", "HA-Kontrolle").Replace("Hausaufgaben", "HA").Replace("Arbeitsblatt", "AB").Replace("Arbeitsblätter", "ABs")
        .Replace("Wiederholung", "Wdh.").Replace("Besprechung", "Bespr.").Replace("Ergebnissicherung", "Sicherung");
}
