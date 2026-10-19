using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

public class MarkerErkundungTests
{
    private readonly ITestOutputHelper _aus;
    public MarkerErkundungTests(ITestOutputHelper aus) => _aus = aus;
    private static readonly DateOnly Do = new(2026, 10, 15);

    private static Schuldatei Schule()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        return d;
    }

    private void Dump(string titel, Schuldatei d, params DateOnly[] tage)
    {
        var paket = UntPlanWebExport.PaketErstellen(d, Do.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });
        var s = new Stand(paket, "");
        var p = new Plandienst();
        _aus.WriteLine($"===== {titel}");
        foreach (var v in paket.Vertretungen.Where(v => tage.Any(t => v.Datum == t.ToString("yyyy-MM-dd")) && !v.NurLehrer))
            _aus.WriteLine($"  V{v.Id} {v.Datum} {v.Stunde} art={v.Art} {v.Klassen.FirstOrDefault()} {v.Lehrer}->{v.Vertreter} {v.Fach}->{v.NeuesFach} {v.Raum}->{v.NeuerRaum} von='{v.VertrVon}' nach='{v.Nach}' text='{v.Text}'");
        foreach (var t in paket.Tage.Where(t => tage.Any(x => t.Datum == x.ToString("yyyy-MM-dd"))))
            foreach (var x in t.Stunden.Where(x => x.Status != "regulaer"))
                _aus.WriteLine($"  S {t.Datum} {x.Stunde} u={x.Unterricht} st={x.Status} {x.Fach} kl={string.Join(",", x.Klassen)} le={string.Join(",", x.Lehrer)} ra={string.Join(",", x.Raeume)} v={x.Vertretung} alt={string.Join(",", x.LehrerAlt ?? new())}");
        foreach (var tag in tage)
            foreach (var (typ, el) in paket.Elemente.Klassen.Select(k => ("klasse", k.Kurz)).Concat(paket.Elemente.Lehrer.Select(l => ("lehrer", l.Kurz))).Concat(paket.Elemente.Raeume.Select(r => ("raum", r.Kurz))))
                foreach (var k in p.Karten(s, tag, typ, el).Where(k => k.Status != "regulaer" || k.Marker.Count > 0))
                    _aus.WriteLine($"  {tag:dd.MM} {typ}:{el} {k.Von}-{k.Bis} st={k.Status} art='{k.Art}' fach={k.Fach} kl={string.Join(",", k.Klassen.Select(W))} le={string.Join(",", k.Lehrer.Select(W))} ra={string.Join(",", k.Raeume.Select(W))} u={k.Unterricht} z={k.Zusaetzlich} vl={k.Verlegung} text='{k.Text}' marker=[{string.Join(",", k.Marker)}]");
    }
    private static string W(Wert w) => (w.Neu ? (w.Plus ? "+" : "*") : "") + w.K + (w.Alt is { Length: > 0 } a ? "/" + a : "");

    [Fact]
    public void Erkunden()
    {
        // 1) Verlegung über Tage
        {
            var d = Schule();
            var fr = Do.AddDays(1);
            var tp = Tagesplan.Berechnen(d, Do); var tpf = Tagesplan.Berechnen(d, fr);
            var r = Tagesplan.Regulaer(d, Do).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
            var ziel = Enumerable.Range(1, 8).First(st => Tagesverlegung.Zielstatus(d, tp, r.Stunde, r.Unterricht.Id, null, tpf, st) == UntPlan.Core.Planung.Feldstatus.Frei);
            Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, r.Stunde, r.Unterricht.Id, null, 1, fr, ziel), VerlegeModus.Verlegen);
            Dump($"Verlegung Do {r.Stunde} -> Fr {ziel}", d, Do, fr);
        }
        // 2) Verlegung am selben Tag
        {
            var d = Schule();
            var tp = Tagesplan.Berechnen(d, Do);
            foreach (var r in Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId)))
            {
                var ziel = Enumerable.Range(1, 8).FirstOrDefault(st => st != r.Stunde && Tagesverlegung.Zielstatus(d, tp, r.Stunde, r.Unterricht.Id, null, tp, st) == UntPlan.Core.Planung.Feldstatus.Frei);
                if (ziel == 0) continue;
                Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, r.Stunde, r.Unterricht.Id, null, 1, Do, ziel), VerlegeModus.Verlegen);
                Dump($"Verlegung Do {r.Stunde} -> Do {ziel}", d, Do);
                break;
            }
        }
        // 3) Lehrkraft krank + Automatik
        {
            var d = Schule();
            var ix = new DatenIndex(d);
            var lehrer = Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue).GroupBy(x => x.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = Do, Bis = Do });
            Vertretungen.Aktualisieren(d, Do);
            Dump($"krank {ix.KLehrer(lehrer)} offen", d, Do);
            Vertretungsautomatik.Ausfuehren(d, Do, Do, AutomatikMethode.BesterVorschlag);
            Dump($"krank {ix.KLehrer(lehrer)} automatik", d, Do);
        }
        // 4) Sondereinsatz einer Klasse mit Unterricht / Lehrkraft mit Unterricht
        {
            var d = Schule();
            var r = Tagesplan.Regulaer(d, Do).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.KlassenIds.Count == 1);
            var frei = d.Lehrer.First(l => !Tagesplan.Regulaer(d, Do).Any(x => x.Stunde == r.Stunde && x.LehrerId == l.Id));
            Vertretungen.Sondereinsatz(d, Do, r.Stunde, r.Stunde, r.KlassenIds.ToList(), new[] { frei.Id, r.LehrerId!.Value }, r.FachId, r.RaumId, "Projekt");
            Dump("Sondereinsatz Klasse+Lehrer mit Unterricht", d, Do);
        }
        // 5) Raumänderung, Klausur, Veranstaltung
        {
            var d = Schule();
            var regel = Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.RaumId.HasValue).ToList();
            var r = regel[0];
            var tp = Tagesplan.Berechnen(d, Do);
            var neu = d.Raeume.First(x => !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
            Vertretungen.RaumAendern(d, r.Unterricht.Id, r.ZeilenIndex, Do, r.Stunde, neu.Id, false);
            var r2 = regel.First(x => x.Stunde != r.Stunde && !x.KlassenIds.Intersect(r.KlassenIds).Any());
            var andere = d.Lehrer.First(l => l.Id != r2.LehrerId);
            d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "Mathe-Arbeit", Datum = Do, StundeVon = r2.Stunde, StundeBis = r2.Stunde, KlassenIds = r2.KlassenIds.ToList(), LehrerIds = new() { andere.Id }, RaumIds = new() { neu.Id }, UnterrichtId = r2.Unterricht.Id });
            var r3 = regel.First(x => x.Stunde != r.Stunde && x.Stunde != r2.Stunde && !x.KlassenIds.Intersect(r.KlassenIds.Concat(r2.KlassenIds)).Any());
            d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = r3.Stunde, StundeBis = r3.Stunde, KlassenIds = r3.KlassenIds.ToList(), LehrerIds = new() { r3.LehrerId!.Value }, Text = "Exkursion" });
            Vertretungen.Aktualisieren(d, Do);
            Dump("Raum/Klausur/Veranstaltung", d, Do);
        }
        // 6) Kopplung: eine Zeile verlegt
        {
            var d = Schule();
            var tp = Tagesplan.Berechnen(d, Do);
            var r = Tagesplan.Regulaer(d, Do).FirstOrDefault(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count > 1);
            if (r != null)
            {
                var ziel = Enumerable.Range(1, 8).FirstOrDefault(st => st != r.Stunde && Tagesverlegung.Zielstatus(d, tp, r.Stunde, r.Unterricht.Id, null, tp, st) == UntPlan.Core.Planung.Feldstatus.Frei);
                if (ziel > 0)
                {
                    Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, r.Stunde, r.Unterricht.Id, null, 1, Do, ziel), VerlegeModus.Verlegen);
                    Dump($"Kopplung verlegt {r.Stunde}->{ziel}", d, Do);
                }
            }
        }
        // 7) Tausch
        {
            var d = Schule();
            var regel = Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.KlassenIds.Count == 1).ToList();
            var a = regel[0];
            var b = regel.First(x => x.KlassenIds.SequenceEqual(a.KlassenIds) && x.Stunde != a.Stunde && x.Unterricht.Id != a.Unterricht.Id);
            Vertretungen.Tauschen(d, a.Unterricht.Id, Do, a.Stunde, b.Unterricht.Id, Do, b.Stunde);
            Dump($"Tausch {a.Stunde}<->{b.Stunde}", d, Do);
        }
    }
}
