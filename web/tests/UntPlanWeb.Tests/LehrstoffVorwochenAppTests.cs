using System.Net.Http.Json;
using UntPlanWeb.Dienste;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>Lehrstoff aus den Vorwochen für die App: Einträge mit Beginn und Ende laut Zeitraster (Datumszeile im Unterblatt).</summary>
public sealed class LehrstoffVorwochenAppTests
{
    [Fact]
    public async Task Vorstunden_liefern_Beginn_und_Ende_laut_Zeitraster()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tage = Enumerable.Range(0, 14).Select(i => new DateOnly(2026, 10, 12).AddDays(i));
        var alle = tage.Where(d => d >= new DateOnly(2026, 10, 14)).SelectMany(d => plan.Karten(stand, d, Elementtyp.Klasse, "7a"))
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1)
            .GroupBy(x => x.Unterricht).Select(g => g.OrderBy(x => x.Datum).ThenBy(x => x.Von).ToList()).First(g => g.Count >= 2);
        var lehrer = alle[0].Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var erste = alle[0];
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde",
            new { datum = erste.Datum, stunde = erste.Von, unterricht = erste.Unterricht, thema = "Thema mit Zeit" }));
        var ziel = alle[1];
        var liste = (await Testserver.HolenAsync(c,
            $"/api/app/klassenbuch/lehrstoff/vorstunden?datum={ziel.Datum}&stunde={ziel.Von}&unterricht={ziel.Unterricht}&bis={ziel.Bis}")).AsArray();
        var e = Assert.Single(liste)!;
        var raster = stand.Paket.Zeitraster.Stunden;
        Assert.Equal(raster.First(z => z.Nr == erste.Von).Beginn, e["beginn"]!.GetValue<string>());
        Assert.Equal(raster.First(z => z.Nr == erste.Von + (ziel.Bis - ziel.Von)).Ende, e["ende"]!.GetValue<string>());
        Assert.Equal("Thema mit Zeit", e["thema"]!.GetValue<string>());
    }
}
