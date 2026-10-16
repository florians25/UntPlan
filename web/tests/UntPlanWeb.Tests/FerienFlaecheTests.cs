using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Ferien als eine Fläche (Regression): eine ganze freie Woche erscheint im Wochenraster als eine durchgehende Fläche, nicht je Tag –
/// auch wenn die Tage aus mehreren Einträgen bestehen (tageweise eingetragene unterrichtsfreie Tage, Ferien + Feiertag).
/// </summary>
public class FerienFlaecheTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public async Task Ganze_Ferienwoche_hat_fuer_jeden_Tag_dieselben_Ferienangaben()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-19");
        var tage = plan["tage"]!.AsArray().Select(t => t!).ToList();
        Assert.Equal(5, tage.Count);
        Assert.All(tage, t =>
        {
            Assert.True(t["frei"]!.GetValue<bool>());
            Assert.Equal("ferien", t["freiArt"]!.GetValue<string>());
            Assert.Equal("Herbstferien", t["freiGrund"]!.GetValue<string>());
            Assert.Equal("2026-10-17", t["freiVon"]!.GetValue<string>());
        });
    }

    [Fact]
    public void Raster_fasst_aufeinanderfolgende_freie_Tage_zu_einer_Flaeche_zusammen()
    {
        var js = Datei("js", "plangitter.js");
        var css = Datei("css", "nl-design.css");
        // eine Fläche über n Tage (--frei-tage) und je Tag eine Fläche nur für die Tagesansicht
        Assert.Contains("tt-ferien gesamt\" style=\"--frei-tage:${n}\"", js);
        // zusammengefasst wird jeder freie Folgetag mit Ferien/Feiertag/unterrichtsfrei – nicht nur bei gleichem Eintrag
        Assert.Contains("while (freiFlaeche(tage[i + n])) n++;", js);
        // Desktop: Einzelflächen aus, Gesamtfläche über die Spalten; Tagesansicht umgekehrt
        Assert.Contains(".tt-ferien.einzeln { display: none; }", css);
        Assert.Contains(".tt-ferien.gesamt { width: calc(var(--frei-tage, 1) * 100%", css);
    }
}
