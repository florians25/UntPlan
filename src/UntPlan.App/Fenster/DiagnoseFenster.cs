using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Stundenplan-Diagnose: links das Auswahlfenster mit den Karteikarten "Eingabedaten" und "Stundenplan",
/// rechts das Detailfenster mit Beschreibung und den konkreten Fällen.
/// </summary>
public sealed class DiagnoseFenster : MdiFenster
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _eingabe = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _plan = new() { Dock = DockStyle.Fill };
    private readonly TextBox _beschreibung = new() { Dock = DockStyle.Top, Height = 80, Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(250, 250, 240), ScrollBars = ScrollBars.Vertical };
    private readonly Label _titel = new() { Dock = DockStyle.Top, Height = 22, Font = Ui.SchriftFett, Padding = new Padding(4, 3, 0, 0), Text = "Art der Diagnose" };
    private readonly DataGridView _faelle = new() { Dock = DockStyle.Fill };

    public DiagnoseFenster()
    {
        Text = "Stundenplan-Diagnose";
        HilfeThema = "Diagnose";
        Icon = SymbolIcon("diagnose");
        Size = new Size(940, 480);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Aktualisieren", "diagnose", Neuaufbau, false));
        ts.Items.Add(Knopf("Betroffenen Stundenplan anzeigen", "stundenplan", PlanZeigen, false));
        foreach (var g in new[] { _eingabe, _plan })
        {
            Ui.RasterGestalten(g, false);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.RowHeadersVisible = false;
            g.Columns.Add("Diagnose", "Diagnose");
            g.Columns.Add("Gew", "Gew.");
            g.Columns.Add("Anz", "Anz.");
            g.Columns[0].Width = 230;
            g.Columns[1].Width = 50;
            g.Columns[2].Width = 45;
            g.SelectionChanged += (_, _) => Details(g);
            g.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && g.Rows[e.RowIndex].Tag is null) { e.CellStyle!.Font = Ui.SchriftFett; e.CellStyle.BackColor = Ui.Kopf; } };
        }
        _tabs.TabPages.Add(new TabPage("Eingabedaten") { Controls = { _eingabe } });
        _tabs.TabPages.Add(new TabPage("Stundenplan") { Controls = { _plan } });
        _tabs.SelectedIndexChanged += (_, _) => Details(_tabs.SelectedIndex == 0 ? _eingabe : _plan);
        Ui.RasterGestalten(_faelle, false);
        _faelle.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _faelle.RowHeadersVisible = false;
        _faelle.Columns.Add("E", "Element");
        _faelle.Columns.Add("U", "U-Nr");
        _faelle.Columns.Add("Z", "Zeit");
        _faelle.Columns.Add("D", "Detail");
        _faelle.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _faelle.CellDoubleClick += (_, _) => PlanZeigen();
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340 };
        split.Panel1.Controls.Add(_tabs);
        split.Panel2.Controls.Add(_faelle);
        split.Panel2.Controls.Add(_beschreibung);
        split.Panel2.Controls.Add(_titel);
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = 340;
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art != AenderungsArt.Vertretung) Neuaufbau();
    }

    private void Neuaufbau()
    {
        using (new Dialoge.WarteCursor())
        {
            Fuellen(_eingabe, Diagnose.Eingabedaten(D));
            Fuellen(_plan, Diagnose.Stundenplan(D));
        }
    }

    private static void Fuellen(DataGridView g, List<DiagnosePunkt> punkte)
    {
        g.Rows.Clear();
        foreach (var gruppe in punkte.GroupBy(p => p.Gruppe))
        {
            g.Rows.Add(gruppe.Key, "", gruppe.Sum(p => p.Faelle.Count));
            foreach (var p in gruppe)
            {
                var r = g.Rows.Add("   " + p.Titel, p.Gewicht, p.Faelle.Count);
                g.Rows[r].Tag = p;
                if (p.Faelle.Count == 0) g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
                else if (p.Gewicht >= 5) g.Rows[r].DefaultCellStyle.ForeColor = Color.DarkRed;
            }
        }
    }

    private void Details(DataGridView g)
    {
        _faelle.Rows.Clear();
        if (g.SelectedRows.Count == 0 || g.SelectedRows[0].Tag is not DiagnosePunkt p)
        {
            _titel.Text = "Art der Diagnose";
            _beschreibung.Text = "";
            return;
        }
        _titel.Text = $"{p.Rubrik} | {p.Gruppe} | {p.Titel}";
        _beschreibung.Text = p.Beschreibung;
        foreach (var f in p.Faelle)
        {
            var r = _faelle.Rows.Add(f.Element, f.UnterrichtId?.ToString() ?? "", f.Zeit, f.Detail);
            _faelle.Rows[r].Tag = f;
        }
    }

    private void PlanZeigen()
    {
        if (_faelle.CurrentRow?.Tag is not DiagnoseFall f) return;
        if (f.Typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum && f.ElementId.HasValue) StundenplanFenster.Neu(f.Typ.Value, f.ElementId, false);
        else if (f.UnterrichtId is int uid && Ix.Unterrichte.TryGetValue(uid, out var u) && u.KlassenIds.Any()) StundenplanFenster.Neu(ElementTyp.Klasse, u.KlassenIds.First(), false);
        else if (f.UnterrichtId is int uid2 && Ix.Unterrichte.TryGetValue(uid2, out var u2) && u2.LehrerIds.Any()) StundenplanFenster.Neu(ElementTyp.Lehrer, u2.LehrerIds.First(), false);
    }
}
