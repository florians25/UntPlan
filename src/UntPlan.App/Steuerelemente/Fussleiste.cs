using System.Runtime.InteropServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Fußleiste der Arbeitsfenster mit der Ansichts- bzw. Formatauswahl unten rechts wie im Vorbild („Kla1 - Klasse 1“): die Auswahl hat
/// Abstand zum unteren und rechten Fensterrand (nie abgeschnitten), rechts unten bleibt der Größengriff frei (gezeichnet, ziehbar).
/// </summary>
public sealed class Fussleiste : Panel
{
    /// <summary>Höhe der Leiste (Auswahl mit Abstand oben und unten).</summary>
    public const int Hoehe = 34;
    /// <summary>Breite des freien Bereichs für den Größengriff rechts.</summary>
    public const int GriffBreite = 18;
    /// <summary>Abstand der Auswahl zum unteren Rand.</summary>
    public const int AbstandUnten = 6;

    private Control? _rechts;
    private static readonly Font LesbareSchrift = new("Segoe UI", 9.5f);
    private static IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(k => new[] { k }.Concat(Alle(k)));

    public Fussleiste()
    {
        Dock = DockStyle.Bottom;
        Height = Hoehe;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Steuerelement unten rechts (Format- bzw. Ansichtsauswahl, ggf. mit Beschriftung).</summary>
    public Control? Rechts
    {
        get => _rechts;
        set
        {
            if (_rechts != null) Controls.Remove(_rechts);
            _rechts = value;
            if (value is null) return;
            value.Dock = DockStyle.None;
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            // Ansichts-/Formatauswahl gut lesbar: etwas größere, dunkle Schrift auf Weiß, Beschriftungen dunkel
            foreach (var c in new[] { value }.Concat(Alle(value)))
            {
                if (c is ComboBox cb) { cb.Font = LesbareSchrift; cb.ForeColor = Color.FromArgb(20, 28, 36); cb.BackColor = Color.White; }
                else if (c is Label l) { l.Font = LesbareSchrift; l.ForeColor = Color.FromArgb(40, 48, 56); }
            }
            Controls.Add(value);
            value.SizeChanged += (_, _) => Anordnen();
            Anordnen();
        }
    }

    /// <summary>Gewünschte Breite einer Auswahlliste rechts (0 = Breite nicht ändern); bei schmalen Fenstern wird sie verkleinert.</summary>
    public int Wunschbreite { get; set; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Anordnen();
    }

    /// <summary>Rechte Auswahl ausrichten: rechts mit Platz für den Größengriff, unten mit festem Abstand.</summary>
    public void Anordnen()
    {
        if (_rechts is null) return;
        if (_rechts is ComboBox cb && Wunschbreite > 0)
        {
            var b = Math.Max(60, Math.Min(Wunschbreite, ClientSize.Width - GriffBreite - 8));
            if (cb.Width != b) cb.Width = b;
        }
        var x = Math.Max(2, ClientSize.Width - _rechts.Width - GriffBreite - 4);
        var y = Math.Max(1, ClientSize.Height - _rechts.Height - AbstandUnten);
        if (_rechts.Location != new Point(x, y)) _rechts.Location = new Point(x, y);
    }

    /// <summary>Unterkante der rechten Auswahl liegt vollständig in der Leiste und mit Abstand zum Rand (Selbsttest).</summary>
    public bool AuswahlFrei => _rechts is null || (_rechts.Bottom <= ClientSize.Height - 3 && _rechts.Right <= ClientSize.Width - GriffBreite + 2 && _rechts.Top >= 0);

    private Rectangle Griff => new(ClientSize.Width - 16, ClientSize.Height - 16, 16, 16);

    private bool GriffAktiv => FindForm() is { WindowState: FormWindowState.Normal } f && (f.FormBorderStyle is FormBorderStyle.None or FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!GriffAktiv) return;
        // Größengriff aus Punkten wie im Vorbild
        using var dunkel = new SolidBrush(Color.FromArgb(150, 150, 150));
        using var hell = new SolidBrush(Color.White);
        var g = Griff;
        for (var i = 0; i < 3; i++)
            for (var j = 0; j <= i; j++)
            {
                var x = g.Right - 4 - (i - j) * 4;
                var y = g.Bottom - 4 - j * 4;
                e.Graphics.FillRectangle(hell, x + 1, y + 1, 2, 2);
                e.Graphics.FillRectangle(dunkel, x, y, 2, 2);
            }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = GriffAktiv && Griff.Contains(e.Location) ? Cursors.SizeNWSE : Cursors.Default;
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !GriffAktiv || !Griff.Contains(e.Location) || FindForm() is not { } f) return;
        try
        {
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1, (IntPtr)17, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTBOTTOMRIGHT
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
