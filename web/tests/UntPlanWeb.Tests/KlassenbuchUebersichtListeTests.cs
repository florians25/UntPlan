using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenbuch → Übersicht (Liste aller Unterrichte des Schuljahres): erster Punkt im Untermenü, keine Wochenübersicht mehr (alte Adressen
/// führen zur Übersicht), Filter „Lehrstoff fehlt“ / „Abwesenheiten nicht kontrolliert“, Suchleiste der Administration und das Planfenster
/// der Rolle als Grenze beim Scrollen in die Zukunft. Feste Uhr: Mittwoch, 14.10.2026.
/// </summary>
public class KlassenbuchUebersichtListeTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public void Uebersicht_ist_erster_Punkt_im_Klassenbuch_und_Wochenuebersicht_gibt_es_nicht_mehr()
    {
        var app = Datei("js", "app.js");
        var start = app.IndexOf("m.push({ id: 'klassenbuch'", StringComparison.Ordinal);
        Assert.True(start > 0);
        var menue = app[start..app.IndexOf("] });", start, StringComparison.Ordinal)];
        // erster Unterpunkt = Übersicht (öffnet sich beim Klick auf „Klassenbuch“)
        var erster = menue.IndexOf("{ nav: '", StringComparison.Ordinal);
        Assert.StartsWith("{ nav: 'klassenbuch', href: '#/klassenbuch', text: 'Übersicht'", menue[erster..]);
        Assert.DoesNotContain("Wochenübersicht", menue);
        Assert.DoesNotContain("klassenbuch-woche", app);
        Assert.Contains("case 'klassenbuch': return c === 'fehlzeiten' ? 'fehlzeiten' : 'klassenbuch';", app);

        var kb = Datei("js", "ansichten", "klassenbuch.js");
        Assert.DoesNotContain("wochenraster", kb);
        Assert.DoesNotContain("/api/app/klassenbuch/woche", kb);
        // alte Adressen …/woche/<Datum> zeigen die Übersicht
        Assert.Contains("if (!['fehlzeiten', 'vermerke', 'verlauf'].includes(reiterParam))", kb);
        Assert.Contains("/uebersicht/", kb);
        Assert.DoesNotContain("/woche`", Datei("js", "ansichten", "unterricht.js"));

        // Filter, Datumspille mit Kalender, Suchleiste der Administration
        Assert.Contains(">Lehrstoff fehlt</button>", kb);
        Assert.Contains(">Abwesenheiten nicht kontrolliert</button>", kb);
        Assert.Contains("{ html: 'Heute', d: heute }, { html: 'Schuljahresbeginn', d: sjVon }, { html: 'Schuljahresende', d: sjBis }, { html: 'Kalender öffnen', kal: true }", kb);
        Assert.Contains("Bitte schränken Sie die Suche auf mindestens eine Lehrkraft oder Klasse ein.", kb);
        Assert.Contains("Ein Fach allein reicht nicht aus", kb);
        // Planfenster begrenzt das Scrollen
        Assert.Contains("const fenster = planfensterGrenzen(ich, heute);", kb);
        Assert.Contains("Die Anzeige des Stundenplans ist durch Ihren Administrator beschränkt", kb);
    }

    [Fact]
    public async Task Planfenster_der_Rolle_begrenzt_die_Uebersicht_in_die_Zukunft()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        const string Url = "/api/app/klassenbuch/uebersicht?von=2026-10-12&bis=2026-11-11&klasse=7a";
        static List<string> Tage(JsonNode u) => u["tage"]!.AsArray().Select(t => t!["datum"]!.GetValue<string>()).ToList();

        // unbegrenzt: Unterricht auch nach dem 25.10.
        var frei = await Testserver.HolenAsync(lehrer, Url);
        Assert.Contains(Tage(frei), d => string.CompareOrdinal(d, "2026-10-25") > 0);
        Assert.Null(frei["planBis"]);

        // Rolle Lehrkraft: eine Woche voraus → bis Sonntag, 25.10.2026
        var vorher = (await Testserver.HolenAsync(admin, "/api/app/admin/rechte/katalog"))["profile"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "lehrer")!;
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/lehrer",
            new { name = vorher["name"]!.GetValue<string>(), beschreibung = "", rechte = vorher["rechte"], planWochenZurueck = (int?)null, planWochenVor = 1 }));
        var begrenzt = await Testserver.HolenAsync(lehrer, Url);
        Assert.NotEmpty(Tage(begrenzt));
        Assert.All(Tage(begrenzt), d => Assert.True(string.CompareOrdinal(d, "2026-10-25") <= 0, d));
        Assert.Equal("2026-10-25", begrenzt["planBis"]!.GetValue<string>());
        Assert.Null(begrenzt["planVon"]);

        // Administration ist nicht beschränkt
        var a = await Testserver.HolenAsync(admin, Url);
        Assert.Contains(Tage(a), d => string.CompareOrdinal(d, "2026-10-25") > 0);
        Assert.Null(a["planBis"]);
    }
}
