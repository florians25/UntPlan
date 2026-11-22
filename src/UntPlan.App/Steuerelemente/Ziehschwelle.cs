namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Gemeinsame Ziehschwelle für alle Ziehquellen: Ein Klick zieht nie. Erst wenn die Maus mit gedrückter linker Taste weiter als die
/// Windows-Ziehschwelle (mindestens 5 Pixel, in Bildschirmkoordinaten) bewegt wurde, beginnt das Ziehen.
/// </summary>
public static class Ziehschwelle
{
    /// <summary>Schwelle in Pixeln (Windows-Einstellung, mindestens 5).</summary>
    public static int Pixel => Math.Max(5, Math.Max(SystemInformation.DragSize.Width, SystemInformation.DragSize.Height));

    /// <summary>Ist die Maus seit <paramref name="startBildschirm"/> weit genug bewegt worden, um ein Ziehen zu beginnen?</summary>
    public static bool Ueberschritten(Point startBildschirm, Point jetztBildschirm) =>
        Math.Abs(jetztBildschirm.X - startBildschirm.X) >= Pixel || Math.Abs(jetztBildschirm.Y - startBildschirm.Y) >= Pixel;

    /// <summary>
    /// Ziehquelle anbinden: <paramref name="beginn"/> entscheidet beim Drücken der linken Maustaste, ob von hier gezogen werden kann
    /// (und liefert die Daten, null = nicht ziehen); gezogen wird erst nach Überschreiten der Schwelle.
    /// </summary>
    public static void Anbinden(Control quelle, Func<MouseEventArgs, object?> beginn, DragDropEffects effekte, Action<DragDropEffects>? danach = null)
    {
        object? daten = null;
        var start = Point.Empty;
        quelle.MouseDown += (_, e) =>
        {
            daten = e.Button == MouseButtons.Left ? beginn(e) : null;
            start = Cursor.Position;
        };
        quelle.MouseMove += (_, e) =>
        {
            if (daten is null) return;
            if ((e.Button & MouseButtons.Left) == 0) { daten = null; return; }
            if (!Ueberschritten(start, Cursor.Position)) return;
            var d = daten;
            daten = null;
            var ergebnis = quelle.DoDragDrop(d, effekte);
            danach?.Invoke(ergebnis);
        };
        quelle.MouseUp += (_, _) => daten = null;
        quelle.MouseCaptureChanged += (_, _) => { if (Control.MouseButtons == MouseButtons.None) daten = null; };
    }
}
