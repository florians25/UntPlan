using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UntPlan.Core.Austausch;

/// <summary>
/// Export nach Microsoft Excel: schreibt eine echte Excel-Arbeitsmappe (.xlsx, Office Open XML) ohne Zusatzbibliothek – Excel öffnet sie
/// ohne Formatwarnung. Zahlen (auch mit Komma, „+3“, „-1,5“) werden als Zahlen abgelegt, alles andere als Text; fette Zeilen
/// (Überschriften, Summen) bleiben fett.
/// </summary>
public static class Excel
{
    /// <summary>Eine Zeile der Tabelle; <see cref="Fett"/> für Überschriften und Summenzeilen.</summary>
    public sealed record Zeile(IReadOnlyList<string> Zellen, bool Fett = false);

    /// <summary>Ein Tabellenblatt (Name höchstens 31 Zeichen, ohne : \ / ? * [ ]).</summary>
    public sealed record Blatt(string Name, IReadOnlyList<Zeile> Zeilen);

    /// <summary>Arbeitsmappe mit einem Blatt aus einfachen Zeilen (erste Zeile fett).</summary>
    public static byte[] Arbeitsmappe(string blatt, IEnumerable<IReadOnlyList<string>> zeilen) =>
        Arbeitsmappe(new[] { new Blatt(blatt, zeilen.Select((z, i) => new Zeile(z, i == 0)).ToList()) });

    /// <summary>Arbeitsmappe (.xlsx) mit den angegebenen Blättern.</summary>
    public static byte[] Arbeitsmappe(IReadOnlyList<Blatt> blaetter)
    {
        if (blaetter.Count == 0) blaetter = new[] { new Blatt("Tabelle1", Array.Empty<Zeile>()) };
        var namen = EindeutigeNamen(blaetter.Select(b => b.Name));
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Datei(string pfad, string inhalt)
            {
                var e = zip.CreateEntry(pfad, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(inhalt);
            }
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (var i = 1; i <= blaetter.Count; i++)
                sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            Datei("[Content_Types].xml", sb.Append("</Types>").ToString());
            Datei("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            sb.Clear().Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (var i = 0; i < blaetter.Count; i++) sb.Append($"<sheet name=\"{Xml(namen[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            Datei("xl/workbook.xml", sb.Append("</sheets></workbook>").ToString());
            sb.Clear().Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var i = 0; i < blaetter.Count; i++)
                sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            sb.Append($"<Relationship Id=\"rId{blaetter.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            Datei("xl/_rels/workbook.xml.rels", sb.Append("</Relationships>").ToString());
            // Stil 0 = normal, Stil 1 = fett
            Datei("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><name val=\"Arial\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "</styleSheet>");
            for (var i = 0; i < blaetter.Count; i++) Datei($"xl/worksheets/sheet{i + 1}.xml", BlattXml(blaetter[i]));
        }
        return ms.ToArray();
    }

    private static string BlattXml(Blatt b)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        // Spaltenbreiten nach dem längsten Inhalt (höchstens 60 Zeichen)
        var spalten = b.Zeilen.Select(z => z.Zellen.Count).DefaultIfEmpty(0).Max();
        if (spalten > 0)
        {
            sb.Append("<cols>");
            for (var c = 0; c < spalten; c++)
            {
                var laenge = b.Zeilen.Where(z => c < z.Zellen.Count).Select(z => (z.Zellen[c] ?? "").Length).DefaultIfEmpty(0).Max();
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{Math.Clamp(laenge + 2, 6, 60)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (var r = 0; r < b.Zeilen.Count; r++)
        {
            var z = b.Zeilen[r];
            sb.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < z.Zellen.Count; c++)
            {
                var text = z.Zellen[c] ?? "";
                if (text.Length == 0) continue;
                var stil = z.Fett ? " s=\"1\"" : "";
                var bezug = Spalte(c) + (r + 1);
                if (Zahl(text) is { } n) sb.Append($"<c r=\"{bezug}\"{stil}><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>");
                else sb.Append($"<c r=\"{bezug}\"{stil} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xml(text)}</t></is></c>");
            }
            sb.Append("</row>");
        }
        return sb.Append("</sheetData></worksheet>").ToString();
    }

    /// <summary>Spaltenbuchstaben (0 → A, 26 → AA).</summary>
    public static string Spalte(int index)
    {
        var s = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static readonly Regex ZahlMuster = new(@"^[+-]?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$|^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Zahl einer Zelle oder null: deutsche Schreibweise („1.234,5“, „-1,5“, „+3“) bzw. Punkt als Dezimaltrenner („0.000“, „2.5“).
    /// Texte mit führender Null wie „05“ oder Kürzel wie „1a“ bleiben Text.
    /// </summary>
    public static decimal? Zahl(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.Length > 18 || !ZahlMuster.IsMatch(t)) return null;
        var ohneVorzeichen = t.TrimStart('+', '-');
        if (ohneVorzeichen.Length > 1 && ohneVorzeichen[0] == '0' && char.IsDigit(ohneVorzeichen[1])) return null;
        if (t.Contains(','))
            return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("de-DE"), out var d) ? d : null;
        // nur Punkte: „1.234.567“ (Tausender) bzw. „0.000“/„2.5“ (Dezimalpunkt)
        if (Regex.IsMatch(t, @"^[+-]?\d{1,3}(\.\d{3}){2,}$")) t = t.Replace(".", "");
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var e) ? e : null;
    }

    private static string Xml(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch < 0x20 && ch is not ('\t' or '\n' or '\r')) continue; // in XML nicht erlaubt
            sb.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => ch.ToString() });
        }
        return sb.ToString();
    }

    private static List<string> EindeutigeNamen(IEnumerable<string> namen)
    {
        var res = new List<string>();
        foreach (var roh in namen)
        {
            var n = new string((roh ?? "").Where(c => !":\\/?*[]".Contains(c)).ToArray()).Trim();
            if (n.Length == 0) n = "Tabelle";
            if (n.Length > 31) n = n[..31];
            var basis = n;
            for (var i = 2; res.Contains(n, StringComparer.OrdinalIgnoreCase); i++)
                n = (basis.Length > 27 ? basis[..27] : basis) + " " + i;
            res.Add(n);
        }
        return res;
    }

    // --------------------------------------------------------------------------------------------
    // HTML-Bericht → Zeilen
    // --------------------------------------------------------------------------------------------

    private static readonly Regex Token = new(@"<(/?)([a-zA-Z0-9]+)([^>]*)>|([^<]+)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly HashSet<string> Block = new(StringComparer.OrdinalIgnoreCase) { "div", "p", "h1", "h2", "h3", "h4", "br", "li", "table", "body" };

    /// <summary>
    /// Zeilen eines HTML-Berichts (wie ihn die Berichtsansicht zeigt) für den Excel-Export: jede Tabellenzeile wird eine Zeile mit ihren
    /// Zellen (colspan füllt leere Zellen auf), Text außerhalb von Tabellen (Überschriften, Bilanzzeilen) je Absatz bzw. Zeilenumbruch eine
    /// einspaltige Zeile. Kopfzeilen (th), Überschriften, fette Absätze und Summenzeilen sind fett. Stil, Skripte und Titel entfallen.
    /// </summary>
    public static List<Zeile> AusHtml(string html)
    {
        var res = new List<Zeile>();
        html = Regex.Replace(html, @"<(style|script|title|head)\b.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        List<string>? zeile = null;
        var zeileFett = false;
        StringBuilder? zelle = null;
        var zelleSpalten = 1;
        var text = new StringBuilder();
        var fettTiefe = 0;
        var textFett = false;
        var tabellenTiefe = 0;
        var stapel = new Stack<bool>();

        void TextAbschliessen()
        {
            var t = Normalisieren(text.ToString());
            if (t.Length > 0) res.Add(new Zeile(new[] { t }, textFett));
            text.Clear();
            textFett = false;
        }
        void ZelleAbschliessen()
        {
            if (zelle is null || zeile is null) return;
            zeile.Add(Normalisieren(zelle.ToString()));
            for (var i = 1; i < zelleSpalten; i++) zeile.Add("");
            zelle = null;
        }
        void ZeileAbschliessen()
        {
            ZelleAbschliessen();
            if (zeile is { Count: > 0 } && zeile.Any(z => z.Length > 0)) res.Add(new Zeile(zeile.ToList(), zeileFett));
            zeile = null;
        }

        foreach (Match m in Token.Matches(html))
        {
            if (m.Groups[4].Success)
            {
                var t = WebUtility.HtmlDecode(m.Groups[4].Value);
                if (zelle != null) zelle.Append(t);
                else if (tabellenTiefe == 0)
                {
                    if (fettTiefe > 0 && t.Trim().Length > 0) textFett = true;
                    text.Append(t);
                }
                continue;
            }
            var zu = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var attr = m.Groups[3].Value;
            var fettAttr = Regex.IsMatch(attr, @"class\s*=\s*""[^""]*\b(b|bil|s|kn)\b", RegexOptions.IgnoreCase) || Regex.IsMatch(attr, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase);
            switch (tag)
            {
                case "table":
                    if (!zu) { TextAbschliessen(); tabellenTiefe++; }
                    else { ZeileAbschliessen(); tabellenTiefe = Math.Max(0, tabellenTiefe - 1); }
                    continue;
                case "tr":
                    ZeileAbschliessen();
                    if (!zu) { zeile = new List<string>(); zeileFett = fettAttr; }
                    continue;
                case "td" or "th":
                    ZelleAbschliessen();
                    if (!zu && zeile != null)
                    {
                        zelle = new StringBuilder();
                        var span = Regex.Match(attr, @"colspan\s*=\s*""?(\d+)", RegexOptions.IgnoreCase);
                        zelleSpalten = span.Success ? Math.Clamp(int.Parse(span.Groups[1].Value, CultureInfo.InvariantCulture), 1, 100) : 1;
                        if (tag == "th") zeileFett = true;
                    }
                    continue;
            }
            if (zelle != null)
            {
                if (tag == "br") zelle.Append(' ');
                continue;
            }
            if (tabellenTiefe > 0) continue;
            if (Block.Contains(tag) || tag is "h1" or "h2" or "h3" or "h4") TextAbschliessen();
            else if (tag == "span" && !zu && text.Length > 0) text.Append(' ');
            if (tag is "b" or "strong" or "h1" or "h2" or "h3" or "h4")
                fettTiefe = zu ? Math.Max(0, fettTiefe - 1) : fettTiefe + 1;
            else if (tag is "div" or "span" or "p")
            {
                // Fettdruck über die Klasse bzw. den Stil des Elements: beim Schließen das passende Öffnen zurücknehmen
                if (!zu) { stapel.Push(fettAttr); if (fettAttr) fettTiefe++; }
                else if (stapel.Count > 0 && stapel.Pop()) fettTiefe = Math.Max(0, fettTiefe - 1);
            }
        }
        ZeileAbschliessen();
        TextAbschliessen();
        return res;
    }

    private static string Normalisieren(string s) => Regex.Replace(s.Replace(' ', ' '), @"\s+", " ").Trim();
}
