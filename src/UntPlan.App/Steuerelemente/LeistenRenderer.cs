namespace UntPlan.App.Steuerelemente;

/// <summary>Darstellung der Schnellzugriffsleiste in der Modusfarbe (heller Hover, keine Systemrahmen).</summary>
public sealed class LeistenRenderer : ToolStripProfessionalRenderer
{
    public LeistenRenderer() => RoundedEdges = false;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Ui.Titel);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed && !(e.Item is ToolStripButton { Checked: true })) return;
        var hell = e.Item.Pressed ? 70 : 40;
        using var b = new SolidBrush(Color.FromArgb(hell, Color.White));
        e.Graphics.FillRectangle(b, 1, 1, e.Item.Width - 2, e.Item.Height - 2);
        // wie in der Vorlage: unter der Maus bzw. eingedrückt zusätzlich ein heller Rahmen (nicht bei den Fensterknöpfen rechts)
        if (e.Item.Alignment == ToolStripItemAlignment.Right) return;
        using var p = new Pen(Color.FromArgb(e.Item.Pressed ? 200 : 150, Color.White));
        e.Graphics.DrawRectangle(p, 1, 1, e.Item.Width - 3, e.Item.Height - 3);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Color.FromArgb(90, Color.White));
        var x = e.Item.Width / 2;
        e.Graphics.DrawLine(p, x, 4, x, e.Item.Height - 4);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) { base.OnRenderItemText(e); return; }
        e.TextColor = Color.White;
        base.OnRenderItemText(e);
    }
}
