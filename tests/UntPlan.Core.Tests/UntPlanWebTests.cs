using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Portal;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Schnittstelle untplanweb/1: Paket erzeugen, serialisieren, an den Referenzserver übertragen, Rückmeldungen übernehmen.</summary>
public class UntPlanWebTests
{
    [Fact]
    public void ExportzeitraumZaehltAbHeuteAuchBeiRueckwirkendemAnfang()
    {
        var d = Testdaten.Verplant();
        var heute = d.Einstellungen.SchuljahrVon.AddDays(40);
        var o = new WebExportOptionen { TageVoraus = 7, Heute = heute };
        // normal: ab heute 7 Tage voraus
        Assert.Equal(heute.AddDays(7), UntPlanWebExport.ExportBis(d, heute, o));
        // rückwirkend: ab dem Anfangsdatum bis zum regulären Ende – nicht 7 Tage ab dem Anfangsdatum
        Assert.Equal(heute.AddDays(7), UntPlanWebExport.ExportBis(d, heute.AddDays(-30), o));
        var paket = UntPlanWebExport.PaketErstellen(d, heute.AddDays(-30), o);
        Assert.Contains(paket.Tage, t => t.Datum == heute.AddDays(7).ToString("yyyy-MM-dd"));
        Assert.Contains(paket.Tage, t => t.Datum == heute.AddDays(-30).ToString("yyyy-MM-dd"));
        // künftiges Anfangsdatum: Tage ab dort
        Assert.Equal(heute.AddDays(17), UntPlanWebExport.ExportBis(d, heute.AddDays(10), o));
        // ohne Tagesbegrenzung bis Schuljahresende
        o.OhneTagesbegrenzung = true;
        Assert.Equal(d.Einstellungen.SchuljahrBis, UntPlanWebExport.ExportBis(d, heute.AddDays(-30), o));
        // Optionen aus den Einstellungen: Tagesdatum, nicht das gewählte Vertretungsdatum
        d.Einstellungen.Portal.OhneTagesbegrenzung = true;
        var aus = WebExportOptionen.Aus(d.Einstellungen.Portal);
        Assert.True(aus.OhneTagesbegrenzung);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), aus.Heute);
    }

    [Fact]
    public void PaketEnthaeltStammdatenPeriodenTagesplaeneUndRespektiertDatenschutz()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var l = d.Lehrer[0];
        l.Email = "test@schule.de";
        var grund = d.Abwesenheitsgruende.First();
        grund.Vertraulich = true;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = tag, Bis = tag, GrundId = grund.Id });
        Vertretungsplanung.Vertretungen.Aktualisieren(d, tag);

        var p = UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { Absenzgruende = true });
        Assert.Equal(UntPlanWebSchema.Schema, p.Schema);
        Assert.Equal(d.Klassen.Count(k => !k.Ignorieren), p.Elemente.Klassen.Count);
        Assert.All(p.Elemente.Lehrer, x => Assert.Null(x.Email)); // E-Mail nur mit Freigabe
        Assert.Equal(d.Unterrichte.Count(u => !u.Ignorieren), p.Perioden.Single().Unterricht.Count);
        Assert.Equal(d.Stundenplan.Count, p.Perioden.Single().Stundenplan.Count);
        var heute = p.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd"));
        Assert.True(heute.Unterrichtstag);
        Assert.Contains(heute.Stunden, s => s.Status is "offen" or "vertreten" or "entfall");
        Assert.NotEmpty(p.Vertretungen);
        Assert.Null(p.Absenzen.Single(a => a.Art == "lehrer").Grund); // vertraulicher Grund wird nie übertragen

        var json = UntPlanWebExport.Json(p);
        var zurueck = UntPlanWebExport.Lesen(json)!;
        Assert.Equal(p.Tage.Count, zurueck.Tage.Count);
        Assert.Equal(p.Vertretungen.Count, zurueck.Vertretungen.Count);
        Assert.Contains("\"schema\":\"untplanweb/1\"", json);
    }

    [Fact]
    public void TeilpaketeEnthaltenNurDieGewaehltenTeile()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var stamm = UntPlanWebExport.PaketErstellen(d, tag, WebExportOptionen.Aus(d.Einstellungen.Portal, WebTeile.Stammdaten));
        Assert.Equal(new[] { WebTeile.Stammdaten }, stamm.Teile);
        Assert.NotEmpty(stamm.Elemente.Klassen);
        Assert.Empty(stamm.Perioden);
        Assert.Empty(stamm.Tage);
        var vertr = UntPlanWebExport.PaketErstellen(d, tag, WebExportOptionen.Aus(d.Einstellungen.Portal, WebTeile.Vertretungen));
        Assert.Empty(vertr.Perioden);
        Assert.NotEmpty(vertr.Tage);
        d.Einstellungen.Portal.FerienNichtExportieren = true;
        var unt = UntPlanWebExport.PaketErstellen(d, tag, WebExportOptionen.Aus(d.Einstellungen.Portal, WebTeile.Stammdaten, WebTeile.Unterricht));
        Assert.NotEmpty(unt.Perioden);
        Assert.Empty(unt.Ferien);
        Assert.Empty(unt.Tage);
    }

    [Fact]
    public void BuchungenUndKlausurenWerdenUebernommen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Vertretungsplanung.Tagesplan.Berechnen(d, tag);
        var freierRaum = d.Raeume.First(r => !tp.Stunde(1).Any(e => e.RaumIds.Contains(r.Id)));
        var belegt = tp.Stunde(1).First(e => e.RaumIds.Count > 0).RaumIds[0];
        var klasse = d.Klassen[0];
        var lehrer = d.Lehrer[0];
        var liste = new List<WebRueckmeldung>
        {
            new() { Id = "b1", Typ = WebRueckmeldungsTypen.Raumbuchung, Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"), StundeVon = 1, StundeBis = 1, Raum = freierRaum.Kurzname, Titel = "Elternsprechtag" },
            new() { Id = "b2", Typ = WebRueckmeldungsTypen.Raumbuchung, Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"), StundeVon = 1, StundeBis = 1, Raum = d.Raeume.First(r => r.Id == belegt).Kurzname },
            new() { Id = "k1", Typ = WebRueckmeldungsTypen.Klausur, Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"), StundeVon = 2, StundeBis = 3, Klassen = { klasse.Kurzname }, Titel = "Mathe-Klausur" },
            new() { Id = "k2", Typ = WebRueckmeldungsTypen.Klausur, Von = tag.ToString("yyyy-MM-dd"), Klassen = { "GIBTSNICHT" } },
        };
        var (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
        Assert.Equal(new[] { "b1", "k1" }, ok);
        Assert.Equal(new[] { "b2", "k2" }, nein);
        var buchung = Assert.Single(d.Vertretungen, v => v.Art == VertretungsArt.Raumbuchung);
        Assert.Equal(freierRaum.Id, buchung.VertretungsraumId);
        Assert.Equal(lehrer.Id, buchung.VertreterId);
        var k = Assert.Single(d.Klausuren);
        Assert.Equal("Mathe-Klausur", k.Name);
        Assert.Equal(3, k.StundeBis);
        // Unterricht der Klasse entfällt während der Klausur
        Assert.Contains(d.Vertretungen, v => v.KlausurId == k.Id && v.Datum == tag);
        // Löschen aus UntPlanWeb entfernt die Prüfung wieder
        var (ok2, _) = UntPlanWebExport.Uebernehmen(d, new List<WebRueckmeldung>
        {
            new() { Id = "kl1", Typ = WebRueckmeldungsTypen.KlausurLoeschen, Von = tag.ToString("yyyy-MM-dd"), StundeVon = 2, StundeBis = 3, Klassen = { klasse.Kurzname }, Titel = "Mathe-Klausur" },
        });
        Assert.Equal(new[] { "kl1" }, ok2);
        Assert.Empty(d.Klausuren);
        // doppelt importierte Prüfungen werden beim nächsten Hochladen auf eine zurückgeführt
        for (var i = 0; i < 3; i++)
            d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "Doppelt", Datum = tag, StundeVon = 2, StundeBis = 3, KlassenIds = { klasse.Id } });
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        Assert.Single(d.Klausuren);
        Assert.Single(paket.Termine, t => t.Art == "klausur" && t.Titel == "Doppelt");
    }

    /// <summary>
    /// Prüfung aus UntPlanWeb: auch doppelt übernommen (automatischer Abgleich und Dialog „Stundenplanänderungen“) entsteht nur eine
    /// Prüfung; im Paket tragen Prüfungszeilen und der wegen der Prüfung entfallene Unterricht die Klausur-Id (eine Karte im Web).
    /// </summary>
    [Fact]
    public void PruefungAusDemWebNurEinmalUndMitKlausurIdImPaket()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Vertretungsplanung.Tagesplan.Berechnen(d, tag);
        var stunde = tp.Eintraege.First(e => e.Unterricht != null && e.KlassenIds.Count == 1 && e.RaumIds.Count == 1 && e.LehrerIds.Count == 1);
        var klasse = d.Klassen.First(k => k.Id == stunde.KlassenIds[0]);
        var lehrer = d.Lehrer.First(l => l.Id == stunde.LehrerIds[0]);
        var raum = d.Raeume.First(r => !tp.Stunde(stunde.Stunde).Any(e => e.RaumIds.Contains(r.Id)));
        var m = new WebRueckmeldung
        {
            Id = "pk1", Typ = WebRueckmeldungsTypen.Klausur, Lehrer = lehrer.Kurzname, Von = tag.ToString("yyyy-MM-dd"), StundeVon = stunde.Stunde, StundeBis = stunde.Stunde,
            Klassen = { klasse.Kurzname }, Raeume = { raum.Kurzname }, Titel = "IT Klassenarbeit",
        };
        Assert.Equal(new[] { "pk1" }, UntPlanWebExport.Uebernehmen(d, new[] { m }).Uebernommen);
        StundenplanAenderungen.Uebernehmen(d, StundenplanAenderungen.Analysieren(d, new[] { m }, tag.AddDays(-1), tag.AddDays(1), false));
        UntPlanWebExport.Uebernehmen(d, new[] { m });
        var k = Assert.Single(d.Klausuren);
        Assert.Equal("pk1", k.WebId);
        // ältere Dateien: dieselbe Rückmeldung zweimal (z. B. mit anderem Namen) → beim Hochladen auf eine zurückgeführt
        d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "IT", Datum = tag, StundeVon = stunde.Stunde, StundeBis = stunde.Stunde, KlassenIds = { klasse.Id }, WebId = "pk1" });
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        Assert.Single(d.Klausuren);
        var stunden = paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd")).Stunden.Where(x => x.Stunde == stunde.Stunde && x.Klassen.Contains(klasse.Kurzname)).ToList();
        var entfall = Assert.Single(stunden, x => x.Status == "entfall");
        Assert.Equal(k.Id, entfall.Klausur);
        Assert.Equal(stunde.Unterricht!.Id, entfall.Unterricht);
        var pruefung = Assert.Single(stunden, x => x.Status == "zusaetzlich");
        Assert.Equal(k.Id, pruefung.Klausur);
        Assert.Equal(new[] { raum.Kurzname }, pruefung.Raeume);
        // übriger Unterricht trägt keine Klausur-Id
        Assert.All(paket.Tage.SelectMany(t => t.Stunden).Where(x => x.Stunde != stunde.Stunde || !x.Klassen.Contains(klasse.Kurzname)), x => Assert.Null(x.Klausur));
    }

    [Fact]
    public async Task UebertragungUndRueckmeldungen()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "untplan_web_" + Guid.NewGuid().ToString("N"));
        var app = PortalServer.Erstellen(new[] { "--urls", "http://127.0.0.1:0" }, o =>
        {
            o.Datenordner = ordner;
            o.Schulen = new() { new SchulKonfiguration { Id = "199999", Name = "Testschule", ApiSchluessel = "geheim", MeldeSchluessel = "1234" } };
        });
        await app.StartAsync();
        try
        {
            var adresse = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var d = Testdaten.Verplant();
            var tag = Testdaten.Unterrichtstag(d);
            var ps = new PortalEinstellungen { Adresse = adresse, SchulId = "199999", ApiSchluessel = "geheim" };
            var antwort = await UntPlanWebExport.HochladenAsync(ps, UntPlanWebExport.PaketErstellen(d, tag));
            Assert.True(antwort.Ok);
            // gleiche Revision erneut → veraltet
            await Assert.ThrowsAsync<InvalidOperationException>(() => UntPlanWebExport.HochladenAsync(ps, UntPlanWebExport.PaketErstellen(d, tag)));

            using var http = new HttpClient { BaseAddress = new Uri(adresse) };
            var l = d.Lehrer[1];
            (await http.PostAsJsonAsync("/api/v1/schulen/199999/meldungen", new { pin = "1234", kuerzel = l.Kurzname, von = tag.ToString("yyyy-MM-dd") })).EnsureSuccessStatusCode();
            var rm = await UntPlanWebExport.RueckmeldungenAsync(ps);
            Assert.Equal("absenz", rm.Single().Typ);
            var (ok, _) = UntPlanWebExport.Uebernehmen(d, rm);
            Assert.Single(ok);
            Assert.Contains(d.Abwesenheiten, a => a.ElementId == l.Id && a.Von == tag);
            await UntPlanWebExport.QuittierenAsync(ps, ok);
            Assert.Empty(await UntPlanWebExport.RueckmeldungenAsync(ps));
        }
        finally
        {
            await app.StopAsync();
            try { Directory.Delete(ordner, true); } catch { }
        }
    }

    [Fact]
    public void FehlzeitenWerdenOhneDoppelimportAlsSchuelerabwesenheitenUebernommen()
    {
        var d = Demo.Beispielschule();
        var a = d.Schueler[0];
        var b = d.Schueler[1];
        var ab = new DateOnly(2026, 10, 1);
        var liste = new List<WebFehlzeit>
        {
            new() { Id = 1, Datum = "2026-10-14", Stunde = 1, Unterricht = 5, SchuelerId = a.Id, Klasse = "EF", Fach = "M", Art = "fehlt", Lehrer = "MUE" },
            new() { Id = 2, Datum = "2026-10-14", Stunde = 2, Unterricht = 5, SchuelerId = a.Id, Klasse = "EF", Fach = "M", Art = "fehlt", Lehrer = "MUE" },
            new() { Id = 3, Datum = "2026-10-14", Stunde = 1, SchuelerId = b.Id, Klasse = "EF", Art = "verspaetet", Minuten = 12, Status = "unentschuldigt" },
            new() { Id = 4, Datum = "2026-10-14", Stunde = 1, SchuelerId = 999999, Klasse = "EF" },   // unbekannt
            new() { Id = 5, Datum = "14.10.2026", Stunde = 1, SchuelerId = a.Id },                    // ungültiges Datum
        };
        Assert.Equal(new WebImportErgebnis(3, 0, 0, 2), UntPlanWebExport.FehlzeitenUebernehmen(d, liste, ab));
        Assert.Equal(3, d.SchuelerAbwesenheiten.Count);
        var v = d.SchuelerAbwesenheiten.Single(x => x.SchuelerId == b.Id);
        Assert.True(v.Verspaetet);
        Assert.Equal((12, "unentschuldigt", (int?)null), (v.Minuten, v.Status, v.UnterrichtId));
        Assert.Equal(5, d.SchuelerAbwesenheiten.First(x => x.SchuelerId == a.Id).UnterrichtId);

        // gleicher Import erneut: keine Änderung, nichts doppelt
        Assert.Equal(new WebImportErgebnis(0, 0, 0, 2), UntPlanWebExport.FehlzeitenUebernehmen(d, liste, ab));
        Assert.Equal(3, d.SchuelerAbwesenheiten.Count);

        // entschuldigt, neu gespeichert (neue Server-Id) und Verspätung gelöscht
        var neu = new List<WebFehlzeit>
        {
            liste[0] with { Id = 11, Status = "entschuldigt", Bemerkung = "Arzt" },
            liste[1] with { Id = 12, Status = "entschuldigt", Bemerkung = "Arzt" },
        };
        Assert.Equal(new WebImportErgebnis(0, 2, 1, 0), UntPlanWebExport.FehlzeitenUebernehmen(d, neu, ab));
        Assert.Equal(2, d.SchuelerAbwesenheiten.Count);
        Assert.All(d.SchuelerAbwesenheiten, x => Assert.Equal(("entschuldigt", "Arzt"), (x.Status, x.Bemerkung)));
        Assert.Equal(new[] { 11, 12 }, d.SchuelerAbwesenheiten.Select(x => x.WebId).OrderBy(x => x));

        // Einträge vor dem abgefragten Zeitraum bleiben erhalten
        Assert.Equal(new WebImportErgebnis(0, 0, 0, 0), UntPlanWebExport.FehlzeitenUebernehmen(d, new List<WebFehlzeit>(), new DateOnly(2026, 10, 15)));
        Assert.Equal(2, d.SchuelerAbwesenheiten.Count);

        // übersteht Speichern und Laden der Schuldatei
        var json = System.Text.Json.JsonSerializer.Serialize(d, Dokument.JsonOptionen);
        var geladen = System.Text.Json.JsonSerializer.Deserialize<Schuldatei>(json, Dokument.JsonOptionen)!;
        Assert.Equal(2, geladen.SchuelerAbwesenheiten.Count);
    }

    [Fact]
    public void KlassenbuchWirdOhneDoppelimportUebernommen()
    {
        var d = Demo.Beispielschule();
        var ab = new DateOnly(2026, 9, 1);
        var e = new WebKlassenbucheintrag { Id = 7, Datum = "2026-10-14", Stunde = 3, Unterricht = 12, Klassen = { "5a" }, Fach = "M", Lehrer = "MUE", Lehrstoff = "Brüche", Hausaufgabe = "S. 12", Faellig = "2026-10-16", Geaendert = "2026-10-14 10:00" };
        Assert.Equal(new WebImportErgebnis(1, 0, 0, 0), UntPlanWebExport.KlassenbuchUebernehmen(d, new[] { e }, ab));
        Assert.Equal(new WebImportErgebnis(0, 0, 0, 0), UntPlanWebExport.KlassenbuchUebernehmen(d, new[] { e }, ab));
        var k = Assert.Single(d.Klassenbuch);
        Assert.Equal(("Brüche", "S. 12", new DateOnly(2026, 10, 16), 12, false), (k.Lehrstoff, k.Hausaufgabe, k.Faellig!.Value, k.UnterrichtId!.Value, k.AnwesenheitGeprueft));

        var geaendert = e with { Lehrstoff = "Brüche kürzen", Anwesenheit = "2026-10-14 10:05", Geaendert = "2026-10-14 10:05" };
        var zweiter = e with { Id = 8, Stunde = 4, Lehrstoff = "Übung", Hausaufgabe = "", Faellig = null };
        Assert.Equal(new WebImportErgebnis(1, 1, 0, 0), UntPlanWebExport.KlassenbuchUebernehmen(d, new[] { geaendert, zweiter }, ab));
        Assert.Equal(2, d.Klassenbuch.Count);
        Assert.True(d.Klassenbuch.Single(x => x.WebId == 7).AnwesenheitGeprueft);
        // auf dem Server gelöscht
        Assert.Equal(new WebImportErgebnis(0, 0, 1, 0), UntPlanWebExport.KlassenbuchUebernehmen(d, new[] { geaendert }, ab));
        Assert.Equal(7, Assert.Single(d.Klassenbuch).WebId);
    }
}
