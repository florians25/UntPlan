using System.Drawing.Drawing2D;

namespace UntPlan.App.Steuerelemente;

/// <summary>Flacher Blätterknopf mit gezeichnetem Winkel (‹ ›) bzw. Kalendersymbol, mit Hover- und Druckzustand.</summary>
public sealed class PfeilKnopf : Control
{
    public enum Art { Links, Rechts, Kalender }

    private readonly Art _art;
    private bool _ueber, _gedrueckt;

    public PfeilKnopf(Art art, string hinweis)
    {
        _art = art;
        Size = new Size(art == Art.Kalender ? 30 : 22, 23);
        Margin = new Padding(1, 2, 0, 0);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        new ToolTip().SetToolTip(this, hinweis);
    }

    protected override void OnMouseEnter(EventArgs e) { _ueber = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _ueber = _gedrueckt = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _gedrueckt = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _gedrueckt = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var pfad = Rund(r, 3))
        {
            var hinter = !Enabled ? Color.FromArgb(240, 240, 240) : _gedrueckt ? Ui.BlauHell : _ueber ? Ui.BlauSehrHell : Color.White;
            using var b = new SolidBrush(hinter);
            g.FillPath(b, pfad);
            using var rand = new Pen(_ueber ? Ui.Blau : Ui.Rasterlinie);
            g.DrawPath(rand, pfad);
        }
        var farbe = Enabled ? (_ueber ? Ui.Blau : Color.FromArgb(70, 80, 95)) : Color.Silver;
        var m = new PointF(Width / 2f, Height / 2f);
        if (_art == Art.Kalender)
        {
            // kleines Kalenderblatt mit Kopfleiste und Ringen, daneben ein Dropdown-Dreieck
            var k = new RectangleF(5, 6, 13, 12);
            using var stift = new Pen(farbe, 1.2f);
            using var kopf = new SolidBrush(_ueber ? Ui.Blau : Color.FromArgb(200, 70, 60));
            g.FillRectangle(Brushes.White, k);
            g.FillRectangle(kopf, k.X, k.Y, k.Width, 3.5f);
            g.DrawRectangle(stift, k.X, k.Y, k.Width, k.Height);
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 2; j++)
                    g.FillRectangle(new SolidBrush(farbe), k.X + 2.2f + i * 3.5f, k.Y + 5.5f + j * 3f, 1.8f, 1.6f);
            using var dreieck = new SolidBrush(farbe);
            g.FillPolygon(dreieck, new[] { new PointF(21, 10), new PointF(26, 10), new PointF(23.5f, 13.5f) });
            return;
        }
        using var p = new Pen(farbe, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var dx = _art == Art.Links ? 2.5f : -2.5f;
        g.DrawLines(p, new[] { new PointF(m.X + dx, m.Y - 5), new PointF(m.X - dx, m.Y), new PointF(m.X + dx, m.Y + 5) });
    }

    private static GraphicsPath Rund(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Winkel als Bild für Werkzeugleisten (16×16).</summary>
    public static Bitmap Bild(bool rechts)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(Color.FromArgb(60, 75, 95), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var dx = rechts ? -2.5f : 2.5f;
        g.DrawLines(p, new[] { new PointF(8 + dx, 3), new PointF(8 - dx, 8), new PointF(8 + dx, 13) });
        return bmp;
    }
}
