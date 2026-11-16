using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Handbuch Kap. II/III: Teilungs-Nummer, Studenten je Klasse/Unterricht, Klassenlehrerschaften, Hohlstunden, Unterrichtsdruck.</summary>
public class StammdatenUnterrichtHandbuchTests
{
    private static Schuldatei Datei(out Klasse k, out Lehrer a, out Lehrer b, out Fach f)
    {
        var d = new Schuldatei();
        Dokument.Normalisieren(d);
        k = new Klasse { Id = d.NeueId(), Kurzname = "1a" };
        a = new Lehrer { Id = d.NeueId(), Kurzname = "A" };
        b = new Lehrer { Id = d.NeueId(), Kurzname = "B" };
        f = new Fach { Id = d.NeueId(), Kurzname = "E" };
        d.Klassen.Add(k);
        d.Lehrer.AddRange(new[] { a, b });
        d.Faecher.Add(f);
        return d;
    }

    private static Unterricht U(Schuldatei d, int wst, params (Lehrer L, Fach F, Klasse[] K)[] zeilen)
    {
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = wst };
        foreach (var z in zeilen) u.Zeilen.Add(new Kopplungszeile { LehrerId = z.L.Id, FachId = z.F.Id, KlassenIds = z.K.Select(x => x.Id).ToList() });
        d.Unterrichte.Add(u);
        return u;
    }

    [Fact]
    public void Teilungsnummer_GroessteStundenzahlZaehlt_NullZaehltNicht()
    {
        var d = Datei(out var k, out var a, out var b, out var f);
        U(d, 4, (a, f, new[] { k })).Teilungsnummer = 1;
        U(d, 3, (b, f, new[] { k })).Teilungsnummer = 1;
        U(d, 2, (a, f, new[] { k }));
        U(d, 5, (b, f, new[] { k })).Teilungsnummer = 0;
        Assert.Equal(4 + 2, Stammdatenkennzahlen.KlassenWochenstunden(new DatenIndex(d), k));
    }

    [Fact]
    public void Kopie_UebernimmtNeueUnterrichtsfelder()
    {
        var d = Datei(out var k, out var a, out _, out var f);
        var u = U(d, 2, (a, f, new[] { k }));
        u.Teilungsnummer = 7;
        u.SchuelerMaennlich = 5;
        u.SchuelerWeiblich = 6;
        u.Verplanungsprioritaet = 3;
        var c = u.Kopie(99);
        Assert.Equal((7, 11, 3), (c.Teilungsnummer!.Value, c.Schuelerzahl, c.Verplanungsprioritaet));
    }

    [Fact]
    public void Schuelerzahl_Klasse_ZaehltInter_Unterricht_GehtBeiRaumkapazitaetVor()
    {
        var d = Datei(out var k, out var a, out _, out var f);
        k.SchuelerMaennlich = 10;
        k.SchuelerWeiblich = 10;
        k.SchuelerInter = 2;
        Assert.Equal(22, k.Schuelerzahl);
        var gross = new Raum { Id = d.NeueId(), Kurzname = "Gross", Kapazitaet = 30 };
        var klein = new Raum { Id = d.NeueId(), Kurzname = "Klein", Kapazitaet = 12, AusweichraumId = null };
        d.Raeume.AddRange(new[] { klein, gross });
        klein.AusweichraumId = gross.Id;
        gross.AusweichraumId = klein.Id;
        var u = U(d, 1, (a, f, new[] { k }));
        u.Zeilen[0].FachraumId = klein.Id;
        var o = new OptimierungsOptionen { RaumkapazitaetBeruecksichtigen = true };
        // Klasse mit 22: zuerst der große Raum
        Assert.Equal(gross.Id, Optimierer.RaumReihenfolge(d, new DatenIndex(d), u, u.Zeilen[0], o).First());
        // nur 10 Studenten/Studentinnen am Unterricht: der kleine Raum reicht
        u.SchuelerMaennlich = 4;
        u.SchuelerWeiblich = 6;
        Assert.Equal(klein.Id, Optimierer.RaumReihenfolge(d, new DatenIndex(d), u, u.Zeilen[0], o).First());
    }

    [Fact]
    public void Klassenlehrerschaften_ZeigtAnzahlUndKlassen()
    {
        var d = Datei(out var k, out var a, out var b, out _);
        var k2 = new Klasse { Id = d.NeueId(), Kurzname = "2b", KlassenlehrerIds = { a.Id } };
        d.Klassen.Add(k2);
        k.KlassenlehrerIds.Add(a.Id);
        Assert.Equal("2: 1a, 2b", Stammdatenkennzahlen.Klassenlehrerschaften(d, a));
        Assert.Equal("", Stammdatenkennzahlen.Klassenlehrerschaften(d, b));
    }

    [Fact]
    public void HohlstundenAktuell_ZaehltLueckenOhneMittagspause()
    {
        var d = Datei(out var k, out var a, out _, out var f);
        d.Zeitraster.LetzteVormittagsstunde = 5;
        d.Zeitraster.MittagspauseVon = 5;
        d.Zeitraster.MittagspauseBis = 7;
        var u = U(d, 6, (a, f, new[] { k }));
        void P(int tag, int std) => d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = std, Raeume = { null } });
        // Montag: 1, 3 → eine Hohlstunde (2)
        P(0, 1); P(0, 3);
        // Dienstag: 4, 8 → Lücke 5–7 im Mittagsfenster: Mittagspause (ohne Bereich bei der Lehrkraft: ganze Lücke)
        P(1, 4); P(1, 8);
        var ix = new DatenIndex(d);
        Assert.Equal(1, Stammdatenkennzahlen.HohlstundenAktuell(d, ix, a));
        // Mittagspause der Lehrkraft höchstens 1 Stunde: 2 der 3 Lücken am Dienstag sind Hohlstunden
        a.Mittagspause = new Bereich(1, 1);
        Assert.Equal(3, Stammdatenkennzahlen.HohlstundenAktuell(d, ix, a));
    }

    [Fact]
    public void Unterrichtsdruck_UnterrichtDesElements_UndZusammenfassen()
    {
        var d = Datei(out var k, out var a, out var b, out var f);
        var k2 = new Klasse { Id = d.NeueId(), Kurzname = "1b" };
        d.Klassen.Add(k2);
        // D der 1a,1b getrennt: U1 nur Lehrer A, U2 mit A und Förderlehrer B
        var u1 = U(d, 3, (a, f, new[] { k, k2 }));
        var u2 = U(d, 1, (a, f, new[] { k, k2 }), (b, f, new[] { k, k2 }));
        var ix = new DatenIndex(d);
        var normal = Unterrichtsdruck.Eintraege(ix.UnterrichteVon(ElementTyp.Lehrer, a.Id), ElementTyp.Lehrer, a.Id, false, false);
        Assert.Equal(2, normal.Count);
        Assert.Equal(2, normal[1].Zeilen.Count);
        var nurA = Unterrichtsdruck.Eintraege(ix.UnterrichteVon(ElementTyp.Lehrer, a.Id), ElementTyp.Lehrer, a.Id, true, false);
        Assert.Equal(new[] { 0 }, nurA[1].Zeilen);
        var zus = Unterrichtsdruck.Eintraege(ix.UnterrichteVon(ElementTyp.Lehrer, a.Id), ElementTyp.Lehrer, a.Id, true, true);
        var e = Assert.Single(zus);
        Assert.Equal(new[] { u1.Id, u2.Id }, e.Nummern);
        Assert.Equal(4, e.Wochenstunden);
    }

    [Fact]
    public void ZeitwuenscheText_ListetWuenscheMitTagUndStunde()
    {
        var d = Datei(out _, out _, out _, out _);
        var zw = new Zeitwuensche();
        zw.Set(1, 2, 1);
        zw.Set(0, 1, -3);
        Assert.Equal("Mo-1:-3 Di-2:+1", Stammdatenkennzahlen.ZeitwuenscheText(d, zw));
    }
}
