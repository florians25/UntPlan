using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vergleichsmodus wie im Handbuch (7.17): Auswahl des Vergleichsbestands („Daten zu vergleichen“: Datei A = aktueller Bestand grün,
/// Datei B rot; „2. UntPlan-Datei öffnen“ bzw. „Mit der aktuellen Datei“ und „Zu vergleichende Periode“), danach das Vergleichsfenster
/// mit den Optionen „ohne Räume“, „Detaillierter Vergleich bei Kopplungen“ und „Nur geänderte Pläne“ (Stundenplanvergleich) sowie
/// „Nur unterschiedliche Zeilen anzeigen“ / „Nur unterschiedliche Spalten anzeigen“ (Stammdaten- und Unterrichtsvergleich).
/// </summary>
public sealed class VergleichFenster : MdiFenster
{
    internal static readonly Color Gruen = Color.FromArgb(120, 230, 120);
    internal static readonly Color Rot = Color.FromArgb(245, 140, 140);

    private readonly Schuldatei _b;
    private readonly string _nameB;
    private readonly ToolStripButton _ohneRaeume, _detailliert, _nurGeaendert, _nurZeilen, _nurSpalten;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _planArt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _stammArt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ListBox _plaene = new() { Dock = DockStyle.Left, Width = 120, IntegralHeight = false };
    private readonly DataGridView _stunden = new() { Dock = DockStyle.Fill, Name = "Stunden" };
    private readonly DataGridView _stamm = new() { Dock = DockStyle.Fill, Name = "Stammdaten" };
    private readonly DataGridView _unt = new() { Dock = DockStyle.Fill, Name = "Unterricht" };
    private readonly DataGridView _kennzahlen = new() { Dock = DockStyle.Fill, Name = "Kennzahlen" };

    /// <summary>Auswahldialog „Vergleichsmodus“ und danach das Vergleichsfenster.</summary>
    public static void Starten(IWin32Window owner)
    {
        using var d = new Dialoge.Dialog("Vergleichsmodus", 470, 196);
        var g = new GroupBox { Text = "Daten zu vergleichen", Left = 8, Top = 4, Width = 224, Height = 150 };
        var a = new Label { Left = 8, Top = 22, Width = 140, Text = Path.GetFileName(AppZustand.Doc.Pfad ?? "Aktuelle Daten"), AutoEllipsis = true };
        var bName = new Label { Left = 8, Top = 44, Width = 140, Text = "", AutoEllipsis = true };
        g.Controls.Add(a);
        g.Controls.Add(bName);
        g.Controls.Add(new Label { Text = "Datei A", Left = 152, Top = 20, Width = 62, Height = 18, BackColor = Gruen, TextAlign = ContentAlignment.MiddleCenter });
        g.Controls.Add(new Label { Text = "Datei B", Left = 152, Top = 42, Width = 62, Height = 18, BackColor = Rot, TextAlign = ContentAlignment.MiddleCenter });
        var oeffnen = new Button { Text = "2. UntPlan-Datei öffnen", Left = 8, Top = 70, Width = 206, Height = 24 };
        var aktuell = new CheckBox { Text = "Mit der aktuellen Datei", Left = 8, Top = 104, AutoSize = true };
        g.Controls.AddRange(new Control[] { oeffnen, aktuell });
        var pg = new GroupBox { Text = "Zu vergleichende Periode", Left = 240, Top = 4, Width = 222, Height = 110, Enabled = false };
        var periode = new ComboBox { Left = 8, Top = 20, Width = 204, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in AppZustand.D.Perioden.OrderBy(p => p.Von)) periode.Items.Add(new PeriodeEintrag(p));
        if (periode.Items.Count > 0) periode.SelectedIndex = 0;
        var info = new Label { Left = 8, Top = 50, Width = 204, Height = 54, ForeColor = Color.DimGray };
        pg.Controls.AddRange(new Control[] { periode, info });
        d.Controls.Add(g);
        d.Controls.Add(pg);
        d.Ok.Text = "Ok";
        Schuldatei? b = null;
        var nameB = "";
        void Info() => info.Text = $"Datei A: {a.Text}\nDatei B: {(aktuell.Checked ? a.Text + " (" + (periode.SelectedItem as PeriodeEintrag)?.P.Name + ")" : nameB)}";
        oeffnen.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = Dokument.DateiFilter, Title = "Vergleichsdatei wählen" };
            if (ofd.ShowDialog(d) != DialogResult.OK) return;
            try { b = Dokument.Laden(ofd.FileName).Daten; nameB = Path.GetFileName(ofd.FileName); bName.Text = nameB; aktuell.Checked = false; Info(); }
            catch (Exception ex) { Ui.Fehler(ex); }
        };
        aktuell.CheckedChanged += (_, _) => { pg.Enabled = aktuell.Checked; Info(); };
        periode.SelectedIndexChanged += (_, _) => Info();
        Info();
        if (d.ShowDialog(owner) != DialogResult.OK) return;
        if (aktuell.Checked && periode.SelectedItem is PeriodeEintrag pe) { b = Vergleich.Periode(AppZustand.D, pe.P.Id); nameB = $"{a.Text} ({pe.P.Name})"; }
        if (b is null) { Ui.Info("Bitte zuerst eine zweite Datei öffnen oder „Mit der aktuellen Datei“ und eine Periode wählen.", "Vergleichsmodus"); return; }
        var f = new VergleichFenster(b, nameB);
        f.Show();
    }

    private sealed record PeriodeEintrag(Periode P)
    {
        public override string ToString() => $"{P.Name} ({Format.Datum(P.Von)})";
    }

    public VergleichFenster(Schuldatei b, string nameB)
    {
        _b = b;
        _nameB = nameB;
        Text = $"Vergleichsmodus – {nameB}";
        HilfeThema = "Stundenpläne";
        Icon = SymbolIcon("suchen");
        Size = new Size(900, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ToolStripButton Schalter(string text, bool an)
        {
            var k = new ToolStripButton(text) { CheckOnClick = true, Checked = an, DisplayStyle = ToolStripItemDisplayStyle.Text };
            k.CheckedChanged += (_, _) => Neuaufbau();
            ts.Items.Add(k);
            return k;
        }
        _ohneRaeume = Schalter("ohne Räume", false);
        _detailliert = Schalter("Detaillierter Vergleich bei Kopplungen", false);
        _nurGeaendert = Schalter("Nur geänderte Pläne", true);
        ts.Items.Add(new ToolStripSeparator());
        _nurZeilen = Schalter("Nur unterschiedliche Zeilen anzeigen", true);
        _nurSpalten = Schalter("Nur unterschiedliche Spalten anzeigen", true);
        foreach (var c in new[] { _planArt, _stammArt })
        {
            c.Items.AddRange(new object[] { "Klassen", "Lehrer", "Räume", "Fächer" });
            c.SelectedIndex = 0;
            c.SelectedIndexChanged += (_, _) => Neuaufbau();
        }
        foreach (var g in new[] { _stunden, _stamm, _unt, _kennzahlen })
        {
            Ui.RasterGestalten(g, false);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
        }
        _plaene.SelectedIndexChanged += (_, _) => StundenZeigen();
        _plaene.DoubleClick += (_, _) => { if (_plaene.SelectedItem is string n && D.AlleElemente(PlanTyp).FirstOrDefault(e => e.Kurzname == n) is { } el) StundenplanFenster.Neu(PlanTyp, el.Id, false); };
        var plan = new TabPage("Stundenpläne");
        plan.Controls.Add(_stunden);
        plan.Controls.Add(_plaene);
        plan.Controls.Add(Kopfzeile(_planArt, "Doppelklick auf einen Plan: Stundenplan des aktuellen Bestands öffnen"));
        var stamm = new TabPage("Stammdaten");
        stamm.Controls.Add(_stamm);
        stamm.Controls.Add(Kopfzeile(_stammArt, ""));
        _tabs.TabPages.Add(plan);
        _tabs.TabPages.Add(stamm);
        _tabs.TabPages.Add(new TabPage("Unterricht") { Controls = { _unt } });
        _tabs.TabPages.Add(new TabPage("Kennzahlen") { Controls = { _kennzahlen } });
        _stamm.CellFormatting += Faerben;
        _unt.CellFormatting += Faerben;
        _stunden.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0) e.CellStyle!.BackColor = _stunden.Rows[e.RowIndex].Tag is true ? Gruen : Rot; };
        Controls.Add(_tabs);
        Controls.Add(ts);
        Neuaufbau();
    }

    private static Panel Kopfzeile(ComboBox art, string hinweis)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 28 };
        art.Location = new Point(4, 3);
        p.Controls.Add(art);
        p.Controls.Add(new Label { Text = hinweis, Left = 104, Top = 7, AutoSize = true, ForeColor = Color.DimGray });
        return p;
    }

    private static ElementTyp Typ(ComboBox c) => c.SelectedIndex switch { 1 => ElementTyp.Lehrer, 2 => ElementTyp.Raum, 3 => ElementTyp.Fach, _ => ElementTyp.Klasse };
    private ElementTyp PlanTyp => Typ(_planArt);

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art != AenderungsArt.Vertretung) Neuaufbau();
    }

    private void Neuaufbau()
    {
        using var _ = new Dialoge.WarteCursor();
        // Stundenpläne: nur geänderte bzw. alle Pläne
        var gewaehlt = _plaene.SelectedItem as string;
        _plaene.Items.Clear();
        var namen = _nurGeaendert.Checked
            ? Vergleich.GeaendertePlaene(D, _b, PlanTyp, _ohneRaeume.Checked, _detailliert.Checked)
            : D.AlleElemente(PlanTyp).Select(e => e.Kurzname).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var n in namen) _plaene.Items.Add(n);
        if (gewaehlt != null && _plaene.Items.Contains(gewaehlt)) _plaene.SelectedItem = gewaehlt;
        else if (_plaene.Items.Count > 0) _plaene.SelectedIndex = 0;
        else StundenZeigen();
        Tabelle(_stamm, Vergleich.Stammdaten(D, _b, Typ(_stammArt), _nurZeilen.Checked, _nurSpalten.Checked), "Name");
        Tabelle(_unt, Vergleich.Unterrichte(D, _b, _nurZeilen.Checked, _nurSpalten.Checked), "U-Nr");
        var ka = Bewertung.Kennzahlen(D);
        var kb = Bewertung.Kennzahlen(_b);
        _kennzahlen.Columns.Clear();
        _kennzahlen.Columns.Add("K", "Kennzahl");
        _kennzahlen.Columns.Add("A", "Aktueller Bestand");
        _kennzahlen.Columns.Add("B", _nameB);
        _kennzahlen.Columns[0].Width = 180;
        _kennzahlen.Columns[1].DefaultCellStyle.BackColor = Gruen;
        _kennzahlen.Columns[2].DefaultCellStyle.BackColor = Rot;
        foreach (var (n, a, b) in new (string, object, object)[]
                 {
                     ("Bewertung", ka.Bewertung, kb.Bewertung), ("Nicht verplante Stunden", ka.NichtVerplant, kb.NichtVerplant), ("Hohlstunden Klassen", ka.HohlstundenKlassen, kb.HohlstundenKlassen),
                     ("Hohlstunden Lehrer", ka.HohlstundenLehrer, kb.HohlstundenLehrer), ("Kernzeitverletzungen", ka.Kernzeitverletzungen, kb.Kernzeitverletzungen),
                     ("Fach 2x / Tag", ka.FachZweimalTag, kb.FachZweimalTag), ("Kollisionen", ka.Kollisionen, kb.Kollisionen),
                 })
            _kennzahlen.Rows.Add(n, a, b);
    }

    /// <summary>Unterschiedliche Stunden des gewählten Plans: grün nur im aktuellen, rot nur im Vergleichsbestand.</summary>
    private void StundenZeigen()
    {
        _stunden.Columns.Clear();
        foreach (var (n, t, b) in new[] { ("B", "Bestand", 70), ("Z", "Zeit", 70), ("F", "Fach", 60), ("K", "Klasse(n)", 90), ("L", "Lehrer", 70), ("R", "Raum", 60) })
            _stunden.Columns[_stunden.Columns.Add(n, t)].Width = b;
        if (_plaene.SelectedItem is not string name) return;
        var (nurA, nurB) = Vergleich.Stundenunterschiede(D, _b, PlanTyp, name, _ohneRaeume.Checked, _detailliert.Checked);
        void Zeilen(IEnumerable<string> liste, bool aktuell)
        {
            foreach (var s in liste)
            {
                var t = s.Split('|');
                var r = _stunden.Rows.Add(aktuell ? "aktuell" : "Vergleich", $"{Planungsdienst.Tagname(D, int.Parse(t[0]), true)}-{t[1]}", t[2], t[3], t[4], t[5]);
                _stunden.Rows[r].Tag = aktuell;
            }
        }
        Zeilen(nurA, true);
        Zeilen(nurB, false);
    }

    /// <summary>Je Element bzw. Unterricht zwei Zeilen (aktuell grün, Vergleich rot); „Nur unterschiedliche Spalten“ blendet gleiche Spalten aus.</summary>
    private void Tabelle(DataGridView g, List<DateiVergleichsZeile> unterschiede, string schluessel)
    {
        g.Columns.Clear();
        g.Rows.Clear();
        var felder = unterschiede.SelectMany(z => z.Felder.Select(f => f.Feld)).Distinct().ToList();
        g.Columns.Add("S", schluessel);
        g.Columns[0].Width = 60;
        foreach (var f in felder) g.Columns[g.Columns.Add("F_" + f, f)].Width = 90;
        foreach (var z in unterschiede)
        {
            var a = new object[felder.Count + 1];
            var b = new object[felder.Count + 1];
            a[0] = z.Schluessel;
            b[0] = "";
            for (var i = 0; i < felder.Count; i++)
            {
                var f = z.Felder.FirstOrDefault(x => x.Feld == felder[i]);
                a[i + 1] = f.Feld is null ? "" : f.Aktuell;
                b[i + 1] = f.Feld is null ? "" : f.Vergleich;
            }
            g.Rows[g.Rows.Add(a)].Tag = true;
            g.Rows[g.Rows.Add(b)].Tag = false;
        }
    }

    private void Faerben(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView g || e.RowIndex < 0 || e.ColumnIndex <= 0 || g.Rows[e.RowIndex].Tag is not bool aktuell) return;
        var andere = g.Rows[e.RowIndex + (aktuell ? 1 : -1)];
        var unterschied = !Equals(e.Value?.ToString() ?? "", andere.Cells[e.ColumnIndex].Value?.ToString() ?? "");
        if (unterschied) e.CellStyle!.BackColor = aktuell ? Gruen : Rot;
    }
}
