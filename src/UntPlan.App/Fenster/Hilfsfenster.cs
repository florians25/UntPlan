using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Eingabeassistent: Die Reihenfolge der Punkte entspricht den Schritten bei der Erstellung des Stundenplans.
/// Alle Fenster sind auch über das Menüband erreichbar.
/// </summary>
public sealed class Eingabeassistent : Panel
{
    private readonly Label _titel;
    private readonly FlowLayoutPanel _flow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6) };

    public Eingabeassistent()
    {
        BackColor = Color.FromArgb(236, 241, 249);
        _titel = new Label { Text = "UntPlan Eingabeassistent", Dock = DockStyle.Top, Height = 26, Font = Ui.SchriftFett, ForeColor = Color.White, BackColor = Ui.Titel, Padding = new Padding(6, 5, 0, 0) };
        var schliessen = new Label { Text = "✕", Dock = DockStyle.Right, Width = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Cursor = Cursors.Hand };
        new ToolTip().SetToolTip(schliessen, "Eingabeassistent ausblenden (wieder einblenden: Start → Einstellungen → Eingabeassistent)");
        schliessen.Click += (_, _) => AppZustand.Haupt?.AssistentUmschalten();
        schliessen.MouseEnter += (_, _) => schliessen.BackColor = Color.FromArgb(200, 60, 60);
        schliessen.MouseLeave += (_, _) => schliessen.BackColor = Color.Transparent;
        _titel.Controls.Add(schliessen);
        Controls.Add(_flow);
        Controls.Add(_titel);
        Aktualisieren();
    }

    public void Aktualisieren()
    {
        _titel.BackColor = Ui.Titel;
        if (AppZustand.Haupt is null) return;
        var d = AppZustand.D;
        _flow.SuspendLayout();
        _flow.Controls.Clear();
        void Kopf(string t) => _flow.Controls.Add(new Label { Text = t, AutoSize = false, Width = 200, Height = 22, Font = Ui.SchriftFett, ForeColor = Ui.Blau, Margin = new Padding(0, 8, 0, 0) });
        void Schritt(string t, bool erledigt, Action a)
        {
            var l = new LinkLabel { Text = (erledigt ? "● " : "○ ") + t, AutoSize = false, Width = 205, Height = 20, LinkColor = Color.FromArgb(30, 60, 140), ActiveLinkColor = Color.OrangeRed, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(6, 0, 0, 0) };
            l.LinkClicked += (_, _) => a();
            _flow.Controls.Add(l);
        }
        var h = AppZustand.Haupt;
        if (AppZustand.Vertretungsmodus)
        {
            var datum = AppZustand.Datum;
            Kopf("Vertretungsplanung – " + Format.Datum(datum));
            Schritt("Planungsdatum wählen (Kalender)", true, () => h.VertretungsFensterOeffnen("kal"));
            Schritt("Absenzen eingeben (Strg+M)", d.Abwesenheiten.Any(a => datum >= a.Von && datum <= a.Bis), () => h.VertretungsFensterOeffnen("abw"));
            Schritt("Veranstaltungen", d.Veranstaltungen.Any(v => datum >= v.Von && datum <= v.Bis), () => h.VertretungsFensterOeffnen("veranst"));
            Schritt("Klausuren", d.Klausuren.Any(k => k.Datum == datum), () => h.VertretungsFensterOeffnen("klaus"));
            Kopf("Vertretungen bearbeiten");
            Schritt("Vertretungen (Strg+T)", !d.Vertretungen.Any(v => v.Datum == datum && v.Offen), () => h.VertretungsFensterOeffnen("vertr"));
            Schritt("Vertretungsvorschlag (Strg+G)", false, () => h.VertretungsFensterOeffnen("vorschlag"));
            Schritt("Vormerkungen", d.Vormerkungen.All(v => v.Offen == 0), () => h.VertretungsFensterOeffnen("vorm"));
            Schritt("Bereitschaften", false, () => h.VertretungsFensterOeffnen("bereit"));
            Schritt("Pausenaufsichten", false, () => h.VertretungsFensterOeffnen("pause"));
            Kopf("Ausgabe");
            Schritt("Nachrichten zum Tag", d.Tagestexte.Any(t => t.GiltAm(datum)), () => Dialoge.TagestextDialog.Zeigen(h, datum));
            Schritt("Vertretungsplan (HTML)", false, () => VertretungenFenster.HtmlAusgabe(datum, 2, false));
            Schritt("Vertretungsstatistik / Abrechnung", false, () => h.VertretungsFensterOeffnen("stat"));
            Kopf("Einstellungen");
            Schritt("Einstellungen Vertretungsplanung", true, () => Dialoge.VertretungsEinstellungenDialog.Zeigen(h));
            Schritt("Zurück zur Stundenplanung", false, h.VertretungsmodusUmschalten);
            _flow.ResumeLayout();
            return;
        }
        Kopf("Festlegen des Schuljahres und der Unterrichtszeiten");
        Schritt("Eingabe des Schuljahres", d.Einstellungen.Schulname.Length > 0, () => new EinstellungenDialog().ShowDialog(h));
        Schritt("Eingabe von Ferien und Feiertagen", d.Ferien.Count > 0, () => HauptFenster.Oeffnen("ferien", () => new FerienFenster()));
        Schritt("Eingabe des Zeitrasters", d.Zeitraster.Zeiten.Count > 0, () => new ZeitrasterDialog().ShowDialog(h));
        Kopf("Eingabe der Räume, Klassen, Lehrer und Fächer");
        Schritt("Eingabe der Räume", d.Raeume.Count > 0, () => HauptFenster.Oeffnen("st:Raum", () => new StammdatenFenster(ElementTyp.Raum)));
        Schritt("Eingabe der Klassen", d.Klassen.Count > 0, () => HauptFenster.Oeffnen("st:Klasse", () => new StammdatenFenster(ElementTyp.Klasse)));
        Schritt("Eingabe der Lehrer", d.Lehrer.Count > 0, () => HauptFenster.Oeffnen("st:Lehrer", () => new StammdatenFenster(ElementTyp.Lehrer)));
        Schritt("Eingabe der Fächer", d.Faecher.Count > 0, () => HauptFenster.Oeffnen("st:Fach", () => new StammdatenFenster(ElementTyp.Fach)));
        Kopf("Verfügbarkeiten");
        Schritt("Eingabe der Kernzeiten (Klassen)", d.Klassen.Any(k => k.Zeitwuensche.Werte.Values.Any(v => v >= 3)), () => ZeitwuenscheFenster.Zeigen(ElementTyp.Klasse, null));
        Schritt("Lehrer-Verfügbarkeiten", d.Lehrer.Any(l => !l.Zeitwuensche.Leer || l.UnbestimmteZeitwuensche.Count > 0), () => ZeitwuenscheFenster.Zeigen(ElementTyp.Lehrer, null));
        Schritt("Raum-Verfügbarkeiten", d.Raeume.Any(r => !r.Zeitwuensche.Leer), () => ZeitwuenscheFenster.Zeigen(ElementTyp.Raum, null));
        Kopf("Eingabe des Unterrichts");
        Schritt("Eingabe des Unterrichts", d.Unterrichte.Count > 0, () => HauptFenster.Oeffnen("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)));
        Kopf("Planung");
        Schritt("Stunden mit der Hand verplanen", d.Stundenplan.Any(p => p.Fixiert), () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false));
        Schritt("Gewichtung", true, () => new GewichtungDialog().ShowDialog(h));
        Schritt("Datenüberprüfung (Diagnose)", false, () => HauptFenster.Oeffnen("diag", () => new DiagnoseFenster()));
        Schritt("Automatische Verplanung", d.Stundenplan.Count > 0, () => OptimierungDialog.Starten(h));
        Kopf("Stundenpläne");
        Schritt("Stundenpläne", d.Stundenplan.Count > 0, () => h.StandardFenster());
        Kopf("Weitere Informationen");
        Schritt("Hilfe", false, () => HilfeFenster.Zeigen("Eingabeassistent"));
        _flow.ResumeLayout();
    }
}

/// <summary>Begrüßungsbildschirm beim ersten Start.</summary>
/// <summary>
/// Startbildschirm: zuletzt geöffnete Schuldateien, neue Datei, Öffnen, Beispielschule.
/// Wird angezeigt, wenn beim Start keine Datei geladen wird (oder über Datei → Startbildschirm).
/// </summary>
public static class BegruessungDialog
{
    private static readonly Color Dunkel = Color.FromArgb(29, 79, 115);

    public static void Zeigen(IWin32Window owner)
    {
        using var f = new Form
        {
            Text = "UntPlan", Font = Ui.Schrift, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(780, 450), ShowInTaskbar = false, BackColor = Color.White,
        };
        f.Icon = Icon.FromHandle(Symbole.Get("logo", 32).GetHicon());

        // linke Spalte: Logo, Name, Version
        var links = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Dunkel };
        links.Controls.Add(new PictureBox { Image = Symbole.Get("logo", 64), SizeMode = PictureBoxSizeMode.Zoom, Left = 28, Top = 34, Width = 56, Height = 56 });
        links.Controls.Add(new Label { Text = "UntPlan", ForeColor = Color.White, Font = new Font(Ui.Schrift.FontFamily, 20f, FontStyle.Bold), Left = 26, Top = 100, AutoSize = true });
        links.Controls.Add(new Label { Text = "Stunden- und Vertretungsplanung", ForeColor = Color.FromArgb(200, 218, 232), Left = 28, Top = 142, Width = 210 });
        var version = typeof(BegruessungDialog).Assembly.GetName().Version;
        links.Controls.Add(new Label { Text = $"Version {version?.ToString(3)}", ForeColor = Color.FromArgb(160, 188, 210), Left = 28, Top = 410, Width = 200 });

        var rechts = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 26, 28, 18) };
        f.Controls.Add(rechts);
        f.Controls.Add(links);

        rechts.Controls.Add(new Label { Text = "Zuletzt geöffnet", Font = Ui.SchriftFett, Left = 28, Top = 26, AutoSize = true });
        var liste = new ListView
        {
            Left = 28, Top = 52, Width = 474, Height = 236, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None,
            BorderStyle = BorderStyle.FixedSingle, MultiSelect = false, Font = Ui.Schrift,
        };
        liste.Columns.Add("Datei", 300);
        liste.Columns.Add("Geändert", 150);
        foreach (var pfad in AppZustand.Einstellungen.ZuletztGeoeffnet.Where(File.Exists).Take(9))
        {
            var it = new ListViewItem(new[] { Path.GetFileNameWithoutExtension(pfad), File.GetLastWriteTime(pfad).ToString("dd.MM.yyyy HH:mm") }) { Tag = pfad, ToolTipText = pfad };
            liste.Items.Add(it);
        }
        liste.ShowItemToolTips = true;
        if (liste.Items.Count == 0) liste.Items.Add(new ListViewItem("Noch keine Dateien geöffnet") { ForeColor = Color.Gray });
        else liste.Items[0].Selected = true;
        rechts.Controls.Add(liste);
        var pfadLabel = new Label { Left = 28, Top = 292, Width = 474, ForeColor = Color.Gray, AutoEllipsis = true };
        rechts.Controls.Add(pfadLabel);
        liste.SelectedIndexChanged += (_, _) => pfadLabel.Text = liste.SelectedItems.Count > 0 ? liste.SelectedItems[0].Tag as string ?? "" : "";
        pfadLabel.Text = liste.SelectedItems.Count > 0 ? liste.SelectedItems[0].Tag as string ?? "" : "";

        Button Knopf(string text, int left, bool haupt = false)
        {
            var b = new Button { Text = text, Left = left, Top = 322, Width = 150, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = haupt ? Dunkel : Color.White, ForeColor = haupt ? Color.White : Color.Black };
            b.FlatAppearance.BorderColor = haupt ? Dunkel : Color.FromArgb(190, 198, 206);
            rechts.Controls.Add(b);
            return b;
        }
        var offen = Knopf("Öffnen", 28, true);
        var durchsuchen = Knopf("Andere Datei…", 190);
        var neu = Knopf("Neue Schuldatei…", 352);
        var demo = new LinkLabel { Text = "Beispielschule öffnen", Left = 28, Top = 362, AutoSize = true, LinkColor = Dunkel };
        rechts.Controls.Add(demo);

        var automatisch = new CheckBox { Text = "Zuletzt geöffnete Datei beim Start automatisch laden", Left = 28, Top = 392, Width = 474, Checked = AppZustand.Einstellungen.LetzteDateiLaden };
        var anzeigen = new CheckBox { Text = "Startbildschirm anzeigen, wenn keine Datei geladen wird", Left = 28, Top = 414, Width = 474, Checked = AppZustand.Einstellungen.BegruessungZeigen };
        rechts.Controls.Add(automatisch);
        rechts.Controls.Add(anzeigen);

        void Laden()
        {
            if (liste.SelectedItems.Count == 0 || liste.SelectedItems[0].Tag is not string pfad) return;
            f.Close();
            AppZustand.Haupt.DateiLaden(pfad);
        }
        offen.Enabled = liste.Items.Count > 0 && liste.Items[0].Tag is string;
        offen.Click += (_, _) => Laden();
        liste.DoubleClick += (_, _) => Laden();
        liste.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Laden(); };
        durchsuchen.Click += (_, _) => { f.Close(); AppZustand.Haupt.Oeffnen(); };
        neu.Click += (_, _) => { f.Close(); AppZustand.Haupt.Neu(); };
        demo.LinkClicked += (_, _) => { f.Close(); AppZustand.Haupt.BeispielOeffnen(); };
        f.FormClosed += (_, _) =>
        {
            AppZustand.Einstellungen.BegruessungZeigen = anzeigen.Checked;
            AppZustand.Einstellungen.LetzteDateiLaden = automatisch.Checked;
            AppZustand.Einstellungen.Speichern();
        };
        f.AcceptButton = offen;
        f.ShowDialog(owner);
    }
}

/// <summary>Kontextbezogene Hilfe (F1) mit allen Kapiteln des Handbuchs.</summary>
public sealed class HilfeFenster : Form
{
    private static HilfeFenster? _instanz;
    private readonly TreeView _baum = new() { Dock = DockStyle.Left, Width = 240, HideSelection = false };
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f) };

    public static void Zeigen(string thema)
    {
        if (_instanz is null || _instanz.IsDisposed) { _instanz = new HilfeFenster(); _instanz.Show(AppZustand.Haupt); }
        _instanz.Activate();
        _instanz.Waehlen(thema);
    }

    private HilfeFenster()
    {
        Text = "UntPlan – Hilfe";
        Font = Ui.Schrift;
        Size = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Icon.FromHandle(Symbole.Get("hilfe", 16).GetHicon());
        foreach (var (kapitel, themen) in Handbuch.Kapitel)
        {
            var k = _baum.Nodes.Add(kapitel);
            foreach (var t in themen) k.Nodes.Add(t.Titel).Tag = t;
        }
        _baum.ExpandAll();
        _baum.AfterSelect += (_, e) => Anzeigen(e.Node?.Tag as Handbuch.Thema);
        Controls.Add(_text);
        Controls.Add(new Splitter { Dock = DockStyle.Left });
        Controls.Add(_baum);
    }

    private void Waehlen(string thema)
    {
        foreach (TreeNode k in _baum.Nodes)
            foreach (TreeNode t in k.Nodes)
                if (thema.Length == 0 && t.Index == 0 && k.Index == 0 || t.Text.Equals(thema, StringComparison.OrdinalIgnoreCase) || (t.Tag is Handbuch.Thema th && th.Stichworte.Contains(thema)))
                {
                    _baum.SelectedNode = t;
                    return;
                }
    }

    private void Anzeigen(Handbuch.Thema? t)
    {
        _text.Clear();
        if (t is null) return;
        _text.SelectionFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        _text.SelectionColor = Ui.Blau;
        _text.AppendText(t.Titel + "\n\n");
        foreach (var absatz in t.Text.Split("\n"))
        {
            if (absatz.StartsWith("Tipp:") || absatz.StartsWith("Achtung:"))
            {
                _text.SelectionBackColor = absatz.StartsWith("Tipp:") ? Color.FromArgb(232, 248, 220) : Color.FromArgb(253, 228, 228);
                _text.SelectionFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            }
            else
            {
                _text.SelectionBackColor = Color.White;
                _text.SelectionFont = new Font("Segoe UI", 10f);
            }
            _text.SelectionColor = Color.Black;
            _text.AppendText(absatz + "\n");
        }
    }
}

/// <summary>Übersicht über alle Kommentare.</summary>
public sealed class KommentarUebersicht : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public KommentarUebersicht()
    {
        Text = "Kommentar-Übersicht";
        Icon = SymbolIcon("kommentar");
        HilfeThema = "Kommentare";
        Size = new Size(720, 320);
        Positionieren();
        Ui.RasterGestalten(_g, false);
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, t) in new[] { ("Kat", "Kategorie"), ("Typ", "Element"), ("El", "Name"), ("F", "Feld"), ("T", "Kommentar"), ("D", "Erstellt") }) _g.Columns.Add(n, t);
        _g.Columns["T"]!.Width = 300;
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Löschen", "loeschen", () =>
        {
            var ids = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => (int)r.Tag!).ToList();
            Aendern("Kommentare löschen", AenderungsArt.Stammdaten, d => d.Kommentare.RemoveAll(k => ids.Contains(k.Id)));
            Neuaufbau();
        }));
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var k in D.Kommentare.OrderByDescending(k => k.Erstellt))
        {
            var kat = D.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie);
            var r = _g.Rows.Add(kat?.Name, Ui.TypName(k.ElementTyp), Ix.K(k.ElementTyp, k.ElementId), k.Feld, k.Text, k.Erstellt.ToString("dd.MM.yyyy HH:mm"));
            _g.Rows[r].Tag = k.Id;
            _g.Rows[r].Cells[0].Style.BackColor = Ui.Farbe(kat?.Farbe ?? 0, Color.White);
        }
    }
}


/// <summary>Vergleichsmodus: aktuellen Stundenplan mit einer gespeicherten Datei vergleichen.</summary>
public static class VergleichDialog
{
    public static void Zeigen(IWin32Window owner)
    {
        using var ofd = new OpenFileDialog { Filter = Core.Dokument.DateiFilter, Title = "Vergleichsdatei wählen" };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        var andere = Dokument.Laden(ofd.FileName).Daten;
        var d = AppZustand.D;
        var ka = Bewertung.Kennzahlen(d);
        var kb = Bewertung.Kennzahlen(andere);
        var setA = d.Stundenplan.Select(p => (p.UnterrichtId, p.Tag, p.Stunde)).ToHashSet();
        var setB = andere.Stundenplan.Select(p => (p.UnterrichtId, p.Tag, p.Stunde)).ToHashSet();
        var nurA = setA.Except(setB).Count();
        var nurB = setB.Except(setA).Count();
        using var dlg = new Dialog("Vergleichsmodus", 520, 330);
        var g = new DataGridView { Left = 10, Top = 10, Width = 500, Height = 230 };
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        g.Columns.Add("K", "Kennzahl");
        g.Columns.Add("A", "Aktueller Plan");
        g.Columns.Add("B", Path.GetFileName(ofd.FileName));
        g.Columns[0].Width = 180;
        void Z(string n, object a, object b) => g.Rows.Add(n, a, b);
        Z("Bewertung", ka.Bewertung, kb.Bewertung);
        Z("Nicht verplante Stunden", ka.NichtVerplant, kb.NichtVerplant);
        Z("Hohlstunden Klassen", ka.HohlstundenKlassen, kb.HohlstundenKlassen);
        Z("Hohlstunden Lehrer", ka.HohlstundenLehrer, kb.HohlstundenLehrer);
        Z("Kernzeitverletzungen", ka.Kernzeitverletzungen, kb.Kernzeitverletzungen);
        Z("Fach 2x / Tag", ka.FachZweimalTag, kb.FachZweimalTag);
        Z("Kollisionen", ka.Kollisionen, kb.Kollisionen);
        Z("Abweichende Stunden", nurA, nurB);
        dlg.Controls.Add(g);
        dlg.ShowDialog(owner);
    }
}

public static class ModuleDialog
{
    public static void Zeigen(IWin32Window owner)
    {
        using var d = new Dialog("Module", 620, 420);
        d.Abbrechen.Visible = false;
        var g = new DataGridView { Left = 10, Top = 10, Width = 600, Height = 360 };
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        g.Columns.Add("M", "Modul");
        g.Columns.Add("S", "Status");
        g.Columns.Add("B", "Beschreibung");
        g.Columns[0].Width = 170;
        g.Columns[2].Width = 360;
        g.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var (m, b) in new[]
        {
            ("Stundenplan (Standardpaket)", "Stammdaten, Unterricht, Optimierung, Diagnose, manuelles Planen, Stundenpläne, Druck"),
            ("Mehrwochenstundenplan", "Zeitlich begrenzter und periodischer (A/B-Woche) Unterricht, Perioden mit eigenen Stundenplänen"),
            ("Unterrichtsplanung und Wertrechnung", "Lehrereinsatz, Soll-/Ist-Werte, Fach-Faktoren, Unterrichts-Matrix, Stundentafel, Lehrervorschlag"),
            ("Vertretungsplanung", "Abwesenheiten, Vertretungsvorschlag mit Gewichtung, Bereitschaften, Vertretungsplan, Statistik und NRW-Mehrarbeit"),
            ("Pausenaufsichten", "Aufsichtsbereiche (Gänge), Häufigkeit je Lehrer, automatische Verteilung, Aufsichtsvertretung"),
            ("Kursplanung", "Schüler und Kurswahlen, Kollisionsmatrix, automatische und manuelle Bandbildung"),
            ("Abteilungsplanung", "Abteilungsdateien exportieren und wieder zusammenführen"),
            ("Info-Stundenplan", "HTML-Ausgabe aller Stunden- und Vertretungspläne, Monitor-Ansicht"),
            ("Web-Portal", "Synchronisation mit dem UntPlan Web-Portal (Pläne veröffentlichen, Krankmeldungen übernehmen)"),
        })
            g.Rows.Add(m, "aktiv", b);
        d.Controls.Add(g);
        d.ShowDialog(owner);
    }
}

/// <summary>Web-Portal: Einstellungen, Synchronisation (Pläne hochladen) und Meldungen (Krankmeldungen) übernehmen.</summary>
public sealed class PortalDialog : Dialog
{
    private readonly TextBox _adresse = new();
    private readonly TextBox _schule = new();
    private readonly TextBox _schluessel = new() { UseSystemPasswordChar = true };
    private readonly NumericUpDown _tage = new() { Minimum = 1, Maximum = 30 };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Left = 12, Top = 200, Width = 556, Height = 150 };

    public PortalDialog() : base("Web-Portal", 580, 400)
    {
        Ok.Text = "Schließen";
        Abbrechen.Visible = false;
        var p = AppZustand.D.Einstellungen.Portal;
        _adresse.Text = p.Adresse;
        _schule.Text = p.SchulId.Length > 0 ? p.SchulId : AppZustand.D.Einstellungen.Schulnummer;
        _schluessel.Text = p.ApiSchluessel;
        _tage.Value = Math.Clamp(p.TageVoraus, 1, 30);
        var y = 12;
        Ui.Zeile(this, "Portal-Adresse", _adresse, ref y, 150, 380);
        Ui.Zeile(this, "Schul-ID", _schule, ref y, 150, 160);
        Ui.Zeile(this, "API-Schlüssel", _schluessel, ref y, 150, 220);
        Ui.Zeile(this, "Vertretungen für Tage", _tage, ref y, 150, 60);
        var test = new Button { Text = "Verbindung testen", Left = 12, Top = y + 4, Width = 140 };
        var sync = new Button { Text = "Jetzt synchronisieren", Left = 160, Top = y + 4, Width = 160 };
        Controls.Add(test);
        Controls.Add(sync);
        Controls.Add(_log);
        Controls.Add(new Label { Text = $"Letzte Synchronisation: {(p.LetzteSynchronisation?.ToString("dd.MM.yyyy HH:mm") ?? "noch nie")}, Revision {p.Revision}", Left = 330, Top = y + 8, Width = 240, ForeColor = Color.DimGray });
        test.Click += async (_, _) =>
        {
            try { Log("Verbunden: " + await Core.Austausch.Portal.StatusAsync(Einst())); }
            catch (Exception ex) { Log("Fehler: " + ex.Message); }
        };
        sync.Click += async (_, _) =>
        {
            var e = Einst();
            AppZustand.Doc.Aendern("Portal-Einstellungen", AenderungsArt.Einstellungen, d => { d.Einstellungen.Portal.Adresse = e.Adresse; d.Einstellungen.Portal.SchulId = e.SchulId; d.Einstellungen.Portal.ApiSchluessel = e.ApiSchluessel; d.Einstellungen.Portal.TageVoraus = e.TageVoraus; });
            try
            {
                var paket = Core.Austausch.Portal.PaketErstellen(AppZustand.D, DateOnly.FromDateTime(DateTime.Today));
                var antwort = await Core.Austausch.Portal.HochladenAsync(e, paket);
                AppZustand.Doc.Aendern("Synchronisation", AenderungsArt.Einstellungen, d => { d.Einstellungen.Portal.Revision = antwort.Revision; d.Einstellungen.Portal.LetzteSynchronisation = DateTime.Now; });
                Log($"Hochgeladen: {paket.Stundenplan.Length} Planstunden, {paket.Vertretungen.Length} Vertretungen → Revision {antwort.Revision}");
                var meldungen = await Core.Austausch.Portal.MeldungenAsync(e);
                Log($"{meldungen.Count} Meldung(en) aus dem Portal");
                if (meldungen.Count > 0 && Ui.Frage($"{meldungen.Count} Meldung(en) (z. B. Krankmeldungen) übernehmen?\n\n" + string.Join("\n", meldungen.Select(m => $"{m.LehrerKuerzel ?? m.Absender}: {m.Von} – {m.Bis} {m.Grund} {m.Text}"))))
                {
                    (List<string> ok, List<string> nein) r = (new(), new());
                    AppZustand.Doc.Aendern("Portal-Meldungen übernehmen", AenderungsArt.Vertretung, d => r = Core.Austausch.Portal.MeldungenUebernehmen(d, meldungen));
                    await Core.Austausch.Portal.QuittierenAsync(e, r.ok.Concat(r.nein));
                    Log($"{r.ok.Count} übernommen, {r.nein.Count} abgelehnt");
                }
            }
            catch (Exception ex) { Log("Fehler: " + ex.Message); }
        };
    }

    private PortalEinstellungen Einst() => new() { Adresse = _adresse.Text.Trim(), SchulId = _schule.Text.Trim(), ApiSchluessel = _schluessel.Text, TageVoraus = (int)_tage.Value };

    private void Log(string t) => _log.AppendText($"{DateTime.Now:HH:mm:ss} {t}\r\n");
}
