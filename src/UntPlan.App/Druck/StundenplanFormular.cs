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
/// <summary>
/// Druckoptionen aus dem Seitenlayout der Stundenpläne (Vorbild: Unterricht, Kopplungslegende (Fußnoten), Abkürzungen,
/// Element-Text, Schriftgrößen in % für Überschrift und Kopp.-Legende).
/// </summary>
public sealed class StundenplanDruckOptionen
{
    public bool Unterricht { get; set; } = true;
    public bool Kopplungslegende { get; set; } = true;
    public bool Abkuerzungen { get; set; } = true;
    public bool ElementText { get; set; }
    public int UeberschriftProzent { get; set; } = 100;
    public int LegendeProzent { get; set; } = 100;
    /// <summary>Druckdetails des Formats (Pläne pro Seite, Zusatzinformationen, Hintergrund-Bild, Überschrift-Felder).</summary>
    public StundenplanDruckdetails Details { get; set; } = new();
    /// <summary>Layout 2 „1 Überschrift je Blatt“.</summary>
    public bool EineUeberschriftJeBlatt { get; set; }
}

/// <summary>Ein Plan einer Druckseite: Element und (bei „Für jede Woche ein Stundenplan“) die Schulwoche.</summary>
public readonly record struct DruckPlan(int ElementId, int? Woche);

internal static class StundenplanFormular
{
    private const string Schrift = "Arial";

    /// <summary>Optionen des aktuellen Drucks (vom Seitenlayout bzw. aus dem Format gesetzt).</summary>
    internal static StundenplanDruckOptionen Optionen { get; set; } = new();
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Eine Druckseite mit einem (oder im kompakten Format mehreren) Plänen.</summary>
    public static void Seite(Graphics g, Rectangle r, ElementTyp typ, IReadOnlyList<int> ids, bool kompakt, int seite, int seiten) =>
        Seite(g, r, typ, ids.Select(i => new DruckPlan(i, null)).ToList(), kompakt ? 0 : 1, seite, seiten);

    /// <summary>
    /// Eine Druckseite: Pläne im Raster „Anzahl Pläne nebeneinander × übereinander“ (Seitenlayout | Einstellungen); bei
    /// <paramref name="spalten"/> = 0 alle Pläne kompakt nebeneinander. Titelleiste je Plan bzw. bei „1 Überschrift je Blatt“ nur einmal;
    /// Hintergrund-Bild laut Seitenlayout.
    /// </summary>
    public static void Seite(Graphics g, Rectangle r, ElementTyp typ, IReadOnlyList<DruckPlan> plaene, int spalten, int seite, int seiten)
    {
        var d = AppZustand.D;
        var ix = new DatenIndex(d);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        Hintergrundbild(g, r);
        var det = Optionen.Details;
        var nx = spalten <= 0 ? Math.Max(1, plaene.Count) : Math.Clamp(det.PlaeneNebeneinander, 1, 6);
        var ny = spalten <= 0 ? 1 : Math.Clamp(det.PlaeneUebereinander, 1, 6);
        var raster = nx * ny > 1;
        var eineUeberschrift = !raster || spalten <= 0 || Optionen.EineUeberschriftJeBlatt;
        var oben = eineUeberschrift ? Kopf(g, r, d) : r.Top;
        var unten = r.Bottom - 18;
        Fuss(g, r, d, seite, seiten);
        const int abstand = 24;
        var zellB = (r.Width - (nx - 1) * abstand) / nx;
        var zellH = (unten - oben - (ny - 1) * 14) / ny;
        for (var i = 0; i < plaene.Count && i < nx * ny; i++)
        {
            var zelle = new Rectangle(r.Left + i % nx * (zellB + abstand), oben + i / nx * (zellH + 14), zellB, zellH);
            if (!eineUeberschrift)
            {
                var y = Kopf(g, zelle, d, klein: true);
                zelle = new Rectangle(zelle.Left, y, zelle.Width, zelle.Bottom - y);
            }
            var (pd, pix) = plaene[i].Woche is int w ? WochenDaten(d, w) : (d, ix);
            PlanMitKlassenlehrer(g, zelle, pd, pix, typ, plaene[i], raster || plaene.Count > 1);
        }
    }

    /// <summary>Daten der Schulwoche <paramref name="woche"/> (Für jede Woche ein Stundenplan): nur der in dieser Woche gültige Unterricht.</summary>
    private static (Schuldatei, DatenIndex) WochenDaten(Schuldatei d, int woche)
    {
        var montag = Kalender.Montag(d.Einstellungen.SchuljahrVon).AddDays(7 * Math.Max(0, woche - 1));
        var w = Core.Optimierung.Diagnose.FuerWoche(d, montag);
        return (w, new DatenIndex(w));
    }

    /// <summary>
    /// Plan des Elements; mit „Stundenplan Klasse des Klassenlehrers“ (Lehrerplan) bzw. „Stundenplan Klassenlehrer“ (Klassenplan) steht
    /// rechts daneben der Plan der Klasse bzw. der Klassenleitung.
    /// </summary>
    private static void PlanMitKlassenlehrer(Graphics g, Rectangle r, Schuldatei d, DatenIndex ix, ElementTyp typ, DruckPlan p, bool kompakt)
    {
        int? partner = null;
        var partnerTyp = typ == ElementTyp.Lehrer ? ElementTyp.Klasse : ElementTyp.Lehrer;
        if (Optionen.Details.StundenplanKlassenlehrer)
        {
            if (typ == ElementTyp.Lehrer) partner = d.Klassen.Where(k => !k.Ignorieren && k.KlassenlehrerIds.Contains(p.ElementId)).OrderBy(k => k.Sortierung).FirstOrDefault()?.Id;
            else if (typ == ElementTyp.Klasse && ix.Klassen.TryGetValue(p.ElementId, out var kl)) partner = kl.KlassenlehrerIds.Where(ix.Lehrer.ContainsKey).Select(x => (int?)x).FirstOrDefault();
        }
        if (partner is not int pid)
        {
            Plan(g, r, d, ix, typ, p.ElementId, kompakt, p.Woche);
            return;
        }
        var b = (r.Width - 24) / 2;
        Plan(g, new Rectangle(r.Left, r.Top, b, r.Height), d, ix, typ, p.ElementId, true, p.Woche);
        Plan(g, new Rectangle(r.Left + b + 24, r.Top, b, r.Height), d, ix, partnerTyp, pid, true, p.Woche);
    }

    /// <summary>Hintergrund-Bild des Seitenlayouts (Schullogo bzw. Hintergrund) an der eingestellten Stelle.</summary>
    private static void Hintergrundbild(Graphics g, Rectangle r)
    {
        var det = Optionen.Details;
        if (string.IsNullOrWhiteSpace(det.Hintergrundbild) || !File.Exists(det.Hintergrundbild)) return;
        try
        {
            using var bild = Image.FromFile(det.Hintergrundbild);
            g.DrawImage(bild, BildRechteck(r, bild.Size, det));
        }
        catch (Exception ex) when (ex is OutOfMemoryException or IOException or ArgumentException) { /* kein gültiges Bild */ }
    }

    /// <summary>Lage des Hintergrund-Bildes auf der Seite (Breite laut Details, Seitenverhältnis des Bildes).</summary>
    internal static Rectangle BildRechteck(Rectangle r, Size bild, StundenplanDruckdetails det)
    {
        var b = (int)(r.Width * Math.Clamp(det.BildBreite, 0.02f, 1f));
        var h = bild.Width > 0 ? b * bild.Height / bild.Width : b;
        var x = r.Left + (int)(r.Width * Math.Clamp(det.BildX, 0f, 1f));
        var y = r.Top + (int)(r.Height * Math.Clamp(det.BildY, 0f, 1f));
        return new Rectangle(Math.Max(r.Left, Math.Min(x, r.Right - b)), Math.Max(r.Top, Math.Min(y, r.Bottom - h)), b, h);
    }

    // ------------------------------------------------------------------------------------------ Kopf und Fuß

    /// <summary>
    /// Titelleiste (Einstellungen | Berichte | Überschriften): links Schulname (Lizenztext, immer), daneben die Überschrift (Zeile 1/2,
    /// Standard „Stundenplan Schuljahr“ / „Gilt ab“), rechts Programm, Stundenplan-Version, Datum und Uhrzeit; darunter die waagrechte Linie.
    /// </summary>
    private static int Kopf(Graphics g, Rectangle r, Schuldatei d, bool klein = false)
    {
        var hoehe = klein ? 30 : 36;
        var faktor = klein ? 0.85f : 1f;
        using var fett = new Font(Schrift, 9.5f * faktor, FontStyle.Bold);
        using var normal = new Font(Schrift, 9f * faktor);
        var e = d.Einstellungen;
        var b = e.Berichte;
        var zz = klein ? 12 : 15;
        g.DrawString(e.Schulname, fett, Brushes.Black, r.Left, r.Top);
        var zweite = e.Schulname2.Length > 0 ? e.Schulname2 : e.Adresse;
        if (zweite.Length > 0) g.DrawString(zweite, normal, Brushes.Black, r.Left, r.Top + zz);
        var x2 = r.Left + Math.Max(klein ? 110 : 160, (int)Math.Max(g.MeasureString(e.Schulname, fett).Width, zweite.Length > 0 ? g.MeasureString(zweite, normal).Width : 0) + 28);
        var z1 = b.Ueberschrift1.Trim().Length > 0 ? b.Ueberschrift1.Trim() : $"Stundenplan {e.SchuljahrVon.Year}/{e.SchuljahrVon.Year + 1}";
        var z2 = b.Ueberschrift2.Trim().Length > 0 ? b.Ueberschrift2.Trim() : $"Gilt ab: {d.AktivePeriode.Von.ToString("d. MMMM", De)}";
        g.DrawString(z1, normal, Brushes.Black, x2, r.Top + 1);
        g.DrawString(z2, normal, Brushes.Black, x2, r.Top + zz);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        var titel = $"UntPlan {e.SchuljahrVon.Year}" + (b.VersionDrucken ? $"  Version {e.StundenplanVersion}" : "");
        g.DrawString(titel, fett, Brushes.Black, new RectangleF(r.Left, r.Top, r.Width, 16), rechts);
        var zeit = string.Join("  ", new[] { b.DatumDrucken ? DateTime.Now.ToString("dd.MM.yyyy", De) : "", b.UhrzeitDrucken ? DateTime.Now.ToString("HH:mm", De) : "" }.Where(t => t.Length > 0));
        if (zeit.Length > 0) g.DrawString(zeit, normal, Brushes.Black, new RectangleF(r.Left, r.Top + zz, r.Width, 16), rechts);
        if (b.WaagrechteLinie)
        {
            using var stift = new Pen(Color.Black, 1f);
            g.DrawLine(stift, r.Left, r.Top + hoehe - 5, r.Right, r.Top + hoehe - 5);
        }
        return r.Top + hoehe;
    }

    /// <summary>
    /// Fußzeile (Einstellungen | Berichte | Überschriften): Periode, Dateiname, Datum, Schulwoche, Kalenderwoche, Wochennummer und Text
    /// (Standard „UntPlan“), waagrechte Linie; rechts „Seite x / y“ laut „Seiten-Nummer drucken“ / „Anzahl Seiten drucken“.
    /// </summary>
    private static void Fuss(Graphics g, Rectangle r, Schuldatei d, int seite, int seiten)
    {
        var b = d.Einstellungen.Berichte;
        using var f = new Font(Schrift, 8f);
        using var stift = new Pen(Color.Black, 1f);
        var y = r.Bottom - 14;
        if (b.FussLinie) g.DrawLine(stift, r.Left, y - 2, r.Right, y - 2);
        var p = d.AktivePeriode;
        var datum = AppZustand.Datum;
        var teile = new List<string>();
        if (b.FussPeriode) teile.Add($"{(p.Name.Length > 0 ? p.Name : "Periode")}  {Format.Datum(p.Von)} - {(p.Bis is DateOnly pb ? Format.Datum(pb) : Format.Datum(d.Einstellungen.SchuljahrBis))}");
        if (b.FussDateiname && AppZustand.Doc.Pfad is string pfad) teile.Add(Path.GetFileName(pfad));
        if (b.FussDatum) teile.Add(Format.Datum(datum));
        if (b.FussSchulwoche) teile.Add($"Schulwoche {Kalender.Schulwoche(d, datum)}");
        if (b.FussKalenderwoche) teile.Add($"KW {ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue))}");
        if (b.FussWochennummer) teile.Add($"{Kalender.WochenTyp(d, datum)}-Woche");
        teile.Add(b.Fusszeile.Trim().Length > 0 ? b.Fusszeile.Trim() : "UntPlan");
        using var mitte = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(string.Join("     ", teile), f, Brushes.Black, new RectangleF(r.Left, y, r.Width, 14), mitte);
        if (!b.SeitenNummer) return;
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(b.AnzahlSeiten ? $"Seite {seite} / {seiten}" : $"Seite {seite}", f, Brushes.Black, new RectangleF(r.Left, y, r.Width, 14), rechts);
    }

    /// <summary>Druckname einer Lehrkraft (Einstellungen | Berichte | Drucknamen).</summary>
    private static string Druckname(DatenIndex ix, int? lehrerId) =>
        lehrerId is int l && ix.Lehrer.TryGetValue(l, out var le) ? Drucknamen.Lehrer(le, AppZustand.D.Einstellungen.Berichte.LehrerDruckname) : ix.KLehrer(lehrerId);

    /// <summary>Text eines Überschrift-Feldes (Überschrift-Editor des Seitenlayouts), gekürzt auf „Stellen maximal“.</summary>
    internal static string UeberschriftText(UeberschriftFeld f, Schuldatei d, DatenIndex ix, Element? el, int? woche)
    {
        var t = f.Art switch
        {
            UeberschriftFeldArt.Kurzname => el?.Kurzname ?? "",
            UeberschriftFeldArt.Langname => el is Lehrer l ? l.VollerName : el?.Langname ?? "",
            UeberschriftFeldArt.Text => el?.Text ?? "",
            UeberschriftFeldArt.Beschreibung => el?.Beschreibung ?? "",
            UeberschriftFeldArt.Abteilung => el?.AbteilungId is int a ? ix.K(ElementTyp.Abteilung, a) : "",
            UeberschriftFeldArt.Klassenlehrer => el is Klasse k ? string.Join(", ", k.KlassenlehrerIds.Select(x => Druckname(ix, x))) : "",
            UeberschriftFeldArt.Stammraum => el switch { Klasse k => ix.KRaum(k.StammraumId), Lehrer l => ix.KRaum(l.StammraumId), _ => "" },
            UeberschriftFeldArt.Zeitbereich => woche is int w ? $"Schulwoche {w}" : $"{Format.Datum(d.Einstellungen.SchuljahrVon)} - {Format.Datum(d.Einstellungen.SchuljahrBis)}",
            UeberschriftFeldArt.PeriodeKurz => d.AktivePeriode.Name,
            UeberschriftFeldArt.PeriodeLang => d.AktivePeriode.Langname.Length > 0 ? d.AktivePeriode.Langname : d.AktivePeriode.Name,
            _ => f.Text,
        };
        return f.StellenMax > 0 && t.Length > f.StellenMax ? t[..f.StellenMax] : t;
    }

    // ------------------------------------------------------------------------------------------ ein Plan

    /// <summary>Teil eines Unterrichts, der zum Element gehört (Kopplungszeile mit verplantem Raum).</summary>
    private sealed record Teil(Kopplungszeile Zeile, int? Raum);
    private sealed record Fussnote(int Nr, List<(string Text, string Klassen)> Zeilen);

    private static void Plan(Graphics g, Rectangle r, Schuldatei d, DatenIndex ix, ElementTyp typ, int id, bool kompakt, int? woche = null)
    {
        var el = ix.Element(typ, id);
        var ueb = Math.Clamp(Optionen.UeberschriftProzent, 50, 300) / 100f;
        int top;
        var felder = Optionen.Details.Ueberschrift;
        if (felder.Count > 0) top = r.Top + UeberschriftZeichnen(g, r, d, ix, el, woche, felder, ueb * (kompakt ? 0.85f : 1f)) + 4;
        else
        {
            using var fName = new Font(Schrift, (kompakt ? 15f : 18f) * ueb);
            using var fLang = new Font(Schrift, (kompakt ? 11f : 13f) * ueb);
            var kurz = el?.Kurzname ?? "";
            var lang = (el is Lehrer l ? l.VollerName : el?.Langname ?? "") + (woche is int wo ? $"  (Schulwoche {wo})" : "");
            g.DrawString(kurz, fName, Brushes.Black, r.Left - 3, r.Top);
            var kb = g.MeasureString(kurz, fName);
            if (lang.Length > 0 && lang != kurz) g.DrawString(lang, fLang, Brushes.Black, r.Left + kb.Width + 2, r.Top + kb.Height - g.MeasureString(lang, fLang).Height - 2);
            top = r.Top + (int)kb.Height + 4;
        }
        // Seitenlayout „Element-Text“: Text und Beschreibung des Elements unter der Überschrift
        if (Optionen.ElementText && el is not null && string.Join("  ", new[] { el.Text, el.Beschreibung }.Where(t => !string.IsNullOrWhiteSpace(t))) is { Length: > 0 } et)
        {
            using var fText = new Font(Schrift, 9f);
            g.DrawString(et, fText, Brushes.Black, r.Left, top - 2);
            top += (int)fText.GetHeight(g) + 2;
        }

        var fussnoten = new List<Fussnote>();
        var faecher = new SortedSet<int>(Comparer<int>.Create((a, b) => string.Compare(ix.KFach(a), ix.KFach(b), StringComparison.CurrentCultureIgnoreCase) is var c && c != 0 ? c : a.CompareTo(b)));
        var zusatz = Optionen.Details.ZusatzKlassenLehrer && typ is ElementTyp.Klasse or ElementTyp.Lehrer;
        if (kompakt)
        {
            var zusatzH = zusatz ? Math.Min(120, (r.Bottom - top) / 4) : 0;
            Raster(g, new Rectangle(r.Left, top, r.Width, r.Bottom - top - 4 - zusatzH), d, ix, typ, id, fussnoten, faecher, true);
            if (zusatz) Zusatzinformation(g, new Rectangle(r.Left, r.Bottom - zusatzH, r.Width, zusatzH - 2), ix, typ, id);
            return;
        }
        var mitLegende = Optionen.Kopplungslegende || Optionen.Abkuerzungen || zusatz;
        var legende = mitLegende ? 150 : 0;
        var rasterBreite = Optionen.Unterricht ? (int)(r.Width * .485) : r.Width;
        Raster(g, new Rectangle(r.Left, top, rasterBreite, r.Bottom - top - legende - 10), d, ix, typ, id, fussnoten, faecher, false);
        if (Optionen.Unterricht) Liste(g, new Rectangle(r.Left + rasterBreite + 16, top + 18, r.Width - rasterBreite - 16, r.Bottom - top - legende - 28), ix, typ, id, faecher);
        else foreach (var u in ix.UnterrichteVon(typ, id)) foreach (var z in u.Zeilen) if (z.FachId is int fa) faecher.Add(fa);
        var zusatzB = zusatz ? Math.Min(220, r.Width / 4) : 0;
        if (zusatz) Zusatzinformation(g, new Rectangle(r.Right - zusatzB, r.Bottom - legende, zusatzB, legende - 4), ix, typ, id);
        if (Optionen.Kopplungslegende || Optionen.Abkuerzungen) Legenden(g, new Rectangle(r.Left, r.Bottom - legende, r.Width - zusatzB - (zusatz ? 12 : 0), legende - 4), ix, fussnoten, faecher);
    }

    /// <summary>Überschrift aus den Feldern des Überschrift-Editors (nebeneinander bzw. mit „neue Zeile“ untereinander). Ergebnis: Höhe.</summary>
    private static int UeberschriftZeichnen(Graphics g, Rectangle r, Schuldatei d, DatenIndex ix, Element? el, int? woche, List<UeberschriftFeld> felder, float faktor)
    {
        float x = r.Left - 2, y = r.Top, zeileH = 0;
        foreach (var f in felder)
        {
            var text = UeberschriftText(f, d, ix, el, woche);
            using var font = new Font(Schrift, Math.Max(5f, 12f * Math.Clamp(f.SchriftProzent, 50, 500) / 100f * faktor), (f.Fett ? FontStyle.Bold : 0) | (f.Kursiv ? FontStyle.Italic : 0));
            var gr = g.MeasureString(text.Length > 0 ? text : " ", font);
            if (f.NeueZeile && x > r.Left) { x = r.Left - 2; y += zeileH; zeileH = 0; }
            if (text.Length > 0) g.DrawString(text, font, Brushes.Black, x, y);
            x += gr.Width + 4;
            zeileH = Math.Max(zeileH, gr.Height);
        }
        return (int)(y + zeileH - r.Top);
    }

    /// <summary>
    /// Zusatzinformation „Lehrer der Klasse“ (Klassenplan) bzw. „Klassen des Lehrers“ (Lehrerplan): die Lehrkräfte bzw. Klassen mit
    /// ihren Fächern.
    /// </summary>
    private static void Zusatzinformation(Graphics g, Rectangle r, DatenIndex ix, ElementTyp typ, int id)
    {
        if (r.Height < 20) return;
        var gr = 8f * Math.Clamp(Optionen.LegendeProzent, 50, 300) / 100f;
        using var f = new Font(Schrift, gr);
        using var kopf = new Font(Schrift, gr, FontStyle.Underline);
        var paare = new List<(string Name, string Faecher)>();
        foreach (var u in ix.UnterrichteVon(typ, id))
            foreach (var z in u.Zeilen.Where(z => typ == ElementTyp.Klasse ? z.KlassenIds.Contains(id) : z.LehrerId == id))
            {
                var namen = typ == ElementTyp.Klasse ? new[] { Druckname(ix, z.LehrerId) } : z.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)).ToArray();
                foreach (var n in namen.Where(n => n.Length > 0)) paare.Add((n, ix.KFach(z.FachId)));
            }
        var gruppen = paare.GroupBy(p => p.Name).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x.Key, string.Join(", ", x.Select(p => p.Faecher).Where(t => t.Length > 0).Distinct()))).ToList();
        var zeileH = f.GetHeight(g) + 1;
        g.DrawString(typ == ElementTyp.Klasse ? "Lehrer" : "Klassen", kopf, Brushes.Black, r.Left, r.Top);
        g.DrawString("Fächer", kopf, Brushes.Black, r.Left + r.Width * .45f, r.Top);
        var y = r.Top + zeileH + 2;
        using var nw = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        foreach (var (name, faecher) in gruppen)
        {
            if (y + zeileH > r.Bottom) { g.DrawString("…", f, Brushes.Black, r.Left, y); break; }
            g.DrawString(name, f, Brushes.Black, new RectangleF(r.Left, y, r.Width * .45f - 2, zeileH), nw);
            g.DrawString(faecher, f, Brushes.Black, new RectangleF(r.Left + r.Width * .45f, y, r.Width * .55f, zeileH), nw);
            y += zeileH;
        }
    }

    /// <summary>Teile eines Unterrichts in einer Planstunde, die das Element betreffen.</summary>
    private static List<Teil> Teile(Unterricht u, Planstunde p, ElementTyp typ, int id)
    {
        var res = new List<Teil>();
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            var raum = p.RaumFuerZeile(i);
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
            var raum = p.RaumFuerZeile(i) is int r ? ix.KRaum(r) : z.FachraumId is int f ? ix.KRaum(f) : "";
            return ($"{Druckname(ix, z.LehrerId)}, {ix.KFach(z.FachId)}{(raum.Length > 0 ? ",  " + raum : "")}", ix.KKlassen(z.KlassenIds));
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
                ? new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 ? u.Wochenstunden.ToString(De) : "", Druckname(ix, z.LehrerId), ix.KFach(z.FachId), ix.KKlassen(z.KlassenIds) }
                : new[] { i == 0 ? u.Id.ToString(De) : "", i == 0 ? klLe : "", i == 0 && nvpl > 0 ? nvpl.ToString(De) : "", i == 0 ? u.Wochenstunden.ToString(De) : "", Druckname(ix, z.LehrerId), ix.KFach(z.FachId) };
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
        var gr = 8.5f * Math.Clamp(Optionen.LegendeProzent, 50, 300) / 100f;
        using var kopf = new Font(Schrift, gr);
        using var f = new Font(Schrift, gr);
        using var fett = new Font(Schrift, gr, FontStyle.Bold);
        using var grau = new SolidBrush(Color.FromArgb(232, 232, 232));
        using var stift = new Pen(Color.Black, .75f);
        var zeileH = f.GetHeight(g) + 1;
        var x = (float)r.Left;
        // nur Abkürzungen: Fächerlegende über die ganze Breite; nur Kopplungslegende: Fußnoten über die ganze Breite
        var breiteNote = !Optionen.Abkuerzungen ? r.Width * .5f : !Optionen.Kopplungslegende ? 0 : r.Width * .25f;

        // Fußnoten in zwei Spalten
        var noteZeilen = fussnoten.SelectMany(n => n.Zeilen.Select((z, i) => (Nr: i == 0 ? $"{n.Nr})" : "", z.Text, z.Klassen))).ToList();
        var proSpalte = Math.Max(1, (int)((r.Height - zeileH - 4) / zeileH));
        for (var sp = 0; sp < 2 && Optionen.Kopplungslegende; sp++)
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
        if (!Optionen.Abkuerzungen) return;
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
