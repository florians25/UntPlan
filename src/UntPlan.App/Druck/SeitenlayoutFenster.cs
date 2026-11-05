using UntPlan.App.Dialoge;
using UntPlan.Core;

namespace UntPlan.App.Druck;

/// <summary>
/// Seitenlayout des Vertretungsplans wie im Handbuch: oben die Symbolleiste (Spalten ein- und ausblenden, Drucken / Seitenvorschau,
/// Fett / Zeilenumbruch, Wechsel Kurzname / Langname, Schriftschnitt der Überschriftszeile, Schrift, Hoch- / Querformat), links die
/// Seitenvorschau (WYSIWYG, Seiten blättern, Zoom, Stifte für Listen- und Spaltenüberschriften, Spalten per Drag&amp;Drop anordnen bzw.
/// ein- und ausblenden), rechts die Einstellungen – Auswahl, „Druck nur, wenn geändert nach“, Erste Beschriftungsspalte auf jeder Seite,
/// Raster, Beschränkung auf Seitenbreite, Felder, Hintergrund-Bild sowie die Karten „Aktuelle Ansicht“ (Auswahl, Form, Gliederung,
/// Absenzkopf, Überschrift) und „Global“ (Absenzkopf, Tagestexte). Jede Änderung ist sofort in der Vorschau zu sehen.
/// </summary>
public sealed class SeitenlayoutFenster : Form
{
    private readonly DateOnly _von, _bis;
    private bool _lehrer;
    private readonly PictureBox _seite = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AllowDrop = true };
    private readonly Panel _vorschauRahmen = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(160, 166, 176) };
    private readonly ToolStripLabel _seitenText = new("1-1 / 1");
    private readonly ToolStripComboBox _zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ToolStripComboBox _schrift = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ToolStripComboBox _groesse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 };
    private readonly ToolStripButton _fett = new() { ToolTipText = "Fett", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
    private readonly ToolStripButton _umbruch = new() { ToolTipText = "Zeilenumbruch", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
    private readonly ToolStripButton _langname = new() { ToolTipText = "Wechsel Kurzname / Langname", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
    private readonly ToolStripButton[] _schnitt = new ToolStripButton[3];
    private readonly ToolStripDropDownButton _format = new() { ToolTipText = "Hochformat / Querformat", DisplayStyle = ToolStripItemDisplayStyle.Image };
    private readonly Label _auswahlText = new() { AutoSize = false };
    private readonly ListBox _felder = new() { IntegralHeight = false, BorderStyle = BorderStyle.None, AllowDrop = true };
    private readonly Label _bildText = new() { Text = "<nicht definiert>", AutoSize = false, ForeColor = Color.DimGray, TextAlign = ContentAlignment.BottomCenter, AutoEllipsis = true };
    private readonly ComboBox _gruppierung = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<Button> _stifte = new();
    private List<Bitmap> _bilder = new();
    private List<(string Schluessel, RectangleF Kopf)> _spalten = new();
    private RectangleF _titel;
    private float _faktor = 1f;
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

        // Symbolleiste wie im Handbuch: Spalten ein-/ausblenden | Drucken, Seitenvorschau | Fett, Zeilenumbruch, Kurz-/Langname |
        // Schriftschnitt der Überschriftszeile | Schrift und Größe | Hoch-/Querformat
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
        ts.Items.Add(new ToolStripButton(null, Glyphe("▤", 0, false, Color.FromArgb(40, 110, 200)), (_, _) => FelderDialog()) { ToolTipText = "Spalten ein- und ausblenden", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton(null, Symbole.Get("drucken", 16), (_, _) => VertretungsplanDruck.Ausgeben(this, _von, _bis, _lehrer, false)) { ToolTipText = "Drucken", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripButton(null, Symbole.Get("vorschau", 16), (_, _) => VertretungsplanDruck.Ausgeben(this, _von, _bis, _lehrer, true)) { ToolTipText = "Seitenvorschau", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripButton("HTML-Ausg.", Symbole.Get("html", 16), (_, _) => HtmlAusgabe()) { ToolTipText = "Vertretungsliste im HTML-Format in einem frei wählbaren Ordner speichern" });
        ts.Items.Add(new ToolStripSeparator());
        _fett.Image = Glyphe("B", 0, true, Color.Black);
        _umbruch.Image = Glyphe("↵", 0, true, Color.FromArgb(40, 110, 200));
        _langname.Image = Glyphe("ab", 0, false, Color.Black, "↔");
        _fett.CheckedChanged += (_, _) => Setzen(() => E.TabelleFett = _fett.Checked);
        _umbruch.CheckedChanged += (_, _) => Setzen(() => E.Zeilenumbruch = _umbruch.Checked);
        _langname.CheckedChanged += (_, _) => Setzen(() => E.Langname = _langname.Checked);
        ts.Items.AddRange(new ToolStripItem[] { _fett, _umbruch, _langname, new ToolStripSeparator() });
        var schnittTexte = new[] { "Überschriftszeile waagrecht", "Überschriftszeile schräg", "Überschriftszeile senkrecht" };
        for (var i = 0; i < 3; i++)
        {
            var nr = i;
            _schnitt[i] = new ToolStripButton(null, Glyphe("abc", i == 0 ? 0 : i == 1 ? -45 : -90, false, Color.Black)) { ToolTipText = "Schriftschnitt der " + schnittTexte[i], DisplayStyle = ToolStripItemDisplayStyle.Image };
            _schnitt[i].Click += (_, _) => { E.UeberschriftSchnitt = nr; EinstellungenLaden(); Neuzeichnen(); };
            ts.Items.Add(_schnitt[i]);
        }
        ts.Items.Add(new ToolStripSeparator());
        _schrift.Items.AddRange(FontFamily.Families.Select(f => (object)f.Name).ToArray());
        _groesse.Items.AddRange(new object[] { "8", "9", "10", "11", "12", "14", "16" });
        _schrift.ToolTipText = _groesse.ToolTipText = "Einstellungen zur Schrift";
        ts.Items.Add(_schrift);
        ts.Items.Add(_groesse);
        _format.DropDownItems.Add("Hochformat", Glyphe("▯", 0, false, Color.Black), (_, _) => { E.Querformat = false; EinstellungenLaden(); Neuzeichnen(); });
        _format.DropDownItems.Add("Querformat", Glyphe("▭", 0, false, Color.Black), (_, _) => { E.Querformat = true; EinstellungenLaden(); Neuzeichnen(); });
        ts.Items.Add(_format);
        ts.Items.Add(new ToolStripSeparator());
        var version = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, ToolTipText = "Klassen- oder Lehrerversion" };
        version.Items.AddRange(new object[] { "Klassen", "Lehrer" });
        version.SelectedIndex = lehrer ? 1 : 0;
        version.SelectedIndexChanged += (_, _) => { _lehrer = version.SelectedIndex == 1; EinstellungenLaden(); Neuzeichnen(); };
        ts.Items.Add(new ToolStripLabel("Version:"));
        ts.Items.Add(version);
        _schrift.SelectedIndexChanged += (_, _) => { if (!_laden && _schrift.SelectedItem is string s) { E.Schriftart = s; Neuzeichnen(); } };
        _groesse.SelectedIndexChanged += (_, _) => { if (!_laden && float.TryParse(_groesse.SelectedItem as string, out var g)) { E.Schriftgroesse = g; Neuzeichnen(); } };

        // über der Vorschau: Seiten blättern und Zoom
        var blatt = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(4, 1, 4, 1) };
        blatt.Items.Add(new ToolStripButton("|◀", null, (_, _) => Blaettern(-1000)) { ToolTipText = "Erste Seite" });
        blatt.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(false), (_, _) => Blaettern(-1)) { ToolTipText = "Vorherige Seite", DisplayStyle = ToolStripItemDisplayStyle.Image });
        blatt.Items.Add(_seitenText);
        blatt.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(true), (_, _) => Blaettern(1)) { ToolTipText = "Nächste Seite", DisplayStyle = ToolStripItemDisplayStyle.Image });
        blatt.Items.Add(new ToolStripButton("▶|", null, (_, _) => Blaettern(1000)) { ToolTipText = "Letzte Seite" });
        blatt.Items.Add(new ToolStripSeparator());
        _zoom.Items.AddRange(new object[] { "50 %", "75 %", "100 %", "125 %", "Ganze Seite" });
        _zoom.SelectedIndex = 4;
        _zoom.SelectedIndexChanged += (_, _) => Anordnen();
        blatt.Items.Add(_zoom);

        _vorschauRahmen.Controls.Add(_seite);
        _vorschauRahmen.Resize += (_, _) => Anordnen();
        SpaltenZiehenAnbinden();
        var links = new Panel { Dock = DockStyle.Fill };
        links.Controls.Add(_vorschauRahmen);
        links.Controls.Add(blatt);
        var rechts = new Panel { Dock = DockStyle.Right, Width = 452, AutoScroll = true, Padding = new Padding(8) };
        RechtsAufbauen(rechts);
        Controls.Add(links);
        Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 4 });
        Controls.Add(rechts);
        Controls.Add(ts);
        EinstellungenLaden();
        Shown += (_, _) => Neuzeichnen();
        FormClosed += (_, _) => { AppZustand.Einstellungen.Speichern(); foreach (var b in _bilder) b.Dispose(); };
        if (!IsHandleCreated) Neuzeichnen();
    }

    /// <summary>Kleines Symbol aus Text (gedreht um <paramref name="winkel"/> Grad) für die Symbolleiste.</summary>
    private static Bitmap Glyphe(string text, float winkel, bool fett, Color farbe, string? zusatz = null)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font("Segoe UI", text.Length > 2 ? 6.5f : zusatz is null ? 9f : 7f, fett ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        using var b = new SolidBrush(farbe);
        using var mitte = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TranslateTransform(8, zusatz is null ? 8 : 5);
        g.RotateTransform(winkel);
        g.DrawString(text, f, b, new RectangleF(-10, -8, 20, 16), mitte);
        g.ResetTransform();
        if (zusatz != null)
        {
            using var z = new Font("Segoe UI Symbol", 7f);
            using var blau = new SolidBrush(Color.FromArgb(40, 110, 200));
            g.DrawString(zusatz, z, blau, new RectangleF(0, 7, 16, 9), mitte);
        }
        return bmp;
    }

    private void Setzen(Action a)
    {
        if (_laden) return;
        a();
        Neuzeichnen();
    }

    private void RechtsAufbauen(Panel p)
    {
        var y = 8;
        void Add(Control c, int x, int w, int h = 22) { c.SetBounds(x, y, w, h); p.Controls.Add(c); }

        // Auswahl der Elemente (Lehrer: 10/10) und „Druck nur, wenn geändert nach“
        Add(_auswahlText, 8, 150, 18);
        var gGeaendert = new GroupBox { Text = "Druck nur, wenn geändert nach", Left = 170, Top = y, Width = 262, Height = 56 };
        p.Controls.Add(gGeaendert);
        y += 22;
        var auswahl = new Button { Text = "Auswahl" };
        auswahl.Click += (_, _) => AuswahlDialog();
        Add(auswahl, 8, 150, 26);
        var ab = E.NurGeaendertNach ?? DateTime.Today.AddHours(8);
        var datum = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Value = ab.Date, Left = 8, Top = 22, Width = 104 };
        datum.Checked = E.NurGeaendertNach.HasValue; // nach Value setzen (Value setzt das Häkchen)
        var zeit = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Value = ab, Left = 116, Top = 22, Width = 72 };
        var heute = new Button { Text = "Heute", Left = 192, Top = 21, Width = 62, Height = 24 };
        gGeaendert.Controls.AddRange(new Control[] { datum, zeit, heute });
        void Geaendert()
        {
            if (_laden) return;
            E.NurGeaendertNach = datum.Checked ? datum.Value.Date + zeit.Value.TimeOfDay : null;
            Neuzeichnen();
        }
        datum.ValueChanged += (_, _) => Geaendert();
        zeit.ValueChanged += (_, _) => Geaendert();
        heute.Click += (_, _) => { datum.Value = DateTime.Today; datum.Checked = true; Geaendert(); };
        y += 38;
        Add(Haken("Erste Beschriftungsspalte auf jeder Seite", () => E.ErsteBeschriftungJedeSeite, v => E.ErsteBeschriftungJedeSeite = v), 8, 300, 20);
        y += 20;
        Add(Haken("Raster", () => E.Raster, v => E.Raster = v), 8, 300, 20);
        y += 20;
        Add(Haken("Beschränkung auf Seitenbreite", () => E.BeschraenkungSeitenbreite, v => E.BeschraenkungSeitenbreite = v), 8, 300, 20);
        y += 26;

        // Felder (nicht gedruckte Felder – per Drag&Drop in die Vorschau einblenden) und Hintergrund-Bild
        var gFelder = new GroupBox { Text = "Felder", Left = 8, Top = y, Width = 296, Height = 150 };
        _felder.SetBounds(8, 18, 280, 124);
        _felder.BackColor = gFelder.BackColor = Ui.BlauSehrHell;
        gFelder.Controls.Add(_felder);
        p.Controls.Add(gFelder);
        new ToolTip().SetToolTip(_felder, "Nicht gedruckte Felder – mit der Maus in die Vorschau ziehen (oder Doppelklick), um sie einzublenden;\nSpalten aus der Vorschau hierher ziehen, um sie auszublenden.");
        FelderZiehenAnbinden();
        var gBild = new GroupBox { Text = "Hintergrund-Bild", Left = 310, Top = y, Width = 122, Height = 150 };
        p.Controls.Add(gBild);
        var bildWahl = new Button { Image = Symbole.Get("oeffnen", 16), Left = 22, Top = 26, Width = 34, Height = 28 };
        var bildWeg = new Button { Image = Symbole.Get("loeschen", 16), Left = 62, Top = 26, Width = 34, Height = 28 };
        _bildText.SetBounds(4, 100, 114, 44);
        gBild.Controls.AddRange(new Control[] { bildWahl, bildWeg, _bildText });
        new ToolTip().SetToolTip(bildWahl, "Hintergrund-Bild wählen");
        new ToolTip().SetToolTip(bildWeg, "Hintergrund-Bild entfernen");
        bildWahl.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Alle Dateien|*.*", Title = "Hintergrund-Bild" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            E.Hintergrundbild = dlg.FileName;
            EinstellungenLaden();
            Neuzeichnen();
        };
        bildWeg.Click += (_, _) => { E.Hintergrundbild = ""; EinstellungenLaden(); Neuzeichnen(); };
        y += 158;

        // Karten „Aktuelle Ansicht“ und „Global“
        var tabs = new TabControl();
        Add(tabs, 8, 426, 372);
        var ansicht = new TabPage("Aktuelle Ansicht") { BackColor = Ui.BlauSehrHell };
        var global = new TabPage("Global") { BackColor = Ui.BlauSehrHell };
        tabs.TabPages.Add(ansicht);
        tabs.TabPages.Add(global);

        var gAuswahl = Gruppe(ansicht, "Auswahl", 4, 4, 204, 84);
        Haken(gAuswahl, "Nur neue Vertretungen", () => E.NurNeue, v => E.NurNeue = v, 18, 194);
        Haken(gAuswahl, "Abwesende Lehrer/Klassen nicht drucken", () => E.AbwesendeNichtDrucken, v => E.AbwesendeNichtDrucken = v, 38, 194);
        Haken(gAuswahl, "Auch Elemente ohne Vertretung", () => E.AuchElementeOhneVertretung, v => E.AuchElementeOhneVertretung = v, 58, 194);

        var gGliederung = Gruppe(ansicht, "Gliederung", 4, 92, 204, 212);
        var proElement = Haken(gGliederung, "Überschrift / Element", () => E.UeberschriftJeElement, v => E.UeberschriftJeElement = v, 18);
        var eineSeite = Haken(gGliederung, "1 Seite / Element", () => E.EineSeiteJeElement, v => E.EineSeiteJeElement = v, 40, 170, 26);
        eineSeite.Enabled = proElement.Checked;
        proElement.CheckedChanged += (_, _) => eineSeite.Enabled = proElement.Checked;
        Haken(gGliederung, "Elementüberschrift komprimiert", () => E.ElementueberschriftKomprimiert, v => E.ElementueberschriftKomprimiert = v, 64);
        var gGruppierung = new GroupBox { Text = "Gruppierung (=Sortierkriterium 1)", Left = 6, Top = 90, Width = 192, Height = 54 };
        _gruppierung.SetBounds(8, 20, 176, 23);
        _gruppierung.SelectedIndexChanged += (_, _) =>
        {
            if (_laden || _gruppierung.SelectedIndex < 0) return;
            E.Gruppierung = VertretungsplanDruck.Sortierfelder(_lehrer)[_gruppierung.SelectedIndex].Schluessel;
            Neuzeichnen();
        };
        gGruppierung.Controls.Add(_gruppierung);
        gGliederung.Controls.Add(gGruppierung);
        var sortierung = new Button { Text = "Sortierung", Left = 6, Top = 152, Width = 192, Height = 28 };
        sortierung.Click += (_, _) => SortierungDialog();
        gGliederung.Controls.Add(sortierung);

        var gForm = Gruppe(ansicht, "Form", 214, 4, 200, 156);
        Haken(gForm, "Entfallslehrer durchstreichen", () => E.EntfallslehrerDurchstreichen, v => E.EntfallslehrerDurchstreichen = v, 18, 190);
        Haken(gForm, "Komb. Klassen (2a,2b->2ab)", () => E.KombinierteKlassen, v => E.KombinierteKlassen = v, 40, 190);
        Haken(gForm, "Stundenbezeichnung", () => E.Stundenbezeichnung, v => E.Stundenbezeichnung = v, 62, 190);
        Haken(gForm, "Stammdaten in einzelnem Feld", () => E.StammdatenEinzeln, v => E.StammdatenEinzeln = v, 84, 190);
        Haken(gForm, "Blockvertretungen zusammenfassen", () => E.BlockvertretungenZusammenfassen, v => E.BlockvertretungenZusammenfassen = v, 106, 190);
        Haken(gForm, "Kein Entfall bei Ersatz in Stunde", () => E.KeinEntfallBeiErsatz, v => E.KeinEntfallBeiErsatz = v, 128, 190);

        // Absenzkopf: Spalten „Absenz“ und „Vertretung“
        var gAbs = Gruppe(ansicht, "Absenzkopf", 214, 164, 200, 118);
        gAbs.Controls.Add(new Label { Text = "Absenz", Left = 70, Top = 14, Width = 50, Height = 16 });
        gAbs.Controls.Add(new Label { Text = "Vertretung", Left = 136, Top = 14, Width = 62, Height = 16 });
        gAbs.Controls.Add(new Label { Text = "Lehrer", Left = 8, Top = 34, Width = 60, Height = 16 });
        gAbs.Controls.Add(new Label { Text = "Klassen", Left = 8, Top = 74, Width = 60, Height = 16 });
        gAbs.Controls.Add(new Label { Text = "Räume", Left = 8, Top = 94, Width = 60, Height = 16 });
        Haken(gAbs, "", () => E.AbwesendeLehrer, v => E.AbwesendeLehrer = v, 32, 18, 82);
        Haken(gAbs, "", () => E.VertretungLehrer, v => E.VertretungLehrer = v, 32, 18, 156);
        Haken(gAbs, "mit Absenzgrund", () => E.LehrerMitAbsenzgrund, v => E.LehrerMitAbsenzgrund = v, 52, 116, 82);
        Haken(gAbs, "", () => E.AbwesendeKlassen, v => E.AbwesendeKlassen = v, 72, 18, 82);
        Haken(gAbs, "", () => E.VertretungKlassen, v => E.VertretungKlassen = v, 72, 18, 156);
        Haken(gAbs, "", () => E.BlockierteRaeume, v => E.BlockierteRaeume = v, 92, 18, 82);
        Haken(gAbs, "", () => E.VertretungRaeume, v => E.VertretungRaeume = v, 92, 18, 156);

        var ueberschrift = new Button { Text = "Überschrift", Left = 314, Top = 290, Width = 100, Height = 28 };
        ueberschrift.Click += (_, _) => UeberschriftsfelderDialog();
        ansicht.Controls.Add(ueberschrift);

        // Global: Absenzkopf und Tagestexte (Schriftgröße in %, Rahmen), Langnamen im Absenzkopf
        var gAbsG = Gruppe(global, "Absenzkopf", 4, 4, 204, 280);
        Regler(gAbsG, () => E.AbsenzkopfGroesse, v => E.AbsenzkopfGroesse = v);
        Haken(gAbsG, "Ausdruck mit Rahmen", () => E.AbsenzkopfRahmen, v => E.AbsenzkopfRahmen = v, 96, 194);
        Haken(gAbsG, "Absenzkopf nicht für Veranstaltungen", () => E.AbsenzkopfNichtVeranstaltungen, v => E.AbsenzkopfNichtVeranstaltungen = v, 118, 194);
        var gLang = new GroupBox { Text = "Langname", Left = 6, Top = 146, Width = 192, Height = 100 };
        gAbsG.Controls.Add(gLang);
        Haken(gLang, "Lehrer", () => E.AbsenzkopfLangLehrer, v => E.AbsenzkopfLangLehrer = v, 20);
        Haken(gLang, "Klassen", () => E.AbsenzkopfLangKlassen, v => E.AbsenzkopfLangKlassen = v, 44);
        Haken(gLang, "Räume", () => E.AbsenzkopfLangRaeume, v => E.AbsenzkopfLangRaeume = v, 68);
        var gTag = Gruppe(global, "Tagestexte", 214, 4, 200, 150);
        Regler(gTag, () => E.TagestexteGroesse, v => E.TagestexteGroesse = v);
        Haken(gTag, "Ausdruck mit Rahmen", () => E.TagestexteRahmen, v => E.TagestexteRahmen = v, 96, 190);
        Haken(gTag, "Tagestexte drucken", () => E.Nachrichten, v => E.Nachrichten = v, 118, 190);
    }

    /// <summary>Schieberegler „Schriftgröße in %“ (25–200 %).</summary>
    private void Regler(GroupBox g, Func<int> lesen, Action<int> schreiben)
    {
        var wert = new Label { Text = lesen().ToString(), Left = 8, Top = 18, Width = 50, Height = 16 };
        g.Controls.Add(wert);
        g.Controls.Add(new Label { Text = "Schriftgröße in %", Left = g.Width - 118, Top = 18, Width = 110, Height = 16, TextAlign = ContentAlignment.TopRight });
        var tb = new TrackBar { Minimum = 25, Maximum = 200, SmallChange = 5, LargeChange = 25, TickFrequency = 25, Value = Math.Clamp(lesen(), 25, 200), Left = 6, Top = 38, Width = g.Width - 14, Height = 40 };
        tb.ValueChanged += (_, _) => { wert.Text = tb.Value.ToString(); if (!_laden) { schreiben(tb.Value); Neuzeichnen(); } };
        g.Controls.Add(tb);
    }

    private static GroupBox Gruppe(Control parent, string titel, int x, int y, int w, int h)
    {
        var g = new GroupBox { Text = titel, Left = x, Top = y, Width = w, Height = h };
        parent.Controls.Add(g);
        return g;
    }

    private readonly List<(CheckBox Box, Func<bool> Lesen)> _haken = new();

    private CheckBox Haken(string text, Func<bool> lesen, Action<bool> schreiben)
    {
        var c = new CheckBox { Text = text, Checked = lesen() };
        c.CheckedChanged += (_, _) => { if (_laden) return; schreiben(c.Checked); Neuzeichnen(); };
        _haken.Add((c, lesen));
        return c;
    }

    private CheckBox Haken(GroupBox g, string text, Func<bool> lesen, Action<bool> schreiben, int y, int w = 186, int x = 8)
    {
        var c = Haken(text, lesen, schreiben);
        c.SetBounds(x, y, w, 20);
        g.Controls.Add(c);
        return c;
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
            _fett.Checked = E.TabelleFett;
            _umbruch.Checked = E.Zeilenumbruch;
            _langname.Checked = E.Langname;
            for (var i = 0; i < _schnitt.Length; i++) _schnitt[i].Checked = E.UeberschriftSchnitt == i;
            _format.Image = Glyphe(E.Querformat ? "▭" : "▯", 0, false, Color.Black);
            foreach (ToolStripMenuItem m in _format.DropDownItems) m.Checked = (m.Text == "Querformat") == E.Querformat;
            foreach (var (box, lesen) in _haken) box.Checked = lesen();
            _felder.Items.Clear();
            foreach (var f in VertretungsplanDruck.Felder(E).Where(f => !f.Drucken))
                _felder.Items.Add(new FeldEintrag(f.Schluessel, VertretungsplanDruck.Ueberschrift(f, _lehrer).Replace('\n', ' ')));
            _bildText.Text = E.Hintergrundbild.Length > 0 ? E.Hintergrundbild : "<nicht definiert>";
            var felder = VertretungsplanDruck.Sortierfelder(_lehrer);
            _gruppierung.Items.Clear();
            _gruppierung.Items.AddRange(felder.Select(x => (object)x.Text).ToArray());
            _gruppierung.SelectedIndex = Math.Max(0, Array.FindIndex(felder, x => x.Schluessel == E.Gruppierung));
            var d = AppZustand.D;
            var auswahl = _lehrer ? E.LehrerAuswahl : E.KlassenAuswahl;
            var alle = _lehrer ? d.Lehrer.Count : d.Klassen.Count;
            _auswahlText.Text = $"{(_lehrer ? "Lehrer" : "Klasse(n)")}: {(auswahl.Count == 0 ? alle : auswahl.Count)}/{alle}";
        }
        finally { _laden = false; }
    }

    private sealed record FeldEintrag(string Schluessel, string Text)
    {
        public override string ToString() => Text;
    }

    private void FelderDialog() =>
        FelderDerAnsichtDialog.Zeigen(this, VertretungsplanDruck.Felder(E), k => VertretungsplanDruck.StandardUeberschrift(k, _lehrer), () => { EinstellungenLaden(); Neuzeichnen(); });

    // ---------------- Drag&Drop: Spalten ein- und ausblenden, Anordnung ändern ----------------

    private const string FeldFormat = "feld:", SpalteFormat = "spalte:";

    private void FelderZiehenAnbinden()
    {
        var start = Point.Empty;
        _felder.MouseDown += (_, e) => start = e.Location;
        _felder.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 5) return;
            var i = _felder.IndexFromPoint(start);
            if (i >= 0 && _felder.Items[i] is FeldEintrag f) _felder.DoDragDrop(FeldFormat + f.Schluessel, DragDropEffects.Move);
        };
        _felder.DoubleClick += (_, _) => { if (_felder.SelectedItem is FeldEintrag f) SpalteEinfuegen(f.Schluessel, null); };
        // Spalte aus der Vorschau hierher ziehen = ausblenden
        _felder.DragOver += (_, e) => e.Effect = Gezogen(e, SpalteFormat) is null ? DragDropEffects.None : DragDropEffects.Move;
        _felder.DragDrop += (_, e) =>
        {
            if (Gezogen(e, SpalteFormat) is not { } k) return;
            var f = VertretungsplanDruck.Felder(E).First(x => x.Schluessel == k);
            if (VertretungsplanDruck.Felder(E).Count(x => x.Drucken) <= 1) return;
            f.Drucken = false;
            EinstellungenLaden();
            Neuzeichnen();
        };
    }

    private static string? Gezogen(DragEventArgs e, string praefix) =>
        e.Data?.GetData(DataFormats.StringFormat) is string s && s.StartsWith(praefix, StringComparison.Ordinal) ? s[praefix.Length..] : null;

    // ---------------- Ziehen mit der Maus: Seitenrand, Spaltenbreite, Zeilenhöhe ----------------

    /// <summary>Art des Anfassers im Seitenlayout.</summary>
    private enum Anfasser { Keiner, RandLinks, RandRechts, Spalte, Zeile }

    private VertretungsplanDruck.Seitengeometrie _geo;
    private (Anfasser Art, int Index) _zug;
    private Point _zugStart;
    private int _zugPos;

    /// <summary>Anfasser unter dem Mauszeiger (nur auf der ersten Seite, deren Lage bekannt ist).</summary>
    private (Anfasser Art, int Index) AnfasserBei(Point p)
    {
        if (_seiteNr != 0 || _geo.Spaltengrenzen is null || _geo.Bereich.Width == 0) return (Anfasser.Keiner, 0);
        const float Toleranz = 4f;
        float X(float v) => v * _faktor;
        var zeile = _geo.ErsteZeile;
        // blaue Linie unter der ersten Tabellenzeile: Zeilenhöhe
        if (zeile != RectangleF.Empty && Math.Abs(p.Y - X(zeile.Bottom)) <= Toleranz && p.X >= X(zeile.Left) && p.X <= X(zeile.Right)) return (Anfasser.Zeile, 0);
        // Spaltengrenzen im Bereich der Spaltenköpfe und der Tabelle
        var oben = _spalten.Count > 0 ? _spalten.Min(s => s.Kopf.Top) : zeile.Top;
        var unten = zeile != RectangleF.Empty ? zeile.Bottom : _spalten.Count > 0 ? _spalten.Max(s => s.Kopf.Bottom) : 0;
        for (var i = 1; i < _geo.Spaltengrenzen.Length - 1; i++)
            if (Math.Abs(p.X - X(_geo.Spaltengrenzen[i])) <= Toleranz && p.Y >= X(oben) && p.Y <= X(unten) + 4) return (Anfasser.Spalte, i);
        // Seitenränder: senkrechte Linien links und rechts über die ganze Seite
        if (Math.Abs(p.X - X(_geo.Bereich.Left)) <= Toleranz) return (Anfasser.RandLinks, 0);
        if (Math.Abs(p.X - X(_geo.Bereich.Right)) <= Toleranz) return (Anfasser.RandRechts, 0);
        return (Anfasser.Keiner, 0);
    }

    /// <summary>Blaue Hilfslinien wie im Handbuch: Zeilenhöhe (erste Tabellenzeile), Seitenränder; beim Ziehen die neue Lage.</summary>
    private void HilfslinienZeichnen(object? sender, PaintEventArgs e)
    {
        if (_seiteNr != 0 || _geo.Bereich.Width == 0) return;
        float X(float v) => v * _faktor;
        using var blau = new Pen(Color.FromArgb(60, 90, 230)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };
        using var rand = new Pen(Color.FromArgb(150, 60, 90, 230)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var g = e.Graphics;
        if (_geo.ErsteZeile != RectangleF.Empty)
        {
            g.DrawLine(blau, 0, X(_geo.ErsteZeile.Top), _seite.Width, X(_geo.ErsteZeile.Top));
            g.DrawLine(blau, 0, X(_geo.ErsteZeile.Bottom), _seite.Width, X(_geo.ErsteZeile.Bottom));
        }
        g.DrawLine(rand, X(_geo.Bereich.Left), 0, X(_geo.Bereich.Left), _seite.Height);
        g.DrawLine(rand, X(_geo.Bereich.Right), 0, X(_geo.Bereich.Right), _seite.Height);
        if (_zug.Art == Anfasser.Keiner) return;
        using var zug = new Pen(Color.FromArgb(220, 40, 40), 1.5f);
        if (_zug.Art == Anfasser.Zeile) g.DrawLine(zug, 0, _zugPos, _seite.Width, _zugPos);
        else g.DrawLine(zug, _zugPos, 0, _zugPos, _seite.Height);
    }

    /// <summary>Gezogene Linie übernehmen: Seitenrand, Breite der beiden angrenzenden Spalten bzw. Zeilenhöhe.</summary>
    private void ZugAnwenden(Point ende)
    {
        var art = _zug;
        _zug = (Anfasser.Keiner, 0);
        ZugAnwenden(art, (ende.X - _zugStart.X) / Math.Max(0.05f, _faktor), (ende.Y - _zugStart.Y) / Math.Max(0.05f, _faktor));
    }

    /// <summary>Verschiebung um <paramref name="dx"/>/<paramref name="dy"/> Bildpunkte der Seite (100 dpi) übernehmen.</summary>
    private void ZugAnwenden((Anfasser Art, int Index) art, float dx, float dy)
    {
        switch (art.Art)
        {
            case Anfasser.RandLinks: E.RandLinks = Math.Clamp((int)Math.Round(E.RandLinks + dx), 10, 300); break;
            case Anfasser.RandRechts: E.RandRechts = Math.Clamp((int)Math.Round(E.RandRechts - dx), 10, 300); break;
            case Anfasser.Zeile:
                E.ZeilenAbstand = Math.Clamp((int)Math.Round(E.ZeilenAbstand + dy), 0, 60);
                break;
            case Anfasser.Spalte:
                var gedruckt = VertretungsplanDruck.Felder(E).Where(f => f.Drucken).ToList();
                var i = art.Index;
                if (i <= 0 || i >= gedruckt.Count) break;
                var summe = gedruckt.Sum(f => Math.Max(0.02f, f.Anteil));
                var anteil = dx / Math.Max(1, _geo.Bereich.Width) * summe;
                var links = gedruckt[i - 1];
                var rechts = gedruckt[i];
                anteil = Math.Clamp(anteil, 0.02f - Math.Max(0.02f, links.Anteil), Math.Max(0.02f, rechts.Anteil) - 0.02f);
                links.Anteil = Math.Max(0.02f, links.Anteil) + anteil;
                rechts.Anteil = Math.Max(0.02f, rechts.Anteil) - anteil;
                break;
            default: return;
        }
        Neuzeichnen();
    }

    /// <summary>Selbsttest: Anfasser ziehen (Bildpunkte der Vorschau bei Zoom 100 %).</summary>
    internal void SelbsttestZiehen(string art, int index, int dx, int dy)
    {
        ZugAnwenden((Enum.Parse<Anfasser>(art), index), dx, dy);
    }

    private void SpaltenZiehenAnbinden()
    {
        string? gefasst = null;
        var start = Point.Empty;
        _seite.Paint += HilfslinienZeichnen;
        _seite.MouseDown += (_, e) =>
        {
            start = e.Location;
            gefasst = null;
            if (e.Button == MouseButtons.Left && AnfasserBei(e.Location) is { Art: not Anfasser.Keiner } a)
            {
                _zug = a;
                _zugStart = e.Location;
                _zugPos = a.Art == Anfasser.Zeile ? e.Y : e.X;
                _seite.Capture = true;
                return;
            }
            gefasst = _spalten.Where(s => Skaliert(s.Kopf).Contains(e.Location)).Select(s => s.Schluessel).FirstOrDefault();
        };
        _seite.MouseUp += (_, e) =>
        {
            if (_zug.Art == Anfasser.Keiner) return;
            _seite.Capture = false;
            ZugAnwenden(e.Location);
            _seite.Invalidate();
        };
        _seite.MouseMove += (_, e) =>
        {
            if (_zug.Art != Anfasser.Keiner)
            {
                _zugPos = _zug.Art == Anfasser.Zeile ? e.Y : e.X;
                _seite.Invalidate();
                return;
            }
            var anfasser = AnfasserBei(e.Location).Art;
            if (anfasser != Anfasser.Keiner && e.Button == MouseButtons.None)
            {
                _seite.Cursor = anfasser == Anfasser.Zeile ? Cursors.HSplit : Cursors.VSplit;
                return;
            }
            var ueberKopf = _spalten.Any(s => Skaliert(s.Kopf).Contains(e.Location));
            _seite.Cursor = ueberKopf ? Cursors.SizeAll : _titel != RectangleF.Empty && Skaliert(_titel).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (gefasst is null || e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 6) return;
            var k = gefasst;
            gefasst = null;
            _seite.DoDragDrop(SpalteFormat + k, DragDropEffects.Move);
        };
        _seite.MouseUp += (_, _) => gefasst = null;
        var tipp = new ToolTip();
        tipp.SetToolTip(_seite, "Blaue Linie ziehen: Zeilenhöhe · Linie zwischen den Spalten ziehen: Spaltenbreite · gestrichelte Linie links/rechts ziehen: Seitenrand\nSpaltenkopf ziehen: Anordnung ändern bzw. ausblenden");
        _seite.DragOver += (_, e) => e.Effect = Gezogen(e, FeldFormat) is not null || Gezogen(e, SpalteFormat) is not null ? DragDropEffects.Move : DragDropEffects.None;
        _seite.DragDrop += (_, e) =>
        {
            var p = _seite.PointToClient(new Point(e.X, e.Y));
            var ziel = _spalten.Select(s => (s.Schluessel, R: Skaliert(s.Kopf))).FirstOrDefault(s => p.X < s.R.Left + s.R.Width / 2).Schluessel;
            if ((Gezogen(e, FeldFormat) ?? Gezogen(e, SpalteFormat)) is { } k && k != ziel) SpalteEinfuegen(k, ziel);
        };
        _seite.DoubleClick += (_, _) =>
        {
            var p = _seite.PointToClient(Cursor.Position);
            if (_titel != RectangleF.Empty && Skaliert(_titel).Contains(p)) ListenueberschriftAendern();
        };
    }

    private RectangleF Skaliert(RectangleF r) => new(r.X * _faktor, r.Y * _faktor, r.Width * _faktor, r.Height * _faktor);

    /// <summary>Feld (wieder) drucken und vor <paramref name="vor"/> einordnen (null = ans Ende der gedruckten Felder).</summary>
    private void SpalteEinfuegen(string schluessel, string? vor)
    {
        var liste = VertretungsplanDruck.Felder(E);
        var feld = liste.First(x => x.Schluessel == schluessel);
        liste.Remove(feld);
        var i = vor is null ? -1 : liste.FindIndex(x => x.Schluessel == vor);
        if (i < 0) i = liste.FindLastIndex(x => x.Drucken) + 1;
        liste.Insert(i, feld);
        feld.Drucken = true;
        EinstellungenLaden();
        Neuzeichnen();
    }

    // ---------------- Dialoge ----------------

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

    /// <summary>„Sortierkriterien“: Sortieren nach (= Gruppierung) und dann nach, je auf- oder absteigend.</summary>
    private void SortierungDialog()
    {
        var felder = VertretungsplanDruck.Sortierfelder(_lehrer);
        using var d = new Dialog("Sortierkriterien", 330, 210);
        (ComboBox Cb, RadioButton Ab) Block(string titel, int y, string wert, bool absteigend, bool leerErlaubt)
        {
            var g = new GroupBox { Text = titel, Left = 10, Top = y, Width = 310, Height = 72 };
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 20, Width = 290 };
            if (leerErlaubt) cb.Items.Add("");
            cb.Items.AddRange(felder.Select(x => (object)x.Text).ToArray());
            var i = Array.FindIndex(felder, x => x.Schluessel == wert);
            cb.SelectedIndex = i < 0 ? 0 : i + (leerErlaubt ? 1 : 0);
            var auf = new RadioButton { Text = "Aufsteigend", Left = 10, Top = 46, Width = 120, Checked = !absteigend };
            var ab = new RadioButton { Text = "Absteigend", Left = 150, Top = 46, Width = 120, Checked = absteigend };
            g.Controls.AddRange(new Control[] { cb, auf, ab });
            d.Controls.Add(g);
            return (cb, ab);
        }
        var (cb1, ab1) = Block("Sortieren nach", 8, E.Gruppierung, E.SortierungAbsteigend, false);
        var (cb2, ab2) = Block("dann nach", 86, E.Sortierung2, E.Sortierung2Absteigend, true);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        E.Gruppierung = felder[Math.Max(0, cb1.SelectedIndex)].Schluessel;
        E.SortierungAbsteigend = ab1.Checked;
        E.Sortierung2 = cb2.SelectedIndex <= 0 ? "" : felder[cb2.SelectedIndex - 1].Schluessel;
        E.Sortierung2Absteigend = ab2.Checked;
        EinstellungenLaden();
        Neuzeichnen();
    }

    /// <summary>„Überschriftsfelder“: Feldart (Kurz- oder Langname des Elements) und Größe in % je Zeile der Elementüberschrift.</summary>
    private void UeberschriftsfelderDialog()
    {
        var art = _lehrer ? "Lehrer" : "Klasse";
        if (E.Gruppierung == "Vertreter") art = _lehrer ? "Klasse" : "Lehrer";
        else if (E.Gruppierung is not "Element") art = VertretungsplanDruck.Sortierfelder(_lehrer).FirstOrDefault(x => x.Schluessel == E.Gruppierung).Text ?? art;
        var arten = new[] { "<kein Feld>", $"{art} Kurzname", $"{art} Langname" };
        using var d = new Dialog("Überschriftsfelder", 300, 300);
        d.Controls.Add(new Label { Text = "Überschriftsfelder", Left = 10, Top = 8, Width = 200, Height = 18 });
        var g = new DataGridView { Left = 10, Top = 28, Width = 280, Height = 220 };
        Ui.RasterGestalten(g, true);
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.RowHeadersVisible = true;
        g.RowHeadersWidth = 34;
        var feldart = new DataGridViewComboBoxColumn { Name = "Feldart", HeaderText = "Feldart", Width = 150, FlatStyle = FlatStyle.Flat };
        feldart.Items.AddRange(arten);
        g.Columns.Add(feldart);
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Groesse", HeaderText = "Größe (in %)", Width = 86, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        for (var i = 0; i < 6; i++)
        {
            var f = E.UeberschriftsFelder.ElementAtOrDefault(i);
            g.Rows.Add(f?.Feld switch { "Kurzname" => arten[1], "Langname" => arten[2], _ => arten[0] }, (f?.Groesse ?? 100).ToString());
            g.Rows[i].HeaderCell.Value = (i + 1).ToString();
        }
        d.Controls.Add(g);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        g.EndEdit();
        E.UeberschriftsFelder = g.Rows.Cast<DataGridViewRow>().Select(r => new UeberschriftsFeld
        {
            Feld = (r.Cells[0].Value as string) == arten[1] ? "Kurzname" : (r.Cells[0].Value as string) == arten[2] ? "Langname" : "",
            Groesse = int.TryParse(r.Cells[1].Value?.ToString(), out var n) ? Math.Clamp(n, 25, 400) : 100,
        }).ToList();
        while (E.UeberschriftsFelder.Count > 0 && E.UeberschriftsFelder[^1].Feld.Length == 0) E.UeberschriftsFelder.RemoveAt(E.UeberschriftsFelder.Count - 1);
        Neuzeichnen();
    }

    /// <summary>HTML-Ausgabe wie im Handbuch: Datei „Druck_Leh.htm“ bzw. „Druck_Kla.htm“ in einem frei wählbaren Ordner.</summary>
    private void HtmlAusgabe()
    {
        var d = AppZustand.D;
        var tage = Kalender.Tage(_von, _bis).Where(t => Kalender.IstUnterrichtstag(d, t)).ToList();
        if (tage.Count == 0) { Ui.Info("Im gewählten Zeitraum gibt es keinen Unterrichtstag."); return; }
        using var dlg = new FolderBrowserDialog { Description = "Ordner für die HTML-Ausgabe", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var pfad = Path.Combine(dlg.SelectedPath, _lehrer ? "Druck_Leh.htm" : "Druck_Kla.htm");
        try
        {
            File.WriteAllText(pfad, Core.Austausch.Export.VertretungsplanHtml(d, tage, _lehrer));
            HauptFenster.Starten(pfad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Fehler(ex); }
    }

    /// <summary>Stift „Listenüberschrift“: eigene Überschrift des Ausdrucks.</summary>
    private void ListenueberschriftAendern()
    {
        var t = Ui.Eingabe("Listenüberschrift (leer = Titel aus den Schuldaten):", "Listenüberschrift", E.Ueberschrift);
        if (t is null) return;
        E.Ueberschrift = t.Trim();
        Neuzeichnen();
    }

    /// <summary>Vorschau neu berechnen (jede Einstellung wirkt sofort).</summary>
    internal void Neuzeichnen()
    {
        if (_laden) return;
        foreach (var b in _bilder) b.Dispose();
        _bilder = VertretungsplanDruck.Bilder(_von, _bis, _lehrer, out _spalten, out _titel);
        _geo = VertretungsplanDruck.Geometrie;
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
        _faktor = faktor;
        var w = (int)(bild.Width * faktor);
        var h = (int)(bild.Height * faktor);
        _seite.SetBounds(Math.Max(10, (platz.Width - w) / 2), 14, w, h);
        void Stift(Point lage, string tipp, Action klick)
        {
            var stift = new Button
            {
                Text = "✎", FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(40, 70, 160), Font = new Font("Segoe UI Symbol", 8f),
                Size = new Size(16, 16), TabStop = false, Padding = new Padding(0), Location = lage,
            };
            stift.FlatAppearance.BorderSize = 0;
            stift.Click += (_, _) => klick();
            new ToolTip().SetToolTip(stift, tipp);
            _stifte.Add(stift);
            _vorschauRahmen.Controls.Add(stift);
            stift.BringToFront();
        }
        // Stift „Berichtseinstellungen“ oben in der Mitte des Seitenkopfs (Kopf- und Fußzeile aller Berichte)
        if (_seiteNr == 0 && _geo.Bereich.Width > 0)
            Stift(new Point(_seite.Left + (int)((_geo.Bereich.Left + _geo.Bereich.Width * 0.62f) * faktor), _seite.Top + (int)(_geo.Bereich.Top * faktor) - 2), "Berichtseinstellungen (Kopf- und Fußzeile)",
                () => { if (BerichtseinstellungenDialog.Zeigen(this)) Neuzeichnen(); });
        // Stift „Listenüberschrift“ rechts neben dem Titel
        if (_seiteNr == 0 && _titel != RectangleF.Empty)
            Stift(new Point(_seite.Left + (int)(_titel.Right * faktor) + 4, _seite.Top + (int)(_titel.Top * faktor)), "Listenüberschrift ändern", ListenueberschriftAendern);
        // Stifte über den Spaltenköpfen (die erste Seite bestimmt die Lage)
        foreach (var (schluessel, kopf) in _spalten.ToList())
            Stift(new Point(_seite.Left + (int)(kopf.Right * faktor) - 17, _seite.Top + (int)(kopf.Y * faktor) + 1), "Spaltenüberschrift und Breite ändern", () => SpalteBearbeiten(schluessel));
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
