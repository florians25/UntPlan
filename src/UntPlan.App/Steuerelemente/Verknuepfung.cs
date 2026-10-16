using System.Drawing.Drawing2D;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Fensterverknüpfung wie im Vorbild: Wird in einem Fenster eine Klasse, Lehrkraft, ein Raum, Fach oder Unterricht bzw. eine Stunde
/// gewählt, zeigen alle verknüpften Fenster die Daten zu diesem Element. Die Verknüpfung ist pro Fenster über das Kettensymbol
/// („Fenster verknüpfen“) abschaltbar.
/// </summary>
public static class Verknuepfung
{
    private static readonly Dictionary<bool, Bitmap> Bilder = new();

    /// <summary>Kettensymbol (16 px): geschlossene Kette = verknüpft, geöffnete Kette = nicht verknüpft.</summary>
    public static Bitmap Symbol(bool verknuepft)
    {
        if (Bilder.TryGetValue(verknuepft, out var b)) return b;
        b = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var farbe = verknuepft ? Color.FromArgb(18, 94, 122) : Color.FromArgb(130, 130, 130);
            using var pen = new Pen(farbe, 2.2f);
            if (verknuepft)
            {
                g.TranslateTransform(8, 8);
                g.RotateTransform(-45);
                g.DrawArc(pen, -7, -2.8f, 7.5f, 5.6f, 90, 180);
                g.DrawLine(pen, -3.25f, -2.8f, -0.5f, -2.8f);
                g.DrawLine(pen, -3.25f, 2.8f, -0.5f, 2.8f);
                g.DrawArc(pen, -0.5f, -2.8f, 7.5f, 5.6f, -90, 180);
                g.DrawLine(pen, 0.5f, -2.8f, 3.25f, -2.8f);
                g.DrawLine(pen, 0.5f, 2.8f, 3.25f, 2.8f);
                using var mitte = new Pen(Color.FromArgb(229, 131, 42), 2.2f);
                g.DrawLine(mitte, -2.5f, 0, 2.5f, 0);
            }
            else
            {
                g.DrawArc(pen, 1, 5, 6, 6, 90, 180);
                g.DrawLine(pen, 4, 5, 6, 5);
                g.DrawLine(pen, 4, 11, 6, 11);
                g.DrawArc(pen, 9, 5, 6, 6, -90, 180);
                g.DrawLine(pen, 10, 5, 12, 5);
                g.DrawLine(pen, 10, 11, 12, 11);
            }
        }
        Bilder[verknuepft] = b;
        return b;
    }

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
