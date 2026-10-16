using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Mitteilungen wie im Original: Empfängergruppen (Erziehungsberechtigte bzw. Schüler*innen einer Klasse, Klassenlehrkräfte, Lehrkräfte der Klasse),
/// Lesebestätigung (anfordern, senden, Liste), Antworten/Allen antworten/Weiterleiten mit Anhängen, Suche, Rechte und Sperren laut Einstellungen
/// der Administration, Grenzen für Anhänge und automatisches Löschen nach der Aufbewahrungsfrist.
/// </summary>
public class MitteilungenTests
{
    private static async Task<(HttpClient C, int Id)> ElternAsync(Testserver s, HttpClient admin, string login, params int[] kinder)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "eltern-passwort", rolle = "eltern", anzeigename = login, gesperrt = false, schuelerIds = kinder }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "eltern-passwort" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    private static int Id(Testserver s, string login) => s.Dienst<Benutzerdienst>().NachLogin(login)!.Id;

    private static Task<HttpResponseMessage> Senden(HttpClient c, object daten) => c.PostAsJsonAsync("/api/app/mitteilungen", daten);

    private static async Task Status(HttpResponseMessage r, HttpStatusCode erwartet)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(erwartet == r.StatusCode, $"erwartet {erwartet}, erhalten {(int)r.StatusCode}: {text}");
    }

    private static async Task<JsonObject> EinstellungenAsync(HttpClient admin) => (await Testserver.HolenAsync(admin, "/api/app/mitteilungen/einstellungen"))["einstellungen"]!.AsObject();

    private static async Task EinstellenAsync(HttpClient admin, Action<JsonObject> aendern)
    {
        var e = await EinstellungenAsync(admin);
        aendern(e);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/mitteilungen/einstellungen", e));
    }

    [Fact]
    public async Task Lesebestaetigung_anfordern_senden_und_Liste()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var bec = await s.AlsAsync("bec", "lehrer", "BEC");
        var id = (await Testserver.JsonAsync(await Senden(lan, new { empfaenger = new[] { "benutzer:" + Id(s, "fis"), "benutzer:" + Id(s, "bec") }, betreff = "Konferenz", text = "Bitte bestätigen", wichtig = false, lesebestaetigung = true })))["id"]!.GetValue<int>();

        var eingang = (await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray().Single()!;
        Assert.True(eingang["lesebestaetigung"]!.GetValue<bool>());
        Assert.Null(eingang["bestaetigt"]);
        var gesendet = (await Testserver.HolenAsync(lan, "/api/app/mitteilungen?ordner=gesendet")).AsArray().Single()!;
        Assert.Equal(2, gesendet["anzahl"]!.GetValue<int>());
        Assert.Equal(0, gesendet["bestaetigtAnzahl"]!.GetValue<int>());

        await Testserver.JsonAsync(await fis.PostAsync($"/api/app/mitteilungen/{id}/bestaetigen", null));
        Assert.NotNull((await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray().Single()!["bestaetigt"]);
        var liste = await Testserver.HolenAsync(lan, $"/api/app/mitteilungen/{id}/lesebestaetigungen");
        Assert.Equal(2, liste["anzahl"]!.GetValue<int>());
        Assert.Equal(1, liste["bestaetigt"]!.GetValue<int>());
        Assert.Equal("FIS", liste["empfaenger"]![0]!["name"]!.GetValue<string>()); // bestätigte zuerst
        gesendet = (await Testserver.HolenAsync(lan, "/api/app/mitteilungen?ordner=gesendet")).AsArray().Single()!;
        Assert.Equal(1, gesendet["bestaetigtAnzahl"]!.GetValue<int>());
        Assert.Equal(1, gesendet["gelesenAnzahl"]!.GetValue<int>());

        // nur der Absender sieht die Liste; ohne Anforderung keine Bestätigung
        await Status(await bec.GetAsync($"/api/app/mitteilungen/{id}/lesebestaetigungen"), HttpStatusCode.Forbidden);
        var ohne = (await Testserver.JsonAsync(await Senden(lan, new { empfaenger = new[] { "benutzer:" + Id(s, "fis") }, betreff = "Info", text = "ohne", wichtig = false })))["id"]!.GetValue<int>();
        await Status(await fis.PostAsync($"/api/app/mitteilungen/{ohne}/bestaetigen", null), HttpStatusCode.BadRequest);
        // Entwurf mit Lesebestätigung behält die Anforderung
        var e = (await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new { empfaenger = new[] { "benutzer:" + Id(s, "bec") }, betreff = "E", text = "T", wichtig = false, lesebestaetigung = true })))["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await lan.PostAsync($"/api/app/mitteilungen/{e}/senden", new StringContent("{}", Encoding.UTF8, "application/json")));
        Assert.Contains((await Testserver.HolenAsync(bec, "/api/app/mitteilungen")).AsArray(), m => m!["betreff"]!.GetValue<string>() == "E" && m["lesebestaetigung"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Empfaengergruppen_Eltern_Schueler_Klassenlehrkraefte()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var kind7 = stand.Schueler.First(x => x.Klasse == "7a");
        var kind5 = stand.Schueler.First(x => x.Klasse == "5a");
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var bec = await s.AlsAsync("bec", "lehrer", "BEC");
        var (eltern7, _) = await ElternAsync(s, admin, "eltern-7", kind7.Id);
        var (eltern5, _) = await ElternAsync(s, admin, "eltern-5", kind5.Id);
        var klasse7 = await s.AlsAsync("klasse-7a", "schueler", "7a");

        var liste = (await Testserver.HolenAsync(lan, "/api/app/mitteilungen/empfaenger")).AsArray();
        Assert.Contains(liste, x => x!["id"]!.GetValue<string>() == "eltern:7a" && x["kategorie"]!.GetValue<string>() == "eltern");
        Assert.Contains(liste, x => x!["id"]!.GetValue<string>() == "schueler:7a" && x["kategorie"]!.GetValue<string>() == "schueler");
        Assert.Contains(liste, x => x!["id"]!.GetValue<string>() == "gruppe:klassenleitungen");
        // Elternkonto mit Kind in der Auswahl
        Assert.Contains(liste, x => x!["kategorie"]!.GetValue<string>() == "eltern" && x["name"]!.GetValue<string>().Contains(kind7.Vorname));

        await Testserver.JsonAsync(await Senden(lan, new { empfaenger = new[] { "eltern:7a" }, betreff = "Elternbrief", text = "Wandertag", wichtig = true }));
        var beiEltern = (await Testserver.HolenAsync(eltern7, "/api/app/mitteilungen")).AsArray().Single()!;
        // Elternkonten sehen die übrigen Empfänger*innen (andere Eltern) nicht
        Assert.Empty(beiEltern["empfaenger"]!.AsArray());
        Assert.Equal("", beiEltern["empfaengerText"]!.GetValue<string>());
        Assert.Empty((await Testserver.HolenAsync(eltern5, "/api/app/mitteilungen")).AsArray());
        Assert.Empty((await Testserver.HolenAsync(klasse7, "/api/app/mitteilungen")).AsArray());
        var gesendet = (await Testserver.HolenAsync(lan, "/api/app/mitteilungen?ordner=gesendet")).AsArray().Single()!;
        Assert.Equal("Erziehungsberechtigte der Klasse 7a", gesendet["empfaengerText"]!.GetValue<string>());

        await Testserver.JsonAsync(await Senden(lan, new { empfaenger = new[] { "schueler:7a" }, betreff = "An die Klasse", text = "Hallo", wichtig = false }));
        Assert.Single((await Testserver.HolenAsync(klasse7, "/api/app/mitteilungen")).AsArray());

        // Lehrkräfte der Klasse 7a: FIS (unterrichtet), nicht BEC
        await Testserver.JsonAsync(await Senden(admin, new { empfaenger = new[] { "lehrer:7a" }, betreff = "Zeugniskonferenz 7a", text = "Termin", wichtig = false }));
        Assert.Single((await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray());
        Assert.Empty((await Testserver.HolenAsync(bec, "/api/app/mitteilungen")).AsArray());
        // Klassenlehrkräfte: LAN (Klassenleitung 7a)
        await Testserver.JsonAsync(await Senden(admin, new { empfaenger = new[] { "gruppe:klassenleitungen" }, betreff = "KL-Runde", text = "Termin", wichtig = false }));
        Assert.Contains((await Testserver.HolenAsync(lan, "/api/app/mitteilungen")).AsArray(), m => m!["betreff"]!.GetValue<string>() == "KL-Runde");

        // Suche (Server)
        Assert.Single((await Testserver.HolenAsync(lan, "/api/app/mitteilungen?ordner=gesendet&suche=wandertag")).AsArray());
        Assert.Empty((await Testserver.HolenAsync(lan, "/api/app/mitteilungen?ordner=gesendet&suche=gibtsnicht")).AsArray());
    }

    [Fact]
    public async Task Rechte_Einstellungen_und_Sperren()
    {
        await using var s = await Testserver.StartenAsync();
        var kind7 = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Klasse == "7a");
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        await s.AlsAsync("bec", "lehrer", "BEC");
        var (eltern, elternId) = await ElternAsync(s, admin, "eltern-7", kind7.Id);

        // Eltern: Klassenleitung (LAN) und Lehrkräfte der Klasse (FIS), keine Gruppen, nicht BEC
        var liste = (await Testserver.HolenAsync(eltern, "/api/app/mitteilungen/empfaenger")).AsArray().Select(x => x!["id"]!.GetValue<string>()).ToList();
        Assert.Contains("benutzer:" + Id(s, "lan"), liste);
        Assert.Contains("benutzer:" + Id(s, "fis"), liste);
        Assert.DoesNotContain("benutzer:" + Id(s, "bec"), liste);
        Assert.DoesNotContain(liste, x => x.StartsWith("gruppe:") || x.StartsWith("eltern:"));
        await Status(await Senden(eltern, new { empfaenger = new[] { "benutzer:" + Id(s, "bec") }, betreff = "x", text = "y", wichtig = false }), HttpStatusCode.BadRequest);
        await Testserver.JsonAsync(await Senden(eltern, new { empfaenger = new[] { "benutzer:" + Id(s, "fis") }, betreff = "Frage", text = "Hausaufgaben?", wichtig = false }));

        // nur die Administration ändert die Einstellungen
        await Status(await lan.GetAsync("/api/app/mitteilungen/einstellungen"), HttpStatusCode.Forbidden);
        await Status(await lan.PutAsJsonAsync("/api/app/mitteilungen/einstellungen", new { }), HttpStatusCode.Forbidden);
        await Status(await admin.PutAsJsonAsync("/api/app/mitteilungen/einstellungen", new { maxAnhangMb = 100, maxAnhaenge = 5, aufbewahrungTage = 365 }), HttpStatusCode.BadRequest);

        // Eltern nur noch an die Klassenleitung
        await EinstellenAsync(admin, e => { e["familieAnLehrkraefte"] = false; e["familieAnLeitung"] = false; });
        liste = (await Testserver.HolenAsync(eltern, "/api/app/mitteilungen/empfaenger")).AsArray().Select(x => x!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "benutzer:" + Id(s, "lan") }, liste);

        // Antwort auf eine Mitteilung von FIS bleibt möglich (familieAntworten)
        var vonFis = (await Testserver.JsonAsync(await Senden(fis, new { empfaenger = new[] { "benutzer:" + elternId }, betreff = "Rückfrage", text = "Bitte melden", wichtig = false })))["id"]!.GetValue<int>();
        var antwort = await Testserver.HolenAsync(eltern, $"/api/app/mitteilungen/{vonFis}/antwort");
        Assert.Equal("benutzer:" + Id(s, "fis"), antwort["empfaenger"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("AW: Rückfrage", antwort["betreff"]!.GetValue<string>());
        await Status(await Senden(eltern, new { empfaenger = new[] { "benutzer:" + Id(s, "fis") }, betreff = "AW: Rückfrage", text = "ok", wichtig = false }), HttpStatusCode.BadRequest);
        await Testserver.JsonAsync(await Senden(eltern, new { empfaenger = new[] { "benutzer:" + Id(s, "fis") }, betreff = "AW: Rückfrage", text = "ok", wichtig = false, antwortAuf = vonFis }));
        Assert.Contains((await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray(), m => m!["betreff"]!.GetValue<string>() == "AW: Rückfrage" && m["antwortAuf"]!.GetValue<int>() == vonFis);

        // Lehrkräfte dürfen Familien nicht mehr schreiben
        await EinstellenAsync(admin, e => e["lehrerAnFamilie"] = false);
        await Status(await Senden(lan, new { empfaenger = new[] { "eltern:7a" }, betreff = "x", text = "y", wichtig = false }), HttpStatusCode.BadRequest);
        Assert.DoesNotContain((await Testserver.HolenAsync(lan, "/api/app/mitteilungen/empfaenger")).AsArray(), x => x!["kategorie"]!.GetValue<string>() == "eltern");
        // die Schulleitung (Recht Nachrichten) weiterhin
        await Testserver.JsonAsync(await Senden(admin, new { empfaenger = new[] { "eltern:7a" }, betreff = "Elternbrief", text = "y", wichtig = false }));

        // Eltern dürfen nicht senden bzw. Konto gesperrt
        await EinstellenAsync(admin, e => e["elternSenden"] = false);
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/mitteilungen/empfaenger")).AsArray());
        await Status(await Senden(eltern, new { empfaenger = new[] { "benutzer:" + Id(s, "lan") }, betreff = "x", text = "y", wichtig = false }), HttpStatusCode.Forbidden);
        Assert.False((await Testserver.HolenAsync(eltern, "/api/app/mitteilungen/optionen"))["darfSenden"]!.GetValue<bool>());
        await EinstellenAsync(admin, e => { e["elternSenden"] = true; e["gesperrt"] = new JsonArray(Id(s, "lan")); });
        await Status(await Senden(lan, new { empfaenger = new[] { "gruppe:kollegium" }, betreff = "x", text = "y", wichtig = false }), HttpStatusCode.Forbidden);
        // lesen bleibt möglich
        await Testserver.HolenAsync(lan, "/api/app/mitteilungen");
    }

    [Fact]
    public async Task Allen_antworten_Weiterleiten_mit_Anhang_und_Grenzen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var bec = await s.AlsAsync("bec", "lehrer", "BEC");

        // Entwurf mit Anhang senden
        var entwurf = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new { empfaenger = new[] { "benutzer:" + Id(s, "lan"), "benutzer:" + Id(s, "fis") }, betreff = "Wichtige Information", text = "Siehe **PDF**", wichtig = false })))["id"]!.GetValue<int>();
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-Konferenz")), "datei", "Konferenz.pdf" } };
        await Testserver.JsonAsync(await admin.PostAsync($"/api/app/mitteilungen/{entwurf}/anhaenge", form));
        await Testserver.JsonAsync(await admin.PostAsync($"/api/app/mitteilungen/{entwurf}/senden", new StringContent("{}", Encoding.UTF8, "application/json")));

        // Allen antworten: Absender und die übrigen Empfänger*innen
        var alle = await Testserver.HolenAsync(lan, $"/api/app/mitteilungen/{entwurf}/antwort?alle=true");
        var ids = alle["empfaenger"]!.AsArray().Select(x => x!["id"]!.GetValue<string>()).ToList();
        Assert.Equal("benutzer:" + Id(s, "admin"), ids[0]);
        Assert.Contains("benutzer:" + Id(s, "fis"), ids);
        Assert.DoesNotContain("benutzer:" + Id(s, "lan"), ids);
        Assert.Single((await Testserver.HolenAsync(lan, $"/api/app/mitteilungen/{entwurf}/antwort"))["empfaenger"]!.AsArray());
        await Testserver.JsonAsync(await Senden(lan, new { empfaenger = ids, betreff = alle["betreff"]!.GetValue<string>(), text = "Danke", wichtig = false, antwortAuf = entwurf }));
        Assert.Contains((await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray(), m => m!["betreff"]!.GetValue<string>() == "AW: Wichtige Information");

        // Weiterleiten: Entwurf mit kopiertem Anhang
        var wg = await Testserver.JsonAsync(await lan.PostAsync($"/api/app/mitteilungen/{entwurf}/weiterleiten", null));
        Assert.Equal("WG: Wichtige Information", wg["betreff"]!.GetValue<string>());
        Assert.Contains("Siehe **PDF**", wg["text"]!.GetValue<string>());
        var anhang = wg["anhaenge"]!.AsArray().Single()!;
        Assert.Equal("Konferenz.pdf", anhang["name"]!.GetValue<string>());
        var wid = wg["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new { id = wid, empfaenger = new[] { "benutzer:" + Id(s, "bec") }, betreff = wg["betreff"]!.GetValue<string>(), text = wg["text"]!.GetValue<string>(), wichtig = false }));
        await Testserver.JsonAsync(await lan.PostAsync($"/api/app/mitteilungen/{wid}/senden", new StringContent("{}", Encoding.UTF8, "application/json")));
        var beiBec = (await Testserver.HolenAsync(bec, "/api/app/mitteilungen")).AsArray().Single()!;
        var aid = beiBec["anhaenge"]![0]!["id"]!.GetValue<int>();
        Assert.Equal("%PDF-Konferenz", await bec.GetStringAsync($"/api/app/mitteilungen/anhang/{aid}"));

        // Grenzen: Größe (Einstellung 1 MB) und Anzahl
        await EinstellenAsync(admin, e => { e["maxAnhangMb"] = 1; e["maxAnhaenge"] = 1; });
        Assert.Equal(1, (await Testserver.HolenAsync(lan, "/api/app/mitteilungen/optionen"))["maxAnhangMb"]!.GetValue<int>());
        var neu = (await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new { empfaenger = new[] { "benutzer:" + Id(s, "bec") }, betreff = "Gross", text = "x", wichtig = false })))["id"]!.GetValue<int>();
        var gross = new MultipartFormDataContent { { new ByteArrayContent(new byte[1_500_000]), "datei", "gross.bin" } };
        await Status(await lan.PostAsync($"/api/app/mitteilungen/{neu}/anhaenge", gross), HttpStatusCode.BadRequest);
        await Testserver.JsonAsync(await lan.PostAsync($"/api/app/mitteilungen/{neu}/anhaenge", new MultipartFormDataContent { { new ByteArrayContent(new byte[10]), "datei", "a.txt" } }));
        await Status(await lan.PostAsync($"/api/app/mitteilungen/{neu}/anhaenge", new MultipartFormDataContent { { new ByteArrayContent(new byte[10]), "datei", "b.txt" } }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ungelesen_markieren_und_Aufbewahrung()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var id = (await Testserver.JsonAsync(await Senden(lan, new { empfaenger = new[] { "benutzer:" + Id(s, "fis") }, betreff = "Alt", text = "x", wichtig = false })))["id"]!.GetValue<int>();
        var m = s.Dienst<Mitteilungsdienst>();
        await Testserver.JsonAsync(await fis.PostAsync($"/api/app/mitteilungen/{id}/gelesen", null));
        Assert.Equal(0, m.Ungelesen(Id(s, "fis")));
        await Testserver.JsonAsync(await fis.PostAsync($"/api/app/mitteilungen/{id}/ungelesen", null));
        Assert.Equal(1, m.Ungelesen(Id(s, "fis")));

        // älter als die Aufbewahrungsfrist (ein Jahr): wird gelöscht
        s.Dienst<Db>().Ausfuehren("UPDATE mitteilungen SET erstellt = '2025-09-01 08:00' WHERE id = @id", Db.P("@id", id));
        Assert.Equal(1, m.Aufraeumen(sofort: true));
        Assert.Empty((await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray());
    }
}
