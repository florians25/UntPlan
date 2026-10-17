using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Überlappung einer Lehrer-Absenz mit einer Veranstaltung, an der die Lehrkraft teilnimmt.</summary>
/// <param name="Tage">Tage, an denen sich Absenz und Veranstaltung überschneiden (und die Lehrkraft noch eingetragen ist).</param>
public sealed record VeranstaltungsUeberlappung(Veranstaltung Veranstaltung, int LehrerId, IReadOnlyList<DateOnly> Tage);

/// <summary>
/// Logik der Veranstaltungen über die reine Abwesenheit hinaus: Kl-Teil und Schülergruppe, Lehrkraft fällt für eine
/// Veranstaltung aus (aus der Veranstaltung entfernen, offener Platz „?“, Ersatzlehrkraft), Kopieren auf ein anderes Datum.
/// </summary>
public static class VeranstaltungsLogik
{
    /// <summary>Schülergruppe einer Kopplungszeile (Zeile vor Unterricht).</summary>
    public static string Schuelergruppe(Unterricht u, int zeilenIndex) =>
        zeilenIndex >= 0 && zeilenIndex < u.Zeilen.Count && u.Zeilen[zeilenIndex].Schuelergruppe is { Length: > 0 } z ? z : u.Schuelergruppe;

    public static string Schuelergruppe(RegelStunde r) => Schuelergruppe(r.Unterricht, r.ZeilenIndex);

    /// <summary>
    /// Findet der Unterricht einer abwesenden (an der Veranstaltung teilnehmenden) Lehrkraft als Veranstaltung statt?
    /// Das ist der Fall, wenn eine der Klassen (Spalte Klasse oder Kl-Teil) beteiligt ist und – falls die Veranstaltung eine
    /// Schülergruppe hat – der Unterricht genau diese Schülergruppe betrifft. Sonst wird der Unterricht zur offenen Vertretung.
    /// </summary>
    public static bool UnterrichtInVeranstaltung(Veranstaltung va, RegelStunde r)
    {
        if (!r.KlassenIds.Intersect(va.AlleKlassenIds).Any()) return false;
        if (string.IsNullOrWhiteSpace(va.Schuelergruppe)) return true;
        return string.Equals(Schuelergruppe(r).Trim(), va.Schuelergruppe.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Alle bekannten Schülergruppen: aus den Unterrichten (Zeile und Unterricht, alle Perioden) und den Veranstaltungen.</summary>
    public static List<string> Schuelergruppen(Schuldatei d)
    {
        var unterrichte = d.Unterrichte.Concat(d.Perioden.SelectMany(p => p.Unterrichte ?? new()));
        return unterrichte.SelectMany(u => u.Zeilen.Select(z => z.Schuelergruppe).Append(u.Schuelergruppe))
            .Concat(d.Veranstaltungen.Select(v => v.Schuelergruppe))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Create(Format.De, true)).ToList();
    }

    /// <summary>
    /// Entfernt Ersatzeinträge, deren Grundlage weggefallen ist: Tage außerhalb der Veranstaltung sowie (an diesem Datum) Einträge
    /// aus einer Absenz, die die Lehrkraft während der Veranstaltung nicht mehr abwesend setzt – die Lehrkraft nimmt dann wieder teil.
    /// </summary>
    public static void Bereinigen(Schuldatei d, Veranstaltung va, DateOnly datum)
    {
        va.Ersatz.RemoveAll(e => e.Datum < va.Von || e.Datum > va.Bis || !va.LehrerIds.Contains(e.LehrerId));
        va.Zusatz.RemoveAll(z => z.Datum < va.Von || z.Datum > va.Bis || va.LehrerIds.Contains(z.LehrerId));
        va.Ersatz.RemoveAll(e => e.Datum == datum && e.AbwesenheitId is int aid &&
            !(d.Abwesenheiten.FirstOrDefault(a => a.Id == aid) is { } abs && abs.Art == AbwesenheitsArt.Lehrer && abs.ElementId == e.LehrerId &&
              Stunden(d, va, datum).Any(s => abs.Deckt(datum, s))));
        // doppelte Einträge (gleicher Tag, gleiche Lehrkraft) zusammenfassen
        foreach (var g in va.Ersatz.GroupBy(e => (e.Datum, e.LehrerId)).Where(g => g.Count() > 1).ToList())
            foreach (var weg in g.OrderByDescending(e => e.ErsatzId.HasValue).Skip(1).ToList()) va.Ersatz.Remove(weg);
    }

    /// <summary>Stunden der Veranstaltung an diesem Tag.</summary>
    public static IEnumerable<int> Stunden(Schuldatei d, Veranstaltung va, DateOnly datum) =>
        Enumerable.Range(1, Math.Max(1, d.Zeitraster.StundenProTag)).Where(s => va.Deckt(datum, s));

    /// <summary>Veranstaltungen, mit denen eine (neue oder geänderte) Lehrer-Absenz überlappt und an denen die Lehrkraft noch teilnimmt.</summary>
    public static List<VeranstaltungsUeberlappung> Ueberlappungen(Schuldatei d, Abwesenheit a)
    {
        var res = new List<VeranstaltungsUeberlappung>();
        if (a.Art != AbwesenheitsArt.Lehrer || a.ElementId == 0) return res;
        var l = a.ElementId;
        foreach (var va in d.Veranstaltungen.Where(v => v.LehrerIds.Contains(l) && v.Bis >= a.Von && v.Von <= a.Bis).OrderBy(v => v.Von).ThenBy(v => v.Id))
        {
            var tage = Kalender.Tage(a.Von > va.Von ? a.Von : va.Von, a.Bis < va.Bis ? a.Bis : va.Bis)
                .Where(t => va.NimmtTeil(l, t) && Stunden(d, va, t).Any(s => a.Deckt(t, s))).ToList();
            if (tage.Count > 0) res.Add(new VeranstaltungsUeberlappung(va, l, tage));
        }
        return res;
    }

    private static string Wt(DateOnly t) => Planung.Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7];

    private static string Zeitraum(DateOnly von, DateOnly bis) => von == bis ? $"{von:dd.MM.} / {Wt(von)}" : $"{von:dd.MM.} - {bis:dd.MM.}";

    /// <summary>Text der Nachfrage beim Anlegen einer Absenz, die mit einer Veranstaltung überlappt (wie im Vorbild).</summary>
    public static string FrageText(Schuldatei d, Abwesenheit a, VeranstaltungsUeberlappung u)
    {
        var lehrer = d.Lehrer.FirstOrDefault(x => x.Id == u.LehrerId);
        var kurz = lehrer?.Kurzname ?? "?";
        var lang = lehrer is null ? "" : string.IsNullOrWhiteSpace(lehrer.Langname) ? lehrer.Kurzname : lehrer.Langname;
        return $"{kurz} - {lang}\nAbsenz {a.Id}:  {Zeitraum(a.Von, a.Bis)}\nüberlappt mit\nVeranstaltung {u.Veranstaltung.Id}:  {Zeitraum(u.Veranstaltung.Von, u.Veranstaltung.Bis)}\n\n" +
               $"Soll Lehrer {kurz} aus der Veranstaltung entfernt werden?";
    }

    /// <summary>Bestätigung nach dem Entfernen: „Lehrer X wurde aus der Veranstaltung Nr. M am TT.MM.JJJJ entfernt.“</summary>
    public static string HinweisText(Schuldatei d, VeranstaltungsUeberlappung u)
    {
        var kurz = d.Lehrer.FirstOrDefault(x => x.Id == u.LehrerId)?.Kurzname ?? "?";
        var tage = u.Tage.Count <= 3 ? string.Join(", ", u.Tage.Select(t => t.ToString("dd.MM.yyyy")))
            : $"{u.Tage[0]:dd.MM.yyyy} bis {u.Tage[^1]:dd.MM.yyyy} ({u.Tage.Count} Tage)";
        return $"Lehrer {kurz} wurde aus der Veranstaltung Nr. {u.Veranstaltung.Id} am {tage} entfernt.";
    }

    /// <summary>Lehrkraft an den angegebenen Tagen aus der Veranstaltung entfernen; ihr Platz bleibt als „?“ offen, bis eine Ersatzlehrkraft eingetragen ist.</summary>
    public static void LehrerEntfernen(Schuldatei d, int veranstaltungId, int lehrerId, IEnumerable<DateOnly> tage, int? abwesenheitId = null)
    {
        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == veranstaltungId) ?? throw new InvalidOperationException($"Veranstaltung {veranstaltungId} nicht gefunden.");
        if (!va.LehrerIds.Contains(lehrerId)) throw new InvalidOperationException("Die Lehrkraft nimmt an der Veranstaltung nicht teil.");
        var liste = tage.Distinct().OrderBy(t => t).ToList();
        foreach (var t in liste)
        {
            if (va.Ersatz.Any(e => e.Datum == t && e.LehrerId == lehrerId)) continue;
            va.Ersatz.Add(new VeranstaltungsErsatz { Datum = t, LehrerId = lehrerId, AbwesenheitId = abwesenheitId });
        }
        foreach (var t in liste) Vertretungen.Aktualisieren(d, t);
    }

    /// <summary>
    /// Ersatzlehrkraft für einen offenen Platz („?“) eintragen bzw. (ersatzId = null) wieder offen setzen. Ohne Angabe der entfernten
    /// Lehrkraft wird der erste offene Platz des Tages verwendet. Liefert false, wenn es an diesem Tag keinen passenden Platz gibt.
    /// </summary>
    public static bool ErsatzSetzen(Schuldatei d, int veranstaltungId, DateOnly datum, int? fuerLehrerId, int? ersatzId)
    {
        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == veranstaltungId);
        if (va is null) return false;
        var e = fuerLehrerId is int f
            ? va.Ersatz.FirstOrDefault(x => x.Datum == datum && x.LehrerId == f)
            : va.Ersatz.Where(x => x.Datum == datum).OrderBy(x => x.ErsatzId.HasValue).FirstOrDefault(x => ersatzId is null ? x.ErsatzId.HasValue : x.ErsatzId is null);
        if (e is null) return false;
        if (ersatzId is int neu && va.LehrerAm(datum).Contains(neu) && e.ErsatzId != neu) throw new InvalidOperationException("Die Lehrkraft nimmt an diesem Tag bereits an der Veranstaltung teil.");
        e.ErsatzId = ersatzId;
        Vertretungen.Aktualisieren(d, datum);
        return true;
    }

    /// <summary>Entfernte Lehrkraft an einem Tag wieder in die Veranstaltung aufnehmen.</summary>
    public static void LehrerWiederAufnehmen(Schuldatei d, int veranstaltungId, int lehrerId, DateOnly datum)
    {
        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == veranstaltungId);
        if (va is null) return;
        va.Ersatz.RemoveAll(e => e.Datum == datum && e.LehrerId == lehrerId);
        Vertretungen.Aktualisieren(d, datum);
    }

    /// <summary>
    /// Zusätzliche Lehrkraft (kein Ersatz für eine entfernte): nur an diesem Tag (<paramref name="ganzeVeranstaltung"/> = false)
    /// oder für die gesamte Veranstaltung. Ihr Unterricht in diesen Stunden wird – wie bei den übrigen Teilnehmern – zur Vertretung.
    /// </summary>
    public static void LehrerHinzufuegen(Schuldatei d, int veranstaltungId, DateOnly datum, int lehrerId, bool ganzeVeranstaltung)
    {
        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == veranstaltungId) ?? throw new InvalidOperationException($"Veranstaltung {veranstaltungId} nicht gefunden.");
        if (datum < va.Von || datum > va.Bis) throw new InvalidOperationException("Die Veranstaltung findet an diesem Tag nicht statt.");
        if (ganzeVeranstaltung)
        {
            if (va.LehrerIds.Contains(lehrerId)) throw new InvalidOperationException("Die Lehrkraft nimmt bereits an der Veranstaltung teil.");
            va.LehrerIds.Add(lehrerId);
            va.Zusatz.RemoveAll(z => z.LehrerId == lehrerId);
            Vertretungen.Aktualisieren(d, va.Von, va.Bis);
            return;
        }
        if (va.NimmtTeil(lehrerId, datum)) throw new InvalidOperationException("Die Lehrkraft nimmt an diesem Tag bereits an der Veranstaltung teil.");
        // war sie an diesem Tag entfernt (Absenz), ist das keine Zusatz-, sondern ihre eigene Teilnahme – das bleibt dem Absenzfenster vorbehalten
        va.Zusatz.Add(new VeranstaltungsZusatz { Datum = datum, LehrerId = lehrerId });
        Vertretungen.Aktualisieren(d, datum);
    }

    /// <summary>Zusätzlich eingetragene Lehrkraft an diesem Tag wieder aus der Veranstaltung nehmen. Liefert false, wenn sie dort nicht zusätzlich eingetragen war.</summary>
    public static bool ZusatzEntfernen(Schuldatei d, int veranstaltungId, DateOnly datum, int lehrerId)
    {
        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == veranstaltungId);
        if (va is null || va.Zusatz.RemoveAll(z => z.Datum == datum && z.LehrerId == lehrerId) == 0) return false;
        Vertretungen.Aktualisieren(d, datum);
        return true;
    }

    /// <summary>Hat die Veranstaltung im Zeitraum offene Plätze (entfernte Lehrkraft ohne Ersatz)?</summary>
    public static bool HatOffenePlaetze(Veranstaltung va, DateOnly von, DateOnly bis) =>
        va.Ersatz.Any(e => e.ErsatzId is null && e.Datum >= von && e.Datum <= bis && e.Datum >= va.Von && e.Datum <= va.Bis && va.LehrerIds.Contains(e.LehrerId));

    /// <summary>
    /// Lehrkräfte der Veranstaltung an einem Tag als Text, offene Plätze als „?“ (bzw. <paramref name="offen"/>), z. B. „Cer,New,?“.
    /// </summary>
    public static string LehrerText(Schuldatei d, Veranstaltung va, DateOnly datum, string trenner = ",", string offen = "?")
    {
        var namen = new List<string>();
        foreach (var l in va.LehrerIds)
        {
            var e = va.Ersatz.FirstOrDefault(x => x.Datum == datum && x.LehrerId == l);
            var id = e is null ? l : e.ErsatzId;
            namen.Add(id is int x ? d.Lehrer.FirstOrDefault(y => y.Id == x)?.Kurzname ?? "?" : offen);
        }
        foreach (var z in va.Zusatz.Where(z => z.Datum == datum)) namen.Add(d.Lehrer.FirstOrDefault(y => y.Id == z.LehrerId)?.Kurzname ?? "?");
        return string.Join(trenner, namen.Distinct().Where(n => n != offen).Concat(namen.Where(n => n == offen)));
    }

    /// <summary>
    /// Wie <see cref="LehrerText"/>, aber kompakt für Veranstaltungen mit vielen Lehrkräften: „Alle Lehrkräfte“ bzw. „Alle Lehrkräfte
    /// außer A, B“ statt einer endlosen Liste; offene Plätze folgen als „?“ (bzw. <paramref name="offen"/>).
    /// </summary>
    public static string LehrerKurztext(Schuldatei d, Veranstaltung va, DateOnly datum, string trenner = ",", string offen = "?")
    {
        var text = VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, va.LehrerAm(datum), va.Von, va.Bis, trenner);
        var teile = new List<string>();
        if (text.Length > 0) teile.Add(text);
        teile.AddRange(Enumerable.Repeat(offen, va.OffenAm(datum).Count));
        return string.Join(trenner, teile);
    }

    /// <summary>Klassen (Spalte Klasse und Kl-Teil) kompakt: „Alle Klassen“, „Alle Klassen außer …“ oder Liste.</summary>
    public static string KlassenKurztext(Schuldatei d, Veranstaltung va, string trenner = ",") =>
        VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Klasse, va.AlleKlassenIds, va.Von, va.Bis, trenner);

    /// <summary>Nehmen alle (wählbaren) Lehrkräfte an diesem Tag teil?</summary>
    public static bool AlleLehrer(Schuldatei d, Veranstaltung va, DateOnly? datum = null) =>
        VeranstaltungsAuswahl.UmfasstAlle(d, ElementTyp.Lehrer, datum is DateOnly t ? va.LehrerAm(t) : va.LehrerIds, va.Von, va.Bis);

    /// <summary>Sind alle (wählbaren) Klassen beteiligt (Spalte Klasse oder Kl-Teil)?</summary>
    public static bool AlleKlassen(Schuldatei d, Veranstaltung va) =>
        VeranstaltungsAuswahl.UmfasstAlle(d, ElementTyp.Klasse, va.AlleKlassenIds, va.Von, va.Bis);

    /// <summary>Kopie einer Veranstaltung auf ein anderes Datum (Strg+C / Strg+V): Zeitraum wird verschoben, entfernte Lehrkräfte nicht übernommen.</summary>
    public static Veranstaltung Kopieren(Veranstaltung va, DateOnly ab)
    {
        var tage = ab.DayNumber - va.Von.DayNumber;
        return new Veranstaltung
        {
            Text = va.Text, Von = va.Von.AddDays(tage), Bis = va.Bis.AddDays(tage), StundeVon = va.StundeVon, StundeBis = va.StundeBis,
            KlassenIds = va.KlassenIds.ToList(), KlassenteilIds = va.KlassenteilIds.ToList(), LehrerIds = va.LehrerIds.ToList(), RaumIds = va.RaumIds.ToList(),
            GrundId = va.GrundId, Schuelergruppe = va.Schuelergruppe, Zaehlen = va.Zaehlen, Wert = va.Wert,
        };
    }
}

/// <summary>Anlegen, Ändern und Löschen von Absenzen (gemeinsam für Absenzfenster, Dialog und Tests).</summary>
public static class AbsenzVerwaltung
{
    /// <summary>
    /// Eingaben vereinheitlichen: Bis nicht vor Von, am selben Tag ab St. nicht nach bis St., Stunden im Zeitraster,
    /// „bis zur letzten Stunde“ wird als 99 (ganzer Tag) gespeichert.
    /// </summary>
    public static void Normalisieren(Abwesenheit a, int letzteStunde)
    {
        letzteStunde = Math.Max(1, letzteStunde);
        if (a.Bis < a.Von) (a.Von, a.Bis) = (a.Bis, a.Von);
        a.StundeVon = Math.Clamp(a.StundeVon, 1, letzteStunde);
        a.StundeBis = a.StundeBis >= letzteStunde ? 99 : Math.Max(1, a.StundeBis);
        if (a.Von == a.Bis && a.StundeVon > a.StundeBis) (a.StundeVon, a.StundeBis) = (a.StundeBis, a.StundeVon);
    }

    /// <summary>
    /// Ersetzt die alten Absenzen durch die neuen (neue erhalten eine Nummer) und berechnet die Vertretungen des betroffenen
    /// Zeitraums (alter und neuer Zeitraum) neu. Eine identische Absenz wird nicht doppelt angelegt. Liefert die gespeicherten Absenzen.
    /// </summary>
    public static List<Abwesenheit> Speichern(Schuldatei d, IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt)
    {
        var altIds = alt.Select(a => a.Id).ToHashSet();
        var bereich = alt.Select(a => (a.Von, a.Bis)).ToList();
        d.Abwesenheiten.RemoveAll(x => altIds.Contains(x.Id));
        var res = new List<Abwesenheit>();
        foreach (var a in neu)
        {
            if (a.ElementId == 0) throw new InvalidOperationException("Für eine Absenz muss ein Element gewählt sein.");
            Normalisieren(a, d.Zeitraster.StundenProTag);
            if (d.Abwesenheiten.Any(x => x.Art == a.Art && x.ElementId == a.ElementId && x.Von == a.Von && x.Bis == a.Bis && x.StundeVon == a.StundeVon && x.StundeBis == a.StundeBis))
                continue;
            if (a.Id == 0 || (!altIds.Contains(a.Id) && d.Abwesenheiten.Any(x => x.Id == a.Id))) a.Id = d.NeueId();
            d.Abwesenheiten.Add(a);
            res.Add(a);
            bereich.Add((a.Von, a.Bis));
        }
        if (bereich.Count > 0) Vertretungen.Aktualisieren(d, bereich.Min(b => b.Von), bereich.Max(b => b.Bis));
        return res;
    }

    public static void Loeschen(Schuldatei d, IReadOnlyList<Abwesenheit> liste) => Speichern(d, Array.Empty<Abwesenheit>(), liste);
}
