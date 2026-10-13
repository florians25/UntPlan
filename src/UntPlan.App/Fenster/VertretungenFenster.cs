using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vertretungsfenster (Vertretungsmodus): alle Vertretungen des Planungszeitraums mit Ansichten Klasse/Lehrer/Druck,
/// Filter "Offene Vertretungen" und "Kollisionen", direkter Eingabe des Vertreters ("+" = Vertr. o. Lehrkraft, "-" = Entfall)
/// und gleichzeitiger Bearbeitung mehrerer markierter Zeilen.
/// </summary>
public sealed class VertretungenFenster : MdiFenster, IDruckbar
{
    private static readonly string[] Ansichten = { "Klasse - Standard", "Lehrer - Standard", "Druck - Klasse", "Druck - Lehrer", "Pausenaufsichten" };

    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd.MM.yyyy", Width = 118 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd.MM.yyyy", Width = 118 };
    private readonly CheckBox _offen = new() { Text = "Offene Vertretungen", AutoSize = true };
    private readonly CheckBox _kollisionen = new() { Text = "Kollisionen", AutoSize = true };
    private readonly TextBox _element = new() { Width = 70 };
    private readonly ComboBox _ansicht = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _nr = new() { Width = 60 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(12, 5, 0, 0) };
    private readonly Dictionary<DateOnly, Tagesplan> _plaene = new();
    private bool _laedt;
    private bool _sync = true;

    public VertretungenFenster()
    {
        Text = "Vertretungen / Klasse – Standard";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("vertretung");
        Size = new Size(1180, 520);
        Positionieren();
        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _ansicht.Items.AddRange(Ansichten);
        _ansicht.SelectedIndex = Math.Max(0, Array.IndexOf(Ansichten, AppZustand.Einstellungen.Vertretungsansicht));

        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Vertretungsvorschlag", "vertretung", VorschlagOeffnen, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Entfall (-)", "entfall", Entfall, false));
        ts.Items.Add(Knopf("Vertr. o. Lehrkraft (+)", "planen", OhneLehrkraft, false));
        ts.Items.Add(Knopf("Vertretung teilen", "tausch", Teilen));
        ts.Items.Add(Knopf("Raum zuordnen", "raumzuordnen", RaumZuordnen));
        ts.Items.Add(Knopf("Sondereinsatz", "neu", () => SondereinsatzDialog.Neu(this, AppZustand.Datum, Aktuelle().FirstOrDefault()?.Stunde ?? 1, Array.Empty<int>(), Array.Empty<int>())));
        ts.Items.Add(Knopf("Als Vormerkung beiseitelegen", "zurueck", AlsVormerkung));
        ts.Items.Add(Knopf("Zurücksetzen (wieder offen)", "rueckgaengig", Zuruecksetzen));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        var auto = new ToolStripDropDownButton("Automatik", Symbole.Get("optimierung", 16));
        auto.DropDownItems.Add("Bester Vorschlag", null, (_, _) => Automatik(AutomatikMethode.BesterVorschlag));
        auto.DropDownItems.Add("Bereitschaft und Freigesetzte zuerst", null, (_, _) => Automatik(AutomatikMethode.BereitschaftUndFreigesetzteZuerst));
        auto.DropDownItems.Add("Vorziehungen zuerst", null, (_, _) => Automatik(AutomatikMethode.VorziehungenZuerst));
        auto.DropDownItems.Add("Nur Pausenaufsichten", null, (_, _) => Automatik(AutomatikMethode.Pausenaufsichten));
        ts.Items.Add(auto);
        ts.Items.Add(Knopf("Neu berechnen (Absenzen, Veranstaltungen, Klausuren)", "aktualisieren", NeuBerechnen));
        ts.Items.Add(Knopf("Nachrichten zum Tag", "kommentar", () => TagestextDialog.Zeigen(this, AppZustand.Datum)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Vertretungsplan (HTML)", "html", () => HtmlAusgabe(DateOnly.FromDateTime(_von.Value), Math.Max(1, (int)(_bis.Value - _von.Value).TotalDays + 1), _ansicht.SelectedIndex is 1 or 3)));
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Einstellungen", "einstellungen", () => VertretungsEinstellungenDialog.Zeigen(this)));

        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 3, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        filter.Controls.Add(new Label { Text = "Element:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        filter.Controls.Add(_element);
        filter.Controls.Add(new Label { Text = "  Von", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        filter.Controls.Add(_von);
        filter.Controls.Add(new Label { Text = "Bis", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        filter.Controls.Add(_bis);
        filter.Controls.Add(_offen);
        filter.Controls.Add(_kollisionen);
        _offen.Padding = _kollisionen.Padding = new Padding(8, 3, 0, 0);

        var unten = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(4, 3, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        unten.Controls.Add(new Label { Text = "Vtr-Nr.:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        unten.Controls.Add(_nr);
        unten.Controls.Add(new Label { Text = "   Ansicht:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        unten.Controls.Add(_ansicht);
        unten.Controls.Add(_status);

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        _g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        foreach (var (name, kopf, breite, lesbar) in Spalten)
        {
            DataGridViewColumn c = name switch
            {
                "Vertreter" => Steuerelemente.SuchKombi.Spalte(name, kopf, breite, new[] { "+", "-", "???", "---" }.Concat(Stammdaten.Sortiert(D.Lehrer).Select(l => l.Kurzname))),
                "NFach" => Steuerelemente.SuchKombi.Spalte(name, kopf, breite, Stammdaten.Sortiert(D.Faecher).Select(f => f.Kurzname)),
                _ => new DataGridViewTextBoxColumn { Name = name, HeaderText = kopf },
            };
            _g.Columns.Add(c);
            c.Width = breite;
            c.ReadOnly = lesbar;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (!lesbar) c.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 236);
        }
        _g.Columns["Text"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        // mehrere Klassen untereinander (Zeilenhöhe passt sich an)
        _g.Columns["Klassen"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.Columns["Text"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellFormatting += Formatieren;
        _g.EditingControlShowing += EditorZeigen;
        Steuerelemente.SuchKombi.Aktivieren(_g, VertreterStil);
        _g.SelectionChanged += (_, _) => { if (!_laedt) Gewaehlt(); };
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _g.Columns[e.ColumnIndex].ReadOnly) VorschlagOeffnen(); };
        _g.KeyPress += TastenDruck;
        _g.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !_g.IsCurrentCellInEditMode) { Zuruecksetzen(); e.Handled = true; }
            if (e.KeyCode == Keys.Enter && !_g.IsCurrentCellInEditMode) { VorschlagOeffnen(); e.Handled = true; }
        };
        _g.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _g.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && !_g.Rows[hit.RowIndex].Selected)
            {
                _g.ClearSelection();
                _g.Rows[hit.RowIndex].Selected = true;
                _g.CurrentCell = _g.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
            }
            Kontextmenue(e.Location);
        };

        _von.ValueChanged += (_, _) => { if (_bis.Value < _von.Value) _bis.Value = _von.Value; ZeitraumGeaendert(); };
        _bis.ValueChanged += (_, _) => { if (_bis.Value < _von.Value) _von.Value = _bis.Value; ZeitraumGeaendert(); };
        _offen.CheckedChanged += (_, _) => Neuaufbau();
        _kollisionen.CheckedChanged += (_, _) => Neuaufbau();
        _element.TextChanged += (_, _) => Neuaufbau();
        _ansicht.SelectedIndexChanged += (_, _) =>
        {
            AppZustand.Einstellungen.Vertretungsansicht = Ansichten[_ansicht.SelectedIndex];
            AppZustand.Einstellungen.Speichern();
            Text = "Vertretungen / " + Ansichten[_ansicht.SelectedIndex].Replace(" - ", " – ");
            Neuaufbau();
        };
        _nr.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !int.TryParse(_nr.Text, out var nr)) return;
            e.SuppressKeyPress = true;
            Springen(nr);
        };

        Controls.Add(_g);
        Controls.Add(filter);
        Controls.Add(unten);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.VertretungGewaehlt += FremdGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.VertretungGewaehlt -= FremdGewaehlt;
        };
        Shown += (_, _) => ZeitraumGeaendert();
    }

    private static readonly (string, string, int, bool)[] Spalten =
    {
        ("Nr", "Vtr-Nr.", 52, true), ("Art", "Art", 118, true), ("Datum", "Datum", 78, true), ("Std", "Stunde", 46, true), ("Klassen", "Klasse(n)", 80, true),
        ("Lehrer", "(Lehrer)", 62, true), ("Vertreter", "Vertreter", 66, false), ("Fach", "(Fach)", 52, true), ("NFach", "Fach", 52, false),
        ("Raum", "(Raum)", 54, true), ("NRaum", "Raum", 54, false), ("Von", "Vertr. von", 92, true), ("Nach", "(Le.) nach", 92, true),
        ("Grund", "Absenzgrund", 80, true), ("Kz", "Stat.", 38, false), ("Text", "Vertretungstext", 200, false), ("Mehr", "Mehrarb.", 54, true),
    };

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        _plaene.Clear();
        Neuaufbau();
    }

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private void DatumGewechselt(DateOnly datum)
    {
        if (!_sync || IsDisposed) return;
        _laedt = true;
        _von.Value = _bis.Value = datum.ToDateTime(TimeOnly.MinValue);
        _laedt = false;
        ZeitraumGeaendert();
    }

    private void FremdGewaehlt(Vertretung? v)
    {
        if (v is null || _laedt || IsDisposed || ContainsFocus) return;
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Vertretung x && x.Id == v.Id)
            {
                _laedt = true;
                _g.ClearSelection();
                r.Selected = true;
                _g.CurrentCell = r.Cells[0];
                _laedt = false;
                return;
            }
    }

    /// <summary>Beim Zeitraumwechsel offene Vertretungen aus Absenzen, Veranstaltungen und Klausuren erzeugen (nur wenn sich etwas ändert).</summary>
    private void ZeitraumGeaendert()
    {
        if (_laedt) return;
        var von = Von;
        var bis = Bis;
        if (bis.DayNumber - von.DayNumber <= 31)
        {
            var probe = Dokument.Klonen(D);
            Vertretungen.Aktualisieren(probe, von, bis);
            if (Signatur(probe, von, bis) != Signatur(D, von, bis))
                Aendern($"Vertretungen {Format.Datum(von)}{(von == bis ? "" : " – " + Format.Datum(bis))} aktualisiert", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, von, bis));
        }
        if (von == bis && AppZustand.Datum != von)
        {
            _sync = false;
            AppZustand.DatumSetzen(von);
            _sync = true;
        }
        _plaene.Clear();
        Neuaufbau();
    }

    private static string Signatur(Schuldatei d, DateOnly von, DateOnly bis) =>
        string.Join(";", d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).Select(v => $"{v.UnterrichtId}/{v.ZeilenIndex}/{v.Datum}/{v.Stunde}/{v.Art}/{v.VertreterId}/{v.GangId}").OrderBy(s => s));

    private Tagesplan Plan(DateOnly datum)
    {
        if (!_plaene.TryGetValue(datum, out var tp)) _plaene[datum] = tp = Tagesplan.Berechnen(D, datum);
        return tp;
    }

    /// <summary>Kollision: Vertreter ist zur Stunde anderweitig belegt bzw. abwesend, Raum doppelt belegt.</summary>
    private string? Kollision(Vertretung v)
    {
        if (v.IstEntfall) return null;
        if (v.VertreterId is int l && Vertretungen.LehrerBelegt(D, Plan(v.Datum), l, v.Stunde, v) is { } g && v.Art != VertretungsArt.TrotzAbsenz && !v.Mitbetreuung) return g;
        var raum = v.VertretungsraumId ?? v.RaumId;
        if (raum is int r && v.Art != VertretungsArt.Pausenaufsicht &&
            Plan(v.Datum).Stunde(v.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(r)) > 1) return $"Raum {Ix.KRaum(r)} doppelt belegt";
        return null;
    }

    private bool Passt(Vertretung v)
    {
        var pausen = _ansicht.SelectedIndex == 4;
        if (pausen != (v.Art == VertretungsArt.Pausenaufsicht)) return false;
        if (_offen.Checked && !v.Offen) return false;
        if (_kollisionen.Checked && Kollision(v) is null) return false;
        var f = _element.Text.Trim();
        if (f.Length > 0)
        {
            var lehrer = _ansicht.SelectedIndex is 1 or 3;
            var treffer = lehrer
                ? new[] { v.LehrerId, v.VertreterId }.Any(i => i is int x && Ix.KLehrer(x).StartsWith(f, StringComparison.OrdinalIgnoreCase))
                : v.NeueKlassenIds.Concat(v.KlassenIds).Any(k => Ix.KKlassen(new[] { k }).StartsWith(f, StringComparison.OrdinalIgnoreCase));
            if (!treffer) return false;
        }
        return true;
    }

    private void Neuaufbau()
    {
        if (IsDisposed) return;
        var auswahl = Aktuelle().Select(v => v.Id).ToHashSet();
        var aktuell = (_g.CurrentRow?.Tag as Vertretung)?.Id;
        _laedt = true;
        _g.Rows.Clear();
        var lehrerSicht = _ansicht.SelectedIndex is 1 or 3;
        var druck = _ansicht.SelectedIndex is 2 or 3;
        var liste = Vertretungen.ImZeitraum(D, Von, Bis).Where(Passt);
        if (druck) liste = liste.Where(v => !v.NichtDrucken);
        var sortiert = lehrerSicht
            ? liste.OrderBy(v => v.Datum).ThenBy(v => Ix.KLehrer(v.VertreterId ?? v.LehrerId), StringComparer.Create(Format.De, true)).ThenBy(v => v.Stunde)
            : liste.OrderBy(v => v.Datum).ThenBy(v => Ix.KKlassen(v.NeueKlassenIds), StringComparer.Create(Format.De, true)).ThenBy(v => v.Stunde);
        foreach (var v in sortiert)
        {
            var r = _g.Rows.Add(Werte(v));
            _g.Rows[r].Tag = v;
            if (auswahl.Contains(v.Id)) _g.Rows[r].Selected = true;
            if (v.Id == aktuell) _g.CurrentCell = _g.Rows[r].Cells[0];
        }
        foreach (var name in new[] { "Datum" }) _g.Columns[name]!.Visible = Von != Bis;
        _g.Columns["Von"]!.Visible = _g.Columns["Nach"]!.Visible = !druck || _g.Rows.Cast<DataGridViewRow>().Any(r => r.Tag is Vertretung { VonDatum: not null } or Vertretung { NachDatum: not null });
        _laedt = false;
        var alle = Vertretungen.ImZeitraum(D, Von, Bis);
        var offen = alle.Count(v => v.Offen);
        var koll = alle.Count(v => Kollision(v) != null);
        _status.Text = $"{_g.Rows.Count} Zeilen · {offen} offen · {koll} Kollisionen";
        _status.ForeColor = offen > 0 || koll > 0 ? Color.Firebrick : Color.DarkGreen;
        Gewaehlt();
    }

    private object?[] Werte(Vertretung v)
    {
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var vertreter = v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten ? "+"
            : v.IstEntfall || v.Art == VertretungsArt.Vormerkung ? "---" : v.VertreterId is int l ? Ix.KLehrer(l) : v.Offen ? "???" : "";
        var raumNeu = v.IstEntfall ? "" : v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId)
            : string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => Ix.KRaum(x)));
        string Pos(DateOnly? d, int? s) => d is DateOnly dd ? $"{Planungsdienst.TageKurz[((int)dd.DayOfWeek + 6) % 7]}-{dd:dd.MM.} / {s}" : "";
        return new object?[]
        {
            v.Id, Vertretungsplan.ArtText(D, v.Art), $"{tag} {v.Datum:dd.MM.}", Vertretungsplan.StundeText(v), Ix.KKlassen(v.NeueKlassenIds).Replace(",", ", "),
            Ix.KLehrer(v.LehrerId), vertreter, Ix.KFach(v.FachId), v.IstEntfall ? "" : Ix.KFach(v.VertretungsfachId ?? v.FachId),
            v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId) : Ix.KRaum(v.RaumId), raumNeu,
            Pos(v.VonDatum, v.VonStunde), Pos(v.NachDatum, v.NachStunde), v.Grund, v.Statistik, v.Text, v.Mehrarbeit ? "x" : "",
        };
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        var name = _g.Columns[e.ColumnIndex].Name;
        if (D.Vertretungseinstellungen.Farben.TryGetValue(v.Art.ToString(), out var farbe) && name == "Art") e.CellStyle.BackColor = Ui.Farbe(farbe, Color.White);
        // offene Vertretung: nur rotes "???" beim Vertreter
        if (v.Offen && name == "Vertreter") e.CellStyle.ForeColor = Color.Red;
        else if (v.IstEntfall)
        {
            e.CellStyle.ForeColor = Color.DimGray;
            if (name is "Vertreter" or "NFach" or "NRaum") e.CellStyle.BackColor = Color.FromArgb(236, 236, 236);
        }
        else if (name == "Vertreter" && v.VertreterId.HasValue && v.VertreterId != v.LehrerId) e.CellStyle.ForeColor = Color.FromArgb(0, 90, 160);
        if (name == "NRaum" && v.VertretungsraumId.HasValue && v.VertretungsraumId != v.RaumId) e.CellStyle.ForeColor = Color.FromArgb(0, 90, 160);
        if (name == "NFach" && v.VertretungsfachId.HasValue && v.VertretungsfachId != v.FachId) e.CellStyle.ForeColor = Color.FromArgb(0, 90, 160);
        if (Kollision(v) is { } k)
        {
            if (name is "Vertreter" or "NRaum") e.CellStyle.BackColor = Color.FromArgb(255, 190, 120);
            _g.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = "Kollision: " + k;
        }
        if (name == "Nr" && v.Bearbeitet) e.CellStyle.Font = Ui.SchriftFett;
    }

    /// <summary>Auswahlliste "Vertreter": belegte bzw. abwesende Lehrer rot, Bereitschaft/freigesetzt grün, Langname als Zusatz.</summary>
    private Steuerelemente.ListenStil? VertreterStil(DataGridViewCell zelle, string text)
    {
        if (zelle.OwningColumn.Name != "Vertreter") return D.Faecher.FirstOrDefault(f => f.Kurzname == text) is { } fa ? new(Color.White, Color.Black, fa.Langname) : null;
        if (zelle.OwningRow.Tag is not Vertretung v) return null;
        var l = D.Lehrer.FirstOrDefault(x => x.Kurzname == text);
        if (l is null) return text switch { "+" => new(Color.White, Color.Black, "Vertr. o. Lehrkraft"), "-" => new(Color.White, Color.Black, "Entfall"), _ => null };
        var tp = Plan(v.Datum);
        if (Vertretungen.LehrerBelegt(D, tp, l.Id, v.Stunde, v) is { } grund) return new(Color.FromArgb(225, 40, 40), Color.White, $"{l.VollerName} – {grund}");
        if (Vertretungen.HatBereitschaft(D, tp, l.Id, v.Stunde) || Vertretungen.IstFreigesetzt(D, tp, l.Id, v.Stunde)) return new(Color.FromArgb(200, 240, 200), Color.Black, l.VollerName);
        return new(Color.White, Color.Black, l.VollerName);
    }

    private void EditorZeigen(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox t) return;
        var name = _g.CurrentCell?.OwningColumn.Name;
        var typ = name switch { "Vertreter" => ElementTyp.Lehrer, "NFach" => ElementTyp.Fach, "NRaum" => ElementTyp.Raum, _ => (ElementTyp?)null };
        if (typ is null)
        {
            t.AutoCompleteMode = AutoCompleteMode.None;
            return;
        }
        var quelle = new AutoCompleteStringCollection();
        quelle.AddRange(D.AlleElemente(typ.Value).Where(x => !x.Ignorieren).Select(x => x.Kurzname).ToArray());
        if (typ == ElementTyp.Lehrer) quelle.AddRange(new[] { "+", "-" });
        t.AutoCompleteCustomSource = quelle;
        t.AutoCompleteSource = AutoCompleteSource.CustomSource;
        t.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
    }

    /// <summary>"+" und "-" ohne Bearbeitungsmodus wirken auf alle markierten Zeilen.</summary>
    private void TastenDruck(object? sender, KeyPressEventArgs e)
    {
        if (_g.IsCurrentCellInEditMode) return;
        if (e.KeyChar == '+') { OhneLehrkraft(); e.Handled = true; }
        else if (e.KeyChar == '-') { Entfall(); e.Handled = true; }
    }

    private List<Vertretung> Aktuelle()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Vertretung>().ToList();
        if (liste.Count == 0 && _g.CurrentRow?.Tag is Vertretung v) liste.Add(v);
        return liste.OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList();
    }

    private void Gewaehlt()
    {
        var liste = Aktuelle();
        var aktiv = _g.CurrentRow?.Tag as Vertretung ?? liste.FirstOrDefault();
        _laedt = true;
        AppZustand.VertretungWaehlen(aktiv, liste.Select(v => v.Id));
        _laedt = false;
    }

    private void Springen(int nr)
    {
        var v = D.Vertretungen.FirstOrDefault(x => x.Id == nr);
        if (v is null) { Ui.Info($"Die Vertretung Nr. {nr} gibt es nicht."); return; }
        if (v.Datum < Von || v.Datum > Bis)
        {
            _laedt = true;
            _von.Value = _bis.Value = v.Datum.ToDateTime(TimeOnly.MinValue);
            _laedt = false;
            ZeitraumGeaendert();
        }
        _offen.Checked = _kollisionen.Checked = false;
        _element.Text = "";
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Vertretung x && x.Id == nr)
            {
                _g.ClearSelection();
                r.Selected = true;
                _g.CurrentCell = r.Cells[0];
            }
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Vertretung v) return;
        var spalte = _g.Columns[e.ColumnIndex].Name;
        var wert = _g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var id = v.Id;
        switch (spalte)
        {
            case "Vertreter":
                if (wert == "+") { OhneLehrkraft(); return; }
                if (wert is "-" or "---") { Entfall(); return; }
                if (wert is "" or "???") { Ausfuehren("Vertreter entfernt", new[] { id }, (d, x) => Vertretungen.VertreterSetzen(d, x, null, false, AppZustand.Benutzer)); return; }
                var l = D.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (l is null) { Ui.Info($"Der Lehrer '{wert}' ist nicht bekannt."); BeginInvoke(Neuaufbau); return; }
                VertreterSetzen(l.Id, false);
                return;
            case "NFach":
                var f = wert.Length == 0 ? null : D.Faecher.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (wert.Length > 0 && f is null) { Ui.Info($"Das Fach '{wert}' ist nicht bekannt."); BeginInvoke(Neuaufbau); return; }
                Ausfuehren("Vertretungsfach", new[] { id }, (d, x) => { x.VertretungsfachId = f?.Id; x.Bearbeitet = true; });
                return;
            case "NRaum":
                var r = wert.Length == 0 ? null : D.Raeume.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                if (wert.Length > 0 && r is null) { Ui.Info($"Der Raum '{wert}' ist nicht bekannt."); BeginInvoke(Neuaufbau); return; }
                Ausfuehren("Vertretungsraum", new[] { id }, (d, x) =>
                {
                    x.VertretungsraumId = r?.Id;
                    x.ZusatzraumIds.Clear();
                    if (x.Art == VertretungsArt.Vertretung && x.VertreterId == x.LehrerId) x.Art = VertretungsArt.Raumvertretung;
                    x.Bearbeitet = true;
                });
                return;
            case "Kz":
                Ausfuehren("Statistik-Kennzeichen", new[] { id }, (d, x) => x.Statistik = wert);
                return;
            case "Text":
                Ausfuehren("Vertretungstext", Aktuelle().Select(x => x.Id).Append(id).Distinct().ToArray(), (d, x) => { x.Text = wert; x.Bearbeitet = true; });
                return;
        }
    }

    /// <summary>Änderung auf mehrere Vertretungen (per Id) anwenden.</summary>
    private void Ausfuehren(string text, IReadOnlyCollection<int> ids, Action<Schuldatei, Vertretung> aktion)
    {
        if (ids.Count == 0) return;
        Aendern(ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, AenderungsArt.Vertretung, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList()) aktion(d, v);
        });
    }

    public void VertreterSetzen(int lehrerId, bool mitbetreuung)
    {
        var liste = Aktuelle();
        if (liste.Count == 0) return;
        var probleme = liste.Select(v => Vertretungen.LehrerBelegt(D, Plan(v.Datum), lehrerId, v.Stunde, v) is { } g ? $"{Format.Datum(v.Datum)} {v.Stunde}. Std.: {g}" : null).Where(x => x != null).ToList();
        if (probleme.Count > 0 && !mitbetreuung && !Ui.Frage($"{Ix.KLehrer(lehrerId)} ist nicht frei:\n{string.Join("\n", probleme)}\n\nTrotzdem als Vertreter eintragen?")) { Neuaufbau(); return; }
        Ausfuehren($"Vertreter {Ix.KLehrer(lehrerId)}", liste.Select(v => v.Id).ToList(), (d, v) => Vertretungen.VertreterSetzen(d, v, lehrerId, mitbetreuung, AppZustand.Benutzer));
    }

    private void Entfall()
    {
        var liste = Aktuelle().Where(v => v.Art != VertretungsArt.Freisetzung).ToList();
        if (liste.Count == 0) return;
        var zurueck = liste.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt);
        Ausfuehren(zurueck ? "Entfall aufgehoben" : "Entfall", liste.Select(v => v.Id).ToList(), (d, v) =>
        {
            var istEntfall = v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt;
            if (istEntfall == zurueck) Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer);
        });
    }

    private void OhneLehrkraft()
    {
        var liste = Aktuelle().Where(v => v.Art != VertretungsArt.Pausenaufsicht).ToList();
        Ausfuehren("Vertr. o. Lehrkraft", liste.Select(v => v.Id).ToList(), (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer));
    }

    private void Teilen()
    {
        if (Aktuelle().FirstOrDefault() is not { } v) return;
        var kl = Ui.Eingabe("Klassen der neuen Teilvertretung (Kurznamen, die übrigen bleiben in der bisherigen Zeile):", "Vertretung teilen", "");
        if (kl is null) return;
        var ids = kl.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => D.Klassen.FirstOrDefault(k => k.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id)
            .Where(i => i.HasValue).Select(i => i!.Value).Where(v.NeueKlassenIds.Contains).ToList();
        Ausfuehren("Vertretung teilen", new[] { v.Id }, (d, x) =>
        {
            var n = Vertretungen.Teilen(d, x, AppZustand.Benutzer);
            if (ids.Count > 0 && ids.Count < x.NeueKlassenIds.Count)
            {
                n.VertretungsKlassenIds = ids;
                x.VertretungsKlassenIds = x.NeueKlassenIds.Except(ids).ToList();
            }
        });
    }

    private void RaumZuordnen()
    {
        if (Aktuelle().FirstOrDefault() is not { } v || v.Art == VertretungsArt.Pausenaufsicht) return;
        var r = SondereinsatzDialog.RaumWahl(this, v, $"Raum für Vertretung {v.Id} ({Format.Datum(v.Datum)}, {v.Stunde}. Std.)", out var zusatz, true);
        if (r is null) return;
        Ausfuehren("Raum zuordnen", new[] { v.Id }, (_, x) =>
        {
            if (zusatz && r.Value != 0) { if (!x.ZusatzraumIds.Contains(r.Value)) x.ZusatzraumIds.Add(r.Value); }
            else x.VertretungsraumId = r.Value == 0 ? null : r.Value;
            if (x.Art == VertretungsArt.Vertretung && x.VertreterId == x.LehrerId && x.VertreterId != null) x.Art = VertretungsArt.Raumvertretung;
            x.Bearbeitet = true;
        });
    }

    private void AlsVormerkung()
    {
        var liste = Aktuelle().Where(v => v.UnterrichtId.HasValue && v.Art != VertretungsArt.Vormerkung).ToList();
        if (liste.Count == 0) return;
        Ausfuehren("Als Vormerkung beiseitelegen", liste.Select(v => v.Id).ToList(), (d, v) => Vertretungen.AlsVormerkung(d, v, AppZustand.Benutzer));
    }

    private void Zuruecksetzen()
    {
        var liste = Aktuelle();
        if (liste.Count == 0) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Vertretung zurückgesetzt", AenderungsArt.Vertretung, d =>
        {
            var tage = new HashSet<DateOnly>();
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList())
            {
                tage.Add(v.Datum);
                if (Vertretungen.IstAbgeleitet(v))
                {
                    v.Bearbeitet = false;
                    v.VertreterId = null;
                    v.VertretungsraumId = null;
                    v.VertretungsfachId = null;
                    v.Text = "";
                    v.Mehrarbeit = false;
                }
                else
                {
                    // manuelle Zeile (Verlegung/Sondereinsatz) samt Gegenstück entfernen
                    d.Vertretungen.RemoveAll(x => x == v || (v.VerlegungsId.HasValue && x.VerlegungsId == v.VerlegungsId));
                }
            }
            foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        });
    }

    private void Loeschen()
    {
        var liste = Aktuelle();
        if (liste.Count == 0) return;
        var abgeleitet = liste.Count(Vertretungen.IstAbgeleitet);
        if (!Ui.Frage($"{liste.Count} Vertretung(en) löschen?" + (abgeleitet > 0 ? $"\n\n{abgeleitet} Zeile(n) stammen aus Absenzen/Veranstaltungen und werden wieder als offen angelegt, solange die Ursache besteht." : ""))) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Vertretungen löschen", AenderungsArt.Vertretung, d =>
        {
            var weg = d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList();
            var verlegungen = weg.Where(x => x.VerlegungsId.HasValue).Select(x => x.VerlegungsId).ToHashSet();
            d.Vertretungen.RemoveAll(x => ids.Contains(x.Id) || (x.VerlegungsId.HasValue && verlegungen.Contains(x.VerlegungsId)));
            foreach (var t in weg.Select(x => x.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        });
    }

    private void Automatik(AutomatikMethode m)
    {
        var n = 0;
        using (new WarteCursor())
            Aendern("Vertretungsautomatik", AenderungsArt.Vertretung, d =>
            {
                foreach (var t in Kalender.Tage(Von, Bis).Where(t => Kalender.IstUnterrichtstag(d, t))) n += VertretungsVorschlag.Automatisch(d, t, m, AppZustand.Benutzer);
            });
        var offen = Vertretungen.ImZeitraum(D, Von, Bis).Count(v => v.Offen);
        Ui.Info($"{n} Vertretungen wurden bearbeitet.{(offen > 0 ? $"\n{offen} Vertretungen sind weiterhin offen." : "")}", "Vertretungsautomatik");
    }

    private void NeuBerechnen()
    {
        Aendern("Vertretungen neu berechnet", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, Von, Bis));
    }

    private void VorschlagOeffnen()
    {
        Gewaehlt();
        HauptFenster.Oeffnen("vorschlag", () => new VertretungsvorschlagFenster());
    }

    private void Kontextmenue(Point pt)
    {
        var liste = Aktuelle();
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        if (liste.Count > 0)
        {
            cm.Items.Add("Vertretungsvorschlag…", Symbole.Get("vertretung"), (_, _) => VorschlagOeffnen());
            var schnell = new ToolStripMenuItem("Vertreter (bester Vorschlag)", Symbole.Get("lehrer"));
            if (liste[0].Art != VertretungsArt.Pausenaufsicht)
                foreach (var k in VertretungsVorschlag.Berechnen(D, liste[0]).Take(8))
                {
                    var id = k.Lehrer.Id;
                    schnell.DropDownItems.Add($"{k.Lehrer.Kurzname}  ({k.Lage}, Zähler {k.Zaehler:0.#})", null, (_, _) => VertreterSetzen(id, false));
                }
            if (schnell.DropDownItems.Count > 0) cm.Items.Add(schnell);
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add($"Entfall{(liste.Count > 1 ? $" ({liste.Count} Stunden)" : "")}", Symbole.Get("entfall"), (_, _) => Entfall());
            cm.Items.Add($"Vertr. o. Lehrkraft{(liste.Count > 1 ? $" ({liste.Count} Stunden)" : "")}", Symbole.Get("planen"), (_, _) => OhneLehrkraft());
            cm.Items.Add("Vertretung teilen", Symbole.Get("tausch"), (_, _) => Teilen());
            cm.Items.Add("Raum zuordnen / löschen…", Symbole.Get("raumzuordnen"), (_, _) => RaumZuordnen());
            cm.Items.Add("Als Vormerkung beiseitelegen", Symbole.Get("zurueck"), (_, _) => AlsVormerkung());
            var nicht = new ToolStripMenuItem("Nicht drucken") { Checked = liste.All(v => v.NichtDrucken) };
            nicht.Click += (_, _) => { var ziel = !nicht.Checked; Ausfuehren("Nicht drucken", liste.Select(v => v.Id).ToList(), (_, v) => v.NichtDrucken = ziel); };
            cm.Items.Add(nicht);
            var fix = new ToolStripMenuItem("Fixieren") { Checked = liste.All(v => v.Fixiert) };
            fix.Click += (_, _) => { var ziel = !fix.Checked; Ausfuehren("Fixieren", liste.Select(v => v.Id).ToList(), (_, v) => v.Fixiert = ziel); };
            cm.Items.Add(fix);
            cm.Items.Add(new ToolStripSeparator());
            if (liste[0].VertreterId is int vt) cm.Items.Add($"Lehrerplan {Ix.KLehrer(vt)} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Lehrer, vt, false));
            if (liste[0].LehrerId is int lt) cm.Items.Add($"Lehrerplan {Ix.KLehrer(lt)} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Lehrer, lt, false));
            if (liste[0].NeueKlassenIds.FirstOrDefault() is var kt && kt != 0) cm.Items.Add($"Klassenplan {Ix.KKlassen(new[] { kt })} (tagesaktuell)", Symbole.Get("stundenplan"), (_, _) => StundenplanFenster.Neu(ElementTyp.Klasse, kt, false));
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Zurücksetzen (wieder offen)", Symbole.Get("rueckgaengig"), (_, _) => Zuruecksetzen());
            cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
            cm.Items.Add(new ToolStripSeparator());
        }
        cm.Items.Add("Sondereinsatz…", Symbole.Get("neu"), (_, _) => SondereinsatzDialog.Neu(this, AppZustand.Datum, liste.FirstOrDefault()?.Stunde ?? 1, liste.FirstOrDefault()?.NeueKlassenIds.ToArray() ?? Array.Empty<int>(), Array.Empty<int>()));
        cm.Items.Add("Nachrichten zum Tag…", Symbole.Get("kommentar"), (_, _) => TagestextDialog.Zeigen(this, AppZustand.Datum));
        cm.Show(_g, pt);
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
        var lehrer = _ansicht.SelectedIndex is 1 or 3;
        var zeilen = new List<string[]>();
        foreach (var t in Kalender.Tage(Von, Bis).Where(t => Kalender.IstUnterrichtstag(D, t)))
        {
            var z = Vertretungsplan.Zeilen(D, t);
            if (lehrer) z = z.OrderBy(x => x.Vertreter == "---" || x.Vertreter == "+" ? x.Lehrer : x.Vertreter).ThenBy(x => x.Stunde).ToList();
            if (Von != Bis) zeilen.Add(new[] { $"{Planungsdienst.TageLang[((int)t.DayOfWeek + 6) % 7]}, {Format.Datum(t)}", "", "", "", "", "", "", "" });
            zeilen.AddRange(z.Select(x => lehrer
                ? new[] { x.Vertreter, x.Stunde.ToString(), x.Klassen, x.Lehrer, x.NeuesFach, x.NeuerRaum, x.Art, x.Text }
                : new[] { x.Klassen, x.Stunde.ToString(), x.Lehrer, x.Vertreter, x.NeuesFach, x.NeuerRaum, x.Art, x.Text }));
        }
        var kopf = lehrer ? new[] { "Vertreter", "Std.", "Klasse(n)", "(Lehrer)", "Fach", "Raum", "Art", "Text" } : new[] { "Klasse(n)", "Std.", "(Lehrer)", "Vertreter", "Fach", "Raum", "Art", "Text" };
        ListenDruck.Drucken(this, $"{D.Einstellungen.Vertretungsplantitel} – {Format.Datum(Von)}{(Von == Bis ? "" : " bis " + Format.Datum(Bis))}",
            kopf, new[] { 90, 40, 70, 70, 55, 55, 100, 250 }, zeilen, vorschau);
    }
}

/// <summary>
/// Vertretungsvorschlag in einem eigenen Fenster: folgt der markierten Vertretung. Reiter Vertretungsvorschläge
/// (mit Mini-Stundenplan, Merker, Zähler und Vorziehungen), Betreuungen und Räume.
/// </summary>
public sealed class VertretungsvorschlagFenster : MdiFenster
{
    private readonly Label _kopf = new() { Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 5, 0, 0), Font = Ui.SchriftFett };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorschlag = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _vorziehung = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _betreuung = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _raeume = new() { Dock = DockStyle.Fill };
    private readonly ToolStripButton _block = new("Stundenblock") { CheckOnClick = true, ToolTipText = "Vorschlag für alle Stunden des Blocks (Doppelstunde)" };
    private readonly ToolStripButton _alle = new("Alle Lehrer") { CheckOnClick = true, ToolTipText = "Auch gesperrte und weit entfernte Lehrer anzeigen" };
    private readonly ToolStripButton _besetzt = new("Auch besetzte Räume") { CheckOnClick = true };
    private Vertretung? _v;

    public VertretungsvorschlagFenster()
    {
        Text = "Vertretungsvorschlag";
        HilfeThema = "Vertretungsvorschlag";
        Icon = SymbolIcon("vertretung");
        Size = new Size(760, 440);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Vertreter übernehmen", "planen", Uebernehmen, false));
        ts.Items.Add(Knopf("Entfall", "entfall", () => Aktion("Entfall", (d, v) => Vertretungen.EntfallUmschalten(d, v, AppZustand.Benutzer)), false));
        ts.Items.Add(Knopf("Vertr. o. Lehrkraft", "lehrer", () => Aktion("Vertr. o. Lehrkraft", (_, v) => Vertretungen.OhneLehrer(v, AppZustand.Benutzer)), false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(_block);
        ts.Items.Add(_alle);
        ts.Items.Add(Knopf("Einstellungen Vertretungsvorschlag", "gewichtung", () => { if (VertretungsEinstellungenDialog.Zeigen(this, 2)) Neuaufbau(); }));
        _block.CheckedChanged += (_, _) => Neuaufbau();
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        _besetzt.CheckedChanged += (_, _) => Neuaufbau();

        Ui.RasterGestalten(_vorschlag, false);
        _vorschlag.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorschlag.MultiSelect = false;
        foreach (var (n, k, b) in new[]
        {
            ("Name", "Name", 70), ("Stpl", "Stundenplan", 170), ("Merker", "Merker", 50), ("Lage", "Lage", 85), ("Vertr", "Vertr.", 45), ("Entf", "Entfall", 50),
            ("Zaehler", "Zähler", 50), ("Woche", "Woche", 48), ("Monat", "Monat", 48), ("Punkte", "Pkt.", 42), ("Kr", "Kriterien", 220),
        })
            _vorschlag.Columns[_vorschlag.Columns.Add(n, k)].Width = b;
        _vorschlag.Columns["Kr"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _vorschlag.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Uebernehmen(); };
        _vorschlag.CellPainting += MiniPlanZeichnen;
        _vorschlag.CellFormatting += VorschlagFormatieren;
        _vorschlag.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Uebernehmen(); e.Handled = true; } };

        Ui.RasterGestalten(_vorziehung, false);
        _vorziehung.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _vorziehung.MultiSelect = false;
        _vorziehung.Columns[_vorziehung.Columns.Add("B", "Vorziehungen (Doppelklick = Unterricht in die offene Stunde verlegen)")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _vorziehung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Vorziehen(); };

        Ui.RasterGestalten(_betreuung, false);
        _betreuung.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _betreuung.MultiSelect = false;
        foreach (var (n, k, b) in new[] { ("L", "Lehrer", 70), ("K", "Klasse(n)", 90), ("F", "Fach", 60), ("R", "Raum", 60), ("G", "Gang", 60), ("Ko", "Kopplung", 60), ("Z", "Zähler", 55) })
            _betreuung.Columns[_betreuung.Columns.Add(n, k)].Width = b;
        _betreuung.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Betreuung(); };

        Ui.RasterGestalten(_raeume, false);
        _raeume.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _raeume.MultiSelect = false;
        foreach (var (n, k, b) in new[] { ("R", "Raum", 70), ("A", "Ausweichraum", 90), ("S", "Ausw. Stammraum", 105), ("B", "besetzt durch", 130), ("G", "Raumgruppen", 90), ("Ga", "Gang", 60), ("K", "Kap.-Diff.", 60) })
            _raeume.Columns[_raeume.Columns.Add(n, k)].Width = b;
        _raeume.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Raum(); };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_vorschlag);
        split.Panel2.Controls.Add(_vorziehung);
        var s1 = new TabPage("Vertretungsvorschläge");
        s1.Controls.Add(split);
        var s2 = new TabPage("Betreuungen");
        s2.Controls.Add(_betreuung);
        var raumLeiste = Werkzeugleiste();
        raumLeiste.Items.Add(Knopf("Raum übernehmen", "raumzuordnen", Raum, false));
        raumLeiste.Items.Add(_besetzt);
        var s3 = new TabPage("Räume");
        s3.Controls.Add(_raeume);
        s3.Controls.Add(raumLeiste);
        _tabs.TabPages.AddRange(new[] { s1, s2, s3 });
        Controls.Add(_tabs);
        Controls.Add(_kopf);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(80, (int)(split.Height * 0.7));
        AppZustand.VertretungGewaehlt += Gewaehlt;
        FormClosed += (_, _) => AppZustand.VertretungGewaehlt -= Gewaehlt;
        _v = AppZustand.AktiveVertretung;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
    }

    private void Gewaehlt(Vertretung? v)
    {
        if (IsDisposed) return;
        _v = v is null ? null : D.Vertretungen.FirstOrDefault(x => x.Id == v.Id) ?? v;
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        _vorschlag.Rows.Clear();
        _vorziehung.Rows.Clear();
        _betreuung.Rows.Clear();
        _raeume.Rows.Clear();
        var v = _v;
        if (v is null)
        {
            _kopf.Text = "Bitte im Vertretungsfenster oder im tagesaktuellen Stundenplan eine Vertretung markieren.";
            return;
        }
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var mehr = AppZustand.MarkierteVertretungen.Count > 1 ? $"   (+{AppZustand.MarkierteVertretungen.Count - 1} weitere markiert)" : "";
        _kopf.Text = $"Vtr-Nr. {v.Id}   {tag} {Format.Datum(v.Datum)}   {Vertretungsplan.StundeText(v)}. Std.   {Ix.KKlassen(v.NeueKlassenIds)}   ({Ix.KLehrer(v.LehrerId)})   {Ix.KFach(v.FachId)}   " +
                     $"{(v.Art == VertretungsArt.Pausenaufsicht ? Ix.K(ElementTyp.Gang, v.GangId) : Ix.KRaum(v.RaumId))}   – {Vertretungsplan.ArtText(D, v.Art)}{(v.Grund.Length > 0 ? ", " + v.Grund : "")}{mehr}";
        _kopf.BackColor = v.Offen ? Color.FromArgb(255, 220, 220) : Ui.BlauSehrHell;
        if (v.Art == VertretungsArt.Pausenaufsicht)
        {
            var a = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(D, v.Datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
            var (zv, zb) = Zaehlung.Zeitraum(D, v.Datum);
            foreach (var l in Core.Module.Pausenaufsichten.Kandidaten(D, v.Datum, a))
            {
                var z = Zaehlung.Lehrer(D, l, zv, zb);
                var r = _vorschlag.Rows.Add(l.Kurzname, "", "", "Pausenaufsicht", z.Vertretungen, z.Entfaelle, z.Summe.ToString("0.#"), "", "", "", $"Aufsichten: {Core.Module.Pausenaufsichten.Anzahl(D, l.Id)}");
                _vorschlag.Rows[r].Tag = l.Id;
            }
            return;
        }
        foreach (var k in VertretungsVorschlag.Berechnen(D, v, _block.Checked, _alle.Checked))
        {
            var r = _vorschlag.Rows.Add(D.Vertretungseinstellungen.Vorschlag.Langname ? k.Lehrer.VollerName : k.Lehrer.Kurzname, k.Stundenplan, k.Merker, k.Lage,
                k.Vertretungen, k.Entfaelle, k.Zaehler.ToString("+0.#;-0.#;0"), k.ZaehlerWoche, k.ZaehlerMonat, ((int)k.Punkte).ToString(), string.Join(", ", k.Kriterien));
            _vorschlag.Rows[r].Tag = k;
        }
        if (v.Offen)
            foreach (var vz in VertretungsVorschlag.Vorziehungen(D, v))
                _vorziehung.Rows[_vorziehung.Rows.Add(vz.Beschreibung)].Tag = vz;
        foreach (var b in VertretungsVorschlag.Betreuungen(D, v))
            _betreuung.Rows[_betreuung.Rows.Add(b.Lehrer.Kurzname, b.Klassen, b.Fach, b.Raum, b.Gang, b.Kopplung ? "ja" : "", b.Zaehler.ToString("0.#"))].Tag = b;
        foreach (var r in VertretungsVorschlag.Raeume(D, v, _besetzt.Checked, _block.Checked))
        {
            var n = _raeume.Rows.Add(r.Raum.Kurzname, r.Ausweichraum ? "ja" : "", r.AusweichStammraum ? "ja" : "", r.BesetztDurch ?? "", r.Raumgruppen, r.Gang, r.KapazitaetDifferenz == 0 ? "" : r.KapazitaetDifferenz.ToString("+0;-0"));
            _raeume.Rows[n].Tag = r;
            if (r.BesetztDurch != null) _raeume.Rows[n].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
    }

    private void VorschlagFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _vorschlag.Rows[e.RowIndex].Tag is not Kandidat k) return;
        var name = _vorschlag.Columns[e.ColumnIndex].Name;
        if (k.Rot) e.CellStyle.ForeColor = Color.Firebrick;
        if (name == "Name")
        {
            if (k.Bereitschaft) e.CellStyle.BackColor = Color.FromArgb(200, 235, 200);
            else if (k.Freigesetzt) e.CellStyle.BackColor = Color.FromArgb(215, 230, 250);
            if (k.Benachrichtigen) e.CellStyle.Font = Ui.SchriftFett;
        }
        if (name == "Merker")
        {
            var m = Vertretungen.MerkerWert(k.Merker);
            e.CellStyle.BackColor = k.Merker switch
            {
                "x" => Color.FromArgb(215, 230, 250),
                "0" => Color.FromArgb(200, 235, 200),
                "1" => Color.FromArgb(225, 245, 215),
                _ when m >= 9 => Color.FromArgb(255, 220, 220),
                _ when m >= 5 => Color.FromArgb(255, 240, 210),
                _ when !int.TryParse(k.Merker, out _) => Color.FromArgb(200, 235, 200),
                _ => Color.White,
            };
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
    }

    /// <summary>Spalte "Stundenplan": benachbarte Stunden des Lehrers als kleine Kästchen, die betroffene Stunde hervorgehoben.</summary>
    private void MiniPlanZeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _vorschlag.Columns[e.ColumnIndex].Name != "Stpl" || e.Graphics is null) return;
        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        var teile = (e.FormattedValue?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length > 0)
        {
            var b = Math.Min(34, (e.CellBounds.Width - 4) / teile.Length);
            var x = e.CellBounds.X + 2;
            foreach (var t in teile)
            {
                var aktuell = t.StartsWith('[');
                var text = t.Trim('[', ']');
                var r = new Rectangle(x, e.CellBounds.Y + 2, b - 2, e.CellBounds.Height - 5);
                var frei = text is "·" or "";
                using (var br = new SolidBrush(aktuell ? Color.FromArgb(255, 230, 150) : frei ? Color.White : Ui.BlauSehrHell)) e.Graphics.FillRectangle(br, r);
                using (var p = new Pen(aktuell ? Color.DarkOrange : Ui.Rasterlinie)) e.Graphics.DrawRectangle(p, r);
                TextRenderer.DrawText(e.Graphics, frei ? "" : text, Ui.SchriftKlein, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += b;
            }
        }
        e.Handled = true;
    }

    private List<int> Ziele()
    {
        if (_v is null) return new();
        var ids = AppZustand.MarkierteVertretungen.Contains(_v.Id) ? AppZustand.MarkierteVertretungen.ToList() : new List<int> { _v.Id };
        if (_block.Checked) ids.AddRange(VertretungsVorschlag.Block(D, _v).Select(x => x.Id));
        return ids.Distinct().ToList();
    }

    private void Aktion(string text, Action<Schuldatei, Vertretung> a)
    {
        var ids = Ziele();
        if (ids.Count == 0) return;
        Aendern(ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, AenderungsArt.Vertretung, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList()) a(d, v);
        });
        if (_v != null) _v = D.Vertretungen.FirstOrDefault(x => x.Id == _v.Id);
        Neuaufbau();
    }

    private void Uebernehmen()
    {
        var lehrer = _vorschlag.CurrentRow?.Tag switch { Kandidat k => k.Lehrer.Id, int id => id, _ => (int?)null };
        if (lehrer is not int l) return;
        Aktion($"Vertreter {Ix.KLehrer(l)}", (d, v) => Vertretungen.VertreterSetzen(d, v, l, false, AppZustand.Benutzer));
    }

    private void Betreuung()
    {
        if (_betreuung.CurrentRow?.Tag is not Betreuungskandidat b) return;
        Aktion($"Betreuung {b.Lehrer.Kurzname}", (d, v) => Vertretungen.VertreterSetzen(d, v, b.Lehrer.Id, true, AppZustand.Benutzer));
    }

    private void Raum()
    {
        if (_raeume.CurrentRow?.Tag is not Raumkandidat r) return;
        if (r.BesetztDurch != null && !Ui.Frage($"Raum {r.Raum.Kurzname} ist besetzt durch {r.BesetztDurch}. Trotzdem zuordnen?")) return;
        Aktion($"Raum {r.Raum.Kurzname}", (_, v) =>
        {
            v.VertretungsraumId = r.Raum.Id;
            if (v.Art == VertretungsArt.Vertretung && v.VertreterId == v.LehrerId && v.VertreterId != null) v.Art = VertretungsArt.Raumvertretung;
            v.Bearbeitet = true;
        });
    }

    private void Vorziehen()
    {
        if (_v is null || _vorziehung.CurrentRow?.Tag is not Vorziehung vz) return;
        var id = _v.Id;
        Aendern("Vorziehung", AenderungsArt.Vertretung, d =>
        {
            var v = d.Vertretungen.First(x => x.Id == id);
            var aktuell = VertretungsVorschlag.Vorziehungen(d, v).FirstOrDefault(x => x.Datum == vz.Datum && x.Stunde == vz.Stunde && x.Eintrag.Unterricht?.Id == vz.Eintrag.Unterricht?.Id)
                          ?? throw new InvalidOperationException("Die Vorziehung ist nicht mehr möglich.");
            VertretungsVorschlag.VorziehungAusfuehren(d, v, aktuell, AppZustand.Benutzer);
        });
    }
}
