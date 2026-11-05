using System.Text.Json;
using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>Zeile des Unterrichtsvergleichs: ein Unterricht in mehreren Perioden (null = in der Periode nicht vorhanden).</summary>
public sealed record VergleichsZeile(int UnterrichtId, IReadOnlyList<string?> Texte, bool Unterschiedlich);

/// <summary>
/// Funktionen des Unterrichtsfensters: Koppeln, Unterrichtsfolgen in Kopplungen umwandeln, Unterricht als Kurs übernehmen,
/// erweitertes Einfügen (mit Stundenplan), Unterrichtsvergleich zwischen Perioden, Filter „Eine Woche“ und vererbte Kennzeichen.
/// </summary>
public static class Unterrichtswerkzeuge
{
    /// <summary>
    /// Koppeln: die Kopplungszeilen der weiteren Unterrichte kommen zum Zielunterricht, die weiteren Unterrichte werden gelöscht
    /// (ihre verplanten Stunden entfallen). Zeilen-Unterrichtsgruppen übernehmen ggf. die Unterrichtsgruppe des Quellunterrichts.
    /// </summary>
    public static void Koppeln(Schuldatei d, int zielId, IEnumerable<int> weitere, bool ugruppeInZeile = false)
    {
        var u = d.Unterrichte.First(x => x.Id == zielId);
        foreach (var id in weitere.Where(i => i != zielId).Distinct().ToList())
        {
            var w = d.Unterrichte.FirstOrDefault(x => x.Id == id);
            if (w is null) continue;
            foreach (var z in w.Zeilen)
            {
                var k = z.Kopie();
                if (ugruppeInZeile && k.UGruppeId is null) k.UGruppeId = w.UGruppeId;
                u.Zeilen.Add(k);
                foreach (var p in d.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id && !(p.Raeume.Count == 1 && u.Zeilen.Count > 2))) p.Raeume.Add(null);
            }
            Stammdaten.UnterrichtLoeschen(d, id);
        }
        if (ugruppeInZeile && u.UGruppeId is int g)
        {
            // die eigene Unterrichtsgruppe des Ziels gilt jetzt nur für dessen ursprüngliche Zeilen
            foreach (var z in u.Zeilen.Where(z => z.UGruppeId is null)) z.UGruppeId = g;
            u.UGruppeId = null;
        }
    }

    /// <summary>„Unterrichtsfolgen in Kopplungen umwandeln“: Folgen der Art „Gleichzeitiger Unterricht“ werden zu Kopplungen.</summary>
    public static int GleichzeitigeInKopplungen(Schuldatei d)
    {
        var n = 0;
        foreach (var f in d.Unterrichtsfolgen.Where(f => f.Art == UnterrichtsfolgeArt.Gleichzeitig).ToList())
        {
            var ids = f.UnterrichtIds.Where(id => d.Unterrichte.Any(u => u.Id == id)).Distinct().ToList();
            d.Unterrichtsfolgen.Remove(f);
            if (ids.Count < 2) continue;
            var mitUGruppe = ids.Any(id => d.Unterrichte.First(u => u.Id == id).UGruppeId.HasValue);
            Koppeln(d, ids[0], ids.Skip(1), mitUGruppe);
            n++;
        }
        return n;
    }

    /// <summary>
    /// Unterricht als Kurs übernehmen (Kursplanung): Kursname aus Fach und Klasse(n), sofern noch keiner vergeben ist; die Schüler der
    /// beteiligten Klassen werden in den Kurs eingetragen. Ergebnis: Anzahl neu eingetragener Schüler.
    /// </summary>
    public static int AlsKursUebernehmen(Schuldatei d, Unterricht u)
    {
        var ix = new DatenIndex(d);
        if (string.IsNullOrWhiteSpace(u.Kursname))
        {
            var basis = $"{ix.KFach(u.HauptfachId)}-{string.Join("", u.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)))}".Trim('-');
            if (basis.Length == 0) basis = $"Kurs{u.Id}";
            var name = basis;
            for (var i = 2; d.Unterrichte.Any(x => x.Id != u.Id && x.Kursname.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = basis + i;
            u.Kursname = name;
        }
        var klassen = u.KlassenIds.ToHashSet();
        var n = 0;
        foreach (var s in d.Schueler.Where(s => s.KlasseId is int k && klassen.Contains(k)))
            if (!s.KurswahlIds.Contains(u.Id)) { s.KurswahlIds.Add(u.Id); n++; }
        if (u.MaxSchueler == 0) u.MaxSchueler = d.Schueler.Count(s => s.KurswahlIds.Contains(u.Id));
        return n;
    }

    /// <summary>
    /// Erweitertes Einfügen: Kopien der Unterrichte; das Ausgangselement (Klasse bzw. Lehrer) wird durch das Zielelement ersetzt
    /// (bei Klassen inkl. Stammraum). Mit <paramref name="mitStundenplan"/> werden auch die verplanten Stunden übernommen, soweit
    /// das Zielelement zu diesen Zeiten frei ist (sonst bleibt die Stunde unverplant). Ergebnis: neue U-Nummern.
    /// </summary>
    public static List<int> ErweitertesEinfuegen(Schuldatei d, IEnumerable<int> unterrichtIds, ElementTyp typ, int? vonId, int? zielId,
        bool mitStundenplan, IReadOnlyDictionary<int, Unterricht>? vorlagen = null)
    {
        var neue = new List<int>();
        foreach (var id in unterrichtIds.Distinct().ToList())
        {
            var vorlage = d.Unterrichte.FirstOrDefault(u => u.Id == id) ?? vorlagen?.GetValueOrDefault(id);
            if (vorlage is null) continue;
            var u = vorlage.Kopie(d.NeueId());
            if (vonId is int q && zielId is int z && q != z)
                foreach (var zeile in u.Zeilen)
                {
                    if (typ == ElementTyp.Klasse && zeile.KlassenIds.Contains(q))
                    {
                        zeile.KlassenIds = zeile.KlassenIds.Select(k => k == q ? z : k).Distinct().ToList();
                        if (d.Klassen.FirstOrDefault(k => k.Id == z)?.StammraumId is int sr) { zeile.StammraumId = sr; zeile.StammraumgruppeId = null; }
                    }
                    else if (typ == ElementTyp.Lehrer && zeile.LehrerId == q) zeile.LehrerId = z;
                }
            d.Unterrichte.Add(u);
            neue.Add(u.Id);
            if (!mitStundenplan) continue;
            var ix = new DatenIndex(d);
            foreach (var p in d.Stundenplan.Where(p => p.UnterrichtId == vorlage.Id).ToList())
            {
                if (Planungsdienst.Hindernisse(d, ix, u, p.Tag, p.Stunde).Count > 0) continue;
                var raeume = Planungsdienst.RaeumeSuchen(d, ix, u, p.Tag, p.Stunde);
                d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = p.Tag, Stunde = p.Stunde, Raeume = raeume });
            }
        }
        return neue;
    }

    /// <summary>Vergleichstext eines Unterrichts (Wst, Kopplungszeilen, Doppelstunden, Block, Kennzeichen).</summary>
    public static string VergleichsText(DatenIndex ix, Unterricht u) =>
        $"{u.Wochenstunden} Wst; " + string.Join(" | ", u.Zeilen.Select(z => $"{ix.KLehrer(z.LehrerId)} {ix.KFach(z.FachId)} {ix.KKlassen(z.KlassenIds)} {ix.KRaum(z.Wunschraum)}".Trim())) +
        (u.Doppelstunden.IstLeer ? "" : $"; Dopp. {u.Doppelstunden}") + (u.Block.Length > 0 ? $"; Block {u.Block}" : "") + (u.Kennzeichen.Length > 0 ? $"; {u.Kennzeichen}" : "");

    /// <summary>
    /// Unterrichtsvergleich: alle Unterrichte der gewählten Perioden mit ihrem Stand je Periode; <paramref name="nurUnterschiede"/>
    /// blendet Unterrichte aus, die in allen Perioden gleich sind.
    /// </summary>
    public static List<VergleichsZeile> Vergleichen(Schuldatei d, IReadOnlyList<Periode> perioden, bool nurUnterschiede)
    {
        var ix = new DatenIndex(d);
        var ids = perioden.SelectMany(p => p.Unterrichte ?? new()).Select(u => u.Id).Distinct().OrderBy(x => x);
        var res = new List<VergleichsZeile>();
        foreach (var id in ids)
        {
            var texte = perioden.Select(p => (p.Unterrichte ?? new()).FirstOrDefault(u => u.Id == id) is { } u ? VergleichsText(ix, u) : null).ToList();
            var json = perioden.Select(p => (p.Unterrichte ?? new()).FirstOrDefault(u => u.Id == id) is { } u ? JsonSerializer.Serialize(u) : null).ToList();
            var verschieden = json.Distinct().Count() > 1;
            if (nurUnterschiede && !verschieden) continue;
            res.Add(new VergleichsZeile(id, texte, verschieden));
        }
        return res;
    }

    /// <summary>„Eine Woche“: findet der Unterricht in der Woche ab <paramref name="montag"/> statt (Zeitbereich, Wochenperiodizität)?</summary>
    public static bool FindetInWocheStatt(Schuldatei d, Unterricht u, DateOnly montag)
    {
        var maske = Kalender.WochenMaske(d, u);
        var tage = Math.Max(1, d.Zeitraster.TageProWoche);
        for (var i = 0; i < tage; i++)
        {
            var datum = montag.AddDays(d.Zeitraster.ErsterSchultag + i);
            if (u.VonDatum is DateOnly von && datum < von) continue;
            if (u.BisDatum is DateOnly bis && datum > bis) continue;
            if ((maske & (1 << Kalender.WochenIndex(d, datum))) == 0) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Vererbte Kennzeichen: Kennzeichen der beteiligten Stammdatenelemente (Fächer, Klassen, Lehrer; fixiert/ignoriert und Zeitwünsche),
    /// in der Spalte „Kennzeichen“ in Klammern angezeigt.
    /// </summary>
    public static string VererbteKennzeichen(DatenIndex ix, Unterricht u)
    {
        var zeichen = new List<char>();
        void Add(string s) { foreach (var c in s) if (!zeichen.Contains(c)) zeichen.Add(c); }
        foreach (var f in u.FachIds.Select(id => ix.Faecher.GetValueOrDefault(id)).Where(f => f != null)) Add((f!.Fix ? "X" : "") + (f.Ignorieren ? "i" : "") + f.Kennzeichen + (f.Zeitwuensche.Leer ? "" : "Z"));
        foreach (var k in u.KlassenIds.Select(id => ix.Klassen.GetValueOrDefault(id)).Where(k => k != null)) Add((k!.Fix ? "X" : "") + (k.Ignorieren ? "i" : "") + (k.Zeitwuensche.Leer ? "" : "Z"));
        foreach (var l in u.LehrerIds.Select(id => ix.Lehrer.GetValueOrDefault(id)).Where(l => l != null)) Add((l!.Fix ? "X" : "") + (l.Ignorieren ? "i" : "") + (l.Zeitwuensche.Leer ? "" : "Z"));
        var eigen = u.Kennzeichen;
        var vererbt = new string(zeichen.Where(c => !eigen.Contains(c)).ToArray());
        return vererbt;
    }

    /// <summary>Spalte „Kennzeichen“: eigene Kennzeichen, optional mit vererbten in Klammern, z. B. „FZ (HX)“.</summary>
    public static string KennzeichenAnzeige(DatenIndex ix, Unterricht u, bool vererbte)
    {
        var eigen = u.Kennzeichen;
        if (!vererbte) return eigen;
        var v = VererbteKennzeichen(ix, u);
        return v.Length == 0 ? eigen : $"{eigen} ({v})".Trim();
    }
}
