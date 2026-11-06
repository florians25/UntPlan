using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Absenzen / Lehrer | Klassen | Räume (Reiter mit Anzahl wie im Vorbild). Direkte Eingabe in der Liste – Änderungen einer Zeile
/// werden beim Verlassen der Zeile (auch beim Reiter- oder Datumswechsel, beim Verlassen und Schließen des Fensters) übernommen.
/// Überlappt eine Lehrer-Absenz mit einer Veranstaltung, wird gefragt, ob die Lehrkraft aus der Veranstaltung entfernt werden soll.
/// </summary>
public sealed class AbwesenheitenFenster : MdiFenster
{
    private static readonly string[] Reiter = { "Lehrer", "Klassen", "Räume" };
    private readonly Steuerelemente.Reiterleiste _tabs = new();
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly Steuerelemente.ZeitraumAuswahl _zeitraum;
    private readonly ToolStripMenuItem _vaZeigen = new("Veranstaltungen anzeigen") { CheckOnClick = true };
    private int _letzterReiter;
    private bool _laedt;
    private bool _neuaufbauAusstehend;
    private readonly Label _tage = new() { AutoSize = true };

    // Entwurf der gerade bearbeiteten Zeile: Änderungen werden erst beim Verlassen der Zeile übernommen,
    // damit z. B. eine stundenweise Absenz nicht schon nach der ersten Eingabe Vertretungen auslöst.
    private Abwesenheit? _entwurf;
    private Abwesenheit? _entwurfAlt;
    private int _entwurfZeile = -1;
    private bool _entwurfBisGesetzt;
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
        Text = "Absenzen / Standard";
        HilfeThema = "Abwesenheiten";
        Icon = SymbolIcon("abwesenheit");
        Size = new Size(780, 320);
        Positionieren();
        foreach (var t in Reiter) _tabs.Hinzufuegen(t + " (0)");
        // Reiterwechsel: offenen Entwurf zuerst übernehmen (früher blieb die alte Liste unter dem neuen Reiter stehen)
        _tabs.Selecting += (_, _) => EntwurfSofortUebernehmen();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == _letzterReiter) return;
            _letzterReiter = _tabs.SelectedIndex;
            Neuaufbau();
        };
        // Symbolleiste wie im Vorbild (Handbuch Kap. 5.1, Bereich 1): Fensterverknüpfung, Neu, Löschen | Felder der Ansicht,
        // Einstellungen, Absenz splitten – dazu die eigenen Funktionen (Bearbeiten, Kopieren auf andere Elemente, Veranstaltungen)
        var ts = Werkzeugleiste();
        ts.Items.Add(VerknuepfenKnopf());
        ts.Items.Add(Knopf("Neue Absenz (Dialog)", "neu", Neu));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Felder der Ansicht", "rollup", () => Steuerelemente.TabellenFormate.MenueZeigen(_g, Cursor.Position)));
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
        ts.Items.Add(einst);
        ts.Items.Add(Knopf("Absenz splitten", "tausch", Teilen));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Absenz kopieren (andere Elemente)", "kopieren", Kopieren));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Veranstaltungen", "veranstaltung", () => HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster())));

        Ui.RasterGestalten(_g, true);
        // wie im Vorbild: keine Zeilenköpfe, vertiefter Rahmen unter den Reitern
        _g.RowHeadersVisible = false;
        _g.BorderStyle = BorderStyle.Fixed3D;
        Ui.ZeilenmarkeErsteSpalte(_g);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nr", HeaderText = "AbsNr.", Width = 55, ReadOnly = true });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("E", "Lehrer", 80, Array.Empty<string>()));
        foreach (var (n, k, b) in new[] { ("V", "Von TT.MM.", 108), ("SV", "ab St.", 45), ("B", "Bis TT.MM.", 108), ("SB", "bis St.", 45) })
            _g.Columns.Add(n is "V" or "B" ? Steuerelemente.DatumSpalte.Neu(n, k, b) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("G", "Grund", 70, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Beg", HeaderText = "Begleitlehrer", Width = 100 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "O", HeaderText = "offen", Width = 45, ReadOnly = true });
        // wie im Vorbild: Anrechnung, Entfalltage und Absenztage (Unterrichtstage im Zeitraum)
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Anr", HeaderText = "Anrechnung", Width = 80, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ent", HeaderText = "Entfalltage", Width = 75, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Abs", HeaderText = "Absenztage", Width = 75, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        // Reihenfolge wie im Vorbild: … Grund, Text, Anrechnung, Entfalltage, Absenztage, dann die weiteren Spalten
        var nachText = _g.Columns["T"]!.DisplayIndex;
        _g.Columns["Anr"]!.DisplayIndex = nachText + 1;
        _g.Columns["Ent"]!.DisplayIndex = nachText + 2;
        _g.Columns["Abs"]!.DisplayIndex = nachText + 3;
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        Steuerelemente.SuchKombi.Aktivieren(_g, (zelle, text) =>
        {
            Element? el = zelle.OwningColumn.Name == "G" ? D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname == text) : D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname == text);
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el is Lehrer l ? l.VollerName : el.Langname);
        });
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellBeginEdit += (_, e) => { if (e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is Veranstaltung) e.Cancel = true; };
        _g.RowValidated += (_, e) => { if (e.RowIndex == _entwurfZeile) EntwurfUebernehmen(); };
        // Entwurf nicht verlieren: beim Verlassen des Fensters und beim Schließen sofort (nicht verzögert) speichern
        if (AppZustand.Haupt != null)
        {
            void Gewechselt(object? o, EventArgs e) { if (!IsDisposed && AppZustand.Haupt.ActiveMdiChild != this) EntwurfSofortUebernehmen(); }
            AppZustand.Haupt.MdiChildActivate += Gewechselt;
            FormClosed += (_, _) => AppZustand.Haupt.MdiChildActivate -= Gewechselt;
        }
        FormClosing += (_, _) => EntwurfSofortUebernehmen();
        _g.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
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
            if (e.KeyCode == Keys.Enter && _entwurf != null && e.Modifiers == Keys.Control) { EntwurfSofortUebernehmen(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.Control) { Loeschen(); e.Handled = true; }
            if (e.KeyData == Keys.Insert) { Neu(); e.Handled = true; }
            // wie im Vorbild (Kap. 5.2 Absenzen kopieren): Strg+C merkt die markierten Absenzen, Strg+V fügt sie am eingestellten Tag ein
            // zusätzlich wie in jeder Tabelle: Strg+C kopiert die Zellen als Text; kam danach anderer Text in die Zwischenablage,
            // fügt Strg+V diesen in die Felder ein (Tabellenbedienung)
            if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control) { AbsenzenMerken(); _ablageText = Steuerelemente.Tabellenbedienung.DatensaetzeKopieren(_g); e.Handled = true; }
            if (e.KeyCode == Keys.V && e.Modifiers == Keys.Control && _zwischenablage.Count > 0 && Steuerelemente.Tabellenbedienung.DatensatzEinfuegen(_ablageText)) { AbsenzenEinfuegen(); e.Handled = true; }
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
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 6, 0, 2), BackColor = Ui.Kopf, WrapContents = false };
        var gruppe = new GroupBox { Text = "Von-Bis", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), Margin = Padding.Empty };
        gruppe.Controls.Add(_zeitraum);
        _zeitraum.Location = new Point(9, 17);
        kopf.Controls.Add(gruppe);
        var rechts = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(7, 8, 0, 0), Padding = Padding.Empty, BackColor = Color.Transparent };
        _zeitraum.Kalender.Margin = Padding.Empty;
        rechts.Controls.Add(_zeitraum.Kalender);
        _tage.Margin = new Padding(0, 3, 0, 0);
        rechts.Controls.Add(_tage);
        kopf.Controls.Add(rechts);

        Controls.Add(_g);
        Controls.Add(_tabs);
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
        eintraege.AddRange(D.Abwesenheiten.Where(a => a.Art == Art && a.Bis >= von && a.Von <= bis).OrderBy(a => Ix.K(typ, a.ElementId), StringComparer.Create(Format.De, true)).ThenBy(a => a.Von));
        // Veranstaltungen zur Information (Einstellungen „Veranstaltungen anzeigen“)
        if (_vaZeigen.Checked)
            eintraege.AddRange(D.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= bis && VeranstaltungsElemente(v).Length > 0).OrderBy(v => v.Von));
        eintraege.Add(EingabeZeile);
        if (_zuMarkierungSpringen && _markiert is { } mk && eintraege.OfType<Abwesenheit>().FirstOrDefault(ab => ab.ElementId == mk.Id && mk.Typ == typ) is { } ziel)
            springe = "A" + ziel.Id;
        _zuMarkierungSpringen = false;
        try { Steuerelemente.TabellenAbgleich.Anwenden(_g, eintraege, Schluessel, Schluessel, ZeileSetzen, aktuell: springe); }
        finally { _laedt = false; }
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
                var offen = D.Vertretungen.Count(v => v.AbwesenheitId == a.Id && v.OffenOderRaumOffen);
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
                // leere Eingabezeile
                werte = new object?[] { "", "", "", "", "", "", "", "", "", "", "", "", "" };
                hinter = Color.FromArgb(255, 228, 190);
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

    private List<Abwesenheit> Markierte() => _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Abwesenheit>().Distinct().ToList();

    /// <summary>Direkte Eingabe: geänderte Zelle in den Entwurf der Zeile übernehmen (gespeichert wird beim Zeilenwechsel).</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        if (zeile.Tag is Veranstaltung) return;
        var wert = zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var alt = zeile.Tag as Abwesenheit;
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
                    a.Von = Ui.DatumLesen(wert, a.Von);
                    // Bis nur mitziehen, solange es nicht selbst eingegeben wurde (früher ging ein zuerst eingegebenes Bis verloren)
                    if (a.Bis < a.Von || !_entwurfBisGesetzt) a.Bis = a.Von;
                    break;
                case "B":
                    a.Bis = Ui.DatumLesen(wert, a.Bis);
                    _entwurfBisGesetzt = true;
                    if (a.Bis < a.Von) a.Von = a.Bis;
                    break;
                case "SV": a.StundeVon = wert.Length == 0 ? 1 : Math.Clamp(int.Parse(wert), 1, LetzteStunde); break;
                case "SB": a.StundeBis = wert.Length == 0 ? LetzteStunde : Math.Clamp(int.Parse(wert), 1, LetzteStunde); break;
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
        // Meldungen erst nach dem Übernehmen in den Entwurf zeigen
        if (meldung != null) Ui.Info(meldung);
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
        _entwurf = null;
        _entwurfAlt = null;
        _entwurfZeile = -1;
        if (a.ElementId == 0)
        {
            // ohne Element keine Absenz – Eingaben verwerfen
            if (alt != null) Ui.Info("Ohne Element kann die Absenz nicht gespeichert werden – die Änderung wurde verworfen.");
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
        if (sofort || !IsHandleCreated || IsDisposed) Speichern(new[] { a }, alt is null ? Array.Empty<Abwesenheit>() : new[] { alt }, text);
        else NachZellwechsel(() => Speichern(new[] { a }, alt is null ? Array.Empty<Abwesenheit>() : new[] { alt }, text));
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
        var text = Ui.Eingabe($"Absenz {Format.Datum(alt.Von)} – {Format.Datum(alt.Bis)} splitten: zweiter Teil beginnt am (TT.MM.JJJJ):", "Absenz splitten", Format.Datum(alt.Von.AddDays(1)));
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
    /// Kontextmenü wie im Vorbild (Handbuch Kap. 4.5): Felder der Ansicht, Spaltenbreite anpassen, Alle Spalten anpassen, Alle Spalten an
    /// Inhalt anpassen, Zeilenumbruch, Kommentar erstellen/bearbeiten/löschen, Kommentar Übersichtsfenster – davor die Bearbeitungsfunktionen.
    /// </summary>
    private ContextMenuStrip Kontextmenue(int zeile, int spalte)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Closed += (_, _) => BeginInvoke(cm.Dispose);
        cm.Items.Add("Neue Absenz…", Symbole.Get("neu"), (_, _) => Neu());
        cm.Items.Add("Bearbeiten…", Symbole.Get("einstellungen"), (_, _) => Bearbeiten());
        cm.Items.Add("Absenz splitten…", Symbole.Get("tausch"), (_, _) => Teilen());
        cm.Items.Add("Absenz kopieren…", null, (_, _) => Kopieren());
        cm.Items.Add("Absenzen zusammenfassen", null, (_, _) => Zusammenfassen());
        cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Felder der Ansicht", null, (_, _) => Steuerelemente.TabellenFormate.MenueZeigen(_g, Cursor.Position));
        var col = _g.Columns[spalte];
        cm.Items.Add("Spaltenbreite anpassen", null, (_, _) => col.Width = Math.Max(col.MinimumWidth, col.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true)));
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten anpassen", null, (_, _) => SpaltenAnpassen(DataGridViewAutoSizeColumnMode.AllCells)) { ShortcutKeyDisplayString = "Strg+E" });
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten an Inhalt anpassen", null, (_, _) => SpaltenAnpassen(DataGridViewAutoSizeColumnMode.AllCellsExceptHeader)) { ShortcutKeyDisplayString = "Strg+F" });
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

    private void Speichern(IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt, string text)
    {
        if (neu.Count == 1 && Anschluss(neu[0], alt) is { } andere)
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
        // Hinweis: eingeplante Vertreter, die durch die neue Absenz abwesend werden
        var betroffen = neu.SelectMany(a => Vertretungen.BetroffeneVertreter(D, a)).Distinct().ToList();
        List<Abwesenheit> gespeichert = new();
        if (!Aendern(text, AenderungsArt.Vertretung, d => gespeichert = AbsenzVerwaltung.Speichern(d, neu, alt))) return;
        if (gespeichert.Count == 1) _springeZu = gespeichert[0].Id;
        if (neu.Count > 0 && gespeichert.Count == 0 && alt.Count == 0) Ui.Info("Diese Absenz ist bereits vorhanden.");
        if (betroffen.Count > 0)
            Ui.Info("Achtung: Folgende Vertretungen waren bereits mit einer Lehrkraft geplant, die nun abwesend ist. " +
                "Sie sind wieder offen und müssen neu vertreten werden:\n\n" +
                string.Join("\n", betroffen.Take(20).Select(v => $"Vtr-Nr. {v.Id}: {Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7]} {Format.Datum(v.Datum)}, {v.Stunde}. Std., {Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)} – Vertreter {Ix.KLehrer(v.VertreterId)}")) +
                (betroffen.Count > 20 ? $"\n… und {betroffen.Count - 20} weitere" : ""), "Vertreter abwesend");
        VeranstaltungenPruefen(this, gespeichert, (t, a) => Aendern(t, AenderungsArt.Vertretung, a));
        // Lehrkraft wird abwesend: Verlegungen und Sondereinsätze im Absenzzeitraum rückgängig machen bzw. löschen?
        VerlegungsAbfrage.NachAbsenz(gespeichert, Aendern);
        Neuaufbau();
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
        if (!Ui.LoeschenBestaetigen(liste.Count, "Absenz", "Absenzen", zusatz: "Daraus entstandene Vertretungen werden entfernt.")) return;
        Speichern(Array.Empty<Abwesenheit>(), liste, "Absenz löschen");
    }

    // ----------------------------------------------------------------------------------------------
    // Selbsttest (Handbuch-Abläufe, Kurzeinführung Kap. 3.2)
    // ----------------------------------------------------------------------------------------------

    /// <summary>Selbsttest: Reiter Lehrer (0), Klassen (1), Räume (2) wählen.</summary>
    internal void SelbsttestReiter(int i) => _tabs.SelectedIndex = i;

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
