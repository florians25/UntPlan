namespace UntPlan.App.Dialoge;

/// <summary>
/// Gemeinsame Maße der Vertretungs-Dialoge wie in den Handbuch-Abbildungen (auf 96 dpi umgerechnet):
/// Windows-Standardschrift der Dialoge (Microsoft Sans Serif 8,25) und Standard-Schaltfläche 75 × 23.
/// </summary>
internal static class VorbildMasse
{
    /// <summary>Dialogschrift der Vorbild-Dialoge (Rückfragen, Sondereinsatz / Stunde ändern, Klausur, Auswahllisten).</summary>
    public static readonly Font Schrift = new("Microsoft Sans Serif", 8.25f);

    /// <summary>Standardgröße einer Schaltfläche (OK, Abbrechen …).</summary>
    public static readonly Size Knopf = new(75, 23);
}
