using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Vertretungs-Diagnose: weicht der Stundenplan eines Vertreters durch die zugewiesenen Vertretungen von den Stammdaten ab
/// (Lehrerkollision, Raumkollision, zu viele Stunden pro Tag, gesperrte Zeit)?
/// </summary>
public static class Vertretungsdiagnose
{
    public sealed record Befund(Vertretung Vertretung, string Art);

    public static List<Befund> Pruefen(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var res = new List<Befund>();
        using var _ = DatenIndex.Gemeinsam(d);
        var ix = DatenIndex.Fuer(d);
        foreach (var tag in d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).GroupBy(v => v.Datum).OrderBy(g => g.Key))
        {
            var tp = Tagesplan.Berechnen(d, tag.Key);
            foreach (var v in tag.Where(v => !v.IstEntfall).OrderBy(v => v.Stunde).ThenBy(v => v.Id))
            {
                if (v.VertreterId is int vid && ix.Lehrer.TryGetValue(vid, out var l))
                {
                    var gleichzeitig = tp.VonLehrer(vid).Where(e => e.Stunde == v.Stunde).ToList();
                    var unterrichte = gleichzeitig.Select(e => e.Vertretung is { Kopplung: > 0 } k ? -k.Kopplung : e.Unterricht?.Id ?? e.Vertretung?.Id ?? 0).Distinct().Count();
                    if (unterrichte > 1) res.Add(new Befund(v, "Lehrerkollision"));
                    var stunden = tp.VonLehrer(vid).Select(e => e.Stunde).Distinct().Count();
                    if (l.StundenProTag.Max > 0 && stunden > l.StundenProTag.Max) res.Add(new Befund(v, "Stunden pro Tag"));
                    if (Folge(tp.VonLehrer(vid).Select(e => e.Stunde).Distinct().OrderBy(s => s).ToList(), v.Stunde) > Math.Max(6, d.Zeitraster.LetzteVormittagsstunde))
                        res.Add(new Befund(v, "Stundenfolge"));
                    if (tp.Tag >= 0 && l.Zeitwuensche.Gesperrt(tp.Tag, v.Stunde)) res.Add(new Befund(v, "Zeitwunsch"));
                }
                if ((v.VertretungsraumId ?? v.RaumId) is int raum && v.Art != VertretungsArt.Pausenaufsicht)
                {
                    var belegt = tp.Eintraege.Where(e => e.Findetstatt && e.Stunde == v.Stunde && e.RaumIds.Contains(raum))
                        .Select(e => e.Vertretung is { Kopplung: > 0 } k ? -k.Kopplung : e.Unterricht?.Id ?? e.Vertretung?.Id ?? 0).Distinct().Count();
                    if (belegt > 1) res.Add(new Befund(v, "Raumkollision"));
                }
            }
        }
        return res;
    }

    /// <summary>Länge des zusammenhängenden Stundenblocks um eine Stunde.</summary>
    private static int Folge(List<int> stunden, int stunde)
    {
        if (!stunden.Contains(stunde)) return 0;
        var n = 1;
        for (var s = stunde - 1; stunden.Contains(s); s--) n++;
        for (var s = stunde + 1; stunden.Contains(s); s++) n++;
        return n;
    }
}
