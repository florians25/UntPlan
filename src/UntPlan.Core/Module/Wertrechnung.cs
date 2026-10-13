using UntPlan.Core.Model;

namespace UntPlan.Core.Module;

/// <summary>
/// Modul Unterrichtsplanung und Wertrechnung: Lehrer Soll- und Ist-Werte, Fach-Faktoren, befristete Unterrichte,
/// Unterrichts-Matrix, Stundentafeln, Lehrervorschlag.
/// </summary>
public static class Wertrechnung
{
    /// <summary>Wert einer Kopplungszeile: Wochenstunden × Faktor (Unterricht vor Fach), ggf. anteilig bei Befristung bzw. A/B-Woche.</summary>
    public static decimal Wert(Schuldatei d, DatenIndex ix, Unterricht u, Kopplungszeile z)
    {
        if (z.Wert.HasValue) return z.Wert.Value;
        var faktor = u.WerteFaktor ?? (z.FachId.HasValue && ix.Faecher.TryGetValue(z.FachId.Value, out var f) ? f.WerteFaktor : 1m);
        decimal anteil = u.Periodizitaet == Periodizitaet.JedeWoche ? 1m : 0.5m;
        if (u.VonDatum.HasValue || u.BisDatum.HasValue)
        {
            var von = u.VonDatum ?? d.Einstellungen.SchuljahrVon;
            var bis = u.BisDatum ?? d.Einstellungen.SchuljahrBis;
            var gesamt = Math.Max(1, d.Einstellungen.SchuljahrBis.DayNumber - d.Einstellungen.SchuljahrVon.DayNumber);
            anteil *= Math.Clamp((decimal)(bis.DayNumber - von.DayNumber) / gesamt, 0, 1);
        }
        return u.Wochenstunden * faktor * anteil;
    }

    public static decimal Ist(Schuldatei d, DatenIndex ix, Lehrer l) =>
        d.Unterrichte.Where(u => !u.Ignorieren).SelectMany(u => u.Zeilen.Where(z => z.LehrerId == l.Id).Select(z => Wert(d, ix, u, z))).Sum() + l.Wertkorrektur;

    public sealed record LehrerWerte(Lehrer Lehrer, decimal Wochensoll, decimal Ermaessigung, decimal Soll, decimal Ist, decimal Differenz, int Wochenstunden, int Verplant);

    public static List<LehrerWerte> Uebersicht(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        return d.Lehrer.Select(l =>
        {
            var ist = Ist(d, ix, l);
            var wst = d.Unterrichte.Where(u => !u.Ignorieren).Sum(u => u.Zeilen.Count(z => z.LehrerId == l.Id) > 0 ? u.Wochenstunden : 0);
            var verplant = ix.PlanVon(ElementTyp.Lehrer, l.Id).Count();
            return new LehrerWerte(l, l.Wochensoll, l.SummeErmaessigungen, l.Soll, ist, ist - l.Soll, wst, verplant);
        }).ToList();
    }

    /// <summary>Unterrichts-Matrix: Klassen × Fächer mit eingesetzten Lehrern und Stunden.</summary>
    public static Dictionary<(int Klasse, int Fach), List<(Unterricht U, int? Lehrer)>> Matrix(Schuldatei d)
    {
        var res = new Dictionary<(int, int), List<(Unterricht, int?)>>();
        foreach (var u in d.Unterrichte)
            foreach (var z in u.Zeilen.Where(z => z.FachId.HasValue))
                foreach (var k in z.KlassenIds)
                {
                    var key = (k, z.FachId!.Value);
                    if (!res.TryGetValue(key, out var l)) res[key] = l = new List<(Unterricht, int?)>();
                    l.Add((u, z.LehrerId));
                }
        return res;
    }

    /// <summary>Lehrervorschlag: geeignete Lehrer (Lehrbefähigung, freies Deputat, kennt Klasse) für eine Kopplungszeile.</summary>
    public static List<(Lehrer Lehrer, decimal Frei, bool KenntKlasse, bool Fach)> Lehrervorschlag(Schuldatei d, Unterricht u, Kopplungszeile z)
    {
        var ix = new DatenIndex(d);
        return d.Lehrer.Where(l => !l.Ignorieren)
            .Select(l => (l, Frei: l.Soll - Ist(d, ix, l), Kennt: d.Unterrichte.Any(x => x.Zeilen.Any(y => y.LehrerId == l.Id && y.KlassenIds.Intersect(z.KlassenIds).Any())),
                Fach: z.FachId.HasValue && l.FaecherIds.Contains(z.FachId.Value)))
            .OrderByDescending(x => x.Fach).ThenByDescending(x => x.Frei >= u.Wochenstunden).ThenByDescending(x => x.Kennt).ThenByDescending(x => x.Frei)
            .ToList();
    }

    /// <summary>
    /// Automatische Unterrichtszuordnung: Kopplungszeilen ohne Lehrer erhalten den geeignetsten Lehrer – Lehrbefähigung
    /// (bzw. unterrichtet das Fach bereits), freies Deputat reicht, Klassenlehrer bzw. Lehrer, der die Klasse schon kennt, bevorzugt;
    /// bei Gleichstand der Lehrer mit dem meisten freien Deputat. Große Unterrichte werden zuerst vergeben.
    /// </summary>
    public static List<(Unterricht U, int Zeile, Lehrer Lehrer)> AutomatischZuordnen(Schuldatei d, IEnumerable<Unterricht>? nur = null, bool deputatUeberschreiten = false)
    {
        var ix = new DatenIndex(d);
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren).ToList();
        var frei = lehrer.ToDictionary(l => l.Id, l => l.Soll - Ist(d, ix, l));
        var faecherVon = lehrer.ToDictionary(l => l.Id, l => l.FaecherIds.Concat(d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.LehrerId == l.Id && z.FachId.HasValue).Select(z => z.FachId!.Value)).ToHashSet());
        var klassenVon = lehrer.ToDictionary(l => l.Id, l => d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.LehrerId == l.Id).SelectMany(z => z.KlassenIds).ToHashSet());
        var ergebnis = new List<(Unterricht, int, Lehrer)>();
        var offen = (nur ?? d.Unterrichte).Where(u => !u.Ignorieren)
            .SelectMany(u => u.Zeilen.Select((z, i) => (U: u, Z: z, I: i)))
            .Where(x => x.Z.LehrerId is null && x.Z.FachId.HasValue)
            .OrderByDescending(x => Wert(d, ix, x.U, x.Z)).ToList();
        foreach (var (u, z, i) in offen)
        {
            var wert = Wert(d, ix, u, z);
            var klassenlehrer = d.Klassen.Where(k => z.KlassenIds.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds).ToHashSet();
            // gleichzeitig gekoppelte Zeilen dürfen nicht denselben Lehrer erhalten
            var belegt = u.Zeilen.Where(x => x != z && x.LehrerId.HasValue).Select(x => x.LehrerId!.Value).ToHashSet();
            var best = lehrer
                .Where(l => !belegt.Contains(l.Id) && faecherVon[l.Id].Contains(z.FachId!.Value) && (deputatUeberschreiten || frei[l.Id] >= wert))
                .OrderByDescending(l => klassenlehrer.Contains(l.Id))
                .ThenByDescending(l => klassenVon[l.Id].Overlaps(z.KlassenIds))
                .ThenByDescending(l => frei[l.Id])
                .ThenBy(l => l.Kurzname, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best is null) continue;
            z.LehrerId = best.Id;
            if (z.StammraumId is null && z.KlassenIds.Count == 0) z.StammraumId = best.StammraumId;
            frei[best.Id] -= wert;
            klassenVon[best.Id].UnionWith(z.KlassenIds);
            ergebnis.Add((u, i, best));
        }
        return ergebnis;
    }

    public sealed record StundentafelAbgleich(Klasse Klasse, Fach Fach, decimal Soll, decimal Ist);

    public static List<StundentafelAbgleich> Stundentafel(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var res = new List<StundentafelAbgleich>();
        foreach (var k in d.Klassen)
        {
            var zeilen = d.Stundentafel.Where(z => z.Jahrgang == k.Jahrgang).ToList();
            if (zeilen.Count == 0) continue;
            var faecher = zeilen.Select(z => z.FachId).Concat(ix.UnterrichteVon(ElementTyp.Klasse, k.Id).SelectMany(u => u.FachIds)).Distinct();
            foreach (var fid in faecher)
            {
                if (!ix.Faecher.TryGetValue(fid, out var f) || f.Bereitschaft) continue;
                var soll = zeilen.FirstOrDefault(z => z.FachId == fid)?.Stunden ?? 0;
                var ist = (decimal)ix.UnterrichteVon(ElementTyp.Klasse, k.Id).Where(u => u.HauptfachId == fid).Select(u => u.Wochenstunden).DefaultIfEmpty(0).Max();
                if (soll != ist) res.Add(new StundentafelAbgleich(k, f, soll, ist));
            }
        }
        return res;
    }
}
