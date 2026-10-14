using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Modul Pausenaufsichten: Reiter Gang bzw. Lehrer, je Element eine Matrix Tage × Aufsichtspausen. Zellen: Kurznamen der
/// Aufsichten (geteilte Aufsicht: mehrere), "???" = offen, grau = keine Aufsicht, rot = schlecht bewertet; im Reiter Lehrer
/// grün = möglich, rot = gesperrt. Lehrervorschlag, Aufsicht teilen, keine Aufsicht, Optimierung, Gewichtung, Druck.
/// </summary>
public sealed class PausenaufsichtFenster : MdiFenster, IDruckbar
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly ToolStripComboBox _element = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ToolStripLabel _minuten = new() { ForeColor = Color.DimGray };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _lehrer = new() { Dock = DockStyle.Fill };
    private bool _laedt;
    // eigene Merkvariable: TabControl.SelectedIndex liefert vor dem Erzeugen des Handles -1
    private bool _gangSicht = true;

    public PausenaufsichtFenster()
    {
        Text = "Pausenaufsichten";
        HilfeThema = "Pausenaufsichten";
        Icon = SymbolIcon("pausenaufsicht");
        Size = new Size(1060, 520);
        Positionieren();
        _tabs.TabPages.Add("Gang");
        _tabs.TabPages.Add("Lehrer");
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex < 0 || (_tabs.SelectedIndex == 0) == _gangSicht) return;
            _gangSicht = _tabs.SelectedIndex == 0;
            ElementeFuellen();
            Neuaufbau();
        };
        var ts = Werkzeugleiste();
        ts.Items.Add(_element);
        ts.Items.AddRange(Ui.Durchschalter(_element));
        _element.SelectedIndexChanged += (_, _) => { if (!_laedt) Neuaufbau(); };
        ts.Items.Add(_minuten);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Lehrervorschlag", "lehrer", Lehrervorschlag, false));
        ts.Items.Add(Knopf("Aufsicht löschen", "loeschen", AufsichtLoeschen));
        ts.Items.Add(Knopf("Aufsicht (???) / Keine Aufsicht", "entfall", KeineAufsicht));
        ts.Items.Add(Knopf("Aufsicht teilen", "plus", () => Teilen(+1)));
        ts.Items.Add(Knopf("Teilung entfernen", "minus", () => Teilen(-1)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Optimierung", "optimierung", Optimieren, false));
        ts.Items.Add(Knopf("Gewichtung", "gewichtung", Gewichtung));
        var loeschen = new ToolStripDropDownButton("Löschen", Symbole.Get("loeschen", 16));
        loeschen.DropDownItems.Add("Aufsichten dieses Elements löschen", null, (_, _) => ElementLoeschen());
        loeschen.DropDownItems.Add("Alle Aufsichten löschen", null, (_, _) => { if (Ui.Frage("Alle Pausenaufsichten löschen?")) Aendern("Pausenaufsichten löschen", AenderungsArt.Stundenplan, d => d.Pausenaufsichten.RemoveAll(a => a.Datum is null)); });
        ts.Items.Add(loeschen);
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));

        Ui.RasterGestalten(_g, true);
        _g.RowTemplate.Height = 34;
        _g.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.CellEndEdit += Bearbeitet;
        _g.CellFormatting += Formatieren;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0 && _tabs.SelectedIndex == 0) Lehrervorschlag(); };

        Ui.RasterGestalten(_lehrer, false);
        _lehrer.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("L", "Lehrer", 60), ("Max", "PA Max (Min)", 80), ("Ist", "PA Ist (Min)", 75), ("Anz", "PA Anz", 50), ("V", "PA-Vertr.", 60), ("D", "Ist-Max", 55), ("G", "Geschl.", 50) })
            _lehrer.Columns[_lehrer.Columns.Add(n, k)].Width = b;
        _lehrer.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _lehrer.Rows[e.RowIndex].Tag is not Lehrer l) return;
            _tabs.SelectedIndex = 1;
            _element.SelectedItem = _element.Items.Cast<Eintrag>().FirstOrDefault(x => x.Id == l.Id);
        };
        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_g);
        split.Panel2.Controls.Add(_lehrer);
        Controls.Add(split);
        Controls.Add(_tabs);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, split.Width - 450);
        ElementeFuellen();
        Neuaufbau();
    }

    private sealed record Eintrag(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    private bool GangSicht => _gangSicht;
    private int? ElementId => (_element.SelectedItem as Eintrag)?.Id;
    private List<Pause> Pausen => Pausenaufsichten.AufsichtsPausen(D).ToList();

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void ElementeFuellen()
    {
        _laedt = true;
        var alt = ElementId;
        _element.Items.Clear();
        if (GangSicht) foreach (var g in Stammdaten.Sortiert(D.Gaenge)) _element.Items.Add(new Eintrag(g.Id, g.Kurzname));
        else foreach (var l in Stammdaten.Sortiert(D.Lehrer).Where(l => !l.Ignorieren)) _element.Items.Add(new Eintrag(l.Id, l.Kurzname));
        _element.SelectedItem = _element.Items.Cast<Eintrag>().FirstOrDefault(x => x.Id == alt) ?? _element.Items.Cast<Eintrag>().FirstOrDefault();
        _laedt = false;
    }

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        _laedt = true;
        var pos = (_g.CurrentCell?.RowIndex ?? 0, _g.CurrentCell?.ColumnIndex ?? 1);
        _g.Columns.Clear();
        _g.Rows.Clear();
        var pausen = Pausen;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "", Width = 80, ReadOnly = true });
        foreach (var p in pausen)
        {
            var ende = D.Zeitraster.Zeit(p.NachStunde)?.Ende;
            var beginn = D.Zeitraster.Zeit(p.NachStunde + 1)?.Beginn;
            _g.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "P" + p.NachStunde, HeaderText = $"{Pausenaufsichten.PausenName(D, p.NachStunde)}\n{ende:H\\:mm}-{beginn:H\\:mm}", Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable, ReadOnly = !GangSicht, Tag = p.NachStunde,
            });
        }
        _g.ColumnHeadersHeight = 36;
        _g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        var ix = Ix;
        if (ElementId is int id)
            for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
            {
                var werte = new List<object> { Core.Planung.Planungsdienst.Tagname(D, t) };
                foreach (var p in pausen)
                {
                    if (GangSicht)
                    {
                        var teile = Pausenaufsichten.Teile(D, id, t, p.NachStunde);
                        var namen = Enumerable.Range(1, Math.Max(0, teile)).Select(teil => D.Pausenaufsichten.FirstOrDefault(a => a.GangId == id && a.Tag == t && a.NachStunde == p.NachStunde && a.Teil == teil && a.Datum is null) is { } a ? ix.KLehrer(a.LehrerId) : "???");
                        werte.Add(teile == 0 ? "" : string.Join(", ", namen));
                    }
                    else werte.Add(string.Join(", ", D.Pausenaufsichten.Where(a => a.LehrerId == id && a.Tag == t && a.NachStunde == p.NachStunde && a.Datum is null).Select(a => ix.K(ElementTyp.Gang, a.GangId))));
                }
                _g.Rows[_g.Rows.Add(werte.ToArray())].Tag = t;
            }
        var min = 0;
        var offen = 0;
        if (ElementId is int e2)
        {
            if (GangSicht)
                for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
                    foreach (var p in pausen)
                    {
                        var teile = Pausenaufsichten.Teile(D, e2, t, p.NachStunde);
                        for (var teil = 1; teil <= teile; teil++)
                        {
                            var m = Pausenaufsichten.Minuten(D, e2, t, p.NachStunde);
                            min += m;
                            if (!D.Pausenaufsichten.Any(a => a.GangId == e2 && a.Tag == t && a.NachStunde == p.NachStunde && a.Teil == teil && a.Datum is null)) offen += m;
                        }
                    }
            else min = Pausenaufsichten.IstMinuten(D, e2);
        }
        _minuten.Text = GangSicht ? $"Minuten: {min} (offen: {offen})" : $"Minuten: {min}";
        _lehrer.Rows.Clear();
        foreach (var l in Stammdaten.Sortiert(D.Lehrer).Where(l => !l.Ignorieren))
        {
            var ist = Pausenaufsichten.IstMinuten(D, l.Id);
            var diff = ist - l.PausenaufsichtMaxMinuten;
            var r = _lehrer.Rows.Add(l.Kurzname, l.PausenaufsichtMaxMinuten, ist, Pausenaufsichten.Anzahl(D, l.Id),
                D.Vertretungen.Count(v => v.VertreterId == l.Id && v.Art == VertretungsArt.Pausenaufsicht), diff.ToString("+0;-0;0"), l.Geschlecht);
            _lehrer.Rows[r].Tag = l;
            if (diff > 0) _lehrer.Rows[r].Cells["D"].Style.ForeColor = Color.Firebrick;
        }
        _laedt = false;
        if (pos.Item1 < _g.Rows.Count && pos.Item2 < _g.Columns.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[pos.Item2];
    }

    private (int Tag, int Nach)? Zelle(int zeile, int spalte) =>
        zeile >= 0 && spalte > 0 && _g.Rows[zeile].Tag is int t && _g.Columns[spalte].Tag is int n ? (t, n) : null;

    private (int Tag, int Nach)? Aktuell => _g.CurrentCell is { } c ? Zelle(c.RowIndex, c.ColumnIndex) : null;

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle is null || ElementId is not int id || Zelle(e.RowIndex, e.ColumnIndex) is not { } z) return;
        if (GangSicht)
        {
            var teile = Pausenaufsichten.Teile(D, id, z.Tag, z.Nach);
            if (teile == 0) { e.CellStyle.BackColor = Color.FromArgb(220, 220, 220); return; }
            var liste = D.Pausenaufsichten.Where(a => a.GangId == id && a.Tag == z.Tag && a.NachStunde == z.Nach && a.Datum is null).ToList();
            if (liste.Count < teile) e.CellStyle.ForeColor = Color.Firebrick;
            if (liste.Any(a => Ix.Lehrer.TryGetValue(a.LehrerId, out var l) && Pausenaufsichten.Bewerten(D, Ix, l, id, z.Tag, z.Nach, a).Schlecht))
                e.CellStyle.BackColor = Color.FromArgb(255, 205, 205);
        }
        else if (Ix.Lehrer.TryGetValue(id, out var l))
        {
            var moeglich = Pausenaufsichten.Moeglich(D, Ix, l, z.Tag, z.Nach);
            e.CellStyle.BackColor = !moeglich ? Color.FromArgb(255, 210, 210) : Color.FromArgb(215, 240, 215);
        }
    }

    private void Bearbeitet(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || !GangSicht || ElementId is not int gang || Zelle(e.RowIndex, e.ColumnIndex) is not { } z) return;
        var namen = (_g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Where(n => n != "???").ToList();
        var ids = new List<int>();
        foreach (var n in namen)
        {
            var l = D.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (l is null) { Ui.Info($"Lehrer '{n}' ist nicht bekannt."); BeginInvoke(Neuaufbau); return; }
            ids.Add(l.Id);
        }
        Aendern("Pausenaufsicht", AenderungsArt.Stundenplan, d =>
        {
            d.Pausenaufsichten.RemoveAll(a => a.GangId == gang && a.NachStunde == z.Nach && a.Tag == z.Tag && a.Datum is null);
            if (ids.Count > Pausenaufsichten.Teile(d, gang, z.Tag, z.Nach)) Pausenaufsichten.TeileSetzen(d, gang, z.Tag, z.Nach, ids.Count);
            for (var i = 0; i < ids.Count; i++) d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang, NachStunde = z.Nach, Tag = z.Tag, LehrerId = ids[i], Teil = i + 1 });
        });
    }

    private void Lehrervorschlag()
    {
        if (!GangSicht || ElementId is not int gang || Aktuell is not { } z) { Ui.Info("Bitte im Reiter Gang eine Aufsicht wählen."); return; }
        var teile = Pausenaufsichten.Teile(D, gang, z.Tag, z.Nach);
        if (teile == 0) { Ui.Info("Für diese Pause ist keine Aufsicht vorgesehen."); return; }
        var belegt = D.Pausenaufsichten.Where(a => a.GangId == gang && a.Tag == z.Tag && a.NachStunde == z.Nach && a.Datum is null).ToList();
        var teil = Enumerable.Range(1, teile).FirstOrDefault(t => belegt.All(a => a.Teil != t));
        var ersetzen = teil == 0 ? belegt.First() : null;
        var liste = Pausenaufsichten.Lehrervorschlag(D, gang, z.Tag, z.Nach, ersetzen);
        using var f = new Dialog($"Lehrervorschlag – {Ix.K(ElementTyp.Gang, gang)}, {Core.Planung.Planungsdienst.Tagname(D, z.Tag)}, Pause {Pausenaufsichten.PausenName(D, z.Nach)}", 900, 440);
        var g = new DataGridView { Left = 8, Top = 8, Width = 884, Height = 380 };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        string J(bool b) => b ? "x" : "";
        foreach (var (n, k, w) in new[] { ("N", "Name", 55), ("P", "Punkte", 50), ("Z", "Zähler", 45), ("T", "PA/Tag", 50), ("M", "Minuten", 55), ("MM", "Max.Min", 55), ("UV", "Unt. davor", 65), ("UN", "Unt. danach", 70),
                     ("GV", "Gang davor", 70), ("GN", "Gang danach", 75), ("B", "Bereit.", 50), ("V1", "vor 1.St.", 60), ("NL", "nach letzter", 70), ("F", "Aufs.Folge", 65), ("S", "Sperrung", 60), ("H", "Hohlst.", 50), ("G", "Geschl.", 50) })
            g.Columns[g.Columns.Add(n, k)].Width = w;
        foreach (var b in liste)
        {
            var r = g.Rows.Add(b.Lehrer.Kurzname, b.Punkte.ToString("0.#"), b.Zaehler, b.ProTag, b.Minuten, b.MaxMinuten, J(b.UnterrichtDavor), J(b.UnterrichtDanach), J(b.GangDavor), J(b.GangDanach),
                J(b.Bereitschaft), J(b.VorErsterStunde), J(b.NachLetzterStunde), J(b.Folge), J(b.VorSperrung || b.NachSperrung), J(b.HohlstundeDavor || b.HohlstundeDanach), b.Lehrer.Geschlecht);
            g.Rows[r].Tag = b.Lehrer;
            if (b.Schlecht) g.Rows[r].DefaultCellStyle.ForeColor = Color.Firebrick;
            g.Rows[r].Cells["N"].ToolTipText = string.Join("\n", b.Verletzt);
        }
        g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) f.DialogResult = DialogResult.OK; };
        f.Controls.Add(g);
        if (f.ShowDialog(this) != DialogResult.OK || g.CurrentRow?.Tag is not Lehrer l) return;
        Aendern($"Pausenaufsicht {l.Kurzname}", AenderungsArt.Stundenplan, d =>
        {
            if (ersetzen != null) d.Pausenaufsichten.RemoveAll(a => a.GangId == gang && a.Tag == z.Tag && a.NachStunde == z.Nach && a.Teil == ersetzen.Teil && a.Datum is null);
            d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang, Tag = z.Tag, NachStunde = z.Nach, LehrerId = l.Id, Teil = ersetzen?.Teil ?? teil });
        });
    }

    private void AufsichtLoeschen()
    {
        if (ElementId is not int id || Aktuell is not { } z) return;
        var gang = GangSicht;
        Aendern("Aufsicht löschen", AenderungsArt.Stundenplan, d => d.Pausenaufsichten.RemoveAll(a => a.Tag == z.Tag && a.NachStunde == z.Nach && a.Datum is null && (gang ? a.GangId == id : a.LehrerId == id)));
    }

    private void KeineAufsicht()
    {
        if (!GangSicht || ElementId is not int gang || Aktuell is not { } z) return;
        var teile = Pausenaufsichten.Teile(D, gang, z.Tag, z.Nach);
        Aendern(teile == 0 ? "Aufsicht (???)" : "Keine Aufsicht", AenderungsArt.Stundenplan, d => Pausenaufsichten.TeileSetzen(d, gang, z.Tag, z.Nach, teile == 0 ? 1 : 0));
    }

    private void Teilen(int delta)
    {
        if (!GangSicht || ElementId is not int gang || Aktuell is not { } z) return;
        var teile = Pausenaufsichten.Teile(D, gang, z.Tag, z.Nach);
        if (teile + delta < 1) return;
        Aendern(delta > 0 ? "Aufsicht teilen" : "Teilung entfernen", AenderungsArt.Stundenplan, d => Pausenaufsichten.TeileSetzen(d, gang, z.Tag, z.Nach, teile + delta));
    }

    private void ElementLoeschen()
    {
        if (ElementId is not int id || !Ui.Frage("Alle Aufsichten dieses Elements löschen?")) return;
        var gang = GangSicht;
        Aendern("Aufsichten löschen", AenderungsArt.Stundenplan, d => d.Pausenaufsichten.RemoveAll(a => a.Datum is null && (gang ? a.GangId == id : a.LehrerId == id)));
    }

    private void Optimieren()
    {
        using var f = new Dialog("Optimierung Pausenaufsichten", 380, 210);
        var y = 12;
        var nurGang = Ui.Zeile(f, "", new CheckBox { Text = "Nur aktuellen Gang", Checked = false, AutoSize = true, Enabled = GangSicht }, ref y, 10);
        var schlecht = Ui.Zeile(f, "", new CheckBox { Text = "Schlechte Einteilungen vorher löschen", AutoSize = true }, ref y, 10);
        var kurz = Ui.Zeile(f, "Pausen ab Minuten:", new NumericUpDown { Minimum = 0, Maximum = 60, Value = 0 }, ref y, 160, 60);
        f.Controls.Add(new Label { Text = "Es werden nur offene Aufsichten (???) besetzt. Lehrer mit PA Max = 0, gesperrte Lehrer und unpassendes Geschlecht sind ausgeschlossen.", Left = 12, Top = y, Width = 350, Height = 40, ForeColor = Color.DimGray });
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var n = 0;
        int? gang = nurGang.Checked && GangSicht ? ElementId : null;
        using (new WarteCursor())
            Aendern("Pausenaufsichten optimieren", AenderungsArt.Stundenplan, d => n = Pausenaufsichten.Optimieren(d, schlecht.Checked, gang, 0, 0, (int)kurz.Value));
        Ui.Info($"{n} Aufsichten wurden eingeteilt.", "Pausenaufsichten");
    }

    private void Gewichtung()
    {
        using var f = new Dialog("Gewichtung Pausenaufsichten", 540, 120 + Pausenaufsichten.Kriterien.Count * 32);
        var y = 12;
        var max = Ui.Zeile(f, "Max. Pausenaufsichten pro Tag", new NumericUpDown { Minimum = 1, Maximum = 5, Value = Pausenaufsichten.MaxProTag(D) }, ref y, 300, 60);
        f.Controls.Add(new Label { Text = "unwichtig ← → extrem wichtig (5 = nie verletzen)", Left = 300, Top = y, AutoSize = true, ForeColor = Color.DimGray });
        y += 22;
        var regler = new Dictionary<string, TrackBar>();
        foreach (var k in Pausenaufsichten.Kriterien)
        {
            var tb = new TrackBar { Minimum = 0, Maximum = 5, TickStyle = TickStyle.BottomRight, Height = 28, AutoSize = false, Value = Math.Clamp(Pausenaufsichten.Gewicht(D, k.Schluessel), 0, 5) };
            Ui.Zeile(f, k.Text, tb, ref y, 300, 200);
            regler[k.Schluessel] = tb;
        }
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var werte = regler.ToDictionary(x => x.Key, x => x.Value.Value);
        var maxWert = (int)max.Value;
        Aendern("Gewichtung Pausenaufsichten", AenderungsArt.Einstellungen, d =>
        {
            foreach (var (k, v) in werte) d.Gewichtung[k] = v;
            d.Gewichtung["PA.MaxProTagAnzahl"] = maxWert;
        });
    }

    public void Drucken(bool vorschau) => ListenDruck.DruckenTabelle(this, $"Pausenaufsichten – {(GangSicht ? "Gang" : "Lehrer")} {_element.Text}", _g, vorschau);
}

/// <summary>
/// Vertretungsstatistik und Berichte: Lehrer-Abrechnung, Vertretungszähler im Detail (Abrechnungscodes), Mehrarbeit NRW je Monat,
/// Monatsabrechnung (Tagesbilanz), Entfalltage, Vertretungs-Übersicht und Wochenwerte. Jede Ansicht als Druck, CSV oder HTML-Dokument.
/// </summary>
public sealed class StatistikFenster : MdiFenster, IDruckbar
{
    private static readonly string[] Berichte =
    {
        "Vertretungsabrechnung (alle Lehrer)", "Vertretungszähler – Einzelnachweis", "Mehrarbeit NRW (Monate)", "Monatsabrechnung (Tagesbilanz)",
        "Entfalltage", "Vertretungs-Übersicht", "Wochenwerte",
    };

    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly ToolStripComboBox _bericht = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ToolStripComboBox _lehrer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly Label _summe = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6), BackColor = Ui.Kopf };
    private bool _laedt;

    public StatistikFenster()
    {
        Text = "Vertretungsstatistik";
        HilfeThema = "Statistik";
        Icon = SymbolIcon("statistik");
        Size = new Size(1040, 540);
        Positionieren();
        var heute = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _von.Value = new DateTime(heute.Year, heute.Month, 1);
        _bis.Value = _von.Value.AddMonths(1).AddDays(-1);
        _bericht.Items.AddRange(Berichte);
        _bericht.SelectedIndex = 0;
        foreach (var l in Stammdaten.Sortiert(D.Lehrer)) _lehrer.Items.Add(new LehrerEintrag(l));
        if (_lehrer.Items.Count > 0) _lehrer.SelectedIndex = 0;
        var ts = Werkzeugleiste();
        ts.Items.Add(_bericht);
        ts.Items.Add(new ToolStripLabel("Lehrer"));
        ts.Items.Add(_lehrer);
        ts.Items.Add(new ToolStripLabel("von"));
        ts.Items.Add(new ToolStripControlHost(_von));
        ts.Items.Add(new ToolStripLabel("bis"));
        ts.Items.Add(new ToolStripControlHost(_bis));
        var zeit = new ToolStripDropDownButton("Zeitraum", Symbole.Get("kalender", 16));
        zeit.DropDownItems.Add("Aktueller Monat", null, (_, _) => Setzen(new DateTime(heute.Year, heute.Month, 1), 1));
        zeit.DropDownItems.Add("Vormonat", null, (_, _) => Setzen(new DateTime(heute.Year, heute.Month, 1).AddMonths(-1), 1));
        zeit.DropDownItems.Add("Aktuelle Woche", null, (_, _) => { var m = Kalender.Montag(AppZustand.Datum).ToDateTime(TimeOnly.MinValue); Bereich(m, m.AddDays(6)); });
        zeit.DropDownItems.Add("1. Halbjahr", null, (_, _) => Bereich(D.Einstellungen.SchuljahrVon.ToDateTime(TimeOnly.MinValue), new DateTime(D.Einstellungen.SchuljahrVon.Year + 1, 1, 31)));
        zeit.DropDownItems.Add("2. Halbjahr", null, (_, _) => Bereich(new DateTime(D.Einstellungen.SchuljahrVon.Year + 1, 2, 1), D.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue)));
        zeit.DropDownItems.Add("Schuljahr", null, (_, _) => Bereich(D.Einstellungen.SchuljahrVon.ToDateTime(TimeOnly.MinValue), D.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue)));
        ts.Items.Add(zeit);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Dokument (HTML) für alle Lehrer", "html", () => HtmlBericht(true), false));
        ts.Items.Add(Knopf("Dokument (HTML)", "html", () => HtmlBericht(false)));
        ts.Items.Add(Knopf("Export CSV", "export", CsvExport));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        _von.ValueChanged += (_, _) => Neuaufbau();
        _bis.ValueChanged += (_, _) => Neuaufbau();
        _bericht.SelectedIndexChanged += (_, _) => Neuaufbau();
        _lehrer.SelectedIndexChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.CellDoubleClick += (_, e) =>
        {
            // Doppelklick in der Abrechnung: Einzelnachweis des Lehrers
            if (e.RowIndex < 0 || _bericht.SelectedIndex != 0 || _g.Rows[e.RowIndex].Tag is not Lehrer l) return;
            _lehrer.SelectedItem = _lehrer.Items.Cast<LehrerEintrag>().FirstOrDefault(x => x.L.Id == l.Id);
            _bericht.SelectedIndex = 1;
        };
        Controls.Add(_g);
        Controls.Add(_summe);
        Controls.Add(ts);
        Neuaufbau();
    }

    private sealed record LehrerEintrag(Lehrer L)
    {
        public override string ToString() => L.Kurzname;
    }

    private void Setzen(DateTime start, int monate) => Bereich(start, start.AddMonths(monate).AddDays(-1));

    private void Bereich(DateTime von, DateTime bis)
    {
        _laedt = true;
        _von.Value = von;
        _bis.Value = bis;
        _laedt = false;
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);
    private Lehrer? L => (_lehrer.SelectedItem as LehrerEintrag)?.L;

    /// <summary>Kopf, Breiten, Zeilen (mit optionalem Tag) und Summentext des gewählten Berichts.</summary>
    private (string[] Kopf, int[] Breiten, List<(string[] Werte, object? Tag)> Zeilen, string Summe) Daten(int bericht, Lehrer? l)
    {
        var zeilen = new List<(string[], object?)>();
        string S(decimal x) => x.ToString("+0.##;-0.##;0");
        switch (bericht)
        {
            case 0:
            {
                var liste = Statistik.Abrechnung(D, Von, Bis);
                foreach (var z in liste)
                    zeilen.Add((new[] { z.Lehrer.Kurzname, z.Lehrer.VollerName, z.Vertretungen.ToString(), z.Betreuungen.ToString(), z.Sondereinsaetze.ToString(), z.Aufsichten.ToString(),
                        z.Mehrarbeit.ToString(), z.Entfaelle.ToString(), z.Freisetzungen.ToString(), S(z.Saldo), z.Verguetungsfaehig.ToString() }, z.Lehrer));
                var s = Statistik.Zusammenfassung(D, Von, Bis);
                return (new[] { "Lehrer", "Name", "Vertretungen", "Betreuungen", "Sondereins.", "Pausenaufs.", "Mehrarbeit", "Entfall", "Freisetzung", "Zähler", "vergütungsfähig (NRW)" },
                    new[] { 60, 150, 80, 75, 75, 75, 70, 55, 70, 55, 130 }, zeilen,
                    $"Gesamt: {liste.Sum(z => z.Vertretungen)} Vertretungen, {liste.Sum(z => z.Mehrarbeit)} Mehrarbeitsstunden, davon {liste.Sum(z => z.Verguetungsfaehig)} vergütungsfähig. " +
                    $"Ausfall: {s.Gesamt} Einträge – vertreten {s.Vertreten}, Entfall {s.Entfall}, Freisetzung {s.Freisetzung}, Raum {s.Raum}, Verlegung {s.Verlegung}, offen {s.Offen}.\nDoppelklick auf einen Lehrer zeigt den Einzelnachweis.");
            }
            case 1:
            {
                if (l is null) break;
                var z = Zaehlung.Lehrer(D, l, Von, Bis);
                foreach (var x in z.Zeilen.OrderBy(x => x.Datum).ThenBy(x => x.Stunde))
                    zeilen.Add((new[] { $"{Planungsdienst.TageKurz[((int)x.Datum.DayOfWeek + 6) % 7]} {Format.Datum(x.Datum)}", x.Stunde == 0 ? "" : x.Stunde.ToString(), x.Code, x.Art, x.Fach, x.Klassen, x.Grund,
                        x.Zaehlt ? S(x.Wert) : "0", x.Bezahlt ? "b" : "", x.Mehrarbeit ? "x" : "", x.Text }, x.Vertretung));
                return (new[] { "Datum", "Std.", "Code", "Art", "Fach", "Klasse(n)", "Grund", "Wert", "bez.", "MA", "Text" },
                    new[] { 95, 40, 45, 120, 55, 80, 90, 50, 40, 35, 200 }, zeilen,
                    $"{l.Kurzname} – {l.VollerName}: Zähler {S(z.Summe)} · Vertretungen {z.Vertretungen} (nicht gezählt {z.VertretungenNichtZuZaehlen}) · Entfälle {z.Entfaelle} (nicht gezählt {z.EntfaelleNichtZuZaehlen}) · " +
                    $"Freisetzungen {z.Freisetzungen} · Betreuungen {z.Betreuungen} · Sondereinsätze {z.Sondereinsaetze} · bezahlt {z.Bezahlt} · Mehrarbeit {z.Mehrarbeit}\n" +
                    "Codes: +V Vertretung, V Statt-Vertretung, B Betreuung, P Pausenaufsicht, +Y Sondereinsatz, +K Klausuraufsicht, VA Veranstaltung, -E Entfall, F Freisetzung, WK Wertkorrektur");
            }
            case 2:
            {
                if (l is null) break;
                var monate = Statistik.MehrarbeitNrw(D, l, Von, Bis);
                foreach (var m in monate)
                    zeilen.Add((new[] { new DateTime(m.Jahr, m.Monat, 1).ToString("MMMM yyyy", Format.De), m.Geleistet.ToString(), m.Minder.ToString(), m.Saldo.ToString("+0;-0;0"), m.Grenze.ToString("0.#"), m.Verguetungsfaehig.ToString() }, null));
                return (new[] { "Monat", "geleistete MA", "Minderleistung", "Saldo", "Grenze", "vergütungsfähig" }, new[] { 130, 100, 100, 70, 70, 110 }, zeilen,
                    $"{l.Kurzname}: Teilzeitquote {Statistik.Teilzeitquote(D, l):0.00} · Grenze {D.Einstellungen.Mehrarbeitsgrenze} Std. × Quote. Übersteigt der Saldo eines Kalendermonats die Grenze, sind alle Stunden vergütungsfähig.\n" +
                    $"Summe vergütungsfähig: {monate.Sum(m => m.Verguetungsfaehig)} Stunden.");
            }
            case 3:
            {
                if (l is null) break;
                var jahr = Von.Year;
                var monat = Von.Month;
                var tage = Statistik.Mehrarbeit(D, l, jahr, monat);
                foreach (var t in tage)
                    zeilen.Add((new[] { $"{Planungsdienst.TageKurz[((int)t.Datum.DayOfWeek + 6) % 7]} {Format.Datum(t.Datum)}", t.Soll.ToString(), t.Ist.ToString(), t.Vertretungen.ToString(), t.Entfaelle.ToString(), (t.Ist - t.Soll).ToString("+0;-0;0") }, null));
                return (new[] { "Tag", "Soll", "Ist", "Vertretungen", "Entfälle", "Differenz" }, new[] { 110, 50, 50, 85, 70, 70 }, zeilen,
                    $"{l.Kurzname} – Monatsabrechnung {new DateTime(jahr, monat, 1).ToString("MMMM yyyy", Format.De)}: Soll {tage.Sum(t => t.Soll)}, Ist {tage.Sum(t => t.Ist)}, Vertretungen {tage.Sum(t => t.Vertretungen)}, Entfälle {tage.Sum(t => t.Entfaelle)}.");
            }
            case 4:
            {
                var liste = Statistik.Entfalltage(D, Von, Bis);
                foreach (var e in liste.OrderBy(e => e.Lehrer.Kurzname).ThenBy(e => e.Datum))
                    zeilen.Add((new[] { e.Lehrer.Kurzname, e.Lehrer.VollerName, $"{Planungsdienst.TageKurz[((int)e.Datum.DayOfWeek + 6) % 7]} {Format.Datum(e.Datum)}", e.Stunden.ToString(), e.Grund }, e.Lehrer));
                return (new[] { "Lehrer", "Name", "Datum", "Stunden", "Grund" }, new[] { 60, 160, 100, 60, 150 }, zeilen,
                    $"{liste.Count} Entfalltage (Tage, an denen wegen einer Absenz der gesamte Unterricht des Lehrers entfiel), {liste.Select(e => e.Lehrer.Id).Distinct().Count()} Lehrer.");
            }
            case 5:
            {
                var u = Statistik.VertretungsUebersicht(D, Von, Bis);
                zeilen.Add((new[] { "Geplanter Unterricht", u.Geplant.ToString(), "100 %" }, null));
                string P(int x) => u.Geplant == 0 ? "" : $"{100.0 * x / u.Geplant:0.0} %";
                zeilen.Add((new[] { "gemäß Plan erteilt", u.GemaessPlan.ToString(), P(u.GemaessPlan) }, null));
                zeilen.Add((new[] { "nicht gemäß Plan erteilt", u.NichtErteilt.ToString(), P(u.NichtErteilt) }, null));
                zeilen.Add((new[] { "Sondereinsätze", u.Sondereinsaetze.ToString(), "" }, null));
                zeilen.Add((new[] { "— nach Grund —", "", "" }, null));
                foreach (var (k, v) in u.NachGrund.OrderByDescending(x => x.Value)) zeilen.Add((new[] { "  " + k, v.ToString(), P(v) }, null));
                zeilen.Add((new[] { "— nach Art —", "", "" }, null));
                foreach (var (k, v) in u.NachArt.OrderByDescending(x => x.Value)) zeilen.Add((new[] { "  " + k, v.ToString(), P(v) }, null));
                zeilen.Add((new[] { "— ersatzlos entfallen, nach Grund —", "", "" }, null));
                foreach (var (k, v) in u.EntfallNachGrund.OrderByDescending(x => x.Value)) zeilen.Add((new[] { "  " + k, v.ToString(), P(v) }, null));
                return (new[] { "Kennzahl", "Stunden", "Anteil" }, new[] { 300, 80, 80 }, zeilen, "Vertretungs-Übersicht der Schule (Bereitschaften nicht mitgezählt).");
            }
            case 6:
            {
                if (l is null) break;
                var ww = Statistik.Wochenwerte(D, l).Where(w => w.Montag.AddDays(6) >= Von && w.Montag <= Bis).ToList();
                foreach (var w in ww)
                    zeilen.Add((new[] { $"KW {System.Globalization.ISOWeek.GetWeekOfYear(w.Montag.ToDateTime(TimeOnly.MinValue))}", Format.Datum(w.Montag), w.Periode, w.Soll.ToString("0.##"), w.Unterricht.ToString(),
                        w.Vertretungen.ToString(), w.Entfaelle.ToString(), w.Korrektur.ToString("+0.##;-0.##;"), w.Ist.ToString("0.##"), (w.Ist - w.Soll).ToString("+0.##;-0.##;0") }, null));
                return (new[] { "Woche", "Montag", "Periode", "Soll", "Unterricht", "Vertr.", "Entfall", "Korrektur", "Ist", "Differenz" }, new[] { 60, 85, 90, 50, 70, 50, 55, 70, 50, 70 }, zeilen,
                    $"{l.Kurzname}: Summe Differenz {ww.Sum(w => w.Ist - w.Soll):+0.##;-0.##;0} Stunden in {ww.Count} Wochen.");
            }
        }
        return (new[] { "" }, new[] { 200 }, zeilen, "Bitte einen Lehrer wählen.");
    }

    private void Neuaufbau()
    {
        if (_laedt || IsDisposed) return;
        _lehrer.Enabled = _bericht.SelectedIndex is 1 or 2 or 3 or 6;
        var (kopf, breiten, zeilen, summe) = Daten(_bericht.SelectedIndex, L);
        _g.Rows.Clear();
        _g.Columns.Clear();
        for (var i = 0; i < kopf.Length; i++) _g.Columns[_g.Columns.Add("C" + i, kopf[i])].Width = breiten[i];
        foreach (var (werte, tag) in zeilen)
        {
            var r = _g.Rows.Add(werte);
            _g.Rows[r].Tag = tag;
            if (_bericht.SelectedIndex == 0 && werte[^1] != "0") _g.Rows[r].Cells[kopf.Length - 1].Style.BackColor = Color.FromArgb(255, 240, 200);
            if (_bericht.SelectedIndex == 2 && werte[^1] != "0") _g.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 240, 200);
            if (_bericht.SelectedIndex == 1 && werte[2].StartsWith('-') || _bericht.SelectedIndex == 1 && werte[2] == "F") _g.Rows[r].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
        _summe.Text = summe;
        Text = $"Vertretungsstatistik – {Berichte[_bericht.SelectedIndex]}";
    }

    private string Titel(int bericht, Lehrer? l) =>
        $"{Berichte[bericht]}{(bericht is 1 or 2 or 3 or 6 && l != null ? $" – {l.Kurzname} {l.VollerName}" : "")} – {Format.Datum(Von)} bis {Format.Datum(Bis)}";

    private string HtmlTeil(int bericht, Lehrer? l)
    {
        var (kopf, _, zeilen, summe) = Daten(bericht, l);
        static string H(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new System.Text.StringBuilder();
        sb.Append($"<div class=\"pg\"><h1>{H(Titel(bericht, l))}</h1><div class=\"sub\">{H(D.Einstellungen.Schulname)} · erstellt am {DateTime.Now:dd.MM.yyyy HH:mm} von {H(AppZustand.Benutzer)}</div><table><tr>");
        foreach (var k in kopf) sb.Append($"<th>{H(k)}</th>");
        sb.Append("</tr>");
        foreach (var (w, _) in zeilen)
        {
            sb.Append("<tr>");
            foreach (var x in w) sb.Append($"<td>{H(x)}</td>");
            sb.Append("</tr>");
        }
        sb.Append($"</table><p>{H(summe).Replace("\n", "<br>")}</p>");
        if (bericht is 1 or 2 or 3)
            sb.Append("<p style=\"margin-top:40px\">_______________________________ &nbsp; &nbsp; _______________________________<br>Lehrkraft &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; Schulleitung</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>Bericht als HTML-Dokument (druckfertig, je Lehrer eine Seite bei "für alle Lehrer").</summary>
    private void HtmlBericht(bool alle)
    {
        var b = _bericht.SelectedIndex;
        var sb = new System.Text.StringBuilder();
        if (alle && b is 1 or 2 or 3 or 6)
        {
            using (new WarteCursor())
                foreach (var l in Statistik.Abrechnung(D, Von, Bis).Select(z => z.Lehrer)) sb.Append(HtmlTeil(b, l));
        }
        else sb.Append(HtmlTeil(b, L));
        var pfad = Path.Combine(Path.GetTempPath(), $"UntPlan_Bericht_{b}_{Von:yyyyMMdd}.html");
        File.WriteAllText(pfad, Export.Seite(Berichte[b], sb.ToString()));
        HauptFenster.Starten(pfad);
    }

    private void CsvExport()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV-Datei|*.csv", FileName = $"Statistik_{_bericht.SelectedIndex}_{Von:yyyyMMdd}-{Bis:yyyyMMdd}.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var (kopf, _, zeilen, _) = Daten(_bericht.SelectedIndex, L);
        var liste = new List<string[]> { kopf };
        liste.AddRange(zeilen.Select(z => z.Werte));
        File.WriteAllText(sfd.FileName, Csv.Schreiben(liste, bom: false), new System.Text.UTF8Encoding(true));
    }

    public void Drucken(bool vorschau)
    {
        var (kopf, breiten, zeilen, _) = Daten(_bericht.SelectedIndex, L);
        ListenDruck.Drucken(this, Titel(_bericht.SelectedIndex, L), kopf, breiten, zeilen.Select(z => z.Werte).ToList(), vorschau);
    }
}

/// <summary>Monitoransicht des Vertretungsplans (Vollbild, automatischer Seitenwechsel und Aktualisierung). ESC beendet.</summary>
public sealed class MonitorFenster : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };
    private int _seite;
    private int _takt;

    public MonitorFenster()
    {
        Text = "UntPlan – Vertretungsplan (Monitor)";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(11, 26, 51);
        KeyPreview = true;
        DoubleBuffered = true;
        Icon = Icon.FromHandle(Symbole.Get("monitor", 16).GetHicon());
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode is Keys.Right or Keys.Space) { _seite++; Invalidate(); }
            if (e.KeyCode == Keys.Left) { _seite = Math.Max(0, _seite - 1); Invalidate(); }
        };
        Click += (_, _) => { _seite++; Invalidate(); };
        _timer.Tick += (_, _) => { _seite++; _takt++; Invalidate(); };
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
        Load += (_, _) => { WindowState = FormWindowState.Normal; Bounds = Screen.FromControl(this).Bounds; };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var d = AppZustand.D;
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var tage = new[] { Kalender.IstUnterrichtstag(d, heute) ? heute : Kalender.NaechsterUnterrichtstag(d, heute) };
        tage = tage.Append(Kalender.NaechsterUnterrichtstag(d, tage[0])).ToArray();
        using var titel = new Font("Segoe UI", 26f, FontStyle.Bold);
        using var kopf = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var schrift = new Font("Segoe UI", 15f);
        var spalten = new[] { ("Klasse(n)", 0.14f), ("Std.", 0.06f), ("Vertreter", 0.12f), ("Fach", 0.1f), ("Raum", 0.1f), ("Art", 0.15f), ("Text", 0.33f) };
        var halbe = (Width - 60) / 2;
        for (var i = 0; i < tage.Length; i++)
        {
            var x0 = 20 + i * (halbe + 20);
            var datum = tage[i];
            TextRenderer.DrawText(g, $"{Planungsdienst.TageLang[((int)datum.DayOfWeek + 6) % 7]}, {Format.Datum(datum)}", titel, new Point(x0, 16), Color.White);
            var texte = string.Join("  ·  ", d.Tagestexte.Where(t => t.GiltAm(datum) && !t.FuerLehrer).Select(t => t.Text));
            TextRenderer.DrawText(g, texte, schrift, new Rectangle(x0, 64, halbe, 30), Color.FromArgb(255, 220, 120), TextFormatFlags.EndEllipsis);
            var y = 100;
            var x = x0;
            using (var b = new SolidBrush(Color.FromArgb(29, 58, 102))) g.FillRectangle(b, x0, y, halbe, 34);
            foreach (var (k, anteil) in spalten)
            {
                TextRenderer.DrawText(g, k, kopf, new Rectangle(x + 4, y, (int)(halbe * anteil), 34), Color.White, TextFormatFlags.VerticalCenter);
                x += (int)(halbe * anteil);
            }
            y += 38;
            var zeilen = Vertretungsplan.Zeilen(d, datum).Where(z => z.Art != "Pausenaufsicht").ToList();
            var proSeite = Math.Max(1, (Height - y - 50) / 32);
            var seiten = Math.Max(1, (zeilen.Count + proSeite - 1) / proSeite);
            var s = _seite % seiten;
            var n = 0;
            foreach (var z in zeilen.Skip(s * proSeite).Take(proSeite))
            {
                if (n++ % 2 == 1) using (var b = new SolidBrush(Color.FromArgb(18, 38, 70))) g.FillRectangle(b, x0, y, halbe, 32);
                x = x0;
                var entfall = z.Art is "Entfall" or "Freisetzung";
                var werte = new[] { z.Klassen, z.Stunde.ToString(), z.Vertreter, z.NeuesFach, z.NeuerRaum, z.Art, z.Text };
                for (var c = 0; c < werte.Length; c++)
                {
                    TextRenderer.DrawText(g, werte[c], schrift, new Rectangle(x + 4, y, (int)(halbe * spalten[c].Item2) - 6, 32), entfall ? Color.FromArgb(255, 154, 154) : Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += (int)(halbe * spalten[c].Item2);
                }
                y += 32;
            }
            if (zeilen.Count == 0) TextRenderer.DrawText(g, "Keine Vertretungen.", schrift, new Point(x0 + 4, y + 6), Color.LightGray);
            if (seiten > 1) TextRenderer.DrawText(g, $"Seite {s + 1}/{seiten}", schrift, new Point(x0, Height - 44), Color.LightGray);
        }
        TextRenderer.DrawText(g, $"{d.Einstellungen.Schulname}   ·   Stand {DateTime.Now:HH:mm}   ·   ESC = Beenden", schrift, new Rectangle(0, Height - 44, Width - 20, 40), Color.LightGray, TextFormatFlags.Right);
    }
}
