namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Fensterverknüpfung wie im Vorbild: Wird in einem Fenster eine Klasse, Lehrkraft, ein Raum, Fach oder Unterricht bzw. eine Stunde
/// gewählt, zeigen alle verknüpften Fenster die Daten zu diesem Element. Die Verknüpfung ist pro Fenster über das Kettensymbol
/// („Fenster verknüpfen“) abschaltbar.
/// </summary>
public static class Verknuepfung
{
    /// <summary>Kettensymbol (16 px, FatCow): geschlossene Kette = verknüpft, getrennte Kette = nicht verknüpft.</summary>
    public static Bitmap Symbol(bool verknuepft) => Symbole.Get(verknuepft ? "verknuepft" : "nicht_verknuepft", 16);

    /// <summary>Werkzeugleisten-Knopf „Fenster verknüpfen“ (umschaltbar, Symbol zeigt den Zustand).</summary>
    public static ToolStripButton Knopf(bool start, Action<bool> geaendert)
    {
        var k = new ToolStripButton(Symbol(start))
        {
            CheckOnClick = true, Checked = start, DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Fenster verknüpfen: Das Fenster folgt dem in anderen Fenstern gewählten Element bzw. der gewählten Stunde",
        };
        k.CheckedChanged += (_, _) =>
        {
            k.Image = Symbol(k.Checked);
            geaendert(k.Checked);
        };
        return k;
    }
}
