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
    /// <summary>„% der Klassenstunden zu verplanen (blank=100%)“: null = alle (bei Strategie C/D: <see cref="StartProzent"/>).</summary>
    public int? Prozent { get; set; }
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
    /// <summary>Lehrer-Zuordnung bei Optimierung: „Keine Optimierung der Zuordnung“ – Kennzeichen (V) Variabler Lehrer werden ignoriert.</summary>
    public bool KeineZuordnungsOptimierung { get; set; } = true;
    /// <summary>„Kein Lehrertausch mit anderem Fach“: Lehrkräfte nur zwischen Unterrichten mit gleichem Fach tauschen.</summary>
    public bool KeinTauschAnderesFach { get; set; } = true;
    /// <summary>„Tausch nur für gleichstündige Unt.“.</summary>
    public bool TauschNurGleichstuendig { get; set; } = true;
    /// <summary>„Tausch nur innerhalb Jahrgangsstufe“.</summary>
    public bool TauschNurJahrgang { get; set; } = true;
    /// <summary>„Raumkapazität berücksichtigen“: bei der Raumzuordnung Räume bevorzugen, deren Kapazität zur Schülerzahl passt.</summary>
    public bool RaumkapazitaetBeruecksichtigen { get; set; } = true;
    /// <summary>„Mit Voroptimierung“: beim Setzen werden mehr Varianten untersucht (längere Rechenzeit).</summary>
    public bool MitVoroptimierung { get; set; }
    /// <summary>„Kalenderverteilung beibehalten“ (nur gespeichert).</summary>
    public bool KalenderverteilungBeibehalten { get; set; }
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
    /// <summary>Lehrertausche der Lehrer-Zuordnung bei Optimierung (beim Übernehmen des Plans mit angewendet).</summary>
    public List<Lehrertausch> Lehrertausche { get; set; } = new();
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
    public static List<PlanErgebnis> Optimieren(Schuldatei d, OptimierungsOptionen o, IProgress<OptimierungsFortschritt>? fortschritt = null, CancellationToken ct = default) =>
        Optimieren(d, o, fortschritt, ct, default, null, null);

    /// <summary>
    /// Optimierung mit Begleitfenster: <paramref name="anhalten"/> („Optimierung anhalten – Sofort“) bricht laufende Pläne ab, liefert aber
    /// die bereits fertigen; <paramref name="weiter"/> wird vor jedem Plan gefragt („Nach Plan“: keine weiteren Pläne beginnen);
    /// <paramref name="fertig"/> meldet jeden fertigen Plan (für die Liste im Begleitfenster). <paramref name="ct"/> bricht alles ab.
    /// </summary>
    public static List<PlanErgebnis> Optimieren(Schuldatei d, OptimierungsOptionen o, IProgress<OptimierungsFortschritt>? fortschritt, CancellationToken ct,
        CancellationToken anhalten, Func<bool>? weiter, Action<PlanErgebnis>? fertig)
    {
        var anzahl = Math.Clamp(o.AnzahlPlaene, 1, 20);
        var ergebnisse = new PlanErgebnis?[anzahl];
        var erledigt = new double[anzahl];
        using var beide = CancellationTokenSource.CreateLinkedTokenSource(ct, anhalten);
        var parallel = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        Parallel.For(0, anzahl, parallel, i =>
        {
            if (anhalten.IsCancellationRequested || weiter?.Invoke() == false) return;
            try
            {
                var e = EinErgebnis(d, o, i, (a, phase, k) =>
                {
                    erledigt[i] = a;
                    fortschritt?.Report(new OptimierungsFortschritt(i + 1, anzahl, erledigt.Average(), phase, k));
                }, beide.Token);
                ergebnisse[i] = e;
                fertig?.Invoke(e);
            }
            // angehalten bzw. abgebrochen: Plan verwerfen – ein Abbruch wird nach der Schleife einheitlich gemeldet
            // (sonst verpackt Parallel.For die Ausnahme des verknüpften Tokens in eine AggregateException)
            catch (OperationCanceledException) { }
        });
        ct.ThrowIfCancellationRequested();
        return ergebnisse.Where(e => e != null).Select(e => e!).OrderBy(e => e.Kennzahlen.Bewertung).ToList();
    }

    /// <summary>Einen Plan der Serie rechnen (Setz-/Tauschoptimierung, danach ggf. Lehrer-Zuordnung) und bewerten.</summary>
    public static PlanErgebnis EinErgebnis(Schuldatei d, OptimierungsOptionen o, int i, Action<double, string, double>? fortschritt = null, CancellationToken ct = default)
    {
        var start = DateTime.Now;
        var plan = EinPlan(d, o, o.Seed + i * 7919, fortschritt, ct);
        var bewertet = d;
        var tausche = new List<Lehrertausch>();
        // Lehrer-Zuordnung bei Optimierung: Tausche unter variablen Lehrkräften, die den Plan verbessern
        if (!o.KeineZuordnungsOptimierung && Lehrerzuordnung.Moeglich(d))
        {
            fortschritt?.Invoke(1, "Lehrer-Zuordnung", 0);
            bewertet = Dokument.Klonen(d);
            tausche = Lehrerzuordnung.Optimieren(bewertet, plan, o, new Random(o.Seed + i * 31), ct);
        }
        return new PlanErgebnis { Nummer = i + 1, Plan = plan, Lehrertausche = tausche, Kennzahlen = Bewertung.Kennzahlen(bewertet, plan), Dauer = DateTime.Now - start };
    }

    /// <summary>Schuldatei mit dem Plan (und den Lehrertauschen) eines Ergebnisses – Inhalt einer Arbeitsdatei („work1.up“ …).</summary>
    public static Schuldatei MitErgebnis(Schuldatei d, PlanErgebnis e)
    {
        var k = Dokument.Klonen(d);
        k.Stundenplan.Clear();
        k.Stundenplan.AddRange(e.Plan.Select(p => p.Kopie()));
        Lehrerzuordnung.Anwenden(k, e.Lehrertausche);
        return k;
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
        if (o.MitVoroptimierung) basis = basis * 3 / 2;
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
            // (M) Manuell planen: vorhandene Stunden bleiben, die Optimierung verplant den Unterricht nicht
            var fix = p.Fixiert || u.Fix || u.ManuellPlanen || o.NurNichtVerplante || IstFixiertesElement(u, ix);
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
            if (rest <= 0 || u.ManuellPlanen) continue;
            // (B) Bedingt fixieren beim Unterricht: im Setzlauf an der bisherigen Stelle, in der Tauschoptimierung beweglich
            if (o.BedingtFixieren || u.BedingtFixieren)
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
            // Fixe Fachfolge: alle Stunden des Unterrichts als ein Block
            var bloecke = m.InFixerFolge(ui) && rest <= m.S ? new List<int> { rest } : u.Bloecke();
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
            // (S) Klassengruppe später planen: diese Unterrichte werden erst nach den übrigen gesetzt
            var spaeter = b.Mitglieder.Any(ui => m.Unterrichte[ui].KlassengruppeSpaeter) ? 10000 : 0;
            return b.Laenge * 30 + b.Entitaeten.Length * 6 + b.Pools.Length * 10 + b.Mitglieder.Length * 8 - moeglich * 0.5 + rnd.NextDouble() * 4 - spaeter;
        });
        var reihenfolge = beweglich.OrderByDescending(bi => schwierigkeit[bi]).ToList();

        // „% der Klassenstunden zu verplanen (blank=100%)“: gilt für alle Strategien; bei C/D Startwert der Prozentverplanung
        var prozent = Math.Clamp(o.Prozent ?? (o.Strategie is Strategie.C or Strategie.D ? o.StartProzent : 100), 1, 100);
        var erhoehen = o.Strategie is Strategie.C or Strategie.D;
        var erhoehung = Math.Clamp(o.ErhoehungProzent, 5, 100);
        var iterGesamt = Iterationen(o, beweglich.Count);
        var gesetztBis = 0;
        var phasen = new List<int>();
        while (true)
        {
            var bis = (int)Math.Ceiling(reihenfolge.Count * prozent / 100.0);
            phasen.Add(Math.Min(bis, reihenfolge.Count));
            if (prozent >= 100 || !erhoehen) break;
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
        var gleicherRaum = new Dictionary<(int, int), int>(); // (r) Stunden im selben Raum: bisher gewählter Raum je Zeile
        var imFachraum = new Dictionary<(int, int), int>(); // Stunden im Raum: bereits im Fachraum verplante Stunden je Zeile
        foreach (var bi in zuordnen)
        {
            var b = m.Bloecke[bi];
            foreach (var mi in b.Mitglieder)
            {
                var u = m.Unterrichte[mi];
                var raeume = new List<int?>();
                for (var zi = 0; zi < u.Zeilen.Count; zi++)
                {
                    var z = u.Zeilen[zi];
                    int? gewaehlt = null;
                    IEnumerable<int> kandidaten = RaumReihenfolge(d, ix, u, z, o);
                    // Stunden im Raum: ist die Anzahl im Fachraum erreicht, den Stammraum verwenden
                    var fachraumVoll = u.StundenImRaum > 0 && z.FachraumId.HasValue && imFachraum.GetValueOrDefault((u.Id, zi)) + b.Laenge > u.StundenImRaum;
                    if (fachraumVoll)
                        kandidaten = z.StammraumId is int st ? RaumReihenfolge(d, ix, u, new Kopplungszeile { StammraumId = st, KlassenIds = z.KlassenIds }, o) : new List<int>();
                    if (u.SelberRaum && gleicherRaum.TryGetValue((u.Id, zi), out var bisher)) kandidaten = new[] { bisher }.Concat(kandidaten.Where(r => r != bisher));
                    foreach (var r in kandidaten)
                    {
                        var frei = true;
                        for (var j = 0; j < b.Laenge && frei; j++)
                            if (raumBelegt.Contains((r, b.Tag, b.Stunde + 1 + j)) || ix.Raeume[r].Zeitwuensche.Gesperrt(b.Tag, b.Stunde + 1 + j)) frei = false;
                        if (frei) { gewaehlt = r; break; }
                    }
                    if (gewaehlt.HasValue)
                    {
                        for (var j = 0; j < b.Laenge; j++) raumBelegt.Add((gewaehlt.Value, b.Tag, b.Stunde + 1 + j));
                        gleicherRaum.TryAdd((u.Id, zi), gewaehlt.Value);
                        if (!fachraumVoll && z.FachraumId.HasValue) imFachraum[(u.Id, zi)] = imFachraum.GetValueOrDefault((u.Id, zi)) + b.Laenge;
                    }
                    raeume.Add(gewaehlt);
                }
                for (var j = 0; j < b.Laenge; j++)
                    plan.Add(new Planstunde { UnterrichtId = u.Id, Tag = b.Tag, Stunde = b.Stunde + 1 + j, Raeume = new List<int?>(raeume) });
            }
        }
        return plan;
    }

    /// <summary>
    /// Reihenfolge der Raumkandidaten einer Kopplungszeile: Wunschraum und Ausweichräume in Eingabereihenfolge; bei „Raumkapazität
    /// berücksichtigen“ zuerst Räume, die groß genug sind (in Eingabereihenfolge), dann die etwas zu kleinen (der größte zuerst).
    /// </summary>
    public static IEnumerable<int> RaumReihenfolge(Schuldatei d, DatenIndex ix, Unterricht u, Kopplungszeile z, OptimierungsOptionen o)
    {
        var kand = ix.RaumKandidaten(u, z); // beachtet (K) Kein Ausweichraum
        if (!o.RaumkapazitaetBeruecksichtigen) return kand;
        var schueler = z.KlassenIds.Sum(k => ix.Klassen.TryGetValue(k, out var kl) ? kl.Schuelerzahl : 0);
        if (u.MaxSchueler > 0) schueler = u.MaxSchueler;
        if (schueler <= 0 || kand.All(r => ix.Raeume[r].Kapazitaet <= 0)) return kand;
        int Kap(int r) => ix.Raeume[r].Kapazitaet;
        return kand.Select((r, i) => (r, i))
            .OrderBy(x => Kap(x.r) <= 0 ? 1 : Kap(x.r) >= schueler ? 0 : 2)
            .ThenBy(x => Kap(x.r) >= schueler ? 0 : schueler - Kap(x.r))
            .ThenBy(x => x.i).Select(x => x.r).ToList();
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
        k += m.FolgenKosten(unts);
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
        for (var j = 0; j < b.Laenge; j++) if (el.Zeitwuensche.Gesperrt(b.Tag, b.Stunde + 1 + j) || m.Gesperrt(e, b.Tag * m.S + b.Stunde + j)) return true;
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
