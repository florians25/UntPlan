using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// „Aufgestanden“ (Bett mit Pfeil nach oben in der Liste „Abwesende Schüler*innen“) und die genaue Dokumentation von Verspätungen in den Berichten:
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

    /// <summary>
    /// Server mit zwei reinen Verspätungen einer Schülerin bzw. eines Schülers der 7a (ohne Abwesenheit, nur über die Anwesenheit erfasst):
    /// 7 Minuten in der ersten und 5 Minuten in einer späteren Stunde des Tages.
    /// </summary>
    private static async Task<(Testserver S, List<Karte> K, int Sid, HttpClient Leitung)> MitReinenVerspaetungenAsync()
    {
        var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var d = DateOnly.Parse(Tag);
        var karten = s.Dienst<Plandienst>().Karten(stand, d, Elementtyp.Klasse, "7a")
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0 && x.Von == x.Bis).OrderBy(x => x.Von).Take(2).ToList();
        Assert.Equal(2, karten.Count);
        s.Dienst<Uhr>().Fest = d.ToDateTime(new TimeOnly(18, 0));
        var kl = await s.AlsAsync("sl-test", "leitung", "");
        int sid = 0;
        foreach (var (k, min, text) in new[] { (karten[0], 7, "Bus verpasst"), (karten[1], 5, "") })
        {
            var c = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
            if (sid == 0)
                sid = (await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={Tag}&stunde={k.Von}&unterricht={k.Unterricht}"))["schueler"]![0]!["id"]!.GetValue<int>();
            await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
            {
                datum = Tag, stunde = k.Von, unterricht = k.Unterricht, eintraege = new[] { new { schuelerId = sid, art = "verspaetet", minuten = min, bemerkung = text } },
            }));
        }
        return (s, karten, sid, kl);
    }

    private static string H(TimeOnly t) => t.ToString("HH:mm");
    private static List<List<string>> Zeilen(JsonNode b) => b["zeilen"]!.AsArray().Select(z => z!.AsArray().Select(x => x?.ToString() ?? "").ToList()).ToList();
    private static List<string> Arten(JsonNode b) => b["arten"]?.AsArray().Select(x => x?.ToString() ?? "").ToList() ?? new();

    [Fact]
    public async Task Bericht_Abwesenheiten_listet_reine_Verspaetungen_einzeln_mit_Summe_je_Schueler()
    {
        var (s, k, sid, kl) = await MitReinenVerspaetungenAsync();
        await using var _s = s;
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var b0 = Klassenbuchdienst.StundenZeit(stand, k[0].Von).Beginn;
        var b1 = Klassenbuchdienst.StundenZeit(stand, k[1].Von).Beginn;
        var q = $"klasse=7a&von={Tag}&bis={Tag}&schueler={sid}";
        var bericht = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/abwesenheiten?{q}");
        var zeilen = Zeilen(bericht);
        var arten = Arten(bericht);
        // je Verspätung eine eigene Zeile: Datum, Stunde, Uhrzeit von–bis, „verspätet x Min.“, Fach, Lehrkraft, Status, Text
        var z7 = zeilen.Single(z => z[5] == "verspätet 7 Min.");
        Assert.Equal(new[] { "14.10.2026", $"{k[0].Von}.", $"{H(b0)}–{H(b0.AddMinutes(7))}", "verspätet 7 Min.", k[0].Fach, k[0].Lehrer[0].K }, z7.Skip(2).Take(6));
        Assert.Equal(("offen", "Bus verpasst"), (z7[9], z7[10]));
        var z5 = zeilen.Single(z => z[5] == "verspätet 5 Min.");
        Assert.Equal(new[] { $"{k[1].Von}.", $"{H(b1)}–{H(b1.AddMinutes(5))}" }, z5.Skip(3).Take(2));
        Assert.Equal((k[1].Fach, k[1].Lehrer[0].K), (z5[6], z5[7]));
        // Summenzeile je Schüler*in: Anzahl und Minuten
        var iSumme = zeilen.FindIndex(z => z[5] == "2 Verspätungen, 12 Min.");
        Assert.True(iSumme > zeilen.IndexOf(z5), "Summenzeile nach den Verspätungen");
        Assert.Equal("zwischensumme", arten[iSumme]);
        Assert.Equal("Verspätungen", zeilen[iSumme][2]);
        Assert.StartsWith("Summe ", zeilen[iSumme][1]);
        // CSV und PDF
        var csv = await kl.GetStringAsync($"/api/app/berichte/erstellen/abwesenheiten?{q}&format=csv");
        Assert.Contains("verspätet 7 Min.", csv);
        Assert.Contains("verspätet 5 Min.", csv);
        Assert.Contains("2 Verspätungen, 12 Min.", csv);
        var pdf = await kl.GetByteArrayAsync($"/api/app/berichte/erstellen/abwesenheiten?{q}&format=pdf");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        // Haken „Verspätungen“ aus: weder Zeilen noch Summe
        var ohne = Zeilen(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/abwesenheiten?{q}&verspaetungen=0"));
        Assert.DoesNotContain(ohne, z => z.Any(x => x.Contains("Verspätung") || x.StartsWith("verspätet")));
    }

    [Fact]
    public async Task Bericht_Fehlzeiten_pro_Schueler_listet_reine_Verspaetungen_in_allen_Darstellungen()
    {
        var (s, k, sid, kl) = await MitReinenVerspaetungenAsync();
        await using var _s = s;
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var b0 = Klassenbuchdienst.StundenZeit(stand, k[0].Von).Beginn;
        var b1 = Klassenbuchdienst.StundenZeit(stand, k[1].Von).Beginn;
        var q = $"klasse=7a&von={Tag}&bis={Tag}&schueler={sid}";

        // pro Stunde: je Verspätung eine Zeile (Datum, Stunde, Uhrzeit, Art, Fach, Lehrkraft, Text, Status), Summenzeile „Verspätungen“
        var ps = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=pro_stunde");
        var zeilen = Zeilen(ps);
        var z7 = zeilen.Single(z => z[5] == "verspätet 7 Min.");
        Assert.Equal(new[] { $"{k[0].Von}.", "0", "0:07", $"{H(b0)}–{H(b0.AddMinutes(7))}", "verspätet 7 Min.", k[0].Fach, k[0].Lehrer[0].K }, z7.Skip(1).Take(7));
        Assert.Equal(("Bus verpasst", "[offen]"), (z7[9], z7[12]));
        var z5 = zeilen.Single(z => z[5] == "verspätet 5 Min.");
        Assert.Equal(($"{k[1].Von}.", $"{H(b1)}–{H(b1.AddMinutes(5))}"), (z5[1], z5[4]));
        var iSumme = zeilen.FindIndex(z => z[0] == "Verspätungen");
        Assert.Equal("2 Verspätungen, 12 Min.", zeilen[iSumme][5]);
        Assert.Equal("zwischensumme", Arten(ps)[iSumme]);
        var csv = await kl.GetStringAsync($"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=pro_stunde&format=csv");
        Assert.Contains("verspätet 7 Min.", csv);
        Assert.Contains("2 Verspätungen, 12 Min.", csv);
        var pdf = await kl.GetByteArrayAsync($"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=pro_stunde&format=pdf");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        // pro Tag: der Tag mit beiden Verspätungen (je Stunde eine Angabe) und die Summenzeile
        var pt = Zeilen(await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=pro_tag"));
        var tag = pt.Single(z => z[9].Length > 0 && z[0] != "Verspätungen");
        Assert.Contains($"{k[0].Von}. Std. {k[0].Fach}: verspätet {H(b0)}–{H(b0.AddMinutes(7))} (7 Min.)", tag[9]);
        Assert.Contains($"{k[1].Von}. Std. {k[1].Fach}: verspätet {H(b1)}–{H(b1.AddMinutes(5))} (5 Min.)", tag[9]);
        Assert.Equal("2 Verspätungen, 12 Min.", pt.Single(z => z[0] == "Verspätungen")[9]);

        // Gesamtübersicht: Anzahl und Minuten der Verspätungen je Schüler*in
        var g = await Testserver.HolenAsync(kl, $"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=gesamt");
        var sp = g["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        var gz = Zeilen(g).Single();
        Assert.Equal(("2", "12"), (gz[sp.IndexOf("Verspätungen")], gz[sp.IndexOf("Verspätung (Min.)")]));
        Assert.Contains("2 Verspätungen, 12 Min.", await kl.GetStringAsync($"/api/app/berichte/erstellen/fehlzeiten_schueler?{q}&ausgabe=pro_tag&format=csv"));
    }
}
