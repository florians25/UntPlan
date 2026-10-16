using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Vertretungsvorschlag wie im Vorbild: Stundenplanspalte, Vertr./Entfall-Werte, Kennzeichen, Sortierung, Dislozierung, Betreuungen, Vorziehungen.</summary>
public class VertretungsvorschlagTests
{
    private static (Schuldatei d, DateOnly tag, List<Vertretung> offen, int lehrer) Krank()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var ix = new DatenIndex(d);
        var plan = Tagesplan.Regulaer(d, tag);
        var lehrer = plan.Where(x => !Vertretungen.IstBereitschaft(ix, x.Unterricht) && x.LehrerId.HasValue).GroupBy(x => x.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var offen = d.Vertretungen.Where(v => v.Datum == tag && v.Offen && v.LehrerId == lehrer && v.Art != VertretungsArt.Pausenaufsicht).OrderBy(v => v.Stunde).ToList();
        Assert.NotEmpty(offen);
        return (d, tag, offen, lehrer);
    }

    [Fact]
    public void StundenplanspalteZeigtDreiStundenMitMarkierterVertretungsstunde()
    {
        var (d, tag, offen, lehrer) = Krank();
        var tp = Tagesplan.Berechnen(d, tag);
        var ix = new DatenIndex(d);
        var max = d.Zeitraster.StundenProTag;
        // erste Stunde: Fenster 1–3, Ziel links; letzte Stunde: Fenster am Tagesende, Ziel rechts; sonst Ziel in der Mitte
        var erste = VertretungsVorschlag.StundenplanZellen(d, ix, tp, lehrer, 1);
        Assert.Equal(new[] { 1, 2, 3 }, erste.Select(z => z.Stunde));
        Assert.True(erste[0].Ziel);
        var letzte = VertretungsVorschlag.StundenplanZellen(d, ix, tp, lehrer, max);
        Assert.Equal(max, letzte[2].Stunde);
        Assert.True(letzte[2].Ziel);
        var mitte = VertretungsVorschlag.StundenplanZellen(d, ix, tp, lehrer, 3);
        Assert.Equal(new[] { 2, 3, 4 }, mitte.Select(z => z.Stunde));
        Assert.Equal(1, mitte.Count(z => z.Ziel));
        Assert.True(mitte[1].Ziel);
        // die offenen Stunden der abwesenden Lehrkraft erscheinen durchgestrichen mit ihrem Fach
        var v = offen[0];
        var zelle = VertretungsVorschlag.StundenplanZellen(d, ix, tp, lehrer, v.Stunde).Single(z => z.Ziel);
        Assert.True(zelle.Entfallen);
        Assert.Equal(ix.KFach(v.FachId), zelle.Text);
        // Kandidaten tragen die Spalte mit drei Stunden
        Assert.All(VertretungsVorschlag.Berechnen(d, v), k =>
        {
            Assert.Equal(3, k.Zellen.Count);
            Assert.Equal(v.Stunde, k.Zellen.Single(z => z.Ziel).Stunde);
            Assert.False(k.Zellen.Single(z => z.Ziel).Text.Length > 0 && !k.Zellen.Single(z => z.Ziel).Entfallen && !k.Bereitschaft,
                "Ein vorgeschlagener Lehrer hat zur Vertretungsstunde selbst Unterricht.");
        });
    }

    [Fact]
    public void VertrUndEntfallSindGewerteteSummenUndKennzeichenMarkiertBetroffene()
    {
        var (d, tag, offen, _) = Krank();
        Assert.True(offen.Count >= 2, "Testdaten: mindestens zwei offene Stunden erwartet.");
        var k = VertretungsVorschlag.Berechnen(d, offen[0]).First(x => !x.Bereitschaft && !x.Freigesetzt);
        Vertretungen.VertreterSetzen(d, offen[0], k.Lehrer.Id);
        var neu = VertretungsVorschlag.Berechnen(d, offen[1], alleAnzeigen: true).FirstOrDefault(x => x.Lehrer.Id == k.Lehrer.Id);
        if (neu is null) return; // zur zweiten Stunde nicht frei
        Assert.Equal(1m, neu.VertretungenWert);
        Assert.Equal(0m, neu.EntfaelleWert);
        Assert.Equal(neu.VertretungenWert - neu.EntfaelleWert, neu.Zaehler);
        Assert.True(neu.Kennzeichen);
        Assert.Contains($"{offen[0].Stunde}. Std.", neu.KennzeichenText);
    }

    [Fact]
    public void WerteTrennenVertretungenUndEntfaelle()
    {
        var z = new ZaehlSumme();
        z.Zeilen.Add(new ZaehlZeile(null, default, 1, "+V", "", "", "", "", 1m, true, false, false, ""));
        z.Zeilen.Add(new ZaehlZeile(null, default, 2, "+V", "", "", "", "", 0.5m, true, false, false, ""));
        z.Zeilen.Add(new ZaehlZeile(null, default, 3, "-E", "", "", "", "", -2m, true, false, false, ""));
        z.Zeilen.Add(new ZaehlZeile(null, default, 4, "-E", "", "", "", "", 0m, false, false, false, ""));
        Assert.Equal((1.5m, 2m), VertretungsVorschlag.Werte(z));
    }

    [Fact]
    public void ZeitwunschMinusDreiStehtAmEnde()
    {
        var (d, tag, offen, _) = Krank();
        var v = offen[0];
        d.Vertretungseinstellungen.Vorschlag.GesperrteNichtAnzeigen = false;
        var vorher = VertretungsVorschlag.Berechnen(d, v);
        Assert.True(vorher.Count >= 2);
        var erster = vorher[0].Lehrer;
        erster.Zeitwuensche.Set(Kalender.TagIndex(d, tag), v.Stunde, -3);
        var nachher = VertretungsVorschlag.Berechnen(d, v);
        Assert.Equal(erster.Id, nachher[^1].Lehrer.Id);
        Assert.Equal(-3, nachher[^1].Zeitwunsch);
        // mit „gesperrte nicht anzeigen“ fällt er ganz heraus
        d.Vertretungseinstellungen.Vorschlag.GesperrteNichtAnzeigen = true;
        Assert.DoesNotContain(VertretungsVorschlag.Berechnen(d, v), x => x.Lehrer.Id == erster.Id);
    }

    [Fact]
    public void DislozierteLehrkraftNurMitOptionDislozierungenIgnorieren()
    {
        var (d, tag, offen, _) = Krank();
        Assert.False(VertretungsVorschlag.DislozierungenDefiniert(d));
        var tp = Tagesplan.Berechnen(d, tag);
        // Vertretung mit Raum und einem Kandidaten, der direkt davor oder danach in einem anderen Raum unterrichtet
        foreach (var v in offen.Where(x => x.RaumId.HasValue))
        {
            var kandidat = VertretungsVorschlag.Berechnen(d, v).Select(k => k.Lehrer)
                .FirstOrDefault(l => tp.VonLehrer(l.Id).Any(e => (e.Stunde == v.Stunde - 1 || e.Stunde == v.Stunde + 1) && e.RaumIds.Any(r => r != v.RaumId)));
            if (kandidat is null) continue;
            var nachbar = tp.VonLehrer(kandidat.Id).First(e => (e.Stunde == v.Stunde - 1 || e.Stunde == v.Stunde + 1) && e.RaumIds.Any(r => r != v.RaumId)).RaumIds.First(r => r != v.RaumId);
            var hausA = d.NeueId();
            var hausB = d.NeueId();
            foreach (var r in d.Raeume) r.AbteilungId = hausA;
            d.Raeume.First(r => r.Id == nachbar).AbteilungId = hausB;
            Assert.True(VertretungsVorschlag.DislozierungenDefiniert(d));
            Assert.DoesNotContain(VertretungsVorschlag.Berechnen(d, v), k => k.Lehrer.Id == kandidat.Id);
            var mit = VertretungsVorschlag.Berechnen(d, v, dislozierungenIgnorieren: true).Single(k => k.Lehrer.Id == kandidat.Id);
            Assert.True(mit.Disloziert);
            Assert.Contains("Dislozierung", mit.Kriterien);
            return;
        }
    }

    [Fact]
    public void BetreuungenTragenStundenplanZeitwunschUndJahressaldo()
    {
        var (d, tag, offen, _) = Krank();
        var v = offen[0];
        var betreuungen = VertretungsVorschlag.Betreuungen(d, v);
        Assert.NotEmpty(betreuungen);
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.All(betreuungen, b =>
        {
            Assert.Equal(3, b.Zellen.Count);
            var ziel = b.Zellen.Single(z => z.Ziel);
            Assert.Equal(v.Stunde, ziel.Stunde);
            // Betreuer unterrichten zur Stunde selbst – ihr Fach steht in der markierten Stunde
            Assert.False(string.IsNullOrEmpty(ziel.Text));
            Assert.Equal(b.Lehrer.Zeitwuensche.Get(Kalender.TagIndex(d, tag), v.Stunde), b.Zeitwunsch);
        });
    }

    [Fact]
    public void VorziehungTraegtSpaltenUndLaesstSichAlsVerlegungUebernehmen()
    {
        var (d, tag, offen, _) = Krank();
        foreach (var v in offen)
        {
            var vz = VertretungsVorschlag.Vorziehungen(d, v, VorziehungsModus.AlleVerlegungen).FirstOrDefault();
            if (vz is null) continue;
            Assert.False(string.IsNullOrEmpty(vz.Lehrer));
            Assert.False(string.IsNullOrEmpty(vz.Klassen));
            Assert.Equal(vz.Randstunde, vz.Beschreibung.EndsWith("(Randstunde)"));
            VertretungsVorschlag.VorziehungAusfuehren(d, v, vz);
            Assert.Equal(VertretungsArt.Entfall, v.Art);
            Assert.Contains(d.Vertretungen, x => x.Datum == v.Datum && x.Stunde == v.Stunde && x.Art == VertretungsArt.Verlegung && x.UnterrichtId == vz.Eintrag.Unterricht!.Id);
            return;
        }
    }
}
