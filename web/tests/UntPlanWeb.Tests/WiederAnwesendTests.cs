using System.Net;
using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Tests;

/// <summary>
/// Lehrkraft früher wieder anwesend Ende zu Ende: Abwesenheit mit Vertretungen in UntPlan → Export → Lehrkraft meldet in UntPlanWeb
/// „wieder anwesend ab“ (Vorschau der Vertretungen, die rückgängig gemacht werden) → UntPlan übernimmt: Absenz verkürzt, Vertretungen
/// der frei gewordenen Zeit rückgängig, Hinweis mit Liste.
/// </summary>
public class WiederAnwesendTests
{
    private static readonly DateOnly Montag = new(2026, 10, 12);
    private static readonly DateOnly Donnerstag = new(2026, 10, 15);
    private static readonly DateOnly Freitag = new(2026, 10, 16);

    private static PortalEinstellungen Verbindung(Testserver s) => new() { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };

    /// <summary>Beispielschule mit Plan; Lehrkraft mit Unterricht am Montag und Donnerstag ist Mo–Fr abwesend, beide Stunden sind vertreten.</summary>
    private static (Schuldatei d, Lehrer lehrer, Vertretung montag, Vertretung donnerstag) Schule()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        bool Unterricht(RegelStunde r) => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && !r.Unterricht.KeineVertretung && r.KlassenIds.Count > 0;
        var mo = Tagesplan.Regulaer(d, Montag).Where(Unterricht).ToList();
        var lehrer = d.Lehrer.First(l => mo.Any(r => r.LehrerId == l.Id) && Tagesplan.Regulaer(d, Donnerstag).Where(Unterricht).Any(r => r.LehrerId == l.Id));
        AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = lehrer.Id, Von = Montag, Bis = Freitag } }, Array.Empty<Abwesenheit>());
        Vertretung Vertreten(DateOnly tag)
        {
            var v = d.Vertretungen.Where(x => x.Datum == tag && x.LehrerId == lehrer.Id && x.Offen && x.UnterrichtId.HasValue && x.GangId is null).OrderBy(x => x.Stunde).First();
            var tp = Tagesplan.Berechnen(d, tag);
            var vertreter = d.Lehrer.First(x => x.Id != lehrer.Id && Vertretungen.LehrerBelegt(d, tp, x.Id, v.Stunde, v) is null);
            Vertretungen.VertreterSetzen(d, v, vertreter.Id);
            return v;
        }
        return (d, lehrer, Vertreten(Montag), Vertreten(Donnerstag));
    }

    [Fact]
    public async Task Wieder_anwesend_macht_Vertretungen_in_UntPlan_rueckgaengig()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var (d, lehrer, montag, donnerstag) = Schule();
        var p = Verbindung(s);
        var paket = UntPlanWebExport.PaketErstellen(d, Montag, new WebExportOptionen { TageVoraus = 7, LehrerAbsenzen = true });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);

        var c = await s.AlsAsync(lehrer.Kurzname.ToLowerInvariant(), "lehrer", lehrer.Kurzname);
        // Vorschau: die Vertretung am Donnerstag wird rückgängig gemacht, die vom Montag nicht
        var v = await Testserver.HolenAsync(c, "/api/app/meldungen/anwesenheit/vorschau?ab=2026-10-15&stunde=1");
        var ids = v["eintraege"]!.AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        Assert.Contains(donnerstag.Id, ids);
        Assert.DoesNotContain(montag.Id, ids);
        Assert.Contains("rückgängig gemacht", v["hinweis"]!.GetValue<string>());
        Assert.Single(v["absenzen"]!.AsArray());

        // keine Abwesenheit zu diesem Zeitpunkt: Fehler
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/app/meldungen/anwesenheit/vorschau?ab=2026-10-19&stunde=1")).StatusCode);

        var r = await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/anwesenheit", new { ab = "2026-10-15", stunde = 1, text = "wieder gesund" }));
        Assert.Equal(WebRueckmeldungsTypen.Anwesend, r["meldung"]!["typ"]!.GetValue<string>());
        var meine = await Testserver.HolenAsync(c, "/api/app/meldungen");
        Assert.Contains(meine.AsArray(), m => m!["typ"]!.GetValue<string>() == "anwesend" && m["status"]!.GetValue<string>() == "offen");

        // UntPlan holt ab und übernimmt: Absenz endet am Mittwoch, Vertretung am Donnerstag rückgängig, Hinweis mit Liste
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        var meldung = Assert.Single(liste, m => m.Typ == WebRueckmeldungsTypen.Anwesend);
        Assert.Equal(lehrer.Kurzname, meldung.Lehrer);
        var hinweise = new List<string>();
        var (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste, hinweise);
        Assert.Contains(meldung.Id, ok);
        Assert.Empty(nein);
        var absenz = Assert.Single(d.Abwesenheiten, a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrer.Id);
        Assert.Equal((Montag, new DateOnly(2026, 10, 14)), (absenz.Von, absenz.Bis));
        Assert.DoesNotContain(d.Vertretungen, x => x.LehrerId == lehrer.Id && x.Datum >= Donnerstag && x.Datum <= Freitag);
        Assert.Contains(d.Vertretungen, x => x.Id == montag.Id && x.VertreterId == montag.VertreterId);
        var hinweis = Assert.Single(hinweise);
        Assert.Contains($"weil {lehrer.Kurzname} ab Do 15.10. 1. Std. wieder anwesend ist", hinweis);
        Assert.Contains("wurden rückgängig gemacht", hinweis);
        await UntPlanWebExport.QuittierenAsync(p, ok);
        Assert.Empty(await UntPlanWebExport.RueckmeldungenAsync(p));
    }

    [Fact]
    public async Task Nur_Lehrkraefte_melden_sich_wieder_anwesend()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/app/meldungen/anwesenheit/vorschau?ab=2026-10-14&stunde=1")).StatusCode);
    }

    [Fact]
    public void Oberflaeche_bietet_Wieder_anwesend_mit_Vorschau_und_Rueckfrage()
    {
        var js = File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "ansichten", "melden.js"));
        Assert.Contains("#/melden/anwesend", js);
        Assert.Contains("/api/app/meldungen/anwesenheit/vorschau", js);
        Assert.Contains("anwesendBestaetigen", js);
        var ui = File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "ui.js"));
        Assert.Contains("case 'anwesend'", ui);
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
