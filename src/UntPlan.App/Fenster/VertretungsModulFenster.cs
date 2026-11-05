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
    private readonly Label _tagTitel = new() { AutoSize = true, Font = new Font("Segoe UI", 10f), Location = new Point(14, 12) };
    private readonly RadioButton _normal = new() { Text = "Normaler Schultag", AutoSize = true };
    private readonly RadioButton _feiertag = new() { Text = "Feiertag", AutoSize = true };
    private readonly RadioButton _frei = new() { Text = "Unterrichtsfrei", AutoSize = true };
    private readonly RadioButton _nurStunden = new() { Text = "Nur diese Stunden unterrichtsfrei", AutoSize = true };
    private readonly ComboBox _grund = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _stunden = new() { Width = 200 };
    private readonly TextBox _notiz = new() { Width = 300 };
    private static readonly string[] Arten = { "Alle", "Klassen", "Lehrer", "Nur Lehrer" };
    private readonly ToolTip _tipp = new();
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
        // Spalten wie im Vorbild (Handbuch Kap. 4.4.1): Lehrer | Pausenaufsichten | Klassen | Räume | Tagestexte | Kollisionen
        foreach (var (n, k, w) in new[]
        {
            ("D", "Datum", 46), ("T", "Tag", 34), ("AL", "Abw.Le.", 52), ("V", "Vertr.", 44), ("O", "Offen", 44), ("PV", "PA-Vertr.", 58), ("PO", "Offen (Paus.)", 76),
            ("AK", "Abw.Kl.", 50), ("F", "Freis.", 44), ("BR", "Bl.Räume.", 62), ("RV", "Raum-Vtr.", 62), ("RO", "Offen (Raum)", 76), ("TT", "Tagestexte", 66),
            ("KL", "Koll. Lehrer", 72), ("KR", "Koll. Räume", 72),
        })
        {
            var c = _woche.Columns[_woche.Columns.Add(n, k)];
            c.Width = w;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        _woche.CellClick += (_, e) => { if (e.RowIndex >= 0 && _woche.Rows[e.RowIndex].Tag is DateOnly d) AppZustand.DatumSetzen(d); };
        _woche.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null || _woche.Rows[e.RowIndex].Tag is not DateOnly d) return;
            if (!Kalender.IstUnterrichtstag(D, d)) e.CellStyle.ForeColor = Color.Gray;
            if (d == AppZustand.Datum) e.CellStyle.BackColor = Color.FromArgb(255, 222, 222);
            if (e.ColumnIndex >= 2 && e.Value is int n && n > 0 && _woche.Columns[e.ColumnIndex].Name is "O" or "PO" or "RO" or "KL" or "KR") e.CellStyle.ForeColor = Color.Firebrick;
        };

        // Tagestexte direkt in der Tabelle (unten immer eine leere Zeile) wie im Vorbild: Gültig bis | Tagestext […] | Webportal;
        // „…“ in der Textzelle öffnet den Dialog für formatierte Tagestexte (Fett, Kursiv, Unterstrichen, Farbe)
        Ui.RasterGestalten(_texte, true);
        _texte.AllowUserToAddRows = true;
        _texte.AllowUserToDeleteRows = true;
        _texte.Columns.Add(Steuerelemente.DatumSpalte.Neu("G", "Gültig bis", 90));
        var textSpalte = _texte.Columns[_texte.Columns.Add("X", "Tagestext")];
        textSpalte.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        textSpalte.MinimumWidth = 80;
        _texte.Columns.Add(new DataGridViewCheckBoxColumn { Name = "P", HeaderText = "Webportal", Width = 62 });
        var art = new DataGridViewComboBoxColumn { Name = "A", HeaderText = "Art", Width = 80, FlatStyle = FlatStyle.Flat };
        art.Items.AddRange(Arten);
        _texte.Columns.Add(art);
        _texte.Columns[_texte.Columns.Add("E", "Elemente")].Width = 90;
        // Reihenfolge wie im Handbuch: Art | Elemente | Gültig bis | Tagestext | Webportal
        var reihe = new[] { "A", "E", "G", "X", "P" };
        for (var i = 0; i < reihe.Length; i++) _texte.Columns[reihe[i]]!.DisplayIndex = i;
        Steuerelemente.SuchKombi.Aktivieren(_texte);
        _texte.DefaultValuesNeeded += (_, e) => { e.Row.Cells["A"].Value = Arten[0]; e.Row.Cells["G"].Value = Format.Datum(AppZustand.Datum); e.Row.Cells["P"].Value = true; };
        _texte.CellEndEdit += (_, _) => TexteSpeichern();
        _texte.UserDeletedRow += (_, _) => TexteSpeichern();
        _texte.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _texte.Columns[e.ColumnIndex].Name == "P") _texte.EndEdit(); };
        _texte.CellPainting += TextKnopfZeichnen;
        _texte.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _texte.Columns[e.ColumnIndex].Name != "X" || e.Button != MouseButtons.Left) return;
            var breite = _texte.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false).Width;
            if (e.X >= breite - KnopfBreite - 2) BeginInvoke(() => TextFormatieren(e.RowIndex));
        };
        _texte.CellFormatting += (_, e) =>
        {
            // formatierte Tagestexte fett kennzeichnen (die Formatierung selbst zeigt der Dialog)
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _texte.Columns[e.ColumnIndex].Name != "X" || e.CellStyle is null) return;
            // mehrzeilige Texte einzeilig anzeigen (der gespeicherte Text bleibt unverändert)
            if (e.Value is string mz && mz.Contains('\n')) { e.Value = mz.Replace("\r", "").Replace("\n", " "); e.FormattingApplied = true; }
            if (_texte.Rows[e.RowIndex].Tag is Tagestext { TextHtml.Length: > 0 }) e.CellStyle.Font = _fett;
        };
        var seiteTexte = new TabPage("Tagestexte");
        seiteTexte.Controls.Add(_texte);

        // Kalendertag wie im Vorbild: Datum, Normaler Schultag / Feiertag / Unterrichtsfrei [Grund] / Nur diese Stunden unterrichtsfrei [Stunden], Notiz
        var tag = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 240, 236), Padding = new Padding(8) };
        var rahmen = new Panel { Location = new Point(6, 6), Size = new Size(520, 210), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(232, 240, 236) };
        rahmen.Controls.Add(_tagTitel);
        var y = 42;
        foreach (var rb in new[] { _normal, _feiertag, _frei, _nurStunden })
        {
            rb.Location = new Point(14, y);
            rahmen.Controls.Add(rb);
            rb.CheckedChanged += (_, _) => { if (rb.Checked) KalendertagGeaendert(); };
            y += 28;
        }
        _grund.Location = new Point(290, _frei.Top - 2);
        _stunden.Location = new Point(290, _nurStunden.Top - 2);
        _grund.SelectedIndexChanged += (_, _) => { if (_frei.Checked) KalendertagGeaendert(); };
        _stunden.Enter += (_, _) => { if (!_nurStunden.Checked) { _laedt = true; _nurStunden.Checked = true; _laedt = false; } };
        _stunden.Validated += (_, _) => { if (_nurStunden.Checked) KalendertagGeaendert(); };
        rahmen.Controls.Add(_grund);
        rahmen.Controls.Add(_stunden);
        var notizLabel = new Label { Text = "Notiz", AutoSize = true, Location = new Point(16, y + 10) };
        _notiz.Location = new Point(140, y + 6);
        _notiz.Width = 364;
        _notiz.Validated += (_, _) => KalendertagGeaendert();
        rahmen.Controls.Add(notizLabel);
        rahmen.Controls.Add(_notiz);
        tag.Controls.Add(rahmen);
        tag.Resize += (_, _) => rahmen.Width = Math.Max(420, tag.ClientSize.Width - 14);
        rahmen.Resize += (_, _) =>
        {
            var rechts = rahmen.ClientSize.Width - 14;
            _grund.Width = _stunden.Width = Math.Max(120, rechts - 290);
            _notiz.Width = Math.Max(160, rechts - 140);
        };
        var seiteTag = new TabPage("Kalendertag");
        seiteTag.Controls.Add(tag);
        _tabs.TabPages.Add(seiteTexte);
        _tabs.TabPages.Add(seiteTag);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_woche);
        split.Panel2.Controls.Add(_tabs);
        // wie im Vorbild: kleiner schwarzer Pfeil links unten klappt den Detailbereich (Tagestexte, Kalendertag) aus bzw. ein
        var klappe = new Panel { Dock = DockStyle.Bottom, Height = 24 };
        var pfeil = new Button { Size = new Size(20, 18), Location = new Point(4, 3), FlatStyle = FlatStyle.Flat, BackColor = Color.White, UseVisualStyleBackColor = false };
        pfeil.FlatAppearance.BorderColor = Color.Gray;
        klappe.Controls.Add(pfeil);
        split.Panel1.Controls.Add(klappe);
        void Klappen(bool offen)
        {
            split.Panel2Collapsed = !offen;
            pfeil.Text = offen ? "▴" : "▾";
            _tipp.SetToolTip(pfeil, offen ? "Detailbereich einklappen" : "Detailbereich ausklappen");
            AppZustand.Einstellungen.KalenderDetailOffen = offen;
        }
        pfeil.Click += (_, _) => { Klappen(split.Panel2Collapsed); AppZustand.Einstellungen.Speichern(); };
        Controls.Add(split);
        Controls.Add(kopf);
        Load += (_, _) =>
        {
            split.SplitterDistance = Math.Max(120, (int)(split.Height * 0.52));
            Klappen(AppZustand.Einstellungen.KalenderDetailOffen);
        };
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
            // an unterrichtsfreien Tagen (Wochenende, Ferien) bleiben die Kennzahlen leer wie im Vorbild
            var r = Kalender.IstUnterrichtstag(D, t) || liste.Count > 0
                ? _woche.Rows.Add($"{t:d.M.}", Planungsdienst.TageKurz[i],
                    D.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && t >= a.Von && t <= a.Bis).Select(a => a.ElementId).Distinct().Count(),
                    liste.Count(v => v.GangId is null && v.Art != VertretungsArt.Raumvertretung && v.Art != VertretungsArt.Freisetzung),
                    liste.Count(v => v.GangId is null && v.Offen),
                    liste.Count(v => v.GangId != null), liste.Count(v => v.GangId != null && v.Offen),
                    D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && t >= a.Von && t <= a.Bis), liste.Count(v => v.Art == VertretungsArt.Freisetzung),
                    D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && t >= a.Von && t <= a.Bis), liste.Count(v => v.Art == VertretungsArt.Raumvertretung),
                    liste.Count(v => v.RaumOffen), D.Tagestexte.Count(x => x.GiltAm(t)), kollL, kollR)
                : _woche.Rows.Add($"{t:d.M.}", Planungsdienst.TageKurz[i]);
            _woche.Rows[r].Tag = t;
            if (t == datum) _woche.CurrentCell = _woche.Rows[r].Cells[0];
        }
        _texte.Rows.Clear();
        var ix = Ix;
        foreach (var x in D.Tagestexte.Where(x => x.GiltAm(datum)))
        {
            var a = x.NurLehrer ? TagestextArt.NurLehrer : x.Art;
            var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            var r = _texte.Rows.Add(Format.Datum(x.Datum.AddDays(Math.Max(1, x.GueltigTage) - 1)), x.Text, x.Portal, Arten[(int)a], string.Join(",", x.ElementIds.Select(id => ix.K(typ, id))));
            _texte.Rows[r].Tag = x;
        }
        _tabs.TabPages[0].Text = $"Tagestexte ({D.Tagestexte.Count(x => x.GiltAm(datum))})";
        var k = Kalender.KalendertagAm(D, datum);
        _tagTitel.Text = $"{Format.De.DateTimeFormat.GetDayName(datum.DayOfWeek)} {datum.Day}.{datum.Month}.{datum.Year}";
        _grund.Items.Clear();
        _grund.Items.Add("Kein Grund");
        foreach (var g in D.Abwesenheitsgruende) _grund.Items.Add(new GrundEintrag(g));
        _grund.SelectedIndex = k?.GrundId is int gid && D.Abwesenheitsgruende.FindIndex(g => g.Id == gid) is var gi and >= 0 ? gi + 1 : 0;
        _feiertag.Checked = k?.Feiertag == true;
        _frei.Checked = k is { Unterrichtsfrei: true, Feiertag: false };
        _nurStunden.Checked = k is { Unterrichtsfrei: false, Feiertag: false } && k.FreieStunden.Length > 0;
        _normal.Checked = !_feiertag.Checked && !_frei.Checked && !_nurStunden.Checked;
        _stunden.Text = k?.FreieStunden ?? "";
        _notiz.Text = k?.Notiz ?? "";
        _tabs.TabPages[1].Text = "Kalendertag";
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
            var text = r.Cells["X"].Value!.ToString()!;
            // Formatierung bleibt erhalten, solange der Text nicht direkt in der Zelle geändert wurde
            var formatiert = alt != null && alt.Text == text && alt.TextHtml.Length > 0;
            neu.Add(new Tagestext
            {
                Datum = start, Art = a, Text = text, TextRtf = formatiert ? alt!.TextRtf : "", TextHtml = formatiert ? alt!.TextHtml : "",
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

    private const int KnopfBreite = 18;
    private readonly Font _fett = new(Ui.Schrift, FontStyle.Bold);

    /// <summary>„…“-Knopf rechts in der Tagestext-Zelle (öffnet den Formatierungsdialog).</summary>
    private void TextKnopfZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || _texte.Columns[e.ColumnIndex].Name != "X") return;
        e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
        var r = new Rectangle(e.CellBounds.Right - KnopfBreite - 2, e.CellBounds.Y + 2, KnopfBreite, e.CellBounds.Height - 5);
        ButtonRenderer.DrawButton(e.Graphics, r, "…", Ui.SchriftKlein, false, System.Windows.Forms.VisualStyles.PushButtonState.Normal);
        e.Handled = true;
    }

    /// <summary>Formatierten Tagestext im Dialog „Tagestexte“ bearbeiten (neue Zeile: neuer Tagestext).</summary>
    internal void TextFormatieren(int zeile)
    {
        if (IsDisposed || zeile < 0 || zeile >= _texte.Rows.Count) return;
        if (_texte.IsCurrentCellInEditMode) _texte.EndEdit();
        var r = _texte.Rows[zeile];
        var datum = AppZustand.Datum;
        var alt = r.Tag as Tagestext;
        var t = new Tagestext
        {
            Datum = alt?.Datum ?? datum, Text = r.Cells["X"].Value?.ToString() ?? "", TextRtf = alt?.TextRtf ?? "", TextHtml = alt?.TextHtml ?? "",
        };
        if (alt != null && alt.Text != t.Text) { t.TextRtf = ""; t.TextHtml = ""; }
        if (!TagestextFormatDialog.Bearbeiten(this, datum, t)) return;
        if (IsDisposed) return;
        // während der Dialog offen war, wurde die Tabelle neu aufgebaut (Speichern beim Beenden der Zellbearbeitung):
        // die gemerkte Zeile gehört dann nicht mehr zur Tabelle – dieselbe Zeile neu suchen
        if (r.DataGridView != _texte || r.Index < 0)
        {
            var gleich = alt is null ? null : _texte.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => !x.IsNewRow && x.Tag is Tagestext tt && tt.Datum == alt.Datum && tt.Text == alt.Text);
            r = gleich ?? (zeile < _texte.Rows.Count ? _texte.Rows[zeile] : _texte.Rows[_texte.NewRowIndex]);
        }
        if (r.IsNewRow)
        {
            var neu = _texte.Rows.Add(Format.Datum(datum), t.Text, true, Arten[0], "");
            r = _texte.Rows[neu];
        }
        else r.Cells["X"].Value = t.Text;
        r.Tag = t;
        TexteSpeichern();
    }

    private sealed record GrundEintrag(Abwesenheitsgrund G)
    {
        public override string ToString() => G.Langname.Length > 0 ? $"{G.Kurzname} – {G.Langname}" : G.Kurzname;
    }

    private void KalendertagGeaendert()
    {
        if (_laedt) return;
        KalendertagSpeichern();
    }

    private void KalendertagSpeichern()
    {
        var datum = AppZustand.Datum;
        var frei = _frei.Checked;
        var feiertag = _feiertag.Checked;
        var stunden = _nurStunden.Checked ? _stunden.Text.Trim() : "";
        var notiz = _notiz.Text.Trim();
        int? grund = frei && _grund.SelectedItem is GrundEintrag ge ? ge.G.Id : null;
        var alt = Kalender.KalendertagAm(D, datum);
        if (alt != null && alt.Unterrichtsfrei == frei && alt.Feiertag == feiertag && alt.FreieStunden == stunden && alt.Notiz == notiz && alt.GrundId == grund) return;
        if (alt is null && !frei && !feiertag && stunden.Length == 0 && notiz.Length == 0) return;
        Aendern("Kalendertag", AenderungsArt.Vertretung, d =>
        {
            d.Kalender.RemoveAll(x => x.Datum == datum);
            if (frei || feiertag || stunden.Length > 0 || notiz.Length > 0)
                d.Kalender.Add(new Kalendertag { Datum = datum, Unterrichtsfrei = frei, Feiertag = feiertag, FreieStunden = stunden, Notiz = notiz, GrundId = grund });
            Kalender.CacheLeeren();
            Vertretungen.Aktualisieren(d, datum);
        });
    }

    /// <summary>Selbsttest: Kalendertag-Reiter zeigen.</summary>
    internal void SelbsttestKalendertag()
    {
        if (_tabs.Parent?.Parent is SplitContainer sc) sc.Panel2Collapsed = false;
        _tabs.SelectedIndex = 1;
    }
}

/// <summary>Klausuren (Prüfungen) mit Aufsichten; kollidierender Unterricht der Klassen entfällt.</summary>
public sealed class KlausurenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly ToolStripButton _ganzesJahr = new("Ganzes Schuljahr") { CheckOnClick = true, Checked = true, ToolTipText = "Zwischen dem gewählten Zeitbereich und dem ganzen Schuljahr wechseln" };
    private readonly ToolStripComboBox _klasse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, ToolTipText = "Ansicht auf eine Klasse einschränken" };

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
        // wie im Handbuch: Zeitbereich (Datumsfelder), „Ganzes Schuljahr“ und Einschränkung auf eine Klasse
        ts.Items.Add(new ToolStripSeparator());
        var heute = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _von.Value = heute;
        _bis.Value = heute.AddDays(27);
        ts.Items.Add(new ToolStripControlHost(_von) { ToolTipText = "Zeitbereich von" });
        ts.Items.Add(new ToolStripLabel("-"));
        ts.Items.Add(new ToolStripControlHost(_bis) { ToolTipText = "Zeitbereich bis" });
        _ganzesJahr.Image = Symbole.Get("kalender", 16);
        ts.Items.Add(_ganzesJahr);
        _klasse.Items.Add("Alle Klassen");
        foreach (var k in Stammdaten.Sortiert(D.Klassen)) _klasse.Items.Add(k.Kurzname);
        _klasse.SelectedIndex = 0;
        ts.Items.Add(_klasse);
        _von.ValueChanged += (_, _) => { if (_bis.Value < _von.Value) _bis.Value = _von.Value; Neuaufbau(); };
        _bis.ValueChanged += (_, _) => { if (_bis.Value < _von.Value) _von.Value = _bis.Value; Neuaufbau(); };
        _ganzesJahr.CheckedChanged += (_, _) => { _von.Enabled = _bis.Enabled = !_ganzesJahr.Checked; Neuaufbau(); };
        _von.Enabled = _bis.Enabled = !_ganzesJahr.Checked;
        _klasse.SelectedIndexChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        foreach (var (n, k, b) in new[] { ("N", "Name", 150), ("D", "Datum", 108), ("S", "Stunden", 60), ("K", "Klassen", 110), ("L", "Aufsicht", 110), ("R", "Räume", 90) })
            _g.Columns.Add(n == "D" ? Steuerelemente.DatumSpalte.Neu(n, k, b) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b, SortMode = DataGridViewColumnSortMode.NotSortable });
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
        if (_auswahlNachLoeschen is not null) BearbeitungVerwerfen();
        if (_g.IsCurrentCellInEditMode) return;
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 0);
        if (_auswahlNachLoeschen is { } nach)
        {
            // nach dem Löschen: die nachfolgende Klausur (bzw. die letzte) auswählen, nicht die Leerzeile für neue Klausuren
            var anzahl = D.Klausuren.Count(ImFilter);
            pos = (anzahl == 0 ? 0 : Math.Min(nach.Zeile, anzahl - 1), nach.Spalte);
            _auswahlNachLoeschen = null;
        }
        var nachLoeschen = pos != (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 0);
        _laedt = true;
        // nur betroffene Zeilen ändern (an Ort und Stelle, neue an ihren Platz) – Zeile, Spalte und Bildlauf bleiben
        var eintraege = D.Klausuren.Where(ImFilter).OrderBy(k => k.Datum).ThenBy(k => k.StundeVon).Cast<object>().Append(NeueKlausurZeile).ToList();
        try { Steuerelemente.TabellenAbgleich.Anwenden(_g, eintraege, KlausurSchluessel, r => KlausurSchluessel(r.Tag ?? NeueKlausurZeile), KlausurZeileSetzen); }
        finally { _laedt = false; }
        if (nachLoeschen && pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[Math.Clamp(pos.Item2, 0, _g.Columns.Count - 1)];
    }

    /// <summary>Zeitbereich (bzw. ganzes Schuljahr) und gewählte Klasse.</summary>
    private bool ImFilter(Klausur k)
    {
        if (!_ganzesJahr.Checked && (k.Datum < DateOnly.FromDateTime(_von.Value) || k.Datum > DateOnly.FromDateTime(_bis.Value))) return false;
        return _klasse.SelectedIndex <= 0 || k.KlassenIds.Any(id => Ix.KKlassen(new[] { id }) == _klasse.SelectedItem as string);
    }

    /// <summary>Platzhalter der Leerzeile für neue Klausuren.</summary>
    private static readonly object NeueKlausurZeile = new();

    private static string KlausurSchluessel(object o) => o is Klausur k ? "K" + k.Id : "neu";

    private void KlausurZeileSetzen(DataGridViewRow zeile, object eintrag)
    {
        object?[] werte = eintrag is Klausur k
            ? new object?[] { k.Name, $"{Planungsdienst.TageKurz[((int)k.Datum.DayOfWeek + 6) % 7]} {Format.Datum(k.Datum)}", k.StundeVon == k.StundeBis ? k.StundeVon.ToString() : $"{k.StundeVon}-{k.StundeBis}",
                Ix.KKlassen(k.KlassenIds), string.Join(",", k.LehrerIds.Select(Ix.KLehrer)), string.Join(",", k.RaumIds.Select(Ix.KRaum)), k.UnterrichtEntfaellt, k.Aktiv, k.Text }
            : new object?[] { "", "", "", "", "", "", true, true, "" };
        zeile.Tag = eintrag as Klausur;
        for (var i = 0; i < werte.Length && i < zeile.Cells.Count; i++)
            if (!Equals(zeile.Cells[i].Value, werte[i])) zeile.Cells[i].Value = werte[i];
        var hinter = eintrag is Klausur ? Color.Empty : Color.FromArgb(252, 252, 236);
        if (zeile.DefaultCellStyle.BackColor != hinter) zeile.DefaultCellStyle.BackColor = hinter;
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var alt = zeile.Tag as Klausur;
        var k = alt is null ? new Klausur { Datum = AppZustand.Datum, Name = "Klausur" } : System.Text.Json.JsonSerializer.Deserialize<Klausur>(System.Text.Json.JsonSerializer.Serialize(alt))!;
        var roh = zeile.Cells[e.ColumnIndex].Value;
        var wert = roh is bool b ? (b ? "1" : "") : roh?.ToString()?.Trim() ?? "";
        // aus UntPlanWeb übernommene Prüfung: nur Lehrkraft und Raum änderbar, keine Verlegung
        if (alt?.Importiert == true && _g.Columns[e.ColumnIndex].Name is not ("L" or "R" or "A"))
        {
            Ui.Info("Bei aus UntPlanWeb importierten Klausuren können nur Lehrkraft und Raum geändert werden. Eine Verlegung ist nicht möglich.");
            BeginInvoke(Neuaufbau);
            return;
        }
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
        if (liste.Count == 0 && _g.CurrentRow?.Tag is Klausur aktuell) liste.Add(aktuell);
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Klausur(en) löschen?")) return;
        var ids = liste.Select(k => k.Id).ToHashSet();
        // Ein Klick auf eine Zelle startet die Bearbeitung – die offene Bearbeitung verwerfen, sonst bliebe der Neuaufbau aus und die
        // gelöschte Klausur stünde weiter in der Liste. Danach die Zeile an der Stelle der ersten gelöschten Klausur auswählen.
        var erste = _g.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Klausur k && ids.Contains(k.Id)).Select(r => r.Index).DefaultIfEmpty(0).Min();
        var spalte = _g.CurrentCell?.ColumnIndex ?? 0;
        BearbeitungVerwerfen();
        _auswahlNachLoeschen = (erste, spalte);
        if (!Aendern("Klausur löschen", AenderungsArt.Vertretung, d =>
        {
            // ohne Reste; zur Klausur gewordene Buchungen kehren zurück
            foreach (var k in d.Klausuren.Where(k => ids.Contains(k.Id)).ToList())
            {
                d.Klausuren.Remove(k);
                Core.Austausch.StundenplanAenderungen.KlausurZeilenEntfernen(d, k);
            }
            foreach (var t in liste.Select(k => k.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        }))
        {
            _auswahlNachLoeschen = null;
            return;
        }
        // sofort neu aufbauen (nicht erst nach der verzögerten Benachrichtigung)
        Neuaufbau();
    }

    /// <summary>Zeile/Spalte, die nach dem Löschen ausgewählt wird (Index der ersten gelöschten Zeile, bzw. die letzte Klausur).</summary>
    private (int Zeile, int Spalte)? _auswahlNachLoeschen;

    private void BearbeitungVerwerfen()
    {
        if (!_g.IsCurrentCellInEditMode) return;
        _laedt = true;
        try { _g.CancelEdit(); _g.EndEdit(); }
        catch (InvalidOperationException) { }
        finally { _laedt = false; }
    }

    /// <summary>Selbsttest: Klausur auswählen (mit offener Zellbearbeitung wie nach einem Klick) und ohne Rückfrage löschen.</summary>
    internal int SelbsttestLoeschen(int klausurId)
    {
        var zeile = _g.Rows.Cast<DataGridViewRow>().First(r => r.Tag is Klausur k && k.Id == klausurId);
        _g.CurrentCell = zeile.Cells[0];
        _g.BeginEdit(true);
        var frage = Ui.FrageAntwort;
        Ui.FrageAntwort = (_, _) => true;
        try { Loeschen(); }
        finally { Ui.FrageAntwort = frage; }
        return _g.Rows.Cast<DataGridViewRow>().Count(r => r.Tag is Klausur k && k.Id == klausurId);
    }

    /// <summary>Index der aktuellen Zeile (Selbsttest).</summary>
    internal int SelbsttestAktuelleZeile => _g.CurrentCell?.RowIndex ?? -1;
}

/// <summary>
/// Vormerkungen wie im Handbuch: „Nur nicht verplante Vormerkungen anzeigen“; Spalten Vm-Nr., Nvpl Std. (zum Verplanen in den
/// tagesaktuellen Stundenplan bzw. Planungsdialog ziehen), Stunden, Fach, Lehrer, Klasse(n), Raum, Statistik, Text, Vtr-Nr., U-Nr, Art,
/// Studenten. Konkrete Vormerkungen werden direkt in der Tabelle (leere Zeile unten) eingegeben; Unterrichte aus dem Unterrichtsfenster
/// und Stunden aus Stundenplan bzw. Planungsdialog können hierher gezogen werden. Mit dem kleinen Plus vor der Vm-Nr. öffnen sich die
/// Informationszeilen der verplanten Stunden („16.10. / 6 Sondereins. zu Vormerkung“, Handbuch Kap. 12.2.1); Lehrer und Klassen einer
/// verplanten Vormerkung lassen sich ändern, wenn dadurch keine Kollision entsteht (Kap. 12.1.6).
/// </summary>
public sealed class VormerkungenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly CheckBox _nurOffen = new() { Text = "Nur nicht verplante Vormerkungen anzeigen", AutoSize = true, Left = 8, Top = 6 };
    private bool _laedt;
    /// <summary>Vormerkungen mit aufgeklappten Informationszeilen (verplante Stunden).</summary>
    private readonly HashSet<int> _aufgeklappt = new();

    /// <summary>Informationszeile unter einer Vormerkung: eine verplante Stunde.</summary>
    private sealed record InfoZeile(Vormerkung Vormerkung, string Text);

    public VormerkungenFenster()
    {
        Text = "Vormerkungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("vormerkung");
        Size = new Size(900, 300);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Vormerkung", "neu", Neu, false));
        ts.Items.Add(Knopf("Am Planungsdatum verplanen…", "planen", Verplanen, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        _nurOffen.CheckedChanged += (_, _) => Neuaufbau();
        var oben = new Panel { Dock = DockStyle.Top, Height = 28 };
        oben.Controls.Add(_nurOffen);
        Ui.RasterGestalten(_g, true);
        _g.AllowUserToAddRows = true;
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        foreach (var (n, k, b) in new[]
        {
            ("Nr", "Vm-Nr.", 55), ("O", "Nvpl Std.", 62), ("S", "Stunden", 55), ("F", "Fach", 55), ("L", "Lehrer", 75), ("K", "Klasse(n)", 85), ("R", "Raum", 55),
            ("Stat", "Statistik", 60), ("T", "Text", 160), ("VNr", "Vtr-Nr.", 55), ("U", "U-Nr", 50), ("A", "Art", 110), ("Stud", "Studenten", 70),
            ("Urspr", "ursprünglich", 110), ("G", "gültig", 140), ("V", "verplant am", 120),
        })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        foreach (var n in new[] { "Nr", "O", "Stat", "VNr", "U", "A", "Stud", "Urspr", "G", "V" }) _g.Columns[n]!.ReadOnly = true;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.Columns["Nr"]!.DefaultCellStyle.Padding = new Padding(14, 0, 0, 0);
        _g.Columns["Nr"]!.HeaderCell.Style.Padding = new Padding(14, 0, 0, 0);
        _g.CellEndEdit += ZelleGeaendert;
        // „+“ vor der Vm-Nr.: Informationszeilen der verplanten Stunden auf-/zuklappen; „−“ im Spaltenkopf klappt alle zu bzw. auf
        _g.CellMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0 || _g.Columns[e.ColumnIndex].Name != "Nr" || e.X > 14) return;
            if (e.RowIndex == -1)
            {
                var alle = D.Vormerkungen.Where(v => Verplante(v).Count > 0).Select(v => v.Id).ToList();
                if (alle.All(_aufgeklappt.Contains)) _aufgeklappt.Clear(); else _aufgeklappt.UnionWith(alle);
                Neuaufbau();
            }
            else if (_g.Rows[e.RowIndex].Tag is Vormerkung vk && Verplante(vk).Count > 0)
            {
                if (!_aufgeklappt.Remove(vk.Id)) _aufgeklappt.Add(vk.Id);
                Neuaufbau();
            }
        };
        _g.CellPainting += InfoZeichnen;
        // Kopf „Vm-Nr.“ klappt auf/zu statt zu sortieren; nach dem Sortieren einer anderen Spalte werden die Informationszeilen zugeklappt
        _g.Columns["Nr"]!.SortMode = DataGridViewColumnSortMode.NotSortable;
        _g.Sorted += (_, _) =>
        {
            if (_aufgeklappt.Count == 0) return;
            _aufgeklappt.Clear();
            for (var i = _g.Rows.Count - 1; i >= 0; i--)
                if (_g.Rows[i].Tag is InfoZeile) _g.Rows.RemoveAt(i);
        };
        _g.RowPostPaint += (_, e) =>
        {
            // Informationszeile: Text über die Spalten ab „Nvpl Std.“ (wie im Handbuch)
            if (_g.Rows[e.RowIndex].Tag is not InfoZeile iz) return;
            var o = _g.GetCellDisplayRectangle(_g.Columns["O"]!.Index, e.RowIndex, false);
            if (o.Width == 0) return;
            var r = new Rectangle(o.X + 3, e.RowBounds.Y, Math.Max(40, e.RowBounds.Right - o.X - 3), e.RowBounds.Height);
            TextRenderer.DrawText(e.Graphics, iz.Text, _g.Font, r, Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        };
        _g.SelectionChanged += (_, _) =>
        {
            // wie im Handbuch: offene Stundenpläne zeigen Lehrer bzw. Klasse der gewählten Vormerkung
            if (_laedt || _g.CurrentRow?.Tag is not Vormerkung vm) return;
            if (vm.LehrerIds.Count > 0) AppZustand.Waehlen(ElementTyp.Lehrer, vm.LehrerIds[0], this);
            if (vm.KlassenIds.Count > 0) AppZustand.Waehlen(ElementTyp.Klasse, vm.KlassenIds[0], this);
        };
        // Ziehen aus „Nvpl Std.“ (bzw. der ganzen Zeile) in den tagesaktuellen Stundenplan oder Planungsdialog verplant die Vormerkung
        Point start = Point.Empty;
        _g.MouseDown += (_, e) => start = e.Location;
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 6) return;
            var hit = _g.HitTest(start.X, start.Y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0 && _g.Columns[hit.ColumnIndex].ReadOnly && _g.Rows[hit.RowIndex].Tag is Vormerkung vm && vm.Offen > 0)
                _g.DoDragDrop("vormerkung:" + vm.Id, DragDropEffects.Move);
        };
        _g.CellPainting += (_, e) =>
        {
            // Symbol „ziehen“ vor der Anzahl der nicht verplanten Stunden
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _g.Columns[e.ColumnIndex].Name != "O" || _g.Rows[e.RowIndex].Tag is not Vormerkung { Offen: > 0 } || e.Graphics is null) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            e.Graphics.DrawImage(Symbole.Get("vormerkung", 16), e.CellBounds.Left + 2, e.CellBounds.Top + (e.CellBounds.Height - 16) / 2, 16, 16);
            e.Handled = true;
        };
        _g.Columns["O"]!.DefaultCellStyle.Padding = new Padding(20, 0, 0, 0);
        // Unterricht (Unterrichtsfenster) bzw. Stunde (Stundenplan, Planungsdialog) hierher ziehen = Vormerkung anlegen
        _g.DragOver += (_, e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string s && (s.StartsWith("unterricht:") || s.StartsWith("tag:")) ? DragDropEffects.Move : DragDropEffects.None;
        _g.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.Text) is string s) Fallen(s); };
        Controls.Add(_g);
        Controls.Add(oben);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private static string ArtText(Vormerkung v, Schuldatei d) => v.Quelle switch
    {
        VormerkungsQuelle.Unterricht => $"Unterricht / {d.Unterrichte.FirstOrDefault(u => u.Id == v.UnterrichtId)?.Wochenstunden ?? 0}",
        VormerkungsQuelle.Vertretung => "Vertretung",
        VormerkungsQuelle.Verdraengung => "Verdrängung",
        VormerkungsQuelle.Stundenplan => "Verlegung",
        _ => "Sondereinsatz",
    };

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        _laedt = true;
        try
        {
            _g.Rows.Clear();
            foreach (var v in D.Vormerkungen.Where(v => !_nurOffen.Checked || v.Offen > 0).OrderBy(v => v.Id))
            {
                var u = v.UnterrichtId is int uid ? D.Unterrichte.FirstOrDefault(x => x.Id == uid) : null;
                var vtr = D.Vertretungen.FirstOrDefault(x => x.VormerkungId == v.Id && x.Art == VertretungsArt.Vormerkung);
                var r = _g.Rows.Add(v.Id, v.Offen, v.Stunden, Ix.KFach(v.FachId), string.Join(",", v.LehrerIds.Select(Ix.KLehrer)), Ix.KKlassen(v.KlassenIds), Ix.KRaum(v.RaumId),
                    u?.Statistik ?? "", v.Text, vtr?.Id.ToString() ?? "", v.UnterrichtId?.ToString() ?? "", ArtText(v, D), "",
                    v.Datum is DateOnly dd ? $"{Format.Datum(dd)} / {v.Stunde}" : "", v.GueltigVon.HasValue || v.GueltigBis.HasValue ? $"{(v.GueltigVon is DateOnly a ? Format.Datum(a) : "")} – {(v.GueltigBis is DateOnly b ? Format.Datum(b) : "")}" : "",
                    string.Join(", ", v.VerplantAm.Select(Format.Datum)));
                _g.Rows[r].Tag = v;
                if (v.Offen == 0) _g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
                // aus Vertretung bzw. Unterricht entstandene Vormerkungen sind nicht veränderbar
                if (v.Quelle is VormerkungsQuelle.Vertretung or VormerkungsQuelle.Unterricht) _g.Rows[r].ReadOnly = true;
                InfoZeilenAnhaengen(v);
            }
        }
        finally { _laedt = false; }
    }

    /// <summary>Verplante Stunden einer Vormerkung (Datum, Stunde, Art) – für die Informationszeilen.</summary>
    private List<string> Verplante(Vormerkung v)
    {
        var zeilen = D.Vertretungen.Where(x => x.VormerkungId == v.Id && x.Art != VertretungsArt.Vormerkung)
            .GroupBy(x => (x.Datum, x.Stunde)).OrderBy(g => g.Key.Datum).ThenBy(g => g.Key.Stunde)
            .Select(g => $"{g.Key.Datum:d.M.} / {g.Key.Stunde} {Vertretungsplan.ArtText(D, g.First().Art)}").ToList();
        // verplant, aber ohne eigene Zeile in der Vertretungsliste (z. B. eine vollständig durchgeführte Verlegung)
        foreach (var t in v.VerplantAm.Where(t => !D.Vertretungen.Any(x => x.VormerkungId == v.Id && x.Datum == t && x.Art != VertretungsArt.Vormerkung)))
            zeilen.Add($"{t:d.M.} verplant");
        return zeilen;
    }

    /// <summary>Informationszeilen einer aufgeklappten Vormerkung hinter ihrer Zeile einfügen.</summary>
    private void InfoZeilenAnhaengen(Vormerkung v)
    {
        if (!_aufgeklappt.Contains(v.Id)) return;
        foreach (var text in Verplante(v))
        {
            var r = _g.Rows.Add();
            _g.Rows[r].Tag = new InfoZeile(v, text);
            _g.Rows[r].ReadOnly = true;
            _g.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(250, 250, 240);
        }
    }

    /// <summary>„+“ bzw. „−“ vor der Vm-Nr. (Zeile und Spaltenkopf), Baumlinie vor den Informationszeilen.</summary>
    private void InfoZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex < 0 || _g.Columns[e.ColumnIndex].Name != "Nr" || e.Graphics is null) return;
        var kasten = new Rectangle(e.CellBounds.X + 3, e.CellBounds.Y + (e.CellBounds.Height - 9) / 2, 9, 9);
        void Kasten(bool offen)
        {
            e.Graphics.FillRectangle(Brushes.White, kasten);
            using var p = new Pen(Color.FromArgb(120, 120, 120));
            e.Graphics.DrawRectangle(p, kasten);
            var m = kasten.Y + kasten.Height / 2;
            e.Graphics.DrawLine(Pens.Black, kasten.X + 2, m, kasten.Right - 2, m);
            if (!offen) e.Graphics.DrawLine(Pens.Black, kasten.X + kasten.Width / 2, kasten.Y + 2, kasten.X + kasten.Width / 2, kasten.Bottom - 2);
        }
        if (e.RowIndex == -1)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            var alle = D.Vormerkungen.Where(v => Verplante(v).Count > 0).Select(v => v.Id).ToList();
            Kasten(alle.Count > 0 && alle.All(_aufgeklappt.Contains));
            e.Handled = true;
            return;
        }
        if (e.RowIndex < 0) return;
        var tag = _g.Rows[e.RowIndex].Tag;
        if (tag is Vormerkung v && Verplante(v).Count > 0)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            Kasten(_aufgeklappt.Contains(v.Id));
            e.Handled = true;
        }
        else if (tag is InfoZeile)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            using var p = new Pen(Color.Gray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            var x = kasten.X + 4;
            var y = e.CellBounds.Y + e.CellBounds.Height / 2;
            e.Graphics.DrawLine(p, x, e.CellBounds.Y, x, y);
            e.Graphics.DrawLine(p, x, y, x + 10, y);
            e.Handled = true;
        }
    }

    /// <summary>Direkte Eingabe bzw. Änderung (Stunden, Fach, Lehrer, Klasse(n), Raum, Text).</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var spalte = _g.Columns[e.ColumnIndex].Name;
        var wert = zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var alt = zeile.Tag as Vormerkung;
        if (alt is null && wert.Length == 0) return;
        var vm = alt is null ? new Vormerkung { Quelle = VormerkungsQuelle.Konkret } : System.Text.Json.JsonSerializer.Deserialize<Vormerkung>(System.Text.Json.JsonSerializer.Serialize(alt))!;
        switch (spalte)
        {
            case "S":
                if (!int.TryParse(wert, out var n) || n < 1) { Ui.Info($"Die Eingabe \"{wert}\" ist ungültig."); BeginInvoke(Neuaufbau); return; }
                if (n < vm.VerplantAm.Count) { Ui.Info($"Die Stundenzahl kann nur bis zur Anzahl der bereits verplanten Stunden ({vm.VerplantAm.Count}) vermindert werden."); BeginInvoke(Neuaufbau); return; }
                vm.Stunden = n;
                break;
            case "F": vm.FachId = D.Faecher.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
            case "R": vm.RaumId = D.Raeume.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
            case "L" or "K":
                var lehrer = spalte == "L" ? VeranstaltungenFenster.Kurznamen(ElementTyp.Lehrer, wert) : vm.LehrerIds;
                var klassen = spalte == "K" ? VeranstaltungenFenster.Kurznamen(ElementTyp.Klasse, wert) : vm.KlassenIds;
                // Handbuch Kap. 12.1.6: bereits verplante Vormerkung nur ändern, wenn keine Kollision entsteht – die verplanten Stunden ändern sich mit
                if (alt != null && D.Vertretungen.Any(x => x.VormerkungId == alt.Id && x.Art != VertretungsArt.Vormerkung))
                {
                    if (Vertretungen.VormerkungAenderungPruefen(D, alt, lehrer, klassen) is { } grund)
                    {
                        Ui.Info(grund + Environment.NewLine + Environment.NewLine + "Die Änderung wurde nicht durchgeführt.");
                        BeginInvoke(Neuaufbau);
                        return;
                    }
                    var id = alt.Id;
                    Aendern("Vormerkung geändert", AenderungsArt.Vertretung, d =>
                    {
                        var x = d.Vormerkungen.First(m => m.Id == id);
                        if (Vertretungen.VormerkungAendern(d, x, lehrer, klassen, AppZustand.Benutzer) is { } fehler) throw new InvalidOperationException(fehler);
                    });
                    return;
                }
                vm.LehrerIds = lehrer.ToList();
                vm.KlassenIds = klassen.ToList();
                break;
            case "T": vm.Text = wert; break;
            default: return;
        }
        Aendern(alt is null ? "Vormerkung" : "Vormerkung geändert", AenderungsArt.Vertretung, d =>
        {
            if (alt != null) d.Vormerkungen[d.Vormerkungen.FindIndex(x => x.Id == alt.Id)] = vm;
            else { vm.Id = d.NeueId(); d.Vormerkungen.Add(vm); }
        });
    }

    /// <summary>Vormerkung aus Unterricht (befristet: Wochenstunden × Wochen) bzw. aus Stundenplan / Planungsdialog.</summary>
    private void Fallen(string s)
    {
        if (s.StartsWith("unterricht:") && int.TryParse(s[11..], out var uid) && D.Unterrichte.FirstOrDefault(x => x.Id == uid) is { } u)
        {
            var stunden = Math.Max(1, u.Wochenstunden);
            if (u.VonDatum is DateOnly von && u.BisDatum is DateOnly bis && bis >= von)
                stunden = Math.Max(1, u.Wochenstunden) * Math.Max(1, (bis.DayNumber - von.DayNumber + 7) / 7);
            var z = u.Zeilen.FirstOrDefault();
            var vm = new Vormerkung
            {
                Quelle = VormerkungsQuelle.Unterricht, UnterrichtId = u.Id, Stunden = stunden, FachId = u.HauptfachId, LehrerIds = u.LehrerIds.ToList(), KlassenIds = u.KlassenIds.ToList(),
                RaumId = z?.FachraumId ?? z?.StammraumId, GueltigVon = u.VonDatum, GueltigBis = u.BisDatum, Text = u.Text,
            };
            Aendern("Vormerkung aus Unterricht", AenderungsArt.Vertretung, d => { vm.Id = d.NeueId(); d.Vormerkungen.Add(vm); });
            return;
        }
        if (s.StartsWith("tag:"))
        {
            var t = s.Split(':');
            if (t.Length < 5 || !DateOnly.TryParseExact(t[1], "yyyyMMdd", out var datum) || !int.TryParse(t[2], out var st) || !int.TryParse(t[3], out var unt)) return;
            int? zeile = int.TryParse(t[4], out var zz) && zz >= 0 ? zz : null;
            var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
            Aendern(laenge > 1 ? $"Vormerkung ({laenge} Stunden)" : "Vormerkung", AenderungsArt.Vertretung, d =>
            {
                for (var i = 0; i < laenge; i++) Tagesverlegung.Vormerken(d, datum, st + i, unt, zeile, VormerkungsQuelle.Stundenplan, AppZustand.Benutzer);
            });
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

    /// <summary>
    /// Löschen wie im Handbuch: sind bereits Stunden verplant, werden diese nach der Rückfrage „Verplante Stunden löschen“ wieder entplant;
    /// sind keine Stunden (mehr) verplant, wird die ganze Vormerkung gelöscht.
    /// </summary>
    private void Loeschen()
    {
        var liste = _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Vormerkung>().Distinct().ToList();
        if (liste.Count == 0 && _g.CurrentRow?.Tag is Vormerkung aktuell) liste.Add(aktuell);
        if (liste.Count == 0) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        var verplant = D.Vertretungen.Where(v => v.VormerkungId is int id && ids.Contains(id) && v.Art != VertretungsArt.Vormerkung).ToList();
        if (verplant.Count > 0)
        {
            if (!Ui.Frage($"Verplante Stunden löschen? ({verplant.Count} Stunde(n))")) return;
            Aendern("Verplante Stunden löschen", AenderungsArt.Vertretung, d =>
            {
                var sonder = verplant.Where(v => v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung).Select(v => v.Id).ToList();
                Verlegungsaufhebung.SondereinsaetzeLoeschen(d, sonder);
                foreach (var v in verplant.Where(v => !sonder.Contains(v.Id)))
                    if (d.Vertretungen.FirstOrDefault(x => x.Id == v.Id) is { } w) Verlegungsaufhebung.Aufheben(d, w);
            });
            return;
        }
        if (!Ui.Frage($"{liste.Count} Vormerkung(en) löschen?")) return;
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
