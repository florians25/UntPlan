using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>Anlegen und Löschen von Stammdaten-Elementen inkl. Bereinigung aller Verweise.</summary>
public static class Stammdaten
{
    public static Element Neu(Schuldatei d, ElementTyp typ, string kurzname)
    {
        kurzname = kurzname.Trim();
        if (kurzname.Length == 0) throw new InvalidOperationException("Bitte einen Kurznamen eingeben.");
        if (d.AlleElemente(typ).Any(e => e.Kurzname.Equals(kurzname, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Der Kurzname \"{kurzname}\" ist bereits vergeben.");
        var sort = d.AlleElemente(typ).Select(e => e.Sortierung).DefaultIfEmpty(-1).Max() + 1;
        Element e = typ switch
        {
            ElementTyp.Klasse => new Klasse { Jahrgang = new string(kurzname.TakeWhile(char.IsDigit).ToArray()) },
            ElementTyp.Lehrer => new Lehrer { Wochensoll = Nrw.Pflichtstunden.GetValueOrDefault(d.Einstellungen.Schulform, 25.5m) },
            ElementTyp.Raum => new Raum(),
            ElementTyp.Fach => new Fach(),
            ElementTyp.Raumgruppe => new Raumgruppe(),
            ElementTyp.Gang => new Gang(),
            ElementTyp.Abteilung => new Abteilung(),
            ElementTyp.Abwesenheitsgrund => new Abwesenheitsgrund(),
            ElementTyp.Schueler => new Schueler(),
            _ => throw new ArgumentOutOfRangeException(nameof(typ)),
        };
        e.Id = d.NeueId();
        e.Kurzname = kurzname;
        e.Sortierung = sort;
        switch (e)
        {
            case Klasse k: d.Klassen.Add(k); break;
            case Lehrer l: d.Lehrer.Add(l); break;
            case Raum r: d.Raeume.Add(r); break;
            case Fach f: d.Faecher.Add(f); break;
            case Raumgruppe g: d.Raumgruppen.Add(g); break;
            case Gang g: d.Gaenge.Add(g); break;
            case Abteilung a: d.Abteilungen.Add(a); break;
            case Abwesenheitsgrund a: d.Abwesenheitsgruende.Add(a); break;
            case Schueler s: d.Schueler.Add(s); break;
        }
        return e;
    }

    public static void Umbenennen(Schuldatei d, Element e, string kurzname)
    {
        kurzname = kurzname.Trim();
        if (kurzname.Length == 0) throw new InvalidOperationException("Der Kurzname darf nicht leer sein.");
        if (d.AlleElemente(e.Typ).Any(x => x.Id != e.Id && x.Kurzname.Equals(kurzname, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Der Kurzname \"{kurzname}\" ist bereits vergeben.");
        e.Kurzname = kurzname;
    }

    /// <summary>Anzahl der Verwendungen (Unterrichte) – für Löschhinweise.</summary>
    public static int Verwendung(Schuldatei d, Element e) => e.Typ switch
    {
        ElementTyp.Klasse => d.Unterrichte.Count(u => u.KlassenIds.Contains(e.Id)),
        ElementTyp.Lehrer => d.Unterrichte.Count(u => u.LehrerIds.Contains(e.Id)),
        ElementTyp.Fach => d.Unterrichte.Count(u => u.FachIds.Contains(e.Id)),
        ElementTyp.Raum => d.Unterrichte.Count(u => u.Zeilen.Any(z => z.FachraumId == e.Id || z.StammraumId == e.Id)),
        _ => 0,
    };

    public static void Loeschen(Schuldatei d, Element e)
    {
        var id = e.Id;
        switch (e)
        {
            case Klasse:
                d.Klassen.RemoveAll(x => x.Id == id);
                foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen)) z.KlassenIds.Remove(id);
                d.Abwesenheiten.RemoveAll(a => a.Art == AbwesenheitsArt.Klasse && a.ElementId == id);
                foreach (var s in d.Schueler.Where(s => s.KlasseId == id)) s.KlasseId = null;
                foreach (var v in d.Vertretungen) v.KlassenIds.Remove(id);
                break;
            case Lehrer:
                d.Lehrer.RemoveAll(x => x.Id == id);
                foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen).Where(z => z.LehrerId == id)) z.LehrerId = null;
                foreach (var k in d.Klassen) k.KlassenlehrerIds.Remove(id);
                d.Abwesenheiten.RemoveAll(a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == id);
                foreach (var a in d.Abwesenheiten) a.BegleitLehrerIds.Remove(id);
                foreach (var per in d.Perioden) per.Pausenaufsichten?.RemoveAll(p => p.LehrerId == id);
                foreach (var v in d.Vertretungen) { if (v.VertreterId == id) v.VertreterId = null; if (v.LehrerId == id) v.LehrerId = null; }
                foreach (var a in d.Abteilungen.Where(a => a.LeiterId == id)) a.LeiterId = null;
                break;
            case Raum:
                d.Raeume.RemoveAll(x => x.Id == id);
                foreach (var r in d.Raeume.Where(r => r.AusweichraumId == id)) r.AusweichraumId = null;
                foreach (var k in d.Klassen.Where(k => k.StammraumId == id)) k.StammraumId = null;
                foreach (var l in d.Lehrer.Where(l => l.StammraumId == id)) l.StammraumId = null;
                foreach (var f in d.Faecher.Where(f => f.FachraumId == id)) f.FachraumId = null;
                foreach (var g in d.Raumgruppen) g.RaumIds.Remove(id);
                foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen))
                {
                    if (z.FachraumId == id) z.FachraumId = null;
                    if (z.StammraumId == id) z.StammraumId = null;
                }
                foreach (var p in d.Perioden.SelectMany(p => p.Plan))
                    for (var i = 0; i < p.Raeume.Count; i++) if (p.Raeume[i] == id) p.Raeume[i] = null;
                d.Abwesenheiten.RemoveAll(a => a.Art == AbwesenheitsArt.Raum && a.ElementId == id);
                break;
            case Fach:
                d.Faecher.RemoveAll(x => x.Id == id);
                foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen).Where(z => z.FachId == id)) z.FachId = null;
                foreach (var l in d.Lehrer) l.FaecherIds.Remove(id);
                d.Stundentafel.RemoveAll(s => s.FachId == id);
                break;
            case Raumgruppe:
                d.Raumgruppen.RemoveAll(x => x.Id == id);
                foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen))
                {
                    if (z.FachraumgruppeId == id) z.FachraumgruppeId = null;
                    if (z.StammraumgruppeId == id) z.StammraumgruppeId = null;
                }
                foreach (var f in d.Faecher.Where(f => f.FachraumgruppeId == id)) f.FachraumgruppeId = null;
                break;
            case Gang:
                d.Gaenge.RemoveAll(x => x.Id == id);
                foreach (var r in d.Raeume.Where(r => r.GangId == id)) r.GangId = null;
                foreach (var per in d.Perioden) per.Pausenaufsichten?.RemoveAll(p => p.GangId == id);
                break;
            case Abteilung:
                d.Abteilungen.RemoveAll(x => x.Id == id);
                foreach (var x in d.Klassen.Cast<Element>().Concat(d.Lehrer).Concat(d.Raeume).Concat(d.Faecher).Where(x => x.AbteilungId == id)) x.AbteilungId = null;
                break;
            case Abwesenheitsgrund:
                d.Abwesenheitsgruende.RemoveAll(x => x.Id == id);
                foreach (var a in d.Abwesenheiten.Where(a => a.GrundId == id)) a.GrundId = null;
                break;
            case Schueler:
                d.Schueler.RemoveAll(x => x.Id == id);
                break;
        }
        d.Kommentare.RemoveAll(k => k.ElementTyp == e.Typ && k.ElementId == id);
    }

    public static void UnterrichtLoeschen(Schuldatei d, int unterrichtId)
    {
        d.Unterrichte.RemoveAll(u => u.Id == unterrichtId);
        foreach (var p in d.Perioden) p.Plan.RemoveAll(x => x.UnterrichtId == unterrichtId);
        foreach (var s in d.Schueler) s.KurswahlIds.Remove(unterrichtId);
        d.Vertretungen.RemoveAll(v => v.UnterrichtId == unterrichtId && Vertretungsplanung.Vertretungen.IstAbgeleitet(v));
    }

    /// <summary>Elemente in Anzeigereihenfolge (Lehrer und Fächer alphabetisch, sonst Eingabereihenfolge).</summary>
    public static IEnumerable<Element> Sortiert(Schuldatei d, ElementTyp typ) => typ is ElementTyp.Lehrer or ElementTyp.Fach && d.AlleElemente(typ).All(e => e.Sortierung == 0)
        ? d.AlleElemente(typ).OrderBy(e => e.Kurzname, StringComparer.Create(Format.De, true))
        : d.AlleElemente(typ).OrderBy(e => e.Sortierung).ThenBy(e => e.Kurzname, StringComparer.Create(Format.De, true));

    public static IEnumerable<T> Sortiert<T>(IEnumerable<T> liste) where T : Element
    {
        var l = liste.ToList();
        var cmp = StringComparer.Create(Format.De, true);
        return l.All(e => e.Sortierung == 0) ? l.OrderBy(e => e.Kurzname, cmp) : l.OrderBy(e => e.Sortierung).ThenBy(e => e.Kurzname, cmp);
    }
}
