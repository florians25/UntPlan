using System.Net.Http.Json;
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
/// Raumänderungen aller Art Ende zu Ende: echte Vertretungszeilen aus UntPlan (Raumvertretung, Raum hinzugefügt, Raum gelöscht,
/// gesperrter bzw. verdrängter Raum, Raumtausch, Vertretung mit anderem Raum, Kopplung, Doppelstunde, Unterricht geändert,
/// Raumänderung aus UntPlanWeb) → Export → UntPlanWeb → Karten der App-API (raeume: k/alt/neu/plus) in Klassen-, Lehrer- und Raumplan.
/// Regel: Der neue Raum steht immer als grünes Label (neu, mit altem Raum bzw. als „+R“), ein entfernter bzw. offener Raum
/// durchgestrichen im grünen Label ({ k: "", alt: "R1", neu: true }).
/// </summary>
public class RaumaenderungenTests
{
    private readonly ITestOutputHelper _aus;
    public RaumaenderungenTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Montag = new(2026, 10, 12);

    /// <summary>Eine Variante: Stunde(n) eines Unterrichts und die erwarteten Raumwerte der Karte.</summary>
    private sealed record Fall(string Name, DateOnly Tag, int Stunde, int Unterricht, string Klasse, string Lehrer, List<(string K, string? Alt, bool Plus)> Erwartet,
        string? Raumplan = null, string Status = "geaendert");

    private sealed class Aufbau
    {
        public readonly Schuldatei D;
        public readonly DatenIndex Ix;
        public readonly HashSet<(DateOnly, int)> Belegt = new();
        public Aufbau(Schuldatei d) { D = d; Ix = new DatenIndex(d); }
        public string R(int? id) => id is int i ? Ix.KRaum(i) : "";
        public string L(int? id) => id is int i ? Ix.KLehrer(i) : "";
        public string K(int id) => Ix.K(ElementTyp.Klasse, id);

        /// <summary>Reguläre Einzelstunde (eine Lehrkraft, ein Raum, eine Klasse, keine Kopplung) zu einer noch freien Tag/Stunde.</summary>
        public TagesEintrag Stunde(Func<TagesEintrag, Tagesplan, bool>? passt = null)
        {
            for (var i = 0; i < 5; i++)
            {
                var t = Montag.AddDays(i);
                var tp = Tagesplan.Berechnen(D, t);
                var e = tp.Eintraege.FirstOrDefault(e => e.Regel != null && e.Status == TagesStatus.Regulaer && e.Stunde <= 8 && !Belegt.Contains((t, e.Stunde))
                    && e.RaumIds.Count == 1 && e.LehrerIds.Count == 1 && e.KlassenIds.Count == 1 && e.Unterricht!.Zeilen.Count == 1
                    && !Vertretungen.IstBereitschaftsfach(D, e.FachId) && FreierRaum(tp, e.Stunde) != null && (passt?.Invoke(e, tp) ?? true));
                if (e is null) continue;
                Belegt.Add((t, e.Stunde));
                return e;
            }
            throw new InvalidOperationException("keine passende Stunde");
        }

        public DateOnly TagVon(TagesEintrag e) => Enumerable.Range(0, 5).Select(i => Montag.AddDays(i))
            .First(t => Tagesplan.Berechnen(D, t).Eintraege.Any(x => x.Regel?.Planstunde == e.Regel?.Planstunde));

        public Raum? FreierRaum(Tagesplan tp, int stunde, params int[] ausser) =>
            D.Raeume.FirstOrDefault(r => !r.Ignorieren && !ausser.Contains(r.Id) && !tp.Stunde(stunde).Any(x => x.RaumIds.Contains(r.Id))
                                        && !Absenzen.RaumGesperrt(D, r.Id, tp.Datum, stunde));
    }

    private static (string, string?, bool) Neu(string k, string alt) => (k, alt, false);
    private static (string, string?, bool) Plus(string k) => (k, null, true);
    private static (string, string?, bool) Weg(string alt) => ("", alt, false);
    private static (string, string?, bool) Gleich(string k) => (k, null, false);

    [Fact]
    public async Task Alle_Raumaenderungen_zeigen_den_neuen_Raum_als_Label_in_allen_Plaenen()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var a = new Aufbau(d);
        var faelle = new List<Fall>();

        Fall Neuer(string name, TagesEintrag e, List<(string, string?, bool)> erw, string? raumplan, string status = "geaendert")
        {
            var t = a.TagVon(e);
            return new Fall(name, t, e.Stunde, e.Unterricht!.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), erw, raumplan, status);
        }

        // 1) Raumvertretung (Raum zuordnen) in einen freien Raum
        {
            var e = a.Stunde(); var t = a.TagVon(e); var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Raumvertretung", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
        }
        // 2) Raum hinzugefügt (zusätzlicher Raum)
        {
            var e = a.Stunde(); var t = a.TagVon(e); var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, neu, true, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Raum hinzugefügt", e, new() { Gleich(a.R(alt)), Plus(a.R(neu)) }, a.R(neu)));
        }
        // 3) Raum gelöscht (Stunde ohne Raum)
        {
            var e = a.Stunde(); var t = a.TagVon(e);
            var alt = e.RaumIds[0];
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, null, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Raum gelöscht", e, new() { Weg(a.R(alt)) }, null));
        }
        // 4) Raum gesperrt (Raumabsenz) → offene Raumvertretung
        {
            var e = a.Stunde(); var t = a.TagVon(e);
            var alt = e.RaumIds[0];
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = alt, Von = t, Bis = t, StundeVon = e.Stunde, StundeBis = e.Stunde });
            Vertretungen.Aktualisieren(d, t);
            faelle.Add(Neuer("Raum gesperrt (offene Raumvertretung)", e, new() { Weg(a.R(alt)) }, null, "offen"));
        }
        // 5) Raum gesperrt, Ersatzraum zugeordnet
        {
            var e = a.Stunde(); var t = a.TagVon(e);
            var alt = e.RaumIds[0];
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = alt, Von = t, Bis = t, StundeVon = e.Stunde, StundeBis = e.Stunde });
            Vertretungen.Aktualisieren(d, t);
            var v = d.Vertretungen.Single(x => x.Datum == t && x.Stunde == e.Stunde && x.UnterrichtId == e.Unterricht!.Id && x.Art == VertretungsArt.Raumvertretung);
            var neu = a.FreierRaum(Tagesplan.Berechnen(d, t), e.Stunde, alt)!.Id;
            Raumvertretungen.Zuordnen(d, v, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Raum gesperrt, Ersatzraum", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
        }
        // 6) Raum verdrängt: eine Stunde nimmt den Raum einer anderen (ohne Kollision) – die andere wird offen
        {
            TagesEintrag? opfer = null;
            var e = a.Stunde((x, tp) => (opfer = tp.Stunde(x.Stunde).FirstOrDefault(o => o != x && o.Regel != null && o.Status == TagesStatus.Regulaer && o.RaumIds.Count == 1
                && o.LehrerIds.Count == 1 && o.KlassenIds.Count == 1 && o.Unterricht!.Zeilen.Count == 1 && o.RaumIds[0] != x.RaumIds[0])) != null);
            var t = a.TagVon(e);
            var alt = e.RaumIds[0]; var neu = opfer!.RaumIds[0];
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Raumverlegung (nimmt fremden Raum)", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
            faelle.Add(Neuer("Raum verdrängt (offen)", opfer, new() { Weg(a.R(neu)) }, null, "offen"));
        }
        // 7) Raumtausch: zwei Stunden tauschen ihre Räume
        {
            TagesEintrag? partner = null;
            var e = a.Stunde((x, tp) => (partner = tp.Stunde(x.Stunde).FirstOrDefault(o => o != x && o.Regel != null && o.Status == TagesStatus.Regulaer && o.RaumIds.Count == 1
                && o.LehrerIds.Count == 1 && o.KlassenIds.Count == 1 && o.Unterricht!.Zeilen.Count == 1 && o.RaumIds[0] != x.RaumIds[0])) != null);
            var t = a.TagVon(e);
            var ra = e.RaumIds[0]; var rb = partner!.RaumIds[0];
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, rb, false, RaumKollisionsLoesung.RaeumeTauschen);
            faelle.Add(Neuer("Raumtausch (A)", e, new() { Neu(a.R(rb), a.R(ra)) }, a.R(rb)));
            faelle.Add(Neuer("Raumtausch (B)", partner, new() { Neu(a.R(ra), a.R(rb)) }, a.R(ra)));
        }
        // 8) Vertretung mit Lehrerwechsel und anderem Raum
        {
            var e = a.Stunde(); var t = a.TagVon(e); var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id;
            var frei = d.Lehrer.First(l => !l.Ignorieren && !tp.Stunde(e.Stunde).Any(x => x.LehrerIds.Contains(l.Id)) && l.Id != e.LehrerIds[0]);
            Vertretungen.StundeAendern(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, e.Stunde, frei.Id, e.FachId, e.KlassenIds.ToList(), neu, "");
            faelle.Add(Neuer("Vertretung mit anderem Raum", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)) with { Lehrer = frei.Kurzname });
        }
        // 9) Vertretung (Lehrkraft abwesend), danach Raum geändert
        {
            var e = a.Stunde(); var t = a.TagVon(e);
            var alt = e.RaumIds[0];
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = e.LehrerIds[0], Von = t, Bis = t, StundeVon = e.Stunde, StundeBis = e.Stunde });
            Vertretungen.Aktualisieren(d, t);
            var v = d.Vertretungen.Single(x => x.Datum == t && x.Stunde == e.Stunde && x.UnterrichtId == e.Unterricht!.Id);
            var tp = Tagesplan.Berechnen(d, t);
            var frei = d.Lehrer.First(l => !l.Ignorieren && !tp.Stunde(e.Stunde).Any(x => x.LehrerIds.Contains(l.Id)) && l.Id != e.LehrerIds[0]
                && !Absenzen.LehrerAbwesend(d, l.Id, t, e.Stunde));
            Vertretungen.VertreterSetzen(d, v, frei.Id);
            var neu = a.FreierRaum(Tagesplan.Berechnen(d, t), e.Stunde)!.Id;
            Raumvertretungen.Zuordnen(d, v, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Vertretung (Absenz) + Raumänderung", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)) with { Lehrer = frei.Kurzname });
        }
        // 10) Unterricht geändert (anderes Fach und anderer Raum)
        {
            var e = a.Stunde(); var t = a.TagVon(e); var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id;
            var fach = d.Faecher.First(f => f.Id != e.FachId && !f.Bereitschaft);
            Vertretungen.StundeAendern(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, e.Stunde, e.LehrerIds[0], fach.Id, e.KlassenIds.ToList(), neu, "");
            faelle.Add(Neuer("Unterricht geändert (Fach + Raum)", e, new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
        }
        // 11) mehrere Räume: neuer Raum und zusätzlicher Raum
        {
            var e = a.Stunde(); var t = a.TagVon(e); var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id; var dazu = a.FreierRaum(tp, e.Stunde, neu)!.Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, neu, false, RaumKollisionsLoesung.OhneKollision);
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, new[] { e.Stunde }, dazu, true, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(Neuer("Mehrere Räume (neu + zusätzlich)", e, new() { Neu(a.R(neu), a.R(alt)), Plus(a.R(dazu)) }, a.R(dazu)));
        }
        // 12) Kopplung (KR/ER): Raumänderung nur für eine Gruppe
        {
            var (t, e) = Enumerable.Range(0, 5).Select(i => Montag.AddDays(i)).SelectMany(t => Tagesplan.Berechnen(d, t).Eintraege.Select(e => (t, e)))
                .First(x => x.e.Regel != null && x.e.Unterricht!.Zeilen.Count == 2 && x.e.Regel.ZeilenIndex == 0 && x.e.RaumIds.Count == 1 && x.e.Status == TagesStatus.Regulaer
                            && !a.Belegt.Contains((x.t, x.e.Stunde)) && x.e.Stunde <= 8);
            a.Belegt.Add((t, e.Stunde));
            var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde)!.Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, 0, t, new[] { e.Stunde }, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(new Fall("Kopplung: Raumänderung einer Gruppe", t, e.Stunde, e.Unterricht.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
        }
        // 13) Doppelstunde: Raumänderung für beide Stunden
        {
            var (t, e) = Enumerable.Range(0, 5).Select(i => Montag.AddDays(i)).SelectMany(t => Tagesplan.Berechnen(d, t).Eintraege.Select(e => (t, e)))
                .First(x => x.e.Regel != null && x.e.Unterricht!.Zeilen.Count == 1 && x.e.RaumIds.Count == 1 && x.e.Status == TagesStatus.Regulaer && x.e.Stunde <= 7
                            && !a.Belegt.Contains((x.t, x.e.Stunde)) && !a.Belegt.Contains((x.t, x.e.Stunde + 1))
                            && Tagesplan.Berechnen(d, x.t).Stunde(x.e.Stunde + 1).Any(y => y.Unterricht?.Id == x.e.Unterricht.Id && y.RaumIds.SequenceEqual(x.e.RaumIds)));
            a.Belegt.Add((t, e.Stunde)); a.Belegt.Add((t, e.Stunde + 1));
            var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0];
            var neu = d.Raeume.First(r => !r.Ignorieren && !tp.Stunde(e.Stunde).Concat(tp.Stunde(e.Stunde + 1)).Any(x => x.RaumIds.Contains(r.Id))).Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, 0, t, new[] { e.Stunde, e.Stunde + 1 }, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(new Fall("Doppelstunde: Raumänderung (1. Std.)", t, e.Stunde, e.Unterricht.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
            faelle.Add(new Fall("Doppelstunde: Raumänderung (2. Std.)", t, e.Stunde + 1, e.Unterricht.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
        }
        // 14) Doppelstunde: Raumänderung nur in der zweiten Stunde
        {
            var (t, e) = Enumerable.Range(0, 5).Select(i => Montag.AddDays(i)).SelectMany(t => Tagesplan.Berechnen(d, t).Eintraege.Select(e => (t, e)))
                .First(x => x.e.Regel != null && x.e.Unterricht!.Zeilen.Count == 1 && x.e.RaumIds.Count == 1 && x.e.Status == TagesStatus.Regulaer && x.e.Stunde <= 7
                            && !a.Belegt.Contains((x.t, x.e.Stunde)) && !a.Belegt.Contains((x.t, x.e.Stunde + 1))
                            && Tagesplan.Berechnen(d, x.t).Stunde(x.e.Stunde + 1).Any(y => y.Unterricht?.Id == x.e.Unterricht.Id && y.RaumIds.SequenceEqual(x.e.RaumIds)));
            a.Belegt.Add((t, e.Stunde)); a.Belegt.Add((t, e.Stunde + 1));
            var tp = Tagesplan.Berechnen(d, t);
            var alt = e.RaumIds[0]; var neu = a.FreierRaum(tp, e.Stunde + 1)!.Id;
            Raumvertretungen.Zuordnen(d, e.Unterricht!.Id, 0, t, new[] { e.Stunde + 1 }, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(new Fall("Doppelstunde: nur 2. Std. anderer Raum", t, e.Stunde + 1, e.Unterricht.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), new() { Neu(a.R(neu), a.R(alt)) }, a.R(neu)));
            faelle.Add(new Fall("Doppelstunde: 1. Std. unverändert", t, e.Stunde, e.Unterricht.Id, a.K(e.KlassenIds[0]), a.L(e.LehrerIds[0]), new() { Gleich(a.R(alt)) }, a.R(alt), "regulaer"));
        }

        // 15) Verlegung in eine andere Stunde mit anderem Raum (Zielstunde)
        {
            var e = a.Stunde(); var t = a.TagVon(e);
            var alt = e.RaumIds[0];
            var tp = Tagesplan.Berechnen(d, t);
            var klasse = e.KlassenIds[0]; var lehrerId = e.LehrerIds[0];
            var ziel = Enumerable.Range(1, 8).First(st => !a.Belegt.Contains((t, st)) && !tp.Stunde(st).Any(x => x.Findetstatt && (x.KlassenIds.Contains(klasse) || x.LehrerIds.Contains(lehrerId))));
            a.Belegt.Add((t, ziel));
            Vertretungen.Verlegen(d, e.Unterricht!.Id, e.Regel!.ZeilenIndex, t, e.Stunde, t, ziel);
            var zv = d.Vertretungen.Single(x => x.Datum == t && x.Stunde == ziel && x.UnterrichtId == e.Unterricht.Id && !x.IstEntfall);
            var neu = a.FreierRaum(Tagesplan.Berechnen(d, t), ziel, alt, zv.VertretungsraumId ?? -1)!.Id;
            var bisher = zv.VertretungsraumId ?? zv.RaumId;
            Raumvertretungen.Zuordnen(d, zv, neu, false, RaumKollisionsLoesung.OhneKollision);
            faelle.Add(new Fall("Verlegung mit anderem Raum (Ziel)", t, ziel, e.Unterricht.Id, a.K(klasse), a.L(lehrerId), new() { Neu(a.R(neu), a.R(bisher)) }, a.R(neu)));
        }
        // 16) Raumänderung aus UntPlanWeb (Lehrkraft meldet sie, noch nicht übernommen) – Stunde hier nur auswählen
        var web = a.Stunde((x, tp) => tp.Datum > Montag.AddDays(2));   // nicht rückwirkend (Testuhr: Mi 10 Uhr)
        var webTag = a.TagVon(web);
        var webAlt = a.R(web.RaumIds[0]);
        var webNeu = a.R(a.FreierRaum(Tagesplan.Berechnen(d, webTag), web.Stunde)!.Id);
        var webFall = new Fall("Raumänderung aus UntPlanWeb", webTag, web.Stunde, web.Unterricht!.Id, a.K(web.KlassenIds[0]), a.L(web.LehrerIds[0]), new() { Neu(webNeu, webAlt) }, webNeu);

        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Montag, new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
        var c = await s.AlsAsync("admin");
        var fehler = new List<string>();
        await Pruefen("vollständiger Export", faelle);

        // Raumänderung in UntPlanWeb melden: vorgemerkt (Raumplan des neuen Raums: noch nicht) …
        var lc = await s.AlsAsync(webFall.Lehrer.ToLowerInvariant(), "lehrer", webFall.Lehrer);
        await Testserver.JsonAsync(await lc.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = webTag.ToString("yyyy-MM-dd"), stundeVon = web.Stunde, stundeBis = web.Stunde, unterricht = web.Unterricht.Id, raeume = new[] { webNeu } }));
        await Pruefen("vorgemerkt", new() { webFall with { Status = "regulaer" } });   // wie eine Buchung: regulär, neuer Raum im Label
        // … in UntPlan übernehmen und erneut exportieren
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        var zeilen = StundenplanAenderungen.Analysieren(d, liste, Montag, Montag.AddDays(14), false);
        var (uebernommen, abgelehnt) = StundenplanAenderungen.Uebernehmen(d, zeilen, "lan");
        Assert.Empty(abgelehnt);
        await UntPlanWebExport.QuittierenAsync(p, uebernommen);
        faelle.Add(webFall);
        paket = UntPlanWebExport.PaketErstellen(d, Montag, new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 2 })).Ok);
        await Pruefen("nach Übernahme", faelle);

        // nur Vertretungen übertragen (Stunden nur mit Änderungen, über den Stundenplan gelegt)
        paket = UntPlanWebExport.PaketErstellen(d, Montag, new WebExportOptionen { TageVoraus = 7, Teile = new() { WebTeile.Vertretungen } });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 3 })).Ok);
        await Pruefen("nur Vertretungen", faelle);
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));

        async Task Pruefen(string phase, List<Fall> liste)
        {
            _aus.WriteLine($"######## {phase}");
            foreach (var f in liste)
            {
                var iso = f.Tag.ToString("yyyy-MM-dd");
                var vz = paket.Vertretungen.Where(v => v.Datum == iso && v.Stunde == f.Stunde && v.Klassen.Contains(f.Klasse)).ToList();
                _aus.WriteLine($"== {f.Name} ({iso} {f.Stunde}. Std., U{f.Unterricht}, {f.Klasse}/{f.Lehrer}) Export: " +
                    string.Join(" | ", vz.Select(v => $"{v.Art} {v.Lehrer}->{v.Vertreter} Raum '{v.Raum}' -> '{v.NeuerRaum}'")) + " Stunde: " +
                    string.Join(" | ", paket.Tage.Single(x => x.Datum == iso).Stunden.Where(x => x.Stunde == f.Stunde && x.Unterricht == f.Unterricht).Select(x => $"{x.Status} [{string.Join(",", x.Raeume)}]")));
                var plaene = new List<(string, string)> { ("klasse", f.Klasse), ("lehrer", f.Lehrer) };
                if (f.Raumplan != null) plaene.Add(("raum", f.Raumplan));
                foreach (var (typ, el) in plaene)
                {
                    var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(el)}&datum={iso}");
                    var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso && k["von"]!.GetValue<int>() <= f.Stunde
                        && k["bis"]!.GetValue<int>() >= f.Stunde && k["unterricht"]?.GetValue<int>() == f.Unterricht).ToList();
                    // Kopplung: im Klassenplan je Gruppe eine Karte – die der betroffenen Lehrkraft
                    if (karten.Count > 1)
                        karten = karten.Where(k => k["lehrer"]!.AsArray().Any(w => w!["k"]!.GetValue<string>() == f.Lehrer || w["alt"]?.GetValue<string>() == f.Lehrer)).ToList();
                    var ist = string.Join("; ", karten.Select(k => k["status"]!.GetValue<string>() + " " + string.Join(",", k["raeume"]!.AsArray().Select(w => Text(w!)))));
                    var soll = f.Status + " " + string.Join(",", f.Erwartet.Select(w => w.Plus ? "+" + w.K : w.Alt is null ? w.K : $"{w.Alt}→{(w.K.Length == 0 ? "∅" : w.K)}"));
                    var ok = karten.Count == 1 && ist == soll;
                    _aus.WriteLine($"   {(ok ? "ok " : "FEHLER")} {typ} {el}: {ist}   (soll {soll})");
                    if (!ok) fehler.Add($"[{phase}] {f.Name} – {typ} {el}: ist „{ist}“, soll „{soll}“");
                }
            }
        }
    }

    /// <summary>Raumwert wie dargestellt: „R1“, „+R2“ (hinzugefügt), „R1→R2“ (grünes Label mit Hover), „R1→∅“ (durchgestrichen im Label).</summary>
    private static string Text(JsonNode w)
    {
        var k = w["k"]!.GetValue<string>();
        var alt = w["alt"]?.GetValue<string>();
        var neu = w["neu"]!.GetValue<bool>();
        if (w["plus"]!.GetValue<bool>()) return "+" + k;
        if (!neu) return alt is { Length: > 0 } ? $"{k}(alt {alt})" : k;
        return $"{alt ?? "?"}→{(k.Length == 0 ? "∅" : k)}";
    }
}
