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
        Aktualisieren(art);
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
            Aktualisieren(AenderungsArt.Alles);
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
        if (FormBorderStyle != FormBorderStyle.None && WindowState != FormWindowState.Minimized)
        {
            // Unterklasse wünscht einen Systemrahmen (z. B. Werkzeugfenster)
            _titel.Visible = false;
            Padding = Padding.Empty;
        }
        Ui.SucheAktivieren(this);
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
            BackColor = Color.FromArgb(246, 248, 251),
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
