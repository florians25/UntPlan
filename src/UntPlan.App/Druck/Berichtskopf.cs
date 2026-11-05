using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Druck;

/// <summary>Kopf- und Fußzeile der Berichte laut „Berichts-Einstellungen“ (gilt für alle Berichte).</summary>
internal static class Berichtskopf
{
    private static Berichtseinstellungen B => AppZustand.Einstellungen.Berichte;

    /// <summary>Kopf zeichnen: Überschrift (zwei Zeilen) links, Schuljahr in der Mitte, Programm und Druckzeitpunkt rechts. Liefert die Höhe.</summary>
    public static float Zeichnen(Graphics g, Rectangle r, Font fett, Font normal)
    {
        var d = AppZustand.D;
        var b = B;
        var y = (float)r.Top;
        var zeile1 = b.Ueberschrift1.Length > 0 ? b.Ueberschrift1 : d.Einstellungen.Schulname.ToUpperInvariant();
        var zeile2 = b.Ueberschrift2.Length > 0 ? b.Ueberschrift2 : d.Einstellungen.Adresse;
        var hk = fett.GetHeight(g);
        g.DrawString(zeile1, fett, Brushes.Black, r.Left, y);
        var sj = $"Schuljahr {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrBis.Year}";
        var sjBreite = g.MeasureString(sj, normal).Width;
        var sjX = Math.Max(r.Left + r.Width / 2f - sjBreite / 2, r.Left + g.MeasureString(zeile1, fett).Width + 24);
        g.DrawString(sj, normal, Brushes.Black, sjX, y + 1);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"UntPlan {DateTime.Now.Year}", fett, Brushes.Black, new RectangleF(r.Left, y, r.Width, hk), rechts);
        y += hk + 2;
        g.DrawString(zeile2, normal, Brushes.Black, r.Left, y);
        var zeitpunkt = string.Join("  ", new[]
        {
            b.VersionDrucken ? $"Version {b.Version}" : "",
            b.DatumDrucken ? DateTime.Now.ToString("d.M.yyyy", CultureInfo.GetCultureInfo("de-DE")) : "",
            b.UhrzeitDrucken ? DateTime.Now.ToString("HH:mm") : "",
        }.Where(t => t.Length > 0));
        g.DrawString(zeitpunkt, normal, Brushes.Black, new RectangleF(r.Left, y, r.Width, hk), rechts);
        y += normal.GetHeight(g) + 1;
        if (b.LinieKopf) g.DrawLine(Pens.Black, r.Left, y, r.Right, y);
        return y - r.Top;
    }

    /// <summary>Text der Fußzeile (Text, Dateiname, Datum, Schulwoche, Kalenderwoche, Wochennummer, Periode).</summary>
    public static string FussText(DateOnly datum)
    {
        var d = AppZustand.D;
        var b = B;
        var teile = new List<string> { b.Fusszeile.Length > 0 ? b.Fusszeile : "Vertretungsplanung mit UntPlan" };
        if (b.FussDateiname && AppZustand.Doc.Pfad is { } pfad) teile.Add(Path.GetFileName(pfad));
        if (b.FussDatum) teile.Add(Format.Datum(datum));
        if (b.FussSchulwoche) teile.Add($"Schulwoche {Kalender.Schulwoche(d, datum)}");
        if (b.FussKalenderwoche) teile.Add($"KW {ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue))}");
        if (b.FussWochennummer) teile.Add($"Woche {Kalender.WochenTyp(d, datum)}");
        if (b.FussPeriode) teile.Add(d.PeriodeAm(datum).Name);
        return string.Join("  ·  ", teile.Where(t => t.Length > 0));
    }

    /// <summary>Fußzeile unter dem Druckbereich: Linie, Text links, Seiten-Nummer (und Anzahl Seiten) rechts.</summary>
    public static void FussZeichnen(Graphics g, Rectangle r, Font schrift, DateOnly datum, int seite, int seiten)
    {
        var b = B;
        var y = r.Bottom + 6f;
        if (b.LinieFuss) using (var p = new Pen(Color.Gray)) g.DrawLine(p, r.Left, y, r.Right, y);
        y += 2;
        using var mitte = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(FussText(datum), schrift, Brushes.DimGray, new RectangleF(r.Left, y, r.Width, 20), mitte);
        if (!b.SeitenNummer) return;
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(b.AnzahlSeiten ? $"Seite {seite}/{seiten}" : $"Seite {seite}", schrift, Brushes.DimGray, new RectangleF(r.Left, y, r.Width, 20), rechts);
    }
}
