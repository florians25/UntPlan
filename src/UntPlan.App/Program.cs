using System.Globalization;

namespace UntPlan.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;
        Thread.CurrentThread.CurrentCulture = de;
        Thread.CurrentThread.CurrentUICulture = de;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Ui.Fehler(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Ui.Fehler(ex); };
        var datei = args.FirstOrDefault(a => a.EndsWith(Core.Dokument.Dateiendung, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        var test = args.FirstOrDefault(a => a.StartsWith("--selbsttest=", StringComparison.OrdinalIgnoreCase))?["--selbsttest=".Length..];
        if (test != null) AppZustand.Einstellungen.BegruessungZeigen = false;
        var haupt = new HauptFenster(test is null ? datei : null);
        if (test != null) haupt.Shown += (_, _) => haupt.BeginInvoke(() => haupt.SelbstTest(Path.GetFullPath(test)));
        Application.Run(haupt);
    }
}
