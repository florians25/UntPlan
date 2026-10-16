using System.Text;

namespace UntPlan.Core.Austausch;

public static class Csv
{
    public static List<string[]> Lesen(string text, char? trenner = null)
    {
        text = text.TrimStart('﻿');
        var erste = text.Split('\n')[0];
        var t = trenner ?? new[] { ';', ',', '\t' }.OrderByDescending(c => erste.Count(x => x == c)).First();
        var rows = new List<string[]>();
        var row = new List<string>();
        var feld = new StringBuilder();
        var q = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (q)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                    else q = false;
                }
                else feld.Append(ch);
            }
            else if (ch == '"') q = true;
            else if (ch == t) { row.Add(feld.ToString().Trim()); feld.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(feld.ToString().Trim());
                feld.Clear();
                if (row.Any(c => c.Length > 0)) rows.Add(row.ToArray());
                row = new List<string>();
            }
            else feld.Append(ch);
        }
        row.Add(feld.ToString().Trim());
        if (row.Any(c => c.Length > 0)) rows.Add(row.ToArray());
        return rows;
    }

    public static string Schreiben(IEnumerable<IEnumerable<object?>> zeilen, char trenner = ';', bool bom = true)
    {
        var sb = new StringBuilder();
        if (bom) sb.Append('﻿');
        foreach (var z in zeilen)
        {
            sb.Append(string.Join(trenner, z.Select(v =>
            {
                var s = Convert.ToString(v, Model.Format.De) ?? "";
                return s.IndexOfAny(new[] { trenner, '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            })));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Liest Dateien aus Schild-NRW/Vorbild, die häufig Windows-1252 kodiert sind.</summary>
    public static string DateiLesen(string pfad)
    {
        var bytes = File.ReadAllBytes(pfad);
        var utf8 = new UTF8Encoding(false, true);
        try { return utf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
