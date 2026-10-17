using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>
/// Angeklickte Zelle (auch leer, ohne Unterricht) eines Stundenplans, die verknüpfte Pläne übernehmen: Tag und Stunde, beim
/// tagesaktuellen Plan zusätzlich das Datum. <paramref name="Tag"/> ist der Tag des Regelplans (Index ab dem ersten Schultag).
/// </summary>
public sealed record PlanZelle(int Tag, int Stunde, DateOnly? Datum = null);

/// <summary>
/// Stundenplan-Synchronisation für leere Zellen: verknüpfte Pläne behalten ihr Element und markieren dieselbe Stunde am selben
/// Wochentag – im Regelplan über den Tagesindex, im tagesaktuellen Plan über die Spalte der angezeigten Woche.
/// </summary>
public static class PlanSynchronisation
{
    /// <summary>
    /// Zelle aus der Spalte eines Plans: Regelplan (<paramref name="woche"/> = null) – Spalte = Tag des Regelplans; tagesaktueller
    /// Plan – Spalte = Tage ab dem Montag der angezeigten Woche (Datum = woche + spalte).
    /// </summary>
    public static PlanZelle? AusSpalte(Schuldatei d, int spalte, int stunde, DateOnly? woche)
    {
        if (spalte < 0 || stunde < 1) return null;
        if (woche is not DateOnly w) return spalte < Math.Max(1, d.Zeitraster.TageProWoche) ? new PlanZelle(spalte, stunde) : null;
        var datum = w.AddDays(spalte);
        var tag = Kalender.TagIndex(d, datum);
        return new PlanZelle(tag, stunde, datum);
    }

    /// <summary>
    /// Spalte der Zelle in einem verknüpften Plan: im Regelplan der Tag (aus dem Datum, falls vorhanden), im tagesaktuellen Plan
    /// derselbe Wochentag in der dort angezeigten Woche. null = dieser Tag ist im Zielplan nicht darstellbar.
    /// </summary>
    public static int? ZielSpalte(Schuldatei d, PlanZelle z, DateOnly? zielWoche, int spalten)
    {
        int spalte;
        // tagesaktuell: derselbe Wochentag (Montag = 0) – unabhängig davon, welche Woche der Zielplan zeigt
        if (zielWoche.HasValue)
            spalte = z.Datum is DateOnly dt ? ((int)dt.DayOfWeek + 6) % 7 : z.Tag < 0 ? -1 : (z.Tag + d.Zeitraster.ErsterSchultag) % 7;
        else spalte = z.Datum is DateOnly dt2 ? Kalender.TagIndex(d, dt2) : z.Tag;
        return spalte >= 0 && spalte < spalten && z.Stunde >= 1 ? spalte : null;
    }

    /// <summary>Datum der Zielspalte im tagesaktuellen Plan (null im Regelplan).</summary>
    public static DateOnly? ZielDatum(DateOnly? zielWoche, int spalte) => zielWoche?.AddDays(spalte);
}
