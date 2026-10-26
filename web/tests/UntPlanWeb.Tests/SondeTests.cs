using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;
namespace UntPlanWeb.Tests;
public class SondeTests
{
    private readonly ITestOutputHelper _aus;
    public SondeTests(ITestOutputHelper aus) => _aus = aus;
    private static readonly DateOnly Do = new(2026, 10, 15);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Veranstaltung_ohne_Lehrer(bool umbenannt)
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        if (umbenannt) d.Vertretungseinstellungen.Bezeichnungen["Veranstaltung"] = "Ausflug";
        var kl = d.Klassen.Where(k => !k.Ignorieren).Take(2).ToList();
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = 3, StundeBis = 4, KlassenIds = kl.Select(k => k.Id).ToList(), Text = "Wandertag" });
        Vertretungen.Aktualisieren(d, Do);
        var p = UntPlanWebExport.PaketErstellen(d, Do.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });
        foreach (var x in p.Tage.Single(t => t.Datum == "2026-10-15").Stunden.Where(x => x.Stunde is 3 or 4 && (x.Veranstaltung.HasValue || x.Status != "regulaer")))
            _aus.WriteLine($"ST {x.Stunde} {x.Status} U={x.Unterricht} F={x.Fach} K={string.Join(",", x.Klassen)} L={string.Join(",", x.Lehrer)} R={string.Join(",", x.Raeume)} V={x.Vertretung} Va={x.Veranstaltung} T={x.Text}");
        foreach (var v in p.Vertretungen.Where(v => v.Datum == "2026-10-15" && v.Stunde is 3 or 4))
            _aus.WriteLine($"VT {v.Id} {v.Stunde} {v.Art} K={string.Join(",", v.Klassen)} {v.Lehrer}->{v.Vertreter} F={v.Fach}->{v.NeuesFach} R={v.Raum}->{v.NeuerRaum} Va={v.Veranstaltung} T={v.Text}");
        var s = new Stand(p, "");
        var ix = new DatenIndex(d);
        foreach (var k in new Plandienst().Karten(s, Do, "klasse", ix.K(ElementTyp.Klasse, kl[0].Id)).Where(k => k.Von <= 4 && k.Bis >= 3))
            _aus.WriteLine($"KA {k.Von}-{k.Bis} {k.Status} {k.Art} F={k.Fach} L={string.Join(",", k.Lehrer.Select(w => w.K + "/" + w.Alt))} K={string.Join(",", k.Klassen.Select(w => w.K))} T={k.Text}");
        var vp = new Vertretungsplandienst(new Plandienst(), new UntPlanWeb.Daten.Db(Directory.CreateTempSubdirectory("untplanweb-test-").FullName)).Tag(s, Do, true);
        foreach (var z in vp.Zeilen) _aus.WriteLine($"VP {z.Stunde} {z.Klassen} {z.Art} L={z.Lehrer} alt={z.LehrerAlt} {z.Status} T={z.Text}");
        foreach (var z in vp.Veranstaltungen) _aus.WriteLine("VA " + z);
    }
}
