using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>Kandidat im Dialog „Lehrer ändern“ / „Lehrer hinzufügen“: Lehrkraft und ob sie zur Zeit der Stunde(n) bereits verplant ist.</summary>
public sealed record StundenLehrerKandidat(Lehrer Lehrer, bool Belegt, bool Lehrbefaehigt);

/// <summary>
/// „Lehrer ändern“ und „Lehrer hinzufügen“ im Stundenplan (Handbuch, Manuelles Planen | Lehrer ändern und hinzufügen). Wird die
/// Lehrkraft nur in einer Stunde eines mehrstündigen Unterrichts ersetzt bzw. ergänzt, entsteht ein neuer einstündiger Unterricht
/// und die Stundenzahl des bestehenden Unterrichts sinkt um eins. „Für alle Stunden des Unterrichtes“ ändert den Unterricht selbst.
/// </summary>
public static class StundenLehrer
{
    /// <summary>Alle Lehrkräfte mit Kennzeichnung „zur Zeit der Stunde(n) bereits verplant“; optional nur lehrbefähigte.</summary>
    public static List<StundenLehrerKandidat> Kandidaten(Schuldatei d, Planstunde p, bool fuerAlleStunden, bool nurLehrbefaehigt)
    {
        var ix = new DatenIndex(d);
        if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return new();
        var zeiten = (fuerAlleStunden ? d.Stundenplan.Where(q => q.UnterrichtId == u.Id) : new[] { p }).Select(q => (q.Tag, q.Stunde)).ToHashSet();
        var belegt = new HashSet<int>();
        foreach (var q in d.Stundenplan)
        {
            if (q.UnterrichtId == u.Id || !zeiten.Contains((q.Tag, q.Stunde)) || !ix.Unterrichte.TryGetValue(q.UnterrichtId, out var o)) continue;
            foreach (var l in o.LehrerIds) belegt.Add(l);
        }
        var faecher = u.FachIds.ToList();
        return d.Lehrer.Where(l => !l.Ignorieren && !u.LehrerIds.Contains(l.Id))
            .Select(l => new StundenLehrerKandidat(l, belegt.Contains(l.Id), faecher.Count == 0 || faecher.All(f => l.FaecherIds.Contains(f))))
            .Where(k => !nurLehrbefaehigt || k.Lehrbefaehigt)
            .OrderBy(k => k.Belegt).ThenBy(k => k.Lehrer.Kurzname, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Lehrkraft der Kopplungszeile <paramref name="zeile"/> ersetzen. Ergebnis: Nummer des neu angelegten Unterrichts bzw. die
    /// Nummer des geänderten Unterrichts (für alle Stunden oder bei einstündigem Unterricht).
    /// </summary>
    public static int Aendern(Schuldatei d, Planstunde p, int zeile, int neuerLehrerId, bool fuerAlleStunden)
    {
        var u = d.Unterrichte.First(x => x.Id == p.UnterrichtId);
        var ziel = fuerAlleStunden || u.Wochenstunden <= 1 ? u : Abspalten(d, u, p);
        if (zeile >= 0 && zeile < ziel.Zeilen.Count) ziel.Zeilen[zeile].LehrerId = neuerLehrerId;
        return ziel.Id;
    }

    /// <summary>
    /// Lehrkraft hinzufügen: neue Kopplungszeile mit Klassen und Fach der ersten Zeile, gekoppelt mit der vorhandenen Lehrkraft.
    /// Ergebnis wie bei <see cref="Aendern"/>.
    /// </summary>
    public static int Hinzufuegen(Schuldatei d, Planstunde p, int lehrerId, bool fuerAlleStunden)
    {
        var u = d.Unterrichte.First(x => x.Id == p.UnterrichtId);
        var ziel = fuerAlleStunden || u.Wochenstunden <= 1 ? u : Abspalten(d, u, p);
        var vorlage = ziel.Zeilen.FirstOrDefault();
        var neu = new Kopplungszeile { LehrerId = lehrerId, FachId = vorlage?.FachId };
        if (vorlage != null) neu.KlassenIds.AddRange(vorlage.KlassenIds);
        ziel.Zeilen.Add(neu);
        foreach (var q in d.Stundenplan.Where(q => q.UnterrichtId == ziel.Id))
            while (q.Raeume.Count < ziel.Zeilen.Count) q.Raeume.Add(null);
        return ziel.Id;
    }

    /// <summary>Eine Stunde als neuen einstündigen Unterricht abspalten (die Stundenzahl des bestehenden sinkt um eins).</summary>
    private static Unterricht Abspalten(Schuldatei d, Unterricht u, Planstunde p)
    {
        var neu = u.Kopie(d.NeueId());
        neu.Wochenstunden = 1;
        neu.Doppelstunden = default;
        neu.Block = "";
        u.Wochenstunden--;
        if (u.Doppelstunden.Max * 2 > u.Wochenstunden) u.Doppelstunden = new Bereich(Math.Min(u.Doppelstunden.Min, u.Wochenstunden / 2), u.Wochenstunden / 2);
        d.Unterrichte.Insert(d.Unterrichte.IndexOf(u) + 1, neu);
        p.UnterrichtId = neu.Id;
        return neu;
    }
}
