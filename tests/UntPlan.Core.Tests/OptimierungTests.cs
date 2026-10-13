using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using Xunit;
using Xunit.Abstractions;

namespace UntPlan.Core.Tests;

public class OptimierungTests
{
    private readonly ITestOutputHelper _out;
    public OptimierungTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(Strategie.A)]
    [InlineData(Strategie.B)]
    [InlineData(Strategie.D)]
    public void BeispielschuleWirdKollisionsfreiVerplant(Strategie s)
    {
        var d = Demo.Beispielschule();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = s, AnzahlPlaene = 2, Schritte = 2, Seed = 1 });
        var best = erg[0];
        var k = best.Kennzahlen;
        _out.WriteLine($"{s}: {sw.ElapsedMilliseconds} ms, Bewertung {k.Bewertung}, nv {k.NichtVerplant}, HohlKl {k.HohlstundenKlassen}, HohlL {k.HohlstundenLehrer}, Kern {k.Kernzeitverletzungen}, 2x {k.FachZweimalTag}, Dopp {k.Doppelstundenfehler}, ZW {k.Zeitwunschverletzungen}, Koll {k.Kollisionen}, Raum {k.OhneRaum}");
        foreach (var g in Bewertung.Verstoesse(d, best.Plan).GroupBy(v => v.Art).OrderByDescending(g => g.Sum(x => x.Kosten)).Take(8))
            _out.WriteLine($"   {g.Key}: {g.Count()} ({g.Sum(x => x.Kosten):0})");
        Assert.Equal(0, k.Kollisionen);
        var wst = d.Unterrichte.Sum(u => u.Wochenstunden);
        Assert.True(best.Plan.Count + k.NichtVerplant >= wst - 0);
    }
}
