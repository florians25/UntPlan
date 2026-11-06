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
    /// <summary>Platzhalter für Schloss und Bis-Datum, solange sie ausgeblendet sind.</summary>
    private readonly Panel _schlossPlatz;
    private readonly int _volleBreite;
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

    /// <summary>Kalenderknopf (Zeitraumart); liegt wie im Vorbild rechts neben dem Rahmen „Von-Bis“, wenn das Fenster ihn dort einsetzt.</summary>
    public Control Kalender { get; }

    public ZeitraumAuswahl(DateTimePicker von, DateTimePicker bis, string schluessel, bool schloss = false, bool kalenderAussen = false)
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
        // wie im Vorbild: Datum immer als TT.MM.JJJJ (unabhängig von der Windows-Region), Wochentag steht darunter
        foreach (var f in new[] { _von, _bis })
        {
            f.Format = DateTimePickerFormat.Custom;
            f.CustomFormat = "dd.MM.yyyy";
        }
        _von.Margin = new Padding(0, 0, 0, 0);
        _bis.Margin = new Padding(0, 0, 0, 0);
        // Breite nach der tatsächlichen Schrift: das Datum passt auch bei größerer Windows-Schrift vollständig hinein
        var muster = new DateTime(2026, 11, 25).ToString("dd.MM.yyyy");
        var noetig = Math.Max(TextRenderer.MeasureText(muster, _von.Font).Width, TextRenderer.MeasureText(muster, Ui.Schrift).Width) + SystemInformation.VerticalScrollBarWidth + 6;
        _von.Width = _bis.Width = noetig;
        // wie im Vorbild: unter jedem Datum links der Wochentag, rechts bündig zwei kleine weiße Pfeilfelder
        PfeilKnopf Klein(PfeilKnopf.Art art, string hinweis) => new(art, hinweis) { Klassisch = true, Size = new Size(15, 14), Margin = Padding.Empty, Cursor = Cursors.Default };
        var zurueck = Klein(PfeilKnopf.Art.Links, "Zurück");
        var vor = Klein(PfeilKnopf.Art.Rechts, "Vor");
        zurueck.Click += (_, _) => Blaettern(-1);
        vor.Click += (_, _) => Blaettern(+1);
        var bisZurueck = Klein(PfeilKnopf.Art.Links, "Bis-Datum zurück");
        var bisVor = Klein(PfeilKnopf.Art.Rechts, "Bis-Datum vor");
        bisZurueck.Click += (_, _) => BisVerschieben(-1);
        bisVor.Click += (_, _) => BisVerschieben(+1);
        var kalender = new PfeilKnopf(PfeilKnopf.Art.Kalender, "Zeitraum: Tag, Woche oder Von-Bis")
        {
            Klassisch = true, Size = new Size(35, Math.Max(26, _von.PreferredSize.Height + 3)), Margin = new Padding(7, 0, 0, 0), Cursor = Cursors.Default,
        };
        Kalender = kalender;
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
            var sp = new Panel { Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            void Anordnen()
            {
                var h = feld.Height;
                feld.Location = Point.Empty;
                tag.AutoSize = true;
                tag.Padding = Padding.Empty;
                tag.MinimumSize = Size.Empty;
                tag.Location = new Point(1, h + 4);
                v.Location = new Point(feld.Width - 2 - v.Width, h + 5);
                z.Location = new Point(v.Left - z.Width, h + 5);
                sp.Size = new Size(feld.Width, Math.Max(tag.Bottom, v.Bottom) + 1);
            }
            sp.Controls.AddRange(new Control[] { feld, tag, z, v });
            feld.SizeChanged += (_, _) => Anordnen();
            tag.SizeChanged += (_, _) => Anordnen();
            Anordnen();
            return sp;
        }
        _bisZeile = Spalte(_bis, _wochentagBis, bisZurueck, bisVor);
        _bisZeile.Margin = new Padding(0, 0, 0, 0);
        Controls.Add(Spalte(_von, _wochentag, zurueck, vor));
        _schlossPlatz = new Panel { Size = new Size(39, 1), Margin = Padding.Empty, BackColor = Color.Transparent };
        if (schloss)
        {
            _schloss = new CheckBox
            {
                Appearance = Appearance.Button, Image = Symbole.Get("schloss", 16), Size = new Size(23, 23), Margin = new Padding(6, 0, 10, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Text = "", Visible = false,
            };
            _schloss.FlatAppearance.BorderColor = Color.FromArgb(171, 171, 171);
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
        else _bisZeile.Margin = new Padding(10, 0, 0, 0);
        Controls.Add(_schlossPlatz);
        Controls.Add(_bisZeile);
        if (!kalenderAussen) Controls.Add(kalender);
        // wie im Vorbild bleibt der Platz für das Bis-Datum auch bei „Tag“ frei (Rahmen „Von-Bis“ immer gleich breit)
        _volleBreite = _von.Width + 6 + 23 + 10 + _bis.Width + (kalenderAussen ? 0 : kalender.Width + kalender.Margin.Horizontal);
        _von.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(true); };
        _bis.ValueChanged += (_, _) => { if (!_setzen) Abgleichen(false); };
        var gespeichert = AppZustand.Einstellungen.Zeitraumarten.TryGetValue(schluessel, out var g) && Enum.TryParse<Zeitraumart>(g, out var z) ? z : Zeitraumart.Tag;
        ArtSetzen(gespeichert, false);
    }

    /// <summary>
    /// Rahmen (GroupBox) und Kopfbereich darüber an die tatsächliche Höhe anpassen: Datumsfeld und Wochentagszeile mit Pfeilen
    /// sind je nach Windows-Version und Schrift unterschiedlich hoch – nichts darf abgeschnitten oder von Reitern überdeckt werden.
    /// </summary>
    private void PlatzSichern()
    {
        if (!IsHandleCreated) return;
        for (Control? c = this, p = Parent; p != null; c = p, p = p.Parent)
        {
            if (p is GroupBox gb)
            {
                var noetig = PointToScreen(new Point(0, Height)).Y - gb.PointToScreen(Point.Empty).Y + 7;
                if (gb.Height < noetig) { gb.MinimumSize = new Size(gb.MinimumSize.Width, noetig); gb.Height = noetig; }
            }
            else if (p is FlowLayoutPanel kopf && kopf.Dock == DockStyle.Top)
            {
                var noetig = 0;
                foreach (Control k in kopf.Controls) if (k.Visible) noetig = Math.Max(noetig, k.Bottom + k.Margin.Bottom);
                noetig += kopf.Padding.Bottom + 2;
                if (kopf.Height < noetig) { kopf.MinimumSize = new Size(kopf.MinimumSize.Width, noetig); kopf.Height = noetig; }
                break;
            }
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (IsHandleCreated) BeginInvoke(PlatzSichern);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(PlatzSichern);
    }

    /// <summary>Platzhalter füllt die Lücke ausgeblendeter Felder: der Kopf bleibt in jeder Zeitraumart gleich breit.</summary>
    private void PlatzhalterAnpassen()
    {
        var belegt = _von.Width + (_schloss is { Visible: true } ? _schloss.Width + _schloss.Margin.Horizontal : 0) +
                     (_bisZeile is { Visible: true } ? _bisZeile.Width + _bisZeile.Margin.Horizontal : 0) +
                     (Controls.Contains(Kalender) ? Kalender.Width + Kalender.Margin.Horizontal : 0);
        _schlossPlatz.Width = Math.Max(0, _volleBreite - belegt);
        // Platzhalter vor dem Kalenderknopf (bzw. am Ende)
        Controls.SetChildIndex(_schlossPlatz, Controls.Count - (Controls.Contains(Kalender) ? 2 : 1));
    }

    /// <summary>
    /// In den Rahmen „Von-Bis“ setzen – Abstände wie im Vorbild nachgemessen: Datum knapp unter dem Rahmentitel, unter den Pfeilen
    /// 6 Pixel bis zur Rahmenlinie, rechts 8 Pixel.
    /// </summary>
    public void InRahmen(GroupBox gb)
    {
        gb.AutoSize = false;
        gb.Controls.Add(this);
        Location = new Point(9, Math.Max(16, gb.Font.Height + 1));
        void Groesse() => gb.Size = new Size(Right + 8, Bottom + 6);
        SizeChanged += (_, _) => Groesse();
        Groesse();
    }

    public void ArtSetzen(Zeitraumart art, bool merken = true)
    {
        Art = art;
        foreach (ToolStripMenuItem it in _art.DropDownItems) it.Checked = _art.DropDownItems.IndexOf(it) == (int)art;
        if (_bisZeile != null) _bisZeile.Visible = art != Zeitraumart.Tag;
        if (_schloss != null) _schloss.Visible = art == Zeitraumart.VonBis;
        PlatzhalterAnpassen();
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
