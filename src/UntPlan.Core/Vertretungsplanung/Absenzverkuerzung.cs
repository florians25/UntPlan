using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Lehrkraft früher wieder anwesend: Wird eine Lehrer-Absenz verkürzt oder gelöscht (bzw. die Begleitung einer Klassenabsenz), werden alle
/// Maßnahmen, die wegen dieser Absenz in der nun nicht mehr abwesenden Zeit entstanden sind, rückgängig gemacht – der Unterricht findet
/// wieder regulär mit der eigenen Lehrkraft statt:
/// <list type="bullet">
/// <item>Vertretungen (eingetragene Vertreter werden wieder frei), Betreuungen, Pausenaufsichtsvertretungen, offene Vertretungen und
/// Entfälle der Absenz,</item>
/// <item>Verlegungen bzw. Tausche der Stunden der Lehrkraft (die Stunde liegt wieder an ihrem Ursprung),</item>
/// <item>Vorziehungen in die frei gewordene Stunde (die vorgezogene Stunde kehrt an ihren Platz zurück) und dort eingesetzte
/// Vormerkungen (Sondereinsatz zu Vormerkung),</item>
/// <item>von Hand geteilte Vertretungen (Teilvertretung).</item>
/// </list>
/// Maßnahmen außerhalb der frei gewordenen Zeit sowie Maßnahmen ohne Bezug zur Absenz bleiben unverändert. <see cref="Ermitteln"/> liefert
/// vorab die Liste für die Rückfrage (ändert nichts), <see cref="Speichern"/> speichert die Absenzen und macht die Maßnahmen rückgängig.
/// </summary>
public static class Absenzverkuerzung
{
    /// <summary>Art einer rückgängig gemachten Maßnahme.</summary>
    public enum Massnahmenart
    {
        /// <summary>Vertretung mit Vertreter (auch Betreuung, Statt-Vertretung, EVA mit Lehrkraft) – der Vertreter wird wieder frei.</summary>
        Vertretung,
        /// <summary>Vertretene Pausenaufsicht.</summary>
        Pausenaufsicht,
        /// <summary>Entfall wegen der Absenz.</summary>
        Entfall,
        /// <summary>Verlegung bzw. Tausch einer Stunde der Lehrkraft.</summary>
        Verlegung,
        /// <summary>In die frei gewordene Stunde vorgezogener Unterricht.</summary>
        Vorziehung,
        /// <summary>In der frei gewordenen Stunde eingesetzte Vormerkung (Sondereinsatz).</summary>
        Sondereinsatz,
        /// <summary>Von Hand geteilte Vertretung (zusätzliche Zeile).</summary>
        Teilvertretung,
        /// <summary>Noch offene Vertretung (ohne Vertreter) – entfällt ohne weitere Folgen.</summary>
        Offen,
    }

    /// <summary>Eine Maßnahme: Vertretungszeile (Vtr-Nr.), Lehrkraft der Absenz, Position und Beschreibung für den Hinweis.</summary>
    public sealed record Massnahme(Massnahmenart Art, int VertretungId, int LehrerId, DateOnly Datum, int Stunde, string Beschreibung, int? VertreterId = null);

    /// <summary>Ab wann eine Lehrkraft nach der Änderung wieder anwesend ist (erste nicht mehr abwesende Stunde der alten Absenz).</summary>
    public sealed record Rueckkehr(int LehrerId, DateOnly Datum, int Stunde);

    /// <summary>Ergebnis: Maßnahmen (sortiert nach Datum/Stunde) und Rückkehr je Lehrkraft.</summary>
    public sealed class Ergebnis
    {
        public List<Massnahme> Massnahmen { get; } = new();
        public List<Rueckkehr> Rueckkehr { get; } = new();

        /// <summary>Keine Maßnahme betroffen (offene Vertretungen zählen mit – sie verschwinden ohnehin).</summary>
        public bool Leer => Massnahmen.Count == 0;

        /// <summary>Maßnahmen, die wirklich etwas rückgängig machen (ohne bloß offene Vertretungen).</summary>
        public IEnumerable<Massnahme> Wesentlich => Massnahmen.Where(m => m.Art != Massnahmenart.Offen);

        /// <summary>Lehrkräfte, die wieder frei werden, weil ihre Vertretung entfällt.</summary>
        public IEnumerable<int> FreieVertreter => Massnahmen.Where(m => m.VertreterId.HasValue).Select(m => m.VertreterId!.Value).Distinct();

        public int Anzahl(Massnahmenart a) => Massnahmen.Count(m => m.Art == a);

        /// <summary>
        /// Kurzfassung: „3 Vertretungen und 1 Verlegung werden rückgängig gemacht, weil GAU ab Mi 14.10. 1. Std. wieder anwesend ist.“
        /// (<paramref name="vorschau"/>: „werden“, sonst „wurden“).
        /// </summary>
        public string Zusammenfassung(Schuldatei d, bool vorschau)
        {
            var teile = new List<string>();
            void Teil(int n, string eins, string mehr) { if (n > 0) teile.Add($"{n} {(n == 1 ? eins : mehr)}"); }
            Teil(Anzahl(Massnahmenart.Vertretung) + Anzahl(Massnahmenart.Teilvertretung), "Vertretung", "Vertretungen");
            Teil(Anzahl(Massnahmenart.Pausenaufsicht), "Pausenaufsichtsvertretung", "Pausenaufsichtsvertretungen");
            Teil(Anzahl(Massnahmenart.Entfall), "Entfall", "Entfälle");
            Teil(Anzahl(Massnahmenart.Verlegung), "Verlegung", "Verlegungen");
            Teil(Anzahl(Massnahmenart.Vorziehung), "Vorziehung", "Vorziehungen");
            Teil(Anzahl(Massnahmenart.Sondereinsatz), "Sondereinsatz", "Sondereinsätze");
            Teil(Anzahl(Massnahmenart.Offen), "offene Vertretung", "offene Vertretungen");
            var anzahl = Massnahmen.Count;
            var aufzaehlung = teile.Count <= 1 ? string.Join("", teile) : string.Join(", ", teile.Take(teile.Count - 1)) + " und " + teile[^1];
            var verb = vorschau ? (anzahl == 1 ? "wird" : "werden") : (anzahl == 1 ? "wurde" : "wurden");
            var lehrer = Massnahmen.Select(m => m.LehrerId).Distinct().ToHashSet();
            var weil = string.Join(", ", Rueckkehr.Where(r => lehrer.Contains(r.LehrerId)).Select(r => $"{Kurzname(d, r.LehrerId)} ab {Tag(r.Datum)} {r.Stunde}. Std."));
            var mehrere = Rueckkehr.Count(r => lehrer.Contains(r.LehrerId)) > 1;
            return weil.Length == 0 ? $"{aufzaehlung} {verb} rückgängig gemacht, weil die Absenz verkürzt bzw. gelöscht wurde."
                : $"{aufzaehlung} {verb} rückgängig gemacht, weil {weil} wieder anwesend {(mehrere ? "sind" : "ist")}.";
        }

        /// <summary>Hinweistext mit Kurzfassung, Liste (höchstens <paramref name="max"/> Einträge) und freien Vertretern.</summary>
        public string HinweisText(Schuldatei d, bool vorschau, int max = 15)
        {
            if (Leer) return "";
            var zeilen = new List<string> { Zusammenfassung(d, vorschau), "" };
            zeilen.AddRange(Massnahmen.Take(max).Select(m => "• " + m.Beschreibung));
            if (Massnahmen.Count > max) zeilen.Add($"… und {Massnahmen.Count - max} weitere");
            var frei = FreieVertreter.Select(l => Kurzname(d, l)).Where(k => k.Length > 0).ToList();
            if (frei.Count > 0) zeilen.AddRange(new[] { "", $"Wieder frei: {string.Join(", ", frei)}." });
            return string.Join("\n", zeilen);
        }
    }

    private static string Kurzname(Schuldatei d, int lehrerId) => d.Lehrer.FirstOrDefault(l => l.Id == lehrerId)?.Kurzname ?? "";

    private static string Tag(DateOnly t) => $"{Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {t.Day:00}.{t.Month:00}.";

    /// <summary>Lehrkräfte, die eine Absenz abwesend macht: die Lehrkraft einer Lehrer-Absenz bzw. die Begleitlehrer einer Klassenabsenz.</summary>
    private static IReadOnlyCollection<int> LehrerVon(Abwesenheit a) => a.Art switch
    {
        AbwesenheitsArt.Lehrer => new[] { a.ElementId },
        AbwesenheitsArt.Klasse => a.BegleitLehrerIds,
        _ => Array.Empty<int>(),
    };

    /// <summary>
    /// Maßnahmen, die beim Ersetzen von <paramref name="alt"/> durch <paramref name="neu"/> rückgängig gemacht werden (ändert nichts).
    /// Betroffen ist jede Stunde, in der eine Lehrkraft laut alter Absenz abwesend war und danach weder durch eine andere Absenz noch durch
    /// eine Veranstaltung abwesend ist.
    /// </summary>
    public static Ergebnis Ermitteln(Schuldatei d, IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt)
    {
        var e = new Ergebnis();
        var lehrerJeAbsenz = alt.Where(a => LehrerVon(a).Count > 0).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => LehrerVon(g.First()).ToHashSet());
        if (lehrerJeAbsenz.Count == 0) return e;
        var letzte = d.Zeitraster.StundenProTag;
        var altIds = alt.Select(a => a.Id).ToHashSet();
        // Absenzen nach der Änderung (neue wie beim Speichern vereinheitlicht)
        var nachher = d.Abwesenheiten.Where(a => !altIds.Contains(a.Id)).Concat(neu.Select(n =>
        {
            var k = n.Kopie();
            AbsenzVerwaltung.Normalisieren(k, letzte);
            return k;
        })).ToList();
        bool AbwesendNachher(int l, DateOnly t, int s) =>
            nachher.Any(a => a.Deckt(t, s) && LehrerVon(a).Contains(l)) || Absenzen.VeranstaltungLehrer(d, l, t, s) != null;
        // Pausenaufsicht nach der n. Stunde: abwesend, wenn die Lehrkraft in der n. oder (n+1). Stunde fehlt
        bool Frei(Vertretung v, int l) => v.GangId.HasValue ? !AbwesendNachher(l, v.Datum, v.Stunde) && !AbwesendNachher(l, v.Datum, v.Stunde + 1) : !AbwesendNachher(l, v.Datum, v.Stunde);

        var von = alt.Min(a => a.Von);
        var bis = alt.Max(a => a.Bis);
        var imZeitraum = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).ToList();
        // Zeilen der Absenz in der frei gewordenen Zeit
        var zeilen = imZeitraum.Where(v => v.Quelle == VertretungsQuelle.Absenz && v.AbwesenheitId is int aid && lehrerJeAbsenz.TryGetValue(aid, out var ls)
                                           && v.LehrerId is int l && ls.Contains(l) && Frei(v, l))
            .OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
        if (zeilen.Count > 0)
        {
            var schonVerlegung = new HashSet<int>();
            var verdeckt = new Dictionary<int, Vertretung>();
            foreach (var tag in zeilen.Select(v => v.Datum).Distinct())
                foreach (var (zielId, q) in Vertretungen.ZusammengefassteZeilen(imZeitraum.Where(v => v.Datum == tag).ToList()))
                    if (imZeitraum.FirstOrDefault(v => v.Id == zielId) is { } z) verdeckt.TryAdd(q.Id, z);
            foreach (var v in zeilen)
            {
                var l = v.LehrerId!.Value;
                string Text(Massnahmenart art, Vertretung zeile, string zusatz = "") =>
                    $"{Bezeichnung(art)}: {Verlegungsaufhebung.Beschreibung(d, zeile)}{zusatz}";
                if (v.VerlegungsId is int vid)
                {
                    // die Stunde der Lehrkraft wurde verlegt bzw. getauscht: Verlegung aufheben
                    if (!schonVerlegung.Add(vid)) continue;
                    var ziel = Verlegungsaufhebung.Ziel(d, v) ?? v;
                    e.Massnahmen.Add(new Massnahme(Massnahmenart.Verlegung, v.Id, l, v.Datum, v.Stunde, Text(Massnahmenart.Verlegung, ziel)));
                    continue;
                }
                if (v.IstEntfall && verdeckt.TryGetValue(v.Id, out var vz) && vz.VerlegungsId is int zid && schonVerlegung.Add(zid))
                {
                    // in die frei gewordene Stunde wurde Unterricht vorgezogen: die Vorziehung aufheben
                    e.Massnahmen.Add(new Massnahme(Massnahmenart.Vorziehung, vz.Id, l, v.Datum, v.Stunde, Text(Massnahmenart.Vorziehung, vz)));
                    continue;
                }
                if (v.IstEntfall && imZeitraum.FirstOrDefault(s => s.Datum == v.Datum && s.Stunde == v.Stunde && s.VormerkungId.HasValue
                        && s.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung && s.NeueKlassenIds.Intersect(v.NeueKlassenIds).Any()) is { } se)
                {
                    // Vormerkung in der frei gewordenen Stunde eingesetzt: Sondereinsatz löschen (die Vormerkung wird wieder frei)
                    e.Massnahmen.Add(new Massnahme(Massnahmenart.Sondereinsatz, se.Id, l, v.Datum, v.Stunde, Text(Massnahmenart.Sondereinsatz, se, VertreterText(d, se.VertreterId)), se.VertreterId));
                    continue;
                }
                // entfallene Bereitschaft: keine Maßnahme (die Bereitschaft findet einfach wieder statt)
                if (v.Art == VertretungsArt.BereitschaftEntfaellt) continue;
                var artM = v.GangId.HasValue ? (v.VertreterId.HasValue ? Massnahmenart.Pausenaufsicht : Massnahmenart.Offen)
                    : v.IstEntfall ? Massnahmenart.Entfall
                    : v.Offen ? Massnahmenart.Offen : Massnahmenart.Vertretung;
                var vertreter = v.VertreterId is int vt && vt != l ? vt : (int?)null;
                var art = Vertretungsplan.ArtText(d, v.Art);
                var zusatzText = (art.Length > 0 && artM is Massnahmenart.Vertretung ? $" ({art})" : "") + VertreterText(d, vertreter);
                e.Massnahmen.Add(new Massnahme(artM, v.Id, l, v.Datum, v.Stunde, Text(artM, v, zusatzText), vertreter));
            }
            // von Hand geteilte Vertretungen derselben Stunde (zusätzliche Zeile ohne Absenzbezug)
            var stunden = zeilen.Where(v => v.UnterrichtId.HasValue).Select(v => (v.Datum, v.Stunde, v.UnterrichtId, v.ZeilenIndex, v.LehrerId)).ToHashSet();
            foreach (var t in imZeitraum.Where(v => v.Quelle == VertretungsQuelle.Manuell && v.Art == VertretungsArt.Teilvertretung && stunden.Contains((v.Datum, v.Stunde, v.UnterrichtId, v.ZeilenIndex, v.LehrerId))))
                e.Massnahmen.Add(new Massnahme(Massnahmenart.Teilvertretung, t.Id, t.LehrerId!.Value, t.Datum, t.Stunde,
                    $"{Bezeichnung(Massnahmenart.Teilvertretung)}: {Verlegungsaufhebung.Beschreibung(d, t)}{VertreterText(d, t.VertreterId)}", t.VertreterId));
        }
        e.Massnahmen.Sort((x, y) => (x.Datum, x.Stunde, x.VertretungId).CompareTo((y.Datum, y.Stunde, y.VertretungId)));

        // ab wann ist die Lehrkraft wieder anwesend? erste nicht mehr abwesende Stunde der alten Absenz an einem Unterrichtstag
        foreach (var l in lehrerJeAbsenz.Values.SelectMany(x => x).Distinct())
        {
            Rueckkehr? erste = null;
            foreach (var a in alt.Where(a => lehrerJeAbsenz.TryGetValue(a.Id, out var ls) && ls.Contains(l)))
            {
                var ende = a.Bis.DayNumber - a.Von.DayNumber > 400 ? a.Von.AddDays(400) : a.Bis;
                for (var t = a.Von; t <= ende && (erste is null || t <= erste.Datum); t = t.AddDays(1))
                {
                    if (!Kalender.IstUnterrichtstag(d, t)) continue;
                    var s = Enumerable.Range(1, letzte).FirstOrDefault(s => a.Deckt(t, s) && !AbwesendNachher(l, t, s));
                    if (s == 0) continue;
                    if (erste is null || (t, s).CompareTo((erste.Datum, erste.Stunde)) < 0) erste = new Rueckkehr(l, t, s);
                    break;
                }
            }
            if (erste != null) e.Rueckkehr.Add(erste);
        }
        return e;
    }

    private static string VertreterText(Schuldatei d, int? vertreterId) =>
        vertreterId is int v && Kurzname(d, v) is { Length: > 0 } k ? $" – {k} wieder frei" : "";

    private static string Bezeichnung(Massnahmenart a) => a switch
    {
        Massnahmenart.Vertretung => "Vertretung",
        Massnahmenart.Pausenaufsicht => "Pausenaufsicht",
        Massnahmenart.Entfall => "Entfall",
        Massnahmenart.Verlegung => "Verlegung",
        Massnahmenart.Vorziehung => "Vorziehung",
        Massnahmenart.Sondereinsatz => "Sondereinsatz",
        Massnahmenart.Teilvertretung => "Teilvertretung",
        _ => "offene Vertretung",
    };

    /// <summary>
    /// Absenzen speichern (wie <see cref="AbsenzVerwaltung.Speichern"/>) und die Maßnahmen der frei gewordenen Zeit rückgängig machen.
    /// Liefert die gespeicherten Absenzen und die rückgängig gemachten Maßnahmen.
    /// </summary>
    public static (List<Abwesenheit> Gespeichert, Ergebnis Ergebnis) Speichern(Schuldatei d, IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt)
    {
        var e = Ermitteln(d, neu, alt);
        // die Neuberechnung entfernt die Zeilen der Absenz (Vertretungen, Entfälle, offene Vertretungen); Verlegungen bleiben dabei stehen
        var gespeichert = AbsenzVerwaltung.Speichern(d, neu, alt);
        Aufheben(d, e);
        return (gespeichert, e);
    }

    /// <summary>Verlegungen, Vorziehungen, Sondereinsätze und Teilvertretungen der Maßnahmenliste aufheben bzw. löschen.</summary>
    private static void Aufheben(Schuldatei d, Ergebnis e)
    {
        var verlegungen = e.Massnahmen.Where(m => m.Art is Massnahmenart.Verlegung or Massnahmenart.Vorziehung).Select(m => m.VertretungId).ToList();
        if (verlegungen.Count > 0) Verlegungsaufhebung.AlleAufheben(d, verlegungen);
        var sonder = e.Massnahmen.Where(m => m.Art == Massnahmenart.Sondereinsatz).Select(m => m.VertretungId).ToList();
        if (sonder.Count > 0) Verlegungsaufhebung.SondereinsaetzeLoeschen(d, sonder);
        var teil = e.Massnahmen.Where(m => m.Art == Massnahmenart.Teilvertretung).Select(m => m.VertretungId).ToHashSet();
        if (teil.Count > 0)
        {
            var tage = d.Vertretungen.Where(v => teil.Contains(v.Id)).Select(v => v.Datum).Distinct().ToList();
            d.Vertretungen.RemoveAll(v => teil.Contains(v.Id));
            foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        }
        // Zeilen der Absenz, die die Neuberechnung nicht entfernt hat (z. B. Tag außerhalb des Unterrichts): ebenfalls weg
        var rest = e.Massnahmen.Where(m => m.Art is Massnahmenart.Vertretung or Massnahmenart.Pausenaufsicht or Massnahmenart.Entfall or Massnahmenart.Offen)
            .Select(m => m.VertretungId).ToHashSet();
        d.Vertretungen.RemoveAll(v => rest.Contains(v.Id) && v.Quelle == VertretungsQuelle.Absenz && v.VerlegungsId is null
                                      && (v.AbwesenheitId is null || !d.Abwesenheiten.Any(a => a.Id == v.AbwesenheitId && v.LehrerId is int l && LehrerVon(a).Contains(l) && a.Deckt(v.Datum, v.Stunde))));
    }

    /// <summary>
    /// Rückmeldung „wieder anwesend ab“: Lehrer-Absenzen der Lehrkraft, die die Stunde <paramref name="abStunde"/> am <paramref name="ab"/>
    /// oder später decken und vorher begonnen haben, enden mit der Stunde davor; beginnen sie erst ab diesem Zeitpunkt, entfallen sie.
    /// Spätere, erst danach beginnende Absenzen (z. B. eine geplante Fortbildung) bleiben unberührt. Liefert die neuen und alten Absenzen für
    /// <see cref="Speichern"/> (leer, wenn die Lehrkraft zu diesem Zeitpunkt nicht abwesend ist).
    /// </summary>
    public static (List<Abwesenheit> Neu, List<Abwesenheit> Alt) WiederAnwesend(Schuldatei d, int lehrerId, DateOnly ab, int abStunde)
    {
        var neu = new List<Abwesenheit>();
        var alt = new List<Abwesenheit>();
        abStunde = Math.Clamp(abStunde, 1, Math.Max(1, d.Zeitraster.StundenProTag));
        foreach (var a in d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrerId && a.Deckt(ab, abStunde)).ToList())
        {
            alt.Add(a);
            // beginnt die Absenz genau hier, bleibt nichts übrig
            if (a.Von == ab && a.StundeVon >= abStunde) continue;
            var k = a.Kopie();
            if (abStunde > 1) { k.Bis = ab; k.StundeBis = abStunde - 1; }
            else { k.Bis = ab.AddDays(-1); k.StundeBis = 99; }
            if (k.Bis < k.Von) continue;
            neu.Add(k);
        }
        return (neu, alt);
    }
}
