using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Ferien je Tag: jeder freie Tag (Ferien, Feiertag, unterrichtsfrei) erscheint im Wochenraster als eigene Fläche mit eigenem Text
/// (Art, Name, Zeitraum) – aufeinanderfolgende freie Tage werden nicht mehr zu einer Fläche über mehrere Spalten zusammengefasst.
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
    public void Raster_zeigt_jeden_freien_Tag_als_eigene_Flaeche()
    {
        var js = Datei("js", "plangitter.js");
        var css = Datei("css", "nl-design.css");
        // je Tag eine Fläche mit dem eigenen Inhalt, keine Gesamtfläche über n Tage
        Assert.Contains("return `<div class=\"tt-ferien tag\">${freiInhalt([t])}</div>`;", js);
        Assert.Contains("if (t.frei) html += freiUeberlagerung(t);", js);
        Assert.DoesNotContain("tt-ferien gesamt", js);
        Assert.DoesNotContain("--frei-tage", js);
        Assert.DoesNotContain("--frei-tage", css);
        Assert.DoesNotContain(".tt-ferien.einzeln { display: none; }", css);
        // Fläche nur so breit wie die eigene Spalte (4px Überstand je Seite), Schrift höchstens Medium
        Assert.Contains(".tt-ferien { inset: 0 auto 0 -4px; width: calc(100% + 8px);", css);
        Assert.Contains(".tt-ferien .inhalt { top: 50%; transform: translateY(-50%); gap: 16px; font-size: 14px; font-weight: 500;", css);
    }
}
