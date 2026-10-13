using UntPlan.Core.Model;

namespace UntPlan.Core.Module;

/// <summary>
/// Modul Kursplanung (z. B. gymnasiale Oberstufe NRW, APO-GOSt): Schüler, Kurswahlen, Kollisionsmatrix,
/// automatische und manuelle Bandbildung (Kursschienen).
/// </summary>
public static class Kursplanung
{
    public static List<Unterricht> Kurse(Schuldatei d, int? klasseId = null) =>
        d.Unterrichte.Where(u => !string.IsNullOrWhiteSpace(u.Kursname) && (klasseId is null || u.KlassenIds.Contains(klasseId.Value))).OrderBy(u => u.Kursname).ToList();

    public static int Teilnehmer(Schuldatei d, int kursId) => d.Schueler.Count(s => s.KurswahlIds.Contains(kursId));

    /// <summary>Kollisionsmatrix: Anzahl Schüler, die beide Kurse gewählt haben.</summary>
    public static Dictionary<(int, int), int> Kollisionsmatrix(Schuldatei d, IReadOnlyList<Unterricht> kurse)
    {
        var ids = kurse.Select(k => k.Id).ToHashSet();
        var m = new Dictionary<(int, int), int>();
        foreach (var s in d.Schueler)
        {
            var w = s.KurswahlIds.Where(ids.Contains).ToList();
            for (var i = 0; i < w.Count; i++)
                for (var j = i + 1; j < w.Count; j++)
                {
                    var key = w[i] < w[j] ? (w[i], w[j]) : (w[j], w[i]);
                    m[key] = m.GetValueOrDefault(key) + 1;
                }
        }
        return m;
    }

    public static int Kollisionen(Schuldatei d, IReadOnlyList<Unterricht> kurse)
    {
        var m = Kollisionsmatrix(d, kurse);
        var band = kurse.ToDictionary(k => k.Id, k => k.Band);
        return m.Where(kv => !string.IsNullOrEmpty(band[kv.Key.Item1]) && band[kv.Key.Item1] == band[kv.Key.Item2]).Sum(kv => kv.Value);
    }

    /// <summary>
    /// Automatische Bandbildung: ordnet die Kurse möglichst wenigen Bändern zu, sodass kein Schüler zwei Kurse im selben Band hat
    /// (Graphfärbung nach DSatur, anschließend Verbesserung). Liefert Kurs → Bandnummer.
    /// </summary>
    public static Dictionary<int, int> Bandbildung(Schuldatei d, IReadOnlyList<Unterricht> kurse, int maxBaender, int seed = 1)
    {
        var m = Kollisionsmatrix(d, kurse);
        int K(int a, int b) => a == b ? 0 : m.GetValueOrDefault(a < b ? (a, b) : (b, a));
        var zuordnung = new Dictionary<int, int>();
        var offen = kurse.Select(k => k.Id).ToList();
        var lehrer = kurse.ToDictionary(k => k.Id, k => k.LehrerIds.ToHashSet());
        while (offen.Count > 0)
        {
            // Kurs mit den meisten unterschiedlich belegten Nachbarbändern zuerst
            var next = offen.OrderByDescending(k => zuordnung.Where(z => K(k, z.Key) > 0 || lehrer[k].Overlaps(lehrer[z.Key])).Select(z => z.Value).Distinct().Count())
                .ThenByDescending(k => offen.Sum(o => K(k, o))).First();
            var best = -1;
            var bestKosten = int.MaxValue;
            for (var b = 1; b <= Math.Max(1, maxBaender); b++)
            {
                var kosten = zuordnung.Where(z => z.Value == b).Sum(z => K(next, z.Key) + (lehrer[next].Overlaps(lehrer[z.Key]) ? 1000 : 0));
                if (kosten < bestKosten) { bestKosten = kosten; best = b; }
                if (kosten == 0) break;
            }
            zuordnung[next] = best;
            offen.Remove(next);
        }
        // Verbesserung: einzelne Kurse umhängen
        var rnd = new Random(seed);
        for (var it = 0; it < 2000; it++)
        {
            var k = kurse[rnd.Next(kurse.Count)].Id;
            var alt = zuordnung[k];
            int Kost(int band) => zuordnung.Where(z => z.Key != k && z.Value == band).Sum(z => K(k, z.Key) + (lehrer[k].Overlaps(lehrer[z.Key]) ? 1000 : 0));
            var neu = rnd.Next(1, Math.Max(1, maxBaender) + 1);
            if (Kost(neu) < Kost(alt)) zuordnung[k] = neu;
        }
        return zuordnung;
    }

    public static void BaenderUebernehmen(Schuldatei d, string praefix, Dictionary<int, int> zuordnung)
    {
        foreach (var u in d.Unterrichte)
            if (zuordnung.TryGetValue(u.Id, out var b)) u.Band = $"{praefix}-{b}";
    }
}

/// <summary>Abteilungsplanung: Abteilungsdatei exportieren und wieder zusammenführen.</summary>
public static class Abteilungen
{
    public static bool GehoertZu(Schuldatei d, Unterricht u, int abteilungId)
    {
        var ix = new DatenIndex(d);
        return u.KlassenIds.Any(k => ix.Klassen.TryGetValue(k, out var kl) && kl.AbteilungId == abteilungId);
    }

    /// <summary>Erzeugt eine Abteilungsdatei: alle Stammdaten, aber nur Unterrichte/Stunden der Abteilung (andere Stunden fixiert).</summary>
    public static Schuldatei Exportieren(Schuldatei gesamt, int abteilungId)
    {
        var kopie = Dokument.Klonen(gesamt);
        foreach (var p in kopie.Stundenplan)
        {
            var u = kopie.Unterrichte.FirstOrDefault(x => x.Id == p.UnterrichtId);
            if (u != null && !GehoertZu(kopie, u, abteilungId)) p.Fixiert = true;
        }
        foreach (var u in kopie.Unterrichte.Where(u => !GehoertZu(kopie, u, abteilungId))) u.Fix = true;
        return kopie;
    }

    /// <summary>Übernimmt Unterrichte und Stunden der Abteilung aus der Abteilungsdatei in die Gesamtdatei.</summary>
    public static int Zusammenfuehren(Schuldatei gesamt, Schuldatei teil, int abteilungId)
    {
        var ids = teil.Unterrichte.Where(u => GehoertZu(teil, u, abteilungId)).Select(u => u.Id).ToHashSet();
        gesamt.Unterrichte.RemoveAll(u => ids.Contains(u.Id));
        gesamt.Unterrichte.AddRange(teil.Unterrichte.Where(u => ids.Contains(u.Id)));
        gesamt.Stundenplan.RemoveAll(p => ids.Contains(p.UnterrichtId));
        gesamt.Stundenplan.AddRange(teil.Stundenplan.Where(p => ids.Contains(p.UnterrichtId)).Select(p => p.Kopie()));
        gesamt.NaechsteId = Math.Max(gesamt.NaechsteId, teil.NaechsteId);
        return ids.Count;
    }
}
