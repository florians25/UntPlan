using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Web.Contracts;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Kopplungsstunden (mehrere Kopplungszeilen, mehrere Klassen bzw. Lehrkräfte): Lehrer- und Klassenabwesenheit, gesperrte Räume und
/// Raumänderungen erzeugen konsistente Vertretungszeilen, Tagespläne und UntPlanWeb-Stunden – ohne verlorene Klassen, ohne doppelte
/// Zeilen und mit der Raumänderung genau an den betroffenen Zeilen.
/// </summary>
public class KopplungVertretungTests
{
    private sealed record Kopplung(Schuldatei D, DateOnly Tag, Unterricht U, int Stunde, int L1, int L2, int K1, int K2, int R1, int R2, int R3);

    /// <summary>
    /// Einzelunterricht (L1, K1, R1) wird zur Kopplung: zweite Zeile mit L2 und – je nach Fall – eigener Klasse K2 bzw. ohne eigene
    /// Klassen, eigenem Raum R2 bzw. demselben Raum R1. L2, K2, R2 und R3 sind in der betrachteten Stunde frei.
    /// </summary>
    private static Kopplung Erzeugen(bool eigeneKlasse = true, bool gleicherRaum = false)
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var plan = d.AktivePeriode.Plan;
        var unterrichte = d.Unterrichte.ToDictionary(u => u.Id);
        // Stunde, zu der eine weitere Lehrkraft, eine weitere Klasse und zwei weitere Räume frei sind
        foreach (var r in Tagesplan.Regulaer(d, tag).Where(x => x.LehrerId.HasValue && x.RaumId.HasValue && x.KlassenIds.Count == 1 && x.Unterricht.Zeilen.Count == 1
                     && !Vertretungen.IstBereitschaftsfach(d, x.FachId) && string.IsNullOrEmpty(x.Unterricht.Band)))
        {
            var belegt = plan.Where(p => p.Tag == r.Planstunde.Tag && p.Stunde == r.Stunde).ToList();
            bool Frei(Func<Planstunde, Unterricht, bool> nutzt) => !belegt.Any(p => unterrichte.TryGetValue(p.UnterrichtId, out var x) && nutzt(p, x));
            var l2 = d.Lehrer.FirstOrDefault(l => !l.Ignorieren && l.Id != r.LehrerId && Frei((_, x) => x.LehrerIds.Contains(l.Id)))?.Id;
            var k2 = d.Klassen.FirstOrDefault(k => k.Id != r.KlassenIds[0] && Frei((_, x) => x.KlassenIds.Contains(k.Id)))?.Id;
            var freieRaeume = d.Raeume.Where(rm => !rm.Ignorieren && rm.Id != r.RaumId && Frei((p, _) => p.Raeume.Contains(rm.Id))).Select(rm => rm.Id).Take(2).ToList();
            if (l2 is null || k2 is null || freieRaeume.Count < 2) continue;
            var u = r.Unterricht;
            var slots = plan.Where(p => p.UnterrichtId == u.Id).ToList();
            u.Zeilen.Add(new Kopplungszeile { LehrerId = l2, FachId = r.FachId, KlassenIds = eigeneKlasse ? new() { k2.Value } : new() });
            foreach (var p in slots)
            {
                while (p.Raeume.Count < 1) p.Raeume.Add(null);
                p.Raeume.Add(gleicherRaum ? r.RaumId : freieRaeume[0]);
            }
            return new Kopplung(d, tag, u, r.Stunde, r.LehrerId!.Value, l2.Value, r.KlassenIds[0], k2.Value, r.RaumId!.Value, gleicherRaum ? r.RaumId!.Value : freieRaeume[0], freieRaeume[1]);
        }
        throw new InvalidOperationException("Keine Stunde mit freier Lehrkraft, Klasse und zwei freien Räumen.");
    }

    private static void Absenz(Kopplung k, AbwesenheitsArt art, int element)
    {
        k.D.Abwesenheiten.Add(new Abwesenheit { Id = k.D.NeueId(), Art = art, ElementId = element, Von = k.Tag, Bis = k.Tag });
        Vertretungen.Aktualisieren(k.D, k.Tag);
    }

    private static List<Vertretung> Zeilen(Kopplung k) =>
        k.D.Vertretungen.Where(v => v.Datum == k.Tag && v.Stunde == k.Stunde && v.UnterrichtId == k.U.Id).OrderBy(v => v.ZeilenIndex).ToList();

    private static TagesEintrag Eintrag(Kopplung k, int zeile) =>
        Tagesplan.Berechnen(k.D, k.Tag).Stunde(k.Stunde).Single(e => e.Unterricht?.Id == k.U.Id && e.Regel?.ZeilenIndex == zeile);

    private static List<WebStunde> WebStunden(Kopplung k)
    {
        var paket = UntPlanWebExport.PaketErstellen(k.D, k.Tag, new WebExportOptionen { TageZurueck = 0 });
        return paket.Tage.Single(t => t.Datum == k.Tag.ToString("yyyy-MM-dd")).Stunden.Where(s => s.Stunde == k.Stunde && s.Unterricht == k.U.Id).ToList();
    }

    private static string K(Kopplung k, ElementTyp t, int id) => new DatenIndex(k.D).K(t, id);

    [Fact]
    public void LehrerAbwesendBetrifftNurDieEigeneKopplungszeile()
    {
        var k = Erzeugen();
        Absenz(k, AbwesenheitsArt.Lehrer, k.L1);
        Vertretungen.Aktualisieren(k.D, k.Tag);
        var v = Assert.Single(Zeilen(k));
        Assert.Equal(0, v.ZeilenIndex);
        Assert.True(v.Offen);
        Assert.Equal(new[] { k.K1 }, v.KlassenIds);
        Assert.Equal(k.R1, v.RaumId);
        Assert.Equal(TagesStatus.Offen, Eintrag(k, 0).Status);
        var e1 = Eintrag(k, 1);
        Assert.Equal(TagesStatus.Regulaer, e1.Status);
        Assert.Equal(new[] { k.L2 }, e1.LehrerIds);
        Assert.Equal(new[] { k.R2 }, e1.RaumIds);
        // UntPlanWeb: eine offene Stunde für K1 (bisherige Lehrkraft L1) und die reguläre Stunde der zweiten Zeile
        var web = WebStunden(k);
        Assert.Equal(2, web.Count);
        var offen = Assert.Single(web, s => s.Status == "offen");
        Assert.Contains(K(k, ElementTyp.Klasse, k.K1), offen.Klassen);
        Assert.Equal(new[] { K(k, ElementTyp.Lehrer, k.L1) }, offen.LehrerAlt);
        Assert.Contains(web, s => s.Status == "regulaer" && s.Lehrer.SequenceEqual(new[] { K(k, ElementTyp.Lehrer, k.L2) }) && s.Klassen.Contains(K(k, ElementTyp.Klasse, k.K2)));
    }

    [Fact]
    public void BeideLehrkraefteAbwesendJeZeileEineVertretungOhneDoppelte()
    {
        var k = Erzeugen();
        Absenz(k, AbwesenheitsArt.Lehrer, k.L1);
        Absenz(k, AbwesenheitsArt.Lehrer, k.L2);
        Vertretungen.Aktualisieren(k.D, k.Tag);
        var zeilen = Zeilen(k);
        Assert.Equal(new[] { 0, 1 }, zeilen.Select(v => v.ZeilenIndex));
        Assert.Equal(new[] { k.K1 }, zeilen[0].KlassenIds);
        Assert.Equal(new[] { k.K2 }, zeilen[1].KlassenIds);
        Assert.Equal(new[] { k.L1, k.L2 }, zeilen.Select(v => v.LehrerId!.Value));
        Assert.All(zeilen, v => Assert.True(v.Offen));
        Assert.Equal(2, WebStunden(k).Count(s => s.Status == "offen"));
    }

    [Fact]
    public void ZeileOhneEigeneKlassenBehaeltDieKlassenDerKopplung()
    {
        var k = Erzeugen(eigeneKlasse: false);
        Absenz(k, AbwesenheitsArt.Lehrer, k.L2);
        var v = Assert.Single(Zeilen(k));
        Assert.Equal(1, v.ZeilenIndex);
        // die Vertretungszeile der zweiten Lehrkraft nennt die Klassen der Kopplung (keine leere Klassenspalte)
        Assert.Equal(new[] { k.K1 }, v.NeueKlassenIds);
        var offen = Assert.Single(WebStunden(k), s => s.Status == "offen");
        Assert.Contains(K(k, ElementTyp.Klasse, k.K1), offen.Klassen);
        // Vertretungsliste (Plan-Zeilen) ebenfalls mit Klasse
        Assert.Contains(Vertretungsplan.Zeilen(k.D, k.Tag), z => z.Id == v.Id && z.Klassen.Contains(K(k, ElementTyp.Klasse, k.K1)));
    }

    [Fact]
    public void GesperrterRaumBetrifftNurDieZeilenInDiesemRaum()
    {
        var k = Erzeugen();
        Absenz(k, AbwesenheitsArt.Raum, k.R1);
        var v = Assert.Single(Zeilen(k));
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal(0, v.ZeilenIndex);
        Assert.True(v.RaumOffen);
        Assert.Equal(new[] { k.R2 }, Eintrag(k, 1).RaumIds);

        var g = Erzeugen(gleicherRaum: true);
        Absenz(g, AbwesenheitsArt.Raum, g.R1);
        Assert.Equal(new[] { 0, 1 }, Zeilen(g).Select(x => x.ZeilenIndex));
        Assert.All(Zeilen(g), x => Assert.Equal(VertretungsArt.Raumvertretung, x.Art));
    }

    [Fact]
    public void RaumaenderungEinerZeileBzwAllerZeilen()
    {
        var k = Erzeugen();
        Raumvertretungen.Zuordnen(k.D, k.U.Id, 0, k.Tag, new[] { k.Stunde }, k.R3, false, RaumKollisionsLoesung.OhneKollision, "test");
        Vertretungen.Aktualisieren(k.D, k.Tag);
        var v = Assert.Single(Zeilen(k));
        Assert.Equal(0, v.ZeilenIndex);
        Assert.Equal(k.R3, v.VertretungsraumId);
        Assert.Equal(new[] { k.R3 }, Eintrag(k, 0).RaumIds);
        Assert.Equal(new[] { k.R2 }, Eintrag(k, 1).RaumIds);
        Assert.Equal(new[] { k.L1 }, Eintrag(k, 0).LehrerIds);
        // auch die zweite Zeile
        Raumvertretungen.Zuordnen(k.D, k.U.Id, 1, k.Tag, new[] { k.Stunde }, k.R3, true, RaumKollisionsLoesung.MitKollision, "test");
        Assert.Equal(new[] { 0, 1 }, Zeilen(k).Select(x => x.ZeilenIndex));
        Assert.Contains(k.R3, Eintrag(k, 1).RaumIds);
        var web = WebStunden(k);
        Assert.Equal(2, web.Count);
        Assert.All(web, s => Assert.Contains(K(k, ElementTyp.Raum, k.R3), s.Raeume));
    }

    [Fact]
    public void RaumaenderungUndDanachLehrerAbwesendWirdOffenUndBehaeltDenRaum()
    {
        var k = Erzeugen();
        Raumvertretungen.Zuordnen(k.D, k.U.Id, 0, k.Tag, new[] { k.Stunde }, k.R3, false, RaumKollisionsLoesung.OhneKollision, "test");
        Absenz(k, AbwesenheitsArt.Lehrer, k.L1);
        var v = Assert.Single(Zeilen(k));
        // die abwesende Lehrkraft unterrichtet nicht: die Stunde ist offen (bzw. entfällt laut Vertretungsregel), der neue Raum bleibt
        Assert.True(v.Offen || v.IstEntfall, $"Art {v.Art}, Vertreter {v.VertreterId}");
        Assert.DoesNotContain(k.L1, Eintrag(k, 0).LehrerIds);
        if (v.Offen) Assert.Equal(new[] { k.R3 }, Eintrag(k, 0).RaumIds);
        Assert.Equal(TagesStatus.Regulaer, Eintrag(k, 1).Status);
        Assert.DoesNotContain(WebStunden(k), s => s.Lehrer.Contains(K(k, ElementTyp.Lehrer, k.L1)));
        // Absenz gelöscht: wieder die Raumänderung mit der eigenen Lehrkraft
        k.D.Abwesenheiten.Clear();
        Vertretungen.Aktualisieren(k.D, k.Tag);
        v = Assert.Single(Zeilen(k));
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal(k.L1, v.VertreterId);
        Assert.Equal(k.R3, v.VertretungsraumId);
        Assert.Equal(new[] { k.L1 }, Eintrag(k, 0).LehrerIds);
    }

    [Fact]
    public void KlasseAbwesendSetztNurDieZeileDieserKlasseFrei()
    {
        var k = Erzeugen();
        Absenz(k, AbwesenheitsArt.Klasse, k.K1);
        var v = Assert.Single(Zeilen(k));
        Assert.Equal(VertretungsArt.Freisetzung, v.Art);
        Assert.Equal(0, v.ZeilenIndex);
        Assert.Equal(TagesStatus.Regulaer, Eintrag(k, 1).Status);
        // beide Klassen abwesend: beide Zeilen frei, je eine Zeile
        Absenz(k, AbwesenheitsArt.Klasse, k.K2);
        Assert.Equal(new[] { 0, 1 }, Zeilen(k).Select(x => x.ZeilenIndex));
        Assert.All(Zeilen(k), x => Assert.Equal(VertretungsArt.Freisetzung, x.Art));
    }

    [Fact]
    public void KlasseAbwesendBeiZweiterLehrkraftOhneEigeneKlassenSetztBeideFrei()
    {
        var k = Erzeugen(eigeneKlasse: false, gleicherRaum: true);
        Absenz(k, AbwesenheitsArt.Klasse, k.K1);
        Assert.Equal(new[] { 0, 1 }, Zeilen(k).Select(x => x.ZeilenIndex));
        Assert.All(Zeilen(k), x => Assert.Equal(VertretungsArt.Freisetzung, x.Art));
        Assert.All(Zeilen(k), x => Assert.Contains(k.K1, x.NeueKlassenIds));
    }

    [Fact]
    public void LehrerAbwesendUndVertreterGesetztBleibtBeiErneutemAbgleich()
    {
        var k = Erzeugen();
        Absenz(k, AbwesenheitsArt.Lehrer, k.L1);
        var v = Assert.Single(Zeilen(k));
        var frei = k.D.Lehrer.First(l => l.Id != k.L1 && l.Id != k.L2 && Vertretungen.LehrerBelegt(k.D, Tagesplan.Berechnen(k.D, k.Tag), l.Id, k.Stunde) is null);
        Vertretungen.VertreterSetzen(k.D, v, frei.Id, false, "test");
        Vertretungen.Aktualisieren(k.D, k.Tag);
        v = Assert.Single(Zeilen(k));
        Assert.Equal(frei.Id, v.VertreterId);
        Assert.Equal(new[] { frei.Id }, Eintrag(k, 0).LehrerIds);
        Assert.Equal(new[] { k.L2 }, Eintrag(k, 1).LehrerIds);
        var web = WebStunden(k);
        Assert.Contains(web, s => s.Status == "vertreten" && s.Lehrer.Contains(frei.Kurzname) && s.Klassen.Contains(K(k, ElementTyp.Klasse, k.K1)));
        Assert.Contains(web, s => s.Status == "regulaer" && s.Lehrer.Contains(K(k, ElementTyp.Lehrer, k.L2)));
    }
}
