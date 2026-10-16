using System.Drawing.Drawing2D;

namespace UntPlan.App.Steuerelemente;

public sealed class RibbonEintrag
{
    public string Text { get; init; } = "";
    public string Symbol { get; init; } = "";
    public bool Gross { get; init; }
    public Action? Klick { get; init; }
    /// <summary>Untermenü (null-Einträge = Trennlinie).</summary>
    public List<RibbonEintrag?>? Menue { get; init; }
    public string Hinweis { get; init; } = "";
    public Func<bool>? Aktiviert { get; init; }
    public Keys Tastenkuerzel { get; init; }
    /// <summary>Umschalter: eingedrückt darstellen (z. B. Vertretungsmodus aktiv).</summary>
    public Func<bool>? Gedrueckt { get; init; }
    /// <summary>Datumsfeld mit Pfeilen (Planungsdatum).</summary>
    public bool IstDatum { get; init; }
    internal Rectangle Bounds;
    internal Rectangle PfeilBounds;
}

public sealed class RibbonGruppe
{
    public string Text { get; init; } = "";
    public List<RibbonEintrag> Eintraege { get; } = new();
    internal Rectangle Bounds;
}

public sealed class RibbonSeite
{
    public string Text { get; init; } = "";
    public List<RibbonGruppe> Gruppen { get; } = new();
    /// <summary>Nur unter dieser Bedingung sichtbar (z. B. Registerkarte nur im Vertretungsmodus).</summary>
    public Func<bool>? Sichtbar { get; init; }
    public bool IstSichtbar => Sichtbar?.Invoke() ?? true;
    internal Rectangle TabBounds;
}

/// <summary>Menüband im Stil klassischer Office-/Windows-Anwendungen.</summary>
public sealed class Ribbon : Control
{
    /// <summary>Registerleiste wie im Vorbild 2026: schmal, in der Modusfarbe, durchgehend mit der Titelzeile darüber.</summary>
    public const int TabHoehe = 24;
    /// <summary>Schrift der Registerkarten (Datei, Start, …) – klein und weiß wie im Vorbild.</summary>
    private static readonly Font TabSchrift = new("Segoe UI", 9f);
    /// <summary>Kleine Knöpfe rechts in der Registerleiste (Fensteranordnungen 1–8).</summary>
    public ToolStrip Rechts { get; } = new()
    {
        GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, AutoSize = true, Padding = new Padding(0),
        ImageScalingSize = new Size(16, 16), CanOverflow = false, Renderer = new LeistenRenderer(),
    };
    private const int Inhalt = 94;
    private readonly ToolTip _tip = new() { InitialDelay = 600 };
    private RibbonEintrag? _hover;
    private RibbonSeite? _hoverTab;
    private Rectangle _dateiBounds;
    private bool _hoverDatei;

    public List<RibbonSeite> Seiten { get; } = new();
    public int AktiveSeite { get; set; } = 1;
    public List<RibbonEintrag?> DateiMenue { get; } = new();

    public Ribbon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = TabHoehe + Inhalt + 1;
        Font = Ui.Schrift;
        Controls.Add(Rechts);
        Rechts.SizeChanged += (_, _) => RechtsPlatzieren();
    }

    private void RechtsPlatzieren()
    {
        var w = Rechts.PreferredSize.Width;
        Rechts.SetBounds(Math.Max(0, Width - w - 6), 1, w, TabHoehe - 2);
        Rechts.BackColor = Ui.Titel;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RechtsPlatzieren();
    }

    /// <summary>Liegt der Punkt in der Registerleiste auf keiner Registerkarte (dort lässt sich das Fenster ziehen)?</summary>
    public bool FreieKopfFlaeche(Point p) =>
        p.Y < TabHoehe && !_dateiBounds.Contains(p) && !Seiten.Any(s => s.IstSichtbar && s.TabBounds.Contains(p)) && !Rechts.Bounds.Contains(p);

    /// <summary>Akzentlinie über der gewählten Registerkarte (heller und kräftiger als die Modusfarbe).</summary>
    public static Color Akzent => Ui.Vertretungsmodus ? Color.FromArgb(122, 204, 96) : Color.FromArgb(96, 186, 232);

    public RibbonSeite Seite(string text, Func<bool>? sichtbar = null)
    {
        var s = new RibbonSeite { Text = text, Sichtbar = sichtbar };
        Seiten.Add(s);
        return s;
    }

    private void Layout2(Graphics g)
    {
        var x = 4;
        _dateiBounds = new Rectangle(x, 2, (int)g.MeasureString("Datei", TabSchrift).Width + 22, TabHoehe - 2);
        x = _dateiBounds.Right + 2;
        if (!(Seiten.ElementAtOrDefault(AktiveSeite)?.IstSichtbar ?? false))
            AktiveSeite = Math.Max(0, Seiten.FindIndex(x => x.IstSichtbar));
        foreach (var s in Seiten)
        {
            if (!s.IstSichtbar) { s.TabBounds = Rectangle.Empty; continue; }
            var w = (int)g.MeasureString(s.Text, TabSchrift).Width + 24;
            s.TabBounds = new Rectangle(x, 2, w, TabHoehe - 2);
            x += w + 1;
        }
        var seite = Seiten.ElementAtOrDefault(AktiveSeite);
        if (seite is null) return;
        x = 6;
        foreach (var gr in seite.Gruppen)
        {
            var start = x;
            var y = TabHoehe + 3;
            var spaltenBreite = 0;
            var zeile = 0;
            foreach (var e in gr.Eintraege)
            {
                if (e.Gross)
                {
                    if (zeile > 0) { x += spaltenBreite + 4; zeile = 0; spaltenBreite = 0; }
                    var w = Math.Max(44, (int)g.MeasureString(e.Text, Font).Width + 10);
                    e.Bounds = new Rectangle(x, y, w, Inhalt - 22);
                    e.PfeilBounds = e.Menue != null ? new Rectangle(x, y + Inhalt - 36, w, 14) : Rectangle.Empty;
                    x += w + 2;
                }
                else
                {
                    var w = e.IstDatum ? 136 : (int)g.MeasureString(e.Text, Font).Width + 26 + (e.Menue != null ? 10 : 0);
                    spaltenBreite = Math.Max(spaltenBreite, w);
                    e.Bounds = new Rectangle(x, y + zeile * 23, w, 22);
                    e.PfeilBounds = e.Menue != null && e.Klick != null ? new Rectangle(e.Bounds.Right - 12, e.Bounds.Y, 12, 22) : Rectangle.Empty;
                    zeile++;
                    if (zeile == 3) { x += spaltenBreite + 4; zeile = 0; spaltenBreite = 0; }
                }
            }
            if (zeile > 0) x += spaltenBreite + 4;
            // Breite der Kleinbuttons in einer Spalte angleichen
            var textBreite = (int)g.MeasureString(gr.Text, Ui.SchriftKlein).Width + 10;
            if (x - start < textBreite) x = start + textBreite;
            gr.Bounds = new Rectangle(start - 3, TabHoehe + 1, x - start + 4, Inhalt - 2);
            x += 6;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        Layout2(g);
        // Registerleiste in der Modusfarbe (durchgehend mit Titel- und Schnellzugriffsleiste)
        using (var b = new SolidBrush(Ui.Titel)) g.FillRectangle(b, 0, 0, Width, TabHoehe);
        using (var b = new LinearGradientBrush(new Rectangle(0, TabHoehe, Width, Inhalt), Color.FromArgb(250, 251, 252), Ui.BlauSehrHell, 90f)) g.FillRectangle(b, 0, TabHoehe, Width, Inhalt + 1);
        using (var p = new Pen(Color.FromArgb(198, 205, 216))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        if (Rechts.BackColor != Ui.Titel) Rechts.BackColor = Ui.Titel;
        // Datei wie die übrigen Registerkarten (Vorbild 2026): weiße Schrift, beim Überfahren aufgehellt
        if (_hoverDatei) using (var b = new SolidBrush(Color.FromArgb(45, Color.White))) g.FillRectangle(b, _dateiBounds);
        TextRenderer.DrawText(g, "Datei", TabSchrift, _dateiBounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        for (var i = 0; i < Seiten.Count; i++)
        {
            var s = Seiten[i];
            if (!s.IstSichtbar) continue;
            var aktiv = i == AktiveSeite;
            if (aktiv)
            {
                // gewählte Registerkarte hell, geht nahtlos in das Menüband über; oben eine dünne helle Akzentlinie
                using (var b = new SolidBrush(Color.FromArgb(250, 251, 252))) g.FillRectangle(b, s.TabBounds.X, s.TabBounds.Y, s.TabBounds.Width, s.TabBounds.Height + 2);
                using var akzent = new SolidBrush(Akzent);
                g.FillRectangle(akzent, s.TabBounds.X, s.TabBounds.Y, s.TabBounds.Width, 3);
            }
            else if (_hoverTab == s)
                using (var b = new SolidBrush(Color.FromArgb(45, Color.White))) g.FillRectangle(b, s.TabBounds);
            TextRenderer.DrawText(g, s.Text, TabSchrift, Rectangle.FromLTRB(s.TabBounds.X, s.TabBounds.Y + 2, s.TabBounds.Right, s.TabBounds.Bottom), aktiv ? Color.FromArgb(40, 44, 48) : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var seite = Seiten.ElementAtOrDefault(AktiveSeite);
        if (seite is null) return;
        foreach (var gr in seite.Gruppen)
        {
            using (var p = new Pen(Color.FromArgb(215, 220, 228))) g.DrawLine(p, gr.Bounds.Right, gr.Bounds.Y + 4, gr.Bounds.Right, gr.Bounds.Bottom - 4);
            TextRenderer.DrawText(g, gr.Text, Ui.SchriftKlein, new Rectangle(gr.Bounds.X, gr.Bounds.Bottom - 16, gr.Bounds.Width, 14), Color.FromArgb(110, 115, 125),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            foreach (var it in gr.Eintraege)
            {
                var aktiv = it.Aktiviert?.Invoke() ?? true;
                var gedrueckt = it.Gedrueckt?.Invoke() == true;
                if ((it == _hover && aktiv) || gedrueckt)
                {
                    using var path = Ui.AbgerundetesRechteck(it.Bounds, 3);
                    using var b = new SolidBrush(gedrueckt ? Ui.BlauHell : Ui.Hover);
                    using var p = new Pen(Ui.HoverRand);
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                if (it.IstDatum)
                {
                    DatumZeichnen(g, it);
                    continue;
                }
                var farbe = aktiv ? Color.FromArgb(30, 30, 30) : Color.FromArgb(160, 160, 160);
                if (it.Gross)
                {
                    var img = Symbole.Get(it.Symbol, 32);
                    if (aktiv) g.DrawImage(img, it.Bounds.X + (it.Bounds.Width - 32) / 2, it.Bounds.Y + 4, 32, 32);
                    else ControlPaint.DrawImageDisabled(g, img, it.Bounds.X + (it.Bounds.Width - 32) / 2, it.Bounds.Y + 4, Color.Transparent);
                    var tr = new Rectangle(it.Bounds.X, it.Bounds.Y + 38, it.Bounds.Width, 30);
                    TextRenderer.DrawText(g, it.Text, Font, tr, farbe, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    if (it.Menue != null) DreieckUnten(g, it.Bounds.X + it.Bounds.Width / 2, it.Bounds.Bottom - 6, farbe);
                }
                else
                {
                    var img = Symbole.Get(it.Symbol, 16);
                    if (aktiv) g.DrawImage(img, it.Bounds.X + 3, it.Bounds.Y + 3, 16, 16);
                    else ControlPaint.DrawImageDisabled(g, img, it.Bounds.X + 3, it.Bounds.Y + 3, Color.Transparent);
                    TextRenderer.DrawText(g, it.Text, Font, new Rectangle(it.Bounds.X + 22, it.Bounds.Y, it.Bounds.Width - 22, it.Bounds.Height), farbe, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    if (it.Menue != null) DreieckUnten(g, it.Bounds.Right - 6, it.Bounds.Y + 11, farbe);
                }
            }
        }
    }

    private static Rectangle DatumLinks(RibbonEintrag it) => new(it.Bounds.Right - 34, it.Bounds.Y + 3, 16, 16);
    private static Rectangle DatumRechts(RibbonEintrag it) => new(it.Bounds.Right - 17, it.Bounds.Y + 3, 16, 16);

    private void DatumZeichnen(Graphics g, RibbonEintrag it)
    {
        var box = new Rectangle(it.Bounds.X + 2, it.Bounds.Y + 2, it.Bounds.Width - 4, 18);
        g.FillRectangle(Brushes.White, box);
        using (var p = new Pen(Ui.HoverRand)) g.DrawRectangle(p, box);
        var datum = AppZustand.Datum;
        TextRenderer.DrawText(g, $"{Core.Planung.Planungsdienst.TageKurz[((int)datum.DayOfWeek + 6) % 7]} {datum:dd.MM.yyyy}", Font, new Rectangle(box.X + 3, box.Y, box.Width - 38, box.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        var l = DatumLinks(it);
        var r = DatumRechts(it);
        var modus = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var p2 = new Pen(Ui.Blau, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawLines(p2, new[] { new PointF(l.X + 10, l.Y + 4), new PointF(l.X + 6, l.Y + 8), new PointF(l.X + 10, l.Y + 12) });
        g.DrawLines(p2, new[] { new PointF(r.X + 6, r.Y + 4), new PointF(r.X + 10, r.Y + 8), new PointF(r.X + 6, r.Y + 12) });
        g.SmoothingMode = modus;
    }

    private void DatumWaehlen(RibbonEintrag it, Point p)
    {
        var d = AppZustand.D;
        if (DatumLinks(it).Contains(p)) { AppZustand.DatumSetzen(Core.Kalender.NaechsterUnterrichtstag(d, AppZustand.Datum, -1)); Invalidate(); return; }
        if (DatumRechts(it).Contains(p)) { AppZustand.DatumSetzen(Core.Kalender.NaechsterUnterrichtstag(d, AppZustand.Datum)); Invalidate(); return; }
        var kal = new MonthCalendar { MaxSelectionCount = 1, SelectionStart = AppZustand.Datum.ToDateTime(TimeOnly.MinValue), ShowToday = true };
        var host = new ToolStripControlHost(kal) { Margin = Padding.Empty, Padding = Padding.Empty };
        var dd = new ToolStripDropDown { Padding = Padding.Empty };
        dd.Items.Add(host);
        kal.DateSelected += (_, e) => { AppZustand.DatumSetzen(DateOnly.FromDateTime(e.Start)); dd.Close(); Invalidate(); };
        dd.Show(this, new Point(it.Bounds.X, it.Bounds.Bottom));
    }

    private static void DreieckUnten(Graphics g, int x, int y, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillPolygon(b, new[] { new Point(x - 3, y - 1), new Point(x + 4, y - 1), new Point(x, y + 3) });
    }

    private IEnumerable<RibbonEintrag> Sichtbare() => Seiten.ElementAtOrDefault(AktiveSeite)?.Gruppen.SelectMany(g => g.Eintraege) ?? Enumerable.Empty<RibbonEintrag>();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Sichtbare().FirstOrDefault(x => x.Bounds.Contains(e.Location));
        var t = Seiten.FirstOrDefault(s => s.IstSichtbar && s.TabBounds.Contains(e.Location));
        var d = _dateiBounds.Contains(e.Location);
        if (h != _hover || t != _hoverTab || d != _hoverDatei)
        {
            _hover = h;
            _hoverTab = t;
            _hoverDatei = d;
            _tip.SetToolTip(this, h is null ? "" : string.IsNullOrEmpty(h.Hinweis) ? h.Text : $"{h.Text}\n{h.Hinweis}");
            Invalidate();
        }
        Cursor = h != null || t != null || d ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = null;
        _hoverTab = null;
        _hoverDatei = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_dateiBounds.Contains(e.Location))
        {
            MenueZeigen(DateiMenue, new Point(_dateiBounds.X, _dateiBounds.Bottom));
            return;
        }
        for (var i = 0; i < Seiten.Count; i++)
            if (Seiten[i].IstSichtbar && Seiten[i].TabBounds.Contains(e.Location)) { AktiveSeite = i; Invalidate(); return; }
        var it = Sichtbare().FirstOrDefault(x => x.Bounds.Contains(e.Location));
        if (it is null || !(it.Aktiviert?.Invoke() ?? true)) return;
        if (it.IstDatum) { DatumWaehlen(it, e.Location); return; }
        var pfeil = it.Menue != null && (it.Klick is null || it.PfeilBounds.Contains(e.Location));
        if (pfeil) MenueZeigen(it.Menue!, new Point(it.Bounds.X, it.Bounds.Bottom));
        else it.Klick?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var sichtbar = Enumerable.Range(0, Seiten.Count).Where(i => Seiten[i].IstSichtbar).ToList();
        var pos = sichtbar.IndexOf(AktiveSeite);
        if (pos < 0) return;
        AktiveSeite = sichtbar[Math.Clamp(pos + (e.Delta > 0 ? -1 : 1), 0, sichtbar.Count - 1)];
        Invalidate();
    }

    public static ToolStripItem[] MenueEintraege(IEnumerable<RibbonEintrag?> eintraege) =>
        eintraege.Select(x =>
        {
            if (x is null) return (ToolStripItem)new ToolStripSeparator();
            var mi = new ToolStripMenuItem(x.Text, string.IsNullOrEmpty(x.Symbol) ? null : Symbole.Get(x.Symbol, 16)) { ShortcutKeys = x.Tastenkuerzel, Checked = x.Gedrueckt?.Invoke() == true };
            if (x.Menue != null) mi.DropDownItems.AddRange(MenueEintraege(x.Menue));
            if (x.Klick != null) mi.Click += (_, _) => x.Klick();
            if (x.Aktiviert != null) mi.Enabled = x.Aktiviert();
            return mi;
        }).ToArray();

    private void MenueZeigen(IEnumerable<RibbonEintrag?> eintraege, Point p)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.AddRange(MenueEintraege(eintraege));
        cm.Closed += (_, _) => BeginInvoke(() => cm.Dispose());
        cm.Show(this, p);
    }

    /// <summary>Tastenkürzel der Menüeinträge auswerten.</summary>
    public bool TasteVerarbeiten(Keys keys)
    {
        foreach (var e in Seiten.SelectMany(s => s.Gruppen).SelectMany(g => g.Eintraege).Concat(DateiMenue.OfType<RibbonEintrag>()).SelectMany(Flach))
            if (e.Tastenkuerzel != Keys.None && e.Tastenkuerzel == keys && e.Klick != null && (e.Aktiviert?.Invoke() ?? true))
            {
                e.Klick();
                return true;
            }
        return false;
    }

    private static IEnumerable<RibbonEintrag> Flach(RibbonEintrag e)
    {
        yield return e;
        if (e.Menue != null)
            foreach (var x in e.Menue.Where(x => x != null).SelectMany(x => Flach(x!))) yield return x;
    }
}
