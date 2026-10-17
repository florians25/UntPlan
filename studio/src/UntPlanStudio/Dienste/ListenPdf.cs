using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using Farbe = UntPlanStudio.Dienste.PdfSeite.Farbe;

namespace UntPlanStudio.Dienste;

/// <summary>Ein Bericht als Tabelle (Druck-Modul): Titel, Spalten und Zeilen als Text; optional Zeilenarten „gruppe“ und „summe“.</summary>
public sealed record Bericht(string Titel, string Untertitel, IReadOnlyList<string> Spalten, IReadOnlyList<IReadOnlyList<string>> Zeilen, bool Quer = false,
    IReadOnlyList<string>? Arten = null);

/// <summary>
/// Listen- und Übersichtsdrucke als PDF in Schwarz-Weiß: Berichte als Tabelle mit umrandetem Kopf (auf jeder Seite wiederholt),
/// rechtsbündigen Zahlenspalten, feinen Zeilenlinien und „Seite x von y“; Übersichtspläne (Elemente × Tage/Stunden) im Querformat.
/// </summary>
public static class ListenPdf
{
    private const float Rand = 34;
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static float Kopfzeile(PdfSeite s, Schuldatei d, string titel, int seite)
    {
        var y = Rand;
        s.Text(Rand, y + 10, 10.5f, d.Einstellungen.Schulname, true);
        s.TextRechts(s.Breite - Rand, y + 10, 8, $"Erstellt am {DateTime.Now:dd.MM.yyyy} um {DateTime.Now:HH:mm} Uhr");
        s.TextRechts(s.Breite - Rand, y + 20, 8, PeriodeText(d));
        y += 26;
        s.Linie(Rand, y, s.Breite - Rand, y, 0.8f);
        y += seite == 1 ? 20 : 14;
        return y;
    }

    private static void Fusszeile(PdfSeite s, string titel, int seite)
    {
        var y = s.Hoehe - Rand + 8;
        s.Linie(Rand, y - 10, s.Breite - Rand, y - 10, 0.5f);
        s.Text(Rand, y, 7.5f, PdfDokument.Kuerzen(titel, s.Breite * .5f, 7.5f));
        s.TextRechts(s.Breite - Rand, y, 7.5f, $"Seite {seite} von {PdfDokument.Seitenzahl}");
        s.TextMittig(0, s.Breite, y, 7.5f, "UntPlan Studio");
    }

    public static byte[] Bericht(Schuldatei d, Bericht b)
    {
        var pdf = new PdfDokument { Titel = b.Titel };
        string Art(int r) => b.Arten != null && r < b.Arten.Count ? b.Arten[r] : "";
        const float g = 8.5f, zh = 15f;
        var probe = pdf.NeueSeite(b.Quer);
        var nutz = probe.Breite - 2 * Rand;
        var w = b.Spalten.Select((sp, i) => Math.Max(PdfDokument.TextBreite(sp, 8, true), b.Zeilen.Where((z, r) => Art(r) != "gruppe")
            .Select(z => i < z.Count ? Math.Min(PdfDokument.TextBreite(z[i], g), nutz * .45f) : 0).DefaultIfEmpty(0).Max()) + 10).ToArray();
        var faktor = nutz / Math.Max(1, w.Sum());
        for (var i = 0; i < w.Length; i++) w[i] *= faktor;
        var daten = b.Zeilen.Where((z, r) => Art(r) is "" or "zeile").ToList();
        var zahl = b.Spalten.Select((sp, i) => daten.Count > 0 && daten.All(z => i >= z.Count || z[i].Length == 0
            || double.TryParse(z[i].Replace("%", "").Trim(), NumberStyles.Number, De, out _)) && daten.Any(z => i < z.Count && z[i].Length > 0)).ToArray();

        var seite = 1;
        var s = probe;
        float y = 0;
        void Kopf()
        {
            y = Kopfzeile(s, d, b.Titel, seite);
            if (seite == 1)
            {
                s.Text(Rand, y, 15, b.Titel, true);
                if (b.Untertitel.Length > 0) s.Text(Rand, y + 15, 9.5f, b.Untertitel);
                y += b.Untertitel.Length > 0 ? 24 : 12;
            }
            else { s.Text(Rand, y, 10, b.Titel + " (Fortsetzung)", true); y += 8; }
            s.Flaeche(Rand, y, nutz, 17, Farbe.Grau(0.9f));
            s.Linie(Rand, y, Rand + nutz, y, 0.6f);
            s.Linie(Rand, y + 17, Rand + nutz, y + 17, 0.6f);
            var x = Rand;
            for (var i = 0; i < b.Spalten.Count; i++)
            {
                s.TextImKasten(x, y, w[i], 17, 7.8f, b.Spalten[i], zahl[i] ? 2 : 0, true, null, 4);
                x += w[i];
            }
            y += 17;
        }
        void Neu()
        {
            Fusszeile(s, b.Titel, seite);
            seite++;
            s = pdf.NeueSeite(b.Quer);
            Kopf();
        }
        Kopf();
        if (b.Zeilen.Count == 0) { s.Text(Rand + 4, y + 11, g, "Keine Einträge."); y += zh; }
        for (var r = 0; r < b.Zeilen.Count; r++)
        {
            var art = Art(r);
            if (y + zh * (art == "gruppe" ? 3 : 1) > s.Hoehe - Rand - 14) Neu();
            var z = b.Zeilen[r];
            if (art == "gruppe")
            {
                y += 3;
                s.Flaeche(Rand, y, nutz, zh, Farbe.Grau(0.95f));
                s.TextImKasten(Rand, y, nutz, zh, 9.5f, z.Count > 0 ? z[0] : "", 0, true, null, 4);
                s.Linie(Rand, y + zh, Rand + nutz, y + zh, 0.5f);
                y += zh;
                continue;
            }
            var fett = art == "summe";
            if (fett) { s.Flaeche(Rand, y, nutz, zh, Farbe.Grau(0.9f)); s.Linie(Rand, y, Rand + nutz, y, 0.6f); }
            var x = Rand;
            for (var i = 0; i < b.Spalten.Count; i++)
            {
                s.TextImKasten(x, y, w[i], zh, g, i < z.Count ? z[i] : "", zahl[i] ? 2 : 0, fett || i == 0, null, 4);
                x += w[i];
            }
            s.Linie(Rand, y + zh, Rand + nutz, y + zh, 0.4f, fett ? Farbe.Schwarz : Farbe.Grau(0.75f));
            y += zh;
        }
        if (b.Zeilen.Count > 0 && b.Arten is null)
        {
            s.Linie(Rand, y, Rand + nutz, y, 0.6f);
            s.Text(Rand + 4, y + 12, g, b.Zeilen.Count == 1 ? "1 Eintrag" : $"{b.Zeilen.Count} Einträge", true);
        }
        Fusszeile(s, b.Titel, seite);
        return pdf.Erzeugen();
    }

    // ------------------------------------------------------------------------------------------ Übersichtsplan

    /// <summary>Übersichtsplan im Querformat: je Element eine Zeile, Tage × Stunden als Spalten (wie Desktop „… Übersicht quer“).</summary>
    public static byte[] Uebersicht(Schuldatei d, DatenIndex ix, ElementTyp typ, IReadOnlyList<Element> elemente, Func<Element, Dictionary<(int Tag, int Stunde), string>> inhalt)
    {
        var zr = d.Zeitraster;
        var titel = $"{TypText(typ)} Übersicht – {d.Einstellungen.Schulname}";
        var pdf = new PdfDokument { Titel = titel };
        var spalten = zr.TageProWoche * zr.StundenProTag;
        const float nameB = 40, kopf = 24;
        var seite = 1;
        var i = 0;
        do
        {
            var s = pdf.NeueSeite(true);
            var y0 = Kopfzeile(s, d, titel, seite);
            s.Text(Rand, y0, 13, $"{TypText(typ)} Übersicht quer", true);
            y0 += 8;
            var nutz = s.Breite - 2 * Rand;
            var cw = (nutz - nameB) / spalten;
            var verfuegbar = s.Hoehe - Rand - 18 - y0 - kopf;
            var rh = Math.Clamp(verfuegbar / Math.Max(1, elemente.Count - i), 13f, 22f);
            var platz = (int)(verfuegbar / rh);
            var teil = elemente.Skip(i).Take(Math.Max(1, platz)).ToList();
            var g = Math.Clamp(cw / 4.2f, 4f, 6.5f);
            s.Flaeche(Rand, y0, nutz, kopf, Farbe.Grau(0.92f));
            for (var t = 0; t < zr.TageProWoche; t++)
            {
                var x = Rand + nameB + t * zr.StundenProTag * cw;
                s.TextImKasten(x, y0, zr.StundenProTag * cw, 12, 8.5f, Planungsdienst.Tagname(d, t, true), 1, true);
                for (var st = 1; st <= zr.StundenProTag; st++)
                    s.TextImKasten(x + (st - 1) * cw, y0 + 12, cw, 12, 6.5f, zr.StundenLabel(st), 1);
            }
            var y = y0 + kopf;
            for (var k = 0; k < teil.Count; k++)
            {
                var e = teil[k];
                if (k % 2 == 1) s.Flaeche(Rand, y, nutz, rh, Farbe.Grau(0.965f));
                s.TextImKasten(Rand, y, nameB, rh, 7.5f, e.Kurzname, 0, true);
                foreach (var ((tag, st), text) in inhalt(e))
                {
                    var x = Rand + nameB + (tag * zr.StundenProTag + st - 1) * cw;
                    s.Flaeche(x + 0.4f, y + 0.4f, cw - 0.8f, rh - 0.8f, Farbe.Grau(0.85f));
                    s.TextImKasten(x, y, cw, rh, g, text, 1, false, null, 0.6f);
                }
                y += rh;
                s.Linie(Rand, y, Rand + nutz, y, 0.3f, Farbe.Grau(0.7f));
            }
            for (var t = 0; t <= zr.TageProWoche; t++)
            {
                var x = Rand + nameB + t * zr.StundenProTag * cw;
                s.Linie(x, y0, x, y, 0.8f);
            }
            s.Linie(Rand, y0 + kopf, Rand + nutz, y0 + kopf, 0.8f);
            s.Rahmen(Rand, y0, nutz, y - y0, 0.8f);
            Fusszeile(s, titel, seite);
            seite++;
            i += teil.Count;
        } while (i < elemente.Count);
        return pdf.Erzeugen();
    }

    /// <summary>„Periode 1“ bzw. „Periode Halbjahr 2“ (ohne doppeltes „Periode“).</summary>
    public static string PeriodeText(Schuldatei d) =>
        d.AktivePeriode.Name.StartsWith("Periode", StringComparison.CurrentCultureIgnoreCase) ? d.AktivePeriode.Name : $"Periode {d.AktivePeriode.Name}";

    public static string TypText(ElementTyp t) => t switch
    {
        ElementTyp.Klasse => "Klassen",
        ElementTyp.Lehrer => "Lehrer",
        ElementTyp.Raum => "Räume",
        ElementTyp.Fach => "Fächer",
        _ => t.ToString(),
    };
}
