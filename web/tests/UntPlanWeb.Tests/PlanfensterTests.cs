using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Planfenster je Rolle (Wochen zurück bzw. voraus, null = unbegrenzt): Speichern in den Rollen, Sitzung/Profil der App-API,
/// gesperrte Wochen im Wochenplan (gesperrt, sperrhinweis, leere Karten) und in der Tagesübersicht; Klassenbucheinträge
/// (Lehrstoff, Notizen, „Findet online statt“) ohne zeitliche Begrenzung.
/// </summary>
public class PlanfensterTests
{
    private const string Hinweis = "Die Anzeige des Stundenplans ist durch Ihren Administrator beschränkt";

    private static Benutzer Konto(string rolle) => new(1, "x", "X", rolle, "", "", false, false, "", "", null);

    [Fact]
    public void Fenster_zaehlt_Kalenderwochen_ab_der_aktuellen_Woche()
    {
        var heute = new DateOnly(2026, 10, 14); // Mittwoch
        Assert.True(Planfenster.Unbegrenzt.Erlaubt(new DateOnly(2030, 1, 1), heute));
        var f = new Planfenster(1, 2);
        Assert.True(f.Erlaubt(new DateOnly(2026, 10, 12), heute));   // Montag der aktuellen Woche
        Assert.True(f.Erlaubt(new DateOnly(2026, 10, 18), heute));   // Sonntag der aktuellen Woche
        Assert.True(f.Erlaubt(new DateOnly(2026, 10, 5), heute));    // Vorwoche
        Assert.False(f.Erlaubt(new DateOnly(2026, 10, 4), heute));   // zwei Wochen zurück
        Assert.True(f.Erlaubt(new DateOnly(2026, 11, 1), heute));    // Sonntag in zwei Wochen
        Assert.False(f.Erlaubt(new DateOnly(2026, 11, 2), heute));   // drei Wochen voraus
        // 0 = nur die aktuelle Woche, null = in diese Richtung unbegrenzt
        var nur = new Planfenster(0, null);
        Assert.False(nur.Erlaubt(new DateOnly(2026, 10, 11), heute));
        Assert.True(nur.Erlaubt(new DateOnly(2027, 6, 1), heute));
        // Administration immer unbegrenzt
        Assert.True(Rechtedienst.PlanSichtbar(Konto(Rollen.Admin) with { Planfenster = new Planfenster(0, 0) }, new DateOnly(2027, 6, 1), heute));
        Assert.False(Rechtedienst.PlanSichtbar(Konto(Rollen.Schueler) with { Planfenster = new Planfenster(0, 0) }, new DateOnly(2027, 6, 1), heute));
        Assert.Throws<ArgumentException>(() => Planfenster.Pruefen(-1, "zurück"));
        Assert.Throws<ArgumentException>(() => Planfenster.Pruefen(Planfenster.Hoechstens + 1, "voraus"));
    }

    [Fact]
    public async Task Standard_ist_unbegrenzt_und_Sitzung_liefert_null()
    {
        await using var s = await Testserver.StartenAsync();
        var sch = await s.AlsAsync("klasse-7a", "schueler", "7a");
        var b = (await Testserver.HolenAsync(sch, "/api/app/sitzung"))["benutzer"]!;
        Assert.Null(b["planWochenZurueck"]);
        Assert.Null(b["planWochenVor"]);
        Assert.True(b.AsObject().ContainsKey("planWochenZurueck"));
        var plan = await Testserver.HolenAsync(sch, "/api/app/plan?typ=klasse&element=7a&datum=2027-03-03");
        Assert.False(plan["gesperrt"]!.GetValue<bool>());
        Assert.Null(plan["sperrhinweis"]);
        var katalog = await Testserver.HolenAsync(await s.AlsAsync("admin"), "/api/app/admin/rechte/katalog");
        Assert.All(katalog["profile"]!.AsArray(), p => { Assert.Null(p!["planWochenZurueck"]); Assert.Null(p["planWochenVor"]); });
    }

    [Fact]
    public async Task Rolle_mit_Planfenster_sperrt_Wochen_ausserhalb_in_Web_und_App_API()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var sch = await s.AlsAsync("klasse-7a", "schueler", "7a");

        // Systemrolle Schüler*in: 1 Woche zurück, 2 Wochen voraus (Rechte bleiben unverändert)
        var vorher = (await Testserver.HolenAsync(admin, "/api/app/admin/rechte/katalog"))["profile"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "schueler")!;
        var gespeichert = await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/schueler",
            new { name = "Schüler*in", beschreibung = "", rechte = vorher["rechte"], planWochenZurueck = 1, planWochenVor = 2 }));
        Assert.Equal(1, gespeichert["planWochenZurueck"]!.GetValue<int>());
        Assert.Equal(2, gespeichert["planWochenVor"]!.GetValue<int>());
        Assert.Equal(vorher["rechte"]!.ToJsonString(), gespeichert["rechte"]!.ToJsonString());
        // ungültige Werte
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/schueler",
            new { name = "Schüler*in", beschreibung = "", rechte = vorher["rechte"], planWochenZurueck = -3, planWochenVor = 2 })).StatusCode);

        // Sitzung und Profil der App-API
        var b = (await Testserver.HolenAsync(sch, "/api/app/sitzung"))["benutzer"]!;
        Assert.Equal(1, b["planWochenZurueck"]!.GetValue<int>());
        Assert.Equal(2, b["planWochenVor"]!.GetValue<int>());
        var profil = await Testserver.HolenAsync(sch, "/api/app/profil");
        Assert.Equal(2, profil["planWochenVor"]!.GetValue<int>());

        // im Fenster: normaler Plan (heute = 14.10.2026; Vorwoche, aktuelle Woche, zwei Wochen voraus)
        foreach (var d in new[] { "2026-10-07", "2026-10-14", "2026-10-28" })
        {
            var plan = await Testserver.HolenAsync(sch, $"/api/app/plan?typ=klasse&element=7a&datum={d}");
            Assert.False(plan["gesperrt"]!.GetValue<bool>());
            if (d == "2026-10-14") Assert.NotEmpty(plan["karten"]!.AsArray());
        }
        // außerhalb: gesperrt, Hinweis, leere Karten – Raster und Tage bleiben
        foreach (var d in new[] { "2026-09-30", "2026-11-04", "2027-03-03" })
        {
            var plan = await Testserver.HolenAsync(sch, $"/api/app/plan?typ=klasse&element=7a&datum={d}");
            Assert.True(plan["gesperrt"]!.GetValue<bool>());
            Assert.Equal(Hinweis, plan["sperrhinweis"]!.GetValue<string>());
            Assert.Empty(plan["karten"]!.AsArray());
            Assert.Empty(plan["aufsichten"]!.AsArray());
            Assert.NotEmpty(plan["zeitraster"]!.AsArray());
            Assert.Equal(5, plan["tage"]!.AsArray().Count);
            Assert.All(plan["tage"]!.AsArray(), t => { Assert.Empty(t!["hinweise"]!.AsArray()); Assert.Empty(t["ereignisse"]!.AsArray()); Assert.False(t["frei"]!.GetValue<bool>()); });
        }
        // auch Raumpläne
        var raum = s.Dienst<Paketdienst>().Aktuell!.Paket.Elemente.Raeume[0].Kurz;
        Assert.True((await Testserver.HolenAsync(sch, $"/api/app/plan?typ=raum&element={raum}&datum=2027-03-03"))["gesperrt"]!.GetValue<bool>());

        // andere Rollen sind nicht betroffen
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        Assert.False((await Testserver.HolenAsync(lehrer, "/api/app/plan?typ=klasse&element=7a&datum=2027-03-03"))["gesperrt"]!.GetValue<bool>());
        Assert.False((await Testserver.HolenAsync(admin, "/api/app/plan?typ=klasse&element=7a&datum=2027-03-03"))["gesperrt"]!.GetValue<bool>());

        // zurück auf unbegrenzt (leer)
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/schueler",
            new { name = "Schüler*in", beschreibung = "", rechte = vorher["rechte"], planWochenZurueck = (int?)null, planWochenVor = (int?)null }));
        Assert.Null((await Testserver.HolenAsync(sch, "/api/app/sitzung"))["benutzer"]!["planWochenVor"]);
    }

    [Fact]
    public async Task Eigene_Rolle_mit_Fenster_gilt_fuer_zugewiesene_Konten_und_Tagesuebersicht()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var refe = await s.AlsAsync("ref", "lehrer", "LAN");
        var p = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/rechte/profile",
            new { name = "Referendariat", basis = "lehrer", beschreibung = "", planWochenZurueck = 0, planWochenVor = 0 }));
        var id = p["id"]!.GetValue<string>();
        Assert.Equal(0, p["planWochenVor"]!.GetValue<int>());
        var konto = s.Dienst<Benutzerdienst>().NachLogin("ref")!;
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/admin/rechte/benutzer/{konto.Id}", new { profil = id, ueberschreibungen = new Dictionary<string, int?>() }));

        Assert.False((await Testserver.HolenAsync(refe, "/api/app/plan?typ=lehrer&element=LAN&datum=2026-10-16"))["gesperrt"]!.GetValue<bool>());
        var naechste = await Testserver.HolenAsync(refe, "/api/app/plan?typ=lehrer&element=LAN&datum=2026-10-19");
        Assert.True(naechste["gesperrt"]!.GetValue<bool>());
        Assert.Empty(naechste["karten"]!.AsArray());
        var tag = await Testserver.HolenAsync(refe, "/api/app/tag?typ=klasse&datum=2026-10-19");
        Assert.True(tag["gesperrt"]!.GetValue<bool>());
        Assert.Equal(Hinweis, tag["sperrhinweis"]!.GetValue<string>());
        Assert.Empty(tag["zeilen"]!.AsArray());
        Assert.False((await Testserver.HolenAsync(refe, "/api/app/tag?typ=klasse&datum=2026-10-14"))["gesperrt"]!.GetValue<bool>());

        // Kopie übernimmt das Fenster
        var kopie = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/admin/rechte/profile/{id}/kopieren", new { name = "Referendariat 2" }));
        Assert.Equal(0, kopie["planWochenZurueck"]!.GetValue<int>());
        Assert.Equal(0, kopie["planWochenVor"]!.GetValue<int>());
        // gespeichert in der Datenbank (Migration)
        Assert.Equal(0, Convert.ToInt32(s.Dienst<Db>().Wert("SELECT plan_wochen_vor FROM rechteprofile WHERE id = @i", Db.P("@i", id))));
    }

    [Fact]
    public async Task Lehrstoff_Notizen_und_online_auch_weit_in_der_Zukunft()
    {
        await using var s = await Testserver.StartenAsync();
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        // ein Unterrichtstag mindestens zehn Wochen voraus (Regelplan)
        var tag = Enumerable.Range(70, 120).Select(i => new DateOnly(2026, 10, 14).AddDays(i))
            .First(d => !plan.Frei(stand, d).Frei && plan.Karten(stand, d, Elementtyp.Klasse, "7a").Any(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1));
        var k = plan.Karten(stand, tag, Elementtyp.Klasse, "7a").First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var iso = Plandienst.Iso(tag);

        // Lehrstoff und Notizen
        var e = await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde",
            new { datum = iso, stunde = k.Von, unterricht = k.Unterricht, thema = "Ausblick", notiz = "Material vorbereiten", notizSchueler = "Taschenrechner mitbringen" }));
        Assert.Equal("Ausblick", e["thema"]!.GetValue<string>());
        Assert.Equal("Taschenrechner mitbringen", e["notizSchueler"]!.GetValue<string>());

        // „Findet online statt“ (App-API POST und Web PUT /klassenbuch/online) – ohne vorhandenen Eintrag an einem anderen Tag
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/klassenbuch/stunde",
            new { datum = iso, stunde = k.Von, unterricht = k.Unterricht, online = true, onlineLink = "https://video.example.org/zukunft" }));
        var g = await Testserver.HolenAsync(c, $"/api/app/klassenbuch/stunde?datum={iso}&stunde={k.Von}&unterricht={k.Unterricht}");
        Assert.True(g["eintrag"]!["online"]!.GetValue<bool>());
        Assert.Equal("https://video.example.org/zukunft", g["eintrag"]!["onlineLink"]!.GetValue<string>());
        Assert.True(g["darf"]!.GetValue<bool>());

        var spaeter = Enumerable.Range(1, 60).Select(i => tag.AddDays(i))
            .FirstOrDefault(d => !plan.Frei(stand, d).Frei && plan.Karten(stand, d, Elementtyp.Lehrer, lehrer).Any(x => x.Unterricht == k.Unterricht && x.Status == "regulaer"));
        if (spaeter != default)
        {
            var k2 = plan.Karten(stand, spaeter, Elementtyp.Lehrer, lehrer).First(x => x.Unterricht == k.Unterricht && x.Status == "regulaer");
            await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/online",
                new { datum = Plandienst.Iso(spaeter), stunde = k2.Von, unterricht = k2.Unterricht, online = true }));
            var karte = (await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={lehrer}&datum={Plandienst.Iso(spaeter)}"))["karten"]!.AsArray()
                .First(x => x!["datum"]!.GetValue<string>() == Plandienst.Iso(spaeter) && x["unterricht"]?.GetValue<int>() == k2.Unterricht)!;
            Assert.True(karte["online"]!.GetValue<bool>());
        }
    }
}
