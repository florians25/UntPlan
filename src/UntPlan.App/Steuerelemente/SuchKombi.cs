namespace UntPlan.App.Steuerelemente;

/// <summary>Darstellung eines Eintrags in einer Auswahlliste (Hintergrund z. B. rot = nicht verfügbar, Zusatztext = Langname).</summary>
public readonly record struct ListenStil(Color Hintergrund, Color Text, string Zusatz);

/// <summary>
/// Auswahllisten in Tabellen mit Direktsuche: In Auswahlspalten (DataGridViewComboBoxColumn) kann getippt werden, die Liste
/// springt zum passenden Eintrag und ergänzt den Text. Optional farbige Einträge mit Zusatztext (z. B. Lehrer rot, wenn belegt).
/// </summary>
public static class SuchKombi
{
    public static void Aktivieren(DataGridView g, Func<DataGridViewCell, string, ListenStil?>? stil = null)
    {
        g.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not ComboBox cb || g.CurrentCell is not DataGridViewComboBoxCell zelle) return;
            cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cb.AutoCompleteSource = AutoCompleteSource.ListItems;
            cb.MaxDropDownItems = 18;
            if (stil != null)
            {
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = 18;
                cb.DropDownWidth = Math.Max(cb.Width, 230);
                cb.DrawItem -= Zeichnen;
                cb.DrawItem += Zeichnen;
                cb.Tag = (Func<string, ListenStil?>)(text => stil(zelle, text));
            }
            else cb.DrawMode = DrawMode.Normal;
        };
        g.CellValidating += (_, e) =>
        {
            if (g.Columns[e.ColumnIndex] is not DataGridViewComboBoxColumn spalte || g.EditingControl is not ComboBox cb) return;
            var text = cb.Text.Trim();
            var items = (g.Rows[e.RowIndex].Cells[e.ColumnIndex] as DataGridViewComboBoxCell)?.Items ?? spalte.Items;
            var treffer = items.Cast<object>().Select(o => o.ToString() ?? "")
                .FirstOrDefault(x => x.Equals(text, StringComparison.OrdinalIgnoreCase))
                ?? items.Cast<object>().Select(o => o.ToString() ?? "").FirstOrDefault(x => text.Length > 0 && x.StartsWith(text, StringComparison.OrdinalIgnoreCase));
            if (treffer is null) treffer = items.Cast<object>().Select(o => o.ToString() ?? "").FirstOrDefault(x => x.Length == 0) ?? "";
            cb.Text = treffer;
            if (cb.Items.Contains(treffer)) cb.SelectedItem = treffer;
        };
        g.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Auswahl mit der Maus sofort übernehmen
            if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell && g.EditingControl is ComboBox { DroppedDown: false } cb && cb.SelectedIndex >= 0)
                g.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        g.DataError += (_, e) => e.ThrowException = false;
    }

    private static void Zeichnen(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb || e.Index < 0) return;
        var text = cb.Items[e.Index]?.ToString() ?? "";
        var s = (cb.Tag as Func<string, ListenStil?>)?.Invoke(text);
        var ausgewaehlt = (e.State & DrawItemState.Selected) != 0;
        var hinter = ausgewaehlt ? SystemColors.Highlight : s?.Hintergrund ?? cb.BackColor;
        var vorder = ausgewaehlt ? SystemColors.HighlightText : s?.Text ?? cb.ForeColor;
        using (var b = new SolidBrush(hinter)) e.Graphics.FillRectangle(b, e.Bounds);
        var r = e.Bounds;
        TextRenderer.DrawText(e.Graphics, text, cb.Font, new Rectangle(r.X + 3, r.Y, 60, r.Height), vorder, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(s?.Zusatz))
            TextRenderer.DrawText(e.Graphics, s.Value.Zusatz, cb.Font, new Rectangle(r.X + 66, r.Y, r.Width - 68, r.Height), vorder, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    /// <summary>Auswahlspalte mit Direktsuche (Werte: Kurznamen; leerer Eintrag zuerst).</summary>
    public static DataGridViewComboBoxColumn Spalte(string name, string kopf, int breite, IEnumerable<string> werte, bool leer = true)
    {
        var c = new DataGridViewComboBoxColumn
        {
            Name = name, HeaderText = kopf, Width = breite, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DisplayStyleForCurrentCellOnly = true, AutoComplete = true, SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        if (leer) c.Items.Add("");
        foreach (var w in werte.Distinct()) c.Items.Add(w);
        return c;
    }
}
