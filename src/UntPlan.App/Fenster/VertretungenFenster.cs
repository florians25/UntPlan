using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vertretungsfenster (Vertretungsmodus): alle Vertretungen des Planungszeitraums mit Ansichten Klasse/Lehrer/Druck,
/// Filter "Offene Vertretungen" und "Kollisionen", direkter Eingabe des Vertreters ("+" = Vertr. o. Lehrkraft, "-" = Entfall)
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
    /// <summary>Sortierung per Klick auf die Spaltenüberschrift (wie in Untis); null = Standard (Datum, Klasse bzw. Lehrer, Stunde).</summary>
    private string? _sortSpalte;
    private bool _sortAbsteigend;
    private readonly CheckBox _kollisionen = new() { Text = "Kollisionen", AutoSize = true };
    private readonly ComboBox _element = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ToolStripButton _inhalt = new("Felder mit Inhalt", Symbole.Get("uebersicht", 16)) { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Felder mit Inhalt: alle Spalten mit einer Eingabe zeigen – erneuter Klick stellt die ursprünglichen Spalten wieder her" };
    /// <summary>Spalten, die vor „Felder mit Inhalt“ sichtbar waren.</summary>
    private HashSet<string>? _vorInhalt;
    /// <summary>Zusammengefasste Zeilen einer Veranstaltung (je Tag eine Zeile): Stellvertreter-Id → alle Stundenzeilen.</summary>
    private readonly Dictionary<int, List<Vertretung>> _gruppen = new();
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
        ts.Items.Add(Knopf("Vtr. ohne Lehrer (+)", "planen", OhneLehrkraft, false));
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
        filter.Controls.Add(Gruppe("Ansicht einschränken auf …", _offen, _kollisionen));

        // Fußleiste wie Untis: Vtr-Nr. links, Ansicht ganz rechts (keine Zeilen-/Offen-Zählung)
        var unten = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Ui.Kopf };
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 3, 0, 0), WrapContents = false, BackColor = Ui.Kopf };
        links.Controls.Add(new Label { Text = "Vtr-Nr.:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        links.Controls.Add(_nr);
        links.Controls.Add(_status);
        var rechts = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 3, 6, 0), WrapContents = false, BackColor = Ui.Kopf };
        rechts.Controls.Add(new Label { Text = "Ansicht:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        rechts.Controls.Add(_ansicht);
        unten.Controls.Add(links);
        unten.Controls.Add(rechts);

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
            // bearbeitbare Felder: im Vertretungsmodus weiß wie Untis (nicht bearbeitbare grünlich, siehe Auswahlbreite.ZellFarben)
            if (!lesbar && !Ui.Vertretungsmodus) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 236);
        }
        // Reihenfolge wie in Untis: Vtr-Nr. Art Datum Stunde (Fach) Fach (Lehrer) Vertreter (Klasse(n)) Klasse(n) (Raum) Raum Vertr. von (Le.) nach Text Statistik …
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
            if (v.Art == VertretungsArt.Freisetzung && spalte is "Vertreter" or "NFach" or "NRaum") e.Cancel = true;
        };
        _g.EditingControlShowing += EditorZeigen;
        Steuerelemente.SuchKombi.Aktivieren(_g, VertreterStil);
        // Auswahl aus der Liste (Maus bzw. Enter) wie in Untis sofort übernehmen: Bearbeitung beenden, die Zeile bleibt aktuell
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
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && (e.ColumnIndex < 0 || _g.Columns[e.ColumnIndex].ReadOnly)) VorschlagOeffnen(); };
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
    /// Felder der Ansicht (wie Untis): Spaltenname, Überschrift, Bezeichnung im Dialog „Felder der Ansicht“, Breite, nur lesen,
    /// standardmäßig aktiv, Anzeige wahlweise als Kurz- oder Langname.
    /// </summary>
    internal static readonly (string Name, string Kopf, string Feld, int Breite, bool Lesbar, bool Standard, bool Lang)[] Spalten =
    {
        ("Nr", "Vtr-Nr.", "Vertretungsnummer", 52, true, true, false), ("Art", "Art", "Art der Vertretung", 118, true, true, false),
        ("Datum", "Datum", "Datum (TT.MM.)", 50, true, true, false), ("Tag", "Tag", "Tag", 30, true, false, false),
        ("Std", "Stunde", "Std.", 46, true, true, false), ("Zeit", "Zeit", "Beginn- und Endzeit", 82, true, false, false),
        ("Fach", "(Fach)", "statt Fach", 52, true, true, true), ("NFach", "Fach", "Fach", 52, false, true, true),
        ("Lehrer", "(Lehrer)", "statt Lehrer", 62, true, true, true), ("Vertreter", "Vertreter", "Vertreter", 66, false, true, true),
        ("AKlassen", "(Klasse(n))", "statt Klasse(n)", 70, true, true, true), ("Klassen", "Klasse(n)", "Klasse(n)", 70, true, true, true),
        ("Raum", "(Raum)", "statt Raum", 54, true, true, true), ("NRaum", "Raum", "Raum", 54, false, true, true),
        ("Von", "Vertr. von", "Verlegung", 80, true, true, false), ("Nach", "(Le.) nach", "Wegverlegung", 80, true, true, false),
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

    private bool Langname(string spalte) => FeldEinstellungen().TryGetValue(spalte, out var x) && x.Contains('L');

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
            var probe = Dokument.Klonen(D);
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

    /// <summary>Kollision: Vertreter ist zur Stunde anderweitig belegt bzw. abwesend, Raum doppelt belegt.</summary>
    private string? Kollision(Vertretung v)
    {
        if (v.Id == 0) return null;
        if (_gruppen.TryGetValue(v.Id, out var gruppe) && gruppe.Count > 1)
            return gruppe.Select(KollisionEinzeln).FirstOrDefault(k => k != null);
        return KollisionEinzeln(v);
    }

    /// <summary>Art der Kollision für die Hervorhebung: Lehrer (Vertreter-Feld) oder Raum.</summary>
    private (bool Lehrer, bool Raum) KollisionsFelder(Vertretung v)
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
        // Ansicht „Pausenaufsichten“ zeigt nur diese; in den übrigen Ansichten stehen sie wie in Untis mit in der Liste
        if (_ansicht.SelectedIndex == 4 && v.Art != VertretungsArt.Pausenaufsicht) return false;
        // gerade bearbeitete Zeile bleibt bis zum Zeilenwechsel sichtbar, auch wenn sie nicht mehr zum Filter passt
        var behalten = _behalten.Enthaelt(v.Id);
        if (_offen.Checked && !v.OffenOderRaumOffen && !behalten) return false;
        if (_kollisionen.Checked && KollisionEinzeln(v) is null && !behalten) return false;
        // wie Untis: Veranstaltungen selbst stehen nicht in der Vertretungsliste, nur die zugehörigen Freisetzungen/Entfälle
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
        _gruppen.Clear();
        var liste = Zeilenliste();
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
        IEnumerable<Vertretung> liste = Zusammenfassen(Vertretungen.ImZeitraum(D, Von, Bis).Where(Passt).ToList());
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

    /// <summary>Klick auf eine Spaltenüberschrift: nach dieser Spalte sortieren, erneuter Klick kehrt die Richtung um (wie in Untis).</summary>
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
        foreach (var g in liste.GroupBy(v => v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int va ? $"VA{va}/{v.Datum}" : $"#{v.Id}"))
        {
            var zeilen = g.OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
            res.Add(zeilen[0]);
            if (zeilen.Count > 1 || zeilen[0].Art == VertretungsArt.Veranstaltung) _gruppen[zeilen[0].Id] = zeilen;
        }
        return res;
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
        if (v.Art == VertretungsArt.Freisetzung && neueKlassen.Length > 0) neueKlassen = $"({neueKlassen})";
        var zeit = D.Zeitraster.Zeiten.FirstOrDefault(z => z.Nr == (gruppe?.Min(x => x.Stunde) ?? v.Stunde)) is { } z1 && D.Zeitraster.Zeiten.FirstOrDefault(z => z.Nr == (gruppe?.Max(x => x.Stunde) ?? v.Stunde)) is { } z2
            ? $"{z1.Beginn:HH\\:mm}-{z2.Ende:HH\\:mm}" : "";
        var zaehler = v.IstEntfall && v.Art != VertretungsArt.BereitschaftEntfaellt ? "-1" : v.VertreterId.HasValue && v.VertreterId != v.LehrerId && Vertretungen.ZaehlendeArten.Contains(v.Art) ? "+1" : "";
        var abteilung = v.NeueKlassenIds.Select(k => Ix.Klassen.GetValueOrDefault(k)?.AbteilungId).FirstOrDefault(x => x.HasValue);
        var benachrichtigen = v.VertreterId is int vt && v.Datum == DateOnly.FromDateTime(DateTime.Today) &&
                              !Plan(v.Datum).VonLehrer(vt).Any(e => e.Stunde < v.Stunde && e.Vertretung != v) ? "X" : "";
        return new object?[]
        {
            v.Id == 0 ? "" : v.Id, Vertretungsplan.ArtText(D, v.Art), $"{v.Datum:dd.MM.}", tag, stunde, zeit,
            // „Fach“ bleibt wie in Untis leer, solange kein Vertretungsfach eingetragen ist (der Druck setzt dann das ursprüngliche Fach ein)
            Anzeige(ElementTyp.Fach, v.FachId, "Fach"), v.IstEntfall ? "---" : Anzeige(ElementTyp.Fach, v.VertretungsfachId, "NFach"),
            lehrer, vertreter, Klassen(v.KlassenIds, "AKlassen"), neueKlassen,
            v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId) : Anzeige(ElementTyp.Raum, v.RaumId, "Raum"), raumNeu,
            Pos(v.VonDatum, v.VonStunde), Pos(v.NachDatum, v.NachStunde), v.Text, v.Statistik, v.Schuelergruppe,
            Langname("Grund") && D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == v.GrundId) is { } gl ? gl.Langname : v.Grund,
            u?.Text ?? "", u != null && v.ZeilenIndex < u.Zeilen.Count ? u.Zeilen[v.ZeilenIndex].Zeilentext : "", v.Beschreibung,
            v.IstEntfall ? "X" : "", v.Mitbetreuung ? "X" : "", v.Kopplung > 0 ? v.Kopplung.ToString() : "", Vertretungen.IstNeu(v, Doc.Sitzungsbeginn) ? "Neu" : "",
            v.Fixiert ? "X" : "", v.NichtDrucken ? "N" : "", v.Text2, benachrichtigen, zaehler, "", v.Wert?.ToString("0.##") ?? "",
            Anzeige(ElementTyp.Abteilung, abteilung, "Abt"), v.Benutzer, v.Mehrarbeit ? "x" : "",
        };
    }

    /// <summary>Hintergrund des Vertreterfeldes einer Pausenaufsicht (wie in Untis türkis).</summary>
    private static readonly Color PausenFarbe = Color.FromArgb(160, 236, 246);

    /// <summary>
    /// Darstellung wie in Untis: alle Werte in schwarzer Schrift; offene Felder „???“ rot (nicht fett), „---“ (Entfall) schwarz,
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
        if (l is null) return text switch { "+++" => new(Color.White, Color.Black, "Vtr. ohne Lehrer"), "---" => new(Color.White, Color.Black, "Entfall"), _ => null };
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
    /// (wie in Untis), in einem editierbaren Feld (Text, Raum, Fach …) wird dessen Inhalt gelöscht.
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
        var zurueck = liste.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt);
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
        Ausfuehren("Vtr. ohne Lehrer", liste.Select(v => v.Id).ToList(), (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer));
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
        Aendern("Vertretungen neu berechnet", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, Von, Bis));
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
            cm.Items.Add($"Vtr. ohne Lehrer{(liste.Count > 1 ? $" ({liste.Count} Stunden)" : "")}", Symbole.Get("planen"), (_, _) => OhneLehrkraft());
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

/// <summary>
/// Vertretungsvorschlag in einem eigenen Fenster: folgt der markierten Vertretung. Reiter Vertretungsvorschläge
/// (mit Mini-Stundenplan, Merker, Zähler und Vorziehungen), Betreuungen und Räume.
/// </summary>
public sealed class VertretungsvorschlagFenster : MdiFenster
{
    private readonly Label _kopf = new() { Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 5, 0, 0), Font = Ui.SchriftFett };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorziehung = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _betreuung = new() { Dock = DockStyle.Fill };
    private readonly Steuerelemente.RaumListe _raeume = new("vorschlag", new[] { "Rm", "Kap", "AR", "SR", "B", "RG" }) { Dock = DockStyle.Fill };
    private readonly ToolStripButton _zusatzRaum = new("Zusätzlichen Raum zuordnen") { CheckOnClick = true, ToolTipText = "Den gewählten Raum zusätzlich zum bestehenden Raum zuordnen" };
    private readonly ToolStripButton _block = new("Stundenblock") { CheckOnClick = true, ToolTipText = "Vorschlag für alle Stunden des Blocks (Doppelstunde)" };
    private readonly ToolStripButton _alle = new("Alle Lehrer") { CheckOnClick = true, ToolTipText = "Auch gesperrte und weit entfernte Lehrer anzeigen" };
    private readonly ToolStripButton _besetzt = new("Auch besetzte Räume anzeigen") { CheckOnClick = true };
    private readonly ToolStripButton _weiter = new("Weiter zur nächsten offenen") { CheckOnClick = true, Checked = true, ToolTipText = "Nach dem Zuweisen automatisch die nächste offene Vertretung wählen" };
    private readonly ToolStripButton _nahe = new("Nur in der Nähe") { CheckOnClick = true, ToolTipText = "Nur gekoppelte Lehrkräfte, gleicher Gang oder gleiches Stockwerk" };
    private readonly ToolStripButton _dauer = new("Dauervertretung") { CheckOnClick = true, ToolTipText = "Vertreter für alle Stunden dieses Unterrichts während der Absenz übernehmen" };
    private Vertretung? _v;

    public VertretungsvorschlagFenster()
    {
        Text = "Vertretungsvorschlag";
        HilfeThema = "Vertretungsvorschlag";
        Icon = SymbolIcon("vertretung");
        Size = new Size(760, 440);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Vertreter übernehmen", "planen", Uebernehmen, false));
        ts.Items.Add(Knopf("Entfall", "entfall", () => Aktion("Entfall", (d, v) => Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer)), false));
        ts.Items.Add(Knopf("Vertr. o. Lehrkraft", "lehrer", () => Aktion("Vertr. o. Lehrkraft", (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer)), false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(_block);
        ts.Items.Add(_dauer);
        ts.Items.Add(_weiter);
        ts.Items.Add(_alle);
        ts.Items.Add(Knopf("Einstellungen Vertretungsvorschlag", "gewichtung", () => { if (VertretungsEinstellungenDialog.Zeigen(this, 2)) Neuaufbau(); }));
        _block.CheckedChanged += (_, _) => Neuaufbau();
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        _besetzt.CheckedChanged += (_, _) => Neuaufbau();

        Ui.RasterGestalten(_vorschlag, false);
        _vorschlag.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorschlag.MultiSelect = false;
        foreach (var (n, k, b) in new[]
        {
            ("Name", "Name", 70), ("Stpl", "Stundenplan", 170), ("Merker", "Merker", 50), ("Lage", "Lage", 85), ("Vertr", "Vertr.", 45), ("Entf", "Entfall", 50),
            ("Zaehler", "Zähler", 50), ("Woche", "Woche", 48), ("Monat", "Monat", 48), ("Punkte", "Pkt.", 42), ("Kr", "Kriterien", 220),
        })
            _vorschlag.Columns[_vorschlag.Columns.Add(n, k)].Width = b;
        _vorschlag.Columns["Kr"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Uebernehmen(); };
        _vorschlag.CellPainting += MiniPlanZeichnen;
        _vorschlag.CellFormatting += VorschlagFormatieren;
        _vorschlag.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Uebernehmen(); e.Handled = true; } };

        Ui.RasterGestalten(_vorziehung, false);
        _vorziehung.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorziehung.MultiSelect = false;
        _vorziehung.Columns[_vorziehung.Columns.Add("B", "Vorziehungen (Doppelklick = Unterricht in die offene Stunde verlegen)")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _vorziehung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Vorziehen(); };

        Ui.RasterGestalten(_betreuung, false);
        _betreuung.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _betreuung.MultiSelect = false;
        foreach (var (n, k, b) in new[] { ("L", "Lehrer", 70), ("N", "Nähe", 110), ("K", "Klasse(n)", 90), ("F", "Fach", 55), ("R", "Raum", 60), ("G", "Gang", 55), ("KK", "kennt Kl.", 60), ("H", "Betr. heute", 70), ("Z", "Zähler", 55) })
            _betreuung.Columns[_betreuung.Columns.Add(n, k)].Width = b;
        _betreuung.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Betreuung(); e.Handled = true; } };
        _betreuung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Betreuung(); };

        _raeume.Gewaehlt += _ => Raum();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_vorschlag);
        split.Panel2.Controls.Add(_vorziehung);
        var s1 = new TabPage("Vertretungsvorschläge");
        s1.Controls.Add(split);
        var s2 = new TabPage("Betreuungen");
        var betrLeiste = Werkzeugleiste();
        betrLeiste.Items.Add(Knopf("Betreuung übernehmen (Doppelklick, Enter)", "planen", Betreuung, false));
        betrLeiste.Items.Add(_nahe);
        betrLeiste.Items.Add(new ToolStripLabel("grün = gekoppelt bzw. gleicher Gang") { ForeColor = Color.DimGray });
        _nahe.CheckedChanged += (_, _) => Neuaufbau();
        s2.Controls.Add(_betreuung);
        s2.Controls.Add(betrLeiste);
        var raumLeiste = Werkzeugleiste();
        raumLeiste.Items.Add(Knopf("Raum übernehmen", "raumzuordnen", Raum, false));
        raumLeiste.Items.Add(_besetzt);
        raumLeiste.Items.Add(_zusatzRaum);
        var s3 = new TabPage("Räume");
        s3.Controls.Add(_raeume);
        s3.Controls.Add(raumLeiste);
        _tabs.TabPages.AddRange(new[] { s1, s2, s3 });
        Controls.Add(_tabs);
        Controls.Add(_kopf);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(80, (int)(split.Height * 0.7));
        AppZustand.VertretungGewaehlt += Gewaehlt;
        FormClosed += (_, _) => AppZustand.VertretungGewaehlt -= Gewaehlt;
        _v = AppZustand.AktiveVertretung;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
    }

    /// <summary>
    /// Nach einer Zuweisung aus diesem Fenster („Weiter zur nächsten offenen“) zur nächsten offenen Vertretung springen.
    /// Nur nach eigener Zuweisung – Änderungen im Vertretungsfenster (z. B. ein Text) lassen die Zeile dort stehen.
    /// </summary>
    private void Weiter(bool warOffen)
    {
        if (!warOffen || !_weiter.Checked || _v is null || _v.OffenOderRaumOffen) return;
        BeginInvoke(() => AppZustand.Haupt.MdiChildren.OfType<VertretungenFenster>().FirstOrDefault()?.NaechsteOffene());
    }

    private void Gewaehlt(Vertretung? v)
    {
        if (IsDisposed) return;
        _v = v is null ? null : D.Vertretungen.FirstOrDefault(x => x.Id == v.Id) ?? v;
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        _vorschlag.Rows.Clear();
        _vorziehung.Rows.Clear();
        _betreuung.Rows.Clear();
        _raeume.Setzen(Array.Empty<RaumMoeglichkeit>());
        var v = _v;
        if (v is null)
        {
            _kopf.Text = "Bitte im Vertretungsfenster oder im tagesaktuellen Stundenplan eine Vertretung markieren.";
            return;
        }
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var mehr = AppZustand.MarkierteVertretungen.Count > 1 ? $"   (+{AppZustand.MarkierteVertretungen.Count - 1} weitere markiert)" : "";
        _kopf.Text = $"Vtr-Nr. {v.Id}   {tag} {Format.Datum(v.Datum)}   {Vertretungsplan.StundeText(v)}. Std.   {Ix.KKlassen(v.NeueKlassenIds)}   ({Ix.KLehrer(v.LehrerId)})   {Ix.KFach(v.FachId)}   " +
                     $"{(v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId) : Ix.KRaum(v.RaumId))}   – {Vertretungsplan.ArtText(D, v.Art)}{(v.Grund.Length > 0 ? ", " + v.Grund : "")}{mehr}";
        _kopf.BackColor = v.Offen ? Color.FromArgb(255, 220, 220) : Ui.BlauSehrHell;
        if (v.Art == VertretungsArt.Pausenaufsicht)
        {
            var a = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(D, v.Datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
            var (zv, zb) = Zaehlung.Zeitraum(D, v.Datum);
            foreach (var l in Core.Module.Pausenaufsichten.Kandidaten(D, v.Datum, a))
            {
                var z = Zaehlung.Lehrer(D, l, zv, zb);
                var r = _vorschlag.Rows.Add(l.Kurzname, "", "", "Pausenaufsicht", z.Vertretungen, z.Entfaelle, z.Summe.ToString("0.#"), "", "", "", $"Aufsichten: {Core.Module.Pausenaufsichten.Anzahl(D, l.Id)}");
                _vorschlag.Rows[r].Tag = l.Id;
            }
            return;
        }
        foreach (var k in VertretungsVorschlag.Berechnen(D, v, _block.Checked, _alle.Checked))
        {
            var r = _vorschlag.Rows.Add(D.Vertretungseinstellungen.Vorschlag.Langname ? k.Lehrer.VollerName : k.Lehrer.Kurzname, k.Stundenplan, k.Merker, k.Lage,
                k.Vertretungen, k.Entfaelle, k.Zaehler.ToString("+0.#;-0.#;0"), k.ZaehlerWoche, k.ZaehlerMonat, ((int)k.Punkte).ToString(), string.Join(", ", k.Kriterien));
            _vorschlag.Rows[r].Tag = k;
        }
        if (v.Offen)
            foreach (var vz in VertretungsVorschlag.Vorziehungen(D, v))
                _vorziehung.Rows[_vorziehung.Rows.Add(vz.Beschreibung)].Tag = vz;
        foreach (var b in VertretungsVorschlag.Betreuungen(D, v).Where(b => !_nahe.Checked || b.Naehe <= 2))
        {
            var r = _betreuung.Rows.Add(b.Lehrer.Kurzname, b.NaeheText, b.Klassen, b.Fach, b.Raum, b.Gang, b.KenntKlasse ? "ja" : "", b.BetreuungenHeute > 0 ? b.BetreuungenHeute.ToString() : "", b.Zaehler.ToString("0.#"));
            _betreuung.Rows[r].Tag = b;
            if (b.Naehe <= 1) _betreuung.Rows[r].DefaultCellStyle.BackColor = b.Naehe == 0 ? Color.FromArgb(205, 238, 205) : Color.FromArgb(228, 246, 228);
        }
        _tabs.TabPages[0].Text = $"Vertretungsvorschläge ({_vorschlag.Rows.Count})";
        _tabs.TabPages[1].Text = $"Betreuungen ({_betreuung.Rows.Count})";
        _raeume.Langname = D.Vertretungseinstellungen.Vorschlag.Langname;
        _raeume.Setzen(Raumvertretungen.MoeglicheRaeume(D, v, _besetzt.Checked, _block.Checked));
        _tabs.TabPages[2].Text = $"Räume ({_raeume.Raster.Rows.Count})";
    }

    private void VorschlagFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _vorschlag.Rows[e.RowIndex].Tag is not Kandidat k) return;
        var name = _vorschlag.Columns[e.ColumnIndex].Name;
        if (k.Rot) e.CellStyle.ForeColor = Color.Firebrick;
        if (name == "Name")
        {
            if (k.Bereitschaft) e.CellStyle.BackColor = Color.FromArgb(200, 235, 200);
            else if (k.Freigesetzt) e.CellStyle.BackColor = Color.FromArgb(215, 230, 250);
            if (k.Benachrichtigen) e.CellStyle.Font = Ui.SchriftFett;
        }
        if (name == "Merker")
        {
            var m = Vertretungen.MerkerWert(k.Merker);
            e.CellStyle.BackColor = k.Merker switch
            {
                "x" => Color.FromArgb(215, 230, 250),
                "0" => Color.FromArgb(200, 235, 200),
                "1" => Color.FromArgb(225, 245, 215),
                _ when m >= 9 => Color.FromArgb(255, 220, 220),
                _ when m >= 5 => Color.FromArgb(255, 240, 210),
                _ when !int.TryParse(k.Merker, out _) => Color.FromArgb(200, 235, 200),
                _ => Color.White,
            };
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
    }

    /// <summary>Spalte "Stundenplan": benachbarte Stunden des Lehrers als kleine Kästchen, die betroffene Stunde hervorgehoben.</summary>
    private void MiniPlanZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _vorschlag.Columns[e.ColumnIndex].Name != "Stpl" || e.Graphics is null) return;
        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        var teile = (e.FormattedValue?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length > 0)
        {
            var b = Math.Min(34, (e.CellBounds.Width - 4) / teile.Length);
            var x = e.CellBounds.X + 2;
            foreach (var t in teile)
            {
                var aktuell = t.StartsWith('[');
                var text = t.Trim('[', ']');
                var r = new Rectangle(x, e.CellBounds.Y + 2, b - 2, e.CellBounds.Height - 5);
                var frei = text is "·" or "";
                using (var br = new SolidBrush(aktuell ? Color.FromArgb(255, 244, 200) : frei ? Color.White : Ui.BlauSehrHell)) e.Graphics.FillRectangle(br, r);
                using (var p = new Pen(aktuell ? Color.FromArgb(230, 160, 0) : Ui.Rasterlinie, aktuell ? 2 : 1) { DashStyle = aktuell ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid }) e.Graphics.DrawRectangle(p, r);
                TextRenderer.DrawText(e.Graphics, frei ? "" : text, Ui.SchriftKlein, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += b;
            }
        }
        e.Handled = true;
    }

    private List<int> Ziele()
    {
        if (_v is null) return new();
        var ids = AppZustand.MarkierteVertretungen.Contains(_v.Id) ? AppZustand.MarkierteVertretungen.ToList() : new List<int> { _v.Id };
        if (_block.Checked) ids.AddRange(VertretungsVorschlag.Block(D, _v).Select(x => x.Id));
        if (_dauer.Checked && _v.UnterrichtId is int uid)
        {
            // Dauervertretung: alle offenen Stunden desselben Unterrichts aus derselben Absenz ab diesem Tag
            var v = _v;
            ids.AddRange(D.Vertretungen.Where(x => x.UnterrichtId == uid && x.ZeilenIndex == v.ZeilenIndex && x.Datum >= v.Datum && x.Offen &&
                (v.AbwesenheitId is null ? x.LehrerId == v.LehrerId : x.AbwesenheitId == v.AbwesenheitId)).Select(x => x.Id));
        }
        return ids.Distinct().ToList();
    }

    private void Aktion(string text, Action<Schuldatei, Vertretung> a)
    {
        var ids = Ziele();
        if (ids.Count == 0) return;
        var warOffen = _v?.OffenOderRaumOffen == true;
        Aendern(ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, AenderungsArt.Vertretung, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList()) a(d, v);
        });
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
        Weiter(warOffen);
    }

    private void Uebernehmen()
    {
        var lehrer = _vorschlag.CurrentRow?.Tag switch { Kandidat k => k.Lehrer.Id, int id => id, _ => (int?)null };
        if (lehrer is not int l) return;
        if (_dauer.Checked && _v is { } dv)
        {
            // Dauervertretung: Vertreter für dieselbe Stunde in allen Folgewochen der Absenz (Vertretungen werden dazu erzeugt)
            var id = dv.Id;
            var block = _block.Checked;
            var uebersprungen = 0;
            var warOffen = dv.OffenOderRaumOffen;
            Aendern($"Dauervertretung {Ix.KLehrer(l)}", AenderungsArt.Vertretung, d =>
                uebersprungen = Vertretungen.Dauervertretung(d, d.Vertretungen.First(x => x.Id == id), l, block, AppZustand.Benutzer).Uebersprungen.Count);
            if (uebersprungen > 0) Ui.Info($"{Ix.KLehrer(l)} ist in {uebersprungen} Stunde(n) der Folgewochen nicht frei – diese bleiben offen.");
            _v = D.Vertretungen.FirstOrDefault(x => x.Id == id);
            Neuaufbau();
            Weiter(warOffen);
            return;
        }
        Aktion($"Vertreter {Ix.KLehrer(l)}", (d, v) => Vertretungen.VertreterSetzen(d, v, l, false, AppZustand.Benutzer));
    }

    private void Betreuung()
    {
        if (_betreuung.CurrentRow?.Tag is not Betreuungskandidat b) return;
        Aktion($"Betreuung {b.Lehrer.Kurzname}", (d, v) => Vertretungen.VertreterSetzen(d, v, b.Lehrer.Id, true, AppZustand.Benutzer));
    }

    private void Raum()
    {
        if (_v is null || _raeume.Aktuell is not { } r) return;
        var loesung = RaumKollisionsLoesung.MitKollision;
        if (r.Belegung.FirstOrDefault() is { Eintrag: not null } b)
        {
            var w = RaumZuordnenVertretungDialog.KollisionFragen(this, Raumvertretungen.KollisionsText(D, r.Raum, _v.Datum, b.Stunde, r.Belegung.Where(x => x.Stunde == b.Stunde).Select(x => x.Eintrag)));
            if (w is null or RaumKollisionsLoesung.NichtVerplanen) return;
            loesung = w.Value;
        }
        var zusatz = _zusatzRaum.Checked;
        Aktion($"Raum {r.Raum.Kurzname}", (d, v) => Raumvertretungen.Zuordnen(d, v, r.Raum.Id, zusatz, loesung, AppZustand.Benutzer));
    }

    private void Vorziehen()
    {
        if (_v is null || _vorziehung.CurrentRow?.Tag is not Vorziehung vz) return;
        var id = _v.Id;
        Aendern("Vorziehung", AenderungsArt.Vertretung, d =>
        {
            var v = d.Vertretungen.First(x => x.Id == id);
            var aktuell = VertretungsVorschlag.Vorziehungen(d, v).FirstOrDefault(x => x.Datum == vz.Datum && x.Stunde == vz.Stunde && x.Eintrag.Unterricht?.Id == vz.Eintrag.Unterricht?.Id)
                          ?? throw new InvalidOperationException("Die Vorziehung ist nicht mehr möglich.");
            VertretungsVorschlag.VorziehungAusfuehren(d, v, aktuell, AppZustand.Benutzer);
        });
    }
}
