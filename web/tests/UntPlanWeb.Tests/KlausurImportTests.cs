using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Prüfung in UntPlanWeb eintragen, in UntPlan übernehmen und erneut exportieren: danach genau eine Karte (die Stunde mit Prüfung,
/// Raum der Prüfung als Änderung) – keine zusätzliche Prüfungskarte je Aufsicht, kein Entfall daneben.
/// </summary>
public class KlausurImportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)] // älteres Paket ohne Klausur-Id an den Stunden
    public async Task Uebernommene_Pruefung_ergibt_eine_Karte(bool raumZuerst, bool ohneKlausurId)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var optionen = new WebExportOptionen { TageVoraus = 7 };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), optionen) with { Revision = 1 })).Ok);
        var plan = s.Dienst<Plandienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tag = new DateOnly(2026, 10, 15);
        var klasse = stand.Paket.Elemente.Klassen[0].Kurz;
        var k = plan.Karten(stand, tag, Elementtyp.Klasse, klasse).First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1 && x.Raeume.Count == 1 && x.Von == x.Bis);
        var lehrer = k.Lehrer[0].K;
        var alterRaum = k.Raeume[0].K;
        // zweite Aufsicht: Lehrkraft mit eigenem Unterricht in einer anderen Klasse zur selben Zeit
        var aufsicht = plan.Rohstunden(stand, tag).First(x => x.Stunde == k.Von && x.Lehrer.Count > 0 && !x.Lehrer.Contains(lehrer) && !x.Klassen.Contains(klasse)).Lehrer[0];
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var raum = (await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum=2026-10-15&von={k.Von}&bis={k.Von}")).AsArray()[0]!["kurz"]!.GetValue<string>();
        if (raumZuerst)
            await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung", new { datum = "2026-10-15", stundeVon = k.Von, stundeBis = k.Von, unterricht = k.Unterricht, raeume = new[] { raum } }));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new
        {
            datum = "2026-10-15", stundeVon = k.Von, stundeBis = k.Von, klassen = new[] { klasse }, raeume = new[] { raum }, aufsicht = new[] { lehrer, aufsicht }, titel = k.Fach + " Klassenarbeit", text = "",
        }));

        // UntPlan übernimmt (Dialog „Stundenplanänderungen“ und automatischer Abgleich – doppelt darf nichts verdoppeln) und exportiert erneut
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        StundenplanAenderungen.Uebernehmen(d, StundenplanAenderungen.Analysieren(d, liste, tag.AddDays(-5), tag.AddDays(5), false), "test");
        UntPlanWebExport.Uebernehmen(d, liste);
        Assert.Single(d.Klausuren);
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), optionen);
        if (ohneKlausurId)
            paket = paket with { Tage = paket.Tage.Select(t => t with { Stunden = t.Stunden.Select(x => x with { Klausur = null }).ToList() }).ToList() };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 2 })).Ok);
        stand = s.Dienst<Paketdienst>().Aktuell!;
        List<Karte> Karten(string typ, string kurz) =>
            plan.Karten(stand, tag, typ, kurz, s.Dienst<Rueckmeldungsdienst>().Vorgemerkt(tag, stand)).Where(x => x.Von <= k.Von && x.Bis >= k.Von).ToList();

        foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, klasse), (Elementtyp.Lehrer, lehrer) })
        {
            var karte = Assert.Single(Karten(typ, kurz), x => x.Klassen.Any(w => w.K == klasse));
            Assert.Equal("klausur", karte.Status);
            Assert.Equal(k.Fach, karte.Fach);
            Assert.Equal(k.Unterricht, karte.Unterricht);
            Assert.Equal(k.Fach + " Klassenarbeit", karte.Text);
            var r = Assert.Single(karte.Raeume);
            Assert.Equal(raum, r.K);
            Assert.Equal(alterRaum, r.Alt);
        }
        // zweite Aufsicht: eine Prüfungskarte; der eigene Unterricht wird vertreten
        var beiAufsicht = Karten(Elementtyp.Lehrer, aufsicht);
        Assert.Single(beiAufsicht, x => x.Status == "klausur");
        Assert.DoesNotContain(beiAufsicht, x => x.Status == "entfall" && x.Klassen.Any(w => w.K == klasse));
        // Raumplan des Prüfungsraums: genau eine Prüfung; bisheriger Raum: kein Entfall
        Assert.Single(Karten(Elementtyp.Raum, raum), x => x.Status == "klausur");
        if (!ohneKlausurId) Assert.DoesNotContain(Karten(Elementtyp.Raum, alterRaum), x => x.Status == "entfall");
    }
}
