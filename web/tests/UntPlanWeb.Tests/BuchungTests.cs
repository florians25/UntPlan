using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Buchungsmodul: Räume, Ressourcen und Aktivitäten buchen (einmalig und periodisch), Konfliktprüfung, Anzeige in den Plänen ohne
/// Vertretungsstatus, Storno einzeln bzw. als Serie, Übertragung an UntPlan als Rückmeldung „raumbuchung“, Neuer Unterricht.
/// Feste Uhr: Mittwoch, 14.10.2026; Herbstferien 17.–31.10.2026.
/// </summary>
public class BuchungTests
{
    private static object Zeit(string datum, int von, int bis) => new { wiederholung = "einmalig", datum, stundeVon = von, stundeBis = bis };

    private static object Buchung(string art, string datum, int von, int bis, string? raum, object? extra = null)
    {
        var d = new Dictionary<string, object?> { ["art"] = art, ["termine"] = new[] { new { datum, stundeVon = von, stundeBis = bis, raum } } };
        if (extra != null) foreach (var p in extra.GetType().GetProperties()) d[p.Name] = p.GetValue(extra);
        return d;
    }

    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    [Fact]
    public async Task Raum_buchen_erscheint_im_Plan_als_Buchung_ohne_Vertretung_und_blockiert_den_Raum()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // Suche: R108 ist am Donnerstag in der 3.–4. Stunde frei
        var suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = Zeit("2026-10-15", 3, 4) }));
        Assert.Contains(suche["raeume"]!.AsArray(), r => r!["kurz"]!.GetValue<string>() == "R108");
        Assert.Single(suche["termine"]!.AsArray());

        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen",
            Buchung("raum", "2026-10-15", 3, 4, "R108", new { klassen = new[] { "7a" }, titel = "Projektarbeit", fach = "D" })));
        var b = antwort["buchungen"]!.AsArray().Single()!;
        Assert.Equal("", antwort["serie"]!.GetValue<string>());
        Assert.Equal("LAN", b["lehrer"]!.GetValue<string>());

        // Raumplan, Lehrerplan und Klassenplan: Status „buchung“, keine Vertretung, kein Vertretungstext
        foreach (var (typ, element) in new[] { ("raum", "R108"), ("lehrer", "LAN"), ("klasse", "7a") })
        {
            var plan = await Testserver.HolenAsync(lan, $"/api/app/plan?typ={typ}&element={element}&datum=2026-10-15");
            var k = Karten(plan, "2026-10-15", 3).Single(x => x["text"]!.GetValue<string>() == "Projektarbeit");
            Assert.Equal("buchung", k["status"]!.GetValue<string>());
            Assert.Null(k["vertretung"]?.GetValue<int?>());
            Assert.Equal(4, k["bis"]!.GetValue<int>());
            Assert.Equal("R108", k["raeume"]!.AsArray().Single()!["k"]!.GetValue<string>());
        }
        // Vertretungsplan bleibt unberührt
        var vp = await Testserver.HolenAsync(lan, "/api/app/vertretungen?datum=2026-10-15");
        Assert.DoesNotContain("Projektarbeit", vp.ToJsonString());

        // Raum ist nun belegt: zweite Buchung (andere Lehrkraft) scheitert, Suche zeigt ihn nicht mehr, auch die bisherige Raumbuchung nicht
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var zweite = await sne.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-15", 4, 5, "R108"));
        Assert.Equal(HttpStatusCode.BadRequest, zweite.StatusCode);
        Assert.Contains("R108", await zweite.Content.ReadAsStringAsync());
        suche = await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = Zeit("2026-10-15", 4, 4) }));
        Assert.DoesNotContain(suche["raeume"]!.AsArray(), r => r!["kurz"]!.GetValue<string>() == "R108");
        var frei = await Testserver.HolenAsync(sne, "/api/app/meldungen/freieraeume?datum=2026-10-15&von=4&bis=4");
        Assert.DoesNotContain(frei.AsArray(), r => r!["kurz"]!.GetValue<string>() == "R108");
    }

    [Fact]
    public async Task Belegter_Raum_durch_Unterricht_und_rueckwirkend_wird_abgelehnt()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // Freitag 2. Stunde: Unterricht 7b in R108
        var r = await lan.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-16", 2, 2, "R108"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("belegt", await r.Content.ReadAsStringAsync());
        r = await lan.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-13", 2, 2, "R109"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        // nicht buchbarer Raum
        r = await lan.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-15", 2, 2, "R101"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("nicht buchbar", await r.Content.ReadAsStringAsync());
        // Schüler- bzw. Klassenkonten dürfen nicht buchen
        var klasse = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await klasse.GetAsync("/api/app/buchen/optionen")).StatusCode);
    }

    [Fact]
    public async Task Periodizitaet_erzeugt_Serie_ueberspringt_Ferien_und_ist_einzeln_und_gesamt_stornierbar()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var zeit = new { wiederholung = "woechentlich", von = "2026-10-15", bis = "2026-11-12", wochentage = new[] { 3 }, stundeVon = 5, stundeBis = 6 };
        var termine = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/termine", zeit));
        Assert.Equal(new[] { "2026-10-15", "2026-11-05", "2026-11-12" }, termine["termine"]!.AsArray().Select(t => t!["datum"]!.GetValue<string>()));
        Assert.Equal(2, termine["uebersprungen"]!.AsArray().Count);

        var suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit }));
        var raum = suche["raeume"]!.AsArray().First(r => r!["belegt"]!.AsArray().Count == 0)!["kurz"]!.GetValue<string>();
        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "raum", titel = "AG Robotik",
            termine = suche["termine"]!.AsArray().Select(t => new { datum = t!["termin"]!["datum"]!.GetValue<string>(), stundeVon = 5, stundeBis = 6, raum }).ToList(),
        }));
        var liste = antwort["buchungen"]!.AsArray();
        Assert.Equal(3, liste.Count);
        var serie = antwort["serie"]!.GetValue<string>();
        Assert.NotEqual("", serie);
        Assert.All(liste, b => Assert.Equal(serie, b!["serie"]!.GetValue<string>()));

        // einzelnen Termin stornieren
        var id = liste[1]!["id"]!.GetValue<int>();
        var st = await Testserver.JsonAsync(await lan.DeleteAsync($"/api/app/buchen/{id}"));
        Assert.Equal(1, st["storniert"]!.GetValue<int>());
        var meine = await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-01&bis=2026-12-31");
        Assert.Equal(2, meine.AsArray().Count(b => b!["status"]!.GetValue<string>() == "aktiv"));
        // Raum ist am stornierten Termin wieder frei
        var plan = await Testserver.HolenAsync(lan, $"/api/app/plan?typ=raum&element={raum}&datum=2026-11-05");
        Assert.DoesNotContain(Karten(plan, "2026-11-05", 5), k => k["text"]!.GetValue<string>() == "AG Robotik");
        // fremde Lehrkraft darf nicht stornieren
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.Equal(HttpStatusCode.Forbidden, (await sne.DeleteAsync($"/api/app/buchen/{liste[0]!["id"]!.GetValue<int>()}")).StatusCode);
        // ganze Serie (kommende Termine) stornieren
        st = await Testserver.JsonAsync(await lan.DeleteAsync($"/api/app/buchen/{liste[0]!["id"]!.GetValue<int>()}?serie=true"));
        Assert.Equal(2, st["storniert"]!.GetValue<int>());
        meine = await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-01&bis=2026-12-31");
        Assert.All(meine.AsArray(), b => Assert.Equal("storniert", b!["status"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Maximale_Belegung_zeigt_teilweise_belegte_Raeume_und_je_Termin_anderen_Raum()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // R108 an einem der beiden Termine belegen (Montag 12.10. ist vergangen → Freitag 16.10., 2. Stunde Unterricht)
        var zeit = new { wiederholung = "taeglich", von = "2026-10-15", bis = "2026-10-16", stundeVon = 2, stundeBis = 2 };
        var ohne = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit }));
        Assert.DoesNotContain(ohne["raeume"]!.AsArray(), r => r!["kurz"]!.GetValue<string>() == "R108");
        var mit = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit, kriterien = new { maxBelegung = 50 } }));
        var r108 = mit["raeume"]!.AsArray().Single(r => r!["kurz"]!.GetValue<string>() == "R108")!;
        Assert.Equal(50, r108["prozent"]!.GetValue<int>());
        Assert.Equal(1, r108["belegt"]!.AsArray().Single()!.GetValue<int>());
        var ersatz = mit["termine"]!.AsArray()[1]!["freieRaeume"]!.AsArray().Select(x => x!.GetValue<string>()).First(x => x != "R108");
        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "raum",
            termine = new[] { new { datum = "2026-10-15", stundeVon = 2, stundeBis = 2, raum = "R108" }, new { datum = "2026-10-16", stundeVon = 2, stundeBis = 2, raum = ersatz } },
        }));
        Assert.Equal(new[] { "R108", ersatz }, antwort["buchungen"]!.AsArray().Select(b => b!["raum"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Kriterien_Ressourcen_und_Raumdaten_filtern_die_Raumsuche()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // nur die Administration verwaltet Ressourcen
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync("/api/app/buchen/ressourcenarten", new { name = "Beamer" })).StatusCode);
        var art = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcenarten", new { name = "Beamer" }));
        var artId = art["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/buchen/ressourcenarten", new { name = "beamer" })).StatusCode);
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Beamer fest", artId, raum = "EDV1" }));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/buchen/raumdaten/EDV1", new { kapazitaet = 28, gebaeude = "Haus A", abteilung = "Informatik", raumgruppe = "Computerräume" }));

        var suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = Zeit("2026-10-15", 1, 1), kriterien = new { ressourcenarten = new[] { artId } } }));
        Assert.Equal("EDV1", suche["raeume"]!.AsArray().Single()!["kurz"]!.GetValue<string>());
        Assert.Equal(28, suche["raeume"]!.AsArray().Single()!["kapazitaet"]!.GetValue<int>());
        suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = Zeit("2026-10-15", 1, 1), kriterien = new { gebaeude = "Haus A", kapazitaetMin = 25 } }));
        Assert.Equal("EDV1", suche["raeume"]!.AsArray().Single()!["kurz"]!.GetValue<string>());
        suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/raeume", new { zeit = Zeit("2026-10-15", 1, 1), kriterien = new { kapazitaetMin = 50 } }));
        Assert.All(suche["raeume"]!.AsArray(), r => Assert.Equal("Sporthalle", r!["raumart"]!.GetValue<string>()));
        var opt = await Testserver.HolenAsync(lan, "/api/app/buchen/optionen");
        Assert.Contains("Haus A", opt["gebaeude"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("Beamer fest", opt["raeume"]!.AsArray().Single(r => r!["kurz"]!.GetValue<string>() == "EDV1")!["ressourcen"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Mobile_Ressource_nach_Anzahl_buchen_und_Uebersicht()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var wagen = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Laptopwagen", anzahl = 2 }));
        var id = wagen["id"]!.GetValue<int>();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/ressourcen", new { zeit = Zeit("2026-10-15", 3, 4) }));
        Assert.Equal(2, suche["ressourcen"]!.AsArray().Single()!["frei"]!.GetValue<int>());

        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("ressource", "2026-10-15", 3, 4, null, new { ressourcen = new[] { new { id, menge = 1 } } })));
        // noch einer frei – zwei nicht mehr
        var r = await sne.PostAsJsonAsync("/api/app/buchen", Buchung("ressource", "2026-10-15", 4, 4, null, new { ressourcen = new[] { new { id, menge = 2 } } }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        await Testserver.JsonAsync(await sne.PostAsJsonAsync("/api/app/buchen", Buchung("ressource", "2026-10-15", 4, 4, null, new { ressourcen = new[] { new { id, menge = 1 } } })));
        suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/ressourcen", new { zeit = Zeit("2026-10-15", 4, 4) }));
        Assert.Empty(suche["ressourcen"]!.AsArray());

        var uebersicht = await Testserver.HolenAsync(lan, "/api/app/buchen/ressourcenuebersicht?datum=2026-10-15");
        Assert.Equal(2, uebersicht["ressourcen"]!.AsArray().Single()!["belegung"]!["2026-10-15"]!.AsArray().Count);
        // Ressourcenbuchung ohne Raum erscheint nicht im Plan und wird nicht an UntPlan übertragen
        var rm = await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen");
        Assert.DoesNotContain(rm.AsArray(), x => x!["id"]!.GetValue<string>().StartsWith("buchung-"));
        Assert.Single((await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-15&bis=2026-10-15")).AsArray());
    }

    [Fact]
    public async Task Aktivitaet_ohne_Pflichtangaben_als_Veranstaltung_mit_Benachrichtigung()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        // ganz ohne Klassen, Fach und Raum
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("aktivitaet", "2026-10-15", 9, 9, null, new { lehrer = "" })));
        // mit Lehrkraft SNE, Klasse, Raum (per Uhrzeit), Benachrichtigung
        var a = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("aktivitaet", "2026-10-15", 7, 8, "R109",
            new { lehrer = "SNE", klassen = new[] { "5a" }, fach = "D", titel = "Förderunterricht", benachrichtigen = true, beginn = "13:30", ende = "15:00" })));
        Assert.Empty(a["hinweise"]!.AsArray());
        Assert.Equal("13:30", a["buchungen"]![0]!["beginn"]!.GetValue<string>());
        var plan = await Testserver.HolenAsync(sne, "/api/app/plan?typ=lehrer&element=SNE&datum=2026-10-15");
        var k = Karten(plan, "2026-10-15", 7).Single(x => x["text"]!.GetValue<string>() == "Förderunterricht");
        Assert.Equal("veranstaltung", k["status"]!.GetValue<string>());
        Assert.Null(k["vertretung"]?.GetValue<int?>());
        var post = await Testserver.HolenAsync(sne, "/api/app/mitteilungen");
        Assert.Contains(post.AsArray(), m => m!["betreff"]!.GetValue<string>() == "Neue Aktivität: Förderunterricht");
        // auch im Klassenplan der beteiligten Klasse
        var klasse = await Testserver.HolenAsync(sne, "/api/app/plan?typ=klasse&element=5a&datum=2026-10-15");
        Assert.Contains(Karten(klasse, "2026-10-15", 7), x => x["text"]!.GetValue<string>() == "Förderunterricht" && x["status"]!.GetValue<string>() == "veranstaltung");
        // Hinweis bei Überschneidung mit Unterricht der Lehrkraft (kein Hindernis)
        var h = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/hinweise", new { termine = new[] { new { datum = "2026-10-12", stundeVon = 1, stundeBis = 1 } }, lehrer = "LAN" }));
        Assert.Single(h.AsArray());
    }

    [Fact]
    public async Task Buchungen_gehen_als_Rueckmeldung_raumbuchung_an_UntPlan_und_werden_quittiert()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var b = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-15", 3, 4, "R108", new { klassen = new[] { "7a" }, titel = "Projektarbeit", text = "Gruppen" })));
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("aktivitaet", "2026-10-15", 9, 9, null, new { fach = "D", titel = "Förderkurs" })));
        var sync = s.SyncClient();
        var rm = (await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray().Where(x => x!["id"]!.GetValue<string>().StartsWith("buchung-")).ToList();
        Assert.Equal(2, rm.Count);
        var raum = rm.Single(x => x!["raum"] != null)!;
        Assert.Equal("raumbuchung", raum["typ"]!.GetValue<string>());
        Assert.Equal("2026-10-15", raum["von"]!.GetValue<string>());
        Assert.Equal(3, raum["stundeVon"]!.GetValue<int>());
        Assert.Equal("LAN", raum["lehrer"]!.GetValue<string>());
        Assert.Equal("7a", raum["klassen"]![0]!.GetValue<string>());
        Assert.Equal("Projektarbeit", raum["titel"]!.GetValue<string>());
        var akt = rm.Single(x => x!["raum"] == null)!;
        Assert.Equal("D", akt["fach"]!.GetValue<string>());
        // Vertrag: wie jede Rückmeldung lesbar
        var vertrag = System.Text.Json.JsonSerializer.Deserialize<List<UntPlan.Web.Contracts.WebRueckmeldung>>(
            await (await sync.GetAsync($"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).Content.ReadAsStringAsync(), UntPlan.Web.Contracts.UntPlanWebSchema.Json)!;
        Assert.Contains(vertrag, m => m.Typ == UntPlan.Web.Contracts.WebRueckmeldungsTypen.Raumbuchung && m.Raum == "R108" && m.Id.StartsWith("buchung-"));

        var q = await Testserver.JsonAsync(await sync.PostAsJsonAsync($"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen/quittieren", rm.Select(x => x!["id"]!.GetValue<string>()).ToArray()));
        Assert.Equal(2, q["quittiert"]!.GetValue<int>());
        Assert.DoesNotContain((await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray(), x => x!["id"]!.GetValue<string>().StartsWith("buchung-"));
        var liste = await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-15&bis=2026-10-15");
        Assert.All(liste.AsArray(), x => Assert.NotNull(x!["uebertragen"]?.GetValue<string>()));
        // übertragene Buchung bleibt sichtbar, solange UntPlan sie nicht im Paket zurückliefert
        var plan = await Testserver.HolenAsync(lan, "/api/app/plan?typ=raum&element=R108&datum=2026-10-15");
        Assert.Contains(Karten(plan, "2026-10-15", 3), k => k["text"]!.GetValue<string>() == "Projektarbeit");
        Assert.Equal(b["buchungen"]![0]!["id"]!.GetValue<int>(), liste.AsArray().First()!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Neuer_Unterricht_mit_zusaetzlichen_Stunden()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var u = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/unterricht", new { fach = "D", klassen = new[] { "7a" }, gruppe = "Förderkurs", wochenstunden = 1 }));
        Assert.Equal("LAN", u["lehrer"]!.GetValue<string>());
        var id = u["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PostAsJsonAsync("/api/app/buchen/unterricht", new { fach = "XYZ" })).StatusCode);

        var b = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("unterricht", "2026-10-15", 8, 8, "R109", new { unterrichtId = id })));
        var x = b["buchungen"]![0]!;
        Assert.Equal("D", x["fach"]!.GetValue<string>());
        Assert.Equal("Förderkurs", x["titel"]!.GetValue<string>());
        Assert.Equal("7a", x["klassen"]![0]!.GetValue<string>());
        var liste = await Testserver.HolenAsync(lan, "/api/app/buchen/unterricht");
        Assert.Equal(1, liste.AsArray().Single()!["gebuchteStunden"]!.GetValue<int>());
        var plan = await Testserver.HolenAsync(lan, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-15");
        var k = Karten(plan, "2026-10-15", 8).Single(k => k["text"]!.GetValue<string>() == "Förderkurs");
        Assert.Equal("buchung", k["status"]!.GetValue<string>());
        Assert.Equal("D", k["fach"]!.GetValue<string>());
        // fremde Lehrkraft darf für diesen Unterricht keine Stunden buchen
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.Equal(HttpStatusCode.Forbidden, (await sne.PostAsJsonAsync("/api/app/buchen", Buchung("unterricht", "2026-10-15", 9, 9, null, new { unterrichtId = id }))).StatusCode);
        // Löschen storniert die kommenden Stunden
        Assert.Equal(HttpStatusCode.OK, (await lan.DeleteAsync($"/api/app/buchen/unterricht/{id}")).StatusCode);
        Assert.Equal("storniert", (await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-15&bis=2026-10-15")).AsArray().Single()!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Raumuebersicht_zeigt_Unterricht_Buchungen_und_freie_Stunden()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung("raum", "2026-10-15", 3, 3, "R109", new { titel = "Elternsprechtag" })));
        var u = await Testserver.HolenAsync(lan, "/api/app/buchen/raumuebersicht?datum=2026-10-15&tage=2");
        Assert.Equal(2, u["tage"]!.AsArray().Count);
        var r109 = u["raeume"]!.AsArray().Single(r => r!["kurz"]!.GetValue<string>() == "R109")!;
        var e = r109["belegung"]!["2026-10-15"]!.AsArray().Single(x => x!["stunde"]!.GetValue<int>() == 3)!;
        Assert.Equal("buchung", e["art"]!.GetValue<string>());
        Assert.NotNull(e["buchung"]?.GetValue<int?>());
        var r108 = u["raeume"]!.AsArray().Single(r => r!["kurz"]!.GetValue<string>() == "R108")!;
        Assert.Contains(r108["belegung"]!["2026-10-16"]!.AsArray(), x => x!["stunde"]!.GetValue<int>() == 2 && x["art"]!.GetValue<string>() == "unterricht");
        // nicht buchbare Räume fehlen in der Übersicht
        Assert.DoesNotContain(u["raeume"]!.AsArray(), r => r!["kurz"]!.GetValue<string>() == "R101");
    }
}
