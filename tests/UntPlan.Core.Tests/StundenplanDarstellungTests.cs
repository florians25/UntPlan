using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Stundenplan-Darstellung nach den Handbuch-Bildern: voreingestellte Plan-Schrift Arial.</summary>
public class StundenplanDarstellungTests
{
    [Fact]
    public void LeereSchriftartIstArial()
    {
        var f = new StundenplanFormat();
        Assert.Equal("", f.Schriftart);
        Assert.Equal("Arial", f.SchriftartWirksam);
    }

    [Fact]
    public void EigeneSchriftartBleibt()
    {
        var f = new StundenplanFormat { Schriftart = "Tahoma" };
        Assert.Equal("Tahoma", f.SchriftartWirksam);
    }

    [Fact]
    public void WirksameSchriftartWirdNichtGespeichert()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new StundenplanFormat());
        var zurueck = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(json)!;
        Assert.Equal("", zurueck.Schriftart);
        Assert.Equal(StundenplanFormat.StandardSchriftart, zurueck.SchriftartWirksam);
    }
}
