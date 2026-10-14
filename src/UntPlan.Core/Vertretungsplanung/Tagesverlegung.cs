using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Was beim Ablegen einer gezogenen Stunde im tagesaktuellen Stundenplan geschehen soll.</summary>
public enum VerlegeModus
{
    /// <summary>Nur dorthin verlegen (kollisionsfreies Ziel).</summary>
    Verlegen,
    /// <summary>Mit dortiger Unterrichtsstunde tauschen.</summary>
    Tauschen,
    /// <summary>Behindernden Unterricht verdrängen (vormerken).</summary>
    Verdraengen,
    /// <summary>Kollision erzeugen.</summary>
    Kollision,
}

/// <summary>Quelle und Ziel einer Blockstunde mit dem Unterricht, der das Ziel behindert.</summary>
public sealed record VerlegeSchritt(int VonStunde, int NachStunde, IReadOnlyList<TagesEintrag> Hindernisse);

/// <summary>Ergebnis der Prüfung vor dem Ablegen.</summary>
public sealed record VerlegeAnalyse(DateOnly Von, int VonStunde, int UnterrichtId, int? Zeile, DateOnly Ziel, int ZielStunde, IReadOnlyList<VerlegeSchritt> Schritte, bool TauschMoeglich, bool LehrerAbwesend)
{
    public bool Kollision => Schritte.Any(s => s.Hindernisse.Count > 0);
    public int? TauschUnterricht => TauschMoeglich ? Schritte[0].Hindernisse[0].Unterricht?.Id : null;

    /// <summary>
    /// Antwort „nur dorthin verlegen“ auf die Tauschfrage: eine einfache Verlegung, bei der die dortige (tauschbare) Stunde
    /// stehen bleibt – sie behindert die Verlegung nicht. Bleibt danach weiterer behindernder Unterricht übrig, folgt wie
    /// beim Ablegen auf ein belegtes Feld die Kollisionsfrage.
    /// </summary>
    public VerlegeAnalyse NurDorthin()
    {
        if (TauschUnterricht is not int t) return this;
        var schritte = Schritte.Select(s => s with { Hindernisse = s.Hindernisse.Where(h => h.Unterricht?.Id != t).ToList() }).ToList();
        return this with { Schritte = schritte, TauschMoeglich = false };
    }
}

/// <summary>
/// Stundenplan im Vertretungsmodus: Verlegen, Tauschen und Verdrängen per Drag &amp; Drop, Ablegen neben dem Plan
/// (Vormerkung) und Zurückziehen einer Vormerkung in den Plan (auch in eine andere Woche). Im Klassenplan wird die
/// ganze Kopplung verschoben (<c>zeile = null</c>), im Lehrerplan nur die Kopplungszeile des Lehrers.
/// </summary>
public static class Tagesverlegung
{
    private static List<TagesEintrag> Quelle(Tagesplan tp, int stunde, int unterrichtId, int? zeile) =>
        tp.Stunde(stunde).Where(e => e.Findetstatt && Raumvertretungen.Gehoert(e, unterrichtId, zeile)).ToList();

    private static List<int> LehrerVon(IEnumerable<TagesEintrag> q) =>
        q.SelectMany(e => e.Regel?.LehrerId is int l && e.Status != TagesStatus.Offen ? new[] { l } : e.LehrerIds.ToArray()).Distinct().ToList();

    /// <summary>Unterricht, der Lehrer oder Klassen in der Zielstunde bindet (Bereitschaften behindern nicht).</summary>
    public static List<TagesEintrag> Hindernisse(Schuldatei d, Tagesplan ziel, int stunde, int unterrichtId, IReadOnlyCollection<int> lehrer, IReadOnlyCollection<int> klassen) =>
        ziel.Stunde(stunde).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) != unterrichtId && !Vertretungen.IstBereitschaftsfach(d, e.FachId)
                                       && (e.LehrerIds.Intersect(lehrer).Any() || e.KlassenIds.Intersect(klassen).Any())).ToList();

    /// <summary>
    /// Status eines Zielfeldes beim Ziehen: Frei (kollisionsfrei, grün), TauschMoeglich (dortige Stunde kann an die
    /// Ursprungsposition getauscht werden), VorgabeVerletzt (Lehrer abwesend) oder Kollision.
    /// </summary>
    public static Planung.Feldstatus Zielstatus(Schuldatei d, Tagesplan von, int vonStunde, int unterrichtId, int? zeile, Tagesplan ziel, int zielStunde)
    {
        if (!d.Zeitraster.Verfuegbar(ziel.Tag, zielStunde) || !Kalender.IstUnterrichtstag(d, ziel.Datum)) return Planung.Feldstatus.Kollision;
        var q = Quelle(von, vonStunde, unterrichtId, zeile);
        if (q.Count == 0) return Planung.Feldstatus.Kollision;
        if (von.Datum == ziel.Datum && vonStunde == zielStunde) return Planung.Feldstatus.Kollision;
        var lehrer = LehrerVon(q);
        var klassen = q.SelectMany(e => e.KlassenIds).Distinct().ToList();
        var h = Hindernisse(d, ziel, zielStunde, unterrichtId, lehrer, klassen);
        if (h.Count > 0) return TauschPruefen(d, von, vonStunde, unterrichtId, h) ? Planung.Feldstatus.TauschMoeglich : Planung.Feldstatus.Kollision;
        if (lehrer.Any(l => Absenzen.LehrerAbwesend(d, l, ziel.Datum, zielStunde))) return Planung.Feldstatus.VorgabeVerletzt;
        return Planung.Feldstatus.Frei;
    }

    /// <summary>Tausch möglich: genau ein behindernder Unterricht, der an der Ursprungsposition kollisionsfrei stattfinden kann.</summary>
    private static bool TauschPruefen(Schuldatei d, Tagesplan von, int vonStunde, int unterrichtId, List<TagesEintrag> h)
    {
        if (h.Any(x => x.Unterricht is null) || h.Select(x => x.Unterricht!.Id).Distinct().Count() != 1) return false;
        var uid = h[0].Unterricht!.Id;
        if (h.Any(x => x.Regel?.Planstunde.Fixiert == true || x.Vertretung?.Fixiert == true)) return false;
        var lehrer = LehrerVon(h);
        var klassen = h.SelectMany(e => e.KlassenIds).Distinct().ToList();
        // an der Ursprungsposition darf nur der gezogene Unterricht stören
        var zurueck = Hindernisse(d, von, vonStunde, uid, lehrer, klassen).Where(x => (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) != unterrichtId).ToList();
        return zurueck.Count == 0 && !lehrer.Any(l => Absenzen.LehrerAbwesend(d, l, von.Datum, vonStunde));
    }

    /// <summary>Ablegen prüfen (ganzer Block: <paramref name="laenge"/> Stunden ab <paramref name="vonStunde"/>).</summary>
    public static VerlegeAnalyse Analysieren(Schuldatei d, DateOnly von, int vonStunde, int unterrichtId, int? zeile, int laenge, DateOnly ziel, int zielStunde)
    {
        var tpVon = Tagesplan.Berechnen(d, von);
        var tp = Tagesplan.Berechnen(d, ziel);
        var schritte = new List<VerlegeSchritt>();
        var abwesend = false;
        for (var i = 0; i < Math.Max(1, laenge); i++)
        {
            var q = Quelle(tpVon, vonStunde + i, unterrichtId, zeile);
            if (q.Count == 0) continue;
            var lehrer = LehrerVon(q);
            var klassen = q.SelectMany(e => e.KlassenIds).Distinct().ToList();
            var h = Hindernisse(d, tp, zielStunde + i, unterrichtId, lehrer, klassen);
            // bei Verschiebung im eigenen Block behindert der Unterricht sich nicht selbst
            schritte.Add(new VerlegeSchritt(vonStunde + i, zielStunde + i, h));
            abwesend |= lehrer.Any(l => Absenzen.LehrerAbwesend(d, l, ziel, zielStunde + i));
        }
        var tausch = schritte.Count == 1 && schritte[0].Hindernisse.Count > 0 && TauschPruefen(d, tpVon, schritte[0].VonStunde, unterrichtId, schritte[0].Hindernisse.ToList());
        return new VerlegeAnalyse(von, vonStunde, unterrichtId, zeile, ziel, zielStunde, schritte, tausch, abwesend);
    }

    /// <summary>Frage beim Ablegen auf eine Stunde mit Tauschmöglichkeit: „Was wollen Sie am 26.10. in der 4. Stunde machen?“</summary>
    public static string TauschFrage(Schuldatei d, VerlegeAnalyse a)
    {
        var ix = new DatenIndex(d);
        return $"Was wollen Sie am {a.Ziel.Day}.{a.Ziel.Month}. in der {a.ZielStunde}. Stunde machen?\n\n" + string.Join("\n", a.Schritte[0].Hindernisse.Select(h => UntText(ix, h)).Distinct());
    }

    /// <summary>Frage bei Kollision: „Am 30.10. in der 5. Stunde: Kollision in Zielstunde“.</summary>
    public static string KollisionsFrage(Schuldatei d, VerlegeAnalyse a)
    {
        var ix = new DatenIndex(d);
        return string.Join("\n\n", a.Schritte.Where(s => s.Hindernisse.Count > 0).Select(s =>
            $"Am {a.Ziel.Day}.{a.Ziel.Month}. in der {s.NachStunde}. Stunde: Kollision in Zielstunde\n\n" + string.Join("\n", s.Hindernisse.Select(h => UntText(ix, h)).Distinct())));
    }

    private static string UntText(DatenIndex ix, TagesEintrag h) =>
        $"Unt. {h.Unterricht?.Id.ToString() ?? "V" + h.Vertretung?.Id}: {string.Join(",", h.LehrerIds.Select(ix.KLehrer))}{(h.LehrerIds.Count > 0 ? ", " : "")}{ix.KKlassen(h.KlassenIds)},";

    /// <summary>Verlegung ausführen.</summary>
    public static void Ausfuehren(Schuldatei d, VerlegeAnalyse a, VerlegeModus modus, string benutzer = "")
    {
        if (a.Schritte.Count == 0) return;
        if (modus == VerlegeModus.Tauschen)
        {
            if (a.TauschUnterricht is not int b) throw new InvalidOperationException("Ein Tausch ist an dieser Position nicht möglich.");
            Vertretungen.Tauschen(d, a.UnterrichtId, a.Von, a.Schritte[0].VonStunde, b, a.Ziel, a.Schritte[0].NachStunde, benutzer);
            return;
        }
        if (modus == VerlegeModus.Verdraengen)
            foreach (var s in a.Schritte)
                foreach (var h in s.Hindernisse.Where(h => h.Unterricht != null).Select(h => h.Unterricht!.Id).Distinct())
                    Vormerken(d, a.Ziel, s.NachStunde, h, null, VormerkungsQuelle.Verdraengung, benutzer);
        // innerhalb desselben Tages in Richtung der eigenen Stunden: Reihenfolge so wählen, dass nichts überschrieben wird
        var reihe = a.Von == a.Ziel && a.ZielStunde > a.VonStunde ? a.Schritte.Reverse() : a.Schritte;
        foreach (var s in reihe) Vertretungen.Verlegen(d, a.UnterrichtId, a.Zeile, a.Von, s.VonStunde, a.Ziel, s.NachStunde, VertretungsArt.Verlegung, benutzer);
    }

    /// <summary>
    /// Stunde neben den Stundenplan legen (bzw. verdrängen): an der Position entfällt der Unterricht, die Stunde liegt als
    /// Vormerkung bereit. Eine bereits verlegte Stunde wird an ihrem Ursprung vorgemerkt.
    /// </summary>
    public static Vormerkung? Vormerken(Schuldatei d, DateOnly datum, int stunde, int unterrichtId, int? zeile, VormerkungsQuelle quelle = VormerkungsQuelle.Stundenplan, string benutzer = "")
    {
        var tp = Tagesplan.Berechnen(d, datum);
        var eintraege = Quelle(tp, stunde, unterrichtId, zeile);
        if (eintraege.Count == 0) return null;
        // bereits verlegte Stunde: Verlegung aufheben, Ursprung (Entfall) wird zur Vormerkung
        var verlegt = eintraege.Select(e => e.Vertretung).FirstOrDefault(v => v is { Art: VertretungsArt.Verlegung or VertretungsArt.Tausch, VerlegungsId: not null });
        if (verlegt != null && d.Vertretungen.FirstOrDefault(x => x != verlegt && x.VerlegungsId == verlegt.VerlegungsId) is { } ursprung)
        {
            foreach (var z in eintraege.Select(e => e.Vertretung).OfType<Vertretung>().Where(v => v.VerlegungsId == verlegt.VerlegungsId)) d.Vertretungen.Remove(z);
            ursprung.NachDatum = null;
            ursprung.NachStunde = null;
            ursprung.VerlegungsId = null;
            var vm1 = Vertretungen.AlsVormerkung(d, ursprung, benutzer);
            vm1.Quelle = quelle;
            return vm1;
        }
        Vertretungen.Entfallen(d, unterrichtId, zeile, datum, stunde, benutzer);
        var v = d.Vertretungen.Where(x => x.Datum == datum && x.Stunde == stunde && x.UnterrichtId == unterrichtId && (zeile is null || x.ZeilenIndex == zeile) && x.Art == VertretungsArt.Entfall).ToList();
        if (v.Count == 0) return null;
        var vm = Vertretungen.AlsVormerkung(d, v[0], benutzer);
        vm.Quelle = quelle;
        // weitere Kopplungszeilen derselben Stunde gehören zur selben Vormerkung
        foreach (var w in v.Skip(1))
        {
            w.Art = VertretungsArt.Vormerkung;
            w.VormerkungId = vm.Id;
            if (w.LehrerId is int l && !vm.LehrerIds.Contains(l)) vm.LehrerIds.Add(l);
            foreach (var k in w.KlassenIds.Where(k => !vm.KlassenIds.Contains(k))) vm.KlassenIds.Add(k);
        }
        return vm;
    }

    /// <summary>
    /// Vormerkung zurück in den Plan ziehen. Stammt sie aus einer Unterrichtsstunde, entsteht eine Verlegung (am Ursprung
    /// Entfall, am Ziel Verlegung) – auch in eine andere Woche. Sonst wird sie als Sondereinsatz verplant.
    /// </summary>
    public static Vertretung VormerkungVerplanen(Schuldatei d, Vormerkung vm, DateOnly ziel, int stunde, string benutzer = "")
    {
        var ursprung = d.Vertretungen.Where(v => v.VormerkungId == vm.Id && v.Art == VertretungsArt.Vormerkung && v.UnterrichtId.HasValue).OrderBy(v => v.ZeilenIndex).ToList();
        if (ursprung.Count == 0 || vm.Offen <= 0) return Vertretungen.VormerkungVerplanen(d, vm, ziel, stunde, benutzer);
        var tp = Tagesplan.Berechnen(d, ziel);
        var vid = d.NeueId();
        Vertretung? erste = null;
        foreach (var q in ursprung)
        {
            q.Art = VertretungsArt.Entfall;
            q.NachDatum = ziel;
            q.NachStunde = stunde;
            q.VerlegungsId = vid;
            q.Bearbeitet = true;
            var lehrer = q.LehrerId is int l && !Absenzen.LehrerAbwesend(d, l, ziel, stunde) ? l : (int?)null;
            var z = new Vertretung
            {
                Id = d.NeueId(), Datum = ziel, Stunde = stunde, Art = VertretungsArt.Verlegung, Quelle = VertretungsQuelle.Verlegung, UnterrichtId = q.UnterrichtId,
                ZeilenIndex = q.ZeilenIndex, LehrerId = q.LehrerId, VertreterId = lehrer, FachId = q.FachId, VertretungsfachId = q.VertretungsfachId, KlassenIds = q.KlassenIds.ToList(),
                RaumId = q.RaumId, VonDatum = q.Datum, VonStunde = q.Stunde, VerlegungsId = vid, VormerkungId = vm.Id, Bearbeitet = true, Benutzer = benutzer, Schuelergruppe = q.Schuelergruppe,
            };
            var raum = q.VertretungsraumId ?? q.RaumId;
            z.VertretungsraumId = raum is int r && !tp.Stunde(stunde).Any(e => e.Findetstatt && e.RaumIds.Contains(r)) && !Absenzen.RaumGesperrt(d, r, ziel, stunde) ? r : null;
            d.Vertretungen.Add(z);
            erste ??= z;
        }
        vm.VerplantAm.Add(ziel);
        return erste!;
    }
}
