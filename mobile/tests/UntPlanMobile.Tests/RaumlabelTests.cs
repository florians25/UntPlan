using System.Diagnostics;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Raumänderungen aller Art als Kacheln der PWA: Die Karten haben genau die Form, die UntPlanWeb für die Vertretungszeilen
/// aus UntPlan liefert (raeume mit k/alt/neu/plus). Regel: neuer Raum immer als grüne Plakette (roomNew), hinzugefügter
/// als „+R“ (roomPlus), entfernter bzw. offener durchgestrichen in der grünen Plakette – Lehrkraft und Klasse ohne Label.
/// </summary>
public class RaumlabelTests
{
    private static PlanKarte Karte(List<PlanWert> raeume, string status = "geaendert", string art = "Raum-Vtr.", List<PlanWert>? lehrer = null) =>
        new("2026-10-14", 3, 3, "", "", status, "D", null, "Deutsch", "#7BAAF7", new() { new("7a") }, lehrer ?? new() { new("LAN") }, raeume, art, "", 9001, 106);

    [Theory]
    [InlineData("class")]
    [InlineData("teacher")]
    [InlineData("room")]
    public void Raumvertretung_neuer_Raum_als_Plakette(string art)
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("R109", "R105", true) }), art);
        Assert.Equal("change", k.Art);
        Assert.Equal("R109", k.Raum);
        Assert.Equal("R105", k.RaumAlt);
        Assert.Equal("R109", k.RaumNeu);
        Assert.Equal(new[] { "room" }, k.Geaendert);
        Assert.Null(k.RaumOffen);
        Assert.Null(k.RaumEntfernt);
    }

    [Fact]
    public void Raum_hinzugefuegt_als_Plus()
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("R105"), new("R109", null, true, true) }), "class");
        Assert.Equal("R105,R109", k.Raum);
        Assert.Equal("R109", k.RaumNeu);
        Assert.Equal("R109", k.RaumPlus);
        Assert.Contains("room", k.Geaendert);
    }

    [Fact]
    public void Mehrere_Raeume_neu_und_zusaetzlich()
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("R102", "PH1", true), new("R108", null, true, true) }), "class");
        Assert.Equal("R102,R108", k.Raum);
        Assert.Equal("PH1", k.RaumAlt);
        Assert.Equal("R102,R108", k.RaumNeu);
        Assert.Equal("R108", k.RaumPlus);
    }

    [Theory]
    [InlineData("geaendert")]   // Raum gelöscht
    [InlineData("offen")]       // gesperrter bzw. verdrängter Raum ohne Ersatz (UntPlan: „???“)
    public void Entfernter_oder_offener_Raum_durchgestrichen_in_der_Plakette(string status)
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("", "TH2", true) }, status), "class");
        Assert.Equal("change", k.Art);
        Assert.Equal("", k.Raum);
        Assert.Equal("TH2", k.RaumAlt);
        Assert.Contains("room", k.Geaendert);
        Assert.DoesNotContain("teacher", k.Geaendert);
    }

    [Fact]
    public void Entfernter_Raum_neben_verbleibendem_Raum_ist_keine_offene_Raumvertretung()
    {
        // Raumänderung aus UntPlanWeb bzw. aus UntPlan: von zwei Räumen bleibt einer
        var k = Kachelbau.AusKarte(Karte(new() { new("R105"), new("", "R109", true) }), "class");
        Assert.Null(k.RaumOffen);
        Assert.Equal("R105", k.Raum);
        Assert.Equal("R109", k.RaumEntfernt);
        Assert.Null(k.RaumNeu);
    }

    [Fact]
    public void Vertretung_mit_anderem_Raum_Lehrkraft_und_Raum_als_Plakette()
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("R101", "CH1", true) }, art: "Vertretung", lehrer: new() { new("MUE", "NEU", true) }), "class");
        Assert.Equal("R101", k.Raum);
        Assert.Equal("CH1", k.RaumAlt);
        Assert.Equal("R101", k.RaumNeu);
        Assert.Contains("room", k.Geaendert);
        Assert.Contains("teacher", k.Geaendert);
    }

    [Fact]
    public void Raumaenderung_aus_UntPlanWeb_vorgemerkt_bleibt_regulaer_mit_Raumplakette()
    {
        var k = Kachelbau.AusKarte(Karte(new() { new("R101", "TH1", true) }, "regulaer", "Raumänderung (vorgemerkt)"), "teacher");
        Assert.Equal("normal", k.Art);
        Assert.Equal("R101", k.Raum);
        Assert.Equal("R101", k.RaumNeu);
        Assert.Equal(new[] { "room" }, k.Geaendert);
    }

    // ------------------------------------------------------------------ app.js: Raumzeile der Kachel

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

    [Fact]
    public void Raumzeile_neuer_hinzugefuegter_und_entfernter_Raum()
    {
        var node = Node();
        if (node is null) return;
        var js = AppJs;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-raum-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, "const window = {};\nconst pruefe = (ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(a + ' statt ' + b); process.exit(1); } };\n"
            + Abschnitt(js, "TRENNER") + Abschnitt(js, "RAUMTOKENS") + """
            const t = window.UntPlanRaum.raumTokens;
            // ein Raum ersetzt: gewöhnliche Raumzeile (Plakette über changed)
            pruefe(t({ room:'R109', roomOrig:'R105', roomNew:'R109' }, true), null);
            // Raum hinzugefügt: der neue vorn als Plakette, der bisherige daneben
            pruefe(t({ room:'R105,R109', roomNew:'R109', roomPlus:'R109' }, true), [{ text:'R109', state:'sub' }, { text:'R105', state:'ok' }]);
            // neuer und zusätzlicher Raum
            pruefe(t({ room:'R102,R108', roomOrig:'PH1', roomNew:'R102,R108' }, true), [{ text:'R102', state:'sub' }, { text:'R108', state:'sub' }]);
            // entfernter Raum neben verbleibendem: verbleibender ohne Plakette, entfernter durchgestrichen in der grünen Plakette
            pruefe(t({ room:'R105', roomOrig:'R109', roomRemoved:'R109' }, true), [{ text:'R105', state:'ok' }, { text:'R109', state:'absent', open:true }]);
            // ältere Kacheln ohne roomNew: wie bisher
            pruefe(t({ room:'R105,R109', roomOrig:'R105' }, true), [{ text:'R109', state:'sub' }, { text:'R105', state:'ok' }]);
            """ + "\nconsole.log('ok');\n");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }
}
