using System.Drawing.Drawing2D;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Manuelles Planen mit der Maus wie im Handbuch (Stundenplan Kap. 5.2, Vertretungsplanung Kap. 3.8.2 und 8):
/// <list type="bullet">
/// <item>Farbfelder beim Ziehen: Grünschattierungen der freien Felder (je dunkler, desto besser), rot = Sperrung bzw. deutliche
/// Verschlechterung, violett = Raum belegt, weiß = Kollision, grün hinterlegte Stunde = 2-er-Tausch, das Feld unter der Maus kräftig grün.</item>
/// <item>Blauer Pfeil links oben in einer Stunde: Tauschkette möglich; über einer solchen Stunde zeigen rote Pfeile die Kette.</item>
/// <item>Vorschläge für Hohlstunden: Maustaste auf einer freien Position mindestens 2 Sekunden gedrückt halten – Stunden, die dorthin
/// verlegt werden können, werden grün hinterlegt.</item>
/// <item>Strg+Klick: Doppelstunden und Blöcke einzeln ziehen bzw. alle parallelen Unterrichte einer Stunde markieren
/// („DragDrop: mehrere Unterrichte“ dauerhaft).</item>
/// <item>Ziehen in die Stundenlupe: schwarzes X als Mauszeiger (Entplanen bzw. Entfall).</item>
/// </list>
/// </summary>
public sealed partial class PlanAnsicht
{
    /// <summary>Farben der Felder beim Ziehen wie in den Handbuch-Abbildungen (Stundenplanmodus).</summary>
    public static readonly Color[] ZiehGruen =
    {
        Color.FromArgb(127, 254, 127), Color.FromArgb(159, 254, 192), Color.FromArgb(191, 255, 208), Color.FromArgb(223, 255, 224),
    };
    /// <summary>Feld unter der Maus (Ziel) beim Ziehen.</summary>
    public static readonly Color ZiehZiel = Color.FromArgb(0, 255, 0);
    /// <summary>Rot: kollisionsfrei, aber Sperrung (Zeitwunsch -3) bzw. deutliche Verschlechterung.</summary>
    public static readonly Color ZiehRot = Color.FromArgb(255, 210, 210);
    /// <summary>Violett: kollisionsfrei, aber weder Raum noch Ausweichraum frei.</summary>
    public static readonly Color ZiehViolett = Color.FromArgb(255, 160, 255);
    /// <summary>Grün hinterlegte Stunde: 2-er-Tausch möglich.</summary>
    public static readonly Color ZiehTausch = Color.FromArgb(127, 254, 127);
    /// <summary>Blauer Pfeil (Tauschkette) links oben in der Stunde.</summary>
    public static readonly Color KettenBlau = Color.FromArgb(40, 110, 220);

    /// <summary>Legende der Farben beim Ziehen (für die Anzeige neben dem Plan und die Hilfe).</summary>
    public static IReadOnlyList<(Color Farbe, string Text)> ZiehLegende(bool tagesmodus) => tagesmodus
        ? new[]
        {
            (Color.FromArgb(160, 245, 190), "Verlegung frei"),
            (Color.FromArgb(120, 230, 120), "Tausch möglich"),
            (ZiehViolett, "Raum belegt"),
            (Ui.Rot, "Vorgabe verletzt"),
            (Color.White, "Kollision (verdrängen)"),
        }
        : new[]
        {
            (ZiehGruen[0], "frei (dunkler = besser)"),
            (ZiehTausch, "2-er-Tausch möglich"),
            (KettenBlau, "Tauschkette möglich"),
            (ZiehRot, "Sperrung / schlechter"),
            (ZiehViolett, "Raum belegt"),
            (Color.White, "Kollision"),
        };

    /// <summary>Wird gezogen (true) bzw. ist das Ziehen beendet (false) – für die Farblegende.</summary>
    public event Action<bool>? ZiehenAktivGeaendert;

    // Grünschattierung je Feld (0 = am besten), Tauschketten je Feld und die beste Kette unter der Maus
    private Dictionary<(int Tag, int Stunde), int> _guete = new();
    private bool[,]? _kette;
    private Kettentausch? _ketteUnterMaus;
    private (int Tag, int Stunde)? _ketteFeld;
    private List<Planstunde> _ziehBlock = new();

    /// <summary>Doppelstunden und Blöcke werden einzeln angezeigt und gezogen (Format „Doppelstunden wie Einzelstunden“ oder Strg+Klick).</summary>
    public bool EinzelAnzeige => Format.Einzelstunden || _einzelZiehen;
    private bool _einzelZiehen;

    /// <summary>Strg+Klick auf eine Doppelstunde bzw. einen Block: Stunden einzeln ziehen (bis zum nächsten Klick in eine leere Zelle).</summary>
    public bool EinzelZiehen
    {
        get => _einzelZiehen;
        set { if (_einzelZiehen == value) return; _einzelZiehen = value; Invalidate(); }
    }

    /// <summary>
    /// Mitgezogene Stunden beim Ziehen von <paramref name="p"/>: die markierten Stunden (Strg+Klick), sonst die Stunde bzw. der ganze Block –
    /// mit „DragDrop: mehrere Unterrichte“ zusätzlich alle parallelen Unterrichte dieser Stunde.
    /// </summary>
    internal List<Planstunde> Mitziehen(Planstunde p)
    {
        if (Mehrfach.Count > 1 && Mehrfach.Contains(p)) return Mehrfach.ToList();
        var basis = EinzelAnzeige ? new List<Planstunde> { p } : Block(p);
        if (!Format.DragDropMehrereUnterrichte) return basis;
        var parallel = StundenAm(p.Tag, p.Stunde).Where(x => x != p).ToList();
        if (parallel.Count == 0) return basis;
        var alle = new List<Planstunde>(basis);
        foreach (var x in parallel)
            foreach (var y in EinzelAnzeige ? new List<Planstunde> { x } : Block(x))
                if (!alle.Contains(y)) alle.Add(y);
        return alle;
    }

    /// <summary>
    /// Strg+Klick auf eine Stunde (Handbuch 5.2.4/5.2.8): liegen in der Stunde mehrere Unterrichte, werden alle markiert; gehört die Stunde
    /// zu einer Doppelstunde bzw. einem Block, wird sie einzeln gezogen.
    /// </summary>
    private void StrgKlick(Planstunde p)
    {
        var parallel = StundenAm(p.Tag, p.Stunde);
        if (parallel.Count > 1 && Mehrfach.Contains(p))
            foreach (var x in parallel)
                if (!Mehrfach.Contains(x)) Mehrfach.Add(x);
        if (Block(p).Count > 1) EinzelZiehen = true;
    }

    // ------------------------------------------------------------------------------------------
    // Farbfelder
    // ------------------------------------------------------------------------------------------

    /// <summary>Farbe eines freien (grünen) Feldes: Schattierung nach Eignung, das Ziel unter der Maus kräftig grün.</summary>
    private Color FreiFarbe(int t, int s)
    {
        if (_hover is { } h && h.Tag == t && s >= h.Stunde - _ziehVersatz && s < h.Stunde - _ziehVersatz + Math.Max(1, _ziehLaenge)) return ZiehZiel;
        return _guete.TryGetValue((t, s), out var g) ? ZiehGruen[Math.Clamp(g, 0, ZiehGruen.Length - 1)] : ZiehGruen[0];
    }

    /// <summary>Farbe eines Feldes beim Ziehen im Stundenplanmodus (Handbuch-Farben).</summary>
    private Color ZiehFarbe(int t, int s, Feldstatus st) => st switch
    {
        Feldstatus.Frei => FreiFarbe(t, s),
        Feldstatus.VorgabeVerletzt => ZiehRot,
        Feldstatus.KeinRaum => ZiehViolett,
        Feldstatus.TauschMoeglich => ZiehTausch,
        _ => Color.White,
    };

    /// <summary>Zusätzliche Auswertung beim Beginn des Ziehens: Grünschattierung und Tauschketten (nur Stundenplanmodus, ein Unterricht).</summary>
    private void ZiehZusatzBerechnen(string s)
    {
        _guete = new();
        _kette = null;
        _ketteUnterMaus = null;
        _ketteFeld = null;
        _ziehBlock = new();
        if (Tagesmodus || _status is null) return;
        try
        {
            Unterricht? u = null;
            Planstunde? bewegt = null;
            if (s.StartsWith("plan:"))
            {
                var (prim, alle) = PlanDaten(s);
                if (prim is null || alle.Any(x => x.UnterrichtId != prim.UnterrichtId || x.Tag != prim.Tag)) return;
                u = Ix.Unterrichte.GetValueOrDefault(prim.UnterrichtId);
                _ziehBlock = alle.OrderBy(x => x.Stunde).ToList();
                bewegt = _ziehBlock[0];
            }
            else if (s.StartsWith("unterricht:"))
            {
                var (ids, _) = Fenster.StundenplanFenster.UnterrichtDaten(s);
                if (ids.Count != 1) return;
                u = Ix.Unterrichte.GetValueOrDefault(ids[0]);
            }
            if (u is null) return;
            var felder = new List<(int, int)>();
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    if (_status[t, st - 1] == Feldstatus.Frei) felder.Add((t, st - _ziehVersatz));
            var bewertung = Ziehbewertung.Gruenschattierung(D, u, bewegt, felder);
            foreach (var ((t, start), g) in bewertung) _guete[(t, start + _ziehVersatz)] = g;
            // Tauschketten: nur für belegte Stunden dieses Plans, mit denen kein 2-er-Tausch (bzw. zusätzlich zum Tausch) möglich ist
            if (_ziehBlock.Count > 0 && Format.DragDropTauschketten)
            {
                _kette = new bool[Tage, Stunden];
                var belegt = Ix.PlanVon(Typ, ElementId).Where(p => !_ziehBlock.Contains(p)).Select(p => (p.Tag, p.Stunde)).ToHashSet();
                for (var t = 0; t < Tage; t++)
                    for (var st = 1; st <= Stunden; st++)
                        if (belegt.Contains((t, st)) && _status[t, st - 1] is Feldstatus.Kollision or Feldstatus.TauschMoeglich)
                            _kette[t, st - 1] = Ziehbewertung.KettePruefen(D, Ix, _ziehBlock, t, st - _ziehVersatz, false) != null;
            }
        }
        catch (Exception)
        {
            // die Zusatzanzeige ist nur eine Hilfe: bei Fehlern bleibt es bei den Grundfarben
            _guete = new();
            _kette = null;
        }
    }

    /// <summary>Tauschkette unter der Maus (für die roten Pfeile); wird nur bei einem Feldwechsel neu berechnet.</summary>
    private void KetteUnterMausAktualisieren()
    {
        if (_kette is null || _hover is not { } h || h.Stunde < 1 || h.Stunde > Stunden || !_kette[h.Tag, h.Stunde - 1]) { _ketteUnterMaus = null; _ketteFeld = null; return; }
        if (_ketteFeld == h) return;
        _ketteFeld = h;
        try { _ketteUnterMaus = Ziehbewertung.KettePruefen(D, Ix, _ziehBlock, h.Tag, h.Stunde - _ziehVersatz); }
        catch (Exception) { _ketteUnterMaus = null; }
    }

    /// <summary>Status „Tauschkette möglich“ eines Feldes beim Ziehen (Selbsttest).</summary>
    internal bool KetteMoeglich(int tag, int stunde) => _kette is { } k && tag >= 0 && tag < Tage && stunde >= 1 && stunde <= Stunden && k[tag, stunde - 1];

    /// <summary>Grünstufe eines freien Feldes beim Ziehen (0 = am besten, -1 = keine Bewertung) – Selbsttest.</summary>
    internal int Gruenstufe(int tag, int stunde) => _guete.TryGetValue((tag, stunde), out var g) ? g : -1;

    private void ZiehenAktivMelden(bool aktiv) => ZiehenAktivGeaendert?.Invoke(aktiv);

    /// <summary>Blaue Pfeile (Tauschkette), rote Kettenpfeile unter der Maus und die Hohlstunden-Vorschläge zeichnen.</summary>
    private void ZiehZusatzZeichnen(Graphics g, Rectangle bounds)
    {
        var alt = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            if (_kette is { } k && _status != null)
                for (var t = 0; t < Tage; t++)
                    for (var s = 1; s <= Stunden; s++)
                        if (k[t, s - 1]) BlauerPfeil(g, Zelle(bounds, t, s));
            if (_ketteUnterMaus is { } kt && _ziehQuelle is { } q)
            {
                var n = Math.Max(1, _ziehLaenge);
                var a = Bereich(bounds, q.Tag, q.Stunde, q.Stunde + n - 1);
                var b = Bereich(bounds, kt.ZielTag, kt.ZielStunde, kt.ZielStunde + n - 1);
                var c = Bereich(bounds, kt.PartnerTag, kt.PartnerStunde, kt.PartnerStunde + kt.PartnerLaenge - 1);
                using var rot = new Pen(Color.FromArgb(230, 0, 0), Rahmen);
                foreach (var r in new[] { a, b, c })
                    if (r.Width > 0) g.DrawRectangle(rot, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
                if (a.Width > 0 && b.Width > 0) RoterPfeil(g, a, b);
                if (b.Width > 0 && c.Width > 0) RoterPfeil(g, b, c);
            }
            FremdeVorschauZeichnen(g, bounds);
            if (_hohlVorschlaege.Count > 0 && !Tagesmodus)
            {
                using var gruen = new SolidBrush(Color.FromArgb(150, ZiehTausch));
                foreach (var p in _hohlVorschlaege)
                    if (p.Tag >= 0 && p.Tag < Tage && p.Stunde >= 1 && p.Stunde <= Stunden) g.FillRectangle(gruen, Zelle(bounds, p.Tag, p.Stunde));
                if (_hohlZiel is { } hz)
                {
                    var r = Zelle(bounds, hz.Tag, hz.Stunde);
                    using var p = new Pen(Color.FromArgb(230, 0, 0), Rahmen) { DashStyle = DashStyle.Dash };
                    g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                }
            }
        }
        finally { g.SmoothingMode = alt; }
    }

    /// <summary>Kleiner blauer Pfeil im linken oberen Eck einer Stunde (Tauschkette möglich).</summary>
    private static void BlauerPfeil(Graphics g, RectangleF r)
    {
        if (r.Width < 10 || r.Height < 8 || r.X < -1000) return;
        var gr = Math.Min(9f, Math.Min(r.Width, r.Height) * 0.55f);
        var x = r.X + 2;
        var y = r.Y + 2;
        using var b = new SolidBrush(KettenBlau);
        // Pfeil nach links oben: Spitze im Eck, Schaft nach rechts unten
        g.FillPolygon(b, new[] { new PointF(x, y), new PointF(x + gr * 0.75f, y), new PointF(x, y + gr * 0.75f) });
        using var p = new Pen(KettenBlau, Math.Max(1.5f, gr / 4.5f));
        g.DrawLine(p, x + gr * 0.25f, y + gr * 0.25f, x + gr, y + gr);
    }

    /// <summary>Roter Pfeil von einem Feld zum anderen (geplante Verschiebung).</summary>
    private static void RoterPfeil(Graphics g, RectangleF von, RectangleF nach)
    {
        var pa = new PointF(von.X + von.Width / 2, von.Y + von.Height / 2);
        var pb = new PointF(nach.X + nach.Width / 2, nach.Y + nach.Height / 2);
        if (Math.Abs(pa.X - pb.X) < 2 && Math.Abs(pa.Y - pb.Y) < 2) return;
        var ende = RandPunkt(nach, pa);
        using var saum = new Pen(Color.FromArgb(220, Color.White), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var spitze = new AdjustableArrowCap(3.2f, 3.6f, true);
        using var rot = new Pen(Color.FromArgb(230, 0, 0), 2.5f) { StartCap = LineCap.Round, CustomEndCap = spitze };
        g.DrawLine(saum, pa, ende);
        g.DrawLine(rot, pa, ende);
    }

    // ------------------------------------------------------------------------------------------
    // Ziehen im Planungsdialog: geplante Verschiebung in den Stundenplänen mit roten Pfeilen (Handbuch 5.4.2.4)
    // ------------------------------------------------------------------------------------------

    private static (int UnterrichtId, int VonTag, int VonStunde, int NachTag, int NachStunde)? _fremdeVorschau;
    private static event Action? FremdeVorschauGeaendert;

    /// <summary>Gezogene Stunde im Planungsdialog (null = es wird nicht gezogen): alle Regelpläne mit dieser Stunde zeigen den Pfeil.</summary>
    public static void FremdeVorschauSetzen((int UnterrichtId, int VonTag, int VonStunde, int NachTag, int NachStunde)? vorschau)
    {
        if (_fremdeVorschau == vorschau) return;
        _fremdeVorschau = vorschau;
        FremdeVorschauGeaendert?.Invoke();
    }

    /// <summary>Zeigt dieser Plan die Pfeilvorschau aus dem Planungsdialog (Selbsttest)?</summary>
    internal bool FremdeVorschauSichtbar => !Tagesmodus && _fremdeVorschau is { } v && Ix.PlanVon(Typ, ElementId).Any(p => p.UnterrichtId == v.UnterrichtId && p.Tag == v.VonTag && p.Stunde == v.VonStunde);

    private void FremdeVorschauNeu() { if (IsHandleCreated && !IsDisposed) Invalidate(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FremdeVorschauGeaendert += FremdeVorschauNeu;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        FremdeVorschauGeaendert -= FremdeVorschauNeu;
        base.OnHandleDestroyed(e);
    }

    private void FremdeVorschauZeichnen(Graphics g, Rectangle bounds)
    {
        if (!FremdeVorschauSichtbar || _fremdeVorschau is not { } v) return;
        var a = Bereich(bounds, v.VonTag, v.VonStunde, v.VonStunde);
        var b = Bereich(bounds, v.NachTag, v.NachStunde, v.NachStunde);
        if (a.Width <= 0 || b.Width <= 0 || (v.VonTag == v.NachTag && v.VonStunde == v.NachStunde)) return;
        using var rot = new Pen(Color.FromArgb(230, 0, 0), Rahmen);
        g.DrawRectangle(rot, a.X + 0.5f, a.Y + 0.5f, a.Width - 2, a.Height - 2);
        g.DrawRectangle(rot, b.X + 0.5f, b.Y + 0.5f, b.Width - 2, b.Height - 2);
        RoterPfeil(g, a, b);
    }

    // ------------------------------------------------------------------------------------------
    // Vorschläge für Hohlstunden (Maustaste 2 Sekunden gedrückt)
    // ------------------------------------------------------------------------------------------

    /// <summary>Haltezeit der Maustaste für die Hohlstunden-Vorschläge (Handbuch: mindestens 2 Sekunden).</summary>
    public const int HohlstundenHaltezeit = 2000;
    private System.Windows.Forms.Timer? _hohlTimer;
    private (int Tag, int Stunde)? _hohlZiel;
    private (int Tag, int Stunde)? _hohlKandidat;
    private List<Planstunde> _hohlVorschlaege = new();

    /// <summary>Gefundene Vorschläge für die gehaltene Position (Selbsttest).</summary>
    internal IReadOnlyList<Planstunde> HohlstundenVorschlaege => _hohlVorschlaege;

    /// <summary>Maustaste auf einer leeren Zelle gedrückt: nach 2 Sekunden die Vorschläge anzeigen.</summary>
    private void HohlstundenBeginnen((int Tag, int Stunde)? zelle)
    {
        HohlstundenEnde();
        if (Tagesmodus || !Bearbeitbar || zelle is null || Typ is ElementTyp.Fach) return;
        _hohlKandidat = zelle;
        _hohlTimer ??= new System.Windows.Forms.Timer { Interval = HohlstundenHaltezeit };
        _hohlTimer.Tick -= HohlTick;
        _hohlTimer.Tick += HohlTick;
        _hohlTimer.Start();
    }

    private void HohlTick(object? sender, EventArgs e)
    {
        _hohlTimer?.Stop();
        if (_hohlKandidat is not { } z || (MouseButtons & MouseButtons.Left) == 0) return;
        HohlstundenZeigen(z.Tag, z.Stunde);
    }

    /// <summary>Vorschläge für die freie Position (tag, stunde) berechnen und grün hinterlegen (auch für Selbsttest/Tastatur).</summary>
    internal void HohlstundenZeigen(int tag, int stunde)
    {
        _hohlZiel = (tag, stunde);
        try { _hohlVorschlaege = Ziehbewertung.Hohlstundenvorschlaege(D, Ix, Typ, ElementId, tag, stunde).Where(p => Ix.PlanVon(Typ, ElementId).Contains(p)).ToList(); }
        catch (Exception) { _hohlVorschlaege = new(); }
        Invalidate();
    }

    /// <summary>Vorschläge ausblenden (nächster Mausklick, Ende des Ziehens).</summary>
    internal void HohlstundenEnde()
    {
        _hohlTimer?.Stop();
        _hohlKandidat = null;
        if (_hohlZiel is null && _hohlVorschlaege.Count == 0) return;
        _hohlZiel = null;
        _hohlVorschlaege = new();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        // Haltezeit nicht erreicht: keine Vorschläge; erreicht: die grünen Stunden bleiben bis zum nächsten Klick sichtbar
        _hohlTimer?.Stop();
        _hohlKandidat = null;
    }

    // ------------------------------------------------------------------------------------------
    // Mauszeiger beim Ziehen in die Stundenlupe (Entplanen / Entfall)
    // ------------------------------------------------------------------------------------------

    /// <summary>Die Maus steht beim Ziehen über der Stundenlupe: statt des Mauszeigers erscheint ein schwarzes X.</summary>
    public bool EntplanenZiel { get; set; }

    private static Cursor? _xCursor;

    /// <summary>Schwarzes X als Mauszeiger (Handbuch 5.2.2: Entplanen über die Lupe).</summary>
    internal static Cursor XCursor
    {
        get
        {
            if (_xCursor != null) return _xCursor;
            try
            {
                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using var weiss = new Pen(Color.White, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    using var schwarz = new Pen(Color.Black, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    foreach (var p in new[] { weiss, schwarz })
                    {
                        g.DrawLine(p, 6, 6, 22, 22);
                        g.DrawLine(p, 22, 6, 6, 22);
                    }
                }
                _xCursor = new Cursor(bmp.GetHicon());
            }
            catch (Exception) { _xCursor = Cursors.No; }
            return _xCursor;
        }
    }

    protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);
        if (EntplanenZiel && e.Effect != DragDropEffects.None)
        {
            e.UseDefaultCursors = false;
            System.Windows.Forms.Cursor.Current = XCursor;
        }
    }
}

/// <summary>
/// Legende der Farben beim Ziehen (Handbuch Stundenplan Kap. 5.2.1–5.2.5): wird neben dem Stundenplan eingeblendet, solange eine
/// Stunde gezogen wird.
/// </summary>
public sealed class ZiehLegende : Control
{
    private IReadOnlyList<(Color Farbe, string Text)> _eintraege = PlanAnsicht.ZiehLegende(false);
    private const int Zeile = 16;

    public ZiehLegende()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = Ui.SchriftKlein;
        Visible = false;
        Groesse();
    }

    /// <summary>Einträge für den Stundenplan- bzw. Vertretungsmodus setzen.</summary>
    public void Modus(bool tagesmodus)
    {
        _eintraege = PlanAnsicht.ZiehLegende(tagesmodus);
        Groesse();
        Invalidate();
    }

    /// <summary>Angezeigte Texte (Selbsttest).</summary>
    internal IReadOnlyList<string> Texte => _eintraege.Select(e => e.Text).ToList();

    private void Groesse() => Size = new Size(146, 18 + _eintraege.Count * Zeile + 4);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(252, 252, 252));
        g.DrawRectangle(Pens.Gray, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, "Legende", Ui.SchriftFett, new Rectangle(4, 2, Width - 8, 15), Color.Black, TextFormatFlags.NoPrefix);
        for (var i = 0; i < _eintraege.Count; i++)
        {
            var (farbe, text) = _eintraege[i];
            var y = 18 + i * Zeile;
            var kasten = new Rectangle(4, y + 2, 12, 11);
            if (farbe == PlanAnsicht.KettenBlau)
            {
                g.FillRectangle(Brushes.White, kasten);
                using var b = new SolidBrush(farbe);
                g.FillPolygon(b, new[] { new Point(kasten.X + 1, kasten.Y + 1), new Point(kasten.X + 8, kasten.Y + 1), new Point(kasten.X + 1, kasten.Y + 8) });
                using var p = new Pen(farbe, 2f);
                g.DrawLine(p, kasten.X + 3, kasten.Y + 3, kasten.Right - 1, kasten.Bottom - 1);
            }
            else
            {
                using var b = new SolidBrush(farbe);
                g.FillRectangle(b, kasten);
            }
            g.DrawRectangle(Pens.DimGray, kasten);
            TextRenderer.DrawText(g, text, Font, new Rectangle(20, y, Width - 22, Zeile), Color.Black, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }
    }
}
