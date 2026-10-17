using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Api;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprechtag – Status „angekündigt“ (Startseite, /heute, Familienansicht), Sichtbarkeit für Eltern/Schüler*innen, weitere Optionen
/// (Zeitblöcke, Termindauer je Lehrkraft, Puffer, Höchstzahl je Familie, Warteliste, Online-Termine mit Link, Erinnerungen, mehrtägige Reihe),
/// das Recht „Andere Klassenpläne“ für Schüler*innen und öffentliche Hinweise auf der Anmeldeseite.
/// </summary>
public class SprechtagOptionenTests
{
    // Testuhr: Mittwoch, 14.10.2026 10:00
    private static object Daten(string anmeldungVon = "2026-10-10T08:00", object? optionen = null, object[]? lehrkraefte = null, object? zeitbloecke = null,
        string[]? weitereTage = null, string ort = "Hauptgebäude") => new
    {
        name = "Elternsprechtag", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, pausen = Array.Empty<object>(),
        anmeldungVon, anmeldungBis = "2026-10-20T18:00", sichtbar = true, raumwahl = true, hinweis = "", ort, optionen, zeitbloecke, weitereTage,
        lehrkraefte = lehrkraefte ?? new object[] { new { lehrer = "LAN", raum = "" }, new { lehrer = "FIS", raum = "" } },
    };

    private static async Task<(HttpClient C, int Id)> KontoAsync(Testserver s, HttpClient admin, string login, string rolle, params int[] kinder)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "konto-passwort-1", rolle, anzeigename = login, gesperrt = false, schuelerIds = kinder }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "konto-passwort-1" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    private static async Task<int> AnlegenAsync(HttpClient admin, object daten) =>
        (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", daten)))["id"]!.GetValue<int>();

    private static async Task<string> FehlerAsync(HttpResponseMessage r, HttpStatusCode erwartet = HttpStatusCode.BadRequest)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(erwartet == r.StatusCode, $"erwartet {erwartet}, erhalten {(int)r.StatusCode}: {text}");
        return text;
    }

    [Fact]
    public async Task Angekuendigt_fuer_Eltern_Schueler_und_Heute()
    {
        await using var s = await Testserver.StartenAsync();
        var kind = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Klasse == "7a");
        var admin = await s.AlsAsync("admin");
        var (eltern, _) = await KontoAsync(s, admin, "eltern-ank", "eltern", kind.Id);
        var (schueler, _) = await KontoAsync(s, admin, "schueler-ank", "schueler", kind.Id);
        // Anmeldung erst ab 16.10. → angekündigt
        var id = await AnlegenAsync(admin, Daten(anmeldungVon: "2026-10-16T08:00"));

        var karte = Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray())!;
        Assert.Equal("angekuendigt", karte["status"]!.GetValue<string>());
        Assert.Equal("einladung", karte["phase"]!.GetValue<string>());
        Assert.Equal("Hauptgebäude", karte["ort"]!.GetValue<string>());
        Assert.Equal("16.10.2026 08:00", karte["anmeldungVonText"]!.GetValue<string>());
        Assert.False(karte["buchbar"]!.GetValue<bool>());
        Assert.Contains("15:00–17:00", karte["text"]!.GetValue<string>());

        // nicht beteiligte Lehrkraft sieht die Ankündigung ebenfalls (auch in /heute)
        var kle = await s.AlsAsync("kle", "lehrer", "KLE");
        var kleKarte = Assert.Single((await Testserver.HolenAsync(kle, "/api/app/sprechtag/start")).AsArray())!;
        Assert.Equal("angekuendigt", kleKarte["status"]!.GetValue<string>());
        Assert.Contains("nicht beteiligt", kleKarte["text"]!.GetValue<string>());
        Assert.Single((await Testserver.HolenAsync(kle, "/api/app/heute"))["sprechtage"]!.AsArray());

        // /heute liefert dieselben Karten
        var heute = await Testserver.HolenAsync(eltern, "/api/app/heute");
        Assert.Equal(id, Assert.Single(heute["sprechtage"]!.AsArray())!["id"]!.GetValue<int>());

        // Familienansicht: angekündigt, nicht buchbar, Vorschau der Zeitfenster
        var sicht = await Testserver.HolenAsync(eltern, $"/api/app/sprechtag/{id}/eltern");
        Assert.Equal("angekuendigt", sicht["sprechtag"]!["status"]!.GetValue<string>());
        Assert.StartsWith("Angekündigt", sicht["sprechtag"]!["statusText"]!.GetValue<string>());
        Assert.False(sicht["buchbar"]!.GetValue<bool>());
        Assert.Contains("Anmeldezeitraum", await FehlerAsync(await eltern.PostAsJsonAsync($"/api/app/sprechtag/{id}/buchen", new { lehrer = "LAN", schuelerId = kind.Id, beginn = "15:00" })));

        // Schüler*innen sehen die Ankündigung standardmäßig (Startseite, /heute, Liste) – nur lesen; mit „sichtbar für Schüler*innen“ = aus nicht
        Assert.True((await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}"))["sprechtag"]!["optionen"]!["sichtbarSchueler"]!.GetValue<bool>());
        Assert.Equal("angekuendigt", Assert.Single((await Testserver.HolenAsync(schueler, "/api/app/sprechtag/start")).AsArray())!["status"]!.GetValue<string>());
        Assert.Equal(id, Assert.Single((await Testserver.HolenAsync(schueler, "/api/app/heute"))["sprechtage"]!.AsArray())!["id"]!.GetValue<int>());
        Assert.Single((await Testserver.HolenAsync(schueler, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        var lesen = await Testserver.HolenAsync(schueler, $"/api/app/sprechtag/{id}/eltern");
        Assert.True(lesen["nurLesen"]!.GetValue<bool>());
        await FehlerAsync(await schueler.PostAsJsonAsync($"/api/app/sprechtag/{id}/buchen", new { lehrer = "LAN", schuelerId = kind.Id, beginn = "15:00" }), HttpStatusCode.Forbidden);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Daten(anmeldungVon: "2026-10-16T08:00", optionen: new { sichtbarSchueler = false })));
        Assert.Empty((await Testserver.HolenAsync(schueler, "/api/app/sprechtag/start")).AsArray());
        Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray());

        // ohne „ankündigen“: Eltern ohne Einladung sehen nichts; ohne „sichtbar für Eltern“ auch nicht in der Anmeldung
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Daten(anmeldungVon: "2026-10-16T08:00", optionen: new { ankuendigen = false })));
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray());
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Daten(optionen: new { sichtbarEltern = false })));
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        await FehlerAsync(await eltern.GetAsync($"/api/app/sprechtag/{id}/eltern"));
    }

    [Fact]
    public async Task Ankuendigung_ohne_gespeicherte_Optionen_fuer_Eltern_und_Schueler_sichtbar()
    {
        await using var s = await Testserver.StartenAsync();
        var kind = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Klasse == "7a");
        var admin = await s.AlsAsync("admin");
        var (eltern, _) = await KontoAsync(s, admin, "eltern-alt", "eltern", kind.Id);
        var (schueler, _) = await KontoAsync(s, admin, "schueler-alt", "schueler", kind.Id);
        // angelegt ohne Optionen (wie im Formular ohne Änderungen) und ein älterer Sprechtag ohne gespeicherte Optionen
        var neu = await AnlegenAsync(admin, Daten(anmeldungVon: "2026-10-16T08:00"));
        var alt = await AnlegenAsync(admin, Daten(anmeldungVon: "2026-10-18T08:00"));
        s.Dienst<UntPlanWeb.Daten.Db>().Ausfuehren("UPDATE sprechtage SET optionen = '' WHERE id = @id", ("@id", alt));
        var o = (await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{alt}"))["sprechtag"]!["optionen"]!;
        Assert.True(o["ankuendigen"]!.GetValue<bool>() && o["sichtbarEltern"]!.GetValue<bool>() && o["sichtbarSchueler"]!.GetValue<bool>());
        foreach (var c in new[] { eltern, schueler })
        {
            var karten = (await Testserver.HolenAsync(c, "/api/app/sprechtag/start")).AsArray();
            Assert.Equal(new[] { neu, alt }.OrderBy(x => x), karten.Select(k => k!["id"]!.GetValue<int>()).OrderBy(x => x));
            Assert.All(karten, k => Assert.Equal("angekuendigt", k!["status"]!.GetValue<string>()));
            Assert.All(karten, k => Assert.StartsWith("Angekündigt", k!["text"]!.GetValue<string>()));
            Assert.Equal(2, (await Testserver.HolenAsync(c, "/api/app/heute"))["sprechtage"]!.AsArray().Count);
        }
    }

    [Fact]
    public async Task Zeitbloecke_Dauer_je_Lehrkraft_Puffer_und_Reihe()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        // zwei Blöcke 15:00–16:00 und 17:00–18:00 → Lücke wird Pause; Puffer 5 Min.; FIS 20 Min.
        var id = await AnlegenAsync(admin, Daten(optionen: new { puffer = 5 }, zeitbloecke: new[] { new { von = "15:00", bis = "16:00" }, new { von = "17:00", bis = "18:00" } },
            lehrkraefte: new object[] { new { lehrer = "LAN", raum = "" }, new { lehrer = "FIS", raum = "", dauer = 20 } }, weitereTage: new[] { "2026-10-22" }));
        var t = (await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}"))["sprechtag"]!;
        Assert.Equal("15:00", t["beginn"]!.GetValue<string>());
        Assert.Equal("18:00", t["ende"]!.GetValue<string>());
        Assert.Equal(2, t["zeitbloecke"]!.AsArray().Count);
        Assert.Equal("16:00", t["pausen"]![0]!["von"]!.GetValue<string>());
        // LAN: 10 Min. + 5 Puffer → 15:00, 15:15, 15:30, 15:45 und 17:00 … 17:45 = 8 Fenster
        var lan = (await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}/lehrkraft?lehrer=LAN"))["zeitfenster"]!.AsArray();
        Assert.Equal(8, lan.Count);
        Assert.Equal("15:15", lan[1]!["beginn"]!.GetValue<string>());
        Assert.Equal("15:25", lan[1]!["ende"]!.GetValue<string>());
        // FIS: 20 Min. + 5 → 15:00, 15:25 und 17:00, 17:25 = 4 Fenster
        var fis = (await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}/lehrkraft?lehrer=FIS"))["zeitfenster"]!.AsArray();
        Assert.Equal(new[] { "15:00", "15:25", "17:00", "17:25" }, fis.Select(f => f!["beginn"]!.GetValue<string>()).ToArray());
        Assert.Equal("15:20", fis[0]!["ende"]!.GetValue<string>());
        // Reihe: zweiter Tag angelegt
        var reihe = t["reihe"]!.AsArray();
        Assert.Equal(new[] { "2026-10-21", "2026-10-22" }, reihe.Select(r => r!["datum"]!.GetValue<string>()).ToArray());
        Assert.Equal(2, (await Testserver.HolenAsync(admin, "/api/app/sprechtag"))["sprechtage"]!.AsArray().Count);
        // ungültige Optionen
        Assert.Contains("Puffer", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", Daten(optionen: new { puffer = 90 }))));
        Assert.Contains("Link", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", Daten(lehrkraefte: new object[] { new { lehrer = "LAN", online = true, link = "javascript:alert(1)" } }))));
        Assert.Contains("Anmeldezeitraum", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", Daten(weitereTage: new[] { "2026-10-19" }))));
    }

    [Fact]
    public async Task Hoechstzahl_Warteliste_Online_und_Erinnerung()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var kinder = stand.Schueler.Where(x => x.Klasse == "7a").Take(2).ToList();
        var admin = await s.AlsAsync("admin");
        var lanKonto = await s.AlsAsync("lan", "lehrer", "LAN");
        var (eltern, elternId) = await KontoAsync(s, admin, "eltern-max", "eltern", kinder[0].Id);
        var (andere, andereId) = await KontoAsync(s, admin, "eltern-warte", "eltern", kinder[1].Id);
        // LAN: nur 15:00–15:20 → zwei Fenster; online mit Link; höchstens ein Termin je Familie; Warteliste; Erinnerung 24 h
        var id = await AnlegenAsync(admin, new
        {
            name = "Online-Sprechtag", datum = "2026-10-21", beginn = "15:00", ende = "15:20", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00",
            sichtbar = true, optionen = new { maxTermineFamilie = 1, warteliste = true, erinnerungStunden = 24 },
            lehrkraefte = new object[] { new { lehrer = "LAN", online = true, link = "https://meet.example.org/lan" }, new { lehrer = "FIS" } },
        });
        string Url(string p = "") => $"/api/app/sprechtag/{id}{p}";
        // Link nur für Verwaltung und die Lehrkraft selbst
        Assert.Equal("", (await Testserver.HolenAsync(eltern, Url("/eltern")))["sprechtag"]!["lehrkraefte"]!.AsArray().Single(l => l!["lehrer"]!.GetValue<string>() == "LAN")!["link"]!.GetValue<string>());
        Assert.Equal("https://meet.example.org/lan", (await Testserver.HolenAsync(lanKonto, Url("/lehrkraft")))["lehrer"]!["link"]!.GetValue<string>());

        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kinder[0].Id, beginn = "15:00" }));
        Assert.Contains("höchstens ein Termin", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "FIS", schuelerId = kinder[0].Id, beginn = "15:10" })));
        var sicht = await Testserver.HolenAsync(eltern, Url("/eltern"));
        var termin = Assert.Single(sicht["termine"]!.AsArray())!;
        Assert.True(termin["online"]!.GetValue<bool>());
        Assert.Equal("https://meet.example.org/lan", termin["link"]!.GetValue<string>());
        Assert.Equal(1, sicht["gebuchtFamilie"]!.GetValue<int>());

        // Warteliste erst, wenn ausgebucht
        Assert.Contains("noch Termine frei", await FehlerAsync(await andere.PostAsJsonAsync(Url("/warteliste"), new { lehrer = "LAN", schuelerId = kinder[1].Id })));
        await Testserver.JsonAsync(await lanKonto.PutAsJsonAsync(Url("/sperren"), new { beginne = new[] { "15:10" }, gesperrt = true }));
        var lanZeile = (await Testserver.HolenAsync(andere, Url("/eltern")))["kinder"]![0]!["lehrkraefte"]!.AsArray().Single(l => l!["kurz"]!.GetValue<string>() == "LAN")!;
        Assert.True(lanZeile["ausgebucht"]!.GetValue<bool>());
        var platz = await Testserver.JsonAsync(await andere.PostAsJsonAsync(Url("/warteliste"), new { lehrer = "LAN", schuelerId = kinder[1].Id }));
        Assert.Equal(1, platz["position"]!.GetValue<int>());
        Assert.Single((await Testserver.HolenAsync(lanKonto, Url("/lehrkraft")))["warteliste"]!.AsArray());
        // frei geworden → Mitteilung an die Wartenden (Absender: Administration)
        var m = s.Dienst<Mitteilungsdienst>();
        var vorher = m.Ungelesen(andereId);
        var tid = termin["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url($"/termine/{tid}/absagen"), new { grund = "" }));
        Assert.Equal(vorher + 1, m.Ungelesen(andereId));
        Assert.Contains("frei geworden", m.Posteingang(andereId).First().Betreff);
        // Buchen entfernt den Platz auf der Warteliste
        await Testserver.JsonAsync(await andere.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kinder[1].Id, beginn = "15:00" }));
        Assert.Empty((await Testserver.HolenAsync(lanKonto, Url("/lehrkraft")))["warteliste"]!.AsArray());

        // Erinnerung: 24 h vorher einmal an Familie und Lehrkraft
        var dienst = s.Dienst<Sprechtagdienst>();
        Assert.Equal(0, dienst.ErinnerungenSenden(stand));
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 20, 16, 0, 0);
        var ungelesen = m.Ungelesen(andereId);
        Assert.Equal(1, dienst.ErinnerungenSenden(stand));
        Assert.Equal(ungelesen + 1, m.Ungelesen(andereId));
        Assert.Contains("meet.example.org", m.Posteingang(andereId).First().Text);
        Assert.Equal(0, dienst.ErinnerungenSenden(stand));
    }

    [Fact]
    public async Task Recht_andere_Klassenplaene_fuer_Schueler()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(Stufe.Nein, Recht.Standard(Rollen.Schueler, Recht.PlanKlassenSchueler));
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/plan?typ=klasse&element=5a")).StatusCode);
        var el = await Testserver.HolenAsync(schueler, "/api/app/elemente");
        Assert.False(el["andereKlassen"]!.GetValue<bool>());
        Assert.Single(el["klassen"]!.AsArray());

        // Recht für die Rolle Schüler*in freischalten
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/schueler", new { rechte = new Dictionary<string, int> { [Recht.PlanKlassenSchueler] = Stufe.Ja } }));
        schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(HttpStatusCode.OK, (await schueler.GetAsync("/api/app/plan?typ=klasse&element=5a")).StatusCode);
        el = await Testserver.HolenAsync(schueler, "/api/app/elemente");
        Assert.True(el["andereKlassen"]!.GetValue<bool>());
        Assert.True(el["klassen"]!.AsArray().Count > 1);
        Assert.Empty(el["lehrer"]!.AsArray());
        // Lehrerpläne bleiben gesperrt
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/plan?typ=lehrer&element=LAN")).StatusCode);
        Assert.Equal(2, (await Testserver.HolenAsync(schueler, "/api/app/sitzung"))["benutzer"]!["rechte"]![Recht.PlanKlassenSchueler]!.GetValue<int>());
    }

    [Fact]
    public async Task Oeffentliche_Hinweise_ohne_Anmeldung()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        var anonym = s.Client(cookies: false);
        Assert.Empty((await Testserver.HolenAsync(anonym, "/api/app/hinweise/oeffentlich")).AsArray());

        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "Elternsprechtag", text = "Am 21.10. findet der Elternsprechtag statt.", von = "2026-10-01", bis = "2026-10-21", aktiv = true }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "Entwurf", text = "noch nicht", aktiv = false }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "Abgelaufen", text = "alt", bis = "2026-10-01" }));
        // DSGVO: keine Links, keine E-Mail-Adressen, kein HTML
        Assert.Contains("Links", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "x", text = "https://tracker.example" })));
        Assert.Contains("E-Mail", await FehlerAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "x", text = "Frau Muster: muster@schule.de" })));
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/hinweise", new { titel = "<img src=x>", text = "" }));
        // nur Verwaltung
        await FehlerAsync(await lehrer.PostAsJsonAsync("/api/app/hinweise", new { titel = "x", text = "y" }), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonym.GetAsync("/api/app/hinweise")).StatusCode);

        var r = await anonym.GetAsync("/api/app/hinweise/oeffentlich");
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.False(r.Headers.Contains("Set-Cookie"));
        var liste = (await Testserver.JsonAsync(r)).AsArray();
        var h = Assert.Single(liste)!;
        Assert.Equal("Elternsprechtag", h["titel"]!.GetValue<string>());
        Assert.Null(h["aktiv"]);
        Assert.Equal(3, (await Testserver.HolenAsync(admin, "/api/app/hinweise")).AsArray().Count);
    }
}
