using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>
/// Veranstaltungskarte: mit eingetragenen Lehrkräften oben die Lehrkräfte, darunter der Text der Veranstaltung, dann der Raum
/// (hinzugefügter Wert – Web „+A103“, Apps grünes Label); ohne Lehrkräfte steht der Text oben. Server (Plandienst, /api/app/plan) und Web (plangitter.js).
/// </summary>
public class VeranstaltungKarteTests
{
    private static readonly DateOnly Do = new(2026, 10, 15);

    private static (Schuldatei D, List<Lehrer> Lehrer, Klasse Klasse, Raum Raum) SchuleMitKonferenz()
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        var tp = Tagesplan.Berechnen(d, Do);
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren && !tp.Stunde(3).Any(e => e.Findetstatt && e.LehrerIds.Contains(l.Id))).Take(3).ToList();
        var raum = d.Raeume.First(x => !x.Ignorieren && !tp.Stunde(3).Any(e => e.RaumIds.Contains(x.Id)));
        var klasse = d.Klassen.First(k => !k.Ignorieren);
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = Do, Bis = Do, StundeVon = 3, StundeBis = 3, KlassenIds = { klasse.Id },
            LehrerIds = lehrer.Select(l => l.Id).ToList(), RaumIds = { raum.Id }, Text = "Lehrkräftekonferenz" });
        Vertretungen.Aktualisieren(d, Do);
        return (d, lehrer, klasse, raum);
    }

    private static WebPaket Paket(Schuldatei d) => UntPlanWebExport.PaketErstellen(d, Do.AddDays(-3), new WebExportOptionen { TageVoraus = 7 });

    [Fact]
    public void Raum_der_Veranstaltung_ist_hinzugefuegt_in_allen_Plaenen()
    {
        var (d, lehrer, klasse, raum) = SchuleMitKonferenz();
        var ix = new DatenIndex(d);
        var s = new Stand(Paket(d), "");
        var rk = ix.K(ElementTyp.Raum, raum.Id);
        var plan = new Plandienst();
        foreach (var (typ, el) in new[] { ("lehrer", ix.KLehrer(lehrer[0].Id)), ("klasse", ix.K(ElementTyp.Klasse, klasse.Id)), ("raum", rk) })
        {
            var k = Assert.Single(plan.Karten(s, Do, typ, el), x => x.Status == "veranstaltung" && x.Von <= 3 && x.Bis >= 3);
            Assert.Equal("Lehrkräftekonferenz", k.Text);
            var w = Assert.Single(k.Raeume);
            Assert.Equal(rk, w.K);
            Assert.True(w.Plus);
            Assert.Equal(lehrer.Count, k.Lehrer.Count);
            Assert.All(k.Lehrer, x => Assert.False(x.Neu));
            // grünes Änderungs-Viereck bleibt, kein „+“-Symbol
            Assert.Equal(new[] { "aenderung" }, k.Marker);
        }
    }

    [Fact]
    public void Ersetzte_Raeume_und_andere_Karten_bleiben_unveraendert()
    {
        Karte K(string status, List<Wert> raeume) => new("2026-10-15", 3, 3, "", "", status, "", null, "", "", new(), new(), raeume, "Veranstaltung", "X", null, null, "");
        var v = Plandienst.RaeumeHinzugefuegt(K("veranstaltung", new() { new("A103"), new("B070", "B071", true), new("", "C1", true) }));
        Assert.True(v.Raeume[0].Plus);
        Assert.Equal(new Wert("B070", "B071", true), v.Raeume[1]);
        Assert.Equal(new Wert("", "C1", true), v.Raeume[2]);
        Assert.False(Plandienst.RaeumeHinzugefuegt(K("geaendert", new() { new("A103") })).Raeume[0].Neu);
    }

    [Fact]
    public async Task App_API_liefert_Lehrkraefte_Text_und_hinzugefuegten_Raum()
    {
        await using var server = await Testserver.StartenAsync(mitPaket: false);
        var (d, lehrer, _, raum) = SchuleMitKonferenz();
        var p = new PortalEinstellungen { Adresse = server.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        Assert.True((await UntPlanWebExport.HochladenAsync(p, Paket(d) with { Revision = 1 })).Ok);
        var ix = new DatenIndex(d);
        var lk = ix.KLehrer(lehrer[0].Id);
        var c = await server.AlsAsync(lk.ToLowerInvariant(), "lehrer", lk);
        var karte = (await Testserver.HolenAsync(c, $"/api/app/plan?typ=lehrer&element={Uri.EscapeDataString(lk)}&datum={Do:yyyy-MM-dd}"))["karten"]!.AsArray()
            .Select(x => x!).Single(x => x["datum"]!.GetValue<string>() == $"{Do:yyyy-MM-dd}" && x["status"]!.GetValue<string>() == "veranstaltung");
        Assert.Equal("Lehrkräftekonferenz", karte["text"]!.GetValue<string>());
        Assert.Equal(lehrer.Select(l => ix.KLehrer(l.Id)).OrderBy(x => x), karte["lehrer"]!.AsArray().Select(w => w!["k"]!.GetValue<string>()).OrderBy(x => x));
        var r = Assert.Single(karte["raeume"]!.AsArray())!;
        Assert.Equal(ix.K(ElementTyp.Raum, raum.Id), r["k"]!.GetValue<string>());
        Assert.True(r["neu"]!.GetValue<bool>());
        Assert.True(r["plus"]!.GetValue<bool>());
        Assert.Contains("aenderung", karte["marker"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    /// <summary>Web (plangitter.js): Reihenfolge Lehrkräfte · Text · „+Raum“; ohne Lehrkräfte der Text oben.</summary>
    [Fact]
    public void Web_Karte_Lehrkraefte_Text_Raum()
    {
        string? node = null;
        foreach (var kandidat in new[] { "node", "/opt/node22/bin/node", "/usr/bin/node", "/usr/local/bin/node" })
        {
            try
            {
                using var v = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(kandidat, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
                v!.WaitForExit(5000);
                if (v.ExitCode == 0) { node = kandidat; break; }
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        var quelle = File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js"));
        // Veranstaltungen aus dem Tageskopf (ohne Karte): Raum ebenfalls als hinzugefügter Wert
        Assert.Contains("raeume: x.raeume.map(k => ({ k, neu: true }))", quelle);
        if (node is null) return;
        var js = new Uri(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js")).AbsoluteUri;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-vk-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(skript, """
            const stub = () => new Proxy(function () {}, { get: (t, p) => p === Symbol.toPrimitive ? () => '' : p === 'then' ? undefined : stub(), apply: () => stub(), construct: () => stub() });
            globalThis.document = stub(); globalThis.window = globalThis; globalThis.matchMedia = () => stub();
            globalThis.localStorage = { getItem() { return null; }, setItem() {}, removeItem() {} };
            const pg = await import(process.argv[2]);
            let fehler = 0;
            const soll = (ja, text) => { if (!ja) { console.error(text); fehler++; } };
            const karte = lehrer => ({ status: 'veranstaltung', fach: '', klassen: [], lehrer, raeume: [{ k: 'A103', neu: true, plus: true }], marker: ['aenderung'], art: 'Veranstaltung', text: 'Lehrkräftekonferenz' });
            const mit = karte([{ k: 'AI' }, { k: 'AY' }, { k: 'BA' }]);
            for (const typ of ['klasse', 'lehrer', 'raum']) {
              const html = pg.karteInhalt(mit, typ, false);
              const z1 = html.indexOf('class="z1'), ai = html.indexOf('AI'), text = html.indexOf('Lehrkräftekonferenz'), raum = html.indexOf('<span class="lbl lbl-plus">+A103</span>');
              soll(z1 >= 0 && ai > z1 && text > ai && raum > text, `${typ}: Reihenfolge Lehrkräfte, Text, +Raum: ${html}`);
              soll(html.slice(z1, text).includes('z-liste'), `${typ}: Lehrkräfte in der ersten Zeile: ${html}`);
              soll(html.includes('mk-quadrat'), `${typ}: Änderungs-Viereck fehlt: ${html}`);
            }
            const ohne = karte([]);
            for (const typ of ['klasse', 'lehrer', 'raum']) {
              const html = pg.karteInhalt(ohne, typ, false);
              soll(html.includes('<div class="z1" data-voll="Lehrkräftekonferenz">Lehrkräftekonferenz</div>'), `${typ}: Text oben: ${html}`);
              soll(html.includes('<span class="lbl lbl-plus">+A103</span>'), `${typ}: +Raum: ${html}`);
            }
            // eigener Lehrerplan nur mit der eigenen Lehrkraft: Text oben
            const eigen = pg.karteInhalt(karte([{ k: 'AI' }]), 'lehrer', false);
            soll(eigen.includes('<div class="z1" data-voll="Lehrkräftekonferenz">Lehrkräftekonferenz</div>'), 'eigener Plan: ' + eigen);
            // sehr viele Lehrkräfte: eine gekürzte Zeile (Hover mit allen als Pillen)
            const viele = pg.karteInhalt(karte(Array.from({ length: 40 }, (_, i) => ({ k: 'L' + i }))), 'klasse', false);
            soll(viele.includes('class="z1 z-liste"') && viele.includes('L39'), 'viele: ' + viele);
            console.log(fehler ? 'fehler' : 'ok');
            process.exit(fehler ? 1 : 0);
            """);
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node, $"\"{skript}\" \"{js}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
