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
    /// <summary>Großer Knopf mit Pfeil, der statt eines Menüs den Monatskalender aufklappt (Kalender im Vertretungsmodus).</summary>
    public bool PfeilKalender { get; init; }
    /// <summary>Bleibt auch bei verkleinertem Menüband ein großer Knopf (z. B. „Vertretungsplanung“ wie im Vorbild).</summary>
    public bool ImmerGross { get; init; }
    /// <summary>Menü beim Aufklappen neu erzeugen (z. B. Stundenplanformate „im Menü“); sonst gilt <see cref="Menue"/>.</summary>
    public Func<List<RibbonEintrag?>>? MenueErzeugen { get; init; }
    internal Rectangle Bounds;
    internal Rectangle PfeilBounds;
    /// <summary>Großer Knopf wird aus Platzgründen klein (eine von drei Zeilen) dargestellt.</summary>
    internal bool KleinDargestellt;
    internal bool GrossDargestellt => Gross && !KleinDargestellt;
    internal List<RibbonEintrag?>? AktuellesMenue => MenueErzeugen?.Invoke() ?? Menue;
    /// <summary>Feste Zugriffstaste (KeyTip, Alt); leer = automatisch aus der Beschriftung.</summary>
    public string KeyTip { get; init; } = "";
}

public sealed class RibbonGruppe
{
    public string Text { get; init; } = "";
    public List<RibbonEintrag> Eintraege { get; } = new();
    internal Rectangle Bounds;
    /// <summary>Dialogstarter in der rechten unteren Gruppenecke (öffnet den passenden Einstellungsdialog); null = keiner.</summary>
    public Action? DialogStarter { get; set; }
    /// <summary>Hinweis zum Dialogstarter (Kurzinfo).</summary>
    public string DialogStarterHinweis { get; set; } = "";
    internal Rectangle StarterBounds;
    /// <summary>
    /// Verkleinerungsstufe bei zu schmalem Fenster (wie im Vorbild): 0 = normal, 1 = große Knöpfe klein (drei je Spalte),
    /// 2 = zusammengeklappt zu einem Knopf mit dem Gruppennamen, der alle Einträge als Menü zeigt.
    /// </summary>
    internal int Stufe;
    private RibbonEintrag? _sammel;

    /// <summary>Knopf der zusammengeklappten Gruppe.</summary>
    internal RibbonEintrag Sammel => _sammel ??= new RibbonEintrag
    {
        Text = Text, Gross = true, Symbol = Eintraege.FirstOrDefault(e => e.Gross)?.Symbol ?? Eintraege.FirstOrDefault()?.Symbol ?? "",
        MenueErzeugen = () => Eintraege.Select(e => (RibbonEintrag?)e).ToList(), Hinweis = "Gruppe " + Text,
    };

    /// <summary>Angezeigte Knöpfe (zusammengeklappt nur der Gruppenknopf).</summary>
    internal IEnumerable<RibbonEintrag> Angezeigt => Stufe >= 2 ? new[] { Sammel } : Eintraege;

    /// <summary>Kann zusammengeklappt werden (nicht mit Datumsfeld).</summary>
    internal bool Klappbar => Eintraege.Count > 1 && !Eintraege.Any(e => e.IstDatum);
}

public sealed class RibbonSeite
{
    public string Text { get; init; } = "";
    public List<RibbonGruppe> Gruppen { get; } = new();
    /// <summary>Nur unter dieser Bedingung sichtbar (z. B. Registerkarte nur im Vertretungsmodus).</summary>
    public Func<bool>? Sichtbar { get; init; }
    public bool IstSichtbar => Sichtbar?.Invoke() ?? true;
    internal Rectangle TabBounds;
    /// <summary>Feste Zugriffstaste der Registerkarte (KeyTip); leer = automatisch.</summary>
    public string KeyTip { get; init; } = "";
}

/// <summary>Menüband im Stil klassischer Office-/Windows-Anwendungen.</summary>
public sealed partial class Ribbon : Control
{
    /// <summary>
    /// Registerleiste wie in der Vorlage, in der Modusfarbe und durchgehend mit der Titelzeile darüber: 27 Pixel, die
    /// Registerkarten selbst 24 Pixel hoch und unten bündig (oben 3 Pixel Abstand zur Titelzeile, Kopf gesamt 33 + 27 = 60 Pixel).
    /// </summary>
    public const int TabHoehe = 27;
    /// <summary>Höhe einer Registerkarte (Oberkante bis Menüband).</summary>
    private const int KartenHoehe = 24;
    /// <summary>Oberkante der großen Knöpfe und der ersten Zeile kleiner Knöpfe unter der Menübandlinie (Vorlage: 5 Pixel).</summary>
    private const int KnopfOben = 5;
    /// <summary>Großer Knopf: 66 Pixel hoch (Symbol 32 Pixel 2 Pixel unter der Oberkante, Beschriftung ab 36, Pfeil ab 55).</summary>
    private const int GrossHoehe = 66;
    /// <summary>Kleine Knöpfe: drei Zeilen zu je 22 Pixeln.</summary>
    private const int KleinHoehe = 22;
    /// <summary>Abstand Gruppentrenner → erster Knopf bzw. letzter Knopf → Gruppentrenner (Vorlage: 7 bzw. 5 Pixel).</summary>
    private const int GruppeLinks = 7, GruppeRechts = 5;
    /// <summary>Schrift der Registerkarten (Datei, Start, …) – klein und weiß wie im Vorbild.</summary>
    private static readonly Font TabSchrift = new("Segoe UI", 9f);
    /// <summary>Kleine Knöpfe rechts in der Registerleiste (Fensteranordnungen 1–8).</summary>
    public ToolStrip Rechts { get; } = new()
    {
        GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, AutoSize = true, Padding = new Padding(0),
        ImageScalingSize = new Size(16, 16), CanOverflow = false, Renderer = new LeistenRenderer { Trennfarbe = Color.FromArgb(213, 213, 213) },
    };
    // Fläche unter der Registerleiste wie auf dem Original-Bildschirmfoto: 91 Pixel samt Trennlinie oben
    private const int Inhalt = 92;
    /// <summary>Erweiterte Kurzinfo wie in der Vorlage (Symbol, fette Überschrift, Beschreibung, „Mit F1 erhalten Sie Hilfe.“).</summary>
    private readonly SuperTipp _tip = new();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    private void RechtsPlatzieren()
    {
        var w = Rechts.PreferredSize.Width;
        // wie in der Vorlage: kleine Symbole 3 Pixel vor dem rechten Rand, senkrecht mittig auf den Registerkarten
        Rechts.SetBounds(Math.Max(0, Width - w - 3), 0, w, KartenHoehe);
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

    /// <summary>
    /// Vertretungsmodus wie in der Vorlage: über der Registerkarte „Vertretungsplanung“ ein dunkleres Band (81,132,111) bis zum oberen
    /// Fensterrand, oben 4 Pixel hellgrün (127,202,104) – reicht in die Titelleiste (dort von der Kopfleiste gezeichnet). Leer = kein Band.
    /// </summary>
    public Rectangle KontextBand
    {
        get
        {
            if (!Ui.Vertretungsmodus) return Rectangle.Empty;
            var s = Seiten.FirstOrDefault(x => x.IstSichtbar && x.Text == "Vertretungsplanung");
            return s is null ? Rectangle.Empty : new Rectangle(s.TabBounds.X, 0, s.TabBounds.Width, s.TabBounds.Y);
        }
    }

    /// <summary>Lage des Kontextbands hat sich geändert (die Kopfleiste zeichnet ihren Teil neu).</summary>
    public event Action? KontextBandGeaendert;
    private Rectangle _letztesBand;
    public static readonly Color KontextDunkel = Color.FromArgb(81, 132, 111), KontextHell = Color.FromArgb(127, 202, 104);

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
        // Registerkarten wie im Vorbild (Bild 308 vermessen): „Datei“ mittig bei 29, jede Karte Textbreite (GDI) + 31 Pixel,
        // Kartenmitten im Abstand halbe Textbreiten + 29,3 Pixel („Start“ mittig bei 89, „Vertretungsplanung“ 560 bis 694)
        static int Breite(Graphics g, string t) => TextRenderer.MeasureText(g, t, TabSchrift, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        _dateiBounds = new Rectangle(5, TabHoehe - KartenHoehe, 50, KartenHoehe);
        if (!(Seiten.ElementAtOrDefault(AktiveSeite)?.IstSichtbar ?? false))
            AktiveSeite = Math.Max(0, Seiten.FindIndex(x => x.IstSichtbar));
        double? xd = null;
        foreach (var s in Seiten)
        {
            if (!s.IstSichtbar) { s.TabBounds = Rectangle.Empty; continue; }
            var w = Breite(g, s.Text) + 31;
            xd ??= 89.5 - w / 2.0;
            s.TabBounds = new Rectangle((int)Math.Round(xd.Value), TabHoehe - KartenHoehe, w, KartenHoehe);
            xd += w - 1.7;
        }
        var seite = Seiten.ElementAtOrDefault(AktiveSeite);
        if (seite is null) return;
        // Wie im Vorbild: passt das Menüband nicht in die Fensterbreite, werden Gruppen von rechts nach links verkleinert
        // (große Knöpfe klein) und danach zusammengeklappt (ein Knopf mit dem Gruppennamen und allen Einträgen als Menü)
        foreach (var gr in seite.Gruppen) gr.Stufe = 0;
        var platz = Width - 4;
        if (GruppenAnordnen(g, seite) > platz)
        {
            for (var stufe = 1; stufe <= 2; stufe++)
            {
                for (var i = seite.Gruppen.Count - 1; i >= 0; i--)
                {
                    var gr = seite.Gruppen[i];
                    if (stufe == 1 && (!gr.Eintraege.Any(e => e.Gross) || gr.Eintraege.Any(e => e.IstDatum))) continue;
                    if (stufe == 2 && !gr.Klappbar) continue;
                    gr.Stufe = stufe;
                    if (GruppenAnordnen(g, seite) <= platz) return;
                }
            }
        }
    }

    /// <summary>Selbsttest: rechter Rand der letzten Gruppe und Anzahl verkleinerter bzw. zusammengeklappter Gruppen der aktiven Seite.</summary>
    internal (int Rechts, int Verkleinert, int Zugeklappt) LayoutStand()
    {
        using var g = CreateGraphics();
        Layout2(g);
        var seite = Seiten.ElementAtOrDefault(AktiveSeite);
        if (seite is null || seite.Gruppen.Count == 0) return (0, 0, 0);
        return (seite.Gruppen[^1].Bounds.Right, seite.Gruppen.Count(x => x.Stufe == 1), seite.Gruppen.Count(x => x.Stufe == 2));
    }

    /// <summary>Gruppen der Seite nach ihrer Verkleinerungsstufe anordnen; liefert den rechten Rand der letzten Gruppe (Trennlinie).</summary>
    private int GruppenAnordnen(Graphics g, RibbonSeite seite)
    {
        // Maße wie in der Vorlage (100 %): Trennlinie → Knopf 7 Pixel, Knopf → Trennlinie 5 Pixel; Gruppen mit Datumsfeld
        // (Kalender) enger: Feld 4 Pixel nach dem linken Rand, Trennlinie 3 Pixel danach
        var oben = TabHoehe + KnopfOben;
        var trenner = 0;
        foreach (var gr in seite.Gruppen)
        {
            var mitDatum = gr.Stufe < 2 && gr.Eintraege.Any(e => e.IstDatum);
            var x = trenner + (mitDatum ? 4 : GruppeLinks);
            var start = x;
            var ende = x;
            foreach (var e in gr.Eintraege) e.KleinDargestellt = gr.Stufe == 1 && e.Gross && !e.PfeilKalender && !e.ImmerGross;
            if (gr.Stufe >= 2)
            {
                var sammel = gr.Sammel;
                var sw = Math.Max(48, TextRenderer.MeasureText(sammel.Text, Font).Width + 4);
                sammel.Bounds = new Rectangle(x, oben, sw, GrossHoehe);
                sammel.PfeilBounds = new Rectangle(x, oben + 52, sw, 14);
                foreach (var e in gr.Eintraege) e.Bounds = e.PfeilBounds = Rectangle.Empty;
                ende = x + sw;
            }
            else
            {
                var spaltenBreite = 0;
                var zeile = 0;
                for (var i = 0; i < gr.Eintraege.Count; i++)
                {
                    var e = gr.Eintraege[i];
                    if (e.IstDatum && i > 0 && gr.Eintraege[i - 1] is { GrossDargestellt: true } ueber && zeile == 0)
                    {
                        // wie in der Vorlage: Datumsfeld (92 × 16 Pixel) unter dem großen Kalenderknopf (der Knopf wird etwas niedriger)
                        var bx = ueber.Bounds.X;
                        var w = Math.Max(92, ueber.Bounds.Width);
                        ueber.Bounds = new Rectangle(bx, oben, w, GrossHoehe - 4);
                        if (ueber.PfeilBounds != Rectangle.Empty) ueber.PfeilBounds = new Rectangle(bx, oben + 50, w, 12);
                        e.Bounds = new Rectangle(bx, TabHoehe + 69, w, 22);
                        e.PfeilBounds = Rectangle.Empty;
                        x = bx + w;
                        ende = Math.Max(ende, x);
                        continue;
                    }
                    if (e.GrossDargestellt)
                    {
                        if (zeile > 0) { x += spaltenBreite + 4; zeile = 0; spaltenBreite = 0; }
                        // große Knöpfe eng wie in der Vorlage (Bild 308 gemessen): Beschriftung + 2 Pixel, 2 Pixel Abstand (Text zu Text 6 bis 7 Pixel)
                        // eingedrückte Umschalter („Vertretungsplanung“) im Vorbild mit etwas mehr Rand: Beschriftung + 3 Pixel
                        var w = VorbildBreite.TryGetValue(e.Text, out var vb) ? vb
                            : e.Gedrueckt != null ? Math.Max(40, TextBreite(e.Text) + 3) : Math.Max(40, LayoutBreite(e.Text, 0.995) + 2);
                        e.Bounds = new Rectangle(x, oben, w, GrossHoehe);
                        e.PfeilBounds = HatMenue(e) || e.PfeilKalender ? new Rectangle(x, oben + 52, w, 14) : Rectangle.Empty;
                        ende = x + w;
                        x += w + 2;
                    }
                    else
                    {
                        // kleiner Knopf wie in der Vorlage: Symbol 3 Pixel eingerückt, Text 3 Pixel nach dem Symbol, Pfeil 5 Pixel nach dem Text
                        var tw = TextBreite(e.Text);
                        var w = e.IstDatum ? 136 : VorbildBreite.TryGetValue(e.Text, out var vk) ? vk : KleinText + LayoutBreite(e.Text) + (HatMenue(e) ? 13 : 4);
                        spaltenBreite = Math.Max(spaltenBreite, w);
                        e.Bounds = new Rectangle(x, oben + zeile * KleinHoehe, w, KleinHoehe);
                        e.PfeilBounds = HatMenue(e) && e.Klick != null ? Rectangle.FromLTRB(x + KleinText + tw + 2, e.Bounds.Y, e.Bounds.Right, e.Bounds.Bottom) : Rectangle.Empty;
                        ende = Math.Max(ende, e.Bounds.Right);
                        zeile++;
                        if (zeile == 3) { x += spaltenBreite + 4; zeile = 0; spaltenBreite = 0; }
                    }
                }
            }
            // Gruppe mindestens so breit wie ihre Beschriftung
            var textBreite = TextRenderer.MeasureText(gr.Text, Ui.SchriftKlein).Width + 6 + (StarterSichtbar(gr) ? 16 : 0);
            if (ende - start < textBreite) ende = start + textBreite;
            // eingedrückter Umschalter („Vertretungsplanung“) im Vorbild 4 Pixel vor dem Gruppentrenner
            var rechts = ende + (mitDatum ? 2 : gr.Stufe < 2 && gr.Eintraege.Count > 0 && gr.Eintraege[^1] is { Gedrueckt: not null, GrossDargestellt: true } ? GruppeRechts - 1 : GruppeRechts);
            gr.Bounds = Rectangle.FromLTRB(trenner, TabHoehe + 1, rechts, TabHoehe + Inhalt - 1);
            gr.StarterBounds = StarterSichtbar(gr) ? new Rectangle(rechts - 15, gr.Bounds.Bottom - 17, 13, 13) : Rectangle.Empty;
            trenner = rechts;
        }
        return trenner;
    }

    /// <summary>
    /// Knopfbreiten wie im Vorbild (Bild 308 vermessen, 100 %): große Knöpfe so breit, dass Beschriftung und Pfeil auf denselben
    /// Pixeln mittig stehen wie dort; kleine Knöpfe als Spaltenbreite (Symbol, Text, Pfeil). Damit liegen alle Gruppentrenner der
    /// Registerkarte Vertretungsplanung auf den Pixeln des Vorbilds (98, 391, 523, 795, 902, 1040, 1160), unabhängig davon, wie breit
    /// die Schrift auf dem jeweiligen System läuft. Nicht aufgeführte Beschriftungen werden aus der Textbreite berechnet.
    /// </summary>
    private static readonly Dictionary<string, int> VorbildBreite = new()
    {
        ["Absenzen"] = 52, ["Vertretungen"] = 72, ["Ausgabe"] = 46, ["Vertretungsstatistik"] = 105,
        ["Planungsdialog"] = 82, ["Vormerkungen"] = 80, ["Pausenaufsichten"] = 94, ["Vertretungsplanung"] = 109,
        ["Veranstaltungen"] = 120, ["Klassenplan"] = 95, ["Vertretungsformate"] = 126, ["Info-Stundenplan"] = 126,
    };

    /// <summary>
    /// Mittig gesetzte Beschriftungen (große Knöpfe, Gruppennamen): GDI setzt die Schrift bei waagrechter Zentrierung im Mittel
    /// 1,5 Pixel links der Mitte; das Vorbild zeichnet sie auf der Mitte (Bild 308).
    /// </summary>
    private const int MitteAusgleich = 2;

    /// <summary>Kleiner Knopf: Beginn des Texts (Symbol bei 3, 16 Pixel breit, 3 Pixel Abstand).</summary>
    private const int KleinText = 22;

    internal int TextBreite(string text) => TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

    /// <summary>
    /// Breite einer Beschriftung für die Anordnung: Die Schrift läuft hier je Wort 1 bis 2 Pixel breiter als im Vorbild (Bild 308);
    /// ohne Ausgleich wandern die Gruppentrenner um bis zu 16 Pixel nach rechts. Gezeichnet wird ungekürzt.
    /// </summary>
    private int LayoutBreite(string text, double faktor = 0.99) => (int)Math.Round(TextBreite(text) * faktor);

    private static bool HatMenue(RibbonEintrag e) => e.Menue != null || e.MenueErzeugen != null;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        Layout2(g);
        // Registerleiste in der Modusfarbe (durchgehend mit Titel- und Schnellzugriffsleiste)
        using (var b = new SolidBrush(Ui.Titel)) g.FillRectangle(b, 0, 0, Width, TabHoehe);
        var band = KontextBand;
        if (band.Height > 0) using (var b = new SolidBrush(KontextDunkel)) g.FillRectangle(b, band);
        if (band != _letztesBand) { _letztesBand = band; KontextBandGeaendert?.Invoke(); }
        // Menüband flach in der hellen Modusfarbe wie in der Vorlage (kein Verlauf)
        using (var b = new SolidBrush(Ui.Menueband)) g.FillRectangle(b, 0, TabHoehe, Width, Inhalt + 1);
        // Trennlinien oben und unten wie in der Vorlage: hellgrau (213,213,213)
        using (var p = new Pen(Ui.MenuebandLinie))
        {
            g.DrawLine(p, 0, TabHoehe, Width, TabHoehe);
            g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
        if (Rechts.BackColor != Ui.Titel) Rechts.BackColor = Ui.Titel;
        // Text der Registerkarten wie in der Vorlage: Großbuchstaben senkrecht mittig auf der 24 Pixel hohen Karte
        static Rectangle TextFlaeche(Rectangle r) => new(r.X, r.Y, r.Width, r.Height - 2);
        // Datei wie die übrigen Registerkarten (Vorlage): weiße Schrift, beim Überfahren aufgehellt
        if (_hoverDatei) using (var b = new SolidBrush(Color.FromArgb(45, Color.White))) g.FillRectangle(b, _dateiBounds);
        TextRenderer.DrawText(g, "Datei", TabSchrift, TextFlaeche(_dateiBounds), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        for (var i = 0; i < Seiten.Count; i++)
        {
            var s = Seiten[i];
            if (!s.IstSichtbar) continue;
            var aktiv = i == AktiveSeite;
            var tb = s.TabBounds;
            if (aktiv)
            {
                // gewählte Registerkarte wie in der Vorlage: Menübandfarbe mit hellgrauem Rand oben und seitlich, geht unten
                // nahtlos in das Menüband über; oben die dünne helle Akzentlinie
                using (var b = new SolidBrush(Ui.Menueband)) g.FillRectangle(b, tb.X, tb.Y, tb.Width, tb.Height + 1);
                g.SmoothingMode = SmoothingMode.None;
                using (var p = new Pen(Ui.MenuebandLinie))
                {
                    g.DrawLine(p, tb.X, tb.Y, tb.X, tb.Bottom);
                    g.DrawLine(p, tb.Right - 1, tb.Y, tb.Right - 1, tb.Bottom);
                    // unter dem Kontextband (Vertretungsmodus) wie in der Vorlage nur die hellgraue Kante oben, sonst die Akzentlinie
                    if (band.Height > 0 && band.X == tb.X) g.DrawLine(p, tb.X, tb.Y, tb.Right - 1, tb.Y);
                }
                if (band.Height <= 0 || band.X != tb.X)
                    using (var akzent = new SolidBrush(Akzent)) g.FillRectangle(akzent, tb.X, tb.Y, tb.Width, 3);
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
            else if (_hoverTab == s)
                using (var b = new SolidBrush(Color.FromArgb(45, Color.White))) g.FillRectangle(b, tb);
            TextRenderer.DrawText(g, s.Text, TabSchrift, TextFlaeche(tb), aktiv ? Color.Black : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var seite = Seiten.ElementAtOrDefault(AktiveSeite);
        if (seite is null) return;
        foreach (var gr in seite.Gruppen)
        {
            // Gruppentrenner wie in der Vorlage: 1 Pixel hellgrau, 1 Pixel unter der Menübandlinie bis 3 Pixel über dem Gruppenende
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Ui.MenuebandLinie)) g.DrawLine(p, gr.Bounds.Right, gr.Bounds.Y + 1, gr.Bounds.Right, gr.Bounds.Bottom - 3);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Vertretungsmodus wie in der Vorlage: nur die Gruppe „Stundenpläne“ trägt eine Beschriftung (Unterkante 8 Pixel über der Menübandunterkante)
            if (!Ui.Vertretungsmodus || gr.Text == "Stundenpläne")
                TextRenderer.DrawText(g, gr.Text, Ui.SchriftKlein, new Rectangle(gr.Bounds.X + MitteAusgleich, gr.Bounds.Bottom - 19, gr.Bounds.Width - (gr.StarterBounds.IsEmpty ? 0 : 14), 15), Ui.Vertretungsmodus ? Color.Black : Color.FromArgb(90, 95, 105),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!gr.StarterBounds.IsEmpty) StarterZeichnen(g, gr);
            foreach (var it in gr.Angezeigt)
            {
                var aktiv = it.Aktiviert?.Invoke() ?? true;
                var gedrueckt = it.Gedrueckt?.Invoke() == true;
                if (gedrueckt)
                {
                    // eingedrückt wie „Vertretungsplanung“: kräftige helle Modusfarbe, 1 Pixel Rand in der Titelfarbe, leicht abgerundete
                    // Ecken (Radius 2 wie die übrigen Knöpfe beim Überfahren)
                    using var pfad = Ui.AbgerundetesRechteck(new RectangleF(it.Bounds.X, it.Bounds.Y, it.Bounds.Width - 1, it.Bounds.Height - 1), GedruecktRadius);
                    using (var b = new SolidBrush(Ui.MenuebandGedrueckt)) g.FillPath(b, pfad);
                    using (var p = new Pen(it == _hover && aktiv ? Ui.HoverRand : Ui.Titel)) g.DrawPath(p, pfad);
                }
                else if (it == _hover && aktiv)
                {
                    using var path = Ui.AbgerundetesRechteck(it.Bounds, 3);
                    using var b = new SolidBrush(Ui.Hover);
                    using var p = new Pen(Ui.HoverRand);
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                if (it.IstDatum)
                {
                    DatumZeichnen(g, it);
                    continue;
                }
                // Vorlage: Beschriftungen schwarz, im eingedrückten Knopf („Vertretungsplanung“) dunkelgrau
                var farbe = !aktiv ? Color.FromArgb(160, 160, 160) : gedrueckt ? Color.FromArgb(70, 70, 70) : Color.Black;
                if (it.GrossDargestellt)
                {
                    // Symbol 32 Pixel, 2 Pixel unter der Knopfoberkante; Beschriftung ab 36; Pfeil „˅“ ab 55 (Vorlage)
                    var img = Symbole.Get(it.Symbol, 32);
                    // deaktiviert: offizielle graue FatCow-Variante statt Graufilter
                    g.DrawImage(aktiv ? img : Symbole.Grau(it.Symbol, 32) ?? img, it.Bounds.X + (it.Bounds.Width - 32) / 2, it.Bounds.Y + 2, 32, 32);
                    // Beschriftung mittig, seitlich etwas Luft (die Knopfbreite rechnet mit der engeren Vorbildschrift, siehe LayoutBreite)
                    // eingedrückter Knopf („Vertretungsplanung“): im Vorbild ohne den Ausgleich genau mittig
                    var tr = new Rectangle(it.Bounds.X - 4 + (gedrueckt ? 0 : MitteAusgleich), it.Bounds.Y + 36, it.Bounds.Width + 8, 30);
                    Laufweite.Zeichnen(g, it.Text, Font, tr, farbe, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    if (HatMenue(it) || it.PfeilKalender) Pfeil(g, it.Bounds.X + it.Bounds.Width / 2, it.Bounds.Y + 55, farbe);
                }
                else
                {
                    var img = Symbole.Get(it.Symbol, 16);
                    g.DrawImage(aktiv ? img : Symbole.Grau(it.Symbol, 16) ?? img, it.Bounds.X + 3, it.Bounds.Y + 3, 16, 16);
                    Laufweite.Zeichnen(g, it.Text, Font, new Rectangle(it.Bounds.X + KleinText, it.Bounds.Y, it.Bounds.Width - KleinText, it.Bounds.Height - 1), farbe,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    // Vorbild: Pfeil 6 Pixel nach dem Textende
                    if (HatMenue(it)) Pfeil(g, it.Bounds.X + KleinText + Laufweite.Breite(it.Text, Font) + 7, it.Bounds.Y + 9, farbe);
                }
            }
        }
        KeyTipsZeichnen(g);
    }

    /// <summary>Eckenradius eingedrückter Knöpfe (z. B. „Vertretungsplanung“).</summary>
    internal const float GedruecktRadius = 2.5f;

    private static readonly Color DatumBlau = Color.FromArgb(100, 150, 200);
    private static Rectangle DatumLinks(RibbonEintrag it) => new(it.Bounds.Right - 30, it.Bounds.Y + 4, 14, 14);
    private static Rectangle DatumRechts(RibbonEintrag it) => new(it.Bounds.Right - 15, it.Bounds.Y + 4, 14, 14);

    private void DatumZeichnen(Graphics g, RibbonEintrag it)
    {
        // wie auf dem Original-Bildschirmfoto: blaue Fläche (100,150,200) mit weißem Datum „T.M.JJJJ“, rechts zwei weiße Felder ‹ ›
        var box = new Rectangle(it.Bounds.X, it.Bounds.Y + 3, it.Bounds.Width, 16);
        using (var b = new SolidBrush(DatumBlau)) g.FillRectangle(b, box);
        var datum = AppZustand.Datum;
        TextRenderer.DrawText(g, $"{datum.Day}.{datum.Month}.{datum.Year}", Ui.DialogSchrift, new Rectangle(box.X + 4, box.Y, box.Width - 36, box.Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        foreach (var (r, links) in new[] { (DatumLinks(it), true), (DatumRechts(it), false) })
        {
            g.FillRectangle(Brushes.White, r);
            // kleines schwarzes Dreieck ‹ bzw. › (3 Pixel hoch)
            var mx = r.X + r.Width / 2;
            var my = r.Y + r.Height / 2;
            for (var i = 0; i < 3; i++)
                g.FillRectangle(Brushes.Black, links ? mx - 1 + i : mx + 1 - i, my - i, 1, 2 * i + 1);
        }
    }

    private void DatumWaehlen(RibbonEintrag it, Point p)
    {
        var d = AppZustand.D;
        if (DatumLinks(it).Contains(p)) { AppZustand.DatumSetzen(Core.Kalender.NaechsterUnterrichtstag(d, AppZustand.Datum, -1)); Invalidate(); return; }
        if (DatumRechts(it).Contains(p)) { AppZustand.DatumSetzen(Core.Kalender.NaechsterUnterrichtstag(d, AppZustand.Datum)); Invalidate(); return; }
        KalenderAufklappen(new Point(it.Bounds.X, it.Bounds.Bottom));
    }

    /// <summary>Monatskalender aufklappen (unten „Heute: …“ stellt das Planungsdatum auf den aktuellen Tag).</summary>
    private void KalenderAufklappen(Point p)
    {
        var kal = new MonthCalendar { MaxSelectionCount = 1, SelectionStart = AppZustand.Datum.ToDateTime(TimeOnly.MinValue), ShowToday = true, ShowTodayCircle = true };
        var host = new ToolStripControlHost(kal) { Margin = Padding.Empty, Padding = Padding.Empty };
        var dd = new ToolStripDropDown { Padding = Padding.Empty };
        dd.Items.Add(host);
        kal.DateSelected += (_, e) => { AppZustand.DatumSetzen(DateOnly.FromDateTime(e.Start)); dd.Close(); Invalidate(); };
        dd.Closed += (_, _) => BeginInvoke(dd.Dispose);
        dd.Show(this, p);
    }

    /// <summary>Schmaler Aufklapppfeil „˅“ wie in der Vorlage: 6 Pixel breit, 4 Pixel hoch, Spitze unten (x = Mitte, y = Oberkante).</summary>
    /// <summary>
    /// Pfeil „˅“ wie im Vorbild pixelweise (Bild 308): 6 Pixel breit und 4 hoch, Kern (72, 72, 72), weiche Kanten mit 30 % Deckung;
    /// <paramref name="x"/> ist die rechte der beiden Mittelspalten, <paramref name="y"/> die oberste Zeile.
    /// </summary>
    internal static void Pfeil(Graphics g, int x, int y, Color c)
    {
        var alt = g.SmoothingMode;
        // ganze Pixel (mit Kantenglättung würde jedes 1-Pixel-Rechteck auf zwei Pixel verschmiert)
        g.SmoothingMode = SmoothingMode.None;
        var kern = c.ToArgb() == Color.Black.ToArgb() ? Color.FromArgb(72, 72, 72) : c;
        using var voll = new SolidBrush(kern);
        using var weich = new SolidBrush(Color.FromArgb(76, kern));
        var l = x - 3;
        void P(Brush b, int dx, int dy) => g.FillRectangle(b, l + dx, y + dy, 1, 1);
        P(voll, 0, 0); P(weich, 1, 0); P(weich, 4, 0); P(voll, 5, 0);
        P(weich, 0, 1); P(voll, 1, 1); P(weich, 2, 1); P(weich, 3, 1); P(voll, 4, 1); P(weich, 5, 1);
        P(weich, 1, 2); P(voll, 2, 2); P(voll, 3, 2); P(weich, 4, 2);
        P(weich, 2, 3); P(weich, 3, 3);
        g.SmoothingMode = alt;
    }

    private IEnumerable<RibbonEintrag> Sichtbare() => Seiten.ElementAtOrDefault(AktiveSeite)?.Gruppen.SelectMany(g => g.Angezeigt) ?? Enumerable.Empty<RibbonEintrag>();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = Sichtbare().FirstOrDefault(x => x.Bounds.Contains(e.Location));
        var starter = AktiveGruppen().FirstOrDefault(x => x.StarterBounds.Contains(e.Location));
        if (starter != _hoverStarter)
        {
            _hoverStarter = starter;
            if (starter != null) _tip.Setzen(this, starter.Text, string.IsNullOrEmpty(starter.DialogStarterHinweis) ? "Dialog mit den Einstellungen zu „" + starter.Text + "“ öffnen." : starter.DialogStarterHinweis);
            else if (h is null) _tip.Setzen(this, "");
            Invalidate();
        }
        var t = Seiten.FirstOrDefault(s => s.IstSichtbar && s.TabBounds.Contains(e.Location));
        var d = _dateiBounds.Contains(e.Location);
        if (h != _hover || t != _hoverTab || d != _hoverDatei)
        {
            _hover = h;
            _hoverTab = t;
            _hoverDatei = d;
            if (h is null) { if (starter is null) _tip.Setzen(this, ""); }
            else _tip.Setzen(this, h.Text, h.Hinweis, string.IsNullOrEmpty(h.Symbol) ? null : Symbole.Get(h.Symbol, 16));
            Invalidate();
        }
        Cursor = h != null || t != null || d || starter != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = null;
        _hoverTab = null;
        _hoverDatei = false;
        _hoverStarter = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        KeyTipsBeenden();
        if (e.Button != MouseButtons.Left) return;
        if (AktiveGruppen().FirstOrDefault(x => x.StarterBounds.Contains(e.Location)) is { DialogStarter: { } starter })
        {
            starter();
            return;
        }
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
        if (it.PfeilKalender && it.PfeilBounds.Contains(e.Location)) { KalenderAufklappen(new Point(it.Bounds.X, it.Bounds.Bottom)); return; }
        var menue = it.AktuellesMenue;
        var pfeil = menue != null && (it.Klick is null || it.PfeilBounds.Contains(e.Location));
        if (pfeil) MenueZeigen(menue!, new Point(it.Bounds.X, it.Bounds.Bottom));
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
            if (!string.IsNullOrEmpty(x.Hinweis)) mi.ToolTipText = x.Hinweis;
            if (x.AktuellesMenue is { } untermenue) mi.DropDownItems.AddRange(MenueEintraege(untermenue));
            if (x.Klick != null) mi.Click += (_, _) => x.Klick();
            if (x.Aktiviert != null) mi.Enabled = x.Aktiviert();
            return mi;
        }).ToArray();

    /// <summary>Dateimenü öffnen (Selbsttest: Bildschirmfoto des Menüs).</summary>
    internal ContextMenuStrip DateiMenueOeffnen() => MenueZeigen(DateiMenue, new Point(_dateiBounds.X, _dateiBounds.Bottom));

    private ContextMenuStrip MenueZeigen(IEnumerable<RibbonEintrag?> eintraege, Point p)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.AddRange(MenueEintraege(eintraege));
        cm.Closed += (_, _) => BeginInvoke(() => cm.Dispose());
        cm.Show(this, p);
        return cm;
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
