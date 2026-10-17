using System.Globalization;
using System.Text;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Schlanker PDF-Erzeuger für Berichte als schlichtes Schulformular in Schwarz-Weiß (ohne Fremdbibliothek, ohne Logo): A4 quer oder hoch,
/// Kopf mit Schule und Erstellungsdatum, schwarze Trennlinie, Titel mit Zeitraum, Tabelle mit umrandeter Kopfzeile (auf jeder Seite
/// wiederholt), rechtsbündigen Zahlenspalten und feinen Zeilenlinien, Summenzeile, Hinweis, Unterschriftsfeld und Fußzeile „Seite x von y“.
/// Schrift Helvetica (WinAnsi, Umlaute und ß).
/// </summary>
public static partial class PdfBericht
{
    private const float A4Lang = 841.89f, A4Kurz = 595.28f, Rand = 40f;
    /// <summary>Ausrichtung der gerade erzeugten Datei (je Thread, die Erzeugung ist synchron).</summary>
    [ThreadStatic] private static bool _hoch;
    private static float Breite => _hoch ? A4Kurz : A4Lang;
    private static float Hoehe => _hoch ? A4Lang : A4Kurz;

    /// <summary>Ausrichtung aus einer Anfrage: „hoch“ bzw. „quer“, sonst die Vorgabe des Formulars.</summary>
    public static bool Hochformat(string? ausrichtung, bool vorgabe) => ausrichtung?.Trim().ToLowerInvariant() switch
    {
        "hoch" or "hochformat" or "portrait" => true,
        "quer" or "querformat" or "landscape" => false,
        _ => vorgabe,
    };

    private static byte[] MitAusrichtung(bool hoch, Func<byte[]> erzeugen)
    {
        var alt = _hoch;
        _hoch = hoch;
        try { return erzeugen(); }
        finally { _hoch = alt; }
    }

    /// <param name="hochformat">A4 hoch statt quer (schmalere Tabelle, mehr Zeilen je Seite).</param>
    public static byte[] Erzeugen(string schule, string titel, string untertitel, IReadOnlyList<string> spalten, IReadOnlyList<IReadOnlyList<string>> zeilen,
        string? hinweis, string? ersteller, bool unterschrift, bool hochformat, IReadOnlyList<string>? arten = null) =>
        MitAusrichtung(hochformat, () => Erzeugen(schule, titel, untertitel, spalten, zeilen, hinweis, ersteller, unterschrift, arten));
    private const float Schrift = 8.5f, Zeile = 16f;
    private const string Seitenzahl = "@@SEITEN@@";

    /// <param name="arten">Optional je Zeile: <c>gruppe</c> (Gruppenkopf über die ganze Breite), <c>zwischensumme</c>, <c>summe</c> (fett mit Linie) –
    /// gegliederte Berichte; ohne Angabe schlichte Tabelle mit Zahl der Einträge.</param>
    public static byte[] Erzeugen(string schule, string titel, string untertitel, IReadOnlyList<string> spalten, IReadOnlyList<IReadOnlyList<string>> zeilen,
        string? hinweis = null, string? ersteller = null, bool unterschrift = true, IReadOnlyList<string>? arten = null)
    {
        string Art(int r) => arten != null && r < arten.Count ? arten[r] : "";
        var nutz = Breite - 2 * Rand;
        // Spaltenbreiten nach dem längsten Inhalt (Kopf zählt mit), höchstens 40 % je Spalte, auf die Nutzbreite skaliert
        var w = spalten.Select((s, i) => Math.Max(Math.Max(TextBreite(s, true, 7.8f) * .55f, s.Split(' ').Max(x => TextBreite(x, true, 7.8f))), zeilen.Where((z, r) => Art(r) != "gruppe").Select(z => i < z.Count ? Math.Min(TextBreite(z[i], false), nutz * .4f) : 0).DefaultIfEmpty(0).Max()) + 14).ToArray();
        var faktor = nutz / Math.Max(1, w.Sum());
        for (var i = 0; i < w.Length; i++) w[i] *= faktor;
        // Zahlenspalten (alle Werte Zahlen oder leer) rechtsbündig
        var daten = zeilen.Where((z, r) => Art(r) is not ("gruppe" or "zwischensumme" or "summe")).ToList();
        var zahl = spalten.Select((sp, i) => daten.Count > 0 && daten.All(z => i >= z.Count || z[i].Length == 0 || double.TryParse(z[i].Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out _))
            && daten.Any(z => i < z.Count && z[i].Length > 0)).ToArray();
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
            Rechteck(sb, Rand, y - kopfHoehe, nutz, kopfHoehe, Grau(0.9f));
            Linie(sb, Rand, y, Rand + nutz, y, 0f);
            Linie(sb, Rand, y - kopfHoehe, Rand + nutz, y - kopfHoehe, 0f);
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++)
            {
                var teile = kopfZeilen[i].Take(2).ToList();
                if (kopfZeilen[i].Count > 2) teile[1] = Kuerzen(string.Join(" ", kopfZeilen[i].Skip(1)), w[i] - 10, true, 7.8f);
                var ty = y - (kopfHoehe - teile.Count * 9) / 2 - 7;
                foreach (var teil in teile)
                {
                    var t = Kuerzen(teil, w[i] - 10, true, 7.8f);
                    Text(sb, zahl[i] ? x + w[i] - 6 - TextBreite(t, true, 7.8f) : x + 6, ty, 7.8f, true, t, Schwarz);
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
            Text(sb, Rand, y - 10, 11, true, schule, Schwarz);
            Text(sb, Rand, y - 23, 8, false, "Schulverwaltung", Schwarz);
            var r1 = $"Erstellt am {jetzt:dd.MM.yyyy} um {jetzt:HH:mm} Uhr";
            Text(sb, Breite - Rand - TextBreite(r1, false, 8), y - 10, 8, false, r1, Schwarz);
            if (!string.IsNullOrWhiteSpace(ersteller)) { var r2 = $"von {ersteller}"; Text(sb, Breite - Rand - TextBreite(r2, false, 8), y - 21, 8, false, r2, Schwarz); }
            y -= 34;
            Balken(sb, Rand, y, nutz, 0.8f, Schwarz);
            y -= 26;
            if (seite == 1)
            {
                Text(sb, Rand, y, 16, true, titel, Schwarz);
                if (untertitel.Length > 0) Text(sb, Rand, y - 16, 9.5f, false, untertitel, Schwarz);
                y -= untertitel.Length > 0 ? 32 : 18;
            }
            else
            {
                Text(sb, Rand, y + 4, 10, true, titel + " (Fortsetzung)", Schwarz);
                y -= 10;
            }
            Tabellenkopf();
        }
        void Fuss()
        {
            Linie(sb, Rand, Rand - 4, Breite - Rand, Rand - 4, 0f);
            Text(sb, Rand, Rand - 16, 7.5f, false, Kuerzen($"{schule} · {titel}", nutz * .4f, false, 7.5f), Schwarz);
            const string mitte = "Nur für den Dienstgebrauch";
            Text(sb, (Breite - TextBreite(mitte, false, 7.5f)) / 2, Rand - 16, 7.5f, false, mitte, Schwarz);
            var t = $"Seite {seite} von {Seitenzahl}";
            Text(sb, Breite - Rand - TextBreite($"Seite {seite} von 99", false, 7.5f), Rand - 16, 7.5f, false, t, Schwarz);
            seiten.Add(sb.ToString());
        }
        Kopf();
        if (zeilen.Count == 0) { Text(sb, Rand + 6, y - 11, Schrift, false, "Keine Einträge im gewählten Zeitraum.", Schwarz); y -= Zeile; }
        for (var r = 0; r < zeilen.Count; r++)
        {
            var art = Art(r);
            // Gruppenkopf nicht allein am Seitenende
            if (y - Zeile * (art == "gruppe" ? 3 : 1) < Rand + 14) { Fuss(); Kopf(); }
            if (art == "gruppe")
            {
                y -= 4;
                Rechteck(sb, Rand, y - Zeile, nutz, Zeile, Grau(0.95f));
                Text(sb, Rand + 6, y - 11.5f, 9.5f, true, Kuerzen(zeilen[r].Count > 0 ? zeilen[r][0] : "", nutz - 12, true, 9.5f), Schwarz);
                Linie(sb, Rand, y - Zeile, Rand + nutz, y - Zeile, 0f);
                y -= Zeile;
                continue;
            }
            var fett = art is "zwischensumme" or "summe";
            if (art == "summe") { Rechteck(sb, Rand, y - Zeile, nutz, Zeile, Grau(0.9f)); Linie(sb, Rand, y, Rand + nutz, y, 0f); }
            else if (art == "zwischensumme") Linie(sb, Rand, y, Rand + nutz, y, 0.35f);
            var x = Rand;
            for (var i = 0; i < spalten.Count; i++)
            {
                var t = Kuerzen(i < zeilen[r].Count ? zeilen[r][i] : "", w[i] - 10, i == 0 || fett);
                // Zwischensummen: Beschriftung („Summe …“) darf über die Datumsspalte hinauslaufen
                if (fett && i == 1 && !zahl[i] && zeilen[r][i].StartsWith("Summe", StringComparison.Ordinal)) t = Kuerzen(zeilen[r][i], w[i] + (spalten.Count > 2 && !zahl[2] ? w[2] : 0) - 10, true);
                Text(sb, zahl[i] ? x + w[i] - 6 - TextBreite(t, fett) : x + 6, y - 11, Schrift, i == 0 || fett, t, Schwarz);
                x += w[i];
            }
            Linie(sb, Rand, y - Zeile, Rand + nutz, y - Zeile, art == "summe" ? 0f : 0.75f);
            y -= Zeile + (art == "summe" ? 4 : 0);
        }
        // Summenzeile (nur schlichte Tabellen; gegliederte Berichte haben eigene Summen)
        if (zeilen.Count > 0 && arten == null)
        {
            Linie(sb, Rand, y, Rand + nutz, y, 0f);
            Text(sb, Rand + 6, y - 12, Schrift, true, zeilen.Count == 1 ? "1 Eintrag" : $"{zeilen.Count} Einträge", Schwarz);
            y -= Zeile + 2;
        }
        if (!string.IsNullOrWhiteSpace(hinweis))
        {
            foreach (var teil in Umbrechen(hinweis, nutz, 8.5f))
            {
                if (y - 14 < Rand + 14) { Fuss(); Kopf(); }
                Text(sb, Rand, y - 12, 8.5f, false, teil, Schwarz);
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
                Linie(sb, x, y, x + nutz / 2 - 40, y, 0f);
                Text(sb, x, y - 11, 7.5f, false, text, Schwarz);
            }
        }
        Fuss();
        var anzahl = seiten.Count.ToString(CultureInfo.InvariantCulture);
        return Datei(seiten.Select(x => x.Replace(Seitenzahl, anzahl)).ToList());
    }

    // ------------------------------------------------------------------------------------------ Graustufen und Zeichenhilfen

    private readonly record struct Farbe(float R, float G, float B);
    private static readonly Farbe Schwarz = new(0, 0, 0);
    private static Farbe Grau(float g) => new(g, g, g);

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
