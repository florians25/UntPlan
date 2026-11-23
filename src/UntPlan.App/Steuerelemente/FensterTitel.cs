using System.Runtime.InteropServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Eigene Titelleiste für Arbeitsfenster: Farbe des aktuellen Modus (Petrol bzw. Grün), Symbol, Titel und
/// Schaltflächen Minimieren/Maximieren/Schließen. Ziehen verschiebt, Doppelklick maximiert.
/// </summary>
public sealed class FensterTitel : Control
{
    // wie auf dem Original-Bildschirmfoto: Titelleiste der Arbeitsfenster 30 Pixel samt Rand
    public const int Hoehe = 30;
    // Vorlage: Fensterknöpfe im Abstand von 47 Pixeln, Mitte des Schließen-Knopfs 21 Pixel vor dem rechten Rand der Leiste,
    // Symbole 9 × 9 Pixel, 1 Pixel stark, ihre Mitte 8 Pixel unter der Oberkante der Leiste (nicht senkrecht mittig)
    private const int KnopfBreite = 47;
    private const int SymbolMitte = 10;
    /// <summary>Schrift des Fenstertitels wie in der Vorlage (Segoe UI 8,25: „Vertretungen / Klasse - Standard“ 164 Pixel breit).</summary>
    private static readonly Font TitelSchrift = new("Segoe UI", 8.25f);
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
        // Vorlage: zwei Knöpfe 13 × 18 Pixel im Abstand von 17 Pixeln, der rechte endet 28 Pixel vor der Mitte des Minimieren-Knopfs
        const int b = 13, h = 18;
        var x = DurchschalterLinks ? 24 : Knopf(0).X + KnopfBreite / 2 - 28 - b - 17;
        return new Rectangle(x + i * 17, 5, b, h);
    }

    private int SchalterBei(Point p) => Durchschalten is null ? -1 : Enumerable.Range(0, 2).FirstOrDefault(i => Schalter(i).Contains(p), -1);

    public FensterTitel(Form form)
    {
        _form = form;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = Hoehe;
        Font = TitelSchrift;
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
        var x = 9;
        if (_form.Icon != null)
        {
            try { g.DrawIcon(_form.Icon, new Rectangle(x, (Height - 16) / 2 + 2, 16, 16)); } catch { /* Symbol nicht darstellbar */ }
            x += 22;
        }
        var rechts = Knopf(0).X - 4;
        if (Durchschalten != null)
        {
            if (DurchschalterLinks) x = Schalter(1).Right + 6;
            else rechts = Schalter(0).X - 4;
            for (var i = 0; i < 2; i++) SchalterZeichnen(g, Schalter(i), i == 1, _schalterHover == i);
        }
        TextRenderer.DrawText(g, _form.Text, TitelSchrift, new Rectangle(x - 1, 3, Math.Max(0, rechts - x + 1), Height - 2), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        for (var i = 0; i < 3; i++)
        {
            var r = Knopf(i);
            if (_hover == i)
                using (var b = new SolidBrush(i == 2 ? Color.FromArgb(232, 17, 35) : Mischen(farbe, Color.White, 0.25f))) g.FillRectangle(b, r);
            var glyphe = _hover == 2 && i == 2 ? Color.White : Ui.Vertretungsmodus && !Aktiv ? Color.FromArgb(72, 72, 72) : text;
            var m = new Point(r.X + r.Width / 2, SymbolMitte);
            GlypheZeichnen(g, i == 1 && _form.WindowState == FormWindowState.Maximized ? 3 : i, m, glyphe, _hover == i ? (i == 2 ? Color.FromArgb(232, 17, 35) : Mischen(farbe, Color.White, 0.25f)) : farbe);
        }
    }

    // Vorbild (gemessen, 9 × 9 Pixel): Kreuz mit weichen Kanten (Nachbarpixel 30 %, Mitte 50 %), Quadrat mit halbtransparenten Ecken
    private static readonly float[,] Kreuz =
    {
        { 1, .3f, 0, 0, 0, 0, 0, .3f, 1 }, { .3f, 1, .3f, 0, 0, 0, .3f, 1, .3f }, { 0, .3f, 1, .3f, 0, .3f, 1, .3f, 0 },
        { 0, 0, .3f, 1, .5f, 1, .3f, 0, 0 }, { 0, 0, 0, .5f, 1, .5f, 0, 0, 0 }, { 0, 0, .3f, 1, .5f, 1, .3f, 0, 0 },
        { 0, .3f, 1, .3f, 0, .3f, 1, .3f, 0 }, { .3f, 1, .3f, 0, 0, 0, .3f, 1, .3f }, { 1, .3f, 0, 0, 0, 0, 0, .3f, 1 },
    };

    /// <summary>Fenstersymbol zeichnen: 0 Minimieren, 1 Maximieren, 2 Schließen, 3 Wiederherstellen (m = Mitte).</summary>
    internal static void GlypheZeichnen(Graphics g, int art, Point m, Color c, Color grund)
    {
        void Punkt(int x, int y, float a)
        {
            if (a <= 0) return;
            using var b = new SolidBrush(a >= 1 ? c : Mischen(grund, c, a));
            g.FillRectangle(b, x, y, 1, 1);
        }
        switch (art)
        {
            case 0:
                for (var k = -4; k <= 4; k++) Punkt(m.X + k, m.Y, 1);
                break;
            case 1:
                for (var k = -3; k <= 3; k++) { Punkt(m.X + k, m.Y - 4, 1); Punkt(m.X + k, m.Y + 4, 1); Punkt(m.X - 4, m.Y + k, 1); Punkt(m.X + 4, m.Y + k, 1); }
                foreach (var (dx, dy) in new[] { (-4, -4), (4, -4), (-4, 4), (4, 4) }) Punkt(m.X + dx, m.Y + dy, .64f);
                break;
            case 3:
                // Wiederherstellen: vorderes Quadrat 7 × 7 unten links, dahinter oben rechts die Kanten des zweiten
                for (var k = -3; k <= 1; k++) { Punkt(m.X + k, m.Y - 2, 1); Punkt(m.X + k, m.Y + 4, 1); }
                for (var k = -1; k <= 3; k++) { Punkt(m.X - 4, m.Y + k, 1); Punkt(m.X + 2, m.Y + k, 1); }
                foreach (var (dx, dy) in new[] { (-4, -2), (2, -2), (-4, 4), (2, 4) }) Punkt(m.X + dx, m.Y + dy, .64f);
                for (var k = -1; k <= 3; k++) Punkt(m.X + k, m.Y - 4, 1);
                for (var k = -3; k <= 1; k++) Punkt(m.X + 4, m.Y + k, 1);
                Punkt(m.X + 4, m.Y - 4, .64f);
                break;
            default:
                for (var y = 0; y < 9; y++)
                    for (var x = 0; x < 9; x++) Punkt(m.X - 4 + x, m.Y - 4 + y, Kreuz[y, x]);
                break;
        }
    }

    /// <summary>Weißer Knopf mit blauem Dreieck (Vorbild-Durchschalter, Symbol aus FatCow).</summary>
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
        // Blätterpfeil aus dem FatCow-Satz („resultset_next“ bzw. „resultset_previous“)
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.DrawImage(Symbole.Get(vor ? "vor" : "zurueck", 16), r.X + (r.Width - 16) / 2, r.Y + (r.Height - 16) / 2, 16, 16);
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
