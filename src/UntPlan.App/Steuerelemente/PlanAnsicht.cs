using System.Drawing.Drawing2D;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Gezeichneter Einzel-Stundenplan eines Elements.
/// Regelplan: manuelles Planen per Drag &amp; Drop (Doppelstunden und Blöcke werden als Ganzes gezogen, Mehrfachauswahl mit Strg),
/// Farben: grün = kollisionsfrei, rot = Vorgabe verletzt, violett = kein Raum frei, grün hinterlegte Stunde = Tausch möglich.
/// Tagesaktueller Plan (Vertretungsmodus): Änderungen rot, Entfälle durchgestrichen, Verlegen und Tauschen per Drag &amp; Drop.
/// </summary>
public sealed class PlanAnsicht : Control
{
    public ElementTyp Typ { get; set; } = ElementTyp.Klasse;
    public int ElementId { get; set; }
    public StundenplanFormat Format { get; set; } = new();
    /// <summary>Montag der angezeigten Woche (tagesaktueller Plan mit Vertretungen) oder null für den Regelplan.</summary>
    public DateOnly? Woche
    {
        get => _woche;
        set { _woche = value; TageNeu(); }
    }
    public bool Tagesmodus => _woche.HasValue;

    /// <summary>Zeitbereich „Tag“ wie in Untis: nur dieser Wochentag (Index) wird über die ganze Breite gezeigt, null = alle Tage.</summary>
    public int? NurTag
    {
        get => _nurTag;
        set { _nurTag = value is int t && t >= 0 && t < Tage ? t : null; Invalidate(); }
    }
    private int? _nurTag;
    private int SichtbareTage => _nurTag.HasValue ? 1 : Tage;

    /// <summary>
    /// Gewählte Stunde. Die Auswahl wird zusätzlich über Unterricht, Tag und Stunde gemerkt, damit sie nach dem Neuladen der Daten
    /// (Rückgängig, Änderungen in anderen Fenstern) wieder gefunden wird.
    /// </summary>
    public Planstunde? Ausgewaehlt
    {
        get => _ausgewaehlt;
        set
        {
            _ausgewaehlt = value;
            _auswahlSchluessel = value is null ? null : (value.UnterrichtId, value.Tag, value.Stunde);
        }
    }
    private Planstunde? _ausgewaehlt;
    private (int Unterricht, int Tag, int Stunde)? _auswahlSchluessel;

    /// <summary>Zusätzlich markierte Stunden (Strg+Klick) – werden gemeinsam verschoben.</summary>
    public List<Planstunde> Mehrfach { get; } = new();

    /// <summary>Verknüpfte Markierung: alle Stunden dieses Unterrichts werden gestrichelt umrandet (Auswahl in einem anderen Fenster).</summary>
    public int? MarkierterUnterricht { get; set; }

    /// <summary>Gewählte Stunde im tagesaktuellen Plan (wird nach dem Neuberechnen über Datum, Stunde und Unterricht wieder gefunden).</summary>
    public (DateOnly Datum, TagesEintrag Eintrag)? TagesAuswahl
    {
        get => _tagesAuswahl;
        private set
        {
            _tagesAuswahl = value;
            _tagesSchluessel = value is { } v ? (v.Datum, v.Eintrag.Stunde, TagesId(v.Eintrag), v.Eintrag.Regel?.ZeilenIndex ?? v.Eintrag.Vertretung?.ZeilenIndex ?? 0) : null;
        }
    }
    private (DateOnly Datum, TagesEintrag Eintrag)? _tagesAuswahl;
    private (DateOnly Datum, int Stunde, int Id, int Zeile)? _tagesSchluessel;

    /// <summary>Unterricht (positiv) bzw. Vertretung ohne Unterricht (negativ) eines Eintrags.</summary>
    private static int TagesId(TagesEintrag e) => e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId ?? -(e.Vertretung?.Id ?? 0);

    /// <summary>Gehört der Eintrag zur gemerkten Auswahl im tagesaktuellen Plan?</summary>
    private bool IstTagesAuswahl(DateOnly datum, TagesEintrag e) =>
        _tagesSchluessel is { } k && k.Datum == datum && k.Stunde == e.Stunde && k.Id == TagesId(e) &&
        (Typ != ElementTyp.Lehrer || k.Zeile == (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0));

    /// <summary>Eintrag eines Unterrichts (bzw. einer Vertretung) im tagesaktuellen Plan markieren (Synchronisation aus einem anderen Fenster).</summary>
    public void TagesAuswaehlen(DateOnly datum, int stunde, int unterrichtId, int? vertretungId = null)
    {
        if (_woche is not DateOnly w) return;
        var tag = datum.DayNumber - w.DayNumber;
        if (tag < 0 || tag >= Tage) return;
        if (_tage.Count != Tage) TageNeu();
        var e = EintraegeAm(tag, stunde).OrderBy(x => !x.Findetstatt)
            .FirstOrDefault(x => (vertretungId is int vid && x.Vertretung?.Id == vid) || (unterrichtId > 0 && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == unterrichtId));
        TagesAuswahl = e is null ? null : (datum, e);
        Invalidate();
    }

    /// <summary>
    /// Auswahl nach dem Neuladen der Daten wieder auflösen: dieselbe Planstunde (Unterricht, Tag, Stunde) bzw. derselbe Eintrag
    /// im tagesaktuellen Plan. Markierte Stunden, die es nicht mehr gibt, werden entfernt.
    /// </summary>
    public void AuswahlAktualisieren()
    {
        if (_ausgewaehlt != null && !D.Stundenplan.Contains(_ausgewaehlt))
            _ausgewaehlt = _auswahlSchluessel is { } k ? D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == k.Unterricht && p.Tag == k.Tag && p.Stunde == k.Stunde) : null;
        else if (_ausgewaehlt != null) _auswahlSchluessel = (_ausgewaehlt.UnterrichtId, _ausgewaehlt.Tag, _ausgewaehlt.Stunde);
        for (var i = Mehrfach.Count - 1; i >= 0; i--)
        {
            if (D.Stundenplan.Contains(Mehrfach[i])) continue;
            var m = Mehrfach[i];
            var neu = D.Stundenplan.FirstOrDefault(p => p.UnterrichtId == m.UnterrichtId && p.Tag == m.Tag && p.Stunde == m.Stunde);
            if (neu is null) Mehrfach.RemoveAt(i);
            else Mehrfach[i] = neu;
        }
        if (_tagesSchluessel is { } t && _woche is DateOnly w && t.Datum.DayNumber - w.DayNumber is var tag && tag >= 0 && tag < Tage)
        {
            var e = EintraegeAm(tag, t.Stunde).OrderBy(x => !x.Findetstatt).FirstOrDefault(x => IstTagesAuswahl(t.Datum, x));
            _tagesAuswahl = e is null ? null : (t.Datum, e);
        }
    }
    public bool Bearbeitbar { get; set; } = true;
    /// <summary>Text der linken oberen Ecke, z. B. „Nvpl 0/25“ (nicht verplante / alle Stunden).</summary>
    public string Ecke { get; set; } = "";

    /// <summary>Anzahl der stattfindenden Stunden des Elements in der angezeigten Woche (tagesaktuell).</summary>
    public int StundenDerWoche()
    {
        if (!Tagesmodus) return 0;
        if (_tage.Count != Tage) TageNeu();
        var n = 0;
        for (var t = 0; t < Tage; t++)
            for (var s = 1; s <= Stunden; s++)
                if (EintraegeAm(t, s).Any(e => e.Findetstatt)) n++;
        return n;
    }

    public event Action<Planstunde?>? AuswahlGeaendert;
    public event Action<DateOnly, TagesEintrag?>? TagesAuswahlGeaendert;
    public event Action<string, int, int>? Abgelegt;
    public event Action<Planstunde>? Doppelklick;
    public event Action<Point, Planstunde?>? Kontext;
    public event Action<Point, DateOnly, int, TagesEintrag?>? TagesKontext;

    private DateOnly? _woche;
    private readonly Dictionary<int, Tagesplan> _tage = new();
    private Feldstatus[,]? _status;
    private (int Tag, int Stunde)? _hover;
    private Point _down;
    private Planstunde? _downStunde;
    private (DateOnly, TagesEintrag)? _downEintrag;
    private readonly ToolTip _tip = new() { InitialDelay = 500 };
    private string _letzterTip = "";

    private int KopfHoehe => Format.Klein ? 17 : 22;
    private int KopfBreite => Format.Klein ? 20 : Format.ZeitenAnzeigen ? 74 : 32;
    private int PausenHoehe => Format.Klein ? 12 : 15;

    /// <summary>Angezeigte Pausen (eigene schmale Zeilen), nach der Stunde sortiert; „nach der 0. Stunde“ = vor der ersten Stunde (Frühaufsicht).</summary>
    private List<Pause> PausenZeilen() =>
        Format.PausenAnzeigen ? D.Zeitraster.Pausen.Where(p => p.NachStunde >= 0 && p.NachStunde < Stunden).GroupBy(p => p.NachStunde).Select(g => g.First()).OrderBy(p => p.NachStunde).ToList() : new();

    // Zellen für das schnelle Zeichnen: Pausen werden je Zeichenvorgang einmal ermittelt
    private List<Pause>? _pausenCache;
    private List<Pause> Pausen => _pausenCache ?? PausenZeilen();

    private static Schuldatei D => AppZustand.D;
    private static DatenIndex Ix => AppZustand.Doc.Index;

    public PlanAnsicht()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AllowDrop = true;
        BackColor = Color.White;
        Font = Ui.Schrift;
    }

    private int Tage => D.Zeitraster.TageProWoche;
    private int Stunden => D.Zeitraster.StundenProTag;

    /// <summary>Tagesaktuelle Pläne neu berechnen (nach Änderungen).</summary>
    public void TageNeu()
    {
        _tage.Clear();
        if (_woche is DateOnly w)
        {
            for (var t = 0; t < Tage; t++)
                _tage[t] = Tagesplan.Berechnen(D, w.AddDays(t));
            AuswahlAktualisieren();
        }
        Invalidate();
    }

    public DateOnly? DatumVon(int tag) => _woche?.AddDays(tag);

    private RectangleF Zelle(Rectangle bounds, int tag, int stunde)
    {
        var pausen = Pausen;
        var w = (bounds.Width - KopfBreite - 1) / (float)SichtbareTage;
        var h = Math.Max(4f, (bounds.Height - KopfHoehe - 1 - pausen.Count * PausenHoehe) / (float)Stunden);
        var vor = 0;
        foreach (var p in pausen) if (p.NachStunde < stunde) vor++;
        // Zeitbereich „Tag“: die übrigen Tage liegen außerhalb des sichtbaren Bereichs (gleiche Größe, damit Schriften gleich bleiben)
        var x = _nurTag is int nt ? (tag == nt ? bounds.X + KopfBreite : bounds.X - 100000f) : bounds.X + KopfBreite + tag * w;
        return new RectangleF(x, bounds.Y + KopfHoehe + (stunde - 1) * h + vor * PausenHoehe, w, h);
    }

    /// <summary>Zeile einer Pause (über die ganze Breite, unmittelbar vor der Stunde nach der Pause).</summary>
    private RectangleF PausenRechteck(Rectangle bounds, Pause p)
    {
        var r = Zelle(bounds, 0, p.NachStunde + 1);
        return new RectangleF(bounds.X, r.Y - PausenHoehe, bounds.Width - 1, PausenHoehe);
    }

    private (int Tag, int Stunde)? Treffer(Point p)
    {
        if (p.X < KopfBreite || p.Y < KopfHoehe) return null;
        var w = (Width - KopfBreite - 1) / (float)SichtbareTage;
        var t = (int)((p.X - KopfBreite) / w);
        if (_nurTag is int nt) t = t == 0 ? nt : -1;
        if (t < 0 || t >= Tage) return null;
        for (var s = 1; s <= Stunden; s++)
        {
            var r = Zelle(ClientRectangle, t, s);
            if (p.Y >= r.Y && p.Y < r.Bottom) return (t, s);
        }
        return null;
    }

    public List<Planstunde> StundenAm(int tag, int stunde) =>
        Ix.PlanVon(Typ, ElementId).Where(p => p.Tag == tag && p.Stunde == stunde).ToList();

    /// <summary>Einträge des Elements im tagesaktuellen Plan (inkl. Entfälle zur Anzeige).</summary>
    public List<TagesEintrag> EintraegeAm(int tag, int stunde)
    {
        if (!_tage.TryGetValue(tag, out var tp)) return new();
        return tp.Stunde(stunde).Where(e => Typ switch
        {
            ElementTyp.Klasse => e.KlassenIds.Contains(ElementId) || (e.Regel?.KlassenIds.Contains(ElementId) ?? false),
            ElementTyp.Lehrer => e.LehrerIds.Contains(ElementId) || e.Regel?.LehrerId == ElementId,
            ElementTyp.Raum => e.RaumIds.Contains(ElementId) || e.Regel?.RaumId == ElementId,
            ElementTyp.Fach => e.FachId == ElementId || e.Regel?.FachId == ElementId,
            _ => false,
        }).GroupBy(e => (e.Unterricht?.Id ?? -(e.Vertretung?.Id ?? 0), Typ == ElementTyp.Lehrer || Typ == ElementTyp.Raum ? (e.Regel?.ZeilenIndex ?? 0) : 0, e.Status == TagesStatus.Entfall))
          .Select(g => g.First()).OrderBy(e => e.Status == TagesStatus.Entfall).ToList();
    }

    private Planstunde? StundeBei(Point p)
    {
        var t = Treffer(p);
        if (t is null) return null;
        var f = RegelFelder(t.Value.Tag).FirstOrDefault(x => FeldRechteck(ClientRectangle, t.Value.Tag, x).Contains(p));
        return f is null ? null : f.Alle.FirstOrDefault(x => x.Stunde == t.Value.Stunde) ?? f.Erster;
    }

    private (DateOnly Datum, TagesEintrag Eintrag)? EintragBei(Point p)
    {
        var t = Treffer(p);
        if (t is null || _woche is null) return null;
        var f = TagesFelder(t.Value.Tag).FirstOrDefault(x => FeldRechteck(ClientRectangle, t.Value.Tag, x).Contains(p));
        if (f is null) return null;
        return (_woche.Value.AddDays(t.Value.Tag), f.Alle.FirstOrDefault(x => x.Stunde == t.Value.Stunde) ?? f.Erster);
    }

    // ------------------------------------------------------------------------------------------
    // Anordnung: zusammenhängende Stunden als ein Feld; gleichzeitige Stunden und Blöcke immer nebeneinander (Spuren)
    // ------------------------------------------------------------------------------------------

    private sealed class Feld<T>
    {
        public required T Erster;
        public readonly List<T> Alle = new();
        public int Start;
        public int Laenge = 1;
        public int Spur;
        public int Spuren = 1;
        public int StapelIndex = 0;
        public int StapelAnzahl = 1;
    }

    private static List<Feld<T>> Anordnen<T>(int stunden, Func<int, IReadOnlyList<T>> am, Func<T, string?> schluessel)
    {
        var felder = new List<Feld<T>>();
        var offen = new Dictionary<string, Feld<T>>();
        for (var s = 1; s <= stunden; s++)
        {
            var neu = new Dictionary<string, Feld<T>>();
            foreach (var x in am(s))
            {
                var k = schluessel(x);
                if (k != null && !neu.ContainsKey(k) && offen.TryGetValue(k, out var f))
                {
                    f.Alle.Add(x);
                    f.Laenge++;
                    neu[k] = f;
                    continue;
                }
                f = new Feld<T> { Erster = x, Start = s };
                f.Alle.Add(x);
                felder.Add(f);
                if (k != null && !neu.ContainsKey(k)) neu[k] = f;
            }
            offen = neu;
        }
        // Gruppen sich zeitlich überschneidender Felder
        var gruppen = new List<List<Feld<T>>>();
        foreach (var f in felder.OrderBy(x => x.Start))
        {
            var g = gruppen.LastOrDefault();
            if (g != null && g.Any(x => x.Start + x.Laenge > f.Start)) g.Add(f);
            else gruppen.Add(new List<Feld<T>> { f });
        }
        foreach (var g in gruppen)
        {
            var ende = new List<int>();
            foreach (var f in g.OrderBy(x => x.Start).ThenByDescending(x => x.Laenge))
            {
                var spur = ende.FindIndex(e => e < f.Start);
                if (spur < 0) { spur = ende.Count; ende.Add(0); }
                ende[spur] = f.Start + f.Laenge - 1;
                f.Spur = spur;
            }
            foreach (var f in g) f.Spuren = ende.Count;
        }
        return felder;
    }

    private List<Feld<Planstunde>> RegelFelder(int tag)
    {
        var plan = Ix.PlanVon(Typ, ElementId).Where(p => p.Tag == tag).ToList();
        return Anordnen<Planstunde>(Stunden, s => plan.Where(p => p.Stunde == s).ToList(), RegelSchluessel);
    }

    /// <summary>Zusammenfassung zu Doppelstunden/Blöcken (nicht in der Darstellung „Einzelstunden“).</summary>
    private string? RegelSchluessel(Planstunde p) => Format.Einzelstunden ? null : p.UnterrichtId.ToString();

    private List<Feld<TagesEintrag>> TagesFelder(int tag) => Anordnen<TagesEintrag>(Stunden, s => EintraegeAm(tag, s), e => Format.Einzelstunden ? null : TagesSchluessel(e));

    private RectangleF FeldRechteck<T>(Rectangle bounds, int tag, Feld<T> f)
    {
        var r = Zelle(bounds, tag, f.Start);
        var hoehe = f.Laenge == 1 ? r.Height : Zelle(bounds, tag, f.Start + f.Laenge - 1).Bottom - r.Y;
        var breite = (r.Width - 1) / f.Spuren;
        var x = r.X + 1 + f.Spur * breite;
        var teilHoehe = (hoehe - 1) / f.StapelAnzahl;
        return new RectangleF(x, r.Y + 1 + f.StapelIndex * teilHoehe, breite - 1, teilHoehe - 1);
    }

    /// <summary>Schlüssel für zusammenhängende Stunden im tagesaktuellen Plan (gleicher Unterricht, gleiche Änderung).</summary>
    private static string? TagesSchluessel(TagesEintrag e) =>
        (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) is int u
            ? $"{u}/{e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex}/{e.Status}/{e.Vertretung?.Art}/{string.Join(",", e.LehrerIds)}/{string.Join(",", e.RaumIds)}/{e.Vertretung?.VerlegungsId}"
            : null;

    private Feld<TagesEintrag>? TagesFeld(int tag, int stunde, TagesEintrag e) =>
        TagesFelder(tag).FirstOrDefault(f => f.Alle.Contains(e)) ??
        TagesFelder(tag).FirstOrDefault(f => f.Start <= stunde && f.Start + f.Laenge > stunde && TagesSchluessel(f.Erster) == TagesSchluessel(e));

    /// <summary>Anzahl zusammenhängender Stunden ab (tag, stunde) mit gleichem Schlüssel.</summary>
    private int TagesBlockLaenge(int tag, int stunde, TagesEintrag e) =>
        TagesFeld(tag, stunde, e) is { } f ? f.Start + f.Laenge - stunde : 1;

    /// <summary>Erste Stunde des Blocks, zu dem ein Eintrag gehört.</summary>
    private int TagesBlockStart(int tag, int stunde, TagesEintrag e) => TagesFeld(tag, stunde, e)?.Start ?? stunde;

    private int _ziehLaenge = 1;
    private int _ziehVersatz;
    /// <summary>Position der gezogenen Stunde(n) in diesem Plan (Tag, erste Stunde) für die Tausch-Pfeilanzeige.</summary>
    private (int Tag, int Stunde)? _ziehQuelle;

    /// <summary>Stunden des Blocks (Doppelstunde, Block) einer Planstunde: benachbarte Stunden desselben Unterrichts am selben Tag.</summary>
    public static List<Planstunde> Block(Planstunde p)
    {
        var gleiche = D.Stundenplan.Where(x => x.UnterrichtId == p.UnterrichtId && x.Tag == p.Tag).ToList();
        var res = new List<Planstunde> { p };
        for (var s = p.Stunde - 1; gleiche.FirstOrDefault(x => x.Stunde == s) is { } x; s--) res.Insert(0, x);
        for (var s = p.Stunde + 1; gleiche.FirstOrDefault(x => x.Stunde == s) is { } x; s++) res.Add(x);
        return res;
    }

    protected override void OnPaint(PaintEventArgs e) => Zeichnen(e.Graphics, ClientRectangle, false);

    public void Zeichnen(Graphics g, Rectangle bounds, bool druck)
    {
        if (Tagesmodus && _tage.Count != Tage) TageNeu();
        AuswahlAktualisieren();
        _pausenCache = PausenZeilen();
        try { ZeichnenIntern(g, bounds, druck); }
        finally { _pausenCache = null; }
    }

    private void ZeichnenIntern(Graphics g, Rectangle bounds, bool druck)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.FillRectangle(Brushes.White, bounds);
        var zr = D.Zeitraster;
        var klein = Format.Klein;
        using var linie = new Pen(klein ? Color.FromArgb(70, 70, 70) : Color.FromArgb(160, 170, 185));
        using var duenn = new Pen(klein ? Color.FromArgb(150, 150, 150) : Color.FromArgb(210, 216, 226));
        using var kopfSchrift = new Font("Segoe UI", klein ? Math.Max(7f, Math.Min(Format.Schriftgroesse, KopfHoehe / 1.9f)) : Ui.SchriftFett.Size, klein ? FontStyle.Regular : FontStyle.Bold);
        var heuteDatum = DateOnly.FromDateTime(DateTime.Today);
        var heuteTag = Kalender.IstUnterrichtstag(D, heuteDatum) ? Kalender.TagIndex(D, heuteDatum) : -1;
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, 1);
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            var datum = _woche?.AddDays(t);
            // heutiger Tag rosa (wie Untis); im Regelplan der heutige Wochentag
            var heute = datum.HasValue ? datum == heuteDatum : t == heuteTag;
            var gewaehlt = datum.HasValue && datum == AppZustand.Datum && AppZustand.Vertretungsmodus && datum != heuteDatum;
            using (var b = new SolidBrush(heute ? Color.FromArgb(255, 205, 210) : gewaehlt ? Ui.BlauHell : klein ? Color.FromArgb(232, 240, 230) : Ui.Kopf)) g.FillRectangle(b, kopf);
            var text = Planungsdienst.Tagname(D, t, !klein && r.Width > 70) + (!klein && datum.HasValue && r.Width > 60 ? " " + datum.Value.ToString("dd.MM.") : "");
            TextRenderer.DrawText(g, text, kopfSchrift, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
        using var stundenSchrift = new Font("Segoe UI", klein ? Math.Max(7f, Math.Min(Format.Schriftgroesse, Zelle(bounds, 0, 1).Height / 1.8f)) : Ui.SchriftKlein.Size);
        for (var s = 1; s <= Stunden; s++)
        {
            var r = Zelle(bounds, 0, s);
            var kopf = new RectangleF(bounds.X, r.Y, KopfBreite, r.Height);
            var nachm = zr.Art(0, s) == Tageszeit.Nachmittag || s > zr.LetzteVormittagsstunde;
            var markiert = !druck && _hover is { } hv0 && hv0.Stunde == s && _status != null;
            using (var b = new SolidBrush(markiert ? Color.FromArgb(255, 205, 210) : klein ? Color.FromArgb(232, 240, 230) : nachm ? Color.FromArgb(230, 245, 248) : Color.FromArgb(242, 244, 248))) g.FillRectangle(b, kopf);
            var z = zr.Zeit(s);
            var text = !klein && Format.ZeitenAnzeigen && z != null && r.Height > 26 ? $"{zr.StundenLabel(s)}\n{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}" : zr.StundenLabel(s);
            TextRenderer.DrawText(g, text, stundenSchrift, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
        if (Ecke.Length > 0 && !klein)
        {
            var ecke = new Rectangle(bounds.X, bounds.Y, KopfBreite, KopfHoehe);
            using (var b = new SolidBrush(Ui.Kopf)) g.FillRectangle(b, ecke);
            g.DrawRectangle(linie, ecke);
            using var f = new Font("Segoe UI", Math.Max(6f, Math.Min(10f, KopfHoehe / 3.2f)), FontStyle.Bold);
            TextRenderer.DrawText(g, Ecke, f, ecke, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        var plan = Tagesmodus ? new List<Planstunde>() : Ix.PlanVon(Typ, ElementId).ToList();
        var el = Ix.Element(Typ, ElementId);
        for (var t = 0; t < Tage; t++)
        {
            var datum = _woche?.AddDays(t);
            var ferien = datum.HasValue ? Kalender.FerienAm(D, datum.Value) : null;
            var kt = datum.HasValue ? Kalender.KalendertagAm(D, datum.Value) : null;
            for (var s = 1; s <= Stunden; s++)
            {
                var r = Zelle(bounds, t, s);
                var hint = druck ? Color.White : Ui.PlanLeer;
                var frei = kt?.StundeFrei(s) == true;
                if (!zr.Verfuegbar(t, s)) hint = Color.FromArgb(225, 225, 225);
                else if (ferien != null || frei) hint = Color.FromArgb(255, 248, 220);
                else if (!druck && _status is null && el != null && Format.DragDropZeitwunschFarben && !Tagesmodus)
                {
                    var w = el.Zeitwuensche.Get(t, s);
                    if (w != 0) hint = Color.FromArgb(70, Ui.ZeitwunschFarbe(w));
                }
                if (!druck && _status != null && Tagesmodus)
                {
                    // Vertretungsmodus: kollisionsfreie Zielfelder hellgrün, Ziel unter der Maus kräftig grün
                    var st = _status[t, s - 1];
                    var ziel = _hover is { } hz && hz.Tag == t && s >= hz.Stunde && s < hz.Stunde + Math.Max(1, _ziehLaenge);
                    hint = st switch
                    {
                        Feldstatus.Frei => ziel ? Color.FromArgb(0, 225, 0) : Color.FromArgb(160, 245, 190),
                        Feldstatus.TauschMoeglich => ziel ? Color.FromArgb(0, 225, 0) : Color.FromArgb(120, 230, 120),
                        Feldstatus.VorgabeVerletzt => Ui.Rot,
                        _ => hint,
                    };
                }
                else if (!druck && _status != null)
                {
                    hint = _status[t, s - 1] switch
                    {
                        Feldstatus.Frei => Ui.Gruen,
                        Feldstatus.VorgabeVerletzt => Ui.Rot,
                        Feldstatus.KeinRaum => Ui.Violett,
                        Feldstatus.TauschMoeglich => Color.FromArgb(160, 230, 160),
                        _ => Color.White,
                    };
                }
                using (var b = new SolidBrush(hint)) g.FillRectangle(b, r);
                g.DrawRectangle(duenn, r.X, r.Y, r.Width, r.Height);
            }
            // zweiter Durchgang: Inhalte; zusammenhängende Stunden (Doppelstunde, Block) werden als ein Feld gezeichnet
            // (außer in der Darstellung „Einzelstunden“), parallele Blöcke nebeneinander
            if (Tagesmodus)
            {
                foreach (var f in TagesFelder(t))
                    EintragZeichnen(g, FeldRechteck(bounds, t, f), f.Erster, datum!.Value, druck, f.Alle.Any(x => IstTagesAuswahl(datum.Value, x)));
                if (kt?.StundeFrei(1) == true && !klein) TextRenderer.DrawText(g, "frei", Ui.SchriftKlein, Rectangle.Round(Zelle(bounds, t, 1)), Color.Peru, TextFormatFlags.Top | TextFormatFlags.Right);
            }
            else
            {
                var kollisionen = new HashSet<int>();
                for (var s = 1; s <= Stunden; s++)
                {
                    var stunden = plan.Where(p => p.Tag == t && p.Stunde == s).ToList();
                    if (stunden.Count > 1 && Typ != ElementTyp.Fach && !AbWochenGetrennt(stunden) &&
                        stunden.Select(p => Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId)).Where(u => u != null)
                            .Select(u => string.IsNullOrWhiteSpace(u!.Band) ? "#" + u.Id : u.Band).Distinct().Count() > 1)
                        kollisionen.Add(s);
                }
                var tagPlan = plan.Where(p => p.Tag == t).ToList();
                foreach (var f in Anordnen<Planstunde>(Stunden, s => tagPlan.Where(p => p.Stunde == s).ToList(), RegelSchluessel))
                    StundeZeichnen(g, FeldRechteck(bounds, t, f), f.Erster, druck, Enumerable.Range(f.Start, f.Laenge).Any(kollisionen.Contains), f.Alle);
            }
            // beim Ziehen: Stunden, mit denen getauscht werden kann, grün; Ziel unter der Maus kräftig grün
            if (!druck && _status != null)
                for (var s = 1; s <= Stunden; s++)
                {
                    var st = _status[t, s - 1];
                    var ziel = _hover is { } hz && hz.Tag == t && s >= hz.Stunde && s < hz.Stunde + Math.Max(1, _ziehLaenge);
                    if (st != Feldstatus.TauschMoeglich && !(ziel && st == Feldstatus.Frei && Tagesmodus)) continue;
                    if (Tagesmodus ? !EintraegeAm(t, s).Any(e => e.Findetstatt) : !plan.Any(p => p.Tag == t && p.Stunde == s)) continue;
                    var r = Zelle(bounds, t, s);
                    using var b = new SolidBrush(ziel ? Color.FromArgb(150, 0, 225, 0) : Color.FromArgb(130, 90, 220, 90));
                    g.FillRectangle(b, r);
                }
            if (ferien != null && !klein) TextRenderer.DrawText(g, ferien.Kurzname, Ui.SchriftKlein, Rectangle.Round(Zelle(bounds, t, 1)), Color.Peru, TextFormatFlags.Top | TextFormatFlags.Right);
            if (!druck && _hover is { } hv && hv.Tag == t)
            {
                var start = Math.Clamp(hv.Stunde - _ziehVersatz, 1, Stunden);
                var r = Zelle(bounds, t, start);
                var n = Math.Max(1, Math.Min(_ziehLaenge, Stunden - start + 1));
                var unten = Zelle(bounds, t, start + n - 1).Bottom;
                using var p = new Pen(Color.Black, 2) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, unten - r.Y - 2);
            }
        }
        // Pausen als eigene schmale Zeilen mit Bezeichnung über die ganze Breite
        if (Pausen.Count > 0)
        {
            using var pausenSchrift = new Font("Segoe UI", Math.Max(6f, Math.Min(8f, PausenHoehe / 1.6f)));
            using var pausenHg = new SolidBrush(klein ? Color.FromArgb(214, 226, 214) : Color.FromArgb(236, 239, 244));
            foreach (var p in Pausen)
            {
                var r = PausenRechteck(bounds, p);
                g.FillRectangle(pausenHg, r);
                g.DrawRectangle(linie, r.X, r.Y, r.Width, r.Height);
                var text = klein || bounds.Width < 260 ? Pausenbezeichnung.Kurz(zr, p) : p.Bezeichnung.Length > 0 ? p.Bezeichnung : Pausenbezeichnung.Kurz(zr, p);
                TextRenderer.DrawText(g, text, pausenSchrift, Rectangle.Round(r), Color.FromArgb(60, 60, 60), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
        if (!druck) TauschAnzeigen(g, bounds);
        g.DrawRectangle(linie, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    /// <summary>
    /// Untis-Pfeilanzeige beim Ziehen: Liegt die Maus auf einer Stunde, mit der getauscht werden kann, werden Quelle und Ziel rot
    /// umrandet und durch einen roten, gebogenen Doppelpfeil verbunden.
    /// </summary>
    private void TauschAnzeigen(Graphics g, Rectangle bounds)
    {
        if (_status is null || _hover is not { } hv || _ziehQuelle is not { } q) return;
        if (_status[hv.Tag, hv.Stunde - 1] != Feldstatus.TauschMoeglich) return;
        if (q.Tag < 0 || q.Tag >= Tage || q.Stunde < 1 || q.Stunde > Stunden) return;
        var laenge = Math.Max(1, _ziehLaenge);
        var zielStart = Math.Clamp(hv.Stunde - _ziehVersatz, 1, Stunden);
        RectangleF Bereich(int tag, int start)
        {
            var oben = Zelle(bounds, tag, start);
            var unten = Zelle(bounds, tag, Math.Min(Stunden, start + laenge - 1));
            return new RectangleF(oben.X, oben.Y, oben.Width, unten.Bottom - oben.Y);
        }
        var a = Bereich(q.Tag, q.Stunde);
        var b = Bereich(hv.Tag, zielStart);
        var alt = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var rot = new Pen(Color.FromArgb(230, 0, 0), 3f))
        {
            g.DrawRectangle(rot, a.X + 1.5f, a.Y + 1.5f, a.Width - 3, a.Height - 3);
            g.DrawRectangle(rot, b.X + 1.5f, b.Y + 1.5f, b.Width - 3, b.Height - 3);
        }
        TauschPfeil(g, a, b);
        g.SmoothingMode = alt;
    }

    /// <summary>Roter, gebogener Doppelpfeil zwischen zwei Feldern: ein Bogen von der Quelle zum Ziel, der andere auf der Gegenseite zurück.</summary>
    internal static void TauschPfeil(Graphics g, RectangleF quelle, RectangleF ziel)
    {
        var pa = new PointF(quelle.X + quelle.Width / 2, quelle.Y + quelle.Height / 2);
        var pb = new PointF(ziel.X + ziel.Width / 2, ziel.Y + ziel.Height / 2);
        var dx = pb.X - pa.X;
        var dy = pb.Y - pa.Y;
        var laenge = (float)Math.Sqrt(dx * dx + dy * dy);
        if (laenge < 4) return;
        var nx = -dy / laenge;
        var ny = dx / laenge;
        var bauch = Math.Max(16f, laenge * 0.32f);
        using var saum = new Pen(Color.FromArgb(220, Color.White), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var spitze = new AdjustableArrowCap(3.2f, 3.6f, true);
        using var rot = new Pen(Color.FromArgb(230, 0, 0), 3f) { StartCap = LineCap.Round, CustomEndCap = spitze };
        foreach (var seite in new[] { 1f, -1f })
        {
            var (von, nach, rv, rn) = seite > 0 ? (pa, pb, quelle, ziel) : (pb, pa, ziel, quelle);
            var mitte = new PointF((von.X + nach.X) / 2 + nx * bauch * seite, (von.Y + nach.Y) / 2 + ny * bauch * seite);
            // Start knapp innerhalb der Quelle, Ende am Rand des Ziels (Pfeilspitze sichtbar)
            var start = Richtung(von, mitte, Math.Min(rv.Width, rv.Height) * 0.25f);
            var ende = RandPunkt(rn, mitte);
            var c1 = new PointF(start.X + (mitte.X - start.X) * 2 / 3, start.Y + (mitte.Y - start.Y) * 2 / 3);
            var c2 = new PointF(ende.X + (mitte.X - ende.X) * 2 / 3, ende.Y + (mitte.Y - ende.Y) * 2 / 3);
            g.DrawBezier(saum, start, c1, c2, ende);
            g.DrawBezier(rot, start, c1, c2, ende);
        }
    }

    private static PointF Richtung(PointF von, PointF zu, float abstand)
    {
        var dx = zu.X - von.X;
        var dy = zu.Y - von.Y;
        var l = (float)Math.Sqrt(dx * dx + dy * dy);
        return l < 0.1f ? von : new PointF(von.X + dx / l * abstand, von.Y + dy / l * abstand);
    }

    /// <summary>Schnittpunkt der Strecke vom Mittelpunkt des Rechtecks zu <paramref name="aussen"/> mit dem Rand (leicht außerhalb).</summary>
    private static PointF RandPunkt(RectangleF r, PointF aussen)
    {
        var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
        var dx = aussen.X - c.X;
        var dy = aussen.Y - c.Y;
        if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return c;
        var tx = Math.Abs(dx) < 0.01f ? float.MaxValue : (r.Width / 2) / Math.Abs(dx);
        var ty = Math.Abs(dy) < 0.01f ? float.MaxValue : (r.Height / 2) / Math.Abs(dy);
        var t = Math.Min(1f, Math.Min(tx, ty) * 1.08f);
        return new PointF(c.X + dx * t, c.Y + dy * t);
    }

    /// <summary>Stunden verschiedener Unterrichtsgruppen (A/B-Woche) im selben Feld sind keine Kollision.</summary>
    private static bool AbWochenGetrennt(List<Planstunde> stunden)
    {
        if (stunden.Count < 2) return false;
        var gruppen = stunden.Select(p => Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId)).Where(u => u != null).Select(u => WochenMaske(u!)).ToList();
        for (var i = 0; i < gruppen.Count; i++)
            for (var j = i + 1; j < gruppen.Count; j++)
                if ((gruppen[i] & gruppen[j]) != 0) return false;
        return true;
    }

    /// <summary>Bitmaske der Wochen (A=1, B=2, …), in denen ein Unterricht stattfindet.</summary>
    public static int WochenMaske(Unterricht u) => Kalender.WochenMaske(D, u);

    private Color Farbe(Unterricht? u, int zeile, int? fachId = null)
    {
        var z = u?.Zeilen.ElementAtOrDefault(zeile) ?? u?.Zeilen.FirstOrDefault();
        Element? e = Format.Farbe switch
        {
            FarbeNach.Fach => (z?.FachId ?? fachId) is int f ? Ix.Faecher.GetValueOrDefault(f) : null,
            FarbeNach.Lehrer => z?.LehrerId is int l ? Ix.Lehrer.GetValueOrDefault(l) : null,
            FarbeNach.Klasse => z?.KlassenIds.Count > 0 ? Ix.Klassen.GetValueOrDefault(z.KlassenIds[0]) : null,
            _ => null,
        };
        return Ui.Farbe(e?.Hintergrund ?? 0, Color.FromArgb(225, 232, 245));
    }

    private string Kurz(ElementTyp t, int? id)
    {
        if (id is null) return "";
        var e = Ix.Element(t, id.Value);
        return e is null ? "?" : Format.Langname && e.Langname.Length > 0 ? e.Langname : e.Kurzname;
    }

    /// <summary>Präfixe wie in Untis: „.“ = Kopplung (mehrere Kopplungszeilen), „*“ = fixiert.</summary>
    private static string Praefix(Unterricht? u, bool fixiert) => (u?.Zeilen.Count > 1 ? "." : "") + (fixiert ? "*" : "");

    /// <summary>Kleiner Stundenplan: nur das Fachkürzel (mit Präfixen), zentriert; Entfall durchgestrichen, Änderungen rot.</summary>
    private void KleinZeichnen(Graphics g, RectangleF r, string text, Color farbe, bool durchgestrichen = false)
    {
        var groesse = Math.Max(6f, Math.Min(Format.Schriftgroesse + 1, Math.Min(r.Height / 1.45f, r.Width / Math.Max(2.2f, text.Length * 0.62f))));
        using var f = new Font("Segoe UI", groesse, durchgestrichen ? FontStyle.Strikeout : FontStyle.Regular);
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), farbe, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    /// <summary>Rahmen der gewählten Stunde: kräftig dunkelblau mit gelber Strichelung (gut sichtbar auf jeder Fachfarbe).</summary>
    private static void AuswahlRahmen(Graphics g, RectangleF r)
    {
        using var blau = new Pen(Color.FromArgb(20, 40, 160), 3f);
        using var gelb = new Pen(Color.FromArgb(255, 220, 0), 3f) { DashPattern = new[] { 1.5f, 1.5f } };
        g.DrawRectangle(blau, r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
        g.DrawRectangle(gelb, r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
    }

    /// <summary>Verknüpfte Markierung (derselbe Unterricht, in einem anderen Fenster gewählt): gestrichelter orangefarbener Rahmen.</summary>
    private static void VerknuepfungsRahmen(Graphics g, RectangleF r)
    {
        using var pen = new Pen(Color.FromArgb(255, 120, 0), 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
    }

    private void StundeZeichnen(Graphics g, RectangleF r, Planstunde p, bool druck, bool kollision, List<Planstunde>? feld = null)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
        feld ??= new List<Planstunde> { p };
        if (Format.Klein)
        {
            var zi = Enumerable.Range(0, u.Zeilen.Count).FirstOrDefault(i => Typ switch
            {
                ElementTyp.Klasse => u.Zeilen[i].KlassenIds.Contains(ElementId),
                ElementTyp.Lehrer => u.Zeilen[i].LehrerId == ElementId,
                ElementTyp.Fach => u.Zeilen[i].FachId == ElementId,
                ElementTyp.Raum => p.RaumFuerZeile(i) == ElementId,
                _ => true,
            });
            var hg = Farbe(u, zi);
            using (var b = new SolidBrush(hg)) g.FillRectangle(b, r);
            KleinZeichnen(g, r, Praefix(u, p.Fixiert) + Kurz(ElementTyp.Fach, u.Zeilen.ElementAtOrDefault(zi)?.FachId), Ui.Textfarbe(hg));
            if (kollision) using (var pen = new Pen(Color.Red, 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            AuswahlZeichnen(g, r, p, feld, druck);
            return;
        }
        var zeilen = Enumerable.Range(0, u.Zeilen.Count).Where(i => Typ switch
        {
            ElementTyp.Klasse => u.Zeilen[i].KlassenIds.Contains(ElementId),
            ElementTyp.Lehrer => u.Zeilen[i].LehrerId == ElementId,
            ElementTyp.Fach => u.Zeilen[i].FachId == ElementId,
            ElementTyp.Raum => p.RaumFuerZeile(i) == ElementId,
            _ => true,
        }).ToList();
        if (zeilen.Count == 0) zeilen.Add(0);
        var farbe = Farbe(u, zeilen[0]);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
        var text = Ui.Textfarbe(farbe);
        using var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        using var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        var ug = u.UGruppeId is int ugid ? D.UGruppen.FirstOrDefault(x => x.Id == ugid)?.Kurzname : u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => null };
        if (Format.Standardformat || Format.Felder.Count == 0)
        {
            var z = u.Zeilen[zeilen[0]];
            var kopf = (p.Fixiert ? "*" : "") + Kurz(ElementTyp.Fach, z.FachId) + (u.Kursname.Length > 0 && r.Width > 60 ? " " + u.Kursname : "") + (ug != null ? $" ({ug})" : "");
            var info = Typ switch
            {
                ElementTyp.Klasse => string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId) + ZeilenGruppe(u.Zeilen[i]))),
                ElementTyp.Lehrer => Ix.KKlassen(u.Zeilen[zeilen[0]].KlassenIds),
                ElementTyp.Raum => Ix.KKlassen(u.KlassenIds) + " " + string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId))),
                _ => Ix.KKlassen(u.KlassenIds) + " " + Kurz(ElementTyp.Lehrer, z.LehrerId),
            };
            var raum = Typ == ElementTyp.Raum ? "" : string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))).Where(x => x.Length > 0));
            if (Format.Gross && u.Zeilen.Count > 1 && r.Height > 40)
            {
                var y = r.Y + 1;
                var zh = (r.Height - 2) / u.Zeilen.Count;
                for (var i = 0; i < u.Zeilen.Count; i++)
                {
                    var zz = u.Zeilen[i];
                    var zeileText = $"{(p.Fixiert && i == 0 ? "*" : "")}{Kurz(ElementTyp.Fach, zz.FachId)} {Kurz(ElementTyp.Lehrer, zz.LehrerId)} {Ix.KKlassen(zz.KlassenIds)} {Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))}{ZeilenGruppe(zz)}";
                    TextRenderer.DrawText(g, zeileText, klein, new Rectangle((int)r.X + 2, (int)y, (int)r.Width - 3, (int)zh), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    y += zh;
                }
            }
            else
            {
                // alle Werte untereinander: Fach, dann Lehrer/Klassen, dann Raum
                var zweite = Typ switch
                {
                    ElementTyp.Raum => new[] { Ix.KKlassen(u.KlassenIds), string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId))) },
                    ElementTyp.Fach => new[] { Ix.KKlassen(u.KlassenIds), Kurz(ElementTyp.Lehrer, z.LehrerId) },
                    _ => new[] { info },
                };
                TextZeilen(g, r, new[] { kopf }.Concat(zweite).Append(raum).ToList(), text);
            }
        }
        else
        {
            foreach (var f in Format.Felder)
            {
                var zi = Math.Clamp(f.Zeile - 1, 0, u.Zeilen.Count - 1);
                var z = u.Zeilen[zi];
                var wert = f.Art switch
                {
                    StundenFeldArt.Fach => (p.Fixiert && zi == 0 ? "*" : "") + Kurz(ElementTyp.Fach, z.FachId),
                    StundenFeldArt.Lehrer => Kurz(ElementTyp.Lehrer, z.LehrerId),
                    StundenFeldArt.Klasse => Ix.KKlassen(z.KlassenIds),
                    StundenFeldArt.Raum => Kurz(ElementTyp.Raum, p.RaumFuerZeile(zi)),
                    StundenFeldArt.UNr => u.Id.ToString(),
                    StundenFeldArt.Text => u.Text,
                    StundenFeldArt.Gruppe => u.Schuelergruppe,
                    StundenFeldArt.Kurs => u.Kursname,
                    _ => D.Zeitraster.Zeit(p.Stunde)?.Beginn.ToString("H:mm") ?? "",
                };
                var fr = new RectangleF(r.X + f.X * r.Width, r.Y + f.Y * r.Height, f.B * r.Width, f.H * r.Height);
                using var ff = new Font("Segoe UI", Math.Max(5.5f, Math.Min(Format.Schriftgroesse * f.SchriftProzent / 100f, fr.Height / 1.6f)), f.Fett ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, wert, ff, Rectangle.Round(fr), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        if (kollision)
            using (var pen = new Pen(Color.Red, 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        AuswahlZeichnen(g, r, p, feld, druck);
    }

    /// <summary>Markierung eines Feldes: gewählte Stunde (auch innerhalb eines Blocks), Mehrfachauswahl, verknüpfter Unterricht.</summary>
    private void AuswahlZeichnen(Graphics g, RectangleF r, Planstunde p, List<Planstunde> feld, bool druck)
    {
        if (druck) return;
        if (Ausgewaehlt != null && feld.Contains(Ausgewaehlt)) AuswahlRahmen(g, r);
        else if (feld.Any(Mehrfach.Contains))
            using (var pen = new Pen(Color.FromArgb(255, 140, 0), 2.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        else if (MarkierterUnterricht == p.UnterrichtId) VerknuepfungsRahmen(g, r);
    }

    /// <summary>Texte einer Stunde untereinander; reicht die Höhe nicht, werden die unteren Zeilen zusammengefasst.</summary>
    /// <param name="durchgestrichen">Anzahl der (nicht leeren) Zeilen von oben, die einzeln durchgestrichen werden (Entfall).</param>
    private void TextZeilen(Graphics g, RectangleF r, IReadOnlyList<string> zeilen, Color farbe, int durchgestrichen = 0)
    {
        var liste = zeilen.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        durchgestrichen = Math.Min(durchgestrichen, liste.Count);
        if (liste.Count == 0) return;
        var maxZeilen = Math.Max(1, (int)((r.Height - 2) / 11));
        while (liste.Count > maxZeilen && liste.Count > 1)
        {
            liste[^2] = liste[^2] + " " + liste[^1];
            liste.RemoveAt(liste.Count - 1);
        }
        var zh = Math.Min((r.Height - 2) / liste.Count, Format.Schriftgroesse * 2.1f);
        var groesse = Math.Max(6f, Math.Min(Format.Schriftgroesse, zh / 1.55f));
        using var fett = new Font("Segoe UI", groesse, FontStyle.Bold);
        using var normal = new Font("Segoe UI", Math.Max(6f, groesse - 0.5f));
        using var fettWeg = new Font("Segoe UI", groesse, FontStyle.Bold | FontStyle.Strikeout);
        using var normalWeg = new Font("Segoe UI", Math.Max(6f, groesse - 0.5f), FontStyle.Strikeout);
        var y = r.Y + 1 + Math.Max(0, (r.Height - 2 - zh * liste.Count) / 2);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        for (var i = 0; i < liste.Count; i++)
        {
            var weg = i < durchgestrichen;
            TextRenderer.DrawText(g, liste[i], i == 0 ? (weg ? fettWeg : fett) : (weg ? normalWeg : normal), new Rectangle((int)r.X + 1, (int)y, (int)r.Width - 2, (int)Math.Ceiling(zh)), farbe, flags);
            y += zh;
        }
    }

    /// <summary>Mindesthöhe, damit alle Werte einer Stunde untereinander lesbar sind (das Fenster scrollt dann).</summary>
    /// <remarks>Im Vertretungsmodus kompakt (zwei bis drei Zeilen je Stunde), damit der Tagesplan auf die Seite passt.</remarks>
    public int BenoetigteHoehe => KopfHoehe + PausenZeilen().Count * PausenHoehe + 2 + Stunden * (Format.ZellenHoehe > 0 ? Format.ZellenHoehe :
        Format.Klein ? (int)Math.Ceiling(Format.Schriftgroesse * 1.6f) :
        (int)Math.Ceiling(Format.Schriftgroesse * (AppZustand.Vertretungsmodus ? (Format.Gross ? 3.6f : 2.8f) : Format.Gross ? 6.4f : 5.0f)));

    /// <summary>Mindestbreite laut Format (Zellenbreite je Tag), 0 = an die Fensterbreite anpassen.</summary>
    public int BenoetigteBreite => Format.ZellenBreite > 0 ? KopfBreite + SichtbareTage * Format.ZellenBreite + 2 : 0;

    private static string ZeilenGruppe(Kopplungszeile z) =>
        z.UGruppeId is int g && D.UGruppen.FirstOrDefault(x => x.Id == g) is { } ug ? $"({ug.Kurzname})" : "";

    private void EintragZeichnen(Graphics g, RectangleF r, TagesEintrag e, DateOnly datum, bool druck, bool gewaehlt)
    {
        var geaendert = e.Vertretung != null && e.Vertretung.Art != VertretungsArt.Raumvertretung || e.Status == TagesStatus.Zusaetzlich;
        var entfall = e.Status == TagesStatus.Entfall;
        var farbe = entfall ? Color.FromArgb(238, 238, 238) : Farbe(e.Unterricht, e.Regel?.ZeilenIndex ?? 0, e.FachId);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
        var textfarbe = geaendert || e.Vertretung?.Art == VertretungsArt.Raumvertretung ? Color.FromArgb(200, 0, 0) : Ui.Textfarbe(farbe);
        if (entfall) textfarbe = Color.Gray;
        if (Format.Klein)
        {
            var fixiert = e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true;
            var kurz = e.Status == TagesStatus.Offen && Typ == ElementTyp.Lehrer ? "???" : Praefix(e.Unterricht, fixiert) + Kurz(ElementTyp.Fach, e.FachId ?? e.Regel?.FachId);
            KleinZeichnen(g, r, kurz, textfarbe, entfall);
            if (e.Status is TagesStatus.Offen or TagesStatus.Zusaetzlich)
                using (var pen = new Pen(Color.Red, 1.5f) { DashStyle = e.Status == TagesStatus.Offen ? DashStyle.Dash : DashStyle.Solid }) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            if (!druck && gewaehlt) AuswahlRahmen(g, r);
            else if (!druck && MarkierterUnterricht is int mu && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == mu) VerknuepfungsRahmen(g, r);
            return;
        }
        using var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        using var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        var v = e.Vertretung;
        var fach = Kurz(ElementTyp.Fach, e.FachId ?? e.Regel?.FachId);
        var stern = e.Regel?.Planstunde.Fixiert == true || v?.Fixiert == true ? "*" : "";
        var lehrer = e.Status == TagesStatus.Offen ? "???" : string.Join(",", e.LehrerIds.Select(l => Kurz(ElementTyp.Lehrer, l)));
        if (v?.Art == VertretungsArt.VertretungOhneLehrer) lehrer = "+++";
        var info = Typ switch
        {
            ElementTyp.Lehrer => Ix.KKlassen(e.KlassenIds),
            ElementTyp.Raum => (Ix.KKlassen(e.KlassenIds) + " " + lehrer).Trim(),
            _ => lehrer,
        };
        var raum = Typ == ElementTyp.Raum ? "" : string.Join(",", e.RaumIds.Select(x => Kurz(ElementTyp.Raum, x)));
        if (entfall)
        {
            // Entfall wie in Untis: alle Werte einzeln durchgestrichen (kein Schrägbalken), ggf. Ziel der Verlegung darunter
            var urLehrer = string.Join(",", (e.LehrerIds.Count > 0 ? e.LehrerIds : v?.LehrerId is int ul ? new List<int> { ul } : new List<int>()).Select(l => Kurz(ElementTyp.Lehrer, l)));
            var werte = Typ switch
            {
                ElementTyp.Lehrer => new List<string> { stern + fach, Ix.KKlassen(e.KlassenIds), raum },
                ElementTyp.Raum => new List<string> { stern + fach, Ix.KKlassen(e.KlassenIds), urLehrer },
                _ => new List<string> { stern + fach, urLehrer, raum },
            };
            var anzahl = werte.Count(x => !string.IsNullOrWhiteSpace(x));
            if (v?.NachDatum.HasValue == true) werte.Add($"→ {Planungsdienst.TageKurz[((int)v.NachDatum.Value.DayOfWeek + 6) % 7]} {v.NachStunde}.");
            TextZeilen(g, r, werte, textfarbe, anzahl);
        }
        else
        {
            var kopf = stern + fach;
            if (v != null && v.Art is not (VertretungsArt.Vertretung or VertretungsArt.Raumvertretung)) kopf += r.Width > 70 ? " " + Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art) : "";
            var zeilenTexte = new List<string> { kopf };
            if (Typ == ElementTyp.Raum) { zeilenTexte.Add(Ix.KKlassen(e.KlassenIds)); zeilenTexte.Add(lehrer); }
            else zeilenTexte.Add(info);
            if (v?.LehrerId is int alt && !e.LehrerIds.Contains(alt) && r.Height > 44) zeilenTexte.Add($"({Kurz(ElementTyp.Lehrer, alt)})");
            zeilenTexte.Add(raum);
            TextZeilen(g, r, zeilenTexte, textfarbe);
        }
        if (e.Status == TagesStatus.Offen)
            using (var pen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        if (e.Status == TagesStatus.Zusaetzlich)
            using (var pen = new Pen(Color.Red, 1.5f)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        if (!druck && gewaehlt) AuswahlRahmen(g, r);
        else if (!druck && MarkierterUnterricht is int mu && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == mu) VerknuepfungsRahmen(g, r);
    }

    // ------------------------------------------------------------------------------------------
    // Maus / Drag & Drop
    // ------------------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (Tagesmodus)
        {
            var te = EintragBei(e.Location);
            var h = Treffer(e.Location);
            if (h is { } hh && _woche is DateOnly w) AppZustand.DatumSetzen(w.AddDays(hh.Tag));
            TagesAuswahl = te;
            Invalidate();
            if (te is { } x) TagesAuswahlGeaendert?.Invoke(x.Datum, x.Eintrag);
            else if (h is { } h2 && _woche is DateOnly w2) TagesAuswahlGeaendert?.Invoke(w2.AddDays(h2.Tag), null);
            if (e.Button == MouseButtons.Left) { _down = e.Location; _downEintrag = te; }
            else if (e.Button == MouseButtons.Right && h is { } h3 && _woche is DateOnly w3) TagesKontext?.Invoke(e.Location, w3.AddDays(h3.Tag), h3.Stunde, te?.Eintrag);
            return;
        }
        var p = StundeBei(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            _down = e.Location;
            _downStunde = p;
            if ((ModifierKeys & Keys.Control) != 0 && p != null)
            {
                if (Ausgewaehlt != null && !Mehrfach.Contains(Ausgewaehlt)) Mehrfach.Add(Ausgewaehlt);
                if (!Mehrfach.Remove(p)) Mehrfach.Add(p);
                Ausgewaehlt = p;
                Invalidate();
                AuswahlGeaendert?.Invoke(p);
                return;
            }
            if (!Mehrfach.Contains(p!)) Mehrfach.Clear();
            if (p != Ausgewaehlt)
            {
                Ausgewaehlt = p;
                Invalidate();
                AuswahlGeaendert?.Invoke(p);
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (p != null && p != Ausgewaehlt && !Mehrfach.Contains(p)) { Ausgewaehlt = p; Mehrfach.Clear(); Invalidate(); AuswahlGeaendert?.Invoke(p); }
            Kontext?.Invoke(e.Location, p);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (!Tagesmodus && StundeBei(e.Location) is { } p) Doppelklick?.Invoke(p);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var weit = Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4;
        if (e.Button == MouseButtons.Left && weit && Bearbeitbar)
        {
            if (Tagesmodus && _downEintrag is { } de && de.Item2.Findetstatt)
            {
                _downEintrag = null;
                var uid = de.Item2.Unterricht?.Id ?? de.Item2.Vertretung?.UnterrichtId;
                if (uid is null) return;
                var zeile = Typ == ElementTyp.Lehrer ? (de.Item2.Regel?.ZeilenIndex ?? de.Item2.Vertretung?.ZeilenIndex ?? 0) : -1;
                var tagIdx = _woche is DateOnly wo ? de.Item1.DayNumber - wo.DayNumber : 0;
                var feld = TagesFeld(tagIdx, de.Item2.Stunde, de.Item2);
                var start = feld?.Start ?? de.Item2.Stunde;
                var laenge = feld?.Laenge ?? 1;
                DoDragDrop($"tag:{de.Item1:yyyyMMdd}:{start}:{uid}:{zeile}:{laenge}", DragDropEffects.Move);
                return;
            }
            if (!Tagesmodus && _downStunde != null)
            {
                var p = _downStunde;
                _downStunde = null;
                var mitgezogen = Mehrfach.Count > 1 && Mehrfach.Contains(p) ? Mehrfach.ToList() : Format.Einzelstunden ? new List<Planstunde> { p } : Block(p);
                var idx = D.Stundenplan.IndexOf(p);
                if (idx < 0) return;
                var alle = mitgezogen.Select(x => D.Stundenplan.IndexOf(x)).Where(i => i >= 0);
                DoDragDrop($"plan:{idx};{string.Join(",", alle)}", DragDropEffects.Move);
                return;
            }
        }
        string tip;
        if (Tagesmodus) tip = EintragBei(e.Location) is { } te ? TagesLupentext(te.Eintrag) : "";
        else tip = StundeBei(e.Location) is { } s ? Lupentext(s) : "";
        if (tip != _letzterTip)
        {
            _letzterTip = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    public static string Lupentext(Planstunde p)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return "";
        var zeilen = u.Zeilen.Select((z, i) => $"{Ix.KFach(z.FachId)}  {Ix.KLehrer(z.LehrerId)}  {Ix.KKlassen(z.KlassenIds)}  {Ix.KRaum(p.RaumFuerZeile(i))}{ZeilenGruppe(z)}");
        return $"U-Nr. {u.Id}{(p.Fixiert ? " (fixiert)" : "")}\n{string.Join("\n", zeilen)}";
    }

    public static string TagesLupentext(TagesEintrag e)
    {
        var v = e.Vertretung;
        var basis = $"{Ix.KFach(e.FachId)}  {string.Join(",", e.LehrerIds.Select(Ix.KLehrer))}  {Ix.KKlassen(e.KlassenIds)}  {string.Join(",", e.RaumIds.Select(Ix.KRaum))}";
        if (v is null) return (e.Unterricht != null ? $"U-Nr. {e.Unterricht.Id}\n" : "") + basis;
        return $"Vtr-Nr. {v.Id}: {Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art)}\n{basis}" +
               (v.LehrerId.HasValue ? $"\n(Lehrer) {Ix.KLehrer(v.LehrerId)}" : "") + (string.IsNullOrEmpty(v.Text) ? "" : "\n" + v.Text);
    }

    /// <summary>Gezogene Stunden: primäre Planstunde und alle mitgezogenen (Block bzw. Mehrfachauswahl).</summary>
    public static (Planstunde? Primaer, List<Planstunde> Alle) PlanDaten(string s)
    {
        if (!s.StartsWith("plan:")) return (null, new());
        var teile = s[5..].Split(';');
        if (!int.TryParse(teile[0], out var idx) || idx >= D.Stundenplan.Count) return (null, new());
        var alle = teile.Length > 1 ? teile[1].Split(',').Select(x => int.TryParse(x, out var i) && i < D.Stundenplan.Count ? D.Stundenplan[i] : null).OfType<Planstunde>().ToList() : new();
        if (alle.Count == 0) alle.Add(D.Stundenplan[idx]);
        return (D.Stundenplan[idx], alle);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        if (!Bearbeitbar || e.Data?.GetData(DataFormats.Text) is not string s || !ZiehenBeginnen(s)) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
    }

    /// <summary>Zielfarben für gezogene Daten berechnen („plan:“, „tag:“, „vormerkung:“, „unterricht:“); false = hier nicht ablegbar.</summary>
    private bool ZiehenBeginnen(string s)
    {
        var e = new ZiehErgebnis();
        ZiehenBerechnen(s, e);
        if (e.Effect == DragDropEffects.None) { _status = null; _ziehQuelle = null; return false; }
        Invalidate();
        return true;
    }

    private sealed class ZiehErgebnis
    {
        public DragDropEffects Effect = DragDropEffects.Move;
    }

    private void ZiehenBerechnen(string s, ZiehErgebnis e)
    {
        _status = new Feldstatus[Tage, Stunden];
        _ziehQuelle = null;
        if (Tagesmodus)
        {
            if (!s.StartsWith("tag:") && !s.StartsWith("vormerkung:")) { _status = null; e.Effect = DragDropEffects.None; return; }
            var (lehrer, klassen, uid, raum) = TagesDragDaten(s);
            var teile = s.Split(':');
            _ziehLaenge = teile.Length > 5 && int.TryParse(teile[5], out var zl) ? Math.Max(1, zl) : 1;
            _ziehVersatz = 0;
            // gezogene Stunde: Quelle für die Prüfung der Tauschmöglichkeit
            Tagesplan? quelle = null;
            int vonStunde = 0;
            int? zeile = null;
            if (s.StartsWith("tag:") && teile.Length > 4)
            {
                var vonDatum = DateOnly.ParseExact(teile[1], "yyyyMMdd");
                quelle = Tagesplan.Berechnen(D, vonDatum);
                vonStunde = int.Parse(teile[2]);
                zeile = int.Parse(teile[4]) is var zz && zz >= 0 ? zz : null;
                if (_woche is DateOnly wq) _ziehQuelle = (vonDatum.DayNumber - wq.DayNumber, vonStunde);
            }
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                {
                    if (!_tage.TryGetValue(t, out var tp) || st + _ziehLaenge - 1 > Stunden) { _status[t, st - 1] = Feldstatus.Kollision; continue; }
                    // Stunde bzw. ganzer Block: Tausch nur, wenn der Unterricht am Ziel kollisionsfrei an die Quelle passt
                    _status[t, st - 1] = quelle != null
                        ? Tagesverlegung.Zielstatus(D, quelle, vonStunde, uid, zeile, _ziehLaenge, tp, st)
                        : AusSchwere(Enumerable.Range(st, _ziehLaenge).Max(x => Schwere(Vertretungen.Feldstatus(D, tp, lehrer, klassen, x, uid, raum))));
                }
        }
        else if (s.StartsWith("plan:"))
        {
            var (prim, alle) = PlanDaten(s);
            var gleicherTag = prim is null ? new List<Planstunde>() : alle.Where(x => x.Tag == prim.Tag && x.UnterrichtId == prim.UnterrichtId).ToList();
            _ziehLaenge = Math.Max(1, gleicherTag.Count);
            _ziehVersatz = prim is null || gleicherTag.Count == 0 ? 0 : prim.Stunde - gleicherTag.Min(x => x.Stunde);
            if (prim is null) { _status = null; e.Effect = DragDropEffects.None; return; }
            // eine Stunde oder ein Block eines Unterrichts: geprüfter Zielstatus (Tausch nur, wenn beide Seiten kollisionsfrei sind)
            var einBlock = gleicherTag.Count == alle.Count;
            if (einBlock) _ziehQuelle = (prim.Tag, gleicherTag.Min(x => x.Stunde));
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    _status[t, st - 1] = einBlock
                        ? Planungsdienst.Zielstatus(D, Ix, alle, t, st - _ziehVersatz)
                        : Kombiniert(alle.Select(x => (x, x.Tag - prim.Tag + t, x.Stunde - prim.Stunde + st)).ToList(), alle);
        }
        else if (s.StartsWith("unterricht:"))
        {
            var uids = s[11..].Split(',').Select(x => int.TryParse(x, out var i) ? Ix.Unterrichte.GetValueOrDefault(i) : null).OfType<Unterricht>().ToList();
            if (uids.Count == 0) { _status = null; e.Effect = DragDropEffects.None; return; }
            var laenge = uids[0].Bloecke().FirstOrDefault(b => b > 0);
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    _status[t, st - 1] = uids.Select(u => Planungsdienst.Status(D, Ix, u, t, st, Math.Max(1, uids.Count == 1 ? laenge : 1))).Max(Schwere) is var m && m < 3 ? AusSchwere(m) : Feldstatus.Kollision; // nicht verplante Stunden können nicht getauscht werden
        }
        else { _status = null; e.Effect = DragDropEffects.None; return; }
    }

    private static int Schwere(Feldstatus f) => f switch { Feldstatus.Frei => 0, Feldstatus.KeinRaum => 1, Feldstatus.VorgabeVerletzt => 2, Feldstatus.TauschMoeglich => 3, _ => 4 };
    private static Feldstatus AusSchwere(int s) => s switch { 0 => Feldstatus.Frei, 1 => Feldstatus.KeinRaum, 2 => Feldstatus.VorgabeVerletzt, 3 => Feldstatus.TauschMoeglich, _ => Feldstatus.Kollision };

    private Feldstatus Kombiniert(List<(Planstunde P, int Tag, int Stunde)> ziele, List<Planstunde> ignorieren)
    {
        var schlimmste = 0;
        foreach (var (p, t, s) in ziele)
        {
            if (t < 0 || t >= Tage || s < 1 || s > Stunden) return Feldstatus.Kollision;
            if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            var f = Planungsdienst.Status(D, Ix, u, t, s, 1, ignorieren);
            if (f == Feldstatus.TauschMoeglich && ziele.Count > 1) f = Feldstatus.Kollision;
            schlimmste = Math.Max(schlimmste, Schwere(f));
        }
        return AusSchwere(schlimmste);
    }

    private (List<int> Lehrer, List<int> Klassen, int Uid, int? Raum) TagesDragDaten(string s)
    {
        if (s.StartsWith("vormerkung:") && int.TryParse(s[11..], out var vid) && D.Vormerkungen.FirstOrDefault(x => x.Id == vid) is { } vm)
            return (vm.LehrerIds, vm.KlassenIds, -1, vm.RaumId);
        var t = s.Split(':');
        if (t.Length < 5) return (new(), new(), -1, null);
        var datum = DateOnly.ParseExact(t[1], "yyyyMMdd");
        var stunde = int.Parse(t[2]);
        var uid = int.Parse(t[3]);
        var zeile = int.Parse(t[4]);
        var tp = Tagesplan.Berechnen(D, datum);
        var e = tp.Stunde(stunde).Where(x => x.Findetstatt && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == uid && (zeile < 0 || (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex) == zeile)).ToList();
        var lehrer = e.SelectMany(x => x.Regel?.LehrerId is int l ? new[] { l } : x.LehrerIds.ToArray()).Distinct().ToList();
        return (lehrer, e.SelectMany(x => x.KlassenIds).Distinct().ToList(), uid, e.SelectMany(x => x.RaumIds).Cast<int?>().FirstOrDefault());
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        if (_status is null) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
        var h = Treffer(PointToClient(new Point(e.X, e.Y)));
        if (h != _hover)
        {
            _hover = h;
            Invalidate();
        }
    }

    protected override void OnDragLeave(EventArgs e) => ZiehenBeenden();

    /// <summary>Ende des Ziehens: Zielfarben, Pfeilanzeige und Mausposition zurücksetzen.</summary>
    public void ZiehenBeenden()
    {
        _ziehLaenge = 1;
        _ziehVersatz = 0;
        _ziehQuelle = null;
        _status = null;
        _hover = null;
        Invalidate();
    }

    /// <summary>
    /// Ziehen ohne Maus nachstellen (Selbsttest, Dokumentation): Zielfarben für die gezogenen Daten berechnen und die Maus
    /// über (tag, stunde) stellen – zeigt mögliche Tauschpartner und die Tausch-Pfeilanzeige.
    /// </summary>
    public void Ziehvorschau(string daten, int tag, int stunde)
    {
        if (!ZiehenBeginnen(daten)) return;
        _hover = (tag, stunde);
        Invalidate();
    }

    /// <summary>Status des Feldes unter der Maus während des Ziehens (null = es wird nicht gezogen).</summary>
    public Feldstatus? ZielstatusUnterMaus => _status != null && _hover is { } h ? _status[h.Tag, h.Stunde - 1] : null;

    protected override void OnDragDrop(DragEventArgs e)
    {
        var h = Treffer(PointToClient(new Point(e.X, e.Y)));
        ZiehenBeenden();
        if (h is null || e.Data?.GetData(DataFormats.Text) is not string s) return;
        Abgelegt?.Invoke(s, h.Value.Tag, h.Value.Stunde);
    }

    public void AuswahlLeeren()
    {
        Ausgewaehlt = null;
        Mehrfach.Clear();
        TagesAuswahl = null;
        Invalidate();
    }
}
