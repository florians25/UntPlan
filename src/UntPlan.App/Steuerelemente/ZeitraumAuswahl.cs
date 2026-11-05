using UntPlan.Core.Planung;

namespace UntPlan.App.Steuerelemente;

public enum Zeitraumart { Tag, Woche, VonBis }

/// <summary>
/// Kopfbereich „Von-Bis“ für Listen des Vertretungsmodus:
/// <list type="bullet">
/// <item>Datum mit Wochentag.</item>
/// <item>◄ ► blättert um einen Tag, eine Woche bzw. die Länge des Zeitraums.</item>
/// <item>Die Schaltfläche mit Menü wählt die Zeitraumart Tag, Woche oder Von-Bis.</item>
/// <item>Optional (Vertretungsfenster) friert das Schloss neben dem ersten Datum bei „Von-Bis“ den Tages-Abstand ein: er bleibt bei einer
/// Datumsänderung erhalten (Handbuch Vertretungsplanung Kap. 13.1.1.4).</item>
/// </list>
/// Arbeitet mit zwei vorhandenen Datumsfeldern (Von, Bis); das Bis-Feld ist nur bei „Von-Bis“ sichtbar.
/// </summary>
public sealed class ZeitraumAuswahl : FlowLayoutPanel
{
    private readonly DateTimePicker _von;
    private readonly DateTimePicker _bis;
    private readonly Label _wochentag = new() { AutoSize = true, MinimumSize = new Size(34, 16), Padding = new Padding(2, 1, 0, 0) };
    private readonly Label _wochentagBis = new() { AutoSize = true, MinimumSize = new Size(34, 16), Padding = new Padding(2, 1, 0, 0) };
    private readonly Label _strich = new() { Text = "", AutoSize = true, Width = 0, Visible = false };
    private Control? _bisZeile;
    private readonly ToolStripDropDownButton _art = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Zeitraum: Tag, Woche oder Von-Bis" };
    private readonly string _schluessel;
    private bool _setzen;
    /// <summary>Schloss „Tages-Abstand einfrieren“ (nur mit <c>schloss: true</c>).</summary>
    private readonly CheckBox? _schloss;
    /// <summary>Eingefrorener Abstand Bis − Von in Tagen.</summary>
    private int _abstand;

    /// <summary>Tages-Abstand bei „Von-Bis“ eingefroren (Schloss).</summary>
    public bool Gesperrt => _schloss is { Checked: true } && Art == Zeitraumart.VonBis;

    public event Action? Geaendert;

    public Zeitraumart Art { get; private set; }

    public ZeitraumAuswahl(DateTimePicker von, DateTimePicker bis, string schluessel, bool schloss = false)
    {
        _von = von;
        _bis = bis;
        _schluessel = schluessel;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(0);
        Padding = new Padding(0);
        BackColor = Color.Transparent;
        _von.Margin = _bis.Margin = new Padding(2, 2, 8, 0);
        // Breite nach der tatsächlichen Schrift: Datum mit Wochentag passt auch bei größerer Windows-Schrift vollständig hinein
        var muster = _von.Format == DateTimePickerFormat.Custom ? new DateTime(2026, 11, 25).ToString(_von.CustomFormat) : new DateTime(2026, 11, 25).ToShortDateString();
        var noetig = Math.Max(TextRenderer.MeasureText(muster, _von.Font).Width, TextRenderer.MeasureText(muster, Ui.Schrift).Width) + SystemInformation.VerticalScrollBarWidth + 14;
        _von.Width = Math.Max(_von.Width, noetig);
        _bis.Width = Math.Max(_bis.Width, noetig);
        // wie im Vorbild: unter jedem Datum der Wochentag mit zwei kleinen Pfeilen (◂ ▸)
        PfeilKnopf Klein(PfeilKnopf.Art art, string hinweis) => new(art, hinweis) { Size = new Size(16, 15), Margin = new Padding(0, 1, 0, 0) };
        var zurueck = Klein(PfeilKnopf.Art.Links, "Zurück");
        var vor = Klein(PfeilKnopf.Art.Rechts, "Vor");
        zurueck.Click += (_, _) => Blaettern(-1);
        vor.Click += (_, _) => Blaettern(+1);
        var bisZurueck = Klein(PfeilKnopf.Art.Links, "Bis-Datum zurück");
        var bisVor = Klein(PfeilKnopf.Art.Rechts, "Bis-Datum vor");
        bisZurueck.Click += (_, _) => BisVerschieben(-1);
        bisVor.Click += (_, _) => BisVerschieben(+1);
        var kalender = new PfeilKnopf(PfeilKnopf.Art.Kalender, "Zeitraum: Tag, Woche oder Von-Bis") { Margin = new Padding(6, 2, 0, 0) };
        foreach (var a in Enum.GetValues<Zeitraumart>())
        {
            var art = a;
            var it = new ToolStripMenuItem(a switch { Zeitraumart.Tag => "Tag", Zeitraumart.Woche => "Woche", _ => "Von-Bis" });
            it.Click += (_, _) => ArtSetzen(art);
            _art.DropDownItems.Add(it);
        }
        var menue = new ContextMenuStrip { Font = Ui.Schrift };
        kalender.Click += (_, _) =>
        {
            menue.Items.Clear();
            foreach (ToolStripMenuItem it in _art.DropDownItems.Cast<ToolStripMenuItem>().ToList())
            {
                var art = (Zeitraumart)_art.DropDownItems.IndexOf(it);
                menue.Items.Add(new ToolStripMenuItem(it.Text, null, (_, _) => ArtSetzen(art)) { Checked = art == Art });
            }
            menue.Show(kalender, new Point(0, kalender.Height));
        };
        Panel Spalte(DateTimePicker feld, Label tag, Control z, Control v)
        {
            var unten = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(2, 0, 0, 0), Padding = Padding.Empty, BackColor = Color.Transparent };
            unten.Controls.AddRange(new Control[] { tag, z, v });
            var sp = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            sp.Controls.Add(feld);
            sp.Controls.Add(unten);
            return sp;
        }
        _bisZeile = Spalte(_bis, _wochentagBis, bisZurueck, bisVor);
        Controls.Add(Spalte(_von, _wochentag, zurueck, vor));
        if (schloss)
        {
            _schloss = new CheckBox
            {
                Appearance = Appearance.Button, Image = Symbole.Get("schloss", 16), Size = new Size(22, 22), Margin = new Padding(0, 2, 6, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, Text = "", Visible = false,
            };
            _schloss.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
            _schloss.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 226, 150);
            new ToolTip().SetToolTip(_schloss, "Tages-Abstand einfrieren: bleibt bei einer Datumsänderung erhalten");
            if (AppZustand.Einstellungen.Zeitraumarten.TryGetValue(schluessel + ".Schloss", out var ab) && int.TryParse(ab, out var tage) && tage >= 0)
            {
                _abstand = tage;
                _schloss.Checked = true;
            }
            _schloss.CheckedChanged += (_, _) =>
            {
                _abstand = Math.Max(0, (int)(_bis.Value.Date - _von.Value.Date).TotalDays);
                if (_schloss.Checked) AppZustand.Einstellungen.Zeitraumarten[_schluessel + ".Schloss"] = _abstand.ToString();
                else AppZustand.Einstellungen.Zeitraumarten.Remove(_schluessel + ".Schloss");
                AppZustand.Einstellungen.Speichern();
            };
            Controls.Add(_schloss);
        }
        Controls.AddRange(new Control[] { _bisZeile, kalender });
        _von.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(true); };
        _bis.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(false); };
        var gespeichert = AppZustand.Einstellungen.Zeitraumarten.TryGetValue(schluessel, out var g) && Enum.TryParse<Zeitraumart>(g, out var z) ? z : Zeitraumart.Tag;
        ArtSetzen(gespeichert, false);
    }

    public void ArtSetzen(Zeitraumart art, bool merken = true)
    {
        Art = art;
        foreach (ToolStripMenuItem it in _art.DropDownItems) it.Checked = _art.DropDownItems.IndexOf(it) == (int)art;
        if (_bisZeile != null) _bisZeile.Visible = art != Zeitraumart.Tag;
        if (_schloss != null) _schloss.Visible = art == Zeitraumart.VonBis;
        _bis.Visible = art != Zeitraumart.Tag;
        _bis.Enabled = art == Zeitraumart.VonBis;
        if (merken)
        {
            AppZustand.Einstellungen.Zeitraumarten[_schluessel] = art.ToString();
            AppZustand.Einstellungen.Speichern();
        }
        Abgleichen(true);
    }

    /// <summary>
    /// Auf ein Datum stellen (z. B. Tageswechsel im Kalender): Von = Datum, Bis = Datum – bei eingefrorenem Tages-Abstand (Schloss)
    /// Datum plus Abstand, bei „Woche“ die Woche des Datums.
    /// </summary>
    public void DatumSetzen(DateTime datum)
    {
        _setzen = true;
        _von.Value = datum.Date;
        if (!Gesperrt || _bis.Value.Date < datum.Date) _bis.Value = datum.Date;
        _setzen = false;
        Abgleichen(true);
    }

    private void Blaettern(int richtung)
    {
        var tage = Art switch { Zeitraumart.Tag => 1, Zeitraumart.Woche => 7, _ => (int)(_bis.Value.Date - _von.Value.Date).TotalDays + 1 };
        _setzen = true;
        var von = _von.Value.Date.AddDays(richtung * tage);
        // im Tagesmodus Wochenenden überspringen
        if (Art == Zeitraumart.Tag)
            while (von.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) von = von.AddDays(richtung);
        _von.Value = von;
        _bis.Value = _bis.Value.Date.AddDays(richtung * tage) < von ? von : _bis.Value.Date.AddDays(richtung * tage);
        _setzen = false;
        Abgleichen(true);
    }

    /// <summary>Pfeile unter dem Bis-Datum: nur das Ende verschieben (Von-Bis); sonst wie die Pfeile unter Von.</summary>
    private void BisVerschieben(int richtung)
    {
        if (Art != Zeitraumart.VonBis) { Blaettern(richtung); return; }
        _setzen = true;
        var bis = _bis.Value.Date.AddDays(richtung);
        if (bis < _von.Value.Date) bis = _von.Value.Date;
        _bis.Value = bis;
        _setzen = false;
        Abgleichen(false);
    }

    /// <summary>Von/Bis zur Zeitraumart passend machen und melden.</summary>
    private void Abgleichen(bool vonGeaendert)
    {
        _setzen = true;
        var von = _von.Value.Date;
        switch (Art)
        {
            case Zeitraumart.Tag:
                _bis.Value = von;
                break;
            case Zeitraumart.Woche:
                var montag = von.AddDays(-(((int)von.DayOfWeek + 6) % 7));
                _von.Value = montag;
                _bis.Value = montag.AddDays(AppZustand.D.Zeitraster.TageProWoche - 1);
                break;
            case Zeitraumart.VonBis when Gesperrt:
                // eingefrorener Tages-Abstand: geändertes Von verschiebt Bis mit, geändertes Bis setzt den neuen Abstand
                if (vonGeaendert) _bis.Value = _von.Value.Date.AddDays(_abstand);
                else if (_bis.Value.Date < _von.Value.Date) _von.Value = _bis.Value.Date;
                else
                {
                    _abstand = (int)(_bis.Value.Date - _von.Value.Date).TotalDays;
                    AppZustand.Einstellungen.Zeitraumarten[_schluessel + ".Schloss"] = _abstand.ToString();
                }
                break;
            default:
                if (_bis.Value.Date < _von.Value.Date)
                {
                    if (vonGeaendert) _bis.Value = _von.Value.Date;
                    else _von.Value = _bis.Value.Date;
                }
                break;
        }
        _wochentag.Text = Planungsdienst.TageKurz[((int)_von.Value.DayOfWeek + 6) % 7];
        _wochentagBis.Text = Planungsdienst.TageKurz[((int)_bis.Value.DayOfWeek + 6) % 7];
        _setzen = false;
        Geaendert?.Invoke();
    }
}
