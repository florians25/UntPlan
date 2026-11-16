using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// „Lehrer ändern“ bzw. „Lehrer hinzufügen“ im Kontextmenü des Stundenplans (Handbuch, Manuelles Planen | Lehrer ändern und
/// hinzufügen): unter „Neues Element“ die Lehrkräfte, wobei zur Zeit der Stunde bereits verplante rot hinterlegt sind; Optionen
/// „Für alle Stunden des Unterrichtes“ und „Lehrer mit Lehrbefähigung für das Fach“.
/// </summary>
public static class StundenLehrerDialog
{
    /// <summary>Ergebnis: true, wenn geändert wurde.</summary>
    public static bool Zeigen(IWin32Window owner, int stundeIndex, bool hinzufuegen)
    {
        var doc = AppZustand.Doc;
        if (stundeIndex < 0 || stundeIndex >= doc.Daten.Stundenplan.Count) return false;
        var p = doc.Daten.Stundenplan[stundeIndex];
        var ix = doc.Index;
        if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return false;
        using var d = new Dialog(hinzufuegen ? "Lehrer hinzufügen" : "Lehrer ändern", 460, 470);
        d.Controls.Add(new Label
        {
            Text = $"Unterricht {u.Id}: {ix.UnterrichtText(u)}\n{Planungsdienst.Tagname(doc.Daten, p.Tag, true)}, {p.Stunde}. Stunde",
            Left = 10, Top = 10, Width = 430, Height = 36,
        });
        var zeile = new ComboBox { Left = 120, Top = 52, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Visible = !hinzufuegen };
        for (var i = 0; i < u.Zeilen.Count; i++)
            zeile.Items.Add(u.Zeilen[i].LehrerId is int l ? ix.K(ElementTyp.Lehrer, l) : $"Zeile {i + 1} (ohne Lehrer)");
        if (zeile.Items.Count > 0) zeile.SelectedIndex = 0;
        if (!hinzufuegen)
        {
            d.Controls.Add(new Label { Text = "Altes Element:", Left = 10, Top = 55, Width = 104 });
            d.Controls.Add(zeile);
        }
        d.Controls.Add(new Label { Text = "Neues Element:", Left = 10, Top = 86, Width = 200 });
        var liste = new ListView { Left = 10, Top = 104, Width = 430, Height = 250, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        liste.Columns.Add("Lehrer", 110);
        liste.Columns.Add("Name", 200);
        liste.Columns.Add("Status", 100);
        var alle = new CheckBox { Text = "Für alle Stunden des Unterrichtes", Left = 10, Top = 362, AutoSize = true };
        var befaehigt = new CheckBox { Text = "Lehrer mit Lehrbefähigung für das Fach", Left = 10, Top = 386, AutoSize = true };
        d.Controls.AddRange(new Control[] { liste, alle, befaehigt });
        void Fuellen()
        {
            liste.Items.Clear();
            foreach (var k in StundenLehrer.Kandidaten(doc.Daten, p, alle.Checked, befaehigt.Checked))
                liste.Items.Add(new ListViewItem(new[] { k.Lehrer.Kurzname, k.Lehrer.Langname, k.Belegt ? "verplant" : "frei" })
                {
                    Tag = k.Lehrer, BackColor = k.Belegt ? Color.FromArgb(255, 200, 200) : Color.White,
                });
        }
        alle.CheckedChanged += (_, _) => Fuellen();
        befaehigt.CheckedChanged += (_, _) => Fuellen();
        liste.DoubleClick += (_, _) => d.Ok.PerformClick();
        Fuellen();
        if (d.ShowDialog(owner) != DialogResult.OK || liste.SelectedItems.Count == 0 || liste.SelectedItems[0].Tag is not Lehrer neu) return false;
        var z = Math.Max(0, zeile.SelectedIndex);
        var fuerAlle = alle.Checked;
        doc.Aendern(hinzufuegen ? "Lehrer hinzufügen" : "Lehrer ändern", AenderungsArt.Unterricht, x =>
        {
            var stunde = x.Stundenplan[stundeIndex];
            if (hinzufuegen) StundenLehrer.Hinzufuegen(x, stunde, neu.Id, fuerAlle);
            else StundenLehrer.Aendern(x, stunde, z, neu.Id, fuerAlle);
        });
        return true;
    }
}
