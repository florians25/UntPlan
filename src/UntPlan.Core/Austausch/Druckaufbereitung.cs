using System.Text.RegularExpressions;

namespace UntPlan.Core.Austausch;

/// <summary>
/// Aufbereitung von Druckzeilen nach den Seitenlayout-Optionen wie im Vorbild („Form“): kombinierte Klassen (2a,2b → 2ab),
/// Blockvertretungen zusammenfassen (1, 2 → 1-2), Stundenbezeichnung.
/// </summary>
public static class Druckaufbereitung
{
    private static readonly Regex KlasseMuster = new(@"^(?<stufe>\d+)(?<zug>[A-Za-zÄÖÜäöü]+)$", RegexOptions.Compiled);

    /// <summary>„Komb. Klassen (2a,2b->2ab)“: Klassen derselben Stufe mit einbuchstabigem Zug zusammenfassen.</summary>
    public static string KlassenKombinieren(string klassen)
    {
        if (string.IsNullOrWhiteSpace(klassen)) return klassen;
        var teile = klassen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var erg = new List<string>();
        var stufen = new Dictionary<string, (int Index, List<string> Zuege)>();
        foreach (var t in teile)
        {
            var m = KlasseMuster.Match(t);
            if (!m.Success || m.Groups["zug"].Value.Length != 1) { erg.Add(t); continue; }
            var stufe = m.Groups["stufe"].Value;
            if (stufen.TryGetValue(stufe, out var s)) s.Zuege.Add(m.Groups["zug"].Value);
            else { stufen[stufe] = (erg.Count, new List<string> { m.Groups["zug"].Value }); erg.Add(""); }
        }
        foreach (var (stufe, (index, zuege)) in stufen) erg[index] = stufe + string.Concat(zuege);
        return string.Join(",", erg);
    }

    /// <summary>Stunden als Text, aufeinanderfolgende zusammengefasst („1-2“, „3, 5“).</summary>
    public static string StundenText(IEnumerable<int> stunden)
    {
        var liste = stunden.Distinct().OrderBy(x => x).ToList();
        var teile = new List<string>();
        for (var i = 0; i < liste.Count; i++)
        {
            var start = liste[i];
            while (i + 1 < liste.Count && liste[i + 1] == liste[i] + 1) i++;
            teile.Add(liste[i] == start ? start.ToString() : $"{start}-{liste[i]}");
        }
        return string.Join(", ", teile);
    }

    /// <summary>
    /// „Blockvertretungen zusammenfassen“: aufeinanderfolgende Zeilen, die sich nur in der Stunde unterscheiden, werden zu einer
    /// Zeile „1-2“ zusammengefasst.
    /// </summary>
    public static List<T> Zusammenfassen<T>(IReadOnlyList<T> zeilen, Func<T, int?> stunde, Func<T, string> schluessel, Func<T, string, T> mitStunde)
    {
        var erg = new List<T>();
        var i = 0;
        while (i < zeilen.Count)
        {
            var z = zeilen[i];
            var s = stunde(z);
            var j = i + 1;
            var ende = s;
            while (s.HasValue && j < zeilen.Count && stunde(zeilen[j]) == ende + 1 && schluessel(zeilen[j]) == schluessel(z)) { ende++; j++; }
            erg.Add(s.HasValue && ende > s ? mitStunde(z, $"{s}-{ende}") : z);
            i = j;
        }
        return erg;
    }
}
