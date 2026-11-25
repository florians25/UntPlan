using System.Net;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stundenkarten: reine Verlegung ohne Lehrer-Label und ohne Zusatzstunde, Entfall wegen einer Veranstaltung ohne Vertretungstext,
/// Fachwechsel als zwei parallele Blöcke (Entfall in der Farbe des alten Fachs, neuer Block in der Farbe des neuen Fachs).
/// </summary>
public class KartenVerlegungFachTests
{
    private static JsonObject Vertretung(int id, string datum, int stunde, string art, string lehrer, string vertreter, string fach, string neuesFach,
        string raum, string neuerRaum, string vertrVon = "", string nach = "", string text = "", int? veranstaltung = null, params string[] klassen) => new()
    {
        ["id"] = id, ["datum"] = datum, ["stunde"] = stunde, ["art"] = art, ["klassen"] = new JsonArray(klassen.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray()),
        ["lehrer"] = lehrer, ["vertreter"] = vertreter, ["fach"] = fach, ["neuesFach"] = neuesFach, ["raum"] = raum, ["neuerRaum"] = neuerRaum,
        ["vertrVon"] = vertrVon, ["nach"] = nach, ["text"] = text, ["nurLehrer"] = false, ["veranstaltung"] = veranstaltung,
    };

    private static JsonNode Tag(JsonObject paket, string datum) => paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == datum)!;

    private static JsonObject Stunde(JsonObject paket, string datum, string klasse, int stunde) =>
        Tag(paket, datum)["stunden"]!.AsArray().First(s => s!["stunde"]!.GetValue<int>() == stunde && s["klassen"]!.AsArray().Any(k => k!.GetValue<string>() == klasse))!.AsObject();

    private static async Task<JsonNode> PlanAsync(JsonObject paket, string typ, string element, string datum)
    {
        await using var s = await Testserver.StartenAsync(false);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        return await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={element}&datum={datum}");
    }

    private static List<JsonNode> Karten(JsonNode plan, string datum, int von) =>
        plan["karten"]!.AsArray().Where(k => k!["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() == von).Select(k => k!).ToList();

    private static bool Wahr(JsonNode k, string feld) => k[feld]?.GetValue<bool>() ?? false;

    [Fact]
    public async Task Reine_Verlegung_zeigt_die_Lehrkraft_ohne_Label_und_ist_keine_Zusatzstunde()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 81;
        // 7a: Stunde Mo 12.10. 4. Stunde wird auf Mi 14.10. 7. Stunde verlegt – gleiche Lehrkraft; die Vertretung am Ziel nennt die
        // Lehrkraft, deren Stunde dort vorher lag (MUE), und einen anderen Raum
        var s = Stunde(paket, "2026-10-12", "7a", 4);
        var lehrer = s["lehrer"]![0]!.GetValue<string>();
        var fach = s["fach"]!.GetValue<string>();
        var raum = s["raeume"]![0]!.GetValue<string>();
        s["status"] = "entfall"; s["vertretung"] = 9301;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9301, "2026-10-12", 4, "Entfall", lehrer, "---", fach, fach, raum, "", "", "Mi-14.10. / 7", "", null, "7a"));
        Tag(paket, "2026-10-14")["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 7, ["unterricht"] = s["unterricht"]!.GetValue<int>(), ["fach"] = fach, ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(lehrer),
            ["raeume"] = new JsonArray("R999"), ["status"] = "zusaetzlich", ["vertretung"] = 9302, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(Vertretung(9302, "2026-10-14", 7, "Verlegung", "MUE", lehrer, fach, fach, raum, "R999", "Mo-12.10. / 4", "", "", null, "7a"));

        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var ziel = Karten(plan, "2026-10-14", 7).Single();
        Assert.Equal("nach", ziel["verlegung"]!.GetValue<string>());
        // keine zusätzliche Unterrichtsstunde (kein Badge, keine „+“-Lehrkräfte in den Apps)
        Assert.False(Wahr(ziel, "zusaetzlich"));
        // Lehrkraft unverändert: kein alter Wert, nicht neu → kein Label
        var l = ziel["lehrer"]!.AsArray().Single()!;
        Assert.Equal(lehrer, l["k"]!.GetValue<string>());
        Assert.Null(l["alt"]?.GetValue<string>());
        Assert.False(Wahr(l, "neu"));
        // nur der tatsächlich geänderte Raum behält sein Label
        var r = ziel["raeume"]!.AsArray().Single()!;
        Assert.Equal("R999", r["k"]!.GetValue<string>());
        Assert.Equal(raum, r["alt"]!.GetValue<string>());
        Assert.True(Wahr(r, "neu"));
        // Ursprung: Entfall ohne Label
        var von = Karten(plan, "2026-10-12", 4).Single(k => k["verlegung"]?.GetValue<string>() == "von");
        Assert.All(von["lehrer"]!.AsArray(), w => Assert.False(Wahr(w!, "neu")));
    }

    /// <summary>
    /// Zielstunde einer Verlegung bzw. eines Tauschs in einem anderen Raum als am Ursprung (die Klasse hat am Ziel keinen Unterricht): Der Vertretungsplan nennt am Ziel als bisherigen
    /// Raum den Raum, den die Stunde an ihrem Ursprung hatte – die Karte zeigt den neuen Raum als grünes Label („alt → neu“). Bleibt der Raum
    /// gleich („Räume mitnehmen“), gibt es kein Label – auch wenn der Raum am Ursprung schon geändert war (Bezug ist dieser geänderte Raum).
    /// Ältere Pakete ohne Raum in der Vertretung: Vergleich mit dem Raum laut Stundenplan am Ursprung.
    /// </summary>
    [Theory]
    [InlineData("Verlegung", "R999", "", true)]
    [InlineData("Tausch", "R999", "", true)]
    [InlineData("Verlegung", "", "", false)]
    [InlineData("Verlegung", "R777", "R777", false)]
    [InlineData("Verlegung", "R999", "R777", true)]
    [InlineData("Verlegung", "R999", "-", true)]
    public async Task Verlegung_in_anderen_Raum_zeigt_Raumlabel_gegenueber_dem_Raum_am_Ursprung(string art, string zielraum, string ursprungsraum, bool geaendert)
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 84;
        var s = Stunde(paket, "2026-10-12", "7a", 4);
        var lehrer = s["lehrer"]![0]!.GetValue<string>();
        var fach = s["fach"]!.GetValue<string>();
        var raum = s["raeume"]![0]!.GetValue<string>();
        var ziel = zielraum.Length > 0 ? zielraum : raum;
        // Raum der Stunde am Ursprung (am Ursprung geändert bzw. wie im Stundenplan); „-“ = älteres Paket ohne Raum in der Vertretung
        var bezug = ursprungsraum == "-" ? "" : ursprungsraum.Length > 0 ? ursprungsraum : raum;
        var erwartetAlt = ursprungsraum is "-" or "" ? raum : ursprungsraum;
        s["status"] = "entfall"; s["vertretung"] = 9311;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9311, "2026-10-12", 4, "Entfall", lehrer, "---", fach, fach, raum, "", "", "Mi-14.10. / 7", "", null, "7a"));
        Tag(paket, "2026-10-14")["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 7, ["unterricht"] = s["unterricht"]!.GetValue<int>(), ["fach"] = fach, ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(lehrer),
            ["raeume"] = new JsonArray(ziel), ["status"] = "zusaetzlich", ["vertretung"] = 9312, ["text"] = "",
        });
        // bisheriger Raum am Ziel = Raum am Ursprung (wie der Vertretungsplan des Desktops)
        paket["vertretungen"]!.AsArray().Add(Vertretung(9312, "2026-10-14", 7, art, lehrer, lehrer, fach, fach, bezug, ziel, "Mo-12.10. / 4", "", "", null, "7a"));

        foreach (var (typ, element) in new[] { ("klasse", "7a"), ("lehrer", lehrer), ("raum", ziel) })
        {
            var plan = await PlanAsync(paket, typ, element, "2026-10-12");
            var karte = Karten(plan, "2026-10-14", 7).Single(k => k["verlegung"]?.GetValue<string>() == "nach");
            var r = karte["raeume"]!.AsArray().Single()!;
            Assert.Equal(ziel, r["k"]!.GetValue<string>());
            if (geaendert)
            {
                Assert.Equal(erwartetAlt, r["alt"]?.GetValue<string>());
                Assert.True(Wahr(r, "neu"));
                Assert.Contains("aenderung", karte["marker"]!.AsArray().Select(x => x!.GetValue<string>()));
            }
            else
            {
                Assert.Null(r["alt"]?.GetValue<string>());
                Assert.False(Wahr(r, "neu"));
            }
        }
    }

    /// <summary>
    /// Wie im Vorbild (Klassenplan, Stunde in TH3 in die Stunde verlegt, in der die Klasse regulär GF/FPHT in B043 hat): die Stunde, die an
    /// der Zielposition laut Stundenplan stattfände, gilt als Ursprungsstunde – deren Raum ist der Bezug, nicht der Raum am Ursprung. Hier:
    /// 7a SP/SNE in TH2 von Mo 4. nach Mi 4. Stunde, wo 7a regulär E/FIS in R105 hat: Label „TH2“ (R105 → TH2) im Klassen-, Lehrer- und
    /// Raumplan – gleich, ob das Paket als bisherigen Raum den Raum am Ursprung (ältere Pakete) oder den der Zielstunde nennt. Wird die Stunde
    /// in R105 gelegt, ist das keine Raumänderung.
    /// </summary>
    [Theory]
    [InlineData("TH2", "TH2", true)]
    [InlineData("TH2", "R105", true)]
    [InlineData("R105", "TH2", false)]
    [InlineData("R105", "R105", false)]
    public async Task Verlegung_vergleicht_mit_dem_Raum_der_regulaeren_Stunde_am_Ziel(string zielraum, string bisherigerRaum, bool geaendert)
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 85;
        var s = Stunde(paket, "2026-10-12", "7a", 4);
        var lehrer = s["lehrer"]![0]!.GetValue<string>();
        var fach = s["fach"]!.GetValue<string>();
        Assert.Equal("TH2", s["raeume"]![0]!.GetValue<string>());
        var e = Stunde(paket, "2026-10-14", "7a", 4);
        Assert.Equal("R105", e["raeume"]![0]!.GetValue<string>());
        s["status"] = "entfall"; s["vertretung"] = 9321;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9321, "2026-10-12", 4, "Entfall", lehrer, "---", fach, fach, "TH2", "", "", "Mi-14.10. / 4", "", null, "7a"));
        // die reguläre Stunde am Ziel entfällt (verdrängt)
        e["status"] = "entfall"; e["vertretung"] = 9323;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9323, "2026-10-14", 4, "Entfall", e["lehrer"]![0]!.GetValue<string>(), "---", e["fach"]!.GetValue<string>(),
            e["fach"]!.GetValue<string>(), "R105", "", "", "", "", null, "7a"));
        Tag(paket, "2026-10-14")["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 4, ["unterricht"] = s["unterricht"]!.GetValue<int>(), ["fach"] = fach, ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(lehrer),
            ["raeume"] = new JsonArray(zielraum), ["status"] = "zusaetzlich", ["vertretung"] = 9322, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(Vertretung(9322, "2026-10-14", 4, "Verlegung", lehrer, lehrer, fach, fach, bisherigerRaum, zielraum, "Mo-12.10. / 4", "", "", null, "7a"));

        foreach (var (typ, element) in new[] { ("klasse", "7a"), ("lehrer", lehrer), ("raum", zielraum) })
        {
            var plan = await PlanAsync(paket, typ, element, "2026-10-12");
            var karte = Karten(plan, "2026-10-14", 4).Single(k => k["verlegung"]?.GetValue<string>() == "nach");
            var r = karte["raeume"]!.AsArray().Single()!;
            Assert.Equal(zielraum, r["k"]!.GetValue<string>());
            if (geaendert)
            {
                Assert.Equal("R105", r["alt"]?.GetValue<string>());
                Assert.True(Wahr(r, "neu"));
            }
            else
            {
                Assert.Null(r["alt"]?.GetValue<string>());
                Assert.False(Wahr(r, "neu"));
            }
        }
    }

    private static void RaumPruefen(JsonNode r, bool label)
    {
        Assert.Equal(label, Wahr(r, "neu"));
        if (!label) Assert.Null(r["alt"]?.GetValue<string>());
    }

    /// <summary>
    /// Verlegte Stunde mit mehreren Räumen (7a SP/SNE regulär in TH2 und TH3, verlegt in die Stunde, in der 7a regulär in R105 ist): die
    /// eigenen Räume der Stunde sind keine Raumänderung und stehen ohne Label da (nicht „R105 → TH2“ für den ersten Raum) – nur ein Raum,
    /// den die Stunde sonst nicht hat, bekommt eines. Ein einzelner Raum wird weiter mit dem Raum der Zielstunde verglichen (siehe oben).
    /// </summary>
    [Theory]
    [InlineData("TH2,TH3", "")]
    [InlineData("TH2,C101", "C101")]
    [InlineData("R105,TH3", "")]
    public async Task Verlegung_mit_mehreren_Raeumen_zeigt_nur_fremde_Raeume_als_Label(string zielraeume, string label)
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 86;
        var s = Stunde(paket, "2026-10-12", "7a", 4);
        var lehrer = s["lehrer"]![0]!.GetValue<string>();
        var fach = s["fach"]!.GetValue<string>();
        var u = s["unterricht"]!.GetValue<int>();
        // Stundenplan: Mo 4. Stunde in TH2 und TH3
        var eintrag = paket["perioden"]![0]!["stundenplan"]!.AsArray()
            .First(x => x!["unterricht"]!.GetValue<int>() == u && x["tag"]!.GetValue<int>() == 0 && x["stunde"]!.GetValue<int>() == 4)!;
        eintrag["raeume"] = new JsonArray("TH2", "TH3");
        s["raeume"] = new JsonArray("TH2", "TH3");
        var e = Stunde(paket, "2026-10-14", "7a", 4);
        Assert.Equal("R105", e["raeume"]![0]!.GetValue<string>());
        s["status"] = "entfall"; s["vertretung"] = 9331;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9331, "2026-10-12", 4, "Entfall", lehrer, "---", fach, fach, "TH2,TH3", "", "", "Mi-14.10. / 4", "", null, "7a"));
        e["status"] = "entfall"; e["vertretung"] = 9333;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9333, "2026-10-14", 4, "Entfall", e["lehrer"]![0]!.GetValue<string>(), "---", e["fach"]!.GetValue<string>(),
            e["fach"]!.GetValue<string>(), "R105", "", "", "", "", null, "7a"));
        var ziel = zielraeume.Split(',');
        Tag(paket, "2026-10-14")["stunden"]!.AsArray().Add(new JsonObject
        {
            ["stunde"] = 4, ["unterricht"] = u, ["fach"] = fach, ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(lehrer),
            ["raeume"] = new JsonArray(ziel.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()), ["status"] = "zusaetzlich", ["vertretung"] = 9332, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(Vertretung(9332, "2026-10-14", 4, "Verlegung", lehrer, lehrer, fach, fach, "R105", zielraeume, "Mo-12.10. / 4", "", "", null, "7a"));

        foreach (var (typ, element) in new[] { ("klasse", "7a"), ("lehrer", lehrer) })
        {
            var plan = await PlanAsync(paket, typ, element, "2026-10-12");
            var karte = Karten(plan, "2026-10-14", 4).Single(k => k["verlegung"]?.GetValue<string>() == "nach");
            var raeume = karte["raeume"]!.AsArray().Select(r => r!).ToList();
            Assert.Equal(ziel.Order(), raeume.Select(r => r["k"]!.GetValue<string>()).Order());
            foreach (var r in raeume) RaumPruefen(r, r["k"]!.GetValue<string>() == label);
        }
    }

    /// <summary>
    /// Raumvertretung, die einen Raum hinzufügt (Stunde jetzt in TH2 und C101): TH2 bleibt ohne Label, nur C101 ist hinzugekommen („+C101“) –
    /// auch wenn die Vertretung als neuen Raum nur C101 nennt (kein „TH2 → C101“, TH2 ist ja weiter dabei).
    /// </summary>
    [Theory]
    [InlineData("TH2,C101")]
    [InlineData("C101")]
    public async Task Hinzugefuegter_Raum_hat_als_einziger_ein_Label(string neuerRaum)
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 87;
        var s = Stunde(paket, "2026-10-12", "7a", 4);
        var lehrer = s["lehrer"]![0]!.GetValue<string>();
        var fach = s["fach"]!.GetValue<string>();
        s["raeume"] = new JsonArray("TH2", "C101");
        s["status"] = "vertreten"; s["vertretung"] = 9341;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9341, "2026-10-12", 4, "Raum-Vtr.", lehrer, lehrer, fach, fach, "TH2", neuerRaum, "", "", "", null, "7a"));

        foreach (var (typ, element) in new[] { ("klasse", "7a"), ("lehrer", lehrer) })
        {
            var plan = await PlanAsync(paket, typ, element, "2026-10-12");
            var karte = Karten(plan, "2026-10-12", 4).Single();
            var raeume = karte["raeume"]!.AsArray().Select(r => r!).ToList();
            Assert.Equal(new[] { "C101", "TH2" }, raeume.Select(r => r["k"]!.GetValue<string>()).Order());
            var plus = raeume.Single(r => r["k"]!.GetValue<string>() == "C101");
            Assert.True(Wahr(plus, "neu"));
            Assert.True(Wahr(plus, "plus"));
            Assert.Null(plus["alt"]?.GetValue<string>());
            RaumPruefen(raeume.Single(r => r["k"]!.GetValue<string>() == "TH2"), false);
        }
    }

    [Fact]
    public async Task Entfall_wegen_Veranstaltung_ohne_Vertretungstext()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 82;
        paket["termine"]!.AsArray().Add(new JsonObject
        {
            ["art"] = "veranstaltung", ["titel"] = "Wandertag", ["von"] = "2026-10-12", ["bis"] = "2026-10-12", ["stundeVon"] = 1, ["stundeBis"] = 3,
            ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray(), ["raeume"] = new JsonArray(), ["id"] = 77,
        });
        // 1. Stunde: Bezug über die Stunde (veranstaltung), 2. Stunde: über die Vertretung, 3. Stunde: älteres Paket (nur der Text)
        for (var st = 1; st <= 3; st++)
        {
            var s = Stunde(paket, "2026-10-12", "7a", st);
            s["status"] = "entfall"; s["vertretung"] = 9400 + st; s["text"] = "Wandertag mit Picknick";
            if (st == 1) s["veranstaltung"] = 77;
            paket["vertretungen"]!.AsArray().Add(Vertretung(9400 + st, "2026-10-12", st, "Freisetzung", s["lehrer"]![0]!.GetValue<string>(), "---",
                s["fach"]!.GetValue<string>(), s["fach"]!.GetValue<string>(), "", "", "", "", st == 3 ? "Wandertag" : "Wandertag mit Picknick", st == 2 ? 77 : null, "7a"));
            if (st == 3) s["text"] = "Wandertag";
        }
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        for (var st = 1; st <= 3; st++)
        {
            var k = plan["karten"]!.AsArray().Select(x => x!).Single(x => x["datum"]!.GetValue<string>() == "2026-10-12" && x["von"]!.GetValue<int>() <= st
                && x["bis"]!.GetValue<int>() >= st && x["status"]!.GetValue<string>() == "entfall");
            Assert.Equal("", k["text"]!.GetValue<string>());
            Assert.True(Wahr(k, "wegenVeranstaltung"));
        }
        // ein gewöhnlicher Entfall behält seinen Text
        var paket2 = Testserver.Beispielpaket();
        paket2["revision"] = 83;
        var e = Stunde(paket2, "2026-10-12", "7a", 1);
        e["status"] = "entfall"; e["vertretung"] = 9410; e["text"] = "Lehrkraft krank";
        paket2["vertretungen"]!.AsArray().Add(Vertretung(9410, "2026-10-12", 1, "Entfall", e["lehrer"]![0]!.GetValue<string>(), "---",
            e["fach"]!.GetValue<string>(), e["fach"]!.GetValue<string>(), "", "", "", "", "Lehrkraft krank", null, "7a"));
        var plan2 = await PlanAsync(paket2, "klasse", "7a", "2026-10-12");
        var normal = Karten(plan2, "2026-10-12", 1).Single(x => x["status"]!.GetValue<string>() == "entfall");
        Assert.Equal("Lehrkraft krank", normal["text"]!.GetValue<string>());
        Assert.False(Wahr(normal, "wegenVeranstaltung"));
    }

    [Fact]
    public async Task Fachwechsel_neuer_Block_mit_neuer_Fachfarbe_alter_Block_entfaellt_in_alter_Farbe()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 84;
        // 6a, Mittwoch 1. Stunde: Vertretung KRU für WAG – statt Englisch (grün) jetzt Mathematik (blau)
        var stunde = Stunde(paket, "2026-10-14", "6a", 1);
        stunde["fach"] = "M";
        paket["vertretungen"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == 450)!["neuesFach"] = "M";
        var plan = await PlanAsync(paket, "klasse", "6a", "2026-10-14");
        var karten = Karten(plan, "2026-10-14", 1);
        Assert.Equal(2, karten.Count);
        var neu = karten.Single(k => k["status"]!.GetValue<string>() == "geaendert");
        Assert.Equal("M", neu["fach"]!.GetValue<string>());
        Assert.Equal("E", neu["fachAlt"]!.GetValue<string>());   // bisheriges Fach (Tooltip; die Details zeigen keine Fachänderung)
        Assert.Equal("#7BAAF7", neu["farbe"]!.GetValue<string>());
        var alt = karten.Single(k => k["status"]!.GetValue<string>() == "entfall");
        Assert.Equal("E", alt["fach"]!.GetValue<string>());
        Assert.Equal("#81C995", alt["farbe"]!.GetValue<string>());   // nur der neue Block bekommt die neue Fachfarbe
        Assert.Null(alt["fachAlt"]?.GetValue<string>());
        Assert.Equal("", alt["text"]!.GetValue<string>());
        Assert.False(Wahr(alt, "zusaetzlich"));
    }
    [Fact]
    public void Karten_kuerzen_Listen_nur_mit_Auslassungszeichen_ohne_Anzahl()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js"));
        // keine Anzahl „+N“ verdeckter Werte auf den Karten – nur „…“; die vollständige Liste steht in den Stundendetails
        Assert.DoesNotContain("mehr.textContent = `+${verdeckt}`", js);
        Assert.DoesNotContain("+${l.length - max}", js);
        Assert.DoesNotContain("<span class=\"mehr\" hidden></span>", js);
        Assert.Contains("data-hover-art=\"grau\">…</span>", js);
        // Details: keine Fachänderung (jeder Block nennt nur sein eigenes Fach)
        var details = File.ReadAllText(Path.Combine(dir.FullName, "src", "UntPlanWeb", "wwwroot", "js", "stundendetails.js"));
        Assert.Contains("const fachTeil = k.status === 'veranstaltung' ? e(k.fach || '') : e(k.fach || k.text || k.art || 'Stunde');", details);
        Assert.DoesNotContain("e(k.fachAlt)", details);
    }
}
