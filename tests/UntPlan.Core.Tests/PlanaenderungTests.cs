using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Bestehende Absenzen wirken nach einer Stundenplanänderung auch auf neu eingeplante Stunden.</summary>
public class PlanaenderungTests
{
    [Fact]
    public void Klassenabwesenheit_wirkt_auf_neu_eingeplante_Stunde()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var klasse = d.Klassen.First(k => k.Kurzname == "5a");
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = klasse.Id, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var regel = Tagesplan.Regulaer(d, tag).First(r => r.KlassenIds.SequenceEqual(new[] { klasse.Id }));
        var periode = d.PeriodeAm(tag);
        var p = regel.Planstunde;
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.UnterrichtId == regel.Unterricht.Id && v.Stunde == p.Stunde && v.Art == VertretungsArt.Freisetzung);

        // Stunde aus dem Plan nehmen (vollständiger Abgleich): die Freisetzung verschwindet
        periode.Plan.Remove(p);
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.UnterrichtId == regel.Unterricht.Id && v.Stunde == p.Stunde && v.Art == VertretungsArt.Freisetzung);

        // wieder (bzw. zusätzlich) einplanen: die geplante Klassenabwesenheit gilt sofort auch für diese Stunde
        periode.Plan.Add(p);
        Vertretungen.NachPlanaenderung(d, tag);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.UnterrichtId == regel.Unterricht.Id && v.Stunde == p.Stunde && v.Art == VertretungsArt.Freisetzung);
    }

    [Fact]
    public void Dokument_gleicht_nach_Stundenplanaenderung_ab()
    {
        var d = Testdaten.Verplant();
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var tag = Enumerable.Range(0, 60).Select(i => heute.AddDays(i)).FirstOrDefault(t => Kalender.IstUnterrichtstag(d, t) && Tagesplan.Regulaer(d, t).Count > 0);
        if (tag == default) return;
        var klasse = d.Klassen.First(k => k.Kurzname == "5a");
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = klasse.Id, Von = tag, Bis = tag });
        var doc = new Dokument(d);
        var regel = Tagesplan.Regulaer(d, tag).FirstOrDefault(r => r.KlassenIds.SequenceEqual(new[] { klasse.Id }));
        if (regel is null) return;
        Assert.DoesNotContain(doc.Daten.Vertretungen, v => v.Datum == tag && v.Art == VertretungsArt.Freisetzung);
        doc.Aendern("Stunde verschoben", AenderungsArt.Stundenplan, x => { });
        Assert.Contains(doc.Daten.Vertretungen, v => v.Datum == tag && v.Art == VertretungsArt.Freisetzung);
    }
}
