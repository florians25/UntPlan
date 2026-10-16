using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sitzplan im Klassenbuch: Wirkungsbereiche Klasse/Unterricht/Raum mit Rechteprüfung, Auflösung (Unterricht vor Raum vor Klasse),
/// Bereinigung der Eingaben, Druck als PDF, Übernahme alter Klassen-Sitzpläne. Beispielpaket: LAN ist Klassenleitung der 7a und
/// unterrichtet D in der 7a (Unterricht 147, Raum R105); MUE ist Klassenleitung der 7b; Unterricht 169 (KR/ER) hat die Klassen 7a und 7b.
/// </summary>
public class SitzplanTests
{
    private const string Stunde = "/api/app/sitzplaene/stunde?unterricht=147&raeume=R105&klassen=7a";

    private static object Plan(string bereich, Dictionary<string, int> belegung, int unterricht = 147, string raum = "R105", string klasse = "7a",
        int zeilen = 4, int spalten = 5, string pult = "vorne", List<string>? ohneTisch = null) =>
        new { bereich, unterricht, raeume = new[] { raum }, klassen = new[] { klasse }, zeilen, spalten, pult, belegung, ohneTisch = ohneTisch ?? new List<string>() };

    private static async Task<List<int>> SchuelerAsync(HttpClient c) =>
        (await Testserver.HolenAsync(c, Stunde))["schueler"]!.AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();

    [Fact]
    public async Task Rechte_je_Wirkungsbereich()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        var d = await Testserver.HolenAsync(lan, Stunde);
        Assert.True(d["rechte"]!["klasse"]!.GetValue<bool>());
        Assert.True(d["rechte"]!["unterricht"]!.GetValue<bool>());
        Assert.True(d["rechte"]!["raum"]!.GetValue<bool>());
        Assert.Equal("7a", d["kontext"]!["klasse"]!.GetValue<string>());
        Assert.Equal("LAN", d["kontext"]!["lehrer"]!.GetValue<string>());
        Assert.Equal(24, d["schueler"]!.AsArray().Count);
        Assert.Null(d["gueltig"]);

        // fremde Lehrkraft: darf lesen, aber nichts anlegen
        var m = await Testserver.HolenAsync(mue, Stunde);
        Assert.False(m["rechte"]!["klasse"]!.GetValue<bool>());
        Assert.False(m["rechte"]!["unterricht"]!.GetValue<bool>());
        Assert.False(m["rechte"]!["raum"]!.GetValue<bool>());
        var ids = await SchuelerAsync(lan);
        foreach (var b in new[] { "klasse", "unterricht", "raum" })
            Assert.Equal(HttpStatusCode.Forbidden, (await mue.PutAsJsonAsync("/api/app/sitzplaene", Plan(b, new() { ["1-1"] = ids[0] }))).StatusCode);

        // Klassen-Sitzplan nur bei Unterricht mit einer Klasse; Schüler*innen und Eltern gar nicht
        var leitung = await s.AlsAsync("admin");
        var multi = await leitung.PutAsJsonAsync("/api/app/sitzplaene", new { bereich = "klasse", unterricht = 169, klassen = new[] { "7a", "7b" }, zeilen = 2, spalten = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, multi.StatusCode);
        var schueler = await s.AlsAsync("sch", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync(Stunde)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("tisch", new()))).StatusCode);
    }

    [Fact]
    public async Task Aufloesung_Unterricht_vor_Raum_vor_Klasse()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var ids = await SchuelerAsync(lan);

        var k = await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("klasse", new() { ["1-1"] = ids[0] })));
        Assert.Equal("7a", k["schluessel"]!.GetValue<string>());
        Assert.Equal("klasse", (await Testserver.HolenAsync(lan, Stunde))["gueltig"]!["bereich"]!.GetValue<string>());
        // gilt auch in einem anderen Unterricht der Klasse (KR/ER nur 7a-Teil) …
        var andere = await Testserver.HolenAsync(lan, "/api/app/sitzplaene/stunde?klassen=7a");
        Assert.Equal("klasse", andere["gueltig"]!["bereich"]!.GetValue<string>());

        await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("raum", new() { ["2-2"] = ids[1] }, pult: "hinten")));
        var g = (await Testserver.HolenAsync(lan, Stunde))["gueltig"]!;
        Assert.Equal("raum", g["bereich"]!.GetValue<string>());
        Assert.Equal("R105|LAN", g["schluessel"]!.GetValue<string>());
        Assert.Equal("hinten", g["pult"]!.GetValue<string>());

        await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("unterricht", new() { ["3-3"] = ids[2] }, zeilen: 6, spalten: 7)));
        var d = await Testserver.HolenAsync(lan, Stunde);
        Assert.Equal("unterricht", d["gueltig"]!["bereich"]!.GetValue<string>());
        Assert.Equal(7, d["gueltig"]!["spalten"]!.GetValue<int>());
        Assert.Equal(ids[2], d["gueltig"]!["belegung"]!["3-3"]!.GetValue<int>());
        // alle drei Pläne werden mitgeliefert (Editor: Punkt „gespeichert“, laden, löschen)
        Assert.NotNull(d["plaene"]!["klasse"]);
        Assert.NotNull(d["plaene"]!["raum"]);
        Assert.NotNull(d["plaene"]!["unterricht"]);

        // andere Lehrkraft in derselben Stunde (z. B. Vertretung) sieht denselben gültigen Plan
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        Assert.Equal("unterricht", (await Testserver.HolenAsync(mue, Stunde))["gueltig"]!["bereich"]!.GetValue<string>());

        // löschen: Unterricht → Raum → Klasse → keiner
        await Testserver.JsonAsync(await lan.DeleteAsync("/api/app/sitzplaene?bereich=unterricht&unterricht=147&raeume=R105&klassen=7a"));
        Assert.Equal("raum", (await Testserver.HolenAsync(lan, Stunde))["gueltig"]!["bereich"]!.GetValue<string>());
        await Testserver.JsonAsync(await lan.DeleteAsync("/api/app/sitzplaene?bereich=raum&unterricht=147&raeume=R105&klassen=7a"));
        Assert.Equal("klasse", (await Testserver.HolenAsync(lan, Stunde))["gueltig"]!["bereich"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await mue.DeleteAsync("/api/app/sitzplaene?bereich=klasse&klassen=7a&klasse=7a")).StatusCode);
        await Testserver.JsonAsync(await lan.DeleteAsync("/api/app/sitzplaene?bereich=klasse&unterricht=147&klassen=7a"));
        Assert.Null((await Testserver.HolenAsync(lan, Stunde))["gueltig"]);
    }

    [Fact]
    public async Task Eingaben_werden_bereinigt()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var ids = await SchuelerAsync(lan);
        var fremd = (await Testserver.HolenAsync(lan, "/api/app/sitzplaene/stunde?klassen=7b"))["schueler"]![0]!["id"]!.GetValue<int>();
        var p = await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("unterricht", new()
        {
            ["1-1"] = ids[0], ["1-2"] = ids[0], // doppelt
            ["2-1"] = fremd,                    // nicht in dieser Stunde
            ["9-9"] = ids[1], ["x"] = ids[2],   // außerhalb bzw. ungültig
            ["3-1"] = ids[3],                   // Platz ohne Tisch
            ["3-2"] = ids[4],
        }, zeilen: 3, spalten: 40, pult: "seitlich", ohneTisch: new() { "3-1", "3-1", "7-7" })));
        Assert.Equal(15, p["spalten"]!.GetValue<int>());
        Assert.Equal("vorne", p["pult"]!.GetValue<string>());
        var belegung = p["belegung"]!.AsObject();
        Assert.Equal(2, belegung.Count);
        Assert.Equal(ids[4], belegung["3-2"]!.GetValue<int>());
        Assert.Single(belegung, x => x.Value!.GetValue<int>() == ids[0]);
        Assert.Equal(new[] { "3-1" }, p["ohneTisch"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Raum_Sitzplan_behaelt_Plaetze_anderer_Unterrichte()
    {
        await using var s = await Testserver.StartenAsync();
        var dienst = s.Dienst<Sitzplandienst>();
        var b = s.Dienst<Benutzerdienst>().NachLogin("admin")!;
        dienst.Speichern(b, Sitzplandienst.Raum, "R105|LAN", new SitzplanEingabe(4, 4, null, new() { ["1-1"] = 1, ["1-2"] = 2 }, null), new HashSet<int> { 1, 2 });
        // anderer Unterricht im selben Raum: nur Schüler*in 3 und 2 dürfen gesetzt werden – 1 bleibt, 2 wird umgesetzt
        var neu = dienst.Speichern(b, Sitzplandienst.Raum, "R105|LAN", new SitzplanEingabe(4, 4, null, new() { ["2-2"] = 3, ["1-1"] = 2 }, null), new HashSet<int> { 2, 3 });
        Assert.Equal(new Dictionary<string, int> { ["2-2"] = 3, ["1-1"] = 2 }, neu.Belegung);
        neu = dienst.Speichern(b, Sitzplandienst.Raum, "R105|LAN", new SitzplanEingabe(4, 4, null, new() { ["4-4"] = 3 }, null), new HashSet<int> { 3 });
        Assert.Equal(3, neu.Belegung["4-4"]);
        Assert.Equal(2, neu.Belegung["1-1"]);
        Assert.False(neu.Belegung.ContainsKey("2-2"));
    }

    [Fact]
    public async Task Druck_als_Pdf()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var ids = await SchuelerAsync(lan);
        Assert.Equal(HttpStatusCode.NotFound, (await lan.GetAsync("/api/app/sitzplaene/pdf?unterricht=147&raeume=R105&klassen=7a")).StatusCode);
        await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplaene", Plan("unterricht", new() { ["1-1"] = ids[0], ["1-2"] = ids[1] }, ohneTisch: new() { "2-3" })));
        foreach (var r in new[]
        {
            await lan.GetAsync("/api/app/sitzplaene/pdf?unterricht=147&raeume=R105&klassen=7a&klasseZeigen=true"),
            await lan.GetAsync("/api/app/sitzplaene/pdf?bereich=unterricht&unterricht=147&klassen=7a"),
            await lan.PostAsJsonAsync("/api/app/sitzplaene/pdf", new { bereich = "raum", unterricht = 147, raeume = new[] { "R105" }, klassen = new[] { "7a" }, zeilen = 3, spalten = 3, pult = "hinten",
                belegung = new Dictionary<string, int> { ["3-3"] = ids[5] }, kurz = true, titel = "Sitzplan Physik" }),
        })
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
            Assert.Contains("Lehrerpult", System.Text.Encoding.Latin1.GetString(bytes));
        }
    }

    [Fact]
    public async Task Klassenseite_nutzt_neue_Tabelle()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        var sitz = await Testserver.HolenAsync(lan, "/api/app/sitzplan/7a");
        Assert.True(sitz["verwalten"]!.GetValue<bool>());
        Assert.False(sitz["plan"]!["vorhanden"]!.GetValue<bool>());
        var erster = sitz["schueler"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await lan.PutAsJsonAsync("/api/app/sitzplan/7a", new { zeilen = 3, spalten = 3, pult = "hinten", belegung = new Dictionary<string, int> { ["2-2"] = erster } }));
        Assert.Equal(HttpStatusCode.Forbidden, (await mue.PutAsJsonAsync("/api/app/sitzplan/7a", new { zeilen = 3, spalten = 3 })).StatusCode);
        sitz = await Testserver.HolenAsync(mue, "/api/app/sitzplan/7a");
        Assert.False(sitz["verwalten"]!.GetValue<bool>());
        Assert.Equal("hinten", sitz["plan"]!["pult"]!.GetValue<string>());
        // derselbe Plan ist der Klassen-Sitzplan in den Stunden der 7a
        Assert.Equal(erster, (await Testserver.HolenAsync(lan, Stunde))["gueltig"]!["belegung"]!["2-2"]!.GetValue<int>());
    }

    [Fact]
    public void Update_uebernimmt_alte_Klassen_Sitzplaene()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "untplanweb-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        var stand = Array.FindIndex(Db.MigrationsSkripte.ToArray(), m => m.Contains("CREATE TABLE sitzplan_bereiche"));
        Assert.True(stand > 0);
        using (var c = new SqliteConnection($"Data Source={Path.Combine(ordner, "untplanweb.db")}"))
        {
            c.Open();
            Db.Ausfuehren(c, "CREATE TABLE schema_version (version INTEGER NOT NULL)");
            for (var i = 0; i < stand; i++)
            {
                Db.Ausfuehren(c, Db.MigrationsSkripte[i]);
                Db.Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", Db.P("@v", i + 1));
            }
            Db.Ausfuehren(c, "INSERT INTO sitzplaene (klasse, zeilen, spalten, belegung, geaendert) VALUES ('7a', 3, 4, '{\"1-1\":5}', '2026-09-01 08:00')");
        }
        SqliteConnection.ClearAllPools();
        var db = new Db(ordner);
        Assert.Equal(4L, Convert.ToInt64(db.Wert("SELECT spalten FROM sitzplan_bereiche WHERE bereich = 'klasse' AND schluessel = '7a'")));
        Assert.Equal("vorne", db.Wert("SELECT pult FROM sitzplan_bereiche WHERE bereich = 'klasse' AND schluessel = '7a'"));
        Assert.Equal("{\"1-1\":5}", db.Wert("SELECT belegung FROM sitzplan_bereiche WHERE bereich = 'klasse' AND schluessel = '7a'"));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Anzeigename_nach_Einstellungen()
    {
        var x = new UntPlan.Web.Contracts.WebSchueler(1, "Emma", "Schulz", "7a");
        Assert.Equal("Schulz Emma", Sitzplandienst.Anzeigename(x, false, false, false));
        Assert.Equal("Emma Schulz (7a)", Sitzplandienst.Anzeigename(x, true, false, true));
        Assert.Equal("Emma S.", Sitzplandienst.Anzeigename(x, false, true, false));
    }
}
