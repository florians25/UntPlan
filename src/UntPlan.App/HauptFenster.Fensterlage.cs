using UntPlan.App.Fenster;
using UntPlan.Core;

namespace UntPlan.App;

/// <summary>
/// Arbeitsfenster bleiben im sichtbaren MDI-Arbeitsbereich (unter Programmkopf und Menüband): beim Öffnen, Verschieben und
/// Anordnen (siehe <see cref="MdiFenster"/>), beim Wiederherstellen gespeicherter Fensteranordnungen und wenn der Arbeitsbereich
/// kleiner wird (Hauptfenster verkleinert, Menüband höher, anderer Bildschirm bzw. andere DPI-Skalierung).
/// </summary>
public sealed partial class HauptFenster
{
    private MdiClient? MdiArbeitsbereich => Controls.OfType<MdiClient>().FirstOrDefault();

    private void MdiLageVorbereiten()
    {
        if (MdiArbeitsbereich is not { } mdi) return;
        MdiRand.Anmelden(mdi);
        // unsichtbares Hauptmenü: ohne es setzt Windows Forms für den MDI-Container ein leeres Windows-Menü, in das Windows beim
        // Maximieren eines Arbeitsfensters dessen Symbol und Fensterknöpfe als graue Menüzeile über den Programmkopf legt
        if (MainMenuStrip is null)
        {
            var menue = new MenuStrip { Visible = false, AllowMerge = false };
            Controls.Add(menue);
            MainMenuStrip = menue;
        }
        // erst nach dem Neuanordnen der Leisten klemmen (Größe steht dann fest)
        mdi.Resize += (_, _) => { if (IsHandleCreated && WindowState != FormWindowState.Minimized) BeginInvoke(MdiFensterKlemmen); };
    }

    /// <summary>Alle Arbeitsfenster (im Fenstermodus) vollständig in den MDI-Arbeitsbereich holen.</summary>
    internal void MdiFensterKlemmen()
    {
        if (IsDisposed || WindowState == FormWindowState.Minimized) return;
        foreach (var f in MdiChildren)
        {
            if (f is MdiFenster m) m.InArbeitsbereichKlemmen();
            else if (f.WindowState == FormWindowState.Normal && MdiArbeitsbereich is { } mdi && Fensterlage.BereichGueltig(new Rectangle(Point.Empty, mdi.ClientSize)))
            {
                var r = Fensterlage.MdiFensterErreichbar(f.Bounds, mdi.ClientSize, Steuerelemente.FensterTitel.Hoehe);
                if (r != f.Bounds) f.Bounds = r;
            }
        }
    }

    /// <summary>Gespeicherte Lage eines Arbeitsfensters an den aktuellen MDI-Arbeitsbereich anpassen.</summary>
    private Rectangle GespeicherteLage(FensterPosition p)
    {
        var r = new Rectangle(p.X, p.Y, Math.Max(200, p.B), Math.Max(120, p.H));
        return MdiArbeitsbereich is { } mdi && Fensterlage.BereichGueltig(new Rectangle(Point.Empty, mdi.ClientSize)) ? Fensterlage.MdiFenster(r, mdi.ClientSize, true) : r;
    }

    /// <summary>
    /// Rand des MDI-Arbeitsbereichs wie im Vorbild (gemessen): flach statt 3D – außen (213,213,213), innen (245,249,247), je 1 Pixel.
    /// Windows zeichnet den Nicht-Client-Bereich (Rand, Bildlaufleisten) zuerst, danach wird nur der 2 Pixel breite Rand übermalt.
    /// </summary>
    private sealed class MdiRand : NativeWindow
    {
        private readonly MdiClient _mdi;
        private MdiRand(MdiClient mdi)
        {
            _mdi = mdi;
            if (mdi.IsHandleCreated) AssignHandle(mdi.Handle);
            mdi.HandleCreated += (_, _) => { if (Handle == IntPtr.Zero) AssignHandle(mdi.Handle); };
            mdi.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        public static void Anmelden(MdiClient mdi) => _ = new MdiRand(mdi);

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SCROLLBARINFO
        {
            public int cbSize;
            public RECT rcScrollBar;
            public int dxyLineButton, xyThumbTop, xyThumbBottom, reserved;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 6)] public int[] rgstate;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr hwnd, int idObject, ref SCROLLBARINFO psbi);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

        /// <summary>Farben der Rollbalken des Arbeitsbereichs wie im Vorbild (Bild 308): flach, Rahmen 188, Knöpfe und Schieber hell.</summary>
        private static readonly Color RollRand = Color.FromArgb(188, 188, 188), RollHell = Color.FromArgb(245, 249, 247),
            RollSpur = Color.FromArgb(229, 239, 235), RollPfeil = Color.FromArgb(119, 119, 119);

        /// <summary>
        /// Rollbalken des Arbeitsbereichs (erscheinen, wenn Fenster über ihn hinausragen) flach wie im Vorbild zeichnen: Spur in der
        /// Fensterfarbe, Pfeilknöpfe und Schieber hell mit grauem Rand, Pfeile als gefüllte Dreiecke (7 × 4 Pixel).
        /// </summary>
        private void RollbalkenZeichnen(Graphics g)
        {
            if (!GetWindowRect(_mdi.Handle, out var fenster)) return;
            var senkrecht = false;
            foreach (var (id, hoch) in new[] { (unchecked((int)0xFFFFFFFB), true), (unchecked((int)0xFFFFFFFA), false) })
            {
                var info = new SCROLLBARINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SCROLLBARINFO>() };
                if (!GetScrollBarInfo(_mdi.Handle, id, ref info) || (info.rgstate[0] & 0x8000) != 0) continue;
                var r = Rectangle.FromLTRB(info.rcScrollBar.Left - fenster.Left, info.rcScrollBar.Top - fenster.Top, info.rcScrollBar.Right - fenster.Left, info.rcScrollBar.Bottom - fenster.Top);
                if (r.Width <= 0 || r.Height <= 0) continue;
                if (hoch) senkrecht = true;
                using (var b = new SolidBrush(RollSpur)) g.FillRectangle(b, r);
                var knopf = Math.Max(1, info.dxyLineButton);
                var eins = hoch ? new Rectangle(r.X, r.Y, r.Width, knopf) : new Rectangle(r.X, r.Y, knopf, r.Height);
                var zwei = hoch ? new Rectangle(r.X, r.Bottom - knopf, r.Width, knopf) : new Rectangle(r.Right - knopf, r.Y, knopf, r.Height);
                var schieber = hoch ? Rectangle.FromLTRB(r.X, r.Y + info.xyThumbTop - 1, r.Right, r.Y + info.xyThumbBottom)
                    : Rectangle.FromLTRB(r.X + info.xyThumbTop - 1, r.Y, r.X + info.xyThumbBottom, r.Bottom);
                foreach (var k in new[] { eins, zwei, schieber })
                {
                    if (k.Width <= 1 || k.Height <= 1) continue;
                    using (var b = new SolidBrush(RollHell)) g.FillRectangle(b, k);
                    using (var p = new Pen(RollRand)) g.DrawRectangle(p, k.X, k.Y, k.Width - 1, k.Height - 1);
                }
                Dreieck(g, eins, hoch ? 0 : 2);
                Dreieck(g, zwei, hoch ? 1 : 3);
            }
            // Ecke zwischen beiden Rollbalken
            if (senkrecht && (GetWindowLongMdi(_mdi.Handle) & 0x100000) != 0)
            {
                var info = new SCROLLBARINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SCROLLBARINFO>() };
                if (GetScrollBarInfo(_mdi.Handle, unchecked((int)0xFFFFFFFB), ref info))
                    using (var b = new SolidBrush(RollSpur))
                        g.FillRectangle(b, info.rcScrollBar.Left - fenster.Left, info.rcScrollBar.Bottom - fenster.Top, info.rcScrollBar.Right - info.rcScrollBar.Left, info.dxyLineButton);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLongMdi(IntPtr hwnd, int index = -16);

        /// <summary>Gefülltes Dreieck 7 × 4 Pixel mittig im Knopf: 0 hoch, 1 runter, 2 links, 3 rechts.</summary>
        private static void Dreieck(Graphics g, Rectangle k, int richtung)
        {
            using var b = new SolidBrush(RollPfeil);
            var mx = k.X + (k.Width - 1) / 2;
            var my = k.Y + (k.Height - 1) / 2;
            for (var i = 0; i < 4; i++)
            {
                switch (richtung)
                {
                    case 0: g.FillRectangle(b, mx - i, my - 1 + i, 2 * i + 1, 1); break;
                    case 1: g.FillRectangle(b, mx - 3 + i, my - 1 + i, 7 - 2 * i, 1); break;
                    case 2: g.FillRectangle(b, mx - 1 + i, my - i, 1, 2 * i + 1); break;
                    default: g.FillRectangle(b, mx - 1 + i, my - 3 + i, 1, 7 - 2 * i); break;
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_NCPAINT = 0x85, WM_VSCROLL = 0x115, WM_HSCROLL = 0x114, WM_NCMOUSEMOVE = 0xA0, WM_NCLBUTTONUP = 0xA2, WM_NCMOUSELEAVE = 0x2A2, WM_SIZE = 0x05;
            if (!OperatingSystem.IsWindows()) return;
            if (m.Msg is WM_VSCROLL or WM_HSCROLL or WM_NCMOUSEMOVE or WM_NCLBUTTONUP or WM_NCMOUSELEAVE or WM_SIZE)
            {
                try
                {
                    var dc2 = GetWindowDC(m.HWnd);
                    if (dc2 == IntPtr.Zero) return;
                    try { using var g2 = Graphics.FromHdc(dc2); RollbalkenZeichnen(g2); }
                    finally { ReleaseDC(m.HWnd, dc2); }
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
                return;
            }
            if (m.Msg != WM_NCPAINT) return;
            try
            {
                var dc = GetWindowDC(m.HWnd);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using var g = Graphics.FromHdc(dc);
                    var w = _mdi.Width; var h = _mdi.Height;
                    using var aussen = new Pen(Color.FromArgb(213, 213, 213));
                    using var innen = new Pen(Color.FromArgb(245, 249, 247));
                    g.DrawRectangle(aussen, 0, 0, w - 1, h - 1);
                    g.DrawRectangle(innen, 1, 1, w - 3, h - 3);
                    RollbalkenZeichnen(g);
                }
                finally { ReleaseDC(m.HWnd, dc); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }
}
