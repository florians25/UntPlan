using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace UntPlanStudio.Tests;

/// <summary>Prüfungen der Oberflächendateien ohne Browser: Syntax aller JS-Module (Node), Verweise, Modulregister.</summary>
public sealed partial class OberflaecheTests
{
    private readonly ITestOutputHelper _aus;

    public OberflaecheTests(ITestOutputHelper aus) => _aus = aus;

    private static string Wwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "UntPlanStudio", "wwwroot"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "UntPlanStudio", "wwwroot");
    }

    private static IEnumerable<string> JsDateien() => Directory.EnumerateFiles(Path.Combine(Wwwroot(), "js"), "*.js", SearchOption.AllDirectories);

    private static string? NodePfad()
    {
        foreach (var verz in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Append("/opt/node22/bin"))
        {
            var p = Path.Combine(verz, OperatingSystem.IsWindows() ? "node.exe" : "node");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void AlleJsModuleSindSyntaktischKorrekt()
    {
        var node = NodePfad();
        if (node is null)
        {
            _aus.WriteLine("Node.js nicht gefunden – Syntaxprüfung übersprungen.");
            return;
        }
        var fehler = new List<string>();
        foreach (var datei in JsDateien())
        {
            var psi = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("--experimental-default-type=module");
            psi.ArgumentList.Add("--check");
            psi.ArgumentList.Add(datei);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) fehler.Add($"{Path.GetFileName(datei)}: {err}");
        }
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    [GeneratedRegex(@"^\s*import\s+(?:[^'""]*?\s+from\s+)?['""](\.[^'""]+)['""]", RegexOptions.Multiline)]
    private static partial Regex ImportMuster();

    [Fact]
    public void AlleImportsVerweisenAufVorhandeneDateien()
    {
        foreach (var datei in JsDateien())
            foreach (Match m in ImportMuster().Matches(File.ReadAllText(datei)))
            {
                var ziel = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(datei)!, m.Groups[1].Value));
                Assert.True(File.Exists(ziel), $"{Path.GetFileName(datei)} importiert {m.Groups[1].Value}, die Datei fehlt.");
            }
    }

    [Fact]
    public void JedesModulIstImRegisterEingetragenUndHatEineId()
    {
        var modulordner = Path.Combine(Wwwroot(), "js", "module");
        var register = File.ReadAllText(Path.Combine(modulordner, "register.js"));
        foreach (var datei in Directory.EnumerateFiles(modulordner, "*.js").Where(d => Path.GetFileName(d) != "register.js"))
        {
            var name = Path.GetFileName(datei);
            Assert.Contains($"'./{name}'", register);
            Assert.Matches(@"export default \{\s*\n\s*id: '", File.ReadAllText(datei));
        }
    }

    [Fact]
    public void IndexHtmlBindetNurVorhandeneDateienEin()
    {
        var html = File.ReadAllText(Path.Combine(Wwwroot(), "index.html"));
        foreach (Match m in Regex.Matches(html, @"(?:href|src)=""([^""#:]+)"""))
            Assert.True(File.Exists(Path.Combine(Wwwroot(), m.Groups[1].Value)), $"index.html verweist auf fehlende Datei {m.Groups[1].Value}");
        // keine externen Quellen (kein CDN)
        Assert.DoesNotMatch(@"(?:href|src)=""https?://", html);
    }
}
