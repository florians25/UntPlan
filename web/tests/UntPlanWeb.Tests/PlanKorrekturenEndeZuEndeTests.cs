using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Ende zu Ende (Core → Vertretungen → echter Export → UntPlanWeb → /api/app/…):
/// <list type="bullet">
/// <item>Veranstaltung ohne Lehrkräfte: nirgends ein Platzhalter („---“, „???“, „+++“) – auch wenn die Art im Desktop umbenannt ist.</item>
/// <item>Raumänderung bei Stunden mehrerer Klassen (eine Zeile mit mehreren Klassen, Kopplungszeilen, Mitklassen): grün mit Raumlabel wie bei
///   einer Einzelstunde, ohne Entfall, ohne fehlende Klassen, ohne doppelte Karten – aus dem Desktop und aus den Stundendetails.</item>
/// <item>Fachänderung: zwei Blöcke (Details ohne Fachänderung), im Plan der ursprünglichen Lehrkraft Entfall (bisheriges Fach), die neue Stunde dort nicht.</item>
/// <item>Raumplan eines Aufsichtsorts (z. B. „Einfahrt“): die Pausenaufsichten dort.</item>
/// </list>
/// </summary>
public class PlanKorrekturenEndeZuEndeTests
{
    private readonly ITestOutputHelper _aus;
    public PlanKorrekturenEndeZuEndeTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private const int Std = 5;
    private static readonly string[] Platzhalter = { "---", "???", "?", "+", "+++" };

    // ------------------------------------------------------------------ Hilfen

    private static async Task<Testserver> HochladenAsync(Schuldatei d, DateOnly ab, bool nurVertretungen = false)
    {
        var s = await Testserver.StartenAsync(mitPaket: false);
        await ErneutAsync(s, d, ab, 1);
        if (nurVertretungen) await ErneutAsync(s, d, ab, 2, true);
        return s;
    }

    private static async Task ErneutAsync(Testserver s, Schuldatei d, DateOnly ab, int revision, bool nurVertretungen = false)
    {
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var o = new WebExportOptionen { TageVoraus = 7 };
        if (nurVertretungen) o.Teile = new() { WebTeile.Vertretungen };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, UntPlanWebExport.PaketErstellen(d, ab, o) with { Revision = revision })).Ok);
    }

    private static PortalEinstellungen Portal(Testserver s) => new() { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };

    private static string W(JsonNode w)
    {
        var kk = w["k"]!.GetValue<string>();
        var alt = w["alt"]?.GetValue<string>();
        var neu = w["neu"]?.GetValue<bool>() == true;
        return alt is { Length: > 0 } ? $"{alt}→{(kk.Length == 0 ? "∅" : kk)}" : neu && kk.Length > 0 ? "+" + kk : kk;
    }

    private static string L(JsonNode k, string feld) => k[feld] is JsonArray a ? string.Join(",", a.Select(w => W(w!))) : "";

    /// <summary>Kurzform: Status, Lehrkräfte, Klassen (+Mitklassen), Räume.</summary>
    private static string Kurz(JsonNode k) =>
        $"{k["status"]!.GetValue<string>()} L={L(k, "lehrer")} K={L(k, "klassen")}{(L(k, "mitklassen").Length > 0 ? "+" + L(k, "mitklassen") : "")} R={L(k, "raeume")}";

    private async Task<List<JsonNode>> KartenAsync(HttpClient c, string typ, string kurz, DateOnly tag, int stunde)
    {
        var iso = tag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso
            && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde).ToList();
        _aus.WriteLine($"  {typ} {kurz}: " + (karten.Count == 0 ? "–" : string.Join("  ||  ", karten.Select(Kurz))));
        return karten;
    }

    /// <summary>Alle genannten Pläne gegen die Erwartung (mehrere Karten sortiert mit „ || “; „–“ = keine Karte).</summary>
    private async Task PruefenAsync(HttpClient c, DateOnly tag, int stunde, Dictionary<string, string> erwartet)
    {
        var fehler = new List<string>();
        foreach (var (plan, soll) in erwartet)
        {
            var teile = plan.Split(' ');
            var karten = await KartenAsync(c, teile[0], teile[1], tag, stunde);
            var ist = karten.Count == 0 ? "–" : string.Join("  ||  ", karten.Select(Kurz).OrderBy(x => x, StringComparer.Ordinal));
            var sollSortiert = soll == "–" ? "–" : string.Join("  ||  ", soll.Split("  ||  ").OrderBy(x => x, StringComparer.Ordinal));
            if (ist != sollSortiert) fehler.Add($"{plan}: ist „{ist}“, soll „{sollSortiert}“");
            // geänderte Karten: grünes Änderungssymbol, nie ein Entfall-Symbol
            foreach (var k in karten.Where(k => k["status"]!.GetValue<string>() is "geaendert" or "offen"))
            {
                var marker = k["marker"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
                if (!marker.Contains("aenderung") || marker.Contains("entfall")) fehler.Add($"{plan}: Symbole {string.Join(",", marker)}");
            }
        }
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    // ------------------------------------------------------------------ 1) Veranstaltung ohne Lehrkräfte

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Veranstaltung_ohne_Lehrkraefte_zeigt_nirgends_einen_Platzhalter(bool artUmbenannt, bool offenerPlatz)
    {
        var tag = new DateOnly(2026, 10, 15);
        var d = Demo.NeueDatei("Testschule", "GY");
        var fach = new Fach { Id = d.NeueId(), Kurzname = "M" };
        d.Faecher.Add(fach);
        var klassen = new[] { "5a", "5b" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var lehrer = new[] { "WEB", "SWA", "KOC" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var raum = new Raum { Id = d.NeueId(), Kurzname = "R101" };
        d.Klassen.AddRange(klassen);
        d.Lehrer.AddRange(lehrer);
        d.Raeume.Add(raum);
        for (var i = 0; i < 2; i++)
        {
            var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = lehrer[i].Id, FachId = fach.Id, KlassenIds = { klassen[i].Id }, FachraumId = raum.Id } } };
            d.Unterrichte.Add(u);
            d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)tag.DayOfWeek - 1, Stunde = 3 + i, Raeume = { raum.Id } });
        }
        if (artUmbenannt) d.Vertretungseinstellungen.Bezeichnungen["Veranstaltung"] = "Ausflug";
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = 3, StundeBis = 4, KlassenIds = klassen.Select(k => k.Id).ToList(), Text = "Wandertag" };
        // offener Platz: die einzige Lehrkraft wurde entfernt, ohne Ersatz („???“ in der Vertretungszeile)
        if (offenerPlatz)
        {
            va.LehrerIds.Add(lehrer[2].Id);
            var krank = new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer[2].Id, Von = tag, Bis = tag };
            d.Abwesenheiten.Add(krank);
            va.Ersatz.Add(new VeranstaltungsErsatz { Datum = tag, LehrerId = lehrer[2].Id, ErsatzId = null, AbwesenheitId = krank.Id });
        }
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        foreach (var z in Vertretungsplan.Zeilen(d, tag)) _aus.WriteLine($"Desktop-Zeile: {z.Art} Kl={string.Join(",", z.KlassenIds)} {z.Lehrer}->{z.Vertreter}");
        await using var s = await HochladenAsync(d, tag.AddDays(-3));
        var iso = tag.ToString("yyyy-MM-dd");
        var admin = await s.AlsAsync("admin");
        foreach (var (typ, el) in new[] { ("klasse", "5a"), ("klasse", "5b") })
        {
            var karten = (await KartenAsync(admin, typ, el, tag, 3)).Where(k => k["status"]!.GetValue<string>() == "veranstaltung").ToList();
            var k = Assert.Single(karten);
            Assert.Equal("Wandertag", k["text"]!.GetValue<string>());
            Assert.Empty(k["lehrer"]!.AsArray());
        }
        // Vertretungsliste (Lehrer- und Schülerversion), Startseite der Klasse, Tagesübersicht
        var liste = await Testserver.HolenAsync(admin, $"/api/app/vertretungen?datum={iso}");
        var zeilen = liste["tage"]!.AsArray().SelectMany(t => t!["zeilen"]!.AsArray()).Select(z => z!).Where(z => z["text"]?.GetValue<string>() == "Wandertag" && z["status"]!.GetValue<string>() != "entfall").ToList();
        // (mit nur einem offenen Platz führt der Desktop keine Vertretungszeile – die Veranstaltung steht dann nur oben in der Liste)
        if (!offenerPlatz) Assert.NotEmpty(zeilen);
        Assert.Contains("Wandertag (3.–4. Std., 5a, 5b)", liste["tage"]!.AsArray().SelectMany(t => t!["veranstaltungen"]!.AsArray()).Select(x => x!.GetValue<string>()));
        foreach (var z in zeilen)
        {
            Assert.DoesNotContain(z["lehrer"]!.GetValue<string>().Split(',', StringSplitOptions.TrimEntries), Platzhalter.Contains);
            Assert.True(z["lehrerAlt"] is null || !Platzhalter.Contains(z["lehrerAlt"]!.GetValue<string>()), z.ToJsonString());
            Assert.Equal("geaendert", z["status"]!.GetValue<string>());
            Assert.False(z["ohneLehrer"]?.GetValue<bool>() == true);
        }
        var schueler = await s.AlsAsync("schueler5a", "schueler", "5a");
        var tagJson = await Testserver.HolenAsync(admin, $"/api/app/tag?typ=klasse&datum={iso}");
        foreach (var k in tagJson["zeilen"]!.AsArray().SelectMany(z => z!["karten"]!.AsArray()).Select(k => k!).Where(k => k["status"]!.GetValue<string>() == "veranstaltung"))
            Assert.DoesNotContain(k["lehrer"]!.AsArray(), w => Platzhalter.Contains(w!["k"]!.GetValue<string>()) || w["k"]!.GetValue<string>().Length == 0);
        var plan = await Testserver.HolenAsync(schueler, $"/api/app/plan?typ=klasse&element=5a&datum={iso}");
        Assert.DoesNotContain("\"---\"", plan.ToJsonString());
        Assert.DoesNotContain("\"???\"", plan.ToJsonString());
    }

    // ------------------------------------------------------------------ 3) Raumänderung bei Stunden mehrerer Klassen

    /// <summary>
    /// Form der Stunde: eine Zeile mit zwei Klassen; zwei Unterrichte derselben Lehrkraft (Mitklassen); Kopplung mit einer Zeile je Klasse
    /// (dieselbe Lehrkraft); Kopplung aus einer Zeile mit zwei Klassen und einer zweiten Lehrkraft ohne eigene Klassen.
    /// </summary>
    public enum Form { ZeileZweiKlassen, Mitklassen, KopplungJeKlasse, KopplungZweiteLehrkraft }

    private static (Schuldatei D, List<Unterricht> U) Schule(Form form)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW" };
        d.Faecher.Add(ww);
        var kl = new[] { "ET13", "INF13" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var le = new[] { "HE", "CS" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var ra = new[] { "B117", "R200", "R201" }.Select(k => new Raum { Id = d.NeueId(), Kurzname = k, Buchbar = true }).ToArray();
        d.Klassen.AddRange(kl);
        d.Lehrer.AddRange(le);
        d.Raeume.AddRange(ra);
        Kopplungszeile Z(int lehrer, params Klasse[] k) => new() { LehrerId = le[lehrer].Id, FachId = ww.Id, KlassenIds = k.Select(x => x.Id).ToList(), FachraumId = ra[0].Id };
        var us = form switch
        {
            Form.ZeileZweiKlassen => new List<Unterricht> { new() { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { Z(0, kl) } } },
            Form.Mitklassen => kl.Select(k => new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { Z(0, k) } }).ToList(),
            Form.KopplungJeKlasse => new List<Unterricht> { new() { Id = d.NeueId(), Wochenstunden = 1, Zeilen = kl.Select(k => Z(0, k)).ToList() } },
            _ => new List<Unterricht> { new() { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { Z(0, kl), Z(1) } } },
        };
        d.Unterrichte.AddRange(us);
        foreach (var u in us) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)Freitag.DayOfWeek - 1, Stunde = Std, Raeume = u.Zeilen.Select(_ => (int?)ra[0].Id).ToList() });
        return (d, us);
    }

    private static int Raum(Schuldatei d, string kurz) => d.Raeume.First(r => r.Kurzname == kurz).Id;

    [Theory]
    [InlineData(Form.ZeileZweiKlassen, false, false)]
    [InlineData(Form.ZeileZweiKlassen, false, true)]
    [InlineData(Form.Mitklassen, false, false)]
    [InlineData(Form.Mitklassen, true, false)]
    [InlineData(Form.Mitklassen, true, true)]
    [InlineData(Form.KopplungJeKlasse, false, false)]
    [InlineData(Form.KopplungJeKlasse, false, true)]
    [InlineData(Form.KopplungJeKlasse, true, false)]
    [InlineData(Form.KopplungJeKlasse, true, true)]
    [InlineData(Form.KopplungZweiteLehrkraft, false, false)]
    [InlineData(Form.KopplungZweiteLehrkraft, false, true)]
    [InlineData(Form.KopplungZweiteLehrkraft, true, false)]
    public async Task Raumaenderung_bei_mehreren_Klassen_wie_bei_einer_Einzelstunde(Form form, bool alle, bool nurVertretungen)
    {
        var (d, us) = Schule(form);
        // Raumänderung im Desktop: eine Zeile (der erste Unterricht, erste Zeile) bzw. alle Zeilen aller Unterrichte nach R200
        foreach (var u in alle ? us : us.Take(1))
            for (var z = 0; z < (alle ? u.Zeilen.Count : 1); z++)
                Raumvertretungen.Zuordnen(d, u.Id, z, Freitag, new[] { Std }, Raum(d, "R200"), false, RaumKollisionsLoesung.MitKollision);
        foreach (var z in Vertretungsplan.Zeilen(d, Freitag)) _aus.WriteLine($"Desktop-Zeile: {z.Art} Kl={string.Join(",", z.KlassenIds)} {z.Lehrer}->{z.Vertreter} Raum {z.Raum}->{z.NeuerRaum}");
        await using var s = await HochladenAsync(d, Freitag.AddDays(-4), nurVertretungen);
        var c = await s.AlsAsync("admin");
        const string ganz = "geaendert L=HE K=ET13,INF13 R=B117→R200";
        await PruefenAsync(c, Freitag, Std, (form, alle) switch
        {
            (Form.ZeileZweiKlassen, _) => new()
            {
                ["lehrer HE"] = ganz, ["klasse ET13"] = ganz, ["klasse INF13"] = ganz, ["raum R200"] = ganz, ["raum B117"] = "–",
            },
            (Form.Mitklassen, false) => new()
            {
                // nur die Gruppe von ET13 zieht um: zwei Blöcke nebeneinander, je wie eine Einzelstunde
                ["lehrer HE"] = "geaendert L=HE K=ET13 R=B117→R200  ||  regulaer L=HE K=INF13 R=B117",
                ["klasse ET13"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["klasse INF13"] = "regulaer L=HE K=INF13 R=B117",
                ["raum B117"] = "regulaer L=HE K=INF13 R=B117",
                ["raum R200"] = "geaendert L=HE K=ET13 R=B117→R200",
            },
            // Kopplung im gemeinsamen Raum: die ganze Kopplung zieht mit um – auch wenn nur eine Zeile geändert wird
            (Form.Mitklassen, true) or (Form.KopplungJeKlasse, _) => new()
            {
                ["lehrer HE"] = ganz,
                ["klasse ET13"] = "geaendert L=HE K=ET13+INF13 R=B117→R200",
                ["klasse INF13"] = "geaendert L=HE K=INF13+ET13 R=B117→R200",
                ["raum B117"] = "–",
                ["raum R200"] = ganz,
            },
            _ => new()
            {
                ["lehrer HE"] = "geaendert L=HE,CS K=ET13,INF13 R=B117→R200",
                ["lehrer CS"] = "geaendert L=HE,CS K=ET13,INF13 R=B117→R200",
                ["klasse ET13"] = "geaendert L=HE,CS K=ET13,INF13 R=B117→R200",
                ["klasse INF13"] = "geaendert L=HE,CS K=ET13,INF13 R=B117→R200",
                ["raum B117"] = "–",
                ["raum R200"] = "geaendert L=HE,CS K=ET13,INF13 R=B117→R200",
            },
        });
    }

    /// <summary>
    /// Fall aus den Echtdaten: Raum B117 nicht verfügbar (Raumsperre). Die Kopplung ET13/INF13/ING13 (HE, CS, SZ) mit EINEM Raum im Stundenplan –
    /// UntPlan ordnet ihn nur der ersten Zeile zu – zeigt die offene Raumvertretung wie jede Einzelstunde: grün, B117 durchgestrichen im Label,
    /// in allen Plänen und für alle Zeilen (auch beim Export nur der Vertretungen).
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Raumsperre_bei_einer_Kopplung_mit_gemeinsamem_Raum(bool einRaumImStundenplan, bool nurVertretungen)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW" };
        d.Faecher.Add(ww);
        var kl = new[] { "ET13", "INF13", "ING13" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var le = new[] { "HE", "CS", "SZ", "SW" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var b117 = new Raum { Id = d.NeueId(), Kurzname = "B117" };
        d.Klassen.AddRange(kl);
        d.Lehrer.AddRange(le);
        d.Raeume.Add(b117);
        var tag = (int)Freitag.DayOfWeek - 1;
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = Enumerable.Range(0, 3).Select(i => new Kopplungszeile { LehrerId = le[i].Id, FachId = ww.Id, KlassenIds = { kl[i].Id }, FachraumId = einRaumImStundenplan ? null : b117.Id }).ToList() };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = Std, Raeume = einRaumImStundenplan ? new List<int?> { b117.Id } : new List<int?> { b117.Id, b117.Id, b117.Id } });
        // zum Vergleich eine Einzelstunde im selben Raum
        var e = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[3].Id, FachId = ww.Id, KlassenIds = { kl[0].Id }, FachraumId = b117.Id } } };
        d.Unterrichte.Add(e);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = e.Id, Tag = tag, Stunde = 3, Raeume = { b117.Id } });
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = b117.Id, Von = Freitag, Bis = Freitag });
        Vertretungen.Aktualisieren(d, Freitag);
        foreach (var z in Vertretungsplan.Zeilen(d, Freitag)) _aus.WriteLine($"Desktop-Zeile: {z.Art} Kl={string.Join(",", z.KlassenIds)} {z.Lehrer}->{z.Vertreter} Raum {z.Raum}->{z.NeuerRaum}");
        await using var s = await HochladenAsync(d, Freitag.AddDays(-4), nurVertretungen);
        var c = await s.AlsAsync("admin");
        await PruefenAsync(c, Freitag, 3, new() { ["lehrer SW"] = "offen L=SW K=ET13 R=B117→∅", ["klasse ET13"] = "offen L=SW K=ET13 R=B117→∅" });
        const string alle = "offen L=HE,CS,SZ K=ET13,INF13,ING13 R=B117→∅";
        await PruefenAsync(c, Freitag, Std, new()
        {
            ["lehrer HE"] = alle, ["lehrer CS"] = alle, ["lehrer SZ"] = alle, ["raum B117"] = alle,
            ["klasse ET13"] = "offen L=HE K=ET13+INF13,ING13 R=B117→∅",
            ["klasse INF13"] = "offen L=CS K=INF13+ET13,ING13 R=B117→∅",
            ["klasse ING13"] = "offen L=SZ K=ING13+ET13,INF13 R=B117→∅",
        });
    }

    /// <summary>
    /// Raumänderung aus den Stundendetails (Web) bei Stunden mehrerer Klassen: solange UntPlan sie nicht übernommen hat („vorgemerkt“) wie bei
    /// einer Einzelstunde der neue Raum als Label in jedem Plan, alle Klassen auf der Karte, im neuen Raum die ganze Stunde, im bisherigen
    /// keine; die Meldung nennt die Klassen EINER Zeile (sonst fände UntPlan keine Stunde und legte eine zusätzliche an).
    /// </summary>
    [Theory]
    [InlineData(Form.ZeileZweiKlassen)]
    [InlineData(Form.KopplungJeKlasse)]
    [InlineData(Form.KopplungZweiteLehrkraft)]
    public async Task Raumaenderung_aus_den_Stundendetails_bei_mehreren_Klassen(Form form)
    {
        var (d, us) = Schule(form);
        await using var s = await HochladenAsync(d, Freitag.AddDays(-4));
        var he = await s.AlsAsync("he", "lehrer", "HE");
        var karte = (await KartenAsync(he, "lehrer", "HE", Freitag, Std)).Single();
        var bisher = karte["raeume"]!.AsArray().Select(w => w!["k"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "B117" }, bisher);
        await Testserver.JsonAsync(await he.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = Freitag.ToString("yyyy-MM-dd"), stundeVon = Std, stundeBis = Std, unterricht = karte["unterricht"]!.GetValue<int>(), raeume = new[] { "R200" }, bisher }));
        var c = await s.AlsAsync("admin");
        // vorgemerkt: wie eine Raumbuchung regulär, der neue Raum als Label
        var alleKlassen = form == Form.KopplungZweiteLehrkraft ? "regulaer L=HE,CS K=ET13,INF13 R=B117→R200" : "regulaer L=HE K=ET13,INF13 R=B117→R200";
        await PruefenAsync(c, Freitag, Std, form switch
        {
            Form.KopplungJeKlasse => new()
            {
                ["lehrer HE"] = alleKlassen,
                ["klasse ET13"] = "regulaer L=HE K=ET13+INF13 R=B117→R200",
                ["klasse INF13"] = "regulaer L=HE K=INF13+ET13 R=B117→R200",
                ["raum B117"] = "–",
                ["raum R200"] = alleKlassen,
            },
            _ => new()
            {
                ["lehrer HE"] = alleKlassen, ["klasse ET13"] = alleKlassen, ["klasse INF13"] = alleKlassen, ["raum B117"] = "–", ["raum R200"] = alleKlassen,
            },
        });
        // Meldung an UntPlan: Unterricht, vollständige Raumliste, Klassen einer Zeile
        var meldung = Assert.Single((await UntPlanWebExport.RueckmeldungenAsync(Portal(s))).Where(m => m.Typ == WebRueckmeldungsTypen.Raumbuchung));
        Assert.Equal(us[0].Id, meldung.UnterrichtNr);
        Assert.Equal(new[] { "R200" }, meldung.Raeume);
        Assert.Equal(form == Form.KopplungJeKlasse ? new[] { "ET13" } : new[] { "ET13", "INF13" }, meldung.Klassen.OrderBy(x => x));
        // Übernahme in UntPlan: Raumänderung des Unterrichts, keine zusätzliche Stunde
        var zeilen = StundenplanAenderungen.Analysieren(d, new List<WebRueckmeldung> { meldung }, Freitag.AddDays(-4), Freitag.AddDays(14), false);
        _aus.WriteLine("Übernahme: " + string.Join(" | ", zeilen.Select(z => $"{z.Art} U={z.UnterrichtId} Z={z.ZeilenIndex} {z.Fehler}")));
        Assert.Equal(StplAenderungsArt.Unterricht, Assert.Single(zeilen).Art);
        // (Protokoll) was der Desktop daraus macht – je Zeile der Kopplung der Raum nach der Übernahme
        StundenplanAenderungen.Uebernehmen(d, zeilen, "lan");
        foreach (var e in Tagesplan.Berechnen(d, Freitag).Stunde(Std))
            _aus.WriteLine($"Desktop nach Übernahme: U={e.Unterricht?.Id} L={string.Join(",", e.LehrerIds)} R={string.Join(",", e.RaumIds)} K={string.Join(",", e.KlassenIds)}");
    }

    /// <summary>
    /// Kopplung mit Blöcken in eigenen Räumen: Raumänderung eines Blocks aus den Stundendetails (bisher = Räume dieses Blocks) – nur dieser
    /// Block bekommt den neuen Raum, die übrigen bleiben; die Meldung trägt die vollständige Raumliste des Unterrichts.
    /// </summary>
    [Fact]
    public async Task Raumaenderung_eines_Blocks_einer_Kopplung_mit_eigenen_Raeumen()
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW" };
        d.Faecher.Add(ww);
        var kl = new[] { "ET13", "INF13", "ING13" }.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var le = new[] { "HE", "CS", "SZ" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var ra = new[] { "B117", "B118", "B119", "R201" }.Select(k => new Raum { Id = d.NeueId(), Kurzname = k, Buchbar = true }).ToArray();
        d.Klassen.AddRange(kl);
        d.Lehrer.AddRange(le);
        d.Raeume.AddRange(ra);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = Enumerable.Range(0, 3).Select(i => new Kopplungszeile { LehrerId = le[i].Id, FachId = ww.Id, KlassenIds = { kl[i].Id }, FachraumId = ra[i].Id }).ToList() };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)Freitag.DayOfWeek - 1, Stunde = Std, Raeume = Enumerable.Range(0, 3).Select(i => (int?)ra[i].Id).ToList() });
        await using var s = await HochladenAsync(d, Freitag.AddDays(-4));
        var cs = await s.AlsAsync("cs", "lehrer", "CS");
        await Testserver.JsonAsync(await cs.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
            new { datum = Freitag.ToString("yyyy-MM-dd"), stundeVon = Std, stundeBis = Std, unterricht = u.Id, raeume = new[] { "R201" }, bisher = new[] { "B118" } }));
        var c = await s.AlsAsync("admin");
        await PruefenAsync(c, Freitag, Std, new()
        {
            ["lehrer HE"] = "regulaer L=HE K=ET13 R=B117",
            ["lehrer CS"] = "regulaer L=CS K=INF13 R=B118→R201",
            ["lehrer SZ"] = "regulaer L=SZ K=ING13 R=B119",
            ["klasse INF13"] = "regulaer L=CS K=INF13 R=B118→R201",
            ["klasse ET13"] = "regulaer L=HE K=ET13 R=B117",
            ["raum B118"] = "–",
            ["raum B117"] = "regulaer L=HE K=ET13 R=B117",
            ["raum R201"] = "regulaer L=CS K=INF13 R=B118→R201",
        });
        var meldung = Assert.Single((await UntPlanWebExport.RueckmeldungenAsync(Portal(s))).Where(m => m.Typ == WebRueckmeldungsTypen.Raumbuchung));
        Assert.Equal(new[] { "B117", "B119", "R201" }, meldung.Raeume.OrderBy(x => x));
        Assert.Equal(new[] { "INF13" }, meldung.Klassen);
    }

    // ------------------------------------------------------------------ 6) Fachänderung

    public enum Fachfall { VertretungMitAnderemFach, StundeAendernBeiAbwesenheit, StundeAendernOhneAbwesenheit }

    [Theory]
    [InlineData(Fachfall.VertretungMitAnderemFach, false)]
    [InlineData(Fachfall.VertretungMitAnderemFach, true)]
    [InlineData(Fachfall.StundeAendernBeiAbwesenheit, false)]
    [InlineData(Fachfall.StundeAendernBeiAbwesenheit, true)]
    [InlineData(Fachfall.StundeAendernOhneAbwesenheit, false)]
    public async Task Fachaenderung_Entfall_bei_der_urspruenglichen_Lehrkraft(Fachfall fall, bool nurVertretungen)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW", Hintergrund = unchecked((int)0xFFFF0000) };
        var ma = new Fach { Id = d.NeueId(), Kurzname = "MA", Hintergrund = unchecked((int)0xFF00FF00) };
        d.Faecher.AddRange(new[] { ww, ma });
        var kl = new Klasse { Id = d.NeueId(), Kurzname = "ET13" };
        var le = new[] { "HE", "VX" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var ra = new Raum { Id = d.NeueId(), Kurzname = "B117" };
        d.Klassen.Add(kl);
        d.Lehrer.AddRange(le);
        d.Raeume.Add(ra);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { kl.Id }, FachraumId = ra.Id } } };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)Freitag.DayOfWeek - 1, Stunde = Std, Raeume = { ra.Id } });
        if (fall != Fachfall.StundeAendernOhneAbwesenheit)
        {
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = Freitag, Bis = Freitag });
            Vertretungen.Aktualisieren(d, Freitag);
        }
        if (fall == Fachfall.VertretungMitAnderemFach)
        {
            var v = d.Vertretungen.Single(x => x.Datum == Freitag && x.Stunde == Std);
            Vertretungen.VertreterSetzen(d, v, le[1].Id);
            v.VertretungsfachId = ma.Id;
        }
        else Stundenaenderung.Ausfuehren(d, u.Id, 0, Freitag, new[] { Std }, new StundeAendernWerte(le[1].Id, ma.Id, new[] { kl.Id }, ra.Id));
        foreach (var z in Vertretungsplan.Zeilen(d, Freitag)) _aus.WriteLine($"Desktop-Zeile: {z.Art} {z.Lehrer}->{z.Vertreter} Fach {z.Fach}->{z.NeuesFach}");
        await using var s = await HochladenAsync(d, Freitag.AddDays(-4), nurVertretungen);
        var c = await s.AlsAsync("admin");

        // Plan der ursprünglichen Lehrkraft: nur der Entfall mit dem bisherigen Fach (rot) – die neue Stunde steht dort nicht
        var he = Assert.Single(await KartenAsync(c, "lehrer", "HE", Freitag, Std));
        Assert.Equal("entfall", he["status"]!.GetValue<string>());
        Assert.Equal("WW", he["fach"]!.GetValue<string>());
        Assert.Equal("#FF0000", he["farbe"]!.GetValue<string>());
        Assert.Contains("entfall", he["marker"]!.AsArray().Select(x => x!.GetValue<string>()));
        // Plan der Vertretung: nur die neue Stunde (grün, neues Fach mit seiner Farbe; fachAlt nur noch für Tooltip/Vertretungsliste)
        var vx = Assert.Single(await KartenAsync(c, "lehrer", "VX", Freitag, Std));
        Assert.Equal("geaendert", vx["status"]!.GetValue<string>());
        Assert.Equal("MA", vx["fach"]!.GetValue<string>());
        Assert.Equal("WW", vx["fachAlt"]!.GetValue<string>());
        Assert.Equal("#00FF00", vx["farbe"]!.GetValue<string>());
        // Klassen- und Raumplan: neue Stunde grün und daneben die entfallene alte
        foreach (var (typ, el) in new[] { ("klasse", "ET13"), ("raum", "B117") })
        {
            var karten = await KartenAsync(c, typ, el, Freitag, Std);
            Assert.Equal(2, karten.Count);
            var neu = karten.Single(k => k["status"]!.GetValue<string>() == "geaendert");
            Assert.Equal("MA", neu["fach"]!.GetValue<string>());
            Assert.Equal("WW", neu["fachAlt"]!.GetValue<string>());
            var alt = karten.Single(k => k["status"]!.GetValue<string>() == "entfall");
            Assert.Equal("WW", alt["fach"]!.GetValue<string>());
        }
    }

    /// <summary>
    /// Fall aus den Echtdaten: CK (abwesend) hat ING12 MIK in B063 in der 5.–6. Stunde. Die 5. vertritt WR im selben Fach, die 6. EVA mit
    /// anderem Fach (SL). Plan von CK: 5. Stunde abgegeben (Eckklammern, WR als Label), 6. Stunde Entfall MIK – die SL-Stunde nicht.
    /// Klassenplan: 5. grün MIK (WR), 6. grün SL und daneben der Entfall MIK.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fachaenderung_in_einer_Stunde_eines_Blocks_der_abwesenden_Lehrkraft(bool nurVertretungen)
    {
        var di = new DateOnly(2026, 11, 3);
        var d = Demo.NeueDatei("Testschule", "GY");
        var mik = new Fach { Id = d.NeueId(), Kurzname = "MIK" };
        var sl = new Fach { Id = d.NeueId(), Kurzname = "SL" };
        d.Faecher.AddRange(new[] { mik, sl });
        var kl = new Klasse { Id = d.NeueId(), Kurzname = "ING12" };
        var le = new[] { "CK", "WR", "EVA" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        var ra = new Raum { Id = d.NeueId(), Kurzname = "B063" };
        d.Klassen.Add(kl);
        d.Lehrer.AddRange(le);
        d.Raeume.Add(ra);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = mik.Id, KlassenIds = { kl.Id }, FachraumId = ra.Id } } };
        d.Unterrichte.Add(u);
        foreach (var st in new[] { 5, 6 }) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)di.DayOfWeek - 1, Stunde = st, Raeume = { ra.Id } });
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = di, Bis = di });
        Vertretungen.Aktualisieren(d, di);
        Vertretungen.VertreterSetzen(d, d.Vertretungen.Single(x => x.Datum == di && x.Stunde == 5), le[1].Id);
        var v6 = d.Vertretungen.Single(x => x.Datum == di && x.Stunde == 6);
        Vertretungen.VertreterSetzen(d, v6, le[2].Id);
        v6.VertretungsfachId = sl.Id;
        await using var s = await HochladenAsync(d, di.AddDays(-1), nurVertretungen);
        var c = await s.AlsAsync("admin");
        var ck5 = Assert.Single(await KartenAsync(c, "lehrer", "CK", di, 5));
        Assert.Equal("abwesend L=CK→WR K=ING12 R=B063", Kurz(ck5));
        Assert.Equal("MIK", ck5["fach"]!.GetValue<string>());
        var ck6 = Assert.Single(await KartenAsync(c, "lehrer", "CK", di, 6));
        Assert.Equal("entfall L=CK K=ING12 R=B063", Kurz(ck6));
        Assert.Equal("MIK", ck6["fach"]!.GetValue<string>());
        var eva = Assert.Single(await KartenAsync(c, "lehrer", "EVA", di, 6));
        Assert.Equal("SL", eva["fach"]!.GetValue<string>());
        Assert.Equal("MIK", eva["fachAlt"]!.GetValue<string>());
        var klasse6 = await KartenAsync(c, "klasse", "ING12", di, 6);
        Assert.Equal(new[] { "entfall L=CK K=ING12 R=B063", "geaendert L=CK→EVA K=ING12 R=B063" }, klasse6.Select(Kurz).OrderBy(x => x));
        Assert.Equal("MIK", klasse6.Single(k => k["status"]!.GetValue<string>() == "geaendert")["fachAlt"]!.GetValue<string>());
        var klasse5 = Assert.Single(await KartenAsync(c, "klasse", "ING12", di, 5));
        Assert.Equal("geaendert L=CK→WR K=ING12 R=B063", Kurz(klasse5));
    }

    /// <summary>Ältere bzw. nachbearbeitete Exporte ohne „neues Fach“ in der Vertretungszeile: das bisherige Fach kommt aus dem Stundenplan.</summary>
    [Fact]
    public void Fachaenderung_ohne_neues_Fach_in_der_Vertretungszeile()
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW" };
        var ma = new Fach { Id = d.NeueId(), Kurzname = "MA" };
        d.Faecher.AddRange(new[] { ww, ma });
        var kl = new Klasse { Id = d.NeueId(), Kurzname = "ET13" };
        var le = new[] { "HE", "VX" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        d.Klassen.Add(kl);
        d.Lehrer.AddRange(le);
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = le[0].Id, FachId = ww.Id, KlassenIds = { kl.Id } } } };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = (int)Freitag.DayOfWeek - 1, Stunde = Std });
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = Freitag, Bis = Freitag });
        Vertretungen.Aktualisieren(d, Freitag);
        var v = d.Vertretungen.Single(x => x.Datum == Freitag && x.Stunde == Std);
        Vertretungen.VertreterSetzen(d, v, le[1].Id);
        v.VertretungsfachId = ma.Id;
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        paket = paket with { Vertretungen = paket.Vertretungen.Select(x => x.Id == v.Id ? x with { Fach = "", NeuesFach = "" } : x).ToList() };
        var st = new Stand(paket, "");
        var k = new Plandienst().Karten(st, Freitag, Elementtyp.Lehrer, "VX").Single(x => x.Von == Std);
        Assert.Equal("MA", k.Fach);
        Assert.Equal("WW", k.FachAlt);
        var abwesend = Assert.Single(new Plandienst().Karten(st, Freitag, Elementtyp.Lehrer, "HE"), x => x.Von == Std);
        Assert.Equal("entfall", abwesend.Status);
        Assert.Equal("WW", abwesend.Fach);
    }

    // ------------------------------------------------------------------ 4) Aufsichten im Raumplan eines Aufsichtsorts

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raumplan_eines_Aufsichtsorts_zeigt_die_Aufsichten(bool alsRaum)
    {
        var tag = new DateOnly(2026, 10, 15);
        var d = Demo.NeueDatei("Testschule", "GY");
        var le = new[] { "HE", "CS", "VX" }.Select(k => new Lehrer { Id = d.NeueId(), Kurzname = k }).ToArray();
        d.Lehrer.AddRange(le);
        d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = "5a" });
        var einfahrt = new Gang { Id = d.NeueId(), Kurzname = "EIN", Langname = "Einfahrt" };
        var hof = new Gang { Id = d.NeueId(), Kurzname = "HOF", Langname = "Hof" };
        d.Gaenge.AddRange(new[] { einfahrt, hof });
        // Variante: „Einfahrt“ ist zugleich als Raum angelegt (Kürzel EINF, Name Einfahrt)
        if (alsRaum) d.Raeume.Add(new Raum { Id = d.NeueId(), Kurzname = "EINF", Langname = "Einfahrt" });
        var t = (int)tag.DayOfWeek - 1;
        d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = einfahrt.Id, Tag = t, NachStunde = 2, LehrerId = le[0].Id });
        d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = einfahrt.Id, Tag = t, NachStunde = 4, LehrerId = le[1].Id });
        d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = hof.Id, Tag = t, NachStunde = 2, LehrerId = le[1].Id });
        // HE fehlt: VX übernimmt die Aufsicht nach der 2. Stunde
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = le[0].Id, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var va = d.Vertretungen.Single(v => v.Datum == tag && v.Art == VertretungsArt.Pausenaufsicht);
        Vertretungen.VertreterSetzen(d, va, le[2].Id);
        await using var s = await HochladenAsync(d, tag.AddDays(-3));
        var c = await s.AlsAsync("admin");
        var ort = alsRaum ? "EINF" : "EIN";
        // Auswahl der Raumpläne: der Aufsichtsort steht dabei (als Raum bzw. als eigener Aufsichtsort)
        var raeume = (await Testserver.HolenAsync(c, "/api/app/elemente"))["raeume"]!.AsArray().Select(r => r!).ToList();
        Assert.Contains(raeume, r => r["kurz"]!.GetValue<string>() == ort);
        Assert.Equal(!alsRaum, raeume.Single(r => r["kurz"]!.GetValue<string>() == ort)["aufsichtsort"]!.GetValue<bool>());
        Assert.Equal(alsRaum, raeume.All(r => r["kurz"]!.GetValue<string>() != "EIN"));
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ=raum&element={ort}&datum={tag:yyyy-MM-dd}");
        var auf = plan["aufsichten"]!.AsArray().Select(a => a!).Where(a => a["datum"]!.GetValue<string>() == $"{tag:yyyy-MM-dd}").ToList();
        foreach (var a in auf) _aus.WriteLine(a.ToJsonString());
        Assert.Equal(2, auf.Count);
        var nach2 = auf.Single(a => a["nachStunde"]!.GetValue<int>() == 2);
        Assert.Equal("vertretung", nach2["status"]!.GetValue<string>());
        Assert.Equal("VX", nach2["lehrer"]!.GetValue<string>());
        Assert.Equal("HE", nach2["alt"]!.GetValue<string>());
        var nach4 = auf.Single(a => a["nachStunde"]!.GetValue<int>() == 4);
        Assert.Equal("regulaer", nach4["status"]!.GetValue<string>());
        Assert.Equal("CS", nach4["lehrer"]!.GetValue<string>());
        // nicht für Schüler*innen
        var schueler = await s.AlsAsync("schueler1", "schueler", "5a");
        Assert.DoesNotContain((await Testserver.HolenAsync(schueler, "/api/app/elemente"))["raeume"]!.AsArray(), r => r!["kurz"]!.GetValue<string>() == "EIN");
    }

    // ------------------------------------------------------------------ 2) und 5) Web: Startseite schmaler, Ankunft beim Umschalten

    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public void Nur_die_Startseite_ist_schmaler()
    {
        var css = Datei("css", "seitenlayout.css");
        var volle = css.IndexOf("Volle Breite", StringComparison.Ordinal);
        var start = css.IndexOf(".u-app[data-ansicht=\"heute\"] #seite, .u-app[data-ansicht=\"heute\"] #seitenkopf { max-width: var(--u-start-max); margin-left: 0; margin-right: auto; }", StringComparison.Ordinal);
        Assert.True(volle > 0 && start > volle, "Startseite nach der Regel für volle Breite");
        // links ausgerichtet (nicht zentriert): der freie Platz bleibt rechts
        Assert.DoesNotContain("data-ansicht=\"heute\"] #seitenkopf { max-width: var(--u-start-max); margin-left: auto", css);
        Assert.Contains(":root { --u-start-max: 1100px; }", css);
        // alle anderen Seiten bleiben volle Breite
        Assert.Contains(":root { --u-seite-max: none; --u-seite-mittel: none; --u-seite-schmal: none; }", css);
        Assert.DoesNotContain("data-ansicht=\"plan\"] #seite { max-width", css);
    }

    [Fact]
    public void Details_zeigen_keine_Fachaenderung()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.DoesNotContain("e(k.fachAlt)", sd);
        Assert.Contains("const fachTeil = k.status === 'veranstaltung' ? e(k.fach || '') : e(k.fach || k.text || k.art || 'Stunde');", sd);
    }

    [Fact]
    public void Abwesend_auf_anwesend_oeffnet_den_Abwesenheitsdialog_mit_der_Ankunft()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("if (f) abwesenheitDialog([id], ankunftVorgabe(f), { ankunft: true });", sd);
        Assert.Contains("return { ...f, bisDatum: k.datum, bisZeit: uhrJetzt(), ankunft: true };", sd);
        Assert.Contains("data-ab-ankunft=", sd);
        Assert.DoesNotContain("if (m.has(id)) { abwesenheitEntfernen([id]); return; }", sd);
    }
}
