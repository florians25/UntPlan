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

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_NCPAINT = 0x85;
            if (m.Msg != WM_NCPAINT || !OperatingSystem.IsWindows()) return;
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
                }
                finally { ReleaseDC(m.HWnd, dc); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }
}
