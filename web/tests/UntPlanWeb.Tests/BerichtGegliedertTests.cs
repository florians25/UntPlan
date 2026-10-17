using System.Net;
using System.Net.Http.Json;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Gegliederte Abwesenheitsberichte wie in den Vorlagen: „Fehltage pro Klasse“ (je Klasse, je Schüler*in eine Zeile je Fehltag, Summe je
/// Schüler*in mit Schultagen, Summe je Klasse) und „Unentschuldigte Abwesenheiten“ (zusammenhängende Abwesenheiten ohne Entschuldigung mit
/// Beginn, Ende, Tagen, Stunden, Grund) – als Web-Ansicht (Zeilenarten) und als PDF.
/// </summary>
public class BerichtGegliedertTests
{
    [Fact]
    public async Task Fehltage_und_Unentschuldigte_gegliedert_mit_Summen()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var kinder = stand.Paket.Elemente.Schueler.Where(x => x.Klasse == "7a").Take(2).ToList();
        // Mo 12.10. und Mi 14.10. (Di 13.10. entfällt der Unterricht der 7a → zusammenhängend), dazu der Mittwoch für das zweite Kind
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[0].Id, von = "2026-10-12", bis = "2026-10-14", grund = "K" }));
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[1].Id, von = "2026-10-14", bis = "2026-10-14" }));

        var f = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/fehltage_klasse?klasse=7a&von=2026-10-12&bis=2026-10-14");
        var arten = f["arten"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        var zeilen = f["zeilen"]!.AsArray();
        Assert.Equal(new[] { "Schüler*in", "Datum", "Fehltage", "Fehlstunden", "entsch.", "unent.", "offen", "Schultage" }, f["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal("gruppe", arten[0]);
        Assert.Equal("Klasse 7a", zeilen[0]![0]!.GetValue<string>());
        Assert.Equal(2, arten.Count(a => a == "zwischensumme"));
        Assert.Equal("summe", arten[^1]);
        // Summe der Klasse: 3 Fehltage, alle offen
        var summe = zeilen[^1]!.AsArray();
        Assert.True(3 == summe[2]!.GetValue<int>(), f.ToJsonString());
        Assert.True(summe[6]!.GetValue<int>() + summe[5]!.GetValue<int>() + summe[4]!.GetValue<int>() == 3, f.ToJsonString());
        // Zwischensumme des ersten Kindes: 2 Tage, 2 Schultage der 7a im Zeitraum (Di entfällt)
        var zs = zeilen[arten.IndexOf("zwischensumme")]!.AsArray();
        Assert.Equal(2, zs[2]!.GetValue<int>());
        Assert.Equal(2, zs[7]!.GetValue<int>());

        var u = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/unentschuldigt?klasse=7a&von=2026-10-12&bis=2026-10-14&n1=1");
        var ua = u["arten"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        var uz = u["zeilen"]!.AsArray();
        var block = uz[ua.IndexOf("")]!.AsArray();
        Assert.Equal("12.10.2026", block[1]!.GetValue<string>());
        Assert.Equal("14.10.2026", block[2]!.GetValue<string>());
        Assert.Equal(2, block[3]!.GetValue<int>());
        Assert.Equal("Krankheit", block[5]!.GetValue<string>());
        // mindestens 2 Tage am Stück: nur noch das erste Kind
        var u2 = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/unentschuldigt?klasse=7a&von=2026-10-12&bis=2026-10-14&n1=2");
        Assert.Single(u2["arten"]!.AsArray(), a => a!.GetValue<string>() == "zwischensumme");

        foreach (var art in new[] { "fehltage_klasse", "unentschuldigt" })
        {
            var pdf = await leitung.GetAsync($"/api/app/berichte/erstellen/{art}?klasse=7a&von=2026-10-12&bis=2026-10-14&format=pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            var text = System.Text.Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync());
            Assert.StartsWith("%PDF", text);
            Assert.Contains("Summe 7a", text);
            Assert.Contains("Klasse 7a", text);
        }
    }
}
