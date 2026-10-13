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
