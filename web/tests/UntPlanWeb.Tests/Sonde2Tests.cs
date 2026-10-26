using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;
namespace UntPlanWeb.Tests;
public class Sonde2Tests
{
    private readonly ITestOutputHelper _aus;
    public Sonde2Tests(ITestOutputHelper aus) => _aus = aus;
    private static readonly DateOnly Fr = new(2026, 11, 6);
    // form 0: eine Zeile mit zwei Klassen; 1: zwei Unterrichte (Mitklassen) gleiche Lehrkraft/Fach/Raum; 2: Kopplung zwei Zeilen, gleiche Lehrkraft
    [Theory]
    [InlineData(0, false, false)] [InlineData(0, false, true)]
    [InlineData(1, false, false)] [InlineData(1, true, false)] [InlineData(1, true, true)]
    [InlineData(2, false, false)] [InlineData(2, true, false)] [InlineData(2, true, true)]
    [InlineData(3, false, false)] [InlineData(3, false, true)]
    public void Raum(int form, bool alle, bool nur)
    {
        var d = Demo.NeueDatei("T", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW" }; d.Faecher.Add(ww);
        var kl = new[] { "ET13", "INF13" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray(); d.Klassen.AddRange(kl);
        var le = new[] { "HE", "CS" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray(); d.Lehrer.AddRange(le);
        var ra = new[] { "B117", "R200" }.Select(k => new Raum { Id = d.NeueId(), Kurzname = k }).ToArray(); d.Raeume.AddRange(ra);
        var tag = (int)Fr.DayOfWeek - 1;
        var us = new List<Unterricht>();
        if (form == 0) us.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { kl[0].Id, kl[1].Id }, FachraumId = ra[0].Id } } });
        if (form == 1) foreach (var k in kl) us.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { k.Id }, FachraumId = ra[0].Id } } });
        if (form == 2) us.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = kl.Select(k => new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { k.Id }, FachraumId = ra[0].Id }).ToList() });
        if (form == 3) us.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { kl[0].Id, kl[1].Id }, FachraumId = ra[0].Id }, new Kopplungszeile { LehrerId = le[1].Id, FachId = ww.Id, FachraumId = ra[0].Id } } });
        d.Unterrichte.AddRange(us);
        foreach (var u in us) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = 5, Raeume = u.Zeilen.Select(_ => (int?)ra[0].Id).ToList() });
        foreach (var u in alle ? us : us.Take(1))
            for (var z = 0; z < (alle ? u.Zeilen.Count : 1); z++)
                Raumvertretungen.Zuordnen(d, u.Id, z, Fr, new[] { 5 }, ra[1].Id, false, RaumKollisionsLoesung.MitKollision);
        foreach (var z in Vertretungsplan.Zeilen(d, Fr)) _aus.WriteLine($"VT {z.Id} {z.Art} K={string.Join(",", z.KlassenIds)} {z.Lehrer}->{z.Vertreter} R={z.Raum}->{z.NeuerRaum}");
        var p = UntPlanWebExport.PaketErstellen(d, Fr.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        var s = new Stand(p, "");
        if (nur)
        {
            var n = UntPlanWebExport.PaketErstellen(d, Fr.AddDays(-4), new WebExportOptionen { TageVoraus = 7, Teile = new() { WebTeile.Vertretungen } });
            s = new Stand(p with { Tage = n.Tage, Vertretungen = n.Vertretungen }, "");
        }
        foreach (var x in s.Paket.Tage.Single(t => t.Datum == "2026-11-06").Stunden) _aus.WriteLine($"ST {x.Status} U={x.Unterricht} K={string.Join(",", x.Klassen)} L={string.Join(",", x.Lehrer)} R={string.Join(",", x.Raeume)} V={x.Vertretung}");
        var pl = new Plandienst();
        foreach (var (t, e) in new[] { ("lehrer", "HE"), ("lehrer", "CS"), ("klasse", "ET13"), ("klasse", "INF13"), ("raum", "B117"), ("raum", "R200") })
            foreach (var k in pl.Karten(s, Fr, t, e).Where(k => k.Von == 5))
                _aus.WriteLine($"{t} {e}: {k.Status} L={string.Join(",", k.Lehrer.Select(w => w.K + "/" + w.Alt))} K={string.Join(",", k.Klassen.Select(w => w.K + "/" + w.Alt))}+{string.Join(",", k.Mitklassen.Select(w => w.K + "/" + w.Alt))} R={string.Join(",", k.Raeume.Select(w => w.K + "/" + w.Alt + (w.Neu ? "*" : "")))} M={string.Join(",", k.Marker)} T={k.Text} A={k.Art}");
    }
}
