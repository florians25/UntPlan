namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Aufklappliste unter einer Tabellenzelle (wie die Auswahlbox der Vorbild-Spalten): Kurzname und Langname nebeneinander,
/// mit gedrückter Strg-Taste (bzw. Umschalt) mehrere Einträge wählen, Enter oder Doppelklick übernimmt, Esc bricht ab.
/// Bei Mehrfachauswahl oben „Alle“, „Keine“, „Umkehren“ und „Übernehmen“.
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
        var breite = Math.Min(420, Math.Max(Math.Max(g.Columns[spalte].Width, mehrfach ? 230 : 0), breiteKurz + breiteLang + 24));
        var hoehe = Math.Min(18 * 17 + 4, Math.Max(3, eintraege.Count) * 17 + 4);
        // Mehrfachauswahl: oben „Alle“, „Keine“, „Umkehren“ und „Übernehmen“ (schnell alle Klassen/Lehrkräfte/Räume wählen)
        Control inhalt = liste;
        if (mehrfach)
        {
            var leiste = new FlowLayoutPanel { Dock = DockStyle.Top, Height = LeistenHoehe, Padding = new Padding(2, 2, 0, 0), WrapContents = false, BackColor = Color.FromArgb(240, 244, 248) };
            LinkLabel Link(string text, Action aktion)
            {
                var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(4, 1, 6, 0), LinkBehavior = LinkBehavior.HoverUnderline };
                l.LinkClicked += (_, _) => { aktion(); liste.Focus(); };
                leiste.Controls.Add(l);
                return l;
            }
            Link("Alle", () => AlleSetzen(liste, true));
            Link("Keine", () => AlleSetzen(liste, false));
            Link("Umkehren", () =>
            {
                liste.BeginUpdate();
                for (var i = 0; i < liste.Items.Count; i++) liste.SetSelected(i, !liste.GetSelected(i));
                liste.EndUpdate();
            });
            Link("Übernehmen", () => _uebernehmen?.Invoke());
            liste.Dock = DockStyle.Fill;
            var panel = new Panel { Size = new Size(breite, hoehe + LeistenHoehe), Margin = Padding.Empty, Padding = Padding.Empty };
            panel.Controls.Add(liste);
            panel.Controls.Add(leiste);
            inhalt = panel;
            hoehe += LeistenHoehe;
        }
        var host = new ToolStripControlHost(inhalt) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        host.Size = new Size(breite, hoehe);
        var dd = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true };
        dd.Items.Add(host);
        var uebernommen = false;
        void Uebernehmen()
        {
            if (uebernommen) return;
            uebernommen = true;
            _uebernehmen = null;
            Offen = null;
            var res = liste.SelectedItems.Cast<Eintrag>().Select(e => e.Kurz).ToList();
            dd.Close();
            fertig(res);
        }
        _uebernehmen = Uebernehmen;
        Offen = liste;
        dd.Closed += (_, _) => { if (ReferenceEquals(Offen, liste)) { Offen = null; _uebernehmen = null; } };
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

    private const int LeistenHoehe = 22;
    private static Action? _uebernehmen;

    /// <summary>Geöffnete Auswahlliste (Selbsttest), null wenn keine offen ist.</summary>
    internal static ListBox? Offen { get; private set; }

    /// <summary>Selbsttest: in der offenen Liste alle bzw. keine Einträge wählen und übernehmen.</summary>
    internal static bool SelbsttestAlleUebernehmen(bool alle)
    {
        if (Offen is not { } l || _uebernehmen is not { } u) return false;
        AlleSetzen(l, alle);
        u();
        return true;
    }

    private static void AlleSetzen(ListBox liste, bool an)
    {
        liste.BeginUpdate();
        for (var i = 0; i < liste.Items.Count; i++) liste.SetSelected(i, an);
        liste.EndUpdate();
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
