using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>
/// „Soll behindernder Unterricht entplant werden?“ (wie im Vorbild): Ermittelt die Stunden, die das Setzen eines Unterrichts bzw. eine
/// Änderung an einem verplanten Unterricht (z. B. zusätzliche Klasse) behindern – gleiche Lehrkraft, gleiche Klasse oder ein fest
/// vorgegebener Raum, der zur selben Zeit belegt ist – und entplant sie auf Wunsch (sie erscheinen dann unter „nicht verplant“).
/// </summary>
public static class Behinderung
{
    /// <summary>
    /// Behindernde Stunden für das Setzen von <paramref name="u"/> an den angegebenen Stellen (Tag, Stunde). Neben Lehrer- und
    /// Klassenkollisionen zählen Räume: ausdrücklich angegebene Räume (<paramref name="raeume"/> je Kopplungszeile) bzw. – ohne
    /// Angabe – ein fester Raum ohne Ausweichmöglichkeit, der zu der Zeit von anderem Unterricht belegt ist.
    /// </summary>
    public static List<Hindernis> AnStellen(Schuldatei d, DatenIndex ix, Unterricht u, IEnumerable<(int Tag, int Stunde)> stellen,
        ICollection<Planstunde>? ignorieren = null, IReadOnlyList<int?>? raeume = null)
    {
        var res = new List<Hindernis>();
        var gesehen = new HashSet<Planstunde>(ReferenceEqualityComparer.Instance);
        foreach (var (tag, stunde) in stellen.Distinct())
        {
            foreach (var h in Planungsdienst.Hindernisse(d, ix, u, tag, stunde, ignorieren))
                if (gesehen.Add(h.Planstunde)) res.Add(h);
            foreach (var h in RaumHindernisse(d, ix, u, tag, stunde, ignorieren, raeume))
                if (gesehen.Add(h.Planstunde)) res.Add(h);
        }
        return res;
    }

    /// <summary>Stunden, die einen benötigten Raum zur selben Zeit belegen (fester Raum ohne Ausweichraum bzw. angegebene Räume).</summary>
    public static List<Hindernis> RaumHindernisse(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, ICollection<Planstunde>? ignorieren = null,
        IReadOnlyList<int?>? raeume = null)
    {
        var benoetigt = new HashSet<int>();
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            if (raeume != null)
            {
                if (i < raeume.Count && raeume[i] is int r) benoetigt.Add(r);
                continue;
            }
            var kandidaten = ix.RaumKandidaten(u.Zeilen[i]);
            if (kandidaten.Count == 1) benoetigt.Add(kandidaten[0]);
        }
        var res = new List<Hindernis>();
        if (benoetigt.Count == 0) return res;
        // ohne angegebene Räume: nur behindernd, wenn der feste Raum nicht anderweitig zu ersetzen ist (alle Kandidaten belegt)
        foreach (var p in d.Stundenplan)
        {
            if (p.Tag != tag || p.Stunde != stunde || p.UnterrichtId == u.Id) continue;
            if (ignorieren != null && ignorieren.Contains(p)) continue;
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o)) continue;
            if (!string.IsNullOrWhiteSpace(u.Band) && u.Band == o.Band) continue;
            if (!Kalender.GleicheWoche(d, u, o)) continue;
            var gemeinsam = p.Raeume.Where(r => r is int x && benoetigt.Contains(x)).Select(r => r!.Value).Distinct().ToList();
            if (gemeinsam.Count > 0) res.Add(new Hindernis(p, o, $"Raum {string.Join(",", gemeinsam.Select(r => ix.KRaum(r)))}"));
        }
        return res;
    }

    /// <summary>
    /// Neue Behinderungen durch eine Änderung an einem (verplanten) Unterricht: z. B. wird eine Klasse oder Lehrkraft hinzugefügt,
    /// die zu den verplanten Zeiten schon Unterricht hat. <paramref name="alt"/> und <paramref name="neu"/> haben dieselbe U-Nr.;
    /// Kollisionen, die schon vorher bestanden, werden nicht noch einmal gemeldet.
    /// </summary>
    public static List<Hindernis> DurchAenderung(Schuldatei d, DatenIndex ix, Unterricht alt, Unterricht neu)
    {
        var res = new List<Hindernis>();
        var gesehen = new HashSet<Planstunde>(ReferenceEqualityComparer.Instance);
        foreach (var p in d.Stundenplan.Where(p => p.UnterrichtId == alt.Id).OrderBy(p => p.Tag).ThenBy(p => p.Stunde).ToList())
        {
            var vorher = Planungsdienst.Hindernisse(d, ix, alt, p.Tag, p.Stunde).Select(h => h.Planstunde).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var h in Planungsdienst.Hindernisse(d, ix, neu, p.Tag, p.Stunde))
                if (!vorher.Contains(h.Planstunde) && gesehen.Add(h.Planstunde)) res.Add(h);
        }
        return res;
    }

    /// <summary>Behindernde Stunden entplanen (sie zählen danach wieder als „nicht verplant“). Liefert die Anzahl.</summary>
    public static int Entplanen(Schuldatei d, IEnumerable<Hindernis> hindernisse)
    {
        var n = 0;
        foreach (var h in hindernisse.Select(h => h.Planstunde).Distinct(ReferenceEqualityComparer.Instance).Cast<Planstunde>().ToList())
            if (d.Stundenplan.Remove(h)) n++;
        return n;
    }

    /// <summary>Frage mit der Liste der behindernden Stunden (Tag-Stunde: U-Nr. Text (Grund)).</summary>
    public static string Fragetext(Schuldatei d, DatenIndex ix, IReadOnlyCollection<Hindernis> hindernisse, string? anlass = null)
    {
        var zeilen = hindernisse.OrderBy(h => h.Planstunde.Tag).ThenBy(h => h.Planstunde.Stunde).Take(20)
            .Select(h => $"  {Planungsdienst.Tagname(d, h.Planstunde.Tag)}-{h.Planstunde.Stunde}: U-Nr. {h.Unterricht.Id} {ix.UnterrichtText(h.Unterricht)} ({h.Grund})");
        return (anlass is null ? "" : anlass + "\n\n") + $"Behindernde Stunden ({hindernisse.Count}):\n" + string.Join("\n", zeilen) +
               (hindernisse.Count > 20 ? "\n  …" : "") + "\n\nSoll behindernder Unterricht entplant werden?";
    }
}
