using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vertretungszeitraster wie im Handbuch (Kap. 4.2): Fenster „Zeitraster“ mit Registerkarte „Vertretung“, Rahmen „Eintragung:“ mit den
/// Schaltflächen &lt;Vertretung&gt; (grün) und &lt;Entfall&gt;, darunter das Raster Stunden × Tage (Spaltenköpfe Stunde, Beginn, Ende).
/// Stunden mit der Maus überstreichen und eine Schaltfläche klicken: „*“ = wird vertreten, leer = Unterricht entfällt bei Absenz.
/// Als Klassenzeitraster („Klassenzeitraster / Klasse“) zusätzlich &lt;Eigenv. Arbeiten&gt; („#“) und &lt;Kopieren&gt; (auf andere Klassen).
/// </summary>
public static class VertretungsZeitrasterDialog
{
    private static readonly Color Gruen = Color.FromArgb(128, 255, 128);
    private static readonly Color GruenHell = Color.FromArgb(178, 255, 178);
    private static readonly Color Violett = Color.FromArgb(170, 170, 255);
    private static readonly Color ViolettHell = Color.FromArgb(214, 214, 255);

    /// <summary>Zeitraster der Schule (klasse = null) bzw. Klassenzeitraster einer Klasse bearbeiten.</summary>
    public static bool Zeigen(IWin32Window owner, Klasse? klasse = null)
    {
        var d = AppZustand.D;
        var tage = Math.Max(1, d.Zeitraster.TageProWoche);
        var stunden = Math.Max(1, d.Zeitraster.StundenProTag);
        var schule = new Dictionary<int, VertretungsRegel>(d.Vertretungseinstellungen.Zeitraster);
        var klassen = d.Klassen.ToDictionary(k => k.Id, k => new Dictionary<int, VertretungsRegel>(k.VertretungsZeitraster));
        var istKlasse = klasse != null;
        var aktuell = klasse;

        var breite = Math.Max(400, 96 + stunden * 46);
        using var f = new Dialog(istKlasse ? "Klassenzeitraster / Klasse" : "Zeitraster", breite, istKlasse ? 400 : 350);
        f.Ok.Text = "Ok";
        var oben = 8;
        ComboBox? wahl = null;
        Label? langname = null;
        if (istKlasse)
        {
            wahl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 10, Width = 80 };
            foreach (var k in Stammdaten.Sortiert(d.Klassen)) wahl.Items.Add(k);
            wahl.Format += (_, e) => e.Value = e.ListItem is Klasse k ? k.Kurzname : e.ListItem?.ToString();
            langname = new Label { Left = 100, Top = 13, AutoSize = true };
            f.Controls.Add(wahl);
            f.Controls.Add(langname);
            oben = 40;
        }
        var tabs = new TabControl { Left = 8, Top = oben, Width = breite - 16, Height = f.ClientSize.Height - oben - 48, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        var seite = new TabPage("Vertretung") { BackColor = Ui.Kopf };
        tabs.TabPages.Add(seite);
        // Klassenzeitraster: Registerkarte „Verplanung“ (Stundenplan) vor „Vertretung“; im Stundenplanmodus aktiv
        var verplanung = istKlasse ? new KlassenzeitrasterVerplanung(d, f) : null;
        if (verplanung != null)
        {
            tabs.TabPages.Insert(0, verplanung.Seite);
            tabs.SelectedIndex = AppZustand.Vertretungsmodus ? 1 : 0;
        }
        f.Controls.Add(tabs);

        var eintragung = new GroupBox { Text = "Eintragung:", Left = 10, Top = 6, Width = 170, Height = istKlasse ? 112 : 84 };
        Button Knopf(string text, Color farbe, int y) => new() { Text = text, Left = 10, Top = y, Width = 150, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = farbe, UseVisualStyleBackColor = false };
        var kVertretung = Knopf("Vertretung", GruenHell, 22);
        var kEntfall = Knopf("Entfall", Color.White, 50);
        var kEigen = Knopf("Eigenv. Arbeiten", ViolettHell, 78);
        eintragung.Controls.Add(kVertretung);
        eintragung.Controls.Add(kEntfall);
        if (istKlasse) eintragung.Controls.Add(kEigen);
        seite.Controls.Add(eintragung);
        var kopieren = new Button { Text = "Kopieren", Left = 200, Top = 26, Width = 110, Height = 24, Visible = istKlasse };
        seite.Controls.Add(kopieren);

        var raster = new DataGridView
        {
            Left = 6, Top = eintragung.Bottom + 8, Width = seite.ClientSize.Width - 12, Height = seite.ClientSize.Height - eintragung.Bottom - 14,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Ui.RasterGestalten(raster, false);
        raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        raster.MultiSelect = true;
        raster.RowHeadersWidth = 80;
        raster.ColumnHeadersHeight = 22;
        raster.ScrollBars = ScrollBars.Both;
        for (var s = 1; s <= stunden; s++)
        {
            var c = raster.Columns[raster.Columns.Add("S" + s, s.ToString())];
            c.Width = 42;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        // zwei Zeilen mit Beginn und Ende der Stunde (wie die Kopfzeilen im Vorbild), dann je Tag eine Zeile
        string Zeit(int s, bool ende) => d.Zeitraster.Zeit(s) is { } z ? (ende ? z.Ende : z.Beginn).ToString("H:mm") : "";
        raster.Rows.Add(Enumerable.Range(1, stunden).Select(s => (object)Zeit(s, false)).ToArray());
        raster.Rows.Add(Enumerable.Range(1, stunden).Select(s => (object)Zeit(s, true)).ToArray());
        for (var i = 0; i < 2; i++)
        {
            raster.Rows[i].DefaultCellStyle.BackColor = raster.Rows[i].DefaultCellStyle.SelectionBackColor = Color.White;
            raster.Rows[i].DefaultCellStyle.SelectionForeColor = Color.Black;
        }
        for (var t = 0; t < tage; t++)
        {
            var r = raster.Rows.Add();
            raster.Rows[r].HeaderCell.Value = Planungsdienst.Tagname(d, t, true);
            raster.Rows[r].Tag = t;
        }
        raster.Height = Math.Min(raster.Height, 22 + raster.Rows.Count * 20 + 20);

        VertretungsRegel Regel(int tag, int stunde)
        {
            var key = tag * 100 + stunde;
            if (aktuell != null && klassen[aktuell.Id].TryGetValue(key, out var kr)) return kr;
            return schule.TryGetValue(key, out var sr) ? sr : VertretungsRegel.Vertreten;
        }
        void Fuellen()
        {
            foreach (DataGridViewRow r in raster.Rows)
            {
                if (r.Tag is not int t) continue;
                for (var s = 1; s <= stunden; s++)
                {
                    var regel = Regel(t, s);
                    var zelle = r.Cells[s - 1];
                    zelle.Value = regel switch { VertretungsRegel.Vertreten => "*", VertretungsRegel.Eigenverantwortlich => "#", _ => "" };
                    zelle.Style.BackColor = regel switch { VertretungsRegel.Vertreten => Gruen, VertretungsRegel.Eigenverantwortlich => Violett, _ => Color.White };
                    zelle.Style.SelectionBackColor = ControlPaint.Dark(zelle.Style.BackColor, 0.05f);
                }
            }
            if (aktuell != null && langname != null) langname.Text = aktuell.Langname.Length > 0 ? $"{aktuell.Kurzname} ({aktuell.Langname})" : aktuell.Kurzname;
        }
        void Setzen(VertretungsRegel regel)
        {
            foreach (DataGridViewCell c in raster.SelectedCells)
            {
                if (c.OwningRow.Tag is not int t) continue;
                var key = t * 100 + c.ColumnIndex + 1;
                if (aktuell != null) klassen[aktuell.Id][key] = regel;
                else if (regel == VertretungsRegel.Vertreten) schule.Remove(key);
                else schule[key] = regel;
            }
            Fuellen();
        }
        kVertretung.Click += (_, _) => Setzen(VertretungsRegel.Vertreten);
        kEntfall.Click += (_, _) => Setzen(VertretungsRegel.Entfall);
        kEigen.Click += (_, _) => Setzen(VertretungsRegel.Eigenverantwortlich);
        kopieren.Click += (_, _) =>
        {
            if (aktuell is null) return;
            var ziel = AuswahlDialog.Mehrere(f, ElementTyp.Klasse, $"Klassenzeitraster von {aktuell.Kurzname} kopieren nach", Array.Empty<int>());
            if (ziel is null) return;
            foreach (var k in ziel.Where(k => k.Id != aktuell.Id && klassen.ContainsKey(k.Id)))
                klassen[k.Id] = new Dictionary<int, VertretungsRegel>(klassen[aktuell.Id]);
        };
        if (wahl != null)
        {
            wahl.SelectedIndexChanged += (_, _) => { aktuell = wahl.SelectedItem as Klasse; Fuellen(); verplanung?.Klasse(aktuell); };
            wahl.SelectedItem = wahl.Items.Cast<Klasse>().FirstOrDefault(k => k.Id == klasse!.Id);
        }
        raster.SelectionChanged += (_, _) =>
        {
            // die beiden Zeitzeilen sind nur Beschriftung
            foreach (DataGridViewCell c in raster.SelectedCells) if (c.RowIndex < 2) c.Selected = false;
        };
        seite.Controls.Add(raster);
        Fuellen();
        raster.ClearSelection();

        void Speichern()
        {
            // Klassenzeitraster: nur Abweichungen vom Zeitraster der Schule speichern (Änderungen der Schule wirken weiter)
            var bereinigt = klassen.ToDictionary(x => x.Key, x => x.Value.Where(e => e.Value != (schule.TryGetValue(e.Key, out var sr) ? sr : VertretungsRegel.Vertreten))
                .ToDictionary(e => e.Key, e => e.Value));
            var neuSchule = new Dictionary<int, VertretungsRegel>(schule);
            AppZustand.Doc.Aendern(istKlasse ? "Klassenzeitraster" : "Vertretungszeitraster", AenderungsArt.Vertretung, x =>
            {
                x.Vertretungseinstellungen.Zeitraster = neuSchule;
                foreach (var k in x.Klassen) if (bereinigt.TryGetValue(k.Id, out var r)) k.VertretungsZeitraster = r;
                verplanung?.Uebernehmen(x);
                foreach (var t in x.Vertretungen.Select(v => v.Datum).Where(t => t >= DateOnly.FromDateTime(DateTime.Today)).Distinct().ToList()) Core.Vertretungsplanung.Vertretungen.Aktualisieren(x, t);
            });
        }
        var uebernehmen = new Button { Text = "Übernehmen", Width = 88, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        uebernehmen.Location = new Point(f.ClientSize.Width - 96, f.ClientSize.Height - 36);
        f.Ok.Left -= 96;
        f.Abbrechen.Left -= 96;
        uebernehmen.Click += (_, _) => Speichern();
        f.Controls.Add(uebernehmen);
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        Speichern();
        return true;
    }
}
