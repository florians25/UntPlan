using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Tests;

/// <summary>
/// Stundendetails nach den Mobile-Sollbildern: Trennzeichen (Kachel Komma, Details „ | “), Raumbuchung (ersetzen, hinzufügen,
/// löschen), Schülerliste (Sortierung, Dienste, gemeinsamer Klassenbucheintrag) und „Findet online statt“.
/// </summary>
public class StundendetailsTests
{
    private static IEnumerable<JsonNode> Kacheln(JsonNode woche, string datum) => woche["days"]![datum]!.AsArray().Select(k => k!);

    private static readonly Dictionary<string, string> Stunde = new()
    {
        // SNE: Mittwoch 1. Stunde Englisch in der 5a (Unterricht 107, Raum R101)
        ["date"] = "2026-10-14", ["from"] = "1", ["to"] = "1", ["class"] = "5a", ["unterricht"] = "107", ["subject"] = "E",
    };

    private static Dictionary<string, string> Mit(Dictionary<string, string> basis, params (string, string)[] werte) =>
        basis.Concat(werte.Select(w => new KeyValuePair<string, string>(w.Item1, w.Item2))).ToDictionary(x => x.Key, x => x.Value);

    private static async Task<JsonNode> EnglischAsync(HttpClient c)
    {
        var woche = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-14&type=mine");
        return Kacheln(woche, "2026-10-14").First(k => k["lesson"]!.GetValue<int>() == 1 && k["unterricht"]?.GetValue<int>() == 107);
    }

    private static List<string> Liste(JsonNode? n) => (n?.GetValue<string>() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public async Task Raumbuchung_ersetzen_hinzufuegen_und_loeschen()
    {
        await using var u = await Umgebung.StartenAsync();
        var sne = await u.AngemeldetAsync("sne");
        Assert.Equal("R101", (await EnglischAsync(sne))["room"]!.GetValue<string>());

        var frei = (await Umgebung.HolenAsync(sne, "/api/rooms?action=free&date=2026-10-14&from=1&to=1"))["rooms"]!.AsArray()
            .Select(r => r!["code"]!.GetValue<string>()).ToList();
        Assert.True(frei.Count >= 2);
        var (a, b) = (frei[0], frei[1]);

        // Ersetzen: R101 → a
        var r = await Umgebung.SendenAsync(sne, "/api/rooms", Mit(Stunde, ("action", "change"), ("op", "replace"), ("old", "R101"), ("room", a)));
        Assert.Equal(new[] { a }, r["rooms"]!.AsArray().Select(x => x!.GetValue<string>()));
        var k = await EnglischAsync(sne);
        Assert.Equal(a, k["room"]!.GetValue<string>());
        Assert.Equal("R101", k["roomOrig"]!.GetValue<string>());
        Assert.Contains("room", k["changed"]!.AsArray().Select(x => x!.GetValue<string>()));

        // Hinzufügen: zusätzlicher Raum b – beide Räume stehen auf der Kachel, R101 nur einmal als ursprünglicher Raum
        await Umgebung.SendenAsync(sne, "/api/rooms", Mit(Stunde, ("action", "change"), ("op", "add"), ("room", b)));
        k = await EnglischAsync(sne);
        Assert.Equal(new[] { a, b }.OrderBy(x => x), Liste(k["room"]).OrderBy(x => x));
        Assert.Equal("R101", k["roomOrig"]!.GetValue<string>());
        // je Lehrkraft und Stunde höchstens eine offene Raumänderung
        Assert.Equal(1, u.OffeneRaumbuchungen());

        // Löschen: a fällt weg, b bleibt
        await Umgebung.SendenAsync(sne, "/api/rooms", Mit(Stunde, ("action", "change"), ("op", "remove"), ("room", a)));
        Assert.Equal(new[] { b }, Liste((await EnglischAsync(sne))["room"]));

        // Den letzten Raum löschen geht nicht – die Meldung kommt durch
        var nein = await sne.PostAsync("/api/rooms", new FormUrlEncodedContent(Mit(Stunde, ("action", "change"), ("op", "remove"), ("room", b))));
        Assert.Equal(HttpStatusCode.BadRequest, nein.StatusCode);
        Assert.Contains("mindestens einen Raum", await nein.Content.ReadAsStringAsync());

        // Zurück auf den ursprünglichen Raum: die Raumänderung wird zurückgezogen
        r = await Umgebung.SendenAsync(sne, "/api/rooms", Mit(Stunde, ("action", "change"), ("op", "replace"), ("old", b), ("room", "R101")));
        Assert.True(r["reset"]!.GetValue<bool>());
        k = await EnglischAsync(sne);
        Assert.Equal("R101", k["room"]!.GetValue<string>());
        Assert.DoesNotContain("room", k["changed"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(0, u.OffeneRaumbuchungen());

        // Belegter Raum (R102: Unterricht der 5b) wird abgewiesen, Klassenkonten dürfen nicht
        var belegt = await sne.PostAsync("/api/rooms", new FormUrlEncodedContent(Mit(Stunde, ("action", "change"), ("op", "add"), ("room", "R102"))));
        Assert.Equal(HttpStatusCode.BadRequest, belegt.StatusCode);
        var klasse = await u.AngemeldetAsync("5a");
        Assert.Equal(HttpStatusCode.Forbidden, (await klasse.PostAsync("/api/rooms", new FormUrlEncodedContent(Mit(Stunde, ("action", "change"), ("op", "add"), ("room", a))))).StatusCode);
    }

    [Fact]
    public async Task Schuelerliste_Dienste_gemeinsamer_Eintrag_und_online()
    {
        await using var u = await Umgebung.StartenAsync();
        var sne = await u.AngemeldetAsync("sne");
        var schluessel = new Dictionary<string, string> { ["date"] = "2026-10-14", ["lesson"] = "1", ["class"] = "5a", ["unterricht"] = "107" };
        string Abfrage(string aktion) => "/api/classbook?" + string.Join("&", schluessel.Append(new("action", aktion)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        var alles = await Umgebung.HolenAsync(sne, Abfrage("all"));
        Assert.True(alles["canEdit"]!.GetValue<bool>());
        Assert.False(alles["online"]!.GetValue<bool>());
        var schueler = alles["students"]!.AsArray().Select(s => s!).ToList();
        // „Nachname Vorname“ ohne Komma, alphabetisch
        var namen = schueler.Select(s => s["name"]!.GetValue<string>()).ToList();
        Assert.All(namen, n => Assert.DoesNotContain(",", n));
        Assert.Equal(namen.OrderBy(n => n, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true)), namen);

        // Klassendienst (Klassensprecher*in) steht als Zusatz unter dem Namen
        var erster = schueler[0]["id"]!.GetValue<int>();
        var zweiter = schueler[1]["id"]!.GetValue<int>();
        u.KlassendienstAnlegen("Klassensprecher*in", erster, "5a");
        alles = await Umgebung.HolenAsync(sne, Abfrage("all"));
        Assert.Contains("Klassensprecher*in", alles["students"]!.AsArray().Single(s => s!["id"]!.GetValue<int>() == erster)!["duties"]!.AsArray().Select(x => x!.GetValue<string>()));

        // Abwesend setzen – Zahl der Abwesenden für das rote Feld
        await Umgebung.SendenAsync(sne, "/api/classbook", Mit(schluessel, ("action", "attendance"), ("student_id", zweiter.ToString()), ("state", "absent")));
        Assert.Equal(1, (await Umgebung.HolenAsync(sne, Abfrage("attSummary")))["absent"]!.GetValue<int>());

        // Gemeinsamer Klassenbucheintrag für zwei Schüler*innen
        var eintrag = await Umgebung.SendenAsync(sne, "/api/classbook", Mit(schluessel, ("action", "remarkMany"), ("student_ids", $"{erster},{zweiter}"), ("type", "Lob"), ("text", "Gute Präsentation")));
        Assert.Equal(2, eintrag["count"]!.GetValue<int>());
        Assert.Equal("Gute Präsentation", eintrag["remarks"]![erster.ToString()]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Gute Präsentation", eintrag["remarks"]![zweiter.ToString()]![0]!["text"]!.GetValue<string>());
        var leer = await sne.PostAsync("/api/classbook", new FormUrlEncodedContent(Mit(schluessel, ("action", "remarkMany"), ("student_ids", ""), ("text", "x"))));
        Assert.Equal(HttpStatusCode.BadRequest, leer.StatusCode);

        // Findet online statt
        await Umgebung.SendenAsync(sne, "/api/classbook", Mit(schluessel, ("action", "online"), ("value", "1")));
        Assert.True((await Umgebung.HolenAsync(sne, Abfrage("all")))["online"]!.GetValue<bool>());

        // Fremde Stunde: kein Klassenbuch zum Bearbeiten
        var fremd = await Umgebung.HolenAsync(sne, "/api/classbook?action=all&date=2026-10-14&lesson=2&class=5a&unterricht=106");
        Assert.False(fremd["canEdit"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public async Task Abwesenheit_minutengenau_mit_Grund_Liste_je_Schueler_und_Loeschen()
    {
        await using var u = await Umgebung.StartenAsync();
        var sne = await u.AngemeldetAsync("sne");
        var schluessel = new Dictionary<string, string> { ["date"] = "2026-10-14", ["lesson"] = "1", ["class"] = "5a", ["unterricht"] = "107" };
        string Abfrage(string aktion, string extra = "") => "/api/classbook?" + string.Join("&", schluessel.Append(new("action", aktion)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")) + extra;

        // Gründe kommen aus UntPlanWeb
        var gruende = (await Umgebung.HolenAsync(sne, Abfrage("reasons")))["reasons"]!.AsArray();
        Assert.Contains(gruende, g => g!["code"]!.GetValue<string>() == "K" && g["name"]!.GetValue<string>() == "Krankheit");

        var alles = await Umgebung.HolenAsync(sne, Abfrage("all"));
        var id = alles["students"]![0]!["id"]!.GetValue<int>();
        var beginn = alles["students"]![0]!["from_time"]; // vorher leer
        Assert.Equal("", beginn!.GetValue<string>());

        // Ab 08:10 bis Stundenende abwesend (vorzeitig gegangen), Grund Krankheit, Text
        await Umgebung.SendenAsync(sne, "/api/classbook", Mit(schluessel, ("action", "attendance"), ("student_id", id.ToString()), ("state", "absent"),
            ("from_date", "2026-10-14"), ("from_time", "08:10"), ("to_date", "2026-10-14"), ("to_time", "08:40"), ("reason", "K"), ("note", "Übelkeit"), ("notified", "0")));
        var s = (await Umgebung.HolenAsync(sne, Abfrage("students")))["students"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == id)!;
        Assert.Equal("absent", s["state"]!.GetValue<string>());
        Assert.Equal("K", s["reason"]!.GetValue<string>());
        Assert.Equal("08:10", s["from_time"]!.GetValue<string>());
        Assert.Equal("Übelkeit", s["note"]!.GetValue<string>());

        // Abwesenheiten der Schülerin / des Schülers
        var liste = await Umgebung.HolenAsync(sne, Abfrage("absences", $"&student_id={id}"));
        var eintrag = liste["items"]!.AsArray().Single()!;
        Assert.Equal("Krankheit", eintrag["reasonName"]!.GetValue<string>());
        Assert.Equal("08:10", eintrag["fromTime"]!.GetValue<string>());
        Assert.True(eintrag["current"]!.GetValue<bool>());

        // Ende vor Beginn wird abgewiesen
        var falsch = await sne.PostAsync("/api/classbook", new FormUrlEncodedContent(Mit(schluessel, ("action", "attendance"), ("student_id", id.ToString()), ("state", "absent"),
            ("from_date", "2026-10-14"), ("from_time", "08:30"), ("to_date", "2026-10-14"), ("to_time", "08:00"))));
        Assert.Equal(HttpStatusCode.BadRequest, falsch.StatusCode);

        // Löschen
        await Umgebung.SendenAsync(sne, "/api/classbook", Mit(schluessel, ("action", "absenceDelete"), ("student_id", id.ToString()), ("current", "1")));
        Assert.Equal(0, (await Umgebung.HolenAsync(sne, Abfrage("attSummary")))["absent"]!.GetValue<int>());
        Assert.Empty((await Umgebung.HolenAsync(sne, Abfrage("absences", $"&student_id={id}")))["items"]!.AsArray());
    }

    [Fact]
    public async Task Meine_Abwesenheiten_fuer_Schuelerkonto()
    {
        await using var u = await Umgebung.StartenAsync();
        var klasse = await u.AngemeldetAsync("5a");
        var d = await Umgebung.HolenAsync(klasse, "/api/home?action=absences");
        Assert.True(d["ok"]!.GetValue<bool>());
        Assert.True(d["student"]!.GetValue<bool>());
        Assert.NotNull(d["from"]);
        Assert.NotNull(d["items"]!.AsArray());
    }

    /// <summary>
    /// Trennzeichen der Oberfläche: Der Abschnitt TRENNER aus app.js wird mit Node ausgeführt (ohne Node nur die statische Prüfung).
    /// Auf den Stundencards trennt ein Komma (auch mehrere Lehrkräfte), in den Stundendetails „ | “.
    /// </summary>
    [Fact]
    public void Trennzeichen_Kachel_Komma_Details_senkrechter_Strich()
    {
        var js = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
        var beginn = js.IndexOf("/* TRENNER-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf("/* TRENNER-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn);
        var abschnitt = js[beginn..ende];
        Assert.Contains("const KACHEL_TRENNER = ', ';", abschnitt);
        Assert.Contains("const DETAIL_TRENNER = ' | ';", abschnitt);
        // Auf den Kacheln kein „ | “ mehr (Lehrerleiste, Veranstaltungen)
        Assert.DoesNotContain("join(' | ')", js);
        Assert.DoesNotContain("<i class=\"tok__trenn\"> | </i>", js);

        string? node = null;
        foreach (var kandidat in new[] { "node", "/opt/node22/bin/node", "/usr/bin/node", "/usr/local/bin/node" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(kandidat, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
                p!.WaitForExit(5000);
                if (p.ExitCode == 0) { node = kandidat; break; }
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        if (node is null) return;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-trenner-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, "const window = {};\n" + abschnitt + """

            const t = window.UntPlanTrenner;
            const pruefe = (ist, soll) => { if (ist !== soll) { console.error(JSON.stringify(ist) + ' statt ' + JSON.stringify(soll)); process.exit(1); } };
            pruefe(t.kachelWert('SWA,SNE'), 'SWA, SNE');
            pruefe(t.kachelWert('SWA | SNE'), 'SWA, SNE');
            pruefe(t.kachelWert('R101'), 'R101');
            pruefe(t.kachelWert(''), '');
            pruefe(t.detailWert('SWA,SNE'), 'SWA | SNE');
            pruefe(t.detailWert('5a, 5b,6a'), '5a | 5b | 6a');
            pruefe(t.detailWert('---'), '---');
            console.log('ok');
            """);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Assert.True(p.ExitCode == 0, aus);
            Assert.Contains("ok", aus);
        }
        finally { File.Delete(skript); }
    }
}
