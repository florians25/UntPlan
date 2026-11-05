using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vorbild „Formate / Stundenpläne“ (Schaltfläche „Stundenplanformate“): Liste aller Stundenplanformate mit Name, Langname,
/// Standard und im Menü. Symbolleiste: Planart (Alle, Klasse, Lehrer, Raum, Fach), Neu (Kopie des markierten Formats),
/// Ansicht zeigen (auch Doppelklick auf den Langnamen) und Löschen.
/// </summary>
public sealed class StundenplanformateFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripDropDownButton _art = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Planart" };
    private ElementTyp? _filter;
    private bool _laedt;

    public StundenplanformateFenster()
    {
        Text = "Formate / Stundenpläne";
        HilfeThema = "Stundenpläne";
        Icon = SymbolIcon("stundenplan");
        Size = new Size(420, 360);
        Positionieren();
        var ts = Werkzeugleiste();
        _art.Image = Symbole.Get("uebersicht", 16);
        foreach (var (t, text, symbol) in new (ElementTyp?, string, string)[] { (null, "Alle", "uebersicht"), (ElementTyp.Klasse, "Klasse", "klassen"), (ElementTyp.Lehrer, "Lehrer", "lehrer"), (ElementTyp.Raum, "Raum", "raeume"), (ElementTyp.Fach, "Fach", "faecher") })
        {
            var typ = t;
            var bild = Symbole.Get(symbol, 16);
            _art.DropDownItems.Add(text, bild, (_, _) => { _filter = typ; _art.Image = bild; Neuaufbau(); });
        }
        ts.Items.Add(_art);
        ts.Items.Add(Knopf("Neu", "neu", Neu));
        ts.Items.Add(Knopf("Ansicht zeigen\nZeigt die Ansicht der aktuellen Zeile.", "vorschau", AnsichtZeigen));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, false);
        _g.RowHeadersVisible = false;
        _g.AllowUserToAddRows = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = false;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "N", HeaderText = "Name", Width = 70 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "L", HeaderText = "Langname", Width = 150 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "S", HeaderText = "Standard", Width = 64 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "M", HeaderText = "im Menü", Width = 64 });
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex <= 1) AnsichtZeigen(); };
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 2) _g.EndEdit(); };
        _g.CellValueChanged += ZelleGeaendert;
        _g.CurrentCellDirtyStateChanged += (_, _) => { if (_g.CurrentCell is DataGridViewCheckBoxCell) _g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private static string AutoKurz(StundenplanFormat f, int nr) =>
        (f.Typ switch { ElementTyp.Klasse => "Kla", ElementTyp.Lehrer => "Leh", ElementTyp.Raum => "Rau", ElementTyp.Fach => "Fa", _ => "Stp" }) + nr + (f.Gross && !f.Klein ? "A" : "");

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        _laedt = true;
        var zeile = _g.CurrentCell?.RowIndex ?? 0;
        _g.Rows.Clear();
        var liste = D.Formate.Where(f => _filter is null || f.Typ == _filter).OrderBy(f => f.Typ).ThenBy(f => f.Kurzname.Length > 0 ? f.Kurzname : f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var gruppe in liste.GroupBy(f => f.Typ))
        {
            var nr = 1;
            foreach (var f in gruppe)
            {
                var i = _g.Rows.Add(f.Kurzname.Length > 0 ? f.Kurzname : AutoKurz(f, nr), f.Name, f.Standard, f.ImMenue);
                _g.Rows[i].Tag = f;
                _g.Rows[i].Cells["N"].ReadOnly = _g.Rows[i].Cells["L"].ReadOnly = true;
                nr++;
            }
        }
        if (_g.Rows.Count > 0) _g.CurrentCell = _g.Rows[Math.Clamp(zeile, 0, _g.Rows.Count - 1)].Cells[0];
        _laedt = false;
    }

    private StundenplanFormat? Aktuell() => _g.CurrentRow?.Tag as StundenplanFormat;

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not StundenplanFormat f) return;
        var wert = _g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is true;
        var (typ, name, spalte) = (f.Typ, f.Name, _g.Columns[e.ColumnIndex].Name);
        Aendern(spalte == "S" ? "Standardformat" : "Format im Menü", AenderungsArt.Einstellungen, d =>
        {
            foreach (var x in d.Formate.Where(x => x.Typ == typ))
            {
                if (spalte == "S") x.Standard = x.Name == name ? wert : wert ? (x.Klein != f.Klein || x.Gross != f.Gross) && x.Standard : x.Standard;
                else if (x.Name == name) x.ImMenue = wert;
            }
        });
        BeginInvoke(Neuaufbau);
    }

    private void AnsichtZeigen()
    {
        if (Aktuell() is not { } f) return;
        var sp = StundenplanFenster.Neu(f.Typ, null, f.Gross, f.Klein);
        sp.FormatSetzen(f.Name);
    }

    private void Neu()
    {
        if (Aktuell() is not { } f) { Ui.Info("Bitte zuerst das Format markieren, das kopiert werden soll."); return; }
        using var dlg = KurznameDialog.Erzeugen("", f.Name + " 2", out var k, out var l);
        dlg.Text = "Neues Stundenplanformat";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var neu = (Kurz: k.Text.Trim(), Lang: l.Text.Trim());
        var lang = neu.Lang.Length > 0 ? neu.Lang : neu.Kurz;
        if (lang.Length == 0) return;
        if (D.Formate.Any(x => x.Typ == f.Typ && x.Name == lang)) { Ui.Info($"Das Format \"{lang}\" gibt es bereits."); return; }
        var kopie = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.Serialize(f))!;
        kopie.Name = lang;
        kopie.Kurzname = neu.Kurz;
        kopie.Standard = false;
        kopie.ImMenue = false;
        Aendern("Neues Stundenplanformat", AenderungsArt.Einstellungen, d => d.Formate.Add(kopie));
        // wie im Vorbild öffnet sich die neue Ansicht sofort
        var sp = StundenplanFenster.Neu(kopie.Typ, null, kopie.Gross, kopie.Klein);
        sp.FormatSetzen(kopie.Name);
    }

    private void Loeschen()
    {
        if (Aktuell() is not { } f) return;
        if (!Ui.Frage($"Stundenplanformat \"{f.Name}\" löschen?")) return;
        var (typ, name) = (f.Typ, f.Name);
        Aendern("Stundenplanformat löschen", AenderungsArt.Einstellungen, d => d.Formate.RemoveAll(x => x.Typ == typ && x.Name == name));
    }
}
