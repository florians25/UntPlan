using System.Globalization;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Dienste;

/// <summary>
/// Eine Woche im Format der Oberfläche (wie api/mobile.php?action=week der Vorlage). Gesperrt (nur gesetzt, wenn wahr):
/// Woche außerhalb der Wochenbegrenzung der Rolle – leere Tage, die Oberfläche zeigt das Schloss mit dem Sperrhinweis.
/// </summary>
public sealed record WochenAntwort(
    bool Ok, string WeekStart, string Kind, object Entity, Dictionary<string, object> LessonTimes,
    Dictionary<string, List<Kachel>> Days, List<object> Bands, bool? Gesperrt = null, string? Sperrhinweis = null,
    Dictionary<string, List<Abwesenheitsbalken>>? Absences = null);

/// <summary>
/// Eigene Abwesenheit an einem Tag als grauer Balken hinter den Stunden („Mein Plan“): Beginn und Ende (HH:mm) und Text
/// („Abwesend“ bzw. „Klasse abwesend“). Die Oberfläche blendet ihn über das ⋮-Menü ein und aus.
/// </summary>
public sealed record Abwesenheitsbalken(
    [property: System.Text.Json.Serialization.JsonPropertyName("from")] string Von,
    [property: System.Text.Json.Serialization.JsonPropertyName("to")] string Bis,
    [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text);

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

    /// <summary>Angaben zum angemeldeten Konto für die Profilseite; dazu die Wochenbegrenzung der Rolle (null = unbegrenzt, dann weggelassen).</summary>
    public static object Benutzer(Konto k) => new
    {
        login = k.Benutzer.Login, role = k.Benutzer.Rollenname, rolle = k.Benutzer.Rolle, kollegium = k.Kollegium,
        planWochenZurueck = k.PlanWochenZurueck, planWochenVor = k.PlanWochenVor,
    };

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
        if (plan.Gesperrt)
        {
            // Außerhalb der Wochenbegrenzung der Rolle: keine Termine, nur der Sperrhinweis (die Wochennavigation bleibt)
            var mo = DateOnly.TryParse(plan.Montag, CultureInfo.InvariantCulture, out var m) ? m : montag;
            var leer = Enumerable.Range(0, 5).ToDictionary(i => Kachelbau.Iso(mo.AddDays(i)), _ => new List<Kachel>());
            return new WochenAntwort(true, Kachelbau.Iso(mo), art, Entitaet(k, art, plan.Element.Length > 0 ? plan.Element : element),
                Kachelbau.Stundenzeiten(plan.Zeitraster.Count > 0 ? plan.Zeitraster : zeiten), leer, new List<object>(),
                true, string.IsNullOrWhiteSpace(plan.Sperrhinweis) ? null : plan.Sperrhinweis.Trim());
        }
        var tage = Kachelbau.Woche(plan, k.Elemente.Ferien);
        if (!k.Kollegium) Kachelbau.NurSchuelerNotizen(tage);
        if (art == "class") await HausaufgabenMarkieren(c, element, plan, tage);
        // Nur im eigenen Plan: eigene Abwesenheiten als Balken hinter den Stunden (Lehrkraft/Klasse aus den Tageshinweisen,
        // Schüler*innen bzw. Kind der Eltern aus „Meine Abwesenheiten“)
        Dictionary<string, List<Abwesenheitsbalken>>? abwesenheiten = null;
        if (type is not ("class" or "teacher" or "room"))
        {
            var raster = plan.Zeitraster.Count > 0 ? plan.Zeitraster : zeiten;
            // Neuere UntPlanWeb-Server liefern die eigenen Abwesenheiten mit dem Plan; sonst Tageshinweise bzw. „Meine Abwesenheiten“
            var zeitraeume = plan.Abwesenheiten is null && k.IstSchueler ? await EigeneZeitraeumeAsync(c, plan.Montag) : new List<JsonObject>();
            abwesenheiten = plan.Tage.Select(t => (t.Datum, Balken: BalkenDesTages(t, raster, zeitraeume, plan.Abwesenheiten)))
                .Where(x => x.Balken.Count > 0).ToDictionary(x => x.Datum, x => x.Balken);
        }
        return new WochenAntwort(true, plan.Montag, art, Entitaet(k, art, plan.Element),
            Kachelbau.Stundenzeiten(plan.Zeitraster.Count > 0 ? plan.Zeitraster : zeiten), tage, new List<object>(), Absences: abwesenheiten);
    }

    /// <summary>Eigene Abwesenheiten (Schüler*in bzw. Kind) der Woche aus UntPlanWeb; ohne Zugriff bzw. bei Fehlern keine.</summary>
    private async Task<List<JsonObject>> EigeneZeitraeumeAsync(HttpContext c, string montag)
    {
        if (!DateOnly.TryParse(montag, CultureInfo.InvariantCulture, out var mo)) return new();
        try
        {
            var j = await _web.GetAsync(c, $"api/app/familie/abwesenheiten?von={Kachelbau.Iso(mo)}&bis={Kachelbau.Iso(mo.AddDays(6))}");
            return (j?["zeilen"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().ToList();
        }
        catch (UntPlanWebFehler) { return new(); }
    }

    /// <summary>
    /// Abwesenheitsbalken eines Tages (wie Kachelbau.abwesenheitsbalken der iOS-App): die vom Server mit dem Plan gelieferten
    /// eigenen Abwesenheiten (<paramref name="eigene"/>) bzw. bei älteren Servern Tageshinweise „abwesend 1.–8. Std. – Grund“,
    /// „abwesend ab 3. Std.“, „Klasse abwesend 2. Std.“ (Stunden über das Zeitraster in Uhrzeiten) und eigene Abwesenheiten
    /// („fehlt“; am ersten bzw. letzten Tag ab bzw. bis zur Uhrzeit, dazwischen ganztägig). Überlappende Balken werden
    /// zusammengefasst, alles bleibt im Bereich des Zeitrasters; freie Tage haben keinen Balken.
    /// </summary>
    public static List<Abwesenheitsbalken> BalkenDesTages(PlanTag tag, IReadOnlyList<PlanZeit> raster, IEnumerable<JsonObject> zeitraeume,
        IReadOnlyList<PlanAbwesenheit>? eigene = null)
    {
        var res = new List<Abwesenheitsbalken>();
        var zeiten = raster.Select(z => (z.Nr, Beginn: Minuten(z.Beginn), Ende: Minuten(z.Ende))).Where(z => z.Beginn is not null && z.Ende is not null)
            .Select(z => (z.Nr, Beginn: z.Beginn!.Value, Ende: z.Ende!.Value)).OrderBy(z => z.Beginn).ToList();
        if (tag.Frei || zeiten.Count == 0) return res;
        int start = zeiten[0].Beginn, schluss = zeiten[^1].Ende;
        var roh = new List<(int Beginn, int Ende, string Text)>();
        // Vom Server mitgelieferte eigene Abwesenheiten gelten allein
        foreach (var a in eigene ?? Array.Empty<PlanAbwesenheit>())
            if (a.Datum.Length >= 10 && a.Datum[..10] == tag.Datum) roh.Add((Minuten(a.Von) ?? start, Minuten(a.Bis) ?? schluss, "Abwesend"));
        foreach (var hinweis in eigene is null ? tag.Hinweise ?? new() : new List<string>())
        {
            if (AbwesenheitsStunden(hinweis) is not { } st) continue;
            var betroffen = zeiten.Where(z => z.Nr >= st.Von && z.Nr <= (st.Bis ?? int.MaxValue)).ToList();
            if (betroffen.Count > 0) roh.Add((betroffen[0].Beginn, betroffen[^1].Ende, st.Klasse ? "Klasse abwesend" : "Abwesend"));
        }
        string S(JsonObject z, string f) => z[f] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        foreach (var z in eigene is null ? zeitraeume : Array.Empty<JsonObject>())
        {
            var art = S(z, "art");
            var von = S(z, "vonDatum");
            von = von.Length >= 10 ? von[..10] : von;
            var bis = S(z, "bisDatum");
            bis = bis.Length >= 10 ? bis[..10] : bis.Length == 0 ? von : bis;
            if (art is not ("" or "fehlt") || von.Length == 0 || string.CompareOrdinal(von, tag.Datum) > 0 || string.CompareOrdinal(bis, tag.Datum) < 0) continue;
            var b = von == tag.Datum ? Minuten(S(z, "vonZeit")) ?? start : start;
            var e = bis == tag.Datum ? Minuten(S(z, "bisZeit")) ?? schluss : schluss;
            roh.Add((b, e, "Abwesend"));
        }
        var zusammen = new List<(int Beginn, int Ende, string Text)>();
        foreach (var r in roh.Select(r => (Beginn: Math.Max(start, r.Beginn), Ende: Math.Min(schluss, r.Ende), r.Text)).Where(r => r.Ende > r.Beginn).OrderBy(r => r.Beginn))
        {
            if (zusammen.Count > 0 && r.Beginn <= zusammen[^1].Ende)
            {
                var l = zusammen[^1];
                zusammen[^1] = (l.Beginn, Math.Max(l.Ende, r.Ende), r.Text == "Abwesend" ? r.Text : l.Text);
            }
            else zusammen.Add(r);
        }
        return zusammen.Select(z => new Abwesenheitsbalken(Uhrzeit(z.Beginn), Uhrzeit(z.Ende), z.Text)).ToList();
    }

    /// <summary>Stunden aus „abwesend …“ bzw. „Klasse abwesend …“: von, bis (null = bis zum Ende), Klasse.</summary>
    public static (int Von, int? Bis, bool Klasse)? AbwesenheitsStunden(string hinweis)
    {
        var klein = (hinweis ?? "").Trim().ToLowerInvariant();
        var klasse = klein.StartsWith("klasse abwesend", StringComparison.Ordinal);
        if (!klasse && !klein.StartsWith("abwesend", StringComparison.Ordinal)) return null;
        var bereich = System.Text.RegularExpressions.Regex.Match(klein, @"(\d+)\.\s*[–-]\s*(\d+)\.");
        var teil = klein.Split('–', 2)[0];
        var erste = System.Text.RegularExpressions.Regex.Match(teil, @"\d+");
        if (!erste.Success) return (1, null, klasse);
        var von = int.Parse(erste.Value, CultureInfo.InvariantCulture);
        if (teil.Contains(" ab ", StringComparison.Ordinal)) return (von, null, klasse);
        if (bereich.Success) return (int.Parse(bereich.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(bereich.Groups[2].Value, CultureInfo.InvariantCulture), klasse);
        return (von, von, klasse);
    }

    private static int? Minuten(string? text)
    {
        var t = (text ?? "").Split(':');
        return t.Length >= 2 && int.TryParse(t[0], out var h) && int.TryParse(t[1].Length > 2 ? t[1][..2] : t[1], out var m) ? h * 60 + m : null;
    }

    private static string Uhrzeit(int minuten) => $"{minuten / 60:00}:{minuten % 60:00}";

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
                gesperrt = woche.Gesperrt,
                sperrhinweis = woche.Sperrhinweis,
                absences = woche.Absences,
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
