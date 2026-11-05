using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// „Formate / Vertretung“ wie im Handbuch (Kap. 13.1.1 und 13.3): alle Vertretungsformate mit Name, Langname, Standard, Art, Tagesliste
/// und Art2. Symbolleiste: Art (Alle, Klasse, Lehrer), Neu (Kopie des markierten Formats), Ansicht zeigen (auch Doppelklick) und Löschen.
/// Mit dem Häkchen „Tagesliste“ werden die Formate über „Ausgabe ▸ Tageslisten“ mit einem Klick gedruckt.
/// </summary>
public sealed class VertretungsformateFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripDropDownButton _art = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Art" };
    private VertretungsformatArt? _filter;
    private bool _laedt;

    public VertretungsformateFenster()
    {
        Text = "Formate / Vertretung";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("uebersicht");
        Size = new Size(470, 330);
        Positionieren();
        var ts = Werkzeugleiste();
        _art.Image = Symbole.Get("uebersicht", 16);
        foreach (var (a, text, symbol) in new (VertretungsformatArt?, string, string)[] { (null, "Alle", "uebersicht"), (VertretungsformatArt.Klasse, "Klasse", "klassen"), (VertretungsformatArt.Lehrer, "Lehrer", "lehrer") })
        {
            var art = a;
            var bild = Symbole.Get(symbol, 16);
            _art.DropDownItems.Add(text, bild, (_, _) => { _filter = art; _art.Image = bild; Neuaufbau(); });
        }
        ts.Items.Add(_art);
        ts.Items.Add(Knopf("Neu\nKopie des markierten Formats unter neuem Namen (Format speichern als…).", "neu", Neu));
        ts.Items.Add(Knopf("Ansicht zeigen\nZeigt die Ansicht der aktuellen Zeile.", "vorschau", AnsichtZeigen));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, false);
        _g.RowHeadersVisible = false;
        _g.AllowUserToAddRows = false;
        _g.AllowUserToDeleteRows = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = false;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "N", HeaderText = "Name", Width = 70 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "L", HeaderText = "Langname", Width = 140 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "S", HeaderText = "Standard", Width = 58 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "A", HeaderText = "Art", Width = 54, ReadOnly = true });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "T", HeaderText = "Tagesliste", Width = 64 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "A2", HeaderText = "Art2", Width = 44, ReadOnly = true });
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _g.Columns[e.ColumnIndex].ReadOnly) AnsichtZeigen(); };
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _g.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _g.EndEdit(); };
        _g.CurrentCellDirtyStateChanged += (_, _) => { if (_g.CurrentCell is DataGridViewCheckBoxCell) _g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _g.CellValueChanged += ZelleGeaendert;
        _g.CellBeginEdit += (_, e) =>
        {
            // Langname der mitgelieferten Formate ist fest (Schlüssel der Ansicht)
            if (e.RowIndex >= 0 && _g.Columns[e.ColumnIndex].Name == "L" && _g.Rows[e.RowIndex].Tag is Vertretungsformat { Eigen: false }) e.Cancel = true;
        };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    private static List<Vertretungsformat> Formate => AppZustand.Einstellungen.VertretungsformateListe;

    /// <summary>Selbsttest: Raster der Formate.</summary>
    internal DataGridView Raster => _g;

    internal void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        _laedt = true;
        try
        {
            var aktuell = Aktuell();
            _g.Rows.Clear();
            foreach (var f in Formate.Where(f => _filter is null || f.Art == _filter))
            {
                var i = _g.Rows.Add(f.Name, f.Langname, f.Standard, f.Art == VertretungsformatArt.Lehrer ? "Lehrer" : "Klasse", f.Tagesliste, "Vertr.");
                _g.Rows[i].Tag = f;
                if (!f.Eigen) _g.Rows[i].Cells["L"].Style.ForeColor = Color.FromArgb(60, 60, 60);
            }
            var zeile = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, aktuell)) ?? (_g.Rows.Count > 0 ? _g.Rows[0] : null);
            if (zeile != null) _g.CurrentCell = zeile.Cells[0];
        }
        finally { _laedt = false; }
    }

    private Vertretungsformat? Aktuell() => _g.CurrentRow?.Tag as Vertretungsformat;

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Vertretungsformat f) return;
        var zelle = _g.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var einst = AppZustand.Einstellungen;
        switch (_g.Columns[e.ColumnIndex].Name)
        {
            case "S":
                if (zelle.Value is true) Vertretungsformate.StandardSetzen(Formate, f);
                else f.Standard = false;
                Vertretungsformate.Ergaenzen(Formate);
                break;
            case "T":
                f.Tagesliste = zelle.Value is true;
                break;
            case "N":
                var name = zelle.Value?.ToString()?.Trim() ?? "";
                if (name.Length > 0 && !Formate.Any(x => x != f && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) f.Name = name;
                break;
            case "L":
                var lang = zelle.Value?.ToString()?.Trim() ?? "";
                if (!f.Eigen || lang.Length == 0 || lang == f.Langname || Formate.Any(x => x != f && x.Langname.Equals(lang, StringComparison.OrdinalIgnoreCase))) break;
                // Umbenennen: gespeicherte Felder, Vertretungsarten und Sortierkriterien der Ansicht ziehen mit
                einst.FormatEinstellungenUmbenennen(f.Langname, lang);
                if (einst.Vertretungsansicht == f.Langname) einst.Vertretungsansicht = lang;
                f.Langname = lang;
                break;
        }
        einst.Speichern();
        VertretungenFenster.AlleAnsichtenAktualisieren();
        BeginInvoke(Neuaufbau);
    }

    private void AnsichtZeigen()
    {
        if (Aktuell() is not { } f) return;
        AppZustand.Einstellungen.Vertretungsansicht = f.Langname;
        AppZustand.Einstellungen.Speichern();
        if (AppZustand.Haupt.VertretungsFensterOeffnen("vertr") is VertretungenFenster vf) vf.AnsichtSetzen(f.Langname);
    }

    private void Neu()
    {
        if (Aktuell() is not { } f) { Ui.Info("Bitte zuerst das Format markieren, das kopiert werden soll."); return; }
        var r = NeuesFormatDialog.Zeigen(this, "", f.Langname + " 2");
        if (r is null) return;
        if (Vertretungsformate.NamePruefen(Formate, r.Value.Name, r.Value.Langname) is { } fehler) { Ui.Info(fehler); return; }
        var neu = VertretungenFenster.FormatKopieren(f, r.Value.Name, r.Value.Langname);
        Neuaufbau();
        if (_g.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => ReferenceEquals(x.Tag, neu)) is { } zeile) _g.CurrentCell = zeile.Cells[0];
    }

    private void Loeschen()
    {
        if (Aktuell() is not { } f) return;
        if (!f.Eigen) { Ui.Info($"Das mitgelieferte Format „{f.Langname}“ kann nicht gelöscht werden."); return; }
        if (!Ui.Frage($"Vertretungsformat „{f.Langname}“ löschen?")) return;
        VertretungenFenster.FormatLoeschen(f);
        Neuaufbau();
    }
}
