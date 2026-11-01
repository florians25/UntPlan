using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Aufsichtsplanung im Vertretungsmodus: Änderungen werden als Vertretungen des Tages erfasst, der Aufsichtsplan bleibt.</summary>
public class PausenaufsichtVertretungTests
{
    private static (Schuldatei d, DateOnly tag, Pausenaufsicht a) Daten()
    {
        var d = Testdaten.Verplant();
        Pausenaufsichten.Optimieren(d);
        var tag = Testdaten.Unterrichtstag(d);
        var wt = Kalender.TagIndex(d, tag);
        var a = d.Pausenaufsichten.First(x => x.Tag == wt && x.Datum is null);
        return (d, tag, a);
    }

    [Fact]
    public void VertreterSetzenErfasstVertretungUndLaesstDenPlan()
    {
        var (d, tag, a) = Daten();
        var plan = d.Pausenaufsichten.Count;
        var ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Assert.Equal(a.LehrerId, ta.Wirksam);
        var neu = Pausenaufsichten.LehrervorschlagAmDatum(d, tag, ta).First().Lehrer.Id;
        Assert.NotEqual(a.LehrerId, neu);
        var v = Pausenaufsichten.VertretungSetzen(d, tag, ta, neu)!;
        Assert.Equal(VertretungsArt.Pausenaufsicht, v.Art);
        Assert.Equal(a.LehrerId, v.LehrerId);
        Assert.Equal(neu, v.VertreterId);
        Assert.Equal(a.GangId, v.GangId);
        Assert.Equal(plan, d.Pausenaufsichten.Count);
        Assert.Contains(d.Pausenaufsichten, x => x == a && x.LehrerId == a.LehrerId);
        ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Assert.Equal(neu, ta.Wirksam);
        // wieder die geplante Lehrkraft: die Vertretungszeile verschwindet
        Pausenaufsichten.VertretungSetzen(d, tag, ta, a.LehrerId);
        Assert.DoesNotContain(d.Vertretungen, x => x.GangId == a.GangId && x.Datum == tag);
        // offen und entfällt
        ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Pausenaufsichten.VertretungSetzen(d, tag, ta, null);
        ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Assert.True(ta.Offen);
        Assert.True(ta.Vertretung!.Offen);
        Pausenaufsichten.VertretungSetzen(d, tag, ta, null, entfall: true);
        ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Assert.True(ta.Entfaellt);
        Assert.Null(ta.Wirksam);
        Assert.Single(d.Vertretungen, x => x.GangId == a.GangId && x.Datum == tag);
    }

    [Fact]
    public void HandvertretungUndDanachAbwesenheitGibtKeineZweiteZeile()
    {
        var (d, tag, a) = Daten();
        var ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        var neu = Pausenaufsichten.LehrervorschlagAmDatum(d, tag, ta).First().Lehrer.Id;
        Pausenaufsichten.VertretungSetzen(d, tag, ta, neu);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = a.LehrerId, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var zeilen = d.Vertretungen.Where(x => x.Datum == tag && x.GangId == a.GangId && x.Stunde == a.NachStunde && x.LehrerId == a.LehrerId).ToList();
        var z = Assert.Single(zeilen);
        Assert.Equal(neu, z.VertreterId);
        Assert.NotNull(z.AbwesenheitId);
    }

    [Fact]
    public void AbwesendeAufsichtIstOffenUndLaesstSichBesetzen()
    {
        var (d, tag, a) = Daten();
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = a.LehrerId, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var ta = Pausenaufsichten.AmDatum(d, tag, a.GangId, a.NachStunde).Single(x => x.Regulaer == a.LehrerId);
        Assert.True(ta.Offen);
        var neu = Pausenaufsichten.LehrervorschlagAmDatum(d, tag, ta).First().Lehrer.Id;
        Assert.NotEqual(a.LehrerId, neu);
        Pausenaufsichten.VertretungSetzen(d, tag, ta, neu);
        Vertretungen.Aktualisieren(d, tag);
        var z = Assert.Single(d.Vertretungen, x => x.Datum == tag && x.GangId == a.GangId && x.Stunde == a.NachStunde && x.LehrerId == a.LehrerId);
        Assert.Equal(neu, z.VertreterId);
    }
}
