using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>Arbeitsplatzbezogene Einstellungen des Planungsdialogs (Handbuch Stundenplan Kap. 5.4.3.7 bzw. Vertretungsplanung Kap. 9).</summary>
public sealed class PlanungsdialogEinstellungen
{
    public string Schriftart { get; set; } = "Arial";
    public float Schriftgroesse { get; set; } = 9f;
    /// <summary>Spaltenbreite in % (20–200 %).</summary>
    public int Spaltenbreite { get; set; } = 120;
    /// <summary>Höhe einer Zeile in % (20–200 %).</summary>
    public int Zeilenhoehe { get; set; } = 100;
    /// <summary>Auswahl (Bereich): angezeigte Stunden (0 = alle).</summary>
    public int StundeVon { get; set; } = 1;
    public int StundeBis { get; set; }
    public bool FensterImHintergrund { get; set; }
    public bool NurNichtVerplanteDerWoche { get; set; }
    public bool StudentenAnzeigen { get; set; } = true;
    public bool MehrereUnterrichte { get; set; }
    public bool SummenzeileAusblenden { get; set; }
    public StundenAnzeige KlassenStunde { get; set; } = StundenAnzeige.Fach;
    public StundenAnzeige LehrerStunde { get; set; } = StundenAnzeige.Klasse;
    public StundenAnzeige RaumStunde { get; set; } = StundenAnzeige.Klasse;
    public StundenAnzeige StudentenStunde { get; set; } = StundenAnzeige.Fach;
    public bool KlassenMitStudentenzahl { get; set; }
    public bool RaeumeMitKapazitaet { get; set; }
    public bool UnterrichtMitStudentenzahl { get; set; }
    public bool LehrerMitStudentenzahl { get; set; }
    public bool KarteikartenNebeneinander { get; set; }
    /// <summary>Vertretungsmodus: Darstellung 0 = Stunden-Merker (O,X,..), 1 = Fächer, 2 = Klassen / Lehrer.</summary>
    public int VertretungDarstellung { get; set; } = 2;
    /// <summary>Vertretungsmodus: true = Datum senkrecht (Zeilen), Stunden waagrecht (Spalten).</summary>
    public bool VertretungDatumZeilen { get; set; } = true;
    /// <summary>Vertretungsmodus: Spaltenbreite (Pixel × 10 wie im Vorbild „600“).</summary>
    public int VertretungSpaltenbreite { get; set; } = 600;

    public Font Schrift() => new(Schriftart, Math.Clamp(Schriftgroesse, 6f, 20f));
}

/// <summary>
/// Planungsdialog bzw. Planungs-Stundenplan (Handbuch Stundenplan Kap. 5.3–5.5). Oben Auswahlfeld „Unterricht“ und Karteikarten
/// (Nicht verplant, Information, Historie, Tauschketten), in der Mitte der Planungsbereich (Stecktafel: Stunden der Woche nebeneinander,
/// Unterricht, Klassen, Lehrer und Räume zeilenweise mit Summenzeilen und Stunden-Merkern) bzw. im Planungs-Stundenplan der
/// Klassenstundenplan mit Merkern, unten die Stundenlupe. Verplanen, Entplanen, Verschieben und Tauschen per Drag &amp; Drop mit
/// Kollisionsanzeige, Tauschvorschläge, Tauschketten, Lehrer zuordnen, Bewertung zur aktuellen Zeit (Hohlstunden füllen), Optimierung.
/// </summary>
public sealed class PlanungsdialogFenster : MdiFenster
{
    private enum Art { Unterricht, Summe, Element, RaumSumme, Zusatz }

    /// <summary>Beschreibung einer Zeile des Planungsbereichs.</summary>
    private sealed record Zeile(Art Art, int UnterrichtId, ElementTyp Typ = ElementTyp.Klasse, int Id = 0, string Schluessel = "");

    private readonly bool _stundenplan;
    private readonly PlanungsRaster _raster = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _lupe = new() { Dock = DockStyle.Bottom, Height = 70, Name = "Lupe" };
    private readonly ComboBox _unterricht = new() { Left = 4, Top = 18, Width = 94, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _zeitraum = new() { Left = 4, Top = 46, Width = 118, Height = 30 };
    private readonly Label _fach = new() { Left = 4, Top = 78, Width = 118, Height = 30 };
    private readonly CheckBox _multiDrag = new() { Text = "Multi-Drag", Left = 4, Top = 110, Width = 110 };
    private readonly TabControl _reiter = new() { Dock = DockStyle.Fill };
    private readonly TabControl _reiter2 = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly DataGridView _nv = new() { Dock = DockStyle.Fill, Name = "NichtVerplant" };
    private readonly Label _stundenText = new() { Left = 4, Top = 2, Width = 104, Height = 30 };
    private readonly CheckBox _alleNv = new() { Text = "Alle nicht verpl. Std.", Left = 4, Top = 34, Width = 110, Height = 32 };
    private readonly CheckBox _nurWochen = new() { Text = "Nur aktuelle Wochen", Left = 4, Top = 68, Width = 110, Height = 32 };
    private readonly DataGridView _info = new() { Dock = DockStyle.Fill, Name = "Information" };
    private readonly ListBox _historie = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _ketteText = new() { Left = 214, Top = 6, Width = 200, Height = 22, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripButton _beibehalten;
    private readonly ToolStripButton _farbenKnopf;
    private readonly ToolStripButton _zeitKnopf;

    private int _aktiv;
    private int? _zweiter;
    private readonly HashSet<string> _offen = new();
    private readonly List<(ElementTyp Typ, int Id)> _zusatz = new();
    private readonly HashSet<int> _ausgeblendet = new();
    private readonly List<Zeile> _zeilen = new();
    private readonly List<(int Tag, int Stunde)> _spalten = new();
    private Dictionary<(int, int), int> _raenge = new();
    private readonly Dictionary<(int Tag, int Stunde), Feldstatus> _status = new();
    private Tauschkette? _kette;
    private readonly Stack<(Tauschkette Stand, int Schritte)> _ketteVerlauf = new();
    private int _ketteSchritte;
    private (int Tag, int Stunde, int Klasse, List<Platzbewertung.Zeitbewertung> Liste)? _zeitbewertung;
    private Dictionary<(int, int), Tauschplatz> _tauschplaetze = new();
    private bool _laden;
    private string _eingabe = "";

    private static PlanungsdialogEinstellungen E => AppZustand.Einstellungen.Planungsdialog;

    /// <summary>Planungsdialog öffnen (bzw. vorhandenen aktivieren) – Menüpunkt „Planung | Planungsdialog | Planungsdialog“.</summary>
    public static PlanungsdialogFenster Zeigen(int? unterricht = null) => Oeffnen(false, unterricht);

    /// <summary>Planungs-Stundenplan öffnen – Menüpunkt „Planung | Planungsdialog | Planungs-Stundenplan“.</summary>
    public static PlanungsdialogFenster PlanungsStundenplan(int? unterricht = null) => Oeffnen(true, unterricht);

    private static PlanungsdialogFenster Oeffnen(bool stundenplan, int? unterricht)
    {
        var f = AppZustand.Haupt?.MdiChildren.OfType<PlanungsdialogFenster>().FirstOrDefault(x => x._stundenplan == stundenplan);
        if (f is null)
        {
            f = new PlanungsdialogFenster(stundenplan, unterricht);
            f.Show();
        }
        else
        {
            if (unterricht is int u) f.UnterrichtSetzen(u);
            f.Activate();
        }
        return f;
    }

    public PlanungsdialogFenster(bool stundenplan, int? unterricht = null)
    {
        _stundenplan = stundenplan;
        HilfeThema = "Planungsdialog";
        Icon = SymbolIcon("planen");
        Size = stundenplan ? new Size(560, 600) : new Size(900, 520);
        Positionieren();
        _aktiv = unterricht ?? AnfangsUnterricht();

        // Symbolleiste wie im Vorbild (Handbuch Kap. 5.4.2/5.4.3)
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Stunde verplanen (Einfg)", "plus", StundeVerplanen));
        ts.Items.Add(Knopf("Stunde löschen (Entf)", "loeschen", StundeLoeschen));
        ts.Items.Add(Knopf("Löschen, aktueller Unterricht (Strg+X)", "entfall", LoeschenAktuellerUnterricht));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Rückgängig", "rueckgaengig", Rueckgaengig));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Raum zuordnen / löschen (Strg+R)", "raumzuordnen", RaumZuordnen));
        ts.Items.Add(Knopf("Stunde fixieren (F7)", "fixieren", Fixieren));
        ts.Items.Add(Knopf("Neuer aktueller Unterricht (Strg+Eingabetaste)", "unterricht", NeuerAktuellerUnterricht));
        ts.Items.Add(Knopf("Tauschvorschläge", "tausch", Tauschvorschlaege));
        ts.Items.Add(new ToolStripSeparator());
        _beibehalten = new ToolStripButton(Symbole.Get("schloss")) { ToolTipText = "Aktiven Unterricht beibehalten", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        ts.Items.Add(_beibehalten);
        var alle = new ToolStripButton(Symbole.Get("uebersicht")) { ToolTipText = "Anzeige aller Klassen, Lehrer und Räume (mit Umschalt-Taste: nur Klassen)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        alle.Click += (_, _) => AlleAnzeigen((ModifierKeys & Keys.Shift) != 0);
        ts.Items.Add(alle);
        ts.Items.Add(Knopf("2. Unterricht (Strg+Umschalt+Eingabetaste)", "kopieren", ZweiterUnterricht));
        _zeitKnopf = new ToolStripButton(Symbole.Get("zeitwunsch")) { ToolTipText = "Bewertung zur aktuellen Zeit (Hohlstunde füllen)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _zeitKnopf.Click += (_, _) => BewertungZurZeit();
        ts.Items.Add(_zeitKnopf);
        ts.Items.Add(Knopf("Lehrer der Klasse", "lehrer", LehrerDerKlasse));
        ts.Items.Add(Knopf("Stunden einer Zeile löschen", "minus", StundenEinerZeileLoeschen));
        ts.Items.Add(Knopf("Zeilen löschen", "zurueck", ZeilenLoeschen));
        ts.Items.Add(Knopf("Markierte Zeilen ausblenden", "filter", MarkierteZeilenAusblenden));
        ts.Items.Add(Knopf("Optimierung", "optimierung", () => { OptimierungDialog.Starten(this); Neuaufbau(); }));
        ts.Items.Add(Knopf(stundenplan ? "Planungsdialog" : "Planungs-Stundenplan", "stundenplan", () => Oeffnen(!stundenplan, _aktiv)));
        ts.Items.Add(Knopf("Lehrer im Unterricht ändern", "klassen", LehrerAendern));
        _farbenKnopf = new ToolStripButton(Symbole.Get("farbe")) { ToolTipText = "Unterrichts-Farben anzeigen", CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _farbenKnopf.CheckedChanged += (_, _) => _raster.Invalidate();
        ts.Items.Add(_farbenKnopf);
        ts.Items.Add(Knopf("Einstellungen", "einstellungen", EinstellungenZeigen));

        // Kopf: Auswahlfeld „Unterricht“ (links), Karteikarten (Mitte), Stunden (rechts)
        var kopf = new Panel { Dock = DockStyle.Top, Height = 136 };
        var links = new Panel { Dock = DockStyle.Left, Width = 126 };
        links.Controls.Add(new Label { Text = "Unterricht", Left = 4, Top = 2, AutoSize = true });
        links.Controls.Add(_unterricht);
        var dreh = new Drehknopf("Nächster / vorheriger Unterricht") { Left = 100, Top = 18 };
        dreh.Gedreht += r => Blaettern(r);
        links.Controls.Add(dreh);
        links.Controls.Add(_zeitraum);
        links.Controls.Add(_fach);
        links.Controls.Add(_multiDrag);
        _multiDrag.Checked = E.MehrereUnterrichte;
        _unterricht.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { UnterrichtSuchen(_unterricht.Text); e.Handled = e.SuppressKeyPress = true; } };
        _unterricht.SelectionChangeCommitted += (_, _) => { if (_unterricht.SelectedItem is Treffer t) UnterrichtSetzen(t.Id); };

        var rechts = new Panel { Dock = DockStyle.Right, Width = 150 };
        var stunden = new Panel { Dock = DockStyle.Fill };
        stunden.Controls.Add(_stundenText);
        stunden.Controls.Add(_alleNv);
        stunden.Controls.Add(_nurWochen);
        _alleNv.CheckedChanged += (_, _) => NvFuellen();
        _nurWochen.Checked = E.NurNichtVerplanteDerWoche;
        _nurWochen.CheckedChanged += (_, _) => NvFuellen();
        var pfeile = new Panel { Dock = DockStyle.Right, Width = 30 };
        var nebeneinander = new Button { Text = "◂▸", Left = 3, Top = 4, Width = 24, Height = 22, FlatStyle = FlatStyle.Flat };
        new ToolTip().SetToolTip(nebeneinander, "Karteikarten nebeneinander anordnen");
        nebeneinander.Click += (_, _) => { E.KarteikartenNebeneinander = !E.KarteikartenNebeneinander; ReiterAnordnen(); AppZustand.Einstellungen.Speichern(); };
        pfeile.Controls.Add(nebeneinander);
        rechts.Controls.Add(stunden);
        rechts.Controls.Add(pfeile);

        var mitte = new Panel { Dock = DockStyle.Fill };
        var reiterTeil = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        reiterTeil.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reiterTeil.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        reiterTeil.Controls.Add(_reiter, 0, 0);
        reiterTeil.Controls.Add(_reiter2, 1, 0);
        mitte.Controls.Add(reiterTeil);
        kopf.Controls.Add(mitte);
        kopf.Controls.Add(rechts);
        kopf.Controls.Add(links);
        ReiterAufbauen();

        // Planungsbereich
        _raster.ZelleLiefern = ZelleLiefern;
        _raster.ZielLiefern = ZielLiefern;
        _raster.Umgeschaltet += Umschalten;
        _raster.CursorGeaendert += () => { LupeFuellen(); };
        _raster.Doppelklick += Doppelklick;
        _raster.ZiehenBeginnt += ZiehenBeginnt;
        _raster.Abgelegt += Abgelegt;
        _raster.ZielGeaendert += (z, s) => { if (_raster.Ziehdaten != null) LupeFuellen(z, s); };
        _raster.Kontext += Kontextmenue;
        _raster.KeyDown += RasterTaste;
        _raster.KeyPress += RasterZeichen;

        // Stundenlupe: Stunde hierher ziehen = entplanen
        Ui.RasterGestalten(_lupe, false);
        _lupe.RowHeadersVisible = false;
        foreach (var (n, t, w) in new[] { ("U", "U-Nr", 48), ("LFR", "Lehr., Fa., Rm.", 130), ("K", "Kla.", 60), ("Z", "Zeit", 64), ("W", "Schulwoche", 76), ("S", "Studt", 44), ("T", "Sondertext", 120) })
            _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns["LFR"]!.HeaderCell.Style.BackColor = Ui.Erste;
        _lupe.AllowDrop = true;
        _lupe.DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string x && x.StartsWith("pd:") ? DragDropEffects.Move : DragDropEffects.None;
        _lupe.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.Text) is string x && PlanDaten(x) is { } p) BeginInvoke(() => Entplanen(new List<Planstunde> { p })); };
        _lupe.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _lupe.Rows[e.RowIndex].Tag is int uid) NachZellwechsel(() => UnterrichtSetzen(uid)); };

        Controls.Add(_raster);
        Controls.Add(_lupe);
        Controls.Add(kopf);
        Controls.Add(ts);
        SchriftAnwenden();
        Neuaufbau();
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        FormClosed += (_, _) => AppZustand.StundeGewaehlt -= StundeGewaehlt;
    }

    // ------------------------------------------------------------------------------------------
    // Aufbau
    // ------------------------------------------------------------------------------------------

    private int AnfangsUnterricht()
    {
        var ix = Ix;
        return D.Unterrichte.Where(u => !u.Ignorieren && ix.NichtVerplant(u) > 0).OrderBy(u => u.Id).FirstOrDefault()?.Id
               ?? D.Unterrichte.Where(u => !u.Ignorieren).OrderBy(u => u.Id).FirstOrDefault()?.Id ?? 0;
    }

    private Unterricht? Aktiv => Ix.Unterrichte.GetValueOrDefault(_aktiv);

    private void ReiterAufbauen()
    {
        // Nicht verplant
        var nvSeite = new TabPage("Nicht verplant");
        Ui.RasterGestalten(_nv, false);
        _nv.RowHeadersVisible = false;
        _nv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _nv.MultiSelect = true;
        foreach (var (n, t, w) in new[] { ("Unt", "Unt.", 40), ("Nvp", "Nvp", 34), ("Zeit", "Zeit", 40), ("Kla", "Kla.", 50), ("Le", "Le.", 46), ("Fa", "Fa.", 46), ("Gruppe", "Schülergruppe", 90) })
            _nv.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.Automatic });
        _nv.CellClick += (_, e) => { if (e.RowIndex >= 0 && _nv.Rows[e.RowIndex].Tag is int uid && uid != _aktiv) NachZellwechsel(() => UnterrichtSetzen(uid)); };
        var nvDown = Point.Empty;
        _nv.MouseDown += (_, e) => nvDown = e.Location;
        _nv.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (Math.Abs(e.X - nvDown.X) < 6 && Math.Abs(e.Y - nvDown.Y) < 6)) return;
            var ids = _nv.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<int>().ToList();
            if (ids.Count == 0) return;
            if (!_multiDrag.Checked) ids = ids.Take(1).ToList();
            _nv.DoDragDrop("unterricht:" + string.Join(",", ids), DragDropEffects.Move);
        };
        nvSeite.Controls.Add(_nv);

        // Information
        var infoSeite = new TabPage("Information");
        Ui.RasterGestalten(_info, false);
        _info.RowHeadersVisible = false;
        _info.ColumnHeadersVisible = true;
        foreach (var (n, t, w) in new[] { ("A", "", 60), ("V", "verplant", 60), ("S", "Soll", 50), ("D", "Diff", 50) })
            _info.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        infoSeite.Controls.Add(_info);

        // Historie: Planungsschritte, Rückgängig, Liste löschen
        var histSeite = new TabPage("Historie");
        var histKnoepfe = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 104, FlowDirection = FlowDirection.TopDown, AutoSize = false };
        var rg = new Button { Text = "Rückgängig", Width = 96, Height = 24 };
        rg.Click += (_, _) => Rueckgaengig();
        var ll = new Button { Text = "Liste löschen", Width = 96, Height = 24 };
        ll.Click += (_, _) => { if (Ui.Frage("Liste der Planungsschritte löschen? Die bisherigen Schritte können dann nicht mehr rückgängig gemacht werden.")) { Doc.HistorieLoeschen(); HistorieFuellen(); } };
        histKnoepfe.Controls.Add(rg);
        histKnoepfe.Controls.Add(ll);
        _historie.DoubleClick += (_, _) =>
        {
            // bis einschließlich des gewählten Schritts rückgängig machen
            var n = _historie.SelectedIndex + 1;
            if (n <= 0) return;
            for (var i = 0; i < n && Doc.KannRueckgaengig; i++) Doc.Rueckgaengig();
        };
        histSeite.Controls.Add(_historie);
        histSeite.Controls.Add(histKnoepfe);

        // Tauschketten: Beginn, Tausch, -> x <-, Rückgängig (1 Schritt, Alles)
        var ketteSeite = new TabPage("Tauschketten");
        var beginn = new Button { Text = "Beginn", Left = 6, Top = 6, Width = 96, Height = 24 };
        beginn.Click += (_, _) => KetteBeginnen();
        var tausch = new Button { Text = "Tausch", Left = 6, Top = 34, Width = 96, Height = 24 };
        tausch.Click += (_, _) => KetteTauschen();
        var ende = new Button { Text = "-> x <-", Left = 6, Top = 62, Width = 96, Height = 24 };
        new ToolTip().SetToolTip(ende, "Tauschkette beenden: die offene Stunde bleibt unverplant (Nicht verplant)");
        ende.Click += (_, _) => KetteBeenden();
        var rgGruppe = new GroupBox { Text = "Rückgängig", Left = 108, Top = 0, Width = 100, Height = 88 };
        var schritt = new Button { Text = "1 Schritt", Left = 6, Top = 20, Width = 88, Height = 24 };
        schritt.Click += (_, _) => KetteRueckgaengig(false);
        var allesK = new Button { Text = "Alles", Left = 6, Top = 52, Width = 88, Height = 24 };
        allesK.Click += (_, _) => KetteRueckgaengig(true);
        rgGruppe.Controls.Add(schritt);
        rgGruppe.Controls.Add(allesK);
        ketteSeite.Controls.AddRange(new Control[] { beginn, tausch, ende, rgGruppe, _ketteText });

        _reiter.TabPages.Add(nvSeite);
        _reiter.TabPages.Add(infoSeite);
        _reiter.TabPages.Add(histSeite);
        if (!_stundenplan) _reiter.TabPages.Add(ketteSeite);
        ReiterAnordnen();
    }

    /// <summary>Karteikarten über die Pfeile nebeneinander anordnen (links Nicht verplant/Tauschketten, rechts Information/Historie).</summary>
    private void ReiterAnordnen()
    {
        var alle = _reiter.TabPages.Cast<TabPage>().Concat(_reiter2.TabPages.Cast<TabPage>()).OrderBy(Reihenfolge).ToList();
        _reiter.TabPages.Clear();
        _reiter2.TabPages.Clear();
        var neben = E.KarteikartenNebeneinander;
        foreach (var p in alle)
            (neben && p.Text is "Information" or "Historie" ? _reiter2 : _reiter).TabPages.Add(p);
        _reiter2.Visible = neben;
        if (_reiter.Parent is TableLayoutPanel t)
        {
            t.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, neben ? 55 : 100);
            t.ColumnStyles[1] = neben ? new ColumnStyle(SizeType.Percent, 45) : new ColumnStyle(SizeType.Absolute, 0);
        }
        static int Reihenfolge(TabPage p) => p.Text switch { "Nicht verplant" => 0, "Information" => 1, "Historie" => 2, _ => 3 };
    }

    private void SchriftAnwenden()
    {
        var f = E.Schrift();
        _raster.Font = f;
        var basis = TextRenderer.MeasureText("Mus", f);
        _raster.Spaltenbreite = Math.Max(10, (basis.Width + 4) * Math.Clamp(E.Spaltenbreite, 20, 200) / 100);
        _raster.Zeilenhoehe = Math.Max(10, (basis.Height + 4) * Math.Clamp(E.Zeilenhoehe, 20, 200) / 100);
        _raster.Namensbreite = _stundenplan ? 30 : 100;
        _raster.Baumbreite = _stundenplan ? 0 : 18;
        _raster.ZweiKopfzeilen = !_stundenplan;
        if (_stundenplan)
        {
            _raster.Spaltenbreite = Math.Max(_raster.Spaltenbreite, 70 * Math.Clamp(E.Spaltenbreite, 20, 200) / 100);
            _raster.Zeilenhoehe = Math.Max(_raster.Zeilenhoehe, 22);
        }
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    protected override bool EigeneAenderungenAnzeigen => true;

    private void Neuaufbau()
    {
        if (_laden || IsDisposed) return;
        _laden = true;
        try
        {
            if (!Ix.Unterrichte.ContainsKey(_aktiv)) _aktiv = AnfangsUnterricht();
            var u = Aktiv;
            Text = _stundenplan ? $"Unt.:{_aktiv} - {(u is null ? "" : Ix.KKlassen(u.KlassenIds))}- Planungs-Stundenplan" : $"Unt.:{_aktiv}  Planungsdialog";
            _unterricht.Text = _aktiv == 0 ? "" : _aktiv.ToString();
            _zeitraum.Text = u is null ? "" : $"{(u.VonDatum ?? D.Einstellungen.SchuljahrVon):d.M.yyyy} -\n{(u.BisDatum ?? D.Einstellungen.SchuljahrBis):d.M.yyyy}";
            _fach.Text = u is null ? "" : string.Join(",\n", u.FachIds.Select(f => Ix.KFach(f)).Distinct());
            SpaltenAufbauen();
            ZeilenAufbauen();
            BerechnungenAktualisieren();
            NvFuellen();
            InfoFuellen();
            HistorieFuellen();
            KetteAnzeigen();
            _raster.Neu();
            LupeFuellen();
        }
        finally { _laden = false; }
    }

    private void SpaltenAufbauen()
    {
        _spalten.Clear();
        _raster.Spalten.Clear();
        var zr = D.Zeitraster;
        var von = Math.Clamp(E.StundeVon <= 0 ? 1 : E.StundeVon, 1, zr.StundenProTag);
        var bis = E.StundeBis <= 0 ? zr.StundenProTag : Math.Clamp(E.StundeBis, von, zr.StundenProTag);
        if (_stundenplan)
        {
            // Planungs-Stundenplan: Tage als Spalten, Stunden als Zeilen
            for (var t = 0; t < zr.TageProWoche; t++)
                _raster.Spalten.Add(new RasterSpalte("", Planungsdienst.Tagname(D, t), t));
            for (var s = von; s <= bis; s++) _spalten.Add((-1, s));
            return;
        }
        for (var t = 0; t < zr.TageProWoche; t++)
            for (var s = von; s <= bis; s++)
            {
                _spalten.Add((t, s));
                _raster.Spalten.Add(new RasterSpalte(Planungsdienst.Tagname(D, t, true), (s + zr.ErsteStundennummer - 1).ToString(), (t, s)));
            }
    }

    private void ZeilenAufbauen()
    {
        _zeilen.Clear();
        _raster.Zeilen.Clear();
        if (_stundenplan)
        {
            foreach (var (_, s) in _spalten)
            {
                _zeilen.Add(new Zeile(Art.Unterricht, _aktiv));
                _raster.Zeilen.Add(new RasterZeile { Text = (s + D.Zeitraster.ErsteStundennummer - 1).ToString(), Farbe = Color.Black });
            }
            return;
        }
        foreach (var uid in new[] { (int?)_aktiv, _zweiter }.Where(x => x is int i && i != 0 && Ix.Unterrichte.ContainsKey(i)).Select(x => x!.Value).Distinct())
        {
            var u = Ix.Unterrichte[uid];
            Hinzu(new Zeile(Art.Unterricht, uid), new RasterZeile { Text = $"Unt. {uid}", Offen = null, Hintergrund = uid == _aktiv ? Ui.Erste : null, Farbe = Color.FromArgb(0, 0, 200) });
            Gruppe(u, ElementTyp.Klasse, u.KlassenIds.ToList(), "K");
            Gruppe(u, ElementTyp.Lehrer, u.LehrerIds.ToList(), "L");
            var raeume = Planungsbereich.Raumkandidaten(Ix, u);
            if (raeume.Count > 0)
            {
                var key = $"R{uid}";
                var offen = _offen.Contains(key);
                var name = string.Join(", ", raeume.Take(3).Select(Ix.KRaum)) + (raeume.Count > 3 ? ",…" : "");
                Hinzu(new Zeile(Art.RaumSumme, uid, ElementTyp.Raum, 0, key), new RasterZeile { Text = name, Offen = offen });
                if (offen)
                    foreach (var r in raeume)
                        Hinzu(new Zeile(Art.Element, uid, ElementTyp.Raum, r), new RasterZeile { Text = Elementname(ElementTyp.Raum, r), Ebene = 1, Farbe = Color.Black });
            }
        }
        foreach (var (typ, id) in _zusatz)
            Hinzu(new Zeile(Art.Zusatz, _aktiv, typ, id), new RasterZeile { Text = Elementname(typ, id), Farbe = typ == ElementTyp.Lehrer ? Color.FromArgb(0, 110, 0) : Color.Black });

        void Gruppe(Unterricht u, ElementTyp typ, List<int> ids, string praefix)
        {
            ids = ids.Where(i => !_ausgeblendet.Contains(i)).ToList();
            if (ids.Count == 0) return;
            var key = $"{praefix}{u.Id}";
            // „Wenn nur 1 Zeile ausklappbar, Summenzeile nicht anzeigen“
            if (ids.Count == 1 && E.SummenzeileAusblenden)
            {
                Hinzu(new Zeile(Art.Element, u.Id, typ, ids[0]), new RasterZeile { Text = Elementname(typ, ids[0]), Farbe = Color.Black });
                return;
            }
            var offen = _offen.Contains(key);
            Hinzu(new Zeile(Art.Summe, u.Id, typ, 0, key), new RasterZeile { Text = string.Join(", ", ids.Select(i => Ix.K(typ, i))), Offen = offen });
            if (offen)
                foreach (var i in ids)
                    Hinzu(new Zeile(Art.Element, u.Id, typ, i), new RasterZeile { Text = Elementname(typ, i), Ebene = 1, Farbe = Color.Black });
        }
        void Hinzu(Zeile z, RasterZeile rz) { _zeilen.Add(z); _raster.Zeilen.Add(rz); }
    }

    /// <summary>Name im Namensfeld, ggf. mit Studentenzahl bzw. Kapazität (Einstellungen „Zusätzliche Informationen im Namensfeld“).</summary>
    private string Elementname(ElementTyp typ, int id)
    {
        var e = Ix.Element(typ, id);
        var n = e?.Kurzname ?? "?";
        if (typ == ElementTyp.Klasse && E.KlassenMitStudentenzahl && e is Klasse k && k.Schuelerzahl > 0) n += $" ({k.Schuelerzahl})";
        if (typ == ElementTyp.Raum && E.RaeumeMitKapazitaet && e is Raum r && r.Kapazitaet > 0) n += $" ({r.Kapazitaet})";
        if (typ == ElementTyp.Lehrer && E.LehrerMitStudentenzahl)
        {
            var st = Ix.UnterrichteVon(ElementTyp.Lehrer, id).SelectMany(u => u.KlassenIds).Distinct().Sum(kid => Ix.Klassen.GetValueOrDefault(kid)?.Schuelerzahl ?? 0);
            if (st > 0) n += $" ({st})";
        }
        return n;
    }

    private void BerechnungenAktualisieren()
    {
        _status.Clear();
        _raenge = new();
        _tauschplaetze = new();
        var u = Aktiv;
        if (u is null) return;
        var ignorieren = Ausgewaehlt() is { } sel && sel.UnterrichtId == u.Id ? new[] { sel } : null;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
            for (var s = 1; s <= D.Zeitraster.StundenProTag; s++)
                _status[(t, s)] = Planungsdienst.Status(D, Ix, u, t, s, 1, ignorieren);
        try { _raenge = Platzbewertung.Raenge(D, Ix, u, ignorieren?[0]); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { _raenge = new(); }
        if (_kette is { Abgeschlossen: false } k)
            foreach (var p in k.Plaetze(D)) _tauschplaetze[(p.Tag, p.Stunde)] = p;
    }

    // ------------------------------------------------------------------------------------------
    // Zellen
    // ------------------------------------------------------------------------------------------

    private static readonly Color Gruen = Color.FromArgb(0, 230, 70);
    private static readonly Color GruenHell = Color.FromArgb(170, 240, 170);
    private static readonly Color Rosa = Color.FromArgb(255, 205, 205);
    private static readonly Color Lila = Color.FromArgb(230, 210, 250);
    private static readonly Color Gelb = Color.FromArgb(255, 250, 200);

    private (int Tag, int Stunde) Position(int z, int s) => _stundenplan ? (s, _spalten[z].Stunde) : _spalten[s];

    private RasterZelle ZelleLiefern(int z, int s)
    {
        if (z >= _zeilen.Count || (_stundenplan ? z >= _spalten.Count : s >= _spalten.Count)) return RasterZelle.Leer;
        var zeile = _zeilen[z];
        var (tag, stunde) = Position(z, s);
        if (_stundenplan) return StundenplanZelle(tag, stunde);
        if (!Ix.Unterrichte.TryGetValue(zeile.UnterrichtId, out var u)) return RasterZelle.Leer;
        if (!D.Zeitraster.Verfuegbar(tag, stunde)) return new RasterZelle("", Color.FromArgb(200, 200, 200), Color.Black);
        switch (zeile.Art)
        {
            case Art.Unterricht:
                return UnterrichtZelle(u, tag, stunde);
            case Art.Summe:
            {
                var ids = (zeile.Typ == ElementTyp.Klasse ? u.KlassenIds : u.LehrerIds).Where(i => !_ausgeblendet.Contains(i)).ToList();
                var m = Planungsbereich.Merker(D, Ix, u, zeile.Typ, ids, tag, stunde);
                return MerkerZelle(m, u.Id == _aktiv ? _status.GetValueOrDefault((tag, stunde)) : Feldstatus.Kollision);
            }
            case Art.RaumSumme:
            {
                if (D.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Tag == tag && p.Stunde == stunde)) return new RasterZelle("O", Gelb, Color.Black);
                var (frei, gesamt) = Planungsbereich.RaeumeFrei(D, Ix, u, tag, stunde);
                return new RasterZelle($"{frei}/{gesamt}", frei > 0 ? Gruen : Rosa, Color.Black);
            }
            default:
                return ElementZelle(u, zeile.Typ, zeile.Id, tag, stunde);
        }
    }

    private RasterZelle UnterrichtZelle(Unterricht u, int tag, int stunde)
    {
        var eigene = D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == u.Id && p.Tag == tag && p.Stunde == stunde);
        if (_zeitbewertung is { } zb && u.Id == _aktiv)
        {
            if (zb.Tag == tag && zb.Stunde == stunde) return new RasterZelle("?", Color.FromArgb(255, 230, 120), Color.DarkRed, true);
        }
        if (eigene != null) return new RasterZelle(eigene.Fixiert ? "*" : "+", Gelb, Color.Blue, true, true);
        if (u.Id != _aktiv) return new RasterZelle("", Color.FromArgb(252, 236, 236), Color.Black);
        if (_tauschplaetze.TryGetValue((tag, stunde), out var tp)) return new RasterZelle(tp.Symbol, tp.Abschluss ? Gruen : GruenHell, Color.Black, true);
        var st = _status.GetValueOrDefault((tag, stunde));
        var text = _kette is { Abgeschlossen: false } ? "" : _raenge.TryGetValue((tag, stunde), out var r) ? $"!{r}!" : "";
        return st switch
        {
            Feldstatus.Frei => new RasterZelle(text, Gruen, Color.Black),
            Feldstatus.KeinRaum => new RasterZelle(text, Lila, Color.Black),
            Feldstatus.VorgabeVerletzt => new RasterZelle("", Color.FromArgb(255, 160, 160), Color.Black),
            _ => new RasterZelle("", Color.FromArgb(252, 228, 228), Color.Black),
        };
    }

    private static RasterZelle MerkerZelle(string m, Feldstatus status)
    {
        if (m == "O") return new RasterZelle(m, Gelb, Color.Black);
        if (m.Length == 0) return new RasterZelle("", status == Feldstatus.Frei ? Gruen : Color.White, Color.Black);
        if (int.TryParse(m, out var w)) return new RasterZelle(m, Ui.ZeitwunschFarbe(Math.Clamp(w, -3, 3)), w <= -3 ? Color.White : Color.Black);
        return new RasterZelle(m, Rosa, Color.Black);
    }

    private RasterZelle ElementZelle(Unterricht aktiv, ElementTyp typ, int id, int tag, int stunde)
    {
        var anzeige = typ switch { ElementTyp.Klasse => E.KlassenStunde, ElementTyp.Lehrer => E.LehrerStunde, _ => E.RaumStunde };
        var c = Planungsbereich.Zelle(D, Ix, aktiv, typ, id, tag, stunde, anzeige);
        if (c.Frei)
        {
            if (c.Zeitwunsch != 0) return new RasterZelle(c.Text, Ui.ZeitwunschFarbe(Math.Clamp(c.Zeitwunsch, -3, 3)), c.Zeitwunsch <= -3 ? Color.White : Color.Black);
            return new RasterZelle("", aktiv.Id == _aktiv && _status.GetValueOrDefault((tag, stunde)) == Feldstatus.Frei ? GruenHell : Color.White, Color.Black);
        }
        var hinter = Color.White;
        if (_farbenKnopf.Checked && c.Unterricht is { } o)
        {
            var fach = o.FachIds.Select(f => Ix.Faecher.GetValueOrDefault(f)).FirstOrDefault(f => f != null);
            if (fach != null) hinter = Ui.Farbe(fach.Hintergrund, Color.White);
        }
        if (c.Eigene) hinter = Gelb;
        if (_zeitbewertung is { } zb && c.Stunde != null && zb.Liste.FirstOrDefault(x => ReferenceEquals(x.Stunde, c.Stunde)) is { } bew)
            return new RasterZelle(bew.Rang.ToString(), Color.FromArgb(120, 210, 255), Color.Black, true);
        return new RasterZelle(c.Text, hinter, Ui.Textfarbe(hinter), c.Eigene);
    }

    /// <summary>Planungs-Stundenplan: Stunden der ersten Klasse des aktiven Unterrichts, freie Stunden mit den Merkern des aktiven Unterrichts.</summary>
    private RasterZelle StundenplanZelle(int tag, int stunde)
    {
        var u = Aktiv;
        if (u is null) return RasterZelle.Leer;
        if (!D.Zeitraster.Verfuegbar(tag, stunde)) return new RasterZelle("", Color.FromArgb(200, 200, 200), Color.Black);
        var klasse = u.KlassenIds.FirstOrDefault();
        var hindern = Planungsbereich.Merker(D, Ix, u, ElementTyp.Lehrer, u.LehrerIds.ToList(), tag, stunde);
        var eigen = D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == u.Id && p.Tag == tag && p.Stunde == stunde);
        if (klasse != 0)
        {
            var c = Planungsbereich.Zelle(D, Ix, u, ElementTyp.Klasse, klasse, tag, stunde, E.KlassenStunde);
            if (!c.Frei)
            {
                var hinter = Color.White;
                if (_farbenKnopf.Checked && c.Unterricht?.FachIds.Select(f => Ix.Faecher.GetValueOrDefault(f)).FirstOrDefault(f => f != null) is { } fach)
                    hinter = Ui.Farbe(fach.Hintergrund, Color.White);
                var text = c.Text + (eigen != null ? (eigen.Fixiert ? " *" : " +") : "");
                // Unterricht der Klasse, an dem auch der Lehrer des aktiven Unterrichts beteiligt ist: Merker statt Fach
                return new RasterZelle(text, hinter, Ui.Textfarbe(hinter), eigen != null);
            }
            var st = _status.GetValueOrDefault((tag, stunde));
            if (hindern.Length > 0 && hindern != "O" && !int.TryParse(hindern, out _)) return new RasterZelle(hindern, Color.FromArgb(255, 250, 200), Color.Black);
            var w = Planungsbereich.Zeitwunsch(Ix, ElementTyp.Klasse, u.KlassenIds, tag, stunde);
            if (w < 0) return new RasterZelle(w.ToString(), Ui.ZeitwunschFarbe(Math.Clamp(w, -3, 3)), w <= -3 ? Color.White : Color.Black);
            return new RasterZelle(_raenge.TryGetValue((tag, stunde), out var r) ? $"!{r}!" : "", st == Feldstatus.KeinRaum ? Lila : Color.White, Color.Black);
        }
        return new RasterZelle(hindern, Color.White, Color.Black);
    }

    /// <summary>Hervorhebung beim Ziehen: grün = verplanbar, ↔ = Tausch möglich (Handbuch Kap. 5.4.2.4).</summary>
    private (Color, string?)? ZielLiefern(int z, int s, string daten)
    {
        if (z >= _zeilen.Count) return null;
        var zeile = _zeilen[z];
        if (!_stundenplan && zeile.Art != Art.Unterricht && !(zeile.Art == Art.Element && zeile.Typ == ElementTyp.Raum)) return null;
        var (tag, stunde) = Position(z, s);
        if (daten.StartsWith("pd:") && PlanDaten(daten) is { } p)
        {
            if (zeile.Art == Art.Element && zeile.Typ == ElementTyp.Raum) return p.Tag == tag && p.Stunde == stunde ? (Color.FromArgb(150, 200, 255), null) : null;
            if (zeile.UnterrichtId != p.UnterrichtId && !_stundenplan) return null;
            var st = _zielCache.TryGetValue((daten, tag, stunde), out var c) ? c : _zielCache[(daten, tag, stunde)] = Planungsdienst.Zielstatus(D, Ix, new[] { p }, tag, stunde);
            return st switch
            {
                Feldstatus.Frei => (Gruen, null),
                Feldstatus.TauschMoeglich => (Gruen, "↔"),
                Feldstatus.KeinRaum => (Lila, null),
                _ => null,
            };
        }
        if (daten.StartsWith("unterricht:") && zeile.Art == Art.Unterricht)
        {
            var (ids, _) = StundenplanFenster.UnterrichtDaten(daten);
            if (ids.Count == 0 || !Ix.Unterrichte.TryGetValue(ids[0], out var u)) return null;
            if (!_stundenplan && zeile.UnterrichtId != u.Id) return null;
            return Planungsdienst.Status(D, Ix, u, tag, stunde) == Feldstatus.Frei ? (Gruen, null) : null;
        }
        return null;
    }

    private readonly Dictionary<(string, int, int), Feldstatus> _zielCache = new();

    // ------------------------------------------------------------------------------------------
    // Auswahl, Lupe, Karteikarten
    // ------------------------------------------------------------------------------------------

    /// <summary>Stunde an der Cursorposition (in der Unterrichtszeile: die des Unterrichts, sonst die des Elements).</summary>
    private Planstunde? StundeBei(int z, int s)
    {
        if (z < 0 || z >= _zeilen.Count || s < 0) return null;
        var zeile = _zeilen[z];
        var (tag, stunde) = Position(z, s);
        if (_stundenplan)
        {
            var u = Aktiv;
            var k = u?.KlassenIds.FirstOrDefault() ?? 0;
            return D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == _aktiv && p.Tag == tag && p.Stunde == stunde)
                   ?? (k == 0 ? null : Planungsbereich.Zelle(D, Ix, u!, ElementTyp.Klasse, k, tag, stunde, StundenAnzeige.Fach).Stunde);
        }
        if (!Ix.Unterrichte.TryGetValue(zeile.UnterrichtId, out var akt)) return null;
        return zeile.Art switch
        {
            Art.Unterricht or Art.Summe or Art.RaumSumme => D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == zeile.UnterrichtId && p.Tag == tag && p.Stunde == stunde),
            _ => Planungsbereich.Zelle(D, Ix, akt, zeile.Typ, zeile.Id, tag, stunde, StundenAnzeige.Fach).Stunde,
        };
    }

    private Planstunde? Ausgewaehlt() => _raster.Zeilen.Count == 0 ? null : StundeBei(_raster.Zellcursor.Z, _raster.Zellcursor.S);

    private (int Tag, int Stunde)? CursorPosition()
    {
        var (z, s) = _raster.Zellcursor;
        if (z < 0 || z >= _zeilen.Count || s < 0 || s >= _raster.Spalten.Count) return null;
        return Position(z, s);
    }

    private void LupeFuellen() => LupeFuellen(_raster.Zellcursor.Z, _raster.Zellcursor.S);

    /// <summary>
    /// Stundenlupe: belegte Stunde → deren Unterricht; freie Stelle in der Unterrichtszeile → alle Unterrichte, die eine kollisionsfreie
    /// Verplanung des aktiven Unterrichts verhindern würden (Handbuch Kap. 5.3).
    /// </summary>
    private void LupeFuellen(int z, int s)
    {
        _lupe.Rows.Clear();
        if (z < 0 || z >= _zeilen.Count || s < 0 || s >= _raster.Spalten.Count) return;
        var (tag, stunde) = Position(z, s);
        var p = StundeBei(z, s);
        var u = Aktiv;
        if (p != null && Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var pu)) LupeZeilen(pu, p, pu.Id == _aktiv);
        else if (u != null)
        {
            var hind = Planungsbereich.Verhindernde(D, Ix, u, tag, stunde);
            foreach (var h in hind.DistinctBy(h => h.Planstunde)) LupeZeilen(h.Unterricht, h.Planstunde, false);
            if (hind.Count == 0) LupeZeilen(u, null, true);
        }
        var offen = u is null ? 0 : Ix.NichtVerplant(u);
        _lupe.Rows.Add("+" + offen, "", "", "", "", "", "");
        _lupe.ClearSelection();
    }

    private void LupeZeilen(Unterricht u, Planstunde? p, bool aktiv)
    {
        var zeit = p != null && D.Zeitraster.Zeit(p.Stunde) is { } zt ? $"{Planungsdienst.Tagname(D, p.Tag)}-{p.Stunde}" : "";
        var wochen = $"{Kalender.Schulwoche(D, u.VonDatum ?? D.Einstellungen.SchuljahrVon)}-{Kalender.Schulwoche(D, u.BisDatum ?? D.Einstellungen.SchuljahrBis)}";
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            var raum = p is null ? Ix.KRaum(z.Wunschraum) : Ix.KRaum(p.RaumFuerZeile(i));
            var lfr = string.Join(", ", new[] { Ix.KLehrer(z.LehrerId), Ix.KFach(z.FachId), raum }.Where(x => x.Length > 0));
            var studt = D.Klassen.Where(k => z.KlassenIds.Contains(k.Id)).Sum(k => k.Schuelerzahl);
            var r = _lupe.Rows.Add(i == 0 ? u.Id + (p?.Fixiert == true ? "*" : "") : "", lfr, Ix.KKlassen(z.KlassenIds), i == 0 ? zeit : "", wochen, studt > 0 ? studt.ToString() : "", i == 0 ? u.Text : "");
            _lupe.Rows[r].Tag = u.Id;
            if (aktiv) _lupe.Rows[r].DefaultCellStyle.BackColor = Ui.Erste;
            else _lupe.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 225, 225);
        }
    }

    private sealed record Treffer(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    private void NvFuellen()
    {
        if (_nv.IsDisposed) return;
        _nv.Rows.Clear();
        var u = Aktiv;
        IEnumerable<Unterricht> liste = D.Unterrichte.Where(x => !x.Ignorieren && Ix.NichtVerplant(x) > 0);
        // Standard: nicht verplante Unterrichte der Elemente des aktiven Unterrichts; „Alle nicht verpl. Std.“ = alle
        if (!_alleNv.Checked && u != null)
        {
            var k = u.KlassenIds.ToHashSet();
            var l = u.LehrerIds.ToHashSet();
            liste = liste.Where(x => x.Id == u.Id || x.KlassenIds.Any(k.Contains) || x.LehrerIds.Any(l.Contains));
        }
        if (_nurWochen.Checked)
        {
            var heute = AppZustand.Datum;
            liste = liste.Where(x => (x.VonDatum ?? DateOnly.MinValue) <= heute.AddDays(6) && (x.BisDatum ?? DateOnly.MaxValue) >= heute);
        }
        var summe = 0;
        foreach (var x in liste.OrderBy(x => x.Id))
        {
            var nvp = Ix.NichtVerplant(x);
            summe += nvp;
            var zeit = _kette is { ImPlan: false, Abgeschlossen: false } k2 && k2.UnterrichtId == x.Id ? $"{Planungsdienst.Tagname(D, k2.Tag)}-{k2.Stunde}" : "";
            var r = _nv.Rows.Add(x.Id, nvp, zeit, Ix.KKlassen(x.KlassenIds), string.Join(",", x.LehrerIds.Select(Ix.KLehrer)), string.Join(",", x.FachIds.Select(f => Ix.KFach(f))), x.Schuelergruppe);
            _nv.Rows[r].Tag = x.Id;
            if (x.Id == _aktiv) _nv.Rows[r].DefaultCellStyle.BackColor = Ui.Erste;
        }
        _stundenText.Text = $"Stunden:\n{summe}";
        _nv.ClearSelection();
        // aktiver Unterricht bleibt markiert (Ziehen in den Planungsbereich verplant ihn)
        foreach (DataGridViewRow row in _nv.Rows) if (row.Tag is int id && id == _aktiv) row.Selected = true;
    }

    private void InfoFuellen()
    {
        _info.Rows.Clear();
        var u = Aktiv;
        if (u is null) return;
        var verplant = Ix.Verplant(u);
        var wochen = Math.Max(1, (int)Math.Round(((u.BisDatum ?? D.Einstellungen.SchuljahrBis).DayNumber - (u.VonDatum ?? D.Einstellungen.SchuljahrVon).DayNumber + 1) / 7.0));
        _info.Rows.Add("Woche:", verplant, u.Wochenstunden, u.Wochenstunden - verplant);
        _info.Rows.Add("Jahr:", verplant * wochen, u.Wochenstunden * wochen, (u.Wochenstunden - verplant) * wochen);
        foreach (var f in u.FachIds.Select(f => Ix.Faecher.GetValueOrDefault(f)).Where(f => f != null)) _info.Rows.Add(f!.Kurzname, f.Langname, "", "");
        if (u.Text.Length > 0) _info.Rows.Add("Text", u.Text, "", "");
        if (u.Schuelergruppe.Length > 0) _info.Rows.Add("Gruppe", u.Schuelergruppe, "", "");
        _info.ClearSelection();
    }

    private void HistorieFuellen()
    {
        _historie.Items.Clear();
        foreach (var h in Doc.Historie) _historie.Items.Add(h);
    }

    // ------------------------------------------------------------------------------------------
    // Fensterlogik
    // ------------------------------------------------------------------------------------------

    /// <summary>Synchronisation: ein in anderen Fenstern gewählter Unterricht wird zum aktiven Unterricht (außer „Aktiven Unterricht beibehalten“).</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || _beibehalten.Checked || IsDisposed || u.Id == _aktiv) return;
        _aktiv = u.Id;
        Neuaufbau();
    }

    public void UnterrichtSetzen(int id)
    {
        if (!Ix.Unterrichte.ContainsKey(id)) return;
        if (_kette is { Abgeschlossen: false }) { _kette = null; _ketteVerlauf.Clear(); }
        _aktiv = id;
        _zeitbewertung = null;
        _zielCache.Clear();
        Neuaufbau();
        if (Ix.Unterrichte.TryGetValue(id, out var u)) AppZustand.StundeWaehlen(u, null, this);
    }

    private void Blaettern(int r)
    {
        var ids = D.Unterrichte.Where(u => !u.Ignorieren).Select(u => u.Id).OrderBy(i => i).ToList();
        if (ids.Count == 0) return;
        var i = ids.IndexOf(_aktiv);
        UnterrichtSetzen(ids[((i < 0 ? 0 : i) + r + ids.Count) % ids.Count]);
    }

    /// <summary>Auswahlfeld: Unterrichtsnummer oder Suche nach Klasse, Lehrer bzw. Fach.</summary>
    private void UnterrichtSuchen(string text)
    {
        var treffer = Planungsbereich.Suchen(D, Ix, text);
        if (treffer.Count == 0) { Ui.Info($"Kein Unterricht zu „{text}“ gefunden."); return; }
        _unterricht.Items.Clear();
        foreach (var u in treffer) _unterricht.Items.Add(new Treffer(u.Id, $"{u.Id}  {Ix.UnterrichtText(u)}"));
        if (treffer.Count == 1) { UnterrichtSetzen(treffer[0].Id); return; }
        _unterricht.DroppedDown = true;
    }

    private void Umschalten(int z)
    {
        if (z >= _zeilen.Count || _zeilen[z].Schluessel.Length == 0) return;
        var key = _zeilen[z].Schluessel;
        if (!_offen.Remove(key)) _offen.Add(key);
        ZeilenAufbauen();
        _raster.Neu();
    }

    protected override void WndProc(ref Message m)
    {
        // „Fenster im Hintergrund“: Klicks in den Planungsdialog holen ihn nicht in den Vordergrund
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE && E.FensterImHintergrund && !ContainsFocus) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------------------------------
    // Planungsfunktionen
    // ------------------------------------------------------------------------------------------

    /// <summary>Stunde des aktiven Unterrichts an der Cursorposition verplanen (Einfg, Doppelklick, Schaltfläche).</summary>
    private void StundeVerplanen()
    {
        if (CursorPosition() is not { } pos) return;
        Verplanen(new[] { _aktiv }, pos.Tag, pos.Stunde);
    }

    private void Verplanen(IReadOnlyList<int> uids, int tag, int stunde)
    {
        var unts = uids.Select(i => Ix.Unterrichte.GetValueOrDefault(i)).Where(u => u != null && Ix.NichtVerplant(u) > 0).Cast<Unterricht>().ToList();
        if (unts.Count == 0) { Ui.Info("Alle Stunden dieses Unterrichts sind bereits verplant."); return; }
        if (!D.Zeitraster.Verfuegbar(tag, stunde)) { Ui.Info("Diese Stunde ist im Zeitraster nicht vorgesehen."); return; }
        if (unts.Any(u => D.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Tag == tag && p.Stunde == stunde))) { Ui.Info("Der Unterricht ist zu dieser Zeit bereits verplant."); return; }
        var behindernd = unts.SelectMany(u => Behinderung.AnStellen(D, Ix, u, new[] { (tag, stunde) })).Where(h => !uids.Contains(h.Unterricht.Id))
            .DistinctBy(h => D.Stundenplan.IndexOf(h.Planstunde)).ToList();
        var weg = new List<Planstunde>();
        if (behindernd.Count > 0)
        {
            // Mit Kollision verplanen (Handbuch Kap. 5.4.2.3)
            var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, behindernd,
                $"Stunde verplanen – U-Nr. {string.Join(",", unts.Select(u => u.Id))} in {Planungsdienst.Tagname(D, tag)}-{stunde}");
            if (antwort == BehinderungsAntwort.Abbrechen) return;
            if (antwort == BehinderungsAntwort.Entplanen) weg = behindernd.Select(h => h.Planstunde).ToList();
        }
        Aendern(unts.Count > 1 ? "Stunden gleichzeitig verplanen" : "Stunde verplanen", AenderungsArt.Stundenplan, d =>
        {
            foreach (var x in weg) d.Stundenplan.Remove(x);
            foreach (var u in unts) Planungsdienst.Verplanen(d, d.Unterrichte.First(y => y.Id == u.Id), tag, stunde, false);
        });
        Neuaufbau();
    }

    /// <summary>Stunde löschen (Entf, Schaltfläche): aktive Stunde bzw. Stunde laut Cursor entplanen; leere Zelle mit Zeitwunsch → Zeitwunsch löschen.</summary>
    private void StundeLoeschen()
    {
        var bereich = MarkierteStunden();
        if (bereich.Count > 0) { Entplanen(bereich); return; }
        if (CursorPosition() is not { } pos) return;
        var (z, _) = _raster.Zellcursor;
        var zeile = _zeilen[z];
        // Tipp Handbuch 5.4.1.4: Zeitwunsch direkt im Planungsdialog löschen
        var typ = zeile.Art == Art.Unterricht ? ElementTyp.Unterricht : zeile.Typ;
        var id = zeile.Art == Art.Unterricht ? zeile.UnterrichtId : zeile.Id;
        if (zeile.Art is Art.Element or Art.Zusatz or Art.Unterricht)
        {
            var w = typ == ElementTyp.Unterricht ? Aktiv?.Zeitwuensche.Get(pos.Tag, pos.Stunde) ?? 0 : Planungsbereich.Zeitwunsch(Ix, typ, id, pos.Tag, pos.Stunde);
            if (w != 0)
            {
                Aendern("Zeitwunsch löschen", AenderungsArt.Stammdaten, d =>
                {
                    var ix = new DatenIndex(d);
                    var zw = typ == ElementTyp.Unterricht ? ix.Unterrichte[id].Zeitwuensche : ix.Element(typ, id)!.Zeitwuensche;
                    zw.Set(pos.Tag, pos.Stunde, 0);
                });
                Neuaufbau();
            }
        }
    }

    private List<Planstunde> MarkierteStunden()
    {
        var res = new List<Planstunde>();
        var (c, a) = (_raster.Zellcursor, _raster.Anker);
        for (var z = Math.Min(c.Z, a.Z); z <= Math.Max(c.Z, a.Z); z++)
            for (var s = Math.Min(c.S, a.S); s <= Math.Max(c.S, a.S); s++)
                if (StundeBei(z, s) is { } p && !res.Contains(p)) res.Add(p);
        return res;
    }

    private void Entplanen(List<Planstunde> stunden)
    {
        if (stunden.Count == 0) return;
        if (stunden.Any(p => p.Fixiert) && !Warnhinweis.Frage(this, "fixiert", "Unter den Stunden sind fixierte Stunden.\nTrotzdem entplanen ?")) return;
        Aendern(stunden.Count > 1 ? "Stunden entplanen" : "Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var p in stunden) d.Stundenplan.Remove(p); });
        Neuaufbau();
    }

    /// <summary>Löschen, aktueller Unterricht (Strg+X): Stunde laut Cursor entplanen und deren Unterricht zum aktiven machen.</summary>
    private void LoeschenAktuellerUnterricht()
    {
        if (Ausgewaehlt() is not { } p) { Ui.Info("Bitte den Cursor auf eine verplante Stunde stellen."); return; }
        var uid = p.UnterrichtId;
        Entplanen(new List<Planstunde> { p });
        UnterrichtSetzen(uid);
    }

    /// <summary>Stunden einer Zeile löschen: alle Stunden des Elements in der Cursorzeile entplanen.</summary>
    private void StundenEinerZeileLoeschen()
    {
        var (z, _) = _raster.Zellcursor;
        if (z < 0 || z >= _zeilen.Count) return;
        var stunden = Enumerable.Range(0, _raster.Spalten.Count).Select(s => StundeBei(z, s)).Where(p => p != null).Cast<Planstunde>().Distinct().ToList();
        if (stunden.Count == 0) return;
        if (!Ui.Frage($"{stunden.Count} Stunde(n) der Zeile „{_raster.Zeilen[z].Text}“ entplanen?")) return;
        Entplanen(stunden);
    }

    private void RaumZuordnen()
    {
        if (Ausgewaehlt() is not { } p) { Ui.Info("Bitte den Cursor auf eine verplante Stunde stellen."); return; }
        // Cursor in der Raumzeile des aktiven Unterrichts: Raum sofort löschen bzw. den Raum laut Unterricht verplanen
        var (z, _) = _raster.Zellcursor;
        if (z < _zeilen.Count && _zeilen[z].Art == Art.RaumSumme && p.UnterrichtId == _aktiv)
        {
            var hatRaum = p.Raeume.Any(r => r.HasValue);
            Aendern(hatRaum ? "Raum löschen" : "Raum zuordnen", AenderungsArt.Stundenplan, d =>
            {
                var ix = new DatenIndex(d);
                p.Raeume = hatRaum ? p.Raeume.Select(_ => (int?)null).ToList() : Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[p.UnterrichtId], p.Tag, p.Stunde, new[] { p });
            });
            Neuaufbau();
            return;
        }
        if (RaumZuordnenDialog.Zeigen(this, p)) Neuaufbau();
    }

    private void Fixieren()
    {
        var m = MarkierteStunden();
        if (m.Count == 0) return;
        var ziel = !m[0].Fixiert;
        Aendern(ziel ? "Stunden fixieren" : "Fixierung lösen", AenderungsArt.Stundenplan, _ => { foreach (var p in m) p.Fixiert = ziel; });
        Neuaufbau();
    }

    private void NeuerAktuellerUnterricht()
    {
        if (Ausgewaehlt() is { } p) UnterrichtSetzen(p.UnterrichtId);
    }

    private void ZweiterUnterricht()
    {
        if (Ausgewaehlt() is not { } p || p.UnterrichtId == _aktiv) { Ui.Info("Bitte den Cursor auf eine Stunde eines anderen Unterrichts stellen."); return; }
        _zweiter = p.UnterrichtId;
        Neuaufbau();
    }

    private void Rueckgaengig()
    {
        if (!Doc.KannRueckgaengig) return;
        Doc.Rueckgaengig();
    }

    private void Tauschvorschlaege()
    {
        if (Ausgewaehlt() is not { } p) { Ui.Info("Bitte den Cursor auf eine verplante Stunde stellen."); return; }
        var v = TauschvorschlaegeDialog.Zeigen(this, p);
        if (v is null) return;
        Aendern("Tauschvorschlag", AenderungsArt.Stundenplan, d =>
        {
            var (pa, pb) = (v.A, v.B);
            (pa.Tag, pb.Tag) = (pb.Tag, pa.Tag);
            (pa.Stunde, pb.Stunde) = (pb.Stunde, pa.Stunde);
            var ix = new DatenIndex(d);
            pa.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pa.UnterrichtId], pa.Tag, pa.Stunde, new[] { pa });
            pb.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pb.UnterrichtId], pb.Tag, pb.Stunde, new[] { pb });
        });
        Neuaufbau();
    }

    private void LehrerAendern()
    {
        if (Aktiv is null) return;
        if (LehrerZuordnenDialog.Zeigen(this, _aktiv)) Neuaufbau();
    }

    /// <summary>
    /// Bewertung zur aktuellen Zeit (Handbuch Kap. 5.4.2.10): für die freie Stunde laut Cursor alle Stunden der Klasse bewerten,
    /// wie gut sie dorthin passen (z. B. um eine Hohlstunde zu füllen). Erneuter Klick blendet die Bewertung aus.
    /// </summary>
    private void BewertungZurZeit()
    {
        if (_zeitbewertung != null) { _zeitbewertung = null; _raster.Invalidate(); return; }
        if (CursorPosition() is not { } pos || Aktiv is not { } u) return;
        var (z, _) = _raster.Zellcursor;
        var klasse = _zeilen[z].Typ == ElementTyp.Klasse && _zeilen[z].Id != 0 ? _zeilen[z].Id : u.KlassenIds.FirstOrDefault();
        if (klasse == 0) { Ui.Info("Der Unterricht hat keine Klasse."); return; }
        List<Platzbewertung.Zeitbewertung> liste;
        using (new WarteCursor()) liste = Platzbewertung.ZurZeit(D, Ix, klasse, pos.Tag, pos.Stunde);
        if (liste.Count == 0) { Ui.Info($"Keine Stunde der Klasse {Ix.K(ElementTyp.Klasse, klasse)} kann kollisionsfrei nach {Planungsdienst.Tagname(D, pos.Tag)}-{pos.Stunde} verlegt werden."); return; }
        _zeitbewertung = (pos.Tag, pos.Stunde, klasse, liste);
        // Klasse aufklappen, damit die Bewertungsziffern sichtbar sind
        _offen.Add($"K{_aktiv}");
        ZeilenAufbauen();
        _raster.Neu();
    }

    /// <summary>Lehrer der Klasse (Cursor in einer Klassenzeile) bzw. Klassen des Lehrers (Lehrerzeile) als zusätzliche Zeilen.</summary>
    private void LehrerDerKlasse()
    {
        var (z, s) = _raster.Zellcursor;
        if (z < 0 || z >= _zeilen.Count || Aktiv is not { } u) return;
        var zeile = _zeilen[z];
        var pos = CursorPosition();
        if (zeile.Typ == ElementTyp.Lehrer)
        {
            var lehrer = zeile.Id != 0 ? new[] { zeile.Id } : u.LehrerIds.ToArray();
            foreach (var k in lehrer.SelectMany(l => Ix.UnterrichteVon(ElementTyp.Lehrer, l)).SelectMany(x => x.KlassenIds).Distinct().OrderBy(k => Ix.K(ElementTyp.Klasse, k)))
                ZusatzHinzu(ElementTyp.Klasse, k);
        }
        else
        {
            var klassen = zeile.Typ == ElementTyp.Klasse && zeile.Id != 0 ? new[] { zeile.Id } : u.KlassenIds.ToArray();
            var lehrer = klassen.SelectMany(k => Ix.UnterrichteVon(ElementTyp.Klasse, k)).SelectMany(x => x.LehrerIds).Distinct().ToList();
            // in dieser Stunde nicht belegte sowie ungekoppelte Lehrer zuerst
            int Gewicht(int l)
            {
                var belegt = pos is { } p && D.Stundenplan.Any(x => x.Tag == p.Tag && x.Stunde == p.Stunde && Ix.Unterrichte.TryGetValue(x.UnterrichtId, out var o) && o.LehrerIds.Contains(l));
                var gekoppelt = Ix.UnterrichteVon(ElementTyp.Lehrer, l).Any(x => x.Zeilen.Count > 1);
                return (belegt ? 2 : 0) + (gekoppelt ? 1 : 0);
            }
            foreach (var l in lehrer.OrderBy(Gewicht).ThenBy(Ix.KLehrer)) ZusatzHinzu(ElementTyp.Lehrer, l);
        }
        ZeilenAufbauen();
        _raster.Neu();
        _ = s;
    }

    private void ZusatzHinzu(ElementTyp typ, int id)
    {
        if (!_zusatz.Contains((typ, id))) _zusatz.Add((typ, id));
    }

    /// <summary>Anzeige aller Klassen, Lehrer und Räume (Umschalt: nur Klassen); die Art laut Cursorzeile wird vorgereiht.</summary>
    private void AlleAnzeigen(bool nurKlassen)
    {
        var (z, _) = _raster.Zellcursor;
        var vorn = z >= 0 && z < _zeilen.Count ? _zeilen[z].Typ : ElementTyp.Klasse;
        var typen = nurKlassen ? new[] { ElementTyp.Klasse } : new[] { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum }.OrderBy(t => t == vorn ? 0 : 1).ToArray();
        _zusatz.Clear();
        foreach (var t in typen)
            foreach (var e in Stammdaten.Sortiert(D, t)) ZusatzHinzu(t, e.Id);
        ZeilenAufbauen();
        _raster.Neu();
    }

    /// <summary>Zeilen löschen: zusätzliche Zeilen und den 2. Unterricht ausblenden (der aktive Unterricht bleibt).</summary>
    private void ZeilenLoeschen()
    {
        _zusatz.Clear();
        _zweiter = null;
        _ausgeblendet.Clear();
        Neuaufbau();
    }

    /// <summary>Markierte Zeilen ausblenden (zusätzliche Zeilen bzw. einzelne Elemente des Unterrichts).</summary>
    private void MarkierteZeilenAusblenden()
    {
        var (c, a) = (_raster.Zellcursor, _raster.Anker);
        for (var z = Math.Max(c.Z, a.Z); z >= Math.Min(c.Z, a.Z); z--)
        {
            if (z < 0 || z >= _zeilen.Count) continue;
            var zeile = _zeilen[z];
            if (zeile.Art == Art.Zusatz) _zusatz.Remove((zeile.Typ, zeile.Id));
            else if (zeile.Art == Art.Element && zeile.Typ != ElementTyp.Raum) _ausgeblendet.Add(zeile.Id);
            else if (zeile.Art == Art.Unterricht && zeile.UnterrichtId == _zweiter) _zweiter = null;
        }
        ZeilenAufbauen();
        _raster.Neu();
    }

    private void Doppelklick(int z, int s)
    {
        if (z >= _zeilen.Count) return;
        var zeile = _zeilen[z];
        var p = StundeBei(z, s);
        var (tag, stunde) = Position(z, s);
        if (_stundenplan)
        {
            if (p is null) Verplanen(new[] { _aktiv }, tag, stunde);
            else if (p.UnterrichtId == _aktiv) Entplanen(new List<Planstunde> { p });
            else UnterrichtSetzen(p.UnterrichtId);
            return;
        }
        if (zeile.Art == Art.Unterricht && zeile.UnterrichtId == _aktiv)
        {
            // freie Stelle: verplanen, aktive Stunde: entplanen
            if (p is null) Verplanen(new[] { _aktiv }, tag, stunde);
            else Entplanen(new List<Planstunde> { p });
        }
        else if (p != null && p.UnterrichtId != _aktiv) UnterrichtSetzen(p.UnterrichtId); // nicht aktiver Unterricht → neuer aktueller Unterricht
        else if (p is null && zeile.Art is Art.Summe or Art.Element or Art.RaumSumme && zeile.UnterrichtId == _aktiv) Verplanen(new[] { _aktiv }, tag, stunde);
    }

    // ------------------------------------------------------------------------------------------
    // Drag & Drop
    // ------------------------------------------------------------------------------------------

    private string? ZiehenBeginnt(int z, int s)
    {
        var p = StundeBei(z, s);
        if (p is null) return null;
        var idx = D.Stundenplan.IndexOf(p);
        _zielCache.Clear();
        return idx < 0 ? null : $"pd:{idx}:{z}";
    }

    private Planstunde? PlanDaten(string daten)
    {
        var t = daten.Split(':');
        return t.Length >= 2 && int.TryParse(t[1], out var i) && i >= 0 && i < D.Stundenplan.Count ? D.Stundenplan[i] : null;
    }

    private void Abgelegt(int z, int s, string daten, bool kopieren)
    {
        _zielCache.Clear();
        if (z >= _zeilen.Count) return;
        var (tag, stunde) = Position(z, s);
        if (daten.StartsWith("unterricht:"))
        {
            var (ids, _) = StundenplanFenster.UnterrichtDaten(daten);
            if (ids.Count == 1) _aktiv = ids[0];
            Verplanen(ids, tag, stunde);
            return;
        }
        if (!daten.StartsWith("pd:") || PlanDaten(daten) is not { } p) return;
        var zeile = _zeilen[z];
        var quellZeile = int.TryParse(daten.Split(':').ElementAtOrDefault(2), out var qz) && qz < _zeilen.Count ? _zeilen[qz] : null;
        // Raum ändern: Stunde im Bereich der Räume in eine andere Raumzeile verschieben
        if (!_stundenplan && zeile.Art == Art.Element && zeile.Typ == ElementTyp.Raum && quellZeile is { Art: Art.Element, Typ: ElementTyp.Raum } && p.Tag == tag && p.Stunde == stunde)
        {
            var alt = quellZeile.Id;
            var neu = zeile.Id;
            if (alt == neu) return;
            var zeileIdx = p.Raeume.FindIndex(r => r == alt);
            if (zeileIdx < 0) zeileIdx = 0;
            var belegt = D.Stundenplan.Any(q => q != p && q.Tag == p.Tag && q.Stunde == p.Stunde && q.Raeume.Contains(neu));
            Aendern(belegt ? "Raumtausch" : "Raum ändern", AenderungsArt.Stundenplan, d =>
                Planungsdienst.RaumZuordnen(d, p, zeileIdx, neu, belegt ? Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen : Planungsdienst.RaumKonfliktLoesung.Verdraengen));
            Neuaufbau();
            return;
        }
        if (p.Tag == tag && p.Stunde == stunde) return;
        Verschieben(p, tag, stunde);
    }

    /// <summary>Stunde verschieben bzw. tauschen (wie im Stundenplan): geprüfter Tausch, sonst Rückfrage zu behindernden Stunden.</summary>
    private void Verschieben(Planstunde p, int tag, int stunde)
    {
        var u = Ix.Unterrichte[p.UnterrichtId];
        if (p.Fixiert && !Warnhinweis.Frage(this, "fixiert", $"Unterricht ist fixiert. Unterricht: {u.Id}\n{Planungsdienst.Tagname(D, p.Tag)}-{p.Stunde}\nUnterricht trotzdem verschieben ?")) return;
        var tausch = Planungsdienst.TauschPruefen(D, Ix, new[] { p }, tag, stunde, out var grund);
        var erzwingen = false;
        if (tausch != null)
        {
            var wahl = PlanungsKonfliktDialog.Fragen(this, $"Stunden tauschen – Unterricht {u.Id} {Ix.UnterrichtText(u)}", tausch.Beschreibung, "2-er-Tausch", "Kollision erzeugen");
            if (wahl is null) return;
            if (wahl == 0)
            {
                Aendern("Stunden tauschen", AenderungsArt.Stundenplan, d => Planungsdienst.Tauschen(d, tausch));
                Neuaufbau();
                return;
            }
            erzwingen = true;
        }
        var behindernd = erzwingen ? new List<Hindernis>() : Behinderung.AnStellen(D, Ix, u, new[] { (tag, stunde) }, new[] { p });
        var weg = new List<Planstunde>();
        if (behindernd.Count > 0)
        {
            var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, behindernd, $"Stunde verschieben – U-Nr. {u.Id} {Ix.UnterrichtText(u)} nach {Planungsdienst.Tagname(D, tag)}-{stunde}" +
                (grund.Length > 0 ? $"\n(kein Tausch möglich: {grund})" : ""));
            if (antwort == BehinderungsAntwort.Abbrechen) return;
            if (antwort == BehinderungsAntwort.Entplanen) weg = behindernd.Select(h => h.Planstunde).ToList();
        }
        Aendern(weg.Count > 0 ? $"Stunde verschieben ({weg.Count} behindernde entplant)" : "Stunde verschieben", AenderungsArt.Stundenplan, d =>
        {
            foreach (var x in weg) d.Stundenplan.Remove(x);
            Planungsdienst.Verschieben(d, p, tag, stunde, false, false);
        });
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Tauschketten
    // ------------------------------------------------------------------------------------------

    private void KetteAnzeigen() => _ketteText.Text = _kette?.Text(D) ?? "";

    /// <summary>„Beginn“: Tauschkette mit der Stunde des aktiven Unterrichts laut Cursor beginnen.</summary>
    private void KetteBeginnen()
    {
        var p = Ausgewaehlt();
        if (p is null || p.UnterrichtId != _aktiv) p = D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == _aktiv && CursorPosition() is { } c && x.Tag == c.Tag && x.Stunde == c.Stunde);
        if (p is null) { Ui.Info("Bitte den Cursor auf eine verplante Stunde des aktiven Unterrichts stellen (Symbol +) und dann „Beginn“ wählen."); return; }
        _kette = Tauschkette.Beginnen(p);
        _ketteVerlauf.Clear();
        _ketteSchritte = 0;
        BerechnungenAktualisieren();
        KetteAnzeigen();
        _raster.Invalidate();
        if (_tauschplaetze.Count == 0) Ui.Info("Für diese Stunde gibt es keine Tauschplätze, die den Stundenplan nicht wesentlich verschlechtern.");
    }

    /// <summary>„Tausch“: die offene Stunde an die Cursorposition (markierter Tauschplatz) setzen.</summary>
    private void KetteTauschen()
    {
        if (_kette is not { Abgeschlossen: false } k) { Ui.Info("Bitte zuerst mit „Beginn“ eine Tauschkette starten."); return; }
        if (CursorPosition() is not { } pos || !_tauschplaetze.ContainsKey(pos)) { Ui.Info("Bitte den Cursor auf einen der markierten Tauschplätze in der Unterrichtszeile stellen."); return; }
        var vorher = k.Kopie();
        Kettenglied? glied = null;
        var u = Ix.Unterrichte[k.UnterrichtId];
        var ok = Aendern($"Tauschkette: Unt. {u.Id} {Planungsdienst.Tagname(D, k.Tag)}{k.Stunde} -> {Planungsdienst.Tagname(D, pos.Tag)}{pos.Stunde}", AenderungsArt.Stundenplan, d =>
        {
            var arbeit = k.Kopie();
            glied = arbeit.Tauschen(d, pos.Tag, pos.Stunde);
            _kette = arbeit;
        });
        if (!ok || glied is null) { _kette = vorher; return; }
        _ketteVerlauf.Push((vorher, ++_ketteSchritte));
        // verdrängter Unterricht wird zum aktiven Unterricht
        if (_kette!.Abgeschlossen) Ui.Info("Die Tauschkette ist abgeschlossen.");
        else _aktiv = _kette.UnterrichtId;
        Neuaufbau();
    }

    private void KetteBeenden()
    {
        if (_kette is null) return;
        _kette.Beenden();
        _kette = null;
        _ketteVerlauf.Clear();
        Neuaufbau();
    }

    /// <summary>Rückgängig: letzten Schritt bzw. die ganze Tauschkette zurücknehmen.</summary>
    private void KetteRueckgaengig(bool alles)
    {
        if (_ketteVerlauf.Count == 0) return;
        do
        {
            var (stand, _) = _ketteVerlauf.Pop();
            if (Doc.KannRueckgaengig && Doc.RueckgaengigText?.StartsWith("Tauschkette:") == true) Doc.Rueckgaengig();
            _kette = stand;
            _ketteSchritte--;
        } while (alles && _ketteVerlauf.Count > 0);
        if (_kette != null) _aktiv = _kette.UnterrichtId;
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Tastatur, Kontextmenü, Einstellungen
    // ------------------------------------------------------------------------------------------

    private void RasterTaste(object? sender, KeyEventArgs e)
    {
        var erledigt = true;
        switch (e.KeyCode)
        {
            case Keys.Insert: StundeVerplanen(); break;
            case Keys.Delete: StundeLoeschen(); break;
            case Keys.X when e.Control: LoeschenAktuellerUnterricht(); break;
            case Keys.F7: Fixieren(); break;
            case Keys.R when e.Control: RaumZuordnen(); break;
            case Keys.Enter when e.Control && e.Shift: ZweiterUnterricht(); break;
            case Keys.Enter when e.Control: NeuerAktuellerUnterricht(); break;
            case Keys.Enter when _eingabe.Length > 0: ElementEingegeben(_eingabe); _eingabe = ""; break;
            case Keys.Escape: _eingabe = ""; break;
            default: erledigt = false; break;
        }
        if (erledigt) e.Handled = e.SuppressKeyPress = true;
    }

    /// <summary>Anzeige eines beliebigen Elements: Namen bzw. Unterrichtsnummer eintippen und mit der Eingabetaste bestätigen.</summary>
    private void RasterZeichen(object? sender, KeyPressEventArgs e)
    {
        if (char.IsLetterOrDigit(e.KeyChar) || e.KeyChar is '.' or '-' or '_')
        {
            _eingabe += e.KeyChar;
            e.Handled = true;
        }
        else if (e.KeyChar == '\b' && _eingabe.Length > 0) { _eingabe = _eingabe[..^1]; e.Handled = true; }
    }

    private void ElementEingegeben(string text)
    {
        foreach (var t in new[] { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum })
            if (Stammdaten.Sortiert(D, t).FirstOrDefault(x => string.Equals(x.Kurzname, text, StringComparison.OrdinalIgnoreCase)) is { } e)
            {
                ZusatzHinzu(t, e.Id);
                ZeilenAufbauen();
                _raster.Neu();
                return;
            }
        if (int.TryParse(text, out var nr) && Ix.Unterrichte.ContainsKey(nr)) { UnterrichtSetzen(nr); return; }
        Ui.Info($"„{text}“: kein Element bzw. Unterricht gefunden.");
    }

    private void Kontextmenue(int z, int s, Point pt)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var p = s >= 0 ? StundeBei(z, s) : null;
        cm.Items.Add("Stunde verplanen", Symbole.Get("plus"), (_, _) => StundeVerplanen());
        cm.Items.Add("Stunde löschen", Symbole.Get("loeschen"), (_, _) => StundeLoeschen());
        if (p != null)
        {
            cm.Items.Add("Löschen, aktueller Unterricht", Symbole.Get("entfall"), (_, _) => LoeschenAktuellerUnterricht());
            cm.Items.Add("Neuer aktueller Unterricht", Symbole.Get("unterricht"), (_, _) => NeuerAktuellerUnterricht());
            cm.Items.Add("2. Unterricht", Symbole.Get("kopieren"), (_, _) => ZweiterUnterricht());
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Raum zuordnen / löschen", Symbole.Get("raumzuordnen"), (_, _) => RaumZuordnen());
            cm.Items.Add(p.Fixiert ? "Fixierung lösen" : "Stunde fixieren", Symbole.Get("fixieren"), (_, _) => Fixieren());
            cm.Items.Add("Tauschvorschläge", Symbole.Get("tausch"), (_, _) => Tauschvorschlaege());
            cm.Items.Add("Lehrer im Unterricht ändern", Symbole.Get("klassen"), (_, _) => { if (LehrerZuordnenDialog.Zeigen(this, p.UnterrichtId)) Neuaufbau(); });
        }
        else if (MarkierteStunden().Count > 0) cm.Items.Add("Stunden fixieren", Symbole.Get("fixieren"), (_, _) => Fixieren());
        cm.Show(_raster, pt);
    }

    private void EinstellungenZeigen()
    {
        if (PlanungsdialogEinstellungenDialog.Zeigen(this, E))
        {
            AppZustand.Einstellungen.Speichern();
            _multiDrag.Checked = E.MehrereUnterrichte;
            SchriftAnwenden();
            Neuaufbau();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Funktionstasten auch, wenn der Fokus nicht im Planungsbereich steht
        if (!_raster.Focused && e.KeyCode == Keys.F7) { Fixieren(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    /// <summary>Selbsttest: Zeilentexte des Planungsbereichs und Text einer Zelle.</summary>
    internal IReadOnlyList<string> SelbsttestZeilen => _raster.Zeilen.Select(z => z.Text).ToList();
    internal string SelbsttestZelle(int z, int s) => ZelleLiefern(z, s).Text;
}

/// <summary>Dialog „Einstellungen“ des Planungsdialogs (Handbuch Stundenplan Kap. 5.4.3.7).</summary>
public static class PlanungsdialogEinstellungenDialog
{
    public static bool Zeigen(IWin32Window owner, PlanungsdialogEinstellungen e)
    {
        using var d = new Dialog("Einstellungen", 316, 400);
        var schrift = new Font(e.Schriftart, e.Schriftgroesse);
        var schriftKnopf = new Button { Text = "Schrift", Left = 8, Top = 8, Width = 90, Height = 24 };
        var schriftText = new Label { Left = 108, Top = 12, Width = 180, Text = $"{schrift.Name} {schrift.SizeInPoints:0.#}" };
        schriftKnopf.Click += (_, _) =>
        {
            using var fd = new FontDialog { Font = schrift, ShowEffects = false };
            if (fd.ShowDialog(d) != DialogResult.OK) return;
            schrift = fd.Font;
            schriftText.Text = $"{schrift.Name} {schrift.SizeInPoints:0.#}";
        };
        var raster = new GroupBox { Text = "Raster: Breite / Höhe", Left = 8, Top = 38, Width = 150, Height = 78 };
        var breite = new NumericUpDown { Left = 6, Top = 18, Width = 44, Minimum = 20, Maximum = 200, Value = Math.Clamp(e.Spaltenbreite, 20, 200) };
        var hoehe = new NumericUpDown { Left = 6, Top = 48, Width = 44, Minimum = 20, Maximum = 200, Value = Math.Clamp(e.Zeilenhoehe, 20, 200) };
        raster.Controls.AddRange(new Control[] { breite, new Label { Text = "Spaltenbreite in % (20 - 200 %)", Left = 54, Top = 16, Width = 94, Height = 30, Font = Ui.SchriftKlein },
            hoehe, new Label { Text = "Höhe einer Zeile in % (20 - 200 %)", Left = 54, Top = 46, Width = 94, Height = 30, Font = Ui.SchriftKlein } });
        var bereich = new GroupBox { Text = "Auswahl (Bereich)", Left = 164, Top = 38, Width = 144, Height = 78 };
        var von = new NumericUpDown { Left = 6, Top = 18, Width = 44, Minimum = 1, Maximum = 30, Value = Math.Clamp(e.StundeVon <= 0 ? 1 : e.StundeVon, 1, 30) };
        var bis = new NumericUpDown { Left = 6, Top = 48, Width = 44, Minimum = 1, Maximum = 30, Value = Math.Clamp(e.StundeBis <= 0 ? AppZustand.D.Zeitraster.StundenProTag : e.StundeBis, 1, 30) };
        bereich.Controls.AddRange(new Control[] { von, new Label { Text = "Stunde von", Left = 56, Top = 20, AutoSize = true }, bis, new Label { Text = "Stunde bis", Left = 56, Top = 50, AutoSize = true } });
        var hinter = new CheckBox { Text = "Fenster im Hintergrund", Left = 8, Top = 122, Width = 150, Checked = e.FensterImHintergrund };
        var band = new CheckBox { Text = "Planungsdialog: Band-Modus", Left = 8, Top = 142, Width = 150, Enabled = false };
        var kalender = new CheckBox { Text = "Kalender: Nur nicht verplante der Woche anzeigen", Left = 8, Top = 162, Width = 150, Height = 30, Checked = e.NurNichtVerplanteDerWoche };
        var studenten = new CheckBox { Text = "Studenten anzeigen", Left = 164, Top = 122, Width = 144, Checked = e.StudentenAnzeigen };
        var mehrere = new CheckBox { Text = "DragDrop: mehrere Unterrichte", Left = 164, Top = 142, Width = 144, Checked = e.MehrereUnterrichte };
        var summe = new CheckBox { Text = "Wenn nur 1 Zeile ausklappbar, Summenzeile nicht anzeigen", Left = 164, Top = 162, Width = 144, Height = 30, Checked = e.SummenzeileAusblenden };
        var was = new GroupBox { Text = "Was wird in einer Stunde angezeigt ?", Left = 8, Top = 196, Width = 300, Height = 110 };
        ComboBox Anzeige(StundenAnzeige wert, int top, string text)
        {
            var cb = new ComboBox { Left = 6, Top = top, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Fach", "Klasse", "Lehrer", "Raum" });
            cb.SelectedIndex = (int)wert;
            was.Controls.Add(cb);
            was.Controls.Add(new Label { Text = text, Left = 142, Top = top + 3, AutoSize = true });
            return cb;
        }
        var kl = Anzeige(e.KlassenStunde, 18, "Klassen-Stunde");
        var le = Anzeige(e.LehrerStunde, 40, "Lehrer-Stunde");
        var ra = Anzeige(e.RaumStunde, 62, "Raum-Stunde");
        var st = Anzeige(e.StudentenStunde, 84, "Studenten-Stunde");
        var zus = new GroupBox { Text = "Zusätzliche Informationen im Namensfeld", Left = 8, Top = 310, Width = 300, Height = 50 };
        var kStud = new CheckBox { Text = "Klassen mit Studentenzahl", Left = 6, Top = 16, Width = 150, Checked = e.KlassenMitStudentenzahl, Font = Ui.SchriftKlein };
        var rKap = new CheckBox { Text = "Räume mit Kapazität", Left = 160, Top = 16, Width = 134, Checked = e.RaeumeMitKapazitaet, Font = Ui.SchriftKlein };
        var lStud = new CheckBox { Text = "Lehrer mit Studentenzahl", Left = 160, Top = 32, Width = 134, Height = 16, Checked = e.LehrerMitStudentenzahl, Font = Ui.SchriftKlein };
        var uStud = new CheckBox { Text = "Unterricht mit Studentenzahl", Left = 6, Top = 32, Width = 150, Height = 16, Checked = e.UnterrichtMitStudentenzahl, Font = Ui.SchriftKlein };
        zus.Controls.AddRange(new Control[] { kStud, rKap, uStud, lStud });
        d.Controls.AddRange(new Control[] { schriftKnopf, schriftText, raster, bereich, hinter, band, kalender, studenten, mehrere, summe, was, zus });
        d.Ok.Text = "Ok";
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        e.Schriftart = schrift.Name;
        e.Schriftgroesse = schrift.SizeInPoints;
        e.Spaltenbreite = (int)breite.Value;
        e.Zeilenhoehe = (int)hoehe.Value;
        e.StundeVon = (int)von.Value;
        e.StundeBis = (int)Math.Max(von.Value, bis.Value);
        e.FensterImHintergrund = hinter.Checked;
        e.NurNichtVerplanteDerWoche = kalender.Checked;
        e.StudentenAnzeigen = studenten.Checked;
        e.MehrereUnterrichte = mehrere.Checked;
        e.SummenzeileAusblenden = summe.Checked;
        e.KlassenStunde = (StundenAnzeige)kl.SelectedIndex;
        e.LehrerStunde = (StundenAnzeige)le.SelectedIndex;
        e.RaumStunde = (StundenAnzeige)ra.SelectedIndex;
        e.StudentenStunde = (StundenAnzeige)st.SelectedIndex;
        e.KlassenMitStudentenzahl = kStud.Checked;
        e.RaeumeMitKapazitaet = rKap.Checked;
        e.UnterrichtMitStudentenzahl = uStud.Checked;
        e.LehrerMitStudentenzahl = lStud.Checked;
        return true;
    }
}
