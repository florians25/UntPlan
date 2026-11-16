using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core;

/// <summary>
/// Berechnete (nicht editierbare) Angaben der Stammdaten- und Unterrichtsformate: Klassenstundensumme mit Teilungs-Nummern,
/// Klassenlehrerschaften und Hohlstunden einer Lehrkraft im aktuellen Stundenplan, Zeitwünsche eines Unterrichts als Text.
/// </summary>
public static class Stammdatenkennzahlen
{
    /// <summary>
    /// Wochenstunden der Klasse: Unterrichte ohne Teilungs-Nummer zählen voll; Unterrichte mit derselben Teilungs-Nummer zählen
    /// zusammen nur mit der größten Stundenzahl; Teilungs-Nummer 0 trägt nichts bei. Ignorierte Unterrichte zählen mit (Wertsumme).
    /// </summary>
    public static int KlassenWochenstunden(DatenIndex ix, Klasse k) => KlassenWochenstunden(ix.UnterrichteVon(ElementTyp.Klasse, k.Id));

    public static int KlassenWochenstunden(IEnumerable<Unterricht> unterrichte)
    {
        var summe = 0;
        var teilungen = new Dictionary<int, int>();
        foreach (var u in unterrichte)
        {
            if (u.Teilungsnummer is not int t) { summe += u.Wochenstunden; continue; }
            if (t == 0) continue;
            teilungen[t] = Math.Max(teilungen.GetValueOrDefault(t), u.Wochenstunden);
        }
        return summe + teilungen.Values.Sum();
    }

    /// <summary>Klassen, in denen die Lehrkraft als Klassenlehrer eingetragen ist, z. B. „2: 1a, 3b“ (leer = keine).</summary>
    public static string Klassenlehrerschaften(Schuldatei d, Lehrer l)
    {
        var klassen = Stammdaten.Sortiert(d, ElementTyp.Klasse).OfType<Klasse>().Where(k => k.KlassenlehrerIds.Contains(l.Id)).Select(k => k.Kurzname).ToList();
        return klassen.Count == 0 ? "" : $"{klassen.Count}: {string.Join(", ", klassen)}";
    }

    /// <summary>
    /// Hohlstunden der Lehrkraft im aktuellen Stundenplan (Woche): freie, im Zeitraster vorhandene Stunden zwischen der ersten und der
    /// letzten Stunde eines Tages. An Tagen mit Vor- und Nachmittagsunterricht gilt die längste Lücke im Mittagspausen-Fenster des
    /// Zeitrasters (höchstens „Mittagspause max.“ der Lehrkraft) als Mittagspause und nicht als Hohlstunde.
    /// </summary>
    public static int HohlstundenAktuell(Schuldatei d, DatenIndex ix, Lehrer l)
    {
        var zr = d.Zeitraster;
        var hohl = 0;
        foreach (var tag in ix.PlanVon(ElementTyp.Lehrer, l.Id).GroupBy(p => p.Tag))
        {
            var belegt = tag.Select(p => p.Stunde).ToHashSet();
            var erste = belegt.Min();
            var letzte = belegt.Max();
            var luecken = Enumerable.Range(erste + 1, Math.Max(0, letzte - erste - 1)).Where(s => !belegt.Contains(s) && zr.Verfuegbar(tag.Key, s)).ToList();
            if (luecken.Count == 0) continue;
            var vorm = belegt.Any(s => zr.Art(tag.Key, s) == Tageszeit.Vormittag);
            var nachm = belegt.Any(s => zr.Art(tag.Key, s) == Tageszeit.Nachmittag);
            var mittag = 0;
            if (vorm && nachm && zr.MittagspauseVon > 0 && zr.MittagspauseBis >= zr.MittagspauseVon)
            {
                var lauf = 0;
                for (var s = zr.MittagspauseVon; s <= zr.MittagspauseBis; s++)
                {
                    if (luecken.Contains(s)) mittag = Math.Max(mittag, ++lauf);
                    else lauf = 0;
                }
                if (!l.Mittagspause.IstLeer) mittag = Math.Min(mittag, l.Mittagspause.Max);
            }
            hohl += luecken.Count - mittag;
        }
        return hohl;
    }

    /// <summary>Zeitwünsche (Stundenwünsche) eines Unterrichts als Text, z. B. „Mo-1:-3 Di-2:+1“ (leer = keine).</summary>
    public static string ZeitwuenscheText(Schuldatei d, Zeitwuensche zw) =>
        string.Join(" ", zw.Werte.Where(w => w.Value != 0).OrderBy(w => w.Key)
            .Select(w => $"{Planungsdienst.Tagname(d, w.Key / 100)}-{w.Key % 100}:{(w.Value > 0 ? "+" : "")}{w.Value}"));
}

/// <summary>
/// Unterrichtszeilen für den Druck der Unterrichtsansichten (Druckauswahl): Optionen „Unterricht des Elements“ und
/// „Unterricht zusammenfassen“.
/// </summary>
public static class Unterrichtsdruck
{
    /// <summary>Ein gedruckter Unterricht: der (erste) Unterricht, alle zusammengefassten Nummern, Stundensumme und gedruckte Kopplungszeilen.</summary>
    public sealed record Eintrag(Unterricht U, List<int> Nummern, int Wochenstunden, List<int> Zeilen);

    /// <summary>Kopplungszeile betrifft das Element (Lehrer bzw. Klasse, Fach, Raum).</summary>
    public static bool Betrifft(Kopplungszeile z, ElementTyp typ, int id) => typ switch
    {
        ElementTyp.Lehrer => z.LehrerId == id,
        ElementTyp.Klasse => z.KlassenIds.Contains(id),
        ElementTyp.Fach => z.FachId == id,
        ElementTyp.Raum => z.FachraumId == id || z.StammraumId == id,
        _ => true,
    };

    /// <param name="nurElement">„Unterricht des Elements“: bei Kopplungen nur die für das Element relevanten Zeilen drucken.</param>
    /// <param name="zusammenfassen">
    /// „Unterricht zusammenfassen“: aufgesplittete Unterrichte (für das Element gleiche Lehrer, Fächer und Klassen) als einen Unterricht
    /// mit der Summe der Wochenstunden ausgeben.
    /// </param>
    public static List<Eintrag> Eintraege(IEnumerable<Unterricht> unterrichte, ElementTyp typ, int id, bool nurElement, bool zusammenfassen)
    {
        var liste = new List<Eintrag>();
        var gruppen = new Dictionary<string, int>();
        foreach (var u in unterrichte.OrderBy(u => u.Id))
        {
            var relevant = Enumerable.Range(0, u.Zeilen.Count).Where(i => Betrifft(u.Zeilen[i], typ, id)).ToList();
            if (relevant.Count == 0) relevant = Enumerable.Range(0, u.Zeilen.Count).ToList();
            var zeilen = nurElement ? relevant : Enumerable.Range(0, u.Zeilen.Count).ToList();
            if (zusammenfassen)
            {
                var schluessel = string.Join("|", relevant.Select(i => u.Zeilen[i])
                    .Select(z => $"{z.LehrerId}/{z.FachId}/{string.Join(",", z.KlassenIds.OrderBy(k => k))}").OrderBy(s => s, StringComparer.Ordinal));
                if (gruppen.TryGetValue(schluessel, out var pos))
                {
                    var e = liste[pos];
                    liste[pos] = e with { Nummern = e.Nummern.Append(u.Id).ToList(), Wochenstunden = e.Wochenstunden + u.Wochenstunden };
                    continue;
                }
                gruppen[schluessel] = liste.Count;
            }
            liste.Add(new Eintrag(u, new List<int> { u.Id }, u.Wochenstunden, zeilen));
        }
        return liste;
    }
}
