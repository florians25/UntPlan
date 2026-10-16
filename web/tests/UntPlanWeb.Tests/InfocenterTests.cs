using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>Infocenter: Infos der Schule mit Zielgruppe und Gültigkeit, Neuigkeiten, Gelesen-Vermerk je Benutzer, Rechteprüfung.</summary>
public class InfocenterTests
{
    private static List<JsonNode> Eintraege(JsonNode n) => n["eintraege"]!.AsArray().Select(x => x!).ToList();
    private static List<JsonNode> Infos(JsonNode n) => Eintraege(n).Where(x => x["art"]!.GetValue<string>() == "info").ToList();

    [Fact]
    public async Task Infos_anlegen_lesen_und_verwalten()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("lehrer1", "lehrer", "BEC");
        var schueler = await s.AlsAsync("schueler1", "schueler");
        var eltern = await s.AlsAsync("eltern1", "eltern");

        // Neuigkeiten zu UntPlanWeb stehen allen im Infocenter (Kategorie „Update“)
        var start = await Testserver.HolenAsync(lehrer, "/api/app/infocenter");
        Assert.Contains(Eintraege(start), x => x["art"]!.GetValue<string>() == "neuigkeit" && x["kategorie"]!.GetValue<string>() == "Update");
        Assert.False(start["darfVerwalten"]!.GetValue<bool>());
        Assert.True((await Testserver.HolenAsync(admin, "/api/app/infocenter"))["darfVerwalten"]!.GetValue<bool>());

        // nur Administration/Schulleitung verwalten
        var verboten = await lehrer.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "X", text = "", zielgruppe = "alle" });
        Assert.Equal(HttpStatusCode.Forbidden, verboten.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/infocenter/infos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync("/api/app/infocenter")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = " ", zielgruppe = "alle" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "A", zielgruppe = "hausmeister" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "A", von = "2026-10-20", bis = "2026-10-10" })).StatusCode);

        var konferenz = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/infocenter/infos",
            new { titel = "Lehrerkonferenz", text = "Donnerstag 15 Uhr, Aula", zielgruppe = "kollegium", wichtig = true }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "Schulfest", text = "Am Freitag", zielgruppe = "alle", von = "2026-10-01", bis = "2026-10-31" }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "Elternabend", text = "", zielgruppe = "eltern" }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "Abgelaufen", text = "", zielgruppe = "alle", bis = "2026-10-01" }));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/infocenter/infos", new { titel = "Später", text = "", zielgruppe = "alle", von = "2026-11-01" }));
        Assert.Equal(5, (await Testserver.HolenAsync(admin, "/api/app/infocenter/infos")).AsArray().Count);

        // Zielgruppen und Gültigkeit (feste Uhr: 14.10.2026); wichtige ungelesene Infos zuerst
        var l = await Testserver.HolenAsync(lehrer, "/api/app/infocenter");
        Assert.Equal(new[] { "Lehrerkonferenz", "Schulfest" }, Infos(l).Select(x => x["titel"]!.GetValue<string>()));
        Assert.Equal("Wichtig", Infos(l)[0]["kategorie"]!.GetValue<string>());
        Assert.Equal(new[] { "Schulfest" }, Infos(await Testserver.HolenAsync(schueler, "/api/app/infocenter")).Select(x => x["titel"]!.GetValue<string>()));
        Assert.Equal(new[] { "Schulfest", "Elternabend" }, Infos(await Testserver.HolenAsync(eltern, "/api/app/infocenter")).Select(x => x["titel"]!.GetValue<string>()).Order().Reverse());

        // gelesen je Benutzer: einzelne bzw. alle
        var ungelesen = l["ungelesen"]!.GetValue<int>();
        var schluessel = Infos(l)[0]["schluessel"]!.GetValue<string>();
        await Testserver.JsonAsync(await lehrer.PostAsJsonAsync("/api/app/infocenter/gelesen", new { schluessel = new[] { schluessel, "i:999999" } }));
        var l2 = await Testserver.HolenAsync(lehrer, "/api/app/infocenter");
        Assert.Equal(ungelesen - 1, l2["ungelesen"]!.GetValue<int>());
        Assert.True(Infos(l2).Single(x => x["schluessel"]!.GetValue<string>() == schluessel)["gelesen"]!.GetValue<bool>());
        await Testserver.JsonAsync(await lehrer.PostAsJsonAsync("/api/app/infocenter/gelesen", new { alle = true }));
        Assert.Equal(0, (await Testserver.HolenAsync(lehrer, "/api/app/infocenter"))["ungelesen"]!.GetValue<int>());
        // andere Benutzer unberührt
        Assert.True((await Testserver.HolenAsync(schueler, "/api/app/infocenter"))["ungelesen"]!.GetValue<int>() > 0);

        // Ändern: wieder ungelesen; Löschen: verschwindet
        var id = konferenz["id"]!.GetValue<int>();
        var geaendert = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/infocenter/infos/{id}", new { titel = "Lehrerkonferenz verschoben", text = "Freitag", zielgruppe = "kollegium", wichtig = true }));
        Assert.Equal("Lehrerkonferenz verschoben", geaendert["titel"]!.GetValue<string>());
        var l3 = await Testserver.HolenAsync(lehrer, "/api/app/infocenter");
        Assert.Equal(1, l3["ungelesen"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.DeleteAsync($"/api/app/infocenter/infos/{id}")).StatusCode);
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/infocenter/infos/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/app/infocenter/infos/{id}")).StatusCode);
        Assert.DoesNotContain(Infos(await Testserver.HolenAsync(lehrer, "/api/app/infocenter")), x => x["id"]?.GetValue<int>() == id);
    }
}
