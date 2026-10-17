using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;
using Feld = UntPlan.Core.Vertretungsplanung.VeranstaltungsAuswahl.Feld;

namespace UntPlan.Core.Tests;

/// <summary>Sprint 27: Schnellauswahl in Veranstaltungen (alle Lehrkräfte/Klassen/Räume, Gruppen, aus Stunden übernehmen).</summary>
public class VeranstaltungsAuswahlTests
{
    private static (Schuldatei d, DateOnly tag) Daten()
    {
        var d = Testdaten.Verplant();
        return (d, Testdaten.Unterrichtstag(d));
    }

    [Fact]
    public void AlleOhneIgnorierteUndInStammdatenReihenfolge()
    {
        var (d, _) = Daten();
        d.Lehrer[1].Ignorieren = true;
        var alle = VeranstaltungsAuswahl.Alle(d, ElementTyp.Lehrer);
        Assert.Equal(d.Lehrer.Count - 1, alle.Count);
        Assert.DoesNotContain(d.Lehrer[1].Id, alle);
        Assert.Equal(Stammdaten.Sortiert(d, ElementTyp.Lehrer).Where(e => !e.Ignorieren).Select(e => e.Id), alle);
        Assert.Equal(d.Klassen.Count, VeranstaltungsAuswahl.Alle(d, ElementTyp.Klasse).Count);
        Assert.Equal(d.Raeume.Count, VeranstaltungsAuswahl.Alle(d, ElementTyp.Raum).Count);
    }

    [Fact]
    public void AlleBeachtetZeitraumVonKlassenUndLehrkraeften()
    {
        var (d, tag) = Daten();
        d.Lehrer[0].Austritt = tag.AddDays(-1);
        d.Klassen[0].VonDatum = tag.AddDays(10);
        Assert.DoesNotContain(d.Lehrer[0].Id, VeranstaltungsAuswahl.Alle(d, ElementTyp.Lehrer, tag, tag));
        Assert.DoesNotContain(d.Klassen[0].Id, VeranstaltungsAuswahl.Alle(d, ElementTyp.Klasse, tag, tag));
        Assert.Contains(d.Klassen[0].Id, VeranstaltungsAuswahl.Alle(d, ElementTyp.Klasse, tag, tag.AddDays(10)));
    }

    [Fact]
    public void GruppenNachJahrgangAbteilungUndKlassengruppe()
    {
        var (d, _) = Daten();
        var abt = new Abteilung { Id = d.NeueId(), Kurzname = "SekI" };
        d.Abteilungen.Add(abt);
        var jg5 = d.Klassen.Where(k => k.Jahrgang == "5").ToList();
        Assert.NotEmpty(jg5);
        foreach (var k in jg5) k.AbteilungId = abt.Id;
        jg5[0].Klassengruppe = "Bili";
        var g = VeranstaltungsAuswahl.Gruppen(d, ElementTyp.Klasse);
        Assert.Equal("Alle Klassen", g[0].Name);
        Assert.Equal(d.Klassen.Count, g[0].Ids.Count);
        Assert.Equal(jg5.Select(k => k.Id), g.Single(x => x.Name == "Jahrgang 5").Ids);
        Assert.Equal(jg5.Select(k => k.Id), g.Single(x => x.Name == "Abteilung SekI").Ids);
        Assert.Equal("Abteilung", g.Single(x => x.Name == "Abteilung SekI").Bereich);
        Assert.Equal(new[] { jg5[0].Id }, g.Single(x => x.Name == "Klassengruppe Bili").Ids);
        Assert.All(g, x => Assert.NotEmpty(x.Ids));
    }

    [Fact]
    public void LehrerGruppenDerKlassenDerVeranstaltung()
    {
        var (d, tag) = Daten();
        var k = d.Klassen.First(x => x.Jahrgang == "5");
        k.KlassenlehrerIds = new() { d.Lehrer[2].Id };
        var va = new Veranstaltung { Von = tag, Bis = tag, KlassenIds = { k.Id } };
        var g = VeranstaltungsAuswahl.Gruppen(d, ElementTyp.Lehrer, va);
        Assert.Equal("Alle Lehrkräfte", g[0].Name);
        var erwartet = d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.LehrerId.HasValue && z.KlassenIds.Contains(k.Id)).Select(z => z.LehrerId!.Value).ToHashSet();
        Assert.Equal(erwartet, g.Single(x => x.Name == "Lehrkräfte der Klassen").Ids.ToHashSet());
        Assert.Equal(new[] { d.Lehrer[2].Id }, g.Single(x => x.Name == "Klassenleitungen der Klassen").Ids);
        // ohne Klassen in der Veranstaltung gibt es diese Gruppen nicht
        Assert.DoesNotContain(VeranstaltungsAuswahl.Gruppen(d, ElementTyp.Lehrer), x => x.Name == "Lehrkräfte der Klassen");
    }

    [Fact]
    public void RaumGruppenNachRaumgruppeUndStammraum()
    {
        var (d, tag) = Daten();
        var rg = d.Raumgruppen.First(x => x.RaumIds.Count > 0);
        var k = d.Klassen.First(x => x.StammraumId.HasValue);
        var g = VeranstaltungsAuswahl.Gruppen(d, ElementTyp.Raum, new Veranstaltung { Von = tag, Bis = tag, KlassenteilIds = { k.Id } });
        Assert.Equal("Alle Räume", g[0].Name);
        Assert.Equal(rg.RaumIds.ToHashSet(), g.Single(x => x.Name == "Raumgruppe " + rg.Kurzname).Ids.ToHashSet());
        Assert.Equal(new[] { k.StammraumId!.Value }, g.Single(x => x.Name == "Stammräume der Klassen").Ids);
        Assert.Contains(g, x => x.Bereich == "Raumart");
    }

    [Fact]
    public void UebernehmenErgaenztOhneDoppelteUndTrenntKlasseVonKlTeil()
    {
        var (d, tag) = Daten();
        var va = new Veranstaltung { Von = tag, Bis = tag, KlassenIds = { d.Klassen[0].Id }, KlassenteilIds = { d.Klassen[1].Id } };
        var alle = VeranstaltungsAuswahl.Alle(d, ElementTyp.Klasse);
        var neu = VeranstaltungsAuswahl.Uebernehmen(va, Feld.Klassen, alle);
        Assert.Equal(alle.Count - 1, neu);
        Assert.Equal(alle.Count, va.KlassenIds.Count);
        Assert.Equal(d.Klassen[0].Id, va.KlassenIds[0]); // Reihenfolge bleibt
        Assert.Empty(va.KlassenteilIds); // Kl-Teil-Klasse ist jetzt ganze Klasse
        Assert.Equal(0, VeranstaltungsAuswahl.Uebernehmen(va, Feld.Klassen, alle));
        // Kl-Teil übernimmt eine Klasse und entfernt sie aus „Klasse“
        VeranstaltungsAuswahl.Uebernehmen(va, Feld.Klassenteil, new[] { d.Klassen[2].Id });
        Assert.DoesNotContain(d.Klassen[2].Id, va.KlassenIds);
        // Ersetzen mit leerer Liste leert das Feld
        VeranstaltungsAuswahl.Uebernehmen(va, Feld.Lehrer, VeranstaltungsAuswahl.Alle(d, ElementTyp.Lehrer));
        Assert.Equal(d.Lehrer.Count, va.LehrerIds.Count);
        VeranstaltungsAuswahl.Uebernehmen(va, Feld.Lehrer, Array.Empty<int>(), ersetzen: true);
        Assert.Empty(va.LehrerIds);
    }

    [Fact]
    public void AusStundenUebernimmtLehrkraefteUndRaeumeDerKlassen()
    {
        var (d, tag) = Daten();
        var regel = Tagesplan.Regulaer(d, tag);
        var k = regel.Where(r => r.LehrerId.HasValue && r.KlassenIds.Count > 0).SelectMany(r => r.KlassenIds).First();
        var va = new Veranstaltung { Von = tag, Bis = tag, StundeVon = 1, StundeBis = 3, KlassenIds = { k } };
        var e = VeranstaltungsAuswahl.AusStunden(d, va);
        var erwartet = regel.Where(r => r.Stunde <= 3 && r.KlassenIds.Contains(k)).ToList();
        Assert.NotEmpty(erwartet);
        Assert.Equal(erwartet.Where(r => r.LehrerId.HasValue).Select(r => r.LehrerId!.Value).ToHashSet(), e.LehrerIds.ToHashSet());
        Assert.Equal(erwartet.Where(r => r.RaumId.HasValue).Select(r => r.RaumId!.Value).ToHashSet(), e.RaumIds.ToHashSet());
        Assert.Contains(k, e.KlassenIds);
        // keine Stunden außerhalb der Veranstaltung
        Assert.DoesNotContain(regel.Where(r => r.Stunde > 3 && r.LehrerId.HasValue && !erwartet.Any(x => x.LehrerId == r.LehrerId)).Select(r => r.LehrerId!.Value), e.LehrerIds.Contains);

        var n = VeranstaltungsAuswahl.AllesUebernehmen(va, e);
        Assert.True(n > 0);
        Assert.Equal(e.LehrerIds.ToHashSet(), va.LehrerIds.ToHashSet());
        Assert.Equal(0, VeranstaltungsAuswahl.AllesUebernehmen(va, e));
    }

    [Fact]
    public void AusUnterrichtLiefertKlassenLehrerUndRaeumeDerPlanstunde()
    {
        var (d, _) = Daten();
        var p = d.Stundenplan.First(x => x.Raeume.Any(r => r.HasValue));
        var u = d.Unterrichte.First(x => x.Id == p.UnterrichtId);
        var e = VeranstaltungsAuswahl.AusUnterricht(d, u, p);
        Assert.Equal(u.KlassenIds.ToHashSet(), e.KlassenIds.ToHashSet());
        Assert.Equal(u.LehrerIds.ToHashSet(), e.LehrerIds.ToHashSet());
        Assert.Equal(p.Raeume.Where(r => r.HasValue).Select(r => r!.Value).ToHashSet(), e.RaumIds.ToHashSet());
        // Klasse, die schon Kl-Teil ist, bleibt im Kl-Teil
        var va = new Veranstaltung { KlassenteilIds = { u.KlassenIds.First() } };
        VeranstaltungsAuswahl.AllesUebernehmen(va, e);
        Assert.DoesNotContain(u.KlassenIds.First(), va.KlassenIds);
        Assert.Contains(u.KlassenIds.First(), va.KlassenteilIds);
    }
}

/// <summary>Sprint 27: Stundenplan-Synchronisation bei Klick auf eine leere Zelle (nicht verplante Stunde).</summary>
public class PlanSynchronisationTests
{
    [Fact]
    public void RegelplanZuRegelplanBehaeltTagUndStunde()
    {
        var d = Testdaten.Verplant();
        var z = PlanSynchronisation.AusSpalte(d, 2, 4, null)!;
        Assert.Equal(new PlanZelle(2, 4), z);
        Assert.Equal(2, PlanSynchronisation.ZielSpalte(d, z, null, d.Zeitraster.TageProWoche));
    }

    [Fact]
    public void TagesplanZuTagesplanAndererWocheGleicherWochentag()
    {
        var d = Testdaten.Verplant();
        var montag = Kalender.Montag(Testdaten.Unterrichtstag(d));
        var z = PlanSynchronisation.AusSpalte(d, 3, 5, montag)!; // Donnerstag
        Assert.Equal(montag.AddDays(3), z.Datum);
        Assert.Equal(Kalender.TagIndex(d, montag.AddDays(3)), z.Tag);
        var andereWoche = montag.AddDays(7);
        var s = PlanSynchronisation.ZielSpalte(d, z, andereWoche, d.Zeitraster.TageProWoche);
        Assert.Equal(3, s);
        Assert.Equal(andereWoche.AddDays(3), PlanSynchronisation.ZielDatum(andereWoche, s!.Value));
    }

    [Fact]
    public void ZwischenRegelplanUndTagesplan()
    {
        var d = Testdaten.Verplant();
        var montag = Kalender.Montag(Testdaten.Unterrichtstag(d));
        // tagesaktuell → Regelplan: Tag aus dem Datum
        var z = PlanSynchronisation.AusSpalte(d, 1, 2, montag)!;
        Assert.Equal(Kalender.TagIndex(d, montag.AddDays(1)), PlanSynchronisation.ZielSpalte(d, z, null, d.Zeitraster.TageProWoche));
        // Regelplan → tagesaktuell: Wochentag ab dem ersten Schultag
        var r = PlanSynchronisation.AusSpalte(d, 1, 2, null)!;
        Assert.Equal((1 + d.Zeitraster.ErsterSchultag) % 7, PlanSynchronisation.ZielSpalte(d, r, montag, d.Zeitraster.TageProWoche));
    }

    [Fact]
    public void NichtDarstellbareTageUndUngueltigeZellen()
    {
        var d = Testdaten.Verplant();
        var montag = Kalender.Montag(Testdaten.Unterrichtstag(d));
        Assert.Null(PlanSynchronisation.AusSpalte(d, -1, 2, null));
        Assert.Null(PlanSynchronisation.AusSpalte(d, 1, 0, null));
        Assert.Null(PlanSynchronisation.AusSpalte(d, d.Zeitraster.TageProWoche, 1, null));
        // Samstag aus einem tagesaktuellen Plan ist im Regelplan (5 Tage) nicht darstellbar
        if (d.Zeitraster.TageProWoche == 5 && d.Zeitraster.ErsterSchultag == 0)
        {
            var sa = new PlanZelle(-1, 2, montag.AddDays(5));
            Assert.Null(PlanSynchronisation.ZielSpalte(d, sa, null, 5));
            Assert.Null(PlanSynchronisation.ZielSpalte(d, sa, montag, 5));
        }
    }
}
