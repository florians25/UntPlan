using System.Diagnostics;

namespace UntPlanMobile.Tests;

/// <summary>
/// Parallele Blöcke (Kopplung, Überlappung): grüne Karten (Änderung, Vertretung, Raumänderung, Zusatzstunde) stehen immer
/// ganz links; bleibt wegen Platzmangels nur ein Block mit Zählkarte (iPhone-Woche), ist der sichtbare Block der grüne.
/// Danach gilt die bisherige Rangfolge. Ausgeführt wird der Abschnitt PARALLEL aus app.js mit Node.
/// </summary>
public class GruenVorrangTests
{
    private static string AppJs => File.ReadAllText(Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "js", "app.js"));

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

    private static string Abschnitt(string js, string name)
    {
        var beginn = js.IndexOf($"/* {name}-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf($"/* {name}-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn, $"Abschnitt {name} fehlt");
        return js[beginn..ende];
    }

    [Fact]
    public void Gruene_Karten_stehen_bei_parallelen_Bloecken_vorne()
    {
        var node = Node();
        if (node is null) return;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-gruen-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, """
            const window = {};
            const state = { entity: { code: 'WEB', label: 'WEB' } };
            let ansicht = 'teacher';
            const viewKind = () => ansicht;
            let fehler = 0;
            const pruefe = (name, ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(name + ': ' + a + ' statt ' + b); fehler++; } };
            """ + Abschnitt(AppJs, "PARALLEL") + """
            const k = (name, lesson, span, blockType = 'normal', mehr = {}) => ({ name, lesson, span, blockType, changed: [], ...mehr });
            const lage = karten => { window.UntPlanParallel.assignParallel(karten); return karten.map(c => [c.name, c._i, c._n]); };

            // Kopplung: Vertretung (grün) als zweite geliefert – steht ganz links, Entfall zuletzt
            pruefe('Kopplung', lage([k('KR', 2, 1), k('ER', 2, 1, 'change', { changed: ['teacher'] }), k('PL', 2, 1, 'cancelled')]),
              [['KR', 1, 3], ['ER', 0, 3], ['PL', 2, 3]]);
            // Raumänderung (grün) vor der unveränderten Stunde
            pruefe('Raumänderung', lage([k('M', 3, 2), k('D', 3, 2, 'change', { changed: ['room'] })]), [['M', 1, 2], ['D', 0, 2]]);
            // langer Entfall, grüne Stunde in der 3./4.: grün links (sichtbar vor der Zählkarte)
            pruefe('Entfall lang', lage([k('E', 1, 4, 'cancelled'), k('G', 3, 2, 'change')]), [['E', 1, 2], ['G', 0, 2]]);
            // grüne Stunde später in der Kette: nichts steht links neben ihr, auch wenn dort eine Spalte frei wäre
            pruefe('Kette', lage([k('G1', 1, 3, 'change'), k('G2', 3, 2, 'change'), k('N', 4, 1)]), [['G1', 0, 3], ['G2', 1, 3], ['N', 2, 3]]);
            // reguläre Stunde vor grüner, die erst später beginnt: grün trotzdem links
            pruefe('später grün', lage([k('N', 1, 2), k('G', 2, 1, 'change')]), [['N', 1, 2], ['G', 0, 2]]);
            // ohne grüne Karten bleibt alles wie bisher (nach Anfangsstunde, längere zuerst)
            pruefe('ohne Grün', lage([k('A', 2, 1), k('B', 1, 2), k('C', 3, 1)]), [['A', 1, 2], ['B', 0, 2], ['C', 0, 1]]);
            // abgegebene Stunde (läuft ohne mich, Eckklammern statt grün) hat keinen Vorrang
            pruefe('abgegeben', lage([k('AB', 2, 1, 'change', { changed: ['teacher'], teacherOrig: 'WEB', teacher: 'MUE' }), k('R', 2, 1)]),
              [['AB', 1, 2], ['R', 0, 2]]);
            pruefe('grün?', window.UntPlanParallel.istGrueneKarte(k('X', 1, 1, 'change', { openSub: true }), 'teacher'), false);
            ansicht = 'room';
            pruefe('grün im Raumplan', window.UntPlanParallel.istGrueneKarte(k('X', 1, 1, 'change', { openSub: true }), 'room'), true);
            // Zufall: keine Überlappung innerhalb einer Spalte, nie eine nicht-grüne links neben einer gleichzeitigen grünen
            ansicht = 'class';
            let s = 7; const zufall = n => { s = (s * 1103515245 + 12345) % 2147483648; return s % n; };
            const ueber = (a, b) => a.lesson < b.lesson + b.span && b.lesson < a.lesson + a.span;
            for (let r = 0; r < 3000; r++) {
              const karten = Array.from({ length: 1 + zufall(7) }, (_, i) => k('k' + i, 1 + zufall(6), 1 + zufall(3), zufall(3) === 0 ? 'change' : zufall(4) === 0 ? 'cancelled' : 'normal'));
              window.UntPlanParallel.assignParallel(karten);
              for (const a of karten) for (const b of karten) {
                if (a === b || !ueber(a, b)) continue;
                if (a._i === b._i) { console.error('Überlappung', JSON.stringify(karten)); fehler++; }
                if (a.blockType === 'change' && b.blockType !== 'change' && b._i < a._i) { console.error('nicht grün links', JSON.stringify(karten)); fehler++; }
              }
              if (fehler) break;
            }
            console.log(fehler ? 'fehler' : 'ok');
            process.exit(fehler ? 1 : 0);
            """);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    [Fact]
    public void Zaehlkarte_zeigt_vorne_die_Karte_der_ersten_Spalte()
    {
        // verdrängt werden nur Karten ab der zweiten Spalte – die erste Spalte gehört damit den grünen Karten
        var js = AppJs;
        Assert.Contains("card._n > grenzeFuer(cards, card) && card._i > 0", js);
        Assert.Contains("const vorrang = c => rank(c) < 4 && istGrueneKarte(c, kind) ? -1 : rank(c);", js);
        Assert.Contains("card._released = freigegeben(card, kind);", js);
    }
}
