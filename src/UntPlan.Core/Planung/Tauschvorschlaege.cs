using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.Core.Planung;

/// <summary>Ein Zug eines Tauschvorschlags: Stunde (Index in <see cref="Schuldatei.Stundenplan"/>) an eine neue Position.</summary>
public sealed record TauschZug(int Index, int Tag, int Stunde);

/// <summary>
/// Zeile der Tauschvorschläge: 2er-, 3er- oder 4er-Tausch innerhalb der Klasse mit „Gewinn“ (positiv = Verbesserung der Bewertung),
/// „Auswirkungen“ (Kategorie der größten Verbesserung bzw. der gröbste neue Verstoß), bei „Mit Klassensprung“ der „Spr.Kla“
/// (Sprungklasse), dem besten Folgetausch dort und dem „Gesamt“-Gewinn beider Tausche.
/// </summary>
public sealed class TauschvorschlagZeile
{
    public int Art { get; init; }
    public string Beschreibung { get; init; } = "";
    public List<TauschZug> Zuege { get; init; } = new();
    public double Gewinn { get; init; }
    public string Auswirkungen { get; init; } = "";
    /// <summary>Sprungklasse (Klassensprung) oder leer.</summary>
    public string Sprungklasse { get; init; } = "";
    /// <summary>Möglichkeiten, die behindernde Stunde in der Sprungklasse zu verlegen (bester zuerst).</summary>
    public List<TauschvorschlagZeile> Folgetausche { get; init; } = new();
    /// <summary>Gewinn des Tausches plus Gewinn des besten Folgetausches.</summary>
    public double Gesamt => Gewinn + (Folgetausche.Count > 0 ? Folgetausche[0].Gewinn : 0);
    /// <summary>Alle auszuführenden Züge (bei Klassensprung inkl. des gewählten Folgetausches).</summary>
    public List<TauschZug> AlleZuege(TauschvorschlagZeile? folge = null) =>
        Zuege.Concat((folge ?? Folgetausche.FirstOrDefault())?.Zuege ?? new List<TauschZug>()).ToList();
}

/// <summary>Optionen der Tauschvorschläge.</summary>
public sealed class TauschvorschlagOptionen
{
    /// <summary>„Mit Klassensprung“: auch Tausche, die nur zusammen mit einer Verlegung in einer anderen Klasse kollisionsfrei sind.</summary>
    public bool MitKlassensprung { get; set; }
    /// <summary>„Nur verschiedene Lehrer“: keine Tausche zwischen Stunden derselben Lehrkraft.</summary>
    public bool NurVerschiedeneLehrer { get; set; }
    /// <summary>Auch 3er- und 4er-Tausche (Ringtausche) untersuchen.</summary>
    public bool Ringtausche { get; set; } = true;
    public int Maximal { get; set; } = 30;
}

/// <summary>
/// Tauschvorschläge (Handbuch, Manuelles Planen | Tauschvorschläge): für die aktive Stunde einer Klasse mögliche 2er-, 3er- und
/// 4er-Tausche innerhalb der Klasse, bewertet nach der Gewichtung. Angeboten werden nur Tausche ohne zusätzliche Kollisionen.
/// </summary>
public static class Tauschvorschlaege
{
    private static readonly HashSet<VerstossArt> Harte = new()
    {
        VerstossArt.Lehrerkollision, VerstossArt.Klassenkollision, VerstossArt.Raumkollision, VerstossArt.SperrungKlasse, VerstossArt.SperrungLehrer,
        VerstossArt.SperrungRaum, VerstossArt.SperrungUnterricht, VerstossArt.ZeitrasterLeer,
    };

    private sealed record Auswertung(double Kosten, int Harte, Dictionary<VerstossArt, double> JeArt);

    private static Auswertung Auswerten(Schuldatei d, IReadOnlyList<Planstunde> plan)
    {
        var v = Bewertung.Verstoesse(d, plan);
        return new Auswertung(v.Sum(x => x.Kosten), v.Count(x => Harte.Contains(x.Art)), v.GroupBy(x => x.Art).ToDictionary(g => g.Key, g => g.Sum(x => x.Kosten)));
    }

    private static string Auswirkung(Auswertung basis, Auswertung neu, double gewinn)
    {
        var arten = basis.JeArt.Keys.Union(neu.JeArt.Keys)
            .Select(a => (Art: a, Diff: neu.JeArt.GetValueOrDefault(a) - basis.JeArt.GetValueOrDefault(a))).ToList();
        var x = gewinn >= 0 ? arten.Where(a => a.Diff < 0).OrderBy(a => a.Diff).FirstOrDefault() : arten.Where(a => a.Diff > 0).OrderByDescending(a => a.Diff).FirstOrDefault();
        return x.Diff == 0 ? "" : Bewertung.ArtText(x.Art);
    }

    private static List<Planstunde> Plan(Schuldatei d, IEnumerable<TauschZug> zuege)
    {
        var z = zuege.ToDictionary(x => x.Index);
        return d.Stundenplan.Select((x, i) =>
        {
            if (!z.TryGetValue(i, out var m)) return x;
            var k = x.Kopie();
            k.Tag = m.Tag;
            k.Stunde = m.Stunde;
            return k;
        }).ToList();
    }

    private static string Pos(Schuldatei d, int tag, int stunde) => $"{Planungsdienst.Tagname(d, tag)}-{stunde}";

    public static List<TauschvorschlagZeile> Berechnen(Schuldatei d, Planstunde p, TauschvorschlagOptionen? o = null)
    {
        o ??= new TauschvorschlagOptionen();
        var ix = new DatenIndex(d);
        var pi = d.Stundenplan.IndexOf(p);
        if (pi < 0 || !ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return new();
        var klassen = u.KlassenIds.ToHashSet();
        var basis = Auswerten(d, d.Stundenplan);
        // Kandidaten: nicht fixierte Stunden anderer Unterrichte derselben Klasse(n) an anderen Positionen
        var kand = d.Stundenplan.Select((q, i) => (q, i)).Where(x => x.i != pi && !x.q.Fixiert && x.q.UnterrichtId != p.UnterrichtId &&
                (x.q.Tag != p.Tag || x.q.Stunde != p.Stunde) && ix.Unterrichte.TryGetValue(x.q.UnterrichtId, out var o2) && o2.KlassenIds.Any(klassen.Contains) &&
                !(o.NurVerschiedeneLehrer && o2.LehrerIds.Any(u.LehrerIds.Contains)))
            .GroupBy(x => (x.q.Tag, x.q.Stunde, x.q.UnterrichtId)).Select(g => g.First()).ToList();
        var res = new List<TauschvorschlagZeile>();
        string Text(int idx) => ix.UnterrichtText(ix.Unterrichte[d.Stundenplan[idx].UnterrichtId]);
        TauschvorschlagZeile? Pruefen(int art, List<TauschZug> zuege, string beschr, bool sprungErlaubt)
        {
            var neu = Auswerten(d, Plan(d, zuege));
            var gewinn = Math.Round(basis.Kosten - neu.Kosten);
            if (neu.Harte <= basis.Harte)
                return new TauschvorschlagZeile { Art = art, Zuege = zuege, Gewinn = gewinn, Auswirkungen = Auswirkung(basis, neu, gewinn), Beschreibung = beschr };
            if (!sprungErlaubt || !o.MitKlassensprung) return null;
            return Klassensprung(d, ix, zuege, basis, art, beschr, klassen);
        }
        foreach (var (q, qi) in kand)
        {
            var zuege = new List<TauschZug> { new(pi, q.Tag, q.Stunde), new(qi, p.Tag, p.Stunde) };
            var z = Pruefen(2, zuege, $"{Text(pi)} {Pos(d, p.Tag, p.Stunde)} ⇄ {Text(qi)} {Pos(d, q.Tag, q.Stunde)}", true);
            if (z != null) res.Add(z);
        }
        if (o.Ringtausche)
        {
            // 3er-Tausch: p → Platz von q, q → Platz von r, r → Platz von p (begrenzte Anzahl Kombinationen)
            var budget = 400;
            foreach (var (q, qi) in kand)
                foreach (var (r, ri) in kand)
                {
                    if (ri == qi || (r.Tag == q.Tag && r.Stunde == q.Stunde) || budget-- <= 0) continue;
                    var zuege = new List<TauschZug> { new(pi, q.Tag, q.Stunde), new(qi, r.Tag, r.Stunde), new(ri, p.Tag, p.Stunde) };
                    var z = Pruefen(3, zuege, $"{Text(pi)} → {Pos(d, q.Tag, q.Stunde)}, {Text(qi)} → {Pos(d, r.Tag, r.Stunde)}, {Text(ri)} → {Pos(d, p.Tag, p.Stunde)}", false);
                    if (z != null && z.Gewinn > 0) res.Add(z);
                }
            // 4er-Tausch: zwei 2er-Tausche hintereinander (p ⇄ q und dazu ein weiterer verbessernder Tausch der Klasse)
            var besteZwei = res.Where(x => x.Art == 2 && x.Gewinn <= 0 && x.Sprungklasse.Length == 0).OrderByDescending(x => x.Gewinn).Take(5).ToList();
            var weitere = d.Stundenplan.Select((q, i) => (q, i)).Where(x => !x.q.Fixiert && ix.Unterrichte.TryGetValue(x.q.UnterrichtId, out var o3) && o3.KlassenIds.Any(klassen.Contains)).ToList();
            var budget4 = 300;
            foreach (var zwei in besteZwei)
            {
                var belegt = zwei.Zuege.Select(z => z.Index).ToHashSet();
                foreach (var (a, ai) in weitere)
                    foreach (var (b, bi) in weitere)
                    {
                        if (ai >= bi || belegt.Contains(ai) || belegt.Contains(bi) || a.UnterrichtId == b.UnterrichtId || (a.Tag == b.Tag && a.Stunde == b.Stunde) || budget4-- <= 0) continue;
                        var zuege = zwei.Zuege.Concat(new[] { new TauschZug(ai, b.Tag, b.Stunde), new TauschZug(bi, a.Tag, a.Stunde) }).ToList();
                        var z = Pruefen(4, zuege, $"{zwei.Beschreibung}; {Text(ai)} {Pos(d, a.Tag, a.Stunde)} ⇄ {Text(bi)} {Pos(d, b.Tag, b.Stunde)}", false);
                        if (z != null && z.Gewinn > 0) res.Add(z);
                    }
            }
        }
        return res.OrderByDescending(x => x.Gesamt).ThenBy(x => x.Art).Take(o.Maximal).ToList();
    }

    /// <summary>
    /// Klassensprung: der Tausch ist nur wegen einer einzigen Stunde einer anderen Klasse nicht kollisionsfrei – diese Stunde wird
    /// zusätzlich verlegt (auf eine freie Position oder per 2er-Tausch in ihrer Klasse).
    /// </summary>
    private static TauschvorschlagZeile? Klassensprung(Schuldatei d, DatenIndex ix, List<TauschZug> zuege, Auswertung basis, int art, string beschr, HashSet<int> klassen)
    {
        var bewegt = zuege.Select(z => z.Index).ToHashSet();
        var plan = Plan(d, zuege);
        var hindernd = new HashSet<int>();
        foreach (var z in zuege)
        {
            var u = ix.Unterrichte[d.Stundenplan[z.Index].UnterrichtId];
            var ign = bewegt.Select(i => d.Stundenplan[i]).ToList();
            foreach (var h in Planungsdienst.Hindernisse(d, ix, u, z.Tag, z.Stunde, ign))
            {
                var hi = d.Stundenplan.IndexOf(h.Planstunde);
                if (hi >= 0 && !bewegt.Contains(hi)) hindernd.Add(hi);
            }
        }
        if (hindernd.Count != 1) return null;
        var xi = hindernd.First();
        var x = d.Stundenplan[xi];
        if (x.Fixiert || !ix.Unterrichte.TryGetValue(x.UnterrichtId, out var ux) || !ux.KlassenIds.Any() || ux.KlassenIds.Any(klassen.Contains)) return null;
        var nachTausch = Auswerten(d, plan);
        var gewinn1 = Math.Round(basis.Kosten - nachTausch.Kosten);
        var folge = new List<TauschvorschlagZeile>();
        var zr = d.Zeitraster;
        var xKlassen = ux.KlassenIds.ToHashSet();
        // freie Positionen und 2er-Tausche in der Sprungklasse
        var ziele = new List<List<TauschZug>>();
        for (var t = 0; t < zr.TageProWoche; t++)
            for (var s = 1; s <= zr.StundenProTag; s++)
                if (!(t == x.Tag && s == x.Stunde) && zr.Verfuegbar(t, s) && !plan.Any(q => q.Tag == t && q.Stunde == s && ix.Unterrichte.TryGetValue(q.UnterrichtId, out var uq) && uq.KlassenIds.Any(xKlassen.Contains)))
                    ziele.Add(new List<TauschZug> { new(xi, t, s) });
        for (var i = 0; i < d.Stundenplan.Count; i++)
        {
            var q = plan[i];
            if (i == xi || bewegt.Contains(i) || q.Fixiert || q.UnterrichtId == x.UnterrichtId || !ix.Unterrichte.TryGetValue(q.UnterrichtId, out var uq) || !uq.KlassenIds.Any(xKlassen.Contains)) continue;
            ziele.Add(new List<TauschZug> { new(xi, q.Tag, q.Stunde), new(i, x.Tag, x.Stunde) });
        }
        foreach (var z2 in ziele.Take(120))
        {
            var neu = Auswerten(d, Plan(d, zuege.Concat(z2)));
            if (neu.Harte > basis.Harte) continue;
            var g2 = Math.Round(nachTausch.Kosten - neu.Kosten);
            var text = z2.Count == 1
                ? $"{ix.UnterrichtText(ux)} {Pos(d, x.Tag, x.Stunde)} → {Pos(d, z2[0].Tag, z2[0].Stunde)}"
                : $"{ix.UnterrichtText(ux)} {Pos(d, x.Tag, x.Stunde)} ⇄ {ix.UnterrichtText(ix.Unterrichte[d.Stundenplan[z2[1].Index].UnterrichtId])} {Pos(d, z2[0].Tag, z2[0].Stunde)}";
            folge.Add(new TauschvorschlagZeile { Art = z2.Count == 1 ? 1 : 2, Zuege = z2, Gewinn = g2, Auswirkungen = Auswirkung(nachTausch, neu, g2), Beschreibung = text });
        }
        if (folge.Count == 0) return null;
        return new TauschvorschlagZeile
        {
            Art = art, Zuege = zuege, Gewinn = gewinn1, Auswirkungen = Auswirkung(basis, nachTausch, gewinn1), Beschreibung = beschr,
            Sprungklasse = string.Join(",", ux.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k))),
            Folgetausche = folge.OrderByDescending(f => f.Gewinn).Take(10).ToList(),
        };
    }

    /// <summary>Tausch ausführen: Stunden an die neuen Positionen setzen und die Räume neu suchen.</summary>
    public static void Ausfuehren(Schuldatei d, IReadOnlyList<TauschZug> zuege)
    {
        var stunden = zuege.Where(z => z.Index >= 0 && z.Index < d.Stundenplan.Count).Select(z => (P: d.Stundenplan[z.Index], z)).ToList();
        foreach (var (p, z) in stunden)
        {
            p.Tag = z.Tag;
            p.Stunde = z.Stunde;
        }
        var ix = new DatenIndex(d);
        foreach (var (p, _) in stunden)
            if (ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u))
                p.Raeume = Planungsdienst.RaeumeSuchen(d, ix, u, p.Tag, p.Stunde, new[] { p });
    }
}
