using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Zeitwünsche für Lehrer, Klassen, Räume, Fächer und Unterrichte: Wunschgewicht -3 (keinesfalls Unterricht) bis +3
/// (auf jeden Fall Unterricht). Werte auswählen und Stunden mit gedrückter Maustaste überstreichen.
/// </summary>
public sealed class ZeitwuenscheFenster : MdiFenster
{
    private ElementTyp _typ;
    private int? _id;
    private int _wert = -3;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _unbestimmt = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _auswahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Label _name = new() { Dock = DockStyle.Top, Height = 22, Font = Ui.SchriftFett, Padding = new Padding(6, 4, 0, 0) };
    private readonly List<ToolStripButton> _wertKnoepfe = new();
    private bool _malen;
    private Zeitwuensche? _arbeitskopie;
    private bool _laden;

    public static ZeitwuenscheFenster Zeigen(ElementTyp typ, int? id)
    {
        var f = HauptFenster.Oeffnen("zeitwunsch", () => new ZeitwuenscheFenster());
        f.Setzen(typ, id ?? f.StandardId(typ));
        return f;
    }

    private int? StandardId(ElementTyp typ) => typ == ElementTyp.Unterricht ? D.Unterrichte.FirstOrDefault()?.Id : Stammdaten.Sortiert(D, typ).FirstOrDefault()?.Id;

    public ZeitwuenscheFenster()
    {
        HilfeThema = "Zeitwünsche";
        Icon = SymbolIcon("zeitwunsch");
        Size = new Size(620, 400);
        Positionieren();
        var ts = Werkzeugleiste();
        var typBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        typBox.Items.AddRange(new object[] { "Klasse", "Lehrer", "Raum", "Fach", "Unterricht" });
        typBox.SelectedIndexChanged += (_, _) =>
        {
            if (_laden) return;
            var t = typBox.SelectedIndex switch { 0 => ElementTyp.Klasse, 1 => ElementTyp.Lehrer, 2 => ElementTyp.Raum, 3 => ElementTyp.Fach, _ => ElementTyp.Unterricht };
            Setzen(t, StandardId(t));
        };
        ts.Items.Add(typBox);
        ts.Items.Add(_auswahl);
        ts.Items.AddRange(Ui.Durchschalter(_auswahl));
        _auswahl.SelectedIndexChanged += (_, _) => { if (!_laden && _auswahl.SelectedItem is Eintrag e) { _id = e.Id; Neuaufbau(); AppZustand.Waehlen(_typ, e.Id, this); } };
        ts.Items.Add(VerknuepfenKnopf());
        ts.Items.Add(new ToolStripSeparator());
        foreach (var w in new[] { 3, 2, 1, 0, -1, -2, -3 })
        {
            var b = new ToolStripButton(w > 0 ? "+" + w : w == 0 ? "0" : w.ToString())
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text, BackColor = w == 0 ? Color.White : Ui.ZeitwunschFarbe(w), Font = Ui.SchriftFett,
                ToolTipText = WunschText(w), CheckOnClick = false, Width = 28, AutoSize = false, Height = 22,
            };
            var wert = w;
            b.Click += (_, _) => WertWaehlen(wert);
            _wertKnoepfe.Add(b);
            ts.Items.Add(b);
        }
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Alle Zeitwünsche löschen", "loeschen", () => Uebernehmen(new Zeitwuensche(), "Zeitwünsche gelöscht")));
        ts.Items.Add(Knopf("Stundenplan", "stundenplan", () => { if (_id.HasValue && _typ != ElementTyp.Unterricht) StundenplanFenster.Neu(_typ, _id, false); }));
        _laden = true;
        typBox.SelectedIndex = 1;
        _laden = false;

        Ui.RasterGestalten(_grid, false);
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = false;
        _grid.DefaultCellStyle.SelectionBackColor = Color.Transparent;
        _grid.RowHeadersWidth = 90;
        _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.CellMouseDown += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) { _malen = true; _arbeitskopie = Aktuell()?.Kopie(); Malen(e.RowIndex, e.ColumnIndex); } };
        _grid.CellMouseEnter += (_, e) => { if (_malen && Control.MouseButtons == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0) Malen(e.RowIndex, e.ColumnIndex); };
        _grid.MouseUp += (_, _) => MalenBeenden();
        _grid.CellPainting += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.CellStyle!.SelectionBackColor = e.CellStyle.BackColor; };

        Ui.RasterGestalten(_unbestimmt);
        _unbestimmt.Columns.Add(new DataGridViewComboBoxColumn { Name = "Bereich", HeaderText = "Bereich", Width = 110, DataSource = new[] { "Tage", "Vormittage", "Nachmittage" }, FlatStyle = FlatStyle.Flat });
        _unbestimmt.Columns.Add(new DataGridViewTextBoxColumn { Name = "Anzahl", HeaderText = "Anzahl", Width = 55 });
        _unbestimmt.Columns.Add(new DataGridViewComboBoxColumn { Name = "Wunsch", HeaderText = "Zeitwunsch", Width = 230, DataSource = new[] { UnbestimmterZeitwunsch.WunschText(-3), UnbestimmterZeitwunsch.WunschText(-2), UnbestimmterZeitwunsch.WunschText(-1) }, FlatStyle = FlatStyle.Flat });
        _unbestimmt.AllowUserToAddRows = true;
        _unbestimmt.AllowUserToDeleteRows = true;
        _unbestimmt.EditMode = DataGridViewEditMode.EditOnEnter;
        _unbestimmt.CellEndEdit += (_, _) => UnbestimmteSpeichern();
        _unbestimmt.UserDeletedRow += (_, _) => UnbestimmteSpeichern();
        _unbestimmt.DataError += (_, e) => e.ThrowException = false;
        _unbestimmt.CurrentCellDirtyStateChanged += (_, _) => { if (_unbestimmt.CurrentCell is DataGridViewComboBoxCell) _unbestimmt.CommitEdit(DataGridViewDataErrorContexts.Commit); };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Controls.Add(new Label { Text = "Halten Sie die linke Maustaste gedrückt und überstreichen Sie die gewünschten Stunden. Klick auf \"Tage\"/\"Vorm.\"/\"Nachm.\" setzt den ganzen (Halb-)Tag.", Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.DimGray, Font = Ui.SchriftKlein });
        split.Panel1.Controls.Add(_name);
        split.Panel2.Controls.Add(_unbestimmt);
        split.Panel2.Controls.Add(new Label { Text = "Zusätzliche, unbestimmte Zeitwünsche", Dock = DockStyle.Top, Height = 20, Padding = new Padding(4, 3, 0, 0) });
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(120, split.Height - 110);
        WertWaehlen(-3);
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
        };
        void Typbox() { _laden = true; typBox.SelectedIndex = _typ switch { ElementTyp.Klasse => 0, ElementTyp.Lehrer => 1, ElementTyp.Raum => 2, ElementTyp.Fach => 3, _ => 4 }; _laden = false; }
        _typAnzeigen = Typbox;
    }

    private readonly Action _typAnzeigen;

    private static string WunschText(int w) => w switch
    {
        3 => "+3 auf jeden Fall Unterricht (Kernzeit)",
        2 => "+2 eigentlich schon",
        1 => "+1 eher ja",
        0 => "0 kein Wunsch",
        -1 => "-1 lieber nicht",
        -2 => "-2 eigentlich nicht",
        _ => "-3 keinesfalls Unterricht (absolute Sperrung)",
    };

    private sealed record Eintrag(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed || (typ == _typ && id == _id)) return;
        if (typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach or ElementTyp.Unterricht) Setzen(typ, id);
    }

    /// <summary>Fensterverknüpfung: Zeitwünsche des Elements (bzw. Unterrichts) der gewählten Stunde zeigen.</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        if (AppZustand.ElementDerStunde(_typ, u, p, _id) is int id && id != _id) Setzen(_typ, id);
    }

    private void WertWaehlen(int w)
    {
        _wert = w;
        foreach (var b in _wertKnoepfe) b.Checked = b.Text == (w > 0 ? "+" + w : w == 0 ? "0" : w.ToString());
    }

    public void Setzen(ElementTyp typ, int? id)
    {
        _typ = typ;
        _id = id;
        Text = $"Zeitwünsche / {Ui.TypName(typ)}";
        _typAnzeigen?.Invoke();
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art is AenderungsArt.Stundenplan or AenderungsArt.Vertretung) return;
        Neuaufbau();
    }

    private Zeitwuensche? Aktuell()
    {
        if (_id is null) return null;
        if (_typ == ElementTyp.Unterricht) return D.Unterrichte.FirstOrDefault(u => u.Id == _id)?.Zeitwuensche;
        return D.AlleElemente(_typ).FirstOrDefault(e => e.Id == _id)?.Zeitwuensche;
    }

    private List<UnbestimmterZeitwunsch>? Unbestimmte() =>
        _typ == ElementTyp.Unterricht || _id is null ? null : D.AlleElemente(_typ).FirstOrDefault(e => e.Id == _id)?.UnbestimmteZeitwuensche;

    private void Neuaufbau()
    {
        _laden = true;
        _auswahl.Items.Clear();
        IEnumerable<Eintrag> eintraege = _typ == ElementTyp.Unterricht
            ? D.Unterrichte.Select(u => new Eintrag(u.Id, $"{u.Id} {Ix.UnterrichtText(u)}"))
            : Stammdaten.Sortiert(D, _typ).Select(e => new Eintrag(e.Id, e.Kurzname));
        foreach (var e in eintraege) _auswahl.Items.Add(e);
        _auswahl.SelectedItem = _auswahl.Items.Cast<Eintrag>().FirstOrDefault(e => e.Id == _id);
        _laden = false;
        var el = _typ == ElementTyp.Unterricht ? null : D.AlleElemente(_typ).FirstOrDefault(e => e.Id == _id);
        _name.Text = _typ == ElementTyp.Unterricht ? $"Unterricht {_id}" : el is null ? "" : $"{Ui.TypName(_typ)} {el.Kurzname} ({(el is Lehrer l ? l.VollerName : el.Langname)})";
        var zr = D.Zeitraster;
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        for (var s = 1; s <= zr.StundenProTag; s++)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = zr.StundenLabel(s), Width = 30, SortMode = DataGridViewColumnSortMode.NotSortable };
            c.HeaderCell.Style.BackColor = s <= zr.LetzteVormittagsstunde ? Color.FromArgb(255, 232, 205) : Color.FromArgb(214, 245, 248);
            _grid.Columns.Add(c);
        }
        foreach (var t in new[] { "Tage", "Vorm.", "Nachm." })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = t, Width = 48, SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = { BackColor = Color.FromArgb(245, 245, 245) } });
        var zw = Aktuell();
        for (var t = 0; t < zr.TageProWoche; t++)
        {
            var r = _grid.Rows.Add();
            _grid.Rows[r].HeaderCell.Value = Planungsdienst.Tagname(D, t, true);
            _grid.Rows[r].Height = 24;
            for (var s = 1; s <= zr.StundenProTag; s++) ZelleSetzen(r, s - 1, zw?.Get(t, s) ?? 0, zr.Verfuegbar(t, s));
        }
        _unbestimmt.Rows.Clear();
        _unbestimmt.Enabled = Unbestimmte() != null;
        foreach (var u in Unbestimmte() ?? new())
            _unbestimmt.Rows.Add(u.Bereich switch { ZeitbereichArt.Tage => "Tage", ZeitbereichArt.Vormittage => "Vormittage", _ => "Nachmittage" }, u.Anzahl.ToString(), UnbestimmterZeitwunsch.WunschText(u.Wunsch));
    }

    private void ZelleSetzen(int r, int c, int w, bool verfuegbar)
    {
        var cell = _grid[c, r];
        cell.Value = w == 0 ? "" : (w > 0 ? "+" : "") + w;
        cell.Style.BackColor = verfuegbar ? Ui.ZeitwunschFarbe(w) : Color.FromArgb(200, 200, 200);
        cell.Style.ForeColor = Math.Abs(w) >= 2 ? Color.White : Color.Black;
    }

    private void Malen(int zeile, int spalte)
    {
        if (_arbeitskopie is null) return;
        var zr = D.Zeitraster;
        var tag = zeile;
        IEnumerable<int> stunden = spalte < zr.StundenProTag ? new[] { spalte + 1 }
            : spalte == zr.StundenProTag ? Enumerable.Range(1, zr.StundenProTag)
            : spalte == zr.StundenProTag + 1 ? Enumerable.Range(1, zr.LetzteVormittagsstunde)
            : Enumerable.Range(zr.LetzteVormittagsstunde + 1, Math.Max(0, zr.StundenProTag - zr.LetzteVormittagsstunde));
        foreach (var s in stunden)
        {
            _arbeitskopie.Set(tag, s, _wert);
            ZelleSetzen(zeile, s - 1, _wert, zr.Verfuegbar(tag, s));
        }
    }

    private void MalenBeenden()
    {
        if (!_malen) return;
        _malen = false;
        if (_arbeitskopie != null) Uebernehmen(_arbeitskopie, "Zeitwünsche geändert");
        _arbeitskopie = null;
    }

    private void Uebernehmen(Zeitwuensche neu, string text)
    {
        if (_id is null) return;
        var id = _id.Value;
        var typ = _typ;
        Aendern(text, AenderungsArt.Stammdaten, d =>
        {
            if (typ == ElementTyp.Unterricht) d.Unterrichte.First(u => u.Id == id).Zeitwuensche = neu.Kopie();
            else d.AlleElemente(typ).First(e => e.Id == id).Zeitwuensche = neu.Kopie();
        });
        Neuaufbau();
    }

    private void UnbestimmteSpeichern()
    {
        if (_laden || _id is null || _typ == ElementTyp.Unterricht) return;
        var liste = new List<UnbestimmterZeitwunsch>();
        foreach (DataGridViewRow r in _unbestimmt.Rows)
        {
            if (r.IsNewRow) continue;
            var bereich = r.Cells[0].Value?.ToString() switch { "Vormittage" => ZeitbereichArt.Vormittage, "Nachmittage" => ZeitbereichArt.Nachmittage, _ => ZeitbereichArt.Tage };
            var anzahl = int.TryParse(r.Cells[1].Value?.ToString(), out var a) ? Math.Max(1, a) : 1;
            var w = r.Cells[2].Value?.ToString() is string s && s.Contains("-2") ? -2 : r.Cells[2].Value?.ToString()?.Contains("-1") == true ? -1 : -3;
            liste.Add(new UnbestimmterZeitwunsch { Bereich = bereich, Anzahl = anzahl, Wunsch = w });
        }
        var id = _id.Value;
        var typ = _typ;
        Aendern("Unbestimmte Zeitwünsche", AenderungsArt.Stammdaten, d => d.AlleElemente(typ).First(e => e.Id == id).UnbestimmteZeitwuensche = liste);
    }
}
