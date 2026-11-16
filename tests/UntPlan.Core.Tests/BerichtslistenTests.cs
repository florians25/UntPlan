using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Listen der Listenauswahl: „Freie Stunden“ und „Fach-Stunden-Liste“.</summary>
public class BerichtslistenTests
{
    [Fact]
    public void FreieStundenOhneBelegteLehrkraefteUndRaeume()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var liste = Berichtslisten.FreieStunden(d, tag);
        Assert.NotEmpty(liste);
        Assert.All(liste, z => Assert.Equal(Kalender.Montag(tag), Kalender.Montag(z.Datum)));
        var regel = Tagesplan.Regulaer(d, tag);
        var belegt = regel.First(r => r.LehrerId.HasValue && r.RaumId.HasValue);
        var zeile = liste.Single(z => z.Datum == tag && z.Stunde == belegt.Stunde);
        Assert.DoesNotContain(zeile.Lehrer, l => l.Id == belegt.LehrerId);
        Assert.DoesNotContain(zeile.Raeume, r => r.Id == belegt.RaumId);
        // wer laut Stundenplan in dieser Stunde keinen Unterricht hat, steht in der Liste
        var frei = d.Lehrer.Where(l => !l.Ignorieren && !regel.Any(r => r.Stunde == belegt.Stunde && r.LehrerId == l.Id)).Select(l => l.Id).ToHashSet();
        Assert.True(zeile.Lehrer.Select(l => l.Id).ToHashSet().IsSubsetOf(frei));
        Assert.Equal(d.Raeume.Count(r => !r.Ignorieren && !regel.Any(x => x.Stunde == belegt.Stunde && x.RaumId == r.Id)), zeile.Raeume.Count);
    }

    [Fact]
    public void FachStundenSummenJeKlasseUndLehrkraft()
    {
        var d = Testdaten.Verplant();
        var bloecke = Berichtslisten.FachStunden(d);
        Assert.NotEmpty(bloecke);
        foreach (var b in bloecke)
        {
            Assert.Equal(b.Zeilen.Sum(z => z.Stunden), b.Summe);
            Assert.Equal(b.Zeilen.Where(z => z.Lehrer.Length > 0).Sum(z => z.Stunden), b.JeLehrer.Sum(x => x.Stunden));
        }
        // Summe aller Blöcke = Wochenstunden aller Kopplungszeilen mit Fach
        var erwartet = d.Unterrichte.Where(u => !u.Ignorieren && u.Wochenstunden > 0).Sum(u => u.Zeilen.Count(z => z.FachId.HasValue && d.Faecher.Any(f => f.Id == z.FachId)) * u.Wochenstunden);
        Assert.Equal(erwartet, bloecke.Sum(b => b.Summe));
        // neuer Unterricht: 3 Stunden einer Lehrkraft in einer Klasse erscheinen im Block des Fachs
        var fach = new Fach { Id = d.NeueId(), Kurzname = "TST" };
        d.Faecher.Add(fach);
        d.Unterrichte.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 3, Zeilen = { new Kopplungszeile { FachId = fach.Id, LehrerId = d.Lehrer[0].Id, KlassenIds = { d.Klassen[0].Id } } } });
        var block = Berichtslisten.FachStunden(d).Single(b => b.Fach.Id == fach.Id);
        Assert.Equal(new Berichtslisten.FachStundenZeile(d.Klassen[0].Kurzname, d.Lehrer[0].Kurzname, 3), Assert.Single(block.Zeilen));
        Assert.Equal((d.Klassen[0].Kurzname, 3), Assert.Single(block.JeKlasse));
        Assert.Equal(3, block.Summe);
    }
}
