using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Eine im Stundenplan umgeplante Stunde: Unterricht von (AltTag, AltStunde) nach (NeuTag, NeuStunde).</summary>
public sealed record Umplanung(int UnterrichtId, int AltTag, int AltStunde, int NeuTag, int NeuStunde);

/// <summary>Ergebnis der Nacherfassung umgeplanter Stunden in der Vertretungsplanung.</summary>
public sealed class UmplanungsErgebnis
{
    /// <summary>Erkannte Umplanungen (alle Perioden).</summary>
    public List<Umplanung> Umplanungen { get; } = new();
    /// <summary>Auf die neue Lage übertragene Vertretungszeilen.</summary>
    public List<Vertretung> Uebertragen { get; } = new();
    /// <summary>Nicht mehr zuordenbare (bearbeitete) Vertretungen, die als Vormerkung erhalten bleiben.</summary>
    public List<Vormerkung> Vormerkungen { get; } = new();
    /// <summary>Hinweise für den Benutzer (nicht zuordenbar, Ziel bereits belegt, Ursache besteht zur neuen Zeit nicht mehr …).</summary>
    public List<string> Hinweise { get; } = new();
    /// <summary>Wurden Vertretungen bzw. Vormerkungen geändert?</summary>
    public bool Geaendert => Uebertragen.Count > 0 || Vormerkungen.Count > 0;
}

/// <summary>
/// Nacherfassung im Stundenplan umgeplanter Stunden für die Vertretungsplanung: Wird eine Stunde im Stundenplan verlegt, obwohl
/// für sie schon Vertretungen bzw. Veranstaltungen geplant waren, passen diese nicht mehr zum Regelplan und würden nicht mehr
/// angezeigt. Beim Wechsel in die Vertretungsplanung (bzw. bei der Neuberechnung) wird der Stundenplan mit dem Stand des letzten
/// Abgleichs (<see cref="Periode.Vertretungsstand"/>) verglichen; die Vertretungen der umgeplanten Stunden werden mit Vertreter,
/// Raum, Text und Art auf die neue Lage derselben Woche übertragen. Nicht mehr zuordenbare bearbeitete Vertretungen bleiben als
/// Vormerkung erhalten (statt still verloren zu gehen).
/// </summary>
public static class Umplanungsabgleich
{
    private static string Schluessel(Planstunde p) => $"{p.UnterrichtId}/{p.Tag}/{p.Stunde}";

    /// <summary>Hat eine Periode noch keinen Stand bzw. weicht der Stand vom Stundenplan ab?</summary>
    public static bool StandVeraltet(Periode p) =>
        p.Vertretungsstand is null || !p.Vertretungsstand.Select(Schluessel).OrderBy(x => x).SequenceEqual(p.Plan.Select(Schluessel).OrderBy(x => x));

    /// <summary>Aktuellen Stundenplan aller Perioden als Stand für den nächsten Abgleich merken.</summary>
    public static void StandSichern(Schuldatei d)
    {
        foreach (var p in d.Perioden) p.Vertretungsstand = p.Plan.Select(x => x.Kopie()).ToList();
    }

    /// <summary>Fehlenden Stand nachtragen (z. B. beim Öffnen älterer Dateien); liefert true, wenn etwas gesetzt wurde.</summary>
    public static bool StandErgaenzen(Schuldatei d)
    {
        var gesetzt = false;
        foreach (var p in d.Perioden.Where(p => p.Vertretungsstand is null))
        {
            p.Vertretungsstand = p.Plan.Select(x => x.Kopie()).ToList();
            gesetzt = true;
        }
        return gesetzt;
    }

    /// <summary>
    /// Umplanungen einer Periode seit dem letzten Stand: je Unterricht werden weggefallene und neue Positionen gepaart – zuerst
    /// zusammenhängende Blöcke gleicher Länge (Stunde für Stunde), danach die übrigen Stunden in zeitlicher Reihenfolge.
    /// <paramref name="nichtZuordenbar"/> erhält die weggefallenen Positionen ohne neue Lage (Unterricht gelöscht bzw. weniger Stunden).
    /// </summary>
    public static List<Umplanung> Umplanungen(Periode p, out List<(int UnterrichtId, int Tag, int Stunde)> nichtZuordenbar)
    {
        nichtZuordenbar = new();
        var res = new List<Umplanung>();
        if (p.Vertretungsstand is null) return res;
        var jetzt = p.Plan.Select(x => (x.UnterrichtId, x.Tag, x.Stunde)).ToList();
        var vorher = p.Vertretungsstand.Select(x => (x.UnterrichtId, x.Tag, x.Stunde)).ToList();
        foreach (var uid in vorher.Select(x => x.UnterrichtId).Distinct().OrderBy(x => x))
        {
            var alt = Differenz(vorher.Where(x => x.UnterrichtId == uid), jetzt.Where(x => x.UnterrichtId == uid));
            if (alt.Count == 0) continue;
            var neu = Differenz(jetzt.Where(x => x.UnterrichtId == uid), vorher.Where(x => x.UnterrichtId == uid));
            var altBloecke = Laeufe(alt);
            var neuBloecke = Laeufe(neu);
            // 1. Blöcke gleicher Länge (Doppelstunde bleibt Doppelstunde)
            foreach (var a in altBloecke.ToList())
            {
                var n = neuBloecke.FirstOrDefault(b => b.Count == a.Count);
                if (n is null) continue;
                for (var i = 0; i < a.Count; i++) res.Add(new Umplanung(uid, a[i].Tag, a[i].Stunde, n[i].Tag, n[i].Stunde));
                altBloecke.Remove(a);
                neuBloecke.Remove(n);
            }
            // 2. übrige Stunden in zeitlicher Reihenfolge
            var restAlt = altBloecke.SelectMany(x => x).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).ToList();
            var restNeu = neuBloecke.SelectMany(x => x).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).ToList();
            for (var i = 0; i < restAlt.Count; i++)
            {
                if (i < restNeu.Count) res.Add(new Umplanung(uid, restAlt[i].Tag, restAlt[i].Stunde, restNeu[i].Tag, restNeu[i].Stunde));
                else nichtZuordenbar.Add((uid, restAlt[i].Tag, restAlt[i].Stunde));
            }
        }
        return res;
    }

    private static List<(int Tag, int Stunde)> Differenz(IEnumerable<(int UnterrichtId, int Tag, int Stunde)> a, IEnumerable<(int UnterrichtId, int Tag, int Stunde)> b)
    {
        var rest = b.Select(x => (x.Tag, x.Stunde)).ToList();
        var res = new List<(int, int)>();
        foreach (var x in a.Select(x => (x.Tag, x.Stunde)).OrderBy(x => x.Tag).ThenBy(x => x.Stunde))
            if (!rest.Remove(x)) res.Add(x);
        return res;
    }

    private static List<List<(int Tag, int Stunde)>> Laeufe(List<(int Tag, int Stunde)> pos)
    {
        var res = new List<List<(int Tag, int Stunde)>>();
        foreach (var x in pos.OrderBy(x => x.Tag).ThenBy(x => x.Stunde))
        {
            var letzter = res.LastOrDefault();
            if (letzter != null && letzter[^1].Tag == x.Tag && letzter[^1].Stunde == x.Stunde - 1) letzter.Add(x);
            else res.Add(new List<(int Tag, int Stunde)> { x });
        }
        return res;
    }

    /// <summary>Vertretungszeilen, die an einer regulären Unterrichtsstunde hängen und mit ihr wandern.</summary>
    public static bool Uebertragbar(Vertretung v) =>
        v.UnterrichtId.HasValue && v.VerlegungsId is null && v.GangId is null &&
        v.Art is not (VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.SondereinsatzVormerkung or VertretungsArt.Klausur
            or VertretungsArt.Vormerkung or VertretungsArt.Pausenaufsicht or VertretungsArt.Raumbuchung or VertretungsArt.Unterricht) &&
        !(v.Art == VertretungsArt.Veranstaltung && v.ErsatzFuerLehrerId.HasValue) && !(v.Art == VertretungsArt.Sondereinsatz && v.LehrerId is null);

    /// <summary>Enthält die Zeile Planungsarbeit, die nicht verloren gehen darf (bearbeitet, manuell, Vertreter, Raum oder Text)?</summary>
    public static bool Bearbeitet(Vertretung v) =>
        v.Bearbeitet || !Vertretungen.IstAbgeleitet(v) || v.VertreterId.HasValue || v.VertretungsraumId.HasValue || !string.IsNullOrWhiteSpace(v.Text);

    /// <summary>
    /// Umgeplante Stunden nacherfassen: Vertretungen auf die neue Lage übertragen, nicht zuordenbare bearbeitete Vertretungen als
    /// Vormerkung behalten, die betroffenen Tage neu berechnen und den Stand für den nächsten Abgleich merken.
    /// </summary>
    public static UmplanungsErgebnis Nacherfassen(Schuldatei d, string benutzer = "")
    {
        var erg = new UmplanungsErgebnis();
        var tage = new HashSet<DateOnly>();
        var ix = new DatenIndex(d);
        foreach (var per in d.Perioden)
        {
            if (per.Vertretungsstand is null) continue;
            var ump = Umplanungen(per, out var weg);
            if (ump.Count == 0 && weg.Count == 0) continue;
            erg.Umplanungen.AddRange(ump);
            var ziel = ump.ToDictionary(u => (u.UnterrichtId, u.AltTag, u.AltStunde), u => (u.NeuTag, u.NeuStunde));
            var ohne = weg.ToHashSet();
            var uids = ump.Select(u => u.UnterrichtId).Concat(weg.Select(w => w.UnterrichtId)).ToHashSet();
            var unterrichte = (per.Unterrichte ?? d.Unterrichte).ToDictionary(u => u.Id);
            var kandidaten = d.Vertretungen.Where(v => v.UnterrichtId is int u && uids.Contains(u) && Uebertragbar(v) && d.PeriodeAm(v.Datum) == per)
                .OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
            foreach (var v in kandidaten)
            {
                var tag = Kalender.TagIndex(d, v.Datum);
                if (tag < 0) continue;
                var schluessel = (v.UnterrichtId!.Value, tag, v.Stunde);
                var text = $"{Format.Datum(v.Datum)} {v.Stunde}. Std. U-Nr. {v.UnterrichtId} ({Vertretungsplan.ArtText(v.Art)}{(v.VertreterId is int vt ? ", Vertreter " + ix.KLehrer(vt) : "")})";
                if (ziel.TryGetValue(schluessel, out var neu))
                {
                    var datum = v.Datum.AddDays(neu.NeuTag - tag);
                    if (!Kalender.IstUnterrichtstag(d, datum) || d.PeriodeAm(datum) != per)
                    {
                        Behalten(d, erg, v, text + $": die neue Lage am {Format.Datum(datum)} ist kein Unterrichtstag.", benutzer);
                        continue;
                    }
                    var belegt = d.Vertretungen.Where(x => x != v && x.UnterrichtId == v.UnterrichtId && x.ZeilenIndex == v.ZeilenIndex && x.Datum == datum && x.Stunde == neu.NeuStunde).ToList();
                    if (belegt.Any(Bearbeitet))
                    {
                        Behalten(d, erg, v, text + $": an der neuen Lage ({Format.Datum(datum)} {neu.NeuStunde}. Std.) ist bereits eine Vertretung geplant.", benutzer);
                        continue;
                    }
                    foreach (var b in belegt) d.Vertretungen.Remove(b);
                    tage.Add(v.Datum);
                    tage.Add(datum);
                    v.Datum = datum;
                    v.Stunde = neu.NeuStunde;
                    // Raum der Stunde an der neuen Lage (der Vertretungsraum bleibt)
                    var p = per.Plan.FirstOrDefault(x => x.UnterrichtId == v.UnterrichtId && x.Tag == neu.NeuTag && x.Stunde == neu.NeuStunde);
                    if (p != null) v.RaumId = p.RaumFuerZeile(v.ZeilenIndex);
                    if (unterrichte.TryGetValue(v.UnterrichtId.Value, out var u) && v.ZeilenIndex < u.Zeilen.Count && v.Art != VertretungsArt.Veranstaltung)
                    {
                        var z = u.Zeilen[v.ZeilenIndex];
                        v.LehrerId ??= z.LehrerId;
                        v.FachId ??= z.FachId;
                    }
                    Vertretungen.AlsGeaendertMarkieren(v, benutzer);
                    erg.Uebertragen.Add(v);
                }
                else if (ohne.Contains(schluessel) && Bearbeitet(v))
                    Behalten(d, erg, v, text + ": die Stunde ist im Stundenplan nicht mehr vorhanden.", benutzer);
            }
        }
        StandSichern(d);
        // abgeleitete Zeilen der betroffenen Tage angleichen; bearbeitete Zeilen, deren Ursache zur neuen Zeit nicht mehr besteht, melden
        var vorher = erg.Uebertragen.Where(Bearbeitet).ToList();
        foreach (var t in tage.OrderBy(x => x)) Vertretungen.Aktualisieren(d, t);
        foreach (var v in vorher.Where(v => !d.Vertretungen.Contains(v)))
            erg.Hinweise.Add($"{Format.Datum(v.Datum)} {v.Stunde}. Std. U-Nr. {v.UnterrichtId}: Vertretung entfällt – die Ursache (z. B. Absenz) besteht zur neuen Zeit nicht mehr.");
        return erg;
    }

    /// <summary>Nicht zuordenbare bearbeitete Vertretung als Vormerkung behalten (offener Hinweis in der Ablage neben dem Stundenplan).</summary>
    private static void Behalten(Schuldatei d, UmplanungsErgebnis erg, Vertretung v, string hinweis, string benutzer)
    {
        erg.Hinweise.Add(hinweis);
        if (!Bearbeitet(v)) return;
        var vm = Vertretungen.AlsVormerkung(d, v, benutzer);
        vm.Text = "Umplanung: " + hinweis + (string.IsNullOrWhiteSpace(v.Text) ? "" : $" Text: {v.Text}");
        erg.Vormerkungen.Add(vm);
    }
}
