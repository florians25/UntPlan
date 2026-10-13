using System.Drawing.Drawing2D;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Gezeichneter Einzel-Stundenplan eines Elements. Unterstützt manuelles Planen per Drag &amp; Drop:
/// grün = kollisionsfrei, rot = kollisionsfrei aber Vorgabe verletzt, violett = kein Raum frei,
/// grün hinterlegte Stunde = Tausch möglich, nicht gefärbt = Kollision.
/// </summary>
public sealed class PlanAnsicht : Control
{
    public ElementTyp Typ { get; set; } = ElementTyp.Klasse;
    public int ElementId { get; set; }
    public StundenplanFormat Format { get; set; } = new();
    /// <summary>Montag der angezeigten Woche (Vertretungen werden eingeblendet) oder null für den Regelplan.</summary>
    public DateOnly? Woche { get; set; }
    public Planstunde? Ausgewaehlt { get; set; }
    public bool Bearbeitbar { get; set; } = true;

    public event Action<Planstunde?>? AuswahlGeaendert;
    public event Action<string, int, int>? Abgelegt;
    public event Action<Planstunde>? Doppelklick;
    public event Action<Point, Planstunde?>? Kontext;

    private Feldstatus[,]? _status;
    private (int Tag, int Stunde)? _hover;
    private Point _down;
    private Planstunde? _downStunde;
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

    protected override void OnPaint(PaintEventArgs e) => Zeichnen(e.Graphics, ClientRectangle, false);

    public void Zeichnen(Graphics g, Rectangle bounds, bool druck)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.FillRectangle(Brushes.White, bounds);
        var zr = D.Zeitraster;
        using var kopfBrush = new SolidBrush(Color.FromArgb(232, 237, 245));
        using var linie = new Pen(Color.FromArgb(160, 170, 185));
        using var duenn = new Pen(Color.FromArgb(210, 216, 226));
        // Tageskopf
        for (var t = 0; t < Tage; t++)
        {
            var r = Zelle(bounds, t, 1);
            var kopf = new RectangleF(r.X, bounds.Y, r.Width, KopfHoehe);
            var heute = Woche.HasValue && Woche.Value.AddDays(t) == DateOnly.FromDateTime(DateTime.Today);
            using (var b = new SolidBrush(heute ? Color.FromArgb(255, 210, 200) : Color.FromArgb(232, 237, 245))) g.FillRectangle(b, kopf);
            var text = Planungsdienst.Tagname(D, t, r.Width > 70) + (Woche.HasValue && r.Width > 90 ? " " + Woche.Value.AddDays(t).ToString("dd.MM.") : "");
            TextRenderer.DrawText(g, text, Ui.SchriftFett, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
        // Stundenkopf
        for (var s = 1; s <= Stunden; s++)
        {
            var r = Zelle(bounds, 0, s);
            var kopf = new RectangleF(bounds.X, r.Y, KopfBreite, r.Height);
            var nachm = zr.Art(0, s) == Tageszeit.Nachmittag || s > zr.LetzteVormittagsstunde;
            using (var b = new SolidBrush(nachm ? Color.FromArgb(230, 245, 248) : Color.FromArgb(242, 244, 248))) g.FillRectangle(b, kopf);
            var z = zr.Zeit(s);
            var text = Format.ZeitenAnzeigen && z != null && r.Height > 26 ? $"{zr.StundenLabel(s)}\n{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}" : zr.StundenLabel(s);
            TextRenderer.DrawText(g, text, r.Height > 26 ? Ui.SchriftKlein : Ui.SchriftKlein, Rectangle.Round(kopf), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.DrawRectangle(linie, kopf.X, kopf.Y, kopf.Width, kopf.Height);
        }
        // Zellen
        var plan = Ix.PlanVon(Typ, ElementId).ToList();
        var el = Ix.Element(Typ, ElementId);
        for (var t = 0; t < Tage; t++)
        {
            var ferien = Woche.HasValue ? Kalender.FerienAm(D, Woche.Value.AddDays(t)) : null;
            for (var s = 1; s <= Stunden; s++)
            {
                var r = Zelle(bounds, t, s);
                var hint = Color.White;
                if (!zr.Verfuegbar(t, s)) hint = Color.FromArgb(225, 225, 225);
                else if (ferien != null) hint = Color.FromArgb(255, 248, 220);
                else if (!druck && _status is null && el != null && Format.DragDropZeitwunschFarben)
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
                var stunden = plan.Where(p => p.Tag == t && p.Stunde == s).ToList();
                var kollision = stunden.Select(p => Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId)).Where(u => u != null)
                    .Select(u => string.IsNullOrWhiteSpace(u!.Band) ? "#" + u.Id : u.Band).Distinct().Count() > 1 && Typ != ElementTyp.Fach;
                for (var i = 0; i < stunden.Count; i++)
                {
                    var teil = new RectangleF(r.X + 1, r.Y + 1 + i * (r.Height - 1) / stunden.Count, r.Width - 2, (r.Height - 1) / stunden.Count - 1);
                    StundeZeichnen(g, teil, stunden[i], Woche?.AddDays(t), druck, kollision);
                }
                if (Woche.HasValue) ZusatzVertretungen(g, r, Woche.Value.AddDays(t), s, stunden.Count);
                if (ferien != null && s == 1) TextRenderer.DrawText(g, ferien.Kurzname, Ui.SchriftKlein, Rectangle.Round(r), Color.Peru, TextFormatFlags.Top | TextFormatFlags.Right);
                g.DrawRectangle(duenn, r.X, r.Y, r.Width, r.Height);
                if (!druck && _hover.HasValue && _hover.Value.Tag == t && _hover.Value.Stunde == s)
                    using (var p = new Pen(Color.Black, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            }
        }
        g.DrawRectangle(linie, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private Color Farbe(Unterricht u, int zeile)
    {
        var z = u.Zeilen.ElementAtOrDefault(zeile) ?? u.Zeilen.FirstOrDefault();
        if (z is null) return Color.FromArgb(230, 230, 240);
        Element? e = Format.Farbe switch
        {
            FarbeNach.Fach => z.FachId.HasValue ? Ix.Faecher.GetValueOrDefault(z.FachId.Value) : null,
            FarbeNach.Lehrer => z.LehrerId.HasValue ? Ix.Lehrer.GetValueOrDefault(z.LehrerId.Value) : null,
            FarbeNach.Klasse => z.KlassenIds.Count > 0 ? Ix.Klassen.GetValueOrDefault(z.KlassenIds[0]) : null,
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

    private void StundeZeichnen(Graphics g, RectangleF r, Planstunde p, DateOnly? datum, bool druck, bool kollision)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
        // Zeilen dieses Elements (bei Kopplungen nur die relevanten)
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
        var vertretung = datum.HasValue ? D.Vertretungen.FirstOrDefault(v => v.Datum == datum && v.Stunde == p.Stunde && v.UnterrichtId == u.Id && zeilen.Contains(v.ZeilenIndex)) : null;
        var entfall = vertretung?.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung or VertretungsArt.Verlegung;
        if (entfall) farbe = Color.FromArgb(235, 235, 235);
        using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
        var text = Ui.Textfarbe(farbe);
        var gewaehlt = !druck && p == Ausgewaehlt;
        var schrift = new Font("Segoe UI", Math.Max(6.5f, Math.Min(Format.Schriftgroesse, r.Height / 2.6f)), FontStyle.Bold);
        var klein = new Font("Segoe UI", Math.Max(6f, Math.Min(Format.Schriftgroesse - 1.5f, r.Height / 3.2f)));
        try
        {
            if (Format.Standardformat || Format.Felder.Count == 0)
            {
                var z = u.Zeilen[zeilen[0]];
                var kopf = (p.Fixiert ? "*" : "") + Kurz(ElementTyp.Fach, z.FachId) + (u.Kursname.Length > 0 && r.Width > 60 ? " " + u.Kursname : "");
                var info = Typ switch
                {
                    ElementTyp.Klasse => string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId))),
                    ElementTyp.Lehrer => Ix.KKlassen(u.Zeilen[zeilen[0]].KlassenIds),
                    ElementTyp.Raum => Ix.KKlassen(u.KlassenIds) + " " + string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Lehrer, u.Zeilen[i].LehrerId))),
                    _ => Ix.KKlassen(u.KlassenIds) + " " + Kurz(ElementTyp.Lehrer, z.LehrerId),
                };
                var raum = Typ == ElementTyp.Raum ? "" : string.Join(",", zeilen.Select(i => Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))).Where(x => x.Length > 0));
                if (vertretung != null)
                {
                    if (vertretung.VertreterId.HasValue && vertretung.VertreterId != z.LehrerId && Typ != ElementTyp.Lehrer) info = "+" + Kurz(ElementTyp.Lehrer, vertretung.VertreterId);
                    if (vertretung.VertretungsraumId.HasValue) raum = Kurz(ElementTyp.Raum, vertretung.VertretungsraumId);
                }
                var zweizeilig = r.Height >= 26;
                if (Format.Gross && u.Zeilen.Count > 1 && r.Height > 40)
                {
                    // Großes Format: alle Kopplungszeilen
                    var y = r.Y + 1;
                    var zh = (r.Height - 2) / u.Zeilen.Count;
                    for (var i = 0; i < u.Zeilen.Count; i++)
                    {
                        var zz = u.Zeilen[i];
                        var zeileText = $"{(p.Fixiert && i == 0 ? "*" : "")}{Kurz(ElementTyp.Fach, zz.FachId)} {Kurz(ElementTyp.Lehrer, zz.LehrerId)} {Ix.KKlassen(zz.KlassenIds)} {Kurz(ElementTyp.Raum, p.RaumFuerZeile(i))}";
                        TextRenderer.DrawText(g, zeileText, klein, new Rectangle((int)r.X + 2, (int)y, (int)r.Width - 3, (int)zh), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        y += zh;
                    }
                }
                else if (zweizeilig)
                {
                    TextRenderer.DrawText(g, kopf, schrift, new Rectangle((int)r.X + 2, (int)r.Y + 1, (int)r.Width - 3, (int)(r.Height / 2)), text, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                    var unten = (info + " " + raum).Trim();
                    TextRenderer.DrawText(g, unten, klein, new Rectangle((int)r.X + 2, (int)(r.Y + r.Height / 2), (int)r.Width - 3, (int)(r.Height / 2)), vertretung?.VertreterId != null ? Color.DarkRed : text, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
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
        }
        finally
        {
            schrift.Dispose();
            klein.Dispose();
        }
        if (entfall)
            using (var pen = new Pen(Color.FromArgb(200, 60, 60), 1.5f)) g.DrawLine(pen, r.Left + 2, r.Bottom - 3, r.Right - 2, r.Top + 3);
        if (kollision)
            using (var pen = new Pen(Color.Red, 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        if (gewaehlt)
            using (var pen = new Pen(Color.FromArgb(255, 140, 0), 2.5f)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
    }

    /// <summary>Vertretungen, die der Lehrer zusätzlich übernimmt (nur in der Wochenansicht).</summary>
    private void ZusatzVertretungen(Graphics g, RectangleF r, DateOnly datum, int stunde, int belegt)
    {
        if (Typ != ElementTyp.Lehrer && Typ != ElementTyp.Raum) return;
        var zusatz = D.Vertretungen.Where(v => v.Datum == datum && v.Stunde == stunde &&
            (Typ == ElementTyp.Lehrer ? v.VertreterId == ElementId && v.LehrerId != ElementId : v.VertretungsraumId == ElementId)).ToList();
        if (zusatz.Count == 0) return;
        var h = r.Height / (belegt + zusatz.Count);
        var y = r.Y + belegt * h;
        foreach (var v in zusatz)
        {
            var rr = new RectangleF(r.X + 1, y + 1, r.Width - 2, h - 2);
            using (var b = new SolidBrush(Color.FromArgb(255, 225, 225))) g.FillRectangle(b, rr);
            using (var p = new Pen(Color.Red)) g.DrawRectangle(p, rr.X, rr.Y, rr.Width, rr.Height);
            TextRenderer.DrawText(g, $"{Ix.KFach(v.VertretungsfachId ?? v.FachId)} {Ix.KKlassen(v.KlassenIds)} ({Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art)})", Ui.SchriftKlein, Rectangle.Round(rr), Color.DarkRed, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            y += h;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Maus / Drag & Drop
    // ------------------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var p = StundeBei(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            _down = e.Location;
            _downStunde = p;
            if (p != Ausgewaehlt)
            {
                Ausgewaehlt = p;
                Invalidate();
                AuswahlGeaendert?.Invoke(p);
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (p != null && p != Ausgewaehlt) { Ausgewaehlt = p; Invalidate(); AuswahlGeaendert?.Invoke(p); }
            Kontext?.Invoke(e.Location, p);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (StundeBei(e.Location) is { } p) Doppelklick?.Invoke(p);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _downStunde != null && Bearbeitbar && Woche is null &&
            (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4))
        {
            var p = _downStunde;
            _downStunde = null;
            var idx = D.Stundenplan.IndexOf(p);
            if (idx >= 0) DoDragDrop("plan:" + idx, DragDropEffects.Move);
            return;
        }
        var s = StundeBei(e.Location);
        var tip = s is null ? "" : Lupentext(s);
        if (tip != _letzterTip)
        {
            _letzterTip = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    public static string Lupentext(Planstunde p)
    {
        if (!Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return "";
        var zeilen = u.Zeilen.Select((z, i) => $"{Ix.KFach(z.FachId)}  {Ix.KLehrer(z.LehrerId)}  {Ix.KKlassen(z.KlassenIds)}  {Ix.KRaum(p.RaumFuerZeile(i))}");
        return $"U-Nr. {u.Id}{(p.Fixiert ? " (fixiert)" : "")}\n{string.Join("\n", zeilen)}";
    }

    private Unterricht? DragUnterricht(DragEventArgs e, out Planstunde? quelle)
    {
        quelle = null;
        if (e.Data?.GetData(DataFormats.Text) is not string s) return null;
        if (s.StartsWith("plan:") && int.TryParse(s[5..], out var idx) && idx < D.Stundenplan.Count)
        {
            quelle = D.Stundenplan[idx];
            return Ix.Unterrichte.GetValueOrDefault(quelle.UnterrichtId);
        }
        if (s.StartsWith("unterricht:") && int.TryParse(s[11..], out var uid)) return Ix.Unterrichte.GetValueOrDefault(uid);
        return null;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        var u = DragUnterricht(e, out var quelle);
        if (u is null || !Bearbeitbar || Woche.HasValue) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
        _status = new Feldstatus[Tage, Stunden];
        var ignorieren = quelle is null ? null : new List<Planstunde> { quelle };
        for (var t = 0; t < Tage; t++)
            for (var s = 1; s <= Stunden; s++)
                _status[t, s - 1] = Planungsdienst.Status(D, Ix, u, t, s, 1, ignorieren);
        Invalidate();
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
}
