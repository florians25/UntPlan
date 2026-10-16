using System.Runtime.InteropServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Eigene Titelleiste für Arbeitsfenster: Farbe des aktuellen Modus (Petrol bzw. Grün), Symbol, Titel und
/// Schaltflächen Minimieren/Maximieren/Schließen. Ziehen verschiebt, Doppelklick maximiert.
/// </summary>
public sealed class FensterTitel : Control
{
    public const int Hoehe = 26;
    private const int KnopfBreite = 34;
    private readonly Form _form;
    private int _hover = -1;

    public bool Aktiv { get; set; }

    /// <summary>Blaue Durchschaltknöpfe ◀ ▶ im Titel wie im Vorbild (vorheriges/nächstes Element); null = keine.</summary>
    public Action<int>? Durchschalten { get; set; }
    /// <summary>Durchschaltknöpfe links neben dem Titel (kleine Fenster) statt rechts vor den Fensterknöpfen.</summary>
    public bool DurchschalterLinks { get; set; }
    private int _schalterHover = -1;

    private Rectangle Schalter(int i)
    {
        const int b = 15, h = 17;
        var x = DurchschalterLinks ? 22 : Width - 3 * KnopfBreite - 2 * b - 10;
        return new Rectangle(x + i * (b + 2), (Height - h) / 2, b, h);
    }

    private int SchalterBei(Point p) => Durchschalten is null ? -1 : Enumerable.Range(0, 2).FirstOrDefault(i => Schalter(i).Contains(p), -1);

    public FensterTitel(Form form)
    {
        _form = form;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = Hoehe;
        Font = Ui.Schrift;
        TabStop = false;
    }

    private Rectangle Knopf(int i) => new(Width - (3 - i) * KnopfBreite, 0, KnopfBreite, Height);

    /// <summary>Titelfarbe; im Vertretungsmodus wie im Vorbild: aktives Fenster dunkelgrün mit weißer Schrift, inaktive hellgrün.</summary>
    public static Color Farbe(bool aktiv) => Ui.Vertretungsmodus
        ? (aktiv ? Color.FromArgb(88, 150, 125) : Color.FromArgb(149, 208, 185))
        : aktiv ? Ui.Titel : Mischen(Ui.Titel, Color.White, 0.45f);

    private static Color Mischen(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var farbe = Farbe(Aktiv);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, ClientRectangle);
        var text = Ui.Textfarbe(farbe);
        var x = 6;
        if (_form.Icon != null)
        {
            try { g.DrawIcon(_form.Icon, new Rectangle(x, (Height - 16) / 2, 16, 16)); } catch { /* Symbol nicht darstellbar */ }
            x += 22;
        }
        var rechts = Width - 3 * KnopfBreite - 4;
        if (Durchschalten != null)
        {
            if (DurchschalterLinks) x = Schalter(1).Right + 6;
            else rechts = Schalter(0).X - 4;
            for (var i = 0; i < 2; i++) SchalterZeichnen(g, Schalter(i), i == 1, _schalterHover == i);
        }
        TextRenderer.DrawText(g, _form.Text, Aktiv ? Ui.SchriftFett : Ui.Schrift, new Rectangle(x, 0, Math.Max(0, rechts - x), Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        for (var i = 0; i < 3; i++)
        {
            var r = Knopf(i);
            if (_hover == i)
                using (var b = new SolidBrush(i == 2 ? Color.FromArgb(232, 17, 35) : Mischen(farbe, Color.White, 0.25f))) g.FillRectangle(b, r);
            using var p = new Pen(_hover == 2 && i == 2 ? Color.White : text, 1.2f);
            var m = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            switch (i)
            {
                case 0: g.DrawLine(p, m.X - 5, m.Y, m.X + 5, m.Y); break;
                case 1:
                    if (_form.WindowState == FormWindowState.Maximized)
                    {
                        g.DrawRectangle(p, m.X - 5, m.Y - 3, 8, 8);
                        g.DrawLine(p, m.X - 3, m.Y - 5, m.X + 5, m.Y - 5);
                        g.DrawLine(p, m.X + 5, m.Y - 5, m.X + 5, m.Y + 3);
                    }
                    else g.DrawRectangle(p, m.X - 5, m.Y - 5, 10, 10);
                    break;
                default:
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawLine(p, m.X - 5, m.Y - 5, m.X + 5, m.Y + 5);
                    g.DrawLine(p, m.X + 5, m.Y - 5, m.X - 5, m.Y + 5);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    break;
            }
        }
    }

    /// <summary>Weißer Knopf mit blauem Dreieck (Vorbild-Durchschalter).</summary>
    private static void SchalterZeichnen(Graphics g, Rectangle r, bool vor, bool hover)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var pfad = Ui.AbgerundetesRechteck(r, 3))
        {
            using var b = new SolidBrush(hover ? Color.FromArgb(225, 238, 255) : Color.White);
            g.FillPath(b, pfad);
            using var p = new Pen(Color.FromArgb(120, 120, 120));
            g.DrawPath(p, pfad);
        }
        var m = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        var d = vor
            ? new[] { new PointF(m.X - 3, m.Y - 5), new PointF(m.X + 4, m.Y), new PointF(m.X - 3, m.Y + 5) }
            : new[] { new PointF(m.X + 3, m.Y - 5), new PointF(m.X - 4, m.Y), new PointF(m.X + 3, m.Y + 5) };
        using (var blau = new SolidBrush(Color.FromArgb(0, 102, 214))) g.FillPolygon(blau, d);
        using (var rand = new Pen(Color.FromArgb(0, 40, 110))) g.DrawPolygon(rand, d);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Enumerable.Range(0, 3).FirstOrDefault(i => Knopf(i).Contains(e.Location), -1);
        var s = SchalterBei(e.Location);
        if (h != _hover || s != _schalterHover) { _hover = h; _schalterHover = s; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        _schalterHover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _form.Activate();
        if (Enumerable.Range(0, 3).Any(i => Knopf(i).Contains(e.Location)) || SchalterBei(e.Location) >= 0) return;
        if (e.Clicks == 2) { Maximieren(); return; }
        if (_form.WindowState == FormWindowState.Maximized || !OperatingSystem.IsWindows()) return;
        ReleaseCapture();
        SendMessage(_form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (SchalterBei(e.Location) is var sb && sb >= 0) { Durchschalten?.Invoke(sb == 0 ? -1 : 1); return; }
        if (Knopf(0).Contains(e.Location)) _form.WindowState = FormWindowState.Minimized;
        else if (Knopf(1).Contains(e.Location)) Maximieren();
        else if (Knopf(2).Contains(e.Location)) _form.Close();
    }

    private void Maximieren() =>
        _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
