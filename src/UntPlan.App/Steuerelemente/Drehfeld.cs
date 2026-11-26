using System.Globalization;

namespace UntPlan.App.Steuerelemente;

/// <summary>Aussehen eines <see cref="Drehfeld"/>s wie an den jeweiligen Stellen im Vorbild.</summary>
public enum DrehfeldStil
{
    /// <summary>„Vtr-Nr.“, „VA-Nr.“ (Bilder 308, 350): Eingabefeld 18 Pixel hoch mit dunkelgrauem Rand, Hoch/Runter-Knopf 17 × 18 rechts daneben.</summary>
    Abgesetzt,
    /// <summary>„Stunde von/bis“ (Bild 348): 20 Pixel hoch, hellgrauer Rand, Hoch/Runter-Knopf 16 × 18 innerhalb des Rands.</summary>
    Eingebettet,
    /// <summary>„Kalenderwoche“ (Bild 308): Zahl ohne Rand auf dem Fensterhintergrund, weißer Hoch/Runter-Knopf 17 × 23.</summary>
    Frei,
}

/// <summary>
/// Kompaktes Zahlenfeld mit Hoch/Runter-Knopf, pixelgenau wie im Vorbild gezeichnet (das Windows-Zahlenfeld ist mindestens
/// 23 Pixel hoch). Verhalten wie <see cref="NumericUpDown"/>: Eingabe wird bei Enter, Verlassen und den Pfeilen übernommen und auf
/// Minimum/Maximum begrenzt, Pfeiltasten und Mausrad zählen um <see cref="Increment"/>, Bild auf/ab um das Zehnfache.
/// </summary>
public class Drehfeld : Control
{
    private readonly TextBox _eingabe = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Left };
    private DrehfeldStil _stil;
    private decimal _wert, _minimum, _maximum = 100, _schritt = 1;
    private bool _nutzerEingabe, _setzt;
    private int _hover, _gedrueckt;
    private readonly System.Windows.Forms.Timer _wiederholen = new() { Interval = 400 };

    /// <summary>Wert hat sich geändert (Pfeile, Mausrad, übernommene Eingabe oder <see cref="Value"/>).</summary>
    public event EventHandler? ValueChanged;

    public Drehfeld() : this(DrehfeldStil.Eingebettet) { }

    public Drehfeld(DrehfeldStil stil)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable, true);
        _stil = stil;
        Controls.Add(_eingabe);
        Size = StandardGroesse(stil);
        _eingabe.Text = "0";
        _eingabe.TextChanged += (_, _) => { if (!_setzt) _nutzerEingabe = true; OnTextChanged(EventArgs.Empty); };
        _eingabe.KeyDown += (_, e) => TasteGedrueckt(e);
        _eingabe.KeyUp += (_, e) => OnKeyUp(e);
        _eingabe.KeyPress += (_, e) => ZeichenPruefen(e);
        _eingabe.MouseWheel += (_, e) =>
        {
            // das Eingabefeld reicht das Rad danach an dieses Feld weiter: nur einmal zählen
            Rad(e.Delta);
            _radGezaehlt = true;
            if (e is HandledMouseEventArgs h) h.Handled = true;
        };
        _eingabe.Leave += (_, _) => Uebernehmen();
        _eingabe.GotFocus += (_, _) => OnGotFocus(EventArgs.Empty);
        _eingabe.LostFocus += (_, _) => OnLostFocus(EventArgs.Empty);
        _wiederholen.Tick += (_, _) =>
        {
            _wiederholen.Interval = 60;
            if (_gedrueckt != 0 && Knopf(PointToClient(Cursor.Position)) == _gedrueckt) Drehen(_gedrueckt);
        };
        AnordnungAnpassen();
    }

    /// <summary>Größe wie im Vorbild je Stil.</summary>
    public static Size StandardGroesse(DrehfeldStil stil) => stil switch
    {
        DrehfeldStil.Abgesetzt => new Size(89, 18),
        DrehfeldStil.Frei => new Size(45, 23),
        _ => new Size(64, 20),
    };

    public DrehfeldStil Stil
    {
        get => _stil;
        set { _stil = value; AnordnungAnpassen(); Invalidate(); }
    }

    public decimal Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < value) _maximum = value; Value = _wert; }
    }

    public decimal Maximum
    {
        get => _maximum;
        set { _maximum = value; if (_minimum > value) _minimum = value; Value = _wert; }
    }

    public decimal Increment { get => _schritt; set => _schritt = value; }

    public decimal Value
    {
        get { if (_nutzerEingabe) Uebernehmen(); return _wert; }
        set
        {
            var neu = Math.Clamp(value, _minimum, _maximum);
            var geaendert = neu != _wert;
            _wert = neu;
            TextSetzen(Format(neu));
            _nutzerEingabe = false;
            if (geaendert) OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>Text des Eingabefelds (z. B. leer, solange nichts gewählt ist).</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _eingabe.Text;
        set => TextSetzen(value ?? "");
    }

    /// <summary>Hat das Eingabefeld den Fokus?</summary>
    public new bool Focused => _eingabe.Focused;

    /// <summary>Zum Eingabefeld (Selbsttest, Fokus, Markieren).</summary>
    public TextBox Eingabe => _eingabe;

    public HorizontalAlignment TextAlign { get => _eingabe.TextAlign; set => _eingabe.TextAlign = value; }

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    /// <summary>Oberer Knopf (Pfeil hoch, Bild auf über das Zehnfache): Wert um <see cref="Increment"/> erhöhen.</summary>
    public virtual void UpButton()
    {
        if (_nutzerEingabe) Uebernehmen();
        Value = _wert + _schritt;
    }

    /// <summary>Unterer Knopf: Wert um <see cref="Increment"/> verringern.</summary>
    public virtual void DownButton()
    {
        if (_nutzerEingabe) Uebernehmen();
        Value = _wert - _schritt;
    }

    /// <summary>Getippten Text übernehmen: Zahl begrenzt auf Minimum/Maximum, sonst den bisherigen Wert wieder anzeigen.</summary>
    public void Uebernehmen()
    {
        if (!_nutzerEingabe) return;
        _nutzerEingabe = false;
        var t = _eingabe.Text.Trim();
        if (t.Length == 0) return;
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out var z)) Value = z;
        else TextSetzen(Format(_wert));
    }

    private static string Format(decimal z) => Math.Round(z).ToString("0", CultureInfo.CurrentCulture);

    private void TextSetzen(string t)
    {
        if (_eingabe.Text == t) return;
        _setzt = true;
        try { _eingabe.Text = t; }
        finally { _setzt = false; }
    }

    private void TasteGedrueckt(KeyEventArgs e)
    {
        OnKeyDown(e);
        if (e.Handled || e.SuppressKeyPress) return;
        switch (e.KeyCode)
        {
            case Keys.Up: Drehen(1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Down: Drehen(-1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.PageUp: if (_nutzerEingabe) Uebernehmen(); Value = _wert + 10 * _schritt; e.Handled = e.SuppressKeyPress = true; break;
            case Keys.PageDown: if (_nutzerEingabe) Uebernehmen(); Value = _wert - 10 * _schritt; e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Enter: Uebernehmen(); break;
        }
    }

    private void ZeichenPruefen(KeyPressEventArgs e)
    {
        OnKeyPress(e);
        if (e.Handled || char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar)) return;
        // wie das Windows-Zahlenfeld: nur Ziffern (Minus nur bei negativem Minimum)
        if (e.KeyChar == '-' && _minimum < 0) return;
        e.Handled = true;
    }

    private void Drehen(int richtung)
    {
        if (richtung > 0) UpButton(); else DownButton();
    }

    private void Rad(int delta)
    {
        if (!Enabled || delta == 0) return;
        Drehen(delta > 0 ? 1 : -1);
    }

    private bool _radGezaehlt;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_radGezaehlt) _radGezaehlt = false;
        else Rad(e.Delta);
        base.OnMouseWheel(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _eingabe.Enabled = Enabled;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _eingabe.Font = Font;
        base.OnFontChanged(e);
        AnordnungAnpassen();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        AnordnungAnpassen();
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        AnordnungAnpassen();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (!_eingabe.Focused) _eingabe.Focus();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AnordnungAnpassen();
    }

    /// <summary>Lage des Hoch/Runter-Knopfs (samt hellem Rahmen).</summary>
    internal Rectangle KnopfFlaeche => _stil switch
    {
        DrehfeldStil.Abgesetzt => new Rectangle(Width - 17, (Height - 18) / 2, 17, 18),
        DrehfeldStil.Frei => new Rectangle(Width - 17, 0, 17, Height),
        _ => new Rectangle(Width - 17, 1, 16, Height - 2),
    };

    /// <summary>Fläche des Zahlenfelds (bei „Abgesetzt“ samt dunklem Rand).</summary>
    private Rectangle Feld => _stil == DrehfeldStil.Abgesetzt ? new Rectangle(0, 0, Width - 17, Height) : new Rectangle(0, 0, Width, Height);

    private void AnordnungAnpassen()
    {
        var hoehe = _eingabe.PreferredHeight;
        var feld = Feld;
        var links = _stil switch { DrehfeldStil.Frei => 0, DrehfeldStil.Abgesetzt => 3, _ => 2 };
        var rechts = _stil == DrehfeldStil.Abgesetzt ? feld.Right - 2 : KnopfFlaeche.X - 1;
        // Kalenderwoche (Bild 308): Ziffern 6 Pixel unter der Oberkante des 23 Pixel hohen Knopfs
        var oben = _stil == DrehfeldStil.Frei ? Math.Max(0, feld.Height / 2 - 7) : Math.Max(0, (feld.Height - hoehe + 1) / 2);
        _eingabe.SetBounds(links, oben, Math.Max(4, rechts - links), hoehe);
        _eingabe.BackColor = _stil == DrehfeldStil.Frei ? (BackColor.A == 255 ? BackColor : Parent?.BackColor ?? SystemColors.Control) : Enabled ? Color.White : SystemColors.Control;
    }

    /// <summary>+1 = oberer, −1 = unterer Knopf, 0 = nicht auf dem Knopf.</summary>
    private int Knopf(Point p)
    {
        var k = KnopfFlaeche;
        if (!k.Contains(p)) return 0;
        return p.Y < k.Y + k.Height / 2 ? 1 : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Enabled ? Knopf(e.Location) : 0;
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = 0;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        var k = Knopf(e.Location);
        if (k == 0) { _eingabe.Focus(); return; }
        if (!_eingabe.Focused) _eingabe.Focus();
        _gedrueckt = k;
        Invalidate();
        Drehen(k);
        _wiederholen.Interval = 400;
        _wiederholen.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _wiederholen.Stop();
        _gedrueckt = 0;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wiederholen.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var hinten = BackColor.A == 255 ? BackColor : Parent?.BackColor ?? SystemColors.Control;
        using (var b = new SolidBrush(hinten)) g.FillRectangle(b, ClientRectangle);
        var feld = Feld;
        switch (_stil)
        {
            case DrehfeldStil.Abgesetzt:
                g.FillRectangle(Enabled ? Brushes.White : SystemBrushes.Control, feld);
                using (var p = new Pen(Color.FromArgb(100, 100, 100))) g.DrawRectangle(p, feld.X, feld.Y, feld.Width - 1, feld.Height - 1);
                KnopfZeichnen(g, KnopfFlaeche, _hover, _gedrueckt, Enabled);
                break;
            case DrehfeldStil.Eingebettet:
                g.FillRectangle(Enabled ? Brushes.White : SystemBrushes.Control, feld);
                using (var p = new Pen(_hover != 0 || Focused ? Color.FromArgb(86, 157, 229) : Color.FromArgb(171, 173, 179)))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                KnopfZeichnen(g, KnopfFlaeche, _hover, _gedrueckt, Enabled);
                break;
            default:
                KnopfFreiZeichnen(g, KnopfFlaeche, _hover, _gedrueckt, Enabled);
                break;
        }
    }

    private static void Punkt(Graphics g, int x, int y, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, x, y, 1, 1);
    }

    private static void Zeile(Graphics g, int x, int y, int w, Color c)
    {
        if (w <= 0) return;
        using var b = new SolidBrush(c);
        g.FillRectangle(b, x, y, w, 1);
    }

    private static Color Grau(int v) => Color.FromArgb(v, v, v);

    /// <summary>
    /// Hoch/Runter-Knopf wie im Vorbild (Bilder 308, 348, 350 pixelweise abgelesen): 1 Pixel Rahmen (240), darin zwei 7 Pixel hohe
    /// Hälften (Fläche 250, Kanten 210 bis 213), dazwischen eine Linie 188 und eine Zeile 210; Pfeile 1, 3 und 5 Pixel breit (26) mit
    /// weichen Kanten. Höhere Knöpfe verteilen die zusätzlichen Zeilen auf beide Hälften.
    /// </summary>
    internal static void KnopfZeichnen(Graphics g, Rectangle r, int hover, int gedrueckt, bool aktiv)
    {
        Zeile(g, r.X, r.Y, r.Width, Grau(240));
        Zeile(g, r.X, r.Bottom - 1, r.Width, Grau(240));
        for (var y = r.Y; y < r.Bottom; y++) { Punkt(g, r.X, y, Grau(240)); Punkt(g, r.Right - 1, y, Grau(240)); }
        var ix = r.X + 1;
        var iw = r.Width - 2;
        var ih = r.Height - 2;
        var haelfte = Math.Max(3, (ih - 2) / 2);
        var obenY = r.Y + 1;
        var trennY = obenY + haelfte;
        var untenY = trennY + 2;
        var untenH = r.Bottom - 1 - untenY;
        var mitte = ix + (iw - 1) / 2;
        var pfeil = aktiv ? Grau(26) : Grau(160);
        foreach (var (y0, h, richtung) in new[] { (obenY, haelfte, 1), (untenY, untenH, -1) })
        {
            var flaeche = gedrueckt == richtung ? Ui.BlauHell : hover == richtung ? Ui.Hover : Grau(250);
            using (var b = new SolidBrush(flaeche)) g.FillRectangle(b, ix, y0, iw, h);
            for (var y = y0; y < y0 + h; y++) { Punkt(g, ix, y, Grau(210)); Punkt(g, ix + iw - 1, y, Grau(210)); }
            if (richtung > 0)
            {
                Punkt(g, ix, y0, Grau(213)); Punkt(g, ix + 1, y0, Grau(245)); Zeile(g, ix + 2, y0, iw - 4, flaeche);
                Punkt(g, ix + iw - 2, y0, Grau(245)); Punkt(g, ix + iw - 1, y0, Grau(212));
            }
            else
            {
                var u = y0 + h - 1;
                Punkt(g, ix, u - 1, Grau(211)); Punkt(g, ix + 1, u - 1, Grau(245)); Zeile(g, ix + 2, u - 1, iw - 4, flaeche);
                Punkt(g, ix + iw - 2, u - 1, Grau(245)); Punkt(g, ix + iw - 1, u - 1, Grau(214));
                Punkt(g, ix, u, Grau(220)); Punkt(g, ix + 1, u, Grau(195)); Zeile(g, ix + 2, u, iw - 4, Grau(188));
                Punkt(g, ix + iw - 2, u, Grau(192)); Punkt(g, ix + iw - 1, u, Grau(220));
            }
            // Pfeil: oben ab der 2. Zeile der Hälfte, unten bis zur viertletzten (gespiegelt)
            var py = richtung > 0 ? y0 + 1 + Math.Max(0, (h - 7) / 2) : y0 + h - 6 - Math.Max(0, (h - 7) / 2);
            for (var i = 0; i < 4; i++)
            {
                var y = richtung > 0 ? py + i : py + 3 - i;
                var kanten = i switch { 0 => Grau(180), 1 => Grau(138), 2 => Grau(152), _ => Grau(236) };
                if (i == 0) { Punkt(g, mitte, y, aktiv ? kanten : Grau(215)); continue; }
                Zeile(g, mitte - i + 1, y, 2 * i - 1, pfeil);
                Punkt(g, mitte - i, y, aktiv ? kanten : Grau(225));
                Punkt(g, mitte + i, y, aktiv ? kanten : Grau(225));
            }
        }
        Zeile(g, ix, trennY, iw, Grau(188));
        Zeile(g, ix, trennY + 1, iw, Grau(210));
    }

    /// <summary>
    /// Hoch/Runter-Knopf der Kalenderwoche (Bild 308): zwei weiße Hälften mit hellem Rand (245, 249, 247), die obere 12, die untere
    /// 11 Pixel hoch; Pfeile 1, 3 und 5 Pixel breit (72) mit weichen Kanten, 5 Pixel unter bzw. 4 Pixel über dem Rand.
    /// </summary>
    internal static void KnopfFreiZeichnen(Graphics g, Rectangle r, int hover, int gedrueckt, bool aktiv)
    {
        var rand = Color.FromArgb(245, 249, 247);
        var obenH = (r.Height + 1) / 2;
        var oben = new Rectangle(r.X, r.Y, r.Width, obenH);
        var unten = new Rectangle(r.X, r.Y + obenH, r.Width, r.Height - obenH);
        var mitte = r.X + r.Width / 2;
        foreach (var (k, richtung) in new[] { (oben, 1), (unten, -1) })
        {
            var flaeche = gedrueckt == richtung ? Ui.BlauHell : hover == richtung ? Ui.Hover : Color.White;
            using (var b = new SolidBrush(flaeche)) g.FillRectangle(b, k);
            using (var p = new Pen(hover == richtung ? Ui.HoverRand : rand)) g.DrawRectangle(p, k.X, k.Y, k.Width - 1, k.Height - 1);
            var dunkel = aktiv ? Grau(72) : Grau(170);
            var y0 = richtung > 0 ? k.Y + 5 : k.Bottom - 5;
            var schritt = richtung > 0 ? 1 : -1;
            // Spitze (243 | 129 | 243), Mitte (252 | 121 | 72 | 121 | 252), Fuß (155 | 72 72 72 | 155)
            Punkt(g, mitte - 1, y0, Grau(243)); Punkt(g, mitte, y0, aktiv ? Grau(129) : Grau(200)); Punkt(g, mitte + 1, y0, Grau(243));
            var y1 = y0 + schritt;
            Punkt(g, mitte - 2, y1, Grau(252)); Punkt(g, mitte - 1, y1, aktiv ? Grau(121) : Grau(200)); Punkt(g, mitte, y1, dunkel);
            Punkt(g, mitte + 1, y1, aktiv ? Grau(121) : Grau(200)); Punkt(g, mitte + 2, y1, Grau(252));
            var y2 = y1 + schritt;
            Punkt(g, mitte - 2, y2, aktiv ? Grau(155) : Grau(210)); Zeile(g, mitte - 1, y2, 3, dunkel); Punkt(g, mitte + 2, y2, aktiv ? Grau(155) : Grau(210));
        }
    }
}
