using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>Ein angezeigter Wert (Klasse, Lehrkraft, Raum); Alt = ursprünglicher Wert (durchgestrichen), Neu = geändert.</summary>
public sealed record Wert(string K, string? Alt = null, bool Neu = false);

/// <summary>
/// Stundenkarte im Plan.
/// Status:
/// <list type="bullet">
/// <item>regulaer</item>
/// <item>geaendert (Vertretung, Raum- oder Fachänderung, zusätzliche Stunde)</item>
/// <item>offen (Vertretung noch ohne Lehrkraft)</item>
/// <item>entfall</item>
/// <item>abwesend (eigene Stunde wird von anderen übernommen; Lehrer- bzw. Raumplan)</item>
/// <item>klausur</item>
/// <item>bereitschaft</item>
/// <item>buchung (Raumbuchung)</item>
/// <item>vorgemerkt (in UntPlanWeb beantragt, von UntPlan noch nicht übernommen)</item>
/// </list>
/// </summary>
public sealed record Karte(
    string Datum, int Von, int Bis, string Beginn, string Ende, string Status,
    string Fach, string? FachAlt, string FachName, string Farbe,
    List<Wert> Klassen, List<Wert> Lehrer, List<Wert> Raeume,
    string Art, string Text, int? Vertretung, int? Unterricht, string Gruppe);

/// <summary>Pausenaufsicht im Lehrerplan. Status: regulaer, vertreten (fällt für diese Lehrkraft weg), vertretung (zusätzlich übernommen).</summary>
public sealed record Aufsicht(string Datum, int NachStunde, string Beginn, string Ende, string Gang, string GangName, string Status, string Lehrer, string? Alt);

public sealed record Tageskopf(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, bool Tagesaktuell, List<string> Hinweise, List<Ereignis> Ereignisse, string? FreiArt = null);

/// <summary>Veranstaltung an einem Tag: ganztägig (Leiste über dem Tag) oder in bestimmten Stunden (Karte im Raster).</summary>
public sealed record Ereignis(string Titel, string Art, int Von, int Bis, bool Ganztaegig, List<string> Klassen, List<string> Lehrer, List<string> Raeume);

public sealed record Zeit(int Nr, string Bezeichnung, string Beginn, string Ende);

public sealed record Wochenplan(string Typ, string Element, string Name, string Montag, string Woche, List<Zeit> Zeitraster, List<Tageskopf> Tage,
    List<Karte> Karten, List<Aufsicht> Aufsichten);

public sealed record Tagesuebersicht(string Typ, string Datum, Tageskopf Kopf, List<Zeit> Zeitraster, List<Tageszeile> Zeilen);
public sealed record Tageszeile(string Kurz, string Name, List<Karte> Karten);

/// <summary>Rohdaten einer Stunde (aus dem tagesaktuellen Plan, dem Regelplan oder einer Vormerkung).</summary>
public sealed record Rohstunde(int Stunde, int? Unterricht, string Fach, IReadOnlyList<string> Klassen, IReadOnlyList<string> Lehrer, IReadOnlyList<string> Raeume,
    string Status, int? Vertretung, string Text, string Art = "");

public static class Elementtyp
{
    public const string Klasse = "klasse";
    public const string Lehrer = "lehrer";
    public const string Raum = "raum";
    public static bool Gueltig(string t) => t is Klasse or Lehrer or Raum;
}

/// <summary>Stundenpläne (Woche, Tag), Tagesübersichten aller Elemente und Hinweise aus dem übertragenen Paket.</summary>
public sealed class Plandienst
{
    public static readonly string[] Wochentage = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    public static DateOnly Montag(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    public static int TagIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    public List<Zeit> Zeitraster(Stand s) => s.Paket.Zeitraster.Stunden.Select(z => new Zeit(z.Nr, z.Bezeichnung.Length > 0 ? z.Bezeichnung : z.Nr.ToString(), z.Beginn, z.Ende)).ToList();

    public string Name(Stand s, string typ, string kurz) => typ switch
    {
        Elementtyp.Klasse => s.Klassen.TryGetValue(kurz, out var k) ? (k.Name.Length > 0 && k.Name != k.Kurz ? $"{k.Kurz} – {k.Name}" : $"Klasse {k.Kurz}") : kurz,
        Elementtyp.Lehrer => s.Lehrer.TryGetValue(kurz, out var l) ? $"{l.Kurz} – {string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => x.Length > 0))}" : kurz,
        Elementtyp.Raum => s.Raeume.TryGetValue(kurz, out var r) ? (r.Name.Length > 0 && r.Name != r.Kurz ? $"{r.Kurz} – {r.Name}" : $"Raum {r.Kurz}") : kurz,
        _ => kurz,
    };

    /// <summary>Wochentyp (A/B …) eines Datums: aus dem tagesaktuellen Plan, sonst aus dem Wochenzyklus ab Schuljahresbeginn.</summary>
    public string Woche(Stand s, DateOnly d)
    {
        if (s.Tage.TryGetValue(Iso(d), out var t) && t.Woche.Length > 0) return t.Woche;
        var n = Math.Max(1, s.Paket.Schule.Wochenperiodizitaet);
        if (n == 1 || s.SchuljahrVon == DateOnly.MinValue) return "";
        var wochen = (Montag(d).DayNumber - Montag(s.SchuljahrVon).DayNumber) / 7;
        return ((char)('A' + ((wochen % n) + n) % n)).ToString();
    }

    public WebFerien? Ferien(Stand s, DateOnly d) =>
        s.Paket.Ferien.FirstOrDefault(f => DateOnly.TryParse(f.Von, out var v) && DateOnly.TryParse(f.Bis, out var b) && d >= v && d <= b);

    /// <summary>Unterrichtsfrei (Wochenende, Ferien, außerhalb des Schuljahres oder im Paket als unterrichtsfrei markiert)?</summary>
    public (bool Frei, string? Grund) Frei(Stand s, DateOnly d)
    {
        if (s.Tage.TryGetValue(Iso(d), out var t))
            return t.Unterrichtstag ? (false, null) : (true, Ferien(s, d)?.Name ?? (TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? null : "unterrichtsfrei"));
        if (TagIndex(d) >= Math.Max(1, s.Paket.Zeitraster.TageProWoche)) return (true, null);
        if (d < s.SchuljahrVon || d > s.SchuljahrBis) return (true, "außerhalb des Schuljahres");
        if (Ferien(s, d) is { } f) return (true, f.Name);
        return (false, null);
    }

    /// <summary>Alle Stunden eines Tages (tagesaktuell, sonst Regelplan der gültigen Periode).</summary>
    public List<Rohstunde> Rohstunden(Stand s, DateOnly d)
    {
        if (s.Tage.TryGetValue(Iso(d), out var tag))
            return tag.Unterrichtstag
                ? tag.Stunden.Select(x => new Rohstunde(x.Stunde, x.Unterricht, x.Fach, x.Klassen, x.Lehrer, x.Raeume, x.Status, x.Vertretung, x.Text)).ToList()
                : new List<Rohstunde>();
        if (Frei(s, d).Frei) return new List<Rohstunde>();
        var periode = s.Paket.Perioden
            .Where(p => DateOnly.TryParse(p.Von, out var v) && v <= d && (!DateOnly.TryParse(p.Bis, out var b) || d <= b))
            .OrderByDescending(p => DateOnly.Parse(p.Von)).FirstOrDefault();
        if (periode is null) return new List<Rohstunde>();
        var unterricht = periode.Unterricht.ToDictionary(u => u.Id);
        var woche = Woche(s, d);
        var t = TagIndex(d);
        var res = new List<Rohstunde>();
        foreach (var ps in periode.Stundenplan.Where(p => p.Tag == t))
        {
            if (!unterricht.TryGetValue(ps.Unterricht, out var u)) continue;
            if (woche.Length > 0 && u.Wochen.Length > 0 && !u.Wochen.Contains(woche, StringComparison.OrdinalIgnoreCase)) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var raeume = ps.Raeume.Count == u.Zeilen.Count ? new List<string> { ps.Raeume[i] } : ps.Raeume;
                res.Add(new Rohstunde(ps.Stunde, u.Id, z.Fach, z.Klassen, z.Lehrer.Length > 0 ? new List<string> { z.Lehrer } : new List<string>(), raeume, "regulaer", null, ""));
            }
        }
        // Klausuren aus den Terminen (im tagesaktuellen Plan sind sie bereits enthalten)
        foreach (var k in s.Paket.Termine.Where(x => x.Art == "klausur" && DateOnly.TryParse(x.Von, out var v) && v == d))
            for (var st = k.StundeVon; st <= Math.Max(k.StundeVon, k.StundeBis); st++)
                res.Add(new Rohstunde(st, null, "", k.Klassen, k.Lehrer, k.Raeume, "zusaetzlich", null, k.Titel, "Klausur"));
        return res;
    }

    private static bool Enthaelt(IReadOnlyList<string> liste, string kurz) => liste.Any(x => Ci.Equals(x, kurz));

    private static bool IstBereitschaft(Stand s, string fach) =>
        fach.Equals("BER", StringComparison.OrdinalIgnoreCase) || (s.Faecher.TryGetValue(fach, out var f) && f.Name.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase));

    /// <summary>Karten eines Elements an einem Tag; zusammenhängende gleiche Stunden werden zu Blöcken zusammengefasst.</summary>
    public List<Karte> Karten(Stand s, DateOnly d, string typ, string kurz, IEnumerable<Rohstunde>? zusaetzlich = null)
    {
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        var einzel = new List<Karte>();
        var vorgemerkt = new List<(Rohstunde R, Karte K)>();
        foreach (var r in Rohstunden(s, d).Concat(zusaetzlich ?? Enumerable.Empty<Rohstunde>()))
        {
            WebVertretung? v = r.Vertretung is int vid && s.Vertretungen.TryGetValue(vid, out var vx) ? vx : null;
            var betrifft = typ switch
            {
                Elementtyp.Klasse => Enthaelt(r.Klassen, kurz),
                Elementtyp.Lehrer => Enthaelt(r.Lehrer, kurz) || (v != null && Ci.Equals(v.Lehrer, kurz)),
                Elementtyp.Raum => Enthaelt(r.Raeume, kurz) || (v != null && Ci.Equals(v.Raum, kurz)),
                _ => false,
            };
            if (!betrifft) continue;
            var k = KarteBauen(s, d, typ, kurz, r, v, zeiten);
            if (r.Status == "vorgemerkt") vorgemerkt.Add((r, k));
            else einzel.Add(k);
        }
        // Vorgemerkte Klausuren und Raumbuchungen zu einer Unterrichtsstunde (gleiche Stunde, gemeinsame Klasse) direkt auf die Stundenkarte legen
        foreach (var (r, k) in vorgemerkt)
        {
            var i = r.Klassen.Count == 0 ? -1 : einzel.FindIndex(x => x.Von == r.Stunde && x.Unterricht.HasValue && x.Status is not ("entfall" or "abwesend")
                                                                      && x.Klassen.Any(w => Enthaelt(r.Klassen, w.K)));
            if (i < 0) { einzel.Add(k); continue; }
            var ziel = einzel[i];
            var raeume = r.Raeume.Count == 0 ? ziel.Raeume
                : r.Raeume.Select(x => ziel.Raeume.Any(w => Ci.Equals(w.K, x)) ? new Wert(x) : new Wert(x, ziel.Raeume.FirstOrDefault()?.K ?? "", true)).ToList();
            einzel[i] = r.Art == "Klausur"
                ? ziel with { Status = "klausur", Art = "Klausur (vorgemerkt)", Text = r.Text, Raeume = raeume }
                : ziel with { Status = ziel.Status == "regulaer" ? "geaendert" : ziel.Status, Art = "Raumänderung (vorgemerkt)", Raeume = raeume, Text = r.Text.Length > 0 && r.Text != "Raumbuchung" ? r.Text : ziel.Text };
        }
        // Teilnahmen mehrerer Lehrkräfte an derselben Veranstaltung: eine Karte mit allen Beteiligten
        var veranstaltungen = einzel.Where(k => k.Status == "veranstaltung").GroupBy(k => (k.Von, k.Text))
            .Select(g => g.First() with
            {
                Klassen = g.SelectMany(k => k.Klassen).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Lehrer = g.SelectMany(k => k.Lehrer).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Raeume = g.SelectMany(k => k.Raeume).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
            }).ToList();
        einzel = einzel.Where(k => k.Status != "veranstaltung").Concat(veranstaltungen).ToList();
        return Zusammenfassen(einzel, zeiten);
    }

    private Karte KarteBauen(Stand s, DateOnly d, string typ, string kurz, Rohstunde r, WebVertretung? v, Dictionary<int, WebStundenzeit> zeiten)
    {
        var art = r.Art.Length > 0 ? r.Art : v?.Art ?? "";
        var status = r.Status switch
        {
            "entfall" => "entfall",
            "offen" => "offen",
            "vertreten" => "geaendert",
            "zusaetzlich" when art.StartsWith("Klausur", StringComparison.OrdinalIgnoreCase) => "klausur",
            "zusaetzlich" when art.StartsWith("Raumbuchung", StringComparison.OrdinalIgnoreCase) => "buchung",
            "zusaetzlich" when art.StartsWith("Veranst", StringComparison.OrdinalIgnoreCase) => "veranstaltung",
            "zusaetzlich" => "geaendert",
            "vorgemerkt" => "vorgemerkt",
            _ => IstBereitschaft(s, r.Fach) ? "bereitschaft" : "regulaer",
        };
        var fach = r.Fach;
        string? fachAlt = null;
        if (v != null && v.NeuesFach.Length > 0 && v.Fach.Length > 0 && !Ci.Equals(v.NeuesFach, v.Fach) && status != "entfall")
        {
            fach = v.NeuesFach;
            fachAlt = v.Fach;
        }
        if (fach.Length == 0 && v != null) fach = v.NeuesFach.Length > 0 ? v.NeuesFach : v.Fach;

        var lehrer = r.Lehrer.Select(l => new Wert(l)).ToList();
        var raeume = r.Raeume.Select(x => new Wert(x)).ToList();
        if (v != null)
        {
            if (status == "offen")
                lehrer = new List<Wert> { new("", v.Lehrer.Length > 0 ? v.Lehrer : null, true) };
            else if (status == "geaendert" && v.Lehrer.Length > 0 && v.Vertreter.Length > 0 && v.Vertreter is not ("+" or "---") && !Ci.Equals(v.Lehrer, v.Vertreter))
            {
                lehrer = lehrer.Select(l => Ci.Equals(l.K, v.Vertreter) ? new Wert(l.K, v.Lehrer, true) : l).ToList();
                if (!lehrer.Any(l => l.Neu)) lehrer.Insert(0, new Wert(v.Vertreter, v.Lehrer, true));
            }
            else if (status == "entfall" && lehrer.Count == 0 && v.Lehrer.Length > 0)
                lehrer.Add(new Wert(v.Lehrer));
            if (status != "entfall" && v.NeuerRaum.Length > 0 && v.Raum.Length > 0 && !Ci.Equals(v.NeuerRaum, v.Raum))
            {
                raeume = raeume.Select(x => Ci.Equals(x.K, v.NeuerRaum) ? new Wert(x.K, v.Raum, true) : x).ToList();
                if (!raeume.Any(x => x.Neu)) raeume.Insert(0, new Wert(v.NeuerRaum, v.Raum, true));
            }
            else if (raeume.Count == 0 && v.Raum.Length > 0)
                raeume.Add(new Wert(v.Raum));
            // Eigene Stunde wird von jemand anderem gehalten bzw. findet in einem anderen Raum statt
            if (typ == Elementtyp.Lehrer && Ci.Equals(v.Lehrer, kurz) && !Enthaelt(r.Lehrer, kurz) && status is "geaendert" or "offen")
                status = "abwesend";
            if (typ == Elementtyp.Raum && Ci.Equals(v.Raum, kurz) && !Enthaelt(r.Raeume, kurz) && status is "geaendert")
                status = "abwesend";
        }
        var text = string.Join(" · ", new[] { r.Text, v != null && v.Text != r.Text ? v.Text : "" }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        s.Faecher.TryGetValue(fach, out var fe);
        var zb = zeiten.TryGetValue(r.Stunde, out var z) ? z : null;
        return new Karte(Iso(d), r.Stunde, r.Stunde, zb?.Beginn ?? "", zb?.Ende ?? "", status, fach, fachAlt, fe?.Name ?? fach, fe?.Farbe ?? "",
            r.Klassen.Select(k => new Wert(k)).ToList(), lehrer, raeume, art, text, v?.Id ?? r.Vertretung, r.Unterricht, "");
    }

    private static string Schluessel(Karte k) =>
        $"{k.Unterricht}|{k.Status}|{k.Fach}|{k.FachAlt}|{k.Art}|{k.Text}|{string.Join(",", k.Klassen.Select(x => x.K))}|" +
        $"{string.Join(",", k.Lehrer.Select(x => x.K + "/" + x.Alt))}|{string.Join(",", k.Raeume.Select(x => x.K + "/" + x.Alt))}";

    private static List<Karte> Zusammenfassen(List<Karte> einzel, Dictionary<int, WebStundenzeit> zeiten)
    {
        var res = new List<Karte>();
        foreach (var g in einzel.GroupBy(Schluessel))
        {
            Karte? offen = null;
            foreach (var k in g.OrderBy(x => x.Von))
            {
                if (offen != null && k.Von == offen.Bis + 1)
                {
                    offen = offen with { Bis = k.Bis, Ende = k.Ende };
                    continue;
                }
                if (offen != null) res.Add(offen);
                offen = k;
            }
            if (offen != null) res.Add(offen);
        }
        // doppelte Karten (gleiche Stunde, gleicher Inhalt aus mehreren Kopplungszeilen) entfernen
        return res.GroupBy(k => (k.Von, k.Bis, Schluessel(k))).Select(g => g.First()).OrderBy(k => k.Von).ThenBy(k => k.Fach).ToList();
    }

    /// <summary>Pausenaufsichten einer Lehrkraft an einem Tag (regelmäßige und Vertretungen).</summary>
    public List<Aufsicht> Aufsichten(Stand s, DateOnly d, string lehrer)
    {
        var res = new List<Aufsicht>();
        if (Frei(s, d).Frei) return res;
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        (string, string) Pause(int nach) => (zeiten.TryGetValue(nach, out var a) ? a.Ende : "", zeiten.TryGetValue(nach + 1, out var b) ? b.Beginn : "");
        string GangName(string g) => s.Gaenge.TryGetValue(g, out var x) && x.Name.Length > 0 ? x.Name : g;
        var woche = Woche(s, d);
        var t = TagIndex(d);
        var vertretungen = s.Paket.Vertretungen.Where(v => v.Datum == Iso(d) && v.NurLehrer && v.Art.Contains("Pausenaufsicht", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var a in s.Paket.Pausenaufsichten.Where(a => a.Tag == t && Ci.Equals(a.Lehrer, lehrer) && (woche.Length == 0 || a.Wochen.Length == 0 || a.Wochen.Contains(woche))))
        {
            var (b, e) = Pause(a.NachStunde);
            var v = vertretungen.FirstOrDefault(x => x.Stunde == a.NachStunde && Ci.Equals(x.Lehrer, lehrer));
            res.Add(v is null
                ? new Aufsicht(Iso(d), a.NachStunde, b, e, a.Gang, GangName(a.Gang), "regulaer", lehrer, null)
                : new Aufsicht(Iso(d), a.NachStunde, b, e, a.Gang, GangName(a.Gang), "vertreten", v.Vertreter is "" or "???" ? "???" : v.Vertreter, lehrer));
        }
        foreach (var v in vertretungen.Where(x => Ci.Equals(x.Vertreter, lehrer) && !Ci.Equals(x.Lehrer, lehrer)))
        {
            var (b, e) = Pause(v.Stunde);
            var gang = v.NeuerRaum.Length > 0 ? v.NeuerRaum : v.Raum;
            res.Add(new Aufsicht(Iso(d), v.Stunde, b, e, gang, GangName(gang), "vertretung", lehrer, v.Lehrer));
        }
        return res.OrderBy(x => x.NachStunde).ToList();
    }

    /// <summary>Hinweise im Tageskopf: Veranstaltungen, Abwesenheiten (Kollegium), Raumsperren.</summary>
    public List<string> Hinweise(Stand s, DateOnly d, string typ, string kurz, bool kollegium)
    {
        var res = new List<string>();
        string Std(int von, int bis) => bis >= 20 || bis < von ? $"ab {von}. Std." : von == bis ? $"{von}. Std." : $"{von}.–{bis}. Std.";
        foreach (var a in s.Paket.Absenzen.Where(x => DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b && Ci.Equals(x.Element, kurz)))
        {
            if (a.Art == "lehrer" && typ == Elementtyp.Lehrer && kollegium) res.Add($"abwesend {Std(a.StundeVon, a.StundeBis)}{(string.IsNullOrEmpty(a.Grund) ? "" : " – " + a.Grund)}");
            if (a.Art == "klasse" && typ == Elementtyp.Klasse) res.Add($"Klasse abwesend {Std(a.StundeVon, a.StundeBis)}{(string.IsNullOrEmpty(a.Grund) || !kollegium ? "" : " – " + a.Grund)}");
            if (a.Art == "raum" && typ == Elementtyp.Raum) res.Add($"Raum gesperrt {Std(a.StundeVon, a.StundeBis)}");
        }
        return res;
    }

    public Tageskopf Kopf(Stand s, DateOnly d, string typ, string kurz, bool kollegium)
    {
        var (frei, grund) = Frei(s, d);
        return new Tageskopf(Iso(d), Wochentage[TagIndex(d)], Woche(s, d), frei, grund, s.Tage.ContainsKey(Iso(d)),
            frei ? new List<string>() : Hinweise(s, d, typ, kurz, kollegium), frei ? new List<Ereignis>() : Ereignisse(s, d, typ, kurz),
            !frei ? null : Ferien(s, d) is { } f ? (f.Feiertag ? "feiertag" : "ferien") : TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? "wochenende" : "frei");
    }

    /// <summary>
    /// Veranstaltungen eines Tages für ein Element (ohne Klausuren, die als Karten erscheinen). Veranstaltungen ohne
    /// zugeordnete Elemente gelten für die ganze Schule. Mehrtägige Veranstaltungen beginnen am ersten Tag mit
    /// StundeVon und enden am letzten Tag mit StundeBis; dazwischen sind sie ganztägig.
    /// </summary>
    public List<Ereignis> Ereignisse(Stand s, DateOnly d, string typ, string kurz)
    {
        var letzte = s.Paket.Zeitraster.Stunden.Count > 0 ? s.Paket.Zeitraster.Stunden.Max(z => z.Nr) : 10;
        var res = new List<Ereignis>();
        foreach (var t in s.Paket.Termine.Where(x => x.Art != "klausur" && DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b))
        {
            var schulweit = t.Klassen.Count == 0 && t.Lehrer.Count == 0 && t.Raeume.Count == 0;
            var betrifft = schulweit || typ switch
            {
                Elementtyp.Klasse => Enthaelt(t.Klassen, kurz),
                Elementtyp.Lehrer => Enthaelt(t.Lehrer, kurz),
                Elementtyp.Raum => Enthaelt(t.Raeume, kurz),
                _ => kurz.Length == 0,
            };
            if (!betrifft) continue;
            var von = DateOnly.Parse(t.Von) == d ? Math.Max(1, t.StundeVon) : 1;
            var bis = DateOnly.Parse(t.Bis) == d && t.StundeBis > 0 ? Math.Min(t.StundeBis, letzte) : letzte;
            if (bis < von) bis = von;
            res.Add(new Ereignis(t.Titel, t.Art, von, bis, von <= 1 && bis >= letzte, t.Klassen, t.Lehrer, t.Raeume));
        }
        return res.OrderBy(e => !e.Ganztaegig).ThenBy(e => e.Von).ToList();
    }

    /// <summary>Wochenplan eines Elements ab Montag.</summary>
    public Wochenplan Wochenplan(Stand s, string typ, string kurz, DateOnly datum, bool kollegium, Func<DateOnly, IEnumerable<Rohstunde>>? zusaetzlich = null)
    {
        var montag = Montag(datum);
        var tage = Math.Clamp(s.Paket.Zeitraster.TageProWoche, 1, 7);
        var koepfe = new List<Tageskopf>();
        var karten = new List<Karte>();
        var aufsichten = new List<Aufsicht>();
        for (var i = 0; i < tage; i++)
        {
            var d = montag.AddDays(i);
            var kopf = Kopf(s, d, typ, kurz, kollegium);
            koepfe.Add(kopf);
            if (kopf.Frei) continue;
            karten.AddRange(Karten(s, d, typ, kurz, zusaetzlich?.Invoke(d)));
            if (typ == Elementtyp.Lehrer && kollegium) aufsichten.AddRange(Aufsichten(s, d, kurz));
        }
        return new Wochenplan(typ, kurz, Name(s, typ, kurz), Iso(montag), Woche(s, montag), Zeitraster(s), koepfe, karten, aufsichten);
    }

    /// <summary>Tagesübersicht aller Klassen, Lehrkräfte bzw. Räume (nur Elemente mit Stunden).</summary>
    public Tagesuebersicht Tag(Stand s, string typ, DateOnly d, bool kollegium, Func<DateOnly, IEnumerable<Rohstunde>>? zusaetzlich = null)
    {
        var kopf = Kopf(s, d, "", "", kollegium);
        var zeilen = new List<Tageszeile>();
        if (!kopf.Frei)
        {
            var extra = zusaetzlich?.Invoke(d).ToList();
            IEnumerable<WebElement> elemente = typ switch
            {
                Elementtyp.Klasse => s.Paket.Elemente.Klassen,
                Elementtyp.Lehrer => s.Paket.Elemente.Lehrer,
                _ => s.Paket.Elemente.Raeume,
            };
            foreach (var e in elemente.OrderBy(e => e.Kurz, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true)))
            {
                var k = Karten(s, d, typ, e.Kurz, extra);
                if (k.Count > 0) zeilen.Add(new Tageszeile(e.Kurz, e.Name, k));
            }
        }
        return new Tagesuebersicht(typ, Iso(d), kopf, Zeitraster(s), zeilen);
    }
}
