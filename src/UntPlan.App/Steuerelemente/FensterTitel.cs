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

    /// <summary>Titelfarbe; im Vertretungsmodus wie Untis hellgrau mit dunkler Schrift.</summary>
    public static Color Farbe(bool aktiv) => Ui.Vertretungsmodus
        ? (aktiv ? Color.FromArgb(212, 212, 212) : Color.FromArgb(232, 232, 232))
        : aktiv ? Ui.Titel : Mischen(Ui.Titel, Color.White, 0.45f);

    private static Color Mischen(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var farbe = Farbe(Aktiv);
        if (Ui.Vertretungsmodus)
        {
            // leichter Verlauf wie die Fenstertitel in Untis
            using var vb = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Mischen(farbe, Color.White, 0.55f), farbe, 90f);
            g.FillRectangle(vb, ClientRectangle);
            using var rand = new Pen(Color.FromArgb(185, 185, 185));
            g.DrawLine(rand, 0, Height - 1, Width, Height - 1);
        }
        else using (var b = new SolidBrush(farbe)) g.FillRectangle(b, ClientRectangle);
        var text = Ui.Textfarbe(farbe);
        var x = 6;
        if (_form.Icon != null)
        {
            try { g.DrawIcon(_form.Icon, new Rectangle(x, (Height - 16) / 2, 16, 16)); } catch { /* Symbol nicht darstellbar */ }
            x += 22;
        }
        TextRenderer.DrawText(g, _form.Text, Aktiv ? Ui.SchriftFett : Ui.Schrift, new Rectangle(x, 0, Width - x - 3 * KnopfBreite - 4, Height), text,
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Enumerable.Range(0, 3).FirstOrDefault(i => Knopf(i).Contains(e.Location), -1);
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _form.Activate();
        if (Enumerable.Range(0, 3).Any(i => Knopf(i).Contains(e.Location))) return;
        if (e.Clicks == 2) { Maximieren(); return; }
        if (_form.WindowState == FormWindowState.Maximized || !OperatingSystem.IsWindows()) return;
        ReleaseCapture();
        SendMessage(_form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (Knopf(0).Contains(e.Location)) _form.WindowState = FormWindowState.Minimized;
        else if (Knopf(1).Contains(e.Location)) Maximieren();
        else if (Knopf(2).Contains(e.Location)) _form.Close();
    }

    private void Maximieren() =>
        _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
