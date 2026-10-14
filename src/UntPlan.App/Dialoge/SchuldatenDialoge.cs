using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Zeitraster: Anzahl Tage, Stunden pro Tag, Zeiten, Vormittag/Nachmittag/leer je Tag und Stunde (Karteikarte "Allgemein"),
/// Pausen und flexible Mittagspause (Karteikarte "Pausen").
/// </summary>
public sealed class ZeitrasterDialog : Dialog
{
    private readonly Zeitraster _z;
    private readonly DataGridView _grid = new() { Left = 10, Top = 112, Width = 760, Height = 220 };
    private readonly DataGridView _pausen = new() { Left = 10, Top = 100, Width = 520, Height = 200 };
    private readonly NumericUpDown _tage = new() { Minimum = 1, Maximum = 7 };
    private readonly NumericUpDown _stunden = new() { Minimum = 1, Maximum = 16 };
    private readonly ComboBox _erster = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _nummer = new() { Minimum = 0, Maximum = 1 };
    private readonly NumericUpDown _grenze = new() { Minimum = 1, Maximum = 16 };
    private readonly NumericUpDown _mittagVon = new() { Minimum = 0, Maximum = 16 };
    private readonly NumericUpDown _mittagBis = new() { Minimum = 0, Maximum = 16 };
    private readonly NumericUpDown _maxKlassen = new() { Minimum = 0, Maximum = 500 };

    public ZeitrasterDialog() : base("Zeitraster", 800, 440)
    {
        _z = Dokument.Klonen(AppZustand.D).Zeitraster;
        var tabs = new TabControl { Left = 6, Top = 6, Width = 788, Height = 386 };
        var allg = new TabPage("Allgemein");
        var y = 10;
        _tage.Value = _z.TageProWoche;
        _stunden.Value = _z.StundenProTag;
        _erster.Items.AddRange(Planungsdienst.TageLang);
        _erster.SelectedIndex = _z.ErsterSchultag;
        _nummer.Value = _z.ErsteStundennummer;
        _grenze.Value = Math.Clamp(_z.LetzteVormittagsstunde, 1, 16);
        Ui.Zeile(allg, "Anzahl Tage (1-7)", _tage, ref y, 220, 60);
        Ui.Zeile(allg, "Maximale Stundenzahl pro Tag (1-16)", _stunden, ref y, 220, 60);
        var y2 = 10;
        allg.Controls.Add(new Label { Text = "Erster Schultag der Woche", Left = 330, Top = y2 + 3, Width = 170 });
        _erster.Left = 500; _erster.Top = y2; _erster.Width = 110; allg.Controls.Add(_erster);
        y2 += 26;
        allg.Controls.Add(new Label { Text = "Stundennummer der ersten Stunde", Left = 330, Top = y2 + 3, Width = 170 });
        _nummer.Left = 500; _nummer.Top = y2; _nummer.Width = 50; allg.Controls.Add(_nummer);
        Ui.Zeile(allg, "Letzte Vormittagsstunde", _grenze, ref y, 220, 60);
        var grp = new GroupBox { Text = "Eintragung", Left = 640, Top = 4, Width = 130, Height = 100 };
        var bVorm = new Button { Text = "Vormittag", Left = 10, Top = 18, Width = 108 };
        var bLeer = new Button { Text = "leer", Left = 10, Top = 44, Width = 108 };
        var bNachm = new Button { Text = "Nachmittag", Left = 10, Top = 70, Width = 108 };
        grp.Controls.AddRange(new Control[] { bVorm, bLeer, bNachm });
        allg.Controls.Add(grp);
        bVorm.Click += (_, _) => Markieren(Tageszeit.Vormittag);
        bLeer.Click += (_, _) => Markieren(Tageszeit.Leer);
        bNachm.Click += (_, _) => Markieren(Tageszeit.Nachmittag);
        Ui.RasterGestalten(_grid);
        _grid.RowHeadersWidth = 110;
        _grid.MultiSelect = true;
        _grid.CellEndEdit += (_, e) => ZeitGeaendert(e.RowIndex, e.ColumnIndex);
        allg.Controls.Add(_grid);
        allg.Controls.Add(new Label { Text = "Zellen markieren und mit <Vormittag>, <leer> oder <Nachmittag> belegen. In den ersten beiden Zeilen werden Beginn und Ende der Stunden eingetragen.", Left = 10, Top = 336, Width = 760, Height = 20, ForeColor = Color.DimGray });
        _tage.ValueChanged += (_, _) => { _z.TageProWoche = (int)_tage.Value; RasterFuellen(); };
        _stunden.ValueChanged += (_, _) => { _z.StundenProTag = (int)_stunden.Value; Dokument.Normalisieren(new Schuldatei { Zeitraster = _z }); RasterFuellen(); };
        _grenze.ValueChanged += (_, _) => { _z.LetzteVormittagsstunde = (int)_grenze.Value; RasterFuellen(); };
        tabs.TabPages.Add(allg);

        var pausen = new TabPage("Pausen");
        y = 10;
        _mittagVon.Value = _z.MittagspauseVon;
        _mittagBis.Value = _z.MittagspauseBis;
        _maxKlassen.Value = _z.MaxKlassenGleicheMittagspause;
        Ui.Zeile(pausen, "Mittagspause von Stunde (0 = keine)", _mittagVon, ref y, 260, 60);
        Ui.Zeile(pausen, "Mittagspause bis Stunde", _mittagBis, ref y, 260, 60);
        Ui.Zeile(pausen, "Max. Klassenzahl mit gleichzeitiger Mittagspause", _maxKlassen, ref y, 260, 60);
        Ui.RasterGestalten(_pausen);
        _pausen.AllowUserToAddRows = true;
        _pausen.AllowUserToDeleteRows = true;
        _pausen.Columns.Add("N", "nach Stunde");
        _pausen.Columns.Add("B", "Bezeichnung");
        _pausen.Columns.Add("D", "Dauer (Min.)");
        _pausen.Columns.Add(new DataGridViewCheckBoxColumn { Name = "A", HeaderText = "Pausenaufsicht" });
        _pausen.Columns[1].Width = 180;
        foreach (var p in _z.Pausen.OrderBy(p => p.NachStunde)) _pausen.Rows.Add(p.NachStunde, p.Bezeichnung, p.DauerMinuten, p.Aufsicht);
        pausen.Controls.Add(_pausen);
        pausen.Controls.Add(new Label { Text = "Die Mittagspause wird automatisch in die letzten Vormittags- bzw. ersten Nachmittagsstunden gelegt. Die Länge legen Sie bei den Klassen (Spalte \"Mitt-pause\", z. B. 1-2) fest.", Left = 10, Top = 306, Width = 760, Height = 34, ForeColor = Color.DimGray });
        tabs.TabPages.Add(pausen);
        Controls.Add(tabs);
        RasterFuellen();
        Ok.Click += (_, _) => Uebernehmen();
    }

    private void RasterFuellen()
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        for (var s = 1; s <= _z.StundenProTag; s++) _grid.Columns.Add($"s{s}", _z.StundenLabel(s));
        foreach (DataGridViewColumn c in _grid.Columns) { c.Width = 62; c.SortMode = DataGridViewColumnSortMode.NotSortable; }
        var r1 = _grid.Rows.Add();
        var r2 = _grid.Rows.Add();
        _grid.Rows[r1].HeaderCell.Value = "Beginn";
        _grid.Rows[r2].HeaderCell.Value = "Ende";
        for (var s = 1; s <= _z.StundenProTag; s++)
        {
            var z = _z.Zeit(s);
            _grid[s - 1, r1].Value = z?.Beginn.ToString("HH:mm");
            _grid[s - 1, r2].Value = z?.Ende.ToString("HH:mm");
        }
        for (var t = 0; t < _z.TageProWoche; t++)
        {
            var r = _grid.Rows.Add();
            _grid.Rows[r].HeaderCell.Value = Planungsdienst.TageLang[(_z.ErsterSchultag + t) % 7];
            for (var s = 1; s <= _z.StundenProTag; s++)
            {
                var art = _z.Art(t, s);
                var c = _grid[s - 1, r];
                c.Value = art switch { Tageszeit.Vormittag => "Vormittag", Tageszeit.Nachmittag => "Nachmittag", _ => "" };
                c.Style.BackColor = art switch { Tageszeit.Vormittag => Ui.Vormittag, Tageszeit.Nachmittag => Ui.Nachmittag, _ => Color.White };
                c.ReadOnly = true;
            }
        }
    }

    private void Markieren(Tageszeit art)
    {
        foreach (DataGridViewCell c in _grid.SelectedCells)
        {
            if (c.RowIndex < 2) continue;
            _z.Belegung[(c.RowIndex - 2) * 100 + c.ColumnIndex + 1] = art;
        }
        RasterFuellen();
    }

    private void ZeitGeaendert(int r, int c)
    {
        if (r > 1 || c < 0) return;
        var z = _z.Zeit(c + 1);
        if (z is null || !TimeOnly.TryParse(_grid[c, r].Value?.ToString(), out var t)) { RasterFuellen(); return; }
        if (r == 0) { var dauer = z.Ende - z.Beginn; z.Beginn = t; if (dauer > TimeSpan.Zero) z.Ende = t.Add(dauer); }
        else z.Ende = t;
        RasterFuellen();
    }

    private void Uebernehmen()
    {
        _z.ErsterSchultag = _erster.SelectedIndex;
        _z.ErsteStundennummer = (int)_nummer.Value;
        _z.MittagspauseVon = (int)_mittagVon.Value;
        _z.MittagspauseBis = (int)_mittagBis.Value;
        _z.MaxKlassenGleicheMittagspause = (int)_maxKlassen.Value;
        _z.Pausen = _pausen.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && int.TryParse(r.Cells[0].Value?.ToString(), out _))
            .Select(r => new Pause
            {
                NachStunde = int.Parse(r.Cells[0].Value!.ToString()!), Bezeichnung = r.Cells[1].Value?.ToString() ?? "",
                DauerMinuten = int.TryParse(r.Cells[2].Value?.ToString(), out var dm) ? dm : 0, Aufsicht = r.Cells[3].Value is true,
            }).ToList();
        var z = _z;
        AppZustand.Doc.Aendern("Zeitraster", AenderungsArt.Alles, d =>
        {
            d.Zeitraster = z;
            Dokument.Normalisieren(d);
        });
    }
}

/// <summary>Einstellungen: Schuldaten (Schuljahr, Land, Schulform …), Vertretungsplanung, Portal.</summary>
public sealed class EinstellungenDialog : Dialog
{
    private readonly Einstellungen _e;
    private readonly TreeView _baum = new() { Left = 8, Top = 8, Width = 180, Height = 400, HideSelection = false };
    private readonly Panel _seite = new() { Left = 196, Top = 8, Width = 560, Height = 400, AutoScroll = true };
    private readonly Dictionary<string, Action> _seiten = new();

    public EinstellungenDialog(string start = "Allgemein") : base("Einstellungen", 766, 460)
    {
        _e = Dokument.Klonen(AppZustand.D).Einstellungen;
        var schuldaten = _baum.Nodes.Add("Schuldaten");
        schuldaten.Nodes.Add("Allgemein");
        schuldaten.Nodes.Add("Werte");
        _baum.Nodes.Add("Vertretungsplanung");
        _baum.Nodes.Add("Diverses");
        _baum.ExpandAll();
        _seiten["Allgemein"] = SeiteAllgemein;
        _seiten["Schuldaten"] = SeiteAllgemein;
        _seiten["Werte"] = SeiteWerte;
        _seiten["Vertretungsplanung"] = SeiteVertretung;
        _seiten["Diverses"] = SeiteDiverses;
        _baum.AfterSelect += (_, e) => { _seite.Controls.Clear(); if (_seiten.TryGetValue(e.Node!.Text, out var a)) a(); };
        Controls.Add(_baum);
        Controls.Add(_seite);
        _baum.SelectedNode = _baum.Nodes.Find(start, true).FirstOrDefault() ?? FindeKnoten(start) ?? schuldaten.Nodes[0];
        Ok.Click += (_, _) =>
        {
            if (_e.SchuljahrBis <= _e.SchuljahrVon) { Ui.Fehler(new InvalidOperationException("Das Schuljahresende muss nach dem Beginn liegen.")); DialogResult = DialogResult.None; return; }
            var e = _e;
            AppZustand.Doc.Aendern("Einstellungen", AenderungsArt.Einstellungen, d => d.Einstellungen = e);
        };
    }

    private TreeNode? FindeKnoten(string text)
    {
        foreach (TreeNode n in _baum.Nodes)
        {
            if (n.Text == text) return n;
            foreach (TreeNode c in n.Nodes) if (c.Text == text) return c;
        }
        return null;
    }

    private void SeiteAllgemein()
    {
        var y = 8;
        var name = Ui.Zeile(_seite, "Schulbezeichnung", new TextBox { Text = _e.Schulname }, ref y, 160, 330);
        name.TextChanged += (_, _) => _e.Schulname = name.Text;
        var name2 = Ui.Zeile(_seite, "Zusatz / Ort", new TextBox { Text = _e.Schulname2 }, ref y, 160, 330);
        name2.TextChanged += (_, _) => _e.Schulname2 = name2.Text;
        var adr = Ui.Zeile(_seite, "Adresse", new TextBox { Text = _e.Adresse }, ref y, 160, 330);
        adr.TextChanged += (_, _) => _e.Adresse = adr.Text;
        var nr = Ui.Zeile(_seite, "Schulnummer (NRW, 6-stellig)", new TextBox { Text = _e.Schulnummer }, ref y, 160, 100);
        nr.TextChanged += (_, _) => _e.Schulnummer = nr.Text;
        var land = Ui.Zeile(_seite, "Land", new ComboBox { Text = _e.Land, DropDownStyle = ComboBoxStyle.DropDown }, ref y, 160, 160);
        land.Items.AddRange(new object[] { "Deutschland", "Österreich", "Schweiz" });
        land.TextChanged += (_, _) => _e.Land = land.Text;
        var region = Ui.Zeile(_seite, "Region / Bundesland", new ComboBox { Text = _e.Region, DropDownStyle = ComboBoxStyle.DropDown }, ref y, 160, 220);
        region.Items.Add("Nordrhein-Westfalen");
        region.TextChanged += (_, _) => _e.Region = region.Text;
        var form = Ui.Zeile(_seite, "Schulform", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 160, 220);
        foreach (var (k, v) in Nrw.Schulformen) form.Items.Add($"{k} – {v}");
        form.SelectedIndex = Math.Max(0, Nrw.Schulformen.Keys.ToList().IndexOf(_e.Schulform));
        form.SelectedIndexChanged += (_, _) => _e.Schulform = Nrw.Schulformen.Keys.ElementAt(form.SelectedIndex);
        y += 8;
        _seite.Controls.Add(new Label { Text = "Schuljahr (die Sommerferien sollen im Schuljahr nicht enthalten sein)", Left = 12, Top = y, Width = 500, Font = Ui.SchriftFett });
        y += 22;
        var von = Ui.Zeile(_seite, "von", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = _e.SchuljahrVon.ToDateTime(TimeOnly.MinValue) }, ref y, 160, 120);
        von.ValueChanged += (_, _) => _e.SchuljahrVon = DateOnly.FromDateTime(von.Value);
        var bis = Ui.Zeile(_seite, "bis", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = _e.SchuljahrBis.ToDateTime(TimeOnly.MinValue) }, ref y, 160, 120);
        bis.ValueChanged += (_, _) => _e.SchuljahrBis = DateOnly.FromDateTime(bis.Value);
        var per = Ui.Zeile(_seite, "Wochenperiodizität (1 = jede Woche, 2 = A/B)", new NumericUpDown { Minimum = 1, Maximum = 4, Value = Math.Clamp(_e.Wochenperiodizitaet, 1, 4) }, ref y, 260, 50);
        per.ValueChanged += (_, _) => _e.Wochenperiodizitaet = (int)per.Value;
    }

    private void SeiteWerte()
    {
        var y = 8;
        var tg = Ui.Zeile(_seite, "Tagesgenaue Wertrechnung", new CheckBox { Checked = _e.TagesgenaueWertrechnung }, ref y, 220);
        tg.CheckedChanged += (_, _) => _e.TagesgenaueWertrechnung = tg.Checked;
        var mg = Ui.Zeile(_seite, "Mehrarbeit vergütungsfähig ab mehr als … Std./Monat (NRW)", new NumericUpDown { Minimum = 0, Maximum = 20, Value = _e.Mehrarbeitsgrenze }, ref y, 340, 60);
        mg.ValueChanged += (_, _) => _e.Mehrarbeitsgrenze = (int)mg.Value;
        _seite.Controls.Add(new Label { Text = $"Pflichtstunden der Schulform ({Nrw.Schulformen.GetValueOrDefault(_e.Schulform)}): {Format.Zahl(Nrw.Pflichtstunden.GetValueOrDefault(_e.Schulform, 25.5m))} Std. (VO zu § 93 Abs. 2 SchulG) – Voreinstellung für neue Lehrer.", Left = 12, Top = y + 6, Width = 520, Height = 40, ForeColor = Color.DimGray });
    }

    private void SeiteVertretung()
    {
        var y = 8;
        var titel = Ui.Zeile(_seite, "Titel des Vertretungsplans", new TextBox { Text = _e.Vertretungsplantitel }, ref y, 200, 260);
        titel.TextChanged += (_, _) => _e.Vertretungsplantitel = titel.Text;
        var ab = Ui.Zeile(_seite, "Entfall in Randstunden ab Jahrgang", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = _e.EntfallAbJahrgang }, ref y, 200, 80);
        ab.Items.AddRange(Nrw.Jahrgaenge.GetValueOrDefault(_e.Schulform, Array.Empty<string>()).Cast<object>().Append("nie").ToArray());
        ab.TextChanged += (_, _) => _e.EntfallAbJahrgang = ab.Text == "nie" ? "99" : ab.Text;
        y += 6;
        _seite.Controls.Add(new Label { Text = "Vertretungsgewichtung (Kriterien für den Vertretungsvorschlag)", Left = 12, Top = y, Width = 500, Font = Ui.SchriftFett });
        y += 22;
        foreach (var (key, text) in VertretungsVorschlag.Kriterien)
        {
            _seite.Controls.Add(new Label { Text = text, Left = 12, Top = y + 6, Width = 300 });
            var tb = new TrackBar { Minimum = 0, Maximum = 5, Left = 316, Top = y, Width = 180, Value = _e.Vertretungsgewichtung.GetValueOrDefault(key, VertretungsVorschlag.StandardGewichtung[key]), TickStyle = TickStyle.BottomRight };
            var w = new Label { Text = tb.Value.ToString(), Left = 500, Top = y + 6, Width = 20 };
            var k = key;
            tb.ValueChanged += (_, _) => { _e.Vertretungsgewichtung[k] = tb.Value; w.Text = tb.Value.ToString(); };
            _seite.Controls.Add(tb);
            _seite.Controls.Add(w);
            y += 40;
        }
    }

    private void SeiteDiverses()
    {
        var y = 8;
        var b = Ui.Zeile(_seite, "Startbildschirm beim Start", new CheckBox { Checked = AppZustand.Einstellungen.BegruessungZeigen }, ref y, 260);
        b.CheckedChanged += (_, _) => { AppZustand.Einstellungen.BegruessungZeigen = b.Checked; AppZustand.Einstellungen.Speichern(); };
        var ld = Ui.Zeile(_seite, "Zuletzt geöffnete Datei beim Start laden", new CheckBox { Checked = AppZustand.Einstellungen.LetzteDateiLaden }, ref y, 260);
        ld.CheckedChanged += (_, _) => { AppZustand.Einstellungen.LetzteDateiLaden = ld.Checked; AppZustand.Einstellungen.Speichern(); };
        var s = Ui.Zeile(_seite, "Stundenpläne synchronisieren (Voreinstellung)", new CheckBox { Checked = AppZustand.Einstellungen.StundenplanSynchronisieren }, ref y, 260);
        s.CheckedChanged += (_, _) => { AppZustand.Einstellungen.StundenplanSynchronisieren = s.Checked; AppZustand.Einstellungen.Speichern(); };
        var a = Ui.Zeile(_seite, "Automatisch speichern alle … Minuten (0 = aus)", new NumericUpDown { Minimum = 0, Maximum = 120, Value = Math.Clamp(AppZustand.Einstellungen.AutoSpeichernMinuten, 0, 120) }, ref y, 260, 60);
        a.ValueChanged += (_, _) => { AppZustand.Einstellungen.AutoSpeichernMinuten = (int)a.Value; AppZustand.Einstellungen.Speichern(); AppZustand.Haupt.AutoSpeichernEinstellen(); };
        var m = Ui.Zeile(_seite, "Beim Start den zuletzt benutzten Modus öffnen", new CheckBox { Checked = AppZustand.Einstellungen.ModusMerken }, ref y, 260);
        m.CheckedChanged += (_, _) => { AppZustand.Einstellungen.ModusMerken = m.Checked; AppZustand.Einstellungen.Speichern(); };
    }
}

/// <summary>Perioden (Mehrwochenstundenplan): Das Schuljahr in Abschnitte mit eigenen Stundenplänen unterteilen.</summary>
public sealed class PeriodenDialog : Dialog
{
    private readonly TextBox _name = new() { Left = 12, Top = 22, Width = 110 };
    private readonly Button _loeschen = new() { Text = "Löschen", Left = 132, Top = 20, Width = 72, Height = 24 };
    private readonly Button _neu = new() { Text = "Neue Periode zu dieser Mutterper.", Left = 12, Top = 50, Width = 192, Height = 24 };
    private readonly CheckBox _fixiert = new() { Text = "Fixiert", Left = 230, Top = 60, AutoSize = true };
    private readonly TextBox _lang = new() { Left = 12, Top = 96, Width = 196 };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Left = 12, Top = 140, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Left = 120, Top = 140, Width = 116, ShowCheckBox = true };
    private readonly Label _tage = new() { Left = 250, Top = 144, AutoSize = true, Font = Ui.SchriftFett };
    private readonly DataGridView _g = new() { Left = 12, Top = 176, Width = 690, Height = 230, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private bool _laedt;

    private Schuldatei D => AppZustand.D;

    public PeriodenDialog() : base("Perioden", 716, 456)
    {
        Ok.Text = "Ok";
        Abbrechen.Visible = false;
        var gruppe = new GroupBox { Text = "Name", Left = 8, Top = 4, Width = 214, Height = 84 };
        Controls.Add(gruppe);
        _name.SetBounds(8, 20, 110, 23);
        _loeschen.SetBounds(124, 18, 82, 25);
        _neu.SetBounds(8, 50, 198, 25);
        gruppe.Controls.AddRange(new Control[] { _name, _loeschen, _neu });
        var druck = new Button { Text = "Druck", Left = 230, Top = 18, Width = 82, Height = 25 };
        var aktiv = new Button { Text = "Als aktive Periode", Left = 320, Top = 18, Width = 130, Height = 25 };
        Controls.AddRange(new Control[] { druck, aktiv, _fixiert });
        var folge = new CheckBox { Text = "Unterrichtsänderungen in Folgeperioden übernehmen", Left = 320, Top = 60, AutoSize = true, Checked = D.Einstellungen.UnterrichtInFolgeperioden };
        folge.CheckedChanged += (_, _) => AppZustand.Doc.Aendern("Unterricht in Folgeperioden", AenderungsArt.Alles, d => d.Einstellungen.UnterrichtInFolgeperioden = folge.Checked);
        Controls.Add(folge);
        Controls.Add(new Label { Text = "Langname", Left = 214, Top = 99, AutoSize = true });
        _lang.SetBounds(12, 96, 196, 23);
        Controls.Add(_lang);
        Controls.Add(new Label { Text = "Von", Left = 12, Top = 124, AutoSize = true });
        Controls.Add(new Label { Text = "Bis", Left = 120, Top = 124, AutoSize = true });
        Controls.Add(new Label { Text = "Schultage:", Left = 250, Top = 124, AutoSize = true });
        Controls.AddRange(new Control[] { _von, _bis, _tage });
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = false;
        foreach (var (n, k, w) in new[] { ("N", "Name", 110), ("L", "Langname", 150), ("V", "Von", 80), ("B", "Bis", 80), ("F", "Fixiert", 50), ("T", "Tage", 50), ("M", "Mutter-Per.", 110) })
            _g.Columns[_g.Columns.Add(n, k)].Width = w;
        Controls.Add(_g);
        Controls.Add(new Label
        {
            Text = "Eine Tochterperiode übernimmt Unterricht, Stundenplan und Pausenaufsichten der Mutterperiode. An jedem Tag gilt die innerste (zuletzt beginnende) Periode. " +
                   "Unterrichtsänderungen werden in Tochterperioden übernommen, sofern der Unterricht dort nicht abweicht. Fixierte Perioden sind gegen Änderungen geschützt.",
            Left = 12, Top = 410, Width = 500, Height = 40, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        });
        _g.SelectionChanged += (_, _) => { if (!_laedt) Anzeigen(); };
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Aktivieren(); };
        _name.Leave += (_, _) => Uebernehmen();
        _lang.Leave += (_, _) => Uebernehmen();
        _von.ValueChanged += (_, _) => Uebernehmen();
        _bis.ValueChanged += (_, _) => Uebernehmen();
        _fixiert.CheckedChanged += (_, _) => Uebernehmen();
        _loeschen.Click += (_, _) => Loeschen();
        _neu.Click += (_, _) => Neu();
        aktiv.Click += (_, _) => Aktivieren();
        druck.Click += (_, _) => ListenDruck.Drucken(this, "Perioden", new[] { "Name", "Langname", "Von", "Bis", "Fixiert", "Tage", "Mutter-Per." }, new[] { 90, 140, 75, 75, 50, 45, 90 },
            _g.Rows.Cast<DataGridViewRow>().Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "").ToArray()).ToList(), true);
        Fuellen(D.AktivePeriodeId);
    }

    private Periode? Gewaehlt => _g.CurrentRow?.Tag is int id ? D.Perioden.FirstOrDefault(p => p.Id == id) : null;

    private void Fuellen(int? waehlen)
    {
        _laedt = true;
        _g.Rows.Clear();
        // Baumreihenfolge: Mutter vor ihren Töchtern
        void Ebene(int? mutter, int tiefe)
        {
            foreach (var p in D.Perioden.Where(x => x.MutterId == mutter || (mutter is null && x.MutterId is int m && D.Perioden.All(y => y.Id != m))).OrderBy(x => x.Von))
            {
                var r = _g.Rows.Add(new string(' ', tiefe * 3) + p.Name, p.Langname, Format.Datum(p.Von), p.Bis is DateOnly b ? Format.Datum(b) : "", p.Fixiert ? "ja" : "",
                    Core.Module.Perioden.Schultage(D, p), D.Perioden.FirstOrDefault(x => x.Id == p.MutterId)?.Name ?? "");
                _g.Rows[r].Tag = p.Id;
                if (p.Id == D.AktivePeriodeId) _g.Rows[r].DefaultCellStyle.Font = Ui.SchriftFett;
                Ebene(p.Id, tiefe + 1);
            }
        }
        Ebene(null, 0);
        _laedt = false;
        var zeile = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is int id && id == waehlen) ?? (_g.Rows.Count > 0 ? _g.Rows[0] : null);
        if (zeile != null) { _g.ClearSelection(); zeile.Selected = true; _g.CurrentCell = zeile.Cells[0]; }
        Anzeigen();
    }

    private void Anzeigen()
    {
        if (Gewaehlt is not { } p) return;
        _laedt = true;
        _name.Text = p.Name;
        _lang.Text = p.Langname;
        _von.Value = p.Von.ToDateTime(TimeOnly.MinValue);
        _bis.Checked = p.Bis.HasValue;
        _bis.Value = (p.Bis ?? Core.Module.Perioden.Ende(D, p)).ToDateTime(TimeOnly.MinValue);
        _fixiert.Checked = p.Fixiert;
        _tage.Text = Core.Module.Perioden.Schultage(D, p).ToString();
        _loeschen.Enabled = D.Perioden.Count > 1;
        _laedt = false;
    }

    private void Uebernehmen()
    {
        if (_laedt || Gewaehlt is not { } p) return;
        var name = _name.Text;
        var lang = _lang.Text;
        var von = DateOnly.FromDateTime(_von.Value);
        DateOnly? bis = _bis.Checked ? DateOnly.FromDateTime(_bis.Value) : null;
        var fix = _fixiert.Checked;
        if (p.Name == name && p.Langname == lang && p.Von == von && p.Bis == bis && p.Fixiert == fix) return;
        try
        {
            AppZustand.Doc.Aendern("Periode ändern", AenderungsArt.Alles, d => Core.Module.Perioden.Aendern(d, d.Perioden.First(x => x.Id == p.Id), name, lang, von, bis, fix));
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        Fuellen(p.Id);
    }

    private void Neu()
    {
        if (Gewaehlt is not { } mutter) return;
        var ab = Ui.Eingabe($"Neue Periode zur Mutterperiode {mutter.Name} – gültig ab (TT.MM.JJJJ):", "Neue Periode", Format.Datum(AppZustand.Datum < mutter.Von ? mutter.Von : AppZustand.Datum));
        if (ab is null) return;
        try
        {
            var von = Ui.DatumLesen(ab, mutter.Von);
            var bisText = Ui.Eingabe("Gültig bis (leer = bis zur nächsten Periode bzw. Schuljahresende):", "Neue Periode", "");
            if (bisText is null) return;
            DateOnly? bis = bisText.Trim().Length == 0 ? null : Ui.DatumLesen(bisText, von);
            var id = 0;
            AppZustand.Doc.Aendern("Neue Periode", AenderungsArt.Alles, d =>
            {
                var p = Core.Module.Perioden.Neu(d, d.Perioden.First(x => x.Id == mutter.Id), $"Periode{d.Perioden.Count + 1}", von, bis);
                d.AktivePeriodeId = p.Id;
                id = p.Id;
            });
            Fuellen(id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { Ui.Info("Das Datum ist ungültig."); }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    private void Loeschen()
    {
        if (Gewaehlt is not { } p || !Ui.Frage($"Periode {p.Name} mit Unterricht und Stundenplan löschen?")) return;
        try { AppZustand.Doc.Aendern("Periode löschen", AenderungsArt.Alles, d => Core.Module.Perioden.Loeschen(d, d.Perioden.First(x => x.Id == p.Id))); }
        catch (Exception ex) { Ui.Fehler(ex); }
        Fuellen(D.AktivePeriodeId);
    }

    private void Aktivieren()
    {
        if (Gewaehlt is not { } p) return;
        AppZustand.Doc.Aendern("Periode aktivieren", AenderungsArt.Alles, d => d.AktivePeriodeId = p.Id);
        Fuellen(p.Id);
    }
}
