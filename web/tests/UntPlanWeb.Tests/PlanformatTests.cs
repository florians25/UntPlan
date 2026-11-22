using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stundenplanformate (Administration → Stundenplaneinstellungen): Formate anlegen und prüfen, Plänen zuordnen; der Plan liefert das Format
/// mit Langnamen aus und wendet Tage, entfallenen Unterricht und Hervorhebungen an.
/// </summary>
public class PlanformatTests
{
    private static object Format(string name, bool entfall = true, int[]? tage = null) => new
    {
        name, details = false, farben = true, zeiten = true, stundennummer = false,
        elemente = new[] { new { art = "fach", name = "lang" }, new { art = "lehrer", name = "kurz" }, new { art = "raum", name = "kurz" } },
        tage = tage ?? new[] { 0, 1, 2, 3, 4 }, zeitVon = "", zeitBis = "",
        entfall, bereitschaften = true, sprechstunden = true, buchungen = true,
        aenderungen = true, pruefungen = true, entfaelle = true, symbole = true,
    };

    [Fact]
    public async Task Format_anlegen_zuordnen_und_im_Klassenplan_anwenden()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var f = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("Schüler*innen", entfall: false, tage: new[] { 0, 1, 2 })));
        var id = f["id"]!.GetValue<int>();
        Assert.True(id > 0);
        // gleicher Name: abgelehnt; ohne Element: abgelehnt
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("schüler*innen"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/planformate", new { name = "Leer", elemente = Array.Empty<object>() })).StatusCode);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate-zuordnung", new { klasse = id, kollegium = false }));

        // Kollegium: Standarddarstellung (kein Format)
        var lehrkraft = await s.AlsAsync("mue", "lehrer", "MUE");
        var planL = await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13");
        Assert.Null(planL["format"]);
        // Schüler*in: Format, nur Mo–Mi, kein entfallener Unterricht
        var schueler = await s.AlsAsync("schueler7a", "schueler", "7a");
        var plan = await Testserver.HolenAsync(schueler, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13");
        Assert.Equal("Schüler*innen", plan["format"]!["name"]!.GetValue<string>());
        Assert.False(plan["format"]!["details"]!.GetValue<bool>());
        Assert.Equal(3, plan["tage"]!.AsArray().Count);
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() == "entfall");
        Assert.NotNull(plan["langnamen"]);
        Assert.Equal(id, plan["formatId"]!.GetValue<int>());
        Assert.Contains(plan["formate"]!.AsArray(), x => x!["id"]!.GetValue<int>() == id && x["name"]!.GetValue<string>() == "Schüler*innen");
        // Auswahl im Menü „⋮“: Standard (0) bzw. ein bestimmtes Format, unbekannte Id = Vorgabe
        var standard = await Testserver.HolenAsync(schueler, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format=0");
        Assert.Null(standard["format"]);
        Assert.Equal(0, standard["formatId"]!.GetValue<int>());
        Assert.True(standard["tage"]!.AsArray().Count >= 5);
        var gewaehlt = await Testserver.HolenAsync(lehrkraft, $"/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format={id}");
        Assert.Equal(id, gewaehlt["formatId"]!.GetValue<int>());
        Assert.Null((await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format=999"))["format"]);

        // Kollegium einschließen
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate-zuordnung", new { klasse = id, kollegium = true }));
        Assert.NotNull((await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13"))["format"]);
        // Löschen hebt die Zuordnung auf
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/admin/planformate/{id}"));
        var liste = await Testserver.HolenAsync(admin, "/api/app/admin/planformate");
        Assert.Empty(liste["formate"]!.AsArray());
        Assert.Null(liste["zuordnung"]!["klasse"]);
        // nur Administration
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrkraft.GetAsync("/api/app/admin/planformate")).StatusCode);
    }

    [Fact]
    public void Hervorhebungen_aus_machen_geaenderte_Stunden_schlicht()
    {
        var k = new UntPlanWeb.Dienste.Karte("2026-10-13", 1, 1, "08:00", "08:45", "geaendert", "M", null, "Mathe", "#ff0000",
            new() { new("7a") }, new() { new("BEC", "MUE", true) }, new() { new("A1") }, "Vertretung", "", 1, 1, "");
        var w = new UntPlanWeb.Dienste.Wochenplan("klasse", "7a", "7a", "2026-10-12", "", new() { new(1, "1", "08:00", "08:45") },
            new() { new UntPlanWeb.Dienste.Tageskopf("2026-10-13", "Dienstag", "", false, null, false, new(), new()) }, new() { k }, new());
        var f = new UntPlanWeb.Dienste.Planformat { Id = 1, Name = "x", Elemente = new() { new("fach") }, Aenderungen = false, Farben = false, Symbole = false };
        var stand = new UntPlanWeb.Dienste.Stand(new UntPlan.Web.Contracts.WebPaket(), "2026-10-12 07:00:00");
        var r = UntPlanWeb.Dienste.Planformatdienst.Anwenden(w, f, stand);
        var x = Assert.Single(r.Karten);
        Assert.Equal("regulaer", x.Status);
        Assert.Equal("", x.Farbe);
        Assert.Null(x.Lehrer[0].Alt);
        Assert.Empty(x.Marker);
    }

    /// <summary>
    /// Standardformat (Id 0): bearbeitbar wie die übrigen Formate (fester Name, ohne Elemente „je nach Plan“), gespeichert auf dem Server,
    /// gilt für alle Pläne ohne zugeordnetes Format und im Menü „⋮“ unter „Standard“; nicht löschbar, aber auf die Vorgabe zurücksetzbar.
    /// </summary>
    [Fact]
    public async Task Standardformat_bearbeiten_anwenden_und_zuruecksetzen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var liste = await Testserver.HolenAsync(admin, "/api/app/admin/planformate");
        Assert.Equal(0, liste["standard"]!["id"]!.GetValue<int>());
        Assert.Equal("Standard", liste["standard"]!["name"]!.GetValue<string>());
        Assert.False(liste["standardGeaendert"]!.GetValue<bool>());
        Assert.Empty(liste["standard"]!["elemente"]!.AsArray());

        // bearbeiten: nur Mo–Mi, Beginn- und Endzeit auf dem Kärtchen, keine Stundennummer, kein entfallener Unterricht; Anordnung je nach Plan
        var f = await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate/0", new
        {
            name = "Anders", tage = new[] { 0, 1, 2 }, kartenZeiten = true, benutzer = true, stundennummer = false, entfall = false, zeiten = true,
            elemente = Array.Empty<object>(),
        }));
        Assert.Equal(0, f["id"]!.GetValue<int>());
        Assert.Equal("Standard", f["name"]!.GetValue<string>());
        liste = await Testserver.HolenAsync(admin, "/api/app/admin/planformate");
        Assert.True(liste["standardGeaendert"]!.GetValue<bool>());
        Assert.True(liste["standard"]!["kartenZeiten"]!.GetValue<bool>());
        Assert.True(liste["standard"]!["benutzer"]!.GetValue<bool>());
        Assert.Equal(new[] { 0, 1, 2 }, liste["standard"]!["tage"]!.AsArray().Select(x => x!.GetValue<int>()));
        // das Standardformat steht nicht unter den übrigen Formaten (die Liste im Menü „⋮“ ergänzt „Standard“ selbst)
        Assert.Empty(liste["formate"]!.AsArray());

        // Plan ohne zugeordnetes Format: das bearbeitete Standardformat (Id 0)
        var schueler = await s.AlsAsync("schueler7a", "schueler", "7a");
        var lehrkraft = await s.AlsAsync("mue", "lehrer", "MUE");
        foreach (var url in new[] { "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13", "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format=0" })
        foreach (var wer in new[] { schueler, lehrkraft })
        {
            var plan = await Testserver.HolenAsync(wer, url);
            Assert.Equal("Standard", plan["format"]!["name"]!.GetValue<string>());
            Assert.True(plan["format"]!["kartenZeiten"]!.GetValue<bool>());
            Assert.False(plan["format"]!["stundennummer"]!.GetValue<bool>());
            Assert.Equal(0, plan["formatId"]!.GetValue<int>());
            Assert.Equal(3, plan["tage"]!.AsArray().Count);
            Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() == "entfall");
        }
        // ein zugeordnetes Format hat weiter Vorrang
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("Eigenes"))))["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate-zuordnung", new { klasse = id, kollegium = true }));
        Assert.Equal("Eigenes", (await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13"))["format"]!["name"]!.GetValue<string>());
        Assert.Equal("Standard", (await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=lehrer&element=MUE&datum=2026-10-13"))["format"]!["name"]!.GetValue<string>());
        // „Standard“ ist als Name eines weiteren Formats vergeben
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("standard"))).StatusCode);

        // nicht löschbar; zurücksetzen = wieder die Standarddarstellung (kein Format im Plan)
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/app/admin/planformate/0")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrkraft.PostAsync("/api/app/admin/planformate/0/zuruecksetzen", null)).StatusCode);
        await Testserver.JsonAsync(await admin.PostAsync("/api/app/admin/planformate/0/zuruecksetzen", null));
        liste = await Testserver.HolenAsync(admin, "/api/app/admin/planformate");
        Assert.False(liste["standardGeaendert"]!.GetValue<bool>());
        var zurueck = await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=lehrer&element=MUE&datum=2026-10-13");
        Assert.Null(zurueck["format"]);
        Assert.True(zurueck["tage"]!.AsArray().Count >= 5);
    }

    /// <summary>
    /// Web (plangitter.js): das bearbeitete Standardformat ohne Elemente zeichnet die Kärtchen wie die Standarddarstellung, wendet aber
    /// seine Einstellungen an (hier: keine Stundennummer, Beginn-/Endzeit im Raster). Die Verwaltung bietet „Auf Standard zurücksetzen“.
    /// </summary>
    [Fact]
    public void Standardformat_im_Plangitter()
    {
        var wurzel = new DirectoryInfo(AppContext.BaseDirectory);
        while (wurzel != null && !File.Exists(Path.Combine(wurzel.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) wurzel = wurzel.Parent;
        var js = Path.Combine(wurzel!.FullName, "src", "UntPlanWeb", "wwwroot", "js");
        var verwaltung = File.ReadAllText(Path.Combine(js, "ansichten", "planeinstellungen.js"));
        Assert.Contains("Auf Standard zurücksetzen", verwaltung);
        Assert.Contains("data-bearbeiten=\"0\"", verwaltung);
        Assert.Contains("/0/zuruecksetzen", verwaltung);
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
        if (node is null) return;
        var pfad = new Uri(Path.Combine(js, "plangitter.js")).AbsoluteUri;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-standardformat-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(skript, """
            const stub = () => new Proxy(function () {}, { get: (t, p) => p === Symbol.toPrimitive ? () => '' : p === 'then' ? undefined : stub(), apply: () => stub(), construct: () => stub() });
            globalThis.document = stub(); globalThis.window = globalThis; globalThis.matchMedia = () => stub(); globalThis.innerHeight = 0;
            globalThis.localStorage = { getItem() { return null; }, setItem() {}, removeItem() {} };
            const pg = await import(process.argv[2]);
            let fehler = 0;
            const pruefe = (was, ok) => { if (!ok) { console.error(was); fehler++; } };
            const datum = '2030-01-07';
            const raster = [{ nr: 1, bezeichnung: '', beginn: '08:00', ende: '08:45' }, { nr: 2, bezeichnung: '', beginn: '08:50', ende: '09:35' }];
            const karte = { datum, von: 1, bis: 1, status: 'geaendert', fach: 'SPO', fachName: 'Sport', klassen: [{ k: '5a' }], marker: ['aenderung'], art: '', text: '',
              lehrer: [{ k: 'CS' }, { k: '', alt: 'SN', neu: true }, { k: 'SZ' }], raeume: [{ k: 'TH1' }] };
            const plan = format => ({ typ: 'klasse', zeitraster: raster, karten: [karte], aufsichten: [], abwesenheiten: [], format, langnamen: {},
              tage: [{ datum, hinweise: [], ereignisse: [], frei: false }] });
            const vorher = pg.wochenGitter(plan(null));
            const standard = { id: 0, name: 'Standard', elemente: [], zeiten: true, stundennummer: false, kartenZeiten: false, details: true };
            const html = pg.wochenGitter(plan(standard));
            pruefe('Stundennummer trotz Standardformat ohne Nummer', !html.includes('class="tt-nr'));
            pruefe('Stundennummer fehlt ohne Format', vorher.includes('class="tt-nr'));
            pruefe('Beginnzeit fehlt', html.includes('tt-zeit beginn'));
            // Kärtchen wie in der Standarddarstellung (gleicher Inhalt), Kopplung mit entfallener Lehrkraft: grün, SN durchgestrichen
            const inhalt = h => h.slice(h.indexOf('class="std'));
            pruefe('Kärtcheninhalt weicht von der Standarddarstellung ab', inhalt(html) === inhalt(vorher));
            pruefe('keine grüne Änderungskarte', /class="std geaendert/.test(html));
            pruefe('SN nicht durchgestrichen', html.includes('<span class="lbl weg">SN</span>'));
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
}
