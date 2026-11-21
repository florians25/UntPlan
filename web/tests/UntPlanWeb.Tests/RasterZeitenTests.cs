using System.Net;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Höhen im Stundenplanraster aus dem Zeitraster von UntPlan: Stunden, Pausen, Pausenaufsichten (auch vor der 1. Stunde) und Blöcke
/// im Verhältnis ihrer Dauer – keine festen Pausenhöhen und keine erfundenen Zeiten.
/// </summary>
public class RasterZeitenTests
{
    private static WebZeitraster Raster(params WebPause[] pausen) => new()
    {
        TageProWoche = 5,
        Stunden = new()
        {
            new(1, "", "07:55", "08:40"), new(2, "", "08:45", "09:30"), new(3, "", "09:45", "10:30"), new(4, "", "10:35", "11:20"),
            new(5, "", "11:45", "12:30"),
        },
        Pausen = pausen.ToList(),
    };

    [Fact]
    public void Pausenzeit_aus_dem_Zeitraster_auch_vor_der_ersten_und_nach_der_letzten_Stunde()
    {
        var r = Raster(new WebPause(0, "Frühaufsicht", 15), new WebPause(2, "1. große Pause", 15), new WebPause(4, "2. große Pause", 25), new WebPause(5, "Nachmittag", 10));
        Assert.Equal(("09:30", "09:45"), Plandienst.PausenZeit(r, 2));
        Assert.Equal(("11:20", "11:45"), Plandienst.PausenZeit(r, 4));
        // Frühaufsicht: Pausendauer aus UntPlan vor dem Beginn der 1. Stunde
        Assert.Equal(("07:40", "07:55"), Plandienst.PausenZeit(r, 0));
        // nach der letzten Stunde: ab deren Ende mit der Pausendauer
        Assert.Equal(("12:30", "12:40"), Plandienst.PausenZeit(r, 5));
        // ohne gepflegte Pausendauer keine erfundene Uhrzeit
        Assert.Equal(("", "07:55"), Plandienst.PausenZeit(Raster(), 0));
        Assert.Equal(("12:30", ""), Plandienst.PausenZeit(Raster(), 5));
    }

    [Fact]
    public async Task Fruehaufsicht_im_Plan_mit_Zeiten_aus_dem_Raster()
    {
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 4711;
        paket["zeitraster"]!["pausen"]!.AsArray().Add(new JsonObject { ["nachStunde"] = 0, ["bezeichnung"] = "Frühaufsicht", ["minuten"] = 15 });
        paket["pausenaufsichten"] = new JsonArray(new JsonObject { ["gang"] = "Hof", ["tag"] = 0, ["nachStunde"] = 0, ["lehrer"] = "KLE", ["wochen"] = "" },
            new JsonObject { ["gang"] = "Hof", ["tag"] = 0, ["nachStunde"] = 2, ["lehrer"] = "KLE", ["wochen"] = "" });
        await using var s = await Testserver.StartenAsync(false);
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");
        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=lehrer&element=KLE&datum=2026-10-12");
        var aufsichten = plan["aufsichten"]!.AsArray().Where(a => a!["datum"]!.GetValue<string>() == "2026-10-12").ToList();
        var frueh = aufsichten.Single(a => a!["nachStunde"]!.GetValue<int>() == 0)!;
        Assert.Equal("07:40", frueh["beginn"]!.GetValue<string>());
        Assert.Equal("07:55", frueh["ende"]!.GetValue<string>());
        var pause = aufsichten.Single(a => a!["nachStunde"]!.GetValue<int>() == 2)!;
        Assert.Equal("09:30", pause["beginn"]!.GetValue<string>());
        Assert.Equal("09:50", pause["ende"]!.GetValue<string>());
    }

    /// <summary>
    /// Web (plangitter.js): Raster mit 15- und 25-Minuten-Pausen, 40-Minuten-Mittagspause, Frühaufsicht (15 Minuten) und einer 60-Minuten-
    /// Stunde – Aufsichten füllen ihre Pausenzeile (1 px Rand), deren Höhe im Verhältnis der Pausendauer steht; Karten und Blöcke im
    /// Verhältnis der Stundendauer. Das bisherige Raster (45-Minuten-Stunden, 20-Minuten-Pausen) bleibt unverändert: Stunde 80 px, Pause 30 px.
    /// </summary>
    [Fact]
    public void Web_Hoehen_im_Verhaeltnis_der_Rasterzeiten()
    {
        var node = Node();
        var js = Datei("js", "plangitter.js");
        Assert.DoesNotContain("const PAUSE = 30", js);
        if (node is null) return;
        var pfad = new Uri(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js")).AbsoluteUri;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-raster-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(skript, """
            const stub = () => new Proxy(function () {}, { get: (t, p) => p === Symbol.toPrimitive ? () => '' : p === 'then' ? undefined : stub(), apply: () => stub(), construct: () => stub() });
            globalThis.document = stub(); globalThis.window = globalThis; globalThis.matchMedia = () => stub(); globalThis.innerHeight = 0;
            globalThis.localStorage = { getItem() { return null; }, setItem() {}, removeItem() {} };
            const pg = await import(process.argv[2]);
            let fehler = 0;
            const gleich = (ist, soll, was) => { if (Math.abs(ist - soll) > 1) { console.error(`${was}: ${ist} statt ${soll}`); fehler++; } };
            const datum = '2030-01-07';
            const karte = (von, bis, fach) => ({ datum, von, bis, status: 'regulaer', fach, fachName: fach, klassen: [{ k: '5a' }], lehrer: [{ k: 'KLE' }], raeume: [{ k: 'R1' }], marker: [], art: '', text: '' });
            const aufsicht = (nach, beginn, ende) => ({ datum, nachStunde: nach, beginn, ende, gang: 'Hof', gangName: 'Hof', status: 'regulaer', lehrer: 'KLE', alt: null });
            const lage = (html, klasse) => [...html.matchAll(new RegExp(`class="${klasse}[^"]*"[^>]*?style="[^"]*?top:(\\d+)px;height:(\\d+)px`, 'g'))].map(m => ({ top: +m[1], h: +m[2] }));
            const plan = (zeitraster, karten, aufsichten) => ({ typ: 'lehrer', zeitraster, karten, aufsichten, abwesenheiten: [], tage: [{ datum, hinweise: [], ereignisse: [], frei: false }] });

            // 1) bisheriges Raster: 45-Minuten-Stunden, 20-Minuten-Pausen → Stunde 80 px, Pausenzeile 30 px (Aufsicht 29 px)
            const alt = [['07:55', '08:40'], ['08:45', '09:30'], ['09:50', '10:35'], ['10:40', '11:25']].map(([beginn, ende], i) => ({ nr: i + 1, bezeichnung: '', beginn, ende }));
            let html = pg.wochenGitter(plan(alt, [karte(1, 1, 'D'), karte(3, 4, 'M')], [aufsicht(2, '09:30', '09:50')]));
            let std = lage(html, 'std'), auf = lage(html, 'aufsicht');
            gleich(std[0].h, 79, 'Stunde (bisher)'); gleich(std[1].h, 159, 'Doppelstunde (bisher)');
            gleich(auf[0].h, 29, 'Aufsicht 20 min (bisher)'); gleich(auf[0].top, 161, 'Lage der Aufsicht (bisher)');

            // 2) Raster mit Frühaufsicht 15 min, Pausen 15 und 25 min, Mittagspause 40 min, 6. Stunde 60 min
            const neu = [['07:55', '08:40'], ['08:45', '09:30'], ['09:45', '10:30'], ['10:35', '11:20'], ['11:45', '12:30'], ['13:10', '14:10']]
              .map(([beginn, ende], i) => ({ nr: i + 1, bezeichnung: '', beginn, ende }));
            html = pg.wochenGitter(plan(neu, [karte(1, 1, 'D'), karte(3, 4, 'M'), karte(6, 6, 'SP')],
              [aufsicht(0, '07:40', '07:55'), aufsicht(2, '09:30', '09:45'), aufsicht(4, '11:20', '11:45'), aufsicht(5, '12:30', '13:10')]));
            std = lage(html, 'std'); auf = lage(html, 'aufsicht');
            const pausen = lage(html, 'tt-pause');
            if (auf.length !== 4 || pausen.length !== 4) { console.error('Aufsichten/Pausen fehlen', auf.length, pausen.length); fehler++; }
            // Pausenzeilen zwischen den Stunden im Verhältnis der Dauer (1,5 px je Minute); die Frühaufsicht vor der 1. Stunde im Maßstab
            // der Stunden (15 von 45 Minuten einer 80-px-Stunde = 27 px); Aufsicht füllt die Zeile bis auf 1 px
            [15, 15, 25, 40].forEach((m, i) => { gleich(pausen[i].h, i === 0 ? Math.round(m * 80 / 45) : m * 1.5, `Pause ${m} min`); gleich(auf[i].h, pausen[i].h - 1, `Aufsicht ${m} min`); gleich(auf[i].top, pausen[i].top + 1, `Lage Aufsicht ${m} min`); });
            const verhaeltnis = (pausen[3].h / pausen[1].h);
            if (Math.abs(verhaeltnis - 40 / 15) > 0.1) { console.error('Pausenverhältnis', verhaeltnis); fehler++; }
            // Frühaufsicht vor der 1. Stunde: oben, danach beginnt die 1. Stunde
            gleich(pausen[0].top, 0, 'Frühaufsicht oben'); gleich(std[0].top, pausen[0].h + 1, 'Stunde 1 nach der Frühaufsicht');
            // Karten im Verhältnis der Stundendauer: 45 min = 80 px, Block 3.–4. Stunde (mit 5-Minuten-Pause ohne Zeile) 160 px, 60 min = 107 px
            gleich(std[0].h, 79, 'Stunde 45 min'); gleich(std[1].h, 159, 'Block 3.–4. Stunde'); gleich(std[2].h, 80 * 60 / 45 - 1, 'Stunde 60 min');
            console.log(fehler ? 'fehler' : 'ok');
            process.exit(fehler ? 1 : 0);
            """);
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node, $"\"{skript}\" \"{pfad}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    /// <summary>
    /// Web (plangitter.js): Läuft eine Karte (Block, Veranstaltung) über eine Pause mit Aufsicht, stehen Karte und Aufsicht nebeneinander
    /// wie parallele Stunden – die Karte halbiert (links), die Aufsicht in der rechten Hälfte der Pausenzeile; keine Aufsicht liegt über
    /// einer Karte. Aufsichten in Pausen ohne durchlaufende Karte bleiben über die ganze Breite (mehrere teilen sich die Breite).
    /// </summary>
    [Fact]
    public void Web_Aufsicht_neben_durchlaufendem_Block()
    {
        var node = Node();
        var js = Datei("js", "plangitter.js");
        Assert.Contains("spuren([...alleKarten, ...aufsichten])", js);
        // keine feste rechte Hälfte mehr per CSS (die Lage kommt aus den Spuren)
        Assert.DoesNotContain(".aufsicht.schmal { left: auto !important", Datei("css", "app.css"));
        if (node is null) return;
        var pfad = new Uri(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js")).AbsoluteUri;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-aufsichtspur-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(skript, """
            const stub = () => new Proxy(function () {}, { get: (t, p) => p === Symbol.toPrimitive ? () => '' : p === 'then' ? undefined : stub(), apply: () => stub(), construct: () => stub() });
            globalThis.document = stub(); globalThis.window = globalThis; globalThis.matchMedia = () => stub(); globalThis.innerHeight = 0;
            globalThis.localStorage = { getItem() { return null; }, setItem() {}, removeItem() {} };
            const pg = await import(process.argv[2]);
            let fehler = 0;
            const pruefe = (ist, soll, was) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(`${was}: ${a} statt ${b}`); fehler++; } };
            const datum = '2030-01-07';
            const karte = (von, bis, fach) => ({ datum, von, bis, status: 'regulaer', fach, fachName: fach, klassen: [{ k: '5a' }], lehrer: [{ k: 'KLE' }], raeume: [{ k: 'R1' }], marker: [], art: '', text: '' });
            const aufsicht = (nach, beginn, ende, gang = 'Hof') => ({ datum, nachStunde: nach, beginn, ende, gang, gangName: gang, status: 'regulaer', lehrer: 'KLE', alt: null });
            // [links %, Breite %] je Element der Klasse
            const lage = (html, klasse) => [...html.matchAll(new RegExp(`class="${klasse}[^"]*"[^>]*?style="[^"]*?left:calc\\(([\\d.]+)% \\+ 1px\\);width:calc\\(([\\d.]+)% - 1px\\)`, 'g'))].map(m => [+m[1], +m[2]]);
            const plan = (karten, aufsichten) => ({ typ: 'lehrer', karten, aufsichten, abwesenheiten: [], tage: [{ datum, hinweise: [], ereignisse: [], frei: false }],
              zeitraster: [['07:55', '08:40'], ['08:45', '09:30'], ['09:50', '10:35'], ['10:40', '11:25'], ['11:45', '12:30'], ['12:35', '13:20']].map(([beginn, ende], i) => ({ nr: i + 1, bezeichnung: '', beginn, ende })) });

            // Block 1.–4. Stunde läuft über die Pausen nach der 2. und 4. – nach der 2. eine Aufsicht: Block links halbiert, Aufsicht rechts
            let html = pg.wochenGitter(plan([karte(1, 4, 'EW')], [aufsicht(2, '09:30', '09:50', 'Hinten')]));
            pruefe(lage(html, 'std'), [[0, 50]], 'Block halbiert');
            pruefe(lage(html, 'aufsicht'), [[50, 50]], 'Aufsicht daneben');
            // Pause ohne durchlaufende Karte: Aufsicht über die ganze Breite, Karten unverändert
            html = pg.wochenGitter(plan([karte(1, 2, 'D'), karte(3, 4, 'M')], [aufsicht(2, '09:30', '09:50')]));
            pruefe(lage(html, 'std'), [[0, 100], [0, 100]], 'Karten ohne Aufsicht daneben');
            pruefe(lage(html, 'aufsicht'), [[0, 100]], 'Aufsicht in freier Pause');
            // zwei Aufsichten in einer freien Pause teilen sich die Breite (wie bisher)
            html = pg.wochenGitter(plan([karte(1, 1, 'D')], [aufsicht(2, '09:30', '09:50', 'Hof'), aufsicht(2, '09:30', '09:50', 'Halle')]));
            pruefe(lage(html, 'aufsicht'), [[0, 50], [50, 50]], 'zwei Aufsichten');
            // Block 3.–6. Stunde über die Pause nach der 4. mit Aufsicht, daneben eine freie Pause nach der 2. mit Aufsicht: nur die über
            // dem Block halbiert; eine Karte vor dem Block (1.–2.) bleibt ganz
            html = pg.wochenGitter(plan([karte(1, 2, 'D'), karte(3, 6, 'EW')], [aufsicht(2, '09:30', '09:50'), aufsicht(4, '11:25', '11:45', 'Hinten')]));
            pruefe(lage(html, 'std'), [[0, 100], [0, 50]], 'nur der Block halbiert');
            pruefe(lage(html, 'aufsicht'), [[0, 100], [50, 50]], 'Aufsichten');
            // grüne Karte (Vertretung) über die Pause: ebenso links, Aufsicht rechts
            html = pg.wochenGitter(plan([{ ...karte(1, 4, 'EW'), status: 'geaendert' }], [{ ...aufsicht(2, '09:30', '09:50'), status: 'offen' }]));
            pruefe(lage(html, 'std'), [[0, 50]], 'grüner Block halbiert');
            pruefe(lage(html, 'aufsicht'), [[50, 50]], 'offene Aufsicht daneben');
            console.log(fehler ? 'fehler' : 'ok');
            process.exit(fehler ? 1 : 0);
            """);
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node, $"\"{skript}\" \"{pfad}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    private static string? Node()
    {
        foreach (var kandidat in new[] { "node", "/opt/node22/bin/node", "/usr/bin/node", "/usr/local/bin/node" })
        {
            try
            {
                using var v = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(kandidat, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
                v!.WaitForExit(5000);
                if (v.ExitCode == 0) return kandidat;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Datei(params string[] teile) =>
        File.ReadAllText(Path.Combine(new[] { Wurzel(), "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
}
