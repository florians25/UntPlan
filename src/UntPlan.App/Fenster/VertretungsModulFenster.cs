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
    internal void SelbsttestKalendertag() => _tabs.SelectedIndex = 1;
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
            var anzahl = D.Klausuren.Count;
            pos = (anzahl == 0 ? 0 : Math.Min(nach.Zeile, anzahl - 1), nach.Spalte);
            _auswahlNachLoeschen = null;
        }
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
