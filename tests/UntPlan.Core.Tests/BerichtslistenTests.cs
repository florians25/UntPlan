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

    [Fact]
    public void BelegungIstGegenstueckDerFreienStunden()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var frei = Berichtslisten.FreieStunden(d, tag);
        var belegung = Berichtslisten.Belegung(d, tag);
        Assert.Equal(frei.Count, belegung.Count);
        var regel = Tagesplan.Regulaer(d, tag);
        var stunde = regel.First(r => r.LehrerId.HasValue).Stunde;
        var z = belegung.Single(x => x.Datum == tag && x.Stunde == stunde);
        var f = frei.Single(x => x.Datum == tag && x.Stunde == stunde);
        Assert.Equal(z.LehrerGesamt - f.Lehrer.Count, z.LehrerBelegt);
        Assert.Equal(z.RaeumeGesamt - f.Raeume.Count, z.RaeumeBelegt);
        Assert.True(z.LehrerBelegt > 0 && z.LehrerProzent > 0 && z.LehrerProzent <= 100);
    }

    [Fact]
    public void StundenListeJeLehrkraftNachTagUndStunde()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var liste = Berichtslisten.StundenListe(d, tag);
        Assert.NotEmpty(liste);
        var montag = Kalender.Montag(tag);
        var erwartet = Enumerable.Range(0, 7).Select(i => montag.AddDays(i)).Sum(t => Tagesplan.Regulaer(d, t).Count(r => r.LehrerId.HasValue));
        Assert.Equal(erwartet, liste.Sum(x => x.Stunden.Count));
        foreach (var (_, stunden) in liste)
            Assert.Equal(stunden.OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList(), stunden);
    }

    [Fact]
    public void AbrechnungZeigtFachLangnameWieImHandbuch()
    {
        var d = Testdaten.Verplant();
        var fach = d.Faecher.First();
        fach.Langname = "Mathematik";
        var tag = Testdaten.Unterrichtstag(d);
        ZaehlZeile Zeile(Vertretung? v, string code, string fachText) => new(v, tag, 1, code, fachText, "", "", "", 1, true, false, false, "");
        // Vertretung: Langname des Fachs statt Kurzname
        Assert.Equal("Mathematik", Berichtslisten.AbrechnungFach(d, Zeile(new Vertretung { FachId = fach.Id, Art = VertretungsArt.Vertretung }, "+V", fach.Kurzname)));
        // Vertretungsfach geht vor
        var anderes = d.Faecher.Skip(1).First();
        anderes.Langname = "";
        Assert.Equal(anderes.Kurzname, Berichtslisten.AbrechnungFach(d, Zeile(new Vertretung { FachId = fach.Id, VertretungsfachId = anderes.Id }, "+V", "x")));
        // Pausenaufsicht ohne Fach, Wertkorrektur mit ihrer Art
        Assert.Equal("", Berichtslisten.AbrechnungFach(d, Zeile(new Vertretung { Art = VertretungsArt.Pausenaufsicht }, "P", "Gang")));
        Assert.Equal("V", Berichtslisten.AbrechnungFach(d, Zeile(null, "WK", "V")));
    }

    [Fact]
    public void WertkorrekturListeImZeitraumMitWirkung()
    {
        var d = Testdaten.Verplant();
        var l = d.Lehrer.First();
        var von = d.Einstellungen.SchuljahrVon;
        l.Wertkorrekturen.Add(new Wertkorrektur { Datum = von.AddDays(10), Art = "E", Wert = 2, Text = "b" });
        l.Wertkorrekturen.Add(new Wertkorrektur { Datum = von.AddDays(5), Art = "V", Wert = 3, Text = "a" });
        l.Wertkorrekturen.Add(new Wertkorrektur { Datum = von.AddDays(6), Art = "+", Wert = 1 });
        l.Wertkorrekturen.Add(new Wertkorrektur { Datum = von.AddDays(60), Art = "Y", Wert = 1 });
        var liste = Berichtslisten.Wertkorrekturen(new[] { l }, von, von.AddDays(30));
        Assert.Equal(new[] { "V", "+", "E" }, liste.Select(z => z.Art));
        Assert.Equal(3 + 0 - 2, liste.Sum(z => z.Zaehlerwirkung));
        Assert.Equal("Unterrichtswert erhöhen", liste[1].Wirkung);
    }
}
