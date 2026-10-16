namespace UntPlan.Core.Planung;

/// <summary>
/// Feld eines gezeichneten Stundenplans: zusammenhängende Stunden (Doppelstunde, Block) eines Unterrichts an einem Tag.
/// Gleichzeitige Felder liegen nebeneinander in Spuren.
/// </summary>
public sealed class PlanBlock<T>
{
    public required T Erster { get; init; }
    public List<T> Alle { get; } = new();
    /// <summary>Erste Stunde (1-basiert).</summary>
    public int Start { get; set; }
    public int Laenge { get; set; } = 1;
    /// <summary>Letzte Stunde des Feldes.</summary>
    public int Ende => Start + Laenge - 1;
    public int Spur { get; set; }
    public int Spuren { get; set; } = 1;
    public int StapelIndex { get; set; }
    public int StapelAnzahl { get; set; } = 1;
}

/// <summary>Zeichenteil eines Feldes: Stunden von <see cref="Start"/> bis <see cref="Ende"/> ohne dazwischenliegende Pausenzeile.</summary>
public readonly record struct BlockTeil(int Start, int Laenge)
{
    public int Ende => Start + Laenge - 1;
}

/// <summary>
/// Blockbildung und Zeilenmaße der Stundenplanansicht (testbar ohne Oberfläche):
/// <list type="bullet">
/// <item>Zusammenhängende Stunden mit gleichem Schlüssel werden zu einem Feld zusammengefasst, gleichzeitige Felder bekommen eigene Spuren.</item>
/// <item>Liegt zwischen zwei Stunden eines Feldes eine angezeigte Pausenzeile, wird das Feld in Teilen gezeichnet – nie über die Pause hinweg.</item>
/// <item>Zeilen haben eine Mindesthöhe; reicht der Platz nicht, scrollt der Plan statt die Zeilen zu quetschen.</item>
/// </list>
/// </summary>
public static class Blockbildung
{
    /// <summary>Felder bilden: <paramref name="am"/> liefert die Einträge einer Stunde, <paramref name="schluessel"/> null = nie zusammenfassen.</summary>
    /// <param name="stapeln">
    /// Vorbild-Darstellung der großen Pläne: Gleichzeitige Felder mit genau gleicher Lage (gleiche Stunden) werden nicht nebeneinander,
    /// sondern untereinander als eigene Zeilen in einem gemeinsamen Feld gezeigt (z. B. „D Way R105“ über „M LesD A117“).
    /// </param>
    public static List<PlanBlock<T>> Anordnen<T>(int stunden, Func<int, IReadOnlyList<T>> am, Func<T, string?> schluessel, bool stapeln = false)
    {
        var felder = new List<PlanBlock<T>>();
        var offen = new Dictionary<string, PlanBlock<T>>();
        for (var s = 1; s <= stunden; s++)
        {
            var neu = new Dictionary<string, PlanBlock<T>>();
            foreach (var x in am(s))
            {
                var k = schluessel(x);
                if (k != null && !neu.ContainsKey(k) && offen.TryGetValue(k, out var f))
                {
                    f.Alle.Add(x);
                    f.Laenge++;
                    neu[k] = f;
                    continue;
                }
                f = new PlanBlock<T> { Erster = x, Start = s };
                f.Alle.Add(x);
                felder.Add(f);
                if (k != null && !neu.ContainsKey(k)) neu[k] = f;
            }
            offen = neu;
        }
        // Gruppen sich zeitlich überschneidender Felder: je Gruppe so viele Spuren wie gleichzeitig nötig
        var gruppen = new List<List<PlanBlock<T>>>();
        foreach (var f in felder.OrderBy(x => x.Start))
        {
            var g = gruppen.LastOrDefault();
            if (g != null && g.Any(x => x.Ende >= f.Start)) g.Add(f);
            else gruppen.Add(new List<PlanBlock<T>> { f });
        }
        foreach (var g in gruppen)
        {
            var ende = new List<int>();
            foreach (var f in g.OrderBy(x => x.Start).ThenByDescending(x => x.Laenge))
            {
                var spur = ende.FindIndex(e => e < f.Start);
                if (spur < 0) { spur = ende.Count; ende.Add(0); }
                ende[spur] = f.Ende;
                f.Spur = spur;
            }
            foreach (var f in g) f.Spuren = ende.Count;
            if (stapeln && g.Count > 1 && g.All(x => x.Start == g[0].Start && x.Laenge == g[0].Laenge))
                foreach (var f in g)
                {
                    f.StapelIndex = f.Spur;
                    f.StapelAnzahl = f.Spuren;
                    f.Spur = 0;
                    f.Spuren = 1;
                }
        }
        return felder;
    }

    /// <summary>
    /// Teile eines Feldes zwischen den angezeigten Pausenzeilen. <paramref name="pausenNach"/> enthält die Stunden, nach denen eine
    /// Pausenzeile liegt; eine Pause nach Stunde s trennt s und s+1.
    /// </summary>
    public static List<BlockTeil> Teile(int start, int laenge, IReadOnlyCollection<int> pausenNach)
    {
        var res = new List<BlockTeil>();
        var anfang = start;
        var ende = start + Math.Max(1, laenge) - 1;
        for (var s = start; s < ende; s++)
        {
            if (!pausenNach.Contains(s)) continue;
            res.Add(new BlockTeil(anfang, s - anfang + 1));
            anfang = s + 1;
        }
        res.Add(new BlockTeil(anfang, ende - anfang + 1));
        return res;
    }

    /// <summary>Teile eines Feldes (siehe <see cref="Teile(int,int,IReadOnlyCollection{int})"/>).</summary>
    public static List<BlockTeil> Teile<T>(PlanBlock<T> f, IReadOnlyCollection<int> pausenNach) => Teile(f.Start, f.Laenge, pausenNach);

    /// <summary>Mindesthöhe einer Stundenzeile in Pixeln: Schrift lesbar, im kleinen Plan mindestens 13 Pixel.</summary>
    public static int MindestZeilenHoehe(float schriftgroesse, bool klein) =>
        klein ? Math.Max(13, (int)Math.Ceiling(schriftgroesse * 1.55f)) : Math.Max(18, (int)Math.Ceiling(schriftgroesse * 2.4f));

    /// <summary>Höhe, die der Plan mindestens braucht (Kopf, Pausenzeilen, Stunden in Mindesthöhe, Rahmen).</summary>
    public static int MindestHoehe(int kopf, int pausen, int pausenHoehe, int stunden, int zeilenHoehe) =>
        kopf + pausen * pausenHoehe + stunden * zeilenHoehe + 2;

    /// <summary>
    /// Höhe einer Stundenzeile bei der verfügbaren Höhe: der Platz wird auf die Stunden verteilt, aber nie unter die Mindesthöhe.
    /// <paramref name="scrollen"/> meldet, dass der Plan höher als der verfügbare Platz wird (Rollbalken statt Quetschen).
    /// </summary>
    public static float ZeilenHoehe(int verfuegbar, int kopf, int pausen, int pausenHoehe, int stunden, int mindest, out bool scrollen)
    {
        var h = (verfuegbar - kopf - 2 - pausen * pausenHoehe) / (float)Math.Max(1, stunden);
        scrollen = h < mindest;
        return Math.Max(mindest, h);
    }
}
