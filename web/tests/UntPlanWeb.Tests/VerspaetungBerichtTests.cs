using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// „Aufgestanden“ (Bett mit Pfeil nach oben in der Schülerliste) und die genaue Dokumentation von Verspätungen in den Berichten:
/// Datum, Stunde, Uhrzeit von–bis, Minuten, Fach, Lehrkraft, Grund, Text und Status; Summen mit Anzahl und Minuten.
/// </summary>
public class VerspaetungBerichtTests
{
    private const string Tag = "2026-10-14";

    /// <summary>Server mit einer Stunde der 7a, in der eine Schülerin bzw. ein Schüler um Stundenbeginn + 12 Minuten „aufgestanden“ ist.</summary>
    private static async Task<(Testserver S, Karte K, int Sid, string Beginn, string Ankunft, HttpClient Lehrkraft, HttpClient Leitung)> MitVerspaetungAsync()
    {
        var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var d = DateOnly.Parse(Tag);
        var k = s.Dienst<Plandienst>().Karten(stand, d, Elementtyp.Klasse, "7a")
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0 && x.Von == x.Bis).OrderBy(x => x.Von).First();
        var beginn = Klassenbuchdienst.StundenZeit(stand, k.Von).Beginn;
        // laufende Stunde: 12 Minuten nach Stundenbeginn
        s.Dienst<Uhr>().Fest = d.ToDateTime(beginn.AddMinutes(12));
        var c = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
        var kl = await s.AlsAsync("sl-test", "leitung", "");
        var anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={Tag}&stunde={k.Von}&unterricht={k.Unterricht}");
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        // als abwesend erfasst (mit Grund und Text), dann „Aufgestanden“
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { sid }, von = $"{Tag}T{beginn:HH\\:mm}", bis = $"{Tag}T{Klassenbuchdienst.StundenZeit(stand, k.Bis).Ende:HH\\:mm}",
            grund = "", text = "verschlafen", datum = Tag, stunde = k.Von, unterricht = k.Unterricht,
        }));
        var r = await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/aufgestanden", new { datum = Tag, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { sid } }));
        Assert.Equal(12, r["minuten"]!.GetValue<int>());
        return (s, k, sid, beginn.ToString("HH:mm"), beginn.AddMinutes(12).ToString("HH:mm"), c, kl);
    }

    [Fact]
    public async Task Aufgestanden_setzt_anwesend_ab_jetzt_mit_Verspaetung_von_Stundenbeginn()
    {
        var (s, k, sid, beginn, ankunft, c, _) = await MitVerspaetungAsync();
        await using var _s = s;
        var anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={Tag}&stunde={k.Von}&unterricht={k.Unterricht}");
        var f = anw["fehlzeiten"]!.AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == sid)!;
        Assert.Equal(("verspaetet", 12), (f["art"]!.GetValue<string>(), f["minuten"]!.GetValue<int>()));
        Assert.Equal((beginn, ankunft), (f["vonZeit"]!.GetValue<string>(), f["bisZeit"]!.GetValue<string>()));
        Assert.Equal("verschlafen", f["text"]!.GetValue<string>());

        // nicht (mehr) abwesend bzw. außerhalb der Stunde: Fehler
        var nochmal = await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/aufgestanden", new { datum = Tag, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { sid } });
        Assert.Equal(HttpStatusCode.BadRequest, nochmal.StatusCode);
        var andere = anw["schueler"]![1]!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/aufgestanden", new { datum = Tag, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { andere } })).StatusCode);
        s.Dienst<Uhr>().Fest = DateOnly.Parse(Tag).ToDateTime(new TimeOnly(23, 0));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/aufgestanden", new { datum = Tag, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { sid } })).StatusCode);
    }

    [Fact]
    public async Task Berichte_dokumentieren_eine_Verspaetung_von_12_Minuten_genau()
    {
        var (s, k, sid, beginn, ankunft, _, kl) = await MitVerspaetungAsync();
        await using var _s = s;
        var q = $"klasse=7a&von={Tag}&bis={Tag}";
        string Alles(JsonNode b) => string.Join("|", b["zeilen"]!.AsArray().SelectMany(z => z!.AsArray()).Select(x => x?.ToString() ?? ""));

        // Verspätungen und vorzeitiges Gehen: Datum, Stunde, Uhrzeit, Minuten, Fach, Lehrkraft, Status, Text – und Summen
        var v = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/verspaetungen?{q}");
        var zeile = v["zeilen"]!.AsArray().Select(z => z!.AsArray().Select(x => x?.ToString() ?? "").ToList()).First(z => z[8] == "12 Min.");
        Assert.Equal(new[] { "14.10.2026", $"{k.Von}.", k.Fach, k.Lehrer[0].K, "verspätet", $"{beginn}–{ankunft}", "12 Min." }, zeile.Skip(2).Take(7));
        Assert.Equal(("offen", "verschlafen"), (zeile[10], zeile[11]));
        Assert.Contains("1 Verspätung, 12 Min.", Alles(v));
        var csv = await kl.GetStringAsync($"/api/app/berichte/erstellen/verspaetungen?{q}&format=csv");
        Assert.Contains($"{beginn}–{ankunft}", csv);
        Assert.Contains("12 Min.", csv);
        Assert.Contains("verschlafen", csv);
        var pdf = await kl.GetByteArrayAsync($"/api/app/berichte/erstellen/verspaetungen?{q}&format=pdf");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        // Fehlzeiten pro Schüler*in pro Stunde: Uhrzeit und „verspätet 12 Min.“, Summenzeile mit Anzahl und Minuten
        var ps = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&schueler={sid}&ausgabe=pro_stunde");
        Assert.Contains($"{beginn}–{ankunft}", Alles(ps));
        Assert.Contains("verspätet 12 Min.", Alles(ps));
        Assert.Contains("verschlafen", Alles(ps));
        Assert.Contains("1 Verspätung, 12 Min.", Alles(ps));
        // Haken „Verspätungen“ aus: die Verspätung fehlt
        var ohne = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&schueler={sid}&ausgabe=pro_stunde&verspaetungen=0");
        Assert.DoesNotContain("verspätet 12 Min.", Alles(ohne));
        // pro Tag
        Assert.Contains($"{k.Von}. Std. {k.Fach}: verspätet {beginn}–{ankunft} (12 Min.)", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&schueler={sid}&ausgabe=pro_tag")));

        // Fehlzeiten pro Klasse (Gesamtübersicht): Anzahl und Minuten der Verspätungen; ohne Haken leer
        var g = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_klasse?{q}&alle=1");
        var sp = g["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        var iV = sp.IndexOf("Verspätungen");
        var name = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Id == sid).Name;
        var gz = g["zeilen"]!.AsArray().Select(z => z!.AsArray()).First(z => z[0]!.ToString().StartsWith(name));
        Assert.Equal(("1", "12"), (gz[iV]!.ToString(), gz[iV + 1]!.ToString()));
        var g0 = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_klasse?{q}&alle=1&verspaetungen=0");
        Assert.Equal("", g0["zeilen"]!.AsArray().Select(z => z!.AsArray()).First(z => z[0]!.ToString().StartsWith(name))[iV]!.ToString());

        // Fehlzeiten pro Schüler*in (Gesamtübersicht), pro Fach, Abwesenheiten, Statusübersicht, Anwesenheiten, Auffällige Verspätungen
        Assert.Contains("12", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&schueler={sid}&ausgabe=gesamt")).Split('|'));
        var fach = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_fach?{q}");
        Assert.Contains(fach["zeilen"]!.AsArray(), z => z![1]!.ToString() == k.Fach && z[7]!.ToString() == "1" && z[8]!.ToString() == "12");
        var abw = Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/abwesenheiten?{q}"));
        Assert.Contains("verspätet 12 Min.", abw);
        Assert.Contains($"{beginn}–{ankunft}", abw);
        Assert.DoesNotContain("verspätet 12 Min.", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/abwesenheiten?{q}&verspaetungen=0")));
        Assert.Contains(Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/statusuebersicht?{q}")).Split('|'), x => x == "12");
        Assert.Contains(Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/anwesenheiten?{q}")).Split('|'), x => x == "12");
        Assert.Contains($"verspätet {beginn}–{ankunft} (12 Min.)", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/verspaetungen_auffaellig?{q}&n1=1")));

        // Schuljahresende: Lehrnachweis (Wochenübersicht mit Abwesenheiten) und Tagesübersicht
        Assert.Contains("(verspätet", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/wochenuebersicht?{q}&mitabw=1")));
        Assert.Contains($"12 min, {beginn}–{ankunft}", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/tagesuebersicht?{q}")));

        // Unterrichtsberichte: pro Stunde, pro Schüler*in, Arbeitsbericht, Fehlstundenmatrix
        var u = $"von={Tag}&bis={Tag}&unterricht={k.Unterricht}";
        Assert.Contains("(12 Min.)", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_unterricht_stunde?{u}")));
        var us = Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_unterricht_schueler?{u}"));
        Assert.Contains("(12 Min.)", us);
        Assert.Contains("1 Verspätung, 12 Min.", us);
        Assert.Contains("(12 Min.)", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/arbeitsbericht_unterricht?{u}")));
        Assert.Contains("V12", Alles(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlstundenmatrix?{u}")).Split('|'));
    }
}
