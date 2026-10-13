using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Modul Vertretungsplanung: Vertretungsfenster eines Tages mit Vertretungsvorschlag, Bereitschaft, Entfall,
/// Raumvertretung, Verlegung, Betreuung, Sondereinsatz, Klausur, Pausenaufsicht-Vertretungen und Tagestext.
/// </summary>
public sealed class VertretungenFenster : MdiFenster, IDruckbar
{
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd, dd.MM.yyyy", Width = 130 };
    private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _pausen = new() { Dock = DockStyle.Fill };
    private readonly TextBox _tagestext = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _lehrertext = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _info = new() { Dock = DockStyle.Top, Height = 22, Padding = new Padding(6, 4, 0, 0), BackColor = Ui.Kopf };
    private readonly TabControl _unten = new() { Dock = DockStyle.Fill };
    private bool _laedt;

    private DateOnly Datum => DateOnly.FromDateTime(_datum.Value);

    public VertretungenFenster()
    {
        Text = "Vertretungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("vertretung");
        Size = new Size(1180, 640);
        Positionieren();
        var heute = DateOnly.FromDateTime(DateTime.Today);
        _datum.Value = (Kalender.IstUnterrichtstag(D, heute) ? heute : Kalender.NaechsterUnterrichtstag(D, heute)).ToDateTime(TimeOnly.MinValue);
        _datum.ValueChanged += (_, _) => TagWechseln();

        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Vorheriger Tag", "zurueck", () => _datum.Value = Kalender.NaechsterUnterrichtstag(D, Datum, -1).ToDateTime(TimeOnly.MinValue)));
        ts.Items.Add(new ToolStripControlHost(_datum));
        ts.Items.Add(Knopf("Nächster Tag", "vor", () => _datum.Value = Kalender.NaechsterUnterrichtstag(D, Datum).ToDateTime(TimeOnly.MinValue)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Abwesenheiten", "abwesenheit", () => HauptFenster.Oeffnen<AbwesenheitenFenster>("abw", () => new AbwesenheitenFenster()), false));
        ts.Items.Add(Knopf("Vertreter aus Vorschlag zuweisen", "planen", VorschlagZuweisen, false));
        ts.Items.Add(Knopf("Entfall", "entfall", () => ArtSetzen(VertretungsArt.Entfall), false));
        ts.Items.Add(Knopf("Raumvertretung", "raeume", Raumvertretung));
        ts.Items.Add(Knopf("Verlegung", "tausch", Verlegung));
        ts.Items.Add(Knopf("Betreuung", "lehrer", () => ArtSetzen(VertretungsArt.Betreuung)));
        ts.Items.Add(Knopf("Sondereinsatz", "neu", () => NeuerEintrag(VertretungsArt.Sondereinsatz)));
        ts.Items.Add(Knopf("Klausur", "kommentar", () => NeuerEintrag(VertretungsArt.Klausur)));
        ts.Items.Add(Knopf("Vertretung zurücksetzen (offen)", "rueckgaengig", Zuruecksetzen));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Automatische Vertretung des Tages", "optimierung", Automatisch, false));
        ts.Items.Add(Knopf("Vertretungsgewichtung", "gewichtung", () => { if (VertretungsgewichtungDialog.Zeigen(this)) VorschlagAnzeigen(); }));
        ts.Items.Add(new ToolStripSeparator());
        _filter.Items.AddRange(new object[] { "Alle Einträge", "Nur offene", "Vertretungen", "Entfälle/Freisetzungen", "Raumvertretungen", "Pausenaufsichten" });
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => Neuaufbau();
        ts.Items.Add(_filter);
        ts.Items.Add(Knopf("Vertretungsplan (HTML)", "html", () => HtmlAusgabe(Datum, 1, true)));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (name, kopf, breite, lesbar) in new[]
        {
            ("Nr", "Vtr-Nr.", 55, true), ("Art", "Art", 100, true), ("Std", "Std.", 40, true), ("Lehrer", "(Lehrer)", 65, true), ("Vertreter", "Vertreter", 70, false),
            ("Fach", "(Fach)", 55, true), ("NFach", "Fach", 55, false), ("Klassen", "Klasse(n)", 90, true), ("Raum", "(Raum)", 60, true), ("NRaum", "Raum", 60, false),
            ("Grund", "Grund", 70, true), ("Mehr", "Mehrarb.", 60, true), ("Text", "Vertretungstext", 240, false),
        })
        {
            var c = _g.Columns[_g.Columns.Add(name, kopf)];
            c.Width = breite;
            c.ReadOnly = lesbar;
            if (!lesbar) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 238);
        }
        _g.Columns["Text"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.AllowUserToAddRows = false;
        _g.CellEndEdit += ZelleGeaendert;
        _g.SelectionChanged += (_, _) => { if (!_laedt) VorschlagAnzeigen(); };
        _g.CellFormatting += Formatieren;

        Ui.RasterGestalten(_vorschlag, false);
        _vorschlag.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorschlag.MultiSelect = false;
        foreach (var (name, kopf, breite) in new[]
        {
            ("L", "Lehrer", 70), ("P", "Punkte", 55), ("B", "Bereitsch.", 65), ("F", "freigesetzt", 70), ("K", "kennt Kl.", 60), ("FL", "Fachlehrer", 65),
            ("Lage", "Lage", 95), ("W", "Vtr. Woche", 70), ("M", "Vtr. Monat", 70), ("J", "Vtr. Jahr", 65), ("S", "Saldo", 55), ("Kr", "Kriterien", 260),
        })
            _vorschlag.Columns[_vorschlag.Columns.Add(name, kopf)].Width = breite;
        _vorschlag.Columns["Kr"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VorschlagZuweisen(); };

        Ui.RasterGestalten(_pausen, false);
        _pausen.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (name, kopf, breite) in new[] { ("G", "Gang", 90), ("P", "Pause nach Std.", 100), ("L", "Aufsicht (abwesend)", 120), ("V", "Vertreter", 90), ("Kand", "mögliche Vertreter", 400) })
            _pausen.Columns[_pausen.Columns.Add(name, kopf)].Width = breite;
        _pausen.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) PausenVertreter(); };
        var pausenLeiste = Werkzeugleiste();
        pausenLeiste.Items.Add(Knopf("Vertreter zuweisen", "planen", PausenVertreter, false));
        pausenLeiste.Items.Add(Knopf("Vertretung löschen", "loeschen", PausenLoeschen, false));
        var pausenSeite = new TabPage("Pausenaufsicht-Vertretungen");
        pausenSeite.Controls.Add(_pausen);
        pausenSeite.Controls.Add(pausenLeiste);

        var textSeite = new TabPage("Tagestext");
        var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        tl.Controls.Add(new Label { Text = "Text für alle (Schüler und Lehrer):", AutoSize = true }, 0, 0);
        tl.Controls.Add(new Label { Text = "Nur für Lehrer:", AutoSize = true }, 1, 0);
        tl.Controls.Add(_tagestext, 0, 1);
        tl.Controls.Add(_lehrertext, 1, 1);
        var speichern = new Button { Text = "Tagestext übernehmen", Width = 160, Height = 26 };
        speichern.Click += (_, _) => TagestextSpeichern();
        tl.Controls.Add(speichern, 0, 2);
        textSeite.Controls.Add(tl);

        _unten.TabPages.Add(new TabPage("Vertretungsvorschlag") { Controls = { _vorschlag } });
        _unten.TabPages.Add(pausenSeite);
        _unten.TabPages.Add(textSeite);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_g);
        split.Panel1.Controls.Add(_info);
        split.Panel2.Controls.Add(_unten);
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = (int)(split.Height * 0.58);
        TagWechseln();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    /// <summary>Beim Tageswechsel offene Vertretungen aus den Abwesenheiten erzeugen (nur wenn sich etwas ändert).</summary>
    private void TagWechseln()
    {
        var datum = Datum;
        var probe = Dokument.Klonen(D);
        Vertretungen.Aktualisieren(probe, datum);
        static string Sig(Schuldatei d, DateOnly t) => string.Join(";", d.Vertretungen.Where(v => v.Datum == t).Select(v => $"{v.UnterrichtId}/{v.ZeilenIndex}/{v.Stunde}/{v.Art}").OrderBy(s => s));
        if (Sig(probe, datum) != Sig(D, datum))
            Aendern($"Vertretungen {Format.Datum(datum)} aktualisiert", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, datum));
        var texte = D.Tagestexte.Where(t => t.Datum == datum).ToList();
        _tagestext.Text = string.Join(Environment.NewLine, texte.Where(t => !t.NurLehrer).Select(t => t.Text));
        _lehrertext.Text = string.Join(Environment.NewLine, texte.Where(t => t.NurLehrer).Select(t => t.Text));
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        if (IsDisposed) return;
        _laedt = true;
        var markiert = Aktuelle().Select(v => v.Id).ToHashSet();
        _g.Rows.Clear();
        var datum = Datum;
        var liste = Vertretungen.Am(D, datum).Where(v => _filter.SelectedIndex switch
        {
            1 => v.Offen,
            2 => v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung or VertretungsArt.Betreuung or VertretungsArt.Sondereinsatz,
            3 => v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung,
            4 => v.Art == VertretungsArt.Raumvertretung,
            5 => v.Art == VertretungsArt.Pausenaufsicht,
            _ => true,
        }).ToList();
        foreach (var v in liste)
        {
            var r = _g.Rows.Add(v.Id, Vertretungsplan.ArtText(v.Art), v.Art == VertretungsArt.Pausenaufsicht ? $"n.{v.Stunde}" : v.Stunde.ToString(), Ix.KLehrer(v.LehrerId),
                v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung ? "---" : v.VertreterId.HasValue ? Ix.KLehrer(v.VertreterId) : "???",
                Ix.KFach(v.FachId), Ix.KFach(v.VertretungsfachId), Ix.KKlassen(v.KlassenIds), Ix.KRaum(v.RaumId), Ix.KRaum(v.VertretungsraumId),
                v.Grund, v.Mehrarbeit ? "ja" : "",
                v.Art == VertretungsArt.Verlegung && v.NachDatum.HasValue ? $"→ {Format.Datum(v.NachDatum.Value)} {v.NachStunde}. Std. {v.Text}".Trim() : v.Text);
            _g.Rows[r].Tag = v;
            if (markiert.Contains(v.Id)) _g.Rows[r].Selected = true;
        }
        var alle = Vertretungen.Am(D, datum);
        var abwL = D.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && datum >= a.Von && datum <= a.Bis).Select(a => Ix.KLehrer(a.ElementId)).Distinct().ToList();
        var abwK = D.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Klasse && datum >= a.Von && datum <= a.Bis).Select(a => Ix.K(ElementTyp.Klasse, a.ElementId)).Distinct().ToList();
        _info.Text = Kalender.IstUnterrichtstag(D, datum)
            ? $"{Planungsdienst.Tagname(D, Kalender.TagIndex(D, datum))} {Format.Datum(datum)} ({Kalender.WochenTyp(D, datum)}-Woche)   |   {alle.Count} Einträge, davon {alle.Count(v => v.Offen)} offen   |   " +
              $"Abwesende Lehrer: {(abwL.Count == 0 ? "–" : string.Join(", ", abwL))}   |   Abwesende Klassen: {(abwK.Count == 0 ? "–" : string.Join(", ", abwK))}"
            : $"{Format.Datum(datum)}: kein Unterrichtstag ({Kalender.FerienAm(D, datum)?.Langname ?? "Wochenende"})";
        _info.ForeColor = alle.Any(v => v.Offen) ? Color.DarkRed : Color.Black;
        _laedt = false;
        VorschlagAnzeigen();
        PausenAnzeigen();
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        if (v.Offen && _g.Columns[e.ColumnIndex].Name == "Vertreter") { e.CellStyle!.BackColor = Ui.Rot; e.CellStyle.ForeColor = Color.White; }
        if (v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung) e.CellStyle!.ForeColor = Color.Firebrick;
        if (_g.Columns[e.ColumnIndex].Name == "Art")
            e.CellStyle!.BackColor = v.Art switch
            {
                VertretungsArt.Vertretung or VertretungsArt.StattVertretung => v.Offen ? Color.FromArgb(255, 220, 220) : Color.FromArgb(220, 240, 220),
                VertretungsArt.Entfall or VertretungsArt.Freisetzung => Color.FromArgb(255, 235, 205),
                VertretungsArt.Raumvertretung => Color.FromArgb(220, 230, 255),
                VertretungsArt.Verlegung or VertretungsArt.Tausch => Color.FromArgb(240, 225, 250),
                _ => Color.FromArgb(240, 240, 240),
            };
    }

    private List<Vertretung> Aktuelle() =>
        _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Vertretung>().Concat(_g.CurrentRow?.Tag is Vertretung c && _g.SelectedRows.Count == 0 ? new[] { c } : Array.Empty<Vertretung>()).Distinct().ToList();

    private Vertretung? Aktuelle1() => _g.CurrentRow?.Tag as Vertretung;

    private void VorschlagAnzeigen()
    {
        _vorschlag.Rows.Clear();
        if (Aktuelle1() is not { } v || v.Art is VertretungsArt.Pausenaufsicht or VertretungsArt.Freisetzung) return;
        foreach (var k in VertretungsVorschlag.Berechnen(D, v))
        {
            var r = _vorschlag.Rows.Add(k.Lehrer.Kurzname, k.Punkte.ToString("0"), k.Bereitschaft ? "✔" : "", k.Freigesetzt ? "✔" : "", k.KenntKlasse ? "✔" : "", k.Fachlehrer ? "✔" : "",
                k.Lage, k.ZaehlerWoche, k.ZaehlerMonat, k.ZaehlerJahr, k.Saldo.ToString("+0;-0;0"), string.Join(", ", k.Kriterien));
            _vorschlag.Rows[r].Tag = k;
            if (k.Bereitschaft || k.Freigesetzt) _vorschlag.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(220, 245, 220);
            else if (k.Punkte < 0) _vorschlag.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
        if (VertretungsVorschlag.EntfallSinnvoll(D, v))
        {
            var r = _vorschlag.Rows.Add("Entfall", "", "", "", "", "", "Randstunde", "", "", "", "", $"Klasse ab Jahrgang {D.Einstellungen.EntfallAbJahrgang} in Randstunde – Entfall möglich");
            _vorschlag.Rows[r].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
    }

    private void VorschlagZuweisen()
    {
        var v = Aktuelle1();
        if (v is null) return;
        if (_vorschlag.CurrentRow is null) { Ui.Info("Bitte im Vertretungsvorschlag einen Lehrer markieren."); return; }
        if (_vorschlag.CurrentRow.Tag is not Kandidat k)
        {
            ArtSetzen(VertretungsArt.Entfall);
            return;
        }
        var id = v.Id;
        Aendern($"Vertretung durch {k.Lehrer.Kurzname}", AenderungsArt.Vertretung, d =>
        {
            var x = d.Vertretungen.First(y => y.Id == id);
            x.VertreterId = k.Lehrer.Id;
            x.Mehrarbeit = k.Mehrarbeit;
            x.VertretungsfachId ??= x.FachId;
            if (x.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung) x.Art = VertretungsArt.Vertretung;
            if (k.Freigesetzt && x.Art == VertretungsArt.Vertretung) x.Art = VertretungsArt.StattVertretung;
        });
        Weiter(id);
    }

    /// <summary>Nach dem Zuweisen zur nächsten offenen Vertretung springen.</summary>
    private void Weiter(int id)
    {
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Vertretung x && x.Offen && x.Id != id)
            {
                _g.ClearSelection();
                _g.CurrentCell = r.Cells[1];
                r.Selected = true;
                return;
            }
    }

    private void ArtSetzen(VertretungsArt art)
    {
        var ids = Aktuelle().Where(v => v.UnterrichtId.HasValue).Select(v => v.Id).ToList();
        if (ids.Count == 0) return;
        Lehrer? betreuer = null;
        if (art == VertretungsArt.Betreuung)
        {
            betreuer = _vorschlag.CurrentRow?.Tag is Kandidat k ? k.Lehrer : AuswahlDialog.Element(this, ElementTyp.Lehrer, "Betreuung durch") as Lehrer;
            if (betreuer is null) return;
        }
        Aendern(Vertretungsplan.ArtText(art), AenderungsArt.Vertretung, d =>
        {
            foreach (var x in d.Vertretungen.Where(y => ids.Contains(y.Id)))
            {
                x.Art = art;
                if (art == VertretungsArt.Entfall)
                {
                    x.VertreterId = null;
                    x.VertretungsraumId = null;
                    x.Mehrarbeit = false;
                    if (string.IsNullOrEmpty(x.Text)) x.Text = "fällt aus";
                }
                else if (betreuer != null)
                {
                    x.VertreterId = betreuer.Id;
                    x.VertretungsfachId = null;
                    x.Mehrarbeit = Vertretungen.HatBereitschaft(d, new DatenIndex(d), betreuer.Id, x.Datum, x.Stunde, Vertretungen.Tagesplan(d, new DatenIndex(d), x.Datum)) == false;
                    if (string.IsNullOrEmpty(x.Text)) x.Text = "Betreuung";
                }
            }
        });
    }

    private void Zuruecksetzen()
    {
        var ids = Aktuelle().Where(v => v.UnterrichtId.HasValue).Select(v => v.Id).ToList();
        if (ids.Count == 0) return;
        var datum = Datum;
        Aendern("Vertretung zurückgesetzt", AenderungsArt.Vertretung, d =>
        {
            d.Vertretungen.RemoveAll(v => ids.Contains(v.Id));
            Vertretungen.Aktualisieren(d, datum);
        });
    }

    private void Loeschen()
    {
        var ids = Aktuelle().Select(v => v.Id).ToList();
        if (ids.Count == 0) return;
        if (Aktuelle().Any(v => v.UnterrichtId.HasValue && !Vertretungen.IstAutomatisch(v)) && !Ui.Frage("Bearbeitete Vertretungen löschen? Ist der Lehrer weiterhin abwesend, wird die Vertretung wieder als offen angelegt.")) return;
        var datum = Datum;
        Aendern("Vertretung gelöscht", AenderungsArt.Vertretung, d =>
        {
            d.Vertretungen.RemoveAll(v => ids.Contains(v.Id));
            Vertretungen.Aktualisieren(d, datum);
        });
    }

    private void Automatisch()
    {
        var datum = Datum;
        var n = 0;
        using (new WarteCursor())
            Aendern($"Automatische Vertretung {Format.Datum(datum)}", AenderungsArt.Vertretung, d => n = VertretungsVorschlag.Automatisch(d, datum));
        var offen = D.Vertretungen.Count(v => v.Datum == datum && v.Offen);
        Ui.Info($"{n} Vertretungen wurden automatisch geplant.{(offen > 0 ? $"\n{offen} Vertretungen sind weiterhin offen." : "")}", "Automatische Vertretung");
    }

    private void Raumvertretung()
    {
        if (Aktuelle1() is not { } v) return;
        var plan = Vertretungen.Tagesplan(D, Ix, v.Datum);
        var belegt = plan.Where(x => x.Planstunde.Stunde == v.Stunde).SelectMany(x => x.Planstunde.Raeume).OfType<int>().ToHashSet();
        belegt.UnionWith(D.Vertretungen.Where(x => x.Datum == v.Datum && x.Stunde == v.Stunde && x.VertretungsraumId.HasValue && x.Id != v.Id).Select(x => x.VertretungsraumId!.Value));
        var frei = D.Raeume.Where(r => !r.Ignorieren && !belegt.Contains(r.Id) && Vertretungen.RaumGesperrt(D, r.Id, v.Datum, v.Stunde) is null)
            .OrderByDescending(r => v.RaumId.HasValue && Ix.RaumKandidaten(new Kopplungszeile { FachraumId = v.RaumId }).Contains(r.Id)).ThenBy(r => r.Kurzname).ToList();
        // Freigewordene Räume (Entfall/Freisetzung) zuerst
        var freigeworden = D.Vertretungen.Where(x => x.Datum == v.Datum && x.Stunde == v.Stunde && x.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung && x.RaumId.HasValue).Select(x => x.RaumId!.Value).ToHashSet();
        frei.InsertRange(0, D.Raeume.Where(r => freigeworden.Contains(r.Id) && !frei.Contains(r)));
        using var d = new Dialog("Raumvertretung", 360, 380);
        var lb = new ListBox { Left = 12, Top = 30, Width = 336, Height = 300 };
        d.Controls.Add(new Label { Text = $"Freie Räume am {Format.Datum(v.Datum)}, {v.Stunde}. Stunde:", Left = 12, Top = 8, AutoSize = true });
        foreach (var r in frei) lb.Items.Add(new Eintrag(r.Id, $"{r.Kurzname} – {r.Langname}{(freigeworden.Contains(r.Id) ? "  (frei geworden)" : "")}{(r.Kapazitaet > 0 ? $"  [{r.Kapazitaet} Pl.]" : "")}"));
        lb.DoubleClick += (_, _) => d.DialogResult = DialogResult.OK;
        d.Controls.Add(lb);
        if (lb.Items.Count > 0) lb.SelectedIndex = 0;
        if (d.ShowDialog(this) != DialogResult.OK || lb.SelectedItem is not Eintrag e) return;
        var id = v.Id;
        Aendern("Raumvertretung", AenderungsArt.Vertretung, dd =>
        {
            var x = dd.Vertretungen.First(y => y.Id == id);
            x.VertretungsraumId = e.Id;
            if (x.Art == VertretungsArt.Raumvertretung) x.VertreterId ??= x.LehrerId;
        });
    }

    private void Verlegung()
    {
        if (Aktuelle1() is not { UnterrichtId: not null } v) return;
        using var d = new Dialog("Verlegung", 380, 200);
        var y = 14;
        var datum = Ui.Zeile(d, "Verlegen auf Datum:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = v.Datum.ToDateTime(TimeOnly.MinValue) }, ref y, 140, 120);
        var stunde = Ui.Zeile(d, "Stunde:", new NumericUpDown { Minimum = 1, Maximum = D.Zeitraster.StundenProTag, Value = v.Stunde }, ref y, 140, 60);
        var info = new Label { Left = 12, Top = y + 4, Width = 356, Height = 40, ForeColor = Color.DarkRed };
        d.Controls.Add(info);
        void Pruefen()
        {
            var nd = DateOnly.FromDateTime(datum.Value);
            var ns = (int)stunde.Value;
            var p = Vertretungen.Tagesplan(D, Ix, nd);
            var probleme = new List<string>();
            if (!Kalender.IstUnterrichtstag(D, nd)) probleme.Add("kein Unterrichtstag");
            if (v.LehrerId is int l && Vertretungen.LehrerBelegt(D, Ix, l, nd, ns, p) is { } g) probleme.Add($"{Ix.KLehrer(l)}: {g}");
            foreach (var k in v.KlassenIds)
                if (p.Any(x => x.Planstunde.Stunde == ns && x.Unterricht.KlassenIds.Contains(k))) probleme.Add($"{Ix.K(ElementTyp.Klasse, k)} hat Unterricht");
            info.Text = probleme.Count == 0 ? "" : "Achtung: " + string.Join("; ", probleme);
            info.ForeColor = probleme.Count == 0 ? Color.DarkGreen : Color.DarkRed;
            if (probleme.Count == 0) info.Text = "Lehrer und Klasse(n) sind frei.";
        }
        datum.ValueChanged += (_, _) => Pruefen();
        stunde.ValueChanged += (_, _) => Pruefen();
        Pruefen();
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var id = v.Id;
        var ziel = DateOnly.FromDateTime(datum.Value);
        var zs = (int)stunde.Value;
        Aendern("Verlegung", AenderungsArt.Vertretung, dd =>
        {
            var x = dd.Vertretungen.First(q => q.Id == id);
            x.Art = VertretungsArt.Verlegung;
            x.NachDatum = ziel;
            x.NachStunde = zs;
            x.VertreterId = null;
            // Zielstunde als Sondereinsatz des eigenen Lehrers eintragen
            dd.Vertretungen.Add(new Vertretung
            {
                Id = dd.NeueId(), Datum = ziel, Stunde = zs, Art = VertretungsArt.Sondereinsatz, UnterrichtId = null, LehrerId = x.LehrerId, VertreterId = x.LehrerId,
                FachId = x.FachId, VertretungsfachId = x.FachId, KlassenIds = new List<int>(x.KlassenIds), VertretungsraumId = x.VertretungsraumId ?? x.RaumId,
                Text = $"verlegt vom {Format.Datum(x.Datum)} {x.Stunde}. Std.",
            });
        });
    }

    private void NeuerEintrag(VertretungsArt art)
    {
        using var d = new Dialog(Vertretungsplan.ArtText(art), 420, 270);
        var y = 14;
        var stunde = Ui.Zeile(d, "Stunde:", new NumericUpDown { Minimum = 1, Maximum = D.Zeitraster.StundenProTag, Value = Math.Max(1, Aktuelle1()?.Stunde ?? 1) }, ref y, 120, 60);
        var lehrer = Ui.Zeile(d, art == VertretungsArt.Klausur ? "Aufsicht:" : "Lehrer:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 120, 160);
        lehrer.Items.Add("");
        foreach (var l in Stammdaten.Sortiert(D.Lehrer)) lehrer.Items.Add(new Eintrag(l.Id, l.Kurzname));
        var klassen = Ui.Zeile(d, "Klasse(n):", new TextBox(), ref y, 120, 160);
        var fach = Ui.Zeile(d, "Fach:", new TextBox(), ref y, 120, 80);
        var raum = Ui.Zeile(d, "Raum:", new TextBox(), ref y, 120, 80);
        var text = Ui.Zeile(d, "Text:", new TextBox { Text = art == VertretungsArt.Klausur ? "Klausur" : "" }, ref y, 120, 270);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var datum = Datum;
        var lid = (lehrer.SelectedItem as Eintrag)?.Id;
        var kids = klassen.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(k => Finde(D.Klassen, k)).OfType<int>().ToList();
        var fid = Finde(D.Faecher, fach.Text);
        var rid = Finde(D.Raeume, raum.Text);
        var s = (int)stunde.Value;
        Aendern(Vertretungsplan.ArtText(art), AenderungsArt.Vertretung, dd =>
            dd.Vertretungen.Add(new Vertretung
            {
                Id = dd.NeueId(), Datum = datum, Stunde = s, Art = art, VertreterId = lid, LehrerId = art == VertretungsArt.Klausur ? null : lid, KlassenIds = kids,
                FachId = fid, VertretungsfachId = fid, VertretungsraumId = rid, Text = text.Text, Mehrarbeit = art == VertretungsArt.Sondereinsatz,
            }));
    }

    private static int? Finde<T>(IEnumerable<T> liste, string kurz) where T : Element =>
        string.IsNullOrWhiteSpace(kurz) ? null : liste.FirstOrDefault(e => e.Kurzname.Equals(kurz.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        var spalte = _g.Columns[e.ColumnIndex].Name;
        var wert = _g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var id = v.Id;
        int? gefunden = null;
        if (spalte is "Vertreter" or "NFach" or "NRaum" && wert.Length > 0 && wert != "???" && wert != "---")
        {
            gefunden = spalte switch { "Vertreter" => Finde(D.Lehrer, wert), "NFach" => Finde(D.Faecher, wert), _ => Finde(D.Raeume, wert) };
            if (gefunden is null)
            {
                Ui.Info($"'{wert}' ist nicht bekannt.");
                BeginInvoke(Neuaufbau);
                return;
            }
        }
        if (spalte == "Vertreter" && gefunden is int lid && Vertretungen.LehrerBelegt(D, Ix, lid, v.Datum, v.Stunde, null, v) is { } grund &&
            !Ui.Frage($"{wert} ist in dieser Stunde nicht frei ({grund}). Trotzdem eintragen?")) { BeginInvoke(Neuaufbau); return; }
        var plan = Vertretungen.Tagesplan(D, Ix, v.Datum);
        Aendern("Vertretung bearbeitet", AenderungsArt.Vertretung, d =>
        {
            var x = d.Vertretungen.First(y => y.Id == id);
            switch (spalte)
            {
                case "Vertreter":
                    x.VertreterId = gefunden;
                    if (gefunden is int l)
                    {
                        if (x.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung) x.Art = VertretungsArt.Vertretung;
                        x.VertretungsfachId ??= x.FachId;
                        x.Mehrarbeit = !Vertretungen.HatBereitschaft(d, new DatenIndex(d), l, x.Datum, x.Stunde, plan) && !Vertretungen.IstFreigesetzt(d, l, x.Datum, x.Stunde);
                    }
                    break;
                case "NFach": x.VertretungsfachId = gefunden; break;
                case "NRaum": x.VertretungsraumId = gefunden; break;
                case "Text": x.Text = wert; break;
            }
        });
        BeginInvoke(Neuaufbau);
    }

    private void PausenAnzeigen()
    {
        _pausen.Rows.Clear();
        var datum = Datum;
        foreach (var a in Pausenaufsichten.OffeneVertretungen(D, datum))
        {
            var r = _pausen.Rows.Add(Ix.K(ElementTyp.Gang, a.GangId), a.NachStunde, Ix.KLehrer(a.LehrerId), "???", string.Join(", ", Pausenaufsichten.Kandidaten(D, datum, a).Take(12).Select(l => l.Kurzname)));
            _pausen.Rows[r].Tag = a;
            _pausen.Rows[r].Cells[3].Style.BackColor = Ui.Rot;
        }
        foreach (var v in D.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht))
        {
            var r = _pausen.Rows.Add(Ix.K(ElementTyp.Gang, v.GangId), v.Stunde, Ix.KLehrer(v.LehrerId), Ix.KLehrer(v.VertreterId), "");
            _pausen.Rows[r].Tag = v;
        }
        _unten.TabPages[1].Text = $"Pausenaufsicht-Vertretungen ({Pausenaufsichten.OffeneVertretungen(D, datum).Count} offen)";
    }

    private void PausenVertreter()
    {
        var datum = Datum;
        Pausenaufsicht? a = _pausen.CurrentRow?.Tag as Pausenaufsicht;
        if (_pausen.CurrentRow?.Tag is Vertretung pv)
            a = new Pausenaufsicht { GangId = pv.GangId ?? 0, NachStunde = pv.Stunde, LehrerId = pv.LehrerId ?? 0, Tag = Kalender.TagIndex(D, datum) };
        if (a is null) return;
        var kand = Pausenaufsichten.Kandidaten(D, datum, a);
        using var d = new Dialog("Pausenaufsicht-Vertretung", 300, 360);
        d.Controls.Add(new Label { Text = $"{Ix.K(ElementTyp.Gang, a.GangId)}, Pause nach der {a.NachStunde}. Stunde:", Left = 12, Top = 8, AutoSize = true });
        var lb = new ListBox { Left = 12, Top = 30, Width = 276, Height = 280 };
        foreach (var l in kand) lb.Items.Add(new Eintrag(l.Id, $"{l.Kurzname} – {l.VollerName}  ({Vertretungen.Zaehler(D, l.Id, Kalender.Montag(datum), Kalender.Montag(datum).AddDays(6))} Vtr. diese Woche)"));
        lb.DoubleClick += (_, _) => d.DialogResult = DialogResult.OK;
        if (lb.Items.Count > 0) lb.SelectedIndex = 0;
        d.Controls.Add(lb);
        if (d.ShowDialog(this) != DialogResult.OK || lb.SelectedItem is not Eintrag e) return;
        var alt = _pausen.CurrentRow?.Tag as Vertretung;
        Aendern("Pausenaufsicht-Vertretung", AenderungsArt.Vertretung, dd =>
        {
            if (alt != null) dd.Vertretungen.RemoveAll(x => x.Id == alt.Id);
            dd.Vertretungen.Add(new Vertretung { Id = dd.NeueId(), Datum = datum, Stunde = a.NachStunde, Art = VertretungsArt.Pausenaufsicht, GangId = a.GangId, LehrerId = a.LehrerId, VertreterId = e.Id, Text = "Pausenaufsicht" });
        });
    }

    private void PausenLoeschen()
    {
        if (_pausen.CurrentRow?.Tag is not Vertretung v) return;
        Aendern("Pausenaufsicht-Vertretung löschen", AenderungsArt.Vertretung, d => d.Vertretungen.RemoveAll(x => x.Id == v.Id));
    }

    private void TagestextSpeichern()
    {
        var datum = Datum;
        var alle = _tagestext.Text;
        var lehrer = _lehrertext.Text;
        Aendern("Tagestext", AenderungsArt.Vertretung, d =>
        {
            d.Tagestexte.RemoveAll(t => t.Datum == datum);
            foreach (var z in alle.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0)) d.Tagestexte.Add(new Tagestext { Datum = datum, Text = z });
            foreach (var z in lehrer.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0)) d.Tagestexte.Add(new Tagestext { Datum = datum, Text = z, NurLehrer = true });
        });
    }

    /// <summary>Vertretungsplan der nächsten Unterrichtstage als HTML (Schüler- oder Lehrerversion) im Browser öffnen.</summary>
    public static void HtmlAusgabe(DateOnly ab, int tage, bool lehrer)
    {
        var d = AppZustand.D;
        var liste = new List<DateOnly>();
        var x = Kalender.IstUnterrichtstag(d, ab) ? ab : Kalender.NaechsterUnterrichtstag(d, ab);
        for (var i = 0; i < Math.Max(1, tage); i++)
        {
            liste.Add(x);
            x = Kalender.NaechsterUnterrichtstag(d, x);
        }
        var pfad = Path.Combine(Path.GetTempPath(), lehrer ? "UntPlan_Vertretungsplan_Lehrer.html" : "UntPlan_Vertretungsplan.html");
        File.WriteAllText(pfad, Export.VertretungsplanHtml(d, liste, lehrer));
        HauptFenster.Starten(pfad);
    }

    public void Drucken(bool vorschau)
    {
        var zeilen = Vertretungsplan.Zeilen(D, Datum).Select(z => new[] { z.Klassen, z.Stunde.ToString(), z.Lehrer, z.Vertreter, z.NeuesFach, z.NeuerRaum, z.Art, z.Text }).ToList();
        ListenDruck.Drucken(this, $"{D.Einstellungen.Vertretungsplantitel} – {Planungsdienst.TageLang[((int)Datum.DayOfWeek + 6) % 7]}, {Format.Datum(Datum)}",
            new[] { "Klasse(n)", "Std.", "(Lehrer)", "Vertreter", "Fach", "Raum", "Art", "Text" }, new[] { 90, 40, 60, 70, 55, 55, 90, 260 }, zeilen, vorschau);
    }

    private sealed record Eintrag(int Id, string Text)
    {
        public override string ToString() => Text;
    }
}

/// <summary>Vertretungsgewichtung: Bedeutung der Kriterien für den Vertretungsvorschlag (0–5).</summary>
public static class VertretungsgewichtungDialog
{
    public static bool Zeigen(IWin32Window owner)
    {
        var d = AppZustand.D;
        using var f = new Dialog("Vertretungsgewichtung", 520, 90 + VertretungsVorschlag.Kriterien.Length * 34);
        var regler = new Dictionary<string, TrackBar>();
        var y = 12;
        f.Controls.Add(new Label { Text = "unwichtig  ←  →  sehr wichtig", Left = 300, Top = y, AutoSize = true, ForeColor = Color.DimGray });
        y += 20;
        foreach (var (key, text) in VertretungsVorschlag.Kriterien)
        {
            var tb = new TrackBar { Minimum = 0, Maximum = 5, TickStyle = TickStyle.BottomRight, Height = 30, AutoSize = false, Value = d.Einstellungen.Vertretungsgewichtung.GetValueOrDefault(key, VertretungsVorschlag.StandardGewichtung[key]) };
            Ui.Zeile(f, text, tb, ref y, 290, 200);
            y += 4;
            regler[key] = tb;
        }
        var standard = new Button { Text = "Standard", Left = 12, Top = f.ClientSize.Height - 36, Width = 88, Height = 26 };
        standard.Click += (_, _) => { foreach (var (k, tb) in regler) tb.Value = VertretungsVorschlag.StandardGewichtung[k]; };
        f.Controls.Add(standard);
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        var werte = regler.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        AppZustand.Doc.Aendern("Vertretungsgewichtung", AenderungsArt.Einstellungen, x => x.Einstellungen.Vertretungsgewichtung = werte);
        return true;
    }
}

/// <summary>Abwesenheiten von Lehrern, Klassen und Räumen (inkl. Begleitlehrer bei Klassenabwesenheiten).</summary>
public sealed class AbwesenheitenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _zeitraum = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };

    public AbwesenheitenFenster()
    {
        Text = "Abwesenheiten";
        HilfeThema = "Abwesenheiten";
        Icon = SymbolIcon("abwesenheit");
        Size = new Size(920, 420);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Lehrer-Abwesenheit", "lehrer", () => Neu(AbwesenheitsArt.Lehrer), false));
        ts.Items.Add(Knopf("Neue Klassen-Abwesenheit", "klassen", () => Neu(AbwesenheitsArt.Klasse), false));
        ts.Items.Add(Knopf("Raumsperrung", "raeume", () => Neu(AbwesenheitsArt.Raum), false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        _zeitraum.Items.AddRange(new object[] { "ab heute", "diese Woche", "gesamtes Schuljahr" });
        _zeitraum.SelectedIndex = 0;
        _zeitraum.SelectedIndexChanged += (_, _) => Neuaufbau();
        ts.Items.Add(new ToolStripLabel("Zeitraum:"));
        ts.Items.Add(_zeitraum);
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Abw-Nr.", 55), ("Art", "Art", 60), ("E", "Name", 70), ("V", "von", 85), ("SV", "Std.", 40), ("B", "bis", 85), ("SB", "Std.", 40), ("G", "Grund", 90), ("Beg", "Begleitlehrer", 120), ("T", "Text", 250) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var (von, bis) = _zeitraum.SelectedIndex switch
        {
            0 => (heute, DateOnly.MaxValue),
            1 => (Kalender.Montag(heute), Kalender.Montag(heute).AddDays(6)),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
        foreach (var a in D.Abwesenheiten.Where(a => a.Bis >= von && a.Von <= bis).OrderBy(a => a.Von).ThenBy(a => a.Art))
        {
            var typ = a.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
            var r = _g.Rows.Add(a.Id, a.Art switch { AbwesenheitsArt.Lehrer => "Lehrer", AbwesenheitsArt.Klasse => "Klasse", _ => "Raum" }, Ix.K(typ, a.ElementId),
                Format.Datum(a.Von), a.StundeVon, Format.Datum(a.Bis), a.StundeBis >= 99 ? "" : a.StundeBis.ToString(), Vertretungen.GrundText(D, Ix, a),
                string.Join(", ", a.BegleitLehrerIds.Select(Ix.KLehrer)), a.Text);
            _g.Rows[r].Tag = a;
        }
    }

    private void Neu(AbwesenheitsArt art)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var a = new Abwesenheit { Art = art, Von = heute, Bis = heute };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(a, null);
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var a = new Abwesenheit
        {
            Id = alt.Id, Art = alt.Art, ElementId = alt.ElementId, Von = alt.Von, Bis = alt.Bis, StundeVon = alt.StundeVon, StundeBis = alt.StundeBis,
            GrundId = alt.GrundId, Text = alt.Text, BegleitLehrerIds = new List<int>(alt.BegleitLehrerIds),
        };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(a, alt);
    }

    private void Speichern(Abwesenheit a, Abwesenheit? alt)
    {
        Aendern(alt is null ? "Abwesenheit eingeben" : "Abwesenheit geändert", AenderungsArt.Vertretung, d =>
        {
            if (alt != null) d.Abwesenheiten.RemoveAll(x => x.Id == alt.Id);
            else a.Id = d.NeueId();
            d.Abwesenheiten.Add(a);
            var von = alt is null ? a.Von : (alt.Von < a.Von ? alt.Von : a.Von);
            var bis = alt is null ? a.Bis : (alt.Bis > a.Bis ? alt.Bis : a.Bis);
            foreach (var t in Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t))) Vertretungen.Aktualisieren(d, t);
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Abwesenheit>().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Abwesenheit(en) löschen? Offene Vertretungen dazu werden entfernt.")) return;
        var ids = liste.Select(a => a.Id).ToHashSet();
        Aendern("Abwesenheit löschen", AenderungsArt.Vertretung, d =>
        {
            d.Abwesenheiten.RemoveAll(a => ids.Contains(a.Id));
            foreach (var a in liste)
                foreach (var t in Kalender.Tage(a.Von, a.Bis).Where(t => Kalender.IstUnterrichtstag(d, t))) Vertretungen.Aktualisieren(d, t);
        });
    }
}

/// <summary>Eingabe einer Abwesenheit.</summary>
public static class AbwesenheitDialog
{
    public static bool Zeigen(IWin32Window owner, Abwesenheit a)
    {
        var d = AppZustand.D;
        var typ = a.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        using var f = new Dialog(a.Art switch { AbwesenheitsArt.Lehrer => "Lehrer-Abwesenheit", AbwesenheitsArt.Klasse => "Klassen-Abwesenheit", _ => "Raumsperrung" }, 440, a.Art == AbwesenheitsArt.Klasse ? 330 : 290);
        var y = 14;
        var element = Ui.Zeile(f, Ui.TypName(typ) + ":", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 130, 270);
        foreach (var e in Stammdaten.Sortiert(d.AlleElemente(typ))) element.Items.Add(e);
        element.Format += (_, e) => e.Value = e.ListItem is Element el ? Ui.ElementName(el) : "";
        element.SelectedItem = d.AlleElemente(typ).FirstOrDefault(e => e.Id == a.ElementId);
        if (element.SelectedIndex < 0 && element.Items.Count > 0) element.SelectedIndex = 0;
        var von = Ui.Zeile(f, "von:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.Von.ToDateTime(TimeOnly.MinValue) }, ref y, 130, 110);
        var sv = new NumericUpDown { Minimum = 1, Maximum = d.Zeitraster.StundenProTag, Value = Math.Clamp(a.StundeVon, 1, d.Zeitraster.StundenProTag), Left = 260, Top = von.Top, Width = 50 };
        f.Controls.Add(new Label { Text = "ab Std.", Left = 210, Top = von.Top + 3, AutoSize = true });
        f.Controls.Add(sv);
        var bis = Ui.Zeile(f, "bis:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.Bis.ToDateTime(TimeOnly.MinValue) }, ref y, 130, 110);
        var sb = new NumericUpDown { Minimum = 1, Maximum = d.Zeitraster.StundenProTag, Value = Math.Clamp(a.StundeBis, 1, d.Zeitraster.StundenProTag), Left = 260, Top = bis.Top, Width = 50 };
        f.Controls.Add(new Label { Text = "bis Std.", Left = 210, Top = bis.Top + 3, AutoSize = true });
        f.Controls.Add(sb);
        von.ValueChanged += (_, _) => { if (bis.Value < von.Value) bis.Value = von.Value; };
        var grund = Ui.Zeile(f, "Abwesenheitsgrund:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 130, 270);
        grund.Items.Add("");
        foreach (var g in d.Abwesenheitsgruende) grund.Items.Add(g);
        grund.Format += (_, e) => e.Value = e.ListItem is Abwesenheitsgrund g ? $"{g.Kurzname} – {g.Langname}" : "";
        grund.SelectedItem = d.Abwesenheitsgruende.FirstOrDefault(g => g.Id == a.GrundId);
        if (grund.SelectedIndex < 0) grund.SelectedIndex = 0;
        var text = Ui.Zeile(f, "Text:", new TextBox { Text = a.Text }, ref y, 130, 270);
        var begleit = new List<int>(a.BegleitLehrerIds);
        if (a.Art == AbwesenheitsArt.Klasse)
        {
            var bl = new Button { Text = "Begleitlehrer…", Width = 120, Height = 24 };
            var lbl = new Label { AutoSize = true, Left = 270, ForeColor = Color.DimGray };
            void Zeigen() => lbl.Text = string.Join(", ", begleit.Select(i => d.Lehrer.FirstOrDefault(l => l.Id == i)?.Kurzname));
            bl.Click += (_, _) =>
            {
                var w = AuswahlDialog.Mehrere(f, ElementTyp.Lehrer, "Begleitlehrer (gelten als abwesend)", begleit);
                if (w is null) return;
                begleit = w.Select(e => e.Id).ToList();
                Zeigen();
            };
            Ui.Zeile(f, "Begleitlehrer:", bl, ref y, 130, 120);
            lbl.Top = bl.Top + 4;
            f.Controls.Add(lbl);
            Zeigen();
        }
        if (f.ShowDialog(owner) != DialogResult.OK || element.SelectedItem is not Element el) return false;
        a.ElementId = el.Id;
        a.Von = DateOnly.FromDateTime(von.Value);
        a.Bis = DateOnly.FromDateTime(bis.Value);
        a.StundeVon = (int)sv.Value;
        a.StundeBis = (int)sb.Value >= d.Zeitraster.StundenProTag ? 99 : (int)sb.Value;
        a.GrundId = (grund.SelectedItem as Abwesenheitsgrund)?.Id;
        a.Text = text.Text;
        a.BegleitLehrerIds = begleit;
        return true;
    }
}

/// <summary>Modul Pausenaufsichten: Aufsichtsbereiche (Gänge) × Pausen × Tage, Optimierung und Lehrer-Übersicht.</summary>
public sealed class PausenaufsichtFenster : MdiFenster, IDruckbar
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _lehrer = new() { Dock = DockStyle.Fill };

    public PausenaufsichtFenster()
    {
        Text = "Pausenaufsichten";
        HilfeThema = "Pausenaufsichten";
        Icon = SymbolIcon("pausenaufsicht");
        Size = new Size(1000, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Pausenaufsichten optimieren", "optimierung", Optimieren, false));
        ts.Items.Add(Knopf("Alle Aufsichten löschen", "loeschen", () => { if (Ui.Frage("Alle Pausenaufsichten löschen?")) Aendern("Pausenaufsichten löschen", AenderungsArt.Stundenplan, d => d.Pausenaufsichten.Clear()); }, false));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(new ToolStripLabel("   Eingabe: Lehrer-Kurznamen (mehrere mit Komma). Aufsichtspausen im Zeitraster, Bereiche als Gänge anlegen.") { ForeColor = Color.DimGray });
        Ui.RasterGestalten(_g, true);
        _g.AllowUserToAddRows = false;
        _g.CellEndEdit += Bearbeitet;
        _g.CellFormatting += Formatieren;
        Ui.RasterGestalten(_lehrer, false);
        foreach (var (n, k, b) in new[] { ("L", "Lehrer", 70), ("Min", "Aufs. min", 65), ("Max", "Aufs. max", 65), ("Ist", "Ist", 45), ("Keine", "keine Aufs.", 75) })
            _lehrer.Columns[_lehrer.Columns.Add(n, k)].Width = b;
        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_g);
        split.Panel2.Controls.Add(_lehrer);
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(200, split.Width - 340);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Columns.Clear();
        _g.Rows.Clear();
        _g.Columns.Add("G", "Gang");
        _g.Columns.Add("P", "Pause");
        _g.Columns[0].ReadOnly = _g.Columns[1].ReadOnly = true;
        _g.Columns[1].Width = 120;
        for (var t = 0; t < D.Zeitraster.TageProWoche; t++) _g.Columns[_g.Columns.Add("T" + t, Planungsdienst.Tagname(D, t))].Width = 110;
        var pausen = Pausenaufsichten.AufsichtsPausen(D).ToList();
        foreach (var gang in Stammdaten.Sortiert(D.Gaenge))
            foreach (var p in pausen)
            {
                var werte = new object[2 + D.Zeitraster.TageProWoche];
                werte[0] = gang.Kurzname;
                werte[1] = $"{(string.IsNullOrEmpty(p.Bezeichnung) ? "Pause" : p.Bezeichnung)} n. {p.NachStunde}.";
                for (var t = 0; t < D.Zeitraster.TageProWoche; t++)
                    werte[2 + t] = string.Join(", ", D.Pausenaufsichten.Where(a => a.GangId == gang.Id && a.NachStunde == p.NachStunde && a.Tag == t).Select(a => Ix.KLehrer(a.LehrerId)));
                var r = _g.Rows.Add(werte);
                _g.Rows[r].Tag = (gang, p);
            }
        if (D.Gaenge.Count == 0 || pausen.Count == 0)
            _g.Rows.Add("–", D.Gaenge.Count == 0 ? "Keine Gänge angelegt" : "Keine Aufsichtspausen im Zeitraster");
        _lehrer.Rows.Clear();
        foreach (var l in Stammdaten.Sortiert(D.Lehrer))
        {
            var ist = D.Pausenaufsichten.Count(a => a.LehrerId == l.Id);
            var r = _lehrer.Rows.Add(l.Kurzname, l.AufsichtenMin, l.AufsichtenMax, ist, l.KeineAufsicht ? "ja" : "");
            if ((l.AufsichtenMax > 0 && ist > l.AufsichtenMax) || ist < l.AufsichtenMin || (l.KeineAufsicht && ist > 0)) _lehrer.Rows[r].DefaultCellStyle.ForeColor = Color.DarkRed;
        }
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 2 || _g.Rows[e.RowIndex].Tag is not ValueTuple<Gang, Pause> t) return;
        var (gang, p) = t;
        var tag = e.ColumnIndex - 2;
        var liste = D.Pausenaufsichten.Where(a => a.GangId == gang.Id && a.NachStunde == p.NachStunde && a.Tag == tag).ToList();
        if (liste.Count < gang.Bedarf) e.CellStyle!.BackColor = Color.FromArgb(255, 225, 225);
        else if (liste.Any(a => !Ix.Lehrer.TryGetValue(a.LehrerId, out var l) || !Pausenaufsichten.Moeglich(D, Ix, l, tag, p.NachStunde))) e.CellStyle!.BackColor = Ui.Violett;
        else e.CellStyle!.BackColor = Color.FromArgb(225, 245, 225);
    }

    private void Bearbeitet(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 2 || _g.Rows[e.RowIndex].Tag is not ValueTuple<Gang, Pause> t) return;
        var (gang, p) = t;
        var tag = e.ColumnIndex - 2;
        var namen = (_g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<int>();
        foreach (var n in namen)
        {
            var l = D.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (l is null) { Ui.Info($"Lehrer '{n}' ist nicht bekannt."); BeginInvoke(Neuaufbau); return; }
            ids.Add(l.Id);
        }
        Aendern("Pausenaufsicht", AenderungsArt.Stundenplan, d =>
        {
            d.Pausenaufsichten.RemoveAll(a => a.GangId == gang.Id && a.NachStunde == p.NachStunde && a.Tag == tag);
            foreach (var id in ids) d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang.Id, NachStunde = p.NachStunde, Tag = tag, LehrerId = id });
        });
    }

    private void Optimieren()
    {
        var loeschen = D.Pausenaufsichten.Count == 0 || Ui.Frage("Bestehende Aufsichten vorher löschen?\n(Nein = nur fehlende Aufsichten ergänzen)");
        var n = 0;
        using (new WarteCursor())
            Aendern("Pausenaufsichten optimieren", AenderungsArt.Stundenplan, d => n = Pausenaufsichten.Optimieren(d, loeschen));
        Ui.Info($"{n} Aufsichten wurden eingeteilt.", "Pausenaufsichten");
    }

    public void Drucken(bool vorschau)
    {
        var kopf = _g.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray();
        var zeilen = _g.Rows.Cast<DataGridViewRow>().Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "").ToArray()).ToList();
        ListenDruck.Drucken(this, "Pausenaufsichten", kopf, kopf.Select((_, i) => i < 2 ? 90 : 110).ToArray(), zeilen, vorschau);
    }
}

/// <summary>Vertretungsstatistik: Lehrer-Abrechnung (Vertretungen, Mehrarbeit nach NRW-Regel, Entfälle, Saldo) und Ausfallstatistik.</summary>
public sealed class StatistikFenster : MdiFenster, IDruckbar
{
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly Label _summe = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), BackColor = Ui.Kopf };

    public StatistikFenster()
    {
        Text = "Vertretungsstatistik";
        HilfeThema = "Statistik";
        Icon = SymbolIcon("statistik");
        Size = new Size(1000, 520);
        Positionieren();
        var heute = DateTime.Today;
        _von.Value = new DateTime(heute.Year, heute.Month, 1);
        _bis.Value = heute;
        var ts = Werkzeugleiste();
        ts.Items.Add(new ToolStripLabel("Zeitraum von"));
        ts.Items.Add(new ToolStripControlHost(_von));
        ts.Items.Add(new ToolStripLabel("bis"));
        ts.Items.Add(new ToolStripControlHost(_bis));
        ts.Items.Add(Knopf("Aktueller Monat", "kalender", () => { _von.Value = new DateTime(heute.Year, heute.Month, 1); _bis.Value = _von.Value.AddMonths(1).AddDays(-1); }, false));
        ts.Items.Add(Knopf("Vormonat", "zurueck", () => { _von.Value = new DateTime(heute.Year, heute.Month, 1).AddMonths(-1); _bis.Value = _von.Value.AddMonths(1).AddDays(-1); }, false));
        ts.Items.Add(Knopf("Schuljahr", "kalender", () => { _von.Value = D.Einstellungen.SchuljahrVon.ToDateTime(TimeOnly.MinValue); _bis.Value = D.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue); }, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Export CSV", "export", CsvExport, false));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        _von.ValueChanged += (_, _) => Neuaufbau();
        _bis.ValueChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, false);
        foreach (var (n, k, b) in Kopf) _g.Columns[_g.Columns.Add(n, k)].Width = b;
        Controls.Add(_g);
        Controls.Add(_summe);
        Controls.Add(ts);
        Neuaufbau();
    }

    private static readonly (string, string, int)[] Kopf =
    {
        ("L", "Lehrer", 70), ("N", "Name", 150), ("V", "Vertretungen", 85), ("B", "Betreuungen", 80), ("S", "Sondereins.", 80), ("A", "Pausenaufs.", 80),
        ("M", "Mehrarbeit", 75), ("E", "Entfall", 60), ("F", "Freisetzung", 75), ("Sa", "Saldo", 55), ("Vg", "vergütungsfähig (NRW)", 140),
    };

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private List<string[]> Zeilen() => Statistik.Abrechnung(D, DateOnly.FromDateTime(_von.Value), DateOnly.FromDateTime(_bis.Value))
        .Select(z => new[] { z.Lehrer.Kurzname, z.Lehrer.VollerName, z.Vertretungen.ToString(), z.Betreuungen.ToString(), z.Sondereinsaetze.ToString(), z.Aufsichten.ToString(),
            z.Mehrarbeit.ToString(), z.Entfaelle.ToString(), z.Freisetzungen.ToString(), z.Saldo.ToString("+0;-0;0"), z.Verguetungsfaehig.ToString() }).ToList();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var z in Zeilen())
        {
            var r = _g.Rows.Add(z);
            if (z[10] != "0") _g.Rows[r].Cells[10].Style.BackColor = Color.FromArgb(255, 240, 200);
        }
        var s = Statistik.Zusammenfassung(D, DateOnly.FromDateTime(_von.Value), DateOnly.FromDateTime(_bis.Value));
        _summe.Text = $"Ausfallstatistik: {s.Gesamt} Einträge – vertreten {s.Vertreten}, Entfall {s.Entfall}, Freisetzung {s.Freisetzung}, Raumvertretung {s.Raum}, Verlegung {s.Verlegung}, offen {s.Offen}.\n" +
            $"Mehrarbeit (NRW): vergütungsfähig, wenn im Kalendermonat mehr als {D.Einstellungen.Mehrarbeitsgrenze} Stunden (abzüglich Minderstunden) geleistet wurden – dann alle Stunden.";
    }

    private void CsvExport()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV-Datei|*.csv", FileName = $"Vertretungsstatistik_{_von.Value:yyyyMMdd}-{_bis.Value:yyyyMMdd}.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var zeilen = new List<string[]> { Kopf.Select(k => k.Item2).ToArray() };
        zeilen.AddRange(Zeilen());
        File.WriteAllText(sfd.FileName, Csv.Schreiben(zeilen, bom: false), new System.Text.UTF8Encoding(true));
    }

    public void Drucken(bool vorschau) =>
        ListenDruck.Drucken(this, $"Vertretungsstatistik {_von.Value:dd.MM.yyyy} – {_bis.Value:dd.MM.yyyy}", Kopf.Select(k => k.Item2).ToArray(), Kopf.Select(k => k.Item3).ToArray(), Zeilen(), vorschau);
}

/// <summary>Monitoransicht des Vertretungsplans (Vollbild, automatischer Seitenwechsel und Aktualisierung). ESC beendet.</summary>
public sealed class MonitorFenster : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };
    private int _seite;
    private int _takt;

    public MonitorFenster()
    {
        Text = "UntPlan – Vertretungsplan (Monitor)";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(11, 26, 51);
        KeyPreview = true;
        DoubleBuffered = true;
        Icon = Icon.FromHandle(Symbole.Get("monitor", 16).GetHicon());
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode is Keys.Right or Keys.Space) { _seite++; Invalidate(); }
            if (e.KeyCode == Keys.Left) { _seite = Math.Max(0, _seite - 1); Invalidate(); }
        };
        Click += (_, _) => { _seite++; Invalidate(); };
        _timer.Tick += (_, _) => { _seite++; _takt++; Invalidate(); };
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var d = AppZustand.D;
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var tage = new[] { Kalender.IstUnterrichtstag(d, heute) ? heute : Kalender.NaechsterUnterrichtstag(d, heute) };
        tage = tage.Append(Kalender.NaechsterUnterrichtstag(d, tage[0])).ToArray();
        using var titel = new Font("Segoe UI", 26f, FontStyle.Bold);
        using var kopf = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var schrift = new Font("Segoe UI", 15f);
        var spalten = new[] { ("Klasse(n)", 0.14f), ("Std.", 0.06f), ("Vertreter", 0.12f), ("Fach", 0.1f), ("Raum", 0.1f), ("Art", 0.15f), ("Text", 0.33f) };
        var halbe = (Width - 60) / 2;
        for (var i = 0; i < tage.Length; i++)
        {
            var x0 = 20 + i * (halbe + 20);
            var datum = tage[i];
            TextRenderer.DrawText(g, $"{Planungsdienst.TageLang[((int)datum.DayOfWeek + 6) % 7]}, {Format.Datum(datum)}", titel, new Point(x0, 16), Color.White);
            var texte = string.Join("  ·  ", d.Tagestexte.Where(t => t.Datum == datum && !t.NurLehrer).Select(t => t.Text));
            TextRenderer.DrawText(g, texte, schrift, new Rectangle(x0, 64, halbe, 30), Color.FromArgb(255, 220, 120), TextFormatFlags.EndEllipsis);
            var y = 100;
            var x = x0;
            using (var b = new SolidBrush(Color.FromArgb(29, 58, 102))) g.FillRectangle(b, x0, y, halbe, 34);
            foreach (var (k, anteil) in spalten)
            {
                TextRenderer.DrawText(g, k, kopf, new Rectangle(x + 4, y, (int)(halbe * anteil), 34), Color.White, TextFormatFlags.VerticalCenter);
                x += (int)(halbe * anteil);
            }
            y += 38;
            var zeilen = Vertretungsplan.Zeilen(d, datum).Where(z => z.Art != "Pausenaufsicht").ToList();
            var proSeite = Math.Max(1, (Height - y - 50) / 32);
            var seiten = Math.Max(1, (zeilen.Count + proSeite - 1) / proSeite);
            var s = _seite % seiten;
            var n = 0;
            foreach (var z in zeilen.Skip(s * proSeite).Take(proSeite))
            {
                if (n++ % 2 == 1) using (var b = new SolidBrush(Color.FromArgb(18, 38, 70))) g.FillRectangle(b, x0, y, halbe, 32);
                x = x0;
                var entfall = z.Art is "Entfall" or "Freisetzung";
                var werte = new[] { z.Klassen, z.Stunde.ToString(), z.Vertreter, z.NeuesFach, z.NeuerRaum, z.Art, z.Text };
                for (var c = 0; c < werte.Length; c++)
                {
                    TextRenderer.DrawText(g, werte[c], schrift, new Rectangle(x + 4, y, (int)(halbe * spalten[c].Item2) - 6, 32), entfall ? Color.FromArgb(255, 154, 154) : Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += (int)(halbe * spalten[c].Item2);
                }
                y += 32;
            }
            if (zeilen.Count == 0) TextRenderer.DrawText(g, "Keine Vertretungen.", schrift, new Point(x0 + 4, y + 6), Color.LightGray);
            if (seiten > 1) TextRenderer.DrawText(g, $"Seite {s + 1}/{seiten}", schrift, new Point(x0, Height - 44), Color.LightGray);
        }
        TextRenderer.DrawText(g, $"{d.Einstellungen.Schulname}   ·   Stand {DateTime.Now:HH:mm}   ·   ESC = Beenden", schrift, new Rectangle(0, Height - 44, Width - 20, 40), Color.LightGray, TextFormatFlags.Right);
    }
}
