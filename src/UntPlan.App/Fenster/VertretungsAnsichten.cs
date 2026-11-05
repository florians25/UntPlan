namespace UntPlan.App;

/// <summary>
/// Eigene Vertretungsansicht (Handbuch Vertretungsplanung Kap. 13.1.1): über „Ansicht speichern als…“ im Auswahlfeld unten rechts im
/// Vertretungsfenster angelegt. Sie übernimmt Art (Klasse/Lehrer/Druck/Pausenaufsichten), Felder der Ansicht, angezeigte
/// Vertretungsarten und Sortierung der Ansicht, aus der sie gespeichert wurde, und kann danach unabhängig angepasst werden.
/// </summary>
public sealed class EigeneVertretungsansicht
{
    /// <summary>Kurzname („Leli“) – erscheint im Auswahlfeld.</summary>
    public string Name { get; set; } = "";
    /// <summary>Langname („Vertretungen Lehrende“).</summary>
    public string Langname { get; set; } = "";
    /// <summary>Standardansicht, deren Art die eigene Ansicht übernimmt („Lehrer - Standard“ …).</summary>
    public string Basis { get; set; } = "Klasse - Standard";
}

/// <summary>Sortierkriterium einer Vertretungsansicht (Dialog „Sortierkriterien“, Handbuch Kap. 13.1.1.3).</summary>
public sealed class VertretungsSortierkriterium
{
    /// <summary>Spaltenname (interner Schlüssel, z. B. „Nr“, „Std“).</summary>
    public string Feld { get; set; } = "";
    public bool Absteigend { get; set; }
}

public sealed partial class Benutzereinstellungen
{
    /// <summary>Vertretungsfenster: eigene, mit „Ansicht speichern als…“ angelegte Ansichten.</summary>
    public List<EigeneVertretungsansicht> EigeneVertretungsansichten { get; set; } = new();
    /// <summary>Vertretungsfenster: gespeicherte Sortierung je Ansicht (bis zu fünf Kriterien, „Sortieren“).</summary>
    public Dictionary<string, List<VertretungsSortierkriterium>> VertretungsSortierung { get; set; } = new();
}
