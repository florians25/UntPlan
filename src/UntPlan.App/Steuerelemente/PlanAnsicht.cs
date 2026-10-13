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
    public Planstunde? Ausgewaehlt { get; set; }
    /// <summary>Zusätzlich markierte Stunden (Strg+Klick) – werden gemeinsam verschoben.</summary>
    public List<Planstunde> Mehrfach { get; } = new();
    /// <summary>Gewählte Stunde im tagesaktuellen Plan.</summary>
    public (DateOnly Datum, TagesEintrag Eintrag)? TagesAuswahl { get; private set; }
    public bool Bearbeitbar { get; set; } = true;

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

    private const int KopfHoehe = 22;
    private int KopfBreite => Format.ZeitenAnzeigen ? 74 : 32;

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
            for (var t = 0; t < Tage; t++)
                _tage[t] = Tagesplan.Berechnen(D, w.AddDays(t));
        Invalidate();
    }

    public DateOnly? DatumVon(int tag) => _woche?.AddDays(tag);

    private RectangleF Zelle(Rectangle bounds, int tag, int stunde)
    {
        var w = (bounds.Width - KopfBreite - 1) / (float)Tage;
        var h = (bounds.Height - KopfHoehe - 1) / (float)Stunden;
        return new RectangleF(bounds.X + KopfBreite + tag * w, bounds.Y + KopfHoehe + (stunde - 1) * h, w, h);
    }

    private (int Tag, int Stunde)? Treffer(Point p)
    {
        var w = (Width - KopfBreite - 1) / (float)Tage;
        var h = (Height - KopfHoehe - 1) / (float)Stunden;
        if (p.X < KopfBreite || p.Y < KopfHoehe) return null;
        var t = (int)((p.X - KopfBreite) / w);
        var s = (int)((p.Y - KopfHoehe) / h) + 1;
        if (t < 0 || t >= Tage || s < 1 || s > Stunden) return null;
        return (t, s);
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
        var liste = StundenAm(t.Value.Tag, t.Value.Stunde);
        if (liste.Count <= 1) return liste.FirstOrDefault();
        var r = Zelle(ClientRectangle, t.Value.Tag, t.Value.Stunde);
        var i = (int)((p.Y - r.Y) / (r.Height / liste.Count));
        return liste[Math.Clamp(i, 0, liste.Count - 1)];
    }

    private (DateOnly Datum, TagesEintrag Eintrag)? EintragBei(Point p)
    {
        var t = Treffer(p);
        if (t is null || _woche is null) return null;
        var liste = EintraegeAm(t.Value.Tag, t.Value.Stunde);
        if (liste.Count == 0) return null;
        var r = Zelle(ClientRectangle, t.Value.Tag, t.Value.Stunde);
        var i = liste.Count == 1 ? 0 : (int)((p.Y - r.Y) / (r.Height / liste.Count));
        return (_woche.Value.AddDays(t.Value.Tag), liste[Math.Clamp(i, 0, liste.Count - 1)]);
    }

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
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.FillRectangle(Brushes.White, bounds);
        var zr = D.Zeitraster;
        using var linie = new Pen(Color.FromArgb(160, 170, 185));
        using var duenn = new Pen(Color.FromArgb(210, 216, 226));
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, 1);
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            var datum = _woche?.AddDays(t);
            var heute = datum == DateOnly.FromDateTime(DateTime.Today);
            var gewaehlt = datum.HasValue && datum == AppZustand.Datum && AppZustand.Vertretungsmodus;
            using (var b = new SolidBrush(gewaehlt ? Ui.BlauHell : heute ? Color.FromArgb(255, 210, 200) : Ui.Kopf)) g.FillRectangle(b, kopf);
            var text = Planungsdienst.Tagname(D, t, r.Width > 70) + (datum.HasValue && r.Width > 60 ? " " + datum.Value.ToString("dd.MM.") : "");
            TextRenderer.DrawText(g, text, Ui.SchriftFett, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
        for (var s = 1; s <= Stunden; s++)
        {
            var r = Zelle(bounds, 0, s);
            var kopf = new RectangleF(bounds.X, r.Y, KopfBreite, r.Height);
            var nachm = zr.Art(0, s) == Tageszeit.Nachmittag || s > zr.LetzteVormittagsstunde;
            using (var b = new SolidBrush(nachm ? Color.FromArgb(230, 245, 248) : Color.FromArgb(242, 244, 248))) g.FillRectangle(b, kopf);
            var z = zr.Zeit(s);
            var text = Format.ZeitenAnzeigen && z != null && r.Height > 26 ? $"{zr.StundenLabel(s)}\n{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}" : zr.StundenLabel(s);
            TextRenderer.DrawText(g, text, Ui.SchriftKlein, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
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
                var hint = Color.White;
                var frei = kt?.StundeFrei(s) == true;
                if (!zr.Verfuegbar(t, s)) hint = Color.FromArgb(225, 225, 225);
                else if (ferien != null || frei) hint = Color.FromArgb(255, 248, 220);
                else if (!druck && _status is null && el != null && Format.DragDropZeitwunschFarben && !Tagesmodus)
                {
                    var w = el.Zeitwuensche.Get(t, s);
                    if (w != 0) hint = Color.FromArgb(70, Ui.ZeitwunschFarbe(w));
                }
                if (!druck && _status != null)
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
                if (Tagesmodus)
                {
                    var eintraege = EintraegeAm(t, s);
                    for (var i = 0; i < eintraege.Count; i++)
                    {
                        var teil = new RectangleF(r.X + 1, r.Y + 1 + i * (r.Height - 1) / eintraege.Count, r.Width - 2, (r.Height - 1) / eintraege.Count - 1);
                        EintragZeichnen(g, teil, eintraege[i], datum!.Value, druck);
                    }
                    if (frei && s == 1) TextRenderer.DrawText(g, "frei", Ui.SchriftKlein, Rectangle.Round(r), Color.Peru, TextFormatFlags.Top | TextFormatFlags.Right);
                }
                else
                {
                    var stunden = plan.Where(p => p.Tag == t && p.Stunde == s).ToList();
                    var kollision = stunden.Select(p => Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId)).Where(u => u != null)
                        .Select(u => string.IsNullOrWhiteSpace(u!.Band) ? "#" + u.Id : u.Band).Distinct().Count() > 1 && Typ != ElementTyp.Fach && !AbWochenGetrennt(stunden);
                    for (var i = 0; i < stunden.Count; i++)
                    {
                        var teil = new RectangleF(r.X + 1, r.Y + 1 + i * (r.Height - 1) / stunden.Count, r.Width - 2, (r.Height - 1) / stunden.Count - 1);
                        StundeZeichnen(g, teil, stunden[i], druck, kollision);
                    }
                }
                if (ferien != null && s == 1) TextRenderer.DrawText(g, ferien.Kurzname, Ui.SchriftKlein, Rectangle.Round(r), Color.Peru, TextFormatFlags.Top | TextFormatFlags.Right);
                g.DrawRectangle(duenn, r.X, r.Y, r.Width, r.Height);
                if (!druck && _hover.HasValue && _hover.Value.Tag == t && _hover.Value.Stunde == s)
                    using (var p = new Pen(Color.Black, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            }
        }
        g.DrawRectangle(linie, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
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

    private void StundeZeichnen(Graphics g, RectangleF r, Planstunde p, bool druck, bool kollision)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
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
        var gewaehlt = !druck && (p == Ausgewaehlt || Mehrfach.Contains(p));
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
            else if (r.Height >= 26)
            {
                TextRenderer.DrawText(g, kopf, schrift, new Rectangle((int)r.X + 2, (int)r.Y + 1, (int)r.Width - 3, (int)(r.Height / 2)), text, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, (info + " " + raum).Trim(), klein, new Rectangle((int)r.X + 2, (int)(r.Y + r.Height / 2), (int)r.Width - 3, (int)(r.Height / 2)), text, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }
            else
                TextRenderer.DrawText(g, kopf + " " + info, klein, Rectangle.Round(r), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
        if (gewaehlt)
            using (var pen = new Pen(Color.FromArgb(255, 140, 0), 2.5f) { DashStyle = Mehrfach.Contains(p) && p != Ausgewaehlt ? DashStyle.Dash : DashStyle.Solid }) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
    }

    private static string ZeilenGruppe(Kopplungszeile z) =>
        z.UGruppeId is int g && D.UGruppen.FirstOrDefault(x => x.Id == g) is { } ug ? $"({ug.Kurzname})" : "";

    private void EintragZeichnen(Graphics g, RectangleF r, TagesEintrag e, DateOnly datum, bool druck)
    {
        var geaendert = e.Vertretung != null && e.Vertretung.Art != VertretungsArt.Raumvertretung || e.Status == TagesStatus.Zusaetzlich;
        var entfall = e.Status == TagesStatus.Entfall;
        var farbe = entfall ? Color.FromArgb(238, 238, 238) : Farbe(e.Unterricht, e.Regel?.ZeilenIndex ?? 0, e.FachId);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
        var textfarbe = geaendert || e.Vertretung?.Art == VertretungsArt.Raumvertretung ? Color.FromArgb(200, 0, 0) : Ui.Textfarbe(farbe);
        if (entfall) textfarbe = Color.Gray;
        using var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        using var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        var v = e.Vertretung;
        var fach = Kurz(ElementTyp.Fach, e.FachId ?? e.Regel?.FachId);
        var stern = e.Regel?.Planstunde.Fixiert == true || v?.Fixiert == true ? "*" : "";
        var lehrer = e.Status == TagesStatus.Offen ? "???" : string.Join(",", e.LehrerIds.Select(l => Kurz(ElementTyp.Lehrer, l)));
        if (v?.Art == VertretungsArt.VertretungOhneLehrer) lehrer = "+";
        var info = Typ switch
        {
            ElementTyp.Lehrer => Ix.KKlassen(e.KlassenIds),
            ElementTyp.Raum => (Ix.KKlassen(e.KlassenIds) + " " + lehrer).Trim(),
            _ => lehrer,
        };
        if (entfall) info = v?.NachDatum.HasValue == true ? $"→ {Planungsdienst.TageKurz[((int)v.NachDatum.Value.DayOfWeek + 6) % 7]} {v.NachStunde}." : Core.Vertretungsplanung.Vertretungsplan.ArtText(v!.Art);
        var raum = Typ == ElementTyp.Raum ? "" : string.Join(",", e.RaumIds.Select(x => Kurz(ElementTyp.Raum, x)));
        var kopf = stern + fach;
        if (v != null && !entfall && v.Art is not (VertretungsArt.Vertretung or VertretungsArt.Raumvertretung)) kopf += r.Width > 70 ? " " + Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art) : "";
        if (r.Height >= 26)
        {
            TextRenderer.DrawText(g, kopf, schrift, new Rectangle((int)r.X + 2, (int)r.Y + 1, (int)r.Width - 3, (int)(r.Height / 2)), textfarbe, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, (info + " " + raum).Trim(), klein, new Rectangle((int)r.X + 2, (int)(r.Y + r.Height / 2), (int)r.Width - 3, (int)(r.Height / 2)), textfarbe, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
        else TextRenderer.DrawText(g, kopf + " " + info + " " + raum, klein, Rectangle.Round(r), textfarbe, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (entfall)
            using (var pen = new Pen(Color.FromArgb(200, 60, 60), 1.5f)) g.DrawLine(pen, r.Left + 2, r.Bottom - 3, r.Right - 2, r.Top + 3);
        if (e.Status == TagesStatus.Offen)
            using (var pen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        if (e.Status == TagesStatus.Zusaetzlich)
            using (var pen = new Pen(Color.Red, 1.5f)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        if (!druck && TagesAuswahl is { } ta && ta.Datum == datum && ta.Eintrag == e)
            using (var pen = new Pen(Color.FromArgb(255, 140, 0), 2.5f)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
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
                DoDragDrop($"tag:{de.Item1:yyyyMMdd}:{de.Item2.Stunde}:{uid}:{zeile}", DragDropEffects.Move);
                return;
            }
            if (!Tagesmodus && _downStunde != null)
            {
                var p = _downStunde;
                _downStunde = null;
                var mitgezogen = Mehrfach.Count > 1 && Mehrfach.Contains(p) ? Mehrfach.ToList() : Block(p);
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
        if (!Bearbeitbar || e.Data?.GetData(DataFormats.Text) is not string s) { e.Effect = DragDropEffects.None; return; }
        _status = new Feldstatus[Tage, Stunden];
        if (Tagesmodus)
        {
            if (!s.StartsWith("tag:") && !s.StartsWith("vormerkung:")) { _status = null; e.Effect = DragDropEffects.None; return; }
            var (lehrer, klassen, uid, raum) = TagesDragDaten(s);
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    _status[t, st - 1] = _tage.TryGetValue(t, out var tp) ? Vertretungen.Feldstatus(D, tp, lehrer, klassen, st, uid, raum) : Feldstatus.Kollision;
        }
        else if (s.StartsWith("plan:"))
        {
            var (prim, alle) = PlanDaten(s);
            if (prim is null) { _status = null; e.Effect = DragDropEffects.None; return; }
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    _status[t, st - 1] = Kombiniert(alle.Select(x => (x, x.Tag - prim.Tag + t, x.Stunde - prim.Stunde + st)).ToList(), alle);
        }
        else if (s.StartsWith("unterricht:"))
        {
            var uids = s[11..].Split(',').Select(x => int.TryParse(x, out var i) ? Ix.Unterrichte.GetValueOrDefault(i) : null).OfType<Unterricht>().ToList();
            if (uids.Count == 0) { _status = null; e.Effect = DragDropEffects.None; return; }
            var laenge = uids[0].Bloecke().FirstOrDefault(b => b > 0);
            for (var t = 0; t < Tage; t++)
                for (var st = 1; st <= Stunden; st++)
                    _status[t, st - 1] = uids.Select(u => Planungsdienst.Status(D, Ix, u, t, st, Math.Max(1, uids.Count == 1 ? laenge : 1))).Max(Schwere) is var m ? AusSchwere(m) : Feldstatus.Frei;
        }
        else { _status = null; e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
        Invalidate();
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

    protected override void OnDragLeave(EventArgs e)
    {
        _status = null;
        _hover = null;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        var h = Treffer(PointToClient(new Point(e.X, e.Y)));
        _status = null;
        _hover = null;
        Invalidate();
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
