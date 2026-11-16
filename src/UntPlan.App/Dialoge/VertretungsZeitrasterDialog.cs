using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vertretungszeitraster wie in der Vorlage: Fenster „Zeitraster“ mit Registerkarte „Vertretung“, Rahmen „Eintragung:“ mit den
/// Schaltflächen &lt;Vertretung&gt; (grün) und &lt;Entfall&gt;, darunter das Raster Stunden × Tage (Spaltenköpfe Stunde, Beginn, Ende).
/// Stunden mit der Maus überstreichen und eine Schaltfläche klicken: „*“ = wird vertreten, leer = Unterricht entfällt bei Absenz.
/// Als Klassenzeitraster („Klassenzeitraster / Klasse“, Klassenwahl mit Auf-/Ab-Pfeilen, Reiter „Verplanung“ und „Vertretung“)
/// zusätzlich &lt;Eigenv. Arbeiten&gt; („#“) und &lt;Kopieren&gt; (auf andere Klassen).
/// </summary>
public static class VertretungsZeitrasterDialog
{
    /// <summary>Zeitraster der Schule (klasse = null) bzw. Klassenzeitraster einer Klasse bearbeiten.</summary>
    public static bool Zeigen(IWin32Window owner, Klasse? klasse = null)
    {
        var d = AppZustand.D;
        var stunden = Math.Max(1, d.Zeitraster.StundenProTag);
        var istKlasse = klasse != null;

        // Größe wie in der Vorlage: großzügiger Rahmen um Eintragung und Raster
        var breite = Math.Max(istKlasse ? 500 : 620, 110 + stunden * 46);
        using var f = new Dialog(istKlasse ? "Klassenzeitraster / Klasse" : "Zeitraster", breite, istKlasse ? 540 : 480);
        f.Ok.Text = "Ok";
        var oben = 8;
        // Klassenwahl wie in der Vorlage: Kurzname mit Auf-/Ab-Pfeilen (Eintippen des Kurznamens springt zur Klasse)
        DomainUpDown? wahl = null;
        var klassenListe = Stammdaten.Sortiert(d.Klassen).ToList();
        var tipp = new ToolTip();
        f.FormClosed += (_, _) => tipp.Dispose();
        if (istKlasse)
        {
            wahl = new DomainUpDown { Left = 24, Top = 12, Width = 84, Wrap = false, ReadOnly = false };
            foreach (var k in klassenListe) wahl.Items.Add(k.Kurzname);
            f.Controls.Add(wahl);
            oben = 44;
        }
        var tabs = new TabControl { Left = 8, Top = oben, Width = breite - 16, Height = f.ClientSize.Height - oben - 48, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        f.Controls.Add(tabs);
        var vertretung = new VertretungsZeitrasterSeite(d, f, istKlasse, klasse);
        tabs.TabPages.Add(vertretung.Seite);
        // Klassenzeitraster: Registerkarte „Verplanung“ (Stundenplan) vor „Vertretung“; im Stundenplanmodus aktiv
        var verplanung = istKlasse ? new KlassenzeitrasterVerplanung(d, f) : null;
        if (verplanung != null)
        {
            tabs.TabPages.Insert(0, verplanung.Seite);
            tabs.SelectedIndex = AppZustand.Vertretungsmodus ? 1 : 0;
        }

        if (wahl != null)
        {
            void Gewaehlt(Klasse k)
            {
                vertretung.Klasse(k);
                verplanung?.Klasse(k);
                tipp.SetToolTip(wahl, k.Langname.Length > 0 ? $"{k.Kurzname} ({k.Langname})" : k.Kurzname);
            }
            wahl.SelectedItemChanged += (_, _) =>
            {
                if (wahl.SelectedIndex >= 0 && wahl.SelectedIndex < klassenListe.Count) Gewaehlt(klassenListe[wahl.SelectedIndex]);
            };
            // eingetippten Kurznamen übernehmen (unbekannt: bisherige Klasse bleibt)
            wahl.Leave += (_, _) =>
            {
                var i = klassenListe.FindIndex(k => k.Kurzname.Equals(wahl.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i >= 0) wahl.SelectedIndex = i;
                else if (vertretung.Aktuell != null) wahl.Text = vertretung.Aktuell.Kurzname;
            };
            var start = Math.Max(0, klassenListe.FindIndex(k => k.Id == klasse!.Id));
            if (start < klassenListe.Count)
            {
                if (wahl.SelectedIndex == start) Gewaehlt(klassenListe[start]);
                else wahl.SelectedIndex = start;
            }
        }

        void Speichern()
        {
            AppZustand.Doc.Aendern(istKlasse ? "Klassenzeitraster" : "Vertretungszeitraster", AenderungsArt.Vertretung, x =>
            {
                vertretung.Uebernehmen(x);
                verplanung?.Uebernehmen(x);
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

/// <summary>
/// Registerkarte „Vertretung“ des Zeitrasters (Schule) bzw. des Klassenzeitrasters – auch im Dialog „Zeitraster“
/// (Start | Einstellungen | Zeitraster) als letzte Registerkarte wie in der Vorlage.
/// </summary>
public sealed class VertretungsZeitrasterSeite
{
    private static readonly Color Gruen = Color.FromArgb(128, 255, 128);
    private static readonly Color GruenHell = Color.FromArgb(178, 255, 178);
    private static readonly Color Violett = Color.FromArgb(170, 170, 255);
    private static readonly Color ViolettHell = Color.FromArgb(214, 214, 255);

    private readonly Dictionary<int, VertretungsRegel> _schule;
    private readonly Dictionary<int, Dictionary<int, VertretungsRegel>> _klassen;
    private readonly DataGridView _raster = new();
    private readonly int _stunden;

    public TabPage Seite { get; } = new("Vertretung") { BackColor = Ui.Kopf };
    public Klasse? Aktuell { get; private set; }
    /// <summary>Es wurde etwas eingetragen oder kopiert.</summary>
    public bool Geaendert { get; private set; }

    public VertretungsZeitrasterSeite(Schuldatei d, IWin32Window owner, bool istKlasse, Klasse? klasse = null)
    {
        var tage = Math.Max(1, d.Zeitraster.TageProWoche);
        _stunden = Math.Max(1, d.Zeitraster.StundenProTag);
        _schule = new Dictionary<int, VertretungsRegel>(d.Vertretungseinstellungen.Zeitraster);
        _klassen = d.Klassen.ToDictionary(k => k.Id, k => new Dictionary<int, VertretungsRegel>(k.VertretungsZeitraster));
        Aktuell = klasse;

        // Anordnung wie in der Vorlage: Rahmen „Eintragung:“ mit breiten Schaltflächen, rechts daneben <Kopieren>
        var eintragung = new GroupBox { Text = "Eintragung:", Left = 12, Top = 10, Width = 214, Height = istKlasse ? 124 : 92 };
        Button Knopf(string text, Color farbe, int y) => new() { Text = text, Left = 14, Top = y, Width = 184, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = farbe, UseVisualStyleBackColor = false };
        var kVertretung = Knopf("Vertretung", GruenHell, 22);
        var kEntfall = Knopf("Entfall", Color.White, 54);
        var kEigen = Knopf("Eigenv. Arbeiten", ViolettHell, 88);
        eintragung.Controls.Add(kVertretung);
        eintragung.Controls.Add(kEntfall);
        if (istKlasse) eintragung.Controls.Add(kEigen);
        Seite.Controls.Add(eintragung);
        var kopieren = new Button { Text = "Kopieren", Left = 290, Top = 32, Width = 130, Height = 26, Visible = istKlasse };
        Seite.Controls.Add(kopieren);

        var raster = _raster;
        Ui.RasterGestalten(raster, false);
        raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        raster.MultiSelect = true;
        raster.RowHeadersWidth = 80;
        raster.ColumnHeadersHeight = 22;
        raster.ScrollBars = ScrollBars.Both;
        for (var s = 1; s <= _stunden; s++)
        {
            var c = raster.Columns[raster.Columns.Add("S" + s, s.ToString())];
            c.Width = 42;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        // zwei Zeilen mit Beginn und Ende der Stunde (wie die Kopfzeilen in der Vorlage), dann je Tag eine Zeile
        string Zeit(int s, bool ende) => d.Zeitraster.Zeit(s) is { } z ? (ende ? z.Ende : z.Beginn).ToString("H:mm") : "";
        raster.Rows.Add(Enumerable.Range(1, _stunden).Select(s => (object)Zeit(s, false)).ToArray());
        raster.Rows.Add(Enumerable.Range(1, _stunden).Select(s => (object)Zeit(s, true)).ToArray());
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
        var oben = eintragung.Bottom + 8;
        void Anordnen() => raster.SetBounds(6, oben, Math.Max(100, Seite.ClientSize.Width - 12),
            Math.Max(60, Math.Min(Seite.ClientSize.Height - oben - 6, 22 + raster.Rows.Count * 20 + 20)));
        Seite.Resize += (_, _) => Anordnen();
        Anordnen();

        kVertretung.Click += (_, _) => Setzen(VertretungsRegel.Vertreten);
        kEntfall.Click += (_, _) => Setzen(VertretungsRegel.Entfall);
        kEigen.Click += (_, _) => Setzen(VertretungsRegel.Eigenverantwortlich);
        kopieren.Click += (_, _) =>
        {
            if (Aktuell is not { } akt) return;
            var ziel = AuswahlDialog.Mehrere(owner, ElementTyp.Klasse, $"Klassenzeitraster von {akt.Kurzname} kopieren nach", Array.Empty<int>());
            if (ziel is null || !_klassen.TryGetValue(akt.Id, out var quelle)) return;
            foreach (var k in ziel.Where(k => k.Id != akt.Id && _klassen.ContainsKey(k.Id)))
                _klassen[k.Id] = new Dictionary<int, VertretungsRegel>(quelle);
            Geaendert = true;
        };
        raster.SelectionChanged += (_, _) =>
        {
            // die beiden Zeitzeilen sind nur Beschriftung
            foreach (DataGridViewCell c in raster.SelectedCells) if (c.RowIndex < 2) c.Selected = false;
        };
        Seite.Controls.Add(raster);
        Fuellen();
        raster.ClearSelection();
    }

    /// <summary>Angezeigte Klasse wechseln (Klassenzeitraster).</summary>
    public void Klasse(Klasse? k)
    {
        Aktuell = k;
        Fuellen();
    }

    private VertretungsRegel Regel(int tag, int stunde)
    {
        var key = tag * 100 + stunde;
        if (Aktuell != null && _klassen.TryGetValue(Aktuell.Id, out var kl) && kl.TryGetValue(key, out var kr)) return kr;
        return _schule.TryGetValue(key, out var sr) ? sr : VertretungsRegel.Vertreten;
    }

    private void Fuellen()
    {
        foreach (DataGridViewRow r in _raster.Rows)
        {
            if (r.Tag is not int t) continue;
            for (var s = 1; s <= _stunden; s++)
            {
                var regel = Regel(t, s);
                var zelle = r.Cells[s - 1];
                zelle.Value = regel switch { VertretungsRegel.Vertreten => "*", VertretungsRegel.Eigenverantwortlich => "#", _ => "" };
                zelle.Style.BackColor = regel switch { VertretungsRegel.Vertreten => Gruen, VertretungsRegel.Eigenverantwortlich => Violett, _ => Color.White };
                zelle.Style.SelectionBackColor = ControlPaint.Dark(zelle.Style.BackColor, 0.05f);
            }
        }
    }

    private void Setzen(VertretungsRegel regel)
    {
        foreach (DataGridViewCell c in _raster.SelectedCells)
        {
            if (c.OwningRow.Tag is not int t) continue;
            var key = t * 100 + c.ColumnIndex + 1;
            if (Aktuell != null) { if (_klassen.TryGetValue(Aktuell.Id, out var kl)) kl[key] = regel; }
            else if (regel == VertretungsRegel.Vertreten) _schule.Remove(key);
            else _schule[key] = regel;
        }
        Geaendert = true;
        Fuellen();
    }

    /// <summary>Eingaben in die Schuldatei übernehmen (innerhalb einer Änderung).</summary>
    public void Uebernehmen(Schuldatei x)
    {
        // Klassenzeitraster: nur Abweichungen vom Zeitraster der Schule speichern (Änderungen der Schule wirken weiter)
        var bereinigt = _klassen.ToDictionary(e => e.Key, e => e.Value.Where(r => r.Value != (_schule.TryGetValue(r.Key, out var sr) ? sr : VertretungsRegel.Vertreten))
            .ToDictionary(r => r.Key, r => r.Value));
        x.Vertretungseinstellungen.Zeitraster = new Dictionary<int, VertretungsRegel>(_schule);
        foreach (var k in x.Klassen) if (bereinigt.TryGetValue(k.Id, out var r)) k.VertretungsZeitraster = r;
        foreach (var t in x.Vertretungen.Select(v => v.Datum).Where(t => t >= DateOnly.FromDateTime(DateTime.Today)).Distinct().ToList()) Core.Vertretungsplanung.Vertretungen.Aktualisieren(x, t);
    }
}
