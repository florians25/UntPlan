using System.Net;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Tests;

/// <summary>UntPlanMobile gegen ein echtes UntPlanWeb (im Testprozess, Beispielpaket).</summary>
public class EndeZuEndeTests
{
    private static IEnumerable<JsonNode> Kacheln(JsonNode woche, string datum) => woche["days"]![datum]!.AsArray().Select(k => k!);

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_die_Anmeldemaske_und_401()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = u.Client();
        var seite = await c.GetStringAsync("/");
        Assert.Contains("\"needLogin\":true", seite);
        Assert.Contains("UntPlanMobile", seite);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/mobile?action=meta")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/mobile?action=week&date=2026-10-14")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/messages?action=list")).StatusCode);
    }

    [Fact]
    public async Task Falsches_Passwort_wird_abgewiesen()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = u.Client();
        var r = await c.PostAsync("/api/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["action"] = "login", ["username"] = "sne", ["password"] = "falsch" }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var j = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.False(j["ok"]!.GetValue<bool>());
        Assert.Contains("falsch", j["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anmeldung_Stammdaten_und_eigene_Woche()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = await u.AngemeldetAsync("sne");

        var meta = await Umgebung.HolenAsync(c, "/api/mobile?action=meta");
        Assert.True(meta["ok"]!.GetValue<bool>());
        Assert.Equal("SNE", meta["own"]!["label"]!.GetValue<string>());
        Assert.Equal("Julia Schneider", meta["own"]!["info"]!.GetValue<string>());
        Assert.Equal("teacher", meta["own"]!["kind"]!.GetValue<string>());
        Assert.False(meta["isStudent"]!.GetValue<bool>());
        var entities = meta["entities"]!.AsArray();
        Assert.Contains(entities, e => e!["type"]!.GetValue<string>() == "class" && e["id"]!.GetValue<string>() == "5a" && e["classTeacher"]!.GetValue<string>() == "FIS");
        Assert.Contains(entities, e => e!["type"]!.GetValue<string>() == "teacher" && e["id"]!.GetValue<string>() == "WAG");
        Assert.Contains(entities, e => e!["type"]!.GetValue<string>() == "room" && e["id"]!.GetValue<string>() == "R101");
        Assert.Equal("07:55", meta["lessonTimes"]!["1"]!["start"]!.GetValue<string>());

        var woche = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-14&type=mine");
        Assert.Equal("2026-10-12", woche["weekStart"]!.GetValue<string>());
        Assert.Equal("teacher", woche["kind"]!.GetValue<string>());
        Assert.Equal(5, woche["days"]!.AsObject().Count);
        // Mittwoch, 3. Stunde: Vertretung für WAG in der 7b
        var vertretung = Kacheln(woche, "2026-10-14").Single(k => k["lesson"]!.GetValue<int>() == 3 && k["blockType"]!.GetValue<string>() == "change");
        Assert.Equal("7b", vertretung["class"]!.GetValue<string>());
        Assert.Equal("SNE", vertretung["teacher"]!.GetValue<string>());
        Assert.Equal("WAG", vertretung["teacherOrig"]!.GetValue<string>());
        Assert.Contains("teacher", vertretung["changed"]!.AsArray().Select(x => x!.GetValue<string>()));
        // Donnerstag: Klausur, Freitag: offene Vertretung der eigenen Stunde und abgegebene Pausenaufsicht
        Assert.Contains(Kacheln(woche, "2026-10-15"), k => k["blockType"]!.GetValue<string>() == "klausur");
        Assert.Contains(Kacheln(woche, "2026-10-16"), k => k["blockType"]!.GetValue<string>() == "change" && k["openSub"]!.GetValue<bool>() && k["teacherOrig"]!.GetValue<string>() == "SNE");
        // offene Vertretung: Kennzeichen für die durchgestrichene Lehrkraft in der grünen Plakette
        Assert.Contains(Kacheln(woche, "2026-10-16"), k => k["openSub"]!.GetValue<bool>() && k["teacherOpen"]?.GetValue<bool>() == true);
        Assert.Contains(Kacheln(woche, "2026-10-16"), k => k["blockType"]!.GetValue<string>() == "supervision" && k["_supAbsent"]?.GetValue<bool>() == true);
        Assert.Contains(Kacheln(woche, "2026-10-14"), k => k["blockType"]!.GetValue<string>() == "absence" && k["subject"]!.GetValue<string>() == "Lehrerkonferenz");

        // Klassenplan mit Entfall (Projekttag) und Herbstferien in der Woche danach
        var klasse = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-14&type=class&id=5a");
        Assert.Equal("class", klasse["kind"]!.GetValue<string>());
        // Freisetzung (Klasse abwesend) ist Entfall mit Hinweis „Klasse abwesend“
        Assert.Contains(Kacheln(klasse, "2026-10-15"), k => k["blockType"]!.GetValue<string>() == "cancelled" && (k["info"]?.GetValue<string>() ?? "").StartsWith("Klasse abwesend"));
        Assert.Contains(Kacheln(klasse, "2026-10-15"), k => k["blockType"]!.GetValue<string>() == "absence" && k["subject"]!.GetValue<string>() == "Projekttag Medien");
        var ferien = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-21&type=class&id=5a");
        var feld = Kacheln(ferien, "2026-10-21").Single();
        Assert.Equal("holiday", feld["blockType"]!.GetValue<string>());
        Assert.Equal("Herbst­ferien", feld["holidayName"]!.GetValue<string>());
        Assert.Equal("17.10. bis 31.10.", feld["holidayRange"]!.GetValue<string>());

        // Die Hülle bringt die Woche gleich mit
        var seite = await c.GetStringAsync("/");
        Assert.Contains("window.TT_BOOT = {\"ok\":true", seite);
        Assert.Contains("\"weekStart\":\"2026-10-12\"", seite);
        Assert.DoesNotContain("__BOOT__", seite);
        Assert.DoesNotContain("<!--SYMBOLE-->", seite);
    }

    [Fact]
    public async Task Klassenkonto_sieht_nur_die_eigene_Klasse()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = await u.AngemeldetAsync("5a");
        var meta = await Umgebung.HolenAsync(c, "/api/mobile?action=meta");
        Assert.True(meta["isStudent"]!.GetValue<bool>());
        Assert.Equal("class", meta["own"]!["kind"]!.GetValue<string>());
        Assert.DoesNotContain(meta["entities"]!.AsArray(), e => e!["type"]!.GetValue<string>() == "teacher");
        var woche = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-14");
        Assert.Equal("class", woche["kind"]!.GetValue<string>());
        Assert.NotEmpty(Kacheln(woche, "2026-10-14"));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/mobile?action=week&date=2026-10-14&type=teacher&id=WAG")).StatusCode);
    }

    [Fact]
    public async Task Klassenbuch_Lehrstoff_Hausaufgabe_und_Anwesenheit()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = await u.AngemeldetAsync("sne");
        // SNE: Mittwoch 1. Stunde Englisch in der 5a (Unterricht 107)
        var schluessel = new Dictionary<string, string> { ["date"] = "2026-10-14", ["lesson"] = "1", ["class"] = "5a", ["unterricht"] = "107" };
        string Abfrage(string aktion) => "/api/classbook?" + string.Join("&", schluessel.Append(new("action", aktion)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        Dictionary<string, string> Mit(params (string, string)[] werte) => schluessel.Concat(werte.Select(w => new KeyValuePair<string, string>(w.Item1, w.Item2))).ToDictionary(x => x.Key, x => x.Value);

        var alles = await Umgebung.HolenAsync(c, Abfrage("all"));
        Assert.True(alles["ok"]!.GetValue<bool>());
        var schueler = alles["students"]!.AsArray();
        Assert.NotEmpty(schueler);
        Assert.All(schueler, s => Assert.Equal("present", s!["state"]!.GetValue<string>()));

        await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "save"), ("field", "lehrstoff"), ("value", "Unit 3: Present progressive")));
        var hausaufgabe = await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "addItem"), ("field", "homework"), ("value", "{\"text\":\"Workbook S. 18\",\"due\":\"2026-10-16\"}")));
        Assert.Equal("Workbook S. 18", hausaufgabe["data"]![0]!["text"]!.GetValue<string>());

        var id = schueler[0]!["id"]!.GetValue<int>();
        var zweiter = schueler[1]!["id"]!.GetValue<int>();
        await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "attendance"), ("student_id", id.ToString()), ("state", "absent"), ("note", "krank")));
        await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "attendance"), ("student_id", zweiter.ToString()), ("state", "late"), ("arrival", "08:05")));

        alles = await Umgebung.HolenAsync(c, Abfrage("all"));
        Assert.Equal("Unit 3: Present progressive", alles["data"]!["lehrstoff"]!.GetValue<string>());
        Assert.Equal("2026-10-16", alles["data"]!["homework"]![0]!["due"]!.GetValue<string>());
        Assert.True(alles["checked"]!.GetValue<bool>());
        Assert.Equal(1, alles["absent"]!.GetValue<int>());
        var liste = alles["students"]!.AsArray();
        Assert.Equal("absent", liste.Single(s => s!["id"]!.GetValue<int>() == id)!["state"]!.GetValue<string>());
        Assert.Equal("krank", liste.Single(s => s!["id"]!.GetValue<int>() == id)!["note"]!.GetValue<string>());
        var spaet = liste.Single(s => s!["id"]!.GetValue<int>() == zweiter)!;
        Assert.Equal("late", spaet["state"]!.GetValue<string>());
        Assert.Equal("08:05", spaet["arrival"]!.GetValue<string>());

        // Klassenbucheintrag und Bemerkung (Vermerke in UntPlanWeb)
        var arten = await Umgebung.HolenAsync(c, Abfrage("remarkTypes"));
        Assert.Contains(arten["types"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "Lob");
        var eintrag = await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "addItem"), ("field", "classbook"), ("value", "{\"text\":\"Tafeldienst vergessen\",\"cat\":\"Vermerk\"}")));
        Assert.Equal("Tafeldienst vergessen", eintrag["data"]![0]!["text"]!.GetValue<string>());
        var bemerkung = await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "remarkSave"), ("student_id", id.ToString()), ("id", "0"), ("type", "Lob"), ("text", "Sehr gute Mitarbeit")));
        Assert.Equal("Sehr gute Mitarbeit", bemerkung["remarks"]![id.ToString()]![0]!["text"]!.GetValue<string>());
        var geloescht = await Umgebung.SendenAsync(c, "/api/classbook", Mit(("action", "delItem"), ("field", "classbook"), ("index", "0")));
        Assert.Empty(geloescht["data"]!.AsArray());

        // Fremde Stunde: UntPlanWeb lehnt ab, die Meldung kommt durch
        var r = await c.PostAsync("/api/classbook", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["action"] = "save", ["field"] = "lehrstoff", ["value"] = "x", ["date"] = "2026-10-14", ["lesson"] = "2", ["class"] = "5a", ["unterricht"] = "106" }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Notizen_fuer_Schueler_und_Raumbuchung_aus_den_Stundendetails()
    {
        await using var u = await Umgebung.StartenAsync();
        var sne = await u.AngemeldetAsync("sne");
        var schluessel = new Dictionary<string, string> { ["date"] = "2026-10-14", ["lesson"] = "1", ["class"] = "5a", ["unterricht"] = "107" };
        Dictionary<string, string> Mit(params (string, string)[] werte) => schluessel.Concat(werte.Select(w => new KeyValuePair<string, string>(w.Item1, w.Item2))).ToDictionary(x => x.Key, x => x.Value);

        // Notizen für Schüler*innen: Lehrkraft trägt ein, die Klasse sieht sie (Info-Zeichen auf der Kachel)
        await Umgebung.SendenAsync(sne, "/api/classbook", Mit(("action", "save"), ("field", "notes_students"), ("value", "Bitte Wörterbuch mitbringen")));
        var alles = await Umgebung.HolenAsync(sne, "/api/classbook?" + string.Join("&", schluessel.Select(p => $"{p.Key}={p.Value}")) + "&action=all");
        Assert.Equal("Bitte Wörterbuch mitbringen", alles["data"]!["notes_students"]!.GetValue<string>());
        var klasse = await u.AngemeldetAsync("5a");
        var woche = await Umgebung.HolenAsync(klasse, "/api/mobile?action=week&date=2026-10-14");
        var kachel = Kacheln(woche, "2026-10-14").First(k => k["lesson"]!.GetValue<int>() == 1 && k["unterricht"]?.GetValue<int>() == 107);
        Assert.True(kachel["hasNote"]!.GetValue<bool>());
        Assert.Equal("Bitte Wörterbuch mitbringen", kachel["noteStudents"]!.GetValue<string>());
        Assert.Null(kachel["noteTeachers"]);

        // Raum buchen: freie Räume der Stunde, Buchung erscheint sofort als Raumänderung
        var frei = await Umgebung.HolenAsync(sne, "/api/rooms?action=free&date=2026-10-14&from=1&to=1");
        var raum = frei["rooms"]!.AsArray().First()!["code"]!.GetValue<string>();
        var gebucht = await Umgebung.SendenAsync(sne, "/api/rooms", new() { ["action"] = "book", ["date"] = "2026-10-14", ["from"] = "1", ["to"] = "1", ["room"] = raum, ["class"] = "5a", ["subject"] = "E" });
        Assert.True(gebucht["ok"]!.GetValue<bool>());
        woche = await Umgebung.HolenAsync(sne, "/api/mobile?action=week&date=2026-10-14&type=mine");
        var neu = Kacheln(woche, "2026-10-14").First(k => k["lesson"]!.GetValue<int>() == 1 && k["unterricht"]?.GetValue<int>() == 107);
        Assert.Equal(raum, neu["room"]!.GetValue<string>());
        Assert.Contains("room", neu["changed"]!.AsArray().Select(x => x!.GetValue<string>()));
        // Klassenkonten können keine Räume buchen
        Assert.Equal(HttpStatusCode.Forbidden, (await klasse.GetAsync("/api/rooms?action=free&date=2026-10-14&from=1&to=1")).StatusCode);
    }

    [Fact]
    public async Task Startseite_Mitteilungen_und_Abmelden()
    {
        await using var u = await Umgebung.StartenAsync();
        var fis = await u.AngemeldetAsync("fis");
        var sne = await u.AngemeldetAsync("sne");

        var start = await Umgebung.HolenAsync(fis, "/api/home?action=home");
        Assert.Equal("Städtisches Beispiel-Gymnasium", start["school"]!.GetValue<string>());
        Assert.Contains(start["classes"]!.AsArray(), k => k!["id"]!.GetValue<string>() == "5a");
        var termine = await Umgebung.HolenAsync(sne, "/api/home?action=exams");
        Assert.Contains(termine["items"]!.AsArray(), t => t!["subject"]!.GetValue<string>() == "Mathematik-Klausur" && t["art"]!.GetValue<string>() == "Klausur");

        var empfaenger = await Umgebung.HolenAsync(fis, "/api/messages?action=recipients&q=Schneider");
        var token = empfaenger["items"]!.AsArray().Single()!["token"]!.GetValue<string>();
        await Umgebung.SendenAsync(fis, "/api/messages", new() { ["action"] = "send", ["subject"] = "Projekttag", ["body"] = "Kannst du übernehmen?", ["recipients"] = $"[\"{token}\"]" });

        var eingang = await Umgebung.HolenAsync(sne, "/api/messages?action=list&box=inbox");
        var m = eingang["items"]!.AsArray().Single()!;
        Assert.Equal("Michael Fischer", m["sender"]!.GetValue<string>());
        Assert.Equal("Projekttag", m["subject"]!.GetValue<string>());
        Assert.False(m["read"]!.GetValue<bool>());
        var detail = await Umgebung.HolenAsync(sne, $"/api/messages?action=detail&id={m["id"]}");
        Assert.Equal("Kannst du übernehmen?", detail["item"]!["body"]!.GetValue<string>());
        eingang = await Umgebung.HolenAsync(sne, "/api/messages?action=list&box=inbox");
        Assert.True(eingang["items"]![0]!["read"]!.GetValue<bool>());
        var gesendet = await Umgebung.HolenAsync(fis, "/api/messages?action=list&box=sent");
        Assert.Equal("Julia Schneider", gesendet["items"]![0]!["recipients"]![0]!["label"]!.GetValue<string>());

        await Umgebung.SendenAsync(sne, "/api/login", new() { ["action"] = "logout" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await sne.GetAsync("/api/mobile?action=meta")).StatusCode);
    }

    [Fact]
    public async Task Service_Worker_und_Manifest_tragen_die_Fassung()
    {
        await using var u = await Umgebung.StartenAsync();
        var c = u.Client();
        var sw = await c.GetAsync("/sw.js");
        Assert.Equal("no-cache", sw.Headers.CacheControl?.ToString());
        var text = await sw.Content.ReadAsStringAsync();
        Assert.Contains("const VERSION = 'untplanmobile-1.0.0-", text);
        Assert.DoesNotContain("__FASSUNG__", text);
        var manifest = await Umgebung.HolenAsync(c, "/manifest.webmanifest");
        Assert.Equal("UntPlanMobile", manifest["name"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/assets/js/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/gesundheit")).StatusCode);
    }
}
