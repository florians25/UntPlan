using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Regulärer Unterricht einer Kopplungszeile an einem Tag (laut Periodenstundenplan).</summary>
public sealed record RegelStunde(Planstunde Planstunde, Unterricht Unterricht, int ZeilenIndex)
{
    public int Stunde => Planstunde.Stunde;
    public Kopplungszeile Zeile => Unterricht.Zeilen[ZeilenIndex];
    public int? LehrerId => Zeile.LehrerId;
    public IReadOnlyList<int> KlassenIds => Zeile.KlassenIds;
    public int? FachId => Zeile.FachId;
    public int? RaumId => Planstunde.RaumFuerZeile(ZeilenIndex);
}

public enum TagesStatus
{
    Regulaer,
    Vertreten,
    Offen,
    Entfall,
    Frei,
    Zusaetzlich,
}

/// <summary>Eine Stunde im tagesaktuellen Stundenplan (nach Anwendung der Vertretungen).</summary>
public sealed class TagesEintrag
{
    public int Stunde { get; init; }
    public RegelStunde? Regel { get; init; }
    public Vertretung? Vertretung { get; set; }
    public TagesStatus Status { get; set; }
    public List<int> LehrerIds { get; set; } = new();
    public List<int> KlassenIds { get; set; } = new();
    public int? FachId { get; set; }
    public List<int> RaumIds { get; set; } = new();
    public Unterricht? Unterricht => Regel?.Unterricht;
    /// <summary>Findet tatsächlich statt (bindet Lehrer, Klassen und Räume).</summary>
    public bool Findetstatt => Status is TagesStatus.Regulaer or TagesStatus.Vertreten or TagesStatus.Offen or TagesStatus.Zusaetzlich;
}

/// <summary>Tagesaktueller Stundenplan: regulärer Unterricht der an diesem Tag gültigen Periode plus Vertretungen.</summary>
public sealed class Tagesplan
{
    public DateOnly Datum { get; }
    public Periode Periode { get; }
    public int Tag { get; }
    public List<RegelStunde> Regel { get; } = new();
    public List<TagesEintrag> Eintraege { get; } = new();
    public Dictionary<int, Unterricht> Unterrichte { get; }

    private Tagesplan(DateOnly datum, Periode periode, int tag, Dictionary<int, Unterricht> unterrichte)
    {
        Datum = datum;
        Periode = periode;
        Tag = tag;
        Unterrichte = unterrichte;
    }

    /// <summary>Nur der reguläre Unterricht (ohne Vertretungen).</summary>
    public static List<RegelStunde> Regulaer(Schuldatei d, DateOnly datum)
    {
        var res = new List<RegelStunde>();
        if (!Kalender.IstUnterrichtstag(d, datum)) return res;
        var tag = Kalender.TagIndex(d, datum);
        var periode = d.PeriodeAm(datum);
        var unterrichte = (periode.Unterrichte ?? d.Unterrichte).ToDictionary(u => u.Id);
        var kalender = Kalender.KalendertagAm(d, datum);
        foreach (var p in periode.Plan)
        {
            if (p.Tag != tag || !unterrichte.TryGetValue(p.UnterrichtId, out var u) || !Kalender.UnterrichtAktiv(d, u, datum)) continue;
            if (kalender != null && kalender.StundeFrei(p.Stunde)) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
                if (Kalender.ZeileAktiv(d, u.Zeilen[i], datum))
                    res.Add(new RegelStunde(p, u, i));
        }
        return res;
    }

    public static Tagesplan Berechnen(Schuldatei d, DateOnly datum)
    {
        var periode = d.PeriodeAm(datum);
        var unterrichte = (periode.Unterrichte ?? d.Unterrichte).ToDictionary(u => u.Id);
        var tp = new Tagesplan(datum, periode, Kalender.TagIndex(d, datum), unterrichte);
        tp.Regel.AddRange(Regulaer(d, datum));
        var vertretungen = d.Vertretungen.Where(v => v.Datum == datum && v.Art != VertretungsArt.Pausenaufsicht).ToList();
        var zuRegel = new Dictionary<(int, int, int), List<Vertretung>>();
        var regelSchluessel = tp.Regel.Select(r => (r.Unterricht.Id, r.ZeilenIndex, r.Stunde)).ToHashSet();
        var zusaetzlich = new List<Vertretung>();
        foreach (var v in vertretungen)
        {
            // Sondereinsatz zu einem regulären Unterricht („Stunde ändern“, andere Lehrkraft) ersetzt diesen Unterricht, ebenso eine
            // Prüfung, die den Unterricht ihrer Aufsicht ersetzt (Prüfungszeile mit Unterricht)
            // wegen abwesender Klasse entfallene Zeile: zugeordnet wird nach der ursprünglichen Art
            var art = v.ArtVorKlassenabwesenheit ?? v.Art;
            if (v.UnterrichtId is int uid && art is not (VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.SondereinsatzVormerkung
                    or VertretungsArt.Veranstaltung) && !(art == VertretungsArt.Klausur && v.Quelle != VertretungsQuelle.Klausur) && !(art == VertretungsArt.Sondereinsatz && v.LehrerId is null) && regelSchluessel.Contains((uid, v.ZeilenIndex, v.Stunde)))
            {
                var key = (uid, v.ZeilenIndex, v.Stunde);
                if (!zuRegel.TryGetValue(key, out var l)) zuRegel[key] = l = new();
                l.Add(v);
            }
            else zusaetzlich.Add(v);
        }
        foreach (var r in tp.Regel)
        {
            var e = new TagesEintrag
            {
                Stunde = r.Stunde, Regel = r, Status = TagesStatus.Regulaer, FachId = r.FachId, KlassenIds = r.KlassenIds.ToList(),
                LehrerIds = r.LehrerId is int l ? new List<int> { l } : new(), RaumIds = r.RaumId is int rm ? new List<int> { rm } : new(),
            };
            if (zuRegel.TryGetValue((r.Unterricht.Id, r.ZeilenIndex, r.Stunde), out var vs))
            {
                // Mehrere Zeilen (Vertretung teilen): erste bestimmt den Status, weitere werden zusätzlich eingetragen
                var v = vs[0];
                Anwenden(e, v);
                foreach (var weitere in vs.Skip(1))
                {
                    var e2 = new TagesEintrag { Stunde = r.Stunde, Regel = r, FachId = r.FachId, KlassenIds = r.KlassenIds.ToList(), RaumIds = e.RaumIds.ToList() };
                    Anwenden(e2, weitere);
                    tp.Eintraege.Add(e2);
                }
            }
            tp.Eintraege.Add(e);
        }
        foreach (var v in zusaetzlich)
        {
            // zusätzlicher Unterricht, der wegen abwesender Klasse entfällt, bleibt als Entfall sichtbar (z. B. durchgestrichen im Web)
            var entfallKlasse = v.IstEntfall && v.ArtVorKlassenabwesenheit.HasValue;
            if (v.IstEntfall && !entfallKlasse) continue;
            tp.Unterrichte.TryGetValue(v.UnterrichtId ?? -1, out var u);
            var regel = u is null ? null : new RegelStunde(new Planstunde { UnterrichtId = u.Id, Tag = tp.Tag, Stunde = v.Stunde }, u, Math.Min(v.ZeilenIndex, Math.Max(0, u.Zeilen.Count - 1)));
            var e = new TagesEintrag
            {
                Stunde = v.Stunde, Regel = u is null || u.Zeilen.Count == 0 ? null : regel, Vertretung = v, Status = entfallKlasse ? TagesStatus.Entfall : TagesStatus.Zusaetzlich,
                FachId = v.VertretungsfachId ?? v.FachId, KlassenIds = v.NeueKlassenIds.ToList(),
                LehrerIds = v.VertreterId is int l ? new List<int> { l } : new(),
                RaumIds = (v.VertretungsraumId ?? v.RaumId) is int rm ? new List<int> { rm } : new(),
            };
            e.RaumIds.AddRange(v.ZusatzraumIds);
            tp.Eintraege.Add(e);
        }
        return tp;
    }

    private static void Anwenden(TagesEintrag e, Vertretung v)
    {
        e.Vertretung = v;
        if (v.IstEntfall || v.Art is VertretungsArt.Vormerkung)
        {
            e.Status = TagesStatus.Entfall;
            return;
        }
        if (v.VertretungsKlassenIds != null) e.KlassenIds = v.VertretungsKlassenIds.ToList();
        if (v.VertretungsfachId.HasValue) e.FachId = v.VertretungsfachId;
        if (v.VertretungsraumId.HasValue) e.RaumIds = new List<int> { v.VertretungsraumId.Value };
        // Raum verdrängt bzw. Raum gelöscht: die Stunde belegt keinen Raum mehr
        else if (v.RaumVerdraengt || (v.Art == VertretungsArt.Raumvertretung && v.Quelle == VertretungsQuelle.Manuell && v.ZusatzraumIds.Count == 0)) e.RaumIds = new List<int>();
        e.RaumIds.AddRange(v.ZusatzraumIds);
        switch (v.Art)
        {
            case VertretungsArt.Raumvertretung:
            case VertretungsArt.UnterrichtGeaendert:
                if (v.VertreterId.HasValue) e.LehrerIds = new List<int> { v.VertreterId.Value };
                e.Status = TagesStatus.Vertreten;
                break;
            case VertretungsArt.VertretungOhneLehrer:
            case VertretungsArt.EigenverantwortlichesArbeiten:
                e.LehrerIds = new();
                e.Status = TagesStatus.Vertreten;
                break;
            default:
                if (v.VertreterId is int l)
                {
                    e.LehrerIds = new List<int> { l };
                    e.Status = TagesStatus.Vertreten;
                }
                else
                {
                    e.LehrerIds = new();
                    e.Status = TagesStatus.Offen;
                }
                break;
        }
    }

    private Dictionary<int, List<TagesEintrag>>? _nachStunde;
    private int _indexStand = -1;

    /// <summary>Einträge einer Stunde (Index je Stunde, wird bei geänderter Eintragszahl neu aufgebaut).</summary>
    public IEnumerable<TagesEintrag> Stunde(int stunde)
    {
        if (_nachStunde is null || _indexStand != Eintraege.Count)
        {
            _nachStunde = new Dictionary<int, List<TagesEintrag>>();
            foreach (var e in Eintraege)
            {
                if (!_nachStunde.TryGetValue(e.Stunde, out var l)) _nachStunde[e.Stunde] = l = new List<TagesEintrag>();
                l.Add(e);
            }
            _indexStand = Eintraege.Count;
        }
        return _nachStunde.TryGetValue(stunde, out var liste) ? liste : Array.Empty<TagesEintrag>();
    }

    /// <summary>Stunden, in denen der Lehrer tatsächlich unterrichtet (ohne Bereitschaft).</summary>
    public IEnumerable<TagesEintrag> VonLehrer(int lehrerId) => Eintraege.Where(e => e.Findetstatt && e.LehrerIds.Contains(lehrerId));

    public IEnumerable<TagesEintrag> VonKlasse(int klasseId) => Eintraege.Where(e => e.Findetstatt && e.KlassenIds.Contains(klasseId));

    public IEnumerable<RegelStunde> RegelVonLehrer(int lehrerId) => Regel.Where(r => r.LehrerId == lehrerId);
}

/// <summary>Abwesenheiten durch Absenzen und Veranstaltungen.</summary>
public static class Absenzen
{
    public static Abwesenheit? Lehrer(Schuldatei d, int lehrerId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.Where(a => a.Deckt(datum, stunde) &&
            ((a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrerId) || (a.Art == AbwesenheitsArt.Klasse && a.BegleitLehrerIds.Contains(lehrerId))))
            .OrderByDescending(a => a.Von).FirstOrDefault();

    public static Abwesenheit? Klasse(Schuldatei d, int klasseId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.FirstOrDefault(a => a.Art == AbwesenheitsArt.Klasse && a.ElementId == klasseId && a.Deckt(datum, stunde));

    public static Abwesenheit? Raum(Schuldatei d, int raumId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.FirstOrDefault(a => a.Art == AbwesenheitsArt.Raum && a.ElementId == raumId && a.Deckt(datum, stunde));

    public static Veranstaltung? VeranstaltungLehrer(Schuldatei d, int lehrerId, DateOnly datum, int stunde) =>
        d.Veranstaltungen.FirstOrDefault(v => v.Deckt(datum, stunde) && v.NimmtTeil(lehrerId, datum));

    public static Veranstaltung? VeranstaltungKlasse(Schuldatei d, int klasseId, DateOnly datum, int stunde) =>
        d.Veranstaltungen.FirstOrDefault(v => v.KlassenIds.Contains(klasseId) && v.Deckt(datum, stunde));

    public static Veranstaltung? VeranstaltungRaum(Schuldatei d, int raumId, DateOnly datum, int stunde) =>
        d.Veranstaltungen.FirstOrDefault(v => v.RaumIds.Contains(raumId) && v.Deckt(datum, stunde));

    /// <summary>Ist der Lehrer abwesend (Absenz oder Veranstaltung)?</summary>
    public static bool LehrerAbwesend(Schuldatei d, int lehrerId, DateOnly datum, int stunde) =>
        Lehrer(d, lehrerId, datum, stunde) != null || VeranstaltungLehrer(d, lehrerId, datum, stunde) != null;

    public static bool KlasseAbwesend(Schuldatei d, int klasseId, DateOnly datum, int stunde) =>
        Klasse(d, klasseId, datum, stunde) != null || VeranstaltungKlasse(d, klasseId, datum, stunde) != null;

    public static bool RaumGesperrt(Schuldatei d, int raumId, DateOnly datum, int stunde) =>
        Raum(d, raumId, datum, stunde) != null || VeranstaltungRaum(d, raumId, datum, stunde) != null;
}
