using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

/// <summary>
/// Bewertung eines Stundenplans: Jeder Verstoß gegen eine Vorgabe wird mit Schlechtpunkten geahndet und gemäß Gewichtung
/// bewertet. Je niedriger die Bewertungszahl, umso weniger Vorgaben sind verletzt.
/// </summary>
public static class Bewertung
{
    public static List<Verstoss> Verstoesse(Schuldatei d, IReadOnlyList<Planstunde>? plan = null)
    {
        plan ??= d.Stundenplan;
        var liste = new List<Verstoss>();
        var m = Modell(d, plan);
        m.Gesamtkosten(liste.Add);
        RaumVerstoesse(d, plan, liste.Add);
        return liste;
    }

    public static Kennzahlen Kennzahlen(Schuldatei d, IReadOnlyList<Planstunde>? plan = null)
    {
        var v = Verstoesse(d, plan);
        int Anz(params VerstossArt[] arten) => v.Count(x => arten.Contains(x.Art));
        return new Kennzahlen
        {
            Bewertung = (long)Math.Round(v.Sum(x => x.Kosten)),
            NichtVerplant = v.Where(x => x.Art == VerstossArt.NichtVerplant).Sum(x => int.TryParse(x.Text.Split(' ')[0], out var n) ? n : 1),
            HohlstundenKlassen = Anz(VerstossArt.HohlstundeKlasse),
            HohlstundenLehrer = v.Where(x => x.Art == VerstossArt.HohlstundenLehrer).Sum(x => int.TryParse(x.Text.Split(' ')[0], out var n) ? n : 1),
            Kernzeitverletzungen = Anz(VerstossArt.Kernzeitverletzung),
            FachZweimalTag = Anz(VerstossArt.FachMehrmalsProTag),
            Doppelstundenfehler = Anz(VerstossArt.DoppelstundenFehler, VerstossArt.BlockFehler),
            Zeitwunschverletzungen = Anz(VerstossArt.ZeitwunschKlasse, VerstossArt.ZeitwunschLehrer, VerstossArt.ZeitwunschRaum, VerstossArt.UnbestimmterZeitwunsch),
            Kollisionen = Anz(VerstossArt.Lehrerkollision, VerstossArt.Klassenkollision, VerstossArt.Raumkollision, VerstossArt.SperrungKlasse,
                VerstossArt.SperrungLehrer, VerstossArt.SperrungRaum, VerstossArt.SperrungUnterricht, VerstossArt.ZeitrasterLeer),
            OhneRaum = Anz(VerstossArt.KeinRaum),
        };
    }

    /// <summary>Kosten der Platzierung einer Stunde an einer Stelle (für die Farbanzeige beim manuellen Planen).</summary>
    public static PlanModell Modell(Schuldatei d, IReadOnlyList<Planstunde> plan)
    {
        var m = new PlanModell(d, mitPools: false);
        var gesetzt = new Dictionary<int, int>();
        foreach (var p in plan)
        {
            var ui = m.UnterrichtIdx(p.UnterrichtId);
            if (ui < 0) continue;
            var bi = m.NeuerBlock(new[] { ui }, 1, true, p);
            if (p.Tag >= 0 && p.Tag < m.T && p.Stunde >= 1 && p.Stunde <= m.S) m.Platzieren(bi, p.Tag, p.Stunde - 1);
            gesetzt[ui] = gesetzt.GetValueOrDefault(ui) + 1;
        }
        for (var ui = 0; ui < m.Unterrichte.Count; ui++)
        {
            var rest = m.Unterrichte[ui].Wochenstunden - gesetzt.GetValueOrDefault(ui);
            if (rest > 0) m.NeuerBlock(new[] { ui }, rest, true); // nicht platziert = nicht verplant
        }
        return m;
    }

    private static void RaumVerstoesse(Schuldatei d, IReadOnlyList<Planstunde> plan, Action<Verstoss> sink)
    {
        var ix = new DatenIndex(d);
        var fRaum = Gewichtung.Faktor(d.Gewichtung.GetValueOrDefault("R.Raumoptimierung", 3));
        var fZR = Gewichtung.Faktor(d.Gewichtung.GetValueOrDefault("Z.Raeume", 2));
        var belegung = new Dictionary<(int, int, int), List<int>>();
        foreach (var p in plan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) || u.Ignorieren) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var r = p.RaumFuerZeile(i);
                var z = u.Zeilen[i];
                if (r is null)
                {
                    if (ix.RaumKandidaten(z).Count > 0)
                    {
                        var g = ix.Raumgewicht(z);
                        var k = (g switch { 0 => 0, 1 => 2, 2 => 6, 3 => 25, _ => 120 }) * fRaum / 8.0;
                        sink(new Verstoss(VerstossArt.KeinRaum, ElementTyp.Unterricht, u.Id, u.Id, p.Tag, p.Stunde, k, "Raum und Ausweichräume belegt – kein Raum"));
                    }
                    continue;
                }
                var key = (r.Value, p.Tag, p.Stunde);
                if (!belegung.TryGetValue(key, out var l)) belegung[key] = l = new List<int>();
                if (!l.Contains(u.Id)) l.Add(u.Id);
                if (ix.Raeume.TryGetValue(r.Value, out var raum))
                {
                    var w = raum.Zeitwuensche.Get(p.Tag, p.Stunde);
                    if (w <= -3) sink(new Verstoss(VerstossArt.SperrungRaum, ElementTyp.Raum, raum.Id, u.Id, p.Tag, p.Stunde, PlanModell.Hart, "Raum gesperrt (-3)"));
                    else if (w < 0) sink(new Verstoss(VerstossArt.ZeitwunschRaum, ElementTyp.Raum, raum.Id, u.Id, p.Tag, p.Stunde, -w * 3 * fZR, $"Zeitwunsch Raum {w}"));
                }
            }
        }
        SelberRaumVerstoesse(ix, plan, fRaum, sink);
        DislozierungsVerstoesse(d, ix, plan, fRaum, sink);
        var wochen = Math.Clamp(d.Einstellungen.Wochenperiodizitaet, 1, 16);
        foreach (var ((raumId, tag, stunde), ids) in belegung)
        {
            if (ids.Count < 2) continue;
            // A-/B-Woche: Unterrichte verschiedener Wochen dürfen denselben Raum nutzen
            var masken = ids.Select(id => Kalender.WochenMaske(d, ix.Unterrichte[id])).ToList();
            var n = Enumerable.Range(0, wochen).Max(w => masken.Count(m => (m & (1 << w)) != 0));
            if (n > 1)
                sink(new Verstoss(VerstossArt.Raumkollision, ElementTyp.Raum, raumId, ids[1], tag, stunde, PlanModell.Hart * (n - 1), $"Raum {ix.KRaum(raumId)} {n}× belegt"));
        }
    }

    /// <summary>(r) Stunden im selben Raum: ein Unterricht mit diesem Kennzeichen belegt je Kopplungszeile verschiedene Räume.</summary>
    private static void SelberRaumVerstoesse(DatenIndex ix, IReadOnlyList<Planstunde> plan, double fRaum, Action<Verstoss> sink)
    {
        foreach (var g in plan.GroupBy(p => p.UnterrichtId))
        {
            if (!ix.Unterrichte.TryGetValue(g.Key, out var u) || !u.SelberRaum || u.Ignorieren) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var raeume = g.Select(p => p.RaumFuerZeile(i)).Where(r => r.HasValue).Distinct().Count();
                if (raeume > 1)
                    sink(new Verstoss(VerstossArt.SelberRaum, ElementTyp.Unterricht, u.Id, u.Id, -1, 0, (raeume - 1) * 10 * fRaum / 8.0, $"{raeume} verschiedene Räume (r)"));
            }
        }
    }

    /// <summary>
    /// Dislozierte Räume: wechselt eine Lehrkraft oder Klasse zwischen Räumen verschiedener Dislozierung, müssen dazwischen so viele
    /// freie Stunden liegen, wie die Wegzeit verlangt (Ziffern: Differenz, Buchstaben: eine Stunde).
    /// </summary>
    private static void DislozierungsVerstoesse(Schuldatei d, DatenIndex ix, IReadOnlyList<Planstunde> plan, double fRaum, Action<Verstoss> sink)
    {
        if (!d.Raeume.Any(r => Raum.DislozierungNormal(r.Dislozierung).Length > 0)) return;
        var orte = new Dictionary<(ElementTyp Typ, int Id, int Tag), SortedDictionary<int, string>>();
        void Merken(ElementTyp typ, int id, int tag, int stunde, int raum)
        {
            if (!ix.Raeume.TryGetValue(raum, out var r)) return;
            if (!orte.TryGetValue((typ, id, tag), out var l)) orte[(typ, id, tag)] = l = new SortedDictionary<int, string>();
            l.TryAdd(stunde, Raum.DislozierungNormal(r.Dislozierung));
        }
        foreach (var p in plan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) || u.Ignorieren) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                if (p.RaumFuerZeile(i) is not int r) continue;
                var z = u.Zeilen[i];
                if (z.LehrerId is int l) Merken(ElementTyp.Lehrer, l, p.Tag, p.Stunde, r);
                foreach (var k in z.KlassenIds) Merken(ElementTyp.Klasse, k, p.Tag, p.Stunde, r);
            }
        }
        foreach (var ((typ, id, tag), stunden) in orte)
        {
            var liste = stunden.ToList();
            for (var i = 0; i + 1 < liste.Count; i++)
            {
                var weg = Raum.Wegzeit(liste[i].Value, liste[i + 1].Value);
                var frei = liste[i + 1].Key - liste[i].Key - 1;
                if (weg > frei)
                    sink(new Verstoss(VerstossArt.Dislozierung, typ, id, null, tag, liste[i + 1].Key, (weg - frei) * 20 * fRaum / 3.0,
                        $"Gebäudewechsel ({Ort(liste[i].Value)} → {Ort(liste[i + 1].Value)}) ohne ausreichende Pause"));
            }
        }
    }

    private static string Ort(string disloz) => disloz.Length > 0 ? disloz : "Hauptgebäude";

    public static string ArtText(VerstossArt a) => a switch
    {
        VerstossArt.HalbtagKlasse or VerstossArt.HalbtagLehrer => "1 Halbtag/Tag (H)",
        VerstossArt.Unterrichtstage => "Unterrichtstage (F/2)",
        VerstossArt.Klassenzeitraster => "Klassenzeitraster",
        VerstossArt.VerschiedeneUnterrichte => "Max. verschiedene Unt./Tag",
        VerstossArt.StundenFolgeLehrer => "Max. Stundenzahl in Folge",
        VerstossArt.Verweildauer => "Max. Verweildauer",
        VerstossArt.Tagesrandpause => "Tagesrand-Pause",
        VerstossArt.ErsteUndLetzteStunde => "1. und letzte Stunde (R)",
        VerstossArt.Fachfolge => "Fachfolge",
        VerstossArt.Nachmittagsstunden => "Nachmittagsstunden des Faches",
        VerstossArt.NachmittagEinzelstunde => "Einzelstunde am Nachmittag (U)",
        VerstossArt.ImmerSelbeZeit => "Immer selbe Zeit (s)",
        VerstossArt.DoppelstundeUeberPause => "Doppelstunde über *-Pause",
        VerstossArt.Unterrichtsfolge => "Unterrichtsfolge",
        VerstossArt.Dislozierung => "Dislozierung",
        VerstossArt.SelberRaum => "Stunden im selben Raum (r)",
        VerstossArt.Lehrerkollision => "Lehrerkollision",
        VerstossArt.Klassenkollision => "Klassenkollision",
        VerstossArt.Raumkollision => "Raumkollision",
        VerstossArt.SperrungLehrer => "Sperrung Lehrer verletzt",
        VerstossArt.SperrungKlasse => "Sperrung Klasse verletzt",
        VerstossArt.SperrungRaum => "Raum gesperrt",
        VerstossArt.SperrungFach => "Sperrung Fach verletzt",
        VerstossArt.SperrungUnterricht => "Gesperrte Stunde",
        VerstossArt.ZeitrasterLeer => "Außerhalb des Zeitrasters",
        VerstossArt.NichtVerplant => "Nicht verplante Stunden",
        VerstossArt.HohlstundeKlasse => "Hohlstunden",
        VerstossArt.Kernzeitverletzung => "Kernzeitverletzung",
        VerstossArt.StdTagKlasse => "Stunden pro Tag",
        VerstossArt.MittagspauseKlasse => "Mittagspause",
        VerstossArt.NachmittageKlasse => "Max. Nachmittage",
        VerstossArt.HauptfaecherProTag => "Hauptfächer pro Tag",
        VerstossArt.HauptfaecherFolge => "Hauptfächer-Folge",
        VerstossArt.ZeitwunschKlasse => "Zeitwunsch verletzt",
        VerstossArt.HohlstundenLehrer => "Hohlstunden außerhalb Bereich",
        VerstossArt.StdTagLehrer => "Zu wenig/viele Stunden pro Tag",
        VerstossArt.MittagspauseLehrer => "Mittagspause",
        VerstossArt.NachmittageLehrer => "Max. Nachmittage",
        VerstossArt.UnbestimmterZeitwunsch => "Unbestimmte Zeitwünsche",
        VerstossArt.ZeitwunschLehrer => "Zeitwunsch verletzt",
        VerstossArt.Tagesbelastung => "Tagesbelastung",
        VerstossArt.FachMehrmalsProTag => "Fach 2x / Tag",
        VerstossArt.DoppelstundenFehler => "Doppelstunden-Fehler",
        VerstossArt.BlockFehler => "Block-Fehler",
        VerstossArt.Randstunde => "Randstunde",
        VerstossArt.KeinNachmittag => "Nachmittag nicht erlaubt",
        VerstossArt.Verteilung => "Ungleichmäßige Verteilung",
        VerstossArt.HauptfachSpaet => "Hauptfach in später Stunde",
        VerstossArt.SpaeteStunde => "Späte Stunde",
        VerstossArt.ZeitwunschFach => "Zeitwunsch Fach",
        VerstossArt.ZeitwunschUnterricht => "Zeitwunsch Fach/Unterricht",
        VerstossArt.ZeitwunschRaum => "Zeitwunsch Raum",
        VerstossArt.KeinRaum => "Kein Raum (Raum/Ausweichraum belegt)",
        _ => a.ToString(),
    };
}
