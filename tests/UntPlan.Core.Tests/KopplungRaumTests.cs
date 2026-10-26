using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Gekoppelter Unterricht (mehrere Kopplungszeilen): ein gemeinsamer Raum gilt für alle Zeilen (Pläne, Raumsperre, Raumänderung,
/// Export); Raumänderungen aus UntPlanWeb gelten für den ganzen Unterricht; Veranstaltung mit nur einem offenen Platz bleibt in der Liste.
/// </summary>
public class KopplungRaumTests
{
    /// <summary>Unterricht mit zwei Kopplungszeilen (zwei Lehrkräfte, zwei Klassen) an einem für alle freien Termin.</summary>
    private static (Schuldatei d, DateOnly tag, Unterricht u, Planstunde p, int stunde) Kopplung(params int?[] raeume)
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var wt = Kalender.TagIndex(d, tag);
        var ix = new DatenIndex(d);
        bool Frei(int s, Func<Unterricht, bool> belegt) => !d.Stundenplan.Any(p => p.Tag == wt && p.Stunde == s && ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o) && belegt(o));
        foreach (var s in Enumerable.Range(1, d.Zeitraster.StundenProTag).Where(s => d.Zeitraster.Verfuegbar(wt, s)))
        {
            var lehrer = d.Lehrer.Where(l => Frei(s, o => o.LehrerIds.Contains(l.Id))).Take(2).ToList();
            var klassen = d.Klassen.Where(k => Frei(s, o => o.KlassenIds.Contains(k.Id))).Take(2).ToList();
            var freieRaeume = d.Raeume.Where(r => !d.Stundenplan.Any(p => p.Tag == wt && p.Stunde == s && p.Raeume.Contains(r.Id))).ToList();
            if (lehrer.Count < 2 || klassen.Count < 2 || freieRaeume.Count < 4) continue;
            var fach = d.Faecher.First(f => !f.Bereitschaft && !f.KeineVertretung).Id;
            var u = new Unterricht
            {
                Id = d.NeueId(), Wochenstunden = 1,
                Zeilen = { new Kopplungszeile { LehrerId = lehrer[0].Id, FachId = fach, KlassenIds = { klassen[0].Id } }, new Kopplungszeile { LehrerId = lehrer[1].Id, FachId = fach, KlassenIds = { klassen[1].Id } } },
            };
            d.Unterrichte.Add(u);
            var p = new Planstunde { UnterrichtId = u.Id, Tag = wt, Stunde = s, Raeume = raeume.Select((r, i) => r ?? freieRaeume[i].Id).Cast<int?>().ToList() };
            d.Stundenplan.Add(p);
            return (d, tag, u, p, s);
        }
        throw new InvalidOperationException("Kein freier Termin für die Kopplung.");
    }

    private static List<TagesEintrag> Zeilen(Schuldatei d, DateOnly tag, Unterricht u, int stunde) =>
        Tagesplan.Berechnen(d, tag).Stunde(stunde).Where(e => e.Unterricht?.Id == u.Id).OrderBy(e => e.Regel!.ZeilenIndex).ToList();

    [Fact]
    public void GemeinsamerRaumGiltFuerAlleZeilen()
    {
        var (d, tag, u, p, s) = Kopplung((int?)null);
        var r = p.Raeume[0]!.Value;
        Assert.Single(p.Raeume);
        Assert.Equal(r, p.RaumFuerZeile(1));
        var zeilen = Zeilen(d, tag, u, s);
        Assert.Equal(2, zeilen.Count);
        Assert.All(zeilen, e => Assert.Contains(r, e.RaumIds));
        // Laden: Raumliste wird mit dem gemeinsamen Raum aufgefüllt
        p.RaeumeErweitern(2);
        Assert.Equal(new int?[] { r, r }, p.Raeume);
        // Export: Raum für beide Zeilen (Pläne der Klassen bzw. Lehrkräfte)
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        var raum = d.Raeume.First(x => x.Id == r).Kurzname;
        var web = paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd")).Stunden.Where(x => x.Unterricht == u.Id).ToList();
        Assert.Equal(2, web.Count);
        Assert.All(web, x => Assert.Contains(raum, x.Raeume));
    }

    [Fact]
    public void RaumsperreTrifftAlleZeilen()
    {
        var (d, tag, u, p, s) = Kopplung((int?)null);
        var r = p.Raeume[0]!.Value;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = r, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var vs = d.Vertretungen.Where(v => v.Datum == tag && v.UnterrichtId == u.Id && v.Stunde == s).ToList();
        Assert.Equal(new[] { 0, 1 }, vs.Select(v => v.ZeilenIndex).OrderBy(x => x));
        Assert.All(vs, v => Assert.Equal(VertretungsArt.Raumvertretung, v.Art));
    }

    [Fact]
    public void RaumaenderungJeZeileUndFuerAlleZeilen()
    {
        // gemeinsamer Raum: Raumänderung einer Zeile im Desktop betrifft nur diese Zeile, die übrigen bleiben im gemeinsamen Raum;
        // werden alle Zeilen geändert, sitzen alle im neuen Raum
        var (d, tag, u, p, s) = Kopplung((int?)null);
        var r = p.Raeume[0]!.Value;
        var tp = Tagesplan.Berechnen(d, tag);
        var neu = d.Raeume.First(x => x.Id != r && !tp.Stunde(s).Any(e => e.RaumIds.Contains(x.Id))).Id;
        Raumvertretungen.Zuordnen(d, u.Id, 0, tag, new[] { s }, neu, false, RaumKollisionsLoesung.MitKollision);
        var z = Zeilen(d, tag, u, s);
        Assert.Equal(new[] { neu }, z[0].RaumIds);
        Assert.Equal(new[] { r }, z[1].RaumIds);
        Raumvertretungen.Zuordnen(d, u.Id, 1, tag, new[] { s }, neu, false, RaumKollisionsLoesung.MitKollision);
        Assert.All(Zeilen(d, tag, u, s), e => Assert.Equal(new[] { neu }, e.RaumIds));
        // getrennte Räume: nur die geänderte Zeile zieht um
        var (d2, tag2, u2, p2, s2) = Kopplung(null, null);
        var tp2 = Tagesplan.Berechnen(d2, tag2);
        var neu2 = d2.Raeume.First(x => !tp2.Stunde(s2).Any(e => e.RaumIds.Contains(x.Id))).Id;
        Raumvertretungen.Zuordnen(d2, u2.Id, 0, tag2, new[] { s2 }, neu2, false, RaumKollisionsLoesung.MitKollision);
        var z2 = Zeilen(d2, tag2, u2, s2);
        Assert.Equal(new[] { neu2 }, z2[0].RaumIds);
        Assert.Equal(new[] { p2.Raeume[1]!.Value }, z2[1].RaumIds);
    }

    private static WebRueckmeldung Meldung(Schuldatei d, DateOnly tag, int stunde, Unterricht u, IEnumerable<int> klassen, IEnumerable<int> raeume, bool mitNummer) => new()
    {
        Id = "rb-" + Guid.NewGuid().ToString("N")[..6], Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "Web", Lehrer = d.Lehrer.First(l => l.Id == u.Zeilen[0].LehrerId).Kurzname,
        Von = tag.ToString("yyyy-MM-dd"), StundeVon = stunde, StundeBis = stunde, UnterrichtNr = mitNummer ? u.Id : null,
        Klassen = klassen.Select(k => d.Klassen.First(x => x.Id == k).Kurzname).ToList(), Raeume = raeume.Select(r => d.Raeume.First(x => x.Id == r).Kurzname).ToList(),
    };

    [Fact]
    public void RaumaenderungAusDemWebGiltFuerDenGanzenUnterricht()
    {
        var (d, tag, u, p, s) = Kopplung(null, null);
        var (r0, r1) = (p.Raeume[0]!.Value, p.Raeume[1]!.Value);
        var tp = Tagesplan.Berechnen(d, tag);
        var neu = d.Raeume.First(x => !tp.Stunde(s).Any(e => e.RaumIds.Contains(x.Id))).Id;
        // Web: Klassen einer Zeile, vollständige Raumliste (Raum der Zeile 2 gestrichen, neuer Raum dazu)
        var m = Meldung(d, tag, s, u, u.Zeilen[0].KlassenIds, new[] { r0, neu }, true);
        var analyse = StundenplanAenderungen.Analysieren(d, new[] { m }, tag, tag, false);
        Assert.Equal(StplAenderungsArt.Unterricht, Assert.Single(analyse).Art);
        var (ok, nein) = StundenplanAenderungen.Uebernehmen(d, analyse, "test");
        Assert.Empty(nein);
        var z = Zeilen(d, tag, u, s);
        Assert.Equal(new[] { r0 }, z[0].RaumIds);
        Assert.Equal(new[] { neu }, z[1].RaumIds);
    }

    [Fact]
    public void KlassenUeberMehrereZeilenFindenDenUnterricht()
    {
        var (d, tag, u, p, s) = Kopplung((int?)null);
        var r = p.Raeume[0]!.Value;
        var tp = Tagesplan.Berechnen(d, tag);
        var neu = d.Raeume.First(x => x.Id != r && !tp.Stunde(s).Any(e => e.RaumIds.Contains(x.Id))).Id;
        // Klassen beider Zeilen, ohne Unterrichtsnummer: keine Aktivität (Sondereinsatz), sondern Raumänderung des Unterrichts
        var m = Meldung(d, tag, s, u, u.KlassenIds, new[] { neu }, false);
        var a = Assert.Single(StundenplanAenderungen.Analysieren(d, new[] { m }, tag, tag, false));
        Assert.Equal(StplAenderungsArt.Unterricht, a.Art);
        Assert.Equal(u.Id, a.UnterrichtId);
        StundenplanAenderungen.Uebernehmen(d, new[] { a }, "test");
        Assert.All(Zeilen(d, tag, u, s), e => Assert.Equal(new[] { neu }, e.RaumIds));
        Assert.DoesNotContain(d.Vertretungen, v => v.Art == VertretungsArt.Sondereinsatz && v.WebId == m.Id);
    }

    [Fact]
    public void VeranstaltungNurMitOffenemPlatzBleibtInDerListe()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var lehrer = d.Lehrer[0].Id;
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = 1, StundeBis = 2, KlassenIds = { d.Klassen[0].Id }, LehrerIds = { lehrer }, Text = "Wandertag" };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        // einzige Lehrkraft fällt aus, kein Ersatz
        var abw = AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag } }, Array.Empty<Abwesenheit>()).Single();
        VeranstaltungsLogik.LehrerEntfernen(d, va.Id, lehrer, new[] { tag }, abw.Id);
        Assert.Empty(va.LehrerAm(tag));
        var ids = d.Vertretungen.Where(v => v.Datum == tag && v.VeranstaltungId == va.Id && v.Art == VertretungsArt.Veranstaltung).Select(v => v.Id).ToHashSet();
        Assert.NotEmpty(ids);
        var zeilen = Vertretungsplan.Zeilen(d, tag).Where(z => ids.Contains(z.Id) || z.VeranstaltungId == va.Id && z.Art == Vertretungsplan.ArtText(d, VertretungsArt.Veranstaltung)).ToList();
        Assert.NotEmpty(zeilen);
        Assert.All(zeilen, z => Assert.Equal("", z.Vertreter));
    }
}
