using System.Diagnostics;
using UntPlan.App.Update;

namespace UntPlan.App;

/// <summary>
/// Updateanzeige im Desktop wie in UntPlanWeb/UntPlanMobile: beim Start (verzögert, im Hintergrund) werden die
/// GitHub-Releases abgefragt; ist eine neuere Version veröffentlicht, erscheint rechts in der Statusleiste
/// „Update verfügbar: 1.0.x“ – ein Klick öffnet den Hinweis mit „Herunterladen“ (Release-Seite mit Setup-EXE).
/// „Nach Updates suchen“ (Hilfe) prüft sofort. Abschaltbar mit der Umgebungsvariablen <c>UNTPLAN_UPDATE_PRUEFEN=0</c>,
/// andere Quelle mit <c>UNTPLAN_UPDATE_URL</c> (gleiche Formate wie bei UntPlanWeb).
/// </summary>
internal sealed class Updateanzeige
{
    /// <summary>Alle Releases, damit nur Desktop-Tags („v1.0.x“) zählen, auch wenn zuletzt etwas anderes veröffentlicht wurde.</summary>
    public const string Standardquelle = "https://api.github.com/repos/florians25/UntPlan/releases?per_page=30";
    public const string Releaseseite = "https://github.com/florians25/UntPlan/releases/latest";

    private static readonly HttpClient Http = new();
    private readonly ToolStripStatusLabel _label;
    private readonly Control _besitzer;

    public Updatepruefung Pruefung { get; }

    /// <summary>Zuletzt angezeigter Stand (für den Selbsttest und den Hinweisdialog).</summary>
    public Updatestand? Stand { get; private set; }

    public ToolStripStatusLabel Label => _label;

    public Updateanzeige(Control besitzer)
    {
        _besitzer = besitzer;
        var url = Environment.GetEnvironmentVariable("UNTPLAN_UPDATE_URL");
        var aktiv = Updatepruefung.Schalter(Environment.GetEnvironmentVariable("UNTPLAN_UPDATE_PRUEFEN"), true);
        Pruefung = new Updatepruefung(Http, typeof(Updateanzeige).Assembly, string.IsNullOrWhiteSpace(url) ? Standardquelle : url, aktiv, "v");
        _label = new ToolStripStatusLabel
        {
            Visible = false,
            IsLink = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = Color.White,
            ActiveLinkColor = Color.White,
            BackColor = Color.FromArgb(232, 126, 4),
            ForeColor = Color.White,
            Font = new Font(Ui.Schrift, FontStyle.Bold),
            Margin = new Padding(6, 2, 2, 2),
            Padding = new Padding(6, 0, 6, 0),
            ToolTipText = "Eine neuere Version von UntPlan ist verfügbar – klicken für Details",
        };
        _label.Click += (_, _) => HinweisZeigen();
    }

    /// <summary>Laufende Version (z. B. 1.0.69).</summary>
    public string Version => Pruefung.Version;

    /// <summary>Prüfung im Hintergrund starten (nach dem Start verzögert, damit das Öffnen nicht gebremst wird).</summary>
    public void Starten(TimeSpan verzoegerung)
    {
        if (!Pruefung.Aktiv) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(verzoegerung);
                var stand = await Pruefung.StandAsync();
                Anwenden(stand);
            }
            catch { /* keine Verbindung: still bleiben */ }
        });
    }

    /// <summary>„Nach Updates suchen“: sofort prüfen und das Ergebnis immer melden.</summary>
    public async void JetztPruefen()
    {
        if (!Pruefung.Aktiv)
        {
            Ui.Info($"Installierte Version: {Version}\n\nDie Prüfung auf Updates ist abgeschaltet (UNTPLAN_UPDATE_PRUEFEN=0).", "Nach Updates suchen");
            return;
        }
        Updatestand stand;
        using (new WarteCursor()) stand = await Pruefung.StandAsync(erzwingen: true);
        Anwenden(stand);
        if (stand.UpdateVerfuegbar) HinweisZeigen();
        else if (stand.Fehler != null)
            Ui.Info($"Installierte Version: {Version}\n\nDie Prüfung auf Updates war nicht möglich:\n{stand.Fehler}", "Nach Updates suchen");
        else
            Ui.Info($"UntPlan ist auf dem neuesten Stand.\n\nInstallierte Version: {Version}\nNeueste Version: {stand.Neueste}", "Nach Updates suchen");
    }

    /// <summary>Ergebnis in der Statusleiste zeigen (auch aus dem Hintergrund-Thread aufrufbar).</summary>
    public void Anwenden(Updatestand stand)
    {
        if (_besitzer.IsDisposed) return;
        if (_besitzer.InvokeRequired) { _besitzer.BeginInvoke(() => Anwenden(stand)); return; }
        Stand = stand;
        _label.Visible = stand.UpdateVerfuegbar;
        _label.Text = stand.UpdateVerfuegbar ? $"Update verfügbar: {stand.Neueste}" : "";
    }

    /// <summary>Text des Hinweises (eigene Methode, damit der Selbsttest ihn prüfen kann).</summary>
    public string HinweisText(Updatestand s) =>
        $"Eine neue Version von UntPlan ist verfügbar.\n\nInstallierte Version: {s.Version}\nNeue Version: {s.Neueste}\n\n" +
        "Jetzt herunterladen? Es öffnet sich die Seite mit der Setup-EXE (UntPlan-Setup-*.exe) und der portablen Version.\n" +
        "Die Schuldatei bleibt beim Update unverändert.";

    public void HinweisZeigen()
    {
        if (Stand is not { UpdateVerfuegbar: true } s) return;
        if (Ui.Frage(HinweisText(s), "UntPlan - Update verfügbar"))
            Oeffnen(s.Hinweisurl ?? Releaseseite);
    }

    private static void Oeffnen(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Ui.Info($"Die Seite konnte nicht geöffnet werden:\n{url}\n\n{ex.Message}", "Update"); }
    }

    private sealed class WarteCursor : IDisposable
    {
        private readonly Cursor _alt = Cursor.Current ?? Cursors.Default;
        public WarteCursor() => Cursor.Current = Cursors.WaitCursor;
        public void Dispose() => Cursor.Current = _alt;
    }
}
