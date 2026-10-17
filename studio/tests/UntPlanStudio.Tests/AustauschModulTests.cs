using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Web.Contracts;

namespace UntPlanStudio.Tests;

/// <summary>Gemockter UntPlanWeb-Server (Kestrel auf einem freien Port): nimmt Pakete an, liefert Rückmeldungen, merkt Quittungen.</summary>
public sealed class UntPlanWebAttrappe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public ConcurrentQueue<JsonNode> Pakete { get; } = new();
    public ConcurrentBag<string> Quittiert { get; } = new();
    public List<WebRueckmeldung> Rueckmeldungen { get; set; } = new();
    public string? Autorisierung { get; private set; }
    public int Port { get; }

    private UntPlanWebAttrappe(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public static async Task<UntPlanWebAttrappe> StartenAsync()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Logging.ClearProviders();
        var app = b.Build();
        UntPlanWebAttrappe? self = null;
        app.MapGet("/api/v1/status", () => Results.Json(new { schule = "Attrappe-Gymnasium" }));
        app.MapPut("/api/v1/schulen/{schule}/untplanweb", async (HttpRequest req) =>
        {
            self!.Autorisierung = req.Headers.Authorization.ToString();
            var paket = (await JsonNode.ParseAsync(req.Body))!;
            self.Pakete.Enqueue(paket);
            return Results.Json(new { ok = true, revision = paket["revision"]!.GetValue<int>(), meldung = (string?)null });
        });
        app.MapGet("/api/v1/schulen/{schule}/rueckmeldungen", () => Results.Json(self!.Rueckmeldungen.Where(m => !self.Quittiert.Contains(m.Id)), UntPlanWebSchema.Json));
        app.MapPost("/api/v1/schulen/{schule}/rueckmeldungen/quittieren", async (HttpRequest req) =>
        {
            foreach (var id in (await JsonSerializer.DeserializeAsync<string[]>(req.Body))!) self!.Quittiert.Add(id);
            return Results.Ok();
        });
        await app.StartAsync();
        var adresse = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new UntPlanWebAttrappe(app, new Uri(adresse).Port);
        return self;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Module Import/Export, UntPlanWeb (gegen die Attrappe), Abrechnung, Pausenaufsichten, Kursplanung, Kommentare, MultiUser.</summary>
public sealed class AustauschModulTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;

    public AustauschModulTests(StudioFabrik f) => _f = f;

    /// <summary>Erster Unterrichtstag ab heute (sonst ab Schuljahresbeginn) der Beispielschule.</summary>
    private static DateOnly Unterrichtstag(Schuldatei d)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var start = heute >= d.Einstellungen.SchuljahrVon && heute <= d.Einstellungen.SchuljahrBis ? heute : d.Einstellungen.SchuljahrVon;
        for (var t = start; t <= d.Einstellungen.SchuljahrBis; t = t.AddDays(1))
            if (Kalender.IstUnterrichtstag(d, t)) return t;
        throw new InvalidOperationException("Kein Unterrichtstag.");
    }

    private static async Task<UpwAuftragAntwort> WartenAsync(HttpClient c, string dok, JsonNode auftrag)
    {
        var id = auftrag["id"]!.GetValue<string>();
        for (var i = 0; i < 400; i++)
        {
            var a = await c.GetJsonAsync($"/api/dokumente/{dok}/untplanweb/auftrag/{id}");
            if (a["fertig"]!.GetValue<bool>())
                return new UpwAuftragAntwort(a["wert"]!.GetValue<int>(), a["text"]!.GetValue<string>(), a["fehler"]?.GetValue<string>(), a["ergebnis"]?.GetValue<string>());
            await Task.Delay(50);
        }
        throw new TimeoutException("Auftrag wurde nicht fertig.");
    }

    private sealed record UpwAuftragAntwort(int Wert, string Text, string? Fehler, string? Ergebnis);

    [Fact]
    public async Task ImportExportCsvGpuAbteilung()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var basis = $"/api/dokumente/{id}/austausch";
        var info = await c.GetJsonAsync(basis);
        var lehrerVorher = info["zaehler"]!["lehrer"]!.GetValue<int>();

        // Schild-NRW-Lehrer (CSV, Windows-1252)
        var csv = Encoding.Latin1.GetBytes("Kürzel;Nachname;Vorname\nZZZ;Zöllner;Anna\n");
        var b = await (await c.HochladenAsync($"{basis}/csv/lehrer", csv, "lehrer.csv")).JsonAsync();
        Assert.True(b["neu"]!.GetValue<int>() + b["aktualisiert"]!.GetValue<int>() >= 1, b.ToJsonString());
        var nachher = (await c.GetJsonAsync(basis))["zaehler"]!["lehrer"]!.GetValue<int>();
        Assert.Equal(lehrerVorher + b["neu"]!.GetValue<int>(), nachher);

        // GPU-Export als ZIP und Reimport
        var zip = await c.GetByteArrayAsync($"{basis}/gpu");
        using (var archiv = new ZipArchive(new MemoryStream(zip)))
            Assert.Contains(archiv.Entries, e => e.Name == "GPU002.TXT");
        var gpu = await (await c.HochladenAsync($"{basis}/gpu", zip, "gpu.zip")).JsonAsync();
        Assert.True(gpu["aktualisiert"]!.GetValue<int>() > 0);

        // weitere Exporte
        Assert.Contains("U-Nr", await c.GetStringAsync($"{basis}/unterricht.csv"));
        var paket = JsonNode.Parse(await c.GetStringAsync($"{basis}/untplanweb.json"))!;
        Assert.Equal(UntPlanWebSchema.Schema, paket["schema"]!.GetValue<string>());
        var lehrerId = info["lehrer"]!.AsArray()[0]!["id"]!.GetValue<int>();
        Assert.Contains("BEGIN:VCALENDAR", await c.GetStringAsync($"{basis}/ical/{lehrerId}"));

        // Abteilungsdatei: anlegen, exportieren, zusammenführen
        var ab = await c.PostJsonAsync($"/api/dokumente/{id}/stammdaten/abteilung", new { kurzname = "SI" });
        var abId = ab["id"]!.GetValue<int>();
        var datei = await c.GetByteArrayAsync($"{basis}/abteilung/{abId}");
        Assert.NotNull(JsonSerializer.Deserialize<Schuldatei>(datei, Dokument.JsonOptionen));
        var z = await (await c.HochladenAsync($"{basis}/abteilung/{abId}", datei, "Abteilung_SI.up")).JsonAsync();
        Assert.Contains("übernommen", z["text"]!.GetValue<string>());

        // unbekannte Importart
        Assert.Equal(HttpStatusCode.BadRequest, (await c.HochladenAsync($"{basis}/csv/xyz", csv, "x.csv")).StatusCode);
    }

    [Fact]
    public async Task UntPlanWebExportImportKlausurUndStorno()
    {
        await using var web = await UntPlanWebAttrappe.StartenAsync();
        var c = await _f.AngemeldetAsync();
        var schule = Demo.Beispielschule();
        var id = await c.BeispielHochladenAsync(schule);
        var basis = $"/api/dokumente/{id}/untplanweb";

        // ohne Verbindung: Fehler im Auftrag, kein Absturz
        var ohne = await WartenAsync(c, id, await c.PostJsonAsync($"{basis}/export/vertretungen"));
        Assert.NotNull(ohne.Fehler);

        // Einstellungen wie im Desktop (Server, Port, https, Schul-ID, Schlüssel)
        var e = await (await c.PutAsJsonAsync($"{basis}/einstellungen", new { schulId = "123456", server = "127.0.0.1", port = web.Port, https = false, apiSchluessel = "geheim", tageVoraus = 3 })).JsonAsync();
        Assert.Equal($"http://127.0.0.1:{web.Port}", e["adresse"]!.GetValue<string>());
        Assert.True(e["apiSchluesselGesetzt"]!.GetValue<bool>());
        var pruefung = await c.PostJsonAsync($"{basis}/pruefen", new { });
        Assert.True(pruefung["ok"]!.GetValue<bool>(), pruefung.ToJsonString());
        Assert.Equal("Attrappe-Gymnasium", (await c.GetJsonAsync($"{basis}/schulname"))["schulname"]!.GetValue<string>());

        // Export Vertretungen mit Fortschritt
        var a = await WartenAsync(c, id, await c.PostJsonAsync($"{basis}/export/vertretungen"));
        Assert.Null(a.Fehler);
        Assert.Equal(100, a.Wert);
        Assert.Contains("übertragen", a.Text);
        Assert.True(web.Pakete.TryDequeue(out var paket));
        Assert.Equal(UntPlanWebSchema.Schema, paket!["schema"]!.GetValue<string>());
        Assert.Equal("Bearer geheim", web.Autorisierung);
        var status = await c.GetJsonAsync(basis);
        Assert.Equal(paket["revision"]!.GetValue<int>(), status["revision"]!.GetValue<int>());
        Assert.Contains("Zuletzt exportiert", status["zeilen"]!.AsArray()[2]!["zuletzt"]!.GetValue<string>());

        // Rückmeldungen: Prüfung und Buchung
        var tag = Unterrichtstag(schule);
        var klasse = schule.Klassen[0].Kurzname;
        var lehrer = schule.Lehrer[0].Kurzname;
        var raum = schule.Raeume.Last().Kurzname;
        web.Rueckmeldungen = new()
        {
            new WebRueckmeldung { Id = "klausur-7", Typ = WebRueckmeldungsTypen.Klausur, Absender = "MÜL", Von = tag.ToString("yyyy-MM-dd"), StundeVon = 1, StundeBis = 2, Titel = "Mathe-Klausur", Klassen = { klasse }, Lehrer = lehrer, Raeume = { raum } },
            new WebRueckmeldung { Id = "buchung-1", Typ = WebRueckmeldungsTypen.Raumbuchung, Absender = "MÜL", Von = tag.ToString("yyyy-MM-dd"), StundeVon = 6, StundeBis = 6, Titel = "Elternabend", Raum = raum },
        };
        var stpl = await c.GetJsonAsync($"{basis}/stpl?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&neu=true");
        var zeilen = stpl["zeilen"]!.AsArray();
        Assert.Equal(2, zeilen.Count);
        Assert.Contains(zeilen, z => z!["id"]!.GetValue<string>() == "klausur-7" && z["art"]!.GetValue<string>() == "Pruefung");

        var u = await c.PostJsonAsync($"{basis}/stpl/uebernehmen", new { ids = new[] { "klausur-7", "buchung-1" } });
        Assert.Equal(2, u["uebernommen"]!.AsArray().Count);
        Assert.Contains("klausur-7", web.Quittiert);
        var klausuren = (await c.GetJsonAsync($"{basis}/klausuren")).AsArray();
        var k = klausuren.Single(x => x!["webId"]!.GetValue<string>() == "klausur-7")!;
        Assert.True(k["importiert"]!.GetValue<bool>());
        Assert.Equal("Mathe-Klausur", k["name"]!.GetValue<string>());

        // erledigt: wird nicht erneut angeboten, aber mit „erledigte“ angezeigt
        Assert.Empty((await c.GetJsonAsync($"{basis}/stpl?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&neu=true"))["zeilen"]!.AsArray());
        Assert.Equal(2, (await c.GetJsonAsync($"{basis}/stpl?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&erledigte=true"))["zeilen"]!.AsArray().Count);

        // importierte Prüfung: nur Aufsicht/Raum änderbar – Name bleibt
        var kid = k["id"]!.GetValue<int>();
        var geaendert = await (await c.PutAsJsonAsync($"{basis}/klausuren/{kid}", new { name = "Anders", lehrer = schule.Lehrer[1].Kurzname })).JsonAsync();
        Assert.Equal("Mathe-Klausur", geaendert["name"]!.GetValue<string>());
        Assert.Equal(schule.Lehrer[1].Kurzname, geaendert["lehrer"]!.GetValue<string>());

        // Storno der Buchung: Zeilen aus „buchung-1“ verschwinden
        async Task<int> Buchungszeilen() => await _f.Services.GetRequiredService<UntPlanStudio.Dienste.Dokumentdienst>().Holen(id).LesenAsync(d => d.Daten.Vertretungen.Count(v => v.WebId == "buchung-1"));
        Assert.True(await Buchungszeilen() > 0);
        web.Rueckmeldungen = new() { new WebRueckmeldung { Id = "storno-buchung-1", Typ = WebRueckmeldungsTypen.BuchungStorno, Absender = "MÜL", Von = tag.ToString("yyyy-MM-dd"), StundeVon = 6, StundeBis = 6, Raum = raum, BezugId = "buchung-1" } };
        var s = await c.GetJsonAsync($"{basis}/stpl?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&neu=true");
        Assert.True(s["zeilen"]!.AsArray().Single()!["storno"]!.GetValue<bool>());
        await c.PostJsonAsync($"{basis}/stpl/uebernehmen", new { ids = new[] { "storno-buchung-1" } });
        Assert.Equal(0, await Buchungszeilen());

        // Ignorieren einer neuen Prüfung
        web.Rueckmeldungen = new() { new WebRueckmeldung { Id = "klausur-8", Typ = WebRueckmeldungsTypen.Klausur, Absender = "X", Von = tag.ToString("yyyy-MM-dd"), StundeVon = 3, Klassen = { klasse }, Titel = "Bio" } };
        await c.GetJsonAsync($"{basis}/stpl?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&neu=true");
        await c.PostJsonAsync($"{basis}/stpl/ignorieren", new { ids = new[] { "klausur-8" } });
        Assert.Contains("klausur-8", web.Quittiert);
        Assert.DoesNotContain((await c.GetJsonAsync($"{basis}/klausuren")).AsArray(), x => x!["webId"]!.GetValue<string>() == "klausur-8");

        // automatischer Abgleich (manuell ausgelöst): exportiert die Vertretungen
        await (await c.PutAsJsonAsync($"{basis}/abgleich", new { aktiv = false, minuten = 15, vertretungen = true, rueckmeldungen = true })).JsonAsync();
        var abgleich = await c.PostJsonAsync($"{basis}/abgleich/jetzt");
        Assert.Contains("Vertretungen exportiert", abgleich["ergebnis"]!.GetValue<string>());
        Assert.True(web.Pakete.TryDequeue(out _));
    }

    [Fact]
    public async Task AbrechnungLiefertZahlen()
    {
        var c = await _f.AngemeldetAsync();
        var schule = Demo.Beispielschule();
        var id = await c.BeispielHochladenAsync(schule);
        var basis = $"/api/dokumente/{id}/abrechnung";

        var werte = await c.GetJsonAsync($"{basis}/werte");
        Assert.Equal(schule.Lehrer.Count, werte["zeilen"]!.AsArray().Count);
        Assert.Contains("Summe Soll", werte["summe"]!.GetValue<string>());
        var lid = werte["zeilen"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var neu = await c.PatchJsonAsync($"{basis}/werte/{lid}", new { feld = "Wochensoll", wert = "20,5" });
        Assert.Equal("20,5", neu["zeilen"]!.AsArray().First(z => z!["id"]!.GetValue<int>() == lid)!["werte"]![2]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/werte/{lid}", new { feld = "Wochensoll", wert = "abc" })).StatusCode);

        var matrix = await c.GetJsonAsync($"{basis}/matrix?art=1");
        Assert.Equal(schule.Klassen.Count, matrix["zeilen"]!.AsArray().Count);
        Assert.Contains(matrix["zeilen"]!.AsArray(), z => int.Parse(z!["werte"]!.AsArray().Last()!.GetValue<string>()) > 0);

        var tafel = await c.PostJsonAsync($"{basis}/stundentafel/nrw");
        Assert.NotEmpty(tafel["zeilen"]!.AsArray());
        var fach = tafel["faecher"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var j = tafel["zeilen"]!.AsArray()[0]!["jahrgang"]!.GetValue<string>();
        var t2 = await (await c.PutAsJsonAsync($"{basis}/stundentafel", new { jahrgang = j, fach, wert = "4" })).JsonAsync();
        Assert.Equal("4", t2["zeilen"]!.AsArray().First(z => z!["jahrgang"]!.GetValue<string>() == j)!["werte"]![0]!.GetValue<string>());

        var sv = schule.Einstellungen.SchuljahrVon;
        var sb = schule.Einstellungen.SchuljahrBis;
        for (var nr = 0; nr <= 6; nr++)
        {
            var b = await c.GetJsonAsync($"{basis}/statistik?bericht={nr}&von={sv:yyyy-MM-dd}&bis={sb:yyyy-MM-dd}&lehrer={lid}");
            Assert.NotEmpty(b["kopf"]!.AsArray());
            Assert.False(string.IsNullOrEmpty(b["summe"]!.GetValue<string>()));
        }
        var abrechnung = await c.GetJsonAsync($"{basis}/statistik?bericht=0&von={sv:yyyy-MM-dd}&bis={sb:yyyy-MM-dd}");
        Assert.Equal("Vertretungen", abrechnung["kopf"]![2]!.GetValue<string>());
        Assert.Contains("<table>", await c.GetStringAsync($"{basis}/statistik.html?bericht=0&von={sv:yyyy-MM-dd}&bis={sb:yyyy-MM-dd}"));
        Assert.Contains("Lehrer", await c.GetStringAsync($"{basis}/statistik.csv?bericht=0"));
        Assert.True((await c.GetJsonAsync($"{basis}/zaehler/{lid}"))["von"] != null);
    }

    [Fact]
    public async Task PausenaufsichtenPlanenUndVertreten()
    {
        var c = await _f.AngemeldetAsync();
        var schule = Demo.Beispielschule();
        var id = await c.BeispielHochladenAsync(schule);
        var basis = $"/api/dokumente/{id}/pausenaufsicht";
        var uebersicht = await c.GetJsonAsync(basis);
        int gang;
        if (uebersicht["gaenge"]!.AsArray().Count == 0)
            gang = (await c.PostJsonAsync($"/api/dokumente/{id}/stammdaten/gang", new { kurzname = "Hof" }))["id"]!.GetValue<int>();
        else gang = uebersicht["gaenge"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var pausen = (await c.GetJsonAsync(basis))["pausen"]!.AsArray();
        Assert.NotEmpty(pausen);
        var nach = pausen[0]!["nach"]!.GetValue<int>();

        var plan = await c.GetJsonAsync($"{basis}/gang/{gang}");
        Assert.Equal(schule.Zeitraster.TageProWoche, plan["tage"]!.AsArray().Count);

        // Zelle eintragen (geteilt: zwei Lehrkräfte)
        var l1 = schule.Lehrer[0];
        var l2 = schule.Lehrer[1];
        var z = await (await c.PutAsJsonAsync($"{basis}/zelle", new { gang, tag = 0, nach, lehrer = $"{l1.Kurzname}, {l2.Kurzname}" })).JsonAsync();
        var zelle = z["tage"]!.AsArray()[0]!["zellen"]!.AsArray().First(x => x!["nach"]!.GetValue<int>() == nach)!;
        Assert.Equal(2, zelle["teile"]!.GetValue<int>());
        Assert.Equal($"{l1.Kurzname}, {l2.Kurzname}", zelle["text"]!.GetValue<string>());
        var lehrerSicht = await c.GetJsonAsync($"{basis}/lehrer/{l1.Id}");
        Assert.Contains(lehrerSicht["tage"]!.AsArray()[0]!["zellen"]!.AsArray(), x => x!["text"]!.GetValue<string>().Length > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{basis}/zelle", new { gang, tag = 0, nach, lehrer = "GIBTSNICHT" })).StatusCode);

        // Teilung entfernen, keine Aufsicht umschalten
        var t = await c.PostJsonAsync($"{basis}/teile", new { gang, tag = 0, nach, delta = -1 });
        Assert.Equal(1, t["tage"]!.AsArray()[0]!["zellen"]!.AsArray().First(x => x!["nach"]!.GetValue<int>() == nach)!["teile"]!.GetValue<int>());

        // Vorschlag, Optimierung, Gewichtung
        var v = await c.GetJsonAsync($"{basis}/vorschlag?gang={gang}&tag=0&nach={nach}");
        Assert.Equal("Name", v["spalten"]![0]!.GetValue<string>());
        var o = await c.PostJsonAsync($"{basis}/optimieren", new { schlechteLoeschen = false, gang = (int?)null, kuerzestePause = 0 });
        Assert.True(o["eingeteilt"]!.GetValue<int>() >= 0);
        var g = await c.GetJsonAsync($"{basis}/gewichtung");
        Assert.NotEmpty(g["kriterien"]!.AsArray());
        await (await c.PutAsJsonAsync($"{basis}/gewichtung", new { maxProTag = 2, werte = new Dictionary<string, int> { ["PA.Folge"] = 5 } })).JsonAsync();
        Assert.Equal(2, (await c.GetJsonAsync($"{basis}/gewichtung"))["maxProTag"]!.GetValue<int>());

        // Vertretung der Aufsichten: Tagesliste
        var tag = Unterrichtstag(schule);
        var vt = await c.GetJsonAsync($"{basis}/vertretung/{tag:yyyy-MM-dd}");
        Assert.True(vt["unterrichtstag"]!.GetValue<bool>());
    }

    [Fact]
    public async Task KursplanungUndKommentare()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var klassen = (await c.GetJsonAsync($"/api/dokumente/{id}/kursplanung")).AsArray();
        Assert.NotEmpty(klassen);
        var kl = klassen[0]!["id"]!.GetValue<int>();
        var k = await c.GetJsonAsync($"/api/dokumente/{id}/kursplanung/{kl}");
        Assert.NotNull(k["status"]);
        if (k["kurse"]!.AsArray().Count > 0)
        {
            var kurs = k["kurse"]!.AsArray()[0]!["id"]!.GetValue<int>();
            var neu = await c.PatchJsonAsync($"/api/dokumente/{id}/kursplanung/{kl}/kurs/{kurs}", new { feld = "band", wert = "B1" });
            Assert.Equal("B1", neu["kurse"]!.AsArray().First(x => x!["id"]!.GetValue<int>() == kurs)!["band"]!.GetValue<string>());
            var b = await c.PostJsonAsync($"/api/dokumente/{id}/kursplanung/{kl}/bandbildung", new { maxBaender = 8 });
            Assert.NotEmpty(b["baender"]!.AsArray());
        }

        var kom = $"/api/dokumente/{id}/kommentare";
        var lehrer = (await c.GetJsonAsync($"/api/dokumente/{id}/stammdaten/lehrer"))["zeilen"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var liste = (await c.PostJsonAsync(kom, new { typ = "lehrer", element = lehrer, feld = "Wochensoll", kategorie = 2, text = "Teilzeit ab Februar" })).AsArray();
        var eintrag = liste.Single()!;
        Assert.Equal("Teilzeit ab Februar", eintrag["text"]!.GetValue<string>());
        Assert.Equal(2, (await c.GetJsonAsync($"{kom}/kategorien")).AsArray()[1]!["nr"]!.GetValue<int>());
        var geaendert = (await (await c.PutAsJsonAsync($"{kom}/{eintrag["id"]}", new { typ = "lehrer", element = lehrer, kategorie = 3, text = "geändert" })).JsonAsync()).AsArray();
        Assert.Equal("geändert", geaendert.Single()!["text"]!.GetValue<string>());
        Assert.Empty((await c.PostJsonAsync($"{kom}/loeschen", new { ids = new[] { eintrag["id"]!.GetValue<int>() } })).AsArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(kom, new { typ = "lehrer", element = lehrer, kategorie = 1, text = " " })).StatusCode);
    }

    [Fact]
    public async Task MultiUserSperrenExklusivUndRechte()
    {
        var admin = await _f.AngemeldetAsync();
        var name = "planer" + Guid.NewGuid().ToString("N")[..6];
        (await admin.PostAsJsonAsync("/api/benutzer", new { name, anzeigename = "Pia Planer", rolle = "Planer", passwort = "planer-pw" })).EnsureSuccessStatusCode();
        var planer = await _f.AngemeldetAsync(name, "planer-pw");
        var id = await admin.BeispielHochladenAsync();
        var dok = $"/api/dokumente/{id}";
        var lehrer = (await admin.GetJsonAsync($"{dok}/stammdaten/lehrer"))["zeilen"]!.AsArray()[0]!["id"]!.GetValue<int>();

        // beide aktiv
        await planer.GetJsonAsync($"{dok}/multiuser?taetigkeit=Lehrkräfte");
        var zustand = await admin.GetJsonAsync($"{dok}/multiuser");
        var aktive = zustand["aktive"]!.AsArray();
        Assert.Contains(aktive, a => a!["benutzer"]!.GetValue<string>() == name && a["taetigkeit"]!.GetValue<string>() == "Lehrkräfte" && a["aktiv"]!.GetValue<bool>());
        Assert.Contains(aktive, a => a!["benutzer"]!.GetValue<string>() == "admin");

        // Admin sperrt die Stammdaten → Planer erhält einen Sperrhinweis (423)
        await admin.PostJsonAsync($"{dok}/multiuser/sperren", new { bereich = "Stammdaten" });
        var r = await planer.PatchAsJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "20" });
        Assert.Equal(HttpStatusCode.Locked, r.StatusCode);
        Assert.Contains("gesperrt", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Locked, (await planer.PostAsJsonAsync($"{dok}/multiuser/sperren", new { bereich = "Stammdaten" })).StatusCode);
        // der Sperrende selbst darf ändern, andere Bereiche bleiben frei
        await admin.PatchJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "21" });
        Assert.Equal(HttpStatusCode.OK, (await planer.GetAsync($"{dok}/stammdaten/lehrer")).StatusCode);
        // Freigeben → Planer darf wieder
        await (await admin.DeleteAsync($"{dok}/multiuser/sperren/Stammdaten")).JsonAsync();
        await planer.PatchJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "22" });

        // Exklusivbetrieb nicht möglich, solange ein anderer aktiv ist
        Assert.Equal(HttpStatusCode.Locked, (await admin.PostAsJsonAsync($"{dok}/multiuser/exklusiv", new { ein = true })).StatusCode);

        // Gruppe „Info“ (alles nur lesen) → 403 mit Hinweis
        var verwaltung = await admin.GetJsonAsync("/api/multiuser/verwaltung");
        var info = verwaltung["gruppen"]!.AsArray().First(g => g!["name"]!.GetValue<string>() == "Info")!["id"]!.GetValue<int>();
        await (await admin.PutAsJsonAsync($"/api/multiuser/benutzer/{name}", new { gruppeId = info, nurLesen = false, abteilung = "" })).JsonAsync();
        var verboten = await planer.PatchAsJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "23" });
        Assert.Equal(HttpStatusCode.Forbidden, verboten.StatusCode);
        Assert.Contains("nur lesen", await verboten.Content.ReadAsStringAsync());
        var ich = await planer.GetJsonAsync("/api/multiuser/ich");
        Assert.Equal("Info", ich["gruppe"]!.GetValue<string>());
        // Planer darf die Verwaltung nicht
        Assert.Equal(HttpStatusCode.Forbidden, (await planer.GetAsync("/api/multiuser/verwaltung")).StatusCode);

        // Gruppe „Vertretungsplaner“: Vertretung ja, Stammdaten nein
        var vp = verwaltung["gruppen"]!.AsArray().First(g => g!["name"]!.GetValue<string>() == "Vertretungsplaner")!["id"]!.GetValue<int>();
        await (await admin.PutAsJsonAsync($"/api/multiuser/benutzer/{name}", new { gruppeId = vp, nurLesen = false, abteilung = "" })).JsonAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await planer.PatchAsJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "24" })).StatusCode);

        // Nachricht an alle
        await planer.PostJsonAsync($"{dok}/multiuser/nachricht", new { an = (string?)null, text = "Bitte nicht die Räume ändern" });
        Assert.Contains((await admin.GetJsonAsync($"{dok}/multiuser?nach=0"))["nachrichten"]!.AsArray(), n => n!["text"]!.GetValue<string>().Contains("Räume"));

        // Zuordnung entfernen: wieder volle Planer-Rechte
        await (await admin.PutAsJsonAsync($"/api/multiuser/benutzer/{name}", new { gruppeId = 0, nurLesen = false, abteilung = "" })).JsonAsync();
        await planer.PatchJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "25" });

        // Planer schaltet Exklusiv ein (Admin war gerade aktiv → abgelehnt); nach Abmelden des Admins möglich, Admin dann gesperrt
        await admin.PostAsync($"{dok}/multiuser/abmelden", null);
        var ex = await planer.PostJsonAsync($"{dok}/multiuser/exklusiv", new { ein = true });
        Assert.True(ex["exklusiv"]!["eigen"]!.GetValue<bool>());
        var gesperrt = await admin.PatchAsJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "26" });
        Assert.Equal(HttpStatusCode.Locked, gesperrt.StatusCode);
        Assert.Contains("exklusiv", await gesperrt.Content.ReadAsStringAsync());
        await planer.PostJsonAsync($"{dok}/multiuser/exklusiv", new { ein = false });
        await admin.PatchJsonAsync($"{dok}/stammdaten/lehrer/{lehrer}", new { feld = "Wochensoll", wert = "27" });
    }
}
