using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>Stundendetails wie im Original: Notizen auf den Karten, Hausaufgabenliste, Abwesenheiten verlängern, Lehrkraft abwesend.</summary>
public class StundendetailsTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    [Fact]
    public async Task Lehrkraft_abwesend_ist_keine_Entfallkarte_echter_Entfall_bleibt_Entfall()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        var hof = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=HOF&datum=2026-10-12");
        // Montag: HOF ist abwesend, die Stunden werden vertreten → „abwesend“ (gestrichelt), nicht „entfall“
        var montag = Karten(hof, "2026-10-12", 3).Single();
        Assert.Equal("abwesend", montag["status"]!.GetValue<string>());
        Assert.Contains(montag["lehrer"]!.AsArray(), l => l!["alt"]?.GetValue<string>() == "HOF" || l!["k"]!.GetValue<string>() == "HOF");
        // Dienstag: EF fällt wirklich aus → Entfall
        Assert.Equal("entfall", Karten(hof, "2026-10-13", 2).Single()["status"]!.GetValue<string>());
        // im Klassenplan ist die vertretene Stunde eine Änderung
        var klasse = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-12");
        Assert.Equal("geaendert", Karten(klasse, "2026-10-12", 3).Single()["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Notizen_stehen_auf_der_Karte_Lehrernotiz_nur_fuers_Kollegium()
    {
        await using var s = await Testserver.StartenAsync();
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var r = await sne.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = 1, unterricht = 107, thema = "Unit 3", hausaufgabe = "", faellig = (string?)null, notiz = "Beamer defekt", notizSchueler = "Wörterbuch mitbringen" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var kollegium = await Testserver.HolenAsync(sne, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-14");
        var k = Karten(kollegium, "2026-10-14", 1).Single(x => x["unterricht"]?.GetValue<int>() == 107);
        Assert.Equal("Wörterbuch mitbringen", k["notizSchueler"]!.GetValue<string>());
        Assert.Equal("Beamer defekt", k["notizLehrer"]!.GetValue<string>());

        var klasse = await s.AlsAsync("klasse-5a", "schueler", "5a");
        var plan = await Testserver.HolenAsync(klasse, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-14");
        var ks = Karten(plan, "2026-10-14", 1).Single(x => x["unterricht"]?.GetValue<int>() == 107);
        Assert.Equal("Wörterbuch mitbringen", ks["notizSchueler"]!.GetValue<string>());
        Assert.Equal("", ks["notizLehrer"]!.GetValue<string>());
        // Startseite (Heute) trägt die Notizen ebenfalls
        var heute = await Testserver.HolenAsync(klasse, "/api/app/heute");
        Assert.Contains(heute["karten"]!.AsArray(), x => x!["notizSchueler"]!.GetValue<string>() == "Wörterbuch mitbringen");
    }

    [Fact]
    public async Task Hausaufgabenliste_mit_Faelligkeit_und_Zeichen_in_der_faelligen_Stunde()
    {
        await using var s = await Testserver.StartenAsync();
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        // ohne Fälligkeit: eine Woche später (wie im Original)
        var a = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = "2026-10-14", stunde = 1, unterricht = 107, text = "Workbook S. 18", faellig = (string?)null, bemerkung = "" }));
        Assert.Equal("2026-10-21", a["faellig"]!.GetValue<string>());
        var b = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = "2026-10-14", stunde = 1, unterricht = 107, text = "Vokabeln Unit 3", faellig = "2026-10-16", bemerkung = "schriftlich" }));
        // vor der Stunde fällig geht nicht
        Assert.Equal(HttpStatusCode.BadRequest, (await sne.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = "2026-10-14", stunde = 1, unterricht = 107, text = "x", faellig = "2026-10-13", bemerkung = "" })).StatusCode);

        var liste = await Testserver.HolenAsync(sne, "/api/app/klassenbuch/hausaufgabenliste?datum=2026-10-14&stunde=1&unterricht=107");
        Assert.Equal(2, liste["liste"]!.AsArray().Count);
        Assert.True(liste["darf"]!.GetValue<bool>());
        // Feld „Hausaufgabe“ des Klassenbuchs (Listen, Export) bleibt angeglichen
        var stunde = await Testserver.HolenAsync(sne, "/api/app/klassenbuch/stunde?datum=2026-10-14&stunde=1&unterricht=107");
        Assert.Equal("Workbook S. 18\nVokabeln Unit 3", stunde["eintrag"]!["hausaufgabe"]!.GetValue<string>());
        Assert.Equal("2026-10-16", stunde["eintrag"]!["faellig"]!.GetValue<string>());

        // Freitag, 5. Stunde Englisch in der 5a: Hausaufgabe fällig (Zeichen auf der Karte, Liste in den Details)
        var plan = await Testserver.HolenAsync(sne, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-16");
        var freitag = Karten(plan, "2026-10-16", 5).Single(k => k["fach"]!.GetValue<string>() == "E");
        Assert.True(freitag["hausaufgabeFaellig"]!.GetValue<bool>());
        Assert.False(Karten(plan, "2026-10-16", 1).First()["hausaufgabeFaellig"]!.GetValue<bool>());
        var faellig = await Testserver.HolenAsync(sne, $"/api/app/klassenbuch/hausaufgabenliste?datum=2026-10-16&stunde=5&unterricht={freitag["unterricht"]}&fach=E&klassen=5a");
        Assert.Equal("Vokabeln Unit 3", faellig["faellig"]!.AsArray().Single()!["text"]!.GetValue<string>());

        // erledigt, ändern, löschen
        var id = b["id"]!.GetValue<int>();
        var geaendert = await Testserver.JsonAsync(await sne.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}", new { erledigt = true, text = "Vokabeln Unit 3 und 4" }));
        Assert.True(geaendert["erledigt"]!.GetValue<bool>());
        Assert.Equal("schriftlich", geaendert["bemerkung"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await sne.DeleteAsync($"/api/app/klassenbuch/hausaufgabenliste/{a["id"]}")).StatusCode);
        stunde = await Testserver.HolenAsync(sne, "/api/app/klassenbuch/stunde?datum=2026-10-14&stunde=1&unterricht=107");
        Assert.Equal("Vokabeln Unit 3 und 4", stunde["eintrag"]!["hausaufgabe"]!.GetValue<string>());

        // fremde Lehrkraft darf nicht schreiben
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        Assert.Equal(HttpStatusCode.Forbidden, (await fis.DeleteAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}")).StatusCode);
    }

    [Fact]
    public async Task Abwesenheit_aus_der_Vorstunde_verlaengern_und_bis_Tagesbeginn()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var schueler = (await Testserver.HolenAsync(admin, "/api/app/klassenbuch/schueler?klasse=5a")).AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        var plan = await Testserver.HolenAsync(admin, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-14");
        var zweite = Karten(plan, "2026-10-14", 2).First(k => k["unterricht"] != null);
        var u = zweite["unterricht"]!.GetValue<int>();

        // Schüler A fehlt in der 1. Stunde (Krankheit)
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = schueler[0], von = "2026-10-14", bis = "2026-10-14", stundeVon = 1, stundeBis = 1, grund = "K", status = "offen", bemerkung = "krank" }));
        var anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum=2026-10-14&stunde=2&unterricht={u}");
        Assert.Equal(schueler[0], anw["vorstunden"]!.AsArray().Single()!["schuelerId"]!.GetValue<int>());

        // Verlängern: in die 2. Stunde übernehmen (Grund und Text bleiben)
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/verlaengern", new { datum = "2026-10-14", stunde = 2, unterricht = u, schuelerIds = new[] { schueler[0] }, bis = "stunde" }));
        anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum=2026-10-14&stunde=2&unterricht={u}");
        var f = anw["fehlzeiten"]!.AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == schueler[0])!;
        Assert.Equal("K", f["grund"]!.GetValue<string>());
        Assert.Equal("krank", f["bemerkung"]!.GetValue<string>());
        Assert.Empty(anw["vorstunden"]!.AsArray());
        Assert.NotNull(anw["geprueft"]?.GetValue<string>());

        // Schüler B fehlt ab der 2. Stunde – auf den Tagesbeginn verlängern
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = "2026-10-14", stunde = 2, unterricht = u,
            eintraege = new object[] { new { schuelerId = schueler[0], art = "fehlt", minuten = 0, bemerkung = "krank" }, new { schuelerId = schueler[1], art = "fehlt", minuten = 0, bemerkung = "", grund = "A" } } }));
        // Grund bleibt beim erneuten Speichern erhalten
        anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum=2026-10-14&stunde=2&unterricht={u}");
        Assert.Equal("K", anw["fehlzeiten"]!.AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == schueler[0])!["grund"]!.GetValue<string>());
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/verlaengern", new { datum = "2026-10-14", stunde = 2, unterricht = u, schuelerIds = new[] { schueler[1] }, bis = "tagesbeginn" }));
        var fehlzeiten = await Testserver.HolenAsync(admin, "/api/app/klassenbuch/fehlzeiten?klasse=5a&von=2026-10-14&bis=2026-10-14");
        Assert.Contains(fehlzeiten["eintraege"]!.AsArray(), x => x!["schuelerId"]!.GetValue<int>() == schueler[1] && x["stunde"]!.GetValue<int>() == 1 && x["grund"]!.GetValue<string>() == "A");
    }
}
