using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Api;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Anforderungen UW-14 (reine Raumbuchung ohne Nebeneffekte), UW-15 (eigene Abwesenheit, Bereitschaften),
/// „---“ bei zusätzlichem Unterricht ohne Lehrkraft, formatierte Tagestexte (HTML-Bereinigung), Schülerverwaltung, Schrift.
/// </summary>
public class OberflaecheTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    private static readonly DateOnly Freitag = new(2026, 10, 16);

    // ------------------------------------------------------------------ UW-14

    [Fact]
    public async Task Reine_Raumbuchung_erzeugt_keinen_Vertretungstext_und_keinen_zusaetzlichen_Eintrag()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = plan.Karten(stand, Freitag, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1 && x.Raeume.Count == 1);
        var lehrer = k.Lehrer[0].K;
        var alterRaum = k.Raeume[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var vorher = await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum=2026-10-16");
        var anzahlVorher = Karten(vorher, "2026-10-16", k.Von).Count();
        var frei = await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum=2026-10-16&von={k.Von}&bis={k.Von}");
        var neuerRaum = frei.AsArray().Select(x => x!["kurz"]!.GetValue<string>()).First(x => x != alterRaum);
        var vpVorher = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-16");

        // Raum ersetzen (Stundendetails)
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = "2026-10-16", stundeVon = k.Von, stundeBis = k.Von, unterricht = k.Unterricht, raeume = new[] { neuerRaum } }));

        var nachher = await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum=2026-10-16");
        var karten = Karten(nachher, "2026-10-16", k.Von).ToList();
        Assert.Equal(anzahlVorher, karten.Count);              // kein zusätzlicher Eintrag im eigenen Stundenplan
        var karte = karten.Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal("", karte["text"]!.GetValue<string>());     // kein Vertretungstext
        Assert.Equal(neuerRaum, karte["raeume"]![0]!["k"]!.GetValue<string>());
        Assert.Equal(alterRaum, karte["raeume"]![0]!["alt"]!.GetValue<string>()); // Kopf: alt → neu
        Assert.DoesNotContain(karten, x => x["status"]!.GetValue<string>() is "vorgemerkt" or "buchung");

        // keine Vertretungszeile im Vertretungsplan
        var vp = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-16");
        Assert.Equal(vpVorher["tage"]![0]!["zeilen"]!.ToJsonString(), vp["tage"]![0]!["zeilen"]!.ToJsonString());

        // Rückmeldung an UntPlan: Raumbuchung zum Unterricht, ohne Titel/Text
        var rm = (await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray()
            .Single(x => x!["typ"]!.GetValue<string>() == "raumbuchung");
        Assert.Equal(k.Unterricht, rm!["unterrichtNr"]!.GetValue<int>());
        Assert.Null(rm["text"]);
        Assert.Null(rm["titel"]);

        // im Raumplan des neuen Raums steht die Stunde, im alten Raum ist sie verlegt
        var neu = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={neuerRaum}&datum=2026-10-16");
        Assert.Contains(Karten(neu, "2026-10-16", k.Von), x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        var alt = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={alterRaum}&datum=2026-10-16");
        Assert.Equal("abwesend", Karten(alt, "2026-10-16", k.Von).Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht)["status"]!.GetValue<string>());

        // zusätzlichen Raum buchen: beide Räume, eine Karte; zurück auf den alten Raum: keine offene Buchung mehr
        var zweiter = frei.AsArray().Select(x => x!["kurz"]!.GetValue<string>()).First(x => x != alterRaum && x != neuerRaum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = "2026-10-16", stundeVon = k.Von, unterricht = k.Unterricht, raeume = new[] { alterRaum, zweiter } }));
        var zwei = Karten(await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum=2026-10-16"), "2026-10-16", k.Von)
            .Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal(new[] { alterRaum, zweiter }, zwei["raeume"]!.AsArray().Select(x => x!["k"]!.GetValue<string>()));
        Assert.Single(s.Dienst<Rueckmeldungsdienst>().Offene(), m => m.Typ == "raumbuchung");
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = "2026-10-16", stundeVon = k.Von, unterricht = k.Unterricht, raeume = new[] { alterRaum } }));
        Assert.DoesNotContain(s.Dienst<Rueckmeldungsdienst>().Offene(), m => m.Typ == "raumbuchung");
    }

    [Fact]
    public async Task Raumbuchung_ohne_Unterricht_steht_nur_im_Raumplan()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=9&bis=9");
        var raum = frei[0]!["kurz"]!.GetValue<string>();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 9, stundeBis = 9, raum, titel = "AG" }));
        var eigen = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=MUE&datum=2026-10-16");
        Assert.Empty(Karten(eigen, "2026-10-16", 9));
        var raumplan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={raum}&datum=2026-10-16");
        Assert.Single(Karten(raumplan, "2026-10-16", 9));
    }

    // ------------------------------------------------------------------ UW-15

    [Fact]
    public async Task Eigene_Abwesenheit_zeigt_die_vertretende_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("hof", "lehrer", "HOF");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=HOF&datum=2026-10-12");
        // Montag 1.–5. Stunde: vertreten durch HAR, LAN, BEC, ZIM, KLE (laut Vertretungen im Beispielpaket)
        var erwartet = new Dictionary<int, string> { [1] = "HAR", [2] = "LAN", [3] = "BEC", [4] = "ZIM", [5] = "KLE" };
        foreach (var (stunde, vertreter) in erwartet)
        {
            var k = Karten(plan, "2026-10-12", stunde).Single();
            Assert.Equal("abwesend", k["status"]!.GetValue<string>());
            var l = k["lehrer"]!.AsArray().Single()!;
            Assert.Equal(vertreter, l["k"]!.GetValue<string>());
            Assert.Equal("HOF", l["alt"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Bereitschaft_ist_kein_Entfall_sondern_Abwesenheit_der_eingeteilten_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=SNE&datum=2026-10-15");
        var ber = Karten(plan, "2026-10-15", 3).Single(k => k["fach"]!.GetValue<string>() == "BER");
        Assert.Equal("abwesend", ber["status"]!.GetValue<string>());
        var l = ber["lehrer"]!.AsArray().Single()!;
        Assert.Equal("", l["k"]!.GetValue<string>());
        Assert.Equal("SNE", l["alt"]!.GetValue<string>());
        // eindeutige Kennzeichen für Apps: Darstellung „Lehrkraft abwesend“, Bereitschaft
        Assert.True(ber["lehrerAbwesend"]!.GetValue<bool>());
        Assert.True(ber["bereitschaft"]!.GetValue<bool>());
        // Raum-/Tagesansicht: auch außerhalb des Lehrerplans kein Entfall, sondern abwesende Lehrkraft
        var tagKarten = s.Dienst<Plandienst>().Tag(s.Dienst<Paketdienst>().Aktuell!, Elementtyp.Lehrer, new DateOnly(2026, 10, 15), true)
            .Zeilen.Single(z => z.Kurz == "SNE").Karten.Where(k => k.Fach == "BER" && k.Von <= 3 && k.Bis >= 3).ToList();
        Assert.All(tagKarten, k => Assert.True(k.LehrerAbwesend));
        // Vertretungsplan (Lehrerversion): keine Entfallzeile, sondern Abwesenheit
        var vp = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-15");
        var zeile = vp["tage"]![0]!["zeilen"]!.AsArray().Single(z => z!["fach"]!.GetValue<string>() == "BER")!;
        Assert.Equal("abwesend", zeile["status"]!.GetValue<string>());
        Assert.Equal("SNE", zeile["lehrerAlt"]!.GetValue<string>());
        Assert.Equal("Abwesenheit", zeile["art"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bereitschaft_aus_dem_Paket_ist_auch_im_Raumplan_kein_Entfall()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 51;
        var tag = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-16")!;
        // Bereitschaft mit Raum und anderem Kürzel – erkannt am Kennzeichen „bereitschaft“ aus UntPlan
        tag["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 9, ["fach"] = "VB", ["klassen"] = new JsonArray(), ["lehrer"] = new JsonArray("MUE"), ["raeume"] = new JsonArray("R103"),
            ["status"] = "entfall", ["text"] = "", ["bereitschaft"] = true,
        });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        foreach (var url in new[] { "/api/app/plan?typ=raum&element=R103&datum=2026-10-16", "/api/app/plan?typ=lehrer&element=MUE&datum=2026-10-16" })
        {
            var k = Karten(await Testserver.HolenAsync(c, url), "2026-10-16", 9).Single(x => x["fach"]!.GetValue<string>() == "VB");
            Assert.Equal("abwesend", k["status"]!.GetValue<string>());
            Assert.True(k["lehrerAbwesend"]!.GetValue<bool>());
            Assert.True(k["bereitschaft"]!.GetValue<bool>());
            Assert.Equal("MUE", k["lehrer"]![0]!["alt"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Veranstaltung_der_Klasse_laesst_regulaeren_Unterricht_entfallen()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 52;
        paket["termine"]!.AsArray().Add(new JsonObject
        {
            ["art"] = "veranstaltung", ["titel"] = "Bewerbungstraining", ["von"] = "2026-10-16", ["bis"] = "2026-10-16", ["stundeVon"] = 1, ["stundeBis"] = 3,
            ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(), ["raeume"] = new JsonArray(),
        });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var karten = plan.Karten(stand, Freitag, Elementtyp.Klasse, "7a");
        // in den Stunden 1–3 läuft kein regulärer Unterricht der 7a mehr (Entfall, durchgestrichen); Stunden mit Vertretung
        // bestimmt weiterhin UntPlan …
        var betroffen = karten.Where(k => k.Unterricht.HasValue && k.Von <= 3).ToList();
        Assert.NotEmpty(betroffen);
        Assert.DoesNotContain(betroffen, k => k.Status == "regulaer");
        Assert.Contains(betroffen, k => k.Status == "entfall" && k.Text == "Bewerbungstraining");
        // … danach regulär
        Assert.Contains(karten, k => k.Unterricht.HasValue && k.Von > 3 && k.Status == "regulaer");
    }

    // ------------------------------------------------------------------ „---“ bei Zusatzstunden ohne Lehrkraft

    [Fact]
    public async Task Zusatzstunde_ohne_Lehrkraft_zeigt_drei_Striche_nur_bei_Zusatzstunden()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 50;
        var tag = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-16")!;
        tag["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 9, ["fach"] = "M", ["klassen"] = new JsonArray("6a"), ["lehrer"] = new JsonArray(), ["raeume"] = new JsonArray("R103"),
            ["status"] = "zusaetzlich", ["vertretung"] = 9001, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(new JsonObject
        {
            ["id"] = 9001, ["datum"] = "2026-10-16", ["stunde"] = 9, ["art"] = "Zusatzunterricht", ["klassen"] = new JsonArray("6a"), ["lehrer"] = "", ["vertreter"] = "",
            ["fach"] = "M", ["neuesFach"] = "M", ["raum"] = "", ["neuerRaum"] = "R103", ["vertrVon"] = "", ["nach"] = "", ["text"] = "", ["nurLehrer"] = false,
        });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");

        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-16");
        var zusatz = Karten(plan, "2026-10-16", 9).Single();
        Assert.Equal("geaendert", zusatz["status"]!.GetValue<string>());
        Assert.True(zusatz["zusaetzlich"]!.GetValue<bool>());
        Assert.Equal("---", zusatz["lehrer"]![0]!["k"]!.GetValue<string>());
        // Vertretungsplan
        var vp = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-16");
        var zeile = vp["tage"]![0]!["zeilen"]!.AsArray().Single(z => z!["stundeVon"]!.GetValue<int>() == 9 && z["klassen"]!.GetValue<string>() == "6a")!;
        Assert.Equal("---", zeile["lehrer"]!.GetValue<string>());
        // nur Zusatzstunden: Entfall (Vertreter „---“) und Veranstaltungen ohne Lehrkraft bekommen keine „---“
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() != "geaendert" && k["lehrer"]!.AsArray().Any(l => l!["k"]!.GetValue<string>() == "---"));
        var vpDo = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-15");
        Assert.DoesNotContain(vpDo["tage"]![0]!["zeilen"]!.AsArray(), z => z!["lehrer"]!.GetValue<string>() == "---");
        // reguläre Karten ohne Zusatz-Kennzeichen
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() == "regulaer" && k["zusaetzlich"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------------ Klasse abwesend, offene Raumvertretung

    [Fact]
    public async Task Abwesende_Klasse_und_offene_Raumvertretung_auf_den_Karten()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 52;
        paket["absenzen"]!.AsArray().Add(new JsonObject { ["art"] = "klasse", ["element"] = "6a", ["von"] = "2026-10-16", ["bis"] = "2026-10-16", ["stundeVon"] = 4, ["stundeBis"] = 5, ["grund"] = "Exkursion" });
        var fr = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-16")!;
        var std = fr["stunden"]!.AsArray().First(x => x!["unterricht"]?.GetValue<int>() == 122 && x["stunde"]!.GetValue<int>() == 6)!;
        std["raeume"] = new JsonArray("???"); std["status"] = "vertreten"; std["vertretung"] = 9002;
        paket["vertretungen"]!.AsArray().Add(new JsonObject
        {
            ["id"] = 9002, ["datum"] = "2026-10-16", ["stunde"] = 6, ["art"] = "Raumvertretung", ["klassen"] = new JsonArray("5b"), ["lehrer"] = "KLE", ["vertreter"] = "KLE",
            ["fach"] = "PK", ["neuesFach"] = "PK", ["raum"] = "R102", ["neuerRaum"] = "???", ["vertrVon"] = "", ["nach"] = "", ["text"] = "", ["nurLehrer"] = false,
        });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("hof", "lehrer", "HOF");
        // Klasse 6a abwesend: die Stunde entfällt (wie ein Entfall), Klasse markiert, Grund nur fürs Kollegium
        var k = Karten(await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=HOF&datum=2026-10-16"), "2026-10-16", 4).Single();
        Assert.Equal("entfall", k["status"]!.GetValue<string>());
        Assert.Equal("Klasse abwesend", k["art"]!.GetValue<string>());
        Assert.Equal("6a", k["abwesendeKlassen"]![0]!.GetValue<string>());
        Assert.Equal("Klasse abwesend – Exkursion", k["abwesendText"]!.GetValue<string>());
        var schueler = await s.AlsAsync("klasse6a", "schueler", "6a");
        var ks = Karten(await Testserver.HolenAsync(schueler, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-16"), "2026-10-16", 4).Single();
        Assert.Equal("entfall", ks["status"]!.GetValue<string>());
        Assert.Equal("Klasse abwesend", ks["abwesendText"]!.GetValue<string>());
        // offene Raumvertretung: wie offene Lehrervertretung (Status offen, bisheriger Raum durchgestrichen)
        var r = Karten(await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5b&datum=2026-10-16"), "2026-10-16", 6).Single();
        Assert.Equal("offen", r["status"]!.GetValue<string>());
        Assert.Equal("", r["raeume"]![0]!["k"]!.GetValue<string>());
        Assert.Equal("R102", r["raeume"]![0]!["alt"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ Formatierte Tagestexte

    [Theory]
    [InlineData("<b>fett</b> und <i>kursiv</i>", "<b>fett</b> und <i>kursiv</i>")]
    [InlineData("<u>u</u><br/>Zeile<p>Absatz</p>", "<u>u</u><br>Zeile<p>Absatz</p>")]
    [InlineData("<span style=\"color:#ff0000\">rot</span>", "<span style=\"color:#ff0000\">rot</span>")]
    [InlineData("<span style='color: red;'>rot</span>", "<span style=\"color:red\">rot</span>")]
    [InlineData("<span style=\"color:red;background:url(x)\">x</span>", "<span>x</span>")]
    [InlineData("<span onclick=\"alert(1)\">x</span>", "<span>x</span>")]
    [InlineData("<b onmouseover=\"x()\">b</b>", "<b>b</b>")]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<img src=x onerror=alert(1)>", "&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<a href=\"javascript:x\">Link</a>", "&lt;a href=&quot;javascript:x&quot;&gt;Link&lt;/a&gt;")]
    [InlineData("<b>offen", "<b>offen</b>")]
    [InlineData("</i>zu <b>a <i>b</b> c", "zu <b>a <i>b</i></b> c")]
    [InlineData("A &amp; B < C &nbsp;", "A &amp; B &lt; C &#160;")]
    [InlineData("<span style=\"color:expression(alert(1))\">x</span>", "<span>x</span>")]
    public void Html_wird_nach_Whitelist_bereinigt(string eingabe, string erwartet) =>
        Assert.Equal(erwartet, HtmlBereinigung.Bereinigen(eingabe));

    [Fact]
    public async Task Startseite_liefert_bereinigtes_TextHtml_und_Klartext()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 51;
        paket["tagestexte"]!.AsArray().Add(new JsonObject
        {
            ["datum"] = "2026-10-14", ["gueltigTage"] = 1, ["text"] = "Achtung: Hof gesperrt", ["fuer"] = "alle", ["elemente"] = new JsonArray(),
            ["textHtml"] = "<b>Achtung:</b> <span style=\"color:#c00000\">Hof gesperrt</span><script>x()</script>",
        });
        paket["tagestexte"]!.AsArray().Add(new JsonObject
        {
            ["datum"] = "2026-10-14", ["gueltigTage"] = 1, ["text"] = "", ["fuer"] = "alle", ["elemente"] = new JsonArray(), ["textHtml"] = "<p>Nur <i>HTML</i></p>",
        });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        var start = await Testserver.HolenAsync(c, "/api/app/start");
        var n = start["nachrichten"]!.AsArray().Single(x => x!["text"]!.GetValue<string>() == "Achtung: Hof gesperrt")!;
        Assert.Equal("<b>Achtung:</b> <span style=\"color:#c00000\">Hof gesperrt</span>&lt;script&gt;x()&lt;/script&gt;", n["textHtml"]!.GetValue<string>());
        // nur HTML: Klartext wird abgeleitet (Monitor, ältere Clients)
        var nurHtml = start["nachrichten"]!.AsArray().Single(x => x!["textHtml"]?.GetValue<string>() == "<p>Nur <i>HTML</i></p>")!;
        Assert.Equal("Nur HTML", nurHtml["text"]!.GetValue<string>());
        // ohne Formatierung: kein textHtml
        Assert.Contains(start["nachrichten"]!.AsArray(), x => x!["textHtml"] is null);
        var monitor = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-14");
        Assert.Contains("Achtung: Hof gesperrt", monitor["tage"]![0]!["nachrichten"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    // ------------------------------------------------------------------ Schülerverwaltung

    [Fact]
    public async Task Schuelerverwaltung_beachtet_Rechte()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var klasse = stand.Paket.Elemente.Klassen.First(k => k.Klassenleitung.Count > 0);
        var leitung = klasse.Klassenleitung[0];
        var andere = stand.Paket.Elemente.Klassen.First(k => !k.Klassenleitung.Contains(leitung));
        var fremdeLehrkraft = stand.Paket.Elemente.Lehrer.First(l => !stand.Paket.Elemente.Klassen.Any(k => k.Klassenleitung.Contains(l.Kurz))).Kurz;

        // Klassenkonto: kein Zugriff
        var kl = await s.AlsAsync("klasse" + klasse.Kurz.ToLowerInvariant(), "schueler", klasse.Kurz);
        Assert.Equal(HttpStatusCode.Forbidden, (await kl.GetAsync("/api/app/schueler")).StatusCode);

        // Lehrkraft ohne Klassenleitung: nur lesen, keine persönlichen Angaben, nichts ändern
        var lk = await s.AlsAsync(fremdeLehrkraft.ToLowerInvariant(), "lehrer", fremdeLehrkraft);
        var liste = await Testserver.HolenAsync(lk, $"/api/app/schueler?klasse={klasse.Kurz}");
        Assert.False(liste["anlegen"]!.GetValue<bool>());
        var erster = liste["schueler"]![0]!;
        Assert.False(erster["bearbeiten"]!.GetValue<bool>());
        var id = erster["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.Forbidden, (await lk.PutAsJsonAsync($"/api/app/schueler/{id}", new { vorname = "X", name = "Y", klasse = klasse.Kurz })).StatusCode);

        // Klassenleitung: eigene Klasse bearbeiten (inkl. persönlicher Angaben), kein Klassenwechsel, kein Löschen, nicht in fremden Klassen anlegen
        var kli = await s.AlsAsync(leitung.ToLowerInvariant(), "lehrer", leitung);
        var geaendert = await Testserver.JsonAsync(await kli.PutAsJsonAsync($"/api/app/schueler/{id}",
            new { vorname = "Neu", name = erster["name"]!.GetValue<string>(), klasse = klasse.Kurz, geschlecht = "w", geburtsdatum = "2012-05-01", email = "neu@example.org", bemerkung = "Nachteilsausgleich" }));
        Assert.Equal("Neu", geaendert["vorname"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await kli.PutAsJsonAsync($"/api/app/schueler/{id}", new { vorname = "Neu", name = "N", klasse = andere.Kurz })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kli.DeleteAsync($"/api/app/schueler/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kli.PostAsJsonAsync("/api/app/schueler", new { vorname = "A", name = "B", klasse = andere.Kurz })).StatusCode);
        var neu = await Testserver.JsonAsync(await kli.PostAsJsonAsync("/api/app/schueler", new { vorname = "Anna", name = "Neumann", klasse = klasse.Kurz, geschlecht = "w" }));
        var neuId = neu["id"]!.GetValue<int>();
        Assert.True(neuId >= Schuelerverwaltung.ErsteEigeneId);
        Assert.Equal("UntPlanWeb", neu["quelle"]!.GetValue<string>());
        // ungültige Eingaben
        Assert.Equal(HttpStatusCode.BadRequest, (await kli.PostAsJsonAsync("/api/app/schueler", new { vorname = "A", name = "", klasse = klasse.Kurz })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kli.PutAsJsonAsync($"/api/app/schueler/{id}", new { vorname = "A", name = "B", klasse = klasse.Kurz, geburtsdatum = "01.05.2012" })).StatusCode);

        // Änderungen gelten überall (Klassenbuch-Schülerliste), andere Lehrkräfte sehen keine persönlichen Angaben
        var kb = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/schueler?klasse={klasse.Kurz}");
        Assert.Contains(kb.AsArray(), x => x!["id"]!.GetValue<int>() == id && x["vorname"]!.GetValue<string>() == "Neu");
        Assert.Contains(kb.AsArray(), x => x!["id"]!.GetValue<int>() == neuId);
        var fremd = (await Testserver.HolenAsync(lk, $"/api/app/schueler?klasse={klasse.Kurz}"))["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == id)!;
        Assert.Equal("", fremd["email"]!.GetValue<string>());
        Assert.Equal("w", fremd["geschlecht"]!.GetValue<string>());

        // Administration: Klassenwechsel und Löschen; Änderungen überstehen eine neue Übertragung aus UntPlan
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/schueler/{id}", new { vorname = "Neu", name = "N", klasse = andere.Kurz }));
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/schueler/{neuId}"));
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 60;
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var nachher = s.Dienst<Paketdienst>().Aktuell!.Schueler;
        Assert.Equal(andere.Kurz, nachher.Single(x => x.Id == id).Klasse);
        Assert.DoesNotContain(nachher, x => x.Id == neuId);
    }

    // ------------------------------------------------------------------ Schrift (UW-04)

    [Fact]
    public void Schrift_wird_nur_mit_vorhandenen_Dateien_eingebunden()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "untplanweb-schrift-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(ordner, "fonts"));
            Assert.DoesNotContain("@font-face", OberflaecheApi.SchriftCss(ordner));
            foreach (var f in new[] { "AvenirNextPro-Regular.woff2", "AvenirNextPro-Regular.otf", "AvenirNextPro-Demi.woff2", "AvenirNextPro-BoldIt.ttf", "fremd.woff2" })
                File.WriteAllText(Path.Combine(ordner, "fonts", f), "");
            var css = OberflaecheApi.SchriftCss(ordner);
            // jedes CSS-Gewicht 100–900 fest belegt (normal und kursiv), keine Lücken für browserabhängige Ersatzwahl
            Assert.Equal(18, css.Split("@font-face").Length - 1);
            Assert.Contains("font-synthesis: none", css);
            Assert.Contains("url(\"../fonts/AvenirNextPro-Regular.woff2\") format(\"woff2\"), url(\"../fonts/AvenirNextPro-Regular.otf\") format(\"opentype\"); font-weight: 400; font-style: normal", css);
            Assert.Contains("font-weight: 600", css);
            Assert.Contains("font-weight: 700; font-style: italic", css);
            Assert.DoesNotContain("fremd", css);
            Assert.Contains("--u-schrift: \"AvenirNextPro\", \"Avenir Next\", \"Nunito Sans\"", css);
            Assert.Equal(new[] { (100, 400), (200, 400), (300, 400), (400, 400), (500, 400), (600, 600), (700, 600), (800, 600), (900, 600) }, SchriftApi.Zuordnung(new[] { 400, 600 }));
            Assert.Contains((600, 500), SchriftApi.Zuordnung(new[] { 400, 500, 600, 700 }));
            Assert.Contains((700, 600), SchriftApi.Zuordnung(new[] { 400, 500, 600, 700 }));
        }
        finally { Directory.Delete(ordner, true); }
    }
}
