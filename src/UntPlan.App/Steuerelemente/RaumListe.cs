using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Liste „Mögliche Räume“ wie im Vertretungsvorschlag (Reiter Räume) und im Dialog „Raum zuordnen / löschen“:
/// Rm., Kap., Ausw-Rm., Ausw-Sr., Besetzt, Raumgruppe, Gang, Statistik, Kap.Diff. Häkchen werden grün gezeichnet.
/// Rechtsklick auf die Überschrift: Filterzeile ein/aus und Spalten ein-/ausblenden. Die Filter der Spalten lassen
/// sich beliebig kombinieren (z. B. nicht besetzte Ausweichräume des Stammraumes).
/// </summary>
public sealed class RaumListe : UserControl
{
    private static readonly (string Name, string Kopf, int Breite, bool Haken)[] AlleSpalten =
    {
        ("Rm", "Rm.", 62, false), ("Kap", "Kap.", 40, false), ("AR", "Ausw-Rm.", 62, true), ("SR", "Ausw-Sr.", 62, true), ("B", "Besetzt", 50, true),
        ("RG", "Raumgruppe", 84, false), ("Ga", "Gang", 50, false), ("St", "Statistik", 58, false), ("KD", "Kap.Diff.", 58, false),
    };

    public DataGridView Raster { get; } = new() { Dock = DockStyle.Fill };
    private readonly Panel _filter = new() { Dock = DockStyle.Top, Height = 24, Visible = false, BackColor = Color.FromArgb(255, 255, 230) };
    private readonly Dictionary<string, Control> _filterFelder = new();
    private List<RaumMoeglichkeit> _daten = new();
    private readonly string _speicherSchluessel;

    /// <summary>Langname der Räume anzeigen.</summary>
    public bool Langname { get; set; }

    /// <summary>Doppelklick bzw. Enter auf einen Raum.</summary>
    public event Action<RaumMoeglichkeit>? Gewaehlt;

    /// <param name="schluessel">Name für die gespeicherten sichtbaren Spalten.</param>
    /// <param name="sichtbar">Standardmäßig sichtbare Spalten (null = alle).</param>
    public RaumListe(string schluessel, IEnumerable<string>? sichtbar = null)
    {
        _speicherSchluessel = "raumliste:" + schluessel;
        Ui.RasterGestalten(Raster, false);
        Raster.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Raster.MultiSelect = false;
        Raster.RowHeadersVisible = false;
        Raster.ScrollBars = ScrollBars.Both;
        var standard = sichtbar?.ToHashSet();
        var gespeichert = AppZustand.Einstellungen.SichtbareSpalten.TryGetValue(_speicherSchluessel, out var sp) ? sp.ToHashSet() : null;
        foreach (var (n, k, b, haken) in AlleSpalten)
        {
            var c = Raster.Columns[Raster.Columns.Add(n, k)];
            c.Width = b;
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            c.Visible = gespeichert?.Contains(n) ?? standard?.Contains(n) ?? true;
            if (n is "Kap" or "KD") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (haken) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            Control feld = haken
                ? new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Items = { "", "✔", "–" }, SelectedIndex = 0, FlatStyle = FlatStyle.Flat }
                : new TextBox { BorderStyle = BorderStyle.FixedSingle };
            feld.Tag = n;
            if (feld is ComboBox cb) cb.SelectedIndexChanged += (_, _) => Fuellen();
            else feld.TextChanged += (_, _) => Fuellen();
            _filter.Controls.Add(feld);
            _filterFelder[n] = feld;
        }
        Raster.Columns["Rm"]!.Frozen = true;
        Raster.CellPainting += HakenZeichnen;
        Raster.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && Raster.Rows[e.RowIndex].Tag is RaumMoeglichkeit r) Gewaehlt?.Invoke(r); };
        Raster.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || Aktuell is not { } r) return;
            Gewaehlt?.Invoke(r);
            e.Handled = true;
        };
        Raster.ColumnHeaderMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) KopfMenue(Raster.PointToClient(Cursor.Position)); };
        Raster.ColumnWidthChanged += (_, _) => FilterAnordnen();
        Raster.ColumnStateChanged += (_, _) => FilterAnordnen();
        Raster.Scroll += (_, _) => FilterAnordnen();
        Raster.Resize += (_, _) => FilterAnordnen();
        Controls.Add(Raster);
        Controls.Add(_filter);
    }

    public RaumMoeglichkeit? Aktuell => Raster.CurrentRow?.Tag as RaumMoeglichkeit;

    public bool FilterzeileSichtbar
    {
        get => _filter.Visible;
        set
        {
            _filter.Visible = value;
            if (!value) foreach (var f in _filterFelder.Values) { if (f is ComboBox cb) cb.SelectedIndex = 0; else f.Text = ""; }
            FilterAnordnen();
        }
    }

    private void KopfMenue(Point pt)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var f = new ToolStripMenuItem("Filterzeile") { Checked = FilterzeileSichtbar };
        f.Click += (_, _) => FilterzeileSichtbar = !FilterzeileSichtbar;
        cm.Items.Add(f);
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add(new ToolStripLabel("Spalten ein-/ausblenden:") { ForeColor = Color.DimGray });
        foreach (DataGridViewColumn c in Raster.Columns)
        {
            if (c.Name == "Rm") continue;
            var spalte = c;
            var m = new ToolStripMenuItem(c.HeaderText) { Checked = c.Visible, CheckOnClick = true };
            m.CheckedChanged += (_, _) =>
            {
                spalte.Visible = m.Checked;
                AppZustand.Einstellungen.SichtbareSpalten[_speicherSchluessel] = Raster.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).Select(x => x.Name).ToList();
                AppZustand.Einstellungen.Speichern();
            };
            cm.Items.Add(m);
        }
        cm.Show(Raster, pt);
    }

    private void FilterAnordnen()
    {
        if (!_filter.Visible) return;
        var x0 = -Raster.HorizontalScrollingOffset;
        foreach (DataGridViewColumn c in Raster.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
        {
            var feld = _filterFelder[c.Name];
            feld.Visible = c.Visible;
            if (!c.Visible) continue;
            var r = Raster.GetColumnDisplayRectangle(c.Index, false);
            var links = r.Width > 0 ? r.X : x0;
            feld.SetBounds(links + 1, 1, Math.Max(10, c.Width - 2), 22);
            x0 = links + c.Width;
        }
    }

    private bool Passt(RaumMoeglichkeit r, string spalte, string wert)
    {
        if (_filterFelder[spalte] is ComboBox cb)
        {
            if (cb.SelectedIndex <= 0) return true;
            var ja = wert.Length > 0;
            return cb.SelectedIndex == 1 ? ja : !ja;
        }
        var t = _filterFelder[spalte].Text.Trim();
        return t.Length == 0 || wert.Contains(t, StringComparison.CurrentCultureIgnoreCase);
    }

    private string[] Werte(RaumMoeglichkeit r) => new[]
    {
        Langname && r.Raum.Langname.Length > 0 ? r.Raum.Langname : r.Raum.Kurzname, r.Raum.Kapazitaet > 0 ? r.Raum.Kapazitaet.ToString() : "",
        r.Ausweichraum ? "✔" : "", r.AusweichStammraum ? "✔" : "", r.Besetzt ? "✔" : "", r.Raumgruppen, r.Gang, r.Statistik, r.KapazitaetDiff?.ToString() ?? "",
    };

    /// <summary>Liste mit neuen Räumen füllen (die Auswahl bleibt nach Möglichkeit erhalten).</summary>
    public void Setzen(IEnumerable<RaumMoeglichkeit> daten)
    {
        _daten = daten.ToList();
        Fuellen();
    }

    private void Fuellen()
    {
        var alt = Aktuell?.Raum.Id;
        Raster.Rows.Clear();
        foreach (var r in _daten)
        {
            var w = Werte(r);
            if (_filter.Visible && !AlleSpalten.Select((s, i) => Passt(r, s.Name, w[i])).All(x => x)) continue;
            var i = Raster.Rows.Add(w);
            var row = Raster.Rows[i];
            row.Tag = r;
            var farbe = Ui.Farbe(r.Raum.Hintergrund, Color.Empty);
            if (farbe != Color.Empty) { row.Cells["Rm"].Style.BackColor = farbe; row.Cells["Rm"].Style.ForeColor = Ui.Textfarbe(farbe); }
            if (r.KapazitaetDiff < 0) row.Cells["KD"].Style.ForeColor = Color.Firebrick;
            if (r.Gesperrt) row.DefaultCellStyle.ForeColor = Color.Gray;
            if (r.Raum.Id == alt) Raster.CurrentCell = row.Cells["Rm"];
        }
    }

    /// <summary>Häkchen selbst zeichnen (grün, unabhängig von der Schriftart).</summary>
    private void HakenZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
        Ui.ZellHaken(e, Color.FromArgb(60, 170, 60));
    }
}
