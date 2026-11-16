using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>„Lehrer ändern“ und „Lehrer hinzufügen“ im Stundenplan (Handbuch, Manuelles Planen | Lehrer ändern und hinzufügen).</summary>
public class StundenLehrerTests
{
    private static (Schuldatei D, Unterricht U, Lehrer A, Lehrer B, Lehrer C) Schule()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 5;
        d.Zeitraster.StundenProTag = 6;
        var a = new Lehrer { Id = d.NeueId(), Kurzname = "Rub" };
        var b = new Lehrer { Id = d.NeueId(), Kurzname = "Gauss" };
        var c = new Lehrer { Id = d.NeueId(), Kurzname = "Nobel" };
        var k = new Klasse { Id = d.NeueId(), Kurzname = "1a" };
        var f = new Fach { Id = d.NeueId(), Kurzname = "D" };
        b.FaecherIds.Add(f.Id);
        d.Lehrer.AddRange(new[] { a, b, c });
        d.Klassen.Add(k);
        d.Faecher.Add(f);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 5, Zeilen = { new Kopplungszeile { LehrerId = a.Id, FachId = f.Id, KlassenIds = { k.Id } } } };
        d.Unterrichte.Add(u);
        for (var t = 0; t < 5; t++) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = t, Stunde = 1, Raeume = { null } });
        // Nobel ist am Montag in der 1. Stunde bereits verplant
        var o = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = c.Id, FachId = f.Id } } };
        d.Unterrichte.Add(o);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = o.Id, Tag = 0, Stunde = 1, Raeume = { null } });
        return (d, u, a, b, c);
    }

    [Fact]
    public void EinzelneStundeErzeugtNeuenEinstuendigenUnterricht()
    {
        var (d, u, _, b, _) = Schule();
        var p = d.Stundenplan[2];
        var neu = StundenLehrer.Aendern(d, p, 0, b.Id, false);
        Assert.NotEqual(u.Id, neu);
        Assert.Equal(4, u.Wochenstunden);
        var nu = d.Unterrichte.Single(x => x.Id == neu);
        Assert.Equal(1, nu.Wochenstunden);
        Assert.Equal(b.Id, nu.Zeilen[0].LehrerId);
        Assert.Equal(neu, p.UnterrichtId);
        Assert.Equal(4, d.Stundenplan.Count(q => q.UnterrichtId == u.Id));
    }

    [Fact]
    public void FuerAlleStundenAendertDenUnterricht()
    {
        var (d, u, _, b, _) = Schule();
        Assert.Equal(u.Id, StundenLehrer.Aendern(d, d.Stundenplan[0], 0, b.Id, true));
        Assert.Equal(5, u.Wochenstunden);
        Assert.Equal(b.Id, u.Zeilen[0].LehrerId);
    }

    [Fact]
    public void HinzufuegenKoppeltMitVorhandenerLehrkraft()
    {
        var (d, u, a, b, _) = Schule();
        var p = d.Stundenplan[1];
        var id = StundenLehrer.Hinzufuegen(d, p, b.Id, false);
        var neu = d.Unterrichte.Single(x => x.Id == id);
        Assert.Equal(new[] { a.Id, b.Id }, neu.LehrerIds.ToArray());
        Assert.Equal(neu.Zeilen[0].KlassenIds, neu.Zeilen[1].KlassenIds);
        Assert.Equal(2, p.Raeume.Count);
        Assert.Equal(4, u.Wochenstunden);
    }

    [Fact]
    public void KandidatenZeigenBelegteUndLehrbefaehigte()
    {
        var (d, _, a, b, c) = Schule();
        var k = StundenLehrer.Kandidaten(d, d.Stundenplan[0], false, false);
        Assert.DoesNotContain(k, x => x.Lehrer == a);
        Assert.True(k.Single(x => x.Lehrer == c).Belegt);
        Assert.False(k.Single(x => x.Lehrer == b).Belegt);
        Assert.Equal(new[] { b }, StundenLehrer.Kandidaten(d, d.Stundenplan[0], false, true).Select(x => x.Lehrer));
        // für alle Stunden: Nobel ist (Mo-1) belegt
        Assert.True(StundenLehrer.Kandidaten(d, d.Stundenplan[3], true, false).Single(x => x.Lehrer == c).Belegt);
        Assert.False(StundenLehrer.Kandidaten(d, d.Stundenplan[3], false, false).Single(x => x.Lehrer == c).Belegt);
    }
}

/// <summary>Verplanen auf ein violettes Feld: Raum verdrängen, Kollision erzeugen, Raum nicht verplanen.</summary>
public class RaumBelegtVerplanenTests
{
    [Fact]
    public void VerdraengenUndKollision()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 5;
        d.Zeitraster.StundenProTag = 6;
        var r = new Raum { Id = d.NeueId(), Kurzname = "Th1" };
        d.Raeume.Add(r);
        Unterricht U()
        {
            var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { FachraumId = r.Id } } };
            d.Unterrichte.Add(u);
            return u;
        }
        var a = U();
        var b = U();
        var pa = Planungsdienst.Verplanen(d, a, 0, 1, false);
        Assert.Equal(r.Id, pa.Raeume[0]);
        var ix = new DatenIndex(d);
        Assert.Equal(Feldstatus.KeinRaum, Planungsdienst.Status(d, ix, b, 0, 1));
        var pb = Planungsdienst.Verplanen(d, b, 0, 1, false);
        Assert.Equal(new[] { 0 }, RaumBelegtVerplanen.OhneRaum(new DatenIndex(d), pb));
        RaumBelegtVerplanen.Anwenden(d, pb, RaumBelegtWahl.KollisionErzeugen);
        Assert.Equal((r.Id, r.Id), (pa.Raeume[0], pb.Raeume[0]));
        pb.Raeume[0] = null;
        RaumBelegtVerplanen.Anwenden(d, pb, RaumBelegtWahl.Verdraengen);
        Assert.Equal(r.Id, pb.Raeume[0]);
        Assert.Null(pa.Raeume[0]);
    }
}
