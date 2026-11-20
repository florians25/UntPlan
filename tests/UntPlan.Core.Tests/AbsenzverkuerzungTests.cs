using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Lehrkraft früher wieder anwesend: Wird eine Absenz verkürzt oder gelöscht, werden Vertretungen, Entfälle, Verlegungen und
/// Vorziehungen der nicht mehr abwesenden Zeit rückgängig gemacht – mit Hinweis; Maßnahmen davor und ohne Bezug bleiben.
/// </summary>
public class AbsenzverkuerzungTests
{
    private sealed record Lage(Schuldatei D, DateOnly Tag1, DateOnly Tag2, DateOnly Tag3, int Lehrer, Abwesenheit Absenz);

    private static bool Geeignet(Schuldatei d, RegelStunde r) =>
        r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(r.Unterricht.Band)
        && !r.Unterricht.KeineVertretung && r.KlassenIds.Count > 0;

    /// <summary>Lehrkraft mit mindestens zwei Stunden am dritten und einer am ersten Tag, abwesend vom ersten bis zum dritten Tag.</summary>
    private static Lage Krank()
    {
        var d = Testdaten.Verplant();
        var tag1 = Testdaten.Unterrichtstag(d);
        var tag2 = Kalender.NaechsterUnterrichtstag(d, tag1);
        var tag3 = Kalender.NaechsterUnterrichtstag(d, tag2);
        var r3 = Tagesplan.Regulaer(d, tag3).Where(r => Geeignet(d, r)).ToList();
        var r1 = Tagesplan.Regulaer(d, tag1).Where(r => Geeignet(d, r)).ToList();
        var lehrer = r3.GroupBy(r => r.LehrerId!.Value).First(g => g.Select(r => r.Stunde).Distinct().Count() >= 2 && r1.Any(r => r.LehrerId == g.Key)).Key;
        var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag1, Bis = tag3, GrundId = d.Abwesenheitsgruende.FirstOrDefault()?.Id };
        var gespeichert = AbsenzVerwaltung.Speichern(d, new[] { a }, Array.Empty<Abwesenheit>());
        return new Lage(d, tag1, tag2, tag3, lehrer, gespeichert.Single());
    }

    private static List<Vertretung> Offene(Lage l, DateOnly tag) =>
        l.D.Vertretungen.Where(v => v.Datum == tag && v.LehrerId == l.Lehrer && v.AbwesenheitId == l.Absenz.Id && v.Offen && v.UnterrichtId.HasValue && v.GangId is null)
            .OrderBy(v => v.Stunde).ToList();

    /// <summary>Freie, anwesende Lehrkraft für eine Stunde.</summary>
    private static int FreierLehrer(Schuldatei d, Vertretung v, params int[] ausser)
    {
        var tp = Tagesplan.Berechnen(d, v.Datum);
        return d.Lehrer.First(x => x.Id != v.LehrerId && !ausser.Contains(x.Id) && !Absenzen.LehrerAbwesend(d, x.Id, v.Datum, v.Stunde)
                                   && Vertretungen.LehrerBelegt(d, tp, x.Id, v.Stunde, v) is null).Id;
    }

    private static Abwesenheit BisTag(Lage l, DateOnly bis)
    {
        var k = l.Absenz.Kopie();
        k.Bis = bis;
        return k;
    }

    [Fact]
    public void VerkuerzteAbsenzMachtVertretungUndEntfallDerFreienZeitRueckgaengig()
    {
        var l = Krank();
        var d = l.D;
        var offen3 = Offene(l, l.Tag3);
        Assert.True(offen3.Count >= 2);
        var vertretung = offen3[0];
        var vertreter = FreierLehrer(d, vertretung);
        Vertretungen.VertreterSetzen(d, vertretung, vertreter);
        var entfall = offen3[1];
        Vertretungen.EntfallUmschalten(d, entfall);
        Assert.True(entfall.IstEntfall);
        // Vertretung am ersten Tag bleibt (Lehrkraft dort weiterhin abwesend)
        var frueh = Offene(l, l.Tag1)[0];
        var vertreter1 = FreierLehrer(d, frueh);
        Vertretungen.VertreterSetzen(d, frueh, vertreter1);

        // Absenz endet nun am ersten Tag
        var neu = BisTag(l, l.Tag1);
        var vorher = System.Text.Json.JsonSerializer.Serialize(d.Vertretungen);
        var vorschau = Absenzverkuerzung.Ermitteln(d, new[] { neu }, new[] { l.Absenz });
        // Vorschau ändert nichts
        Assert.Equal(vorher, System.Text.Json.JsonSerializer.Serialize(d.Vertretungen));
        Assert.Contains(vorschau.Massnahmen, m => m.VertretungId == vertretung.Id && m.Art == Absenzverkuerzung.Massnahmenart.Vertretung && m.VertreterId == vertreter);
        Assert.Contains(vorschau.Massnahmen, m => m.VertretungId == entfall.Id && m.Art == Absenzverkuerzung.Massnahmenart.Entfall);
        Assert.DoesNotContain(vorschau.Massnahmen, m => m.Datum == l.Tag1);
        Assert.Contains(vertreter, vorschau.FreieVertreter);
        var r = Assert.Single(vorschau.Rueckkehr);
        Assert.Equal((l.Lehrer, l.Tag2, 1), (r.LehrerId, r.Datum, r.Stunde));
        var text = vorschau.HinweisText(d, vorschau: true);
        var kurz = d.Lehrer.First(x => x.Id == l.Lehrer).Kurzname;
        Assert.Contains($"werden rückgängig gemacht, weil {kurz} ab ", text);
        Assert.Contains("wieder anwesend ist", text);
        Assert.Contains("• Entfall: ", text);
        Assert.Contains($"Wieder frei: ", text);
        Assert.Contains(d.Lehrer.First(x => x.Id == vertreter).Kurzname, text);

        var (gespeichert, e) = Absenzverkuerzung.Speichern(d, new[] { neu }, new[] { l.Absenz });
        Assert.Equal(l.Tag1, Assert.Single(gespeichert).Bis);
        Assert.Equal(vorschau.Massnahmen.Count, e.Massnahmen.Count);
        Assert.Contains("wurden rückgängig gemacht", e.HinweisText(d, vorschau: false));
        // Tage 2 und 3: keine Zeilen der Lehrkraft mehr, der Vertreter ist frei, der Unterricht findet regulär statt
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum >= l.Tag2 && v.LehrerId == l.Lehrer);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == l.Tag3 && v.VertreterId == vertreter && v.Stunde == vertretung.Stunde);
        var tp = Tagesplan.Berechnen(d, l.Tag3);
        Assert.All(tp.Eintraege.Where(x => x.Regel?.LehrerId == l.Lehrer), x => Assert.Equal(TagesStatus.Regulaer, x.Status));
        // Tag 1 unverändert
        Assert.Contains(d.Vertretungen, v => v.Id == frueh.Id && v.VertreterId == vertreter1);
    }

    [Fact]
    public void VerlegungWegenAbsenzWirdAufgehobenUnabhaengigeBleibt()
    {
        var l = Krank();
        var d = l.D;
        var q = Offene(l, l.Tag3)[0];
        var klasse = q.KlassenIds[0];
        var tp = Tagesplan.Berechnen(d, l.Tag3);
        var frei = Enumerable.Range(1, 8).First(s => s != q.Stunde && !tp.VonKlasse(klasse).Any(e => e.Stunde == s));
        Vertretungen.Verlegen(d, q.UnterrichtId!.Value, null, l.Tag3, q.Stunde, l.Tag3, frei);
        Assert.True(Verlegungsaufhebung.IstVerlegung(d, q));

        // unabhängige Verlegung einer anderen Lehrkraft am selben Tag
        var andere = Tagesplan.Regulaer(d, l.Tag3).First(r => Geeignet(d, r) && r.LehrerId != l.Lehrer && !r.KlassenIds.Contains(klasse));
        var tpa = Tagesplan.Berechnen(d, l.Tag3);
        var zielA = Enumerable.Range(1, 8).First(s => s != andere.Stunde &&
            Vertretungen.Feldstatus(d, tpa, new[] { andere.LehrerId!.Value }, andere.KlassenIds, s, andere.Unterricht.Id, andere.RaumId) == Feldstatus.Frei);
        Vertretungen.Verlegen(d, andere.Unterricht.Id, null, l.Tag3, andere.Stunde, l.Tag3, zielA);
        var unabhaengig = d.Vertretungen.Where(v => v.UnterrichtId == andere.Unterricht.Id && v.Datum == l.Tag3).Select(v => v.Id).OrderBy(x => x).ToList();
        Assert.Equal(2, unabhaengig.Count);

        var (_, e) = Absenzverkuerzung.Speichern(d, new[] { BisTag(l, l.Tag2) }, new[] { l.Absenz });
        var m = Assert.Single(e.Massnahmen, x => x.Art == Absenzverkuerzung.Massnahmenart.Verlegung);
        Assert.Contains($"{frei}. Std.", m.Beschreibung);
        Assert.Contains("1 Verlegung", e.Zusammenfassung(d, false));
        // Stunde wieder an ihrem Ursprung mit der eigenen Lehrkraft
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == l.Tag3 && v.UnterrichtId == q.UnterrichtId);
        var nachher = Tagesplan.Berechnen(d, l.Tag3);
        Assert.Contains(nachher.VonKlasse(klasse), x => x.Stunde == q.Stunde && x.Unterricht?.Id == q.UnterrichtId && x.Status == TagesStatus.Regulaer);
        // die andere Verlegung bleibt
        Assert.Equal(unabhaengig, d.Vertretungen.Where(v => v.UnterrichtId == andere.Unterricht.Id && v.Datum == l.Tag3).Select(v => v.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public void VorziehungInDieFreieStundeWirdAufgehoben()
    {
        var l = Krank();
        var d = l.D;
        // offene Stunde der Lehrkraft, in die Unterricht derselben Klasse einer anderen Lehrkraft aus einer späteren Stunde vorgezogen wird
        var tp = Tagesplan.Berechnen(d, l.Tag3);
        (Vertretung o, RegelStunde spaeter)? wahl = null;
        foreach (var o in Offene(l, l.Tag3))
        {
            var r = tp.Regel.FirstOrDefault(x => Geeignet(d, x) && x.Stunde > o.Stunde && x.LehrerId != l.Lehrer && x.KlassenIds.SequenceEqual(o.KlassenIds)
                                                && !tp.Regel.Any(y => y.Stunde == x.Stunde && y.LehrerId == l.Lehrer));
            if (r != null) { wahl = (o, r); break; }
        }
        Assert.NotNull(wahl);
        var (offen, vor) = wahl!.Value;
        offen.Art = VertretungsArt.Entfall;
        offen.VertreterId = null;
        offen.Bearbeitet = true;
        Vertretungen.Verlegen(d, vor.Unterricht.Id, null, l.Tag3, vor.Stunde, l.Tag3, offen.Stunde);
        Assert.Contains(d.Vertretungen, v => v.Datum == l.Tag3 && v.Stunde == offen.Stunde && v.UnterrichtId == vor.Unterricht.Id);

        var (_, e) = Absenzverkuerzung.Speichern(d, new[] { BisTag(l, l.Tag2) }, new[] { l.Absenz });
        Assert.Contains(e.Massnahmen, x => x.Art == Absenzverkuerzung.Massnahmenart.Vorziehung && x.Stunde == offen.Stunde);
        // vorgezogener Unterricht wieder an seinem Platz, die Lehrkraft hält ihre Stunde selbst
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == l.Tag3 && (v.UnterrichtId == vor.Unterricht.Id || v.UnterrichtId == offen.UnterrichtId));
        var nachher = Tagesplan.Berechnen(d, l.Tag3);
        Assert.Contains(nachher.Eintraege, x => x.Stunde == vor.Stunde && x.Unterricht?.Id == vor.Unterricht.Id && x.Status == TagesStatus.Regulaer);
        Assert.Contains(nachher.Eintraege, x => x.Stunde == offen.Stunde && x.Unterricht?.Id == offen.UnterrichtId && x.Status == TagesStatus.Regulaer);
    }

    [Fact]
    public void GeloeschteAbsenzMachtAllesRueckgaengig()
    {
        var l = Krank();
        var d = l.D;
        foreach (var v in new[] { Offene(l, l.Tag1)[0], Offene(l, l.Tag3)[0] }) Vertretungen.VertreterSetzen(d, v, FreierLehrer(d, v));
        var e = Absenzverkuerzung.Ermitteln(d, Array.Empty<Abwesenheit>(), new[] { l.Absenz });
        Assert.Equal(2, e.Anzahl(Absenzverkuerzung.Massnahmenart.Vertretung));
        Assert.Equal(l.Tag1, Assert.Single(e.Rueckkehr).Datum);
        Assert.Contains(e.Massnahmen, m => m.Art == Absenzverkuerzung.Massnahmenart.Offen);
        Absenzverkuerzung.Speichern(d, Array.Empty<Abwesenheit>(), new[] { l.Absenz });
        Assert.DoesNotContain(d.Vertretungen, v => v.LehrerId == l.Lehrer && v.Datum >= l.Tag1 && v.Datum <= l.Tag3);
        Assert.DoesNotContain(d.Abwesenheiten, a => a.Id == l.Absenz.Id);
    }

    [Fact]
    public void OhneVerkuerzungKeineMassnahmen()
    {
        var l = Krank();
        var d = l.D;
        Vertretungen.VertreterSetzen(d, Offene(l, l.Tag3)[0], FreierLehrer(d, Offene(l, l.Tag3)[0]));
        // nur der Text geändert, Zeitraum gleich
        var k = l.Absenz.Kopie();
        k.Text = "Arzttermin";
        Assert.True(Absenzverkuerzung.Ermitteln(d, new[] { k }, new[] { l.Absenz }).Leer);
        // verlängert
        var lang = l.Absenz.Kopie();
        lang.Bis = Kalender.NaechsterUnterrichtstag(d, l.Tag3);
        Assert.True(Absenzverkuerzung.Ermitteln(d, new[] { lang }, new[] { l.Absenz }).Leer);
        // weitere Absenz deckt die Zeit weiterhin ab (z. B. zweite Meldung): nichts rückgängig
        AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = l.Lehrer, Von = l.Tag2, Bis = l.Tag3, StundeVon = 1 } }, Array.Empty<Abwesenheit>());
        Assert.DoesNotContain(Absenzverkuerzung.Ermitteln(d, new[] { BisTag(l, l.Tag1) }, new[] { l.Absenz }).Massnahmen, m => m.Datum >= l.Tag2);
    }

    [Fact]
    public void VerkuerzungInnerhalbEinesTagesNurAbDerStunde()
    {
        var l = Krank();
        var d = l.D;
        var offen = Offene(l, l.Tag3);
        foreach (var v in offen) Vertretungen.VertreterSetzen(d, v, FreierLehrer(d, v));
        var ab = offen[1].Stunde;
        var (neu, alt) = Absenzverkuerzung.WiederAnwesend(d, l.Lehrer, l.Tag3, ab);
        var n = Assert.Single(neu);
        Assert.Equal(l.Absenz.Id, Assert.Single(alt).Id);
        Assert.Equal((l.Tag1, l.Tag3, ab - 1), (n.Von, n.Bis, n.StundeBis));
        var (_, e) = Absenzverkuerzung.Speichern(d, neu, alt);
        Assert.All(e.Massnahmen, m => Assert.True(m.Datum == l.Tag3 && m.Stunde >= ab));
        Assert.Contains(e.Massnahmen, m => m.VertretungId == offen[1].Id);
        Assert.Equal((l.Tag3, ab), (Assert.Single(e.Rueckkehr).Datum, e.Rueckkehr[0].Stunde));
        // Vertretung vor der Stunde bleibt
        Assert.Contains(d.Vertretungen, v => v.Id == offen[0].Id && v.VertreterId.HasValue);
        Assert.DoesNotContain(d.Vertretungen, v => v.Id == offen[1].Id);
    }

    [Fact]
    public void WiederAnwesendAbBeginnLoeschtDieAbsenzUndLaesstSpaetereStehen()
    {
        var l = Krank();
        var d = l.D;
        var spaeter = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = l.Lehrer, Von = l.Tag3.AddDays(14), Bis = l.Tag3.AddDays(14), Text = "Fortbildung" };
        AbsenzVerwaltung.Speichern(d, new[] { spaeter }, Array.Empty<Abwesenheit>());
        var (neu, alt) = Absenzverkuerzung.WiederAnwesend(d, l.Lehrer, l.Tag1, 1);
        Assert.Empty(neu);
        Assert.Equal(l.Absenz.Id, Assert.Single(alt).Id);
        // ab dem zweiten Tag: Absenz endet am ersten Tag (ganztägig)
        var (neu2, _) = Absenzverkuerzung.WiederAnwesend(d, l.Lehrer, l.Tag2, 1);
        Assert.Equal((l.Tag1, 99), (Assert.Single(neu2).Bis, neu2[0].StundeBis));
        // nicht abwesend: nichts zu tun
        var (neu3, alt3) = Absenzverkuerzung.WiederAnwesend(d, l.Lehrer, l.Tag3.AddDays(7), 1);
        Assert.Empty(neu3);
        Assert.Empty(alt3);
    }
}
