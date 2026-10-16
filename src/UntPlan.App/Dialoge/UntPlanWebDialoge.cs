using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Web.Contracts;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Fenster „UntPlanWeb“ (wie „Webportal“ in Vorbild):
/// <list type="bullet">
/// <item>Oben links „Einstellungen“ und der Exportstatus, oben rechts Schulname, Server:Port und Schul-ID.</item>
/// <item>Reiter „UntPlanWeb“ mit der Gruppe „UntPlanWeb Import/Export“: Anfangsdatum für die mit * markierten Daten,
/// Export von Stammdaten, Unterricht* und Vertretungen*, Import von Stpl.-Änderungen* (Buchungen, Prüfungen …),
/// Absenzen* (Krankmeldungen, Nachrichten), Studenten* (Schüler-Fehlzeiten aus dem Klassenbuch) und Kurse
/// (Klassenbuch je Unterricht: Lehrstoff, Hausaufgaben).</item>
/// <item>Fortschritt, Ergebnis und Übertragungsfehler erscheinen in der Statuszeile unter den Zeilen.</item>
/// </list>
/// </summary>
public sealed class UntPlanWebDialog : Dialog
{
    // Vorbild-Optik des Fensters „Webportal“: hellgrüner Hintergrund, hervorgehobene Knöpfe grün
    // Farben passen sich dem Modus an: Vertretungsmodus mintgrün wie die übrigen Fenster, Stundenplanmodus Petrol
    internal static Color Hintergrund => Ui.Vertretungsmodus ? Color.FromArgb(229, 239, 235) : Color.FromArgb(232, 242, 246);
    internal static Color KnopfGruen => Ui.Vertretungsmodus ? Color.FromArgb(199, 222, 213) : Color.FromArgb(201, 228, 236);
    internal static Color KnopfAktiv => Ui.Vertretungsmodus ? Color.FromArgb(168, 205, 190) : Color.FromArgb(166, 208, 222);
    internal static Color RandGruen => Ui.Vertretungsmodus ? Color.FromArgb(81, 132, 111) : Color.FromArgb(18, 94, 122);
    internal static readonly Color RandGrau = Color.FromArgb(173, 173, 173);

    /// <summary>Teil-Schlüssel der Importzeilen (Export: WebTeile).</summary>
    internal const string Stpl = "stpl-aenderungen";
    internal const string Absenzen = "absenzen";
    internal const string Studenten = "studenten";
    internal const string Kurse = "kurse";

    private readonly GroupBox _links = new() { Left = 12, Top = 4, Width = 336, Height = 126 };
    private readonly GroupBox _rechts = new() { Left = 356, Top = 4, Width = 332, Height = 126 };
    private readonly Label _exportStatus = new() { Left = 6, Top = 92, Width = 324, Height = 20, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _server = new() { Left = 16, Top = 34, Width = 300, Height = 20 };
    private readonly Label _benutzer = new() { Left = 16, Top = 60, Width = 300, Height = 20 };
    private readonly CheckBox _anfangAktiv = new() { Text = "Anfangsdatum für Daten, die mit * markiert sind, aktivieren.", Left = 14, Top = 22, AutoSize = true };
    private readonly ComboBox _datumWahl = new() { Left = 16, Top = 46, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _anfang = new() { Format = DateTimePickerFormat.Short, Left = 194, Top = 46, Width = 110 };
    private readonly List<Zeile> _zeilen = new();
    private readonly List<(string Text, DateOnly Datum)> _datumVorgaben = new();
    private readonly ToolTip _tipp = new() { AutoPopDelay = 15000 };
    private readonly Panel _meldung = new() { Left = 12, Top = 308, Width = 640, Height = 64, Visible = false };
    private readonly ProgressBar _fortschritt = new() { Left = 2, Top = 2, Width = 636, Height = 12, Style = ProgressBarStyle.Continuous };
    private readonly Label _meldungTitel = new() { Left = 24, Top = 0, Width = 612, Height = 18, Font = Ui.SchriftFett, ForeColor = Color.FromArgb(180, 30, 30) };
    private readonly Label _meldungText = new() { Left = 2, Top = 19, Width = 636, Height = 28, ForeColor = Color.DimGray };
    private readonly LinkLabel _meldungLink = new() { Text = "Verbindungseinstellungen prüfen", Left = 0, Top = 47, AutoSize = true, Visible = false };
    private bool _fehler;
    private bool _laeuft;
    private bool _fuellen;
    private static string? _schulnameServer;

    private sealed record Zeile(string Teil, bool Export, Label Titel, Label Datum, KlassikKnopf Knopf);

    private static Schuldatei D => AppZustand.D;
    private static PortalEinstellungen P => AppZustand.D.Einstellungen.Portal;

    public UntPlanWebDialog() : base("UntPlanWeb", 700, 566)
    {
        BackColor = Hintergrund;
        KeyPreview = true;
        Ok.Visible = false;
        Abbrechen.Visible = false;
        AcceptButton = null;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && !_laeuft) { e.Handled = true; Close(); } };

        // ---------------------------------------------------------------- oben links: Einstellungen und Exportstatus
        var einstellungen = new KlassikKnopf("Einstellungen", dauerhaftGruen: true) { Left = 22, Top = 22, Width = 130, Height = 26 };
        einstellungen.Click += (_, _) => EinstellungenOeffnen();
        _tipp.SetToolTip(einstellungen, "Verbindung zu UntPlanWeb (Server, Port, Schul-ID, API-Schlüssel) und Exportoptionen");
        _links.Controls.Add(einstellungen);
        _links.Controls.Add(_exportStatus);
        Controls.Add(_links);

        // ---------------------------------------------------------------- oben rechts: Schule, Server:Port, Schul-ID
        _rechts.Controls.Add(_server);
        _rechts.Controls.Add(_benutzer);
        Controls.Add(_rechts);

        // ---------------------------------------------------------------- Reiter „UntPlanWeb“
        var tabs = new TabControl { Left = 4, Top = 138, Width = 692, Height = 424 };
        var seite = new TabPage("UntPlanWeb") { BackColor = Hintergrund, UseVisualStyleBackColor = false };
        tabs.TabPages.Add(seite);
        Controls.Add(tabs);
        var gruppe = new GroupBox { Text = "UntPlanWeb Import/Export", Left = 8, Top = 6, Width = 664, Height = 382 };
        seite.Controls.Add(gruppe);

        gruppe.Controls.Add(_anfangAktiv);
        gruppe.Controls.Add(_datumWahl);
        gruppe.Controls.Add(_anfang);
        _tipp.SetToolTip(_datumWahl, "Anfangsdatum aus Vorgaben wählen (Tagesdatum, nächster Montag, Schuljahresbeginn, Periodenbeginn)");
        _anfangAktiv.CheckedChanged += (_, _) => AnfangGeaendert();
        _anfang.ValueChanged += (_, _) => AnfangGeaendert();
        _datumWahl.SelectedIndexChanged += (_, _) => DatumGewaehlt();

        var y = 82;
        Zeile Neu(string titel, string teil, bool export, string tipp)
        {
            var t = new Label { Text = titel, Left = 14, Top = y + 5, Width = 132, Height = 18 };
            var d = new Label { Left = 148, Top = y + 5, Width = 260, Height = 18, Font = Ui.SchriftFett };
            var k = new KlassikKnopf(export ? "Exportieren" : "Importieren") { Left = export ? 434 : 542, Top = y, Width = 104, Height = 26 };
            k.Click += async (_, _) => await Ausfuehren(teil, export);
            _tipp.SetToolTip(t, tipp);
            gruppe.Controls.AddRange(new Control[] { t, d, k });
            var z = new Zeile(teil, export, t, d, k);
            _zeilen.Add(z);
            y += 32;
            return z;
        }
        Neu("Stammdaten", WebTeile.Stammdaten, true, "Schule, Zeitraster, Klassen, Lehrkräfte, Räume, Fächer, Gänge, Ferien (und Schüler, falls freigegeben)");
        Neu("Stundenplan*", WebTeile.Unterricht, true, "Perioden mit Unterricht und Stundenplan sowie Pausenaufsichten – Stundenplanänderungen gehen nur hiermit an UntPlanWeb");
        var vertretungen = Neu("Vertretungen*", WebTeile.Vertretungen, true, "Vertretungen, Absenzen, Termine und Tagestexte – nur die geänderten Stunden, ohne Stundenplanänderungen");
        var stpl = Neu("Stpl.-Änderungen*", Stpl, false, "Raumänderungen, Buchungen (zusätzliche Unterrichte) und Prüfungen aus UntPlanWeb");
        stpl.Datum.Font = Ui.Schrift;
        stpl.Datum.Width = 230;
        var info = new InfoSymbol { Left = 410, Top = stpl.Titel.Top - 1 };
        _tipp.SetToolTip(info, "Raumänderungen, zusätzliche Unterrichte (Buchungen) und Prüfungen, die Lehrkräfte in UntPlanWeb\nerfasst haben, im Fenster „Stundenplanänderungen aus UntPlanWeb“ prüfen und übernehmen.\nBereits übernommene oder ignorierte Änderungen werden nicht erneut importiert.");
        gruppe.Controls.Add(info);
        Neu("Absenzen*", Absenzen, false, "Krankmeldungen bzw. Abwesenheitsmeldungen der Lehrkräfte und Nachrichten aus UntPlanWeb");
        var studenten = Neu("Studenten*", Studenten, false, "Im Klassenbuch von UntPlanWeb erfasste Fehlzeiten der Schülerinnen und Schüler\nals Schülerabwesenheiten übernehmen (Doppelklick: Schülerabwesenheiten anzeigen)");
        var kurse = Neu("Kurse", Kurse, false, "Klassenbuch je Unterricht (Lehrstoff, Hausaufgaben) aus UntPlanWeb übernehmen\n(Doppelklick: Klassenbuch anzeigen)");
        Anzeigefenster(studenten.Titel, "Schülerabwesenheiten anzeigen…", () => { using var f = new SchuelerAbwesenheitenDialog(); f.ShowDialog(this); });
        Anzeigefenster(kurse.Titel, "Klassenbuch (UntPlanWeb) anzeigen…", () => { using var f = new KlassenbuchDialog(); f.ShowDialog(this); });

        // ---------------------------------------------------------------- Statuszeile (Fortschritt, Ergebnis, Fehler)
        _meldung.Paint += (_, e) =>
        {
            if (!_fehler) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var kreis = new SolidBrush(Color.FromArgb(210, 50, 40));
            e.Graphics.FillEllipse(kreis, 3, 1, 16, 16);
            using var weiss = new Pen(Color.White, 2f);
            e.Graphics.DrawLine(weiss, 7, 5, 15, 13);
            e.Graphics.DrawLine(weiss, 15, 5, 7, 13);
        };
        _meldungLink.LinkClicked += (_, _) => EinstellungenOeffnen();
        _meldung.Controls.AddRange(new Control[] { _fortschritt, _meldungTitel, _meldungText, _meldungLink });
        gruppe.Controls.Add(_meldung);

        Anzeigen();
        // wie im Vorbild ist der aktive Knopf hervorgehoben – zu Beginn „Vertretungen exportieren“ (häufigste Aktion)
        ActiveControl = vertretungen.Knopf;
    }

    /// <summary>Kontextmenü und Doppelklick auf der Zeilenbeschriftung öffnen das Anzeigefenster der importierten Daten.</summary>
    private static void Anzeigefenster(Label titel, string text, Action oeffnen)
    {
        var menue = new ContextMenuStrip();
        menue.Items.Add(text, null, (_, _) => oeffnen());
        titel.ContextMenuStrip = menue;
        titel.DoubleClick += (_, _) => oeffnen();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Schulname laut UntPlanWeb (wie im Vorbild den Schulnamen aus Webportal zeigt); ohne Server bleibt der Name der Schuldatei
        if (Ui.FehlerProtokoll != null || string.IsNullOrWhiteSpace(P.Adresse)) return;
        var name = await UntPlanWebExport.SchulnameAsync(P);
        if (name is null || IsDisposed) return;
        _schulnameServer = name;
        Anzeigen();
    }

    private void EinstellungenOeffnen()
    {
        if (_laeuft) return;
        using var d = new UntPlanWebEinstellungenDialog();
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _schulnameServer = null;
        FehlerZeigen(null);
        Anzeigen();
    }

    // ------------------------------------------------------------------------------------------ Datumsangaben

    /// <summary>Der Export läuft unabhängig vom gewählten Vertretungsdatum standardmäßig immer ab dem aktuellen Tag.</summary>
    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Anfangsdatum der mit * markierten Daten: Tagesdatum bzw. das gewählte Anfangsdatum, wenn aktiviert.</summary>
    private static DateOnly Start => P.AnfangsdatumAktiv && P.Anfangsdatum is DateOnly a ? a : Heute;

    /// <summary>
    /// „ab“-Datum einer Zeile: Stammdaten und Kurse ab Schuljahresbeginn, Studenten* ab Schuljahresbeginn bzw. dem
    /// Anfangsdatum (wenn aktiviert), die übrigen mit * markierten ab Tagesdatum bzw. Anfangsdatum.
    /// </summary>
    internal static DateOnly AbDatum(string teil) => teil switch
    {
        WebTeile.Stammdaten or Kurse => D.Einstellungen.SchuljahrVon,
        Studenten => P.AnfangsdatumAktiv && P.Anfangsdatum is DateOnly a ? a : D.Einstellungen.SchuljahrVon,
        _ => Start,
    };

    private void DatumVorgabenFuellen()
    {
        _datumVorgaben.Clear();
        var heute = DateOnly.FromDateTime(DateTime.Today);
        _datumVorgaben.Add(("Tagesdatum", heute));
        if (AppZustand.Vertretungsmodus && AppZustand.Datum != heute) _datumVorgaben.Add(("Vertretungsdatum", AppZustand.Datum));
        var bisMontag = ((int)DayOfWeek.Monday - (int)heute.DayOfWeek + 7) % 7;
        _datumVorgaben.Add(("Nächster Montag", heute.AddDays(bisMontag == 0 ? 7 : bisMontag)));
        _datumVorgaben.Add(("Schuljahresbeginn", D.Einstellungen.SchuljahrVon));
        foreach (var p in D.Perioden.OrderBy(p => p.Von))
            _datumVorgaben.Add(($"Periode {p.Name}", p.Von));
        _datumWahl.Items.Clear();
        _datumWahl.Items.Add("Datum wählen...");
        foreach (var (text, datum) in _datumVorgaben) _datumWahl.Items.Add($"{text} ({datum:d.M.yyyy})");
    }

    private void DatumGewaehlt()
    {
        if (_fuellen || _datumWahl.SelectedIndex <= 0) return;
        var datum = _datumVorgaben[_datumWahl.SelectedIndex - 1].Datum;
        _fuellen = true;
        try { _anfang.Value = datum.ToDateTime(TimeOnly.MinValue); }
        finally { _fuellen = false; }
        AnfangGeaendert();
    }

    private void AnfangGeaendert()
    {
        if (_fuellen) return;
        var aktiv = _anfangAktiv.Checked;
        var datum = DateOnly.FromDateTime(_anfang.Value);
        if (P.AnfangsdatumAktiv != aktiv || P.Anfangsdatum != datum)
            AppZustand.Doc.Aendern("UntPlanWeb-Anfangsdatum", AenderungsArt.Einstellungen, d => { d.Einstellungen.Portal.AnfangsdatumAktiv = aktiv; d.Einstellungen.Portal.Anfangsdatum = datum; });
        Anzeigen();
    }

    // ------------------------------------------------------------------------------------------ Anzeige

    private void Anzeigen()
    {
        _fuellen = true;
        try
        {
            var aktiv = P.AnfangsdatumAktiv;
            _anfangAktiv.Checked = aktiv;
            _anfang.Value = (P.Anfangsdatum ?? Heute).ToDateTime(TimeOnly.MinValue);
            _anfang.Enabled = _datumWahl.Enabled = aktiv && !_laeuft;
            if (_datumWahl.Items.Count == 0) DatumVorgabenFuellen();
            _datumWahl.SelectedIndex = 0;
        }
        finally { _fuellen = false; }

        var eingerichtet = !string.IsNullOrWhiteSpace(P.Adresse);
        _exportStatus.Text = eingerichtet ? "Unterrichtsexport aktiv." : "Keine Verbindung eingerichtet.";
        _tipp.SetToolTip(_exportStatus, eingerichtet
            ? $"Revision {P.Revision}" + (P.LetzteSynchronisation is DateTime t ? $", zuletzt übertragen am {t:dd.MM.yyyy} um {t:HH:mm}" : ", noch nicht übertragen")
            : "Bitte unter „Einstellungen“ Server, Port, Schul-ID und API-Schlüssel eintragen.");
        _rechts.Text = _schulnameServer ?? (D.Einstellungen.Schulname.Length > 0 ? D.Einstellungen.Schulname : "UntPlanWeb");
        if (eingerichtet && Uri.TryCreate(P.Adresse, UriKind.Absolute, out var uri))
        {
            _server.Text = $"{uri.Host}:{uri.Port}";
            _tipp.SetToolTip(_server, P.Adresse);
        }
        else _server.Text = eingerichtet ? P.Adresse : "";
        _benutzer.Text = eingerichtet ? P.SchulId : "";

        foreach (var z in _zeilen)
        {
            z.Datum.Text = z.Teil == Stpl ? "Import aller Buchungen, Prüfungen, ..." : $"{(z.Export ? "Export" : "Import")} ab {AbDatum(z.Teil):d.M.yyyy}";
            var art = z.Export ? "exportiert" : "importiert";
            var zuletzt = P.LetzteUebertragung.TryGetValue(z.Teil, out var zl) ? $"Zuletzt {art}: {zl:dd.MM.yyyy HH:mm}" : $"Noch nicht {art}";
            _tipp.SetToolTip(z.Knopf, zuletzt);
            _tipp.SetToolTip(z.Datum, zuletzt);
        }
    }

    // ------------------------------------------------------------------------------------------ Statuszeile

    /// <summary>Kleines Fortschrittsfenster wie im Vorbild („Unterrichtsbestandteile werden konvertiert (153 von 238)“).</summary>
    private UploadFortschritt? _popup;

    /// <summary>Fortschritt anzeigen (Wert 0–100 und Text des Arbeitsschritts): während der Arbeit im eigenen kleinen Fenster, am Ende in der Statuszeile.</summary>
    private void Fortschritt(int wert, string text)
    {
        if (wert < 100)
        {
            if (_popup is null || _popup.IsDisposed)
            {
                _popup = new UploadFortschritt();
                var o = Visible ? (Form)this : Form.ActiveForm;
                _popup.Zeigen(o);
            }
            _popup.Setzen(wert, text);
            return;
        }
        PopupSchliessen();
        _fehler = false;
        _meldung.Visible = true;
        _fortschritt.Visible = true;
        _fortschritt.Value = Math.Clamp(wert, 0, 100);
        _meldungTitel.Visible = _meldungLink.Visible = false;
        _meldungText.ForeColor = Color.DimGray;
        _meldungText.Text = text;
        _meldung.Invalidate();
        _meldung.Refresh();
    }

    private void PopupSchliessen()
    {
        _popup?.Close();
        _popup?.Dispose();
        _popup = null;
    }

    /// <summary>Fehlerhinweis anzeigen (auch für den Selbsttest).</summary>
    internal void FehlerAnzeigen(string titel, string text) => FehlerZeigen(titel, text);

    private void FehlerZeigen(string? titel, string text = "")
    {
        if (titel is null)
        {
            if (_fehler) _meldung.Visible = false;
            _fehler = false;
            return;
        }
        _fehler = true;
        _meldung.Visible = true;
        _fortschritt.Visible = false;
        _meldungTitel.Visible = _meldungLink.Visible = true;
        _meldungTitel.Text = titel;
        _meldungText.ForeColor = Color.Black;
        _meldungText.Text = text;
        _tipp.SetToolTip(_meldungText, text);
        _meldung.Invalidate();
    }

    // ------------------------------------------------------------------------------------------ Export / Import

    private void Sperren(bool gesperrt)
    {
        _laeuft = gesperrt;
        foreach (var z in _zeilen) z.Knopf.Enabled = !gesperrt;
        _links.Enabled = _anfangAktiv.Enabled = !gesperrt;
        _anfang.Enabled = _datumWahl.Enabled = !gesperrt && P.AnfangsdatumAktiv;
        UseWaitCursor = gesperrt;
    }

    private async Task Ausfuehren(string teil, bool export)
    {
        if (_laeuft) return;
        if (teil == Stpl)
        {
            using (var f = new StundenplanAenderungenDialog()) f.ShowDialog(this);
            Anzeigen();
            return;
        }
        if (string.IsNullOrWhiteSpace(P.Adresse))
        {
            FehlerZeigen("Keine Verbindung eingerichtet", "Bitte zuerst unter „Einstellungen“ Server, Port, Schul-ID und API-Schlüssel eintragen.");
            return;
        }
        Sperren(true);
        Action? danach = null;
        try
        {
            FehlerZeigen(null);
            if (export)
            {
                await Exportieren(teil);
                danach = Erfolgsmeldung;
            }
            else danach = teil switch
            {
                Studenten => await StudentenImportieren(),
                Kurse => await KurseImportieren(),
                _ => await AbsenzenImportieren(),
            };
        }
        catch (Exception ex)
        {
            var text = ex is HttpRequestException or TaskCanceledException
                ? "Es konnte keine Verbindung zum Server hergestellt werden. Überprüfen Sie Ihre Internetverbindung, die Verbindungseinstellungen und gegebenenfalls den Status des UntPlanWeb-Servers."
                : ex.Message;
            FehlerZeigen("UntPlanWeb-Übertragungsfehler!", text);
        }
        finally
        {
            PopupSchliessen();
            Sperren(false);
            Anzeigen();
        }
        danach?.Invoke();
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
        try { await d.Exportieren(teil); }
        finally { d.PopupSchliessen(); }
        Erfolgsmeldung();
        return $"An UntPlanWeb übertragen ({(teil.Length == 0 ? "alles" : teil)}, Revision {P.Revision}) um {DateTime.Now:HH:mm}";
    }

    /// <summary>Meldung nach erfolgreicher Übertragung (wie im Vorbild nach dem Export nach Webportal).</summary>
    private static void Erfolgsmeldung() => Ui.Info("Die Datenübertragung nach UntPlanWeb war erfolgreich!", "UntPlan - Frage");

    private static string TeilName(string teil) => teil switch
    {
        WebTeile.Stammdaten => "Stammdaten", WebTeile.Unterricht => "Unterricht/Stundenplan", WebTeile.Vertretungen => "Vertretungen", "" => "Alle Daten", _ => teil,
    };

    private async Task Exportieren(string teil)
    {
        // Unterricht und Vertretungen beziehen sich auf die Stammdaten: diese immer mitsenden
        var teile = teil.Length == 0 ? Array.Empty<string>() : new[] { WebTeile.Stammdaten, teil }.Distinct().ToArray();
        Fortschritt(10, $"{TeilName(teil)}: Daten werden zusammengestellt …");
        var d0 = D;
        var start = Start;
        var optionen = WebExportOptionen.Aus(P, teile);
        // Progress<T> meldet auf dem UI-Thread zurück (auch beim Direktexport ohne sichtbaren Dialog)
        var konvertiert = false;
        IProgress<(int I, int N)> schritt = new Progress<(int I, int N)>(x =>
        {
            // verspätete Meldungen nach dem Zusammenstellen ignorieren (sonst öffnete sich das Fenster nach dem Ende erneut)
            if (!konvertiert) Fortschritt(10 + 35 * x.I / Math.Max(1, x.N), $"Unterrichtsbestandteile werden konvertiert ({x.I} von {x.N})");
        });
        var paket = await Task.Run(() => UntPlanWebExport.PaketErstellen(d0, start, optionen, (i, n) => schritt.Report((i, n))));
        konvertiert = true;
        Fortschritt(45, $"{TeilName(teil)}: Übertragung an {P.Adresse} … ({paket.Tage.Count} Tage, {paket.Vertretungen.Count} Vertretungen)");
        var a = await UntPlanWebExport.HochladenAsync(P, paket);
        Fortschritt(100, $"{TeilName(teil)}: an UntPlanWeb übertragen (Revision {a.Revision}) um {DateTime.Now:HH:mm}.");
        var jetzt = DateTime.Now;
        AppZustand.Doc.Aendern("UntPlanWeb-Übertragung", AenderungsArt.Einstellungen, d =>
        {
            var p = d.Einstellungen.Portal;
            p.Revision = a.Revision;
            p.LetzteSynchronisation = jetzt;
            foreach (var t in teile.Length == 0 ? WebTeile.Felder.Keys.ToArray() : teile) p.LetzteUebertragung[t] = jetzt;
        });
    }

    /// <summary>Absenzen*: Krankmeldungen und Nachrichten über die Rückmeldungen übernehmen.</summary>
    private async Task<Action?> AbsenzenImportieren()
    {
        var start = Start;
        var typen = new[] { WebRueckmeldungsTypen.Absenz, WebRueckmeldungsTypen.Nachricht };
        Fortschritt(20, "Absenzen: Rückmeldungen werden abgeholt …");
        var liste = (await UntPlanWebExport.RueckmeldungenAsync(P))
            .Where(m => typen.Contains(m.Typ) && (!DateOnly.TryParse(m.Bis ?? m.Von, out var bis) || bis >= start)).ToList();
        var jetzt = DateTime.Now;
        if (liste.Count == 0)
        {
            Fortschritt(100, "Absenzen: keine neuen Absenzen oder Nachrichten.");
            AppZustand.Doc.Aendern("UntPlanWeb-Absenzen", AenderungsArt.Einstellungen, d => d.Einstellungen.Portal.LetzteUebertragung[Absenzen] = jetzt);
            return null;
        }
        Fortschritt(60, $"Absenzen: {liste.Count} Meldungen werden übernommen …");
        List<string> ok = new(), nein = new();
        AppZustand.Doc.Aendern("UntPlanWeb-Rückmeldungen", AenderungsArt.Vertretung, d =>
        {
            (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
            var tage = d.Abwesenheiten.Where(a => a.Text.Contains("Portal-Meldung")).SelectMany(a => Kalender.Tage(a.Von, a.Bis)).Distinct().ToList();
            foreach (var t in tage.Where(t => Kalender.IstUnterrichtstag(d, t))) Core.Vertretungsplanung.Vertretungen.Aktualisieren(d, t);
            d.Einstellungen.Portal.LetzteUebertragung[Absenzen] = jetzt;
        });
        await UntPlanWebExport.QuittierenAsync(P, ok);
        var text = $"{ok.Count} Absenzen oder Nachrichten übernommen{(nein.Count > 0 ? $", {nein.Count} abgelehnt (unbekannte Elemente oder kein Unterrichtstag)" : "")}.";
        Fortschritt(100, "Absenzen: " + text);
        return () => Ui.Info(text, "UntPlanWeb");
    }

    /// <summary>Studenten*: Schüler-Fehlzeiten aus dem Klassenbuch als Schülerabwesenheiten übernehmen (ohne Doppelimport).</summary>
    private async Task<Action?> StudentenImportieren()
    {
        var ab = AbDatum(Studenten);
        Fortschritt(20, $"Studenten: Fehlzeiten ab {ab:d.M.yyyy} werden abgeholt …");
        var liste = await UntPlanWebExport.FehlzeitenAsync(P, ab);
        Fortschritt(60, $"Studenten: {liste.Count} Fehlzeiten werden abgeglichen …");
        WebImportErgebnis? e = null;
        var jetzt = DateTime.Now;
        AppZustand.Doc.Aendern("UntPlanWeb-Fehlzeiten", AenderungsArt.Vertretung, d =>
        {
            e = UntPlanWebExport.FehlzeitenUebernehmen(d, liste, ab);
            d.Einstellungen.Portal.LetzteUebertragung[Studenten] = jetzt;
        });
        Fortschritt(100, $"Studenten: Schülerabwesenheiten übernommen – {e}.");
        if (D.SchuelerAbwesenheiten.Count == 0) return null;
        return () => { using var f = new SchuelerAbwesenheitenDialog(); f.ShowDialog(this); };
    }

    /// <summary>Kurse: Klassenbuch je Unterricht (Lehrstoff, Hausaufgaben) übernehmen (ohne Doppelimport).</summary>
    private async Task<Action?> KurseImportieren()
    {
        var ab = AbDatum(Kurse);
        Fortschritt(20, $"Kurse: Klassenbuch ab {ab:d.M.yyyy} wird abgeholt …");
        var liste = await UntPlanWebExport.KlassenbuchAsync(P, ab);
        Fortschritt(60, $"Kurse: {liste.Count} Klassenbucheinträge werden abgeglichen …");
        WebImportErgebnis? e = null;
        var jetzt = DateTime.Now;
        AppZustand.Doc.Aendern("UntPlanWeb-Klassenbuch", AenderungsArt.Vertretung, d =>
        {
            e = UntPlanWebExport.KlassenbuchUebernehmen(d, liste, ab);
            d.Einstellungen.Portal.LetzteUebertragung[Kurse] = jetzt;
        });
        Fortschritt(100, $"Kurse: Klassenbuch übernommen – {e}.");
        if (D.Klassenbuch.Count == 0) return null;
        return () => { using var f = new KlassenbuchDialog(); f.ShowDialog(this); };
    }

    // ------------------------------------------------------------------------------------------ Steuerelemente

    /// <summary>Kleines randloses Fortschrittsfenster (Text und grüner Balken) über dem Dialog, wie im Vorbild beim Export.</summary>
    internal sealed class UploadFortschritt : Form
    {
        private readonly Label _text = new() { Left = 18, Top = 22, Width = 300, Height = 18, Font = Ui.SchriftKlein };
        private readonly ProgressBar _balken = new() { Left = 18, Top = 46, Width = 300, Height = 18, Style = ProgressBarStyle.Continuous, ForeColor = Color.FromArgb(0, 160, 60) };

        public UploadFortschritt()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(336, 86);
            BackColor = Ui.Vertretungsmodus ? Color.FromArgb(240, 246, 243) : Color.FromArgb(246, 248, 251);
            Controls.Add(_text);
            Controls.Add(_balken);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var rand = new Pen(Color.FromArgb(150, 150, 150));
            e.Graphics.DrawRectangle(rand, 0, 0, Width - 1, Height - 1);
            using var innen = new Pen(Color.FromArgb(200, 200, 200));
            e.Graphics.DrawRectangle(innen, 8, 8, Width - 17, Height - 17);
        }

        public void Zeigen(Form? besitzer)
        {
            var b = besitzer?.Bounds ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Location = new Point(b.X + (b.Width - Width) / 2, b.Y + (b.Height - Height) / 2);
            if (besitzer != null) Show(besitzer); else Show();
            Refresh();
        }

        public void Setzen(int wert, string text)
        {
            if (IsDisposed) return;
            _text.Text = text;
            _balken.Value = Math.Clamp(wert, 0, 100);
            Refresh();
        }
    }

    /// <summary>Knopf in Vorbild-Optik: weiß mit grauem Rand, unter der Maus bzw. aktiv grün mit dunkelgrünem Rand.</summary>
    internal sealed class KlassikKnopf : Button
    {
        private readonly bool _dauerhaftGruen;
        private bool _maus;

        public KlassikKnopf(string text, bool dauerhaftGruen = false)
        {
            Text = text;
            _dauerhaftGruen = dauerhaftGruen;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.MouseOverBackColor = KnopfAktiv;
            FlatAppearance.MouseDownBackColor = Ui.Vertretungsmodus ? Color.FromArgb(150, 192, 175) : Color.FromArgb(140, 192, 210);
            Faerben();
        }

        private void Faerben()
        {
            var aktiv = Enabled && (_maus || Focused);
            var farbe = aktiv ? KnopfAktiv : _dauerhaftGruen ? KnopfGruen : Color.White;
            var rand = aktiv || _dauerhaftGruen ? RandGruen : RandGrau;
            var breite = aktiv ? 2 : 1;
            if (BackColor != farbe) BackColor = farbe;
            if (FlatAppearance.BorderColor != rand) FlatAppearance.BorderColor = rand;
            if (FlatAppearance.BorderSize != breite) FlatAppearance.BorderSize = breite;
        }

        protected override void OnMouseEnter(EventArgs e) { _maus = true; Faerben(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _maus = false; Faerben(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Faerben(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Faerben(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Faerben(); }
    }

    /// <summary>Blaues ⓘ-Symbol (Hinweis mit Tooltip).</summary>
    private sealed class InfoSymbol : Control
    {
        public InfoSymbol()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(18, 18);
            Cursor = Cursors.Help;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var blau = Color.FromArgb(0, 102, 204);
            using var stift = new Pen(blau, 1.3f);
            g.DrawEllipse(stift, 1.5f, 1.5f, Width - 4f, Height - 4f);
            using var pinsel = new SolidBrush(blau);
            var mx = (Width - 1) / 2f;
            g.FillEllipse(pinsel, mx - 1.1f, 4.2f, 2.2f, 2.2f);
            g.FillRectangle(pinsel, mx - 1f, 7.5f, 2f, 6f);
        }
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
        var tage = Feld("Tage voraus", new NumericUpDown { Minimum = 1, Maximum = 366, Value = Math.Clamp(p.TageVoraus, 1, 366) }, 70);
        var unbegrenzt = new CheckBox { Text = "unbegrenzt", Left = 206, Top = y - 30, AutoSize = true, Checked = p.OhneTagesbegrenzung };
        new ToolTip().SetToolTip(unbegrenzt, "Keine Tagesbegrenzung: Unterricht und Vertretungen bis zum Schuljahresende übertragen");
        links.Controls.Add(unbegrenzt);
        tage.Enabled = !unbegrenzt.Checked;
        unbegrenzt.CheckedChanged += (_, _) => tage.Enabled = !unbegrenzt.Checked;
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
            e.OhneTagesbegrenzung = unbegrenzt.Checked;
            e.IgnoriertenUnterrichtUebertragen = ignoriert.Checked;
            e.FerienNichtExportieren = ferien.Checked;
            e.LehrerEmail = email.Checked;
            e.LehrerAbsenzen = absenzen.Checked;
            e.Absenzgruende = gruende.Checked;
            e.SchuelerExportieren = schueler.Checked;
        });
    }
}
