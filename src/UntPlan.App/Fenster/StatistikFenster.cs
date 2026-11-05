using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vertretungsstatistik wie im Handbuch: Karteikarte „Statistik“ mit den Bereichen Zeitraum (Verlaufsgrafik), Stundenverteilung,
/// Wochenverteilung, Lehrerübersicht und Absenzgründe sowie der Detailansicht für einzelne Lehrer/Klassen; Karteikarte „Berichte“ mit
/// den Berichten der Vertretungsplanung (links) und der frei gegliederten Vertretungsstatistik (rechts).
/// </summary>
public sealed class StatistikFenster : MdiFenster, IDruckbar
{
    private readonly TabControl _reiter = new() { Dock = DockStyle.Fill };
    private readonly TabPage _seiteStatistik = new("Statistik") { BackColor = Abschnitt.Grund };
    private readonly TabPage _seiteBerichte = new("Berichte") { BackColor = Abschnitt.Grund };

    // --- Statistik ------------------------------------------------------------------------------------
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100, Left = 4, Top = 18 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100, Left = 148, Top = 18 };
    private readonly LinienGrafik _verlauf = new() { Left = 4, Top = 44, Height = 150, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly DataGridView _stunden = Raster();
    private readonly DataGridView _woche = Raster();
    private readonly DataGridView _lehrer = Raster();
    private readonly DataGridView _gruende = Raster();
    private readonly TortenGrafik _torte = new() { Dock = DockStyle.Top, Height = 190 };
    private readonly TabControl _ueberblick = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _entfaelle = Raster();
    private readonly DataGridView _vertretungen = Raster();
    private readonly Label _ueberblickSumme = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(245, 248, 242) };
    private readonly FlowLayoutPanel _links = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Abschnitt.Grund };
    private readonly FlowLayoutPanel _rechts = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Abschnitt.Grund };
    private readonly SplitContainer _teilung = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
    private readonly Panel _detailKopf = new() { Dock = DockStyle.Top, Height = 30, Visible = false };
    private readonly Label _detailArt = new() { Left = 6, Top = 7, AutoSize = true };
    private readonly ComboBox _detailWahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 60, Top = 4, Width = 110 };
    private readonly LinkLabel _zurueck = new() { Text = "Zurück zur Übersicht", Left = 190, Top = 7, AutoSize = true };
    private readonly LinkLabel _filterAus = new() { Left = 340, Top = 7, AutoSize = true, Visible = false };
    private readonly Abschnitt _aZeitraum, _aStunden, _aWoche, _aLehrer, _aGruende, _aUeberblick;
    private bool _wocheEntfall = true;
    private ElementTyp? _detailTyp;
    private int _detailId;
    private int? _grundFilter;
    private List<Statistik.Fall> _faelle = new();
    private (DateOnly, DateOnly, object?) _faelleStand;
    private bool _laedt;

    public StatistikFenster()
    {
        Text = "Vertretungsstatistik";
        HilfeThema = "Statistik";
        Icon = SymbolIcon("statistik");
        Size = new Size(1080, 640);
        Positionieren();
        var heute = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _von.Value = new DateTime(heute.Year, heute.Month, 1);
        _bis.Value = _von.Value.AddMonths(1).AddDays(-1);

        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Export CSV", "export", Exportieren));

        // Zeitraum
        var zeit = new Panel { BackColor = Abschnitt.Grund };
        zeit.Controls.Add(new Label { Text = "Von", Left = 4, Top = 2, AutoSize = true });
        zeit.Controls.Add(new Label { Text = "Bis", Left = 148, Top = 2, AutoSize = true });
        var neu = new Button { Left = 110, Top = 16, Width = 30, Height = 25, Image = Symbole.Get("aktualisieren", 16), FlatStyle = FlatStyle.Flat };
        new ToolTip().SetToolTip(neu, "Aktualisieren");
        neu.Click += (_, _) => { _faelleStand = default; Neuaufbau(); };
        zeit.Controls.AddRange(new Control[] { _von, neu, _bis, _verlauf });
        zeit.Resize += (_, _) => { _verlauf.Width = zeit.Width - 8; _verlauf.Height = zeit.Height - 48; };
        _aZeitraum = new Abschnitt("Zeitraum", zeit, 240);

        // Stundenverteilung
        _stunden.Columns.Add("K", "Klassen");
        _stunden.Columns.Add("KL", "Klassenlehrer");
        _stunden.Columns.Add(new DataGridViewTextBoxColumn { Name = "SV", HeaderText = "Stundenverteilung", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _stunden.CellPainting += VerteilungZeichnen;
        _stunden.CellToolTipTextNeeded += (_, e) => { if (e.RowIndex >= 0 && _stunden.Rows[e.RowIndex].Cells["SV"].Tag is int[] w) e.ToolTipText = VerteilungText(w); };
        _stunden.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _detailTyp != null || _stunden.Rows[e.RowIndex].Tag is not int id) return;
            DetailZeigen(ElementTyp.Klasse, id);
        };
        _aStunden = new Abschnitt("Stundenverteilung", _stunden, 210);

        // Wochenverteilung
        _woche.CellPainting += WocheZeichnen;
        var wcm = new ContextMenuStrip();
        var mEntfall = new ToolStripMenuItem("Entfall") { ForeColor = LinienGrafik.FarbeEntfall };
        var mVertr = new ToolStripMenuItem("Vertretungen") { ForeColor = LinienGrafik.FarbeVertretung };
        mEntfall.Click += (_, _) => { _wocheEntfall = true; WocheFuellen(); };
        mVertr.Click += (_, _) => { _wocheEntfall = false; WocheFuellen(); };
        wcm.Opening += (_, _) => { mEntfall.Checked = _wocheEntfall; mVertr.Checked = !_wocheEntfall; };
        wcm.Items.AddRange(new ToolStripItem[] { mEntfall, mVertr });
        _woche.ContextMenuStrip = wcm;
        _aWoche = new Abschnitt("Wochenverteilung", _woche, 230);

        // Lehrerübersicht
        foreach (var (n, k) in new[] { ("L", "Lehrer"), ("A", "Absenzen"), ("ET", "Entfalltage"), ("E", "Entfall"), ("V", "Vertretungen") })
            _lehrer.Columns[_lehrer.Columns.Add(n, k)].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (var n in new[] { "A", "ET", "E", "V" }) _lehrer.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _lehrer.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || _lehrer.Rows[e.RowIndex].Tag is not int id) return;
            DetailZeigen(ElementTyp.Lehrer, id);
        };
        _lehrer.Columns["L"]!.DefaultCellStyle.ForeColor = Color.FromArgb(0, 70, 160);
        _aLehrer = new Abschnitt("Lehrerübersicht", _lehrer, 240);

        // Absenzgründe
        var gp = new Panel { BackColor = Abschnitt.Grund };
        foreach (var (n, k, b) in new[] { ("N", "#", 40), ("P", "%", 46), ("G", "Absenzgrund", 110), ("LN", "", 140), ("EZ", "Entf zählen", 76), ("FS", "FS n.zählen", 76) })
            _gruende.Columns[_gruende.Columns.Add(n, k)].Width = b;
        _gruende.Columns["N"]!.DefaultCellStyle.Alignment = _gruende.Columns["P"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _gruende.Columns["EZ"]!.DefaultCellStyle.Alignment = _gruende.Columns["FS"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _gruende.CellPainting += GrundFarbeZeichnen;
        _gruende.CellClick += (_, e) => { if (e.RowIndex >= 0 && _gruende.Rows[e.RowIndex].Tag is int g) GrundFiltern(g); };
        _torte.Gewaehlt += k => { if (k is int g) GrundFiltern(g); };
        _gruende.Dock = DockStyle.Fill;
        gp.Controls.Add(_gruende);
        gp.Controls.Add(_torte);
        _aGruende = new Abschnitt("Absenzgründe", gp, 400);

        // Detailansicht: Überblick der Entfälle und Vertretungen
        var up = new Panel { BackColor = Abschnitt.Grund };
        foreach (var g in new[] { _entfaelle, _vertretungen })
        {
            foreach (var (n, k, b) in new[] { ("D", "Datum", 70), ("S", "Stunde", 46), ("G", "Grund/Art", 90), ("T", "Text", 110), ("V", "Vertreter", 60), ("K", "Klassen", 70), ("F", "Fach", 50), ("R", "Raum", 50), ("Z", "Zählen", 46) })
                g.Columns[g.Columns.Add(n, k)].Width = b;
            g.Columns["Z"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.Columns["Z"]!.DefaultCellStyle.ForeColor = Color.FromArgb(0, 150, 0);
            g.Dock = DockStyle.Fill;
        }
        _ueberblick.TabPages.Add(new TabPage("Entfälle") { Controls = { _entfaelle } });
        _ueberblick.TabPages.Add(new TabPage("Vertretungen") { Controls = { _vertretungen } });
        up.Controls.Add(_ueberblick);
        up.Controls.Add(_ueberblickSumme);
        _aUeberblick = new Abschnitt("Überblick der Entfälle und Vertretungen", up, 330);

        _detailKopf.Controls.AddRange(new Control[] { _detailArt, _detailWahl, _zurueck, _filterAus });
        _zurueck.LinkClicked += (_, _) => { _detailTyp = null; Neuaufbau(); };
        _filterAus.LinkClicked += (_, _) => { _grundFilter = null; Neuaufbau(); };
        _detailWahl.SelectionChangeCommitted += (_, _) => { if (_detailWahl.SelectedItem is Element el && _detailTyp is { } t) DetailZeigen(t, el.Id); };
        _detailWahl.Format += (_, e) => e.Value = e.ListItem is Element el ? el.Kurzname : e.ListItem?.ToString();
        _teilung.Panel1.Controls.Add(_links);
        _teilung.Panel2.Controls.Add(_rechts);
        _links.Resize += (_, _) => Breiten();
        _rechts.Resize += (_, _) => Breiten();
        _seiteStatistik.Controls.Add(_teilung);
        _seiteStatistik.Controls.Add(_detailKopf);
        _von.ValueChanged += (_, _) => Neuaufbau();
        _bis.ValueChanged += (_, _) => Neuaufbau();

        BerichteAufbauen();
        _reiter.TabPages.Add(_seiteStatistik);
        _reiter.TabPages.Add(_seiteBerichte);
        Controls.Add(_reiter);
        Controls.Add(ts);
        Load += (_, _) => Neuaufbau();
    }

    private static DataGridView Raster()
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.RowHeadersVisible = false;
        g.BackgroundColor = Color.White;
        g.ShowCellToolTips = true;
        return g;
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        _faelleStand = default;
        Neuaufbau();
        BerichtNeuaufbau();
    }

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private List<Statistik.Fall> Faelle()
    {
        var stand = (Von, Bis, (object?)Ix);
        if (!Equals(stand, _faelleStand))
        {
            using (new WarteCursor()) _faelle = Statistik.Faelle(D, Von, Bis);
            _faelleStand = stand;
        }
        return _faelle;
    }

    /// <summary>Fälle der Ansicht: Detailelement (Lehrer: eigener Unterricht, Klasse: Unterricht der Klasse) und Absenzgrund-Filter.</summary>
    private IEnumerable<Statistik.Fall> Gefiltert(bool mitGrund = true) => Faelle().Where(f =>
        (_detailTyp is null || _detailTyp == ElementTyp.Lehrer && f.LehrerId == _detailId || _detailTyp == ElementTyp.Klasse && f.KlassenIds.Contains(_detailId)) &&
        (!mitGrund || _grundFilter is null || f.Status != Statistik.FallStatus.Planmaessig && (f.GrundId ?? 0) == _grundFilter));

    private void DetailZeigen(ElementTyp typ, int id)
    {
        _detailTyp = typ;
        _detailId = id;
        Neuaufbau();
    }

    private void GrundFiltern(int grund)
    {
        _grundFilter = _grundFilter == grund ? null : grund;
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        if (_laedt || IsDisposed || !IsHandleCreated) return;
        _laedt = true;
        try
        {
            if (_bis.Value < _von.Value) _bis.Value = _von.Value;
            Anordnen();
            var faelle = Gefiltert().ToList();
            _verlauf.Setzen(Kalender.Tage(Von, Bis).Where(t => Kalender.IstUnterrichtstag(D, t)).Select(t =>
            {
                var tag = faelle.Where(f => f.Datum == t).ToList();
                return new LinienGrafik.Punkt(t, tag.Count(f => f.Status == Statistik.FallStatus.Planmaessig), tag.Count(f => f.Status == Statistik.FallStatus.Vertretung), tag.Count(f => f.Status == Statistik.FallStatus.Entfall));
            }).ToList());
            StundenFuellen(faelle);
            WocheFuellen();
            if (_detailTyp is null) LehrerFuellen();
            else UeberblickFuellen();
            GruendeFuellen();
            _filterAus.Visible = _grundFilter != null;
            _filterAus.Text = _grundFilter is int g ? $"Filter Absenzgrund {GrundName(g)} aufheben" : "";
            if (_filterAus.Visible && _detailTyp is null) { _detailKopf.Visible = true; _detailArt.Visible = _detailWahl.Visible = _zurueck.Visible = false; }
        }
        finally { _laedt = false; }
    }

    private string GrundName(int id) => id == 0 ? "Kein Grund" : D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == id)?.Kurzname ?? "?";

    /// <summary>Übersicht: alle Bereiche untereinander; Detailansicht: links Zeitraum/Stunden-/Wochenverteilung, rechts Überblick und Absenzgründe.</summary>
    private void Anordnen()
    {
        var detail = _detailTyp != null;
        _links.SuspendLayout();
        _rechts.SuspendLayout();
        _links.Controls.Clear();
        _rechts.Controls.Clear();
        if (detail)
        {
            _links.Controls.AddRange(new Control[] { _aZeitraum, _aStunden, _aWoche });
            _rechts.Controls.AddRange(new Control[] { _aUeberblick, _aGruende });
            _teilung.Panel2Collapsed = false;
            if (_teilung.Width > 500) _teilung.SplitterDistance = Math.Min(420, Math.Max(200, _teilung.Width / 2 - 60));
            _detailKopf.Visible = _detailArt.Visible = _detailWahl.Visible = _zurueck.Visible = true;
            _detailArt.Text = _detailTyp == ElementTyp.Lehrer ? "Lehrer" : "Klasse";
            _detailWahl.Items.Clear();
            foreach (var el in _detailTyp == ElementTyp.Lehrer ? Stammdaten.Sortiert(D.Lehrer).Cast<Element>() : Stammdaten.Sortiert(D.Klassen)) _detailWahl.Items.Add(el);
            _detailWahl.SelectedItem = _detailWahl.Items.Cast<Element>().FirstOrDefault(x => x.Id == _detailId);
            _stunden.Columns["KL"]!.HeaderText = "Fach";
            _stunden.Columns["K"]!.HeaderText = "Klasse";
        }
        else
        {
            _links.Controls.AddRange(new Control[] { _aZeitraum, _aStunden, _aWoche, _aLehrer, _aGruende });
            _teilung.Panel2Collapsed = true;
            _detailKopf.Visible = false;
            _stunden.Columns["KL"]!.HeaderText = "Klassenlehrer";
            _stunden.Columns["K"]!.HeaderText = "Klassen";
        }
        _links.ResumeLayout();
        _rechts.ResumeLayout();
        Breiten();
    }

    private void Breiten()
    {
        foreach (var p in new[] { _links, _rechts })
        {
            var w = Math.Max(200, p.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            foreach (Control c in p.Controls) c.Width = w;
        }
    }

    // --- Stundenverteilung ----------------------------------------------------------------------------

    private void StundenFuellen(List<Statistik.Fall> faelle)
    {
        _stunden.Rows.Clear();
        var ix = Ix;
        if (_detailTyp is null)
        {
            foreach (var k in Stammdaten.Sortiert(D.Klassen))
            {
                var eigene = faelle.Where(f => f.KlassenIds.Contains(k.Id)).ToList();
                if (eigene.Count == 0) continue;
                var r = _stunden.Rows.Add(k.Kurzname, string.Join(", ", k.KlassenlehrerIds.Select(i => ix.K(ElementTyp.Lehrer, i))), "");
                _stunden.Rows[r].Tag = k.Id;
                _stunden.Rows[r].Cells["SV"].Tag = Verteilung(eigene);
            }
            return;
        }
        // Detail: betroffene Unterrichte (Klasse, Fach)
        foreach (var g in faelle.GroupBy(f => (Klassen: ix.KKlassen(f.KlassenIds), Fach: ix.KFach(f.FachId))).OrderBy(g => g.Key.Klassen).ThenBy(g => g.Key.Fach))
        {
            var r = _stunden.Rows.Add(g.Key.Klassen, g.Key.Fach, "");
            _stunden.Rows[r].Cells["SV"].Tag = Verteilung(g.ToList());
        }
    }

    private static int[] Verteilung(List<Statistik.Fall> l) =>
        new[] { l.Count(f => f.Status == Statistik.FallStatus.Planmaessig), l.Count(f => f.Status == Statistik.FallStatus.Vertretung), l.Count(f => f.Status == Statistik.FallStatus.Entfall) };

    private static string VerteilungText(int[] w)
    {
        var s = Math.Max(1, w.Sum());
        return $"{w[0]} ({100.0 * w[0] / s:0.00} %) Planmäßig\n{w[1]} ({100.0 * w[1] / s:0.00} %) Vertretungen\n{w[2]} ({100.0 * w[2] / s:0.00} %) Entfall";
    }

    private void VerteilungZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || sender is not DataGridView g || g.Columns[e.ColumnIndex].Name != "SV" || g.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not int[] w) return;
        e.PaintBackground(e.CellBounds, false);
        var r = Rectangle.Inflate(e.CellBounds, -2, -2);
        var summe = Math.Max(1, w.Sum());
        var x = (float)r.Left;
        foreach (var (wert, farbe) in new[] { (w[0], Color.FromArgb(0, 128, 0)), (w[1], Color.Orange), (w[2], Color.Red) })
        {
            var breite = r.Width * wert / (float)summe;
            using var b = new SolidBrush(farbe);
            e.Graphics!.FillRectangle(b, x, r.Top, breite, r.Height);
            x += breite;
        }
        e.Handled = true;
    }

    // --- Wochenverteilung -----------------------------------------------------------------------------

    private void WocheFuellen()
    {
        _woche.Rows.Clear();
        _woche.Columns.Clear();
        _woche.Columns.Add("S", "Stunde");
        _woche.Columns["S"]!.Width = 46;
        _woche.Columns["S"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _woche.Columns.Add("Z", "");
        _woche.Columns["Z"]!.Width = 90;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++) _woche.Columns[_woche.Columns.Add("T" + t, Planungsdienst.TageKurz[(D.Zeitraster.ErsterSchultag + t) % 7])].Width = 56;
        var status = _wocheEntfall ? Statistik.FallStatus.Entfall : Statistik.FallStatus.Vertretung;
        var faelle = Gefiltert().Where(f => f.Status == status).ToList();
        var summe = Math.Max(1, faelle.Count);
        for (var s = 1; s <= D.Zeitraster.StundenProTag; s++)
        {
            var z = D.Zeitraster.Zeit(s);
            var werte = new object[2 + D.Zeitraster.TageProWoche];
            werte[0] = s;
            werte[1] = z != null ? $"{z.Beginn:H\\:mm} - {z.Ende:H\\:mm}" : "";
            for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
            {
                var n = faelle.Count(f => f.Stunde == s && f.Tag == t);
                werte[2 + t] = n == 0 ? "" : (100.0 * n / summe).ToString("0.0", Format.De) + " %";
            }
            _woche.Rows.Add(werte);
        }
    }

    private void WocheZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 2 || e.Value is not string { Length: > 0 } text) return;
        e.PaintBackground(e.CellBounds, false);
        var prozent = double.TryParse(text.Replace(" %", ""), System.Globalization.NumberStyles.Float, Format.De, out var p) ? p : 0;
        var r = e.CellBounds;
        var breite = Math.Max(2, (int)(Math.Min(prozent, 20) / 20 * 8));
        using (var b = new SolidBrush(Color.FromArgb(160, 20, 30))) e.Graphics!.FillRectangle(b, r.Left + 2, r.Top + 2, breite, r.Height - 5);
        TextRenderer.DrawText(e.Graphics!, text, Font, new Rectangle(r.Left + breite + 4, r.Top, r.Width - breite - 4, r.Height), Color.FromArgb(180, 30, 40), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.Handled = true;
    }

    // --- Lehrerübersicht ------------------------------------------------------------------------------

    private void LehrerFuellen()
    {
        _lehrer.Rows.Clear();
        var faelle = Gefiltert().ToList();
        foreach (var z in Statistik.Lehreruebersicht(D, Von, Bis, faelle).OrderBy(z => z.Lehrer.Kurzname, StringComparer.Create(Format.De, true)))
        {
            static string N(int x) => x == 0 ? "" : x.ToString();
            var r = _lehrer.Rows.Add(z.Lehrer.Kurzname, N(z.Absenzen), N(z.Entfalltage), N(z.Entfall), N(z.Vertretungen));
            _lehrer.Rows[r].Tag = z.Lehrer.Id;
        }
    }

    // --- Absenzgründe ---------------------------------------------------------------------------------

    private void GruendeFuellen()
    {
        _gruende.Rows.Clear();
        var nicht = Gefiltert(false).Where(f => f.Status != Statistik.FallStatus.Planmaessig).ToList();
        var gruppen = nicht.GroupBy(f => f.GrundId ?? 0).OrderByDescending(g => g.Count()).ToList();
        var summe = Math.Max(1, nicht.Count);
        var stuecke = new List<TortenGrafik.Stueck>();
        var i = 0;
        foreach (var g in gruppen)
        {
            var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == g.Key);
            var farbe = grund is { Hintergrund: not 0 } ? Ui.Farbe(grund.Hintergrund & 0xFFFFFF, TortenGrafik.Palette[i % TortenGrafik.Palette.Length]) : TortenGrafik.Palette[i % TortenGrafik.Palette.Length];
            i++;
            var kurz = grund?.Kurzname ?? "Kein Grund";
            stuecke.Add(new TortenGrafik.Stueck(g.Key, kurz, g.Count(), farbe));
            var r = _gruende.Rows.Add(g.Count(), $"{100.0 * g.Count() / summe:0} %", kurz, grund?.Langname ?? "Kein Grund", grund?.EntfallZaehlen == true ? "✔" : "", grund?.FreisetzungNichtZaehlen == true ? "✔" : "");
            _gruende.Rows[r].Tag = g.Key;
            _gruende.Rows[r].Cells["G"].Tag = farbe;
            if (_grundFilter == g.Key) _gruende.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 200);
        }
        _torte.Setzen(stuecke);
    }

    private void GrundFarbeZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _gruende.Columns[e.ColumnIndex].Name != "G" || _gruende.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not Color farbe) return;
        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        var r = e.CellBounds;
        using (var b = new SolidBrush(farbe)) e.Graphics!.FillRectangle(b, r.Left + 3, r.Top + 4, 26, r.Height - 8);
        TextRenderer.DrawText(e.Graphics!, e.Value?.ToString(), Font, new Rectangle(r.Left + 32, r.Top, r.Width - 32, r.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.Handled = true;
    }

    // --- Detailansicht: Überblick der Entfälle und Vertretungen ---------------------------------------

    private void UeberblickFuellen()
    {
        _entfaelle.Rows.Clear();
        _vertretungen.Rows.Clear();
        var ix = Ix;
        var lehrer = _detailTyp == ElementTyp.Lehrer ? D.Lehrer.FirstOrDefault(l => l.Id == _detailId) : null;
        var zaehlt = new Dictionary<Vertretung, bool>(ReferenceEqualityComparer.Instance);
        if (lehrer != null)
            foreach (var z in Zaehlung.Lehrer(D, lehrer, Von, Bis).Zeilen.Where(z => z.Vertretung != null)) zaehlt[z.Vertretung!] = z.Zaehlt;
        string Tag(DateOnly t) => $"{t.Day:00}.{t.Month:00}.";
        void Zeile(DataGridView g, Vertretung v, int stunde, IReadOnlyList<int> klassen, int? fach, bool zaehlen)
        {
            var r = g.Rows.Add(Tag(v.Datum), stunde, Vertretungsplan.ArtText(D, v.Art), v.Text, ix.K(ElementTyp.Lehrer, v.VertreterId), ix.KKlassen(klassen), ix.KFach(fach),
                ix.K(ElementTyp.Raum, v.VertretungsraumId ?? v.RaumId), zaehlen ? "✔" : "");
            g.Rows[r].Tag = v;
        }
        // Entfälle gruppiert nach Absenzen / Veranstaltungen
        var nicht = Gefiltert().Where(f => f.Status != Statistik.FallStatus.Planmaessig && f.Vertretung != null).OrderBy(f => f.Datum).ThenBy(f => f.Stunde).ToList();
        foreach (var gruppe in nicht.GroupBy(f => (f.Vertretung!.AbwesenheitId, f.Vertretung.VeranstaltungId, Ohne: f.Vertretung.AbwesenheitId is null && f.Vertretung.VeranstaltungId is null ? f.Vertretung.GrundId : null)))
        {
            var erste = gruppe.First();
            var kopf = _entfaelle.Rows.Add(Tag(erste.Datum), "", erste.Vertretung!.GrundId is int g ? GrundName(g) : erste.Vertretung.Grund is { Length: > 0 } gt ? gt : "Kein Grund", "", "", "", "", "", "");
            _entfaelle.Rows[kopf].DefaultCellStyle.BackColor = Abschnitt.Leiste;
            _entfaelle.Rows[kopf].DefaultCellStyle.Font = Ui.SchriftFett;
            foreach (var f in gruppe) Zeile(_entfaelle, f.Vertretung!, f.Stunde, f.KlassenIds, f.FachId, zaehlt.TryGetValue(f.Vertretung!, out var zz) && zz);
        }
        // Vertretungen: vom Lehrer gehaltene Vertretungen bzw. vertretener Unterricht der Klasse
        var vertr = _detailTyp == ElementTyp.Lehrer
            ? D.Vertretungen.Where(v => v.Datum >= Von && v.Datum <= Bis && v.VertreterId == _detailId && v.LehrerId != _detailId && !v.IstEntfall && v.Art != VertretungsArt.Raumvertretung).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList()
            : nicht.Where(f => f.Status == Statistik.FallStatus.Vertretung).Select(f => f.Vertretung!).ToList();
        foreach (var v in vertr) Zeile(_vertretungen, v, v.Stunde, v.NeueKlassenIds, v.VertretungsfachId ?? v.FachId, zaehlt.TryGetValue(v, out var zz) && zz);
        var nEntf = nicht.Count;
        _ueberblick.TabPages[0].Text = $"Entfälle ({nEntf})";
        _ueberblick.TabPages[1].Text = $"Vertretungen ({vertr.Count})";
        _ueberblickSumme.Text = lehrer != null
            ? $" {nEntf} nicht gehaltene Einheiten, davon zählen {nicht.Count(f => zaehlt.TryGetValue(f.Vertretung!, out var z) && z)} · {vertr.Count} Vertretungen, davon zählen {vertr.Count(v => zaehlt.TryGetValue(v, out var z) && z)}"
            : $" {nEntf} nicht gehaltene Einheiten, davon {nicht.Count(f => f.Status == Statistik.FallStatus.Vertretung)} vertreten";
    }

    // --- Druck / Export -------------------------------------------------------------------------------

    public void Drucken(bool vorschau)
    {
        if (_reiter.SelectedTab == _seiteBerichte) { BerichtAusgeben(vorschau ? Ausgabe.Seitenansicht : Ausgabe.Druck); return; }
        // Handbuch „Druck“: Elemente der aktiven Seite wählen (Details), Zeitraum
        using var f = new Dialog("Details", 300, 230);
        f.Ok.Text = "Ok";
        var haken = new List<(CheckBox, Abschnitt)>();
        var y = 10;
        foreach (var (text, a) in new[] { ("Lehrerübersicht", _aLehrer), ("Stundenverteilung", _aStunden), ("Wochenverteilung", _aWoche), ("Zeitbereich", _aZeitraum), ("Absenzgründe", _aGruende) })
        {
            if (_detailTyp != null && a == _aLehrer) continue;
            var c = new CheckBox { Text = text, Checked = true, Left = 12, Top = y, AutoSize = true };
            f.Controls.Add(c);
            haken.Add((c, a));
            y += 26;
        }
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var teile = haken.Where(h => h.Item1.Checked).Select(h => h.Item2).ToList();
        var titel = $"Vertretungsstatistik {Format.Datum(Von)} - {Format.Datum(Bis)}" + (_detailTyp is { } t ? $" – {Ix.K(t, _detailId)}" : "");
        var sb = new System.Text.StringBuilder($"<div class=\"pg\"><h1>{H(titel)}</h1><div class=\"sub\">{H(D.Einstellungen.Schulname)}</div>");
        foreach (var a in teile)
        {
            if (a == _aZeitraum)
            {
                var faelle = Gefiltert().ToList();
                sb.Append($"<h2>Zeitbereich</h2><p>{faelle.Count(x => x.Status == Statistik.FallStatus.Planmaessig)} Planmäßig · {faelle.Count(x => x.Status == Statistik.FallStatus.Vertretung)} Vertretungen · {faelle.Count(x => x.Status == Statistik.FallStatus.Entfall)} Entfall · {faelle.Count} Gesamt</p>");
                continue;
            }
            var g = a == _aLehrer ? _lehrer : a == _aStunden ? _stunden : a == _aWoche ? _woche : _gruende;
            sb.Append($"<h2>{H(a.Titel)}</h2>").Append(StatistikBerichte.Tabelle(g));
        }
        if (_detailTyp != null) sb.Append("<h2>Entfälle</h2>").Append(StatistikBerichte.Tabelle(_entfaelle)).Append("<h2>Vertretungen</h2>").Append(StatistikBerichte.Tabelle(_vertretungen));
        sb.Append("</div>");
        StatistikBerichte.Oeffnen(titel, sb.ToString());
    }

    private static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private void Exportieren()
    {
        if (_reiter.SelectedTab == _seiteBerichte) { BerichtAusgeben(Ausgabe.Export); return; }
        var g = _detailTyp != null ? (_ueberblick.SelectedIndex == 0 ? _entfaelle : _vertretungen) : _lehrer;
        StatistikBerichte.CsvSpeichern(this, g, $"Vertretungsstatistik_{Von:yyyyMMdd}-{Bis:yyyyMMdd}.csv");
    }

    // ================================================================================================
    // Karteikarte „Berichte“
    // ================================================================================================

    private enum Ausgabe { Seitenansicht, Druck, Export, Excel }

    private readonly TreeView _berichte = new() { Dock = DockStyle.Fill, ShowLines = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly DateTimePicker _bVon = new() { Format = DateTimePickerFormat.Short, Left = 10, Top = 18, Width = 105 };
    private readonly DateTimePicker _bBis = new() { Format = DateTimePickerFormat.Short, Left = 122, Top = 18, Width = 105 };
    private readonly RadioButton _bKlassen = new() { Text = "Klassen", Left = 10, Top = 18, AutoSize = true, Checked = true };
    private readonly RadioButton _bLehrer = new() { Text = "Lehrer", Left = 10, Top = 38, AutoSize = true };
    private readonly RadioButton _bEntfaelle = new() { Text = "Entfälle", Left = 6, Top = 6, AutoSize = true };
    private readonly RadioButton _bVertretungen = new() { Text = "Vertretungen", Left = 6, Top = 26, AutoSize = true };
    private readonly RadioButton _bUnterricht = new() { Text = "Unterricht", Left = 6, Top = 46, AutoSize = true, Checked = true };
    private readonly ListBox _bElemente = new() { Left = 200, Top = 14, Width = 120, Height = 136, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly ComboBox[] _bGliederung = { Combo(), Combo(), Combo() };
    private readonly ListBox _bGruende = new() { Left = 200, Top = 20, Width = 120, Height = 104, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly ListBox _bFaecher = new() { Left = 330, Top = 20, Width = 110, Height = 104, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly CheckBox _bJeElement = new() { Text = "Vertretungsstatistik je Lehrer / Klasse", Left = 10, Top = 130, AutoSize = true, Checked = true };
    private readonly CheckBox _bPersonalnummer = new() { Text = "Export mit Personalnummer", Left = 10, Top = 152, AutoSize = true };
    private readonly CheckBox _bMatrix = new() { Text = "Matrixdruck: Fach / Grund", Left = 10, Top = 174, AutoSize = true };
    private readonly DataGridView _bErgebnis = Raster();
    private bool _bLaedt;

    private static readonly string[] GliederungUnterricht = { "keine", "Fach", "Klasse", "Lehrer" };
    private static readonly string[] GliederungFaelle = { "keine", "Fach", "Grund", "Art d. Entfalls", "Statistik-Kennzeichen", "Klasse", "Lehrer" };

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };

    private void BerichteAufbauen()
    {
        var split = new SplitContainer { Width = 1000, Height = 600, SplitterDistance = 200, Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        var bilder = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        bilder.Images.Add(Symbole.Get("drucken", 16));
        _berichte.ImageList = bilder;
        foreach (var b in StatistikBerichte.Namen) _berichte.Nodes.Add(b, b);
        _berichte.NodeMouseDoubleClick += (_, e) => StatistikBerichte.Starten(this, e.Node.Text, DateOnly.FromDateTime(_bVon.Value), DateOnly.FromDateTime(_bBis.Value));
        _berichte.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && _berichte.SelectedNode is { } n) StatistikBerichte.Starten(this, n.Text, DateOnly.FromDateTime(_bVon.Value), DateOnly.FromDateTime(_bBis.Value)); };
        split.Panel1.Controls.Add(_berichte);
        split.Panel1.Controls.Add(new Label { Text = "Doppelklick öffnet den Bericht", Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.DimGray });

        var rechts = new Panel { Dock = DockStyle.Top, Height = 470, BackColor = Abschnitt.Grund };
        var gZeit = new GroupBox { Text = "Von-Bis", Left = 8, Top = 4, Width = 460, Height = 50 };
        gZeit.Controls.AddRange(new Control[] { _bVon, _bBis });
        var gAuswahl = new GroupBox { Text = "Auswahl", Left = 8, Top = 58, Width = 460, Height = 160 };
        gAuswahl.Controls.AddRange(new Control[] { _bKlassen, _bLehrer, _bElemente });
        var art = new Panel { Left = 4, Top = 78, Width = 180, Height = 72 };
        art.Controls.AddRange(new Control[] { _bEntfaelle, _bVertretungen, _bUnterricht });
        gAuswahl.Controls.Add(art);
        var gGliederung = new GroupBox { Text = "Gliederung je Lehrer / Klasse", Left = 8, Top = 222, Width = 460, Height = 204 };
        for (var i = 0; i < 3; i++)
        {
            gGliederung.Controls.Add(new Label { Text = (i + 1).ToString(), Left = 10, Top = 24 + i * 30, Width = 14 });
            _bGliederung[i].Left = 28;
            _bGliederung[i].Top = 20 + i * 30;
            gGliederung.Controls.Add(_bGliederung[i]);
        }
        gGliederung.Controls.Add(new Label { Text = "Absenzgrund", Left = 200, Top = 4, AutoSize = true });
        gGliederung.Controls.Add(new Label { Text = "Fach", Left = 330, Top = 4, AutoSize = true });
        gGliederung.Controls.AddRange(new Control[] { _bGruende, _bFaecher, _bJeElement, _bPersonalnummer, _bMatrix });
        _bGruende.Top = _bFaecher.Top = 20;
        var knoepfe = new FlowLayoutPanel { Left = 8, Top = 432, Width = 470, Height = 32 };
        foreach (var (text, a) in new[] { ("Seitenansicht", Ausgabe.Seitenansicht), ("Druck", Ausgabe.Druck), ("Export", Ausgabe.Export), ("Excel", Ausgabe.Excel) })
        {
            var b = new Button { Text = text, Width = 100, Height = 25 };
            var ausgabe = a;
            b.Click += (_, _) => BerichtAusgeben(ausgabe);
            knoepfe.Controls.Add(b);
        }
        rechts.Controls.AddRange(new Control[] { gZeit, gAuswahl, gGliederung, knoepfe });
        _bErgebnis.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_bErgebnis);
        split.Panel2.Controls.Add(rechts);
        _seiteBerichte.Controls.Add(split);

        _bVon.Value = _von.Value;
        _bBis.Value = _bis.Value;
        foreach (var g in Stammdaten.Sortiert(D.Abwesenheitsgruende)) _bGruende.Items.Add(new Eintrag(g.Id, g.Kurzname));
        foreach (var f in Stammdaten.Sortiert(D.Faecher)) _bFaecher.Items.Add(new Eintrag(f.Id, f.Kurzname));
        GliederungSetzen();
        ElementeSetzen();
        foreach (var r in new[] { _bKlassen, _bLehrer }) r.CheckedChanged += (_, _) => { if (r.Checked) { ElementeSetzen(); BerichtNeuaufbau(); } };
        foreach (var r in new[] { _bEntfaelle, _bVertretungen, _bUnterricht }) r.CheckedChanged += (_, _) => { if (r.Checked) { GliederungSetzen(); BerichtNeuaufbau(); } };
        foreach (var c in _bGliederung) c.SelectedIndexChanged += (_, _) => BerichtNeuaufbau();
        foreach (var l in new[] { _bElemente, _bGruende, _bFaecher }) l.SelectedIndexChanged += (_, _) => BerichtNeuaufbau();
        _bJeElement.CheckedChanged += (_, _) => BerichtNeuaufbau();
        _bVon.ValueChanged += (_, _) => BerichtNeuaufbau();
        _bBis.ValueChanged += (_, _) => BerichtNeuaufbau();
        _reiter.SelectedIndexChanged += (_, _) => { if (_reiter.SelectedTab == _seiteBerichte) BerichtNeuaufbau(); };
    }

    private sealed record Eintrag(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    private void ElementeSetzen()
    {
        _bLaedt = true;
        _bElemente.Items.Clear();
        foreach (var el in _bLehrer.Checked ? Stammdaten.Sortiert(D.Lehrer).Cast<Element>() : Stammdaten.Sortiert(D.Klassen)) _bElemente.Items.Add(new Eintrag(el.Id, el.Kurzname));
        _bLaedt = false;
    }

    private void GliederungSetzen()
    {
        _bLaedt = true;
        var liste = _bUnterricht.Checked ? GliederungUnterricht : GliederungFaelle;
        for (var i = 0; i < 3; i++)
        {
            var alt = _bGliederung[i].SelectedItem as string;
            _bGliederung[i].Items.Clear();
            _bGliederung[i].Items.AddRange(liste.Select(x => x == "Art d. Entfalls" && _bVertretungen.Checked ? "Art d. Vertretung" : x).Cast<object>().ToArray());
            var idx = alt is null ? -1 : _bGliederung[i].Items.IndexOf(alt);
            _bGliederung[i].SelectedIndex = idx >= 0 ? idx : i == 0 ? 1 : 0;
        }
        _bLaedt = false;
    }

    /// <summary>Ergebnis der Vertretungsstatistik: Blöcke je Element (oder gesamt) mit Kopf, Zeilen und Summenzeile.</summary>
    private sealed record Block(string Titel, string[] Kopf, List<string[]> Zeilen);

    private List<Block> BerichtDaten()
    {
        var von = DateOnly.FromDateTime(_bVon.Value);
        var bis = DateOnly.FromDateTime(_bBis.Value);
        if (bis < von) return new();
        var ix = Ix;
        var lehrerSicht = _bLehrer.Checked;
        var gewaehlt = _bElemente.SelectedItems.Cast<Eintrag>().Select(e => e.Id).ToHashSet();
        var elemente = _bElemente.Items.Cast<Eintrag>().Where(e => gewaehlt.Count == 0 || gewaehlt.Contains(e.Id)).ToList();
        var gruende = _bGruende.SelectedItems.Cast<Eintrag>().Select(e => e.Id).ToHashSet();
        var faecher = _bFaecher.SelectedItems.Cast<Eintrag>().Select(e => e.Id).ToHashSet();
        var gliederung = _bGliederung.Select(c => c.SelectedItem as string ?? "keine").Where(x => x != "keine").ToList();
        var faelle = (von, bis) == (Von, Bis) ? Faelle() : Statistik.Faelle(D, von, bis);
        bool FachOk(int? f) => faecher.Count == 0 || f is int x && faecher.Contains(x);
        bool GrundOk(Vertretung? v) => gruende.Count == 0 || v?.GrundId is int g && gruende.Contains(g);
        string Schluessel(string art, Statistik.Fall? f, Vertretung? v) => art switch
        {
            "Fach" => ix.KFach(f?.FachId ?? v?.VertretungsfachId ?? v?.FachId),
            "Grund" => v?.GrundId is int g ? ix.Gruende.GetValueOrDefault(g)?.Kurzname ?? "" : v is { AbwesenheitId: null, VeranstaltungId: null } ? "spontaner Entfall" : "Kein Grund",
            "Art d. Entfalls" or "Art d. Vertretung" => v is null ? "" : v.IstEntfall ? "Entfall" : Vertretungsplan.ArtText(D, v.Art),
            "Statistik-Kennzeichen" => v?.Statistik ?? "",
            "Klasse" => ix.KKlassen(f?.KlassenIds ?? v?.NeueKlassenIds ?? new List<int>()),
            "Lehrer" => ix.K(ElementTyp.Lehrer, f?.LehrerId ?? v?.LehrerId),
            _ => "",
        };
        var bloecke = new List<Block>();
        var gruppenEinzeln = _bJeElement.Checked ? elemente.Select(e => new[] { e }).ToList() : new List<Eintrag[]> { elemente.ToArray() };
        foreach (var gruppe in gruppenEinzeln)
        {
            var ids = gruppe.Select(e => e.Id).ToHashSet();
            bool Betrifft(Statistik.Fall f) => lehrerSicht ? f.LehrerId is int l && ids.Contains(l) : f.KlassenIds.Any(ids.Contains);
            var titel = _bJeElement.Checked && gruppe.Length == 1 ? $"{gruppe[0].Text}  {ix.Element(lehrerSicht ? ElementTyp.Lehrer : ElementTyp.Klasse, gruppe[0].Id)?.Langname}" : "Gesamt";
            if (_bUnterricht.Checked)
            {
                var eigene = faelle.Where(f => Betrifft(f) && FachOk(f.FachId)).ToList();
                if (eigene.Count == 0) continue;
                // zusätzlich gehaltene Vertretungen (Lehrer als Vertreter, Klasse: Sondereinsätze)
                var plus = D.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis && !v.IstEntfall && v.Art != VertretungsArt.Raumvertretung &&
                    (lehrerSicht ? v.VertreterId is int vt && ids.Contains(vt) && v.LehrerId != vt : v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung && v.NeueKlassenIds.Any(ids.Contains))).ToList();
                var kopf = gliederung.Concat(new[] { "gehalten", "geplant", "Vertretungen", "Entfälle", "Betreuung", "+ Vertr.", "% gehalten" }).ToArray();
                var zeilen = new List<string[]>();
                string[] Werte(List<Statistik.Fall> l, int plusAnzahl)
                {
                    var geplant = l.Count;
                    var vertr = l.Count(f => f.Status == Statistik.FallStatus.Vertretung);
                    var entf = l.Count(f => f.Status == Statistik.FallStatus.Entfall);
                    var betr = l.Count(f => f.Vertretung?.Art == VertretungsArt.Betreuung);
                    var gehalten = geplant - entf - (lehrerSicht ? vertr : 0) + plusAnzahl;
                    return new[] { gehalten.ToString(), geplant.ToString(), vertr.ToString(), entf.ToString(), betr.ToString(), plusAnzahl.ToString(), geplant == 0 ? "" : (100.0 * gehalten / geplant).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) };
                }
                foreach (var g in eigene.GroupBy(f => string.Join("\u0001", gliederung.Select(a => Schluessel(a, f, f.Vertretung)))).OrderBy(g => g.Key, StringComparer.Create(Format.De, true)))
                {
                    var plusG = gliederung.Count == 0 ? plus.Count : plus.Count(v => string.Join("\u0001", gliederung.Select(a => Schluessel(a, null, v))) == g.Key);
                    zeilen.Add(g.Key.Split('\u0001').Take(gliederung.Count).Concat(Werte(g.ToList(), plusG)).ToArray());
                }
                zeilen.Add(new[] { "Summe" }.Concat(Enumerable.Repeat("", Math.Max(0, gliederung.Count - 1))).Take(gliederung.Count).Concat(Werte(eigene, plus.Count)).ToArray());
                bloecke.Add(new Block(titel, kopf, zeilen));
            }
            else
            {
                var liste = _bEntfaelle.Checked
                    ? faelle.Where(f => Betrifft(f) && f.Status != Statistik.FallStatus.Planmaessig && FachOk(f.FachId) && GrundOk(f.Vertretung)).Select(f => (Fall: (Statistik.Fall?)f, V: f.Vertretung)).ToList()
                    : (lehrerSicht
                        ? D.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis && v.VertreterId is int vt && ids.Contains(vt) && v.LehrerId != vt && !v.IstEntfall && v.Art != VertretungsArt.Raumvertretung
                                                    && FachOk(v.VertretungsfachId ?? v.FachId) && GrundOk(v)).Select(v => (Fall: (Statistik.Fall?)null, V: (Vertretung?)v))
                        : faelle.Where(f => Betrifft(f) && f.Status == Statistik.FallStatus.Vertretung && FachOk(f.FachId) && GrundOk(f.Vertretung)).Select(f => (Fall: (Statistik.Fall?)f, V: f.Vertretung))).ToList();
                if (liste.Count == 0) continue;
                var kopf = new[] { "Anzahl" }.Concat(gliederung).ToArray();
                var zeilen = liste.GroupBy(x => string.Join("\u0001", gliederung.Select(a => Schluessel(a, x.Fall, x.V)))).OrderByDescending(g => g.Count())
                    .Select(g => new[] { g.Count().ToString() }.Concat(g.Key.Split('\u0001').Take(gliederung.Count)).ToArray()).ToList();
                zeilen.Add(new[] { liste.Count.ToString(), "Summe" }.Concat(Enumerable.Repeat("", Math.Max(0, gliederung.Count - 1))).Take(kopf.Length).ToArray());
                bloecke.Add(new Block(titel, kopf, zeilen));
            }
        }
        return bloecke;
    }

    private string BerichtTitel => $"{(_bUnterricht.Checked ? "Unterricht" : _bEntfaelle.Checked ? "Entfälle" : "Vertretungen")}  {Format.Datum(DateOnly.FromDateTime(_bVon.Value))} - {Format.Datum(DateOnly.FromDateTime(_bBis.Value))}";

    private void BerichtNeuaufbau()
    {
        if (_bLaedt || IsDisposed || _reiter.SelectedTab != _seiteBerichte) return;
        var bloecke = BerichtDaten();
        _bErgebnis.Rows.Clear();
        _bErgebnis.Columns.Clear();
        var breite = bloecke.Count == 0 ? 1 : bloecke.Max(b => b.Kopf.Length);
        for (var i = 0; i < breite; i++) _bErgebnis.Columns[_bErgebnis.Columns.Add("C" + i, "")].Width = 90;
        foreach (var b in bloecke)
        {
            var t = _bErgebnis.Rows.Add(b.Titel);
            _bErgebnis.Rows[t].DefaultCellStyle.Font = Ui.SchriftFett;
            _bErgebnis.Rows[t].DefaultCellStyle.BackColor = Abschnitt.Leiste;
            var k = _bErgebnis.Rows.Add(b.Kopf.Cast<object>().ToArray());
            _bErgebnis.Rows[k].DefaultCellStyle.Font = Ui.SchriftFett;
            foreach (var z in b.Zeilen)
            {
                var r = _bErgebnis.Rows.Add(z.Cast<object>().ToArray());
                if (z.Contains("Summe")) _bErgebnis.Rows[r].DefaultCellStyle.Font = Ui.SchriftFett;
            }
        }
        if (bloecke.Count == 0) _bErgebnis.Rows.Add("Keine Daten im gewählten Zeitraum.");
    }

    private void BerichtAusgeben(Ausgabe ausgabe)
    {
        var bloecke = BerichtDaten();
        if (bloecke.Count == 0) { Ui.Info("Keine Daten im gewählten Zeitraum.", "Vertretungsstatistik"); return; }
        var lehrerSicht = _bLehrer.Checked;
        if (ausgabe is Ausgabe.Export or Ausgabe.Excel)
        {
            var zeilen = new List<string[]>();
            foreach (var b in bloecke)
            {
                var kopf = new[] { lehrerSicht ? "Lehrer" : "Klasse" }.Concat(_bPersonalnummer.Checked && lehrerSicht ? new[] { "Personalnummer" } : Array.Empty<string>()).Concat(b.Kopf).ToArray();
                if (zeilen.Count == 0) zeilen.Add(kopf);
                var kurz = b.Titel.Split(' ')[0];
                var pnr = D.Lehrer.FirstOrDefault(l => l.Kurzname == kurz)?.Personalnummer ?? "";
                foreach (var z in b.Zeilen) zeilen.Add(new[] { kurz }.Concat(_bPersonalnummer.Checked && lehrerSicht ? new[] { pnr } : Array.Empty<string>()).Concat(z).ToArray());
            }
            string pfad;
            if (ausgabe == Ausgabe.Excel) pfad = Path.Combine(Path.GetTempPath(), $"Vertretungsstatistik_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            else
            {
                using var sfd = new SaveFileDialog { Filter = "CSV-Datei|*.csv", FileName = "Vertretungsstatistik.csv" };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                pfad = sfd.FileName;
            }
            File.WriteAllText(pfad, Csv.Schreiben(zeilen, bom: false), new System.Text.UTF8Encoding(true));
            if (ausgabe == Ausgabe.Excel) HauptFenster.Starten(pfad);
            return;
        }
        if (_bMatrix.Checked && !_bUnterricht.Checked)
        {
            StatistikBerichte.Oeffnen(BerichtTitel, StatistikBerichte.Matrix(BerichtTitel, bloecke.Select(b => (b.Titel, b.Kopf, b.Zeilen)).ToList()));
            return;
        }
        var druck = new List<string[]>();
        var breite = bloecke.Max(b => b.Kopf.Length);
        string[] Auffuellen(IEnumerable<string> x) => x.Concat(Enumerable.Repeat("", breite)).Take(breite).ToArray();
        foreach (var b in bloecke)
        {
            druck.Add(Auffuellen(new[] { b.Titel }));
            druck.Add(Auffuellen(b.Kopf));
            druck.AddRange(b.Zeilen.Select(Auffuellen));
            druck.Add(Auffuellen(Array.Empty<string>()));
        }
        ListenDruck.Drucken(this, BerichtTitel, Enumerable.Repeat("", breite).ToArray(), Enumerable.Repeat(90, breite).ToArray(), druck, ausgabe == Ausgabe.Seitenansicht);
    }
}
