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
}
