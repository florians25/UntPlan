using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>Was in einer Stunde einer Elementzeile des Planungsdialogs angezeigt wird (Einstellungen „Was wird in einer Stunde angezeigt?“).</summary>
public enum StundenAnzeige { Fach, Klasse, Lehrer, Raum }

/// <summary>Inhalt einer Zelle in einer Elementzeile des Planungsdialogs.</summary>
/// <param name="Text">angezeigter Text (Fach, Klasse …) bzw. Zeitwunsch („-3“) einer freien Stunde</param>
/// <param name="Stunde">verplante Stunde an dieser Stelle (null = frei)</param>
/// <param name="Unterricht">Unterricht der Stunde</param>
/// <param name="Zeitwunsch">Zeitwunsch des Elements an dieser Stelle</param>
/// <param name="Eigene">die Stunde gehört zum aktiven Unterricht</param>
public sealed record Planungszelle(string Text, Planstunde? Stunde, Unterricht? Unterricht, int Zeitwunsch, bool Eigene)
{
    public bool Frei => Stunde is null;
}

/// <summary>
/// Planungsbereich des Planungsdialogs (Handbuch Stundenplan Kap. 5.3/5.4): Stunden-Merker der Summenzeilen, Inhalt der
/// Elementzeilen, freie Räume („1/5“) und Zeitwünsche – jeweils aus Sicht des aktiven Unterrichts.
/// </summary>
public static class Planungsbereich
{
    /// <summary>Gleicher Zeitpunkt, anderer Unterricht, nicht dasselbe Band und nicht A-/B-Woche-getrennt.</summary>
    private static IEnumerable<(Planstunde P, Unterricht U)> Andere(Schuldatei d, DatenIndex ix, Unterricht aktiv, int tag, int stunde)
    {
        foreach (var p in d.Stundenplan)
        {
            if (p.Tag != tag || p.Stunde != stunde || p.UnterrichtId == aktiv.Id) continue;
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o)) continue;
            if (!string.IsNullOrWhiteSpace(aktiv.Band) && aktiv.Band == o.Band) continue;
            if (!Kalender.GleicheWoche(d, aktiv, o)) continue;
            yield return (p, o);
        }
    }

    /// <summary>Elemente eines Unterrichts bzw. einer Stunde (für Räume: die Räume der Stunde).</summary>
    public static IEnumerable<int> ElementeVon(ElementTyp typ, Unterricht u, Planstunde? p) => typ switch
    {
        ElementTyp.Klasse => u.KlassenIds,
        ElementTyp.Lehrer => u.LehrerIds,
        ElementTyp.Raum => p?.Raeume.Where(r => r.HasValue).Select(r => r!.Value).Distinct() ?? Enumerable.Empty<int>(),
        ElementTyp.Fach => u.FachIds,
        _ => Enumerable.Empty<int>(),
    };

    /// <summary>Zeitwunsch eines Elements (Klasse, Lehrer, Raum, Fach) an einer Stelle.</summary>
    public static int Zeitwunsch(DatenIndex ix, ElementTyp typ, int id, int tag, int stunde) =>
        ix.Element(typ, id)?.Zeitwuensche.Get(tag, stunde) ?? 0;

    /// <summary>Zeitwunsch einer Gruppe von Elementen: der stärkste negative Wunsch, sonst der größte positive.</summary>
    public static int Zeitwunsch(DatenIndex ix, ElementTyp typ, IEnumerable<int> ids, int tag, int stunde)
    {
        var w = ids.Select(i => Zeitwunsch(ix, typ, i, tag, stunde)).ToList();
        if (w.Count == 0) return 0;
        var min = w.Min();
        return min < 0 ? min : w.Max();
    }

    /// <summary>Zeitwunsch als Text („-3“, „+2“, leer bei 0).</summary>
    public static string WunschText(int w) => w == 0 ? "" : w > 0 ? "+" + w : w.ToString();

    /// <summary>
    /// Stunden-Merker einer Summenzeile (Handbuch Kap. 5.3.1): „O“ der aktive Unterricht selbst, „x“ ein Element ist belegt,
    /// „X“ Kopplung, an der das Element beteiligt ist, „&amp;“ mehrere Elemente sind belegt, „*x*“ usw. behindernde Stunde fixiert,
    /// sonst der Zeitwunsch (z. B. „-3“) bzw. leer.
    /// </summary>
    public static string Merker(Schuldatei d, DatenIndex ix, Unterricht aktiv, ElementTyp typ, IReadOnlyCollection<int> elemente, int tag, int stunde)
    {
        if (d.Stundenplan.Any(p => p.UnterrichtId == aktiv.Id && p.Tag == tag && p.Stunde == stunde)) return "O";
        var menge = elemente.ToHashSet();
        var belegt = new HashSet<int>();
        var stunden = new List<(Planstunde P, Unterricht U)>();
        foreach (var (p, o) in Andere(d, ix, aktiv, tag, stunde))
        {
            var gemeinsam = ElementeVon(typ, o, p).Where(menge.Contains).ToList();
            if (gemeinsam.Count == 0) continue;
            foreach (var g in gemeinsam) belegt.Add(g);
            stunden.Add((p, o));
        }
        if (stunden.Count == 0) return WunschText(Zeitwunsch(ix, typ, elemente, tag, stunde));
        var m = belegt.Count > 1 || stunden.Select(x => x.U.Id).Distinct().Count() > 1 ? "&" : stunden[0].U.Zeilen.Count > 1 ? "X" : "x";
        return stunden.Any(x => x.P.Fixiert) ? $"*{m}*" : m;
    }

    /// <summary>
    /// Inhalt der Zeile eines einzelnen Elements an einer Stelle: belegt → Fach/Klasse/Lehrer/Raum laut <paramref name="anzeige"/>
    /// (fixierte Stunden mit „*“), frei → Zeitwunsch des Elements.
    /// </summary>
    public static Planungszelle Zelle(Schuldatei d, DatenIndex ix, Unterricht aktiv, ElementTyp typ, int id, int tag, int stunde, StundenAnzeige anzeige)
    {
        var wunsch = Zeitwunsch(ix, typ, id, tag, stunde);
        foreach (var p in d.Stundenplan)
        {
            if (p.Tag != tag || p.Stunde != stunde || !ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o)) continue;
            if (!ElementeVon(typ, o, p).Contains(id)) continue;
            if (o.Id != aktiv.Id && !Kalender.GleicheWoche(d, aktiv, o)) continue;
            var text = Text(ix, o, p, typ, id, anzeige);
            if (p.Fixiert) text = "*" + text;
            return new Planungszelle(text, p, o, wunsch, o.Id == aktiv.Id);
        }
        return new Planungszelle(WunschText(wunsch), null, null, wunsch, false);
    }

    /// <summary>Text einer Stunde aus Sicht eines Elements (nur die Kopplungszeilen dieses Elements).</summary>
    public static string Text(DatenIndex ix, Unterricht u, Planstunde p, ElementTyp typ, int id, StundenAnzeige anzeige)
    {
        var zeilen = u.Zeilen.Select((z, i) => (z, i)).Where(x => typ switch
        {
            ElementTyp.Klasse => x.z.KlassenIds.Contains(id),
            ElementTyp.Lehrer => x.z.LehrerId == id,
            ElementTyp.Raum => p.RaumFuerZeile(x.i) == id,
            _ => true,
        }).ToList();
        if (zeilen.Count == 0) zeilen = u.Zeilen.Select((z, i) => (z, i)).ToList();
        IEnumerable<string> teile = anzeige switch
        {
            StundenAnzeige.Fach => zeilen.Select(x => ix.KFach(x.z.FachId)),
            StundenAnzeige.Klasse => new[] { ix.KKlassen(zeilen.SelectMany(x => x.z.KlassenIds).Distinct()) },
            StundenAnzeige.Lehrer => zeilen.Select(x => ix.KLehrer(x.z.LehrerId)),
            _ => zeilen.Select(x => ix.KRaum(p.RaumFuerZeile(x.i))),
        };
        return string.Join(",", teile.Where(t => t.Length > 0).Distinct());
    }

    /// <summary>Räume, die für den aktiven Unterricht in Frage kommen (gewünschte Räume und Ausweichräume aller Kopplungszeilen).</summary>
    public static List<int> Raumkandidaten(DatenIndex ix, Unterricht u) =>
        u.Zeilen.SelectMany(ix.RaumKandidaten).Distinct().ToList();

    /// <summary>
    /// Summenzeile der Räume: wie viele der möglichen Räume (Wunschraum und Ausweichräume) zu diesem Zeitpunkt frei wären
    /// („1/5“ = 1 von 5 Räumen frei). Die Räume des aktiven Unterrichts selbst gelten als frei.
    /// </summary>
    public static (int Frei, int Gesamt) RaeumeFrei(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde)
    {
        var kand = Raumkandidaten(ix, u);
        if (kand.Count == 0) return (0, 0);
        var belegt = new HashSet<int>();
        foreach (var p in d.Stundenplan)
            if (p.Tag == tag && p.Stunde == stunde && p.UnterrichtId != u.Id && ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o) && Kalender.GleicheWoche(d, u, o))
                foreach (var r in p.Raeume) if (r.HasValue) belegt.Add(r.Value);
        var frei = kand.Count(r => !belegt.Contains(r) && !(ix.Raeume.TryGetValue(r, out var raum) && raum.Zeitwuensche.Gesperrt(tag, stunde)));
        return (frei, kand.Count);
    }

    /// <summary>
    /// Unterrichte, die eine kollisionsfreie Verplanung des aktiven Unterrichts an dieser Stelle verhindern (Lupe des Planungsdialogs):
    /// Lehrer-, Klassen- und Raumbelegung.
    /// </summary>
    public static List<Hindernis> Verhindernde(Schuldatei d, DatenIndex ix, Unterricht aktiv, int tag, int stunde)
    {
        var res = Planungsdienst.Hindernisse(d, ix, aktiv, tag, stunde);
        foreach (var h in Behinderung.RaumHindernisse(d, ix, aktiv, tag, stunde))
            if (!res.Any(x => ReferenceEquals(x.Planstunde, h.Planstunde))) res.Add(h);
        return res;
    }

    /// <summary>
    /// Suche im Auswahlfeld „Unterricht“: Unterrichtsnummer oder Teil eines Kurznamens von Klasse, Lehrer bzw. Fach
    /// („1a“ findet alle Unterrichte der 1a, „D“ alle Deutsch-Unterrichte und alle weiteren mit „D“ in einem Element).
    /// </summary>
    public static List<Unterricht> Suchen(Schuldatei d, DatenIndex ix, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return new();
        if (int.TryParse(text, out var nr) && ix.Unterrichte.TryGetValue(nr, out var genau)) return new() { genau };
        bool Passt(string k) => k.Contains(text, StringComparison.OrdinalIgnoreCase);
        return d.Unterrichte.Where(u => !u.Ignorieren && (u.KlassenIds.Any(k => Passt(ix.K(ElementTyp.Klasse, k))) || u.LehrerIds.Any(l => Passt(ix.KLehrer(l))) ||
                                                          u.FachIds.Any(f => Passt(ix.KFach(f))))).OrderBy(u => u.Id).ToList();
    }
}
