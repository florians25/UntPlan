using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using Xunit;

namespace UntPlan.Core.Tests;

public class PeriodenTests
{
    private static Dokument Doc()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan.Clear();
        var u = d.Unterrichte[0];
        d.AktivePeriode.Plan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 1, Raeume = u.Zeilen.Select(_ => (int?)null).ToList() });
        return new Dokument(d);
    }

    [Fact]
    public void NeuePeriodeUebernimmtUnterrichtPlanUndGiltAbDatum()
    {
        var doc = Doc();
        var d = doc.Daten;
        var mutter = d.AktivePeriode;
        var anzahl = d.Unterrichte.Count;
        var ab = d.Einstellungen.SchuljahrVon.AddDays(70);
        Periode? neu = null;
        doc.Aendern("neu", AenderungsArt.Alles, x => neu = Perioden.Neu(x, x.AktivePeriode, "P2", ab, null));
        Assert.Equal(anzahl, neu!.Unterrichte!.Count);
        Assert.Single(neu.Plan);
        Assert.Equal(mutter.Id, neu.MutterId);
        Assert.Equal(mutter.Id, d.PeriodeAm(ab.AddDays(-1)).Id);
        Assert.Equal(neu.Id, d.PeriodeAm(ab).Id);
        Assert.True(Perioden.Schultage(d, mutter) > 0 && Perioden.Schultage(d, neu) > 0);
        Assert.Throws<InvalidOperationException>(() => Perioden.Neu(d, mutter, "zu früh", d.Einstellungen.SchuljahrVon.AddDays(-10), null));
    }

    [Fact]
    public void UnterrichtsaenderungWirdInTochterUebernommenAusserWennDortAbweichend()
    {
        var doc = Doc();
        var d = doc.Daten;
        var mutterId = d.AktivePeriodeId;
        var u1 = d.Unterrichte[0].Id;
        var u2 = d.Unterrichte[1].Id;
        Periode? t = null;
        doc.Aendern("neu", AenderungsArt.Alles, x => t = Perioden.Neu(x, x.AktivePeriode, "P2", x.Einstellungen.SchuljahrVon.AddDays(60), null));
        // in der Tochter u2 bewusst abweichend
        doc.Aendern("aktiv", AenderungsArt.Alles, x => x.AktivePeriodeId = t!.Id);
        doc.Aendern("abw", AenderungsArt.Unterricht, x => x.Unterrichte.First(u => u.Id == u2).Wochenstunden = 7);
        // zurück zur Mutter und beide ändern
        doc.Aendern("aktiv", AenderungsArt.Alles, x => x.AktivePeriodeId = mutterId);
        doc.Aendern("änd", AenderungsArt.Unterricht, x =>
        {
            x.Unterrichte.First(u => u.Id == u1).Text = "neu";
            x.Unterrichte.First(u => u.Id == u2).Text = "neu";
            x.Unterrichte.Add(new Unterricht { Id = x.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile() }, Text = "zusätzlich" });
        });
        var tochter = d.Perioden.First(p => p.Id == t!.Id);
        Assert.Equal("neu", tochter.Unterrichte!.First(u => u.Id == u1).Text);
        Assert.NotEqual("neu", tochter.Unterrichte!.First(u => u.Id == u2).Text); // abweichend → unverändert
        Assert.Equal(7, tochter.Unterrichte!.First(u => u.Id == u2).Wochenstunden);
        Assert.Contains(tochter.Unterrichte!, u => u.Text == "zusätzlich");
    }

    [Fact]
    public void FixiertePeriodeSperrtUnterrichtUndPlan()
    {
        var doc = Doc();
        doc.Aendern("fix", AenderungsArt.Alles, x => x.AktivePeriode.Fixiert = true);
        Assert.Throws<InvalidOperationException>(() => doc.Aendern("x", AenderungsArt.Unterricht, x => x.Unterrichte[0].Wochenstunden = 9));
        Assert.Throws<InvalidOperationException>(() => doc.Aendern("x", AenderungsArt.Stundenplan, x => x.Stundenplan.Clear()));
        Assert.Throws<InvalidOperationException>(() => Perioden.Loeschen(doc.Daten, doc.Daten.AktivePeriode));
    }

    [Fact]
    public void LoeschenHaengtToechterUmUndElementLoeschenBereinigtAllePerioden()
    {
        var doc = Doc();
        var d = doc.Daten;
        var p1 = d.AktivePeriode;
        var p2 = Perioden.Neu(d, p1, "P2", d.Einstellungen.SchuljahrVon.AddDays(30), null);
        var p3 = Perioden.Neu(d, p2, "P3", d.Einstellungen.SchuljahrVon.AddDays(60), null);
        Perioden.Loeschen(d, p2);
        Assert.Equal(p1.Id, p3.MutterId);
        var lehrer = d.Lehrer.First(l => p3.Unterrichte!.Any(u => u.LehrerIds.Contains(l.Id)));
        Stammdaten.Loeschen(d, lehrer);
        Assert.DoesNotContain(d.AlleUnterrichte.SelectMany(u => u.Zeilen), z => z.LehrerId == lehrer.Id);
        Assert.Throws<InvalidOperationException>(() => { Perioden.Loeschen(d, p3); Perioden.Loeschen(d, p1); });
    }
}
