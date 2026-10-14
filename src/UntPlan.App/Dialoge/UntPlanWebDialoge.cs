using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Web.Contracts;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Datenaustausch mit UntPlanWeb.
/// <list type="bullet">
/// <item>Export: Stammdaten, Unterricht (mit Stundenplan und Pausenaufsichten) und Vertretungen, jeweils einzeln oder alles.</item>
/// <item>Import: Absenzen bzw. Krankmeldungen und Nachrichten.</item>
/// <item>Ein Anfangsdatum kann für die mit * markierten Daten aktiviert werden.</item>
/// </list>
/// Übertragungsfehler werden oben im Dialog angezeigt.
/// </summary>
public sealed class UntPlanWebDialog : Dialog
{
    private readonly Panel _fehler = new() { Left = 10, Top = 92, Width = 640, Height = 82, BackColor = Color.White, Visible = false };
    private readonly Label _fehlerTitel = new() { Left = 36, Top = 8, Width = 590, Height = 18, Font = Ui.SchriftFett };
    private readonly Label _fehlerText = new() { Left = 36, Top = 28, Width = 590, Height = 32 };
    private readonly Label _status = new() { Left = 140, Top = 14, Width = 500, Height = 64, ForeColor = Color.DimGray };
    private readonly CheckBox _anfangAktiv = new() { Text = "Anfangsdatum für Daten, die mit * markiert sind, aktivieren.", Left = 14, Top = 14, AutoSize = true };
    private readonly DateTimePicker _anfang = new() { Format = DateTimePickerFormat.Short, Left = 14, Top = 38, Width = 120 };
    private readonly List<(Label Datum, Label Zuletzt, string Teil, bool Export)> _zeilen = new();
    private bool _laeuft;
    private readonly TabControl _tabs;
    private readonly ProgressBar _fortschritt = new() { Left = 14, Width = 580, Height = 14, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Label _schritt = new() { Left = 14, Width = 580, Height = 18, ForeColor = Color.DimGray, Visible = false };

    /// <summary>Fortschritt anzeigen (Wert 0–100 und Text des Arbeitsschritts).</summary>
    private void Fortschritt(int wert, string text)
    {
        _fortschritt.Visible = _schritt.Visible = true;
        _fortschritt.Value = Math.Clamp(wert, 0, 100);
        _schritt.Text = text;
        _fortschritt.Refresh();
        _schritt.Refresh();
    }

    private static Schuldatei D => AppZustand.D;
    private static PortalEinstellungen P => AppZustand.D.Einstellungen.Portal;

    public UntPlanWebDialog() : base("UntPlanWeb – Datenaustausch", 660, 544)
    {
        Ok.Text = "Schließen";
        Ok.DialogResult = DialogResult.OK;
        Abbrechen.Visible = false;
        var einstellungen = new Button { Text = "Einstellungen…", Left = 14, Top = 14, Width = 112, Height = 28 };
        einstellungen.Click += (_, _) => { if (new UntPlanWebEinstellungenDialog().ShowDialog(this) == DialogResult.OK) Anzeigen(); };
        var oben = new GroupBox { Left = 10, Top = 4, Width = 640, Height = 84 };
        oben.Controls.Add(einstellungen);
        oben.Controls.Add(_status);
        Controls.Add(oben);

        // Fehlerhinweis (rote Leiste links)
        _fehler.Paint += (_, e) =>
        {
            using var rot = new SolidBrush(Color.FromArgb(200, 40, 40));
            e.Graphics.FillRectangle(rot, 0, 0, 5, _fehler.Height);
            using var rahmen = new Pen(Color.FromArgb(225, 225, 225));
            e.Graphics.DrawRectangle(rahmen, 0, 0, _fehler.Width - 1, _fehler.Height - 1);
            using var kreis = new SolidBrush(Color.FromArgb(210, 50, 40));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(kreis, 12, 8, 18, 18);
            using var weiss = new Pen(Color.White, 2f);
            e.Graphics.DrawLine(weiss, 17, 13, 25, 21);
            e.Graphics.DrawLine(weiss, 25, 13, 17, 21);
        };
        var link = new LinkLabel { Text = "Verbindungseinstellungen prüfen", Left = 36, Top = 60, AutoSize = true };
        link.LinkClicked += (_, _) => { if (new UntPlanWebEinstellungenDialog().ShowDialog(this) == DialogResult.OK) Anzeigen(); };
        _fehler.Controls.AddRange(new Control[] { _fehlerTitel, _fehlerText, link });
        Controls.Add(_fehler);

        _tabs = new TabControl { Left = 10, Top = 94, Width = 640, Height = 400 };
        var tabs = _tabs;
        var seite = new TabPage("UntPlanWeb") { BackColor = Color.White };
        tabs.TabPages.Add(seite);
        seite.Controls.Add(_anfangAktiv);
        seite.Controls.Add(_anfang);
        _anfangAktiv.CheckedChanged += (_, _) => AnfangGeaendert();
        _anfang.ValueChanged += (_, _) => AnfangGeaendert();
        var y = 74;
        void Zeile(string titel, string teil, bool export)
        {
            seite.Controls.Add(new Label { Text = titel, Left = 14, Top = y + 4, Width = 140 });
            var datum = new Label { Left = 160, Top = y + 4, Width = 170, Font = Ui.SchriftFett };
            var zuletzt = new Label { Left = 336, Top = y + 4, Width = 150, ForeColor = Color.DimGray, Font = Ui.SchriftKlein };
            var knopf = new Button { Text = export ? "Exportieren" : "Importieren", Left = export ? 494 : 520, Top = y, Width = 100, Height = 25 };
            knopf.Click += async (_, _) => await Ausfuehren(teil, export);
            seite.Controls.AddRange(new Control[] { datum, zuletzt, knopf });
            _zeilen.Add((datum, zuletzt, teil, export));
            y += 30;
        }
        Zeile("Stammdaten", WebTeile.Stammdaten, true);
        Zeile("Unterricht*", WebTeile.Unterricht, true);
        Zeile("Vertretungen*", WebTeile.Vertretungen, true);
        Zeile("Absenzen*", "absenzen", false);
        Zeile("Buchungen*", WebRueckmeldungsTypen.Raumbuchung, false);
        Zeile("Klausuren*", WebRueckmeldungsTypen.Klausur, false);
        var alles = new Button { Text = "Alles exportieren", Left = 474, Top = y + 8, Width = 120, Height = 25 };
        alles.Click += async (_, _) => await Ausfuehren("", true);
        seite.Controls.Add(alles);
        seite.Controls.Add(new Label
        {
            Text = "Unterricht enthält Stundenplan und Pausenaufsichten; Vertretungen enthalten tagesaktuelle Pläne, Absenzen, Termine und Nachrichten zum Tag.",
            Left = 14, Top = y + 40, Width = 590, Height = 32, ForeColor = Color.DimGray,
        });
        // Fortschrittsanzeige unter den Zeilen
        _schritt.Top = y + 78;
        _fortschritt.Top = y + 98;
        seite.Controls.Add(_schritt);
        seite.Controls.Add(_fortschritt);
        Controls.Add(tabs);
        Anzeigen();
    }

    private DateOnly Start => P.AnfangsdatumAktiv && P.Anfangsdatum is DateOnly a ? a : AppZustand.Vertretungsmodus ? AppZustand.Datum : DateOnly.FromDateTime(DateTime.Today);

    private void AnfangGeaendert()
    {
        if (_laeuft) return;
        var aktiv = _anfangAktiv.Checked;
        var datum = DateOnly.FromDateTime(_anfang.Value);
        if (P.AnfangsdatumAktiv != aktiv || P.Anfangsdatum != datum)
            AppZustand.Doc.Aendern("UntPlanWeb-Anfangsdatum", AenderungsArt.Einstellungen, d => { d.Einstellungen.Portal.AnfangsdatumAktiv = aktiv; d.Einstellungen.Portal.Anfangsdatum = datum; });
        Anzeigen();
    }

    private void Anzeigen()
    {
        _laeuft = true;
        _anfangAktiv.Checked = P.AnfangsdatumAktiv;
        _anfang.Value = (P.Anfangsdatum ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        _anfang.Enabled = P.AnfangsdatumAktiv;
        _laeuft = false;
        _status.Text = string.IsNullOrWhiteSpace(P.Adresse)
            ? "Noch keine Verbindung eingerichtet. Bitte unter „Einstellungen“ Server, Schul-ID und API-Schlüssel eintragen."
            : $"Server: {P.Adresse}\nSchul-ID: {P.SchulId}\nRevision: {P.Revision}" + (P.LetzteSynchronisation is DateTime t ? $" · zuletzt übertragen {t:dd.MM.yyyy HH:mm}" : "");
        foreach (var (datum, zuletzt, teil, export) in _zeilen)
        {
            var ab = teil == WebTeile.Stammdaten ? D.Einstellungen.SchuljahrVon : Start;
            datum.Text = $"{(export ? "Export" : "Import")} ab {ab:d.M.yyyy}";
            zuletzt.Text = P.LetzteUebertragung.TryGetValue(teil, out var z) ? $"zuletzt {z:dd.MM. HH:mm}" : "";
        }
    }

    /// <summary>Fehlerhinweis anzeigen (auch für den Selbsttest).</summary>
    internal void FehlerAnzeigen(string titel, string text) => FehlerZeigen(titel, text);

    private void FehlerZeigen(string? titel, string text = "")
    {
        var sichtbar = titel != null;
        if (_fehler.Visible != sichtbar)
        {
            _fehler.Visible = sichtbar;
            _tabs.Top = sichtbar ? 180 : 94;
            ClientSize = new Size(ClientSize.Width, sichtbar ? 630 : 544);
        }
        _fehlerTitel.Text = titel ?? "";
        _fehlerText.Text = text;
        _fehler.Invalidate();
    }

    private async Task Ausfuehren(string teil, bool export)
    {
        if (string.IsNullOrWhiteSpace(P.Adresse))
        {
            FehlerZeigen("Keine Verbindung eingerichtet", "Bitte zuerst unter „Einstellungen“ Server, Schul-ID und API-Schlüssel eintragen.");
            return;
        }
        UseWaitCursor = true;
        Enabled = false;
        try
        {
            if (export) await Exportieren(teil);
            else await Importieren(teil);
            FehlerZeigen(null);
        }
        catch (Exception ex)
        {
            _fortschritt.Visible = false;
            _schritt.Text = "Abgebrochen.";
            var text = ex is HttpRequestException or TaskCanceledException
                ? "Es konnte keine Verbindung zum Server hergestellt werden. Überprüfen Sie Ihre Internetverbindung, die Verbindungseinstellungen und gegebenenfalls den Status des UntPlanWeb-Servers."
                : ex.Message;
            FehlerZeigen("UntPlanWeb-Übertragungsfehler!", text);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
            Anzeigen();
        }
    }

    /// <summary>
    /// Export direkt (ohne Dialog), z. B. aus der Schnellzugriffsleiste. Ohne Verbindungseinstellungen öffnen sich zuerst
    /// die Einstellungen. Liefert eine Erfolgsmeldung bzw. wirft bei Fehlern.
    /// </summary>
    public static async Task<string> DirektExportieren(IWin32Window owner, string teil)
    {
        if (string.IsNullOrWhiteSpace(P.Adresse) && new UntPlanWebEinstellungenDialog().ShowDialog(owner) != DialogResult.OK) return "";
        if (string.IsNullOrWhiteSpace(P.Adresse)) return "";
        using var d = new UntPlanWebDialog();
        await d.Exportieren(teil);
        return $"An UntPlanWeb übertragen ({(teil.Length == 0 ? "alles" : teil)}, Revision {P.Revision}) um {DateTime.Now:HH:mm}";
    }

    private async Task Exportieren(string teil)
    {
        // Unterricht und Vertretungen beziehen sich auf die Stammdaten: diese immer mitsenden
        var teile = teil.Length == 0 ? Array.Empty<string>() : new[] { WebTeile.Stammdaten, teil }.Distinct().ToArray();
        var was = teil.Length == 0 ? "alle Daten" : teil;
        Fortschritt(10, $"Daten werden zusammengestellt ({was}) …");
        var d0 = D;
        var start = Start;
        var optionen = WebExportOptionen.Aus(P, teile);
        var paket = await Task.Run(() => UntPlanWebExport.PaketErstellen(d0, start, optionen));
        Fortschritt(45, $"Übertragung an {P.Adresse} … ({paket.Tage.Count} Tage, {paket.Vertretungen.Count} Vertretungen)");
        var a = await UntPlanWebExport.HochladenAsync(P, paket);
        Fortschritt(100, $"Fertig: an UntPlanWeb übertragen (Revision {a.Revision}) um {DateTime.Now:HH:mm}.");
        var jetzt = DateTime.Now;
        AppZustand.Doc.Aendern("UntPlanWeb-Übertragung", AenderungsArt.Einstellungen, d =>
        {
            var p = d.Einstellungen.Portal;
            p.Revision = a.Revision;
            p.LetzteSynchronisation = jetzt;
            foreach (var t in teile.Length == 0 ? WebTeile.Felder.Keys.ToArray() : teile) p.LetzteUebertragung[t] = jetzt;
        });
    }

    private async Task Importieren(string teil)
    {
        var start = Start;
        var typen = teil == "absenzen" ? new[] { WebRueckmeldungsTypen.Absenz, WebRueckmeldungsTypen.Nachricht } : new[] { teil };
        Fortschritt(20, "Rückmeldungen werden abgeholt …");
        var liste = (await UntPlanWebExport.RueckmeldungenAsync(P))
            .Where(m => typen.Contains(m.Typ) && (!DateOnly.TryParse(m.Bis ?? m.Von, out var bis) || bis >= start)).ToList();
        var was = teil switch { WebRueckmeldungsTypen.Raumbuchung => "Buchungen", WebRueckmeldungsTypen.Klausur => "Klausuren", _ => "Absenzen oder Nachrichten" };
        if (liste.Count == 0) { Fortschritt(100, $"Keine neuen {was}."); return; }
        Fortschritt(60, $"{liste.Count} {was} werden übernommen …");
        List<string> ok = new(), nein = new();
        var jetzt = DateTime.Now;
        AppZustand.Doc.Aendern("UntPlanWeb-Rückmeldungen", AenderungsArt.Vertretung, d =>
        {
            (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
            var tage = d.Abwesenheiten.Where(a => a.Text.Contains("Portal-Meldung")).SelectMany(a => Kalender.Tage(a.Von, a.Bis)).Distinct().ToList();
            foreach (var t in tage.Where(t => Kalender.IstUnterrichtstag(d, t))) Core.Vertretungsplanung.Vertretungen.Aktualisieren(d, t);
            d.Einstellungen.Portal.LetzteUebertragung[teil] = jetzt;
        });
        await UntPlanWebExport.QuittierenAsync(P, ok);
        Fortschritt(100, $"Fertig: {ok.Count} {was} übernommen.");
        Ui.Info($"{ok.Count} {was} übernommen{(nein.Count > 0 ? $", {nein.Count} abgelehnt (unbekannte Elemente, kein Unterrichtstag oder Raum belegt)" : "")}.", "UntPlanWeb");
    }
}

/// <summary>Verbindung zu UntPlanWeb (Server, Port, HTTPS, Schul-ID, API-Schlüssel, Verbindung prüfen) und Exportoptionen.</summary>
public sealed class UntPlanWebEinstellungenDialog : Dialog
{
    public UntPlanWebEinstellungenDialog() : base("UntPlanWeb – Einstellungen", 700, 330)
    {
        var p = AppZustand.D.Einstellungen.Portal;
        Uri.TryCreate(p.Adresse, UriKind.Absolute, out var uri);
        var links = new GroupBox { Left = 10, Top = 6, Width = 330, Height = 260 };
        var rechts = new GroupBox { Left = 350, Top = 6, Width = 340, Height = 260 };
        Controls.Add(links);
        Controls.Add(rechts);
        var y = 20;
        T Feld<T>(string text, T c, int breite = 170) where T : Control
        {
            links.Controls.Add(new Label { Text = text, Left = 12, Top = y + 4, Width = 110 });
            c.SetBounds(126, y, breite, 23);
            links.Controls.Add(c);
            y += 32;
            return c;
        }
        var schule = Feld("Schul-ID", new TextBox { Text = p.SchulId });
        var server = Feld("Server", new TextBox { Text = uri?.Host ?? "", PlaceholderText = "z. B. web.meine-schule.de" });
        var port = Feld("Port", new NumericUpDown { Minimum = 1, Maximum = 65535, Value = uri?.Port is int pt and > 0 ? pt : 443 }, 70);
        var https = new CheckBox { Text = "https", Left = 206, Top = y - 30, AutoSize = true, Checked = uri is null || uri.Scheme == Uri.UriSchemeHttps };
        links.Controls.Add(https);
        https.CheckedChanged += (_, _) => { if (https.Checked && port.Value == 80) port.Value = 443; else if (!https.Checked && port.Value == 443) port.Value = 80; };
        var pfad = uri?.AbsolutePath.Trim('/') ?? "";
        var schluessel = Feld("API-Schlüssel", new TextBox { Text = p.ApiSchluessel, UseSystemPasswordChar = true });
        var tage = Feld("Tage voraus", new NumericUpDown { Minimum = 1, Maximum = 60, Value = Math.Clamp(p.TageVoraus, 1, 60) }, 70);
        var pruefen = new Button { Text = "Verbindung prüfen", Left = 126, Top = y + 4, Width = 170, Height = 28 };
        var ergebnis = new Label { Left = 12, Top = y + 38, Width = 306, Height = 34, Font = Ui.SchriftKlein };
        links.Controls.Add(pruefen);
        links.Controls.Add(ergebnis);

        var oy = 20;
        CheckBox Option(string text, bool wert)
        {
            var c = new CheckBox { Text = text, Left = 14, Top = oy, Width = 316, Checked = wert };
            rechts.Controls.Add(c);
            oy += 28;
            return c;
        }
        var ignoriert = Option("Ignorierten Unterricht übertragen", p.IgnoriertenUnterrichtUebertragen);
        var ferien = Option("Ferien nicht als Stammdaten exportieren", p.FerienNichtExportieren);
        var email = Option("E-Mail-Adressen der Lehrkräfte übertragen", p.LehrerEmail);
        var absenzen = Option("Lehrer-Absenzen übertragen (ohne Grund)", p.LehrerAbsenzen);
        var gruende = Option("Absenzgründe übertragen (außer vertrauliche)", p.Absenzgruende);
        var schueler = Option("Schülerliste übertragen (Klassenbuch, Fehlzeiten)", p.SchuelerExportieren);
        rechts.Controls.Add(new Label
        {
            Text = "Personenbezogene Daten werden nur übertragen, wenn sie hier ausdrücklich freigegeben sind.",
            Left = 14, Top = oy + 8, Width = 316, Height = 36, ForeColor = Color.DimGray,
        });

        string Adresse() => string.IsNullOrWhiteSpace(server.Text) ? "" :
            $"{(https.Checked ? "https" : "http")}://{server.Text.Trim().Replace("https://", "").Replace("http://", "").TrimEnd('/')}" +
            ((https.Checked && port.Value == 443) || (!https.Checked && port.Value == 80) ? "" : $":{port.Value}") + (pfad.Length > 0 ? "/" + pfad : "");

        pruefen.Click += async (_, _) =>
        {
            ergebnis.ForeColor = Color.DimGray;
            ergebnis.Text = "Verbindung wird geprüft …";
            pruefen.Enabled = false;
            try
            {
                var test = new PortalEinstellungen { Adresse = Adresse(), SchulId = schule.Text.Trim(), ApiSchluessel = schluessel.Text.Trim() };
                ergebnis.Text = await UntPlanWebExport.VerbindungPruefenAsync(test);
                ergebnis.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                ergebnis.Text = ex is HttpRequestException or TaskCanceledException ? "Keine Verbindung zum Server." : ex.Message;
                ergebnis.ForeColor = Color.Firebrick;
            }
            finally { pruefen.Enabled = true; }
        };

        Ok.Click += (_, _) => AppZustand.Doc.Aendern("UntPlanWeb-Einstellungen", AenderungsArt.Einstellungen, d =>
        {
            var e = d.Einstellungen.Portal;
            e.Adresse = Adresse();
            e.SchulId = schule.Text.Trim();
            e.ApiSchluessel = schluessel.Text.Trim();
            e.TageVoraus = (int)tage.Value;
            e.IgnoriertenUnterrichtUebertragen = ignoriert.Checked;
            e.FerienNichtExportieren = ferien.Checked;
            e.LehrerEmail = email.Checked;
            e.LehrerAbsenzen = absenzen.Checked;
            e.Absenzgruende = gruende.Checked;
            e.SchuelerExportieren = schueler.Checked;
        });
    }
}
