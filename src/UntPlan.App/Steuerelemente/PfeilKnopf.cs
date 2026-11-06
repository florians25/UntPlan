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

    /// <summary>
    /// Darstellung wie im Vorbild (Von-Bis): kleine weiße Pfeilfelder ohne Rahmen mit winzigem Dreieck bzw. Kalenderknopf als weißes
    /// Rechteck mit grauem Rand und grauem Dropdown-Dreieck.
    /// </summary>
    public bool Klassisch { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Klassisch) { KlassischZeichnen(g); return; }
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

    private void KlassischZeichnen(Graphics g)
    {
        var hinter = !Enabled ? Color.FromArgb(240, 240, 240) : _gedrueckt ? Color.FromArgb(204, 228, 247) : _ueber ? Color.FromArgb(229, 241, 251) : Color.White;
        using (var b = new SolidBrush(hinter)) g.FillRectangle(b, ClientRectangle);
        var farbe = Enabled ? Color.FromArgb(72, 72, 72) : Color.Silver;
        using var pinsel = new SolidBrush(farbe);
        if (_art == Art.Kalender)
        {
            using (var rand = new Pen(_ueber ? Color.FromArgb(0, 120, 215) : Color.FromArgb(171, 171, 171))) g.DrawRectangle(rand, 0, 0, Width - 1, Height - 1);
            // Kalenderblatt (links) und Dropdown-Dreieck (rechts)
            var k = new Rectangle(4, (Height - 14) / 2, 14, 14);
            using var stift = new Pen(Color.FromArgb(110, 110, 110));
            using var kopf = new SolidBrush(Color.FromArgb(127, 177, 206));
            g.FillRectangle(kopf, k.X, k.Y, k.Width, 4);
            g.DrawRectangle(stift, k.X, k.Y, k.Width - 1, k.Height - 1);
            using var tage = new SolidBrush(Color.FromArgb(90, 90, 90));
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 2; j++)
                    g.FillRectangle(tage, k.X + 2 + i * 4, k.Y + 6 + j * 3, 2, 2);
            var dx = Width - 13;
            var dy = Height / 2 - 1;
            using var grau = new SolidBrush(Color.FromArgb(119, 119, 119));
            g.FillPolygon(grau, new[] { new Point(dx, dy), new Point(dx + 7, dy), new Point(dx + 3, dy + 4), new Point(dx + 3, dy + 4) });
            return;
        }
        // winziges Dreieck (3 Pixel breit, 5 hoch) in der Mitte
        var mx = Width / 2;
        var my = Height / 2;
        var punkte = _art == Art.Links
            ? new[] { new Point(mx + 1, my - 3), new Point(mx + 1, my + 3), new Point(mx - 2, my) }
            : new[] { new Point(mx - 1, my - 3), new Point(mx - 1, my + 3), new Point(mx + 2, my) };
        g.SmoothingMode = SmoothingMode.None;
        g.FillPolygon(pinsel, punkte);
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
