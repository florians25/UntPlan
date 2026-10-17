using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlanStudio.Tests;

public sealed class DokumentTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;

    public DokumentTests(StudioFabrik f) => _f = f;

    [Fact]
    public async Task UpHochladenOeffnenUndHerunterladen()
    {
        var c = await _f.AngemeldetAsync();
        var d = Demo.Beispielschule();
        var r = await c.HochladenAsync("/api/dokumente/hochladen", Hilfen.AlsUp(d), "Meine Schule.up");
        var stand = (await r.JsonAsync())["dokument"]!;
        var id = stand["id"]!.GetValue<string>();
        Assert.Equal("Meine Schule", stand["name"]!.GetValue<string>());
        Assert.Equal(d.Klassen.Count, stand["zaehler"]!["klassen"]!.GetValue<int>());
        Assert.Equal(d.Lehrer.Count, stand["zaehler"]!["lehrer"]!.GetValue<int>());

        var liste = await c.GetJsonAsync("/api/dokumente");
        Assert.Contains(liste["dokumente"]!.AsArray(), x => x!["id"]!.GetValue<string>() == id);
        Assert.Equal(id, liste["zuletzt"]![0]!.GetValue<string>());

        var antwort = await c.GetAsync($"/api/dokumente/{id}/herunterladen");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("Meine Schule.up", antwort.Content.Headers.ContentDisposition?.FileNameStar ?? antwort.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var zurueck = JsonSerializer.Deserialize<Schuldatei>(await antwort.Content.ReadAsByteArrayAsync(), Dokument.JsonOptionen)!;
        Assert.Equal(d.Unterrichte.Count, zurueck.Unterrichte.Count);
        Assert.Equal(d.Lehrer.Select(l => l.Kurzname), zurueck.Lehrer.Select(l => l.Kurzname));
    }

    [Fact]
    public async Task UnbekanntesFormatUndUnbekanntesDokumentWerdenAbgewiesen()
    {
        var c = await _f.AngemeldetAsync();
        var r = await c.HochladenAsync("/api/dokumente/hochladen", "Hallo Welt"u8.ToArray(), "text.txt");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/dokumente/0123456789abcdef")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/dokumente/..%2F..%2Fbenutzer")).StatusCode);
    }

    [Fact]
    public async Task NeuSpeichernSchliessenUndWiederOeffnen()
    {
        var c = await _f.AngemeldetAsync();
        var stand = await c.PostJsonAsync("/api/dokumente/neu", new { schulname = "Testschule Nord", schulform = "GE" });
        var id = stand["id"]!.GetValue<string>();
        Assert.Equal("Testschule Nord", stand["schulname"]!.GetValue<string>());
        await c.PostJsonAsync($"/api/dokumente/{id}/stammdaten/klasse", new { kurzname = "5a" });
        Assert.True((await c.GetJsonAsync($"/api/dokumente/{id}"))["geaendert"]!.GetValue<bool>());

        var gespeichert = await c.PostJsonAsync($"/api/dokumente/{id}/speichern");
        Assert.False(gespeichert["geaendert"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(_f.Datenordner, "dokumente", id + ".up")));

        await c.PostJsonAsync($"/api/dokumente/{id}/schliessen");
        var wieder = await c.GetJsonAsync($"/api/dokumente/{id}/stammdaten/klasse");
        Assert.Contains(wieder["zeilen"]!.AsArray(), z => z!["werte"]![0]!.GetValue<string>() == "5a");

        var umbenannt = await (await c.PutAsJsonAsync($"/api/dokumente/{id}/name", new { name = "Nord 2026" })).JsonAsync();
        Assert.Equal("Nord 2026", umbenannt["name"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/dokumente/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/dokumente/{id}")).StatusCode);
    }

    [Fact]
    public async Task BalDateiWirdAlsNeuesDokumentImportiert()
    {
        var c = await _f.AngemeldetAsync();
        var r = await c.HochladenAsync("/api/dokumente/hochladen", File.ReadAllBytes(Hilfen.Testdatei("beispiel.bal")), "schule.bal");
        var j = await r.JsonAsync();
        var id = j["dokument"]!["id"]!.GetValue<string>();
        Assert.True(j["bericht"]!["neu"]!.GetValue<int>() > 0);
        var g = await c.GetJsonAsync($"/api/dokumente/{id}/grunddaten");
        Assert.Equal(2, g["zeitraster"]!["tage"]!.AsArray().Count);
        Assert.Equal(8, g["zeitraster"]!["stunden"]!.AsArray().Count);
        var lehrer = await c.GetJsonAsync($"/api/dokumente/{id}/stammdaten/lehrer");
        Assert.Contains(lehrer["zeilen"]!.AsArray(), z => z!["werte"]![0]!.GetValue<string>() == "AD");
        Assert.DoesNotContain(lehrer["zeilen"]!.AsArray(), z => z!["werte"]![0]!.GetValue<string>() == "NN");
    }

    [Fact]
    public async Task BalImportInGeoeffnetesDokumentIstRueckgaengigZuMachen()
    {
        var c = await _f.AngemeldetAsync();
        var id = (await c.PostJsonAsync("/api/dokumente/neu", new { schulname = "Import", schulform = "BK" }))["id"]!.GetValue<string>();
        var r = await c.HochladenAsync($"/api/dokumente/{id}/bal-import", File.ReadAllBytes(Hilfen.Testdatei("beispiel.bal")), "schule.bal",
            new() { ["pausenaufsichten"] = "false" });
        var bericht = await r.JsonAsync();
        Assert.True(bericht["neu"]!.GetValue<int>() > 0);
        var stand = await c.GetJsonAsync($"/api/dokumente/{id}");
        Assert.Equal("BAL-Import", stand["rueckgaengigText"]!.GetValue<string>());
        Assert.True(stand["zaehler"]!["planstunden"]!.GetValue<int>() > 0);

        var zurueck = await c.PostJsonAsync($"/api/dokumente/{id}/rueckgaengig");
        Assert.Equal(0, zurueck["zaehler"]!["unterrichte"]!.GetValue<int>());
        Assert.True(zurueck["kannWiederholen"]!.GetValue<bool>());
        var vor = await c.PostJsonAsync($"/api/dokumente/{id}/wiederholen");
        Assert.True(vor["zaehler"]!["unterrichte"]!.GetValue<int>() > 0);
    }

    [Fact]
    public async Task BeispielschuleHatEinenVerplantenStundenplan()
    {
        var c = await _f.AngemeldetAsync();
        var stand = await c.PostJsonAsync("/api/dokumente/beispiel");
        Assert.True(stand["zaehler"]!["planstunden"]!.GetValue<int>() > 100);
    }

    [Fact]
    public async Task EreignisseMeldenAenderungenAnAlleFenster()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"/api/dokumente/{id}/ereignisse");
        using var antwort = await c.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, abbruch.Token);
        Assert.Equal("text/event-stream", antwort.Content.Headers.ContentType?.MediaType);
        using var leser = new StreamReader(await antwort.Content.ReadAsStreamAsync(abbruch.Token));
        Assert.Equal("event: hallo", await leser.ReadLineAsync(abbruch.Token));

        var aendern = c.PostJsonAsync($"/api/dokumente/{id}/stammdaten/raum", new { kurzname = "R999" });
        string? zeile;
        do zeile = await leser.ReadLineAsync(abbruch.Token);
        while (zeile != null && zeile != "event: aenderung");
        var daten = await leser.ReadLineAsync(abbruch.Token);
        Assert.StartsWith("data: ", daten);
        var e = JsonDocument.Parse(daten![6..]).RootElement;
        Assert.Equal("Stammdaten", e.GetProperty("bereich").GetString());
        Assert.Equal("admin", e.GetProperty("benutzer").GetString());
        await aendern;
    }

    [Fact]
    public async Task AntwortenTragenDenAenderungsstand()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var r = await c.PostAsJsonAsync($"/api/dokumente/{id}/stammdaten/fach", new { kurzname = "XY" });
        Assert.Equal("1", r.Headers.GetValues("X-Dokument-Version").Single());
        Assert.Equal("Stammdaten", r.Headers.GetValues("X-Dokument-Bereich").Single());
    }
}
