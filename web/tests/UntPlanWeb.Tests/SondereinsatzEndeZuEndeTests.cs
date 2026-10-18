using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sondereinsatz bzw. Veranstaltung mit mehreren Lehrkräften Ende zu Ende: wie im Desktop angelegt → echter Export → UntPlanWeb →
/// /api/app/plan jeder beteiligten Lehrkraft: alle Beteiligten stehen auf der (einen) Karte.
/// </summary>
public class SondereinsatzEndeZuEndeTests
{
    private readonly ITestOutputHelper _aus;
    public SondereinsatzEndeZuEndeTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Donnerstag = new(2026, 10, 15);

    /// <summary>Beispielschule mit fertigem Stundenplan (reguläre Stunden, mit denen ein Sondereinsatz kollidieren kann).</summary>
    private static Schuldatei Schule()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        return d;
    }

    /// <summary>Stunde (und ggf. Folgestunde), in der <paramref name="anzahl"/> Lehrkräfte frei sind; mit <paramref name="belegt"/> zusätzlich
    /// eine Lehrkraft mit eigenem Unterricht (der dann wegen des Sondereinsatzes vertreten wird bzw. entfällt).</summary>
    private static (int Stunde, List<Lehrer> Lehrer) FreieLehrkraefte(Schuldatei d, DateOnly tag, int anzahl, int dauer = 1, bool belegt = false)
    {
        var tp = Tagesplan.Berechnen(d, tag);
        bool Frei(Lehrer l, int st) => Enumerable.Range(st, dauer).All(x => !tp.Stunde(x).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id)));
        for (var st = 1; st <= 7; st++)
        {
            var frei = d.Lehrer.Where(l => Frei(l, st)).Take(anzahl).ToList();
            var mit = belegt ? d.Lehrer.FirstOrDefault(l => tp.Stunde(st).Any(e => e.Findetstatt && e.Regel != null && e.LehrerIds.Contains(l.Id))) : null;
            if (frei.Count == anzahl && (!belegt || mit != null)) return (st, mit is null ? frei : frei.Append(mit).ToList());
        }
        throw new InvalidOperationException("keine freien Lehrkräfte");
    }

    private async Task<List<JsonNode>> KartenAsync(Testserver s, string kurz, DateOnly tag, int stunde)
    {
        var c = await s.AlsAsync(kurz.ToLowerInvariant(), "lehrer", kurz);
        var iso = tag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso
            && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde).ToList();
        foreach (var k in karten) _aus.WriteLine($"{kurz}: {k.ToJsonString()}");
        return karten;
    }

    private static List<string> Lehrkraefte(JsonNode k) =>
        k["lehrer"]!.AsArray().Select(w => w!["k"]!.GetValue<string>()).Where(x => x.Length > 0).OrderBy(x => x).ToList();

    private static async Task HochladenAsync(Testserver s, Schuldatei d, int revision = 1, bool nurVertretungen = false)
    {
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var o = new WebExportOptionen { TageVoraus = 7 };
        if (nurVertretungen) o.Teile = new() { WebTeile.Vertretungen };
        var paket = UntPlanWebExport.PaketErstellen(d, Donnerstag.AddDays(-3), o);
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = revision })).Ok);
    }

    [Theory]
    [InlineData(true, true, true, 1, false)]
    [InlineData(false, true, true, 1, false)]
    [InlineData(false, false, true, 1, false)]
    [InlineData(true, false, false, 1, false)]
    [InlineData(false, true, false, 1, false)]
    [InlineData(false, true, true, 2, false)]
    [InlineData(true, true, true, 1, true)]
    [InlineData(false, false, true, 2, true)]
    public async Task Sondereinsatz_mehrerer_Lehrkraefte_zeigt_allen_alle_Beteiligten(bool mitFach, bool mitRaum, bool mitKlasse, int dauer, bool belegt)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule();
        var (stunde, lehrer) = FreieLehrkraefte(d, Donnerstag, belegt ? 2 : 3, dauer, belegt);
        var tp = Tagesplan.Berechnen(d, Donnerstag);
        // Klasse ohne Unterricht in diesen Stunden (sonst entfiele deren Unterricht – auch das darf aber nichts ändern)
        var klasse = d.Klassen.First(k => Enumerable.Range(stunde, dauer).All(x => !tp.Stunde(x).Any(e => e.Findetstatt && e.KlassenIds.Contains(k.Id))))
            ?? d.Klassen.First();
        var fach = mitFach ? d.Faecher.First(f => !Vertretungen.IstBereitschaftsfach(d, f.Id)).Id : (int?)null;
        int? raum = mitRaum ? d.Raeume.First(r => !r.Ignorieren && !Enumerable.Range(stunde, dauer).Any(x => tp.Stunde(x).Any(e => e.RaumIds.Contains(r.Id)))).Id : null;
        Vertretungen.Sondereinsatz(d, Donnerstag, stunde, stunde + dauer - 1, mitKlasse ? new[] { klasse.Id } : Array.Empty<int>(), lehrer.Select(l => l.Id).ToList(),
            fach, raum, "Projektgruppe", "", null, "lan");
        var ix = new DatenIndex(d);
        var alle = lehrer.Select(l => ix.KLehrer(l.Id)).OrderBy(x => x).ToList();
        foreach (var nur in new[] { false, true })
        {
            await HochladenAsync(s, d, nur ? 2 : 1, nur);
            foreach (var l in alle)
            {
                for (var st = stunde; st < stunde + dauer; st++)
                {
                    var karten = (await KartenAsync(s, l, Donnerstag, st)).Where(k => k["unterricht"] is null).ToList();
                    var k = Assert.Single(karten);
                    Assert.Equal(alle, Lehrkraefte(k));
                    Assert.Equal(stunde, k["von"]!.GetValue<int>());
                    Assert.Equal(stunde + dauer - 1, k["bis"]!.GetValue<int>());
                    // Klassenplan: ebenfalls eine Karte mit allen Lehrkräften
                    if (mitKlasse && l == alle[0])
                    {
                        var c = await s.AlsAsync("admin");
                        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=klasse&element={Uri.EscapeDataString(ix.K(ElementTyp.Klasse, klasse.Id))}&datum={Donnerstag:yyyy-MM-dd}");
                        var kk = Assert.Single(plan["karten"]!.AsArray().Select(x => x!), x => x["datum"]!.GetValue<string>() == $"{Donnerstag:yyyy-MM-dd}"
                            && x["von"]!.GetValue<int>() <= st && x["bis"]!.GetValue<int>() >= st && x["unterricht"] is null);
                        Assert.Equal(alle, Lehrkraefte(kk));
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Veranstaltung_mit_vielen_Lehrkraeften_zeigt_allen_alle_Beteiligten()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule();
        var (stunde, lehrer) = FreieLehrkraefte(d, Donnerstag, 4);
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Donnerstag, Bis = Donnerstag, StundeVon = stunde, StundeBis = stunde,
            LehrerIds = lehrer.Select(l => l.Id).ToList(), Text = "Fachkonferenz" });
        Vertretungen.Aktualisieren(d, Donnerstag);
        await HochladenAsync(s, d);
        var ix = new DatenIndex(d);
        var alle = lehrer.Select(l => ix.KLehrer(l.Id)).OrderBy(x => x).ToList();
        foreach (var l in alle)
        {
            var karten = await KartenAsync(s, l, Donnerstag, stunde);
            var k = Assert.Single(karten);
            Assert.Equal(alle, Lehrkraefte(k));
        }
    }
}
