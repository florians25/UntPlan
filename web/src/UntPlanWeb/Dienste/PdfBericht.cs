using System.Globalization;
using System.Text;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Schlanker PDF-Erzeuger für Berichte als Schuldokument (ohne Fremdbibliothek): A4 quer, Briefkopf mit Logo, Schule und Erstellungsdatum,
/// orange Trennlinie, Titel mit Zeitraum, Tabelle mit dunkler Kopfzeile (auf jeder Seite wiederholt), rechtsbündigen Zahlenspalten und
/// abwechselnd hinterlegten Zeilen, Summenzeile, Hinweis, Unterschriftsfeld und Fußzeile „Seite x von y“. Schrift Helvetica (WinAnsi, Umlaute und ß).
/// </summary>
public static class PdfBericht
{
    private const float Breite = 841.89f, Hoehe = 595.28f, Rand = 40f;
    private const float Schrift = 8.5f, Zeile = 16f;
    private const string Seitenzahl = "@@SEITEN@@";

    public static byte[] Erzeugen(string schule, string titel, string untertitel, IReadOnlyList<string> spalten, IReadOnlyList<IReadOnlyList<string>> zeilen,
        string? hinweis = null, string? ersteller = null, bool unterschrift = true)
    {
        var nutz = Breite - 2 * Rand;
        // Spaltenbreiten nach dem längsten Inhalt (Kopf zählt mit), höchstens 40 % je Spalte, auf die Nutzbreite skaliert
        var w = spalten.Select((s, i) => Math.Max(Math.Max(TextBreite(s, true, 7.8f) * .55f, s.Split(' ').Max(x => TextBreite(x, true, 7.8f))), zeilen.Select(z => i < z.Count ? Math.Min(TextBreite(z[i], false), nutz * .4f) : 0).DefaultIfEmpty(0).Max()) + 14).ToArray();
        var faktor = nutz / Math.Max(1, w.Sum());
        for (var i = 0; i < w.Length; i++) w[i] *= faktor;
        // Zahlenspalten (alle Werte Zahlen oder leer) rechtsbündig
        var zahl = spalten.Select((sp, i) => zeilen.Count > 0 && zeilen.All(z => i >= z.Count || z[i].Length == 0 || double.TryParse(z[i].Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out _))
            && zeilen.Any(z => i < z.Count && z[i].Length > 0)).ToArray();
        var jetzt = DateTime.Now;

        var seiten = new List<string>();
        var sb = new StringBuilder();
        var y = 0f;
        var seite = 0;
        // Kopfzeile: lange Spaltennamen in zwei Zeilen statt abgeschnitten
        var kopfZeilen = spalten.Select((sp, i) => Umbrechen(sp, w[i] - 10, 7.8f).ToList()).ToList();
        var kopfHoehe = kopfZeilen.Any(k => k.Count > 1) ? 26f : 18f;
        void Tabellenkopf()
        {
            Rechteck(sb, Rand, y - kopfHoehe, nutz, kopfHoehe, Dunkel);
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++)
            {
                var teile = kopfZeilen[i].Take(2).ToList();
                if (kopfZeilen[i].Count > 2) teile[1] = Kuerzen(string.Join(" ", kopfZeilen[i].Skip(1)), w[i] - 10, true, 7.8f);
                var ty = y - (kopfHoehe - teile.Count * 9) / 2 - 7;
                foreach (var teil in teile)
                {
                    var t = Kuerzen(teil, w[i] - 10, true, 7.8f);
                    Text(sb, zahl[i] ? x + w[i] - 6 - TextBreite(t, true, 7.8f) : x + 6, ty, 7.8f, true, t, Weiss);
                    ty -= 9;
                }
                x += w[i];
            }
            y -= kopfHoehe;
        }
        void Kopf()
        {
            seite++;
            sb.Clear();
            y = Hoehe - Rand + 6;
            Logo(sb, Rand, y - 26, 26);
            Text(sb, Rand + 34, y - 10, 11, true, schule, Dunkel);
            Text(sb, Rand + 34, y - 23, 8, false, "Stundenplan · Vertretungsplan · Klassenbuch", Grau(0.45f));
            var r1 = $"Erstellt am {jetzt:dd.MM.yyyy} um {jetzt:HH:mm} Uhr";
            Text(sb, Breite - Rand - TextBreite(r1, false, 8), y - 10, 8, false, r1, Grau(0.4f));
            if (!string.IsNullOrWhiteSpace(ersteller)) { var r2 = $"von {ersteller}"; Text(sb, Breite - Rand - TextBreite(r2, false, 8), y - 21, 8, false, r2, Grau(0.4f)); }
            y -= 34;
            Balken(sb, Rand, y, nutz, 1.6f, Akzent);
            y -= 26;
            if (seite == 1)
            {
                Text(sb, Rand, y, 17, true, titel, Dunkel);
                if (untertitel.Length > 0) Text(sb, Rand, y - 16, 9.5f, false, untertitel, Grau(0.35f));
                y -= untertitel.Length > 0 ? 32 : 18;
            }
            else
            {
                Text(sb, Rand, y + 4, 10, true, titel + " (Fortsetzung)", Dunkel);
                y -= 10;
            }
            Tabellenkopf();
        }
        void Fuss()
        {
            Linie(sb, Rand, Rand - 4, Breite - Rand, Rand - 4, 0.8f);
            Text(sb, Rand, Rand - 16, 7.5f, false, Kuerzen($"{schule} · {titel}", nutz * .4f, false, 7.5f), Grau(0.45f));
            const string mitte = "Vertraulich – nur für den Dienstgebrauch";
            Text(sb, (Breite - TextBreite(mitte, false, 7.5f)) / 2, Rand - 16, 7.5f, false, mitte, Grau(0.45f));
            var t = $"Seite {seite} von {Seitenzahl}";
            Text(sb, Breite - Rand - TextBreite($"Seite {seite} von 99", false, 7.5f), Rand - 16, 7.5f, false, t, Grau(0.45f));
            seiten.Add(sb.ToString());
        }
        Kopf();
        if (zeilen.Count == 0) { Text(sb, Rand + 6, y - 11, Schrift, false, "Keine Einträge im gewählten Zeitraum.", Grau(0.35f)); y -= Zeile; }
        for (var r = 0; r < zeilen.Count; r++)
        {
            if (y - Zeile < Rand + 14) { Fuss(); Kopf(); }
            if (r % 2 == 1) Rechteck(sb, Rand, y - Zeile, nutz, Zeile, Grau(0.965f));
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++)
            {
                var t = Kuerzen(i < zeilen[r].Count ? zeilen[r][i] : "", w[i] - 10, i == 0);
                Text(sb, zahl[i] ? x + w[i] - 6 - TextBreite(t, false) : x + 6, y - 11, Schrift, i == 0, t, zahl[i] && t == "0" ? Grau(0.62f) : Grau(0.12f));
                x += w[i];
            }
            Linie(sb, Rand, y - Zeile, Rand + nutz, y - Zeile, 0.88f);
            y -= Zeile;
        }
        // Summenzeile
        if (zeilen.Count > 0)
        {
            Linie(sb, Rand, y, Rand + nutz, y, 0.35f);
            Text(sb, Rand + 6, y - 12, Schrift, true, zeilen.Count == 1 ? "1 Eintrag" : $"{zeilen.Count} Einträge", Dunkel);
            y -= Zeile + 2;
        }
        if (!string.IsNullOrWhiteSpace(hinweis))
        {
            foreach (var teil in Umbrechen(hinweis, nutz, 8.5f))
            {
                if (y - 14 < Rand + 14) { Fuss(); Kopf(); }
                Text(sb, Rand, y - 12, 8.5f, false, teil, Grau(0.35f));
                y -= 12;
            }
            y -= 4;
        }
        if (unterschrift)
        {
            if (y - 60 < Rand + 14) { Fuss(); Kopf(); }
            y -= 46;
            foreach (var (x, text) in new[] { (Rand, "Ort, Datum"), (Rand + nutz / 2 + 20, "Unterschrift") })
            {
                Linie(sb, x, y, x + nutz / 2 - 40, y, 0.45f);
                Text(sb, x, y - 11, 7.5f, false, text, Grau(0.45f));
            }
        }
        Fuss();
        var anzahl = seiten.Count.ToString(CultureInfo.InvariantCulture);
        return Datei(seiten.Select(x => x.Replace(Seitenzahl, anzahl)).ToList());
    }

    // ------------------------------------------------------------------------------------------ Farben und Zeichenhilfen

    private readonly record struct Farbe(float R, float G, float B);
    private static readonly Farbe Dunkel = new(0.145f, 0.216f, 0.275f), Weiss = new(1, 1, 1), Akzent = new(0.96f, 0.42f, 0.165f);
    private static Farbe Grau(float g) => new(g, g, g);

    /// <summary>Bildmarke wie das Programmlogo: Kalenderblatt mit blauer Kopfzeile und farbigen Feldern.</summary>
    private static void Logo(StringBuilder sb, float x, float y, float groesse)
    {
        var e = groesse / 28f;
        void Feld(float fx, float fy, float fb, float fh, Farbe f) => Rechteck(sb, x + fx * e, y + (28 - fy - fh) * e, fb * e, fh * e, f);
        Feld(0, 0, 28, 28, new(0.5f, 0.51f, 0.53f));
        Feld(0.6f, 0.6f, 26.8f, 26.8f, Weiss);
        Feld(0.6f, 0.6f, 26.8f, 6.4f, new(0.184f, 0.435f, 0.753f));
        Feld(2, 9, 3, 16.4f, new(0.84f, 0.86f, 0.89f));
        var farben = new Farbe[] { new(0.9f, 0.51f, 0.165f), new(0.184f, 0.435f, 0.753f), new(0.84f, 0.86f, 0.89f), new(0.18f, 0.5f, 0.28f),
            new(0.18f, 0.5f, 0.28f), new(0.95f, 0.745f, 0.18f), new(0.48f, 0.34f, 0.72f), new(0.184f, 0.435f, 0.753f) };
        for (var i = 0; i < 8; i++) Feld(6.4f + i % 4 * 5.1f, 10 + i / 4 * 5.2f, 4.2f, 4.2f, farben[i]);
    }

    private static IEnumerable<string> Umbrechen(string text, float max, float groesse)
    {
        var zeile = "";
        foreach (var wort in text.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var neu = zeile.Length == 0 ? wort : zeile + " " + wort;
            if (TextBreite(neu, false, groesse) > max && zeile.Length > 0) { yield return zeile; zeile = wort; }
            else zeile = neu;
        }
        if (zeile.Length > 0) yield return zeile;
    }

    // ------------------------------------------------------------------------------------------ PDF-Grundbausteine

    private static readonly Encoding Win = CodePages();
    private static Encoding CodePages()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252); }
        catch { return Encoding.Latin1; }
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Text(StringBuilder sb, float x, float y, float groesse, bool fett, string text, Farbe f) =>
        sb.Append($"BT {F(f.R)} {F(f.G)} {F(f.B)} rg /{(fett ? "F2" : "F1")} {F(groesse)} Tf {F(x)} {F(y)} Td ({Maskieren(text)}) Tj ET\n");

    private static void Rechteck(StringBuilder sb, float x, float y, float b, float h, Farbe f) =>
        sb.Append($"{F(f.R)} {F(f.G)} {F(f.B)} rg {F(x)} {F(y)} {F(b)} {F(h)} re f\n");

    private static void Balken(StringBuilder sb, float x, float y, float b, float h, Farbe f) => Rechteck(sb, x, y - h, b, h, f);

    private static void Linie(StringBuilder sb, float x1, float y1, float x2, float y2, float grau) =>
        sb.Append($"{F(grau)} G 0.5 w {F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S\n");

    private static string Maskieren(string t)
    {
        var sb = new StringBuilder();
        foreach (var ch in t.Replace('\n', ' ').Replace('\r', ' '))
        {
            var c = ch switch { '–' or '—' => '-', '„' or '“' or '”' => '"', '‚' or '‘' or '’' => '\'', '→' => '>', '…' => '.', '′' => '\'', _ => ch };
            var b = Win.GetBytes(c.ToString());
            var code = b.Length == 1 ? b[0] : (byte)'?';
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (code < 32 || code > 126) sb.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
            else sb.Append((char)code);
        }
        return sb.ToString();
    }

    /// <summary>Ungefähre Textbreite in Helvetica (Mittelwerte der Zeichenbreiten), für Spaltenbreiten und Kürzen.</summary>
    private static float TextBreite(string t, bool fett, float groesse = Schrift)
    {
        float s = 0;
        foreach (var c in t)
            s += c switch
            {
                'i' or 'l' or 'j' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' => 0.24f,
                'f' or 't' or 'r' or ' ' or '(' or ')' or '-' => 0.32f,
                'm' or 'w' or 'M' or 'W' => 0.83f,
                >= 'A' and <= 'Z' or 'Ä' or 'Ö' or 'Ü' => 0.68f,
                >= '0' and <= '9' => 0.556f,
                _ => 0.53f,
            };
        return s * groesse * (fett ? 1.06f : 1f);
    }

    private static string Kuerzen(string t, float max, bool fett, float groesse = Schrift)
    {
        if (TextBreite(t, fett, groesse) <= max) return t;
        while (t.Length > 1 && TextBreite(t + "...", fett, groesse) > max) t = t[..^1];
        return t.TrimEnd() + "...";
    }

    private static byte[] Datei(List<string> seiten)
    {
        var objekte = new List<string>();
        var anzahl = seiten.Count;
        // 1 Katalog, 2 Seitenbaum, 3 Helvetica, 4 Helvetica-Bold, dann je Seite Seite + Inhalt
        objekte.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objekte.Add($"<< /Type /Pages /Count {anzahl} /Kids [{string.Join(" ", Enumerable.Range(0, anzahl).Select(i => $"{5 + 2 * i} 0 R"))}] >>");
        objekte.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objekte.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        foreach (var (inhalt, i) in seiten.Select((s, i) => (s, i)))
        {
            objekte.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(Breite)} {F(Hoehe)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + 2 * i} 0 R >>");
            objekte.Add($"<< /Length {Win.GetByteCount(inhalt)} >>\nstream\n{inhalt}endstream");
        }
        using var ms = new MemoryStream();
        void Schreiben(string s) { var b = Win.GetBytes(s); ms.Write(b, 0, b.Length); }
        Schreiben("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objekte.Count; i++)
        {
            offsets.Add(ms.Position);
            Schreiben($"{i + 1} 0 obj\n{objekte[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Schreiben($"xref\n0 {objekte.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Schreiben($"{o:D10} 00000 n \n");
        Schreiben($"trailer\n<< /Size {objekte.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
