using System.Diagnostics;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Pausenaufsichten in den Spuren (wie Stunden: gleiche Breite, bei Platzmangel hinter der Zählkarte), Klausur über eine
/// Aufsichtspause als eine Kachel, Karten mit mehreren Lehrkräften (abwesende Lehrkraft ohne Vertretung durchgestrichen im Label, kein „+“).
/// </summary>
public class AufsichtSpurenTests
{
    private static string AppJs => File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));

    private static string Abschnitt(string js, string name)
    {
        var beginn = js.IndexOf($"/* {name}-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf($"/* {name}-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn, $"Abschnitt {name} fehlt");
        return js[beginn..ende];
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

    private static void JsPruefen(string code)
    {
        var node = Node();
        if (node is null) return;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-spuren-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, """
            const window = {};
            const state = { entity: { code: 'BIK', label: 'BIK' }, times: { 1: { start: '08:00', end: '08:45' }, 2: { start: '08:50', end: '09:35' },
              3: { start: '09:55', end: '10:40' }, 4: { start: '10:45', end: '11:30' } } };
            const viewKind = () => 'teacher';
            const pruefe = (name, ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(name + ': ' + a + ' statt ' + b); process.exit(1); } };
            """ + code + "\nconsole.log('ok');\n");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    [Fact]
    public void Aufsichten_teilen_die_Spalte_nach_Uhrzeit_wie_Stunden()
    {
        JsPruefen(Abschnitt(AppJs, "PARALLEL") + """
            const k = (name, lesson, span, mehr = {}) => ({ name, lesson, span, blockType: 'normal', changed: [], ...mehr });
            const auf = (name, nach, from, to) => k(name, nach, 1, { blockType: 'supervision', _supervision: true, _pause: true, from, to });
            const lage = karten => { window.UntPlanParallel.assignParallel(karten); return karten.map(c => [c.name, c._i, c._n]); };
            // Aufsicht nach der 2. Stunde überlappt die 2. Stunde nicht (gleiche Nummer, andere Uhrzeit): volle Breite
            pruefe('allein', lage([k('M', 2, 1), auf('Hof', 2, '09:35', '09:55')]), [['M', 0, 1], ['Hof', 0, 1]]);
            // zwei Aufsichten derselben Pause: nebeneinander wie zwei gleichzeitige Stunden
            pruefe('zwei', lage([auf('Hof', 2, '09:35', '09:55'), auf('Halle', 2, '09:35', '09:55')]), [['Hof', 0, 2], ['Halle', 1, 2]]);
            // durchgehende Klausur bzw. Veranstaltung über die Pause: die lange Kachel bleibt vorne, die Aufsicht daneben
            pruefe('Klausur', lage([k('KL', 1, 4, { blockType: 'klausur' }), auf('Hof', 2, '09:35', '09:55')]), [['KL', 0, 2], ['Hof', 1, 2]]);
            pruefe('Feld', lage([k('AK', 1, 4, { overlay: true }), auf('Hof', 2, '09:35', '09:55')]), [['AK', 0, 2], ['Hof', 1, 2]]);
            // Pille vor der ersten Stunde bleibt außen
            pruefe('vor erster', lage([auf('Früh', 1, '07:45', '08:00'), k('M', 1, 1)]).map(x => x[0]).sort(), ['Früh', 'M']);
            pruefe('überlappt', window.UntPlanParallel.ueberlappt(auf('Hof', 2, '09:35', '09:55'), k('M', 2, 1)), false);
            """);
    }

    [Fact]
    public void Abwesende_Lehrkraft_ohne_Vertretung_steht_durchgestrichen_im_Label()
    {
        JsPruefen(Abschnitt(AppJs, "KACHELTOKENS") + """
            const t = card => window.UntPlanKachelTokens.kachelTokens(card).map(x => x.text + ':' + x.state + (x.allein ? ':allein' : '') + (x.state === 'absent' && window.UntPlanKachelTokens.koppOffen(x, []) ? ':label' : ''));
            const liste = (...l) => l.map(([code, st, f]) => ({ code, state: st, for: f }));
            // Konferenz: BAM fehlt ohne Vertretung – durchgestrichen in der grünen Plakette, die übrigen schlicht
            pruefe('Konferenz', t({ blockType: 'change', teacherList: liste(['BAM', 'absent'], ['BIK', 'ok'], ['BRN', 'ok']) }), ['BAM:absent:label', 'BIK:ok', 'BRN:ok']);
            // Vertretung: nur die Vertretung mit Label, die vertretene Lehrkraft steht nicht da
            pruefe('Vertretung', t({ blockType: 'change', teacherList: liste(['BAM', 'absent'], ['ILK', 'sub', 'BAM'], ['BRN', 'ok']) }), ['ILK:sub', 'BRN:ok']);
            // zwei Lehrkräfte, eine fehlt: weiter eine Liste mit der fehlenden durchgestrichen
            pruefe('zwei', t({ blockType: 'change', teacherList: liste(['BAM', 'absent'], ['BIK', 'ok']) }), ['BAM:absent:label', 'BIK:ok']);
            // alle fehlen bzw. Entfall: unverändert
            pruefe('alle weg', t({ blockType: 'change', teacherList: liste(['BAM', 'absent'], ['BIK', 'absent']) }), ['BAM:absent:label', 'BIK:absent:label']);
            pruefe('Entfall', t({ blockType: 'cancelled', teacherList: liste(['BAM', 'absent'], ['BIK', 'ok']) }), ['BAM:absent:label', 'BIK:ok']);
            // vertretene Lehrkraft steht auch als gewöhnliches Kürzel nicht auf der Kachel – nur die Vertretung
            pruefe('vertreten doppelt', t({ blockType: 'change', teacherList: liste(['ILK', 'sub', 'BAM'], ['BAM', 'ok']) }), ['ILK:sub']);
            pruefe('Einzelvertretung', t({ blockType: 'change', teacherList: liste(['ILK', 'sub', 'BAM'], ['BIK', 'ok'], ['BAM', 'ok']) }), ['ILK:sub', 'BIK:ok']);
            """);
    }

    [Fact]
    public void Kachel_zeigt_bei_Vertretung_nie_die_vertretene_Lehrkraft()
    {
        // kein durchgestrichenes „für“-Kürzel neben der Vertretung auf der Kachel (nur in den Details)
        Assert.DoesNotContain("<span class=\"badge badge--removed\">${esc(t.for)}</span>", AppJs);
        Assert.Contains("${esc(l.key === 'teacher' ? t.text : plusT)}", AppJs);
        // iOS-App: ebenso – kein „alt | neu“ und kein durchgestrichenes Kürzel der vertretenen Lehrkraft auf der Karte
        var ios = File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "ios", "UntPlanMobile", "Dienste", "Kachelbau.swift"));
        Assert.DoesNotContain("\\u{2009}|\\u{2009}", ios);
        Assert.DoesNotContain("hinzu(Kachelteil(text: alt, stil: .weg))", ios);
    }

    [Fact]
    public void App_js_Aufsicht_ohne_Sonderbreite_und_Zaehlkarte_nach_Pixeln()
    {
        var js = AppJs;
        Assert.DoesNotContain("aufsichtenIm", js);
        Assert.DoesNotContain("_unterFeld = true", js);
        Assert.Contains("if (!isDay && !card._pausePre && card._n > grenzeFuer(cards, card) && card._i > 0)", js);
        Assert.Contains("const yBereich = k =>", js);
        Assert.Contains("istPlus(card, l.key, t.text)", js);
        Assert.DoesNotContain("? '+' + t : t", js);
    }

    private static readonly List<PlanZeit> Zeiten = new()
    {
        new(1, "1", "08:00", "08:45"), new(2, "2", "08:50", "09:35"), new(3, "3", "09:55", "10:40"),
    };

    [Fact]
    public void Klausur_ueber_eine_Aufsichtspause_bleibt_eine_Kachel()
    {
        var plan = new Wochenplan("lehrer", "WEB", "", "2026-10-12", "A", Zeiten,
            new() { new PlanTag("2026-10-14", "Mi", "A", false, null, true, new(), new()) },
            new() { new PlanKarte("2026-10-14", 1, 3, "08:00", "10:40", "klausur", "M", null, "Mathematik", "", new() { new("5a") }, new() { new("WEB") }, new() { new("R1") }, "Klausur", "", null, 5) },
            new() { new PlanAufsicht("2026-10-14", 2, "09:35", "09:55", "Hof", "Hof", "regulaer", "WEB", null, null) });
        var tag = Kachelbau.Tag(plan, plan.Tage[0], Array.Empty<Ferienzeit>());
        var klausur = Assert.Single(tag, k => k.Art == "klausur");
        Assert.Equal(1, klausur.Stunde);
        Assert.Equal(3, klausur.Dauer);
        Assert.Single(tag, k => k.Aufsicht == true);
    }
}
