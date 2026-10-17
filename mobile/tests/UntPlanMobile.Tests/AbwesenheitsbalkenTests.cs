using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// „Mein Plan“: eigene Abwesenheit (Lehrkraft bzw. Klasse aus den Tageshinweisen, Schüler*innen aus „Meine Abwesenheiten“)
/// als grauer Balken hinter den Stunden; im ⋮-Menü über „Abwesenheiten anzeigen“ ein- und ausblendbar (bleibt gespeichert).
/// </summary>
public class AbwesenheitsbalkenTests
{
    private static string Datei(params string[] teile) => File.ReadAllText(Path.Combine(new[] { Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile" }.Concat(teile).ToArray()));
    private static string AppJs => Datei("wwwroot", "assets", "js", "app.js");

    private static readonly List<PlanZeit> Raster = new()
    {
        new(1, "1", "07:55", "08:40"), new(2, "2", "08:45", "09:30"), new(3, "3", "09:50", "10:35"), new(4, "4", "10:40", "11:25"),
    };

    private static PlanTag Tag(string datum, params string[] hinweise) =>
        new(datum, "Mittwoch", "A", false, null, true, hinweise.ToList(), new());

    private static JsonObject Zeitraum(string von, string vonZeit, string bis, string bisZeit, string art = "fehlt") => new()
    {
        ["vonDatum"] = von, ["vonZeit"] = vonZeit, ["bisDatum"] = bis, ["bisZeit"] = bisZeit, ["art"] = art,
    };

    private static string Text(List<Abwesenheitsbalken> b) => string.Join("; ", b.Select(x => $"{x.Von}-{x.Bis} {x.Text}"));

    [Fact]
    public void Stunden_aus_den_Tageshinweisen()
    {
        Assert.Equal((1, 8, false), Wochendienst.AbwesenheitsStunden("abwesend 1.–8. Std. – Fortbildung"));
        Assert.Equal((3, (int?)null, false), Wochendienst.AbwesenheitsStunden("abwesend ab 3. Std."));
        Assert.Equal((2, 2, true), Wochendienst.AbwesenheitsStunden("Klasse abwesend 2. Std. – Exkursion"));
        Assert.Equal((4, 4, false), Wochendienst.AbwesenheitsStunden("abwesend 4. Std. – 2. Elternabend"));
        Assert.Null(Wochendienst.AbwesenheitsStunden("Raum gesperrt 1. Std."));

        var tag = Tag("2026-10-14", "abwesend 2.–3. Std. – krank", "Raum gesperrt 1. Std.");
        Assert.Equal("08:45-10:35 Abwesend", Text(Wochendienst.BalkenDesTages(tag, Raster, Array.Empty<JsonObject>())));
        Assert.Equal("09:50-11:25 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-14", "abwesend ab 3. Std."), Raster, Array.Empty<JsonObject>())));
        // freie Tage: kein Balken
        Assert.Empty(Wochendienst.BalkenDesTages(Tag("2026-10-14", "abwesend 1.–4. Std.") with { Frei = true }, Raster, Array.Empty<JsonObject>()));
    }

    [Fact]
    public void Eigene_Abwesenheiten_der_Schueler_ueber_mehrere_Tage()
    {
        var z = new[] { Zeitraum("2026-10-13", "09:00", "2026-10-15", "09:30"), Zeitraum("2026-10-14", "10:40", "2026-10-14", "10:55", "verspaetet") };
        // erster Tag ab der Uhrzeit, mittlerer Tag ganz, letzter Tag bis zur Uhrzeit; Verspätungen zählen nicht
        Assert.Equal("09:00-11:25 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-13"), Raster, z)));
        Assert.Equal("07:55-11:25 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-14"), Raster, z)));
        Assert.Equal("07:55-09:30 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-15"), Raster, z)));
        Assert.Empty(Wochendienst.BalkenDesTages(Tag("2026-10-16"), Raster, z));
        // überlappende Balken werden zusammengefasst
        var zwei = new[] { Zeitraum("2026-10-14", "08:00", "2026-10-14", "09:00"), Zeitraum("2026-10-14", "08:50", "2026-10-14", "10:00") };
        Assert.Equal("08:00-10:00 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-14"), Raster, zwei)));
        // neuere Server liefern die eigenen Abwesenheiten mit dem Plan: dann gelten nur diese (keine Hinweise, keine Zeiträume)
        var eigene = new List<PlanAbwesenheit> { new("2026-10-14", "08:45", "10:35", "K", "fehlt"), new("2026-10-15", "07:55", "11:25") };
        Assert.Equal("08:45-10:35 Abwesend", Text(Wochendienst.BalkenDesTages(Tag("2026-10-14", "abwesend 1. Std."), Raster, z, eigene)));
        Assert.Empty(Wochendienst.BalkenDesTages(Tag("2026-10-13"), Raster, z, eigene));
        Assert.Empty(Wochendienst.BalkenDesTages(Tag("2026-10-13"), Raster, z, new List<PlanAbwesenheit>()));
        // JSON für die Oberfläche: absences → { from, to, text }
        var json = JsonSerializer.Serialize(new Abwesenheitsbalken("08:00", "10:00", "Abwesend"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("{\"from\":\"08:00\",\"to\":\"10:00\",\"text\":\"Abwesend\"}", json);
    }

    [Fact]
    public void Balken_hinter_den_Karten_und_Menuepunkt()
    {
        var js = AppJs;
        // Wochenantwort und Startdaten tragen die Balken, nur im eigenen Plan
        var dienst = Datei("Dienste", "Wochendienst.cs");
        Assert.Contains("absences = woche.Absences,", dienst);
        Assert.Contains("if (type is not (\"class\" or \"teacher\" or \"room\"))", dienst);
        // Oberfläche: Balken in der Tagesspalte vor den Karten (dahinter), Text nur in der größten freien Lücke
        Assert.Contains("state.entity.type === 'mine' && state.opts.showAbsences", js);
        Assert.Contains("showAbsences:  store.get('showAbsences', true)", js);
        Assert.Contains("store.set('showAbsences', state.opts.showAbsences);", js);
        var html = Datei("Vorlagen", "index.html");
        Assert.Contains("data-act=\"toggle-absences\"", html);
        Assert.Contains("Abwesenheiten anzeigen", html);
        var css = Datei("wwwroot", "assets", "css", "app.css");
        Assert.Contains(".absfeld{", css);
        Assert.Contains("pointer-events:none;", css[css.IndexOf(".absfeld{", StringComparison.Ordinal)..]);

        var node = Node();
        if (node is null) return;
        var beginn = js.IndexOf("/* ABWESENHEITSFELD-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf("/* ABWESENHEITSFELD-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn);
        var skript = Path.Combine(Path.GetTempPath(), "untplan-abw-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, "const window = {};\nconst pruefe = (ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(a + ' statt ' + b); process.exit(1); } };\n"
            + js[beginn..ende] + """
            const y = t => { const [h, m] = t.split(':').map(Number); return h * 60 + m - 480; };
            const f = window.UntPlanAbwesenheit.abwesenheitsFeld;
            // Karte 08:00–08:45 oben: Text in der freien Lücke darunter
            pruefe(f({ from:'08:00', to:'10:00' }, [[1, 45]], y), { oben:1, hoehe:118, textOben:44, textHoehe:74, mitText:true });
            // überall Karten: kein Text
            pruefe(f({ from:'08:00', to:'09:00' }, [[1, 30], [28, 60]], y).mitText, false);
            // ohne Karten: Text über den ganzen Balken
            pruefe(f({ from:'08:00', to:'09:00' }, [], y), { oben:1, hoehe:58, textOben:0, textHoehe:58, mitText:true });
            console.log('ok');
            """);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
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
                using var p = Process.Start(new ProcessStartInfo(kandidat, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
                p!.WaitForExit(5000);
                if (p.ExitCode == 0) return kandidat;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }
}
