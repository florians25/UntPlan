using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Sofort-Benachrichtigungen der iOS-App: Gerätetokens, APNs-JWT, Änderungsermittlung nach dem Import, Versand.</summary>
public class PushTests
{
    private const string Token1 = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string Token2 = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

    /// <summary>Nimmt APNs-Anfragen entgegen (statt Apple) und antwortet mit dem vorgegebenen Status.</summary>
    private sealed class Apns : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Anfrage, string Inhalt)> Anfragen = new();
        public Func<HttpRequestMessage, HttpStatusCode> Status = _ => HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var text = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Anfragen) Anfragen.Add((request, text));
            var st = Status(request);
            return new HttpResponseMessage(st) { Content = new StringContent(st == HttpStatusCode.Gone ? "{\"reason\":\"Unregistered\"}" : "") };
        }
    }

    private static string TestSchluessel(out ECDsa oeffentlich)
    {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        oeffentlich = ECDsa.Create(ec.ExportParameters(false));
        return ec.ExportPkcs8PrivateKeyPem();
    }

    private static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }

    [Fact]
    public async Task Geraetetoken_speichern_und_loeschen()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/push/geraet", new { token = Token1, plattform = "ios", umgebung = "sandbox", bundle = "de.untplan.mobile" }));
        // erneut melden aktualisiert nur; dasselbe Gerät mit einem zweiten Konto
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/push/geraet", new { token = Token1, plattform = "ios", umgebung = "production", bundle = "de.untplan.mobile" }));
        await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/push/geraet", new { token = Token1, plattform = "ios", umgebung = "production" }));
        var push = s.Dienst<Pushdienst>();
        var alle = push.Geraete();
        Assert.Equal(2, alle.Count);
        Assert.All(alle, g => Assert.Equal("production", g.Umgebung));
        // ungültige Angaben, ohne Anmeldung
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PostAsJsonAsync("/api/app/push/geraet", new { token = Token2, umgebung = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PostAsJsonAsync("/api/app/push/geraet", new { token = "<script>", umgebung = "sandbox" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync("/api/app/push/geraet", new { token = Token2 })).StatusCode);
        // Abmelden betrifft nur das eigene Konto
        var r = await Testserver.JsonAsync(await lan.DeleteAsync($"/api/app/push/geraet?token={Token1}"));
        Assert.Equal(1, r["entfernt"]!.GetValue<int>());
        var rest = Assert.Single(push.Geraete());
        Assert.Equal(s.Dienst<Benutzerdienst>().NachLogin("sne")!.Id, rest.BenutzerId);
        // Konfiguration nur für die Administration
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.GetAsync("/api/app/admin/push")).StatusCode);
        var admin = await s.AlsAsync("admin");
        var k = await Testserver.HolenAsync(admin, "/api/app/admin/push");
        Assert.False(k["konfiguriert"]!.GetValue<bool>());
        Assert.Equal(Pushdienst.StandardTopic, k["topic"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/admin/push", new { schluessel = "kein schluessel", keyId = "K", teamId = "T" })).StatusCode);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/push", new { schluessel = TestSchluessel(out _), keyId = "ABC123DEFG", teamId = "TEAM123456" }));
        Assert.True((await Testserver.HolenAsync(admin, "/api/app/admin/push"))["konfiguriert"]!.GetValue<bool>());
    }

    [Fact]
    public void Jwt_hat_das_APNs_Format_und_eine_gueltige_ES256_Signatur()
    {
        var p8 = TestSchluessel(out var oeffentlich);
        var zeit = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var jwt = Pushdienst.Jwt(p8, "ABC123DEFG", "TEAM123456", zeit);
        var teile = jwt.Split('.');
        Assert.Equal(3, teile.Length);
        Assert.DoesNotContain('=', jwt);
        var kopf = JsonNode.Parse(B64Url(teile[0]))!;
        Assert.Equal("ES256", kopf["alg"]!.GetValue<string>());
        Assert.Equal("ABC123DEFG", kopf["kid"]!.GetValue<string>());
        var inhalt = JsonNode.Parse(B64Url(teile[1]))!;
        Assert.Equal("TEAM123456", inhalt["iss"]!.GetValue<string>());
        Assert.Equal(zeit.ToUnixTimeSeconds(), inhalt["iat"]!.GetValue<long>());
        var sig = B64Url(teile[2]);
        Assert.Equal(64, sig.Length);   // r‖s (IEEE P1363), nicht DER
        Assert.True(oeffentlich.VerifyData(Encoding.ASCII.GetBytes($"{teile[0]}.{teile[1]}"), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        // .p8-Inhalt ohne Kopf-/Fußzeile wird ebenfalls gelesen
        var nackt = string.Join("", p8.Split('\n').Where(z => !z.StartsWith("-----")));
        Assert.Equal(3, Pushdienst.Jwt(nackt, "K", "T", zeit).Split('.').Length);
    }

    /// <summary>Beispielpaket mit nächster Revision; am Do 15.10. entfällt die erste Stunde der Lehrkraft.</summary>
    private static (JsonObject Paket, string Lehrer, int Stunde, string Fach, string Klasse) MitEntfall(int revision)
    {
        var p = Testserver.Beispielpaket();
        p["revision"] = revision;
        var tag = p["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-15")!;
        var st = tag["stunden"]!.AsArray().First(x => x!["status"]!.GetValue<string>() == "regulaer" && x["lehrer"]!.AsArray().Count == 1 && x["klassen"]!.AsArray().Count == 1)!;
        st["status"] = "entfall";
        return (p, st["lehrer"]![0]!.GetValue<string>(), st["stunde"]!.GetValue<int>(), st["fach"]!.GetValue<string>(), st["klassen"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Aenderungen_nach_dem_Import_werden_je_Lehrkraft_ermittelt_und_gesendet()
    {
        await using var s = await Testserver.StartenAsync();
        var push = s.Dienst<Pushdienst>();
        var pakete = s.Dienst<Paketdienst>();
        var (paket, lehrer, stunde, fach, klasse) = MitEntfall(2);
        var andere = pakete.Aktuell!.Paket.Elemente.Lehrer.Select(x => x.Kurz).First(x => x != lehrer);

        // Änderungsermittlung: nur die betroffene Lehrkraft, genau diese Stunde
        var alt = pakete.Aktuell!;
        var neu = new Stand(System.Text.Json.JsonSerializer.Deserialize<UntPlan.Web.Contracts.WebPaket>(paket.ToJsonString(), UntPlan.Web.Contracts.UntPlanWebSchema.Json)!, "");
        var aenderungen = push.Aenderungen(alt, neu, new[] { lehrer, andere }, new DateOnly(2026, 10, 14));
        var liste = Assert.Single(aenderungen).Value;
        var a = Assert.Single(liste, x => x.Datum == "2026-10-15" && x.Von <= stunde && x.Bis >= stunde);
        Assert.Equal("entfall", a.Status);
        Assert.Empty(push.Aenderungen(alt, alt, new[] { lehrer }, new DateOnly(2026, 10, 14)));
        Assert.Empty(push.Aenderungen(null, neu, new[] { lehrer }, new DateOnly(2026, 10, 14)));
        var (titel, text) = Pushdienst.Nachricht(liste);
        Assert.Equal("Änderung im Stundenplan", titel);
        Assert.StartsWith($"Do {stunde}.", text);
        Assert.Contains($"{fach} {klasse} entfällt", text);
        var viele = Enumerable.Range(1, 5).Select(i => new PlanAenderung("2026-10-16", i, i, "D", "7a", "geaendert")).ToList();
        Assert.Equal(("Änderungen im Stundenplan", "5 Änderungen ab Fr 16.10. – bitte den Stundenplan prüfen."), Pushdienst.Nachricht(viele));

        // Versand nach dem Import (APNs simuliert), ungültige Tokens werden entfernt
        var apns = new Apns { Status = r => r.RequestUri!.AbsolutePath.EndsWith(Token2) ? HttpStatusCode.Gone : HttpStatusCode.OK };
        push.Handler = apns;
        push.KonfigurationSpeichern(TestSchluessel(out _), "ABC123DEFG", "TEAM123456", null);
        var bd = s.Dienst<Benutzerdienst>();
        await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        await s.AlsAsync(andere.ToLowerInvariant(), "lehrer", andere);
        push.GeraetSpeichern(bd.NachLogin(lehrer.ToLowerInvariant())!.Id, Token1, "ios", "sandbox", "");
        push.GeraetSpeichern(bd.NachLogin(lehrer.ToLowerInvariant())!.Id, Token2, "ios", "production", "");
        push.GeraetSpeichern(bd.NachLogin(andere.ToLowerInvariant())!.Id, "0011223344556677", "ios", "production", "");
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        await push.LetzterVersand;
        Assert.Equal(2, apns.Anfragen.Count);                      // nur die Geräte der betroffenen Lehrkraft
        var (anfrage, inhalt) = apns.Anfragen.Single(x => x.Anfrage.RequestUri!.Host == "api.sandbox.push.apple.com");
        Assert.Equal($"/3/device/{Token1}", anfrage.RequestUri!.AbsolutePath);
        Assert.Equal(new Version(2, 0), anfrage.Version);
        Assert.Equal("bearer", anfrage.Headers.Authorization!.Scheme);
        Assert.Equal(3, anfrage.Headers.Authorization.Parameter!.Split('.').Length);
        Assert.Equal(Pushdienst.StandardTopic, anfrage.Headers.GetValues("apns-topic").Single());
        Assert.Equal("alert", anfrage.Headers.GetValues("apns-push-type").Single());
        var json = JsonNode.Parse(inhalt)!;
        Assert.Equal("2026-10-15", json["datum"]!.GetValue<string>());
        Assert.Equal("Änderung im Stundenplan", json["aps"]!["alert"]!["title"]!.GetValue<string>());
        Assert.Contains(apns.Anfragen, x => x.Anfrage.RequestUri!.Host == "api.push.apple.com");
        Assert.DoesNotContain(push.Geraete(), g => g.Token == Token2);   // 410 → entfernt
        Assert.Contains(push.Geraete(), g => g.Token == Token1);
    }

    [Fact]
    public async Task Ohne_Konfiguration_wird_nichts_gesendet()
    {
        await using var s = await Testserver.StartenAsync();
        var push = s.Dienst<Pushdienst>();
        var apns = new Apns();
        push.Handler = apns;
        var (paket, lehrer, _, _, _) = MitEntfall(2);
        await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var id = s.Dienst<Benutzerdienst>().NachLogin(lehrer.ToLowerInvariant())!.Id;
        push.GeraetSpeichern(id, Token1, "ios", "production", "");
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        await push.LetzterVersand;
        Assert.Equal(0, await push.SendenAsync(new[] { id }, "Test", "Test", "2026-10-15"));
        // unvollständig (ohne Team-ID): ebenfalls nichts
        push.KonfigurationSpeichern(TestSchluessel(out _), "ABC123DEFG", "", null);
        Assert.Equal(0, await push.SendenAsync(new[] { id }, "Test", "Test", "2026-10-15"));
        Assert.Empty(apns.Anfragen);
    }
}
