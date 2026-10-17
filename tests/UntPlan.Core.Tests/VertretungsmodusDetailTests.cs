using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Raumvertretung, Raum zuordnen (mit Tausch), Vertretungsautomatik, Verlegen/Tauschen/Verdrängen, Stunde ändern und Stundenplanänderungen aus UntPlanWeb.</summary>
public class VertretungsmodusDetailTests
{
    private static (Schuldatei d, DateOnly tag) Daten()
    {
        var d = Testdaten.Verplant();
        return (d, Testdaten.Unterrichtstag(d));
    }

    private static List<RegelStunde> Normale(Schuldatei d, DateOnly tag) =>
        Tagesplan.Regulaer(d, tag).Where(r => r.LehrerId.HasValue && r.RaumId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1 &&
                                              string.IsNullOrEmpty(r.Unterricht.Band)).ToList();

    private static TagesEintrag Eintrag(Schuldatei d, DateOnly tag, int stunde, int uid) =>
        Tagesplan.Berechnen(d, tag).Stunde(stunde).Single(e => e.Findetstatt && e.Unterricht?.Id == uid);

    [Fact]
    public void RaumabsenzErzeugtOffeneRaumvertretungUndAusweichraumWirdVorgeschlagen()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var raum = d.Raeume.First(x => x.Id == r.RaumId);
        var tp = Tagesplan.Berechnen(d, tag);
        var frei = d.Raeume.First(x => x.Id != raum.Id && !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        raum.AusweichraumId = frei.Id;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = raum.Id, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.Stunde == r.Stunde && x.UnterrichtId == r.Unterricht.Id);
        Assert.True(v.RaumOffen);
        var liste = Raumvertretungen.MoeglicheRaeume(d, v, false, false);
        Assert.DoesNotContain(liste, x => x.Raum.Id == raum.Id);
        Assert.True(liste.Single(x => x.Raum.Id == frei.Id).Ausweichraum);
        Assert.Contains(Raumvertretungen.MoeglicheRaeume(d, v, true, false), x => x.Raum.Id == raum.Id && x.Besetzt);
        // Automatik „Raumvertretungen (nur Ausweichräume)“
        Assert.True(Vertretungsautomatik.Zaehlen(d, tag, tag).Raumvertretungen >= 1);
        var n = Vertretungsautomatik.Ausfuehren(d, tag, tag, AutomatikMethode.Raumvertretungen);
        Assert.True(n >= 1);
        Assert.Equal(frei.Id, v.VertretungsraumId);
        Assert.False(v.RaumOffen);
        Assert.Contains(Tagesplan.Berechnen(d, tag).Stunde(r.Stunde), e => e.Vertretung == v && e.RaumIds.Contains(frei.Id));
    }

    [Fact]
    public void KapazitaetsdifferenzNegativWennRaumZuKlein()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var klasse = d.Klassen.First(k => k.Id == r.KlassenIds[0]);
        klasse.SchuelerMaennlich = 15;
        klasse.SchuelerWeiblich = 7;
        var klein = d.Raeume.First(x => x.Id != r.RaumId);
        klein.Kapazitaet = 20;
        Assert.Equal(22, Raumvertretungen.BenoetigteKapazitaet(d, r.KlassenIds, r.RaumId));
        var v = new Vertretung { Datum = tag, Stunde = r.Stunde, UnterrichtId = r.Unterricht.Id, KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId };
        Assert.Equal(-2, Raumvertretungen.MoeglicheRaeume(d, v, true, false).Single(x => x.Raum.Id == klein.Id).KapazitaetDiff);
    }

    private static (RegelStunde A, RegelStunde B) ZweiInGleicherStunde(Schuldatei d, DateOnly tag) =>
        Normale(d, tag).GroupBy(x => x.Stunde).Where(g => g.Select(x => x.RaumId).Distinct().Count() >= 2).Select(g => (g.First(), g.First(x => x.RaumId != g.First().RaumId))).First();

    [Fact]
    public void RaumMitKollisionOhneKollisionUndTausch()
    {
        // Räume tauschen: zwei Raumvertretungen
        var (d, tag) = Daten();
        var (a, b) = ZweiInGleicherStunde(d, tag);
        Raumvertretungen.Zuordnen(d, a.Unterricht.Id, a.ZeilenIndex, tag, new[] { a.Stunde }, b.RaumId, false, RaumKollisionsLoesung.RaeumeTauschen);
        Assert.Equal(new[] { b.RaumId!.Value }, Eintrag(d, tag, a.Stunde, a.Unterricht.Id).RaumIds);
        Assert.Equal(new[] { a.RaumId!.Value }, Eintrag(d, tag, b.Stunde, b.Unterricht.Id).RaumIds);
        Assert.Equal(2, d.Vertretungen.Count(v => v.Datum == tag && v.Stunde == a.Stunde && v.Art == VertretungsArt.Raumvertretung));

        // ohne Kollision: der andere Unterricht verliert den Raum (offen)
        (d, tag) = Daten();
        Raumvertretungen.Zuordnen(d, a.Unterricht.Id, a.ZeilenIndex, tag, new[] { a.Stunde }, b.RaumId, false, RaumKollisionsLoesung.OhneKollision);
        var eb = Eintrag(d, tag, b.Stunde, b.Unterricht.Id);
        Assert.Empty(eb.RaumIds);
        Assert.True(eb.Vertretung!.RaumOffen);

        // mit Kollision: beide im selben Raum
        (d, tag) = Daten();
        Raumvertretungen.Zuordnen(d, a.Unterricht.Id, a.ZeilenIndex, tag, new[] { a.Stunde }, b.RaumId, false, RaumKollisionsLoesung.MitKollision);
        Assert.Equal(2, Tagesplan.Berechnen(d, tag).Stunde(a.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(b.RaumId!.Value)));

        // nicht verplanen: nichts ändert sich
        (d, tag) = Daten();
        Raumvertretungen.Zuordnen(d, a.Unterricht.Id, a.ZeilenIndex, tag, new[] { a.Stunde }, b.RaumId, false, RaumKollisionsLoesung.NichtVerplanen);
        Assert.Empty(d.Vertretungen);
    }

    [Fact]
    public void ZusaetzlicherRaumBehaeltDenBisherigen()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var tp = Tagesplan.Berechnen(d, tag);
        var frei = d.Raeume.First(x => !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        Raumvertretungen.Zuordnen(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, frei.Id, true, RaumKollisionsLoesung.MitKollision);
        var e = Eintrag(d, tag, r.Stunde, r.Unterricht.Id);
        Assert.Contains(r.RaumId!.Value, e.RaumIds);
        Assert.Contains(frei.Id, e.RaumIds);
    }

    [Fact]
    public void AutomatikMethodenAllePausenaufsichtenFreigesetzte()
    {
        var (d, tag) = Daten();
        var ix = new DatenIndex(d);
        var lehrer = Tagesplan.Regulaer(d, tag).Where(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaft(ix, x.Unterricht)).GroupBy(x => x.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var vorher = Vertretungsautomatik.Zaehlen(d, tag, tag);
        Assert.True(vorher.Vertretungen > 0);
        // Pausenaufsichten bearbeitet keine Unterrichtsvertretungen
        Vertretungsautomatik.Ausfuehren(d, tag, tag, AutomatikMethode.Pausenaufsichten);
        Assert.Equal(vorher.Vertretungen, Vertretungsautomatik.Zaehlen(d, tag, tag).Vertretungen);
        // Freigesetzte: nur Bereitschaft oder freigesetzt (keine Mehrarbeit)
        var offen = d.Vertretungen.Where(v => v.Datum == tag && v.Offen && v.Art != VertretungsArt.Pausenaufsicht).ToList();
        Vertretungsautomatik.Ausfuehren(d, tag, tag, AutomatikMethode.Freigesetzte);
        Assert.All(offen.Where(v => v.VertreterId.HasValue), v => Assert.False(v.Mehrarbeit));
        // Alle: danach keine offene Vertretung mehr, für die der Vorschlag jemanden findet
        var fortschritt = new List<string>();
        Vertretungsautomatik.Ausfuehren(d, tag, tag, AutomatikMethode.BesterVorschlag, (v, i, n) => fortschritt.Add(Vertretungsautomatik.Beschreibung(d, v)));
        Assert.All(d.Vertretungen.Where(v => v.Datum == tag && v.Offen && v.Art != VertretungsArt.Pausenaufsicht), v => Assert.DoesNotContain(VertretungsVorschlag.Berechnen(d, v), k => !k.Rot));
        Assert.All(fortschritt, t => Assert.StartsWith("Vertreter statt:", t));
    }

    [Fact]
    public void AutomatikKopplungslehrerBetreut()
    {
        var (d, tag) = Daten();
        var r = Tagesplan.Regulaer(d, tag).FirstOrDefault(x => x.Unterricht.Zeilen.Count > 1 && x.LehrerId.HasValue && x.Unterricht.Zeilen.Select(z => z.LehrerId).Distinct().Count() > 1 &&
                                                                 !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        if (r is null) return; // Beispielschule ohne Kopplung
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag, StundeVon = r.Stunde, StundeBis = r.Stunde });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.Stunde == r.Stunde && x.UnterrichtId == r.Unterricht.Id && x.ZeilenIndex == r.ZeilenIndex);
        if (!v.Offen) return;
        Vertretungsautomatik.Ausfuehren(d, tag, tag, AutomatikMethode.Kopplungslehrer);
        if (v.VertreterId is int l)
        {
            Assert.Equal(VertretungsArt.Betreuung, v.Art);
            Assert.Contains(l, r.Unterricht.LehrerIds);
        }
    }

    private static (RegelStunde R, int Frei) StundeMitFreierZielstunde(Schuldatei d, DateOnly tag)
    {
        var tp = Tagesplan.Berechnen(d, tag);
        foreach (var r in Normale(d, tag))
            for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
                if (s != r.Stunde && Tagesverlegung.Zielstatus(d, tp, r.Stunde, r.Unterricht.Id, null, tp, s) == Planung.Feldstatus.Frei) return (r, s);
        throw new InvalidOperationException("keine freie Zielstunde");
    }

    [Fact]
    public void VerlegenVormerkenUndInAndereWocheZurueckziehen()
    {
        var (d, tag) = Daten();
        var (r, frei) = StundeMitFreierZielstunde(d, tag);
        var a = Tagesverlegung.Analysieren(d, tag, r.Stunde, r.Unterricht.Id, null, 1, tag, frei);
        Assert.False(a.Kollision);
        Tagesverlegung.Ausfuehren(d, a, VerlegeModus.Verlegen);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == frei && v.Art == VertretungsArt.Verlegung);
        // verlegte Stunde neben den Plan legen → Vormerkung, Verlegung aufgehoben
        var vm = Tagesverlegung.Vormerken(d, tag, frei, r.Unterricht.Id, null);
        Assert.NotNull(vm);
        Assert.Equal(1, vm!.Offen);
        Assert.DoesNotContain(Tagesplan.Berechnen(d, tag).Eintraege, e => e.Findetstatt && e.Unterricht?.Id == r.Unterricht.Id && (e.Stunde == frei || e.Stunde == r.Stunde));
        // in der nächsten Woche wieder einplanen → Entfall am Ursprung, Verlegung am Ziel
        var naechste = Kalender.NaechsterUnterrichtstag(d, tag.AddDays(6));
        var tpn = Tagesplan.Berechnen(d, naechste);
        var ziel = Enumerable.Range(1, d.Zeitraster.StundenProTag).First(s => Tagesverlegung.Hindernisse(d, tpn, s, r.Unterricht.Id, new[] { r.LehrerId!.Value }, r.KlassenIds.ToList()).Count == 0);
        var z = Tagesverlegung.VormerkungVerplanen(d, vm, naechste, ziel);
        Assert.Equal(VertretungsArt.Verlegung, z.Art);
        Assert.Equal(tag, z.VonDatum);
        Assert.Equal(0, vm.Offen);
        var q = d.Vertretungen.Single(v => v.Datum == tag && v.Stunde == r.Stunde && v.UnterrichtId == r.Unterricht.Id);
        Assert.Equal(VertretungsArt.Entfall, q.Art);
        Assert.Equal(naechste, q.NachDatum);
        Assert.Contains(Tagesplan.Berechnen(d, naechste).VonKlasse(r.KlassenIds[0]), e => e.Stunde == ziel && e.Unterricht?.Id == r.Unterricht.Id);
    }

    [Fact]
    public void TauschenUndVerdraengen()
    {
        var (d, tag) = Daten();
        var tp = Tagesplan.Berechnen(d, tag);
        // zwei Stunden derselben Klasse am selben Tag, die getauscht werden können
        (RegelStunde A, RegelStunde B)? paar = null;
        foreach (var a in Normale(d, tag))
        {
            foreach (var b in Normale(d, tag).Where(b => b.Stunde != a.Stunde && b.KlassenIds.SequenceEqual(a.KlassenIds) && b.Unterricht.Id != a.Unterricht.Id))
                if (Tagesverlegung.Zielstatus(d, tp, a.Stunde, a.Unterricht.Id, null, tp, b.Stunde) == Planung.Feldstatus.TauschMoeglich) { paar = (a, b); break; }
            if (paar != null) break;
        }
        Assert.NotNull(paar);
        var (x, y) = paar!.Value;
        var an = Tagesverlegung.Analysieren(d, tag, x.Stunde, x.Unterricht.Id, null, 1, tag, y.Stunde);
        Assert.True(an.TauschMoeglich);
        Assert.Contains("Was wollen Sie am", Tagesverlegung.TauschFrage(d, an));
        Tagesverlegung.Ausfuehren(d, an, VerlegeModus.Tauschen);
        var neu = Tagesplan.Berechnen(d, tag);
        Assert.Contains(neu.VonKlasse(x.KlassenIds[0]), e => e.Stunde == y.Stunde && e.Unterricht?.Id == x.Unterricht.Id);
        Assert.Contains(neu.VonKlasse(x.KlassenIds[0]), e => e.Stunde == x.Stunde && e.Unterricht?.Id == y.Unterricht.Id);

        // Verdrängen: der behindernde Unterricht wird vorgemerkt
        (d, tag) = Daten();
        an = Tagesverlegung.Analysieren(d, tag, x.Stunde, x.Unterricht.Id, null, 1, tag, y.Stunde);
        Assert.Contains("Kollision in Zielstunde", Tagesverlegung.KollisionsFrage(d, an));
        Tagesverlegung.Ausfuehren(d, an, VerlegeModus.Verdraengen);
        Assert.Contains(d.Vormerkungen, v => v.UnterrichtId == y.Unterricht.Id && v.Quelle == VormerkungsQuelle.Verdraengung);
        Assert.Contains(Tagesplan.Berechnen(d, tag).VonKlasse(x.KlassenIds[0]), e => e.Stunde == y.Stunde && e.Unterricht?.Id == x.Unterricht.Id);
        Assert.DoesNotContain(Tagesplan.Berechnen(d, tag).VonKlasse(x.KlassenIds[0]), e => e.Unterricht?.Id == y.Unterricht.Id && e.Stunde == y.Stunde);
    }

    [Fact]
    public void VerlegungEinerGeregeltenVertretung()
    {
        var (d, tag) = Daten();
        var (r, frei) = StundeMitFreierZielstunde(d, tag);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag, StundeVon = r.Stunde, StundeBis = r.Stunde });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.Stunde == r.Stunde && x.UnterrichtId == r.Unterricht.Id);
        var tp = Tagesplan.Berechnen(d, tag);
        var vertreter = d.Lehrer.First(l => l.Id != r.LehrerId && Vertretungen.LehrerBelegt(d, tp, l.Id, r.Stunde) is null && Vertretungen.LehrerBelegt(d, tp, l.Id, frei) is null);
        Vertretungen.VertreterSetzen(d, v, vertreter.Id);
        var a = Tagesverlegung.Analysieren(d, tag, r.Stunde, r.Unterricht.Id, null, 1, tag, frei);
        Tagesverlegung.Ausfuehren(d, a, VerlegeModus.Kollision);
        Assert.Equal(VertretungsArt.Entfall, v.Art);
        Assert.Contains(d.Vertretungen, x => x.Datum == tag && x.Stunde == frei && x.Art == VertretungsArt.Verlegung && x.UnterrichtId == r.Unterricht.Id);
    }

    [Fact]
    public void StundeAendernKonsequenzen()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var tp = Tagesplan.Berechnen(d, tag);
        // Fach ändern → Unterricht geändert
        var anderesFach = d.Faecher.First(f => f.Id != r.FachId && !f.Bereitschaft);
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(r.LehrerId, anderesFach.Id, r.KlassenIds.ToList(), r.RaumId));
        var v = d.Vertretungen.Single(x => x.UnterrichtId == r.Unterricht.Id && x.Datum == tag);
        Assert.Equal(VertretungsArt.UnterrichtGeaendert, v.Art);
        Assert.Equal(anderesFach.Id, Eintrag(d, tag, r.Stunde, r.Unterricht.Id).FachId);

        // Lehrer ohne Unterricht → Sondereinsatz, alter Lehrer frei
        (d, tag) = Daten();
        var frei = Stundenaenderung.VerfuegbareLehrer(d, tag, new[] { r.Stunde }).First(l => l.Id != r.LehrerId);
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(frei.Id, r.FachId, r.KlassenIds.ToList(), r.RaumId));
        v = d.Vertretungen.Single(x => x.UnterrichtId == r.Unterricht.Id && x.Datum == tag);
        Assert.Equal(VertretungsArt.Sondereinsatz, v.Art);
        Assert.Equal(frei.Id, v.VertreterId);
        var neu = Tagesplan.Berechnen(d, tag);
        Assert.Contains(neu.VonLehrer(frei.Id), e => e.Stunde == r.Stunde);
        Assert.DoesNotContain(neu.VonLehrer(r.LehrerId!.Value), e => e.Stunde == r.Stunde && e.Unterricht?.Id == r.Unterricht.Id);

        // Raum ändern → Raumvertretung
        (d, tag) = Daten();
        var raum = Stundenaenderung.FreieRaeume(d, tag, new[] { r.Stunde }).First();
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(r.LehrerId, r.FachId, r.KlassenIds.ToList(), raum.Id));
        v = d.Vertretungen.Single(x => x.UnterrichtId == r.Unterricht.Id && x.Datum == tag);
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal(new[] { raum.Id }, Eintrag(d, tag, r.Stunde, r.Unterricht.Id).RaumIds);
    }

    [Fact]
    public void StundeAendernKlasseUndLehrerMitUnterricht()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var tp = Tagesplan.Berechnen(d, tag);
        // Klasse ändern: Entfall für alte Klasse, kollidierender Unterricht der neuen Klasse entfällt, „Unterricht geändert“
        var andere = tp.Stunde(r.Stunde).First(e => e.Findetstatt && e.Unterricht != null && e.Unterricht.Id != r.Unterricht.Id && e.KlassenIds.Count == 1 && !e.KlassenIds.Intersect(r.KlassenIds).Any());
        var neueKlasse = andere.KlassenIds[0];
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(r.LehrerId, r.FachId, new[] { neueKlasse }, r.RaumId));
        var zeilen = d.Vertretungen.Where(x => x.Datum == tag && x.Stunde == r.Stunde).ToList();
        Assert.Contains(zeilen, x => x.UnterrichtId == r.Unterricht.Id && x.Art == VertretungsArt.Entfall);
        Assert.Contains(zeilen, x => x.UnterrichtId == andere.Unterricht!.Id && x.Art == VertretungsArt.Entfall);
        var g = Assert.Single(zeilen, x => x.Art == VertretungsArt.UnterrichtGeaendert);
        Assert.Equal(new[] { neueKlasse }, g.NeueKlassenIds);
        var neu = Tagesplan.Berechnen(d, tag);
        Assert.Contains(neu.VonKlasse(neueKlasse), e => e.Stunde == r.Stunde && e.Unterricht?.Id == r.Unterricht.Id);
        Assert.DoesNotContain(neu.VonKlasse(r.KlassenIds[0]), e => e.Stunde == r.Stunde && e.Unterricht?.Id == r.Unterricht.Id);

        // Lehrer mit Unterricht: „im regulären Unterricht vertreten lassen“ legt dort eine offene Vertretung an
        (d, tag) = Daten();
        tp = Tagesplan.Berechnen(d, tag);
        var konflikt = Normale(d, tag).First(x => x.Stunde == r.Stunde && x.LehrerId != r.LehrerId && x.Unterricht.Id != r.Unterricht.Id);
        Assert.NotNull(Stundenaenderung.LehrerKonflikt(d, tag, r.Stunde, konflikt.LehrerId!.Value, r.Unterricht.Id));
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(konflikt.LehrerId, r.FachId, r.KlassenIds.ToList(), r.RaumId),
            LehrerKonfliktLoesung.RegulaerVertreten);
        Assert.Contains(d.Vertretungen, x => x.UnterrichtId == konflikt.Unterricht.Id && x.Datum == tag && x.Offen);
        Assert.Contains(d.Vertretungen, x => x.UnterrichtId == r.Unterricht.Id && x.VertreterId == konflikt.LehrerId);

        // Betreuung
        (d, tag) = Daten();
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(konflikt.LehrerId, r.FachId, r.KlassenIds.ToList(), r.RaumId),
            LehrerKonfliktLoesung.Betreuung);
        var b = d.Vertretungen.Single(x => x.UnterrichtId == r.Unterricht.Id);
        Assert.True(b.Mitbetreuung);
        Assert.Equal(VertretungsArt.Betreuung, b.Art);

        // nicht verplanen
        (d, tag) = Daten();
        Stundenaenderung.Ausfuehren(d, r.Unterricht.Id, r.ZeilenIndex, tag, new[] { r.Stunde }, new StundeAendernWerte(konflikt.LehrerId, r.FachId, r.KlassenIds.ToList(), r.RaumId));
        Assert.Empty(d.Vertretungen);
    }

    [Fact]
    public void Raumaenderung_Zusatzraum_loeschen_behaelt_den_urspruenglichen_Raum()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag).First(x => x.RaumId.HasValue);
        var tp = Tagesplan.Berechnen(d, tag);
        var original = d.Raeume.First(x => x.Id == r.RaumId);
        var zusatz = d.Raeume.First(x => x.Id != original.Id && !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        var lehrer = d.Lehrer.First(l => l.Id == r.LehrerId);
        var datum = tag.ToString("yyyy-MM-dd");
        WebRueckmeldung Buchung(string id, params string[] raeume) => new()
        {
            Id = id, Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "web", Lehrer = lehrer.Kurzname, Von = datum, StundeVon = r.Stunde, StundeBis = r.Stunde,
            UnterrichtNr = r.Unterricht.Id, Raeume = raeume.ToList(), Raum = raeume.FirstOrDefault(x => x != original.Kurzname),
        };
        // Zusatzraum hinzufügen (in der Reihenfolge der App: neuer Raum zuerst)
        var a1 = StundenplanAenderungen.Analysieren(d, new[] { Buchung("R1", zusatz.Kurzname, original.Kurzname) }, tag, tag.AddDays(14), false);
        StundenplanAenderungen.Uebernehmen(d, a1);
        var e1 = Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(x => x.Unterricht?.Id == r.Unterricht.Id && x.LehrerIds.Contains(lehrer.Id));
        Assert.Contains(original.Id, e1.RaumIds);
        Assert.Contains(zusatz.Id, e1.RaumIds);
        // Zusatzraum wieder löschen: der ursprüngliche Raum bleibt
        var a2 = StundenplanAenderungen.Analysieren(d, new[] { Buchung("R2", original.Kurzname) }, tag, tag.AddDays(14), false);
        StundenplanAenderungen.Uebernehmen(d, a2);
        var e2 = Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(x => x.Unterricht?.Id == r.Unterricht.Id && x.LehrerIds.Contains(lehrer.Id));
        Assert.Equal(new[] { original.Id }, e2.RaumIds);
    }

    [Fact]
    public void StundenplanAenderungenAusUntPlanWebOhneDoppelimport()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var tp = Tagesplan.Berechnen(d, tag);
        var freierRaum = d.Raeume.First(x => !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)) && !tp.Stunde(3).Any(e => e.RaumIds.Contains(x.Id)));
        var lehrer = d.Lehrer.First(l => l.Id == r.LehrerId);
        var freierLehrer = d.Lehrer.First(l => Vertretungen.LehrerBelegt(d, tp, l.Id, 3) is null && l.Id != lehrer.Id);
        var datum = tag.ToString("yyyy-MM-dd");
        var liste = new List<WebRueckmeldung>
        {
            new() { Id = "B510", Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "admin", Lehrer = lehrer.Kurzname, Von = datum, StundeVon = r.Stunde, StundeBis = r.Stunde, Raum = freierRaum.Kurzname },
            new() { Id = "B513", Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "admin", Lehrer = freierLehrer.Kurzname, Von = datum, StundeVon = 3, StundeBis = 3, Raum = freierRaum.Kurzname,
                Klassen = { d.Klassen[0].Kurzname }, Fach = d.Faecher[0].Kurzname },
            new() { Id = "E68", Typ = WebRueckmeldungsTypen.Klausur, Absender = "Arist", Lehrer = lehrer.Kurzname, Von = datum, StundeVon = 4, StundeBis = 4, Klassen = { d.Klassen[1].Kurzname }, Titel = "Mathe Schularbeit" },
            new() { Id = "N1", Typ = WebRueckmeldungsTypen.Nachricht, Text = "keine Stundenplanänderung" },
        };
        var zeilen = StundenplanAenderungen.Analysieren(d, liste, tag, tag.AddDays(14), false);
        Assert.Equal(3, zeilen.Count);
        Assert.Equal(StplAenderungsArt.Unterricht, zeilen.Single(z => z.Id == "B510").Art);
        Assert.Equal(StplAenderungsArt.Aktivitaet, zeilen.Single(z => z.Id == "B513").Art);
        Assert.Equal(StplAenderungsArt.Pruefung, zeilen.Single(z => z.Id == "E68").Art);
        var (ok, nein) = StundenplanAenderungen.Uebernehmen(d, zeilen.Where(z => z.Id != "B513"));
        Assert.Equal(new[] { "B510", "E68" }, ok.OrderBy(x => x));
        Assert.Empty(nein);
        var rv = d.Vertretungen.Single(v => v.WebId == "B510");
        Assert.Equal(VertretungsArt.Raumvertretung, rv.Art);
        Assert.Equal("", rv.Text);   // Raumänderung aus einer Buchung: kein Vertretungstext
        Assert.Equal(freierRaum.Id, rv.VertretungsraumId);
        var k = Assert.Single(d.Klausuren);
        Assert.True(k.Importiert);
        StundenplanAenderungen.Ignorieren(d, zeilen.Where(z => z.Id == "B513"));
        // erneuter Import: nichts mehr offen, bereits Erledigte nur mit Option sichtbar und mit Status
        Assert.Empty(StundenplanAenderungen.Analysieren(d, liste, tag, tag.AddDays(14), false));
        var alle = StundenplanAenderungen.Analysieren(d, liste, tag, tag.AddDays(14), true);
        Assert.Equal(3, alle.Count);
        Assert.True(alle.Single(z => z.Id == "B510").Uebernommen);
        Assert.False(alle.Single(z => z.Id == "B513").Uebernommen);
        var (ok2, _) = StundenplanAenderungen.Uebernehmen(d, alle);
        Assert.Empty(ok2);
        Assert.Single(d.Klausuren);
        Assert.Single(d.Vertretungen, v => v.WebId == "B510");
    }

    [Fact]
    public void RaumaenderungAusBuchungHatImDesktopUndImExportKeinenVertretungstext()
    {
        var (d, tag) = Daten();
        var r = Normale(d, tag)[0];
        var tp = Tagesplan.Berechnen(d, tag);
        var freierRaum = d.Raeume.First(x => !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        var lehrer = d.Lehrer.First(l => l.Id == r.LehrerId);
        var m = new WebRueckmeldung { Id = "B900", Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "lan", Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"),
            StundeVon = r.Stunde, StundeBis = r.Stunde, UnterrichtNr = r.Unterricht.Id, Raeume = { freierRaum.Kurzname }, Raum = freierRaum.Kurzname };
        var z = StundenplanAenderungen.Zeile(d, m);
        Assert.Equal(StplAenderungsArt.Unterricht, z.Art);
        var (ok, _) = StundenplanAenderungen.Uebernehmen(d, new[] { z }, "lan");
        Assert.Equal(new[] { "B900" }, ok);
        var v = Assert.Single(d.Vertretungen, x => x.WebId == "B900");
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal("", v.Text);
        // Desktop: Aushang-Bemerkung nur die Art, kein Vertretungstext
        var zeile = Vertretungsplan.Zeilen(d, tag).Single(x => x.Id == v.Id);
        Assert.Equal("", zeile.Text);
        Assert.Equal("Raumänderung", Vertretungsplan.Bemerkung(d, v));
        // Export: weder Vertretungszeile noch Stunde tragen einen Text
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        Assert.Equal("", paket.Vertretungen.Single(x => x.Id == v.Id).Text);
        Assert.All(paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd")).Stunden.Where(x => x.Vertretung == v.Id), x => Assert.Equal("", x.Text));

        // ältere Dateien mit dem automatischen Hinweis „(lan) Raumänderung“: wird nicht exportiert und nicht als Bemerkung gezeigt
        v.Text = "(lan) Raumänderung";
        Assert.True(StundenplanAenderungen.IstRaumaenderungsHinweis(v.Text));
        Assert.False(StundenplanAenderungen.IstRaumaenderungsHinweis("Raumänderung wegen Wasserschaden"));
        Assert.Equal("Raumänderung", Vertretungsplan.Bemerkung(d, v));
        paket = UntPlanWebExport.PaketErstellen(d, tag);
        Assert.Equal("", paket.Vertretungen.Single(x => x.Id == v.Id).Text);
        Assert.All(paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd")).Stunden.Where(x => x.Vertretung == v.Id), x => Assert.Equal("", x.Text));
        // ein eigener Text der Lehrkraft bleibt erhalten
        v.Text = "Bitte Laptops mitbringen";
        Assert.Equal("Bitte Laptops mitbringen", UntPlanWebExport.PaketErstellen(d, tag).Vertretungen.Single(x => x.Id == v.Id).Text);
    }

    [Fact]
    public void AktivitaetWirdSondereinsatz()
    {
        var (d, tag) = Daten();
        var tp = Tagesplan.Berechnen(d, tag);
        var lehrer = d.Lehrer.First(l => Vertretungen.LehrerBelegt(d, tp, l.Id, 2) is null && !tp.Eintraege.Any(e => e.LehrerIds.Contains(l.Id) && e.Stunde == 2));
        var raum = d.Raeume.First(x => !tp.Stunde(2).Any(e => e.RaumIds.Contains(x.Id)));
        var m = new WebRueckmeldung { Id = "B1", Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "admin", Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"), StundeVon = 2, Raum = raum.Kurzname, Klassen = { d.Klassen[0].Kurzname }, Fach = "GIBTSNICHTFACH" };
        var z = StundenplanAenderungen.Zeile(d, m);
        Assert.Equal(StplAenderungsArt.Aktivitaet, z.Art);
        StundenplanAenderungen.Uebernehmen(d, new[] { z });
        var v = Assert.Single(d.Vertretungen, x => x.WebId == "B1");
        Assert.Equal(VertretungsArt.Sondereinsatz, v.Art);
        Assert.Contains(Tagesplan.Berechnen(d, tag).VonLehrer(lehrer.Id), e => e.Stunde == 2);
    }
}
