using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;
namespace UntPlanWeb.Tests;
public class Sonde3Tests
{
    private readonly ITestOutputHelper _aus;
    public Sonde3Tests(ITestOutputHelper aus) => _aus = aus;
    private static readonly DateOnly Fr = new(2026, 11, 6);
    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void Fach(int weg, bool nur)
    {
        var d = Demo.NeueDatei("T", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW", Hintergrund = 0xFF0000 }; d.Faecher.Add(ww);
        var ma = new Fach { Id = d.NeueId(), Kurzname = "MA", Hintergrund = 0x00FF00 }; d.Faecher.Add(ma);
        var kl = new Klasse { Id = d.NeueId(), Kurzname = "ET13" }; d.Klassen.Add(kl);
        var le = new[] { "HE", "VX" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray(); d.Lehrer.AddRange(le);
        var ra = new Raum { Id = d.NeueId(), Kurzname = "B117" }; d.Raeume.Add(ra);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { kl.Id }, FachraumId = ra.Id } } };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)Fr.DayOfWeek - 1, Stunde = 5, Raeume = { ra.Id } });
        if (weg == 0)
        {
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = Fr, Bis = Fr });
            Vertretungen.Aktualisieren(d, Fr);
            var v = d.Vertretungen.Single(x => x.Datum == Fr && x.Stunde == 5);
            Vertretungen.VertreterSetzen(d, v, le[1].Id);
            v.VertretungsfachId = ma.Id;
        }
        else if (weg == 1)
        {
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = Fr, Bis = Fr });
            Vertretungen.Aktualisieren(d, Fr);
            Stundenaenderung.Ausfuehren(d, u.Id, 0, Fr, new[] { 5 }, new StundeAendernWerte(le[1].Id, ma.Id, new[] { kl.Id }, ra.Id), LehrerKonfliktLoesung.NichtVerplanen);
        }
        else Stundenaenderung.Ausfuehren(d, u.Id, 0, Fr, new[] { 5 }, new StundeAendernWerte(le[1].Id, ma.Id, new[] { kl.Id }, ra.Id), LehrerKonfliktLoesung.NichtVerplanen);
        var p = UntPlanWebExport.PaketErstellen(d, Fr.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        var s = new Stand(p, "");
        if (nur) { var n = UntPlanWebExport.PaketErstellen(d, Fr.AddDays(-4), new WebExportOptionen { TageVoraus = 7, Teile = new() { WebTeile.Vertretungen } }); s = new Stand(p with { Tage = n.Tage, Vertretungen = n.Vertretungen }, ""); }
        foreach (var x in s.Paket.Tage.Single(t => t.Datum == "2026-11-06").Stunden) _aus.WriteLine($"ST {x.Status} U={x.Unterricht} F={x.Fach} L={string.Join(",", x.Lehrer)} alt={string.Join(",", x.LehrerAlt ?? new())} V={x.Vertretung}");
        foreach (var v in s.Paket.Vertretungen) _aus.WriteLine($"VT {v.Id} {v.Art} {v.Lehrer}->{v.Vertreter} F={v.Fach}->{v.NeuesFach}");
        var pl = new Plandienst();
        foreach (var (t, e) in new[] { ("lehrer", "HE"), ("lehrer", "VX"), ("klasse", "ET13"), ("raum", "B117") })
            foreach (var k in pl.Karten(s, Fr, t, e).Where(k => k.Von == 5))
                _aus.WriteLine($"{t} {e}: {k.Status} F={k.Fach} alt={k.FachAlt} farbe={k.Farbe} L={string.Join(",", k.Lehrer.Select(w => w.K + "/" + w.Alt))} lA={k.LehrerAbwesend} M={string.Join(",", k.Marker)} A={k.Art}");
    }
}
