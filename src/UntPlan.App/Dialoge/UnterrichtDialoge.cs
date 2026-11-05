using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Dialog „Koppeln“ (Handbuch 3.3.4, Koppeln per Schaltfläche): der aktivierte Unterricht ist eingetragen; weitere Unterrichte per
/// Doppelklick auf den Unterricht im Unterrichtsfenster, über &lt;Dazu&gt; (aktivierter Unterricht) oder durch Eingabe der
/// Unterrichtsnummer und &lt;Dazu&gt;. Der Dialog ist nicht modal, damit im Unterrichtsfenster gewählt werden kann.
/// </summary>
public static class KoppelnDialog
{
    public static Form Zeigen(Form owner, int start, Func<int?> aktiverUnterricht, Action<List<int>> koppeln)
    {
        var d = new Dialog("Koppeln", 300, 250) { ShowInTaskbar = false };
        var liste = new ListBox { Left = 12, Top = 12, Width = 170, Height = 160, Name = "Unterrichte" };
        var nummer = new TextBox { Left = 12, Top = 180, Width = 80, Name = "Nummer" };
        var dazu = new Button { Text = "Dazu", Left = 98, Top = 178, Width = 84, Height = 24 };
        var entfernen = new Button { Text = "Entfernen", Left = 192, Top = 12, Width = 96, Height = 24 };
        d.Controls.AddRange(new Control[] { liste, nummer, dazu, entfernen });
        d.Ok.Text = "Koppeln";
        d.Ok.DialogResult = DialogResult.None;
        d.Abbrechen.DialogResult = DialogResult.None;
        string Text(int id)
        {
            var u = AppZustand.D.Unterrichte.FirstOrDefault(x => x.Id == id);
            return u is null ? id.ToString() : $"{id}, {AppZustand.Doc.Index.UnterrichtText(u)}";
        }
        var ids = new List<int>();
        void Hinzu(int id)
        {
            if (ids.Contains(id) || !AppZustand.D.Unterrichte.Any(u => u.Id == id)) return;
            ids.Add(id);
            liste.Items.Add(Text(id));
        }
        Hinzu(start);
        dazu.Click += (_, _) =>
        {
            if (int.TryParse(nummer.Text.Trim(), out var nr))
            {
                if (!AppZustand.D.Unterrichte.Any(u => u.Id == nr)) { Ui.Info($"Unterricht \"{nr}\" ist nicht vorhanden.", owner: d); return; }
                Hinzu(nr);
                nummer.Clear();
            }
            else if (aktiverUnterricht() is int a) Hinzu(a);
        };
        entfernen.Click += (_, _) =>
        {
            if (liste.SelectedIndex <= 0) return; // der aktivierte (erste) Unterricht bleibt
            ids.RemoveAt(liste.SelectedIndex);
            liste.Items.RemoveAt(liste.SelectedIndex);
        };
        void Doppelklick(int id) { if (!d.IsDisposed) Hinzu(id); }
        UnterrichtFenster.UnterrichtsnummerGewaehlt += Doppelklick;
        d.FormClosed += (_, _) => UnterrichtFenster.UnterrichtsnummerGewaehlt -= Doppelklick;
        d.Ok.Click += (_, _) =>
        {
            if (ids.Count < 2) { Ui.Info("Bitte mindestens einen weiteren Unterricht dazunehmen.", owner: d); return; }
            var auswahl = ids.ToList();
            d.Close();
            koppeln(auswahl);
        };
        d.Abbrechen.Click += (_, _) => d.Close();
        d.AcceptButton = dazu;
        d.StartPosition = FormStartPosition.Manual;
        d.Location = new Point(owner.PointToScreen(Point.Empty).X + Math.Max(0, owner.Width - 320), owner.PointToScreen(Point.Empty).Y + 40);
        d.Show(owner);
        return d;
    }
}

/// <summary>
/// Unterrichtsvergleich (Handbuch 3.5): Unterrichte in mehreren Perioden vergleichen. Oben die Auswahl der Perioden und
/// „Nur Unterschiede“, darunter je Unterricht eine Zeile mit dem Stand in jeder Periode (abweichende Zellen markiert).
/// </summary>
public static class UnterrichtsvergleichDialog
{
    public static void Zeigen(IWin32Window owner, ISet<int> unterrichte)
    {
        var d = AppZustand.D;
        using var f = new Dialog("Unterrichtsvergleich", 760, 420) { FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = true };
        f.Ok.Text = "Schließen";
        f.Abbrechen.Visible = false;
        f.Ok.Left = f.Abbrechen.Left;
        var perioden = new CheckedListBox { Left = 12, Top = 12, Width = 220, Height = 90, CheckOnClick = true, Name = "Perioden" };
        foreach (var p in d.Perioden.OrderBy(p => p.Von)) perioden.Items.Add(new PeriodenEintrag(p), true);
        var nurUnterschiede = new CheckBox { Text = "Nur Unterschiede", Left = 244, Top = 12, AutoSize = true, Checked = true };
        var nurAnsicht = new CheckBox { Text = "Nur Unterrichte der Ansicht", Left = 244, Top = 36, AutoSize = true, Checked = unterrichte.Count > 0 };
        var g = new DataGridView
        {
            Left = 12, Top = 110, Width = 736, Height = 264, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Name = "Vergleich",
        };
        Ui.RasterGestalten(g, false);
        g.ReadOnly = true;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        void Fuellen()
        {
            var gewaehlt = perioden.CheckedItems.Cast<PeriodenEintrag>().Select(x => x.P).ToList();
            g.Rows.Clear();
            g.Columns.Clear();
            g.Columns.Add("UNr", "U-Nr");
            g.Columns[0].Width = 50;
            foreach (var p in gewaehlt) g.Columns[g.Columns.Add("P" + p.Id, p.Name)].Width = Math.Max(160, 680 / Math.Max(1, gewaehlt.Count));
            if (gewaehlt.Count == 0) return;
            foreach (var z in Unterrichtswerkzeuge.Vergleichen(d, gewaehlt, nurUnterschiede.Checked))
            {
                if (nurAnsicht.Checked && !unterrichte.Contains(z.UnterrichtId)) continue;
                var i = g.Rows.Add(new object[] { z.UnterrichtId }.Concat(z.Texte.Select(t => (object)(t ?? "– nicht vorhanden –"))).ToArray());
                if (!z.Unterschiedlich) continue;
                var erster = z.Texte[0];
                for (var k = 0; k < z.Texte.Count; k++)
                    if (z.Texte[k] != erster) g.Rows[i].Cells[k + 1].Style.BackColor = Color.FromArgb(255, 236, 200);
            }
        }
        perioden.ItemCheck += (_, _) => f.BeginInvoke(Fuellen);
        nurUnterschiede.CheckedChanged += (_, _) => Fuellen();
        nurAnsicht.CheckedChanged += (_, _) => Fuellen();
        f.Controls.AddRange(new Control[] { perioden, nurUnterschiede, nurAnsicht, g });
        Fuellen();
        f.ShowDialog(owner);
    }

    private sealed record PeriodenEintrag(Periode P)
    {
        public override string ToString() => $"{P.Name} (ab {Format.Datum(P.Von)})";
    }
}
