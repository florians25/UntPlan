using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Schüler- und Elternkonten (Zuordnung zu Schüler*innen, nur eigene Daten, keine Lehrerpläne), minutengenaue Abwesenheiten im
/// Abwesenheitsdialog, Abwesenheiten als Zeiträume und Fehlzeiten je Stunde, Notizen der Stunde, Buchungen ohne Vertretung.
/// </summary>
public class FamilieTests
{
    private static readonly DateOnly Mittwoch = new(2026, 10, 14);

    private static async Task<(HttpClient C, int Id)> KontoAsync(Testserver s, HttpClient admin, string login, string rolle, params int[] schueler)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "familie-passwort", rolle, anzeigename = login, gesperrt = false, schuelerIds = schueler }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "familie-passwort" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Schueler_und_Eltern_sehen_nur_eigene_Daten_und_keine_Lehrerplaene()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var a7 = stand.Schueler.First(x => x.Klasse == "7a");
        var b7 = stand.Schueler.Where(x => x.Klasse == "7a").Skip(1).First();
        var k5 = stand.Schueler.First(x => x.Klasse != "7a");
        var admin = await s.AlsAsync("admin");
        var (schueler, _) = await KontoAsync(s, admin, "schueler-a", "schueler", a7.Id);
        var (eltern, elternId) = await KontoAsync(s, admin, "eltern-a", "eltern", a7.Id, k5.Id);

        var sitzung = await Testserver.HolenAsync(schueler, "/api/app/sitzung");
        Assert.True(sitzung["benutzer"]!["familie"]!.GetValue<bool>());
        Assert.Equal("7a", sitzung["benutzer"]!["element"]!.GetValue<string>());
        Assert.Equal("Erziehungsberechtigte*r", (await Testserver.HolenAsync(eltern, "/api/app/sitzung"))["benutzer"]!["rollenname"]!.GetValue<string>());

        // Pläne: eigene Klasse bzw. Klassen der Kinder und Räume – nie Lehrkräfte (auch nicht über die App-API)
        Assert.Equal(HttpStatusCode.OK, (await schueler.GetAsync("/api/app/plan?typ=klasse&element=7a")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync($"/api/app/plan?typ=klasse&element={k5.Klasse}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await eltern.GetAsync($"/api/app/plan?typ=klasse&element={k5.Klasse}")).StatusCode);
        foreach (var c in new[] { schueler, eltern })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/plan?typ=lehrer&element=LAN")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/tag?typ=lehrer")).StatusCode);
            Assert.Empty((await Testserver.HolenAsync(c, "/api/app/elemente"))["lehrer"]!.AsArray());
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/klassenbuch/anwesenheit?datum=2026-10-14&stunde=1&unterricht=1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/abwesenheiten/zeitraeume")).StatusCode);
        }

        // Familienseiten: nur zugeordnete Schüler*innen
        Assert.Equal(a7.Id, (await Testserver.HolenAsync(schueler, "/api/app/familie/abwesenheiten"))["schueler"]!["id"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync($"/api/app/familie/abwesenheiten?schueler={b7.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync($"/api/app/familie/fehlzeiten?schueler={k5.Id}")).StatusCode);
        Assert.Equal(2, (await Testserver.HolenAsync(eltern, "/api/app/familie/kinder")).AsArray().Count);
        Assert.Equal(k5.Id, (await Testserver.HolenAsync(eltern, $"/api/app/familie/fehlzeiten?schueler={k5.Id}"))["schueler"]!["id"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await eltern.GetAsync($"/api/app/familie/hausaufgaben?schueler={b7.Id}")).StatusCode);
        // Lehrkräfte und Klassenkonten ohne Zuordnung haben keine Familienseiten
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/familie/abwesenheiten")).StatusCode);
        var klasse = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await klasse.GetAsync("/api/app/familie/abwesenheiten")).StatusCode);

        // Zuordnung ändern: Schülerkonto genau eine Person
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/app/admin/benutzer/{elternId}", new { rolle = "schueler", anzeigename = "x", gesperrt = false, schuelerIds = new[] { a7.Id, k5.Id } })).StatusCode);
        var liste = await Testserver.HolenAsync(admin, "/api/app/admin/benutzer");
        Assert.Equal(2, liste.AsArray().Single(b => b!["login"]!.GetValue<string>() == "eltern-a")!["schuelerIds"]!.AsArray().Count);
    }

    [Fact]
    public async Task Abwesenheitsdialog_minutengenau_Verspaetung_und_vorzeitig()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var karten = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0).OrderBy(x => x.Von).ToList();
        var k = karten.First(x => karten.Any(y => y.Von > x.Bis) && x.Lehrer[0].K != "LAN");
        var c = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
        var kl = await s.AlsAsync("leitung", "leitung", "");
        var sid = stand.Schueler.First(x => x.Klasse == "7a").Id;
        var (beginn, _) = Klassenbuchdienst.StundenZeit(stand, k.Von);
        var (_, ende) = Klassenbuchdienst.StundenZeit(stand, k.Bis);
        string Url() => $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}";
        object Daten(TimeOnly von, TimeOnly bis, string? status = null) => new
        {
            schuelerIds = new[] { sid }, von = $"{k.Datum}T{von:HH:mm}", bis = $"{k.Datum}T{bis:HH:mm}", grund = "K", text = "Bus", status,
            datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht,
        };

        // Verspätung = abwesend von Stundenbeginn bis Ankunft (12 Minuten)
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", Daten(beginn, beginn.AddMinutes(12), "entschuldigt")));
        var f = (await Testserver.HolenAsync(c, Url()))["fehlzeiten"]!.AsArray().Single()!;
        Assert.Equal(("verspaetet", 12, "Bus"), (f["art"]!.GetValue<string>(), f["minuten"]!.GetValue<int>(), f["text"]!.GetValue<string>()));
        Assert.Equal(($"{beginn:HH:mm}", $"{beginn.AddMinutes(12):HH:mm}"), (f["beginn"]!.GetValue<string>(), f["ende"]!.GetValue<string>()));
        // Status darf nur die Klassenleitung setzen
        Assert.Equal("offen", f["status"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty((await Testserver.HolenAsync(c, Url()))["geprueft"]?.GetValue<string>()));
        var fz = (await Testserver.HolenAsync(kl, $"/api/app/abwesenheiten/fehlzeiten?klasse=7a&von={k.Datum}&bis={k.Datum}&schueler={sid}")).AsArray();
        var zeile = Assert.Single(fz)!;
        Assert.Equal((12, 0, false, 0), (zeile["fehlminuten"]!.GetValue<int>(), zeile["fehlstunden"]!.GetValue<int>(), zeile["zaehlt"]!.GetValue<bool>(), zeile["fehltage"]!.GetValue<int>()));

        // erneut speichern ersetzt (kein zweiter Eintrag): vorzeitig gegangen 10 Minuten vor Ende, für den Rest des Tages darf nur die Klassenleitung
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { sid }, von = $"{k.Datum}T{ende.AddMinutes(-10):HH:mm}", bis = $"{Mittwoch.AddDays(1):yyyy-MM-dd}T23:00", datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht,
        })).StatusCode);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", Daten(ende.AddMinutes(-10), new TimeOnly(23, 0))));
        var zr = (await Testserver.HolenAsync(kl, $"/api/app/abwesenheiten/zeitraeume?klasse=7a&von={k.Datum}&bis={k.Datum}")).AsArray()
            .Where(z => z!["schuelerId"]!.GetValue<int>() == sid).ToList();
        var abw = Assert.Single(zr)!;
        Assert.Equal(("vorzeitig", 10, $"{ende.AddMinutes(-10):HH:mm}"), (abw["art"]!.GetValue<string>(), abw["minuten"]!.GetValue<int>(), abw["vonZeit"]!.GetValue<string>()));
        Assert.True(abw["ids"]!.AsArray().Count > 1);
        Assert.Equal(abw["ids"]!.AsArray().Count - 1, abw["fehlstunden"]!.GetValue<int>());

        // Anwesend: Abwesenheit samt „Rest des Tages“ entfernen
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/loeschen", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { sid } }));
        Assert.Empty((await Testserver.HolenAsync(kl, $"/api/app/abwesenheiten/fehlzeiten?klasse=7a&von={k.Datum}&bis={k.Datum}&schueler={sid}")).AsArray());

        // fremde Lehrkraft darf nicht
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", Daten(beginn, beginn.AddMinutes(5)))).StatusCode);
    }

    [Fact]
    public async Task Ganzer_Tag_ergibt_Fehlzeiten_je_Stunde_mit_einem_Fehltag()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var sch = stand.Schueler.First(x => x.Klasse == "7a");
        var stunden = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status is not ("entfall" or "veranstaltung"))
            .SelectMany(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1)).Distinct().OrderBy(x => x).ToList();
        var kl = await s.AlsAsync("leitung", "leitung", "");
        var r = await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
            { schuelerIds = new[] { sch.Id }, von = "2026-10-14T07:00", bis = "2026-10-14T21:00", grund = "K", status = "entschuldigt", text = "krank" }));
        Assert.Equal(stunden.Count, r["stunden"]!.GetValue<int>());

        var admin = await s.AlsAsync("admin");
        var (eltern, _) = await KontoAsync(s, admin, "eltern-b", "eltern", sch.Id);
        var fz = (await Testserver.HolenAsync(eltern, "/api/app/familie/fehlzeiten?von=2026-10-14&bis=2026-10-14"))["zeilen"]!.AsArray();
        Assert.Equal(stunden.Count, fz.Count);
        Assert.All(fz, z => Assert.True(z!["zaehlt"]!.GetValue<bool>()));
        Assert.All(fz, z => Assert.Equal("entschuldigt", z!["status"]!.GetValue<string>()));
        Assert.Equal(1, fz.Sum(z => z!["fehltage"]!.GetValue<int>()));
        Assert.Equal(1, fz.Last()!["fehltage"]!.GetValue<int>());
        var laengen = stunden.Sum(n => { var (b, e) = Klassenbuchdienst.StundenZeit(stand, n); return (int)(e - b).TotalMinutes; });
        Assert.Equal(laengen, fz.Sum(z => z!["fehlminuten"]!.GetValue<int>()));
        Assert.All(fz, z => Assert.False(string.IsNullOrEmpty(z!["lehrer"]!.GetValue<string>())));

        // eine Abwesenheit (Zeitraum), keine interne Notiz für Familien
        var abw = (await Testserver.HolenAsync(eltern, "/api/app/familie/abwesenheiten?von=2026-10-01&bis=2026-10-31"))["zeilen"]!.AsArray();
        var z1 = Assert.Single(abw)!;
        Assert.Equal(("2026-10-14", "2026-10-14", "entschuldigt", "krank"), (z1["vonDatum"]!.GetValue<string>(), z1["bisDatum"]!.GetValue<string>(), z1["status"]!.GetValue<string>(), z1["text"]!.GetValue<string>()));
        Assert.Equal("", z1["notizIntern"]!.GetValue<string>());

        // Export und Entschuldigungsschreiben
        var csv = await eltern.GetStringAsync("/api/app/familie/export?liste=fehlzeiten&format=csv&von=2026-10-14&bis=2026-10-14");
        Assert.Contains("Fehlmin.", csv);
        var pdf = await eltern.GetByteArrayAsync("/api/app/familie/export?liste=abwesenheiten&format=pdf&von=2026-10-01&bis=2026-10-31");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var brief = await eltern.GetByteArrayAsync($"/api/app/familie/entschuldigung?von=2026-10-01&bis=2026-10-31&ids={z1["ids"]![0]}");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(brief, 0, 4));
        var ueb = await Testserver.HolenAsync(eltern, "/api/app/familie/uebersicht");
        Assert.Empty(ueb["offeneAbwesenheiten"]!.AsArray());
    }

    [Fact]
    public async Task Notizen_und_Lehrstoff_einzeln_speichern_und_fuer_Schueler_ohne_Lehrernotiz()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0);
        var c = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
        var basis = new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht };
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/stunde", new { basis.datum, basis.stunde, basis.unterricht, thema = "Bruchrechnung" }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/stunde", new { basis.datum, basis.stunde, basis.unterricht, notizSchueler = "Zirkel mitbringen" }));
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { basis.datum, basis.stunde, basis.unterricht, notiz = "Elterngespräch Müller" }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { basis.datum, basis.stunde, basis.unterricht, text = "S. 12 Nr. 3", faellig = "2026-10-21" }));
        var url = $"/api/app/klassenbuch/stunde?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}";
        var e = (await Testserver.HolenAsync(c, url))["eintrag"]!;
        Assert.Equal(("Bruchrechnung", "Zirkel mitbringen", "Elterngespräch Müller"), (e["thema"]!.GetValue<string>(), e["notizSchueler"]!.GetValue<string>(), e["notiz"]!.GetValue<string>()));

        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var es = (await Testserver.HolenAsync(schueler, url))["eintrag"]!;
        Assert.Equal(("Bruchrechnung", "Zirkel mitbringen", ""), (es["thema"]!.GetValue<string>(), es["notizSchueler"]!.GetValue<string>(), es["notiz"]!.GetValue<string>()));
        var hw = await Testserver.HolenAsync(schueler, $"/api/app/klassenbuch/hausaufgabenliste?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}&klassen=7a");
        Assert.Equal("S. 12 Nr. 3", hw["liste"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.PostAsJsonAsync("/api/app/klassenbuch/stunde", new { basis.datum, basis.stunde, basis.unterricht, notizSchueler = "x" })).StatusCode);
        // Karten im Plan: Schülernotiz ja, Lehrernotiz nie
        var p = await Testserver.HolenAsync(schueler, $"/api/app/plan?typ=klasse&element=7a&datum={k.Datum}");
        var karte = p["karten"]!.AsArray().First(x => x!["datum"]!.GetValue<string>() == k.Datum && x["von"]!.GetValue<int>() == k.Von && x["unterricht"]?.GetValue<int>() == k.Unterricht)!;
        Assert.Equal("Zirkel mitbringen", karte["notizSchueler"]!.GetValue<string>());
        Assert.True(string.IsNullOrEmpty(karte["notizLehrer"]?.GetValue<string>()));

        // „Findet online statt“ über dieselbe Schnittstelle (Feld online), sichtbar in Eintrag und Karte; Lehrstoff bleibt
        var on = await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { basis.datum, basis.stunde, basis.unterricht, online = true }));
        Assert.True(on["online"]!.GetValue<bool>());
        Assert.Equal("Bruchrechnung", on["thema"]!.GetValue<string>());
        Assert.True((await Testserver.HolenAsync(schueler, url))["eintrag"]!["online"]!.GetValue<bool>());
        p = await Testserver.HolenAsync(schueler, $"/api/app/plan?typ=klasse&element=7a&datum={k.Datum}");
        Assert.True(p["karten"]!.AsArray().First(x => x!["datum"]!.GetValue<string>() == k.Datum && x["von"]!.GetValue<int>() == k.Von && x["unterricht"]?.GetValue<int>() == k.Unterricht)!["online"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Raumbuchung_und_Klausur_erzeugen_keine_Vertretung()
    {
        var paket = Testserver.Beispielpaket();
        var tag = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-14")!;
        var stunden = tag["stunden"]!.AsArray().Where(x => x!["status"]!.GetValue<string>() == "regulaer" && x["klassen"]!.AsArray().Any(k => k!.GetValue<string>() == "7a")).Take(2).ToList();
        var vertretungen = paket["vertretungen"]!.AsArray();
        var i = 0;
        foreach (var (st, art) in stunden.Zip(new[] { "Raumbuchung", "Klausur" }))
        {
            var id = 9900 + i++;
            st!["status"] = "vertreten";
            st["vertretung"] = id;
            var raum = st["raeume"]![0]!.GetValue<string>();
            vertretungen.Add(new JsonObject
            {
                ["id"] = id, ["datum"] = "2026-10-14", ["stunde"] = st["stunde"]!.GetValue<int>(), ["art"] = art, ["klassen"] = new JsonArray("7a"),
                ["lehrer"] = st["lehrer"]![0]!.GetValue<string>(), ["vertreter"] = st["lehrer"]![0]!.GetValue<string>(), ["fach"] = st["fach"]!.GetValue<string>(),
                ["neuesFach"] = st["fach"]!.GetValue<string>(), ["raum"] = raum, ["neuerRaum"] = raum, ["vertrVon"] = "", ["nach"] = "", ["text"] = art + " Text", ["nurLehrer"] = false,
            });
        }
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("lan", "lehrer", "LAN");
        var vt = await Testserver.HolenAsync(c, "/api/app/vertretungen?datum=2026-10-14&tage=1");
        Assert.DoesNotContain(vt["tage"]![0]!["zeilen"]!.AsArray(), z => z!["art"]!.GetValue<string>() is "Raumbuchung" or "Klausur");
        var p = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-14");
        var karten = p["karten"]!.AsArray().Where(x => x!["datum"]!.GetValue<string>() == "2026-10-14").ToList();
        var buchung = karten.First(x => x!["von"]!.GetValue<int>() <= stunden[0]!["stunde"]!.GetValue<int>() && x["bis"]!.GetValue<int>() >= stunden[0]!["stunde"]!.GetValue<int>() && x["fach"]!.GetValue<string>() == stunden[0]!["fach"]!.GetValue<string>())!;
        Assert.Equal(("regulaer", "", ""), (buchung["status"]!.GetValue<string>(), buchung["art"]!.GetValue<string>(), buchung["text"]!.GetValue<string>()));
        var klausur = karten.First(x => x!["von"]!.GetValue<int>() <= stunden[1]!["stunde"]!.GetValue<int>() && x["bis"]!.GetValue<int>() >= stunden[1]!["stunde"]!.GetValue<int>() && x["fach"]!.GetValue<string>() == stunden[1]!["fach"]!.GetValue<string>())!;
        Assert.Equal("klausur", klausur["status"]!.GetValue<string>());
    }
}
