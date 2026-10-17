using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;

namespace UntPlan.Core.Austausch;

/// <summary>Spalte „Art“ im Fenster „Stundenplanänderungen aus UntPlanWeb“.</summary>
public enum StplAenderungsArt
{
    /// <summary>Änderung eines bestehenden Unterrichts (Raumänderung) → Raumvertretung.</summary>
    Unterricht,
    /// <summary>Zusätzlicher Unterricht bzw. Buchung ohne Unterricht → Sondereinsatz.</summary>
    Aktivitaet,
    /// <summary>Prüfung → Klausur.</summary>
    Pruefung,
}

/// <summary>Eine Zeile im Fenster „Stundenplanänderungen aus UntPlanWeb“.</summary>
public sealed record StplAenderung(WebRueckmeldung Meldung, DateOnly Datum, int StundeVon, int StundeBis, string Zeit, StplAenderungsArt Art, int? UnterrichtId, int? ZeilenIndex,
    string KlassenFach, string Lehrer, string Raeume, string Wert, string Statistik, string Text, string Ersteller, bool? Uebernommen, string? Fehler)
{
    public string Id => Meldung.Id;
    public string Stunde => StundeVon == StundeBis ? StundeVon.ToString() : $"{StundeVon}-{StundeBis}";
    public string ArtText => Meldung.Typ == WebRueckmeldungsTypen.BuchungStorno ? "Storno"
        : Art switch { StplAenderungsArt.Unterricht => "Unterricht", StplAenderungsArt.Aktivitaet => "Aktivität", _ => "Prüfung" };
    /// <summary>Bereits übernommen oder ignoriert.</summary>
    public bool Erledigt => Uebernommen.HasValue;
}

/// <summary>
/// Stundenplanänderungen aus UntPlanWeb: Raumbuchungen, zusätzliche Unterrichte (Aktivitäten) und Prüfungen. Quelle sind die
/// Rückmeldungen (<see cref="UntPlanWebExport.RueckmeldungenAsync"/>). Beim Übernehmen entsteht eine Raumvertretung
/// (ohne Vertretungstext), ein Sondereinsatz oder eine Klausur; übernommene und ignorierte Änderungen werden in der
/// Datei vermerkt und nicht erneut importiert.
/// </summary>
public static class StundenplanAenderungen
{
    public static readonly string[] Typen = { WebRueckmeldungsTypen.Raumbuchung, WebRueckmeldungsTypen.Klausur, WebRueckmeldungsTypen.KlausurLoeschen, WebRueckmeldungsTypen.BuchungStorno };

    /// <summary>Löschmeldungen (Prüfung löschen, Buchung storniert) werden vor allen anderen Meldungen desselben Imports ausgeführt.</summary>
    public static bool IstLoeschung(WebRueckmeldung m) => m.Typ is WebRueckmeldungsTypen.KlausurLoeschen or WebRueckmeldungsTypen.BuchungStorno;

    /// <summary>Präfix der Kennung einer Storno-Rückmeldung („storno-buchung-42“ bezieht sich auf „buchung-42“).</summary>
    public const string StornoPraefix = "storno-";

    /// <summary>Kennung der ursprünglichen (stornierten) Rückmeldung: <see cref="WebRueckmeldung.BezugId"/>, sonst aus der Kennung abgeleitet.</summary>
    public static string? BezugId(WebRueckmeldung m) =>
        !string.IsNullOrWhiteSpace(m.BezugId) ? m.BezugId.Trim()
        : m.Id.StartsWith(StornoPraefix, StringComparison.Ordinal) && m.Id.Length > StornoPraefix.Length ? m.Id[StornoPraefix.Length..] : null;

    /// <summary>
    /// Stornierte Buchung aus UntPlanWeb entfernen (Rückmeldung <see cref="WebRueckmeldungsTypen.BuchungStorno"/>): alle Zeilen, die aus der
    /// ursprünglichen Rückmeldung entstanden sind (gleiche Web-Kennung) – Raumbuchung, Sondereinsatz (Aktivität, zusätzlicher Unterricht) bzw.
    /// Raumänderung eines Unterrichts –, auch zu einer Prüfung gewordene Buchungen (sie kehren beim Löschen der Prüfung nicht zurück).
    /// Eine Raumänderung in einer sonst geänderten Stunde (z. B. mit Vertretungslehrkraft) behält die Zeile, nur der ursprüngliche Raum
    /// kehrt zurück. Ohne gespeicherte Kennung (ältere Übernahmen) wird die Buchung über Datum, Stunden, Raum, Lehrkraft und Klassen gesucht –
    /// nur Raumbuchungen und Sondereinsätze ohne Unterricht und ohne fremde Web-Kennung. Idempotent: ist nichts (mehr) vorhanden, ändert sich nichts.
    /// Betroffene Tage kommen nach <paramref name="tage"/>. Ergebnis: Anzahl entfernter bzw. zurückgesetzter Zeilen.
    /// </summary>
    public static int BuchungStornieren(Schuldatei d, WebRueckmeldung m, ISet<DateOnly> tage)
    {
        var bezug = BezugId(m);
        var zeilen = string.IsNullOrEmpty(bezug) ? new List<Vertretung>() : d.Vertretungen.Where(v => v.KlausurId is null && v.WebId == bezug).ToList();
        var inKlausur = 0;
        if (!string.IsNullOrEmpty(bezug))
            foreach (var k in d.Klausuren)
            {
                var n = k.Buchungen.RemoveAll(b => b.WebId == bezug);
                if (n > 0) { inKlausur += n; tage.Add(k.Datum); }
            }
        if (zeilen.Count == 0 && inKlausur == 0) zeilen = BuchungSuchen(d, m);
        foreach (var v in zeilen)
        {
            tage.Add(v.Datum);
            var art = v.ArtVorKlassenabwesenheit ?? v.Art;
            var reineBuchung = art == VertretungsArt.Raumbuchung || (art == VertretungsArt.Sondereinsatz && v.UnterrichtId is null)
                               || (art == VertretungsArt.Raumvertretung && (v.VertreterId is null || v.VertreterId == v.LehrerId));
            if (reineBuchung) { d.Vertretungen.Remove(v); continue; }
            // Raumänderung in einer sonst geänderten Stunde: nur den ursprünglichen Raum wiederherstellen
            v.VertretungsraumId = v.RaumId;
            v.ZusatzraumIds.Clear();
            v.RaumVerdraengt = false;
            v.WebId = "";
            v.Geaendert = DateTime.Now;
        }
        return zeilen.Count + inKlausur;
    }

    /// <summary>Übernommene Buchung ohne gespeicherte Web-Kennung über ihre Daten wiederfinden (nur eindeutig beschriebene Buchungen).</summary>
    private static List<Vertretung> BuchungSuchen(Schuldatei d, WebRueckmeldung m)
    {
        var leer = new List<Vertretung>();
        if (!DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var datum) || m.StundeVon is not int von) return leer;
        var bis = Math.Max(von, m.StundeBis ?? von);
        var raumNamen = m.Raeume.Append(m.Raum ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var raeume = raumNamen.Select(r => Id(d, ElementTyp.Raum, r)).ToList();
        var lehrer = string.IsNullOrWhiteSpace(m.Lehrer) ? null : Id(d, ElementTyp.Lehrer, m.Lehrer);
        var klassen = m.Klassen.Select(k => Id(d, ElementTyp.Klasse, k)).ToList();
        // unbekannte Elemente bzw. weder Raum noch Lehrkraft: nicht raten
        if (raeume.Any(r => r is null) || klassen.Any(k => k is null) || (!string.IsNullOrWhiteSpace(m.Lehrer) && lehrer is null)) return leer;
        if (raeume.Count == 0 && lehrer is null) return leer;
        var klassenSet = klassen.OfType<int>().ToHashSet();
        var titel = string.Join(" – ", new[] { m.Titel, m.Text }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return d.Vertretungen.Where(v => v.KlausurId is null && string.IsNullOrEmpty(v.WebId) && v.Datum == datum && v.Stunde >= von && v.Stunde <= bis
                                         && (v.Art == VertretungsArt.Raumbuchung || (v.Art == VertretungsArt.Sondereinsatz && v.UnterrichtId is null))
                                         && (raeume.Count == 0 || v.VertretungsraumId == raeume[0])
                                         && (lehrer is null || v.VertreterId == lehrer)
                                         && (klassenSet.Count == 0 || v.KlassenIds.ToHashSet().SetEquals(klassenSet))
                                         && (titel.Length == 0 || string.IsNullOrWhiteSpace(v.Text) || TitelPasst(titel, v.Text) || TitelPasst(m.Titel, v.Text)))
            .ToList();
    }

    /// <summary>
    /// Automatischer Hinweis „(Benutzer) Raumänderung“, den ältere Versionen beim Übernehmen einer Raumänderung aus UntPlanWeb als
    /// Vertretungstext eingetragen haben. Eine Raumänderung aus einer Buchung hat keinen Vertretungstext: der Hinweis wird beim
    /// Übernehmen nicht mehr gesetzt und beim Export nicht übertragen.
    /// </summary>
    public static bool IstRaumaenderungsHinweis(string? text) =>
        !string.IsNullOrWhiteSpace(text) && System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^\([^()]*\)\s*Raumänderung$");

    /// <summary>Vertretungstext für den Export: ohne den automatischen Raumänderungs-Hinweis.</summary>
    public static string ExportText(string? text) => IstRaumaenderungsHinweis(text) ? "" : text ?? "";

    /// <summary>
    /// Doppelt bzw. mehrfach vorhandene Prüfungen (gleicher Tag, gleiche Stunden, gleiche Klassen, gleicher Name – z. B. durch
    /// mehrfachen Import aus älteren Versionen) auf eine zurückführen; die Vertretungen der betroffenen Tage werden neu abgeglichen.
    /// </summary>
    public static int KlausurDublettenEntfernen(Schuldatei d)
    {
        // dieselbe Rückmeldung mehrfach übernommen (gleiche WebId) bzw. gleiche Prüfung (Tag, Stunden, Klassen, Name) mehrfach angelegt
        var weg = d.Klausuren.Where(k => k.Importiert).GroupBy(k => k.WebId)
            .SelectMany(g => g.OrderBy(k => k.Id).Skip(1)).ToList();
        weg.AddRange(d.Klausuren.Except(weg)
            .GroupBy(k => (k.Datum, k.StundeVon, k.StundeBis, Klassen: string.Join(",", k.KlassenIds.OrderBy(x => x)), Name: k.Name.Trim().ToLowerInvariant()))
            .SelectMany(g => g.OrderByDescending(k => k.Importiert).ThenBy(k => k.Id).Skip(1)));
        foreach (var k in weg)
        {
            // Dublette: die verbleibende Prüfung deckt deren Buchungen ab
            d.Klausuren.Remove(k);
            d.Vertretungen.RemoveAll(v => v.KlausurId == k.Id);
        }
        foreach (var t in weg.Select(k => k.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        return weg.Count;
    }

    /// <summary>
    /// Eintrag im Protokoll, der die Rückmeldung bereits erledigt (übernommen bzw. ignoriert) – oder null. Eine übernommene Prüfung,
    /// die UntPlanWeb mit derselben Kennung, aber geändertem Inhalt (Termin, Stunden, Klassen, Räume, Aufsicht, Titel) erneut meldet,
    /// gilt nicht als erledigt: sie wird als Aktualisierung übernommen.
    /// </summary>
    public static StplAenderungsEintrag? Erledigt(IEnumerable<StplAenderungsEintrag> protokoll, WebRueckmeldung m)
    {
        var e = protokoll.LastOrDefault(x => x.Id == m.Id);
        if (e is null) return null;
        if (e.Uebernommen && m.Typ == WebRueckmeldungsTypen.Klausur && e.Meldung.Typ == WebRueckmeldungsTypen.Klausur && Inhalt(e.Meldung) != Inhalt(m)) return null;
        return e;
    }

    private static string Inhalt(WebRueckmeldung m)
    {
        static string L(IEnumerable<string?> x) => string.Join(",", x.Where(y => !string.IsNullOrWhiteSpace(y)).Select(y => y!.Trim().ToLowerInvariant()).Distinct().OrderBy(y => y));
        return $"{m.Von}|{m.StundeVon}|{m.StundeBis ?? m.StundeVon}|{L(m.Klassen)}|{L(m.Raeume.Append(m.Raum))}|{L(m.WeitereLehrer.Append(m.Lehrer))}|{(m.Titel ?? "").Trim()}|{(m.Text ?? "").Trim()}|{m.Fach}";
    }

    /// <summary>
    /// Prüfung aus einer Rückmeldung übernehmen – ohne Dubletten: Gibt es die Prüfung schon (gleiche Kennung aus UntPlanWeb oder gleiche
    /// Klassen, gleicher Tag, gleiche Stunden und passender Titel), wird sie nur aktualisiert (bei geändertem Termin samt ihrer
    /// Vertretungszeilen verlegt). Bestehende Raumbuchungen für die Prüfung werden zur Prüfung (<see cref="BuchungenInKlausurUmwandeln"/>).
    /// Betroffene Tage kommen nach <paramref name="tage"/>.
    /// </summary>
    public static Klausur KlausurUebernehmen(Schuldatei d, WebRueckmeldung m, DateOnly datum, int von, int bis, List<int> klassen, List<int> lehrer, List<int> raeume,
        ISet<DateOnly> tage, string benutzer = "", bool pausenaufsichten = false)
    {
        var name = string.IsNullOrWhiteSpace(m.Titel) ? "Klausur" : m.Titel!.Trim();
        var k = d.Klausuren.FirstOrDefault(x => x.WebId == m.Id)
                ?? d.Klausuren.FirstOrDefault(x => x.Datum == datum && x.StundeVon == von && Math.Max(x.StundeVon, x.StundeBis) == bis
                                                   && x.KlassenIds.ToHashSet().SetEquals(klassen) && TitelPasst(name, x.Name));
        if (k is null)
        {
            k = new Klausur { Id = d.NeueId(), WebId = m.Id, UnterrichtEntfaellt = true };
            d.Klausuren.Add(k);
        }
        else if (k.Datum != datum || k.StundeVon != von || Math.Max(k.StundeVon, k.StundeBis) != bis)
        {
            // verlegt: Zeilen des alten Termins (auch Pausenaufsichten vor/nach der Prüfung) restlos entfernen
            tage.Add(k.Datum);
            KlausurZeilenEntfernen(d, k);
        }
        if (string.IsNullOrEmpty(k.WebId)) k.WebId = m.Id;
        k.Name = name;
        k.Text = m.Text ?? "";
        k.Datum = datum;
        k.StundeVon = von;
        k.StundeBis = bis;
        k.KlassenIds = klassen.ToList();
        k.LehrerIds = lehrer.ToList();
        if (raeume.Count > 0 || k.RaumIds.Count == 0) k.RaumIds = raeume.ToList();
        k.Aktiv = true;
        UnterrichtZuordnen(d, k);
        BuchungenInKlausurUmwandeln(d, k);
        tage.Add(datum);
        if (pausenaufsichten) PausenaufsichtenPruefen(d, k, benutzer);
        return k;
    }

    /// <summary>
    /// Unterricht, den eine aus UntPlanWeb übernommene Prüfung ersetzt. Die Lehrkraft trägt die Prüfung in ihrer Stunde für die Klasse(n)
    /// der Stunde ein. Haben diese Klassen zur selben Zeit parallel weiteren Unterricht anderer Lehrkräfte (z. B. Kurse der Oberstufe,
    /// Gruppen), gilt die Prüfung nur für den eigenen Unterricht der Aufsicht: <see cref="Klausur.UnterrichtId"/> = dieser Unterricht,
    /// der übrige Unterricht der Klassen entfällt nicht (sonst entfiele der Parallelkurs, und UntPlanWeb zeigte dort eine zweite Prüfung).
    /// Ohne parallelen Unterricht entfällt wie bisher der Unterricht der Klassen. Ergebnis: geändert.
    /// </summary>
    public static bool UnterrichtZuordnen(Schuldatei d, Klausur k)
    {
        var bis = Math.Max(k.StundeVon, k.StundeBis);
        var regel = Tagesplan.Regulaer(d, k.Datum).Where(r => r.Stunde >= k.StundeVon && r.Stunde <= bis && r.KlassenIds.Intersect(k.KlassenIds).Any()).ToList();
        var eigene = regel.Where(r => r.LehrerId is int l && k.LehrerIds.Contains(l)).ToList();
        // nur wenn der eigene Unterricht alle Klassen der Prüfung umfasst (Prüfung über mehrere Klassen mit verschiedenen Lehrkräften:
        // der Unterricht aller Klassen entfällt wie bisher)
        var abgedeckt = k.KlassenIds.All(kl => eigene.Any(e => e.KlassenIds.Contains(kl)));
        var parallel = regel.Any(r => eigene.All(e => e.Unterricht.Id != r.Unterricht.Id));
        int? uid = null;
        var entfaellt = true;
        if (eigene.Count > 0 && abgedeckt && parallel)
        {
            uid = eigene.GroupBy(r => r.Unterricht.Id).OrderByDescending(g => g.Count()).First().Key;
            entfaellt = false;
        }
        if (k.UnterrichtId == uid && k.UnterrichtEntfaellt == entfaellt) return false;
        k.UnterrichtId = uid;
        k.UnterrichtEntfaellt = entfaellt;
        return true;
    }

    /// <summary>
    /// Aus UntPlanWeb übernommene Prüfungen älterer Importe (Unterricht der ganzen Klasse entfällt, kein Unterricht zugeordnet) nach
    /// <see cref="UnterrichtZuordnen"/> korrigieren; die Vertretungen der betroffenen Tage werden neu abgeglichen. Ergebnis: Anzahl.
    /// </summary>
    public static int ImportierteKlausurenZuordnen(Schuldatei d)
    {
        var tage = new HashSet<DateOnly>();
        foreach (var k in d.Klausuren.Where(k => k.Importiert && k.Aktiv && k.UnterrichtEntfaellt && k.UnterrichtId is null))
            if (UnterrichtZuordnen(d, k)) tage.Add(k.Datum);
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return tage.Count;
    }

    /// <summary>Aus UntPlanWeb übernommene Buchung: Raumbuchung, Sondereinsatz (Aktivität) bzw. Raumänderung eines Unterrichts.</summary>
    private static bool IstBuchung(Vertretung v) =>
        v.KlausurId is null && (v.Art == VertretungsArt.Raumbuchung || (!string.IsNullOrEmpty(v.WebId) && (v.Art == VertretungsArt.Raumvertretung
            || (v.Art == VertretungsArt.Sondereinsatz && v.UnterrichtId is null))));

    private static bool BuchungPasst(Klausur k, int? lehrer, IEnumerable<int> raeume, IEnumerable<int> klassen)
    {
        var lehrerOk = lehrer is not int l || k.LehrerIds.Count == 0 || k.LehrerIds.Contains(l);
        var raumOk = raeume.Any(k.RaumIds.Contains);
        var klasseOk = klassen.Any(k.KlassenIds.Contains);
        return lehrerOk && (raumOk || (klasseOk && lehrer.HasValue) || (k.RaumIds.Count == 0 && lehrer.HasValue && k.LehrerIds.Contains(lehrer.Value)));
    }

    /// <summary>
    /// Bestehende Raumbuchungen, die zur Prüfung gehören (gleicher Tag, Stunden der Prüfung, Lehrkraft der Aufsicht und gleicher Raum bzw.
    /// gleiche Klasse), werden zur Prüfung: die Buchungszeilen entfallen, ihr Raum geht auf die Prüfung über, falls diese noch keinen hat.
    /// Ergebnis: Anzahl umgewandelter Zeilen.
    /// </summary>
    public static int BuchungenInKlausurUmwandeln(Schuldatei d, Klausur k)
    {
        var bis = Math.Max(k.StundeVon, k.StundeBis);
        var weg = d.Vertretungen.Where(v => v.Datum == k.Datum && v.Stunde >= k.StundeVon && v.Stunde <= bis && IstBuchung(v)
                                            && BuchungPasst(k, v.VertreterId, Raeume(v), v.KlassenIds)).ToList();
        if (weg.Count == 0) return 0;
        if (k.RaumIds.Count == 0) k.RaumIds = weg.SelectMany(Raeume).Distinct().ToList();
        foreach (var v in weg)
        {
            d.Vertretungen.Remove(v);
            k.Buchungen.Add(v);
        }
        return weg.Count;
    }

    /// <summary>
    /// Prüfung entfernen bzw. vom Termin lösen: ihre Vertretungszeilen (Prüfung, Entfall, Aufsicht, Pausenaufsicht) restlos entfernen und
    /// die zur Prüfung gewordenen Buchungen (z. B. eine schon vorher bestehende Raumänderung) wiederherstellen.
    /// </summary>
    public static void KlausurZeilenEntfernen(Schuldatei d, Klausur k)
    {
        d.Vertretungen.RemoveAll(v => v.KlausurId == k.Id);
        foreach (var b in k.Buchungen)
        {
            b.Id = d.NeueId();
            d.Vertretungen.Add(b);
        }
        k.Buchungen.Clear();
    }

    private static IEnumerable<int> Raeume(Vertretung v) => new[] { v.VertretungsraumId }.OfType<int>().Concat(v.ZusatzraumIds);

    /// <summary>
    /// Prüfung, für die eine (neue) Raumbuchung gemeldet wird: gleicher Tag, Stunden innerhalb der Prüfung, Lehrkraft der Aufsicht und
    /// gleicher Raum bzw. gleiche Klasse. Die Buchung ist dann schon durch die Prüfung abgedeckt und wird nicht zusätzlich angelegt; hat die
    /// Prüfung noch keinen Raum, übernimmt sie den gebuchten.
    /// </summary>
    public static Klausur? KlausurFuerBuchung(Schuldatei d, DateOnly datum, int von, int bis, IReadOnlyCollection<int> lehrer, IReadOnlyCollection<int> raeume, IReadOnlyCollection<int> klassen)
    {
        var k = d.Klausuren.FirstOrDefault(k => k.Aktiv && k.Datum == datum && k.StundeVon <= von && Math.Max(k.StundeVon, k.StundeBis) >= bis
                                                && (lehrer.Count == 0 ? BuchungPasst(k, null, raeume, klassen) : lehrer.Any(l => BuchungPasst(k, l, raeume, klassen))));
        if (k != null && k.RaumIds.Count == 0 && raeume.Count > 0) k.RaumIds = raeume.ToList();
        return k;
    }

    /// <summary>Kennzeichen (Feld Grund) einer Löschmeldung aus UntPlanWeb, die den Termin genau beschreibt (Stunden, Räume, Titel).</summary>
    public const string Exakt = "exakt";

    /// <summary>Titel der Meldung passt zur Prüfung (Kartentext „Name Text“ bzw. „Name · Text“; „Klausur“/„Prüfung“ passt immer).</summary>
    internal static bool TitelPasst(string? titel, string name)
    {
        var a = (titel ?? "").Trim();
        var b = (name ?? "").Trim();
        return a.Length == 0 || b.Length == 0 || a is "Klausur" or "Prüfung" || a.Equals(b, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prüfungen eines Tages entfernen, die zu Stunden, Klassen und (falls angegeben) Titel der Meldung passen – bei gleichen Stunden
    /// (und bei einer genau beschriebenen Meldung gleichen Räumen) nur diese. Prüfungen aus <paramref name="geschuetzt"/> (WebIds der
    /// im selben Import neu angelegten Prüfungen, z. B. der neue Termin einer verlegten Prüfung) bleiben. Auch die zugehörigen
    /// Vertretungszeilen (Entfall, Aufsicht, Pausenaufsicht vor/nach der Prüfung) werden entfernt. Ergebnis: Anzahl.
    /// </summary>
    public static int KlausurenEntfernen(Schuldatei d, WebRueckmeldung m, DateOnly datum, int von, int bis, IReadOnlySet<string>? geschuetzt = null)
    {
        // Bezug auf eine bestimmte Prüfung (Web-Kennung): nur diese
        if (!string.IsNullOrWhiteSpace(m.BezugId) && d.Klausuren.Where(k => k.WebId == m.BezugId.Trim()).ToList() is { Count: > 0 } bezogen)
        {
            foreach (var k in bezogen)
            {
                d.Klausuren.Remove(k);
                KlausurZeilenEntfernen(d, k);
            }
            return bezogen.Count;
        }
        var klassen = m.Klassen.Select(k => Id(d, ElementTyp.Klasse, k)).OfType<int>().ToHashSet();
        // genannte, aber unbekannte Klassen: nicht auf „alle Klassen“ ausweiten (sonst träfe die Meldung fremde Prüfungen)
        if (m.Klassen.Any(k => !string.IsNullOrWhiteSpace(k)) && klassen.Count == 0) return 0;
        var raeume = m.Raeume.Append(m.Raum ?? "").Select(r => Id(d, ElementTyp.Raum, r)).OfType<int>().ToHashSet();
        var kandidaten = d.Klausuren.Where(k => k.Datum == datum && k.StundeVon <= bis && Math.Max(k.StundeVon, k.StundeBis) >= von
                                                && (klassen.Count == 0 || k.KlassenIds.Any(klassen.Contains)) && TitelPasst(m.Titel, k.Name)
                                                && (geschuetzt is null || k.WebId is null || !geschuetzt.Contains(k.WebId))).ToList();
        var gleich = kandidaten.Where(k => k.StundeVon == von && Math.Max(k.StundeVon, k.StundeBis) == bis).ToList();
        var genau = m.Grund == Exakt ? gleich.Where(k => k.RaumIds.ToHashSet().SetEquals(raeume)).ToList() : new List<Klausur>();
        var weg = genau.Count > 0 ? genau : gleich.Count > 0 ? gleich : kandidaten;
        foreach (var k in weg)
        {
            d.Klausuren.Remove(k);
            KlausurZeilenEntfernen(d, k);
        }
        return weg.Count;
    }

    private static int? Id(Schuldatei d, ElementTyp t, string? k) =>
        string.IsNullOrWhiteSpace(k) ? null : d.AlleElemente(t).FirstOrDefault(e => e.Kurzname.Equals(k.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>
    /// Rückmeldungen vom Server und bereits bearbeitete Änderungen der Datei zu Zeilen aufbereiten (nach Datum und Stunde sortiert).
    /// </summary>
    public static List<StplAenderung> Analysieren(Schuldatei d, IEnumerable<WebRueckmeldung> rueckmeldungen, DateOnly von, DateOnly bis, bool erledigteAnzeigen)
    {
        var protokoll = d.Einstellungen.Portal.StplAenderungen.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last());
        var liste = rueckmeldungen.Where(m => Typen.Contains(m.Typ)).GroupBy(m => m.Id).Select(g => g.First()).ToList();
        if (erledigteAnzeigen) liste.AddRange(protokoll.Values.Where(p => liste.All(m => m.Id != p.Id)).Select(p => p.Meldung));
        var res = new List<StplAenderung>();
        foreach (var m in liste)
        {
            protokoll.TryGetValue(m.Id, out var p);
            // übernommene Prüfung mit geändertem Inhalt: als Aktualisierung erneut anbieten
            if (p != null && Erledigt(new[] { p }, m) is null) p = null;
            if (p != null && !erledigteAnzeigen) continue;
            var z = Zeile(d, m, p?.Uebernommen);
            if (z.Datum < von || z.Datum > bis) continue;
            res.Add(z);
        }
        return res.OrderBy(z => z.Datum).ThenBy(z => z.StundeVon).ThenBy(z => z.Id).ToList();
    }

    /// <summary>Eine Rückmeldung einordnen (Unterricht / Aktivität / Prüfung).</summary>
    public static StplAenderung Zeile(Schuldatei d, WebRueckmeldung m, bool? uebernommen = null)
    {
        var ix = new DatenIndex(d);
        string? fehler = null;
        if (!DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var datum)) { fehler = "ungültiges Datum"; datum = DateOnly.MinValue; }
        else if (!Kalender.IstUnterrichtstag(d, datum)) fehler = "kein Unterrichtstag";
        var spt = Math.Max(1, d.Zeitraster.StundenProTag);
        var von = Math.Clamp(m.StundeVon ?? 1, 1, spt);
        var bis = Math.Clamp(m.StundeBis ?? von, von, spt);
        var z1 = d.Zeitraster.Zeit(von);
        var z2 = d.Zeitraster.Zeit(bis);
        var zeit = z1 != null && z2 != null ? $"{z1.Beginn:HH\\:mm} - {z2.Ende:HH\\:mm}" : "";
        var raeume = m.Raeume.Append(m.Raum ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var lehrerNamen = new[] { m.Lehrer ?? "" }.Concat(m.WeitereLehrer).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var storno = m.Typ == WebRueckmeldungsTypen.BuchungStorno;
        // Storno: unbekannte Elemente bzw. ein inzwischen freier Tag hindern das Entfernen nicht
        if (storno) fehler = fehler == "kein Unterrichtstag" || BezugId(m) != null ? null : fehler;
        else
        {
            foreach (var k in m.Klassen) if (Id(d, ElementTyp.Klasse, k) is null) fehler ??= $"Klasse {k} unbekannt";
            foreach (var r in raeume) if (Id(d, ElementTyp.Raum, r) is null) fehler ??= $"Raum {r} unbekannt";
            foreach (var l in lehrerNamen) if (Id(d, ElementTyp.Lehrer, l) is null) fehler ??= $"Lehrer {l} unbekannt";
        }
        var art = m.Typ is WebRueckmeldungsTypen.Klausur or WebRueckmeldungsTypen.KlausurLoeschen ? StplAenderungsArt.Pruefung : StplAenderungsArt.Aktivitaet;
        int? uid = null, zeile = null;
        var fach = m.Fach;
        var klassen = m.Klassen.ToList();
        if (art == StplAenderungsArt.Aktivitaet && fehler is null && !storno)
        {
            // Bezieht sich die Buchung auf einen bestehenden Unterricht der Lehrkraft? Dann ist es eine Raumänderung.
            var lehrer = Id(d, ElementTyp.Lehrer, m.Lehrer);
            var tp = Tagesplan.Berechnen(d, datum);
            var e = tp.Stunde(von).FirstOrDefault(x => x.Findetstatt && x.Unterricht != null && (m.UnterrichtNr is int nr ? x.Unterricht.Id == nr : lehrer is int l && x.LehrerIds.Contains(l)) &&
                                                       (m.Klassen.Count == 0 || m.Klassen.All(k => x.KlassenIds.Contains(Id(d, ElementTyp.Klasse, k) ?? -1))));
            if (e != null && (string.IsNullOrWhiteSpace(m.Fach) || Id(d, ElementTyp.Fach, m.Fach) == e.FachId))
            {
                art = StplAenderungsArt.Unterricht;
                uid = e.Unterricht!.Id;
                zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex;
                fach ??= ix.KFach(e.FachId);
                if (klassen.Count == 0) klassen = e.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)).ToList();
            }
        }
        var titel = string.Join(" – ", new[] { m.Titel, m.Text }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var text = storno ? $"Buchung storniert: {titel}".TrimEnd(' ', ':')
            : art == StplAenderungsArt.Unterricht ? $"({m.Absender}) Raumänderung" : m.Typ == WebRueckmeldungsTypen.KlausurLoeschen ? $"Prüfung löschen: {titel}" : titel;
        var klf = string.Join(", ", klassen) + (string.IsNullOrWhiteSpace(fach) ? "" : " / " + fach);
        return new StplAenderung(m, datum, von, bis, zeit, art, uid, zeile, klf, string.Join(", ", lehrerNamen), string.Join(", ", raeume), "", "", text, m.Absender, uebernommen, fehler);
    }

    /// <summary>Änderungen ignorieren (werden vermerkt und nicht erneut angeboten).</summary>
    public static void Ignorieren(Schuldatei d, IEnumerable<StplAenderung> liste, string benutzer = "")
    {
        foreach (var a in liste.Where(a => !a.Erledigt))
            d.Einstellungen.Portal.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = false, Benutzer = benutzer, Meldung = a.Meldung });
    }

    /// <summary>
    /// Änderungen übernehmen. Bereits übernommene oder ignorierte werden übersprungen. Liefert die übernommenen und die
    /// abgelehnten Kennungen (mit Grund).
    /// </summary>
    public static (List<string> Uebernommen, List<(string Id, string Grund)> Abgelehnt) Uebernehmen(Schuldatei d, IEnumerable<StplAenderung> liste, string benutzer = "")
    {
        var ok = new List<string>();
        var nein = new List<(string, string)>();
        var p = d.Einstellungen.Portal;
        var tage = new HashSet<DateOnly>();
        // Löschungen zuerst (sie meinen stets schon übertragene Prüfungen); im selben Import neu angelegte Prüfungen – etwa der neue
        // Termin einer verlegten Prüfung – sind vor ihnen geschützt
        var alle = liste.ToList();
        var neueIds = alle.Where(a => a.Meldung.Typ == WebRueckmeldungsTypen.Klausur).Select(a => a.Id).ToHashSet();
        foreach (var alt in alle.OrderBy(a => IstLoeschung(a.Meldung) ? 0 : 1))
        {
            if (Erledigt(p.StplAenderungen, alt.Meldung) != null) continue;
            var a = Zeile(d, alt.Meldung);
            if (a.Fehler != null) { nein.Add((a.Id, a.Fehler)); continue; }
            var m = a.Meldung;
            if (m.Typ == WebRueckmeldungsTypen.BuchungStorno)
            {
                // stornierte Buchung entfernen – auch wenn sie nicht (mehr) vorhanden ist: erledigt und quittiert (idempotent)
                BuchungStornieren(d, m, tage);
                p.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = true, Benutzer = benutzer, Meldung = m });
                ok.Add(a.Id);
                continue;
            }
            var raeume = m.Raeume.Append(m.Raum ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Id(d, ElementTyp.Raum, x)!.Value).Distinct().ToList();
            var lehrer = new[] { m.Lehrer ?? "" }.Concat(m.WeitereLehrer).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Id(d, ElementTyp.Lehrer, x)!.Value).Distinct().ToList();
            var klassen = m.Klassen.Select(k => Id(d, ElementTyp.Klasse, k)!.Value).ToList();
            var fach = Id(d, ElementTyp.Fach, m.Fach);
            var stunden = Enumerable.Range(a.StundeVon, a.StundeBis - a.StundeVon + 1).ToList();
            // Buchung für eine schon eingetragene Prüfung (gleiche Zeit, Aufsicht, Raum bzw. Klasse): die Prüfung deckt sie ab – nicht
            // zusätzlich anlegen (auch keine Raumänderung des Unterrichts, den die Prüfung ersetzt)
            if (a.Art != StplAenderungsArt.Pruefung)
            {
                var buchKlassen = klassen.ToList();
                if (a.UnterrichtId is int bu)
                    buchKlassen.AddRange(Tagesplan.Regulaer(d, a.Datum).Where(r => r.Unterricht.Id == bu).SelectMany(r => r.KlassenIds));
                if (KlausurFuerBuchung(d, a.Datum, a.StundeVon, a.StundeBis, lehrer, raeume, buchKlassen) != null)
                {
                    tage.Add(a.Datum);
                    p.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = true, Benutzer = benutzer, Meldung = m });
                    ok.Add(a.Id);
                    continue;
                }
            }
            switch (a.Art)
            {
                case StplAenderungsArt.Unterricht:
                {
                    if (raeume.Count == 0) { nein.Add((a.Id, "kein Raum angegeben")); continue; }
                    // Die Rückmeldung enthält die vollständige neue Raumliste: der ursprüngliche Raum bleibt Hauptraum, solange er dabei ist;
                    // die übrigen sind genau die Zusatzräume (gelöschte Zusatzräume verschwinden, der ursprüngliche Raum bleibt erhalten)
                    var regel = Tagesplan.Berechnen(d, a.Datum).Stunde(a.StundeVon).FirstOrDefault(x => x.Unterricht?.Id == a.UnterrichtId);
                    var ursprung = regel?.Regel?.RaumId;
                    var haupt = ursprung is int ur && raeume.Contains(ur) ? ur : raeume[0];
                    var zusatz = raeume.Where(r => r != haupt).ToList();
                    var neu = Raumvertretungen.Zuordnen(d, a.UnterrichtId!.Value, a.ZeilenIndex ?? 0, a.Datum, stunden, haupt, false, RaumKollisionsLoesung.MitKollision, benutzer);
                    foreach (var v in neu.Where(v => v.UnterrichtId == a.UnterrichtId))
                    {
                        // reine Raumänderung aus einer Buchung: kein Vertretungstext (ein vorhandener eigener Text der Stunde bleibt)
                        if (IstRaumaenderungsHinweis(v.Text)) v.Text = "";
                        v.WebId = a.Id;
                        v.ZusatzraumIds = zusatz.ToList();
                    }
                    // wieder genau der ursprüngliche Raum: bestehende Zusatzräume dieser Stunden entfernen
                    if (zusatz.Count == 0)
                        foreach (var v in d.Vertretungen.Where(v => v.Datum == a.Datum && v.UnterrichtId == a.UnterrichtId && stunden.Contains(v.Stunde)))
                            v.ZusatzraumIds.Clear();
                    break;
                }
                case StplAenderungsArt.Aktivitaet:
                    foreach (var s in stunden)
                        foreach (var l in lehrer.Cast<int?>().DefaultIfEmpty(null))
                        {
                            var tp = Tagesplan.Berechnen(d, a.Datum);
                            d.Vertretungen.Add(new Vertretung
                            {
                                Id = d.NeueId(), Datum = a.Datum, Stunde = s, Art = VertretungsArt.Sondereinsatz, Quelle = VertretungsQuelle.Sondereinsatz, VertreterId = l,
                                FachId = fach, VertretungsfachId = fach, KlassenIds = klassen, VertretungsraumId = raeume.Cast<int?>().FirstOrDefault(), ZusatzraumIds = raeume.Skip(1).ToList(),
                                Text = a.Text, Bearbeitet = true, Benutzer = string.IsNullOrEmpty(benutzer) ? "UntPlanWeb " + m.Absender : benutzer, WebId = a.Id,
                                Mehrarbeit = l is int ll && !Vertretungen.HatBereitschaft(d, tp, ll, s),
                            });
                        }
                    break;
                default:
                {
                    if (m.Typ == WebRueckmeldungsTypen.KlausurLoeschen)
                    {
                        if (KlausurenEntfernen(d, m, a.Datum, a.StundeVon, a.StundeBis, neueIds) == 0) { nein.Add((a.Id, "Prüfung nicht gefunden")); continue; }
                        tage.Add(a.Datum);
                        break;
                    }
                    // dieselbe Prüfung schon vorhanden (z. B. über den automatischen Abgleich übernommen): nur aktualisieren
                    KlausurUebernehmen(d, m, a.Datum, a.StundeVon, a.StundeBis, klassen, lehrer, raeume, tage, benutzer, pausenaufsichten: true);
                    break;
                }
            }
            p.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = true, Benutzer = benutzer, Meldung = m });
            ok.Add(a.Id);
        }
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return (ok, nein);
    }

    /// <summary>
    /// Pausenaufsichten der Prüfungsaufsicht direkt vor bzw. nach der Prüfung: wenn nicht „außer Betracht“, entsteht eine
    /// offene Pausenaufsichtsvertretung.
    /// </summary>
    private static void PausenaufsichtenPruefen(Schuldatei d, Klausur k, string benutzer)
    {
        var p = d.Einstellungen.Portal;
        var tag = Kalender.TagIndex(d, k.Datum);
        if (tag < 0) return;
        var aufsichten = d.PeriodeAm(k.Datum).Pausenaufsichten ?? new();
        foreach (var a in aufsichten.Where(a => a.Tag == tag && (a.Datum is null || a.Datum == k.Datum) && k.LehrerIds.Contains(a.LehrerId)))
        {
            var vor = a.NachStunde == k.StundeVon - 1;
            var nach = a.NachStunde == k.StundeBis;
            if (!(vor && !p.PausenaufsichtVorPruefungIgnorieren) && !(nach && !p.PausenaufsichtNachPruefungIgnorieren)) continue;
            if (d.Vertretungen.Any(v => v.Datum == k.Datum && v.Art == VertretungsArt.Pausenaufsicht && v.GangId == a.GangId && v.Stunde == a.NachStunde && v.LehrerId == a.LehrerId)) continue;
            d.Vertretungen.Add(new Vertretung
            {
                Id = d.NeueId(), Datum = k.Datum, Stunde = a.NachStunde, Art = VertretungsArt.Pausenaufsicht, Quelle = VertretungsQuelle.Manuell, GangId = a.GangId,
                LehrerId = a.LehrerId, KlausurId = k.Id, Text = vor ? "vor Prüfung" : "nach Prüfung", Bearbeitet = true, Benutzer = benutzer, WebId = k.WebId,
            });
        }
    }
}
