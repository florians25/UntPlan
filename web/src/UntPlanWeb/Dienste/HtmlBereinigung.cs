using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Bereinigt formatierte Tagestexte (HTML aus UntPlan) für die Anzeige im Browser. Erlaubt sind nur
/// <c>b</c>, <c>i</c>, <c>u</c>, <c>br</c>, <c>p</c> (ohne Attribute) und <c>span</c> mit <c>style="color:…"</c>
/// (Hex-Farbe, rgb() oder Farbname). Alles andere – andere Elemente, Attribute, Kommentare, Skripte – wird als Text
/// escaped; offene Elemente werden am Ende geschlossen.
/// </summary>
public static partial class HtmlBereinigung
{
    private static readonly HashSet<string> Erlaubt = new(StringComparer.OrdinalIgnoreCase) { "b", "i", "u", "br", "p", "span" };

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)\b([^<>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex TagMuster();

    [GeneratedRegex(@"^\s*color\s*:\s*(#[0-9a-fA-F]{3}|#[0-9a-fA-F]{6}|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)|[a-zA-Z]{3,20})\s*;?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FarbMuster();

    [GeneratedRegex(@"\bstyle\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StilMuster();

    /// <summary>Text außerhalb von Tags: Entitäten auflösen und neu escapen (keine doppelte Kodierung, keine rohen Zeichen).</summary>
    private static string Text(string s) => WebUtility.HtmlEncode(WebUtility.HtmlDecode(s));

    public static string Bereinigen(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var sb = new StringBuilder(html.Length + 16);
        var offen = new Stack<string>();
        var pos = 0;
        foreach (Match m in TagMuster().Matches(html))
        {
            sb.Append(Text(html[pos..m.Index]));
            pos = m.Index + m.Length;
            var schliessend = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToLowerInvariant();
            if (!Erlaubt.Contains(name))
            {
                sb.Append(WebUtility.HtmlEncode(m.Value));
                continue;
            }
            if (name == "br")
            {
                if (!schliessend) sb.Append("<br>");
                continue;
            }
            if (!schliessend)
            {
                if (name == "span")
                {
                    var stil = StilMuster().Match(m.Groups[3].Value);
                    var wert = stil.Success ? (stil.Groups[2].Success && stil.Groups[2].Length > 0 ? stil.Groups[2].Value : stil.Groups[3].Value) : "";
                    var farbe = FarbMuster().Match(wert);
                    sb.Append(farbe.Success ? $"<span style=\"color:{farbe.Groups[1].Value.Replace(" ", "")}\">" : "<span>");
                }
                else sb.Append('<').Append(name).Append('>');
                offen.Push(name);
                continue;
            }
            // schließendes Tag: nur, wenn es geöffnet wurde (dazwischen offene Elemente mit schließen)
            if (!offen.Contains(name)) continue;
            while (offen.Count > 0)
            {
                var o = offen.Pop();
                sb.Append("</").Append(o).Append('>');
                if (o == name) break;
            }
        }
        sb.Append(Text(html[pos..]));
        while (offen.Count > 0) sb.Append("</").Append(offen.Pop()).Append('>');
        return sb.ToString();
    }

    /// <summary>Klartext aus (bereinigtem) HTML, z. B. als Ersatz, wenn ein Paket nur TextHtml enthält.</summary>
    public static string AlsText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var t = Regex.Replace(html, @"<\s*br\s*/?>|</\s*p\s*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]*>", "");
        return WebUtility.HtmlDecode(t).Trim();
    }
}
