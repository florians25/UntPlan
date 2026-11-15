using System.Text.Json;
using System.Text.Json.Serialization;

namespace UntPlanWeb.Dienste;

/// <summary>Element auf dem Stundenkärtchen: Art (klasse, lehrer, fach, raum, notiz) und Namensform (kurz, lang, langkurz = „Langname (Kurzname)“).</summary>
public sealed record PlanformatElement(string Art, string Name = "kurz");

/// <summary>
/// Stundenplanformat (Administration → Stundenplaneinstellungen): legt fest, was auf den Stundenkärtchen eines Plans steht und welche
/// Stunden bzw. Aktivitäten gezeigt und hervorgehoben werden. Ohne zugeordnetes Format gilt die Standarddarstellung.
/// </summary>
public sealed record Planformat
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Allgemein: Zugriff auf die Stundendetails, unangekündigte Prüfungen, individuelle Farben (Fachfarben).</summary>
    public bool Details { get; init; } = true;
    public bool UnangekuendigtePruefungen { get; init; }
    public bool Farben { get; init; } = true;
    /// <summary>Zeitraster-Einstellung: „raster“ (Zeitraster der Schule) bzw. „uhrzeit“ (tatsächliche Uhrzeit).</summary>
    public string Zeitraster { get; init; } = "raster";
    /// <summary>Stundenbeschriftung im Raster: Beginn-/Endzeit, Stundennummer, Stundenname.</summary>
    public bool Zeiten { get; init; } = true;
    public bool Stundennummer { get; init; } = true;
    public bool Stundenname { get; init; }
    /// <summary>Zusätzliche Information auf dem Stundenkärtchen: Beginn- und Endzeit, Benutzer (Info-Symbol).</summary>
    public bool KartenZeiten { get; init; }
    public bool Benutzer { get; init; }
    /// <summary>Externe Kalender anzeigen bzw. hervorheben (Plattform-Applikationen).</summary>
    public bool ExterneKalender { get; init; } = true;
    public bool ExterneHervorheben { get; init; } = true;
    /// <summary>Anordnung auf dem Stundenkärtchen (1. Element erforderlich, bis zu 5).</summary>
    public List<PlanformatElement> Elemente { get; init; } = new();
    /// <summary>Angezeigte Wochentage (0 = Montag … 6 = Sonntag); leer = alle Tage des Zeitrasters.</summary>
    public List<int> Tage { get; init; } = new();
    /// <summary>Uhrzeiten („HH:mm“), leer = alle Stunden des Zeitrasters.</summary>
    public string ZeitVon { get; init; } = "";
    public string ZeitBis { get; init; } = "";
    /// <summary>Aktivitäten anzeigen.</summary>
    public bool Entfall { get; init; } = true;
    public bool Bereitschaften { get; init; } = true;
    public bool Sprechstunden { get; init; } = true;
    public bool Buchungen { get; init; } = true;
    /// <summary>Hervorhebungen (farbige Karten, Labels, Symbole).</summary>
    public bool Aenderungen { get; init; } = true;
    public bool Pruefungen { get; init; } = true;
    public bool Entfaelle { get; init; } = true;
    public bool Symbole { get; init; } = true;
}

/// <summary>
/// Stundenplaneinstellungen „Allgemein“: Format je Plan (null = Standarddarstellung). Klassen-, Lehrkraft- und Raum-Stundenpläne gelten für alle;
/// das Format für Schüler-Stundenpläne gilt für Schüler*innen und Erziehungsberechtigte (eigener Plan und Klassenplan) und hat dort Vorrang.
/// </summary>
public sealed record PlanformatZuordnung
{
    public int? Klasse { get; init; }
    public int? Lehrer { get; init; }
    public int? Raum { get; init; }
    public int? Schueler { get; init; }
    /// <summary>Älterer Stand (ohne Schüler-Format): Formate nur für Schüler*innen und Eltern – wird beim Lesen in <see cref="Schueler"/> übernommen.</summary>
    public bool Kollegium { get; init; } = true;
    /// <summary>Links zu Videokonferenzen in den Stundendetails.</summary>
    public bool VideoLinks { get; init; } = true;
}

/// <summary>Stundenplanformate: Verwaltung (Einstellungen-Tabelle, JSON) und Anwendung auf den Wochenplan.</summary>
public sealed class Planformatdienst
{
    public static readonly string[] Arten = { "klasse", "lehrer", "fach", "raum", "notiz" };
    public static readonly string[] Namensformen = { "kurz", "lang", "langkurz" };
    private const string SchluesselFormate = "planformate";
    private const string SchluesselZuordnung = "planformate:zuordnung";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private readonly Einstellungen _einst;

    public Planformatdienst(Einstellungen einst) => _einst = einst;

    public List<Planformat> Formate()
    {
        var json = _einst.Lesen(SchluesselFormate);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<Planformat>>(json, Json) ?? new(); } catch (JsonException) { return new(); }
    }

    public PlanformatZuordnung Zuordnung()
    {
        var json = _einst.Lesen(SchluesselZuordnung);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<PlanformatZuordnung>(json, Json) ?? new(); } catch (JsonException) { return new(); }
    }

    public PlanformatZuordnung ZuordnungSpeichern(PlanformatZuordnung z)
    {
        var ids = Formate().Select(f => f.Id).ToHashSet();
        foreach (var id in new[] { z.Klasse, z.Lehrer, z.Raum, z.Schueler })
            if (id is int i && !ids.Contains(i)) throw new ArgumentException("Unbekanntes Stundenplanformat.");
        _einst.Schreiben(SchluesselZuordnung, JsonSerializer.Serialize(z, Json));
        return z;
    }

    /// <summary>Format anlegen (Id null bzw. 0) oder ändern; prüft Name, Elemente, Tage und Uhrzeiten.</summary>
    public Planformat Speichern(Planformat f)
    {
        var name = (f.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Bitte einen Namen eingeben.");
        if (name.Length > 60) throw new ArgumentException("Der Name ist zu lang (höchstens 60 Zeichen).");
        var elemente = (f.Elemente ?? new()).Where(e => !string.IsNullOrWhiteSpace(e.Art)).Select(e => e with
        {
            Art = e.Art.Trim().ToLowerInvariant(), Name = string.IsNullOrWhiteSpace(e.Name) ? "kurz" : e.Name.Trim().ToLowerInvariant(),
        }).ToList();
        if (elemente.Count == 0) throw new ArgumentException("Bitte mindestens das 1. Element auf dem Stundenkärtchen wählen.");
        if (elemente.Count > 5) throw new ArgumentException("Höchstens 5 Elemente auf dem Stundenkärtchen.");
        if (elemente.FirstOrDefault(e => !Arten.Contains(e.Art)) is { } fa) throw new ArgumentException($"Unbekanntes Element „{fa.Art}“.");
        if (elemente.FirstOrDefault(e => !Namensformen.Contains(e.Name)) is { } fn) throw new ArgumentException($"Unbekannte Namensform „{fn.Name}“.");
        if (elemente.GroupBy(e => e.Art).Any(g => g.Count() > 1)) throw new ArgumentException("Jedes Element nur einmal auf dem Stundenkärtchen.");
        var tage = (f.Tage ?? new()).Distinct().OrderBy(t => t).ToList();
        if (tage.Any(t => t is < 0 or > 6)) throw new ArgumentException("Ungültiger Wochentag.");
        string Uhrzeit(string? z, string feld)
        {
            var t = (z ?? "").Trim();
            if (t.Length == 0) return "";
            return TimeOnly.TryParseExact(t, "HH:mm", out var u) ? u.ToString("HH:mm") : throw new ArgumentException($"{feld}: Uhrzeit als HH:mm angeben.");
        }
        var von = Uhrzeit(f.ZeitVon, "Beginnzeit");
        var bis = Uhrzeit(f.ZeitBis, "Endzeit");
        if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(von, bis) >= 0) throw new ArgumentException("Die Beginnzeit muss vor der Endzeit liegen.");
        var liste = Formate();
        if (liste.Any(x => x.Id != f.Id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Ein Stundenplanformat mit diesem Namen gibt es schon.");
        var neu = f with { Name = name, Elemente = elemente, Tage = tage, ZeitVon = von, ZeitBis = bis, Zeitraster = f.Zeitraster == "uhrzeit" ? "uhrzeit" : "raster" };
        if (f.Id <= 0)
        {
            neu = neu with { Id = liste.Count == 0 ? 1 : liste.Max(x => x.Id) + 1 };
            liste.Add(neu);
        }
        else
        {
            var i = liste.FindIndex(x => x.Id == f.Id);
            if (i < 0) throw new ArgumentException("Unbekanntes Stundenplanformat.");
            liste[i] = neu;
        }
        _einst.Schreiben(SchluesselFormate, JsonSerializer.Serialize(liste, Json));
        return neu;
    }

    public void Loeschen(int id)
    {
        var liste = Formate();
        if (liste.RemoveAll(x => x.Id == id) == 0) throw new ArgumentException("Unbekanntes Stundenplanformat.");
        _einst.Schreiben(SchluesselFormate, JsonSerializer.Serialize(liste, Json));
        var z = Zuordnung();
        _einst.Schreiben(SchluesselZuordnung, JsonSerializer.Serialize(z with
        {
            Klasse = z.Klasse == id ? null : z.Klasse, Lehrer = z.Lehrer == id ? null : z.Lehrer, Raum = z.Raum == id ? null : z.Raum,
            Schueler = z.Schueler == id ? null : z.Schueler,
        }, Json));
    }

    /// <summary>Format für einen Plan des Typs (Klasse, Lehrkraft, Raum); Kollegium nur, wenn die Zuordnung es vorsieht. Null = Standard.</summary>
    public Planformat? FormatFuer(string typ, bool kollegium)
    {
        var z = Zuordnung();
        // älterer Stand: „nur für Schüler*innen und Eltern“ – das Klassenformat galt dort nur für Familien
        if (!z.Kollegium && z.Schueler is null) z = z with { Schueler = z.Klasse, Klasse = null, Kollegium = true };
        int? id = typ switch
        {
            Elementtyp.Klasse when !kollegium && z.Schueler is not null => z.Schueler,
            Elementtyp.Klasse => z.Klasse, Elementtyp.Lehrer => z.Lehrer, Elementtyp.Raum => z.Raum, _ => null,
        };
        return id is int i ? Formate().FirstOrDefault(f => f.Id == i) : null;
    }

    /// <summary>Gewähltes Format (Menü „⋮“ im Stundenplan): 0 = Standarddarstellung, sonst das Format mit dieser Id (unbekannt: Vorgabe).</summary>
    public Planformat? Gewaehlt(int? wahl, string typ, bool kollegium) =>
        wahl switch { null => FormatFuer(typ, kollegium), 0 => null, int i => Formate().FirstOrDefault(f => f.Id == i) ?? FormatFuer(typ, kollegium) };

    /// <summary>
    /// Format auf den Wochenplan anwenden: Tage und Uhrzeiten eingrenzen, Aktivitäten ausblenden, Hervorhebungen und Farben entfernen;
    /// das Format selbst und die Langnamen der vorkommenden Elemente gehen an Web und App (Anordnung auf dem Kärtchen).
    /// </summary>
    public static Wochenplan Anwenden(Wochenplan w, Planformat? f, Stand s)
    {
        if (f is null) return w;
        var tage = w.Tage;
        var karten = w.Karten;
        var raster = w.Zeitraster;
        if (f.Tage.Count > 0)
        {
            var erlaubt = tage.Select((t, i) => (t, i)).Where(x => f.Tage.Contains(x.i)).Select(x => x.t.Datum).ToHashSet();
            tage = tage.Where(t => erlaubt.Contains(t.Datum)).ToList();
            karten = karten.Where(k => erlaubt.Contains(k.Datum)).ToList();
        }
        if (f.ZeitVon.Length > 0 || f.ZeitBis.Length > 0)
        {
            bool Drin(string beginn, string ende) => (f.ZeitVon.Length == 0 || string.CompareOrdinal(ende, f.ZeitVon) > 0)
                                                     && (f.ZeitBis.Length == 0 || string.CompareOrdinal(beginn, f.ZeitBis) < 0);
            raster = raster.Where(z => Drin(z.Beginn, z.Ende)).ToList();
            var nummern = raster.Select(z => z.Nr).ToHashSet();
            karten = karten.Where(k => Enumerable.Range(k.Von, Math.Max(1, k.Bis - k.Von + 1)).Any(nummern.Contains)).ToList();
        }
        bool IstSprechstunde(Karte k) => k.Art.Contains("sprechstunde", StringComparison.OrdinalIgnoreCase) || k.Text.Contains("sprechstunde", StringComparison.OrdinalIgnoreCase);
        karten = karten.Where(k => (f.Entfall || k.Status != "entfall")
                                   && (f.Bereitschaften || !k.Bereitschaft)
                                   && (f.Sprechstunden || !IstSprechstunde(k))
                                   && (f.Buchungen || k.Status != "buchung")).ToList();
        static Wert Schlicht(Wert w) => w.K.Length == 0 ? w : new Wert(w.K);
        karten = karten.Select(k =>
        {
            var x = k;
            if (!f.Aenderungen && x.Status is "geaendert" or "offen")
                x = x with { Status = "regulaer", FachAlt = null, Klassen = x.Klassen.Select(Schlicht).ToList(), Lehrer = x.Lehrer.Select(Schlicht).ToList(), Raeume = x.Raeume.Select(Schlicht).ToList() };
            if (!f.Pruefungen && x.Status == "klausur") x = x with { Status = "regulaer" };
            if (!f.Farben) x = x with { Farbe = "" };
            if (!f.Symbole) x = x with { OhneMarker = true };
            if (!f.Entfaelle && x.Status == "entfall") x = x with { OhneMarker = true };
            return x;
        }).ToList();
        // Langnamen der vorkommenden Elemente (für „Langname“ bzw. „Langname (Kurzname)“ auf dem Kärtchen)
        Dictionary<string, string> Namen<T>(IEnumerable<string> kuerzel, Dictionary<string, T> quelle, Func<T, string> name) =>
            kuerzel.Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(k => (k, n: quelle.TryGetValue(k, out var e) ? name(e) : ""))
                .Where(x => x.n.Length > 0 && !string.Equals(x.n, x.k, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.k, x => x.n, StringComparer.OrdinalIgnoreCase);
        var namen = new Dictionary<string, Dictionary<string, string>>
        {
            ["klasse"] = Namen(karten.SelectMany(k => k.Klassen.Select(w => w.K)), s.Klassen, e => e.Name),
            ["lehrer"] = Namen(karten.SelectMany(k => k.Lehrer.Select(w => w.K)), s.Lehrer, e => string.Join(" ", new[] { e.Vorname, e.Name }.Where(t => t.Length > 0))),
            ["raum"] = Namen(karten.SelectMany(k => k.Raeume.Select(w => w.K)), s.Raeume, e => e.Name),
            ["fach"] = Namen(karten.Select(k => k.Fach), s.Faecher, e => e.Name),
        };
        return w with { Tage = tage, Karten = karten, Zeitraster = raster, Format = f, Langnamen = namen };
    }
}
