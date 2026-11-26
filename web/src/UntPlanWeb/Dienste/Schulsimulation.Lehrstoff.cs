using System.Text.Json;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>Fachgruppe der Unterrichtsreihen (Kürzel, Schlüsselwörter des Langnamens, Reihen je Stufe).</summary>
public sealed record Fachgruppe(string Id, List<string> Kuerzel, List<string> Worte, Dictionary<string, List<string>> Stufen)
{
    /// <summary>Klassenleitungsstunde: wiederkehrende Themen (Klassenrat, Organisatorisches) statt einer fortlaufenden Reihe.</summary>
    public bool Zyklisch => Id == "klassenleitung";
}

/// <summary>Katalog der Unterrichtsreihen (eingebettete Ressource Dienste/Simulation/lehrstoff.json).</summary>
public sealed record Lehrstoffkatalog(List<Fachgruppe> Gruppen, List<string> NichtKuerzel, List<string> NichtWorte);

public sealed partial class Schulsimulation
{
    private static readonly Lazy<Lehrstoffkatalog> KatalogLaden = new(() =>
    {
        using var strom = typeof(Schulsimulation).Assembly.GetManifestResourceStream("UntPlanWeb.Simulation.lehrstoff.json")
                          ?? throw new InvalidOperationException("Lehrstoffkatalog fehlt.");
        using var doc = JsonDocument.Parse(strom);
        var w = doc.RootElement;
        static List<string> Liste(JsonElement e) => e.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var gruppen = w.GetProperty("gruppen").EnumerateArray().Select(g => new Fachgruppe(g.GetProperty("id").GetString()!, Liste(g.GetProperty("kuerzel")),
            Liste(g.GetProperty("worte")), g.GetProperty("stufen").EnumerateObject().ToDictionary(p => p.Name, p => Liste(p.Value)))).ToList();
        return new(gruppen, Liste(w.GetProperty("nicht").GetProperty("kuerzel")), Liste(w.GetProperty("nicht").GetProperty("worte")));
    });

    public static Lehrstoffkatalog Katalog => KatalogLaden.Value;

    /// <summary>
    /// Fachgruppe eines Fachs aus Kürzel und Langname. null = kein Unterricht (Bereitschaft, Besprechung, Sprechstunde, Vertretungsplanung, Online-Platzhalter …).
    /// Vorrang: Kürzel einer Gruppe, deren Schlüsselwort auch im Langnamen steht (bzw. ohne Langnamen); dann das längste passende Schlüsselwort des Langnamens
    /// (z. B. „F“ = Fotografie oder Französisch je nach Langname); dann das Kürzel; sonst eine allgemeine Gruppe.
    /// </summary>
    public static Fachgruppe? Zuordnung(string kurz, string? langname)
    {
        var k = Katalog;
        var name = (langname ?? "").Trim().ToLowerInvariant();
        if (k.NichtKuerzel.Contains(kurz, Ci) || (name.Length > 0 && k.NichtWorte.Any(name.Contains))) return null;
        var perKuerzel = k.Gruppen.FirstOrDefault(g => g.Kuerzel.Contains(kurz, Ci));
        if (perKuerzel is not null && (name.Length == 0 || Ci.Equals(name, kurz) || perKuerzel.Worte.Any(name.Contains))) return perKuerzel;
        var perName = name.Length == 0 ? null : k.Gruppen.SelectMany(g => g.Worte.Where(name.Contains).Select(wort => (g, wort.Length)))
            .OrderByDescending(x => x.Length).Select(x => x.g).FirstOrDefault();
        return perName ?? perKuerzel ?? Allgemein(langname is { Length: > 0 } ? langname : kurz);
    }

    /// <summary>Allgemeine Reihe für Fächer ohne eigene Gruppe (Fachname als Titel).</summary>
    private static Fachgruppe Allgemein(string fach) => new("allgemein", new(), new(), new()
    {
        ["alle"] = new()
        {
            $"{fach}: Grundlagen|Einführung und Überblick|Grundbegriffe|Erarbeitung mit Arbeitsblatt|Übungsaufgaben|Ergebnissicherung",
            $"{fach}: Anwendung|Fallbeispiel aus der Praxis|Gruppenarbeit|Präsentation der Ergebnisse|Reflexion",
            $"{fach}: Vertiefung|Fachtexte erschließen|Aufgaben in Partnerarbeit|Lernkontrolle|Besprechung",
        },
    });

    /// <summary>Stufe einer Klasse: sek1u (5, 6), sek1o (7 bis 10), sek2 (EF, Q1, Q2) bzw. bk (Bildungsgänge des Berufskollegs).</summary>
    public static string Stufe(Stand s, string klasse)
    {
        if (Oberstufe(s, klasse)) return "sek2";
        var j = s.Klassen.TryGetValue(klasse, out var k) ? k.Jahrgang : "";
        if (!int.TryParse(j, out var n)) n = klasse.Length > 0 && char.IsDigit(klasse[0]) && int.TryParse(new string(klasse.TakeWhile(char.IsDigit).ToArray()), out var m) && m <= 10 ? m : 0;
        return n is >= 5 and <= 6 ? "sek1u" : n is >= 7 and <= 10 ? "sek1o" : "bk";
    }

    private static List<string> ReihenFuer(Fachgruppe g, string stufe)
    {
        foreach (var st in stufe switch
                 {
                     "sek1u" => new[] { "sek1u", "sek1o", "alle", "bk", "sek2" },
                     "sek1o" => new[] { "sek1o", "sek1u", "alle", "bk", "sek2" },
                     "sek2" => new[] { "sek2", "bk", "alle", "sek1o" },
                     _ => new[] { "bk", "alle", "sek2", "sek1o", "sek1u" },
                 })
            if (g.Stufen.TryGetValue(st, out var l) && l.Count > 0) return l;
        return g.Stufen.Values.FirstOrDefault(l => l.Count > 0) ?? new();
    }

    // ------------------------------------------------------------------------------------------ Fortschritt je Lerngruppe

    private readonly Dictionary<string, List<DateOnly>> _schuljahrTage = new();
    private readonly Dictionary<(string, int), List<(DateOnly Tag, int Von)>> _bloecke = new();

    /// <summary>Schultage des Schuljahres (Ferien und freie Tage ausgenommen).</summary>
    private List<DateOnly> Schuljahrestage(Stand s)
    {
        lock (_schuljahrTage)
        {
            if (_schuljahrTage.TryGetValue(s.Empfangen, out var t)) return t;
            if (_schuljahrTage.Count > 4) { _schuljahrTage.Clear(); _bloecke.Clear(); }
            var (von, bis) = (Halbjahre(s)[0].Von, Halbjahre(s)[^1].Bis);
            t = new List<DateOnly>();
            for (var d = von; d <= bis; d = d.AddDays(1)) if (!_plan.Frei(s, d).Frei) t.Add(d);
            return _schuljahrTage[s.Empfangen] = t;
        }
    }

    /// <summary>
    /// Stunden (Blöcke: aufeinanderfolgende Stunden zählen als eine) des Unterrichts im Schuljahr laut Periodenstundenplan, in zeitlicher Reihenfolge.
    /// Der Platz einer Stunde in dieser Liste ist der Fortschritt der Lerngruppe: Die Reihe läuft so über das ganze Jahr weiter, auch nach einem Neustart.
    /// </summary>
    public List<(DateOnly Tag, int Von)> Bloecke(Stand s, int unterricht)
    {
        var tage = Schuljahrestage(s);
        lock (_schuljahrTage)
        {
            if (_bloecke.TryGetValue((s.Empfangen, unterricht), out var b)) return b;
            b = new();
            var perioden = s.Paket.Perioden.Select(p => (V: DateOnly.TryParse(p.Von, out var v) ? v : DateOnly.MinValue, B: DateOnly.TryParse(p.Bis, out var bi) ? bi : DateOnly.MaxValue,
                    Plan: p.Stundenplan.Where(x => x.Unterricht == unterricht).GroupBy(x => x.Tag).ToDictionary(g => g.Key, g => g.Select(x => x.Stunde).Distinct().Order().ToList())))
                .OrderByDescending(p => p.V).ToList();
            foreach (var d in tage)
            {
                var p = perioden.FirstOrDefault(x => x.V <= d && d <= x.B);
                if (p.Plan is null || !p.Plan.TryGetValue(Plandienst.TagIndex(d), out var stunden)) continue;
                foreach (var n in stunden.Where(n => !stunden.Contains(n - 1))) b.Add((d, n));
            }
            return _bloecke[(s.Empfangen, unterricht)] = b;
        }
    }

    /// <summary>
    /// Lehrstoff der Stunde: fortlaufende Unterrichtsreihen des Fachs für die Stufe über das Schuljahr (je Klasse an anderer Stelle beginnend), auf die Zahl
    /// der Stunden verteilt (zusätzliche Stunden üben und vertiefen das Thema), mit Einstieg zu Schuljahresbeginn, Wiederholung nach den Ferien sowie
    /// Wiederholung, Klassenarbeit bzw. Klausur und Rückgabe zu den Prüfungsterminen. null = kein Unterrichtsfach.
    /// </summary>
    public string? LehrstoffFuer(Stand s, Weltstand w, Simulationseinstellungen e, Karte k, DateOnly d) =>
        k.Unterricht is int u ? Lehrstoff(s, w, e, k.Fach, k.Klassen.FirstOrDefault()?.K ?? "", u, d, k.Von) : null;

    /// <summary>Lehrstoff einer Stunde des Unterrichts (Fach, erste Klasse, Datum, erste Stunde des Blocks).</summary>
    public string? Lehrstoff(Stand s, Weltstand w, Simulationseinstellungen e, string fach, string klasse, int u, DateOnly d, int von)
    {
        var fachName = s.Faecher.TryGetValue(fach, out var f) ? f.Name : fach;
        var g = Zuordnung(fach, fachName);
        if (g is null) return null;
        var k = (Fach: fach, Von: von);
        var reihen = ReihenFuer(g, Stufe(s, klasse)).Select(r => r.Split('|')).Where(r => r.Length >= 2).ToList();
        if (reihen.Count == 0) return null;
        var bloecke = Bloecke(s, u);
        var i = bloecke.IndexOf((d, k.Von));
        if (i < 0) i = bloecke.Count(x => x.Tag < d || (x.Tag == d && x.Von < k.Von));
        var z = Zufall.Fuer(w.Seed, "lsr", u, i);
        string Fertig(string t) => z.Zahlen(t);
        if (g.Zyklisch)
        {
            var themen = reihen.SelectMany(r => r.Skip(1)).ToList();
            return Fertig(i == 0 ? "Organisatorisches zum Schuljahresbeginn" : themen[i % themen.Count]);
        }
        // fortlaufende Liste: Reihen ab einer Startreihe je Klasse, nach längeren Reihen eine Übungsstunde
        var start = (int)(Zufall.Fuer(w.Seed, "reihe", klasse, fach).Naechste() % (ulong)reihen.Count);
        var liste = new List<(string Titel, string Stunde)>();
        for (var r = 0; r < reihen.Count; r++)
        {
            var reihe = reihen[(start + r) % reihen.Count];
            liste.AddRange(reihe.Skip(1).Select(x => (reihe[0], x)));
            if (reihe.Length >= 6) liste.Add((reihe[0], "Übungen und Wiederholung"));
        }
        var gesamt = Math.Max(bloecke.Count, 1);
        var pos = Math.Min(liste.Count - 1, (int)((long)Math.Max(0, i) * liste.Count / gesamt));
        var (titel, stunde) = liste[pos];
        // Prüfung: Stunde davor Wiederholung, am Termin die Prüfung, gut eine Woche später Rückgabe
        if (e.Pruefungen)
        {
            var art = Oberstufe(s, klasse) ? "Klausur" : "Klassenarbeit";
            foreach (var p in PruefungenDerKlasse(s, e, w, klasse, k.Fach))
            {
                var pi = bloecke.IndexOf((p.Datum, p.Von));
                if (pi < 0) continue;
                var proWoche = Math.Max(1, bloecke.Count(x => Plandienst.Montag(x.Tag) == Plandienst.Montag(p.Datum)));
                if (i == pi) return $"{art}: {titel}";
                if (i == pi - 1) return $"{titel}: Wiederholung zur {art}";
                if (i == pi + Math.Max(2, proWoche + 1)) return $"Rückgabe und Besprechung der {art}";
            }
        }
        if (i == 0) return "Einstieg ins Schuljahr: Organisatorisches, Leistungsbewertung, " + titel;
        if (i > 0 && i < bloecke.Count && bloecke[i].Tag.DayNumber - bloecke[i - 1].Tag.DayNumber > 10) return Fertig($"{titel}: Wiederholung nach den Ferien");
        // mehrere Stunden je Eintrag (mehr Stunden als Einträge): Fortsetzung, Übung, Sicherung
        var erste = (int)Math.Ceiling((double)pos * gesamt / liste.Count);
        var teil = Math.Max(0, i - erste);
        // Übungs- und Wiederholungsstunden nicht mit „Übungen“ verlängern (keine doppelten Angaben)
        var uebung = stunde.Contains("Übung", StringComparison.OrdinalIgnoreCase) || stunde.Contains("Wiederholung", StringComparison.OrdinalIgnoreCase);
        var text = teil switch
        {
            0 => $"{titel}: {stunde}",
            1 => uebung ? $"{titel}: Anwendungsaufgaben" : $"{titel}: Fortsetzung {stunde}",
            2 => uebung ? $"{titel}: Besprechung der Übungen" : $"{titel}: {stunde}, Übungen S. {{s}}",
            3 => uebung ? $"{titel}: Lernkontrolle" : $"{titel}: {stunde}, Vertiefung in Gruppenarbeit",
            4 => $"{titel}: Anwendungsaufgaben zu {stunde}",
            5 => $"{titel}: Lernkontrolle {stunde}",
            _ => $"{titel}: Übungen S. {{s}} Nr. {{a}} und {{b}}",
        };
        return Fertig(text);
    }

    /// <summary>Geplante Prüfungen der Simulation für Klasse und Fach im Schuljahr.</summary>
    private IEnumerable<Pruefungsplan> PruefungenDerKlasse(Stand s, Simulationseinstellungen e, Weltstand w, string klasse, string fach)
    {
        if (!DateTime.TryParse(e.Start, Inv, System.Globalization.DateTimeStyles.None, out var start)) yield break;
        foreach (var (hv, hb) in Halbjahre(s))
            foreach (var p in Pruefungsplanung(s, e, w, hv, hb, start).Where(p => Ci.Equals(p.Klasse, klasse) && Ci.Equals(p.Fach, fach)))
                yield return p;
    }
}
