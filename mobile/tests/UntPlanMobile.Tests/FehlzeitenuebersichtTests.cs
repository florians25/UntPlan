using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// „Meine Abwesenheiten“ in der App für Schüler*innen und Erziehungsberechtigte: Summen aus UntPlanWeb (Fehltage laut Stundenplan statt
/// Kalendertagen, Verspätungen nicht als Tag), Fehlstunden je Abwesenheit, Kinderwahl bei mehreren Kindern, langer Zeitraum („Alles“)
/// und die Sprechtag-Ankündigung für Eltern und Schüler*innen (Standard an).
/// </summary>
public class FehlzeitenuebersichtTests
{
    private static string AppJs => File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));
    private static readonly DateOnly Krank = new(2026, 10, 12), Spaet = new(2026, 10, 13);

    private static async Task<HttpClient> AdminAsync(Umgebung u)
    {
        var admin = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = u.WebAdresse };
        admin.DefaultRequestHeaders.Add("X-UntPlanWeb", "1");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/app/anmelden", new { login = "admin", passwort = "admin-passwort" })).StatusCode);
        return admin;
    }

    private static async Task<JsonNode> KontoAsync(HttpClient admin, string login, string rolle, params int[] kinder) =>
        await Umgebung.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = Umgebung.Passwort, rolle, anzeigename = login, gesperrt = false, schuelerIds = kinder }));

    private static int Z(JsonNode n, string f) => n[f]!.GetValue<int>();

    [Fact]
    public async Task Summen_Fehlstunden_und_Kinderwahl_in_Meine_Abwesenheiten()
    {
        await using var u = await Umgebung.StartenAsync();
        var plan = u.WebDienste.GetRequiredService<Plandienst>();
        var stand = u.WebDienste.GetRequiredService<Paketdienst>().Aktuell!;
        var kind = stand.Schueler.First(x => x.Klasse == "5a");
        var zweites = stand.Schueler.First(x => x.Klasse != "5a");
        List<int> Stunden(DateOnly tag) => plan.Karten(stand, tag, Elementtyp.Klasse, "5a").Where(x => x.Unterricht.HasValue && x.Status is not ("entfall" or "veranstaltung"))
            .SelectMany(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1)).Distinct().OrderBy(x => x).ToList();
        var krank = Stunden(Krank);
        var spaet = Stunden(Spaet);
        Assert.True(krank.Count > 1 && spaet.Count > 0);
        var (beginn, _) = Klassenbuchdienst.StundenZeit(stand, spaet[0]);

        var admin = await AdminAsync(u);
        async Task Erfassen(int sid, DateOnly tag, string von, string bis, string status, string text) =>
            await Umgebung.JsonAsync(await admin.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
            { schuelerIds = new[] { sid }, von = $"{tag:yyyy-MM-dd}T{von}", bis = $"{tag:yyyy-MM-dd}T{bis}", grund = "K", status, text }));
        await Erfassen(kind.Id, Krank, "07:00", "21:00", "entschuldigt", "Fieber");
        await Erfassen(kind.Id, Spaet, $"{beginn:HH:mm}", $"{beginn.AddMinutes(10):HH:mm}", "offen", "Bus");
        await Erfassen(zweites.Id, Krank, "07:00", "21:00", "unentschuldigt", "");
        await KontoAsync(admin, "eltern-fz", "eltern", kind.Id, zweites.Id);
        await KontoAsync(admin, "schueler-fz", "schueler", kind.Id);

        // Schülerkonto: Startseite mit Fehltagen (nicht Kalendertagen) und Fehlstunden, Liste mit Summen und Fehlstunden je Karte
        var schueler = await u.AngemeldetAsync("schueler-fz");
        var home = (await Umgebung.HolenAsync(schueler, "/api/home?action=home"))["absence"]!;
        Assert.Equal((2, 1, krank.Count, 1, 1), (Z(home, "total"), Z(home, "days"), Z(home, "hours"), Z(home, "late"), Z(home, "open")));
        var d = await Umgebung.HolenAsync(schueler, "/api/home?action=absences");
        Assert.True(d["student"]!.GetValue<bool>());
        var su = d["summary"]!;
        Assert.Equal((1, krank.Count, krank.Count, 0, 0, 1, 10), (Z(su, "days"), Z(su, "hours"), Z(su, "excused"), Z(su, "unexcused"), Z(su, "pending"), Z(su, "late"), Z(su, "lateMinutes")));
        Assert.Single(d["children"]!.AsArray());
        var items = d["items"]!.AsArray().Select(x => x!).ToList();
        Assert.Equal(2, items.Count);
        var spaetKarte = items.First(x => x["from"]!.GetValue<string>() == $"{Spaet:yyyy-MM-dd}");
        Assert.Equal(("Verspätung (10 min)", "offen", ""), (spaetKarte["art"]!.GetValue<string>(), spaetKarte["label"]!.GetValue<string>(), spaetKarte["lessons"]!.GetValue<string>()));
        var krankKarte = items.First(x => x["from"]!.GetValue<string>() == $"{Krank:yyyy-MM-dd}");
        Assert.Equal(($"{krank.Count} Fehlstunden", "ok", "entschuldigt"), (krankKarte["lessons"]!.GetValue<string>(), krankKarte["tone"]!.GetValue<string>(), krankKarte["label"]!.GetValue<string>()));
        // langer Zeitraum („Alles“ in der App) liefert alle Einträge
        Assert.Equal(2, (await Umgebung.HolenAsync(schueler, "/api/home?action=absences&from=2000-01-01&to=2099-12-31"))["items"]!.AsArray().Count);

        // Elternkonto mit zwei Kindern: Kinderwahl, Summen je Kind; fremde Kinder werden nicht angenommen
        var eltern = await u.AngemeldetAsync("eltern-fz");
        var e = await Umgebung.HolenAsync(eltern, "/api/home?action=absences");
        Assert.Equal(2, e["children"]!.AsArray().Count);
        var erstes = Z(e, "selected");
        var anderes = e["children"]!.AsArray().Select(k => Z(k!, "id")).First(i => i != erstes);
        var e2 = await Umgebung.HolenAsync(eltern, $"/api/home?action=absences&student={anderes}");
        Assert.Equal(anderes, Z(e2, "selected"));
        var beide = new[] { e, e2 }.ToDictionary(x => Z(x, "selected"));
        Assert.Equal(2, beide[kind.Id]["items"]!.AsArray().Count);
        var z2 = Assert.Single(beide[zweites.Id]["items"]!.AsArray())!;
        Assert.Equal("nicht entschuldigt", z2["label"]!.GetValue<string>());
        Assert.Equal((1, 0), (Z(beide[zweites.Id]["summary"]!, "days"), Z(beide[zweites.Id]["summary"]!, "late")));
        var fremd = stand.Schueler.First(x => x.Id != kind.Id && x.Id != zweites.Id).Id;
        Assert.Equal(erstes, Z(await Umgebung.HolenAsync(eltern, $"/api/home?action=absences&student={fremd}"), "selected"));
    }

    [Fact]
    public async Task Sprechtag_Ankuendigung_fuer_Eltern_und_Schueler_in_der_App()
    {
        await using var u = await Umgebung.StartenAsync();
        var stand = u.WebDienste.GetRequiredService<Paketdienst>().Aktuell!;
        var kind = stand.Schueler.First(x => x.Klasse == "5a").Id;
        var admin = await AdminAsync(u);
        await KontoAsync(admin, "eltern-spt", "eltern", kind);
        await KontoAsync(admin, "schueler-spt", "schueler", kind);
        // ohne Optionen angelegt: Anmeldung erst ab 16.10. → angekündigt, für Eltern und Schüler*innen sichtbar
        var id = Z(await Umgebung.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new
        {
            name = "Elternsprechtag", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, anmeldungVon = "2026-10-16T08:00", anmeldungBis = "2026-10-20T18:00",
            sichtbar = true, raumwahl = true, lehrkraefte = new[] { new { lehrer = "FIS", raum = "" } },
        })), "id");
        foreach (var login in new[] { "eltern-spt", "schueler-spt" })
        {
            var c = await u.AngemeldetAsync(login);
            var karte = Assert.Single((await Umgebung.HolenAsync(c, "/api/sprechtag/start")).AsArray())!;
            Assert.Equal((id, "angekuendigt"), (Z(karte, "id"), karte["status"]!.GetValue<string>()));
            var home = Assert.Single((await Umgebung.HolenAsync(c, "/api/home?action=home"))["sprechtage"]!.AsArray())!;
            Assert.Equal("angekuendigt", home["status"]!.GetValue<string>());
            Assert.StartsWith("Angekündigt", home["text"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Oberflaeche_ohne_Summenbox_mit_Kinderwahl()
    {
        var js = AppJs;
        // Meine Abwesenheiten: oben keine Box mit Summen bzw. Statistik (summary liefert der Server weiter), Kind-Chips bleiben
        Assert.DoesNotContain("absSummen", js);
        Assert.DoesNotContain("abssum", js);
        Assert.Contains("function absKinder(d)", js);
        Assert.Contains("const kopf = d.student ? absKinder(d) : '';", js);
        Assert.Contains("art === 'absences' && listeStand.kind ? { student: String(listeStand.kind) }", js);
        var css = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "css", "app.css"));
        Assert.DoesNotContain(".abssum", css);
        Assert.Contains(".abskind{", css);
    }
}
