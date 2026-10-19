using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Symbole der Stundenkarten (Verlegung, Zusatzstunde) nur an den richtigen Stellen, keine automatischen Verlegungstexte und
/// Veranstaltungen ohne abwesende Lehrkräfte – Ende zu Ende: wie im Desktop angelegt → echter Export → Plandienst bzw. /api/app/plan.
/// </summary>
public class PlanSymboleTests
{
    private static readonly DateOnly Do = new(2026, 10, 15);
    private static readonly DateOnly Fr = Do.AddDays(1);

    private static Schuldatei Schule()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        return d;
    }

    private static WebPaket Paket(Schuldatei d) => UntPlanWebExport.PaketErstellen(d, Do.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });

    private static List<(string Typ, string Element)> Elemente(WebPaket p) =>
        p.Elemente.Klassen.Select(k => ("klasse", k.Kurz)).Concat(p.Elemente.Lehrer.Select(l => ("lehrer", l.Kurz))).Concat(p.Elemente.Raeume.Select(r => ("raum", r.Kurz))).ToList();

    private static List<Karte> Karten(Stand s, string typ, string element, DateOnly d, int stunde) =>
        new Plandienst().Karten(s, d, typ, element).Where(k => k.Von <= stunde && k.Bis >= stunde).ToList();

    private static Vertretungsplandienst Vertretungsplan(Plandienst plan) =>
        new(plan, new UntPlanWeb.Daten.Db(Directory.CreateTempSubdirectory("untplanweb-test-").FullName));

    private static bool Verlegungssymbol(Karte k) => k.Marker.Any(m => m.StartsWith("verlegung"));

    private static RegelStunde Normale(Schuldatei d, DateOnly tag, Func<RegelStunde, bool>? und = null) =>
        Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.KlassenIds.Count == 1 && x.RaumId.HasValue
                                              && !Vertretungen.IstBereitschaftsfach(d, x.FachId) && (und is null || und(x)));

    // ------------------------------------------------------------------ Verlegungssymbol

    [Fact]
    public void Verlegung_ueber_Tage_Symbol_nur_an_Ursprung_und_Ziel()
    {
        var d = Schule();
        var tp = Tagesplan.Berechnen(d, Do);
        var tpf = Tagesplan.Berechnen(d, Fr);
        var (r, ziel) = Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.KlassenIds.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId))
            .SelectMany(x => Enumerable.Range(1, 8).Where(st => UntPlan.Core.Planung.Feldstatus.Frei == Tagesverlegung.Zielstatus(d, tp, x.Stunde, x.Unterricht.Id, null, tpf, st)
                && !tpf.Stunde(st).Any(e => e.Unterricht?.Id == x.Unterricht.Id)).Select(st => (x, st))).First();
        Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, r.Stunde, r.Unterricht.Id, null, 1, Fr, ziel), VerlegeModus.Verlegen);
        var p = Paket(d);
        var s = new Stand(p, "");
        var ix = new DatenIndex(d);
        foreach (var (typ, el) in new[] { ("klasse", ix.K(ElementTyp.Klasse, r.KlassenIds[0])), ("lehrer", ix.KLehrer(r.LehrerId)) })
        {
            var von = Assert.Single(Karten(s, typ, el, Do, r.Stunde), k => k.Unterricht == r.Unterricht.Id);
            Assert.Equal(new[] { "verlegungVon" }, von.Marker);
            var nach = Assert.Single(Karten(s, typ, el, Fr, ziel), k => k.Unterricht == r.Unterricht.Id);
            Assert.Equal("nach", nach.Verlegung);
            Assert.Equal("verlegungNach", nach.Marker[0]);
            Assert.NotNull(nach.VerlegungVon);
            // sonst trägt keine Karte der Woche ein Verlegungssymbol
            var plan = new Plandienst();
            Assert.Equal(2, Enumerable.Range(0, 5).SelectMany(i => plan.Karten(s, Do.AddDays(i - 3), typ, el)).Count(Verlegungssymbol));
        }
    }

    [Fact]
    public void Verlegung_auf_eine_Stunde_desselben_Unterrichts_wird_nicht_mit_der_regulaeren_Stunde_vermischt()
    {
        var d = Schule();
        var tp = Tagesplan.Berechnen(d, Do);
        var tpf = Tagesplan.Berechnen(d, Fr);
        // Unterricht, der freitags in einer Stunde regulär liegt, in die er vom Donnerstag aus verlegt werden kann
        foreach (var r in Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && x.KlassenIds.Count == 1))
        {
            var ziel = Enumerable.Range(1, 8).FirstOrDefault(st => tpf.Stunde(st).Any(e => e.Unterricht?.Id == r.Unterricht.Id)
                && UntPlan.Core.Planung.Feldstatus.Frei == Tagesverlegung.Zielstatus(d, tp, r.Stunde, r.Unterricht.Id, null, tpf, st));
            if (ziel == 0) continue;
            Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, r.Stunde, r.Unterricht.Id, null, 1, Fr, ziel), VerlegeModus.Verlegen);
            var s = new Stand(Paket(d), "");
            var kl = new DatenIndex(d).K(ElementTyp.Klasse, r.KlassenIds[0]);
            var karten = Karten(s, "klasse", kl, Fr, ziel).Where(k => k.Unterricht == r.Unterricht.Id).ToList();
            Assert.Equal(2, karten.Count);
            Assert.Single(karten, k => k.Verlegung == "nach");
            var regulaer = Assert.Single(karten, k => k.Verlegung is null);
            Assert.DoesNotContain(regulaer.Marker, m => m.StartsWith("verlegung"));
            Assert.False(regulaer.Zusaetzlich);
            return;
        }
    }

    [Fact]
    public void Statt_Vertretung_und_Vertretung_im_regulaeren_Unterricht_ohne_Verlegungssymbol()
    {
        var d = Schule();
        var lehrer = Tagesplan.Regulaer(d, Do).Where(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, x.FachId))
            .GroupBy(x => x.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = Do, Bis = Do });
        Vertretungen.Aktualisieren(d, Do);
        Vertretungsautomatik.Ausfuehren(d, Do, Do, AutomatikMethode.BesterVorschlag);
        // eine der Vertretungen als Statt-Vertretung (vertretende Lehrkraft ist freigesetzt), eine noch offen („???“)
        var vertretungen = d.Vertretungen.Where(v => v.Datum == Do && v.LehrerId == lehrer && v.UnterrichtId.HasValue).OrderBy(v => v.Stunde).ToList();
        Assert.True(vertretungen.Count >= 2);
        vertretungen[0].Art = VertretungsArt.StattVertretung;
        vertretungen[^1].VertreterId = null;
        vertretungen[^1].Art = VertretungsArt.Vertretung;
        var p = Paket(d);
        var s = new Stand(p, "");
        var plan = new Plandienst();
        var alle = Elemente(p).SelectMany(e => plan.Karten(s, Do, e.Typ, e.Element)).ToList();
        Assert.Contains(alle, k => k.Art.StartsWith("Statt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(alle, k => k.Status == "offen");
        Assert.DoesNotContain(alle, Verlegungssymbol);
        Assert.DoesNotContain(alle, k => k.Marker.Contains("plus"));
        Assert.All(alle.Where(k => k.Status is "geaendert" or "offen"), k => Assert.Contains("aenderung", k.Marker));
    }

    [Fact]
    public void Raumaenderung_Pruefung_Veranstaltung_ohne_Verlegungs_und_Plussymbol()
    {
        var d = Schule();
        var r = Normale(d, Do);
        var tp = Tagesplan.Berechnen(d, Do);
        var neu = d.Raeume.First(x => !x.Ignorieren && !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        Vertretungen.RaumAendern(d, r.Unterricht.Id, r.ZeilenIndex, Do, r.Stunde, neu.Id, false);
        var r2 = Normale(d, Do, x => x.Stunde != r.Stunde && !x.KlassenIds.Intersect(r.KlassenIds).Any());
        d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "Mathe-Arbeit", Datum = Do, StundeVon = r2.Stunde, StundeBis = r2.Stunde, KlassenIds = r2.KlassenIds.ToList(),
            LehrerIds = new() { d.Lehrer.First(l => l.Id != r2.LehrerId).Id }, RaumIds = new() { neu.Id }, UnterrichtId = r2.Unterricht.Id });
        var r3 = Normale(d, Do, x => x.Stunde != r.Stunde && x.Stunde != r2.Stunde && !x.KlassenIds.Intersect(r.KlassenIds.Concat(r2.KlassenIds)).Any());
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = r3.Stunde, StundeBis = r3.Stunde, KlassenIds = r3.KlassenIds.ToList(),
            LehrerIds = new() { r3.LehrerId!.Value }, Text = "Exkursion" });
        Vertretungen.Aktualisieren(d, Do);
        var p = Paket(d);
        var s = new Stand(p, "");
        var plan = new Plandienst();
        var alle = Elemente(p).SelectMany(e => plan.Karten(s, Do, e.Typ, e.Element)).ToList();
        Assert.Contains(alle, k => k.Status == "klausur");
        Assert.Contains(alle, k => k.Status == "veranstaltung");
        Assert.Contains(alle, k => k.Raeume.Any(w => w.Alt is { Length: > 0 }) && k.Status == "geaendert");
        Assert.DoesNotContain(alle, Verlegungssymbol);
        Assert.DoesNotContain(alle, k => k.Marker.Contains("plus"));
        Assert.All(alle.Where(k => k.Status == "veranstaltung"), k => Assert.Equal(new[] { "aenderung" }, k.Marker));
    }

    // ------------------------------------------------------------------ Prüfung im Raumplan wie im Klassenplan

    [Fact]
    public void Pruefung_im_Raumplan_wie_im_Klassenplan_ohne_freie_Angaben()
    {
        var d = Schule();
        // Klasse mit genau einer Stunde zu dieser Zeit; Prüfung in einem anderen Raum, Aufsicht durch eine andere Lehrkraft
        var r = Normale(d, Do, x => Tagesplan.Regulaer(d, Do).Count(y => y.Stunde == x.Stunde && y.KlassenIds.Intersect(x.KlassenIds).Any()) == 1);
        var tp = Tagesplan.Berechnen(d, Do);
        var raum = d.Raeume.First(x => !x.Ignorieren && !tp.Stunde(r.Stunde).Any(e => e.RaumIds.Contains(x.Id)));
        var aufsicht = d.Lehrer.First(l => !l.Ignorieren && !tp.Stunde(r.Stunde).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id)));
        d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "Mathe-Arbeit", Text = "Klassenarbeit: Bruchrechnung – Notenschema: Noten 1–6 – Teilnehmende: Max Muster",
            Datum = Do, StundeVon = r.Stunde, StundeBis = r.Stunde, KlassenIds = r.KlassenIds.ToList(), LehrerIds = new() { aufsicht.Id }, RaumIds = new() { raum.Id },
            UnterrichtId = r.Unterricht.Id });
        Vertretungen.Aktualisieren(d, Do);
        var p = Paket(d);
        Assert.Contains(p.Vertretungen, v => v.Text.Contains("Teilnehmende"));
        var s = new Stand(p, "");
        var ix = new DatenIndex(d);
        var fach = ix.KFach(r.FachId);
        var inKlasse = Assert.Single(Karten(s, "klasse", ix.K(ElementTyp.Klasse, r.KlassenIds[0]), Do, r.Stunde));
        var imRaum = Assert.Single(Karten(s, "raum", ix.KRaum(raum.Id), Do, r.Stunde));
        var beiAufsicht = Assert.Single(Karten(s, "lehrer", ix.KLehrer(aufsicht.Id), Do, r.Stunde));
        foreach (var k in new[] { inKlasse, imRaum, beiAufsicht })
        {
            Assert.Equal("klausur", k.Status);
            Assert.Equal("Mathe-Arbeit", k.Pruefung);
            Assert.Equal(fach, k.Fach);
            Assert.Equal(new[] { "pruefung" }, k.Marker.Take(1));
            Assert.Contains(k.Klassen, w => w.K == ix.K(ElementTyp.Klasse, r.KlassenIds[0]));
            Assert.Contains(k.Raeume, w => w.K == ix.KRaum(raum.Id));
        }
        Assert.Contains(imRaum.Lehrer, w => w.K == ix.KLehrer(aufsicht.Id));
        // Web: Bezeichnung bzw. Info ist der Name der Prüfung, nie der freie Text
        var js = File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js"));
        Assert.Contains("k.status === 'klausur' && k.pruefung", js);
        Assert.Contains("`Prüfung: ${k.pruefung || k.text}`", js);
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    // ------------------------------------------------------------------ Zusatzstunde („+“)

    [Fact]
    public void Sondereinsatz_anstelle_einer_Stunde_ist_keine_Zusatzstunde_nur_fuer_freie_Lehrkraefte()
    {
        var d = Schule();
        var r = Normale(d, Do);
        var frei = d.Lehrer.First(l => !Tagesplan.Berechnen(d, Do).Stunde(r.Stunde).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id)));
        Vertretungen.Sondereinsatz(d, Do, r.Stunde, r.Stunde, r.KlassenIds.ToList(), new[] { frei.Id, r.LehrerId!.Value }, r.FachId, r.RaumId, "Projekt");
        var s = new Stand(Paket(d), "");
        var ix = new DatenIndex(d);
        Karte Einsatz(string typ, string el) => Assert.Single(Karten(s, typ, el, Do, r.Stunde), k => k.Unterricht is null && k.Status == "geaendert");
        // Klasse, Lehrkraft der entfallenen Stunde und Raum: die reguläre Stunde entfällt dafür – Änderung, kein „+“
        foreach (var (typ, el) in new[] { ("klasse", ix.K(ElementTyp.Klasse, r.KlassenIds[0])), ("lehrer", ix.KLehrer(r.LehrerId)), ("raum", ix.KRaum(r.RaumId)) })
        {
            var k = Einsatz(typ, el);
            Assert.DoesNotContain("plus", k.Marker);
            Assert.Contains("aenderung", k.Marker);
            Assert.False(k.Zusaetzlich);
        }
        // freie Lehrkraft: echte Zusatzstunde
        var z = Einsatz("lehrer", ix.KLehrer(frei.Id));
        Assert.Equal("plus", z.Marker[0]);
        Assert.True(z.Zusaetzlich);
    }

    [Fact]
    public void Sondereinsatz_ohne_regulaere_Stunde_bleibt_Zusatzstunde()
    {
        var d = Schule();
        var tp = Tagesplan.Berechnen(d, Do);
        var (stunde, klasse) = Enumerable.Range(1, 6).SelectMany(st => d.Klassen.Where(k => !tp.Stunde(st).Any(e => e.Findetstatt && e.KlassenIds.Contains(k.Id))).Select(k => (st, k))).First();
        var lehrer = d.Lehrer.Where(l => !tp.Stunde(stunde).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id))).Take(2).ToList();
        var fach = d.Faecher.First(f => !Vertretungen.IstBereitschaftsfach(d, f.Id)).Id;
        Vertretungen.Sondereinsatz(d, Do, stunde, stunde, new[] { klasse.Id }, lehrer.Select(l => l.Id).ToList(), fach, null, "AG");
        var s = new Stand(Paket(d), "");
        var ix = new DatenIndex(d);
        foreach (var (typ, el) in new[] { ("klasse", ix.K(ElementTyp.Klasse, klasse.Id)) }.Concat(lehrer.Select(l => ("lehrer", ix.KLehrer(l.Id)))))
        {
            var k = Assert.Single(Karten(s, typ, el, Do, stunde), x => x.Unterricht is null);
            Assert.True(k.Zusaetzlich);
            Assert.Equal("plus", k.Marker[0]);
            Assert.Null(k.Verlegung);
        }
    }

    [Fact]
    public void Sondereinsatz_zu_einer_regulaeren_Stunde_in_der_Kopplung_ist_keine_Zusatzstunde()
    {
        // zweite Lehrkraft per Sondereinsatz in derselben Stunde (gleicher Unterricht, Fach und Raum): eine gemeinsame Karte, Änderung statt „+“
        var paket = Testserver.Beispielpaket();
        var tag = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-12")!;
        var stunden = tag["stunden"]!.AsArray();
        var basis = stunden.First(x => x!["unterricht"] is not null && x["klassen"]!.AsArray().Count == 1 && x["lehrer"]!.AsArray().Count == 1 && x["raeume"]!.AsArray().Count == 1
                                       && x["fach"]!.GetValue<string>().Length > 0)!;
        var zusatz = basis.DeepClone().AsObject();
        zusatz["status"] = "zusaetzlich";
        zusatz["lehrer"] = new JsonArray("ZZZ");
        zusatz["vertretung"] = 9901;
        stunden.Add(zusatz);
        paket["vertretungen"]!.AsArray().Add(new JsonObject
        {
            ["id"] = 9901, ["datum"] = "2026-10-12", ["stunde"] = basis["stunde"]!.GetValue<int>(), ["art"] = "Sondereinsatz", ["klassen"] = basis["klassen"]!.DeepClone(),
            ["lehrer"] = "", ["vertreter"] = "ZZZ", ["fach"] = basis["fach"]!.GetValue<string>(), ["neuesFach"] = basis["fach"]!.GetValue<string>(), ["raum"] = "",
            ["neuerRaum"] = basis["raeume"]![0]!.GetValue<string>(), ["vertrVon"] = "", ["nach"] = "", ["text"] = "wegen Sondereinsatz", ["nurLehrer"] = false,
        });
        var s = new Stand(JsonSerializer.Deserialize<WebPaket>(paket.ToJsonString(), UntPlanWebSchema.Json)!, "");
        var kl = basis["klassen"]![0]!.GetValue<string>();
        var k = Assert.Single(Karten(s, "klasse", kl, new DateOnly(2026, 10, 12), basis["stunde"]!.GetValue<int>()), x => x.Unterricht == basis["unterricht"]!.GetValue<int>());
        Assert.Contains(k.Lehrer, w => w.K == "ZZZ");
        Assert.Contains(k.Lehrer, w => w.K == basis["lehrer"]![0]!.GetValue<string>());
        Assert.False(k.Zusaetzlich);
        Assert.DoesNotContain("plus", k.Marker);
        Assert.Contains("aenderung", k.Marker);
        Assert.Null(k.Verlegung);
    }

    // ------------------------------------------------------------------ keine automatischen Verlegungstexte

    [Fact]
    public void Automatische_Verlegungstexte_entfallen()
    {
        Assert.Equal("", Plandienst.TextNormal("Vertretung durch Verlegung"));
        Assert.Equal("", Plandienst.TextNormal("Vertretung durch Verlegung erzeugen"));
        Assert.Equal("", Plandienst.TextNormal("Vorziehung"));
        Assert.Equal("Bitte Buch mitbringen", Plandienst.TextNormal("Vertretung durch Verlegung · Bitte Buch mitbringen"));
        Assert.Equal("Verlegung wegen Ausflug", Plandienst.TextNormal("Verlegung wegen Ausflug"));
    }

    [Fact]
    public void Verlegung_in_die_Luecke_einer_kranken_Lehrkraft_ohne_Standardtext_auf_Karten_und_im_Vertretungsplan()
    {
        var d = Schule();
        var normale = Tagesplan.Regulaer(d, Do).Where(r => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1
                                                        && string.IsNullOrEmpty(r.Unterricht.Band) && r.KlassenIds.Count == 1).ToList();
        var (krank, verlegt) = normale.SelectMany(a => normale.Where(b => b.Stunde > a.Stunde && b.KlassenIds.SequenceEqual(a.KlassenIds) && b.LehrerId != a.LehrerId
                && !Tagesplan.Regulaer(d, Do).Any(x => x.Stunde == a.Stunde && x.LehrerId == b.LehrerId)).Select(b => (a, b))).First();
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = krank.LehrerId!.Value, Von = Do, Bis = Do });
        Vertretungen.Aktualisieren(d, Do);
        Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Do, verlegt.Stunde, verlegt.Unterricht.Id, null, 1, Do, krank.Stunde), VerlegeModus.Verlegen);
        var p = Paket(d);
        Assert.DoesNotContain(p.Vertretungen, v => v.Text == "Vertretung durch Verlegung");
        var s = new Stand(p, "");
        var plan = new Plandienst();
        var alle = Elemente(p).SelectMany(e => plan.Karten(s, Do, e.Typ, e.Element)).ToList();
        Assert.DoesNotContain(alle, k => k.Text.Contains("durch Verlegung", StringComparison.OrdinalIgnoreCase));
        var vp = Vertretungsplan(plan);
        foreach (var kollegium in new[] { true, false })
            Assert.DoesNotContain(vp.Tag(s, Do, kollegium).Zeilen, z => z.Text.Contains("durch Verlegung", StringComparison.OrdinalIgnoreCase));
        // die Klasse: Ursprung (von) und Ziel (nach) der Verlegung, die Stunde der kranken Lehrkraft ist ein schlichter Entfall
        var kl = new DatenIndex(d).K(ElementTyp.Klasse, krank.KlassenIds[0]);
        Assert.Contains(Karten(s, "klasse", kl, Do, krank.Stunde), k => k.Verlegung == "nach" && k.Unterricht == verlegt.Unterricht.Id);
        var luecke = Assert.Single(Karten(s, "klasse", kl, Do, krank.Stunde), k => k.Unterricht == krank.Unterricht.Id);
        Assert.Equal(new[] { "entfall" }, luecke.Marker);
        Assert.Contains(Karten(s, "klasse", kl, Do, verlegt.Stunde), k => k.Verlegung == "von");
    }

    // ------------------------------------------------------------------ Veranstaltung und abwesende Lehrkraft

    private static (Schuldatei D, Lehrer Krank, Lehrer Gesund, Klasse Klasse, int Von, int Bis) VeranstaltungMitKranker(int? krankVon = null, int? krankBis = null)
    {
        var d = Schule();
        var tp = Tagesplan.Berechnen(d, Do);
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren && !tp.Stunde(3).Concat(tp.Stunde(4)).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id))).Take(2).ToList();
        var klasse = d.Klassen.First(k => !k.Ignorieren);
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = 3, StundeBis = 4, KlassenIds = { klasse.Id }, LehrerIds = lehrer.Select(l => l.Id).ToList(), Text = "Museumsbesuch" });
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer[0].Id, Von = Do, Bis = Do, StundeVon = krankVon ?? 1, StundeBis = krankBis ?? 99 });
        Vertretungen.Aktualisieren(d, Do);
        return (d, lehrer[0], lehrer[1], klasse, 3, 4);
    }

    [Fact]
    public void Veranstaltung_verschwindet_beim_kranken_Lehrer_und_er_aus_der_Veranstaltung()
    {
        var (d, krank, gesund, klasse, von, bis) = VeranstaltungMitKranker();
        var ix = new DatenIndex(d);
        var (k, g, kl) = (ix.KLehrer(krank.Id), ix.KLehrer(gesund.Id), ix.K(ElementTyp.Klasse, klasse.Id));
        var p = Paket(d);
        var s = new Stand(p, "");
        var plan = new Plandienst();
        for (var st = von; st <= bis; st++)
        {
            Assert.DoesNotContain(Karten(s, "lehrer", k, Do, st), x => x.Status == "veranstaltung");
            var bei = Assert.Single(Karten(s, "lehrer", g, Do, st), x => x.Status == "veranstaltung");
            Assert.DoesNotContain(bei.Lehrer, w => w.K == k);
            Assert.Contains(bei.Lehrer, w => w.K == g);
            var inKlasse = Assert.Single(Karten(s, "klasse", kl, Do, st), x => x.Status == "veranstaltung");
            Assert.DoesNotContain(inKlasse.Lehrer, w => w.K == k);
        }
        // Tageskopf (Veranstaltungsleiste, Tagesansicht): nicht im Plan der kranken Lehrkraft, nicht in der Teilnehmerliste
        Assert.DoesNotContain(plan.Ereignisse(s, Do, "lehrer", k), e => e.Titel == "Museumsbesuch");
        Assert.DoesNotContain(plan.Ereignisse(s, Do, "klasse", kl).Single(e => e.Titel == "Museumsbesuch").Lehrer, x => x == k);
        Assert.Contains(g, plan.Ereignisse(s, Do, "lehrer", g).Single(e => e.Titel == "Museumsbesuch").Lehrer);
        Assert.DoesNotContain(plan.Tag(s, "lehrer", Do, true).Zeilen.Where(z => z.Kurz == k).SelectMany(z => z.Karten), x => x.Status == "veranstaltung");
        // Vertretungsplan (Kollegium): Veranstaltungszeile ohne die kranke Lehrkraft
        var vp = Vertretungsplan(plan).Tag(s, Do, true);
        Assert.DoesNotContain(vp.Zeilen, z => z.Art.StartsWith("Veranst") && z.Lehrer.Split(',').Contains(k));
    }

    [Fact]
    public void Veranstaltung_nur_in_den_Stunden_der_Abwesenheit_ohne_die_Lehrkraft()
    {
        var (d, krank, gesund, klasse, von, bis) = VeranstaltungMitKranker(1, 3);
        var k = new DatenIndex(d).KLehrer(krank.Id);
        var s = new Stand(Paket(d), "");
        Assert.DoesNotContain(Karten(s, "lehrer", k, Do, 3), x => x.Status == "veranstaltung");
        Assert.Single(Karten(s, "lehrer", k, Do, 4), x => x.Status == "veranstaltung");
        // Teilnahme in Stunde 4: die Veranstaltung bleibt in seinem Tageskopf
        Assert.Contains(new Plandienst().Ereignisse(s, Do, "lehrer", k), e => e.Titel == "Museumsbesuch");
    }

    [Fact]
    public async Task In_UntPlanWeb_gemeldete_Abwesenheit_nimmt_die_Lehrkraft_aus_der_Veranstaltung_in_der_App_API()
    {
        await using var server = await Testserver.StartenAsync(mitPaket: false);
        var d = Schule();
        var tp = Tagesplan.Berechnen(d, Do);
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren && !tp.Stunde(3).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id))).Take(2).ToList();
        var klasse = d.Klassen.First(k => !k.Ignorieren);
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = 3, StundeBis = 3, KlassenIds = { klasse.Id }, LehrerIds = lehrer.Select(l => l.Id).ToList(), Text = "Museumsbesuch" });
        Vertretungen.Aktualisieren(d, Do);
        var p = new PortalEinstellungen { Adresse = server.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, Paket(d) with { Revision = 1 })).Ok);
        var ix = new DatenIndex(d);
        var (k, g, kl) = (ix.KLehrer(lehrer[0].Id), ix.KLehrer(lehrer[1].Id), ix.K(ElementTyp.Klasse, klasse.Id));
        var c = await server.AlsAsync(k.ToLowerInvariant(), "lehrer", k);
        async Task<List<JsonNode>> Veranstaltungen(string typ, string el) =>
            (await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(el)}&datum={Do:yyyy-MM-dd}"))["karten"]!.AsArray()
                .Select(x => x!).Where(x => x["datum"]!.GetValue<string>() == $"{Do:yyyy-MM-dd}" && x["status"]!.GetValue<string>() == "veranstaltung").ToList();
        static List<string> Lehrer(JsonNode x) => x["lehrer"]!.AsArray().Select(w => w!["k"]!.GetValue<string>()).ToList();
        Assert.Single(await Veranstaltungen("lehrer", k));
        Assert.Contains(k, Lehrer((await Veranstaltungen("klasse", kl)).Single()));

        // Lehrkraft meldet sich in UntPlanWeb krank (noch nicht von UntPlan übernommen)
        var antwort = await c.PostAsJsonAsync("/api/app/meldungen/abwesenheit", new { von = Do, bis = Do, grund = "Krankheit" });
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Empty(await Veranstaltungen("lehrer", k));
        Assert.DoesNotContain(k, Lehrer((await Veranstaltungen("klasse", kl)).Single()));
        Assert.Contains(g, Lehrer((await Veranstaltungen("lehrer", g)).Single()));
        // Meldung zurückgezogen: wieder dabei
        var id = (await Testserver.HolenAsync(c, "/api/app/meldungen")).AsArray()[0]!["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/app/meldungen/{id}")).StatusCode);
        Assert.Single(await Veranstaltungen("lehrer", k));
    }
}
