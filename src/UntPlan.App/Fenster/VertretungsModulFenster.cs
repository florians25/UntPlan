using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Kalender der Vertretungsplanung: Kalenderwoche mit Kennzahlen je Tag (offene Vertretungen, abwesende Klassen, Freisetzungen,
/// gesperrte Räume, Raumvertretungen, Tagestexte, Kollisionen), darunter Tagestexte und Kalendertag des Planungsdatums.
/// </summary>
public sealed class KalenderFenster : MdiFenster
{
    private readonly NumericUpDown _kw = new() { Minimum = 1, Maximum = 53, Width = 50 };
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DataGridView _woche = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _texte = new() { Dock = DockStyle.Fill };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _frei = new() { Text = "unterrichtsfrei", AutoSize = true };
    private readonly CheckBox _feiertag = new() { Text = "Feiertag", AutoSize = true };
    private readonly TextBox _stunden = new() { Width = 70 };
    private readonly TextBox _notiz = new() { Width = 260 };
    private static readonly string[] Arten = { "Alle", "Klassen", "Lehrer", "Nur Lehrer" };
    private bool _laedt;

    public KalenderFenster()
    {
        Text = "Kalender";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("kalender");
        Size = new Size(560, 480);
        Positionieren();
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 0, 0), WrapContents = false };
        kopf.Controls.Add(new Label { Text = "Kalenderwoche", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        kopf.Controls.Add(_kw);
        kopf.Controls.Add(_datum);
        _kw.ValueChanged += (_, _) =>
        {
            if (_laedt) return;
            var jahr = AppZustand.Datum.Year;
            var kwJetzt = System.Globalization.ISOWeek.GetWeekOfYear(AppZustand.Datum.ToDateTime(TimeOnly.MinValue));
            if ((int)_kw.Value < 10 && kwJetzt > 40) jahr++;
            if ((int)_kw.Value > 40 && kwJetzt < 10) jahr--;
            AppZustand.DatumSetzen(DateOnly.FromDateTime(System.Globalization.ISOWeek.ToDateTime(jahr, (int)_kw.Value, DayOfWeek.Monday)));
        };
        _datum.ValueChanged += (_, _) => { if (!_laedt) AppZustand.DatumSetzen(DateOnly.FromDateTime(_datum.Value)); };

        Ui.RasterGestalten(_woche, false);
        _woche.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _woche.MultiSelect = false;
        foreach (var (n, k, w) in new[] { ("D", "Datum", 52), ("T", "Tag", 34), ("O", "Offen", 46), ("AK", "Abw.Kl.", 50), ("F", "Freis.", 46), ("BR", "Bl.Räume", 58), ("RV", "Raum-V.", 52), ("TT", "Tagestexte", 66), ("KL", "Koll. Lehrer", 72), ("KR", "Koll. Räume", 72) })
            _woche.Columns[_woche.Columns.Add(n, k)].Width = w;
        _woche.CellClick += (_, e) => { if (e.RowIndex >= 0 && _woche.Rows[e.RowIndex].Tag is DateOnly d) AppZustand.DatumSetzen(d); };
        _woche.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null || _woche.Rows[e.RowIndex].Tag is not DateOnly d) return;
            if (!Kalender.IstUnterrichtstag(D, d)) e.CellStyle.ForeColor = Color.Gray;
            if (d == AppZustand.Datum) e.CellStyle.BackColor = Color.FromArgb(255, 222, 222);
            if (e.ColumnIndex >= 2 && e.Value is int n && n > 0 && _woche.Columns[e.ColumnIndex].Name is "O" or "KL" or "KR") e.CellStyle.ForeColor = Color.Firebrick;
        };

        // Tagestexte direkt in der Tabelle (unten immer eine leere Zeile)
        Ui.RasterGestalten(_texte, true);
        _texte.AllowUserToAddRows = true;
        _texte.AllowUserToDeleteRows = true;
        var art = new DataGridViewComboBoxColumn { Name = "A", HeaderText = "Art", Width = 80, FlatStyle = FlatStyle.Flat };
        art.Items.AddRange(Arten);
        _texte.Columns.Add(art);
        _texte.Columns[_texte.Columns.Add("E", "Elemente")].Width = 90;
        _texte.Columns[_texte.Columns.Add("G", "Gültig bis")].Width = 80;
        _texte.Columns[_texte.Columns.Add("X", "Tagestext")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _texte.Columns.Add(new DataGridViewCheckBoxColumn { Name = "P", HeaderText = "Portal", Width = 50 });
        Steuerelemente.SuchKombi.Aktivieren(_texte);
        _texte.DefaultValuesNeeded += (_, e) => { e.Row.Cells["A"].Value = Arten[0]; e.Row.Cells["G"].Value = Format.Datum(AppZustand.Datum); e.Row.Cells["P"].Value = true; };
        _texte.CellEndEdit += (_, _) => TexteSpeichern();
        _texte.UserDeletedRow += (_, _) => TexteSpeichern();
        _texte.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 4) _texte.EndEdit(); };
        var seiteTexte = new TabPage("Tagestexte");
        seiteTexte.Controls.Add(_texte);

        var tag = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8), WrapContents = false };
        var uebernehmen = new Button { Text = "Kalendertag übernehmen", Width = 180, Height = 26 };
        uebernehmen.Click += (_, _) => KalendertagSpeichern();
        tag.Controls.Add(_frei);
        tag.Controls.Add(_feiertag);
        tag.Controls.Add(new Label { Text = "nur diese Stunden unterrichtsfrei (z. B. 6-8):", AutoSize = true });
        tag.Controls.Add(_stunden);
        tag.Controls.Add(new Label { Text = "Notiz (wird nicht gedruckt):", AutoSize = true });
        tag.Controls.Add(_notiz);
        tag.Controls.Add(uebernehmen);
        var seiteTag = new TabPage("Kalendertag");
        seiteTag.Controls.Add(tag);
        _tabs.TabPages.Add(seiteTexte);
        _tabs.TabPages.Add(seiteTag);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_woche);
        split.Panel2.Controls.Add(_tabs);
        Controls.Add(split);
        Controls.Add(kopf);
        Load += (_, _) => split.SplitterDistance = Math.Max(120, (int)(split.Height * 0.52));
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void DatumGewechselt(DateOnly _)
    {
        if (!IsDisposed) Neuaufbau();
    }

    private void Neuaufbau()
    {
        if (_texte.IsCurrentCellInEditMode) return;
        _laedt = true;
        var datum = AppZustand.Datum;
        _datum.Value = datum.ToDateTime(TimeOnly.MinValue);
        _kw.Value = System.Globalization.ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue));
        _woche.Rows.Clear();
        var montag = Kalender.Montag(datum);
        for (var i = 0; i < 7; i++)
        {
            var t = montag.AddDays(i);
            var liste = D.Vertretungen.Where(v => v.Datum == t).ToList();
            int kollL = 0, kollR = 0;
            if (Kalender.IstUnterrichtstag(D, t) && liste.Count > 0)
            {
                var tp = Tagesplan.Berechnen(D, t);
                kollL = liste.Count(v => v.VertreterId is int l && !v.IstEntfall && !v.Mitbetreuung && v.Art != VertretungsArt.TrotzAbsenz && Vertretungen.LehrerBelegt(D, tp, l, v.Stunde, v) != null);
                kollR = tp.Eintraege.Where(e => e.Findetstatt).SelectMany(e => e.RaumIds.Select(r => (e.Stunde, r))).GroupBy(x => x).Count(g => g.Count() > 1);
            }
            var r = _woche.Rows.Add($"{t:d.M.}", Planungsdienst.TageKurz[i], liste.Count(v => v.Offen),
                D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && t >= a.Von && t <= a.Bis), liste.Count(v => v.Art == VertretungsArt.Freisetzung),
                D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && t >= a.Von && t <= a.Bis), liste.Count(v => v.Art == VertretungsArt.Raumvertretung),
                D.Tagestexte.Count(x => x.GiltAm(t)), kollL, kollR);
            _woche.Rows[r].Tag = t;
            if (t == datum) _woche.CurrentCell = _woche.Rows[r].Cells[0];
        }
        _texte.Rows.Clear();
        var ix = Ix;
        foreach (var x in D.Tagestexte.Where(x => x.GiltAm(datum)))
        {
            var a = x.NurLehrer ? TagestextArt.NurLehrer : x.Art;
            var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            var r = _texte.Rows.Add(Arten[(int)a], string.Join(",", x.ElementIds.Select(id => ix.K(typ, id))), Format.Datum(x.Datum.AddDays(Math.Max(1, x.GueltigTage) - 1)), x.Text, x.Portal);
            _texte.Rows[r].Tag = x;
        }
        _tabs.TabPages[0].Text = $"Tagestexte ({D.Tagestexte.Count(x => x.GiltAm(datum))})";
        var k = Kalender.KalendertagAm(D, datum);
        _frei.Checked = k?.Unterrichtsfrei == true;
        _feiertag.Checked = k?.Feiertag == true;
        _stunden.Text = k?.FreieStunden ?? "";
        _notiz.Text = k?.Notiz ?? "";
        _tabs.TabPages[1].Text = k is null ? "Kalendertag" : "Kalendertag ●";
        _laedt = false;
    }

    private void TexteSpeichern()
    {
        if (_laedt) return;
        var datum = AppZustand.Datum;
        var neu = new List<Tagestext>();
        foreach (DataGridViewRow r in _texte.Rows)
        {
            if (r.IsNewRow || string.IsNullOrWhiteSpace(r.Cells["X"].Value?.ToString())) continue;
            var a = (TagestextArt)Math.Max(0, Array.IndexOf(Arten, r.Cells["A"].Value?.ToString() ?? "Alle"));
            var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            var alt = r.Tag as Tagestext;
            var start = alt?.Datum ?? datum;
            DateOnly bis;
            try { bis = Ui.DatumLesen(r.Cells["G"].Value?.ToString() ?? "", datum); } catch (Exception) { bis = datum; }
            neu.Add(new Tagestext
            {
                Datum = start, Art = a, Text = r.Cells["X"].Value!.ToString()!,
                ElementIds = (r.Cells["E"].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => D.AlleElemente(typ).FirstOrDefault(e => e.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id).OfType<int>().ToList(),
                GueltigTage = Math.Max(1, bis.DayNumber - start.DayNumber + 1), Portal = r.Cells["P"].Value is true,
            });
        }
        Aendern("Tagestexte", AenderungsArt.Vertretung, d =>
        {
            d.Tagestexte.RemoveAll(x => x.GiltAm(datum));
            d.Tagestexte.AddRange(neu);
        });
        BeginInvoke(Neuaufbau);
    }

    private void KalendertagSpeichern()
    {
        var datum = AppZustand.Datum;
        var frei = _frei.Checked;
        var feiertag = _feiertag.Checked;
        var stunden = _stunden.Text.Trim();
        var notiz = _notiz.Text.Trim();
        Aendern("Kalendertag", AenderungsArt.Vertretung, d =>
        {
            d.Kalender.RemoveAll(x => x.Datum == datum);
            if (frei || feiertag || stunden.Length > 0 || notiz.Length > 0)
                d.Kalender.Add(new Kalendertag { Datum = datum, Unterrichtsfrei = frei, Feiertag = feiertag, FreieStunden = stunden, Notiz = notiz });
            Kalender.CacheLeeren();
            Vertretungen.Aktualisieren(d, datum);
        });
    }
}

/// <summary>Absenzen von Lehrern, Klassen und Räumen (Reiter), Zeitraum um das Planungsdatum, Teilen/Kopieren.</summary>
public sealed class AbwesenheitenFenster : MdiFenster
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _zeitraum = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };

    public AbwesenheitenFenster()
    {
        Text = "Absenzen";
        HilfeThema = "Abwesenheiten";
        Icon = SymbolIcon("abwesenheit");
        Size = new Size(760, 300);
        Positionieren();
        foreach (var t in new[] { "Lehrer", "Klassen", "Räume" }) _tabs.TabPages.Add(t);
        _tabs.SelectedIndexChanged += (_, _) => Neuaufbau();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Absenz", "neu", Neu, false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Absenz teilen", "tausch", Teilen));
        ts.Items.Add(Knopf("Absenz kopieren (andere Elemente)", "kopieren", Kopieren));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        _zeitraum.Items.AddRange(new object[] { "Planungsdatum", "Woche", "ab Planungsdatum", "gesamtes Schuljahr" });
        _zeitraum.SelectedIndex = 0;
        _zeitraum.SelectedIndexChanged += (_, _) => Neuaufbau();
        ts.Items.Add(new ToolStripLabel("Zeitraum:"));
        ts.Items.Add(_zeitraum);
        ts.Items.Add(Knopf("Veranstaltungen", "klassen", () => HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster()), false));
        // direkte Eingabe in der Liste: unten steht immer eine leere Zeile für die nächste Absenz
        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nr", HeaderText = "Abs-Nr.", Width = 55, ReadOnly = true });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("E", "Name", 80, Array.Empty<string>()));
        foreach (var (n, k, b) in new[] { ("V", "Von", 88), ("SV", "ab St.", 45), ("B", "Bis", 88), ("SB", "bis St.", 45) })
            _g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("G", "Grund", 90, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Beg", HeaderText = "Begleitlehrer", Width = 100 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "O", HeaderText = "offen", Width = 45, ReadOnly = true });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        Steuerelemente.SuchKombi.Aktivieren(_g, (zelle, text) =>
        {
            Element? el = zelle.OwningColumn.Name == "G" ? D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname == text) : D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname == text);
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el is Lehrer l ? l.VollerName : el.Langname);
        });
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0 && _g.Rows[e.RowIndex].Tag is Abwesenheit) Bearbeiten(); };
        _g.KeyDown += (_, e) =>
        {
            if (_g.IsCurrentCellInEditMode) return;
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.Control) { Loeschen(); e.Handled = true; }
            if (e.KeyCode == Keys.Insert) Neu();
        };
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Neue Absenz…", Symbole.Get("neu"), (_, _) => Neu());
        cm.Items.Add("Bearbeiten…", Symbole.Get("einstellungen"), (_, _) => Bearbeiten());
        cm.Items.Add("Absenz teilen…", Symbole.Get("tausch"), (_, _) => Teilen());
        cm.Items.Add("Absenz kopieren…", null, (_, _) => Kopieren());
        cm.Items.Add("Absenzen zusammenfassen", null, (_, _) => Zusammenfassen());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
        _g.ContextMenuStrip = cm;
        Controls.Add(_g);
        Controls.Add(_tabs);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();
    private void DatumGewechselt(DateOnly _) { if (!IsDisposed) Neuaufbau(); }

    private AbwesenheitsArt Art => (AbwesenheitsArt)Math.Max(0, _tabs.SelectedIndex);

    private ElementTyp Typ => Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
    private bool _laedt;

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        var zelle = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 1, (_g.CurrentRow?.Tag as Abwesenheit)?.Id);
        _laedt = true;
        _g.Rows.Clear();
        var namen = (DataGridViewComboBoxColumn)_g.Columns["E"]!;
        namen.Items.Clear();
        namen.Items.Add("");
        foreach (var el in Stammdaten.Sortiert(D, Typ)) namen.Items.Add(el.Kurzname);
        var gruende = (DataGridViewComboBoxColumn)_g.Columns["G"]!;
        gruende.Items.Clear();
        gruende.Items.Add("");
        foreach (var gr in D.Abwesenheitsgruende) gruende.Items.Add(gr.Kurzname);
        var heute = AppZustand.Datum;
        var (von, bis) = _zeitraum.SelectedIndex switch
        {
            0 => (heute, heute),
            1 => (Kalender.Montag(heute), Kalender.Montag(heute).AddDays(6)),
            2 => (heute, DateOnly.MaxValue),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
        var typ = Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        _g.Columns["Beg"]!.Visible = Art == AbwesenheitsArt.Klasse;
        foreach (var a in D.Abwesenheiten.Where(a => a.Art == Art && a.Bis >= von && a.Von <= bis).OrderBy(a => Ix.K(typ, a.ElementId), StringComparer.Create(Format.De, true)).ThenBy(a => a.Von))
        {
            var offen = D.Vertretungen.Count(v => v.AbwesenheitId == a.Id && v.Offen);
            var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
            var r = _g.Rows.Add(a.Id, Ix.K(typ, a.ElementId), $"{Planungsdienst.TageKurz[((int)a.Von.DayOfWeek + 6) % 7]} {a.Von:dd.MM.yy}", a.StundeVon,
                $"{Planungsdienst.TageKurz[((int)a.Bis.DayOfWeek + 6) % 7]} {a.Bis:dd.MM.yy}", a.StundeBis >= 99 ? "" : a.StundeBis.ToString(), grund,
                string.Join(", ", a.BegleitLehrerIds.Select(Ix.KLehrer)), offen == 0 ? "" : offen.ToString(), a.Text);
            _g.Rows[r].Tag = a;
            if (offen > 0) _g.Rows[r].Cells["O"].Style.ForeColor = Color.Firebrick;
        }
        // leere Eingabezeile
        var neu = _g.Rows.Add("", "", "", "", "", "", "", "", "", "");
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 236);
        _g.Rows[neu].Cells["Nr"].Value = "neu";
        _laedt = false;
        var ziel = zelle.Item3 is int id ? _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag is Abwesenheit ab && ab.Id == id)?.Index ?? zelle.Item1 : zelle.Item1;
        if (ziel >= 0 && ziel < _g.Rows.Count && zelle.Item2 >= 0 && zelle.Item2 < _g.Columns.Count && _g.Columns[zelle.Item2].Visible)
            _g.CurrentCell = _g.Rows[ziel].Cells[zelle.Item2];
        _tabs.TabPages[0].Text = $"Lehrer ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Lehrer && a.Bis >= von && a.Von <= bis)})";
        _tabs.TabPages[1].Text = $"Klassen ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && a.Bis >= von && a.Von <= bis)})";
        _tabs.TabPages[2].Text = $"Räume ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && a.Bis >= von && a.Von <= bis)})";
    }

    private List<Abwesenheit> Markierte() => _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Abwesenheit>().Distinct().ToList();

    /// <summary>Direkte Eingabe: geänderte Zelle übernehmen bzw. aus der leeren Zeile eine neue Absenz anlegen.</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var wert = zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var alt = zeile.Tag as Abwesenheit;
        var a = alt is null ? new Abwesenheit { Art = Art, Von = AppZustand.Datum, Bis = AppZustand.Datum, Benutzer = AppZustand.Benutzer } : Kopie(alt);
        var name = _g.Columns[e.ColumnIndex].Name;
        try
        {
            switch (name)
            {
                case "E":
                    var el = D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                    if (el is null) { if (alt != null) BeginInvoke(Neuaufbau); return; }
                    a.ElementId = el.Id;
                    break;
                case "V":
                    a.Von = Ui.DatumLesen(wert, a.Von);
                    if (a.Bis < a.Von || alt is null) a.Bis = a.Von;
                    break;
                case "B":
                    a.Bis = Ui.DatumLesen(wert, a.Bis);
                    if (a.Bis < a.Von) a.Von = a.Bis;
                    break;
                case "SV": a.StundeVon = wert.Length == 0 ? 1 : Math.Clamp(int.Parse(wert), 1, D.Zeitraster.StundenProTag); break;
                case "SB": a.StundeBis = wert.Length == 0 ? 99 : Math.Clamp(int.Parse(wert), 1, 99); break;
                case "G": a.GrundId = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
                case "Beg":
                    a.BegleitLehrerIds = wert.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => D.Lehrer.FirstOrDefault(l => l.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id).OfType<int>().ToList();
                    break;
                case "T": a.Text = wert; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException) { Ui.Info($"Die Eingabe \"{wert}\" ist ungültig."); BeginInvoke(Neuaufbau); return; }
        if (alt is null)
        {
            // neue Zeile: erst anlegen, wenn ein Element gewählt ist; andere Felder der Zeile mitnehmen
            if (a.ElementId == 0)
            {
                var k = zeile.Cells["E"].Value?.ToString() ?? "";
                var el = D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase));
                if (el is null) return;
                a.ElementId = el.Id;
            }
            Speichern(new[] { a }, Array.Empty<Abwesenheit>(), "Absenz eingeben");
            // weiter in derselben Zeile (Spalte "Von") bzw. in der neuen leeren Zeile
            BeginInvoke(() =>
            {
                var r = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag is Abwesenheit ab && ab.ElementId == a.ElementId && ab.Von == a.Von && ab.Id == a.Id);
                if (r != null && name == "E") _g.CurrentCell = r.Cells["V"];
            });
            return;
        }
        Speichern(new[] { a }, new[] { alt }, "Absenz geändert");
    }

    private static Abwesenheit Kopie(Abwesenheit a) => new()
    {
        Id = a.Id, Art = a.Art, ElementId = a.ElementId, Von = a.Von, Bis = a.Bis, StundeVon = a.StundeVon, StundeBis = a.StundeBis,
        GrundId = a.GrundId, Text = a.Text, BegleitLehrerIds = new List<int>(a.BegleitLehrerIds), Erstellt = a.Erstellt, Benutzer = a.Benutzer,
    };

    private void Neu()
    {
        var a = new Abwesenheit { Art = Art, Von = AppZustand.Datum, Bis = AppZustand.Datum };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, Array.Empty<Abwesenheit>(), "Absenz eingeben");
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var a = Kopie(alt);
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, new[] { alt }, "Absenz geändert");
    }

    private void Teilen()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        if (alt.Von == alt.Bis) { Ui.Info("Eine eintägige Absenz kann nicht geteilt werden."); return; }
        var text = Ui.Eingabe($"Absenz {Format.Datum(alt.Von)} – {Format.Datum(alt.Bis)} teilen: zweiter Teil beginnt am (TT.MM.JJJJ):", "Absenz teilen", Format.Datum(alt.Von.AddDays(1)));
        if (text is null || !DateOnly.TryParse(text, Format.De, out var ab) || ab <= alt.Von || ab > alt.Bis) return;
        var a = Kopie(alt);
        a.Bis = ab.AddDays(-1);
        a.StundeBis = 99;
        var b = Kopie(alt);
        b.Id = 0;
        b.Von = ab;
        b.StundeVon = 1;
        Speichern(new[] { a, b }, new[] { alt }, "Absenz teilen");
    }

    private void Kopieren()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var typ = alt.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        var w = AuswahlDialog.Mehrere(this, typ, "Absenz für weitere Elemente übernehmen", Array.Empty<int>());
        if (w is null || w.Count == 0) return;
        var neu = w.Where(e => e.Id != alt.ElementId).Select(e => { var a = Kopie(alt); a.Id = 0; a.ElementId = e.Id; a.Erstellt = DateTime.Now; return a; }).ToArray();
        Speichern(neu, Array.Empty<Abwesenheit>(), "Absenz kopieren");
    }

    private void Zusammenfassen()
    {
        var m = Markierte().OrderBy(a => a.Von).ToList();
        if (m.Count < 2 || m.Select(a => (a.Art, a.ElementId)).Distinct().Count() > 1) { Ui.Info("Bitte mindestens zwei Absenzen desselben Elements markieren."); return; }
        var a = Kopie(m[0]);
        a.Bis = m.Max(x => x.Bis);
        a.StundeBis = m.First(x => x.Bis == a.Bis).StundeBis;
        Speichern(new[] { a }, m.ToArray(), "Absenzen zusammenfassen");
    }

    private void Speichern(IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt, string text)
    {
        // Hinweis: eingeplante Vertreter, die durch die neue Absenz abwesend werden
        var betroffen = neu.SelectMany(a => Vertretungen.BetroffeneVertreter(D, a)).Distinct().ToList();
        if (betroffen.Count > 0)
            BeginInvoke(() => Ui.Info("Achtung: Folgende Vertretungen waren bereits mit einer Lehrkraft geplant, die nun abwesend ist. " +
                "Sie sind wieder offen und müssen neu vertreten werden:\n\n" +
                string.Join("\n", betroffen.Take(20).Select(v => $"Vtr-Nr. {v.Id}: {Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7]} {Format.Datum(v.Datum)}, {v.Stunde}. Std., {Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)} – Vertreter {Ix.KLehrer(v.VertreterId)}")) +
                (betroffen.Count > 20 ? $"\n… und {betroffen.Count - 20} weitere" : ""), "Vertreter abwesend"));
        var altIds = alt.Select(a => a.Id).ToHashSet();
        Aendern(text, AenderungsArt.Vertretung, d =>
        {
            d.Abwesenheiten.RemoveAll(x => altIds.Contains(x.Id));
            foreach (var a in neu)
            {
                if (a.Id == 0 || !altIds.Contains(a.Id) && d.Abwesenheiten.Any(x => x.Id == a.Id)) a.Id = d.NeueId();
                d.Abwesenheiten.Add(a);
            }
            var alle = neu.Concat(alt).ToList();
            if (alle.Count > 0) Vertretungen.Aktualisieren(d, alle.Min(a => a.Von), alle.Max(a => a.Bis));
        });
    }

    private void Loeschen()
    {
        var liste = Markierte();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Absenz(en) löschen? Daraus entstandene Vertretungen werden entfernt.")) return;
        Speichern(Array.Empty<Abwesenheit>(), liste, "Absenz löschen");
    }
}

/// <summary>Veranstaltungen: Exkursionen, Klassenfahrten, Schulveranstaltungen mit Auswirkung auf Stunden- und Vertretungsplan.</summary>
public sealed class VeranstaltungenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly TextBox _nr = new() { Width = 60 };
    private readonly Steuerelemente.ZeitraumAuswahl _zeitraum;

    public VeranstaltungenFenster()
    {
        Text = "Veranstaltungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("klassen");
        Size = new Size(900, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Veranstaltung", "neu", Neu, false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        // direkte Eingabe; Klassen/Lehrer/Räume als Kurznamen mit Komma, unten immer eine leere Zeile
        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Nr.", 45), ("T", "Veranstaltung", 160), ("V", "Von", 88), ("SV", "ab St.", 45), ("B", "Bis", 88), ("SB", "bis St.", 45), ("K", "Klassen", 100), ("L", "Lehrer", 100), ("R", "Räume", 80) })
            _g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b, ReadOnly = n == "Nr", SortMode = DataGridViewColumnSortMode.NotSortable });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("G", "Grund", 80, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Z", HeaderText = "zählt", Width = 45 });
        Steuerelemente.SuchKombi.Aktivieren(_g);
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _g.Columns[e.ColumnIndex].Name == "Z") _g.EndEdit(); };
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0) Bearbeiten(); };
        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _zeitraum = new Steuerelemente.ZeitraumAuswahl(_von, _bis, "Veranstaltungen");
        _zeitraum.Geaendert += () => { if (!_laedt) Neuaufbau(); };
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(4, 2, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        var gruppe = new GroupBox { Text = "Von-Bis", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), MinimumSize = new Size(0, 50) };
        gruppe.Controls.Add(_zeitraum);
        _zeitraum.Location = new Point(6, 18);
        kopf.Controls.Add(gruppe);
        var unten = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(4, 3, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        unten.Controls.Add(new Label { Text = "VA-Nr.:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        unten.Controls.Add(_nr);
        _nr.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !int.TryParse(_nr.Text, out var nr)) return;
            e.SuppressKeyPress = true;
            if (D.Veranstaltungen.FirstOrDefault(x => x.Id == nr) is not { } va) { Ui.Info($"Die Veranstaltung Nr. {nr} gibt es nicht."); return; }
            if (va.Bis < DateOnly.FromDateTime(_von.Value) || va.Von > DateOnly.FromDateTime(_bis.Value))
            {
                _zeitraum.ArtSetzen(Steuerelemente.Zeitraumart.VonBis);
                _laedt = true;
                _von.Value = va.Von.ToDateTime(TimeOnly.MinValue);
                _bis.Value = va.Bis.ToDateTime(TimeOnly.MinValue);
                _laedt = false;
                Neuaufbau();
            }
            foreach (DataGridViewRow r in _g.Rows) if (r.Tag is Veranstaltung x && x.Id == nr) _g.CurrentCell = r.Cells[1];
        };
        _g.SelectionChanged += (_, _) => { if (!_laedt && !_nr.Focused) _nr.Text = (_g.CurrentRow?.Tag as Veranstaltung)?.Id.ToString() ?? ""; };
        Controls.Add(_g);
        Controls.Add(kopf);
        Controls.Add(unten);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || _zeitraum.Art == Steuerelemente.Zeitraumart.VonBis) return;
        _von.Value = datum.ToDateTime(TimeOnly.MinValue);
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private bool _laedt;

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 1);
        _laedt = true;
        _g.Rows.Clear();
        var von = DateOnly.FromDateTime(_von.Value);
        var bis = DateOnly.FromDateTime(_bis.Value);
        foreach (var v in D.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= bis).OrderBy(v => v.Von))
        {
            var r = _g.Rows.Add(v.Id, v.Text, Format.Datum(v.Von), v.StundeVon, Format.Datum(v.Bis), v.StundeBis >= 99 ? "" : v.StundeBis.ToString(), Ix.KKlassen(v.KlassenIds),
                string.Join(",", v.LehrerIds.Select(Ix.KLehrer)), string.Join(",", v.RaumIds.Select(Ix.KRaum)), D.Abwesenheitsgruende.FirstOrDefault(g => g.Id == v.GrundId)?.Kurzname ?? "", v.Zaehlen);
            _g.Rows[r].Tag = v;
            if (v.Bis < AppZustand.Datum) _g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
        var neu = _g.Rows.Add("neu", "", "", "", "", "", "", "", "", "", false);
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 236);
        _laedt = false;
        if (pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[Math.Clamp(pos.Item2, 0, _g.Columns.Count - 1)];
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var alt = zeile.Tag as Veranstaltung;
        var start = DateOnly.FromDateTime(_von.Value);
        var v = alt is null ? new Veranstaltung { Von = start, Bis = start } : Kopie(alt);
        var wert = zeile.Cells[e.ColumnIndex].Value is bool bw ? (bw ? "1" : "") : zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        try
        {
            switch (_g.Columns[e.ColumnIndex].Name)
            {
                case "T": v.Text = wert; break;
                case "V": v.Von = Ui.DatumLesen(wert, v.Von); if (v.Bis < v.Von || alt is null) v.Bis = v.Von; break;
                case "B": v.Bis = Ui.DatumLesen(wert, v.Bis); if (v.Bis < v.Von) v.Von = v.Bis; break;
                case "SV": v.StundeVon = wert.Length == 0 ? 1 : int.Parse(wert); break;
                case "SB": v.StundeBis = wert.Length == 0 ? 99 : int.Parse(wert); break;
                case "K": v.KlassenIds = Kurznamen(ElementTyp.Klasse, wert); break;
                case "L": v.LehrerIds = Kurznamen(ElementTyp.Lehrer, wert); break;
                case "R": v.RaumIds = Kurznamen(ElementTyp.Raum, wert); break;
                case "G": v.GrundId = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
                case "Z": v.Zaehlen = wert == "1"; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException) { Ui.Info($"Die Eingabe \"{wert}\" ist ungültig."); BeginInvoke(Neuaufbau); return; }
        if (alt is null && string.IsNullOrWhiteSpace(v.Text) && v.KlassenIds.Count == 0 && v.LehrerIds.Count == 0) return;
        if (alt is null && string.IsNullOrWhiteSpace(v.Text)) v.Text = "Veranstaltung";
        Aendern(alt is null ? "Veranstaltung" : "Veranstaltung geändert", AenderungsArt.Vertretung, d =>
        {
            if (alt != null) d.Veranstaltungen.RemoveAll(x => x.Id == alt.Id);
            else v.Id = d.NeueId();
            d.Veranstaltungen.Add(v);
            Vertretungen.Aktualisieren(d, alt is null || v.Von < alt.Von ? v.Von : alt.Von, alt is null || v.Bis > alt.Bis ? v.Bis : alt.Bis);
        });
    }

    internal static List<int> Kurznamen(ElementTyp typ, string text)
    {
        var alle = AppZustand.D.AlleElemente(typ).ToList();
        var res = new List<int>();
        foreach (var k in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            res.Add(alle.FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"{Ui.TypName(typ)} {k} unbekannt"));
        return res.Distinct().ToList();
    }

    private static Veranstaltung Kopie(Veranstaltung v) => System.Text.Json.JsonSerializer.Deserialize<Veranstaltung>(System.Text.Json.JsonSerializer.Serialize(v))!;

    private void Neu()
    {
        var v = new Veranstaltung { Von = AppZustand.Datum, Bis = AppZustand.Datum, Text = "Exkursion" };
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Aendern("Veranstaltung", AenderungsArt.Vertretung, d =>
        {
            v.Id = d.NeueId();
            d.Veranstaltungen.Add(v);
            Vertretungen.Aktualisieren(d, v.Von, v.Bis);
        });
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Veranstaltung alt) return;
        var v = Kopie(alt);
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Aendern("Veranstaltung geändert", AenderungsArt.Vertretung, d =>
        {
            d.Veranstaltungen.RemoveAll(x => x.Id == alt.Id);
            d.Veranstaltungen.Add(v);
            Vertretungen.Aktualisieren(d, alt.Von < v.Von ? alt.Von : v.Von, alt.Bis > v.Bis ? alt.Bis : v.Bis);
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Veranstaltung>().Distinct().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Veranstaltung(en) löschen?")) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Veranstaltung löschen", AenderungsArt.Vertretung, d =>
        {
            d.Veranstaltungen.RemoveAll(v => ids.Contains(v.Id));
            Vertretungen.Aktualisieren(d, liste.Min(v => v.Von), liste.Max(v => v.Bis));
        });
    }
}

/// <summary>Klausuren (Prüfungen) mit Aufsichten; kollidierender Unterricht der Klassen entfällt.</summary>
public sealed class KlausurenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public KlausurenFenster()
    {
        Text = "Klausuren";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("kommentar");
        Size = new Size(860, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Klausur", "neu", () => KlausurDialog.Neu(this, AppZustand.Datum, 1, null), false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        foreach (var (n, k, b) in new[] { ("N", "Name", 150), ("D", "Datum", 95), ("S", "Stunden", 60), ("K", "Klassen", 110), ("L", "Aufsicht", 110), ("R", "Räume", 90) })
            _g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b, SortMode = DataGridViewColumnSortMode.NotSortable });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "E", HeaderText = "Unterr. entfällt", Width = 95 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "A", HeaderText = "aktiv", Width = 45 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _g.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _g.EndEdit(); };
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0 && _g.Rows[e.RowIndex].Tag is Klausur) Bearbeiten(); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private bool _laedt;

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 0);
        _laedt = true;
        _g.Rows.Clear();
        foreach (var k in D.Klausuren.OrderBy(k => k.Datum).ThenBy(k => k.StundeVon))
        {
            var r = _g.Rows.Add(k.Name, $"{Planungsdienst.TageKurz[((int)k.Datum.DayOfWeek + 6) % 7]} {Format.Datum(k.Datum)}", k.StundeVon == k.StundeBis ? k.StundeVon.ToString() : $"{k.StundeVon}-{k.StundeBis}",
                Ix.KKlassen(k.KlassenIds), string.Join(",", k.LehrerIds.Select(Ix.KLehrer)), string.Join(",", k.RaumIds.Select(Ix.KRaum)), k.UnterrichtEntfaellt, k.Aktiv, k.Text);
            _g.Rows[r].Tag = k;
        }
        var neu = _g.Rows.Add("", "", "", "", "", "", true, true, "");
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 236);
        _laedt = false;
        if (pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[Math.Clamp(pos.Item2, 0, _g.Columns.Count - 1)];
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var alt = zeile.Tag as Klausur;
        var k = alt is null ? new Klausur { Datum = AppZustand.Datum, Name = "Klausur" } : System.Text.Json.JsonSerializer.Deserialize<Klausur>(System.Text.Json.JsonSerializer.Serialize(alt))!;
        var roh = zeile.Cells[e.ColumnIndex].Value;
        var wert = roh is bool b ? (b ? "1" : "") : roh?.ToString()?.Trim() ?? "";
        try
        {
            switch (_g.Columns[e.ColumnIndex].Name)
            {
                case "N": k.Name = wert.Length == 0 ? "Klausur" : wert; break;
                case "D": k.Datum = Ui.DatumLesen(wert, k.Datum); break;
                case "S":
                    var b2 = Bereich.Parse(wert.Replace('-', '-'));
                    k.StundeVon = Math.Max(1, b2.Min);
                    k.StundeBis = Math.Max(k.StundeVon, b2.Max);
                    break;
                case "K": k.KlassenIds = VeranstaltungenFenster.Kurznamen(ElementTyp.Klasse, wert); break;
                case "L": k.LehrerIds = VeranstaltungenFenster.Kurznamen(ElementTyp.Lehrer, wert); break;
                case "R": k.RaumIds = VeranstaltungenFenster.Kurznamen(ElementTyp.Raum, wert); break;
                case "E": k.UnterrichtEntfaellt = wert == "1"; break;
                case "A": k.Aktiv = wert == "1"; break;
                case "T": k.Text = wert; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException) { Ui.Info($"Die Eingabe \"{wert}\" ist ungültig."); BeginInvoke(Neuaufbau); return; }
        if (alt is null && k.KlassenIds.Count == 0 && _g.Columns[e.ColumnIndex].Name is not ("K" or "N")) return;
        Aendern(alt is null ? "Klausur anlegen" : "Klausur geändert", AenderungsArt.Vertretung, d =>
        {
            if (alt != null) d.Klausuren.RemoveAll(x => x.Id == alt.Id);
            else k.Id = d.NeueId();
            d.Klausuren.Add(k);
            Vertretungen.Aktualisieren(d, k.Datum);
            if (alt != null && alt.Datum != k.Datum) Vertretungen.Aktualisieren(d, alt.Datum);
        });
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Klausur alt) return;
        var k = System.Text.Json.JsonSerializer.Deserialize<Klausur>(System.Text.Json.JsonSerializer.Serialize(alt))!;
        if (!KlausurDialog.Zeigen(this, k)) return;
        Aendern("Klausur geändert", AenderungsArt.Vertretung, d =>
        {
            d.Klausuren.RemoveAll(x => x.Id == alt.Id);
            d.Klausuren.Add(k);
            Vertretungen.Aktualisieren(d, alt.Datum);
            if (k.Datum != alt.Datum) Vertretungen.Aktualisieren(d, k.Datum);
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Klausur>().Distinct().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Klausur(en) löschen?")) return;
        var ids = liste.Select(k => k.Id).ToHashSet();
        Aendern("Klausur löschen", AenderungsArt.Vertretung, d =>
        {
            d.Klausuren.RemoveAll(k => ids.Contains(k.Id));
            foreach (var t in liste.Select(k => k.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        });
    }
}

/// <summary>Vormerkungen: beiseitegelegte bzw. noch zu verplanende Stunden. Ziehen in den tagesaktuellen Stundenplan verplant die Vormerkung.</summary>
public sealed class VormerkungenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripButton _alle = new("Auch verplante") { CheckOnClick = true };

    public VormerkungenFenster()
    {
        Text = "Vormerkungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("zurueck");
        Size = new Size(900, 300);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Vormerkung", "neu", Neu, false));
        ts.Items.Add(Knopf("Am Planungsdatum verplanen…", "planen", Verplanen, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(_alle);
        ts.Items.Add(new ToolStripLabel("   Vormerkung mit der Maus in einen tagesaktuellen Stundenplan ziehen, um sie zu verplanen.") { ForeColor = Color.DimGray });
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Nr.", 45), ("Q", "Herkunft", 85), ("S", "Std.", 38), ("O", "offen", 42), ("F", "Fach", 55), ("L", "Lehrer", 75), ("K", "Klassen", 85), ("R", "Raum", 55), ("U", "ursprünglich", 110), ("G", "gültig", 140), ("T", "Text", 200), ("V", "verplant am", 120) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Point start = Point.Empty;
        _g.MouseDown += (_, e) => start = e.Location;
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 6) return;
            var hit = _g.HitTest(start.X, start.Y);
            if (hit.RowIndex >= 0 && _g.Rows[hit.RowIndex].Tag is Vormerkung vm && vm.Offen > 0) _g.DoDragDrop("vormerkung:" + vm.Id, DragDropEffects.Move);
        };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var v in D.Vormerkungen.Where(v => _alle.Checked || v.Offen > 0).OrderBy(v => v.Datum ?? DateOnly.MaxValue))
        {
            var r = _g.Rows.Add(v.Id, v.Quelle switch { VormerkungsQuelle.Vertretung => "Vertretung", VormerkungsQuelle.Verdraengung => "Verdrängung", VormerkungsQuelle.Stundenplan => "Stundenplan", VormerkungsQuelle.Unterricht => "Unterricht", _ => "konkret" },
                v.Stunden, v.Offen, Ix.KFach(v.FachId), string.Join(",", v.LehrerIds.Select(Ix.KLehrer)), Ix.KKlassen(v.KlassenIds), Ix.KRaum(v.RaumId),
                v.Datum is DateOnly dd ? $"{Format.Datum(dd)} / {v.Stunde}" : "", v.GueltigVon.HasValue || v.GueltigBis.HasValue ? $"{(v.GueltigVon is DateOnly a ? Format.Datum(a) : "")} – {(v.GueltigBis is DateOnly b ? Format.Datum(b) : "")}" : "",
                v.Text, string.Join(", ", v.VerplantAm.Select(Format.Datum)));
            _g.Rows[r].Tag = v;
            if (v.Offen == 0) _g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private void Neu()
    {
        using var f = new Dialog("Vormerkung", 460, 250);
        var y = 14;
        var fa = Ui.Zeile(f, "Fach:", ElementListe.Combo(ElementTyp.Fach, null), ref y, 120, 300);
        var le = Ui.Zeile(f, "Lehrer:", new ElementWahl(ElementTyp.Lehrer), ref y, 120, 300);
        var kl = Ui.Zeile(f, "Klassen:", new ElementWahl(ElementTyp.Klasse), ref y, 120, 300);
        var ra = Ui.Zeile(f, "Raum:", ElementListe.Combo(ElementTyp.Raum, null), ref y, 120, 300);
        var st = Ui.Zeile(f, "Stunden:", new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1 }, ref y, 120, 60);
        var tx = Ui.Zeile(f, "Text:", new TextBox(), ref y, 120, 300);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var vm = new Vormerkung { Quelle = VormerkungsQuelle.Konkret, FachId = ElementListe.Id(fa), LehrerIds = le.Ids, KlassenIds = kl.Ids, RaumId = ElementListe.Id(ra), Stunden = (int)st.Value, Text = tx.Text };
        Aendern("Vormerkung", AenderungsArt.Vertretung, d => { vm.Id = d.NeueId(); d.Vormerkungen.Add(vm); });
    }

    private void Verplanen()
    {
        if (_g.CurrentRow?.Tag is not Vormerkung vm) return;
        var s = Ui.Eingabe($"Vormerkung am {Format.Datum(AppZustand.Datum)} verplanen – Stunde:", "Vormerkung verplanen", "1");
        if (s is null || !int.TryParse(s, out var stunde)) return;
        Aendern("Vormerkung verplanen", AenderungsArt.Vertretung, d => Vertretungen.VormerkungVerplanen(d, d.Vormerkungen.First(x => x.Id == vm.Id), AppZustand.Datum, stunde, AppZustand.Benutzer));
    }

    private void Loeschen()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Vormerkung>().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Vormerkung(en) löschen?")) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Vormerkung löschen", AenderungsArt.Vertretung, d => d.Vormerkungen.RemoveAll(v => ids.Contains(v.Id)));
    }
}

/// <summary>Bereitschaften am Planungsdatum: wer hat zu welcher Stunde Bereitschaft, und ist sie bereits verplant?</summary>
public sealed class BereitschaftenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public BereitschaftenFenster()
    {
        Text = "Bereitschaften";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("lehrer");
        Size = new Size(520, 300);
        Positionieren();
        Ui.RasterGestalten(_g, false);
        foreach (var (n, k, b) in new[] { ("S", "Std.", 40), ("L", "Lehrer", 80), ("F", "Bereitschaft", 90), ("V", "eingesetzt für", 180), ("Z", "Zähler", 55) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["V"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Controls.Add(_g);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();
    private void DatumGewechselt(DateOnly _) { if (!IsDisposed) Neuaufbau(); }

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        var datum = AppZustand.Datum;
        Text = $"Bereitschaften – {Format.Datum(datum)}";
        var tp = Tagesplan.Berechnen(D, datum);
        var (zv, zb) = Zaehlung.Zeitraum(D, datum);
        foreach (var e in tp.Regel.Where(r => Vertretungen.IstBereitschaftsfach(D, r.FachId) && r.LehrerId.HasValue).OrderBy(r => r.Stunde).ThenBy(r => Ix.KLehrer(r.LehrerId)))
        {
            var l = e.LehrerId!.Value;
            var einsatz = D.Vertretungen.Where(v => v.Datum == datum && v.Stunde == e.Stunde && v.VertreterId == l && v.LehrerId != l).ToList();
            var abwesend = Absenzen.LehrerAbwesend(D, l, datum, e.Stunde);
            var r = _g.Rows.Add(e.Stunde, Ix.KLehrer(l), Ix.KFach(e.FachId),
                abwesend ? "(abwesend)" : string.Join("; ", einsatz.Select(v => $"{Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)} ({Ix.KLehrer(v.LehrerId)})")),
                Ix.Lehrer.TryGetValue(l, out var le) ? Zaehlung.Lehrer(D, le, zv, zb).Summe.ToString("0.#") : "");
            _g.Rows[r].DefaultCellStyle.BackColor = abwesend ? Color.FromArgb(240, 240, 240) : einsatz.Count > 0 ? Color.FromArgb(255, 240, 210) : Color.FromArgb(220, 242, 220);
        }
        if (_g.Rows.Count == 0) _g.Rows.Add("", "", "", "Keine Bereitschaften an diesem Tag (Fach mit Kennzeichen \"Bereitschaft\" im Stundenplan).");
    }
}
