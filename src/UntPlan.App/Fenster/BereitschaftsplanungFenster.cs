using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Bereitschaftsplanung wie im Handbuch (Fenster „Bereitschaften“): oben Bereitschaftsfach, Bereitschaft (1, 2 … – die wievielte
/// Bereitschaft einer Stunde das Raster zeigt), „Anzahl Bereitsch. anzeigen“ und „Doppelstundenmodell“, darunter das Wochenraster.
/// Mit Haken zeigt und ändert das Raster die Anzahl der nötigen Bereitschaften je Stunde, ohne Haken die eingeteilten Lehrkräfte
/// („???“ = fehlt; Kurzname direkt eintragbar). Werkzeugleiste: Lehrer-Vorschlag (eigenes Fenster mit „Übernahme“), Löschen,
/// Automatik, Bereitschaften in Unterricht übertragen, Aktualisieren.
/// </summary>
public sealed class BereitschaftsplanungFenster : MdiFenster, IDruckbar
{
    // Maße nach dem Handbuch-Bild (auf 96 dpi umgerechnet): Fenster 461 × 305, Raster ab y = 103, Zeilen 15 px, Tagesspalten 67 px
    private readonly DataGridView _raster = new() { Left = 6, Top = 103, Width = 421, Height = 160, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly ComboBox _fach = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 20, Top = 33, Width = 72 };
    private readonly NumericUpDown _nummer = new() { Minimum = 1, Maximum = 20, Value = 1, Left = 20, Top = 56, Width = 43 };
    private readonly CheckBox _anzahl = new() { Text = "Anzahl Bereitsch. anzeigen", AutoSize = true, Left = 20, Top = 83 };
    private readonly CheckBox _doppelt = new() { Text = "Doppelstundenmodell", AutoSize = true, Left = 232, Top = 83 };
    private (int Tag, int Stunde)? _gewaehlt;
    private bool _laedt;
    private VorschlagFenster? _vorschlag;

    public BereitschaftsplanungFenster()
    {
        Text = "Bereitschaften";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("bereitschaft");
        Size = new Size(461, 305);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Lehrer-Vorschlag", "lehrer", VorschlagOeffnen));
        ts.Items.Add(Knopf("Löschen", "loeschen", Entfernen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Automatik", "optimierung", Automatisch));
        ts.Items.Add(Knopf("Bereitschaften in Unterricht übertragen", "unterricht", InUnterrichtUebertragen));
        ts.Items.Add(Knopf("Aktualisieren", "aktualisieren", Neuaufbau));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Bedarf für alle Vormittagsstunden setzen", "bereitschaft", BedarfVormittag));
        ts.Items.Add(Knopf("Alle Bereitschaften entfernen", "minus", AlleEntfernen));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));

        var flaeche = new Panel { Dock = DockStyle.Fill };
        _fach.Format += (_, e) => e.Value = e.ListItem is Fach f ? f.Kurzname : e.ListItem?.ToString();
        FaecherLaden();
        _fach.SelectionChangeCommitted += (_, _) => Neuaufbau();
        _nummer.ValueChanged += (_, _) => { if (!_laedt) Neuaufbau(); };
        _doppelt.Checked = D.Einstellungen.BereitschaftNurDoppelstunden;
        _doppelt.CheckedChanged += (_, _) =>
        {
            if (_doppelt.Checked != D.Einstellungen.BereitschaftNurDoppelstunden)
                Aendern("Bereitschaften nur als Doppelstunden", AenderungsArt.Einstellungen, d => d.Einstellungen.BereitschaftNurDoppelstunden = _doppelt.Checked);
        };
        new ToolTip().SetToolTip(_doppelt, "Die Automatik besetzt Bereitschaften immer paarweise in zwei aufeinanderfolgenden Stunden");
        new ToolTip().SetToolTip(_nummer, "Die wievielte Bereitschaft einer Stunde das Raster zeigt (bei mehreren Bereitschaften je Stunde)");
        _anzahl.CheckedChanged += (_, _) => Neuaufbau();
        flaeche.Controls.AddRange(new Control[]
        {
            _fach, new Label { Text = "Bereitschaftsfach", Left = 95, Top = 37, AutoSize = true },
            _nummer, new Label { Text = "Bereitschaft", Left = 68, Top = 59, AutoSize = true },
            _anzahl, _doppelt, _raster,
        });
        flaeche.Resize += (_, _) => { _raster.Width = Math.Max(100, flaeche.ClientSize.Width - 12); _raster.Height = Math.Max(60, flaeche.ClientSize.Height - _raster.Top - 6); };

        Ui.RasterGestalten(_raster, false);
        _raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _raster.MultiSelect = false;
        _raster.AllowUserToAddRows = false;
        _raster.RowHeadersVisible = false;
        _raster.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _raster.ColumnHeadersHeight = 15;
        _raster.RowTemplate.Height = Ui.ZeilenHoehe();
        _raster.CurrentCellChanged += (_, _) =>
        {
            if (_laedt || _raster.CurrentCell is not { } c || c.ColumnIndex < 1) return;
            _gewaehlt = (c.ColumnIndex - 1, c.RowIndex + 1);
            _vorschlag?.Fuellen();
        };
        _raster.CellEndEdit += ZelleGeaendert;
        _raster.CellFormatting += Formatieren;

        Controls.Add(flaeche);
        Controls.Add(ts);
        FormClosed += (_, _) => _vorschlag?.Close();
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        FaecherLaden();
        Neuaufbau();
    }

    private void FaecherLaden()
    {
        var alt = (_fach.SelectedItem as Fach)?.Id;
        _fach.Items.Clear();
        foreach (var f in Stammdaten.Sortiert(D.Faecher).Where(f => f.Bereitschaft)) _fach.Items.Add(f);
        var idx = _fach.Items.Cast<Fach>().ToList().FindIndex(f => f.Id == alt);
        if (_fach.Items.Count > 0) _fach.SelectedIndex = Math.Max(0, idx);
    }

    /// <summary>Gewähltes Bereitschaftsfach (null = noch keines angelegt; wird beim ersten Eintrag angelegt).</summary>
    private int? FachId => (_fach.SelectedItem as Fach)?.Id;

    private List<Bereitschaftsstunde> Stunden() =>
        Bereitschaftsplanung.Stunden(D).Where(b => FachId is not int f || b.Unterricht.Zeilen.Any(z => z.FachId == f)).ToList();

    private void Neuaufbau()
    {
        _laedt = true;
        var stunden = Stunden();
        _raster.Columns.Clear();
        _raster.Rows.Clear();
        var kopf = _raster.Columns[_raster.Columns.Add("S", "")];
        kopf.Width = 44;
        kopf.ReadOnly = true;
        kopf.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        kopf.SortMode = DataGridViewColumnSortMode.NotSortable;
        _raster.ReadOnly = false;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
        {
            var c = _raster.Columns[_raster.Columns.Add("T" + t, Planungsdienst.Tagname(D, t, true))];
            c.Width = 67;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        var n = (int)_nummer.Value;
        for (var s = 1; s <= D.Zeitraster.StundenProTag; s++)
        {
            var werte = new object[1 + D.Zeitraster.TageProWoche];
            werte[0] = D.Zeitraster.StundenLabel(s);
            for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
            {
                var bedarf = Bereitschaftsplanung.Bedarf(D, t, s);
                if (_anzahl.Checked) { werte[1 + t] = bedarf.ToString(); continue; }
                // die n-te Bereitschaft der Stunde: Kurzname, „???“ wenn laut Bedarf noch offen, sonst leer
                var namen = stunden.Where(b => b.Tag == t && b.Stunde == s).Select(b => b.Lehrer.Kurzname).OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase).ToList();
                werte[1 + t] = n <= namen.Count ? namen[n - 1] : n <= bedarf ? "???" : "";
            }
            _raster.Rows.Add(werte);
        }
        if (_gewaehlt is { } g && g.Stunde - 1 < _raster.Rows.Count && g.Tag + 1 < _raster.Columns.Count)
            _raster.CurrentCell = _raster.Rows[g.Stunde - 1].Cells[g.Tag + 1];
        _laedt = false;
        _vorschlag?.Fuellen();
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 1 || e.CellStyle is null) return;
        if (!D.Zeitraster.Verfuegbar(e.ColumnIndex - 1, e.RowIndex + 1)) e.CellStyle.BackColor = Color.FromArgb(225, 225, 225);
    }

    /// <summary>Eingabe in eine Zelle: mit „Anzahl Bereitsch. anzeigen“ der Bedarf, sonst der Kurzname der Lehrkraft (leer = entfernen).</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || e.ColumnIndex < 1) return;
        var t = e.ColumnIndex - 1;
        var s = e.RowIndex + 1;
        var text = _raster.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        if (_anzahl.Checked)
        {
            if (!int.TryParse(text, out var anzahl)) anzahl = 0;
            anzahl = Math.Clamp(anzahl, 0, 20);
            if (anzahl != Bereitschaftsplanung.Bedarf(D, t, s)) BeginInvoke(() => Aendern("Bereitschaftsbedarf", AenderungsArt.Einstellungen, d => Bereitschaftsplanung.BedarfSetzen(d, t, s, anzahl)));
            else BeginInvoke(Neuaufbau);
            return;
        }
        var bisher = Aktuell(t, s);
        if (text == "???" || string.Equals(text, bisher?.Kurzname, StringComparison.CurrentCultureIgnoreCase)) { BeginInvoke(Neuaufbau); return; }
        Lehrer? neu = null;
        if (text.Length > 0)
        {
            neu = D.Lehrer.FirstOrDefault(l => string.Equals(l.Kurzname, text, StringComparison.CurrentCultureIgnoreCase));
            if (neu is null) { Ui.Info($"Lehrer \"{text}\" ist nicht vorhanden."); BeginInvoke(Neuaufbau); return; }
        }
        var fach = FachId;
        BeginInvoke(() => Aendern($"Bereitschaft {neu?.Kurzname ?? bisher?.Kurzname}", AenderungsArt.Stundenplan, d =>
        {
            if (bisher != null) Bereitschaftsplanung.Entfernen(d, bisher.Id, t, s);
            if (neu != null) Bereitschaftsplanung.Setzen(d, neu.Id, t, s, fach);
        }));
    }

    /// <summary>Lehrkraft der gezeigten (n-ten) Bereitschaft an Tag/Stunde.</summary>
    private Lehrer? Aktuell(int tag, int stunde)
    {
        var n = (int)_nummer.Value;
        var liste = Stunden().Where(b => b.Tag == tag && b.Stunde == stunde).Select(b => b.Lehrer).OrderBy(l => l.Kurzname, StringComparer.CurrentCultureIgnoreCase).ToList();
        return n <= liste.Count ? liste[n - 1] : null;
    }

    /// <summary>Merker wie im Vertretungsvorschlag: Abstand der Bereitschaft zur nächsten eigenen Stunde am Tag (leer = kein Unterricht).</summary>
    private string Merker(Lehrer l, int tag, int stunde)
    {
        var eigene = Ix.PlanVon(ElementTyp.Lehrer, l.Id).Where(p => p.Tag == tag && p.Stunde != stunde).Select(p => Math.Abs(p.Stunde - stunde)).ToList();
        return eigene.Count == 0 ? "" : eigene.Min().ToString();
    }

    private void VorschlagOeffnen()
    {
        if (_vorschlag is { IsDisposed: false }) { _vorschlag.Activate(); return; }
        _vorschlag = new VorschlagFenster(this);
        _vorschlag.FormClosed += (_, _) => _vorschlag = null;
        _vorschlag.Show(FindForm()?.MdiParent ?? (IWin32Window?)FindForm() ?? this);
        _vorschlag.Fuellen();
    }

    private void Eintragen(Lehrer l)
    {
        if (_gewaehlt is not { } g) return;
        var bisher = Aktuell(g.Tag, g.Stunde);
        var fach = FachId;
        Aendern($"Bereitschaft {l.Kurzname}", AenderungsArt.Stundenplan, d =>
        {
            if (bisher != null && bisher.Id != l.Id) Bereitschaftsplanung.Entfernen(d, bisher.Id, g.Tag, g.Stunde);
            Bereitschaftsplanung.Setzen(d, l.Id, g.Tag, g.Stunde, fach);
        });
    }

    private void Entfernen()
    {
        if (_gewaehlt is not { } g || Aktuell(g.Tag, g.Stunde) is not { } l) return;
        if (!Ui.LoeschenBestaetigen(1, "Bereitschaft", "Bereitschaften", l.Kurzname)) return;
        Aendern($"Bereitschaft {l.Kurzname} entfernen", AenderungsArt.Stundenplan, d => Bereitschaftsplanung.Entfernen(d, l.Id, g.Tag, g.Stunde));
    }

    private void Automatisch()
    {
        var n = 0;
        Aendern("Bereitschaften automatisch besetzen", AenderungsArt.Stundenplan, d => n = Bereitschaftsplanung.Automatisch(d));
        var fehlt = D.Bereitschaftsbedarf.Sum(b => Math.Max(0, b.Anzahl - Bereitschaftsplanung.Stunden(D).Count(x => x.Tag == b.Tag && x.Stunde == b.Stunde)));
        Ui.Info($"{n} Bereitschaften wurden eingeteilt.{(fehlt > 0 ? $"\n{fehlt} Bereitschaften konnten nicht besetzt werden (keine freie Lehrkraft bzw. Höchstzahl erreicht)." : "")}", "Bereitschaftsplanung");
    }

    /// <summary>
    /// „Bereitschaften in Unterricht übertragen“: In UntPlan steht jede eingeteilte Bereitschaft bereits als fixierte Stunde eines
    /// Bereitschafts-Unterrichts im Stundenplan; die Funktion meldet deshalb nur, wie viele Bereitschaften je Lehrkraft übertragen sind.
    /// </summary>
    private void InUnterrichtUebertragen()
    {
        var stunden = Stunden();
        var lehrer = stunden.Select(b => b.Lehrer.Id).Distinct().Count();
        Ui.Info($"{stunden.Count} Bereitschaften von {lehrer} Lehrkräften stehen im Unterricht und im Stundenplan.", "Bereitschaften in Unterricht übertragen");
        Neuaufbau();
    }

    private void BedarfVormittag()
    {
        var anzahl = Ui.Eingabe("Bedarf je Vormittagsstunde (Anzahl Lehrkräfte):", "Bereitschaftsbedarf", "1");
        if (anzahl is null || !int.TryParse(anzahl, out var n)) return;
        Aendern("Bereitschaftsbedarf", AenderungsArt.Einstellungen, d =>
        {
            for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
                for (var s = 1; s <= d.Zeitraster.LetzteVormittagsstunde; s++)
                    if (d.Zeitraster.Verfuegbar(t, s)) Bereitschaftsplanung.BedarfSetzen(d, t, s, n);
        });
    }

    private void AlleEntfernen()
    {
        if (!Ui.Frage("Alle Bereitschaften aus dem Stundenplan entfernen?")) return;
        Aendern("Bereitschaften entfernen", AenderungsArt.Stundenplan, d =>
        {
            foreach (var b in Bereitschaftsplanung.Stunden(d)) Bereitschaftsplanung.Entfernen(d, b.Lehrer.Id, b.Tag, b.Stunde);
        });
    }

    public void Drucken(bool vorschau) => ListenDruck.DruckenTabelle(this, "Bereitschaftsplanung", _raster, vorschau);

    /// <summary>
    /// Lehrer-Vorschlag der Bereitschaftsplanung wie im Handbuch: eigenes Fenster mit „Übernahme“ und den Spalten Name, Merker,
    /// Anz.Bereit., Max.Bereit., Punkte, Zeitwünsche (dahinter freies Deputat und Lage); Doppelklick übernimmt die Lehrkraft.
    /// </summary>
    private sealed class VorschlagFenster : Form
    {
        private readonly BereitschaftsplanungFenster _f;
        private readonly DataGridView _g = new() { Left = 6, Top = 33, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };

        public VorschlagFenster(BereitschaftsplanungFenster f)
        {
            _f = f;
            Text = "Lehrer-Vorschlag";
            Font = Ui.Schrift;
            ShowInTaskbar = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(354, 200);
            var uebernahme = new Button { Text = "Übernahme", Left = 18, Top = 9, Width = 132, Height = 21 };
            uebernahme.Click += (_, _) => Uebernehmen();
            Ui.RasterGestalten(_g, false);
            _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _g.MultiSelect = false;
            _g.RowHeadersVisible = false;
            _g.ReadOnly = true;
            _g.AllowUserToAddRows = false;
            _g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _g.ColumnHeadersHeight = 15;
            _g.RowTemplate.Height = Ui.ZeilenHoehe();
            foreach (var (n, k, w) in new[] { ("L", "Name", 38), ("M", "Merker", 41), ("W", "Anz.Bereit.", 60), ("Max", "Max.Bereit.", 61), ("P", "Punkte", 41), ("Z", "Zeitwünsche", 68), ("F", "freies Dep.", 60), ("H", "Lage", 120) })
            {
                var c = _g.Columns[_g.Columns.Add(n, k)];
                c.Width = w;
                if (n is "M" or "W" or "Max" or "P" or "Z" or "F") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            _g.SetBounds(6, 33, ClientSize.Width - 12, ClientSize.Height - 39);
            _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Uebernehmen(); };
            Controls.Add(uebernahme);
            Controls.Add(_g);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.TitelleisteFaerben(this);
        }

        private void Uebernehmen()
        {
            if (_g.CurrentRow?.Tag is Lehrer l) _f.Eintragen(l);
        }

        public void Fuellen()
        {
            _g.Rows.Clear();
            if (_f._gewaehlt is not { } g) { Text = "Lehrer-Vorschlag"; return; }
            var d = _f.D;
            Text = $"Lehrer-Vorschlag: {Planungsdienst.Tagname(d, g.Tag)}, {g.Stunde}. Std.";
            foreach (var k in Bereitschaftsplanung.Vorschlag(d, g.Tag, g.Stunde))
            {
                var zw = k.Lehrer.Zeitwuensche.Get(g.Tag, g.Stunde);
                var r = _g.Rows.Add(k.Lehrer.Kurzname, _f.Merker(k.Lehrer, g.Tag, g.Stunde), k.BereitschaftenWoche, k.Lehrer.BereitschaftenMax, (int)k.Punkte,
                    zw == 0 ? "" : zw.ToString("+0;-0"), k.FreiesDeputat.ToString("+0.#;-0.#;0"), k.Hinweis);
                _g.Rows[r].Tag = k.Lehrer;
                if (zw > 0) _g.Rows[r].Cells["Z"].Style.BackColor = Color.FromArgb(140, 240, 140);
                else if (zw < 0) _g.Rows[r].Cells["Z"].Style.BackColor = Color.FromArgb(255, 190, 190);
            }
        }
    }
}
