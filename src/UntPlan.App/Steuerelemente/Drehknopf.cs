namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Kleiner Hoch/Runter-Knopf wie im Vorbild (neben der Elementauswahl und dem Datum): oben = weiter (+1), unten = zurück (−1).
/// </summary>
public sealed class Drehknopf : Control
{
    private int _hover;
    private int _gedrueckt;

    /// <summary>Geklickt: +1 (obere Hälfte) bzw. −1 (untere Hälfte).</summary>
    public event Action<int>? Gedreht;

    public Drehknopf(string hinweis)
    {
        Size = new Size(14, 21);
        Margin = new Padding(1, 1, 1, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        new ToolTip().SetToolTip(this, hinweis);
    }

    private int Haelfte(Point p) => p.Y < Height / 2 ? 1 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Haelfte(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = _gedrueckt = 0; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _gedrueckt = Haelfte(e.Location); Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var war = _gedrueckt;
        _gedrueckt = 0;
        Invalidate();
        if (e.Button == MouseButtons.Left && war != 0 && war == Haelfte(e.Location)) Gedreht?.Invoke(war);
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var oben = new Rectangle(0, 0, Width - 1, Height / 2 - 1);
        var unten = new Rectangle(0, Height / 2, Width - 1, Height - Height / 2 - 1);
        foreach (var (r, richtung) in new[] { (oben, 1), (unten, -1) })
        {
            var farbe = _gedrueckt == richtung ? Ui.BlauHell : _hover == richtung ? Ui.Hover : Color.FromArgb(250, 250, 250);
            using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
            using (var p = new Pen(_hover == richtung ? Ui.HoverRand : Color.FromArgb(170, 170, 170))) g.DrawRectangle(p, r);
            var m = new Point(r.X + r.Width / 2 + 1, r.Y + r.Height / 2 + 1);
            var dreieck = richtung > 0
                ? new[] { new Point(m.X - 3, m.Y + 1), new Point(m.X + 2, m.Y + 1), new Point(m.X, m.Y - 2) }
                : new[] { new Point(m.X - 3, m.Y - 1), new Point(m.X + 2, m.Y - 1), new Point(m.X, m.Y + 2) };
            using var dunkel = new SolidBrush(Enabled ? Color.FromArgb(40, 40, 40) : Color.Silver);
            g.FillPolygon(dunkel, dreieck);
        }
    }
}
