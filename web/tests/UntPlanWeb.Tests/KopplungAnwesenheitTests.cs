using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Anwesenheitskontrolle im gekoppelten Unterricht: alle Schüler*innen aller beteiligten Klassen – gleich, ob die Stunde aus dem
/// Lehrerplan, aus einem der Klassenpläne oder von der Schulleitung geöffnet wird; Fehlzeiten stehen bei der Klasse (und dem Unterricht)
/// der Schülerin bzw. des Schülers, die Kontrolle gilt für alle Klassen.
/// </summary>
public class KopplungAnwesenheitTests
{
    private const string Datum = "2026-10-14";

    /// <summary>
    /// Beispielpaket mit zwei Kopplungen am 14.10.:
    /// 9. Stunde – getrennte Unterrichte 9001 (5a) und 9002 (5b) bei MUE, Fach INF, Raum EDV1 (Koppelklassen);
    /// 10. Stunde – ein Unterricht 9003 mit zwei Kopplungszeilen SP/RIC (6a) und SP/KOC (6b) in TH1.
    /// </summary>
    private static async Task<Testserver> StartenAsync()
    {
        var s = await Testserver.StartenAsync(mitPaket: false);
        var paket = Testserver.Beispielpaket();
        var periode = paket["perioden"]![0]!["unterricht"]!.AsArray();
        JsonObject Zeile(string fach, string lehrer, string klasse) => new() { ["fach"] = fach, ["lehrer"] = lehrer, ["klassen"] = new JsonArray(klasse), ["gruppe"] = "" };
        JsonObject Unterricht(int id, params JsonObject[] zeilen) => new() { ["id"] = id, ["wochenstunden"] = 1, ["zeilen"] = new JsonArray(zeilen), ["wochen"] = "", ["kurs"] = "", ["text"] = "" };
        periode.Add(Unterricht(9001, Zeile("INF", "MUE", "5a")));
        periode.Add(Unterricht(9002, Zeile("INF", "MUE", "5b")));
        periode.Add(Unterricht(9003, Zeile("SP", "RIC", "6a"), Zeile("SP", "KOC", "6b")));
        var tag = paket["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == Datum)!["stunden"]!.AsArray();
        JsonObject Stunde(int nr, int u, string fach, string klasse, string lehrer, string raum) => new()
        {
            ["stunde"] = nr, ["unterricht"] = u, ["fach"] = fach, ["klassen"] = new JsonArray(klasse), ["lehrer"] = new JsonArray(lehrer),
            ["raeume"] = new JsonArray(raum), ["status"] = "regulaer", ["text"] = "",
        };
        tag.Add(Stunde(9, 9001, "INF", "5a", "MUE", "EDV1"));
        tag.Add(Stunde(9, 9002, "INF", "5b", "MUE", "EDV1"));
        tag.Add(Stunde(10, 9003, "SP", "6a", "RIC", "TH1"));
        tag.Add(Stunde(10, 9003, "SP", "6b", "KOC", "TH1"));
        var r = await s.PaketSendenAsync(paket);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return s;
    }

    private static string Url(int stunde, int unterricht) => $"/api/app/klassenbuch/anwesenheit?datum={Datum}&stunde={stunde}&unterricht={unterricht}";

    private static List<(int Id, string Klasse)> Schueler(JsonNode anw) =>
        anw["schueler"]!.AsArray().Select(x => (x!["id"]!.GetValue<int>(), x["klasse"]!.GetValue<string>())).ToList();

    [Fact]
    public async Task Getrennt_gekoppelte_Klassen_stehen_aus_jedem_Plan_vollstaendig_in_der_Anwesenheit()
    {
        await using var s = await StartenAsync();
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        var admin = await s.AlsAsync("admin");
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var tag = DateOnly.Parse(Datum);
        // Ausgangslage: Lehrerplan eine gemeinsame Karte, Klassenpläne je eine Karte mit der anderen Klasse als Mitklasse
        Assert.Contains(plan.Karten(stand, tag, Elementtyp.Lehrer, "MUE"), k => k.Von == 9 && k.Klassen.Select(w => w.K).Order().SequenceEqual(new[] { "5a", "5b" }));
        var k5b = Assert.Single(plan.Karten(stand, tag, Elementtyp.Klasse, "5b"), k => k.Von == 9);
        Assert.Equal(9002, k5b.Unterricht);

        var erwartet = stand.Schueler.Where(x => x.Klasse is "5a" or "5b").Select(x => x.Id).Order().ToList();
        Assert.Equal(48, erwartet.Count);
        // aus dem Lehrerplan (Unterricht der gemeinsamen Karte), aus dem Plan der 5b, als Schulleitung/Administration
        foreach (var (c, u) in new[] { (mue, 9001), (mue, 9002), (admin, 9001), (admin, 9002) })
        {
            var anw = await Testserver.HolenAsync(c, Url(9, u));
            Assert.Equal(erwartet, Schueler(anw).Select(x => x.Id).Order().ToList());
            Assert.True(anw["darf"]!.GetValue<bool>());
            Assert.Equal(new[] { "5a", "5b" }, anw["klassen"]!.AsArray().Select(x => x!.GetValue<string>()).Order());
        }

        // aus dem Plan der 5b speichern: Fehlzeiten für Schüler*innen beider Klassen, jeweils bei der eigenen Klasse und dem eigenen Unterricht
        var liste = Schueler(await Testserver.HolenAsync(mue, Url(9, 9002)));
        var a = liste.First(x => x.Klasse == "5a").Id;
        var b = liste.First(x => x.Klasse == "5b").Id;
        await Testserver.JsonAsync(await mue.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = Datum, stunde = 9, unterricht = 9002,
            eintraege = new object[] { new { schuelerId = a, art = "fehlt" }, new { schuelerId = b, art = "verspaetet", minuten = 5 } },
        }));
        var kb = s.Dienst<Klassenbuchdienst>();
        var fz = kb.FehlzeitenZeitraum(tag, tag).Where(f => f.Stunde == 9).ToList();
        Assert.Equal(("5a", 9001, "fehlt"), fz.Where(f => f.SchuelerId == a).Select(f => (f.Klasse, f.Unterricht, f.Art)).Single());
        Assert.Equal(("5b", 9002, "verspaetet"), fz.Where(f => f.SchuelerId == b).Select(f => (f.Klasse, f.Unterricht, f.Art)).Single());
        Assert.Single(kb.FehlzeitenZeitraum(tag, tag, "5a"), f => f.SchuelerId == a);
        Assert.Single(kb.FehlzeitenZeitraum(tag, tag, "5b"), f => f.SchuelerId == b);

        // aus dem Lehrerplan (anderer Unterricht) dieselben Fehlzeiten, Kontrolle für beide Unterrichte vermerkt
        var ausLehrerplan = await Testserver.HolenAsync(mue, Url(9, 9001));
        Assert.Equal(new[] { a, b }.Order(), ausLehrerplan["fehlzeiten"]!.AsArray().Select(f => f!["schuelerId"]!.GetValue<int>()).Order());
        Assert.NotNull(ausLehrerplan["geprueft"]?.GetValue<string>());
        var kontrolliert = kb.Zeitraum(tag, tag).Where(e => e.Stunde == 9 && e.Anwesenheit != null).Select(e => e.Unterricht).Order().ToList();
        Assert.Equal(new[] { 9001, 9002 }, kontrolliert);
        // im Klassenbuch beider Klassen sichtbar
        Assert.Contains(kb.Klasse("5a", tag, tag), e => e.Stunde == 9 && e.Anwesenheit != null);
        Assert.Contains(kb.Klasse("5b", tag, tag), e => e.Stunde == 9 && e.Anwesenheit != null);

        // erneut aus dem Lehrerplan speichern ersetzt die Fehlzeiten beider Klassen
        await Testserver.JsonAsync(await mue.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = Datum, stunde = 9, unterricht = 9001, eintraege = new object[] { new { schuelerId = b, art = "fehlt" } },
        }));
        fz = kb.FehlzeitenZeitraum(tag, tag).Where(f => f.Stunde == 9).ToList();
        Assert.Equal(("5b", 9002, "fehlt"), fz.Select(f => (f.Klasse, f.Unterricht, f.Art)).Single());
    }

    [Fact]
    public async Task Schuelergruppen_jedes_gekoppelten_Unterrichts_gelten()
    {
        await using var s = await StartenAsync();
        var admin = await s.AlsAsync("admin");
        var mue = await s.AlsAsync("mue", "lehrer", "MUE");
        var liste = Schueler(await Testserver.HolenAsync(mue, Url(9, 9001)));
        var weg = liste.First(x => x.Klasse == "5b").Id;
        var bleibt = liste.Last(x => x.Klasse == "5b").Id;
        // Abmeldung aus dem Unterricht der 5b (9002): fehlt auch in der aus dem Lehrerplan geöffneten Stunde (9001)
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/unterrichtklassen/9002/gruppen/0/schueler?datum={Datum}", new
        {
            schueler = new object[] { new { schuelerId = weg, auswahl = false }, new { schuelerId = bleibt, auswahl = true } },
        }));
        foreach (var u in new[] { 9001, 9002 })
        {
            var ids = Schueler(await Testserver.HolenAsync(mue, Url(9, u))).Select(x => x.Id).ToList();
            Assert.DoesNotContain(weg, ids);
            Assert.Contains(bleibt, ids);
            Assert.Equal(47, ids.Count);
        }
        // Fehlzeiten für Abgemeldete werden nicht angenommen
        await Testserver.JsonAsync(await mue.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = Datum, stunde = 9, unterricht = 9001, eintraege = new object[] { new { schuelerId = weg, art = "fehlt" } },
        }));
        Assert.Empty((await Testserver.HolenAsync(mue, Url(9, 9001)))["fehlzeiten"]!.AsArray());
    }

    [Fact]
    public async Task Kopplungszeilen_eines_Unterrichts_aus_jedem_Plan_mit_allen_Klassen()
    {
        await using var s = await StartenAsync();
        var admin = await s.AlsAsync("admin");
        var ric = await s.AlsAsync("ric", "lehrer", "RIC");
        var koc = await s.AlsAsync("koc", "lehrer", "KOC");
        var leitung = await s.AlsAsync("sl-kopplung", "leitung", "");
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var erwartet = stand.Schueler.Where(x => x.Klasse is "6a" or "6b").Select(x => x.Id).Order().ToList();
        Assert.Equal(48, erwartet.Count);
        foreach (var c in new[] { admin, ric, koc, leitung })
            Assert.Equal(erwartet, Schueler(await Testserver.HolenAsync(c, Url(10, 9003))).Select(x => x.Id).Order().ToList());

        // Schulleitung (ohne eigenen Plan) speichert Fehlzeiten beider Klassen
        var liste = Schueler(await Testserver.HolenAsync(leitung, Url(10, 9003)));
        var a = liste.First(x => x.Klasse == "6a").Id;
        var b = liste.First(x => x.Klasse == "6b").Id;
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = Datum, stunde = 10, unterricht = 9003, eintraege = new object[] { new { schuelerId = a, art = "fehlt" }, new { schuelerId = b, art = "fehlt" } },
        }));
        var tag = DateOnly.Parse(Datum);
        var fz = s.Dienst<Klassenbuchdienst>().FehlzeitenZeitraum(tag, tag).Where(f => f.Stunde == 10).Select(f => (f.SchuelerId, f.Klasse, f.Unterricht)).OrderBy(x => x.Klasse).ToList();
        Assert.Equal(new[] { (a, "6a", 9003), (b, "6b", 9003) }, fz);
        // Abwesenheitsdialog der Stunde für die Schülerin/den Schüler der anderen Klasse
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { b }, von = $"{Datum}T15:55", bis = $"{Datum}T16:40", datum = Datum, stunde = 10, unterricht = 9003, grund = "krank",
        }));
    }
}
