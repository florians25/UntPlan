using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Web.Contracts;

namespace UntPlan.App.Dialoge;

/// <summary>
/// „Stundenplanänderungen aus UntPlanWeb“: Raumbuchungen bzw. Raumänderungen, zusätzliche Unterrichte (Aktivitäten) und
/// Prüfungen. Datumsbereich Von/Bis, „Bereits durchgeführte/ignorierte Änderungen anzeigen“, Buchungen bzw. Prüfungen
/// markieren, Ignorieren und Übernehmen. Übernommene Änderungen erhalten in der Spalte Status ein Häkchen und werden
/// nicht erneut importiert. Über das Zahnrad: Einstellungen zum Import.
/// </summary>
public sealed class StundenplanAenderungenDialog : Dialog
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly CheckBox _erledigte = new() { Text = "Bereits durchgeführte/ignorierte Änderungen anzeigen.", AutoSize = true, Left = 8, Top = 10 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Left = 8, Top = 34 };
    private List<WebRueckmeldung> _server = new();
    private readonly bool _offline;

    private static Schuldatei D => AppZustand.D;
    private static PortalEinstellungen P => AppZustand.D.Einstellungen.Portal;

    /// <param name="vorgabe">Rückmeldungen ohne Serverabruf (Selbsttest), sonst null.</param>
    public StundenplanAenderungenDialog(IEnumerable<WebRueckmeldung>? vorgabe = null) : base("Stundenplanänderungen aus UntPlanWeb", 1000, 440)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(760, 300);
        Ok.Visible = false;
        Abbrechen.Visible = false;
        _offline = vorgabe != null;
        if (vorgabe != null) _server = vorgabe.ToList();
        var heute = AppZustand.Vertretungsmodus ? AppZustand.Datum : DateOnly.FromDateTime(DateTime.Today);
        _von.Value = heute.ToDateTime(TimeOnly.MinValue);
        _bis.Value = heute.AddDays(Math.Max(1, P.StplAenderungenTage)).ToDateTime(TimeOnly.MinValue);

        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = Ui.Kopf, Renderer = new Steuerelemente.LeistenRenderer() };
        ts.Items.Add(new ToolStripButton(null, Symbole.Get("aktualisieren", 16), async (_, _) => await Laden()) { ToolTipText = "Stundenplanänderungen neu laden" });
        ts.Items.Add(new ToolStripButton(null, Symbole.Get("filter", 16), (_, _) => _erledigte.Checked = !_erledigte.Checked) { ToolTipText = "Bereits durchgeführte/ignorierte Änderungen ein-/ausblenden" });
        ts.Items.Add(new ToolStripButton(null, Symbole.Get("einstellungen", 16), (_, _) => Einstellungen()) { ToolTipText = "Einstellungen" });
        var laden = new ToolStripButton("Laden") { DisplayStyle = ToolStripItemDisplayStyle.Text, Alignment = ToolStripItemAlignment.Right };
        laden.Click += async (_, _) => await Laden();
        ts.Items.Add(laden);
        ts.Items.Add(new ToolStripControlHost(_bis) { Alignment = ToolStripItemAlignment.Right });
        ts.Items.Add(new ToolStripLabel("Bis:") { Alignment = ToolStripItemAlignment.Right });
        ts.Items.Add(new ToolStripControlHost(_von) { Alignment = ToolStripItemAlignment.Right });
        ts.Items.Add(new ToolStripLabel("Von:") { Alignment = ToolStripItemAlignment.Right });

        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        _g.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[]
        {
            ("Id", "", 60), ("D", "Datum", 80), ("Z", "Zeit", 90), ("S", "Stunde", 46), ("A", "Art", 70), ("U", "Lv-Id", 50), ("KF", "Klasse(n) / Fach", 120), ("L", "Lehrer", 80),
            ("R", "Räume", 80), ("W", "Wert", 44), ("St", "Statistik", 56), ("T", "Text", 160), ("E", "Buchungsersteller*in", 120), ("X", "Status", 50),
        })
            _g.Columns[_g.Columns.Add(n, k)].Width = w;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.Columns["T"]!.MinimumWidth = 120;
        _g.Columns["X"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _g.CellPainting += HakenZeichnen;

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Ui.Kopf };
        Button Knopf(string text, int rechts, Action a, bool hervor = false)
        {
            var b = new Button { Text = text, Width = 118, Height = 26, Top = 14, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            b.Left = unten.Width - rechts;
            if (hervor) b.BackColor = Ui.BlauHell;
            b.Click += (_, _) => a();
            unten.Controls.Add(b);
            return b;
        }
        unten.Width = ClientSize.Width;
        Knopf("Übernehmen", 128, () => _ = Uebernehmen(), true);
        Knopf("Ignorieren", 252, () => _ = Ignorieren());
        Knopf("Prüfungen markieren", 376, () => Markieren(StplAenderungsArt.Pruefung));
        Knopf("Buchungen markieren", 500, () => Markieren(null));
        unten.Controls.Add(_erledigte);
        unten.Controls.Add(_status);
        _erledigte.CheckedChanged += (_, _) => Fuellen();
        _von.ValueChanged += (_, _) => Fuellen();
        _bis.ValueChanged += (_, _) => Fuellen();
        Controls.Add(_g);
        Controls.Add(unten);
        Controls.Add(ts);
        Shown += async (_, _) => await Laden();
    }

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private async Task Laden()
    {
        if (!_offline)
        {
            if (string.IsNullOrWhiteSpace(P.Adresse)) _status.Text = "Keine Verbindung zu UntPlanWeb eingerichtet – es werden nur bereits bearbeitete Änderungen angezeigt.";
            else
            {
                _status.Text = "Stundenplanänderungen werden geladen …";
                UseWaitCursor = true;
                try
                {
                    _server = (await UntPlanWebExport.RueckmeldungenAsync(P)).Where(m => StundenplanAenderungen.Typen.Contains(m.Typ)).ToList();
                    _status.Text = $"{_server.Count} Änderungen vom Server geladen ({DateTime.Now:HH:mm}).";
                }
                catch (Exception ex)
                {
                    _status.Text = ex is HttpRequestException or TaskCanceledException ? "Keine Verbindung zum UntPlanWeb-Server." : ex.Message;
                }
                finally { UseWaitCursor = false; }
            }
        }
        Fuellen();
    }

    private void Fuellen()
    {
        _g.Rows.Clear();
        DateOnly? letzter = null;
        foreach (var a in StundenplanAenderungen.Analysieren(D, _server, Von, Bis, _erledigte.Checked))
        {
            if (a.Datum != letzter)
            {
                // Gruppenzeile je Datum
                var gi = _g.Rows.Add("", Format.Datum(a.Datum));
                _g.Rows[gi].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 215);
                _g.Rows[gi].DefaultCellStyle.Font = Ui.SchriftFett;
                letzter = a.Datum;
            }
            var i = _g.Rows.Add("⊞ " + a.Id, Format.Datum(a.Datum), a.Zeit, a.Stunde, a.ArtText, a.UnterrichtId?.ToString() ?? "", a.KlassenFach, a.Lehrer, a.Raeume, a.Wert, a.Statistik,
                a.Fehler is null ? a.Text : $"{a.Text}  ({a.Fehler})".Trim(), a.Ersteller, a.Uebernommen == true ? "✔" : a.Uebernommen == false ? "ignoriert" : "");
            var r = _g.Rows[i];
            r.Tag = a;
            if (a.Art == StplAenderungsArt.Aktivitaet) r.DefaultCellStyle.ForeColor = Color.FromArgb(0, 120, 0);
            if (a.Fehler != null) r.DefaultCellStyle.ForeColor = Color.Firebrick;
            if (a.Erledigt) r.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        }
    }

    private List<StplAenderung> Gewaehlt() => _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<StplAenderung>().Where(a => !a.Erledigt).ToList();

    /// <summary>Alle Buchungen (Unterricht und Aktivität) bzw. alle Prüfungen markieren.</summary>
    private void Markieren(StplAenderungsArt? art)
    {
        _g.ClearSelection();
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is StplAenderung a && !a.Erledigt && (art is null ? a.Art != StplAenderungsArt.Pruefung : a.Art == art)) r.Selected = true;
    }

    private async Task Quittieren(IEnumerable<string> ids)
    {
        var liste = ids.ToList();
        if (_offline || liste.Count == 0 || string.IsNullOrWhiteSpace(P.Adresse)) return;
        try { await UntPlanWebExport.QuittierenAsync(P, liste); }
        catch (Exception) { /* bleibt auf dem Server; wird hier als übernommen erkannt und nicht doppelt importiert */ }
    }

    private async Task Uebernehmen()
    {
        var liste = Gewaehlt();
        if (liste.Count == 0) { Ui.Info("Bitte zuerst Änderungen markieren (z. B. „Buchungen markieren“).", Text); return; }
        List<string> ok = new();
        List<(string Id, string Grund)> nein = new();
        try
        {
            AppZustand.Doc.Aendern("Stundenplanänderungen aus UntPlanWeb", AenderungsArt.Vertretung, d => (ok, nein) = StundenplanAenderungen.Uebernehmen(d, liste, AppZustand.Benutzer));
        }
        catch (Exception ex) { Ui.Fehler(ex); return; }
        await Quittieren(ok);
        Fuellen();
        _status.Text = $"{ok.Count} Änderung(en) übernommen." + (nein.Count > 0 ? $" Nicht übernommen: {string.Join("; ", nein.Select(n => $"{n.Id} ({n.Grund})"))}" : "");
    }

    private async Task Ignorieren()
    {
        var liste = Gewaehlt();
        if (liste.Count == 0) return;
        AppZustand.Doc.Aendern("Stundenplanänderungen ignorieren", AenderungsArt.Einstellungen, d => StundenplanAenderungen.Ignorieren(d, liste, AppZustand.Benutzer));
        await Quittieren(liste.Select(a => a.Id));
        Fuellen();
        _status.Text = $"{liste.Count} Änderung(en) ignoriert.";
    }

    /// <summary>Einstellungen zum Import der Stundenplanänderungen.</summary>
    private void Einstellungen()
    {
        using var f = new Dialog("Einstellungen", 470, 150);
        f.Ok.Text = "Ok";
        var vor = new CheckBox { Text = "Pausenaufsichten vor Prüfungen außer Betracht lassen", Left = 12, Top = 12, AutoSize = true, Checked = P.PausenaufsichtVorPruefungIgnorieren };
        var nach = new CheckBox { Text = "Pausenaufsichten nach Prüfungen außer Betracht lassen", Left = 12, Top = 36, AutoSize = true, Checked = P.PausenaufsichtNachPruefungIgnorieren };
        var tage = new NumericUpDown { Left = 12, Top = 64, Width = 50, Minimum = 1, Maximum = 365, Value = Math.Clamp(P.StplAenderungenTage, 1, 365) };
        f.Controls.AddRange(new Control[]
        {
            vor, nach, tage,
            new Label { Text = "Für wie viele Tage in die Zukunft sollen Stundenplanänderungen\nstandardmäßig importiert werden", Left = 70, Top = 62, Width = 390, Height = 34 },
        });
        if (f.ShowDialog(this) != DialogResult.OK) return;
        AppZustand.Doc.Aendern("Einstellungen Stundenplanänderungen", AenderungsArt.Einstellungen, d =>
        {
            var p = d.Einstellungen.Portal;
            p.PausenaufsichtVorPruefungIgnorieren = vor.Checked;
            p.PausenaufsichtNachPruefungIgnorieren = nach.Checked;
            p.StplAenderungenTage = (int)tage.Value;
        });
        _bis.Value = Von.AddDays(P.StplAenderungenTage).ToDateTime(TimeOnly.MinValue);
    }

    private void HakenZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
        Ui.ZellHaken(e, Color.FromArgb(60, 170, 60));
    }
}
