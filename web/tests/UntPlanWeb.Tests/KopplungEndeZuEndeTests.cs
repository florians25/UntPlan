using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Kopplungsstunden mit mehreren Zeilen, Klassen und Lehrkräften Ende zu Ende (Core → Vertretungen → echter Export → UntPlanWeb →
/// /api/app/plan): Raumänderung einer bzw. aller Zeilen, abwesende Lehrkraft mit bzw. ohne Vertretung (auch vertreten durch eine
/// beteiligte Lehrkraft) – in den Plänen jeder Lehrkraft, jeder Klasse und der Räume (alt und neu).
/// </summary>
public class KopplungEndeZuEndeTests
{
    private readonly ITestOutputHelper _aus;
    public KopplungEndeZuEndeTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private const int Std = 5;
    private static readonly string[] Klassen = { "ET13", "INF13", "ING13" };
    private static readonly string[] Lehrer = { "HE", "CS", "SZ" };

    /// <summary>Gemeinsamer Raum (B117 für alle Zeilen, eine gemeinsame Karte) bzw. eigener Raum je Zeile (B117, B118, B119).</summary>
    public enum Form { GemeinsamerRaum, EigeneRaeume }

    private static string RaumVon(Form f, int zeile) => f == Form.GemeinsamerRaum ? "B117" : "B11" + (7 + zeile);

    private static Schuldatei Schule(Form form)
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var ww = new Fach { Id = d.NeueId(), Kurzname = "WW", Langname = "Wirtschaftswissenschaften" };
        d.Faecher.Add(ww);
        var klassen = Klassen.Select(k => new Klasse { Id = d.NeueId(), Kurzname = k }).ToArray();
        var lehrer = Lehrer.Append("VX").Select(l => new Lehrer { Id = d.NeueId(), Kurzname = l }).ToArray();
        var raeume = new[] { "B117", "B118", "B119", "R200", "R201", "R202" }.Select(r => new Raum { Id = d.NeueId(), Kurzname = r }).ToArray();
        d.Klassen.AddRange(klassen);
        d.Lehrer.AddRange(lehrer);
        d.Raeume.AddRange(raeume);
        var tag = (int)Freitag.DayOfWeek - 1;
        Raum R(int zeile) => raeume.First(r => r.Kurzname == RaumVon(form, zeile));
        var zeilen = Enumerable.Range(0, 3).Select(i => new Kopplungszeile { LehrerId = lehrer[i].Id, FachId = ww.Id, KlassenIds = { klassen[i].Id }, FachraumId = R(i).Id }).ToList();
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = zeilen };
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = Std, Raeume = Enumerable.Range(0, 3).Select(i => (int?)R(i).Id).ToList() });
        return d;
    }

    private static int Id(Schuldatei d, string kurz) =>
        d.Lehrer.FirstOrDefault(l => l.Kurzname == kurz)?.Id ?? d.Raeume.FirstOrDefault(r => r.Kurzname == kurz)?.Id ?? d.Klassen.First(k => k.Kurzname == kurz).Id;

    private async Task<List<JsonNode>> KartenAsync(HttpClient c, string typ, string kurz)
    {
        var iso = Freitag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso
            && k["von"]!.GetValue<int>() <= Std && k["bis"]!.GetValue<int>() >= Std).ToList();
        _aus.WriteLine($"  {typ} {kurz}: " + (karten.Count == 0 ? "–" : string.Join("  ||  ", karten.Select(Kurz))));
        return karten;
    }

    /// <summary>Kurzform einer Karte: Status, Lehrkräfte, Klassen, Räume (alt→neu, +plus, ∅ entfernt/offen).</summary>
    private static string Kurz(JsonNode k)
    {
        static string W(JsonNode w)
        {
            var kk = w["k"]!.GetValue<string>();
            var alt = w["alt"]?.GetValue<string>();
            var neu = w["neu"]?.GetValue<bool>() == true;
            return alt is { Length: > 0 } ? $"{alt}→{(kk.Length == 0 ? "∅" : kk)}" : neu && kk.Length > 0 ? "+" + kk : kk;
        }
        string L(string f) => k[f] is JsonArray a ? string.Join(",", a.Select(w => W(w!))) : "";
        var flags = string.Join("", new[] { k["lehrerAbwesend"]?.GetValue<bool>() == true ? " [lehrerAbwesend]" : "", k["vertretungOffen"]?.GetValue<bool>() == true ? " [offen]" : "",
            k["abwesendeKlassen"] is JsonArray ak && ak.Count > 0 ? $" abwKl={string.Join(",", ak)}" : "" });
        return $"{k["status"]!.GetValue<string>()} L={L("lehrer")} K={L("klassen")}{(L("mitklassen").Length > 0 ? "+" + L("mitklassen") : "")} R={L("raeume")}{flags}";
    }

    private static async Task<Testserver> HochladenAsync(Schuldatei d, bool nurVertretungen = false)
    {
        var s = await Testserver.StartenAsync(mitPaket: false);
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
        if (nurVertretungen)
        {
            var nur = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7, Teile = new() { WebTeile.Vertretungen } });
            Assert.True((await UntPlanWebExport.HochladenAsync(p, nur with { Revision = 2 })).Ok);
        }
        return s;
    }

    private void Zeilen(Schuldatei d, string fall)
    {
        _aus.WriteLine($"=== {fall}");
        foreach (var z in Vertretungsplan.Zeilen(d, Freitag))
            _aus.WriteLine($"  Zeile: {z.Art} Kl={string.Join(",", z.KlassenIds)} {z.Lehrer}->{z.Vertreter} Raum {z.Raum}->{z.NeuerRaum}");
    }

    private async Task<Dictionary<(string, string), List<JsonNode>>> AllePlaeneAsync(Testserver s)
    {
        var c = await s.AlsAsync("admin");
        var res = new Dictionary<(string, string), List<JsonNode>>();
        foreach (var l in Lehrer.Append("VX")) res[("lehrer", l)] = await KartenAsync(c, "lehrer", l);
        foreach (var k in Klassen) res[("klasse", k)] = await KartenAsync(c, "klasse", k);
        foreach (var r in new[] { "B117", "B118", "B119", "R200", "R201", "R202" }) res[("raum", r)] = await KartenAsync(c, "raum", r);
        return res;
    }

    /// <summary>Vergleicht alle Pläne mit der Erwartung (Kurzform je Plan, mehrere Karten mit „ || “); nicht genannte Pläne sind leer.</summary>
    private static void Pruefen(Dictionary<(string, string), List<JsonNode>> plaene, Dictionary<string, string> erwartet)
    {
        var fehler = new List<string>();
        foreach (var ((typ, el), karten) in plaene)
        {
            var ist = karten.Count == 0 ? "–" : string.Join("  ||  ", karten.Select(Kurz));
            var soll = erwartet.TryGetValue($"{typ} {el}", out var e) ? e : "–";
            if (ist != soll) fehler.Add($"{typ} {el}: ist „{ist}“, soll „{soll}“");
        }
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    [Theory]
    [InlineData(Form.GemeinsamerRaum, false, false)]
    [InlineData(Form.GemeinsamerRaum, true, false)]
    [InlineData(Form.EigeneRaeume, false, false)]
    [InlineData(Form.EigeneRaeume, true, false)]
    [InlineData(Form.GemeinsamerRaum, false, true)]
    [InlineData(Form.EigeneRaeume, true, true)]
    public async Task Raumaenderung_in_der_Kopplung(Form form, bool alleZeilen, bool nurVertretungen)
    {
        var d = Schule(form);
        var u = d.Unterrichte.Single();
        // alle Zeilen: gemeinsamer Raum → alle in R200 (die Zeilen derselben Kopplung teilen sich den Raum wie vorher B117), eigene
        // Räume → R200, R201, R202
        foreach (var z in alleZeilen ? new[] { 0, 1, 2 } : new[] { 0 })
            Raumvertretungen.Zuordnen(d, u.Id, z, Freitag, new[] { Std }, Id(d, form == Form.GemeinsamerRaum ? "R200" : "R20" + z), false,
                form == Form.GemeinsamerRaum && z > 0 ? RaumKollisionsLoesung.MitKollision : RaumKollisionsLoesung.OhneKollision);
        Zeilen(d, $"Raum {form} alle={alleZeilen}");
        await using var s = await HochladenAsync(d, nurVertretungen);
        var p = await AllePlaeneAsync(s);
        // Der neue Raum steht in jedem Plan der geänderten Zeile(n) als Label („B117→R200“), unveränderte Zeilen bleiben regulär; der
        // bisherige Raum zeigt nur noch die Zeilen, die dort bleiben
        Pruefen(p, (form, alleZeilen) switch
        {
            // gemeinsamer Raum: die ganze Kopplung zieht mit um – auch wenn nur eine Zeile geändert wird (wie im Vorbild)
            (Form.GemeinsamerRaum, _) => new()
            {
                ["lehrer HE"] = "geaendert L=HE,CS,SZ K=ET13,INF13,ING13 R=B117→R200",
                ["lehrer CS"] = "geaendert L=HE,CS,SZ K=ET13,INF13,ING13 R=B117→R200",
                ["lehrer SZ"] = "geaendert L=HE,CS,SZ K=ET13,INF13,ING13 R=B117→R200",
                ["klasse ET13"] = "geaendert L=HE K=ET13+INF13,ING13 R=B117→R200",
                ["klasse INF13"] = "geaendert L=CS K=INF13+ET13,ING13 R=B117→R200",
                ["klasse ING13"] = "geaendert L=SZ K=ING13+ET13,INF13 R=B117→R200",
                ["raum R200"] = "geaendert L=HE,CS,SZ K=ET13,INF13,ING13 R=B117→R200",
            },
            (Form.EigeneRaeume, false) => new()
            {
                ["lehrer HE"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["lehrer CS"] = "regulaer L=CS K=INF13 R=B118",
                ["lehrer SZ"] = "regulaer L=SZ K=ING13 R=B119",
                ["klasse ET13"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["klasse INF13"] = "regulaer L=CS K=INF13 R=B118",
                ["klasse ING13"] = "regulaer L=SZ K=ING13 R=B119",
                ["raum B118"] = "regulaer L=CS K=INF13 R=B118",
                ["raum B119"] = "regulaer L=SZ K=ING13 R=B119",
                ["raum R200"] = "geaendert L=HE K=ET13 R=B117→R200",
            },
            _ => new()
            {
                ["lehrer HE"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["lehrer CS"] = "geaendert L=CS K=INF13 R=B118→R201",
                ["lehrer SZ"] = "geaendert L=SZ K=ING13 R=B119→R202",
                ["klasse ET13"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["klasse INF13"] = "geaendert L=CS K=INF13 R=B118→R201",
                ["klasse ING13"] = "geaendert L=SZ K=ING13 R=B119→R202",
                ["raum R200"] = "geaendert L=HE K=ET13 R=B117→R200",
                ["raum R201"] = "geaendert L=CS K=INF13 R=B118→R201",
                ["raum R202"] = "geaendert L=SZ K=ING13 R=B119→R202",
            },
        });
    }

    public enum Abwesenheitsfall { MitVertretung, OhneVertretung, VertretungDurchBeteiligte }

    [Theory]
    [InlineData(Form.GemeinsamerRaum, Abwesenheitsfall.MitVertretung)]
    [InlineData(Form.GemeinsamerRaum, Abwesenheitsfall.OhneVertretung)]
    [InlineData(Form.GemeinsamerRaum, Abwesenheitsfall.VertretungDurchBeteiligte)]
    [InlineData(Form.EigeneRaeume, Abwesenheitsfall.MitVertretung)]
    [InlineData(Form.EigeneRaeume, Abwesenheitsfall.OhneVertretung)]
    [InlineData(Form.EigeneRaeume, Abwesenheitsfall.VertretungDurchBeteiligte)]
    public async Task Abwesende_Lehrkraft_in_der_Kopplung(Form form, Abwesenheitsfall fall)
    {
        var d = Schule(form);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = Id(d, "CS"), Von = Freitag, Bis = Freitag });
        Vertretungen.Aktualisieren(d, Freitag);
        var v = d.Vertretungen.Single(x => x.Datum == Freitag && x.Stunde == Std && x.UnterrichtId.HasValue);
        if (fall == Abwesenheitsfall.MitVertretung) Vertretungen.VertreterSetzen(d, v, Id(d, "VX"));
        if (fall == Abwesenheitsfall.VertretungDurchBeteiligte) Vertretungen.VertreterSetzen(d, v, Id(d, "HE"));
        Zeilen(d, $"Abwesenheit {form} {fall}");
        await using var s = await HochladenAsync(d);
        var p = await AllePlaeneAsync(s);
        // Vertretung als Label („CS→VX“), ohne Vertretung CS durchgestrichen im Label („CS→∅“, offen); vertritt eine beteiligte Lehrkraft
        // (HE), steht sie regulär UND als Label da. Plan von CS: „abwesend“ (Eckklammern) mit allen Kolleg*innen und Klassen der Stunde.
        var neu = fall switch { Abwesenheitsfall.MitVertretung => "CS→VX", Abwesenheitsfall.OhneVertretung => "CS→∅", _ => "CS→HE" };
        var st = fall == Abwesenheitsfall.OhneVertretung ? "offen" : "geaendert";
        var offen = fall == Abwesenheitsfall.OhneVertretung ? " [offen]" : "";
        Dictionary<string, string> erwartet;
        if (form == Form.GemeinsamerRaum)
        {
            var alle = $"{st} L=HE,{neu},SZ K=ET13,INF13,ING13 R=B117{offen}";
            erwartet = new()
            {
                ["lehrer HE"] = alle,
                ["lehrer CS"] = $"abwesend L=HE,{neu},SZ K=ET13,INF13,ING13 R=B117 [lehrerAbwesend]{offen}",
                ["lehrer SZ"] = alle,
                ["klasse ET13"] = "regulaer L=HE K=ET13+INF13,ING13 R=B117",
                ["klasse INF13"] = $"{st} L={neu} K=INF13+ET13,ING13 R=B117{offen}",
                ["klasse ING13"] = "regulaer L=SZ K=ING13+ET13,INF13 R=B117",
                ["raum B117"] = alle,
            };
            if (fall == Abwesenheitsfall.MitVertretung) erwartet["lehrer VX"] = alle;
        }
        else
        {
            var karte = $"{st} L={neu} K=INF13 R=B118{offen}";
            erwartet = new()
            {
                ["lehrer HE"] = "regulaer L=HE K=ET13 R=B117" + (fall == Abwesenheitsfall.VertretungDurchBeteiligte ? "  ||  " + karte : ""),
                ["lehrer CS"] = $"abwesend L={neu} K=INF13 R=B118 [lehrerAbwesend]{offen}",
                ["lehrer SZ"] = "regulaer L=SZ K=ING13 R=B119",
                ["klasse ET13"] = "regulaer L=HE K=ET13 R=B117",
                ["klasse INF13"] = karte,
                ["klasse ING13"] = "regulaer L=SZ K=ING13 R=B119",
                ["raum B117"] = "regulaer L=HE K=ET13 R=B117",
                ["raum B118"] = karte,
                ["raum B119"] = "regulaer L=SZ K=ING13 R=B119",
            };
            if (fall == Abwesenheitsfall.MitVertretung) erwartet["lehrer VX"] = karte;
        }
        Pruefen(p, erwartet);
    }

    [Theory]
    [InlineData(Form.GemeinsamerRaum, "ET13")]
    [InlineData(Form.EigeneRaeume, "ET13")]
    [InlineData(Form.GemeinsamerRaum, "INF13")]
    public async Task Abwesende_Klasse_in_der_Kopplung(Form form, string klasse)
    {
        var d = Schule(form);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = Id(d, klasse), Von = Freitag, Bis = Freitag, Text = "Exkursion" });
        Vertretungen.Aktualisieren(d, Freitag);
        Zeilen(d, $"Klasse {klasse} abwesend {form}");
        await using var s = await HochladenAsync(d);
        var p = await AllePlaeneAsync(s);
        if (form == Form.EigeneRaeume)
        {
            // eigene Gruppe (HE, B117) nur mit ET13: die Gruppe entfällt (Freisetzung), die übrigen Gruppen laufen regulär
            Pruefen(p, new()
            {
                ["lehrer HE"] = "entfall L=HE K=ET13 R=B117",
                ["lehrer CS"] = "regulaer L=CS K=INF13 R=B118",
                ["lehrer SZ"] = "regulaer L=SZ K=ING13 R=B119",
                ["klasse ET13"] = "entfall L=HE K=ET13 R=B117",
                ["klasse INF13"] = "regulaer L=CS K=INF13 R=B118",
                ["klasse ING13"] = "regulaer L=SZ K=ING13 R=B119",
                ["raum B117"] = "entfall L=HE K=ET13 R=B117",
                ["raum B118"] = "regulaer L=CS K=INF13 R=B118",
                ["raum B119"] = "regulaer L=SZ K=ING13 R=B119",
            });
            return;
        }
        // gemeinsame Stunde: läuft weiter – grüne Karte mit der abwesenden Klasse durchgestrichen im Label, im Plan der Klasse „abwesend“
        var andere = Klassen.Where(k => k != klasse).ToArray();
        var alle = $"geaendert L=HE,CS,SZ K=ET13,INF13,ING13 R=B117 abwKl={klasse}";
        var erwartet = new Dictionary<string, string>
        {
            ["lehrer HE"] = alle, ["lehrer CS"] = alle, ["lehrer SZ"] = alle, ["raum B117"] = alle,
        };
        for (var i = 0; i < 3; i++)
        {
            var mit = string.Join(",", Klassen.Where(k => k != Klassen[i]).Select(k => k == klasse ? k + "→∅" : k));
            erwartet[$"klasse {Klassen[i]}"] = Klassen[i] == klasse
                ? $"abwesend L={Lehrer[i]} K={klasse}+{string.Join(",", andere)} R=B117 abwKl={klasse}"
                : $"geaendert L={Lehrer[i]} K={Klassen[i]}+{mit} R=B117";
        }
        Pruefen(p, erwartet);
    }
}
