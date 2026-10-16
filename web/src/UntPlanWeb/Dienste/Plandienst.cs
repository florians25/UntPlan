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
    string Art, string Text, int? Vertretung, int? Unterricht, string Gruppe)
{
    /// <summary>Notizen für Schüler*innen aus dem Klassenbuch (Info-Symbol unten rechts auf der Karte).</summary>
    public string NotizSchueler { get; init; } = "";
    /// <summary>Notizen für Lehrkräfte (nur Kollegium).</summary>
    public string NotizLehrer { get; init; } = "";
    /// <summary>Zu dieser Stunde ist eine Hausaufgabe fällig (Hausaufgaben-Symbol auf der Karte).</summary>
    public bool HausaufgabeFaellig { get; init; }
    /// <summary>Zusätzlicher Unterricht (Stunde nicht im Regelplan) – Badge „ZUSÄTZLICHER UNTERRICHT“ in den Stundendetails.</summary>
    public bool Zusaetzlich { get; init; }
    /// <summary>Abwesende Klassen dieser Stunde (Klasse durchgestrichen, Label „abwesend“); die Stunde selbst findet laut Plan statt.</summary>
    public List<string> AbwesendeKlassen { get; init; } = new();
    /// <summary>Text des Labels bei abwesender Klasse, z. B. „abwesend“ bzw. „abwesend – Exkursion“ (Grund nur fürs Kollegium).</summary>
    public string AbwesendText { get; init; } = "";
}

/// <summary>Pausenaufsicht im Lehrerplan. Status: regulaer, vertreten (fällt für diese Lehrkraft weg), vertretung (zusätzlich übernommen).</summary>
public sealed record Aufsicht(string Datum, int NachStunde, string Beginn, string Ende, string Gang, string GangName, string Status, string Lehrer, string? Alt);

public sealed record Tageskopf(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, bool Tagesaktuell, List<string> Hinweise, List<Ereignis> Ereignisse, string? FreiArt = null, string? FreiVon = null, string? FreiBis = null);

/// <summary>Veranstaltung an einem Tag: ganztägig (Leiste über dem Tag) oder in bestimmten Stunden (Karte im Raster).</summary>
public sealed record Ereignis(string Titel, string Art, int Von, int Bis, bool Ganztaegig, List<string> Klassen, List<string> Lehrer, List<string> Raeume);

public sealed record Zeit(int Nr, string Bezeichnung, string Beginn, string Ende);

public sealed record Wochenplan(string Typ, string Element, string Name, string Montag, string Woche, List<Zeit> Zeitraster, List<Tageskopf> Tage,
    List<Karte> Karten, List<Aufsicht> Aufsichten);

public sealed record Tagesuebersicht(string Typ, string Datum, Tageskopf Kopf, List<Zeit> Zeitraster, List<Tageszeile> Zeilen);
public sealed record Tageszeile(string Kurz, string Name, List<Karte> Karten);

/// <summary>Rohdaten einer Stunde (aus dem tagesaktuellen Plan, dem Regelplan oder einer Vormerkung).</summary>
public sealed record Rohstunde(int Stunde, int? Unterricht, string Fach, IReadOnlyList<string> Klassen, IReadOnlyList<string> Lehrer, IReadOnlyList<string> Raeume,
    string Status, int? Vertretung, string Text, string Art = "", IReadOnlyList<string>? LehrerAlt = null);

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
    /// <summary>Vertretungstext einer Vertretung ohne Lehrkraft („+++“), wenn kein anderer Text vorhanden ist.</summary>
    public const string OhneLehrkraftText = "Vertr. o. Lehrkr.";

    /// <summary>Älterer Hinweistext aus früheren UntPlan-Versionen → aktuelle Bezeichnung.</summary>
    public static string TextNormal(string text) => text.Replace("Vertr. o. Lehrkraft", OhneLehrkraftText, StringComparison.Ordinal);
    /// <summary>Lehrkraft eines zusätzlichen Unterrichts, für den (noch) keine Lehrkraft eingetragen ist.</summary>
    public const string OhneLehrkraftZusatz = "---";
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
        s.Zusatzferien.Concat(s.Paket.Ferien).FirstOrDefault(f => DateOnly.TryParse(f.Von, out var v) && DateOnly.TryParse(f.Bis, out var b) && d >= v && d <= b);

    /// <summary>In UntPlanWeb gepflegter freier Tag (gilt auch an tagesaktuellen Tagen).</summary>
    private static WebFerien? Zusatz(Stand s, DateOnly d) =>
        s.Zusatzferien.FirstOrDefault(f => DateOnly.TryParse(f.Von, out var v) && DateOnly.TryParse(f.Bis, out var b) && d >= v && d <= b);

    /// <summary>Unterrichtsfrei (Wochenende, Ferien, außerhalb des Schuljahres oder im Paket als unterrichtsfrei markiert)?</summary>
    public (bool Frei, string? Grund) Frei(Stand s, DateOnly d)
    {
        if (TagIndex(d) < Math.Max(1, s.Paket.Zeitraster.TageProWoche) && Zusatz(s, d) is { } z) return (true, z.Name);
        if (s.Tage.TryGetValue(Iso(d), out var t))
            return t.Unterrichtstag ? (false, null) : (true, Ferien(s, d)?.Name ?? (TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? null : "unterrichtsfrei"));
        if (TagIndex(d) >= Math.Max(1, s.Paket.Zeitraster.TageProWoche)) return (true, null);
        if (d < s.SchuljahrVon || d > s.SchuljahrBis) return (true, "außerhalb des Schuljahres");
        if (Ferien(s, d) is { } f) return (true, f.Name);
        return (false, null);
    }

    /// <summary>Alle Stunden eines Tages (tagesaktuell, sonst Regelplan der gültigen Periode).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Stand, Dictionary<int, List<string>>> KopplungsKlassenCache = new();

    /// <summary>Klassen je Unterricht über alle Kopplungszeilen (für Zeilen ohne eigene Klassen, z. B. zweite Lehrkraft).</summary>
    private static Dictionary<int, List<string>> KopplungsKlassen(Stand s) => KopplungsKlassenCache.GetValue(s, st =>
        st.Paket.Perioden.SelectMany(p => p.Unterricht).GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => g.SelectMany(u => u.Zeilen).SelectMany(z => z.Klassen).Where(k => k.Length > 0).Distinct(Ci).ToList()));

    private static IReadOnlyList<string> KlassenOderKopplung(Stand s, int? unterricht, IReadOnlyList<string> klassen) =>
        klassen.Count > 0 || unterricht is not int u ? klassen : KopplungsKlassen(s).TryGetValue(u, out var k) ? k : klassen;

    public List<Rohstunde> Rohstunden(Stand s, DateOnly d)
    {
        Rohstunde Aus(WebStunde x) => new(x.Stunde, x.Unterricht, x.Fach, KlassenOderKopplung(s, x.Unterricht, x.Klassen), x.Lehrer, x.Raeume, x.Status, x.Vertretung, x.Text, "", x.LehrerAlt);
        if (s.Tage.TryGetValue(Iso(d), out var tag) && !tag.NurAenderungen)
            return tag.Unterrichtstag ? tag.Stunden.Select(Aus).ToList() : new List<Rohstunde>();
        if (tag is { NurAenderungen: true })
        {
            // nur Vertretungen übertragen: die geänderten Stunden über den zuletzt übertragenen Stundenplan legen
            if (!tag.Unterrichtstag) return new List<Rohstunde>();
            var basis = RegelStunden(s, d);
            foreach (var x in tag.Stunden)
            {
                var betroffen = new HashSet<string>(x.Lehrer.Concat(x.LehrerAlt ?? new List<string>()), Ci);
                var gleich = basis.Where(b => b.Stunde == x.Stunde && b.Unterricht == x.Unterricht && x.Unterricht.HasValue).ToList();
                var treffer = gleich.Where(b => b.Lehrer.Any(betroffen.Contains)).ToList();
                if (treffer.Count == 0) treffer = gleich.Where(b => Ci.Equals(b.Fach, x.Fach)).Take(1).ToList();
                foreach (var b in treffer) basis.Remove(b);
                basis.Add(Aus(x));
            }
            return basis.OrderBy(b => b.Stunde).ToList();
        }
        return RegelStunden(s, d);
    }

    /// <summary>Stunden laut zuletzt übertragenem Periodenstundenplan (ohne Vertretungen).</summary>
    private List<Rohstunde> RegelStunden(Stand s, DateOnly d)
    {
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
                res.Add(new Rohstunde(ps.Stunde, u.Id, z.Fach, KlassenOderKopplung(s, u.Id, z.Klassen), z.Lehrer.Length > 0 ? new List<string> { z.Lehrer } : new List<string>(), raeume, "regulaer", null, ""));
            }
        }
        // Klausuren aus den Terminen (im tagesaktuellen Plan sind sie bereits enthalten)
        foreach (var k in s.Paket.Termine.Where(x => x.Art == "klausur" && DateOnly.TryParse(x.Von, out var v) && v == d))
            for (var st = k.StundeVon; st <= Math.Max(k.StundeVon, k.StundeBis); st++)
                res.Add(new Rohstunde(st, null, "", k.Klassen, k.Lehrer, k.Raeume, "zusaetzlich", null, k.Titel, "Klausur"));
        return res;
    }

    private static bool Enthaelt(IReadOnlyList<string> liste, string kurz) => liste.Any(x => Ci.Equals(x, kurz));

    public static bool IstBereitschaft(Stand s, string fach) =>
        fach.Equals("BER", StringComparison.OrdinalIgnoreCase) || (s.Faecher.TryGetValue(fach, out var f) && f.Name.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase));

    /// <summary>Karten eines Elements an einem Tag; zusammenhängende gleiche Stunden werden zu Blöcken zusammengefasst.</summary>
    public List<Karte> Karten(Stand s, DateOnly d, string typ, string kurz, IEnumerable<Rohstunde>? zusaetzlich = null)
    {
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        var einzel = new List<Karte>();
        var vorgemerkt = new List<(Rohstunde R, Karte K)>();
        var roh = Rohstunden(s, d).Concat(zusaetzlich ?? Enumerable.Empty<Rohstunde>()).ToList();
        // Kopplung mit gleichem Fach und gleichem Raum: im Lehrerplan auch die Zeilen der Kolleg*innen (eine gemeinsame Karte)
        static bool GleicherRaum(Rohstunde a, Rohstunde b) => a.Raeume.Count == b.Raeume.Count && a.Raeume.All(x => Enthaelt(b.Raeume, x));
        bool Mitgekoppelt(Rohstunde r) => r.Unterricht is int u && r.Fach.Length > 0 && r.Status is not ("entfall" or "vorgemerkt") &&
            roh.Any(x => !ReferenceEquals(x, r) && x.Stunde == r.Stunde && x.Unterricht == u && x.Status is not ("entfall" or "vorgemerkt") && Ci.Equals(x.Fach, r.Fach) && GleicherRaum(x, r) && Enthaelt(x.Lehrer, kurz));
        foreach (var r in roh)
        {
            WebVertretung? v = r.Vertretung is int vid && s.Vertretungen.TryGetValue(vid, out var vx) ? vx : null;
            var betrifft = typ switch
            {
                Elementtyp.Klasse => Enthaelt(r.Klassen, kurz),
                Elementtyp.Lehrer => Enthaelt(r.Lehrer, kurz) || (v != null && Ci.Equals(v.Lehrer, kurz)) || Mitgekoppelt(r),
                // Veranstaltung mit Raum: der Raum steht (bei älteren Paketen) nur in der Vertretung
                Elementtyp.Raum => Enthaelt(r.Raeume, kurz) || (v != null && (RaumListe(v.Raum).Contains(kurz, Ci) || (r.Raeume.Count == 0 && r.Status != "entfall" && RaumListe(v.NeuerRaum).Contains(kurz, Ci)))),
                _ => false,
            };
            // vorgemerkte Raumänderung eines Unterrichts: wird unten auf die Stunde gelegt, falls diese im Plan steht
            if (!betrifft && !(r.Status == "vorgemerkt" && r.Unterricht.HasValue && r.Art != "Klausur")) continue;
            var k = KarteBauen(s, d, typ, kurz, r, v, zeiten);
            if (r.Status == "vorgemerkt") vorgemerkt.Add((r, k));
            else
            {
                einzel.Add(k);
                if (Fachwechsel(k, typ, kurz) is { } alt) einzel.Add(alt);
            }
        }
        // Vorgemerkte Klausuren und Raumbuchungen zu einer Unterrichtsstunde direkt auf die Stundenkarte legen.
        // Raumbuchung zu einem Unterricht (Stundendetails: Ersetzen, Löschen, zusätzlicher Raum) = reine Raumänderung:
        // nur die Räume ändern sich (alt → neu), kein Vertretungstext, kein zusätzlicher Eintrag im Stundenplan.
        foreach (var (r, k) in vorgemerkt)
        {
            var raumbuchung = r.Art != "Klausur";
            int i;
            if (raumbuchung && r.Unterricht is int uz)
            {
                i = einzel.FindIndex(x => x.Unterricht == uz && r.Stunde >= x.Von && r.Stunde <= x.Bis && x.Status is not ("entfall" or "abwesend"));
                // Raumplan des neuen Raums: die hierher verlegte Stunde anzeigen
                if (i < 0 && typ == Elementtyp.Raum && Enthaelt(r.Raeume, kurz) &&
                    roh.FirstOrDefault(x => x.Unterricht == uz && x.Stunde == r.Stunde && x.Status is not ("entfall" or "vorgemerkt")) is { } basis)
                {
                    WebVertretung? bv = basis.Vertretung is int bid && s.Vertretungen.TryGetValue(bid, out var bx) ? bx : null;
                    einzel.Add(KarteBauen(s, d, typ, kurz, basis, bv, zeiten));
                    i = einzel.Count - 1;
                }
                if (i < 0) continue;
            }
            else
                i = r.Klassen.Count == 0 ? -1 : einzel.FindIndex(x => x.Von == r.Stunde && x.Unterricht.HasValue && x.Status is not ("entfall" or "abwesend")
                                                                      && x.Klassen.Any(w => Enthaelt(r.Klassen, w.K)));
            if (i < 0)
            {
                // reine Raumbuchung ohne Unterricht: nur im Raumplan, kein zusätzlicher Eintrag im eigenen Stundenplan
                if (!raumbuchung || typ == Elementtyp.Raum) einzel.Add(k);
                continue;
            }
            var ziel = einzel[i];
            if (!raumbuchung)
            {
                var kr = r.Raeume.Count == 0 ? ziel.Raeume
                    : r.Raeume.Select(x => ziel.Raeume.FirstOrDefault(w => Ci.Equals(w.K, x)) ?? new Wert(x, ziel.Raeume.FirstOrDefault()?.K ?? "", true)).ToList();
                einzel[i] = ziel with { Status = "klausur", Art = "Klausur (vorgemerkt)", Text = r.Text, Raeume = kr };
                continue;
            }
            var raeume = RaeumeNachBuchung(ziel.Raeume, r.Raeume);
            var status = ziel.Status == "regulaer" ? "geaendert" : ziel.Status;
            // Raumplan des bisherigen Raums: die Stunde findet woanders statt
            if (typ == Elementtyp.Raum && !raeume.Any(w => Ci.Equals(w.K, kurz))) status = "abwesend";
            einzel[i] = ziel with { Status = status, Raeume = raeume, Art = ziel.Art.Length > 0 ? ziel.Art : "Raumänderung (vorgemerkt)" };
        }
        // Teilnahmen mehrerer Lehrkräfte an derselben Veranstaltung: eine Karte mit allen Beteiligten
        var veranstaltungen = einzel.Where(k => k.Status == "veranstaltung").GroupBy(k => (k.Von, k.Text))
            .Select(g => g.First() with
            {
                Klassen = g.SelectMany(k => k.Klassen).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Lehrer = g.SelectMany(k => k.Lehrer).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
                Raeume = g.SelectMany(k => k.Raeume).DistinctBy(w => w.K.ToLowerInvariant()).ToList(),
            }).ToList();
        einzel = einzel.Where(k => k.Status != "veranstaltung").Concat(veranstaltungen.Select(k => MitTermin(s, d, k))).ToList();
        return KlassenAbwesenheit(s, d, Zusammenfassen(Kopplungen(einzel), zeiten));
    }

    /// <summary>Abwesende Klassen (Absenzen aus UntPlan) auf den Karten markieren – echte Entfälle und Veranstaltungen bleiben unverändert.</summary>
    private static List<Karte> KlassenAbwesenheit(Stand s, DateOnly d, List<Karte> karten)
    {
        var absenzen = s.Paket.Absenzen.Where(a => a.Art == "klasse" && DateOnly.TryParse(a.Von, out var v) && DateOnly.TryParse(a.Bis, out var b) && d >= v && d <= b).ToList();
        if (absenzen.Count == 0) return karten;
        bool Trifft(WebAbsenz a, int von, int bis) => a.StundeVon <= bis && (a.StundeBis >= von || a.StundeBis < a.StundeVon || a.StundeBis >= 20);
        return karten.Select(k =>
        {
            if (k.Status is "entfall" or "veranstaltung" or "vorgemerkt" or "buchung") return k;
            var treffer = absenzen.Where(a => k.Klassen.Any(w => Ci.Equals(w.K, a.Element)) && Trifft(a, k.Von, k.Bis)).ToList();
            if (treffer.Count == 0) return k;
            var grund = treffer.Select(a => a.Grund).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));
            var weg = treffer.Select(a => a.Element).ToHashSet(Ci);
            // alle Klassen der Stunde abwesend: die Stunde entfällt (wie ein Entfall aus der Vertretungsplanung)
            if (k.Klassen.Count > 0 && k.Klassen.All(w => weg.Contains(w.K)))
                return k with
                {
                    Status = "entfall", Art = "Klasse abwesend",
                    AbwesendeKlassen = k.Klassen.Select(w => w.K).ToList(), AbwesendText = grund is null ? "Klasse abwesend" : $"Klasse abwesend – {grund}",
                };
            return k with
            {
                AbwesendeKlassen = treffer.Select(a => k.Klassen.First(w => Ci.Equals(w.K, a.Element)).K).Distinct(Ci).ToList(),
                AbwesendText = grund is null ? "abwesend" : $"abwesend – {grund}",
            };
        }).ToList();
    }

    /// <summary>
    /// Räume einer Stunde nach einer Raumbuchung (neu = vollständige neue Raumliste): ersetzte Räume als „alt → neu“,
    /// zusätzliche Räume als neuer Wert, entfernte Räume durchgestrichen.
    /// </summary>
    /// <summary>Räume aus einem Feld wie „D017,C151“ bzw. „D017, C151“.</summary>
    internal static List<string> RaumListe(string? text) =>
        (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(Ci).ToList();

    internal static List<Wert> RaeumeNachBuchung(List<Wert> bisher, IReadOnlyList<string> neu)
    {
        // ursprünglicher Raum je bisherigem Wert (bei schon geändertem Raum der Wert vor der Änderung)
        var entfernt = bisher.Where(w => w.K.Length > 0 && !Enthaelt(neu, w.K)).Select(w => w.Alt is { Length: > 0 } a ? a : w.K).ToList();
        var res = new List<Wert>();
        var j = 0;
        foreach (var x in neu.Where(x => x.Length > 0).Distinct(Ci))
            res.Add(bisher.FirstOrDefault(w => Ci.Equals(w.K, x)) ?? new Wert(x, j < entfernt.Count ? entfernt[j++] : null, true));
        for (; j < entfernt.Count; j++) res.Add(new Wert("", entfernt[j], true));
        return res;
    }

    /// <summary>
    /// Fachwechsel wie im Original: Zur geänderten Stunde mit neuem Fach erscheint die ursprüngliche Stunde als Entfall daneben
    /// (altes Fach, ursprüngliche Lehrkräfte und Räume). Im Lehrerplan nur, wenn die Lehrkraft die ursprüngliche Stunde gehalten hätte.
    /// </summary>
    private static Karte? Fachwechsel(Karte k, string typ, string kurz)
    {
        if (k.Status != "geaendert" || string.IsNullOrEmpty(k.FachAlt) || Ci.Equals(k.FachAlt, k.Fach)) return null;
        static List<Wert> Urspruenglich(IEnumerable<Wert> l) => l.Select(w => w.Alt is { Length: > 0 } a ? a : w.K).Where(x => x.Length > 0).Distinct(Ci).Select(x => new Wert(x)).ToList();
        var lehrer = Urspruenglich(k.Lehrer);
        var raeume = Urspruenglich(k.Raeume);
        if (typ == Elementtyp.Lehrer && !lehrer.Any(w => Ci.Equals(w.K, kurz))) return null;
        if (typ == Elementtyp.Raum && !raeume.Any(w => Ci.Equals(w.K, kurz))) return null;
        return k with
        {
            Status = "entfall", Fach = k.FachAlt!, FachAlt = null, FachName = k.FachAlt!, Farbe = k.Farbe, Lehrer = lehrer, Raeume = raeume,
            Klassen = k.Klassen.Select(w => new Wert(w.Alt is { Length: > 0 } a ? a : w.K)).Where(w => w.K.Length > 0).ToList(),
            Art = "Entfall", Text = "", Unterricht = null,
        };
    }

    /// <summary>
    /// Kopplungen: Stunden zur selben Zeit werden nur dann auf einer Karte zusammengefasst, wenn Fach UND Raum gleich sind –
    /// dann stehen alle Lehrkräfte und Klassen auf der Karte. Gekoppelte Stunden mit unterschiedlichen Fächern (z. B. KR und ER)
    /// oder in unterschiedlichen Räumen bleiben einzelne Karten. Entfall und abgegebene Stunden werden nur untereinander zusammengefasst.
    /// </summary>
    internal static List<Karte> Kopplungen(List<Karte> einzel)
    {
        static string Gruppe(string status) => status is "regulaer" or "geaendert" or "offen" ? "laeuft" : status;
        static string Raeume(Karte k) => string.Join(",", k.Raeume.Select(w => (w.K.Length > 0 ? w.K : w.Alt ?? "").ToLowerInvariant()).Where(x => x.Length > 0).Distinct().OrderBy(x => x, StringComparer.Ordinal));
        static List<Wert> Vereinigen(IEnumerable<Wert> l) => l.GroupBy(w => (w.K.ToLowerInvariant(), (w.Alt ?? "").ToLowerInvariant())).Select(g => g.First()).ToList();
        var res = new List<Karte>();
        foreach (var g in einzel.GroupBy(k => k.Unterricht is int && k.Fach.Length > 0
                     ? $"{k.Datum}|{k.Von}|{k.Bis}|{Gruppe(k.Status)}|{k.Fach.ToLowerInvariant()}|{Raeume(k)}" : Guid.NewGuid().ToString()))
        {
            var liste = g.ToList();
            if (liste.Count == 1) { res.AddRange(liste); continue; }
            var f = liste[0];
            var texte = liste.Select(x => x.Text).Where(x => x.Length > 0).Distinct().ToList();
            res.Add(f with
            {
                Status = liste.Any(x => x.Status == "offen") ? "offen" : liste.Any(x => x.Status == "geaendert") ? "geaendert" : f.Status,
                FachAlt = liste.Select(x => x.FachAlt).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                Klassen = Vereinigen(liste.SelectMany(x => x.Klassen)),
                Lehrer = Vereinigen(liste.SelectMany(x => x.Lehrer)),
                Raeume = Vereinigen(liste.SelectMany(x => x.Raeume)),
                Art = liste.Select(x => x.Art).FirstOrDefault(x => x.Length > 0) ?? "",
                Text = string.Join(" · ", texte),
                Vertretung = liste.Select(x => x.Vertretung).FirstOrDefault(x => x.HasValue),
            });
        }
        return res;
    }

    /// <summary>Veranstaltungskarte um Räume, Lehrkräfte (und Klassen) der zugehörigen Veranstaltung aus den Terminen ergänzen.</summary>
    private static Karte MitTermin(Stand s, DateOnly d, Karte k)
    {
        var t = s.Paket.Termine.FirstOrDefault(x => x.Art != "klausur" && Ci.Equals(x.Titel.Trim(), k.Text.Trim()) &&
                                                    DateOnly.TryParse(x.Von, out var v) && DateOnly.TryParse(x.Bis, out var b) && d >= v && d <= b);
        if (t is null) return k;
        var raeume = k.Raeume.Concat(t.Raeume.Where(r => !k.Raeume.Any(w => Ci.Equals(w.K, r))).Select(r => new Wert(r))).ToList();
        var klassen = k.Klassen.Count > 0 ? k.Klassen : t.Klassen.Select(x => new Wert(x)).ToList();
        // alle teilnehmenden Lehrkräfte der Veranstaltung (nicht nur die des angezeigten Plans)
        var lehrer = k.Lehrer.Concat(t.Lehrer.Where(l => !k.Lehrer.Any(w => Ci.Equals(w.K, l))).Select(l => new Wert(l))).ToList();
        return k with { Raeume = raeume, Klassen = klassen, Lehrer = lehrer };
    }

    /// <summary>
    /// Veranstaltungen, die ausdrücklich für dieses Element gelten (nicht schulweit) und noch nicht als Karte vorhanden sind –
    /// für die Tagesübersicht, damit z. B. ein Raum mit Veranstaltung dort erscheint.
    /// </summary>
    public List<Karte> EreignisKarten(Stand s, DateOnly d, string typ, string kurz, IReadOnlyCollection<Karte> vorhanden)
    {
        var zeiten = s.Paket.Zeitraster.Stunden.ToDictionary(z => z.Nr);
        return Ereignisse(s, d, typ, kurz)
            .Where(e => e.Klassen.Count + e.Lehrer.Count + e.Raeume.Count > 0 && !vorhanden.Any(k => k.Status == "veranstaltung" && Ci.Equals(k.Text, e.Titel)))
            .Select(e => new Karte(Iso(d), e.Von, e.Bis, zeiten.TryGetValue(e.Von, out var a) ? a.Beginn : "", zeiten.TryGetValue(e.Bis, out var b) ? b.Ende : "",
                "veranstaltung", "", null, "", "", e.Klassen.Select(x => new Wert(x)).ToList(),
                e.Lehrer.Select(x => new Wert(x)).ToList(), e.Raeume.Select(x => new Wert(x)).ToList(),
                "Veranstaltung", e.Titel, null, null, ""))
            .ToList();
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
        // Vertretung ohne Lehrkraft („+++“): die Stunde findet statt, die bisherige Lehrkraft steht durchgestrichen in der grünen Plakette
        var bisher = v != null && v.Lehrer.Length > 0 ? new List<string> { v.Lehrer } : r.LehrerAlt?.Where(x => x.Length > 0).ToList() ?? new List<string>();
        var ohneLehrer = r.Status == "vertreten" && r.Lehrer.Count == 0 && bisher.Count > 0 && (v is null || v.Vertreter is "" or "+" or "+++");
        if (ohneLehrer)
            lehrer = bisher.Select(x => new Wert("", x, true)).ToList();
        if (v != null)
        {
            if (status == "offen")
                lehrer = new List<Wert> { new("", v.Lehrer.Length > 0 ? v.Lehrer : null, true) };
            else if (ohneLehrer) { }
            else if (status == "geaendert" && v.Lehrer.Length > 0 && v.Vertreter.Length > 0 && v.Vertreter is not ("+" or "+++" or "---" or "???") && !Ci.Equals(v.Lehrer, v.Vertreter))
            {
                lehrer = lehrer.Select(l => Ci.Equals(l.K, v.Vertreter) ? new Wert(l.K, v.Lehrer, true) : l).ToList();
                if (!lehrer.Any(l => l.Neu)) lehrer.Insert(0, new Wert(v.Vertreter, v.Lehrer, true));
            }
            else if (status == "entfall" && lehrer.Count == 0 && v.Lehrer.Length > 0)
                lehrer.Add(new Wert(v.Lehrer));
            // mehrere Räume kommen als „D017,C151“: einzeln vergleichen – unveränderte Räume bleiben, ersetzte als „alt → neu“,
            // zusätzliche als neuer Raum, entfernte durchgestrichen (keine doppelten Räume)
            var alteR = RaumListe(v.Raum);
            var neueR = RaumListe(v.NeuerRaum);
            if (status != "entfall" && neueR.Count > 0 && alteR.Count > 0 && !(alteR.Count == neueR.Count && alteR.All(x => neueR.Contains(x, Ci))) && v.NeuerRaum != "???")
            {
                if (neueR.Count == 1 && alteR.Count == 1)
                {
                    raeume = raeume.Select(x => Ci.Equals(x.K, neueR[0]) ? new Wert(x.K, alteR[0], true) : x).ToList();
                    if (!raeume.Any(x => x.Neu)) raeume.Insert(0, new Wert(neueR[0], alteR[0], true));
                }
                else raeume = RaeumeNachBuchung(alteR.Select(x => new Wert(x)).ToList(), neueR);
            }
            else if (raeume.Count == 0 && (v.Raum.Length > 0 || v.NeuerRaum.Length > 0))
                raeume.Add(new Wert(v.NeuerRaum.Length > 0 && status != "entfall" ? v.NeuerRaum : v.Raum));
            // offene Raumvertretung (Raum „???“): wie eine offene Lehrervertretung – bisheriger Raum durchgestrichen
            if (status != "entfall" && (v.NeuerRaum == "???" || raeume.Any(x => x.K == "???")))
            {
                var bisherRaum = v.Raum.Length > 0 && v.Raum != "???" ? v.Raum : null;
                raeume = raeume.Where(x => x.K != "???").ToList();
                raeume.RemoveAll(x => bisherRaum != null && Ci.Equals(x.K, bisherRaum));
                raeume.Insert(0, new Wert("", bisherRaum ?? "???", true));
                if (status is "regulaer" or "geaendert") status = "offen";
            }
            // Eigene Stunde wird von jemand anderem gehalten bzw. findet in einem anderen Raum statt
            if (typ == Elementtyp.Lehrer && Ci.Equals(v.Lehrer, kurz) && !Enthaelt(r.Lehrer, kurz) && status is "geaendert" or "offen")
                status = "abwesend";
            if (typ == Elementtyp.Raum && RaumListe(v.Raum).Contains(kurz, Ci) && !Enthaelt(r.Raeume, kurz) && status is "geaendert")
                status = "abwesend";
        }
        // Bereitschaft fällt weg: kein Entfall, sondern Abwesenheit der eingeteilten Lehrkraft (Kürzel durchgestrichen)
        var bereitschaft = IstBereitschaft(s, fach) || (v != null && v.Art.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase));
        if (bereitschaft && status == "entfall")
        {
            status = "abwesend";
            var eingeteilt = lehrer.Select(w => w.K.Length > 0 ? w.K : w.Alt ?? "").Where(x => x.Length > 0).ToList();
            if (eingeteilt.Count == 0 && v != null && v.Lehrer.Length > 0) eingeteilt.Add(v.Lehrer);
            lehrer = eingeteilt.Distinct(Ci).Select(x => new Wert("", x)).ToList();
        }
        // zusätzlicher Unterricht ohne Lehrkraft: „---“
        var zusatz = r.Status == "zusaetzlich" && status == "geaendert";
        if (zusatz && lehrer.Count == 0) lehrer.Add(new Wert(OhneLehrkraftZusatz));
        var text = string.Join(" · ", new[] { r.Text, v != null && v.Text != r.Text ? v.Text : "" }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        // Vertretung ohne Lehrkraft ohne eigenen Text: Hinweis als Vertretungstext
        if (ohneLehrer && text.Length == 0) text = OhneLehrkraftText;
        text = TextNormal(text);
        s.Faecher.TryGetValue(fach, out var fe);
        var zb = zeiten.TryGetValue(r.Stunde, out var z) ? z : null;
        return new Karte(Iso(d), r.Stunde, r.Stunde, zb?.Beginn ?? "", zb?.Ende ?? "", status, fach, fachAlt, fe?.Name ?? fach, fe?.Farbe ?? "",
            r.Klassen.Select(k => new Wert(k)).ToList(), lehrer, raeume, art, text, v?.Id ?? r.Vertretung, r.Unterricht, Gruppe(s, r))
        { Zusaetzlich = zusatz };
    }

    /// <summary>Schülergruppe des Unterrichts (Kopplungszeile mit gleichem Fach bzw. gleicher Lehrkraft), sonst leer.</summary>
    private static string Gruppe(Stand s, Rohstunde r)
    {
        if (r.Unterricht is not int u || !s.Unterricht.TryGetValue(u, out var un)) return "";
        var zeile = un.Zeilen.FirstOrDefault(z => Ci.Equals(z.Fach, r.Fach) && r.Lehrer.Any(l => Ci.Equals(l, z.Lehrer)))
                    ?? un.Zeilen.FirstOrDefault(z => Ci.Equals(z.Fach, r.Fach)) ?? (un.Zeilen.Count == 1 ? un.Zeilen[0] : null);
        return zeile?.Gruppe ?? "";
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
            !frei ? null : Ferien(s, d) is { } f ? (f.Feiertag ? "feiertag" : "ferien") : TagIndex(d) >= s.Paket.Zeitraster.TageProWoche ? "wochenende" : "frei",
            frei ? Ferien(s, d)?.Von : null, frei ? Ferien(s, d)?.Bis : null);
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
                k = k.Concat(EreignisKarten(s, d, typ, e.Kurz, k)).OrderBy(x => x.Von).ToList();
                if (k.Count > 0) zeilen.Add(new Tageszeile(e.Kurz, e.Name, k));
            }
        }
        return new Tagesuebersicht(typ, Iso(d), kopf, Zeitraster(s), zeilen);
    }
}
