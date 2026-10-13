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
    private readonly FlowLayoutPanel _flow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6) };

    public Eingabeassistent()
    {
        BackColor = Color.FromArgb(236, 241, 249);
        var titel = new Label { Text = "UntPlan Eingabeassistent", Dock = DockStyle.Top, Height = 26, Font = Ui.SchriftFett, ForeColor = Color.White, BackColor = Ui.Titel, Padding = new Padding(6, 5, 0, 0) };
        Controls.Add(_flow);
        Controls.Add(titel);
        Aktualisieren();
    }

    public void Aktualisieren()
    {
        if (AppZustand.Haupt is null) return;
        var d = AppZustand.D;
        _flow.SuspendLayout();
        _flow.Controls.Clear();
        void Kopf(string t) => _flow.Controls.Add(new Label { Text = t, AutoSize = false, Width = 200, Height = 22, Font = Ui.SchriftFett, ForeColor = Ui.Blau, Margin = new Padding(0, 8, 0, 0) });
        void Schritt(string t, bool erledigt, Action a)
        {
            var l = new LinkLabel { Text = (erledigt ? "✔ " : "○ ") + t, AutoSize = false, Width = 205, Height = 20, LinkColor = Color.FromArgb(30, 60, 140), ActiveLinkColor = Color.OrangeRed, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(6, 0, 0, 0) };
            l.LinkClicked += (_, _) => a();
            _flow.Controls.Add(l);
        }
        var h = AppZustand.Haupt;
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
public static class BegruessungDialog
{
    public static void Zeigen(IWin32Window owner)
    {
        using var f = new Form { Text = "Willkommen bei UntPlan", Font = Ui.Schrift, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(680, 380), ShowInTaskbar = false, BackColor = Color.White };
        var links = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Color.FromArgb(240, 244, 250) };
        var rechts = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        f.Controls.Add(rechts);
        f.Controls.Add(links);
        var y = 14;
        void Navi(string text, string symbol, bool kopf = false)
        {
            if (kopf) { links.Controls.Add(new Label { Text = text, Left = 10, Top = y, Width = 170, ForeColor = Color.Gray }); y += 24; return; }
            links.Controls.Add(new Label { Image = Symbole.Get(symbol, 16), ImageAlign = ContentAlignment.MiddleLeft, Text = "      " + text, Left = 10, Top = y, Width = 170, Height = 22 });
            y += 26;
        }
        Navi("Erste Schritte", "", true);
        Navi("Willkommen bei UntPlan", "info");
        Navi("Stundenplan erstellen", "assistent");
        Navi("Demo-Datei öffnen", "klassen");
        y += 10;
        Navi("Hilfe & Neuigkeiten", "", true);
        Navi("Handbuch / Hilfe (F1)", "hilfe");
        rechts.Controls.Add(new Label { Text = "Stundenplan erstellen", Left = 16, Top = 14, Width = 400, Font = Ui.SchriftGross });
        rechts.Controls.Add(new Label { Text = "UntPlan unterstützt Sie bei Stundenplanerstellung, Vertretungsplanung, Pausenaufsichten, Kursplanung und Wertrechnung – mit NRW-Voreinstellungen (Schulformen, Pflichtstunden, Ferien, Feiertage).\n\nDer Eingabeassistent führt Sie durch die Eingabe der wichtigsten Daten.", Left = 16, Top = 46, Width = 440, Height = 100 });
        Button Knopf(string text, string symbol, int top)
        {
            var b = new Button { Text = "   " + text, Image = Symbole.Get(symbol, 32), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft, Left = 16, Top = top, Width = 440, Height = 52, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(232, 243, 255) };
            b.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 240);
            rechts.Controls.Add(b);
            return b;
        }
        var neu = Knopf("Neue Schuldatei anlegen und den Eingabeassistenten starten", "assistent", 156);
        var demo = Knopf("Beispielschule öffnen (NRW-Gymnasium mit Daten)", "klassen", 214);
        var oeffnen = Knopf("Vorhandene Schuldatei öffnen", "oeffnen", 272);
        var nichtMehr = new CheckBox { Text = "Beim Start nicht mehr anzeigen", Left = 16, Top = 336, Width = 260, Checked = !AppZustand.Einstellungen.BegruessungZeigen };
        rechts.Controls.Add(nichtMehr);
        neu.Click += (_, _) => { f.Close(); AppZustand.Haupt.Neu(); };
        demo.Click += (_, _) => { f.Close(); AppZustand.Haupt.BeispielOeffnen(); };
        oeffnen.Click += (_, _) => { f.Close(); AppZustand.Haupt.Oeffnen(); };
        f.FormClosed += (_, _) => { AppZustand.Einstellungen.BegruessungZeigen = !nichtMehr.Checked; AppZustand.Einstellungen.Speichern(); };
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

/// <summary>Element-Rollup: Elemente per Drag &amp; Drop in Felder ziehen (z. B. Räume in das Feld "Raum").</summary>
public sealed class ElementRollup : Form
{
    private static ElementRollup? _instanz;
    private readonly ComboBox _typ = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _liste = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public static void Zeigen()
    {
        if (_instanz is null || _instanz.IsDisposed) { _instanz = new ElementRollup(); _instanz.Show(AppZustand.Haupt); }
        _instanz.Activate();
    }

    private ElementRollup()
    {
        Text = "Element-Rollup";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Size = new Size(200, 420);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(AppZustand.Haupt.Right - 260, AppZustand.Haupt.Top + 180);
        _typ.Items.AddRange(new object[] { "Klassen", "Lehrer", "Räume", "Fächer", "Raumgruppen" });
        _typ.SelectedIndexChanged += (_, _) => Fuellen();
        _liste.MouseDown += (_, e) => { if (_liste.SelectedItem is string s && e.Button == MouseButtons.Left) _liste.DoDragDrop(s.Split(' ')[0], DragDropEffects.Copy); };
        Controls.Add(_liste);
        Controls.Add(_typ);
        _typ.SelectedIndex = 2;
    }

    private void Fuellen()
    {
        var typ = _typ.SelectedIndex switch { 0 => ElementTyp.Klasse, 1 => ElementTyp.Lehrer, 2 => ElementTyp.Raum, 3 => ElementTyp.Fach, _ => ElementTyp.Raumgruppe };
        _liste.Items.Clear();
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ)) _liste.Items.Add($"{e.Kurzname}  {e.Langname}");
    }
}

/// <summary>Vergleichsmodus: aktuellen Stundenplan mit einer gespeicherten Datei vergleichen.</summary>
public static class VergleichDialog
{
    public static void Zeigen(IWin32Window owner)
    {
        using var ofd = new OpenFileDialog { Filter = "UntPlan-Schuldatei (*.untplan)|*.untplan", Title = "Vergleichsdatei wählen" };
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
