using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>Nachschlagestrukturen für schnellen Zugriff auf Elemente und Unterrichte.</summary>
public sealed class DatenIndex
{
    private readonly Schuldatei _d;
    public Dictionary<int, Klasse> Klassen { get; }
    public Dictionary<int, Lehrer> Lehrer { get; }
    public Dictionary<int, Raum> Raeume { get; }
    public Dictionary<int, Fach> Faecher { get; }
    public Dictionary<int, Raumgruppe> Raumgruppen { get; }
    public Dictionary<int, Gang> Gaenge { get; }
    public Dictionary<int, Unterricht> Unterrichte { get; }
    public Dictionary<int, Abwesenheitsgrund> Gruende { get; }
    public Dictionary<int, List<Planstunde>> PlanJeUnterricht { get; }

    [ThreadStatic] private static DatenIndex? _gemeinsam;

    /// <summary>
    /// Gemeinsamer Index für eine reine Lese-Berechnung (Vorschlag, Listen, Pläne): innerhalb des Bereichs liefert <see cref="Fuer"/>
    /// denselben Index statt ihn je Aufruf neu aufzubauen. Im Bereich dürfen die Stammdaten und der Stundenplan nicht verändert werden.
    /// </summary>
    public static IndexBereich Gemeinsam(Schuldatei d, DatenIndex? vorhanden = null)
    {
        var alt = _gemeinsam;
        if (_gemeinsam is null || !ReferenceEquals(_gemeinsam._d, d)) _gemeinsam = vorhanden != null && ReferenceEquals(vorhanden._d, d) ? vorhanden : new DatenIndex(d);
        return new IndexBereich(alt);
    }

    /// <summary>Gemeinsamer Index, falls gerade ein Lese-Bereich für diese Datei läuft.</summary>
    internal static DatenIndex? GemeinsamFuer(Schuldatei d) => _gemeinsam is { } g && ReferenceEquals(g._d, d) ? g : null;

    /// <summary>Im Lese-Bereich: bereits berechnete Tagespläne (die Daten ändern sich im Bereich nicht).</summary>
    internal Dictionary<DateOnly, Vertretungsplanung.Tagesplan> Tagesplaene { get; } = new();

    private Dictionary<int, List<Vertretung>>? _vertretungenJeLehrer;

    /// <summary>Im Lese-Bereich: Vertretungszeilen je beteiligter Lehrkraft (Lehrer bzw. Vertreter), für Zählungen.</summary>
    internal List<Vertretung> VertretungenVon(int lehrerId)
    {
        if (_vertretungenJeLehrer is null)
        {
            _vertretungenJeLehrer = new();
            foreach (var v in _d.Vertretungen)
            {
                if (v.LehrerId is int l) Hinzu(l, v);
                if (v.VertreterId is int vt && vt != v.LehrerId) Hinzu(vt, v);
            }
        }
        return _vertretungenJeLehrer.TryGetValue(lehrerId, out var liste) ? liste : new List<Vertretung>();

        void Hinzu(int id, Vertretung v)
        {
            if (!_vertretungenJeLehrer!.TryGetValue(id, out var x)) _vertretungenJeLehrer[id] = x = new();
            x.Add(v);
        }
    }

    /// <summary>Index der Datei: im Bereich von <see cref="Gemeinsam"/> der gemeinsame, sonst neu.</summary>
    public static DatenIndex Fuer(Schuldatei d) => _gemeinsam is { } g && ReferenceEquals(g._d, d) ? g : new DatenIndex(d);

    public readonly struct IndexBereich : IDisposable
    {
        private readonly DatenIndex? _alt;
        internal IndexBereich(DatenIndex? alt) { _alt = alt; }
        public void Dispose() => _gemeinsam = _alt;
    }

    public DatenIndex(Schuldatei d)
    {
        using var _m = Messung.Start("DatenIndex");
        _d = d;
        Klassen = d.Klassen.ToDictionary(x => x.Id);
        Lehrer = d.Lehrer.ToDictionary(x => x.Id);
        Raeume = d.Raeume.ToDictionary(x => x.Id);
        Faecher = d.Faecher.ToDictionary(x => x.Id);
        Raumgruppen = d.Raumgruppen.ToDictionary(x => x.Id);
        Gaenge = d.Gaenge.ToDictionary(x => x.Id);
        Unterrichte = d.Unterrichte.ToDictionary(x => x.Id);
        Gruende = d.Abwesenheitsgruende.ToDictionary(x => x.Id);
        PlanJeUnterricht = d.Stundenplan.GroupBy(p => p.UnterrichtId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public Element? Element(ElementTyp typ, int id) => typ switch
    {
        ElementTyp.Klasse => Klassen.GetValueOrDefault(id),
        ElementTyp.Lehrer => Lehrer.GetValueOrDefault(id),
        ElementTyp.Raum => Raeume.GetValueOrDefault(id),
        ElementTyp.Fach => Faecher.GetValueOrDefault(id),
        ElementTyp.Raumgruppe => Raumgruppen.GetValueOrDefault(id),
        ElementTyp.Gang => Gaenge.GetValueOrDefault(id),
        _ => _d.AlleElemente(typ).FirstOrDefault(e => e.Id == id),
    };

    public string K(ElementTyp typ, int? id) => id.HasValue ? Element(typ, id.Value)?.Kurzname ?? "?" : "";
    public string KLehrer(int? id) => K(ElementTyp.Lehrer, id);
    public string KFach(int? id) => K(ElementTyp.Fach, id);
    public string KRaum(int? id) => K(ElementTyp.Raum, id);
    public string KLehrer(int id) => K(ElementTyp.Lehrer, id);
    public string KRaum(int id) => K(ElementTyp.Raum, id);
    public string KKlassen(IEnumerable<int> ids) => string.Join(",", ids.Select(i => Klassen.GetValueOrDefault(i)?.Kurzname ?? "?"));

    public int NichtVerplant(Unterricht u) => Math.Max(0, u.Wochenstunden - (PlanJeUnterricht.TryGetValue(u.Id, out var l) ? l.Count : 0));
    public int Verplant(Unterricht u) => PlanJeUnterricht.TryGetValue(u.Id, out var l) ? l.Count : 0;

    public IEnumerable<Unterricht> UnterrichteVon(ElementTyp typ, int id) => typ switch
    {
        ElementTyp.Klasse => _d.Unterrichte.Where(u => u.Zeilen.Any(z => z.KlassenIds.Contains(id))),
        ElementTyp.Lehrer => _d.Unterrichte.Where(u => u.Zeilen.Any(z => z.LehrerId == id)),
        ElementTyp.Fach => _d.Unterrichte.Where(u => u.Zeilen.Any(z => z.FachId == id)),
        ElementTyp.Raum => _d.Unterrichte.Where(u => u.Zeilen.Any(z => z.FachraumId == id || z.StammraumId == id)),
        _ => Enumerable.Empty<Unterricht>(),
    };

    /// <summary>Planstunden, an denen ein Element beteiligt ist (Raum: tatsächlich verplanter Raum).</summary>
    public IEnumerable<Planstunde> PlanVon(ElementTyp typ, int id)
    {
        foreach (var p in _d.Stundenplan)
        {
            if (!Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            var dabei = typ switch
            {
                ElementTyp.Klasse => u.Zeilen.Any(z => z.KlassenIds.Contains(id)),
                ElementTyp.Lehrer => u.Zeilen.Any(z => z.LehrerId == id),
                ElementTyp.Fach => u.Zeilen.Any(z => z.FachId == id),
                ElementTyp.Raum => p.Raeume.Contains(id),
                _ => false,
            };
            if (dabei) yield return p;
        }
    }

    /// <summary>Räume einer Raumgruppe bzw. der Ausweichraum-Ring eines Raumes (beginnend mit dem Raum selbst).</summary>
    public List<int> RaumKandidaten(Kopplungszeile z)
    {
        var liste = new List<int>();
        if (z.Wunschraum is int r)
        {
            var aktuell = r;
            var guard = 0;
            while (Raeume.ContainsKey(aktuell) && !liste.Contains(aktuell) && guard++ < 100)
            {
                liste.Add(aktuell);
                var aus = Raeume[aktuell].AusweichraumId;
                if (aus is null) break;
                aktuell = aus.Value;
            }
        }
        else if (z.Wunschraumgruppe is int g && Raumgruppen.TryGetValue(g, out var grp))
        {
            liste.AddRange(grp.RaumIds.Where(Raeume.ContainsKey));
        }
        return liste;
    }

    public int Raumgewicht(Kopplungszeile z)
    {
        if (z.Wunschraum is int r && Raeume.TryGetValue(r, out var raum)) return raum.Raumgewicht;
        if (z.Wunschraumgruppe is int g && Raumgruppen.TryGetValue(g, out var grp))
            return grp.RaumIds.Select(i => Raeume.GetValueOrDefault(i)?.Raumgewicht ?? 0).DefaultIfEmpty(0).Max();
        return 0;
    }

    public string UnterrichtText(Unterricht u)
    {
        var z = u.Zeilen.FirstOrDefault();
        return z is null ? $"U{u.Id}" : $"{KFach(z.FachId)} {KKlassen(u.KlassenIds)} {string.Join(",", u.LehrerIds.Select(KLehrer))}";
    }
}
