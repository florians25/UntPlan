using UntPlan.Core;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Runde 3 B: verlässliches Rückgängig/Wiederherstellen (Sammelvorgänge, Beschreibungen für die Statuszeile).</summary>
public class R3BBedienungTests
{
    private static Dokument Doc() => new(Demo.Beispielschule());

    [Fact]
    public void Sammeln_FasstMehrereAenderungenZuEinerStufeZusammen()
    {
        var doc = Doc();
        var alt0 = doc.Daten.Lehrer[0].Langname;
        var alt1 = doc.Daten.Lehrer[1].Langname;
        using (doc.Sammeln("Einfügen"))
        {
            doc.Aendern("Zelle 1", AenderungsArt.Stammdaten, d => d.Lehrer[0].Langname = "A");
            doc.Aendern("Zelle 2", AenderungsArt.Stammdaten, d => d.Lehrer[1].Langname = "B");
        }
        Assert.Equal("Einfügen", doc.RueckgaengigText);
        Assert.Single(doc.Historie);
        doc.Rueckgaengig();
        Assert.Equal(alt0, doc.Daten.Lehrer[0].Langname);
        Assert.Equal(alt1, doc.Daten.Lehrer[1].Langname);
        Assert.False(doc.KannRueckgaengig);
        Assert.Equal("Einfügen", doc.WiederholenText);
        doc.Wiederholen();
        Assert.Equal("A", doc.Daten.Lehrer[0].Langname);
        Assert.Equal("B", doc.Daten.Lehrer[1].Langname);
        Assert.Null(doc.WiederholenText);
    }

    [Fact]
    public void Sammeln_OhneAenderungLegtKeineStufeAn_UndDanachWiederEinzeln()
    {
        var doc = Doc();
        using (doc.Sammeln("Leer")) { }
        Assert.False(doc.KannRueckgaengig);
        using (doc.Sammeln("Außen"))
        using (doc.Sammeln("Innen"))
            doc.Aendern("X", AenderungsArt.Stammdaten, d => d.Lehrer[0].Langname = "X");
        doc.Aendern("Y", AenderungsArt.Stammdaten, d => d.Lehrer[0].Langname = "Y");
        Assert.Equal(new[] { "Y", "Außen" }, doc.Historie);
    }

    [Fact]
    public void Sammeln_FehlschlagMittendrinBehaeltDieErfolgreichenSchritte()
    {
        var doc = Doc();
        var alt0 = doc.Daten.Lehrer[0].Langname;
        var alt1 = doc.Daten.Lehrer[1].Langname;
        using (doc.Sammeln("Einfügen"))
        {
            doc.Aendern("Zelle 1", AenderungsArt.Stammdaten, d => d.Lehrer[0].Langname = "A");
            Assert.Throws<FormatException>(() => doc.Aendern("Zelle 2", AenderungsArt.Stammdaten, d => { d.Lehrer[1].Langname = "B"; throw new FormatException(); }));
        }
        Assert.Equal("A", doc.Daten.Lehrer[0].Langname);
        Assert.Equal(alt1, doc.Daten.Lehrer[1].Langname);
        doc.Rueckgaengig();
        Assert.Equal(alt0, doc.Daten.Lehrer[0].Langname);
    }
}
