using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UntPlan.Core;

/// <summary>
/// Ansichtseinstellungen in der Schuldatei (<see cref="Model.Schuldatei.Ansichtseinstellungen"/>): Die Oberfläche legt fest,
/// welche Eigenschaften ihrer Einstellungen zur Ansicht gehören; diese werden als JSON-Objekt mit der Datei gespeichert
/// und beim Laden feldweise zurückgeschrieben. Felder, die in der Datei fehlen (ältere Dateien), behalten ihren
/// bisherigen Wert – so werden die bisher lokal gespeicherten Einstellungen übernommen (Migration).
/// </summary>
public static class Ansichtsdaten
{
    private static readonly JsonSerializerOptions StandardOptionen = new();

    /// <summary>Die genannten Eigenschaften eines Einstellungsobjekts als JSON-Objekt (tiefe Kopie).</summary>
    public static JsonObject Herausloesen<T>(T einstellungen, IEnumerable<string> felder, JsonSerializerOptions? optionen = null) where T : class
    {
        optionen ??= StandardOptionen;
        var res = new JsonObject();
        foreach (var feld in felder)
        {
            if (Eigenschaft(typeof(T), feld) is not { } p) continue;
            res[feld] = JsonSerializer.SerializeToNode(p.GetValue(einstellungen), p.PropertyType, optionen);
        }
        return res;
    }

    /// <summary>
    /// Schreibt die in <paramref name="ansicht"/> enthaltenen Felder in das Einstellungsobjekt zurück. Fehlende oder
    /// unlesbare Felder bleiben unverändert. Liefert die Anzahl übernommener Felder.
    /// </summary>
    public static int Uebernehmen<T>(T ziel, JsonObject? ansicht, IEnumerable<string> felder, JsonSerializerOptions? optionen = null) where T : class
    {
        if (ansicht is null) return 0;
        optionen ??= StandardOptionen;
        var n = 0;
        foreach (var feld in felder)
        {
            if (!ansicht.TryGetPropertyValue(feld, out var knoten) || knoten is null) continue;
            if (Eigenschaft(typeof(T), feld) is not { CanWrite: true } p) continue;
            try
            {
                var wert = knoten.Deserialize(p.PropertyType, optionen);
                if (wert is null) continue;
                p.SetValue(ziel, wert);
                n++;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                // beschädigtes Feld: bisherigen Wert behalten
            }
        }
        return n;
    }

    /// <summary>Inhaltlicher Vergleich zweier Ansichten.</summary>
    public static bool Gleich(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    private static PropertyInfo? Eigenschaft(Type t, string name) => t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
}
