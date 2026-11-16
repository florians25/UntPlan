using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Gewichtungsparameter (Handbuch Stundenplan, Kap. Optimierung | Gewichtung) und Abschnitt „Analyse“.</summary>
public class GewichtungTests
{
    private static Schuldatei Datei(int tage = 5, int stunden = 8)
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = tage;
        d.Zeitraster.StundenProTag = stunden;
        d.Zeitraster.LetzteVormittagsstunde = 5;
        return d;
    }

    private static Unterricht U(Schuldatei d, Lehrer? l, Klasse? k, Fach? f, int wst = 1)
    {
        var z = new Kopplungszeile { LehrerId = l?.Id, FachId = f?.Id };
        if (k != null) z.KlassenIds.Add(k.Id);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = wst, Zeilen = { z } };
        d.Unterrichte.Add(u);
        return u;
    }

    private static void Setzen(Schuldatei d, Unterricht u, int tag, int stunde) =>
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = { null } });

    private static List<Verstoss> V(Schuldatei d, VerstossArt art) => Bewertung.Verstoesse(d).Where(v => v.Art == art).ToList();

    [Fact]
    public void AbschnitteUndBedeutungWieImHandbuch()
    {
        Assert.Equal(new[] { "Lehrer (1)", "Lehrer (2)", "Klassen", "Fächer", "Hauptfächer", "Räume", "Stundenverteilung", "Zeitwünsche" }, Gewichtung.Karteikarten);
        Assert.Equal("berücksichtigenswert", Gewichtung.ZifferText(2));
        Assert.Equal("extrem wichtig", Gewichtung.ZifferText(5));
        foreach (var text in new[] { "Einzelstunden an einem Halbtag für Lehrer vermeiden", "Doppelhohlstunden für Lehrer vermeiden", "Tagesrand-Pausen einhalten",
                     "Max. Verweildauer des Lehrers pro Tag", "Klassenlehrer mindestens einmal pro Tag", "Optimierung der dislozierten Räume",
                     "2-Stunden-Fächer nicht an aufeinanderfolgenden Tagen", "Großblöcke am Rand des Halbtages", "Hauptfach mindestens 1 mal bis Grenzstunde (incl.)" })
            Assert.Contains(Gewichtung.Alle, r => r.Text == text);
        // Schlüssel eindeutig, Voreinstellung enthält alle Einträge
        Assert.Equal(Gewichtung.Alle.Count, Gewichtung.Alle.Select(r => r.Schluessel).Distinct().Count());
        Assert.All(Gewichtung.Alle, r => Assert.True(Gewichtung.Standard.ContainsKey(r.Schluessel)));
    }

    [Fact]
    public void VoreinstellungIstLautAnalyseUnproblematisch()
    {
        var hinweise = Gewichtung.Analyse(null, Gewichtung.Standard);
        Assert.DoesNotContain(hinweise, h => h.Warnung);
        var n = Gewichtung.Verteilung(Gewichtung.Standard);
        Assert.True(n[4] + n[5] < n[1] + n[2]);
    }

    [Fact]
    public void AnalyseWarntVorZuVielenFuenfernUndWirkungslosenGewichten()
    {
        var g = Gewichtung.Gewichte.ToDictionary(r => r.Schluessel, _ => 5);
        var h = Gewichtung.Analyse(null, g);
        Assert.Contains(h, x => x.Warnung && x.Text.Contains("extrem wichtig"));
        Assert.Contains(h, x => x.Text.Contains("Vergabehäufigkeit"));
        var d = Datei();
        var w = new Dictionary<string, int>(Gewichtung.Standard) { ["L.StdTag"] = 3 };
        Assert.Contains(Gewichtung.Analyse(d, w), x => x.Schluessel == "L.StdTag");
        ((Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo")).StundenProTag = new Bereich(2, 4);
        Assert.DoesNotContain(Gewichtung.Analyse(d, w), x => x.Schluessel == "L.StdTag");
    }

    [Fact]
    public void DiagnoseZeigtGewichtLautGewichtungsdialog()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        Setzen(d, U(d, null, k, f), 0, 1);
        Setzen(d, U(d, null, k, f), 0, 3);
        d.Gewichtung["K.Hohlstunden"] = 2;
        var p = Diagnose.Stundenplan(d).Single(x => x.Titel == Bewertung.ArtText(VerstossArt.HohlstundeKlasse));
        Assert.Equal(2, p.Gewicht);
    }

    [Fact]
    public void EinzelstundenUndDoppelhohlstundenFuerLehrer()
    {
        var d = Datei();
        d.Gewichtung["L.Einzelstunden"] = 2; d.Gewichtung["L.Doppelhohl"] = 2;
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        Setzen(d, U(d, l, null, f), 0, 1);
        Setzen(d, U(d, l, null, f), 0, 4);
        Assert.Single(V(d, VerstossArt.DoppelhohlstundeLehrer));
        Assert.Empty(V(d, VerstossArt.EinzelstundeHalbtag));
        // Dienstag nur eine einzige Vormittagsstunde
        Setzen(d, U(d, l, null, f), 1, 2);
        Assert.Single(V(d, VerstossArt.EinzelstundeHalbtag));
        d.Gewichtung["L.Einzelstunden"] = 0;
        Assert.Empty(V(d, VerstossArt.EinzelstundeHalbtag));
    }

    [Fact]
    public void LetzteVormittagsstundeMitMaximal()
    {
        var d = Datei();
        d.Gewichtung["L.LetzteVorm"] = 2;
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        for (var t = 0; t < 3; t++) Setzen(d, U(d, l, null, f), t, 5);
        Assert.Empty(V(d, VerstossArt.LetzteVormittagsstunde));
        d.Gewichtung["L.LetzteVormMax"] = 2;
        Assert.Single(V(d, VerstossArt.LetzteVormittagsstunde));
    }

    [Fact]
    public void KlassenlehrerMindestensEinmalProTag()
    {
        var d = Datei();
        d.Gewichtung["K.Klassenlehrer"] = 2;
        var l = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Hugo");
        var a = (Lehrer)Stammdaten.Neu(d, ElementTyp.Lehrer, "Anna");
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        k.KlassenlehrerIds.Add(l.Id);
        Setzen(d, U(d, l, k, f), 0, 1);
        Setzen(d, U(d, a, k, f), 1, 1);
        var v = V(d, VerstossArt.KlassenlehrerTag);
        Assert.Single(v);
        Assert.Equal(1, v[0].Tag);
    }

    [Fact]
    public void HauptfachGrenzstunde()
    {
        var d = Datei();
        d.Gewichtung["H.BisGrenz"] = 2;
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "M");
        f.Hauptfach = true;
        var u = U(d, null, k, f, 3);
        u.Zeilen[0].FachId = f.Id;
        Setzen(d, u, 0, 6);
        Setzen(d, u, 1, 7);
        Setzen(d, u, 2, 6);
        Assert.Single(V(d, VerstossArt.HauptfachSpaet));
        Assert.Single(V(d, VerstossArt.HauptfachBisGrenzstunde));
        d.Gewichtung["H.Grenzstunde"] = 6;
        Assert.Empty(V(d, VerstossArt.HauptfachSpaet));
        Assert.Empty(V(d, VerstossArt.HauptfachBisGrenzstunde));
    }

    [Fact]
    public void ZweiStundenFachNichtAnFolgetagen()
    {
        var d = Datei();
        d.Gewichtung["S.Zwei"] = 2;
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "Bio");
        var u = U(d, null, k, f, 2);
        Setzen(d, u, 0, 1);
        Setzen(d, u, 1, 2);
        Assert.Single(V(d, VerstossArt.ZweiStundenFolgetage));
        // letzter und erster Tag der Woche zählen ebenfalls als aufeinanderfolgend
        d.Stundenplan[1].Tag = 4;
        Assert.Single(V(d, VerstossArt.ZweiStundenFolgetage));
        d.Stundenplan[1].Tag = 2;
        Assert.Empty(V(d, VerstossArt.ZweiStundenFolgetage));
    }

    [Fact]
    public void FreifachRandpositionenLautHaekchen()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "D");
        var ff = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "Chor");
        ff.Freifach = true;
        Setzen(d, U(d, null, k, f), 0, 1);
        Setzen(d, U(d, null, k, f), 0, 2);
        Setzen(d, U(d, null, k, ff), 0, 3);
        Assert.Empty(V(d, VerstossArt.Freifach));
        d.Gewichtung["F.Freifach.Letzte"] = 0;
        Assert.Single(V(d, VerstossArt.Freifach));
    }

    [Fact]
    public void GrossblockAmRandDesHalbtages()
    {
        var d = Datei();
        var k = (Klasse)Stammdaten.Neu(d, ElementTyp.Klasse, "1a");
        var f = (Fach)Stammdaten.Neu(d, ElementTyp.Fach, "Werk");
        var u = U(d, null, k, f, 3);
        u.Block = "3";
        foreach (var s in new[] { 2, 3, 4 }) Setzen(d, u, 0, s);
        d.Gewichtung["S.Grossbloecke"] = 3;
        Assert.Single(V(d, VerstossArt.GrossblockRand));
        d.Stundenplan.Clear();
        foreach (var s in new[] { 3, 4, 5 }) Setzen(d, u, 0, s);
        Assert.Empty(V(d, VerstossArt.GrossblockRand));
    }
}
