using UntPlan.Core.Model;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Aufklappliste unter einer Tabellenzelle (wie die Auswahlbox der Vorbild-Spalten): Kurzname und Langname nebeneinander,
/// mit gedrückter Strg-Taste (bzw. Umschalt) mehrere Einträge wählen, Enter oder Doppelklick übernimmt, Esc bricht ab.
/// Bei Mehrfachauswahl unten „Alle“, „Inverse“, „Markierte“, „Ok“ und „Abbrechen“ wie im Dialog „Auswahl“ des Vorbilds.
/// </summary>
public static class MehrfachAuswahl
{
    public sealed record Eintrag(string Kurz, string Lang, Image? Symbol = null);

    /// <summary>Symbol der Einträge einer Elementart wie im Vorbild (Lehrkraft mit Doktorhut, Klasse als Gruppe, Raum als Tür).</summary>
    public static Image? Symbol(ElementTyp typ) => typ switch
    {
        ElementTyp.Lehrer => Symbole.Get("liste_lehrer", 16),
        ElementTyp.Klasse => Symbole.Get("liste_klasse", 16),
        ElementTyp.Raum => Symbole.Get("liste_raum", 16),
        _ => null,
    };

    /// <summary>Schrift der Aufklappliste wie im Vorbild (Bilder 348, 351, 352: Segoe UI 8,25, Zeilen 16 Pixel).</summary>
    private static readonly Font ListenSchrift = new("Segoe UI", 8.25f);
    private const int ZeilenHoehe = 16;
    /// <summary>Vorbild: bis zu 21 Einträge sichtbar.</summary>
    private const int SichtbareZeilen = 21;

    /// <summary>Liste unter der Zelle zeigen; <paramref name="fertig"/> erhält die gewählten Kurznamen (in Listenreihenfolge).</summary>
    public static void Zeigen(DataGridView g, int zeile, int spalte, IReadOnlyList<Eintrag> eintraege, IEnumerable<string> gewaehlt, bool mehrfach, Action<List<string>> fertig)
    {
        var liste = new ListBox
        {
            SelectionMode = mehrfach ? SelectionMode.MultiExtended : SelectionMode.One, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = ZeilenHoehe,
            BorderStyle = BorderStyle.None, Font = ListenSchrift, IntegralHeight = false, BackColor = Color.White,
        };
        foreach (var e in eintraege) liste.Items.Add(e);
        var vorher = gewaehlt.Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < eintraege.Count; i++)
            if (vorher.Contains(eintraege[i].Kurz)) liste.SetSelected(i, true);
        // Vorbild: Symbol 6 Pixel vom Rand, Kurzname 24 Pixel nach dem Symbolanfang, Langname 48 Pixel nach dem Kurznamen
        var mitSymbol = eintraege.Any(e => e.Symbol != null);
        var einzug = mitSymbol ? 30 : 4;
        var breiteKurz = Math.Max(48, eintraege.Select(e => TextRenderer.MeasureText(e.Kurz, ListenSchrift).Width).DefaultIfEmpty(40).Max() + 8);
        var breiteLang = eintraege.Select(e => TextRenderer.MeasureText(e.Lang, ListenSchrift).Width).DefaultIfEmpty(60).Max() + 12;
        // Listenbreite wie im Vorbild (Bilder 348, 351, 352): Klassen 141 Pixel, Lehrkräfte 187 Pixel samt Rollbalken; längere Langnamen
        // werden wie dort ohne Auslassungspunkte abgeschnitten (vollständig im Tooltipp)
        var erstes = eintraege.FirstOrDefault()?.Symbol;
        int? vorbildBreite = erstes is null ? null
            : ReferenceEquals(erstes, Symbol(ElementTyp.Klasse)) ? 141 : ReferenceEquals(erstes, Symbol(ElementTyp.Lehrer)) ? 187 : null;
        liste.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var x = (Eintrag)liste.Items[e.Index];
            var sel = (e.State & DrawItemState.Selected) != 0;
            // weiße Liste wie im Vorbild; gewählte Einträge in der hellen Auswahlfarbe, der aktuelle Eintrag mit dünnem Rahmen
            using (var br = new SolidBrush(sel ? Ui.AuswahlVertretung : Color.White)) e.Graphics.FillRectangle(br, e.Bounds);
            if (x.Symbol is { } sym) e.Graphics.DrawImage(sym, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + (e.Bounds.Height - 16) / 2, 16, 16));
            var farbe = Color.Black;
            Ui.ListenText(e.Graphics, x.Kurz, ListenSchrift, new Rectangle(e.Bounds.X + einzug, e.Bounds.Y, breiteKurz, e.Bounds.Height), farbe, false);
            Ui.ListenText(e.Graphics, x.Lang, ListenSchrift, new Rectangle(e.Bounds.X + einzug + breiteKurz, e.Bounds.Y, e.Bounds.Width - breiteKurz - einzug, e.Bounds.Height), farbe, vorbildBreite is null);
            if ((e.State & DrawItemState.Focus) != 0)
                using (var p = new Pen(Color.FromArgb(120, 120, 120))) e.Graphics.DrawRectangle(p, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        };
        var breite = vorbildBreite ?? Math.Min(420, Math.Max(g.Columns[spalte].Width, einzug + breiteKurz + breiteLang + SystemInformation.VerticalScrollBarWidth + 2));
        // abgeschnittene Langnamen vollständig im Tooltipp
        var tipp = new ToolTip { InitialDelay = 400, ReshowDelay = 100 };
        var tippZeile = -1;
        liste.MouseMove += (_, e) =>
        {
            var i = liste.IndexFromPoint(e.Location);
            if (i == tippZeile) return;
            tippZeile = i;
            var text = i >= 0 && i < liste.Items.Count && liste.Items[i] is Eintrag en && TextRenderer.MeasureText(en.Lang, ListenSchrift).Width > liste.ClientSize.Width - einzug - breiteKurz
                ? $"{en.Kurz}  {en.Lang}" : "";
            tipp.SetToolTip(liste, text);
        };
        liste.Disposed += (_, _) => tipp.Dispose();
        var hoehe = Math.Min(SichtbareZeilen * ZeilenHoehe + 2, Math.Max(3, eintraege.Count) * ZeilenHoehe + 2);
        // Mehrfachauswahl wie der Dialog „Auswahl“ im Vorbild (Bild 349): unten „Alle“, „Inverse“, „Markierte“, darunter „Ok“ und
        // „Abbrechen“ mittig; „Keine“ im Kontextmenü der Liste
        Control inhalt = liste;
        var alle = eintraege.ToList();
        var nurMarkierte = false;
        Action? leisteZeigen = null;
        if (mehrfach)
        {
            // die Liste öffnet wie im Vorbild ohne Knöpfe; sobald mehrere Einträge gewählt sind (Strg/Umschalt, Kontextmenü oder
            // bereits beim Öffnen), erscheint darunter die Knopfleiste
            var leiste = new Panel { Dock = DockStyle.Bottom, Height = LeistenHoehe, BackColor = Ui.Kopf, Visible = false };
            Button Knopf(string text, Action aktion)
            {
                var b = new Button { Text = text, Height = 22, Font = ListenSchrift, UseVisualStyleBackColor = true, TabStop = false };
                b.Click += (_, _) => { aktion(); if (!liste.IsDisposed) liste.Focus(); };
                leiste.Controls.Add(b);
                return b;
            }
            void Umkehren()
            {
                liste.BeginUpdate();
                for (var i = 0; i < liste.Items.Count; i++) liste.SetSelected(i, !liste.GetSelected(i));
                liste.EndUpdate();
            }
            var kAlle = Knopf("Alle", () => AlleSetzen(liste, true));
            var kInverse = Knopf("Inverse", Umkehren);
            var kMarkierte = Knopf("Markierte", () =>
            {
                // nur die gewählten Einträge zeigen (erneut: wieder alle)
                var gewaehlteJetzt = liste.SelectedItems.Cast<Eintrag>().ToHashSet();
                nurMarkierte = !nurMarkierte && gewaehlteJetzt.Count > 0;
                liste.BeginUpdate();
                liste.Items.Clear();
                foreach (var en in alle.Where(en => !nurMarkierte || gewaehlteJetzt.Contains(en))) liste.Items.Add(en);
                for (var i = 0; i < liste.Items.Count; i++) if (gewaehlteJetzt.Contains((Eintrag)liste.Items[i])) liste.SetSelected(i, true);
                liste.EndUpdate();
            });
            var kOk = Knopf("Ok", () => _uebernehmen?.Invoke());
            var kAbbrechen = Knopf("Abbrechen", () => { _abbrechen?.Invoke(); });
            leiste.Resize += (_, _) =>
            {
                var w = (leiste.Width - 4 * 6) / 3;
                kAlle.SetBounds(6, 6, w, 22);
                kInverse.SetBounds(12 + w, 6, w, 22);
                kMarkierte.SetBounds(leiste.Width - 6 - w, 6, w, 22);
                var m = leiste.Width / 2;
                kOk.SetBounds(m - 4 - 73, 34, 73, 22);
                kAbbrechen.SetBounds(m + 4, 34, 75, 22);
            };
            var kontext = new ContextMenuStrip();
            kontext.Items.Add("Alle", null, (_, _) => { AlleSetzen(liste, true); leisteZeigen?.Invoke(); });
            kontext.Items.Add("Keine", null, (_, _) => { AlleSetzen(liste, false); leisteZeigen?.Invoke(); });
            kontext.Items.Add("Inverse", null, (_, _) => { Umkehren(); leisteZeigen?.Invoke(); });
            liste.ContextMenuStrip = kontext;
            liste.Dock = DockStyle.Fill;
            var panel = new Panel { Size = new Size(breite, hoehe), Margin = Padding.Empty, Padding = Padding.Empty };
            panel.Controls.Add(liste);
            panel.Controls.Add(leiste);
            inhalt = panel;
            var listenHoehe = hoehe;
            leisteZeigen = () =>
            {
                if (leiste.Visible || _offenesFenster is not { IsDisposed: false } offen || offen.Items.Count == 0) return;
                var w = Math.Max(breite, 3 * 64 + 4 * 6);
                leiste.Visible = true;
                panel.Size = new Size(w, listenHoehe + LeistenHoehe);
                offen.Items[0].Size = panel.Size;
                offen.PerformLayout();
            };
        }
        var host = new ToolStripControlHost(inhalt) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        host.Size = new Size(breite, hoehe);
        var dd = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true };
        dd.Items.Add(host);
        _offenesFenster = dd;
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
        _leisteZeigen = leisteZeigen;
        _abbrechen = () => { uebernommen = true; dd.Close(); };
        Offen = liste;
        dd.Closed += (_, _) => { if (ReferenceEquals(Offen, liste)) { Offen = null; _uebernehmen = null; } };
        liste.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; Uebernehmen(); }
            else if (e.KeyCode == Keys.Escape) { e.Handled = true; uebernommen = true; dd.Close(); }
        };
        liste.MouseDoubleClick += (_, _) => Uebernehmen();
        // Einfachauswahl: ein Klick genügt; Mehrfachauswahl: ohne Strg/Umschalt ebenfalls sofort übernehmen
        liste.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (!mehrfach || (Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0) Uebernehmen();
            else leisteZeigen?.Invoke();
        };
        // Schließen durch Klick daneben: Auswahl der Mehrfachliste übernehmen (wie im Vorbild)
        dd.Closed += (_, e) => { if (mehrfach && e.CloseReason == ToolStripDropDownCloseReason.AppClicked) Uebernehmen(); };
        var r = g.GetCellDisplayRectangle(spalte, zeile, false);
        dd.Show(g, new Point(r.Left, r.Bottom));
        liste.Focus();
        if (liste.SelectedIndices.Count > 1) leisteZeigen?.Invoke();
        if (liste.SelectedIndex >= 0) liste.TopIndex = Math.Max(0, liste.SelectedIndex - 3);
    }

    private const int LeistenHoehe = 62;
    private static Action? _abbrechen;
    private static Action? _leisteZeigen;

    /// <summary>
    /// Selbsttest: zwei Einträge wählen wie mit Strg+Klick; liefert die Beschriftungen der dann sichtbaren Knopfleiste
    /// (leer, solange sie verborgen ist) und die Breite der Liste vorher/nachher.
    /// </summary>
    internal static (List<string> Knoepfe, int BreiteVorher, int BreiteNachher) SelbsttestMehrfach()
    {
        if (Offen is not { } l || l.Items.Count < 2) return (new(), 0, 0);
        var vorher = l.Parent?.Width ?? l.Width;
        l.SetSelected(0, true);
        l.SetSelected(1, true);
        _leisteZeigen?.Invoke();
        var leiste = l.Parent?.Controls.OfType<Panel>().FirstOrDefault(p => p.Visible);
        return (leiste?.Controls.OfType<Button>().Select(b => b.Text).ToList() ?? new(), vorher, l.Parent?.Width ?? l.Width);
    }
    private static ToolStripDropDown? _offenesFenster;

    /// <summary>Selbsttest: geöffnete Liste ohne Übernahme schließen.</summary>
    internal static void SelbsttestSchliessen()
    {
        _uebernehmen = null;
        Offen = null;
        if (_offenesFenster is { IsDisposed: false } d) d.Close(ToolStripDropDownCloseReason.CloseCalled);
        _offenesFenster = null;
    }
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

    /// <summary>
    /// Aufklappknopf rechts in der aktuellen Zelle wie im Vorbild (Bild 350 vermessen): 17 Pixel breit, 1 Pixel unter und über den
    /// Rasterlinien, fast weiß mit hellgrauem Rand (unten etwas dunkler), darin ein dünner grauer Winkel 8 × 4 Pixel.
    /// </summary>
    public static void PfeilZeichnen(DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is null) return;
        e.Paint(e.CellBounds, DataGridViewPaintParts.All);
        var b = e.CellBounds;
        var r = new Rectangle(b.Right - 19, b.Top + 1, 17, b.Height - 3);
        var g = e.Graphics;
        using (var f = new SolidBrush(Color.FromArgb(253, 253, 253))) g.FillRectangle(f, r);
        using (var rand = new Pen(Color.FromArgb(210, 210, 210)))
        {
            g.DrawLine(rand, r.Left + 1, r.Top, r.Right - 2, r.Top);
            g.DrawLine(rand, r.Left, r.Top + 1, r.Left, r.Bottom - 2);
            g.DrawLine(rand, r.Right - 1, r.Top + 1, r.Right - 1, r.Bottom - 2);
        }
        using (var unten = new Pen(Color.FromArgb(188, 188, 188))) g.DrawLine(unten, r.Left + 1, r.Bottom - 1, r.Right - 2, r.Bottom - 1);
        using var dunkel = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var weich = new SolidBrush(Color.FromArgb(204, 204, 204));
        var x0 = r.Left + 4;
        var y0 = r.Top + (r.Height - 4) / 2;
        for (var k = 0; k < 4; k++)
        {
            g.FillRectangle(dunkel, x0 + k, y0 + k, 1, 1);
            g.FillRectangle(dunkel, x0 + 7 - k, y0 + k, 1, 1);
            if (k < 3) { g.FillRectangle(weich, x0 + k + 1, y0 + k, 1, 1); g.FillRectangle(weich, x0 + 6 - k, y0 + k, 1, 1); }
        }
        e.Handled = true;
    }

    /// <summary>Liegt der Mauspunkt (Zellkoordinaten) auf dem Aufklappdreieck?</summary>
    public static bool AufPfeil(DataGridView g, DataGridViewCellMouseEventArgs e) => e.X >= g.Columns[e.ColumnIndex].Width - 19;
}
