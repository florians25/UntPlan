using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

public class VertretungsnummernTests
{
    [Fact]
    public void Pfeile_springen_zur_naechsten_bzw_vorigen_vorhandenen_Nummer()
    {
        var ids = new[] { 3, 7, 12, 5 };
        Assert.Equal(7, Vertretungsnummern.Nachbar(ids, 5, 1));
        Assert.Equal(3, Vertretungsnummern.Nachbar(ids, 5, -1));
        Assert.Equal(5, Vertretungsnummern.Nachbar(ids, 4, 1));
        Assert.Null(Vertretungsnummern.Nachbar(ids, 12, 1));
        Assert.Null(Vertretungsnummern.Nachbar(ids, 3, -1));
        Assert.Equal(3, Vertretungsnummern.Nachbar(ids, 0, 1));
    }
}
