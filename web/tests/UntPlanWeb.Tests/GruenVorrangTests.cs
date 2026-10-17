namespace UntPlanWeb.Tests;

/// <summary>
/// Plangitter (plangitter.js `spuren()`): bei parallelen Blöcken (Kopplung, Überlappung) stehen grüne Karten (Änderung,
/// Vertretung, Raumänderung, Zusatzstunde, offene Vertretung) immer ganz links; danach die bisherige Reihenfolge. Die
/// Spurverteilung bleibt gültig (keine Überlappung innerhalb einer Spur), ohne grüne Karten ist sie unverändert.
/// </summary>
public class GruenVorrangTests
{
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

    [Fact]
    public void Gruene_Karten_stehen_bei_parallelen_Bloecken_links()
    {
        var node = Node();
        if (node is null) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        var js = new Uri(Path.Combine(dir!.FullName, "src", "UntPlanWeb", "wwwroot", "js", "plangitter.js")).AbsoluteUri;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-gruen-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(skript, """
            const stub = () => new Proxy(function () {}, { get: (t, p) => p === Symbol.toPrimitive ? () => '' : p === 'then' ? undefined : stub(), apply: () => stub(), construct: () => stub() });
            globalThis.document = stub(); globalThis.window = globalThis; globalThis.matchMedia = () => stub();
            globalThis.localStorage = { getItem() { return null; }, setItem() {}, removeItem() {} };
            const pg = await import(process.argv[2]);
            let fehler = 0;
            const pruefe = (name, ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(name + ': ' + a + ' statt ' + b); fehler++; } };
            const k = (name, von, bis, status = 'regulaer') => ({ name, von, bis, status });
            const lage = karten => { const l = pg.spuren(karten); return karten.map(x => [x.name, l.get(x).spur, l.get(x).anzahl]); };

            // Kopplung: Vertretung als zweite geliefert – ganz links
            pruefe('Kopplung', lage([k('KR', 3, 4), k('ER', 3, 4, 'geaendert'), k('PL', 3, 4, 'entfall')]), [['KR', 1, 3], ['ER', 0, 3], ['PL', 2, 3]]);
            // langer Entfall, grüne Stunde in der 3./4.
            pruefe('Entfall lang', lage([k('E', 1, 4, 'entfall'), k('G', 3, 4, 'geaendert')]), [['E', 1, 2], ['G', 0, 2]]);
            // offene Vertretung ist ebenfalls grün
            pruefe('offen', lage([k('R', 2, 2), k('O', 2, 2, 'offen')]), [['R', 1, 2], ['O', 0, 2]]);
            // nichts links neben einer gleichzeitigen grünen Karte, auch wenn dort eine Spur frei wäre
            pruefe('Kette', lage([k('G1', 1, 3, 'geaendert'), k('G2', 3, 4, 'geaendert'), k('N', 4, 4)]), [['G1', 0, 3], ['G2', 1, 3], ['N', 2, 3]]);
            // ohne grüne Karten wie bisher
            pruefe('ohne Grün', lage([k('A', 1, 2), k('B', 2, 3), k('C', 3, 4)]), [['A', 0, 2], ['B', 1, 2], ['C', 0, 2]]);
            pruefe('Veranstaltung nicht grün', pg.istGrueneKarte({ status: 'veranstaltung' }), false);

            // Zufall: keine Überlappung in einer Spur, nie eine nicht-grüne Karte links neben einer gleichzeitigen grünen
            let s = 11; const zufall = n => { s = (s * 1103515245 + 12345) % 2147483648; return s % n; };
            const ueber = (a, b) => a.von <= b.bis && b.von <= a.bis;
            const STATUS = ['regulaer', 'geaendert', 'entfall', 'klausur', 'offen'];
            for (let r = 0; r < 3000 && !fehler; r++) {
              const karten = Array.from({ length: 1 + zufall(8) }, (_, i) => { const v = 1 + zufall(8); return k('k' + i, v, Math.min(10, v + zufall(3)), STATUS[zufall(5)]); });
              const l = pg.spuren(karten);
              for (const a of karten) for (const b of karten) {
                if (a === b || !ueber(a, b)) continue;
                if (l.get(a).spur === l.get(b).spur) { console.error('Überlappung', JSON.stringify(karten)); fehler++; }
                if (l.get(a).anzahl !== l.get(b).anzahl) { console.error('Anzahl', JSON.stringify(karten)); fehler++; }
                if (pg.istGrueneKarte(a) && !pg.istGrueneKarte(b) && l.get(b).spur < l.get(a).spur) { console.error('nicht grün links', JSON.stringify(karten)); fehler++; }
              }
              for (const a of karten) if (l.get(a).spur >= l.get(a).anzahl) { console.error('Spur außerhalb', JSON.stringify(karten)); fehler++; }
            }
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
}
