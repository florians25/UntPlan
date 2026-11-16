using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Tauschvorschläge (Handbuch, Manuelles Planen | Tauschvorschläge): Gewinn, Auswirkungen, Klassensprung, Nur verschiedene Lehrer.</summary>
public class TauschvorschlaegeTests
{
    private static (Schuldatei D, List<Lehrer> L, List<Klasse> K) Schule()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 5;
        d.Zeitraster.StundenProTag = 6;
        d.Zeitraster.LetzteVormittagsstunde = 6;
        var l = Enumerable.Range(0, 3).Select(i => new Lehrer { Id = d.NeueId(), Kurzname = "L" + i }).ToList();
        var k = Enumerable.Range(0, 2).Select(i => new Klasse { Id = d.NeueId(), Kurzname = $"{5 + i}a" }).ToList();
        d.Lehrer.AddRange(l);
        d.Klassen.AddRange(k);
        return (d, l, k);
    }

    private static Planstunde U(Schuldatei d, Lehrer l, Klasse k, string fach, int tag, int stunde)
    {
        var f = d.Faecher.FirstOrDefault(x => x.Kurzname == fach) ?? new Fach { Id = d.NeueId(), Kurzname = fach };
        if (!d.Faecher.Contains(f)) d.Faecher.Add(f);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = l.Id, FachId = f.Id, KlassenIds = { k.Id } } } };
        d.Unterrichte.Add(u);
        var p = new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = { null } };
        d.Stundenplan.Add(p);
        return p;
    }

    [Fact]
    public void ZweierTauschMitGewinnUndAuswirkung()
    {
        var (d, l, k) = Schule();
        U(d, l[0], k[0], "D", 0, 1);
        var p = U(d, l[1], k[0], "M", 0, 4);  // Hohlstunden 2 und 3
        U(d, l[2], k[0], "E", 0, 2);
        var v = Tauschvorschlaege.Berechnen(d, p);
        Assert.NotEmpty(v);
        Assert.Contains(v, x => x.Art == 2);
        Assert.All(v, x => Assert.True(x.Gesamt >= v[^1].Gesamt));
        Tauschvorschlaege.Ausfuehren(d, v[0].Zuege);
        Assert.Equal(2, v[0].Zuege.Count(z => z.Index >= 0));
    }

    [Fact]
    public void NurVerschiedeneLehrerBlendetTauscheDerselbenLehrkraftAus()
    {
        var (d, l, k) = Schule();
        var p = U(d, l[0], k[0], "D", 0, 1);
        U(d, l[0], k[0], "Ge", 1, 1);
        U(d, l[1], k[0], "M", 2, 1);
        Assert.Contains(Tauschvorschlaege.Berechnen(d, p, new() { Ringtausche = false }), x => x.Beschreibung.Contains("Ge"));
        var nur = Tauschvorschlaege.Berechnen(d, p, new() { NurVerschiedeneLehrer = true, Ringtausche = false });
        Assert.DoesNotContain(nur, x => x.Beschreibung.Contains("Ge"));
        Assert.Contains(nur, x => x.Beschreibung.Contains("M"));
    }

    [Fact]
    public void KlassensprungVerlegtDieBehinderndeStundeDerAnderenKlasse()
    {
        var (d, l, k) = Schule();
        var p = U(d, l[0], k[0], "D", 0, 1);
        U(d, l[1], k[0], "M", 0, 2);
        U(d, l[1], k[1], "M", 0, 1);  // L1 unterrichtet Mo-1 in der 6a – behindert den Tausch D ⇄ M in der 5a
        var ohne = Tauschvorschlaege.Berechnen(d, p, new() { Ringtausche = false });
        Assert.DoesNotContain(ohne, x => x.Beschreibung.Contains("Mo-2"));
        var mit = Tauschvorschlaege.Berechnen(d, p, new() { MitKlassensprung = true, Ringtausche = false });
        var z = Assert.Single(mit, x => x.Sprungklasse.Length > 0);
        Assert.Equal("6a", z.Sprungklasse);
        Assert.NotEmpty(z.Folgetausche);
        Assert.Equal(z.Gewinn + z.Folgetausche[0].Gewinn, z.Gesamt);
        Tauschvorschlaege.Ausfuehren(d, z.AlleZuege());
        Assert.Equal(0, Optimierung.Bewertung.Kennzahlen(d).Kollisionen);
    }
}
