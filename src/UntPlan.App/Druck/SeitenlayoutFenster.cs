using UntPlan.App.Dialoge;
using UntPlan.Core;

namespace UntPlan.App.Druck;

/// <summary>
/// Seitenlayout des Vertretungsplans wie im Vorbild: links die Seitenvorschau (WYSIWYG, Seiten blättern, Zoom, Stifte über den
/// Spaltenköpfen zum Ändern von Überschrift und Breite), rechts die Einstellungen – Auswahl der Klassen/Lehrer, „Druck nur, wenn
/// geändert nach …“, Erste Beschriftungsspalte auf jeder Seite, Raster, Beschränkung auf Seitenbreite, Felder, Hintergrund-Bild
/// sowie die Karten „Aktuelle Ansicht“ (Auswahl, Gliederung, Form, Abwesend, Überschrift) und „Global“ (Schrift, Format).
/// Jede Änderung ist sofort in der Vorschau zu sehen; Vorschau und Druck zeichnen mit demselben Code.
/// </summary>
public sealed class SeitenlayoutFenster : Form
{
    private readonly DateOnly _von, _bis;
    private bool _lehrer;
    private readonly PictureBox _seite = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly Panel _vorschauRahmen = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(160, 166, 176) };
    private readonly ToolStripLabel _seitenText = new("1-1 / 1");
    private readonly ToolStripComboBox _zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ToolStripComboBox _schrift = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ToolStripComboBox _groesse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 };
    private readonly Label _auswahlText = new() { AutoSize = false, Width = 160, Height = 18 };
    private readonly CheckedListBox _felder = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly Label _bildText = new() { Text = "<nicht definiert>", AutoSize = false, Width = 150, Height = 32, ForeColor = Color.DimGray };
    private readonly List<Button> _stifte = new();
    private List<Bitmap> _bilder = new();
    private List<(string Schluessel, RectangleF Kopf)> _spalten = new();
    private int _seiteNr;
    private bool _laden;

    private static Vertretungsdruck E => AppZustand.Einstellungen.Vertretungsdruck;

    /// <summary>Für den Selbsttest: Anzahl der Vorschauseiten und gedruckte Felder.</summary>
    internal int Seitenzahl => _bilder.Count;
    internal IReadOnlyList<string> GedruckteFelder => _spalten.Select(s => s.Schluessel).ToList();
    internal Bitmap? Vorschaubild => _bilder.ElementAtOrDefault(_seiteNr);

    public static SeitenlayoutFenster Zeigen(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrer)
    {
        var f = new SeitenlayoutFenster(von, bis, lehrer);
        f.Show(owner);
        return f;
    }

    private SeitenlayoutFenster(DateOnly von, DateOnly bis, bool lehrer)
    {
        _von = von;
        _bis = bis;
        _lehrer = lehrer;
        Text = "Seitenlayout";
        Font = Ui.Schrift;
        Icon = Icon.FromHandle(Symbole.Get("drucken", 16).GetHicon());
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(900, 560);
        ShowInTaskbar = false;
        BackColor = Ui.BlauSehrHell;

        // Werkzeugleiste: blättern, Zoom, Seitenansicht/Drucken, Schrift
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
        ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(false), (_, _) => Blaettern(-1)) { ToolTipText = "Vorherige Seite", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(_seitenText);
        ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(true), (_, _) => Blaettern(1)) { ToolTipText = "Nächste Seite", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripSeparator());
        _zoom.Items.AddRange(new object[] { "50 %", "75 %", "100 %", "125 %", "Ganze Seite" });
        _zoom.SelectedIndex = 4;
        _zoom.SelectedIndexChanged += (_, _) => Anordnen();
        ts.Items.Add(_zoom);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Seitenansicht", Symbole.Get("vorschau", 16), (_, _) => VertretungsplanDruck.Ausgeben(this, _von, _bis, _lehrer, true)));
        ts.Items.Add(new ToolStripButton("Drucken", Symbole.Get("drucken", 16), (_, _) => VertretungsplanDruck.Ausgeben(this, _von, _bis, _lehrer, false)));
        ts.Items.Add(new ToolStripSeparator());
        _schrift.Items.AddRange(FontFamily.Families.Select(f => (object)f.Name).ToArray());
        _groesse.Items.AddRange(new object[] { "8", "9", "10", "11", "12", "14", "16" });
        ts.Items.Add(_schrift);
        ts.Items.Add(_groesse);
        var version = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        version.Items.AddRange(new object[] { "Klassen", "Lehrer" });
        version.SelectedIndex = lehrer ? 1 : 0;
        version.SelectedIndexChanged += (_, _) => { _lehrer = version.SelectedIndex == 1; EinstellungenLaden(); Neuzeichnen(); };
        ts.Items.Add(new ToolStripLabel("Version:"));
        ts.Items.Add(version);
        _schrift.SelectedIndexChanged += (_, _) => { if (!_laden && _schrift.SelectedItem is string s) { E.Schriftart = s; Neuzeichnen(); } };
        _groesse.SelectedIndexChanged += (_, _) => { if (!_laden && float.TryParse(_groesse.SelectedItem as string, out var g)) { E.Schriftgroesse = g; Neuzeichnen(); } };

        _vorschauRahmen.Controls.Add(_seite);
        _vorschauRahmen.Resize += (_, _) => Anordnen();
        var rechts = new Panel { Dock = DockStyle.Right, Width = 440, AutoScroll = true, Padding = new Padding(8) };
        RechtsAufbauen(rechts);
        Controls.Add(_vorschauRahmen);
        Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 4 });
        Controls.Add(rechts);
        Controls.Add(ts);
        EinstellungenLaden();
        Shown += (_, _) => Neuzeichnen();
        FormClosed += (_, _) => { AppZustand.Einstellungen.Speichern(); foreach (var b in _bilder) b.Dispose(); };
        if (!IsHandleCreated) Neuzeichnen();
    }

    private void RechtsAufbauen(Panel p)
    {
        var y = 8;
        void Add(Control c, int x, int w, int h = 22) { c.SetBounds(x, y, w, h); p.Controls.Add(c); }

        Add(_auswahlText, 8, 170, 20);
        var auswahl = new Button { Text = "Auswahl" };
        auswahl.Click += (_, _) => AuswahlDialog();
        Add(auswahl, 180, 80, 24);
        y += 30;
        var geaendert = new CheckBox { Text = "Druck nur, wenn geändert nach …", Checked = E.NurGeaendertNach.HasValue };
        Add(geaendert, 8, 240);
        y += 24;
        var nach = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy  HH:mm", Value = E.NurGeaendertNach ?? DateTime.Today, Enabled = geaendert.Checked };
        Add(nach, 26, 150);
        var heute = new Button { Text = "Heute", Enabled = geaendert.Checked };
        Add(heute, 184, 70, 24);
        geaendert.CheckedChanged += (_, _) => { nach.Enabled = heute.Enabled = geaendert.Checked; E.NurGeaendertNach = geaendert.Checked ? nach.Value : null; Neuzeichnen(); };
        nach.ValueChanged += (_, _) => { if (geaendert.Checked) { E.NurGeaendertNach = nach.Value; Neuzeichnen(); } };
        heute.Click += (_, _) => nach.Value = DateTime.Today;
        y += 32;
        Add(Haken("Erste Beschriftungsspalte auf jeder Seite", () => E.ErsteBeschriftungJedeSeite, v => E.ErsteBeschriftungJedeSeite = v), 8, 300);
        y += 22;
        Add(Haken("Raster", () => E.Raster, v => E.Raster = v), 8, 300);
        y += 22;
        Add(Haken("Beschränkung auf Seitenbreite", () => E.BeschraenkungSeitenbreite, v => E.BeschraenkungSeitenbreite = v), 8, 300);
        y += 30;

        // Felder (Druckreihenfolge, drucken ja/nein) und Hintergrund-Bild
        Add(new Label { Text = "Felder" }, 8, 120, 18);
        Add(new Label { Text = "Hintergrund-Bild" }, 250, 150, 18);
        y += 20;
        Add(_felder, 8, 200, 150);
        _felder.ItemCheck += (_, e) =>
        {
            if (_laden) return;
            var f = VertretungsplanDruck.Felder(E)[e.Index];
            f.Drucken = e.NewValue == CheckState.Checked;
            BeginInvoke(Neuzeichnen);
        };
        var hoch = new Button { Text = "▲" };
        var runter = new Button { Text = "▼" };
        var felderDialog = new Button { Text = "Felder der Ansicht…" };
        p.Controls.AddRange(new Control[] { hoch, runter });
        hoch.SetBounds(212, y, 28, 26);
        runter.SetBounds(212, y + 30, 28, 26);
        hoch.Click += (_, _) => FeldVerschieben(-1);
        runter.Click += (_, _) => FeldVerschieben(1);
        var bildWahl = new Button { Image = Symbole.Get("oeffnen", 16) };
        var bildWeg = new Button { Image = Symbole.Get("loeschen", 16) };
        p.Controls.AddRange(new Control[] { bildWahl, bildWeg, _bildText });
        bildWahl.SetBounds(250, y, 32, 28);
        bildWeg.SetBounds(286, y, 32, 28);
        _bildText.SetBounds(250, y + 120, 170, 32);
        bildWahl.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Alle Dateien|*.*", Title = "Hintergrund-Bild" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            E.Hintergrundbild = dlg.FileName;
            EinstellungenLaden();
            Neuzeichnen();
        };
        bildWeg.Click += (_, _) => { E.Hintergrundbild = ""; EinstellungenLaden(); Neuzeichnen(); };
        y += 156;
        Add(felderDialog, 8, 200, 26);
        felderDialog.Click += (_, _) => FelderDerAnsichtDialog.Zeigen(this, VertretungsplanDruck.Felder(E), k => VertretungsplanDruck.StandardUeberschrift(k, _lehrer), () => { EinstellungenLaden(); Neuzeichnen(); });
        y += 34;

        // Karten „Aktuelle Ansicht“ und „Global“
        var tabs = new TabControl { Font = new Font("Segoe UI", 8.25f) };
        Add(tabs, 8, 410, 400);
        var ansicht = new TabPage("Aktuelle Ansicht");
        var global = new TabPage("Global");
        tabs.TabPages.Add(ansicht);
        tabs.TabPages.Add(global);
        var gAuswahl = Gruppe(ansicht, "Auswahl", 4, 4, 394, 84);
        Haken(gAuswahl, "Nur neue Vertretungen", () => E.NurNeue, v => E.NurNeue = v, 18, 370);
        Haken(gAuswahl, "Abwesende Lehrer/Klassen nicht drucken", () => E.AbwesendeNichtDrucken, v => E.AbwesendeNichtDrucken = v, 38, 370);
        Haken(gAuswahl, "Auch Elemente ohne Vertretung", () => E.AuchElementeOhneVertretung, v => E.AuchElementeOhneVertretung = v, 58, 370);
        var gForm = Gruppe(ansicht, "Form", 194, 92, 204, 156);
        Haken(gForm, "Entfallslehrer durchstreichen", () => E.EntfallslehrerDurchstreichen, v => E.EntfallslehrerDurchstreichen = v, 18, 196);
        Haken(gForm, "Komb. Klassen (2a,2b->2ab)", () => E.KombinierteKlassen, v => E.KombinierteKlassen = v, 40, 196);
        Haken(gForm, "Stundenbezeichnung", () => E.Stundenbezeichnung, v => E.Stundenbezeichnung = v, 62, 196);
        Haken(gForm, "Stammdaten in einzelnem Feld", () => E.StammdatenEinzeln, v => E.StammdatenEinzeln = v, 84, 196);
        Haken(gForm, "Blockvertretungen zusammenfassen", () => E.BlockvertretungenZusammenfassen, v => E.BlockvertretungenZusammenfassen = v, 106, 196);
        Haken(gForm, "Kein Entfall bei Ersatz in Stunde", () => E.KeinEntfallBeiErsatz, v => E.KeinEntfallBeiErsatz = v, 128, 196);
        var gGliederung = Gruppe(ansicht, "Gliederung", 4, 92, 186, 172);
        Haken(gGliederung, "Überschrift / Element", () => E.UeberschriftJeElement, v => E.UeberschriftJeElement = v, 18);
        Haken(gGliederung, "1 Seite / Element", () => E.EineSeiteJeElement, v => E.EineSeiteJeElement = v, 40);
        Haken(gGliederung, "Elementüberschrift komprimiert", () => E.ElementueberschriftKomprimiert, v => E.ElementueberschriftKomprimiert = v, 62);
        gGliederung.Controls.Add(new Label { Text = "Gruppierung (=Sortierkriterium 1)", Left = 8, Top = 86, Width = 176, Height = 16 });
        var gruppierung = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 106, Width = 176 };
        gruppierung.Items.AddRange(new object[] { "Element", "Stunde", "Vertreter", "Fach" });
        gruppierung.SelectedItem = E.Gruppierung;
        if (gruppierung.SelectedIndex < 0) gruppierung.SelectedIndex = 0;
        gruppierung.SelectedIndexChanged += (_, _) => { E.Gruppierung = gruppierung.SelectedItem as string ?? "Element"; Neuzeichnen(); };
        var sortierung = new Button { Text = "Sortierung", Left = 8, Top = 136, Width = 176, Height = 24 };
        sortierung.Click += (_, _) => Ui.Info($"Sortiert wird zuerst nach „{E.Gruppierung}“, danach nach Stunde bzw. Element.", "Sortierung");
        gGliederung.Controls.AddRange(new Control[] { gruppierung, sortierung });
        var gAbw = Gruppe(ansicht, "Abwesend", 194, 252, 204, 112);
        gAbw.Controls.Add(new Label { Text = "Absenz", Left = 100, Top = 14, Width = 60 });
        Haken(gAbw, "Lehrer", () => E.AbwesendeLehrer, v => E.AbwesendeLehrer = v, 32, 150);
        Haken(gAbw, "mit Absenzgrund", () => E.LehrerMitAbsenzgrund, v => E.LehrerMitAbsenzgrund = v, 52, 160, 24);
        Haken(gAbw, "Klassen", () => E.AbwesendeKlassen, v => E.AbwesendeKlassen = v, 72, 150);
        Haken(gAbw, "Räume", () => E.BlockierteRaeume, v => E.BlockierteRaeume = v, 90, 150);
        var ueberschrift = new Button { Text = "Überschrift", Left = 4, Top = 300, Width = 110, Height = 26 };
        ueberschrift.Click += (_, _) =>
        {
            var t = Ui.Eingabe("Überschrift des Vertretungsplans (leer = Titel aus den Schuldaten):", "Überschrift", E.Ueberschrift);
            if (t is null) return;
            E.Ueberschrift = t.Trim();
            Neuzeichnen();
        };
        ansicht.Controls.Add(ueberschrift);
        var gy = 10;
        Ui.Zeile(global, "Querformat", Haken("", () => E.Querformat, v => E.Querformat = v), ref gy, 170);
        Ui.Zeile(global, "Nachrichten zum Tag", Haken("", () => E.Nachrichten, v => E.Nachrichten = v), ref gy, 170);
        global.Controls.Add(new Label { Text = "Schriftart und -größe stehen in der Werkzeugleiste über der Vorschau.", Left = 12, Top = gy + 8, Width = 380, Height = 34, ForeColor = Color.DimGray });
    }

    private static GroupBox Gruppe(Control parent, string titel, int x, int y, int w, int h)
    {
        var g = new GroupBox { Text = titel, Left = x, Top = y, Width = w, Height = h };
        parent.Controls.Add(g);
        return g;
    }

    private CheckBox Haken(string text, Func<bool> lesen, Action<bool> schreiben)
    {
        var c = new CheckBox { Text = text, Checked = lesen() };
        c.CheckedChanged += (_, _) => { if (_laden) return; schreiben(c.Checked); Neuzeichnen(); };
        return c;
    }

    private void Haken(GroupBox g, string text, Func<bool> lesen, Action<bool> schreiben, int y, int w = 186, int x = 8)
    {
        var c = Haken(text, lesen, schreiben);
        c.SetBounds(x, y, w, 20);
        g.Controls.Add(c);
    }

    private void EinstellungenLaden()
    {
        _laden = true;
        try
        {
            _schrift.SelectedItem = VertretungsplanDruck.SchriftartPruefen(E.Schriftart);
            var gr = ((int)Math.Round(E.Schriftgroesse)).ToString();
            if (!_groesse.Items.Contains(gr)) _groesse.Items.Add(gr);
            _groesse.SelectedItem = gr;
            _felder.Items.Clear();
            foreach (var f in VertretungsplanDruck.Felder(E))
                _felder.Items.Add(VertretungsplanDruck.Ueberschrift(f, _lehrer).Replace('\n', ' '), f.Drucken);
            _bildText.Text = E.Hintergrundbild.Length > 0 ? Path.GetFileName(E.Hintergrundbild) : "<nicht definiert>";
            var d = AppZustand.D;
            var auswahl = _lehrer ? E.LehrerAuswahl : E.KlassenAuswahl;
            var alle = _lehrer ? d.Lehrer.Count : d.Klassen.Count;
            _auswahlText.Text = $"{(_lehrer ? "Lehrer" : "Klasse(n)")}: {(auswahl.Count == 0 ? alle : auswahl.Count)}/{alle}";
        }
        finally { _laden = false; }
    }

    private void FeldVerschieben(int richtung)
    {
        var i = _felder.SelectedIndex;
        var liste = VertretungsplanDruck.Felder(E);
        if (i < 0 || i + richtung < 0 || i + richtung >= liste.Count) return;
        (liste[i], liste[i + richtung]) = (liste[i + richtung], liste[i]);
        EinstellungenLaden();
        _felder.SelectedIndex = i + richtung;
        Neuzeichnen();
    }

    private void AuswahlDialog()
    {
        var d = AppZustand.D;
        var namen = (_lehrer ? d.Lehrer.Select(x => (x.Kurzname, x.Langname)) : d.Klassen.Select(x => (x.Kurzname, x.Langname))).ToList();
        var auswahl = _lehrer ? E.LehrerAuswahl : E.KlassenAuswahl;
        using var dlg = new Dialog(_lehrer ? "Lehrer auswählen" : "Klassen auswählen", 320, 460);
        var liste = new CheckedListBox { Left = 10, Top = 10, Width = 298, Height = 360, CheckOnClick = true };
        foreach (var (k, l) in namen) liste.Items.Add($"{k}  {l}", auswahl.Count == 0 || auswahl.Contains(k));
        var alle = new Button { Text = "Alle", Left = 10, Top = 376, Width = 80 };
        var keine = new Button { Text = "Keine", Left = 96, Top = 376, Width = 80 };
        alle.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, true); };
        keine.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, false); };
        dlg.Controls.AddRange(new Control[] { liste, alle, keine });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var gewaehlt = Enumerable.Range(0, namen.Count).Where(liste.GetItemChecked).Select(i => namen[i].Kurzname).ToList();
        auswahl.Clear();
        if (gewaehlt.Count < namen.Count) auswahl.AddRange(gewaehlt);
        EinstellungenLaden();
        Neuzeichnen();
    }

    /// <summary>Vorschau neu berechnen (jede Einstellung wirkt sofort).</summary>
    internal void Neuzeichnen()
    {
        if (_laden) return;
        foreach (var b in _bilder) b.Dispose();
        _bilder = VertretungsplanDruck.Bilder(_von, _bis, _lehrer, out _spalten);
        _seiteNr = Math.Clamp(_seiteNr, 0, Math.Max(0, _bilder.Count - 1));
        Anordnen();
    }

    private void Blaettern(int r)
    {
        _seiteNr = Math.Clamp(_seiteNr + r, 0, Math.Max(0, _bilder.Count - 1));
        Anordnen();
    }

    /// <summary>Seite im gewählten Zoom zeigen und die Stifte über die Spaltenköpfe legen.</summary>
    private bool _ordnen;

    private void Anordnen()
    {
        // das Einfügen der Stifte ändert die Bildlaufleisten und damit die Größe – kein erneutes Anordnen währenddessen
        if (_ordnen) return;
        _ordnen = true;
        _vorschauRahmen.SuspendLayout();
        try { AnordnenIntern(); }
        finally { _vorschauRahmen.ResumeLayout(); _ordnen = false; }
    }

    private void AnordnenIntern()
    {
        _seitenText.Text = $"{(_bilder.Count == 0 ? 0 : _seiteNr + 1)}-{_bilder.Count} / {_bilder.Count}";
        var bild = _bilder.ElementAtOrDefault(_seiteNr);
        _seite.Image = bild;
        foreach (var s in _stifte) { _vorschauRahmen.Controls.Remove(s); s.Dispose(); }
        _stifte.Clear();
        if (bild is null) return;
        var platz = _vorschauRahmen.ClientSize;
        var faktor = _zoom.SelectedIndex switch
        {
            0 => 0.5f, 1 => 0.75f, 2 => 1f, 3 => 1.25f,
            _ => Math.Max(0.2f, Math.Min((platz.Width - 30f) / bild.Width, (platz.Height - 30f) / bild.Height)),
        };
        var w = (int)(bild.Width * faktor);
        var h = (int)(bild.Height * faktor);
        _seite.SetBounds(Math.Max(10, (platz.Width - w) / 2), 14, w, h);
        // Stifte über den Spaltenköpfen (nur auf Seiten mit derselben Tabelle sinnvoll – die erste Seite bestimmt die Lage)
        foreach (var (schluessel, kopf) in _spalten.ToList())
        {
            var stift = new Button
            {
                Text = "✎", FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(40, 70, 160), Font = new Font("Segoe UI Symbol", 8f),
                Size = new Size(16, 16), Tag = schluessel, TabStop = false, Padding = new Padding(0),
            };
            stift.FlatAppearance.BorderSize = 0;
            // im Spaltenkopf oben rechts (wie die Stifte in Vorbild), nicht über dem Inhalt darüber
            stift.Location = new Point(_seite.Left + (int)(kopf.Right * faktor) - 17, _seite.Top + (int)(kopf.Y * faktor) + 1);
            stift.Click += (_, _) => SpalteBearbeiten(schluessel);
            new ToolTip().SetToolTip(stift, "Überschrift und Breite der Spalte ändern");
            _stifte.Add(stift);
            _vorschauRahmen.Controls.Add(stift);
            stift.BringToFront();
        }
    }

    private void SpalteBearbeiten(string schluessel)
    {
        var feld = VertretungsplanDruck.Felder(E).First(f => f.Schluessel == schluessel);
        using var d = new Dialog("Spalte bearbeiten", 380, 190);
        var y = 14;
        var text = Ui.Zeile(d, "Überschrift", new TextBox { Text = VertretungsplanDruck.Ueberschrift(feld, _lehrer).Replace("\n", "\\n") }, ref y, 110, 240);
        var breite = Ui.Zeile(d, "Breite (%)", new NumericUpDown { Minimum = 2, Maximum = 80, Value = (decimal)Math.Clamp(feld.Anteil * 100, 2, 80) }, ref y, 110, 70);
        var drucken = Ui.Zeile(d, "Drucken", new CheckBox { Checked = feld.Drucken }, ref y, 110);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var neu = text.Text.Replace("\\n", "\n");
        feld.Ueberschrift = neu == VertretungsplanDruck.StandardUeberschrift(schluessel, _lehrer) ? "" : neu;
        feld.Anteil = (float)breite.Value / 100f;
        feld.Drucken = drucken.Checked;
        EinstellungenLaden();
        Neuzeichnen();
    }
}

/// <summary>
/// Vorbild „Felder der Ansicht“: Knöpfe Ok / Übernahme / Abbrechen, darunter die Felder mit „drucken“ – Reihenfolge mit ▲/▼.
/// „Übernahme“ wendet die Auswahl an, ohne den Dialog zu schließen.
/// </summary>
public static class FelderDerAnsichtDialog
{
    public static void Zeigen(IWin32Window owner, List<DruckFeld> felder, Func<string, string> standardText, Action uebernommen)
    {
        using var d = Erstellen(felder, standardText, uebernommen, out _);
        d.ShowDialog(owner);
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, List<DruckFeld> felder, Func<string, string> standardText, Action uebernommen, out DataGridView raster)
    {
        var d = Erstellen(felder, standardText, uebernommen, out raster);
        d.Show(owner);
        return d;
    }

    private static Form Erstellen(List<DruckFeld> felder, Func<string, string> standardText, Action uebernommen, out DataGridView raster)
    {
        var d = new Form { Text = "Felder der Ansicht", Size = new Size(330, 470), FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.CenterParent, Font = Ui.Schrift, BackColor = Ui.BlauSehrHell, ShowInTaskbar = false };
        var ok = new Button { Text = "Ok", Left = 10, Top = 10, Width = 90, Height = 26, DialogResult = DialogResult.OK };
        var ueb = new Button { Text = "Übernahme", Left = 106, Top = 10, Width = 90, Height = 26 };
        var abbr = new Button { Text = "Abbrechen", Left = 202, Top = 10, Width = 90, Height = 26, DialogResult = DialogResult.Cancel };
        var g = new DataGridView
        {
            Left = 10, Top = 46, Width = 250, Height = 370, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToResizeRows = false, BackgroundColor = Color.White,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 22, RowTemplate = { Height = 20 },
        };
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feld", HeaderText = "Feld", ReadOnly = true, Width = 170, DefaultCellStyle = { Font = new Font(Ui.Schrift, FontStyle.Bold) } });
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "drucken", HeaderText = "drucken", Width = 60 });
        var arbeit = felder.Select(f => new DruckFeld { Schluessel = f.Schluessel, Ueberschrift = f.Ueberschrift, Drucken = f.Drucken, Anteil = f.Anteil }).ToList();
        void Fuellen()
        {
            g.Rows.Clear();
            foreach (var f in arbeit) g.Rows.Add((f.Ueberschrift.Length > 0 ? f.Ueberschrift : standardText(f.Schluessel)).Replace('\n', ' '), f.Drucken);
        }
        Fuellen();
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.CurrentCell is DataGridViewCheckBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        g.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 1) arbeit[e.RowIndex].Drucken = g.Rows[e.RowIndex].Cells[1].Value is true; };
        var hoch = new Button { Text = "▲", Left = 266, Top = 46, Width = 32, Height = 28 };
        var runter = new Button { Text = "▼", Left = 266, Top = 78, Width = 32, Height = 28 };
        void Schieben(int r)
        {
            var i = g.CurrentRow?.Index ?? -1;
            if (i < 0 || i + r < 0 || i + r >= arbeit.Count) return;
            (arbeit[i], arbeit[i + r]) = (arbeit[i + r], arbeit[i]);
            Fuellen();
            g.CurrentCell = g.Rows[i + r].Cells[0];
        }
        hoch.Click += (_, _) => Schieben(-1);
        runter.Click += (_, _) => Schieben(1);
        void Anwenden()
        {
            g.EndEdit();
            felder.Clear();
            felder.AddRange(arbeit.Select(f => new DruckFeld { Schluessel = f.Schluessel, Ueberschrift = f.Ueberschrift, Drucken = f.Drucken, Anteil = f.Anteil }));
            uebernommen();
        }
        ueb.Click += (_, _) => Anwenden();
        ok.Click += (_, _) => { Anwenden(); d.Close(); };
        abbr.Click += (_, _) => d.Close();
        d.AcceptButton = ok;
        d.CancelButton = abbr;
        d.Controls.AddRange(new Control[] { ok, ueb, abbr, g, hoch, runter });
        raster = g;
        return d;
    }
}
