using System.Globalization;

namespace UntPlan.App;

internal static class Program
{
    /// <summary>Befehlszeile (z. B. MultiUser-Anmeldung "DB~Schule~2024-2025~1 /user=… /pw=…").</summary>
    public static string[] Argumente { get; private set; } = Array.Empty<string>();

    [STAThread]
    private static void Main(string[] args)
    {
        Argumente = args;
        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;
        Thread.CurrentThread.CurrentCulture = de;
        Thread.CurrentThread.CurrentUICulture = de;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Steuerelemente.Auswahlbreite.Einschalten();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Ui.Fehler(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Ui.Fehler(ex); };
        var datei = args.FirstOrDefault(a => (a.EndsWith(Core.Dokument.Dateiendung, StringComparison.OrdinalIgnoreCase) || a.EndsWith(Core.Dokument.DateiendungAlt, StringComparison.OrdinalIgnoreCase)) && File.Exists(a));
        var test = args.FirstOrDefault(a => a.StartsWith("--selbsttest=", StringComparison.OrdinalIgnoreCase))?["--selbsttest=".Length..];
        if (test != null || args.Any(a => a.StartsWith("--druckprobe=", StringComparison.OrdinalIgnoreCase))) { AppZustand.Einstellungen.BegruessungZeigen = false; AppZustand.Einstellungen.LetzteDateiLaden = false; }
        var haupt = new HauptFenster(test is null ? datei : null);
        var probe = args.FirstOrDefault(a => a.StartsWith("--druckprobe=", StringComparison.OrdinalIgnoreCase))?["--druckprobe=".Length..];
        if (probe != null) haupt.Shown += (_, _) => haupt.BeginInvoke(() => haupt.Druckprobe(Path.GetFullPath(probe)));
        else if (test != null) haupt.Shown += (_, _) => haupt.BeginInvoke(() => haupt.SelbstTest(Path.GetFullPath(test)));
        else haupt.Shown += (_, _) => haupt.Updates.Starten(TimeSpan.FromSeconds(4));
        Application.Run(haupt);
    }
}
