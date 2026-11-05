using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App;

/// <summary>
/// Eigene Vertretungsansicht im früheren Speicherformat (nur noch zum Lesen älterer Einstellungen bzw. Schuldateien). Beim Laden
/// werden solche Ansichten als eigene <see cref="Vertretungsformat"/>e übernommen; die Einstellungen der Ansicht (Felder, angezeigte
/// Vertretungsarten, Sortierung), die bisher unter dem Kurznamen gespeichert waren, ziehen auf den Langnamen des Formats um.
/// </summary>
public sealed class EigeneVertretungsansicht
{
    /// <summary>Kurzname („Leli“) – früher Schlüssel der Ansicht.</summary>
    public string Name { get; set; } = "";
    /// <summary>Langname („Vertretungen Lehrende“).</summary>
    public string Langname { get; set; } = "";
    /// <summary>Mitgeliefertes Format, dessen Art die Ansicht übernahm („Lehrer - Standard“ …).</summary>
    public string Basis { get; set; } = "Klasse - Standard";
}

/// <summary>Sortierkriterium eines Vertretungsformats (Dialog „Sortierkriterien“, Handbuch Kap. 13.1.1.3).</summary>
public sealed class VertretungsSortierkriterium
{
    /// <summary>Spaltenname (interner Schlüssel, z. B. „Nr“, „Std“).</summary>
    public string Feld { get; set; } = "";
    public bool Absteigend { get; set; }
}

public sealed partial class Benutzereinstellungen
{
    /// <summary>
    /// Früheres Speicherformat eigener Vertretungsansichten: wird beim Laden in <see cref="Vertretungsformate"/> übernommen und danach
    /// geleert (null wird nicht mehr geschrieben) – einziger Speicherort eigener Ansichten sind die Vertretungsformate.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EigeneVertretungsansicht>? EigeneVertretungsansichten { get; set; }

    /// <summary>Vertretungsfenster: gespeicherte Sortierung je Vertretungsformat (Schlüssel = Langname; bis zu fünf Kriterien, „Sortieren“).</summary>
    public Dictionary<string, List<VertretungsSortierkriterium>> VertretungsSortierung { get; set; } = new();

    /// <summary>Eigene Ansichten im früheren Speicherformat als Vertretungsformate übernehmen (Einstellungen wandern vom Kurz- auf den Langnamen).</summary>
    internal void EigeneAnsichtenUebernehmen()
    {
        var alt = EigeneVertretungsansichten;
        EigeneVertretungsansichten = null;
        if (alt is not { Count: > 0 }) return;
        var liste = Core.Vertretungsplanung.Vertretungsformate.Ergaenzen(Vertretungsformate);
        foreach (var a in alt)
        {
            var name = (a.Name ?? "").Trim();
            if (name.Length == 0) continue;
            // Kurzname, der zugleich Langname eines bestehenden Formats ist, gehört diesem (dessen Einstellungen bleiben unberührt)
            var nameBelegt = liste.Any(f => f.Langname == name);
            var f = Core.Vertretungsplanung.Vertretungsformate.AlteAnsichtUebernehmen(liste, name, a.Langname ?? "", a.Basis);
            if (f is null || nameBelegt || f.Langname == name) continue;
            FormatEinstellungenUmbenennen(name, f.Langname, ueberschreiben: false);
            if (Vertretungsansicht == name) Vertretungsansicht = f.Langname;
        }
    }

    /// <summary>
    /// Ansichtseinstellungen aus einer Schuldatei übernehmen (fehlende Felder behalten den lokalen Wert); eigene Ansichten älterer
    /// Dateien werden dabei als Vertretungsformate übernommen.
    /// </summary>
    public int AnsichtUebernehmen(JsonObject? ansicht)
    {
        var n = Core.Ansichtsdaten.Uebernehmen(this, ansicht, AnsichtsFelder);
        if (ansicht is not null && ansicht.TryGetPropertyValue(nameof(EigeneVertretungsansichten), out var alt) && alt is JsonArray)
        {
            try { EigeneVertretungsansichten = alt.Deserialize<List<EigeneVertretungsansicht>>(); }
            catch (JsonException) { EigeneVertretungsansichten = null; }
            EigeneAnsichtenUebernehmen();
        }
        return n;
    }

    /// <summary>Einstellungen eines Formats (Felder der Ansicht, ausgeblendete Vertretungsarten, Sortierkriterien) auf ein neues Format kopieren.</summary>
    internal void FormatEinstellungenKopieren(string von, string nach)
    {
        if (VertretungsFelder.TryGetValue(von, out var felder)) VertretungsFelder[nach] = new Dictionary<string, string>(felder);
        if (VertretungsArtenAus.TryGetValue(von, out var aus)) VertretungsArtenAus[nach] = aus.ToList();
        if (VertretungsSortierung.TryGetValue(von, out var sort))
            VertretungsSortierung[nach] = sort.Select(k => new VertretungsSortierkriterium { Feld = k.Feld, Absteigend = k.Absteigend }).ToList();
    }

    /// <summary>Einstellungen eines Formats beim Umbenennen (Langname) mitnehmen.</summary>
    internal void FormatEinstellungenUmbenennen(string alt, string neu, bool ueberschreiben = true)
    {
        Umbenennen(VertretungsFelder, alt, neu, ueberschreiben);
        Umbenennen(VertretungsArtenAus, alt, neu, ueberschreiben);
        Umbenennen(VertretungsSortierung, alt, neu, ueberschreiben);
    }

    /// <summary>Einstellungen eines gelöschten Formats entfernen.</summary>
    internal void FormatEinstellungenEntfernen(string langname)
    {
        VertretungsFelder.Remove(langname);
        VertretungsArtenAus.Remove(langname);
        VertretungsSortierung.Remove(langname);
    }

    private static void Umbenennen<T>(Dictionary<string, T> d, string alt, string neu, bool ueberschreiben)
    {
        if (alt == neu || (!ueberschreiben && d.ContainsKey(neu)) || !d.Remove(alt, out var wert)) return;
        d[neu] = wert;
    }
}
