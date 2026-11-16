using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>
/// Übersichtspläne (Handbuch 6.4.2): Filter „Lehrer der Klasse“ bzw. „Lehrer des Faches“, Zeilenbeschriftung mit Wochenstunden
/// („verplant/noch zu verplanen“) bei Lehrkräften und mit Stammraum bei Klassen, speicherbare Elementauswahl.
/// </summary>
public static class Uebersichtsplan
{
    /// <summary>Lehrkräfte, die in der Klasse unterrichten (Kopplungszeilen mit dieser Klasse).</summary>
    public static HashSet<int> LehrerDerKlasse(Schuldatei d, int klasseId) =>
        d.Unterrichte.Where(u => !u.Ignorieren).SelectMany(u => u.Zeilen).Where(z => z.LehrerId.HasValue && z.KlassenIds.Contains(klasseId)).Select(z => z.LehrerId!.Value).ToHashSet();

    /// <summary>Lehrkräfte, die das Fach unterrichten.</summary>
    public static HashSet<int> LehrerDesFaches(Schuldatei d, int fachId) =>
        d.Unterrichte.Where(u => !u.Ignorieren).SelectMany(u => u.Zeilen).Where(z => z.LehrerId.HasValue && z.FachId == fachId).Select(z => z.LehrerId!.Value).ToHashSet();

    /// <summary>Verplante Wochenstunden der Lehrkraft und Stunden, die noch im Plan unterzubringen sind.</summary>
    public static (int Verplant, int Offen) Lehrerstunden(DatenIndex ix, int lehrerId) =>
        (ix.PlanVon(ElementTyp.Lehrer, lehrerId).Count(), ix.UnterrichteVon(ElementTyp.Lehrer, lehrerId).Where(u => !u.Ignorieren).Sum(ix.NichtVerplant));

    /// <summary>
    /// Beschriftung der Elementzeile: Kurzname, bei Lehrkräften optional „verplant/offen“ (z. B. „Arist 22/2“), bei Klassen optional der
    /// Stammraum (z. B. „1a R1a“).
    /// </summary>
    public static string Zeilenbeschriftung(DatenIndex ix, ElementTyp typ, Element e, bool wochenstunden, bool stammraum)
    {
        var text = e.Kurzname;
        if (wochenstunden && typ == ElementTyp.Lehrer)
        {
            var (verplant, offen) = Lehrerstunden(ix, e.Id);
            text += $" {verplant}/{offen}";
        }
        if (stammraum && e is Klasse k && k.StammraumId is int r) text += " " + ix.KRaum(r);
        return text;
    }

    /// <summary>Gespeicherter Filter als Text („Arist,Callas,Hugo“) → Ids der Elemente (unbekannte Namen werden übergangen).</summary>
    public static List<int> FilterAufloesen(IEnumerable<Element> elemente, string filter)
    {
        var namen = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return elemente.Where(e => namen.Contains(e.Kurzname)).Select(e => e.Id).ToList();
    }
}
