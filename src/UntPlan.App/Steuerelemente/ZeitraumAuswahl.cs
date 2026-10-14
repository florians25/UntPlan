using UntPlan.Core.Planung;

namespace UntPlan.App.Steuerelemente;

public enum Zeitraumart { Tag, Woche, VonBis }

/// <summary>
/// Kopfbereich „Von-Bis“ für Listen des Vertretungsmodus:
/// <list type="bullet">
/// <item>Datum mit Wochentag.</item>
/// <item>◄ ► blättert um einen Tag, eine Woche bzw. die Länge des Zeitraums.</item>
/// <item>Die Schaltfläche mit Menü wählt die Zeitraumart Tag, Woche oder Von-Bis.</item>
/// </list>
/// Arbeitet mit zwei vorhandenen Datumsfeldern (Von, Bis); das Bis-Feld ist nur bei „Von-Bis“ sichtbar.
/// </summary>
public sealed class ZeitraumAuswahl : FlowLayoutPanel
{
    private readonly DateTimePicker _von;
    private readonly DateTimePicker _bis;
    private readonly Label _wochentag = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(2, 5, 4, 0) };
    private readonly Label _strich = new() { Text = "–", AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
    private readonly ToolStripDropDownButton _art = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Zeitraum: Tag, Woche oder Von-Bis" };
    private readonly string _schluessel;
    private bool _setzen;

    public event Action? Geaendert;

    public Zeitraumart Art { get; private set; }

    public ZeitraumAuswahl(DateTimePicker von, DateTimePicker bis, string schluessel)
    {
        _von = von;
        _bis = bis;
        _schluessel = schluessel;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0);
        Padding = new Padding(0);
        BackColor = Color.Transparent;
        _von.Margin = _bis.Margin = new Padding(2, 2, 2, 0);
        var zurueck = new PfeilKnopf(PfeilKnopf.Art.Links, "Zurück");
        var vor = new PfeilKnopf(PfeilKnopf.Art.Rechts, "Vor");
        zurueck.Click += (_, _) => Blaettern(-1);
        vor.Click += (_, _) => Blaettern(+1);
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
        Controls.AddRange(new Control[] { zurueck, _von, _wochentag, _strich, _bis, vor, kalender });
        _von.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(true); };
        _bis.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(false); };
        var gespeichert = AppZustand.Einstellungen.Zeitraumarten.TryGetValue(schluessel, out var g) && Enum.TryParse<Zeitraumart>(g, out var z) ? z : Zeitraumart.Tag;
        ArtSetzen(gespeichert, false);
    }

    public void ArtSetzen(Zeitraumart art, bool merken = true)
    {
        Art = art;
        foreach (ToolStripMenuItem it in _art.DropDownItems) it.Checked = _art.DropDownItems.IndexOf(it) == (int)art;
        _bis.Visible = _strich.Visible = art != Zeitraumart.Tag;
        _bis.Enabled = art == Zeitraumart.VonBis;
        if (merken)
        {
            AppZustand.Einstellungen.Zeitraumarten[_schluessel] = art.ToString();
            AppZustand.Einstellungen.Speichern();
        }
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
            default:
                if (_bis.Value.Date < _von.Value.Date)
                {
                    if (vonGeaendert) _bis.Value = _von.Value.Date;
                    else _von.Value = _bis.Value.Date;
                }
                break;
        }
        _wochentag.Text = Planungsdienst.TageKurz[((int)_von.Value.DayOfWeek + 6) % 7];
        _setzen = false;
        Geaendert?.Invoke();
    }
}
