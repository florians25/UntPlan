using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Planungsdialog im Vertretungsmodus (Handbuch Vertretungsplanung Kap. 9 und 10.2): oben das Auswahlfenster (Karteikarten „Klasse(n)“ /
/// „Lehrer“, Element mit Name, Woche-1 und Woche-2), in der Mitte die Belegung des Elements in zwei Wochen (Datum zeilenweise, Stunden
/// spaltenweise bzw. umgekehrt), unten die Lupe, in der der Unterricht der gewählten Stunde geändert und in einer freien Stunde ein
/// Sondereinsatz angelegt werden kann. Stunden werden per Drag &amp; Drop verlegt bzw. getauscht (auch wochenübergreifend), Sondereinsätze
/// verlegt bzw. mit gedrückter Strg-Taste kopiert, Stunden in die Lupe gezogen entfallen; offene Vertretungen zeigen mögliche Vorziehungen.
/// </summary>
public sealed class VertretungsPlanungsdialogFenster : MdiFenster
{
    private readonly TabControl _ansicht = new() { Dock = DockStyle.Top, Height = 22 };
    private readonly ComboBox _element = new() { Left = 6, Top = 6, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _name = new() { Left = 166, Top = 9, Width = 260, Text = "Name" };
    private readonly DateTimePicker _woche1 = new() { Left = 6, Top = 34, Width = 110, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _woche2 = new() { Left = 188, Top = 34, Width = 110, Format = DateTimePickerFormat.Short };
    private readonly PlanungsRaster _raster = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _lupe = new() { Dock = DockStyle.Bottom, Height = 96, Name = "Lupe" };
    private readonly ToolStripButton _beibehalten;
    private ElementTyp _typ = ElementTyp.Klasse;
    private int _id;
    private bool _laden;
    private readonly List<DateOnly> _tage = new();
    private readonly Dictionary<DateOnly, Tagesplan> _plaene = new();
    private readonly Dictionary<(string, DateOnly, int), Feldstatus> _zielCache = new();
    private Dictionary<(DateOnly, int), bool>? _vorziehungen;

    private static PlanungsdialogEinstellungen E => AppZustand.Einstellungen.Planungsdialog;

    /// <summary>Planungsdialog der Vertretungsplanung öffnen bzw. aktivieren.</summary>
    public static VertretungsPlanungsdialogFenster Zeigen()
    {
        var f = HauptFenster.Oeffnen("vplanungsdialog", () => new VertretungsPlanungsdialogFenster());
        if (AppZustand.AktiveVertretung is { } v) f.VertretungGewaehlt(v);
        return f;
    }

    public VertretungsPlanungsdialogFenster()
    {
        Text = "Planungsdialog";
        HilfeThema = "Planungsdialog";
        Icon = SymbolIcon("planen");
        Size = new Size(560, 600);
        Positionieren();

        // Symbolleiste (Handbuch Vertretungsplanung Kap. 9/10.2)
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Entfall", "entfall", Entfall));
        ts.Items.Add(Knopf("Vertretungsvorschlag", "vertretung", Vorschlag));
        ts.Items.Add(Knopf("Vormerkung", "vormerkung", Vormerken));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Sondereinsatz", "sondereinsatz", Sondereinsatz));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Raum zuordnen / löschen", "raumzuordnen", RaumZuordnen));
        _beibehalten = new ToolStripButton(Symbole.Get("schloss")) { ToolTipText = "Aktives Element beibehalten", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        ts.Items.Add(_beibehalten);
        ts.Items.Add(Knopf("Stunde ändern", "lehrer", StundeAendern));
        ts.Items.Add(Knopf("Stunde fixieren", "fixieren", Fixieren));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Einstellungen", "einstellungen", EinstellungenZeigen));

        // Auswahlfenster
        _ansicht.TabPages.Add(new TabPage("Klasse(n)") { Tag = ElementTyp.Klasse });
        _ansicht.TabPages.Add(new TabPage("Lehrer") { Tag = ElementTyp.Lehrer });
        _ansicht.SelectedIndexChanged += (_, _) =>
        {
            if (_laden || _ansicht.SelectedTab?.Tag is not ElementTyp t) return;
            // bisherige Stunde: Element der anderen Sicht übernehmen
            var e = AuswahlEintraege().FirstOrDefault();
            _typ = t;
            _id = e?.Unterricht is { } u ? (t == ElementTyp.Lehrer ? e.LehrerIds.FirstOrDefault(u.LehrerIds.Contains) : e.KlassenIds.FirstOrDefault()) : 0;
            if (_id == 0) _id = Stammdaten.Sortiert(D, t).FirstOrDefault()?.Id ?? 0;
            ElementlisteFuellen();
            Neuaufbau();
        };
        var auswahl = new Panel { Dock = DockStyle.Top, Height = 62 };
        var dreh = new Drehknopf("Nächstes / vorheriges Element") { Left = 138, Top = 6 };
        dreh.Gedreht += r => Blaettern(r);
        auswahl.Controls.AddRange(new Control[] { _element, dreh, _name, _woche1, _woche2 });
        var d1 = new Drehknopf("Woche vor / zurück") { Left = 118, Top = 34 };
        d1.Gedreht += r => _woche1.Value = _woche1.Value.AddDays(7 * r);
        var d2 = new Drehknopf("Woche vor / zurück") { Left = 300, Top = 34 };
        d2.Gedreht += r => _woche2.Value = _woche2.Value.AddDays(7 * r);
        auswahl.Controls.AddRange(new Control[] { d1, new Label { Text = "Woche-1", Left = 136, Top = 38, AutoSize = true }, d2, new Label { Text = "Woche-2", Left = 318, Top = 38, AutoSize = true } });
        var montag = Kalender.Montag(AppZustand.Datum);
        _woche1.Value = montag.ToDateTime(TimeOnly.MinValue);
        _woche2.Value = montag.AddDays(7).ToDateTime(TimeOnly.MinValue);
        _woche1.ValueChanged += (_, _) => { if (!_laden) { _laden = true; _woche2.Value = _woche1.Value.AddDays(7); _laden = false; Neuaufbau(); } };
        _woche2.ValueChanged += (_, _) => { if (!_laden) Neuaufbau(); };
        _element.SelectionChangeCommitted += (_, _) => { if (_element.SelectedItem is Element e) ElementSetzen(e.Id); };
        _element.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            if (Stammdaten.Sortiert(D, _typ).FirstOrDefault(x => string.Equals(x.Kurzname, _element.Text.Trim(), StringComparison.OrdinalIgnoreCase)) is { } el) ElementSetzen(el.Id);
            e.Handled = e.SuppressKeyPress = true;
        };

        // Stundenplanfenster
        _raster.Baumbreite = 0;
        _raster.ZweiKopfzeilen = false;
        _raster.ZelleLiefern = ZelleLiefern;
        _raster.ZielLiefern = ZielLiefern;
        _raster.CursorGeaendert += () => { LupeFuellen(); AuswahlMelden(); };
        _raster.ZiehenBeginnt += ZiehenBeginnt;
        _raster.Abgelegt += Abgelegt;
        _raster.Doppelklick += (_, _) => StundeAendern();
        _raster.Kontext += Kontextmenue;
        _raster.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { Entfall(); e.Handled = true; }
            else if (e.KeyCode == Keys.R && e.Control) { RaumZuordnen(); e.Handled = true; }
            else if (e.KeyCode == Keys.F7) { Fixieren(); e.Handled = true; }
        };

        // Lupe: Details und Änderung des Unterrichts, Sondereinsatz in der ersten Zeile, Stunde hierher ziehen = Entfall
        LupeAufbauen();

        Controls.Add(_raster);
        Controls.Add(_lupe);
        Controls.Add(auswahl);
        Controls.Add(_ansicht);
        Controls.Add(ts);
        _id = Stammdaten.Sortiert(D, _typ).FirstOrDefault()?.Id ?? 0;
        ElementlisteFuellen();
        Neuaufbau();
        AppZustand.VertretungGewaehlt += VertretungGewaehlt;
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.VertretungGewaehlt -= VertretungGewaehlt;
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
        };
    }

    // ------------------------------------------------------------------------------------------
    // Aufbau
    // ------------------------------------------------------------------------------------------

    private void ElementlisteFuellen()
    {
        _laden = true;
        _element.Items.Clear();
        foreach (var e in Stammdaten.Sortiert(D, _typ)) _element.Items.Add(e);
        _element.DisplayMember = nameof(Element.Kurzname);
        _element.SelectedItem = _element.Items.Cast<Element>().FirstOrDefault(e => e.Id == _id);
        _ansicht.SelectedIndex = _typ == ElementTyp.Lehrer ? 1 : 0;
        _laden = false;
    }

    private void ElementSetzen(int id)
    {
        _id = id;
        _laden = true;
        _element.SelectedItem = _element.Items.Cast<Element>().FirstOrDefault(e => e.Id == _id);
        _laden = false;
        Neuaufbau();
        if (Verknuepft) AppZustand.Waehlen(_typ, _id, this);
    }

    private void Blaettern(int r)
    {
        if (_element.Items.Count == 0) return;
        var i = Math.Max(0, _element.SelectedIndex);
        if (_element.Items[(i + r + _element.Items.Count) % _element.Items.Count] is Element el) ElementSetzen(el.Id);
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art is AenderungsArt.Alles or AenderungsArt.Stammdaten) ElementlisteFuellen();
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    private bool DatumZeilen => E.VertretungDatumZeilen;

    private void Neuaufbau()
    {
        if (IsDisposed) return;
        var cursor = AktuelleZelle();
        _plaene.Clear();
        _zielCache.Clear();
        _tage.Clear();
        var w1 = Kalender.Montag(DateOnly.FromDateTime(_woche1.Value));
        var w2 = Kalender.Montag(DateOnly.FromDateTime(_woche2.Value));
        for (var i = 0; i < 7; i++) _tage.Add(w1.AddDays(i));
        for (var i = 0; i < 7; i++) _tage.Add(w2.AddDays(i));
        var el = Ix.Element(_typ, _id);
        _name.Text = el is null ? "Name" : el.Langname.Length > 0 ? el.Langname : "Name";
        // Stundenspalten bzw. Datumszeilen
        _raster.Spalten.Clear();
        _raster.Zeilen.Clear();
        var stunden = Enumerable.Range(1, D.Zeitraster.StundenProTag).ToList();
        if (DatumZeilen)
        {
            foreach (var s in stunden) _raster.Spalten.Add(new RasterSpalte("", (s + D.Zeitraster.ErsteStundennummer - 1).ToString(), s));
            foreach (var t in _tage) _raster.Zeilen.Add(new RasterZeile { Text = DatumText(t), Farbe = Color.Black, Hintergrund = TagFarbe(t) });
        }
        else
        {
            foreach (var t in _tage) _raster.Spalten.Add(new RasterSpalte("", DatumText(t), t));
            foreach (var s in stunden) _raster.Zeilen.Add(new RasterZeile { Text = (s + D.Zeitraster.ErsteStundennummer - 1).ToString(), Farbe = Color.Black });
        }
        var f = E.Schrift();
        _raster.Font = f;
        var basis = TextRenderer.MeasureText("Gauss.", f);
        var datumBreite = TextRenderer.MeasureText("30.12./Mo", f).Width;
        _raster.Spaltenbreite = DatumZeilen ? Math.Max(24, basis.Width * Math.Clamp(E.VertretungSpaltenbreite, 200, 2000) / 600) : Math.Max(56, datumBreite + 6);
        _raster.Zeilenhoehe = Math.Max(16, basis.Height + 3);
        _raster.Namensbreite = DatumZeilen ? datumBreite + 10 : 30;
        VorziehungenBerechnen();
        _raster.Neu();
        _stumm = true;
        try { if (cursor is { } c) ZelleWaehlen(c.Datum, c.Stunde, false); }
        finally { _stumm = false; }
        LupeFuellen();
    }

    private static string DatumText(DateOnly t) => $"{t.Day}.{t.Month}./{Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]}";

    private Color? TagFarbe(DateOnly t) => Kalender.IstUnterrichtstag(D, t) ? null : Color.FromArgb(205, 205, 205);

    private Tagesplan Plan(DateOnly t)
    {
        if (!_plaene.TryGetValue(t, out var tp)) _plaene[t] = tp = Tagesplan.Berechnen(D, t);
        return tp;
    }

    private (DateOnly Datum, int Stunde) Position(int z, int s) => DatumZeilen ? (_tage[z], s + 1) : (_tage[s], z + 1);

    private (DateOnly Datum, int Stunde)? AktuelleZelle()
    {
        if (_tage.Count == 0 || _raster.Zeilen.Count == 0) return null;
        var (z, s) = _raster.Zellcursor;
        return z < _raster.Zeilen.Count && s < _raster.Spalten.Count ? Position(z, s) : null;
    }

    private void ZelleWaehlen(DateOnly datum, int stunde, bool melden = true)
    {
        var i = _tage.IndexOf(datum);
        if (i < 0 || stunde < 1) return;
        if (DatumZeilen) _raster.CursorSetzen(i, stunde - 1);
        else _raster.CursorSetzen(stunde - 1, i);
        if (melden) AuswahlMelden();
    }

    /// <summary>Einträge des angezeigten Elements in einer Stunde (stattfindend und entfallen).</summary>
    private List<TagesEintrag> Eintraege(DateOnly t, int stunde)
    {
        if (!Kalender.IstUnterrichtstag(D, t)) return new();
        return Plan(t).Stunde(stunde).Where(e => _typ == ElementTyp.Lehrer
            ? e.LehrerIds.Contains(_id) || (!e.Findetstatt && (e.Regel?.LehrerId ?? e.Vertretung?.LehrerId) == _id) || (e.Status == TagesStatus.Offen && e.Vertretung?.LehrerId == _id)
            : e.KlassenIds.Contains(_id)).ToList();
    }

    private List<TagesEintrag> AuswahlEintraege() => AktuelleZelle() is { } c ? Eintraege(c.Datum, c.Stunde) : new();

    // ------------------------------------------------------------------------------------------
    // Zellen
    // ------------------------------------------------------------------------------------------

    private static readonly Color Offen = Color.FromArgb(255, 40, 40);
    private static readonly Color Geaendert = Color.FromArgb(200, 225, 255);
    private static readonly Color Gruen = Color.FromArgb(0, 230, 70);
    private static readonly Color GruenHell = Color.FromArgb(160, 240, 170);

    private RasterZelle ZelleLiefern(int z, int s)
    {
        if (z >= _raster.Zeilen.Count || s >= _raster.Spalten.Count) return RasterZelle.Leer;
        var (t, stunde) = Position(z, s);
        if (!Kalender.IstUnterrichtstag(D, t) || !D.Zeitraster.Verfuegbar(Kalender.TagIndex(D, t), stunde)) return new RasterZelle("", Color.FromArgb(205, 205, 205), Color.Black);
        var liste = Eintraege(t, stunde);
        var findet = liste.Where(e => e.Findetstatt).ToList();
        var hinter = Color.White;
        if (_vorziehungen != null && _vorziehungen.TryGetValue((t, stunde), out var moeglich)) hinter = moeglich ? GruenHell : hinter;
        if (findet.Count == 0)
        {
            if (liste.Count > 0) return new RasterZelle("-", hinter, Color.Gray);
            return new RasterZelle("", hinter, Color.Black);
        }
        var offen = findet.Any(e => e.Status == TagesStatus.Offen);
        var text = E.VertretungDarstellung switch
        {
            0 => findet.Count > 1 ? "&" : findet[0].Unterricht?.Zeilen.Count > 1 ? "X" : findet[0].Vertretung?.UnterrichtId is null && findet[0].Vertretung != null ? "S" : "O",
            1 => string.Join(",", findet.Select(e => Ix.KFach(e.FachId)).Where(x => x.Length > 0).Distinct()),
            _ => _typ == ElementTyp.Klasse
                ? string.Join(",", findet.Select(e => e.Status == TagesStatus.Offen && e.LehrerIds.Count == 0 ? "???" : string.Join(",", e.LehrerIds.Select(Ix.KLehrer))).Where(x => x.Length > 0).Distinct())
                : string.Join(",", findet.Select(e => Ix.KKlassen(e.KlassenIds)).Where(x => x.Length > 0).Distinct()),
        };
        if (offen && E.VertretungDarstellung != 2) text = "???";
        if (findet.Any(e => e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true)) text = "*" + text;
        if (offen) return new RasterZelle(text.Length == 0 ? "???" : text, Offen, Color.White, true);
        if (findet.Any(e => e.Vertretung != null)) hinter = hinter == GruenHell ? hinter : Geaendert;
        return new RasterZelle(text, hinter, Color.Black);
    }

    /// <summary>Beim Ziehen: grün = kollisionsfreie Verlegung, ↔ = Tausch möglich.</summary>
    private (Color, string?)? ZielLiefern(int z, int s, string daten)
    {
        var (t, stunde) = Position(z, s);
        if (!Kalender.IstUnterrichtstag(D, t)) return null;
        if (daten.StartsWith("tag:"))
        {
            var teile = daten.Split(':');
            if (teile.Length < 5) return null;
            var von = DateOnly.ParseExact(teile[1], "yyyyMMdd");
            var vonStunde = int.Parse(teile[2]);
            var uid = int.Parse(teile[3]);
            int? zeile = int.Parse(teile[4]) is var zz && zz >= 0 ? zz : null;
            var key = (daten, t, stunde);
            if (!_zielCache.TryGetValue(key, out var st))
                _zielCache[key] = st = Tagesverlegung.Zielstatus(D, Plan(von), vonStunde, uid, zeile, Plan(t), stunde);
            return st switch
            {
                Feldstatus.Frei => (Gruen, null),
                Feldstatus.TauschMoeglich => (Gruen, "↔"),
                _ => null,
            };
        }
        if (daten.StartsWith("sondereinsatz:") || daten.StartsWith("vormerkung:"))
            return Eintraege(t, stunde).Any(e => e.Findetstatt) ? null : (GruenHell, null);
        return null;
    }

    /// <summary>
    /// Vorziehungen (Handbuch Vertretungsplanung Kap. 9.3): Ist eine offene Vertretung des angezeigten Elements aktiv, werden alle Stunden
    /// grün dargestellt, die kollisionsfrei in die offene Vertretung verlegt werden können.
    /// </summary>
    private void VorziehungenBerechnen()
    {
        _vorziehungen = null;
        if (AppZustand.AktiveVertretung is not { } v || v.VertreterId != null || v.IstEntfall || !_tage.Contains(v.Datum)) return;
        var relevant = _typ == ElementTyp.Klasse ? v.NeueKlassenIds.Contains(_id) : v.LehrerId == _id;
        if (!relevant) return;
        var ziel = Plan(v.Datum);
        _vorziehungen = new();
        using var _ = DatenIndex.Gemeinsam(D, Ix);
        foreach (var t in _tage)
            for (var s = 1; s <= D.Zeitraster.StundenProTag; s++)
            {
                if (t == v.Datum && s == v.Stunde) continue;
                foreach (var e in Eintraege(t, s).Where(e => e.Findetstatt && e.Status != TagesStatus.Offen && e.Unterricht != null))
                    if (Tagesverlegung.Zielstatus(D, Plan(t), s, e.Unterricht!.Id, null, ziel, v.Stunde) == Feldstatus.Frei) { _vorziehungen[(t, s)] = true; break; }
            }
    }

    // ------------------------------------------------------------------------------------------
    // Lupe
    // ------------------------------------------------------------------------------------------

    /// <summary>Zeile der Lupe: Kopplungszeile eines Unterrichts, ein Sondereinsatz oder die leere Zeile für einen neuen Eintrag.</summary>
    private sealed record LupenZeile(TagesEintrag? Eintrag, Vertretung? Sondereinsatz);

    private void LupeAufbauen()
    {
        Ui.RasterGestalten(_lupe, true);
        _lupe.RowHeadersVisible = false;
        _lupe.AllowUserToAddRows = false;
        _lupe.SelectionMode = DataGridViewSelectionMode.CellSelect;
        DataGridViewComboBoxColumn Auswahl(string name, string text, ElementTyp typ, int breite)
        {
            var c = new DataGridViewComboBoxColumn { Name = name, HeaderText = text, Width = breite, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, SortMode = DataGridViewColumnSortMode.NotSortable };
            c.Items.Add("");
            c.Items.Add("???");
            foreach (var e in Stammdaten.Sortiert(D, typ)) c.Items.Add(e.Kurzname);
            return c;
        }
        _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = "U", HeaderText = "U-Nr", Width = 44, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns.Add(Auswahl("Lehrer", "Lehrer", ElementTyp.Lehrer, 64));
        _lupe.Columns.Add(Auswahl("Fach", "Fach", ElementTyp.Fach, 56));
        _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = "Klassen", HeaderText = "Klasse(n)", Width = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns.Add(Auswahl("Raum", "Raum", ElementTyp.Raum, 56));
        foreach (var (n, t, w) in new[] { ("von", "von", 50), ("nach", "nach", 50), ("Statistik", "Statistik", 56) })
            _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Text", Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = "Studenten", HeaderText = "Studenten", Width = 64, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gruppe", HeaderText = "Schülergruppe", Width = 110, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.DataError += (_, e) => e.ThrowException = false;
        _lupe.CurrentCellDirtyStateChanged += (_, _) => { if (_lupe.IsCurrentCellDirty && _lupe.CurrentCell is DataGridViewComboBoxCell) _lupe.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _lupe.CellValueChanged += (_, e) => { if (!_laden && e.RowIndex >= 0) NachZellwechsel(() => LupeUebernehmen(e.RowIndex)); };
        _lupe.AllowDrop = true;
        _lupe.DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string x && (x.StartsWith("tag:") || x.StartsWith("sondereinsatz:")) ? DragDropEffects.Move : DragDropEffects.None;
        _lupe.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.Text) is string x) BeginInvoke(() => InLupeGezogen(x)); };
    }

    private void LupeFuellen()
    {
        _laden = true;
        try
        {
            _lupe.Rows.Clear();
            if (AktuelleZelle() is not { } c) return;
            var liste = Eintraege(c.Datum, c.Stunde);
            foreach (var e in liste)
            {
                var v = e.Vertretung;
                var uid = e.Unterricht?.Id ?? v?.UnterrichtId;
                var se = uid is null && v != null && Sondereinsatzverlegung.IstVerlegbar(v) ? v : null;
                var lehrer = e.Status == TagesStatus.Offen && e.LehrerIds.Count == 0 ? "???" : Ix.KLehrer(e.LehrerIds.FirstOrDefault() is var l && l != 0 ? l : null);
                var von = v?.VonDatum is DateOnly vd ? $"{vd.Day}.{vd.Month}.-{v.VonStunde}" : "";
                var nach = v?.NachDatum is DateOnly nd ? $"{nd.Day}.{nd.Month}.-{v.NachStunde}" : "";
                var studt = D.Klassen.Where(k => e.KlassenIds.Contains(k.Id)).Sum(k => k.Schuelerzahl);
                var r = _lupe.Rows.Add(uid?.ToString() ?? (v != null ? "V" + v.Id : ""), lehrer, Ix.KFach(e.FachId), Ix.KKlassen(e.KlassenIds),
                    string.Join(",", e.RaumIds.Select(Ix.KRaum)), von, nach, v?.Statistik ?? e.Unterricht?.Statistik ?? "", v?.Text ?? e.Unterricht?.Text ?? "",
                    studt > 0 ? studt.ToString() : "", v?.Schuelergruppe is { Length: > 0 } sg ? sg : e.Unterricht?.Schuelergruppe ?? "");
                var row = _lupe.Rows[r];
                row.Tag = new LupenZeile(e, se);
                if (!e.Findetstatt) row.DefaultCellStyle.ForeColor = Color.Gray;
                if (e.Status == TagesStatus.Offen) row.Cells["Lehrer"].Style.BackColor = Offen;
            }
            // leere Zeile: weitere Kopplungszeile bzw. Sondereinsatz in einer freien Stunde
            var neu = _lupe.Rows.Add("", "", "", _typ == ElementTyp.Klasse ? Ix.K(ElementTyp.Klasse, _id) : "", "", "", "", "", "", "", "");
            _lupe.Rows[neu].Tag = new LupenZeile(null, null);
            if (_typ == ElementTyp.Lehrer) _lupe.Rows[neu].Cells["Lehrer"].Value = Ix.KLehrer(_id);
            // Einschränkung: in der Sicht „Klasse(n)“ keine Änderung der Klasse, in der Sicht „Lehrer“ keine Änderung des Lehrers
            foreach (DataGridViewRow row in _lupe.Rows)
            {
                row.Cells["Klassen"].ReadOnly = _typ == ElementTyp.Klasse;
                row.Cells["Lehrer"].ReadOnly = _typ == ElementTyp.Lehrer;
                if (row.Tag is LupenZeile { Eintrag: { } e2 } && !e2.Findetstatt) row.ReadOnly = true;
            }
            _lupe.Rows[0].DefaultCellStyle.BackColor = liste.Count > 0 ? Ui.ZelleAktivVertretung : Color.White;
        }
        finally { _laden = false; }
    }

    private int? IdVon(ElementTyp typ, object? wert)
    {
        var k = (wert as string ?? "").Trim();
        if (k.Length == 0 || k == "???") return null;
        return Stammdaten.Sortiert(D, typ).FirstOrDefault(e => string.Equals(e.Kurzname, k, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private List<int> KlassenVon(object? wert) =>
        (wert as string ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(k => IdVon(ElementTyp.Klasse, k)).Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();

    /// <summary>Geänderte Lupenzeile übernehmen: Sondereinsatz anlegen, Sondereinsatz ändern bzw. Unterricht der Stunde ändern.</summary>
    private void LupeUebernehmen(int zeile)
    {
        if (zeile < 0 || zeile >= _lupe.Rows.Count || AktuelleZelle() is not { } c || _lupe.Rows[zeile].Tag is not LupenZeile lz) return;
        var row = _lupe.Rows[zeile];
        var lehrer = IdVon(ElementTyp.Lehrer, row.Cells["Lehrer"].Value);
        var fach = IdVon(ElementTyp.Fach, row.Cells["Fach"].Value);
        var raum = IdVon(ElementTyp.Raum, row.Cells["Raum"].Value);
        var klassen = KlassenVon(row.Cells["Klassen"].Value);
        var text = (row.Cells["Text"].Value as string ?? "").Trim();
        if (lz.Eintrag is null)
        {
            // neue Zeile: Sondereinsatz (Handbuch 10.2 „Eine Stunde hinzufügen“) – erst anlegen, wenn neben dem Element etwas eingetragen ist
            if (_typ == ElementTyp.Klasse && !klassen.Contains(_id)) klassen.Insert(0, _id);
            if (_typ == ElementTyp.Lehrer) lehrer = _id;
            var etwas = _typ == ElementTyp.Klasse ? lehrer.HasValue || fach.HasValue || raum.HasValue : klassen.Count > 0 || fach.HasValue || raum.HasValue;
            if (!etwas) return;
            if (lehrer is int l && Stundenaenderung.LehrerKonflikt(D, c.Datum, c.Stunde, l, null) is { } konflikt &&
                !Ui.Frage(Stundenaenderung.KonfliktText(D, l, c.Datum, c.Stunde, konflikt) + "\n\nSondereinsatz trotzdem anlegen? (Der Unterricht wird zur Vertretung bzw. entfällt.)")) { LupeFuellen(); return; }
            Aendern("Sondereinsatz", AenderungsArt.Vertretung, d =>
                Vertretungen.Sondereinsatz(d, c.Datum, c.Stunde, c.Stunde, klassen, lehrer is int x ? new[] { x } : Array.Empty<int>(), fach, raum, text, "", null, AppZustand.Benutzer));
            Neuaufbau();
            return;
        }
        if (lz.Sondereinsatz is { } se)
        {
            var id = se.Id;
            Aendern("Sondereinsatz ändern", AenderungsArt.Vertretung, d =>
            {
                var v = d.Vertretungen.First(x => x.Id == id);
                v.VertreterId = lehrer;
                v.FachId = v.VertretungsfachId = fach;
                if (klassen.Count > 0) { v.KlassenIds = klassen; v.VertretungsKlassenIds = null; }
                v.VertretungsraumId = raum;
                v.Text = text;
                Vertretungen.AlsGeaendertMarkieren(v, AppZustand.Benutzer);
            });
            Neuaufbau();
            return;
        }
        var e = lz.Eintrag;
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (uid is null) return;
        var zi = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
        if (klassen.Count == 0) klassen = e.KlassenIds.ToList();
        if (lehrer is int neuL && !e.LehrerIds.Contains(neuL) && Stundenaenderung.LehrerKonflikt(D, c.Datum, c.Stunde, neuL, uid) is { } k2 &&
            !Ui.Frage(Stundenaenderung.KonfliktText(D, neuL, c.Datum, c.Stunde, k2) + "\n\nTrotzdem einsetzen?")) { LupeFuellen(); return; }
        Aendern("Unterricht geändert", AenderungsArt.Vertretung, d => Vertretungen.StundeAendern(d, uid.Value, zi, c.Datum, c.Stunde, lehrer, fach, klassen, raum, text, AppZustand.Benutzer));
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Funktionen
    // ------------------------------------------------------------------------------------------

    /// <summary>Gewählte Lupenzeile (Kopplungszeile) bzw. der erste stattfindende Eintrag der Stunde.</summary>
    private TagesEintrag? GewaehlterEintrag(bool auchEntfall = false)
    {
        if (_lupe.CurrentRow?.Tag is LupenZeile { Eintrag: { } e } && (auchEntfall || e.Findetstatt)) return e;
        var liste = AuswahlEintraege();
        return liste.FirstOrDefault(x => x.Findetstatt) ?? (auchEntfall ? liste.FirstOrDefault() : null);
    }

    private bool ZeileGewaehlt => _lupe.ContainsFocus && _lupe.CurrentRow?.Tag is LupenZeile { Eintrag: not null };

    /// <summary>
    /// Entfall (Handbuch Vertretungsplanung Kap. 9.2): Unterricht der gewählten Stunde ohne Absenz entfallen lassen; erneuter Klick macht
    /// den Entfall rückgängig. Mit gewählter Lupenzeile gilt der Entfall nur für diese Kopplungszeile.
    /// </summary>
    private void Entfall()
    {
        if (AktuelleZelle() is not { } c) return;
        var e = GewaehlterEintrag(true);
        if (e is null) { Ui.Info("In dieser Stunde findet kein Unterricht statt."); return; }
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (!e.Findetstatt && e.Vertretung is { } entfall)
        {
            Aendern("Entfall rückgängig", AenderungsArt.Vertretung, d => Vertretungen.EntfallUmschalten(d, d.Vertretungen.First(x => x.Id == entfall.Id), AppZustand.Benutzer));
            Neuaufbau();
            return;
        }
        if (uid is null && e.Vertretung is { } se)
        {
            if (!Ui.LoeschenBestaetigen(1, "Sondereinsatz", "Sondereinsätze")) return;
            Aendern("Sondereinsatz löschen", AenderungsArt.Vertretung, d => Verlegungsaufhebung.SondereinsaetzeLoeschen(d, new[] { se.Id }));
            Neuaufbau();
            return;
        }
        if (uid is null) return;
        int? zeile = ZeileGewaehlt ? e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex : null;
        if (VerlegungsAbfrage.VorEntfall(c.Datum, c.Stunde, uid.Value, Aendern)) { Neuaufbau(); return; }
        var seLoeschen = StundenplanFenster.SondereinsatzLoeschenFragen(Vertretungen.SondereinsaetzeBeiEntfall(D, uid.Value, zeile, c.Datum, c.Stunde));
        Aendern("Entfall", AenderungsArt.Vertretung, d => Vertretungen.Entfallen(d, uid.Value, zeile, c.Datum, c.Stunde, AppZustand.Benutzer, seLoeschen));
        Neuaufbau();
    }

    private void InLupeGezogen(string daten)
    {
        if (daten.StartsWith("sondereinsatz:") && int.TryParse(daten[14..], out var vid))
        {
            if (!Ui.LoeschenBestaetigen(1, "Sondereinsatz", "Sondereinsätze")) return;
            Aendern("Sondereinsatz löschen", AenderungsArt.Vertretung, d => Verlegungsaufhebung.SondereinsaetzeLoeschen(d, new[] { vid }));
            Neuaufbau();
            return;
        }
        var t = daten.Split(':');
        if (t.Length < 5) return;
        var datum = DateOnly.ParseExact(t[1], "yyyyMMdd");
        var stunde = int.Parse(t[2]);
        var uid = int.Parse(t[3]);
        int? zeile = int.Parse(t[4]) is var z && z >= 0 ? z : null;
        if (VerlegungsAbfrage.VorEntfall(datum, stunde, uid, Aendern)) { Neuaufbau(); return; }
        var seLoeschen = StundenplanFenster.SondereinsatzLoeschenFragen(Vertretungen.SondereinsaetzeBeiEntfall(D, uid, zeile, datum, stunde));
        Aendern("Entfall", AenderungsArt.Vertretung, d => Vertretungen.Entfallen(d, uid, zeile, datum, stunde, AppZustand.Benutzer, seLoeschen));
        Neuaufbau();
    }

    private void Vorschlag()
    {
        if (GewaehlterEintrag() is { Vertretung: { } v }) AppZustand.VertretungWaehlen(v);
        HauptFenster.Oeffnen("vorschlag", () => new VertretungsvorschlagFenster());
    }

    /// <summary>Stunde als Vormerkung beiseitelegen (erscheint im Fenster „Vormerkungen“).</summary>
    private void Vormerken()
    {
        if (AktuelleZelle() is not { } c || GewaehlterEintrag() is not { } e || (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) is not int uid)
        {
            Ui.Info("Bitte eine Unterrichtsstunde wählen.");
            return;
        }
        int? zeile = _typ == ElementTyp.Lehrer ? e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex : null;
        Aendern("Vormerkung", AenderungsArt.Vertretung, d => Tagesverlegung.Vormerken(d, c.Datum, c.Stunde, uid, zeile, VormerkungsQuelle.Stundenplan, AppZustand.Benutzer));
        Neuaufbau();
    }

    private void Sondereinsatz()
    {
        if (AktuelleZelle() is not { } c) return;
        SondereinsatzDialog.Neu(this, c.Datum, c.Stunde, _typ == ElementTyp.Klasse ? new[] { _id } : Array.Empty<int>(), _typ == ElementTyp.Lehrer ? new[] { _id } : Array.Empty<int>());
        Neuaufbau();
    }

    private void RaumZuordnen()
    {
        if (AktuelleZelle() is not { } c || GewaehlterEintrag() is not { } e) { Ui.Info("Bitte zuerst eine Stunde wählen."); return; }
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (RaumZuordnenVertretungDialog.Zeigen(this, c.Datum, c.Stunde, uid, uid.HasValue ? e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex : null, e.Vertretung)) Neuaufbau();
    }

    private void StundeAendern()
    {
        if (AktuelleZelle() is not { } c) return;
        var liste = AuswahlEintraege().Where(e => e.Findetstatt).ToList();
        if (liste.Count == 0) { Sondereinsatz(); return; }
        if (StundeAendernDialog.Zeigen(this, c.Datum, c.Stunde, liste)) Neuaufbau();
    }

    /// <summary>Stunde für die Vertretungsplanung fixieren (Kennzeichen „*“) bzw. Fixierung lösen.</summary>
    private void Fixieren()
    {
        if (AktuelleZelle() is not { } c || GewaehlterEintrag() is not { } e) return;
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        var zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
        var ziel = !(e.Vertretung?.Fixiert ?? false);
        Aendern(ziel ? "Stunde fixieren" : "Fixierung lösen", AenderungsArt.Vertretung, d =>
        {
            var v = e.Vertretung is { } alt ? d.Vertretungen.First(x => x.Id == alt.Id) : null;
            if (v is null)
            {
                if (uid is null) return;
                // Fixierung als „Unterricht geändert“-Zeile ohne Änderung festhalten
                var r = e.Regel!;
                v = new Vertretung
                {
                    Id = d.NeueId(), Datum = c.Datum, Stunde = c.Stunde, UnterrichtId = uid, ZeilenIndex = zeile, LehrerId = r.LehrerId, VertreterId = r.LehrerId,
                    FachId = r.FachId, KlassenIds = Vertretungen.KlassenDerZeile(r), RaumId = r.RaumId, VertretungsraumId = r.RaumId,
                    Art = VertretungsArt.UnterrichtGeaendert, Quelle = VertretungsQuelle.Manuell, Bearbeitet = true, NichtDrucken = true,
                };
                d.Vertretungen.Add(v);
            }
            v.Fixiert = ziel;
        });
        Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Drag & Drop
    // ------------------------------------------------------------------------------------------

    private string? ZiehenBeginnt(int z, int s)
    {
        var (t, stunde) = Position(z, s);
        var e = Eintraege(t, stunde).FirstOrDefault(x => x.Findetstatt);
        if (e is null) return null;
        _zielCache.Clear();
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (uid is null) return e.Vertretung is { } v && Sondereinsatzverlegung.IstVerlegbar(v) ? $"sondereinsatz:{v.Id}" : null;
        var zeile = _typ == ElementTyp.Lehrer ? e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0 : -1;
        return $"tag:{t:yyyyMMdd}:{stunde}:{uid}:{zeile}:1";
    }

    private void Abgelegt(int z, int s, string daten, bool kopieren)
    {
        var (ziel, stunde) = Position(z, s);
        _zielCache.Clear();
        if (!Kalender.IstUnterrichtstag(D, ziel)) { Ui.Info("An diesem Tag findet kein Unterricht statt."); return; }
        if (daten.StartsWith("sondereinsatz:") && int.TryParse(daten[14..], out var vid))
        {
            // Sondereinsatz verlegen bzw. mit gedrückter Strg-Taste kopieren (Handbuch Vertretungsplanung Kap. 10.2)
            Aendern(kopieren ? "Sondereinsatz kopieren" : "Sondereinsatz verlegen", AenderungsArt.Vertretung, d => Sondereinsatzverlegung.Verlegen(d, vid, ziel, stunde, kopieren, AppZustand.Benutzer));
            Neuaufbau();
            ZelleWaehlen(ziel, stunde);
            return;
        }
        if (daten.StartsWith("vormerkung:") && int.TryParse(daten[11..], out var vmId))
        {
            Aendern("Vormerkung verplanen", AenderungsArt.Vertretung, d => Tagesverlegung.VormerkungVerplanen(d, d.Vormerkungen.First(v => v.Id == vmId), ziel, stunde, AppZustand.Benutzer));
            Neuaufbau();
            return;
        }
        if (!daten.StartsWith("tag:")) return;
        var t = daten.Split(':');
        var von = DateOnly.ParseExact(t[1], "yyyyMMdd");
        var vonStunde = int.Parse(t[2]);
        var uid = int.Parse(t[3]);
        int? zeile = int.Parse(t[4]) is var zz && zz >= 0 ? zz : null;
        var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
        if (von == ziel && vonStunde == stunde) return;
        var a = Tagesverlegung.Analysieren(D, von, vonStunde, uid, zeile, laenge, ziel, stunde);
        if (a.Schritte.Count == 0) return;
        var tpVon = Plan(von);
        if (a.Schritte.Any(x => tpVon.Stunde(x.VonStunde).Any(e => (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == uid &&
                (e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true))) &&
            !Ui.Frage($"Eine Unterrichtsstunde ist für die Vertretungsplanung fixiert.\n{Planungsdienst.TageKurz[((int)von.DayOfWeek + 6) % 7]}-{vonStunde}\nU-Nr {uid}\n\nTrotzdem fortfahren?")) return;
        var modus = VerlegeModus.Verlegen;
        if (a.TauschMoeglich)
        {
            var w = OptionenDialog.Fragen(this, "Frage", Tagesverlegung.TauschFrage(D, a), new[] { "mit dortiger Unterrichtsstunde tauschen", "nur dorthin verlegen" });
            if (w is null) return;
            if (w == 0) modus = VerlegeModus.Tauschen;
            else a = a.NurDorthin();
        }
        if (modus != VerlegeModus.Tauschen && a.Kollision)
        {
            var w = OptionenDialog.Fragen(this, "Frage", Tagesverlegung.KollisionsFrage(D, a), new[] { "Unterricht nicht verplanen.", "Behindernden Unterricht verdrängen (vormerken)", "Kollision erzeugen" }, 1);
            if (w is null or 0) return;
            modus = w == 1 ? VerlegeModus.Verdraengen : VerlegeModus.Kollision;
        }
        if (modus != VerlegeModus.Tauschen && a.LehrerAbwesend && !Ui.Frage("Ein beteiligter Lehrer ist zu diesem Zeitpunkt abwesend. Trotzdem verlegen? (Es entsteht eine offene Vertretung.)")) return;
        if (RaumWahl(a, modus) is not { } raeume) return;
        Aendern(modus switch
        {
            VerlegeModus.Tauschen => "Stundentausch",
            VerlegeModus.Verdraengen => "Verlegung mit Verdrängung",
            _ => "Stundenverlegung",
        }, AenderungsArt.Vertretung, d => Tagesverlegung.Ausfuehren(d, a, modus, AppZustand.Benutzer, raeume));
        Neuaufbau();
        ZelleWaehlen(ziel, stunde);
    }

    /// <summary>Räume beim Verlegen: mitnehmen oder belassen, bei belegtem bzw. gesperrtem Raum Raumvertretung oder Kollision (wie im Stundenplan).</summary>
    private VerlegeRaumWahl? RaumWahl(VerlegeAnalyse a, VerlegeModus modus)
    {
        var zeilen = Verlegungsraeume.Zeilen(D, a);
        var mitnahme = RaumMitnahme.Automatisch;
        if (Verlegungsraeume.MitnahmeFrageNoetig(zeilen))
        {
            var w = Ui.Auswahl(Verlegungsraeume.MitnahmeText(D, a, zeilen), "UntPlan - Räume mitnehmen oder belassen?", new[] { "Mitnehmen", "Belassen" });
            if (w is null) return null;
            mitnahme = w == 0 ? RaumMitnahme.Mitnehmen : RaumMitnahme.Belassen;
        }
        var konflikte = Verlegungsraeume.Konflikte(D, a, modus, mitnahme, zeilen);
        if (konflikte.Count == 0) return new VerlegeRaumWahl(mitnahme);
        var andere = Verlegungsraeume.AndereRaumvertretungMoeglich(konflikte);
        var antwort = Ui.Auswahl(Verlegungsraeume.KonfliktText(D, a.Ziel, konflikte), konflikte.Any(k => k.Gesperrt) ? "UntPlan - Raum gesperrt" : "UntPlan - Raum belegt",
            new[] { "Verlegen und Raumvertretung für meinen Unterricht", "Raumvertretung für den anderen Unterricht", "Kollision erzeugen" }, 0, new[] { true, andere, true });
        if (antwort is null) return null;
        return new VerlegeRaumWahl(mitnahme, antwort switch { 0 => RaumBelegtLoesung.EigeneRaumvertretung, 1 => RaumBelegtLoesung.AndereRaumvertretung, _ => RaumBelegtLoesung.Kollision });
    }

    // ------------------------------------------------------------------------------------------
    // Fensterlogik
    // ------------------------------------------------------------------------------------------

    private void AuswahlMelden()
    {
        if (_laden || _stumm || AktuelleZelle() is not { } c) return;
        var e = Eintraege(c.Datum, c.Stunde).FirstOrDefault(x => x.Findetstatt);
        if (e?.Vertretung is { } v) { _eigeneAuswahl = true; try { AppZustand.VertretungWaehlen(v); } finally { _eigeneAuswahl = false; } }
    }

    private bool _eigeneAuswahl;
    /// <summary>Cursor wird beim Neuaufbau nur wiederhergestellt – keine Auswahl an andere Fenster melden.</summary>
    private bool _stumm;

    /// <summary>Gewählte Vertretung: Planungsdialog zeigt deren Klasse bzw. Lehrkraft in der passenden Woche (außer „beibehalten“).</summary>
    private void VertretungGewaehlt(Vertretung? v)
    {
        if (IsDisposed || v is null || _eigeneAuswahl) { if (!_eigeneAuswahl && !IsDisposed) Neuaufbau(); return; }
        if (!_beibehalten.Checked)
        {
            var id = _typ == ElementTyp.Klasse ? v.NeueKlassenIds.FirstOrDefault() : v.VertreterId ?? v.LehrerId ?? 0;
            if (id != 0) _id = id;
            ElementlisteFuellen();
        }
        if (!_tage.Contains(v.Datum))
        {
            _laden = true;
            _woche1.Value = Kalender.Montag(v.Datum).ToDateTime(TimeOnly.MinValue);
            _woche2.Value = _woche1.Value.AddDays(7);
            _laden = false;
        }
        Neuaufbau();
        _stumm = true;
        try { ZelleWaehlen(v.Datum, v.Stunde, false); }
        finally { _stumm = false; }
    }

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || _tage.Contains(datum)) return;
        _laden = true;
        _woche1.Value = Kalender.Montag(datum).ToDateTime(TimeOnly.MinValue);
        _woche2.Value = _woche1.Value.AddDays(7);
        _laden = false;
        Neuaufbau();
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || !Verknuepft || _beibehalten.Checked || IsDisposed) return;
        if (typ is not (ElementTyp.Klasse or ElementTyp.Lehrer)) return;
        _typ = typ;
        _id = id;
        ElementlisteFuellen();
        Neuaufbau();
    }

    private void Kontextmenue(int z, int s, Point pt)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Entfall", Symbole.Get("entfall"), (_, _) => Entfall());
        cm.Items.Add("Stunde ändern…", Symbole.Get("lehrer"), (_, _) => StundeAendern());
        cm.Items.Add("Sondereinsatz…", Symbole.Get("sondereinsatz"), (_, _) => Sondereinsatz());
        cm.Items.Add("Raum zuordnen / löschen…", Symbole.Get("raumzuordnen"), (_, _) => RaumZuordnen());
        cm.Items.Add("Vormerkung", Symbole.Get("vormerkung"), (_, _) => Vormerken());
        cm.Items.Add("Stunde fixieren", Symbole.Get("fixieren"), (_, _) => Fixieren());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Vertretungsvorschlag", Symbole.Get("vertretung"), (_, _) => Vorschlag());
        cm.Show(_raster, pt);
    }

    private void EinstellungenZeigen()
    {
        if (!VertretungsPlanungsdialogEinstellungen.Zeigen(this, E)) return;
        AppZustand.Einstellungen.Speichern();
        Neuaufbau();
    }

    /// <summary>Selbsttest: Zeilen (Datum) und Text einer Zelle.</summary>
    internal IReadOnlyList<string> SelbsttestZeilen => _raster.Zeilen.Select(z => z.Text).ToList();
    internal string SelbsttestZelle(int z, int s) => ZelleLiefern(z, s).Text;
}

/// <summary>Einstellungen des Planungsdialogs der Vertretungsplanung: Darstellung, Orientierung, Spaltenbreite, Schrift (Handbuch Kap. 9).</summary>
public static class VertretungsPlanungsdialogEinstellungen
{
    public static bool Zeigen(IWin32Window owner, PlanungsdialogEinstellungen e)
    {
        using var d = new Dialog("Einstellungen", 380, 150);
        var darstellung = new GroupBox { Text = "Darstellung", Left = 8, Top = 6, Width = 160, Height = 80 };
        var r0 = new RadioButton { Text = "Stunden-Merker (O,X,..)", Left = 8, Top = 16, Width = 148, Checked = e.VertretungDarstellung == 0 };
        var r1 = new RadioButton { Text = "Fächer", Left = 8, Top = 36, Width = 148, Checked = e.VertretungDarstellung == 1 };
        var r2 = new RadioButton { Text = "Klassen / Lehrer", Left = 8, Top = 56, Width = 148, Checked = e.VertretungDarstellung == 2 };
        darstellung.Controls.AddRange(new Control[] { r0, r1, r2 });
        var orientierung = new GroupBox { Text = "Orientierung", Left = 176, Top = 6, Width = 196, Height = 80 };
        var o1 = new RadioButton { Text = "Datum waagrecht, Stunden senkrecht", Left = 8, Top = 16, Width = 184, Height = 30, Checked = e.VertretungDatumZeilen };
        var o2 = new RadioButton { Text = "Stunden waagrecht, Datum senkrecht", Left = 8, Top = 46, Width = 184, Height = 30, Checked = !e.VertretungDatumZeilen };
        orientierung.Controls.AddRange(new Control[] { o1, o2 });
        var breite = new NumericUpDown { Left = 8, Top = 92, Width = 52, Minimum = 200, Maximum = 2000, Increment = 50, Value = Math.Clamp(e.VertretungSpaltenbreite, 200, 2000) };
        var schrift = new Font(e.Schriftart, e.Schriftgroesse);
        var schriftKnopf = new Button { Text = "Schrift", Width = 88, Height = 26 };
        schriftKnopf.Click += (_, _) =>
        {
            using var fd = new FontDialog { Font = schrift, ShowEffects = false };
            if (fd.ShowDialog(d) == DialogResult.OK) schrift = fd.Font;
        };
        // Schaltflächen wie im Vorbild: Ok, Schrift, Abbrechen
        d.Ok.Text = "Ok";
        d.Ok.Location = new Point(8 + 80, 114);
        schriftKnopf.Location = new Point(8 + 176, 114);
        d.Abbrechen.Location = new Point(8 + 272, 114);
        d.Controls.AddRange(new Control[] { darstellung, orientierung, breite, new Label { Text = "Spaltenbreite", Left = 64, Top = 95, AutoSize = true }, schriftKnopf });
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        e.VertretungDarstellung = r0.Checked ? 0 : r1.Checked ? 1 : 2;
        e.VertretungDatumZeilen = o1.Checked;
        e.VertretungSpaltenbreite = (int)breite.Value;
        e.Schriftart = schrift.Name;
        e.Schriftgroesse = schrift.SizeInPoints;
        return true;
    }
}
