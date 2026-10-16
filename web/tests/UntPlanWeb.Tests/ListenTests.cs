using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Vertretung ohne Lehrkraft („+++“) und Räume auf Veranstaltungskarten.</summary>
public class FachpunktTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    /// <summary>Beispielpaket, in dem die Vertretung von WAG (6a, Mittwoch 1. Stunde) ohne Lehrkraft stattfindet.</summary>
    private static (JsonObject Paket, int Id) OhneLehrerPaket(bool mitVertretung)
    {
        var p = Testserver.Beispielpaket();
        p["revision"] = 2;
        var stunde = p["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == "2026-10-14")!["stunden"]!.AsArray()
            .Select(x => x!.AsObject()).First(x => x["stunde"]!.GetValue<int>() == 1 && x["klassen"]!.AsArray().Any(k => k!.GetValue<string>() == "6a") && x["vertretung"] != null);
        var id = stunde["vertretung"]!.GetValue<int>();
        stunde["lehrer"] = new JsonArray();
        stunde["status"] = "vertreten";
        stunde["lehrerAlt"] = new JsonArray("WAG");
        var vertretungen = p["vertretungen"]!.AsArray();
        var v = vertretungen.Single(x => x!["id"]!.GetValue<int>() == id)!;
        if (mitVertretung) { v["vertreter"] = "+++"; v["art"] = "Vtr. ohne Lehrer"; }
        else vertretungen.Remove(v);
        return (p, id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Vertretung_ohne_Lehrkraft_zeigt_bisherige_Lehrkraft_durchgestrichen(bool mitVertretung)
    {
        await using var s = await Testserver.StartenAsync();
        var (paket, _) = OhneLehrerPaket(mitVertretung);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-14");
        var k = Karten(plan, "2026-10-14", 1).Single();
        Assert.Equal("geaendert", k["status"]!.GetValue<string>());
        var lehrer = k["lehrer"]!.AsArray().Single()!;
        Assert.Equal("", lehrer["k"]!.GetValue<string>());
        Assert.Equal("WAG", lehrer["alt"]!.GetValue<string>());
        Assert.True(lehrer["neu"]!.GetValue<bool>());
        Assert.DoesNotContain("+++", plan.ToJsonString());
        Assert.DoesNotContain("???", k.ToJsonString());
        // ohne eigenen Vertretungstext: Hinweis „Vertr. o. Lehrkr.“
        Assert.Equal("Vertr. o. Lehrkr.", k["text"]!.GetValue<string>());

        if (mitVertretung)
        {
            // Vertretungsplan (auch Schülerversion): keine neue Lehrkraft, die bisherige durchgestrichen
            var schueler = await s.AlsAsync("klasse-6a", "schueler", "6a");
            var vt = await Testserver.HolenAsync(schueler, "/api/app/vertretungen?datum=2026-10-14&tage=1");
            var z = vt["tage"]![0]!["zeilen"]!.AsArray().First(x => x!["stundeVon"]!.GetValue<int>() == 1)!;
            Assert.Equal("", z["lehrer"]!.GetValue<string>());
            Assert.Equal("WAG", z["lehrerAlt"]!.GetValue<string>());
            Assert.Equal("geaendert", z["status"]!.GetValue<string>());
            Assert.Equal("Vertr. o. Lehrkr.", z["text"]!.GetValue<string>());
            Assert.True(z["ohneLehrer"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task Vertretung_ohne_Lehrkraft_behaelt_eigenen_Text()
    {
        await using var s = await Testserver.StartenAsync();
        var (paket, id) = OhneLehrerPaket(true);
        paket["vertretungen"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == id)!["text"] = "Aufgaben im Raum";
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-14");
        Assert.Equal("Aufgaben im Raum", Karten(plan, "2026-10-14", 1).Single()["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Kopplung_mit_unterschiedlichen_Faechern_bleibt_einzelne_Karten()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        // Unterricht 167 (Religion 5a/5b): KR bei BEC in R101 und ER bei ZIM in R102 – unterschiedliche Fächer, zwei Karten
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-12");
        var karten = Karten(plan, "2026-10-12", 5).ToList();
        Assert.Equal(2, karten.Count);
        var kr = karten.Single(k => k["fach"]!.GetValue<string>() == "KR");
        var er = karten.Single(k => k["fach"]!.GetValue<string>() == "ER");
        Assert.Equal("BEC", kr["lehrer"]!.AsArray().Single()!["k"]!.GetValue<string>());
        Assert.Equal("R101", kr["raeume"]!.AsArray().Single()!["k"]!.GetValue<string>());
        Assert.Equal("ZIM", er["lehrer"]!.AsArray().Single()!["k"]!.GetValue<string>());
        Assert.Equal("R102", er["raeume"]!.AsArray().Single()!["k"]!.GetValue<string>());
        // beide Karten nennen auch die gekoppelte Klasse
        Assert.All(karten, k => Assert.Equal(new[] { "5a", "5b" }, k["klassen"]!.AsArray().Select(x => x!["k"]!.GetValue<string>())));
        // im Lehrerplan bleibt es bei der eigenen Zeile
        var bec = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=BEC&datum=2026-10-12");
        Assert.Equal("KR", Karten(bec, "2026-10-12", 5).Single()["fach"]!.GetValue<string>());
    }

    [Fact]
    public async Task Kopplung_mit_gleichem_Fach_und_Raum_ist_eine_Karte_mit_allen_Lehrkraeften_und_Klassen()
    {
        await using var s = await Testserver.StartenAsync();
        var p = Testserver.Beispielpaket();
        p["revision"] = 2;
        // Montag 5. Stunde: beide Zeilen von Unterricht 167 als KR in R101; ZIM unterrichtet zusätzlich die 6a mit
        var zeilen = p["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == "2026-10-12")!["stunden"]!.AsArray()
            .Select(x => x!.AsObject()).Where(x => x["unterricht"]?.GetValue<int>() == 167).ToList();
        var er = zeilen.Single(x => x["fach"]!.GetValue<string>() == "ER");
        er["fach"] = "KR";
        er["raeume"] = new JsonArray("R101");
        er["klassen"] = new JsonArray("5a", "5b", "6a");
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(p)).StatusCode);
        var c = await s.AlsAsync("admin");

        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-12");
        var k = Karten(plan, "2026-10-12", 5).Single();
        Assert.Equal("KR", k["fach"]!.GetValue<string>());
        Assert.Equal(new[] { "BEC", "ZIM" }, k["lehrer"]!.AsArray().Select(x => x!["k"]!.GetValue<string>()).OrderBy(x => x));
        Assert.Equal("R101", k["raeume"]!.AsArray().Single()!["k"]!.GetValue<string>());
        // die jeweils anderen Klassen stehen mit auf der Karte
        Assert.Equal(new[] { "5a", "5b", "6a" }, k["klassen"]!.AsArray().Select(x => x!["k"]!.GetValue<string>()).OrderBy(x => x));
        // im Lehrerplan von ZIM: eine Karte mit allen Klassen und beiden Lehrkräften
        var zim = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=ZIM&datum=2026-10-12");
        var z = Karten(zim, "2026-10-12", 5).Single();
        Assert.Equal(new[] { "BEC", "ZIM" }, z["lehrer"]!.AsArray().Select(x => x!["k"]!.GetValue<string>()).OrderBy(x => x));
    }

    [Fact]
    public async Task Fachwechsel_zeigt_alte_Stunde_als_Entfall_und_neue_als_Aenderung()
    {
        await using var s = await Testserver.StartenAsync();
        var p = Testserver.Beispielpaket();
        p["revision"] = 2;
        // 6a, Mittwoch 1. Stunde: Vertretung KRU für WAG – statt Englisch jetzt Mathematik
        var stunde = p["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == "2026-10-14")!["stunden"]!.AsArray()
            .Select(x => x!.AsObject()).Single(x => x["vertretung"]?.GetValue<int>() == 450);
        stunde["fach"] = "M";
        p["vertretungen"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == 450)!["neuesFach"] = "M";
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(p)).StatusCode);
        var c = await s.AlsAsync("admin");

        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-14");
        var karten = Karten(plan, "2026-10-14", 1).ToList();
        Assert.Equal(2, karten.Count);
        var neu = karten.Single(k => k["status"]!.GetValue<string>() == "geaendert");
        Assert.Equal("M", neu["fach"]!.GetValue<string>());
        Assert.Equal("E", neu["fachAlt"]!.GetValue<string>());
        var alt = karten.Single(k => k["status"]!.GetValue<string>() == "entfall");
        Assert.Equal("E", alt["fach"]!.GetValue<string>());
        Assert.Equal("WAG", alt["lehrer"]!.AsArray().Single()!["k"]!.GetValue<string>());
        Assert.Null(alt["unterricht"]?.GetValue<int?>());

        // Plan der Vertretung: nur die neue Stunde; Plan der abwesenden Lehrkraft: ihre abgegebene Stunde
        var kru = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=KRU&datum=2026-10-14");
        Assert.Equal("geaendert", Karten(kru, "2026-10-14", 1).Single()["status"]!.GetValue<string>());
        var wag = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=WAG&datum=2026-10-14");
        Assert.Equal("abwesend", Karten(wag, "2026-10-14", 1).Single()["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Export_aus_UntPlan_ueberträgt_Vertretung_ohne_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        var datum = new DateOnly(2026, 10, 14);
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        // Lehrkraft fehlt in einer Stunde; die Vertretung findet ohne Lehrkraft statt („+++“)
        var r = Tagesplan.Regulaer(d, datum).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.Unterricht.Zeilen[0].KlassenIds.Count > 0 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = datum, Bis = datum, StundeVon = r.Stunde, StundeBis = r.Stunde });
        Vertretungen.Aktualisieren(d, datum);
        var v = d.Vertretungen.Single(x => x.Datum == datum && x.Stunde == r.Stunde && x.UnterrichtId == r.Unterricht.Id);
        Vertretungen.OhneLehrer(v);
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7 });
        var alt = new DatenIndex(d).KLehrer(v.LehrerId);
        var stunde = paket.Tage.Single(t => t.Datum == "2026-10-14").Stunden.First(x => x.Vertretung == v.Id);
        Assert.Empty(stunde.Lehrer);
        Assert.Equal(new[] { alt }, stunde.LehrerAlt);
        Assert.True((await UntPlanWebExport.HochladenAsync(new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel },
            paket with { Revision = 1 })).Ok);

        var klasse = new DatenIndex(d).K(ElementTyp.Klasse, v.NeueKlassenIds[0]);
        var karte = s.Dienst<Plandienst>().Karten(s.Dienst<Paketdienst>().Aktuell!, datum, Elementtyp.Klasse, klasse).First(k => k.Vertretung == v.Id);
        Assert.Equal("geaendert", karte.Status);
        Assert.Equal(new Wert("", alt, true), Assert.Single(karte.Lehrer));
    }

    [Fact]
    public async Task Veranstaltung_mit_Raum_und_allen_Lehrkraeften_steht_auf_der_Karte()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("admin");
        // Lehrerkonferenz (7.–8. Stunde) in EDV2: Karte im Lehrerplan, im Raumplan und in der Tagesübersicht der Räume
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=MUE&datum=2026-10-14");
        var k = Karten(plan, "2026-10-14", 7).Single(x => x["text"]!.GetValue<string>() == "Lehrerkonferenz");
        Assert.Equal("veranstaltung", k["status"]!.GetValue<string>());
        Assert.Contains(k["raeume"]!.AsArray(), r => r!["k"]!.GetValue<string>() == "EDV2");
        // alle teilnehmenden Lehrkräfte der Veranstaltung (22), nicht nur MUE
        Assert.Equal(22, k["lehrer"]!.AsArray().Count);
        Assert.Contains(k["lehrer"]!.AsArray(), l => l!["k"]!.GetValue<string>() == "LAN");
        var raum = await Testserver.HolenAsync(c, "/api/app/plan?typ=raum&element=EDV2&datum=2026-10-14");
        Assert.Contains(Karten(raum, "2026-10-14", 7), x => x["text"]!.GetValue<string>() == "Lehrerkonferenz");
        var tag = await Testserver.HolenAsync(c, "/api/app/tag?typ=raum&datum=2026-10-14");
        var zeile = tag["zeilen"]!.AsArray().Single(z => z!["kurz"]!.GetValue<string>() == "EDV2")!;
        Assert.Contains(zeile["karten"]!.AsArray(), x => x!["text"]!.GetValue<string>() == "Lehrerkonferenz" && x["raeume"]![0]!["k"]!.GetValue<string>() == "EDV2");
    }
}

/// <summary>Listen wie im Vorbild: Unterrichte, Prüfungen, Hausaufgaben, Klassenbucheinträge, Abwesenheiten, Offene Stunden, Berichte.</summary>
public class ListenTests
{
    [Fact]
    public async Task Meine_Unterrichte_der_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("sne", "lehrer", "SNE");
        var liste = (await Testserver.HolenAsync(c, "/api/app/unterricht")).AsArray();
        Assert.NotEmpty(liste);
        Assert.All(liste, u => Assert.Contains("SNE", u!["lehrer"]!.AsArray().Select(x => x!.GetValue<string>())));
        Assert.Contains(liste, u => u!["termine"]!.AsArray().Count > 0 && u["wochenstunden"]!.GetValue<int>() > 0);
        var klasse = (await Testserver.HolenAsync(c, "/api/app/unterricht?klasse=7a")).AsArray();
        Assert.All(klasse, u => Assert.Contains("7a", u!["klassen"]!.AsArray().Select(x => x!.GetValue<string>())));
        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/unterricht")).StatusCode);
    }

    [Fact]
    public async Task Pruefungen_aus_UntPlan_und_vorgemerkt()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = 1, stundeBis = 2, klassen = new[] { "7a" }, titel = "Mathe-Arbeit" }));
        var alle = (await Testserver.HolenAsync(c, "/api/app/pruefungen?von=2026-10-12&bis=2026-10-20")).AsArray();
        Assert.Contains(alle, p => p!["titel"]!.GetValue<string>() == "Mathematik-Klausur" && p["status"]!.GetValue<string>() == "geplant" && p["beginn"]!.GetValue<string>().Length > 0);
        Assert.Contains(alle, p => p!["titel"]!.GetValue<string>() == "Mathe-Arbeit" && p["status"]!.GetValue<string>() == "vorgemerkt");
        // Klassenkonto: nur die eigene Klasse, Filter werden ignoriert
        var ef = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var eigene = (await Testserver.HolenAsync(ef, "/api/app/pruefungen?von=2026-10-12&bis=2026-10-20&klasse=EF")).AsArray();
        Assert.Single(eigene);
        Assert.Equal("Mathe-Arbeit", eigene[0]!["titel"]!.GetValue<string>());
    }

    [Fact]
    public async Task Hausaufgaben_Eintraege_Abwesenheiten_Offene_Stunden_und_Berichte()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var heute = new DateOnly(2026, 10, 14);
        var k = plan.Karten(stand, heute, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var kl = await s.AlsAsync("sl-test", "leitung", "");

        // Offene Stunden: vor dem Eintrag enthalten, danach nicht mehr
        var offen = (await Testserver.HolenAsync(c, "/api/app/klassenbuch/offen?tage=30")).AsArray();
        Assert.Contains(offen, o => o!["datum"]!.GetValue<string>() == k.Datum && o["von"]!.GetValue<int>() == k.Von);
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Satz des Pythagoras", hausaufgabe = "S. 40 Nr. 1", faellig = "2026-10-16" }));
        offen = (await Testserver.HolenAsync(c, "/api/app/klassenbuch/offen?tage=30")).AsArray();
        Assert.DoesNotContain(offen, o => o!["datum"]!.GetValue<string>() == k.Datum && o["von"]!.GetValue<int>() == k.Von);

        var ha = (await Testserver.HolenAsync(c, $"/api/app/hausaufgaben?lehrer={lehrer}&von=2026-10-01&bis=2026-10-31")).AsArray();
        Assert.Equal("S. 40 Nr. 1", Assert.Single(ha)!["hausaufgabe"]!.GetValue<string>());
        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var haSchueler = (await Testserver.HolenAsync(schueler, "/api/app/hausaufgaben?klasse=6a")).AsArray();
        Assert.Equal("2026-10-16", Assert.Single(haSchueler)!["faellig"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/abwesenheiten")).StatusCode);

        // Fehlzeiten → Abwesenheitsliste (Block zusammengefasst) und Berichte
        var anw = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}");
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = new object[] { new { schuelerId = sid, art = "fehlt" } } }));
        var ab = (await Testserver.HolenAsync(kl, "/api/app/abwesenheiten?von=2026-10-01&bis=2026-10-14&klasse=7a")).AsArray();
        var z = Assert.Single(ab)!;
        Assert.Equal(k.Von, z["stundeVon"]!.GetValue<int>());
        Assert.Equal(k.Bis, z["stundeBis"]!.GetValue<int>());
        Assert.Equal(k.Bis - k.Von + 1, z["ids"]!.AsArray().Count);
        Assert.Equal("offen", z["status"]!.GetValue<string>());
        Assert.Empty((await Testserver.HolenAsync(kl, "/api/app/abwesenheiten?von=2026-10-01&bis=2026-10-14&status=entschuldigt")).AsArray());

        var bericht = await Testserver.HolenAsync(kl, "/api/app/berichte/fehlzeiten?klasse=7a&von=2026-10-01&bis=2026-10-14");
        Assert.Equal(k.Bis - k.Von + 1, bericht["zeilen"]!.AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == sid)!["stunden"]!.GetValue<int>());
        var csv = await kl.GetAsync("/api/app/berichte/fehlzeiten?klasse=7a&von=2026-10-01&bis=2026-10-14&format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.StartsWith("Klasse;Name;Vorname;Fehlstunden", text.TrimStart('﻿'));
        var lehrstoff = await c.GetStringAsync("/api/app/berichte/lehrstoff?klasse=7a&von=2026-10-01&bis=2026-10-14&format=csv");
        Assert.Contains("Satz des Pythagoras", lehrstoff);

        // Klassenbucheinträge über alle Klassen, Filter nach Art
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-14", klasse = "7a", schuelerId = sid, art = "Lob", text = "Hilft anderen" }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-13", klasse = "6a", art = "Vermerk", text = "Ausflug besprochen" }));
        var ein = await Testserver.HolenAsync(c, "/api/app/klassenbucheintraege?von=2026-10-01&bis=2026-10-14");
        Assert.Equal(2, ein["eintraege"]!.AsArray().Count);
        var lob = (await Testserver.HolenAsync(c, "/api/app/klassenbucheintraege?von=2026-10-01&bis=2026-10-14&art=Lob"))["eintraege"]!.AsArray();
        Assert.False(string.IsNullOrEmpty(Assert.Single(lob)!["schueler"]!.GetValue<string>()));
    }
}
