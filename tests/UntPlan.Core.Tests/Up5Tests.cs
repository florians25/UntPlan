using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Arbeitspaket UP5: behindernden Unterricht entplanen, Unterrichtseingabe, offene Blöcke, Nacherfassung umgeplanter Stunden.</summary>
public class Up5Tests
{
    private static (Schuldatei D, Lehrer L1, Lehrer L2, Klasse K1, Klasse K2, Fach F, Raum R1, Raum R2) Grunddaten()
    {
        var d = Demo.NeueDatei("Test", "GY");
        d.Lehrer.Clear(); d.Klassen.Clear(); d.Faecher.Clear(); d.Raeume.Clear(); d.Unterrichte.Clear(); d.Stundenplan.Clear();
        var l1 = new Lehrer { Id = d.NeueId(), Kurzname = "AB", Langname = "Abel" };
        var l2 = new Lehrer { Id = d.NeueId(), Kurzname = "CD", Langname = "Cordes" };
        var k1 = new Klasse { Id = d.NeueId(), Kurzname = "5a" };
        var k2 = new Klasse { Id = d.NeueId(), Kurzname = "5b" };
        var f = new Fach { Id = d.NeueId(), Kurzname = "M", Langname = "Mathematik" };
        var r1 = new Raum { Id = d.NeueId(), Kurzname = "R1" };
        var r2 = new Raum { Id = d.NeueId(), Kurzname = "R2" };
        d.Lehrer.AddRange(new[] { l1, l2 }); d.Klassen.AddRange(new[] { k1, k2 }); d.Faecher.Add(f); d.Raeume.AddRange(new[] { r1, r2 });
        d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = "MU", Langname = "Musik" });
        d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = "D", Langname = "Deutsch" });
        return (d, l1, l2, k1, k2, f, r1, r2);
    }

    private static Unterricht U(Schuldatei d, int wst, int? lehrer, int fach, params int[] klassen)
    {
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = wst, Zeilen = { new Kopplungszeile { LehrerId = lehrer, FachId = fach, KlassenIds = klassen.ToList() } } };
        d.Unterrichte.Add(u);
        return u;
    }

    // ------------------------------------------------------------------ behindernder Unterricht

    [Fact]
    public void BehinderndeStundenWerdenErkanntUndEntplant()
    {
        var (d, l1, l2, k1, k2, f, _, _) = Grunddaten();
        var a = U(d, 2, l1.Id, f.Id, k1.Id);
        var b = U(d, 2, l1.Id, f.Id, k2.Id);
        var c = U(d, 2, l2.Id, f.Id, k1.Id);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = a.Id, Tag = 0, Stunde = 1, Raeume = { null } });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = c.Id, Tag = 0, Stunde = 2, Raeume = { null } });
        var ix = new DatenIndex(d);
        // b (gleiche Lehrkraft wie a) in Mo-1 und c-Klasse ist nicht betroffen; Mo-1..2 als Block: a (Lehrer) behindert, c nicht (andere Klasse)
        var h = Behinderung.AnStellen(d, ix, b, new[] { (0, 1), (0, 2) });
        Assert.Single(h);
        Assert.Equal(a.Id, h[0].Unterricht.Id);
        Assert.Contains("Lehrer", h[0].Grund);
        Assert.Contains("Soll behindernder Unterricht entplant werden?", Behinderung.Fragetext(d, ix, h));
        Assert.Equal(1, Behinderung.Entplanen(d, h));
        Assert.DoesNotContain(d.Stundenplan, p => p.UnterrichtId == a.Id);
        Assert.Equal(2, new DatenIndex(d).NichtVerplant(a)); // wieder „nicht verplant“
    }

    [Fact]
    public void KlasseHinzufuegenMeldetNurNeueBehinderungen()
    {
        var (d, l1, l2, k1, k2, f, _, _) = Grunddaten();
        var a = U(d, 1, l1.Id, f.Id, k1.Id);
        var b = U(d, 1, l2.Id, f.Id, k2.Id);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = a.Id, Tag = 1, Stunde = 3, Raeume = { null } });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = b.Id, Tag = 1, Stunde = 3, Raeume = { null } });
        var ix = new DatenIndex(d);
        var neu = a.Kopie(a.Id);
        neu.Zeilen[0].KlassenIds.Add(k2.Id);
        var h = Behinderung.DurchAenderung(d, ix, a, neu);
        Assert.Single(h);
        Assert.Equal(b.Id, h[0].Unterricht.Id);
        Assert.Contains("Klasse", h[0].Grund);
        // bereits bestehende Kollision wird nicht erneut gemeldet
        a.Zeilen[0].KlassenIds.Add(k2.Id);
        var neu2 = a.Kopie(a.Id);
        Assert.Empty(Behinderung.DurchAenderung(d, new DatenIndex(d), a, neu2));
    }

    [Fact]
    public void FesterRaumOhneAusweichraumBehindert()
    {
        var (d, l1, l2, k1, k2, f, r1, r2) = Grunddaten();
        var a = U(d, 1, l1.Id, f.Id, k1.Id);
        a.Zeilen[0].FachraumId = r1.Id;
        var b = U(d, 1, l2.Id, f.Id, k2.Id);
        b.Zeilen[0].FachraumId = r1.Id;
        d.Stundenplan.Add(new Planstunde { UnterrichtId = a.Id, Tag = 2, Stunde = 4, Raeume = { r1.Id } });
        var ix = new DatenIndex(d);
        var h = Behinderung.AnStellen(d, ix, b, new[] { (2, 4) });
        Assert.Single(h);
        Assert.Contains("Raum R1", h[0].Grund);
        // mit Ausweichraum ist der Raum ersetzbar – keine Behinderung
        r1.AusweichraumId = r2.Id;
        Assert.Empty(Behinderung.AnStellen(d, new DatenIndex(d), b, new[] { (2, 4) }));
    }

    // ------------------------------------------------------------------ Unterrichtseingabe

    [Fact]
    public void EingabepruefungGibtVerstaendlicheHinweise()
    {
        var (d, _, _, _, _, _, _, _) = Grunddaten();
        Assert.Equal("M", Unterrichtseingabe.Pruefen(d, "Fach", "m").Wert);
        var mehrdeutig = Unterrichtseingabe.Pruefen(d, "Fach", "x");
        Assert.False(mehrdeutig.Gueltig);
        Assert.Contains("nicht vorhanden", mehrdeutig.Hinweis);
        var ergaenzt = Unterrichtseingabe.Pruefen(d, "Fach", "Deu");
        Assert.True(ergaenzt.Gueltig);
        Assert.Equal("D", ergaenzt.Wert);
        Assert.Contains("ergänzt", ergaenzt.Hinweis);
        Assert.Equal("AB", Unterrichtseingabe.Pruefen(d, "Lehrer", "ab").Wert);
        var lehrer = Unterrichtseingabe.Pruefen(d, "Lehrer", "ZZ");
        Assert.False(lehrer.Gueltig);
        Assert.Contains("Lehrer „ZZ“", lehrer.Hinweis);
        Assert.Equal("5a,5b", Unterrichtseingabe.Pruefen(d, "Klassen", "5A; 5b").Wert);
        var klasse = Unterrichtseingabe.Pruefen(d, "Klassen", "5a,7z");
        Assert.False(klasse.Gueltig);
        Assert.Contains("7z", klasse.Hinweis);
        Assert.False(Unterrichtseingabe.Pruefen(d, "Wst", "zwei").Gueltig);
        Assert.Equal("1-2", Unterrichtseingabe.Pruefen(d, "Dopp", "1 - 2").Wert);
        Assert.False(Unterrichtseingabe.Pruefen(d, "Dopp", "a-b").Gueltig);
        Assert.False(Unterrichtseingabe.Pruefen(d, "Block", "1").Gueltig);
        Assert.Equal("R1", Unterrichtseingabe.Pruefen(d, "Fachraum", "r1").Wert);
        var u = new Unterricht { Id = 1, Wochenstunden = 0, Zeilen = { new Kopplungszeile() } };
        var hinweise = Unterrichtseingabe.Pflichthinweise(u);
        Assert.Contains(hinweise, x => x.StartsWith("Wochenstunden fehlen"));
        Assert.Contains(hinweise, x => x.StartsWith("Fach fehlt"));
        Assert.Contains(hinweise, x => x.StartsWith("Klasse fehlt"));
        Assert.True(Unterrichtseingabe.PflichtFehlt(u, 0, "Fach"));
    }

    // ------------------------------------------------------------------ offene Blöcke

    [Fact]
    public void OffeneBloeckeInBlockgroesse()
    {
        var (d, l1, _, k1, _, f, _, _) = Grunddaten();
        var u = U(d, 4, l1.Id, f.Id, k1.Id);
        u.Doppelstunden = new Bereich(1, 1);
        Assert.Equal(new[] { 2, 1, 1 }, Planungsdienst.OffeneBloecke(d, new DatenIndex(d), u));
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 1, Raeume = { null } });
        Assert.Equal(new[] { 2, 1 }, Planungsdienst.OffeneBloecke(d, new DatenIndex(d), u));
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 2, Raeume = { null } });
        Assert.Equal(new[] { 1, 1 }, Planungsdienst.OffeneBloecke(d, new DatenIndex(d), u));
        u.Block = "3";
        Assert.Equal(new[] { 2 }, Planungsdienst.OffeneBloecke(d, new DatenIndex(d), u).Take(1));
        Assert.Equal(2, Planungsdienst.OffeneBloecke(d, new DatenIndex(d), u).Sum());
    }

    // ------------------------------------------------------------------ Nacherfassung umgeplanter Stunden

    private static (Schuldatei d, DateOnly tag, RegelStunde r, Vertretung v) MitVertretung()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, x.FachId) && x.Unterricht.Zeilen.Count == 1 &&
                                                      string.IsNullOrEmpty(x.Unterricht.Band) && d.Stundenplan.Count(p => p.UnterrichtId == x.Unterricht.Id && p.Tag == x.Planstunde.Tag) == 1);
        Umplanungsabgleich.StandSichern(d);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == r.Stunde);
        var k = VertretungsVorschlag.Berechnen(d, v).First(x => !x.Bereitschaft && !x.Freigesetzt);
        Vertretungen.VertreterSetzen(d, v, k.Lehrer.Id);
        v.Text = "Arbeitsblatt";
        return (d, tag, r, v);
    }

    [Fact]
    public void UmgeplanteStundeNimmtVertretungMit()
    {
        var (d, tag, r, v) = MitVertretung();
        var vertreter = v.VertreterId;
        var ix = new DatenIndex(d);
        var p = r.Planstunde;
        var neueStunde = Enumerable.Range(1, d.Zeitraster.StundenProTag).First(s => s != p.Stunde && Planungsdienst.Hindernisse(d, ix, r.Unterricht, p.Tag, s, new[] { p }).Count == 0 &&
                                                                                     d.Zeitraster.Verfuegbar(p.Tag, s));
        var alteStunde = p.Stunde;
        p.Stunde = neueStunde; // im Stundenplan umgeplant
        var erg = Umplanungsabgleich.Nacherfassen(d);
        Assert.Contains(erg.Umplanungen, x => x.UnterrichtId == r.Unterricht.Id && x.AltStunde == alteStunde && x.NeuStunde == neueStunde);
        Assert.Contains(v, erg.Uebertragen);
        var neu = d.Vertretungen.Single(x => x.Id == v.Id);
        Assert.Equal(tag, neu.Datum);
        Assert.Equal(neueStunde, neu.Stunde);
        Assert.Equal(vertreter, neu.VertreterId);
        Assert.Equal("Arbeitsblatt", neu.Text);
        Assert.DoesNotContain(d.Vertretungen, x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == alteStunde);
        // Tagesplan zeigt die Vertretung an der neuen Lage
        Assert.Contains(Tagesplan.Berechnen(d, tag).Stunde(neueStunde), e => e.Vertretung?.Id == v.Id);
        // Stand gemerkt: ein zweiter Abgleich ändert nichts
        Assert.False(Umplanungsabgleich.Nacherfassen(d).Geaendert);
        Assert.False(Umplanungsabgleich.StandVeraltet(d.AktivePeriode));
    }

    [Fact]
    public void NichtZuordenbareVertretungBleibtAlsVormerkung()
    {
        var (d, tag, r, v) = MitVertretung();
        d.Stundenplan.Remove(r.Planstunde); // Stunde gelöscht
        var erg = Umplanungsabgleich.Nacherfassen(d);
        Assert.Single(erg.Vormerkungen);
        Assert.Contains(erg.Hinweise, h => h.Contains("nicht mehr vorhanden"));
        var vm = d.Vormerkungen.Single(x => x.Id == erg.Vormerkungen[0].Id);
        Assert.Equal(r.Unterricht.Id, vm.UnterrichtId);
        Assert.True(vm.Offen > 0);
        Assert.Contains("Arbeitsblatt", vm.Text);
    }

    [Fact]
    public void BlockWirdAlsBlockGepaart()
    {
        var p = new Periode { Id = 1 };
        p.Vertretungsstand = new List<Planstunde>
        {
            new() { UnterrichtId = 5, Tag = 0, Stunde = 3 }, new() { UnterrichtId = 5, Tag = 0, Stunde = 4 }, new() { UnterrichtId = 5, Tag = 2, Stunde = 1 },
        };
        p.Plan = new List<Planstunde> { new() { UnterrichtId = 5, Tag = 4, Stunde = 1 }, new() { UnterrichtId = 5, Tag = 3, Stunde = 5 }, new() { UnterrichtId = 5, Tag = 3, Stunde = 6 } };
        var u = Umplanungsabgleich.Umplanungen(p, out var weg);
        Assert.Empty(weg);
        Assert.Contains(new Umplanung(5, 0, 3, 3, 5), u);
        Assert.Contains(new Umplanung(5, 0, 4, 3, 6), u);
        Assert.Contains(new Umplanung(5, 2, 1, 4, 1), u);
    }

    [Fact]
    public void GleichzeitigeFelderWerdenGestapelt()
    {
        // Stunde 3: zwei Unterrichte (D, M) gleichzeitig – große Pläne untereinander, sonst nebeneinander
        IReadOnlyList<string> Am(int s) => s == 3 ? new[] { "D", "M" } : Array.Empty<string>();
        var neben = Blockbildung.Anordnen(5, Am, x => x);
        Assert.All(neben, f => Assert.Equal(2, f.Spuren));
        var gestapelt = Blockbildung.Anordnen(5, Am, x => x, stapeln: true);
        Assert.All(gestapelt, f => { Assert.Equal(1, f.Spuren); Assert.Equal(2, f.StapelAnzahl); });
        Assert.Equal(new[] { 0, 1 }, gestapelt.Select(f => f.StapelIndex).OrderBy(x => x));
        // unterschiedliche Lage (Doppelstunde neben Einzelstunde): nicht stapeln
        IReadOnlyList<string> Am2(int s) => s == 3 ? new[] { "D", "M" } : s == 4 ? new[] { "D" } : Array.Empty<string>();
        Assert.All(Blockbildung.Anordnen(5, Am2, x => x, stapeln: true), f => Assert.Equal(1, f.StapelAnzahl));
    }
}
