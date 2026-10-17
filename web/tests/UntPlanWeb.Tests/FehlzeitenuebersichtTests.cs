using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fehlzeitenübersicht für Schüler*innen und Erziehungsberechtigte (/api/app/familie/abwesenheiten und /fehlzeiten): Summen über mehrere
/// Wochen (Fehltage, Fehlstunden je Status, Verspätungen, offene Abwesenheiten) unabhängig vom Statusfilter, teilweise von Fachlehrkräften
/// entschuldigte Abwesenheiten, Kinderwahl bei mehreren Kindern, lange Zeiträume (App-Filter „Alles“) und der Export mit Statusfilter.
/// </summary>
public class FehlzeitenuebersichtTests
{
    private static readonly DateOnly Krank1 = new(2026, 10, 5), Verspaetet = new(2026, 10, 7), Krank2 = new(2026, 10, 12), Geschwaenzt = new(2026, 9, 28);

    private static async Task<HttpClient> KontoAsync(Testserver s, HttpClient admin, string login, string rolle, params int[] schueler)
    {
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "familie-passwort", rolle, anzeigename = login, gesperrt = false, schuelerIds = schueler }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "familie-passwort" })).StatusCode);
        return c;
    }

    /// <summary>Unterrichtsstunden der Klasse an einem Tag laut Plan (wie die Abwesenheitserfassung).</summary>
    private static List<int> Stunden(Testserver s, DateOnly tag, string klasse)
    {
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        return s.Dienst<Plandienst>().Karten(stand, tag, Elementtyp.Klasse, klasse).Where(x => x.Unterricht.HasValue && x.Status is not ("entfall" or "veranstaltung"))
            .SelectMany(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1)).Distinct().OrderBy(x => x).ToList();
    }

    private static int Z(JsonNode n, string f) => n[f]!.GetValue<int>();

    [Fact]
    public async Task Summen_ueber_mehrere_Wochen_mit_Teilentschuldigung_Statusfilter_und_Kinderwahl()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var sch = stand.Schueler.First(x => x.Klasse == "7a");
        var geschwister = stand.Schueler.First(x => x.Klasse != "7a");
        var fremd = stand.Schueler.Where(x => x.Klasse == "7a").Skip(1).First();
        var admin = await s.AlsAsync("admin");
        var kl = await s.AlsAsync("leitung", "leitung", "");
        async Task Erfassen(DateOnly tag, string von, string bis, string status, string grund, string text, int? schueler = null) =>
            await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
            { schuelerIds = new[] { schueler ?? sch.Id }, von = $"{tag:yyyy-MM-dd}T{von}", bis = $"{tag:yyyy-MM-dd}T{bis}", grund, status, text }));

        var tag1 = Stunden(s, Krank1, "7a");
        var tag2 = Stunden(s, Krank2, "7a");
        var tagV = Stunden(s, Verspaetet, "7a");
        var tagG = Stunden(s, Geschwaenzt, "7a");
        Assert.True(tag1.Count > 2 && tag2.Count > 2 && tagV.Count > 0 && tagG.Count >= 2, "Unterricht an den Testtagen erwartet");
        var (vBeginn, _) = Klassenbuchdienst.StundenZeit(stand, tagV[0]);
        var (gBeginn, _) = Klassenbuchdienst.StundenZeit(stand, tagG[0]);
        var (_, gEnde) = Klassenbuchdienst.StundenZeit(stand, tagG[1]);

        await Erfassen(Krank1, "07:00", "21:00", "entschuldigt", "K", "Grippe");
        await Erfassen(Verspaetet, $"{vBeginn:HH:mm}", $"{vBeginn.AddMinutes(12):HH:mm}", "offen", "", "Bus verpasst");
        await Erfassen(Krank2, "07:00", "21:00", "offen", "K", "Attest folgt");
        await Erfassen(Geschwaenzt, $"{gBeginn:HH:mm}", $"{gEnde:HH:mm}", "unentschuldigt", "", "");
        await Erfassen(Krank1, "07:00", "21:00", "offen", "K", "", geschwister.Id);

        // Fachlehrkraft entschuldigt ihre Stunde(n) am 12.10. → Abwesenheit „teilweise“ (Status offen, gemischt)
        var e = (await Testserver.HolenAsync(admin, "/api/app/klassenbuch/einstellungen"))["einstellungen"]!.AsObject();
        e["fachlehrerEntschuldigungen"] = true;
        await Testserver.JsonAsync(await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e)));
        var kl7a = stand.Paket.Elemente.Klassen.First(k => k.Kurz == "7a").Klassenleitung;
        var karte = s.Dienst<Plandienst>().Karten(stand, Krank2, Elementtyp.Klasse, "7a")
            .First(k => k.Unterricht.HasValue && k.Status == "regulaer" && k.Lehrer.Count == 1 && !kl7a.Contains(k.Lehrer[0].K));
        var fl = await s.AlsAsync(karte.Lehrer[0].K.ToLowerInvariant(), "lehrer", karte.Lehrer[0].K);
        var eigene = (await Testserver.HolenAsync(fl, $"/api/app/entschuldigungen/fehlzeiten?von={Krank2:yyyy-MM-dd}&bis={Krank2:yyyy-MM-dd}"))["zeilen"]!.AsArray()
            .Where(z => Z(z!, "schuelerId") == sch.Id).Select(z => Z(z!, "id")).ToList();
        Assert.NotEmpty(eigene);
        await Testserver.JsonAsync(await fl.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = eigene, status = "entschuldigt", text = "Attest liegt vor", datum = "2026-10-14" }));

        var eltern = await KontoAsync(s, admin, "eltern-fz", "eltern", sch.Id, geschwister.Id);
        var schueler = await KontoAsync(s, admin, "schueler-fz", "schueler", sch.Id);
        // Elternkonten ohne Angabe: erstes zugeordnetes Kind – hier ausdrücklich gewählt
        var zeitraum = $"von=2026-09-01&bis=2026-10-31&schueler={sch.Id}";

        foreach (var c in new[] { schueler, eltern })
        {
            var d = await Testserver.HolenAsync(c, $"/api/app/familie/abwesenheiten?{zeitraum}");
            var su = d["summen"]!;
            Assert.Equal(4, Z(su, "abwesenheiten"));
            Assert.Equal(2, Z(su, "fehltage"));
            Assert.Equal(tag1.Count + tag2.Count + 2, Z(su, "fehlstunden"));
            Assert.Equal(tag1.Count + eigene.Count, Z(su, "entschuldigt"));
            Assert.Equal(2, Z(su, "unentschuldigt"));
            Assert.Equal(tag2.Count - eigene.Count, Z(su, "offen"));
            Assert.Equal((1, 12), (Z(su, "verspaetet"), Z(su, "verspaetetMinuten")));
            // offen: die Verspätung und die teilweise entschuldigte Abwesenheit
            Assert.Equal(2, Z(su, "offeneAbwesenheiten"));

            // Liste nach Datum (neueste zuerst) mit Fehlstunden, Status und Teilentschuldigung
            var zeilen = d["zeilen"]!.AsArray().Select(x => x!).ToList();
            Assert.Equal(new[] { "2026-10-12", "2026-10-07", "2026-10-05", "2026-09-28" }, zeilen.Select(z => z["vonDatum"]!.GetValue<string>()).ToArray());
            var teil = zeilen[0];
            Assert.True(teil["statusGemischt"]!.GetValue<bool>());
            Assert.Equal("offen", teil["status"]!.GetValue<string>());
            Assert.Equal((tag2.Count, eigene.Count), (Z(teil, "fehlstunden"), Z(teil, "entschuldigt")));
            Assert.Equal(("verspaetet", 12, 0), (zeilen[1]["art"]!.GetValue<string>(), Z(zeilen[1], "minuten"), Z(zeilen[1], "fehlstunden")));
            Assert.Equal(("entschuldigt", "Grippe"), (zeilen[2]["status"]!.GetValue<string>(), zeilen[2]["text"]!.GetValue<string>()));
            Assert.Equal(("unentschuldigt", 2), (zeilen[3]["status"]!.GetValue<string>(), Z(zeilen[3], "fehlstunden")));

            // Statusfilter: weniger Zeilen, gleiche Summen (Übersicht bleibt stabil)
            var nur = await Testserver.HolenAsync(c, $"/api/app/familie/abwesenheiten?{zeitraum}&status=unentschuldigt");
            Assert.Equal("2026-09-28", Assert.Single(nur["zeilen"]!.AsArray())!["vonDatum"]!.GetValue<string>());
            Assert.Equal(Z(su, "fehlstunden"), Z(nur["summen"]!, "fehlstunden"));

            // Fehlzeiten je Stunde: dieselben Summen, Status je Stunde (Fachlehrer-Entschuldigung)
            var fz = await Testserver.HolenAsync(c, $"/api/app/familie/fehlzeiten?{zeitraum}");
            Assert.Equal(su.ToJsonString(), fz["summen"]!.ToJsonString());
            Assert.All(fz["zeilen"]!.AsArray().Where(z => eigene.Contains(Z(z!, "id"))), z => Assert.Equal("entschuldigt", z!["status"]!.GetValue<string>()));
            Assert.Equal(tag2.Count - eigene.Count, (await Testserver.HolenAsync(c, $"/api/app/familie/fehlzeiten?{zeitraum}&status=offen"))["zeilen"]!.AsArray()
                .Count(z => z!["datum"]!.GetValue<string>() == "2026-10-12"));

            // Übersicht: offene Abwesenheiten (Verspätung und teilweise entschuldigte)
            Assert.Equal(2, (await Testserver.HolenAsync(c, $"/api/app/familie/uebersicht?schueler={sch.Id}"))["offeneAbwesenheiten"]!.AsArray().Count);
        }

        // sehr langer Zeitraum (App: „Alles“): früher wurde der Beginn verschoben und die Liste war leer
        Assert.Equal(4, (await Testserver.HolenAsync(schueler, "/api/app/familie/abwesenheiten?von=2000-01-01&bis=2099-12-31"))["zeilen"]!.AsArray().Count);
        Assert.Equal(4, (await Testserver.HolenAsync(schueler, "/api/app/familie/abwesenheiten?von=2025-08-01&bis=2027-07-31"))["zeilen"]!.AsArray().Count);

        // Kinderwahl: zweites Kind mit eigenen Summen; fremde Kinder und Schülerkonto für Geschwister verboten
        var g = await Testserver.HolenAsync(eltern, $"/api/app/familie/abwesenheiten?von=2026-09-01&bis=2026-10-31&schueler={geschwister.Id}");
        Assert.Equal(geschwister.Id, Z(g["schueler"]!, "id"));
        Assert.Single(g["zeilen"]!.AsArray());
        Assert.Equal((0, Z(g["summen"]!, "fehlstunden")), (Z(g["summen"]!, "entschuldigt"), Z(g["summen"]!, "offen")));
        Assert.Equal(HttpStatusCode.Forbidden, (await eltern.GetAsync($"/api/app/familie/abwesenheiten?schueler={fremd.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync($"/api/app/familie/fehlzeiten?schueler={geschwister.Id}")).StatusCode);

        // Export mit demselben Statusfilter wie die Liste
        var csv = await eltern.GetStringAsync($"/api/app/familie/export?liste=abwesenheiten&format=csv&{zeitraum}&status=entschuldigt");
        Assert.Contains("Grippe", csv);
        Assert.DoesNotContain("Bus verpasst", csv);
        Assert.Contains("Bus verpasst", await eltern.GetStringAsync($"/api/app/familie/export?liste=abwesenheiten&format=csv&{zeitraum}"));
    }

    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public void Oberflaeche_ohne_Summen_mit_Zeitraum_Details_und_Sprechtag_fuer_Schueler()
    {
        var familie = Datei("js", "ansichten", "familie.js");
        // keine Summen-/Statistik-Box oben („bis“-Datum in der URL, aufklappbare Stunden und „teilweise“ bei
        // Fachlehrer-Entschuldigungen bleiben; der Server liefert summen weiterhin, angezeigt werden sie nicht)
        Assert.DoesNotContain("summenLeiste", familie);
        Assert.DoesNotContain("fm-summe", familie);
        Assert.DoesNotContain("daten.summen", familie);
        Assert.Contains("data-bis", familie);
        Assert.Contains("#/abwesenheiten/${r}/${v}/${s || '-'}${b ? `/${b}` : ''}", familie);
        Assert.Contains("stundenDetails(", familie);
        Assert.Contains("statusGemischt", familie);
        Assert.Contains("[reiterParam, vonParam, bisParam, klasseParam]", Datei("js", "ansichten", "abwesenheiten.js"));
        // Übersicht: offene Abwesenheit heißt „offen“ (nicht „nicht entschuldigt“), Verspätung als solche
        var ueb = Datei("js", "ansichten", "uebersicht.js");
        Assert.DoesNotContain("'nicht entschuldigt' : e(z.status)", ueb);
        Assert.Contains("'Verspätet'", ueb);
        Assert.DoesNotContain(".fm-summe", Datei("css", "app.css"));
        // Sprechtag: Formular mit „sichtbar für Schüler*innen“ an, Menüpunkt auch für Schüler*innen
        var st = Datei("js", "ansichten", "sprechtag.js");
        Assert.Contains("sichtbarSchueler: true, ankuendigen: true", st);
        Assert.Contains("o.sichtbarSchueler ?? true", st);
        Assert.Contains("b.rolle === 'eltern' || b.rolle === 'schueler') m.push({ id: 'sprechtag'", Datei("js", "app.js"));
    }

    [Fact]
    public async Task Ganztaegig_erfasst_alle_Stunden_der_Tage_mit_Fehltagen()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var schueler = stand.Schueler.Where(x => x.Klasse == "7a").Take(2).ToList();
        var kl = await s.AlsAsync("leitung", "leitung", "");
        var montag = Stunden(s, Krank2, "7a");
        var dienstag = Stunden(s, Krank2.AddDays(2), "7a"); // Mittwoch; Dienstag ohne Unterricht der 7a im Testplan
        Assert.True(montag.Count > 1 && dienstag.Count > 0 && Stunden(s, Krank2.AddDays(1), "7a").Count == 0);

        // Klassenleitung: Uhrzeiten mitten am Tag, aber „ganztägig“ Mo–Mi → alle Stunden der Tage mit Unterricht, zwei Fehltage
        var r = await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
            { schuelerIds = new[] { schueler[0].Id }, von = "2026-10-12T10:00", bis = "2026-10-14T10:30", grund = "K", status = "offen", text = "krank gemeldet", ganztaegig = true }));
        Assert.True(Z(r, "stunden") >= montag.Count + dienstag.Count);
        var fz = (await Testserver.HolenAsync(kl, $"/api/app/abwesenheiten/fehlzeiten?klasse=7a&von=2026-10-12&bis=2026-10-14&schueler={schueler[0].Id}")).AsArray();
        Assert.Equal(montag.Count + dienstag.Count, fz.Count);
        Assert.All(fz, z => Assert.True(z!["zaehlt"]!.GetValue<bool>()));
        Assert.Equal(2, fz.Sum(z => Z(z!, "fehltage")));
        var zr = Assert.Single((await Testserver.HolenAsync(kl, "/api/app/abwesenheiten/zeitraeume?klasse=7a&von=2026-10-12&bis=2026-10-14")).AsArray()
            .Where(z => Z(z!, "schuelerId") == schueler[0].Id))!;
        Assert.Equal(("fehlt", "2026-10-12", "2026-10-14"), (zr["art"]!.GetValue<string>(), zr["vonDatum"]!.GetValue<string>(), zr["bisDatum"]!.GetValue<string>()));

        // ohne „ganztägig“ bleibt es minutengenau (10:00–10:30 am Montag: weniger Stunden)
        var teil = await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
            { schuelerIds = new[] { schueler[1].Id }, von = "2026-10-12T10:00", bis = "2026-10-12T10:30", grund = "K", status = "offen" }));
        Assert.True(Z(teil, "stunden") < montag.Count);

        // Lehrkraft in ihrer Stunde: ganztägig für den Tag der Stunde erlaubt, über mehrere Tage nicht
        var karte = s.Dienst<Plandienst>().Karten(stand, Krank2, Elementtyp.Klasse, "7a").First(k => k.Unterricht.HasValue && k.Status == "regulaer" && k.Lehrer.Count == 1 && k.Lehrer[0].K != "LAN");
        var lk = await s.AlsAsync(karte.Lehrer[0].K.ToLowerInvariant(), "lehrer", karte.Lehrer[0].K);
        object Lk(string bis) => new { schuelerIds = new[] { schueler[1].Id }, von = "2026-10-12T08:00", bis, grund = "K", ganztaegig = true, datum = "2026-10-12", stunde = karte.Von, unterricht = karte.Unterricht };
        Assert.Equal(HttpStatusCode.Forbidden, (await lk.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", Lk("2026-10-13T08:00"))).StatusCode);
        await Testserver.JsonAsync(await lk.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", Lk("2026-10-12T09:00")));
        var tag = (await Testserver.HolenAsync(kl, $"/api/app/abwesenheiten/fehlzeiten?klasse=7a&von=2026-10-12&bis=2026-10-12&schueler={schueler[1].Id}")).AsArray();
        Assert.Equal(montag.Count, tag.Count);
        Assert.Equal(1, tag.Sum(z => Z(z!, "fehltage")));
    }

    [Fact]
    public void Ganztaegig_Zeitraum_und_Oberflaeche()
    {
        Assert.Equal((new DateTime(2026, 10, 12), new DateTime(2026, 10, 13, 23, 59, 0)), Klassenbuchdienst.Ganztaegig(new DateTime(2026, 10, 12, 10, 0, 0), new DateTime(2026, 10, 13, 8, 0, 0)));
        Assert.Equal((new DateTime(2026, 10, 12), new DateTime(2026, 10, 12, 23, 59, 0)), Klassenbuchdienst.Ganztaegig(new DateTime(2026, 10, 12, 10, 0, 0), new DateTime(2026, 10, 11)));
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("data-ab-ganz", sd);
        Assert.Contains("ganztaegig: q('[data-ab-ganz]').checked || null", sd);
        var abw = Datei("js", "ansichten", "abwesenheiten.js");
        Assert.Contains("name=\"ganztaegig\"", abw);
        Assert.Contains("beimOeffnen: ganzBinden", abw);
        Assert.Contains("ganztaegig: ganz || null", abw);
    }

    [Fact]
    public void Summen_aus_Zeilen_und_Zeitraeumen()
    {
        static Fehlzeile F(int id, string datum, string art, string status, int fehltage = 0, int minuten = 45) =>
            new(id, 1, "A", "7a", datum, id, "08:00", "08:45", "08:00", "08:45", "D", "LAN", fehltage, art == "fehlt" ? 1 : 0, minuten, art == "fehlt", art, "K", status, "", "");
        static Abwesenheitszeitraum A(string art, int minuten, string status) =>
            new("1:1", 1, "A", "7a", "2026-10-01", "08:00", "2026-10-01", "08:45", art, minuten, "K", status, "", 0, 0, new(), new(), new(), false, "", "");
        var su = UntPlanWeb.Api.FamilieApi.Summen(
            new() { F(1, "2026-10-01", "fehlt", "entschuldigt", 1), F(2, "2026-10-02", "fehlt", "unentschuldigt"), F(3, "2026-10-02", "fehlt", "offen"), F(4, "2026-10-03", "verspaetet", "offen", 0, 10) },
            new() { A("fehlt", 0, "entschuldigt"), A("fehlt", 0, "offen"), A("verspaetet", 10, "offen"), A("vorzeitig", 20, "entschuldigt") });
        var j = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(su))!;
        Assert.Equal(4, Z(j, "abwesenheiten"));
        Assert.Equal(1, Z(j, "fehltage"));
        Assert.Equal(3, Z(j, "fehlstunden"));
        Assert.Equal(145, Z(j, "fehlminuten"));
        Assert.Equal((1, 1, 1), (Z(j, "entschuldigt"), Z(j, "unentschuldigt"), Z(j, "offen")));
        Assert.Equal((1, 10, 1), (Z(j, "verspaetet"), Z(j, "verspaetetMinuten"), Z(j, "vorzeitig")));
        Assert.Equal(2, Z(j, "offeneAbwesenheiten"));
    }
}
