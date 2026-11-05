using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.Core.Planung;

/// <summary>
/// Bewertet, wie gut eine Stunde eines Unterrichts an eine Position passt (Planungsdialog: Rangziffern &lt;!1!&gt;, Tauschketten,
/// „Bewertung zur aktuellen Zeit“). Grundlage sind die Gewichtungen der Optimierung: Es werden nur die Kosten der beteiligten
/// Lehrer und Klassen, des Unterrichts und der Stunde selbst neu berechnet – je kleiner, desto besser.
/// </summary>
public sealed class Platzbewerter
{
    private readonly PlanModell _m;
    private int _bi = -1;
    private int _ui = -1;
    private readonly Dictionary<Planstunde, int> _bloecke = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Bewerter für eine Stunde von <paramref name="u"/>: <paramref name="bewegt"/> = die verplante Stunde, die verschoben wird
    /// (null = eine noch nicht verplante Stunde wird gesetzt).
    /// </summary>
    public Platzbewerter(Schuldatei d, Unterricht u, Planstunde? bewegt = null)
    {
        _m = Bewertung.Modell(d, d.Stundenplan);
        for (var i = 0; i < _m.Bloecke.Count; i++)
            if (_m.Bloecke[i].Quelle is { } q) _bloecke[q] = i;
        Waehlen(u, bewegt);
    }

    /// <summary>Andere Stunde bewerten (das Modell des Plans wird wiederverwendet).</summary>
    public void Waehlen(Unterricht u, Planstunde? bewegt)
    {
        _ui = _m.UnterrichtIdx(u.Id);
        _bi = -1;
        if (_ui < 0) return;
        if (bewegt != null && _bloecke.TryGetValue(bewegt, out var b)) _bi = b;
        else _bi = _m.NeuerBlock(new[] { _ui }, 1, true);
    }

    /// <summary>Kann der Unterricht überhaupt bewertet werden (Unterricht mit Lehrer bzw. Klasse, nicht ignoriert)?</summary>
    public bool Gueltig => _bi >= 0;

    /// <summary>
    /// Kosten der Stunde an (<paramref name="tag"/>, <paramref name="stunde"/>). <paramref name="ohne"/>: Stunden, die dabei als
    /// entplant gelten (z. B. eine verdrängte Stunde in einer Tauschkette).
    /// </summary>
    public double Kosten(int tag, int stunde, IReadOnlyCollection<Planstunde>? ohne = null)
    {
        if (_bi < 0 || tag < 0 || tag >= _m.T || stunde < 1 || stunde > _m.S) return double.PositiveInfinity;
        var weg = new List<(int Bi, int Tag, int Stunde)>();
        if (ohne != null)
            foreach (var p in ohne)
                if (_bloecke.TryGetValue(p, out var bi) && bi != _bi && _m.Bloecke[bi].Platziert)
                {
                    weg.Add((bi, _m.Bloecke[bi].Tag, _m.Bloecke[bi].Stunde));
                    _m.Setzen(bi, -1);
                    _m.Bloecke[bi].Tag = -1;
                    _m.Bloecke[bi].Stunde = -1;
                }
        var altTag = _m.Bloecke[_bi].Tag;
        var altStunde = _m.Bloecke[_bi].Stunde;
        _m.Platzieren(_bi, tag, stunde - 1);
        double k = _m.BlockKosten(_bi) + _m.PoolKostenBlock(_bi) + _m.UnterrichtKosten(_ui);
        foreach (var e in _m.Bloecke[_bi].Entitaeten.Distinct()) k += _m.EntitaetKosten(e);
        // Zustand wiederherstellen
        if (altTag >= 0) _m.Platzieren(_bi, altTag, altStunde);
        else { _m.Setzen(_bi, -1); _m.Bloecke[_bi].Tag = -1; _m.Bloecke[_bi].Stunde = -1; }
        foreach (var (bi, t, s) in weg)
        {
            _m.Bloecke[bi].Tag = t;
            _m.Bloecke[bi].Stunde = s;
            _m.Setzen(bi, +1);
        }
        return k;
    }

    /// <summary>Kosten an der bisherigen Position der bewegten Stunde (∞ für eine noch nicht verplante Stunde).</summary>
    public double KostenJetzt => _bi >= 0 && _m.Bloecke[_bi].Platziert ? Kosten(_m.Bloecke[_bi].Tag, _m.Bloecke[_bi].Stunde + 1) : double.PositiveInfinity;
}

/// <summary>Rangziffern und Bewertungen des Planungsdialogs.</summary>
public static class Platzbewertung
{
    /// <summary>
    /// Rangziffern &lt;!1!&gt;, &lt;!2!&gt; … der freien Plätze für eine Stunde des aktiven Unterrichts: 1 = der Platz, an den die Stunde
    /// am besten passt. Frei heißt: kollisionsfrei, Zeitraster verfügbar, keine Sperrung. Ist der Unterricht vollständig verplant,
    /// wird die Stunde <paramref name="bewegt"/> (bzw. die erste verplante Stunde) gedanklich verschoben.
    /// </summary>
    public static Dictionary<(int Tag, int Stunde), int> Raenge(Schuldatei d, DatenIndex ix, Unterricht u, Planstunde? bewegt = null)
    {
        var res = new Dictionary<(int, int), int>();
        var eigene = d.Stundenplan.Where(p => p.UnterrichtId == u.Id).ToList();
        if (ix.NichtVerplant(u) > 0) bewegt = null;
        else bewegt ??= eigene.OrderBy(p => p.Tag).ThenBy(p => p.Stunde).FirstOrDefault();
        if (bewegt is null && ix.NichtVerplant(u) == 0) return res;
        var bew = new Platzbewerter(d, u, bewegt);
        if (!bew.Gueltig) return res;
        var ignorieren = bewegt is null ? null : new[] { bewegt };
        var kand = new List<(int Tag, int Stunde, double Kosten)>();
        for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
            for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
            {
                if (eigene.Any(p => p.Tag == t && p.Stunde == s)) continue;
                if (!d.Zeitraster.Verfuegbar(t, s)) continue;
                var st = Planungsdienst.Status(d, ix, u, t, s, 1, ignorieren);
                if (st is not (Feldstatus.Frei or Feldstatus.KeinRaum)) continue;
                kand.Add((t, s, bew.Kosten(t, s)));
            }
        var rang = 1;
        foreach (var k in kand.OrderBy(k => k.Kosten).ThenBy(k => k.Tag).ThenBy(k => k.Stunde)) res[(k.Tag, k.Stunde)] = rang++;
        return res;
    }

    /// <summary>Ergebnis der „Bewertung zur aktuellen Zeit“: eine Stunde, die an die gesuchte Position verlegt werden könnte.</summary>
    public sealed record Zeitbewertung(Planstunde Stunde, int Rang, double Differenz);

    /// <summary>
    /// „Bewertung zur aktuellen Zeit“: Für eine noch freie Position (<paramref name="tag"/>, <paramref name="stunde"/>) einer Klasse
    /// werden alle Stunden der Woche dieser Klasse bewertet, wie gut sie sich für eine Verlegung dorthin eignen (z. B. um eine
    /// Hohlstunde zu füllen). Nur kollisionsfreie, nicht fixierte Stunden; je kleiner der Rang, desto geeigneter.
    /// </summary>
    public static List<Zeitbewertung> ZurZeit(Schuldatei d, DatenIndex ix, int klasseId, int tag, int stunde)
    {
        var res = new List<(Planstunde P, double Diff)>();
        if (!d.Zeitraster.Verfuegbar(tag, stunde)) return new();
        Platzbewerter? bew = null;
        foreach (var p in d.Stundenplan.ToList())
        {
            if (p.Fixiert || (p.Tag == tag && p.Stunde == stunde)) continue;
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) || !u.KlassenIds.Contains(klasseId)) continue;
            if (d.Stundenplan.Any(x => x.UnterrichtId == u.Id && x.Tag == tag && x.Stunde == stunde)) continue;
            if (Planungsdienst.Hindernisse(d, ix, u, tag, stunde, new[] { p }).Count > 0) continue;
            if (Planungsdienst.VorgabeVerletzt(d, ix, u, tag, stunde)) continue;
            if (bew is null) bew = new Platzbewerter(d, u, p);
            else bew.Waehlen(u, p);
            if (!bew.Gueltig) continue;
            res.Add((p, bew.Kosten(tag, stunde) - bew.KostenJetzt));
        }
        var rang = 1;
        return res.OrderBy(x => x.Diff).ThenBy(x => x.P.Tag).ThenBy(x => x.P.Stunde).Select(x => new Zeitbewertung(x.P, rang++, x.Diff)).ToList();
    }
}
