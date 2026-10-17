using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Export von Raumänderungen nach UntPlanWeb: Eine offene Raumvertretung (gesperrter bzw. für einen anderen Unterricht
/// freigegebener Raum ohne Ersatzraum) geht mit neuem Raum „???“ hinaus wie im Vertretungsfenster – nicht mit dem bisherigen
/// Raum, sonst zeigt UntPlanWeb keine Raumänderung. Zugeordnete Räume bleiben unverändert.
/// </summary>
public class RaumaenderungExportTests
{
    private static (TagesEintrag E, TagesEintrag Andere) Zwei(Schuldatei d, DateOnly tag)
    {
        var tp = Tagesplan.Berechnen(d, tag);
        foreach (var e in tp.Eintraege.Where(e => e.Regel != null && e.Status == TagesStatus.Regulaer && e.RaumIds.Count == 1 && e.Unterricht!.Zeilen.Count == 1))
        {
            var o = tp.Stunde(e.Stunde).FirstOrDefault(x => x != e && x.Regel != null && x.Status == TagesStatus.Regulaer && x.RaumIds.Count == 1
                                                            && x.Unterricht!.Zeilen.Count == 1 && x.RaumIds[0] != e.RaumIds[0]);
            if (o != null) return (e, o);
        }
        throw new InvalidOperationException("keine zwei Stunden");
    }

    [Fact]
    public void Offene_Raumvertretung_geht_mit_Fragezeichen_hinaus()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var ix = new DatenIndex(d);
        var (e, andere) = Zwei(d, tag);
        var raumE = e.RaumIds[0];
        var raumAndere = andere.RaumIds[0];

        // e nimmt den Raum der anderen Stunde „ohne Kollision“: die andere verliert ihren Raum (offene Raumvertretung)
        Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, tag, new[] { e.Stunde }, raumAndere, false, RaumKollisionsLoesung.OhneKollision);
        var p = UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 1 });
        var iso = tag.ToString("yyyy-MM-dd");
        WebVertretung Von(TagesEintrag x) => p.Vertretungen.Single(v => v.Datum == iso && v.Stunde == x.Stunde
            && d.Vertretungen.Any(w => w.Id == v.Id && w.UnterrichtId == x.Unterricht!.Id));
        var neu = Von(e);
        Assert.Equal(ix.KRaum(raumE), neu.Raum);
        Assert.Equal(ix.KRaum(raumAndere), neu.NeuerRaum);
        var verdraengt = Von(andere);
        Assert.Equal(ix.KRaum(raumAndere), verdraengt.Raum);
        Assert.Equal("???", verdraengt.NeuerRaum);

        // gesperrter Raum ohne Ersatzraum: ebenfalls „???“; mit Ersatzraum der neue Raum
        var tag2 = Kalender.NaechsterUnterrichtstag(d, tag.AddDays(1));
        var (g, _) = Zwei(d, tag2);
        var gesperrt = g.RaumIds[0];
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = gesperrt, Von = tag2, Bis = tag2, StundeVon = g.Stunde, StundeBis = g.Stunde });
        Vertretungen.Aktualisieren(d, tag2);
        var iso2 = tag2.ToString("yyyy-MM-dd");
        var vg = d.Vertretungen.Single(v => v.Datum == tag2 && v.Stunde == g.Stunde && v.UnterrichtId == g.Unterricht!.Id);
        Assert.True(vg.RaumOffen);
        p = UntPlanWebExport.PaketErstellen(d, tag2, new WebExportOptionen { TageVoraus = 1 });
        var zg = p.Vertretungen.Single(v => v.Id == vg.Id);
        Assert.Equal(ix.KRaum(gesperrt), zg.Raum);
        Assert.Equal("???", zg.NeuerRaum);
        // der Vertretungsplan (Aushang, Druck) bleibt unverändert
        Assert.Equal(ix.KRaum(gesperrt), Vertretungsplan.Zeilen(d, tag2).Single(z => z.Id == vg.Id).NeuerRaum);

        var ersatz = d.Raeume.First(r => !r.Ignorieren && !Tagesplan.Berechnen(d, tag2).Stunde(g.Stunde).Any(x => x.RaumIds.Contains(r.Id)) && r.Id != gesperrt);
        Raumvertretungen.Zuordnen(d, vg, ersatz.Id, false, RaumKollisionsLoesung.OhneKollision);
        p = UntPlanWebExport.PaketErstellen(d, tag2, new WebExportOptionen { TageVoraus = 1 });
        Assert.Equal(ersatz.Kurzname, p.Vertretungen.Single(v => v.Id == vg.Id).NeuerRaum);
    }
}
