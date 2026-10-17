using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Raumplan ohne weggenommene Stunden, Raumänderung aus einer Buchung ohne Vertretungstext (auch nach Im-/Export über UntPlan)
/// und „Findet online statt“ mit Link zum Online-Unterricht.
/// </summary>
public class PlanRaumOnlineTests
{
    private static IEnumerable<JsonNode> Karten(JsonNode plan, string datum, int stunde) =>
        plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == datum && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde);

    private static readonly DateOnly Freitag = new(2026, 10, 16);

    // ------------------------------------------------------------------ Raumplan: aus dem Raum genommene Stunden

    [Fact]
    public async Task Raumaenderung_weg_aus_einem_Raum_erscheint_im_Raumplan_dieses_Raums_nicht_mehr()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var k = plan.Karten(stand, Freitag, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1 && x.Raeume.Count == 1);
        var lehrer = k.Lehrer[0].K;
        var alterRaum = k.Raeume[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var frei = await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum=2026-10-16&von={k.Von}&bis={k.Von}");
        var neuerRaum = frei.AsArray().Select(x => x!["kurz"]!.GetValue<string>()).First(x => x != alterRaum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = "2026-10-16", stundeVon = k.Von, stundeBis = k.Von, unterricht = k.Unterricht, raeume = new[] { neuerRaum } }));

        // App-API (Web, PWA, iOS): im alten Raum weder Entfall noch „findet woanders statt“
        var alt = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={alterRaum}&datum=2026-10-16");
        Assert.DoesNotContain(Karten(alt, "2026-10-16", k.Von), x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.DoesNotContain(plan.Karten(stand, Freitag, Elementtyp.Raum, alterRaum, s.Dienst<Rueckmeldungsdienst>().Vorgemerkt(Freitag, stand)),
            x => x.Unterricht == k.Unterricht && x.Von <= k.Von && x.Bis >= k.Von);
        // im neuen Raum steht die Stunde
        var neu = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={neuerRaum}&datum=2026-10-16");
        Assert.Contains(Karten(neu, "2026-10-16", k.Von), x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        // Klassen- und Lehrerplan unverändert eine Karte mit „alt → neu“
        var kl = Karten(await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-16"), "2026-10-16", k.Von)
            .Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal(neuerRaum, kl["raeume"]![0]!["k"]!.GetValue<string>());
        Assert.Equal(alterRaum, kl["raeume"]![0]!["alt"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ Raumänderung aus Buchung: Im-/Export ohne Vertretungstext

    [Fact]
    public void Automatischer_Raumaenderungs_Hinweis_aelterer_Versionen_ist_kein_Vertretungstext()
    {
        Assert.Equal("", Plandienst.TextNormal("(lan) Raumänderung"));
        Assert.Equal("", Plandienst.TextNormal(" (admin)  Raumänderung "));
        Assert.Equal("Arbeitsblatt", Plandienst.TextNormal("Arbeitsblatt · (lan) Raumänderung"));
        Assert.Equal("Raumänderung wegen Wasserschaden", Plandienst.TextNormal("Raumänderung wegen Wasserschaden"));
        Assert.Equal(Plandienst.OhneLehrkraftText, Plandienst.TextNormal("Vertr. o. Lehrkraft"));
    }

    [Fact]
    public async Task Raumaenderung_aus_Buchung_bleibt_nach_Im_und_Export_ohne_Vertretungstext()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var optionen = new WebExportOptionen { TageVoraus = 7 };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), optionen) with { Revision = 1 })).Ok);

        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        // eine reguläre Stunde mit einer Lehrkraft und einem Raum (Mo–Fr der exportierten Woche)
        var (tag, k) = Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 10, 12).AddDays(i))
            .SelectMany(t => stand.Paket.Elemente.Klassen.SelectMany(kl => plan.Karten(stand, t, Elementtyp.Klasse, kl.Kurz)).Select(x => (t, x)))
            .First(x => x.x.Unterricht.HasValue && x.x.Status == "regulaer" && x.x.Lehrer.Count == 1 && x.x.Raeume.Count == 1 && x.t >= new DateOnly(2026, 10, 14));
        var iso = Plandienst.Iso(tag);
        var lehrer = k.Lehrer[0].K;
        var alterRaum = k.Raeume[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var frei = await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum={iso}&von={k.Von}&bis={k.Von}");
        var neuerRaum = frei.AsArray().Select(x => x!["kurz"]!.GetValue<string>()).First(x => x != alterRaum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = iso, stundeVon = k.Von, stundeBis = k.Von, unterricht = k.Unterricht, raeume = new[] { neuerRaum } }));

        // UntPlan: „Stundenplanänderungen aus UntPlanWeb“ übernehmen → Raumvertretung ohne Vertretungstext
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        var zeilen = StundenplanAenderungen.Analysieren(d, liste, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30), false);
        var z = Assert.Single(zeilen);
        Assert.Equal(StplAenderungsArt.Unterricht, z.Art);
        var (ok, nein) = StundenplanAenderungen.Uebernehmen(d, zeilen, "lan");
        Assert.Empty(nein);
        await UntPlanWebExport.QuittierenAsync(p, ok);
        var v = Assert.Single(d.Vertretungen, x => x.WebId == z.Id);
        Assert.Equal("", v.Text);

        // erneuter Export: weder Karte noch Vertretungsplan zeigen einen Vertretungstext; Räume „alt → neu“
        Assert.True((await UntPlanWebExport.HochladenAsync(p, UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), optionen) with { Revision = 2 })).Ok);
        var karte = Karten(await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum={iso}"), iso, k.Von)
            .Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal("", karte["text"]!.GetValue<string>());
        Assert.Equal(neuerRaum, karte["raeume"]![0]!["k"]!.GetValue<string>());
        Assert.Equal(alterRaum, karte["raeume"]![0]!["alt"]!.GetValue<string>());
        var vp = await Testserver.HolenAsync(c, $"/api/app/vertretungen?datum={iso}");
        Assert.All(vp["tage"]![0]!["zeilen"]!.AsArray(), x => Assert.DoesNotContain("Raumänderung", x!.ToJsonString().Replace("\"art\":\"Raumänderung\"", "")));
        // Raumplan des alten Raums: die Stunde erscheint nicht mehr
        var altPlan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={alterRaum}&datum={iso}");
        Assert.DoesNotContain(Karten(altPlan, iso, k.Von), x => x["unterricht"]?.GetValue<int>() == k.Unterricht);

        // ältere Datei mit dem automatischen Hinweis „(lan) Raumänderung“: wird nicht exportiert
        v.Text = "(lan) Raumänderung";
        Assert.True((await UntPlanWebExport.HochladenAsync(p, UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), optionen) with { Revision = 3 })).Ok);
        karte = Karten(await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum={iso}"), iso, k.Von)
            .Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal("", karte["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ Findet online statt + Link

    [Fact]
    public async Task Online_Unterricht_mit_Link_fuer_Lehrkraft_Schueler_und_App()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tag = new DateOnly(2026, 10, 14);
        var k = plan.Karten(stand, tag, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var stunde = $"/api/app/klassenbuch/stunde?datum=2026-10-14&stunde={k.Von}&unterricht={k.Unterricht}";

        // ungültige Links werden abgelehnt (nur http/https) – und nichts wird gespeichert
        foreach (var falsch in new[] { "javascript:alert(1)", "ftp://server/x", "meet.example.org/abc", "https://", "https://exa mple.org" })
        {
            var r = await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = true, onlineLink = falsch });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Null((await Testserver.HolenAsync(c, stunde))["eintrag"]);

        // Lehrkraft: einschalten und Link speichern (PUT /klassenbuch/stunde, Feld onlineLink)
        const string link = "https://meet.example.org/7a-mathe?pwd=1";
        var e = await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = true, onlineLink = link }));
        Assert.True(e["online"]!.GetValue<bool>());
        Assert.Equal(link, e["onlineLink"]!.GetValue<string>());
        var g = await Testserver.HolenAsync(c, stunde);
        Assert.Equal(link, g["eintrag"]!["onlineLink"]!.GetValue<string>());
        Assert.True(g["darf"]!.GetValue<bool>());

        // Karten (Web, PWA, iOS): online + onlineLink
        foreach (var url in new[] { $"/api/app/plan?typ=lehrer&element={lehrer}&datum=2026-10-14", "/api/app/plan?typ=klasse&element=7a&datum=2026-10-14" })
        {
            var karte = Karten(await Testserver.HolenAsync(c, url), "2026-10-14", k.Von).Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
            Assert.True(karte["online"]!.GetValue<bool>());
            Assert.Equal(link, karte["onlineLink"]!.GetValue<string>());
        }

        // Schüler*innen: sehen den Link (Karte und Stundendetails), dürfen nichts ändern
        var sch = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var sk = Karten(await Testserver.HolenAsync(sch, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-14"), "2026-10-14", k.Von).Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.Equal(link, sk["onlineLink"]!.GetValue<string>());
        var sg = await Testserver.HolenAsync(sch, stunde);
        Assert.Equal(link, sg["eintrag"]!["onlineLink"]!.GetValue<string>());
        Assert.False(sg["darf"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await sch.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = false })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await sch.PutAsJsonAsync("/api/app/klassenbuch/online", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = false })).StatusCode);

        // andere Lehrkraft (nicht in dieser Stunde): kein Recht
        var fremd = stand.Paket.Elemente.Lehrer.Select(x => x.Kurz).First(x => !x.Equals(lehrer, StringComparison.OrdinalIgnoreCase)
            && !plan.Karten(stand, tag, Elementtyp.Lehrer, x).Any(y => y.Unterricht == k.Unterricht));
        var f = await s.AlsAsync(fremd.ToLowerInvariant(), "lehrer", fremd);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PutAsJsonAsync("/api/app/klassenbuch/online",
            new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = true, onlineLink = "https://boese.example.org" })).StatusCode);

        // Administration darf; Link über PUT /klassenbuch/online ändern, ausschalten blendet ihn für Schüler*innen und Karten aus
        var admin = await s.AlsAsync("chef", "admin");
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/klassenbuch/online",
            new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = true, onlineLink = "http://video.example.org/raum1" }));
        Assert.Equal("http://video.example.org/raum1", (await Testserver.HolenAsync(c, stunde))["eintrag"]!["onlineLink"]!.GetValue<string>());
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, online = false }));
        sk = Karten(await Testserver.HolenAsync(sch, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-14"), "2026-10-14", k.Von).Single(x => x["unterricht"]?.GetValue<int>() == k.Unterricht);
        Assert.False(sk["online"]!.GetValue<bool>());
        Assert.Equal("", sk["onlineLink"]!.GetValue<string>());
        Assert.Equal("", (await Testserver.HolenAsync(sch, stunde))["eintrag"]!["onlineLink"]!.GetValue<string>());
        // Link leeren
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = "2026-10-14", stunde = k.Von, unterricht = k.Unterricht, onlineLink = "" }));
        Assert.Equal("", (await Testserver.HolenAsync(c, stunde))["eintrag"]!["onlineLink"]!.GetValue<string>());
    }
}
