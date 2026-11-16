using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core;

/// <summary>
/// Daten der Listen „Freie Stunden“ und „Fach-Stunden-Liste“ (Listenauswahl): reine Lese-Auswertungen von Stundenplan und Unterricht.
/// </summary>
public static class Berichtslisten
{
    /// <summary>Eine Stunde eines Unterrichtstags mit den laut Stundenplan freien Lehrkräften und Räumen.</summary>
    public sealed record FreieStunde(DateOnly Datum, int Tag, int Stunde, IReadOnlyList<Lehrer> Lehrer, IReadOnlyList<Raum> Raeume);

    /// <summary>
    /// Freie Lehrkräfte und freie Räume je Unterrichtstag × Stunde der Woche von <paramref name="datum"/> (regulärer Stundenplan der an
    /// diesem Tag gültigen Periode, ohne Vertretungen). Ignorierte bzw. im Zeitraum nicht tätige Lehrkräfte und ignorierte Räume fehlen.
    /// Stunden bis zur letzten Stunde, in der in der Woche Unterricht liegt (ohne Unterricht: Stunden pro Tag laut Zeitraster).
    /// </summary>
    public static List<FreieStunde> FreieStunden(Schuldatei d, DateOnly datum)
    {
        var montag = Kalender.Montag(datum);
        var tage = Enumerable.Range(0, 7).Select(i => montag.AddDays(i)).Where(t => Kalender.IstUnterrichtstag(d, t)).ToList();
        var plaene = tage.ToDictionary(t => t, t => Tagesplan.Regulaer(d, t));
        var letzte = plaene.Values.SelectMany(p => p).Select(r => r.Stunde).DefaultIfEmpty(d.Zeitraster.StundenProTag).Max();
        var raeume = Stammdaten.Sortiert(d.Raeume).Where(r => !r.Ignorieren).ToList();
        var res = new List<FreieStunde>();
        foreach (var t in tage)
        {
            var lehrerIds = VeranstaltungsAuswahl.Alle(d, ElementTyp.Lehrer, t, t).ToHashSet();
            var lehrer = Stammdaten.Sortiert(d.Lehrer).Where(l => lehrerIds.Contains(l.Id)).ToList();
            var plan = plaene[t];
            for (var s = 1; s <= letzte; s++)
            {
                var stunde = s;
                var belegtL = plan.Where(r => r.Stunde == stunde && r.LehrerId.HasValue).Select(r => r.LehrerId!.Value).ToHashSet();
                var belegtR = plan.Where(r => r.Stunde == stunde && r.RaumId.HasValue).Select(r => r.RaumId!.Value).ToHashSet();
                res.Add(new FreieStunde(t, Kalender.TagIndex(d, t), s, lehrer.Where(l => !belegtL.Contains(l.Id)).ToList(), raeume.Where(r => !belegtR.Contains(r.Id)).ToList()));
            }
        }
        return res;
    }

    /// <summary>Eine Zeile der Fach-Stunden-Liste: Wochenstunden einer Lehrkraft in Klasse(n).</summary>
    public sealed record FachStundenZeile(string Klassen, string Lehrer, int Stunden);

    /// <summary>Block eines Fachs: Zeilen je Klasse(n) und Lehrkraft, Summen je Klasse und je Lehrkraft sowie die Summe der Lehrerstunden.</summary>
    public sealed record FachStundenBlock(Fach Fach, IReadOnlyList<FachStundenZeile> Zeilen, IReadOnlyList<(string Klasse, int Stunden)> JeKlasse,
        IReadOnlyList<(string Lehrer, int Stunden)> JeLehrer, int Summe);

    /// <summary>
    /// Fach-Stunden-Liste des Unterrichts der aktiven Periode: je Fach (Stammdatenreihenfolge) die Wochenstunden je Klasse(n) und Lehrkraft.
    /// Jede Kopplungszeile zählt mit den Wochenstunden ihres Unterrichts; je Klasse zählt ein Unterricht nur einmal. Ignorierter
    /// Unterricht fehlt, Fächer ohne Unterricht ebenso.
    /// </summary>
    public static List<FachStundenBlock> FachStunden(Schuldatei d)
    {
        var klassen = Stammdaten.Sortiert(d.Klassen).Select((k, i) => (k, i)).ToDictionary(x => x.k.Id, x => (x.k.Kurzname, Rang: x.i));
        var lehrer = d.Lehrer.ToDictionary(l => l.Id, l => l.Kurzname);
        string KlassenText(IEnumerable<int> ids) => string.Join(",", ids.Where(klassen.ContainsKey).OrderBy(id => klassen[id].Rang).Select(id => klassen[id].Kurzname));
        int Rang(IEnumerable<int> ids) => ids.Where(klassen.ContainsKey).Select(id => klassen[id].Rang).DefaultIfEmpty(int.MaxValue).Min();
        var unterrichte = d.Unterrichte.Where(u => !u.Ignorieren && u.Wochenstunden > 0).ToList();
        var res = new List<FachStundenBlock>();
        foreach (var f in Stammdaten.Sortiert(d.Faecher))
        {
            var zeilen = unterrichte.SelectMany(u => u.Zeilen.Where(z => z.FachId == f.Id).Select(z => (u, z))).ToList();
            if (zeilen.Count == 0) continue;
            var liste = zeilen
                .GroupBy(x => (Klassen: KlassenText(x.z.KlassenIds), Lehrer: x.z.LehrerId is int l && lehrer.TryGetValue(l, out var k) ? k : ""))
                .Select(g => (Rang: Rang(g.First().z.KlassenIds), Zeile: new FachStundenZeile(g.Key.Klassen, g.Key.Lehrer, g.Sum(x => x.u.Wochenstunden))))
                .OrderBy(x => x.Rang).ThenBy(x => x.Zeile.Klassen, StringComparer.Create(Format.De, true)).ThenBy(x => x.Zeile.Lehrer, StringComparer.Create(Format.De, true))
                .Select(x => x.Zeile).ToList();
            var jeKlasse = zeilen.SelectMany(x => x.z.KlassenIds.Where(klassen.ContainsKey).Select(k => (x.u, k))).Distinct()
                .GroupBy(x => x.k).OrderBy(g => klassen[g.Key].Rang).Select(g => (klassen[g.Key].Kurzname, g.Sum(x => x.u.Wochenstunden))).ToList();
            var jeLehrer = liste.Where(z => z.Lehrer.Length > 0).GroupBy(z => z.Lehrer).OrderBy(g => g.Key, StringComparer.Create(Format.De, true))
                .Select(g => (g.Key, g.Sum(z => z.Stunden))).ToList();
            res.Add(new FachStundenBlock(f, liste, jeKlasse, jeLehrer, liste.Sum(z => z.Stunden)));
        }
        return res;
    }
}
