using System.Drawing.Drawing2D;
using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Druck;

/// <summary>
/// Stundenplan als schlichtes Schulformular in Schwarz-Weiß (wie die gewohnten Planausdrucke): Kopf mit Schule, Schuljahr,
/// „Gilt ab“ und Druckdatum, großer Kurzname mit Langname, links das Raster mit Pausenzeilen und grau hinterlegten belegten
/// Stunden (Doppelstunden zusammengefasst, Kopplungen mit „*“ und Fußnote), rechts die Unterrichtsliste, unten die
/// Fußnoten (Lehrkraft, Fach, Raum, Klassen) und die Fächerlegende, ganz unten Periode und Seitenzahl.
/// Im kompakten Format (zwei Pläne je Seite) entfallen Liste und Legenden.
/// </summary>
internal static class StundenplanFormular
{
    private const string Schrift = "Arial";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Eine Druckseite mit einem (oder im kompakten Format mehreren) Plänen.</summary>
    public static void Seite(Graphics g, Rectangle r, ElementTyp typ, IReadOnlyList<int> ids, bool kompakt, int seite, int seiten)
    {
        var d = AppZustand.D;
        var ix = new DatenIndex(d);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var oben = Kopf(g, r, d);
        var unten = r.Bottom - 18;
        Fuss(g, r, d, seite, seiten);
        var abstand = 28;
        var breite = (r.Width - (ids.Count - 1) * abstand) / Math.Max(1, ids.Count);
        for (var i = 0; i < ids.Count; i++)
            Plan(g, new Rectangle(r.Left + i * (breite + abstand), oben, breite, unten - oben), d, ix, typ, ids[i], ids.Count > 1);
    }

    // ------------------------------------------------------------------------------------------ Kopf und Fuß

    private static int Kopf(Graphics g, Rectangle r, Schuldatei d)
    {
        using var fett = new Font(Schrift, 9.5f, FontStyle.Bold);
        using var normal = new Font(Schrift, 9f);
        var e = d.Einstellungen;
        g.DrawString(e.Schulname, fett, Brushes.Black, r.Left, r.Top);
        var zweite = e.Schulname2.Length > 0 ? e.Schulname2 : e.Adresse;
        if (zweite.Length > 0) g.DrawString(zweite, normal, Brushes.Black, r.Left, r.Top + 15);
        var x2 = r.Left + Math.Max(160, (int)Math.Max(g.MeasureString(e.Schulname, fett).Width, zweite.Length > 0 ? g.MeasureString(zweite, normal).Width : 0) + 28);
        g.DrawString($"Stundenplan {e.SchuljahrVon.Year}/{e.SchuljahrVon.Year + 1}", normal, Brushes.Black, x2, r.Top + 1);
        g.DrawString($"Gilt ab: {d.AktivePeriode.Von.ToString("d. MMMM", De)}", normal, Brushes.Black, x2, r.Top + 15);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"UntPlan {e.SchuljahrVon.Year}", fett, Brushes.Black, new RectangleF(r.Left, r.Top, r.Width, 16), rechts);
        g.DrawString(DateTime.Now.ToString("dd.MM.yyyy  HH:mm", De), normal, Brushes.Black, new RectangleF(r.Left, r.Top + 15, r.Width, 16), rechts);
        using var stift = new Pen(Color.Black, 1f);
        g.DrawLine(stift, r.Left, r.Top + 31, r.Right, r.Top + 31);
        return r.Top + 36;
    }

    private static void Fuss(Graphics g, Rectangle r, Schuldatei d, int seite, int seiten)
    {
        using var f = new Font(Schrift, 8f);
        using var stift = new Pen(Color.Black, 1f);
        var y = r.Bottom - 14;
        g.DrawLine(stift, r.Left, y - 2, r.Right, y - 2);
        var p = d.AktivePeriode;
        var text = $"{(p.Name.Length > 0 ? p.Name : "Periode")}  {Format.Datum(p.Von)} - {(p.Bis is DateOnly b ? Format.Datum(b) : Format.Datum(d.Einstellungen.SchuljahrBis))}     UntPlan";
        using var mitte = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(text, f, Brushes.Black, new RectangleF(r.Left, y, r.Width, 14), mitte);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"Seite {seite} / {seiten}", f, Brushes.Black, new RectangleF(r.Left, y, r.Width, 14), rechts);
    }

    // ------------------------------------------------------------------------------------------ ein Plan

    /// <summary>Teil eines Unterrichts, der zum Element gehört (Kopplungszeile mit verplantem Raum).</summary>
    private sealed record Teil(Kopplungszeile Zeile, int? Raum);
    private sealed record Fussnote(int Nr, List<(string Text, string Klassen)> Zeilen);

    private static void Plan(Graphics g, Rectangle r, Schuldatei d, DatenIndex ix, ElementTyp typ, int id, bool kompakt)
    {
        var el = ix.Element(typ, id);
        using var fName = new Font(Schrift, kompakt ? 15f : 18f);
        using var fLang = new Font(Schrift, kompakt ? 11f : 13f);
        var kurz = el?.Kurzname ?? "";
        var lang = el is Lehrer l ? l.VollerName : el?.Langname ?? "";
        g.DrawString(kurz, fName, Brushes.Black, r.Left - 3, r.Top);
        var kb = g.MeasureString(kurz, fName);
        if (lang.Length > 0 && lang != kurz) g.DrawString(lang, fLang, Brushes.Black, r.Left + kb.Width + 2, r.Top + kb.Height - g.MeasureString(lang, fLang).Height - 2);
        var top = r.Top + (int)kb.Height + 4;

        var fussnoten = new List<Fussnote>();
        var faecher = new SortedSet<int>(Comparer<int>.Create((a, b) => string.Compare(ix.KFach(a), ix.KFach(b), StringComparison.CurrentCultureIgnoreCase) is var c && c != 0 ? c : a.CompareTo(b)));
        if (kompakt)
        {
            Raster(g, new Rectangle(r.Left, top, r.Width, r.Bottom - top - 4), d, ix, typ, id, fussnoten, faecher, true);
            return;
        }
        var legende = 150;
        var rasterBreite = (int)(r.Width * .485);
        Raster(g, new Rectangle(r.Left, top, rasterBreite, r.Bottom - top - legende - 10), d, ix, typ, id, fussnoten, faecher, false);
        Liste(g, new Rectangle(r.Left + rasterBreite + 16, top + 18, r.Width - rasterBreite - 16, r.Bottom - top - legende - 28), ix, typ, id, faecher);
        Legenden(g, new Rectangle(r.Left, r.Bottom - legende, r.Width, legende - 4), ix, fussnoten, faecher);
    }

    /// <summary>Teile eines Unterrichts in einer Planstunde, die das Element betreffen.</summary>
    private static List<Teil> Teile(Unterricht u, Planstunde p, ElementTyp typ, int id)
    {
        var res = new List<Teil>();
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            var raum = i < p.Raeume.Count ? p.Raeume[i] : null;
            var dabei = typ switch
            {
                ElementTyp.Klasse => z.KlassenIds.Contains(id),
                ElementTyp.Lehrer => z.LehrerId == id,
                ElementTyp.Raum => raum == id,
                ElementTyp.Fach => z.FachId == id,
                _ => true,
            };
            if (dabei) res.Add(new Teil(z, raum ?? z.FachraumId ?? z.StammraumId));
        }
        return res.Count > 0 ? res : u.Zeilen.Take(1).Select(z => new Teil(z, null)).ToList();
    }

    private static void Raster(Graphics g, Rectangle r, Schuldatei d, DatenIndex ix, ElementTyp typ, int id, List<Fussnote> fussnoten, ISet<int> faecher, bool kompakt)
    {
        var zr = d.Zeitraster;
        var tage = zr.TageProWoche;
        var stunden = zr.StundenProTag;
        var plan = ix.PlanVon(typ, id).ToList();
        // Zeilen: Unterrichtsstunden und – wo eine Pause liegt – eine schmale Pausenzeile mit ihrer Bezeichnung
        var zeilen = new List<(int Stunde, string Pause)>();
        for (var s = 1; s <= stunden; s++)
        {
            zeilen.Add((s, ""));
            var pause = zr.Pausen.FirstOrDefault(p => p.NachStunde == s);
            if (pause != null && s < stunden) zeilen.Add((0, pause.Bezeichnung.Length > 0 ? pause.Bezeichnung : "Pause"));
        }
        var kopfH = kompakt ? 24 : 30;
        var pauseH = 14;
        var anzahlP = zeilen.Count(z => z.Stunde == 0);
        var zeileH = Math.Clamp((r.Height - kopfH - anzahlP * pauseH) / Math.Max(1, stunden), 18, kompakt ? 44 : 48);
        // Stundenspalte so breit, dass zweistellige Nummern und die Uhrzeit nebeneinander passen
        float nrB, zeitB;
        using (var mNr = new Font(Schrift, kompakt ? 13f : 16f, FontStyle.Bold))
        using (var mZeit = new Font(Schrift, 6.5f))
        {
            nrB = Enumerable.Range(1, stunden).Max(st => g.MeasureString((zr.ErsteStundennummer - 1 + st).ToString(De), mNr).Width);
            zeitB = Enumerable.Range(1, stunden).Select(zr.Zeit).Where(z => z != null)
                .Select(z => g.MeasureString($"{z!.Beginn:H\\:mm}-{z.Ende:H\\:mm}", mZeit).Width).DefaultIfEmpty(0).Max();
        }
        var stdB = (int)Math.Ceiling(Math.Max(kompakt ? 58 : 70, nrB + zeitB + 4));
        var tagB = (r.Width - stdB) / (float)tages(tage);
        var hoehe = kopfH + anzahlP * pauseH + stunden * zeileH;
        var y0 = r.Top;
        static int tages(int t) => Math.Max(1, t);

        using var duenn = new Pen(Color.Black, .75f);
        using var dick = new Pen(Color.Black, 2f);
        using var grau = new SolidBrush(Color.FromArgb(222, 222, 222));
        using var kopfGrau = new SolidBrush(Color.FromArgb(232, 232, 232));
        using var fTag = new Font(Schrift, kompakt ? 9f : 10.5f, FontStyle.Bold);
        using var fNr = new Font(Schrift, kompakt ? 13f : 16f, FontStyle.Bold);
        using var fZeit = new Font(Schrift, 6.5f);
        using var fPause = new Font(Schrift, 8f);
        using var mitte = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // Kopfzeile mit den Wochentagen
        g.FillRectangle(kopfGrau, r.Left, y0, r.Width, kopfH);
        for (var t = 0; t < tage; t++)
            g.DrawString(Planungsdienst.Tagname(d, t, true), fTag, Brushes.Black, new RectangleF(r.Left + stdB + t * tagB, y0, tagB, kopfH), mitte);

        // y-Lage jeder Stunde
        var lage = new Dictionary<int, (int Y, int H)>();
        var pausen = new List<(int Y, string Text)>();
        var y = y0 + kopfH;
        foreach (var z in zeilen)
        {
            if (z.Stunde == 0) { pausen.Add((y, z.Pause)); y += pauseH; }
            else { lage[z.Stunde] = (y, zeileH); y += zeileH; }
        }

        // Inhalte je Tag: gleiche Stunden hintereinander (ohne Pause dazwischen) als ein Block
        for (var t = 0; t < tage; t++)
        {
            var s = 1;
            while (s <= stunden)
            {
                var hier = plan.Where(p => p.Tag == t && p.Stunde == s).ToList();
                var schluessel = string.Join(",", hier.Select(p => p.UnterrichtId).OrderBy(x => x));
                var bis = s;
                while (schluessel.Length > 0 && bis < stunden && !zr.Pausen.Any(p => p.NachStunde == bis)
                       && string.Join(",", plan.Where(p => p.Tag == t && p.Stunde == bis + 1).Select(p => p.UnterrichtId).OrderBy(x => x)) == schluessel)
                    bis++;
                var x = r.Left + stdB + t * tagB;
                var zelle = new RectangleF(x, lage[s].Y, tagB, lage[bis].Y + lage[bis].H - lage[s].Y);
                if (hier.Count > 0)
                {
                    g.FillRectangle(grau, zelle);
                    Zelle(g, zelle, hier, ix, typ, id, fussnoten, faecher);
                }
                g.DrawRectangle(duenn, zelle.X, zelle.Y, zelle.Width, zelle.Height);
                s = bis + 1;
            }
        }

        // Stundenspalte: große Nummer und Uhrzeit
        foreach (var (st, (yy, hh)) in lage)
        {
            var zeit = zr.Zeit(st);
            g.FillRectangle(kopfGrau, r.Left, yy, stdB, hh);
            g.DrawRectangle(duenn, r.Left, yy, stdB, hh);
            var nr = (zr.ErsteStundennummer - 1 + st).ToString(De);
            var nb = g.MeasureString(nr, fNr);
            g.DrawString(nr, fNr, Brushes.Black, r.Left + 2, yy + (hh - nb.Height) / 2);
            if (zeit != null)
            {
                var txt = $"{zeit.Beginn:H\\:mm}-{zeit.Ende:H\\:mm}";
                var zb = g.MeasureString(txt, fZeit);
                g.DrawString(txt, fZeit, Brushes.Black, r.Left + nrB, yy + (hh - zb.Height) / 2 + 2);
            }
        }
        // Pausenzeilen über die Tagesspalten
        foreach (var (py, text) in pausen)
        {
            g.FillRectangle(Brushes.White, r.Left + stdB, py, r.Width - stdB, pauseH);
            g.DrawRectangle(duenn, r.Left + stdB, py, r.Width - stdB, pauseH);
            g.DrawString(text, fPause, Brushes.Black, new RectangleF(r.Left + stdB, py, r.Width - stdB, pauseH), mitte);
            g.FillRectangle(Brushes.White, r.Left, py, stdB, pauseH);
        }
        // kräftige Linien: Rahmen, unter dem Kopf, rechts der Stundenspalte
        g.DrawRectangle(dick, r.Left, y0, r.Width, hoehe);
        g.DrawLine(dick, r.Left, y0 + kopfH, r.Right, y0 + kopfH);
        g.DrawLine(dick, r.Left + stdB, y0, r.Left + stdB, y0 + hoehe);
        for (var t = 1; t < tage; t++) g.DrawLine(duenn, r.Left + stdB + t * tagB, y0, r.Left + stdB + t * tagB, y0 + kopfH);
    }

    /// <summary>Inhalt einer Zelle: je Unterricht eine Zeile (fett, normal, unterstrichen); Kopplungen mit „*“ und Fußnotennummer.</summary>
    private static void Zelle(Graphics g, RectangleF z, List<Planstunde> hier, DatenIndex ix, ElementTyp typ, int id, List<Fussnote> fussnoten, ISet<int> faecher)
    {
        var zeilen = new List<(string A, string B, string C, bool Stern)>();
        var noten = new List<int>();
        foreach (var p in hier)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            var teile = Teile(u, p, typ, id);
            foreach (var t in teile) if (t.Zeile.FachId is int f) faecher.Add(f);
            var gekoppelt = u.Zeilen.Count > 1;
            if (gekoppelt)
            {
                // alle Kopplungszeilen in die Fußnote, in der Zelle nur die eigenen
                var nr = fussnoten.FirstOrDefault(n => n.Zeilen.SequenceEqual(FussnotenZeilen(u, p, ix)))?.Nr;
                if (nr is null) { nr = fussnoten.Count + 1; fussnoten.Add(new Fussnote(nr.Value, FussnotenZeilen(u, p, ix))); }
                noten.Add(nr.Value);
            }
            foreach (var t in teile.Take(gekoppelt && teile.Count > 3 ? 3 : 99))
            {
                var fach = ix.KFach(t.Zeile.FachId);
                var lehrer = ix.KLehrer(t.Zeile.LehrerId);
                var raum = t.Raum is int rr ? ix.KRaum(rr) : "";
                var klassen = ix.KKlassen(t.Zeile.KlassenIds);
                zeilen.Add(typ switch
                {
                    ElementTyp.Klasse => (fach, lehrer, raum, gekoppelt),
                    ElementTyp.Lehrer => (klassen, raum, fach, gekoppelt),
                    _ => (klassen, lehrer, fach, gekoppelt),
                });
            }
        }
        if (zeilen.Count == 0) return;
        var groesse = zeilen.Count <= 2 ? 8.5f : zeilen.Count <= 4 ? 6.5f : 5.5f;
        using var fett = new Font(Schrift, groesse, FontStyle.Bold);
        using var normal = new Font(Schrift, groesse);
        using var klein = new Font(Schrift, 5.5f);
        var fmt = StringFormat.GenericTypographic;
        var zeileH = normal.GetHeight(g) + (zeilen.Count <= 2 ? 3 : 0.5f);
        var platz = z.Height - (noten.Count > 0 ? 7 : 0);
        var maxZeilen = Math.Max(1, (int)(platz / zeileH));
        var sichtbar = zeilen.Take(maxZeilen).ToList();
        var y = z.Y + (platz - sichtbar.Count * zeileH) / 2;
        foreach (var (a, b, c, stern) in sichtbar)
        {
            var ta = (stern ? "*" : "") + a;
            var wa = g.MeasureString(ta, fett, PointF.Empty, fmt).Width;
            var wb = b.Length > 0 ? g.MeasureString(" " + b, normal, PointF.Empty, fmt).Width : 0;
            var mittelUnter = typ == ElementTyp.Lehrer; // im Lehrerplan ist der Raum das zweite Feld
            var wc = c.Length > 0 ? g.MeasureString(" " + c, normal, PointF.Empty, fmt).Width : 0;
            var x = z.X + Math.Max(2, (z.Width - wa - wb - wc) / 2);
            g.DrawString(ta, fett, Brushes.Black, x, y, fmt); x += wa;
            var leer = g.MeasureString(" ", normal, PointF.Empty, fmt).Width;
            if (b.Length > 0)
            {
                if (mittelUnter) Unterstrichen(g, b, normal, x + leer, y, fmt); else g.DrawString(b, normal, Brushes.Black, x + leer, y, fmt);
                x += wb;
            }
            if (c.Length > 0)
            {
                if (typ == ElementTyp.Klasse) Unterstrichen(g, c, normal, x + leer, y, fmt); else g.DrawString(c, normal, Brushes.Black, x + leer, y, fmt);
            }
            y += zeileH;
        }
        if (noten.Count > 0)
            g.DrawString(string.Join(" ", noten.Distinct().Select(n => $"{n})")), klein, Brushes.Black, z.X + 2, z.Bottom - 8, fmt);
    }

    /// <summary>Text unterstrichen zeichnen (eigene Linie – die Schriftunterstreichung fehlt bei manchen Druckertreibern).</summary>
    private static void Unterstrichen(Graphics g, string text, Font f, float x, float y, StringFormat fmt)
    {
        g.DrawString(text, f, Brushes.Black, x, y, fmt);
        var w = g.MeasureString(text, f, PointF.Empty, fmt).Width;
        using var stift = new Pen(Color.Black, .6f);
        var uy = y + f.GetHeight(g) * .92f;
        g.DrawLine(stift, x, uy, x + w, uy);
    }

    private static List<(string Text, string Klassen)> FussnotenZeilen(Unterricht u, Planstunde p, DatenIndex ix) =>
        u.Zeilen.Select((z, i) =>
        {
            var raum = i < p.Raeume.Count && p.Raeume[i] is int r ? ix.KRaum(r) : z.FachraumId is int f ? ix.KRaum(f) : "";
            return ($"{ix.KLehrer(z.LehrerId)}, {ix.KFach(z.FachId)}{(raum.Length > 0 ? ",  " + raum : "")}", ix.KKlassen(z.KlassenIds));
        }).ToList();

    // ------------------------------------------------------------------------------------------ Unterrichtsliste rechts

    private static void Liste(Graphics g, Rectangle r, DatenIndex ix, ElementTyp typ, int id, ISet<int> faecher)
    {
        using var kopf = new Font(Schrift, 9f);
        using var f = new Font(Schrift, 8.5f);
        using var grau = new SolidBrush(Color.FromArgb(232, 232, 232));
        using var linie = new Pen(Color.FromArgb(200, 200, 200), .75f);
        var mitKlassen = typ != ElementTyp.Klasse;
        // Spalten: U-Nr, Kl,Le, Wst, Lehrer, Fach, (Klasse(n))
        var spalten = mitKlassen
            ? new[] { ("U-Nr", .10f), ("Kl,Le", .10f), ("Wst", .08f), ("Lehrer", .18f), ("Fach", .17f), ("Klasse(n)", .37f) }
            : new[] { ("U-Nr", .12f), ("Kl,Le", .12f), ("Std.", .10f), ("Wst", .08f), ("Lehrer", .26f), ("Fach", .32f) };
        var xs = new float[spalten.Length + 1];
        xs[0] = r.Left;
        for (var i = 0; i < spalten.Length; i++) xs[i + 1] = xs[i] + spalten[i].Item2 * r.Width;
        var zeileH = f.GetHeight(g) + 2;
        g.FillRectangle(grau, r.Left, r.Top, r.Width, zeileH + 3);
        for (var i = 0; i < spalten.Length; i++) g.DrawString(spalten[i].Item1, kopf, Brushes.Black, xs[i] + 2, r.Top + 1);
        var y = r.Top + zeileH + 5f;
        foreach (var u in ix.UnterrichteVon(typ, id).OrderBy(u => u.Id))
        {
            var zeilen = u.Zeilen.Where(z => typ switch
            {
                ElementTyp.Lehrer => z.LehrerId == id,
                _ => true,
            }).ToList();
            if (typ == ElementTyp.Klasse) zeilen = u.Zeilen.Where(z => z.KlassenIds.Contains(id)).ToList();
            if (zeilen.Count == 0) zeilen = u.Zeilen.Take(1).ToList();
            var hoehe = zeilen.Count * zeileH;
            if (y + hoehe > r.Bottom) { g.DrawString("…", f, Brushes.Black, r.Left + 2, y); break; }
            foreach (var z in zeilen) if (z.FachId is int fa) faecher.Add(fa);
            var klLe = u.Zeilen.Count > 1 ? $"{u.KlassenIds.Count()}, {u.LehrerIds.Count()}" : "";
            var nvpl = ix.NichtVerplant(u);
            string[] werte(int i, Kopplungszeile z) => mitKlassen
                ? new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 ? u.Wochenstunden.ToString(De) : "", ix.KLehrer(z.LehrerId), ix.KFach(z.FachId), ix.KKlassen(z.KlassenIds) }
                : new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 && nvpl > 0 ? nvpl.ToString(De) : "", i == 0 ? u.Wochenstunden.ToString(De) : "", ix.KLehrer(z.LehrerId), ix.KFach(z.FachId) };
            for (var i = 0; i < zeilen.Count; i++)
            {
                var w = werte(i, zeilen[i]);
                for (var c = 0; c < w.Length; c++)
                    g.DrawString(w[c], f, Brushes.Black, new RectangleF(xs[c] + 2, y + i * zeileH, xs[c + 1] - xs[c] - 3, zeileH), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
            }
            y += hoehe;
            g.DrawLine(linie, r.Left, y, r.Right, y);
            y += 1;
        }
    }

    // ------------------------------------------------------------------------------------------ Legenden unten

    private static void Legenden(Graphics g, Rectangle r, DatenIndex ix, List<Fussnote> fussnoten, ISet<int> faecher)
    {
        using var kopf = new Font(Schrift, 8.5f);
        using var f = new Font(Schrift, 8.5f);
        using var fett = new Font(Schrift, 8.5f, FontStyle.Bold);
        using var grau = new SolidBrush(Color.FromArgb(232, 232, 232));
        using var stift = new Pen(Color.Black, .75f);
        var zeileH = f.GetHeight(g) + 1;
        var x = (float)r.Left;
        var breiteNote = r.Width * .25f;

        // Fußnoten in zwei Spalten
        var noteZeilen = fussnoten.SelectMany(n => n.Zeilen.Select((z, i) => (Nr: i == 0 ? $"{n.Nr})" : "", z.Text, z.Klassen))).ToList();
        var proSpalte = Math.Max(1, (int)((r.Height - zeileH - 4) / zeileH));
        for (var sp = 0; sp < 2; sp++)
        {
            var sx = x + sp * breiteNote;
            if (sp == 0 || noteZeilen.Count > proSpalte)
            {
                g.FillRectangle(grau, sx, r.Top, breiteNote - 10, zeileH);
                g.DrawString("Nr. Le.,Fa.,Rm.", kopf, Brushes.Black, sx, r.Top);
                g.DrawString("Kla.", kopf, Brushes.Black, sx + breiteNote - 46, r.Top);
                g.DrawLine(stift, sx, r.Top + zeileH, sx + breiteNote - 10, r.Top + zeileH);
            }
            var teil = noteZeilen.Skip(sp * proSpalte).Take(proSpalte).ToList();
            for (var i = 0; i < teil.Count; i++)
            {
                var yy = r.Top + zeileH + 2 + i * zeileH;
                g.DrawString(teil[i].Nr, f, Brushes.Black, sx, yy);
                g.DrawString(teil[i].Text, f, Brushes.Black, new RectangleF(sx + 22, yy, breiteNote - 80, zeileH), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
                g.DrawString(teil[i].Klassen, f, Brushes.Black, new RectangleF(sx + breiteNote - 46, yy, 40, zeileH), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        // Fächerlegende (Kurzname, Langname) in zwei Spaltenpaaren
        var fx = x + 2 * breiteNote;
        var fb = r.Right - fx;
        var liste = faecher.Select(id => ix.Faecher.GetValueOrDefault(id)).Where(fa => fa != null).ToList();
        g.DrawString("Fächer", fett, Brushes.Black, fx, r.Top);
        var proSp = Math.Max(1, (int)((r.Height - 2 * zeileH - 4) / zeileH));
        for (var sp = 0; sp < 2; sp++)
        {
            var sx = fx + sp * fb / 2;
            g.DrawString("Name", kopf, Brushes.Black, sx, r.Top + zeileH);
            g.DrawString("Langname", kopf, Brushes.Black, sx + 60, r.Top + zeileH);
            g.DrawLine(stift, sx, r.Top + 2 * zeileH, sx + fb / 2 - 10, r.Top + 2 * zeileH);
            var teil = liste.Skip(sp * proSp).Take(proSp).ToList();
            for (var i = 0; i < teil.Count; i++)
            {
                var yy = r.Top + 2 * zeileH + 2 + i * zeileH;
                g.DrawString(teil[i]!.Kurzname, f, Brushes.Black, sx, yy);
                g.DrawString(teil[i]!.Langname, f, Brushes.Black, new RectangleF(sx + 60, yy, fb / 2 - 72, zeileH), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
            }
        }
    }
}
