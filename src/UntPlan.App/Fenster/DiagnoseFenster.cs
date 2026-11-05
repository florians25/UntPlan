using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Stundenplan-Diagnose wie im Handbuch (4.4.6): Symbolleiste „Normalform anzeigen“, „Neu rechnen“, „Filter“; links das Auswahlfenster mit den
/// Karteikarten "Eingabedaten" und "Stundenplan" (Rubriken auf- und zuklappbar, Spalten „Diagnose“, „Gw.“, „Anz“, darunter die Filterzeile –
/// standardmäßig „Anz &gt;= 1“), rechts das Detailfenster mit Beschreibung, dem Link „Betroffene Fenster anzeigen“ und den konkreten Fällen.
/// Über das Kontextmenü lassen sich Verstöße ignorieren und ignorierte Verstöße ausblenden.
/// </summary>
public sealed class DiagnoseFenster : MdiFenster
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _eingabe = new() { Dock = DockStyle.Fill, Name = "Eingabedaten" };
    private readonly DataGridView _plan = new() { Dock = DockStyle.Fill, Name = "Stundenplan" };
    private readonly TextBox _beschreibung = new() { Dock = DockStyle.Top, Height = 80, Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(250, 250, 240), ScrollBars = ScrollBars.Vertical };
    private readonly Label _titel = new() { Dock = DockStyle.Top, Height = 22, Font = Ui.SchriftFett, Padding = new Padding(4, 3, 0, 0), Text = "Art der Diagnose" };
    private readonly LinkLabel _betroffen = new() { Dock = DockStyle.Top, Height = 20, Text = "Betroffene Fenster anzeigen", Padding = new Padding(4, 2, 0, 0) };
    private readonly DataGridView _faelle = new() { Dock = DockStyle.Fill };
    private readonly ToolStripButton _filterKnopf;
    /// <summary>Filter je Karteikarte: Mindestgewicht (0 = alle) und Mindestanzahl (0 = alle, 1 = „&gt;= 1“).</summary>
    private readonly Dictionary<DataGridView, (int Gw, int Anz)> _filter = new();
    private readonly HashSet<string> _zugeklappt = new();
    /// <summary>Als ignoriert gekennzeichnete Verstöße (nur für die Übersicht der Diagnose, ohne Wirkung auf die Optimierung).</summary>
    private readonly HashSet<string> _ignoriert = new();
    private bool _ignorierteAusblenden;
    private List<DiagnosePunkt> _punkteEingabe = new(), _punktePlan = new();

    /// <summary>Datums-Einstellung unter der Symbolleiste: die Diagnose bezieht sich immer auf die Woche dieses Datums.</summary>
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Short, Width = 104, Left = 4, Top = 3 };
    private readonly Label _wocheText = new() { Left = 116, Top = 6, AutoSize = true, ForeColor = Color.DimGray };

    private static readonly string[] GwFilter = { "Alle", ">= 1", ">= 2", ">= 3", ">= 4", ">= 5" };
    private static readonly string[] AnzFilter = { "Alle", ">= 1" };

    public DiagnoseFenster()
    {
        Text = "Stundenplan-Diagnose";
        HilfeThema = "Diagnose";
        Icon = SymbolIcon("diagnose");
        Size = new Size(940, 480);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Normalform anzeigen", "normalform", () => Size = new Size(Math.Min(960, (AppZustand.Haupt?.ClientSize.Width ?? 1000) - 40), Math.Min(560, (AppZustand.Haupt?.ClientSize.Height ?? 700) - 60))));
        ts.Items.Add(Knopf("Neu rechnen", "aktualisieren", Neuaufbau));
        _filterKnopf = Knopf("Filter", "filter", FilterUmschalten);
        _filterKnopf.Checked = true;
        ts.Items.Add(_filterKnopf);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Betroffenen Stundenplan anzeigen", "stundenplan", PlanZeigen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Totaldiagnose", "diagnose", () => HauptFenster.Oeffnen("totaldiag", () => new TotaldiagnoseFenster()), false));
        ts.Items.Add(Knopf("CCC-Analyse", "lehrer", () => HauptFenster.Oeffnen("ccc", () => new CccFenster()), false));
        foreach (var g in new[] { _eingabe, _plan })
        {
            _filter[g] = (0, 1);
            Ui.RasterGestalten(g, false);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Diagnose", HeaderText = "⊟ Diagnose", Width = 250, SortMode = DataGridViewColumnSortMode.NotSortable });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gew", HeaderText = "Gw.", Width = 44, SortMode = DataGridViewColumnSortMode.NotSortable });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Anz", HeaderText = "Anz", Width = 44, SortMode = DataGridViewColumnSortMode.NotSortable });
            var raster = g;
            g.SelectionChanged += (_, _) => Details(raster);
            g.CellFormatting += (_, e) => Formatieren(raster, e);
            g.CellClick += (_, e) => Klick(raster, e);
            g.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) Kontextmenue(raster, e.RowIndex); };
        }
        _tabs.TabPages.Add(new TabPage("Eingabedaten") { Controls = { _eingabe } });
        _tabs.TabPages.Add(new TabPage("Stundenplan") { Controls = { _plan } });
        _tabs.SelectedIndexChanged += (_, _) => Details(_tabs.SelectedIndex == 0 ? _eingabe : _plan);
        Ui.RasterGestalten(_faelle, false);
        _faelle.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _faelle.RowHeadersVisible = false;
        _faelle.AllowUserToAddRows = false;
        _faelle.Columns.Add("S", "Status");
        _faelle.Columns.Add("E", "Element");
        _faelle.Columns.Add("U", "U-Nr");
        _faelle.Columns.Add("Z", "Zeit");
        _faelle.Columns.Add("D", "Detail");
        _faelle.Columns[0].Width = 60;
        _faelle.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _faelle.CellDoubleClick += (_, _) => PlanZeigen();
        _faelle.CellMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || _faelle.Rows[e.RowIndex].Tag is not DiagnoseFall f) return;
            _faelle.CurrentCell = _faelle[Math.Max(0, e.ColumnIndex), e.RowIndex];
            var cm = new ContextMenuStrip { Font = Ui.Schrift };
            var key = FallSchluessel(f);
            cm.Items.Add(_ignoriert.Contains(key) ? "Verstoß nicht mehr ignorieren" : "Verstoß ignorieren", null, (_, _) => { if (!_ignoriert.Remove(key)) _ignoriert.Add(key); AnsichtErneuern(); });
            cm.Items.Add(new ToolStripMenuItem("Ignorierte Verstöße ausblenden", null, (_, _) => { _ignorierteAusblenden = !_ignorierteAusblenden; AnsichtErneuern(); }) { Checked = _ignorierteAusblenden });
            cm.Show(_faelle, _faelle.PointToClient(Cursor.Position));
        };
        _betroffen.LinkClicked += (_, _) => PlanZeigen();
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 360 };
        split.Panel1.Controls.Add(_tabs);
        split.Panel2.Controls.Add(_faelle);
        split.Panel2.Controls.Add(_betroffen);
        split.Panel2.Controls.Add(_beschreibung);
        split.Panel2.Controls.Add(_titel);
        var datumZeile = new Panel { Dock = DockStyle.Top, Height = 28 };
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _datum.ValueChanged += (_, _) => Neuaufbau();
        datumZeile.Controls.AddRange(new Control[] { _datum, _wocheText });
        Controls.Add(split);
        Controls.Add(datumZeile);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = 360;
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art != AenderungsArt.Vertretung) Neuaufbau();
    }

    private void Neuaufbau()
    {
        var datum = DateOnly.FromDateTime(_datum.Value);
        var montag = Kalender.Montag(datum);
        _wocheText.Text = $"Woche {Format.Datum(montag)} – {Format.Datum(montag.AddDays(Math.Max(1, D.Zeitraster.TageProWoche) - 1))}, Schulwoche {Kalender.Schulwoche(D, datum)}";
        using (new Dialoge.WarteCursor())
        {
            // Mehrwochenstundenplan: nur der in dieser Woche gültige Unterricht
            var woche = Diagnose.FuerWoche(D, datum);
            _punkteEingabe = Diagnose.Eingabedaten(woche);
            _punktePlan = Diagnose.Stundenplan(woche);
        }
        AnsichtErneuern();
    }

    private void AnsichtErneuern()
    {
        Fuellen(_eingabe, _punkteEingabe);
        Fuellen(_plan, _punktePlan);
        Details(_tabs.SelectedIndex == 0 ? _eingabe : _plan);
    }

    private static string FallSchluessel(DiagnoseFall f) => $"{f.Element}|{f.UnterrichtId}|{f.Zeit}|{f.Detail}";

    private int Anzahl(DiagnosePunkt p) => p.Faelle.Count(f => !_ignorierteAusblenden || !_ignoriert.Contains(FallSchluessel(f)));

    private void Fuellen(DataGridView g, List<DiagnosePunkt> punkte)
    {
        var (gw, anz) = _filterKnopf.Checked ? _filter[g] : (0, 0);
        g.Rows.Clear();
        // Filterzeile unter den Überschriften (grün hinterlegt, wenn ein Filter wirkt)
        var fz = g.Rows[g.Rows.Add("", GwFilter[gw], AnzFilter[anz])];
        fz.Tag = "filter";
        foreach (var gruppe in punkte.GroupBy(p => p.Gruppe))
        {
            var sichtbar = gruppe.Where(p => p.Gewicht >= gw && Anzahl(p) >= anz).ToList();
            var summe = gruppe.Sum(Anzahl);
            var zu = _zugeklappt.Contains(g.Name + "|" + gruppe.Key);
            var gr = g.Rows[g.Rows.Add((zu ? "⊞ " : "⊟ ") + gruppe.Key, "", summe == 0 ? "" : summe.ToString())];
            gr.Tag = new Gruppenzeile(gruppe.Key, gruppe.Any(p => p.Gewicht >= 5 && Anzahl(p) > 0));
            if (zu) continue;
            foreach (var p in sichtbar)
            {
                var r = g.Rows.Add("     " + p.Titel, p.Gewicht > 0 ? p.Gewicht.ToString() : "*", Anzahl(p));
                g.Rows[r].Tag = p;
                if (Anzahl(p) == 0) g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
                else if (p.Gewicht >= 5) g.Rows[r].DefaultCellStyle.ForeColor = Color.Red;
            }
        }
    }

    private sealed record Gruppenzeile(string Name, bool Schwer);

    private void Formatieren(DataGridView g, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var tag = g.Rows[e.RowIndex].Tag;
        if (tag is "filter")
        {
            var aktiv = _filterKnopf.Checked && (e.ColumnIndex == 1 ? _filter[g].Gw > 0 : e.ColumnIndex == 2 && _filter[g].Anz > 0);
            e.CellStyle!.BackColor = aktiv ? Color.FromArgb(170, 240, 170) : Color.FromArgb(236, 241, 246);
            e.CellStyle.ForeColor = Color.FromArgb(30, 60, 160);
            return;
        }
        if (tag is Gruppenzeile gz)
        {
            e.CellStyle!.Font = Ui.SchriftFett;
            e.CellStyle.BackColor = Ui.Kopf;
            // rot hinterlegte Anzahl: mindestens ein schwer wiegendes (hoch gewichtetes) Problem
            if (e.ColumnIndex == 2 && gz.Schwer) e.CellStyle.BackColor = Color.FromArgb(240, 190, 200);
        }
        else if (tag is DiagnosePunkt p && e.ColumnIndex == 2 && p.Gewicht >= 5 && Anzahl(p) > 0) e.CellStyle!.BackColor = Color.FromArgb(240, 190, 200);
    }

    private void Klick(DataGridView g, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var tag = g.Rows[e.RowIndex].Tag;
        if (tag is Gruppenzeile gz)
        {
            var k = g.Name + "|" + gz.Name;
            if (!_zugeklappt.Remove(k)) _zugeklappt.Add(k);
            Fuellen(g, g == _eingabe ? _punkteEingabe : _punktePlan);
        }
        else if (tag is "filter" && e.ColumnIndex is 1 or 2)
        {
            // Filterauswahl: Gw. „Alle, >= 1 … >= 5“, Anz „Alle, >= 1“
            var cm = new ContextMenuStrip { Font = Ui.Schrift };
            var liste = e.ColumnIndex == 1 ? GwFilter : AnzFilter;
            for (var i = 0; i < liste.Length; i++)
            {
                var wert = i;
                var spalte = e.ColumnIndex;
                cm.Items.Add(new ToolStripMenuItem(liste[i], null, (_, _) =>
                {
                    var f = _filter[g];
                    _filter[g] = spalte == 1 ? (wert, f.Anz) : (f.Gw, wert);
                    if (!_filterKnopf.Checked) _filterKnopf.Checked = true;
                    Fuellen(g, g == _eingabe ? _punkteEingabe : _punktePlan);
                }) { Checked = (e.ColumnIndex == 1 ? _filter[g].Gw : _filter[g].Anz) == i });
            }
            var r = g.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            cm.Show(g, new Point(r.Left, r.Bottom));
        }
    }

    private void FilterUmschalten()
    {
        _filterKnopf.Checked = !_filterKnopf.Checked;
        AnsichtErneuern();
    }

    private void Kontextmenue(DataGridView g, int zeile)
    {
        if (g.Rows[zeile].Tag is not DiagnosePunkt p) return;
        g.ClearSelection();
        g.Rows[zeile].Selected = true;
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Verstoß ignorieren", null, (_, _) => { foreach (var f in p.Faelle) _ignoriert.Add(FallSchluessel(f)); AnsichtErneuern(); });
        cm.Items.Add(new ToolStripMenuItem("Ignorierte Verstöße ausblenden", null, (_, _) => { _ignorierteAusblenden = !_ignorierteAusblenden; AnsichtErneuern(); }) { Checked = _ignorierteAusblenden });
        cm.Show(g, g.PointToClient(Cursor.Position));
    }

    private void Details(DataGridView g)
    {
        _faelle.Rows.Clear();
        if (g.SelectedRows.Count == 0 || g.SelectedRows[0].Tag is not DiagnosePunkt p)
        {
            _titel.Text = "Art der Diagnose";
            _beschreibung.Text = "";
            _betroffen.Visible = false;
            return;
        }
        _titel.Text = $"{p.Rubrik} | {p.Gruppe} | {p.Titel}";
        _beschreibung.Text = p.Beschreibung;
        _betroffen.Visible = p.Faelle.Count > 0;
        foreach (var f in p.Faelle)
        {
            var ign = _ignoriert.Contains(FallSchluessel(f));
            if (ign && _ignorierteAusblenden) continue;
            var r = _faelle.Rows.Add(ign ? "ignoriert" : "", f.Element, f.UnterrichtId?.ToString() ?? "", f.Zeit, f.Detail);
            _faelle.Rows[r].Tag = f;
            if (ign) _faelle.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private void PlanZeigen()
    {
        if ((_faelle.CurrentRow?.Tag ?? _faelle.Rows.Cast<DataGridViewRow>().FirstOrDefault()?.Tag) is not DiagnoseFall f) return;
        if (f.Typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum && f.ElementId.HasValue) StundenplanFenster.Neu(f.Typ.Value, f.ElementId, false);
        else if (f.UnterrichtId is int uid && Ix.Unterrichte.TryGetValue(uid, out var u) && u.KlassenIds.Any()) StundenplanFenster.Neu(ElementTyp.Klasse, u.KlassenIds.First(), false);
        else if (f.UnterrichtId is int uid2 && Ix.Unterrichte.TryGetValue(uid2, out var u2) && u2.LehrerIds.Any()) StundenplanFenster.Neu(ElementTyp.Lehrer, u2.LehrerIds.First(), false);
    }
}
