using System.Text;

namespace UntPlan.App.Fenster;

/// <summary>
/// Berichtsansicht im Programm: zeigt einen HTML-Bericht (Vertretungsberichte, Listen) in einem eigenen Fenster statt im externen
/// Browser. Werkzeugleiste: Drucken, Seitenansicht, Seite einrichten, Im Browser öffnen (wie bisher) und Speichern unter (HTML).
/// </summary>
public sealed class BerichtsansichtFenster : MdiFenster, IDruckbar
{
    private readonly WebBrowser _browser = new()
    {
        Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true, WebBrowserShortcutsEnabled = true, IsWebBrowserContextMenuEnabled = true,
    };

    /// <summary>Titel des Berichts (auch Schlüssel: ein erneuter Aufruf desselben Berichts ersetzt den Inhalt).</summary>
    public string Titel { get; }

    /// <summary>Angezeigtes HTML-Dokument.</summary>
    public string Html { get; private set; } = "";

    private BerichtsansichtFenster(string titel)
    {
        Titel = titel;
        Text = titel;
        HilfeThema = "Berichte";
        Icon = SymbolIcon("drucken");
        Size = new Size(900, 680);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false), false));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true), false));
        ts.Items.Add(Knopf("Seite einrichten", "einstellungen", SeiteEinrichten, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Im Browser öffnen", "web", ImBrowserOeffnen, false));
        ts.Items.Add(Knopf("Speichern unter…", "speichern", SpeichernUnter, false));
        Controls.Add(_browser);
        Controls.Add(ts);
    }

    /// <summary>Bericht anzeigen: vorhandenes Fenster desselben Berichts übernimmt den neuen Inhalt, sonst ein neues Fenster.</summary>
    public static BerichtsansichtFenster Zeigen(string titel, string html)
    {
        var vorhanden = AppZustand.Haupt?.MdiChildren.OfType<BerichtsansichtFenster>().FirstOrDefault(f => f.Titel == titel && !f.IsDisposed);
        var f = vorhanden ?? new BerichtsansichtFenster(titel);
        f.Setzen(html);
        if (vorhanden is null) f.Show();
        else
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
        }
        return f;
    }

    private void Setzen(string html)
    {
        Html = html;
        _browser.DocumentText = html;
    }

    private bool Geladen => _browser.ReadyState == WebBrowserReadyState.Complete && _browser.Document != null;

    /// <summary>Drucken (Druckdialog) bzw. Seitenansicht des angezeigten Berichts.</summary>
    public void Drucken(bool vorschau)
    {
        if (Ui.FehlerProtokoll != null) return; // im Selbsttest keine modalen Druckdialoge
        if (!Geladen) { Ui.Statushinweis("Der Bericht wird noch geladen – bitte einen Moment warten."); return; }
        if (vorschau) _browser.ShowPrintPreviewDialog();
        else _browser.ShowPrintDialog();
    }

    private void SeiteEinrichten()
    {
        if (Ui.FehlerProtokoll != null) return;
        if (Geladen) _browser.ShowPageSetupDialog();
        else SeiteEinrichtenOhneBericht();
    }

    /// <summary>„Seite einrichten“ der Berichte (Ränder, Kopf- und Fußzeile des Drucks), auch ohne geöffneten Bericht – z. B. im Berichts-Auswahldialog.</summary>
    public static void SeiteEinrichtenOhneBericht()
    {
        if (Ui.FehlerProtokoll != null) return;
        var wb = new WebBrowser { ScriptErrorsSuppressed = true };
        wb.CreateControl();
        wb.DocumentCompleted += (_, _) =>
        {
            try { wb.ShowPageSetupDialog(); }
            catch (Exception ex) { Ui.Fehler(ex); }
            finally { wb.BeginInvoke(wb.Dispose); }
        };
        wb.DocumentText = "<!doctype html><html><body></body></html>";
    }

    private string TempDatei()
    {
        var name = new string(Titel.Where(char.IsLetterOrDigit).ToArray());
        return Path.Combine(Path.GetTempPath(), $"UntPlan_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
    }

    /// <summary>Bisheriges Verhalten: Bericht als HTML-Datei im Standardbrowser öffnen.</summary>
    private void ImBrowserOeffnen()
    {
        var pfad = TempDatei();
        try { File.WriteAllText(pfad, Html, new UTF8Encoding(true)); }
        catch (Exception ex) { Ui.Fehler(ex); return; }
        HauptFenster.Starten(pfad);
    }

    private void SpeichernUnter()
    {
        var name = string.Concat(Titel.Split(Path.GetInvalidFileNameChars())).Trim();
        using var sfd = new SaveFileDialog { Filter = "HTML-Datei|*.html;*.htm", FileName = (name.Length > 0 ? name : "Bericht") + ".html", Title = "Bericht speichern unter" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(sfd.FileName, Html, new UTF8Encoding(true));
            Ui.Statushinweis($"Bericht gespeichert: {sfd.FileName}");
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }
}
