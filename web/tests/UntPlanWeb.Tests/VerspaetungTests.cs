using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Verspätungslogik: Minuten auf Stunden verteilen, Schwelle zur Fehlstunde, vorzeitiges Gehen, Berichte.</summary>
public class VerspaetungTests
{
    [Fact]
    public async Task Minuten_werden_auf_die_Stunden_des_Blocks_verteilt()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var l1 = Klassenbuchdienst.StundenLaenge(stand, 1);
        var ohne = Verspaetungsregeln.Standard with { AbwesendAbMinuten = 0 };

        var a = Klassenbuchdienst.MinutenVerteilen(stand, ohne, 1, 2, "verspaetet", 10);
        Assert.Equal((1, "verspaetet", 10, ""), Assert.Single(a));

        // ganze erste Stunde verpasst → Fehlstunde, Rest als Verspätung in der zweiten
        var b = Klassenbuchdienst.MinutenVerteilen(stand, ohne, 1, 2, "verspaetet", l1 + 5);
        Assert.Equal(2, b.Count);
        Assert.Equal("fehlt", b[0].Art);
        Assert.Equal((2, "verspaetet", 5), (b[1].Stunde, b[1].Art, b[1].Minuten));

        // Schwelle: ab 10 Minuten zählt die Stunde als Fehlstunde
        var c = Klassenbuchdienst.MinutenVerteilen(stand, ohne with { AbwesendAbMinuten = 10 }, 1, 1, "verspaetet", 12);
        Assert.Equal((1, "fehlt", 0, "verspätet 12 min"), Assert.Single(c));

        // vorzeitig: von hinten gerechnet
        var d = Klassenbuchdienst.MinutenVerteilen(stand, ohne, 1, 2, "vorzeitig", 15);
        Assert.Equal((2, "vorzeitig", 15, ""), Assert.Single(d));

        Assert.Equal(1, Verspaetungsregeln.Standard.StundenAus(50));
        Assert.Equal("3× verspätet", Verspaetungsregeln.Standard.Hinweis(3, 0, 0));
    }

    [Fact]
    public async Task Verspaetung_Schwelle_Vorzeitig_RestTag_und_Bericht()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var heute = new DateOnly(2026, 10, 14);
        var karten = plan.Karten(stand, heute, Elementtyp.Klasse, "7a").Where(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0).OrderBy(x => x.Von).ToList();
        var k = karten.First();
        Assert.Contains(karten, x => x.Von > k.Bis);
        var admin = await s.AlsAsync("admin");
        var c = await s.AlsAsync(k.Lehrer[0].K.ToLowerInvariant(), "lehrer", k.Lehrer[0].K);
        var kl = await s.AlsAsync("sl-test", "leitung", "");
        string Url() => $"/api/app/klassenbuch/anwesenheit?datum={k.Datum}&stunde={k.Von}&unterricht={k.Unterricht}";
        var anw = await Testserver.HolenAsync(c, Url());
        var sid = anw["schueler"]![0]!["id"]!.GetValue<int>();
        Assert.Equal(0, anw["regeln"]!["abwesendAbMinuten"]!.GetValue<int>());
        Task Speichern(object e) => Testserver.JsonAsync(c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = new[] { e } }).Result);

        // einfache Verspätung
        // Fehlzeitenverwaltung nur für Klassenleitungen (die Fachlehrkraft ist hier nicht Klassenleitung von 7a)
        if (k.Lehrer[0].K != "LAN") Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await c.GetAsync("/api/app/abwesenheiten")).StatusCode);
        await Speichern(new { schuelerId = sid, art = "verspaetet", minuten = 8, bemerkung = "Bus" });
        var f = (await Testserver.HolenAsync(c, Url()))["fehlzeiten"]!.AsArray().Single()!;
        Assert.Equal(("verspaetet", 8, 0, "Bus"), (f["art"]!.GetValue<string>(), f["minuten"]!.GetValue<int>(), f["fehlstunden"]!.GetValue<int>(), f["bemerkung"]!.GetValue<string>()));

        // Schwelle 5 min → als Fehlstunde gewertet, Anzeige bleibt „verspätet 8 min“ mit eigenem Text
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/einstellungen/verspaetung", new { abwesendAbMinuten = 5 }));
        await Speichern(new { schuelerId = sid, art = "verspaetet", minuten = 8, bemerkung = "Bus" });
        f = (await Testserver.HolenAsync(c, Url()))["fehlzeiten"]!.AsArray().Single()!;
        Assert.Equal(("verspaetet", 8, 1, "Bus"), (f["art"]!.GetValue<string>(), f["minuten"]!.GetValue<int>(), f["fehlstunden"]!.GetValue<int>(), f["bemerkung"]!.GetValue<string>()));
        var ab = (await Testserver.HolenAsync(kl, "/api/app/abwesenheiten?von=2026-10-14&bis=2026-10-14&klasse=7a")).AsArray();
        Assert.Contains(ab, z => z!["art"]!.GetValue<string>() == "fehlt" && z["bemerkung"]!.GetValue<string>().StartsWith("verspätet 8 min"));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/einstellungen/verspaetung", new { abwesendAbMinuten = 0 }));

        // vorzeitig gegangen, Rest des Tages abwesend → spätere Stunden fehlen; erneutes Speichern ohne nimmt sie zurück
        await Speichern(new { schuelerId = sid, art = "vorzeitig", minuten = 10, restTag = true });
        var tag = (await Testserver.HolenAsync(kl, "/api/app/abwesenheiten?von=2026-10-14&bis=2026-10-14&klasse=7a")).AsArray().Where(z => z!["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.Contains(tag, z => z!["art"]!.GetValue<string>() == "vorzeitig" && z["minuten"]!.GetValue<int>() == 10);
        Assert.Contains(tag, z => z!["art"]!.GetValue<string>() == "fehlt" && z["stundeVon"]!.GetValue<int>() > k.Bis);
        var bericht = await Testserver.HolenAsync(kl, "/api/app/berichte/fehlzeiten?klasse=7a&von=2026-10-14&bis=2026-10-14");
        var zeile = bericht["zeilen"]!.AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == sid)!;
        Assert.Equal((1, 10), (zeile["vorzeitig"]!.GetValue<int>(), zeile["vorzeitigMinuten"]!.GetValue<int>()));
        Assert.True(zeile["stunden"]!.GetValue<int>() > 0);

        await Speichern(new { schuelerId = sid, art = "vorzeitig", minuten = 10, restTag = false });
        tag = (await Testserver.HolenAsync(kl, "/api/app/abwesenheiten?von=2026-10-14&bis=2026-10-14&klasse=7a")).AsArray().Where(z => z!["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.DoesNotContain(tag, z => z!["art"]!.GetValue<string>() == "fehlt");
        var csv = await kl.GetStringAsync("/api/app/berichte/fehlzeiten?klasse=7a&von=2026-10-14&bis=2026-10-14&format=csv");
        Assert.Contains("vorzeitig gegangen", csv);

        var b2 = await Testserver.HolenAsync(admin, "/api/app/berichte/erstellen/verspaetungen?von=2026-10-14&bis=2026-10-14&klasse=7a");
        Assert.Contains(b2["zeilen"]!.AsArray(), z => z!.AsArray().Any(x => x?.ToString() == "vorzeitig gegangen"));
    }
}
