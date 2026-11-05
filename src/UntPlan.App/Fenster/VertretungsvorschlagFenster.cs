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
/// Spalten wie im Vorbild: Name (mit „+“ vor Lehrkräften mit passender Vormerkung), Stundenplan (so viele Stunden, wie Platz ist, die
/// Vertretungsstunde gestrichelt markiert; Breite der Stunden per Ziehen, Rechtsklick wählt Fach, Klasse(n) oder Raum), Merker zur
/// Stunde, Vertr., Entfall, Zähler (negativ grün), Zeitwunsch, Jahres-Zähler, Kennzeichen, reg. Ber. und Dep. Ber. (Bereitschafts-
/// Einsatzzähler „m/n“, rot sobald alle genutzt), Dep. (Werte mit drei Nachkommastellen), Fach (Buchsymbol), Klasse (Schülersymbol,
/// Klassenlehrer grün), Unt./Tag, Vorwoche, Statistik, Sperrvermerk, Vormerkungen; ausgeblendet bez., Betreuungen, Benachrichtigen.
/// Bei Pausenaufsichten eigene Spalten (Punkte, Max. Minuten, PA / Tag, Zähler, Unt./Gang davor/danach …). Spalten ein-/ausblenden und
/// umsortieren per Rechtsklick bzw. Ziehen an der Spaltenüberschrift (Tabellenformate). Vorziehungen mit Auswahl „Art“ und „Nur späteren
/// Unterricht“, zweistufig bzw. für Doppelstunden mit Schritt 1 und Schritt 2.
/// </summary>
public sealed class VertretungsvorschlagFenster : MdiFenster
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Color ZaehlerGelb = Color.FromArgb(252, 252, 129);
    private static readonly Color ZaehlerRot = Color.FromArgb(255, 128, 128);
    /// <summary>Negativer Zähler („Vertretungsschulden“, Handbuch Kap. 7.3.3): grün – vorrangig heranziehen.</summary>
    private static readonly Color ZaehlerGruen = Color.FromArgb(150, 240, 150);
    /// <summary>Bereitschafts-Einsatzzähler: alle verplanten Bereitschaften genutzt.</summary>
    private static readonly Color EinsatzRot = Color.FromArgb(240, 128, 128);
    private static readonly Color MerkerRot = Color.FromArgb(255, 55, 55);
    private static readonly Color Hohlstundengruen = Color.FromArgb(150, 240, 150);
    private static readonly Color Klassenlehrergruen = Color.FromArgb(150, 240, 150);
    private static readonly Color KopfLinie = Color.FromArgb(105, 105, 105);
    /// <summary>Spalte „Stundenplan“ (früher drei Spalten S1–S3; zeigt jetzt so viele Stunden, wie Platz ist).</summary>
    private static readonly string[] Stundenplanspalten = { "S1" };
    /// <summary>Breite einer Stunde in der Spalte „Stundenplan“ (per Ziehen an einer Stundengrenze änderbar).</summary>
    private static int _zellBreite = 44;

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
    /// <summary>Unterer Fensterteil „Vorziehungen“: Auswahl „Art“ und „Nur späteren Unterricht“ über der Liste.</summary>
    private readonly Panel _vorzBereich = new() { Dock = DockStyle.Bottom, Height = 176, Visible = false };
    private readonly Panel _vorzKopf = new() { Dock = DockStyle.Top, Height = 28 };
    private readonly ComboBox _vorzArt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 34, Top = 3, Width = 250 };
    private readonly CheckBox _vorzSpaeter = new() { Text = "Nur späteren Unterricht", AutoSize = true, Left = 296, Top = 5 };
    private readonly DataGridView _vorziehung = new() { Dock = DockStyle.Fill, Name = "Vorziehung" };
    /// <summary>Vorziehungen mit Schritt 1 und Schritt 2 (zweistufig bzw. Doppelstunde, Handbuch Kap. 7.3.7).</summary>
    private readonly DataGridView _vorziehung2 = new() { Dock = DockStyle.Fill, Name = "Vorziehung2", Visible = false };
    /// <summary>Vertretungsvorschlag für eine Pausenaufsicht (eigene Spalten wie im Handbuch Kap. 16.1.1).</summary>
    private readonly DataGridView _pause = new() { Dock = DockStyle.Fill, Name = "Pausenaufsicht", Visible = false };
    /// <summary>Lehrkräfte mit passenden Vormerkungen zur markierten Vertretung („+“ vor dem Namen).</summary>
    private readonly Dictionary<int, List<Vormerkung>> _vorschlagVormerkungen = new();
    /// <summary>Lehrkräfte, deren passende Vormerkungen aufgeklappt sind.</summary>
    private readonly HashSet<int> _vormerkungenOffen = new();
    /// <summary>Bereitschafts-Einsatzzähler des Zeitraums (zwischengespeichert bis zur nächsten Änderung).</summary>
    private (DateOnly Von, DateOnly Bis, Dictionary<int, (BereitschaftsEinsatz Regulaer, BereitschaftsEinsatz Deputat)> Werte)? _einsaetze;
    /// <summary>Anzeige in der Spalte „Stundenplan“ (Rechtsklick): 0 Fach, 1 Klasse(n), 2 Raum.</summary>
    private int _planAnzeige;
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
        ts.Items.Add(Knopf("Einstellungen", "gewichtung", () => { if (VorschlagEinstellungenDialog.Zeigen(this)) { SchriftAnwenden(); Neuaufbau(); } }));
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        foreach (var c in new[] { _block, _disloz, _dauer, _nahe, _besetzt })
            c.CheckedChanged += (_, _) => Neuaufbau();

        _kopfLeiste.Umgeschaltet += () => _kopfDetails.Visible = _kopfLeiste.Offen;
        _optLeiste.Umgeschaltet += () => _optionen.Visible = _optLeiste.Offen;
        // zweistufige Vorziehungen werden erst beim Aufklappen berechnet (je erster Schritt eine Probeberechnung)
        _vorzLeiste.Umgeschaltet += () => { _vorzBereich.Visible = _vorzLeiste.Offen; if (_vorzLeiste.Offen) Neuaufbau(); else VorziehungsHoehe(); };
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
            ("Name", "Name", 72), ("S1", "Stundenplan", StandardPlanBreite), ("Merker", "Merker zur Stunde", 82), ("Vertr", "Vertr.", 48),
            ("Entf", "Entfall", 48), ("Zaehler", "Zähler", 50), ("ZW", "Zeitwunsch", 42), ("Jahr", "Jahres-Zähler", 52), ("Kennz", "Kennzeichen", 42),
            ("RegB", "reg. Ber.", 46), ("DepB", "Dep. Ber.", 46), ("Dep", "Dep.", 58), ("Fach", "Fach", 38), ("Klasse", "Klasse", 44), ("StdTag", "Unt./Tag", 50),
            ("Woche", "Woche", 46), ("Monat", "Monat", 46), ("Vorwoche", "Vorwoche", 56), ("Statistik", "Statistik", 52), ("Sperr", "Sperrvermerk", 70),
            ("Vorm", "Vormerkungen", 90), ("Bez", "bez.", 40), ("Betr", "Betreuungen", 70), ("Ben", "Benachrichtigen", 90), ("Punkte", "Punkte", 46),
            ("Kr", "Kriterien", 220),
        });
        _vorschlag.Columns["RegB"]!.ToolTipText = "Genutzte reguläre Bereitschaften";
        _vorschlag.Columns["DepB"]!.ToolTipText = "Genutzte Deputats-Bereitschaften";
        _vorschlag.Columns["Bez"]!.ToolTipText = "Bezahlte Vertretungen im eingestellten Zeitraum";
        _vorschlag.Columns["Vorm"]!.ToolTipText = "Vormerkungen, mit denen die offene Vertretung gefüllt werden kann („+“ vor dem Namen klappt sie auf, Doppelklick setzt ein)";
        foreach (var n in new[] { "Vertr", "Entf", "Zaehler", "Jahr", "Dep", "Punkte", "StdTag", "Bez", "Betr" }) _vorschlag.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var n in new[] { "Fach", "Klasse", "Woche", "Monat", "RegB", "DepB", "Vorwoche", "Ben" }) _vorschlag.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        // wie im Vorbild standardmäßig ausgeblendet (Rechtsklick auf die Spaltenüberschrift blendet sie ein)
        foreach (var n in new[] { "Bez", "Betr", "Ben" }) _vorschlag.Columns[n]!.Visible = false;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !PlusGetroffen(e.RowIndex, e.ColumnIndex)) Uebernehmen(); };
        // „+“ vor dem Namen: passende Vormerkungen auf- bzw. zuklappen (Handbuch Kap. 12.2.2)
        _vorschlag.CellMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && PlusGetroffen(e.RowIndex, e.ColumnIndex, e.X)) VormerkungenUmschalten(e.RowIndex);
        };
        _vorschlag.CellFormatting += VorschlagFormatieren;
        _vorschlag.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Uebernehmen(); e.Handled = true; } };
        _vorschlag.ContextMenuStrip = Menue(("Vertreter übernehmen (Doppelklick, Enter)", Uebernehmen));

        // --- Betreuungen ---
        RasterEinrichten(_betreuung);
        Spalten(_betreuung, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("S1", "Stundenplan", StandardPlanBreite), ("Raum", "Raum", 46), ("Klasse", "Klasse", 46), ("Fach", "Fach", 46),
            ("Zaehler", "Zähler", 50), ("ZW", "Zeitwunsch", 42), ("Jahr", "Jahres-Zähler", 52), ("Kennz", "Kennzeichen", 42), ("Kopplung", "Kopplungslehrer", 42),
            ("Gang", "Gang", 64), ("Naehe", "Nähe", 110), ("Heute", "Betr. heute", 60),
        });
        _betreuung.Columns["Gang"]!.Visible = false;
        foreach (var n in new[] { "Zaehler", "Jahr" }) _betreuung.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _betreuung.Columns["Heute"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _betreuung.CellFormatting += BetreuungFormatieren;
        _betreuung.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Betreuung(); e.Handled = true; } };
        _betreuung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Betreuung(); };
        _betreuung.ContextMenuStrip = Menue(("Betreuung übernehmen (Doppelklick, Enter)", Betreuung));

        // Spalte „Stundenplan“: Rechtsklick wählt die angezeigte Information (Fach, Klasse(n), Raum)
        foreach (var g in new[] { _vorschlag, _betreuung })
            foreach (var n in Stundenplanspalten)
                g.Columns[n]!.ContextMenuStrip = PlanMenue();

        // --- Räume ---
        _raeume.Gewaehlt += _ => Raum();
        _raeume.Raster.ContextMenuStrip = Menue(("Raum übernehmen (Doppelklick, Enter)", Raum));

        // --- Vorziehungen ---
        RasterEinrichten(_vorziehung);
        Spalten(_vorziehung, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("Tag", "von", 70), ("Merker", "Merker", 50), ("Klasse", "Klasse", 60), ("Fach", "Fach", 56), ("Raum", "Raum", 50), ("Art", "Art", 140),
        });
        // wie im Vorbild: Name, von, Merker; die übrigen Angaben lassen sich per Rechtsklick auf die Spaltenüberschrift einblenden
        foreach (var n in new[] { "Klasse", "Fach", "Raum", "Art" }) _vorziehung.Columns[n]!.Visible = false;
        _vorzArt.Items.AddRange(new object[] { "Vorziehungen gemäß Vertretungszeitraster", "Vorziehungen aus Randstunden anzeigen", "Alle möglichen Verlegungen anzeigen" });
        _vorzArt.SelectedIndex = Math.Clamp((int)D.Vertretungseinstellungen.Vorschlag.Vorziehungen, 0, 2);
        _vorzSpaeter.Checked = D.Vertretungseinstellungen.Vorschlag.NurSpaeterenUnterricht;
        _vorzArt.SelectedIndexChanged += (_, _) => Neuaufbau();
        _vorzSpaeter.CheckedChanged += (_, _) => Neuaufbau();
        _vorzKopf.Controls.AddRange(new Control[] { new Label { Text = "Art:", Left = 4, Top = 6, AutoSize = true }, _vorzArt, _vorzSpaeter });
        _vorzBereich.Controls.Add(_vorziehung);
        _vorzBereich.Controls.Add(_vorzKopf);
        _vorziehung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Vorziehen(); };
        _vorziehung.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Vorziehen(); e.Handled = true; } };
        _vorziehung.ContextMenuStrip = Menue(("Vorziehen: Unterricht in die offene Stunde verlegen (Doppelklick, Enter)", Vorziehen));
        // Schritt 1 / Schritt 2: je Schritt Name, von, Merker (Handbuch Kap. 7.3.7, zweistufige Vorziehungen und Doppelstunden)
        RasterEinrichten(_vorziehung2);
        Spalten(_vorziehung2, new (string, string, int)[]
        {
            ("Name", "Name", 60), ("Tag", "von", 64), ("Merker", "Merker", 46), ("Name2", "Name", 60), ("Tag2", "von", 64), ("Merker2", "Merker", 46),
            ("Klasse", "Klasse", 60), ("Fach", "Fach", 56), ("Raum", "Raum", 50), ("Art", "Art", 140),
        });
        foreach (var n in new[] { "Klasse", "Fach", "Raum", "Art" }) _vorziehung2.Columns[n]!.Visible = false;
        // zweizeilige Überschrift: oben „Schritt 1“ bzw. „Schritt 2“, darunter Name, von, Merker
        _vorziehung2.ColumnHeadersHeight = 2 * _vorziehung2.ColumnHeadersHeight;
        _vorziehung2.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Vorziehen(); };
        _vorziehung2.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Vorziehen(); e.Handled = true; } };
        _vorziehung2.ContextMenuStrip = Menue(("Vorziehen: beide Schritte durchführen (Doppelklick, Enter)", Vorziehen));
        _vorzBereich.Controls.Add(_vorziehung2);
        _vorziehung2.BringToFront();

        // --- Pausenaufsicht (Handbuch Kap. 16.1.1) ---
        RasterEinrichten(_pause);
        Spalten(_pause, new (string, string, int)[]
        {
            ("Name", "Name", 72), ("Punkte", "Punkte", 46), ("MaxMin", "Max. Minuten", 56), ("PaTag", "PA / Tag", 50), ("Zaehler", "Zähler", 46),
            ("UntVor", "Unt. davor", 50), ("UntNach", "Unt. danach", 50), ("GangVor", "Gang davor", 50), ("GangNach", "Gang danach", 50),
            ("Vor1", "Vor 1. Stunde", 56), ("NachL", "nach letzter Stunde", 56), ("Folge", "Aufs. Folge", 50), ("VorSp", "vor Sperrung", 50),
            ("NachSp", "nach Sperrung", 50), ("HohlVor", "Hohlstunde davor", 56), ("HohlNach", "Hohlstunde danach", 56), ("Geschl", "Geschlecht", 50),
            ("Ber", "Bereitschaft", 50), ("VplMin", "Verplante Minuten", 60),
        });
        foreach (DataGridViewColumn c in _pause.Columns)
            if (c.Name != "Name") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        // wie im Handbuch: die übrigen Angaben per Rechtsklick in die Überschriften einblenden
        foreach (var n in new[] { "GangVor", "GangNach", "Vor1", "NachL", "Folge", "VorSp", "NachSp", "HohlVor", "HohlNach", "Geschl", "Ber" }) _pause.Columns[n]!.Visible = false;
        _pause.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Uebernehmen(); };
        _pause.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Uebernehmen(); e.Handled = true; } };
        _pause.ContextMenuStrip = Menue(("Vertreter übernehmen (Doppelklick, Enter)", Uebernehmen));

        // Auswahl durch den Benutzer (Klick bzw. Pfeiltasten) schaltet die verknüpften Stundenpläne auf das gewählte Element
        foreach (var g in new[] { _vorschlag, _betreuung, _raeume.Raster, _vorziehung, _vorziehung2, _pause })
        {
            var quelle = g;
            g.CellMouseClick += (_, e) => { if (e.RowIndex >= 0 && e.Button == MouseButtons.Left) PlaeneFolgen(quelle); };
            g.KeyUp += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End) PlaeneFolgen(quelle); };
        }
        _planTimer.Tick += (_, _) => { _planTimer.Stop(); if (!IsDisposed) PlaeneZeigen(); };
        FormClosed += (_, _) => _planTimer.Dispose();

        _reiter.Gewechselt += ReiterGewechselt;
        _inhalt.Controls.Add(_vorschlag);
        _inhalt.Controls.Add(_betreuung);
        _inhalt.Controls.Add(_raeume);
        _inhalt.Controls.Add(_pause);
        StundenplanZiehenEinrichten(_vorschlag);
        StundenplanZiehenEinrichten(_betreuung);
        LayoutUmstellen();

        var flaeche = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 4), BackColor = Ui.BlauSehrHell };
        // Andockreihenfolge: zuletzt hinzugefügte Steuerelemente liegen außen
        flaeche.Controls.Add(_inhalt);
        flaeche.Controls.Add(_reiter);
        flaeche.Controls.Add(_vorzLeiste);
        flaeche.Controls.Add(_vorzBereich);
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
        SchriftAnwenden();
        Neuaufbau();
    }

    /// <summary>Kontextmenü der Spalte „Stundenplan“: Fach / Klasse(n) / Raum.</summary>
    private ContextMenuStrip PlanMenue()
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var texte = new[] { "Fach", "Klasse(n)", "Raum" };
        for (var i = 0; i < texte.Length; i++)
        {
            var art = i;
            cm.Items.Add(new ToolStripMenuItem(texte[i], null, (_, _) =>
            {
                _planAnzeige = art;
                _vorschlag.Invalidate();
                _betreuung.Invalidate();
            }));
        }
        cm.Opening += (_, _) => { for (var i = 0; i < cm.Items.Count; i++) ((ToolStripMenuItem)cm.Items[i]).Checked = i == _planAnzeige; };
        return cm;
    }

    /// <summary>Text einer Zelle der Spalte „Stundenplan“ je nach gewählter Anzeige (Fach, Klasse(n), Raum).</summary>
    private string PlanText(StundenplanZelle z) => _planAnzeige switch
    {
        1 when z.Klassen.Length > 0 => z.Klassen,
        2 when z.Raum.Length > 0 || z.Text.Length > 0 => z.Raum,
        _ => z.Text,
    };

    /// <summary>Schrift aus den Einstellungen („Schrift“) auf die Tabellen anwenden.</summary>
    private void SchriftAnwenden()
    {
        var schrift = VorschlagEinstellungenDialog.Schrift(D.Vertretungseinstellungen.Vorschlag.Schrift) ?? Ui.Schrift;
        foreach (var g in new[] { _vorschlag, _betreuung, _vorziehung, _vorziehung2, _pause, _raeume.Raster })
        {
            g.DefaultCellStyle.Font = schrift;
            g.RowTemplate.Height = Math.Max(21, schrift.Height + 6);
        }
    }

    /// <summary>Höhe des Vorziehungsbereichs: fest bzw. („Höhe automatisch anpassen“) nach der Anzahl der Vorziehungen bis zur eingestellten Höhe.</summary>
    private void VorziehungsHoehe()
    {
        var e = D.Vertretungseinstellungen.Vorschlag;
        var max = Math.Max(60, e.VorziehungenFensterhoehe);
        var vg = VorziehungsRaster;
        var inhalt = _vorzKopf.Height + vg.ColumnHeadersHeight + Math.Max(3, vg.Rows.Count) * vg.RowTemplate.Height + 6;
        var hoehe = e.VorziehungenHoeheAutomatisch ? Math.Min(inhalt, max) : max;
        // höchstens die halbe Fensterhöhe, damit die Vorschläge sichtbar bleiben
        _vorzBereich.Height = Math.Max(60, Math.Min(hoehe, Math.Max(60, ClientSize.Height / 2)));
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        _einsaetze = null;
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
        RasterZeigen();
        (_reiter.Index switch { 0 => (Control)VorschlagsRaster, 1 => _betreuung, _ => _raeume.Raster }).Focus();
    }

    /// <summary>Tabellen des gewählten Reiters zeigen (bei Pausenaufsichten die Aufsichtstabelle) – ohne den Fokus zu übernehmen.</summary>
    private void RasterZeigen()
    {
        var i = _reiter.Index;
        var pause = _v?.Art == VertretungsArt.Pausenaufsicht;
        _vorschlag.Visible = i == 0 && !pause;
        _pause.Visible = i == 0 && pause;
        _betreuung.Visible = i == 1;
        _raeume.Visible = i == 2;
        _nahe.Visible = i == 1;
        _besetzt.Visible = _zusatzRaum.Visible = i == 2;
    }

    /// <summary>Tabelle des Reiters „Vertretungsvorschläge“: bei Pausenaufsichten die eigene Tabelle mit den Aufsichtsspalten.</summary>
    private DataGridView VorschlagsRaster => _v?.Art == VertretungsArt.Pausenaufsicht ? _pause : _vorschlag;

    /// <summary>Angezeigte Vorziehungsliste: mit Schritt 1 und Schritt 2 bzw. einstufig.</summary>
    private DataGridView VorziehungsRaster => _zweiSchritte ? _vorziehung2 : _vorziehung;

    /// <summary>Die Vorziehungsliste enthält Vorziehungen mit Schritt 2 (Tabelle mit Schritt 1 und Schritt 2).</summary>
    private bool _zweiSchritte;

    /// <summary>Verzögerung beim Blättern mit den Pfeiltasten: die Pläne werden erst nach dem Anhalten umgeschaltet (kein Flackern).</summary>
    private readonly System.Windows.Forms.Timer _planTimer = new() { Interval = 160 };
    private DataGridView? _planQuelle;

    private void PlaeneFolgen(DataGridView quelle)
    {
        _planQuelle = quelle;
        _planTimer.Stop();
        _planTimer.Start();
    }

    /// <summary>
    /// Gewähltes Element des Vorschlags in den verknüpften Plänen zeigen (Woche der Vertretung, Stunde markiert): Lehrkraft
    /// (Vorschläge, Betreuungen, Vorziehungen), Raum (Reiter Räume) und bei Vorziehungen bzw. Betreuungen auch die Klasse.
    /// </summary>
    internal void PlaeneZeigen()
    {
        if (_v is not { } v || _planQuelle is not { } g || g.CurrentRow?.Tag is not { } tag) return;
        switch (tag)
        {
            case Kandidat k: StundenplanFenster.VorschlagAnzeigen(ElementTyp.Lehrer, k.Lehrer.Id, v); break;
            case int lehrer when g == _vorschlag || g == _pause: StundenplanFenster.VorschlagAnzeigen(ElementTyp.Lehrer, lehrer, v); break;
            case VormerkungsZeile vz: StundenplanFenster.VorschlagAnzeigen(ElementTyp.Lehrer, vz.Kandidat.Lehrer.Id, v); break;
            case Betreuungskandidat b: StundenplanFenster.VorschlagAnzeigen(ElementTyp.Lehrer, b.Lehrer.Id, v); break;
            case RaumMoeglichkeit r: StundenplanFenster.VorschlagAnzeigen(ElementTyp.Raum, r.Raum.Id, v); break;
            case Vorziehung vz:
                if (vz.Eintrag.LehrerIds.Count > 0) StundenplanFenster.VorschlagAnzeigen(ElementTyp.Lehrer, vz.Eintrag.LehrerIds[0], v);
                if (vz.Eintrag.KlassenIds.Count > 0) StundenplanFenster.VorschlagAnzeigen(ElementTyp.Klasse, vz.Eintrag.KlassenIds[0], v);
                break;
        }
    }

    private void Gewaehlt(Vertretung? v)
    {
        if (IsDisposed) return;
        _v = v is null ? null : D.Vertretungen.FirstOrDefault(x => x.Id == v.Id) ?? v;
        Neuaufbau();
        // Vorbild: ohne „Fenster im Hintergrund“ kommt der Vertretungsvorschlag beim Wählen einer Vertretung nach vorn (ohne den
        // Fokus zu übernehmen); nicht bei maximierten Fenstern, sonst verdeckte er das Vertretungsfenster ganz
        if (v != null && !D.Vertretungseinstellungen.Vorschlag.FensterImHintergrund && Visible && WindowState == FormWindowState.Normal &&
            IsHandleCreated && MdiParent?.ActiveMdiChild is { } aktiv && aktiv != this && aktiv.WindowState != FormWindowState.Maximized)
        {
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
            try { SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr nach, int x, int y, int cx, int cy, uint flags);

    /// <summary>Kopf wie im Vorbild: „30.8. Mo-1 Se/Mu/05D“ (Datum, Tag-Stunde, Lehrer/Fach/Klasse).</summary>
    internal static string KopfText(DatenIndex ix, Vertretung v)
    {
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var teile = new[] { ix.KLehrer(v.LehrerId), v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KFach(v.FachId), ix.KKlassen(v.NeueKlassenIds) };
        return $"{v.Datum.Day}.{v.Datum.Month}. {tag}-{Vertretungsplan.StundeText(v)} {string.Join("/", teile.Where(t => t.Length > 0))}";
    }

    private void Neuaufbau()
    {
        using var _m = Core.Messung.Start("Vorschlag.Neuaufbau");
        using var _ix = DatenIndex.Gemeinsam(D, Ix);
        var tabellen = new[] { _vorschlag, _vorziehung, _vorziehung2, _betreuung, _pause };
        foreach (var g in tabellen) g.SuspendLayout();
        try { NeuaufbauIntern(); }
        finally { foreach (var g in tabellen) g.ResumeLayout(); }
    }

    private void NeuaufbauIntern()
    {
        _vorschlag.Rows.Clear();
        _vorziehung.Rows.Clear();
        _vorziehung2.Rows.Clear();
        _betreuung.Rows.Clear();
        _pause.Rows.Clear();
        _vorschlagVormerkungen.Clear();
        _raeume.Setzen(Array.Empty<RaumMoeglichkeit>());
        var v = _v;
        RasterZeigen();
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
            PausenVorschlag(v);
            VorziehungsHoehe();
            Zaehlerstaende();
            return;
        }
        var langname = D.Vertretungseinstellungen.Vorschlag.Langname;
        var markiert = MarkierteStunden(v);
        // Zeilen vorbereiten und in einem Rutsch einfügen (einzeln eingefügte Zeilen lösen je Zeile Layout und Neuzeichnen aus)
        var neueZeilen = new List<DataGridViewRow>();
        var kennzSpalte = _vorschlag.Columns["Kennz"]!.Index;
        var nameSpalte = _vorschlag.Columns["Name"]!.Index;
        var regSpalte = _vorschlag.Columns["RegB"]!.Index;
        var depSpalte = _vorschlag.Columns["DepB"]!.Index;
        List<Kandidat> kandidaten;
        using (Core.Messung.Start("Vorschlag: Berechnen")) kandidaten = VertretungsVorschlag.Berechnen(D, v, Block, _alle.Checked, _disloz.Enabled && _disloz.Checked, markiert);
        // wie im Vorbild: hat die Lehrkraft dieselbe Stunde in der Vorwoche vertreten, grün mit Haken in „Vorwoche“
        var vorwoche = v.UnterrichtId is int vuid
            ? D.Vertretungen.Where(x => x.Datum == v.Datum.AddDays(-7) && x.Stunde == v.Stunde && x.UnterrichtId == vuid && x.ZeilenIndex == v.ZeilenIndex && x.VertreterId is int)
                .Select(x => x.VertreterId!.Value).ToHashSet()
            : new HashSet<int>();
        var einsaetze = Einsaetze(v.Datum);
        // Vormerkungen, mit denen die offene Vertretung gefüllt werden könnte (Handbuch Kap. 12.2.2)
        if (v.Offen && D.Vormerkungen.Any(x => x.Offen > 0))
        {
            var tpV = Tagesplan.Berechnen(D, v.Datum);
            foreach (var k in kandidaten)
                if (VertretungsVorschlag.PassendeVormerkungen(D, v, k.Lehrer.Id, tpV) is { Count: > 0 } vms) _vorschlagVormerkungen[k.Lehrer.Id] = vms;
        }
        foreach (var k in kandidaten)
        {
            var z = k.Zellen;
            var zeile = new DataGridViewRow();
            var (reg, dep) = einsaetze.TryGetValue(k.Lehrer.Id, out var ein) ? ein : (new BereitschaftsEinsatz(0, 0), new BereitschaftsEinsatz(0, 0));
            var vms = _vorschlagVormerkungen.GetValueOrDefault(k.Lehrer.Id);
            zeile.CreateCells(_vorschlag, Werte(_vorschlag, ("Name", langname ? k.Lehrer.VollerName : k.Lehrer.Kurzname), ("S1", string.Join(" ", z.Select(x => x.Text))),
                ("Merker", k.Merker), ("Vertr", Zahl(k.VertretungenWert)), ("Entf", Zahl(k.EntfaelleWert)), ("Zaehler", ZahlLeer(k.Zaehler)),
                ("ZW", Zeitwunsch(k.Zeitwunsch)), ("Jahr", ZahlLeer(k.Jahreszaehler)), ("Kennz", k.Kennzeichen ? "K" : ""),
                ("RegB", reg.Text), ("DepB", dep.Text), ("Dep", k.Deputat is decimal dp ? ZahlLeer(dp) : ""),
                ("Fach", k.Fachlehrer ? "✔" : ""), ("Klasse", k.Klassenlehrer ? "KL" : k.KenntKlasse ? "✔" : ""), ("StdTag", k.UnterrichtAmTag), ("Woche", k.ZaehlerWoche),
                ("Monat", k.ZaehlerMonat), ("Vorwoche", vorwoche.Contains(k.Lehrer.Id) ? "✔" : ""), ("Statistik", k.Lehrer.Statistik ?? ""),
                ("Sperr", k.Lehrer.Sperrvermerk > 0 ? k.Lehrer.Sperrvermerk.ToString() : ""),
                ("Vorm", vms is null ? "" : string.Join(", ", vms.Select(x => $"{Ix.KFach(x.FachId)} {Ix.KKlassen(x.KlassenIds)}".Trim()))),
                ("Bez", k.Bezahlt > 0 ? k.Bezahlt.ToString() : ""),
                ("Betr", k.Betreuungen > 0 ? k.Betreuungen.ToString() : ""), ("Ben", k.Benachrichtigen ? "✔" : ""), ("Punkte", ((int)k.Punkte).ToString()),
                ("Kr", string.Join(", ", k.Kriterien))));
            zeile.Tag = k;
            if (k.Kennzeichen) zeile.Cells[kennzSpalte].ToolTipText = k.KennzeichenText;
            zeile.Cells[nameSpalte].ToolTipText = k.Lehrer.VollerName + (k.Lage.Length > 0 ? " – " + k.Lage : "") + (vms is null ? "" : $"{Environment.NewLine}+ {vms.Count} Vormerkung(en) – Klick auf das Plus zeigt sie");
            if (reg.Verplant > 0) zeile.Cells[regSpalte].ToolTipText = $"Genutzte reguläre Bereitschaften: {reg.Genutzt} von {reg.Verplant}";
            if (dep.Verplant > 0) zeile.Cells[depSpalte].ToolTipText = $"Genutzte Deputats-Bereitschaften: {dep.Genutzt} von {dep.Verplant}";
            neueZeilen.Add(zeile);
            if (vms != null && _vormerkungenOffen.Contains(k.Lehrer.Id)) neueZeilen.AddRange(VormerkungsZeilen(k, vms));
        }
        using (Core.Messung.Start("Vorschlag: Zeilen einfügen"))
            _vorschlag.Rows.AddRange(neueZeilen.ToArray());
        VorziehungenAufbauen(v);
        var betreuungen = new List<DataGridViewRow>();
        var bKennz = _betreuung.Columns["Kennz"]!.Index;
        var raumReihe = D.Raeume.Select((r, i) => (r.Kurzname, i)).GroupBy(x => x.Kurzname).ToDictionary(g => g.Key, g => g.First().i);
        var eigenerRaum = Ix.KRaum(v.VertretungsraumId ?? v.RaumId);
        foreach (var b in VertretungsVorschlag.Betreuungen(D, v, markiert).Where(b => !_nahe.Checked || b.Naehe <= 2))
        {
            var z = b.Zellen;
            var zeile = new DataGridViewRow();
            // Gang (Flur) des Betreuers; ohne Gänge die Differenz der Eingabereihenfolge der beiden Räume, gekoppelte Lehrkräfte „Kopplung“
            var gang = b.Kopplung ? "Kopplung" : b.Gang.Length > 0 ? b.Gang
                : raumReihe.TryGetValue(b.Raum, out var r1) && raumReihe.TryGetValue(eigenerRaum, out var r2) ? Math.Abs(r1 - r2).ToString() : "";
            zeile.CreateCells(_betreuung, Werte(_betreuung, ("Name", langname ? b.Lehrer.VollerName : b.Lehrer.Kurzname), ("S1", string.Join(" ", z.Select(x => x.Text))),
                ("Raum", b.Raum), ("Klasse", b.Klassen), ("Fach", b.Fach), ("Zaehler", ZahlLeer(b.Zaehler)), ("ZW", Zeitwunsch(b.Zeitwunsch)),
                ("Jahr", ZahlLeer(b.Jahreszaehler)), ("Kennz", b.Kennzeichen ? "K" : ""), ("Kopplung", b.Kopplung ? "✔" : ""), ("Gang", gang), ("Naehe", b.NaeheText),
                ("Heute", b.BetreuungenHeute > 0 ? b.BetreuungenHeute.ToString() : "")));
            zeile.Tag = b;
            if (b.Kennzeichen) zeile.Cells[bKennz].ToolTipText = b.KennzeichenText;
            betreuungen.Add(zeile);
        }
        _betreuung.Rows.AddRange(betreuungen.ToArray());
        _raeume.Langname = langname;
        _raeume.Setzen(Raumvertretungen.MoeglicheRaeume(D, v, _besetzt.Checked, Block));
        foreach (var g in new[] { _vorschlag, _betreuung, _vorziehung, _vorziehung2 })
            if (g.Rows.Count > 0) g.CurrentCell = g.Rows[0].Cells[0];
        Zaehlerstaende();
    }

    /// <summary>Bereitschafts-Einsatzzähler im eingestellten Zeitraum (je Änderung einmal berechnet).</summary>
    private Dictionary<int, (BereitschaftsEinsatz Regulaer, BereitschaftsEinsatz Deputat)> Einsaetze(DateOnly datum)
    {
        var (von, bis) = Zaehlung.Zeitraum(D, datum);
        if (_einsaetze is { } e && e.Von == von && e.Bis == bis) return e.Werte;
        Dictionary<int, (BereitschaftsEinsatz, BereitschaftsEinsatz)> werte;
        using (Core.Messung.Start("Vorschlag: Bereitschafts-Einsatzzähler")) werte = Zaehlung.BereitschaftsEinsaetze(D, von, bis);
        _einsaetze = (von, bis, werte);
        return werte;
    }

    /// <summary>
    /// Vorziehungen: einstufig wie bisher; aufgeklappt zusätzlich zweistufig (Schritt 1 hinterlässt eine Lücke, Schritt 2 füllt sie) und
    /// mit „Stundenblock“ für Doppelstunden (Schritt 1 = 1. Stunde, Schritt 2 = 2. Stunde) – dann mit den Spalten Schritt 1 und Schritt 2.
    /// </summary>
    private void VorziehungenAufbauen(Vertretung v)
    {
        var liste = v.Offen ? VorziehungenBerechnen(D, v) : new List<Vorziehung>();
        var zweistufig = liste.Any(x => x.Schritt2 != null);
        _zweiSchritte = zweistufig;
        _vorziehung.Visible = !zweistufig;
        _vorziehung2.Visible = zweistufig;
        var g = VorziehungsRaster;
        var tpZiel = Tagesplan.Berechnen(D, v.Datum);
        string Merker(Vorziehung vz, Tagesplan tp, int stunde) => vz.Eintrag.LehrerIds.Count > 0 ? Vertretungen.Merker(D, tp, vz.Eintrag.LehrerIds[0], stunde) : "";
        foreach (var vz in liste)
        {
            var werte = new List<(string, object?)>
            {
                ("Name", vz.Lehrer), ("Tag", $"{vz.Datum.Day}.{vz.Datum.Month}. / {vz.Stunde}"), ("Merker", Merker(vz, tpZiel, v.Stunde)),
                ("Klasse", vz.Klassen), ("Fach", vz.Fach), ("Raum", vz.Raum),
                ("Art", vz.Schritt2 is { } x2 ? (vz.Schritt2VertretungId.HasValue ? "Doppelstunde" : "zweistufig") : vz.Randstunde ? "Randstunde" : "erzeugt neue Lücke"),
            };
            if (zweistufig && vz.Schritt2 is { } s2)
            {
                // Schritt 2 füllt die zweite Stunde des Blocks bzw. die Lücke, die Schritt 1 hinterlässt
                var (datum2, stunde2) = vz.Schritt2VertretungId is int id2 && D.Vertretungen.FirstOrDefault(x => x.Id == id2) is { } v2 ? (v2.Datum, v2.Stunde) : (vz.Datum, vz.Stunde);
                werte.Add(("Name2", s2.Lehrer));
                werte.Add(("Tag2", $"{s2.Datum.Day}.{s2.Datum.Month}. / {s2.Stunde}"));
                werte.Add(("Merker2", Merker(s2, datum2 == v.Datum ? tpZiel : Tagesplan.Berechnen(D, datum2), stunde2)));
            }
            var r = g.Rows.Add(Werte(g, werte.ToArray()));
            g.Rows[r].Tag = vz;
            foreach (DataGridViewCell c in g.Rows[r].Cells) c.ToolTipText = vz.Beschreibung;
        }
        VorziehungsHoehe();
    }

    /// <summary>
    /// Vorziehungen laut Auswahl „Art“ und „Nur späteren Unterricht“: bei „Stundenblock“ für die Doppelstunde, sonst einstufig und – im
    /// aufgeklappten Bereich – zweistufig.
    /// </summary>
    private List<Vorziehung> VorziehungenBerechnen(Schuldatei d, Vertretung v)
    {
        var (modus, spaeter) = ((VorziehungsModus)Math.Max(0, _vorzArt.SelectedIndex), _vorzSpaeter.Checked);
        if (Block && VertretungsVorschlag.Block(d, v).Count(x => x.Offen) >= 2)
            return VertretungsVorschlag.DoppelstundenVorziehungen(d, v, modus, spaeter);
        return VertretungsVorschlag.Vorziehungen(d, v, modus, spaeter, zweistufig: _vorzLeiste.Offen);
    }

    /// <summary>Vertretungsvorschlag einer Pausenaufsicht mit den Spalten des Handbuchs (Kap. 16.1.1).</summary>
    private void PausenVorschlag(Vertretung v)
    {
        var tag = Kalender.TagIndex(D, v.Datum);
        var a = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = tag, NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
        var (zv, zb) = Zaehlung.Zeitraum(D, v.Datum);
        var tp = Tagesplan.Berechnen(D, v.Datum);
        var gehalten = D.Vertretungen.Where(x => x.Art == VertretungsArt.Pausenaufsicht && x.VertreterId is int && x.VertreterId != x.LehrerId && x.Datum >= zv && x.Datum <= zb)
            .GroupBy(x => x.VertreterId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var langname = D.Vertretungseinstellungen.Vorschlag.Langname;
        string Ja(bool b) => b ? "✔" : "";
        var zeilen = new List<DataGridViewRow>();
        foreach (var l in Core.Module.Pausenaufsichten.Kandidaten(D, v.Datum, a))
        {
            var b = Core.Module.Pausenaufsichten.Bewerten(D, Ix, l, a.GangId, tag, a.NachStunde);
            var bereit = Vertretungen.HatBereitschaft(D, tp, l.Id, v.Stunde) || Vertretungen.HatBereitschaft(D, tp, l.Id, v.Stunde + 1);
            var zeile = new DataGridViewRow();
            zeile.CreateCells(_pause, Werte(_pause, ("Name", langname ? l.VollerName : l.Kurzname), ("Punkte", ((int)Math.Round(b.Punkte)).ToString()),
                ("MaxMin", b.MaxMinuten > 0 ? b.MaxMinuten.ToString() : ""), ("PaTag", b.ProTag > 0 ? b.ProTag.ToString() : ""),
                ("Zaehler", gehalten.GetValueOrDefault(l.Id) is int n and > 0 ? n.ToString() : ""), ("UntVor", Ja(b.UnterrichtDavor)), ("UntNach", Ja(b.UnterrichtDanach)),
                ("GangVor", Ja(b.GangDavor)), ("GangNach", Ja(b.GangDanach)), ("Vor1", Ja(b.VorErsterStunde)), ("NachL", Ja(b.NachLetzterStunde)), ("Folge", Ja(b.Folge)),
                ("VorSp", Ja(b.VorSperrung)), ("NachSp", Ja(b.NachSperrung)), ("HohlVor", Ja(b.HohlstundeDavor)), ("HohlNach", Ja(b.HohlstundeDanach)),
                ("Geschl", l.Geschlecht is "w" or "W" ? "♀" : l.Geschlecht is "m" or "M" ? "♂" : ""), ("Ber", Ja(bereit)), ("VplMin", b.Minuten > 0 ? b.Minuten.ToString() : "")));
            zeile.Tag = l.Id;
            zeile.Cells[0].ToolTipText = l.VollerName + (b.Verletzt.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, b.Verletzt) : "");
            zeilen.Add(zeile);
        }
        _pause.Rows.AddRange(zeilen.ToArray());
        if (_pause.Rows.Count > 0) _pause.CurrentCell = _pause.Rows[0].Cells[0];
    }

    /// <summary>Unterzeile im Vorschlag: eine Vormerkung, mit der die Lehrkraft die offene Vertretung füllen kann.</summary>
    private sealed record VormerkungsZeile(Kandidat Kandidat, Vormerkung Vormerkung);

    private List<DataGridViewRow> VormerkungsZeilen(Kandidat k, List<Vormerkung> vms)
    {
        var res = new List<DataGridViewRow>();
        foreach (var vm in vms)
        {
            var zeile = new DataGridViewRow();
            var text = VertretungsVorschlag.VormerkungText(Ix, vm);
            zeile.CreateCells(_vorschlag, Werte(_vorschlag, ("S1", text), ("Vorm", text), ("Kr", vm.Text)));
            zeile.Tag = new VormerkungsZeile(k, vm);
            foreach (DataGridViewCell c in zeile.Cells) c.ToolTipText = text + " – Doppelklick setzt die Lehrkraft mit der vorgemerkten Stunde ein";
            res.Add(zeile);
        }
        return res;
    }

    /// <summary>Klick auf das „+“ vor dem Namen getroffen (Lehrkraft mit passenden Vormerkungen)?</summary>
    private bool PlusGetroffen(int zeile, int spalte, int x = 0) =>
        zeile >= 0 && spalte >= 0 && _vorschlag.Columns[spalte].Name == "Name" && x < 15 && _vorschlag.Rows[zeile].Tag is Kandidat k && _vorschlagVormerkungen.ContainsKey(k.Lehrer.Id) &&
        (x > 0 || _vorschlag.PointToClient(Cursor.Position).X - _vorschlag.GetCellDisplayRectangle(spalte, zeile, false).X < 15);

    /// <summary>Passende Vormerkungen unter der Lehrkraft auf- bzw. zuklappen.</summary>
    private void VormerkungenUmschalten(int zeile)
    {
        if (_vorschlag.Rows[zeile].Tag is not Kandidat k || !_vorschlagVormerkungen.TryGetValue(k.Lehrer.Id, out var vms)) return;
        if (_vormerkungenOffen.Remove(k.Lehrer.Id))
        {
            while (zeile + 1 < _vorschlag.Rows.Count && _vorschlag.Rows[zeile + 1].Tag is VormerkungsZeile) _vorschlag.Rows.RemoveAt(zeile + 1);
        }
        else
        {
            _vormerkungenOffen.Add(k.Lehrer.Id);
            var neu = VormerkungsZeilen(k, vms);
            for (var i = 0; i < neu.Count; i++) _vorschlag.Rows.Insert(zeile + 1 + i, neu[i]);
        }
        _vorschlag.InvalidateRow(zeile);
    }

    /// <summary>
    /// Stunden aller mit der aktuellen Vertretung markierten Vertretungen desselben Tages (z. B. zwei markierte Zeilen im
    /// Vertretungsfenster): sie werden in der Spalte „Stundenplan“ und in den verknüpften Plänen gemeinsam umrandet.
    /// </summary>
    internal List<int> MarkierteStunden(Vertretung v) =>
        StundenplanFenster.MarkierteVertretungen(v).Where(x => x.Datum == v.Datum).Select(x => x.Stunde).Distinct().OrderBy(x => x).ToList();

    private bool Block => _block.Enabled && _block.Checked;

    private bool Dauer => _dauer.Enabled && _dauer.Checked;

    private void Zaehlerstaende()
    {
        // Unterzeilen aufgeklappter Vormerkungen zählen nicht als Vorschlag
        var n1 = VorschlagsRaster.Rows.Cast<DataGridViewRow>().Count(r => r.Tag is not VormerkungsZeile);
        var (n2, n3) = (_betreuung.Rows.Count, _raeume.Raster.Rows.Count);
        _reiter.Setzen($"Vertretungsvorschläge ({n1})", $"Betreuungen ({n2})", $"Räume ({n3})");
        _optLeiste.Text = $"Vertretungsvorschläge ({n1}),Betreuungen ({n2}),Räume ({n3})";
        _vorzLeiste.Text = $"Vorziehungen ({VorziehungsRaster.Rows.Count})";
    }

    /// <summary>Standardbreite der Spalte „Stundenplan“: drei Stunden.</summary>
    private const int StandardPlanBreite = 3 * 44 + 2;

    /// <summary>
    /// Gemerkte Spaltenanordnung älterer Versionen (drei Spalten S1–S3 für den Stundenplan) einmalig auf die eine Spalte „Stundenplan“
    /// umstellen: ihre Breite ist die Summe der drei Breiten.
    /// </summary>
    private static void LayoutUmstellen()
    {
        var geaendert = false;
        foreach (var schluessel in new[] { $"{nameof(VertretungsvorschlagFenster)}/Vorschlag", $"{nameof(VertretungsvorschlagFenster)}/Betreuung" })
        {
            if (!AppZustand.Einstellungen.Tabellenlayouts.TryGetValue(schluessel, out var l) || !l.Any(x => x.Name is "S2" or "S3")) continue;
            if (l.FirstOrDefault(x => x.Name == "S1") is { } s1)
                s1.Breite = Math.Max(StandardPlanBreite, l.Where(x => x.Name is "S1" or "S2" or "S3").Sum(x => x.Breite));
            l.RemoveAll(x => x.Name is "S2" or "S3");
            geaendert = true;
        }
        if (geaendert) AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Anzahl der Stunden, die in die Spalte „Stundenplan“ passen (Handbuch Kap. 7.3.2: so viele, wie Platz ist).</summary>
    private static int PlanAnzahl(int spaltenBreite) => Math.Max(1, (spaltenBreite - 2) / Math.Max(12, _zellBreite));

    /// <summary>Angezeigte Stunden einer Zeile in der Spalte „Stundenplan“ (Ausschnitt aus allen Stunden des Tages).</summary>
    private List<StundenplanZelle>? PlanZellen(DataGridView g, int zeile)
    {
        var tag = g.Rows[zeile].Tag switch { Kandidat k => k.TagesZellen, Betreuungskandidat b => b.TagesZellen, _ => null };
        if (tag is null || tag.Count == 0 || !g.Columns.Contains("S1")) return null;
        return VertretungsVorschlag.StundenplanFenster(tag, _v?.Stunde ?? tag[0].Stunde, PlanAnzahl(g.Columns["S1"]!.Width));
    }

    /// <summary>
    /// Breite der einzelnen Stunden in der Spalte „Stundenplan“ per Ziehen an einer Stundengrenze ändern (Handbuch Kap. 7.3.2);
    /// die Breite der Spalte selbst ändert sich wie gewohnt an der Spaltenüberschrift.
    /// </summary>
    private void StundenplanZiehenEinrichten(DataGridView g)
    {
        int? ziehenAb = null;
        var breiteAb = 0;
        bool AnGrenze(MouseEventArgs e, out Rectangle zelle)
        {
            zelle = Rectangle.Empty;
            var hit = g.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || g.Columns[hit.ColumnIndex].Name != "S1") return false;
            zelle = g.GetCellDisplayRectangle(hit.ColumnIndex, hit.RowIndex, false);
            var dx = e.X - zelle.X - 1;
            var w = Math.Max(12, _zellBreite);
            var n = PlanAnzahl(g.Columns["S1"]!.Width);
            var naechste = (int)Math.Round(dx / (double)w);
            return naechste >= 1 && naechste <= n && Math.Abs(dx - naechste * w) <= 2;
        }
        g.MouseMove += (_, e) =>
        {
            if (ziehenAb is int ab)
            {
                var anzahl = Math.Max(1, (int)Math.Round((ab - g.GetColumnDisplayRectangle(g.Columns["S1"]!.Index, false).X) / (double)Math.Max(12, breiteAb)));
                _zellBreite = Math.Clamp(breiteAb + (e.X - ab) / anzahl, 16, 160);
                foreach (var t in new[] { _vorschlag, _betreuung }) t.InvalidateColumn(t.Columns["S1"]!.Index);
                return;
            }
            if (e.Button == MouseButtons.None) g.Cursor = AnGrenze(e, out Rectangle _) ? Cursors.VSplit : Cursors.Default;
        };
        g.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !AnGrenze(e, out Rectangle _)) return;
            ziehenAb = e.X;
            breiteAb = Math.Max(12, _zellBreite);
            g.Capture = true;
        };
        g.MouseUp += (_, _) => { if (ziehenAb.HasValue) { ziehenAb = null; g.Capture = false; g.Cursor = Cursors.Default; } };
        g.MouseLeave += (_, _) => { if (ziehenAb is null) g.Cursor = Cursors.Default; };
    }

    /// <summary>Zellwerte einer Zeile in der Reihenfolge der Spalten (nicht genannte Spalten bleiben leer).</summary>
    private static object?[] Werte(DataGridView g, params (string Spalte, object? Wert)[] werte)
    {
        var res = new object?[g.Columns.Count];
        for (var i = 0; i < res.Length; i++) res[i] = "";
        foreach (var (spalte, wert) in werte) res[g.Columns[spalte]!.Index] = wert;
        return res;
    }

    /// <summary>Zahlenformat wie im Vorbild: drei Nachkommastellen mit Punkt (0.000, -1.000).</summary>
    internal static string Zahl(decimal w) => w.ToString("0.000", Inv);

    internal static string ZahlLeer(decimal w) => w == 0 ? "" : Zahl(w);

    internal static string Zeitwunsch(int w) => w == 0 ? "" : w.ToString("+0;-0", Inv);

    /// <summary>
    /// Merker zur Stunde wie im Vorbild (Handbuch Kap. 7.3.1): 0 (Hohlstunde) grün, 1–3 weiß, 4–8 zunehmend rosa, 9 rot;
    /// x (freigesetzt) und Bereitschaft (Fachkürzel) ohne Farbe.
    /// </summary>
    internal static Color? MerkerFarbe(string merker)
    {
        if (merker.Length == 0 || merker == "x") return null;
        if (!int.TryParse(merker, out var m)) return null;
        if (m == 0) return Hohlstundengruen;
        if (m >= 9) return MerkerRot;
        if (m < 4) return null;
        var g = 217 - (m - 4) * 17;
        return Color.FromArgb(255, g, g);
    }

    // ----------------------------------------------------------------------------------------------
    // Darstellung
    // ----------------------------------------------------------------------------------------------

    /// <summary>Zähler wie im Handbuch (Kap. 7.3.3): negativ („Vertretungsschulden“) grün, positiv rot, sonst gelb.</summary>
    internal static Color ZaehlerFarbe(decimal w) => w < 0 ? ZaehlerGruen : w > 0 ? ZaehlerRot : ZaehlerGelb;

    private void VorschlagFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null) return;
        var name = _vorschlag.Columns[e.ColumnIndex].Name;
        var tag = _vorschlag.Rows[e.RowIndex].Tag;
        if (tag is VormerkungsZeile)
        {
            // aufgeklappte Vormerkung: Infozeile unter der Lehrkraft
            e.CellStyle.BackColor = Color.FromArgb(250, 250, 236);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.FromArgb(60, 60, 60);
            if (name is "S1" or "Vorm") e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            return;
        }
        if (name == "Zaehler") e.CellStyle.BackColor = ZaehlerFarbe(tag is Kandidat kz ? kz.Zaehler : 0m);
        if (tag is not Kandidat ka) return;
        if (name == "Name")
        {
            if (ka.Rot) e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.Firebrick;
            if (ka.Benachrichtigen) e.CellStyle.Font = Ui.SchriftFett;
            // Platz für das „+“ der Vormerkungen
            if (_vorschlagVormerkungen.ContainsKey(ka.Lehrer.Id)) e.CellStyle.Padding = new Padding(13, 0, 0, 0);
        }
        if (name == "Merker" && MerkerFarbe(ka.Merker) is { } mf) e.CellStyle.BackColor = mf;
        // Klassenlehrer: grüne Hintergrundmarkierung in der Spalte „Klasse“
        if (name == "Klasse" && ka.Klassenlehrer) e.CellStyle.BackColor = Klassenlehrergruen;
        // Bereitschafts-Einsatzzähler: rot, sobald alle verplanten Bereitschaften genutzt sind (Handbuch Kap. 16)
        if (name is "RegB" or "DepB" && _einsaetze?.Werte.TryGetValue(ka.Lehrer.Id, out var ein) == true && (name == "RegB" ? ein.Regulaer : ein.Deputat).Erreicht)
            e.CellStyle.BackColor = EinsatzRot;
    }

    private void BetreuungFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null) return;
        var name = _betreuung.Columns[e.ColumnIndex].Name;
        if (name == "Zaehler" && _betreuung.Rows[e.RowIndex].Tag is Betreuungskandidat b) e.CellStyle.BackColor = ZaehlerFarbe(b.Zaehler);
        // mit der abwesenden Lehrkraft gekoppelte Lehrkräfte: grün mit dem Wort „Kopplung“
        if (name == "Gang" && _betreuung.Rows[e.RowIndex].Tag is Betreuungskandidat { Kopplung: true }) e.CellStyle.BackColor = Klassenlehrergruen;
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
        if (i >= 0 && PlanZellen(g, e.RowIndex) is { } z)
        {
            // Spalte „Stundenplan“: so viele Stunden, wie in die Spalte passen, je Stunde in der Fachfarbe
            Ui.ZelleOhneInhalt(e);
            var r = e.CellBounds;
            var w = Math.Max(12, _zellBreite);
            var clip = e.Graphics.Clip;
            e.Graphics.SetClip(new Rectangle(r.X, r.Y, r.Width - 1, r.Height));
            using var linie = new Pen(g.GridColor);
            for (var k = 0; k < z.Count; k++)
            {
                var zelle = z[k];
                var zr = new Rectangle(r.X + 1 + k * w, r.Y, w, r.Height - 1);
                var hg = selected ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
                if (zelle.Hintergrund != 0 && !zelle.Entfallen)
                {
                    hg = Ui.Farbe(zelle.Hintergrund, Color.White);
                    using var br = new SolidBrush(hg);
                    e.Graphics.FillRectangle(br, zr);
                }
                var vg = zelle.Entfallen ? Color.Red : zelle.Vordergrund != 0 ? Ui.Farbe(zelle.Vordergrund, Color.Black) : Ui.Textfarbe(hg);
                var text = new Rectangle(zr.X + 1, zr.Y, zr.Width - 2, zr.Height);
                var inhalt = PlanText(zelle);
                TextRenderer.DrawText(e.Graphics, inhalt, e.CellStyle.Font ?? g.Font, text, vg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (zelle.Entfallen && inhalt.Length > 0)
                {
                    var breite = Math.Min(text.Width - 4, TextRenderer.MeasureText(inhalt, e.CellStyle.Font ?? g.Font).Width);
                    var y = zr.Y + zr.Height / 2;
                    using var p = new Pen(Color.Red, 1);
                    e.Graphics.DrawLine(p, zr.X + (zr.Width - breite) / 2, y, zr.X + (zr.Width + breite) / 2, y);
                }
                if (k < z.Count - 1) e.Graphics.DrawLine(linie, zr.Right - 1, zr.Y, zr.Right - 1, zr.Bottom);
                if (zelle.Ziel) ZielMarkieren(e.Graphics, zr, e.RowIndex == 0, e.RowIndex == g.Rows.Count - 1);
            }
            e.Graphics.Clip = clip;
            e.Handled = true;
            return;
        }
        // „+“ vor dem Namen: Lehrkraft kann die Vertretung mit einer Vormerkung füllen (Handbuch Kap. 12.2.2)
        if (g == _vorschlag && spalte.Name == "Name" && g.Rows[e.RowIndex].Tag is Kandidat kp && _vorschlagVormerkungen.ContainsKey(kp.Lehrer.Id))
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            PlusZeichnen(e.Graphics, new Rectangle(e.CellBounds.X + 2, e.CellBounds.Y + (e.CellBounds.Height - 9) / 2, 9, 9), _vormerkungenOffen.Contains(kp.Lehrer.Id));
            e.Handled = true;
            return;
        }
        if (g == _vorschlag && spalte.Name == "Name" && g.Rows[e.RowIndex].Tag is VormerkungsZeile)
        {
            // Baumlinie wie im Handbuch (aufgeklappte Informationszeilen)
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            using var p = new Pen(Color.Gray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            var x = e.CellBounds.X + 6;
            e.Graphics.DrawLine(p, x, e.CellBounds.Y, x, e.CellBounds.Y + e.CellBounds.Height / 2);
            e.Graphics.DrawLine(p, x, e.CellBounds.Y + e.CellBounds.Height / 2, x + 8, e.CellBounds.Y + e.CellBounds.Height / 2);
            e.Handled = true;
            return;
        }
        // Pausenaufsicht: Geschlecht als Symbol
        if (g == _pause && spalte.Name == "Geschl" && e.Value is string gs && gs.Length > 0)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            TextRenderer.DrawText(e.Graphics, gs, Ui.SchriftFett, e.CellBounds, gs == "♀" ? Color.FromArgb(200, 40, 120) : Color.FromArgb(30, 80, 200),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.Handled = true;
            return;
        }
        // Kennzeichen und (Vorschlag) Fach: Buchsymbol – „kann Fachvertretungen halten“
        if ((spalte.Name == "Kennz" && e.Value as string == "K") || (g == _vorschlag && spalte.Name == "Fach" && e.Value as string == "✔"))
        {
            Ui.ZelleOhneInhalt(e);
            BuchZeichnen(e.Graphics, e.CellBounds);
            e.Handled = true;
            return;
        }
        // Vorschlag, Spalte Klasse: Schülersymbol – „kennt die Schüler“ (Klassenlehrer zusätzlich grün hinterlegt)
        if (g == _vorschlag && spalte.Name == "Klasse" && e.Value is "✔" or "KL")
        {
            Ui.ZelleOhneInhalt(e);
            SchuelerZeichnen(e.Graphics, e.CellBounds);
            e.Handled = true;
            return;
        }
        if (e.Value as string == "✔")
        {
            if (spalte.Name == "Vorwoche") e.CellStyle.BackColor = Color.FromArgb(198, 239, 190);
            Ui.ZellHaken(e, Color.FromArgb(60, 170, 60));
        }
    }

    /// <summary>Kästchen mit „+“ (zugeklappt) bzw. „−“ (aufgeklappt).</summary>
    private static void PlusZeichnen(Graphics g, Rectangle r, bool offen)
    {
        g.FillRectangle(Brushes.White, r);
        using var p = new Pen(Color.FromArgb(120, 120, 120));
        g.DrawRectangle(p, r);
        using var s = new Pen(Color.Black);
        var m = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        g.DrawLine(s, r.X + 2, m.Y, r.Right - 2, m.Y);
        if (!offen) g.DrawLine(s, m.X, r.Y + 2, m.X, r.Bottom - 2);
    }

    /// <summary>Blaues Buchsymbol (Kennzeichen, Fach).</summary>
    private static void BuchZeichnen(Graphics g, Rectangle r)
    {
        var b = new Rectangle(r.X + (r.Width - 10) / 2, r.Y + (r.Height - 13) / 2, 10, 13);
        using (var br = new SolidBrush(Color.FromArgb(40, 90, 180))) g.FillRectangle(br, b);
        using (var br = new SolidBrush(Color.FromArgb(120, 170, 235))) g.FillRectangle(br, b.X + 1, b.Y + 1, 2, b.Height - 2);
        using (var p = new Pen(Color.FromArgb(20, 50, 110))) g.DrawRectangle(p, b);
        using (var p = new Pen(Color.White)) g.DrawLine(p, b.X + 4, b.Y + 3, b.X + 8, b.Y + 3);
    }

    /// <summary>Schülersymbol (zwei Köpfe mit Oberkörper) für „unterrichtet die Klasse“.</summary>
    private static void SchuelerZeichnen(Graphics g, Rectangle r)
    {
        var x = r.X + (r.Width - 16) / 2;
        var y = r.Y + (r.Height - 13) / 2;
        var alt = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var rand = new Pen(Color.FromArgb(90, 60, 20));
        foreach (var (dx, koerper) in new[] { (0, Color.FromArgb(200, 120, 40)), (7, Color.FromArgb(230, 170, 60)) })
        {
            using var haut = new SolidBrush(Color.FromArgb(250, 210, 160));
            using var kb = new SolidBrush(koerper);
            g.FillPie(kb, x + dx, y + 6, 9, 12, 180, 180);
            g.DrawPie(rand, x + dx, y + 6, 9, 12, 180, 180);
            g.FillEllipse(haut, x + dx + 2, y + 1, 5, 5);
            g.DrawEllipse(rand, x + dx + 2, y + 1, 5, 5);
        }
        g.SmoothingMode = alt;
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
        if (g.Name == "Vorziehung2")
        {
            // zweizeilig: oben „Schritt 1“ über Name/von/Merker, „Schritt 2“ über den Spalten des zweiten Schritts
            var halb = r.Height / 2;
            var gruppe = spalte.Name switch { "Name" => "Schritt 1", "Name2" => "Schritt 2", _ => "" };
            if (gruppe.Length > 0)
                TextRenderer.DrawText(e.Graphics, gruppe, g.ColumnHeadersDefaultCellStyle.Font ?? g.Font, new Rectangle(r.X + 3, r.Y, r.Width - 4, halb), Color.Black,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (spalte.Name is "Merker" or "Merker2")
                using (var pg = new Pen(g.GridColor)) e.Graphics.DrawLine(pg, r.Right - 1, r.Y, r.Right - 1, r.Y + halb);
            using (var pg = new Pen(g.GridColor)) e.Graphics.DrawLine(pg, r.X, r.Y + halb, r.Right, r.Y + halb);
            textRect = new Rectangle(textRect.X, r.Y + halb, textRect.Width, r.Height - halb - 1);
        }
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
        if (VorschlagsRaster.CurrentRow?.Tag is VormerkungsZeile vz)
        {
            VormerkungEinsetzen(vz);
            return;
        }
        var lehrer = VorschlagsRaster.CurrentRow?.Tag switch { Kandidat k => k.Lehrer.Id, int id => id, _ => (int?)null };
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
        if (_betreuung.CurrentRow?.Tag is not Betreuungskandidat b || _v is not { } v0) return;
        // Vorbild: Rückfrage, wie die Aktion zu Ende geführt werden soll – „Betreuung“ ist vorausgewählt
        var l = b.Lehrer.Id;
        var eintrag = Stundenaenderung.LehrerKonflikt(D, v0.Datum, v0.Stunde, l, v0.UnterrichtId);
        var raumUebernehmen = false;
        var betreuung = true;
        if (eintrag != null)
        {
            var w = BetreuungFragen(Stundenaenderung.KonfliktText(D, l, v0.Datum, v0.Stunde, eintrag), out raumUebernehmen);
            if (w is null or 0) return;
            betreuung = w == 1;
        }
        var raum = raumUebernehmen ? eintrag?.RaumIds.FirstOrDefault() : null;
        Aktion(betreuung ? $"Betreuung {b.Lehrer.Kurzname}" : $"Vertreter {b.Lehrer.Kurzname}", (d, v) =>
        {
            Vertretungen.VertreterSetzen(d, v, l, betreuung, AppZustand.Benutzer);
            if (raum is int r && r != 0) v.VertretungsraumId = r;
        });
    }

    /// <summary>
    /// Frage wie im Vorbild: „Lehrer … hat am … in der … Stunde bereits Unterricht“, Optionen (Lehrer nicht verplanen / Lehrer hält sowohl
    /// Unterricht als auch Vertretung (Betreuung) / Lehrer im regulären Unterricht vertreten lassen) und „Raum des Betreuungslehrers übernehmen“.
    /// </summary>
    /// <returns>0 nicht verplanen, 1 Betreuung, 2 vertreten lassen; null = abgebrochen.</returns>
    private int? BetreuungFragen(string text, out bool raumUebernehmen)
    {
        raumUebernehmen = false;
        var optionen = new[] { "Lehrer nicht verplanen", "Lehrer hält sowohl Unterricht als auch Vertretung (Betreuung)", "Lehrer im regulären Unterricht vertreten lassen (legt Vertretung an)" };
        using var d = new Dialog("Frage", 520, 290);
        d.Controls.Add(new TextBox { Text = text.Replace("\n", Environment.NewLine), Left = 12, Top = 10, Width = 496, Height = 100, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = d.BackColor });
        var grp = new GroupBox { Text = "Optionen", Left = 12, Top = 116, Width = 496, Height = 28 + optionen.Length * 24 };
        var knoepfe = optionen.Select((o, i) => new RadioButton { Text = o, Left = 12, Top = 20 + i * 24, Width = 476, Checked = i == 1 }).ToList();
        grp.Controls.AddRange(knoepfe.ToArray<Control>());
        var raum = new CheckBox { Text = "Raum des Betreuungslehrers übernehmen", Left = 12, Top = grp.Bottom + 8, AutoSize = true, Checked = true };
        knoepfe[1].CheckedChanged += (_, _) => raum.Enabled = knoepfe[1].Checked;
        d.Controls.AddRange(new Control[] { grp, raum });
        d.Abbrechen.Visible = false;
        d.Ok.Left = d.Abbrechen.Left;
        if (d.ShowDialog(this) != DialogResult.OK) return null;
        var wahl = knoepfe.FindIndex(k => k.Checked);
        raumUebernehmen = wahl == 1 && raum.Checked;
        return wahl;
    }

    private void Raum()
    {
        if (_v is null || _raeume.Aktuell is not { } r) return;
        if (r.Gesperrt && !Ui.Frage($"Der Raum {r.Raum.Kurzname} ist am {Format.Datum(_v.Datum)} in der {_v.Stunde}. Stunde gesperrt.\n\nTrotzdem zuordnen?", "UntPlan - Raum gesperrt")) return;
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

    /// <summary>Lehrkraft mit ihrer vorgemerkten Stunde in die offene Vertretung einsetzen (Doppelklick auf die Vormerkung, Handbuch Kap. 12.2.2).</summary>
    private void VormerkungEinsetzen(VormerkungsZeile vz)
    {
        if (_v is not { } v0) return;
        var (id, vmId) = (v0.Id, vz.Vormerkung.Id);
        var warOffen = v0.OffenOderRaumOffen;
        Aendern($"Vormerkung {vmId} verplant ({vz.Kandidat.Lehrer.Kurzname})", AenderungsArt.Vertretung, d =>
        {
            var v = d.Vertretungen.First(x => x.Id == id);
            var vm = d.Vormerkungen.FirstOrDefault(x => x.Id == vmId) ?? throw new InvalidOperationException("Die Vormerkung gibt es nicht mehr.");
            VertretungsVorschlag.VormerkungEinsetzen(d, v, vm, AppZustand.Benutzer);
        });
        _vormerkungenOffen.Remove(vz.Kandidat.Lehrer.Id);
        _v = D.Vertretungen.FirstOrDefault(x => x.Id == id);
        Neuaufbau();
        Weiter(warOffen);
    }

    /// <summary>Vorziehung übernehmen: die offene Stunde entfällt, der spätere Unterricht wird in sie verlegt (bei Schritt 2 beide Schritte).</summary>
    private void Vorziehen()
    {
        if (_v is null || VorziehungsRaster.CurrentRow?.Tag is not Vorziehung vz) return;
        var id = _v.Id;
        var warOffen = _v.OffenOderRaumOffen;
        var schluessel = vz.Schluessel;
        Aendern(vz.Schritt2 is null ? "Vorziehung" : "Vorziehung (Schritt 1 und 2)", AenderungsArt.Vertretung, d =>
        {
            var v = d.Vertretungen.First(x => x.Id == id);
            var aktuell = VorziehungenBerechnen(d, v).FirstOrDefault(x => x.Schluessel == schluessel)
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
    /// <summary>
    /// Selbsttest: Auswahl eines Vorschlags schaltet den verknüpften Lehrerplan auf die vorgeschlagene Lehrkraft (Woche der
    /// Vertretung). Wirft bei einer Abweichung.
    /// </summary>
    internal string SelbsttestPlaeneFolgen()
    {
        if (_v is null) return "Vorschlag: keine Vertretung markiert";
        _reiter.Index = 0;
        ReiterGewechselt();
        var zeile = _vorschlag.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Kandidat).Skip(1).FirstOrDefault()
                    ?? _vorschlag.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Kandidat);
        if (zeile?.Tag is not Kandidat k) return "Vorschlag: keine Kandidaten";
        _vorschlag.CurrentCell = zeile.Cells["Name"];
        _planQuelle = _vorschlag;
        PlaeneZeigen();
        var plan = AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().FirstOrDefault(f => f.Typ == ElementTyp.Lehrer)
                   ?? throw new InvalidOperationException("Kein Lehrerplan geöffnet.");
        if (plan.ElementId != k.Lehrer.Id) throw new InvalidOperationException($"Lehrerplan zeigt {Ix.KLehrer(plan.ElementId)} statt des gewählten Vorschlags {k.Lehrer.Kurzname}.");
        return $"Vorschlag {k.Lehrer.Kurzname} gewählt – Lehrerplan folgt (Vertretung {_v.Id}, {Format.Datum(_v.Datum)} {_v.Stunde}. Std.)";
    }

    /// <summary>Selbsttest: mehrere markierte Vertretungen – alle Stunden in der Spalte „Stundenplan“ und im verknüpften Lehrerplan umrandet.</summary>
    internal string SelbsttestMehrfachMarkiert()
    {
        if (_v is null) return "Vorschlag: keine Vertretung markiert";
        var stunden = MarkierteStunden(_v);
        if (stunden.Count < 2) throw new InvalidOperationException($"Nur {stunden.Count} markierte Stunde(n) im Vorschlag angekommen.");
        var info = SelbsttestPlaeneFolgen();
        if (_vorschlag.CurrentRow?.Tag is not Kandidat k) throw new InvalidOperationException("Kein Vorschlag gewählt.");
        var sichtbar = stunden.Where(s => k.Zellen.Any(z => z.Stunde == s)).ToList();
        var umrandet = k.Zellen.Where(z => z.Ziel).Select(z => z.Stunde).ToList();
        if (!sichtbar.All(umrandet.Contains) || umrandet.Count < Math.Min(2, sichtbar.Count))
            throw new InvalidOperationException($"Spalte „Stundenplan“ umrandet {string.Join(",", umrandet)} statt {string.Join(",", sichtbar)}.");
        var plan = AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().First(f => f.Typ == ElementTyp.Lehrer);
        var n = plan.Plan.UmrandeteStunden();
        if (n < stunden.Count) throw new InvalidOperationException($"Lehrerplan umrandet {n} statt {stunden.Count} Stunden ({string.Join(",", stunden)}).");
        return $"{info}; markiert {string.Join(",", stunden)}. Std. – Stundenplanspalte umrandet {string.Join(",", umrandet)}, Lehrerplan {n} Stunden";
    }

    internal string SelbsttestPruefen()
    {
        if (_v is null) return "Vorschlag: keine Vertretung markiert";
        var kandidaten = _vorschlag.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Kandidat>().ToList();
        foreach (var k in kandidaten)
            if (k.Zellen.Count != 3 || k.Zellen.Count(z => z.Ziel) != 1 || k.Zellen.Single(z => z.Ziel).Stunde != _v.Stunde)
                throw new InvalidOperationException($"Stundenplanspalte von {k.Lehrer.Kurzname} hat nicht drei Stunden mit der Vertretungsstunde.");
        // Spalte „Stundenplan“: so viele Stunden, wie Platz ist – die Vertretungsstunde ist immer dabei
        var anzahl = PlanAnzahl(_vorschlag.Columns["S1"]!.Width);
        foreach (DataGridViewRow r in _vorschlag.Rows)
            if (r.Tag is Kandidat kp && PlanZellen(_vorschlag, r.Index) is { } pz && (pz.Count != Math.Min(anzahl, kp.TagesZellen.Count) || !pz.Any(z => z.Ziel && z.Stunde == _v.Stunde)))
                throw new InvalidOperationException($"Stundenplanspalte von {kp.Lehrer.Kurzname} zeigt {pz.Count} statt {anzahl} Stunden bzw. nicht die Vertretungsstunde.");
        foreach (DataGridViewRow r in _vorschlag.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Kandidat))
            foreach (var n in new[] { "Vertr", "Entf" })
                if (r.Cells[n].Value is string s && s.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d+\.\d{3}$"))
                    throw new InvalidOperationException($"Zahlenformat in Spalte {n}: „{s}“ statt 0.000");
        var kopf = string.Join(" | ", _vorschlag.Columns.Cast<DataGridViewColumn>().Take(13).Select(c => c.HeaderText).Where(t => t.Length > 0));
        return $"Vorschlag {_kopfLeiste.Text}: {kandidaten.Count} Vorschläge, {_betreuung.Rows.Count} Betreuungen, {_raeume.Raster.Rows.Count} Räume, {VorziehungsRaster.Rows.Count} Vorziehungen; " +
               $"Stundenplan {anzahl} Stunden; Spalten {kopf}";
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
