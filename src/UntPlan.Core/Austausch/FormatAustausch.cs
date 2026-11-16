using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core.Model;

namespace UntPlan.Core.Austausch;

/// <summary>
/// Inhalt einer Formatdatei (Datei | Import/Export | Formate/Fenstergruppen): Stundenplanformate, Stammdatenformate und – von der
/// Oberfläche geliefert – Fensteranordnungen. Enthält keine Schuldaten.
/// </summary>
public sealed class Formatpaket
{
    public const string Kennung = "UntPlan-Formate";
    public string Art { get; set; } = Kennung;
    public int Version { get; set; } = 1;
    public List<StundenplanFormat> Stundenplanformate { get; set; } = new();
    public List<Ansicht> Stammdatenformate { get; set; } = new();
    /// <summary>Fensteranordnungen (Fenstergruppen) als JSON (Name → Anordnung), von der Desktop-App gefüllt.</summary>
    public JsonObject? Fenstergruppen { get; set; }
}

/// <summary>Export und Import von Formaten zwischen Schuldateien (Vorbild „Formate (gpf-) Export“ bzw. Import).</summary>
public static class FormatAustausch
{
    /// <summary>Dateiendung der Formatdateien.</summary>
    public const string Dateiendung = ".upf";

    /// <summary>Formatpaket aus den Daten erstellen; <paramref name="auswahl"/> null = alle Stundenplanformate.</summary>
    public static Formatpaket Erstellen(Schuldatei d, IEnumerable<StundenplanFormat>? auswahl, bool stammdatenformate, JsonObject? fenstergruppen = null)
    {
        var p = new Formatpaket { Fenstergruppen = fenstergruppen?.DeepClone() as JsonObject };
        foreach (var f in auswahl ?? d.Formate) p.Stundenplanformate.Add(Klonen(f));
        if (stammdatenformate)
            foreach (var a in d.Ansichten.Where(a => !a.Geschuetzt)) p.Stammdatenformate.Add(Klonen(a));
        return p;
    }

    public static string AlsJson(Formatpaket p) => JsonSerializer.Serialize(p, Dokument.JsonOptionen);

    /// <summary>
    /// Formate aus einer Formatdatei oder direkt aus einer Schuldatei (.up) lesen. Bei einer Schuldatei werden nur die Formate übernommen,
    /// die Fensteranordnungen stammen aus deren Ansichtseinstellungen.
    /// </summary>
    public static Formatpaket Lesen(string json)
    {
        var knoten = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Die Datei enthält keine Formate.");
        if (knoten["Art"]?.GetValue<string>() == Formatpaket.Kennung)
            return JsonSerializer.Deserialize<Formatpaket>(json, Dokument.JsonOptionen) ?? new();
        if (knoten.ContainsKey("Formate") || knoten.ContainsKey("Ansichten"))
        {
            var d = JsonSerializer.Deserialize<Schuldatei>(json, Dokument.JsonOptionen) ?? throw new InvalidDataException("Die Datei ist keine Schuldatei.");
            var p = Erstellen(d, null, true);
            if (d.Ansichtseinstellungen?["Fensteranordnungen"] is JsonObject fa) p.Fenstergruppen = (JsonObject)fa.DeepClone();
            return p;
        }
        throw new InvalidDataException("Die Datei enthält keine UntPlan-Formate.");
    }

    /// <summary>
    /// Formate in die Daten übernehmen. Gleichnamige Formate derselben Planart (bzw. desselben Fensters) werden ersetzt.
    /// <paramref name="namen"/> schränkt die Stundenplanformate ein (null = alle). Liefert (neu, ersetzt).
    /// </summary>
    public static (int Neu, int Ersetzt) Einspielen(Schuldatei d, Formatpaket p, IEnumerable<string>? namen, bool stammdatenformate)
    {
        var filter = namen?.ToHashSet(StringComparer.Ordinal);
        int neu = 0, ersetzt = 0;
        foreach (var f in p.Stundenplanformate.Where(f => filter is null || filter.Contains(f.Name)))
        {
            var kopie = Klonen(f);
            var i = d.Formate.FindIndex(x => x.Typ == f.Typ && x.Name == f.Name);
            if (i >= 0)
            {
                // Standard und Menü bleiben wie in der Zieldatei, damit pro Planart genau ein Standardformat gilt
                kopie.Standard = d.Formate[i].Standard;
                kopie.ImMenue = d.Formate[i].ImMenue;
                d.Formate[i] = kopie;
                ersetzt++;
            }
            else
            {
                kopie.Standard = false;
                d.Formate.Add(kopie);
                neu++;
            }
        }
        if (stammdatenformate)
            foreach (var a in p.Stammdatenformate)
            {
                var kopie = Klonen(a);
                var i = d.Ansichten.FindIndex(x => x.Fenster == a.Fenster && x.Name == a.Name);
                if (i >= 0 && d.Ansichten[i].Geschuetzt) continue;
                if (i >= 0) { kopie.Standard = d.Ansichten[i].Standard; d.Ansichten[i] = kopie; ersetzt++; }
                else { kopie.Standard = false; d.Ansichten.Add(kopie); neu++; }
            }
        return (neu, ersetzt);
    }

    private static T Klonen<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Dokument.JsonOptionen), Dokument.JsonOptionen)!;
}
