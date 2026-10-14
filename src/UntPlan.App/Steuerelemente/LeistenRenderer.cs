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
