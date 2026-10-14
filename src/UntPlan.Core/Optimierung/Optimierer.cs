using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

public enum Strategie
{
    /// <summary>A – Schnelle Optimierung (eignet sich zum Finden von Eingabefehlern).</summary>
    A,
    /// <summary>B – Aufwändige Optimierung.</summary>
    B,
    /// <summary>C – Schnelle Prozentverplanung.</summary>
    C,
    /// <summary>D – Aufwändige Prozentverplanung (Start- und Erhöhungs-Prozentsatz).</summary>
    D,
    /// <summary>E – Übernacht-Optimierung.</summary>
    E,
}

public sealed class OptimierungsOptionen
{
    public Strategie Strategie { get; set; } = Strategie.B;
    /// <summary>Je Serie der Optimierung: Anzahl Pläne (1–20).</summary>
    public int AnzahlPlaene { get; set; } = 3;
    /// <summary>Optimierungsschritte je Plan (1–9).</summary>
    public int Schritte { get; set; } = 2;
    public int StartProzent { get; set; } = 30;
    public int ErhoehungProzent { get; set; } = 20;
    /// <summary>Nur nicht verplante Stunden setzen; bestehende Stunden bleiben.</summary>
    public bool NurNichtVerplante { get; set; }
    /// <summary>Ähnlichkeit zum vorhandenen Plan (0 = nicht ähnlich … 4 = sehr ähnlich): Abweichungen kosten Punkte.</summary>
    public int Aehnlichkeit { get; set; }
    /// <summary>Plan bedingt fixieren: vorhandene Stunden werden nur verlegt, wenn es gar nicht anders geht.</summary>
    public bool BedingtFixieren { get; set; }
    /// <summary>Freie Tage von Lehrern nur an gewünschten Tagen (Zeitwunsch negativ am ganzen Tag).</summary>
    public bool NurGewuenschteFreieTage { get; set; }
    /// <summary>Doppelstunden besonders optimieren (doppelte Gewichtung der Doppelstundenbedingung).</summary>
    public bool DoppelstundenBesonders { get; set; }
    public int Seed { get; set; } = Environment.TickCount;
}

public static class StrategieInfo
{
    public static string Name(Strategie s) => s switch
    {
        Strategie.A => "A Schnelle Optimierung",
        Strategie.B => "B Aufwändige Optimierung",
        Strategie.C => "C Schnelle Prozentverplanung",
        Strategie.D => "D Aufwändige Prozentverplanung",
        _ => "E Übernacht-Optimierung",
    };
}

/// <summary>Kennzahlen eines Stundenplans (Ergebnisfenster der Optimierung).</summary>
public sealed class Kennzahlen
{
    public long Bewertung { get; set; }
    public int NichtVerplant { get; set; }
    public int HohlstundenKlassen { get; set; }
    public int HohlstundenLehrer { get; set; }
    public int Kernzeitverletzungen { get; set; }
    public int FachZweimalTag { get; set; }
    public int Doppelstundenfehler { get; set; }
    public int Zeitwunschverletzungen { get; set; }
    public int Kollisionen { get; set; }
    public int OhneRaum { get; set; }
}

public sealed class PlanErgebnis
{
    public int Nummer { get; set; }
    public List<Planstunde> Plan { get; set; } = new();
    public Kennzahlen Kennzahlen { get; set; } = new();
    public TimeSpan Dauer { get; set; }
}

public sealed record OptimierungsFortschritt(int Plan, int Plaene, double Anteil, string Phase, double Kosten);

/// <summary>
/// Automatische Verplanung. Jeder Lauf besteht aus Setzoptimierung (schwierigste Stunden zuerst in den leeren Raster)
/// und Tauschoptimierung (gezielte Verschiebungen und Täusche, Simulated Annealing).
/// </summary>
public static class Optimierer
{
    public static List<PlanErgebnis> Optimieren(Schuldatei d, OptimierungsOptionen o, IProgress<OptimierungsFortschritt>? fortschritt = null, CancellationToken ct = default)
    {
        var anzahl = Math.Clamp(o.AnzahlPlaene, 1, 20);
        var ergebnisse = new PlanErgebnis[anzahl];
        var erledigt = new double[anzahl];
        var parallel = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        Parallel.For(0, anzahl, parallel, i =>
        {
            var start = DateTime.Now;
            var plan = EinPlan(d, o, o.Seed + i * 7919, (a, phase, k) =>
            {
                erledigt[i] = a;
                fortschritt?.Report(new OptimierungsFortschritt(i + 1, anzahl, erledigt.Average(), phase, k));
            }, ct);
            ergebnisse[i] = new PlanErgebnis { Nummer = i + 1, Plan = plan, Kennzahlen = Bewertung.Kennzahlen(d, plan), Dauer = DateTime.Now - start };
        });
        return ergebnisse.OrderBy(e => e.Kennzahlen.Bewertung).ToList();
    }

    private static int Iterationen(OptimierungsOptionen o, int bloecke)
    {
        var basis = o.Strategie switch
        {
            Strategie.A => 6,
            Strategie.B => 40,
            Strategie.C => 10,
            Strategie.D => 50,
            _ => 250,
        };
        var schritte = Math.Clamp(o.Schritte, 1, 9);
        return (int)Math.Min(20_000_000, (long)basis * bloecke * (2 + schritte * 2));
    }

    public static List<Planstunde> EinPlan(Schuldatei d, OptimierungsOptionen o, int seed, Action<double, string, double>? fortschritt = null, CancellationToken ct = default)
    {
        var rnd = new Random(seed);
        var m = new PlanModell(d, mitPools: true);
        m.OptionenSetzen(o);
        var ix = new DatenIndex(d);
        var gesperrteQuellen = new List<Planstunde>();

        // --- Bestehende Stunden: fixierte (oder alle bei "nur nicht verplante") bleiben als gesperrte Blöcke
        var gesetzt = new Dictionary<int, int>(); // Unterricht -> bereits gesetzte Stunden
        foreach (var p in d.Stundenplan)
        {
            var ui = m.UnterrichtIdx(p.UnterrichtId);
            if (ui < 0) continue;
            var u = m.Unterrichte[ui];
            var fix = p.Fixiert || u.Fix || o.NurNichtVerplante || IstFixiertesElement(u, ix);
            if (!fix) continue;
            var bi = m.NeuerBlock(new[] { ui }, 1, true, p);
            m.Platzieren(bi, p.Tag, p.Stunde - 1);
            gesperrteQuellen.Add(p);
            gesetzt[ui] = gesetzt.GetValueOrDefault(ui) + 1;
        }

        // --- Zu verplanende Blöcke (Bänder werden zusammengefasst)
        var bandGruppen = m.Unterrichte.Select((u, i) => (u, i)).Where(x => !string.IsNullOrWhiteSpace(x.u.Band)).GroupBy(x => x.u.Band.Trim());
        var inBand = new HashSet<int>();
        foreach (var g in bandGruppen)
        {
            var mitglieder = g.Select(x => x.i).ToList();
            inBand.UnionWith(mitglieder);
            var rest = mitglieder.ToDictionary(i => i, i => Math.Max(0, m.Unterrichte[i].Wochenstunden - gesetzt.GetValueOrDefault(i)));
            var fuehrend = mitglieder.OrderByDescending(i => rest[i]).First();
            var bloecke = m.Unterrichte[fuehrend].Bloecke();
            BloeckeKuerzen(bloecke, rest[fuehrend]);
            var bisher = 0;
            foreach (var len in bloecke)
            {
                var aktiv = mitglieder.Where(i => rest[i] > bisher).ToArray();
                if (aktiv.Length > 0) m.NeuerBlock(aktiv, len, false);
                bisher += len;
            }
        }
        var vorStart = new Dictionary<int, (int Tag, int Stunde)>();
        for (var ui = 0; ui < m.Unterrichte.Count; ui++)
        {
            if (inBand.Contains(ui)) continue;
            var u = m.Unterrichte[ui];
            var rest = u.Wochenstunden - gesetzt.GetValueOrDefault(ui);
            if (rest <= 0) continue;
            if (o.BedingtFixieren)
            {
                // vorhandene Stunden als Blöcke an ihrer bisherigen Position übernehmen (verschiebbar, aber teuer)
                foreach (var tag in d.Stundenplan.Where(p => p.UnterrichtId == u.Id && !gesperrteQuellen.Contains(p)).GroupBy(p => p.Tag))
                {
                    var stunden = tag.Select(p => p.Stunde).Distinct().OrderBy(x => x).ToList();
                    for (var i = 0; i < stunden.Count && rest > 0;)
                    {
                        var j = i;
                        while (j + 1 < stunden.Count && stunden[j + 1] == stunden[j] + 1) j++;
                        var len = Math.Min(j - i + 1, rest);
                        var bi = m.NeuerBlock(new[] { ui }, len, false);
                        vorStart[bi] = (tag.Key, stunden[i] - 1);
                        rest -= len;
                        i = j + 1;
                    }
                }
                if (rest <= 0) continue;
            }
            var bloecke = u.Bloecke();
            BloeckeKuerzen(bloecke, rest);
            foreach (var len in bloecke) m.NeuerBlock(new[] { ui }, len, false);
        }

        var beweglich = Enumerable.Range(0, m.Bloecke.Count).Where(i => !m.Bloecke[i].Gesperrt).ToList();
        if (beweglich.Count == 0) return gesperrteQuellen.Select(p => p.Kopie()).ToList();

        // --- Ähnlichkeit zum Vorplan bzw. bedingtes Fixieren: Startpositionen abseits des bisherigen Plans kosten Punkte
        var stufe = o.BedingtFixieren ? 6 : Math.Clamp(o.Aehnlichkeit, 0, 4);
        if (stufe > 0)
        {
            var vorplan = d.Stundenplan.GroupBy(p => m.UnterrichtIdx(p.UnterrichtId)).Where(g => g.Key >= 0)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Tag * m.S + p.Stunde - 1).ToHashSet());
            var gewicht = stufe switch { 1 => 2.0, 2 => 6.0, 3 => 15.0, 4 => 40.0, _ => 400.0 };
            foreach (var bi in beweglich)
            {
                var b = m.Bloecke[bi];
                if (!b.Mitglieder.Any(vorplan.ContainsKey)) continue;
                for (var c = 0; c < m.C; c++)
                {
                    if (double.IsInfinity(b.Statisch[c])) continue;
                    var abweichend = 0;
                    for (var j = 0; j < b.Laenge && c + j < m.C; j++)
                        if (b.Mitglieder.Any(ui => vorplan.TryGetValue(ui, out var z) && !z.Contains(c + j))) abweichend++;
                    b.Statisch[c] += abweichend * gewicht;
                }
            }
        }

        // --- Setzoptimierung: Reihenfolge nach Schwierigkeit
        var schwierigkeit = beweglich.ToDictionary(bi => bi, bi =>
        {
            var b = m.Bloecke[bi];
            var moeglich = 0;
            for (var c = 0; c < m.C; c++) if (b.Statisch[c] < PlanModell.Hart) moeglich++;
            return b.Laenge * 30 + b.Entitaeten.Length * 6 + b.Pools.Length * 10 + b.Mitglieder.Length * 8 - moeglich * 0.5 + rnd.NextDouble() * 4;
        });
        var reihenfolge = beweglich.OrderByDescending(bi => schwierigkeit[bi]).ToList();

        var prozent = o.Strategie is Strategie.C or Strategie.D ? Math.Clamp(o.StartProzent, 5, 100) : 100;
        var erhoehung = Math.Clamp(o.ErhoehungProzent, 5, 100);
        var iterGesamt = Iterationen(o, beweglich.Count);
        var gesetztBis = 0;
        var phasen = new List<int>();
        while (true)
        {
            var bis = (int)Math.Ceiling(reihenfolge.Count * prozent / 100.0);
            phasen.Add(Math.Min(bis, reihenfolge.Count));
            if (prozent >= 100) break;
            prozent = Math.Min(100, prozent + erhoehung);
        }
        var iterJePhase = iterGesamt / phasen.Count;
        var aktive = new List<int>();
        for (var ph = 0; ph < phasen.Count; ph++)
        {
            for (; gesetztBis < phasen[ph]; gesetztBis++)
            {
                ct.ThrowIfCancellationRequested();
                var bi = reihenfolge[gesetztBis];
                if (vorStart.TryGetValue(bi, out var st) && m.Bloecke[bi].Statisch[st.Tag * m.S + st.Stunde] < PlanModell.Hart) m.Platzieren(bi, st.Tag, st.Stunde);
                else BesteStelle(m, bi, rnd, erlaubeUnplatziert: false);
                aktive.Add(bi);
            }
            fortschritt?.Invoke((ph + 0.2) / phasen.Count, "Setzoptimierung", 0);
            Tauschoptimierung(m, aktive, iterJePhase, rnd, ct, (a, k) => fortschritt?.Invoke((ph + 0.2 + 0.8 * a) / phasen.Count, "Tauschoptimierung", k));
        }

        // --- Bereinigung: harte Konflikte auflösen, sonst Stunde nicht verplanen
        Bereinigen(m, aktive, rnd);
        fortschritt?.Invoke(1, "Raumzuordnung", 0);

        // --- Ergebnis mit Raumzuordnung
        var plan = new List<Planstunde>();
        var raumBelegt = new HashSet<(int Raum, int Tag, int Stunde)>();
        foreach (var bi in Enumerable.Range(0, m.Bloecke.Count).Where(i => m.Bloecke[i].Gesperrt))
        {
            var q = m.Bloecke[bi].Quelle!;
            var kopie = q.Kopie();
            plan.Add(kopie);
            foreach (var r in kopie.Raeume) if (r.HasValue) raumBelegt.Add((r.Value, kopie.Tag, kopie.Stunde));
        }
        // Blöcke mit hohem Raumgewicht zuerst
        var zuordnen = aktive.Where(bi => m.Bloecke[bi].Platziert)
            .OrderByDescending(bi => m.Bloecke[bi].Mitglieder.SelectMany(mi => m.Unterrichte[mi].Zeilen).Select(ix.Raumgewicht).DefaultIfEmpty(0).Max())
            .ThenByDescending(bi => m.Bloecke[bi].Laenge);
        foreach (var bi in zuordnen)
        {
            var b = m.Bloecke[bi];
            foreach (var mi in b.Mitglieder)
            {
                var u = m.Unterrichte[mi];
                var raeume = new List<int?>();
                foreach (var z in u.Zeilen)
                {
                    int? gewaehlt = null;
                    foreach (var r in ix.RaumKandidaten(z))
                    {
                        var frei = true;
                        for (var j = 0; j < b.Laenge && frei; j++)
                            if (raumBelegt.Contains((r, b.Tag, b.Stunde + 1 + j)) || ix.Raeume[r].Zeitwuensche.Gesperrt(b.Tag, b.Stunde + 1 + j)) frei = false;
                        if (frei) { gewaehlt = r; break; }
                    }
                    if (gewaehlt.HasValue)
                        for (var j = 0; j < b.Laenge; j++) raumBelegt.Add((gewaehlt.Value, b.Tag, b.Stunde + 1 + j));
                    raeume.Add(gewaehlt);
                }
                for (var j = 0; j < b.Laenge; j++)
                    plan.Add(new Planstunde { UnterrichtId = u.Id, Tag = b.Tag, Stunde = b.Stunde + 1 + j, Raeume = new List<int?>(raeume) });
            }
        }
        return plan;
    }

    private static void BloeckeKuerzen(List<int> bloecke, int rest)
    {
        while (bloecke.Sum() > rest && bloecke.Count > 0)
        {
            var i = bloecke.Count - 1;
            var ueber = bloecke.Sum() - rest;
            if (bloecke[i] > ueber) bloecke[i] -= ueber;
            else bloecke.RemoveAt(i);
        }
    }

    internal static bool IstFixiertesElement(Unterricht u, DatenIndex ix) =>
        u.LehrerIds.Any(l => ix.Lehrer.TryGetValue(l, out var le) && le.Fix) ||
        u.KlassenIds.Any(k => ix.Klassen.TryGetValue(k, out var kl) && kl.Fix);

    private static double LokaleKosten(PlanModell m, IEnumerable<int> bloecke)
    {
        var ents = new HashSet<int>();
        var unts = new HashSet<int>();
        double k = 0;
        foreach (var bi in bloecke)
        {
            var b = m.Bloecke[bi];
            foreach (var e in b.Entitaeten) ents.Add(e);
            foreach (var u in b.Mitglieder) unts.Add(u);
            k += m.BlockKosten(bi);
        }
        foreach (var e in ents) k += m.EntitaetKosten(e);
        foreach (var u in unts) k += m.UnterrichtKosten(u);
        return k;
    }

    private static double PoolLokal(PlanModell m, int bi, int tag, int stunde)
    {
        var b = m.Bloecke[bi];
        if (tag < 0 || b.Pools.Length == 0) return 0;
        double k = 0;
        foreach (var p in b.Pools.Distinct())
            for (var j = 0; j < b.Laenge; j++) k += m.PoolKosten(p, tag * m.S + stunde + j);
        return k;
    }

    private static void RueckPlatzieren(PlanModell m, int bi, int tag, int stunde)
    {
        m.Setzen(bi, -1);
        m.Bloecke[bi].Tag = tag;
        m.Bloecke[bi].Stunde = stunde;
        m.Setzen(bi, +1);
    }

    private static void Verschieben(PlanModell m, int bi, int tag, int stunde)
    {
        if (tag < 0)
        {
            m.Setzen(bi, -1);
            m.Bloecke[bi].Tag = -1;
            m.Bloecke[bi].Stunde = -1;
        }
        else m.Platzieren(bi, tag, stunde);
    }

    private static void BesteStelle(PlanModell m, int bi, Random rnd, bool erlaubeUnplatziert)
    {
        var b = m.Bloecke[bi];
        var vorherTag = b.Tag;
        var vorherStunde = b.Stunde;
        var basis = LokaleKosten(m, new[] { bi }) + PoolLokal(m, bi, b.Tag, b.Stunde);
        double best = erlaubeUnplatziert ? 0 : double.PositiveInfinity;
        var kandidaten = new List<(int, int)>();
        if (erlaubeUnplatziert)
        {
            Verschieben(m, bi, -1, -1);
            best = LokaleKosten(m, new[] { bi }) - basis;
            kandidaten.Add((-1, -1));
        }
        for (var t = 0; t < m.T; t++)
        {
            for (var s = 0; s + b.Laenge <= m.S; s++)
            {
                if (b.Statisch[t * m.S + s] >= PlanModell.Hart && kandidaten.Count > 0) continue;
                Verschieben(m, bi, t, s);
                var k = LokaleKosten(m, new[] { bi }) + PoolLokal(m, bi, t, s) - basis;
                if (k < best - 1e-9) { best = k; kandidaten.Clear(); kandidaten.Add((t, s)); }
                else if (Math.Abs(k - best) < 1e-9) kandidaten.Add((t, s));
            }
        }
        var (bt, bs) = kandidaten.Count > 0 ? kandidaten[rnd.Next(kandidaten.Count)] : (vorherTag, vorherStunde);
        Verschieben(m, bi, bt, bs);
    }

    private static void Tauschoptimierung(PlanModell m, List<int> aktive, int iterationen, Random rnd, CancellationToken ct, Action<double, double>? fortschritt)
    {
        if (aktive.Count == 0 || iterationen <= 0) return;
        double t0 = 80, t1 = 0.05;
        var melden = Math.Max(1, iterationen / 100);
        for (var it = 0; it < iterationen; it++)
        {
            if ((it & 1023) == 0) ct.ThrowIfCancellationRequested();
            var temp = t0 * Math.Pow(t1 / t0, it / (double)iterationen);
            var bi = aktive[rnd.Next(aktive.Count)];
            var b = m.Bloecke[bi];
            var wahl = rnd.NextDouble();
            if (!b.Platziert || wahl < 0.55)
            {
                // Verschieben (bzw. unverplante Stunde setzen)
                var nt = rnd.Next(m.T);
                var ns = rnd.Next(m.S - b.Laenge + 1);
                if (b.Statisch[nt * m.S + ns] >= PlanModell.Hart) continue;
                if (nt == b.Tag && ns == b.Stunde) continue;
                int ot = b.Tag, os = b.Stunde;
                var vorher = LokaleKosten(m, new[] { bi }) + PoolLokal(m, bi, ot, os) + PoolLokal(m, bi, nt, ns);
                Verschieben(m, bi, nt, ns);
                var nachher = LokaleKosten(m, new[] { bi }) + PoolLokal(m, bi, ot, os) + PoolLokal(m, bi, nt, ns);
                var delta = nachher - vorher;
                if (!(delta <= 0 || rnd.NextDouble() < Math.Exp(-delta / temp))) Verschieben(m, bi, ot, os);
            }
            else
            {
                // Tausch mit einem Block gleicher Länge
                var bj = aktive[rnd.Next(aktive.Count)];
                var c = m.Bloecke[bj];
                if (bj == bi || c.Laenge != b.Laenge || !c.Platziert || (c.Tag == b.Tag && c.Stunde == b.Stunde)) continue;
                int bt = b.Tag, bs = b.Stunde, ctg = c.Tag, cs = c.Stunde;
                var paar = new[] { bi, bj };
                var vorher = LokaleKosten(m, paar) + PoolLokal(m, bi, bt, bs) + PoolLokal(m, bj, ctg, cs);
                Verschieben(m, bi, ctg, cs);
                Verschieben(m, bj, bt, bs);
                var nachher = LokaleKosten(m, paar) + PoolLokal(m, bi, ctg, cs) + PoolLokal(m, bj, bt, bs);
                var delta = nachher - vorher;
                if (!(delta <= 0 || rnd.NextDouble() < Math.Exp(-delta / temp)))
                {
                    Verschieben(m, bi, bt, bs);
                    Verschieben(m, bj, ctg, cs);
                }
            }
            if (it % melden == 0) fortschritt?.Invoke(it / (double)iterationen, 0);
        }
    }

    private static bool HatHartenKonflikt(PlanModell m, int bi)
    {
        var b = m.Bloecke[bi];
        if (!b.Platziert) return false;
        if (b.Statisch[b.Tag * m.S + b.Stunde] >= PlanModell.Hart) return true;
        for (var j = 0; j < b.Laenge; j++)
        {
            var c = b.Tag * m.S + b.Stunde + j;
            foreach (var e in b.Entitaeten)
            {
                if (m.Kollidiert(e, c)) return true;
            }
        }
        return b.Entitaeten.Any(e => GesperrtFuer(m, e, b));
    }

    private static bool GesperrtFuer(PlanModell m, int e, PlanModell.Block b)
    {
        var el = e < m.NL ? (Element)m.LehrerArr[e] : m.KlassenArr[e - m.NL];
        for (var j = 0; j < b.Laenge; j++) if (el.Zeitwuensche.Gesperrt(b.Tag, b.Stunde + 1 + j)) return true;
        return false;
    }

    private static void Bereinigen(PlanModell m, List<int> aktive, Random rnd)
    {
        for (var runde = 0; runde < 3; runde++)
        {
            var konflikte = aktive.Where(bi => HatHartenKonflikt(m, bi)).ToList();
            if (konflikte.Count == 0) break;
            foreach (var bi in konflikte)
            {
                if (!HatHartenKonflikt(m, bi)) continue;
                BesteStelle(m, bi, rnd, erlaubeUnplatziert: false);
                if (HatHartenKonflikt(m, bi)) Verschieben(m, bi, -1, -1);
            }
        }
        // Nicht verplante Blöcke nochmals versuchen
        foreach (var bi in aktive.Where(bi => !m.Bloecke[bi].Platziert).ToList())
        {
            BesteStelle(m, bi, rnd, erlaubeUnplatziert: true);
            if (HatHartenKonflikt(m, bi)) Verschieben(m, bi, -1, -1);
        }
    }
}
