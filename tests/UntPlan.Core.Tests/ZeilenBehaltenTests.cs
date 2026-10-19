using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Gefilterte Vertretungsliste: bearbeitete (und mitgenommene) Zeilen bleiben bis zum Zeilenwechsel sichtbar.</summary>
public class ZeilenBehaltenTests
{
    [Fact]
    public void BearbeiteteZeileBleibtBisZumWechselInEineAndereZeile()
    {
        var b = new ZeilenBehalten();
        Assert.False(b.Zeilenwechsel(5));
        b.Merken(new[] { 7, 0 });
        Assert.True(b.Aktiv);
        Assert.True(b.Enthaelt(7));
        Assert.False(b.Enthaelt(0));
        // andere Spalte derselben Zeile: nichts geschieht
        Assert.False(b.Zeilenwechsel(7));
        Assert.True(b.Enthaelt(7));
        // andere Zeile: freigeben und neu aufbauen
        Assert.True(b.Zeilenwechsel(8));
        Assert.False(b.Aktiv);
        Assert.False(b.Enthaelt(7));
    }

    [Fact]
    public void MitgenommeneZeilenBleibenSichtbarZaehlenAberNichtAlsBearbeitet()
    {
        var b = new ZeilenBehalten();
        // Vertreter in Zeile 7 eingetragen; die gekoppelte Zeile 9 und die übrigen sichtbaren Zeilen bleiben mit stehen
        b.Merken(new[] { 7 }, new[] { 7, 9, 11 });
        Assert.Equal(new[] { 7 }, b.Ids);
        Assert.Equal(new[] { 9, 11 }, b.Mitgenommen.OrderBy(x => x));
        Assert.True(b.Enthaelt(9));
        Assert.False(b.Zeilenwechsel(7));
        Assert.True(b.Enthaelt(11));
        // Wechsel in eine mitgenommene Zeile verlässt die bearbeitete Zeile: alles wird freigegeben
        Assert.True(b.Zeilenwechsel(9));
        Assert.False(b.Enthaelt(9));
        Assert.False(b.Aktiv);
    }

    [Fact]
    public void LeerenGibtAllesFrei()
    {
        var b = new ZeilenBehalten();
        b.Merken(new[] { 1 }, new[] { 2 });
        b.Leeren();
        Assert.False(b.Aktiv);
        Assert.False(b.Zeilenwechsel(3));
    }
}
