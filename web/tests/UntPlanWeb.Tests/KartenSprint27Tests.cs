using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stundenkarten (Sprint 27): Verlegung mit Ursprung und Ziel (verlegungVon/-Nach), mehrere Symbole oben rechts (marker),
/// hinzugefügte Werte („+B070“), Kopplungen behalten bei Vertretungen alle Blöcke, Vertretungstext bei Zusatzstunden (nur
/// Standardtexte entfallen), Hover gekürzter Zeilen, Aufsichten bis an die Linie, Abmelden bei offenen Stundendetails.
/// </summary>
public class KartenSprint27Tests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    private static JsonObject Vertretung(int id, string datum, int stunde, string art, string lehrer, string vertreter, string fach, string raum, string neuerRaum,
        string vertrVon = "", string nach = "", string text = "", params string[] klassen) => new()
    {
        ["id"] = id, ["datum"] = datum, ["stunde"] = stunde, ["art"] = art, ["klassen"] = new JsonArray(klassen.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray()),
        ["lehrer"] = lehrer, ["vertreter"] = vertreter, ["fach"] = fach, ["neuesFach"] = fach, ["raum"] = raum, ["neuerRaum"] = neuerRaum,
        ["vertrVon"] = vertrVon, ["nach"] = nach, ["text"] = text, ["nurLehrer"] = false,
    };

    private static JsonNode Tag(JsonObject paket, string datum) => paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == datum)!;

    private static IEnumerable<JsonNode> Stunden(JsonObject paket, string datum, string klasse, int stunde) =>
        Tag(paket, datum)["stunden"]!.AsArray().Where(s => s!["stunde"]!.GetValue<int>() == stunde && s["klassen"]!.AsArray().Any(k => k!.GetValue<string>() == klasse))!;

    private static async Task<JsonNode> PlanAsync(JsonObject paket, string typ, string element, string datum)
    {
        await using var s = await Testserver.StartenAsync(false);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        return await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={element}&datum={datum}");
    }

    private static List<JsonNode> Karten(JsonNode plan, string datum, int von) =>
        plan["karten"]!.AsArray().Where(k => k!["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() == von).Select(k => k!).ToList();

    private static List<string> Marker(JsonNode k) => k["marker"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();

    // ------------------------------------------------------------------ Verlegung: Ursprung und Ziel

    [Fact]
    public async Task Verlegung_liefert_Ursprung_und_Ziel_mit_Datum_und_Zeiten_ueber_den_ganzen_Block()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 70;
        // 7a: Sport Mo 12.10. 4.–5. Stunde wird auf Mi 14.10. 7.–8. Stunde verlegt (Desktop: Entfall mit „Nach“, Ziel mit „VertrVon“)
        foreach (var st in new[] { 4, 5 })
        {
            var s = Stunden(paket, "2026-10-12", "7a", st).Single();
            s["status"] = "entfall"; s["vertretung"] = 9200 + st;
            paket["vertretungen"]!.AsArray().Add(Vertretung(9200 + st, "2026-10-12", st, "Entfall", "SNE", "---", "SP", "TH2", "", "", $"Mi-14.10. / {st + 3}", "", "7a"));
            Tag(paket, "2026-10-14")["stunden"]!.AsArray().Add(new JsonObject
            {
                ["stunde"] = st + 3, ["unterricht"] = s["unterricht"]!.GetValue<int>(), ["fach"] = "SP", ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray("SNE"),
                ["raeume"] = new JsonArray("TH2"), ["status"] = "zusaetzlich", ["vertretung"] = 9210 + st, ["text"] = "",
            });
            paket["vertretungen"]!.AsArray().Add(Vertretung(9210 + st, "2026-10-14", st + 3, "Verlegung", "SNE", "SNE", "SP", "TH2", "TH2", $"Mo-12.10. / {st}", "", "", "7a"));
        }
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var zeit = plan["zeitraster"]!.AsArray().ToDictionary(z => z!["nr"]!.GetValue<int>(), z => z!);
        string B(int nr) => zeit[nr]["beginn"]!.GetValue<string>();
        string E(int nr) => zeit[nr]["ende"]!.GetValue<string>();

        var von = Karten(plan, "2026-10-12", 4).Single();
        Assert.Equal(5, von["bis"]!.GetValue<int>());
        Assert.Equal("von", von["verlegung"]!.GetValue<string>());
        Assert.Equal(new[] { "verlegungVon" }, Marker(von));     // rotes Entfall-Dreieck mit weißem Pfeil nach rechts
        Assert.Equal("2026-10-12", von["verlegungVon"]!["datum"]!.GetValue<string>());
        Assert.Equal(B(4), von["verlegungVon"]!["beginn"]!.GetValue<string>());
        Assert.Equal(E(5), von["verlegungVon"]!["ende"]!.GetValue<string>());
        Assert.Equal("2026-10-14", von["verlegungNach"]!["datum"]!.GetValue<string>());
        Assert.Equal(7, von["verlegungNach"]!["stunde"]!.GetValue<int>());
        Assert.Equal(8, von["verlegungNach"]!["bisStunde"]!.GetValue<int>());
        Assert.Equal(B(7), von["verlegungNach"]!["beginn"]!.GetValue<string>());
        Assert.Equal(E(8), von["verlegungNach"]!["ende"]!.GetValue<string>());

        var nach = Karten(plan, "2026-10-14", 7).Single();
        Assert.Equal("nach", nach["verlegung"]!.GetValue<string>());
        Assert.Equal(new[] { "verlegungNach" }, Marker(nach));
        Assert.Equal("2026-10-12", nach["verlegungVon"]!["datum"]!.GetValue<string>());
        Assert.Equal(B(4), nach["verlegungVon"]!["beginn"]!.GetValue<string>());
        Assert.Equal(E(5), nach["verlegungVon"]!["ende"]!.GetValue<string>());
        Assert.Equal(E(8), nach["verlegungNach"]!["ende"]!.GetValue<string>());

        // ohne Verlegung: beide Felder null
        var regulaer = plan["karten"]!.AsArray().First(k => k!["status"]!.GetValue<string>() == "regulaer")!;
        Assert.Null(regulaer["verlegungVon"]);
        Assert.Null(regulaer["verlegungNach"]);
        Assert.Empty(Marker(regulaer));
    }

    [Fact]
    public void Verlegungsangabe_lesen_Jahr_naechstliegend()
    {
        var zeiten = new Dictionary<int, WebStundenzeit> { [4] = new(4, "", "10:40", "11:25") };
        var z = Plandienst.VerlegungsZeitLesen("Fr-09.10. / 4", new DateOnly(2026, 10, 13), zeiten)!;
        Assert.Equal(new Verlegungszeit("2026-10-09", "10:40", "11:25", 4, 4), z);
        Assert.Equal("2027-01-04", Plandienst.VerlegungsZeitLesen("Mo-04.01. / 2", new DateOnly(2026, 12, 28), zeiten)!.Datum);
        Assert.Equal("2026-12-28", Plandienst.VerlegungsZeitLesen("Mo-28.12. / 2", new DateOnly(2027, 1, 4), zeiten)!.Datum);
        Assert.Equal("", Plandienst.VerlegungsZeitLesen("Mo-28.12. / 2", new DateOnly(2027, 1, 4), zeiten)!.Beginn);   // Stunde nicht im Raster
        Assert.Null(Plandienst.VerlegungsZeitLesen("", new DateOnly(2026, 10, 13), zeiten));
        Assert.Null(Plandienst.VerlegungsZeitLesen("irgendwann", new DateOnly(2026, 10, 13), zeiten));
    }

    // ------------------------------------------------------------------ Marker, Plus

    private static Karte K(string status, string art = "", List<Wert>? raeume = null, bool zusaetzlich = false, bool lehrerAbwesend = false, string? fachAlt = null) =>
        new("2026-10-16", 1, 1, "08:00", "08:45", status, "M", fachAlt, "Mathematik", "", new() { new("7a") }, new() { new("MUE") }, raeume ?? new() { new("R105") },
            art, "", null, null, "") { Zusaetzlich = zusaetzlich, LehrerAbwesend = lehrerAbwesend };

    [Fact]
    public void Mehrere_Marker_untereinander()
    {
        var raumGeaendert = new List<Wert> { new("R109", "R105", true) };
        Assert.Equal(new[] { "pruefung", "aenderung" }, K("klausur", "Klausur", raumGeaendert).Marker);    // gelber Kreis, darunter grünes Viereck
        Assert.Equal(new[] { "pruefung" }, K("klausur", "Klausur").Marker);
        Assert.Equal(new[] { "plus", "aenderung" }, K("geaendert", "Zusatzunterricht", raumGeaendert, zusaetzlich: true).Marker);
        Assert.Equal(new[] { "plus", "aenderung" }, K("geaendert", "Sondereinsatz", new() { new("R105"), new("B070", null, true) }).Marker);
        Assert.Equal(new[] { "plus" }, K("geaendert", "Zusatzunterricht", zusaetzlich: true).Marker);
        Assert.Equal(new[] { "aenderung" }, K("geaendert", "Vertretung", raumGeaendert).Marker);
        Assert.Equal(new[] { "aenderung" }, K("offen", "Vertretung").Marker);
        Assert.Equal(new[] { "entfall" }, K("entfall", "Entfall").Marker);
        Assert.Equal(new[] { "entfall" }, K("abwesend", "Vertretung").Marker);                   // Raumplan: Stunde findet woanders statt
        Assert.Empty(K("abwesend", "Vertretung", lehrerAbwesend: true).Marker);                  // eigene Stunde wird vertreten
        Assert.Equal(new[] { "verlegungVon" }, K("entfall", "Verlegung").Marker);
        Assert.Equal(new[] { "verlegungNach", "aenderung" }, K("geaendert", "Verlegung", raumGeaendert).Marker);
        Assert.Equal(new[] { "verlegungNach" }, K("geaendert", "Verlegung").Marker);
        Assert.Equal(new[] { "aenderung" }, K("geaendert", "Vertretung", fachAlt: "D").Marker);
        Assert.Empty(K("regulaer").Marker);
    }

    [Fact]
    public void Hinzugefuegter_Wert_ist_Plus()
    {
        Assert.True(new Wert("B070", null, true).Plus);
        Assert.True(new Wert("B070", "", true).Plus);
        Assert.False(new Wert("B070", "R105", true).Plus);      // ersetzt
        Assert.False(new Wert("", "R105", true).Plus);          // entfernt
        Assert.False(new Wert("B070").Plus);
    }

    [Fact]
    public async Task Hinzugefuegter_Raum_als_Plus_in_der_App_API()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 71;
        var s = Stunden(paket, "2026-10-12", "7a", 6).Single();
        s["status"] = "vertreten"; s["raeume"] = new JsonArray("R105", "R109"); s["vertretung"] = 9220;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9220, "2026-10-12", 6, "Raumänderung", "KLE", "KLE", "GE", "R105", "R105,R109", "", "", "", "7a"));
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var k = Karten(plan, "2026-10-12", 6).Single();
        var raeume = k["raeume"]!.AsArray();
        Assert.Contains(raeume, r => r!["k"]!.GetValue<string>() == "R109" && r["plus"]!.GetValue<bool>());
        Assert.Contains(raeume, r => r!["k"]!.GetValue<string>() == "R105" && !r["plus"]!.GetValue<bool>());
        Assert.Equal(new[] { "aenderung" }, Marker(k));
        // nur der Raum hat sich geändert: die Lehrkraft bleibt ein normaler Wert (kein Label)
        Assert.All(k["lehrer"]!.AsArray(), w => { Assert.Null(w!["alt"]); Assert.False(w["neu"]!.GetValue<bool>()); Assert.False(w["plus"]!.GetValue<bool>()); });
    }

    [Fact]
    public async Task Vertretung_im_eigenen_Plan_der_Vertretung_mit_Lehrer_Label()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 77;
        var har = Stunden(paket, "2026-10-12", "7a", 2).Single(z => z["lehrer"]!.AsArray().Any(l => l!.GetValue<string>() == "HAR"));
        har["status"] = "vertreten"; har["lehrer"] = new JsonArray("MUE"); har["lehrerAlt"] = new JsonArray("HAR"); har["vertretung"] = 9280;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9280, "2026-10-12", 2, "Vertretung", "HAR", "MUE", "KR", "R105", "R105", "", "", "", "7a", "7b"));
        var plan = await PlanAsync(paket, "lehrer", "MUE", "2026-10-12");
        var k = Karten(plan, "2026-10-12", 2).Single(x => x["fach"]!.GetValue<string>() == "KR");
        // die vertretende Lehrkraft steht als geänderter Wert (grünes Label „MUE“, Hover „HAR → MUE“) – auch in ihrem eigenen Plan
        var w = k["lehrer"]!.AsArray().Single(x => x!["k"]!.GetValue<string>() == "MUE")!;
        Assert.Equal("HAR", w["alt"]!.GetValue<string>());
        Assert.True(w["neu"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Raumaenderung_neuer_Raum_als_Label_Lehrkraft_ohne_Label()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 76;
        var s = Stunden(paket, "2026-10-12", "7a", 1).Single();     // D bei LAN in R105
        s["status"] = "vertreten"; s["raeume"] = new JsonArray("R109"); s["vertretung"] = 9270;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9270, "2026-10-12", 1, "Raum-Vtr.", "LAN", "???", "D", "R105", "R109", "", "", "", "7a"));
        foreach (var (typ, element) in new[] { ("klasse", "7a"), ("lehrer", "LAN") })
        {
            var plan = await PlanAsync(paket, typ, element, "2026-10-12");
            var k = Karten(plan, "2026-10-12", 1).Single();
            var raum = k["raeume"]!.AsArray().Single()!;
            Assert.Equal("R109", raum["k"]!.GetValue<string>());          // neuer Raum: grünes Label, Hover „R105 → R109“
            Assert.Equal("R105", raum["alt"]!.GetValue<string>());
            var lehrer = k["lehrer"]!.AsArray().Single()!;
            Assert.Equal("LAN", lehrer["k"]!.GetValue<string>());
            Assert.Null(lehrer["alt"]);
            Assert.False(lehrer["neu"]!.GetValue<bool>());
            Assert.Equal(new[] { "aenderung" }, Marker(k));
        }
    }

    // ------------------------------------------------------------------ Kopplung: alle Blöcke bleiben

    [Fact]
    public async Task Kopplung_Vertretung_in_den_Raum_der_anderen_Gruppe_beide_Bloecke_bleiben()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 72;
        // 7a Mo 2. Stunde: zwei Gruppen im gleichen Fach (ER bei HAR in R105, ER bei ZIM in R106); HAR wird von MUE vertreten, die
        // Gruppe geht in R106 – früher wurden beide Karten (gleiches Fach, gleicher Raum) zu einer zusammengefasst
        var zeilen = Stunden(paket, "2026-10-12", "7a", 2).ToList();
        Assert.Equal(2, zeilen.Count);
        var har = zeilen.Single(z => z["lehrer"]!.AsArray().Any(l => l!.GetValue<string>() == "HAR"));
        har["fach"] = "ER"; har["status"] = "vertreten"; har["lehrer"] = new JsonArray("MUE"); har["lehrerAlt"] = new JsonArray("HAR");
        har["raeume"] = new JsonArray("R106"); har["vertretung"] = 9230;
        paket["vertretungen"]!.AsArray().Add(Vertretung(9230, "2026-10-12", 2, "Vertretung", "HAR", "MUE", "ER", "R105", "R106", "", "", "", "7a", "7b"));
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var k = Karten(plan, "2026-10-12", 2);
        Assert.Equal(2, k.Count);
        Assert.Contains(k, x => x["status"]!.GetValue<string>() == "regulaer" && x["lehrer"]![0]!["k"]!.GetValue<string>() == "ZIM");
        Assert.Contains(k, x => x["status"]!.GetValue<string>() == "geaendert" && x["lehrer"]![0]!["k"]!.GetValue<string>() == "MUE"
                                && x["lehrer"]![0]!["alt"]!.GetValue<string>() == "HAR");
    }

    [Fact]
    public async Task Kopplung_nur_Aenderungen_ersetzt_genau_die_betroffene_Zeile()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 73;
        // Kopplung 7a/7b (Unterricht 169): zwei Sportgruppen (HAR in R105, ZIM in R106/R108) laut Stundenplan
        var u = paket["perioden"]![0]!["unterricht"]!.AsArray().First(x => x!["id"]!.GetValue<int>() == 169)!;
        foreach (var z in u["zeilen"]!.AsArray()) z!["fach"] = "SP";
        // nur Änderungen übertragen: ZIMs Gruppe offen (ohne LehrerAlt – die abwesende Lehrkraft steht nur in der Vertretung)
        var tag = Tag(paket, "2026-10-12");
        tag["nurAenderungen"] = true;
        tag["stunden"] = new JsonArray(new JsonObject
        {
            ["stunde"] = 2, ["unterricht"] = 169, ["fach"] = "SP", ["klassen"] = new JsonArray("7a", "7b"), ["lehrer"] = new JsonArray(), ["raeume"] = new JsonArray("R106"),
            ["status"] = "offen", ["vertretung"] = 9240, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(Vertretung(9240, "2026-10-12", 2, "Vertretung", "ZIM", "???", "SP", "R106", "R106", "", "", "", "7a", "7b"));
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var k = Karten(plan, "2026-10-12", 2);
        // HARs Gruppe bleibt regulär daneben, ZIMs Gruppe ist offen (früher verschwand HARs Block und ZIM stand doppelt da)
        Assert.Equal(2, k.Count);
        Assert.Contains(k, x => x["status"]!.GetValue<string>() == "regulaer" && x["lehrer"]![0]!["k"]!.GetValue<string>() == "HAR");
        Assert.Contains(k, x => x["status"]!.GetValue<string>() == "offen" && x["lehrer"]!.AsArray().Any(w => w!["alt"]?.GetValue<string>() == "ZIM"));
        Assert.DoesNotContain(k, x => x["status"]!.GetValue<string>() == "regulaer" && x["lehrer"]![0]!["k"]!.GetValue<string>() == "ZIM");
    }

    [Fact]
    public async Task Kopplung_nur_Aenderungen_Vertretung_durch_Kollegin_der_Kopplung_nimmt_deren_Block_nicht_weg()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 74;
        // KR (HAR) und ER (ZIM) gekoppelt; HARs Stunde wird (im Paket) mit ZIM als Lehrkraft geführt (Zusammenlegung, LehrerAlt HAR):
        // früher wurden beide Zeilen entfernt, weil ZIM betroffen war – ZIMs ER-Block fehlte
        var tag = Tag(paket, "2026-10-12");
        tag["nurAenderungen"] = true;
        tag["stunden"] = new JsonArray(new JsonObject
        {
            ["stunde"] = 2, ["unterricht"] = 169, ["fach"] = "KR", ["klassen"] = new JsonArray("7a", "7b"), ["lehrer"] = new JsonArray("ZIM"), ["lehrerAlt"] = new JsonArray("HAR"),
            ["raeume"] = new JsonArray("R105"), ["status"] = "vertreten", ["vertretung"] = 9250, ["text"] = "",
        });
        paket["vertretungen"]!.AsArray().Add(Vertretung(9250, "2026-10-12", 2, "Vertretung", "HAR", "ZIM", "KR", "R105", "R105", "", "", "", "7a", "7b"));
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-12");
        var k = Karten(plan, "2026-10-12", 2);
        Assert.Equal(2, k.Count);
        Assert.Contains(k, x => x["fach"]!.GetValue<string>() == "ER" && x["status"]!.GetValue<string>() == "regulaer");
        Assert.Contains(k, x => x["fach"]!.GetValue<string>() == "KR" && x["status"]!.GetValue<string>() == "geaendert");
    }

    // ------------------------------------------------------------------ Zusatzstunden: Vertretungstext

    [Fact]
    public void Zusatztext_nur_Standardtexte_entfallen()
    {
        Assert.Equal("", Plandienst.ZusatzText("wegen Sondereinsatz", "Sondereinsatz"));
        Assert.Equal("", Plandienst.ZusatzText("Vertretung durch Verlegung", "Verlegung"));
        Assert.Equal("", Plandienst.ZusatzText("Zusatzunterricht", "Zusatzunterricht"));
        Assert.Equal("", Plandienst.ZusatzText("aus Vertretung 12 (13.10.2026, 3. Std.)", ""));
        Assert.Equal("Bitte Lesebuch mitbringen", Plandienst.ZusatzText("Bitte Lesebuch mitbringen", "Zusatzunterricht"));
        Assert.Equal("Raum vorher aufschließen", Plandienst.ZusatzText("wegen Sondereinsatz · Raum vorher aufschließen", "Sondereinsatz"));
    }

    [Fact]
    public async Task Zusatzstunde_manueller_Text_bleibt_Standardtext_entfaellt_in_der_App_API()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 75;
        var tag = Tag(paket, "2026-10-15");
        foreach (var (st, id, art, text) in new[] { (6, 9260, "Zusatzunterricht", "Förderkurs Lesen – bitte Lesebuch mitbringen"), (7, 9261, "Sondereinsatz", "wegen Sondereinsatz") })
        {
            tag["stunden"]!.AsArray().Add(new JsonObject
            {
                ["stunde"] = st, ["fach"] = "FÖ", ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray("MEY"), ["raeume"] = new JsonArray("R105"),
                ["status"] = "zusaetzlich", ["vertretung"] = id, ["text"] = text,
            });
            paket["vertretungen"]!.AsArray().Add(Vertretung(id, "2026-10-15", st, art, "", "MEY", "FÖ", "R105", "R105", "", "", text, "7a"));
        }
        var plan = await PlanAsync(paket, "klasse", "7a", "2026-10-15");
        var manuell = Karten(plan, "2026-10-15", 6).Single();
        Assert.True(manuell["zusaetzlich"]!.GetValue<bool>());
        Assert.Equal("Förderkurs Lesen – bitte Lesebuch mitbringen", manuell["text"]!.GetValue<string>());
        Assert.Equal(new[] { "plus" }, Marker(manuell));
        var standard = Karten(plan, "2026-10-15", 7).Single();
        Assert.True(standard["zusaetzlich"]!.GetValue<bool>());
        Assert.Equal("", standard["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ Web

    [Fact]
    public void Web_Verlegung_Hover_Marker_Plus_und_gekuerzte_Zeilen()
    {
        var js = Datei("js", "plangitter.js");
        // Verlegung: Hover „Ursprung → Ziel“ (rot an der ursprünglichen Stunde), auch beim Zeigen auf die Karte
        Assert.Contains("const verlegungsZeit = z => `${datumDe(z.datum)}${z.beginn ? ` | ${z.beginn}${z.ende ? ` - ${z.ende}` : ''}` : ''}`;", js);
        Assert.Contains("${verlegungsZeit(k.verlegungVon)} → ${verlegungsZeit(k.verlegungNach)}", js);
        Assert.Contains("data-hover-art=\"rot\"", js);
        // Hover nur auf dem Symbol (nicht auf der ganzen Karte), etwas kleiner
        Assert.DoesNotContain("karte.querySelector('.mks .mk-verlegung[data-hover]')", js);
        Assert.Contains("data-hover-mitte data-hover-klein></i>", js);
        // mehrere Symbole untereinander
        Assert.Contains("if (k.status === 'klausur') { m.push('<i class=\"mk-kreis\"></i>'); if (wertGeaendert(k)) m.push('<i class=\"mk-quadrat\"></i>'); }", js);
        // hinzugefügter Wert: „+B070“
        Assert.Contains("if (w.neu) return `<span class=\"lbl lbl-plus\">+${e(w.k)}</span>`;", js);
        // gekürzte Zeilen: Hover je Zeile, Wertelisten mit „+N“ und Pillen, Fach mit vollem Namen
        Assert.Contains("export function listenKuerzen(wurzel)", js);
        Assert.Contains("if (zeile && karte.contains(zeile) && zeileGekuerzt(zeile)) { hoverZeigen(zeile); return; }", js);
        Assert.Contains("data-pillen=", js);
        Assert.Contains("const fachVoll = k.fachName && k.fachName !== k.fach ? k.fachName : '';", js);
        // Vertretung: Label der vertretenden Lehrkraft auch in deren eigenem Plan (keine Ausnahme für die eigene Lehrkraft)
        Assert.DoesNotContain("lehrerOhneEigene", js);
        Assert.Contains(": k.lehrer.length > 1 || k.lehrer.some(w => w.alt || w.neu) ? werteListe(k.lehrer, entfall, true) : '';", js);
        // grüne Labels genau mittig über dem Label
        Assert.Contains("r.left + r.width / 2 - b / 2", js);
        // keine „Raum gesperrt“/„Klasse abwesend“ über dem Tag
        Assert.Contains("!/^(abwesend|raum gesperrt|klasse abwesend)\\b/i.test(h)", js);
        // Aufsichten wie die Stundenkarten bis an die Linie
        Assert.Contains("top:${aufbau.pause.get(nach) + 1}px;height:${PAUSE - 1}px;left:calc(${j * breite}% + 1px);width:calc(${breite}% - 1px)", js);
        var css = Datei("css", "karten.css");
        Assert.Contains(".u-hover-label.rot { background: #f44336;", css);
        Assert.Contains(".u-hover-label.klein { font-size: 13px;", css);
        Assert.Contains("M13 3.5 23.5 21H2.5Z' fill='%23ff4040' stroke='%23ff4040' stroke-width='4' stroke-linejoin='round'/%3E%3Cpath d='M7.4 16h11.2M14.2 11.6l4.4 4.4-4.4 4.4' fill='none' stroke='%23fff' stroke-width='2.5'", css);
        Assert.Contains(".u-hover-label.pillen span { background: #6b7680;", css);
        Assert.Contains(".u-hover-label.pillen span.p-weg, .u-hover-label.pillen span.p-aus { text-decoration: line-through;", css);
        Assert.Contains(".std .z-liste > .z-namen", css);
        Assert.Contains(".std .mks { gap: 4px; }", css);
    }

    [Fact]
    public void Web_Stundendetails_Badges_oben_Knoepfe_kleiner_Abmelden_schliesst_Dialog()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("<div class=\"sd-titel\"><h2>${titel.join('<span class=\"trenn\">|</span>')}${pille ? `<span class=\"sd-statuspille\">${pille}</span>` : ''}", sd);
        Assert.Contains("window.addEventListener('untplan:abmeldung', abmeldung, { once: true });", sd);
        Assert.Contains("if (beendet || !el.isConnected) return;", sd);
        var app = Datei("js", "app.js");
        Assert.Contains("function dialogeSchliessen()", app);
        Assert.Contains("async function abmelden() {\n  dialogeSchliessen();", app);
        var css = Datei("css", "karten.css");
        Assert.Contains(".sd-titel h2 .sd-statuspille:not(.sd-einzelpille), .sd-kopf.grau .sd-titel h2 .sd-statuspille:not(.sd-einzelpille) { background: #6b7680; color: #fff; }", css);
        Assert.Contains(".sd-fuss.sd-status .sd-orange { height: 46px; min-height: 46px;", css);
    }
    // ------------------------------------------------------------------ Lehrstoff aus Vorwochen

    [Fact]
    public async Task Lehrstoff_aus_Vorwochen_nur_mit_Text_neueste_zuerst_und_je_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tage = Enumerable.Range(0, 14).Select(i => new DateOnly(2026, 10, 12).AddDays(i));
        // Unterricht der 7a mit mindestens drei regulären Stunden ab heute (Mi 14.10.) und einer Lehrkraft
        var alle = tage.Where(d => d >= new DateOnly(2026, 10, 14)).SelectMany(d => plan.Karten(stand, d, Elementtyp.Klasse, "7a"))
            .Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1)
            .GroupBy(x => x.Unterricht).Select(g => g.OrderBy(x => x.Datum).ThenBy(x => x.Von).ToList()).First(g => g.Count >= 3);
        var lehrer = alle[0].Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        async Task Eintragen(Karte k, string thema, string ha = "") =>
            await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema, hausaufgabe = ha }));
        await Eintragen(alle[0], "Thema eins");
        await Eintragen(alle[1], "", "nur Hausaufgabe");      // ohne Lehrstoff: erscheint nicht
        var ziel = alle[2];
        var liste = (await Testserver.HolenAsync(c, $"/api/app/klassenbuch/lehrstoff/vorstunden?datum={ziel.Datum}&stunde={ziel.Von}&unterricht={ziel.Unterricht}")).AsArray();
        Assert.Single(liste);
        Assert.Equal("Thema eins", liste[0]!["thema"]!.GetValue<string>());
        Assert.Equal(alle[0].Datum, liste[0]!["datum"]!.GetValue<string>());
        // gleiche Lehrkraft: weiter dabei; andere Lehrkraft (gekoppelter Unterricht): nicht
        Assert.Single((await Testserver.HolenAsync(c, $"/api/app/klassenbuch/lehrstoff/vorstunden?datum={ziel.Datum}&stunde={ziel.Von}&unterricht={ziel.Unterricht}&lehrer={lehrer}")).AsArray());
        Assert.Empty((await Testserver.HolenAsync(c, $"/api/app/klassenbuch/lehrstoff/vorstunden?datum={ziel.Datum}&stunde={ziel.Von}&unterricht={ziel.Unterricht}&lehrer=XYZ")).AsArray());
    }

    [Fact]
    public void Web_Lehrstoff_Dialog_breiter_mit_Uebernehmen_und_Anhaengen()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("Lehrstoff aus Vorwochen", sd);
        Assert.Contains("data-ad-anhaengen=", sd);
        Assert.Contains("data-ad-kopie=", sd);
        Assert.Contains("/api/app/klassenbuch/lehrstoff/vorstunden?datum=${k.datum}&stunde=${bereich().von}&unterricht=${k.unterricht}${einzigeLehrkraft()}", sd);
        Assert.Contains("}, 600);   // etwas breiter", sd);
        Assert.Contains(".ad-liste.ad-vorwochen .ad-vw-knopf", Datei("css", "karten.css"));
    }
    [Fact]
    public void Web_nur_ein_Hover_Veranstaltungskopf_gestrichelt_Aufsichtsbalken_Zeitleiste()
    {
        // gekürzte Kartenzeilen: nur der dunkelgraue Hover (plangitter.js), kein zweiter heller Kürzungs-Hover
        Assert.Contains(".modal-footer, .u-kuerzung-hover, .std')", Datei("js", "ui.js"));
        var sd = Datei("js", "stundendetails.js");
        // Veranstaltung: oben keine Bezeichnung und kein Badge; Name unten unter „Information zur Stunde“
        Assert.Contains(": k.status === 'veranstaltung' ? e(k.fach || '') : e(k.fach || k.text || k.art || 'Stunde');", sd);
        Assert.Contains("const pille = istEntfall() ? '' : k.status === 'klausur' ? 'Prüfung' : k.zusaetzlich ? 'Zusätzl. Unterrichtsstunde' : '';", sd);
        Assert.Contains("'Information zur Stunde'", sd);
        // sehr viele Elemente im Kopf: „… +N“ mit Hover
        Assert.Contains("class=\"sd-mehr\" data-hover=", sd);
        Assert.Contains("const klassenTeil = alleKlassen.length ? wertKopf(alleKlassen, 6,", sd);
        // Uhrzeiten auf den Karten: links nur Stundennummern
        var js = Datei("js", "plangitter.js");
        Assert.Contains("if (!kartenZeiten) html += `<div class=\"tt-zeit beginn\"", js);
        Assert.Contains("if (!kartenZeiten) html += `<div class=\"tt-zeit ende\"", js);
        var css = Datei("css", "karten.css");
        Assert.Contains(".std.vorgemerkt, .std.lehrer-abwesend, .std.bereitschaft { background: #f1f5f8 !important; }", css);
        Assert.Contains(".tt .aufsicht::before { left: 6px; top: 6px; bottom: 6px; width: 4px; border-radius: 2px; }", css);
    }
}
