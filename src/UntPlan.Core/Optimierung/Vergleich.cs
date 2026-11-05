using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

/// <summary>Unterschied eines Elements bzw. Unterrichts im Vergleichsmodus.</summary>
/// <param name="Schluessel">Kurzname des Elements bzw. U-Nr.</param>
/// <param name="Felder">Unterschiedliche Felder (Feld, aktueller Wert, Vergleichswert).</param>
public sealed record DateiVergleichsZeile(string Schluessel, IReadOnlyList<(string Feld, string Aktuell, string Vergleich)> Felder);

/// <summary>
/// Vergleichsmodus (Handbuch 7.17): aktueller Datenbestand (grün) gegen einen anderen (rot) – andere Datei oder andere Periode.
/// Stundenplanvergleich mit „Nur geänderte Pläne“, „Ohne Räume“ und „Detaillierter Vergleich bei Kopplungen“; Stammdaten- und
/// Unterrichtsvergleich mit „Nur unterschiedliche Zeilen/Spalten“.
/// </summary>
public static class Vergleich
{
    /// <summary>Datenbestand einer anderen Periode derselben Datei (Kopie mit dieser Periode als aktiver).</summary>
    public static Schuldatei Periode(Schuldatei d, int periodeId)
    {
        var k = Dokument.Klonen(d);
        k.AktivePeriodeId = periodeId;
        return k;
    }

    /// <summary>
    /// Stunden eines Elements als vergleichbare Texte je (Tag, Stunde): Unterricht (Fach, Klassen, Lehrkräfte) und – ohne „Ohne Räume“ –
    /// der Raum. Ohne „Detaillierter Vergleich bei Kopplungen“ zählt nur die Kopplungszeile des Elements, sonst alle Zeilen.
    /// </summary>
    public static SortedSet<string> Stunden(Schuldatei d, ElementTyp typ, string kurzname, bool ohneRaeume, bool detailliert)
    {
        var ix = new DatenIndex(d);
        var el = d.AlleElemente(typ).FirstOrDefault(e => e.Kurzname.Equals(kurzname, StringComparison.OrdinalIgnoreCase));
        var res = new SortedSet<string>(StringComparer.Ordinal);
        if (el is null) return res;
        foreach (var p in ix.PlanVon(typ, el.Id))
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var eigene = typ switch
                {
                    ElementTyp.Klasse => z.KlassenIds.Contains(el.Id),
                    ElementTyp.Lehrer => z.LehrerId == el.Id,
                    ElementTyp.Raum => p.RaumFuerZeile(i) == el.Id,
                    ElementTyp.Fach => z.FachId == el.Id,
                    _ => true,
                };
                if (!eigene && !detailliert) continue;
                var raum = ohneRaeume || typ == ElementTyp.Raum ? "" : ix.KRaum(p.RaumFuerZeile(i));
                res.Add($"{p.Tag}|{p.Stunde}|{ix.KFach(z.FachId)}|{ix.KKlassen(z.KlassenIds)}|{ix.KLehrer(z.LehrerId)}|{raum}");
            }
        }
        return res;
    }

    /// <summary>„Nur geänderte Pläne“: Kurznamen der Elemente, deren Stundenplan sich unterscheidet (auch Elemente, die nur in einem Bestand vorkommen).</summary>
    public static List<string> GeaendertePlaene(Schuldatei aktuell, Schuldatei vergleich, ElementTyp typ, bool ohneRaeume, bool detailliert)
    {
        var namen = aktuell.AlleElemente(typ).Select(e => e.Kurzname).Union(vergleich.AlleElemente(typ).Select(e => e.Kurzname), StringComparer.OrdinalIgnoreCase);
        return namen.Where(n => !Stunden(aktuell, typ, n, ohneRaeume, detailliert).SetEquals(Stunden(vergleich, typ, n, ohneRaeume, detailliert)))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Unterschiedliche Stunden eines Elements: nur im aktuellen bzw. nur im Vergleichsbestand (Tag, Stunde, Text).</summary>
    public static (List<string> NurAktuell, List<string> NurVergleich) Stundenunterschiede(Schuldatei aktuell, Schuldatei vergleich, ElementTyp typ, string kurzname, bool ohneRaeume, bool detailliert)
    {
        var a = Stunden(aktuell, typ, kurzname, ohneRaeume, detailliert);
        var b = Stunden(vergleich, typ, kurzname, ohneRaeume, detailliert);
        return (a.Except(b).ToList(), b.Except(a).ToList());
    }

    /// <summary>
    /// Stammdatenvergleich je Element (nach Kurzname): „Nur unterschiedliche Zeilen anzeigen“ (<paramref name="nurZeilen"/>) lässt Elemente ohne
    /// Unterschied weg, „Nur unterschiedliche Spalten anzeigen“ (<paramref name="nurSpalten"/>) liefert nur die Felder mit Unterschied.
    /// </summary>
    public static List<DateiVergleichsZeile> Stammdaten(Schuldatei aktuell, Schuldatei vergleich, ElementTyp typ, bool nurZeilen = true, bool nurSpalten = true)
    {
        var ixA = new DatenIndex(aktuell);
        var ixB = new DatenIndex(vergleich);
        var res = new List<DateiVergleichsZeile>();
        var b = vergleich.AlleElemente(typ).ToDictionary(e => e.Kurzname, StringComparer.OrdinalIgnoreCase);
        var a = aktuell.AlleElemente(typ).ToDictionary(e => e.Kurzname, StringComparer.OrdinalIgnoreCase);
        foreach (var n in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            var fa = a.TryGetValue(n, out var ea) ? Felder(ea, ixA) : new Dictionary<string, string>();
            var fb = b.TryGetValue(n, out var eb) ? Felder(eb, ixB) : new Dictionary<string, string>();
            var diff = fa.Keys.Union(fb.Keys).Where(k => !nurSpalten || fa.GetValueOrDefault(k, "") != fb.GetValueOrDefault(k, ""))
                .Select(k => (k, fa.GetValueOrDefault(k, ""), fb.GetValueOrDefault(k, ""))).ToList();
            var verschieden = diff.Any(x => x.Item2 != x.Item3);
            if (ea is null) diff.Insert(0, ("Element", "", n));
            if (eb is null) diff.Insert(0, ("Element", n, ""));
            if (verschieden || ea is null || eb is null || !nurZeilen) res.Add(new DateiVergleichsZeile(n, diff));
        }
        return res;
    }

    /// <summary>Unterrichtsvergleich: Unterrichte (nach U-Nr) mit unterschiedlichen Feldern.</summary>
    public static List<DateiVergleichsZeile> Unterrichte(Schuldatei aktuell, Schuldatei vergleich, bool nurZeilen = true, bool nurSpalten = true)
    {
        var ixA = new DatenIndex(aktuell);
        var ixB = new DatenIndex(vergleich);
        Dictionary<string, string> F(Unterricht u, DatenIndex ix) => new()
        {
            ["Wst"] = u.Wochenstunden.ToString(),
            ["Lehrer"] = string.Join(",", u.Zeilen.Select(z => ix.KLehrer(z.LehrerId))),
            ["Fach"] = string.Join(",", u.Zeilen.Select(z => ix.KFach(z.FachId))),
            ["Klasse(n)"] = string.Join(" | ", u.Zeilen.Select(z => ix.KKlassen(z.KlassenIds))),
            ["Fachraum"] = string.Join(",", u.Zeilen.Select(z => ix.KRaum(z.FachraumId))),
            ["Stammraum"] = string.Join(",", u.Zeilen.Select(z => ix.KRaum(z.StammraumId))),
            ["Dopp.Std."] = u.Doppelstunden.ToString(),
            ["Block"] = u.Block,
            ["Text"] = u.Text,
        };
        var a = aktuell.Unterrichte.ToDictionary(u => u.Id);
        var b = vergleich.Unterrichte.ToDictionary(u => u.Id);
        var res = new List<DateiVergleichsZeile>();
        foreach (var id in a.Keys.Union(b.Keys).OrderBy(x => x))
        {
            var fa = a.TryGetValue(id, out var ua) ? F(ua, ixA) : new Dictionary<string, string>();
            var fb = b.TryGetValue(id, out var ub) ? F(ub, ixB) : new Dictionary<string, string>();
            var diff = fa.Keys.Union(fb.Keys).Where(k => !nurSpalten || fa.GetValueOrDefault(k, "") != fb.GetValueOrDefault(k, ""))
                .Select(k => (k, fa.GetValueOrDefault(k, ""), fb.GetValueOrDefault(k, ""))).ToList();
            var verschieden = diff.Any(x => x.Item2 != x.Item3);
            if (verschieden || !nurZeilen) res.Add(new DateiVergleichsZeile(id.ToString(), diff));
        }
        return res;
    }

    private static Dictionary<string, string> Felder(Element e, DatenIndex ix)
    {
        var f = new Dictionary<string, string>
        {
            ["Langname"] = e.Langname, ["Text"] = e.Text, ["Beschreibung"] = e.Beschreibung, ["Alias"] = e.Alias, ["Statistik"] = e.Statistik,
        };
        switch (e)
        {
            case Lehrer l:
                f["Vorname"] = l.Vorname; f["Wochensoll"] = l.Wochensoll.ToString("0.###"); f["Stammraum"] = ix.KRaum(l.StammraumId);
                f["Fächer"] = string.Join(",", l.FaecherIds.Select(x => ix.KFach(x)));
                break;
            case Klasse k:
                f["Stammraum"] = ix.KRaum(k.StammraumId); f["Klassenlehrer"] = string.Join(",", k.KlassenlehrerIds.Select(x => ix.KLehrer(x))); f["Jahrgang"] = k.Jahrgang;
                break;
            case Raum r:
                f["Ausweichraum"] = ix.KRaum(r.AusweichraumId); f["Raumgewicht"] = r.Raumgewicht.ToString(); f["Kapazität"] = r.Kapazitaet.ToString();
                break;
            case Fach fa:
                f["Fachraum"] = ix.KRaum(fa.FachraumId); f["Hauptfach"] = fa.Hauptfach ? "X" : "";
                break;
        }
        return f;
    }
}
