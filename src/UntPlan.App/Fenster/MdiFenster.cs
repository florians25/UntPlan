using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>Basis aller Arbeitsfenster im Hauptfenster (MDI). Reagiert auf Änderungen der Schuldatei.</summary>
public class MdiFenster : Form
{
    private bool _eigeneAenderung;
    private Dokument? _doc;

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
        Anmelden();
        AppZustand.DokumentGewechselt += DokumentGewechselt;
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
