using System.Net;
using System.Net.Http.Json;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprechtag – Absagen: Eltern sagen ihren Termin ab (mit Grund, die Lehrkraft wird benachrichtigt), die Lehrkraft sagt einzelne Termine bzw. ihre
/// gesamte Teilnahme ab (betroffene Eltern werden benachrichtigt, keine Buchung mehr möglich, wieder aufnehmbar), die Administration sagt den
/// ganzen Sprechtag ab (alle Beteiligten werden benachrichtigt).
/// </summary>
public class SprechtagAbsageTests
{
    private static object Daten() => new
    {
        name = "Elternsprechtag Herbst", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, pausen = Array.Empty<object>(),
        anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", sichtbar = true, raumwahl = true, hinweis = "",
        lehrkraefte = new object[] { new { lehrer = "LAN", raum = "" }, new { lehrer = "FIS", raum = "" } },
    };

    private static async Task<(HttpClient C, int Id)> ElternAsync(Testserver s, HttpClient admin, string login, params int[] kinder)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "eltern-passwort", rolle = "eltern", anzeigename = login, gesperrt = false, schuelerIds = kinder }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "eltern-passwort" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    private static async Task<int> BuchenAsync(HttpClient eltern, int id, string lehrer, int kind, string beginn) =>
        (await Testserver.JsonAsync(await eltern.PostAsJsonAsync($"/api/app/sprechtag/{id}/buchen", new { lehrer, schuelerId = kind, beginn })))["id"]!.GetValue<int>();

    [Fact]
    public async Task Eltern_und_Lehrkraft_sagen_Termine_und_Teilnahme_ab()
    {
        await using var s = await Testserver.StartenAsync();
        var kind = s.Dienst<Paketdienst>().Aktuell!.Schueler.Where(x => x.Klasse == "7a").ToList();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var (eltern, elternId) = await ElternAsync(s, admin, "eltern-a", kind[0].Id);
        var (andere, andereId) = await ElternAsync(s, admin, "eltern-b", kind[1].Id);
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", Daten())))["id"]!.GetValue<int>();
        var m = s.Dienst<Mitteilungsdienst>();
        var bd = s.Dienst<Benutzerdienst>();
        int Lan() => bd.NachLogin("lan")!.Id;

        // Eltern sagen ihren Termin mit Grund ab → Lehrkraft erhält eine Mitteilung mit dem Grund
        var t1 = await BuchenAsync(eltern, id, "LAN", kind[0].Id, "15:00");
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync($"/api/app/sprechtag/{id}/termine/{t1}/absagen", new { grund = "Krank" }));
        var post = m.Posteingang(Lan()).Single();
        Assert.Contains("abgesagt", post.Betreff);
        Assert.Contains("Grund: Krank", post.Text);
        // fremde Eltern dürfen nicht absagen
        var t2 = await BuchenAsync(eltern, id, "LAN", kind[0].Id, "15:10");
        Assert.Equal(HttpStatusCode.Forbidden, (await andere.PostAsJsonAsync($"/api/app/sprechtag/{id}/termine/{t2}/absagen", new { grund = "" })).StatusCode);

        // Lehrkraft sagt einen einzelnen Termin ab → Eltern erhalten eine Mitteilung
        var vorher = m.Ungelesen(elternId);
        await Testserver.JsonAsync(await lan.PostAsJsonAsync($"/api/app/sprechtag/{id}/termine/{t2}/absagen", new { grund = "Fortbildung" }));
        Assert.Equal(vorher + 1, m.Ungelesen(elternId));

        // Lehrkraft sagt die gesamte Teilnahme ab: alle Termine entfallen, Eltern benachrichtigt, nicht mehr buchbar
        await BuchenAsync(eltern, id, "FIS", kind[0].Id, "15:00");
        await BuchenAsync(andere, id, "FIS", kind[1].Id, "15:20");
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/app/sprechtag/{id}/teilnahme/absagen", new { lehrer = "FIS", grund = "" })).StatusCode);
        var vorherA = m.Ungelesen(elternId);
        var vorherB = m.Ungelesen(andereId);
        var r = await Testserver.JsonAsync(await fis.PostAsJsonAsync($"/api/app/sprechtag/{id}/teilnahme/absagen", new { grund = "Dienstreise" }));
        Assert.Equal(2, r["termine"]!.GetValue<int>());
        Assert.Equal(vorherA + 1, m.Ungelesen(elternId));
        Assert.Equal(vorherB + 1, m.Ungelesen(andereId));
        // Administration wird informiert
        Assert.Contains(m.Posteingang(bd.NachLogin("admin")!.Id), x => x.Betreff.Contains("Teilnahme abgesagt"));
        var raster = await Testserver.HolenAsync(fis, $"/api/app/sprechtag/{id}/lehrkraft");
        Assert.False(string.IsNullOrEmpty(raster["lehrer"]!["abgesagt"]!.GetValue<string>()));
        Assert.All(raster["zeitfenster"]!.AsArray(), f => Assert.Equal("gesperrt", f!["status"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.BadRequest, (await eltern.PostAsJsonAsync($"/api/app/sprechtag/{id}/buchen", new { lehrer = "FIS", schuelerId = kind[0].Id, beginn = "16:00" })).StatusCode);
        var sicht = await Testserver.HolenAsync(eltern, $"/api/app/sprechtag/{id}/eltern");
        var lk = sicht["kinder"]![0]!["lehrkraefte"]!.AsArray().Single(x => x!["kurz"]!.GetValue<string>() == "FIS")!;
        Assert.True(lk["abgesagt"]!.GetValue<bool>());
        Assert.Empty(lk["zeitfenster"]!.AsArray());
        // doppelt absagen nicht möglich, wieder aufnehmen gibt die Fenster frei
        Assert.Equal(HttpStatusCode.BadRequest, (await fis.PostAsJsonAsync($"/api/app/sprechtag/{id}/teilnahme/absagen", new { grund = "" })).StatusCode);
        await Testserver.JsonAsync(await fis.PostAsJsonAsync($"/api/app/sprechtag/{id}/teilnahme/wiederaufnehmen", new { }));
        await BuchenAsync(eltern, id, "FIS", kind[0].Id, "16:00");
    }

    [Fact]
    public async Task Administration_sagt_den_ganzen_Sprechtag_ab()
    {
        await using var s = await Testserver.StartenAsync();
        var kind = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Klasse == "7a");
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var (eltern, elternId) = await ElternAsync(s, admin, "eltern-a", kind.Id);
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", Daten())))["id"]!.GetValue<int>();
        await BuchenAsync(eltern, id, "LAN", kind.Id, "15:00");
        await BuchenAsync(eltern, id, "FIS", kind.Id, "15:30");
        var m = s.Dienst<Mitteilungsdienst>();
        var bd = s.Dienst<Benutzerdienst>();

        // nur die Administration
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/app/sprechtag/{id}/absagen", new { grund = "" })).StatusCode);
        var vorher = m.Ungelesen(elternId);
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/absagen", new { grund = "Streik" }));
        Assert.Equal(2, r["termine"]!.GetValue<int>());
        Assert.Equal(vorher + 1, m.Ungelesen(elternId));
        Assert.Contains(m.Posteingang(bd.NachLogin("lan")!.Id), x => x.Betreff.StartsWith("Sprechtag abgesagt") && x.Text.Contains("Streik"));
        Assert.Contains(m.Posteingang(bd.NachLogin("fis")!.Id), x => x.Betreff.StartsWith("Sprechtag abgesagt"));

        var t = await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}");
        Assert.Equal("abgesagt", t["sprechtag"]!["phase"]!.GetValue<string>());
        Assert.Equal("Streik", t["sprechtag"]!["absageGrund"]!.GetValue<string>());
        Assert.Empty(s.Dienst<Sprechtagdienst>().Termine(id));
        // keine Buchungen, keine Änderungen, keine zweite Absage
        Assert.Equal(HttpStatusCode.BadRequest, (await eltern.PostAsJsonAsync($"/api/app/sprechtag/{id}/buchen", new { lehrer = "LAN", schuelerId = kind.Id, beginn = "16:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Daten())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/absagen", new { grund = "" })).StatusCode);
        // Startseite zeigt die Absage
        var karte = Assert.Single((await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray());
        Assert.Equal("abgesagt", karte!["phase"]!.GetValue<string>());
    }
}
