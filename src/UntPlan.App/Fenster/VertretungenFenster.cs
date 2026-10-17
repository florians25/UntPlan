using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vertretungsfenster (Vertretungsmodus): alle Vertretungen des Planungszeitraums mit Ansichten Klasse/Lehrer/Druck,
/// Filter "Offene Vertretungen" und "Kollisionen", direkter Eingabe des Vertreters ("+" = Vertr. o. Lehrkr., "-" = Entfall)
/// und gleichzeitiger Bearbeitung mehrerer markierter Zeilen.
/// </summary>
public sealed class VertretungenFenster : MdiFenster, IDruckbar
{
    private static readonly string[] Ansichten = { "Klasse - Standard", "Lehrer - Standard", "Druck - Klasse", "Druck - Lehrer", "Pausenaufsichten" };

    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd.MM.yyyy", Width = 118 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd.MM.yyyy", Width = 118 };
    private readonly CheckBox _offen = new() { Text = "Offene Vertretungen", AutoSize = true };
    /// <summary>Bei Filter („Offene Vertretungen“, „Kollisionen“, Element): gerade bearbeitete Zeilen bleiben sichtbar, bis eine andere Zeile gewählt wird.</summary>
    private readonly ZeilenBehalten _behalten = new();
    /// <summary>Neuaufbau wurde während einer Eingabe aufgeschoben (wird beim Verlassen der Zelle nachgeholt).</summary>
    private bool _neuaufbauAusstehend;
    private bool _sortierenAusstehend;
    /// <summary>Wert der Zelle beim Beginn der Bearbeitung (unveränderte Zellen lösen keine Änderung aus).</summary>
    private string? _wertVorher;
    /// <summary>Sortierung per Klick auf die Spaltenüberschrift (wie im Vorbild); null = Standard (Datum, Klasse bzw. Lehrer, Stunde).</summary>
    private string? _sortSpalte;
    private bool _sortAbsteigend;
    private readonly CheckBox _kollisionen = new() { Text = "Kollisionen", AutoSize = true };
    /// <summary>Entfälle/Freisetzungen einer Veranstaltung je Tag in einer Zeile (siehe <see cref="Benutzereinstellungen.VeranstaltungenZusammenfassen"/>).</summary>
    private readonly CheckBox _vaSammeln = new() { Text = "Veranstaltungen zusammenfassen", AutoSize = true };
    /// <summary>Stellvertreter-Ids zusammengefasster Entfälle/Freisetzungen einer Veranstaltung (nur Anzeige, Text bearbeitbar).</summary>
    private readonly HashSet<int> _vaSammel = new();
    /// <summary>Ab so vielen Entfällen/Freisetzungen einer Veranstaltung an einem Tag werden sie zusammengefasst.</summary>
    private const int VaSammelAb = 4;
    /// <summary>„Felder der Ansicht“ während eines Neuaufbaus (sonst je Zelle neu gelesen).</summary>
    private Dictionary<string, string>? _felder;
    private readonly ComboBox _element = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ToolStripButton _inhalt = new("Felder mit Inhalt", Symbole.Get("uebersicht", 16)) { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Felder mit Inhalt: alle Spalten mit einer Eingabe zeigen – erneuter Klick stellt die ursprünglichen Spalten wieder her" };
    /// <summary>Spalten, die vor „Felder mit Inhalt“ sichtbar waren.</summary>
    private HashSet<string>? _vorInhalt;
    /// <summary>Zusammengefasste Zeilen einer Veranstaltung (je Tag eine Zeile): Stellvertreter-Id → alle Stundenzeilen.</summary>
    private readonly Dictionary<int, List<Vertretung>> _gruppen = new();
    /// <summary>
    /// Tausch/Verlegung wie im Vorbild in einer Zeile: zur ankommenden Stunde (Schlüssel) die an derselben Position wegverlegte
    /// Stunde (Entfall mit „(Le.) nach“), deren Lehrer, Fach, Raum und Klassen in den Klammerspalten stehen.
    /// </summary>
    private readonly Dictionary<int, Vertretung> _weg = new();
    private readonly GroupBox _elementGruppe;
    private readonly Steuerelemente.ZeitraumAuswahl _zeitraum;
    private readonly ComboBox _ansicht = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _nr = new() { Width = 60 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(12, 5, 0, 0) };
    private readonly Dictionary<DateOnly, Tagesplan> _plaene = new();
    private readonly System.Windows.Forms.Timer _auswahlTimer = new() { Interval = 140 };
    private bool _laedt;
    private bool _sync = true;

    public VertretungenFenster()
    {
        Text = "Vertretungen / Klasse – Standard";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("vertretung");
        Size = new Size(1180, 520);
        Positionieren();
        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _ansicht.Items.AddRange(Ansichten);
        _ansicht.SelectedIndex = Math.Max(0, Array.IndexOf(Ansichten, AppZustand.Einstellungen.Vertretungsansicht));
        ElementeFuellen();

        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Felder der Ansicht", "rollup", FelderDialog));
        ts.Items.Add(Knopf("Vertretungsvorschlag", "vertretung", VorschlagOeffnen, false));
        ts.Items.Add(new ToolStripButton("Nächste offene", Steuerelemente.PfeilKnopf.Bild(true), (_, _) => NaechsteOffene()) { ToolTipText = "Zur nächsten offenen Vertretung springen (F3)", TextImageRelation = TextImageRelation.TextBeforeImage });
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Entfall", Symbole.Get("loeschen", 16), (_, _) => Entfall())
        {
            ToolTipText = "Entfall – Unterricht entfällt (Taste - beim Vertreter); erneut klicken macht wieder eine offene Vertretung daraus", DisplayStyle = ToolStripItemDisplayStyle.Image,
        });
        ts.Items.Add(Knopf("Vertr. o. Lehrkr. (+)", "planen", OhneLehrkraft, false));
        ts.Items.Add(Knopf("Vertretung teilen", "tausch", Teilen));
        ts.Items.Add(Knopf("Lehrkraft zur Veranstaltung hinzufügen", "lehrer", LehrkraftHinzufuegen));
        ts.Items.Add(Knopf("Raum zuordnen", "raumzuordnen", RaumZuordnen));
        ts.Items.Add(Knopf("Sondereinsatz", "neu", () => SondereinsatzDialog.Neu(this, AppZustand.Datum, Aktuelle().FirstOrDefault()?.Stunde ?? 1, Array.Empty<int>(), Array.Empty<int>())));
        ts.Items.Add(Knopf("Als Vormerkung beiseitelegen", "zurueck", AlsVormerkung));
        ts.Items.Add(Knopf("Zurücksetzen (wieder offen)", "rueckgaengig", Zuruecksetzen));
        ts.Items.Add(Knopf("Löschen", "minus", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        _inhalt.CheckedChanged += (_, _) => FelderMitInhalt(_inhalt.Checked);
        ts.Items.Add(_inhalt);
        ts.Items.Add(Knopf("Vertretung fixieren (X) / lösen", "schloss", Fixieren));
        ts.Items.Add(Knopf("Nicht drucken (N) ein/aus", "drucken", NichtDrucken));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Automatik – Vertretungs-Automatik", "optimierung", () => VertretungsautomatikDialog.Zeigen(this, Von, Bis)));
        ts.Items.Add(Knopf("Neu berechnen (Absenzen, Veranstaltungen, Klausuren)", "aktualisieren", NeuBerechnen));
        ts.Items.Add(Knopf("Nachrichten zum Tag", "kommentar", () => TagestextDialog.Zeigen(this, AppZustand.Datum)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Vertretungsplan (HTML)", "html", () => HtmlAusgabe(DateOnly.FromDateTime(_von.Value), Math.Max(1, (int)(_bis.Value - _von.Value).TotalDays + 1), _ansicht.SelectedIndex is 1 or 3)));
        ts.Items.Add(Knopf("Vertretungsplan drucken (Aushang)", "drucken", () => Drucken(true)));
        ts.Items.Add(Knopf("Liste drucken (Felder mit „drucken“)", "vorschau", ListeDrucken));
        var einst = new ToolStripDropDownButton("Einstellungen", Symbole.Get("einstellungen", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Einstellungen" };
        einst.DropDownItems.Add("Einstellungen Vertretungsfenster (Vertretungsarten anzeigen)…", null, (_, _) => FensterEinstellungen());
        einst.DropDownItems.Add("Vertretungsplanung: Farben und Bezeichnungen, Zähler, Vorschlag…", null, (_, _) => VertretungsEinstellungenDialog.Zeigen(this));
        var nurNeu = new ToolStripMenuItem("Seitenlayout: Nur neue Vertretungen drucken") { CheckOnClick = true, Checked = AppZustand.Einstellungen.Vertretungsdruck.NurNeue, ToolTipText = "Nur Vertretungen drucken, die in dieser Arbeitssitzung angelegt oder geändert wurden (Kennzeichen „Neu“)" };
        nurNeu.CheckedChanged += (_, _) => { AppZustand.Einstellungen.Vertretungsdruck.NurNeue = nurNeu.Checked; AppZustand.Einstellungen.Speichern(); };
        einst.DropDownOpening += (_, _) => nurNeu.Checked = AppZustand.Einstellungen.Vertretungsdruck.NurNeue;
        einst.DropDownItems.Add(nurNeu);
        einst.DropDownItems.Add("Seitenlayout Vertretungsplan…", Symbole.Get("drucken", 16), (_, _) => Druck.SeitenlayoutFenster.Zeigen(this, Von, Bis, _ansicht.SelectedIndex is 1 or 3));
        ts.Items.Add(einst);

        // Kopf wie im Vertretungsfenster üblich: Element, Zeitraum (Tag/Woche/Von-Bis), Ansicht einschränken
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(4, 2, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        GroupBox Gruppe(string text, params Control[] inhalt)
        {
            var g = new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(2, 0, 6, 0), MinimumSize = new Size(0, 52) };
            var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 0) };
            fl.Controls.AddRange(inhalt);
            g.Controls.Add(fl);
            return g;
        }
        _elementGruppe = Gruppe("Klasse(n)", new Control[] { _element }.Concat(Ui.Durchschalter(_element)).ToArray());
        _zeitraum = new Steuerelemente.ZeitraumAuswahl(_von, _bis, "Vertretungen");
        _elementGruppe.Text = _ansicht.SelectedIndex is 1 or 3 ? "Vertreter" : "Klasse(n)";
        filter.Controls.Add(_elementGruppe);
        filter.Controls.Add(Gruppe("Von-Bis", _zeitraum));
        _offen.Padding = _kollisionen.Padding = new Padding(2, 4, 6, 0);
        _vaSammeln.Padding = new Padding(2, 4, 6, 0);
        _vaSammeln.Checked = AppZustand.Einstellungen.VeranstaltungenZusammenfassen;
        new ToolTip().SetToolTip(_vaSammeln, "Entfälle und Freisetzungen einer Veranstaltung (z. B. Lehrkräftekonferenz, pädagogischer Tag) je Tag in einer Zeile zeigen – „Alle Klassen“, Stunde „1-6“. Ausschalten zeigt jede Stunde einzeln.");
        filter.Controls.Add(Gruppe("Ansicht einschränken auf …", _offen, _kollisionen, _vaSammeln));

        // Fußleiste wie im Vorbild: Vtr-Nr. links, Ansicht ganz rechts (keine Zeilen-/Offen-Zählung)
        var unten = new Steuerelemente.Fussleiste { BackColor = Ui.Kopf };
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 5, 0, 0), WrapContents = false, BackColor = Ui.Kopf };
        links.Controls.Add(new Label { Text = "Vtr-Nr.:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        links.Controls.Add(_nr);
        links.Controls.Add(_status);
        var rechts = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Padding.Empty, Margin = Padding.Empty, WrapContents = false, BackColor = Ui.Kopf };
        rechts.Controls.Add(new Label { Text = "Ansicht:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        _ansicht.Margin = new Padding(3, 0, 0, 0);
        rechts.Controls.Add(_ansicht);
        unten.Controls.Add(links);
        unten.Rechts = rechts;
        links.Padding = new Padding(4, 5, 0, 0);
        unten.Resize += (_, _) => links.Width = Math.Max(0, rechts.Left - 4);
        links.Dock = DockStyle.Left;

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        _g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        foreach (var (name, kopf, _, breite, lesbar, _, _) in Spalten)
        {
            DataGridViewColumn c = name switch
            {
                "Vertreter" => Steuerelemente.SuchKombi.Spalte(name, kopf, breite, new[] { "+++", "---", "???" }.Concat(Stammdaten.Sortiert(D.Lehrer).Select(l => l.Kurzname))),
                "NRaum" => Steuerelemente.SuchKombi.Spalte(name, kopf, breite, new[] { "???" }.Concat(Stammdaten.Sortiert(D.Raeume).Select(r => r.Kurzname))),
                "NFach" => Steuerelemente.SuchKombi.Spalte(name, kopf, breite, Stammdaten.Sortiert(D.Faecher).Select(f => f.Kurzname)),
                _ => new DataGridViewTextBoxColumn { Name = name, HeaderText = kopf },
            };
            _g.Columns.Add(c);
            c.Width = breite;
            c.ReadOnly = lesbar;
            c.SortMode = DataGridViewColumnSortMode.Programmatic;
            // bearbeitbare Felder: im Vertretungsmodus weiß wie im Vorbild (nicht bearbeitbare grünlich, siehe Auswahlbreite.ZellFarben)
            if (!lesbar && !Ui.Vertretungsmodus) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 236);
        }
        // Reihenfolge wie im Vorbild: Vtr-Nr. Art Datum Stunde (Fach) Fach (Lehrer) Vertreter (Klasse(n)) Klasse(n) (Raum) Raum Vertr. von (Le.) nach Text Statistik …
        for (var i = 0; i < Spalten.Length; i++) _g.Columns[Spalten[i].Name]!.DisplayIndex = i;
        _g.Columns["Nr"]!.Frozen = true;
        // Vertretungstext füllt die Restbreite, wird aber nie schmaler als 240 Pixel (sonst waagerecht blättern)
        _g.Columns["Text"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.Columns["Text"]!.MinimumWidth = 240;
        _g.Columns["Text"]!.FillWeight = 300;
        // mehrere Klassen untereinander (Zeilenhöhe passt sich an)
        _g.Columns["Klassen"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.Columns["Text"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellFormatting += Formatieren;
        _g.CellPainting += KollisionZeichnen;
        _g.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
            _wertVorher = _g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
            // fixierte Vertretungen sind vor Veränderungen geschützt, Unterrichtszeilen sind nur Anzeige
            if (v.Id == 0) { e.Cancel = true; return; }
            if (v.Fixiert) { e.Cancel = true; _status.Text = $"Vertretung {v.Id} ist fixiert (X) – zum Ändern zuerst lösen."; }
            var spalte = _g.Columns[e.ColumnIndex].Name;
            if (v.Art == VertretungsArt.Veranstaltung && spalte != "Vertreter" && spalte != "Text") e.Cancel = true;
            // zusammengefasste Entfälle einer Veranstaltung: nur der Text (gilt für alle Stunden); einzeln bearbeiten nach „Veranstaltungen zusammenfassen“ aus
            if (_vaSammel.Contains(v.Id) && spalte is not ("Text" or "Text2" or "Kz"))
            {
                e.Cancel = true;
                _status.Text = "Zusammengefasste Zeile einer Veranstaltung – zum Bearbeiten einzelner Stunden „Veranstaltungen zusammenfassen“ ausschalten (Doppelklick).";
            }
            if (v.Art == VertretungsArt.Freisetzung && spalte is "Vertreter" or "NFach" or "NRaum") e.Cancel = true;
        };
        _g.EditingControlShowing += EditorZeigen;
        Steuerelemente.SuchKombi.Aktivieren(_g, VertreterStil);
        // Auswahl aus der Liste (Maus bzw. Enter) wie im Vorbild sofort übernehmen: Bearbeitung beenden, die Zeile bleibt aktuell
        _g.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_g.CurrentCell is DataGridViewComboBoxCell && _g.EditingControl is ComboBox { DroppedDown: false } && !_g.IsCurrentCellDirty)
                BeginInvoke(() => { if (!IsDisposed && _g.IsCurrentCellInEditMode && _g.CurrentCell is DataGridViewComboBoxCell) _g.EndEdit(); });
        };
        _g.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;
            SortierenNach(_g.Columns[e.ColumnIndex].Name);
        };
        // Auswahl verzögert weitergeben: beim schnellen Blättern mit den Pfeiltasten werden die Pläne erst nach dem Anhalten aufgebaut
        _auswahlTimer.Tick += (_, _) => { _auswahlTimer.Stop(); if (!IsDisposed) Gewaehlt(); };
        _g.SelectionChanged += (_, _) =>
        {
            if (_laedt) return;
            if (!_nr.Focused) _nr.Text = (_g.CurrentRow?.Tag as Vertretung)?.Id.ToString() ?? "";
            _auswahlTimer.Stop();
            _auswahlTimer.Start();
        };
        // erst beim Wechsel in eine andere Zeile verschwinden bearbeitete Zeilen, die nicht mehr zum Filter passen
        _g.CurrentCellChanged += (_, _) =>
        {
            if (!_laedt && _g.CurrentRow?.Tag is Vertretung cur && _behalten.Zeilenwechsel(cur.Id)) NachZellwechsel(Neuaufbau);
        };
        _g.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            // zusammengefasste Entfälle einer Veranstaltung: Doppelklick zeigt die Stunden einzeln (statt Vertretungsvorschlag)
            if (_g.Rows[e.RowIndex].Tag is Vertretung { Id: var id } && _vaSammel.Contains(id)) { _vaSammeln.Checked = false; return; }
            if (e.ColumnIndex < 0 || _g.Columns[e.ColumnIndex].ReadOnly) VorschlagOeffnen();
        };
        _g.KeyPress += TastenDruck;
        _g.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !_g.IsCurrentCellInEditMode) { Entfernen(); e.Handled = true; }
            if (e.KeyCode == Keys.Enter && !_g.IsCurrentCellInEditMode) { VorschlagOeffnen(); e.Handled = true; }
            if (e.KeyCode == Keys.F3) { NaechsteOffene(e.Shift ? -1 : 1); e.Handled = true; }
        };
        _g.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _g.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && !_g.Rows[hit.RowIndex].Selected)
            {
                _g.ClearSelection();
                _g.Rows[hit.RowIndex].Selected = true;
                _g.CurrentCell = _g.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
            }
            Kontextmenue(e.Location);
        };

        _zeitraum.Geaendert += ZeitraumGeaendert;
        _offen.CheckedChanged += (_, _) => { _behalten.Leeren(); Neuaufbau(true); };
        _kollisionen.CheckedChanged += (_, _) => { _behalten.Leeren(); Neuaufbau(true); };
        _vaSammeln.CheckedChanged += (_, _) =>
        {
            AppZustand.Einstellungen.VeranstaltungenZusammenfassen = _vaSammeln.Checked;
            AppZustand.Einstellungen.Speichern();
            Neuaufbau(true);
        };
        _element.SelectedIndexChanged += (_, _) => { if (_laedt) return; _behalten.Leeren(); Neuaufbau(true); };
        _ansicht.SelectedIndexChanged += (_, _) =>
        {
            AppZustand.Einstellungen.Vertretungsansicht = Ansichten[_ansicht.SelectedIndex];
            AppZustand.Einstellungen.Speichern();
            Text = "Vertretungen / " + Ansichten[_ansicht.SelectedIndex].Replace(" - ", " – ");
            ElementeFuellen();
            _elementGruppe.Text = _ansicht.SelectedIndex is 1 or 3 ? "Vertreter" : "Klasse(n)";
            if (_inhalt.Checked) _inhalt.Checked = false;
            _behalten.Leeren();
            _sortSpalte = null;
            Neuaufbau(true);
            FelderAnwenden();
        };
        _nr.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !int.TryParse(_nr.Text, out var nr)) return;
            e.SuppressKeyPress = true;
            Springen(nr);
        };

        Controls.Add(_g);
        Controls.Add(filter);
        Controls.Add(unten);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.VertretungGewaehlt += FremdGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.VertretungGewaehlt -= FremdGewaehlt;
        };
        Shown += (_, _) => { ZeitraumGeaendert(); FelderAnwenden(); };
    }

    /// <summary>
    /// Felder der Ansicht (wie im Vorbild): Spaltenname, Überschrift, Bezeichnung im Dialog „Felder der Ansicht“, Breite, nur lesen,
    /// standardmäßig aktiv, Anzeige wahlweise als Kurz- oder Langname.
    /// </summary>
    internal static readonly (string Name, string Kopf, string Feld, int Breite, bool Lesbar, bool Standard, bool Lang)[] Spalten =
    {
        ("Nr", "Vtr-Nr.", "Vertretungsnummer", 46, true, true, false), ("Art", "Art", "Art der Vertretung", 78, true, true, false),
        ("Datum", "Datum", "Datum (TT.MM.)", 44, true, true, false), ("Tag", "Tag", "Tag", 30, true, false, false),
        ("Std", "Stunde", "Std.", 40, true, true, false), ("Zeit", "Zeit", "Beginn- und Endzeit", 76, true, false, false),
        ("Fach", "(Fach)", "statt Fach", 46, true, true, true), ("NFach", "Fach", "Fach", 46, false, true, true),
        ("Lehrer", "(Lehrer)", "statt Lehrer", 46, true, true, true), ("Vertreter", "Vertreter", "Vertreter", 46, false, true, true),
        ("AKlassen", "(Klasse(n))", "statt Klasse(n)", 46, true, true, true), ("Klassen", "Klasse(n)", "Klasse(n)", 46, true, true, true),
        ("Raum", "(Raum)", "statt Raum", 46, true, true, true), ("NRaum", "Raum", "Raum", 46, false, true, true),
        ("Von", "Vertr. von", "Verlegung", 68, true, true, false), ("Nach", "(Le.) nach", "Wegverlegung", 68, true, true, false),
        ("Text", "Text", "Vertretungs-Text", 200, false, true, false), ("Kz", "Statistik", "Statistik-Kennzeichen", 50, false, true, false),
        ("SG", "Schülergruppe", "Schülergruppe", 110, false, true, false), ("Grund", "Absenzgrund", "Absenzgrund", 80, true, false, true),
        ("UText", "Unt.-Text", "Unterrichts-Text", 90, true, false, false), ("UZText", "Unt.-Zeilentext", "Unterrichts-Zeilentext", 90, true, false, false),
        ("Beschr", "Beschr.", "Beschr.", 70, false, false, false), ("Entf", "Entfall", "Entfall", 44, true, false, false),
        ("Mitb", "Mitbetr.", "Mitbetreuung", 50, true, false, false), ("Kopp", "Vtr-Kopplung", "Vertretungs-Kopplung", 50, false, false, false),
        ("Neu", "Neu", "Neue Vertretung", 36, true, false, false), ("Fix", "(X)", "(X) Fixiert", 30, true, false, false),
        ("ND", "(N)", "(N) Nicht Drucken", 30, true, false, false), ("Text2", "Text 2", "Vertretungs-Text 2", 120, false, false, false),
        ("Ben", "Benachr.", "Benachrichtigen", 54, true, false, false), ("Zaehler", "Zähler", "Zähler", 46, true, false, false),
        ("Unterschrift", "Unterschrift", "Unterschrift", 80, true, false, false), ("Wert", "Wert", "Wert bzw. Faktor", 46, false, false, false),
        ("Abt", "Abteilung", "Abteilung", 70, true, false, true), ("Benutzer", "Benutzer", "Benutzer", 70, true, false, false),
        ("Mehr", "Mehrarb.", "Mehrarbeit", 54, true, false, false),
    };

    private string AnsichtName => Ansichten[Math.Max(0, _ansicht.SelectedIndex)];

    /// <summary>Gespeicherte Felder der Ansicht: Feld → Kennzeichen ("a" aktiv, "d" drucken, "L" Langname); fehlt = Standard.</summary>
    private Dictionary<string, string> FeldEinstellungen()
    {
        if (!AppZustand.Einstellungen.VertretungsFelder.TryGetValue(AnsichtName, out var f))
            f = Spalten.ToDictionary(s => s.Name, s => s.Standard ? "ad" : "");
        foreach (var s in Spalten.Where(s => !f.ContainsKey(s.Name))) f[s.Name] = s.Standard ? "ad" : "";
        f["Nr"] = "a" + (f["Nr"].Contains('d') ? "d" : "") + (f["Nr"].Contains('L') ? "L" : ""); // am Bildschirm nicht ausblendbar
        return f;
    }

    private bool Langname(string spalte) => (_felder ?? FeldEinstellungen()).TryGetValue(spalte, out var x) && x.Contains('L');

    /// <summary>Spalten laut „Felder der Ansicht“ ein- bzw. ausblenden.</summary>
    private void FelderAnwenden()
    {
        if (!IsHandleCreated || _inhalt.Checked) return;
        var f = FeldEinstellungen();
        foreach (var s in Spalten)
            Steuerelemente.TabellenFormate.SpalteSichtbar(_g, _g.Columns[s.Name]!, f[s.Name].Contains('a'));
    }

    /// <summary>Dialog „Felder der Ansicht“: Feld / aktiv / drucken / Anzeige von (Kurzname, Langname); Ok, Übernahme, Abbrechen.</summary>
    private void FelderDialog()
    {
        var f = FeldEinstellungen();
        using var dlg = new Form
        {
            Text = "Felder der Ansicht", Font = Ui.Schrift, FormBorderStyle = FormBorderStyle.SizableToolWindow, StartPosition = FormStartPosition.CenterParent,
            Size = new Size(380, 640), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Ui.Kopf,
        };
        var oben = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 0, 0), WrapContents = false };
        var ok = new Button { Text = "Ok", Width = 80, Height = 24 };
        var uebernahme = new Button { Text = "Übernahme", Width = 80, Height = 24 };
        var abbrechen = new Button { Text = "Abbrechen", Width = 80, Height = 24, DialogResult = DialogResult.Cancel };
        oben.Controls.AddRange(new Control[] { ok, uebernahme, abbrechen });
        var t = new DataGridView { Dock = DockStyle.Fill };
        Ui.RasterGestalten(t, true);
        t.AllowUserToAddRows = t.AllowUserToDeleteRows = false;
        t.RowHeadersVisible = false;
        t.Columns.Add(new DataGridViewTextBoxColumn { Name = "F", HeaderText = "Feld", Width = 180, ReadOnly = true });
        t.Columns.Add(new DataGridViewCheckBoxColumn { Name = "A", HeaderText = "aktiv", Width = 44 });
        t.Columns.Add(new DataGridViewCheckBoxColumn { Name = "D", HeaderText = "drucken", Width = 52 });
        t.Columns.Add(new DataGridViewComboBoxColumn { Name = "L", HeaderText = "Anzeige von", Width = 80, FlatStyle = FlatStyle.Flat, DisplayStyleForCurrentCellOnly = true, DataSource = new[] { "", "Kurzname", "Langname" } });
        t.DataError += (_, e) => e.ThrowException = false;
        t.CurrentCellDirtyStateChanged += (_, _) => { if (t.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell) t.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        foreach (var s in Spalten)
        {
            var x = f[s.Name];
            var r = t.Rows.Add(s.Feld, x.Contains('a'), x.Contains('d'), s.Lang ? (x.Contains('L') ? "Langname" : "Kurzname") : "");
            t.Rows[r].Tag = s.Name;
            if (s.Standard) t.Rows[r].Cells[0].Style.Font = Ui.SchriftFett;
            if (!s.Lang) t.Rows[r].Cells["L"].ReadOnly = true;
            // Vertretungsnummer kann am Bildschirm nicht ausgeblendet werden (für den Druck schon)
            if (s.Name == "Nr") { t.Rows[r].Cells["A"].ReadOnly = true; t.Rows[r].Cells["A"].Style.BackColor = Color.Gainsboro; }
        }
        void Uebernehmen()
        {
            t.EndEdit();
            var neu = new Dictionary<string, string>();
            foreach (DataGridViewRow r in t.Rows)
                neu[(string)r.Tag!] = (r.Cells["A"].Value is true ? "a" : "") + (r.Cells["D"].Value is true ? "d" : "") + (r.Cells["L"].Value as string == "Langname" ? "L" : "");
            AppZustand.Einstellungen.VertretungsFelder[AnsichtName] = neu;
            AppZustand.Einstellungen.Speichern();
            if (_inhalt.Checked) _inhalt.Checked = false;
            FelderAnwenden();
            Neuaufbau(true);
        }
        ok.Click += (_, _) => { Uebernehmen(); dlg.DialogResult = DialogResult.OK; };
        uebernahme.Click += (_, _) => Uebernehmen();
        dlg.CancelButton = abbrechen;
        dlg.Controls.Add(t);
        dlg.Controls.Add(oben);
        dlg.ShowDialog(this);
    }

    /// <summary>Felder mit Inhalt: alle Spalten zeigen, die eine Eingabe enthalten; erneuter Klick stellt die vorherigen Spalten wieder her.</summary>
    private void FelderMitInhalt(bool an)
    {
        if (an)
        {
            _vorInhalt = _g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Select(c => c.Name).ToHashSet();
            foreach (DataGridViewColumn c in _g.Columns)
            {
                var inhalt = c.Name == "Nr" || _g.Rows.Cast<DataGridViewRow>().Any(r => r.Cells[c.Index].Value is { } v && v.ToString() is { Length: > 0 } s && s != "False");
                if (inhalt && !c.Visible) Steuerelemente.TabellenFormate.SpalteSichtbar(_g, c, true);
            }
        }
        else if (_vorInhalt != null)
        {
            foreach (DataGridViewColumn c in _g.Columns) Steuerelemente.TabellenFormate.SpalteSichtbar(_g, c, _vorInhalt.Contains(c.Name));
            _vorInhalt = null;
        }
    }

    /// <summary>Einstellungen des Vertretungsfensters: angezeigte Vertretungsarten (je Ansicht) und regulären Unterricht anzeigen.</summary>
    private void FensterEinstellungen()
    {
        using var dlg = new Dialog("Einstellungen – Vertretungen / " + AnsichtName.Replace(" - ", " – "), 380, 560);
        dlg.Controls.Add(new Label { Text = "Diese Vertretungsarten werden angezeigt:", Left = 12, Top = 10, AutoSize = true });
        var liste = new CheckedListBox { Left = 12, Top = 32, Width = 350, Height = 400, CheckOnClick = true, IntegralHeight = false };
        var arten = Enum.GetValues<VertretungsArt>().OrderBy(a => Vertretungsplan.ArtText(D, a), StringComparer.Create(Format.De, true)).ToList();
        var aus = AppZustand.Einstellungen.VertretungsArtenAus.GetValueOrDefault(AnsichtName) ?? new List<string>();
        foreach (var a in arten) liste.Items.Add(Vertretungsplan.ArtText(D, a), !aus.Contains(a.ToString()) && (a != VertretungsArt.Unterricht || AppZustand.Einstellungen.VertretungenUnterrichtZeigen));
        dlg.Controls.Add(liste);
        dlg.Controls.Add(new Label
        {
            Text = "„Unterricht“ blendet auch den regulär stattfindenden Unterricht ein. Bezeichnung und Farbe der Vertretungsarten: Einstellungen › Farben und Bezeichnungen.",
            Left = 12, Top = 438, Width = 350, Height = 44, ForeColor = Color.DimGray,
        });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var neu = new List<string>();
        for (var i = 0; i < arten.Count; i++)
        {
            if (arten[i] == VertretungsArt.Unterricht) { AppZustand.Einstellungen.VertretungenUnterrichtZeigen = liste.GetItemChecked(i); continue; }
            if (!liste.GetItemChecked(i)) neu.Add(arten[i].ToString());
        }
        AppZustand.Einstellungen.VertretungsArtenAus[AnsichtName] = neu;
        AppZustand.Einstellungen.Speichern();
        Neuaufbau(true);
    }

    /// <summary>Liste drucken: Spalten laut „Felder der Ansicht“ (drucken) – auch die Vtr-Nr. kann für den Druck ausgeblendet werden.</summary>
    private void ListeDrucken()
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        var f = FeldEinstellungen();
        var vorher = _g.Columns.Cast<DataGridViewColumn>().ToDictionary(c => c, c => c.Visible);
        try
        {
            foreach (var s in Spalten) _g.Columns[s.Name]!.Visible = f[s.Name].Contains('d');
            if (!_g.Columns.Cast<DataGridViewColumn>().Any(c => c.Visible)) _g.Columns["Art"]!.Visible = true;
            // Unterschrift: Leerfeld zum Abzeichnen; Seitenlayout „Nur neue Vertretungen“
            var nurNeue = AppZustand.Einstellungen.Vertretungsdruck.NurNeue;
            ListenDruck.DruckenTabelle(this, Text + (nurNeue ? " (nur neue)" : ""), _g, true, new HashSet<string> { _g.Columns["Unterschrift"]!.HeaderText },
                nurNeue ? r => r.Tag is Vertretung v && Vertretungen.IstNeu(v, Doc.Sitzungsbeginn) : null);
        }
        finally
        {
            foreach (var (c, v) in vorher) c.Visible = v;
        }
    }

    private bool ArtSichtbar(VertretungsArt a) =>
        a == VertretungsArt.Unterricht ? AppZustand.Einstellungen.VertretungenUnterrichtZeigen
            : !(AppZustand.Einstellungen.VertretungsArtenAus.TryGetValue(AnsichtName, out var aus) && aus.Contains(a.ToString()));

    public void AnsichtSetzen(string ansicht)
    {
        var i = Array.IndexOf(Ansichten, ansicht);
        if (i >= 0) _ansicht.SelectedIndex = i;
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        _plaene.Clear();
        _kollisionen2.Clear();
        Neuaufbau();
    }

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private void DatumGewechselt(DateOnly datum)
    {
        if (!_sync || IsDisposed) return;
        _laedt = true;
        _von.Value = _bis.Value = datum.ToDateTime(TimeOnly.MinValue);
        _laedt = false;
        ZeitraumGeaendert();
    }

    private void FremdGewaehlt(Vertretung? v)
    {
        if (v is null || _laedt || IsDisposed || ContainsFocus || _g.IsCurrentCellInEditMode) return;
        if (_behalten.Zeilenwechsel(v.Id)) Neuaufbau();
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Vertretung x && x.Id == v.Id)
            {
                _laedt = true;
                _g.ClearSelection();
                r.Selected = true;
                _g.CurrentCell = r.Cells[0];
                _laedt = false;
                return;
            }
    }

    /// <summary>Beim Zeitraumwechsel offene Vertretungen aus Absenzen, Veranstaltungen und Klausuren erzeugen (nur wenn sich etwas ändert).</summary>
    private void ZeitraumGeaendert()
    {
        if (_laedt) return;
        var von = Von;
        var bis = Bis;
        if (bis.DayNumber - von.DayNumber <= 31)
        {
            // Probeberechnung auf einer leichten Kopie (nur Vertretungen des Zeitraums und betroffene Veranstaltungen) statt der ganzen Datei
            var probe = Dokument.VertretungsProbe(D, von, bis);
            Vertretungen.Aktualisieren(probe, von, bis);
            if (Signatur(probe, von, bis) != Signatur(D, von, bis))
                Aendern($"Vertretungen {Format.Datum(von)}{(von == bis ? "" : " – " + Format.Datum(bis))} aktualisiert", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, von, bis));
        }
        if (von == bis && AppZustand.Datum != von)
        {
            _sync = false;
            AppZustand.DatumSetzen(von);
            _sync = true;
        }
        _plaene.Clear();
        _behalten.Leeren();
        Neuaufbau(true);
    }

    private static string Signatur(Schuldatei d, DateOnly von, DateOnly bis) =>
        string.Join(";", d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).Select(v => $"{v.UnterrichtId}/{v.ZeilenIndex}/{v.Datum}/{v.Stunde}/{v.Art}/{v.VertreterId}/{v.GangId}").OrderBy(s => s));

    private Tagesplan Plan(DateOnly datum)
    {
        if (!_plaene.TryGetValue(datum, out var tp)) _plaene[datum] = tp = Tagesplan.Berechnen(D, datum);
        return tp;
    }

    /// <summary>
    /// Kollisionen je Zeile zwischengespeichert: Formatierung und Zeichnen fragen sie für jede sichtbare Zelle bei jedem Neuzeichnen ab
    /// (bei großen Listen tausendfach je Bildlauf); verworfen bei jedem Neuaufbau bzw. jeder Änderung.
    /// </summary>
    private readonly Dictionary<Vertretung, (string? Text, bool Lehrer, bool Raum)> _kollisionen2 = new(ReferenceEqualityComparer.Instance);

    private (string? Text, bool Lehrer, bool Raum) KollisionsInfo(Vertretung v)
    {
        if (_kollisionen2.TryGetValue(v, out var k)) return k;
        var text = KollisionBerechnen(v);
        var (l, r) = text is null ? (false, false) : KollisionsFelderBerechnen(v);
        return _kollisionen2[v] = (text, l, r);
    }

    private string? Kollision(Vertretung v) => KollisionsInfo(v).Text;

    private (bool Lehrer, bool Raum) KollisionsFelder(Vertretung v)
    {
        var k = KollisionsInfo(v);
        return (k.Lehrer, k.Raum);
    }

    /// <summary>Kollision: Vertreter ist zur Stunde anderweitig belegt bzw. abwesend, Raum doppelt belegt.</summary>
    private string? KollisionBerechnen(Vertretung v)
    {
        if (v.Id == 0) return null;
        if (_gruppen.TryGetValue(v.Id, out var gruppe) && gruppe.Count > 1)
            return gruppe.Select(KollisionEinzeln).FirstOrDefault(k => k != null);
        return KollisionEinzeln(v);
    }

    /// <summary>Art der Kollision für die Hervorhebung: Lehrer (Vertreter-Feld) oder Raum.</summary>
    private (bool Lehrer, bool Raum) KollisionsFelderBerechnen(Vertretung v)
    {
        var liste = _gruppen.TryGetValue(v.Id, out var g) ? g : new List<Vertretung> { v };
        var lehrer = liste.Any(x => !x.IstEntfall && x.VertreterId is int l && x.Art != VertretungsArt.TrotzAbsenz && !x.Mitbetreuung && Vertretungen.LehrerBelegt(D, Plan(x.Datum), l, x.Stunde, x) != null);
        var raum = liste.Any(x => !x.IstEntfall && x.Art != VertretungsArt.Pausenaufsicht && (x.VertretungsraumId ?? x.RaumId) is int r && Plan(x.Datum).Stunde(x.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(r)) > 1);
        return (lehrer, raum);
    }

    private string? KollisionEinzeln(Vertretung v)
    {
        if (v.IstEntfall) return null;
        if (v.VertreterId is int l && Vertretungen.LehrerBelegt(D, Plan(v.Datum), l, v.Stunde, v) is { } g && v.Art != VertretungsArt.TrotzAbsenz && !v.Mitbetreuung) return g;
        var raum = v.VertretungsraumId ?? v.RaumId;
        if (raum is int r && v.Art != VertretungsArt.Pausenaufsicht &&
            Plan(v.Datum).Stunde(v.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(r)) > 1) return $"Raum {Ix.KRaum(r)} doppelt belegt";
        return null;
    }

    private bool Passt(Vertretung v)
    {
        // Ansicht „Pausenaufsichten“ zeigt nur diese; in den übrigen Ansichten stehen sie wie im Vorbild mit in der Liste
        if (_ansicht.SelectedIndex == 4 && v.Art != VertretungsArt.Pausenaufsicht) return false;
        // gerade bearbeitete Zeile bleibt bis zum Zeilenwechsel sichtbar, auch wenn sie nicht mehr zum Filter passt
        var behalten = _behalten.Enthaelt(v.Id);
        if (_offen.Checked && !v.OffenOderRaumOffen && !behalten) return false;
        if (_kollisionen.Checked && KollisionEinzeln(v) is null && !behalten) return false;
        // wie im Vorbild: Veranstaltungen selbst stehen nicht in der Vertretungsliste, nur die zugehörigen Freisetzungen/Entfälle
        if (v.Art == VertretungsArt.Veranstaltung) return false;
        if (!ArtSichtbar(v.Art)) return false;
        // Bereitschaften und Unterricht, der nicht vertreten wird, stehen nicht in der Vertretungsliste
        if (Vertretungen.NichtInListe(D, v) && !behalten) return false;
        if (_element.SelectedItem is Eintrag { Id: int id } && !behalten)
        {
            var treffer = _ansicht.SelectedIndex is 1 or 3
                ? v.LehrerId == id || v.VertreterId == id
                : v.NeueKlassenIds.Contains(id) || v.KlassenIds.Contains(id);
            if (!treffer) return false;
        }
        return true;
    }

    private void Neuaufbau() => Neuaufbau(false);

    /// <summary>Schlüssel einer Tabellenzeile: Vtr-Nr., bei reinen Unterrichtszeilen Unterricht/Zeile/Datum/Stunde.</summary>
    private static string Schluessel(Vertretung? v) =>
        v is null ? "" : v.Id != 0 ? "V" + v.Id : $"U{v.UnterrichtId}/{v.ZeilenIndex}/{v.Datum:yyyyMMdd}/{v.Stunde}";

    private static string? Schluessel(DataGridViewRow r) => r.Tag is Vertretung v ? Schluessel(v) : null;

    /// <summary>Filter, unter dem eine bearbeitete Zeile verschwinden könnte (dann bleibt sie bis zum Zeilenwechsel stehen).</summary>
    private bool FilterAktiv => _offen.Checked || _kollisionen.Checked || _element.SelectedItem is Eintrag { Id: not null } || _ansicht.SelectedIndex is 2 or 3;

    /// <summary>
    /// Liste aktualisieren. Sind es dieselben Zeilen wie bisher, werden nur die Werte an Ort und Stelle erneuert: Zeile, Spalte,
    /// Markierung und Bildlauf bleiben, nichts springt (<paramref name="neuSortieren"/> erzwingt die Sortierung, z. B. nach einem
    /// Filter- oder Ansichtswechsel). Sonst Neuaufbau mit Wiederherstellung der aktuellen Vertretung (Vtr-Nr.), Spalte und
    /// Bildlaufposition. Während eine Zelle bearbeitet wird, wird nicht neu aufgebaut, sondern beim Verlassen der Zelle nachgeholt.
    /// </summary>
    private void Neuaufbau(bool neuSortieren)
    {
        if (IsDisposed) return;
        if (_g.IsCurrentCellInEditMode)
        {
            _neuaufbauAusstehend = true;
            _sortierenAusstehend |= neuSortieren;
            return;
        }
        neuSortieren |= _sortierenAusstehend;
        _neuaufbauAusstehend = _sortierenAusstehend = false;
        var vorher = AuswahlSignatur();
        _kollisionen2.Clear();
        _gruppen.Clear();
        _vaSammel.Clear();
        _weg.Clear();
        _felder = FeldEinstellungen();
        List<Vertretung> liste;
        try { liste = Zeilenliste(); }
        finally { _felder = null; }
        var alt = _g.Rows.Cast<DataGridViewRow>().Select(r => Schluessel(r) ?? "").ToList();
        var amOrt = Listenabgleich.AmOrt(alt, liste, Schluessel, neuSortieren);
        _laedt = true;
        try
        {
            if (amOrt != null)
                for (var i = 0; i < amOrt.Count; i++) ZeileSetzen(_g.Rows[i], amOrt[i]);
            else
            {
                var stand = Steuerelemente.TabellenStand.Merken(_g, Schluessel);
                _g.Rows.Clear();
                foreach (var v in liste) ZeileSetzen(_g.Rows[_g.Rows.Add()], v);
                stand.Wiederherstellen(_g, Schluessel);
            }
        }
        finally
        {
            _laedt = false;
        }
        SortGlyphen();
        if (!_nr.Focused) _nr.Text = (_g.CurrentRow?.Tag as Vertretung)?.Id.ToString() ?? "";
        _status.Text = "";
        // andere Fenster (Vorschlag, Stundenpläne) nur benachrichtigen, wenn sich die Auswahl tatsächlich geändert hat
        if (AuswahlSignatur() != vorher) Gewaehlt();
    }

    private string AuswahlSignatur() =>
        Schluessel(_g.CurrentRow?.Tag as Vertretung) + "|" + string.Join(",", _g.SelectedRows.Cast<DataGridViewRow>().Select(r => Schluessel(r)).OrderBy(x => x));

    /// <summary>Zeilen der Liste: Filter, Veranstaltungen je Tag zusammengefasst, sortiert (Standard oder nach gewählter Spalte).</summary>
    private List<Vertretung> Zeilenliste()
    {
        var lehrerSicht = _ansicht.SelectedIndex is 1 or 3;
        var druck = _ansicht.SelectedIndex is 2 or 3;
        IEnumerable<Vertretung> liste = TauschZusammenfassen(Zusammenfassen(Vertretungen.ImZeitraum(D, Von, Bis).Where(Passt).ToList()));
        if (druck) liste = liste.Where(v => !v.NichtDrucken || _behalten.Enthaelt(v.Id));
        if (AppZustand.Einstellungen.VertretungenUnterrichtZeigen && _ansicht.SelectedIndex != 4 && !_offen.Checked && !_kollisionen.Checked) liste = liste.Concat(Unterrichtszeilen());
        var zeilen = liste.ToList();
        var vergleich = StringComparer.Create(Format.De, true);
        if (_sortSpalte is { } sp && _g.Columns.Contains(sp))
        {
            var i = _g.Columns[sp]!.Index;
            var werte = zeilen.ToDictionary(v => v, v => Werte(v)[i]?.ToString() ?? "");
            var sortiert = sp switch
            {
                "Nr" => Richtung(zeilen, v => v.Id),
                "Datum" or "Tag" => Richtung(zeilen, v => v.Datum).ThenBy(v => v.Stunde),
                "Std" or "Zeit" => Richtung(zeilen, v => v.Stunde).ThenBy(v => v.Datum),
                _ => _sortAbsteigend ? zeilen.OrderByDescending(v => werte[v], vergleich) : zeilen.OrderBy(v => werte[v], vergleich),
            };
            return sortiert.ThenBy(v => v.Datum).ThenBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
        }
        var standard = lehrerSicht
            ? zeilen.OrderBy(v => v.Datum).ThenBy(v => Ix.KLehrer(v.VertreterId ?? v.LehrerId), vergleich).ThenBy(v => v.Stunde)
            : zeilen.OrderBy(v => v.Datum).ThenBy(v => Ix.KKlassen(v.NeueKlassenIds), vergleich).ThenBy(v => v.Stunde);
        return standard.ThenBy(v => v.Id).ToList();
    }

    private IOrderedEnumerable<Vertretung> Richtung<T>(IEnumerable<Vertretung> liste, Func<Vertretung, T> schluessel) =>
        _sortAbsteigend ? liste.OrderByDescending(schluessel) : liste.OrderBy(schluessel);

    /// <summary>Klick auf eine Spaltenüberschrift: nach dieser Spalte sortieren, erneuter Klick kehrt die Richtung um (wie im Vorbild).</summary>
    private void SortierenNach(string spalte)
    {
        if (_g.IsCurrentCellInEditMode && !_g.EndEdit()) return;
        if (_sortSpalte == spalte) _sortAbsteigend = !_sortAbsteigend;
        else
        {
            _sortSpalte = spalte;
            _sortAbsteigend = false;
        }
        Neuaufbau(true);
    }

    private void SortGlyphen()
    {
        foreach (DataGridViewColumn c in _g.Columns)
        {
            var soll = c.Name == _sortSpalte ? (_sortAbsteigend ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            if (c.HeaderCell.SortGlyphDirection != soll) c.HeaderCell.SortGlyphDirection = soll;
        }
    }

    /// <summary>
    /// Werte einer Zeile setzen – nur Zellen, deren Inhalt sich geändert hat (die aktuelle Zelle behält Fokus und Markierung).
    /// Werte außerhalb der Auswahlliste (Veranstaltung „Cer,New,?“, Langnamen) stehen in einer Textzelle.
    /// </summary>
    private void ZeileSetzen(DataGridViewRow zeile, Vertretung v)
    {
        zeile.Tag = v;
        var werte = Werte(v);
        for (var i = 0; i < werte.Length && i < zeile.Cells.Count; i++)
        {
            var neu = werte[i];
            var zelle = zeile.Cells[i];
            if (_g.Columns[i] is DataGridViewComboBoxColumn kombi)
            {
                var text = neu?.ToString() ?? "";
                if (zelle is DataGridViewComboBoxCell && !kombi.Items.Contains(text))
                {
                    ZelleErsetzen(zeile, i, new DataGridViewTextBoxCell { Value = text });
                    continue;
                }
                neu = text;
            }
            if ((zelle.Value?.ToString() ?? "") != (neu?.ToString() ?? "")) zelle.Value = neu;
        }
    }

    /// <summary>Auswahlzelle durch eine Textzelle ersetzen; ist sie die aktuelle, bleibt sie es.</summary>
    private void ZelleErsetzen(DataGridViewRow zeile, int spalte, DataGridViewCell neu)
    {
        var aktuell = zeile.Index >= 0 && _g.CurrentCell is { } c && c.RowIndex == zeile.Index && c.ColumnIndex == spalte;
        zeile.Cells[spalte] = neu;
        if (aktuell && _g.Columns[spalte].Visible) _g.CurrentCell = zeile.Cells[spalte];
    }

    /// <summary>Zeilen einer Veranstaltung je Tag zu einer Zeile zusammenfassen (Stunde „1-8“, Vertreter „Cer,New,?“).</summary>
    private List<Vertretung> Zusammenfassen(List<Vertretung> liste)
    {
        var res = new List<Vertretung>();
        var sammeln = _vaSammeln.Checked;
        foreach (var g in liste.GroupBy(v => v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int va ? $"VA{va}/{v.Datum}"
                     : sammeln && Veranstaltungsfolge(v) ? $"VE{v.VeranstaltungId}/{v.Datum}/{v.Art}/{v.Text}" : $"#{v.Id}"))
        {
            var zeilen = g.OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
            // Entfälle/Freisetzungen einer Veranstaltung: erst ab einigen Zeilen zusammenfassen, sonst wie bisher einzeln
            if (g.Key.StartsWith("VE", StringComparison.Ordinal) && zeilen.Count < VaSammelAb)
            {
                res.AddRange(zeilen);
                continue;
            }
            res.Add(zeilen[0]);
            if (zeilen.Count > 1 || zeilen[0].Art == VertretungsArt.Veranstaltung) _gruppen[zeilen[0].Id] = zeilen;
            if (g.Key.StartsWith("VE", StringComparison.Ordinal)) _vaSammel.Add(zeilen[0].Id);
        }
        return res;
    }

    /// <summary>
    /// Entfall bzw. Freisetzung, die eine Veranstaltung auslöst (Unterricht findet als Veranstaltung statt, Klasse nimmt teil) – wird bei
    /// vielen Stunden je Tag zusammengefasst (gleicher Text). Fixierte, verlegte bzw. gerade bearbeitete Zeilen bleiben einzeln.
    /// </summary>
    private bool Veranstaltungsfolge(Vertretung v) =>
        v.VeranstaltungId.HasValue && v.Quelle == VertretungsQuelle.Veranstaltung && v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung
        && !v.Fixiert && v.NachDatum is null && !_behalten.Enthaelt(v.Id);

    /// <summary>Anzeige einer zusammengefassten Zeile: Klassen und Lehrkräfte als „Alle …“ bzw. Liste, Fach/Raum nur, wenn überall gleich.</summary>
    private void SammelWerte(object?[] werte, Vertretung v, List<Vertretung> gruppe)
    {
        var va = D.Veranstaltungen.FirstOrDefault(x => x.Id == v.VeranstaltungId);
        void Setzen(string spalte, object? wert)
        {
            var i = _g.Columns[spalte]?.Index ?? -1;
            if (i >= 0 && i < werte.Length) werte[i] = wert;
        }
        string Liste(ElementTyp typ, IEnumerable<int> ids) => VeranstaltungsAuswahl.Kurztext(D, typ, ids.Distinct().ToList(), va?.Von, va?.Bis, ", ");
        string Gleich(Func<Vertretung, int?> wert, ElementTyp typ) => gruppe.Select(wert).Distinct().ToList() is { Count: 1 } l && l[0] is int id ? Ix.K(typ, id) : "";
        var klassen = Liste(ElementTyp.Klasse, gruppe.SelectMany(x => x.KlassenIds));
        var lehrer = Liste(ElementTyp.Lehrer, gruppe.Where(x => x.LehrerId.HasValue).Select(x => x.LehrerId!.Value));
        Setzen("Art", $"{Vertretungsplan.ArtText(D, v.Art)} ({gruppe.Count})");
        Setzen("AKlassen", klassen);
        Setzen("Klassen", v.Art == VertretungsArt.Freisetzung && klassen.Length > 0 ? $"({klassen})" : klassen);
        Setzen("Lehrer", lehrer);
        Setzen("Vertreter", v.Art == VertretungsArt.Freisetzung ? lehrer : "---");
        Setzen("Fach", Gleich(x => x.FachId, ElementTyp.Fach));
        Setzen("Raum", Gleich(x => x.RaumId, ElementTyp.Raum));
        Setzen("UText", "");
        Setzen("UZText", "");
        if (va != null && string.IsNullOrWhiteSpace(v.Text)) Setzen("Text", va.Text);
    }

    /// <summary>Arten, bei denen Unterricht an eine andere Position kommt (geänderte Felder magenta wie im Vorbild).</summary>
    private static bool IstVerlegung(Vertretung v) => v.Art is VertretungsArt.Tausch or VertretungsArt.Verlegung or VertretungsArt.Verlegt;

    /// <summary>
    /// Wie Vorbild: an einer Position, von der Unterricht weg- und auf die anderer Unterricht hinverlegt wurde (Stundentausch),
    /// steht nur eine Zeile „Tausch“ bzw. „Verlegung“ – in Klammern der bisherige, daneben der neue Unterricht,
    /// „Vertr. von“ und „(Le.) nach“ in derselben Zeile. Bearbeitet wird die ankommende Stunde.
    /// </summary>
    private List<Vertretung> TauschZusammenfassen(List<Vertretung> liste)
    {
        var weg = liste.Where(q => q.Art == VertretungsArt.Entfall && q.NachDatum.HasValue && q.VerlegungsId.HasValue && !_behalten.Enthaelt(q.Id))
            .GroupBy(q => (q.Datum, q.Stunde)).ToDictionary(g => g.Key, g => g.ToList());
        if (weg.Count == 0) return liste;
        var versteckt = new HashSet<int>();
        foreach (var z in liste.Where(z => IstVerlegung(z) && z.VonDatum.HasValue && z.VerlegungsId.HasValue))
        {
            if (!weg.TryGetValue((z.Datum, z.Stunde), out var kandidaten)) continue;
            var q = kandidaten.FirstOrDefault(q => !versteckt.Contains(q.Id) && q.VerlegungsId != z.VerlegungsId && q.KlassenIds.ToHashSet().SetEquals(z.NeueKlassenIds));
            if (q is null) continue;
            versteckt.Add(q.Id);
            _weg[z.Id] = q;
        }
        return liste.Where(v => !versteckt.Contains(v.Id)).ToList();
    }

    /// <summary>Regulär stattfindender Unterricht (Art „Unterricht“, nur Anzeige).</summary>
    private IEnumerable<Vertretung> Unterrichtszeilen()
    {
        foreach (var t in Kalender.Tage(Von, Bis).Where(t => Kalender.IstUnterrichtstag(D, t)))
            foreach (var e in Plan(t).Eintraege.Where(e => e.Status == TagesStatus.Regulaer && e.Regel != null))
            {
                var v = new Vertretung
                {
                    Id = 0, Datum = t, Stunde = e.Stunde, Art = VertretungsArt.Unterricht, UnterrichtId = e.Regel!.Unterricht.Id, ZeilenIndex = e.Regel.ZeilenIndex,
                    LehrerId = e.Regel.LehrerId, VertreterId = e.Regel.LehrerId, FachId = e.FachId, KlassenIds = e.KlassenIds.ToList(), RaumId = e.Regel.RaumId,
                    Schuelergruppe = VeranstaltungsLogik.Schuelergruppe(e.Regel),
                };
                if (Passt(v)) yield return v;
            }
    }

    private string Anzeige(ElementTyp typ, int? id, string spalte)
    {
        if (id is not int i) return "";
        if (!Langname(spalte)) return Ix.K(typ, i);
        var e = Ix.Element(typ, i);
        return e is Lehrer l ? l.VollerName : e is null ? "?" : string.IsNullOrEmpty(e.Langname) ? e.Kurzname : e.Langname;
    }

    private string Klassen(IEnumerable<int> ids, string spalte) => string.Join(", ", ids.Select(k => Anzeige(ElementTyp.Klasse, k, spalte)));

    private object?[] Werte(Vertretung v)
    {
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var gruppe = _gruppen.TryGetValue(v.Id, out var gr) ? gr : null;
        var va = v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int vaId ? D.Veranstaltungen.FirstOrDefault(x => x.Id == vaId) : null;
        var vertreter = va != null ? VeranstaltungsLogik.LehrerText(D, va, v.Datum)
            : v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten ? "+++"
            : v.Art == VertretungsArt.Freisetzung ? Anzeige(ElementTyp.Lehrer, v.LehrerId, "Vertreter")
            : v.IstEntfall || v.Art == VertretungsArt.Vormerkung ? "---" : v.VertreterId is int l ? Anzeige(ElementTyp.Lehrer, l, "Vertreter") : v.Offen ? "???" : "";
        var raumNeu = v.IstEntfall ? "---" : v.RaumOffen ? "???" : v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId)
            : string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => Anzeige(ElementTyp.Raum, x, "NRaum")));
        string Pos(DateOnly? d, int? s) => d is DateOnly dd ? $"{dd:d.M.} / {s}" : "";
        var stunde = gruppe is { Count: > 1 } ? $"{gruppe.Min(x => x.Stunde)}-{gruppe.Max(x => x.Stunde)}" : Vertretungsplan.StundeText(v);
        var u = v.UnterrichtId is int uid ? Plan(v.Datum).Unterrichte.GetValueOrDefault(uid) : null;
        // „*“ vor dem Lehrer: Teil einer Kopplung
        var lehrer = va != null ? VeranstaltungsLogik.LehrerText(D, va, v.Datum, ", ")
            : (u is { Zeilen.Count: > 1 } && v.LehrerId.HasValue ? "*" : "") + Anzeige(ElementTyp.Lehrer, v.LehrerId, "Lehrer");
        var neueKlassen = Klassen(v.NeueKlassenIds, "Klassen");
        // Tausch: bisheriger Unterricht dieser Position in den Klammerspalten
        var weg = _weg.GetValueOrDefault(v.Id);
        if (weg != null) lehrer = (Plan(weg.Datum).Unterrichte.GetValueOrDefault(weg.UnterrichtId ?? -1) is { Zeilen.Count: > 1 } && weg.LehrerId.HasValue ? "*" : "") + Anzeige(ElementTyp.Lehrer, weg.LehrerId, "Lehrer");
        var neuesFach = v.IstEntfall ? "---" : Anzeige(ElementTyp.Fach, v.VertretungsfachId ?? (IstVerlegung(v) ? v.FachId : null), "NFach");
        if (v.Art == VertretungsArt.Freisetzung && neueKlassen.Length > 0) neueKlassen = $"({neueKlassen})";
        var zeit = D.Zeitraster.Zeiten.FirstOrDefault(z => z.Nr == (gruppe?.Min(x => x.Stunde) ?? v.Stunde)) is { } z1 && D.Zeitraster.Zeiten.FirstOrDefault(z => z.Nr == (gruppe?.Max(x => x.Stunde) ?? v.Stunde)) is { } z2
            ? $"{z1.Beginn:HH\\:mm}-{z2.Ende:HH\\:mm}" : "";
        var zaehler = v.IstEntfall && v.Art != VertretungsArt.BereitschaftEntfaellt ? "-1" : v.VertreterId.HasValue && v.VertreterId != v.LehrerId && Vertretungen.ZaehlendeArten.Contains(v.Art) ? "+1" : "";
        var abteilung = v.NeueKlassenIds.Select(k => Ix.Klassen.GetValueOrDefault(k)?.AbteilungId).FirstOrDefault(x => x.HasValue);
        var benachrichtigen = v.VertreterId is int vt && v.Datum == DateOnly.FromDateTime(DateTime.Today) &&
                              !Plan(v.Datum).VonLehrer(vt).Any(e => e.Stunde < v.Stunde && e.Vertretung != v) ? "X" : "";
        var werte = new object?[]
        {
            v.Id == 0 ? "" : v.Id, Vertretungsplan.ArtText(D, v.Art), $"{v.Datum:dd.MM.}", tag, stunde, zeit,
            // „Fach“ bleibt wie im Vorbild leer, solange kein Vertretungsfach eingetragen ist (der Druck setzt dann das ursprüngliche Fach ein)
            Anzeige(ElementTyp.Fach, (weg ?? v).FachId, "Fach"), neuesFach,
            lehrer, vertreter, Klassen((weg ?? v).KlassenIds, "AKlassen"), neueKlassen,
            v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId) : Anzeige(ElementTyp.Raum, (weg ?? v).RaumId, "Raum"), raumNeu,
            Pos(v.VonDatum, v.VonStunde), weg != null ? Pos(weg.NachDatum, weg.NachStunde) : Pos(v.NachDatum, v.NachStunde), v.Art == VertretungsArt.VertretungOhneLehrer && string.IsNullOrWhiteSpace(v.Text) ? Vertretungen.OhneLehrerText : v.Text, v.Statistik, v.Schuelergruppe,
            Langname("Grund") && D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == v.GrundId) is { } gl ? gl.Langname : v.Grund,
            u?.Text ?? "", u != null && v.ZeilenIndex < u.Zeilen.Count ? u.Zeilen[v.ZeilenIndex].Zeilentext : "", v.Beschreibung,
            v.IstEntfall ? "X" : "", v.Mitbetreuung ? "X" : "", v.Kopplung > 0 ? v.Kopplung.ToString() : "", Vertretungen.IstNeu(v, Doc.Sitzungsbeginn) ? "Neu" : "",
            v.Fixiert ? "X" : "", v.NichtDrucken ? "N" : "", v.Text2, benachrichtigen, zaehler, "", v.Wert?.ToString("0.##") ?? "",
            Anzeige(ElementTyp.Abteilung, abteilung, "Abt"), v.Benutzer, v.Mehrarbeit ? "x" : "",
        };
        if (gruppe != null && _vaSammel.Contains(v.Id)) SammelWerte(werte, v, gruppe);
        return werte;
    }

    /// <summary>Hintergrund des Vertreterfeldes einer Pausenaufsicht (wie im Vorbild türkis).</summary>
    private static readonly Color PausenFarbe = Color.FromArgb(160, 236, 246);

    /// <summary>Geänderte Felder bei Tausch/Verlegung (Vorbild: magenta, in der markierten Zeile lila).</summary>
    internal static readonly Color VerlegungsFarbe = Color.FromArgb(255, 128, 255);
    internal static readonly Color VerlegungsFarbeMarkiert = Color.FromArgb(212, 166, 223);

    /// <summary>
    /// Darstellung wie im Vorbild: alle Werte in schwarzer Schrift; offene Felder „???“ rot (nicht fett), „---“ (Entfall) schwarz,
    /// Art in der Farbe der Vertretungsart, Pausenaufsicht türkis, Kollision gelb mit rotem Rahmen und roter Vtr-Nr.
    /// </summary>
    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        var name = _g.Columns[e.ColumnIndex].Name;
        e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.Black;
        if (D.Vertretungseinstellungen.Farben.TryGetValue(v.Art.ToString(), out var farbe) && name == "Art") e.CellStyle.BackColor = Ui.Farbe(farbe, Color.White);
        if (name == "Vertreter" && v.Art == VertretungsArt.Pausenaufsicht) e.CellStyle.BackColor = PausenFarbe;
        // offene Vertretung: rotes "???" beim Vertreter bzw. Raum – auch in markierten Zeilen
        var offen = (v.Offen && name == "Vertreter") || (v.RaumOffen && name == "NRaum") ||
                    (name == "Vertreter" && v.Art == VertretungsArt.Veranstaltung && _gruppen.TryGetValue(v.Id, out var vg) && vg.Any(x => x.VeranstaltungOffen));
        if (offen) e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.Red;
        if (Kollision(v) is { } k)
        {
            // Kollision: Feld gelb hinterlegt und rot umrandet (siehe KollisionZeichnen), Vertretungsnummer rot
            var (kl, kr) = KollisionsFelder(v);
            if ((name == "Vertreter" && kl) || (name == "NRaum" && kr)) e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = Color.FromArgb(255, 245, 0);
            if (name == "Nr") e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.Red;
            Hinweis(_g.Rows[e.RowIndex].Cells[e.ColumnIndex], "Kollision: " + k);
        }
        else if (name is "Nr" or "Vertreter" or "NRaum") Hinweis(_g.Rows[e.RowIndex].Cells[e.ColumnIndex], "");
        if (name is "Fix" or "ND" or "Entf" or "Mitb" or "Neu" or "Ben") e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        if (name is "UText" or "UZText") e.CellStyle.BackColor = Color.FromArgb(225, 225, 225);
        // Tausch/Verlegung wie im Vorbild: geänderte Felder magenta, in der markierten Zeile lila
        if (IstVerlegung(v) && name is "Klassen" or "Vertreter" or "NRaum" or "NFach" or "Text" && e.CellStyle.BackColor != Color.FromArgb(255, 245, 0))
        {
            e.CellStyle.BackColor = VerlegungsFarbe;
            e.CellStyle.SelectionBackColor = VerlegungsFarbeMarkiert;
        }
        if (v.Id == 0 && name is not "Art") e.CellStyle.BackColor = Color.FromArgb(242, 242, 242);
    }

    private static void Hinweis(DataGridViewCell zelle, string text)
    {
        if (zelle.ToolTipText != text) zelle.ToolTipText = text;
    }

    /// <summary>Kollidierende Felder rot umranden.</summary>
    private void KollisionZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        var name = _g.Columns[e.ColumnIndex].Name;
        if (name is not ("Vertreter" or "NRaum") || Kollision(v) is null) return;
        var (kl, kr) = KollisionsFelder(v);
        if ((name == "Vertreter" && !kl) || (name == "NRaum" && !kr)) return;
        e.Paint(e.CellBounds, DataGridViewPaintParts.All);
        using var p = new Pen(Color.Red, 2);
        var r = e.CellBounds;
        e.Graphics.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        e.Handled = true;
    }

    /// <summary>Auswahlliste "Vertreter": belegte bzw. abwesende Lehrer rot, Bereitschaft/freigesetzt grün, Langname als Zusatz.</summary>
    private Steuerelemente.ListenStil? VertreterStil(DataGridViewCell zelle, string text)
    {
        if (zelle.OwningColumn.Name == "NRaum") return RaumStil(zelle, text);
        if (zelle.OwningColumn.Name != "Vertreter") return D.Faecher.FirstOrDefault(f => f.Kurzname == text) is { } fa ? new(Color.White, Color.Black, fa.Langname) : null;
        if (zelle.OwningRow.Tag is not Vertretung v) return null;
        var l = D.Lehrer.FirstOrDefault(x => x.Kurzname == text);
        if (l is null) return text switch { "+++" => new(Color.White, Color.Black, "Vertr. o. Lehrkr."), "---" => new(Color.White, Color.Black, "Entfall"), _ => null };
        var tp = Plan(v.Datum);
        if (Vertretungen.LehrerBelegt(D, tp, l.Id, v.Stunde, v) is { } grund) return new(Color.FromArgb(225, 40, 40), Color.White, $"{l.VollerName} – {grund}");
        if (Vertretungen.HatBereitschaft(D, tp, l.Id, v.Stunde) || Vertretungen.IstFreigesetzt(D, tp, l.Id, v.Stunde)) return new(Color.FromArgb(200, 240, 200), Color.Black, l.VollerName);
        return new(Color.White, Color.Black, l.VollerName);
    }

    /// <summary>Auswahlliste "Raum": belegte bzw. gesperrte Räume rot, freie grün, Langname als Zusatz.</summary>
    private Steuerelemente.ListenStil? RaumStil(DataGridViewCell zelle, string text)
    {
        if (zelle.OwningRow.Tag is not Vertretung v) return null;
        var r = D.Raeume.FirstOrDefault(x => x.Kurzname == text);
        if (r is null) return null;
        var gesperrt = Absenzen.RaumGesperrt(D, r.Id, v.Datum, v.Stunde);
        var belegt = Plan(v.Datum).Stunde(v.Stunde).Any(x => x.Findetstatt && x.RaumIds.Contains(r.Id) && x.Vertretung != v);
        var name = r.Langname.Length > 0 && r.Langname != r.Kurzname ? r.Langname : "";
        return gesperrt ? new(Color.FromArgb(225, 40, 40), Color.White, (name + " – gesperrt").Trim(' ', '–'))
            : belegt ? new(Color.FromArgb(255, 205, 205), Color.Black, (name + " – belegt").Trim(' ', '–'))
            : new(Color.FromArgb(200, 240, 200), Color.Black, name);
    }

    private void EditorZeigen(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox t) return;
        var name = _g.CurrentCell?.OwningColumn.Name;
        var typ = name switch { "Vertreter" => ElementTyp.Lehrer, "NFach" => ElementTyp.Fach, "NRaum" => ElementTyp.Raum, _ => (ElementTyp?)null };
        if (typ is null)
        {
            t.AutoCompleteMode = AutoCompleteMode.None;
            return;
        }
        var quelle = new AutoCompleteStringCollection();
        quelle.AddRange(D.AlleElemente(typ.Value).Where(x => !x.Ignorieren).Select(x => x.Kurzname).ToArray());
        if (typ == ElementTyp.Lehrer) quelle.AddRange(new[] { "+++", "---" });
        t.AutoCompleteCustomSource = quelle;
        t.AutoCompleteSource = AutoCompleteSource.CustomSource;
        t.AutoCompleteMode = AutoCompleteMode.Append;
    }

    /// <summary>"+" und "-" ohne Bearbeitungsmodus wirken auf alle markierten Zeilen.</summary>
    private void TastenDruck(object? sender, KeyPressEventArgs e)
    {
        // nur im Feld Vertreter bzw. in nicht editierbaren Feldern – in Text, Raum, Fach … beginnt „+“/„-“ die Eingabe
        if (_g.IsCurrentCellInEditMode || _g.CurrentCell is not { } c || (c.OwningColumn.Name != "Vertreter" && !c.OwningColumn.ReadOnly)) return;
        if (e.KeyChar == '+') { OhneLehrkraft(); e.Handled = true; }
        else if (e.KeyChar == '-') { Entfall(); e.Handled = true; }
    }

    /// <summary>
    /// Taste Entf (ohne Bearbeitung): im Feld Vertreter bzw. in nicht editierbaren Feldern wird die Vertretung wieder offen
    /// (wie im Vorbild), in einem editierbaren Feld (Text, Raum, Fach …) wird dessen Inhalt gelöscht.
    /// </summary>
    private void Entfernen()
    {
        if (_g.CurrentCell is not { } c || c.RowIndex < 0 || _g.Rows[c.RowIndex].Tag is not Vertretung v) return;
        var spalte = c.OwningColumn.Name;
        if (spalte == "Vertreter" || c.OwningColumn.ReadOnly) { Zuruecksetzen(); return; }
        if ((c.Value?.ToString() ?? "").Length == 0) return;
        if (v.Id == 0 || v.Fixiert) return;
        Uebernehmen(v, spalte, "", ZielIds(c.RowIndex));
    }

    /// <summary>Markierte Vertretungen (zusammengefasste Veranstaltungszeilen mit allen Stunden, ohne reine Unterrichtszeilen).</summary>
    private List<Vertretung> Aktuelle()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Vertretung>().ToList();
        if (liste.Count == 0 && _g.CurrentRow?.Tag is Vertretung v) liste.Add(v);
        return Erweitern(liste);
    }

    /// <summary>Vertretungen um die Stunden einer zusammengefassten Veranstaltung erweitern (jede andere Zeile wird einzeln bearbeitet).</summary>
    private List<Vertretung> Erweitern(IEnumerable<Vertretung> liste) =>
        liste.Where(x => x.Id != 0).SelectMany(x => _gruppen.TryGetValue(x.Id, out var g) ? g : new List<Vertretung> { x })
            .DistinctBy(x => x.Id).OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList();

    /// <summary>Markierte Vertretungen ohne Zeilen einer Veranstaltung und ohne fixierte Zeilen (für Entfall, +, Vertreter …).</summary>
    private List<Vertretung> Bearbeitbare(IReadOnlyCollection<int>? ids = null)
    {
        var alle = ids is null ? Aktuelle() : D.Vertretungen.Where(v => ids.Contains(v.Id)).OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList();
        var fix = alle.Count(v => v.Fixiert);
        if (fix > 0) _status.Text = $"{fix} fixierte Vertretung(en) wurden nicht geändert.";
        return alle.Where(v => !v.Fixiert && v.Art != VertretungsArt.Veranstaltung).ToList();
    }

    private void Gewaehlt()
    {
        var liste = Aktuelle();
        var aktiv = _g.CurrentRow?.Tag as Vertretung ?? liste.FirstOrDefault();
        if (!_nr.Focused) _nr.Text = aktiv?.Id.ToString() ?? "";
        _laedt = true;
        AppZustand.VertretungWaehlen(aktiv, liste.Select(v => v.Id));
        _laedt = false;
    }

    private sealed record Eintrag(int? Id, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>Elementauswahl passend zur Ansicht: Klassen bzw. Lehrer, zuerst „(alle)“.</summary>
    private void ElementeFuellen()
    {
        var lehrer = _ansicht.SelectedIndex is 1 or 3;
        var alt = (_element.SelectedItem as Eintrag)?.Id;
        _laedt = true;
        _element.Items.Clear();
        _element.Items.Add(new Eintrag(null, "(alle)"));
        IEnumerable<Element> elemente = lehrer ? Stammdaten.Sortiert(D.Lehrer) : Stammdaten.Sortiert(D.Klassen);
        foreach (var e in elemente.Where(e => !e.Ignorieren)) _element.Items.Add(new Eintrag(e.Id, e.Kurzname));
        _element.SelectedItem = _element.Items.Cast<Eintrag>().FirstOrDefault(x => x.Id == alt) ?? _element.Items[0];
        _laedt = false;
    }

    /// <summary>Zur nächsten (bzw. vorherigen) offenen Vertretung springen, am Ende wieder von vorn.</summary>
    public bool NaechsteOffene(int richtung = 1)
    {
        var zeilen = _g.Rows.Cast<DataGridViewRow>().ToList();
        if (zeilen.Count == 0) return false;
        var start = _g.CurrentRow?.Index ?? -1;
        for (var i = 1; i <= zeilen.Count; i++)
        {
            var r = zeilen[((start + richtung * i) % zeilen.Count + zeilen.Count) % zeilen.Count];
            if (r.Tag is not Vertretung { OffenOderRaumOffen: true }) continue;
            _g.ClearSelection();
            r.Selected = true;
            _g.CurrentCell = r.Cells[Math.Max(0, _g.CurrentCell?.ColumnIndex ?? 0)];
            _auswahlTimer.Stop();
            Gewaehlt();
            return true;
        }
        return false;
    }

    private void Springen(int nr)
    {
        var v = D.Vertretungen.FirstOrDefault(x => x.Id == nr);
        if (v is null) { Ui.Info($"Die Vertretung Nr. {nr} gibt es nicht."); return; }
        if (v.Datum < Von || v.Datum > Bis)
        {
            _laedt = true;
            _von.Value = _bis.Value = v.Datum.ToDateTime(TimeOnly.MinValue);
            _laedt = false;
            ZeitraumGeaendert();
        }
        _offen.Checked = _kollisionen.Checked = false;
        _element.SelectedIndex = 0;
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Vertretung x && x.Id == nr)
            {
                _g.ClearSelection();
                r.Selected = true;
                _g.CurrentCell = r.Cells[0];
            }
    }

    /// <summary>
    /// Zelle verlassen: geänderten Wert übernehmen. Die Übernahme läuft erst nach dem Zellwechsel – Rückfragen und Neuaufbau
    /// stören so den Wechsel in die nächste Zelle nicht (Fokus und Zeile bleiben). Unveränderte Zellen lösen keine Änderung aus.
    /// </summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        var vorher = _wertVorher;
        _wertVorher = null;
        if (_laedt || e.RowIndex < 0 || e.RowIndex >= _g.Rows.Count || _g.Rows[e.RowIndex].Tag is not Vertretung v || v.Id == 0)
        {
            AufgeschobenNachholen();
            return;
        }
        var spalte = _g.Columns[e.ColumnIndex].Name;
        var wert = _g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        if (vorher != null && wert == vorher)
        {
            AufgeschobenNachholen();
            return;
        }
        var ziel = ZielIds(e.RowIndex);
        NachZellwechsel(() =>
        {
            Uebernehmen(v, spalte, wert, ziel);
            AufgeschobenNachholen();
        });
    }

    /// <summary>Während der Eingabe aufgeschobenen Neuaufbau nachholen, sobald keine Zelle mehr bearbeitet wird.</summary>
    private void AufgeschobenNachholen()
    {
        if (_neuaufbauAusstehend) NachZellwechsel(() => { if (_neuaufbauAusstehend && !_g.IsCurrentCellInEditMode) Neuaufbau(); });
    }

    /// <summary>
    /// Ziel einer Eingabe: die bearbeitete Zeile (bei einer zusammengefassten Veranstaltung mit allen Stunden des Tages) bzw. –
    /// wenn sie markiert ist – alle markierten Zeilen (gleichzeitige Bearbeitung mehrerer markierter Stunden). Eine Doppelstunde
    /// wird nicht automatisch mitgeändert: jede Vertretungszeile wird einzeln bearbeitet.
    /// </summary>
    private List<int> ZielIds(int zeile)
    {
        if (zeile < 0 || zeile >= _g.Rows.Count || _g.Rows[zeile].Tag is not Vertretung v || v.Id == 0) return new();
        var liste = _g.Rows[zeile].Selected ? Aktuelle() : Erweitern(new[] { v });
        return liste.Select(x => x.Id).Append(v.Id).Distinct().ToList();
    }

    /// <summary>Eingabe in einer Spalte auf die Vertretung(en) anwenden.</summary>
    private void Uebernehmen(Vertretung v, string spalte, string wert, IReadOnlyList<int> ids)
    {
        if (IsDisposed) return;
        // nach Rückgängig o. Ä. ist das Objekt der Zeile nicht mehr das der Daten: aktuelle Fassung verwenden
        v = D.Vertretungen.FirstOrDefault(x => x.Id == v.Id) ?? v;
        var id = new[] { v.Id };
        switch (spalte)
        {
            case "Vertreter":
                if (v.Art == VertretungsArt.Veranstaltung) { VeranstaltungsVertreter(v, wert); return; }
                if (wert is "+" or "+++") { OhneLehrkraft(ids); return; }
                if (wert is "-" or "---") { Entfall(ids); return; }
                if (wert is "" or "???")
                {
                    var offen = Bearbeitbare(ids);
                    if (offen.Count == 0) { Neuaufbau(); return; }
                    Ausfuehren("Vertreter entfernt", offen.Select(x => x.Id).ToList(), (d, x) => Vertretungen.VertreterSetzen(d, x, null, false, AppZustand.Benutzer));
                    return;
                }
                var l = D.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase)) ?? D.Lehrer.FirstOrDefault(x => x.VollerName.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (l is null) { Ui.Info($"Der Lehrer '{wert}' ist nicht bekannt."); Neuaufbau(); return; }
                VertreterSetzen(l.Id, false, ids);
                return;
            case "NFach":
                var f = wert.Length == 0 ? null : D.Faecher.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (wert.Length > 0 && f is null) { Ui.Info($"Das Fach '{wert}' ist nicht bekannt."); Neuaufbau(); return; }
                Ausfuehren("Vertretungsfach", id, (d, x) => { x.VertretungsfachId = f?.Id; x.Bearbeitet = true; });
                return;
            case "NRaum":
                if (wert == "???") wert = "";
                var r = wert.Length == 0 ? null : D.Raeume.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (wert.Length > 0 && r is null) { Ui.Info($"Der Raum '{wert}' ist nicht bekannt."); Neuaufbau(); return; }
                Ausfuehren("Vertretungsraum", id, (d, x) =>
                {
                    x.VertretungsraumId = r?.Id;
                    x.ZusatzraumIds.Clear();
                    if (x.Art == VertretungsArt.Vertretung && x.VertreterId == x.LehrerId) x.Art = VertretungsArt.Raumvertretung;
                    x.Bearbeitet = true;
                });
                return;
            case "Kz":
                Ausfuehren("Statistik-Kennzeichen", id, (d, x) => x.Statistik = wert);
                return;
            case "Text":
                Ausfuehren("Vertretungstext", ids, (d, x) => { x.Text = wert; x.Bearbeitet = true; });
                return;
            case "Text2":
                Ausfuehren("Vertretungstext 2", ids, (d, x) => { x.Text2 = wert; x.Bearbeitet = true; });
                return;
            case "Beschr":
                Ausfuehren("Beschreibung", ids, (d, x) => x.Beschreibung = wert);
                return;
            case "SG":
                Ausfuehren("Schülergruppe", id, (d, x) => x.Schuelergruppe = wert);
                return;
            case "Kopp":
                if (!int.TryParse(wert.Length == 0 ? "0" : wert, out var kopp) || kopp < 0) { Ui.Info("Vertretungs-Kopplung: bitte eine Zahl eingeben."); Neuaufbau(); return; }
                Ausfuehren("Vertretungs-Kopplung", id, (d, x) => x.Kopplung = kopp);
                return;
            case "Wert":
                decimal? w = null;
                if (wert.Length > 0)
                {
                    if (decimal.TryParse(wert.Replace('.', ','), System.Globalization.NumberStyles.Number, Format.De, out var wd)) w = wd;
                    else { Ui.Info("Wert: bitte eine Zahl eingeben."); Neuaufbau(); return; }
                }
                Ausfuehren("Wert", id, (d, x) => x.Wert = w);
                return;
        }
    }

    /// <summary>
    /// Vertreter einer Veranstaltung („Cer,New,?“): Für einen offenen Platz („?“) eine Ersatzlehrkraft eintragen; weitere Namen
    /// sind zusätzliche Lehrkräfte (nur an diesem Tag oder für die ganze Veranstaltung). Einen Ersatz bzw. eine zusätzliche
    /// Lehrkraft aus der Liste löschen nimmt sie wieder heraus.
    /// </summary>
    private void VeranstaltungsVertreter(Vertretung v, string wert)
    {
        if (v.VeranstaltungId is not int vaId || D.Veranstaltungen.FirstOrDefault(x => x.Id == vaId) is not { } va) { Neuaufbau(); return; }
        var namen = wert.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Trim('?').Length > 0).ToList();
        var datum = v.Datum;
        var bisher = va.LehrerAm(datum);
        var neu = new List<int>();
        foreach (var n in namen)
        {
            var l = D.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (l is null) { Ui.Info($"Der Lehrer '{n}' ist nicht bekannt."); Neuaufbau(); return; }
            if (!bisher.Contains(l.Id) && !neu.Contains(l.Id)) neu.Add(l.Id);
        }
        bool Genannt(int l) => namen.Any(n => Ix.KLehrer(l).Equals(n, StringComparison.OrdinalIgnoreCase));
        var offen = va.OffenAm(datum);
        var ersatzWeg = va.Ersatz.Where(e => e.Datum == datum && e.ErsatzId is int x && !Genannt(x)).ToList();
        var zusatzWeg = va.Zusatz.Where(z => z.Datum == datum && !Genannt(z.LehrerId)).Select(z => z.LehrerId).ToList();
        if (neu.Count == 0 && ersatzWeg.Count == 0 && zusatzWeg.Count == 0) { Neuaufbau(); return; }
        var plaetze = offen.Count + ersatzWeg.Count;
        var alsErsatz = neu.Take(plaetze).ToList();
        var zusaetzlich = neu.Skip(plaetze).ToList();
        var ganze = va.Von == va.Bis;
        if (zusaetzlich.Count > 0 && !ganze)
        {
            var w = OptionenDialog.Fragen(this, "Frage",
                $"{string.Join(", ", zusaetzlich.Select(Ix.KLehrer))} zusätzlich an der Veranstaltung {va.Id} ({va.Text}) teilnehmen lassen?",
                new[] { $"nur am {datum:dd.MM.yyyy}", $"an der ganzen Veranstaltung ({va.Von:dd.MM.} – {va.Bis:dd.MM.yyyy})" });
            if (w is null) { Neuaufbau(); return; }
            ganze = w == 1;
        }
        var stunden = VeranstaltungsLogik.Stunden(D, va, datum).ToList();
        var probleme = neu.Select(l => stunden.Select(s => Vertretungen.LehrerBelegt(D, Plan(datum), l, s, v)).FirstOrDefault(g => g != null) is { } g ? $"{Ix.KLehrer(l)}: {g}" : null).OfType<string>().ToList();
        if (probleme.Count > 0 && !Ui.Frage($"{string.Join("\n", probleme)}\n\nTrotzdem verplanen?", "UntPlan - Frage")) { Neuaufbau(); return; }
        if (FilterAktiv) _behalten.Merken(new[] { v.Id });
        Aendern(zusaetzlich.Count > 0 ? "Lehrkraft zur Veranstaltung hinzugefügt" : "Ersatzlehrkraft für Veranstaltung", AenderungsArt.Vertretung, d =>
        {
            foreach (var e in ersatzWeg) VeranstaltungsLogik.ErsatzSetzen(d, vaId, datum, e.LehrerId, null);
            foreach (var l in zusatzWeg) VeranstaltungsLogik.ZusatzEntfernen(d, vaId, datum, l);
            foreach (var l in alsErsatz) VeranstaltungsLogik.ErsatzSetzen(d, vaId, datum, null, l);
            foreach (var l in zusaetzlich) VeranstaltungsLogik.LehrerHinzufuegen(d, vaId, datum, l, ganze);
        });
    }

    /// <summary>Kontextmenü/Symbolleiste: einer Veranstaltung eine zusätzliche Lehrkraft hinzufügen (nicht nur als Ersatz).</summary>
    private void LehrkraftHinzufuegen()
    {
        var v = Aktuelle().FirstOrDefault(x => x.Art == VertretungsArt.Veranstaltung);
        if (v?.VeranstaltungId is not int vaId || D.Veranstaltungen.FirstOrDefault(x => x.Id == vaId) is not { } va)
        {
            Ui.Info("Bitte im Vertretungsfenster die Zeile einer Veranstaltung markieren.");
            return;
        }
        var name = Ui.Eingabe($"Zusätzliche Lehrkraft für Veranstaltung {va.Id} am {v.Datum:dd.MM.yyyy} (Kurzname, mehrere mit Komma):", "Lehrkraft hinzufügen");
        if (string.IsNullOrWhiteSpace(name)) return;
        VeranstaltungsVertreter(v, string.Join(",", va.LehrerAm(v.Datum).Select(Ix.KLehrer)) + "," + name);
    }

    /// <summary>Vertretung fixieren (X): vor weiterer Veränderung schützen; erneuter Klick löst die Fixierung.</summary>
    private void Fixieren()
    {
        var liste = Aktuelle();
        if (liste.Count == 0) return;
        var ziel = !liste.All(v => v.Fixiert);
        Ausfuehren(ziel ? "Vertretung fixiert" : "Fixierung gelöst", liste.Select(v => v.Id).ToList(), (_, v) => v.Fixiert = ziel);
    }

    /// <summary>Nicht drucken (N) ein/aus.</summary>
    private void NichtDrucken()
    {
        var liste = Aktuelle();
        if (liste.Count == 0) return;
        var ziel = !liste.All(v => v.NichtDrucken);
        Ausfuehren("Nicht drucken", liste.Select(v => v.Id).ToList(), (_, v) => v.NichtDrucken = ziel);
    }

    /// <summary>
    /// Änderung auf mehrere Vertretungen (per Id) anwenden. Die Zeilen bleiben bis zum nächsten Zeilenwechsel sichtbar, auch wenn
    /// sie nicht mehr zum Filter passen, und erhalten das Kennzeichen „Neue Vertretung“.
    /// </summary>
    private void Ausfuehren(string text, IReadOnlyCollection<int> ids, Action<Schuldatei, Vertretung> aktion)
    {
        if (ids.Count == 0) return;
        if (FilterAktiv) _behalten.Merken(ids);
        Aendern(ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, AenderungsArt.Vertretung, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList())
            {
                aktion(d, v);
                Vertretungen.AlsGeaendertMarkieren(v, AppZustand.Benutzer);
            }
        });
    }

    public void VertreterSetzen(int lehrerId, bool mitbetreuung) => VertreterSetzen(lehrerId, mitbetreuung, null);

    private void VertreterSetzen(int lehrerId, bool mitbetreuung, IReadOnlyCollection<int>? ids)
    {
        var liste = Bearbeitbare(ids);
        if (liste.Count == 0) { NachZellwechsel(Neuaufbau); return; }
        // absente Lehrkraft: Nachfrage „Trotzdem verplanen“ (Art „Trotz Absenz“)
        var absent = liste.Where(v => Absenzen.Lehrer(D, lehrerId, v.Datum, v.Stunde) != null).ToList();
        if (absent.Count > 0 && !Ui.Frage($"{Ix.KLehrer(lehrerId)} ist am {Format.Datum(absent[0].Datum)} absent.\n\nTrotzdem verplanen?", "UntPlan - Frage")) { NachZellwechsel(Neuaufbau); return; }
        var probleme = liste.Except(absent).Select(v => Vertretungen.LehrerBelegt(D, Plan(v.Datum), lehrerId, v.Stunde, v) is { } g ? $"{Format.Datum(v.Datum)} {v.Stunde}. Std.: {g}" : null).Where(x => x != null).ToList();
        if (probleme.Count > 0 && !mitbetreuung && !Ui.Frage($"{Ix.KLehrer(lehrerId)} ist nicht frei:\n{string.Join("\n", probleme)}\n\nTrotzdem als Vertreter eintragen?")) { NachZellwechsel(Neuaufbau); return; }
        Ausfuehren($"Vertreter {Ix.KLehrer(lehrerId)}", liste.Select(v => v.Id).ToList(), (d, v) => Vertretungen.VertreterSetzen(d, v, lehrerId, mitbetreuung, AppZustand.Benutzer));
    }

    private void Entfall() => Entfall(null);

    private void Entfall(IReadOnlyCollection<int>? ids)
    {
        var liste = Bearbeitbare(ids).Where(v => v.Art != VertretungsArt.Freisetzung).ToList();
        if (liste.Count == 0) { NachZellwechsel(Neuaufbau); return; }
        // Verlegung (Ziel oder verlegter Ursprung): zuerst fragen, ob die Verlegung rückgängig gemacht werden soll
        var anzahl = liste.Count;
        liste = VerlegungsAbfrage.VorEntfall(liste, Aendern);
        if (liste.Count == 0) { NachZellwechsel(Neuaufbau); return; }
        if (liste.Count != anzahl) liste = liste.Select(v => D.Vertretungen.FirstOrDefault(x => x.Id == v.Id)).OfType<Vertretung>().ToList();
        var zurueck = liste.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt);
        // Sondereinsatz auf Entfall: Rückfrage, ob er gelöscht werden soll (Nein = Entfall wie bisher)
        var seLoeschen = !zurueck && StundenplanFenster.SondereinsatzLoeschenFragen(liste.Where(Vertretungen.IstSondereinsatz)) == true;
        if (seLoeschen)
        {
            var ids2 = liste.Select(v => v.Id).ToHashSet();
            if (FilterAktiv) _behalten.Merken(ids2);
            Aendern(ids2.Count > 1 ? $"Entfall ({ids2.Count} Stunden)" : "Entfall", AenderungsArt.Vertretung, d =>
            {
                foreach (var v in d.Vertretungen.Where(x => ids2.Contains(x.Id) && x.Art is not (VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt)).ToList())
                    if (!Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer, true)) Vertretungen.AlsGeaendertMarkieren(v, AppZustand.Benutzer);
            });
            return;
        }
        Ausfuehren(zurueck ? "Entfall aufgehoben" : "Entfall", liste.Select(v => v.Id).ToList(), (d, v) =>
        {
            var istEntfall = v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt;
            if (istEntfall == zurueck) Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer);
        });
    }

    private void OhneLehrkraft() => OhneLehrkraft(null);

    private void OhneLehrkraft(IReadOnlyCollection<int>? ids)
    {
        var liste = Bearbeitbare(ids).Where(v => v.Art != VertretungsArt.Pausenaufsicht).ToList();
        if (liste.Count == 0) { NachZellwechsel(Neuaufbau); return; }
        Ausfuehren("Vertr. o. Lehrkr.", liste.Select(v => v.Id).ToList(), (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer));
    }

    private void Teilen()
    {
        if (Aktuelle().FirstOrDefault() is not { } v) return;
        var kl = Ui.Eingabe("Klassen der neuen Teilvertretung (Kurznamen, die übrigen bleiben in der bisherigen Zeile):", "Vertretung teilen", "");
        if (kl is null) return;
        var ids = kl.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => D.Klassen.FirstOrDefault(k => k.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id)
            .Where(i => i.HasValue).Select(i => i!.Value).Where(v.NeueKlassenIds.Contains).ToList();
        Ausfuehren("Vertretung teilen", new[] { v.Id }, (d, x) =>
        {
            var n = Vertretungen.Teilen(d, x, AppZustand.Benutzer);
            if (ids.Count > 0 && ids.Count < x.NeueKlassenIds.Count)
            {
                n.VertretungsKlassenIds = ids;
                x.VertretungsKlassenIds = x.NeueKlassenIds.Except(ids).ToList();
            }
        });
    }

    private void RaumZuordnen()
    {
        if (Aktuelle().FirstOrDefault() is not { } v || v.Art == VertretungsArt.Pausenaufsicht) return;
        RaumZuordnenVertretungDialog.Zeigen(this, v);
    }

    private void AlsVormerkung()
    {
        var liste = Aktuelle().Where(v => v.UnterrichtId.HasValue && v.Art != VertretungsArt.Vormerkung).ToList();
        if (liste.Count == 0) return;
        Ausfuehren("Als Vormerkung beiseitelegen", liste.Select(v => v.Id).ToList(), (d, v) => Vertretungen.AlsVormerkung(d, v, AppZustand.Benutzer));
    }

    private void Zuruecksetzen()
    {
        var liste = Bearbeitbare();
        if (liste.Count == 0) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        if (FilterAktiv) _behalten.Merken(ids);
        Aendern("Vertretung zurückgesetzt", AenderungsArt.Vertretung, d =>
        {
            var tage = new HashSet<DateOnly>();
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList())
            {
                tage.Add(v.Datum);
                if (Vertretungen.IstAbgeleitet(v))
                {
                    v.Bearbeitet = false;
                    v.VertreterId = null;
                    v.VertretungsraumId = null;
                    v.VertretungsfachId = null;
                    v.Text = "";
                    v.Mehrarbeit = false;
                    Vertretungen.AlsGeaendertMarkieren(v, AppZustand.Benutzer);
                }
                else
                {
                    // manuelle Zeile (Verlegung/Sondereinsatz) samt Gegenstück entfernen
                    d.Vertretungen.RemoveAll(x => x == v || (v.VerlegungsId.HasValue && x.VerlegungsId == v.VerlegungsId));
                }
            }
            foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        });
    }

    private void Loeschen()
    {
        // Vertretungen aus Absenzen/Veranstaltungen lassen sich nicht löschen (Entfall bzw. Absenz ändern)
        var alle = Aktuelle();
        var liste = alle.Where(v => !Vertretungen.IstAbgeleitet(v)).ToList();
        if (liste.Count == 0)
        {
            if (alle.Count > 0) Ui.Info("Vertretungen, die aus einer Absenz oder Veranstaltung entstehen, können nicht gelöscht werden.\n\n" +
                "Mit X (Entfall) entfällt die Stunde, mit „Zurücksetzen“ wird sie wieder offen. Soll sie ganz verschwinden, die Absenz ändern.", "Vertretung löschen");
            return;
        }
        var nicht = alle.Count - liste.Count;
        if (!Ui.Frage($"{liste.Count} Vertretung(en) löschen?" + (nicht > 0 ? $"\n\n{nicht} Zeile(n) aus Absenzen/Veranstaltungen bleiben erhalten." : ""))) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Vertretungen löschen", AenderungsArt.Vertretung, d =>
        {
            var weg = d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList();
            var verlegungen = weg.Where(x => x.VerlegungsId.HasValue).Select(x => x.VerlegungsId).ToHashSet();
            d.Vertretungen.RemoveAll(x => ids.Contains(x.Id) || (x.VerlegungsId.HasValue && verlegungen.Contains(x.VerlegungsId)));
            foreach (var t in weg.Select(x => x.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        });
    }

    private void NeuBerechnen()
    {
        UmplanungsErgebnis? erg = null;
        Aendern("Vertretungen neu berechnet", AenderungsArt.Vertretung, d =>
        {
            // im Stundenplan umgeplante Stunden nacherfassen, damit ihre Vertretungen an der neuen Lage wieder erscheinen
            erg = Umplanungsabgleich.Nacherfassen(d, AppZustand.Benutzer);
            Vertretungen.Aktualisieren(d, Von, Bis);
        });
        if (erg is { } e && (e.Uebertragen.Count > 0 || e.Hinweise.Count > 0))
            Ui.Info($"{e.Uebertragen.Count} Vertretung(en) umgeplanter Stunden übertragen." + (e.Hinweise.Count > 0 ? "\n\n" + string.Join("\n", e.Hinweise.Take(15)) : ""), "Stundenplanänderungen nacherfasst");
    }

    private void VorschlagOeffnen()
    {
        Gewaehlt();
        HauptFenster.Oeffnen("vorschlag", () => new VertretungsvorschlagFenster());
    }

    private void Kontextmenue(Point pt)
    {
        var liste = Aktuelle();
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        if (liste.Count > 0)
        {
            cm.Items.Add("Vertretungsvorschlag…", Symbole.Get("vertretung"), (_, _) => VorschlagOeffnen());
            var schnell = new ToolStripMenuItem("Vertreter (bester Vorschlag)", Symbole.Get("lehrer"));
            if (liste[0].Art != VertretungsArt.Pausenaufsicht)
                foreach (var k in VertretungsVorschlag.Berechnen(D, liste[0]).Take(8))
                {
                    var id = k.Lehrer.Id;
                    schnell.DropDownItems.Add($"{k.Lehrer.Kurzname}  ({k.Lage}, Zähler {k.Zaehler:0.#})", null, (_, _) => VertreterSetzen(id, false));
                }
            if (schnell.DropDownItems.Count > 0) cm.Items.Add(schnell);
            cm.Items.Add(new ToolStripSeparator());
            if (liste.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt))
                cm.Items.Add("Entfall rückgängig machen", Symbole.Get("rueckgaengig"), (_, _) => Entfall());
            else
                cm.Items.Add($"Entfall{(liste.Count > 1 ? $" ({liste.Count} Stunden)" : "")}", Symbole.Get("entfall"), (_, _) => Entfall());
            cm.Items.Add($"Vertr. o. Lehrkr.{(liste.Count > 1 ? $" ({liste.Count} Stunden)" : "")}", Symbole.Get("planen"), (_, _) => OhneLehrkraft());
            if (liste.Any(v => v.Art == VertretungsArt.Veranstaltung))
                cm.Items.Add("Lehrkraft zur Veranstaltung hinzufügen…", Symbole.Get("lehrer"), (_, _) => LehrkraftHinzufuegen());
            cm.Items.Add("Vertretung teilen", Symbole.Get("tausch"), (_, _) => Teilen());
            cm.Items.Add("Raum zuordnen / löschen…", Symbole.Get("raumzuordnen"), (_, _) => RaumZuordnen());
            cm.Items.Add("Als Vormerkung beiseitelegen", Symbole.Get("zurueck"), (_, _) => AlsVormerkung());
            var nicht = new ToolStripMenuItem("Nicht drucken") { Checked = liste.All(v => v.NichtDrucken) };
            nicht.Click += (_, _) => { var ziel = !nicht.Checked; Ausfuehren("Nicht drucken", liste.Select(v => v.Id).ToList(), (_, v) => v.NichtDrucken = ziel); };
            cm.Items.Add(nicht);
            var fix = new ToolStripMenuItem("(X) Fixieren", Symbole.Get("schloss")) { Checked = liste.All(v => v.Fixiert) };
            fix.Click += (_, _) => { var ziel = !fix.Checked; Ausfuehren("Fixieren", liste.Select(v => v.Id).ToList(), (_, v) => v.Fixiert = ziel); };
            cm.Items.Add(fix);
            cm.Items.Add(new ToolStripSeparator());
            if (liste[0].VertreterId is int vt) cm.Items.Add($"Lehrerplan {Ix.KLehrer(vt)} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Lehrer, vt, false));
            if (liste[0].LehrerId is int lt) cm.Items.Add($"Lehrerplan {Ix.KLehrer(lt)} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Lehrer, lt, false));
            if (liste[0].NeueKlassenIds.FirstOrDefault() is var kt && kt != 0) cm.Items.Add($"Klassenplan {Ix.KKlassen(new[] { kt })} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Klasse, kt, false));
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Zurücksetzen (wieder offen)", Symbole.Get("rueckgaengig"), (_, _) => Zuruecksetzen());
            cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
            cm.Items.Add(new ToolStripSeparator());
        }
        cm.Items.Add("Sondereinsatz…", Symbole.Get("neu"), (_, _) => SondereinsatzDialog.Neu(this, AppZustand.Datum, liste.FirstOrDefault()?.Stunde ?? 1, liste.FirstOrDefault()?.NeueKlassenIds.ToArray() ?? Array.Empty<int>(), Array.Empty<int>()));
        cm.Items.Add("Nachrichten zum Tag…", Symbole.Get("kommentar"), (_, _) => TagestextDialog.Zeigen(this, AppZustand.Datum));
        cm.Show(_g, pt);
    }

    /// <summary>Vertretungsplan der nächsten Unterrichtstage als HTML (Schüler- oder Lehrerversion) im Browser öffnen.</summary>
    public static void HtmlAusgabe(DateOnly ab, int tage, bool lehrer)
    {
        var d = AppZustand.D;
        var liste = new List<DateOnly>();
        var x = Kalender.IstUnterrichtstag(d, ab) ? ab : Kalender.NaechsterUnterrichtstag(d, ab);
        for (var i = 0; i < Math.Max(1, tage); i++)
        {
            liste.Add(x);
            x = Kalender.NaechsterUnterrichtstag(d, x);
        }
        var pfad = Path.Combine(Path.GetTempPath(), lehrer ? "UntPlan_Vertretungsplan_Lehrer.html" : "UntPlan_Vertretungsplan.html");
        File.WriteAllText(pfad, Export.VertretungsplanHtml(d, liste, lehrer));
        HauptFenster.Starten(pfad);
    }

    /// <summary>Aushang des Vertretungsplans (Klassen- bzw. Lehrerversion je nach Ansicht).</summary>
    public void Drucken(bool vorschau) => Druck.VertretungsplanDruck.Drucken(this, Von, Bis, _ansicht.SelectedIndex is 1 or 3, vorschau);

    /// <summary>
    /// Selbsttest der Bearbeitung unter dem Filter „Offene Vertretungen“: Vertreter eintragen – Zeile bleibt sichtbar und aktuell,
    /// Text in derselben Zeile sofort eingeben, Neuaufbau während einer Eingabe wird aufgeschoben, erst beim Zeilenwechsel
    /// verschwindet die Zeile. Wirft bei einer Abweichung.
    /// </summary>
    /// <summary>Selbsttest: die Auswahlliste „Vertreter“ öffnet breit genug für Kurz- und Langnamen (nie in Spaltenbreite).</summary>
    /// <summary>Selbsttest: ein Stundentausch steht wie im Vorbild je Position in einer Zeile „Tausch“ mit magenta hinterlegten geänderten Feldern.</summary>
    internal string SelbsttestTausch()
    {
        var tausch = _g.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Vertretung { Art: VertretungsArt.Tausch }).ToList();
        if (tausch.Count < 2) throw new InvalidOperationException($"Stundentausch: {tausch.Count} Zeile(n) „Tausch“ statt 2.");
        var paare = tausch.Select(r => (Vertretung)r.Tag!).Select(v => v.VerlegungsId).ToHashSet();
        if (_g.Rows.Cast<DataGridViewRow>().Any(r => r.Tag is Vertretung { Art: VertretungsArt.Entfall } q && paare.Contains(q.VerlegungsId)))
            throw new InvalidOperationException("Stundentausch: wegverlegte Stunden stehen zusätzlich als Entfall in der Liste.");
        var r0 = tausch[0];
        var nach = r0.Cells["Nach"].FormattedValue?.ToString() ?? "";
        var von = r0.Cells["Von"].FormattedValue?.ToString() ?? "";
        if (von.Length == 0 || nach.Length == 0) throw new InvalidOperationException("Stundentausch: „Vertr. von“ bzw. „(Le.) nach“ fehlt in der Tauschzeile.");
        _g.ClearSelection();
        _g.CurrentCell = r0.Cells["Nr"];
        return $"Tausch: {tausch.Count} Zeilen, von {von}, nach {nach}, ({r0.Cells["Lehrer"].FormattedValue}) → {r0.Cells["Vertreter"].FormattedValue}";
    }

    /// <summary>
    /// Selbsttest: Veranstaltung mit allen Lehrkräften und Klassen – ihre Entfälle/Freisetzungen stehen je Tag in wenigen Zeilen
    /// („Veranstaltungen zusammenfassen“), sind dort nicht als einzelne Vertretung bearbeitbar und lassen sich einzeln zeigen.
    /// </summary>
    internal string SelbsttestVeranstaltungAlle(int vaId, DateOnly datum, Action warten)
    {
        _offen.Checked = _kollisionen.Checked = false;
        DatumGewechselt(datum);
        _vaSammeln.Checked = true;
        warten();
        List<DataGridViewRow> Zeilen() => _g.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Vertretung v && v.VeranstaltungId == vaId).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Neuaufbau(true);
        var ms = sw.ElapsedMilliseconds;
        var gesammelt = Zeilen();
        var sammel = gesammelt.Where(r => _vaSammel.Contains(((Vertretung)r.Tag!).Id)).ToList();
        if (sammel.Count == 0) throw new InvalidOperationException("Veranstaltung mit allen Lehrkräften: keine zusammengefasste Zeile.");
        var klassen = sammel[0].Cells["AKlassen"].FormattedValue?.ToString() ?? "";
        var stunde = sammel[0].Cells["Std"].FormattedValue?.ToString() ?? "";
        // Einzelbearbeitung der Sammelzeile ist gesperrt (nur Text)
        _g.CurrentCell = sammel[0].Cells["Vertreter"];
        _g.BeginEdit(false);
        var gesperrt = !_g.IsCurrentCellInEditMode;
        _g.CancelEdit();
        _g.EndEdit();
        if (!gesperrt) throw new InvalidOperationException("Zusammengefasste Zeile einer Veranstaltung lässt sich als Vertretung bearbeiten.");
        _vaSammeln.Checked = false;
        warten();
        var einzeln = Zeilen().Count;
        _vaSammeln.Checked = true;
        warten();
        if (einzeln <= gesammelt.Count) throw new InvalidOperationException($"Zusammenfassen: {gesammelt.Count} Zeilen zusammengefasst, {einzeln} einzeln.");
        return $"Veranstaltung alle: {einzeln} Zeilen → {gesammelt.Count} ({sammel.Count} zusammengefasst, Std. {stunde}, Klassen „{(klassen.Length > 40 ? klassen[..40] + "…" : klassen)}“), Neuaufbau {ms} ms";
    }

    internal string SelbsttestListenbreite(Action warten)
    {
        var spalte = _g.Columns["Vertreter"] ?? throw new InvalidOperationException("Spalte Vertreter fehlt");
        var zeile = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Vertretung { Id: > 0, Fixiert: false, Art: not (VertretungsArt.Freisetzung or VertretungsArt.Veranstaltung) } &&
                        !r.Cells[spalte.Index].ReadOnly && r.Cells[spalte.Index] is DataGridViewComboBoxCell)
                    ?? throw new InvalidOperationException("keine bearbeitbare Zeile");
        var ergebnis = new List<int>();
        for (var versuch = 0; versuch < 3; versuch++)
        {
            _g.CurrentCell = zeile.Cells[spalte.Index];
            _g.BeginEdit(false);
            warten();
            if (_g.EditingControl is not ComboBox cb) throw new InvalidOperationException("keine Auswahlliste beim Bearbeiten");
            ergebnis.Add(cb.DropDownWidth);
            if (cb.DropDownWidth < Math.Max(220, spalte.Width + 60)) throw new InvalidOperationException($"Auswahlliste zu schmal: {cb.DropDownWidth} px bei Spaltenbreite {spalte.Width} px (Versuch {versuch + 1})");
            _g.CancelEdit();
            _g.EndEdit();
            _g.CurrentCell = zeile.Cells[0];
            warten();
        }
        return $"Vertreter-Liste {string.Join("/", ergebnis)} px breit (Spalte {spalte.Width} px)";
    }

    internal string SelbsttestBearbeiten(Action warten)
    {
        _offen.Checked = true;
        warten();
        var zeile = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Vertretung { Art: VertretungsArt.Vertretung, Offen: true, Fixiert: false });
        if (zeile?.Tag is not Vertretung v) return "keine offene Vertretung";
        var id = v.Id;
        var lehrer = VertretungsVorschlag.Berechnen(D, v).Select(k => k.Lehrer)
            .FirstOrDefault(l => Vertretungen.LehrerBelegt(D, Plan(v.Datum), l.Id, v.Stunde, v) is null && Absenzen.Lehrer(D, l.Id, v.Datum, v.Stunde) is null);
        // Nachbarstunden (z. B. Doppelstunde) bleiben unverändert – jede Zeile wird einzeln bearbeitet
        var nachbarn = D.Vertretungen.Where(x => x.Id != id && x.Datum == v.Datum && x.UnterrichtId == v.UnterrichtId && x.ZeilenIndex == v.ZeilenIndex && Math.Abs(x.Stunde - v.Stunde) == 1)
            .ToDictionary(x => x.Id, x => (x.VertreterId, x.Text));
        if (lehrer is null) return "kein freier Vertreter";
        DataGridViewRow Zeile() => _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Vertretung x && x.Id == id)
                                   ?? throw new InvalidOperationException($"Vertretung {id} ist nach der Eingabe verschwunden.");
        void Eingeben(string spalte, string wert, string vorher)
        {
            var r = Zeile();
            _g.ClearSelection();
            r.Selected = true;
            _g.CurrentCell = r.Cells[spalte];
            // wie eine abgeschlossene Eingabe: Wert in der Zelle, danach „Zelle verlassen“
            _wertVorher = vorher;
            _g.CurrentCell.Value = wert;
            ZelleGeaendert(_g, new DataGridViewCellEventArgs(_g.CurrentCell.ColumnIndex, _g.CurrentCell.RowIndex));
            warten();
            if (_g.CurrentRow?.Tag is not Vertretung c || c.Id != id || _g.CurrentCell?.OwningColumn.Name != spalte)
                throw new InvalidOperationException($"Nach der Eingabe in „{spalte}“ ist nicht mehr Vertretung {id} / {spalte} aktuell.");
        }
        Eingeben("Vertreter", lehrer.Kurzname, "???");
        if (D.Vertretungen.First(x => x.Id == id) is { Offen: true }) throw new InvalidOperationException("Vertreter wurde nicht übernommen.");

        Zeile();
        // gleich weiter in derselben Zeile: Text
        Eingeben("Text", "Selbsttest", "");
        if (D.Vertretungen.First(x => x.Id == id).Text != "Selbsttest") throw new InvalidOperationException("Text wurde nicht übernommen.");
        if (nachbarn.Any(n => D.Vertretungen.FirstOrDefault(x => x.Id == n.Key) is { } x && (x.VertreterId, x.Text) != n.Value))
            throw new InvalidOperationException("Eine Nachbarstunde wurde mitgeändert – jede Zeile soll einzeln bearbeitet werden.");
        // Neuaufbau während einer Eingabe wird aufgeschoben
        _g.CurrentCell = Zeile().Cells["Text"];
        _g.BeginEdit(false);
        Neuaufbau(true);
        if (!_g.IsCurrentCellInEditMode || !_neuaufbauAusstehend) throw new InvalidOperationException("Neuaufbau während der Eingabe wurde nicht aufgeschoben.");
        _g.CancelEdit();
        _g.EndEdit();
        warten();
        if (_neuaufbauAusstehend) throw new InvalidOperationException("Aufgeschobener Neuaufbau wurde nicht nachgeholt.");
        // Zeilenwechsel: jetzt verschwindet die nicht mehr offene Zeile
        var behalten = _behalten.Ids.ToHashSet();
        if (!behalten.Contains(id)) throw new InvalidOperationException("Bearbeitete Zeile wurde nicht bis zum Zeilenwechsel behalten.");
        var andere = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Vertretung x && x.Id != 0 && !behalten.Contains(x.Id));
        if (andere != null)
        {
            _g.ClearSelection();
            andere.Selected = true;
            _g.CurrentCell = andere.Cells["Nr"];
            warten();
            if (_g.Rows.Cast<DataGridViewRow>().Any(r => r.Tag is Vertretung { OffenOderRaumOffen: false } x && behalten.Contains(x.Id))) throw new InvalidOperationException("Bearbeitete Zeile(n) verschwinden beim Zeilenwechsel nicht.");
        }
        return $"Vertretung {id}: Vertreter {lehrer.Kurzname} und Text eingegeben, Zeile blieb bis zum Zeilenwechsel";
    }
}

