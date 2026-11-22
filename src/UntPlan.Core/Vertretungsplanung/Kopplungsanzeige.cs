namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Art eines Kopplungselements (Kopplungszeile) in einer Zelle des tagesaktuellen Plans.</summary>
public enum KopplungsteilArt
{
    /// <summary>Geändert (Vertretung, Raum- bzw. Unterrichtsänderung, zusätzlich): oben, rot.</summary>
    Geaendert,
    /// <summary>Nur dieses Element entfällt, die übrigen finden statt: wie eine Änderung (oben, rot, durchgestrichen).</summary>
    Entfallen,
    /// <summary>Offene Vertretung: rot mit „?“ nach dem Kopplungspunkt („.?M_G“).</summary>
    Offen,
    /// <summary>Unverändert: unten in normaler Schrift.</summary>
    Unveraendert,
}

/// <summary>Ein Element (Kopplungszeile) einer gekoppelten Stunde mit seiner Art.</summary>
public sealed record Kopplungsteil(TagesEintrag Eintrag, KopplungsteilArt Art)
{
    /// <summary>Rot hervorgehoben (geändert, einzeln entfallen oder offen).</summary>
    public bool Hervorgehoben => Art != KopplungsteilArt.Unveraendert;
}

/// <summary>
/// Darstellung einer gekoppelten Stunde im tagesaktuellen Plan (Klassenplan): Änderungen oben, offene Änderungen darunter, die
/// unveränderten Elemente unten. Entfällt nur ein Teil der Kopplung, ist die Stunde eine Änderung – Entfall nur, wenn alle Elemente entfallen.
/// </summary>
public static class Kopplungsanzeige
{
    /// <summary>Art eines einzelnen Eintrags (eine Kopplungszeile).</summary>
    public static KopplungsteilArt Art(TagesEintrag e) => e.Status switch
    {
        TagesStatus.Entfall => KopplungsteilArt.Entfallen,
        TagesStatus.Offen => KopplungsteilArt.Offen,
        TagesStatus.Vertreten or TagesStatus.Zusaetzlich => KopplungsteilArt.Geaendert,
        _ => e.Vertretung != null ? KopplungsteilArt.Geaendert : KopplungsteilArt.Unveraendert,
    };

    /// <summary>
    /// Gesamtstatus der Zelle: <see cref="TagesStatus.Entfall"/> nur, wenn alle Elemente entfallen; sonst offen, wenn ein Element offen
    /// ist, geändert (<see cref="TagesStatus.Vertreten"/>), wenn ein Element geändert ist oder einzeln entfällt, sonst regulär.
    /// </summary>
    public static TagesStatus Zellstatus(IReadOnlyCollection<TagesEintrag> eintraege)
    {
        if (eintraege.Count == 0) return TagesStatus.Frei;
        if (eintraege.All(e => e.Status == TagesStatus.Entfall)) return TagesStatus.Entfall;
        var arten = eintraege.Select(Art).ToList();
        if (arten.Contains(KopplungsteilArt.Offen)) return TagesStatus.Offen;
        return arten.Any(a => a != KopplungsteilArt.Unveraendert) ? TagesStatus.Vertreten : TagesStatus.Regulaer;
    }

    /// <summary>Reihenfolge in der Zelle: Änderungen (auch einzeln entfallene Elemente) oben, dann offene, dann unveränderte; je Gruppe nach Kopplungszeile.</summary>
    public static List<Kopplungsteil> Reihenfolge(IEnumerable<TagesEintrag> eintraege) =>
        eintraege.Select((e, i) => (Teil: new Kopplungsteil(e, Art(e)), Index: i))
            .OrderBy(x => Rang(x.Teil.Art))
            .ThenBy(x => x.Teil.Eintrag.Regel?.ZeilenIndex ?? x.Teil.Eintrag.Vertretung?.ZeilenIndex ?? int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Teil).ToList();

    private static int Rang(KopplungsteilArt a) => a switch
    {
        KopplungsteilArt.Geaendert or KopplungsteilArt.Entfallen => 0,
        KopplungsteilArt.Offen => 1,
        _ => 2,
    };

    /// <summary>
    /// Sollen die Einträge einer gekoppelten Stunde als Elementliste gezeigt werden? Ja, wenn mehrere Einträge vorliegen, mindestens
    /// einer geändert bzw. offen ist oder einzeln entfällt – aber nicht alle entfallen (dann ist die Stunde ein Entfall).
    /// </summary>
    public static bool AlsElementliste(IReadOnlyCollection<TagesEintrag> eintraege) =>
        eintraege.Count > 1 && Zellstatus(eintraege) is TagesStatus.Vertreten or TagesStatus.Offen;

    /// <summary>
    /// Ein Eintrag für die ganze gekoppelte Stunde: Werte des obersten nicht entfallenen Elements (Auswahl, Ziehen, Kontextmenü),
    /// Klassen aller Elemente und die Elemente in Anzeige-Reihenfolge (<see cref="TagesEintrag.Kopplungsteile"/>).
    /// </summary>
    public static TagesEintrag Zusammenfassen(IReadOnlyList<TagesEintrag> eintraege)
    {
        var teile = Reihenfolge(eintraege);
        var basis = teile.FirstOrDefault(t => t.Art != KopplungsteilArt.Entfallen)?.Eintrag ?? teile[0].Eintrag;
        return new TagesEintrag
        {
            Stunde = basis.Stunde, Regel = basis.Regel, Vertretung = basis.Vertretung, Status = basis.Status, FachId = basis.FachId,
            LehrerIds = basis.LehrerIds.ToList(), RaumIds = basis.RaumIds.ToList(),
            KlassenIds = eintraege.SelectMany(z => z.KlassenIds).Distinct().ToList(),
            Kopplungsteile = teile,
        };
    }
}
