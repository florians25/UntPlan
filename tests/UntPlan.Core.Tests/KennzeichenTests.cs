using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Wirkung der Kennzeichen und Felder aus Stammdaten und Unterricht (Handbuch Stundenplan, Kap. 2.4 und 3.4) sowie Unterrichtsfolgen.</summary>
public class KennzeichenTests
{
    private static Schuldatei Datei(int tage = 5, int stunden = 8)
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = tage;
        d.Zeitraster.StundenProTag = stunden;
        d.Zeitraster.LetzteVormittagsstunde = 5;
        return d;
    }

    private static Unterricht U(Schuldatei d, Lehrer? l, Klasse? k, Fach? f, int wst = 1, Kopplungszeile? zeile = null)
    {
        var z = zeile ?? new Kopplungszeile();
        z.LehrerId = l?.Id;
        z.FachId = f?.Id;
        if (k != null) z.KlassenIds.Add(k.Id);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = wst, Zeilen = { z } };
        d.Unterrichte.Add(u);
        return u;
    }

    private static void Setzen(Schuldatei d, Unterricht u, int tag, int stunde, int? raum = null) =>
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = { raum } });

    private static List<Verstoss> V(Schuldatei d, VerstossArt art) => Bewertung.Verstoesse(d).Where(v => v.Art == art).ToList();

    [Fact]
    public void HalbtagKennzeichenMeldetVorUndNachmittag()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var a = U(d, null, k, f);
        var b = U(d, null, k, f);
        Setzen(d, a, 0, 1);
        Setzen(d, b, 0, 7);
        Assert.Empty(V(d, VerstossArt.HalbtagKlasse));
        k.EinHalbtag = true;
        Assert.Single(V(d, VerstossArt.HalbtagKlasse));
    }

    [Fact]
    public void HohlstundenZulaessigUnterdruecktKlassenhohlstunden()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        Setzen(d, U(d, null, k, f), 0, 1);
        Setzen(d, U(d, null, k, f), 0, 3);
        Assert.NotEmpty(V(d, VerstossArt.HohlstundeKlasse));
        k.HohlstundenZulaessig = true;
        Assert.Empty(V(d, VerstossArt.HohlstundeKlasse));
    }

    [Fact]
    public void UnterrichtstageNichtInFolgeUndZweiTageHintereinander()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "BS1");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        Setzen(d, U(d, null, k, f), 0, 1);
        Setzen(d, U(d, null, k, f), 1, 1);
        k.TageNichtInFolge = true;
        Assert.Single(V(d, VerstossArt.Unterrichtstage));
        k.TageNichtInFolge = false;
        k.ZweiTageHintereinander = true;
        Assert.Empty(V(d, VerstossArt.Unterrichtstage));
        d.Stundenplan[1].Tag = 3;
        Assert.Single(V(d, VerstossArt.Unterrichtstage));
    }

    [Fact]
    public void LehrerVerweildauerStundenfolgeUndTagesrandpause()
    {
        var d = Datei();
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        foreach (var s in new[] { 1, 2, 3, 8 }) Setzen(d, U(d, l, null, f), 0, s);
        Setzen(d, U(d, l, null, f), 1, 1);
        l.MaxVerweildauer = 6;
        l.MaxStundenFolge = 2;
        l.TagesrandPause = 2;
        Assert.Single(V(d, VerstossArt.Verweildauer));
        Assert.Single(V(d, VerstossArt.StundenFolgeLehrer));
        // Montag bis 8. (letzte) Stunde, Dienstag ab 1.: keine freie Stunde dazwischen
        Assert.Single(V(d, VerstossArt.Tagesrandpause));
        l.NichtErsteUndLetzte = true;
        Assert.Single(V(d, VerstossArt.ErsteUndLetzteStunde));
    }

    [Fact]
    public void BesondersWichtigVerstaerktHohlstundenkosten()
    {
        var d = Datei();
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        Setzen(d, U(d, l, null, f), 0, 1);
        Setzen(d, U(d, l, null, f), 0, 4);
        var normal = V(d, VerstossArt.HohlstundenLehrer).Sum(v => v.Kosten);
        l.WichtigHohlstunden = true;
        Assert.True(V(d, VerstossArt.HohlstundenLehrer).Sum(v => v.Kosten) > normal * 3);
        Assert.Contains("A", l.Kennzeichen);
    }

    [Fact]
    public void BelegungBeibehaltenSperrtAusserhalbDerBisherigenStunden()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var a = U(d, null, k, f);
        Setzen(d, a, 0, 2);
        Setzen(d, U(d, null, k, f), 0, 3);
        k.BelegungBeibehalten = true;
        // eine weitere Stunde: erlaubt wäre nur Mo 2.–3. (dort Kollision) – sie bleibt unverplant
        var neu = U(d, null, k, f);
        var plan = Optimierer.EinPlan(d, new OptimierungsOptionen { Strategie = Strategie.A, NurNichtVerplante = true, Seed = 3 }, 3);
        var p = plan.FirstOrDefault(x => x.UnterrichtId == neu.Id);
        Assert.Null(p);
    }

    [Fact]
    public void FachfolgeBuchstabeVerbietetFolgestunden()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var ph = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "PH");
        var ch = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "CH");
        ph.FachfolgeKlassen = "A";
        ch.FachfolgeKlassen = "A";
        Setzen(d, U(d, null, k, ph), 0, 1);
        var c = U(d, null, k, ch);
        Setzen(d, c, 0, 2);
        Assert.NotEmpty(V(d, VerstossArt.Fachfolge));
        // Ziffer beim Unterricht hat Vorrang vor dem Buchstaben des Faches
        c.FachfolgeKlassen = "1";
        Assert.Empty(V(d, VerstossArt.Fachfolge));
    }

    [Fact]
    public void FachfolgeZifferVerlangtFolgestunden()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var a = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "BIT");
        var b = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "BIP");
        var x = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        a.FachfolgeKlassen = b.FachfolgeKlassen = "3";
        Setzen(d, U(d, null, k, a), 0, 1);
        Setzen(d, U(d, null, k, x), 0, 2);
        Setzen(d, U(d, null, k, b), 0, 3);
        Assert.Single(V(d, VerstossArt.Fachfolge));
    }

    [Fact]
    public void KlassenkollisionsKennzeichenErlaubtGleichzeitigkeit()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var a = U(d, null, k, f);
        var b = U(d, null, k, f);
        Setzen(d, a, 0, 1);
        Setzen(d, b, 0, 1);
        Assert.NotEmpty(V(d, VerstossArt.Klassenkollision));
        a.Klassenkollision = b.Klassenkollision = "1";
        Assert.Empty(V(d, VerstossArt.Klassenkollision));
        a.Klassenkollision = "A";
        b.Klassenkollision = "A";
        Assert.NotEmpty(V(d, VerstossArt.Klassenkollision));
        b.Klassenkollision = "B";
        Assert.Empty(V(d, VerstossArt.Klassenkollision));
        // manuelles Planen: keine Klassenkollision als Hindernis
        Assert.Empty(Planungsdienst.Hindernisse(d, new DatenIndex(d), a, 0, 1));
    }

    [Fact]
    public void KeineEinzelstundenBildetDoppelstunden()
    {
        var u = new Unterricht { Wochenstunden = 7, KeineEinzelstunden = true };
        Assert.Equal(new[] { 2, 2, 2, 1 }, u.Bloecke());
    }

    [Fact]
    public void ImmerSelbeZeitUndNachmittagNurDoppel()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "SP");
        var u = U(d, null, k, f, 2);
        u.ImmerSelbeZeit = true;
        u.NachmittagNurDoppel = true;
        u.MehrmalsProTag = true;
        Setzen(d, u, 0, 3);
        Setzen(d, u, 1, 7);
        Assert.Single(V(d, VerstossArt.ImmerSelbeZeit));
        Assert.Single(V(d, VerstossArt.NachmittagEinzelstunde));
    }

    [Fact]
    public void SternPauseDarfNichtUeberspanntWerden()
    {
        var d = Datei();
        d.Zeitraster.Pausen.Add(new Pause { NachStunde = 2, Stern = true });
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "CH");
        var u = U(d, null, k, f, 2);
        u.Doppelstunden = new Bereich(1, 1);
        Setzen(d, u, 0, 2);
        Setzen(d, u, 0, 3);
        Assert.Single(V(d, VerstossArt.DoppelstundeUeberPause));
        f.DoppelstundeUeberPause = true;
        Assert.Empty(V(d, VerstossArt.DoppelstundeUeberPause));
    }

    [Fact]
    public void KlassenzeitrasterLetzteStundeNein()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        Setzen(d, U(d, null, k, f), 0, 1);
        Setzen(d, U(d, null, k, f), 0, 2);
        k.Klassenzeitraster[0 * 100 + 2] = KlassenzeitrasterArt.NichtLetzte;
        Assert.Single(V(d, VerstossArt.Klassenzeitraster));
        k.Klassenzeitraster[0 * 100 + 2] = KlassenzeitrasterArt.Doppelstunden;
        Assert.Single(V(d, VerstossArt.Klassenzeitraster));
    }

    [Fact]
    public void DislozierungVerlangtPauseBeimGebaeudewechsel()
    {
        var d = Datei();
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        var r1 = (Raum)Stammdaten.Neu(d, ElementTyp.Raum, "R1");
        var r2 = (Raum)Stammdaten.Neu(d, ElementTyp.Raum, "AS1");
        r2.Dislozierung = "2";
        Setzen(d, U(d, l, null, f), 0, 1, r1.Id);
        Setzen(d, U(d, l, null, f), 0, 3, r2.Id);
        // Wegzeit 2 Stunden (Hauptgebäude 0 → 2), nur eine freie Stunde
        Assert.Single(V(d, VerstossArt.Dislozierung));
        r2.Dislozierung = "A";
        Assert.Empty(V(d, VerstossArt.Dislozierung));
        Assert.Equal(1, Raum.Wegzeit("A", "B"));
        Assert.Equal(0, Raum.Wegzeit("x", ""));
    }

    [Fact]
    public void UnterrichtsfolgenWerdenBewertetUndVomOptimiererEingehalten()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var t = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "PHT");
        var lab = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "PHL");
        var a = U(d, null, k, t, 1);
        var b = U(d, null, k, lab, 2);
        b.Doppelstunden = new Bereich(1, 1);
        d.Unterrichtsfolgen.Add(new Unterrichtsfolge { Art = UnterrichtsfolgeArt.FixeFachfolge, UnterrichtIds = { a.Id, b.Id } });
        Setzen(d, a, 0, 1);
        Setzen(d, b, 1, 1);
        Setzen(d, b, 1, 2);
        Assert.Single(V(d, VerstossArt.Unterrichtsfolge));
        d.Stundenplan.Clear();
        var plan = Optimierer.EinPlan(d, new OptimierungsOptionen { Strategie = Strategie.A, Seed = 5 }, 5);
        var pa = plan.Where(p => p.UnterrichtId == a.Id).ToList();
        var pb = plan.Where(p => p.UnterrichtId == b.Id).OrderBy(p => p.Stunde).ToList();
        Assert.Single(pa);
        Assert.Equal(2, pb.Count);
        Assert.Equal(pa[0].Tag, pb[0].Tag);
        Assert.Equal(pa[0].Stunde + 1, pb[0].Stunde);
    }

    [Fact]
    public void WochenfolgeUndGleichzeitigkeit()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1b");
        var k2 = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1c");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "CH");
        var a = U(d, null, k, f);
        var b = U(d, null, k, f);
        var c = U(d, null, k2, f);
        d.Unterrichtsfolgen.Add(new Unterrichtsfolge { Art = UnterrichtsfolgeArt.Wochenfolge, UnterrichtIds = { a.Id, b.Id } });
        d.Unterrichtsfolgen.Add(new Unterrichtsfolge { Art = UnterrichtsfolgeArt.Gleichzeitig, UnterrichtIds = { a.Id, c.Id } });
        Setzen(d, a, 2, 1);
        Setzen(d, b, 1, 1);
        Setzen(d, c, 3, 1);
        Assert.Equal(2, V(d, VerstossArt.Unterrichtsfolge).Count);
        // Gleichzeitige Unterrichte in Kopplungen umwandeln
        Assert.Equal(1, Unterrichtswerkzeuge.GleichzeitigeInKopplungen(d));
        Assert.Equal(2, d.Unterrichte.First(u => u.Id == a.Id).Zeilen.Count);
        Assert.DoesNotContain(d.Unterrichte, u => u.Id == c.Id);
    }

    [Fact]
    public void ManuellPlanenWirdNichtOptimiertUndKeinAusweichraum()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var r1 = (Raum)Stammdaten.Neu(d, ElementTyp.Raum, "R1");
        var r2 = (Raum)Stammdaten.Neu(d, ElementTyp.Raum, "R2");
        r1.AusweichraumId = r2.Id;
        var m = U(d, null, k, f, 2, new Kopplungszeile { FachraumId = r1.Id });
        m.ManuellPlanen = true;
        var plan = Optimierer.EinPlan(d, new OptimierungsOptionen { Strategie = Strategie.A, Seed = 1 }, 1);
        Assert.DoesNotContain(plan, p => p.UnterrichtId == m.Id);
        var ix = new DatenIndex(d);
        Assert.Equal(2, ix.RaumKandidaten(m, m.Zeilen[0]).Count);
        m.KeinAusweichraum = true;
        Assert.Equal(new[] { r1.Id }, ix.RaumKandidaten(m, m.Zeilen[0]));
    }

    [Fact]
    public void DiagnoseErkenntFachwochenstundenUndKeineDatenanalyse()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        f.WochenstundenBereich = new Bereich(4, 5);
        var u = U(d, null, k, f, 2);
        var punkte = Diagnose.Eingabedaten(d);
        Assert.Single(punkte.First(p => p.Titel.StartsWith("Wochenstunden des Faches")).Faelle);
        Assert.Contains(punkte.First(p => p.Titel == "Unterricht ohne Lehrer").Faelle, x => x.UnterrichtId == u.Id);
        u.KeineDatenanalyse = true;
        punkte = Diagnose.Eingabedaten(d);
        Assert.DoesNotContain(punkte.First(p => p.Titel == "Unterricht ohne Lehrer").Faelle, x => x.UnterrichtId == u.Id);
    }

    [Fact]
    public void LehrerStatusUndOptimierungskennzeichen()
    {
        var a = new Lehrer { Status = "L", StatusNeu = "SL", StatusNeuAb = new DateOnly(2027, 2, 1), OptimierungsKz = "1" };
        Assert.Equal("L", a.StatusAm(new DateOnly(2027, 1, 31)));
        Assert.Equal("SL", a.StatusAm(new DateOnly(2027, 2, 1)));
        Assert.True(a.TauschErlaubt(new Lehrer { OptimierungsKz = "1" }));
        Assert.False(a.TauschErlaubt(new Lehrer { OptimierungsKz = "2" }));
        var b = new Lehrer { OptimierungsKz = "A" };
        Assert.False(b.TauschErlaubt(new Lehrer { OptimierungsKz = "A" }));
        Assert.True(b.TauschErlaubt(new Lehrer()));
    }

    [Fact]
    public void AutomatischeFarbeFuerNeueElemente()
    {
        var d = Datei();
        Assert.Equal(0, Stammdaten.Neu(d, ElementTyp.Fach, "D").Hintergrund);
        d.Einstellungen.AutomatischeFarbe.Add(nameof(ElementTyp.Fach));
        var e = Stammdaten.Neu(d, ElementTyp.Fach, "M");
        var e2 = Stammdaten.Neu(d, ElementTyp.Fach, "E");
        Assert.NotEqual(0, e.Hintergrund);
        Assert.NotEqual(e.Hintergrund, e2.Hintergrund);
    }

    [Fact]
    public void AlsKursUebernehmenUndErweitertesEinfuegenMitStundenplan()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var k2 = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1b");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var s = (Schueler)Stammdaten.Neu(d, ElementTyp.Schueler, "Muster");
        s.KlasseId = k.Id;
        var u = U(d, null, k, f, 2);
        Setzen(d, u, 0, 1);
        Setzen(d, u, 1, 1);
        Assert.Equal(1, Unterrichtswerkzeuge.AlsKursUebernehmen(d, u));
        Assert.Equal("D-1a", u.Kursname);
        Assert.Contains(u.Id, s.KurswahlIds);
        var neu = Unterrichtswerkzeuge.ErweitertesEinfuegen(d, new[] { u.Id }, ElementTyp.Klasse, k.Id, k2.Id, true);
        var nu = d.Unterrichte.First(x => x.Id == neu[0]);
        Assert.Equal(new[] { k2.Id }, nu.KlassenIds);
        Assert.Equal(2, d.Stundenplan.Count(p => p.UnterrichtId == nu.Id));
    }

    [Fact]
    public void UnterrichtsvergleichUndVererbteKennzeichen()
    {
        var d = Datei();
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        f.Hauptfach = true;
        var u = U(d, null, null, f, 2);
        u.Freifach = true;
        var p1 = d.AktivePeriode;
        var p2 = Core.Module.Perioden.Neu(d, p1, "P2", d.Einstellungen.SchuljahrVon.AddDays(30), null);
        p2.Unterrichte!.First(x => x.Id == u.Id).Wochenstunden = 3;
        var zeilen = Unterrichtswerkzeuge.Vergleichen(d, new[] { p1, p2 }, true);
        Assert.Single(zeilen);
        Assert.True(zeilen[0].Unterschiedlich);
        Assert.Equal("F (H)", Unterrichtswerkzeuge.KennzeichenAnzeige(new DatenIndex(d), u, true));
        Assert.Equal("F", Unterrichtswerkzeuge.KennzeichenAnzeige(new DatenIndex(d), u, false));
    }
}
