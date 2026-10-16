using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fehlzeiten und Abwesenheiten zeigen dieselben Daten: gleiche Quelle (Tabelle fehlzeiten), gleiche Zählung; Status, Zeitraum und Löschen
/// wirken sofort in beiden Bereichen. Außerdem: Klassenbuch-Übersicht (Liste) mit mehreren Kriterien und Hausaufgaben mit Id.
/// </summary>
public class KlassenbuchSyncTests
{
    private static readonly DateOnly Mittwoch = new(2026, 10, 14);
    private const string Tag = "2026-10-14";

    private static async Task<(List<int> Ids, int Fehlt, int Entschuldigt, int Unentschuldigt)> Fehlzeiten(HttpClient c, int sid)
    {
        var f = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/fehlzeiten?klasse=7a&von={Tag}&bis={Tag}");
        var zeilen = f["zeilen"]!.AsArray().Where(z => z!["schuelerId"]!.GetValue<int>() == sid).ToList();
        var fehlt = zeilen.Where(z => z!["art"]!.GetValue<string>() == "fehlt").ToList();
        var schueler = f["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == sid)!;
        Assert.Equal(fehlt.Count, schueler["stunden"]!.GetValue<int>());
        return (zeilen.Select(z => z!["id"]!.GetValue<int>()).OrderBy(x => x).ToList(), fehlt.Count,
            fehlt.Count(z => z!["status"]!.GetValue<string>() == "entschuldigt"), fehlt.Count(z => z!["status"]!.GetValue<string>() == "unentschuldigt"));
    }

    private static async Task<List<JsonNode>> Zeitraeume(HttpClient c, int sid) =>
        (await Testserver.HolenAsync(c, $"/api/app/abwesenheiten/zeitraeume?klasse=7a&von={Tag}&bis={Tag}")).AsArray()
            .Where(z => z!["schuelerId"]!.GetValue<int>() == sid).Select(z => z!).ToList();

    [Fact]
    public async Task Fehlzeiten_und_Abwesenheiten_bleiben_synchron()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var sid = stand.Schueler.First(x => x.Klasse == "7a").Id;
        var karten = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status != "entfall").OrderBy(x => x.Von).ToList();
        var beginn = Klassenbuchdienst.StundenZeit(stand, karten.First().Von).Item1;
        var ende = Klassenbuchdienst.StundenZeit(stand, karten.Last().Bis).Item2;

        // ganzer Tag abwesend → eine Abwesenheit, so viele Fehlstunden wie in den Fehlzeiten
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { sid }, von = $"{Tag}T{beginn:HH:mm}", bis = $"{Tag}T{ende:HH:mm}", grund = "", text = "krank", status = "offen",
        }));
        var zr = Assert.Single(await Zeitraeume(leitung, sid));
        var fz = await Fehlzeiten(leitung, sid);
        Assert.True(fz.Fehlt > 1);
        Assert.Equal(fz.Fehlt, zr["fehlstunden"]!.GetValue<int>());
        Assert.Equal(fz.Ids, zr["ids"]!.AsArray().Select(x => x!.GetValue<int>()).OrderBy(x => x).ToList());

        // eine Fehlstunde in den Fehlzeiten entschuldigen → Abwesenheit zeigt „teilweise“ mit derselben Zählung
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/status", new { ids = new[] { fz.Ids[0] }, status = "entschuldigt" }));
        zr = Assert.Single(await Zeitraeume(leitung, sid));
        fz = await Fehlzeiten(leitung, sid);
        Assert.True(zr["statusGemischt"]!.GetValue<bool>());
        Assert.Equal((fz.Entschuldigt, fz.Unentschuldigt), (zr["entschuldigt"]!.GetValue<int>(), zr["unentschuldigt"]!.GetValue<int>()));
        Assert.Equal(1, fz.Entschuldigt);

        // Status der ganzen Abwesenheit (Bereich Abwesenheiten) → alle Fehlstunden in den Fehlzeiten
        await Testserver.JsonAsync(await leitung.PutAsJsonAsync("/api/app/abwesenheiten", new { ids = fz.Ids, status = "unentschuldigt" }));
        zr = Assert.Single(await Zeitraeume(leitung, sid));
        fz = await Fehlzeiten(leitung, sid);
        Assert.Equal("unentschuldigt", zr["status"]!.GetValue<string>());
        Assert.False(zr["statusGemischt"]!.GetValue<bool>());
        Assert.Equal((0, fz.Fehlt), (fz.Entschuldigt, fz.Unentschuldigt));

        // Zeitraum verkürzen (nur die erste Stunde) → Fehlzeiten schrumpfen genauso
        var ersteEnde = Klassenbuchdienst.StundenZeit(stand, karten.First().Bis).Item2;
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { sid }, von = $"{Tag}T{beginn:HH:mm}", bis = $"{Tag}T{ersteEnde:HH:mm}", grund = "", text = "krank", status = "unentschuldigt",
            ersetzen = zr["ids"]!.AsArray().Select(x => x!.GetValue<int>()).ToList(),
        }));
        zr = Assert.Single(await Zeitraeume(leitung, sid));
        fz = await Fehlzeiten(leitung, sid);
        Assert.Equal(karten.First().Bis - karten.First().Von + 1, fz.Fehlt);
        Assert.Equal(fz.Fehlt, zr["fehlstunden"]!.GetValue<int>());
        Assert.Equal(fz.Ids, zr["ids"]!.AsArray().Select(x => x!.GetValue<int>()).OrderBy(x => x).ToList());

        // löschen → in beiden Bereichen weg
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten/loeschen", new { ids = fz.Ids }));
        Assert.Empty(await Zeitraeume(leitung, sid));
        var f = await Testserver.HolenAsync(leitung, $"/api/app/klassenbuch/fehlzeiten?klasse=7a&von={Tag}&bis={Tag}");
        Assert.DoesNotContain(f["zeilen"]!.AsArray(), z => z!["schuelerId"]!.GetValue<int>() == sid);

        // App-Daten werden nicht zwischengespeichert (beide Bereiche laden immer frisch)
        var antwort = await leitung.GetAsync($"/api/app/abwesenheiten/zeitraeume?klasse=7a&von={Tag}&bis={Tag}");
        Assert.True(antwort.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Uebersicht_als_Liste_mit_Kriterien_und_Hausaufgaben_mit_Id()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var k = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0).OrderBy(x => x.Von).First();
        var lehrer = k.Lehrer[0].K;

        // Zeitraum von–bis (höchstens 31 Tage), Klasse + Lehrkraft + Fach kombiniert, Grenzen des Schuljahres
        var u = await Testserver.HolenAsync(leitung, $"/api/app/klassenbuch/uebersicht?von={Tag}&bis=2026-12-31&klasse=7a&lehrer={lehrer}&fach={k.Fach}");
        Assert.Equal("2026-11-13", u["bis"]!.GetValue<string>());
        Assert.NotNull(u["schuljahrVon"]);
        var stunden = u["tage"]!.AsArray().SelectMany(t => t!["stunden"]!.AsArray()).ToList();
        Assert.NotEmpty(stunden);
        Assert.All(stunden, x =>
        {
            Assert.Equal(k.Fach, x!["fach"]!.GetValue<string>());
            Assert.Contains(lehrer, x["lehrer"]!.AsArray().Select(y => y!.GetValue<string>()));
            Assert.Contains("7a", x["klassen"]!.AsArray().Select(y => y!.GetValue<string>()));
        });
        var tage = u["tage"]!.AsArray().Select(t => t!["datum"]!.GetValue<string>()).ToList();
        Assert.Equal(tage.OrderBy(x => x, StringComparer.Ordinal), tage);
        // nur ein Unterricht
        var nurU = await Testserver.HolenAsync(leitung, $"/api/app/klassenbuch/uebersicht?von={Tag}&bis={Tag}&klasse=7a&unterricht={k.Unterricht}");
        Assert.All(nurU["tage"]!.AsArray().SelectMany(t => t!["stunden"]!.AsArray()), x => Assert.Equal(k.Unterricht, x!["unterricht"]!.GetValue<int>()));

        // Hausaufgabe anlegen → Liste mit Id und Bearbeitungsrecht; erledigt und löschen über die Id
        var h = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = Tag, stunde = k.Von, unterricht = k.Unterricht, text = "S. 12 Nr. 4", bemerkung = "mit Skizze" }));
        var id = h["id"]!.GetValue<int>();
        var liste = (await Testserver.HolenAsync(leitung, $"/api/app/hausaufgaben/eintraege?von={Tag}&bis={Tag}&klasse=7a")).AsArray();
        var e = liste.Single(x => x!["id"]!.GetValue<int>() == id)!;
        Assert.Equal(("S. 12 Nr. 4", "mit Skizze", false, true), (e["text"]!.GetValue<string>(), e["bemerkung"]!.GetValue<string>(), e["erledigt"]!.GetValue<bool>(), e["darf"]!.GetValue<bool>()));
        await Testserver.JsonAsync(await leitung.PutAsJsonAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}", new { erledigt = true }));
        liste = (await Testserver.HolenAsync(leitung, $"/api/app/hausaufgaben/eintraege?von={Tag}&bis={Tag}&klasse=7a")).AsArray();
        Assert.True(liste.Single(x => x!["id"]!.GetValue<int>() == id)!["erledigt"]!.GetValue<bool>());
        // fremde Lehrkraft darf nicht bearbeiten
        var fremd = await s.AlsAsync("fremd", "lehrer", stand.Lehrer.Keys.First(x => !string.Equals(x, lehrer, StringComparison.OrdinalIgnoreCase) && !k.Lehrer.Any(w => string.Equals(w.K, x, StringComparison.OrdinalIgnoreCase))));
        var sicht = (await Testserver.HolenAsync(fremd, $"/api/app/hausaufgaben/eintraege?von={Tag}&bis={Tag}&klasse=7a")).AsArray();
        Assert.False(sicht.Single(x => x!["id"]!.GetValue<int>() == id)!["darf"]!.GetValue<bool>());
        await Testserver.JsonAsync(await leitung.DeleteAsync($"/api/app/klassenbuch/hausaufgabenliste/{id}"));
        liste = (await Testserver.HolenAsync(leitung, $"/api/app/hausaufgaben/eintraege?von={Tag}&bis={Tag}&klasse=7a")).AsArray();
        Assert.DoesNotContain(liste, x => x!["id"]!.GetValue<int>() == id);
    }
}
