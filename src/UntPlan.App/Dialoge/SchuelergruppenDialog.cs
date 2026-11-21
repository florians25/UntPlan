using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// „Unterrichte mit inkonsistenten Schülergruppen“: Unterrichte, die über mehrere Perioden hinweg unterschiedliche Schülergruppen haben,
/// mit den Feldinhalten je Periode (unterschiedliche Werte gelb wie im Periodenüberblick). Die Schülergruppe der markierten Periode lässt
/// sich in alle Perioden übernehmen – sie muss für einen Unterricht über alle Perioden gleich sein (Klassenbuch in UntPlanWeb).
/// </summary>
public static class SchuelergruppenDialog
{
    /// <summary>Fenster zeigen; <paramref name="meldenWennLeer"/>: ohne Befund eine kurze Meldung statt des Fensters.</summary>
    public static void Zeigen(IWin32Window owner, bool meldenWennLeer)
    {
        var d = AppZustand.D;
        if (Schuelergruppen.Inkonsistente(d).Count == 0)
        {
            if (meldenWennLeer) Ui.Info("Alle Unterrichte haben in allen Perioden dieselbe Schülergruppe.", "Unterrichte mit inkonsistenten Schülergruppen", owner);
            return;
        }
        var perioden = d.Perioden.OrderBy(p => p.Von).ThenBy(p => p.Id).ToList();
        using var f = new Dialog("Unterrichte mit inkonsistenten Schülergruppen", 640, 330) { FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = true };
        f.Ok.Text = "Schließen";
        f.Abbrechen.Visible = false;
        f.Ok.Left = f.Abbrechen.Left;
        var hinweis = new Label
        {
            Left = 12, Top = 10, Width = 610, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Unterricht hat über mehrere Perioden hinweg unterschiedliche Schülergruppen. Bitte prüfen Sie die Korrektheit der Schülergruppen " +
                   "in diesem Unterricht. Markieren Sie die Zelle der richtigen Schülergruppe und wählen Sie „In alle Perioden übernehmen“.",
        };
        var g = new DataGridView
        {
            Left = 12, Top = 48, Width = 610, Height = 200, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Name = "Inkonsistent",
        };
        Ui.RasterGestalten(g, false);
        g.ReadOnly = true;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.MultiSelect = false;
        g.Columns[g.Columns.Add("UNr", "Unt. Nr.")].Width = 60;
        foreach (var p in perioden) g.Columns[g.Columns.Add("P" + p.Id, p.Name)].Width = 150;
        var uebernehmen = new Button { Text = "In alle Perioden übernehmen", Left = 12, Width = 190, Height = 25, Top = 258, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        void Fuellen()
        {
            g.Rows.Clear();
            foreach (var x in Schuelergruppen.Inkonsistente(AppZustand.D))
            {
                var werte = perioden.Select(p => (object)(x.Perioden.FirstOrDefault(y => y.Periode.Id == p.Id) is { Periode: not null } y ? y.Schuelergruppe : "(nicht vorhanden)"));
                var i = g.Rows.Add(new object[] { x.UnterrichtId }.Concat(werte).ToArray());
                g.Rows[i].Tag = x.UnterrichtId;
                var aktiv = x.Perioden.FirstOrDefault(y => y.Periode.Id == AppZustand.D.AktivePeriodeId).Schuelergruppe;
                for (var k = 1; k < g.Columns.Count; k++)
                    if (g.Rows[i].Cells[k].Value is string t && t != aktiv && !t.StartsWith('–')) g.Rows[i].Cells[k].Style.BackColor = Color.FromArgb(255, 245, 160);
            }
            uebernehmen.Enabled = g.Rows.Count > 0;
        }
        uebernehmen.Click += (_, _) =>
        {
            if (g.CurrentCell is not { } c || g.Rows[c.RowIndex].Tag is not int id) return;
            var gruppe = c.ColumnIndex > 0 && c.Value is string t && !t.StartsWith('–') ? t
                : AppZustand.D.AlleUnterrichte.FirstOrDefault(u => u.Id == id && u.Schuelergruppe.Length > 0)?.Schuelergruppe ?? "";
            if (gruppe.Length == 0) return;
            try { AppZustand.Doc.Aendern($"Schülergruppe U-Nr. {id} angleichen", AenderungsArt.Unterricht, dd => Schuelergruppen.InAllenPerioden(dd, id, gruppe)); }
            catch (InvalidOperationException ex) { Ui.Warnung(ex.Message, owner: f); return; }
            Fuellen();
            if (g.Rows.Count == 0) f.Close();
        };
        f.Controls.AddRange(new Control[] { hinweis, g, uebernehmen });
        Fuellen();
        f.ShowDialog(owner);
    }
}
