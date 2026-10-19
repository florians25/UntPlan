using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Antwort auf „Räume mitnehmen oder belassen?“ beim Verlegen einer Stunde.</summary>
public enum RaumMitnahme
{
    /// <summary>Keine Rückfrage nötig: der bisherige Raum der Stunde wird – wenn möglich – mitgenommen.</summary>
    Automatisch,
    /// <summary>Die Stunde behält ihren bisherigen Raum auch zur neuen Zeit.</summary>
    Mitnehmen,
    /// <summary>Die Stunde nimmt den Raum, den die Klasse zur neuen Zeit regulär hat (sonst den Stammraum).</summary>
    Belassen,
}

/// <summary>Antwort auf die Frage, was geschehen soll, wenn der Raum zur neuen Zeit belegt bzw. gesperrt ist.</summary>
public enum RaumBelegtLoesung
{
    /// <summary>Keine Rückfrage gestellt: bei unerwarteter Belegung erhält die verlegte Stunde einen freien Raum.</summary>
    Automatisch,
    /// <summary>Verlegen und Raumvertretung für die verlegte Stunde (freier Raum, sonst offener Raum „???“).</summary>
    EigeneRaumvertretung,
    /// <summary>Die verlegte Stunde erhält den Raum, der dortige Unterricht eine Raumvertretung (freier Raum, sonst „???“).</summary>
    AndereRaumvertretung,
    /// <summary>Beide Stunden im selben Raum (Kollision wird angezeigt).</summary>
    Kollision,
}

/// <summary>Raumentscheidung für eine Verlegung (null bei <see cref="Tagesverlegung.Ausfuehren"/> = bisheriges Verhalten).</summary>
public sealed record VerlegeRaumWahl(RaumMitnahme Mitnahme = RaumMitnahme.Automatisch, RaumBelegtLoesung Belegt = RaumBelegtLoesung.Automatisch);

/// <summary>Eine verlegte Kopplungszeile: bisheriger Raum und der Raum, den das Ziel „natürlich“ hätte.</summary>
public sealed record VerlegeRaumZeile(int VonStunde, int NachStunde, int Zeile, int? Quellraum, int? Zielraum, IReadOnlyList<int> KlassenIds)
{
    /// <summary>Quell- und Zielraum sind bekannt und verschieden (dann wird „mitnehmen oder belassen?“ gefragt).</summary>
    public bool Unterschiedlich => Quellraum is int q && Zielraum is int z && q != z;

    /// <summary>Raum der verlegten Stunde nach der Antwort (null = nichts festlegen).</summary>
    public int? Raum(RaumMitnahme m) => m == RaumMitnahme.Belassen ? Zielraum ?? Quellraum : Quellraum;
}

/// <summary>Der gewählte Raum ist zur neuen Zeit belegt (Unterricht) bzw. gesperrt.</summary>
public sealed record VerlegeRaumKonflikt(int Stunde, int RaumId, bool Gesperrt, IReadOnlyList<TagesEintrag> Belegung);

/// <summary>
/// Räume beim Verlegen (tagesaktueller Stundenplan): Unterscheiden sich der bisherige Raum der Stunde und der Raum, den die
/// Klasse zur neuen Zeit regulär hat, wird gefragt, ob der Raum mitgenommen oder belassen wird. Ist der gewählte Raum zur neuen
/// Zeit belegt oder gesperrt, entscheidet der Benutzer: Raumvertretung für die verlegte Stunde, Raumvertretung für den
/// anderen Unterricht oder Kollision.
/// </summary>
public static class Verlegungsraeume
{
    private static int? Uid(TagesEintrag e) => e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;

    /// <summary>Verlegte Kopplungszeilen mit Quell- und Zielraum (vor dem Ausführen berechnen).</summary>
    public static List<VerlegeRaumZeile> Zeilen(Schuldatei d, VerlegeAnalyse a)
    {
        var tpVon = Tagesplan.Berechnen(d, a.Von);
        var tp = a.Von == a.Ziel ? tpVon : Tagesplan.Berechnen(d, a.Ziel);
        var res = new List<VerlegeRaumZeile>();
        foreach (var s in a.Schritte)
            foreach (var e in tpVon.Stunde(s.VonStunde).Where(e => e.Findetstatt && Raumvertretungen.Gehoert(e, a.UnterrichtId, a.Zeile)))
            {
                var zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
                if (res.Any(x => x.VonStunde == s.VonStunde && x.Zeile == zeile)) continue;
                res.Add(new VerlegeRaumZeile(s.VonStunde, s.NachStunde, zeile, e.RaumIds.Count > 0 ? e.RaumIds[0] : null,
                    NatuerlicherRaum(d, tp, s.NachStunde, a.UnterrichtId, e.KlassenIds), e.KlassenIds.ToList()));
            }
        return res;
    }

    /// <summary>
    /// Raum, den die Klasse(n) zur Zielstunde regulär hätten: Raum des regulären Unterrichts der Klasse in dieser Stunde, sonst
    /// der Stammraum. Null, wenn nichts bekannt ist.
    /// </summary>
    public static int? NatuerlicherRaum(Schuldatei d, Tagesplan tp, int stunde, int unterrichtId, IReadOnlyCollection<int> klassen)
    {
        if (klassen.Count == 0) return null;
        var regel = tp.Regel.Where(r => r.Stunde == stunde && r.Unterricht.Id != unterrichtId && r.RaumId is int && r.KlassenIds.Intersect(klassen).Any())
            .Select(r => r.RaumId).FirstOrDefault();
        return regel ?? Raumvertretungen.Stammraum(d, klassen);
    }

    /// <summary>Muss „Räume mitnehmen oder belassen?“ gefragt werden?</summary>
    public static bool MitnahmeFrageNoetig(IEnumerable<VerlegeRaumZeile> zeilen) => zeilen.Any(z => z.Unterschiedlich);

    /// <summary>Unterricht, der die Zielstunden durch die Verlegung verlässt (Tauschpartner, verdrängter Unterricht).</summary>
    private static HashSet<int> Weichende(VerlegeAnalyse a, VerlegeModus modus)
    {
        var weg = new HashSet<int>();
        if (modus == VerlegeModus.Tauschen && a.TauschUnterricht is int b) weg.Add(b);
        if (modus == VerlegeModus.Verdraengen)
            foreach (var h in a.Schritte.SelectMany(s => s.Hindernisse)) if (h.Unterricht?.Id is int u) weg.Add(u);
        return weg;
    }

    /// <summary>Ist der gewählte Raum zur neuen Zeit belegt bzw. gesperrt? (vor dem Ausführen, für die Rückfrage)</summary>
    public static List<VerlegeRaumKonflikt> Konflikte(Schuldatei d, VerlegeAnalyse a, VerlegeModus modus, RaumMitnahme mitnahme, IReadOnlyList<VerlegeRaumZeile>? zeilen = null)
    {
        zeilen ??= Zeilen(d, a);
        var tp = Tagesplan.Berechnen(d, a.Ziel);
        var weg = Weichende(a, modus);
        var res = new List<VerlegeRaumKonflikt>();
        foreach (var z in zeilen)
        {
            if (z.Raum(mitnahme) is not int r || res.Any(k => k.Stunde == z.NachStunde && k.RaumId == r)) continue;
            var belegung = tp.Stunde(z.NachStunde).Where(e => e.Findetstatt && e.RaumIds.Contains(r) && Uid(e) != a.UnterrichtId && !(Uid(e) is int u && weg.Contains(u))
                                                              // Lücke einer kranken Lehrkraft, die die verlegte Stunde füllt, gibt den Raum frei
                                                              && !(Tagesverlegung.IstLuecke(e) && e.KlassenIds.Count > 0 && e.KlassenIds.All(z.KlassenIds.Contains))).ToList();
            var gesperrt = Absenzen.RaumGesperrt(d, r, a.Ziel, z.NachStunde);
            if (belegung.Count > 0 || gesperrt) res.Add(new VerlegeRaumKonflikt(z.NachStunde, r, gesperrt, belegung));
        }
        return res;
    }

    /// <summary>Fragetext „Räume mitnehmen oder belassen?“ mit bisherigem und Zielraum je Stunde.</summary>
    public static string MitnahmeText(Schuldatei d, VerlegeAnalyse a, IEnumerable<VerlegeRaumZeile> zeilen)
    {
        var ix = new DatenIndex(d);
        var liste = zeilen.Where(z => z.Unterschiedlich).Select(z =>
            $"{z.NachStunde}. Stunde ({ix.KKlassen(z.KlassenIds)}): bisher {ix.KRaum(z.Quellraum!.Value)}, zur neuen Zeit regulär {ix.KRaum(z.Zielraum!.Value)}").Distinct();
        return $"Die Stunde wird auf den {a.Ziel.Day}.{a.Ziel.Month}. verlegt. Dort gilt ein anderer Raum:\n\n{string.Join("\n", liste)}\n\n" +
               "Räume mitnehmen oder belassen?\n• Mitnehmen: die Stunde behält ihren bisherigen Raum.\n• Belassen: die Stunde findet im Raum statt, der zur neuen Zeit gilt.";
    }

    /// <summary>Fragetext bei belegtem bzw. gesperrtem Raum.</summary>
    public static string KonfliktText(Schuldatei d, DateOnly ziel, IEnumerable<VerlegeRaumKonflikt> konflikte)
    {
        var ix = new DatenIndex(d);
        var teile = new List<string>();
        foreach (var k in konflikte)
        {
            var raum = d.Raeume.FirstOrDefault(r => r.Id == k.RaumId);
            if (raum is null) continue;
            if (k.Gesperrt) teile.Add($"Der Raum {raum.Kurzname} ist am {ziel.Day}.{ziel.Month}. in der {k.Stunde}. Stunde gesperrt.");
            if (k.Belegung.Count > 0) teile.Add(Raumvertretungen.KollisionsText(d, raum, ziel, k.Stunde, k.Belegung));
        }
        return string.Join("\n\n", teile) + "\n\nWas soll geschehen?";
    }

    /// <summary>Kann der andere Unterricht weichen? (nur bei Belegung durch Unterricht, nicht bei gesperrtem Raum)</summary>
    public static bool AndereRaumvertretungMoeglich(IEnumerable<VerlegeRaumKonflikt> konflikte) => konflikte.All(k => !k.Gesperrt && k.Belegung.Count > 0);

    /// <summary>Nach dem Verlegen: Räume laut Wahl setzen (auch für den Tauschpartner).</summary>
    internal static void Anwenden(Schuldatei d, VerlegeAnalyse a, VerlegeModus modus, VerlegeRaumWahl wahl, IReadOnlyList<VerlegeRaumZeile> zeilen,
        IReadOnlyList<VerlegeRaumZeile> partner, string benutzer)
    {
        foreach (var z in zeilen)
        {
            if (z.Raum(wahl.Mitnahme) is not int r) continue;
            var v = Ziel(d, a.Ziel, z.NachStunde, a.UnterrichtId, z.Zeile);
            if (v != null) RaumSetzen(d, v, r, wahl.Belegt, benutzer);
        }
        if (modus != VerlegeModus.Tauschen || a.TauschUnterricht is not int b) return;
        // Tauschpartner: „Mitnehmen“ = behält seinen Raum, „Belassen“ = übernimmt den Raum der Ursprungsposition
        foreach (var z in partner)
        {
            if (z.Raum(wahl.Mitnahme) is not int r) continue;
            var v = Ziel(d, a.Von, z.NachStunde, b, z.Zeile);
            if (v != null) RaumSetzen(d, v, r, wahl.Belegt, benutzer);
        }
    }

    /// <summary>Kopplungszeilen des Tauschpartners (an den Zielstunden; Ziel = Ursprungsposition, natürlicher Raum = bisheriger Raum der gezogenen Stunde).</summary>
    internal static List<VerlegeRaumZeile> PartnerZeilen(Schuldatei d, VerlegeAnalyse a, IReadOnlyList<VerlegeRaumZeile> eigene)
    {
        var res = new List<VerlegeRaumZeile>();
        if (a.TauschUnterricht is not int b) return res;
        var tp = Tagesplan.Berechnen(d, a.Ziel);
        foreach (var s in a.Schritte)
        {
            var quelle = eigene.FirstOrDefault(z => z.VonStunde == s.VonStunde)?.Quellraum;
            foreach (var e in tp.Stunde(s.NachStunde).Where(e => e.Findetstatt && Uid(e) == b))
            {
                var zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
                if (res.Any(x => x.VonStunde == s.NachStunde && x.Zeile == zeile)) continue;
                res.Add(new VerlegeRaumZeile(s.NachStunde, s.VonStunde, zeile, e.RaumIds.Count > 0 ? e.RaumIds[0] : null, quelle, e.KlassenIds.ToList()));
            }
        }
        return res;
    }

    private static Vertretung? Ziel(Schuldatei d, DateOnly datum, int stunde, int unterrichtId, int zeile) =>
        d.Vertretungen.FirstOrDefault(v => v.Datum == datum && v.Stunde == stunde && v.UnterrichtId == unterrichtId && v.ZeilenIndex == zeile &&
                                           v.Art is VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch);

    /// <summary>
    /// Raum einer verlegten Stunde setzen und eine Belegung laut <paramref name="loesung"/> auflösen. Liefert die Zeilen, die eine
    /// Raumvertretung erhalten haben (die verlegte Stunde selbst bzw. der verdrängte Unterricht).
    /// </summary>
    public static List<Vertretung> RaumSetzen(Schuldatei d, Vertretung v, int raumId, RaumBelegtLoesung loesung, string benutzer = "")
    {
        var res = new List<Vertretung>();
        v.VertretungsraumId = raumId;
        v.RaumVerdraengt = false;
        v.ZusatzraumIds.Remove(raumId);
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var belegung = tp.Stunde(v.Stunde).Where(e => e.Findetstatt && e.Vertretung != v && e.RaumIds.Contains(raumId) && Uid(e) != v.UnterrichtId).ToList();
        var gesperrt = Absenzen.RaumGesperrt(d, raumId, v.Datum, v.Stunde);
        if ((belegung.Count == 0 && !gesperrt) || loesung == RaumBelegtLoesung.Kollision) return res;
        if (loesung == RaumBelegtLoesung.AndereRaumvertretung && !gesperrt)
        {
            foreach (var h in belegung)
            {
                var w = h.Vertretung ?? (h.Regel != null ? Raumvertretungen.ZeileFuer(d, tp, h, benutzer) : null);
                if (w is null) continue;
                w.ZusatzraumIds.Remove(raumId);
                // Raum vorübergehend freigeben, damit der Vorschlag den Raum nicht als eigenen zählt
                w.VertretungsraumId = null;
                w.RaumVerdraengt = true;
                var frei = FreierRaum(d, w, raumId);
                w.VertretungsraumId = frei;
                w.RaumVerdraengt = frei is null;
                if (w.Art == VertretungsArt.Vertretung && w.VertreterId != null && w.VertreterId == w.LehrerId) w.Art = VertretungsArt.Raumvertretung;
                w.Bearbeitet = true;
                Vertretungen.AlsGeaendertMarkieren(w, benutzer);
                res.Add(w);
            }
            return res;
        }
        // eigene Raumvertretung: freier Raum, sonst offen („???“)
        v.VertretungsraumId = null;
        v.RaumVerdraengt = true;
        var raum = FreierRaum(d, v, raumId);
        v.VertretungsraumId = raum;
        v.RaumVerdraengt = raum is null;
        v.Bearbeitet = true;
        res.Add(v);
        return res;
    }

    /// <summary>Freier Raum für eine Stunde: Ausweichraum des belegten Raumes, Ausweichraum des Stammraumes, sonst passende Kapazität.</summary>
    public static int? FreierRaum(Schuldatei d, Vertretung v, int? belegterRaum)
    {
        var liste = Raumvertretungen.MoeglicheRaeume(d, v.Datum, new[] { v.Stunde }, e => e.Vertretung == v, v.NeueKlassenIds, belegterRaum ?? v.RaumId, false)
            .Where(r => r.Raum.Id != belegterRaum).ToList();
        return (liste.FirstOrDefault(r => r.Ausweichraum) ?? liste.FirstOrDefault(r => r.AusweichStammraum)
                ?? liste.Where(r => r.KapazitaetDiff is null or >= 0).OrderBy(r => r.KapazitaetDiff ?? int.MaxValue).FirstOrDefault()
                ?? liste.FirstOrDefault())?.Raum.Id;
    }
}
