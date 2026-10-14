using System.Text.Json;
using UntPlan.Core.Model;

namespace UntPlan.Core.Module;

/// <summary>
/// Periodenverwaltung (Mehrwochen-/Periodenstundenplan): Perioden anlegen (Tochter einer Mutterperiode, übernimmt Unterricht,
/// Stundenplan und Pausenaufsichten), ändern, löschen, fixieren, Schultage zählen und Unterrichtsänderungen in Folgeperioden
/// weitergeben, sofern der Unterricht dort noch unverändert ist.
/// </summary>
public static class Perioden
{
    /// <summary>Ende einer Periode: Bis bzw. Tag vor dem Beginn der nächsten Periode derselben Ebene bzw. Schuljahresende.</summary>
    public static DateOnly Ende(Schuldatei d, Periode p)
    {
        if (p.Bis is DateOnly b) return b;
        var naechste = d.Perioden.Where(x => x.Id != p.Id && x.Von > p.Von && x.MutterId == p.MutterId).OrderBy(x => x.Von).FirstOrDefault();
        return naechste is null ? d.Einstellungen.SchuljahrBis : naechste.Von.AddDays(-1);
    }

    /// <summary>Unterrichtstage, an denen diese Periode tatsächlich gilt (innerste Periode).</summary>
    public static int Schultage(Schuldatei d, Periode p) =>
        Kalender.Tage(p.Von, Ende(d, p)).Count(t => Kalender.IstUnterrichtstag(d, t) && d.PeriodeAm(t).Id == p.Id);

    /// <summary>Neue Periode als Tochter der Mutterperiode (Kopie von Unterricht, Stundenplan, Pausenaufsichten).</summary>
    public static Periode Neu(Schuldatei d, Periode mutter, string name, DateOnly von, DateOnly? bis)
    {
        Pruefen(d, von, bis);
        if (von < mutter.Von) throw new InvalidOperationException($"Die neue Periode muss innerhalb der Mutterperiode {mutter.Name} (ab {Format.Datum(mutter.Von)}) beginnen.");
        if (d.Perioden.Any(x => x.Von == von && x.MutterId == mutter.Id))
            throw new InvalidOperationException($"Zur Mutterperiode {mutter.Name} gibt es bereits eine Periode ab {Format.Datum(von)}.");
        // Mutter-Unterricht sicherstellen (ältere Dateien)
        mutter.Unterrichte ??= new();
        var p = mutter.Kopie(d.NeueId(), string.IsNullOrWhiteSpace(name) ? $"Periode{d.Perioden.Count + 1}" : name.Trim(), von, bis);
        d.Perioden.Add(p);
        return p;
    }

    private static void Pruefen(Schuldatei d, DateOnly von, DateOnly? bis)
    {
        if (von < d.Einstellungen.SchuljahrVon || von > d.Einstellungen.SchuljahrBis)
            throw new InvalidOperationException($"Der Beginn muss im Schuljahr ({Format.Datum(d.Einstellungen.SchuljahrVon)} – {Format.Datum(d.Einstellungen.SchuljahrBis)}) liegen.");
        if (bis is DateOnly b && b < von) throw new InvalidOperationException("Das Ende liegt vor dem Beginn.");
    }

    public static void Aendern(Schuldatei d, Periode p, string name, string langname, DateOnly von, DateOnly? bis, bool fixiert)
    {
        Pruefen(d, von, bis);
        if (p.MutterId is int m && d.Perioden.FirstOrDefault(x => x.Id == m) is { } mutter && von < mutter.Von)
            throw new InvalidOperationException($"Die Periode muss innerhalb der Mutterperiode {mutter.Name} beginnen.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Bitte einen Namen angeben.");
        p.Name = name.Trim();
        p.Langname = langname.Trim();
        p.Von = von;
        p.Bis = bis;
        p.Fixiert = fixiert;
    }

    /// <summary>Periode löschen; Töchter hängen danach an der Mutter der gelöschten Periode. Die letzte Periode bleibt erhalten.</summary>
    public static void Loeschen(Schuldatei d, Periode p)
    {
        if (d.Perioden.Count <= 1) throw new InvalidOperationException("Die einzige Periode kann nicht gelöscht werden.");
        if (p.Fixiert) throw new InvalidOperationException($"Die Periode {p.Name} ist fixiert.");
        foreach (var t in d.Perioden.Where(x => x.MutterId == p.Id)) t.MutterId = p.MutterId;
        d.Perioden.Remove(p);
        if (d.AktivePeriodeId == p.Id) d.AktivePeriodeId = d.Perioden.OrderBy(x => x.Von).First().Id;
    }

    /// <summary>Alle (direkten und indirekten) Töchter einer Periode.</summary>
    public static List<Periode> Toechter(Schuldatei d, Periode p)
    {
        var res = new List<Periode>();
        var offen = new Queue<int>(new[] { p.Id });
        while (offen.Count > 0)
        {
            var id = offen.Dequeue();
            foreach (var t in d.Perioden.Where(x => x.MutterId == id && !res.Contains(x))) { res.Add(t); offen.Enqueue(t.Id); }
        }
        return res;
    }

    private static string Json(Unterricht u) => JsonSerializer.Serialize(u, Dokument.JsonOptionen);

    private static Unterricht Klon(Unterricht u) => JsonSerializer.Deserialize<Unterricht>(Json(u), Dokument.JsonOptionen)!;

    /// <summary>Momentaufnahme des Unterrichts der aktiven Periode (vor einer Änderung).</summary>
    public static Dictionary<int, string> Momentaufnahme(Schuldatei d) => d.Unterrichte.ToDictionary(u => u.Id, Json);

    /// <summary>
    /// Unterrichtsänderungen der aktiven Periode in die Tochterperioden übernehmen: geändert wird dort nur Unterricht,
    /// der noch genau dem alten Stand entspricht (in der Tochter bewusst abweichender Unterricht bleibt erhalten).
    /// Neuer Unterricht wird ergänzt, gelöschter entfernt (sofern unverändert). Fixierte Perioden bleiben unberührt.
    /// </summary>
    public static int Weitergeben(Schuldatei d, Dictionary<int, string> vorher)
    {
        var quelle = d.AktivePeriode;
        var nachher = d.Unterrichte.ToDictionary(u => u.Id, u => (U: u, J: Json(u)));
        var geaendert = 0;
        foreach (var t in Toechter(d, quelle).Where(t => !t.Fixiert))
        {
            t.Unterrichte ??= new();
            foreach (var (id, neu) in nachher)
            {
                vorher.TryGetValue(id, out var alt);
                if (alt == neu.J) continue;
                var idx = t.Unterrichte.FindIndex(x => x.Id == id);
                if (idx < 0)
                {
                    if (alt is null) { t.Unterrichte.Add(Klon(neu.U)); geaendert++; }
                    continue;
                }
                if (Json(t.Unterrichte[idx]) != alt) continue; // in der Tochter abweichend
                t.Unterrichte[idx] = Klon(neu.U);
                geaendert++;
            }
            foreach (var (id, alt) in vorher.Where(x => !nachher.ContainsKey(x.Key)))
            {
                var idx = t.Unterrichte.FindIndex(x => x.Id == id);
                if (idx < 0 || Json(t.Unterrichte[idx]) != alt) continue;
                t.Unterrichte.RemoveAt(idx);
                t.Plan.RemoveAll(p => p.UnterrichtId == id);
                geaendert++;
            }
        }
        return geaendert;
    }
}
