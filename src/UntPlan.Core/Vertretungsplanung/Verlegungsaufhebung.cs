using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Verlegungen rückgängig machen und Folgen einer Lehrer-Absenz:
/// <list type="bullet">
/// <item>„Entfall“ bzw. „-“ bei einer Verlegung: erst fragen, ob die Verlegung aufgehoben werden soll – die Stunde liegt dann
/// wieder an ihrer ursprünglichen Position (kein Entfall).</item>
/// <item>Wird eine Lehrkraft abwesend: Verlegungen (verlegte Stunden, an denen sie beteiligt ist) und Sondereinsätze im
/// Absenzzeitraum ermitteln, damit nachgefragt werden kann, ob sie aufgehoben bzw. gelöscht werden sollen.</item>
/// </list>
/// </summary>
public static class Verlegungsaufhebung
{
    private static bool IstZielArt(VertretungsArt a) => a is VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch;

    /// <summary>Gehört die Zeile zu einer Verlegung (Ziel „Verlegung“/„verlegt“/„Tausch“ oder Ursprung „(Le.) nach“)?</summary>
    public static bool IstVerlegung(Schuldatei d, Vertretung? v) =>
        v?.VerlegungsId is int vid && d.Vertretungen.Any(x => x != v && x.VerlegungsId == vid);

    /// <summary>Zielzeile (verlegte Stunde) einer Verlegung.</summary>
    public static Vertretung? Ziel(Schuldatei d, Vertretung v) =>
        v.VerlegungsId is int vid ? d.Vertretungen.FirstOrDefault(x => x.VerlegungsId == vid && IstZielArt(x.Art) && x.VonDatum.HasValue) : null;

    /// <summary>Ursprungszeile (Entfall mit „nach“) einer Verlegung.</summary>
    public static Vertretung? Ursprung(Schuldatei d, Vertretung v) =>
        v.VerlegungsId is int vid ? d.Vertretungen.FirstOrDefault(x => x.VerlegungsId == vid && !(IstZielArt(x.Art) && x.VonDatum.HasValue)) : null;

    /// <summary>
    /// Alle Zeilen, die beim Aufheben entfernt werden: die Zeilen der Verlegung und – bei einem Tausch – die der Gegenrichtung
    /// (die andere Stunde wurde an den Ursprung dieser Stunde getauscht).
    /// </summary>
    public static List<Vertretung> Zeilen(Schuldatei d, Vertretung v)
    {
        if (v.VerlegungsId is not int vid) return new();
        var ids = new HashSet<int> { vid };
        var gruppe = d.Vertretungen.Where(x => x.VerlegungsId == vid).ToList();
        if (gruppe.Any(x => x.Art == VertretungsArt.Tausch) && gruppe.FirstOrDefault(x => x.Art == VertretungsArt.Tausch && x.VonDatum.HasValue) is { } ziel)
        {
            // Gegenrichtung: Tausch-Ziel an der Ursprungsposition dieser Stunde, das von der Zielposition dieser Stunde kommt
            foreach (var g in d.Vertretungen.Where(x => x.Art == VertretungsArt.Tausch && x.VerlegungsId is int o && o != vid && x.VonDatum.HasValue &&
                                                        x.Datum == ziel.VonDatum && x.Stunde == ziel.VonStunde && x.VonDatum == ziel.Datum && x.VonStunde == ziel.Stunde))
                ids.Add(g.VerlegungsId!.Value);
        }
        return d.Vertretungen.Where(x => x.VerlegungsId is int i && ids.Contains(i)).ToList();
    }

    /// <summary>
    /// Verlegung aufheben: Zielzeile(n) und Ursprung (Entfall „nach“) werden entfernt, die Stunde findet wieder an ihrer
    /// ursprünglichen Position statt. Anschließend werden die betroffenen Tage neu abgeglichen – ist die Lehrkraft am Ursprung
    /// abwesend, entsteht dort wieder eine (offene) Vertretung statt eines Entfalls.
    /// </summary>
    /// <returns>Anzahl der aufgehobenen Verlegungen (0 = keine Verlegung).</returns>
    public static int Aufheben(Schuldatei d, Vertretung v)
    {
        var zeilen = Zeilen(d, v);
        if (zeilen.Count < 2) return 0;
        var tage = zeilen.Select(x => x.Datum).Concat(zeilen.Where(x => x.VonDatum.HasValue).Select(x => x.VonDatum!.Value))
            .Concat(zeilen.Where(x => x.NachDatum.HasValue).Select(x => x.NachDatum!.Value)).Distinct().ToList();
        var anzahl = zeilen.Select(x => x.VerlegungsId).Distinct().Count();
        foreach (var z in zeilen)
        {
            var ziel = IstZielArt(z.Art) && z.VonDatum.HasValue;
            if (!ziel && z.VormerkungId.HasValue)
            {
                // Ursprung einer verplanten Vormerkung: wieder Vormerkung (die Stunde liegt wieder neben dem Plan bereit)
                z.Art = VertretungsArt.Vormerkung;
                z.NachDatum = null;
                z.NachStunde = null;
                z.VerlegungsId = null;
                continue;
            }
            d.Vertretungen.Remove(z);
            // eine aus einer Vormerkung verplante Stunde: Verplanung zurücknehmen
            if (z.VormerkungId is int vmId && z.VonDatum.HasValue && d.Vormerkungen.FirstOrDefault(m => m.Id == vmId) is { } vm) vm.VerplantAm.Remove(z.Datum);
        }
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return anzahl;
    }

    /// <summary>Verlegung über die Position im tagesaktuellen Plan aufheben (Unterricht an Datum/Stunde, Ziel oder Ursprung).</summary>
    public static int Aufheben(Schuldatei d, DateOnly datum, int stunde, int unterrichtId)
    {
        var v = d.Vertretungen.FirstOrDefault(x => x.Datum == datum && x.Stunde == stunde && x.UnterrichtId == unterrichtId && IstVerlegung(d, x));
        return v is null ? 0 : Aufheben(d, v);
    }

    /// <summary>Verlegung an dieser Position (Ziel oder Ursprung) für einen Unterricht, sonst null.</summary>
    public static Vertretung? VerlegungAn(Schuldatei d, DateOnly datum, int stunde, int unterrichtId) =>
        d.Vertretungen.FirstOrDefault(x => x.Datum == datum && x.Stunde == stunde && x.UnterrichtId == unterrichtId && IstVerlegung(d, x));

    private static bool Beteiligt(Vertretung v, int lehrer) => v.LehrerId == lehrer || v.VertreterId == lehrer;

    /// <summary>
    /// Verlegte Stunden (Zielzeilen von Verlegung/Tausch) im Zeitraum einer Lehrer-Absenz, an denen die Lehrkraft beteiligt ist
    /// (eigene verlegte Stunde oder als Lehrkraft der Verlegung eingetragen).
    /// </summary>
    public static List<Vertretung> VerlegungenImZeitraum(Schuldatei d, Abwesenheit a)
    {
        if (a.Art != AbwesenheitsArt.Lehrer) return new();
        return d.Vertretungen.Where(v => IstZielArt(v.Art) && v.VonDatum.HasValue && IstVerlegung(d, v) && Beteiligt(v, a.ElementId) && a.Deckt(v.Datum, v.Stunde))
            .OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
    }

    /// <summary>Sondereinsätze der Lehrkraft im Zeitraum einer Lehrer-Absenz.</summary>
    public static List<Vertretung> SondereinsaetzeImZeitraum(Schuldatei d, Abwesenheit a)
    {
        if (a.Art != AbwesenheitsArt.Lehrer) return new();
        return d.Vertretungen.Where(v => v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung && Beteiligt(v, a.ElementId) && a.Deckt(v.Datum, v.Stunde))
            .OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
    }

    /// <summary>Verlegungen aufheben (Liste der Zielzeilen); bereits mit aufgehobene Tausch-Gegenstücke werden übersprungen.</summary>
    public static int AlleAufheben(Schuldatei d, IEnumerable<int> vertretungsIds)
    {
        var n = 0;
        foreach (var id in vertretungsIds.ToList())
            if (d.Vertretungen.FirstOrDefault(v => v.Id == id) is { } v) n += Aufheben(d, v);
        return n;
    }

    /// <summary>
    /// Sondereinsätze löschen. Unterricht der Lehrkraft, der wegen des Sondereinsatzes entfallen war („wegen Sondereinsatz“),
    /// wird wieder hergestellt; danach werden die Tage neu abgeglichen (abwesende Lehrkraft: offene Vertretung).
    /// </summary>
    public static int SondereinsaetzeLoeschen(Schuldatei d, IEnumerable<int> vertretungsIds)
    {
        var ids = vertretungsIds.ToHashSet();
        var weg = d.Vertretungen.Where(v => ids.Contains(v.Id) && v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung).ToList();
        foreach (var s in weg)
        {
            d.Vertretungen.Remove(s);
            var lehrer = s.VertreterId ?? s.LehrerId;
            d.Vertretungen.RemoveAll(v => v.Datum == s.Datum && v.Stunde == s.Stunde && v.Quelle == VertretungsQuelle.Sondereinsatz && v.IstEntfall &&
                                          lehrer is int l && v.LehrerId == l && !weg.Contains(v) && !d.Vertretungen.Any(x => x != s && !weg.Contains(x) &&
                                              x.Datum == s.Datum && x.Stunde == s.Stunde && x.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung && x.VertreterId == l));
            if (s.VormerkungId is int vmId && d.Vormerkungen.FirstOrDefault(m => m.Id == vmId) is { } vm) vm.VerplantAm.Remove(s.Datum);
        }
        foreach (var t in weg.Select(s => s.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        return weg.Count;
    }

    /// <summary>Kurzbeschreibung einer Zeile für Rückfragen: „Mo 12.10. 3. Std. 5a M (von Di 13.10. 2. Std.)“.</summary>
    public static string Beschreibung(Schuldatei d, Vertretung v)
    {
        string Tag(DateOnly t) => $"{Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {t.Day}.{t.Month}.";
        var klassen = string.Join(",", v.NeueKlassenIds.Select(k => d.Klassen.FirstOrDefault(x => x.Id == k)?.Kurzname).Where(x => x != null));
        var fach = (v.VertretungsfachId ?? v.FachId) is int f ? d.Faecher.FirstOrDefault(x => x.Id == f)?.Kurzname ?? "" : "";
        var text = $"{Tag(v.Datum)} {v.Stunde}. Std. {klassen} {fach}".TrimEnd();
        if (v.VonDatum is DateOnly von) text += $" (von {Tag(von)} {v.VonStunde}. Std.)";
        else if (v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung && v.Text.Length > 0) text += $" – {v.Text}";
        return text;
    }

    /// <summary>Rückfragetext mit Liste der betroffenen Einträge (höchstens 15, danach „… und n weitere“).</summary>
    public static string FrageText(Schuldatei d, string einleitung, IReadOnlyList<Vertretung> liste, string frage) =>
        einleitung + "\n\n" + string.Join("\n", liste.Take(15).Select(v => "• " + Beschreibung(d, v))) +
        (liste.Count > 15 ? $"\n… und {liste.Count - 15} weitere" : "") + "\n\n" + frage;
}
