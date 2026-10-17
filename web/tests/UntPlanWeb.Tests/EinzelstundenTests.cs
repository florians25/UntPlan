using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Doppel-/Blockstunden: Einträge für den ganzen Block oder nur eine Einzelstunde (Parameter „einzelstunde“) – Lehrstoff, Notizen,
/// Hausaufgaben, Abwesenheiten und Abwesenheitskontrolle. Blockeinträge gelten für alle Stunden, Einzelstunden-Einträge nur für ihre Stunde.
/// </summary>
public class EinzelstundenTests
{
    private sealed record Block(string Datum, string Klasse, string Lehrer, int Von, int Bis, int Unterricht);

    /// <summary>Reguläre Doppel-/Blockstunde mit einer Lehrkraft am Montag/Dienstag der Beispielwoche (vor „heute“), möglichst mit Schülerliste.</summary>
    private static async Task<Block> BlockSuchenAsync(HttpClient admin)
    {
        var elemente = await Testserver.HolenAsync(admin, "/api/app/elemente");
        var klassen = elemente["klassen"]!.AsArray().Select(k => k!["kurz"]!.GetValue<string>()).ToList();
        Block? ohneSchueler = null;
        foreach (var datum in new[] { "2026-10-12", "2026-10-13" })
            foreach (var kl in klassen)
            {
                var plan = await Testserver.HolenAsync(admin, $"/api/app/plan?typ=klasse&element={kl}&datum={datum}");
                foreach (var k in plan["karten"]!.AsArray().Select(x => x!))
                {
                    if (k["datum"]?.GetValue<string>() != datum || k["status"]?.GetValue<string>() != "regulaer" || k["unterricht"] is null) continue;
                    var von = k["von"]!.GetValue<int>();
                    var bis = k["bis"]!.GetValue<int>();
                    var lehrer = k["lehrer"]!.AsArray().Select(l => l!["k"]!.GetValue<string>()).Where(x => x.Length > 0).ToList();
                    if (bis <= von || lehrer.Count != 1 || k["klassen"]!.AsArray().Count != 1) continue;
                    var b = new Block(datum, kl, lehrer[0], von, bis, k["unterricht"]!.GetValue<int>());
                    var anw = await Testserver.HolenAsync(admin, $"/api/app/klassenbuch/anwesenheit?datum={datum}&stunde={von}&unterricht={b.Unterricht}");
                    if (anw["schueler"]!.AsArray().Count > 0) return b;
                    ohneSchueler ??= b;
                }
            }
        return ohneSchueler ?? throw new InvalidOperationException("Keine Doppelstunde im Beispielpaket.");
    }

    private static string Q(Block b, string extra = "") => $"datum={b.Datum}&stunde={b.Von}&unterricht={b.Unterricht}{extra}";

    [Fact]
    public async Task Lehrstoff_fuer_Block_und_Einzelstunde_getrennt_mit_Auswahlliste()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var b = await BlockSuchenAsync(admin);
        var lk = await s.AlsAsync("lk-block", "lehrer", b.Lehrer);

        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, thema = "Block-Thema" }));
        var einzel = await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, thema = "Nur letzte Stunde", einzelstunde = b.Bis }));
        Assert.Equal(b.Bis, einzel["einzelstunde"]!.GetValue<int>());
        Assert.Equal(b.Bis, einzel["stunde"]!.GetValue<int>());

        // ganzer Block (wie bisher): Blockeintrag, dazu Block, Einzelstunden für die Auswahl und die Einzelstunden-Einträge
        var block = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b)}");
        Assert.Equal("Block-Thema", block["eintrag"]!["thema"]!.GetValue<string>());
        Assert.Null(block["eintrag"]!["einzelstunde"]);
        Assert.Null(block["einzelstunde"]);
        Assert.Equal(b.Von, block["block"]!["von"]!.GetValue<int>());
        Assert.Equal(b.Bis, block["block"]!["bis"]!.GetValue<int>());
        var stunden = block["einzelstunden"]!.AsArray();
        Assert.Equal(b.Bis - b.Von + 1, stunden.Count);
        Assert.Matches(@"^\d{2}:\d{2}$", stunden[0]!["beginn"]!.GetValue<string>());
        Assert.True(stunden[^1]!["eintrag"]!.GetValue<bool>());
        Assert.False(stunden[0]!["eintrag"]!.GetValue<bool>());
        Assert.Equal("Nur letzte Stunde", block["einzelEintraege"]!.AsArray().Single()!["thema"]!.GetValue<string>());

        // Einzelstunde: eigener Eintrag, Blockeintrag gilt mit
        var letzte = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b, $"&einzelstunde={b.Bis}")}");
        Assert.Equal("Nur letzte Stunde", letzte["eintrag"]!["thema"]!.GetValue<string>());
        Assert.Equal("Block-Thema", letzte["blockEintrag"]!["thema"]!.GetValue<string>());
        Assert.Equal(b.Bis, letzte["einzelstunde"]!.GetValue<int>());
        var erste = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b, $"&einzelstunde={b.Von}")}");
        Assert.Null(erste["eintrag"]);
        Assert.True(erste["darf"]!.GetValue<bool>());

        // Einzelstunde außerhalb des Blocks
        Assert.Equal(HttpStatusCode.BadRequest, (await lk.GetAsync($"/api/app/klassenbuch/stunde?{Q(b, $"&einzelstunde={b.Bis + 1}")}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, thema = "x", einzelstunde = b.Von - 1 })).StatusCode);

        // Listen, Berichte und der Abgleich mit UntPlan sehen weiterhin nur den Blockeintrag; die Klassenansicht zeigt beide
        var kb = s.Dienst<Klassenbuchdienst>();
        var tag = DateOnly.Parse(b.Datum);
        Assert.Equal("Block-Thema", kb.Zeitraum(tag, tag).Single(e => e.Unterricht == b.Unterricht).Thema);
        Assert.Equal(2, kb.Zeitraum(tag, tag, true).Count(e => e.Unterricht == b.Unterricht));
        var klasse = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/klasse?klasse={b.Klasse}&von={b.Datum}&bis={b.Datum}");
        Assert.Contains(klasse.AsArray(), e => e!["einzelstunde"]?.GetValue<int>() == b.Bis && e["thema"]!.GetValue<string>() == "Nur letzte Stunde");
    }

    [Fact]
    public async Task Notizen_und_Online_einer_Einzelstunde_mit_Stunde_auf_der_Karte()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var b = await BlockSuchenAsync(admin);
        var lk = await s.AlsAsync("lk-block", "lehrer", b.Lehrer);

        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, notizSchueler = "Zirkel mitbringen", einzelstunde = b.Bis }));
        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, notizSchueler = "Heft abgeben" }));
        Assert.Equal(HttpStatusCode.OK, (await lk.PutAsJsonAsync("/api/app/klassenbuch/online", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, online = true, onlineLink = "https://video.example/raum", einzelstunde = b.Bis })).StatusCode);

        var plan = await Testserver.HolenAsync(lk, $"/api/app/plan?typ=klasse&element={b.Klasse}&datum={b.Datum}");
        var karte = plan["karten"]!.AsArray().Select(k => k!).Single(k => k["datum"]!.GetValue<string>() == b.Datum && k["unterricht"]?.GetValue<int>() == b.Unterricht && k["von"]!.GetValue<int>() == b.Von);
        var notiz = karte["notizSchueler"]!.GetValue<string>();
        Assert.Contains("Heft abgeben", notiz);
        Assert.Contains($"{b.Bis}. Std.: Zirkel mitbringen", notiz);
        Assert.True(karte["online"]!.GetValue<bool>());

        // der Blockeintrag bleibt offline; die Einzelstunde ist online
        var block = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b)}");
        Assert.False(block["eintrag"]!["online"]!.GetValue<bool>());
        var letzte = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b, $"&einzelstunde={b.Bis}")}");
        Assert.True(letzte["eintrag"]!["online"]!.GetValue<bool>());
        Assert.Equal("https://video.example/raum", letzte["eintrag"]!["onlineLink"]!.GetValue<string>());
        Assert.Equal("Zirkel mitbringen", letzte["eintrag"]!["notizSchueler"]!.GetValue<string>());
    }

    [Fact]
    public async Task Hausaufgaben_des_Blocks_gelten_fuer_jede_Stunde_Einzelstunde_nur_fuer_ihre()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var b = await BlockSuchenAsync(admin);
        var lk = await s.AlsAsync("lk-block", "lehrer", b.Lehrer);

        await Testserver.JsonAsync(await lk.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, text = "HA Block", faellig = (string?)null, bemerkung = "" }));
        var hw = await Testserver.JsonAsync(await lk.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, text = "HA einzeln", faellig = (string?)null, bemerkung = "", einzelstunde = b.Bis }));
        Assert.Equal(b.Bis, hw["einzelstunde"]!.GetValue<int>());

        var basis = $"/api/app/klassenbuch/hausaufgabenliste?{Q(b)}&bis={b.Bis}";
        var alle = (await Testserver.HolenAsync(lk, basis))["liste"]!.AsArray();
        Assert.Equal(2, alle.Count);
        Assert.Contains(alle, h => h!["einzelstunde"]?.GetValue<int>() == b.Bis);
        var erste = (await Testserver.HolenAsync(lk, $"{basis}&einzelstunde={b.Von}"))["liste"]!.AsArray();
        Assert.Equal("HA Block", erste.Single()!["text"]!.GetValue<string>());
        Assert.Equal(2, (await Testserver.HolenAsync(lk, $"{basis}&einzelstunde={b.Bis}"))["liste"]!.AsArray().Count);

        // Feld „Hausaufgabe“ je Eintrag: Block nur mit der Blockhausaufgabe, Einzelstunde mit ihrer
        var block = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b)}");
        Assert.Equal("HA Block", block["eintrag"]!["hausaufgabe"]!.GetValue<string>());
        var letzte = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?{Q(b, $"&einzelstunde={b.Bis}")}");
        Assert.Equal("HA einzeln", letzte["eintrag"]!["hausaufgabe"]!.GetValue<string>());

        // Ändern und Löschen über die Id bleiben gleich
        Assert.Equal("HA neu", (await Testserver.JsonAsync(await lk.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{hw["id"]}", new { text = "HA neu" })))["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await lk.DeleteAsync($"/api/app/klassenbuch/hausaufgabenliste/{hw["id"]}")).StatusCode);
        Assert.Single((await Testserver.HolenAsync(lk, basis))["liste"]!.AsArray());
    }

    [Fact]
    public async Task Abwesenheit_und_Kontrolle_einer_Einzelstunde()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var b = await BlockSuchenAsync(admin);
        var lk = await s.AlsAsync("lk-block", "lehrer", b.Lehrer);
        var anw = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/anwesenheit?{Q(b)}");
        var schueler = anw["schueler"]!.AsArray();
        Assert.True(schueler.Count > 0, "Klasse ohne Schülerliste");
        var sid = schueler[0]!["id"]!.GetValue<int>();

        // nur in der letzten Stunde des Blocks abwesend
        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, einzelstunde = b.Bis, eintraege = new[] { new { schuelerId = sid, art = "fehlt", minuten = 0 } },
        }));
        var letzte = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/anwesenheit?{Q(b, $"&einzelstunde={b.Bis}")}");
        Assert.Equal(b.Bis, letzte["einzelstunde"]!.GetValue<int>());
        Assert.Equal(sid, letzte["fehlzeiten"]!.AsArray().Single()!["schuelerId"]!.GetValue<int>());
        Assert.NotNull(letzte["geprueft"]);
        Assert.Single(letzte["laengen"]!.AsArray());
        var erste = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/anwesenheit?{Q(b, $"&einzelstunde={b.Von}")}");
        Assert.Empty(erste["fehlzeiten"]!.AsArray());
        Assert.Null(erste["geprueft"]);
        var fz = s.Dienst<Klassenbuchdienst>().FehlzeitenZeitraum(DateOnly.Parse(b.Datum), DateOnly.Parse(b.Datum)).Where(f => f.SchuelerId == sid).ToList();
        Assert.Equal(b.Bis, fz.Single().Stunde);

        // Block: noch nicht kontrolliert (nur eine Einzelstunde); nach Kontrolle jeder Einzelstunde gilt der Block als kontrolliert
        Assert.Null((await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/anwesenheit?{Q(b)}"))["geprueft"]);
        for (var n = b.Von; n < b.Bis; n++)
            await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, einzelstunde = n, eintraege = Array.Empty<object>() }));
        var block = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/anwesenheit?{Q(b)}");
        Assert.NotNull(block["geprueft"]);
        Assert.Single(block["fehlzeiten"]!.AsArray());

        // Kontrolle des Blocks gilt für jede Einzelstunde
        await using var s2 = await Testserver.StartenAsync();
        var lk2 = await s2.AlsAsync("lk-block", "lehrer", b.Lehrer);
        await Testserver.JsonAsync(await lk2.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, eintraege = Array.Empty<object>() }));
        Assert.NotNull((await Testserver.HolenAsync(lk2, $"/api/app/klassenbuch/anwesenheit?{Q(b, $"&einzelstunde={b.Von}")}"))["geprueft"]);

        // Abwesenheit einer Einzelstunde entfernen lässt die anderen Stunden unberührt
        Assert.Equal(1, (await Testserver.JsonAsync(await lk.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/loeschen", new { datum = b.Datum, stunde = b.Von, unterricht = b.Unterricht, schuelerIds = new[] { sid }, einzelstunde = b.Bis })))["geloescht"]!.GetValue<int>());
    }

    [Fact]
    public void Einzeln_schraenkt_die_Karte_auf_eine_Stunde_ein()
    {
        var karte = new Karte("2026-10-12", 3, 4, "09:35", "11:05", "regulaer", "M", null, "Mathematik", "", new(), new(), new(), "", "", null, 42, "");
        var stand = new Stand(System.Text.Json.JsonSerializer.Deserialize<UntPlan.Web.Contracts.WebPaket>(Testserver.Beispielpaket().ToJsonString(), UntPlan.Web.Contracts.UntPlanWebSchema.Json)!, "");
        var vierte = Klassenbuchdienst.Einzeln(stand, karte, 4);
        Assert.Equal((4, 4, 4), (vierte.Von, vierte.Bis, vierte.Einzelstunde!.Value));
        Assert.Equal(-4, Klassenbuchdienst.Schluessel(vierte));
        Assert.Equal(3, Klassenbuchdienst.Schluessel(karte));
        Assert.Same(karte, Klassenbuchdienst.Einzeln(stand, karte, null));
        Assert.Throws<ArgumentException>(() => Klassenbuchdienst.Einzeln(stand, karte, 5));
        var einzelstunde = karte with { Bis = 3 };
        Assert.Same(einzelstunde, Klassenbuchdienst.Einzeln(stand, einzelstunde, 3));
    }
}
