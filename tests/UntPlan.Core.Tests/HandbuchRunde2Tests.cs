using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Lehrer-Zuordnung bei Optimierung, Totaldiagnose, CCC-Analyse, Lehrerteams, KKK, Drucknamen, Druckseiten, HTML-Einstellungen.</summary>
public class HandbuchRunde2Tests
{
    /// <summary>Kleine Schule: 2 Klassen, 3 Lehrer, Zeitraster 1 Tag × 2 Stunden.</summary>
    private static Schuldatei Klein()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 1;
        d.Zeitraster.StundenProTag = 2;
        Dokument.Normalisieren(d);
        Klasse K(string n, string jg) { var k = new Klasse { Id = d.NeueId(), Kurzname = n, Jahrgang = jg }; d.Klassen.Add(k); return k; }
        Lehrer L(string n) { var l = new Lehrer { Id = d.NeueId(), Kurzname = n, Langname = n + "mann", Vorname = "Vor" + n, Titel = "Dr." }; d.Lehrer.Add(l); return l; }
        Fach F(string n) { var f = new Fach { Id = d.NeueId(), Kurzname = n }; d.Faecher.Add(f); return f; }
        K("5a", "5"); K("5b", "5");
        L("A"); L("B"); L("C");
        F("D"); F("M");
        return d;
    }

    private static Unterricht U(Schuldatei d, int wst, string lehrer, string fach, params string[] klassen)
    {
        var u = new Unterricht
        {
            Id = d.Unterrichte.Count + 1, Wochenstunden = wst,
            Zeilen = { new Kopplungszeile { LehrerId = d.Lehrer.First(l => l.Kurzname == lehrer).Id, FachId = d.Faecher.First(f => f.Kurzname == fach).Id, KlassenIds = klassen.Select(k => d.Klassen.First(x => x.Kurzname == k).Id).ToList() } },
        };
        d.Unterrichte.Add(u);
        return u;
    }

    [Fact]
    public void LehrertauschBeseitigtLehrerkollision()
    {
        var d = Klein();
        // Lehrer A hat zur selben Zeit Deutsch in 5a und 5b (Kollision); B ist frei – beide Unterrichte variabel
        var u1 = U(d, 1, "A", "D", "5a");
        var u2 = U(d, 1, "A", "D", "5b");
        var u3 = U(d, 1, "B", "D", "5b");
        foreach (var u in d.Unterrichte) u.VariablerLehrer = true;
        var plan = new List<Planstunde>
        {
            new() { UnterrichtId = u1.Id, Tag = 0, Stunde = 1 },
            new() { UnterrichtId = u2.Id, Tag = 0, Stunde = 1 },
            new() { UnterrichtId = u3.Id, Tag = 0, Stunde = 2 },
        };
        var vorher = Bewertung.Kennzahlen(d, plan);
        Assert.True(vorher.Kollisionen > 0);
        var o = new OptimierungsOptionen { KeineZuordnungsOptimierung = false };
        var k = Dokument.Klonen(d);
        var tausche = Lehrerzuordnung.Optimieren(k, plan, o, new Random(1));
        Assert.NotEmpty(tausche);
        Assert.Equal(0, Bewertung.Kennzahlen(k, plan).Kollisionen);
        // Übernehmen und „Lehrer laut Unterricht“
        Assert.Equal(tausche.Count, Lehrerzuordnung.Anwenden(d, tausche));
        Assert.True(Lehrerzuordnung.HatTausche(d));
        Assert.Contains(Diagnose.Eingabedaten(d), p => p.Titel == "Lehrertausch durch Optimierung" && p.Faelle.Count == tausche.Count);
        Assert.Equal(tausche.Count, Lehrerzuordnung.LautUnterricht(d));
        Assert.False(Lehrerzuordnung.HatTausche(d));
        Assert.Equal(d.Lehrer.First(l => l.Kurzname == "A").Id, u2.Zeilen[0].LehrerId);
    }

    [Fact]
    public void TauschoptionenSchraenkenEin()
    {
        var d = Klein();
        var u1 = U(d, 2, "A", "D", "5a");
        var u2 = U(d, 1, "B", "M", "5b");
        u1.VariablerLehrer = u2.VariablerLehrer = true;
        var ix = new DatenIndex(d);
        var alle = new OptimierungsOptionen { KeinTauschAnderesFach = false, TauschNurGleichstuendig = false, TauschNurJahrgang = false };
        Assert.True(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, alle));
        Assert.False(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, new OptimierungsOptionen { KeinTauschAnderesFach = true, TauschNurGleichstuendig = false, TauschNurJahrgang = false }));
        Assert.False(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, new OptimierungsOptionen { KeinTauschAnderesFach = false, TauschNurGleichstuendig = true, TauschNurJahrgang = false }));
        // Jahrgang: 5a und 5b gehören zu 5 → erlaubt; nach Wechsel der 5b in Jahrgang 6 nicht mehr
        Assert.True(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, new OptimierungsOptionen { KeinTauschAnderesFach = false, TauschNurGleichstuendig = false, TauschNurJahrgang = true }));
        d.Klassen[1].Jahrgang = "6";
        Assert.False(Lehrerzuordnung.TauschErlaubt(new DatenIndex(d), u1, 0, u2, 0, new OptimierungsOptionen { KeinTauschAnderesFach = false, TauschNurGleichstuendig = false, TauschNurJahrgang = true }));
        // Lehrerzuord fix bzw. fehlendes (V)
        u1.Zeilen[0].LehrerFix = true;
        Assert.False(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, alle));
        u1.Zeilen[0].LehrerFix = false;
        // Lehrer-Optimierungskennzeichen: Ziffer nur mit gleichem Kennzeichen
        d.Lehrer.First(l => l.Kurzname == "A").OptimierungsKz = "1";
        Assert.False(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, alle));
        d.Lehrer.First(l => l.Kurzname == "B").OptimierungsKz = "1";
        Assert.True(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, alle));
        u2.VariablerLehrer = false;
        Assert.False(Lehrerzuordnung.TauschErlaubt(ix, u1, 0, u2, 0, alle));
        Assert.False(Lehrerzuordnung.Moeglich(new Schuldatei()));
    }

    [Fact]
    public void KlassenkollisionskennzeichenErlaubtGleichzeitigkeit()
    {
        var d = Klein();
        var chor = U(d, 1, "A", "D", "5a");
        var orch = U(d, 1, "B", "M", "5a");
        var plan = new List<Planstunde> { new() { UnterrichtId = chor.Id, Tag = 0, Stunde = 1 }, new() { UnterrichtId = orch.Id, Tag = 0, Stunde = 1 } };
        Assert.True(Bewertung.Kennzahlen(d, plan).Kollisionen > 0);
        chor.Klassenkollision = orch.Klassenkollision = "1";
        Assert.Equal(0, Bewertung.Kennzahlen(d, plan).Kollisionen);
        Assert.True(Klassenkollision.Erlaubt(chor, orch));
        // gleicher Buchstabe: nicht erlaubt, verschiedene Buchstaben: erlaubt
        chor.Klassenkollision = orch.Klassenkollision = "A";
        Assert.False(Klassenkollision.Erlaubt(chor, orch));
        Assert.True(Bewertung.Kennzahlen(d, plan).Kollisionen > 0);
        orch.Klassenkollision = "b";
        Assert.True(Klassenkollision.Erlaubt(chor, orch));
        Assert.Equal(0, Bewertung.Kennzahlen(d, plan).Kollisionen);
        Assert.Equal(2, Klassenkollision.Gleichzeitig(new char?[] { 'A', 'B', 'A' }));
        Assert.Equal(2, Klassenkollision.Gleichzeitig(new char?[] { '1', '1', null }));
        // manuelles Planen: kein Hindernis durch die Klasse
        d.Stundenplan.Add(new Planstunde { UnterrichtId = orch.Id, Tag = 0, Stunde = 1 });
        Assert.Empty(Planungsdienst.Hindernisse(d, new DatenIndex(d), chor, 0, 1));
    }

    [Fact]
    public void TotaldiagnoseSortiertNachPunkten()
    {
        var d = Klein();
        var u1 = U(d, 1, "A", "D", "5a");
        var u2 = U(d, 1, "B", "M", "5a");
        U(d, 1, "C", "M", "5b");
        // 5a: zwei Unterrichte in derselben Stunde (Kollision), 5b ordentlich
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u1.Id, Tag = 0, Stunde = 1 });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u2.Id, Tag = 0, Stunde = 1 });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 3, Tag = 0, Stunde = 1 });
        var z = Totaldiagnose.Berechnen(d, ElementTyp.Klasse);
        Assert.Equal("5a", z[0].Name);
        Assert.True(z[0].Punkte > z[1].Punkte);
        Assert.Equal(0, z[0].Stunden[0].Tag);
        Assert.Equal(1, z[0].Stunden[0].Stunde);
        Assert.NotEqual("", z[0].Stunden[0].Grund);
    }

    [Fact]
    public void CccAnalyseFindetLaengsteKetteUndKritischenLehrer()
    {
        var d = Klein();
        d.Zeitraster.TageProWoche = 1;
        d.Zeitraster.StundenProTag = 4;
        Dokument.Normalisieren(d);
        // 5a: D (A) 2 Std., M (B) 2 Std.; 5b: M (A) 1 Std. → Kette 5a-D, 5a-M (Klasse) und 5b-M (Lehrer A mit 5a-D)
        var u1 = U(d, 2, "A", "D", "5a");
        var u2 = U(d, 2, "B", "M", "5a");
        var u3 = U(d, 1, "A", "M", "5b");
        var erg = CccAnalyse.Analysieren(d);
        Assert.Equal(3, erg.Unterrichte);
        var k = erg.Ketten[0];
        Assert.Equal(4, k.Wochenstunden);
        Assert.Contains(u1.Id, k.UnterrichtIds);
        Assert.Contains(u2.Id, k.UnterrichtIds);
        Assert.Contains(erg.Ketten, x => x.UnterrichtIds.Contains(u3.Id) && x.UnterrichtIds.Contains(u1.Id) && x.Wochenstunden == 3);
        var mitA = erg.Ketten.First(x => x.UnterrichtIds.Contains(u3.Id));
        Assert.Equal(d.Lehrer[0].Id, mitA.Kritisch.Values.First());
        Assert.True(mitA.Verkuerzung > 0);
        Assert.Equal(4, erg.VerfuegbareStunden);
    }

    [Fact]
    public void LehrerteamsAusKopplungen()
    {
        var d = Klein();
        var u1 = U(d, 2, "A", "D", "5a");
        u1.Zeilen.Add(new Kopplungszeile { LehrerId = d.Lehrer[1].Id, FachId = d.Faecher[0].Id, KlassenIds = { d.Klassen[0].Id } });
        var u2 = U(d, 1, "B", "M", "5b");
        u2.Zeilen.Add(new Kopplungszeile { LehrerId = d.Lehrer[0].Id, FachId = d.Faecher[1].Id, KlassenIds = { d.Klassen[1].Id } });
        var u3 = U(d, 1, "C", "M", "5b");
        u3.Zeilen.Add(new Kopplungszeile { LehrerId = d.Lehrer[0].Id, FachId = d.Faecher[1].Id, KlassenIds = { d.Klassen[1].Id } });
        var teams = CccAnalyse.Lehrerteams(d);
        Assert.Equal(2, teams.Count);
        Assert.Equal(new[] { u1.Id, u2.Id }, teams.First(t => t.LehrerIds.Contains(d.Lehrer[1].Id)).UnterrichtIds);
        Assert.Equal(3, teams.First(t => t.LehrerIds.Contains(d.Lehrer[1].Id)).Wochenstunden);
        var jeLehrer = CccAnalyse.TeamsJeLehrer(teams, new DatenIndex(d));
        Assert.Equal(d.Lehrer[0].Id, jeLehrer[0].LehrerId);
        Assert.Equal(2, jeLehrer[0].Teams.Count);
        d.Lehrer[0].Zeitwuensche.Set(0, 1, -3);
        Assert.Equal(-3, CccAnalyse.TeamZeitwunsch(new DatenIndex(d), teams[0], 0, 1));
    }

    [Fact]
    public void DiagnoseFuerWocheOhneNichtGueltigenUnterricht()
    {
        var d = Klein();
        var u1 = U(d, 1, "A", "D", "5a");
        var u2 = U(d, 1, "B", "M", "5a");
        u2.VonDatum = d.Einstellungen.SchuljahrVon.AddDays(60);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u1.Id, Tag = 0, Stunde = 1 });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u2.Id, Tag = 0, Stunde = 2 });
        var w = Diagnose.FuerWoche(d, d.Einstellungen.SchuljahrVon);
        Assert.Single(w.Unterrichte);
        Assert.Single(w.Stundenplan);
        Assert.Equal(2, d.Unterrichte.Count);
        Assert.Equal(2, Diagnose.FuerWoche(d, d.Einstellungen.SchuljahrVon.AddDays(70)).Unterrichte.Count);
    }

    [Fact]
    public void DrucknamenUndSeitenaufteilung()
    {
        var l = new Lehrer { Kurzname = "Gauss", Langname = "Gauss", Vorname = "Carl", Titel = "Dr." };
        Assert.Equal("Gauss", Drucknamen.Lehrer(l, LehrerDruckname.Kurzname));
        Assert.Equal("Dr. Gauss Carl", Drucknamen.Lehrer(l, LehrerDruckname.TitelLangnameVorname));
        Assert.Equal("Gauss, Carl", Drucknamen.Lehrer(l, LehrerDruckname.LangnameVorname));
        Assert.Equal("Carl Gauss", Drucknamen.Lehrer(l, LehrerDruckname.VornameLangname));
        Assert.Equal("X", Drucknamen.Lehrer(new Lehrer { Kurzname = "X" }, LehrerDruckname.Langname));

        var det = new StundenplanDruckdetails { PlaeneNebeneinander = 2, PlaeneUebereinander = 3 };
        Assert.Equal(6, Druckseiten.PlaeneJeSeite(det));
        var seiten = Druckseiten.Einzelplaene(Enumerable.Range(1, 7).ToList(), det);
        Assert.Equal(2, seiten.Count);
        Assert.Equal(6, seiten[0].Count);
        det.JedeWocheEinPlan = true;
        det.JeElementNeueSeite = true;
        det.Schulwoche = "1-2,5";
        var wochen = Druckseiten.Wochen(det, 1);
        Assert.Equal(new[] { 1, 2, 5 }, wochen);
        var s2 = Druckseiten.Einzelplaene(new[] { 10, 11 }, det, wochen);
        Assert.Equal(2, s2.Count);
        Assert.All(s2, s => Assert.Equal(3, s.Count));
        Assert.Equal(new[] { 1, 2, 3 }, Druckseiten.Wochen(new StundenplanDruckdetails { AnzahlWochen = 3 }, 1));

        // Übersichtsplan: 5 Tage × 8 Stunden, 24 Spalten → 3 Tage je Block (nur ganze Tage)
        var u = new StundenplanDruckdetails { ZeilenSpaltenAutomatisch = true };
        var bl = Druckseiten.Uebersicht(10, 5, 8, u, 20, 24);
        Assert.Equal(2, bl.Count);
        Assert.Equal((0, 2), bl[0].Bloecke[0]);
        Assert.Equal((3, 4), bl[1].Bloecke[0]);
        u.MehrereBloecke = true;
        var mb = Druckseiten.Uebersicht(8, 5, 8, u, 20, 24);
        Assert.Single(mb);
        Assert.Equal(2, mb[0].Bloecke.Count);
        u.MehrereBloecke = false;
        u.NurEinzeltage = true;
        Assert.Equal(5, Druckseiten.Uebersicht(8, 5, 8, u, 20, 24).Count);
        var fest = new StundenplanDruckdetails { ZeilenSpaltenAutomatisch = false, ZeilenProBlatt = 4, SpaltenProBlatt = 16 };
        Assert.Equal(3 * 3, Druckseiten.Uebersicht(10, 5, 8, fest, 40, 40).Count);
    }

    [Fact]
    public void HtmlEinstellungenWirken()
    {
        var d = Klein();
        U(d, 1, "A", "D", "5a");
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 1 });
        var h = d.Einstellungen.Html;
        h.SchaltflaechenOben = true;
        h.SchaltflaechenUnten = false;
        h.BodyEigenschaften = "bgcolor=\"#ffffee\"";
        h.TableEigenschaften = "border=\"2\"";
        h.Fusszeile = 1;
        var seite = Export.InfoStundenplan(d).First(x => x.Key.StartsWith("kl_")).Value;
        Assert.Contains("iso-8859-1", seite);
        Assert.Contains("<body bgcolor=\"#ffffee\">", seite);
        Assert.Contains("border=\"2\"", seite);
        Assert.True(seite.IndexOf("<nav>", StringComparison.Ordinal) < seite.IndexOf("<table", StringComparison.Ordinal));
        Assert.Contains("Stand", seite);
        Assert.Same(System.Text.Encoding.Latin1, Export.Kodierung(h));
        h.Utf8 = true;
        Assert.Contains("utf-8", Export.InfoStundenplan(d)["index.html"]);
    }

    [Fact]
    public void VergleichsmodusFindetGeaendertePlaeneUndUnterrichte()
    {
        var a = Klein();
        var u1 = U(a, 1, "A", "D", "5a");
        U(a, 1, "B", "M", "5b");
        var r1 = new Raum { Id = a.NeueId(), Kurzname = "R1" };
        var r2 = new Raum { Id = a.NeueId(), Kurzname = "R2" };
        a.Raeume.AddRange(new[] { r1, r2 });
        a.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 1, Raeume = { r1.Id } });
        a.Stundenplan.Add(new Planstunde { UnterrichtId = 2, Tag = 0, Stunde = 1 });
        var b = Dokument.Klonen(a);
        // nur Raumänderung bei 5a, Verlegung bei 5b
        b.Stundenplan[0].Raeume[0] = r2.Id;
        b.Stundenplan[1].Stunde = 2;
        Assert.Equal(new[] { "5a", "5b" }, Vergleich.GeaendertePlaene(a, b, ElementTyp.Klasse, ohneRaeume: false, detailliert: false));
        Assert.Equal(new[] { "5b" }, Vergleich.GeaendertePlaene(a, b, ElementTyp.Klasse, ohneRaeume: true, detailliert: false));
        var (nurA, nurB) = Vergleich.Stundenunterschiede(a, b, ElementTyp.Klasse, "5b", true, false);
        Assert.Single(nurA);
        Assert.Single(nurB);
        // Unterricht und Stammdaten
        b.Unterrichte[0].Wochenstunden = 3;
        b.Lehrer[0].Langname = "Anders";
        Assert.Contains(Vergleich.Unterrichte(a, b), z => z.Schluessel == u1.Id.ToString() && z.Felder.Any(f => f.Feld == "Wst" && f.Aktuell == "1" && f.Vergleich == "3"));
        Assert.Contains(Vergleich.Stammdaten(a, b, ElementTyp.Lehrer), z => z.Schluessel == "A" && z.Felder.Any(f => f.Feld == "Langname"));
        Assert.Empty(Vergleich.Stammdaten(a, b, ElementTyp.Klasse));
        Assert.Equal(a.AktivePeriodeId, Vergleich.Periode(a, a.AktivePeriodeId).AktivePeriodeId);
    }

    [Fact]
    public void ZuVieleKlassenInDerMittagspause()
    {
        var d = Klein();
        d.Zeitraster.StundenProTag = 3;
        d.Zeitraster.MittagspauseVon = 2;
        d.Zeitraster.MittagspauseBis = 2;
        d.Zeitraster.MaxKlassenGleicheMittagspause = 1;
        Dokument.Normalisieren(d);
        // beide Klassen: 1. und 3. Stunde Unterricht, 2. Stunde frei = Mittagspause
        foreach (var (k, l1, l2) in new[] { ("5a", "A", "B"), ("5b", "C", "A") })
        {
            var x = U(d, 1, l1, "D", k);
            var y = U(d, 1, l2, "M", k);
            d.Stundenplan.Add(new Planstunde { UnterrichtId = x.Id, Tag = 0, Stunde = 1 });
            d.Stundenplan.Add(new Planstunde { UnterrichtId = y.Id, Tag = 0, Stunde = 3 });
        }
        var mp = Diagnose.Mittagspausen(d);
        Assert.Equal(2, mp.Single(m => m.Tag == 0 && m.Stunde == 2).Klassen.Count);
        Assert.Contains(Diagnose.Stundenplan(d), p => p.Titel == "Zu viele Klassen gleichzeitig in der Mittagspause" && p.Faelle.Count == 1);
        d.Zeitraster.MaxKlassenGleicheMittagspause = 2;
        Assert.DoesNotContain(Diagnose.Stundenplan(d), p => p.Titel == "Zu viele Klassen gleichzeitig in der Mittagspause");
    }

    [Fact]
    public void ProzentverplanungUndRaumkapazitaet()
    {
        var d = Demo.Beispielschule();
        var o = new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Schritte = 1, Seed = 3, Prozent = 30 };
        var e = Optimierer.Optimieren(d, o)[0];
        var wst = d.Unterrichte.Where(u => !u.Ignorieren).Sum(u => u.Wochenstunden);
        Assert.True(e.Plan.Count < wst * 0.6, $"{e.Plan.Count} von {wst}");
        // Raumreihenfolge: zu kleiner Wunschraum hinter einen ausreichend großen Ausweichraum
        var k = Klein();
        var klein = new Raum { Id = k.NeueId(), Kurzname = "Klein", Kapazitaet = 10 };
        var gross = new Raum { Id = k.NeueId(), Kurzname = "Gross", Kapazitaet = 30 };
        klein.AusweichraumId = gross.Id;
        k.Raeume.AddRange(new[] { klein, gross });
        k.Klassen[0].SchuelerMaennlich = 12;
        k.Klassen[0].SchuelerWeiblich = 12;
        var u = U(k, 1, "A", "D", "5a");
        u.Zeilen[0].FachraumId = klein.Id;
        var ix = new DatenIndex(k);
        Assert.Equal(new[] { gross.Id, klein.Id }, Optimierer.RaumReihenfolge(k, ix, u, u.Zeilen[0], new OptimierungsOptionen()).ToArray());
        Assert.Equal(new[] { klein.Id, gross.Id }, Optimierer.RaumReihenfolge(k, ix, u, u.Zeilen[0], new OptimierungsOptionen { RaumkapazitaetBeruecksichtigen = false }).ToArray());
    }
}
