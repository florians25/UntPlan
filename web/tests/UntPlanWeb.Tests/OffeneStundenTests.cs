using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Klassenbuch → Offene Stunden: Rechte, Zeitbereiche, gespeicherter Zeitbereich, Klassenleitung, Kennzahlen; Wochenraster.</summary>
public class OffeneStundenTests
{
    private const string Url = "/api/app/klassenbuch/offene-stunden";

    private static IEnumerable<string> Lehrkraefte(JsonNode r) => r["zeilen"]!.AsArray().Select(z => z!["lehrer"]!.GetValue<string>()).Distinct();

    [Fact]
    public async Task Lehrkraft_sieht_nur_eigene_Stunden_und_behaelt_den_Zeitbereich()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");

        // Startwert: aktuelle Woche bis heute, nur eigene Stunden, fremde Lehrkräfte werden ignoriert
        var r = await Testserver.HolenAsync(c, $"{Url}?art=alle&lehrer=FIS,LAN");
        Assert.Equal(("woche", "2026-10-12", "2026-10-14"), (r["bereich"]!.GetValue<string>(), r["von"]!.GetValue<string>(), r["bis"]!.GetValue<string>()));
        Assert.False(r["alle"]!.GetValue<bool>());
        Assert.True(r["gespeichert"]!.GetValue<bool>());
        Assert.True(r["gesamt"]!.GetValue<int>() > 0);
        Assert.Equal(new[] { "SNE" }, Lehrkraefte(r));
        Assert.Equal(new[] { "SNE" }, r["lehrer"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains(r["bereiche"]!.AsArray(), b => b!["id"]!.GetValue<string>() == "schuljahr");

        // Zeitbereich speichern → gilt auch nach erneuter Anmeldung
        await Testserver.JsonAsync(await c.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "monat" }));
        var c2 = await s.AlsAsync("sne", "lehrer", "SNE");
        r = await Testserver.HolenAsync(c2, Url);
        Assert.Equal(("monat", "2026-10-01"), (r["bereich"]!.GetValue<string>(), r["von"]!.GetValue<string>()));

        // individueller Zeitraum (Klick aufs Datum) – Ende wird auf heute begrenzt
        await Testserver.JsonAsync(await c2.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "individuell", von = "2026-10-05", bis = "2026-10-30" }));
        r = await Testserver.HolenAsync(c2, Url);
        Assert.Equal(("individuell", "2026-10-05", "2026-10-14"), (r["bereich"]!.GetValue<string>(), r["von"]!.GetValue<string>(), r["bis"]!.GetValue<string>()));

        // ungültige Angaben
        Assert.Equal(HttpStatusCode.BadRequest, (await c2.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "irgendwann" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c2.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "individuell" })).StatusCode);
    }

    [Fact]
    public async Task Offen_Filter_und_Kennzahlen()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = s.Dienst<Plandienst>().Karten(stand, new DateOnly(2026, 10, 12), Elementtyp.Lehrer, "SNE").First(x => x.Unterricht.HasValue && x.Status == "regulaer");
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var r = await Testserver.HolenAsync(c, Url);
        var gesamt = r["gesamt"]!.GetValue<int>();
        Assert.Equal(gesamt, r["offen"]!.GetValue<int>());
        Assert.Equal(0, r["vollstaendig"]!.GetValue<int>());

        // Lehrstoff eintragen: Stunde fehlt nur noch bei der Abwesenheitskontrolle
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Ballspiele" }));
        r = await Testserver.HolenAsync(c, Url);
        Assert.Equal((gesamt, gesamt - 1), (r["ohneKontrolle"]!.GetValue<int>(), r["ohneLehrstoff"]!.GetValue<int>()));
        Assert.Equal(gesamt - 1, (await Testserver.HolenAsync(c, $"{Url}?art=lehrstoff"))["zeilen"]!.AsArray().Count);
        Assert.Equal(gesamt, (await Testserver.HolenAsync(c, $"{Url}?art=kontrolle"))["zeilen"]!.AsArray().Count);

        // Abwesenheiten kontrolliert → Stunde vollständig, erscheint nur noch unter „alle“
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = Array.Empty<object>() }));
        r = await Testserver.HolenAsync(c, Url);
        Assert.Equal(gesamt - 1, r["offen"]!.GetValue<int>());
        Assert.DoesNotContain(r["zeilen"]!.AsArray(), z => z!["datum"]!.GetValue<string>() == k.Datum && z["von"]!.GetValue<int>() == k.Von);
        var alle = await Testserver.HolenAsync(c, $"{Url}?art=alle");
        var z = alle["zeilen"]!.AsArray().Single(x => x!["datum"]!.GetValue<string>() == k.Datum && x["von"]!.GetValue<int>() == k.Von && x["unterricht"]!.GetValue<int>() == k.Unterricht)!;
        Assert.True(z["geprueft"]!.GetValue<bool>() && z["lehrstoff"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Schulleitung_und_Zusatzrechte_sehen_alle_Lehrkraefte_ab_aktueller_Woche()
    {
        await using var s = await Testserver.StartenAsync();
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        // gespeicherter Zeitbereich wird für Berechtigte nicht verwendet: Start immer aktuelle Woche
        await Testserver.JsonAsync(await leitung.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "schuljahr" }));
        var r = await Testserver.HolenAsync(leitung, $"{Url}?art=alle");
        Assert.Equal("woche", r["bereich"]!.GetValue<string>());
        Assert.True(r["alle"]!.GetValue<bool>());
        Assert.True(Lehrkraefte(r).Count() > 3);
        Assert.Equal(new[] { "FIS" }, Lehrkraefte(await Testserver.HolenAsync(leitung, $"{Url}?art=alle&lehrer=FIS")));

        // Lehrkraft ohne Zusatzrecht: nur eigene; mit „Offene Stunden“ bzw. „Klassenbuch – alle“: alle
        var bd = s.Dienst<Benutzerdienst>();
        foreach (var (login, recht) in new[] { ("fis", Zusatzrechte.OffeneStunden), ("mey", Zusatzrechte.KlassenbuchAlle) })
        {
            var c = await s.AlsAsync(login, "lehrer", login.ToUpperInvariant());
            Assert.Equal(new[] { login.ToUpperInvariant() }, Lehrkraefte(await Testserver.HolenAsync(c, $"{Url}?art=alle&lehrer=SNE")));
            bd.ZusatzrechteSetzen(bd.NachLogin(login)!.Id, new[] { recht });
            c = await s.AlsAsync(login, "lehrer", login.ToUpperInvariant());
            var mit = await Testserver.HolenAsync(c, $"{Url}?art=alle&lehrer=SNE");
            Assert.True(mit["alle"]!.GetValue<bool>());
            Assert.Equal(new[] { "SNE" }, Lehrkraefte(mit));
            Assert.True(Lehrkraefte(await Testserver.HolenAsync(c, $"{Url}?art=alle")).Count() > 3);
        }
    }

    [Fact]
    public async Task Klassenleitung_sieht_alle_Stunden_der_eigenen_Klasse()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN"); // Klassenleitung der 7a
        var r = await Testserver.HolenAsync(lan, $"{Url}?art=alle");
        Assert.Contains("7a", r["klassenleitung"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(new[] { "LAN" }, Lehrkraefte(r));

        var klasse = await Testserver.HolenAsync(lan, $"{Url}?art=alle&klasse=7a");
        Assert.True(Lehrkraefte(klasse).Count() > 1);
        Assert.All(klasse["zeilen"]!.AsArray(), z => Assert.Contains("7a", z!["klassen"]!.AsArray().Select(x => x!.GetValue<string>())));

        // andere Lehrkraft ohne Klassenleitung: mit Klassenfilter weiterhin nur die eigenen Stunden
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var fremd = await Testserver.HolenAsync(sne, $"{Url}?art=alle&klasse=7a");
        Assert.All(fremd["zeilen"]!.AsArray(), z => Assert.Equal("SNE", z!["lehrer"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Nur_fuer_das_Kollegium()
    {
        await using var s = await Testserver.StartenAsync();
        var schueler = await s.AlsAsync("schueler1", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.PutAsJsonAsync($"{Url}/zeitbereich", new { bereich = "woche" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Wochenraster_mit_Zeitraster_Hausaufgaben_und_Eintraegen()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = s.Dienst<Plandienst>().Karten(stand, new DateOnly(2026, 10, 14), Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer");
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, text = "Seite 12" }));
        var beginn = TimeOnly.Parse(k.Beginn);
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = k.Datum, klasse = "7a", art = "Lob", text = "Gut", zeit = $"{beginn.AddMinutes(5):HH:mm}" }));

        var w = await Testserver.HolenAsync(leitung, "/api/app/klassenbuch/woche?klasse=7a&datum=2026-10-14");
        Assert.True(w["raster"]!.AsArray().Count >= 6);
        var tag = w["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == k.Datum)!;
        var st = tag["stunden"]!.AsArray().First(x => x!["von"]!.GetValue<int>() == k.Von && x["unterricht"]!.GetValue<int>() == k.Unterricht)!;
        Assert.Equal("Seite 12", st["hausaufgaben"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal(1, st["eintraege"]!.GetValue<int>());
        Assert.Equal($"{beginn.AddMinutes(5):HH:mm}", tag["vermerke"]!.AsArray().Single()!["zeit"]!.GetValue<string>());
    }
}
