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

    /// <summary>Nach jedem gespeicherten Paket (vorheriger Stand, neuer Stand) – z. B. Sofort-Benachrichtigungen; darf nicht werfen.</summary>
    public event Action<Stand?, Stand>? Gespeichert;

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
        ZusatzferienLaden();
        SchuelerLaden();
    }

    /// <summary>
    /// Schülerliste aus UntPlan mit den in UntPlanWeb gepflegten Änderungen (Schülerverwaltung) zusammenführen:
    /// geänderte Namen/Klassen ersetzen den Eintrag aus UntPlan, gelöschte entfallen, neu angelegte kommen hinzu.
    /// </summary>
    public void SchuelerLaden()
    {
        if (_stand is null) return;
        var aenderungen = _db.Liste("SELECT id, vorname, name, klasse, geloescht, eigen FROM schueler_daten",
            r => (Id: r.Zahl("id"), Schueler: new WebSchueler(r.Zahl("id"), r.Text("vorname"), r.Text("name"), r.Text("klasse")), Geloescht: r.Wahr("geloescht"), Eigen: r.Wahr("eigen")))
            .ToDictionary(x => x.Id);
        var liste = new List<WebSchueler>();
        var gesehen = new HashSet<int>();
        // Quelle „Web-Verwaltung“ (Administration → Import): der Export aus UntPlan überschreibt die Schüler*innen nicht
        var web = SchuelerImport.WebVerwaltung(_db);
        foreach (var x in web ? Enumerable.Empty<WebSchueler>() : _stand.Paket.Elemente.Schueler)
        {
            if (!gesehen.Add(x.Id)) continue;
            if (!aenderungen.TryGetValue(x.Id, out var a)) liste.Add(x);
            else if (!a.Geloescht) liste.Add(a.Schueler);
        }
        // Quelle „Export“: aus dem Export entfernte Schüler*innen entfallen; in UntPlanWeb angelegte (eigen) bleiben
        liste.AddRange(aenderungen.Values.Where(a => !a.Geloescht && !gesehen.Contains(a.Id) && (web || a.Eigen || a.Id >= Schuelerverwaltung.ErsteEigeneId)).Select(a => a.Schueler));
        _stand.Schueler = liste;
        ZuordnungenLaden();
    }

    /// <summary>
    /// Klassenwechsel ab Datum und Schülergruppen je Unterricht (Tabellen klassenwechsel, schuelergruppen) in den Stand übernehmen;
    /// die Schülerliste erhält die Klasse von heute (siehe <see cref="Klassenzugehoerigkeit"/>).
    /// </summary>
    public void ZuordnungenLaden()
    {
        if (_stand is null) return;
        _stand.Zuordnungen = Zuordnungen.Laden(_db, _uhr.Heute);
        if (_stand.Zuordnungen.Wechsel.Count > 0)
            _stand.Schueler = _stand.Schueler.Select(x => x with { Klasse = Klassenzugehoerigkeit.KlasseAm(_stand, x, _uhr.Heute) }).ToList();
    }

    /// <summary>In UntPlanWeb gepflegte freie Tage (Administration → Freie Tage &amp; Ferien) in den aktuellen Stand übernehmen.</summary>
    public void ZusatzferienLaden()
    {
        if (_stand is null) return;
        _stand.Zusatzferien = _db.Liste("SELECT von, bis, art, name FROM freie_tage ORDER BY von",
            r => new WebFerien(r.Text("name"), r.Text("von"), r.Text("bis"), r.Text("art") == "feiertag"));
    }

    /// <summary>Aktuelles Paket mit Nachschlagetabellen (null = noch nichts übertragen).</summary>
    public Stand? Aktuell
    {
        get
        {
            // Klassenwechsel ab Datum: an einem neuen Tag gilt ggf. eine andere Klasse
            if (_stand is { } s && s.Zuordnungen.Tag != _uhr.Heute && s.Zuordnungen.Wechsel.Count > 0) lock (_sperre) ZuordnungenLaden();
            return _stand;
        }
    }

    public int Revision => _stand?.Paket.Revision ?? 0;

    public sealed class VeralteteRevisionException(int revision, int aktuell) : Exception($"Veraltete Revision {revision} (aktuell {aktuell})")
    {
        public int Aktuell { get; } = aktuell;
    }

    /// <summary>Paket übernehmen. Wirft bei veralteter Revision; Teilpakete werden mit dem bisherigen Stand zusammengeführt.</summary>
    public Stand Speichern(WebPaket paket, bool revisionPruefen = true)
    {
        var (alt, neu) = SpeichernIntern(paket, revisionPruefen);
        try { Gespeichert?.Invoke(alt, neu); } catch { /* Benachrichtigungen dürfen den Import nie scheitern lassen */ }
        return neu;
    }

    private (Stand? Alt, Stand Neu) SpeichernIntern(WebPaket paket, bool revisionPruefen)
    {
        if (paket.Schema != UntPlanWebSchema.Schema) throw new InvalidDataException($"Schema {UntPlanWebSchema.Schema} erwartet.");
        lock (_sperre)
        {
            var vorher = _stand;
            var teile = paket.Teile.ToList();
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
            // Tagesaktuelle Daten ersetzen nur den übertragenen Zeitraum: wird z. B. erst das ganze Schuljahr und danach
            // „ab heute“ übertragen, bleiben die vergangenen Tage aus der früheren Übertragung erhalten
            if (_stand != null && (paket.Teile.Count == 0 || paket.Teile.Contains(WebTeile.Vertretungen)))
            {
                paket = ZeitraumZusammenfuehren(_stand.Paket, paket);
                json = JsonSerializer.Serialize(paket, UntPlanWebSchema.Json);
            }
            var empfangen = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");
            _db.Ausfuehren("""
                INSERT INTO paket (id, revision, json, empfangen) VALUES (1, @r, @j, @e)
                ON CONFLICT(id) DO UPDATE SET revision = excluded.revision, json = excluded.json, empfangen = excluded.empfangen
                """, Db.P("@r", paket.Revision), Db.P("@j", json), Db.P("@e", empfangen));
            _stand = new Stand(paket, empfangen);
            ZusatzferienLaden();
            SchuelerLaden();
            // Übertragungsverlauf (Administration → Stammdaten → Stundenplan-Versionen)
            var tageVon = paket.Tage.Select(t => t.Datum).DefaultIfEmpty("").Min() ?? "";
            var tageBis = paket.Tage.Select(t => t.Datum).DefaultIfEmpty("").Max() ?? "";
            _db.Ausfuehren("""
                INSERT INTO paket_verlauf (revision, empfangen, programmversion, klassen, lehrer, raeume, tage, vertretungen, von, bis, teile)
                VALUES (@r, @e, @p, @k, @l, @ra, @t, @v, @vo, @bi, @te)
                """, Db.P("@te", string.Join(",", teile)), Db.P("@r", paket.Revision), Db.P("@e", empfangen), Db.P("@p", paket.Programmversion), Db.P("@k", paket.Elemente.Klassen.Count),
                Db.P("@l", paket.Elemente.Lehrer.Count), Db.P("@ra", paket.Elemente.Raeume.Count), Db.P("@t", paket.Tage.Count), Db.P("@v", paket.Vertretungen.Count),
                Db.P("@vo", tageVon), Db.P("@bi", tageBis));
            return (vorher, _stand);
        }
    }

    /// <summary>
    /// Tagesaktuelle Daten (Tage, Vertretungen, Absenzen, Termine, Tagestexte) des neuen Pakets gelten nur für seinen Zeitraum
    /// <see cref="WebPaket.Von"/>–<see cref="WebPaket.Bis"/>; Einträge des bisherigen Stands, die ganz davor oder danach liegen,
    /// bleiben erhalten. Pakete ohne Zeitraum ersetzen alles (ältere Programmversionen).
    /// </summary>
    public static WebPaket ZeitraumZusammenfuehren(WebPaket alt, WebPaket neu)
    {
        if (string.IsNullOrEmpty(neu.Von) || string.IsNullOrEmpty(neu.Bis)) return neu;
        var von = neu.Von;
        var bis = neu.Bis;
        bool Ausserhalb(string a, string b) => string.CompareOrdinal(b, von) < 0 || string.CompareOrdinal(a, bis) > 0;
        static string Ende(WebTagestext t) => DateOnly.TryParse(t.Datum, out var d) ? d.AddDays(Math.Max(1, t.GueltigTage) - 1).ToString("yyyy-MM-dd") : t.Datum;
        var tage = alt.Tage.Where(t => Ausserhalb(t.Datum, t.Datum)).Concat(neu.Tage).OrderBy(t => t.Datum, StringComparer.Ordinal).ToList();
        var vertretungen = alt.Vertretungen.Where(v => Ausserhalb(v.Datum, v.Datum)).Concat(neu.Vertretungen).ToList();
        var absenzen = alt.Absenzen.Where(a => Ausserhalb(a.Von, a.Bis.Length > 0 ? a.Bis : a.Von)).Concat(neu.Absenzen).ToList();
        var termine = alt.Termine.Where(t => Ausserhalb(t.Von, t.Bis.Length > 0 ? t.Bis : t.Von)).Concat(neu.Termine).ToList();
        var texte = alt.Tagestexte.Where(t => Ausserhalb(t.Datum, Ende(t))).Concat(neu.Tagestexte).ToList();
        string Min(string a, string b) => string.IsNullOrEmpty(a) ? b : string.CompareOrdinal(a, b) <= 0 ? a : b;
        string Max(string a, string b) => string.IsNullOrEmpty(a) ? b : string.CompareOrdinal(a, b) >= 0 ? a : b;
        return neu with
        {
            Tage = tage, Vertretungen = vertretungen, Absenzen = absenzen, Termine = termine, Tagestexte = texte,
            Von = Min(alt.Von, von), Bis = Max(alt.Bis, bis),
        };
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
    public Dictionary<string, WebRaum> Raeume { get; }
    public Dictionary<string, WebElement> Gaenge { get; }
    /// <summary>Unterrichte aller Perioden nach Nummer (spätere Periode gewinnt), z. B. für die Schülergruppe einer Stunde.</summary>
    public Dictionary<int, WebUnterricht> Unterricht { get; }
    public DateOnly SchuljahrVon { get; }
    public DateOnly SchuljahrBis { get; }
    /// <summary>Zusätzliche freie Tage aus UntPlanWeb (werden wie Ferien bzw. Feiertage behandelt).</summary>
    public List<WebFerien> Zusatzferien { get; set; } = new();
    /// <summary>Schüler*innen: Liste aus UntPlan mit den Änderungen der Schülerverwaltung in UntPlanWeb (Klasse von heute).</summary>
    public List<WebSchueler> Schueler { get; set; }
    /// <summary>Klassenwechsel ab Datum und Schülergruppen je Unterricht aus UntPlanWeb.</summary>
    public Zuordnungen Zuordnungen { get; set; } = Zuordnungen.Leer;

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
        Schueler = p.Elemente.Schueler;
        Unterricht = new Dictionary<int, WebUnterricht>();
        foreach (var per in p.Perioden.OrderBy(x => x.Von, StringComparer.Ordinal))
            foreach (var u in per.Unterricht) Unterricht[u.Id] = u;
    }

    public static DateOnly D(string s) => DateOnly.Parse(s);
}
