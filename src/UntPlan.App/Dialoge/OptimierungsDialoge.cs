using UntPlan.Core;
using UntPlan.Core.Optimierung;

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
        _karten.Items.Add("Analyse");
        _karten.SelectedIndexChanged += (_, _) => Anzeigen();
        Controls.Add(_karten);
        Controls.Add(_inhalt);
        _uebernehmen.Location = new Point(Ok.Left - 96, Ok.Top);
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
        if (karte == "Analyse")
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
        var y = 8;
        _inhalt.Controls.Add(new Label { Text = "unwichtig", Left = 330, Top = y, Width = 60, ForeColor = Color.DimGray });
        _inhalt.Controls.Add(new Label { Text = "extrem wichtig", Left = 450, Top = y, Width = 90, ForeColor = Color.DimGray });
        y += 18;
        foreach (var r in Gewichtung.Alle.Where(r => r.Karteikarte == karte))
        {
            _inhalt.Controls.Add(new Label { Text = r.Text, Left = 4, Top = y + 6, Width = 320, Height = 34, AutoSize = false });
            var tb = new TrackBar { Minimum = 0, Maximum = 5, Value = _werte[r.Schluessel], Left = 326, Top = y, Width = 200, TickStyle = TickStyle.BottomRight, LargeChange = 1 };
            var wert = new Label { Text = _werte[r.Schluessel].ToString(), Left = 530, Top = y + 6, Width = 20, Font = Ui.SchriftFett };
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

/// <summary>Steuerdaten zur Optimierung, Fortschritt und Ergebnisfenster.</summary>
public sealed class OptimierungDialog : Dialog
{
    private readonly ComboBox _strategie = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _plaene = new() { Minimum = 1, Maximum = 20, Value = 3 };
    private readonly NumericUpDown _schritte = new() { Minimum = 1, Maximum = 9, Value = 2 };
    private readonly NumericUpDown _start = new() { Minimum = 5, Maximum = 100, Value = 30 };
    private readonly NumericUpDown _erhoehung = new() { Minimum = 5, Maximum = 100, Value = 20 };
    private readonly CheckBox _nurNv = new() { Text = "Nur nicht verplante Stunden setzen (bestehende Stunden bleiben)", Width = 420 };

    public static void Starten(IWin32Window owner)
    {
        using var d = new OptimierungDialog();
        if (d.ShowDialog(owner) != DialogResult.OK) return;
        var o = new OptimierungsOptionen
        {
            Strategie = (Strategie)d._strategie.SelectedIndex, AnzahlPlaene = (int)d._plaene.Value, Schritte = (int)d._schritte.Value,
            StartProzent = (int)d._start.Value, ErhoehungProzent = (int)d._erhoehung.Value, NurNichtVerplante = d._nurNv.Checked,
        };
        using var lauf = new OptimierungsLauf(o);
        if (lauf.ShowDialog(owner) != DialogResult.OK || lauf.Ergebnisse is null) return;
        using var erg = new ErgebnisDialog(lauf.Ergebnisse, o);
        erg.ShowDialog(owner);
    }

    public OptimierungDialog() : base("Steuerdaten zur Optimierung", 470, 330)
    {
        var grp = new GroupBox { Text = "Ablauf der Optimierung", Left = 10, Top = 10, Width = 450, Height = 210 };
        var y = 22;
        foreach (Strategie s in Enum.GetValues<Strategie>()) _strategie.Items.Add(StrategieInfo.Name(s));
        _strategie.SelectedIndex = 1;
        grp.Controls.Add(new Label { Text = "Optimierungs-Strategie (A, B, …)", Left = 12, Top = y, Width = 300 });
        y += 18;
        _strategie.Left = 12; _strategie.Top = y; _strategie.Width = 300;
        grp.Controls.Add(_strategie);
        y += 32;
        Ui.Zeile(grp, "Je Serie der Opt.: Anzahl Pläne (1-20)", _plaene, ref y, 260, 60);
        Ui.Zeile(grp, "Optimierungsschritte je Plan (1-9)", _schritte, ref y, 260, 60);
        Ui.Zeile(grp, "Start-Prozentsatz (C, D)", _start, ref y, 260, 60);
        Ui.Zeile(grp, "Erhöhungs-Prozentsatz (C, D)", _erhoehung, ref y, 260, 60);
        Controls.Add(grp);
        _nurNv.Left = 14; _nurNv.Top = 228;
        Controls.Add(_nurNv);
        var hinweis = new Label { Left = 14, Top = 252, Width = 440, Height = 30, ForeColor = Color.DarkRed, Text = "Prüfen Sie vor den ersten Optimierungsläufen Ihre Eingabedaten mit der Diagnose." };
        Controls.Add(hinweis);
        void Umschalten() { _start.Enabled = _erhoehung.Enabled = _strategie.SelectedIndex is 2 or 3; }
        _strategie.SelectedIndexChanged += (_, _) => Umschalten();
        Umschalten();
    }

    private sealed class OptimierungsLauf : Form
    {
        private readonly ProgressBar _bar = new() { Left = 12, Top = 40, Width = 400, Height = 22, Maximum = 1000 };
        private readonly Label _text = new() { Left = 12, Top = 12, Width = 400, Height = 22 };
        private readonly CancellationTokenSource _cts = new();
        public List<PlanErgebnis>? Ergebnisse { get; private set; }

        public OptimierungsLauf(OptimierungsOptionen o)
        {
            Text = "Optimierung";
            Font = Ui.Schrift;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(424, 110);
            var abbrechen = new Button { Text = "Abbrechen", Left = 324, Top = 74, Width = 88 };
            abbrechen.Click += (_, _) => _cts.Cancel();
            Controls.AddRange(new Control[] { _bar, _text, abbrechen });
            var daten = Dokument.Klonen(AppZustand.D);
            Shown += async (_, _) =>
            {
                var prog = new Progress<OptimierungsFortschritt>(f =>
                {
                    _bar.Value = Math.Clamp((int)(f.Anteil * 1000), 0, 1000);
                    _text.Text = $"{StrategieInfo.Name(o.Strategie)} – {f.Phase} ({f.Plaene} Pläne)";
                });
                try
                {
                    Ergebnisse = await Task.Run(() => Optimierer.Optimieren(daten, o, prog, _cts.Token));
                    DialogResult = DialogResult.OK;
                }
                catch (OperationCanceledException) { DialogResult = DialogResult.Cancel; }
                catch (Exception ex) { Ui.Fehler(ex); DialogResult = DialogResult.Cancel; }
            };
        }
    }

    private sealed class ErgebnisDialog : Dialog
    {
        public ErgebnisDialog(List<PlanErgebnis> erg, OptimierungsOptionen o) : base("Ergebnis der Optimierung", 820, 360)
        {
            Ok.Text = "Plan übernehmen";
            var okFeld = new Label { Text = "OK", Left = 10, Top = 10, Width = 150, Height = 44, BackColor = Color.FromArgb(255, 240, 100), ForeColor = Color.FromArgb(30, 60, 200), Font = new Font("Segoe UI", 18f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(okFeld);
            Controls.Add(new Label { Text = $"Optimierte Pläne: {erg.Count}\nStrategie: {StrategieInfo.Name(o.Strategie)}, Schritte {o.Schritte}", Left = 170, Top = 12, Width = 420, Height = 40 });
            var g = new DataGridView { Left = 10, Top = 64, Width = 800, Height = 240 };
            Ui.RasterGestalten(g, false);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.RowHeadersVisible = false;
            foreach (var t in new[] { "Plan", "Bewertung", "Nicht verplante", "Hohlstunden", "Kernzeitverl.", "Fach 2x/Tag", "Doppelst.-Fehler", "Zeitwünsche", "Lehrer-Hohlst.", "Kein Raum", "Kollisionen", "Dauer" })
                g.Columns.Add(t, t);
            var aktuell = Bewertung.Kennzahlen(AppZustand.D);
            void Zeile(string name, Kennzahlen k, object? tag, string dauer)
            {
                var r = g.Rows.Add(name, k.Bewertung, k.NichtVerplant, k.HohlstundenKlassen, k.Kernzeitverletzungen, k.FachZweimalTag, k.Doppelstundenfehler, k.Zeitwunschverletzungen, k.HohlstundenLehrer, k.OhneRaum, k.Kollisionen, dauer);
                g.Rows[r].Tag = tag;
            }
            Zeile("Akt. Plan", aktuell, null, "");
            foreach (var e in erg) Zeile($"Plan {e.Nummer}", e.Kennzahlen, e, $"{e.Dauer.TotalSeconds:0.0} s");
            g.Rows[0].DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            if (g.Rows.Count > 1) g.Rows[1].Selected = true;
            Controls.Add(g);
            Controls.Add(new Label { Text = "Je niedriger die Bewertungszahl, umso weniger Vorgaben sind verletzt. Der beste Plan ist vorausgewählt.", Left = 10, Top = 310, Width = 600, ForeColor = Color.DimGray });
            Ok.Click += (_, _) =>
            {
                if (g.SelectedRows.Count == 0 || g.SelectedRows[0].Tag is not PlanErgebnis p) return;
                AppZustand.Doc.Aendern($"Optimierung (Plan {p.Nummer})", AenderungsArt.Stundenplan, d => { d.Stundenplan.Clear(); d.Stundenplan.AddRange(p.Plan); });
            };
        }
    }
}
