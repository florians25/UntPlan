using System.Net;
using System.Net.Http.Json;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Raumsperren (Administration → Raumsperren) und „Buchungen ent-/sperren“: kontinuierliche bzw. regelmäßige Sperren für einzelne Räume,
/// Raumgruppen, alle Räume und Ressourcen – gesperrte Räume sind bei der Suche belegt, Buchungen werden abgelehnt, der Raumplan zeigt „Gesperrt“.
/// </summary>
public class RaumsperrenTests
{
    private static Raumsperre Sperre(string von, string bis, string beginn, string ende, bool kont, int tag = -1) =>
        new(1, Raumsperrdienst.Raum, "TH1", von, bis, beginn, ende, kont, tag, "", "", "");

    [Fact]
    public void Kontinuierlich_und_regelmaessig()
    {
        // kontinuierlich: Mo 08:00 bis Mi 12:00 ohne Unterbrechung
        var k = Sperre("2026-11-16", "2026-11-18", "08:00", "12:00", true);
        Assert.False(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 16), "07:00", "07:45"));
        Assert.True(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 16), "13:00", "13:45"));
        Assert.True(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 17), "07:00", "07:45"));
        Assert.True(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 18), "11:30", "12:15"));
        Assert.False(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 18), "12:00", "12:45"));
        Assert.False(Raumsperrdienst.Gilt(k, new DateOnly(2026, 11, 19), "08:00", "08:45"));
        // regelmäßig: nur donnerstags 13:00–15:00 im Datumsbereich
        var r = Sperre("2026-11-01", "2026-12-31", "13:00", "15:00", false, 3);
        Assert.True(Raumsperrdienst.Gilt(r, new DateOnly(2026, 11, 19), "13:15", "14:00"));
        Assert.False(Raumsperrdienst.Gilt(r, new DateOnly(2026, 11, 19), "11:00", "11:45"));
        Assert.False(Raumsperrdienst.Gilt(r, new DateOnly(2026, 11, 18), "13:15", "14:00"));
        // jeden Tag
        var t = Sperre("2026-11-01", "2026-12-31", "13:00", "15:00", false);
        Assert.True(Raumsperrdienst.Gilt(t, new DateOnly(2026, 11, 18), "13:15", "14:00"));
        // Raumgruppe und alle Räume
        Assert.True(Raumsperrdienst.BetrifftRaum(new Raumsperre(1, Raumsperrdienst.Raumgruppe, "Sport", "", "", "", "", true, -1, "", "", ""), "TH2", "Sport"));
        Assert.False(Raumsperrdienst.BetrifftRaum(new Raumsperre(1, Raumsperrdienst.Raumgruppe, "Sport", "", "", "", "", true, -1, "", "", ""), "R101", ""));
        Assert.True(Raumsperrdienst.BetrifftRaum(new Raumsperre(1, Raumsperrdienst.AlleRaeume, "", "", "", "", "", true, -1, "", "", ""), "R101", ""));
        Assert.True(Raumsperrdienst.BetrifftRessource(new Raumsperre(1, Raumsperrdienst.Ressource, "7", "", "", "", "", true, -1, "", "", ""), 7));
        Assert.True(Raumsperrdienst.BetrifftRessource(new Raumsperre(1, Raumsperrdienst.AlleRessourcen, "", "", "", "", "", true, -1, "", "", ""), 3));
    }

    [Fact]
    public async Task Gesperrter_Raum_ist_belegt_und_nicht_buchbar_Raumplan_zeigt_Gesperrt()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var raum = s.Dienst<Buchungsdienst>().Raeume(stand).First(r => r.Buchbar).Kurz;
        // Donnerstag 15.10.2026 (Testuhr 14.10.), ganztägig nur donnerstags
        var tag = "2026-10-15";
        await FehlerAsync(await lan.PostAsJsonAsync("/api/app/raumsperren", new { art = "raum", ziel = raum, von = tag, bis = tag }), HttpStatusCode.Forbidden);
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/raumsperren", new { art = "raum", ziel = "", von = tag, bis = tag }));
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/raumsperren", new { art = "raum", ziel = raum, von = tag, bis = "2026-10-14" }));
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/raumsperren",
            new { art = "raum", ziel = raum, von = tag, bis = "2026-10-16", beginn = "00:00", ende = "23:59", kontinuierlich = false, wochentag = 3, text = "Andere Schule" })))["id"]!.GetValue<int>();
        var liste = await Testserver.HolenAsync(admin, "/api/app/raumsperren");
        Assert.Single(liste["sperren"]!.AsArray());
        Assert.Contains("Donnerstags", liste["sperren"]![0]!["beschreibung"]!.GetValue<string>());

        // Suche: am Mittwoch nicht frei, am Donnerstag frei
        async Task<bool> Frei(string datum) =>
            (await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = new { wiederholung = "einmalig", datum, stundeVon = 2, stundeBis = 2 }, kriterien = new { maxBelegung = 0 } })))
                ["raeume"]!.AsArray().Any(r => r!["kurz"]!.GetValue<string>() == raum);
        Assert.False(await Frei(tag));
        Assert.True(await Frei("2026-10-16"));
        // Buchung wird abgelehnt
        Assert.Contains("Gesperrt", await FehlerAsync(await lan.PostAsJsonAsync("/api/app/buchen",
            new { art = "raum", termine = new[] { new { datum = tag, stundeVon = 2, stundeBis = 2, raum } }, titel = "Test" })));
        // Raumplan: Block „Gesperrt“ mit dem Text der Sperre
        var plan = await Testserver.HolenAsync(lan, $"/api/app/plan?typ=raum&element={raum}&datum={tag}");
        var gesperrt = plan["karten"]!.AsArray().Where(k => k!["status"]!.GetValue<string>() == "gesperrt").ToList();
        Assert.Single(gesperrt.Where(k => k!["datum"]!.GetValue<string>() == tag));
        Assert.Equal("Andere Schule", gesperrt[0]!["text"]!.GetValue<string>());

        // Sperre löschen: wieder buchbar
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/app/raumsperren/{id}")).StatusCode);
        Assert.True(await Frei(tag));

        // Buchungen ent-/sperren: Lehrkräfte können nicht mehr buchen, die Administration schon
        Assert.True((await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/raumsperren/buchungen", new { gesperrt = true })))["gesperrt"]!.GetValue<bool>());
        Assert.True((await Testserver.HolenAsync(lan, "/api/app/buchen/optionen"))["buchungenGesperrt"]!.GetValue<bool>());
        Assert.Contains("gesperrt", await FehlerAsync(await lan.PostAsJsonAsync("/api/app/buchen",
            new { art = "raum", termine = new[] { new { datum = tag, stundeVon = 2, stundeBis = 2, raum } }, titel = "Test" })));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen",
            new { art = "raum", termine = new[] { new { datum = tag, stundeVon = 2, stundeBis = 2, raum } }, titel = "Admin" }));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/raumsperren/buchungen", new { gesperrt = false }));
        Assert.False((await Testserver.HolenAsync(admin, "/api/app/raumsperren/buchungen"))["gesperrt"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Gesperrte_Ressource_ist_nicht_verfuegbar()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var res = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Beamer 1", anzahl = 2, aktiv = true })))["id"]!.GetValue<int>();
        var tag = "2026-10-15";
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/raumsperren", new { art = "alle_ressourcen", von = tag, bis = tag }));
        Assert.Contains("nicht", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/buchen",
            new { art = "ressource", termine = new[] { new { datum = tag, stundeVon = 2, stundeBis = 2 } }, ressourcen = new[] { new { id = res, menge = 1 } }, titel = "Test" })));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen",
            new { art = "ressource", termine = new[] { new { datum = "2026-10-16", stundeVon = 2, stundeBis = 2 } }, ressourcen = new[] { new { id = res, menge = 1 } }, titel = "Test" }));
    }

    private static async Task<string> FehlerAsync(HttpResponseMessage r, HttpStatusCode erwartet = HttpStatusCode.BadRequest)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(erwartet == r.StatusCode, $"erwartet {erwartet}, erhalten {(int)r.StatusCode}: {text}");
        return text;
    }
}
