using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

public class StundenplanmodusTests
{
    private static (Schuldatei D, Lehrer L, Klasse K, Fach F1, Fach F2) Grunddaten()
    {
        var d = Demo.NeueDatei("Test", "GY");
        d.Lehrer.Clear(); d.Klassen.Clear(); d.Faecher.Clear(); d.Raeume.Clear(); d.Unterrichte.Clear(); d.Stundenplan.Clear();
        d.Einstellungen.Wochenperiodizitaet = 2;
        var l = new Lehrer { Id = d.NeueId(), Kurzname = "AB", Wochensoll = 25 };
        var k = new Klasse { Id = d.NeueId(), Kurzname = "5a" };
        var f1 = new Fach { Id = d.NeueId(), Kurzname = "PH" };
        var f2 = new Fach { Id = d.NeueId(), Kurzname = "CH" };
        d.Lehrer.Add(l); d.Klassen.Add(k); d.Faecher.Add(f1); d.Faecher.Add(f2);
        return (d, l, k, f1, f2);
    }

    [Fact]
    public void AbWochenKollidierenNicht()
    {
        var (d, l, k, f1, f2) = Grunddaten();
        var a = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Periodizitaet = Periodizitaet.AWoche, Zeilen = { new Kopplungszeile { LehrerId = l.Id, FachId = f1.Id, KlassenIds = { k.Id } } } };
        var b = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Periodizitaet = Periodizitaet.BWoche, Zeilen = { new Kopplungszeile { LehrerId = l.Id, FachId = f2.Id, KlassenIds = { k.Id } } } };
        d.Unterrichte.Add(a); d.Unterrichte.Add(b);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = a.Id, Tag = 0, Stunde = 1, Raeume = { null } });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = b.Id, Tag = 0, Stunde = 1, Raeume = { null } });
        Assert.Equal(0, Bewertung.Kennzahlen(d).Kollisionen);
        Assert.Empty(Planungsdienst.Hindernisse(d, new DatenIndex(d), b, 0, 1));

        // jede Woche + A-Woche kollidiert
        b.Periodizitaet = Periodizitaet.JedeWoche;
        Assert.True(Bewertung.Kennzahlen(d).Kollisionen > 0);
        Assert.NotEmpty(Planungsdienst.Hindernisse(d, new DatenIndex(d), b, 0, 1));
    }

    [Fact]
    public void AutomatischeZuordnungBeachtetFachUndDeputat()
    {
        var (d, l, k, f1, f2) = Grunddaten();
        l.FaecherIds.Add(f1.Id);
        l.Wochensoll = 3;
        var l2 = new Lehrer { Id = d.NeueId(), Kurzname = "CD", Wochensoll = 25, FaecherIds = { f1.Id, f2.Id } };
        d.Lehrer.Add(l2);
        k.KlassenlehrerIds.Add(l.Id);
        var u1 = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { FachId = f1.Id, KlassenIds = { k.Id } } } };
        var u2 = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { FachId = f1.Id, KlassenIds = { k.Id } } } };
        var u3 = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { FachId = f2.Id, KlassenIds = { k.Id } } } };
        d.Unterrichte.AddRange(new[] { u1, u2, u3 });
        var erg = Wertrechnung.AutomatischZuordnen(d);
        Assert.Equal(3, erg.Count);
        // Klassenlehrer AB bekommt den ersten PH-Unterricht, danach reicht sein Deputat (3) nicht mehr
        Assert.Equal(l.Id, u1.Zeilen[0].LehrerId);
        Assert.Equal(l2.Id, u2.Zeilen[0].LehrerId);
        Assert.Equal(l2.Id, u3.Zeilen[0].LehrerId); // nur CD hat CH
    }
}

public class OptimierungsOptionenTests
{
    [Fact]
    public void BedingtFixierenBehaeltDenVorplanWeitgehend()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 1 })[0].Plan;
        var vorher = d.Stundenplan.Select(p => (p.UnterrichtId, p.Tag, p.Stunde)).ToHashSet();
        var frei = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 99 })[0].Plan;
        var bedingt = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 99, BedingtFixieren = true })[0].Plan;
        int Gleich(List<Planstunde> plan) => plan.Count(p => vorher.Contains((p.UnterrichtId, p.Tag, p.Stunde)));
        Assert.True(Gleich(bedingt) > Gleich(frei), $"bedingt {Gleich(bedingt)} frei {Gleich(frei)} von {vorher.Count}");
        Assert.True(Gleich(bedingt) >= vorher.Count * 0.9, $"bedingt {Gleich(bedingt)} von {vorher.Count}");
    }
}

public class BereitschaftsplanungTests
{
    [Fact]
    public void AutomatischBesetztBedarfMitFreienLehrernUndBeachtetHoechstzahl()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 2 })[0].Plan;
        foreach (var b in Bereitschaftsplanung.Stunden(d)) Bereitschaftsplanung.Entfernen(d, b.Lehrer.Id, b.Tag, b.Stunde);
        foreach (var l in d.Lehrer) { l.BereitschaftenMax = 1; l.KeineVertretung = false; }
        for (var t = 0; t < 5; t++) Bereitschaftsplanung.BedarfSetzen(d, t, 3, 2);
        var n = Bereitschaftsplanung.Automatisch(d);
        Assert.Equal(10, n);
        var stunden = Bereitschaftsplanung.Stunden(d);
        Assert.Equal(10, stunden.Count);
        Assert.All(stunden.GroupBy(b => b.Lehrer.Id), g => Assert.Single(g)); // höchstens 1 je Lehrer
        var ix = new DatenIndex(d);
        foreach (var b in stunden) // Lehrer hat zu der Stunde keinen anderen Unterricht
            Assert.Single(ix.PlanVon(ElementTyp.Lehrer, b.Lehrer.Id), p => p.Tag == b.Tag && p.Stunde == b.Stunde);
        Bereitschaftsplanung.Entfernen(d, stunden[0].Lehrer.Id, stunden[0].Tag, stunden[0].Stunde);
        Assert.Equal(9, Bereitschaftsplanung.Stunden(d).Count);
    }

    [Fact]
    public void BereitschaftWirdNieVertreten()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 2 })[0].Plan;
        var tag = Enumerable.Range(0, 14).Select(i => d.Einstellungen.SchuljahrVon.AddDays(i)).First(t => Kalender.IstUnterrichtstag(d, t));
        var ti = Kalender.TagIndex(d, tag);
        var ix = new DatenIndex(d);
        var l = d.Lehrer.First(x => Enumerable.Range(1, 6).Any(s => !ix.PlanVon(ElementTyp.Lehrer, x.Id).Any(p => p.Tag == ti && p.Stunde == s)));
        var stunde = Enumerable.Range(1, 6).First(s => !ix.PlanVon(ElementTyp.Lehrer, l.Id).Any(p => p.Tag == ti && p.Stunde == s));
        Bereitschaftsplanung.Setzen(d, l.Id, ti, stunde);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = tag, Bis = tag });
        Vertretungsplanung.Vertretungen.Aktualisieren(d, tag);
        var v = Assert.Single(d.Vertretungen, x => x.Datum == tag && x.Stunde == stunde && x.LehrerId == l.Id);
        Assert.Equal(VertretungsArt.BereitschaftEntfaellt, v.Art);
        Assert.False(v.Offen);
        // auch „Entfall umschalten“ macht daraus keine offene Vertretung
        Vertretungsplanung.Vertretungen.EntfallUmschalten(d, v);
        Assert.Equal(VertretungsArt.BereitschaftEntfaellt, v.Art);
        // Bereitschaften stehen nicht in der Vertretungsliste
        Assert.True(Vertretungsplanung.Vertretungen.NichtInListe(d, v));
    }

    [Fact]
    public void UnterrichtOhneVertretungStehtNichtInDerListe()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 2 })[0].Plan;
        var tag = Enumerable.Range(0, 14).Select(i => d.Einstellungen.SchuljahrVon.AddDays(i)).First(t => Kalender.IstUnterrichtstag(d, t));
        var ti = Kalender.TagIndex(d, tag);
        var ix = new DatenIndex(d);
        var l = d.Lehrer.First(x => ix.PlanVon(ElementTyp.Lehrer, x.Id).Count(p => p.Tag == ti) >= 2);
        var stunden = ix.PlanVon(ElementTyp.Lehrer, l.Id).Where(p => p.Tag == ti).ToList();
        var ohne = d.Unterrichte.First(u => u.Id == stunden[0].UnterrichtId);
        ohne.KeineVertretung = true;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = tag, Bis = tag });
        Vertretungsplanung.Vertretungen.Aktualisieren(d, tag);
        var heute = d.Vertretungen.Where(x => x.Datum == tag && x.LehrerId == l.Id).ToList();
        var entfall = heute.Where(x => x.UnterrichtId == ohne.Id).ToList();
        Assert.NotEmpty(entfall);
        Assert.All(entfall, x => Assert.True(Vertretungsplanung.Vertretungen.NichtInListe(d, x)));
        // zu vertretender Unterricht bleibt in der Liste
        Assert.Contains(heute, x => x.UnterrichtId != ohne.Id && !Vertretungsplanung.Vertretungen.NichtInListe(d, x));
    }

    [Fact]
    public void NurDoppelstundenBesetztPaarweiseAufeinanderfolgend()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 2 })[0].Plan;
        foreach (var b in Bereitschaftsplanung.Stunden(d)) Bereitschaftsplanung.Entfernen(d, b.Lehrer.Id, b.Tag, b.Stunde);
        foreach (var l in d.Lehrer) { l.BereitschaftenMax = 2; l.KeineVertretung = false; }
        d.Einstellungen.BereitschaftNurDoppelstunden = true;
        for (var t = 0; t < 5; t++) { Bereitschaftsplanung.BedarfSetzen(d, t, 3, 1); Bereitschaftsplanung.BedarfSetzen(d, t, 4, 1); }
        var n = Bereitschaftsplanung.Automatisch(d);
        Assert.Equal(10, n);
        var stunden = Bereitschaftsplanung.Stunden(d);
        // je Lehrer genau eine Doppelstunde (3./4. Stunde desselben Tages)
        Assert.All(stunden.GroupBy(b => b.Lehrer.Id), g =>
        {
            var l = g.OrderBy(x => x.Stunde).ToList();
            Assert.Equal(2, l.Count);
            Assert.Equal(l[0].Tag, l[1].Tag);
            Assert.Equal(l[0].Stunde + 1, l[1].Stunde);
        });
        for (var t = 0; t < 5; t++)
        {
            Assert.Single(stunden, b => b.Tag == t && b.Stunde == 3);
            Assert.Single(stunden, b => b.Tag == t && b.Stunde == 4);
        }
    }
}
