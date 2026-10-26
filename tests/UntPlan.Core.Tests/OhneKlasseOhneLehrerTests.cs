using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Lehrkräfte können Unterricht ohne Klassen haben (z. B. AG, Förderunterricht, Projekt): Eingabe ohne Pflicht-Klasse, Pläne,
/// Kollisionen, Vertretungsplanung und UntPlanWeb-Export. Veranstaltungen ohne Lehrkraft zeigen bzw. senden kein „---“.
/// </summary>
public class OhneKlasseOhneLehrerTests
{
    /// <summary>Unterricht nur mit Lehrkraft, Fach und Raum – an einem für Lehrkraft und Raum freien Termin verplant.</summary>
    private static (Schuldatei d, DateOnly tag, Unterricht u, Planstunde p, int lehrer, int raum) OhneKlasse()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var wt = ((int)tag.DayOfWeek + 6) % 7;
        var lehrer = d.Lehrer[0].Id;
        var fach = d.Faecher.First(f => !f.Bereitschaft && !f.KeineVertretung).Id;
        var ix = new DatenIndex(d);
        // Stunde, in der die Lehrkraft frei ist, und ein Raum, der dann frei ist
        var stunde = Enumerable.Range(0, d.Zeitraster.StundenProTag).First(s => d.Zeitraster.Verfuegbar(wt, s)
            && !d.Stundenplan.Any(p => p.Tag == wt && p.Stunde == s && ix.Unterrichte.TryGetValue(p.UnterrichtId, out var o) && o.LehrerIds.Contains(lehrer)));
        var raum = d.Raeume.First(r => !d.Stundenplan.Any(p => p.Tag == wt && p.Stunde == stunde && p.Raeume.Contains(r.Id))).Id;
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = lehrer, FachId = fach, FachraumId = raum } } };
        d.Unterrichte.Add(u);
        var p = new Planstunde { UnterrichtId = u.Id, Tag = wt, Stunde = stunde, Raeume = { raum } };
        d.Stundenplan.Add(p);
        return (d, tag, u, p, lehrer, raum);
    }

    [Fact]
    public void EingabeVerlangtKeineKlasse()
    {
        var (d, _, u, _, _, _) = OhneKlasse();
        Assert.DoesNotContain(Unterrichtseingabe.Pflichthinweise(u, d), x => x.StartsWith("Klasse fehlt"));
        Assert.False(Unterrichtseingabe.PflichtFehlt(u, 0, "Klassen", d));
        Assert.False(Unterrichtseingabe.KlasseNoetig(d, u));
        // ganz ohne Angaben: weiterhin Fach und Wochenstunden als Pflicht, die Klasse nicht
        var leer = new Unterricht { Id = 1, Wochenstunden = 0, Zeilen = { new Kopplungszeile() } };
        Assert.DoesNotContain(Unterrichtseingabe.Pflichthinweise(leer), x => x.StartsWith("Klasse fehlt"));
        // Diagnose meldet Unterricht mit Lehrkraft nicht als „ohne Klasse“
        var punkt = Diagnose.Eingabedaten(d).FirstOrDefault(x => x.Titel == "Unterricht ohne Klasse");
        Assert.True(punkt is null || punkt.Faelle.All(f => f.UnterrichtId != u.Id));
    }

    [Fact]
    public void PlaeneUndKollisionen()
    {
        var (d, tag, u, p, lehrer, raum) = OhneKlasse();
        var tp = Tagesplan.Berechnen(d, tag);
        var e = Assert.Single(tp.VonLehrer(lehrer), x => x.Unterricht?.Id == u.Id);
        Assert.Empty(e.KlassenIds);
        Assert.Contains(raum, e.RaumIds);
        Assert.Contains(tp.Stunde(e.Stunde), x => x.Findetstatt && x.RaumIds.Contains(raum) && x.Unterricht?.Id == u.Id);
        // optimierbar und Kollision mit anderem Unterricht derselben Lehrkraft wird erkannt
        var modell = new PlanModell(d, false);
        Assert.True(modell.IstPlanbar(u));
        var ix = new DatenIndex(d);
        var anderer = d.Stundenplan.First(x => x.UnterrichtId != u.Id && ix.Unterrichte[x.UnterrichtId].LehrerIds.Contains(lehrer));
        var hind = Planungsdienst.Hindernisse(d, ix, u, anderer.Tag, anderer.Stunde);
        Assert.Contains(hind, h => h.Planstunde == anderer);
        Assert.Contains(Planungsdienst.Hindernisse(d, ix, ix.Unterrichte[anderer.UnterrichtId], p.Tag, p.Stunde), h => h.Planstunde == p);
    }

    [Fact]
    public void AbsenzErzeugtVertretungOhneKlasseUndExport()
    {
        var (d, tag, u, _, lehrer, _) = OhneKlasse();
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var v = Assert.Single(d.Vertretungen, x => x.Datum == tag && x.UnterrichtId == u.Id);
        Assert.True(v.Offen);
        Assert.Empty(v.KlassenIds);
        Assert.False(Vertretungen.NichtInListe(d, v));
        // Vertretungsvorschlag und Vertretungsliste funktionieren ohne Klasse
        Assert.NotEmpty(VertretungsVorschlag.Berechnen(d, v));
        var zeile = Assert.Single(Vertretungsplan.Zeilen(d, tag), z => z.Id == v.Id);
        Assert.Equal("", zeile.Klassen);
        Assert.Equal("???", zeile.Vertreter);
        // UntPlanWeb: Vertretungszeile und offene Stunde im Plan der Lehrkraft (ohne Klassen)
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        var wv = Assert.Single(paket.Vertretungen, x => x.Id == v.Id);
        Assert.Empty(wv.Klassen);
        var webTag = paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd"));
        var ws = Assert.Single(webTag.Stunden, x => x.Unterricht == u.Id);
        Assert.Equal("offen", ws.Status);
        Assert.Empty(ws.Klassen);
        // Vertreter eingetragen: vertreten, im Export mit Vertreter
        var vertreter = VertretungsVorschlag.Berechnen(d, v).First().Lehrer.Id;
        Vertretungen.VertreterSetzen(d, v, vertreter);
        Assert.False(v.Offen);
        Assert.Contains(Tagesplan.Berechnen(d, tag).VonLehrer(vertreter), x => x.Vertretung == v);
        Assert.Equal(d.Lehrer.First(l => l.Id == vertreter).Kurzname, Vertretungsplan.Zeilen(d, tag).Single(z => z.Id == v.Id).Vertreter);
    }

    [Fact]
    public void VeranstaltungOhneLehrerOhneStriche()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var klasse = d.Klassen[0].Id;
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = 1, StundeBis = 3, KlassenIds = new() { klasse }, Text = "Ausflug" };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        Assert.True(VeranstaltungsLogik.OhneLehrer(va, tag));
        var betroffen = d.Vertretungen.Where(v => v.Datum == tag && v.VeranstaltungId == va.Id).ToList();
        Assert.Contains(betroffen, v => v.IstEntfall);
        Assert.All(betroffen, v => Assert.True(VeranstaltungsLogik.OhneLehrer(d, v)));
        var ids = betroffen.Select(v => v.Id).ToHashSet();
        var zeilen = Vertretungsplan.Zeilen(d, tag).Where(z => ids.Contains(z.Id)).ToList();
        Assert.NotEmpty(zeilen);
        Assert.All(zeilen, z => Assert.NotEqual("---", z.Vertreter));
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        var web = paket.Vertretungen.Where(w => ids.Contains(w.Id)).ToList();
        Assert.NotEmpty(web);
        Assert.All(web, w => Assert.NotEqual("---", w.Vertreter));

        // mit Lehrkraft: Entfall/Freisetzung behalten „---“ (wie bisher)
        va.LehrerIds.Add(d.Lehrer.Last().Id);
        Vertretungen.Aktualisieren(d, tag);
        Assert.False(VeranstaltungsLogik.OhneLehrer(va, tag));
        var mit = d.Vertretungen.Where(v => v.Datum == tag && v.VeranstaltungId == va.Id && v.IstEntfall).Select(v => v.Id).ToHashSet();
        Assert.Contains(Vertretungsplan.Zeilen(d, tag), z => mit.Contains(z.Id) && z.Vertreter == "---");
    }
}
