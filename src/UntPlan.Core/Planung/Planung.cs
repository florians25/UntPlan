using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.Core.Planung;

/// <summary>Farbe eines Feldes beim manuellen Planen (Drag &amp; Drop).</summary>
public enum Feldstatus
{
    /// <summary>Nicht farblich gekennzeichnet: Verplanung nicht kollisionsfrei möglich.</summary>
    Kollision,
    /// <summary>Grün: kollisionsfreie Verplanung möglich.</summary>
    Frei,
    /// <summary>Rot: kollisionsfrei, aber wichtige Vorgaben verletzt (z. B. Sperrung, negativer Zeitwunsch).</summary>
    VorgabeVerletzt,
    /// <summary>Violett: kollisionsfrei, aber weder gewünschter Raum noch Ausweichraum frei.</summary>
    KeinRaum,
    /// <summary>Grün hinterlegte Stunde: Tausch möglich.</summary>
    TauschMoeglich,
}

public sealed record Hindernis(Planstunde Planstunde, Unterricht Unterricht, string Grund);

public sealed record Tauschvorschlag(Planstunde A, Planstunde B, double Gewinn, string Beschreibung);

public static class Planungsdienst
{
    public static IEnumerable<int> Klassen(Unterricht u) => u.KlassenIds;

    /// <summary>Planstunden, die eine Verplanung von u an (tag, stunde) behindern (Lehrer- oder Klassenkollision).</summary>
    public static List<Hindernis> Hindernisse(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, ICollection<Planstunde>? ignorieren = null)
    {
        var res = new List<Hindernis>();
        var lehrer = u.LehrerIds.ToHashSet();
        var klassen = u.KlassenIds.ToHashSet();
        foreach (var p in d.Stundenplan)
        {
            if (p.Tag != tag || p.Stunde != stunde || p.UnterrichtId == u.Id) continue;
            if (ignorieren != null && ignorieren.Contains(p)) continue;
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o)) continue;
            if (!string.IsNullOrWhiteSpace(u.Band) && u.Band == o.Band) continue;
            if (!Kalender.GleicheWoche(d, u, o)) continue; // A-/B-Woche: keine Kollision
            var gemeinsameL = o.LehrerIds.Where(lehrer.Contains).ToList();
            var gemeinsameK = o.KlassenIds.Where(klassen.Contains).ToList();
            if (gemeinsameL.Count > 0) res.Add(new Hindernis(p, o, $"Lehrer {string.Join(",", gemeinsameL.Select(ix.KLehrer))}"));
            else if (gemeinsameK.Count > 0) res.Add(new Hindernis(p, o, $"Klasse {ix.KKlassen(gemeinsameK)}"));
        }
        return res;
    }

    public static bool VorgabeVerletzt(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde)
    {
        if (!d.Zeitraster.Verfuegbar(tag, stunde)) return true;
        if (u.Zeitwuensche.Get(tag, stunde) < 0) return true;
        foreach (var l in u.LehrerIds) if (ix.Lehrer.TryGetValue(l, out var le) && le.Zeitwuensche.Get(tag, stunde) < 0) return true;
        foreach (var k in u.KlassenIds) if (ix.Klassen.TryGetValue(k, out var kl) && kl.Zeitwuensche.Get(tag, stunde) < 0) return true;
        foreach (var f in u.FachIds)
            if (ix.Faecher.TryGetValue(f, out var fa) && (fa.Zeitwuensche.Get(tag, stunde) < 0 || (fa.KeinNachmittag && d.Zeitraster.Art(tag, stunde) == Tageszeit.Nachmittag)))
                return true;
        return false;
    }

    /// <summary>Ermittelt freie Räume je Kopplungszeile (Wunschraum, Ausweichraum-Ring, Raumgruppe).</summary>
    public static List<int?> RaeumeSuchen(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, ICollection<Planstunde>? ignorieren = null)
    {
        var belegt = new HashSet<int>();
        foreach (var p in d.Stundenplan)
            if (p.Tag == tag && p.Stunde == stunde && p.UnterrichtId != u.Id && (ignorieren == null || !ignorieren.Contains(p)))
                foreach (var r in p.Raeume) if (r.HasValue) belegt.Add(r.Value);
        var res = new List<int?>();
        foreach (var z in u.Zeilen)
        {
            int? gewaehlt = null;
            foreach (var r in ix.RaumKandidaten(z))
                if (!belegt.Contains(r) && !ix.Raeume[r].Zeitwuensche.Gesperrt(tag, stunde)) { gewaehlt = r; break; }
            if (gewaehlt.HasValue) belegt.Add(gewaehlt.Value);
            res.Add(gewaehlt);
        }
        return res;
    }

    public static Feldstatus Status(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, int laenge = 1, ICollection<Planstunde>? ignorieren = null)
    {
        if (stunde + laenge - 1 > d.Zeitraster.StundenProTag) return Feldstatus.Kollision;
        var verletzt = false;
        var keinRaum = false;
        for (var j = 0; j < laenge; j++)
        {
            var h = Hindernisse(d, ix, u, tag, stunde + j, ignorieren);
            if (h.Count > 0)
                return h.Count == 1 && laenge == 1 && !h[0].Planstunde.Fixiert ? Feldstatus.TauschMoeglich : Feldstatus.Kollision;
            if (VorgabeVerletzt(d, ix, u, tag, stunde + j)) verletzt = true;
            var raeume = RaeumeSuchen(d, ix, u, tag, stunde + j, ignorieren);
            for (var i = 0; i < u.Zeilen.Count; i++)
                if (raeume[i] is null && ix.RaumKandidaten(u.Zeilen[i]).Count > 0) keinRaum = true;
        }
        if (verletzt) return Feldstatus.VorgabeVerletzt;
        if (keinRaum) return Feldstatus.KeinRaum;
        return Feldstatus.Frei;
    }

    /// <summary>Setzt eine (nicht verplante) Stunde eines Unterrichts. Behindernde Stunden werden entplant, wenn gewünscht.</summary>
    public static Planstunde Verplanen(Schuldatei d, Unterricht u, int tag, int stunde, bool behinderndeEntplanen, bool fixieren = false)
    {
        var ix = new DatenIndex(d);
        if (behinderndeEntplanen)
            foreach (var h in Hindernisse(d, ix, u, tag, stunde)) d.Stundenplan.Remove(h.Planstunde);
        var p = new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = RaeumeSuchen(d, ix, u, tag, stunde), Fixiert = fixieren };
        d.Stundenplan.Add(p);
        return p;
    }

    /// <summary>Verschiebt eine verplante Stunde. Befindet sich am Ziel genau eine andere Stunde, kann getauscht werden.</summary>
    public static void Verschieben(Schuldatei d, Planstunde p, int tag, int stunde, bool tauschen, bool behinderndeEntplanen)
    {
        var ix = new DatenIndex(d);
        var u = ix.Unterrichte[p.UnterrichtId];
        var hind = Hindernisse(d, ix, u, tag, stunde, new[] { p });
        if (tauschen && hind.Count == 1)
        {
            var q = hind[0].Planstunde;
            int qt = q.Tag, qs = q.Stunde;
            q.Tag = p.Tag;
            q.Stunde = p.Stunde;
            p.Tag = qt;
            p.Stunde = qs;
            q.Raeume = RaeumeSuchen(d, ix, ix.Unterrichte[q.UnterrichtId], q.Tag, q.Stunde, new[] { q });
            p.Raeume = RaeumeSuchen(d, ix, u, p.Tag, p.Stunde, new[] { p });
            return;
        }
        if (behinderndeEntplanen) foreach (var h in hind) d.Stundenplan.Remove(h.Planstunde);
        p.Tag = tag;
        p.Stunde = stunde;
        p.Raeume = RaeumeSuchen(d, ix, u, tag, stunde, new[] { p });
    }

    public static void Entplanen(Schuldatei d, Planstunde p) => d.Stundenplan.Remove(p);

    public static void AlleEntplanen(Schuldatei d, bool fixierteBehalten) =>
        d.Stundenplan.RemoveAll(p => !(fixierteBehalten && p.Fixiert));

    /// <summary>Raum zuordnen: verdrängt ggf. den Unterricht aus dem Raum, erzeugt eine Kollision oder tauscht die Räume.</summary>
    public enum RaumKonfliktLoesung { Verdraengen, KollisionErzeugen, RaeumeTauschen }

    public static void RaumZuordnen(Schuldatei d, Planstunde p, int zeile, int? raumId, RaumKonfliktLoesung loesung)
    {
        while (p.Raeume.Count <= zeile) p.Raeume.Add(null);
        if (raumId is null) { p.Raeume[zeile] = null; return; }
        var andere = d.Stundenplan.Where(q => q != p && q.Tag == p.Tag && q.Stunde == p.Stunde && q.Raeume.Contains(raumId)).ToList();
        var alt = p.Raeume[zeile];
        foreach (var q in andere)
        {
            for (var i = 0; i < q.Raeume.Count; i++)
            {
                if (q.Raeume[i] != raumId) continue;
                if (loesung == RaumKonfliktLoesung.Verdraengen) q.Raeume[i] = null;
                else if (loesung == RaumKonfliktLoesung.RaeumeTauschen) q.Raeume[i] = alt;
            }
        }
        p.Raeume[zeile] = raumId;
    }

    /// <summary>Freie und besetzte Räume zu einer Stunde (Raumzuordnungsdialog).</summary>
    public static List<(Raum Raum, bool Frei, Planstunde? Belegt, bool Wunsch)> Raumliste(Schuldatei d, DatenIndex ix, Planstunde p, int zeile)
    {
        var u = ix.Unterrichte[p.UnterrichtId];
        var kand = zeile < u.Zeilen.Count ? ix.RaumKandidaten(u.Zeilen[zeile]) : new List<int>();
        var res = new List<(Raum, bool, Planstunde?, bool)>();
        foreach (var r in d.Raeume)
        {
            var belegt = d.Stundenplan.FirstOrDefault(q => q != p && q.Tag == p.Tag && q.Stunde == p.Stunde && q.Raeume.Contains(r.Id));
            var frei = belegt is null && !r.Zeitwuensche.Gesperrt(p.Tag, p.Stunde);
            res.Add((r, frei, belegt, kand.Contains(r.Id)));
        }
        return res.OrderByDescending(x => x.Item4).ThenByDescending(x => x.Item2).ThenBy(x => x.Item1.Sortierung).ToList();
    }

    /// <summary>Zeile der Raumliste im Raumzuordnungsdialog.</summary>
    public sealed record RaumZeile(Raum Raum, bool Wunsch, bool Ausweichraum, bool Stammraum, List<Planstunde> Belegt, int Gesperrt,
        int StundenFrei, int StundenGesamt, int? KapazitaetDiff, string Raumgruppen, string Gang, int Wochenbelegung)
    {
        public bool GanzFrei => StundenFrei == StundenGesamt;
    }

    /// <summary>Für „Anwenden auf“: die betroffenen Stunden (eine Stunde, der Block oder alle Stunden des Unterrichts).</summary>
    public static List<Planstunde> RaumStunden(Schuldatei d, Planstunde p, int umfang) => umfang switch
    {
        0 => new List<Planstunde> { p },
        1 => BlockVon(d, p),
        _ => d.Stundenplan.Where(x => x.UnterrichtId == p.UnterrichtId).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).ToList(),
    };

    /// <summary>Doppelstunde bzw. Block: benachbarte Stunden desselben Unterrichts am selben Tag.</summary>
    public static List<Planstunde> BlockVon(Schuldatei d, Planstunde p)
    {
        var gleiche = d.Stundenplan.Where(x => x.UnterrichtId == p.UnterrichtId && x.Tag == p.Tag).ToList();
        var res = new List<Planstunde> { p };
        for (var s = p.Stunde - 1; gleiche.FirstOrDefault(x => x.Stunde == s) is { } x; s--) res.Insert(0, x);
        for (var s = p.Stunde + 1; gleiche.FirstOrDefault(x => x.Stunde == s) is { } x; s++) res.Add(x);
        return res;
    }

    /// <summary>
    /// Raumliste für eine Kopplungszeile über die betroffenen Stunden: Wunschraum/Ausweichraum/Stammraum, Belegung durch anderen
    /// Unterricht (A/B-Wochen beachtet), Sperrungen, freie Stunden, Kapazitätsdifferenz zur Schülerzahl, Raumgruppen, Gang.
    /// </summary>
    public static List<RaumZeile> RaumUebersicht(Schuldatei d, DatenIndex ix, List<Planstunde> stunden, int zeile)
    {
        if (stunden.Count == 0) return new();
        var u = ix.Unterrichte[stunden[0].UnterrichtId];
        var z = zeile < u.Zeilen.Count ? u.Zeilen[zeile] : u.Zeilen.FirstOrDefault();
        var kand = z is null ? new List<int>() : ix.RaumKandidaten(z);
        var wunsch = z?.Wunschraum;
        var gruppe = z?.Wunschraumgruppe is int gid ? d.Raumgruppen.FirstOrDefault(g => g.Id == gid) : null;
        var klassen = (z?.KlassenIds ?? u.KlassenIds).Select(k => ix.Klassen.GetValueOrDefault(k)).Where(k => k != null).ToList();
        var stammraeume = klassen.Select(k => k!.StammraumId).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var schueler = klassen.Sum(k => k!.Schuelerzahl);
        var gruppen = d.Raumgruppen.SelectMany(g => g.RaumIds.Select(r => (r, g.Kurzname))).ToLookup(x => x.r, x => x.Kurzname);
        var belegung = d.Stundenplan.SelectMany(q => q.Raeume.Where(r => r.HasValue).Select(r => r!.Value)).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        var eigene = stunden.ToHashSet();
        var res = new List<RaumZeile>();
        foreach (var r in d.Raeume.Where(r => !r.Ignorieren))
        {
            var belegt = new List<Planstunde>();
            var gesperrt = 0;
            var frei = 0;
            foreach (var p in stunden)
            {
                var b = d.Stundenplan.FirstOrDefault(q => !eigene.Contains(q) && q.UnterrichtId != u.Id && q.Tag == p.Tag && q.Stunde == p.Stunde && q.Raeume.Contains(r.Id) &&
                    ix.Unterrichte.TryGetValue(q.UnterrichtId, out var o) && Kalender.GleicheWoche(d, u, o));
                var sperre = r.Zeitwuensche.Gesperrt(p.Tag, p.Stunde);
                if (b != null) belegt.Add(b);
                if (sperre) gesperrt++;
                if (b is null && !sperre) frei++;
            }
            var ausweich = wunsch is int w && kand.Contains(r.Id) && r.Id != w;
            res.Add(new RaumZeile(r, kand.Contains(r.Id) || gruppe?.RaumIds.Contains(r.Id) == true, ausweich, stammraeume.Contains(r.Id), belegt, gesperrt, frei, stunden.Count,
                r.Kapazitaet > 0 && schueler > 0 ? r.Kapazitaet - schueler : null, string.Join(",", gruppen[r.Id]), ix.K(ElementTyp.Gang, r.GangId), belegung.GetValueOrDefault(r.Id)));
        }
        return res.OrderByDescending(x => x.Wunsch).ThenByDescending(x => x.StundenFrei).ThenBy(x => x.Raum.Sortierung).ThenBy(x => x.Raum.Kurzname).ToList();
    }

    /// <summary>Raum für eine Kopplungszeile in mehreren Stunden zuordnen bzw. löschen (raumId = null).</summary>
    public static void RaumZuordnen(Schuldatei d, IEnumerable<Planstunde> stunden, int zeile, int? raumId, RaumKonfliktLoesung loesung)
    {
        foreach (var p in stunden.ToList()) RaumZuordnen(d, p, zeile, raumId, loesung);
    }

    /// <summary>Tauschvorschläge für eine Stunde einer Klasse mit Gewinn (Verbesserung der Bewertungszahl).</summary>
    public static List<Tauschvorschlag> Tauschvorschlaege(Schuldatei d, Planstunde p, int max = 15)
    {
        var ix = new DatenIndex(d);
        if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return new();
        var klassen = u.KlassenIds.ToHashSet();
        var basisKennzahlen = Bewertung.Kennzahlen(d);
        var basis = basisKennzahlen.Bewertung;
        var res = new List<Tauschvorschlag>();
        var kandidaten = d.Stundenplan.Where(q => q != p && !q.Fixiert && q.UnterrichtId != p.UnterrichtId &&
            ix.Unterrichte.TryGetValue(q.UnterrichtId, out var o) && o.KlassenIds.Any(klassen.Contains)).ToList();
        foreach (var q in kandidaten)
        {
            var plan = d.Stundenplan.Select(x => x == p ? Mit(x, q.Tag, q.Stunde) : x == q ? Mit(x, p.Tag, p.Stunde) : x).ToList();
            var kz = Bewertung.Kennzahlen(d, plan);
            if (kz.Kollisionen > basisKennzahlen.Kollisionen) continue; // nur kollisionsfreie Tausche anbieten
            var k = kz.Bewertung;
            var o = ix.Unterrichte[q.UnterrichtId];
            res.Add(new Tauschvorschlag(p, q, basis - k,
                $"{ix.UnterrichtText(u)} ({Tagname(d, p.Tag)} {p.Stunde}.) ⇄ {ix.UnterrichtText(o)} ({Tagname(d, q.Tag)} {q.Stunde}.)"));
        }
        return res.OrderByDescending(x => x.Gewinn).Take(max).ToList();
    }

    private static Planstunde Mit(Planstunde x, int tag, int stunde)
    {
        var k = x.Kopie();
        k.Tag = tag;
        k.Stunde = stunde;
        return k;
    }

    public static readonly string[] TageKurz = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
    public static readonly string[] TageLang = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };

    public static string Tagname(Schuldatei d, int tag, bool lang = false)
    {
        var i = (d.Zeitraster.ErsterSchultag + tag) % 7;
        return lang ? TageLang[i] : TageKurz[i];
    }
}
