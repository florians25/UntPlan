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

    /// <summary>Zeitbereich „Tag“ wie im Vorbild: nur dieser Wochentag (Index) wird über die ganze Breite gezeigt, null = alle Tage.</summary>
    public int? NurTag
    {
        get => _nurTag;
        set { _nurTag = value is int t && t >= 0 && t < Tage ? t : null; Invalidate(); }
    }
    private int? _nurTag;
    private int SichtbareTage => _nurTag.HasValue ? 1 : BisT - VonT + 1;

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

    /// <summary>
    /// Stundenplanmodus (Regelplan): Montag der im Fenster gewählten Woche – unterrichtsfreie Tage dieser Woche (Feiertag, Kalendertag
    /// „unterrichtsfrei“) werden wie im Vorbild als weiße Spalte mit senkrechtem Text („3.9.2021“) gezeigt. Ist die ganze Woche frei
    /// (Ferien), bleibt der Regelplan sichtbar.
    /// </summary>
    public DateOnly? Anzeigewoche { get; set; }

    /// <summary>Datum eines Tages im Regelplan (über <see cref="Anzeigewoche"/>), null im tagesaktuellen Plan bzw. ohne Woche.</summary>
    private DateOnly? RegelDatum(int tag) => !Tagesmodus && Anzeigewoche is DateOnly w ? w.AddDays(tag + D.Zeitraster.ErsterSchultag) : null;

    /// <summary>Ist der Tag im Regelplan als unterrichtsfreie Spalte verdeckt (nicht die ganze Woche frei)?</summary>
    private bool RegelTagFrei(int tag)
    {
        if (RegelDatum(tag) is not DateOnly dt || !Kalender.GanzerTagFrei(D, dt)) return false;
        return Enumerable.Range(0, Tage).Any(t => RegelDatum(t) is DateOnly x && !Kalender.GanzerTagFrei(D, x));
    }
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
    /// <summary>Eine Zelle ohne Unterricht (nicht verplante Stunde) wurde angeklickt: Spalte (Tag bzw. Tag der Woche) und Stunde.</summary>
    public event Action<int, int>? LeereZelleGewaehlt;

    /// <summary>Zuletzt angeklickte bzw. aus einem verknüpften Plan übernommene Zelle (Spalte, Stunde).</summary>
    public (int Tag, int Stunde)? MarkierteZelle => _zelle;

    /// <summary>Anzahl der Tagesspalten (Tage pro Woche).</summary>
    public int Spalten => Tage;

    /// <summary>
    /// Leere Zelle aus einem verknüpften Plan übernehmen: bisherige Auswahl aufheben und dieselbe Stunde gestrichelt markieren
    /// (Tag und Stunde rosa) – das angezeigte Element bleibt.
    /// </summary>
    public void ZelleMarkieren(int tag, int stunde)
    {
        Ausgewaehlt = null;
        Mehrfach.Clear();
        TagesAuswahl = null;
        MarkierterUnterricht = null;
        _zelle = tag >= 0 && tag < Tage && stunde >= 1 && stunde <= Stunden ? (tag, stunde) : null;
        Invalidate();
    }
    public event Action<string, int, int>? Abgelegt;
    public event Action<Planstunde>? Doppelklick;
    public event Action<Point, Planstunde?>? Kontext;
    public event Action<Point, DateOnly, int, TagesEintrag?>? TagesKontext;
    /// <summary>Doppelklick auf eine Stunde im tagesaktuellen Plan (z. B. entfallene Stunde wiederherstellen).</summary>
    public event Action<DateOnly, TagesEintrag>? TagesDoppelklick;

    /// <summary>Einträge des Elements an einem Datum der angezeigten Woche (inkl. Entfälle), leer außerhalb der Woche.</summary>
    public List<TagesEintrag> EintraegeAmDatum(DateOnly datum, int stunde)
    {
        if (_woche is not DateOnly w) return new();
        var tag = datum.DayNumber - w.DayNumber;
        if (tag < 0 || tag >= Tage) return new();
        if (_tage.Count != Tage) TageNeu();
        return EintraegeAm(tag, stunde);
    }

    private DateOnly? _woche;
    private readonly Dictionary<int, Tagesplan> _tage = new();
    private Feldstatus[,]? _status;
    private (int Tag, int Stunde)? _hover;
    private Point _down;
    private Planstunde? _downStunde;
    private (DateOnly, TagesEintrag)? _downEintrag;
    private readonly ToolTip _tip = new() { InitialDelay = 500 };
    private string _letzterTip = "";

    private int KopfHoehe => KlassikStil ? (Format.Klein ? 20 : 30) : Format.Klein ? 17 : 22;
    // im Druck etwas breiter, damit „10:40-11:25“ vollständig in die Stundenspalte passt
    private int KopfBreite => (KlassikStil ? (Format.Klein ? 26 : Format.ZeitenAnzeigen ? 72 : 34) : Format.Klein ? 20 : Format.ZeitenAnzeigen ? 74 : 32) + (_druck && Format.ZeitenAnzeigen && !Format.Klein ? 12 : 0);
    private int PausenHoehe => KlassikStil ? (Format.Klein ? 15 : 16) : Format.Klein ? 12 : 15;

    /// <summary>
    /// Darstellung wie im Vorbild im Vertretungsmodus: Kopf und Stundenspalte mintgrün, belegte Stunden mintgrün, freie weiß,
    /// schwarze dünne Rasterlinien (doppelt unter dem Kopf und rechts der Stundenspalte), Pausenzeilen weiß über die Tage,
    /// Tag und Stunde der gewählten Zelle rosa, gewählte Stunde dünn rot-gelb gestrichelt, Entfall rot durchgestrichen.
    /// </summary>
    /// <remarks>
    /// Seit UP5 auch im Stundenplanmodus (Vorbild-Stundenplanmodus: hellblau statt mintgrün, siehe <see cref="Grund"/>), kleine und
    /// große Pläne; eigene Stundenformate (Felder) behalten ihre Fachfarben.
    /// </remarks>
    public static bool KlassikStil => true;
    /// <summary>Mintgrün der Vorbild-Stundenpläne im Vertretungsmodus (Kopf, Stundenspalte, belegte Stunden).</summary>
    public static readonly Color Mint = Color.FromArgb(175, 223, 204);
    /// <summary>Hellblau der Vorbild-Stundenpläne im Stundenplanmodus (Kopf, Stundenspalte, belegte Stunden).</summary>
    public static readonly Color Hellblau = Color.FromArgb(220, 240, 255);
    /// <summary>Grundfarbe der Pläne: hellblau im Stundenplanmodus, mintgrün im Vertretungsmodus.</summary>
    public static Color Grund => AppZustand.Vertretungsmodus ? Mint : Hellblau;
    /// <summary>Rosa für Tag und Stunde der gewählten Zelle.</summary>
    public static readonly Color Rosa = Color.FromArgb(255, 210, 210);
    private static readonly Color AuswahlRot = Color.FromArgb(255, 128, 128);
    private static readonly Color AuswahlBlau = Color.FromArgb(90, 90, 255);
    private static readonly Color EntfallRot = Color.FromArgb(235, 0, 0);

    /// <summary>Zuletzt angeklickte Zelle (auch leer) – wird wie im Vorbild gestrichelt umrandet, Tag und Stunde rosa.</summary>
    private (int Tag, int Stunde)? _zelle;

    /// <summary>Tag und Stunde der gewählten Stunde bzw. der angeklickten leeren Zelle.</summary>
    private (int Tag, int Stunde)? GewaehlteZelle()
    {
        if (Tagesmodus && _tagesAuswahl is { } ta && _woche is DateOnly w) return (ta.Datum.DayNumber - w.DayNumber, ta.Eintrag.Stunde);
        if (!Tagesmodus && _ausgewaehlt is { } p) return (p.Tag, p.Stunde);
        return _zelle;
    }

    /// <summary>Angezeigte Pausen (eigene schmale Zeilen), nach der Stunde sortiert; „nach der 0. Stunde“ = vor der ersten Stunde (Frühaufsicht).</summary>
    private List<Pause> PausenZeilen() =>
        Format.PausenAnzeigen && !Transponiert ? D.Zeitraster.Pausen.Where(p => p.NachStunde >= 0 && p.NachStunde < Stunden && p.NachStunde >= VonStd - 1 && p.NachStunde < BisStd).GroupBy(p => p.NachStunde).Select(g => g.First()).OrderBy(p => p.NachStunde).ToList() : new();

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

    // Format „Auswahl (Bereich)“: angezeigte Stunden und Tage (ausgeblendete liegen außerhalb des sichtbaren Bereichs)
    private int VonStd => Math.Clamp(Format.VonStunde, 1, Math.Max(1, Stunden));
    private int BisStd => Format.BisStunde <= 0 ? Stunden : Math.Clamp(Format.BisStunde, VonStd, Math.Max(VonStd, Stunden));
    private int SichtbareStunden => Math.Max(1, BisStd - VonStd + 1);
    private int VonT => Math.Clamp(Format.VonTag, 0, Math.Max(0, Tage - 1));
    private int BisT => Format.BisTag < 0 ? Tage - 1 : Math.Clamp(Format.BisTag, VonT, Math.Max(VonT, Tage - 1));
    private bool TagSichtbar(int t) => t >= VonT && t <= BisT;
    private bool StundeSichtbar(int s) => s >= VonStd && s <= BisStd;
    /// <summary>Format 10: Stunden waagrecht, Tage senkrecht (nicht im kleinen Stundenplan).</summary>
    public bool Transponiert => Format.Grundform == Grundform.Format10 && !Format.Klein;

    /// <summary>Für den Selbsttest: sichtbare Stunden und Tage (Format „Auswahl (Bereich)“).</summary>
    internal (int VonStunde, int BisStunde, int VonTag, int BisTag) SichtbarerBereich => (VonStd, BisStd, VonT, BisT);

    /// <summary>Rechteck über mehrere Stunden eines Tages (in beiden Ausrichtungen).</summary>
    private RectangleF Bereich(Rectangle bounds, int tag, int von, int bis)
    {
        von = Math.Max(von, VonStd);
        bis = Math.Min(bis, BisStd);
        if (bis < von) return new RectangleF(-100000, -100000, 0, 0);
        return RectangleF.Union(Zelle(bounds, tag, von), Zelle(bounds, tag, bis));
    }

    /// <summary>Tagesaktuelle Pläne neu berechnen (nach Änderungen).</summary>
    public void TageNeu()
    {
        _tage.Clear();
        _eintraegeCache.Clear();
        _vaTexte.Clear();
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
        var zeileT = _nurTag is int nt0 ? (tag == nt0 ? 0 : -100000) : TagSichtbar(tag) ? tag - VonT : -100000;
        var spalteS = StundeSichtbar(stunde) ? stunde - VonStd : -100000;
        if (Transponiert)
        {
            // Format 10: Stunden als Spalten, Tage als Zeilen (ohne Pausenzeilen)
            var ws = (bounds.Width - KopfBreite - 1) / (float)SichtbareStunden;
            var ht = Math.Max(_druck ? 4f : MindestZeile, (bounds.Height - KopfHoehe - 1) / (float)SichtbareTage);
            var tx0 = bounds.X + KopfBreite + (int)Math.Round(spalteS * ws);
            var tx1 = bounds.X + KopfBreite + (int)Math.Round((spalteS + 1) * ws);
            var ty0 = bounds.Y + KopfHoehe + (int)Math.Round(zeileT * ht);
            var ty1 = bounds.Y + KopfHoehe + (int)Math.Round((zeileT + 1) * ht);
            return new RectangleF(tx0, ty0, tx1 - tx0, ty1 - ty0);
        }
        var pausen = Pausen;
        var w = (bounds.Width - KopfBreite - 1) / (float)SichtbareTage;
        // Mindestzeilenhöhe: Zeilen werden nie gequetscht – reicht der Platz nicht, scrollt der Plan (StundenplanFenster)
        var h = Math.Max(_druck ? 4f : MindestZeile, (bounds.Height - KopfHoehe - 1 - pausen.Count * PausenHoehe) / (float)SichtbareStunden);
        var vor = 0;
        foreach (var p in pausen) if (p.NachStunde < stunde) vor++;
        // ganze Pixel, damit die (schwarzen) Rasterlinien gleichmäßig und ohne Lücken zwischen Linie und Zelle liegen
        // Zeitbereich „Tag“ bzw. Formatbereich: die übrigen Tage/Stunden liegen außerhalb des sichtbaren Bereichs (gleiche Größe, damit Schriften gleich bleiben)
        var x0 = bounds.X + KopfBreite + (int)Math.Round(zeileT * w);
        var x1 = bounds.X + KopfBreite + (int)Math.Round((zeileT + 1) * w);
        var y0 = bounds.Y + KopfHoehe + (int)Math.Round(spalteS * h) + vor * PausenHoehe;
        var y1 = bounds.Y + KopfHoehe + (int)Math.Round((spalteS + 1) * h) + vor * PausenHoehe;
        return new RectangleF(x0, y0, x1 - x0, y1 - y0);
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
        if (Transponiert)
        {
            for (var tt = 0; tt < Tage; tt++)
                for (var ss = VonStd; ss <= BisStd; ss++)
                    if (Zelle(ClientRectangle, tt, ss).Contains(p)) return RegelTagFrei(tt) ? null : (tt, ss);
            return null;
        }
        var w = (Width - KopfBreite - 1) / (float)SichtbareTage;
        var t = (int)((p.X - KopfBreite) / w) + VonT;
        if (_nurTag is int nt) t = t == VonT ? nt : -1;
        if (t < 0 || t >= Tage || RegelTagFrei(t)) return null;
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
    /// <summary>
    /// Einträge je Tag und Stunde zwischengespeichert: Zeichnen, Trefferprüfung bei Mausbewegung und Anordnung fragen sie sehr oft ab
    /// (bei einer Veranstaltung mit allen Lehrkräften Tausende Einträge je Tag). Verworfen mit <see cref="TageNeu"/>.
    /// </summary>
    private readonly Dictionary<(ElementTyp, int, int, int), List<TagesEintrag>> _eintraegeCache = new();

    public List<TagesEintrag> EintraegeAm(int tag, int stunde)
    {
        if (!_tage.ContainsKey(tag)) return new();
        var key = (Typ, ElementId, tag, stunde);
        if (!_eintraegeCache.TryGetValue(key, out var liste)) _eintraegeCache[key] = liste = EintraegeBerechnen(tag, stunde);
        return liste.ToList();
    }

    private List<TagesEintrag> EintraegeBerechnen(int tag, int stunde)
    {
        var tp = _tage[tag];
        return tp.Stunde(stunde).Where(e => Typ switch
        {
            ElementTyp.Klasse => e.KlassenIds.Contains(ElementId) || (e.Regel?.KlassenIds.Contains(ElementId) ?? false),
            ElementTyp.Lehrer => e.LehrerIds.Contains(ElementId) || e.Regel?.LehrerId == ElementId,
            ElementTyp.Raum => e.RaumIds.Contains(ElementId) || e.Regel?.RaumId == ElementId,
            ElementTyp.Fach => e.FachId == ElementId || e.Regel?.FachId == ElementId,
            _ => false,
        }).GroupBy(e => (e.Unterricht?.Id ?? VeranstaltungsSchluessel(e) ?? -(e.Vertretung?.Id ?? 0), Typ == ElementTyp.Lehrer || Typ == ElementTyp.Raum ? (e.Regel?.ZeilenIndex ?? 0) : 0, e.Status == TagesStatus.Entfall))
          .Select(g => g.First()).OrderBy(e => e.Status == TagesStatus.Entfall).ToList();
    }

    /// <summary>
    /// Vorbild-Darstellung: die Einträge einer Veranstaltung (je beteiligte Lehrkraft eine Zeile) erscheinen im Klassen- und
    /// Raumplan als eine Stunde statt als viele schmale Spuren.
    /// </summary>
    private readonly Dictionary<(int, DateOnly, ElementTyp), string> _vaTexte = new();

    private Veranstaltung? VeranstaltungVon(TagesEintrag e) =>
        e.Vertretung is { Art: VertretungsArt.Veranstaltung, VeranstaltungId: int id } ? D.Veranstaltungen.FirstOrDefault(x => x.Id == id) : null;

    /// <summary>Klassen eines Eintrags; bei einer Veranstaltung kompakt („Alle Klassen“ statt Dutzender Kurznamen).</summary>
    private string KlassenText(TagesEintrag e)
    {
        if (e.KlassenIds.Count < 3 || VeranstaltungVon(e) is not { } va) return Ix.KKlassen(e.KlassenIds);
        var key = (va.Id, e.Vertretung!.Datum, ElementTyp.Klasse);
        if (!_vaTexte.TryGetValue(key, out var t)) _vaTexte[key] = t = VeranstaltungsLogik.KlassenKurztext(D, va);
        return t;
    }

    /// <summary>Klassen- und Raumplan: die Veranstaltung steht als eine Stunde – mit allen Lehrkräften („Alle Lehrkräfte“) statt der ersten.</summary>
    private string? VeranstaltungsLehrer(TagesEintrag e)
    {
        if (Typ == ElementTyp.Lehrer || VeranstaltungVon(e) is not { } va) return null;
        var key = (va.Id, e.Vertretung!.Datum, ElementTyp.Lehrer);
        if (!_vaTexte.TryGetValue(key, out var t)) _vaTexte[key] = t = VeranstaltungsLogik.LehrerKurztext(D, va, e.Vertretung.Datum);
        return t;
    }

    private int? VeranstaltungsSchluessel(TagesEintrag e) =>
        KlassikStil && Typ != ElementTyp.Lehrer && e.Vertretung?.VeranstaltungId is int va ? int.MinValue / 2 - va : null;

    private Planstunde? StundeBei(Point p)
    {
        var t = Treffer(p);
        if (t is null) return null;
        var f = RegelFelder(t.Value.Tag).FirstOrDefault(x => FeldTrifft(ClientRectangle, t.Value.Tag, x, p));
        return f is null ? null : f.Alle.FirstOrDefault(x => x.Stunde == t.Value.Stunde) ?? f.Erster;
    }

    private (DateOnly Datum, TagesEintrag Eintrag)? EintragBei(Point p)
    {
        var t = Treffer(p);
        if (t is null || _woche is null) return null;
        var f = TagesFelder(t.Value.Tag).FirstOrDefault(x => FeldTrifft(ClientRectangle, t.Value.Tag, x, p));
        if (f is null) return null;
        return (_woche.Value.AddDays(t.Value.Tag), f.Alle.FirstOrDefault(x => x.Stunde == t.Value.Stunde) ?? f.Erster);
    }

    // ------------------------------------------------------------------------------------------
    // Anordnung: zusammenhängende Stunden als ein Feld; gleichzeitige Stunden und Blöcke immer nebeneinander (Spuren)
    // ------------------------------------------------------------------------------------------

    /// <summary>Stunden, nach denen eine Pausenzeile angezeigt wird (Felder werden dort geteilt, nie über die Pause gezeichnet).</summary>
    private HashSet<int> PausenNach => Pausen.Select(p => p.NachStunde).Where(n => n >= 1).ToHashSet();

    private static List<PlanBlock<T>> Anordnen<T>(int stunden, Func<int, IReadOnlyList<T>> am, Func<T, string?> schluessel, bool stapeln = false) =>
        Blockbildung.Anordnen(stunden, am, schluessel, stapeln);

    private List<PlanBlock<Planstunde>> RegelFelder(int tag)
    {
        var plan = Ix.PlanVon(Typ, ElementId).Where(p => p.Tag == tag).ToList();
        return Anordnen<Planstunde>(Stunden, s => plan.Where(p => p.Stunde == s).ToList(), RegelSchluessel, Stapeln);
    }

    /// <summary>Große Pläne: gleichzeitige Unterrichte untereinander in einem Feld, im kleinen Plan nebeneinander.</summary>
    private bool Stapeln => KlassikStil && !Format.Klein;

    /// <summary>Zusammenfassung zu Doppelstunden/Blöcken (nicht in der Darstellung „Einzelstunden“).</summary>
    private string? RegelSchluessel(Planstunde p) => Format.Einzelstunden ? null : p.UnterrichtId.ToString();

    /// <summary>Felder des tagesaktuellen Plans; Entfälle werden in der Vorbild-Darstellung je Stunde einzeln gezeigt.</summary>
    private List<PlanBlock<TagesEintrag>> TagesFelder(int tag) => Anordnen<TagesEintrag>(Stunden, s => EintraegeAm(tag, s),
        e => Format.Einzelstunden || (KlassikStil && e.Status == TagesStatus.Entfall) ? null : TagesSchluessel(e));

    /// <summary>Rechtecke eines Feldes: ein Teil je Abschnitt zwischen Pausenzeilen (eine Pause wird nie überdeckt).</summary>
    private List<RectangleF> FeldRechtecke<T>(Rectangle bounds, int tag, PlanBlock<T> f)
    {
        var pausen = PausenNach;
        return Blockbildung.Teile(f, pausen).Select(t => TeilRechteck(bounds, tag, f, t)).ToList();
    }

    private bool FeldTrifft<T>(Rectangle bounds, int tag, PlanBlock<T> f, Point p) => FeldRechtecke(bounds, tag, f).Any(r => r.Contains(p));

    private RectangleF TeilRechteck<T>(Rectangle bounds, int tag, PlanBlock<T> f, BlockTeil t)
    {
        var start = Math.Max(t.Start, VonStd);
        var ende = Math.Min(t.Ende, BisStd);
        if (ende < start) return new RectangleF(-100000, -100000, 0, 0);
        var r = Zelle(bounds, tag, start);
        if (Transponiert)
        {
            // Format 10: mehrstündige Felder waagrecht, parallele Stunden übereinander
            var breiteT = ende == start ? r.Width : Zelle(bounds, tag, ende).Right - r.X;
            var ty0 = (int)(r.Y + Math.Round(f.Spur * r.Height / f.Spuren));
            var ty1 = (int)(r.Y + Math.Round((f.Spur + 1) * r.Height / f.Spuren));
            var tx0 = (int)(r.X + Math.Round(f.StapelIndex * breiteT / f.StapelAnzahl));
            var tx1 = (int)(r.X + Math.Round((f.StapelIndex + 1) * breiteT / f.StapelAnzahl));
            return new RectangleF(tx0 + 1, ty0 + 1, tx1 - tx0 - 1, ty1 - ty0 - 1);
        }
        var hoehe = ende == start ? r.Height : Zelle(bounds, tag, ende).Bottom - r.Y;
        if (KlassikStil)
        {
            // Vorbild: Feld füllt die Zelle bis an die Rasterlinien, parallele Stunden durch eine Linie getrennt
            var x0 = (int)(r.X + Math.Round(f.Spur * r.Width / f.Spuren));
            var x1 = (int)(r.X + Math.Round((f.Spur + 1) * r.Width / f.Spuren));
            var y0 = (int)(r.Y + Math.Round(f.StapelIndex * hoehe / f.StapelAnzahl));
            var y1 = (int)(r.Y + Math.Round((f.StapelIndex + 1) * hoehe / f.StapelAnzahl));
            return new RectangleF(x0 + 1, y0 + 1, x1 - x0 - 1, y1 - y0 - 1);
        }
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

    private PlanBlock<TagesEintrag>? TagesFeld(int tag, int stunde, TagesEintrag e) =>
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

    private bool _druck;

    public void Zeichnen(Graphics g, Rectangle bounds, bool druck)
    {
        if (Tagesmodus && _tage.Count != Tage) TageNeu();
        AuswahlAktualisieren();
        _pausenCache = PausenZeilen();
        _druck = druck;
        try { ZeichnenIntern(g, bounds, druck); }
        finally { _pausenCache = null; _druck = false; }
    }

    /// <summary>Mausrad: den umgebenden Rahmen scrollen (der Plan ist höher als das Fenster).</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Parent is ScrollableControl sc && sc.AutoScroll && sc.VerticalScroll.Visible)
        {
            var schritt = Math.Max(12, MindestZeile) * 2;
            var y = Math.Max(0, -sc.AutoScrollPosition.Y - Math.Sign(e.Delta) * schritt);
            sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, y);
            return;
        }
        base.OnMouseWheel(e);
    }

    /// <summary>Mindesthöhe einer Stundenzeile (lesbar, auch bei vielen Stunden).</summary>
    public int MindestZeile => Format.ZellenHoehe > 0 ? Math.Min(Format.ZellenHoehe, Blockbildung.MindestZeilenHoehe(Format.Schriftgroesse, Format.Klein))
        : Blockbildung.MindestZeilenHoehe(Format.Schriftgroesse, Format.Klein);

    /// <summary>Höhe, unter der der Plan nicht gequetscht, sondern gescrollt wird.</summary>
    public int MindestHoehe => Blockbildung.MindestHoehe(KopfHoehe, PausenZeilen().Count, PausenHoehe, Transponiert ? SichtbareTage : SichtbareStunden, MindestZeile);

    private void ZeichnenIntern(Graphics g, Rectangle bounds, bool druck)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.FillRectangle(Brushes.White, bounds);
        FreiBeschriftungen.Clear();
        if (!druck) GezeichneteZeilen.Clear();
        var zr = D.Zeitraster;
        var klein = Format.Klein;
        var klassik = KlassikStil;
        using var linie = new Pen(klassik ? Color.Black : klein ? Color.FromArgb(150, 150, 150) : Color.FromArgb(160, 170, 185));
        using var duenn = new Pen(klassik ? Color.Black : klein ? Color.FromArgb(200, 200, 200) : Color.FromArgb(210, 216, 226));
        GezeichneteLinien.Clear();
        GezeichneteFelder.Clear();
        if (Transponiert) TransponierteKoepfe(g, bounds, druck, linie);
        else if (klassik) KlassikKoepfe(g, bounds, druck, linie);
        else KoepfeZeichnen(g, bounds, druck, linie);
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
                var hint = Format.FreieStunden.Grau ? Color.FromArgb(236, 236, 236) : druck || klassik ? Color.White : Ui.PlanLeer;
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
                Relief(g, r, Format.FreieStunden.Raeumlich);
            }
            // zweiter Durchgang: Inhalte; zusammenhängende Stunden (Doppelstunde, Block) werden als ein Feld gezeichnet
            // (außer in der Darstellung „Einzelstunden“), parallele Blöcke nebeneinander
            if (Tagesmodus)
            {
                foreach (var f in TagesFelder(t))
                    foreach (var fr in FeldRechtecke(bounds, t, f))
                    {
                        EintragZeichnen(g, fr, f.Erster, datum!.Value, druck, f.Alle.Any(x => IstTagesAuswahl(datum.Value, x)));
                        if (klassik) g.DrawRectangle(linie, fr.X - 1, fr.Y - 1, fr.Width + 1, fr.Height + 1);
                        Relief(g, fr, Format.BesetzteStunden.Raeumlich);
                    }
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
                var felder = Anordnen<Planstunde>(Stunden, s => tagPlan.Where(p => p.Stunde == s).ToList(), RegelSchluessel, Stapeln);
                foreach (var f in felder)
                    foreach (var fr in FeldRechtecke(bounds, t, f))
                    {
                        StundeZeichnen(g, fr, f.Erster, druck, Enumerable.Range(f.Start, f.Laenge).Any(kollisionen.Contains), f.Alle);
                        if (klassik) g.DrawRectangle(linie, fr.X - 1, fr.Y - 1, fr.Width + 1, fr.Height + 1);
                        Relief(g, fr, Format.BesetzteStunden.Raeumlich);
                    }
                // untereinander gestapelte Unterrichte bilden ein gemeinsames Feld: keine Linie zwischen den Zeilen
                if (klassik)
                    using (var grund = new SolidBrush(Grund))
                        foreach (var f in felder.Where(f => f.StapelIndex > 0))
                            foreach (var fr in FeldRechtecke(bounds, t, f))
                                g.FillRectangle(grund, fr.X, fr.Y - 1, fr.Width, 1);
            }
            // Vorbild: angeklickte leere Zelle rot-gelb gestrichelt
            if (klassik && !druck && _zelle is { } zc && zc.Tag == t && zc.Stunde >= 1 && zc.Stunde <= Stunden && (Tagesmodus ? _tagesAuswahl is null : _ausgewaehlt is null))
            {
                var r = Zelle(bounds, t, zc.Stunde);
                AuswahlRahmen(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1));
            }
            ZiehMarken(g, bounds, druck, t, plan);
            if (datum is DateOnly dt) FreiZeichnen(g, bounds, t, dt, kt);
        }
        PausenZeichnen(g, bounds, linie);
        FormatLinien(g, bounds);
        // Vorbild-Stundenplanmodus: unterrichtsfreier Tag als weiße Spalte mit senkrechtem Datum bzw. Bezeichnung über alle Stunden
        for (var t = 0; t < Tage; t++)
        {
            var frei = Tagesmodus ? !AppZustand.Vertretungsmodus && _woche?.AddDays(t) is DateOnly wd && Kalender.GanzerTagFrei(D, wd) ? wd : (DateOnly?)null
                : RegelTagFrei(t) ? RegelDatum(t) : null;
            if (frei is DateOnly fd) FreiSpalteZeichnen(g, bounds, t, fd, linie);
        }
        if (!druck) TauschAnzeigen(g, bounds);
        if (klassik)
        {
            // doppelte Linien unter dem Kopf und rechts der Stundenspalte, Rahmen um den ganzen Plan
            g.FillRectangle(Brushes.Black, bounds.X, bounds.Y + KopfHoehe - 1, bounds.Width, 2);
            g.FillRectangle(Brushes.Black, bounds.X + KopfBreite - 1, bounds.Y, 2, bounds.Height);
            g.FillRectangle(Brushes.Black, bounds.X, bounds.Y, bounds.Width, 2);
            g.FillRectangle(Brushes.Black, bounds.X, bounds.Y, 2, bounds.Height);
            g.FillRectangle(Brushes.Black, bounds.Right - 2, bounds.Y, 2, bounds.Height);
            g.FillRectangle(Brushes.Black, bounds.X, bounds.Bottom - 2, bounds.Width, 2);
        }
        else g.DrawRectangle(linie, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    /// <summary>
    /// Selbsttest (UP-01): Felder dürfen keine Pausenzeile überdecken, müssen im Plan liegen und nicht über den Rand hinausragen;
    /// Stundenzeilen haben mindestens die Mindesthöhe. Liefert die gefundenen Fehler.
    /// </summary>
    internal List<string> SelbsttestBlockPruefung()
    {
        var fehler = new List<string>();
        var bounds = ClientRectangle;
        _pausenCache = PausenZeilen();
        try
        {
            var pausen = Pausen.Select(p => PausenRechteck(bounds, p)).ToList();
            for (var t = 0; t < Tage; t++)
            {
                if (_nurTag is int nt && nt != t) continue;
                var rechtecke = Tagesmodus
                    ? TagesFelder(t).SelectMany(f => FeldRechtecke(bounds, t, f).Select(r => (f.Start, f.Laenge, r))).ToList()
                    : RegelFelder(t).SelectMany(f => FeldRechtecke(bounds, t, f).Select(r => (f.Start, f.Laenge, r))).ToList();
                foreach (var (start, laenge, r) in rechtecke)
                {
                    foreach (var p in pausen)
                    {
                        var x = RectangleF.Intersect(r, p);
                        if (x.Height > 1 && x.Width > 1) fehler.Add($"Tag {t + 1}, Stunde {start}-{start + laenge - 1}: Feld überdeckt Pausenzeile");
                    }
                    if (r.Bottom > bounds.Bottom + 1 || r.Right > bounds.Right + 1) fehler.Add($"Tag {t + 1}, Stunde {start}: Feld ragt über den Plan hinaus");
                }
                for (var s = 1; s <= Stunden; s++)
                    if (Zelle(bounds, t, s).Height + 1 < MindestZeile) fehler.Add($"Stunde {s}: Zeile kleiner als die Mindesthöhe");
            }
        }
        finally { _pausenCache = null; }
        return fehler.Distinct().ToList();
    }

    /// <summary>Zuletzt gezeichnete Beschriftungen unterrichtsfreier Zeiten (Tag, Text) – für den Selbsttest.</summary>
    public List<(int Tag, string Text)> FreiBeschriftungen { get; } = new();

    /// <summary>
    /// Unterrichtsfreie Zeiten (Ferien, Feiertage, Kalendertag „unterrichtsfrei“ bzw. „nur diese Stunden unterrichtsfrei“) beschriften:
    /// ohne Bezeichnung das Datum, mit Bezeichnung der Text, gekürzt mit „…“, wenn er nicht in die Spalte passt.
    /// </summary>
    private void FreiZeichnen(Graphics g, Rectangle bounds, int t, DateOnly datum, Kalendertag? kt)
    {
        if (_nurTag is int nt && nt != t) return;
        var bez = Kalender.FreiBezeichnung(D, datum);
        var bereiche = new List<(int Von, int Bis)>();
        if (Kalender.GanzerTagFrei(D, datum)) bereiche.Add((1, Stunden));
        else if (kt != null)
            for (var s = 1; s <= Stunden; s++)
            {
                if (!kt.StundeFrei(s)) continue;
                if (bereiche.Count > 0 && bereiche[^1].Bis == s - 1) bereiche[^1] = (bereiche[^1].Von, s);
                else bereiche.Add((s, s));
            }
        if (bereiche.Count == 0) return;
        using var schrift = new Font("Segoe UI", Format.Klein ? 7f : 8.25f, FontStyle.Bold);
        foreach (var (von, bis) in bereiche)
        {
            var ber = Bereich(bounds, t, von, bis);
            var r = Rectangle.Round(new RectangleF(ber.X + 1, ber.Y + 1, ber.Width - 2, ber.Height - 2));
            if (r.Width < 6 || r.Height < 6) continue;
            var platz = r.Width - 4;
            var text = Kalender.FreiText(bez, datum, x => TextRenderer.MeasureText(g, x, schrift, Size.Empty, TextFormatFlags.NoPadding).Width <= platz);
            FreiBeschriftungen.Add((t, text));
            // Beschriftung oben in der ersten freien Stunde, über dem hellgelben Hintergrund
            var zeile = new Rectangle(r.X, r.Y, r.Width, Math.Min(r.Height, schrift.Height + 4));
            using (var b = new SolidBrush(Color.FromArgb(255, 236, 190))) g.FillRectangle(b, zeile);
            TextRenderer.DrawText(g, text, schrift, zeile, Color.FromArgb(150, 80, 0),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Unterrichtsfreier Tag wie im Vorbild-Stundenplanmodus: die Spalte von der ersten bis zur letzten Stunde weiß (auch über die
    /// Pausenzeilen), darin senkrecht von unten nach oben das Datum („3.9.2021“) bzw. die Bezeichnung des Feiertags.
    /// </summary>
    private void FreiSpalteZeichnen(Graphics g, Rectangle bounds, int t, DateOnly datum, Pen linie)
    {
        if (_nurTag is int nt && nt != t) return;
        var r = Bereich(bounds, t, 1, Stunden);
        if (r.Right < bounds.X || r.Width < 4) return;
        g.FillRectangle(Brushes.White, r);
        g.DrawRectangle(linie, r.X, r.Y, r.Width, r.Height);
        using var schrift = new Font("Segoe UI", Format.Klein ? 8f : 9f);
        var platz = (Transponiert ? r.Width : r.Height) - 8;
        var text = Kalender.FreiText(Kalender.FreiBezeichnung(D, datum), datum, x => g.MeasureString(x, schrift).Width <= platz);
        FreiBeschriftungen.RemoveAll(x => x.Tag == t);
        FreiBeschriftungen.Add((t, text));
        var groesse = g.MeasureString(text, schrift);
        var zustand = g.Save();
        try
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            if (!Transponiert) g.RotateTransform(-90);
            g.DrawString(text, schrift, Brushes.Black, -groesse.Width / 2f, -groesse.Height / 2f);
        }
        finally { g.Restore(zustand); }
    }

    /// <summary>Beim Ziehen: Stunden, mit denen getauscht werden kann, grün; Ziel unter der Maus kräftig grün und gestrichelt umrandet.</summary>
    private void ZiehMarken(Graphics g, Rectangle bounds, bool druck, int t, List<Planstunde> plan)
    {
        if (druck) return;
        if (_status != null)
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
        if (_hover is { } hv && hv.Tag == t)
        {
            var start = Math.Clamp(hv.Stunde - _ziehVersatz, 1, Stunden);
            var n = Math.Max(1, Math.Min(_ziehLaenge, Stunden - start + 1));
            var r = Bereich(bounds, t, start, start + n - 1);
            using var p = new Pen(Color.Black, Rahmen) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
        }
    }

    /// <summary>Farbe der Überschriften (Tage, Stunden): „Eben: Grau“ = Grundfarbe des Plans, „Weiß“ = weiß (Format, Layout 1).</summary>
    private Color KopfFarbe => Format.Ueberschrift.Grau ? (KlassikStil ? Grund : Ui.Kopf) : Color.White;

    /// <summary>Gezeichnete Felder einer eigenen Stundenplan-Stunde (für den Selbsttest).</summary>
    internal List<(StundenFeldArt Art, string Text, Rectangle Bereich)> GezeichneteFelder { get; } = new();

    /// <summary>Gezeichnete Formatlinien (für den Selbsttest), z. B. „dick vor 5“.</summary>
    internal List<string> GezeichneteLinien { get; } = new();

    /// <summary>Räumliche Darstellung (Layout 1): erhaben = hell oben/links und dunkel unten/rechts, versenkt umgekehrt.</summary>
    private static void Relief(Graphics g, RectangleF r, Raeumlich art)
    {
        if (art == Raeumlich.Flach || r.Width < 4 || r.Height < 4 || r.X < -1000 || r.Y < -1000) return;
        var hell = art == Raeumlich.Erhaben ? Color.White : Color.FromArgb(128, 128, 128);
        var dunkel = art == Raeumlich.Erhaben ? Color.FromArgb(128, 128, 128) : Color.White;
        using var ph = new Pen(hell);
        using var pd = new Pen(dunkel);
        g.DrawLine(ph, r.X + 1, r.Y + 1, r.Right - 2, r.Y + 1);
        g.DrawLine(ph, r.X + 1, r.Y + 1, r.X + 1, r.Bottom - 2);
        g.DrawLine(pd, r.X + 1, r.Bottom - 2, r.Right - 2, r.Bottom - 2);
        g.DrawLine(pd, r.Right - 2, r.Y + 1, r.Right - 2, r.Bottom - 2);
    }

    /// <summary>
    /// Linien aus dem Format (Layout 1): „Linie vor folgenden Stunden“, „Doppelstrich vor Stunden“, „Dicker Strich vor Stunden“,
    /// Trennstrich zwischen Vor- und Nachmittag und zwischen den Tagen.
    /// </summary>
    private void FormatLinien(Graphics g, Rectangle bounds)
    {
        var linie = StundenplanFormat.StundenListe(Format.LinieVor);
        var doppelt = StundenplanFormat.StundenListe(Format.DoppelstrichVor);
        var dick = StundenplanFormat.StundenListe(Format.DickerStrichVor);
        if (Format.TrennstrichVormNachm)
        {
            var zr = D.Zeitraster;
            for (var st = VonStd + 1; st <= BisStd; st++)
                if (zr.Art(0, st) == Tageszeit.Nachmittag || st == zr.LetzteVormittagsstunde + 1) { dick.Add(st); break; }
        }
        var tag = _nurTag ?? VonT;
        foreach (var st in linie.Concat(doppelt).Concat(dick).Distinct().Where(x => x > VonStd && x <= BisStd).OrderBy(x => x))
        {
            var r = Zelle(bounds, tag, st);
            var art = dick.Contains(st) ? "dick" : doppelt.Contains(st) ? "doppelt" : "linie";
            GezeichneteLinien.Add($"{art} vor {st}");
            void Strich(float versatz, float staerke)
            {
                if (Transponiert) g.FillRectangle(Brushes.Black, r.X + versatz - staerke / 2f, bounds.Y, staerke, bounds.Height - 1);
                else g.FillRectangle(Brushes.Black, bounds.X, r.Y + versatz - staerke / 2f, bounds.Width - 1, staerke);
            }
            switch (art)
            {
                case "dick": Strich(0, 3); break;
                case "doppelt": Strich(-2, 1); Strich(1, 1); break;
                default: Strich(0, 1.5f); break;
            }
        }
        if (Format.TrennstrichTage && !_nurTag.HasValue)
            for (var t = VonT + 1; t <= BisT; t++)
            {
                var r = Zelle(bounds, t, VonStd);
                GezeichneteLinien.Add($"tag vor {t}");
                if (Transponiert) g.FillRectangle(Brushes.Black, bounds.X, r.Y - 1, bounds.Width - 1, 2);
                else g.FillRectangle(Brushes.Black, r.X - 1, bounds.Y, 2, bounds.Height - 1);
            }
    }

    /// <summary>Format 10 (Stunden waagrecht, Tage senkrecht): Stunden als Spaltenköpfe, Tage als Zeilenköpfe.</summary>
    private void TransponierteKoepfe(Graphics g, Rectangle bounds, bool druck, Pen linie)
    {
        var zr = D.Zeitraster;
        using var kopfB = new SolidBrush(KopfFarbe);
        using var rosa = new SolidBrush(Rosa);
        var wahl = druck ? null : GewaehlteZelle();
        var ecke = new RectangleF(bounds.X, bounds.Y, KopfBreite, KopfHoehe);
        g.FillRectangle(kopfB, ecke);
        g.DrawRectangle(linie, ecke.X, ecke.Y, ecke.Width, ecke.Height);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        using var schrift = new Font("Segoe UI", Math.Max(8f, Math.Min(Format.Schriftgroesse + 1.5f, KopfHoehe / 2.6f)), FontStyle.Bold);
        using var zeit = new Font("Segoe UI", 7f);
        var tag = _nurTag ?? VonT;
        for (var st = VonStd; st <= BisStd; st++)
        {
            var r = Zelle(bounds, tag, st);
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            g.FillRectangle(wahl?.Stunde == st ? rosa : kopfB, kopf);
            var z = zr.Zeit(st);
            var kr = Rectangle.Round(kopf);
            if (Format.ZeitenAnzeigen && z != null && kr.Width > 60)
            {
                TextRenderer.DrawText(g, zr.StundenLabel(st), schrift, new Rectangle(kr.X, kr.Y, kr.Width, kr.Height * 3 / 5), Color.Black, flags);
                TextRenderer.DrawText(g, $"{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}", zeit, new Rectangle(kr.X, kr.Y + kr.Height / 2, kr.Width, kr.Height / 2), Color.Black, flags);
            }
            else TextRenderer.DrawText(g, zr.StundenLabel(st), schrift, kr, Color.Black, flags);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
            Relief(g, kopf, Format.Ueberschrift.Raeumlich);
        }
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, VonStd);
            if (r.Bottom < bounds.Y) continue;
            var kopf = new RectangleF(bounds.X, r.Y, KopfBreite, r.Height);
            g.FillRectangle(wahl?.Tag == t ? rosa : kopfB, kopf);
            var text = Planungsdienst.Tagname(D, t, KopfBreite > 60) + (_woche?.AddDays(t) is DateOnly dt && KopfBreite > 60 ? " " + dt.ToString("dd.MM.") : "");
            TextRenderer.DrawText(g, text, schrift, Rectangle.Round(kopf), Color.Black, flags);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
            Relief(g, kopf, Format.Ueberschrift.Raeumlich);
        }
    }

    /// <summary>Pausen als eigene schmale Zeilen mit Bezeichnung über die ganze Breite (Vorbild: weiß über den Tagen, Stundenspalte mintgrün).</summary>
    private void PausenZeichnen(Graphics g, Rectangle bounds, Pen linie)
    {
        if (Pausen.Count == 0) return;
        var zr = D.Zeitraster;
        var klein = Format.Klein;
        using var pausenSchrift = new Font("Segoe UI", KlassikStil ? Math.Max(6f, Math.Min(9f, PausenHoehe / 1.75f)) : Math.Max(6f, Math.Min(8f, PausenHoehe / 1.6f)));
        using var pausenHg = new SolidBrush(KlassikStil ? Color.White : klein ? Color.FromArgb(214, 226, 214) : Color.FromArgb(236, 239, 244));
        using var mint = new SolidBrush(Grund);
        foreach (var p in Pausen)
        {
            var r = PausenRechteck(bounds, p);
            if (KlassikStil)
            {
                var links = new RectangleF(r.X, r.Y, KopfBreite, r.Height);
                g.FillRectangle(mint, links);
                g.DrawRectangle(linie, links.X, links.Y, links.Width, links.Height);
                r = new RectangleF(r.X + KopfBreite, r.Y, r.Width - KopfBreite, r.Height);
            }
            g.FillRectangle(pausenHg, r);
            g.DrawRectangle(linie, r.X, r.Y, r.Width, r.Height);
            var text = klein || bounds.Width < 260 ? Pausenbezeichnung.Kurz(zr, p) : p.Bezeichnung.Length > 0 ? p.Bezeichnung : Pausenbezeichnung.Kurz(zr, p);
            TextRenderer.DrawText(g, text, pausenSchrift, Rectangle.Round(r), KlassikStil ? Color.Black : Color.FromArgb(60, 60, 60), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// Vorbild-Kopf: Ecke, Tage (fett im normalen Plan, Tag der gewählten Zelle rosa) und Stundenspalte (große fette Nummer mit
    /// kleiner Uhrzeit, im kleinen Plan nur die Nummer; Stunde der gewählten Zelle rosa), alles mintgrün mit schwarzen Linien.
    /// </summary>
    private void KlassikKoepfe(Graphics g, Rectangle bounds, bool druck, Pen linie)
    {
        var zr = D.Zeitraster;
        var klein = Format.Klein;
        var wahl = druck ? null : GewaehlteZelle();
        int? rosaTag = wahl?.Tag;
        if (rosaTag is null && !druck)
        {
            var heute = DateOnly.FromDateTime(DateTime.Today);
            if (_woche is DateOnly w) { var d = AppZustand.Datum.DayNumber - w.DayNumber; if (d >= 0 && d < Tage) rosaTag = d; }
            else if (Kalender.IstUnterrichtstag(D, heute)) rosaTag = Kalender.TagIndex(D, heute);
        }
        int? rosaStunde = druck ? null : _hover is { } hv && _status != null ? hv.Stunde : wahl?.Stunde;
        using var mint = new SolidBrush(KopfFarbe);
        using var rosa = new SolidBrush(Rosa);
        var ecke = new RectangleF(bounds.X, bounds.Y, KopfBreite, KopfHoehe);
        g.FillRectangle(mint, ecke);
        g.DrawRectangle(linie, ecke.X, ecke.Y, ecke.Width, ecke.Height);
        // Vorbild: „Nvpl Std.“ mit nicht verplanten / allen Stunden oben links (große Pläne)
        if (Ecke.Length > 0 && !klein && !druck)
        {
            using var f = new Font("Segoe UI", Math.Max(6.5f, Math.Min(8.25f, KopfHoehe / 3.4f)));
            TextRenderer.DrawText(g, Ecke, f, Rectangle.Round(ecke), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using var kopfSchrift = new Font("Segoe UI", klein ? Math.Max(7f, Math.Min(Format.Schriftgroesse, KopfHoehe / 2.1f)) : Math.Max(8f, Math.Min(Format.Schriftgroesse + 1.5f, KopfHoehe / 2.6f)), klein ? FontStyle.Regular : FontStyle.Bold);
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, 1);
            if (r.Right < bounds.X) continue;
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            g.FillRectangle(rosaTag == t ? rosa : mint, kopf);
            var text = Planungsdienst.Tagname(D, t, !klein && r.Width > 80);
            if (_nurTag.HasValue && _woche?.AddDays(t) is DateOnly dt) text += " " + dt.ToString("dd.MM.");
            TextRenderer.DrawText(g, text, kopfSchrift, Rectangle.Round(kopf), Color.Black, flags);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
            Relief(g, kopf, Format.Ueberschrift.Raeumlich);
        }
        var zh = Zelle(bounds, 0, 1).Height;
        using var nummer = new Font("Segoe UI", klein ? Math.Max(7f, Math.Min(Format.Schriftgroesse, zh / 1.7f)) : Math.Max(8f, Math.Min(15f, zh / 2.1f)), klein ? FontStyle.Regular : FontStyle.Bold);
        using var zeit = new Font("Segoe UI", 7f);
        for (var s = 1; s <= Stunden; s++)
        {
            var r = Zelle(bounds, 0, s);
            var kopf = new RectangleF(bounds.X, r.Y, KopfBreite, r.Height);
            g.FillRectangle(rosaStunde == s ? rosa : mint, kopf);
            var z = zr.Zeit(s);
            if (klein || !Format.ZeitenAnzeigen || z is null)
                TextRenderer.DrawText(g, zr.StundenLabel(s), nummer, Rectangle.Round(kopf), Color.Black, flags);
            else
            {
                // große Nummer links, kleine Uhrzeit rechts daneben (abgeschnitten wie im Vorbild)
                var nb = TextRenderer.MeasureText(g, zr.StundenLabel(s), nummer, Size.Empty, TextFormatFlags.NoPadding).Width + 4;
                var kr = Rectangle.Round(kopf);
                TextRenderer.DrawText(g, zr.StundenLabel(s), nummer, new Rectangle(kr.X + 2, kr.Y, nb, kr.Height), Color.Black, flags);
                TextRenderer.DrawText(g, $"{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}", zeit, new Rectangle(kr.X + 2 + nb, kr.Y + 1, Math.Max(1, kr.Width - nb - 4), kr.Height), Color.Black,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
    }

    /// <summary>Kopf (Tage) und Stundenspalte der normalen Darstellung (Stundenplanmodus).</summary>
    private void KoepfeZeichnen(Graphics g, Rectangle bounds, bool druck, Pen linie)
    {
        var zr = D.Zeitraster;
        var klein = Format.Klein;
        using var kopfSchrift = new Font("Segoe UI", klein ? Math.Max(7f, Math.Min(Format.Schriftgroesse, KopfHoehe / 1.9f)) : Ui.SchriftFett.Size, klein ? FontStyle.Regular : FontStyle.Bold);
        var heuteDatum = DateOnly.FromDateTime(DateTime.Today);
        var heuteTag = Kalender.IstUnterrichtstag(D, heuteDatum) ? Kalender.TagIndex(D, heuteDatum) : -1;
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, 1);
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            var datum = _woche?.AddDays(t);
            // heutiger Tag rosa (wie im Vorbild); im Regelplan der heutige Wochentag
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
    }

    /// <summary>
    /// Vorbild-Pfeilanzeige beim Ziehen: Liegt die Maus auf einer Stunde, mit der getauscht werden kann, werden Quelle und Ziel rot
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
            return this.Bereich(bounds, tag, start, Math.Min(Stunden, start + laenge - 1));
        }
        var a = Bereich(q.Tag, q.Stunde);
        var b = Bereich(hv.Tag, zielStart);
        var alt = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var rot = new Pen(Color.FromArgb(230, 0, 0), Rahmen))
        {
            g.DrawRectangle(rot, a.X + 0.5f, a.Y + 0.5f, a.Width - 2, a.Height - 2);
            g.DrawRectangle(rot, b.X + 0.5f, b.Y + 0.5f, b.Width - 2, b.Height - 2);
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
        if (e is null) return "?";
        // Art des Namens (Stundenplan-Stunde): Kurz-, Lang- oder Druckname (= Alias, sonst Langname)
        if (Format.NamensArt == NamensArt.Druckname) return Format.AliasVerwenden && e.Alias.Length > 0 ? e.Alias : e.Langname.Length > 0 ? e.Langname : e.Kurzname;
        return (Format.Langname || Format.NamensArt == NamensArt.Langname) && e.Langname.Length > 0 ? e.Langname : e.Kurzname;
    }

    /// <summary>Präfixe wie im Vorbild: „.“ = Kopplung (mehrere Kopplungszeilen), „*“ = fixiert.</summary>
    private string Praefix(Unterricht? u, bool fixiert) => (Format.KopplungenMitPunkt && u?.Zeilen.Count > 1 ? "." : "") + Stern(fixiert);

    /// <summary>„Fixierung mit * kennzeichnen“ (Format, Layout 2).</summary>
    private string Stern(bool fixiert) => fixiert && Format.FixierungMitStern ? "*" : "";

    /// <summary>Kleiner Stundenplan: nur das Fachkürzel (mit Präfixen), zentriert; Entfall durchgestrichen, Änderungen rot.</summary>
    private void KleinZeichnen(Graphics g, RectangleF r, string text, Color farbe, bool durchgestrichen = false)
    {
        var groesse = Math.Max(6f, Math.Min(Format.Schriftgroesse + (KlassikStil ? 0 : 1), Math.Min(r.Height / 1.45f, r.Width / Math.Max(2.2f, text.Length * 0.62f))));
        using var f = new Font("Segoe UI", groesse, durchgestrichen ? FontStyle.Strikeout : FontStyle.Regular);
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), farbe, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    /// <summary>Linienstärke von Markierungen: dünn wie im Vorbild, im kleinen Plan nur ein Pixel.</summary>
    private float Rahmen => Format.Klein ? 1f : 1.5f;

    /// <summary>Rahmen der gewählten Stunde: dünn dunkelblau (gut sichtbar auf jeder Fachfarbe).</summary>
    private void AuswahlRahmen(Graphics g, RectangleF r)
    {
        if (KlassikStil) { Strichrahmen(g, r, AuswahlRot); return; }
        using var blau = new Pen(Color.FromArgb(20, 40, 160), Rahmen);
        g.DrawRectangle(blau, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
        if (!Format.Klein)
        {
            using var hell = new Pen(Color.FromArgb(160, Color.White), 1f);
            g.DrawRectangle(hell, r.X + 2f, r.Y + 2f, r.Width - 5, r.Height - 5);
        }
    }

    /// <summary>Verknüpfte Markierung (derselbe Unterricht, in einem anderen Fenster gewählt): gestrichelter orangefarbener Rahmen.</summary>
    private void VerknuepfungsRahmen(Graphics g, RectangleF r)
    {
        if (KlassikStil) { Strichrahmen(g, r, AuswahlBlau); return; }
        using var pen = new Pen(Color.FromArgb(255, 120, 0), Rahmen) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
    }

    /// <summary>
    /// Vorbild-Rahmen der gewählten Stunde: dünn (2 Pixel), abwechselnd farbig (rot bzw. blau für den verknüpften Unterricht)
    /// und gelb gestrichelt, innerhalb des Feldes.
    /// </summary>
    private static void Strichrahmen(Graphics g, RectangleF rf, Color farbe)
    {
        const int dicke = 2;
        var r = Rectangle.Round(rf);
        if (r.Width < 2 * dicke + 2 || r.Height < 2 * dicke + 2) return;
        using var gelb = new SolidBrush(Color.FromArgb(255, 255, 0));
        using var strich = new SolidBrush(farbe);
        void Kante(int x, int y, int laenge, bool waagrecht)
        {
            g.FillRectangle(gelb, waagrecht ? new Rectangle(x, y, laenge, dicke) : new Rectangle(x, y, dicke, laenge));
            for (var i = 0; i < laenge; i += 10)
            {
                var l = Math.Min(6, laenge - i);
                g.FillRectangle(strich, waagrecht ? new Rectangle(x + i, y, l, dicke) : new Rectangle(x, y + i, dicke, l));
            }
        }
        Kante(r.X, r.Y, r.Width, true);
        Kante(r.X, r.Bottom - dicke, r.Width, true);
        Kante(r.X, r.Y, r.Height, false);
        Kante(r.Right - dicke, r.Y, r.Height, false);
    }

    /// <summary>
    /// Texte einer Stunde wie im Vorbild: alle Werte in einer Zeile, der erste fett (z. B. „*07 Do WL-“ im Raumplan), zentriert;
    /// reicht die Breite nicht und ist genug Höhe da, untereinander.
    /// </summary>
    private void KlassikText(Graphics g, RectangleF r, IReadOnlyList<string> werte, Color farbe, bool durchgestrichen)
    {
        var liste = werte.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (liste.Count == 0) return;
        var groesse = Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 1.75f));
        var stil = durchgestrichen ? FontStyle.Strikeout : FontStyle.Regular;
        using var fett = new Font("Segoe UI", groesse, FontStyle.Bold | stil);
        using var normal = new Font("Segoe UI", groesse, stil);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var rest = string.Join(" ", liste.Skip(1));
        var b0 = TextRenderer.MeasureText(g, liste[0], fett, Size.Empty, flags);
        var abstand = rest.Length > 0 ? (int)Math.Ceiling(groesse * 0.45f) : 0;
        var b1 = rest.Length > 0 ? TextRenderer.MeasureText(g, rest, normal, Size.Empty, flags).Width : 0;
        var zeilenHoehe = b0.Height + 2;
        if (b0.Width + abstand + b1 > r.Width - 4 && liste.Count > 1 && r.Height >= zeilenHoehe * 2)
        {
            TextZeilen(g, r, liste, farbe, durchgestrichen ? liste.Count : 0);
            return;
        }
        var alt = g.Clip;
        g.SetClip(r);
        var x = (int)(r.X + Math.Max(2, (r.Width - b0.Width - abstand - b1) / 2));
        var y = (int)(r.Y + (r.Height - b0.Height) / 2);
        TextRenderer.DrawText(g, liste[0], fett, new Point(x, y), farbe, flags);
        if (rest.Length > 0) TextRenderer.DrawText(g, rest, normal, new Point(x + b0.Width + abstand, y), farbe, flags);
        g.Clip = alt;
    }

    /// <summary>Textteil einer Planzeile: Text und Stil (fett bzw. unterstrichen wie im Vorbild).</summary>
    internal readonly record struct Teil(string Text, bool Fett = false, bool Unterstrichen = false);

    /// <summary>
    /// Zeilen einer Stunde im großen Plan des Stundenplanmodus: Klasse: Fach (fett), Lehrer, Raum (unterstrichen); Lehrer: Klasse (fett),
    /// Raum (unterstrichen), Fach; Raum: Klasse (fett), Lehrer, Fach. Haben die beteiligten Kopplungszeilen verschiedene Fächer,
    /// bekommt jede Zeile eine eigene Textzeile, sonst werden Lehrer und Räume zusammengefasst.
    /// </summary>
    internal List<List<Teil>> GrossZeilen(Unterricht u, Planstunde p, List<int> zs)
    {
        var stern = Stern(p.Fixiert);
        var getrennt = zs.Select(i => u.Zeilen[i].FachId).Distinct().Count() > 1;
        var gruppen = getrennt ? zs.Select(i => new List<int> { i }).ToList() : new List<List<int>> { zs };
        var res = new List<List<Teil>>();
        foreach (var gr in gruppen)
        {
            string Liste(IEnumerable<string> x) => string.Join(",", x.Where(y => y.Length > 0).Distinct());
            var fach = Liste(gr.Select(i => Kurz(ElementTyp.Fach, u.Zeilen[i].FachId)));
            var lehrer = Liste(gr.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId)));
            var raum = Liste(gr.Select(i => Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))));
            var klassen = Ix.KKlassen(gr.SelectMany(i => u.Zeilen[i].KlassenIds).Distinct().ToList());
            var teile = Typ switch
            {
                ElementTyp.Klasse => new List<Teil> { new(fach, true), new(lehrer), new(raum, false, true) },
                ElementTyp.Lehrer => new List<Teil> { new(klassen, true), new(raum, false, true), new(fach) },
                ElementTyp.Raum => new List<Teil> { new(klassen, true), new(lehrer), new(fach) },
                _ => new List<Teil> { new(klassen, true), new(lehrer), new(raum, false, true) },
            };
            teile = teile.Where(x => x.Text.Length > 0).ToList();
            if (teile.Count == 0) teile.Add(new Teil("?", true));
            if (res.Count == 0 && stern.Length > 0) teile[0] = teile[0] with { Text = stern + teile[0].Text };
            res.Add(teile);
        }
        return res;
    }

    /// <summary>Zuletzt gezeichnete Textzeilen der großen Pläne (für den Selbsttest).</summary>
    internal List<string> GezeichneteZeilen { get; } = new();

    /// <summary>Zeilen untereinander, jede zentriert; die Schrift wird verkleinert, bis alle Zeilen in Breite und Höhe passen.</summary>
    private void KlassikZeilen(Graphics g, RectangleF r, List<List<Teil>> zeilen, Color farbe)
    {
        if (zeilen.Count == 0 || r.Width < 6 || r.Height < 6) return;
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var groesse = Math.Max(6.5f, Math.Min(Format.Schriftgroesse, (r.Height - 2) / zeilen.Count / 1.55f));
        Font[] Schriften(float gr) => new[] { new Font("Segoe UI", gr), new Font("Segoe UI", gr, FontStyle.Bold), new Font("Segoe UI", gr, FontStyle.Underline), new Font("Segoe UI", gr, FontStyle.Bold | FontStyle.Underline) };
        Font S(Font[] f, Teil t) => f[(t.Fett ? 1 : 0) + (t.Unterstrichen ? 2 : 0)];
        int Breite(Font[] f, List<Teil> z, int abstand) => z.Sum(t => TextRenderer.MeasureText(g, t.Text, S(f, t), Size.Empty, flags).Width) + abstand * (z.Count - 1);
        var schriften = Schriften(groesse);
        try
        {
            while (groesse > 6.5f && zeilen.Max(z => Breite(schriften, z, (int)Math.Ceiling(groesse * 0.45f))) > r.Width - 4)
            {
                foreach (var f in schriften) f.Dispose();
                groesse = Math.Max(6.5f, groesse - 0.5f);
                schriften = Schriften(groesse);
            }
            var abstand = (int)Math.Ceiling(groesse * 0.45f);
            var zh = TextRenderer.MeasureText(g, "Ag", schriften[1], Size.Empty, flags).Height + 1;
            var y = r.Y + Math.Max(1, (r.Height - zh * zeilen.Count) / 2);
            var alt = g.Clip;
            g.SetClip(r);
            foreach (var z in zeilen)
            {
                var b = Breite(schriften, z, abstand);
                var x = (int)(r.X + Math.Max(2, (r.Width - b) / 2));
                foreach (var t in z)
                {
                    TextRenderer.DrawText(g, t.Text, S(schriften, t), new Point(x, (int)y), farbe, flags);
                    x += TextRenderer.MeasureText(g, t.Text, S(schriften, t), Size.Empty, flags).Width + abstand;
                }
                if (!_druck) GezeichneteZeilen.Add(string.Join(" ", z.Select(t => t.Text)));
                y += zh;
            }
            g.Clip = alt;
        }
        finally { foreach (var f in schriften) f.Dispose(); }
    }

    /// <summary>Werte einer Stunde in der Reihenfolge der Planart: Klasse: Fach, Lehrer, Raum; Lehrer: Klasse, Fach, Raum; Raum: Klasse, Lehrer, Fach.</summary>
    private List<string> KlassikWerte(string praefix, string klassen, string fach, string lehrer, string raum)
    {
        var werte = Typ switch
        {
            ElementTyp.Klasse => new List<string> { fach, lehrer, raum },
            ElementTyp.Lehrer => new List<string> { klassen, fach, raum },
            ElementTyp.Raum => new List<string> { klassen, lehrer, fach },
            _ => new List<string> { klassen, lehrer, raum },
        };
        if (Format.Klein)
            // kleiner Plan: nur ein Wert (Klasse: Fach, Lehrer: Klasse, Raum: Lehrer)
            werte = new List<string> { new[] { Typ switch { ElementTyp.Klasse => fach, ElementTyp.Raum => lehrer, _ => klassen }, fach, lehrer, klassen }.FirstOrDefault(x => x.Length > 0) ?? "" };
        var i = werte.FindIndex(x => !string.IsNullOrWhiteSpace(x));
        if (i >= 0) werte[i] = praefix + werte[i].Trim();
        else if (praefix.Length > 0) werte.Insert(0, praefix);
        return werte;
    }

    private void StundeZeichnen(Graphics g, RectangleF r, Planstunde p, bool druck, bool kollision, List<Planstunde>? feld = null)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
        feld ??= new List<Planstunde> { p };
        if (KlassikStil && (Format.Klein || Format.Standardformat || Format.Felder.Count == 0))
        {
            // Vorbild (Vertretungsmodus, Zeitbereich „Periode“): mintgrün, Werte in einer Zeile
            var zs = Enumerable.Range(0, u.Zeilen.Count).Where(i => Typ switch
            {
                ElementTyp.Klasse => u.Zeilen[i].KlassenIds.Contains(ElementId),
                ElementTyp.Lehrer => u.Zeilen[i].LehrerId == ElementId,
                ElementTyp.Fach => u.Zeilen[i].FachId == ElementId,
                ElementTyp.Raum => p.RaumFuerZeile(i) == ElementId,
                _ => true,
            }).DefaultIfEmpty(0).ToList();
            using (var b = new SolidBrush(Grund)) g.FillRectangle(b, r);
            if (!Format.Klein && !AppZustand.Vertretungsmodus)
            {
                // Vorbild-Stundenplanmodus, großer Plan: „Nw Les C12“ (erster Wert fett, Raum unterstrichen); Kopplungszeilen mit
                // unterschiedlichen Fächern untereinander als eigene Zeilen („Rel Beu A11 / Eth Sei A11“)
                KlassikZeilen(g, r, GrossZeilen(u, p, zs), Color.Black);
                if (kollision) using (var pen = new Pen(Color.Red, Rahmen)) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
                AuswahlZeichnen(g, r, p, feld, druck);
                return;
            }
            var werte = KlassikWerte(Praefix(u, p.Fixiert),
                Typ == ElementTyp.Lehrer ? Ix.KKlassen(zs.SelectMany(i => u.Zeilen[i].KlassenIds).Distinct().ToList()) : Ix.KKlassen(u.KlassenIds),
                Kurz(ElementTyp.Fach, u.Zeilen[zs[0]].FachId),
                string.Join(",", zs.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId)).Where(x => x.Length > 0).Distinct()),
                Typ == ElementTyp.Raum ? "" : string.Join(",", zs.Select(i => Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))).Where(x => x.Length > 0).Distinct()));
            if (Format.Klein) KleinZeichnen(g, r, werte[0], Color.Black);
            else KlassikText(g, r, werte, Color.Black, false);
            if (kollision) using (var pen = new Pen(Color.Red, Rahmen)) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
            AuswahlZeichnen(g, r, p, feld, druck);
            return;
        }
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
            if (kollision) using (var pen = new Pen(Color.Red, Rahmen)) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
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
        var farbe = KlassikStil ? Grund : Farbe(u, zeilen[0]);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
        var text = Ui.Textfarbe(farbe);
        using var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        using var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        var ug = u.UGruppeId is int ugid ? D.UGruppen.FirstOrDefault(x => x.Id == ugid)?.Kurzname : u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => null };
        if (Format.Standardformat || Format.Felder.Count == 0)
        {
            var z = u.Zeilen[zeilen[0]];
            var kopf = Stern(p.Fixiert) + Kurz(ElementTyp.Fach, z.FachId) + (u.Kursname.Length > 0 && r.Width > 60 ? " " + u.Kursname : "") + (ug != null ? $" ({ug})" : "");
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
                    var zeileText = $"{Stern(p.Fixiert && i == 0)}{Kurz(ElementTyp.Fach, zz.FachId)} {Kurz(ElementTyp.Lehrer, zz.LehrerId)} {Ix.KKlassen(zz.KlassenIds)} {Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))}{ZeilenGruppe(zz)}";
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
            // eigene Stundenplan-Stunde: Felder an ihrer Position (relativ zur Zelle); „Felder in Stunde zentrieren“ verschiebt die
            // ganze Feldgruppe in die Mitte der Zelle
            var min = new PointF(Format.Felder.Min(x => x.X), Format.Felder.Min(x => x.Y));
            var max = new PointF(Format.Felder.Max(x => x.X + x.B), Format.Felder.Max(x => x.Y + x.H));
            var dx = Format.FelderHorizontalZentrieren ? (1 - (max.X - min.X)) / 2 - min.X : 0;
            var dy = Format.FelderVertikalZentrieren ? (1 - (max.Y - min.Y)) / 2 - min.Y : 0;
            foreach (var f in Format.Felder)
            {
                var zi = Math.Clamp(f.Zeile - 1, 0, u.Zeilen.Count - 1);
                var z = u.Zeilen[zi];
                var zeit = D.Zeitraster.Zeit(p.Stunde);
                var wert = f.Art switch
                {
                    StundenFeldArt.Fach => Stern(p.Fixiert && zi == 0) + Kurz(ElementTyp.Fach, z.FachId),
                    StundenFeldArt.Lehrer => Kurz(ElementTyp.Lehrer, z.LehrerId),
                    StundenFeldArt.Klasse => Ix.KKlassen(z.KlassenIds),
                    StundenFeldArt.Raum => Kurz(ElementTyp.Raum, p.RaumFuerZeile(zi)),
                    StundenFeldArt.UNr => u.Id.ToString(),
                    StundenFeldArt.Text or StundenFeldArt.Stundentext or StundenFeldArt.Sondertext => u.Text,
                    StundenFeldArt.Gruppe or StundenFeldArt.Schuelergruppe => z.Schuelergruppe.Length > 0 ? z.Schuelergruppe : u.Schuelergruppe,
                    StundenFeldArt.Kurs => u.Kursname,
                    StundenFeldArt.StudZl => u.MaxSchueler > 0 ? u.MaxSchueler.ToString() : "",
                    StundenFeldArt.UnterrichtsGruppe => (z.UGruppeId ?? u.UGruppeId) is int ugId ? D.UGruppen.FirstOrDefault(x => x.Id == ugId)?.Kurzname ?? "" : "",
                    StundenFeldArt.ZeilenText => z.Zeilentext,
                    StundenFeldArt.ZeilenText2 => u.Statistik,
                    StundenFeldArt.Beschreibung => u.Kommentar,
                    StundenFeldArt.Band => u.Band,
                    StundenFeldArt.Zeitwunsch => u.Zeitwuensche.Get(p.Tag, p.Stunde) is var w && w != 0 ? w.ToString("+0;-0") : "",
                    StundenFeldArt.Fussnote => "",
                    _ => zeit is null ? "" : Format.BeginnEndzeit ? $"{zeit.Beginn:H:mm}-{zeit.Ende:H:mm}" : zeit.Beginn.ToString("H:mm"),
                };
                var fr = new RectangleF(r.X + (f.X + dx) * r.Width, r.Y + (f.Y + dy) * r.Height, f.B * r.Width, f.H * r.Height);
                var stil = (f.Fett ? FontStyle.Bold : 0) | (f.Kursiv ? FontStyle.Italic : 0) | (f.Unterstrichen ? FontStyle.Underline : 0);
                using var ff = new Font("Segoe UI", Math.Max(5.5f, Math.Min(Format.Schriftgroesse * f.SchriftProzent / 100f, fr.Height / 1.6f)), stil);
                var ausrichtung = f.Ausrichtung == 0 ? TextFormatFlags.Left : f.Ausrichtung == 2 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
                TextRenderer.DrawText(g, wert, ff, Rectangle.Round(fr), text, ausrichtung | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                if (!druck && wert.Length > 0) GezeichneteFelder.Add((f.Art, wert, Rectangle.Round(fr)));
            }
        }
        if (kollision)
            using (var pen = new Pen(Color.Red, Rahmen)) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
        AuswahlZeichnen(g, r, p, feld, druck);
    }

    /// <summary>Markierung eines Feldes: gewählte Stunde (auch innerhalb eines Blocks), Mehrfachauswahl, verknüpfter Unterricht.</summary>
    private void AuswahlZeichnen(Graphics g, RectangleF r, Planstunde p, List<Planstunde> feld, bool druck)
    {
        if (druck) return;
        if (Ausgewaehlt != null && feld.Contains(Ausgewaehlt)) AuswahlRahmen(g, r);
        else if (feld.Any(Mehrfach.Contains))
            using (var pen = new Pen(Color.FromArgb(255, 140, 0), Rahmen) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
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
        Format.Klein ? (int)Math.Ceiling(Format.Schriftgroesse * (KlassikStil ? 1.85f : 1.6f)) :
        (int)Math.Ceiling(Format.Schriftgroesse * (AppZustand.Vertretungsmodus ? (Format.Gross ? 3.6f : 2.8f) : Format.Gross ? 6.4f : 5.0f)));

    /// <summary>Mindestbreite laut Format (Zellenbreite je Tag), 0 = an die Fensterbreite anpassen.</summary>
    public int BenoetigteBreite => Format.ZellenBreite > 0 ? KopfBreite + SichtbareTage * Format.ZellenBreite + 2 : 0;

    private static string ZeilenGruppe(Kopplungszeile z) =>
        z.UGruppeId is int g && D.UGruppen.FirstOrDefault(x => x.Id == g) is { } ug ? $"({ug.Kurzname})" : "";

    /// <summary>
    /// Stunde im tagesaktuellen Plan wie im Vorbild: mintgrün; Änderungen mit „*“ vor dem ersten Wert, Kopplungen mit „.“;
    /// Entfall rot durchgestrichen auf weißem Feld, offene Vertretung „???“ rot.
    /// </summary>
    private void EintragKlassik(Graphics g, RectangleF r, TagesEintrag e, bool druck, bool gewaehlt)
    {
        var v = e.Vertretung;
        var entfall = e.Status == TagesStatus.Entfall;
        var offen = e.Status == TagesStatus.Offen;
        using (var b = new SolidBrush(Grund)) g.FillRectangle(b, r);
        if (entfall && r.Width > 6 && r.Height > 6)
        {
            var innen = Format.Klein ? 2 : 3;
            g.FillRectangle(Brushes.White, r.X + innen, r.Y + innen, r.Width - 2 * innen, r.Height - 2 * innen);
        }
        var geaendert = v != null || e.Status == TagesStatus.Zusaetzlich;
        var fixiert = e.Regel?.Planstunde.Fixiert == true || v?.Fixiert == true;
        var praefix = (Format.KopplungenMitPunkt && e.Unterricht?.Zeilen.Count > 1 ? "." : "") + Stern(geaendert || fixiert);
        var lehrerIds = entfall && e.LehrerIds.Count == 0 && v?.LehrerId is int ul ? new List<int> { ul } : e.LehrerIds;
        var lehrer = offen ? "???" : v?.Art == VertretungsArt.VertretungOhneLehrer ? "+++" : VeranstaltungsLehrer(e) ?? string.Join(",", lehrerIds.Select(l => Kurz(ElementTyp.Lehrer, l)));
        // ohne Fach (Veranstaltung, Sondereinsatz): erstes Wort des Textes, z. B. „Sportwoche“
        var fach = Kurz(ElementTyp.Fach, e.FachId ?? e.Regel?.FachId);
        if (fach.Length == 0 && !string.IsNullOrWhiteSpace(v?.Text)) fach = v.Text.Trim().Split(' ')[0];
        var werte = KlassikWerte(praefix, KlassenText(e), fach, lehrer,
            Typ == ElementTyp.Raum ? "" : string.Join(",", e.RaumIds.Select(x => Kurz(ElementTyp.Raum, x))));
        // offene Vertretung im Plan der abwesenden Lehrkraft: „???“
        if (Format.Klein && offen && Typ == ElementTyp.Lehrer) werte = new List<string> { praefix + "???" };
        var farbe = entfall || offen ? EntfallRot : Color.Black;
        if (Format.Klein) KleinZeichnen(g, r, werte[0], farbe, entfall);
        else KlassikText(g, r, werte, farbe, entfall);
        if (druck) return;
        if (gewaehlt) AuswahlRahmen(g, r);
        else if (MarkierterUnterricht is int mu && (e.Unterricht?.Id ?? v?.UnterrichtId) == mu) VerknuepfungsRahmen(g, r);
    }

    private void EintragZeichnen(Graphics g, RectangleF r, TagesEintrag e, DateOnly datum, bool druck, bool gewaehlt)
    {
        if (KlassikStil) { EintragKlassik(g, r, e, druck, gewaehlt); return; }
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
                using (var pen = new Pen(Color.Red, Rahmen) { DashStyle = e.Status == TagesStatus.Offen ? DashStyle.Dash : DashStyle.Solid }) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
            if (!druck && gewaehlt) AuswahlRahmen(g, r);
            else if (!druck && MarkierterUnterricht is int mu && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == mu) VerknuepfungsRahmen(g, r);
            return;
        }
        using var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        using var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        var v = e.Vertretung;
        var fach = Kurz(ElementTyp.Fach, e.FachId ?? e.Regel?.FachId);
        var stern = Stern(e.Regel?.Planstunde.Fixiert == true || v?.Fixiert == true);
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
            // Entfall wie im Vorbild: alle Werte einzeln durchgestrichen (kein Schrägbalken), ggf. Ziel der Verlegung darunter
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
            using (var pen = new Pen(Color.Red, Rahmen) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
        if (e.Status == TagesStatus.Zusaetzlich)
            using (var pen = new Pen(Color.Red, Rahmen)) g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
        if (!druck && gewaehlt) AuswahlRahmen(g, r);
        else if (!druck && MarkierterUnterricht is int mu && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == mu) VerknuepfungsRahmen(g, r);
    }

    // ------------------------------------------------------------------------------------------
    // Maus / Drag & Drop
    // ------------------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        _zelle = Treffer(e.Location);
        if (Tagesmodus)
        {
            var te = EintragBei(e.Location);
            var h = Treffer(e.Location);
            // ein Klick wählt nur die Stunde – das Planungsdatum (Absenzen, Vertretungsliste) bleibt unverändert
            TagesAuswahl = te;
            Invalidate();
            if (te is { } x) TagesAuswahlGeaendert?.Invoke(x.Datum, x.Eintrag);
            else if (h is { } h2 && _woche is DateOnly w2)
            {
                TagesAuswahlGeaendert?.Invoke(w2.AddDays(h2.Tag), null);
                // nicht verplante Stunde: verknüpfte Pläne trotzdem auf Tag und Stunde setzen
                LeereZelleGewaehlt?.Invoke(h2.Tag, h2.Stunde);
            }
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
            else if (p is null) Invalidate();
            // nicht verplante Stunde: verknüpfte Pläne trotzdem auf Tag und Stunde setzen
            if (p is null && _zelle is { } lz) LeereZelleGewaehlt?.Invoke(lz.Tag, lz.Stunde);
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
        else if (Tagesmodus && e.Button == MouseButtons.Left && EintragBei(e.Location) is { } te) TagesDoppelklick?.Invoke(te.Datum, te.Eintrag);
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
            var (ids, gewuenscht) = Fenster.StundenplanFenster.UnterrichtDaten(s);
            var uids = ids.Select(i => Ix.Unterrichte.GetValueOrDefault(i)).OfType<Unterricht>().ToList();
            if (uids.Count == 0) { _status = null; e.Effect = DragDropEffects.None; return; }
            // gezogener Block aus „nicht verplant“ in seiner Blockgröße, sonst der nächste offene Block
            var laenge = gewuenscht ?? Planungsdienst.OffeneBloecke(D, Ix, uids[0]).FirstOrDefault(1);
            _ziehLaenge = uids.Count == 1 ? Math.Max(1, laenge) : 1;
            _ziehVersatz = 0;
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
        _zelle = null;
        Invalidate();
    }
}
