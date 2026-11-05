using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Fenster „Fixierter/Ignorierter Unterricht“ (Handbuch 3.4.8): alle fixierten bzw. ignorierten Unterrichte mit der Ebene, auf der fixiert
/// bzw. ignoriert wurde (Unterricht, Stunde, Element). Eine Fixierung wird gelöst, indem man die verantwortliche Zelle anklickt und
/// „Löschen“ betätigt.
/// </summary>
public sealed class FixierterUnterrichtFenster : MdiFenster
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _fixiert = new() { Dock = DockStyle.Fill, Name = "Fixiert" };
    private readonly DataGridView _ignoriert = new() { Dock = DockStyle.Fill, Name = "Ignoriert" };
    private const string Haken = "✔";

    public FixierterUnterrichtFenster()
    {
        Text = "Fixierter/Ignorierter Unterricht";
        HilfeThema = "Unterricht";
        Icon = SymbolIcon("fixieren");
        Size = new Size(560, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(Knopf("Normalform anzeigen", "normalform", () =>
        {
            var g = _tabs.SelectedIndex == 0 ? _fixiert : _ignoriert;
            g.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            Width = Math.Max(420, g.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width) + 40);
        }));
        foreach (var (g, art) in new[] { (_fixiert, "fix."), (_ignoriert, "ign.") })
        {
            Ui.RasterGestalten(g, false);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.SelectionMode = DataGridViewSelectionMode.CellSelect;
            g.MultiSelect = false;
            g.Columns.Add("UNr", "U-Nr");
            g.Columns.Add("Lehrer", "Lehrer");
            g.Columns.Add("Fach", "Fach");
            g.Columns.Add("Klassen", "Klasse(n)");
            g.Columns.Add("Unterricht", "Unterricht " + art);
            if (g == _fixiert) g.Columns.Add("Stunde", "Stunde fix.");
            g.Columns.Add("Element", "Element " + art);
            g.Columns["UNr"]!.Width = 50;
            g.Columns["Klassen"]!.Width = 110;
            g.Columns["Unterricht"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (g == _fixiert) g.Columns["Stunde"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.CellFormatting += (_, e) => { if (e.Value as string == Haken) e.CellStyle!.ForeColor = Color.FromArgb(40, 160, 60); };
        }
        _tabs.TabPages.Add(new TabPage("Fixiert") { Controls = { _fixiert } });
        _tabs.TabPages.Add(new TabPage("Ignoriert") { Controls = { _ignoriert } });
        Controls.Add(_tabs);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art != AenderungsArt.Vertretung) Neuaufbau();
    }

    private static IEnumerable<Element> Elemente(Unterricht u, DatenIndex ix, Schuldatei d) =>
        u.KlassenIds.Select(id => (Element?)d.Klassen.FirstOrDefault(k => k.Id == id))
            .Concat(u.LehrerIds.Select(id => (Element?)d.Lehrer.FirstOrDefault(l => l.Id == id)))
            .Concat(u.Zeilen.Select(z => z.FachId).Distinct().Select(id => (Element?)d.Faecher.FirstOrDefault(f => f.Id == id)))
            .Concat(u.Zeilen.SelectMany(z => new[] { z.FachraumId, z.StammraumId }).Where(x => x.HasValue).Distinct().Select(id => (Element?)d.Raeume.FirstOrDefault(r => r.Id == id)))
            .OfType<Element>();

    private void Neuaufbau()
    {
        _fixiert.Rows.Clear();
        _ignoriert.Rows.Clear();
        var alle = D.Unterrichte.Count;
        int nFix = 0, nIgn = 0;
        foreach (var u in D.Unterrichte.OrderBy(x => x.Id))
        {
            var stundeFix = D.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Fixiert);
            var elFix = Elemente(u, Ix, D).Where(e => e.Fix).Select(e => e.Kurzname).Distinct().ToList();
            var elIgn = Elemente(u, Ix, D).Where(e => e.Ignorieren).Select(e => e.Kurzname).Distinct().ToList();
            var z = u.Zeilen.FirstOrDefault();
            if (u.Fix || stundeFix || elFix.Count > 0)
            {
                nFix++;
                var r = _fixiert.Rows.Add(u.Id, Ix.KLehrer(z?.LehrerId), Ix.KFach(z?.FachId), Ix.KKlassen(u.KlassenIds.ToList()), u.Fix ? Haken : "", stundeFix ? Haken : "", string.Join(",", elFix));
                _fixiert.Rows[r].Tag = u;
            }
            if (u.Ignorieren || elIgn.Count > 0)
            {
                nIgn++;
                var r = _ignoriert.Rows.Add(u.Id, Ix.KLehrer(z?.LehrerId), Ix.KFach(z?.FachId), Ix.KKlassen(u.KlassenIds.ToList()), u.Ignorieren ? Haken : "", string.Join(",", elIgn));
                _ignoriert.Rows[r].Tag = u;
            }
        }
        string Prozent(int n) => alle == 0 ? "0%" : $"{Math.Round(100.0 * n / alle):0}%";
        _tabs.TabPages[0].Text = $"Fixiert ({Prozent(nFix)})";
        _tabs.TabPages[1].Text = $"Ignoriert ({Prozent(nIgn)})";
    }

    /// <summary>Fixierung bzw. Ignorieren der angeklickten (verantwortlichen) Zelle aufheben.</summary>
    private void Loeschen()
    {
        var g = _tabs.SelectedIndex == 0 ? _fixiert : _ignoriert;
        if (g.CurrentCell is not { } c || g.Rows[c.RowIndex].Tag is not Unterricht u || (c.Value?.ToString() ?? "").Length == 0)
        {
            Ui.Info("Bitte die Zelle anklicken, die für die Fixierung verantwortlich ist (Unterricht, Stunde oder Element).");
            return;
        }
        var spalte = g.Columns[c.ColumnIndex].Name;
        var ignorieren = g == _ignoriert;
        switch (spalte)
        {
            case "Unterricht":
                Aendern(ignorieren ? "Ignorieren aufheben" : "Fixierung lösen", AenderungsArt.Unterricht, d =>
                {
                    var x = d.Unterrichte.First(y => y.Id == u.Id);
                    if (ignorieren) x.Ignorieren = false; else x.Fix = false;
                });
                break;
            case "Stunde":
                Aendern("Fixierung lösen", AenderungsArt.Stundenplan, d => { foreach (var p in d.Stundenplan.Where(p => p.UnterrichtId == u.Id)) p.Fixiert = false; });
                break;
            case "Element":
                var namen = (c.Value?.ToString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                if (!Ui.Frage($"{(ignorieren ? "Ignorieren" : "Fixierung")} bei den Stammdaten von {string.Join(", ", namen)} aufheben? Das betrifft alle Unterrichte dieser Elemente.")) return;
                Aendern(ignorieren ? "Ignorieren aufheben" : "Fixierung lösen", AenderungsArt.Stammdaten, d =>
                {
                    foreach (var e in Elemente(d.Unterrichte.First(y => y.Id == u.Id), Ix, d).Where(e => namen.Contains(e.Kurzname)))
                        if (ignorieren) e.Ignorieren = false; else e.Fix = false;
                });
                break;
            default:
                return;
        }
        Neuaufbau();
    }
}
