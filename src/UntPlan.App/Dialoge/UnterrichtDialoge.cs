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
    /// <summary>
    /// Aufbau wie im Handbuch (Kap. 3.3.4, Abb. „Koppeln“): links Eingabefeld „U-Nr“ und die Schaltflächen Dazu, Wegnehmen, Abbrechen,
    /// Ok untereinander, rechts die Tabelle der zu koppelnden Unterrichte (U-Nr, Wst, Lehrer, Klasse, Fach).
    /// </summary>
    public static Form Zeigen(Form owner, int start, Func<int?> aktiverUnterricht, Action<List<int>> koppeln)
    {
        var d = new Dialog("Koppeln", 540, 210) { ShowInTaskbar = false };
        var nummer = new TextBox { Left = 12, Top = 12, Width = 60, Name = "Nummer" };
        var nrLabel = new Label { Text = "U-Nr", Left = 78, Top = 15, AutoSize = true };
        Button Knopf(string text, int top) => new() { Text = text, Left = 12, Top = top, Width = 104, Height = 26, Name = text };
        var dazu = Knopf("Dazu", 44);
        var weg = Knopf("Wegnehmen", 76);
        d.Abbrechen.SetBounds(12, 108, 104, 26);
        d.Ok.SetBounds(12, 140, 104, 26);
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        d.Ok.Text = "Ok";
        d.Ok.DialogResult = DialogResult.None;
        d.Abbrechen.DialogResult = DialogResult.None;
        var g = new DataGridView { Left = 128, Top = 12, Width = 400, Height = 186, Name = "Unterrichte" };
        Ui.RasterGestalten(g, false);
        g.ReadOnly = true;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (name, kopf, breite) in new[] { ("UNr", "U-Nr", 50), ("Wst", "Wst", 40), ("Lehrer", "Lehrer", 100), ("Klasse", "Klasse", 110), ("Fach", "Fach", 90) })
            g.Columns[g.Columns.Add(name, kopf)].Width = breite;
        d.Controls.AddRange(new Control[] { nummer, nrLabel, dazu, weg, g });
        var ids = new List<int>();
        void Hinzu(int id)
        {
            if (ids.Contains(id) || AppZustand.D.Unterrichte.FirstOrDefault(u => u.Id == id) is not { } u) return;
            ids.Add(id);
            var ix = AppZustand.Doc.Index;
            g.Rows.Add(id, u.Wochenstunden, string.Join(",", u.LehrerIds.Select(ix.KLehrer)), ix.KKlassen(u.KlassenIds),
                string.Join(",", u.Zeilen.Select(z => ix.KFach(z.FachId)).Where(f => f.Length > 0).Distinct()));
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
        weg.Click += (_, _) =>
        {
            var i = g.CurrentRow?.Index ?? -1;
            if (i <= 0) return; // der aktivierte (erste) Unterricht bleibt
            ids.RemoveAt(i);
            g.Rows.RemoveAt(i);
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
        d.Location = new Point(owner.PointToScreen(Point.Empty).X + Math.Max(0, owner.Width - 560), owner.PointToScreen(Point.Empty).Y + 40);
        d.Show(owner);
        return d;
    }
}

/// <summary>
/// Dialog „Entkoppeln“ wie im Handbuch (Kap. 3.3.5, Erweitertes Entkoppeln): links „Alter Unterricht U-Nr. …, Wst …“ mit seinen
/// Kopplungszeilen, in der Mitte der Pfeil, rechts „Neue Unterrichte“; markierte Zeilen wandern mit dem Pfeil nach rechts
/// (Doppelpfeil zurück). „Alle Entkoppeln“ löst zusätzlich alle Klassenkopplungen auf.
/// </summary>
public sealed class EntkoppelnDialog : Dialog
{
    private readonly DataGridView _alt = Raster("Alt");
    private readonly DataGridView _neu = Raster("Neu");
    private readonly Unterricht _u;

    /// <summary>Indizes der Kopplungszeilen, die selbständige Unterrichte werden.</summary>
    public List<int> Zeilen => _neu.Rows.Cast<DataGridViewRow>().Select(r => (int)r.Tag!).OrderBy(i => i).ToList();

    /// <summary>„Alle Entkoppeln“ gewählt.</summary>
    public bool Alle { get; private set; }

    public EntkoppelnDialog(Unterricht u) : base("Entkoppeln", 700, 250)
    {
        _u = u;
        var ix = AppZustand.Doc.Index;
        Controls.Add(new Label { Text = $"Alter Unterricht  U-Nr. {u.Id}, Wst {u.Wochenstunden}", Left = 12, Top = 10, AutoSize = true });
        Controls.Add(new Label { Text = "Neue Unterrichte", Left = 384, Top = 10, AutoSize = true });
        _alt.SetBounds(12, 30, 300, 168);
        _neu.SetBounds(384, 30, 304, 168);
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            var r = _alt.Rows.Add(z.LehrerId.HasValue ? ix.KLehrer(z.LehrerId) : "?", ix.KFach(z.FachId), ix.KKlassen(z.KlassenIds));
            _alt.Rows[r].Tag = i;
        }
        var hin = new Button { Image = Symbole.Get("vor", 16), Left = 324, Top = 84, Width = 48, Height = 30, Name = "Hin" };
        var zurueck = new Button { Image = Symbole.Get("zurueck", 16), Left = 324, Top = 120, Width = 48, Height = 30, Name = "Zurueck" };
        new ToolTip().SetToolTip(hin, "Markierte Kopplungszeilen werden neue Unterrichte");
        new ToolTip().SetToolTip(zurueck, "Markierte Zeilen bleiben beim alten Unterricht");
        hin.Click += (_, _) => Verschieben(_alt, _neu);
        zurueck.Click += (_, _) => Verschieben(_neu, _alt);
        _alt.CellDoubleClick += (_, _) => Verschieben(_alt, _neu);
        _neu.CellDoubleClick += (_, _) => Verschieben(_neu, _alt);
        var alle = new Button { Text = "Alle Entkoppeln", Left = 12, Top = 214, Width = 120, Height = 26, Name = "AlleEntkoppeln", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        alle.Click += (_, _) => { Alle = true; DialogResult = DialogResult.OK; };
        Ok.Text = "Ok";
        Controls.AddRange(new Control[] { _alt, _neu, hin, zurueck, alle });
    }

    /// <summary>Markierte Zeilen (bzw. aktuelle Zeile) in die andere Tabelle verschieben.</summary>
    internal void Verschieben(bool hin) => Verschieben(hin ? _alt : _neu, hin ? _neu : _alt);

    /// <summary>Zeilen der linken Tabelle markieren (Selbsttest, Tastatur).</summary>
    internal void Markieren(params int[] zeilen)
    {
        _alt.ClearSelection();
        foreach (DataGridViewRow r in _alt.Rows) r.Selected = zeilen.Contains((int)r.Tag!);
    }

    private static void Verschieben(DataGridView von, DataGridView nach)
    {
        var zeilen = von.SelectedRows.Cast<DataGridViewRow>().ToList();
        if (zeilen.Count == 0 && von.CurrentRow is { } c) zeilen.Add(c);
        foreach (var r in zeilen.OrderBy(r => (int)r.Tag!))
        {
            var i = nach.Rows.Add(r.Cells[0].Value, r.Cells[1].Value, r.Cells[2].Value);
            nach.Rows[i].Tag = r.Tag;
            von.Rows.Remove(r);
        }
        nach.Sort(new ZeilenFolge());
        nach.ClearSelection();
    }

    private sealed class ZeilenFolge : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => ((int)((DataGridViewRow)x!).Tag!).CompareTo((int)((DataGridViewRow)y!).Tag!);
    }

    private static DataGridView Raster(string name)
    {
        var g = new DataGridView { Name = name };
        Ui.RasterGestalten(g, false);
        g.ReadOnly = true;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = true;
        foreach (var (n, kopf, breite) in new[] { ("Lehrer", "Lehrer", 80), ("Fach", "Fach", 70), ("Klasse", "Klasse", 140) })
            g.Columns[g.Columns.Add(n, kopf)].Width = breite;
        return g;
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
