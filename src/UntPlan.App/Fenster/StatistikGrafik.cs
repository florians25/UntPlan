using System.Drawing.Drawing2D;

namespace UntPlan.App.Fenster;

/// <summary>Ein- und ausklappbarer Bereich der Vertretungsstatistik (grüne Titelleiste mit schwarzem Pfeil wie im Handbuch).</summary>
public sealed class Abschnitt : Panel
{
    public static readonly Color Leiste = Color.FromArgb(178, 210, 152);
    public static readonly Color Grund = Color.FromArgb(222, 236, 210);
    private readonly Panel _kopf = new() { Dock = DockStyle.Top, Height = 20, BackColor = Leiste, Cursor = Cursors.Hand };
    private readonly Label _pfeil = new() { Dock = DockStyle.Right, Width = 20, Text = "▼", TextAlign = ContentAlignment.MiddleCenter };
    private readonly Control _inhalt;
    private readonly int _hoehe;

    public Abschnitt(string titel, Control inhalt, int hoehe)
    {
        _inhalt = inhalt;
        _hoehe = hoehe;
        BackColor = Grund;
        Margin = new Padding(2, 2, 2, 4);
        BorderStyle = BorderStyle.FixedSingle;
        var t = new Label { Text = titel, Dock = DockStyle.Fill, Font = Ui.SchriftFett, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(40, 60, 30) };
        _kopf.Controls.Add(t);
        _kopf.Controls.Add(_pfeil);
        inhalt.Dock = DockStyle.Fill;
        Controls.Add(inhalt);
        Controls.Add(_kopf);
        Height = hoehe;
        foreach (var c in new Control[] { _kopf, t, _pfeil }) c.Click += (_, _) => Eingeklappt = !Eingeklappt;
    }

    public string Titel => _kopf.Controls[0].Text;

    public bool Eingeklappt
    {
        get => !_inhalt.Visible;
        set
        {
            _inhalt.Visible = !value;
            _pfeil.Text = value ? "◄" : "▼";
            Height = value ? _kopf.Height + 2 : _hoehe;
        }
    }
}

/// <summary>Verlaufsgrafik „Zeitraum“: Planmäßig, Vertretungen, Entfall und Gesamt je Tag; Rechtsklick blendet Kurven ein und aus.</summary>
public sealed class LinienGrafik : Control
{
    public sealed record Punkt(DateOnly Datum, int Planmaessig, int Vertretungen, int Entfall)
    {
        public int Gesamt => Planmaessig + Vertretungen + Entfall;
    }

    public static readonly Color FarbePlan = Color.FromArgb(0, 150, 0);
    public static readonly Color FarbeVertretung = Color.FromArgb(240, 140, 0);
    public static readonly Color FarbeEntfall = Color.FromArgb(220, 0, 0);
    public static readonly Color FarbeGesamt = Color.FromArgb(20, 30, 200);
    private readonly ToolTip _tip = new() { InitialDelay = 0, ReshowDelay = 0 };
    private readonly bool[] _an = { true, true, true, true };
    private List<Punkt> _punkte = new();
    private int _letzter = -1;

    public LinienGrafik()
    {
        DoubleBuffered = true;
        BackColor = Abschnitt.Grund;
        var cm = new ContextMenuStrip();
        foreach (var (i, text, farbe) in new[] { (0, "Planmäßig", FarbePlan), (1, "Vertretungen", FarbeVertretung), (2, "Entfall", FarbeEntfall), (3, "Gesamt", FarbeGesamt) })
        {
            var item = new ToolStripMenuItem(text) { Checked = true, CheckOnClick = true, ForeColor = farbe };
            var k = i;
            item.CheckedChanged += (_, _) => { _an[k] = item.Checked; Invalidate(); };
            cm.Items.Add(item);
        }
        ContextMenuStrip = cm;
    }

    public void Setzen(List<Punkt> punkte)
    {
        _punkte = punkte;
        _letzter = -1;
        Invalidate();
    }

    private Rectangle Flaeche => new(40, 8, Math.Max(10, Width - 56), Math.Max(10, Height - 30));

    private int Max => Math.Max(5, _punkte.Count == 0 ? 0 : _punkte.Max(p => Math.Max(_an[3] ? p.Gesamt : 0, Math.Max(_an[0] ? p.Planmaessig : 0, Math.Max(_an[1] ? p.Vertretungen : 0, _an[2] ? p.Entfall : 0)))));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = Flaeche;
        using (var hinter = new LinearGradientBrush(r, Color.White, Color.FromArgb(225, 225, 225), LinearGradientMode.Vertical)) g.FillRectangle(hinter, r);
        var max = Max;
        var schritt = max <= 10 ? 2 : max <= 30 ? 5 : max <= 60 ? 10 : max <= 150 ? 25 : 50;
        max = (max + schritt - 1) / schritt * schritt;
        using var raster = new Pen(Color.FromArgb(200, 200, 200));
        for (var w = 0; w <= max; w += schritt)
        {
            var y = r.Bottom - r.Height * w / max;
            g.DrawLine(raster, r.Left, y, r.Right, y);
            TextRenderer.DrawText(g, w.ToString(), Font, new Rectangle(0, y - 8, r.Left - 4, 16), Color.Black, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        g.DrawLine(Pens.Black, r.Left, r.Top, r.Left, r.Bottom);
        if (_punkte.Count == 0) return;
        float X(int i) => _punkte.Count == 1 ? r.Left + r.Width / 2f : r.Left + r.Width * i / (float)(_punkte.Count - 1);
        float Y(int w) => r.Bottom - r.Height * w / (float)max;
        var beschriftung = Math.Max(1, _punkte.Count / 5);
        for (var i = 0; i < _punkte.Count; i += beschriftung)
        {
            g.DrawLine(raster, X(i), r.Top, X(i), r.Bottom);
            TextRenderer.DrawText(g, _punkte[i].Datum.ToString("dd.MM.yy"), Font, new Point((int)X(i) - 22, r.Bottom + 3), Color.Black);
        }
        void Kurve(Func<Punkt, int> wert, Color farbe, bool flaeche)
        {
            var pts = _punkte.Select((p, i) => new PointF(X(i), Y(wert(p)))).ToArray();
            if (flaeche && pts.Length > 1)
            {
                using var pfad = new GraphicsPath();
                pfad.AddLines(pts);
                pfad.AddLine(pts[^1], new PointF(pts[^1].X, r.Bottom));
                pfad.AddLine(new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
                pfad.CloseFigure();
                using var b = new SolidBrush(Color.FromArgb(90, farbe));
                g.FillPath(b, pfad);
            }
            using var stift = new Pen(farbe, 2.5f);
            if (pts.Length > 1) g.DrawLines(stift, pts);
        }
        if (_an[0]) Kurve(p => p.Planmaessig, FarbePlan, true);
        if (_an[1]) Kurve(p => p.Vertretungen, FarbeVertretung, true);
        if (_an[2]) Kurve(p => p.Entfall, FarbeEntfall, true);
        if (_an[3]) Kurve(p => p.Gesamt, FarbeGesamt, false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_punkte.Count == 0) return;
        var r = Flaeche;
        var i = _punkte.Count == 1 ? 0 : (int)Math.Round((e.X - r.Left) * (_punkte.Count - 1) / (double)r.Width);
        if (i < 0 || i >= _punkte.Count) { _tip.Hide(this); _letzter = -1; return; }
        if (i == _letzter) return;
        _letzter = i;
        var p = _punkte[i];
        _tip.Show($"{p.Datum:dd.MM.yy}\n{p.Planmaessig} Planmäßig\n{p.Vertretungen} Vertretungen\n{p.Entfall} Entfall\n――――――\n{p.Gesamt} Gesamt", this, e.X + 12, e.Y + 12);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _tip.Hide(this);
        _letzter = -1;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}

/// <summary>Tortendiagramm der Absenzgründe mit Beschriftung; Klick auf ein Stück meldet dessen Schlüssel.</summary>
public sealed class TortenGrafik : Control
{
    public sealed record Stueck(object Schluessel, string Text, int Wert, Color Farbe);

    public static readonly Color[] Palette =
    {
        Color.FromArgb(178, 34, 34), Color.FromArgb(205, 133, 63), Color.FromArgb(154, 205, 50), Color.FromArgb(72, 209, 204), Color.FromArgb(65, 105, 225),
        Color.FromArgb(210, 105, 30), Color.FromArgb(147, 112, 219), Color.FromArgb(218, 165, 32), Color.FromArgb(46, 139, 87), Color.FromArgb(199, 21, 133),
    };

    private List<Stueck> _stuecke = new();
    public event Action<object>? Gewaehlt;

    public TortenGrafik()
    {
        DoubleBuffered = true;
        BackColor = Abschnitt.Grund;
        Cursor = Cursors.Hand;
    }

    public void Setzen(List<Stueck> stuecke)
    {
        _stuecke = stuecke;
        Invalidate();
    }

    private RectangleF Kreis
    {
        get
        {
            var d = Math.Max(20, Math.Min(Width - 160, Height - 40));
            return new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var summe = _stuecke.Sum(s => s.Wert);
        if (summe == 0) { TextRenderer.DrawText(g, "Keine Daten im Zeitraum.", Font, ClientRectangle, Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        var k = Kreis;
        float winkel = 90;
        foreach (var s in _stuecke)
        {
            var w = 360f * s.Wert / summe;
            using (var b = new LinearGradientBrush(k, ControlPaint.Light(s.Farbe, 0.3f), s.Farbe, 45f)) g.FillPie(b, k.X, k.Y, k.Width, k.Height, winkel, w);
            g.DrawPie(Pens.White, k.X, k.Y, k.Width, k.Height, winkel, w);
            var mitte = (winkel + w / 2) * Math.PI / 180;
            var r = k.Width / 2;
            var cx = k.X + r;
            var cy = k.Y + r;
            var p1 = new PointF(cx + (float)Math.Cos(mitte) * r, cy + (float)Math.Sin(mitte) * r);
            var p2 = new PointF(cx + (float)Math.Cos(mitte) * (r + 14), cy + (float)Math.Sin(mitte) * (r + 14));
            var rechts = Math.Cos(mitte) >= 0;
            var p3 = new PointF(p2.X + (rechts ? 12 : -12), p2.Y);
            g.DrawLines(Pens.Black, new[] { p1, p2, p3 });
            var groesse = TextRenderer.MeasureText(s.Text, Ui.SchriftFett);
            TextRenderer.DrawText(g, s.Text, Ui.SchriftFett, new Point((int)(rechts ? p3.X + 2 : p3.X - groesse.Width - 2), (int)p3.Y - groesse.Height / 2), Color.Black);
            winkel += w;
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var k = Kreis;
        var r = k.Width / 2;
        var dx = e.X - (k.X + r);
        var dy = e.Y - (k.Y + r);
        if (dx * dx + dy * dy > r * r) return;
        var summe = _stuecke.Sum(s => s.Wert);
        if (summe == 0) return;
        var a = (Math.Atan2(dy, dx) * 180 / Math.PI - 90 + 720) % 360;
        double w = 0;
        foreach (var s in _stuecke)
        {
            w += 360.0 * s.Wert / summe;
            if (a <= w) { Gewaehlt?.Invoke(s.Schluessel); return; }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
