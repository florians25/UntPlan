using System.Text;

namespace UntPlan.App.Fenster;

/// <summary>
/// Berichtsansicht im Programm: zeigt einen HTML-Bericht (Vertretungsberichte, Listen) in einem eigenen Fenster statt im externen
/// Browser. Wie die Seitenansicht des Vorbilds: weißes Blatt auf grauem Grund; Werkzeugleiste mit Verkleinern/Vergrößern, Drucken,
/// Excel- und PDF-Ausgabe sowie Seitenansicht, Seite einrichten, Im Browser öffnen und Speichern unter (HTML).
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
        ts.Items.Add(Knopf("Verkleinern", "minus", () => Zoomen(-10), false));
        ts.Items.Add(Knopf("Vergrößern", "plus", () => Zoomen(10), false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false), false));
        ts.Items.Add(Knopf("Als Excel-Datei speichern", "export", ExcelSpeichern, false));
        ts.Items.Add(Knopf("PDF (Drucker „Microsoft Print to PDF“)", "dk", PdfAusgeben, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true), false));
        ts.Items.Add(Knopf("Seite einrichten", "einstellungen", SeiteEinrichten, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Im Browser öffnen", "web", ImBrowserOeffnen, false));
        ts.Items.Add(Knopf("Speichern unter…", "speichern", SpeichernUnter, false));
        _browser.DocumentCompleted += (_, _) => ZoomAnwenden();
        Controls.Add(_browser);
        Controls.Add(ts);
    }

    private int _zoom = 100;

    /// <summary>Ansicht verkleinern bzw. vergrößern (50–200 %), wirkt nur auf die Anzeige, nicht auf den Druck.</summary>
    private void Zoomen(int schritt)
    {
        _zoom = Math.Clamp(_zoom + schritt, 50, 200);
        ZoomAnwenden();
        Ui.Statushinweis($"Ansicht {_zoom} %");
    }

    private void ZoomAnwenden()
    {
        try { if (Geladen && _browser.Document!.Body is { } b) b.Style = $"zoom:{_zoom}%"; }
        catch (Exception) { /* Anzeige ohne Zoom */ }
    }

    /// <summary>Bericht als Excel-Datei speichern (HTML-Tabelle, die Excel direkt öffnet).</summary>
    private void ExcelSpeichern()
    {
        var name = string.Concat(Titel.Split(Path.GetInvalidFileNameChars())).Trim();
        using var sfd = new SaveFileDialog { Filter = "Excel-Datei|*.xls", FileName = (name.Length > 0 ? name : "Bericht") + ".xls", Title = "Bericht als Excel-Datei speichern" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(sfd.FileName, Html, new UTF8Encoding(true));
            Ui.Statushinweis($"Excel-Datei gespeichert: {sfd.FileName}");
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    /// <summary>PDF-Ausgabe über den Druckdialog (Drucker „Microsoft Print to PDF“).</summary>
    private void PdfAusgeben()
    {
        if (Ui.FehlerProtokoll != null) return;
        Ui.Info("Wählen Sie im folgenden Druckdialog den Drucker \"Microsoft Print to PDF\".");
        Drucken(false);
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
