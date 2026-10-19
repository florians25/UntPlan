using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenbucheinträge im gekoppelten Unterricht (getrennte Unterrichte 9001/5a und 9002/5b bei MUE, INF, EDV1): Lehrstoff und Hausaufgaben
/// gelten für die ganze Kopplung – gleich, aus welchem Plan die Stunde geöffnet bzw. gespeichert wird – und erscheinen in jeder Klasse genau
/// einmal (Stundendetails, Wochenansicht, Verlauf, offene Stunden). Dazu die Seite „Klassenbucheinträge“ (Vermerke) mit gekoppelten Klassen.
/// </summary>
public class KopplungKlassenbuchTests
{
    private const string Datum = "2026-10-14";

    private static async Task<Testserver> StartenAsync()
    {
        var s = await Testserver.StartenAsync(mitPaket: false);
        var paket = Testserver.Beispielpaket();
        var periode = paket["perioden"]![0]!["unterricht"]!.AsArray();
        JsonObject Zeile(string fach, string lehrer, string klasse) => new() { ["fach"] = fach, ["lehrer"] = lehrer, ["klassen"] = new JsonArray(klasse), ["gruppe"] = "" };
        JsonObject Unterricht(int id, params JsonObject[] zeilen) => new() { ["id"] = id, ["wochenstunden"] = 1, ["zeilen"] = new JsonArray(zeilen), ["wochen"] = "", ["kurs"] = "", ["text"] = "" };
        periode.Add(Unterricht(9001, Zeile("INF", "MUE", "5a")));
        periode.Add(Unterricht(9002, Zeile("INF", "MUE", "5b")));
        var tag = paket["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == Datum)!["stunden"]!.AsArray();
        JsonObject Stunde(int nr, int u, string klasse) => new()
        {
            ["stunde"] = nr, ["unterricht"] = u, ["fach"] = "INF", ["klassen"] = new JsonArray(klasse), ["lehrer"] = new JsonArray("MUE"),
            ["raeume"] = new JsonArray("EDV1"), ["status"] = "regulaer", ["text"] = "",
        };
        tag.Add(Stunde(9, 9001, "5a"));
        tag.Add(Stunde(9, 9002, "5b"));
        var r = await s.PaketSendenAsync(paket);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return s;
    }

    private static async Task<JsonNode?> EintragAsync(HttpClient c, int unterricht) =>
        (await Testserver.HolenAsync(c, $"/api/app/klassenbuch/stunde?datum={Datum}&stunde=9&unterricht={unterricht}"))["eintrag"];

    private static async Task<JsonNode> WocheStundeAsync(HttpClient c, string klasse) =>
        (await Testserver.HolenAsync(c, $"/api/app/klassenbuch/woche?klasse={klasse}&datum={Datum}"))["tage"]!.AsArray()
            .Single(t => t!["datum"]!.GetValue<string>() == Datum)!["stunden"]!.AsArray().Single(x => x!["von"]!.GetValue<int>() == 9)!;

    [Fact]
    public async Task Lehrstoff_und_Hausaufgaben_gelten_fuer_die_ganze_Kopplung()
    {
        await using var s = await StartenAsync();
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        // aus dem Plan der 5a speichern
        var r = await mue.PostAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = Datum, stunde = 9, unterricht = 9001, thema = "Schleifen", notiz = "" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        r = await mue.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = Datum, stunde = 9, unterricht = 9002, text = "Aufgabe 3", faellig = "2026-10-21" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        // Abwesenheitskontrolle aus dem Plan der 5b (legt die Kontrolle für beide Unterrichte ab)
        r = await mue.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = Datum, stunde = 9, unterricht = 9002, eintraege = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        foreach (var u in new[] { 9001, 9002 })
        {
            // Stundendetails aus beiden Plänen: derselbe Lehrstoff, dieselbe Hausaufgabe (genau einmal)
            Assert.Equal("Schleifen", (await EintragAsync(mue, u))?["thema"]?.GetValue<string>());
            var hw = await Testserver.HolenAsync(mue, $"/api/app/klassenbuch/hausaufgabenliste?datum={Datum}&stunde=9&unterricht={u}&fach=INF&klassen=5a,5b");
            Assert.Single(hw["liste"]!.AsArray(), h => h!["text"]!.GetValue<string>() == "Aufgabe 3");
        }
        foreach (var klasse in new[] { "5a", "5b" })
        {
            // Wochenansicht des Klassenbuchs: Lehrstoff, Hausaufgabe und Kontrolle in beiden Klassen
            var w = await WocheStundeAsync(mue, klasse);
            Assert.Equal("Schleifen", w["thema"]!.GetValue<string>());
            Assert.Contains("Aufgabe 3", w["hausaufgaben"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.NotNull(w["geprueft"]?.GetValue<string>());
            // Verlauf: die Stunde genau einmal
            var verlauf = (await Testserver.HolenAsync(mue, $"/api/app/klassenbuch/klasse?klasse={klasse}&von={Datum}&bis={Datum}")).AsArray()
                .Where(e => e!["stunde"]!.GetValue<int>() == 9).ToList();
            var e = Assert.Single(verlauf);
            Assert.Equal("Schleifen", e!["thema"]!.GetValue<string>());
        }

        // aus dem Plan der 5b ändern: derselbe Eintrag (kein zweiter Lehrstoff unter dem anderen Unterricht)
        r = await mue.PostAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = Datum, stunde = 9, unterricht = 9002, thema = "Schleifen und Bedingungen" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Schleifen und Bedingungen", (await EintragAsync(mue, 9001))?["thema"]?.GetValue<string>());
        Assert.Equal("Schleifen und Bedingungen", (await EintragAsync(mue, 9002))?["thema"]?.GetValue<string>());
        Assert.Single((await Testserver.HolenAsync(mue, $"/api/app/klassenbuch/klasse?klasse=5b&von={Datum}&bis={Datum}")).AsArray(),
            x => x!["stunde"]!.GetValue<int>() == 9);
    }

    [Fact]
    public async Task Klassenbucheintraege_der_gekoppelten_Klassen_in_der_Liste()
    {
        await using var s = await StartenAsync();
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        var anw = await Testserver.HolenAsync(mue, $"/api/app/klassenbuch/anwesenheit?datum={Datum}&stunde=9&unterricht=9002");
        var a = anw["schueler"]!.AsArray().First(x => x!["klasse"]!.GetValue<string>() == "5a")!;
        var b = anw["schueler"]!.AsArray().First(x => x!["klasse"]!.GetValue<string>() == "5b")!;
        foreach (var x in new[] { a, b })
        {
            var r = await mue.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = Datum, klasse = x["klasse"]!.GetValue<string>(), schuelerId = x["id"]!.GetValue<int>(), art = "Lob", text = "Gut präsentiert", zeit = "15:20" });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        var liste = (await Testserver.HolenAsync(mue, $"/api/app/klassenbucheintraege?von={Datum}&bis={Datum}"))["eintraege"]!.AsArray();
        Assert.Equal(new[] { "5a", "5b" }, liste.Select(x => x!["klasse"]!.GetValue<string>()).Order());
        Assert.All(liste, x => Assert.True(x!["bearbeiten"]!.GetValue<bool>()));
        var nur5b = (await Testserver.HolenAsync(mue, $"/api/app/klassenbucheintraege?von={Datum}&bis={Datum}&klasse=5b"))["eintraege"]!.AsArray();
        Assert.Equal("5b", Assert.Single(nur5b)!["klasse"]!.GetValue<string>());
    }
}
