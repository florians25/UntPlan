using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Module;

/// <summary>
/// Modul Pausenaufsichten: Aufsichtsbereiche (Gänge), wie oft ein Lehrer pro Woche Aufsicht hat (min/max), Ausnahmen,
/// automatische Verteilung und Vertretung von Pausenaufsichten.
/// </summary>
public static class Pausenaufsichten
{
    public static IEnumerable<Pause> AufsichtsPausen(Schuldatei d) => d.Zeitraster.Pausen.Where(p => p.Aufsicht).OrderBy(p => p.NachStunde);

    private static HashSet<int> StundenAmTag(Schuldatei d, DatenIndex ix, int lehrerId, int tag) =>
        d.Stundenplan.Where(p => p.Tag == tag && ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) && u.LehrerIds.Contains(lehrerId)).Select(p => p.Stunde).ToHashSet();

    /// <summary>Lehrer, die vor oder nach der Pause im Haus sind und keine Sperre haben.</summary>
    public static bool Moeglich(Schuldatei d, DatenIndex ix, Lehrer l, int tag, int nachStunde)
    {
        if (l.KeineAufsicht || l.Ignorieren) return false;
        if (l.Zeitwuensche.Gesperrt(tag, nachStunde) && l.Zeitwuensche.Gesperrt(tag, nachStunde + 1)) return false;
        var std = StundenAmTag(d, ix, l.Id, tag);
        return std.Contains(nachStunde) || std.Contains(nachStunde + 1);
    }

    /// <summary>Verteilt alle Pausenaufsichten neu (Bedarf je Gang, faire Verteilung nach Soll bzw. min/max).</summary>
    public static int Optimieren(Schuldatei d, bool bestehendeLoeschen = true)
    {
        var ix = new DatenIndex(d);
        if (bestehendeLoeschen) d.Pausenaufsichten.Clear();
        var anzahl = d.Lehrer.ToDictionary(l => l.Id, l => d.Pausenaufsichten.Count(a => a.LehrerId == l.Id));
        var neu = 0;
        for (var tag = 0; tag < d.Zeitraster.TageProWoche; tag++)
        {
            foreach (var pause in AufsichtsPausen(d))
            {
                var amTermin = d.Pausenaufsichten.Where(a => a.Tag == tag && a.NachStunde == pause.NachStunde).Select(a => a.LehrerId).ToHashSet();
                foreach (var gang in d.Gaenge)
                {
                    var vorhanden = d.Pausenaufsichten.Count(a => a.Tag == tag && a.NachStunde == pause.NachStunde && a.GangId == gang.Id);
                    for (var n = vorhanden; n < gang.Bedarf; n++)
                    {
                        // Lehrer mit Räumen in diesem Gang bevorzugen
                        var lehrerImGang = d.Stundenplan.Where(p => p.Tag == tag && (p.Stunde == pause.NachStunde || p.Stunde == pause.NachStunde + 1))
                            .Where(p => p.Raeume.Any(r => r.HasValue && ix.Raeume.TryGetValue(r.Value, out var ra) && ra.GangId == gang.Id))
                            .SelectMany(p => ix.Unterrichte[p.UnterrichtId].LehrerIds).ToHashSet();
                        var kand = d.Lehrer.Where(l => !amTermin.Contains(l.Id) && Moeglich(d, ix, l, tag, pause.NachStunde) && (l.AufsichtenMax == 0 || anzahl[l.Id] < l.AufsichtenMax))
                            .OrderByDescending(l => l.AufsichtenMin > anzahl[l.Id])
                            .ThenBy(l => anzahl[l.Id] / (double)Math.Max(1m, l.Soll > 0 ? l.Soll : 1))
                            .ThenByDescending(l => lehrerImGang.Contains(l.Id))
                            .FirstOrDefault();
                        if (kand is null) break;
                        d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang.Id, Tag = tag, NachStunde = pause.NachStunde, LehrerId = kand.Id });
                        amTermin.Add(kand.Id);
                        anzahl[kand.Id]++;
                        neu++;
                    }
                }
            }
        }
        return neu;
    }

    /// <summary>Pausenaufsichten eines Datums, deren Lehrer abwesend ist (noch nicht vertreten).</summary>
    public static List<Pausenaufsicht> OffeneVertretungen(Schuldatei d, DateOnly datum)
    {
        var tag = Kalender.TagIndex(d, datum);
        if (tag < 0 || !Kalender.IstUnterrichtstag(d, datum)) return new();
        return d.Pausenaufsichten.Where(a => a.Tag == tag && Vertretungen.LehrerAbwesend(d, a.LehrerId, datum, a.NachStunde) != null &&
            !d.Vertretungen.Any(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht && v.GangId == a.GangId && v.Stunde == a.NachStunde && v.LehrerId == a.LehrerId)).ToList();
    }

    public static List<Lehrer> Kandidaten(Schuldatei d, DateOnly datum, Pausenaufsicht a)
    {
        var ix = new DatenIndex(d);
        var belegt = d.Pausenaufsichten.Where(x => x.Tag == a.Tag && x.NachStunde == a.NachStunde).Select(x => x.LehrerId).ToHashSet();
        belegt.UnionWith(d.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht && v.Stunde == a.NachStunde && v.VertreterId.HasValue).Select(v => v.VertreterId!.Value));
        return d.Lehrer.Where(l => !belegt.Contains(l.Id) && Vertretungen.LehrerAbwesend(d, l.Id, datum, a.NachStunde) is null && Moeglich(d, ix, l, a.Tag, a.NachStunde))
            .OrderBy(l => Vertretungen.Zaehler(d, l.Id, Kalender.Montag(datum), Kalender.Montag(datum).AddDays(6))).ToList();
    }
}
