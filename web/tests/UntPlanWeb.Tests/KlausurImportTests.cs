using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

public class KlausurImportTests
{
    private readonly ITestOutputHelper _o;
    public KlausurImportTests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public async Task Repro()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var tag = new DateOnly(2026, 10, 15);
        var klasse = paket.Elemente.Klassen[0].Kurz;
        var k = s.Dienst<Plandienst>().Karten(stand, tag, Elementtyp.Klasse, klasse).First(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count > 0 && x.Raeume.Count > 0);
        var lehrer = k.Lehrer[0].K;
        var c = await s.AlsAsync(lehrer.ToLowerInvariant(), "lehrer", lehrer);
        var frei = await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum=2026-10-15&von={k.Von}&bis={k.Von}");
        _o.WriteLine(frei.ToJsonString());
        var raum = frei.AsArray()[0]!["kurz"]!.GetValue<string>();
        var andere = d.Lehrer.First(x => x.Kurzname != lehrer).Kurzname;
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-15", stundeVon = k.Von, stundeBis = k.Von, klassen = new[] { klasse }, raeume = new[] { raum }, aufsicht = new[] { lehrer, andere }, titel = k.Fach + " Klassenarbeit", text = "" }));
        void Zeigen(string t)
        {
            var st = s.Dienst<Paketdienst>().Aktuell!;
            foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, klasse), (Elementtyp.Lehrer, lehrer), (Elementtyp.Raum, raum) })
                foreach (var x in s.Dienst<Plandienst>().Karten(st, tag, typ, kurz, s.Dienst<Rueckmeldungsdienst>().Vorgemerkt(tag, st)).Where(x => x.Von <= k.Von && x.Bis >= k.Von))
                    _o.WriteLine($"{t} {typ} {x.Status} {x.Fach} [{string.Join(",", x.Klassen.Select(w => w.K))}] [{string.Join(",", x.Raeume.Select(w => w.K + "/" + w.Alt))}] {x.Art} '{x.Text}' U={x.Unterricht}");
        }
        Zeigen("vorher");
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        var (ok, nein) = StundenplanAenderungen.Uebernehmen(d, StundenplanAenderungen.Analysieren(d, liste, tag.AddDays(-5), tag.AddDays(5), false), "test");
        _o.WriteLine($"ok {ok.Count} nein {nein.Count}");
        var paket2 = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7 });
        foreach (var st in paket2.Tage.Single(t => t.Datum == "2026-10-15").Stunden.Where(x => x.Stunde == k.Von && (x.Klassen.Contains(klasse) || x.Raeume.Contains(raum))))
            _o.WriteLine($"WS {st.Status} {st.Fach} [{string.Join(",", st.Klassen)}] [{string.Join(",", st.Lehrer)}] [{string.Join(",", st.Raeume)}] U={st.Unterricht} V={st.Vertretung} '{st.Text}'");
        foreach (var v in paket2.Vertretungen.Where(v => v.Datum == "2026-10-15" && v.Stunde == k.Von))
            _o.WriteLine($"VZ {v.Id} {v.Art} [{string.Join(",", v.Klassen)}] {v.Lehrer}/{v.Vertreter} {v.Fach}/{v.NeuesFach} {v.Raum}/{v.NeuerRaum} '{v.Text}'");
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket2 with { Revision = 2 })).Ok);
        Zeigen("nachher");
    }
}
