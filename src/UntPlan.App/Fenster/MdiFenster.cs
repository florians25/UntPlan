using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>Basis aller Arbeitsfenster im Hauptfenster (MDI). Reagiert auf Änderungen der Schuldatei.</summary>
public class MdiFenster : Form
{
    private bool _eigeneAenderung;
    private Dokument? _doc;
    private readonly FensterTitel _titel;
    private const int Rahmen = 3;

    /// <summary>Hilfethema für F1.</summary>
    public string HilfeThema { get; protected set; } = "";

    /// <summary>Fensterverknüpfung: das Fenster folgt dem in anderen Fenstern gewählten Element bzw. der gewählten Stunde.</summary>
    public bool Verknuepft { get; set; } = true;

    /// <summary>Knopf „Fenster verknüpfen“ für die Werkzeugleiste.</summary>
    protected ToolStripButton VerknuepfenKnopf() => Verknuepfung.Knopf(Verknuepft, v => Verknuepft = v);

    /// <summary>Eigene Titelleiste (z. B. für die Vorbild-Durchschaltknöpfe der Stundenpläne).</summary>
    protected FensterTitel Titelleiste => _titel;

    protected Dokument Doc => AppZustand.Doc;
    protected Schuldatei D => AppZustand.Doc.Daten;
    protected DatenIndex Ix => AppZustand.Doc.Index;

    public MdiFenster()
    {
        Font = Ui.Schrift;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        ShowIcon = true;
        Icon = SymbolIcon("stundenplan");
        if (AppZustand.Haupt != null) MdiParent = AppZustand.Haupt;
        // eigene, farbige Titelleiste im Stil des aktuellen Modus statt der grauen Systemleiste
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(Rahmen);
        DoubleBuffered = true;
        _titel = new FensterTitel(this);
        Controls.Add(_titel);
        Activated += (_, _) => { _titel.Aktiv = true; _titel.Invalidate(); Invalidate(); };
        Deactivate += (_, _) => { _titel.Aktiv = false; _titel.Invalidate(); Invalidate(); };
        TextChanged += (_, _) => _titel.Invalidate();
        Anmelden();
        AppZustand.DokumentGewechselt += DokumentGewechselt;
        AppZustand.ModusGewechselt += ModusGewechselt;
    }

    private void ModusGewechselt()
    {
        if (IsDisposed) return;
        _titel.Invalidate();
        Invalidate();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        // Titelleiste immer ganz oben andocken (zuletzt im Z-Order = zuerst angedockt)
        if (e.Control != _titel) _titel?.SendToBack();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_titel is null || !_titel.Visible) return;
        using var p = new Pen(FensterTitel.Farbe(_titel.Aktiv), Rahmen * 2);
        e.Graphics.DrawRectangle(p, 0, 0, Width, Height);
        using var linie = new Pen(Color.FromArgb(90, 0, 0, 0));
        e.Graphics.DrawRectangle(linie, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_titel is null) return; // während des Konstruktors
        Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(Rahmen);
        // minimierte Fenster mit Systemrahmen, damit sie im Arbeitsbereich sichtbar bleiben
        var soll = WindowState == FormWindowState.Minimized ? FormBorderStyle.Sizable : FormBorderStyle.None;
        if (FormBorderStyle != soll) BeginInvoke(() => { if (!IsDisposed) { FormBorderStyle = soll; _titel.Visible = soll == FormBorderStyle.None; } });
        _titel.Invalidate();
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_WINDOWPOSCHANGING = 0x46;
        if (m.Msg == WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero) LageKlemmen(m.LParam);
        base.WndProc(ref m);
        // Größe am Rand ändern (WM_NCHITTEST)
        if (m.Msg != 0x84 || WindowState != FormWindowState.Normal || FormBorderStyle != FormBorderStyle.None) return;
        var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
        const int r = Rahmen + 2;
        var links = p.X <= r;
        var rechts = p.X >= ClientSize.Width - r;
        var oben = p.Y <= r;
        var unten = p.Y >= ClientSize.Height - r;
        if (oben && links) m.Result = (IntPtr)13;
        else if (oben && rechts) m.Result = (IntPtr)14;
        else if (unten && links) m.Result = (IntPtr)16;
        else if (unten && rechts) m.Result = (IntPtr)17;
        else if (links) m.Result = (IntPtr)10;
        else if (rechts) m.Result = (IntPtr)11;
        else if (oben) m.Result = (IntPtr)12;
        else if (unten) m.Result = (IntPtr)15;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowPos { public IntPtr Hwnd, Nach; public int X, Y, Cx, Cy; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    /// <summary>Sichtbarer MDI-Arbeitsbereich (Clientfläche unter Menüband und Programmkopf), leer ohne Hauptfenster.</summary>
    internal Size MdiBereich => MdiParent?.Controls.OfType<MdiClient>().FirstOrDefault()?.ClientSize ?? Size.Empty;

    /// <summary>
    /// Jede Lageänderung (Ziehen an der Titelleiste, Größenänderung, Anordnen, gespeicherte Fensteranordnung, Bounds aus dem
    /// Programm): Das Fenster bleibt vollständig im MDI-Arbeitsbereich – die Titelleiste rutscht nie unter das Menüband.
    /// </summary>
    private void LageKlemmen(IntPtr lParam)
    {
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2;
        if (!IsMdiChild || !IsHandleCreated) return;
        try
        {
            if (IsIconic(Handle) || IsZoomed(Handle) || WindowState != FormWindowState.Normal) return;
            var bereich = MdiBereich;
            if (!Fensterlage.BereichGueltig(new Rectangle(Point.Empty, bereich))) return;
            var wp = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowPos>(lParam);
            if ((wp.Flags & (SWP_NOSIZE | SWP_NOMOVE)) == (SWP_NOSIZE | SWP_NOMOVE)) return;
            var pos = (wp.Flags & SWP_NOMOVE) != 0 ? Location : new Point(wp.X, wp.Y);
            var groesse = (wp.Flags & SWP_NOSIZE) != 0 ? Size : new Size(wp.Cx, wp.Cy);
            var r = Fensterlage.MdiFenster(new Rectangle(pos, groesse), bereich, (wp.Flags & SWP_NOSIZE) == 0);
            if (r.Location == pos && r.Size == groesse) return;
            if (r.Location != pos) { wp.X = r.X; wp.Y = r.Y; wp.Flags &= ~SWP_NOMOVE; }
            if (r.Size != groesse) { wp.Cx = r.Width; wp.Cy = r.Height; wp.Flags &= ~SWP_NOSIZE; }
            if ((wp.Flags & SWP_NOMOVE) == 0 && (wp.Flags & SWP_NOSIZE) != 0) { wp.Cx = Width; wp.Cy = Height; }
            if ((wp.Flags & SWP_NOMOVE) != 0) { wp.X = Left; wp.Y = Top; }
            System.Runtime.InteropServices.Marshal.StructureToPtr(wp, lParam, false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Fenster (z. B. nach Verkleinern des Hauptfensters) wieder vollständig in den MDI-Arbeitsbereich holen.</summary>
    internal void InArbeitsbereichKlemmen()
    {
        if (!IsMdiChild || WindowState != FormWindowState.Normal || IsDisposed) return;
        var bereich = MdiBereich;
        if (!Fensterlage.BereichGueltig(new Rectangle(Point.Empty, bereich))) return;
        var r = Fensterlage.MdiFenster(Bounds, bereich, true);
        if (r != Bounds) Bounds = r;
    }

    protected static Icon SymbolIcon(string name)
    {
        using var bmp = Symbole.Get(name, 16).Clone() as Bitmap;
        return Icon.FromHandle(bmp!.GetHicon());
    }

    private void Anmelden()
    {
        if (_doc != null) _doc.Geaendert -= Geaendert;
        _doc = AppZustand.Doc;
        _doc.Geaendert += Geaendert;
    }

    private void DokumentGewechselt()
    {
        Anmelden();
        if (!IsDisposed) Aktualisieren(AenderungsArt.Alles);
    }

    private void Geaendert(AenderungsArt art)
    {
        if (IsDisposed) return;
        if (_eigeneAenderung && !EigeneAenderungenAnzeigen) return;
        AktualisierenSicher(art);
    }

    private AenderungsArt? _ausstehend;

    /// <summary>
    /// Neuaufbau nach einer Änderung. Läuft die Änderung aus einem Tabellenereignis heraus (Zelle verlassen, Wert übernommen)
    /// oder betrifft sie ein anderes Fenster, wird der Neuaufbau nachgeholt, sobald die Tabelle ihren Zellwechsel beendet hat
    /// (sonst „Wiedereintrittsaufruf an SetCurrentCellAddressCore“). Mehrere Änderungen werden zusammengefasst.
    /// </summary>
    protected void AktualisierenSicher(AenderungsArt art)
    {
        if (!IsHandleCreated || (ContainsFocus && !TabelleHatFokus(this)))
        {
            Aktualisieren(art);
            return;
        }
        if (_ausstehend is null)
            BeginInvoke(() =>
            {
                var a = _ausstehend;
                _ausstehend = null;
                if (!IsDisposed && a is AenderungsArt x) Aktualisieren(x);
            });
        _ausstehend = _ausstehend is null || _ausstehend == art ? art : AenderungsArt.Alles;
    }

    /// <summary>Aktion nach Abschluss des laufenden Zellwechsels ausführen (für Neuaufbau aus Tabellenereignissen).</summary>
    protected void NachZellwechsel(Action aktion)
    {
        if (IsHandleCreated) BeginInvoke(() => { if (!IsDisposed) aktion(); });
        else aktion();
    }

    private static bool TabelleHatFokus(Control c)
    {
        foreach (Control k in c.Controls)
        {
            if (!k.ContainsFocus) continue;
            if (k is DataGridView || TabelleHatFokus(k)) return true;
        }
        return false;
    }

    /// <summary>Soll das Fenster auch nach eigenen Änderungen neu aufgebaut werden?</summary>
    protected virtual bool EigeneAenderungenAnzeigen => false;

    protected virtual void Aktualisieren(AenderungsArt art) { }

    /// <summary>Änderung mit Rückgängig-Möglichkeit durchführen.</summary>
    protected bool Aendern(string text, AenderungsArt art, Action<Schuldatei> aktion)
    {
        _eigeneAenderung = true;
        try
        {
            Doc.Aendern(text, art, aktion);
            return true;
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            AktualisierenSicher(AenderungsArt.Alles);
            return false;
        }
        finally
        {
            _eigeneAenderung = false;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Startlage (kaskadierend bzw. aus dem Konstruktor) im sichtbaren Arbeitsbereich
        InArbeitsbereichKlemmen();
        if (FormBorderStyle != FormBorderStyle.None && WindowState != FormWindowState.Minimized)
        {
            // Unterklasse wünscht einen Systemrahmen (z. B. Werkzeugfenster)
            _titel.Visible = false;
            Padding = Padding.Empty;
        }
        Ui.SucheAktivieren(this);
        // eigene Tabellenformate für alle Tabellen des Fensters
        var n = 0;
        var tabellen = Tabellen(this).ToList();
        foreach (var g in tabellen) Steuerelemente.TabellenFormate.Aktivieren(g, $"{GetType().Name}/{(string.IsNullOrEmpty(g.Name) ? (n++).ToString() : g.Name)}");
        // Knopf „Spalten“ rechts in der Werkzeugleiste: Spalten ein-/ausblenden und Anordnung für die Haupttabelle
        if (tabellen.Count > 0 && Controls.OfType<ToolStrip>().FirstOrDefault(t => t is not MenuStrip && t.Dock == DockStyle.Top) is { } leiste)
        {
            var haupt = tabellen.OrderByDescending(t => t.Width * t.Height).First();
            var knopf = new ToolStripButton("Spalten", Symbole.Get("rollup"))
            {
                Alignment = ToolStripItemAlignment.Right, ToolTipText = "Spalten ein- und ausblenden, Reihenfolge und Tabellenformate (auch per Rechtsklick auf die Spaltenköpfe)",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            };
            knopf.Click += (_, _) => Steuerelemente.TabellenFormate.MenueZeigen(haupt, leiste.PointToScreen(new Point(knopf.Bounds.Left, knopf.Bounds.Bottom)));
            leiste.Items.Add(knopf);
        }
    }

    private static IEnumerable<DataGridView> Tabellen(Control c)
    {
        foreach (Control x in c.Controls)
        {
            if (x is DataGridView g) yield return g;
            else foreach (var y in Tabellen(x)) yield return y;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
        {
            HilfeFenster.Zeigen(HilfeThema);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_doc != null) _doc.Geaendert -= Geaendert;
        AppZustand.DokumentGewechselt -= DokumentGewechselt;
        AppZustand.ModusGewechselt -= ModusGewechselt;
        base.OnFormClosed(e);
    }

    /// <summary>Platziert das Fenster im MDI-Bereich (kaskadierend).</summary>
    public void Positionieren(Rectangle? r = null)
    {
        if (r.HasValue)
        {
            Bounds = r.Value;
            return;
        }
        var n = AppZustand.Haupt?.MdiChildren.Length ?? 0;
        Location = new Point(10 + (n % 8) * 24, 10 + (n % 8) * 24);
    }

    protected static ToolStrip Werkzeugleiste()
    {
        var ts = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Font = Ui.Schrift,
            ImageScalingSize = new Size(16, 16),
            BackColor = Ui.Leiste,
            Padding = new Padding(2, 1, 2, 1),
        };
        return ts;
    }

    protected static ToolStripButton Knopf(string text, string symbol, Action klick, bool nurSymbol = true)
    {
        var b = new ToolStripButton(text, Symbole.Get(symbol, 16)) { DisplayStyle = nurSymbol ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText, ToolTipText = text };
        b.Click += (_, _) => klick();
        return b;
    }
}
