using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Hausaufgaben: nach dem Fälligkeitstag automatisch erledigt; bearbeiten auch aus der Stunde, in der sie fällig ist.</summary>
public class HausaufgabenErledigtTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan) => plan["karten"]!.AsArray().Select(k => k!);

    private static string Lehrer(JsonNode karte) => karte["lehrer"]!.AsArray().First()!["k"]!.GetValue<string>();

    private static bool Normal(JsonNode k) =>
        k["unterricht"] is not null && k["status"]?.GetValue<string>() is "regulaer" or "geaendert" && k["lehrer"]!.AsArray().Count > 0 && Lehrer(k) != "";

    [Fact]
    public async Task Nach_dem_Faelligkeitstag_gilt_die_Hausaufgabe_als_erledigt()
    {
        await using var s = await Testserver.StartenAsync();
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var neu = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste",
            new { datum = "2026-10-14", stunde = 1, unterricht = 107, text = "Vokabeln", faellig = "2026-10-16", bemerkung = "" }));
        Assert.False(neu["erledigt"]!.GetValue<bool>());
        const string pfad = "/api/app/klassenbuch/hausaufgabenliste?datum=2026-10-14&stunde=1&unterricht=107";

        // am Fälligkeitstag noch offen
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 16, 12, 0, 0);
        Assert.False((await Testserver.HolenAsync(sne, pfad))["liste"]!.AsArray().Single()!["erledigt"]!.GetValue<bool>());
        // danach erledigt – ohne Schreibzugriff (zurück vor den Fälligkeitstag: wieder offen)
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 17, 8, 0, 0);
        Assert.True((await Testserver.HolenAsync(sne, pfad))["liste"]!.AsArray().Single()!["erledigt"]!.GetValue<bool>());
        s.Dienst<Uhr>().Fest = Testserver.Jetzt;
        Assert.False((await Testserver.HolenAsync(sne, pfad))["liste"]!.AsArray().Single()!["erledigt"]!.GetValue<bool>());
        // ausdrücklich erledigt bleibt erledigt
        var id = neu["id"]!.GetValue<int>();
        Assert.True((await Testserver.JsonAsync(await sne.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}", new { erledigt = true })))["erledigt"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Lehrkraft_der_faelligen_Stunde_darf_die_Hausaufgabe_bearbeiten()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        // Eine Stunde und eine spätere Stunde desselben Fachs in derselben Klasse bei einer anderen Lehrkraft
        var elemente = await Testserver.HolenAsync(admin, "/api/app/elemente");
        var klassen = elemente["klassen"]!.AsArray().Select(k => k!["kurz"]?.GetValue<string>() ?? k!["k"]?.GetValue<string>() ?? "").Where(k => k != "");
        (JsonNode a, JsonNode b)? gefunden = null;
        List<JsonNode> karten = new();
        foreach (var klasse in klassen)
        {
            karten = new();
            foreach (var woche in new[] { "2026-10-14", "2026-10-21" })
                karten.AddRange(Karten(await Testserver.HolenAsync(admin, $"/api/app/plan?typ=klasse&element={klasse}&datum={woche}")).Where(Normal));
            gefunden = (from x in karten.Where(k => string.CompareOrdinal(k["datum"]!.GetValue<string>(), "2026-10-14") >= 0)
                        from y in karten.Where(k => string.CompareOrdinal(k["datum"]!.GetValue<string>(), x["datum"]!.GetValue<string>()) > 0)
                        where x["fach"]!.GetValue<string>() == y["fach"]!.GetValue<string>() && Lehrer(x) != Lehrer(y)
                        select ((JsonNode, JsonNode)?)(x, y)).FirstOrDefault();
            if (gefunden is not null) break;
        }
        Assert.NotNull(gefunden);
        var paar = gefunden!.Value;
        var (aufgegeben, faellig) = paar;
        var setzer = await s.AlsAsync("setzer", "lehrer", Lehrer(aufgegeben));
        var neu = await Testserver.JsonAsync(await setzer.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new
        {
            datum = aufgegeben["datum"]!.GetValue<string>(), stunde = aufgegeben["von"]!.GetValue<int>(), unterricht = aufgegeben["unterricht"]!.GetValue<int>(),
            text = "Aufgabe", faellig = faellig["datum"]!.GetValue<string>(), bemerkung = ""
        }));
        var id = neu["id"]!.GetValue<int>();

        // Lehrkraft der fälligen Stunde: erledigt setzen und Text ändern geht, löschen nicht
        var spaeter = await s.AlsAsync("spaeter", "lehrer", Lehrer(faellig));
        var r = await spaeter.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}", new { erledigt = true, text = "Aufgabe (kontrolliert)" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var geaendert = await Testserver.JsonAsync(r);
        Assert.True(geaendert["erledigt"]!.GetValue<bool>());
        Assert.Equal("Aufgabe (kontrolliert)", geaendert["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await spaeter.DeleteAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}")).StatusCode);

        // Lehrkraft ohne eine der beiden Stunden: nicht erlaubt
        var andere = karten.Select(Lehrer).Distinct().FirstOrDefault(l => l != Lehrer(aufgegeben) && l != Lehrer(faellig)
            && !karten.Any(k => Lehrer(k) == l && k["fach"]!.GetValue<string>() == faellig["fach"]!.GetValue<string>()));
        if (andere is not null)
        {
            var fremd = await s.AlsAsync("fremd", "lehrer", andere);
            Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}", new { erledigt = false })).StatusCode);
        }
    }
}
