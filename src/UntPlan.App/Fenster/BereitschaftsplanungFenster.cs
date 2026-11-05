using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Bereitschaftsplanung wie im Handbuch (Fenster „Bereitschaften“): mit „Anzahl Bereitsch. anzeigen“ zeigt und ändert das Wochenraster die
/// Anzahl der nötigen Bereitschaften je Stunde, ohne den Haken die eingeteilten Lehrkräfte („???“ = fehlt; grün = gedeckt, rot = fehlt).
/// Lehrer-Vorschlag mit Merker, Anz.Bereit., Max.Bereit., Punkte und Zeitwünsche; automatische Verteilung; „Doppelstundenmodell“.
/// </summary>
public sealed class BereitschaftsplanungFenster : MdiFenster, IDruckbar
{
    private readonly DataGridView _raster = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill };
    private readonly ListBox _eingeteilt = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown _bedarf = new() { Minimum = 0, Maximum = 20, Width = 50 };
    private readonly Label _zelle = new() { AutoSize = true, Font = Ui.SchriftFett, Padding = new Padding(0, 4, 8, 0) };
    private readonly CheckBox _anzahl = new() { Text = "Anzahl Bereitsch. anzeigen", AutoSize = true, Left = 8, Top = 8 };
    private (int Tag, int Stunde)? _gewaehlt;
    private bool _laedt;

    public BereitschaftsplanungFenster()
    {
        Text = "Bereitschaften";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("bereitschaft");
        Size = new Size(1000, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Fehlende Bereitschaften automatisch besetzen", "optimierung", Automatisch, false));
        var doppelt = new CheckBox { Text = "Doppelstundenmodell", AutoSize = true, Left = 220, Top = 8, Checked = D.Einstellungen.BereitschaftNurDoppelstunden };
        doppelt.CheckedChanged += (_, _) =>
        {
            if (doppelt.Checked != D.Einstellungen.BereitschaftNurDoppelstunden)
                Aendern("Bereitschaften nur als Doppelstunden", AenderungsArt.Einstellungen, d => d.Einstellungen.BereitschaftNurDoppelstunden = doppelt.Checked);
        };
        new ToolTip().SetToolTip(doppelt, "Die Automatik besetzt Bereitschaften immer paarweise in zwei aufeinanderfolgenden Stunden");
        var oben = new Panel { Dock = DockStyle.Top, Height = 32 };
        oben.Controls.AddRange(new Control[] { _anzahl, doppelt });
        _anzahl.CheckedChanged += (_, _) => Neuaufbau();
        ts.Items.Add(Knopf("Bedarf für alle Vormittagsstunden setzen", "bereitschaft", BedarfVormittag, false));
        ts.Items.Add(Knopf("Alle Bereitschaften entfernen", "loeschen", AlleEntfernen, false));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));

        Ui.RasterGestalten(_raster, false);
        _raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _raster.MultiSelect = false;
        _raster.RowTemplate.Height = 40;
        _raster.CellClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0) Waehlen(e.ColumnIndex - 1, e.RowIndex + 1); };
        // „Anzahl Bereitsch. anzeigen“: Anzahl direkt in die Zelle eintragen
        _raster.CellEndEdit += (_, e) =>
        {
            if (_laedt || !_anzahl.Checked || e.RowIndex < 0 || e.ColumnIndex < 1) return;
            var t = e.ColumnIndex - 1;
            var s = e.RowIndex + 1;
            if (!int.TryParse(_raster.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString(), out var n)) n = 0;
            n = Math.Clamp(n, 0, 20);
            if (n != Bereitschaftsplanung.Bedarf(D, t, s)) Aendern("Bereitschaftsbedarf", AenderungsArt.Einstellungen, d => Bereitschaftsplanung.BedarfSetzen(d, t, s, n));
            else Neuaufbau();
        };
        _raster.CellFormatting += Formatieren;
        _raster.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        Ui.RasterGestalten(_vorschlag, false);
        _vorschlag.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorschlag.MultiSelect = false;
        foreach (var (n, k, w) in new[] { ("L", "Name", 60), ("M", "Merker", 50), ("W", "Anz.Bereit.", 70), ("Max", "Max.Bereit.", 70), ("P", "Punkte", 50), ("Z", "Zeitwünsche", 75), ("F", "freies Dep.", 70), ("H", "Lage", 150) })
            _vorschlag.Columns[_vorschlag.Columns.Add(n, k)].Width = w;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Eintragen(); };

        var rechts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        rechts.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rechts.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        rechts.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        rechts.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        rechts.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        kopf.Controls.Add(_zelle);
        kopf.Controls.Add(new Label { Text = "Bedarf:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        kopf.Controls.Add(_bedarf);
        var entfernen = new Button { Text = "Entfernen", Height = 24, Width = 80 };
        entfernen.Click += (_, _) => Entfernen();
        var eintragen = new Button { Text = "Übernahme", Height = 24, Width = 80 };
        eintragen.Click += (_, _) => Eintragen();
        kopf.Controls.Add(eintragen);
        kopf.Controls.Add(entfernen);
        rechts.Controls.Add(kopf, 0, 0);
        rechts.Controls.Add(new Label { Text = "Eingeteilt:", AutoSize = true }, 0, 1);
        rechts.Controls.Add(_eingeteilt, 0, 2);
        rechts.Controls.Add(new Label { Text = "Lehrer-Vorschlag (Doppelklick = Übernahme):", AutoSize = true }, 0, 3);
        rechts.Controls.Add(_vorschlag, 0, 4);
        _bedarf.ValueChanged += (_, _) => BedarfGeaendert();

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_raster);
        split.Panel1.Controls.Add(oben);
        split.Panel2.Controls.Add(rechts);
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, (int)(split.Width * 0.58));
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _laedt = true;
        var stunden = Bereitschaftsplanung.Stunden(D);
        _raster.Columns.Clear();
        _raster.Rows.Clear();
        _raster.Columns[_raster.Columns.Add("S", "")].Width = 40;
        _raster.Columns["S"]!.ReadOnly = true;
        _raster.ReadOnly = !_anzahl.Checked;
        _raster.RowTemplate.Height = _anzahl.Checked ? 22 : 40;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
        {
            var c = _raster.Columns[_raster.Columns.Add("T" + t, Planungsdienst.Tagname(D, t, true))];
            c.Width = _anzahl.Checked ? 80 : 110;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        for (var s = 1; s <= D.Zeitraster.StundenProTag; s++)
        {
            var werte = new object[1 + D.Zeitraster.TageProWoche];
            werte[0] = D.Zeitraster.StundenLabel(s);
            for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
            {
                var namen = stunden.Where(b => b.Tag == t && b.Stunde == s).Select(b => b.Lehrer.Kurzname).ToList();
                var bedarf = Bereitschaftsplanung.Bedarf(D, t, s);
                werte[1 + t] = _anzahl.Checked ? bedarf.ToString()
                    : string.Join(", ", namen.Concat(Enumerable.Repeat("???", Math.Max(0, bedarf - namen.Count))));
            }
            _raster.Rows.Add(werte);
        }
        var gesamtBedarf = D.Bereitschaftsbedarf.Sum(b => b.Anzahl);
        Text = $"Bereitschaften – {stunden.Count} verplant, Bedarf {gesamtBedarf}";
        _laedt = false;
        if (_gewaehlt is { } g) Waehlen(g.Tag, g.Stunde);
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 1 || e.CellStyle is null) return;
        var t = e.ColumnIndex - 1;
        var s = e.RowIndex + 1;
        if (!D.Zeitraster.Verfuegbar(t, s)) { e.CellStyle.BackColor = Color.FromArgb(225, 225, 225); return; }
        var bedarf = Bereitschaftsplanung.Bedarf(D, t, s);
        if (bedarf == 0) return;
        var ist = Bereitschaftsplanung.Stunden(D).Count(b => b.Tag == t && b.Stunde == s);
        e.CellStyle.BackColor = ist >= bedarf ? Color.FromArgb(205, 238, 205) : Color.FromArgb(255, 210, 210);
    }

    private void Waehlen(int tag, int stunde)
    {
        _gewaehlt = (tag, stunde);
        _laedt = true;
        _zelle.Text = $"{Planungsdienst.Tagname(D, tag)}, {stunde}. Std.";
        _bedarf.Value = Bereitschaftsplanung.Bedarf(D, tag, stunde);
        _eingeteilt.Items.Clear();
        foreach (var b in Bereitschaftsplanung.Stunden(D).Where(b => b.Tag == tag && b.Stunde == stunde)) _eingeteilt.Items.Add(new Eintrag(b.Lehrer));
        _vorschlag.Rows.Clear();
        foreach (var k in Bereitschaftsplanung.Vorschlag(D, tag, stunde))
        {
            var zw = k.Lehrer.Zeitwuensche.Get(tag, stunde);
            var r = _vorschlag.Rows.Add(k.Lehrer.Kurzname, Merker(k.Lehrer, tag, stunde), k.BereitschaftenWoche, k.Lehrer.BereitschaftenMax, (int)k.Punkte, zw == 0 ? "" : zw.ToString("+0;-0"),
                k.FreiesDeputat.ToString("+0.#;-0.#;0"), k.Hinweis);
            _vorschlag.Rows[r].Tag = k.Lehrer;
            if (zw > 0) _vorschlag.Rows[r].Cells["Z"].Style.BackColor = Color.FromArgb(140, 240, 140);
            else if (zw < 0) _vorschlag.Rows[r].Cells["Z"].Style.BackColor = Color.FromArgb(255, 190, 190);
        }
        _laedt = false;
    }

    /// <summary>Merker wie im Vertretungsvorschlag: Abstand der Bereitschaft zur nächsten eigenen Stunde am Tag (leer = kein Unterricht).</summary>
    private string Merker(Lehrer l, int tag, int stunde)
    {
        var eigene = Ix.PlanVon(ElementTyp.Lehrer, l.Id).Where(p => p.Tag == tag && p.Stunde != stunde).Select(p => Math.Abs(p.Stunde - stunde)).ToList();
        return eigene.Count == 0 ? "" : eigene.Min().ToString();
    }

    private sealed record Eintrag(Lehrer L)
    {
        public override string ToString() => $"{L.Kurzname} – {L.VollerName}";
    }

    private void BedarfGeaendert()
    {
        if (_laedt || _gewaehlt is not { } g) return;
        var n = (int)_bedarf.Value;
        Aendern("Bereitschaftsbedarf", AenderungsArt.Einstellungen, d => Bereitschaftsplanung.BedarfSetzen(d, g.Tag, g.Stunde, n));
    }

    private void Eintragen()
    {
        if (_gewaehlt is not { } g || _vorschlag.CurrentRow?.Tag is not Lehrer l) return;
        Aendern($"Bereitschaft {l.Kurzname}", AenderungsArt.Stundenplan, d => Bereitschaftsplanung.Setzen(d, l.Id, g.Tag, g.Stunde));
    }

    private void Entfernen()
    {
        if (_gewaehlt is not { } g || _eingeteilt.SelectedItem is not Eintrag e) return;
        Aendern($"Bereitschaft {e.L.Kurzname} entfernen", AenderungsArt.Stundenplan, d => Bereitschaftsplanung.Entfernen(d, e.L.Id, g.Tag, g.Stunde));
    }

    private void Automatisch()
    {
        var n = 0;
        Aendern("Bereitschaften automatisch besetzen", AenderungsArt.Stundenplan, d => n = Bereitschaftsplanung.Automatisch(d));
        var fehlt = D.Bereitschaftsbedarf.Sum(b => Math.Max(0, b.Anzahl - Bereitschaftsplanung.Stunden(D).Count(x => x.Tag == b.Tag && x.Stunde == b.Stunde)));
        Ui.Info($"{n} Bereitschaften wurden eingeteilt.{(fehlt > 0 ? $"\n{fehlt} Bereitschaften konnten nicht besetzt werden (keine freie Lehrkraft bzw. Höchstzahl erreicht)." : "")}", "Bereitschaftsplanung");
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
}
