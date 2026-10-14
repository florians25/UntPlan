using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Zusammenspiel mit UntPlan (Desktop): Paket hochladen, Rückmeldungen abholen, übernehmen und quittieren.</summary>
public class DesktopTests
{
    private static PortalEinstellungen Verbindung(Testserver s) => new()
    {
        Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel,
    };

    [Fact]
    public async Task Export_aus_UntPlan_und_Rueckmeldungen_zurueck()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        var p = Verbindung(s);

        Assert.False(string.IsNullOrWhiteSpace(await UntPlanWebExport.VerbindungPruefenAsync(p)));
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7, LehrerAbsenzen = true });
        var antwort = await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 3 });
        Assert.True(antwort.Ok, antwort.Meldung);
        Assert.Equal(d.Klassen.Count, s.Dienst<Paketdienst>().Aktuell!.Paket.Elemente.Klassen.Count);

        // Lehrkraft meldet in UntPlanWeb eine Abwesenheit und bucht einen Raum
        var lehrer = d.Lehrer[0];
        var c = await s.AlsAsync(lehrer.Kurzname.ToLowerInvariant(), "lehrer", lehrer.Kurzname);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/abwesenheit", new { von = "2026-10-15", bis = "2026-10-15", grund = "Fortbildung" }));
        var raum = d.Raeume[0].Kurzname;
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 5, stundeBis = 6, raum, titel = "Projekt" }));

        // UntPlan holt ab und übernimmt
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        Assert.Equal(2, liste.Count);
        var vorher = d.Abwesenheiten.Count;
        var (uebernommen, abgelehnt) = UntPlanWebExport.Uebernehmen(d, liste);
        Assert.Empty(abgelehnt);
        Assert.Equal(2, uebernommen.Count);
        Assert.Contains(d.Abwesenheiten.Skip(vorher), a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrer.Id && a.Von == new DateOnly(2026, 10, 15));
        await UntPlanWebExport.QuittierenAsync(p, uebernommen);

        Assert.Empty(await UntPlanWebExport.RueckmeldungenAsync(p));
        var meine = await Testserver.HolenAsync(c, "/api/app/meldungen");
        Assert.All(meine.AsArray(), m => Assert.Equal("quittiert", m!["status"]!.GetValue<string>()));

        // Export mit veralteter Revision (z. B. zweite Arbeitsstation): UntPlan sendet automatisch mit der nächsten Revision
        var erneut = await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 2 });
        Assert.True(erneut.Ok);
        Assert.Equal(4, erneut.Revision);
    }

    [Fact]
    public async Task Klassenbuch_Endpunkte_pruefen_Schule_Schluessel_und_Zeitraum()
    {
        await using var s = await Testserver.StartenAsync();
        var c = s.SyncClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(UntPlanWebSchema.Pfade.Fehlzeiten(Testserver.SchulId, "2026-09-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2026-09-01", "2026-10-31"))).StatusCode);
        // falsche Schule, fehlender oder falscher Schlüssel
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(UntPlanWebSchema.Pfade.Fehlzeiten("andere", "2026-09-01"))).StatusCode);
        var ohne = s.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ohne.GetAsync(UntPlanWebSchema.Pfade.Fehlzeiten(Testserver.SchulId, "2026-09-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ohne.GetAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2026-09-01"))).StatusCode);
        var falsch = s.Client(false);
        falsch.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "falsch");
        Assert.Equal(HttpStatusCode.Unauthorized, (await falsch.GetAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2026-09-01"))).StatusCode);
        // Angemeldete Benutzer (Cookie) haben keinen Zugriff auf die Sync-Schnittstelle
        var admin = await s.AlsAsync("admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/" + UntPlanWebSchema.Pfade.Fehlzeiten(Testserver.SchulId, "2026-09-01"))).StatusCode);
        // ungültiger Zeitraum
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/schulen/{Testserver.SchulId}/fehlzeiten")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(UntPlanWebSchema.Pfade.Fehlzeiten(Testserver.SchulId, "1.9.2026"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2026-10-01", "2026-09-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2025-01-01", "2026-10-01"))).StatusCode);
    }

    /// <summary>
    /// Ende-zu-Ende über das Demopaket: UntPlan exportiert mit Schülerliste, eine Lehrkraft erfasst in UntPlanWeb Fehlzeiten
    /// und Lehrstoff/Hausaufgabe, UntPlan importiert „Studenten*“ und „Kurse“ – ein erneuter Import legt nichts doppelt an,
    /// Änderungen (Entschuldigung) werden aktualisiert, auf dem Server gelöschte Fehlzeiten entfernt.
    /// </summary>
    [Fact]
    public async Task Studenten_und_Kurse_aus_dem_Klassenbuch_importieren()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var p = Verbindung(s);
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7, Schueler = true });
        Assert.NotEmpty(paket.Elemente.Schueler);
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);

        // Lehrkraft einer Klasse mit Schülerliste trägt Anwesenheit und Klassenbuch ein
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var heute = new DateOnly(2026, 10, 14);
        var klasse = paket.Elemente.Schueler[0].Klasse;
        var karten = s.Dienst<Plandienst>().Karten(stand, heute, Elementtyp.Klasse, klasse);
        var k = karten.First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var schueler = paket.Elemente.Schueler.Where(x => x.Klasse == klasse).Take(2).ToList();
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht,
            eintraege = new object[] { new { schuelerId = schueler[0].Id, art = "fehlt" }, new { schuelerId = schueler[1].Id, art = "verspaetet", minuten = 10 } },
        }));
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Bruchrechnung", hausaufgabe = "S. 12 Nr. 3", faellig = "2026-10-16", notiz = "intern" }));

        // Studenten*: Fehlzeiten als Schülerabwesenheiten
        var ab = new DateOnly(2026, 9, 1);
        var fz = await UntPlanWebExport.FehlzeitenAsync(p, ab);
        Assert.Equal(k.Bis - k.Von + 2, fz.Count); // Fehlen gilt für alle Stunden des Blocks, Verspätung nur für die erste
        var e1 = UntPlanWebExport.FehlzeitenUebernehmen(d, fz, ab);
        Assert.Equal(fz.Count, e1.Neu);
        Assert.Equal(0, e1.Abgelehnt);
        var fehlt = d.SchuelerAbwesenheiten.Where(a => a.SchuelerId == schueler[0].Id).ToList();
        Assert.All(fehlt, a => { Assert.False(a.Verspaetet); Assert.Equal(heute, a.Datum); Assert.Equal(klasse, a.Klasse); Assert.Equal("offen", a.Status); Assert.Equal(k.Unterricht, a.UnterrichtId); });
        Assert.Equal(10, d.SchuelerAbwesenheiten.Single(a => a.SchuelerId == schueler[1].Id).Minuten);

        // erneuter Import: nichts doppelt
        var e2 = UntPlanWebExport.FehlzeitenUebernehmen(d, await UntPlanWebExport.FehlzeitenAsync(p, ab), ab);
        Assert.Equal(new WebImportErgebnis(0, 0, 0, 0), e2);
        Assert.Equal(fz.Count, d.SchuelerAbwesenheiten.Count);

        // Schulleitung entschuldigt, Lehrkraft nimmt die Verspätung zurück → Aktualisieren bzw. Entfernen
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var ids = fz.Where(f => f.SchuelerId == schueler[0].Id).Select(f => f.Id).ToList();
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/status", new { ids, status = "entschuldigt" }));
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/anwesenheit", new
        {
            datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, eintraege = new object[] { new { schuelerId = schueler[0].Id, art = "fehlt" } },
        }));
        var e3 = UntPlanWebExport.FehlzeitenUebernehmen(d, await UntPlanWebExport.FehlzeitenAsync(p, ab), ab);
        Assert.Equal(1, e3.Entfernt);
        Assert.DoesNotContain(d.SchuelerAbwesenheiten, a => a.SchuelerId == schueler[1].Id);
        Assert.All(d.SchuelerAbwesenheiten, a => Assert.Equal("entschuldigt", a.Status));
        Assert.Equal(k.Bis - k.Von + 1, d.SchuelerAbwesenheiten.Count);

        // Kurse: Klassenbuch (ohne interne Notiz)
        var kb = await UntPlanWebExport.KlassenbuchAsync(p, ab);
        var eintrag = Assert.Single(kb);
        Assert.Equal("Bruchrechnung", eintrag.Lehrstoff);
        Assert.NotNull(eintrag.Anwesenheit);
        var roh = await s.SyncClient().GetStringAsync(UntPlanWebSchema.Pfade.Klassenbuch(Testserver.SchulId, "2026-09-01"));
        Assert.DoesNotContain("intern", roh);
        Assert.Equal(1, UntPlanWebExport.KlassenbuchUebernehmen(d, kb, ab).Neu);
        Assert.Equal(new WebImportErgebnis(0, 0, 0, 0), UntPlanWebExport.KlassenbuchUebernehmen(d, await UntPlanWebExport.KlassenbuchAsync(p, ab), ab));
        var x = Assert.Single(d.Klassenbuch);
        Assert.Equal(("Bruchrechnung", "S. 12 Nr. 3", new DateOnly(2026, 10, 16), lehrer, true), (x.Lehrstoff, x.Hausaufgabe, x.Faellig!.Value, x.Lehrer, x.AnwesenheitGeprueft));
        Assert.Contains(klasse, x.Klassen);

        // Lehrstoff geändert → Eintrag aktualisiert
        await Testserver.JsonAsync(await c.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = k.Datum, stunde = k.Von, unterricht = k.Unterricht, thema = "Bruchrechnung: Kürzen", hausaufgabe = "" }));
        Assert.Equal(1, UntPlanWebExport.KlassenbuchUebernehmen(d, await UntPlanWebExport.KlassenbuchAsync(p, ab), ab).Geaendert);
        Assert.Equal(("Bruchrechnung: Kürzen", ""), (d.Klassenbuch[0].Lehrstoff, d.Klassenbuch[0].Hausaufgabe));
        // Zeitraum nach dem Eintrag: nichts
        Assert.Empty(await UntPlanWebExport.KlassenbuchAsync(p, heute.AddDays(1)));
    }
}
