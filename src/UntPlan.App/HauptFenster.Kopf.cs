using System.Runtime.InteropServices;

namespace UntPlan.App;

/// <summary>
/// Programmkopf wie im Vorbild 2026: keine Windows-Titelleiste, sondern ein durchgehender Balken in der Modusfarbe –
/// links die Schnellzugriffsleiste, in der Mitte der Fenstertitel in kleiner weißer Schrift, rechts schlanke weiße
/// Fensterknöpfe (Minimieren, Maximieren/Wiederherstellen, Schließen). Darunter die Registerleiste in derselben Farbe.
/// Unter Windows ist es trotzdem ein echtes Windows-Fenster: Es behält den Systemrahmen (WS_THICKFRAME) – also Schatten,
/// abgerundete Ecken unter Windows 11, Andocken (Snap), unsichtbare Greifränder und Maximieren bis zur Taskleiste –,
/// nur der Titelbereich wird über WM_NCCALCSIZE dem Programmkopf überlassen. Lage und Zustand werden gemerkt.
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

    /// <summary>
    /// Echter Windows-Rahmen (Systemrahmen ohne Titelleiste). Unter Wine bleibt das Fenster rahmenlos, weil Wine den
    /// Systemrahmen selbst als breiten 3D-Rand zeichnen würde.
    /// </summary>
    internal static bool NativerRahmen { get; } = OperatingSystem.IsWindows() && !UnterWine();

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr modul, string name);
    private static bool UnterWine()
    {
        try { var nt = GetModuleHandle("ntdll.dll"); return nt != IntPtr.Zero && GetProcAddress(nt, "wine_get_version") != IntPtr.Zero; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Lage beim Start: zuletzt benutzte Lage, sonst maximiert (kleine Bildschirme) bzw. mittig mit 90 % der Arbeitsfläche.</summary>
    private void FensterlageWiederherstellen()
    {
        var e = AppZustand.Einstellungen;
        var flaeche = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        StartPosition = FormStartPosition.Manual;
        if (e.Hauptfenster is { Length: 4 } b)
        {
            var r = new Rectangle(b[0], b[1], Math.Max(MinimumSize.Width, b[2]), Math.Max(MinimumSize.Height, b[3]));
            // nur übernehmen, wenn die Titelleiste auf einem vorhandenen Bildschirm liegt
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y, Math.Max(10, r.Width - 80), 30))))
            {
                Bounds = r;
                WindowState = e.HauptfensterMaximiert ? FormWindowState.Maximized : FormWindowState.Normal;
                return;
            }
        }
        var w = Math.Min(flaeche.Width, Math.Max(MinimumSize.Width, (int)(flaeche.Width * .9)));
        var h = Math.Min(flaeche.Height, Math.Max(MinimumSize.Height, (int)(flaeche.Height * .9)));
        Bounds = new Rectangle(flaeche.X + (flaeche.Width - w) / 2, flaeche.Y + (flaeche.Height - h) / 2, w, h);
        WindowState = flaeche.Width < 1500 || flaeche.Height < 860 || e.HauptfensterMaximiert ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    private void FensterlageMerken()
    {
        var e = AppZustand.Einstellungen;
        if (WindowState == FormWindowState.Minimized) return;
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        e.Hauptfenster = new[] { r.X, r.Y, r.Width, r.Height };
        e.HauptfensterMaximiert = WindowState == FormWindowState.Maximized;
        try { e.Speichern(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void KopfVorbereiten()
    {
        FormBorderStyle = NativerRahmen ? FormBorderStyle.Sizable : FormBorderStyle.None;
        DoubleBuffered = true;
        BackColor = Ui.Titel;
        foreach (var (k, art) in new[] { (_knopfZu, 2), (_knopfMax, 1), (_knopfMin, 0) })
        {
            k.DisplayStyle = ToolStripItemDisplayStyle.Image;
            k.AutoSize = false;
            k.Margin = new Padding(0);
            k.ImageScaling = ToolStripItemImageScaling.None;
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
        KopfHoeheSetzen();
        DpiChanged += (_, _) => KopfHoeheSetzen();
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
        if (!NativerRahmen)
        {
            LocationChanged += (_, _) => MaximalgroesseSetzen();
            MaximalgroesseSetzen();
        }
        KopfAnpassen();
        var filter = new KantenFilter(this);
        Application.AddMessageFilter(filter);
        FormClosed += (_, _) => Application.RemoveMessageFilter(filter);
    }

    /// <summary>Kante des Fensters unter dem Mauszeiger (Bildschirmkoordinaten) als Trefferwert für WM_NCLBUTTONDOWN, sonst null.</summary>
    internal int? KantenTreffer(Point bildschirm)
    {
        if (WindowState != FormWindowState.Normal || !Visible) return null;
        var b = Bounds;
        if (!b.Contains(bildschirm)) return null;
        var l = bildschirm.X < b.Left + Rand; var r = bildschirm.X >= b.Right - Rand;
        var o = bildschirm.Y < b.Top + 3; var u = bildschirm.Y >= b.Bottom - Rand;
        return (o, u, l, r) switch
        {
            (true, _, true, _) => 13, (true, _, _, true) => 14, (_, true, true, _) => 16, (_, true, _, true) => 17,
            (true, _, _, _) => 12, (_, true, _, _) => 15, (_, _, true, _) => 10, (_, _, _, true) => 11, _ => null,
        };
    }

    /// <summary>
    /// Unsichtbarer Greifbereich an den Fensterkanten: Mausnachrichten an untergeordnete Steuerelemente nahe der Kante setzen den
    /// Größenänderungszeiger bzw. starten die Größenänderung über das System (wie bei einem normalen Fensterrahmen).
    /// </summary>
    private sealed class KantenFilter(HauptFenster f) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201;
            if (m.Msg is not (WM_MOUSEMOVE or WM_LBUTTONDOWN) || f.IsDisposed || !f.IsHandleCreated) return false;
            if (Control.FromHandle(m.HWnd) is not { } c || c.FindForm() != f) return false;
            if (f.KantenTreffer(Cursor.Position) is not int hit) return false;
            Cursor.Current = hit switch { 10 or 11 => Cursors.SizeWE, 12 or 15 => Cursors.SizeNS, 13 or 17 => Cursors.SizeNWSE, _ => Cursors.SizeNESW };
            if (m.Msg != WM_LBUTTONDOWN) return false;
            try
            {
                ReleaseCapture();
                const int WM_NCLBUTTONDOWN = 0xA1;
                SendMessage(f.Handle, WM_NCLBUTTONDOWN, hit, IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
            return true;
        }
    }

    /// <summary>Höhe der Titel-/Schnellzugriffsleiste bei 100 % – kompakt wie die Titelleiste von Office unter Windows 11.</summary>
    internal const int KopfHoeheLogisch = 26;

    /// <summary>Feste, DPI-skalierte Höhe des Programmkopfs; Symbole, Titel und Fensterknöpfe stehen vertikal mittig.</summary>
    private void KopfHoeheSetzen()
    {
        var h = LogicalToDeviceUnits(KopfHoeheLogisch);
        _schnell.AutoSize = false;
        _schnell.Height = h;
        foreach (ToolStripItem i in _schnell.Items)
        {
            if (i is ToolStripSeparator) i.Margin = new Padding(LogicalToDeviceUnits(2), LogicalToDeviceUnits(4), LogicalToDeviceUnits(2), LogicalToDeviceUnits(4));
            else if (i.Alignment == ToolStripItemAlignment.Left) i.Margin = new Padding(0, 0, LogicalToDeviceUnits(1), 0);
        }
        var groesse = new Size(LogicalToDeviceUnits(44), h);
        foreach (var k in new[] { _knopfZu, _knopfMax, _knopfMin })
        {
            k.Size = groesse;
            var art = (int)k.Tag!;
            var alt = k.Image;
            k.Image = KnopfBild(art, false, groesse);
            alt?.Dispose();
        }
        _schnell.PerformLayout();
        _schnell.Invalidate();
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
        // kein sichtbarer Rahmen – auch nicht im Fenstermodus (z. B. auf kleineren Bildschirmen); die Größe ändert sich
        // trotzdem an den Kanten (unsichtbarer Greifbereich über den Nachrichtenfilter)
        if (Padding != Padding.Empty) Padding = Padding.Empty;
        if (!Equals(_knopfMax.Tag, max ? 3 : 1) || _knopfMax.Image is null)
        {
            var alt = _knopfMax.Image;
            _knopfMax.Image = KnopfBild(max ? 3 : 1, false, _knopfMax.Size);
            _knopfMax.Tag = max ? 3 : 1;
            alt?.Dispose();
        }
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

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NcCalcSizeParams { public Rect R0, R1, R2; public IntPtr Pos; }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr nach, int x, int y, int cx, int cy, uint flags);

    /// <summary>Breite des (unsichtbaren) Systemrahmens – um so viel ragt ein maximiertes Fenster über den Bildschirm hinaus.</summary>
    private static int Rahmendicke => GetSystemMetrics(33 /* SM_CYFRAME */) + GetSystemMetrics(92 /* SM_CXPADDEDBORDER */);

    /// <summary>Nach dem Erzeugen des Fensters: Nicht-Client-Bereich neu berechnen lassen (aus OnHandleCreated aufgerufen).</summary>
    private void RahmenAnwenden()
    {
        if (!NativerRahmen) return;
        // neuen Nicht-Client-Bereich sofort berechnen lassen (ohne Titelleiste)
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_FRAMECHANGED = 0x20;
        try { SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84, WM_NCCALCSIZE = 0x83;
        if (NativerRahmen && m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Systemrahmen links, rechts und unten behalten (Schatten, Greifränder, Snap), die Titelleiste dem Programmkopf geben
            var vorher = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
            base.WndProc(ref m);
            var p = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
            p.R0.Top = vorher.R0.Top + (WindowState == FormWindowState.Maximized ? Rahmendicke : 0);
            Marshal.StructureToPtr(p, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || WindowState != FormWindowState.Normal) return;
        var pt = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        if (NativerRahmen)
        {
            // Seiten und unten erledigt der Systemrahmen; oben fehlt er – dort ein schmaler Greifbereich im Programmkopf
            if ((int)m.Result == 1 && pt.Y < 4) m.Result = pt.X < 12 ? 13 : pt.X >= ClientSize.Width - 12 ? 14 : 12;
            return;
        }
        // rahmenlos (Wine): Größe an den Rändern ändern
        var l = pt.X < Rand; var r = pt.X >= ClientSize.Width - Rand;
        var o = pt.Y < 3; var u = pt.Y >= ClientSize.Height - Rand;
        int? hit = (o, u, l, r) switch
        {
            (true, _, true, _) => 13, (true, _, _, true) => 14, (_, true, true, _) => 16, (_, true, _, true) => 17,
            (true, _, _, _) => 12, (_, true, _, _) => 15, (_, _, true, _) => 10, (_, _, _, true) => 11, _ => null,
        };
        if (hit is int h) m.Result = h;
    }

    /// <summary>Schlanke weiße Fensterknöpfe wie Windows 11 / Vorbild 2026 (0 = minimieren, 1 = maximieren, 2 = schließen, 3 = wiederherstellen).</summary>
    private static Bitmap KnopfBild(int art, bool dunkel, Size groesse)
    {
        var bmp = new Bitmap(Math.Max(1, groesse.Width), Math.Max(1, groesse.Height));
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = art == 2 ? System.Drawing.Drawing2D.SmoothingMode.AntiAlias : System.Drawing.Drawing2D.SmoothingMode.None;
        // Symbolgröße mit der Knopfhöhe skaliert (bei 100 %: 10 px wie Windows 11)
        var e = Math.Max(4, (int)Math.Round(groesse.Height * 5 / 26.0));
        using var p = new Pen(dunkel ? Color.FromArgb(40, 40, 40) : Color.White, Math.Max(1f, groesse.Height / 26f));
        int cx = groesse.Width / 2, cy = groesse.Height / 2;
        var d = Math.Max(2, e * 2 / 5);
        switch (art)
        {
            case 0: g.DrawLine(p, cx - e, cy, cx + e, cy); break;
            case 1: g.DrawRectangle(p, cx - e, cy - e, 2 * e, 2 * e); break;
            case 2: g.DrawLine(p, cx - e, cy - e, cx + e, cy + e); g.DrawLine(p, cx + e, cy - e, cx - e, cy + e); break;
            case 3:
                g.DrawRectangle(p, cx - e, cy - e + d, 2 * e - d, 2 * e - d);
                g.DrawLine(p, cx - e + d, cy - e, cx + e, cy - e);
                g.DrawLine(p, cx + e, cy - e, cx + e, cy + e - d);
                break;
        }
        return bmp;
    }
}
