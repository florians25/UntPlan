using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UntPlan.Core.Austausch;

/// <summary>
/// Formatierte Tagestexte für den Export (UntPlanWeb): bereinigtes HTML-Subset mit &lt;b&gt;, &lt;i&gt;, &lt;u&gt;,
/// &lt;span style="color:…"&gt;, &lt;br&gt; und &lt;p&gt;. Alles andere wird entfernt (Inhalt von script/style ganz),
/// Text wird maskiert und offene Tags werden geschlossen – das Ergebnis ist sicher zum Einbetten.
/// </summary>
public static partial class TagestextHtml
{
    private static readonly HashSet<string> Erlaubt = new(StringComparer.Ordinal) { "b", "i", "u", "span", "br", "p" };
    private static readonly HashSet<string> Verwerfen = new(StringComparer.Ordinal) { "script", "style", "iframe", "object", "embed", "template", "noscript", "head", "title" };

    [GeneratedRegex(@"<!--.*?-->|<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>", RegexOptions.Singleline)]
    private static partial Regex TagMuster();

    [GeneratedRegex(@"color\s*:\s*(#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3}|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)|[a-zA-Z]{3,20})\s*(;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FarbMuster();

    [GeneratedRegex(@"style\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex StilMuster();

    /// <summary>HTML auf das erlaubte Subset reduzieren.</summary>
    public static string Bereinigen(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var sb = new StringBuilder();
        var offen = new List<string>();
        var verwerfenBis = (string?)null;
        var pos = 0;
        foreach (Match m in TagMuster().Matches(html))
        {
            if (verwerfenBis is null) Text(sb, html[pos..m.Index]);
            pos = m.Index + m.Length;
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
            var schliessen = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToLowerInvariant();
            name = name switch { "strong" => "b", "em" => "i", "ins" => "u", "div" => "p", _ => name };
            if (verwerfenBis != null)
            {
                if (schliessen && name == verwerfenBis) verwerfenBis = null;
                continue;
            }
            if (Verwerfen.Contains(name))
            {
                if (!schliessen && !m.Groups[3].Value.TrimEnd().EndsWith('/')) verwerfenBis = name;
                continue;
            }
            if (!Erlaubt.Contains(name)) continue;
            if (name == "br")
            {
                if (!schliessen) sb.Append("<br>");
                continue;
            }
            if (schliessen)
            {
                var i = offen.LastIndexOf(name);
                if (i < 0) continue;
                for (var j = offen.Count - 1; j >= i; j--) sb.Append("</").Append(offen[j]).Append('>');
                offen.RemoveRange(i, offen.Count - i);
                continue;
            }
            if (name == "p" && offen.Contains("p"))
            {
                // <p> in <p> ist ungültig: vorherigen Absatz schließen
                var i = offen.LastIndexOf("p");
                for (var j = offen.Count - 1; j >= i; j--) sb.Append("</").Append(offen[j]).Append('>');
                offen.RemoveRange(i, offen.Count - i);
            }
            if (name == "span")
            {
                var farbe = Farbe(m.Groups[3].Value);
                if (farbe is null) { offen.Add("span"); sb.Append("<span>"); continue; }
                offen.Add("span");
                sb.Append("<span style=\"color:").Append(farbe).Append("\">");
                continue;
            }
            offen.Add(name);
            sb.Append('<').Append(name).Append('>');
        }
        if (verwerfenBis is null) Text(sb, html[pos..]);
        for (var j = offen.Count - 1; j >= 0; j--) sb.Append("</").Append(offen[j]).Append('>');
        return sb.ToString().Replace("<span></span>", "");
    }

    private static string? Farbe(string attribute)
    {
        var stil = StilMuster().Match(attribute);
        if (!stil.Success) return null;
        var wert = stil.Groups[2].Success && stil.Groups[2].Length > 0 ? stil.Groups[2].Value : stil.Groups[3].Value;
        var f = FarbMuster().Match(wert);
        if (!f.Success) return null;
        var farbe = f.Groups[1].Value.ToLowerInvariant().Replace(" ", "");
        if (farbe.StartsWith("rgb(", StringComparison.Ordinal))
        {
            var teile = farbe[4..^1].Split(',').Select(int.Parse).ToArray();
            if (teile.Any(x => x > 255)) return null;
            return $"#{teile[0]:x2}{teile[1]:x2}{teile[2]:x2}";
        }
        if (farbe.StartsWith('#')) return farbe;
        // benannte Farben: nur Buchstaben (bereits durch das Muster sichergestellt), bekannte CSS-Schlüsselwörter ausschließen
        return farbe is "inherit" or "initial" or "unset" or "currentcolor" or "expression" ? null : farbe;
    }

    private static void Text(StringBuilder sb, string roh)
    {
        if (roh.Length == 0) return;
        sb.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(roh)));
    }

    /// <summary>Unformatierter Text als HTML (Zeilen als &lt;br&gt;), z. B. für ältere Tagestexte ohne Formatierung.</summary>
    public static string AusText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var zeilen = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("<br>", zeilen.Select(WebUtility.HtmlEncode));
    }

    /// <summary>Reiner Text eines (bereinigten) HTML-Textes: Absätze und &lt;br&gt; werden zu Zeilenumbrüchen.</summary>
    public static string ZuText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = Regex.Replace(html, @"<br\s*/?>|</p>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]*>", "");
        return WebUtility.HtmlDecode(s).Trim('\n', ' ');
    }

    /// <summary>
    /// HTML eines Tagestextes für den Export: das gespeicherte formatierte HTML (bereinigt) oder der unformatierte Text.
    /// </summary>
    public static string FuerExport(Model.Tagestext t) =>
        !string.IsNullOrWhiteSpace(t.TextHtml) ? Bereinigen(t.TextHtml) : AusText(t.Text);
}
