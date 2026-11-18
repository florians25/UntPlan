using System.Text.RegularExpressions;
using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>Unterricht, der in verschiedenen Perioden unterschiedliche Schülergruppen hat (je Periode die eingetragene Gruppe).</summary>
public sealed record InkonsistenteSchuelergruppe(int UnterrichtId, IReadOnlyList<(Periode Periode, string Schuelergruppe)> Perioden);

/// <summary>Unterrichte mit gleichem Fach und gleichen Klassen, von denen mehr als einer keine Schülergruppe hat.</summary>
public sealed record FehlendeSchuelergruppen(string Fach, string Klassen, IReadOnlyList<int> UnterrichtIds);

/// <summary>
/// Schülergruppen der Unterrichte: automatische Namen nach dem Muster „Fach_Klassen_Lehrkraft“ (z. B. „FU1_FIS4A_LN“, mehrere Klassen
/// ohne Trennzeichen: „Gw_1a1b2a2b_Hugo“), Platzhalter bei der Eingabe ($, %, #, TEXT_#), Umbenennen in allen Perioden und die
/// Prüfungen vor der Übertragung (fehlende bzw. über die Perioden unterschiedliche Schülergruppen).
/// </summary>
/// <remarks>
/// Die Schülergruppe kennzeichnet zusammen mit dem Fach einen Unterricht dauerhaft (Klassenbuch in UntPlanWeb). Sie muss deshalb über
/// alle Perioden gleich sein und ändert sich bei Lehrer- oder Raumänderungen nicht – ein einmal vergebener Name wird nie automatisch
/// angepasst oder überschrieben.
/// </remarks>
public static class Schuelergruppen
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex Leerraum = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Kopplungszeile, aus der Fach und Lehrkraft des Namens stammen: erste Zeile mit Fach und Lehrkraft, sonst mit Fach.</summary>
    private static Kopplungszeile? Basiszeile(Unterricht u) =>
        u.Zeilen.FirstOrDefault(z => z.FachId.HasValue && z.LehrerId.HasValue) ?? u.Zeilen.FirstOrDefault(z => z.FachId.HasValue) ?? u.Zeilen.FirstOrDefault();

    private static string Kurz(IEnumerable<Element> liste, int? id) =>
        id is int i && liste.FirstOrDefault(e => e.Id == i) is { } e ? Leerraum.Replace(e.Kurzname, "") : "";

    /// <summary>Klassen des Unterrichts ohne Trennzeichen in der Reihenfolge der Eingabe (z. B. „1a1b2a2b“).</summary>
    private static string Klassen(Schuldatei d, Unterricht u) => string.Concat(u.Zeilen.SelectMany(z => z.KlassenIds).Distinct().Select(k => Kurz(d.Klassen, k)));

    /// <summary>
    /// Automatischer Name „Fach_Klassen_Lehrkraft“ (Platzhalter „$“): Fach und Lehrkraft der ersten Kopplungszeile, alle Klassen des
    /// Unterrichts. Null, wenn Fach, Klasse oder Lehrkraft fehlen (z. B. Sprechstunde ohne Klasse oder noch nicht vollständig eingegeben).
    /// </summary>
    public static string? Name(Schuldatei d, Unterricht u)
    {
        var z = Basiszeile(u);
        if (z is null) return null;
        var fach = Kurz(d.Faecher, z.FachId);
        var lehrer = Kurz(d.Lehrer, z.LehrerId ?? u.Zeilen.FirstOrDefault(x => x.LehrerId.HasValue)?.LehrerId);
        var klassen = Klassen(d, u);
        return fach.Length == 0 || lehrer.Length == 0 || klassen.Length == 0 ? null : $"{fach}_{klassen}_{lehrer}";
    }

    /// <summary>Name nach Platzhalter: „$“ = Fach_Klassen_Lehrkraft, „%“ = Fach_Klassen_Raum (Fachraum vor Stammraum); fehlende Teile entfallen.</summary>
    private static string Muster(Schuldatei d, Unterricht u, bool raum)
    {
        var z = Basiszeile(u);
        var fach = Kurz(d.Faecher, z?.FachId);
        var dritter = raum
            ? Kurz(d.Raeume, z?.Wunschraum ?? u.Zeilen.Select(x => x.Wunschraum).FirstOrDefault(r => r.HasValue))
            : Kurz(d.Lehrer, z?.LehrerId ?? u.Zeilen.FirstOrDefault(x => x.LehrerId.HasValue)?.LehrerId);
        return string.Join("_", new[] { fach, Klassen(d, u), dritter }.Where(t => t.Length > 0));
    }

    /// <summary>Enthält die Eingabe einen Platzhalter ($, %, #)?</summary>
    public static bool IstPlatzhalter(string eingabe) => eingabe.IndexOfAny(new[] { '$', '%', '#' }) >= 0;

    /// <summary>
    /// Eingabe in der Spalte „Schülergruppe“ (auch für mehrere markierte Unterrichte auf einmal): „$“ = Fach_Klassen_Lehrkraft
    /// (z. B. „Mat_1a_Arist“), „%“ = Fach_Klassen_Raum (z. B. „Mat_1a_R1a“), „#“ = laufende Nummer (1, 2, 3 …), „TEXT_#“ = Text mit
    /// laufender Nummer (TEXT_1, TEXT_2 …). Ohne Platzhalter bleibt die Eingabe unverändert (nur ohne Leerzeichen am Rand).
    /// </summary>
    public static string Platzhalter(Schuldatei d, Unterricht u, string eingabe, int laufnummer)
    {
        var t = eingabe.Trim();
        if (!IstPlatzhalter(t)) return t;
        if (t.Contains('$')) t = t.Replace("$", Muster(d, u, false));
        if (t.Contains('%')) t = t.Replace("%", Muster(d, u, true));
        if (t.Contains('#')) t = t.Replace("#", laufnummer.ToString(Format.De));
        return t;
    }

    /// <summary>
    /// Fehlende Schülergruppen automatisch anlegen (Einstellung „Schülergruppen automatisch anlegen“; mit <paramref name="erzwingen"/>
    /// unabhängig davon): jeder Unterricht ohne Schülergruppe erhält – in allen Perioden gleich – den Namen aus einer anderen Periode,
    /// in der er schon eine hat, sonst „Fach_Klassen_Lehrkraft“. Ist der Name bereits an einen anderen Unterricht vergeben (gleiche
    /// Lehrkraft unterrichtet dasselbe Fach zweimal in der Klasse), wird „_2“, „_3“ … angehängt. Vorhandene Schülergruppen bleiben
    /// unverändert. Mit <paramref name="ohneLehrkraft"/> erhalten auch Unterrichte ohne Lehrkraft eine Schülergruppe („Fach_Klassen“,
    /// z. B. vor der Übertragung nach UntPlanWeb). Liefert die Zahl der ergänzten Unterrichte (je Periode gezählt).
    /// </summary>
    public static int Ergaenzen(Schuldatei d, bool erzwingen = false, bool ohneLehrkraft = false)
    {
        if (!erzwingen && !d.Einstellungen.SchuelergruppenAutomatisch) return 0;
        var alle = d.Perioden.SelectMany(p => (p.Unterrichte ?? new()).Select(u => (Periode: p, U: u))).ToList();
        if (alle.All(x => x.U.Schuelergruppe.Length > 0)) return 0;
        // vergebene Namen (Unterricht und Kopplungszeilen) → Unterrichtsnummer
        var vergeben = new Dictionary<string, int>(Ci);
        foreach (var (_, u) in alle)
            foreach (var n in u.Zeilen.Select(z => z.Schuelergruppe).Append(u.Schuelergruppe).Where(n => n.Length > 0))
                vergeben.TryAdd(n, u.Id);
        var aktiv = d.AktivePeriodeId;
        var anzahl = 0;
        foreach (var g in alle.GroupBy(x => x.U.Id).OrderBy(g => g.Key))
        {
            var leer = g.Where(x => x.U.Schuelergruppe.Length == 0).Select(x => x.U).ToList();
            if (leer.Count == 0) continue;
            var name = g.Select(x => x.U.Schuelergruppe).FirstOrDefault(n => n.Length > 0);
            if (name is null)
            {
                var basis = g.FirstOrDefault(x => x.Periode.Id == aktiv).U ?? g.First().U;
                var roh = Name(d, basis) ?? (ohneLehrkraft && basis.HauptfachId.HasValue && basis.KlassenIds.Any() ? Muster(d, basis, false) : null);
                if (roh is null) continue;
                name = roh;
                for (var i = 2; vergeben.TryGetValue(name, out var id) && id != g.Key; i++) name = $"{roh}_{i}";
                vergeben[name] = g.Key;
            }
            foreach (var u in leer) u.Schuelergruppe = name;
            anzahl += leer.Count;
        }
        return anzahl;
    }

    /// <summary>Unterrichte, die in verschiedenen Perioden unterschiedliche (eingetragene) Schülergruppen haben.</summary>
    public static List<InkonsistenteSchuelergruppe> Inkonsistente(Schuldatei d) =>
        d.Perioden.OrderBy(p => p.Von).ThenBy(p => p.Id).SelectMany(p => (p.Unterrichte ?? new()).Select(u => (Periode: p, U: u)))
            .GroupBy(x => x.U.Id)
            .Where(g => g.Select(x => x.U.Schuelergruppe).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => new InkonsistenteSchuelergruppe(g.Key, g.Select(x => (x.Periode, x.U.Schuelergruppe)).ToList()))
            .ToList();

    /// <summary>
    /// Gruppen von Unterrichten mit gleichem Fach und gleichen Klassen, in denen zwei oder mehr Unterrichte keine Schülergruppe haben
    /// (höchstens einer darf ohne sein – sonst lassen sie sich in UntPlanWeb nicht unterscheiden). Ignorierte Unterrichte zählen nicht.
    /// </summary>
    public static List<FehlendeSchuelergruppen> Fehlende(Schuldatei d)
    {
        var res = new List<FehlendeSchuelergruppen>();
        var gesehen = new HashSet<string>();
        foreach (var p in d.Perioden)
            foreach (var g in (p.Unterrichte ?? new()).Where(u => !u.Ignorieren && u.KlassenIds.Any() && u.HauptfachId.HasValue)
                         .GroupBy(u => (Fach: u.HauptfachId!.Value, Klassen: string.Join(",", u.KlassenIds.OrderBy(k => k)))))
            {
                var ohne = g.Where(u => u.Schuelergruppe.Length == 0).Select(u => u.Id).OrderBy(i => i).ToList();
                if (ohne.Count < 2 || !gesehen.Add($"{g.Key.Fach}|{g.Key.Klassen}|{string.Join(",", ohne)}")) continue;
                res.Add(new FehlendeSchuelergruppen(Kurz(d.Faecher, g.Key.Fach),
                    string.Join(",", g.First().KlassenIds.Select(k => d.Klassen.FirstOrDefault(x => x.Id == k)?.Kurzname ?? "")), ohne));
            }
        return res;
    }

    /// <summary>
    /// Schülergruppe umbenennen (Antwort „Ja“ auf „Wollen Sie die Schülergruppe (in allen Perioden) umbenennen?“): in allen Perioden bei
    /// allen Unterrichten und Kopplungszeilen, denen sie zugewiesen ist, sowie in Vertretungen und Veranstaltungen. Liefert die Zahl der
    /// geänderten Einträge.
    /// </summary>
    public static int Umbenennen(Schuldatei d, string alt, string neu)
    {
        alt = alt.Trim();
        neu = neu.Trim();
        if (alt.Length == 0 || string.Equals(alt, neu, StringComparison.Ordinal)) return 0;
        var n = 0;
        foreach (var u in d.AlleUnterrichte)
        {
            if (Ci.Equals(u.Schuelergruppe, alt)) { u.Schuelergruppe = neu; n++; }
            foreach (var z in u.Zeilen.Where(z => Ci.Equals(z.Schuelergruppe, alt))) { z.Schuelergruppe = neu; n++; }
        }
        foreach (var v in d.Vertretungen.Where(v => Ci.Equals(v.Schuelergruppe, alt))) { v.Schuelergruppe = neu; n++; }
        foreach (var va in d.Veranstaltungen.Where(v => Ci.Equals(v.Schuelergruppe, alt))) { va.Schuelergruppe = neu; n++; }
        return n;
    }

    /// <summary>Wird die Schülergruppe außer bei diesem Unterricht (dieser Periode) noch verwendet – andere Periode bzw. anderer Unterricht?</summary>
    public static bool AnderweitigVerwendet(Schuldatei d, Unterricht u, string gruppe) =>
        gruppe.Length > 0 && d.Perioden.SelectMany(p => p.Unterrichte ?? new()).Any(x => !ReferenceEquals(x, u)
            && (Ci.Equals(x.Schuelergruppe, gruppe) || x.Zeilen.Any(z => Ci.Equals(z.Schuelergruppe, gruppe))));

    /// <summary>Gibt es den Unterricht (gleiche U-Nr.) außer in der aktiven Periode noch in einer anderen Periode?</summary>
    public static bool InMehrerenPerioden(Schuldatei d, int unterrichtId) =>
        d.Perioden.Count(p => (p.Unterrichte ?? new()).Any(u => u.Id == unterrichtId)) > 1;

    /// <summary>„Die Änderung in allen Perioden (auch in Mutterperioden) durchführen“: Schülergruppe des Unterrichts in allen Perioden setzen.</summary>
    public static int InAllenPerioden(Schuldatei d, int unterrichtId, string gruppe)
    {
        var n = 0;
        foreach (var u in d.AlleUnterrichte.Where(u => u.Id == unterrichtId)) { u.Schuelergruppe = gruppe.Trim(); n++; }
        return n;
    }

    /// <summary>
    /// „Einen neuen Unterricht anlegen“: der Unterricht der aktiven Periode erhält eine neue U-Nr. und die neue Schülergruppe – alle
    /// Eigenschaften, der Stundenplan und die Vertretungen (im Zeitraum der aktiven Periode) gehen auf ihn über; der ursprüngliche
    /// Unterricht ist damit aus der aktiven Periode entfernt und bleibt in den übrigen Perioden unverändert. Liefert die neue U-Nr.
    /// </summary>
    public static int AlsNeuenUnterricht(Schuldatei d, int unterrichtId, string gruppe)
    {
        var p = d.AktivePeriode;
        var u = (p.Unterrichte ?? new()).FirstOrDefault(x => x.Id == unterrichtId) ?? throw new ArgumentException($"Unterricht {unterrichtId} gibt es in der aktiven Periode nicht.");
        var neu = d.NeueId();
        u.Id = neu;
        u.Schuelergruppe = gruppe.Trim();
        foreach (var s in p.Plan.Concat(p.Vertretungsstand ?? new()).Where(s => s.UnterrichtId == unterrichtId)) s.UnterrichtId = neu;
        foreach (var f in p.Unterrichtsfolgen ?? new())
            for (var i = 0; i < f.UnterrichtIds.Count; i++) if (f.UnterrichtIds[i] == unterrichtId) f.UnterrichtIds[i] = neu;
        foreach (var v in d.Vertretungen.Where(v => v.UnterrichtId == unterrichtId && d.PeriodeAm(v.Datum).Id == p.Id)) v.UnterrichtId = neu;
        return neu;
    }

    /// <summary>Schülergruppe einer Kopplungszeile (eigene Gruppe der Zeile vor der des Unterrichts).</summary>
    public static string VonZeile(Unterricht u, int zeilenIndex) =>
        zeilenIndex >= 0 && zeilenIndex < u.Zeilen.Count && u.Zeilen[zeilenIndex].Schuelergruppe is { Length: > 0 } z ? z : u.Schuelergruppe;

    /// <summary>
    /// Kombination Fach–Schülergruppe einer Vertretung bzw. eines Sondereinsatzes: Unterricht der Klasse(n) mit genau diesem Fach und
    /// dieser Schülergruppe (null = keiner). Gleich wie im vertretenen Unterricht → klassische Vertretung; ein anderer Unterricht der
    /// Klasse → zusätzliche Stunde dieses Unterrichts (die ursprüngliche Stunde entfällt); keiner → Sondereinsatz.
    /// </summary>
    public static Unterricht? UnterrichtZu(IEnumerable<Unterricht> unterrichte, int? fachId, string gruppe, IEnumerable<int> klassenIds)
    {
        if (fachId is null || string.IsNullOrWhiteSpace(gruppe)) return null;
        var klassen = klassenIds.ToHashSet();
        gruppe = gruppe.Trim();
        return unterrichte.Where(u => !u.Ignorieren).SelectMany(u => u.Zeilen.Select((z, i) => (U: u, Z: z, I: i)))
            .FirstOrDefault(x => x.Z.FachId == fachId && Ci.Equals(VonZeile(x.U, x.I), gruppe) && (klassen.Count == 0 || x.U.KlassenIds.Any(klassen.Contains))).U;
    }

    /// <summary>
    /// Auswahlliste „Schülergruppe“ (z. B. im Sondereinsatz-Dialog): zuerst die Schülergruppen der Unterrichte dieser Klassen (mit
    /// passendem Fach vorn), danach alle übrigen bekannten Schülergruppen.
    /// </summary>
    public static List<string> Auswahl(Schuldatei d, IEnumerable<int> klassenIds, int? fachId = null)
    {
        var klassen = klassenIds.ToHashSet();
        var de = StringComparer.Create(Format.De, true);
        var eigene = d.Unterrichte.Where(u => u.KlassenIds.Any(klassen.Contains))
            .SelectMany(u => u.Zeilen.Select((z, i) => (Gruppe: VonZeile(u, i), Passend: fachId.HasValue && z.FachId == fachId)))
            .Where(x => x.Gruppe.Length > 0).OrderBy(x => x.Passend ? 0 : 1).ThenBy(x => x.Gruppe, de).Select(x => x.Gruppe);
        return eigene.Concat(Vertretungsplanung.VeranstaltungsLogik.Schuelergruppen(d)).Distinct(Ci).ToList();
    }
}
