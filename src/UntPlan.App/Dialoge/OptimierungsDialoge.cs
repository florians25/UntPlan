using UntPlan.Core;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Gewichtung: Gewichtungsziffern 0 (unwichtig) bis 5 (extrem wichtig). Links die Karteikarten, rechts die Regler,
/// außerdem "Gewichtungs-Verteilung" (Diagramm) und "Analyse".
/// </summary>
public sealed class GewichtungDialog : Dialog
{
    private readonly Dictionary<string, int> _werte;
    private readonly ListBox _karten = new() { Left = 8, Top = 8, Width = 150, Height = 400, IntegralHeight = false };
    private readonly Panel _inhalt = new() { Left = 166, Top = 8, Width = 560, Height = 400, AutoScroll = true };
    private readonly Button _uebernehmen = new() { Text = "Übernehmen", Width = 88, Height = 26 };

    public GewichtungDialog() : base("Gewichtung", 736, 460)
    {
        _werte = new Dictionary<string, int>(AppZustand.D.Gewichtung);
        foreach (var r in Gewichtung.Alle) _werte.TryAdd(r.Schluessel, r.Standard);
        foreach (var k in Gewichtung.Karteikarten) _karten.Items.Add(k);
        _karten.Items.Add("Gewichtungs-Verteilung");
        _karten.Items.Add($"Analyse ({AnalyseAnzahl()})");
        _karten.SelectedIndexChanged += (_, _) => Anzeigen();
        Controls.Add(_karten);
        Controls.Add(_inhalt);
        // Schaltflächen wie im Handbuch: Ok, Abbrechen, Übernehmen
        _uebernehmen.Location = Abbrechen.Location;
        Abbrechen.Location = Ok.Location;
        Ok.Location = new Point(Ok.Left - 96, Ok.Top);
        Ok.Text = "Ok";
        _uebernehmen.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        Controls.Add(_uebernehmen);
        var standard = new Button { Text = "Voreinstellung", Left = 8, Top = Ok.Top, Width = 110, Height = 26 };
        standard.Click += (_, _) => { foreach (var r in Gewichtung.Alle) _werte[r.Schluessel] = r.Standard; Anzeigen(); };
        Controls.Add(standard);
        _uebernehmen.Click += (_, _) => Speichern();
        Ok.Click += (_, _) => Speichern();
        _karten.SelectedIndex = 0;
    }

    private void Speichern()
    {
        var w = new Dictionary<string, int>(_werte);
        AppZustand.Doc.Aendern("Gewichtung", AenderungsArt.Einstellungen, d => d.Gewichtung = w);
    }

    private void Anzeigen()
    {
        _inhalt.Controls.Clear();
        var karte = _karten.SelectedItem?.ToString() ?? "";
        if (karte == "Gewichtungs-Verteilung")
        {
            var chart = new VerteilungsDiagramm(_werte.Values.ToList()) { Dock = DockStyle.Fill };
            _inhalt.Controls.Add(chart);
            return;
        }
        if (karte.StartsWith("Analyse"))
        {
            var g = new DataGridView { Dock = DockStyle.Fill };
            Ui.RasterGestalten(g, false);
            g.RowHeadersVisible = false;
            g.Columns.Add("R", "Regler");
            g.Columns.Add("W", "Gewicht");
            g.Columns.Add("V", "Verstöße im aktuellen Plan");
            g.Columns[0].Width = 330;
            var v = Bewertung.Verstoesse(AppZustand.D);
            var zuordnung = new Dictionary<string, VerstossArt[]>
            {
                ["L.Hohlstunden"] = new[] { VerstossArt.HohlstundenLehrer }, ["L.StdTag"] = new[] { VerstossArt.StdTagLehrer }, ["L.Mittagspause"] = new[] { VerstossArt.MittagspauseLehrer },
                ["L.Nachmittage"] = new[] { VerstossArt.NachmittageLehrer }, ["L.Unbestimmt"] = new[] { VerstossArt.UnbestimmterZeitwunsch }, ["L.Tagesbelastung"] = new[] { VerstossArt.Tagesbelastung },
                ["K.Hohlstunden"] = new[] { VerstossArt.HohlstundeKlasse }, ["K.Kernzeit"] = new[] { VerstossArt.Kernzeitverletzung }, ["K.StdTag"] = new[] { VerstossArt.StdTagKlasse },
                ["K.Mittagspause"] = new[] { VerstossArt.MittagspauseKlasse }, ["K.Nachmittage"] = new[] { VerstossArt.NachmittageKlasse }, ["F.ZweimalTag"] = new[] { VerstossArt.FachMehrmalsProTag },
                ["F.Doppelstunden"] = new[] { VerstossArt.DoppelstundenFehler, VerstossArt.BlockFehler }, ["F.Randstunde"] = new[] { VerstossArt.Randstunde },
                ["H.ProTag"] = new[] { VerstossArt.HauptfaecherProTag }, ["H.Folge"] = new[] { VerstossArt.HauptfaecherFolge }, ["H.Spaet"] = new[] { VerstossArt.HauptfachSpaet },
                ["R.Raumoptimierung"] = new[] { VerstossArt.KeinRaum }, ["S.Verteilung"] = new[] { VerstossArt.Verteilung }, ["Z.Lehrer"] = new[] { VerstossArt.ZeitwunschLehrer },
                ["Z.Klassen"] = new[] { VerstossArt.ZeitwunschKlasse }, ["Z.Raeume"] = new[] { VerstossArt.ZeitwunschRaum }, ["Z.Unterricht"] = new[] { VerstossArt.ZeitwunschUnterricht },
            };
            foreach (var r in Gewichtung.Alle)
                g.Rows.Add(r.Text, $"{_werte[r.Schluessel]} ({Gewichtung.ZifferText(_werte[r.Schluessel])})", zuordnung.TryGetValue(r.Schluessel, out var arten) ? v.Count(x => arten.Contains(x.Art)).ToString() : "–");
            _inhalt.Controls.Add(g);
            return;
        }
        // wie im Handbuch: Regler links (Überschrift „unwichtig … extrem wichtig“), Text des Parameters rechts daneben
        var y = 4;
        _inhalt.Controls.Add(new Label { Text = "unwichtig", Left = 4, Top = y, Width = 70, ForeColor = Color.DimGray });
        _inhalt.Controls.Add(new Label { Text = "extrem wichtig", Left = 84, Top = y, Width = 100, ForeColor = Color.DimGray });
        y += 18;
        foreach (var r in Gewichtung.Alle.Where(r => r.Karteikarte == karte))
        {
            var tb = new TrackBar { Minimum = 0, Maximum = 5, Value = _werte[r.Schluessel], Left = 4, Top = y, Width = 130, TickStyle = TickStyle.TopLeft, LargeChange = 1 };
            var wert = new Label { Text = _werte[r.Schluessel].ToString(), Left = 138, Top = y + 8, Width = 16, Font = Ui.SchriftFett };
            _inhalt.Controls.Add(new Label { Text = r.Text, Left = 160, Top = y + 8, Width = 390, Height = 34, AutoSize = false });
            var key = r.Schluessel;
            tb.ValueChanged += (_, _) => { _werte[key] = tb.Value; wert.Text = tb.Value.ToString(); };
            _inhalt.Controls.Add(tb);
            _inhalt.Controls.Add(wert);
            y += 48;
        }
        _inhalt.Controls.Add(new Label
        {
            Text = "Verwenden Sie die Prioritäten 4 und 5 nicht zu häufig. Zwischen 4 und 5 ist ein (sehr) großer Unterschied – stellen Sie Regler zunächst maximal auf 4.",
            Left = 4, Top = y + 10, Width = 540, Height = 40, ForeColor = Color.DimGray,
        });
    }

    /// <summary>Anzahl der Gewichtungsparameter, auf die die Analyse hinweist (Gewicht 5).</summary>
    private int AnalyseAnzahl() => _werte.Count(x => x.Value >= 5);

    private sealed class VerteilungsDiagramm : Control
    {
        private readonly int[] _anzahl = new int[6];

        public VerteilungsDiagramm(List<int> werte)
        {
            foreach (var w in werte) _anzahl[Math.Clamp(w, 0, 5)]++;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            var max = Math.Max(1, _anzahl.Max());
            var r = new Rectangle(50, 30, Width - 80, Height - 80);
            g.DrawLine(Pens.Black, r.Left, r.Top, r.Left, r.Bottom);
            g.DrawLine(Pens.Black, r.Left, r.Bottom, r.Right, r.Bottom);
            g.DrawString("Anzahl", Ui.Schrift, Brushes.Black, 4, 8);
            var bw = r.Width / 6f;
            for (var i = 0; i < 6; i++)
            {
                var h = r.Height * _anzahl[i] / (float)max;
                var x = r.Left + i * bw + bw * 0.25f;
                g.FillRectangle(new SolidBrush(Color.FromArgb(100, 100, 240)), x, r.Bottom - h, bw * 0.5f, h);
                g.DrawString(_anzahl[i].ToString(), Ui.Schrift, Brushes.Black, x + bw * 0.18f, r.Bottom - h - 16);
                g.DrawString(i.ToString(), Ui.Schrift, Brushes.Black, x + bw * 0.2f, r.Bottom + 4);
            }
            g.DrawString("unwichtig", Ui.SchriftKlein, Brushes.DimGray, r.Left, r.Bottom + 22);
            g.DrawString("extrem wichtig", Ui.SchriftKlein, Brushes.DimGray, r.Right - 80, r.Bottom + 22);
            g.DrawString("Ziel: wenige Regler auf 4 und 5 (glockenförmige Verteilung).", Ui.Schrift, Brushes.DimGray, r.Left, r.Bottom + 40);
        }
    }
}

/// <summary>
/// Steuerdaten zur Optimierung wie im Handbuch (4.3.1): links „Ablauf der Optimierung“ (Strategie, Anzahl Pläne, Optimierungsschritte)
/// und „Lehrer-Zuordnung bei Optimierung“ (Keine Optimierung der Zuordnung, Kein Lehrertausch mit anderem Fach, Tausch nur für
/// gleichstündige Unt., Tausch nur innerhalb Jahrgangsstufe, „Lehrer laut Unterricht“), rechts Ok/Abbrechen, Prozentsatz, Ähnlichkeit,
/// die Kennzeichen, „Für Strategie D:“ Erhöhungs-Prozentsatz, „xx% Doppelstunden“ und „Optimierung der Kurse“.
/// Nach Ok erscheint das Begleitfenster „Ablauf der Optimierung“.
/// </summary>
public sealed class OptimierungDialog : Dialog
{
    /// <summary>Strategien der Auswahlliste wie im Handbuch (A, B, D, E).</summary>
    private static readonly (Strategie S, string Text)[] Strategien =
    {
        (Strategie.A, "A Schnelle Optimierung"), (Strategie.B, "B Aufwändige Optimierung"), (Strategie.D, "D Aufwändige %-Verplanung"), (Strategie.E, "E Übernacht-Optimierung"),
    };

    private readonly ComboBox _strategie = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _plaene = new() { Minimum = 1, Maximum = 20, Value = 3 };
    private readonly NumericUpDown _schritte = new() { Minimum = 1, Maximum = 9, Value = 2 };
    private readonly TextBox _prozent = new() { MaxLength = 3 };
    private readonly NumericUpDown _erhoehung = new() { Minimum = 5, Maximum = 100, Value = 20 };
    private readonly NumericUpDown _aehnlich = new() { Minimum = 0, Maximum = 4, Value = 0 };
    private readonly CheckBox _bedingt = new() { Text = "Plan bedingt fixieren", AutoSize = true };
    private readonly CheckBox _freieTage = new() { Text = "Lehrer nur gewünschte freie Tage", AutoSize = true };
    private readonly CheckBox _kapazitaet = new() { Text = "Raumkapazität berücksichtigen", AutoSize = true };
    private readonly CheckBox _aussen = new() { Text = "Außenstellen halbtageweise", AutoSize = true, Enabled = false };
    private readonly CheckBox _vor = new() { Text = "Mit Voroptimierung", AutoSize = true };
    private readonly CheckBox _kalender = new() { Text = "Kalenderverteilung beibehalten", AutoSize = true };
    private readonly CheckBox _doppel = new() { Text = "Doppelstunden besonders optimieren", AutoSize = true };
    private readonly CheckBox _keineZuordnung = new() { Text = "Keine Optimierung der Zuordnung", AutoSize = true };
    private readonly CheckBox _anderesFach = new() { Text = "Kein Lehrertausch mit anderem Fach", AutoSize = true };
    private readonly CheckBox _gleichstuendig = new() { Text = "Tausch nur für gleichstündige Unt.", AutoSize = true };
    private readonly CheckBox _jahrgang = new() { Text = "Tausch nur innerhalb Jahrgangsstufe", AutoSize = true };
    private readonly Button _lautUnterricht = new() { Text = "Lehrer laut Unterricht", Width = 236, Height = 30 };
    private readonly CheckBox _nurNv = new() { Text = "Nur nicht verplante Stunden setzen", AutoSize = true };
    private readonly CheckBox _zuordnen = new() { Text = "Unterricht ohne Lehrer vorher automatisch zuordnen", AutoSize = true };
    private readonly CheckBox _studenten = new() { Text = "Studentenkollisionen nicht zulassen", AutoSize = true, Enabled = false };

    private static OptimierungsOptionen _letzte = new();

    /// <summary>Ergebnisse der letzten Optimierung dieser Sitzung (Menü „Optimierung | Optimierte Pläne“).</summary>
    public static List<PlanErgebnis>? LetzteErgebnisse { get; private set; }

    public static void Starten(IWin32Window owner)
    {
        using var d = new OptimierungDialog();
        if (d.ShowDialog(owner) != DialogResult.OK) return;
        var o = d.Optionen();
        _letzte = o;
        if (d._zuordnen.Checked)
        {
            var n = 0;
            AppZustand.Doc.Aendern("Automatische Unterrichtszuordnung", AenderungsArt.Unterricht, x => n = Core.Module.Wertrechnung.AutomatischZuordnen(x).Count);
            if (n > 0) Ui.Info($"{n} Kopplungszeilen wurde vor der Optimierung ein Lehrer zugeordnet.", "Optimierung");
        }
        using var b = new Begleitfenster(o);
        b.ShowDialog(owner);
        if (b.Ergebnisse is { Count: > 0 } erg) LetzteErgebnisse = erg;
    }

    /// <summary>„Optimierung | Optimierte Pläne“: Begleitfenster mit den Plänen der letzten Optimierung dieser Sitzung.</summary>
    public static void OptimiertePlaene(IWin32Window owner)
    {
        if (LetzteErgebnisse is not { Count: > 0 } erg) { Ui.Info("In dieser Sitzung wurde noch keine Optimierung durchgeführt.", "Optimierte Pläne"); return; }
        using var b = new Begleitfenster(_letzte, erg);
        b.ShowDialog(owner);
    }

    private OptimierungsOptionen Optionen()
    {
        var prozent = int.TryParse(_prozent.Text.Trim(), out var p) && p is > 0 and < 100 ? p : (int?)null;
        var s = Strategien[Math.Max(0, _strategie.SelectedIndex)].S;
        return new OptimierungsOptionen
        {
            Strategie = s, AnzahlPlaene = (int)_plaene.Value, Schritte = (int)_schritte.Value,
            Prozent = prozent, StartProzent = prozent ?? (s == Strategie.D ? 30 : 100), ErhoehungProzent = (int)_erhoehung.Value, NurNichtVerplante = _nurNv.Checked,
            Aehnlichkeit = (int)_aehnlich.Value, BedingtFixieren = _bedingt.Checked, NurGewuenschteFreieTage = _freieTage.Checked,
            DoppelstundenBesonders = _doppel.Checked, KeineZuordnungsOptimierung = _keineZuordnung.Checked, KeinTauschAnderesFach = _anderesFach.Checked,
            TauschNurGleichstuendig = _gleichstuendig.Checked, TauschNurJahrgang = _jahrgang.Checked, RaumkapazitaetBeruecksichtigen = _kapazitaet.Checked,
            MitVoroptimierung = _vor.Checked, KalenderverteilungBeibehalten = _kalender.Checked,
        };
    }

    public OptimierungDialog() : base("Steuerdaten zur Optimierung", 520, 486)
    {
        var o = _letzte;
        var d = AppZustand.D;
        // links: Ablauf der Optimierung
        var grp = new GroupBox { Text = "Ablauf der Optimierung", Left = 8, Top = 6, Width = 256, Height = 156 };
        foreach (var (_, text) in Strategien) _strategie.Items.Add(text);
        _strategie.SelectedIndex = Math.Max(0, Array.FindIndex(Strategien, x => x.S == (o.Strategie == Strategie.C ? Strategie.D : o.Strategie)));
        grp.Controls.Add(new Label { Text = "Optimierungs-Strategie (A, B, ...)", Left = 10, Top = 20, Width = 236 });
        _strategie.SetBounds(10, 40, 234, 23);
        grp.Controls.Add(_strategie);
        _plaene.Value = Math.Clamp(o.AnzahlPlaene, 1, 20);
        _schritte.Value = Math.Clamp(o.Schritte, 1, 9);
        _plaene.SetBounds(10, 78, 40, 23);
        _schritte.SetBounds(10, 118, 40, 23);
        grp.Controls.Add(_plaene);
        grp.Controls.Add(_schritte);
        grp.Controls.Add(new Label { Text = "Je Serie der Opt.: Anzahl Pläne(1-20)", Left = 58, Top = 76, Width = 186, Height = 32 });
        grp.Controls.Add(new Label { Text = "Optimierungsschritte je Plan (1-9)", Left = 58, Top = 121, Width = 186 });
        Controls.Add(grp);

        // links: Lehrer-Zuordnung bei Optimierung
        var lz = new GroupBox { Text = "Lehrer-Zuordnung bei Optimierung", Left = 8, Top = 170, Width = 256, Height = 188 };
        var y = 22;
        foreach (var c in new[] { _keineZuordnung, _anderesFach, _gleichstuendig, _jahrgang })
        {
            c.Location = new Point(10, y);
            lz.Controls.Add(c);
            y += 26;
        }
        _lautUnterricht.Location = new Point(10, 146);
        lz.Controls.Add(_lautUnterricht);
        Controls.Add(lz);
        _keineZuordnung.Checked = o.KeineZuordnungsOptimierung;
        _anderesFach.Checked = o.KeinTauschAnderesFach;
        _gleichstuendig.Checked = o.TauschNurGleichstuendig;
        _jahrgang.Checked = o.TauschNurJahrgang;
        _lautUnterricht.Enabled = Lehrerzuordnung.HatTausche(d);
        _lautUnterricht.Click += (_, _) =>
        {
            var n = 0;
            AppZustand.Doc.Aendern("Lehrer laut Unterricht", AenderungsArt.Unterricht, x => n = Lehrerzuordnung.LautUnterricht(x));
            _lautUnterricht.Enabled = false;
            Ui.Info($"{n} Lehrertausche früherer Optimierungen wurden zurückgenommen.", "Lehrer laut Unterricht");
        };
        _nurNv.Location = new Point(12, 372);
        _zuordnen.Location = new Point(12, 396);
        _studenten.Location = new Point(12, 452);
        Controls.AddRange(new Control[] { _nurNv, _zuordnen, _studenten });
        _nurNv.Checked = o.NurNichtVerplante;

        // rechts: Ok / Abbrechen, Prozentsatz, Ähnlichkeit, Kennzeichen
        Ok.Text = "Ok";
        Ok.Anchor = Abbrechen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Ok.Location = new Point(276, 12);
        Abbrechen.Location = new Point(372, 12);
        var x = 276;
        y = 50;
        _prozent.Text = o.Prozent is int p ? p.ToString() : "";
        _prozent.SetBounds(x, y, 30, 23);
        _prozent.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        Controls.Add(_prozent);
        Controls.Add(new Label { Text = "% der Klassenstunden zu verplanen (blank=100%)", Left = x + 36, Top = y, Width = 200, Height = 32 });
        y += 38;
        _aehnlich.Value = Math.Clamp(o.Aehnlichkeit, 0, 4);
        _aehnlich.SetBounds(x, y, 34, 23);
        Controls.Add(_aehnlich);
        Controls.Add(new Label { Text = "Ähnlichkeit zu Vorplan (0-4, 0=nicht ähnlich, 4=sehr ähnlich)", Left = x + 36, Top = y, Width = 200, Height = 32 });
        y += 40;
        foreach (var c in new[] { _bedingt, _freieTage, _kapazitaet, _aussen })
        {
            c.Location = new Point(x, y);
            Controls.Add(c);
            y += 25;
        }
        var fuerD = new Label { Text = "Für Strategie D:", Left = x, Top = y + 4, AutoSize = true };
        Controls.Add(fuerD);
        y += 26;
        _erhoehung.Value = Math.Clamp(o.ErhoehungProzent, 5, 100);
        _erhoehung.SetBounds(x, y, 40, 23);
        var erhText = new Label { Text = "Erhöhungs-Prozentsatz", Left = x + 46, Top = y + 3, AutoSize = true };
        Controls.Add(_erhoehung);
        Controls.Add(erhText);
        y += 32;
        foreach (var c in new[] { _vor, _kalender })
        {
            c.Location = new Point(x, y);
            Controls.Add(c);
            y += 25;
        }
        // „xx% Doppelstunden“: Anteil der Unterrichte mit Doppelstunden – ab 70 % lohnt „Doppelstunden besonders optimieren“
        var unts = d.Unterrichte.Where(u => !u.Ignorieren && u.Wochenstunden > 0).ToList();
        var anteil = unts.Count == 0 ? 0 : (int)Math.Round(100.0 * unts.Count(u => u.Bloecke().Any(b => b >= 2)) / unts.Count);
        var dg = new GroupBox { Text = $"{anteil}% Doppelstunden", Left = x, Top = y + 2, Width = 236, Height = 44 };
        _doppel.Location = new Point(10, 18);
        dg.Controls.Add(_doppel);
        Controls.Add(dg);
        _doppel.Enabled = anteil >= 70 || o.DoppelstundenBesonders;
        y += 52;
        var kurse = new GroupBox { Text = "Optimierung der Kurse", Left = x, Top = y, Width = 236, Height = 64 };
        kurse.Controls.Add(new CheckBox { Text = "Bänder neu rechnen", Left = 10, Top = 18, AutoSize = true, Enabled = false });
        kurse.Controls.Add(new CheckBox { Text = "Kurse getrennt optimieren", Left = 10, Top = 40, AutoSize = true, Enabled = false });
        Controls.Add(kurse);

        _bedingt.Checked = o.BedingtFixieren;
        _freieTage.Checked = o.NurGewuenschteFreieTage;
        _kapazitaet.Checked = o.RaumkapazitaetBeruecksichtigen;
        _vor.Checked = o.MitVoroptimierung;
        _kalender.Checked = o.KalenderverteilungBeibehalten;
        _doppel.Checked = o.DoppelstundenBesonders;
        void Umschalten()
        {
            var istD = Strategien[Math.Max(0, _strategie.SelectedIndex)].S == Strategie.D;
            _erhoehung.Enabled = erhText.Enabled = fuerD.Enabled = istD;
            _aehnlich.Enabled = !_bedingt.Checked;
            // die Tauschoptionen gelten nur, wenn die Zuordnung optimiert wird
            _anderesFach.Enabled = _gleichstuendig.Enabled = _jahrgang.Enabled = !_keineZuordnung.Checked;
        }
        _strategie.SelectedIndexChanged += (_, _) => Umschalten();
        _bedingt.CheckedChanged += (_, _) => Umschalten();
        _keineZuordnung.CheckedChanged += (_, _) => Umschalten();
        Umschalten();
    }

    /// <summary>
    /// Begleitfenster „Ablauf der Optimierung“ (Handbuch 4.3.3/4.3.4): oben das Info-Fenster („Optimierung anhalten“ Sofort / Nach Plan /
    /// Nach Serie, Abbrechen, Hilfe, „Optimierung läuft“, Strategie und Serie), darunter die Eckdaten je Plan (Bewertung, Nicht Verplante,
    /// Hohlstunden, Kernzeitverl, Fach 2x / Tag, Doppstd.-Fehler, Studenten-Kollisionen, Studenten-Hohlst.). Nach Abschluss erscheint die
    /// gelb-blaue Schaltfläche „OK“; der beste Plan ist geladen, ein Klick auf eine Zeile lädt den betreffenden Plan.
    /// </summary>
    internal sealed class Begleitfenster : Form
    {
        private readonly OptimierungsOptionen _o;
        private readonly DataGridView _g = new() { Dock = DockStyle.Fill, Name = "Plaene" };
        private readonly Panel _kopf = new() { Dock = DockStyle.Top, Height = 108 };
        private readonly GroupBox _anhalten = new() { Text = "Optimierung anhalten", Left = 8, Top = 2, Width = 112, Height = 82 };
        private readonly Button _sofort = new() { Text = "Sofort", Left = 8, Top = 18, Width = 96, Height = 24 };
        private readonly CheckBox _nachPlan = new() { Text = "Nach Plan", Left = 8, Top = 44, AutoSize = true };
        private readonly CheckBox _nachSerie = new() { Text = "Nach Serie", Left = 8, Top = 61, AutoSize = true };
        private readonly Label _endverarbeitung = new() { Text = "Optimierungs-Endverarbeitung", Left = 8, Top = 88, AutoSize = true, ForeColor = Color.DimGray };
        private readonly Button _abbrechen = new() { Text = "Abbrechen", Left = 128, Top = 8, Width = 88, Height = 24 };
        private readonly Button _hilfe = new() { Text = "Hilfe", Left = 128, Top = 38, Width = 88, Height = 24 };
        private readonly Button _status = new() { Text = "Optimierung läuft 00", Left = 226, Top = 8, Width = 200, Height = 24, FlatStyle = FlatStyle.Flat };
        private readonly Label _info = new() { Left = 226, Top = 36, Width = 300, Height = 60 };
        private readonly Label _ok = new()
        {
            Text = "OK", Left = 8, Top = 4, Width = 170, Height = 60, BackColor = Color.FromArgb(255, 255, 0), ForeColor = Color.FromArgb(20, 40, 220),
            Font = new Font("Segoe UI", 24f), TextAlign = ContentAlignment.MiddleCenter, Visible = false, Cursor = Cursors.Hand,
        };
        private readonly CancellationTokenSource _abbruch = new();
        private readonly CancellationTokenSource _sofortCts = new();
        private readonly System.Windows.Forms.Timer _uhr = new() { Interval = 1000 };
        private readonly DateTime _start = DateTime.Now;
        private readonly List<PlanErgebnis> _fertig = new();
        private PlanErgebnis? _geladen;
        private bool _laeuft;
        private volatile bool _nachPlanAktiv;
        private double _kosten;

        /// <summary>Alle gerechneten Pläne (nach Bewertung sortiert), null bei Abbruch.</summary>
        public List<PlanErgebnis>? Ergebnisse { get; private set; }

        public Begleitfenster(OptimierungsOptionen o, List<PlanErgebnis>? vorhanden = null)
        {
            _o = o;
            Text = "Ablauf der Optimierung";
            Font = Ui.Schrift;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 330);
            _anhalten.Controls.AddRange(new Control[] { _sofort, _nachPlan, _nachSerie });
            _status.FlatAppearance.BorderColor = Color.Gray;
            _status.BackColor = Color.FromArgb(240, 240, 240);
            _kopf.Controls.AddRange(new Control[] { _ok, _anhalten, _endverarbeitung, _abbrechen, _hilfe, _status, _info });
            Ui.RasterGestalten(_g, false);
            _g.ReadOnly = true;
            _g.AllowUserToAddRows = false;
            _g.RowHeadersVisible = false;
            _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _g.MultiSelect = false;
            foreach (var (name, text, breite) in new[]
                     {
                         ("Name", "", 150), ("Bewertung", "Bewertung", 70), ("NichtVerplant", "Nicht Verplante", 90), ("Hohlstunden", "Hohlstunden", 80),
                         ("Kernzeit", "Kernzeitverl", 80), ("Fach2x", "Fach 2x / Tag", 80), ("Doppstd", "Doppstd.-Fehler", 90),
                         ("StudKoll", "Studenten-Kollisionen", 120), ("StudHohl", "Studenten-Hohlst.", 100), ("Tausch", "Lehrertausche", 86),
                     })
                _g.Columns[_g.Columns.Add(name, text)].Width = breite;
            _g.CellClick += (_, e) => { if (!_laeuft && e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is PlanErgebnis p) Laden(p); };
            Controls.Add(_g);
            Controls.Add(_kopf);
            _sofort.Click += (_, _) => _sofortCts.Cancel();
            _nachPlan.CheckedChanged += (_, _) => _nachPlanAktiv = _nachPlan.Checked;
            _abbrechen.Click += (_, _) => { if (_laeuft) _abbruch.Cancel(); else Close(); };
            _hilfe.Click += (_, _) => Fenster.HilfeFenster.Zeigen("Optimierung");
            _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            _uhr.Tick += (_, _) => _status.Text = $"Optimierung läuft {(int)(DateTime.Now - _start).TotalSeconds:00}";
            FormClosing += (_, e) => { if (_laeuft) { e.Cancel = true; _abbruch.Cancel(); } };
            FormClosed += (_, _) => { _uhr.Dispose(); _abbruch.Dispose(); _sofortCts.Dispose(); };
            if (vorhanden != null)
            {
                Ergebnisse = vorhanden;
                Shown += (_, _) => Abschliessen(vorhanden, laden: false);
                return;
            }
            Shown += async (_, _) => await Rechnen();
        }

        private string Buchstabe => (Strategien.FirstOrDefault(x => x.S == _o.Strategie).Text ?? StrategieInfo.Name(_o.Strategie))[..1];

        private void InfoSetzen() =>
            _info.Text = $"Strategie: {Buchstabe} ({_o.AnzahlPlaene}/{_o.Schritte})  {_fertig.Count}/{Math.Max(0, _o.AnzahlPlaene - _fertig.Count)}/0\n1. Serie\n" +
                         $"{Math.Min(_o.AnzahlPlaene, _fertig.Count + 1)}. Plan der Serie";

        private async Task Rechnen()
        {
            _laeuft = true;
            _uhr.Start();
            InfoSetzen();
            ZeilenWaehrendLauf();
            var daten = Dokument.Klonen(AppZustand.D);
            var prog = new Progress<OptimierungsFortschritt>(f => { if (f.Kosten > 0) _kosten = f.Kosten; });
            IProgress<PlanErgebnis> melden = new Progress<PlanErgebnis>(e => { _fertig.Add(e); InfoSetzen(); ZeilenWaehrendLauf(); });
            using var aktualisieren = new System.Windows.Forms.Timer { Interval = 500 };
            aktualisieren.Tick += (_, _) => { if (_g.Rows.Count > 0) _g.Rows[0].Cells["Bewertung"].Value = _kosten > 0 ? ((long)_kosten).ToString() : ""; };
            aktualisieren.Start();
            List<PlanErgebnis>? erg = null;
            var fertigeAnzahl = 0;
            try
            {
                var o = _o;
                var abbruch = _abbruch.Token;
                var sofort = _sofortCts.Token;
                // „Nach Plan“: nach dem ersten fertigen Plan keine weiteren beginnen
                erg = await Task.Run(() => Optimierer.Optimieren(daten, o, prog, abbruch, sofort, () => !(_nachPlanAktiv && Volatile.Read(ref fertigeAnzahl) > 0),
                    e => { Interlocked.Increment(ref fertigeAnzahl); melden.Report(e); }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Ui.Fehler(ex); }
            aktualisieren.Stop();
            _uhr.Stop();
            _laeuft = false;
            if (erg is null || erg.Count == 0) { DialogResult = DialogResult.Cancel; Close(); return; }
            Ergebnisse = erg;
            ArbeitsdateienSpeichern(daten, erg);
            Abschliessen(erg, laden: true);
        }

        /// <summary>Zeilen während der Optimierung: laufender Stundenplan, bester Plan, die fertigen Pläne der Serie.</summary>
        private void ZeilenWaehrendLauf()
        {
            _g.Rows.Clear();
            _g.Rows.Add("Laufender Stundenplan :");
            var best = _fertig.OrderBy(e => e.Kennzahlen.Bewertung).FirstOrDefault();
            if (best != null) Zeile("Bester Plan :", best, true);
            for (var i = 0; i < _fertig.Count; i++) Zeile($"{i + 1}. Plan der Serie", _fertig[i], ReferenceEquals(_fertig[i], best));
        }

        private void Zeile(string name, PlanErgebnis e, bool hervorheben)
        {
            var k = e.Kennzahlen;
            var r = _g.Rows.Add(name, k.Bewertung, k.NichtVerplant, k.HohlstundenKlassen, k.Kernzeitverletzungen, k.FachZweimalTag, k.Doppelstundenfehler, 0, 0,
                e.Lehrertausche.Count == 0 ? "" : e.Lehrertausche.Count.ToString());
            _g.Rows[r].Tag = e;
            if (hervorheben) _g.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 205, 205);
        }

        /// <summary>Abschluss: gelb-blaue Schaltfläche „OK“, „Optimierte Pläne n“, Liste Bester Plan / Plan 2 …; der beste Plan wird geladen.</summary>
        private void Abschliessen(List<PlanErgebnis> erg, bool laden)
        {
            _anhalten.Visible = _abbrechen.Visible = _hilfe.Visible = _endverarbeitung.Visible = false;
            _ok.Visible = true;
            _status.Left = _info.Left = 190;
            _status.Text = $"Optimierte Pläne {erg.Count}";
            _info.Text = $"Strategie: {Buchstabe} ({_o.AnzahlPlaene}/{_o.Schritte})  {erg.Count}/0/0\n1. Serie";
            _g.Rows.Clear();
            for (var i = 0; i < erg.Count; i++) Zeile(i == 0 ? "Bester Plan" : $"Plan {i + 1}", erg[i], false);
            if (laden) Laden(erg[0]);
            else if (_g.Rows.Count > 0) _g.Rows[0].Selected = true;
        }

        /// <summary>Plan laden (ein zuvor aus diesem Fenster geladener Plan wird zuerst rückgängig gemacht).</summary>
        private void Laden(PlanErgebnis p)
        {
            if (ReferenceEquals(p, _geladen)) return;
            if (_geladen != null && AppZustand.Doc.RueckgaengigText?.StartsWith("Optimierung (Plan ", StringComparison.Ordinal) == true) AppZustand.Doc.Rueckgaengig();
            var nr = (Ergebnisse?.IndexOf(p) ?? -1) + 1;
            AppZustand.Doc.Aendern($"Optimierung (Plan {(nr > 0 ? nr : p.Nummer)})", AenderungsArt.Stundenplan, d =>
            {
                d.Stundenplan.Clear();
                d.Stundenplan.AddRange(p.Plan.Select(x => x.Kopie()));
                Lehrerzuordnung.Anwenden(d, p.Lehrertausche);
            });
            _geladen = p;
            foreach (DataGridViewRow r in _g.Rows) r.Selected = ReferenceEquals(r.Tag, p);
        }

        /// <summary>Einstellungen „Diverses | Sichern“: optimierte Pläne zusätzlich in Arbeitsdateien (work1.up …) speichern.</summary>
        private static void ArbeitsdateienSpeichern(Schuldatei daten, List<PlanErgebnis> erg)
        {
            if (!AppZustand.Einstellungen.OptimiertePlaeneSpeichern) return;
            try
            {
                var ordner = Einstellungsseiten.Pfad("Arbeitsdateien");
                Directory.CreateDirectory(ordner);
                for (var i = 0; i < erg.Count; i++)
                    File.WriteAllText(Path.Combine(ordner, $"work{i + 1}.up"), System.Text.Json.JsonSerializer.Serialize(Optimierer.MitErgebnis(daten, erg[i]), Dokument.JsonOptionen));
            }
            catch (Exception ex) { Ui.Info("Die Arbeitsdateien konnten nicht gespeichert werden:\n" + ex.Message, "Optimierung"); }
        }
    }
}
