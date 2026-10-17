using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace UntPlanMobile.Dienste;

/// <summary>Angemeldetes Konto mit Stammdaten (Klassen, Lehrkräfte, Räume, Fächer, Zeitraster, Ferien).</summary>
public sealed record Konto(Sitzungsbenutzer Benutzer, string Schule, Elemente Elemente)
{
    /// <summary>Schüler-, Eltern- bzw. Klassenkonto: eigener Plan ist ein Klassenplan, keine Lehrerpläne.</summary>
    public bool IstSchueler => Benutzer.Rolle is "schueler" or "eltern";

    /// <summary>Planart des eigenen Plans (class oder teacher).</summary>
    public string EigeneArt => IstSchueler ? "class" : "teacher";

    /// <summary>Kürzel der eigenen Klasse bzw. Lehrkraft (leer z. B. bei der Administration ohne Kürzel).</summary>
    public string Kuerzel => Benutzer.Element ?? "";

    /// <summary>Darf Klassenbuch, Anwesenheit und Fehlzeiten sehen.</summary>
    public bool Kollegium => Benutzer.Kollegium;

    public bool Leitung => Benutzer.Rolle is "admin" or "leitung";

    /// <summary>Wochenbegrenzung der Rolle aus /api/app/sitzung: so viele Wochen zurück bzw. voraus sind sichtbar (null = unbegrenzt).</summary>
    public int? PlanWochenZurueck { get; init; }
    public int? PlanWochenVor { get; init; }

    /// <summary>Voller Name zum eigenen Kürzel (Lehrkraft), sonst der Anzeigename.</summary>
    public string Name =>
        !IstSchueler && Elemente.Lehrer.FirstOrDefault(l => string.Equals(l.Kurz, Kuerzel, StringComparison.OrdinalIgnoreCase)) is { } l && l.Name.Length > 0
            ? l.Name
            : Benutzer.Anzeigename.Length > 0 ? Benutzer.Anzeigename : Benutzer.Login;

    /// <summary>Klassen, die die Lehrkraft leitet (Schulleitung und Administration: alle Klassen).</summary>
    public List<ElementKlasse> GeleiteteKlassen() =>
        Leitung ? Elemente.Klassen
        : Kuerzel.Length == 0 || IstSchueler ? new List<ElementKlasse>()
        : Elemente.Klassen.Where(k => (k.Leitung ?? new()).Contains(Kuerzel, StringComparer.OrdinalIgnoreCase)).ToList();
}

/// <summary>Lädt Sitzung und Stammdaten von UntPlanWeb; kurz zwischengespeichert, damit nicht jeder Aufruf doppelt fragt.</summary>
public sealed class Kontodienst
{
    private readonly UntPlanWebVerbindung _web;
    private readonly IMemoryCache _cache;

    public Kontodienst(UntPlanWebVerbindung web, IMemoryCache cache) { _web = web; _cache = cache; }

    private static string Schluessel(string sitzung) => "konto:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sitzung)));

    public async Task<Konto> LadenAsync(HttpContext c)
    {
        var sitzung = UntPlanWebVerbindung.Sitzung(c) ?? throw new NichtAngemeldetException();
        if (_cache.TryGetValue(Schluessel(sitzung), out Konto? k) && k != null) return k;
        var s = await _web.GetAsync(c, "api/app/sitzung");
        if (s?["benutzer"] is not JsonObject b)
        {
            UntPlanWebVerbindung.SitzungLoeschen(c);
            throw new NichtAngemeldetException();
        }
        var benutzer = b.Deserialize<Sitzungsbenutzer>(UntPlanWebVerbindung.Json)!;
        var elemente = await _web.HolenAsync<Elemente>(c, "api/app/elemente");
        k = new Konto(benutzer, s["schule"]?["name"]?.GetValue<string>() ?? "", elemente)
        {
            PlanWochenZurueck = Wochenzahl(s, b, "planWochenZurueck"),
            PlanWochenVor = Wochenzahl(s, b, "planWochenVor"),
        };
        _cache.Set(Schluessel(sitzung), k, TimeSpan.FromSeconds(30));
        return k;
    }

    /// <summary>Wochenbegrenzung aus der Sitzung – oben oder im Benutzer, als Zahl oder Zahltext; sonst null (unbegrenzt).</summary>
    public static int? Wochenzahl(JsonNode? sitzung, JsonNode? benutzer, string name)
    {
        foreach (var quelle in new[] { sitzung, benutzer, sitzung?["begrenzung"], sitzung?["wochenbegrenzung"] })
        {
            if (quelle is not JsonObject o) continue;
            var wert = o.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            if (wert is not JsonValue v) continue;
            if (v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var n)) return Math.Max(0, n);
            if (v.GetValueKind() == JsonValueKind.String && int.TryParse(v.GetValue<string>(), out var t)) return Math.Max(0, t);
        }
        return null;
    }

    public void Vergessen(HttpContext c)
    {
        if (UntPlanWebVerbindung.Sitzung(c) is { } s) _cache.Remove(Schluessel(s));
    }
}
