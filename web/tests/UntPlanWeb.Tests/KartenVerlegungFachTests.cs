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
        Assert.Equal("E", neu["fachAlt"]!.GetValue<string>());   // für die Details (altes Fach durchgestrichen)
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
        // Details: Fachänderung als altes Fach (durchgestrichen) → neues Fach
        var details = File.ReadAllText(Path.Combine(dir.FullName, "src", "UntPlanWeb", "wwwroot", "js", "stundendetails.js"));
        Assert.Contains("const fachTeil = k.fachAlt && !istEntfall() ? `<s class=\"sd-alt\">${e(k.fachAlt)}</s>", details);
    }
}
