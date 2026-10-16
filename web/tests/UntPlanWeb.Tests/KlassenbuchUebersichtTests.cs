using System.Net;
using System.Net.Http.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Klassenbuch-Übersicht (Wochenansicht mit Einträgen) und Entschuldigungsstatus direkt im Abwesenheitsdialog.</summary>
public class KlassenbuchUebersichtTests
{
    private static readonly DateOnly Mittwoch = new(2026, 10, 14);

    [Fact]
    public async Task Status_im_Abwesenheitsdialog_setzt_nur_die_Klassenleitung()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var karten = plan.Karten(stand, Mittwoch, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0).OrderBy(x => x.Von).ToList();
        var k = karten.First(x => x.Lehrer[0].K != "LAN");
        var lehrer = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var sid = stand.Schueler.First(x => x.Klasse == "7a").Id;
        var (beginn, ende) = (Klassenbuchdienst.StundenZeit(stand, k.Von).Item1, Klassenbuchdienst.StundenZeit(stand, k.Bis).Item2);
        await Testserver.JsonAsync(await lehrer.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new
        {
            schuelerIds = new[] { sid }, von = $"{k.Datum}T{beginn:HH:mm}", bis = $"{k.Datum}T{ende:HH:mm}", grund = "", text = "", status = (string?)null,
            datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht,
        }));
        object Status(string st) => new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, schuelerIds = new[] { sid }, status = st };
        string Url() => $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}";

        // unterrichtende Lehrkraft ohne Klassenleitung: kein Status
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/status", Status("entschuldigt"))).StatusCode);
        Assert.False((await Testserver.HolenAsync(lehrer, Url()))["verwalten"]!.GetValue<bool>());
        // Klassenleitung bzw. Schulleitung: direkt entschuldigen, zurück auf offen, ungültiger Status abgewiesen
        Assert.True((await Testserver.HolenAsync(leitung, Url()))["verwalten"]!.GetValue<bool>());
        var r = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/status", Status("entschuldigt")));
        Assert.True(r["geaendert"]!.GetValue<int>() >= 1);
        Assert.Equal("entschuldigt", (await Testserver.HolenAsync(leitung, Url()))["fehlzeiten"]!.AsArray().Single()!["status"]!.GetValue<string>());
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/status", Status("unentschuldigt")));
        Assert.Equal("unentschuldigt", (await Testserver.HolenAsync(lehrer, Url()))["fehlzeiten"]!.AsArray().Single()!["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit/status", Status("weg"))).StatusCode);

        // Übersicht: Abwesenheit der Stunde gezählt, Klassenbucheintrag des Tages gezählt
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = k.Datum, klasse = "7a", art = "Lob", text = "Gut mitgearbeitet", zeit = $"{beginn.AddMinutes(5):HH:mm}" }));
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/hausaufgabenliste", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, text = "Seite 12" }));
        // mehrere Tage: Hausaufgaben je Tag (nicht vom letzten Tag des Zeitraums)
        var u = await Testserver.HolenAsync(leitung, $"/api/app/klassenbuch/uebersicht?bis={Mittwoch.AddDays(2):yyyy-MM-dd}&tage=3&klasse=7a");
        var tag = u["tage"]!.AsArray().Single(t => t!["datum"]!.GetValue<string>() == k.Datum)!;
        Assert.Equal(1, tag["eintraege"]!.GetValue<int>());
        var st = tag["stunden"]!.AsArray().First(x => x!["von"]!.GetValue<int>() == k.Von && x["unterricht"]!.GetValue<int>() == k.Unterricht)!;
        Assert.Equal((1, 0, 1, "Seite 12"), (st["abwesend"]!.GetValue<int>(), st["entschuldigt"]!.GetValue<int>(), st["eintraege"]!.GetValue<int>(), st["hausaufgaben"]!.GetValue<string>()));
    }
}
