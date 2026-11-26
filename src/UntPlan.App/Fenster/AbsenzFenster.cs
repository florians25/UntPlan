using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Absenzen / Lehrer | Klassen | Räume (Reiter mit Anzahl wie im Vorbild). Direkte Eingabe in der Liste – wie im Vorbild wird jede
/// mit Tab/Enter bestätigte Eingabe sofort gespeichert (Element eintippen = ganzer Tag absent, AbsNr wird vergeben).
/// Überlappt eine Lehrer-Absenz mit einer Veranstaltung, wird gefragt, ob die Lehrkraft aus der Veranstaltung entfernt werden soll.
/// </summary>
public sealed class AbwesenheitenFenster : MdiFenster, IElementEintrag
{
    private static readonly string[] Reiter = { "Lehrer", "Klassen", "Räume" };
    /// <summary>Schrift der Absenzliste wie in den Handbuch-Bildern (Segoe UI 8,25 statt 9).</summary>
    private static Font TabellenSchrift => Ui.AbsenzSchrift;
    private readonly Steuerelemente.Reiterleiste _tabs = new();
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 100 };
    private readonly Steuerelemente.ZeitraumAuswahl _zeitraum;
    private readonly ToolStripMenuItem _vaZeigen = new("Veranstaltungen anzeigen") { CheckOnClick = true };
    private int _letzterReiter;
    private bool _laedt;
    private bool _neuaufbauAusstehend;
    private readonly Label _tage = new() { AutoSize = true };
    private readonly ToolStripButton _filterKnopf;
    private readonly ToolStripDropDownButton _sortierMenue = new("Sortieren", Symbole.Get("sortieren", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Sortieren" };
    /// <summary>Summenzeile unter der Liste (Kontextmenü „Summenzeile“).</summary>
    private readonly Label _summe = new() { Dock = DockStyle.Bottom, Height = 17, Visible = false, Padding = new Padding(4, 2, 0, 0), BackColor = Ui.Kopf };
    /// <summary>Filtertext (Kurzname, Grund oder Text enthält ihn); leer = alle Absenzen.</summary>
    private string _filter = "";
    /// <summary>Sortierung der Liste: 0 = Element und Datum (Standard), 1 = Datum, 2 = Absenznummer, 3 = Absenzgrund.</summary>
    private int _sortierung;

    // Entwurf der gerade bearbeiteten Zeile: Änderungen werden erst beim Verlassen der Zeile übernommen,
    // damit z. B. eine stundenweise Absenz nicht schon nach der ersten Eingabe Vertretungen auslöst.
    private Abwesenheit? _entwurf;
    private Abwesenheit? _entwurfAlt;
    private int _entwurfZeile = -1;
    private bool _entwurfBisGesetzt;
    private int _entwurfZeileVorher = -1;
    /// <summary>Zuletzt gespeicherte einzelne Absenz (für die sofortige Übernahme aus der Eingabezeile).</summary>
    private Abwesenheit? _letzteGespeicherte;
    /// <summary>Nach dem Speichern auf diese Absenz springen.</summary>
    private int? _springeZu;
    /// <summary>Fensterverknüpfung: in einem anderen Fenster gewähltes Element – seine Absenzen werden markiert.</summary>
    private (ElementTyp Typ, int Id)? _markiert;
    private bool _zuMarkierungSpringen;
    private readonly Steuerelemente.ZellKommentare _kommentare;
    /// <summary>Mit Strg+C gemerkte Absenzen (Strg+V fügt sie am eingestellten Tag ein).</summary>
    private static List<Abwesenheit> _zwischenablage = new();
    private static string? _ablageText;

    public AbwesenheitenFenster()
    {
        // Titel wie in der Vorlage: „Absenzen / Lehrer“ (bzw. Klasse, Raum je nach Reiter)
        Text = "Absenzen / Lehrer";
        HilfeThema = "Abwesenheiten";
        // Fenstergröße wie im Handbuch-Bild (Kap. 3.2, 96 dpi)
        Size = new Size(612, 277);
        Positionieren();
        foreach (var t in Reiter) _tabs.Hinzufuegen(t + " (0)");
        // Reiterwechsel: offenen Entwurf zuerst übernehmen (früher blieb die alte Liste unter dem neuen Reiter stehen)
        _tabs.Selecting += (_, _) => EntwurfSofortUebernehmen();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == _letzterReiter) return;
            _letzterReiter = _tabs.SelectedIndex;
            Neuaufbau();
            // ein offenes Element-Rollup zeigt die Elemente des Reiters (Lehrer, Klassen bzw. Räume)
            ElementRollup.TypFolgen(Typ);
        };
        // Symbolleiste in der Reihenfolge der Vorlage: Fensterverknüpfung, Felder der Ansicht, Neu, Löschen | Filter, Sortieren,
        // Absenz kopieren (andere Elemente) | Seitenlayout ▾, Einstellungen ▾, Absenz splitten, Absenzen zusammenfassen, Aktualisieren –
        // dahinter die eigenen Funktionen (Bearbeiten, Veranstaltungen)
        // Reihenfolge der sichtbaren Symbole nach der Original-Symbolübersicht (Absenzen / Standard): Normalform anzeigen, Felder der Ansicht,
        // Neu, Löschen (rotes X), Filter, Sortieren, Element-Rollup, Seitenlayout, Einstellungen, Schuljahreskalender, Absenz splitten,
        // Aktualisieren – die übrigen eigenen Befehle im Überlauf » der Leiste
        var ts = Werkzeugleiste();
        var normalform = Knopf("Normalform anzeigen", "l_normalform", () => NormalformAnzeigen(_g));
        var felder = Knopf("Felder der Ansicht", "l_felder", () => Steuerelemente.TabellenFormate.MenueZeigen(_g, Cursor.Position));
        var neu = Knopf("Neue Absenz (Dialog)", "neu", Neu);
        var loeschen = Knopf("Löschen", "l_loeschen", Loeschen);
        ts.Items.Add(VerknuepfenKnopf());
        ts.Items.Add(normalform);
        ts.Items.Add(felder);
        ts.Items.Add(neu);
        ts.Items.Add(loeschen);
        ts.Items.Add(new ToolStripSeparator());
        _filterKnopf = Knopf("Filter", "filter", FilterSetzen);
        ts.Items.Add(_filterKnopf);
        var sortieren = _sortierMenue;
        foreach (var (text, art) in new[] { ("nach Element und Datum", 0), ("nach Datum", 1), ("nach Absenznummer", 2), ("nach Absenzgrund", 3) })
        {
            var mi = new ToolStripMenuItem(text) { Checked = art == 0, Tag = art };
            mi.Click += (_, _) => SortierungSetzen(art);
            sortieren.DropDownItems.Add(mi);
        }
        // wie im Vorbild: Sortieren und Einstellungen ohne eigenen Aufklapppfeil neben dem Symbol (Klick öffnet das Menü)
        sortieren.ShowDropDownArrow = false;
        ts.Items.Add(sortieren);
        ts.Items.Add(Knopf("Absenz kopieren (andere Elemente)", "kopieren", Kopieren));
        ts.Items.Add(new ToolStripSeparator());
        var layout = new ToolStripSplitButton("Seitenlayout", Symbole.Get("vorschau", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Seitenlayout" };
        layout.ButtonClick += (_, _) => Drucken(true);
        layout.DropDownItems.Add("Seitenansicht", Symbole.Get("vorschau", 16), (_, _) => Drucken(true));
        layout.DropDownItems.Add("Drucken", Symbole.Get("drucken", 16), (_, _) => Drucken(false));
        ts.Items.Add(layout);
        layout.Image = Symbole.Get("l_seitenlayout", 16);
        var einst = new ToolStripDropDownButton("Einstellungen", Symbole.Get("einstellungen", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Einstellungen" };
        _vaZeigen.Checked = AppZustand.Einstellungen.AbsenzenVeranstaltungenZeigen;
        _vaZeigen.CheckedChanged += (_, _) =>
        {
            AppZustand.Einstellungen.AbsenzenVeranstaltungenZeigen = _vaZeigen.Checked;
            AppZustand.Einstellungen.Speichern();
            EntwurfSofortUebernehmen();
            Neuaufbau();
        };
        einst.DropDownItems.Add(_vaZeigen);
        einst.ShowDropDownArrow = false;
        ts.Items.Add(einst);
        // Element-Rollup mit den Elementen des Reiters: mehrere markieren und in die Liste ziehen = je Element eine Absenz
        var rollup = Knopf("Element-Rollup", "rollup", () => ElementRollup.Zeigen(Typ));
        rollup.ToolTipText = "Element-Rollup: mehrere Elemente markieren und in die Liste ziehen, um sie gemeinsam absent zu setzen";
        ts.Items.Add(rollup);
        var kalender = Knopf("Schuljahreskalender: Absenzen", "l_kalender", () =>
        {
            if (_g.CurrentRow?.Tag is Abwesenheit a) AbsenzKalenderDialog.Zeigen(this, Typ, a.ElementId);
            else Ui.Info("Bitte zuerst eine Absenz bzw. ein Element in der Liste wählen.");
        });
        ts.Items.Add(kalender);
        var splitten = Knopf("Absenz splitten", "l_teilen", Teilen);
        ts.Items.Add(splitten);
        ts.Items.Add(Knopf("Absenzen zusammenfassen", "uebersicht", Zusammenfassen));
        var aktualisieren = Knopf("Aktualisieren", "l_aktualisieren", () => { EntwurfSofortUebernehmen(); Neuaufbau(); });
        ts.Items.Add(aktualisieren);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Veranstaltungen", "veranstaltung", () => HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster())));
        OriginalLeiste = new ToolStripItem?[] { normalform, felder, neu, loeschen, null, _filterKnopf, sortieren, rollup, layout, einst, kalender, splitten, aktualisieren };
        sortieren.Image = Symbole.Get("l_sortieren", 16);

        Ui.RasterGestalten(_g, true);
        // wie im Vorbild: keine Zeilenköpfe, vertiefter Rahmen unter den Reitern
        _g.RowHeadersVisible = false;
        _g.BorderStyle = BorderStyle.Fixed3D;
        // wie im Original-Bildschirmfoto (Absenzen / Standard, 100 %): Tabellenschrift Segoe UI 8,25, Zeilen und Überschrift 19 Pixel hoch (samt Linie)
        _g.Font = TabellenSchrift;
        _g.DefaultCellStyle.Font = TabellenSchrift;
        _g.ColumnHeadersDefaultCellStyle.Font = TabellenSchrift;
        _g.RowTemplate.Height = Ui.ZeilenHoehe(TabellenSchrift);
        _g.ColumnHeadersHeight = Ui.ZeilenHoehe(TabellenSchrift);
        Ui.ZeilenmarkeErsteSpalte(_g);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        // Überschriften und Breiten wie im Original-Bildschirmfoto (Absenzen / Standard, 100 %): AbsNr. 43 | Lehrer 80 | Von TT.MM. 43 |
        // ab St. 67 | Bis TT.MM. 43 | bis St. 67 | Grund 67 | Text 67 | Anrechnung 67 | Entfalltage 65 | Absenztage 72 – zu lange
        // Überschriften werden abgeschnitten („Von TT“), rechts davon bleibt die Fläche frei (keine Spalte füllt das Fenster)
        Ui.BreitenWieOriginal(_g);
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nr", HeaderText = "AbsNr.", Width = 44, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("E", "Lehrer", 80, Array.Empty<string>()));
        foreach (var (n, k, b) in new[] { ("V", "Von TT.MM.", 43), ("SV", "ab St.", 67), ("B", "Bis TT.MM.", 43), ("SB", "bis St.", 67) })
            _g.Columns.Add(n is "V" or "B" ? Steuerelemente.DatumSpalte.Neu(n, k, b) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("G", "Grund", 67, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 67 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Beg", HeaderText = "Begleitlehrer", Width = 100 });
        // weitere Felder sind zunächst ausgeblendet und werden über „Felder der Ansicht“ eingeblendet
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "O", HeaderText = "offen", Width = 45, ReadOnly = true, Visible = false });
        // wie im Original sichtbar: Anrechnung, Entfalltage und Absenztage (Unterrichtstage im Zeitraum)
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Anr", HeaderText = "Anrechnung", Width = 67, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ent", HeaderText = "Entfalltage", Width = 65, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Abs", HeaderText = "Absenztage", Width = 72, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        // Reihenfolge wie im Vorbild: … Grund, Text, Anrechnung, Entfalltage, Absenztage, dann die weiteren Spalten
        var nachText = _g.Columns["T"]!.DisplayIndex;
        _g.Columns["Anr"]!.DisplayIndex = nachText + 1;
        _g.Columns["Ent"]!.DisplayIndex = nachText + 2;
        _g.Columns["Abs"]!.DisplayIndex = nachText + 3;
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        // Statuszeile wie in der Vorlage mit kurzem Text („Absent bis: TT.MM.“, „Absenzgrund“); die ausführliche Hilfe als Tooltip der Überschrift
        Ui.Spaltenhilfe(_g, c => StatuszeilenText(c.Name, Typ));
        foreach (DataGridViewColumn c in _g.Columns) c.ToolTipText = SpaltenhilfeText(c.Name, ElementTyp.Lehrer) ?? "";
        Steuerelemente.SuchKombi.Aktivieren(_g, (zelle, text) =>
        {
            Element? el = zelle.OwningColumn.Name == "G" ? D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname == text) : D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname == text);
            // wie im Vorbild (Kap. 3.2): Lehrer mit Lehrersymbol, Kurzname und Langname
            if (el is Lehrer l) return new Steuerelemente.ListenStil(Color.White, Color.Black, l.Langname.Length > 0 ? l.Langname : l.VollerName, Symbole.Get("unterricht", 16));
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el.Langname);
        });
        // zu lange Texte wie im Original abgeschnitten, ohne „…“
        Ui.OhneAuslassungspunkte(_g);
        // Prüfung vor der Übernahme: ungültige Daten bzw. Stunden bleiben rot markiert in der Zelle stehen (Hinweis unter der Zelle,
        // Tooltip am Fehlersymbol); Esc verwirft die Eingabe – nichts Ungültiges wird gespeichert
        _g.CellValidating += ZellePruefen;
        Steuerelemente.Tabellenbedienung.PruefungSetzen(_g, (zelle, text) => ZelleGueltig(zelle, text));
        _g.CellEndEdit += (_, e) => { if (e.RowIndex >= 0 && e.RowIndex < _g.Rows.Count && e.ColumnIndex >= 0) FehlerAnzeigen(_g.Rows[e.RowIndex].Cells[e.ColumnIndex], null); };
        _g.CellEndEdit += ZelleGeaendert;
        // Zeilenwechsel: Esc nimmt danach nicht mehr die Eingaben der vorherigen Zeile zurück
        _g.CurrentCellChanged += (_, _) =>
        {
            if (_laedt || _zeilenstand is not { } st) return;
            if (_g.CurrentRow is { } r && (r.Tag is Abwesenheit x ? x.Id == st.Id : r.Index == _entwurfZeile)) return;
            _zeilenstand = null;
        };
        _g.CellBeginEdit += (_, e) => { if (e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is Veranstaltung) e.Cancel = true; };
        _g.RowValidated += (_, e) => { if (e.RowIndex == _entwurfZeile) EntwurfUebernehmen(); };
        // Entwurf nicht verlieren: beim Verlassen des Fensters und beim Schließen sofort (nicht verzögert) speichern
        if (AppZustand.Haupt != null)
        {
            void Gewechselt(object? o, EventArgs e) { if (!IsDisposed && AppZustand.Haupt.ActiveMdiChild != this) EntwurfSofortUebernehmen(); }
            AppZustand.Haupt.MdiChildActivate += Gewechselt;
            FormClosed += (_, _) => AppZustand.Haupt.MdiChildActivate -= Gewechselt;
        }
        FormClosing += (_, e) =>
        {
            // eine ungültige, noch markierte Eingabe verhindert das Schließen nicht: sie wird verworfen (gültige Eingaben sind gespeichert)
            if (_g.IsCurrentCellInEditMode && _g.CurrentCell is { ErrorText.Length: > 0 } c)
            {
                try { _g.CancelEdit(); _g.EndEdit(); } catch (InvalidOperationException) { }
                FehlerAnzeigen(c, null);
                e.Cancel = false;
            }
            EntwurfSofortUebernehmen();
        };
        // Doppelklick auf AbsNr. bzw. ein nicht änderbares Feld öffnet die Absenz im Dialog (änderbare Felder werden bearbeitet)
        _g.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || (e.ColumnIndex != 0 && !_g.Columns[e.ColumnIndex].ReadOnly)) return;
            if (_g.Rows[e.RowIndex].Tag is Abwesenheit) Bearbeiten();
            else if (_g.Rows[e.RowIndex].Tag is Veranstaltung) HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster());
        };
        _g.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null) return;
            if (_g.Rows[e.RowIndex].Tag is Veranstaltung)
            {
                e.CellStyle.BackColor = Color.FromArgb(226, 240, 226);
                e.CellStyle.ForeColor = Color.FromArgb(40, 90, 40);
            }
        };
        _g.KeyDown += (_, e) =>
        {
            if (_g.IsCurrentCellInEditMode) return;
            if (e.KeyCode == Keys.Escape && _entwurf != null) { EntwurfVerwerfen(); e.Handled = true; return; }
            // Esc nach bereits gespeicherten Eingaben in dieser Zeile: die Absenz wieder in den Zustand vor der Zeilenbearbeitung bringen
            if (e.KeyData == Keys.Escape && _zeilenstand is { } st && _g.CurrentRow?.Tag is Abwesenheit cur && cur.Id == st.Id) { ZeileZuruecknehmen(); e.Handled = true; return; }
            // Entf auf AbsNr. bzw. Element (oder ganzen Zeilen): Absenz löschen – mit einer Rückfrage samt Folgen
            if (e.KeyData == Keys.Delete && (_g.CurrentCell?.OwningColumn.Name is "Nr" or "E" || Steuerelemente.Tabellenbedienung.GanzeZeilenMarkiert(_g)) && Markierte().Count > 0)
            {
                Loeschen();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && _entwurf != null && e.Modifiers == Keys.Control) { EntwurfSofortUebernehmen(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.Control) { Loeschen(); e.Handled = true; }
            if (e.KeyData == Keys.Insert) { Neu(); e.Handled = true; }
            // wie im Vorbild (Kap. 5.2 Absenzen kopieren): Strg+C merkt die markierten Absenzen, Strg+V fügt sie am eingestellten Tag ein
            // zusätzlich wie in jeder Tabelle: Strg+C kopiert die Zellen als Text; kam danach anderer Text in die Zwischenablage,
            // fügt Strg+V diesen in die Felder ein (Tabellenbedienung)
            if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control) { AbsenzenMerken(); _ablageText = Steuerelemente.Tabellenbedienung.DatensaetzeKopieren(_g); e.Handled = true; }
            if (e.KeyCode == Keys.V && e.Modifiers == Keys.Control && _zwischenablage.Count > 0 && Steuerelemente.Tabellenbedienung.DatensatzEinfuegen(_ablageText)) { AbsenzenEinfuegen(); e.Handled = true; }
        };
        // Elemente aus dem Element-Rollup in die Liste ziehen: je Element eine Absenz am eingestellten Tag (ganzer Tag)
        _g.AllowDrop = true;
        void Ziehen(object? o, DragEventArgs e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string t && ElementRollup.Text2Typ(t, out var zt, out _) != null && ReiterVon(zt) >= 0
            ? DragDropEffects.Copy : DragDropEffects.None;
        _g.DragEnter += Ziehen;
        _g.DragOver += Ziehen;
        _g.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is string t && ElementRollup.Text2Typ(t, out var zt, out var namen) != null) ElementeEintragen(zt, namen);
        };
        _kommentare = Steuerelemente.ZellKommentare.Aktivieren(_g, r => r.Tag is Abwesenheit a ? ("Absenz:" + a.Id, "Absenz Nr. " + a.Id) : null);
        _g.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!_g.IsCurrentCellInEditMode && !_g[e.ColumnIndex, e.RowIndex].Selected) _g.CurrentCell = _g[e.ColumnIndex, e.RowIndex];
            Kontextmenue(e.RowIndex, e.ColumnIndex).Show(_g, _g.PointToClient(Cursor.Position));
        };

        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _zeitraum = new Steuerelemente.ZeitraumAuswahl(_von, _bis, "Absenzen", schloss: true, kalenderAussen: true);
        _zeitraum.Geaendert += () =>
        {
            if (_laedt) return;
            EntwurfSofortUebernehmen();
            Neuaufbau();
            // Datumsauswahl gilt für alle verknüpften Fenster (Vertretungsliste, Stundenpläne, Menüband)
            if (Verknuepft && (Von == Bis || _zeitraum.Art != Steuerelemente.Zeitraumart.VonBis) && AppZustand.Datum != Von) AppZustand.DatumSetzen(Von);
        };
        // Kopf wie im Vorbild: Rahmen „Von-Bis“ (Von, Schloss, Bis – darunter Wochentag und Pfeile), rechts daneben der
        // Kalenderknopf und darunter die Anzahl der Tage („329 Tage“)
        // Beschriftungen und Reiter wie im Original-Bildschirmfoto in Microsoft Sans Serif 8,25 (Reiterleiste 24 Pixel hoch)
        // Vorbild (gemessen): Rahmen „Von-Bis“ 15 Pixel vom Fensterrand, Oberkante 11 Pixel unter der Werkzeugleiste; seine Unterkante
        // liegt in der obersten Zeile der Reiterleiste (26 Pixel hoch bis zum vertieften Tabellenrahmen)
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, Padding = new Padding(14, 5, 0, 0), BackColor = Ui.Kopf, WrapContents = false, Font = Ui.DialogSchrift };
        _tabs.Font = Ui.DialogSchrift;
        _tabs.Height = 26;
        var gruppe = new GroupBox { Text = "Von-Bis", Margin = Padding.Empty };
        _zeitraum.InRahmen(gruppe);
        kopf.Controls.Add(gruppe);
        void KopfHoehe() { var h = gruppe.Bottom - 2; if (h > 0 && kopf.Height != h) kopf.Height = h; }
        gruppe.SizeChanged += (_, _) => KopfHoehe();
        gruppe.LocationChanged += (_, _) => KopfHoehe();
        kopf.Layout += (_, _) => KopfHoehe();
        _tabs.Oberlinie = () => gruppe.Height <= 1 ? null : (gruppe.Left + 2, gruppe.Right - 3);
        var rechts = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(7, 8, 0, 0), Padding = Padding.Empty, BackColor = Color.Transparent };
        _zeitraum.Kalender.Margin = Padding.Empty;
        rechts.Controls.Add(_zeitraum.Kalender);
        _tage.Margin = new Padding(0, 3, 0, 0);
        rechts.Controls.Add(_tage);
        kopf.Controls.Add(rechts);

        // Vorlage: Reiter und Tabelle 9 Pixel vom linken und rechten Fensterrand eingerückt (Fensterfläche ringsum)
        var tabellenFlaeche = new Panel { Dock = DockStyle.Fill, Padding = new Padding(9, 0, 9, 9), BackColor = Ui.Kopf };
        tabellenFlaeche.Controls.Add(_g);
        tabellenFlaeche.Controls.Add(_summe);
        tabellenFlaeche.Controls.Add(_tabs);
        Controls.Add(tabellenFlaeche);
        Controls.Add(kopf);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        AppZustand.VertretungGewaehlt += VertretungGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
            AppZustand.VertretungGewaehlt -= VertretungGewaehlt;
        };
        Neuaufbau();
        // wie im Vorbild: beim Öffnen steht die Eingabe in der leeren Zeile in der Spalte des Elements – sofort lostippen
        Shown += (_, _) => { if (IsHandleCreated) BeginInvoke(EingabezeileWaehlen); };
    }

    private static int ReiterVon(ElementTyp typ) => typ switch { ElementTyp.Lehrer => 0, ElementTyp.Klasse => 1, ElementTyp.Raum => 2, _ => -1 };

    /// <summary>
    /// Mehrere Elemente auf einmal absent setzen (aus dem Element-Rollup gezogen bzw. per Doppelklick eingetragen): je Element eine
    /// Absenz am eingestellten Tag, 1. bis letzte Stunde, fortlaufende Absenznummern. Überschneidungen werden wie immer nachgefragt.
    /// Danach sind die Grund-Felder der neuen Absenzen markiert: ein gewählter Grund gilt für alle.
    /// </summary>
    public bool ElementeEintragen(ElementTyp typ, List<string> namen)
    {
        var reiter = ReiterVon(typ);
        if (reiter < 0)
        {
            Ui.Statushinweis("Absent gesetzt werden können nur Lehrer, Klassen und Räume.");
            return false;
        }
        EntwurfSofortUebernehmen();
        if (_tabs.SelectedIndex != reiter) _tabs.SelectedIndex = reiter;
        var art = Art;
        var elemente = namen.Select(n => D.AlleElemente(typ).FirstOrDefault(x => x.Kurzname.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<Element>().DistinctBy(x => x.Id).ToList();
        var unbekannt = namen.Count(n => !D.AlleElemente(typ).Any(x => x.Kurzname.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (elemente.Count == 0) { Ui.EingabeHinweis(_g, "Die gezogenen Elemente sind nicht vorhanden."); return true; }
        var tag = Von;
        var neu = elemente.Select(el => new Abwesenheit
        {
            Art = art, ElementId = el.Id, Von = tag, Bis = tag, StundeVon = 1, StundeBis = LetzteStunde, Benutzer = AppZustand.Benutzer, Erstellt = DateTime.Now,
        }).ToList();
        var vorher = D.Abwesenheiten.Select(x => x.Id).ToHashSet();
        if (!Speichern(neu, Array.Empty<Abwesenheit>(), neu.Count == 1 ? "Absenz eingeben" : $"{neu.Count} Absenzen eingeben")) return true;
        var ids = D.Abwesenheiten.Where(x => !vorher.Contains(x.Id)).Select(x => x.Id).ToHashSet();
        GrundFelderMarkieren(ids);
        if (ids.Count > 0)
            Ui.Statushinweis($"{ids.Count} {(ids.Count == 1 ? "Absenz" : "Absenzen")} am {tag:dd.MM.} angelegt" + (unbekannt > 0 ? $", {unbekannt} unbekannte Elemente übergangen" : "") +
                             ". Grund bzw. Text eintippen: gilt für alle markierten Absenzen (rückgängig mit Strg+Z).");
        return true;
    }

    /// <summary>Grund-Felder der angegebenen Absenzen markieren (erste wird aktuell) – für die gemeinsame Eingabe.</summary>
    private void GrundFelderMarkieren(IReadOnlySet<int> ids, string spalte = "G")
    {
        var zeilen = _g.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Abwesenheit a && ids.Contains(a.Id)).ToList();
        if (zeilen.Count == 0) return;
        try
        {
            _g.CurrentCell = zeilen[0].Cells[spalte];
            _g.ClearSelection();
            foreach (var r in zeilen) r.Cells[spalte].Selected = true;
            _g.Focus();
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Eine Eingabe in Von, ab St., Bis, bis St., Grund bzw. Text gilt für alle Absenzen, deren Feld in dieser Spalte mitmarkiert ist
    /// (wie im Vorbild: Felder mehrerer Zeilen markieren, einmal eingeben). Jede geänderte Absenz wird geprüft, ungültige bleiben
    /// unverändert (Hinweis); alles zusammen ist eine Rückgängig-Stufe.
    /// </summary>
    private void MehrereSetzen(string spalte, string wert, List<Abwesenheit> ziele)
    {
        Abwesenheitsgrund? grund = null;
        if (spalte == "G" && wert.Length > 0 && (grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))) is null)
        {
            Ui.EingabeHinweis(_g, $"Absenzgrund „{wert}“ ist nicht vorhanden.");
            Neuaufbau();
            return;
        }
        var neu = new List<Abwesenheit>();
        var alt = new List<Abwesenheit>();
        var abgelehnt = new List<string>();
        foreach (var a in ziele)
        {
            var k = a.Kopie();
            string? fehler = null;
            try
            {
                switch (spalte)
                {
                    case "V": VonSetzen(k, Ui.DatumLesen(wert, k.Von), true); break;
                    case "B": k.Bis = Ui.DatumLesen(wert, k.Bis); break;
                    case "SV": if (Absenzpruefung.StundeLesen(wert, LetzteStunde, false, out var sv, out fehler)) k.StundeVon = sv; break;
                    case "SB": if (Absenzpruefung.StundeLesen(wert, LetzteStunde, true, out var sb, out fehler)) k.StundeBis = sb; break;
                    case "G": k.GrundId = grund?.Id; break;
                    case "T": k.Text = wert; break;
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException) { fehler = $"„{wert}“ ist kein gültiges Datum."; }
            fehler ??= Absenzpruefung.Fehler(D, k).FirstOrDefault()?.Text;
            if (fehler != null) { abgelehnt.Add($"Absenz {a.Id}: {fehler}"); continue; }
            if (System.Text.Json.JsonSerializer.Serialize(k) == System.Text.Json.JsonSerializer.Serialize(a)) continue;
            neu.Add(k);
            alt.Add(a);
        }
        if (neu.Count > 0) Speichern(neu, alt, $"{Statuszeilentexte.GetValueOrDefault(spalte, spalte)} für {neu.Count} Absenzen gesetzt");
        else Neuaufbau();
        if (abgelehnt.Count > 0) Ui.EingabeHinweis(_g, $"{abgelehnt.Count} {(abgelehnt.Count == 1 ? "Absenz wurde" : "Absenzen wurden")} nicht geändert. {abgelehnt[0]}");
    }

    /// <summary>Aktuelle Zelle: Element-Spalte der leeren Eingabezeile, Fokus in die Liste.</summary>
    private void EingabezeileWaehlen()
    {
        if (IsDisposed || _g.IsCurrentCellInEditMode || _entwurf != null) return;
        var r = _g.Rows.Cast<DataGridViewRow>().LastOrDefault(x => x.Tag is null && !x.IsNewRow);
        if (r is null) return;
        try
        {
            _g.ClearSelection();
            _g.CurrentCell = r.Cells["E"];
            r.Cells["E"].Selected = true;
            _g.Focus();
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Fensterverknüpfung: Lehrer, Klasse bzw. Raum wurde in einem anderen Fenster gewählt – Reiter wechseln, Absenzen markieren.</summary>
    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        var art = typ switch { ElementTyp.Lehrer => 0, ElementTyp.Klasse => 1, ElementTyp.Raum => 2, _ => -1 };
        if (art < 0) return;
        Markieren(typ, id, art);
    }

    /// <summary>Fensterverknüpfung: Stunde in einem Stundenplan gewählt – Absenzen ihrer Lehrkraft (bzw. Klasse, Raum im jeweiligen Reiter) markieren.</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        int? id = Art switch
        {
            AbwesenheitsArt.Lehrer => (AppZustand.AuswahlLehrer is { Count: > 0 } l ? l : u.LehrerIds).Cast<int?>().FirstOrDefault(),
            AbwesenheitsArt.Klasse => (AppZustand.AuswahlKlassen is { Count: > 0 } k ? k : u.KlassenIds).Cast<int?>().FirstOrDefault(),
            _ => (AppZustand.AuswahlRaeume is { Count: > 0 } r ? r : p?.Raeume.Where(x => x.HasValue).Select(x => x!.Value).ToList() ?? new List<int>()).Cast<int?>().FirstOrDefault(),
        };
        if (id is int i) Markieren(Typ, i, _tabs.SelectedIndex);
    }

    /// <summary>Fensterverknüpfung: gewählte Vertretung – Absenz der vertretenen Lehrkraft (bzw. Klasse) markieren.</summary>
    private void VertretungGewaehlt(Vertretung? v)
    {
        if (v is null || !Verknuepft || IsDisposed) return;
        if (Art == AbwesenheitsArt.Klasse && v.KlassenIds.Count > 0) Markieren(ElementTyp.Klasse, v.KlassenIds[0], 1);
        else if (v.LehrerId is int l) Markieren(ElementTyp.Lehrer, l, 0);
    }

    private void Markieren(ElementTyp typ, int id, int reiter)
    {
        // laufende Eingaben nicht stören
        if (_g.IsCurrentCellInEditMode || _entwurf != null || ContainsFocus) return;
        if (_markiert == (typ, id) && _tabs.SelectedIndex == reiter) return;
        _markiert = (typ, id);
        _zuMarkierungSpringen = true;
        if (_tabs.SelectedIndex != reiter) _tabs.SelectedIndex = reiter; // baut neu auf
        else if (_neuaufbauAusstehend) Neuaufbau();
        else MarkierungAnwenden();
    }

    /// <summary>
    /// Markierung ohne Neuaufbau: nur die Zeilenfarbe der Absenzen des gewählten Elements setzen und zur ersten springen (bei jedem
    /// Zeilenwechsel in der Vertretungsliste bzw. Tageswechsel – ein vollständiger Neuaufbau wäre hier unnötig teuer).
    /// </summary>
    private void MarkierungAnwenden()
    {
        var typ = Typ;
        _laedt = true;
        try
        {
            DataGridViewRow? ziel = null;
            foreach (DataGridViewRow r in _g.Rows)
            {
                if (r.Tag is not Abwesenheit a) continue;
                var an = _markiert is { } m && m.Typ == typ && m.Id == a.ElementId;
                var farbe = an ? Ui.ZelleAktivVertretung : Color.Empty;
                if (r.DefaultCellStyle.BackColor != farbe) r.DefaultCellStyle.BackColor = farbe;
                if (an) ziel ??= r;
            }
            if (_zuMarkierungSpringen && ziel != null && _g.Columns.GetFirstColumn(DataGridViewElementStates.Visible) is { } spalte)
                _g.CurrentCell = ziel.Cells[spalte.Index];
        }
        catch (InvalidOperationException) { }
        finally
        {
            _zuMarkierungSpringen = false;
            _laedt = false;
        }
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || !Verknuepft) return;
        EntwurfSofortUebernehmen();
        if (_zeitraum.Art != Steuerelemente.Zeitraumart.VonBis)
        {
            _laedt = true;
            _von.Value = datum.ToDateTime(TimeOnly.MinValue);
            _laedt = false;
        }
        Neuaufbau();
    }

    private AbwesenheitsArt Art => (AbwesenheitsArt)Math.Max(0, _tabs.SelectedIndex);

    private ElementTyp Typ => TypVon(Art);

    /// <summary>Kurzer Hilfetext je Spalte für die Statuszeile (Spaltenhilfe).</summary>
    internal static readonly Dictionary<string, string> Spaltenhilfen = new()
    {
        ["Nr"] = "Absenznummer zur eindeutigen Kennzeichnung der Absenz",
        ["E"] = "Abwesendes Element: Kurzname eingeben oder aus der Liste wählen",
        ["V"] = "Erster Tag der Absenz (TT.MM.)",
        ["SV"] = "Erste abwesende Stunde am ersten Tag (leer = ab der ersten Stunde)",
        ["B"] = "Letzter Tag der Absenz (TT.MM.)",
        ["SB"] = "Letzte abwesende Stunde am letzten Tag (leer = bis zur letzten Stunde)",
        ["G"] = "Grund der Absenz, wird für die Zählerführung benötigt (z. B. ob Entfall gezählt wird)",
        ["T"] = "Text zur Absenz, erscheint auf Wunsch im Vertretungsplan",
        ["Beg"] = "Begleitlehrkraft, z. B. bei Exkursionen der Klasse",
        ["O"] = "Anzahl der noch offenen Vertretungen dieser Absenz",
        ["Anr"] = "Anrechnung der Absenz auf den Vertretungszähler",
        ["Ent"] = "Entfalltage: Tage, an denen die Absenz Unterricht entfallen lässt",
        ["Abs"] = "Absenztage: Unterrichtstage im Zeitraum der Absenz",
    };

    /// <summary>Kurze Statuszeilentexte je Spalte wie in der Vorlage.</summary>
    internal static readonly Dictionary<string, string> Statuszeilentexte = new()
    {
        ["Nr"] = "Absenznummer",
        ["V"] = "Absent von: TT.MM.",
        ["SV"] = "Absent ab Stunde",
        ["B"] = "Absent bis: TT.MM.",
        ["SB"] = "Absent bis Stunde",
        ["G"] = "Absenzgrund",
        ["T"] = "Text zur Absenz",
        ["Beg"] = "Begleitlehrer",
        ["O"] = "Offene Vertretungen",
        ["Anr"] = "Anrechnung",
        ["Ent"] = "Entfalltage",
        ["Abs"] = "Absenztage",
    };

    private static string? StatuszeilenText(string spalte, ElementTyp typ) => spalte == "E"
        ? typ switch { ElementTyp.Klasse => "Abwesende Klasse", ElementTyp.Raum => "Gesperrter Raum", _ => "Abwesender Lehrer" }
        : Statuszeilentexte.GetValueOrDefault(spalte);

    private static string? SpaltenhilfeText(string spalte, ElementTyp typ) => spalte == "E"
        ? typ switch { ElementTyp.Klasse => "Abwesende Klasse: Kurzname eingeben oder aus der Liste wählen", ElementTyp.Raum => "Gesperrter Raum: Kurzname eingeben oder aus der Liste wählen", _ => "Abwesende Lehrkraft: Kurzname eingeben oder aus der Liste wählen" }
        : Spaltenhilfen.GetValueOrDefault(spalte);

    private static ElementTyp TypVon(AbwesenheitsArt a) => a switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private int LetzteStunde => Math.Max(1, D.Zeitraster.StundenProTag);

    /// <summary>Datum wie im Vorbild „TT.MM.“ (Jahr aus dem Schuljahr).</summary>
    private static string TagText(DateOnly d) => $"{d:dd.MM.}";

    private static string? Schluessel(DataGridViewRow r) => r.Tag switch { Abwesenheit a => "A" + a.Id, Veranstaltung v => "VA" + v.Id, _ => "neu" };

    /// <summary>Platzhalter der leeren Eingabezeile am Ende der Liste.</summary>
    private static readonly object EingabeZeile = new();

    private static string Schluessel(object o) => o switch { Abwesenheit a => "A" + a.Id, Veranstaltung v => "VA" + v.Id, _ => "neu" };

    /// <summary>Zuletzt aufgebauter Reiter (Lehrer/Klasse/Raum): beim Wechsel wird neu aufgebaut, sonst nur abgeglichen.</summary>
    private AbwesenheitsArt? _aufgebautFuer;

    /// <summary>Einträge einer Auswahlspalte nur ersetzen, wenn sie sich geändert haben (sonst bleiben Zellen und Bearbeitung unberührt).</summary>
    private static bool ListeSetzen(DataGridViewComboBoxColumn spalte, IEnumerable<string> werte)
    {
        var neu = new[] { "" }.Concat(werte).ToList();
        if (spalte.Items.Cast<object>().Select(x => x?.ToString() ?? "").SequenceEqual(neu)) return false;
        spalte.Items.Clear();
        foreach (var w in neu) spalte.Items.Add(w);
        return true;
    }

    /// <summary>
    /// Liste aktualisieren, ohne sie ganz neu zu laden: geänderte Absenzen werden an Ort und Stelle erneuert, neue an ihrem Platz
    /// eingefügt, gelöschte entfernt – aktuelle Absenz, Spalte und Bildlauf bleiben, nichts verrutscht. Neu aufgebaut wird nur beim
    /// Reiterwechsel bzw. wenn sich die Auswahllisten (Namen, Gründe) geändert haben.
    /// </summary>
    private void Neuaufbau()
    {
        using var _m = Core.Messung.Start("AbsenzFenster.Neuaufbau");
        using var _ix = DatenIndex.Gemeinsam(D, Ix);
        // während der Eingabe bzw. mit offenem Entwurf nicht neu aufbauen (Eingaben gingen verloren) – später nachholen
        if (_g.IsCurrentCellInEditMode || _entwurf != null)
        {
            _neuaufbauAusstehend = true;
            return;
        }
        _neuaufbauAusstehend = false;
        string? springe = _springeZu is int sp ? "A" + sp : null;
        _springeZu = null;
        _laedt = true;
        var typ = Typ;
        var neuAufbauen = _aufgebautFuer != Art;
        var namen = (DataGridViewComboBoxColumn)_g.Columns["E"]!;
        namen.HeaderText = Art switch { AbwesenheitsArt.Lehrer => "Lehrer", AbwesenheitsArt.Klasse => "Klasse", _ => "Raum" };
        namen.ToolTipText = SpaltenhilfeText("E", typ) ?? "";
        Text = "Absenzen / " + namen.HeaderText;
        var gruende = (DataGridViewComboBoxColumn)_g.Columns["G"]!;
        // geänderte Auswahllisten: Zellen mit Werten außerhalb der Liste vermeiden – dann neu aufbauen
        if (neuAufbauen || ListeSetzen(namen, Stammdaten.Sortiert(D, typ).Select(el => el.Kurzname)) | ListeSetzen(gruende, D.Abwesenheitsgruende.Select(gr => gr.Kurzname)))
        {
            _g.Rows.Clear();
            ListeSetzen(namen, Stammdaten.Sortiert(D, typ).Select(el => el.Kurzname));
            ListeSetzen(gruende, D.Abwesenheitsgruende.Select(gr => gr.Kurzname));
        }
        _aufgebautFuer = Art;
        var (von, bis) = (Von, Bis);
        var anzahl = bis.DayNumber - von.DayNumber + 1;
        _tage.Text = anzahl == 1 ? "1 Tag" : $"{anzahl} Tage";
        _g.Columns["Beg"]!.Visible = Art == AbwesenheitsArt.Klasse;
        var eintraege = new List<object>();
        var vergleich = StringComparer.Create(Format.De, true);
        var absenzen = D.Abwesenheiten.Where(a => a.Art == Art && a.Bis >= von && a.Von <= bis && PasstZumFilter(a, typ));
        eintraege.AddRange(_sortierung switch
        {
            1 => absenzen.OrderBy(a => a.Von).ThenBy(a => a.StundeVon).ThenBy(a => Ix.K(typ, a.ElementId), vergleich),
            2 => absenzen.OrderBy(a => a.Id),
            3 => absenzen.OrderBy(a => D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "\uffff", vergleich).ThenBy(a => Ix.K(typ, a.ElementId), vergleich).ThenBy(a => a.Von),
            _ => absenzen.OrderBy(a => Ix.K(typ, a.ElementId), vergleich).ThenBy(a => a.Von),
        });
        // Veranstaltungen zur Information (Einstellungen „Veranstaltungen anzeigen“)
        if (_vaZeigen.Checked)
            eintraege.AddRange(D.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= bis && VeranstaltungsElemente(v).Length > 0).OrderBy(v => v.Von));
        eintraege.Add(EingabeZeile);
        if (_zuMarkierungSpringen && _markiert is { } mk && eintraege.OfType<Abwesenheit>().FirstOrDefault(ab => ab.ElementId == mk.Id && mk.Typ == typ) is { } ziel)
            springe = "A" + ziel.Id;
        _zuMarkierungSpringen = false;
        try { Steuerelemente.TabellenAbgleich.Anwenden(_g, eintraege, Schluessel, Schluessel, ZeileSetzen, aktuell: springe); }
        finally { _laedt = false; }
        SummeAktualisieren();
        for (var i = 0; i < 3; i++)
        {
            var art = (AbwesenheitsArt)i;
            _tabs.TextSetzen(i, $"{Reiter[i]} ({D.Abwesenheiten.Count(a => a.Art == art && a.Bis >= von && a.Von <= bis)})");
        }
    }

    /// <summary>Elemente einer Veranstaltung im aktuellen Reiter kompakt („Alle Lehrkräfte“, „Alle Klassen außer 5a“).</summary>
    private string VeranstaltungsElemente(Veranstaltung va) => Art switch
    {
        AbwesenheitsArt.Lehrer => VeranstaltungsAuswahl.Kurztext(D, ElementTyp.Lehrer, va.LehrerIds, va.Von, va.Bis),
        AbwesenheitsArt.Klasse => VeranstaltungsLogik.KlassenKurztext(D, va),
        _ => VeranstaltungsAuswahl.Kurztext(D, ElementTyp.Raum, va.RaumIds, va.Von, va.Bis),
    };

    /// <summary>Eine Zeile vollständig füllen (Werte, Tag, Format) – für bestehende und neue Zeilen.</summary>
    private void ZeileSetzen(DataGridViewRow zeile, object eintrag)
    {
        var typ = Typ;
        object?[] werte;
        Color hinter = Color.Empty;
        var nurLesen = false;
        switch (eintrag)
        {
            case Abwesenheit a:
            {
                var offen = D.Vertretungen.Count(v => (v.AbwesenheitId == a.Id && v.OffenOderRaumOffen) || (v.RaumAbwesenheitId == a.Id && v.AbwesenheitId != a.Id && v.RaumOffen));
                var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
                var absenztage = 0;
                for (var t = a.Von; t <= a.Bis && absenztage < 400; t = t.AddDays(1)) if (Kalender.IstUnterrichtstag(D, t)) absenztage++;
                var entfalltage = D.Vertretungen.Where(v => v.AbwesenheitId == a.Id && v.IstEntfall).Select(v => v.Datum).Distinct().Count();
                werte = new object?[] { a.Id, Ix.K(typ, a.ElementId), TagText(a.Von), a.StundeVon, TagText(a.Bis), Math.Min(a.StundeBis, LetzteStunde).ToString(), grund, a.Text,
                    string.Join(", ", a.BegleitLehrerIds.Select(Ix.KLehrer)), offen == 0 ? "" : offen.ToString(),
                    0m.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), entfalltage.ToString(), absenztage.ToString() };
                // Fensterverknüpfung: Absenzen des in einem anderen Fenster gewählten Elements hervorheben
                if (_markiert is { } m && m.Typ == typ && m.Id == a.ElementId) hinter = Ui.ZelleAktivVertretung;
                break;
            }
            case Veranstaltung va:
                werte = new object?[] { $"VA {va.Id}", "", TagText(va.Von), va.StundeVon, TagText(va.Bis), va.StundeBis >= 99 ? LetzteStunde.ToString() : va.StundeBis.ToString(),
                    D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == va.GrundId)?.Kurzname ?? "", $"Veranstaltung: {VeranstaltungsElemente(va)} {va.Text}".Trim(), "", "", "", "", "" };
                nurLesen = true;
                break;
            default:
                // leere Eingabezeile: wie im Original weiß
                werte = new object?[] { "", "", "", "", "", "", "", "", "", "", "", "", "" };
                break;
        }
        zeile.Tag = eintrag == EingabeZeile ? null : eintrag;
        for (var i = 0; i < werte.Length && i < zeile.Cells.Count; i++)
            if (!Equals(zeile.Cells[i].Value?.ToString() ?? "", werte[i]?.ToString() ?? "")) zeile.Cells[i].Value = werte[i];
        if (zeile.ReadOnly != nurLesen) zeile.ReadOnly = nurLesen;
        if (zeile.DefaultCellStyle.BackColor != hinter) zeile.DefaultCellStyle.BackColor = hinter;
        var o = zeile.Cells["O"];
        var rot = eintrag is Abwesenheit && (o.Value?.ToString() ?? "").Length > 0 ? Color.Firebrick : Color.Empty;
        if (o.Style.ForeColor != rot) o.Style.ForeColor = rot;
    }

    private bool PasstZumFilter(Abwesenheit a, ElementTyp typ)
    {
        if (_filter.Length == 0) return true;
        var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
        return new[] { Ix.K(typ, a.ElementId), grund, a.Text }.Any(t => t.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Filter: nur Absenzen, deren Element, Grund oder Text den eingegebenen Text enthält (leer = Filter aus).</summary>
    private void FilterSetzen()
    {
        EntwurfSofortUebernehmen();
        var text = Ui.Eingabe("Nur Absenzen anzeigen, deren Element, Grund oder Text Folgendes enthält (leer = alle):", "Filter", _filter);
        if (text is null) return;
        FilterAnwenden(text);
    }

    /// <summary>Filter setzen (leer = aus) und die Liste neu aufbauen.</summary>
    private void FilterAnwenden(string text)
    {
        EntwurfSofortUebernehmen();
        _filter = text.Trim();
        _filterKnopf.Checked = _filter.Length > 0;
        _filterKnopf.ToolTipText = _filter.Length > 0 ? $"Filter: {_filter}" : "Filter";
        Neuaufbau();
    }

    /// <summary>Seitenlayout: Seitenansicht bzw. Druck der angezeigten Liste.</summary>
    private void Drucken(bool vorschau)
    {
        EntwurfSofortUebernehmen();
        ListenDruck.DruckenTabelle(this, $"{Text}: {Format.Datum(Von)}{(Bis != Von ? " bis " + Format.Datum(Bis) : "")}", _g, vorschau, zeilenFilter: r => r.Tag != null);
    }

    private List<Abwesenheit> Markierte() => _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Abwesenheit>().Distinct().ToList();

    /// <summary>Direkte Eingabe: geänderte Zelle in den Entwurf der Zeile übernehmen (gespeichert wird beim Zeilenwechsel).</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        if (zeile.Tag is Veranstaltung) return;
        var wert = zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var alt = zeile.Tag as Abwesenheit;
        // Felder derselben Spalte in mehreren Zeilen markiert: die Eingabe gilt für alle markierten Absenzen
        var spaltenName = _g.Columns[e.ColumnIndex].Name;
        if (alt != null && _entwurf is null && spaltenName is "V" or "SV" or "B" or "SB" or "G" or "T")
        {
            var weitere = _g.SelectedCells.Cast<DataGridViewCell>()
                .Where(c => c.ColumnIndex == e.ColumnIndex && c.RowIndex != e.RowIndex && c.OwningRow.Tag is Abwesenheit)
                .Select(c => (Abwesenheit)c.OwningRow.Tag!).Distinct().ToList();
            if (weitere.Count > 0)
            {
                var ziele = weitere.Prepend(alt).ToList();
                NachZellwechsel(() => MehrereSetzen(spaltenName, wert, ziele));
                return;
            }
        }
        if (_entwurfZeile != e.RowIndex)
        {
            if (_entwurf != null) EntwurfUebernehmen();
            _entwurfAlt = alt;
            // neue Absenz: Standard ist das Planungsdatum bzw. der angezeigte Tag, 1. bis letzte Stunde
            var start = Von;
            _entwurf = alt is null
                ? new Abwesenheit { Art = Art, Von = start, Bis = start, StundeVon = 1, StundeBis = LetzteStunde, Benutzer = AppZustand.Benutzer }
                : Kopie(alt);
            _entwurfBisGesetzt = alt != null;
            _entwurfZeile = e.RowIndex;
        }
        var a = _entwurf!;
        var name = _g.Columns[e.ColumnIndex].Name;
        string? meldung = null;
        try
        {
            switch (name)
            {
                case "E":
                    var el = D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                    if (el is null && wert.Length > 0) meldung = $"{Ui.TypName(Typ)} \"{wert}\" ist nicht vorhanden.";
                    a.ElementId = el?.Id ?? 0;
                    break;
                case "V":
                    // Bis nur mitziehen, solange es nicht selbst eingegeben wurde (früher ging ein zuerst eingegebenes Bis verloren)
                    VonSetzen(a, Ui.DatumLesen(wert, a.Von), _entwurfBisGesetzt);
                    break;
                case "B":
                    a.Bis = Ui.DatumLesen(wert, a.Bis);
                    _entwurfBisGesetzt = true;
                    break;
                // Stunden außerhalb des Zeitrasters werden nicht still auf 1 bzw. die letzte Stunde gesetzt, sondern abgelehnt
                case "SV":
                    if (Absenzpruefung.StundeLesen(wert, LetzteStunde, false, out var sv, out var fsv)) a.StundeVon = sv; else meldung = fsv;
                    break;
                case "SB":
                    if (Absenzpruefung.StundeLesen(wert, LetzteStunde, true, out var sb, out var fsb)) a.StundeBis = sb; else meldung = fsb;
                    break;
                case "G":
                    var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                    // unbekannter Grund: bisherigen Grund behalten (nicht stillschweigend löschen)
                    if (grund is null && wert.Length > 0) meldung = $"Absenzgrund \"{wert}\" ist nicht vorhanden.";
                    else a.GrundId = grund?.Id;
                    break;
                case "Beg":
                    var teile = wert.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var unbekannt = teile.Where(k => !D.Lehrer.Any(l => l.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (unbekannt.Count > 0) meldung = $"Unbekannte Lehrer: {string.Join(", ", unbekannt)}";
                    a.BegleitLehrerIds = teile.Select(k => D.Lehrer.FirstOrDefault(l => l.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id).OfType<int>().Distinct().ToList();
                    break;
                case "T": a.Text = wert; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            meldung = $"Die Eingabe \"{wert}\" ist ungültig.";
        }
        EntwurfAnzeigen(zeile, a);
        // Meldungen erst nach dem Übernehmen in den Entwurf zeigen – als Hinweis unter der Zelle, ohne Meldungsfenster
        if (meldung != null) Ui.EingabeHinweis(_g, meldung);
        // wie im Vorbild (Kap. 3.2/5.2): Element wählen und mit Tab bzw. Enter bestätigen setzt es sofort absent (ganzer Tag,
        // AbsNr wird vergeben) – jede weitere bestätigte Eingabe (Stunden, Bis, Grund, Text) wird ebenfalls gleich gespeichert
        if (a.ElementId != 0 && _entwurf == a) EntwurfUebernehmen();
    }

    /// <summary>
    /// Von setzen: Bis zieht mit, solange es nicht eigens eingegeben wurde bzw. die Absenz eintägig war und sonst vor Von läge
    /// (eintägige Absenz auf einen anderen Tag verschieben). Bei einer mehrtägigen Absenz bleibt Bis stehen (Prüfung meldet Bis vor Von).
    /// </summary>
    private static void VonSetzen(Abwesenheit a, DateOnly von, bool bisGesetzt)
    {
        var eintaegig = a.Von == a.Bis;
        a.Von = von;
        if (!bisGesetzt || (eintaegig && a.Bis < von)) a.Bis = von;
    }

    /// <summary>Hellrot hinterlegte Zelle mit Fehlersymbol und Tooltip (null = Markierung entfernen).</summary>
    private void FehlerAnzeigen(DataGridViewCell zelle, string? fehler)
    {
        var text = fehler ?? "";
        if (zelle.ErrorText != text) zelle.ErrorText = text;
        var farbe = fehler is null ? Color.Empty : Ui.FehlerHintergrund;
        if (zelle.Style.BackColor != farbe) zelle.Style.BackColor = farbe;
        if (_g.IsCurrentCellInEditMode && _g.CurrentCell == zelle && _g.EditingControl is { } ec)
            ec.BackColor = fehler is null ? (zelle.InheritedStyle.BackColor.IsEmpty ? Color.White : zelle.InheritedStyle.BackColor) : Ui.FehlerHintergrund;
    }

    /// <summary>
    /// Eingabe in Von, ab St., Bis bzw. bis St. vor der Übernahme prüfen: Datum bzw. Stunde lesbar, Bis nicht vor Von, Stunden im
    /// Zeitraster, am selben Tag bis St. nicht vor ab St., innerhalb des Schuljahres. Ungültig: die Eingabe bleibt rot markiert stehen.
    /// </summary>
    private void ZellePruefen(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_laedt || e.Cancel || e.RowIndex < 0 || e.ColumnIndex < 0 || !_g.IsCurrentCellInEditMode || _g.CurrentCell?.RowIndex != e.RowIndex) return;
        if (!ZelleGueltig(_g.Rows[e.RowIndex].Cells[e.ColumnIndex], e.FormattedValue?.ToString() ?? "")) e.Cancel = true;
    }

    /// <summary>Eingabe einer Zelle prüfen (Zellwechsel und Enter); ungültig: rot markieren, Hinweis zeigen, false.</summary>
    private bool ZelleGueltig(DataGridViewCell zelle, string eingabe)
    {
        if (_laedt || zelle.RowIndex < 0 || zelle.OwningRow.Tag is Veranstaltung) return true;
        var spalte = zelle.OwningColumn.Name;
        if (spalte is not ("V" or "B" or "SV" or "SB")) return true;
        var text = eingabe.Trim();
        var fehler = text == (zelle.FormattedValue?.ToString()?.Trim() ?? "") ? null : EingabePruefen(zelle.OwningRow, spalte, text);
        FehlerAnzeigen(zelle, fehler);
        if (fehler is null) return true;
        Ui.EingabeHinweis(_g, fehler + " Bitte korrigieren oder mit Esc verwerfen.");
        return false;
    }

    /// <summary>Fehlertext der Eingabe <paramref name="text"/> im Feld <paramref name="spalte"/> der Zeile (null = gültig).</summary>
    internal string? EingabePruefen(DataGridViewRow zeile, string spalte, string text)
    {
        var eigenerEntwurf = _entwurf != null && _entwurfZeile == zeile.Index;
        var basis = eigenerEntwurf ? _entwurf : zeile.Tag as Abwesenheit;
        var k = basis?.Kopie() ?? new Abwesenheit { Art = Art, Von = Von, Bis = Von, StundeVon = 1, StundeBis = LetzteStunde };
        var bisGesetzt = eigenerEntwurf ? _entwurfBisGesetzt : zeile.Tag is Abwesenheit;
        try
        {
            switch (spalte)
            {
                case "V": VonSetzen(k, Ui.DatumLesen(text, k.Von), bisGesetzt); break;
                case "B": k.Bis = Ui.DatumLesen(text, k.Bis); break;
                case "SV":
                    if (!Absenzpruefung.StundeLesen(text, LetzteStunde, false, out var sv, out var f1)) return f1;
                    k.StundeVon = sv;
                    break;
                case "SB":
                    if (!Absenzpruefung.StundeLesen(text, LetzteStunde, true, out var sb, out var f2)) return f2;
                    k.StundeBis = sb;
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            return $"„{text}“ ist kein gültiges Datum. Bitte TT.MM. eingeben (z. B. {DateTime.Today:dd.MM.}).";
        }
        // nur Fehler, die diese Eingabe verursacht (ältere, schon gespeicherte Abweichungen blockieren die Bearbeitung nicht)
        var vorher = basis is null ? new HashSet<string>() : Absenzpruefung.Fehler(D, basis, false).Select(b => b.Text).ToHashSet();
        return Absenzpruefung.Fehler(D, k, false).FirstOrDefault(b => !vorher.Contains(b.Text))?.Text;
    }

    /// <summary>Entwurf in der Zeile anzeigen (normalisierte Werte, Kennzeichnung „nicht gespeichert“).</summary>
    private void EntwurfAnzeigen(DataGridViewRow zeile, Abwesenheit a)
    {
        _laedt = true;
        zeile.Cells["E"].Value = a.ElementId != 0 ? Ix.K(Typ, a.ElementId) : "";
        zeile.Cells["V"].Value = TagText(a.Von);
        zeile.Cells["B"].Value = TagText(a.Bis);
        zeile.Cells["SV"].Value = a.StundeVon.ToString();
        zeile.Cells["SB"].Value = Math.Min(a.StundeBis, LetzteStunde).ToString();
        zeile.Cells["G"].Value = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
        zeile.Cells["Nr"].Value = zeile.Tag is Abwesenheit ab ? $"{ab.Id} *" : "neu *";
        zeile.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 214);
        _laedt = false;
    }

    /// <summary>Zeile verlassen: Entwurf speichern (neue Absenz nur mit gewähltem Element). Verzögert, damit die Tabelle ihren Zeilenwechsel beendet.</summary>
    private void EntwurfUebernehmen() => EntwurfAbschliessen(false);

    /// <summary>Entwurf sofort speichern (Reiter-/Datumswechsel, Fenster verlassen oder schließen).</summary>
    private void EntwurfSofortUebernehmen()
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        EntwurfAbschliessen(true);
    }

    private void EntwurfAbschliessen(bool sofort)
    {
        if (_entwurf is null)
        {
            if (_neuaufbauAusstehend && !_g.IsCurrentCellInEditMode) NachZellwechsel(Neuaufbau);
            return;
        }
        var a = _entwurf;
        var alt = _entwurfAlt;
        _entwurfZeileVorher = _entwurfZeile;
        _entwurf = null;
        _entwurfAlt = null;
        _entwurfZeile = -1;
        if (a.ElementId == 0)
        {
            // ohne Element keine Absenz – Eingaben verwerfen
            if (alt != null) Ui.Info("Ohne Element kann die Absenz nicht gespeichert werden. Die Änderung wurde verworfen.");
            if (sofort || !IsHandleCreated) Neuaufbau(); else NachZellwechsel(Neuaufbau);
            return;
        }
        // nichts geändert (z. B. ungültige Eingabe verworfen): nicht speichern, Daten bleiben unverändert
        if (alt != null && System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(alt))
        {
            if (sofort || !IsHandleCreated) Neuaufbau(); else NachZellwechsel(Neuaufbau);
            return;
        }
        var text = alt is null ? "Absenz eingeben" : "Absenz geändert";
        var zeile = _entwurfZeileVorher;
        void Ausfuehren()
        {
            // Stand vor der Zeilenbearbeitung merken (Esc nimmt alle in dieser Zeile gespeicherten Eingaben zurück)
            var weiter = _zeilenstand is { } bisher && alt != null && alt.Id == bisher.Id ? bisher : null;
            if (Speichern(new[] { a }, alt is null ? Array.Empty<Abwesenheit>() : new[] { alt }, text) && _letzteGespeicherte is { } neuGespeichert)
            {
                var stand = weiter ?? new Zeilenstand();
                foreach (var ersetzt in _letzteErsetzte)
                    if (weiter is null || ersetzt.Id != weiter.Id) stand.Vorher.Add(ersetzt.Kopie());
                stand.Id = neuGespeichert.Id;
                _zeilenstand = stand;
            }
            else _zeilenstand = null;
            // Tabelle ist schon in der nächsten Zelle im Bearbeitungsmodus (Neuaufbau wird nachgeholt): die Eingabezeile gehört
            // jetzt zur gespeicherten Absenz – weitere Eingaben in dieser Zeile ändern sie, statt eine zweite anzulegen
            if (_neuaufbauAusstehend && _letzteGespeicherte is { } g && zeile >= 0 && zeile < _g.Rows.Count && (_g.Rows[zeile].Tag is null || _g.Rows[zeile].Tag is Abwesenheit x && x.Id == g.Id) && !_g.Rows[zeile].IsNewRow)
            {
                _g.Rows[zeile].Tag = g;
                _laedt = true;
                _g.Rows[zeile].Cells["Nr"].Value = g.Id;
                _laedt = false;
            }
        }
        if (sofort || !IsHandleCreated || IsDisposed) Ausfuehren();
        else NachZellwechsel(Ausfuehren);
    }

    /// <summary>Stand einer Zeile vor ihrer Bearbeitung: aktuelle Absenznummer und die ersetzten ursprünglichen Absenzen.</summary>
    private sealed class Zeilenstand
    {
        public int Id;
        public List<Abwesenheit> Vorher = new();
    }

    /// <summary>Zeile, deren Eingaben seit dem Betreten gespeichert wurden (Esc nimmt sie zurück), sonst null.</summary>
    private Zeilenstand? _zeilenstand;

    /// <summary>
    /// Esc in einer Zeile, deren Eingaben schon gespeichert sind: neue Absenz wieder entfernen bzw. geänderte (und dabei
    /// zusammengefasste) Absenzen wiederherstellen – mit derselben Rückfrage wie beim Verkürzen, rückgängig mit Strg+Z.
    /// </summary>
    private void ZeileZuruecknehmen()
    {
        if (_zeilenstand is not { } st) return;
        _zeilenstand = null;
        var aktuell = D.Abwesenheiten.FirstOrDefault(x => x.Id == st.Id);
        if (aktuell is null || st.Vorher.Any(v => v.Id != st.Id && D.Abwesenheiten.Any(x => x.Id == v.Id)))
        {
            Ui.Statushinweis("Die Eingaben dieser Zeile können nicht mehr zurückgenommen werden. Rückgängig mit Strg+Z.");
            return;
        }
        var neu = st.Vorher.Select(v => v.Kopie()).ToList();
        var text = neu.Count == 0 ? "Absenzeingabe verworfen" : "Absenzänderung zurückgenommen";
        if (Speichern(neu, new[] { aktuell }, text, wiederherstellen: true))
            Ui.Statushinweis(neu.Count == 0
                ? $"Neue Absenz {Ix.K(TypVon(aktuell.Art), aktuell.ElementId)} {Absenzpruefung.Zeitraum(aktuell, LetzteStunde)} verworfen (rückgängig mit Strg+Z)."
                : "Änderungen der Zeile zurückgenommen (rückgängig mit Strg+Z).");
    }

    private void EntwurfVerwerfen()
    {
        if (_entwurf is null) return;
        _entwurf = null;
        _entwurfAlt = null;
        _entwurfZeile = -1;
        Neuaufbau();
    }

    private static Abwesenheit Kopie(Abwesenheit a) => a.Kopie();

    private void Neu()
    {
        EntwurfSofortUebernehmen();
        var a = new Abwesenheit { Art = Art, Von = Von, Bis = Von };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, Array.Empty<Abwesenheit>(), "Absenz eingeben");
    }

    private void Bearbeiten()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var a = Kopie(alt);
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, new[] { alt }, "Absenz geändert");
    }

    private void Teilen()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        if (alt.Von == alt.Bis) { Ui.Info("Eine eintägige Absenz kann nicht gesplittet werden."); return; }
        var text = Ui.Eingabe($"Absenz {Format.Datum(alt.Von)} bis {Format.Datum(alt.Bis)} teilen: zweiter Teil beginnt am (TT.MM.JJJJ):", "Absenz splitten", Format.Datum(alt.Von.AddDays(1)));
        if (text is null || !DateOnly.TryParse(text, Format.De, out var ab) || ab <= alt.Von || ab > alt.Bis) return;
        var a = Kopie(alt);
        a.Bis = ab.AddDays(-1);
        a.StundeBis = 99;
        var b = Kopie(alt);
        b.Id = 0;
        b.Von = ab;
        b.StundeVon = 1;
        Speichern(new[] { a, b }, new[] { alt }, "Absenz splitten");
    }

    private void Kopieren()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var w = AuswahlDialog.Mehrere(this, TypVon(alt.Art), "Absenz für weitere Elemente übernehmen", Array.Empty<int>());
        if (w is null || w.Count == 0) return;
        var neu = w.Where(e => e.Id != alt.ElementId).Select(e => { var a = Kopie(alt); a.Id = 0; a.ElementId = e.Id; a.Erstellt = DateTime.Now; return a; }).ToArray();
        Speichern(neu, Array.Empty<Abwesenheit>(), "Absenz kopieren");
    }

    private void Zusammenfassen()
    {
        EntwurfSofortUebernehmen();
        var m = Markierte().OrderBy(a => a.Von).ToList();
        if (m.Count < 2 || m.Select(a => (a.Art, a.ElementId)).Distinct().Count() > 1) { Ui.Info("Bitte mindestens zwei Absenzen desselben Elements markieren."); return; }
        var a = Kopie(m[0]);
        a.Bis = m.Max(x => x.Bis);
        a.StundeBis = m.First(x => x.Bis == a.Bis).StundeBis;
        Speichern(new[] { a }, m.ToArray(), "Absenzen zusammenfassen");
    }

    /// <summary>
    /// Kontextmenü wie im Handbuch-Bild (Kap. 4.5): Sortieren ▸ | Felder der Ansicht, Summenzeile, Spaltenbreite anpassen, Alle Spalten
    /// anpassen (Strg+E), Alle Spalten an Inhalt anpassen (Strg+F), Aktuellen Feldinhalt als Filter setzen, Zeilenumbruch | Kommentar
    /// erstellen, Kommentar bearbeiten ▸, Kommentar(e) löschen, Kommentar Übersichtsfenster. Neu, Bearbeiten, Löschen, Splitten, Kopieren
    /// und Zusammenfassen stehen in der Symbolleiste.
    /// </summary>
    private ContextMenuStrip Kontextmenue(int zeile, int spalte)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Closed += (_, _) => BeginInvoke(cm.Dispose);
        // die wichtigsten Befehle zur Absenz zuerst (auch über Tasten erreichbar)
        var absenz = zeile >= 0 && zeile < _g.Rows.Count ? _g.Rows[zeile].Tag as Abwesenheit : null;
        cm.Items.Add(new ToolStripMenuItem("Neue Absenz…", Symbole.Get("neu", 16), (_, _) => Neu()) { ShortcutKeyDisplayString = "Einfg" });
        cm.Items.Add(new ToolStripMenuItem("Absenz bearbeiten…", null, (_, _) => Bearbeiten()) { Enabled = absenz != null });
        cm.Items.Add(new ToolStripMenuItem("Absenz löschen…", Symbole.Get("l_loeschen", 16), (_, _) => Loeschen()) { Enabled = Markierte().Count > 0, ShortcutKeyDisplayString = "Entf" });
        cm.Items.Add(new ToolStripMenuItem("Schuljahreskalender der Absenzen", null, (_, _) => { if (absenz != null) AbsenzKalenderDialog.Zeigen(this, Typ, absenz.ElementId); }) { Enabled = absenz != null });
        var doc = AppZustand.Doc;
        cm.Items.Add(new ToolStripMenuItem(doc.KannRueckgaengig ? $"Rückgängig: {doc.RueckgaengigText}" : "Rückgängig", null, (_, _) => Ui.Rueckgaengig()) { Enabled = doc.KannRueckgaengig, ShortcutKeyDisplayString = "Strg+Z" });
        cm.Items.Add(new ToolStripSeparator());
        var sortieren = new ToolStripMenuItem("Sortieren");
        foreach (var (text, art) in new[] { ("nach Element und Datum", 0), ("nach Datum", 1), ("nach Absenznummer", 2), ("nach Absenzgrund", 3) })
            sortieren.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) => SortierungSetzen(art)) { Checked = _sortierung == art });
        cm.Items.Add(sortieren);
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Felder der Ansicht", null, (_, _) => Steuerelemente.TabellenFormate.MenueZeigen(_g, Cursor.Position));
        cm.Items.Add(new ToolStripMenuItem("Summenzeile", null, (_, _) => { _summe.Visible = !_summe.Visible; SummeAktualisieren(); }) { Checked = _summe.Visible });
        var col = _g.Columns[spalte];
        cm.Items.Add("Spaltenbreite anpassen", null, (_, _) => col.Width = Math.Max(col.MinimumWidth, col.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true)));
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten anpassen", null, (_, _) => SpaltenAnpassen(DataGridViewAutoSizeColumnMode.AllCells)) { ShortcutKeyDisplayString = "Strg+E" });
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten an Inhalt anpassen", null, (_, _) => SpaltenAnpassen(DataGridViewAutoSizeColumnMode.AllCellsExceptHeader)) { ShortcutKeyDisplayString = "Strg+F" });
        var feldinhalt = zeile >= 0 && zeile < _g.Rows.Count ? _g[spalte, zeile].FormattedValue?.ToString()?.Trim() ?? "" : "";
        cm.Items.Add(new ToolStripMenuItem("Aktuellen Feldinhalt als Filter setzen", null, (_, _) => FilterAnwenden(feldinhalt)) { Enabled = feldinhalt.Length > 0 || _filter.Length > 0 });
        var umbruch = new ToolStripMenuItem("Zeilenumbruch") { Checked = _g.DefaultCellStyle.WrapMode == DataGridViewTriState.True };
        umbruch.Click += (_, _) =>
        {
            _g.DefaultCellStyle.WrapMode = umbruch.Checked ? DataGridViewTriState.False : DataGridViewTriState.True;
            _g.AutoSizeRowsMode = umbruch.Checked ? DataGridViewAutoSizeRowsMode.None : DataGridViewAutoSizeRowsMode.DisplayedCells;
        };
        cm.Items.Add(umbruch);
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.AddRange(_kommentare.MenueEintraege(zeile, spalte).ToArray());
        return cm;
    }

    /// <summary>Sortierung wählen (Symbolleiste und Kontextmenü).</summary>
    private void SortierungSetzen(int art)
    {
        _sortierung = art;
        foreach (var x in _sortierMenue.DropDownItems.OfType<ToolStripMenuItem>()) x.Checked = x.Tag is int t && t == art;
        EntwurfSofortUebernehmen();
        Neuaufbau();
    }

    /// <summary>Summenzeile: Anzahl der angezeigten Absenzen sowie Summen der Absenz- und Entfalltage.</summary>
    private void SummeAktualisieren()
    {
        if (!_summe.Visible) return;
        var zeilen = _g.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Abwesenheit).ToList();
        int Summe(string spalte) => zeilen.Sum(r => int.TryParse(r.Cells[spalte].Value?.ToString(), out var n) ? n : 0);
        _summe.Text = $"Summe: {zeilen.Count} {(zeilen.Count == 1 ? "Absenz" : "Absenzen")}   Absenztage: {Summe("Abs")}   Entfalltage: {Summe("Ent")}";
    }

    private void SpaltenAnpassen(DataGridViewAutoSizeColumnMode modus)
    {
        foreach (var c in _g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill))
            c.Width = Math.Max(Math.Max(c.MinimumWidth, 24), c.GetPreferredWidth(modus, true));
    }

    private void AbsenzenMerken()
    {
        EntwurfSofortUebernehmen();
        var m = Markierte();
        if (m.Count > 0) _zwischenablage = m.Select(Kopie).ToList();
    }

    /// <summary>Gemerkte Absenzen am eingestellten Tag einfügen (Dauer und Stunden bleiben erhalten).</summary>
    private void AbsenzenEinfuegen()
    {
        EntwurfSofortUebernehmen();
        if (_zwischenablage.Count == 0) return;
        var start = _zwischenablage.Min(a => a.Von);
        var versatz = Von.DayNumber - start.DayNumber;
        var neu = _zwischenablage.Select(a =>
        {
            var k = Kopie(a);
            k.Id = 0;
            k.Von = a.Von.AddDays(versatz);
            k.Bis = a.Bis.AddDays(versatz);
            k.Erstellt = DateTime.Now;
            k.Benutzer = AppZustand.Benutzer;
            return k;
        }).ToArray();
        Speichern(neu, Array.Empty<Abwesenheit>(), "Absenz einfügen");
    }

    /// <summary>
    /// Wie im Vorbild (Kap. 5.2 Zusammenfassen von Absenzen): schließt eine einzelne neue bzw. geänderte Absenz zeitlich direkt an eine
    /// andere Absenz desselben Elements mit demselben Absenzgrund an, wird gefragt, ob beide zusammengefasst werden sollen.
    /// </summary>
    private Abwesenheit? Anschluss(Abwesenheit a, IReadOnlyList<Abwesenheit> alt)
    {
        var letzte = LetzteStunde;
        bool Folgt(Abwesenheit frueher, Abwesenheit spaeter) =>
            frueher.StundeBis >= letzte && spaeter.StundeVon <= 1 && frueher.Bis < spaeter.Von &&
            (frueher.Bis.AddDays(1) == spaeter.Von || Kalender.NaechsterUnterrichtstag(D, frueher.Bis) == spaeter.Von);
        return D.Abwesenheiten.FirstOrDefault(b => b.Id != a.Id && !alt.Any(y => y.Id == b.Id) && b.Art == a.Art && b.ElementId == a.ElementId && b.GrundId == a.GrundId
            && (Folgt(b, a) || Folgt(a, b)));
    }

    /// <summary>Absenzen, die das letzte erfolgreiche <see cref="Speichern"/> ersetzt hat (auch zusammengefasste).</summary>
    private IReadOnlyList<Abwesenheit> _letzteErsetzte = Array.Empty<Abwesenheit>();

    private string GrundKurz(Abwesenheit a) => D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname is { Length: > 0 } g ? $" ({g})" : "";

    /// <summary>
    /// Überschneidet sich eine neue bzw. geänderte Absenz mit vorhandenen Absenzen desselben Elements, wird nachgefragt: zu einer
    /// Absenz zusammenfassen (Standard), beide behalten oder abbrechen. Liegt eine neue Absenz ganz in einer vorhandenen, wird keine
    /// zweite angelegt (die vorhandene wird gewählt). false = nichts speichern.
    /// </summary>
    private bool UeberschneidungenKlaeren(ref IReadOnlyList<Abwesenheit> neu, ref IReadOnlyList<Abwesenheit> alt, ref string text)
    {
        if (neu.Count == 0) return true;
        var letzte = LetzteStunde;
        var ersetzt = alt.Select(x => x.Id).Concat(neu.Where(x => x.Id != 0).Select(x => x.Id)).ToList();
        var neuListe = neu;
        string Zeile(Abwesenheit b) => $"Absenz {b.Id}: {Absenzpruefung.Zeitraum(b, letzte)}{GrundKurz(b)}";
        if (neu.Count == 1)
        {
            var na = neu[0];
            var ueber = Absenzpruefung.Ueberschneidungen(D, na, ersetzt);
            if (ueber.Count == 0) return true;
            var name = Ix.K(TypVon(na.Art), na.ElementId);
            var liste = string.Join("\n", ueber.Take(8).Select(Zeile)) + (ueber.Count > 8 ? $"\n… und {ueber.Count - 8} weitere" : "");
            var enthalten = na.Id == 0 && ueber.Count == 1 && Absenzpruefung.Enthalten(na, ueber[0]);
            var wahl = enthalten
                ? Ui.Auswahl($"{name} ist am {Absenzpruefung.Zeitraum(na, letzte)} bereits abwesend:\n\n{liste}\n\nEine zweite Absenz für diesen Zeitraum ist nicht nötig.",
                    "UntPlan - Absenz vorhanden", new[] { "Vorhandene verwenden", "Trotzdem anlegen", "Abbrechen" }, 0, owner: this)
                : Ui.Auswahl($"Die Absenz von {name} ({Absenzpruefung.Zeitraum(na, letzte)}) überschneidet sich mit\n\n{liste}\n\nSollen die Absenzen zu einer Absenz zusammengefasst werden?",
                    "UntPlan - Überschneidung", new[] { "Zusammenfassen", "Beide behalten", "Abbrechen" }, 0, owner: this);
            if (wahl is null or 2)
            {
                Ui.Statushinweis("Absenz nicht gespeichert.");
                return false;
            }
            if (wahl == 1) return true;
            if (enthalten)
            {
                _springeZu = ueber[0].Id;
                Ui.Statushinweis($"Keine zweite Absenz angelegt: {name} ist bereits durch Absenz {ueber[0].Id} abwesend.");
                return false;
            }
            var basis = na.Id != 0 ? na : ueber[0];
            var z = Absenzpruefung.Zusammenfassen(basis, ueber.Where(b => b != basis).Append(na).Where(b => b != basis));
            neu = new[] { z };
            var bisherAlt = alt;
            alt = bisherAlt.Concat(ueber.Where(b => bisherAlt.All(x => x.Id != b.Id))).ToList();
            text = "Absenzen zusammenfassen";
            return true;
        }
        var betroffen = neuListe.Where(a => Absenzpruefung.Ueberschneidungen(D, a, ersetzt).Count > 0).ToList();
        if (betroffen.Count == 0) return true;
        var aufzaehlung = string.Join("\n", betroffen.Take(10).Select(a => $"{Ix.K(TypVon(a.Art), a.ElementId)} {Absenzpruefung.Zeitraum(a, letzte)}"))
                          + (betroffen.Count > 10 ? $"\n… und {betroffen.Count - 10} weitere" : "");
        var w = Ui.Auswahl($"{betroffen.Count} von {neu.Count} Absenzen überschneiden sich mit vorhandenen Absenzen derselben Elemente:\n\n{aufzaehlung}",
            "UntPlan - Überschneidung", new[] { "Nur die übrigen anlegen", "Alle anlegen", "Abbrechen" }, 0, owner: this);
        if (w is null or 2) { Ui.Statushinweis("Absenzen nicht gespeichert."); return false; }
        if (w == 0) neu = neuListe.Where(a => !betroffen.Contains(a)).ToList();
        return neu.Count > 0 || alt.Count > 0;
    }

    /// <param name="bestaetigt">Rückfrage zu rückgängig gemachten Maßnahmen schon gestellt (Löschen fragt mit der Liste).</param>
    /// <param name="wiederherstellen">Früheren Stand wiederherstellen (Esc): ohne Prüfung, Überschneidungs- und Zusammenfassungsfrage.</param>
    /// <returns>true, wenn gespeichert wurde.</returns>
    private bool Speichern(IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt, string text, bool bestaetigt = false, bool wiederherstellen = false)
    {
        var letzte = LetzteStunde;
        if (!wiederherstellen)
        {
            // nie Ungültiges speichern (unbekanntes Element, Bis vor Von, Stunden außerhalb, außerhalb des Schuljahres)
            var fehler = neu.SelectMany(a => Absenzpruefung.Fehler(D, a).Select(b => (A: a, B: b))).ToList();
            if (fehler.Count > 0)
            {
                Ui.Warnung((neu.Count == 1 ? "Die Absenz wurde nicht gespeichert:" : "Die Absenzen wurden nicht gespeichert:") + "\n\n" +
                    string.Join("\n", fehler.Take(10).Select(x => $"{Ix.K(TypVon(x.A.Art), x.A.ElementId)} {Absenzpruefung.Zeitraum(x.A, letzte)}: {x.B.Text}".Trim())), "UntPlan - Hinweis", this);
                Neuaufbau();
                return false;
            }
            if (!bestaetigt && !UeberschneidungenKlaeren(ref neu, ref alt, ref text))
            {
                Neuaufbau();
                return false;
            }
        }
        if (!wiederherstellen && neu.Count == 1 && Anschluss(neu[0], alt) is { } andere)
        {
            var na = neu[0];
            var typ = TypVon(na.Art);
            var name = Ix.K(typ, na.ElementId);
            var lang = D.AlleElemente(typ).FirstOrDefault(x => x.Id == na.ElementId) switch { Lehrer l => l.VollerName, { } el => el.Langname, _ => "" };
            string Zeile(Abwesenheit x, string nr) => $"Absenz {nr}:  {x.Von:d.M.} / {Planungsdienst.TageKurz[((int)x.Von.DayOfWeek + 6) % 7]}";
            if (Ui.Frage($"{name} - {lang}\n{Zeile(na, na.Id == 0 ? "neu" : na.Id.ToString())}\nkann zusammengefasst werden mit\n{Zeile(andere, andere.Id.ToString())}\n\nSollen diese Absenzen zusammengefasst werden?", "UntPlan - Frage", this))
            {
                var frueher = na.Von <= andere.Von ? na : andere;
                var spaeter = frueher == na ? andere : na;
                var z = Kopie(andere);
                z.Von = frueher.Von;
                z.StundeVon = frueher.StundeVon;
                z.Bis = spaeter.Bis;
                z.StundeBis = spaeter.StundeBis;
                if (z.Text.Length == 0) z.Text = na.Text;
                neu = new[] { z };
                alt = alt.Append(andere).ToList();
                text = "Absenzen zusammenfassen";
            }
        }
        // Lehrkraft früher wieder anwesend (Absenz verkürzt bzw. gelöscht): Vertretungen, Entfälle, Verlegungen und Vorziehungen der frei
        // gewordenen Zeit werden rückgängig gemacht – vorher mit Liste nachfragen; Nein lässt die Absenz unverändert
        if (!bestaetigt && alt.Count > 0 && Absenzverkuerzung.Ermitteln(D, neu, alt) is { } rueck && rueck.Wesentlich.Any()
            && !Ui.Frage(rueck.HinweisText(D, vorschau: true) + "\n\nSoll die Absenz trotzdem geändert werden?", "UntPlan - Frage", this))
        {
            Neuaufbau();
            return false;
        }
        // Hinweis: eingeplante Vertreter, die durch die neue Absenz abwesend werden
        var betroffen = neu.SelectMany(a => Vertretungen.BetroffeneVertreter(D, a)).Distinct().ToList();
        List<Abwesenheit> gespeichert = new();
        _letzteGespeicherte = null;
        Absenzverkuerzung.Ergebnis? rueckgaengig = null;
        var ersetzteAbsenzen = alt.Select(x => x.Kopie()).ToList();
        if (!Aendern(text, AenderungsArt.Vertretung, d => (gespeichert, rueckgaengig) = Absenzverkuerzung.Speichern(d, neu, alt))) return false;
        _letzteErsetzte = ersetzteAbsenzen;
        if (gespeichert.Count == 1) _springeZu = gespeichert[0].Id;
        _letzteGespeicherte = gespeichert.Count == 1 ? gespeichert[0] : null;
        // Rückmeldung in der Statuszeile: was gespeichert wurde, wie viele Vertretungen offen sind, Strg+Z
        if (gespeichert.Count == 1)
        {
            var g = gespeichert[0];
            var offen = D.Vertretungen.Count(v => (v.AbwesenheitId == g.Id && v.OffenOderRaumOffen) || (v.RaumAbwesenheitId == g.Id && v.AbwesenheitId != g.Id && v.RaumOffen));
            Ui.Statushinweis($"{text}: Absenz {g.Id} {Ix.K(TypVon(g.Art), g.ElementId)} {Absenzpruefung.Zeitraum(g, letzte)}, " +
                             (offen == 1 ? "1 offene Vertretung" : $"{offen} offene Vertretungen") + " (rückgängig mit Strg+Z).");
        }
        else if (gespeichert.Count > 1 || alt.Count > 0) Ui.Statushinweis($"{text}: {gespeichert.Count} gespeichert, {alt.Count} ersetzt bzw. gelöscht (rückgängig mit Strg+Z).");
        if (rueckgaengig is { } rg && rg.Wesentlich.Any()) Ui.Statushinweis(rg.Zusammenfassung(D, vorschau: false));
        // Hinweis (kein Fehler): Absenz ohne Unterrichtstag bzw. Beginn an einem freien Tag
        if (!wiederherstellen && gespeichert.SelectMany(a => Absenzpruefung.Pruefen(D, a)).FirstOrDefault(b => b.Art == Befundart.Hinweis) is { } hinweis)
            Ui.EingabeHinweis(_g, hinweis.Text);
        if (wiederherstellen)
        {
            Neuaufbau();
            return true;
        }
        if (neu.Count > 0 && gespeichert.Count == 0 && alt.Count == 0) Ui.Info("Diese Absenz ist bereits vorhanden.");
        if (betroffen.Count > 0)
            Ui.Info("Achtung: Folgende Vertretungen waren bereits mit einer Lehrkraft geplant, die nun abwesend ist. " +
                "Sie sind wieder offen und müssen neu vertreten werden:\n\n" +
                string.Join("\n", betroffen.Take(20).Select(v => $"Vtr-Nr. {v.Id}: {Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7]} {Format.Datum(v.Datum)}, {v.Stunde}. Std., {Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)}, Vertretung: {Ix.KLehrer(v.VertreterId)}")) +
                (betroffen.Count > 20 ? $"\n… und {betroffen.Count - 20} weitere" : ""), "Vertreter abwesend");
        VeranstaltungenPruefen(this, gespeichert, (t, a) => Aendern(t, AenderungsArt.Vertretung, a));
        // Lehrkraft wird abwesend: Verlegungen und Sondereinsätze im Absenzzeitraum rückgängig machen bzw. löschen?
        VerlegungsAbfrage.NachAbsenz(gespeichert, Aendern);
        Neuaufbau();
        return true;
    }

    /// <summary>
    /// Lehrer-Absenzen, die mit einer Veranstaltung überlappen: wie im Vorbild nachfragen, ob die Lehrkraft aus der Veranstaltung
    /// entfernt werden soll (nur für die Tage der Absenz). Bei Ja bleibt ihr Platz als „?“ offen.
    /// </summary>
    internal static void VeranstaltungenPruefen(IWin32Window owner, IEnumerable<Abwesenheit> absenzen, Func<string, Action<Schuldatei>, bool> aendern)
    {
        var d = AppZustand.D;
        foreach (var a in absenzen.Where(a => a.Art == AbwesenheitsArt.Lehrer).ToList())
            foreach (var u in VeranstaltungsLogik.Ueberlappungen(d, a))
            {
                if (!Ui.Frage(VeranstaltungsLogik.FrageText(d, a, u), "UntPlan - Frage", owner)) continue;
                var vaId = u.Veranstaltung.Id;
                var tage = u.Tage.ToList();
                var absId = a.Id;
                if (aendern("Lehrer aus Veranstaltung entfernt", x => VeranstaltungsLogik.LehrerEntfernen(x, vaId, u.LehrerId, tage, absId)))
                    Ui.Warnung(VeranstaltungsLogik.HinweisText(AppZustand.D, u), "UntPlan - Hinweis", owner);
            }
    }

    private void Loeschen()
    {
        EntwurfSofortUebernehmen();
        var liste = Markierte();
        // Maßnahmen der Absenz (Vertretungen, Entfälle, Verlegungen) werden rückgängig gemacht: in der Rückfrage auflisten
        var rueck = liste.Count > 0 ? Absenzverkuerzung.Ermitteln(D, Array.Empty<Abwesenheit>(), liste) : null;
        var zusatz = rueck is { } r && r.Wesentlich.Any() ? r.HinweisText(D, vorschau: true) : "Daraus entstandene Vertretungen werden entfernt.";
        if (liste.Count == 0) { Ui.Statushinweis("Bitte zuerst die zu löschende Absenz markieren."); return; }
        var name = liste.Count == 1 ? $"{Ix.K(TypVon(liste[0].Art), liste[0].ElementId)} {Absenzpruefung.Zeitraum(liste[0], LetzteStunde)}" : null;
        if (!Ui.LoeschenBestaetigen(liste.Count, "Absenz", "Absenzen", name: name, zusatz: zusatz, owner: this)) return;
        _zeilenstand = null;
        Speichern(Array.Empty<Abwesenheit>(), liste, "Absenz löschen", bestaetigt: true);
    }

    // ----------------------------------------------------------------------------------------------
    // Selbsttest (Handbuch-Abläufe, Kurzeinführung Kap. 3.2)
    // ----------------------------------------------------------------------------------------------

    /// <summary>Selbsttest: Reiter Lehrer (0), Klassen (1), Räume (2) wählen.</summary>
    internal void SelbsttestReiter(int i) => _tabs.SelectedIndex = i;

    /// <summary>Selbsttest: Kopf „Von-Bis“ (Zeitraumart, Schloss) und die angezeigte Anzahl der Tage.</summary>
    internal Steuerelemente.ZeitraumAuswahl SelbsttestZeitraum => _zeitraum;
    internal string SelbsttestTage => _tage.Text;

    /// <summary>Selbsttest: Überschrift der Namensspalte (Lehrer, Klasse bzw. Raum je Reiter).</summary>
    internal string SelbsttestNamensspalte => _g.Columns["E"]!.HeaderText;

    /// <summary>Selbsttest: Überschriften der sichtbaren Spalten.</summary>
    internal List<string> SelbsttestSpalten() => _g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).Select(c => c.HeaderText).ToList();

    /// <summary>Selbsttest: Texte der Schaltflächen der Symbolleiste.</summary>
    internal List<string> SelbsttestKnoepfe()
    {
        static IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(k => Alle(k).Prepend(k));
        return Alle(this).OfType<ToolStrip>().SelectMany(t => t.Items.OfType<ToolStripItem>()).Select(i => i.Text ?? "").ToList();
    }

    /// <summary>
    /// Selbsttest: in der leeren Eingabezeile (bzw. der Zeile einer Absenz) einen Wert eintippen und mit Tab/Enter bestätigen; mit
    /// <paramref name="zeileVerlassen"/> wird die Zeile danach verlassen (Absenz gespeichert).
    /// </summary>
    internal void SelbsttestEingeben(string spalte, string wert, int? absenzId = null, bool zeileVerlassen = true)
    {
        var r = _g.Rows.Cast<DataGridViewRow>().LastOrDefault(x => absenzId is int id ? x.Tag is Abwesenheit a && a.Id == id : x.Tag is null && !x.IsNewRow)
                ?? throw new InvalidOperationException("Eingabezeile der Absenzen nicht gefunden.");
        _g.ClearSelection();
        _g.CurrentCell = r.Cells[spalte];
        r.Cells[spalte].Value = wert;
        ZelleGeaendert(_g, new DataGridViewCellEventArgs(r.Cells[spalte].ColumnIndex, r.Index));
        if (zeileVerlassen) EntwurfSofortUebernehmen();
    }

    private DataGridViewRow SelbsttestZeile(int? absenzId) =>
        _g.Rows.Cast<DataGridViewRow>().LastOrDefault(x => absenzId is int id ? x.Tag is Abwesenheit a && a.Id == id : x.Tag is null && !x.IsNewRow)
        ?? throw new InvalidOperationException(absenzId is int i ? $"Absenz {i} steht nicht in der Liste." : "Eingabezeile der Absenzen nicht gefunden.");

    /// <summary>
    /// Selbsttest: Text wie per Tastatur in ein Feld eintippen und mit Enter bestätigen (Bearbeiten, Prüfung, Übernahme; Auswahllisten
    /// mit Direktsuche). false = Eingabe abgelehnt – sie steht dann noch rot markiert in der Zelle.
    /// </summary>
    internal bool SelbsttestTippen(string spalte, string text, int? absenzId = null)
    {
        var r = SelbsttestZeile(absenzId);
        _g.Focus();
        _g.ClearSelection();
        _g.CurrentCell = r.Cells[spalte];
        r.Cells[spalte].Selected = true;
        if (!_g.BeginEdit(true)) return false;
        switch (_g.EditingControl)
        {
            case ComboBox cb:
                var i = Steuerelemente.ListenSuche.Treffer(cb, text);
                if (i >= 0) cb.SelectedIndex = i; else cb.Text = text;
                break;
            case { } ec: ec.Text = text; break;
        }
        _g.NotifyCurrentCellDirty(true);
        // bestätigen wie mit der Enter-Taste (Prüfung, Übernahme, Weiterspringen)
        Steuerelemente.Tabellenbedienung.Bearbeitungstaste(_g, Keys.Enter);
        return !_g.IsCurrentCellInEditMode;
    }

    /// <summary>Selbsttest: Fehlertext (rote Markierung) der aktuellen Zelle.</summary>
    internal string SelbsttestFehlertext => _g.CurrentCell?.ErrorText ?? "";

    /// <summary>Selbsttest: ist die aktuelle Zelle rot hinterlegt?</summary>
    internal bool SelbsttestRot => _g.CurrentCell?.Style.BackColor == Ui.FehlerHintergrund;

    /// <summary>Selbsttest: Taste auf der Liste wie vom Benutzer gedrückt (Esc, Entf, …).</summary>
    internal void SelbsttestTaste(Keys taste)
    {
        if (_g.IsCurrentCellInEditMode) Steuerelemente.Tabellenbedienung.Bearbeitungstaste(_g, taste);
        else Steuerelemente.Tabellenbedienung.Senden(_g, taste);
    }

    /// <summary>Selbsttest: aktuelle Zelle setzen (Absenz bzw. Eingabezeile).</summary>
    internal void SelbsttestWaehlen(string spalte, int? absenzId = null)
    {
        var r = SelbsttestZeile(absenzId);
        _g.ClearSelection();
        _g.CurrentCell = r.Cells[spalte];
        r.Cells[spalte].Selected = true;
    }

    /// <summary>Selbsttest: aus dem Element-Rollup gezogenen Text auf der Liste ablegen.</summary>
    internal void SelbsttestFallenlassen(string text)
    {
        if (ElementRollup.Text2Typ(text, out var typ, out var namen) is null) throw new InvalidOperationException("Ungültiger Ziehtext: " + text);
        ElementeEintragen(typ, namen);
    }

    /// <summary>Selbsttest: markierte Felder einer Spalte (Absenznummern).</summary>
    internal List<int> SelbsttestMarkierte(string spalte) => _g.SelectedCells.Cast<DataGridViewCell>()
        .Where(c => c.OwningColumn.Name == spalte && c.OwningRow.Tag is Abwesenheit).Select(c => ((Abwesenheit)c.OwningRow.Tag!).Id).Distinct().OrderBy(x => x).ToList();

    /// <summary>Selbsttest: Felder einer Spalte in mehreren Zeilen markieren (erste wird aktuell) und einmal eintippen.</summary>
    internal bool SelbsttestMehrfach(string spalte, IReadOnlyList<int> ids, string text)
    {
        var zeilen = ids.Select(id => SelbsttestZeile(id)).ToList();
        _g.Focus();
        _g.CurrentCell = zeilen[0].Cells[spalte];
        _g.ClearSelection();
        foreach (var z in zeilen) z.Cells[spalte].Selected = true;
        if (!_g.BeginEdit(true)) return false;
        switch (_g.EditingControl)
        {
            case ComboBox cb:
                var i = Steuerelemente.ListenSuche.Treffer(cb, text);
                if (i >= 0) cb.SelectedIndex = i; else cb.Text = text;
                break;
            case { } ec: ec.Text = text; break;
        }
        _g.NotifyCurrentCellDirty(true);
        Steuerelemente.Tabellenbedienung.Bearbeitungstaste(_g, Keys.Enter);
        return !_g.IsCurrentCellInEditMode;
    }

    /// <summary>Selbsttest: Absenz markieren und „Löschen“ (Rückfrage wird über den Antwort-Haken beantwortet).</summary>
    internal void SelbsttestLoeschen(int absenzId)
    {
        var r = _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag is Abwesenheit a && a.Id == absenzId)
                ?? throw new InvalidOperationException($"Absenz {absenzId} steht nicht in der Liste.");
        _g.ClearSelection();
        _g.CurrentCell = r.Cells["E"];
        r.Cells["E"].Selected = true;
        Loeschen();
    }
}
