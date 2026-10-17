using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprechtag (Elternsprechtag): Verwaltung durch die Administration, Zeitraster mit Pausen, Sperren der Lehrkraft, Einladungen über
/// Mitteilungen, Buchungen der Eltern mit Konfliktprüfung (ein Termin je Lehrkraft und Kind, keine Überschneidung in der Familie),
/// manuelle Termine, Verschieben/Stornieren mit Benachrichtigung, Rechte, Startseite und PDF.
/// </summary>
public class SprechtagTests
{
    // Testuhr: Mittwoch, 14.10.2026 10:00
    private static object Daten(bool sichtbar = true, string anmeldungVon = "2026-10-10T08:00", string anmeldungBis = "2026-10-20T18:00", string? einladungBis = null,
        object[]? lehrkraefte = null) => new
    {
        name = "Elternsprechtag Herbst", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10,
        pausen = new[] { new { von = "16:00", bis = "16:20" } },
        einladungBis, anmeldungVon, anmeldungBis, sichtbar, raumwahl = true, hinweis = "Bitte pünktlich erscheinen.",
        lehrkraefte = lehrkraefte ?? new object[] { new { lehrer = "LAN", raum = "" }, new { lehrer = "FIS", raum = "" }, new { lehrer = "BEC", raum = "" } },
    };

    private static async Task<(HttpClient C, int Id)> ElternAsync(Testserver s, HttpClient admin, string login, params int[] kinder)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "eltern-passwort", rolle = "eltern", anzeigename = login, gesperrt = false, schuelerIds = kinder }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "eltern-passwort" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    private static async Task<int> AnlegenAsync(HttpClient admin, object daten) =>
        (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", daten)))["id"]!.GetValue<int>();

    private static async Task<string> FehlerAsync(HttpResponseMessage r, HttpStatusCode erwartet = HttpStatusCode.BadRequest)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(erwartet == r.StatusCode, $"erwartet {erwartet}, erhalten {(int)r.StatusCode}: {text}");
        return text;
    }

    [Fact]
    public async Task Verwaltung_Zeitraster_und_Sichtbarkeit()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");

        // Pflichtangaben und Prüfungen
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { name = "x", datum = "2026-10-21", beginn = "17:00", ende = "15:00", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", lehrkraefte = new[] { new { lehrer = "LAN" } } }));
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { name = "x", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", lehrkraefte = Array.Empty<object>() }));
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { name = "x", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", lehrkraefte = new[] { new { lehrer = "GIBTSNICHT" } } }));
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { name = "x", datum = "2026-10-21", beginn = "15:00", ende = "17:00", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-22T18:00", lehrkraefte = new[] { new { lehrer = "LAN" } } }));
        // nur die Administration verwaltet
        await FehlerAsync(await lan.PostAsJsonAsync("/api/app/sprechtag", Daten()), HttpStatusCode.Forbidden);

        var id = await AnlegenAsync(admin, Daten(sichtbar: false));
        var t = await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}");
        // 15:00–16:00 = 6 Fenster, Pause 16:00–16:20, 16:20–17:00 = 4 Fenster
        Assert.Equal(10, t["sprechtag"]!["zeitfenster"]!.GetValue<int>());
        Assert.Equal("vorbereitung", t["sprechtag"]!["phase"]!.GetValue<string>());
        Assert.Equal(3, t["uebersicht"]!.AsArray().Count);
        // nicht sichtbar: Lehrkraft sieht ihn nicht
        Assert.Empty((await Testserver.HolenAsync(lan, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        await FehlerAsync(await lan.GetAsync($"/api/app/sprechtag/{id}/lehrkraft"));
        Assert.Empty((await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray());

        // freigeben → Lehrkraft sieht Zeitraster und Startseiten-Karte
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Daten()));
        var raster = await Testserver.HolenAsync(lan, $"/api/app/sprechtag/{id}/lehrkraft");
        var fenster = raster["zeitfenster"]!.AsArray();
        Assert.Equal(10, fenster.Count);
        Assert.Equal("15:50", fenster[5]!["beginn"]!.GetValue<string>());
        Assert.Equal("16:20", fenster[6]!["beginn"]!.GetValue<string>());
        Assert.All(fenster, f => Assert.Equal("frei", f!["status"]!.GetValue<string>()));
        var karte = Assert.Single((await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray());
        Assert.Equal("anmeldung", karte!["phase"]!.GetValue<string>());
        // Lehrkraft darf nur den eigenen Plan sehen, nicht beteiligte Lehrkräfte keinen
        await FehlerAsync(await lan.GetAsync($"/api/app/sprechtag/{id}/lehrkraft?lehrer=FIS"), HttpStatusCode.Forbidden);
        var kle = await s.AlsAsync("kle", "lehrer", "KLE");
        await FehlerAsync(await kle.GetAsync($"/api/app/sprechtag/{id}/lehrkraft"), HttpStatusCode.Forbidden);
        Assert.Equal("FIS", (await Testserver.HolenAsync(admin, $"/api/app/sprechtag/{id}/lehrkraft?lehrer=FIS"))["lehrer"]!["kurz"]!.GetValue<string>());

        // Raum: Lehrkraft wählt selbst (Raumwahl erlaubt)
        var raum = s.Dienst<Paketdienst>().Aktuell!.Paket.Elemente.Raeume[0].Kurz;
        await Testserver.JsonAsync(await lan.PutAsJsonAsync($"/api/app/sprechtag/{id}/raum", new { raum }));
        Assert.Equal(raum, (await Testserver.HolenAsync(lan, $"/api/app/sprechtag/{id}/lehrkraft"))["lehrer"]!["raum"]!.GetValue<string>());

        // Schüler*innen sehen freigegebene Sprechtage standardmäßig (Option „sichtbar für Schüler*innen“, Standard an) – nur lesen,
        // keine Verwaltungs- und Lehrkraftansicht
        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        Assert.Single((await Testserver.HolenAsync(schueler, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        var lesen = await Testserver.HolenAsync(schueler, $"/api/app/sprechtag/{id}");
        Assert.True(lesen["schueler"]!.GetValue<bool>());
        Assert.Null(lesen["uebersicht"]);
        Assert.Equal(0, lesen["termine"]!.GetValue<int>());
        await FehlerAsync(await schueler.GetAsync($"/api/app/sprechtag/{id}/lehrkraft"), HttpStatusCode.Forbidden);

        // löschen
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/sprechtag/{id}"));
        Assert.Empty((await Testserver.HolenAsync(admin, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
    }

    [Fact]
    public async Task Buchung_Konflikte_Sperren_Verschieben_Stornieren()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var kind7 = stand.Schueler.First(x => x.Klasse == "7a");
        var kind5 = stand.Schueler.First(x => x.Klasse == "5a");
        var fremd = stand.Schueler.Where(x => x.Klasse == "7a").Skip(1).First();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var (eltern, elternId) = await ElternAsync(s, admin, "eltern-a", kind7.Id, kind5.Id);
        var (andere, _) = await ElternAsync(s, admin, "eltern-b", fremd.Id);
        var id = await AnlegenAsync(admin, Daten());
        string Url(string pfad = "") => $"/api/app/sprechtag/{id}{pfad}";

        // Eltern sehen die Lehrkräfte des Kindes: 7a → LAN (Klassenleitung, zuerst) und FIS; BEC unterrichtet die 7a nicht
        var sicht = await Testserver.HolenAsync(eltern, Url("/eltern"));
        Assert.True(sicht["buchbar"]!.GetValue<bool>());
        var lk7 = sicht["kinder"]!.AsArray().Single(k => k!["id"]!.GetValue<int>() == kind7.Id)!["lehrkraefte"]!.AsArray();
        Assert.Equal(new[] { "LAN", "FIS" }, lk7.Select(l => l!["kurz"]!.GetValue<string>()).ToArray());
        Assert.True(lk7[0]!["klassenleitung"]!.GetValue<bool>());
        Assert.Equal(10, lk7[0]!["zeitfenster"]!.AsArray().Count);

        // Lehrkraft sperrt 15:00 (Abwesenheit)
        await Testserver.JsonAsync(await lan.PutAsJsonAsync(Url("/sperren"), new { beginne = new[] { "15:00" }, gesperrt = true }));
        Assert.Equal(9, (await Testserver.HolenAsync(eltern, Url("/eltern")))["kinder"]!.AsArray().Single(k => k!["id"]!.GetValue<int>() == kind7.Id)!["lehrkraefte"]![0]!["zeitfenster"]!.AsArray().Count);
        Assert.Contains("gesperrt", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind7.Id, beginn = "15:00" })));

        // Buchung, zweiter Termin bei derselben Lehrkraft, Überschneidung, belegtes Fenster
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind7.Id, beginn = "15:10" }));
        Assert.Contains("bereits einen Termin", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind7.Id, beginn = "15:20" })));
        Assert.Contains("Überschneidung", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "FIS", schuelerId = kind7.Id, beginn = "15:10" })));
        // auch das Geschwisterkind (5a, FIS) überschneidet sich nicht mit dem Termin der Familie
        Assert.Contains("Überschneidung", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "FIS", schuelerId = kind5.Id, beginn = "15:10" })));
        Assert.Contains("vergeben", await FehlerAsync(await andere.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = fremd.Id, beginn = "15:10" })));
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "FIS", schuelerId = kind7.Id, beginn = "15:20" }));
        // Rechte: fremdes Kind, Lehrkraft ohne Unterricht beim Kind, Lehrkraft bucht nicht über /buchen
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = fremd.Id, beginn = "15:30" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "BEC", schuelerId = kind7.Id, beginn = "15:30" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind7.Id, beginn = "15:30" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await lan.GetAsync(Url("/eltern")), HttpStatusCode.Forbidden);
        Assert.Equal(2, (await Testserver.HolenAsync(eltern, Url("/eltern")))["termine"]!.AsArray().Count);

        // Lehrkraft: Zeitraster mit gebuchtem und gesperrtem Fenster; gebuchtes Fenster lässt sich nicht sperren
        var raster = (await Testserver.HolenAsync(lan, Url("/lehrkraft")))["zeitfenster"]!.AsArray();
        Assert.Equal("gesperrt", raster[0]!["status"]!.GetValue<string>());
        Assert.Equal("gebucht", raster[1]!["status"]!.GetValue<string>());
        Assert.Equal(kind7.Id, raster[1]!["termin"]!["schueler"]!["id"]!.GetValue<int>());
        Assert.Equal("eltern-a", raster[1]!["termin"]!["gebuchtVon"]!.GetValue<string>());
        var terminId = raster[1]!["termin"]!["id"]!.GetValue<int>();
        Assert.Contains("bereits gebucht", await FehlerAsync(await lan.PutAsJsonAsync(Url("/sperren"), new { beginne = new[] { "15:10" }, gesperrt = true })));

        // Verschieben: Konflikt mit dem FIS-Termin der Familie um 15:20, dann 15:30 → Mitteilung an die Eltern
        var mitteilungen = s.Dienst<Mitteilungsdienst>();
        var vorher = mitteilungen.Ungelesen(elternId);
        Assert.Contains("Überschneidung", await FehlerAsync(await lan.PutAsJsonAsync(Url($"/termine/{terminId}"), new { beginn = "15:20" })));
        await FehlerAsync(await fis.PutAsJsonAsync(Url($"/termine/{terminId}"), new { beginn = "15:30" }), HttpStatusCode.Forbidden);
        var verschoben = await Testserver.JsonAsync(await lan.PutAsJsonAsync(Url($"/termine/{terminId}"), new { beginn = "15:30", bemerkung = "Zeugnis" }));
        Assert.Equal("15:30", verschoben["beginn"]!.GetValue<string>());
        Assert.Equal(vorher + 1, mitteilungen.Ungelesen(elternId));

        // manueller Termin für eine Familie ohne Konto (auch in einem gesperrten Fenster – die Sperre fällt weg)
        var ohneKonto = stand.Schueler.Where(x => x.Klasse == "7a").Skip(2).First();
        var manuell = await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/termine"), new { schuelerId = ohneKonto.Id, beginn = "15:00", bemerkung = "telefonisch" }));
        Assert.Equal("lehrkraft", manuell["quelle"]!.GetValue<string>());
        Assert.Contains("vergeben", await FehlerAsync(await lan.PostAsJsonAsync(Url("/termine"), new { schuelerId = fremd.Id, beginn = "15:00" })));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/termine"), new { lehrer = "FIS", schuelerId = fremd.Id, beginn = "15:40" }), HttpStatusCode.Forbidden);

        // PDF des Terminplans (S/W) – eigener Plan mit freien Zeilen, Gesamtplan nur Administration, Elternplan
        foreach (var (c, pfad) in new[] { (lan, Url("/plan.pdf?leer=true")), (admin, Url("/plan.pdf")), (eltern, Url("/elternplan.pdf")) })
        {
            var r = await c.GetAsync(pfad);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await r.Content.ReadAsByteArrayAsync())[..4]);
        }
        await FehlerAsync(await eltern.GetAsync(Url("/plan.pdf")), HttpStatusCode.Forbidden);

        // Raster ändern: gebuchter Termin passt nicht mehr bzw. Lehrkraft mit Terminen entfernen → abgelehnt
        Assert.Contains("Zeitraster", await FehlerAsync(await admin.PutAsJsonAsync(Url(), new { name = "x", datum = "2026-10-21", beginn = "15:05", ende = "17:00", dauer = 10, anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", sichtbar = true, lehrkraefte = new[] { new { lehrer = "LAN" }, new { lehrer = "FIS" } } })));
        Assert.Contains("FIS", await FehlerAsync(await admin.PutAsJsonAsync(Url(), Daten(lehrkraefte: new object[] { new { lehrer = "LAN" } }))));

        // Stornieren: fremde Eltern nicht, eigene Eltern ja; Lehrkraft storniert mit Mitteilung
        var fisTermin = (await Testserver.HolenAsync(eltern, Url("/eltern")))["termine"]!.AsArray().Single(x => x!["lehrer"]!.GetValue<string>() == "FIS")!["id"]!.GetValue<int>();
        await FehlerAsync(await andere.DeleteAsync(Url($"/termine/{fisTermin}")), HttpStatusCode.Forbidden);
        await Testserver.JsonAsync(await eltern.DeleteAsync(Url($"/termine/{fisTermin}")));
        vorher = mitteilungen.Ungelesen(elternId);
        await Testserver.JsonAsync(await lan.DeleteAsync(Url($"/termine/{terminId}")));
        Assert.Equal(vorher + 1, mitteilungen.Ungelesen(elternId));
        Assert.Empty((await Testserver.HolenAsync(eltern, Url("/eltern")))["termine"]!.AsArray());
        // Startseite der Eltern während der Anmeldung
        Assert.Equal("anmeldung", Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray())!["phase"]!.GetValue<string>());
    }

    [Fact]
    public async Task Einladungen_per_Mitteilung_und_Anmeldephase()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var kind = stand.Schueler.First(x => x.Klasse == "7a");
        var ohneKonto = stand.Schueler.Where(x => x.Klasse == "7a").Skip(1).First();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var (eltern, elternId) = await ElternAsync(s, admin, "eltern-e", kind.Id);
        // Einladungsphase: Anmeldung erst ab 16.10.
        var id = await AnlegenAsync(admin, Daten(anmeldungVon: "2026-10-16T08:00", einladungBis: "2026-10-15T20:00"));
        string Url(string pfad = "") => $"/api/app/sprechtag/{id}{pfad}";
        var karte = Assert.Single((await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray())!;
        Assert.Equal("einladung", karte["phase"]!.GetValue<string>());
        Assert.Contains("15.10.2026 20:00", karte["text"]!.GetValue<string>());
        // Eltern ohne Einladung sehen den Sprechtag schon als „angekündigt“ (Datum, Zeit, Anmeldebeginn)
        var ankuendigung = Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray())!;
        Assert.Equal("angekuendigt", ankuendigung["status"]!.GetValue<string>());
        Assert.Contains("Anmeldung ab 16.10.2026 08:00", ankuendigung["text"]!.GetValue<string>());

        var auswahl = await Testserver.HolenAsync(lan, Url("/schueler"));
        Assert.Contains("7a", auswahl["eigeneKlassen"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.True(auswahl["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == kind.Id)!["elternkonto"]!.GetValue<bool>());

        var mitteilungen = s.Dienst<Mitteilungsdienst>();
        var r = await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladungen"), new { schuelerIds = new[] { kind.Id, ohneKonto.Id }, text = "Es geht um die Mathearbeit." }));
        Assert.Equal(2, r["neu"]!.GetValue<int>());
        Assert.Equal(1, r["benachrichtigt"]!.GetValue<int>());
        Assert.Single(r["ohneKonto"]!.AsArray());
        Assert.Equal(1, mitteilungen.Ungelesen(elternId));
        var post = mitteilungen.Posteingang(elternId).Single();
        Assert.Contains("Einladung zum Sprechtag", post.Betreff);
        Assert.Contains("Mathearbeit", post.Text);
        // erneut einladen: übersprungen
        Assert.Equal(0, (await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladungen"), new { schuelerIds = new[] { kind.Id } })))["neu"]!.GetValue<int>());
        var liste = (await Testserver.HolenAsync(lan, Url("/lehrkraft")))["einladungen"]!.AsArray();
        Assert.Equal(2, liste.Count);

        // Eltern: Einladung sichtbar, Karte auf der Startseite, Buchen erst im Anmeldezeitraum
        var sicht = await Testserver.HolenAsync(eltern, Url("/eltern"));
        Assert.False(sicht["buchbar"]!.GetValue<bool>());
        var lanZeile = sicht["kinder"]![0]!["lehrkraefte"]!.AsArray().Single(l => l!["kurz"]!.GetValue<string>() == "LAN")!;
        Assert.True(lanZeile["eingeladen"]!.GetValue<bool>());
        Assert.Equal("Es geht um die Mathearbeit.", lanZeile["einladungText"]!.GetValue<string>());
        Assert.Equal("einladung", Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray())!["phase"]!.GetValue<string>());
        Assert.Contains("Anmeldezeitraum", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind.Id, beginn = "15:00" })));

        // Einladung löschen: nur eigene
        var fis = await s.AlsAsync("fis", "lehrer", "FIS");
        var eid = liste[0]!["id"]!.GetValue<int>();
        await FehlerAsync(await fis.DeleteAsync(Url($"/einladungen/{eid}")), HttpStatusCode.Forbidden);
        await Testserver.JsonAsync(await lan.DeleteAsync(Url($"/einladungen/{eid}")));

        // nach der Einladungsphase keine Einladungen mehr; im Anmeldezeitraum bucht die Familie
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 16, 9, 0, 0);
        Assert.Contains("Einladungsphase", await FehlerAsync(await lan.PostAsJsonAsync(Url("/einladungen"), new { schuelerIds = new[] { kind.Id } })));
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", schuelerId = kind.Id, beginn = "15:00" }));
        // nach dem Anmeldezeitraum: Eltern stornieren nicht mehr selbst
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 21, 8, 0, 0);
        var tid = (await Testserver.HolenAsync(eltern, Url("/eltern")))["termine"]![0]!["id"]!.GetValue<int>();
        Assert.Contains("Anmeldezeitraum", await FehlerAsync(await eltern.DeleteAsync(Url($"/termine/{tid}"))));
        Assert.Equal("geschlossen", Assert.Single((await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray())!["phase"]!.GetValue<string>());
    }

    [Fact]
    public void Zeitfenster_beginnen_nach_Pausen_neu()
    {
        var t = new Sprechtag(1, "x", "2026-10-21", "15:00", "16:00", 15, new() { new SprechtagPause("15:20", "15:35") }, "", "2026-10-10T08:00", "2026-10-20T18:00", true, false, "", "");
        Assert.Equal(new[] { "15:00", "15:35" }, Sprechtagdienst.Zeitfenster(t).Select(f => Sprechtagdienst.Text(f.Von)).ToArray());
    }
}
