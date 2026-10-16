using System.Globalization;
using System.Text;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Schlanker PDF-Erzeuger für Berichte (ohne Fremdbibliothek): A4 quer, Kopf mit Schule, Titel und Zeitraum, Tabelle mit
/// wiederholter Kopfzeile auf jeder Seite, abwechselnd hinterlegte Zeilen, Seitenzahlen. Schrift Helvetica (WinAnsi, Umlaute und ß).
/// </summary>
public static class PdfBericht
{
    private const float Breite = 841.89f, Hoehe = 595.28f, Rand = 36f;
    private const float Schrift = 8.5f, Zeile = 15f;

    public static byte[] Erzeugen(string schule, string titel, string untertitel, IReadOnlyList<string> spalten, IReadOnlyList<IReadOnlyList<string>> zeilen, string? hinweis = null)
    {
        var nutz = Breite - 2 * Rand;
        // Spaltenbreiten nach dem längsten Inhalt (Kopf zählt mit), höchstens 40 % je Spalte, auf die Nutzbreite skaliert
        var w = spalten.Select((s, i) => Math.Max(TextBreite(s, true), zeilen.Select(z => i < z.Count ? Math.Min(TextBreite(z[i], false), nutz * .4f) : 0).DefaultIfEmpty(0).Max()) + 10).ToArray();
        var faktor = nutz / Math.Max(1, w.Sum());
        for (var i = 0; i < w.Length; i++) w[i] *= faktor;

        var seiten = new List<string>();
        var sb = new StringBuilder();
        var y = 0f;
        var seite = 0;
        void Kopf()
        {
            seite++;
            sb.Clear();
            y = Hoehe - Rand;
            Text(sb, Rand, y - 10, 9, false, schule, 0.42f);
            Text(sb, Breite - Rand - TextBreite(DateTime.Now.ToString("dd.MM.yyyy HH:mm"), false, 9), y - 10, 9, false, DateTime.Now.ToString("dd.MM.yyyy HH:mm"), 0.42f);
            Text(sb, Rand, y - 32, 16, true, titel, 0.12f);
            if (untertitel.Length > 0) Text(sb, Rand, y - 48, 9.5f, false, untertitel, 0.35f);
            y -= 66;
            // Tabellenkopf
            Rechteck(sb, Rand, y - Zeile + 4, nutz, Zeile, 0.93f);
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++) { Text(sb, x + 5, y - 7, Schrift, true, Kuerzen(spalten[i], w[i] - 8, true), 0.15f); x += w[i]; }
            y -= Zeile;
        }
        void Fuss()
        {
            var t = $"Seite {seite}";
            Text(sb, Breite - Rand - TextBreite(t, false, 8), Rand - 14, 8, false, t, 0.45f);
            Text(sb, Rand, Rand - 14, 8, false, "Erstellt mit UntPlanWeb", 0.45f);
            seiten.Add(sb.ToString());
        }
        Kopf();
        if (zeilen.Count == 0) { Text(sb, Rand + 5, y - 9, Schrift, false, "Keine Einträge im gewählten Zeitraum.", 0.35f); y -= Zeile; }
        for (var r = 0; r < zeilen.Count; r++)
        {
            if (y - Zeile < Rand + 10) { Fuss(); Kopf(); }
            if (r % 2 == 1) Rechteck(sb, Rand, y - Zeile + 4, nutz, Zeile, 0.975f);
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++)
            {
                var t = i < zeilen[r].Count ? zeilen[r][i] : "";
                Text(sb, x + 5, y - 7, Schrift, false, Kuerzen(t, w[i] - 8, false), 0.1f);
                x += w[i];
            }
            Linie(sb, Rand, y - Zeile + 4, Rand + nutz, y - Zeile + 4, 0.88f);
            y -= Zeile;
        }
        if (!string.IsNullOrWhiteSpace(hinweis))
        {
            if (y - 24 < Rand + 10) { Fuss(); Kopf(); }
            Text(sb, Rand, y - 14, 8.5f, false, hinweis, 0.35f);
        }
        Fuss();
        return Datei(seiten);
    }

    // ------------------------------------------------------------------------------------------ PDF-Grundbausteine

    private static readonly Encoding Win = CodePages();
    private static Encoding CodePages()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252); }
        catch { return Encoding.Latin1; }
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Text(StringBuilder sb, float x, float y, float groesse, bool fett, string text, float grau) =>
        sb.Append($"BT {F(grau)} g /{(fett ? "F2" : "F1")} {F(groesse)} Tf {F(x)} {F(y)} Td ({Maskieren(text)}) Tj ET\n");

    private static void Rechteck(StringBuilder sb, float x, float y, float b, float h, float grau) =>
        sb.Append($"{F(grau)} g {F(x)} {F(y)} {F(b)} {F(h)} re f\n");

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

    private static string Kuerzen(string t, float max, bool fett)
    {
        if (TextBreite(t, fett) <= max) return t;
        while (t.Length > 1 && TextBreite(t + "…", fett) > max) t = t[..^1];
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
