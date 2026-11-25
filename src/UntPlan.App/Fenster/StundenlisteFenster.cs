using UntPlan.Core;

namespace UntPlan.App.Fenster;

/// <summary>
/// Handbuch 6.4.4 Stundenlisten: Stundenplan in Listenform (wie ein Vorlesungsverzeichnis), wahlweise nach Klassen oder nach Lehrkräften
/// sortiert. Spalten lassen sich wie in den Stammdaten- und Unterrichtsfenstern sortieren; Druck und Export nach Excel.
/// </summary>
public sealed class StundenlisteFenster : MdiFenster, IDruckbar, IExcelExport
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private bool _nachLehrern;
    private readonly ToolStripButton _klassen;
    private readonly ToolStripButton _lehrer;

    public StundenlisteFenster(bool nachLehrern = false)
    {
        _nachLehrern = nachLehrern;
        HilfeThema = "Stundenpläne";
        Size = new Size(820, 480);
        Positionieren();
        var ts = Werkzeugleiste();
        _klassen = new ToolStripButton("nach Klassen", Symbole.Get("klassen", 16)) { CheckOnClick = false };
        _lehrer = new ToolStripButton("nach Lehrern", Symbole.Get("lehrer", 16)) { CheckOnClick = false };
        _klassen.Click += (_, _) => Umschalten(false);
        _lehrer.Click += (_, _) => Umschalten(true);
        ts.Items.Add(_klassen);
        ts.Items.Add(_lehrer);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.AllowUserToAddRows = false;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, t, w) in new[] { ("E", "Klasse", 70), ("T", "Tag", 50), ("S", "Std.", 40), ("Z", "Zeit", 80), ("F", "Fach", 60), ("L", "Lehrer", 70), ("K", "Klassen", 90), ("R", "Raum", 60), ("U", "U-Nr", 50), ("X", "Text", 160) })
            _g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.Automatic });
        _g.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not StundenlistenZeile z) return;
            if (AppZustand.Doc.Index.Unterrichte.TryGetValue(z.UnterrichtId, out var u)) AppZustand.StundeWaehlen(u, AppZustand.D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == u.Id && p.Tag == z.Tag && p.Stunde == z.Stunde), this);
        };
        Controls.Add(_g);
        Controls.Add(ts);
        Fuellen();
    }

    private void Umschalten(bool nachLehrern)
    {
        _nachLehrern = nachLehrern;
        Fuellen();
    }

    protected override void Aktualisieren(AenderungsArt art) => Fuellen();

    private void Fuellen()
    {
        Text = _nachLehrern ? "Stundenliste Lehrer" : "Stundenliste Klassen";
        _klassen.Checked = !_nachLehrern;
        _lehrer.Checked = _nachLehrern;
        _g.Columns["E"]!.HeaderText = _nachLehrern ? "Lehrer" : "Klasse";
        _g.Columns["L"]!.Visible = !_nachLehrern;
        _g.Columns["K"]!.Visible = _nachLehrern;
        _g.Rows.Clear();
        string? vorher = null;
        foreach (var z in Stundenliste.Zeilen(AppZustand.D, _nachLehrern))
        {
            var i = _g.Rows.Add(z.Element, z.Tagname, AppZustand.D.Zeitraster.StundenLabel(z.Stunde), z.Zeit, z.Fach, z.Lehrer, z.Klassen, z.Raum, z.UnterrichtId, z.Text);
            _g.Rows[i].Tag = z;
            // neues Element: fett wie eine Gruppenüberschrift
            if (z.Element != vorher) _g.Rows[i].Cells["E"].Style.Font = Ui.SchriftFett;
            vorher = z.Element;
        }
    }

    public void Drucken(bool vorschau) => ListenDruck.DruckenTabelle(this, Text, _g, vorschau);

    public string ExcelName => Text;

    public IReadOnlyList<Core.Austausch.Excel.Blatt> ExcelBlaetter() => new[] { ExcelAusgabe.Blatt(Text, _g) };
}
