using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

public class SyncTests
{
    [Fact]
    public async Task Status_liefert_Schema_und_Revision()
    {
        await using var s = await Testserver.StartenAsync();
        var j = await Testserver.HolenAsync(s.Client(false), "/api/v1/status");
        Assert.Equal("untplanweb/1", j["schema"]!.GetValue<string>());
        Assert.Equal(1, j["revision"]!.GetValue<int>());
        Assert.Equal("Städtisches Beispiel-Gymnasium", j["schule"]!.GetValue<string>());
    }

    [Fact]
    public async Task Upload_braucht_gueltigen_Schluessel_und_Schul_ID()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.PaketSendenAsync(Testserver.Beispielpaket(), "falsch")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.PaketSendenAsync(Testserver.Beispielpaket(), null)).StatusCode);
        var c = s.SyncClient();
        var r = await c.PutAsync("/api/v1/schulen/andere-schule/untplanweb", new StringContent(Testserver.Beispielpaket().ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(Testserver.Beispielpaket())).StatusCode);
    }

    [Fact]
    public async Task Veraltete_Revision_ergibt_409()
    {
        await using var s = await Testserver.StartenAsync();
        var r = await s.PaketSendenAsync(Testserver.Beispielpaket());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.False(j["ok"]!.GetValue<bool>());
        Assert.Equal(1, j["revision"]!.GetValue<int>());
    }

    [Fact]
    public async Task Teilpaket_ersetzt_nur_seine_Teile()
    {
        await using var s = await Testserver.StartenAsync();
        var teil = Testserver.Beispielpaket();
        teil["revision"] = 2;
        teil["teile"] = new JsonArray("vertretungen");
        teil["vertretungen"] = new JsonArray();
        teil["elemente"] = new JsonObject(); // muss ignoriert werden, Stammdaten bleiben erhalten
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(teil)).StatusCode);
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        Assert.Equal(2, stand.Paket.Revision);
        Assert.Empty(stand.Paket.Vertretungen);
        Assert.Equal(7, stand.Paket.Elemente.Klassen.Count);
        Assert.NotEmpty(stand.Paket.Perioden);
    }

    [Fact]
    public async Task Ungueltiges_Schema_wird_abgelehnt()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var p = Testserver.Beispielpaket();
        p["schema"] = "fremd/9";
        Assert.Equal(HttpStatusCode.BadRequest, (await s.PaketSendenAsync(p)).StatusCode);
    }
}

public class AnmeldungTests
{
    [Fact]
    public async Task Einrichtung_nur_einmal_moeglich()
    {
        await using var s = await Testserver.StartenAsync();
        var r = await s.Client().PostAsJsonAsync("/api/app/einrichten", new { schulname = "X", login = "chef", passwort = "12345678" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Sperre_nach_fuenf_Fehlversuchen()
    {
        await using var s = await Testserver.StartenAsync();
        var c = s.Client();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = "falsch" })).StatusCode);
        var r = await c.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = Testserver.AdminPasswort });
        Assert.Equal((HttpStatusCode)429, r.StatusCode);
        s.Dienst<Uhr>().Fest = Testserver.Jetzt.AddMinutes(6);
        r = await c.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = Testserver.AdminPasswort });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Aendernde_Aufrufe_brauchen_Schutzkopf()
    {
        await using var s = await Testserver.StartenAsync();
        var c = s.Client();
        c.DefaultRequestHeaders.Remove("X-UntPlanWeb");
        var r = await c.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = Testserver.AdminPasswort });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Ohne_Anmeldung_401_und_Sitzung_ohne_Benutzer()
    {
        await using var s = await Testserver.StartenAsync();
        var c = s.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/app/plan?typ=klasse&element=7a")).StatusCode);
        var j = await Testserver.HolenAsync(c, "/api/app/sitzung");
        Assert.True(j["eingerichtet"]!.GetValue<bool>());
        Assert.Null(j["benutzer"]);
    }

    [Fact]
    public async Task Gesperrtes_Konto_verliert_Sitzung()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/app/elemente")).StatusCode);
        var bd = s.Dienst<Benutzerdienst>();
        var b = bd.NachLogin("mue")!;
        bd.Aendern(b.Id, b.Anzeigename, b.Rolle, b.Element, b.Email, gesperrt: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/app/elemente")).StatusCode);
    }

    [Fact]
    public async Task Konten_aus_Stammdaten_mit_Pflicht_zur_Passwortaenderung()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var j = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/stammdaten", new { lehrer = true, klassen = true }));
        Assert.Equal(22 + 7, j["anzahl"]!.GetValue<int>());
        var konto = j["konten"]!.AsArray().First(k => k!["login"]!.GetValue<string>() == "sne")!;
        var c = s.Client();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/anmelden", new { login = "sne", passwort = konto["passwort"]!.GetValue<string>() }));
        var sitzung = await Testserver.HolenAsync(c, "/api/app/sitzung");
        Assert.True(sitzung["benutzer"]!["passwortAendern"]!.GetValue<bool>());
        // erneuter Aufruf legt nichts doppelt an
        j = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/stammdaten", new { lehrer = true, klassen = true }));
        Assert.Equal(0, j["anzahl"]!.GetValue<int>());
    }
}

public class PlanTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    [Fact]
    public async Task Klassenplan_zeigt_Vertretung_mit_altem_Lehrer()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-14");
        Assert.Equal(5, plan["tage"]!.AsArray().Count);
        var k = Karten(plan, "2026-10-14", 1).Single();
        Assert.Equal("geaendert", k["status"]!.GetValue<string>());
        var lehrer = k["lehrer"]!.AsArray().Single()!;
        Assert.Equal("WAG", lehrer["alt"]!.GetValue<string>());
        Assert.NotEqual("WAG", lehrer["k"]!.GetValue<string>());
    }

    [Fact]
    public async Task Elemente_enthalten_Ferien_mit_Zeitraum()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var elemente = await Testserver.HolenAsync(c, "/api/app/elemente");
        var herbst = elemente["ferien"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "Herbstferien")!;
        Assert.Equal("2026-10-17", herbst["von"]!.GetValue<string>());
        Assert.Equal("2026-10-31", herbst["bis"]!.GetValue<string>());
        Assert.False(herbst["feiertag"]!.GetValue<bool>());
        Assert.NotNull(elemente["klassen"]);
    }

    [Fact]
    public async Task Freisetzung_der_Klasse_ist_Entfall_und_steht_im_Tageskopf()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13");
        Assert.All(plan["karten"]!.AsArray().Where(k => k!["datum"]!.GetValue<string>() == "2026-10-13"), k => Assert.Equal("entfall", k!["status"]!.GetValue<string>()));
        var dienstag = plan["tage"]!.AsArray()[1]!;
        Assert.Contains(dienstag["hinweise"]!.AsArray(), h => h!.GetValue<string>().Contains("abwesend"));
    }

    [Fact]
    public async Task Lehrerplan_mit_Bereitschaft_und_Aufsicht()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=SNE&datum=2026-10-14");
        Assert.Contains(Karten(plan, "2026-10-14", 3), k => k["status"]!.GetValue<string>() == "bereitschaft");
        Assert.Contains(Karten(plan, "2026-10-14", 3), k => k["status"]!.GetValue<string>() == "geaendert" && k["fach"]!.GetValue<string>() == "F");
        // Bereitschaft am Donnerstag entfällt (Klausuraufsicht) – wird nie vertreten
        Assert.Contains(Karten(plan, "2026-10-15", 3), k => k["status"]!.GetValue<string>() == "klausur");
        Assert.NotEmpty(plan["aufsichten"]!.AsArray());
    }

    [Fact]
    public async Task Abwesende_Lehrkraft_sieht_vertretene_Stunden()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=WAG&datum=2026-10-14");
        var k = Karten(plan, "2026-10-14", 1).Single();
        Assert.Equal("abwesend", k["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tagesuebersicht_und_Vertretungsplan()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var tag = await Testserver.HolenAsync(c, "/api/app/tag?typ=klasse&datum=2026-10-14");
        Assert.Equal(7, tag["zeilen"]!.AsArray().Count);
        var vt = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-13&tage=1");
        var zeilen = vt["tage"]![0]!["zeilen"]!.AsArray();
        // 7a: sechs Freisetzungen, zusammengefasst wo Fach/Lehrkraft gleich (M 3–4)
        Assert.Contains(zeilen, z => z!["klassen"]!.GetValue<string>() == "7a" && z["stunde"]!.GetValue<string>() == "3–4");
        Assert.Contains("HOF", vt["tage"]![0]!["abwesendeLehrer"]!.AsArray().Select(x => x!.GetValue<string>()).First());
    }

    [Fact]
    public async Task Woche_ausserhalb_der_Tagesdaten_nutzt_Regelplan()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-11-03");
        Assert.NotEmpty(plan["karten"]!.AsArray());
        Assert.All(plan["karten"]!.AsArray(), k => Assert.Equal("regulaer", k!["status"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Kalenderabo_liefert_iCalendar()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var profil = await Testserver.HolenAsync(c, "/api/app/profil");
        var url = new Uri(profil["ical"]!.GetValue<string>());
        var text = await s.Client(false).GetStringAsync(url.PathAndQuery);
        Assert.StartsWith("BEGIN:VCALENDAR", text);
        Assert.Contains("BEGIN:VEVENT", text);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client(false).GetAsync("/ical/unbekannt.ics")).StatusCode);
    }
}

public class RechteTests
{
    [Fact]
    public async Task Klassenkonto_sieht_nur_eigene_Klasse()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/app/plan?typ=klasse&element=7a")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/app/plan?typ=raum&element=R101")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/plan?typ=klasse&element=7b")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/plan?typ=lehrer&element=SNE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/tag?typ=klasse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/admin/benutzer")).StatusCode);
        var el = await Testserver.HolenAsync(c, "/api/app/elemente");
        Assert.Single(el["klassen"]!.AsArray());
        Assert.Empty(el["lehrer"]!.AsArray());
    }

    [Fact]
    public async Task Schuelerversion_ohne_Lehrerinterna()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("klasse-ef", "schueler", "EF");
        var vt = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-15&tage=1");
        var tag = vt["tage"]![0]!;
        var zeilen = tag["zeilen"]!.AsArray();
        Assert.NotEmpty(zeilen);
        Assert.All(zeilen, z => Assert.Contains("EF", z!["klassen"]!.GetValue<string>()));
        Assert.DoesNotContain(zeilen, z => z!["fach"]!.GetValue<string>() == "BER");
        Assert.Empty(tag["abwesendeLehrer"]!.AsArray());
        // Entfall bleibt sichtbar, wer fehlt nur bei Entfall
        Assert.All(zeilen.Where(z => z!["status"]!.GetValue<string>() != "entfall"), z => Assert.Null(z!["lehrerAlt"]));
    }

    [Fact]
    public async Task Lehrkraft_darf_nicht_administrieren()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/admin/uebersicht")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/app/admin/nachrichten", new { von = "2026-10-14", bis = "2026-10-14", text = "x", startseite = true, monitor = true })).StatusCode);
        var leitung = await s.AlsAsync("chef", "leitung", "");
        Assert.Equal(HttpStatusCode.OK, (await leitung.PostAsJsonAsync("/api/app/admin/nachrichten", new { von = "2026-10-14", bis = "2026-10-14", text = "Hallo", zielgruppe = "alle", startseite = true, monitor = true })).StatusCode);
    }
}

public class RueckmeldungTests
{
    [Fact]
    public async Task Abwesenheit_wird_abgeholt_und_quittiert()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var m = await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/abwesenheit", new { von = "2026-10-15", bis = "2026-10-16", grund = "Fortbildung", text = "Aufgaben im Fach" }));
        var id = m["id"]!.GetValue<string>();

        var sync = s.SyncClient();
        var offen = await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen");
        var r = offen.AsArray().Single()!;
        Assert.Equal("absenz", r["typ"]!.GetValue<string>());
        Assert.Equal("MUE", r["lehrer"]!.GetValue<string>());
        Assert.Equal("2026-10-15", r["von"]!.GetValue<string>());

        await Testserver.JsonAsync(await sync.PostAsJsonAsync($"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen/quittieren", new[] { id }));
        Assert.Empty((await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray());
        var meine = await Testserver.HolenAsync(c, "/api/app/meldungen");
        Assert.Equal("quittiert", meine[0]!["status"]!.GetValue<string>());
        // übernommene Meldungen lassen sich nicht mehr zurückziehen
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/app/meldungen/{id}")).StatusCode);
    }

    [Fact]
    public async Task Abwesenheit_nicht_weit_rueckwirkend()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var r = await c.PostAsJsonAsync("/api/app/meldungen/abwesenheit", new { von = "2026-09-01", bis = "2026-09-02" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("fehler", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Raumbuchung_ist_vorgemerkt_und_blockiert_den_Raum()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=1&bis=1");
        var raum = frei.AsArray().First()!["kurz"]!.GetValue<string>();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 1, stundeBis = 1, raum, titel = "Projekt" }));

        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={raum}&datum=2026-10-16");
        Assert.Contains(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() == "vorgemerkt" && k["datum"]!.GetValue<string>() == "2026-10-16");
        frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=1&bis=1");
        Assert.DoesNotContain(frei.AsArray(), x => x!["kurz"]!.GetValue<string>() == raum);
        var doppelt = await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 1, stundeBis = 1, raum });
        Assert.Equal(HttpStatusCode.BadRequest, doppelt.StatusCode);

        var offen = await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen");
        Assert.Equal("raumbuchung", offen[0]!["typ"]!.GetValue<string>());
        Assert.Equal(raum, offen[0]!["raum"]!.GetValue<string>());
    }

    [Fact]
    public async Task Belegter_Raum_ist_nicht_frei()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        // R101 ist Do 3.–4. gesperrt, R105 Mi 1. durch 7a belegt
        var frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-15&von=3&bis=3");
        Assert.DoesNotContain(frei.AsArray(), x => x!["kurz"]!.GetValue<string>() == "R101");
        frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-14&von=1&bis=1");
        Assert.DoesNotContain(frei.AsArray(), x => x!["kurz"]!.GetValue<string>() == "R105");
    }

    [Fact]
    public async Task Vorgemerkte_Pruefung_loeschen_zieht_die_Rueckmeldung_zurueck()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = (await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=3&bis=4")).AsArray()[0]!["kurz"]!.GetValue<string>();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, raeume = new[] { frei }, aufsicht = new[] { "MUE" }, titel = "Deutsch GK" }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", new { datum = "2026-10-16", stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, titel = "Deutsch GK" }));
        var offen = await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen");
        Assert.DoesNotContain(offen.AsArray(), x => x!["typ"]!.GetValue<string>().StartsWith("klausur"));
    }

    [Fact]
    public async Task Klausur_wird_als_Rueckmeldung_uebertragen()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        // belegter Raum wird abgelehnt
        var belegt = await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, raeume = new[] { "R106" }, aufsicht = new[] { "MUE" }, titel = "Deutsch GK" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, belegt.StatusCode);
        var frei = (await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=3&bis=4")).AsArray()[0]!["kurz"]!.GetValue<string>();
        // doppelt abgeschickt: nur eine Prüfung
        for (var i = 0; i < 2; i++)
            await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, raeume = new[] { frei }, aufsicht = new[] { "MUE" }, titel = "Deutsch GK" }));
        var offen = await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen");
        Assert.Single(offen.AsArray(), x => x!["typ"]!.GetValue<string>() == "klausur");
        var k = offen[0]!;
        Assert.Equal("klausur", k["typ"]!.GetValue<string>());
        Assert.Equal("Deutsch GK", k["titel"]!.GetValue<string>());
        Assert.Equal("EF", k["klassen"]![0]!.GetValue<string>());
    }
}

public class KlassenbuchUndMitteilungTests
{
    [Fact]
    public async Task Nur_unterrichtende_Lehrkraft_darf_eintragen()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = plan.Karten(stand, new DateOnly(2026, 10, 14), Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer");
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var info = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/stunde?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        Assert.True(info["darf"]!.GetValue<bool>());
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Bruchrechnung", hausaufgabe = "S. 12 Nr. 3", faellig = "2026-10-16", notiz = "intern" }));

        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        var r = await fremd.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Nein" });
        Assert.False(r.IsSuccessStatusCode);

        // Klasse sieht Inhalt und Hausaufgabe, aber keine Notiz
        var klasse = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var ha = await Testserver.HolenAsync(klasse, "/api/app/klassenbuch/hausaufgaben?klasse=7a");
        Assert.Equal("S. 12 Nr. 3", ha[0]!["hausaufgabe"]!.GetValue<string>());
        Assert.Equal("", ha[0]!["notiz"]!.GetValue<string>());
    }

    [Fact]
    public async Task Mitteilung_an_Gruppe_kommt_an()
    {
        await using var s = await Testserver.StartenAsync();
        var a = await s.AlsAsync("mue", "lehrer", "MUE");
        var b = await s.AlsAsync("sne", "lehrer", "SNE");
        await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/mitteilungen", new { empfaenger = new[] { "gruppe:kollegium" }, betreff = "Konferenz", text = "Heute 14 Uhr", wichtig = true }));
        var sitzung = await Testserver.HolenAsync(b, "/api/app/sitzung");
        Assert.Equal(1, sitzung["ungelesen"]!.GetValue<int>());
        var post = await Testserver.HolenAsync(b, "/api/app/mitteilungen");
        var id = post[0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await b.PostAsJsonAsync($"/api/app/mitteilungen/{id}/gelesen", new { }));
        Assert.Equal(0, (await Testserver.HolenAsync(b, "/api/app/sitzung"))["ungelesen"]!.GetValue<int>());
        // Absender bekommt die eigene Gruppenmitteilung nicht in den Posteingang
        Assert.Empty((await Testserver.HolenAsync(a, "/api/app/mitteilungen")).AsArray());
        Assert.Single((await Testserver.HolenAsync(a, "/api/app/mitteilungen?ordner=gesendet")).AsArray());
    }

    [Fact]
    public async Task Klassenkonto_darf_nicht_an_alle_schreiben()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var r = await c.PostAsJsonAsync("/api/app/mitteilungen", new { empfaenger = new[] { "gruppe:alle" }, betreff = "x", text = "y", wichtig = false });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}

public class VeranstaltungTests
{
    [Fact]
    public async Task Veranstaltungen_als_Ereignisse_und_im_Vertretungsplan()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        // Projekttag der Klassen 5a/5b am Donnerstag: ganztägig, Unterricht entfällt
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-15");
        var donnerstag = plan["tage"]!.AsArray()[3]!;
        var e = donnerstag["ereignisse"]!.AsArray().Single(x => x!["titel"]!.GetValue<string>() == "Projekttag Medien")!;
        Assert.True(e["ganztaegig"]!.GetValue<bool>());
        // Elternsprechtag ohne Elemente gilt schulweit
        Assert.Contains(plan["tage"]!.AsArray()[1]!["ereignisse"]!.AsArray(), x => x!["titel"]!.GetValue<string>() == "Elternsprechtag");

        // Lehrerkonferenz Mi 7.–8.: im Raumplan eine gemeinsame Karte statt einer je Lehrkraft
        var raum = await Testserver.HolenAsync(c, "/api/app/plan?typ=raum&element=EDV2&datum=2026-10-14");
        var karten = raum["karten"]!.AsArray().Where(k => k!["status"]!.GetValue<string>() == "veranstaltung").ToList();
        Assert.Single(karten);
        Assert.True(karten[0]!["lehrer"]!.AsArray().Count > 10);

        // Vertretungsplan: eine Zeile je Veranstaltung, keine Teilnahmezeilen
        var vt = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-14&tage=1");
        var tag = vt["tage"]![0]!;
        Assert.Contains(tag["veranstaltungen"]!.AsArray(), x => x!.GetValue<string>().StartsWith("Lehrerkonferenz (7.–8. Std."));
        Assert.DoesNotContain(tag["zeilen"]!.AsArray(), z => z!["art"]!.GetValue<string>().StartsWith("Veranst") && z["klassen"]!.GetValue<string>() == "");
    }
}

public class MonitorTests
{
    [Fact]
    public async Task Monitor_zeigt_Vertretungen_ohne_Anmeldung()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/monitore", new { name = "Foyer", version = "schueler", tage = 2 }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/nachrichten", new { von = "2026-10-14", bis = "2026-10-14", text = "Nur intern", zielgruppe = "lehrer", startseite = true, monitor = true }));
        var link = (await Testserver.HolenAsync(admin, "/api/app/admin/monitore"))[0]!["link"]!.GetValue<string>();
        var token = link.Split('/').Last();
        var mon = await Testserver.HolenAsync(s.Client(false), $"/api/monitor/{token}");
        var tage = mon["tage"]!.AsArray();
        Assert.Equal(2, tage.Count);
        Assert.Equal("2026-10-14", tage[0]!["datum"]!.GetValue<string>());
        Assert.DoesNotContain(tage[0]!["nachrichten"]!.AsArray(), n => n!.GetValue<string>() == "Nur intern");
        Assert.DoesNotContain(tage[1]!["zeilen"]!.AsArray(), z => z!["fach"]!.GetValue<string>() == "BER");
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client(false).GetAsync("/api/monitor/falsch")).StatusCode);
        var html = await s.Client(false).GetAsync($"/monitor/{token}");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
    }

    [Fact]
    public async Task Gesundheit_und_Startseite()
    {
        await using var s = await Testserver.StartenAsync();
        var c = s.Client(false);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/gesundheit")).StatusCode);
        var start = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Contains("frame-ancestors 'none'", start.Headers.GetValues("Content-Security-Policy").First());
        Assert.Contains("UntPlanWeb", await start.Content.ReadAsStringAsync());
    }
}

public class BuchbarUndFehlzeitenTests
{
    private static readonly DateOnly Freitag = new(2026, 10, 16);

    [Fact]
    public async Task Nur_buchbare_Raeume_werden_angeboten_und_gebucht()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var buchbar = stand.Paket.Elemente.Raeume.Where(r => r.Buchbar).Select(r => r.Kurz).ToHashSet();
        var nicht = stand.Paket.Elemente.Raeume.First(r => !r.Buchbar).Kurz;
        Assert.NotEmpty(buchbar);
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=1&bis=1");
        Assert.All(frei.AsArray(), x => Assert.Contains(x!["kurz"]!.GetValue<string>(), buchbar));
        var r = await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 1, stundeBis = 1, raum = nicht });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Raumbuchung_und_Klausur_erscheinen_auf_der_Stundenkarte()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var karten = plan.Karten(stand, Freitag, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status == "regulaer").ToList();
        var k = karten[0];
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum=2026-10-16&von={k.Von}&bis={k.Von}");
        var raum = frei[0]!["kurz"]!.GetValue<string>();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = k.Von, stundeBis = k.Von, raum, klassen = new[] { "7a" }, titel = "Raumänderung" }));
        var k2 = karten.First(x => x.Von != k.Von);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = k2.Von, stundeBis = k2.Von, klassen = new[] { "7a" }, titel = "Mathe-Arbeit" }));

        var p = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-16");
        var amTag = p["karten"]!.AsArray().Where(x => x!["datum"]!.GetValue<string>() == "2026-10-16").ToList();
        var mitRaum = amTag.Single(x => x!["von"]!.GetValue<int>() == k.Von && x["fach"]!.GetValue<string>() == k.Fach);
        // Buchungen erzeugen nie eine Vertretung: kein Änderungsstatus, keine Art, kein Text – nur der Raum ändert sich
        Assert.Equal("", mitRaum!["art"]!.GetValue<string>());
        Assert.Equal("", mitRaum["text"]!.GetValue<string>());
        Assert.Equal(k.Status, mitRaum["status"]!.GetValue<string>());
        Assert.Equal(raum, mitRaum["raeume"]![0]!["k"]!.GetValue<string>());
        var klausur = amTag.Single(x => x!["von"]!.GetValue<int>() == k2.Von && x["fach"]!.GetValue<string>() == k2.Fach);
        Assert.Equal("klausur", klausur!["status"]!.GetValue<string>());
        Assert.Equal("Mathe-Arbeit", klausur["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fehlzeiten_erfassen_und_entschuldigen()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var heute = new DateOnly(2026, 10, 14);
        var k = plan.Karten(stand, heute, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var kl = await s.AlsAsync("sl-test", "leitung", "");

        var anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        Assert.True(anw["darf"]!.GetValue<bool>());
        var schueler = anw["schueler"]!.AsArray();
        Assert.NotEmpty(schueler);
        var a = schueler[0]!["id"]!.GetValue<int>();
        var b = schueler[1]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht,
            eintraege = new object[] { new { schuelerId = a, art = "fehlt", minuten = 0 }, new { schuelerId = b, art = "verspaetet", minuten = 10 } },
        }));
        anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        Assert.Equal(2, anw["fehlzeiten"]!.AsArray().Count);
        Assert.False(string.IsNullOrEmpty(anw["geprueft"]?.GetValue<string>()));

        var f = await Testserver.HolenAsync(kl, "/api/app/klassenbuch/fehlzeiten?klasse=7a&von=2026-10-01&bis=2026-10-14");
        var sa = f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == a)!;
        Assert.Equal(k.Bis - k.Von + 1, sa["stunden"]!.GetValue<int>());
        Assert.Equal(1, f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == b)!["verspaetungen"]!.GetValue<int>());
        var ids = f["eintraege"]!.AsArray().Where(x => x!["schuelerId"]!.GetValue<int>() == a).Select(x => x!["id"]!.GetValue<int>()).ToList();

        // Entschuldigen darf nur Klassenleitung / Schulleitung
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.False((await fremd.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/status", new { ids, status = "entschuldigt" })).IsSuccessStatusCode);
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/status", new { ids, status = "entschuldigt" }));
        f = await Testserver.HolenAsync(kl, "/api/app/klassenbuch/fehlzeiten?klasse=7a&von=2026-10-01&bis=2026-10-14");
        Assert.Equal(k.Bis - k.Von + 1, f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == a)!["entschuldigt"]!.GetValue<int>());

        // Erneutes Speichern behält den Entschuldigungsstatus
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = new object[] { new { schuelerId = a, art = "fehlt" } } }));
        f = await Testserver.HolenAsync(kl, "/api/app/klassenbuch/fehlzeiten?klasse=7a&von=2026-10-01&bis=2026-10-14");
        Assert.Equal(0, f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == b)!["verspaetungen"]!.GetValue<int>());
        Assert.Equal(k.Bis - k.Von + 1, f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == a)!["entschuldigt"]!.GetValue<int>());

        // Wochenübersicht zeigt die Fehlenden
        var w = await Testserver.HolenAsync(c, "/api/app/klassenbuch/woche?klasse=7a&datum=2026-10-14");
        var tag = w["tage"]!.AsArray().Single(x => x!["datum"]!.GetValue<string>() == "2026-10-14")!;
        Assert.Contains(tag["stunden"]!.AsArray(), x => x!["fehlend"]!.AsArray().Count == 1);
    }

    [Fact]
    public async Task Ganze_Tage_und_Vermerke()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var sid = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a").Id;
        var r = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/tage", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-13", status = "entschuldigt", bemerkung = "krank" }));
        Assert.True(r["stunden"]!.GetValue<int>() > 0);
        var lehrer = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.False((await lehrer.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/tage", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-12" })).IsSuccessStatusCode);

        await Testserver.JsonAsync(await lehrer.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "7a", schuelerId = sid, art = "Lob", text = "Sehr gutes Referat" }));
        var v = await Testserver.HolenAsync(lehrer, "/api/app/klassenbuch/vermerke?klasse=7a");
        Assert.Equal("Lob", v["vermerke"]![0]!["art"]!.GetValue<string>());
    }
}

public class SsoTests
{
    [Fact]
    public async Task Einmalanmeldung_fuer_UntPlanMobile()
    {
        await using var s = await Testserver.StartenAsync();
        var host = s.Client().BaseAddress!.Host;
        var ruecksprung = $"http://{host}:8081/sso/rueckkehr";
        // nicht angemeldet: weiter zur Anmeldeseite
        var anonym = s.Client();
        var r = await anonym.GetAsync($"/sso/mobil?ruecksprung={Uri.EscapeDataString(ruecksprung)}&state=abc");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/?weiter=", r.Headers.Location!.OriginalString);
        // fremde Rücksprungadresse wird abgelehnt
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/sso/mobil?ruecksprung=" + Uri.EscapeDataString("https://boese.example/x") + "&state=abc")).StatusCode);
        // angemeldet: Einmal-Code
        r = await c.GetAsync($"/sso/mobil?ruecksprung={Uri.EscapeDataString(ruecksprung)}&state=abc");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var ziel = r.Headers.Location!.ToString();
        Assert.StartsWith(ruecksprung + "?code=", ziel);
        Assert.EndsWith("&state=abc", ziel);
        var code = Uri.UnescapeDataString(ziel[(ziel.IndexOf("code=") + 5)..ziel.IndexOf("&state")]);
        // einlösen (ohne Cookie) liefert eine neue Sitzung, aber nur einmal
        var mobil = s.Client(cookies: true);
        await Testserver.JsonAsync(await mobil.PostAsJsonAsync("/api/app/sso/einloesen", new { code, ruecksprung }));
        Assert.Equal("mue", (await Testserver.HolenAsync(mobil, "/api/app/sitzung"))["benutzer"]!["login"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync("/api/app/sso/einloesen", new { code, ruecksprung })).StatusCode);
    }
}
