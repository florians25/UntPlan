using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Vertretungsplanung nach Handbuch: Verlegung, Tausch, Merker, Zähler, Veranstaltungen, Klausuren, Zeitraster, Perioden, A/B-Wochen.</summary>
public class VertretungErweitertTests
{
    private static (Schuldatei d, DateOnly tag) Daten()
    {
        var d = Testdaten.Verplant();
        return (d, Testdaten.Unterrichtstag(d));
    }

    private static RegelStunde ErsteNormaleStunde(Schuldatei d, DateOnly tag) =>
        Tagesplan.Regulaer(d, tag).First(r => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(r.Unterricht.Band));

    [Fact]
    public void VerlegungErzeugtEntfallUndVerlegungUndLaesstSichZuruecknehmen()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        var tp = Tagesplan.Berechnen(d, tag);
        var freieStunde = Enumerable.Range(1, 8).First(s => s != r.Stunde &&
            Vertretungen.Feldstatus(d, tp, new[] { r.LehrerId!.Value }, r.KlassenIds, s, r.Unterricht.Id, r.RaumId) == Planung.Feldstatus.Frei);
        Vertretungen.Verlegen(d, r.Unterricht.Id, null, tag, r.Stunde, tag, freieStunde);
        var quelle = d.Vertretungen.Single(v => v.Datum == tag && v.Stunde == r.Stunde && v.UnterrichtId == r.Unterricht.Id);
        var ziel = d.Vertretungen.Single(v => v.Datum == tag && v.Stunde == freieStunde && v.UnterrichtId == r.Unterricht.Id);
        Assert.Equal(VertretungsArt.Entfall, quelle.Art);
        Assert.Equal(freieStunde, quelle.NachStunde);
        Assert.Equal(VertretungsArt.Verlegung, ziel.Art);
        Assert.Equal(r.Stunde, ziel.VonStunde);
        Assert.Equal(r.LehrerId, ziel.VertreterId);
        // Tagesplan: Klasse hat den Unterricht jetzt in der neuen Stunde
        var neu = Tagesplan.Berechnen(d, tag);
        Assert.Contains(neu.VonKlasse(r.KlassenIds[0]), e => e.Stunde == freieStunde && e.Unterricht?.Id == r.Unterricht.Id);
        Assert.DoesNotContain(neu.VonKlasse(r.KlassenIds[0]), e => e.Stunde == r.Stunde && e.Unterricht?.Id == r.Unterricht.Id);
        // Zurückziehen hebt die Verlegung auf
        Vertretungen.Verlegen(d, r.Unterricht.Id, null, tag, freieStunde, tag, r.Stunde);
        Assert.DoesNotContain(d.Vertretungen, v => v.UnterrichtId == r.Unterricht.Id && v.Datum == tag);
    }

    [Fact]
    public void OffeneVertretungWirdBeiVerlegungMitAnwesendemLehrerAufgeloest()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.Offen);
        // auf den nächsten Tag verlegen, an dem der Lehrer da ist
        var morgen = Kalender.NaechsterUnterrichtstag(d, tag);
        var tpm = Tagesplan.Berechnen(d, morgen);
        var s = Enumerable.Range(1, 8).First(x => Vertretungen.Feldstatus(d, tpm, new[] { r.LehrerId!.Value }, r.KlassenIds, x, r.Unterricht.Id, null) == Planung.Feldstatus.Frei);
        Vertretungen.Verlegen(d, r.Unterricht.Id, null, tag, r.Stunde, morgen, s);
        var ziel = d.Vertretungen.Single(v => v.Datum == morgen && v.Stunde == s && v.UnterrichtId == r.Unterricht.Id);
        Assert.Equal(r.LehrerId, ziel.VertreterId);
        // Neuberechnung erzeugt an der Quelle keine offene Vertretung mehr
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.UnterrichtId == r.Unterricht.Id && v.Offen);
    }

    [Fact]
    public void MerkerUndPunkte()
    {
        var (d, tag) = Daten();
        var tp = Tagesplan.Berechnen(d, tag);
        foreach (var l in d.Lehrer.Take(10))
        {
            var stunden = tp.VonLehrer(l.Id).Where(e => !Vertretungen.IstBereitschaftsfach(d, e.FachId)).Select(e => e.Stunde).ToList();
            if (stunden.Count == 0)
            {
                Assert.Equal("9", Vertretungen.Merker(d, tp, l.Id, 3));
                continue;
            }
            var min = stunden.Min();
            if (min > 1) Assert.Equal("1", Vertretungen.Merker(d, tp, l.Id, min - 1));
            var luecke = Enumerable.Range(min, stunden.Max() - min + 1).FirstOrDefault(s => !stunden.Contains(s) && !Vertretungen.HatBereitschaft(d, tp, l.Id, s));
            if (luecke > 0) Assert.Equal("0", Vertretungen.Merker(d, tp, l.Id, luecke));
        }
    }

    [Fact]
    public void EntfallZaehltNurMitAbsenzgrund()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        var l = d.Lehrer.First(x => x.Id == r.LehrerId);
        var krank = new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "KR", Langname = "Krankheit" };
        var urlaub = new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "SU", Langname = "Sonderurlaub", EntfallZaehlen = true };
        d.Abwesenheitsgruende.AddRange(new[] { krank, urlaub });
        var abw = new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = tag, Bis = tag, GrundId = krank.Id };
        d.Abwesenheiten.Add(abw);
        Vertretungen.Aktualisieren(d, tag);
        foreach (var v in d.Vertretungen.Where(v => v.Datum == tag && v.LehrerId == l.Id && v.Offen).ToList()) Vertretungen.EntfallUmschalten(d, v);
        var z1 = Zaehlung.Lehrer(d, l, tag, tag);
        Assert.Equal(0, z1.Summe);
        Assert.True(z1.EntfaelleNichtZuZaehlen > 0);
        abw.GrundId = urlaub.Id;
        Vertretungen.Aktualisieren(d, tag);
        var z2 = Zaehlung.Lehrer(d, l, tag, tag);
        Assert.True(z2.Summe < 0);
        Assert.Equal(-z2.Entfaelle, (int)z2.Summe);
    }

    [Fact]
    public void VertretungZaehltPositivUndPlusIstOhneLehrer()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var offen = d.Vertretungen.Where(v => v.Datum == tag && v.LehrerId == r.LehrerId && v.Offen).ToList();
        var k = VertretungsVorschlag.Berechnen(d, offen[0]).First(x => !x.Bereitschaft && !x.Freigesetzt);
        Vertretungen.VertreterSetzen(d, offen[0], k.Lehrer.Id);
        Assert.Equal(1, Zaehlung.Lehrer(d, k.Lehrer, tag, tag).Summe);
        if (offen.Count > 1)
        {
            Vertretungen.OhneLehrer(offen[1]);
            Assert.Equal(VertretungsArt.VertretungOhneLehrer, offen[1].Art);
            Assert.False(offen[1].Offen);
        }
    }

    [Fact]
    public void VeranstaltungSetztKlasseFreiUndLehrerAbwesend()
    {
        var (d, tag) = Daten();
        var klasse = d.Klassen.First(k => k.Kurzname == "6a");
        var regel = Tagesplan.Regulaer(d, tag).Where(r => r.KlassenIds.Contains(klasse.Id) && r.Unterricht.Zeilen.Count == 1).ToList();
        var begleiter = regel.First().LehrerId!.Value;
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, KlassenIds = new() { klasse.Id }, LehrerIds = new() { begleiter }, Text = "Exkursion" });
        Vertretungen.Aktualisieren(d, tag);
        var am = d.Vertretungen.Where(v => v.Datum == tag).ToList();
        Assert.Contains(am, v => v.Art == VertretungsArt.Veranstaltung && v.VertreterId == begleiter);
        // Unterricht der Klasse mit anderen Lehrern → Freisetzung
        Assert.Contains(am, v => v.Art == VertretungsArt.Freisetzung && v.KlassenIds.Contains(klasse.Id));
        // Begleiter hat Unterricht in anderen Klassen → offene Vertretung
        var andere = Tagesplan.Regulaer(d, tag).Where(r => r.LehrerId == begleiter && !r.KlassenIds.Contains(klasse.Id) && !Vertretungen.IstBereitschaftsfach(d, r.FachId)).ToList();
        if (andere.Count > 0) Assert.Contains(am, v => v.LehrerId == begleiter && v.Offen);
        d.Veranstaltungen.Clear();
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SondereinsatzAufEntfallLoeschtOderEntfaellt(bool loeschen)
    {
        var (d, tag) = Daten();
        var tp = Tagesplan.Berechnen(d, tag);
        var lehrer = d.Lehrer.First(l => Vertretungen.LehrerBelegt(d, tp, l.Id, 1) is null);
        var se = Vertretungen.Sondereinsatz(d, tag, 1, 1, Array.Empty<int>(), new[] { lehrer.Id }, null, null, "AG").Single();
        Assert.True(Vertretungen.IstSondereinsatz(se));
        var geloescht = Vertretungen.EntfallUmschalten(d, se, "test", loeschen);
        Assert.Equal(loeschen, geloescht);
        if (loeschen) Assert.DoesNotContain(se, d.Vertretungen);
        else Assert.Equal(VertretungsArt.Entfall, Assert.Single(d.Vertretungen, v => v.Id == se.Id).Art);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SondereinsatzImStundenplanEntfallenLassenMitRueckfrage(bool loeschen)
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        var andere = d.Lehrer.First(l => l.Id != r.LehrerId);
        // Sondereinsatz zum Unterricht („Stunde ändern“ mit anderer Lehrkraft)
        var se = new Vertretung
        {
            Id = d.NeueId(), Datum = tag, Stunde = r.Stunde, Art = VertretungsArt.Sondereinsatz, Quelle = VertretungsQuelle.Manuell, UnterrichtId = r.Unterricht.Id, ZeilenIndex = r.ZeilenIndex,
            LehrerId = r.LehrerId, VertreterId = andere.Id, FachId = r.FachId, KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId, Bearbeitet = true,
        };
        d.Vertretungen.Add(se);
        Assert.Equal(new[] { se }, Vertretungen.SondereinsaetzeBeiEntfall(d, r.Unterricht.Id, null, tag, r.Stunde));
        Vertretungen.Entfallen(d, r.Unterricht.Id, null, tag, r.Stunde, "test", loeschen);
        if (loeschen)
        {
            Assert.DoesNotContain(se, d.Vertretungen);
            Assert.Equal(TagesStatus.Regulaer, Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(e => e.Unterricht?.Id == r.Unterricht.Id && e.Regel?.ZeilenIndex == r.ZeilenIndex).Status);
        }
        else Assert.Equal(VertretungsArt.Entfall, se.Art);
        // ohne Sondereinsatz keine Rückfrage nötig
        Assert.Empty(Vertretungen.SondereinsaetzeBeiEntfall(d, r.Unterricht.Id, null, tag, r.Stunde));
    }

    [Fact]
    public void EntfallImStundenplanRueckgaengigMachtWiederRegulaerenUnterricht()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        Vertretungen.Entfallen(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        var v = Assert.Single(Vertretungen.EntfallZeilen(d, r.Unterricht.Id, null, tag, r.Stunde));
        Assert.Equal(TagesStatus.Entfall, Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(e => e.Vertretung == v).Status);
        // Lehrkraft anwesend: manueller Entfall verschwindet, die Stunde findet wieder regulär statt (kein „???“)
        Assert.Equal(1, Vertretungen.EntfallRueckgaengig(d, r.Unterricht.Id, null, tag, r.Stunde, "test"));
        Assert.DoesNotContain(v, d.Vertretungen);
        Assert.Equal(TagesStatus.Regulaer, Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(e => e.Unterricht?.Id == r.Unterricht.Id).Status);
        Assert.Empty(Vertretungen.EntfallZeilen(d, r.Unterricht.Id, null, tag, r.Stunde));
    }

    [Fact]
    public void KlausurErzeugtAufsichtUndEntfall()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        // Aufsicht durch eine andere Lehrkraft: Prüfungszeile der Aufsicht und Entfall des Unterrichts
        var andere = d.Lehrer.First(l => l.Id != r.LehrerId);
        var k = new Klausur { Id = d.NeueId(), Name = "Mathe-Arbeit", Datum = tag, StundeVon = r.Stunde, StundeBis = r.Stunde, KlassenIds = r.KlassenIds.ToList(), LehrerIds = new() { andere.Id }, UnterrichtId = r.Unterricht.Id };
        d.Klausuren.Add(k);
        Vertretungen.Aktualisieren(d, tag);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Art == VertretungsArt.Klausur && v.VertreterId == andere.Id);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.UnterrichtId == r.Unterricht.Id && v.Art == VertretungsArt.Entfall);
        // Aufsicht durch die Lehrkraft des Unterrichts: die Prüfung ersetzt den Unterricht – genau eine Zeile (Klausur) statt Klausur + Entfall
        k.LehrerIds = new() { r.LehrerId!.Value };
        Vertretungen.Aktualisieren(d, tag);
        var zeilen = d.Vertretungen.Where(v => v.Datum == tag && v.Stunde == r.Stunde && v.KlausurId == k.Id).ToList();
        var z = Assert.Single(zeilen);
        Assert.Equal(VertretungsArt.Klausur, z.Art);
        Assert.Equal((r.Unterricht.Id, r.LehrerId, r.LehrerId, r.FachId), (z.UnterrichtId!.Value, z.LehrerId, z.VertreterId, z.FachId));
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.UnterrichtId == r.Unterricht.Id && v.IstEntfall);
        // im Tagesplan findet der Unterricht als Prüfung statt (kein regulärer Unterricht daneben)
        var e = Assert.Single(Tagesplan.Berechnen(d, tag).Stunde(r.Stunde), x => x.Unterricht?.Id == r.Unterricht.Id || x.Vertretung?.KlausurId == k.Id);
        Assert.Equal(TagesStatus.Vertreten, e.Status);
    }

    [Fact]
    public void VertretungszeitrasterErzeugtEntfallUndEigenverantwortlichesArbeiten()
    {
        var (d, tag) = Daten();
        var r = ErsteNormaleStunde(d, tag);
        var t = Kalender.TagIndex(d, tag);
        d.Vertretungseinstellungen.Zeitraster[t * 100 + r.Stunde] = VertretungsRegel.Entfall;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(VertretungsArt.Entfall, d.Vertretungen.Single(v => v.Datum == tag && v.UnterrichtId == r.Unterricht.Id && v.Stunde == r.Stunde).Art);
        var k = d.Klassen.First(x => x.Id == r.KlassenIds[0]);
        k.VertretungsZeitraster[t * 100 + r.Stunde] = VertretungsRegel.Eigenverantwortlich;
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(VertretungsArt.EigenverantwortlichesArbeiten, d.Vertretungen.Single(v => v.Datum == tag && v.UnterrichtId == r.Unterricht.Id && v.Stunde == r.Stunde).Art);
    }

    [Fact]
    public void PeriodenHabenEigenenUnterrichtUndStundenplan()
    {
        var (d, tag) = Daten();
        var p1 = d.AktivePeriode;
        var ab = tag.AddDays(14);
        var p2 = p1.Kopie(d.NeueId(), "Periode 2", ab, null);
        d.Perioden.Add(p2);
        // In Periode 2 wird ein Unterricht entplant
        var u = p2.Unterrichte!.First(x => p2.Plan.Any(p => p.UnterrichtId == x.Id));
        p2.Plan.RemoveAll(p => p.UnterrichtId == u.Id);
        Assert.Same(p1, d.PeriodeAm(tag));
        Assert.Same(p2, d.PeriodeAm(Kalender.NaechsterUnterrichtstag(d, ab.AddDays(-1))));
        var tagP2 = Kalender.NaechsterUnterrichtstag(d, ab.AddDays(-1));
        Assert.DoesNotContain(Tagesplan.Regulaer(d, tagP2), r => r.Unterricht.Id == u.Id);
        // Periode 1 unverändert
        Assert.Contains(p1.Plan, p => p.UnterrichtId == u.Id);
        // eingebettete kurze Periode hat Vorrang
        var p3 = p1.Kopie(d.NeueId(), "Projektwoche", tag, tag.AddDays(4));
        p3.Plan.Clear();
        d.Perioden.Add(p3);
        Assert.Same(p3, d.PeriodeAm(tag));
        Assert.Empty(Tagesplan.Regulaer(d, tag));
        // Speichern/Laden erhält die Perioden
        var json = System.Text.Json.JsonSerializer.Serialize(d, Dokument.JsonOptionen);
        var g = System.Text.Json.JsonSerializer.Deserialize<Schuldatei>(json, Dokument.JsonOptionen)!;
        Dokument.Normalisieren(g);
        Assert.Equal(3, g.Perioden.Count);
        Assert.Equal(p2.Unterrichte!.Count, g.Perioden.Single(p => p.Name == "Periode 2").Unterrichte!.Count);
    }

    [Fact]
    public void AbWochenUndZeilenUGruppe()
    {
        var (d, tag) = Daten();
        d.Einstellungen.Wochenperiodizitaet = 2;
        var a = new UGruppe { Id = d.NeueId(), Kurzname = "WA", Wochen = "A" };
        var b = new UGruppe { Id = d.NeueId(), Kurzname = "WB", Wochen = "B" };
        d.UGruppen.AddRange(new[] { a, b });
        Kalender.CacheLeeren();
        var r = ErsteNormaleStunde(d, tag);
        var u = r.Unterricht;
        // Kopplung: Zeile 1 nur A-Woche, Zeile 2 nur B-Woche
        var zweiter = d.Lehrer.First(l => l.Id != r.LehrerId);
        u.Zeilen.Add(new Kopplungszeile { LehrerId = zweiter.Id, FachId = r.FachId, KlassenIds = r.KlassenIds.ToList(), UGruppeId = b.Id });
        u.Zeilen[0].UGruppeId = a.Id;
        foreach (var p in d.Stundenplan.Where(p => p.UnterrichtId == u.Id)) p.Raeume.Add(null);
        var wocheA = Kalender.WochenTyp(d, tag) == "A" ? tag : tag.AddDays(7);
        var wocheB = wocheA.AddDays(7);
        Assert.Equal("A", Kalender.WochenTyp(d, wocheA));
        Assert.Equal("B", Kalender.WochenTyp(d, wocheB));
        var ra = Tagesplan.Regulaer(d, wocheA).Where(x => x.Unterricht.Id == u.Id && x.Stunde == r.Stunde).ToList();
        var rb = Tagesplan.Regulaer(d, wocheB).Where(x => x.Unterricht.Id == u.Id && x.Stunde == r.Stunde).ToList();
        if (ra.Count > 0) Assert.All(ra, x => Assert.Equal(r.LehrerId, x.LehrerId));
        if (rb.Count > 0) Assert.All(rb, x => Assert.Equal(zweiter.Id, x.LehrerId));
        Assert.Equal(0.5m, Math.Round(Kalender.UGruppeFaktor(d, a) + Kalender.UGruppeFaktor(d, b) - 0.5m, 1));
    }

    [Fact]
    public void StatistikBerichteLaufen()
    {
        var (d, tag) = Daten();
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = d.Lehrer[2].Id, Von = tag, Bis = tag });
        VertretungsVorschlag.Automatisch(d, tag);
        var von = tag.AddDays(-7);
        var bis = tag.AddDays(7);
        Assert.NotEmpty(Statistik.Abrechnung(d, von, bis));
        var ue = Statistik.VertretungsUebersicht(d, von, bis);
        Assert.True(ue.Geplant > 0 && ue.NichtErteilt > 0);
        Assert.NotEmpty(Statistik.Entfalltage(d, von, bis));
        Assert.NotEmpty(Statistik.Mehrarbeit(d, d.Lehrer[2], tag.Year, tag.Month));
    }
}
