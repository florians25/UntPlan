using System.Globalization;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vertretungsvorschlag wie im Vorbild: folgt der markierten Vertretung. Aufklappbarer Kopf „30.8. Mo-1 Se/Mu/05D“, aufklappbare
/// Optionen (Stundenblock, Dislozierungen ignorieren, Dauervertretung) mit den Anzahlen der Reiter, Reiter
/// Vertretungsvorschläge / Betreuungen / Räume und unten aufklappbar die Vorziehungen.
/// Spalten wie im Vorbild: Name, Stundenplan (drei Stunden, die Vertretungsstunde gestrichelt markiert), Merker zur Stunde,
/// Vertr., Entfall, Zähler, Zeitwunsch, Jahressaldo, Kennzeichen, reg. Bereitschaft, Dep. (Werte mit drei Nachkommastellen).
/// </summary>
public sealed class VertretungsvorschlagFenster : MdiFenster
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Color ZaehlerGelb = Color.FromArgb(252, 252, 129);
    private static readonly Color ZaehlerRot = Color.FromArgb(255, 128, 128);
    private static readonly Color MerkerRot = Color.FromArgb(255, 55, 55);
    private static readonly Color Bereitschaftsgruen = Color.FromArgb(200, 235, 200);
    private static readonly Color KopfLinie = Color.FromArgb(105, 105, 105);
    private static readonly string[] Stundenplanspalten = { "S1", "S2", "S3" };

    private readonly Klappleiste _kopfLeiste = new(false) { Text = "" };
    private readonly Label _kopfDetails = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 3, 4, 0), BackColor = Color.White, Visible = false };
    private readonly Klappleiste _optLeiste = new(false) { Offen = true };
    private readonly Panel _optionen = new() { Dock = DockStyle.Top, Height = 44 };
    private readonly CheckBox _block = new() { Text = "Stundenblock", AutoSize = true, Location = new Point(12, 3) };
    private readonly CheckBox _disloz = new() { Text = "Dislozierungen ignorieren", AutoSize = true, Location = new Point(230, 3) };
    private readonly CheckBox _dauer = new() { Text = "Dauervertretung", AutoSize = true, Location = new Point(12, 23) };
    private readonly CheckBox _nahe = new() { Text = "Nur in der Nähe", AutoSize = true, Location = new Point(230, 23), Visible = false };
    private readonly CheckBox _besetzt = new() { Text = "Auch besetzte Räume anzeigen", AutoSize = true, Location = new Point(230, 23), Visible = false };
    private readonly CheckBox _zusatzRaum = new() { Text = "Zusätzlichen Raum zuordnen", AutoSize = true, Location = new Point(430, 23), Visible = false };
    private readonly Reiterleiste _reiter = new() { Dock = DockStyle.Top };
    private readonly Panel _inhalt = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill, Name = "Vorschlag" };
    private readonly DataGridView _betreuung = new() { Dock = DockStyle.Fill, Name = "Betreuung", Visible = false };
    private readonly Steuerelemente.RaumListe _raeume = new("vorschlag", new[] { "Rm", "Kap", "AR", "SR", "B", "RG" }) { Dock = DockStyle.Fill, Visible = false };
    private readonly Klappleiste _vorzLeiste = new(true) { Dock = DockStyle.Bottom };
    private readonly DataGridView _vorziehung = new() { Dock = DockStyle.Bottom, Height = 150, Name = "Vorziehung", Visible = false };
    private readonly ToolStripButton _alle = new("Alle Lehrer") { CheckOnClick = true, ToolTipText = "Auch gesperrte und weit entfernte Lehrer anzeigen" };
    private readonly ToolStripButton _weiter = new("Weiter zur nächsten offenen") { CheckOnClick = true, Checked = true, ToolTipText = "Nach dem Zuweisen automatisch die nächste offene Vertretung wählen" };
    private readonly ToolTip _tip = new();
    private Vertretung? _v;

    public VertretungsvorschlagFenster()
    {
        Text = "Vertretungsvorschlag";
        HilfeThema = "Vertretungsvorschlag";
        Icon = SymbolIcon("vertretung");
        Size = new Size(720, 640);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Vertreter übernehmen (Doppelklick, Enter)", "planen", Uebernehmen));
        ts.Items.Add(Knopf("Entfall", "entfall", EntfallMitRueckfrage));
        ts.Items.Add(Knopf("Vertr. o. Lehrkr.", "lehrer", () => Aktion("Vertr. o. Lehrkr.", (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer))));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(_weiter);
        ts.Items.Add(_alle);
        ts.Items.Add(Knopf("Einstellungen Vertretungsvorschlag", "gewichtung", () => { if (VertretungsEinstellungenDialog.Zeigen(this, 2)) Neuaufbau(); }));
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        foreach (var c in new[] { _block, _disloz, _dauer, _nahe, _besetzt })
            c.CheckedChanged += (_, _) => Neuaufbau();

        _kopfLeiste.Umgeschaltet += () => _kopfDetails.Visible = _kopfLeiste.Offen;
        _optLeiste.Umgeschaltet += () => _optionen.Visible = _optLeiste.Offen;
        _vorzLeiste.Umgeschaltet += () => _vorziehung.Visible = _vorzLeiste.Offen;
        _optionen.Controls.AddRange(new Control[] { _block, _disloz, _dauer, _nahe, _besetzt, _zusatzRaum });
        _tip.SetToolTip(_block, "Vorschlag für alle Stunden des Blocks (Doppelstunde)");
        _tip.SetToolTip(_disloz, "Auch Lehrkräfte vorschlagen, deren angrenzender Unterricht in einem anderen Gebäude (Abteilung des Raumes) liegt");
        _tip.SetToolTip(_dauer, "Vertreter für alle Stunden dieses Unterrichts während der Absenz übernehmen");
        _tip.SetToolTip(_nahe, "Nur gekoppelte Lehrkräfte, gleicher Gang oder gleiches Stockwerk");
        _tip.SetToolTip(_zusatzRaum, "Den gewählten Raum zusätzlich zum bestehenden Raum zuordnen");

        // --- Vertretungsvorschläge ---
        RasterEinrichten(_vorschlag);
        Spalten(_vorschlag, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("S1", "Stundenplan", 50), ("S2", "", 50), ("S3", "", 50), ("Merker", "Merker zur Stunde", 82), ("Vertr", "Vertr.", 48),
            ("Entf", "Entfall", 48), ("Zaehler", "Zähler", 50), ("ZW", "Zeitwunsch", 42), ("Jahr", "Jahressaldo", 52), ("Kennz", "Kennzeichen", 42),
            ("RegB", "reg. Bereitschaft", 42), ("Dep", "Dep.", 58), ("Fach", "Fach", 38), ("Klasse", "Klasse", 44), ("StdTag", "Std./Tag", 50),
            ("Woche", "Woche", 46), ("Monat", "Monat", 46), ("Punkte", "Pkt.", 40), ("Kr", "Kriterien", 220),
        });
        foreach (var n in new[] { "Vertr", "Entf", "Zaehler", "Jahr", "Dep", "Punkte" }) _vorschlag.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var n in new[] { "Fach", "Klasse", "StdTag", "Woche", "Monat", "RegB" }) _vorschlag.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Uebernehmen(); };
        _vorschlag.CellFormatting += VorschlagFormatieren;
        _vorschlag.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Uebernehmen(); e.Handled = true; } };
        _vorschlag.ContextMenuStrip = Menue(("Vertreter übernehmen (Doppelklick, Enter)", Uebernehmen));

        // --- Betreuungen ---
        RasterEinrichten(_betreuung);
        Spalten(_betreuung, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("S1", "Stundenplan", 50), ("S2", "", 50), ("S3", "", 50), ("Raum", "Raum", 46), ("Klasse", "Klasse", 46), ("Fach", "Fach", 46),
            ("Zaehler", "Zähler", 50), ("ZW", "Zeitwunsch", 42), ("Jahr", "Jahressaldo", 52), ("Kennz", "Kennzeichen", 42), ("Kopplung", "Kopplung", 42),
            ("Naehe", "Nähe", 110), ("Heute", "Betr. heute", 60),
        });
        foreach (var n in new[] { "Zaehler", "Jahr" }) _betreuung.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _betreuung.Columns["Heute"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _betreuung.CellFormatting += BetreuungFormatieren;
        _betreuung.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Betreuung(); e.Handled = true; } };
        _betreuung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Betreuung(); };
        _betreuung.ContextMenuStrip = Menue(("Betreuung übernehmen (Doppelklick, Enter)", Betreuung));

        // --- Räume ---
        _raeume.Gewaehlt += _ => Raum();
        _raeume.Raster.ContextMenuStrip = Menue(("Raum übernehmen (Doppelklick, Enter)", Raum));

        // --- Vorziehungen ---
        RasterEinrichten(_vorziehung);
        Spalten(_vorziehung, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("Tag", "Tag", 70), ("Std", "Std.", 36), ("Klasse", "Klasse", 60), ("Fach", "Fach", 56), ("Raum", "Raum", 50), ("Art", "Art", 140),
        });
        _vorziehung.Columns["Std"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _vorziehung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Vorziehen(); };
        _vorziehung.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Vorziehen(); e.Handled = true; } };
        _vorziehung.ContextMenuStrip = Menue(("Vorziehen: Unterricht in die offene Stunde verlegen (Doppelklick, Enter)", Vorziehen));

        _reiter.Gewechselt += ReiterGewechselt;
        _inhalt.Controls.Add(_vorschlag);
        _inhalt.Controls.Add(_betreuung);
        _inhalt.Controls.Add(_raeume);

        var flaeche = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 4), BackColor = Ui.BlauSehrHell };
        // Andockreihenfolge: zuletzt hinzugefügte Steuerelemente liegen außen
        flaeche.Controls.Add(_inhalt);
        flaeche.Controls.Add(_reiter);
        flaeche.Controls.Add(_vorzLeiste);
        flaeche.Controls.Add(_vorziehung);
        flaeche.Controls.Add(_optionen);
        flaeche.Controls.Add(_optLeiste);
        flaeche.Controls.Add(_kopfDetails);
        flaeche.Controls.Add(_kopfLeiste);
        _optionen.BackColor = Ui.BlauSehrHell;
        Controls.Add(flaeche);
        Controls.Add(ts);
        AppZustand.VertretungGewaehlt += Gewaehlt;
        FormClosed += (_, _) => AppZustand.VertretungGewaehlt -= Gewaehlt;
        _v = AppZustand.AktiveVertretung;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
    }

    // ----------------------------------------------------------------------------------------------
    // Aufbau
    // ----------------------------------------------------------------------------------------------

    private ContextMenuStrip Menue(params (string Text, Action Aktion)[] eintraege)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        foreach (var (t, a) in eintraege) cm.Items.Add(t, null, (_, _) => a());
        return cm;
    }

    private static void Spalten(DataGridView g, (string Name, string Kopf, int Breite)[] spalten)
    {
        foreach (var (n, k, b) in spalten)
        {
            var c = g.Columns[g.Columns.Add(n, k)];
            c.Width = b;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        if (g.Columns.Contains("Kr")) g.Columns["Kr"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        if (g.Columns.Contains("Kr")) g.Columns["Kr"]!.MinimumWidth = 160;
    }

    /// <summary>Tabelle wie im Vorbild: weiße Kopfzeile ohne Zeilenköpfe, abwechselnde Zeilen, nur die aktuelle Zelle orange.</summary>
    private void RasterEinrichten(DataGridView g)
    {
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.MultiSelect = false;
        g.ColumnHeadersHeight = 20;
        g.RowTemplate.Height = 21;
        g.AllowUserToResizeColumns = true;
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.DefaultCellStyle.SelectionBackColor = Ui.ZelleAktivVertretung;
        g.DefaultCellStyle.SelectionForeColor = Color.Black;
        g.ShowCellToolTips = true;
        g.CellPainting += ZelleZeichnen;
        g.CellFormatting += (_, e) =>
        {
            // Vorbild: nur die aktuelle Zelle ist orange hinterlegt (auch nach einem Moduswechsel der Tabellenfarben)
            if (e.CellStyle != null) e.CellStyle.SelectionBackColor = Ui.ZelleAktivVertretung;
        };
    }

    private void ReiterGewechselt()
    {
        var i = _reiter.Index;
        _vorschlag.Visible = i == 0;
        _betreuung.Visible = i == 1;
        _raeume.Visible = i == 2;
        _nahe.Visible = i == 1;
        _besetzt.Visible = _zusatzRaum.Visible = i == 2;
        (i switch { 0 => (Control)_vorschlag, 1 => _betreuung, _ => _raeume.Raster }).Focus();
    }

    private void Gewaehlt(Vertretung? v)
    {
        if (IsDisposed) return;
        _v = v is null ? null : D.Vertretungen.FirstOrDefault(x => x.Id == v.Id) ?? v;
        Neuaufbau();
    }

    /// <summary>Kopf wie im Vorbild: „30.8. Mo-1 Se/Mu/05D“ (Datum, Tag-Stunde, Lehrer/Fach/Klasse).</summary>
    internal static string KopfText(DatenIndex ix, Vertretung v)
    {
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var teile = new[] { ix.KLehrer(v.LehrerId), v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KFach(v.FachId), ix.KKlassen(v.NeueKlassenIds) };
        return $"{v.Datum.Day}.{v.Datum.Month}. {tag}-{Vertretungsplan.StundeText(v)} {string.Join("/", teile.Where(t => t.Length > 0))}";
    }

    private void Neuaufbau()
    {
        _vorschlag.Rows.Clear();
        _vorziehung.Rows.Clear();
        _betreuung.Rows.Clear();
        _raeume.Setzen(Array.Empty<RaumMoeglichkeit>());
        var v = _v;
        if (v is null)
        {
            _kopfLeiste.Text = "Keine Vertretung markiert";
            _kopfDetails.Text = "Bitte im Vertretungsfenster oder im tagesaktuellen Stundenplan eine Vertretung markieren.";
            Zaehlerstaende();
            return;
        }
        var mehr = AppZustand.MarkierteVertretungen.Count > 1 ? $"   (+{AppZustand.MarkierteVertretungen.Count - 1} weitere markiert)" : "";
        _kopfLeiste.Text = KopfText(Ix, v) + mehr;
        _kopfDetails.Text = $"Vtr-Nr. {v.Id}   {Format.Datum(v.Datum)}   {Vertretungsplan.StundeText(v)}. Std.   Klasse(n) {Ix.KKlassen(v.NeueKlassenIds)}   Lehrer {Ix.KLehrer(v.LehrerId)}   Fach {Ix.KFach(v.FachId)}   " +
                            $"{(v.Art == VertretungsArt.Pausenaufsicht ? "Gang " + Ix.K(ElementTyp.Gang, v.GangId) : "Raum " + Ix.KRaum(v.VertretungsraumId ?? v.RaumId))}{Environment.NewLine}" +
                            $"{Vertretungsplan.ArtText(D, v.Art)}{(v.Grund.Length > 0 ? ", " + v.Grund : "")}{(v.Text.Length > 0 ? " – " + v.Text : "")}";
        var pause = v.Art == VertretungsArt.Pausenaufsicht;
        _block.Enabled = !pause && VertretungsVorschlag.Block(D, v).Count > 1;
        _disloz.Enabled = !pause && VertretungsVorschlag.DislozierungenDefiniert(D);
        _dauer.Enabled = !pause && v.UnterrichtId.HasValue;
        if (pause)
        {
            var a = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(D, v.Datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
            var (zv, zb) = Zaehlung.Zeitraum(D, v.Datum);
            foreach (var l in Core.Module.Pausenaufsichten.Kandidaten(D, v.Datum, a))
            {
                var z = Zaehlung.Lehrer(D, l, zv, zb);
                var (vw, ew) = VertretungsVorschlag.Werte(z);
                var r = _vorschlag.Rows.Add(l.Kurzname, "", "", "", "", Zahl(vw), Zahl(ew), ZahlLeer(z.Summe), "", "", "", "", "", "", "", "", "", "", "", $"Aufsichten: {Core.Module.Pausenaufsichten.Anzahl(D, l.Id)}");
                _vorschlag.Rows[r].Tag = l.Id;
            }
            Zaehlerstaende();
            return;
        }
        var langname = D.Vertretungseinstellungen.Vorschlag.Langname;
        foreach (var k in VertretungsVorschlag.Berechnen(D, v, Block, _alle.Checked, _disloz.Enabled && _disloz.Checked))
        {
            var z = k.Zellen;
            var r = _vorschlag.Rows.Add(langname ? k.Lehrer.VollerName : k.Lehrer.Kurzname, ZellText(z, 0), ZellText(z, 1), ZellText(z, 2), k.Merker,
                Zahl(k.VertretungenWert), Zahl(k.EntfaelleWert), ZahlLeer(k.Zaehler), Zeitwunsch(k.Zeitwunsch), ZahlLeer(k.Jahreszaehler), k.Kennzeichen ? "K" : "",
                k.RegelBereitschaft > 0 ? k.RegelBereitschaft.ToString() : "", k.Deputat is decimal dep ? ZahlLeer(dep) : "",
                k.Fachlehrer ? "✔" : "", k.Klassenlehrer ? "KL" : k.KenntKlasse ? "✔" : "", k.UnterrichtAmTag, k.ZaehlerWoche, k.ZaehlerMonat, ((int)k.Punkte).ToString(), string.Join(", ", k.Kriterien));
            var zeile = _vorschlag.Rows[r];
            zeile.Tag = k;
            if (k.Kennzeichen) zeile.Cells["Kennz"].ToolTipText = k.KennzeichenText;
            zeile.Cells["Name"].ToolTipText = k.Lehrer.VollerName + (k.Lage.Length > 0 ? " – " + k.Lage : "");
        }
        if (v.Offen)
            foreach (var vz in VertretungsVorschlag.Vorziehungen(D, v))
            {
                var tag = $"{Planungsdienst.TageKurz[((int)vz.Datum.DayOfWeek + 6) % 7]} {vz.Datum.Day}.{vz.Datum.Month}.";
                var r = _vorziehung.Rows.Add(vz.Lehrer, tag, vz.Stunde, vz.Klassen, vz.Fach, vz.Raum, vz.Randstunde ? "Randstunde" : "erzeugt neue Lücke");
                _vorziehung.Rows[r].Tag = vz;
                _vorziehung.Rows[r].Cells["Art"].ToolTipText = vz.Beschreibung;
            }
        foreach (var b in VertretungsVorschlag.Betreuungen(D, v).Where(b => !_nahe.Checked || b.Naehe <= 2))
        {
            var z = b.Zellen;
            var r = _betreuung.Rows.Add(langname ? b.Lehrer.VollerName : b.Lehrer.Kurzname, ZellText(z, 0), ZellText(z, 1), ZellText(z, 2), b.Raum, b.Klassen, b.Fach,
                ZahlLeer(b.Zaehler), Zeitwunsch(b.Zeitwunsch), ZahlLeer(b.Jahreszaehler), b.Kennzeichen ? "K" : "", b.Kopplung ? "✔" : "", b.NaeheText,
                b.BetreuungenHeute > 0 ? b.BetreuungenHeute.ToString() : "");
            _betreuung.Rows[r].Tag = b;
            if (b.Kennzeichen) _betreuung.Rows[r].Cells["Kennz"].ToolTipText = b.KennzeichenText;
        }
        _raeume.Langname = langname;
        _raeume.Setzen(Raumvertretungen.MoeglicheRaeume(D, v, _besetzt.Checked, Block));
        foreach (var g in new[] { _vorschlag, _betreuung, _vorziehung })
            if (g.Rows.Count > 0) g.CurrentCell = g.Rows[0].Cells[0];
        Zaehlerstaende();
    }

    private bool Block => _block.Enabled && _block.Checked;

    private bool Dauer => _dauer.Enabled && _dauer.Checked;

    private void Zaehlerstaende()
    {
        var (n1, n2, n3) = (_vorschlag.Rows.Count, _betreuung.Rows.Count, _raeume.Raster.Rows.Count);
        _reiter.Setzen($"Vertretungsvorschläge ({n1})", $"Betreuungen ({n2})", $"Räume ({n3})");
        _optLeiste.Text = $"Vertretungsvorschläge ({n1}),Betreuungen ({n2}),Räume ({n3})";
        _vorzLeiste.Text = $"Vorziehungen ({_vorziehung.Rows.Count})";
    }

    private static string ZellText(List<StundenplanZelle> z, int i) => i < z.Count ? z[i].Text : "";

    /// <summary>Zahlenformat wie im Vorbild: drei Nachkommastellen mit Punkt (0.000, -1.000).</summary>
    internal static string Zahl(decimal w) => w.ToString("0.000", Inv);

    internal static string ZahlLeer(decimal w) => w == 0 ? "" : Zahl(w);

    internal static string Zeitwunsch(int w) => w == 0 ? "" : w.ToString("+0;-0", Inv);

    /// <summary>Merker zur Stunde wie im Vorbild: 0–3 weiß, 4–8 zunehmend rosa, 9 rot; Bereitschaft (Fachkürzel) grün.</summary>
    internal static Color? MerkerFarbe(string merker)
    {
        if (merker.Length == 0 || merker == "x") return null;
        if (!int.TryParse(merker, out var m)) return Bereitschaftsgruen;
        if (m >= 9) return MerkerRot;
        if (m < 4) return null;
        var g = 217 - (m - 4) * 17;
        return Color.FromArgb(255, g, g);
    }

    // ----------------------------------------------------------------------------------------------
    // Darstellung
    // ----------------------------------------------------------------------------------------------

    private static List<StundenplanZelle>? Zellen(object? tag) => tag switch { Kandidat k => k.Zellen, Betreuungskandidat b => b.Zellen, _ => null };

    private void VorschlagFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null) return;
        var name = _vorschlag.Columns[e.ColumnIndex].Name;
        var tag = _vorschlag.Rows[e.RowIndex].Tag;
        if (name == "Zaehler")
        {
            var w = tag switch { Kandidat k => k.Zaehler, _ => 0m };
            e.CellStyle.BackColor = w > 0 ? ZaehlerRot : ZaehlerGelb;
        }
        StundenplanFarbe(_vorschlag, e, name);
        if (tag is not Kandidat ka) return;
        if (name == "Name")
        {
            if (ka.Rot) e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.Firebrick;
            if (ka.Benachrichtigen) e.CellStyle.Font = Ui.SchriftFett;
        }
        if (name == "Merker" && MerkerFarbe(ka.Merker) is { } mf) e.CellStyle.BackColor = mf;
    }

    private void BetreuungFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null) return;
        var name = _betreuung.Columns[e.ColumnIndex].Name;
        if (name == "Zaehler" && _betreuung.Rows[e.RowIndex].Tag is Betreuungskandidat b) e.CellStyle.BackColor = b.Zaehler > 0 ? ZaehlerRot : ZaehlerGelb;
        StundenplanFarbe(_betreuung, e, name);
    }

    /// <summary>Spalte „Stundenplan“: Hintergrund in der Fachfarbe (z. B. ÖPR rot).</summary>
    private static void StundenplanFarbe(DataGridView g, DataGridViewCellFormattingEventArgs e, string name)
    {
        var i = Array.IndexOf(Stundenplanspalten, name);
        if (i < 0 || e.CellStyle is null || Zellen(g.Rows[e.RowIndex].Tag) is not { } z || i >= z.Count) return;
        if (z[i].Hintergrund != 0 && !z[i].Entfallen) e.CellStyle.BackColor = Ui.Farbe(z[i].Hintergrund, Color.White);
        e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    /// <summary>
    /// Eigene Darstellung: Kopfzeile weiß mit abgeschnittenen Überschriften und über drei Spalten „Stundenplan“; Mini-Stundenplan
    /// mit durchgestrichenem rotem Fach bei Entfall und gestrichelt markierter Vertretungsstunde; Kennzeichen als Symbol, Haken grün.
    /// </summary>
    private void ZelleZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView g || e.ColumnIndex < 0 || e.Graphics is null) return;
        var spalte = g.Columns[e.ColumnIndex];
        if (e.RowIndex == -1)
        {
            KopfZeichnen(g, e, spalte);
            return;
        }
        if (e.RowIndex < 0 || e.CellStyle is null) return;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var i = Array.IndexOf(Stundenplanspalten, spalte.Name);
        if (i >= 0 && Zellen(g.Rows[e.RowIndex].Tag) is { } z && i < z.Count)
        {
            Ui.ZelleOhneInhalt(e);
            var zelle = z[i];
            var r = e.CellBounds;
            var hg = selected ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
            var vg = zelle.Entfallen ? Color.Red : zelle.Vordergrund != 0 ? Ui.Farbe(zelle.Vordergrund, Color.Black) : Ui.Textfarbe(hg);
            var text = new Rectangle(r.X + 1, r.Y, r.Width - 3, r.Height - 1);
            TextRenderer.DrawText(e.Graphics, zelle.Text, e.CellStyle.Font ?? g.Font, text, vg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (zelle.Entfallen && zelle.Text.Length > 0)
            {
                var breite = Math.Min(text.Width - 4, TextRenderer.MeasureText(zelle.Text, e.CellStyle.Font ?? g.Font).Width);
                var y = r.Y + r.Height / 2;
                using var p = new Pen(Color.Red, 1);
                e.Graphics.DrawLine(p, r.X + (r.Width - breite) / 2, y, r.X + (r.Width + breite) / 2, y);
            }
            if (zelle.Ziel) ZielMarkieren(e.Graphics, r, e.RowIndex == 0, e.RowIndex == g.Rows.Count - 1);
            e.Handled = true;
            return;
        }
        if (spalte.Name == "Kennz" && e.Value as string == "K")
        {
            Ui.ZelleOhneInhalt(e);
            var r = e.CellBounds;
            var b = new Rectangle(r.X + (r.Width - 10) / 2, r.Y + (r.Height - 13) / 2, 10, 13);
            using (var br = new SolidBrush(Color.FromArgb(40, 90, 180))) e.Graphics.FillRectangle(br, b);
            using (var br = new SolidBrush(Color.FromArgb(120, 170, 235))) e.Graphics.FillRectangle(br, b.X + 1, b.Y + 1, 2, b.Height - 2);
            using (var p = new Pen(Color.FromArgb(20, 50, 110))) e.Graphics.DrawRectangle(p, b);
            using (var p = new Pen(Color.White)) e.Graphics.DrawLine(p, b.X + 4, b.Y + 3, b.X + 8, b.Y + 3);
            e.Handled = true;
            return;
        }
        if (e.Value as string == "✔")
        {
            Ui.ZellHaken(e, Color.FromArgb(60, 170, 60));
        }
    }

    /// <summary>Vertretungsstunde: gelb-orange gestrichelter Rahmen links und rechts, oben in der ersten und unten in der letzten Zeile.</summary>
    private static void ZielMarkieren(Graphics g, Rectangle r, bool erste, bool letzte)
    {
        using var gelb = new Pen(Color.FromArgb(255, 235, 0), 2);
        using var orange = new Pen(Color.FromArgb(255, 150, 0), 2) { DashPattern = new[] { 2f, 2f } };
        var (x1, x2, y1, y2) = (r.X + 1, r.Right - 2, r.Y, r.Bottom - 1);
        foreach (var p in new[] { gelb, orange })
        {
            g.DrawLine(p, x1, y1, x1, y2);
            g.DrawLine(p, x2, y1, x2, y2);
            if (erste) g.DrawLine(p, x1, y1 + 1, x2, y1 + 1);
            if (letzte) g.DrawLine(p, x1, y2 - 1, x2, y2 - 1);
        }
    }

    private static void KopfZeichnen(DataGridView g, DataGridViewCellPaintingEventArgs e, DataGridViewColumn spalte)
    {
        var r = e.CellBounds;
        using (var br = new SolidBrush(Color.White)) e.Graphics!.FillRectangle(br, r);
        var i = Array.IndexOf(Stundenplanspalten, spalte.Name);
        var textRect = new Rectangle(r.X + 3, r.Y, r.Width - 4, r.Height - 1);
        var text = spalte.HeaderText;
        if (i >= 0 && g.Columns.Contains("S1"))
        {
            // „Stundenplan“ über alle drei Spalten
            var s1 = g.GetColumnDisplayRectangle(g.Columns["S1"]!.Index, false);
            textRect = new Rectangle((s1.Width > 0 ? s1.X : r.X) + 3, r.Y, 200, r.Height - 1);
            text = g.Columns["S1"]!.HeaderText;
            if (s1.Width == 0 && i > 0) text = "";
        }
        var clip = e.Graphics!.Clip;
        e.Graphics.SetClip(r);
        TextRenderer.DrawText(e.Graphics, text, g.ColumnHeadersDefaultCellStyle.Font ?? g.Font, textRect, Color.Black,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        using (var p = new Pen(g.GridColor))
            if (i < 0 || i == Stundenplanspalten.Length - 1) e.Graphics.DrawLine(p, r.Right - 1, r.Y, r.Right - 1, r.Bottom - 1);
        using (var p = new Pen(KopfLinie)) e.Graphics.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        e.Graphics.Clip = clip;
        e.Handled = true;
    }

    // ----------------------------------------------------------------------------------------------
    // Aktionen
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Nach einer Zuweisung aus diesem Fenster („Weiter zur nächsten offenen“) zur nächsten offenen Vertretung springen.
    /// Nur nach eigener Zuweisung – Änderungen im Vertretungsfenster (z. B. ein Text) lassen die Zeile dort stehen.
    /// </summary>
    private void Weiter(bool warOffen)
    {
        if (!warOffen || !_weiter.Checked || _v is null || _v.OffenOderRaumOffen) return;
        BeginInvoke(() => AppZustand.Haupt.MdiChildren.OfType<VertretungenFenster>().FirstOrDefault()?.NaechsteOffene());
    }

    private List<int> Ziele()
    {
        if (_v is null) return new();
        var ids = AppZustand.MarkierteVertretungen.Contains(_v.Id) ? AppZustand.MarkierteVertretungen.ToList() : new List<int> { _v.Id };
        if (Block) ids.AddRange(VertretungsVorschlag.Block(D, _v).Select(x => x.Id));
        if (Dauer && _v.UnterrichtId is int uid)
        {
            // Dauervertretung: alle offenen Stunden desselben Unterrichts aus derselben Absenz ab diesem Tag
            var v = _v;
            ids.AddRange(D.Vertretungen.Where(x => x.UnterrichtId == uid && x.ZeilenIndex == v.ZeilenIndex && x.Datum >= v.Datum && x.Offen &&
                (v.AbwesenheitId is null ? x.LehrerId == v.LehrerId : x.AbwesenheitId == v.AbwesenheitId)).Select(x => x.Id));
        }
        return ids.Distinct().ToList();
    }

    /// <summary>Entfall; bei einer Verlegung zuerst fragen, ob die Verlegung rückgängig gemacht werden soll.</summary>
    private void EntfallMitRueckfrage()
    {
        var ids = Ziele();
        var liste = D.Vertretungen.Where(x => ids.Contains(x.Id)).ToList();
        var rest = VerlegungsAbfrage.VorEntfall(liste, Aendern);
        // Sondereinsatz auf Entfall: Rückfrage, ob er gelöscht werden soll
        var seLoeschen = StundenplanFenster.SondereinsatzLoeschenFragen(rest.Where(v => Vertretungen.IstSondereinsatz(v) && !v.IstEntfall)) == true;
        if (rest.Count == liste.Count && !seLoeschen) { Aktion("Entfall", (d, v) => Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer)); return; }
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        var restIds = rest.Select(v => v.Id).ToHashSet();
        if (restIds.Count > 0)
            Aendern("Entfall", AenderungsArt.Vertretung, d => { foreach (var v in d.Vertretungen.Where(x => restIds.Contains(x.Id)).ToList()) Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer, seLoeschen); });
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
    }

    private void Aktion(string text, Action<Schuldatei, Vertretung> a)
    {
        var ids = Ziele();
        if (ids.Count == 0) return;
        var warOffen = _v?.OffenOderRaumOffen == true;
        Aendern(ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, AenderungsArt.Vertretung, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList()) a(d, v);
        });
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
        Weiter(warOffen);
    }

    private void Uebernehmen()
    {
        var lehrer = _vorschlag.CurrentRow?.Tag switch { Kandidat k => k.Lehrer.Id, int id => id, _ => (int?)null };
        if (lehrer is not int l) return;
        if (Dauer && _v is { } dv)
        {
            // Dauervertretung: Vertreter für dieselbe Stunde in allen Folgewochen der Absenz (Vertretungen werden dazu erzeugt)
            var id = dv.Id;
            var block = Block;
            var uebersprungen = 0;
            var warOffen = dv.OffenOderRaumOffen;
            Aendern($"Dauervertretung {Ix.KLehrer(l)}", AenderungsArt.Vertretung, d =>
                uebersprungen = Vertretungen.Dauervertretung(d, d.Vertretungen.First(x => x.Id == id), l, block, AppZustand.Benutzer).Uebersprungen.Count);
            if (uebersprungen > 0) Ui.Info($"{Ix.KLehrer(l)} ist in {uebersprungen} Stunde(n) der Folgewochen nicht frei – diese bleiben offen.");
            _v = D.Vertretungen.FirstOrDefault(x => x.Id == id);
            Neuaufbau();
            Weiter(warOffen);
            return;
        }
        Aktion($"Vertreter {Ix.KLehrer(l)}", (d, v) => Vertretungen.VertreterSetzen(d, v, l, false, AppZustand.Benutzer));
    }

    private void Betreuung()
    {
        if (_betreuung.CurrentRow?.Tag is not Betreuungskandidat b) return;
        Aktion($"Betreuung {b.Lehrer.Kurzname}", (d, v) => Vertretungen.VertreterSetzen(d, v, b.Lehrer.Id, true, AppZustand.Benutzer));
    }

    private void Raum()
    {
        if (_v is null || _raeume.Aktuell is not { } r) return;
        var loesung = RaumKollisionsLoesung.MitKollision;
        if (r.Belegung.FirstOrDefault() is { Eintrag: not null } b)
        {
            var w = RaumZuordnenVertretungDialog.KollisionFragen(this, Raumvertretungen.KollisionsText(D, r.Raum, _v.Datum, b.Stunde, r.Belegung.Where(x => x.Stunde == b.Stunde).Select(x => x.Eintrag)));
            if (w is null or RaumKollisionsLoesung.NichtVerplanen) return;
            loesung = w.Value;
        }
        var zusatz = _zusatzRaum.Checked;
        Aktion($"Raum {r.Raum.Kurzname}", (d, v) => Raumvertretungen.Zuordnen(d, v, r.Raum.Id, zusatz, loesung, AppZustand.Benutzer));
    }

    /// <summary>Vorziehung übernehmen: die offene Stunde entfällt, der spätere Unterricht wird in sie verlegt.</summary>
    private void Vorziehen()
    {
        if (_v is null || _vorziehung.CurrentRow?.Tag is not Vorziehung vz) return;
        var id = _v.Id;
        var warOffen = _v.OffenOderRaumOffen;
        Aendern("Vorziehung", AenderungsArt.Vertretung, d =>
        {
            var v = d.Vertretungen.First(x => x.Id == id);
            var aktuell = VertretungsVorschlag.Vorziehungen(d, v).FirstOrDefault(x => x.Datum == vz.Datum && x.Stunde == vz.Stunde && x.Eintrag.Unterricht?.Id == vz.Eintrag.Unterricht?.Id)
                          ?? throw new InvalidOperationException("Die Vorziehung ist nicht mehr möglich.");
            VertretungsVorschlag.VorziehungAusfuehren(d, v, aktuell, AppZustand.Benutzer);
        });
        _v = D.Vertretungen.FirstOrDefault(x => x.Id == id);
        Neuaufbau();
        Weiter(warOffen);
    }

    // ----------------------------------------------------------------------------------------------
    // Selbsttest
    // ----------------------------------------------------------------------------------------------

    internal void SelbsttestReiter(int i) => _reiter.Index = i;

    internal void SelbsttestAufklappen(bool kopf, bool vorziehungen)
    {
        _kopfLeiste.Offen = kopf;
        _vorzLeiste.Offen = vorziehungen;
    }

    /// <summary>Selbsttest: Spalten und Werte wie im Vorbild (drei Stundenplanspalten mit genau einer Vertretungsstunde, drei Nachkommastellen).</summary>
    internal string SelbsttestPruefen()
    {
        if (_v is null) return "Vorschlag: keine Vertretung markiert";
        var kandidaten = _vorschlag.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Kandidat>().ToList();
        foreach (var k in kandidaten)
            if (k.Zellen.Count != 3 || k.Zellen.Count(z => z.Ziel) != 1 || k.Zellen.Single(z => z.Ziel).Stunde != _v.Stunde)
                throw new InvalidOperationException($"Stundenplanspalte von {k.Lehrer.Kurzname} hat nicht drei Stunden mit der Vertretungsstunde.");
        foreach (DataGridViewRow r in _vorschlag.Rows)
            foreach (var n in new[] { "Vertr", "Entf" })
                if (r.Cells[n].Value is string s && s.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d+\.\d{3}$"))
                    throw new InvalidOperationException($"Zahlenformat in Spalte {n}: „{s}“ statt 0.000");
        var kopf = string.Join(" | ", _vorschlag.Columns.Cast<DataGridViewColumn>().Take(13).Select(c => c.HeaderText).Where(t => t.Length > 0));
        return $"Vorschlag {_kopfLeiste.Text}: {kandidaten.Count} Vorschläge, {_betreuung.Rows.Count} Betreuungen, {_raeume.Raster.Rows.Count} Räume, {_vorziehung.Rows.Count} Vorziehungen; Spalten {kopf}";
    }

    // ----------------------------------------------------------------------------------------------
    // Steuerelemente
    // ----------------------------------------------------------------------------------------------

    /// <summary>Aufklappbare Leiste wie im Vorbild (weißer Kasten, fetter Text, ▼ zugeklappt / ▲ aufgeklappt; unten umgekehrt).</summary>
    private sealed class Klappleiste : Control
    {
        private readonly bool _unten;
        private bool _offen;

        public Klappleiste(bool unten)
        {
            _unten = unten;
            Dock = unten ? DockStyle.Bottom : DockStyle.Top;
            Height = 24;
            Font = Ui.SchriftFett;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public event Action? Umgeschaltet;

        public bool Offen
        {
            get => _offen;
            set
            {
                if (_offen == value) return;
                _offen = value;
                Invalidate();
                Umgeschaltet?.Invoke();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Offen = !Offen;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Ui.BlauSehrHell);
            var r = _unten ? new Rectangle(0, 2, Width - 1, Height - 3) : new Rectangle(0, 0, Width - 1, Height - 3);
            e.Graphics.FillRectangle(Brushes.White, r);
            using (var p = new Pen(Color.FromArgb(160, 160, 160))) e.Graphics.DrawRectangle(p, r);
            // Pfeil: oben ▲ = aufgeklappt, unten ▲ = zugeklappt (klappt nach oben auf)
            var hoch = _unten ? !_offen : _offen;
            var m = new Point(r.X + 10, r.Y + r.Height / 2);
            var pfeil = hoch
                ? new[] { new Point(m.X - 4, m.Y + 2), new Point(m.X + 4, m.Y + 2), new Point(m.X, m.Y - 2) }
                : new[] { new Point(m.X - 4, m.Y - 2), new Point(m.X + 4, m.Y - 2), new Point(m.X, m.Y + 2) };
            e.Graphics.FillPolygon(Brushes.Black, pfeil);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(r.X + 20, r.Y, r.Width - 22, r.Height), Color.Black,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Reiter wie im Vorbild: mintgrüne Leiste, gewählter Reiter hell und erhöht.</summary>
    private sealed class Reiterleiste : Control
    {
        private string[] _texte = Array.Empty<string>();
        private readonly List<Rectangle> _flaechen = new();
        private int _index;

        public Reiterleiste()
        {
            Height = 24;
            Font = Ui.Schrift;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public event Action? Gewechselt;

        public int Index
        {
            get => _index;
            set
            {
                if (_index == value) return;
                _index = value;
                Invalidate();
                Gewechselt?.Invoke();
            }
        }

        public void Setzen(params string[] texte)
        {
            _texte = texte;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var i = _flaechen.FindIndex(r => r.Contains(e.Location));
            if (i >= 0) Index = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(175, 223, 204));
            _flaechen.Clear();
            var x = 2;
            using var rand = new Pen(Color.FromArgb(150, 150, 150));
            for (var i = 0; i < _texte.Length; i++)
            {
                var w = TextRenderer.MeasureText(_texte[i], Font).Width + 10;
                var gewaehlt = i == _index;
                var r = gewaehlt ? new Rectangle(x, 2, w, Height - 2) : new Rectangle(x, 4, w, Height - 5);
                _flaechen.Add(r);
                using (var br = new SolidBrush(gewaehlt ? Color.FromArgb(246, 251, 249) : Color.FromArgb(229, 239, 235))) g.FillRectangle(br, r);
                g.DrawLine(rand, r.X, r.Bottom, r.X, r.Y);
                g.DrawLine(rand, r.X, r.Y, r.Right, r.Y);
                g.DrawLine(rand, r.Right, r.Y, r.Right, r.Bottom);
                TextRenderer.DrawText(g, _texte[i], Font, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                x += w + 1;
            }
            g.DrawLine(rand, 0, Height - 1, Width, Height - 1);
            if (_index < _flaechen.Count)
                using (var br = new SolidBrush(Color.FromArgb(246, 251, 249))) g.FillRectangle(br, _flaechen[_index].X + 1, Height - 1, _flaechen[_index].Width - 1, 1);
        }
    }
}
