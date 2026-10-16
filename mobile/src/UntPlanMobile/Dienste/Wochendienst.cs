using System.Globalization;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Dienste;

/// <summary>Eine Woche im Format der Oberfläche (wie api/mobile.php?action=week der Vorlage).</summary>
public sealed record WochenAntwort(
    bool Ok, string WeekStart, string Kind, object Entity, Dictionary<string, object> LessonTimes,
    Dictionary<string, List<Kachel>> Days, List<object> Bands);

/// <summary>Wochen, Stammdaten und Startdaten (TT_BOOT) für die Oberfläche.</summary>
public sealed class Wochendienst
{
    private readonly UntPlanWebVerbindung _web;
    private readonly Kontodienst _konten;
    private readonly Zeitgeber _zeit;

    public Wochendienst(UntPlanWebVerbindung web, Kontodienst konten, Zeitgeber zeit) { _web = web; _konten = konten; _zeit = zeit; }

    /// <summary>Eigener Plan im Format der Vorlage: type „mine“, label = Kürzel, info = Name, kind = class/teacher.</summary>
    public static object Eigen(Konto k) => new
    {
        type = "mine",
        id = k.Kuerzel,
        label = k.Kuerzel.Length > 0 ? k.Kuerzel : "Mein Plan",
        info = k.Name,
        kind = k.EigeneArt,
    };

    /// <summary>Auswahlliste: Klassen (mit Klassenleitung), Lehrkräfte, Räume.</summary>
    public static List<object> Entitaeten(Konto k)
    {
        var lehrerName = k.Elemente.Lehrer.ToDictionary(l => l.Kurz, l => l.Name, StringComparer.OrdinalIgnoreCase);
        var res = new List<object>();
        foreach (var c in k.Elemente.Klassen)
            res.Add(new { type = "class", id = c.Kurz, label = c.Kurz, info = KlassenInfo(c, lehrerName), classTeacher = string.Join(", ", c.Leitung ?? new()) });
        // Schüler- und Elternkonten sehen keine Lehrerpläne – die Auswahl bietet sie gar nicht erst an.
        if (!k.IstSchueler)
            foreach (var l in k.Elemente.Lehrer) res.Add(new { type = "teacher", id = l.Kurz, label = l.Kurz, info = l.Name });
        foreach (var r in k.Elemente.Raeume) res.Add(new { type = "room", id = r.Kurz, label = r.Kurz, info = r.Name != r.Kurz ? r.Name : "" });
        return res;
    }

    /// <summary>Angaben zum angemeldeten Konto für die Profilseite.</summary>
    public static object Benutzer(Konto k) => new { login = k.Benutzer.Login, role = k.Benutzer.Rollenname, rolle = k.Benutzer.Rolle, kollegium = k.Kollegium };

    /// <summary>Text hinter dem ⓘ einer Klasse: Klassenleitung mit Namen, sonst der Langname.</summary>
    private static string KlassenInfo(ElementKlasse c, IReadOnlyDictionary<string, string> lehrerName)
    {
        var leitung = c.Leitung ?? new();
        return leitung.Count > 0
            ? "Klassenlehrkraft: " + string.Join(", ", leitung.Select(l => lehrerName.TryGetValue(l, out var n) && n.Length > 0 ? $"{l} ({n})" : l))
            : c.Name != c.Kurz ? c.Name : "";
    }

    /// <summary>Langnamen für die Stundendetails (Fächer, Lehrkräfte, Räume).</summary>
    public static object Namen(Konto k) => new
    {
        subject = k.Elemente.Faecher.GroupBy(f => f.Kurz.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Name),
        teacher = k.Elemente.Lehrer.GroupBy(l => l.Kurz.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Name),
        room = k.Elemente.Raeume.GroupBy(r => r.Kurz).ToDictionary(g => g.Key, g => Raumname(g.First())),
    };

    private static string Raumname(ElementRaum raum) => raum.Name.Length > 0 && raum.Name != raum.Kurz ? $"{raum.Kurz} · {raum.Name}" : raum.Kurz;

    /// <summary>Planart und Element auflösen; „mine“ (oder fehlende Angaben) wird zum eigenen Plan.</summary>
    public static (string Art, string Element) Aufloesen(Konto k, string? type, string? id)
    {
        if (type == "teacher" && k.IstSchueler) return (k.EigeneArt, k.Kuerzel);   // keine Lehrerpläne für Schüler- und Elternkonten
        if (type is "class" or "teacher" or "room" && !string.IsNullOrWhiteSpace(id)) return (type, id!.Trim());
        return (k.EigeneArt, k.Kuerzel);
    }

    private object Entitaet(Konto k, string art, string element)
    {
        var info = art switch
        {
            "teacher" => k.Elemente.Lehrer.FirstOrDefault(l => string.Equals(l.Kurz, element, StringComparison.OrdinalIgnoreCase))?.Name ?? "",
            "room" => k.Elemente.Raeume.FirstOrDefault(r => string.Equals(r.Kurz, element, StringComparison.OrdinalIgnoreCase))?.Name ?? "",
            _ => k.Elemente.Klassen.FirstOrDefault(x => string.Equals(x.Kurz, element, StringComparison.OrdinalIgnoreCase)) is { } kl
                ? KlassenInfo(kl, k.Elemente.Lehrer.ToDictionary(l => l.Kurz, l => l.Name, StringComparer.OrdinalIgnoreCase)) : "",
        };
        return new { type = art, id = element, label = element, info };
    }

    public async Task<WochenAntwort> WocheAsync(HttpContext c, Konto k, string? type, string? id, DateOnly datum)
    {
        var (art, element) = Aufloesen(k, type, id);
        var montag = Kachelbau.Montag(datum);
        var zeiten = k.Elemente.Stunden ?? new();
        if (element.Length == 0)
        {
            // Konto ohne eigenes Kürzel (z. B. Administration): leerer eigener Plan
            var leer = Enumerable.Range(0, 5).ToDictionary(i => Kachelbau.Iso(montag.AddDays(i)), _ => new List<Kachel>());
            return new WochenAntwort(true, Kachelbau.Iso(montag), art, new { type = art, id = "", label = "Mein Plan", info = k.Name },
                Kachelbau.Stundenzeiten(zeiten), leer, new List<object>());
        }
        var plan = await _web.HolenAsync<Wochenplan>(c,
            $"api/app/plan?typ={Kachelbau.Typ(art)}&element={Uri.EscapeDataString(element)}&datum={Kachelbau.Iso(datum)}");
        var tage = Kachelbau.Woche(plan, k.Elemente.Ferien);
        if (art == "class") await HausaufgabenMarkieren(c, element, plan, tage);
        return new WochenAntwort(true, plan.Montag, art, Entitaet(k, art, plan.Element),
            Kachelbau.Stundenzeiten(plan.Zeitraster.Count > 0 ? plan.Zeitraster : zeiten), tage, new List<object>());
    }

    /// <summary>Hausaufgabenzeichen auf der Stunde, zu der eine Hausaufgabe fällig ist (Klassenpläne).</summary>
    private async Task HausaufgabenMarkieren(HttpContext c, string klasse, Wochenplan plan, Dictionary<string, List<Kachel>> tage)
    {
        try
        {
            var montag = DateOnly.Parse(plan.Montag, CultureInfo.InvariantCulture);
            var liste = await _web.GetAsync(c, $"api/app/klassenbuch/klasse?klasse={Uri.EscapeDataString(klasse)}&von={Kachelbau.Iso(montag.AddDays(-35))}&bis={Kachelbau.Iso(montag.AddDays(6))}");
            foreach (var e in liste?.AsArray() ?? new JsonArray())
            {
                var faellig = e?["faellig"]?.GetValue<string>();
                var unterricht = e?["unterricht"]?.GetValue<int>();
                if (faellig is null || unterricht is null || string.IsNullOrWhiteSpace(e?["hausaufgabe"]?.GetValue<string>())) continue;
                if (!tage.TryGetValue(faellig, out var karten)) continue;
                var ziel = karten.Where(x => x.Unterricht == unterricht && x.Art != "cancelled").OrderBy(x => x.Stunde).FirstOrDefault();
                if (ziel != null) ziel.Hausaufgabe = true;
            }
        }
        catch (UntPlanWebFehler) { /* ohne Klassenbuch eben ohne Zeichen */ }
    }

    /// <summary>Startdaten für die Hülle (window.TT_BOOT): Stammdaten und die aktuelle Woche des eigenen Plans.</summary>
    public async Task<object> BootAsync(HttpContext c, string? type, string? id, string? date)
    {
        if (UntPlanWebVerbindung.Sitzung(c) is null) return new { ok = false, needLogin = true, entity = new { label = "" } };
        try
        {
            var k = await _konten.LadenAsync(c);
            var datum = DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var d) ? d : _zeit.Heute;
            var woche = await WocheAsync(c, k, type, id, datum);
            var jetzt = _zeit.Jetzt;
            return new
            {
                ok = true,
                school = k.Schule.Length > 0 ? k.Schule : "UntPlanMobile",
                own = Eigen(k),
                isStudent = k.IstSchueler,
                entities = Entitaeten(k),
                lessonTimes = woche.LessonTimes,
                entity = type is "class" or "teacher" or "room" ? woche.Entity : new { type = "mine", id = k.Kuerzel, label = "Mein Stundenplan", info = k.Name },
                kind = woche.Kind,
                weekStart = woche.WeekStart,
                days = woche.Days,
                bands = woche.Bands,
                today = Kachelbau.Iso(DateOnly.FromDateTime(jetzt)),
                now = jetzt.ToString("HH:mm", CultureInfo.InvariantCulture),
                names = Namen(k),
                user = Benutzer(k),
            };
        }
        catch (NichtAngemeldetException)
        {
            return new { ok = false, needLogin = true, entity = new { label = "" } };
        }
        catch (UntPlanWebFehler ex)
        {
            return new { ok = false, error = ex.Message, entity = new { label = "" } };
        }
    }
}

/// <summary>Uhr der Anwendung (in Tests fest einstellbar).</summary>
public sealed class Zeitgeber
{
    private readonly TimeZoneInfo _zone;

    public Zeitgeber(TimeZoneInfo zone) { _zone = zone; }

    public DateTime? Fest { get; set; }
    public DateTime Jetzt => Fest ?? TimeZoneInfo.ConvertTime(DateTime.UtcNow, _zone);
    public DateOnly Heute => DateOnly.FromDateTime(Jetzt);
}
