using System.ComponentModel;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Reiter wie im Vorbild (Absenzen: „Lehrer (0) | Klassen (0) | Räume (0)“): klassische Karteireiter auf einem Band in der
/// Hintergrundfarbe des Arbeitsbereichs. Der gewählte Reiter ist hell und 2 Pixel höher, die übrigen sitzen im Band; darunter
/// die Kante zur Tabelle (deren vertiefter Rahmen schließt an). Rahmen klassisch: links/oben hell (Weiß, Hellgrau), rechts dunkel (Grau, Dunkelgrau).
/// </summary>
public sealed class Reiterleiste : Control
{
    private readonly List<string> _texte = new();
    private int _gewaehlt;

    public event EventHandler<CancelEventArgs>? Selecting;
    public event EventHandler? SelectedIndexChanged;

    public Reiterleiste()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Dock = DockStyle.Top;
        Font = Ui.Schrift;
        Height = HoeheNachSchrift();
        TabStop = true;
    }

    private int HoeheNachSchrift() => Font.Height + 8;

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Height = HoeheNachSchrift(); }

    public int Count => _texte.Count;

    public void Hinzufuegen(string text) { _texte.Add(text); Invalidate(); }

    /// <summary>Beschriftung eines Reiters (z. B. mit Anzahl).</summary>
    public void TextSetzen(int i, string text)
    {
        if (i < 0 || i >= _texte.Count || _texte[i] == text) return;
        _texte[i] = text;
        Invalidate();
    }

    public string Beschriftung(int i) => _texte[i];

    public int SelectedIndex
    {
        get => _gewaehlt;
        set
        {
            if (value < 0 || value >= _texte.Count || value == _gewaehlt) return;
            var ce = new CancelEventArgs();
            Selecting?.Invoke(this, ce);
            if (ce.Cancel) return;
            _gewaehlt = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gewählter Reiter: etwas heller als der Kopfbereich (nachgemessen).</summary>
    private static Color Gewaehlt => Ui.Vertretungsmodus ? Color.FromArgb(225, 243, 236) : Color.FromArgb(230, 242, 252);

    /// <summary>Rechtecke der Reiter (ohne die Vergrößerung des gewählten).</summary>
    private List<Rectangle> Rechtecke()
    {
        var res = new List<Rectangle>();
        var x = 2;
        var oben = 3;
        var unten = Height - 5;
        foreach (var t in _texte)
        {
            var w = TextRenderer.MeasureText(t, Font, Size.Empty, TextFormatFlags.NoPadding).Width + 10;
            res.Add(new Rectangle(x, oben, w, unten - oben));
            x += w;
        }
        return res;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // Kopfbereich oberhalb des Bandes, Band in der Farbe des Arbeitsbereichs
        using (var kopf = new SolidBrush(Ui.Kopf)) g.FillRectangle(kopf, 0, 0, Width, 1);
        using (var band = new SolidBrush(Ui.MdiHintergrund)) g.FillRectangle(band, 0, 1, Width, Height - 1);
        var unten = Height - 5;
        // Kante unter den Reitern (hell) und Rahmen der Tabelle darunter (vertieft)
        using var weiss = new Pen(Color.White);
        using var hell = new Pen(Color.FromArgb(227, 227, 227));
        using var grau = new Pen(Color.FromArgb(160, 160, 160));
        using var dunkel = new Pen(Color.FromArgb(105, 105, 105));
        g.DrawLine(weiss, 0, unten, Width, unten);
        g.DrawLine(hell, 0, unten + 1, Width, unten + 1);
        g.DrawLine(grau, 0, Height - 2, Width, Height - 2);
        g.DrawLine(dunkel, 0, Height - 1, Width, Height - 1);
        var rr = Rechtecke();
        for (var i = 0; i < rr.Count; i++)
        {
            if (i == _gewaehlt) continue;
            Reiter(g, rr[i], false, _texte[i], weiss, hell, grau, dunkel);
        }
        if (_gewaehlt < rr.Count)
        {
            var r = rr[_gewaehlt];
            // gewählter Reiter: 2 Pixel breiter und höher, verbunden mit der Fläche darunter
            Reiter(g, Rectangle.FromLTRB(Math.Max(0, r.Left - 2), 0, r.Right + 2, unten + 2), true, _texte[_gewaehlt], weiss, hell, grau, dunkel);
        }
    }

    private void Reiter(Graphics g, Rectangle r, bool gewaehlt, string text, Pen weiss, Pen hell, Pen grau, Pen dunkel)
    {
        using (var b = new SolidBrush(gewaehlt ? Gewaehlt : Ui.MdiHintergrund)) g.FillRectangle(b, r.Left + 2, r.Top + 2, r.Width - 4, r.Height - 2);
        if (gewaehlt) using (var oben = new Pen(Color.FromArgb(213, 213, 213))) g.DrawLine(oben, r.Left + 1, r.Top, r.Right - 2, r.Top);
        g.DrawLine(weiss, r.Left, r.Top + 1, r.Left, r.Bottom - 1);
        g.DrawLine(weiss, r.Left + 1, r.Top + 1, r.Right - 2, r.Top + 1);
        g.DrawLine(hell, r.Left + 1, r.Top + 2, r.Left + 1, r.Bottom - 1);
        g.DrawLine(hell, r.Left + 2, r.Top + 2, r.Right - 3, r.Top + 2);
        g.DrawLine(grau, r.Right - 2, r.Top + 2, r.Right - 2, r.Bottom - 1);
        g.DrawLine(dunkel, r.Right - 1, r.Top + 2, r.Right - 1, r.Bottom - 1);
        var textR = Rectangle.FromLTRB(r.Left + 2, r.Top + 2, r.Right - 2, r.Bottom);
        TextRenderer.DrawText(g, text, Font, textR, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        if (gewaehlt && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textR, -2, -3));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var rr = Rechtecke();
        for (var i = 0; i < rr.Count; i++)
            if (e.X >= rr[i].Left && e.X < rr[i].Right && e.Y < Height - 4) { Focus(); SelectedIndex = i; return; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left && _gewaehlt > 0) SelectedIndex = _gewaehlt - 1;
        else if (e.KeyCode == Keys.Right && _gewaehlt < _texte.Count - 1) SelectedIndex = _gewaehlt + 1;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
