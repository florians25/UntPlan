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
    /// <summary>Vorbild: Arbeitsfenster mit 1 Pixel Rand, Inhalt (Werkzeugleiste, Fläche) unmittelbar daneben, kein farbiger Rahmen.</summary>
    private const int Rahmen = 1;
    /// <summary>Unsichtbarer Greifbereich zum Ändern der Größe an den Kanten.</summary>
    private const int Greifbereich = 5;

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
        // wie alle Fenster: das UntPlan-Logo (die eigene Titelleiste zeichnet es oben links)
        Icon = Symbole.Logo;
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
        using var linie = new Pen(RandFarbe(_titel.Aktiv));
        e.Graphics.DrawRectangle(linie, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>
    /// Fensterrand wie im Vorbild (gemessen): aktiv (81,132,111) – etwas dunkler als die Titelleiste –, inaktiv neutral grau (131,131,131).
    /// </summary>
    internal static Color RandFarbe(bool aktiv)
    {
        if (!Ui.Vertretungsmodus) return aktiv ? Color.FromArgb((int)(Ui.Titel.R * .9), (int)(Ui.Titel.G * .88), (int)(Ui.Titel.B * .89)) : Color.FromArgb(131, 131, 131);
        return aktiv ? Color.FromArgb(81, 132, 111) : Color.FromArgb(131, 131, 131);
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
        const int r = Greifbereich;
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
            AktualisierenGemessen(art);
            return;
        }
        if (_ausstehend is null)
            BeginInvoke(() =>
            {
                var a = _ausstehend;
                _ausstehend = null;
                if (!IsDisposed && a is AenderungsArt x) AktualisierenGemessen(x);
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

    /// <summary>Aktualisieren mit Zeitmessung (Leistungslauf).</summary>
    private void AktualisierenGemessen(AenderungsArt art)
    {
        using var _m = Core.Messung.Start($"Aktualisieren {GetType().Name}");
        // Zeile/Spalte und Bildlauf aller Tabellen merken: baut das Fenster seine Tabelle neu auf (Rows.Clear) und springt dabei an
        // den Anfang, wird derselbe Datensatz wieder aktuell – nach Speichern, Rückgängig bzw. Änderungen in verknüpften Fenstern
        var staende = Steuerelemente.Tabellenbedienung.StandMerken(this);
        Aktualisieren(art);
        Steuerelemente.Tabellenbedienung.StandWiederherstellen(staende);
    }

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
        // wie im Vorbild (Absenzen, Vertretungen, Kalender, Vorschlag): Listen ohne Zeilenköpfe, die aktuelle Zeile zeigt ihre erste Zelle orange
        foreach (var g in tabellen) Ui.ZeilenkoepfeWieVorbild(g);
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
        LeisteWieOriginal();
    }

    /// <summary>
    /// Sichtbare Symbole der Werkzeugleiste in der Reihenfolge der Original-Symbolübersicht (null = Leiste unverändert). Alle übrigen
    /// eigenen Befehle (auch „Spalten“) bleiben über den Überlaufpfeil » am rechten Rand der Leiste erreichbar.
    /// </summary>
    protected ToolStripItem?[]? OriginalLeiste { get; set; }

    /// <summary>„Normalform anzeigen“: Fensterrahmen an die Größe der Tabelle anpassen (sichtbare Spalten und Zeilen, im Arbeitsbereich).</summary>
    protected void NormalformAnzeigen(DataGridView g)
    {
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width) + (g.RowHeadersVisible ? g.RowHeadersWidth : 0);
        var zeilen = g.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Sum(r => r.Height) + g.ColumnHeadersHeight;
        var rand = new Size(Width - g.ClientSize.Width, Height - g.ClientSize.Height);
        var arbeit = Parent?.ClientSize ?? new Size(1600, 900);
        Size = new Size(Math.Clamp(spalten + rand.Width + SystemInformation.VerticalScrollBarWidth + 2, 300, Math.Max(300, arbeit.Width - Left)),
            Math.Clamp(zeilen + rand.Height + 4, 200, Math.Max(200, arbeit.Height - Top)));
    }

    private void LeisteWieOriginal()
    {
        if (OriginalLeiste is not { Length: > 0 } sichtbar || Controls.OfType<ToolStrip>().FirstOrDefault(t => t is not MenuStrip && t.Dock == DockStyle.Top) is not { } leiste) return;
        leiste.SuspendLayout();
        var rest = leiste.Items.Cast<ToolStripItem>().Where(i => !sichtbar.Contains(i) && i is not ToolStripSeparator).ToList();
        foreach (var it in leiste.Items.OfType<ToolStripSeparator>().ToList()) leiste.Items.Remove(it);
        foreach (var it in sichtbar) leiste.Items.Remove(it);
        foreach (var it in rest) leiste.Items.Remove(it);
        // null = Trennstrich an dieser Stelle (wie im Original)
        foreach (var it in sichtbar)
        {
            if (it is null) { leiste.Items.Add(new ToolStripSeparator()); continue; }
            it.Overflow = ToolStripItemOverflow.Never;
            leiste.Items.Add(it);
        }
        foreach (var it in rest)
        {
            it.Overflow = ToolStripItemOverflow.Always;
            it.Alignment = ToolStripItemAlignment.Left;
            if (it.DisplayStyle == ToolStripItemDisplayStyle.Image && !string.IsNullOrEmpty(it.Text)) it.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            leiste.Items.Add(it);
        }
        leiste.ResumeLayout();
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
        var ts = new Fensterleiste
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Font = Ui.Schrift,
            ImageScalingSize = new Size(16, 16),
            BackColor = Ui.Leiste,
        };
        ts.Renderer = new Fensterleiste.Darstellung();
        ts.EinzugAnwenden();
        return ts;
    }

    /// <summary>
    /// Werkzeugleiste der Arbeitsfenster. Im Vertretungsmodus wie auf den Vorbild-Bildschirmfotos (100 %) gemessen: die helle Leiste
    /// (240,246,243) beginnt und endet 7 Pixel innerhalb des Fensters, daneben liegt die Fensterfläche (229,239,235); ohne Randlinie.
    /// </summary>
    protected sealed class Fensterleiste : ToolStrip
    {
        /// <summary>Abstand der hellen Leiste zum Fensterrand (beide Modi, wie im Vorbild).</summary>
        public const int Einzug = 7;
        /// <summary>false: Leiste ohne Einzug (z. B. Datumszeile der Stundenpläne, die auf der Fensterfläche liegt).</summary>
        public bool Einziehen { get; set; } = true;
        /// <summary>Innenabstand ohne Einzug.</summary>
        public Padding Grundabstand { get; set; } = new(2, 1, 2, 1);

        public Fensterleiste() => AppZustand.ModusGewechselt += EinzugAnwenden;

        protected override void Dispose(bool disposing)
        {
            if (disposing) AppZustand.ModusGewechselt -= EinzugAnwenden;
            base.Dispose(disposing);
        }

        public void EinzugAnwenden()
        {
            if (IsDisposed) return;
            var e = Einziehen ? Einzug : 0;
            // Vorbild (gemessen): erstes Element 10 Pixel innerhalb der hellen Leiste (18 Pixel vom Fensterrand)
            var soll = new Padding(Grundabstand.Left + e + (Einziehen ? 8 : 0), Grundabstand.Top, Grundabstand.Right + e, Grundabstand.Bottom);
            if (Padding != soll) Padding = soll;
            Invalidate();
        }

        /// <summary>Systemdarstellung mit eingerückter Fläche und ohne Randlinie (beide Modi wie im Vorbild der Vertretungsplanung).</summary>
        public sealed class Darstellung : ToolStripSystemRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste { Einziehen: true } l) { base.OnRenderToolStripBackground(e); return; }
                using (var aussen = new SolidBrush(Ui.Kopf)) e.Graphics.FillRectangle(aussen, 0, 0, l.Width, l.Height);
                using var innen = new SolidBrush(l.BackColor);
                e.Graphics.FillRectangle(innen, Einzug, 0, Math.Max(0, l.Width - 2 * Einzug), l.Height);
            }

            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
            {
                if (!GrauesSymbol.Zeichnen(e)) base.OnRenderItemImage(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is Fensterleiste) return;
                base.OnRenderToolStripBorder(e);
            }

            /// <summary>Überlaufknopf im Vertretungsmodus wie in der Vorlage: kleines Dreieck (5 × 3 Pixel) unten rechts statt „»“.</summary>
            protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste) { base.OnRenderOverflowButtonBackground(e); return; }
                var it = e.Item;
                var l = (Fensterleiste)e.ToolStrip;
                // die helle Leiste endet wie im Vorbild 7 Pixel vor dem Fensterrand, auch unter dem Überlaufknopf
                var hellBis = l.Einziehen ? l.Width - Einzug - it.Bounds.X : it.Width;
                using (var aussen = new SolidBrush(Ui.Kopf)) e.Graphics.FillRectangle(aussen, 0, 0, it.Width, it.Height);
                using (var b = new SolidBrush(it.Selected || it.Pressed ? Ui.Hover : e.ToolStrip.BackColor)) e.Graphics.FillRectangle(b, 0, 0, Math.Min(it.Width, hellBis), it.Height);
                // Vorbild (gemessen): 5 × 3 Pixel in (119,119,119) mit weichen Kanten, Mitte 14 Pixel vor dem Fensterrand
                var m = new Point(l.Einziehen ? l.Width - 14 - it.Bounds.X : it.Width / 2, it.Height - 9);
                var grund = it.Selected || it.Pressed ? Ui.Hover : e.ToolStrip.BackColor;
                Maske(e.Graphics, Dreieck, m.X - 2, m.Y, grund);
                // Vorbild (Bild 312): liegen Befehle im Überlauf, steht darüber „»“ (8 × 5 Pixel, 4 Pixel unter der Leistenoberkante)
                if (it is ToolStripOverflowButton { HasDropDownItems: true }) Maske(e.Graphics, Doppelpfeil, m.X - 4, 4, grund);
            }

            /// <summary>Aufklapppfeil (5 × 3 Pixel, weiche Kanten) wie im Vorbild, gemessen in Bild 308.</summary>
            private static readonly float[,] Dreieck = { { .55f, 1, 1, 1, .55f }, { 0, .74f, 1, .74f, 0 }, { 0, .07f, .69f, .07f, 0 } };

            /// <summary>„»“ des Überlaufknopfs (8 × 5 Pixel), gemessen in Bild 312.</summary>
            private static readonly float[,] Doppelpfeil =
            {
                { .92f, .45f, 0, 0, .99f, .38f, 0, 0 },
                { .15f, .76f, .6f, 0, .21f, .83f, .53f, 0 },
                { 0, 0, .85f, .76f, .13f, .12f, .89f, .69f },
                { .15f, .77f, .6f, 0, .22f, .83f, .54f, 0 },
                { .92f, .45f, 0, 0, .99f, .38f, 0, 0 },
            };

            /// <summary>Deckungsmaske in Grau (119,119,119) über den Grund zeichnen.</summary>
            private static void Maske(Graphics g, float[,] maske, int x0, int y0, Color grund)
            {
                for (var y = 0; y < maske.GetLength(0); y++)
                    for (var x = 0; x < maske.GetLength(1); x++)
                    {
                        var a = maske[y, x];
                        if (a <= 0) continue;
                        using var p = new SolidBrush(Color.FromArgb((int)(grund.R + (119 - grund.R) * a), (int)(grund.G + (119 - grund.G) * a), (int)(grund.B + (119 - grund.B) * a)));
                        g.FillRectangle(p, x0 + x, y0 + y, 1, 1);
                    }
            }

            /// <summary>Umschalter eingedrückt wie im Vorbild (Bild 308): Fläche 20 × 20 Pixel in kräftiger Modusfarbe, ohne Rand.</summary>
            public static Color Eingedrueckt => Ui.Vertretungsmodus ? Color.FromArgb(104, 174, 146) : Color.FromArgb(120, 172, 220);

            /// <summary>Fläche hinter einem Knopf: eingedrückt, gedrückt oder beim Überfahren (Modusfarben), sonst nichts.</summary>
            private static void KnopfGrund(ToolStripItemRenderEventArgs e, bool eingedrueckt)
            {
                if (e.ToolStrip is not Fensterleiste) return;
                var it = e.Item;
                if (!it.Enabled || (!eingedrueckt && !it.Selected && !it.Pressed)) return;
                var w = it.DisplayStyle == ToolStripItemDisplayStyle.Image && it is not ToolStripDropDownButton ? Math.Min(20, it.Width) : it.Width - 2;
                var r = new Rectangle((it.Width - w) / 2, 0, w, Math.Min(20, it.Height));
                var g = e.Graphics;
                if (eingedrueckt)
                {
                    using var b = new SolidBrush(it.Selected ? Ui.HoverRand : Eingedrueckt);
                    g.FillRectangle(b, r);
                    return;
                }
                using (var b = new SolidBrush(it.Pressed ? Ui.MenuebandGedrueckt : Ui.Hover)) g.FillRectangle(b, r);
                using var p = new Pen(Ui.HoverRand);
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste) { base.OnRenderButtonBackground(e); return; }
                KnopfGrund(e, e.Item is ToolStripButton { Checked: true });
            }

            protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste) { base.OnRenderDropDownButtonBackground(e); return; }
                KnopfGrund(e, false);
            }

            protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste || e.Item is not ToolStripSplitButton sb) { base.OnRenderSplitButtonBackground(e); return; }
                KnopfGrund(e, false);
                DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds, Color.Black, ArrowDirection.Down));
            }

            /// <summary>Aufklapppfeile der Leiste wie im Vorbild: kleines graues Dreieck statt des schwarzen Systempfeils.</summary>
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                if (e.Item?.Owner is not Fensterleiste leiste || e.Direction != ArrowDirection.Down) { base.OnRenderArrow(e); return; }
                var r = e.ArrowRectangle;
                var grund = e.Item.Selected || e.Item.Pressed ? Ui.Hover : leiste.BackColor;
                Maske(e.Graphics, Dreieck, r.X + (r.Width - 5) / 2, r.Y + (r.Height - 3) / 2, grund);
            }

            /// <summary>Trennstriche wie im Vorbild: 1 Pixel hellgrau (213,213,213), 1 Pixel unter der Oberkante bis 3 Pixel über der Unterkante.</summary>
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                if (e.ToolStrip is not Fensterleiste || !e.Vertical) { base.OnRenderSeparator(e); return; }
                var by = e.Item.Bounds.Y;
                using var p = new Pen(Color.FromArgb(213, 213, 213));
                var x = e.Item.Width / 2;
                e.Graphics.DrawLine(p, x, Math.Max(0, 1 - by), x, Math.Min(e.Item.Height - 1, e.ToolStrip.Height - 4 - by));
            }
        }
    }

    protected static ToolStripButton Knopf(string text, string symbol, Action klick, bool nurSymbol = true)
    {
        var b = new ToolStripButton(text, Symbole.Get(symbol, 16)) { DisplayStyle = nurSymbol ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText, ToolTipText = text };
        b.Click += (_, _) => klick();
        return b;
    }
}
