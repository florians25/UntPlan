using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Neue Funktionen der Portal-Oberfläche (Startseite, Offene Stunden, Klassendienste, Befreiungen, Berichte, Mitteilungen, Verwaltung).</summary>
public class ErweiterungTests
{
    private static readonly DateOnly Heute = new(2026, 10, 14);

    [Fact]
    public async Task Startseite_zeigt_letzte_Anmeldung_Planaktualisierung_und_Nachrichten()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/nachrichten", new
        {
            von = "2026-10-14", bis = "2026-10-14", text = "Heute Wandertag der 5a.", zielgruppe = "alle", startseite = true, monitor = true, betreff = "Wandertag", aufgefaechert = false,
        }));
        // nur mittwochs
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/nachrichten", new
        {
            von = "2026-10-12", bis = "2026-10-16", text = "Nur am Donnerstag", zielgruppe = "alle", startseite = true, monitor = true, wochentag = 4,
        }));

        // erste Anmeldung: noch keine frühere Anmeldung
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var start = await Testserver.HolenAsync(c, "/api/app/start");
        Assert.Equal("2026-10-14", start["datum"]!.GetValue<string>());
        Assert.Null(start["letzteAnmeldung"]);
        Assert.False(string.IsNullOrEmpty(start["planAktualisiert"]!.GetValue<string>()));
        var n = start["nachrichten"]!.AsArray();
        var wandertag = n.Single(x => x!["betreff"]!.GetValue<string>() == "Wandertag")!;
        Assert.False(wandertag["aufgefaechert"]!.GetValue<bool>());
        Assert.DoesNotContain(n, x => x!["text"]!.GetValue<string>() == "Nur am Donnerstag");
        Assert.NotEmpty(start["stunden"]!.AsArray());
        Assert.All(start["stunden"]!.AsArray(), x => Assert.Matches(@"^\d\d:\d\d$", x!["beginn"]!.GetValue<string>()));

        // zweite Anmeldung: die erste erscheint als „Letzte Anmeldung“
        var c2 = await s.AlsAsync("sne", "lehrer", "SNE");
        start = await Testserver.HolenAsync(c2, "/api/app/start");
        Assert.Equal("2026-10-14 10:00:00", start["letzteAnmeldung"]!.GetValue<string>());
    }

    [Fact]
    public async Task Offene_Stunden_mit_Abwesenheitskontrolle_und_Lehrstoff()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var k = plan.Karten(stand, Heute, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0 && string.CompareOrdinal(x.Ende, "10:00") <= 0);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/stunden?von=2026-10-12&bis=2026-10-14");
        var gesamt = r["gesamt"]!.GetValue<int>();
        Assert.True(gesamt > 0);
        Assert.Equal(gesamt, r["offen"]!.GetValue<int>());
        Assert.All(r["zeilen"]!.AsArray(), z => Assert.Equal(lehrer, z!["lehrer"]!.GetValue<string>()));
        // keine zukünftigen Stunden
        Assert.DoesNotContain(r["zeilen"]!.AsArray(), z => z!["datum"]!.GetValue<string>() == "2026-10-14" && string.CompareOrdinal(z["ende"]!.GetValue<string>(), "10:00") > 0);

        // Lehrstoff und Abwesenheitskontrolle eintragen → Stunde ist erledigt
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Bruchrechnung" }));
        r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/stunden?von=2026-10-12&bis=2026-10-14");
        var z = r["zeilen"]!.AsArray().First(x => x!["datum"]!.GetValue<string>() == k.Datum && x["von"]!.GetValue<int>() == k.Von && x["unterricht"]!.GetValue<int>() == k.Unterricht)!;
        Assert.True(z["lehrstoff"]!.GetValue<bool>());
        Assert.False(z["geprueft"]!.GetValue<bool>());
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = Array.Empty<object>() }));
        r = await Testserver.HolenAsync(c, "/api/app/klassenbuch/stunden?von=2026-10-12&bis=2026-10-14");
        Assert.Equal(gesamt - 1, r["offen"]!.GetValue<int>());

        // Lehrkräfte sehen nur ihre eigenen Stunden, die Schulleitung wählt Lehrkräfte aus
        var fremd = await Testserver.HolenAsync(c, "/api/app/klassenbuch/stunden?von=2026-10-12&bis=2026-10-14&lehrer=SNE,FIS");
        Assert.All(fremd["zeilen"]!.AsArray(), x => Assert.Equal(lehrer, x!["lehrer"]!.GetValue<string>()));
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var zwei = await Testserver.HolenAsync(leitung, "/api/app/klassenbuch/stunden?von=2026-10-12&bis=2026-10-14&lehrer=SNE,FIS");
        Assert.Contains(zwei["zeilen"]!.AsArray(), x => x!["lehrer"]!.GetValue<string>() == "SNE");
        Assert.Contains(zwei["zeilen"]!.AsArray(), x => x!["lehrer"]!.GetValue<string>() == "FIS");
    }

    [Fact]
    public async Task Klassendienste_eintragen_und_automatisch_einteilen()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var schueler = stand.Paket.Elemente.Schueler.Where(x => x.Klasse == "7a").ToList();
        var leitung = await s.AlsAsync("lan", "lehrer", "LAN"); // Klassenleitung der 7a
        var d = await Testserver.HolenAsync(leitung, "/api/app/klassendienste?klasse=7a&von=2026-10-12&bis=2026-12-31");
        Assert.True(d["verwalten"]!.GetValue<bool>());
        var dienst = d["arten"]!.AsArray().First(x => x!["name"]!.GetValue<string>() == "Klassenordner")!["id"]!.GetValue<int>();

        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassendienste/woche", new { dienstId = dienst, schuelerId = schueler[0].Id, datum = "2026-10-14", an = true }));
        d = await Testserver.HolenAsync(leitung, $"/api/app/klassendienste?klasse=7a&von=2026-10-12&bis=2026-10-18&dienst={dienst}");
        var z = d["zuordnungen"]!.AsArray().Single()!;
        Assert.Equal("2026-10-12", z["von"]!.GetValue<string>());
        Assert.Equal(schueler[0].Id, z["schuelerId"]!.GetValue<int>());
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassendienste/woche", new { dienstId = dienst, schuelerId = schueler[0].Id, datum = "2026-10-14", an = false }));
        d = await Testserver.HolenAsync(leitung, $"/api/app/klassendienste?klasse=7a&von=2026-10-12&bis=2026-10-18&dienst={dienst}");
        Assert.Empty(d["zuordnungen"]!.AsArray());

        var auto = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassendienste/automatisch", new { klasse = "7a", dienstId = dienst, von = "2026-10-12", bis = "2026-11-08", proWoche = 2 }));
        Assert.True(auto["eingeteilt"]!.GetValue<int>() >= 4);
        // Fremde Lehrkraft darf nicht einteilen
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsJsonAsync("/api/app/klassendienste", new { dienstId = dienst, schuelerId = schueler[1].Id, von = "2026-10-12", bis = "2026-10-16" })).StatusCode);
    }

    [Fact]
    public async Task Befreiungen_und_Abwesenheiten_bearbeiten()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var sid = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a").Id;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var b = await Testserver.HolenAsync(leitung, "/api/app/befreiungen?klasse=7a");
        var grund = b["gruende"]!.AsArray().First()!["kurz"]!.GetValue<string>();
        var neu = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/befreiungen", new { schuelerId = sid, von = "2026-10-12", bis = "2026-12-18", wochentag = 3, fach = "SP", grund, text = "Attest" }));
        Assert.Equal(3, neu["wochentag"]!.GetValue<int>());
        var befreit = await Testserver.HolenAsync(leitung, "/api/app/klassenbuch/befreit?datum=2026-10-14&klassen=7a&fach=SP");
        Assert.Contains(befreit.AsArray(), x => x!.GetValue<int>() == sid);
        befreit = await Testserver.HolenAsync(leitung, "/api/app/klassenbuch/befreit?datum=2026-10-15&klassen=7a&fach=SP");
        Assert.Empty(befreit.AsArray());

        // Abwesenheit mit Grund anlegen, ändern, zusammenfassen, löschen
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-13", grund = "K", status = "offen", bemerkung = "Grippe" }));
        var liste = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten?von=2026-10-12&bis=2026-10-14&klasse=7a");
        var eigene = liste.AsArray().Where(x => x!["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(eigene, x => Assert.Equal("K", x!["grund"]!.GetValue<string>()));
        var ids = eigene.SelectMany(x => x!["ids"]!.AsArray().Select(i => i!.GetValue<int>())).ToList();
        await Testserver.JsonAsync(await leitung.PutAsJsonAsync("/api/app/abwesenheiten", new { ids, status = "entschuldigt", bemerkung = "Attest liegt vor" }));
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten/zusammenfassen", new { ids }));
        liste = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten?von=2026-10-12&bis=2026-10-14&klasse=7a");
        eigene = liste.AsArray().Where(x => x!["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.Single(eigene.Select(x => x!["gruppe"]!.GetValue<string>()).Distinct());
        Assert.All(eigene, x => Assert.Equal("entschuldigt", x!["status"]!.GetValue<string>()));
        Assert.Contains("Attest liegt vor", (await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/texte")).ToJsonString());
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten/loeschen", new { ids }));
        liste = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten?von=2026-10-12&bis=2026-10-14&klasse=7a");
        Assert.DoesNotContain(liste.AsArray(), x => x!["schuelerId"]!.GetValue<int>() == sid);
    }

    [Theory]
    [InlineData("fehlzeiten_schueler")]
    [InlineData("fehlzeiten_klasse")]
    [InlineData("fehlzeiten_fach")]
    [InlineData("fehltage_klasse")]
    [InlineData("abwesenheitsueberschreitung")]
    [InlineData("abwesenheiten")]
    [InlineData("anwesenheiten")]
    [InlineData("unentschuldigt")]
    [InlineData("statusuebersicht")]
    [InlineData("entschuldigungsschreiben")]
    [InlineData("klassenbucheintraege")]
    [InlineData("klassenbucheintraege_schueler")]
    [InlineData("arbeitsbericht_tag")]
    [InlineData("tagesuebersicht")]
    [InlineData("wochenuebersicht")]
    [InlineData("pruefungen")]
    [InlineData("befreiungen")]
    [InlineData("klassendienste")]
    public async Task Alle_Berichte_lassen_sich_erstellen(string art)
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var sid = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a").Id;
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-12", grund = "K" }));
        var r = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/{art}?klasse=7a&von=2026-10-12&bis=2026-10-14&n1=1&n2=2");
        Assert.NotEmpty(r["spalten"]!.AsArray());
        if (art is "fehlzeiten_schueler" or "fehltage_klasse" or "abwesenheiten" or "unentschuldigt")
            Assert.NotEmpty(r["zeilen"]!.AsArray());
        var csv = await leitung.GetAsync($"/api/app/berichte/erstellen/{art}?klasse=7a&von=2026-10-12&bis=2026-10-14&format=csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Mitteilungen_Entwurf_Liste_und_Anhang()
    {
        await using var s = await Testserver.StartenAsync();
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var idFis = s.Dienst<Benutzerdienst>().NachLogin("fis")!.Id;
        var liste = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/mitteilungen/listen", new { name = "Fachschaft", mitglieder = new[] { "benutzer:" + idFis } }));
        var lid = liste["id"]!.GetValue<int>();
        var entwurf = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new { empfaenger = new[] { "liste:" + lid }, betreff = "Termine", text = "Siehe Anhang", wichtig = false }));
        var id = entwurf["id"]!.GetValue<int>();
        Assert.Single((await Testserver.HolenAsync(sne, "/api/app/mitteilungen?ordner=entwuerfe")).AsArray());
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Hallo Anhang")), "datei", "termine.txt" } };
        var anhang = await Testserver.JsonAsync(await sne.PostAsync($"/api/app/mitteilungen/{id}/anhaenge", form));
        await Testserver.JsonAsync(await sne.PostAsync($"/api/app/mitteilungen/{id}/senden", new StringContent("{}", Encoding.UTF8, "application/json")));
        Assert.Empty((await Testserver.HolenAsync(sne, "/api/app/mitteilungen?ordner=entwuerfe")).AsArray());
        var eingang = (await Testserver.HolenAsync(fis, "/api/app/mitteilungen")).AsArray();
        var m = eingang.Single(x => x!["betreff"]!.GetValue<string>() == "Termine")!;
        Assert.Equal("termine.txt", m["anhaenge"]![0]!["name"]!.GetValue<string>());
        var datei = await fis.GetStringAsync($"/api/app/mitteilungen/anhang/{anhang["id"]!.GetValue<int>()}");
        Assert.Equal("Hallo Anhang", datei);
        // Unbeteiligte erhalten den Anhang nicht
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.NotFound, (await fremd.GetAsync($"/api/app/mitteilungen/anhang/{anhang["id"]!.GetValue<int>()}")).StatusCode);
    }

    [Fact]
    public async Task Konto_wechseln_auf_demselben_Geraet()
    {
        await using var s = await Testserver.StartenAsync();
        var bd = s.Dienst<Benutzerdienst>();
        bd.Anlegen("sne", "test-passwort", "lehrer", "SNE", "SNE");
        bd.Anlegen("fis", "test-passwort", "lehrer", "FIS", "FIS");
        var c = s.Client();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/anmelden", new { login = "sne", passwort = "test-passwort" }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/anmelden", new { login = "fis", passwort = "test-passwort" }));
        var konten = (await Testserver.HolenAsync(c, "/api/app/konten")).AsArray();
        Assert.Equal(2, konten.Count);
        var sneKonto = konten.Single(k => k!["login"]!.GetValue<string>() == "sne")!;
        Assert.False(sneKonto["aktuell"]!.GetValue<bool>());
        await Testserver.JsonAsync(await c.PostAsync($"/api/app/konten/{sneKonto["id"]!.GetValue<int>()}/wechseln", new StringContent("{}", Encoding.UTF8, "application/json")));
        var sitzung = await Testserver.HolenAsync(c, "/api/app/sitzung");
        Assert.Equal("sne", sitzung["benutzer"]!["login"]!.GetValue<string>());
        // anderes Gerät kennt die Konten nicht
        var anders = await s.AlsAsync("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await anders.PostAsync($"/api/app/konten/{sneKonto["id"]!.GetValue<int>()}/wechseln", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Freie_Tage_Einstellungen_und_Pruefungssperre()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/freietage", new { von = "2026-10-16", bis = "2026-10-16", art = "frei", name = "Pädagogischer Tag" }));
        var plan = await Testserver.HolenAsync(admin, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-16");
        var fr = plan["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == "2026-10-16")!;
        Assert.True(fr["frei"]!.GetValue<bool>());
        Assert.Equal("Pädagogischer Tag", fr["freiGrund"]!.GetValue<string>());
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["datum"]!.GetValue<string>() == "2026-10-16" && k["status"]!.GetValue<string>() == "regulaer");

        // Prüfungssperre: höchstens eine Prüfung je Klasse und Tag
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/einstellungen/pruefungssperren", new { aktiv = true, maxProTag = 1, maxProWoche = 9, leitungAusnehmen = false }));
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-15", stundeVon = 1, stundeBis = 2, klassen = new[] { "6b" }, titel = "Test 1" }));
        var zweite = await sne.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-15", stundeVon = 5, stundeBis = 6, klassen = new[] { "6b" }, titel = "Test 2" });
        Assert.Equal(HttpStatusCode.BadRequest, zweite.StatusCode);
        Assert.Contains("Prüfungssperre", await zweite.Content.ReadAsStringAsync());

        // Sicherheitsrichtlinie gilt für selbst gewählte Passwörter
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/einstellungen/passwort", new { minLaenge = 10, ziffern = true }));
        Assert.Equal(HttpStatusCode.BadRequest, (await sne.PostAsJsonAsync("/api/app/passwort", new { alt = "test-passwort", neu = "nurbuchstaben" })).StatusCode);
        await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/passwort", new { alt = "test-passwort", neu = "mitziffer123" }));

        // RSS nur, wenn freigegeben, und nur öffentliche Nachrichten
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client(false).GetAsync("/rss/nachrichten.xml")).StatusCode);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/einstellungen/ansicht", new { rss = true }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/nachrichten", new { von = "2026-10-14", bis = "2026-10-14", text = "Öffentlich", zielgruppe = "alle", startseite = true, monitor = true, oeffentlich = true }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/nachrichten", new { von = "2026-10-14", bis = "2026-10-14", text = "Intern", zielgruppe = "alle", startseite = true, monitor = true }));
        var rss = await s.Client(false).GetStringAsync("/rss/nachrichten.xml");
        Assert.Contains("Öffentlich", rss);
        Assert.DoesNotContain("Intern", rss);
    }

    [Fact]
    public async Task Benutzerimport_und_Massenaktionen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var csv = "Benutzername;Name;Rolle;Kürzel;E-Mail;Passwort\r\nmue;Hans Müller;Lehrkraft;MUE;mue@schule.de;\r\nklasse5a;Klasse 5a;Klasse;5a;;geheim1234\r\nfalsch;;Hausmeister;;;\r\n";
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "datei", "benutzer.csv" } };
        var pruefung = await Testserver.JsonAsync(await admin.PostAsync("/api/app/admin/import/pruefen", form));
        Assert.Equal(1, pruefung["fehler"]!.GetValue<int>());
        var gut = new JsonArray(pruefung["zeilen"]!.AsArray().Where(z => z!["fehler"] is null).Select(z => z!.DeepClone()).ToArray());
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/import/uebernehmen", new { zeilen = gut, ersetzen = false }));
        Assert.Equal(2, r["neu"]!.AsArray().Count);
        var bd = s.Dienst<Benutzerdienst>();
        Assert.Equal("schueler", bd.NachLogin("klasse5a")!.Rolle);
        Assert.True(bd.NachLogin("mue")!.PasswortAendern);

        var id = bd.NachLogin("mue")!.Id;
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/aktion", new { aktion = "sperren", ids = new[] { id } }));
        Assert.True(bd.Holen(id)!.Gesperrt);
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/aktion", new { aktion = "entsperren", ids = new[] { id } }));
        Assert.False(bd.Holen(id)!.Gesperrt);
        // Konten ohne Stammdatenbezug sperren
        bd.Anlegen("alt", "test-passwort", "lehrer", "QQQ");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/aktion", new { aktion = "inaktive_sperren" }));
        Assert.True(bd.NachLogin("alt")!.Gesperrt);
        Assert.False(bd.NachLogin("mue")!.Gesperrt);
    }

    [Fact]
    public async Task Stammdaten_Sitzplan_und_Verlauf()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        Assert.NotEmpty((await Testserver.HolenAsync(admin, "/api/app/stammdaten/klassen")).AsArray());
        Assert.NotEmpty((await Testserver.HolenAsync(admin, "/api/app/stammdaten/schueler")).AsArray());
        Assert.NotEmpty((await Testserver.HolenAsync(admin, "/api/app/stammdaten/versionen")).AsArray());
        Assert.NotEmpty((await Testserver.HolenAsync(admin, "/api/app/stammdaten/gruende")).AsArray());
        var lehrer = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/stammdaten/klassen")).StatusCode);

        var sitz = await Testserver.HolenAsync(admin, "/api/app/sitzplan/7a");
        var erster = sitz["schueler"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/sitzplan/7a", new { zeilen = 4, spalten = 5, belegung = new Dictionary<string, int> { ["1-2"] = erster, ["9-9"] = 1 } }));
        sitz = await Testserver.HolenAsync(admin, "/api/app/sitzplan/7a");
        Assert.Equal(4, sitz["plan"]!["zeilen"]!.GetValue<int>());
        Assert.Equal(erster, sitz["plan"]!["belegung"]!["1-2"]!.GetValue<int>());
        Assert.Null(sitz["plan"]!["belegung"]!["9-9"]);
    }
}
