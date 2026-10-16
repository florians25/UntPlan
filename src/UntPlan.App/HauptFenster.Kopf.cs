using System.Runtime.InteropServices;

namespace UntPlan.App;

/// <summary>
/// Programmkopf wie im Vorbild 2026: keine Windows-Titelleiste, sondern ein durchgehender Balken in der Modusfarbe –
/// links die Schnellzugriffsleiste, in der Mitte der Fenstertitel in kleiner weißer Schrift, rechts schlanke weiße
/// Fensterknöpfe (Minimieren, Maximieren/Wiederherstellen, Schließen). Darunter die Registerleiste in derselben Farbe.
/// Ziehen am Balken verschiebt das Fenster, Doppelklick maximiert/stellt wieder her, an den Rändern lässt es sich
/// in der Größe ändern (Aero Snap bleibt über die Systemnachrichten erhalten).
/// </summary>
public partial class HauptFenster
{
    private const int Rand = 5;
    private readonly ToolStripButton _knopfMin = new() { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Minimieren" };
    private readonly ToolStripButton _knopfMax = new() { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Maximieren" };
    private readonly ToolStripButton _knopfZu = new() { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Schließen" };
    private static readonly Font KopfSchrift = new("Segoe UI", 8.25f);

    /// <summary>Text in der Mitte des Programmkopfs (für den Selbsttest).</summary>
    internal string KopfTitel => Text;

    /// <summary>Bereich, in dem der Titel gezeichnet wurde (für den Selbsttest).</summary>
    internal Rectangle KopfTitelBereich { get; private set; }

    private void KopfVorbereiten()
    {
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        BackColor = Ui.Titel;
        foreach (var (k, art) in new[] { (_knopfZu, 2), (_knopfMax, 1), (_knopfMin, 0) })
        {
            k.DisplayStyle = ToolStripItemDisplayStyle.Image;
            k.AutoSize = false;
            k.Size = new Size(40, 24);
            k.Margin = new Padding(0);
            k.ImageScaling = ToolStripItemImageScaling.None;
            k.Image = KnopfBild(art, false);
            k.Tag = art;
        }
        _knopfMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _knopfMax.Click += (_, _) => MaximierenUmschalten();
        _knopfZu.Click += (_, _) => Close();
        _knopfZu.MouseEnter += (_, _) => _knopfZu.BackColor = Color.FromArgb(232, 17, 35);
        _knopfZu.MouseLeave += (_, _) => _knopfZu.BackColor = Color.Transparent;
        // rechtsbündige Einträge werden von rechts nach links angeordnet: Schließen ganz rechts
        _schnell.Items.AddRange(new ToolStripItem[] { _knopfZu, _knopfMax, _knopfMin });
        _schnell.Padding = new Padding(4, 0, 0, 0);
        _schnell.Paint += TitelZeichnen;
        _schnell.MouseDown += KopfMausDown;
        _schnell.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left && _schnell.GetItemAt(e.Location) is null) MaximierenUmschalten(); };
        _ribbon.MouseDown += (_, e) =>
        {
            // freie Fläche der Registerleiste rechts neben den Registerkarten zieht das Fenster ebenfalls
            if (e.Button == MouseButtons.Left && e.Y < Steuerelemente.Ribbon.TabHoehe && _ribbon.FreieKopfFlaeche(e.Location)) Ziehen();
        };
        TextChanged += (_, _) => _schnell.Invalidate();
        Resize += (_, _) => KopfAnpassen();
        LocationChanged += (_, _) => MaximalgroesseSetzen();
        MaximalgroesseSetzen();
        KopfAnpassen();
    }

    private void MaximierenUmschalten() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    /// <summary>Maximiert nur bis zur Taskleiste (ohne Systemrahmen würde das Fenster sonst den ganzen Bildschirm füllen).</summary>
    private void MaximalgroesseSetzen()
    {
        var s = IsHandleCreated ? Screen.FromHandle(Handle) : Screen.PrimaryScreen;
        if (s is null) return;
        var a = s.WorkingArea;
        var neu = new Rectangle(a.X - s.Bounds.X, a.Y - s.Bounds.Y, a.Width, a.Height);
        if (MaximizedBounds != neu) MaximizedBounds = neu;
    }

    private void KopfAnpassen()
    {
        var max = WindowState == FormWindowState.Maximized;
        // im normalen Zustand ein schmaler Rand in der Modusfarbe zum Ändern der Größe
        var p = max ? new Padding(0) : new Padding(Rand, 1, Rand, Rand);
        if (Padding != p) Padding = p;
        _knopfMax.Image = KnopfBild(max ? 3 : 1, false);
        _knopfMax.ToolTipText = max ? "Verkleinern" : "Maximieren";
        _schnell.Invalidate();
    }

    /// <summary>Farbe des Kopfs nach dem Moduswechsel.</summary>
    private void KopfFarben()
    {
        BackColor = Ui.Titel;
        _schnell.BackColor = Ui.Titel;
        _ribbon.Rechts.BackColor = Ui.Titel;
        Invalidate();
    }

    private void TitelZeichnen(object? sender, PaintEventArgs e)
    {
        // freier Bereich zwischen dem letzten linken und dem ersten rechten Eintrag
        var links = _schnell.Items.Cast<ToolStripItem>().Where(i => i.Visible && i.Alignment == ToolStripItemAlignment.Left).Select(i => i.Bounds.Right).DefaultIfEmpty(0).Max() + 12;
        var rechts = _schnell.Items.Cast<ToolStripItem>().Where(i => i.Visible && i.Alignment == ToolStripItemAlignment.Right).Select(i => i.Bounds.Left).DefaultIfEmpty(_schnell.Width).Min() - 12;
        if (rechts - links < 40) { KopfTitelBereich = Rectangle.Empty; return; }
        var groesse = TextRenderer.MeasureText(Text, KopfSchrift);
        // mittig im ganzen Fenster, wenn Platz ist, sonst im freien Bereich
        var x = Math.Max(links, Math.Min(rechts - groesse.Width, (_schnell.Width - groesse.Width) / 2));
        var r = new Rectangle(x, 0, Math.Min(groesse.Width, rechts - x), _schnell.Height);
        KopfTitelBereich = r;
        var farbe = ContainsFocus || Form.ActiveForm == this ? Color.White : Color.FromArgb(225, 255, 255, 255);
        TextRenderer.DrawText(e.Graphics, Text, KopfSchrift, r, farbe, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void KopfMausDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _schnell.GetItemAt(e.Location) != null || e.Clicks > 1) return;
        Ziehen();
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void Ziehen()
    {
        try
        {
            ReleaseCapture();
            const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Minimieren über die Taskleiste und Systemmenü (Alt+Leertaste) auch ohne Titelleiste
            const int WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_SYSMENU = 0x80000;
            cp.Style |= WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || WindowState != FormWindowState.Normal) return;
        // Größe an den Rändern ändern (die Ränder sind die Polsterung des Formulars in der Modusfarbe)
        var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        var l = p.X < Rand; var r = p.X >= ClientSize.Width - Rand;
        var o = p.Y < 3; var u = p.Y >= ClientSize.Height - Rand;
        int? hit = (o, u, l, r) switch
        {
            (true, _, true, _) => 13, (true, _, _, true) => 14, (_, true, true, _) => 16, (_, true, _, true) => 17,
            (true, _, _, _) => 12, (_, true, _, _) => 15, (_, _, true, _) => 10, (_, _, _, true) => 11, _ => null,
        };
        if (hit is int h) m.Result = h;
    }

    /// <summary>Schlanke weiße Fensterknöpfe wie Windows 11 / Vorbild 2026 (0 = minimieren, 1 = maximieren, 2 = schließen, 3 = wiederherstellen).</summary>
    private static Bitmap KnopfBild(int art, bool dunkel)
    {
        var bmp = new Bitmap(40, 24);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = art == 2 ? System.Drawing.Drawing2D.SmoothingMode.AntiAlias : System.Drawing.Drawing2D.SmoothingMode.None;
        using var p = new Pen(dunkel ? Color.FromArgb(40, 40, 40) : Color.White, 1f);
        const int cx = 20, cy = 12;
        switch (art)
        {
            case 0: g.DrawLine(p, cx - 5, cy, cx + 5, cy); break;
            case 1: g.DrawRectangle(p, cx - 5, cy - 5, 10, 10); break;
            case 2: g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5); break;
            case 3:
                g.DrawRectangle(p, cx - 5, cy - 3, 8, 8);
                g.DrawLine(p, cx - 3, cy - 5, cx + 5, cy - 5);
                g.DrawLine(p, cx + 5, cy - 5, cx + 5, cy + 3);
                break;
        }
        return bmp;
    }
}
