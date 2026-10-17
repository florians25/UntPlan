using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using Farbe = UntPlanStudio.Dienste.PdfSeite.Farbe;

namespace UntPlanStudio.Dienste;

/// <summary>Einstellungen eines Stundenplandrucks (aus der Druckauswahl und dem gewählten Stundenplanformat).</summary>
public sealed record StundenplanDruckOptionen
{
    /// <summary>Kompaktes Format: zwei Pläne je Seite ohne Unterrichtsliste und Legenden (Desktop: „Format groß“ aus).</summary>
    public bool Kompakt { get; init; }
    /// <summary>Belegte Stunden in der (aufgehellten) Fachfarbe statt grau.</summary>
    public bool Farbig { get; init; }
    /// <summary>Doppelstunden/Blöcke nicht zusammenfassen.</summary>
    public bool Einzelstunden { get; init; }
    public bool PausenZeigen { get; init; } = true;
    public bool ZeitenZeigen { get; init; } = true;
    /// <summary>Langnamen der Fächer in den Zellen.</summary>
    public bool Langname { get; init; }
    public int VonStunde { get; init; } = 1;
    /// <summary>0 = bis zur letzten Stunde.</summary>
    public int BisStunde { get; init; }
    public int VonTag { get; init; }
    /// <summary>-1 = bis zum letzten Tag.</summary>
    public int BisTag { get; init; } = -1;
    /// <summary>Name des verwendeten Stundenplanformats (Anzeige im Fuß).</summary>
    public string Format { get; init; } = "";

    public static StundenplanDruckOptionen AusFormat(StundenplanFormat? f, bool kompakt, bool farbig) => f is null
        ? new StundenplanDruckOptionen { Kompakt = kompakt, Farbig = farbig }
        : new StundenplanDruckOptionen
        {
            Kompakt = kompakt, Farbig = farbig && !f.SchwarzWeissDruck, Einzelstunden = f.Einzelstunden, PausenZeigen = f.Pausenbezeichnung,
            ZeitenZeigen = f.ZeitenAnzeigen, Langname = f.Langname, VonStunde = Math.Max(1, f.VonStunde), BisStunde = f.BisStunde,
            VonTag = Math.Max(0, f.VonTag), BisTag = f.BisTag, Format = f.Name,
        };
}

/// <summary>
/// Stundenplan als schlichtes Schulformular in Schwarz-Weiß wie der Desktop-Druck (Druck/StundenplanFormular): Kopf mit Schule,
/// Schuljahr, „Gilt ab“ und Druckdatum, großer Kurzname mit Langname, links das Raster mit Pausenzeilen und grau hinterlegten
/// belegten Stunden (Doppelstunden zusammengefasst, Kopplungen mit „*“ und Fußnote), rechts die Unterrichtsliste, unten Fußnoten
/// und Fächerlegende, ganz unten Periode und Seitenzahl. Kompakt: zwei Pläne je Seite (A4 quer) ohne Liste und Legenden.
/// </summary>
public static class StundenplanPdf
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private const float RandX = 25, RandY = 29;
    private static readonly Farbe Grau = Farbe.Grau(222 / 255f);
    private static readonly Farbe KopfGrau = Farbe.Grau(232 / 255f);

    public static byte[] Erzeugen(Schuldatei d, DatenIndex ix, ElementTyp typ, IReadOnlyList<int> ids, StundenplanDruckOptionen o)
    {
        var pdf = new PdfDokument { Titel = $"Stundenpläne {d.Einstellungen.Schulname}" };
        var proSeite = o.Kompakt && ids.Count > 1 ? 2 : 1;
        var seiten = Math.Max(1, (ids.Count + proSeite - 1) / proSeite);
        for (var i = 0; i < Math.Max(1, ids.Count); i += proSeite)
        {
            var s = pdf.NeueSeite(true);
            var r = new Kasten(RandX, RandY, s.Breite - 2 * RandX, s.Hoehe - 2 * RandY);
            var oben = Kopf(s, r, d);
            Fuss(s, r, d, i / proSeite + 1, seiten, o.Format);
            var unten = r.Unten - 13;
            var teil = ids.Skip(i).Take(proSeite).ToList();
            if (teil.Count == 0) { s.Text(r.X, oben + 30, 12, "Keine Elemente gewählt."); break; }
            const float abstand = 20;
            var breite = (r.B - (teil.Count - 1) * abstand) / teil.Count;
            for (var k = 0; k < teil.Count; k++)
                Plan(s, new Kasten(r.X + k * (breite + abstand), oben, breite, unten - oben), d, ix, typ, teil[k], teil.Count > 1 || o.Kompakt, o);
        }
        return pdf.Erzeugen();
    }

    internal readonly record struct Kasten(float X, float Y, float B, float H)
    {
        public float Rechts => X + B;
        public float Unten => Y + H;
    }

    // ------------------------------------------------------------------------------------------ Kopf und Fuß

    private static float Kopf(PdfSeite s, Kasten r, Schuldatei d)
    {
        var e = d.Einstellungen;
        s.Text(r.X, r.Y + 9, 9.5f, e.Schulname, true);
        var zweite = e.Schulname2.Length > 0 ? e.Schulname2 : e.Adresse;
        if (zweite.Length > 0) s.Text(r.X, r.Y + 20, 9, zweite);
        var x2 = r.X + Math.Max(115, Math.Max(PdfDokument.TextBreite(e.Schulname, 9.5f, true), PdfDokument.TextBreite(zweite, 9)) + 20);
        s.Text(x2, r.Y + 9, 9, $"Stundenplan {e.SchuljahrVon.Year}/{e.SchuljahrVon.Year + 1}");
        s.Text(x2, r.Y + 20, 9, $"Gilt ab: {d.AktivePeriode.Von.ToString("d. MMMM", De)}");
        s.TextRechts(r.Rechts, r.Y + 9, 9.5f, $"UntPlan {e.SchuljahrVon.Year}", true);
        s.TextRechts(r.Rechts, r.Y + 20, 9, DateTime.Now.ToString("dd.MM.yyyy  HH:mm", De));
        s.Linie(r.X, r.Y + 23, r.Rechts, r.Y + 23, 0.75f);
        return r.Y + 27;
    }

    private static void Fuss(PdfSeite s, Kasten r, Schuldatei d, int seite, int seiten, string format)
    {
        var y = r.Unten - 10;
        s.Linie(r.X, y - 1.5f, r.Rechts, y - 1.5f, 0.75f);
        var p = d.AktivePeriode;
        var text = $"{(p.Name.Length > 0 ? p.Name : "Periode")}  {Format.Datum(p.Von)} - {Format.Datum(p.Bis ?? d.Einstellungen.SchuljahrBis)}     UntPlan";
        s.TextMittig(r.X, r.B, y + 8, 8, text);
        if (format.Length > 0) s.Text(r.X, y + 8, 8, format);
        s.TextRechts(r.Rechts, y + 8, 8, $"Seite {seite} / {seiten}");
    }

    // ------------------------------------------------------------------------------------------ ein Plan

    private sealed record Teil(Kopplungszeile Zeile, int? Raum);
    private sealed record Fussnote(int Nr, List<(string Text, string Klassen)> Zeilen);

    private static void Plan(PdfSeite s, Kasten r, Schuldatei d, DatenIndex ix, ElementTyp typ, int id, bool kompakt, StundenplanDruckOptionen o)
    {
        var el = ix.Element(typ, id);
        var gName = kompakt ? 15f : 18f;
        var gLang = kompakt ? 11f : 13f;
        var kurz = el?.Kurzname ?? "";
        var lang = el is Lehrer l ? l.VollerName : el?.Langname ?? "";
        s.Text(r.X, r.Y + gName * 0.95f, gName, kurz);
        if (lang.Length > 0 && lang != kurz) s.Text(r.X + PdfDokument.TextBreite(kurz, gName) + 4, r.Y + gName * 0.95f, gLang, lang);
        var top = r.Y + gName * 1.15f + 3;

        var fussnoten = new List<Fussnote>();
        var faecher = new SortedSet<int>(Comparer<int>.Create((a, b) => string.Compare(ix.KFach(a), ix.KFach(b), StringComparison.CurrentCultureIgnoreCase) is var c && c != 0 ? c : a.CompareTo(b)));
        if (kompakt)
        {
            Raster(s, new Kasten(r.X, top, r.B, r.Unten - top - 3), d, ix, typ, id, fussnoten, faecher, true, o);
            return;
        }
        const float legende = 108;
        var rasterBreite = r.B * .485f;
        Raster(s, new Kasten(r.X, top, rasterBreite, r.Unten - top - legende - 7), d, ix, typ, id, fussnoten, faecher, false, o);
        Liste(s, new Kasten(r.X + rasterBreite + 12, top + 13, r.B - rasterBreite - 12, r.Unten - top - legende - 20), ix, typ, id, faecher);
        Legenden(s, new Kasten(r.X, r.Unten - legende, r.B, legende - 3), ix, fussnoten, faecher);
    }

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

    private static void Raster(PdfSeite s, Kasten r, Schuldatei d, DatenIndex ix, ElementTyp typ, int id, List<Fussnote> fussnoten, ISet<int> faecher, bool kompakt, StundenplanDruckOptionen o)
    {
        var zr = d.Zeitraster;
        var tagVon = Math.Clamp(o.VonTag, 0, zr.TageProWoche - 1);
        var tagBis = o.BisTag < 0 ? zr.TageProWoche - 1 : Math.Clamp(o.BisTag, tagVon, zr.TageProWoche - 1);
        var stVon = Math.Clamp(o.VonStunde, 1, zr.StundenProTag);
        var stBis = o.BisStunde <= 0 ? zr.StundenProTag : Math.Clamp(o.BisStunde, stVon, zr.StundenProTag);
        var tage = Enumerable.Range(tagVon, tagBis - tagVon + 1).ToList();
        var stunden = stBis - stVon + 1;
        var plan = ix.PlanVon(typ, id).Where(p => p.Stunde >= stVon && p.Stunde <= stBis).ToList();
        var zeilen = new List<(int Stunde, string Pause)>();
        for (var st = stVon; st <= stBis; st++)
        {
            zeilen.Add((st, ""));
            var pause = zr.Pausen.FirstOrDefault(p => p.NachStunde == st);
            if (o.PausenZeigen && pause != null && st < stBis) zeilen.Add((0, pause.Bezeichnung.Length > 0 ? pause.Bezeichnung : "Pause"));
        }
        var kopfH = kompakt ? 17f : 22f;
        const float pauseH = 10;
        var anzahlP = zeilen.Count(z => z.Stunde == 0);
        var zeileH = Math.Clamp((r.H - kopfH - anzahlP * pauseH) / Math.Max(1, stunden), 13, kompakt ? 32 : 35);
        var gNr = kompakt ? 13f : 16f;
        var nrB = Enumerable.Range(stVon, stunden).Max(st => PdfDokument.TextBreite(zr.StundenLabel(st), gNr, true));
        var zeitB = o.ZeitenZeigen
            ? Enumerable.Range(stVon, stunden).Select(zr.Zeit).Where(z => z != null).Select(z => PdfDokument.TextBreite($"{z!.Beginn:H\\:mm}-{z.Ende:H\\:mm}", 6.5f)).DefaultIfEmpty(0).Max()
            : 0;
        var stdB = MathF.Ceiling(Math.Max(kompakt ? 34 : 40, nrB + zeitB + 7));
        var tagB = (r.B - stdB) / Math.Max(1, tage.Count);
        var hoehe = kopfH + anzahlP * pauseH + stunden * zeileH;
        var y0 = r.Y;

        s.Flaeche(r.X, y0, r.B, kopfH, KopfGrau);
        for (var t = 0; t < tage.Count; t++)
            s.TextImKasten(r.X + stdB + t * tagB, y0, tagB, kopfH, kompakt ? 9f : 10.5f, Planungsdienst.Tagname(d, tage[t], true), 1, true);

        var lage = new Dictionary<int, (float Y, float H)>();
        var pausen = new List<(float Y, string Text)>();
        var y = y0 + kopfH;
        foreach (var z in zeilen)
        {
            if (z.Stunde == 0) { pausen.Add((y, z.Pause)); y += pauseH; }
            else { lage[z.Stunde] = (y, zeileH); y += zeileH; }
        }

        string Schluessel(int t, int st) => string.Join(",", plan.Where(p => p.Tag == t && p.Stunde == st).Select(p => p.UnterrichtId).OrderBy(x => x));
        for (var ti = 0; ti < tage.Count; ti++)
        {
            var t = tage[ti];
            var st = stVon;
            while (st <= stBis)
            {
                var hier = plan.Where(p => p.Tag == t && p.Stunde == st).ToList();
                var sch = Schluessel(t, st);
                var bis = st;
                while (!o.Einzelstunden && sch.Length > 0 && bis < stBis && !(o.PausenZeigen && zr.Pausen.Any(p => p.NachStunde == bis)) && Schluessel(t, bis + 1) == sch) bis++;
                var x = r.X + stdB + ti * tagB;
                var zelle = new Kasten(x, lage[st].Y, tagB, lage[bis].Y + lage[bis].H - lage[st].Y);
                if (hier.Count > 0)
                {
                    var fuell = Grau;
                    if (o.Farbig && ix.Unterrichte.TryGetValue(hier[0].UnterrichtId, out var u0) && u0.HauptfachId is int hf
                        && ix.Faecher.GetValueOrDefault(hf) is { Hintergrund: not 0 } fa)
                        fuell = Farbe.AusInt(fa.Hintergrund).Hell(0.55f);
                    s.Flaeche(zelle.X, zelle.Y, zelle.B, zelle.H, fuell);
                    Zelle(s, zelle, hier, ix, typ, id, fussnoten, faecher, o);
                }
                s.Rahmen(zelle.X, zelle.Y, zelle.B, zelle.H, 0.55f);
                st = bis + 1;
            }
        }

        foreach (var (st, (yy, hh)) in lage)
        {
            s.Flaeche(r.X, yy, stdB, hh, KopfGrau);
            s.Rahmen(r.X, yy, stdB, hh, 0.55f);
            s.Text(r.X + 2, PdfSeite.Mitte(yy, hh, gNr), gNr, zr.StundenLabel(st), true);
            if (o.ZeitenZeigen && zr.Zeit(st) is { } zeit)
                s.Text(r.X + nrB + 5, PdfSeite.Mitte(yy, hh, 6.5f) + 1, 6.5f, $"{zeit.Beginn:H\\:mm}-{zeit.Ende:H\\:mm}");
        }
        foreach (var (py, text) in pausen)
        {
            s.Flaeche(r.X + stdB, py, r.B - stdB, pauseH, Farbe.Weiss);
            s.Rahmen(r.X + stdB, py, r.B - stdB, pauseH, 0.55f);
            s.TextImKasten(r.X + stdB, py, r.B - stdB, pauseH, 8, text, 1);
            s.Flaeche(r.X + 0.3f, py, stdB - 0.6f, pauseH, Farbe.Weiss);
        }
        s.Rahmen(r.X, y0, r.B, hoehe, 1.5f);
        s.Linie(r.X, y0 + kopfH, r.Rechts, y0 + kopfH, 1.5f);
        s.Linie(r.X + stdB, y0, r.X + stdB, y0 + hoehe, 1.5f);
        for (var t = 1; t < tage.Count; t++) s.Linie(r.X + stdB + t * tagB, y0, r.X + stdB + t * tagB, y0 + kopfH, 0.55f);
    }

    /// <summary>Inhalt einer Zelle: je Unterricht eine Zeile (fett, normal, unterstrichen); Kopplungen mit „*“ und Fußnotennummer.</summary>
    private static void Zelle(PdfSeite s, Kasten z, List<Planstunde> hier, DatenIndex ix, ElementTyp typ, int id, List<Fussnote> fussnoten, ISet<int> faecher, StundenplanDruckOptionen o)
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
                var fz = FussnotenZeilen(u, p, ix);
                var nr = fussnoten.FirstOrDefault(n => n.Zeilen.SequenceEqual(fz))?.Nr;
                if (nr is null) { nr = fussnoten.Count + 1; fussnoten.Add(new Fussnote(nr.Value, fz)); }
                noten.Add(nr.Value);
            }
            foreach (var t in teile.Take(gekoppelt && teile.Count > 3 ? 3 : 99))
            {
                var fachEl = t.Zeile.FachId is int fid ? ix.Faecher.GetValueOrDefault(fid) : null;
                var fach = o.Langname && fachEl is { Langname.Length: > 0 } ? fachEl.Langname : ix.KFach(t.Zeile.FachId);
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
        var g = zeilen.Count <= 2 ? 8.5f : zeilen.Count <= 4 ? 6.5f : 5.5f;
        var zh = g * 1.15f + (zeilen.Count <= 2 ? 2 : 0.4f);
        var platz = z.H - (noten.Count > 0 ? 5 : 0);
        var max = Math.Max(1, (int)(platz / zh));
        var sichtbar = zeilen.Take(max).ToList();
        var y = z.Y + (platz - sichtbar.Count * zh) / 2;
        var leer = PdfDokument.TextBreite(" ", g);
        foreach (var (a, b, c, stern) in sichtbar)
        {
            var ta = (stern ? "*" : "") + a;
            var wa = PdfDokument.TextBreite(ta, g, true);
            var wb = b.Length > 0 ? leer + PdfDokument.TextBreite(b, g) : 0;
            var wc = c.Length > 0 ? leer + PdfDokument.TextBreite(c, g) : 0;
            // zu breit: Schrift auf die Zelle verkleinern
            var faktor = Math.Min(1f, (z.B - 3) / Math.Max(1, wa + wb + wc));
            var gg = g * faktor;
            var x = z.X + Math.Max(1.5f, (z.B - (wa + wb + wc) * faktor) / 2);
            var gl = y + zh / 2 + gg * 0.35f;
            s.Text(x, gl, gg, ta, true);
            x += wa * faktor;
            if (b.Length > 0)
            {
                if (typ == ElementTyp.Lehrer) s.Unterstrichen(x + leer * faktor, gl, gg, b); else s.Text(x + leer * faktor, gl, gg, b);
                x += wb * faktor;
            }
            if (c.Length > 0)
            {
                if (typ == ElementTyp.Klasse) s.Unterstrichen(x + leer * faktor, gl, gg, c); else s.Text(x + leer * faktor, gl, gg, c);
            }
            y += zh;
        }
        if (noten.Count > 0) s.Text(z.X + 1.5f, z.Unten - 1.5f, 5.5f, string.Join(" ", noten.Distinct().Select(n => $"{n})")));
    }

    private static List<(string Text, string Klassen)> FussnotenZeilen(Unterricht u, Planstunde p, DatenIndex ix) =>
        u.Zeilen.Select((z, i) =>
        {
            var raum = i < p.Raeume.Count && p.Raeume[i] is int r ? ix.KRaum(r) : z.FachraumId is int f ? ix.KRaum(f) : "";
            return ($"{ix.KLehrer(z.LehrerId)}, {ix.KFach(z.FachId)}{(raum.Length > 0 ? ",  " + raum : "")}", ix.KKlassen(z.KlassenIds));
        }).ToList();

    // ------------------------------------------------------------------------------------------ Unterrichtsliste rechts

    private static void Liste(PdfSeite s, Kasten r, DatenIndex ix, ElementTyp typ, int id, ISet<int> faecher)
    {
        var mitKlassen = typ != ElementTyp.Klasse;
        var spalten = mitKlassen
            ? new[] { ("U-Nr", .10f), ("Kl,Le", .10f), ("Wst", .08f), ("Lehrer", .18f), ("Fach", .17f), ("Klasse(n)", .37f) }
            : new[] { ("U-Nr", .12f), ("Kl,Le", .12f), ("Std.", .10f), ("Wst", .08f), ("Lehrer", .26f), ("Fach", .32f) };
        var xs = new float[spalten.Length + 1];
        xs[0] = r.X;
        for (var i = 0; i < spalten.Length; i++) xs[i + 1] = xs[i] + spalten[i].Item2 * r.B;
        const float g = 8.5f;
        var zh = g * 1.15f + 1.5f;
        s.Flaeche(r.X, r.Y, r.B, zh + 2, KopfGrau);
        for (var i = 0; i < spalten.Length; i++) s.Text(xs[i] + 1.5f, PdfSeite.Mitte(r.Y, zh + 2, 9), 9, spalten[i].Item1);
        var y = r.Y + zh + 3.5f;
        foreach (var u in ix.UnterrichteVon(typ, id).OrderBy(u => u.Id))
        {
            var zeilen = typ switch
            {
                ElementTyp.Lehrer => u.Zeilen.Where(z => z.LehrerId == id).ToList(),
                ElementTyp.Klasse => u.Zeilen.Where(z => z.KlassenIds.Contains(id)).ToList(),
                _ => u.Zeilen.ToList(),
            };
            if (zeilen.Count == 0) zeilen = u.Zeilen.Take(1).ToList();
            var hoehe = zeilen.Count * zh;
            if (y + hoehe > r.Unten) { s.Text(r.X + 1.5f, y + g, g, "…"); break; }
            foreach (var z in zeilen) if (z.FachId is int fa) faecher.Add(fa);
            var klLe = u.Zeilen.Count > 1 ? $"{u.KlassenIds.Count()}, {u.LehrerIds.Count()}" : "";
            var nvpl = ix.NichtVerplant(u);
            for (var i = 0; i < zeilen.Count; i++)
            {
                var z = zeilen[i];
                var w = mitKlassen
                    ? new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 ? u.Wochenstunden.ToString(De) : "", ix.KLehrer(z.LehrerId), ix.KFach(z.FachId), ix.KKlassen(z.KlassenIds) }
                    : new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 && nvpl > 0 ? nvpl.ToString(De) : "", i == 0 ? u.Wochenstunden.ToString(De) : "", ix.KLehrer(z.LehrerId), ix.KFach(z.FachId) };
                for (var c = 0; c < w.Length; c++) s.TextImKasten(xs[c], y + i * zh, xs[c + 1] - xs[c], zh, g, w[c], 0, false, null, 1.5f);
            }
            y += hoehe;
            s.Linie(r.X, y, r.Rechts, y, 0.55f, Farbe.Grau(200 / 255f));
            y += 0.7f;
        }
    }

    // ------------------------------------------------------------------------------------------ Legenden unten

    private static void Legenden(PdfSeite s, Kasten r, DatenIndex ix, List<Fussnote> fussnoten, ISet<int> faecher)
    {
        const float g = 8.5f;
        var zh = g * 1.15f + 0.7f;
        var breiteNote = r.B * .25f;
        var noteZeilen = fussnoten.SelectMany(n => n.Zeilen.Select((z, i) => (Nr: i == 0 ? $"{n.Nr})" : "", z.Text, z.Klassen))).ToList();
        var proSpalte = Math.Max(1, (int)((r.H - zh - 3) / zh));
        for (var sp = 0; sp < 2; sp++)
        {
            var sx = r.X + sp * breiteNote;
            if (sp == 0 || noteZeilen.Count > proSpalte)
            {
                s.Flaeche(sx, r.Y, breiteNote - 7, zh, KopfGrau);
                s.Text(sx, PdfSeite.Mitte(r.Y, zh, g), g, "Nr. Le.,Fa.,Rm.");
                s.Text(sx + breiteNote - 33, PdfSeite.Mitte(r.Y, zh, g), g, "Kla.");
                s.Linie(sx, r.Y + zh, sx + breiteNote - 7, r.Y + zh, 0.55f);
            }
            var teil = noteZeilen.Skip(sp * proSpalte).Take(proSpalte).ToList();
            for (var i = 0; i < teil.Count; i++)
            {
                var yy = r.Y + zh + 1.5f + i * zh;
                s.Text(sx, PdfSeite.Mitte(yy, zh, g), g, teil[i].Nr);
                s.TextImKasten(sx + 16, yy, breiteNote - 58, zh, g, teil[i].Text, 0, false, null, 0);
                s.TextImKasten(sx + breiteNote - 33, yy, 29, zh, g, teil[i].Klassen, 0, false, null, 0);
            }
        }
        var fx = r.X + 2 * breiteNote;
        var fb = r.Rechts - fx;
        var liste = faecher.Select(id => ix.Faecher.GetValueOrDefault(id)).Where(fa => fa != null).ToList();
        s.Text(fx, PdfSeite.Mitte(r.Y, zh, g), g, "Fächer", true);
        var proSp = Math.Max(1, (int)((r.H - 2 * zh - 3) / zh));
        for (var sp = 0; sp < 2; sp++)
        {
            var sx = fx + sp * fb / 2;
            s.Text(sx, PdfSeite.Mitte(r.Y + zh, zh, g), g, "Name");
            s.Text(sx + 43, PdfSeite.Mitte(r.Y + zh, zh, g), g, "Langname");
            s.Linie(sx, r.Y + 2 * zh, sx + fb / 2 - 7, r.Y + 2 * zh, 0.55f);
            var teil = liste.Skip(sp * proSp).Take(proSp).ToList();
            for (var i = 0; i < teil.Count; i++)
            {
                var yy = r.Y + 2 * zh + 1.5f + i * zh;
                s.TextImKasten(sx, yy, 42, zh, g, teil[i]!.Kurzname, 0, false, null, 0);
                s.TextImKasten(sx + 43, yy, fb / 2 - 52, zh, g, teil[i]!.Langname, 0, false, null, 0);
            }
        }
    }
}
