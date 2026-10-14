using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Hält das zuletzt von UntPlan übertragene Paket (Schema untplanweb/1) im Speicher und in der Datenbank.
/// Teilpakete ersetzen nur ihre Teile (WebTeile.Felder), der Rest bleibt vom bisherigen Stand.
/// </summary>
public sealed class Paketdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly object _sperre = new();
    private Stand? _stand;

    public Paketdienst(Db db, Uhr uhr)
    {
        _db = db;
        _uhr = uhr;
        var z = _db.Einzel("SELECT json, revision, empfangen FROM paket WHERE id = 1", r => new { Json = r.Text("json"), Empfangen = r.Text("empfangen") });
        if (z != null)
        {
            try { _stand = new Stand(JsonSerializer.Deserialize<WebPaket>(z.Json, UntPlanWebSchema.Json)!, z.Empfangen); }
            catch (JsonException) { _stand = null; }
        }
    }

    /// <summary>Aktuelles Paket mit Nachschlagetabellen (null = noch nichts übertragen).</summary>
    public Stand? Aktuell => _stand;

    public int Revision => _stand?.Paket.Revision ?? 0;

    public sealed class VeralteteRevisionException(int revision, int aktuell) : Exception($"Veraltete Revision {revision} (aktuell {aktuell})")
    {
        public int Aktuell { get; } = aktuell;
    }

    /// <summary>Paket übernehmen. Wirft bei veralteter Revision; Teilpakete werden mit dem bisherigen Stand zusammengeführt.</summary>
    public Stand Speichern(WebPaket paket, bool revisionPruefen = true)
    {
        if (paket.Schema != UntPlanWebSchema.Schema) throw new InvalidDataException($"Schema {UntPlanWebSchema.Schema} erwartet.");
        lock (_sperre)
        {
            if (revisionPruefen && _stand != null && paket.Revision <= _stand.Paket.Revision) throw new VeralteteRevisionException(paket.Revision, _stand.Paket.Revision);
            var json = JsonSerializer.Serialize(paket, UntPlanWebSchema.Json);
            if (paket.Teile.Count > 0 && _stand != null && JsonNode.Parse(json) is JsonObject neu &&
                JsonNode.Parse(JsonSerializer.Serialize(_stand.Paket, UntPlanWebSchema.Json)) is JsonObject alt)
            {
                foreach (var (teil, felder) in WebTeile.Felder)
                {
                    if (paket.Teile.Contains(teil)) continue;
                    foreach (var f in felder) neu[f] = alt[f]?.DeepClone();
                }
                neu["teile"] = new JsonArray();
                json = neu.ToJsonString();
                paket = JsonSerializer.Deserialize<WebPaket>(json, UntPlanWebSchema.Json)!;
            }
            var empfangen = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");
            _db.Ausfuehren("""
                INSERT INTO paket (id, revision, json, empfangen) VALUES (1, @r, @j, @e)
                ON CONFLICT(id) DO UPDATE SET revision = excluded.revision, json = excluded.json, empfangen = excluded.empfangen
                """, Db.P("@r", paket.Revision), Db.P("@j", json), Db.P("@e", empfangen));
            _stand = new Stand(paket, empfangen);
            return _stand;
        }
    }

    /// <summary>
    /// Beispielpaket laden (UNTPLANWEB_DEMO=1). Alle Datumsangaben werden um ganze Wochen verschoben, sodass die
    /// Beispielwoche die aktuelle Woche ist – Wochentage bleiben dabei erhalten.
    /// </summary>
    public bool DemoLaden(string datei)
    {
        if (_stand != null || !File.Exists(datei)) return false;
        var knoten = JsonNode.Parse(File.ReadAllText(datei))!;
        var erste = knoten["tage"]?.AsArray().Select(t => DateOnly.Parse(t!["datum"]!.GetValue<string>())).DefaultIfEmpty(_uhr.Heute).Min() ?? _uhr.Heute;
        var montagDemo = erste.AddDays(-(((int)erste.DayOfWeek + 6) % 7));
        var heute = _uhr.Heute;
        var montagHeute = heute.AddDays(-(((int)heute.DayOfWeek + 6) % 7));
        var tage = montagHeute.DayNumber - montagDemo.DayNumber;
        Verschieben(knoten, tage);
        var paket = knoten.Deserialize<WebPaket>(UntPlanWebSchema.Json)!;
        Speichern(paket with { Revision = 1, Erzeugt = _uhr.Jetzt }, false);
        return true;
    }

    private static readonly Regex Datum = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static void Verschieben(JsonNode? n, int tage)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var (k, v) in o.ToList())
                    if (v is JsonValue w && w.TryGetValue<string>(out var s) && Datum.IsMatch(s)) o[k] = DateOnly.Parse(s).AddDays(tage).ToString("yyyy-MM-dd");
                    else Verschieben(v, tage);
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                    if (a[i] is JsonValue w && w.TryGetValue<string>(out var s) && Datum.IsMatch(s)) a[i] = DateOnly.Parse(s).AddDays(tage).ToString("yyyy-MM-dd");
                    else Verschieben(a[i], tage);
                break;
        }
    }
}

/// <summary>Paket mit Nachschlagetabellen (unveränderlich; bei jedem Empfang neu aufgebaut).</summary>
public sealed class Stand
{
    public WebPaket Paket { get; }
    public string Empfangen { get; }
    public Dictionary<string, WebTag> Tage { get; }
    public Dictionary<int, WebVertretung> Vertretungen { get; }
    public Dictionary<string, WebElement> Faecher { get; }
    public Dictionary<string, WebKlasse> Klassen { get; }
    public Dictionary<string, WebLehrer> Lehrer { get; }
    public Dictionary<string, WebElement> Raeume { get; }
    public Dictionary<string, WebElement> Gaenge { get; }
    public DateOnly SchuljahrVon { get; }
    public DateOnly SchuljahrBis { get; }

    public Stand(WebPaket p, string empfangen)
    {
        Paket = p;
        Empfangen = empfangen;
        Tage = p.Tage.GroupBy(t => t.Datum).ToDictionary(g => g.Key, g => g.First());
        Vertretungen = p.Vertretungen.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var ci = StringComparer.OrdinalIgnoreCase;
        Faecher = p.Elemente.Faecher.GroupBy(x => x.Kurz, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        Klassen = p.Elemente.Klassen.GroupBy(x => x.Kurz, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        Lehrer = p.Elemente.Lehrer.GroupBy(x => x.Kurz, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        Raeume = p.Elemente.Raeume.GroupBy(x => x.Kurz, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        Gaenge = p.Elemente.Gaenge.GroupBy(x => x.Kurz, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        SchuljahrVon = DateOnly.TryParse(p.Schule.SchuljahrVon, out var v) ? v : DateOnly.MinValue;
        SchuljahrBis = DateOnly.TryParse(p.Schule.SchuljahrBis, out var b) ? b : DateOnly.MaxValue;
    }

    public static DateOnly D(string s) => DateOnly.Parse(s);
}
