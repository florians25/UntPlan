namespace UntPlan.App.Steuerelemente;

/// <summary>Darstellung der Schnellzugriffsleiste in der Modusfarbe (heller Hover, keine Systemrahmen).</summary>
public sealed class LeistenRenderer : ToolStripProfessionalRenderer
{
    public LeistenRenderer() => RoundedEdges = false;

    /// <summary>Kontextband der gewählten Registerkarte in Leistenkoordinaten (Vorlage, Vertretungsmodus); leer = keines.</summary>
    public Func<Rectangle>? Band { get; init; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Ui.Titel);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
        // Vorlage: oben 4 Pixel hellgrün, darunter dunkler bis zur Registerkarte (unter den Symbolen des Schnellzugriffs)
        if (Band?.Invoke() is { Width: > 0 } band && SichtbaresBand(e.ToolStrip, band) is { Width: > 0 } frei)
        {
            using (var d = new SolidBrush(Ribbon.KontextDunkel)) e.Graphics.FillRectangle(d, frei.X, 0, frei.Width, e.ToolStrip.Height);
            using (var h = new SolidBrush(Ribbon.KontextHell)) e.Graphics.FillRectangle(h, frei.X, 0, frei.Width, 4);
        }
    }

    /// <summary>
    /// Teil des Kontextbands, der in der Schnellzugriffsleiste sichtbar ist: nur im Bereich der Registerkarte und nie hinter den
    /// Einträgen der Leiste (Symbole, „UntPlanWeb“, Trennstriche, Fensterknöpfe behalten ihren normalen Grund).
    /// </summary>
    internal static Rectangle SichtbaresBand(ToolStrip leiste, Rectangle band)
    {
        var (von, bis) = (band.Left, band.Right);
        foreach (ToolStripItem i in leiste.Items)
        {
            if (!i.Visible || i.Bounds.Width <= 0 || i.Bounds.Right <= von || i.Bounds.Left >= bis) continue;
            if (i.Alignment == ToolStripItemAlignment.Left) von = Math.Max(von, i.Bounds.Right + i.Margin.Right);
            else bis = Math.Min(bis, i.Bounds.Left - i.Margin.Left);
        }
        return bis > von ? new Rectangle(von, band.Y, bis - von, band.Height) : Rectangle.Empty;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed && !(e.Item is ToolStripButton { Checked: true })) return;
        var hell = e.Item.Pressed ? 70 : 40;
        using var b = new SolidBrush(Color.FromArgb(hell, Color.White));
        e.Graphics.FillRectangle(b, 1, 1, e.Item.Width - 2, e.Item.Height - 2);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

    /// <summary>Eigene Farbe der Trennlinien (z. B. hellgrau wie rechts in der Registerleiste des Vorbilds), sonst halbtransparent weiß.</summary>
    public Color? Trennfarbe { get; init; }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (Trennfarbe is Color tf)
        {
            using var hp = new Pen(tf);
            e.Graphics.DrawLine(hp, e.Item.Width / 2, 2, e.Item.Width / 2, e.Item.Height - 4);
            return;
        }
        using var p = new Pen(Color.FromArgb(90, Color.White));
        var x = e.Item.Width / 2;
        e.Graphics.DrawLine(p, x, 4, x, e.Item.Height - 4);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (!GrauesSymbol.Zeichnen(e)) base.OnRenderItemImage(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) { base.OnRenderItemText(e); return; }
        e.TextColor = Color.White;
        base.OnRenderItemText(e);
    }
}

/// <summary>
/// Deaktivierte Knöpfe und Menüeinträge zeigen die offizielle graue FatCow-Variante ihres Symbols (statt des berechneten Graufilters
/// von Windows Forms). Für Bilder, die nicht aus <see cref="Symbole"/> stammen, bleibt die Standarddarstellung.
/// </summary>
internal static class GrauesSymbol
{
    public static bool Zeichnen(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item.Enabled || e.ImageRectangle.Width <= 0 || Symbole.Grau(e.Item.Image) is not { } grau) return false;
        e.Graphics.DrawImage(grau, e.ImageRectangle);
        return true;
    }
}
