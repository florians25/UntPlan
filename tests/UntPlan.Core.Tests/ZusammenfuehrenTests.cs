using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Desktop-Listen nach einer Änderung: nur betroffene Zeilen ändern sich, nichts verrutscht (bisherige Reihenfolge bleibt,
/// neue Zeilen kommen an ihren sortierten Platz, weggefallene verschwinden).
/// </summary>
public class ZusammenfuehrenTests
{
    private static List<string>? Z(string[] alt, string[] neu) => Listenabgleich.Zusammenfuehren(alt, neu, x => x);

    [Fact]
    public void GeaenderterSortierwertVerschiebtKeineZeile()
    {
        // B wurde so geändert, dass es neu einsortiert ans Ende gehörte: bleibt an seinem Platz
        Assert.Equal(new[] { "A", "B", "C", "neu" }, Z(new[] { "A", "B", "C", "neu" }, new[] { "A", "C", "B", "neu" }));
    }

    [Fact]
    public void NeueZeileKommtHinterIhrenVorgaenger()
    {
        Assert.Equal(new[] { "A", "B", "X", "C", "neu" }, Z(new[] { "A", "B", "C", "neu" }, new[] { "A", "B", "X", "C", "neu" }));
        // ohne Vorgänger: vor den ersten Nachfolger
        Assert.Equal(new[] { "X", "A", "B" }, Z(new[] { "A", "B" }, new[] { "X", "A", "B" }));
        // Vorgänger ist selbst neu
        Assert.Equal(new[] { "A", "X", "Y", "B" }, Z(new[] { "A", "B" }, new[] { "A", "X", "Y", "B" }));
        // Vorgänger steht wegen geänderter Sortierung woanders: die neue Zeile folgt ihm
        Assert.Equal(new[] { "C", "A", "X", "B" }, Z(new[] { "C", "A", "B" }, new[] { "A", "X", "B", "C" }));
    }

    [Fact]
    public void WeggefalleneZeilenVerschwindenRestBleibt()
    {
        Assert.Equal(new[] { "A", "C", "neu" }, Z(new[] { "A", "B", "C", "neu" }, new[] { "C", "A", "neu" }));
        // ganz neue Liste (z. B. anderes Datum): neue Sortierung
        Assert.Equal(new[] { "X", "Y" }, Z(new[] { "A", "B" }, new[] { "X", "Y" }));
        Assert.Equal(new string[0], Z(new[] { "A" }, new string[0]));
    }

    [Fact]
    public void DoppelteSchluesselErzwingenNeuaufbau()
    {
        Assert.Null(Z(new[] { "A", "B" }, new[] { "A", "A" }));
        Assert.Null(Z(new[] { "A", "A" }, new[] { "A", "B" }));
    }
}
