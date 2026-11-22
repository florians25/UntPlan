namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Kleiner Hoch/Runter-Knopf wie im Vorbild (neben der Elementauswahl und dem Datum): oben = weiter (+1), unten = zurück (−1).
/// </summary>
public sealed class Drehknopf : Control
{
    private int _hover;
    private int _gedrueckt;

    /// <summary>Geklickt: +1 (obere Hälfte) bzw. −1 (untere Hälfte).</summary>
    public event Action<int>? Gedreht;

    public Drehknopf(string hinweis)
    {
        Size = new Size(14, 21);
        Margin = new Padding(1, 1, 1, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        new ToolTip().SetToolTip(this, hinweis);
    }

    private int Haelfte(Point p) => p.Y < Height / 2 ? 1 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Haelfte(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = _gedrueckt = 0; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _gedrueckt = Haelfte(e.Location); Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var war = _gedrueckt;
        _gedrueckt = 0;
        Invalidate();
        if (e.Button == MouseButtons.Left && war != 0 && war == Haelfte(e.Location)) Gedreht?.Invoke(war);
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e) => Zeichnen(e.Graphics, Size, _hover, _gedrueckt, Enabled);

    /// <summary>
    /// Hoch/Runter-Knopf zeichnen: zwei gleich hohe Hälften (bei ungerader Höhe mit gemeinsamer Mittellinie) und zwei pixelgenau
    /// gespiegelte, gleich große Pfeile, jeweils gleich in ihrer Hälfte zentriert (wie im Vorbild „Kalenderwoche“).
    /// </summary>
    internal static void Zeichnen(Graphics g, Size groesse, int hover, int gedrueckt, bool aktiv)
    {
        var (w, h) = (groesse.Width, groesse.Height);
        if (w < 4 || h < 6) return;
        var obenEnde = h % 2 == 1 ? h / 2 : h / 2 - 1;
        var untenAnfang = h / 2;
        var oben = new Rectangle(0, 0, w - 1, obenEnde);
        var unten = new Rectangle(0, untenAnfang, w - 1, h - 1 - untenAnfang);
        var alt = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        // die überfahrene Hälfte zuletzt, damit ihr Rand auf der gemeinsamen Mittellinie sichtbar bleibt
        var reihenfolge = hover == 1 ? new[] { (unten, -1), (oben, 1) } : new[] { (oben, 1), (unten, -1) };
        foreach (var (r, richtung) in reihenfolge)
        {
            var farbe = gedrueckt == richtung ? Ui.BlauHell : hover == richtung ? Ui.Hover : Color.FromArgb(250, 250, 250);
            using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
            using (var p = new Pen(hover == richtung ? Ui.HoverRand : Color.FromArgb(170, 170, 170))) g.DrawRectangle(p, r);
            PfeilZeichnen(g, new Rectangle(r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1), richtung > 0, aktiv ? Color.FromArgb(40, 40, 40) : Color.Silver);
        }
        g.SmoothingMode = alt;
    }

    /// <summary>Pfeil aus drei Pixelzeilen (1, 3, 5 Pixel breit) mittig im Innenraum – oben und unten deckungsgleich gespiegelt.</summary>
    internal static void PfeilZeichnen(Graphics g, Rectangle innen, bool hoch, Color farbe)
    {
        const int zeilen = 3;
        var mx = innen.X + (innen.Width - 1) / 2;
        // abgerundeter Rest liegt beim oberen Pfeil unten, beim unteren oben (Spiegelbild an der Mittellinie)
        var rest = innen.Height - zeilen;
        var y0 = innen.Y + (hoch ? rest / 2 : rest - rest / 2);
        using var b = new SolidBrush(farbe);
        for (var i = 0; i < zeilen; i++)
            g.FillRectangle(b, mx - i, hoch ? y0 + i : y0 + zeilen - 1 - i, 2 * i + 1, 1);
    }

    /// <summary>
    /// Zahlenfelder (NumericUpDown): die von Windows gezeichneten Pfeile sind bei kleinen Höhen oben und unten verschieden groß –
    /// darüber denselben Hoch/Runter-Knopf mit gleichen Pfeilen zeichnen (Zustand aus der Mausposition).
    /// </summary>
    internal static void PfeileEinheitlich(NumericUpDown n)
    {
        if (n.Controls.Cast<Control>().FirstOrDefault(c => c.GetType().Name == "UpDownButtons") is not { } knoepfe) return;
        knoepfe.Paint += (_, e) =>
        {
            var pt = knoepfe.PointToClient(Cursor.Position);
            var hover = knoepfe.ClientRectangle.Contains(pt) && knoepfe.Enabled ? (pt.Y < knoepfe.Height / 2 ? 1 : -1) : 0;
            var gedrueckt = hover != 0 && (Control.MouseButtons & MouseButtons.Left) != 0 ? hover : 0;
            using (var b = new SolidBrush(n.BackColor)) e.Graphics.FillRectangle(b, knoepfe.ClientRectangle);
            Zeichnen(e.Graphics, knoepfe.ClientSize, hover, gedrueckt, n.Enabled);
        };
        // Überfahren/Verlassen neu zeichnen (Hervorhebung der Hälfte unter der Maus)
        knoepfe.MouseMove += (_, _) => knoepfe.Invalidate();
        knoepfe.MouseLeave += (_, _) => knoepfe.Invalidate();
        knoepfe.MouseDown += (_, _) => knoepfe.Invalidate();
        knoepfe.MouseUp += (_, _) => knoepfe.Invalidate();
        knoepfe.Invalidate();
    }
}
