using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UntPlanStudio.Dienste;

/// <summary>
/// Schlanker PDF-Erzeuger ohne Fremdbibliothek (Druck-Modul): A4 hoch oder quer, Schrift Helvetica bzw. Helvetica-Bold
/// (WinAnsi, Umlaute, ß, Gedankenstrich, Anführungszeichen), Linien, Rechtecke, Graustufen und Farben, komprimierte Seiteninhalte.
/// Koordinaten in Punkt mit Ursprung OBEN links (wie beim Zeichnen am Bildschirm); die Umrechnung erledigt <see cref="PdfSeite"/>.
/// </summary>
public sealed class PdfDokument
{
    public const float A4Lang = 841.89f, A4Kurz = 595.28f;
    private readonly List<PdfSeite> _seiten = new();

    public string Titel { get; set; } = "";
    public IReadOnlyList<PdfSeite> Seiten => _seiten;

    public PdfSeite NeueSeite(bool quer)
    {
        var s = new PdfSeite(quer ? A4Lang : A4Kurz, quer ? A4Kurz : A4Lang);
        _seiten.Add(s);
        return s;
    }

    /// <summary>Platzhalter für die Gesamtseitenzahl in Texten („Seite 1 / @@N@@“) – wird beim Erzeugen ersetzt.</summary>
    public const string Seitenzahl = "@@N@@";

    public byte[] Erzeugen()
    {
        if (_seiten.Count == 0) NeueSeite(false);
        var n = _seiten.Count.ToString(CultureInfo.InvariantCulture);
        var objekte = new List<byte[]>();
        void Obj(string s) => objekte.Add(Kodierung.Latin(s));
        // 1 Katalog, 2 Seitenbaum, 3 Helvetica, 4 Helvetica-Bold, 5 Info, dann je Seite: Seite + Inhalt
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Count {_seiten.Count} /Kids [{string.Join(" ", _seiten.Select((_, i) => $"{6 + 2 * i} 0 R"))}] >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Obj($"<< /Producer (UntPlan Studio) /Title ({PdfSeite.Maskieren(Titel)}) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");
        for (var i = 0; i < _seiten.Count; i++)
        {
            var s = _seiten[i];
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfSeite.F(s.Breite)} {PdfSeite.F(s.Hoehe)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {7 + 2 * i} 0 R >>");
            var roh = Kodierung.WinAnsi(s.Inhalt.Replace(Seitenzahl, n));
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(roh);
            var gepackt = ms.ToArray();
            var kopf = Kodierung.Latin($"<< /Length {gepackt.Length} /Filter /FlateDecode >>\nstream\n");
            objekte.Add(kopf.Concat(gepackt).Concat(Kodierung.Latin("\nendstream")).ToArray());
        }
        using var aus = new MemoryStream();
        void Schreiben(byte[] b) => aus.Write(b, 0, b.Length);
        Schreiben(Kodierung.Latin("%PDF-1.4\n%âãÏÓ\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objekte.Count; i++)
        {
            offsets.Add(aus.Position);
            Schreiben(Kodierung.Latin($"{i + 1} 0 obj\n"));
            Schreiben(objekte[i]);
            Schreiben(Kodierung.Latin("\nendobj\n"));
        }
        var xref = aus.Position;
        var sb = new StringBuilder($"xref\n0 {objekte.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objekte.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Schreiben(Kodierung.Latin(sb.ToString()));
        return aus.ToArray();
    }

    // ------------------------------------------------------------------------------------------ Textmaße (Helvetica-AFM)

    private static readonly short[] Normal =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
        278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667,
        611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833,
        556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static readonly short[] Fett =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
        333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667,
        611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889,
        611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    /// <summary>Breite eines Textes in Punkt.</summary>
    public static float TextBreite(string? text, float groesse, bool fett = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var t = fett ? Fett : Normal;
        var s = 0;
        foreach (var ch in text)
        {
            var c = Kodierung.Basis(ch);
            s += c is >= ' ' and <= '~' ? t[c - ' '] : ch == 'ß' ? 611 : ch is '–' ? 556 : ch is '—' or '…' ? 1000 : 556;
        }
        return s * groesse / 1000f;
    }

    /// <summary>Text auf eine Breite kürzen (mit „…“).</summary>
    public static string Kuerzen(string? text, float max, float groesse, bool fett = false)
    {
        var t = text ?? "";
        if (TextBreite(t, groesse, fett) <= max) return t;
        while (t.Length > 1 && TextBreite(t + "…", groesse, fett) > max) t = t[..^1];
        return t.Length <= 1 && TextBreite(t + "…", groesse, fett) > max ? "" : t.TrimEnd() + "…";
    }

    /// <summary>Zeilenumbruch nach Wörtern (harte Umbrüche bleiben erhalten).</summary>
    public static List<string> Umbrechen(string? text, float max, float groesse, bool fett = false)
    {
        var res = new List<string>();
        foreach (var absatz in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var zeile = "";
            foreach (var wort in absatz.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var neu = zeile.Length == 0 ? wort : zeile + " " + wort;
                if (TextBreite(neu, groesse, fett) > max && zeile.Length > 0) { res.Add(zeile); zeile = wort; }
                else zeile = neu;
            }
            res.Add(zeile);
        }
        return res;
    }

    /// <summary>Zeichenkodierung für PDF-Texte (WinAnsi) und Basiszeichen für die Breitenberechnung.</summary>
    internal static class Kodierung
    {
        public static byte[] Latin(string s) => Encoding.Latin1.GetBytes(s);

        public static byte[] WinAnsi(string s)
        {
            var b = new byte[s.Length];
            for (var i = 0; i < s.Length; i++) b[i] = Byte(s[i]);
            return b;
        }

        public static byte Byte(char c) => c switch
        {
            '€' => 0x80, '‚' => 0x82, '„' => 0x84, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94, '•' => 0x95,
            '–' => 0x96, '—' => 0x97, '′' => 0x27, '→' => (byte)'>', '✓' => (byte)'x',
            _ => c <= 0xFF ? (byte)c : (byte)'?',
        };

        /// <summary>Zeichen ohne Akzent (für die Breite): ä → a, Ö → O.</summary>
        public static char Basis(char c) => c switch
        {
            'ä' or 'à' or 'á' or 'â' or 'ã' or 'å' => 'a', 'Ä' or 'À' or 'Á' or 'Â' or 'Ã' or 'Å' => 'A',
            'ö' or 'ò' or 'ó' or 'ô' or 'õ' => 'o', 'Ö' or 'Ò' or 'Ó' or 'Ô' or 'Õ' => 'O',
            'ü' or 'ù' or 'ú' or 'û' => 'u', 'Ü' or 'Ù' or 'Ú' or 'Û' => 'U',
            'é' or 'è' or 'ê' or 'ë' => 'e', 'É' or 'È' or 'Ê' or 'Ë' => 'E', 'í' or 'ì' or 'î' or 'ï' => 'i', 'ç' => 'c', 'ñ' => 'n',
            '„' or '“' or '”' => '"', '‚' or '‘' or '’' => '\'',
            _ => c,
        };
    }
}

/// <summary>Eine Seite: Zeichenbefehle mit Ursprung oben links (Punkt).</summary>
public sealed class PdfSeite
{
    private readonly StringBuilder _sb = new();
    public float Breite { get; }
    public float Hoehe { get; }
    internal string Inhalt => _sb.ToString();

    internal PdfSeite(float breite, float hoehe)
    {
        Breite = breite;
        Hoehe = hoehe;
    }

    internal static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private float Y(float y) => Hoehe - y;

    /// <summary>Farbe als Graustufe (0 = schwarz, 1 = weiß) oder RGB (0..1).</summary>
    public readonly record struct Farbe(float R, float G, float B)
    {
        public static Farbe Grau(float g) => new(g, g, g);
        public static readonly Farbe Schwarz = new(0, 0, 0);
        public static readonly Farbe Weiss = new(1, 1, 1);

        /// <summary>Farbe aus einem Desktop-Farbwert (0xRRGGBB, ARGB als int).</summary>
        public static Farbe AusInt(int argb) => new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f);

        /// <summary>Aufgehellte Farbe (Druck: Fachfarbe als zarte Hinterlegung).</summary>
        public Farbe Hell(float anteil) => new(R + (1 - R) * anteil, G + (1 - G) * anteil, B + (1 - B) * anteil);
    }

    private string Fuell(Farbe f) => $"{F(f.R)} {F(f.G)} {F(f.B)} rg";
    private string Strich(Farbe f) => $"{F(f.R)} {F(f.G)} {F(f.B)} RG";

    /// <summary>Text mit Grundlinie bei <paramref name="grundlinie"/>.</summary>
    public void Text(float x, float grundlinie, float groesse, string? text, bool fett = false, Farbe? farbe = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        _sb.Append($"BT {Fuell(farbe ?? Farbe.Schwarz)} /{(fett ? "F2" : "F1")} {F(groesse)} Tf {F(x)} {F(Y(grundlinie))} Td ({Maskieren(text)}) Tj ET\n");
    }

    /// <summary>Grundlinie für senkrecht zentrierten Text in einem Kasten.</summary>
    public static float Mitte(float oben, float hoehe, float groesse) => oben + hoehe / 2 + groesse * 0.35f;

    /// <summary>Text in einem Kasten: Ausrichtung 0 = links, 1 = mittig, 2 = rechts; senkrecht mittig; gekürzt auf die Breite.</summary>
    public void TextImKasten(float x, float y, float b, float h, float groesse, string? text, int ausrichtung = 0, bool fett = false, Farbe? farbe = null, float innen = 2)
    {
        var t = PdfDokument.Kuerzen(text, b - 2 * innen, groesse, fett);
        if (t.Length == 0) return;
        var w = PdfDokument.TextBreite(t, groesse, fett);
        var tx = ausrichtung switch { 1 => x + (b - w) / 2, 2 => x + b - innen - w, _ => x + innen };
        Text(tx, Mitte(y, h, groesse), groesse, t, fett, farbe);
    }

    public void TextRechts(float rechts, float grundlinie, float groesse, string? text, bool fett = false) =>
        Text(rechts - PdfDokument.TextBreite(text, groesse, fett), grundlinie, groesse, text, fett);

    public void TextMittig(float x, float b, float grundlinie, float groesse, string? text, bool fett = false) =>
        Text(x + (b - PdfDokument.TextBreite(text, groesse, fett)) / 2, grundlinie, groesse, text, fett);

    public void Flaeche(float x, float y, float b, float h, Farbe f) =>
        _sb.Append($"{Fuell(f)} {F(x)} {F(Y(y + h))} {F(b)} {F(h)} re f\n");

    public void Rahmen(float x, float y, float b, float h, float staerke = 0.5f, Farbe? f = null) =>
        _sb.Append($"{Strich(f ?? Farbe.Schwarz)} {F(staerke)} w {F(x)} {F(Y(y + h))} {F(b)} {F(h)} re S\n");

    public void Linie(float x1, float y1, float x2, float y2, float staerke = 0.5f, Farbe? f = null) =>
        _sb.Append($"{Strich(f ?? Farbe.Schwarz)} {F(staerke)} w {F(x1)} {F(Y(y1))} m {F(x2)} {F(Y(y2))} l S\n");

    /// <summary>Unterstrichener Text (eigene Linie wie im Desktop-Druck).</summary>
    public void Unterstrichen(float x, float grundlinie, float groesse, string text, bool fett = false)
    {
        Text(x, grundlinie, groesse, text, fett);
        Linie(x, grundlinie + groesse * 0.15f, x + PdfDokument.TextBreite(text, groesse, fett), grundlinie + groesse * 0.15f, 0.45f);
    }

    internal static string Maskieren(string t)
    {
        var sb = new StringBuilder(t.Length + 8);
        foreach (var ch in t.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' '))
        {
            var code = PdfDokument.Kodierung.Byte(ch);
            if (ch is '(' or ')' or '\\') sb.Append('\\').Append(ch);
            else if (code < 32 || code > 126) sb.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
            else sb.Append((char)code);
        }
        return sb.ToString();
    }
}
