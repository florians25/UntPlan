using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Bereitschaftsplanung (Stundenplanung): Wochenraster mit Bedarf und eingeteilten Bereitschaften je Stunde
/// (grün = gedeckt, rot = fehlt), Lehrervorschlag für die gewählte Stunde, automatische Verteilung.
/// </summary>
public sealed class BereitschaftsplanungFenster : MdiFenster, IDruckbar
{
    private readonly DataGridView _raster = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill };
    private readonly ListBox _eingeteilt = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown _bedarf = new() { Minimum = 0, Maximum = 20, Width = 50 };
    private readonly Label _zelle = new() { AutoSize = true, Font = Ui.SchriftFett, Padding = new Padding(0, 4, 8, 0) };
    private (int Tag, int Stunde)? _gewaehlt;
    private bool _laedt;

    public BereitschaftsplanungFenster()
    {
        Text = "Bereitschaftsplanung";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("bereitschaft");
        Size = new Size(1000, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Fehlende Bereitschaften automatisch besetzen", "optimierung", Automatisch, false));
        var doppelt = new CheckBox { Text = "Bereitschaften nur als Doppelstunden verplanen", AutoSize = true, BackColor = Color.Transparent, Checked = D.Einstellungen.BereitschaftNurDoppelstunden };
        doppelt.CheckedChanged += (_, _) =>
        {
            if (doppelt.Checked != D.Einstellungen.BereitschaftNurDoppelstunden)
                Aendern("Bereitschaften nur als Doppelstunden", AenderungsArt.Einstellungen, d => d.Einstellungen.BereitschaftNurDoppelstunden = doppelt.Checked);
        };
        ts.Items.Add(new ToolStripControlHost(doppelt) { ToolTipText = "Die Automatik besetzt Bereitschaften immer paarweise in zwei aufeinanderfolgenden Stunden" });
        ts.Items.Add(Knopf("Bedarf für alle Vormittagsstunden setzen", "bereitschaft", BedarfVormittag, false));
        ts.Items.Add(Knopf("Alle Bereitschaften entfernen", "loeschen", AlleEntfernen, false));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));

        Ui.RasterGestalten(_raster, false);
        _raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _raster.MultiSelect = false;
        _raster.RowTemplate.Height = 40;
        _raster.CellClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0) Waehlen(e.ColumnIndex - 1, e.RowIndex + 1); };
        _raster.CellFormatting += Formatieren;
        _raster.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        Ui.RasterGestalten(_vorschlag, false);
        _vorschlag.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorschlag.MultiSelect = false;
        foreach (var (n, k, w) in new[] { ("L", "Lehrer", 60), ("P", "Pkt.", 40), ("W", "Ber./Woche", 70), ("F", "freies Dep.", 70), ("H", "Lage", 150) })
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
        var eintragen = new Button { Text = "Eintragen", Height = 24, Width = 80 };
        eintragen.Click += (_, _) => Eintragen();
        kopf.Controls.Add(eintragen);
        kopf.Controls.Add(entfernen);
        rechts.Controls.Add(kopf, 0, 0);
        rechts.Controls.Add(new Label { Text = "Eingeteilt:", AutoSize = true }, 0, 1);
        rechts.Controls.Add(_eingeteilt, 0, 2);
        rechts.Controls.Add(new Label { Text = "Lehrervorschlag (Doppelklick = eintragen):", AutoSize = true }, 0, 3);
        rechts.Controls.Add(_vorschlag, 0, 4);
        _bedarf.ValueChanged += (_, _) => BedarfGeaendert();

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_raster);
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
        _raster.Columns[_raster.Columns.Add("S", "Std.")].Width = 40;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
        {
            var c = _raster.Columns[_raster.Columns.Add("T" + t, Planungsdienst.Tagname(D, t))];
            c.Width = 110;
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
                werte[1 + t] = bedarf == 0 && namen.Count == 0 ? "" : $"{namen.Count}/{bedarf}  {string.Join(", ", namen)}";
            }
            _raster.Rows.Add(werte);
        }
        var gesamtBedarf = D.Bereitschaftsbedarf.Sum(b => b.Anzahl);
        Text = $"Bereitschaftsplanung – {stunden.Count} Bereitschaften, Bedarf {gesamtBedarf}";
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
            _vorschlag.Rows[_vorschlag.Rows.Add(k.Lehrer.Kurzname, (int)k.Punkte, k.BereitschaftenWoche, k.FreiesDeputat.ToString("+0.#;-0.#;0"), k.Hinweis)].Tag = k.Lehrer;
        _laedt = false;
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
