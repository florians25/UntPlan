using System.Globalization;

namespace UntPlanMobile.Api;

/// <summary>Parameter aus Adresse und Formular (die Oberfläche schickt POST-Daten als Formular, wie an die PHP-Endpunkte).</summary>
public sealed class Parameter
{
    private readonly Dictionary<string, string> _werte = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<Parameter> LesenAsync(HttpContext c)
    {
        var p = new Parameter();
        foreach (var (k, v) in c.Request.Query) p._werte[k] = v.ToString();
        if (!HttpMethods.IsGet(c.Request.Method) && c.Request.HasFormContentType)
        {
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            foreach (var (k, v) in form) p._werte[k] = v.ToString();
        }
        return p;
    }

    public string this[string name] => _werte.TryGetValue(name, out var v) ? v : "";

    /// <summary>Wurde der Parameter mitgeschickt (auch leer)?</summary>
    public bool Hat(string name) => _werte.ContainsKey(name);

    public string Text(string name, string standard = "") => _werte.TryGetValue(name, out var v) && v.Length > 0 ? v : standard;

    public int? Zahl(string name) => int.TryParse(this[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) ? z : null;

    public DateOnly? Datum(string name) => DateOnly.TryParseExact(this[name], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
