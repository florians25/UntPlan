using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Druck;

/// <summary>
/// Seitenlayout der Stundenpläne wie im Vorbild: links die Seitenansicht (Blättern, Papierformat), rechts die Druckeinstellungen –
/// „Auswahl“ der Elemente (Alle, Inverse, Elementfilter), „Stundenplangröße an Seitengröße anpassen“, „Unterricht, …“,
/// „Kopplungslegende (Fußnoten)“, „Abkürzungen“ und „Element-Text“; Drucken direkt aus dem Fenster.
/// </summary>
public sealed class StundenplanSeitenlayout : Form
{
    private readonly ElementTyp _typ;
    private readonly StundenplanFormat _format;
    private readonly DateOnly? _woche;
    private List<int> _ids;
    private readonly PictureBox _bild = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, BackColor = Color.FromArgb(160, 170, 185) };
    private readonly ToolStripLabel _seitenText = new();
    private readonly Label _anzahl = new() { Left = 10, Top = 8, Width = 200, Height = 18 };
    private List<Bitmap> _seiten = new();
    private int _seite;

    /// <summary>Druckoptionen dieses Seitenlayouts (bleiben für die Sitzung erhalten).</summary>
    internal static StundenplanDruckOptionen Optionen => StundenplanFormular.Optionen;

    public StundenplanSeitenlayout(ElementTyp typ, IEnumerable<int> ids, StundenplanFormat format, DateOnly? woche)
    {
        _typ = typ;
        _format = format;
        _woche = woche;
        _ids = ids.ToList();
        Text = "Seitenlayout";
        Font = Ui.Schrift;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var leiste = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        leiste.Items.Add(new ToolStripButton("◀") { ToolTipText = "Vorherige Seite" }.Mit(b => b.Click += (_, _) => Blaettern(-1)));
        leiste.Items.Add(new ToolStripButton("▶") { ToolTipText = "Nächste Seite" }.Mit(b => b.Click += (_, _) => Blaettern(1)));
        leiste.Items.Add(_seitenText);
        var papier = new ToolStripDropDownButton("Papier") { ToolTipText = "Papierformat: Hochformat oder Querformat" };
        papier.DropDownItems.Add("Hochformat", null, (_, _) => { StundenplanDruck.Seite.Landscape = false; Neu(); });
        papier.DropDownItems.Add("Querformat", null, (_, _) => { StundenplanDruck.Seite.Landscape = true; Neu(); });
        papier.DropDownItems.Add(new ToolStripSeparator());
        papier.DropDownItems.Add("Seite einrichten…", null, (_, _) => SeiteEinrichten());
        leiste.Items.Add(new ToolStripSeparator());
        leiste.Items.Add(papier);
        leiste.Items.Add(new ToolStripSeparator());
        leiste.Items.Add(new ToolStripButton("Drucken", Symbole.Get("drucken", 16)).Mit(b => b.Click += (_, _) => StundenplanDruck.Drucken(this, _typ, _ids, _format, false, _woche)));

        var rechts = new Panel { Dock = DockStyle.Right, Width = 240, BackColor = Color.FromArgb(232, 240, 250), AutoScroll = true };
        var auswahl = new Button { Text = "Auswahl", Left = 10, Top = 28, Width = 210, Height = 26 };
        auswahl.Click += (_, _) => AuswahlWaehlen();
        rechts.Controls.AddRange(new Control[] { _anzahl, auswahl });
        var y = 66;
        CheckBox Option(string text, bool wert, Action<bool>? setzen)
        {
            var c = new CheckBox { Text = text, Left = 10, Top = y, Width = 220, Height = 34, Checked = wert, Enabled = setzen != null };
            if (setzen != null) c.CheckedChanged += (_, _) => { setzen(c.Checked); Neu(); };
            rechts.Controls.Add(c);
            y += 36;
            return c;
        }
        // das Formular wird immer auf die Seite eingepasst
        Option("Stundenplangröße an Seitengröße anpassen", true, null);
        Option("Unterricht, ...", Optionen.Unterricht, v => Optionen.Unterricht = v);
        Option("Kopplungslegende (Fußnoten)", Optionen.Kopplungslegende, v => Optionen.Kopplungslegende = v);
        Option("Abkürzungen", Optionen.Abkuerzungen, v => Optionen.Abkuerzungen = v);
        Option("Element-Text", Optionen.ElementText, v => Optionen.ElementText = v);

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var schliessen = new Button { Text = "Schließen", Width = 88, Height = 26, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.Cancel };
        unten.Controls.Add(schliessen);
        unten.Resize += (_, _) => schliessen.Location = new Point(unten.Width - 96, 7);
        CancelButton = schliessen;

        Controls.Add(_bild);
        Controls.Add(rechts);
        Controls.Add(unten);
        Controls.Add(leiste);
        FormClosed += (_, _) => { foreach (var b in _seiten) b.Dispose(); };
        Neu();
    }

    /// <summary>Selbsttest: Anzahl der Seiten der Vorschau.</summary>
    internal int Seitenzahl => _seiten.Count;

    /// <summary>Seitenlayout für die angegebenen Elemente öffnen.</summary>
    public static void Zeigen(IWin32Window owner, ElementTyp typ, IEnumerable<int> ids, StundenplanFormat format, DateOnly? woche)
    {
        using var f = new StundenplanSeitenlayout(typ, ids, format, woche);
        f.ShowDialog(owner);
    }

    private void Neu()
    {
        foreach (var b in _seiten) b.Dispose();
        Optionen.UeberschriftProzent = _format.UeberschriftSchriftProzent;
        Optionen.LegendeProzent = _format.LegendeSchriftProzent;
        _seiten = _ids.Count == 0 ? new List<Bitmap>() : StundenplanDruck.Bilder(_typ, _ids, _format, _woche);
        _seite = Math.Clamp(_seite, 0, Math.Max(0, _seiten.Count - 1));
        var alle = Stammdaten.Sortiert(AppZustand.D, _typ).Count();
        _anzahl.Text = $"{Ui.TypName(_typ, true)}: {_ids.Count}/{alle}";
        Anzeigen();
    }

    private void Anzeigen()
    {
        _bild.Image = _seiten.Count > 0 ? _seiten[_seite] : null;
        _seitenText.Text = _seiten.Count > 0 ? $"{_seite + 1}-{_seite + 1} / {_seiten.Count}" : "0 / 0";
    }

    private void Blaettern(int r)
    {
        if (_seiten.Count == 0) return;
        _seite = Math.Clamp(_seite + r, 0, _seiten.Count - 1);
        Anzeigen();
    }

    private void SeiteEinrichten()
    {
        using var dlg = new PageSetupDialog { PageSettings = StundenplanDruck.Seite, EnableMetric = true, AllowPrinter = true };
        try { if (dlg.ShowDialog(this) == DialogResult.OK) Neu(); }
        catch (System.Drawing.Printing.InvalidPrinterException ex) { Ui.Info("Kein Drucker eingerichtet – das Seitenlayout kann erst mit einem Drucker gewählt werden.\n\n" + ex.Message, "Seitenlayout"); }
    }

    private void AuswahlWaehlen()
    {
        if (ElementAuswahlDialog.Zeigen(this, _typ, _ids) is not { } neu) return;
        _ids = neu;
        _seite = 0;
        Neu();
    }
}

/// <summary>
/// Auswahl der zu druckenden Stundenpläne wie im Vorbild: Liste Name/Langname (Mehrfachauswahl mit gedrückter Maustaste bzw.
/// Strg+Klick), „Elementfilter“, Schaltflächen Alle und Inverse, Ok/Abbrechen.
/// </summary>
public static class ElementAuswahlDialog
{
    public static List<int>? Zeigen(IWin32Window owner, ElementTyp typ, IEnumerable<int> vorauswahl)
    {
        using var d = new Dialog(Ui.TypName(typ, true), 300, 420);
        var liste = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Left = 8, Top = 8, Width = 284, Height = 300, GridLines = true };
        liste.Columns.Add("Name", 90);
        liste.Columns.Add("Langname", 170);
        var vor = vorauswahl.ToHashSet();
        var filter = new TextBox { Left = 8, Top = 316, Width = 120 };
        void Fuellen()
        {
            var gewaehlt = liste.Items.Count == 0 ? vor : liste.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag!).ToHashSet();
            liste.BeginUpdate();
            liste.Items.Clear();
            foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ))
            {
                if (filter.Text.Length > 0 && !e.Kurzname.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase) && !e.Langname.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                var it = new ListViewItem(new[] { e.Kurzname, e is Lehrer l ? l.VollerName : e.Langname }) { Tag = e.Id, Selected = gewaehlt.Contains(e.Id) };
                liste.Items.Add(it);
            }
            liste.EndUpdate();
        }
        Fuellen();
        filter.TextChanged += (_, _) => Fuellen();
        var alle = new Button { Text = "Alle", Left = 8, Top = 344, Width = 88 };
        var inverse = new Button { Text = "Inverse", Left = 102, Top = 344, Width = 88 };
        alle.Click += (_, _) => { foreach (ListViewItem i in liste.Items) i.Selected = true; liste.Focus(); };
        inverse.Click += (_, _) => { foreach (ListViewItem i in liste.Items) i.Selected = !i.Selected; liste.Focus(); };
        d.Controls.AddRange(new Control[] { liste, filter, new Label { Text = "Elementfilter", Left = 134, Top = 319, AutoSize = true }, alle, inverse });
        d.Ok.Text = "Ok";
        return d.ShowDialog(owner) == DialogResult.OK ? liste.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag!).ToList() : null;
    }
}

internal static class ToolStripErweiterung
{
    public static T Mit<T>(this T item, Action<T> a) where T : ToolStripItem
    {
        a(item);
        return item;
    }
}
