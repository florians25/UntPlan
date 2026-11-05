using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Vertretungsvorschlag nach Handbuch (Runde 2): Spalte „Stundenplan“ mit so vielen Stunden, wie Platz ist, zweistufige Vorziehungen
/// und Doppelstunden (Schritt 1/Schritt 2), Vormerkungen im Vorschlag, Ändern verplanter Vormerkungen, Bereitschafts-Einsatzzähler, „bez.“.
/// </summary>
public class VorschlagHandbuchTests
{
    private static (Schuldatei d, DateOnly tag, List<Vertretung> offen, int lehrer) Krank(int rang = 0, int tagVersatz = 0)
    {
        var d = Testdaten.Verplant();
        var tag = Kalender.NaechsterUnterrichtstag(d, Testdaten.Unterrichtstag(d).AddDays(tagVersatz));
        var ix = new DatenIndex(d);
        var plan = Tagesplan.Regulaer(d, tag);
        var lehrer = plan.Where(x => !Vertretungen.IstBereitschaft(ix, x.Unterricht) && x.LehrerId.HasValue).GroupBy(x => x.LehrerId!.Value)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Skip(rang).First().Key;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var offen = d.Vertretungen.Where(v => v.Datum == tag && v.Offen && v.LehrerId == lehrer && v.Art != VertretungsArt.Pausenaufsicht).OrderBy(v => v.Stunde).ToList();
        return (d, tag, offen, lehrer);
    }

    [Fact]
    public void StundenplanspalteZeigtSovieleStundenWiePlatzIst()
    {
        var (d, _, offen, _) = Krank();
        var v = offen[0];
        var k = VertretungsVorschlag.Berechnen(d, v).First();
        Assert.Equal(VertretungsVorschlag.TagesStunden(d, new[] { v.Stunde }), k.TagesZellen.Count);
        Assert.Equal(Enumerable.Range(1, k.TagesZellen.Count), k.TagesZellen.Select(z => z.Stunde));
        // der Ausschnitt mit drei Stunden entspricht der bisherigen Spalte
        Assert.Equal(k.Zellen.Select(z => z.Stunde), VertretungsVorschlag.StundenplanFenster(k.TagesZellen, v.Stunde, 3).Select(z => z.Stunde));
        foreach (var n in new[] { 1, 2, 5, 7, 99 })
        {
            var f = VertretungsVorschlag.StundenplanFenster(k.TagesZellen, v.Stunde, n);
            Assert.Equal(Math.Min(n, k.TagesZellen.Count), f.Count);
            Assert.Contains(f, z => z.Ziel && z.Stunde == v.Stunde);
            Assert.Equal(Enumerable.Range(f[0].Stunde, f.Count), f.Select(z => z.Stunde));
        }
    }

    [Fact]
    public void ZweistufigeVorziehungFuelltDieLueckeDesErstenSchritts()
    {
        for (var rang = 0; rang < 6; rang++)
        {
            var (d, _, offen, _) = Krank(rang);
            foreach (var v in offen)
            {
                var liste = VertretungsVorschlag.Vorziehungen(d, v, VorziehungsModus.AlleVerlegungen, false, zweistufig: true);
                var vz = liste.FirstOrDefault(x => x.Schritt2 != null);
                if (vz is null) continue;
                // ohne „zweistufig“ dieselben einstufigen Vorziehungen
                Assert.Equal(VertretungsVorschlag.Vorziehungen(d, v, VorziehungsModus.AlleVerlegungen, false).Select(x => x.Schluessel), liste.Where(x => x.Schritt2 is null).Select(x => x.Schluessel));
                var s2 = vz.Schritt2!;
                Assert.False(vz.Randstunde);
                Assert.Null(vz.Schritt2VertretungId);
                Assert.NotEqual((vz.Datum, vz.Stunde), (s2.Datum, s2.Stunde));
                Assert.Contains("Schritt 2", vz.Beschreibung);
                VertretungsVorschlag.VorziehungAusfuehren(d, v, vz);
                Assert.Equal(VertretungsArt.Entfall, v.Art);
                // Schritt 1: Unterricht in die offene Stunde, Schritt 2: zweiter Unterricht in die Lücke des ersten
                Assert.Contains(d.Vertretungen, x => x.Datum == v.Datum && x.Stunde == v.Stunde && x.Art == VertretungsArt.Verlegung && x.UnterrichtId == vz.Eintrag.Unterricht!.Id);
                Assert.Contains(d.Vertretungen, x => x.Datum == vz.Datum && x.Stunde == vz.Stunde && x.Art is VertretungsArt.Verlegung or VertretungsArt.Verlegt && x.UnterrichtId == s2.Eintrag.Unterricht!.Id);
                Assert.Contains(d.Vertretungen, x => x.Datum == s2.Datum && x.Stunde == s2.Stunde && x.IstEntfall && x.UnterrichtId == s2.Eintrag.Unterricht!.Id && x.NachDatum == vz.Datum);
                return;
            }
        }
        Assert.Fail("Keine zweistufige Vorziehung in den Testdaten gefunden.");
    }

    [Fact]
    public void DoppelstundeMitSchritt1UndSchritt2()
    {
        for (var rang = 0; rang < 10; rang++)
            for (var t = 0; t < 5; t += 1)
            {
                var (d, _, offen, _) = Krank(rang, t);
                foreach (var v in offen)
                {
                    var block = VertretungsVorschlag.Block(d, v).Where(x => x.Offen).OrderBy(x => x.Stunde).ToList();
                    if (block.Count < 2 || block[0] != v) continue;
                    var liste = VertretungsVorschlag.DoppelstundenVorziehungen(d, v, VorziehungsModus.AlleVerlegungen, false);
                    var vz = liste.FirstOrDefault();
                    if (vz is null) continue;
                    Assert.Equal(block[1].Id, vz.Schritt2VertretungId);
                    Assert.All(liste, x => Assert.NotEqual((x.Datum, x.Stunde), (x.Schritt2!.Datum, x.Schritt2.Stunde)));
                    VertretungsVorschlag.VorziehungAusfuehren(d, v, vz);
                    Assert.Equal(VertretungsArt.Entfall, block[0].Art);
                    Assert.Equal(VertretungsArt.Entfall, block[1].Art);
                    Assert.Contains(d.Vertretungen, x => x.Datum == v.Datum && x.Stunde == block[0].Stunde && x.Art == VertretungsArt.Verlegung && x.UnterrichtId == vz.Eintrag.Unterricht!.Id);
                    Assert.Contains(d.Vertretungen, x => x.Datum == v.Datum && x.Stunde == block[1].Stunde && x.Art == VertretungsArt.Verlegung && x.UnterrichtId == vz.Schritt2!.Eintrag.Unterricht!.Id);
                    return;
                }
            }
        Assert.Fail("Keine Doppelstunde mit Vorziehungen in den Testdaten gefunden.");
    }

    /// <summary>Vormerkung für eine freie Lehrkraft mit den Klassen der offenen Vertretung.</summary>
    private static (Schuldatei d, Vertretung v, Kandidat k, Vormerkung vm) MitVormerkung()
    {
        var (d, _, offen, _) = Krank();
        var v = offen.First(x => x.NeueKlassenIds.Count > 0);
        var k = VertretungsVorschlag.Berechnen(d, v).First(x => !x.Bereitschaft);
        var vm = new Vormerkung { Id = d.NeueId(), Quelle = VormerkungsQuelle.Konkret, Stunden = 3, FachId = v.FachId, LehrerIds = new() { k.Lehrer.Id }, KlassenIds = v.NeueKlassenIds.ToList(), Text = "Förderkurs" };
        d.Vormerkungen.Add(vm);
        return (d, v, k, vm);
    }

    [Fact]
    public void VormerkungImVorschlagAngebotenUndEingesetzt()
    {
        var (d, v, k, vm) = MitVormerkung();
        Assert.Contains(vm, VertretungsVorschlag.PassendeVormerkungen(d, v, k.Lehrer.Id));
        // andere Lehrkraft oder nicht mehr gültige Vormerkung: nicht angeboten
        var anderer = d.Lehrer.First(l => l.Id != k.Lehrer.Id && l.Id != v.LehrerId);
        Assert.DoesNotContain(vm, VertretungsVorschlag.PassendeVormerkungen(d, v, anderer.Id));
        vm.GueltigBis = v.Datum.AddDays(-1);
        Assert.Empty(VertretungsVorschlag.PassendeVormerkungen(d, v, k.Lehrer.Id));
        vm.GueltigBis = null;
        Assert.Contains("Vm " + vm.Id, VertretungsVorschlag.VormerkungText(new DatenIndex(d), vm));

        var se = VertretungsVorschlag.VormerkungEinsetzen(d, v, vm);
        Assert.Equal(VertretungsArt.Entfall, v.Art);
        Assert.Equal(VertretungsArt.SondereinsatzVormerkung, se.Art);
        Assert.Equal(k.Lehrer.Id, se.VertreterId);
        Assert.Equal(vm.Id, se.VormerkungId);
        Assert.Equal((v.Datum, v.Stunde), (se.Datum, se.Stunde));
        Assert.Equal(new[] { v.Datum }, vm.VerplantAm);
        Assert.Equal(2, vm.Offen);
        Assert.Null(Vertretungen.LehrerBelegt(d, Tagesplan.Berechnen(d, v.Datum), k.Lehrer.Id, v.Stunde, se));
    }

    [Fact]
    public void VerplanteVormerkungNurOhneKollisionAenderbar()
    {
        var (d, v, k, vm) = MitVormerkung();
        var se = VertretungsVorschlag.VormerkungEinsetzen(d, v, vm);
        var tp = Tagesplan.Berechnen(d, v.Datum);
        // Lehrkraft mit eigenem Unterricht in dieser Stunde: Kollision, nichts wird geändert
        var belegt = tp.Stunde(v.Stunde).Where(e => e.Findetstatt && e.Status == TagesStatus.Regulaer && !Vertretungen.IstBereitschaftsfach(d, e.FachId))
            .SelectMany(e => e.LehrerIds).First(l => l != k.Lehrer.Id);
        var fehler = Vertretungen.VormerkungAendern(d, vm, new[] { belegt }, vm.KlassenIds);
        Assert.NotNull(fehler);
        Assert.StartsWith("Kollision", fehler);
        Assert.Equal(new[] { k.Lehrer.Id }, vm.LehrerIds);
        Assert.Equal(k.Lehrer.Id, se.VertreterId);
        // freie Lehrkraft: Vormerkung und verplante Stunde werden geändert
        var frei = d.Lehrer.Where(l => !l.Ignorieren).Select(l => l.Id).First(l => l != k.Lehrer.Id && l != v.LehrerId && Vertretungen.LehrerBelegt(d, tp, l, v.Stunde) is null);
        Assert.Null(Vertretungen.VormerkungAendern(d, vm, new[] { frei }, vm.KlassenIds));
        Assert.Equal(new[] { frei }, vm.LehrerIds);
        var zeilen = d.Vertretungen.Where(x => x.VormerkungId == vm.Id).ToList();
        Assert.Single(zeilen);
        Assert.Equal(frei, zeilen[0].VertreterId);
        // Klasse, die in der Stunde Unterricht hat: Kollision
        var klasse = Tagesplan.Berechnen(d, v.Datum).Stunde(v.Stunde).Where(e => e.Findetstatt && e.Vertretung?.VormerkungId != vm.Id).SelectMany(e => e.KlassenIds).First(x => !vm.KlassenIds.Contains(x));
        Assert.NotNull(Vertretungen.VormerkungAendern(d, vm, vm.LehrerIds, vm.KlassenIds.Append(klasse).ToList()));
        // aus einer Vertretung entstandene Vormerkungen sind nicht veränderbar
        vm.Quelle = VormerkungsQuelle.Vertretung;
        Assert.NotNull(Vertretungen.VormerkungAendern(d, vm, vm.LehrerIds, vm.KlassenIds));
    }

    [Fact]
    public void BereitschaftsEinsatzzaehlerMitGenutztenUndVerplantenBereitschaften()
    {
        var d = Testdaten.Verplant();
        var ix = new DatenIndex(d);
        var tag = Testdaten.Unterrichtstag(d);
        // eine reguläre Bereitschaftsstunde suchen
        RegelStunde? ber = null;
        foreach (var t in Kalender.Tage(tag, tag.AddDays(14)).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            ber = Tagesplan.Regulaer(d, t).FirstOrDefault(r => r.LehrerId.HasValue && Vertretungen.IstBereitschaftsfach(d, r.FachId));
            if (ber != null) { tag = t; break; }
        }
        Assert.NotNull(ber);
        var l = ber!.LehrerId!.Value;
        var (von, bis) = (Kalender.Montag(tag), Kalender.Montag(tag).AddDays(6));
        var vorher = Zaehlung.BereitschaftsEinsaetze(d, von, bis)[l];
        var einsatz = ix.Faecher[ber.FachId!.Value].BereitschaftImDeputat ? vorher.Deputat : vorher.Regulaer;
        Assert.True(einsatz.Verplant > 0);
        Assert.Equal(0, einsatz.Genutzt);
        Assert.Equal($"0/{einsatz.Verplant}", einsatz.Text);
        Assert.False(einsatz.Erreicht);
        // Vertretung in der Bereitschaftsstunde: genutzt
        d.Vertretungen.Add(new Vertretung { Id = d.NeueId(), Datum = tag, Stunde = ber.Stunde, Art = VertretungsArt.Vertretung, VertreterId = l, LehrerId = d.Lehrer.First(x => x.Id != l).Id });
        var nachher = Zaehlung.BereitschaftsEinsaetze(d, von, bis)[l];
        var neu = ix.Faecher[ber.FachId!.Value].BereitschaftImDeputat ? nachher.Deputat : nachher.Regulaer;
        Assert.Equal(1, neu.Genutzt);
        Assert.Equal(einsatz.Verplant, neu.Verplant);
        Assert.Equal(neu.Verplant == 1, neu.Erreicht);
        Assert.True(new BereitschaftsEinsatz(2, 2).Erreicht);
        Assert.Equal("", new BereitschaftsEinsatz(0, 0).Text);
    }

    [Fact]
    public void BezahlteVertretungenZaehlenNurGehalteneVertretungen()
    {
        var (d, tag, offen, lehrer) = Krank();
        var k = VertretungsVorschlag.Berechnen(d, offen[0]).First();
        Vertretungen.VertreterSetzen(d, offen[0], k.Lehrer.Id);
        offen[0].Statistik = d.Vertretungseinstellungen.Zaehler.BezahltKennzeichen;
        // eigener Entfall mit Kennzeichen „bezahlt“ zählt für die abwesende Lehrkraft nicht als bezahlte Vertretung
        if (offen.Count > 1)
        {
            Vertretungen.EntfallUmschalten(d, offen[1]);
            offen[1].Statistik = d.Vertretungseinstellungen.Zaehler.BezahltKennzeichen;
        }
        Assert.Equal(1, Zaehlung.Lehrer(d, k.Lehrer, tag, tag).Bezahlt);
        Assert.Equal(0, Zaehlung.Lehrer(d, d.Lehrer.First(x => x.Id == lehrer), tag, tag).Bezahlt);
    }
}
