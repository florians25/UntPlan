namespace UntPlan.App.Steuerelemente;

/// <summary>Darstellung eines Eintrags in einer Auswahlliste (Hintergrund z. B. rot = nicht verfügbar, Zusatztext = Langname).</summary>
public readonly record struct ListenStil(Color Hintergrund, Color Text, string Zusatz);

/// <summary>
/// Auswahllisten in Tabellen mit Direktsuche (siehe <see cref="ListenSuche"/>): In Auswahlspalten kann getippt werden, die Liste
/// öffnet sich und markiert den passenden Eintrag. Optional farbige Einträge mit Zusatztext (z. B. Lehrer rot, wenn belegt).
/// </summary>
public static class SuchKombi
{
    private static readonly HashSet<DataGridView> Aktiv = new();

    public static bool IstAktiv(DataGridView g) => Aktiv.Contains(g);

    public static void Aktivieren(DataGridView g, Func<DataGridViewCell, string, ListenStil?>? stil = null)
    {
        if (!Aktiv.Add(g)) return;
        g.Disposed += (_, _) => Aktiv.Remove(g);
        g.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not ComboBox cb || g.CurrentCell is not DataGridViewComboBoxCell zelle) return;
            cb.MaxDropDownItems = 18;
            ListenSuche.Einrichten(cb, stil is null ? null : text => stil(zelle, text));
            // die Zelle setzt die Listenbreite bei jeder Bearbeitung auf die Spaltenbreite – gleich wieder verbreitern
            ListenSuche.BreiteAktualisieren(cb);
            g.BeginInvoke(() => { if (!cb.IsDisposed) ListenSuche.BreiteAktualisieren(cb); });
        };
        g.CellValidating += (_, e) =>
        {
            // eine vorher registrierte Prüfung hat die Eingabe abgelehnt (Hinweis statt stiller Ersetzung durch „leer“)
            if (e.Cancel) return;
            if (g.Columns[e.ColumnIndex] is not DataGridViewComboBoxColumn || g.EditingControl is not ComboBox cb || cb.Items.Count == 0) return;
            var text = cb.Text.Trim();
            var namen = cb.Items.Cast<object>().Select(o => cb.GetItemText(o) ?? "").ToList();
            var i = text.Length == 0 ? namen.FindIndex(x => x.Length == 0) : ListenSuche.Treffer(cb, text);
            if (i < 0) i = namen.FindIndex(x => x.Length == 0);
            if (i < 0) i = cb.SelectedIndex;
            if (i < 0) return;
            if (cb.SelectedIndex != i) cb.SelectedIndex = i;
            cb.Text = namen[i];
            if (g.CurrentCell is { } c && c.RowIndex == e.RowIndex && c.ColumnIndex == e.ColumnIndex) g.NotifyCurrentCellDirty(true);
        };
        g.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Auswahl mit der Maus sofort übernehmen
            if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell && g.EditingControl is ComboBox { DroppedDown: false } cb && cb.SelectedIndex >= 0 &&
                string.Equals(cb.GetItemText(cb.SelectedItem), cb.Text, StringComparison.Ordinal))
                g.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        g.DataError += (_, e) => e.ThrowException = false;
    }

    /// <summary>Auswahlspalte mit Direktsuche (Werte: Kurznamen; leerer Eintrag zuerst).</summary>
    public static DataGridViewComboBoxColumn Spalte(string name, string kopf, int breite, IEnumerable<string> werte, bool leer = true)
    {
        var c = new DataGridViewComboBoxColumn
        {
            Name = name, HeaderText = kopf, Width = breite, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DisplayStyleForCurrentCellOnly = true, AutoComplete = false, SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        if (leer) c.Items.Add("");
        foreach (var w in werte.Distinct()) c.Items.Add(w);
        return c;
    }
}
