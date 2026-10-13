using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;

namespace UntPlan.App.Fenster;

/// <summary>
/// Modul Unterrichtsplanung und Wertrechnung: Lehrer-Werte (Soll/Ist), Unterrichts-Matrix (Klassen × Fächer)
/// und Stundentafel mit Abgleich gegen den eingegebenen Unterricht.
/// </summary>
public sealed class WertrechnungFenster : MdiFenster, IDruckbar
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _werte = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _matrix = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _tafel = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _abgleich = new() { Dock = DockStyle.Fill };
    private readonly Label _summe = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 5, 0, 0), BackColor = Ui.Kopf };
    private readonly ToolStripComboBox _matrixArt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };

    public WertrechnungFenster(int tab = 0)
    {
        Text = "Unterrichtsplanung / Wertrechnung";
        HilfeThema = "Unterrichtsplanung";
        Icon = SymbolIcon("wertrechnung");
        Size = new Size(1060, 560);
        Positionieren();

        Ui.RasterGestalten(_werte, true);
        _werte.AllowUserToAddRows = false;
        foreach (var (n, k, b, ro) in new[]
        {
            ("L", "Lehrer", 65, true), ("N", "Name", 150, true), ("WS", "Wochensoll", 75, false), ("Er", "Ermäßigung", 75, true), ("So", "Soll", 60, true),
            ("Ist", "Ist", 60, true), ("Kor", "Wertkorrektur", 85, false), ("Diff", "Differenz", 70, true), ("Wst", "Wst.", 50, true), ("Vpl", "verplant", 60, true), ("F", "Fächer", 200, true),
        })
        {
            var c = _werte.Columns[_werte.Columns.Add(n, k)];
            c.Width = b;
            c.ReadOnly = ro;
            if (!ro) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 238);
        }
        _werte.Columns["F"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _werte.CellEndEdit += WertGeaendert;
        _werte.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _werte.Rows[e.RowIndex].Tag is Lehrer l) StundenplanFenster.Neu(ElementTyp.Lehrer, l.Id, false); };
        var seite1 = new TabPage("Lehrer: Soll / Ist");
        seite1.Controls.Add(_werte);
        seite1.Controls.Add(_summe);

        Ui.RasterGestalten(_matrix, false);
        _matrix.AllowUserToAddRows = false;
        _matrix.CellDoubleClick += MatrixDoppelklick;
        var mLeiste = Werkzeugleiste();
        _matrixArt.Items.AddRange(new object[] { "Lehrer", "Stunden", "Lehrer + Stunden" });
        _matrixArt.SelectedIndex = 2;
        _matrixArt.SelectedIndexChanged += (_, _) => MatrixAufbauen();
        mLeiste.Items.Add(new ToolStripLabel("Anzeige:"));
        mLeiste.Items.Add(_matrixArt);
        mLeiste.Items.Add(new ToolStripLabel("   Doppelklick öffnet den Unterricht der Klasse.") { ForeColor = Color.DimGray });
        var seite2 = new TabPage("Unterrichts-Matrix");
        seite2.Controls.Add(_matrix);
        seite2.Controls.Add(mLeiste);

        Ui.RasterGestalten(_tafel, true);
        _tafel.AllowUserToAddRows = false;
        _tafel.CellEndEdit += TafelGeaendert;
        Ui.RasterGestalten(_abgleich, false);
        foreach (var (n, k, b) in new[] { ("K", "Klasse", 70), ("F", "Fach", 70), ("S", "Soll", 55), ("I", "Ist", 55), ("D", "Differenz", 70) })
            _abgleich.Columns[_abgleich.Columns.Add(n, k)].Width = b;
        var tLeiste = Werkzeugleiste();
        tLeiste.Items.Add(Knopf("Jahrgang hinzufügen", "plus", JahrgangHinzu, false));
        tLeiste.Items.Add(Knopf("NRW-Jahrgänge eintragen", "neu", () => { foreach (var j in D.Klassen.Select(k => k.Jahrgang).Where(j => j.Length > 0).Distinct()) JahrgangSicherstellen(j); TafelAufbauen(); }, false));
        tLeiste.Items.Add(new ToolStripLabel("   Wochenstunden je Jahrgang und Fach. Rechts: Abweichungen zum eingegebenen Unterricht.") { ForeColor = Color.DimGray });
        var tsplit = new SplitContainer { Dock = DockStyle.Fill };
        tsplit.Panel1.Controls.Add(_tafel);
        tsplit.Panel2.Controls.Add(_abgleich);
        tsplit.Panel2.Controls.Add(new Label { Text = "Abgleich Stundentafel ↔ Unterricht", Dock = DockStyle.Top, Height = 20, Font = Ui.SchriftFett });
        var seite3 = new TabPage("Stundentafel");
        seite3.Controls.Add(tsplit);
        seite3.Controls.Add(tLeiste);
        seite3.Enter += (_, _) => { if (tsplit.Width > 400) tsplit.SplitterDistance = tsplit.Width - 360; };

        _tabs.TabPages.AddRange(new[] { seite1, seite2, seite3 });
        _tabs.SelectedIndex = Math.Clamp(tab, 0, 2);
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Unterricht", "unterricht", () => HauptFenster.Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)), false));
        Controls.Add(_tabs);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        WerteAufbauen();
        MatrixAufbauen();
        TafelAufbauen();
    }

    private void WerteAufbauen()
    {
        _werte.Rows.Clear();
        var liste = Wertrechnung.Uebersicht(D);
        foreach (var w in liste.OrderBy(w => w.Lehrer.Kurzname, StringComparer.Create(Format.De, true)))
        {
            var r = _werte.Rows.Add(w.Lehrer.Kurzname, w.Lehrer.VollerName, Format.Zahl(w.Wochensoll), Format.Zahl(w.Ermaessigung), Format.Zahl(w.Soll), Format.Zahl(w.Ist),
                Format.Zahl(w.Lehrer.Wertkorrektur), w.Differenz.ToString("+0.##;-0.##;0", Format.De), w.Wochenstunden, w.Verplant,
                string.Join(", ", w.Lehrer.FaecherIds.Select(f => Ix.KFach(f))));
            _werte.Rows[r].Tag = w.Lehrer;
            _werte.Rows[r].Cells["Diff"].Style.BackColor = w.Differenz switch
            {
                > 0.5m => Color.FromArgb(255, 220, 200),
                < -0.5m => Color.FromArgb(220, 230, 255),
                _ => Color.FromArgb(225, 245, 225),
            };
        }
        _summe.Text = $"Summe Soll: {Format.Zahl(liste.Sum(w => w.Soll))}   Ist: {Format.Zahl(liste.Sum(w => w.Ist))}   Differenz: {Format.Zahl(liste.Sum(w => w.Differenz))}   " +
                      $"(Ist = Wochenstunden × Werte-Faktor, anteilig bei A/B-Wochen und befristetem Unterricht)";
    }

    private void WertGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _werte.Rows[e.RowIndex].Tag is not Lehrer l) return;
        var spalte = _werte.Columns[e.ColumnIndex].Name;
        if (!decimal.TryParse(_werte.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString(), System.Globalization.NumberStyles.Number, Format.De, out var wert))
        {
            BeginInvoke(WerteAufbauen);
            return;
        }
        var id = l.Id;
        Aendern(spalte == "WS" ? "Wochensoll" : "Wertkorrektur", AenderungsArt.Stammdaten, d =>
        {
            var x = d.Lehrer.First(y => y.Id == id);
            if (spalte == "WS") x.Wochensoll = wert;
            else x.Wertkorrektur = wert;
        });
        BeginInvoke(WerteAufbauen);
    }

    private void MatrixAufbauen()
    {
        _matrix.Columns.Clear();
        var faecher = Stammdaten.Sortiert(D.Faecher).Where(f => D.Unterrichte.Any(u => u.FachIds.Contains(f.Id))).ToList();
        _matrix.Columns.Add("K", "Klasse");
        _matrix.Columns[0].Frozen = true;
        _matrix.Columns[0].Width = 60;
        foreach (var f in faecher) _matrix.Columns[_matrix.Columns.Add("F" + f.Id, f.Kurzname)].Width = _matrixArt.SelectedIndex == 1 ? 40 : 70;
        _matrix.Columns.Add("S", "Summe");
        var m = Wertrechnung.Matrix(D);
        foreach (var k in Stammdaten.Sortiert(D.Klassen))
        {
            var werte = new object[faecher.Count + 2];
            werte[0] = k.Kurzname;
            var summe = 0;
            for (var i = 0; i < faecher.Count; i++)
            {
                if (!m.TryGetValue((k.Id, faecher[i].Id), out var l)) { werte[i + 1] = ""; continue; }
                var std = l.Select(x => x.U).Distinct().Sum(u => u.Wochenstunden);
                summe += l.Select(x => x.U).Distinct().Where(u => u.HauptfachId == faecher[i].Id).Sum(u => u.Wochenstunden);
                var lehrer = string.Join(",", l.Select(x => Ix.KLehrer(x.Lehrer)).Where(s => s.Length > 0).Distinct());
                werte[i + 1] = _matrixArt.SelectedIndex switch { 0 => lehrer, 1 => std.ToString(), _ => $"{std} {lehrer}" };
            }
            werte[^1] = summe;
            var r = _matrix.Rows.Add(werte);
            _matrix.Rows[r].Tag = k;
        }
    }

    private void MatrixDoppelklick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _matrix.Rows[e.RowIndex].Tag is not Klasse k) return;
        var f = HauptFenster.Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse));
        AppZustand.Waehlen(ElementTyp.Klasse, k.Id, this);
        f.Activate();
    }

    private void TafelAufbauen()
    {
        _tafel.Columns.Clear();
        var faecher = Stammdaten.Sortiert(D.Faecher).Where(f => !f.Bereitschaft).ToList();
        _tafel.Columns.Add("J", "Jahrgang");
        _tafel.Columns[0].ReadOnly = true;
        _tafel.Columns[0].Frozen = true;
        foreach (var f in faecher) _tafel.Columns[_tafel.Columns.Add("F" + f.Id, f.Kurzname)].Width = 42;
        _tafel.Columns.Add("S", "Summe");
        _tafel.Columns["S"]!.ReadOnly = true;
        foreach (var j in D.Stundentafel.Select(z => z.Jahrgang).Distinct().OrderBy(Nrw.JahrgangRang))
        {
            var werte = new object[faecher.Count + 2];
            werte[0] = j;
            for (var i = 0; i < faecher.Count; i++)
            {
                var z = D.Stundentafel.FirstOrDefault(x => x.Jahrgang == j && x.FachId == faecher[i].Id);
                werte[i + 1] = z is null || z.Stunden == 0 ? "" : Format.Zahl(z.Stunden);
            }
            werte[^1] = Format.Zahl(D.Stundentafel.Where(x => x.Jahrgang == j).Sum(x => x.Stunden));
            var r = _tafel.Rows.Add(werte);
            _tafel.Rows[r].Tag = j;
        }
        _abgleich.Rows.Clear();
        foreach (var a in Wertrechnung.Stundentafel(D))
        {
            var r = _abgleich.Rows.Add(a.Klasse.Kurzname, a.Fach.Kurzname, Format.Zahl(a.Soll), Format.Zahl(a.Ist), (a.Ist - a.Soll).ToString("+0.##;-0.##;0", Format.De));
            _abgleich.Rows[r].DefaultCellStyle.ForeColor = a.Ist < a.Soll ? Color.DarkRed : Color.DarkBlue;
        }
    }

    private void TafelGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _tafel.Rows[e.RowIndex].Tag is not string jahrgang) return;
        var name = _tafel.Columns[e.ColumnIndex].Name;
        if (!name.StartsWith('F') || !int.TryParse(name[1..], out var fachId)) return;
        var text = _tafel.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
        decimal std = 0;
        if (text.Length > 0 && !decimal.TryParse(text, System.Globalization.NumberStyles.Number, Format.De, out std)) { BeginInvoke(TafelAufbauen); return; }
        Aendern("Stundentafel", AenderungsArt.Einstellungen, d =>
        {
            d.Stundentafel.RemoveAll(z => z.Jahrgang == jahrgang && z.FachId == fachId);
            if (std > 0) d.Stundentafel.Add(new StundentafelZeile { Jahrgang = jahrgang, FachId = fachId, Stunden = std });
        });
        BeginInvoke(TafelAufbauen);
    }

    private void JahrgangHinzu()
    {
        var j = Ui.Eingabe("Jahrgang (z. B. 5, 6, EF, Q1):", "Stundentafel");
        if (string.IsNullOrWhiteSpace(j)) return;
        JahrgangSicherstellen(j.Trim());
        TafelAufbauen();
    }

    /// <summary>Legt einen Jahrgang an und übernimmt als Vorschlag die Stunden aus dem vorhandenen Unterricht.</summary>
    private void JahrgangSicherstellen(string jahrgang)
    {
        if (D.Stundentafel.Any(z => z.Jahrgang == jahrgang)) return;
        var klasse = D.Klassen.FirstOrDefault(k => k.Jahrgang == jahrgang);
        Aendern($"Stundentafel Jahrgang {jahrgang}", AenderungsArt.Einstellungen, d =>
        {
            var ix = new DatenIndex(d);
            var vorschlag = klasse is null ? new Dictionary<int, int>() : ix.UnterrichteVon(ElementTyp.Klasse, klasse.Id)
                .Where(u => u.HauptfachId.HasValue && !(ix.Faecher.TryGetValue(u.HauptfachId.Value, out var f) && f.Bereitschaft))
                .GroupBy(u => u.HauptfachId!.Value).ToDictionary(g => g.Key, g => g.Max(u => u.Wochenstunden));
            if (vorschlag.Count == 0 && d.Faecher.FirstOrDefault(f => !f.Bereitschaft) is { } erstes) vorschlag[erstes.Id] = 0;
            foreach (var (fach, std) in vorschlag) d.Stundentafel.Add(new StundentafelZeile { Jahrgang = jahrgang, FachId = fach, Stunden = std });
        });
    }

    public void Drucken(bool vorschau)
    {
        var g = _tabs.SelectedIndex switch { 0 => _werte, 1 => _matrix, _ => _tafel };
        var kopf = g.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray();
        var zeilen = g.Rows.Cast<DataGridViewRow>().Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "").ToArray()).ToList();
        ListenDruck.Drucken(this, _tabs.SelectedTab!.Text, kopf, g.Columns.Cast<DataGridViewColumn>().Select(c => c.Width).ToArray(), zeilen, vorschau);
    }
}

/// <summary>
/// Modul Kursplanung (Oberstufe): Kurse, Kurswahlen der Schüler, Kollisionsmatrix und automatische Bandbildung (Kursschienen).
/// </summary>
public sealed class KursplanungFenster : MdiFenster
{
    private readonly ToolStripComboBox _klasse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _kurse = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _wahlen = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _matrix = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _baender = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _maxBaender = new() { Minimum = 1, Maximum = 30, Value = 10, Width = 50 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 4, 0, 0), BackColor = Ui.Kopf };
    private Dictionary<int, int>? _vorschlag;

    public KursplanungFenster()
    {
        Text = "Kursplanung";
        HilfeThema = "Kursplanung";
        Icon = SymbolIcon("kurs");
        Size = new Size(1080, 580);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(new ToolStripLabel("Klasse/Jahrgang:"));
        ts.Items.Add(_klasse);
        _klasse.SelectedIndexChanged += (_, _) => Neuaufbau();
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Schüler", "schueler", () => HauptFenster.Oeffnen<StammdatenFenster>("st:Schueler", () => new StammdatenFenster(ElementTyp.Schueler)), false));
        ts.Items.Add(Knopf("Kurswahlen zufällig füllen (Test)", "neu", ZufallsWahlen, false));
        Ui.RasterGestalten(_kurse, true);
        _kurse.AllowUserToAddRows = false;
        foreach (var (n, k, b, ro) in new[] { ("U", "U-Nr", 50, true), ("K", "Kursname", 90, false), ("F", "Fach", 55, true), ("L", "Lehrer", 70, true), ("Kl", "Klasse(n)", 80, true), ("W", "Wst.", 45, true), ("B", "Band", 70, false), ("T", "Teilnehmer", 75, true), ("M", "m/w", 60, true) })
        {
            var c = _kurse.Columns[_kurse.Columns.Add(n, k)];
            c.Width = b;
            c.ReadOnly = ro;
            if (!ro) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 238);
        }
        _kurse.CellEndEdit += KursGeaendert;
        _tabs.TabPages.Add(new TabPage("Kurse") { Controls = { _kurse } });

        Ui.RasterGestalten(_wahlen, true);
        _wahlen.AllowUserToAddRows = false;
        _wahlen.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 2) _wahlen.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _wahlen.CellValueChanged += WahlGeaendert;
        _tabs.TabPages.Add(new TabPage("Kurswahlen") { Controls = { _wahlen } });

        Ui.RasterGestalten(_matrix, false);
        _matrix.AllowUserToAddRows = false;
        _tabs.TabPages.Add(new TabPage("Kollisionsmatrix") { Controls = { _matrix } });

        Ui.RasterGestalten(_baender, false);
        var bLeiste = Werkzeugleiste();
        bLeiste.Items.Add(new ToolStripLabel("max. Bänder:"));
        bLeiste.Items.Add(new ToolStripControlHost(_maxBaender));
        bLeiste.Items.Add(Knopf("Bandbildung starten", "optimierung", Bandbildung, false));
        bLeiste.Items.Add(Knopf("Bänder übernehmen", "planen", Uebernehmen, false));
        var bSeite = new TabPage("Bandbildung");
        bSeite.Controls.Add(_baender);
        bSeite.Controls.Add(bLeiste);
        _tabs.TabPages.Add(bSeite);

        Controls.Add(_tabs);
        Controls.Add(_status);
        Controls.Add(ts);
        KlassenFuellen();
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art is AenderungsArt.Alles or AenderungsArt.Stammdaten) KlassenFuellen();
        Neuaufbau();
    }

    private void KlassenFuellen()
    {
        var alt = _klasse.SelectedItem as Klasse;
        _klasse.Items.Clear();
        var mitKursen = D.Klassen.Where(k => Kursplanung.Kurse(D, k.Id).Count > 0).ToList();
        foreach (var k in Stammdaten.Sortiert(mitKursen.Count > 0 ? mitKursen : D.Klassen)) _klasse.Items.Add(k);
        _klasse.ComboBox.Format += (_, e) => e.Value = e.ListItem is Klasse k ? k.Kurzname : "";
        if (alt != null) _klasse.SelectedItem = _klasse.Items.Cast<Klasse>().FirstOrDefault(k => k.Id == alt.Id);
        if (_klasse.SelectedIndex < 0 && _klasse.Items.Count > 0) _klasse.SelectedIndex = 0;
    }

    private Klasse? Klasse => _klasse.SelectedItem as Klasse;
    private List<Unterricht> Kurse => Klasse is { } k ? Kursplanung.Kurse(D, k.Id) : new();
    private List<Schueler> Schueler => Klasse is { } k ? Stammdaten.Sortiert(D.Schueler.Where(s => s.KlasseId == k.Id)).ToList() : new();

    private void Neuaufbau()
    {
        var kurse = Kurse;
        _kurse.Rows.Clear();
        foreach (var u in kurse)
        {
            var teil = D.Schueler.Where(s => s.KurswahlIds.Contains(u.Id)).ToList();
            var r = _kurse.Rows.Add(u.Id, u.Kursname, Ix.KFach(u.HauptfachId), string.Join(",", u.LehrerIds.Select(Ix.KLehrer)), Ix.KKlassen(u.KlassenIds), u.Wochenstunden, u.Band,
                teil.Count, $"{teil.Count(s => s.Geschlecht == "m")}/{teil.Count(s => s.Geschlecht == "w")}");
            _kurse.Rows[r].Tag = u;
        }
        // Kurswahlen: Schüler × Kurse
        _wahlen.Columns.Clear();
        _wahlen.Rows.Clear();
        _wahlen.Columns.Add("S", "Schüler");
        _wahlen.Columns.Add("A", "Anz.");
        _wahlen.Columns[0].ReadOnly = _wahlen.Columns[1].ReadOnly = true;
        _wahlen.Columns[0].Frozen = true;
        _wahlen.Columns[0].Width = 150;
        _wahlen.Columns[1].Width = 40;
        foreach (var u in kurse) _wahlen.Columns.Add(new DataGridViewCheckBoxColumn { Name = "K" + u.Id, HeaderText = u.Kursname, Width = 58, Tag = u.Id });
        foreach (var s in Schueler)
        {
            var werte = new object[kurse.Count + 2];
            werte[0] = $"{(s.Langname.Length > 0 ? s.Langname : s.Kurzname)}, {s.Vorname}".Trim(' ', ',');
            werte[1] = s.KurswahlIds.Count(id => kurse.Any(k => k.Id == id));
            for (var i = 0; i < kurse.Count; i++) werte[i + 2] = s.KurswahlIds.Contains(kurse[i].Id);
            var r = _wahlen.Rows.Add(werte);
            _wahlen.Rows[r].Tag = s;
        }
        // Kollisionsmatrix
        _matrix.Columns.Clear();
        _matrix.Columns.Add("K", "Kurs");
        _matrix.Columns[0].Frozen = true;
        foreach (var u in kurse) _matrix.Columns[_matrix.Columns.Add("K" + u.Id, u.Kursname)].Width = 48;
        var m = Kursplanung.Kollisionsmatrix(D, kurse);
        foreach (var a in kurse)
        {
            var r = _matrix.Rows.Add();
            _matrix.Rows[r].Cells[0].Value = $"{a.Kursname} ({a.Band})";
            for (var i = 0; i < kurse.Count; i++)
            {
                var b = kurse[i];
                var c = _matrix.Rows[r].Cells[i + 1];
                if (a.Id == b.Id) { c.Style.BackColor = Color.Gainsboro; continue; }
                var n = m.GetValueOrDefault(a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id));
                c.Value = n == 0 ? "" : n.ToString();
                var gleichesBand = !string.IsNullOrEmpty(a.Band) && a.Band == b.Band;
                if (gleichesBand) c.Style.BackColor = n > 0 ? Ui.Rot : Color.FromArgb(225, 245, 225);
            }
        }
        BaenderAnzeigen();
        _status.Text = $"{kurse.Count} Kurse, {Schueler.Count} Schüler, {Kursplanung.Kollisionen(D, kurse)} Kollisionen (Schüler mit zwei Kursen im selben Band).";
        _status.ForeColor = Kursplanung.Kollisionen(D, kurse) > 0 ? Color.DarkRed : Color.Black;
    }

    private void BaenderAnzeigen()
    {
        _baender.Columns.Clear();
        _baender.Rows.Clear();
        var kurse = Kurse;
        var zuordnung = _vorschlag ?? kurse.Where(k => !string.IsNullOrEmpty(k.Band)).GroupBy(k => k.Band).Select((g, i) => (g, i)).SelectMany(x => x.g.Select(k => (k.Id, x.i + 1))).ToDictionary(x => x.Id, x => x.Item2);
        var baender = zuordnung.Values.Distinct().OrderBy(b => b).ToList();
        _baender.Columns.Add("B", _vorschlag is null ? "Band (aktuell)" : "Band (Vorschlag)");
        _baender.Columns[0].Width = 110;
        var maxKurse = baender.Count == 0 ? 0 : baender.Max(b => zuordnung.Count(z => z.Value == b));
        for (var i = 0; i < maxKurse; i++) _baender.Columns[_baender.Columns.Add("K" + i, $"Kurs {i + 1}")].Width = 90;
        _baender.Columns.Add("S", "Schüler");
        foreach (var b in baender)
        {
            var ks = kurse.Where(k => zuordnung.TryGetValue(k.Id, out var x) && x == b).ToList();
            var werte = new object[maxKurse + 2];
            werte[0] = _vorschlag is null ? ks.First().Band : $"Band {b}";
            for (var i = 0; i < maxKurse; i++) werte[i + 1] = i < ks.Count ? $"{ks[i].Kursname} ({Kursplanung.Teilnehmer(D, ks[i].Id)})" : "";
            werte[^1] = ks.Sum(k => Kursplanung.Teilnehmer(D, k.Id));
            _baender.Rows.Add(werte);
        }
    }

    private void KursGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _kurse.Rows[e.RowIndex].Tag is not Unterricht u) return;
        var spalte = _kurse.Columns[e.ColumnIndex].Name;
        var wert = _kurse.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var id = u.Id;
        Aendern(spalte == "B" ? "Band" : "Kursname", AenderungsArt.Unterricht, d =>
        {
            var x = d.Unterrichte.First(y => y.Id == id);
            if (spalte == "B") x.Band = wert;
            else x.Kursname = wert;
        });
        BeginInvoke(Neuaufbau);
    }

    private void WahlGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 2 || _wahlen.Rows[e.RowIndex].Tag is not Schueler s || _wahlen.Columns[e.ColumnIndex].Tag is not int kursId) return;
        var gewaehlt = _wahlen.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is true;
        var sid = s.Id;
        Aendern("Kurswahl", AenderungsArt.Stammdaten, d =>
        {
            var x = d.Schueler.First(y => y.Id == sid);
            x.KurswahlIds.Remove(kursId);
            if (gewaehlt) x.KurswahlIds.Add(kursId);
        });
        _wahlen.Rows[e.RowIndex].Cells[1].Value = D.Schueler.First(y => y.Id == sid).KurswahlIds.Count(id => Kurse.Any(k => k.Id == id));
    }

    private void ZufallsWahlen()
    {
        var kurse = Kurse;
        if (kurse.Count == 0 || !Ui.Frage("Für alle Schüler der Klasse ohne Kurswahl zufällig Kurse wählen (je Fach höchstens ein Kurs)?")) return;
        var rnd = new Random(7);
        var sids = Schueler.Where(s => !s.KurswahlIds.Any(id => kurse.Any(k => k.Id == id))).Select(s => s.Id).ToHashSet();
        Aendern("Kurswahlen", AenderungsArt.Stammdaten, d =>
        {
            foreach (var s in d.Schueler.Where(s => sids.Contains(s.Id)))
                foreach (var g in kurse.GroupBy(k => k.HauptfachId))
                    if (rnd.NextDouble() < 0.8) s.KurswahlIds.Add(g.ElementAt(rnd.Next(g.Count())).Id);
        });
    }

    private void Bandbildung()
    {
        var kurse = Kurse;
        if (kurse.Count == 0) return;
        using (new WarteCursor()) _vorschlag = Kursplanung.Bandbildung(D, kurse, (int)_maxBaender.Value);
        var m = Kursplanung.Kollisionsmatrix(D, kurse);
        var koll = m.Where(kv => _vorschlag[kv.Key.Item1] == _vorschlag[kv.Key.Item2]).Sum(kv => kv.Value);
        BaenderAnzeigen();
        _status.Text = $"Vorschlag: {_vorschlag.Values.Distinct().Count()} Bänder, {koll} Kollisionen. Mit \"Bänder übernehmen\" in den Unterricht schreiben.";
    }

    private void Uebernehmen()
    {
        if (_vorschlag is null || Klasse is null) { Ui.Info("Bitte zuerst die Bandbildung starten."); return; }
        var praefix = Ui.Eingabe("Präfix für die Bandbezeichnung:", "Bänder übernehmen", Klasse.Kurzname);
        if (string.IsNullOrWhiteSpace(praefix)) return;
        var z = _vorschlag;
        Aendern("Bänder übernehmen", AenderungsArt.Unterricht, d => Kursplanung.BaenderUebernehmen(d, praefix.Trim(), z));
        _vorschlag = null;
        Neuaufbau();
    }
}
