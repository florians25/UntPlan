using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>Übertragung „ab heute“ nach einer Übertragung des ganzen Schuljahres: die vergangenen Tage bleiben erhalten.</summary>
public class ZeitraumTests
{
    private static WebPaket Paket(string von, string bis, params string[] tage) => new()
    {
        Von = von, Bis = bis,
        Tage = tage.Select(t => new WebTag { Datum = t }).ToList(),
        Vertretungen = tage.Select(t => new WebVertretung { Datum = t, Text = "V " + t }).ToList(),
        Absenzen = tage.Select(t => new WebAbsenz { Art = "lehrer", Element = "MUE", Von = t, Bis = t }).ToList(),
        Tagestexte = tage.Select(t => new WebTagestext(t, 1, "T " + t, "alle", new())).ToList(),
    };

    [Fact]
    public void Vergangene_Tage_bleiben_bei_Uebertragung_ab_heute_erhalten()
    {
        var schuljahr = Paket("2026-08-01", "2027-07-31", "2026-09-01", "2026-10-15", "2026-10-20");
        var abHeute = Paket("2026-10-16", "2026-10-30", "2026-10-20", "2026-10-21");
        var z = Paketdienst.ZeitraumZusammenfuehren(schuljahr, abHeute);
        Assert.Equal(new[] { "2026-09-01", "2026-10-15", "2026-10-20", "2026-10-21" }, z.Tage.Select(t => t.Datum));
        // der übertragene Zeitraum ersetzt den alten Stand (20.10. nur einmal, aus dem neuen Paket)
        Assert.Equal(4, z.Vertretungen.Count);
        Assert.Equal(4, z.Absenzen.Count);
        Assert.Equal(4, z.Tagestexte.Count);
        Assert.Equal(("2026-08-01", "2027-07-31"), (z.Von, z.Bis));
    }

    [Fact]
    public void Pakete_ohne_Zeitraum_ersetzen_alles()
    {
        var alt = Paket("2026-08-01", "2027-07-31", "2026-09-01");
        var neu = Paket("", "", "2026-10-20");
        Assert.Single(Paketdienst.ZeitraumZusammenfuehren(alt, neu).Tage);
    }
}
