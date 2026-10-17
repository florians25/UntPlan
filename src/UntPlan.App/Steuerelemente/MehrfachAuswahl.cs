namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Aufklappliste unter einer Tabellenzelle (wie die Auswahlbox der Vorbild-Spalten): Kurzname und Langname nebeneinander,
/// mit gedrückter Strg-Taste (bzw. Umschalt) mehrere Einträge wählen, Enter oder Doppelklick übernimmt, Esc bricht ab.
/// </summary>
public static class MehrfachAuswahl
{
    public sealed record Eintrag(string Kurz, string Lang);

    /// <summary>Liste unter der Zelle zeigen; <paramref name="fertig"/> erhält die gewählten Kurznamen (in Listenreihenfolge).</summary>
    public static void Zeigen(DataGridView g, int zeile, int spalte, IReadOnlyList<Eintrag> eintraege, IEnumerable<string> gewaehlt, bool mehrfach, Action<List<string>> fertig)
    {
        var liste = new ListBox
        {
            SelectionMode = mehrfach ? SelectionMode.MultiExtended : SelectionMode.One, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 17,
            BorderStyle = BorderStyle.None, Font = Ui.ListenSchrift, IntegralHeight = false,
        };
        foreach (var e in eintraege) liste.Items.Add(e);
        var vorher = gewaehlt.Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < eintraege.Count; i++)
            if (vorher.Contains(eintraege[i].Kurz)) liste.SetSelected(i, true);
        var breiteKurz = Math.Max(48, eintraege.Select(e => TextRenderer.MeasureText(e.Kurz, Ui.ListenSchrift).Width).DefaultIfEmpty(40).Max() + 10);
        var breiteLang = eintraege.Select(e => TextRenderer.MeasureText(e.Lang, Ui.ListenSchrift).Width).DefaultIfEmpty(60).Max() + 12;
        liste.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var x = (Eintrag)liste.Items[e.Index];
            var sel = (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Color.FromArgb(0, 90, 220) : e.Index % 2 == 0 ? Color.White : Color.FromArgb(246, 250, 246))) e.Graphics.FillRectangle(br, e.Bounds);
            var farbe = sel ? Color.White : Color.Black;
            Ui.ListenText(e.Graphics, x.Kurz, Ui.ListenSchrift, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, breiteKurz, e.Bounds.Height), farbe, false);
            Ui.ListenText(e.Graphics, x.Lang, Ui.ListenSchrift, new Rectangle(e.Bounds.X + 4 + breiteKurz, e.Bounds.Y, e.Bounds.Width - breiteKurz - 4, e.Bounds.Height), farbe);
        };
        var host = new ToolStripControlHost(liste) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        host.Size = new Size(Math.Min(420, Math.Max(g.Columns[spalte].Width, breiteKurz + breiteLang + 24)), Math.Min(18 * 17 + 4, Math.Max(3, eintraege.Count) * 17 + 4));
        var dd = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true };
        dd.Items.Add(host);
        var uebernommen = false;
        void Uebernehmen()
        {
            if (uebernommen) return;
            uebernommen = true;
            var res = liste.SelectedItems.Cast<Eintrag>().Select(e => e.Kurz).ToList();
            dd.Close();
            fertig(res);
        }
        liste.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; Uebernehmen(); }
            else if (e.KeyCode == Keys.Escape) { e.Handled = true; uebernommen = true; dd.Close(); }
        };
        liste.MouseDoubleClick += (_, _) => Uebernehmen();
        // Einfachauswahl: ein Klick genügt; Mehrfachauswahl: ohne Strg/Umschalt ebenfalls sofort übernehmen
        liste.MouseUp += (_, _) =>
        {
            if (!mehrfach || (Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0) Uebernehmen();
        };
        // Schließen durch Klick daneben: Auswahl der Mehrfachliste übernehmen (wie im Vorbild)
        dd.Closed += (_, e) => { if (mehrfach && e.CloseReason == ToolStripDropDownCloseReason.AppClicked) Uebernehmen(); };
        var r = g.GetCellDisplayRectangle(spalte, zeile, false);
        dd.Show(g, new Point(r.Left, r.Bottom));
        liste.Focus();
        if (liste.SelectedIndex >= 0) liste.TopIndex = Math.Max(0, liste.SelectedIndex - 3);
    }

    /// <summary>Kleines Aufklappdreieck rechts in einer Zelle zeichnen (aktuelle Zelle).</summary>
    public static void PfeilZeichnen(DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is null) return;
        e.Paint(e.CellBounds, DataGridViewPaintParts.All);
        var b = e.CellBounds;
        var r = new Rectangle(b.Right - 15, b.Top + 2, 13, b.Height - 5);
        ControlPaint.DrawBorder(e.Graphics, r, Color.FromArgb(170, 170, 170), ButtonBorderStyle.Solid);
        var m = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2 + 1);
        e.Graphics.FillPolygon(Brushes.Black, new[] { new Point(m.X - 3, m.Y - 2), new Point(m.X + 4, m.Y - 2), new Point(m.X, m.Y + 2) });
        e.Handled = true;
    }

    /// <summary>Liegt der Mauspunkt (Zellkoordinaten) auf dem Aufklappdreieck?</summary>
    public static bool AufPfeil(DataGridView g, DataGridViewCellMouseEventArgs e) => e.X >= g.Columns[e.ColumnIndex].Width - 16;
}
