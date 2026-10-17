using System.Drawing;
using UntPlan.Core;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Sprint 26: Clientbereich des Hauptfensters mit eigener Titelleiste (kein weißer Streifen, maximiert genau der Arbeitsbereich).</summary>
public class FensterClientBereichTests
{
    private static readonly Rectangle Arbeit = new(0, 0, 1920, 1032); // Taskleiste unten (48 px)

    [Fact]
    public void FenstermodusOhneOberenRahmen()
    {
        // System: 8 px Rahmen links/rechts/unten, 31 px Titelleiste + Rahmen oben
        var fenster = new Rectangle(100, 50, 1200, 800);
        var system = Rectangle.FromLTRB(108, 89, 1292, 842);
        var c = Fensterlage.ClientBereich(fenster, system, false, Arbeit);
        Assert.Equal(Rectangle.FromLTRB(108, 50, 1292, 842), c);
    }

    [Fact]
    public void MaximiertGenauDerArbeitsbereich()
    {
        // Windows legt das maximierte Fenster um die Rahmendicke (100 %: 8 px) über den Bildschirm
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1040);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1032);
        Assert.Equal(Arbeit, Fensterlage.ClientBereich(fenster, system, true, Arbeit));
    }

    [Theory]
    [InlineData(1.25, 11)]
    [InlineData(1.5, 12)]
    [InlineData(2.0, 16)]
    public void MaximiertUnabhaengigVonDerDpiSkalierung(double skala, int rahmen)
    {
        // Rahmendicke je DPI verschieden: der Programmkopf beginnt trotzdem genau an der Oberkante des Arbeitsbereichs (kein Hochrutschen, kein Streifen)
        var arbeit = new Rectangle(0, 0, (int)(1920 * skala), (int)(1032 * skala));
        var fenster = Rectangle.FromLTRB(-rahmen, -rahmen, arbeit.Right + rahmen, arbeit.Bottom + rahmen);
        var system = Rectangle.FromLTRB(0, 40, arbeit.Right, arbeit.Bottom);
        var c = Fensterlage.ClientBereich(fenster, system, true, arbeit);
        Assert.Equal(arbeit, c);
    }

    [Fact]
    public void MaximiertMitTaskleisteObenUndZweitemMonitor()
    {
        var arbeit = new Rectangle(1920, 40, 2560, 1400);
        var fenster = Rectangle.FromLTRB(1912, 32, 4488, 1448);
        var system = Rectangle.FromLTRB(1920, 63, 4480, 1440);
        Assert.Equal(arbeit, Fensterlage.ClientBereich(fenster, system, true, arbeit));
    }

    [Fact]
    public void MaximiertOhneArbeitsbereichObenWieLinks()
    {
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1040);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1032);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 1920, 1032), Fensterlage.ClientBereich(fenster, system, true, Rectangle.Empty));
    }

    [Fact]
    public void AutomatischAusgeblendeteTaskleisteBleibtErreichbar()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1088);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1080);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 1920, 1079), Fensterlage.ClientBereich(fenster, system, true, monitor, 3));
        Assert.Equal(Rectangle.FromLTRB(0, 1, 1920, 1080), Fensterlage.ClientBereich(fenster, system, true, monitor, 1));
    }
}
