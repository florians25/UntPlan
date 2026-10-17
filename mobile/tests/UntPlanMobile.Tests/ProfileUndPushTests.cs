using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>Mehrere Profile je Gerät, Web-Push bei Planänderungen und der Sprechtag über UntPlanMobile.</summary>
public class ProfileUndPushTests
{
    private static Task<HttpResponseMessage> Formular(HttpClient c, string pfad, params (string, string)[] felder) =>
        c.PostAsync(pfad, new FormUrlEncodedContent(felder.ToDictionary(f => f.Item1, f => f.Item2)));

    private static async Task<HttpClient> AnmeldenAsync(HttpClient c, string login)
    {
        var r = await Formular(c, "/api/login", ("action", "login"), ("username", login), ("password", Umgebung.Passwort));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    private static async Task<string> EigenAsync(HttpClient c) => (await Umgebung.HolenAsync(c, "/api/mobile?action=meta"))["own"]!["label"]!.GetValue<string>();

    [Fact]
    public async Task Mehrere_Profile_wechseln_ohne_Abmelden()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = u.Client();
        await AnmeldenAsync(c, "sne");
        var liste = (await Umgebung.HolenAsync(c, "/api/accounts?action=list"))["accounts"]!.AsArray();
        var sne = Assert.Single(liste)!;
        Assert.True(sne["current"]!.GetValue<bool>());
        Assert.Equal("Julia Schneider", sne["label"]!.GetValue<string>());

        // weiteres Profil anmelden – das erste bleibt hinterlegt
        await AnmeldenAsync(c, "fis");
        Assert.Equal("FIS", await EigenAsync(c));
        liste = (await Umgebung.HolenAsync(c, "/api/accounts?action=list"))["accounts"]!.AsArray();
        Assert.Equal(2, liste.Count);
        Assert.Equal("fis", liste.Single(a => a!["current"]!.GetValue<bool>())!["info"]!.GetValue<string>().Split(" · ").Last());

        // Wechsel ohne Passwort
        var tokenSne = liste.Single(a => a!["label"]!.GetValue<string>() == "Julia Schneider")!["token"]!.GetValue<string>();
        Assert.True((await Umgebung.SendenAsync(c, "/api/accounts", new() { ["action"] = "switch", ["token"] = tokenSne }))["ok"]!.GetValue<bool>());
        Assert.Equal("SNE", await EigenAsync(c));

        // ein anderes Gerät kennt die Profile nicht
        var fremd = await u.AngemeldetAsync("5a");
        Assert.Single((await Umgebung.HolenAsync(fremd, "/api/accounts?action=list"))["accounts"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Formular(fremd, "/api/accounts", ("action", "switch"), ("token", tokenSne))).StatusCode);

        // Abmelden meldet nur das aktuelle Profil ab, das andere übernimmt
        var ab = await Umgebung.SendenAsync(c, "/api/login", new() { ["action"] = "logout" });
        Assert.True(ab["switched"]!.GetValue<bool>());
        Assert.Equal("FIS", await EigenAsync(c));
        Assert.Single((await Umgebung.HolenAsync(c, "/api/accounts?action=list"))["accounts"]!.AsArray());

        // Entfernen des letzten Profils meldet ab
        var letztes = (await Umgebung.HolenAsync(c, "/api/accounts?action=list"))["accounts"]!.AsArray().Single()!["token"]!.GetValue<string>();
        var weg = await Umgebung.SendenAsync(c, "/api/accounts", new() { ["action"] = "remove", ["token"] = letztes });
        Assert.True(weg["loggedOut"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/mobile?action=meta")).StatusCode);
    }

    /// <summary>Push-Dienst im Testprozess: nimmt verschlüsselte Nachrichten an und merkt sich Kopf und Inhalt.</summary>
    private sealed class PushDienst : IAsyncDisposable
    {
        public readonly List<(string Auth, string Kodierung, byte[] Inhalt)> Eingang = new();
        private readonly WebApplication _app;
        public string Adresse { get; }

        private PushDienst(WebApplication app) { _app = app; Adresse = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First(); }

        public static async Task<PushDienst> StartenAsync()
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            var app = b.Build();
            var dienst = (PushDienst?)null;
            app.MapPost("/push/{id}", async (string id, HttpContext c) =>
            {
                using var ms = new MemoryStream();
                await c.Request.Body.CopyToAsync(ms);
                lock (dienst!.Eingang) dienst.Eingang.Add((c.Request.Headers.Authorization.ToString(), c.Request.Headers.ContentEncoding.ToString(), ms.ToArray()));
                return id == "weg" ? Results.StatusCode(410) : Results.StatusCode(201);
            });
            await app.StartAsync();
            dienst = new PushDienst(app);
            return dienst;
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    [Fact]
    public async Task Push_nach_Planaenderung_verschluesselt_an_das_Geraet()
    {
        await using var u = await Umgebung.StartenAsync();
        await using var dienst = await PushDienst.StartenAsync();
        var sne = await u.AngemeldetAsync("sne");

        var status = await Umgebung.HolenAsync(sne, "/api/push?action=status");
        var vapid = status["key"]!.GetValue<string>();
        Assert.Equal(65, WebPush.AusB64(vapid).Length);
        Assert.False(status["on"]!.GetValue<bool>());

        // Abo wie vom Browser (pushManager.subscribe)
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        var abo = new JsonObject { ["endpoint"] = "https://push.example.org/abo/1", ["keys"] = new JsonObject { ["p256dh"] = WebPush.B64(WebPush.Punkt(ua.ExportParameters(false))), ["auth"] = WebPush.B64(auth) } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Formular(sne, "/api/push", ("action", "subscribe"), ("subscription", "{\"endpoint\":\"http://intern/\"}"))).StatusCode);
        Assert.True((await Umgebung.SendenAsync(sne, "/api/push", new() { ["action"] = "subscribe", ["subscription"] = abo.ToJsonString() }))["on"]!.GetValue<bool>());
        Assert.True((await Umgebung.HolenAsync(sne, "/api/push?action=status"))["on"]!.GetValue<bool>());

        // Für den Test zeigt das Abo auf den Push-Dienst im Testprozess (die API nimmt nur https-Adressen an)
        var speicher = u.MobilDienste.GetRequiredService<Kontenspeicher>();
        var (geraet, _, _, _) = Assert.Single(speicher.PushKonten());
        speicher.AboSetzen(geraet, new PushAbo(dienst.Adresse + "/push/1", abo["keys"]!["p256dh"]!.GetValue<string>(), WebPush.B64(auth)));
        speicher.AboSetzen(geraet, new PushAbo(dienst.Adresse + "/push/weg", abo["keys"]!["p256dh"]!.GetValue<string>(), WebPush.B64(auth)));

        var wache = u.MobilDienste.GetRequiredService<Pushwache>();
        // erste Runde: Grundstand merken, nichts senden
        Assert.Equal(0, await wache.RundeAsync(true, default));
        Assert.Empty(dienst.Eingang);
        // keine Änderung: nichts
        Assert.Equal(0, await wache.RundeAsync(true, default));

        // Planänderung: Raumbuchung am Mittwoch, 1. Stunde
        var frei = await Umgebung.HolenAsync(sne, "/api/rooms?action=free&date=2026-10-14&from=1&to=1");
        var raum = frei["rooms"]!.AsArray().First()!["code"]!.GetValue<string>();
        await Umgebung.SendenAsync(sne, "/api/rooms", new() { ["action"] = "book", ["date"] = "2026-10-14", ["from"] = "1", ["to"] = "1", ["room"] = raum, ["class"] = "5a", ["subject"] = "E" });
        Assert.Equal(1, await wache.RundeAsync(true, default));
        Assert.Equal(2, dienst.Eingang.Count);
        var (kopf, kodierung, inhalt) = dienst.Eingang.First(e => true);
        Assert.Equal("aes128gcm", kodierung);
        Assert.StartsWith("vapid t=", kopf);
        Assert.Contains("k=" + vapid, kopf);
        var nachricht = JsonNode.Parse(Encoding.UTF8.GetString(WebPush.Entschluesseln(inhalt, ua, auth)))!;
        Assert.Equal("Änderung im Stundenplan", nachricht["title"]!.GetValue<string>());
        Assert.Contains("Mi. 14.10. 1. Std.", nachricht["body"]!.GetValue<string>());
        Assert.Contains(raum, nachricht["body"]!.GetValue<string>());
        // abgelaufenes Abo (410) wurde entfernt
        Assert.DoesNotContain(speicher.Abos(geraet), a => a.Endpoint.EndsWith("/weg"));

        // ausschalten: keine Nachrichten mehr
        await Umgebung.SendenAsync(sne, "/api/push", new() { ["action"] = "off" });
        Assert.Empty(speicher.PushKonten());
    }

    [Fact]
    public async Task Sprechtag_ueber_UntPlanMobile_buchen_und_absagen()
    {
        await using var u = await Umgebung.StartenAsync();
        using var admin = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = u.WebAdresse };
        admin.DefaultRequestHeaders.Add("X-UntPlanWeb", "1");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = "admin-passwort" })).StatusCode);
        var schueler = (await Umgebung.HolenAsync(admin, "/api/app/klassenbuch/schueler?klasse=5a"));
        var liste = schueler is JsonArray a ? a : schueler["schueler"]!.AsArray();
        var kind = liste.First()!["id"]!.GetValue<int>();
        await Umgebung.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login = "eltern", passwort = Umgebung.Passwort, rolle = "eltern", anzeigename = "Familie Test", gesperrt = false, schuelerIds = new[] { kind } }));
        var tag = await Umgebung.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new
        {
            name = "Elternsprechtag", datum = "2026-10-21", beginn = "15:00", ende = "16:00", dauer = 15, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00",
            sichtbar = true, raumwahl = true, lehrkraefte = new[] { new { lehrer = "FIS", raum = "R101" }, new { lehrer = "SNE", raum = "" } },
        }));
        var id = tag["id"]!.GetValue<int>();

        // Eltern: Startkarte, Lehrkräfte des Kindes mit freien Zeitfenstern, buchen
        var eltern = await u.AngemeldetAsync("eltern");
        var start = (await Umgebung.HolenAsync(eltern, "/api/sprechtag/start")).AsArray();
        Assert.Equal("anmeldung", Assert.Single(start)!["phase"]!.GetValue<string>());
        var e = await Umgebung.HolenAsync(eltern, $"/api/sprechtag/{id}/eltern");
        var fis = e["kinder"]![0]!["lehrkraefte"]!.AsArray().First(l => l!["kurz"]!.GetValue<string>() == "FIS")!;
        var fenster = fis["zeitfenster"]!.AsArray().First()!["beginn"]!.GetValue<string>();
        var r = await eltern.PostAsync($"/api/sprechtag/{id}/buchen", JsonContent.Create(new { lehrer = "FIS", schuelerId = kind, beginn = fenster }));
        var termin = await Umgebung.JsonAsync(r);
        Assert.Equal("FIS", termin["lehrer"]!.GetValue<string>());

        // Lehrkraft: Zeitraster mit gebuchtem Termin, Zeitfenster sperren
        var lk = await u.AngemeldetAsync("fis");
        var raster = await Umgebung.HolenAsync(lk, $"/api/sprechtag/{id}/lehrkraft");
        Assert.Equal("gebucht", raster["zeitfenster"]!.AsArray().First(z => z!["beginn"]!.GetValue<string>() == fenster)!["status"]!.GetValue<string>());
        var frei = raster["zeitfenster"]!.AsArray().First(z => z!["status"]!.GetValue<string>() == "frei")!["beginn"]!.GetValue<string>();
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/sprechtag/{id}/sperren") { Content = JsonContent.Create(new { beginne = new[] { frei }, gesperrt = true }) };
        await Umgebung.JsonAsync(await lk.SendAsync(req));
        raster = await Umgebung.HolenAsync(lk, $"/api/sprechtag/{id}/lehrkraft");
        Assert.Equal("gesperrt", raster["zeitfenster"]!.AsArray().First(z => z!["beginn"]!.GetValue<string>() == frei)!["status"]!.GetValue<string>());

        // Lehrkraft sagt den Termin ab – die Eltern sehen keinen Termin mehr
        var tid = termin["id"]!.GetValue<int>();
        await Umgebung.JsonAsync(await lk.PostAsync($"/api/sprechtag/{id}/termine/{tid}/absagen", JsonContent.Create(new { grund = "Krankheit" })));
        Assert.Empty((await Umgebung.HolenAsync(eltern, $"/api/sprechtag/{id}/eltern"))["termine"]!.AsArray());

        // Schülerkonten lesen höchstens (Sprint 27: wenn der Sprechtag für Schüler*innen sichtbar ist), buchen nie;
        // fremde Pfade gibt es nicht
        var klasse = await u.AngemeldetAsync("5a");
        var lesen = await klasse.GetAsync($"/api/sprechtag/{id}/eltern");
        if (lesen.IsSuccessStatusCode) Assert.False((await Umgebung.JsonAsync(lesen))["buchbar"]!.GetValue<bool>());
        Assert.False((await klasse.PostAsync($"/api/sprechtag/{id}/buchen", JsonContent.Create(new { lehrer = "X", schuelerId = 1, beginn = "15:00" }))).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lk.GetAsync("/api/sprechtag/..%2Fadmin")).StatusCode);
    }
}
