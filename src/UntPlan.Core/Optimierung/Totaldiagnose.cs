using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

/// <summary>Eine schlecht bewertete Stunde eines Elements (Totaldiagnose): Punkte und Grund (leer = Stunde ist gut bewertet).</summary>
public sealed record TotaldiagnoseStunde(int Tag, int Stunde, long Punkte, string Grund);

/// <summary>Zeile der Totaldiagnose: Element, Gesamtpunkte und die drei am schlechtesten bewerteten Stunden.</summary>
public sealed record TotaldiagnoseZeile(ElementTyp Typ, int ElementId, string Name, long Punkte, IReadOnlyList<TotaldiagnoseStunde> Stunden);

/// <summary>
/// Totaldiagnose (Handbuch 4.4.7): Welche Klassen bzw. Lehrkräfte haben – gemessen an den Vorgaben – die schlechtesten Stundenpläne?
/// Je Element werden die Schlechtpunkte aller Verstöße summiert (Verstöße des Elements selbst und seiner Unterrichte); die Liste ist
/// nach Punkten absteigend sortiert, je Element werden die drei am schlechtesten bewerteten Stunden mit Grund angegeben.
/// </summary>
public static class Totaldiagnose
{
    public static List<TotaldiagnoseZeile> Berechnen(Schuldatei d, ElementTyp typ)
    {
        var ix = new DatenIndex(d);
        var verstoesse = Bewertung.Verstoesse(d);
        // Verstöße einzelner Unterrichte (Doppelstunden, Fach 2x/Tag, kein Raum …) zählen für alle beteiligten Elemente
        bool Betrifft(Verstoss v, int id)
        {
            if (v.ElementTyp == typ) return v.ElementId == id;
            if (v.ElementTyp is ElementTyp.Klasse or ElementTyp.Lehrer) return false;
            if (v.UnterrichtId is not int uid || !ix.Unterrichte.TryGetValue(uid, out var u)) return false;
            return typ == ElementTyp.Klasse ? u.KlassenIds.Contains(id) : u.LehrerIds.Contains(id);
        }
        var elemente = typ == ElementTyp.Lehrer ? d.Lehrer.Where(l => !l.Ignorieren).Cast<Element>() : d.Klassen.Where(k => !k.Ignorieren);
        var liste = new List<TotaldiagnoseZeile>();
        foreach (var e in elemente)
        {
            var eigene = verstoesse.Where(v => Betrifft(v, e.Id)).ToList();
            var punkte = (long)Math.Round(eigene.Sum(v => v.Kosten));
            var stunden = eigene.Where(v => v.Tag >= 0 && v.Stunde > 0).GroupBy(v => (v.Tag, v.Stunde))
                .Select(g => new TotaldiagnoseStunde(g.Key.Tag, g.Key.Stunde, (long)Math.Round(g.Sum(v => v.Kosten)),
                    Grund(g.OrderByDescending(v => v.Kosten).First())))
                .OrderByDescending(s => s.Punkte).ThenBy(s => s.Tag).ThenBy(s => s.Stunde).Take(3).ToList();
            // weniger als drei bewertete Stunden: mit belegten (gut bewerteten) Stunden auffüllen – ohne Grund
            if (stunden.Count < 3)
                foreach (var p in ix.PlanVon(typ, e.Id).OrderByDescending(p => p.Stunde).ThenBy(p => p.Tag))
                {
                    if (stunden.Count >= 3) break;
                    if (stunden.Any(s => s.Tag == p.Tag && s.Stunde == p.Stunde)) continue;
                    stunden.Add(new TotaldiagnoseStunde(p.Tag, p.Stunde, 0, ""));
                }
            liste.Add(new TotaldiagnoseZeile(typ, e.Id, e.Kurzname, punkte, stunden));
        }
        return liste.OrderByDescending(z => z.Punkte).ThenBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Grund einer schlecht bewerteten Stunde (z. B. „Raum nicht frei“, „Hohlstunde“).</summary>
    public static string Grund(Verstoss v) => v.Art switch
    {
        VerstossArt.KeinRaum => "Raum nicht frei",
        VerstossArt.HohlstundeKlasse => "Hohlstunde",
        VerstossArt.ZeitwunschKlasse or VerstossArt.ZeitwunschLehrer or VerstossArt.ZeitwunschFach or VerstossArt.ZeitwunschRaum or VerstossArt.ZeitwunschUnterricht => "Zeitwunsch",
        _ => Bewertung.ArtText(v.Art),
    };
}

/// <summary>Konfliktkette (Complex Conflict Chain): Unterrichte, die paarweise nicht gleichzeitig verplant werden können.</summary>
/// <param name="Wochenstunden">Summe der Wochenstunden der Kette (Maß für die Schwierigkeit).</param>
/// <param name="UnterrichtIds">Beteiligte Unterrichte (nach Wochenstunden absteigend).</param>
/// <param name="Kritisch">Kritische Lehrkraft je Unterricht: ein Tausch dieser Lehrkraft würde die Kette am meisten verkürzen.</param>
/// <param name="Verkuerzung">Verkürzung der Kette durch den Tausch der kritischen Lehrkraft (Stunden).</param>
public sealed record Konfliktkette(int Wochenstunden, IReadOnlyList<int> UnterrichtIds, IReadOnlyDictionary<int, int> Kritisch, int Verkuerzung);

/// <summary>Ergebnis der CCC-Analyse.</summary>
public sealed record CccErgebnis(int Unterrichte, int Kopplungen, double Kombinationen, int VerfuegbareStunden, IReadOnlyList<Konfliktkette> Ketten);

/// <summary>Ein Lehrerteam: Lehrkräfte, die gemeinsam (gekoppelt) in Unterrichten eingesetzt sind.</summary>
public sealed record Lehrerteam(int Nr, IReadOnlyList<int> LehrerIds, IReadOnlyList<int> UnterrichtIds, int Wochenstunden);

/// <summary>
/// CCC-Analyse (Handbuch 4.4.8): findet die längsten Konfliktketten – Gruppen von Unterrichten, die wegen gemeinsamer Klassen oder
/// Lehrkräfte nicht gleichzeitig verplant werden können. Ist die Summe der Wochenstunden einer Kette größer als die im Zeitraster
/// verfügbaren Stunden, ist die restlose Verplanung unmöglich. Dazu die Liste der Lehrerteams (Kopplungen mit mehreren Lehrkräften).
/// </summary>
public static class CccAnalyse
{
    /// <summary>Können zwei Unterrichte nicht gleichzeitig liegen (gemeinsame Lehrkraft oder Klasse, nicht im selben Band, KKK beachtet)?</summary>
    public static bool Konflikt(Schuldatei d, Unterricht a, Unterricht b, out bool wegenKlasse, out HashSet<int> gemeinsameLehrer)
    {
        gemeinsameLehrer = a.LehrerIds.Intersect(b.LehrerIds).ToHashSet();
        wegenKlasse = a.KlassenIds.Intersect(b.KlassenIds).Any() && !Klassenkollision.Erlaubt(a, b);
        if (a.Id == b.Id) return false;
        if (!string.IsNullOrWhiteSpace(a.Band) && a.Band.Trim() == b.Band.Trim()) return false;
        if (!Kalender.GleicheWoche(d, a, b)) return false;
        return wegenKlasse || gemeinsameLehrer.Count > 0;
    }

    public static CccErgebnis Analysieren(Schuldatei d, int maxKetten = 30, IProgress<(int UnterrichtId, int Erledigt, int Gesamt)>? fortschritt = null, CancellationToken ct = default)
    {
        var unts = d.Unterrichte.Where(u => !u.Ignorieren && u.Wochenstunden > 0 && u.Zeilen.Count > 0 && (u.LehrerIds.Any() || u.KlassenIds.Any())).OrderBy(u => u.Id).ToList();
        var n = unts.Count;
        var kopplungen = unts.Count(u => u.Zeilen.Count > 1);
        var zr = d.Zeitraster;
        var verfuegbar = Enumerable.Range(0, zr.TageProWoche).Sum(t => Enumerable.Range(1, zr.StundenProTag).Count(s => zr.Verfuegbar(t, s)));
        var nachbarn = new HashSet<int>[n];
        for (var i = 0; i < n; i++) nachbarn[i] = new HashSet<int>();
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Konflikt(d, unts[i], unts[j], out _, out _)) { nachbarn[i].Add(j); nachbarn[j].Add(i); }
        var w = unts.Select(u => u.Wochenstunden).ToArray();

        // je Unterricht die schwerste Kette, die ihn enthält (Branch & Bound mit Schrittbegrenzung)
        var gefunden = new Dictionary<string, List<int>>();
        var reihenfolge = Enumerable.Range(0, n).OrderByDescending(i => w[i] + nachbarn[i].Sum(j => w[j])).ToList();
        var erledigt = 0;
        foreach (var start in reihenfolge)
        {
            ct.ThrowIfCancellationRequested();
            fortschritt?.Report((unts[start].Id, ++erledigt, n));
            var kette = SchwersteClique(start, nachbarn, w, 20000);
            if (kette.Count < 2) continue;
            kette.Sort();
            gefunden.TryAdd(string.Join(",", kette), kette);
        }
        var ketten = gefunden.Values.Select(k => (k, Summe: k.Sum(i => w[i]))).OrderByDescending(x => x.Summe).ThenByDescending(x => x.k.Count).Take(maxKetten)
            .Select(x => Kette(d, unts, x.k, w)).ToList();
        // Kombinationen: Produkt der Kopplungsgrößen über die Kopplungen (Anzeige wie im Handbuch, grobe Größenordnung)
        var kombinationen = unts.Where(u => u.Zeilen.Count > 1).Aggregate(1.0, (p, u) => p * Math.Max(2, u.Zeilen.Count + u.KlassenIds.Count())) * Math.Max(1, n);
        return new CccErgebnis(n, kopplungen, kombinationen, verfuegbar, ketten);
    }

    /// <summary>Schwerste Clique (Kette), die den Unterricht <paramref name="start"/> enthält.</summary>
    private static List<int> SchwersteClique(int start, HashSet<int>[] nachbarn, int[] w, int maxSchritte)
    {
        var best = new List<int> { start };
        var bestW = w[start];
        var schritte = 0;
        var aktuell = new List<int> { start };
        void Suche(List<int> kandidaten, int gewicht)
        {
            if (++schritte > maxSchritte) return;
            if (gewicht > bestW) { bestW = gewicht; best = new List<int>(aktuell); }
            if (kandidaten.Count == 0) return;
            if (gewicht + kandidaten.Sum(i => w[i]) <= bestW) return;
            for (var k = 0; k < kandidaten.Count; k++)
            {
                var v = kandidaten[k];
                var rest = kandidaten.Skip(k + 1).Where(nachbarn[v].Contains).ToList();
                aktuell.Add(v);
                Suche(rest, gewicht + w[v]);
                aktuell.RemoveAt(aktuell.Count - 1);
                if (schritte > maxSchritte) return;
                if (gewicht + kandidaten.Skip(k + 1).Sum(i => w[i]) <= bestW) return;
            }
        }
        Suche(nachbarn[start].OrderByDescending(i => w[i]).ToList(), w[start]);
        return best;
    }

    private static Konfliktkette Kette(Schuldatei d, List<Unterricht> unts, List<int> k, int[] w)
    {
        var summe = k.Sum(i => w[i]);
        // je Unterricht und Lehrkraft: wie viele Stunden würde die Kette kürzer, wenn diese Lehrkraft ersetzt würde?
        var kandidaten = new List<(int UnterrichtId, int LehrerId, int Kuerzer)>();
        foreach (var i in k)
        {
            var u = unts[i];
            foreach (var l in u.LehrerIds)
            {
                // Unterrichte der Kette, mit denen u nur wegen dieser Lehrkraft kollidiert
                var frei = k.Where(j => j != i).Where(j =>
                {
                    Konflikt(d, u, unts[j], out var klasse, out var gemeinsam);
                    return !klasse && gemeinsam.Count == 1 && gemeinsam.Contains(l);
                }).ToList();
                if (frei.Count == 0) continue;
                var kuerzer = Math.Min(w[i], frei.Sum(j => w[j]));
                kandidaten.Add((u.Id, l, kuerzer));
            }
        }
        var max = kandidaten.Count == 0 ? 0 : kandidaten.Max(x => x.Kuerzer);
        var kritisch = kandidaten.Where(x => max > 0 && x.Kuerzer == max).GroupBy(x => x.UnterrichtId).ToDictionary(g => g.Key, g => g.First().LehrerId);
        var ids = k.OrderByDescending(i => w[i]).ThenBy(i => unts[i].Id).Select(i => unts[i].Id).ToList();
        return new Konfliktkette(summe, ids, kritisch, max);
    }

    /// <summary>Lehrerteams: verschiedene Gruppen von Lehrkräften, die gemeinsam in (gekoppelten) Unterrichten eingesetzt sind.</summary>
    public static List<Lehrerteam> Lehrerteams(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var gruppen = d.Unterrichte.Where(u => !u.Ignorieren && u.LehrerIds.Count() > 1)
            .GroupBy(u => string.Join(",", u.LehrerIds.OrderBy(l => ix.KLehrer(l), StringComparer.CurrentCultureIgnoreCase)))
            .Select(g => (Lehrer: g.First().LehrerIds.OrderBy(l => ix.KLehrer(l), StringComparer.CurrentCultureIgnoreCase).ToList(), Unts: g.OrderBy(u => u.Id).ToList()))
            .OrderBy(g => ix.KLehrer(g.Lehrer[0]), StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Lehrer.Count).ToList();
        return gruppen.Select((g, i) => new Lehrerteam(i + 1, g.Lehrer, g.Unts.Select(u => u.Id).ToList(), g.Unts.Sum(u => u.Wochenstunden))).ToList();
    }

    /// <summary>Je Lehrkraft die Nummern der Lehrerteams, in die sie eingebunden ist (absteigend nach Anzahl).</summary>
    public static List<(int LehrerId, List<int> Teams)> TeamsJeLehrer(IEnumerable<Lehrerteam> teams, DatenIndex ix) =>
        teams.SelectMany(t => t.LehrerIds.Select(l => (l, t.Nr))).GroupBy(x => x.l)
            .Select(g => (g.Key, g.Select(x => x.Nr).Distinct().OrderBy(x => x).ToList()))
            .OrderByDescending(x => x.Item2.Count).ThenBy(x => ix.KLehrer(x.Key), StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Gemeinsame Zeitwünsche eines Teams je Stunde: der kleinste Wunsch der Lehrkräfte (ein -3 eines Mitglieds sperrt das Team).
    /// </summary>
    public static int TeamZeitwunsch(DatenIndex ix, Lehrerteam t, int tag, int stunde) =>
        t.LehrerIds.Select(l => ix.Lehrer.TryGetValue(l, out var le) ? le.Zeitwuensche.Get(tag, stunde) : 0).DefaultIfEmpty(0).Min();
}
