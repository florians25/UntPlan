using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Stammdatenfenster (Klassen, Lehrer, Räume, Fächer, …): oben die Rasteransicht, unten die ein-/ausklappbare
/// Formularansicht. Spalten per Drag &amp; Drop ein-/ausblenden (Strg), Ansichten speichern, Sortieren, Kommentare,
/// Serienänderung, Umreihen, Druck.
/// </summary>
public sealed class StammdatenFenster : MdiFenster, IDruckbar
{
    private readonly ElementTyp _typ;
    private readonly List<Feld> _felder;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
    private readonly TabControl _formular = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _auswahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _ansichtBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Right | AnchorStyles.Top };
    private readonly Button _klappen = new() { Text = "▾", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
    private readonly ToolTip _tip = new();
    private List<string> _spalten = new();
    private string _ansichtName = "Standard";
    private bool _ansichtGeaendert;
    private bool _laden;
    private readonly Dictionary<string, Control> _formularEditoren = new();
    private Element? _aktuell;
    private int _ziehenZeile = -1;
    /// <summary>Filterzeile zwischen Überschriften und Tabelleninhalt (Schaltfläche „Filter“); je Spalte ein Eingabefeld.</summary>
    private readonly Panel _filterLeiste = new() { Dock = DockStyle.Top, Height = 22, Visible = false, BackColor = Color.FromArgb(255, 252, 230) };
    private readonly Dictionary<string, string> _filter = new();
    private ToolStripButton _filterKnopf = null!;
    /// <summary>„Felder mit Inhalt“: vorübergehend alle Spalten mit mindestens einem Eintrag; beim zweiten Klick die vorherigen Spalten.</summary>
    private List<string>? _spaltenVorMitInhalt;
    private ToolStripButton _mitInhaltKnopf = null!;
    /// <summary>Temporäre Sortierung per Klick in die Spaltenüberschrift (nach dem Schließen wieder aufgehoben).</summary>
    private (string Feld, bool Absteigend)? _temporaereSortierung;
    /// <summary>Beim nächsten Neuaufbau die Zeilenreihenfolge neu herstellen (nach Sortieren).</summary>
    private bool _neuSortieren;

    private string FensterSchluessel => $"Stammdaten.{_typ}";

    public StammdatenFenster(ElementTyp typ)
    {
        _typ = typ;
        _felder = Felder.Fuer(typ);
        Text = $"{Ui.TypName(typ, true)} / {Ui.TypName(typ)}";
        HilfeThema = "Stammdaten";
        Icon = SymbolIcon(typ switch { ElementTyp.Klasse => "klassen", ElementTyp.Lehrer => "lehrer", ElementTyp.Raum => "raeume", ElementTyp.Fach => "faecher", ElementTyp.Schueler => "schueler", _ => "rollup" });
        Size = new Size(760, 420);
        Positionieren();

        // Symbolleiste wie im Handbuch: Auswahl mit Blätterknöpfen | Normalform anzeigen, Felder der Ansicht, Neu, Löschen,
        // Serienänderung, Filter | Sortieren, Felder mit Inhalt, Ansicht fixiert | Zeitwünsche | Schuljahreskalender |
        // Farbe, Seitenlayout, Einstellungen, Aktualisieren
        var ts = Werkzeugleiste();
        ts.Items.Add(_auswahl);
        _auswahl.SelectedIndexChanged += (_, _) => { if (!_laden && _auswahl.SelectedItem is ElementEintrag ee) ZeileWaehlen(ee.Element); };
        ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(false), (_, _) => Blaettern(-1)) { ToolTipText = "Vorheriges Element", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(true), (_, _) => Blaettern(1)) { ToolTipText = "Nächstes Element", DisplayStyle = ToolStripItemDisplayStyle.Image });
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Normalform anzeigen", "normalform", Normalform));
        ts.Items.Add(Knopf("Felder der Ansicht", "uebersicht", FelderDialog));
        ts.Items.Add(Knopf("Neu", "neu", NeuesElement));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(Knopf("Serienänderung", "kopieren", Serienaenderung));
        _filterKnopf = Knopf("Filter", "filter", FilterUmschalten);
        ts.Items.Add(_filterKnopf);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Sortieren", "sortieren", Sortieren));
        _mitInhaltKnopf = Knopf("Felder mit Inhalt", "statistik", FelderMitInhalt);
        ts.Items.Add(_mitInhaltKnopf);
        // „Ansicht fixiert“: gedrückt = keine Synchronisation mit anderen Fenstern (Gegenstück der Fensterverknüpfung)
        var fixiert = Knopf("Ansicht fixiert", "schloss", () => { });
        fixiert.CheckOnClick = true;
        fixiert.Checked = !Verknuepft;
        fixiert.CheckedChanged += (_, _) => Verknuepft = !fixiert.Checked;
        ts.Items.Add(fixiert);
        if (typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach)
        {
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Knopf("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(_typ, _aktuell?.Id)));
        }
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Schuljahreskalender", "kalender", () => HauptFenster.Oeffnen<FerienFenster>("ferien", () => new FerienFenster())));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Farbe", "farbe", FarbeZuweisen));
        var layout = new ToolStripSplitButton("Seitenlayout", Symbole.Get("vorschau", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Seitenlayout" };
        layout.ButtonClick += (_, _) => Drucken(true);
        layout.DropDownItems.Add("Seitenlayout", Symbole.Get("vorschau", 16), (_, _) => Drucken(true));
        layout.DropDownItems.Add("Drucken", Symbole.Get("drucken", 16), (_, _) => Drucken(false));
        ts.Items.Add(layout);
        ts.Items.Add(Knopf("Einstellungen", "einstellungen", SchriftEinstellen));
        ts.Items.Add(Knopf("Aktualisieren (F5)", "aktualisieren", Neuaufbau));
        // weitere Funktionen dieses Programms (nicht im Handbuch-Fenster)
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Suchen", "suchen", Suchen));
        ts.Items.Add(Knopf("Kommentar erstellen", "kommentar", KommentarErstellen));
        if (typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach)
        {
            if (typ != ElementTyp.Raum) ts.Items.Add(Knopf("Unterricht", "unterricht", () => UnterrichtFenster.Zeigen(_typ, _aktuell?.Id)));
            ts.Items.Add(Knopf("Stundenplan", "stundenplan", () => StundenplanFenster.Neu(_typ, _aktuell?.Id, false)));
            // Absenzeingabe im Schuljahreskalender (Handbuch Vertretungsplanung Kap. 5.3)
            if (typ != ElementTyp.Fach)
                ts.Items.Add(Knopf("Schuljahreskalender – Absenzen", "kalender", () => { if (_aktuell is { } el) AbsenzKalenderDialog.Zeigen(this, _typ, el.Id); }));
            // Klassenzeitraster der Vertretungsplanung (Vertretung / Entfall / Eigenverantwortliches Arbeiten je Stunde)
            if (typ == ElementTyp.Klasse)
                ts.Items.Add(Knopf("Klassenzeitraster", "zeitraster", () => { if ((_aktuell as Klasse ?? D.Klassen.FirstOrDefault()) is { } k) VertretungsZeitrasterDialog.Zeigen(this, k); }));
        }
        if (typ == ElementTyp.Fach)
        {
            ts.Items.Add(Knopf("Fachfarben auf Standard setzen", "farbe", FachfarbenStandard, false));
            ts.Items.Add(Knopf("Farbe löschen (markierte Fächer, sonst alle)", "loeschen", () => FarbenLoeschen(true), false));
        }
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F5) { Neuaufbau(); e.Handled = true; } };

        Ui.RasterGestalten(_grid);
        _grid.AllowUserToOrderColumns = true;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = true;
        _grid.AllowDrop = true;
        _grid.ShowCellToolTips = true;
        _grid.CellEndEdit += CellEndEdit;
        _grid.CellDoubleClick += CellDoubleClick;
        _grid.CurrentCellChanged += (_, _) => AktuellesGeaendert();
        _grid.CellPainting += CellPainting;
        _grid.CellToolTipTextNeeded += CellToolTip;
        _grid.EditingControlShowing += EditingControlShowing;
        // vor der Direktsuche: kommagetrennte Listen (Klassenlehrer „LANG,EGB“) annehmen
        _grid.CellValidating += ListeneingabePruefen;
        Steuerelemente.SuchKombi.Aktivieren(_grid, AuswahlStil);
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CellContentClick += CellContentClick;
        _grid.DragOver += (_, e) => e.Effect = e.Data!.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.None;
        _grid.DragDrop += GridDragDrop;
        _grid.MouseDown += GridMouseDown;
        _grid.MouseMove += GridMouseMove;
        _grid.ColumnDisplayIndexChanged += (_, _) => { if (!_laden) AnsichtVeraendert(); };
        _grid.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) Kontextmenue(e); };
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode) { ZellenLeeren(); e.Handled = true; } };

        _formular.AllowDrop = true;
        _formular.DragOver += (_, e) => e.Effect = (e.Data!.GetData(DataFormats.Text) as string)?.StartsWith("aus:") == true ? DragDropEffects.Move : DragDropEffects.None;
        _formular.DragDrop += (_, e) => { if (e.Data!.GetData(DataFormats.Text) is string s && s.StartsWith("aus:")) SpalteAusblenden(s[4..]); };

        // Fußleiste: Ansichtsauswahl unten rechts mit Abstand zum Rand, Größengriff frei
        var unten = new Steuerelemente.Fussleiste { BackColor = Color.FromArgb(246, 248, 251) };
        _klappen.Left = 4;
        _klappen.Top = (Steuerelemente.Fussleiste.Hoehe - _klappen.Height) / 2;
        _klappen.Click += (_, _) => FormularUmschalten();
        _tip.SetToolTip(_klappen, "Formularansicht ein-/ausklappen");
        unten.Controls.Add(_klappen);
        unten.Rechts = _ansichtBox;
        _ansichtBox.SelectionChangeCommitted += (_, _) => AnsichtGewaehlt();

        _split.Panel1.Controls.Add(_grid);
        _split.Panel1.Controls.Add(_filterLeiste);
        _grid.ColumnWidthChanged += (_, _) => FilterLeisteAnordnen();
        _grid.ColumnDisplayIndexChanged += (_, _) => FilterLeisteAnordnen();
        _grid.Scroll += (_, e) => { if (e.ScrollOrientation == ScrollOrientation.HorizontalScroll) FilterLeisteAnordnen(); };
        _grid.ColumnHeaderMouseClick += SpaltenkopfKlick;
        _split.Panel2.Controls.Add(_formular);
        _split.Panel2Collapsed = true;
        Controls.Add(_split);
        Controls.Add(unten);
        Controls.Add(ts);
        Load += (_, _) => { _split.SplitterDistance = Math.Max(80, _split.Height - 170); };

        AnsichtLaden(D.Ansichten.FirstOrDefault(a => a.Fenster == FensterSchluessel)?.Name ?? "Standard");
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
        };
    }

    private sealed record ElementEintrag(Element Element)
    {
        public override string ToString() => Element.Kurzname;
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || typ != _typ || !Visible || !Verknuepft || IsDisposed) return;
        var e = D.AlleElemente(_typ).FirstOrDefault(x => x.Id == id);
        if (e != null && e != _aktuell) ZeileWaehlen(e, melden: false);
    }

    /// <summary>Fensterverknüpfung: Stunde im Stundenplan bzw. Unterricht gewählt – Klasse, Lehrkraft, Raum bzw. Fach dieser Stunde zeigen.</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Visible || !Verknuepft || IsDisposed || _typ is not (ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach)) return;
        if (AppZustand.ElementDerStunde(_typ, u, p, _aktuell?.Id) is not int id || id == _aktuell?.Id) return;
        var e = D.AlleElemente(_typ).FirstOrDefault(x => x.Id == id);
        if (e != null) ZeileWaehlen(e, melden: false);
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art is AenderungsArt.Stundenplan or AenderungsArt.Vertretung) return;
        Neuaufbau();
    }

    private IEnumerable<Element> Elemente()
    {
        var ansicht = D.Ansichten.FirstOrDefault(a => a.Fenster == FensterSchluessel && a.Name == _ansichtName);
        var liste = Stammdaten.Sortiert(D, _typ).ToList();
        if (_temporaereSortierung is { } temp && _felder.FirstOrDefault(x => x.Schluessel == temp.Feld) is { } tf)
        {
            Func<Element, string> key = e => tf.Anzeige(e, Ix);
            var cmp = StringComparer.Create(Format.De, true);
            return temp.Absteigend ? liste.OrderByDescending(key, cmp) : liste.OrderBy(key, cmp);
        }
        if (ansicht?.Sortierung.Count > 0)
        {
            IOrderedEnumerable<Element>? o = null;
            foreach (var k in ansicht.Sortierung)
            {
                var f = _felder.FirstOrDefault(x => x.Schluessel == k.Feld);
                if (f is null) continue;
                Func<Element, string> key = e => f.Anzeige(e, Ix);
                var cmp = StringComparer.Create(Format.De, true);
                o = o is null ? (k.Absteigend ? liste.OrderByDescending(key, cmp) : liste.OrderBy(key, cmp)) : (k.Absteigend ? o.ThenByDescending(key, cmp) : o.ThenBy(key, cmp));
            }
            if (o != null) return o;
        }
        return liste;
    }

    /// <summary>Platzhalter der Eingabezeile für neue Elemente am Ende der Liste.</summary>
    private static readonly object EingabeZeile = new();

    private static string Schluessel(object o) => o is Element e ? "E" + e.Id : "neu";
    private static string? Schluessel(DataGridViewRow r) => r.Tag is Element e ? "E" + e.Id : "neu";

    /// <summary>Spalten, für die die Tabelle aufgebaut ist (Spalten werden nur bei geänderter Auswahl neu angelegt).</summary>
    private string _spaltenStand = "";

    /// <summary>
    /// Liste aktualisieren, ohne sie ganz neu zu laden: geänderte Elemente an Ort und Stelle, neue an ihrem Platz, gelöschte heraus –
    /// aktuelles Element, Spalte und Bildlauf bleiben, nichts verrutscht. Spalten werden nur neu angelegt, wenn sich die Felder ändern.
    /// </summary>
    private void Neuaufbau()
    {
        _laden = true;
        var aktId = _aktuell?.Id;
        var spalte = _grid.CurrentCell?.ColumnIndex ?? 1;
        _grid.SuspendLayout();
        var stand = string.Join("|", _spalten);
        if (stand != _spaltenStand || _grid.Columns.Count == 0)
        {
            _spaltenStand = stand;
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            foreach (var key in _spalten)
            {
                var f = _felder.FirstOrDefault(x => x.Schluessel == key);
                if (f is null) continue;
                DataGridViewColumn col = f.Art switch
                {
                    FeldArt.Bool => new DataGridViewCheckBoxColumn { ThreeState = false },
                    FeldArt.Auswahl => new DataGridViewComboBoxColumn { DataSource = f.Auswahl.ToList(), FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing },
                    // Elementfelder (z. B. Raum, Klassenlehrer) als Auswahlliste mit Direktsuche wie die Lehrerauswahl im Vertretungsmodus
                    FeldArt.Element or FeldArt.ElementListe => Steuerelemente.SuchKombi.Spalte(f.Schluessel, f.Titel, f.Breite, Array.Empty<string>()),
                    _ => new DataGridViewTextBoxColumn(),
                };
                col.Name = f.Schluessel;
                col.HeaderText = f.Titel;
                col.Width = f.Breite;
                // Programmatic: Klick in die Überschrift sortiert temporär (mit Sortierpfeil), die Tabelle sortiert nicht selbst
                col.SortMode = DataGridViewColumnSortMode.Programmatic;
                col.ReadOnly = !f.Editierbar || f.Schluessel == "Kurzname" && _typ != ElementTyp.Schueler;
                if (f.Schluessel is "Kurzname" or "Langname")
                    col.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 255);
                col.Tag = f;
                _grid.Columns.Add(col);
            }
        }
        foreach (DataGridViewColumn c in _grid.Columns)
            c.HeaderCell.SortGlyphDirection = _temporaereSortierung is { } ts && ts.Feld == c.Name ? (ts.Absteigend ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        AuswahlWerteAktualisieren();
        var eintraege = Elemente().Cast<object>().Append(EingabeZeile).ToList();
        Steuerelemente.TabellenAbgleich.Anwenden(_grid, eintraege, Schluessel, Schluessel, (row, o) =>
        {
            row.Tag = o as Element;
            if (o is Element) ZeileFuellen(row);
            else
            {
                // Eingabezeile für neue Elemente
                foreach (DataGridViewCell c in row.Cells) if (c.Value != null && c is not DataGridViewCheckBoxCell) c.Value = null;
                row.DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 240);
            }
        }, neuSortieren: _neuSortieren);
        _neuSortieren = false;
        _grid.ResumeLayout();
        _auswahl.Items.Clear();
        foreach (var e in Stammdaten.Sortiert(D, _typ)) _auswahl.Items.Add(new ElementEintrag(e));
        _laden = false;
        var ziel = aktId.HasValue ? D.AlleElemente(_typ).FirstOrDefault(x => x.Id == aktId) : D.AlleElemente(_typ).FirstOrDefault();
        if (ziel != null && _grid.CurrentRow?.Tag != ziel) ZeileWaehlen(ziel, false, Math.Min(spalte, _grid.Columns.Count - 1));
        else if (ziel != null)
        {
            // dieselbe Zeile bleibt aktuell: Auswahlliste und Formular wieder auf das Element setzen
            _aktuell = null;
            AktuellesGeaendert(false);
        }
        FormularAufbauen();
        FilterLeisteAufbauen();
        FilterAnwenden();
    }

    /// <summary>
    /// Einträge der Auswahlspalten: leer, alle Kurznamen und die schon eingetragenen Listenwerte (z. B. Klassenlehrer „LANG,EGB“) –
    /// eine Auswahlzelle zeigt nur Werte, die in ihrer Liste stehen.
    /// </summary>
    private void AuswahlWerteAktualisieren()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            if (c is not DataGridViewComboBoxColumn cc || c.Tag is not Feld { Art: FeldArt.Element or FeldArt.ElementListe } f) continue;
            var werte = new List<string> { "" };
            werte.AddRange(Stammdaten.Sortiert(D, f.RefTyp).Where(x => !x.Ignorieren).Select(x => x.Kurzname));
            if (f.Art == FeldArt.ElementListe)
                werte.AddRange(Elemente().Select(e => f.Anzeige(e, Ix)).Where(t => t.Contains(',')));
            var neu = werte.Distinct().ToList();
            if (cc.Items.Cast<object>().Select(o => o.ToString()).SequenceEqual(neu)) continue;
            cc.Items.Clear();
            foreach (var w in neu) cc.Items.Add(w);
        }
    }

    /// <summary>Wert in die Liste der Auswahlspalte aufnehmen (z. B. neue Kombination „LANG,EGB“), damit die Zelle ihn zeigt.</summary>
    private static void AuswahlWertSichern(DataGridViewColumn spalte, string wert)
    {
        if (spalte is DataGridViewComboBoxColumn cc && wert.Length > 0 && !cc.Items.Contains(wert)) cc.Items.Add(wert);
    }

    /// <summary>Langname in der Auswahlliste (Lehrkräfte mit vollem Namen) – wie in der Vertretungsliste.</summary>
    private Steuerelemente.ListenStil? AuswahlStil(DataGridViewCell zelle, string text)
    {
        if (zelle.OwningColumn.Tag is not Feld { Art: FeldArt.Element or FeldArt.ElementListe } f) return null;
        var e = D.AlleElemente(f.RefTyp).FirstOrDefault(x => x.Kurzname == text);
        return e is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, e is Lehrer l ? l.VollerName : e.Langname);
    }

    /// <summary>
    /// Listenfelder (z. B. Klassenlehrer): mehrere Kurznamen mit Komma eintippen („lang, egb“) – gültige Eingaben werden zu
    /// „LANG,EGB“ und in die Auswahlliste aufgenommen, bevor die Direktsuche die Eingabe prüft.
    /// </summary>
    private void ListeneingabePruefen(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Tag is not Feld { Art: FeldArt.ElementListe } f || _grid.EditingControl is not ComboBox cb) return;
        var teile = cb.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length < 2) return;
        var namen = new List<string>();
        foreach (var t in teile)
        {
            var el = D.AlleElemente(f.RefTyp).FirstOrDefault(x => x.Kurzname.Equals(t, StringComparison.OrdinalIgnoreCase));
            if (el is null) return;
            if (!namen.Contains(el.Kurzname)) namen.Add(el.Kurzname);
        }
        var wert = string.Join(",", namen);
        AuswahlWertSichern(_grid.Columns[e.ColumnIndex], wert);
        if (!cb.Items.Contains(wert)) cb.Items.Add(wert);
        cb.SelectedItem = wert;
        cb.Text = wert;
    }

    private void ZeileFuellen(DataGridViewRow row)
    {
        if (row.Tag is not Element e) return;
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            var f = (Feld)c.Tag!;
            row.Cells[c.Index].Value = f.Art switch
            {
                FeldArt.Bool => f.Get(e) is true,
                FeldArt.Auswahl => f.Auswahl.ElementAtOrDefault(f.Get(e) is int i ? i : 0),
                _ => f.Anzeige(e, Ix),
            };
            if (f.Schluessel == "Hintergrund" || (f.Schluessel == "Langname" && e is Fach))
            {
                var farbe = Ui.Farbe(e.Hintergrund, Color.White);
                row.Cells[c.Index].Style.BackColor = farbe;
                row.Cells[c.Index].Style.ForeColor = Ui.Textfarbe(farbe);
            }
        }
        var vorder = e.Ignorieren ? Color.Gray : Color.Empty;
        if (row.DefaultCellStyle.ForeColor != vorder) row.DefaultCellStyle.ForeColor = vorder;
    }

    private void ZeileWaehlen(Element e, bool melden = true, int spalte = -1)
    {
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.Tag != e) continue;
            var c = spalte >= 0 ? spalte : _grid.CurrentCell?.ColumnIndex ?? 0;
            if (c < 0 || c >= _grid.Columns.Count) c = 0;
            if (_grid.Columns.Count == 0) return;
            _laden = !melden;
            try { _grid.CurrentCell = r.Cells[c]; } catch (InvalidOperationException) { }
            _laden = false;
            AktuellesGeaendert(melden);
            return;
        }
    }

    private void AktuellesGeaendert(bool melden = true)
    {
        if (_laden) return;
        var e = _grid.CurrentRow?.Tag as Element;
        if (e == _aktuell) return;
        _aktuell = e;
        _laden = true;
        _auswahl.SelectedItem = _auswahl.Items.Cast<ElementEintrag>().FirstOrDefault(x => x.Element == e);
        _laden = false;
        FormularFuellen();
        if (e != null && melden) AppZustand.Waehlen(_typ, e.Id, this);
    }

    // ------------------------------------------------------------------------------------------
    // Bearbeiten
    // ------------------------------------------------------------------------------------------

    private void CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laden || e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        var f = (Feld)_grid.Columns[e.ColumnIndex].Tag!;
        var wert = _grid[e.ColumnIndex, e.RowIndex].Value;
        var text = wert switch { bool b => b ? "X" : "", null => "", _ => wert.ToString()! };
        if (row.Tag is null)
        {
            // Eingabezeile: neues Element anlegen
            if (f.Schluessel is not ("Kurzname" or "Langname") || text.Trim().Length == 0) { _grid[e.ColumnIndex, e.RowIndex].Value = null; return; }
            Element? neu = null;
            if (Aendern($"{Ui.TypName(_typ)} anlegen", AenderungsArt.Stammdaten, d =>
            {
                neu = Stammdaten.Neu(d, _typ, f.Schluessel == "Kurzname" ? text : Kuerzel(text, d));
                if (f.Schluessel == "Langname") neu.Langname = text;
                if (neu is Fach fa) fa.Hintergrund = unchecked((int)Farbpalette[d.Faecher.Count % Farbpalette.Length]);
            }))
            {
                var spalte = e.ColumnIndex;
                NachZellwechsel(() =>
                {
                    Neuaufbau();
                    if (neu != null) ZeileWaehlen(neu, true, spalte);
                });
            }
            return;
        }
        if (!f.Editierbar) return;
        // Serienänderung: alle markierten Zellen derselben Spalte
        var ziele = _grid.SelectedCells.Cast<DataGridViewCell>().Where(c => c.ColumnIndex == e.ColumnIndex && c.OwningRow.Tag is Element).Select(c => (Element)c.OwningRow.Tag!).ToList();
        if (!ziele.Contains((Element)row.Tag)) ziele.Add((Element)row.Tag);
        object? parsed;
        try { parsed = f.Parse(text, D); }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            Ui.Fehler(new FormatException(ex.Message));
            ZeileFuellen(row);
            return;
        }
        Aendern(ziele.Count > 1 ? $"Serienänderung {f.Titel}" : $"{f.Titel} geändert", AenderungsArt.Stammdaten, _ =>
        {
            foreach (var z in ziele) f.Set!(z, parsed);
        });
        foreach (DataGridViewRow r in _grid.Rows) if (r.Tag is Element el && ziele.Contains(el)) ZeileFuellen(r);
        FormularFuellen();
    }

    private static readonly uint[] Farbpalette = { 0xFFF28B82, 0xFF7BAAF7, 0xFF81C995, 0xFFFFD54F, 0xFFCE93D8, 0xFF4DB6AC, 0xFFFFB74D, 0xFFA1887F, 0xFF90A4AE, 0xFFF48FB1 };

    private static string Kuerzel(string langname, Schuldatei d)
    {
        var basis = new string(langname.Where(char.IsLetterOrDigit).Take(4).ToArray());
        if (basis.Length == 0) basis = "N";
        var k = basis;
        var i = 1;
        while (d.Lehrer.Cast<Element>().Concat(d.Klassen).Concat(d.Raeume).Concat(d.Faecher).Any(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))) k = basis + i++;
        return k;
    }

    private void CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _grid.EndEdit();
    }

    private void CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var f = (Feld)_grid.Columns[e.ColumnIndex].Tag!;
        if (_grid.Rows[e.RowIndex].Tag is not Element el) return;
        if (f.Schluessel == "Kurzname" && _typ != ElementTyp.Schueler)
        {
            var neu = KurznameDialog.Zeigen(this, el.Kurzname, el.Langname);
            if (neu is null) return;
            if (Aendern("Kurzname geändert", AenderungsArt.Stammdaten, d => { Stammdaten.Umbenennen(d, el, neu.Value.Kurz); el.Langname = neu.Value.Lang; })) Neuaufbau();
        }
        else if (f.Art == FeldArt.Farbe && f.Editierbar)
        {
            using var cd = new ColorDialog { Color = Ui.Farbe(el.Hintergrund, Color.White), FullOpen = true };
            if (cd.ShowDialog(this) != DialogResult.OK) return;
            Aendern("Farbe geändert", AenderungsArt.Stammdaten, _ => f.Set!(el, cd.Color.ToArgb()));
            ZeileFuellen(_grid.Rows[e.RowIndex]);
        }
    }

    private void ZellenLeeren()
    {
        var zellen = _grid.SelectedCells.Cast<DataGridViewCell>().Where(c => c.OwningRow.Tag is Element && ((Feld)_grid.Columns[c.ColumnIndex].Tag!).Editierbar).ToList();
        if (zellen.Count == 0) return;
        Aendern("Felder leeren", AenderungsArt.Stammdaten, d =>
        {
            foreach (var c in zellen)
            {
                var f = (Feld)_grid.Columns[c.ColumnIndex].Tag!;
                if (f.Schluessel == "Kurzname") continue;
                f.Set!((Element)c.OwningRow.Tag!, f.Parse("", d));
            }
        });
        Neuaufbau();
    }

    private void EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox tb) return;
        var f = (Feld)_grid.CurrentCell!.OwningColumn.Tag!;
        tb.AutoCompleteMode = AutoCompleteMode.None;
        if (f.Art is FeldArt.Element or FeldArt.ElementListe)
        {
            // Autovervollständigung: erstes passendes Element wird angeboten, Übernahme mit Return/Tab
            var src = new AutoCompleteStringCollection();
            src.AddRange(D.AlleElemente(f.RefTyp).Select(x => x.Kurzname).ToArray());
            tb.AutoCompleteCustomSource = src;
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteMode = f.Art == FeldArt.Element ? AutoCompleteMode.Append : AutoCompleteMode.None;
        }
    }

    /// <summary>Neues Element anlegen: wie im Vorbild zuerst der Dialog „Kurzname“ (Name und Langname).</summary>
    private void NeuesElement()
    {
        var vorschlag = KurznameDialog.Vorschlag(D.AlleElemente(_typ).Select(e => e.Kurzname));
        var eingabe = KurznameDialog.Zeigen(this, vorschlag, "");
        if (eingabe is not { } ein || string.IsNullOrWhiteSpace(ein.Kurz)) return;
        var (name, lang) = ein;
        Element? neu = null;
        if (Aendern($"{Ui.TypName(_typ)} anlegen", AenderungsArt.Stammdaten, d => { neu = Stammdaten.Neu(d, _typ, name); if (lang.Length > 0) neu.Langname = lang; }))
        {
            Neuaufbau();
            if (neu != null) ZeileWaehlen(neu);
        }
    }

    private void Loeschen()
    {
        var auswahl = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Element>().Distinct().ToList();
        if (auswahl.Count == 0 && _aktuell != null) auswahl.Add(_aktuell);
        if (auswahl.Count == 0) return;
        var verwendet = auswahl.Sum(e => Stammdaten.Verwendung(D, e));
        var text = auswahl.Count == 1 ? $"{Ui.TypName(_typ)} \"{auswahl[0].Kurzname}\" löschen?" : $"{auswahl.Count} Elemente löschen?";
        if (verwendet > 0) text += $"\n\nDas Element wird in {verwendet} Unterricht(en) verwendet; die Verweise werden entfernt.";
        text += "\n\nHinweis: Stammdaten sollten im laufenden Schuljahr nicht gelöscht werden (Statistik).";
        if (!Ui.Frage(text)) return;
        _aktuell = null;
        if (Aendern("Löschen", AenderungsArt.Alles, d => auswahl.ForEach(e => Stammdaten.Loeschen(d, e)))) Neuaufbau();
    }

    private void Suchen()
    {
        var s = Ui.Eingabe("Suchbegriff (Kurz- oder Langname):", "Suchen");
        if (string.IsNullOrWhiteSpace(s)) return;
        var e = D.AlleElemente(_typ).FirstOrDefault(x => x.Kurzname.Contains(s, StringComparison.OrdinalIgnoreCase) || x.Langname.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (e != null) ZeileWaehlen(e);
        else Ui.Info("Nicht gefunden.");
    }

    private void Normalform()
    {
        _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        var breite = _grid.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width) + _grid.RowHeadersWidth + 40;
        var hoehe = Math.Min(_grid.Rows.Count * _grid.RowTemplate.Height + 120 + (_split.Panel2Collapsed ? 0 : 170), (AppZustand.Haupt?.ClientSize.Height ?? 800) - 200);
        Size = new Size(Math.Min(Math.Max(420, breite), (AppZustand.Haupt?.ClientSize.Width ?? 1200) - 40), Math.Max(260, hoehe));
    }

    // ------------------------------------------------------------------------------------------
    // Umreihen (Drag & Drop der ersten Spalte) und Spalten einblenden/ausblenden
    // ------------------------------------------------------------------------------------------

    private void GridMouseDown(object? sender, MouseEventArgs e)
    {
        var hit = _grid.HitTest(e.X, e.Y);
        _ziehenZeile = -1;
        if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0 && _grid.Rows[hit.RowIndex].Tag is Element) _ziehenZeile = hit.RowIndex;
        if (hit.Type == DataGridViewHitTestType.ColumnHeader && (ModifierKeys & Keys.Control) != 0)
        {
            var key = ((Feld)_grid.Columns[hit.ColumnIndex].Tag!).Schluessel;
            if (key != "Kurzname") _grid.DoDragDrop("aus:" + key, DragDropEffects.Move);
        }
    }

    private void GridMouseMove(object? sender, MouseEventArgs e)
    {
        if (_ziehenZeile >= 0 && e.Button == MouseButtons.Left)
        {
            var zeile = _ziehenZeile;
            _ziehenZeile = -1;
            _grid.DoDragDrop("zeile:" + zeile, DragDropEffects.Move);
        }
    }

    private void GridDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data!.GetData(DataFormats.Text) is not string s) return;
        var p = _grid.PointToClient(new Point(e.X, e.Y));
        var hit = _grid.HitTest(p.X, p.Y);
        if (s.StartsWith("zeile:"))
        {
            var von = int.Parse(s[6..]);
            var nach = hit.RowIndex < 0 ? _grid.Rows.Count - 2 : hit.RowIndex;
            if (_grid.Rows[von].Tag is not Element el || von == nach) return;
            var liste = Elemente().ToList();
            liste.Remove(el);
            var ziel = _grid.Rows[Math.Min(nach, _grid.Rows.Count - 1)].Tag as Element;
            var idx = ziel is null ? liste.Count : liste.IndexOf(ziel);
            liste.Insert(Math.Max(0, idx), el);
            Aendern("Umreihen", AenderungsArt.Stammdaten, _ => { for (var i = 0; i < liste.Count; i++) liste[i].Sortierung = i; });
            Neuaufbau();
        }
        else if (s.StartsWith("ein:"))
        {
            var key = s[4..];
            if (_spalten.Contains(key)) return;
            var pos = hit.ColumnIndex >= 0 ? _spalten.IndexOf(((Feld)_grid.Columns[hit.ColumnIndex].Tag!).Schluessel) : _spalten.Count;
            _spalten.Insert(Math.Max(1, pos), key);
            AnsichtVeraendert();
            Neuaufbau();
        }
        else if (_grid.Rows.Count > 0 && hit.RowIndex >= 0 && hit.ColumnIndex >= 0 && !s.Contains(':'))
        {
            // Element-Rollup: Kurzname in ein Feld ziehen
            _grid.CurrentCell = _grid[hit.ColumnIndex, hit.RowIndex];
            var cell = _grid.CurrentCell;
            var f = (Feld)_grid.Columns[hit.ColumnIndex].Tag!;
            var wert = f.Art == FeldArt.ElementListe && cell.Value is string vorh && vorh.Length > 0 ? vorh + "," + s : s;
            AuswahlWertSichern(cell.OwningColumn, wert);
            cell.Value = wert;
            CellEndEdit(this, new DataGridViewCellEventArgs(hit.ColumnIndex, hit.RowIndex));
        }
    }

    private void SpalteAusblenden(string key)
    {
        if (key == "Kurzname") return;
        _spalten.Remove(key);
        AnsichtVeraendert();
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Formularansicht
    // ------------------------------------------------------------------------------------------

    private void FormularUmschalten()
    {
        _split.Panel2Collapsed = !_split.Panel2Collapsed;
        _klappen.Text = _split.Panel2Collapsed ? "▾" : "▴";
        if (!_split.Panel2Collapsed) _split.SplitterDistance = Math.Max(80, _split.Height - 180);
    }

    private void FormularAufbauen()
    {
        _formular.TabPages.Clear();
        _formularEditoren.Clear();
        foreach (var gruppe in _felder.GroupBy(f => f.Karteikarte))
        {
            var page = new TabPage(gruppe.Key) { AutoScroll = true, BackColor = Color.White };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true, AutoScroll = true, Padding = new Padding(4) };
            foreach (var f in gruppe)
            {
                // wie im Handbuch: Eingabefeld links, Beschriftung rechts daneben (Kontrollkästchen mit Text)
                var p = new Panel { Width = f.Art == FeldArt.Bool ? 250 : 290, Height = 26, Margin = new Padding(2) };
                var lbl = new Label { Text = f.Formulartext, Left = f.Art == FeldArt.Bool ? 20 : 132, Top = 4, Width = f.Art == FeldArt.Bool ? 226 : 156, AutoEllipsis = true, Cursor = Cursors.SizeAll, ForeColor = _spalten.Contains(f.Schluessel) ? Color.Gray : Color.Black };
                _tip.SetToolTip(lbl, "Zum Einblenden in die Rasteransicht ziehen (Drag & Drop)");
                var key = f.Schluessel;
                lbl.MouseDown += (_, me) => { if (me.Button == MouseButtons.Left) lbl.DoDragDrop("ein:" + key, DragDropEffects.Copy); };
                Control ed;
                if (f.Art == FeldArt.Bool)
                {
                    var cb = new CheckBox { Left = 2, Top = 3, Width = 16, Enabled = f.Editierbar };
                    cb.CheckedChanged += (_, _) => FormularWert(f, cb.Checked ? "X" : "");
                    ed = cb;
                }
                else if (f.Art == FeldArt.Auswahl || f.Art == FeldArt.Element)
                {
                    var cb = new ComboBox { Left = 2, Top = 1, Width = 124, DropDownStyle = f.Art == FeldArt.Auswahl ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown, Enabled = f.Editierbar };
                    if (f.Art == FeldArt.Auswahl) cb.Items.AddRange(f.Auswahl);
                    else { cb.Items.Add(""); cb.Items.AddRange(D.AlleElemente(f.RefTyp).Select(x => (object)x.Kurzname).ToArray()); Steuerelemente.ListenSuche.Einrichten(cb); }
                    cb.SelectionChangeCommitted += (_, _) => FormularWert(f, cb.SelectedItem?.ToString() ?? "");
                    cb.Leave += (_, _) =>
                    {
                        if (f.Art != FeldArt.Element) return;
                        if (cb.Text.Trim().Length > 0 && Steuerelemente.ListenSuche.Treffer(cb, cb.Text) is var i and >= 0) cb.SelectedIndex = i;
                        FormularWert(f, cb.Text);
                    };
                    ed = cb;
                }
                else
                {
                    var tb = new TextBox { Left = 2, Top = 1, Width = 124, ReadOnly = !f.Editierbar || f.Schluessel == "Kurzname" };
                    tb.Leave += (_, _) => FormularWert(f, tb.Text);
                    tb.KeyDown += (_, ke) => { if (ke.KeyCode == Keys.Enter) { FormularWert(f, tb.Text); ke.SuppressKeyPress = true; } };
                    ed = tb;
                }
                p.Controls.Add(lbl);
                p.Controls.Add(ed);
                _formularEditoren[f.Schluessel] = ed;
                flow.Controls.Add(p);
            }
            page.Controls.Add(flow);
            _formular.TabPages.Add(page);
        }
        if (_typ == ElementTyp.Lehrer)
        {
            // Karteikarte „WertKorrektur“ wie im Handbuch (vor „Vertr.“)
            _wertkorrektur = new WertkorrekturSeite(() => _aktuell as Lehrer, (text, aktion) => Aendern(text, AenderungsArt.Stammdaten, aktion));
            var vor = _formular.TabPages.Cast<TabPage>().ToList().FindIndex(p => p.Text == "Vertr.");
            if (vor >= 0) _formular.TabPages.Insert(vor, _wertkorrektur); else _formular.TabPages.Add(_wertkorrektur);
        }
        FormularFuellen();
    }

    private bool _formularLaden;
    private WertkorrekturSeite? _wertkorrektur;

    private void FormularFuellen()
    {
        _wertkorrektur?.Fuellen();
        _formularLaden = true;
        foreach (var f in _felder)
        {
            if (!_formularEditoren.TryGetValue(f.Schluessel, out var ed)) continue;
            var e = _aktuell;
            switch (ed)
            {
                case CheckBox cb: cb.Checked = e != null && f.Get(e) is true; break;
                case ComboBox cb when f.Art == FeldArt.Auswahl: cb.SelectedIndex = e != null && f.Get(e) is int i && i < cb.Items.Count ? i : -1; break;
                case ComboBox cb: cb.Text = e is null ? "" : f.Anzeige(e, Ix); break;
                case TextBox tb: tb.Text = e is null ? "" : f.Anzeige(e, Ix); break;
            }
        }
        _formularLaden = false;
    }

    private void FormularWert(Feld f, string text)
    {
        if (_formularLaden || _aktuell is null || !f.Editierbar) return;
        if (f.Anzeige(_aktuell, Ix) == text && f.Art != FeldArt.Bool) return;
        object? v;
        try { v = f.Parse(text, D); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { Ui.Fehler(new FormatException(ex.Message)); FormularFuellen(); return; }
        var el = _aktuell;
        if (f.Art == FeldArt.Bool && Equals(f.Get(el), v)) return;
        Aendern($"{f.Titel} geändert", AenderungsArt.Stammdaten, _ => f.Set!(el, v));
        foreach (DataGridViewRow r in _grid.Rows) if (r.Tag == el) ZeileFuellen(r);
    }

    // ------------------------------------------------------------------------------------------
    // Ansichten
    // ------------------------------------------------------------------------------------------

    private void AnsichtLaden(string name)
    {
        var a = D.Ansichten.FirstOrDefault(x => x.Fenster == FensterSchluessel && x.Name == name);
        _ansichtName = a?.Name ?? "Standard";
        _spalten = a?.Spalten.Where(s => _felder.Any(f => f.Schluessel == s)).ToList() ?? _felder.Where(f => f.Standard).Select(f => f.Schluessel).ToList();
        if (!_spalten.Contains("Kurzname")) _spalten.Insert(0, "Kurzname");
        _ansichtGeaendert = false;
        AnsichtBoxFuellen();
        Neuaufbau();
    }

    private void AnsichtBoxFuellen()
    {
        _ansichtBox.Items.Clear();
        var namen = new[] { "Standard" }.Concat(D.Ansichten.Where(a => a.Fenster == FensterSchluessel).Select(a => a.Name)).Distinct().ToList();
        foreach (var n in namen) _ansichtBox.Items.Add(n == _ansichtName && _ansichtGeaendert ? n + "*" : n);
        _ansichtBox.Items.Add(FormatSpeichern);
        _ansichtBox.Items.Add(FormatSpeichernAls);
        _ansichtBox.Items.Add(FormatBearbeiten);
        _ansichtBox.Items.Add(FormatLoeschen);
        _ansichtBox.SelectedIndex = namen.IndexOf(_ansichtName);
    }

    private void AnsichtVeraendert()
    {
        _spalten = _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).Select(c => c.Name).Where(_spalten.Contains).Concat(_spalten.Where(s => !_grid.Columns.Contains(s))).Distinct().ToList();
        _ansichtGeaendert = true;
        AnsichtBoxFuellen();
    }

    private void AnsichtGewaehlt()
    {
        var t = _ansichtBox.SelectedItem?.ToString() ?? "";
        if (t == FormatSpeichern) AnsichtSpeichern(_ansichtName);
        else if (t == FormatSpeichernAls)
        {
            var n = Ui.Eingabe("Name des neuen Formats:", "Format speichern als", "Neues Format");
            if (!string.IsNullOrWhiteSpace(n)) AnsichtSpeichern(n.Trim());
            else AnsichtBoxFuellen();
        }
        else if (t == FormatBearbeiten)
        {
            // Ansicht umbenennen (das Standardformat behält seinen Namen)
            var neu = _ansichtName == "Standard" ? null : AnsichtUmbenennenDialog.Zeigen(this, _ansichtName);
            if (neu != null && neu != _ansichtName && !D.Ansichten.Any(a => a.Fenster == FensterSchluessel && a.Name == neu))
            {
                var alt = _ansichtName;
                Aendern("Format umbenennen", AenderungsArt.Einstellungen, d => { foreach (var a in d.Ansichten.Where(a => a.Fenster == FensterSchluessel && a.Name == alt)) a.Name = neu; });
                _ansichtName = neu;
            }
            AnsichtBoxFuellen();
        }
        else if (t == FormatLoeschen)
        {
            if (_ansichtName != "Standard") Aendern("Format löschen", AenderungsArt.Einstellungen, d => d.Ansichten.RemoveAll(a => a.Fenster == FensterSchluessel && a.Name == _ansichtName));
            AnsichtLaden("Standard");
        }
        else AnsichtLaden(t.TrimEnd('*'));
    }

    private const string FormatSpeichern = "Format speichern";
    private const string FormatSpeichernAls = "Format speichern als...";
    private const string FormatBearbeiten = "Bearbeiten";
    private const string FormatLoeschen = "Löschen";

    private void AnsichtSpeichern(string name)
    {
        var spalten = _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).Select(c => c.Name).ToList();
        Aendern("Ansicht speichern", AenderungsArt.Einstellungen, d =>
        {
            var a = d.Ansichten.FirstOrDefault(x => x.Fenster == FensterSchluessel && x.Name == name);
            if (a is null) d.Ansichten.Add(a = new Ansicht { Fenster = FensterSchluessel, Name = name });
            a.Spalten = spalten;
        });
        _ansichtName = name;
        _ansichtGeaendert = false;
        AnsichtBoxFuellen();
    }

    /// <summary>Alle Fächer auf die Standardfarben setzen (Katalogfarbe bzw. ruhige Palette).</summary>
    private void FachfarbenStandard()
    {
        if (!Ui.Frage($"Die Farben aller {D.Faecher.Count} Fächer auf die Standardfarben setzen? Eigene Farben gehen dabei verloren.")) return;
        Aendern("Fachfarben auf Standard", AenderungsArt.Stammdaten, d =>
        {
            var i = 0;
            foreach (var f in d.Faecher.OrderBy(x => x.Sortierung).ThenBy(x => x.Kurzname))
            {
                f.Hintergrund = Nrw.StandardFachfarbe(f.Kurzname, i++);
                f.Vordergrund = 0;
            }
        });
    }

    /// <summary>Farbe der markierten Elemente (ohne Markierung: aller Fächer) entfernen – das Element hat danach keine Farbe mehr.</summary>
    private void FarbenLoeschen(bool alleOhneAuswahl)
    {
        var markiert = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Element>()
            .Concat(_grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Element>()).Distinct().ToList();
        if (markiert.Count == 0 && alleOhneAuswahl)
        {
            if (!Ui.Frage($"Die Farben aller {D.Faecher.Count} Fächer löschen? Die Fächer haben danach keine Farbe mehr.")) return;
            markiert = D.Faecher.Cast<Element>().ToList();
        }
        if (markiert.Count == 0) return;
        Aendern(markiert.Count == 1 ? "Farbe gelöscht" : "Farben gelöscht", AenderungsArt.Stammdaten, _ =>
        {
            foreach (var el in markiert) { el.Hintergrund = 0; el.Vordergrund = 0; }
        });
        Neuaufbau();
    }

    private void Sortieren()
    {
        var a = D.Ansichten.FirstOrDefault(x => x.Fenster == FensterSchluessel && x.Name == _ansichtName);
        var kriterien = SortierDialog.Zeigen(this, _felder, a?.Sortierung ?? new List<Sortierkriterium>(), out var dauerhaft);
        if (kriterien is null) return;
        var name = _ansichtName;
        var spalten = _spalten.ToList();
        Aendern("Sortierung", AenderungsArt.Stammdaten, d =>
        {
            var an = d.Ansichten.FirstOrDefault(x => x.Fenster == FensterSchluessel && x.Name == name);
            if (an is null) d.Ansichten.Add(an = new Ansicht { Fenster = FensterSchluessel, Name = name, Spalten = spalten });
            an.Sortierung = kriterien;
        });
        _temporaereSortierung = null;
        _neuSortieren = true;
        if (dauerhaft)
        {
            var liste = Elemente().ToList();
            Aendern("Sortierung übernehmen", AenderungsArt.Stammdaten, _ => { for (var i = 0; i < liste.Count; i++) liste[i].Sortierung = i; });
        }
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Kommentare
    // ------------------------------------------------------------------------------------------

    private IEnumerable<Kommentar> KommentareFuer(Element e, string feld) => D.Kommentare.Where(k => k.ElementTyp == _typ && k.ElementId == e.Id && k.Feld == feld);

    private void CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].Tag is not Element el) return;
        var feld = _grid.Columns[e.ColumnIndex].Name;
        var k = KommentareFuer(el, feld).FirstOrDefault();
        if (k is null) return;
        e.Paint(e.CellBounds, DataGridViewPaintParts.All);
        var farbe = Ui.Farbe(D.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie)?.Farbe ?? 0, Color.Red);
        using var b = new SolidBrush(farbe);
        var r = e.CellBounds;
        e.Graphics!.FillPolygon(b, new[] { new Point(r.Right - 8, r.Top + 1), new Point(r.Right - 1, r.Top + 1), new Point(r.Right - 1, r.Top + 8) });
        e.Handled = true;
    }

    private void CellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].Tag is not Element el) return;
        var ks = KommentareFuer(el, _grid.Columns[e.ColumnIndex].Name).ToList();
        if (ks.Count > 0)
            e.ToolTipText = string.Join("\n\n", ks.Select(k => $"{D.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie)?.Name}\n{k.Text}\n({k.Erstellt:dd.MM.yyyy HH:mm})"));
        else if (_grid.Columns[e.ColumnIndex].Tag is Feld f && f.Hinweis.Length > 0) e.ToolTipText = f.Hinweis;
    }

    private void KommentarErstellen()
    {
        if (_grid.CurrentCell is null || _grid.CurrentRow?.Tag is not Element el) return;
        var feld = _grid.Columns[_grid.CurrentCell.ColumnIndex].Name;
        var k = KommentarDialog.Neu(this, $"Neuer Kommentar Stammdaten {Ui.TypName(_typ)} {el.Kurzname}");
        if (k is null) return;
        Aendern("Kommentar erstellen", AenderungsArt.Stammdaten, d => d.Kommentare.Add(new Kommentar { Id = d.NeueId(), ElementTyp = _typ, ElementId = el.Id, Feld = feld, Kategorie = k.Value.Kategorie, Text = k.Value.Text }));
        _grid.InvalidateCell(_grid.CurrentCell);
    }

    private void Kontextmenue(DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        if (!_grid[e.ColumnIndex, e.RowIndex].Selected) _grid.CurrentCell = _grid[e.ColumnIndex, e.RowIndex];
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Sortieren…", Symbole.Get("sortieren"), (_, _) => Sortieren());
        cm.Items.Add("Felder der Ansicht…", Symbole.Get("uebersicht"), (_, _) => FelderDialog());
        cm.Items.Add("Spalte ausblenden", null, (_, _) => SpalteAusblenden(_grid.Columns[e.ColumnIndex].Name));
        if (((Feld)_grid.Columns[e.ColumnIndex].Tag!).Art == FeldArt.Farbe || _typ == ElementTyp.Fach)
            cm.Items.Add("Farbe löschen", Symbole.Get("loeschen"), (_, _) => FarbenLoeschen(false));
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Spaltenbreite anpassen", null, (_, _) => _grid.AutoResizeColumn(e.ColumnIndex, DataGridViewAutoSizeColumnMode.AllCells));
        cm.Items.Add("Alle Spalten anpassen", null, (_, _) => _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells));
        cm.Items.Add("Aktuellen Feldinhalt als Filter setzen", Symbole.Get("filter"), (_, _) => Filtern(e.ColumnIndex, _grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? ""));
        cm.Items.Add("Filter aufheben", null, (_, _) => Filtern(-1, ""));
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Kommentar erstellen", Symbole.Get("kommentar"), (_, _) => KommentarErstellen());
        cm.Items.Add("Kommentar(e) löschen", null, (_, _) =>
        {
            if (_grid.Rows[e.RowIndex].Tag is not Element el) return;
            var feld = _grid.Columns[e.ColumnIndex].Name;
            Aendern("Kommentar löschen", AenderungsArt.Stammdaten, d => d.Kommentare.RemoveAll(k => k.ElementTyp == _typ && k.ElementId == el.Id && k.Feld == feld));
            _grid.Invalidate();
        });
        cm.Items.Add("Kommentar-Übersichtsfenster", null, (_, _) => HauptFenster.Oeffnen<KommentarUebersicht>("komm", () => new KommentarUebersicht()));
        cm.Show(_grid, _grid.PointToClient(Cursor.Position));
    }

    private void Filtern(int spalte, string wert)
    {
        // über die Filterzeile: Feldinhalt eintragen bzw. alle Filter aufheben
        _filter.Clear();
        foreach (var tb in _filterLeiste.Controls.OfType<TextBox>()) tb.Text = "";
        _filter.Clear();
        if (spalte >= 0)
        {
            if (!_filterLeiste.Visible) FilterUmschalten();
            var name = _grid.Columns[spalte].Name;
            if (_filterLeiste.Controls[name] is TextBox tb) tb.Text = wert;
            else _filter[name] = wert;
        }
        FilterAnwenden();
    }

    private void FelderDialog()
    {
        void Anwenden(FelderDerAnsichtRaster.Auswahl a)
        {
            _spaltenVorMitInhalt = null;
            _mitInhaltKnopf.Checked = false;
            _spalten = a.Aktiv;
            if (!_spalten.Contains("Kurzname")) _spalten.Insert(0, "Kurzname");
            NichtDruckenSetzen(a.NichtDrucken);
            _ansichtGeaendert = true;
            AnsichtBoxFuellen();
            Neuaufbau();
        }
        var neu = FelderDerAnsichtRaster.Zeigen(this, _felder, _spalten, NichtGedruckt(), Anwenden);
        if (neu != null) Anwenden(neu);
    }

    // ------------------------------------------------------------------------------------------
    // Druck: Spalte „drucken“ im Dialog „Felder der Ansicht“ = Druckauswahl im Seitenlayout dieser Liste
    // ------------------------------------------------------------------------------------------

    private HashSet<string> NichtGedruckt()
    {
        var layout = ListenDruck.LayoutFuer(Text);
        return _felder.Where(f => layout.Spalten.Any(s => s.Name == f.Titel && !s.Drucken)).Select(f => f.Schluessel).ToHashSet();
    }

    private void NichtDruckenSetzen(HashSet<string> nicht)
    {
        var layout = ListenDruck.LayoutFuer(Text);
        var spalten = _spalten.Select(k => _felder.FirstOrDefault(f => f.Schluessel == k)).OfType<Feld>().ToList();
        var alt = layout.Spalten.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First().Breite);
        layout.Spalten = spalten.Select(f => new ListenDruck.DruckSpalte { Name = f.Titel, Drucken = !nicht.Contains(f.Schluessel), Breite = alt.TryGetValue(f.Titel, out var b) ? b : f.Breite }).ToList();
        AppZustand.Einstellungen.Speichern();
    }

    public void Drucken(bool vorschau)
    {
        var spalten = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var zeilen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Element && r.Visible)
            .Select(r => spalten.Select(c => r.Cells[c.Index].Value switch { bool b => b ? "X" : "", var v => v?.ToString() ?? "" }).ToArray()).ToList();
        ListenDruck.Drucken(this, Text, spalten.Select(c => c.HeaderText).ToArray(), spalten.Select(c => c.Width).ToArray(), zeilen, vorschau);
    }

    // ------------------------------------------------------------------------------------------
    // Symbolleiste: Blättern, Serienänderung, Filter, Felder mit Inhalt, Farbe, Einstellungen
    // ------------------------------------------------------------------------------------------

    private void Blaettern(int richtung)
    {
        var zeilen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible && r.Tag is Element).ToList();
        if (zeilen.Count == 0) return;
        var i = zeilen.FindIndex(r => r.Tag == _aktuell);
        var ziel = zeilen[Math.Clamp(i < 0 ? 0 : i + richtung, 0, zeilen.Count - 1)];
        ZeileWaehlen((Element)ziel.Tag!);
    }

    /// <summary>Serienänderung (Handbuch 2.3.3.4): in der aktuellen Spalte alle Felder mit „Alter Wert“ auf „Neuer Wert“ setzen.</summary>
    private void Serienaenderung()
    {
        if (_grid.CurrentCell is null || _grid.Columns[_grid.CurrentCell.ColumnIndex].Tag is not Feld f) return;
        if (!f.Editierbar || f.Schluessel == "Kurzname") { Ui.Info($"Das Feld \"{f.Titel}\" kann nicht per Serienänderung geändert werden."); return; }
        var alt = _aktuell is null ? "" : f.Anzeige(_aktuell, Ix);
        var ein = SerienaenderungDialog.Zeigen(this, f.Titel, alt);
        if (ein is null) return;
        object? wert;
        try { wert = f.Parse(f.Art == FeldArt.Bool ? (ein.NeuerWert.Length > 0 ? "X" : "") : ein.NeuerWert, D); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { Ui.Fehler(new FormatException(ex.Message)); return; }
        var kandidaten = ein.GanzeDatei ? D.AlleElemente(_typ).ToList() : _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Select(r => r.Tag).OfType<Element>().ToList();
        var altText = f.Art == FeldArt.Bool ? (ein.AlterWert.Length > 0 ? "X" : "") : ein.AlterWert;
        var ziele = kandidaten.Where(e => (!ein.NurMarkierte || e.Markiert) && string.Equals(f.Anzeige(e, Ix), altText, StringComparison.OrdinalIgnoreCase)).ToList();
        if (ziele.Count == 0) { Ui.Info("Kein Element mit diesem Wert gefunden."); return; }
        if (Aendern($"Serienänderung {f.Titel}", AenderungsArt.Stammdaten, _ => { foreach (var e in ziele) f.Set!(e, wert); })) Neuaufbau();
    }

    private void FilterUmschalten()
    {
        _filterLeiste.Visible = !_filterLeiste.Visible;
        _filterKnopf.Checked = _filterLeiste.Visible;
        if (!_filterLeiste.Visible) _filter.Clear();
        FilterLeisteAufbauen();
        FilterAnwenden();
        if (_filterLeiste.Visible && _grid.CurrentCell is { } c && _filterLeiste.Controls[_grid.Columns[c.ColumnIndex].Name] is TextBox tb) tb.Focus();
    }

    /// <summary>Eingabefelder der Filterzeile (je Spalte eines, mit dem Filtersymbol über den Zeilenköpfen).</summary>
    private void FilterLeisteAufbauen()
    {
        if (!_filterLeiste.Visible) return;
        var namen = _grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).ToHashSet();
        foreach (var tb in _filterLeiste.Controls.OfType<TextBox>().Where(t => !namen.Contains(t.Name)).ToList()) { _filterLeiste.Controls.Remove(tb); tb.Dispose(); }
        if (_filterLeiste.Controls["FilterSymbol"] is null)
            _filterLeiste.Controls.Add(new PictureBox { Name = "FilterSymbol", Image = Symbole.Get("filter", 16), SizeMode = PictureBoxSizeMode.CenterImage, Left = 0, Top = 0, Height = 22 });
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            if (_filterLeiste.Controls[c.Name] is TextBox) continue;
            var tb = new TextBox { Name = c.Name, BorderStyle = BorderStyle.FixedSingle, Text = _filter.GetValueOrDefault(c.Name, ""), Font = Ui.Schrift };
            var name = c.Name;
            tb.TextChanged += (_, _) => { _filter[name] = tb.Text; FilterAnwenden(); };
            _tip.SetToolTip(tb, "Filter: Platzhalter ? (ein Zeichen) und * (beliebig viele Zeichen), mehrere Werte mit | (oder)");
            _filterLeiste.Controls.Add(tb);
        }
        FilterLeisteAnordnen();
    }

    private void FilterLeisteAnordnen()
    {
        if (!_filterLeiste.Visible) return;
        if (_filterLeiste.Controls["FilterSymbol"] is Control sym) sym.Width = _grid.RowHeadersVisible ? _grid.RowHeadersWidth : 0;
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            if (_filterLeiste.Controls[c.Name] is not TextBox tb) continue;
            var r = _grid.GetColumnDisplayRectangle(c.Index, false);
            tb.Visible = c.Visible && r.Width > 0;
            if (tb.Visible) tb.SetBounds(r.Left, 0, r.Width, _filterLeiste.Height);
        }
    }

    /// <summary>Zeilen nach den Einträgen der Filterzeile ausblenden (UND über die Spalten; ? und * als Platzhalter, | = oder).</summary>
    private void FilterAnwenden()
    {
        var aktiv = _filter.Where(x => x.Value.Trim().Length > 0 && _grid.Columns.Contains(x.Key)).ToList();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.Tag is not Element) continue;
            var sichtbar = aktiv.All(x => FilterPasst(r.Cells[x.Key].Value switch { bool b => b ? "x" : "", var v => v?.ToString() ?? "" }, x.Value));
            if (r.Visible == sichtbar) continue;
            if (!sichtbar && _grid.CurrentRow == r)
            {
                try { _grid.CurrentCell = null; }
                catch (InvalidOperationException) { continue; }
            }
            r.Visible = sichtbar;
        }
    }

    /// <summary>Filterregel: Platzhalter ? und *, Alternativen mit |; bei Listen („LANG,EGB“) genügt ein passender Eintrag.</summary>
    internal static bool FilterPasst(string wert, string filter)
    {
        foreach (var teil in filter.Split('|'))
        {
            var t = teil.Trim();
            if (t.Length == 0) continue;
            var muster = "^" + System.Text.RegularExpressions.Regex.Escape(t).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var re = new System.Text.RegularExpressions.Regex(muster, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (re.IsMatch(wert) || wert.Split(',').Any(x => re.IsMatch(x.Trim()))) return true;
        }
        return false;
    }

    /// <summary>„Felder mit Inhalt“: vorübergehend alle Spalten mit mindestens einem Eintrag; zweiter Klick stellt den Originalzustand wieder her.</summary>
    private void FelderMitInhalt()
    {
        if (_spaltenVorMitInhalt != null)
        {
            _spalten = _spaltenVorMitInhalt;
            _spaltenVorMitInhalt = null;
        }
        else
        {
            _spaltenVorMitInhalt = _spalten.ToList();
            var elemente = D.AlleElemente(_typ).ToList();
            _spalten = _felder.Where(f => f.Schluessel == "Kurzname" || elemente.Any(e => f.Anzeige(e, Ix).Length > 0)).Select(f => f.Schluessel).ToList();
        }
        _mitInhaltKnopf.Checked = _spaltenVorMitInhalt != null;
        Neuaufbau();
    }

    /// <summary>Automatische temporäre Sortierung: Klick in die Überschrift sortiert aufsteigend, ein weiterer Klick absteigend.</summary>
    private void SpaltenkopfKlick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.ColumnIndex < 0 || (ModifierKeys & Keys.Control) != 0) return;
        var key = _grid.Columns[e.ColumnIndex].Name;
        _temporaereSortierung = _temporaereSortierung is { } t && t.Feld == key ? (key, !t.Absteigend) : (key, false);
        _neuSortieren = true;
        Neuaufbau();
    }

    /// <summary>Farbe (Handbuch 2.3.1): Vorder- und Hintergrundfarbe für das aktive bzw. alle markierten Elemente.</summary>
    private void FarbeZuweisen()
    {
        var markiert = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Element>().Distinct().ToList();
        if (markiert.Count == 0 && _aktuell != null) markiert.Add(_aktuell);
        if (markiert.Count == 0) return;
        var e0 = markiert[0];
        var farbe = ElementFarbeDialog.Zeigen(this, markiert.Count == 1 ? e0.Kurzname : $"{markiert.Count} Elemente", e0.Vordergrund, e0.Hintergrund);
        if (farbe is not { } f) return;
        if (Aendern(markiert.Count == 1 ? "Farbe geändert" : "Farben geändert", AenderungsArt.Stammdaten, _ => { foreach (var el in markiert) { el.Vordergrund = f.Vorder; el.Hintergrund = f.Hinter; } }))
            Neuaufbau();
    }

    /// <summary>Einstellungen (Handbuch 2.3.1): Schriftart und -größe der Tabelle.</summary>
    private void SchriftEinstellen()
    {
        using var fd = new FontDialog { Font = _grid.DefaultCellStyle.Font ?? _grid.Font, ShowEffects = false };
        if (fd.ShowDialog(this) != DialogResult.OK) return;
        _grid.DefaultCellStyle.Font = fd.Font;
        _grid.RowTemplate.Height = Math.Max(18, fd.Font.Height + 6);
        foreach (DataGridViewRow r in _grid.Rows) r.Height = _grid.RowTemplate.Height;
    }
}
