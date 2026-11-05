using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>Eingabefeld für mehrere Elemente: Kurznamen mit Komma, Schaltfläche "…" öffnet die Auswahlliste.</summary>
public sealed class ElementWahl : Panel
{
    private readonly TextBox _text = new() { Left = 0, Top = 0 };
    private readonly Button _knopf = new() { Text = "…", Width = 26 };
    private readonly ElementTyp _typ;
    /// <summary>Schnellauswahl (alle, nach Abteilung, Jahrgang, Gruppe …), null = ohne.</summary>
    private readonly Button? _schnell;

    /// <param name="gruppen">Gruppen für die Schnellauswahl (Knopf „+“): Klick ergänzt die Elemente der Gruppe.</param>
    public ElementWahl(ElementTyp typ, IEnumerable<int>? ids = null, Func<IEnumerable<ElementGruppe>>? gruppen = null)
    {
        _typ = typ;
        Height = 24;
        Controls.Add(_text);
        Controls.Add(_knopf);
        if (gruppen != null)
        {
            _schnell = new Button { Text = "+", Width = 24 };
            new ToolTip().SetToolTip(_schnell, $"Schnell hinzufügen: {VeranstaltungsAuswahl.AlleText(typ)}, nach Abteilung, Jahrgang, Gruppe …");
            Controls.Add(_schnell);
            _schnell.Click += (_, _) =>
            {
                var m = new ContextMenuStrip { Font = Ui.Schrift };
                foreach (var b in gruppen().GroupBy(g => g.Bereich))
                {
                    var ziel = m.Items;
                    if (b.Key.Length > 0)
                    {
                        var sub = new ToolStripMenuItem("Nach " + b.Key);
                        m.Items.Add(sub);
                        ziel = sub.DropDownItems;
                    }
                    foreach (var g in b)
                    {
                        var name = b.Key.Length > 0 && g.Name.StartsWith(b.Key + " ") ? g.Name[(b.Key.Length + 1)..] : g.Name;
                        var gruppe = g;
                        ziel.Add($"{name}  ({g.Ids.Count})", null, (_, _) => Hinzufuegen(gruppe.Ids));
                    }
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Keine (leeren)", null, (_, _) => Ids = new List<int>());
                m.Closed += (_, _) => BeginInvoke(m.Dispose);
                m.Show(_schnell, new Point(0, _schnell.Height));
            };
        }
        _knopf.Click += (_, _) =>
        {
            var w = AuswahlDialog.Mehrere(FindForm()!, _typ, Ui.TypName(_typ, true) + " wählen", Ids);
            if (w != null) Ids = w.Select(e => e.Id).ToList();
        };
        var quelle = new AutoCompleteStringCollection();
        quelle.AddRange(AppZustand.D.AlleElemente(typ).Select(e => e.Kurzname).ToArray());
        _text.AutoCompleteCustomSource = quelle;
        _text.AutoCompleteMode = AutoCompleteMode.Append;
        _text.AutoCompleteSource = AutoCompleteSource.CustomSource;
        if (ids != null) Ids = ids.ToList();
        Resize += (_, _) => Anordnen();
        Anordnen();
    }

    private void Anordnen()
    {
        var rechts = _knopf.Width + 2 + (_schnell is null ? 0 : _schnell.Width + 2);
        _text.Width = Math.Max(40, Width - rechts);
        _knopf.Left = _text.Width + 2;
        _knopf.Height = _text.Height;
        if (_schnell is null) return;
        _schnell.Left = _knopf.Right + 2;
        _schnell.Height = _text.Height;
    }

    /// <summary>Elemente ergänzen (bereits eingetragene bleiben, keine Doppelten).</summary>
    public void Hinzufuegen(IEnumerable<int> ids) => Ids = VeranstaltungsAuswahl.Ergaenzen(Ids, ids);

    public List<int> Ids
    {
        get
        {
            var alle = AppZustand.D.AlleElemente(_typ).ToList();
            return _text.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => alle.FirstOrDefault(e => e.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id)
                .Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        }
        set
        {
            var ix = AppZustand.Doc.Index;
            _text.Text = string.Join(", ", value.Select(i => ix.K(_typ, i)).Where(s => s.Length > 0));
        }
    }

    /// <summary>Unbekannte Kurznamen (für eine Rückmeldung vor dem Speichern).</summary>
    public List<string> Unbekannt()
    {
        var alle = AppZustand.D.AlleElemente(_typ).Select(e => e.Kurzname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _text.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(n => !alle.Contains(n)).ToList();
    }
}

/// <summary>Hilfsfunktionen für Element-Auswahllisten.</summary>
public static class ElementListe
{
    public static ComboBox Combo(ElementTyp typ, int? id, bool leer = true)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        if (leer) c.Items.Add("");
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ)) c.Items.Add(e);
        c.Format += (_, e) => e.Value = e.ListItem is Element el ? Ui.ElementName(el) : "";
        c.SelectedItem = AppZustand.D.AlleElemente(typ).FirstOrDefault(e => e.Id == id);
        if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    public static int? Id(ComboBox c) => (c.SelectedItem as Element)?.Id;

    public static NumericUpDown Stunde(int wert)
    {
        var max = AppZustand.D.Zeitraster.StundenProTag;
        return new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, max), Value = Math.Clamp(wert, 1, Math.Max(1, max)), Width = 56 };
    }
}

/// <summary>Sondereinsatz anlegen, Unterricht einer Stunde ändern und Raum einer Stunde zuordnen (Planungsdialog).</summary>
public static class SondereinsatzDialog
{
    /// <summary>
    /// Sondereinsätze: oben Datum, Stunde von/bis und Schaltflächen Klassen, Lehrer, Klassenkonferenz; darunter eine Tabelle,
    /// in der mehrere Sondereinsätze direkt eingegeben werden (unten immer eine leere Zeile).
    /// </summary>
    public static void Neu(IWin32Window owner, DateOnly datum, int stunde, int[] klassen, int[] lehrer)
    {
        var d = AppZustand.D;
        var ix = AppZustand.Doc.Index;
        using var f = new Dialog("Sondereinsätze", 860, 380);
        f.FormBorderStyle = FormBorderStyle.Sizable;
        f.MinimumSize = new Size(700, 300);
        f.Controls.Add(new Label { Text = "Datum", Left = 12, Top = 10, AutoSize = true });
        var dat = new DateTimePicker { Format = DateTimePickerFormat.Short, Left = 12, Top = 30, Width = 120, Value = datum.ToDateTime(TimeOnly.MinValue) };
        f.Controls.Add(new Label { Text = "Stunde von", Left = 150, Top = 10, AutoSize = true });
        var von = ElementListe.Stunde(stunde);
        von.SetBounds(150, 30, 60, 23);
        f.Controls.Add(new Label { Text = "Stunde bis", Left = 220, Top = 10, AutoSize = true });
        var bis = ElementListe.Stunde(stunde);
        bis.SetBounds(220, 30, 60, 23);
        var bKl = new Button { Text = "Klassen", Left = 300, Top = 28, Width = 120, Height = 26 };
        var bLe = new Button { Text = "Lehrer", Left = 426, Top = 28, Width = 120, Height = 26 };
        var bKk = new Button { Text = "Klassenkonferenz", Left = 552, Top = 28, Width = 130, Height = 26 };
        f.Controls.AddRange(new Control[] { dat, von, bis, bKl, bLe, bKk });
        var g = new DataGridView { Left = 12, Top = 66, Width = 836, Height = 260, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(g, true);
        g.AllowUserToAddRows = true;
        g.AllowUserToDeleteRows = true;
        foreach (var (n, k, w) in new[] { ("D", "Datum", 90), ("SV", "Stunde von", 75), ("SB", "Stunde bis", 75), ("L", "Lehrer", 110), ("K", "Klassen", 110) })
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = w });
        g.Columns.Add(Steuerelemente.SuchKombi.Spalte("F", "Fach", 80, Stammdaten.Sortiert(d.Faecher).Select(x => x.Kurzname)));
        g.Columns.Add(Steuerelemente.SuchKombi.Spalte("R", "Raum", 80, Stammdaten.Sortiert(d.Raeume).Select(x => x.Kurzname)));
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "G", HeaderText = "Schülergruppe", Width = 95 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        // Auswahllisten mit Langnamen wie im Veranstaltungsfenster
        Steuerelemente.SuchKombi.Aktivieren(g, (zelle, text) => (zelle.OwningColumn.Name == "F"
                ? (Element?)d.Faecher.FirstOrDefault(x => x.Kurzname == text) : d.Raeume.FirstOrDefault(x => x.Kurzname == text)) is { } el
            ? new Steuerelemente.ListenStil(Color.White, Color.Black, el.Langname) : null);
        f.Controls.Add(g);

        // Wie Vorbild: Die erste Zeile eines Sondereinsatzes gilt für den ganzen Stundenbereich; darunter stehen die Stunden
        // einzeln (eingerückt). Was in der Kopfzeile eingetragen wird, geht auf alle Stunden über; einzelne Stunden lassen
        // sich danach abweichend ändern. Kinderzeilen tragen im Tag ihre Kopfzeile.
        var kopfFarbe = Color.FromArgb(214, 232, 224);
        var syncAktiv = false;
        // Summenzeilen (Klassenlehrer-Unterricht, Handbuch Kap. 10.1.2.1.2.1): Kopfzeile mit allen beteiligten Klassen und Lehrern,
        // darunter je Klasse eine Zeile mit ihrem Klassenlehrer; Fach, Raum, Schülergruppe, Text und Stunden der Summenzeile gelten für alle
        var summen = new HashSet<DataGridViewRow>();
        bool IstKind(DataGridViewRow r) => r.Tag is DataGridViewRow;
        void SummeAktualisieren(DataGridViewRow kopf)
        {
            var kinder = Kinder(kopf);
            string Liste(string spalte) => string.Join(", ", kinder.SelectMany(k => (k.Cells[spalte].Value?.ToString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct());
            kopf.Cells["L"].Value = Liste("L");
            kopf.Cells["K"].Value = Liste("K");
        }
        List<DataGridViewRow> Kinder(DataGridViewRow kopf) => g.Rows.Cast<DataGridViewRow>().Where(r => ReferenceEquals(r.Tag, kopf)).ToList();
        int Zahl(DataGridViewRow r, string spalte, int ersatz) => int.TryParse(r.Cells[spalte].Value?.ToString(), out var v) ? v : ersatz;
        void KinderAufbauen(DataGridViewRow kopf)
        {
            if (kopf.IsNewRow || IstKind(kopf) || kopf.DataGridView is null || summen.Contains(kopf)) return;
            syncAktiv = true;
            try
            {
                foreach (var k in Kinder(kopf)) g.Rows.Remove(k);
                var sv = Zahl(kopf, "SV", 1);
                var sb = Math.Max(sv, Zahl(kopf, "SB", sv));
                kopf.Cells["SV"].Style.BackColor = kopf.Cells["SB"].Style.BackColor = kopf.Cells["D"].Style.BackColor = kopfFarbe;
                if (sb <= sv) return;
                for (var h = sv; h <= sb; h++)
                {
                    var i = kopf.Index + 1 + (h - sv);
                    g.Rows.Insert(i, kopf.Cells["D"].Value, h.ToString(), h.ToString(), kopf.Cells["L"].Value, kopf.Cells["K"].Value,
                        kopf.Cells["F"].Value ?? "", kopf.Cells["R"].Value ?? "", kopf.Cells["G"].Value, kopf.Cells["T"].Value);
                    var r = g.Rows[i];
                    r.Tag = kopf;
                    foreach (var n in new[] { "D", "SV", "SB" })
                    {
                        r.Cells[n].ReadOnly = true;
                        r.Cells[n].Style.BackColor = kopfFarbe;
                        r.Cells[n].Style.ForeColor = Color.FromArgb(60, 70, 80);
                    }
                    r.Cells["SV"].Style.Padding = new Padding(26, 0, 0, 0);
                    r.DefaultCellStyle.BackColor = Color.FromArgb(253, 252, 236);
                }
            }
            finally { syncAktiv = false; }
        }
        g.CellEndEdit += (_, e) =>
        {
            if (syncAktiv || e.RowIndex < 0 || e.RowIndex >= g.Rows.Count) return;
            var r = g.Rows[e.RowIndex];
            var spalte = g.Columns[e.ColumnIndex].Name;
            // Zeile unter einer Summenzeile geändert: Summe (alle Klassen und Lehrer) nachführen
            if (IstKind(r) && summen.Contains((DataGridViewRow)r.Tag!)) { if (spalte is "L" or "K") SummeAktualisieren((DataGridViewRow)r.Tag!); return; }
            if (r.IsNewRow || IstKind(r)) return;
            if (summen.Contains(r))
            {
                if (spalte is "SV" or "SB" && Zahl(r, "SB", 0) < Zahl(r, "SV", 1)) r.Cells["SB"].Value = r.Cells["SV"].Value;
                foreach (var k in Kinder(r).Where(_ => spalte is not ("L" or "K"))) k.Cells[spalte].Value = r.Cells[spalte].Value;
                return;
            }
            var kinder = Kinder(r);
            var sv = Zahl(r, "SV", 1);
            var sb = Math.Max(sv, Zahl(r, "SB", sv));
            var passt = kinder.Count == (sb > sv ? sb - sv + 1 : 0) && kinder.Select((k, i) => Zahl(k, "SV", -1) == sv + i).All(x => x);
            // Neuaufbau außerhalb des Bearbeitungsereignisses (sonst Wiedereintritt in DataGridView)
            if (spalte is "SV" or "SB" or "D" || !passt) g.BeginInvoke(() => KinderAufbauen(r));
            else foreach (var k in kinder) k.Cells[spalte].Value = r.Cells[spalte].Value;
        };
        g.UserDeletingRow += (_, e) =>
        {
            if (e.Row is null) return;
            if (IstKind(e.Row))
            {
                if (e.Row.Tag is DataGridViewRow sk && summen.Contains(sk)) g.BeginInvoke(() => { if (sk.DataGridView != null) SummeAktualisieren(sk); });
                return;
            }
            summen.Remove(e.Row);
            var kinder = Kinder(e.Row);
            if (kinder.Count > 0) g.BeginInvoke(() => { foreach (var k in kinder.Where(k => k.DataGridView != null)) g.Rows.Remove(k); });
        };
        // Summenzeile: Klassen und Lehrer sind die Summe der Zeilen darunter (nicht direkt änderbar)
        g.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && summen.Contains(g.Rows[e.RowIndex]) && g.Columns[e.ColumnIndex].Name is "L" or "K") e.Cancel = true;
        };
        // Kopfzeile: Baumzeichen ⊟ vor der Stunde wie im Original
        g.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != "SV" || e.Graphics is null) return;
            var r = g.Rows[e.RowIndex];
            if (r.IsNewRow) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            using var stift = new Pen(Color.FromArgb(150, 160, 170)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            var b = e.CellBounds;
            if (IstKind(r))
            {
                var x = b.Left + 12;
                e.Graphics.DrawLine(stift, x, b.Top, x, b.Top + b.Height / 2);
                e.Graphics.DrawLine(stift, x, b.Top + b.Height / 2, b.Left + 22, b.Top + b.Height / 2);
                if (Kinder((DataGridViewRow)r.Tag!).LastOrDefault() != r) e.Graphics.DrawLine(stift, x, b.Top + b.Height / 2, x, b.Bottom);
            }
            else if (Kinder(r).Count > 0)
            {
                var k = new Rectangle(b.Left + 8, b.Top + (b.Height - 9) / 2, 9, 9);
                e.Graphics.FillRectangle(Brushes.White, k);
                e.Graphics.DrawRectangle(Pens.Gray, k);
                e.Graphics.DrawLine(Pens.Black, k.Left + 2, k.Top + 4, k.Right - 2, k.Top + 4);
            }
            e.Handled = true;
        };
        g.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["D"].Value = Format.Datum(DateOnly.FromDateTime(dat.Value));
            e.Row.Cells["SV"].Value = ((int)von.Value).ToString();
            e.Row.Cells["SB"].Value = ((int)Math.Max(von.Value, bis.Value)).ToString();
            e.Row.Cells["F"].Value = "";
            e.Row.Cells["R"].Value = "";
        };
        // Datum und Stunde von/bis oben wirken sofort auf die erste Zeile der Tabelle (samt ihren Stundenzeilen)
        void KopfUebernehmen()
        {
            if (syncAktiv) return;
            var erste = g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && !IstKind(r));
            if (erste is null) return;
            if (g.IsCurrentCellInEditMode) g.EndEdit();
            erste.Cells["D"].Value = Format.Datum(DateOnly.FromDateTime(dat.Value));
            erste.Cells["SV"].Value = ((int)von.Value).ToString();
            erste.Cells["SB"].Value = ((int)Math.Max(von.Value, bis.Value)).ToString();
            KinderAufbauen(erste);
            foreach (var k in Kinder(erste))
            {
                k.Cells["D"].Value = erste.Cells["D"].Value;
                if (!summen.Contains(erste)) continue;
                k.Cells["SV"].Value = erste.Cells["SV"].Value;
                k.Cells["SB"].Value = erste.Cells["SB"].Value;
            }
        }
        von.ValueChanged += (_, _) => { if (bis.Value < von.Value) bis.Value = von.Value; KopfUebernehmen(); };
        bis.ValueChanged += (_, _) => { if (bis.Value < von.Value) von.Value = bis.Value; KopfUebernehmen(); };
        dat.ValueChanged += (_, _) => KopfUebernehmen();
        int Zeile()
        {
            if (g.CurrentRow is { IsNewRow: false } r) return r.Index;
            var i = g.Rows.Add(Format.Datum(DateOnly.FromDateTime(dat.Value)), ((int)von.Value).ToString(), ((int)Math.Max(von.Value, bis.Value)).ToString(), "", "", "", "", "", "");
            KinderAufbauen(g.Rows[i]);
            g.CurrentCell = g.Rows[i].Cells["L"];
            return i;
        }
        void Uebertragen(int zeile, string spalte)
        {
            var r = g.Rows[zeile];
            if (IstKind(r)) { if (r.Tag is DataGridViewRow sk && summen.Contains(sk)) SummeAktualisieren(sk); return; }
            if (summen.Contains(r) && spalte is "L" or "K") { SummeAktualisieren(r); return; }
            foreach (var k in Kinder(r)) k.Cells[spalte].Value = r.Cells[spalte].Value;
        }
        List<int> Ids(ElementTyp typ, object? v) => (v?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => d.AlleElemente(typ).FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id).OfType<int>().ToList();
        // Lehrer und Klassen: Mehrfachauswahl mit Häkchen und Langnamen über den Pfeil in der Zelle (Doppelklick oder Alt+↓)
        void MehrfachZeigen(int zeile, int spalte)
        {
            var name = g.Columns[spalte].Name;
            var typ = name == "L" ? ElementTyp.Lehrer : ElementTyp.Klasse;
            var eintraege = Stammdaten.Sortiert(d, typ).Where(x => !x.Ignorieren)
                .Select(x => new Steuerelemente.MehrfachAuswahl.Eintrag(x.Kurzname, x is Lehrer l ? l.VollerName : x.Langname)).ToList();
            var aktuell = (g.Rows[zeile].Cells[spalte].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            Steuerelemente.MehrfachAuswahl.Zeigen(g, zeile, spalte, eintraege, aktuell, true, gewaehlt =>
            {
                if (f.IsDisposed || zeile >= g.Rows.Count) return;
                if (g.Rows[zeile].IsNewRow) zeile = Zeile();
                g.Rows[zeile].Cells[name].Value = string.Join(",", gewaehlt);
                Uebertragen(zeile, name);
            });
        }
        bool Mehrfach(int spalte) => spalte >= 0 && g.Columns[spalte].Name is "L" or "K";
        g.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || !Mehrfach(e.ColumnIndex) || g.CurrentCell is not { } c || c.RowIndex != e.RowIndex || c.ColumnIndex != e.ColumnIndex || g.IsCurrentCellInEditMode) return;
            Steuerelemente.MehrfachAuswahl.PfeilZeichnen(e);
        };
        g.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.Button != MouseButtons.Left || !Mehrfach(e.ColumnIndex)) return;
            if (Steuerelemente.MehrfachAuswahl.AufPfeil(g, e)) MehrfachZeigen(e.RowIndex, e.ColumnIndex);
        };
        g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && Mehrfach(e.ColumnIndex)) MehrfachZeigen(e.RowIndex, e.ColumnIndex); };
        g.KeyDown += (_, e) =>
        {
            if (e.Alt && e.KeyCode == Keys.Down && g.CurrentCell is { } c && Mehrfach(c.ColumnIndex)) { e.Handled = true; MehrfachZeigen(c.RowIndex, c.ColumnIndex); }
        };
        bKl.Click += (_, _) =>
        {
            var i = Zeile();
            var w = SondereinsatzAuswahl.Waehlen(f, ElementTyp.Klasse, Ids(ElementTyp.Klasse, g.Rows[i].Cells["K"].Value), Array.Empty<int>());
            if (w != null) { g.Rows[i].Cells["K"].Value = string.Join(",", w.Select(x => x.Kurzname)); Uebertragen(i, "K"); }
        };
        bLe.Click += (_, _) =>
        {
            var i = Zeile();
            var kl = Ids(ElementTyp.Klasse, g.Rows[i].Cells["K"].Value);
            var w = SondereinsatzAuswahl.Waehlen(f, ElementTyp.Lehrer, Ids(ElementTyp.Lehrer, g.Rows[i].Cells["L"].Value), kl);
            if (w is null) return;
            // Klassenlehrer-Unterricht (wie im Handbuch): mehrere Klassen und deren Klassenlehrer gewählt – jeder Klassenlehrer wird seiner
            // Klasse zugeordnet (je Klasse eine Zeile, Raum = Stammraum der Klasse)
            var gewaehlt = w.Select(x => x.Id).ToHashSet();
            var klassenObj = kl.Select(k => d.Klassen.FirstOrDefault(x => x.Id == k)).OfType<Klasse>().ToList();
            var zuordnung = klassenObj.Select(k => (Klasse: k, Lehrer: k.KlassenlehrerIds.Where(gewaehlt.Contains).ToList())).ToList();
            if (klassenObj.Count > 1 && zuordnung.All(z => z.Lehrer.Count > 0) && gewaehlt.All(l => zuordnung.Any(z => z.Lehrer.Contains(l))))
            {
                // wie im Handbuch: oben die Summenzeile (alle Klassen und Lehrer), darunter jede Klasse mit ihrem Klassenlehrer und Stammraum
                var kopf = g.Rows[i];
                if (IstKind(kopf)) kopf = (DataGridViewRow)kopf.Tag!;
                syncAktiv = true;
                try
                {
                    foreach (var k in Kinder(kopf)) g.Rows.Remove(k);
                    summen.Add(kopf);
                    kopf.Cells["R"].Value = "";
                    kopf.Cells["SV"].Style.BackColor = kopf.Cells["SB"].Style.BackColor = kopf.Cells["D"].Style.BackColor = kopfFarbe;
                    kopf.Cells["L"].Style.BackColor = kopf.Cells["K"].Style.BackColor = Color.FromArgb(240, 240, 240);
                    var pos = kopf.Index + 1;
                    foreach (var (klasse, kls) in zuordnung)
                    {
                        g.Rows.Insert(pos, kopf.Cells["D"].Value, kopf.Cells["SV"].Value, kopf.Cells["SB"].Value, string.Join(",", kls.Select(ix.KLehrer)), klasse.Kurzname,
                            kopf.Cells["F"].Value ?? "", klasse.StammraumId is int sr ? ix.KRaum(sr) : "", kopf.Cells["G"].Value, kopf.Cells["T"].Value);
                        var r = g.Rows[pos];
                        r.Tag = kopf;
                        foreach (var n in new[] { "D", "SV", "SB" })
                        {
                            r.Cells[n].ReadOnly = true;
                            r.Cells[n].Style.BackColor = kopfFarbe;
                            r.Cells[n].Style.ForeColor = Color.FromArgb(60, 70, 80);
                        }
                        r.Cells["SV"].Style.Padding = new Padding(26, 0, 0, 0);
                        r.DefaultCellStyle.BackColor = Color.FromArgb(253, 252, 236);
                        pos++;
                    }
                    SummeAktualisieren(kopf);
                }
                finally { syncAktiv = false; }
                return;
            }
            g.Rows[i].Cells["L"].Value = string.Join(",", w.Select(x => x.Kurzname));
            Uebertragen(i, "L");
        };
        bKk.Click += (_, _) =>
        {
            // Klassenkonferenz: Klasse wählen – alle Lehrkräfte, die in dieser Klasse unterrichten, werden übernommen
            var i = Zeile();
            var kl = Ids(ElementTyp.Klasse, g.Rows[i].Cells["K"].Value);
            var w = SondereinsatzAuswahl.Klassenkonferenz(f, kl.Count > 0 ? kl[0] : null);
            if (w is null) return;
            g.Rows[i].Cells["K"].Value = ix.KKlassen(new[] { w.Value.Klasse });
            g.Rows[i].Cells["L"].Value = string.Join(",", w.Value.Lehrer.Select(ix.KLehrer));
            if (string.IsNullOrWhiteSpace(g.Rows[i].Cells["T"].Value?.ToString())) g.Rows[i].Cells["T"].Value = "Klassenkonferenz " + ix.KKlassen(new[] { w.Value.Klasse });
            Uebertragen(i, "K"); Uebertragen(i, "L"); Uebertragen(i, "T");
        };
        if (klassen.Length > 0 || lehrer.Length > 0)
            g.Rows.Add(Format.Datum(datum), stunde.ToString(), stunde.ToString(), string.Join(",", lehrer.Select(ix.KLehrer)), ix.KKlassen(klassen), "", "", "", "");
        if (f.ShowDialog(owner) != DialogResult.OK) return;
        g.EndEdit();
        var eintraege = new List<(DateOnly Datum, int Von, int Bis, List<int> L, List<int> K, int? F, int? R, string G, string T)>();
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow) continue;
            // Kopfzeilen mit Einzelstunden: die Einzelstunden (ggf. abweichend geändert) werden angelegt
            if (!IstKind(r) && Kinder(r).Count > 0) continue;
            var l = Ids(ElementTyp.Lehrer, r.Cells["L"].Value);
            var k = Ids(ElementTyp.Klasse, r.Cells["K"].Value);
            if (l.Count == 0 && k.Count == 0) continue;
            var dd = Ui.DatumLesen(r.Cells["D"].Value?.ToString() ?? "", DateOnly.FromDateTime(dat.Value));
            var sv = int.TryParse(r.Cells["SV"].Value?.ToString(), out var a1) ? a1 : (int)von.Value;
            var sb = int.TryParse(r.Cells["SB"].Value?.ToString(), out var a2) ? Math.Max(sv, a2) : sv;
            var fach = d.Faecher.FirstOrDefault(x => x.Kurzname == r.Cells["F"].Value?.ToString())?.Id;
            var raum = d.Raeume.FirstOrDefault(x => x.Kurzname == r.Cells["R"].Value?.ToString())?.Id;
            eintraege.Add((dd, sv, sb, l, k, fach, raum, r.Cells["G"].Value?.ToString() ?? "", r.Cells["T"].Value?.ToString() ?? ""));
        }
        if (eintraege.Count == 0) { Ui.Info("Es wurde kein Sondereinsatz eingegeben (Lehrer oder Klassen fehlen)."); return; }
        Ausfuehren(eintraege.Count > 1 ? $"Sondereinsätze ({eintraege.Count})" : "Sondereinsatz", x =>
        {
            foreach (var e in eintraege) Vertretungen.Sondereinsatz(x, e.Datum, e.Von, e.Bis, e.K, e.L, e.F, e.R, e.T, e.G, null, AppZustand.Benutzer);
        });
    }

    /// <summary>Sondereinsatz / Stunde ändern für eine Kopplungszeile (siehe <see cref="StundeAendernDialog"/>).</summary>
    public static void StundeAendern(IWin32Window owner, int uid, int zeile, DateOnly datum, int stunde)
    {
        var tp = Tagesplan.Berechnen(AppZustand.D, datum);
        var e = tp.Stunde(stunde).FirstOrDefault(x => x.Findetstatt && (x.Regel?.Unterricht.Id ?? x.Vertretung?.UnterrichtId) == uid && (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex) == zeile);
        if (e is null) { Ui.Info("Die Stunde findet an diesem Tag nicht statt."); return; }
        StundeAendernDialog.Zeigen(owner, datum, stunde, new[] { e });
    }

    /// <summary>Raum zuordnen / löschen im Vertretungsmodus (siehe <see cref="RaumZuordnenVertretungDialog"/>).</summary>
    public static void RaumZuordnen(IWin32Window owner, int uid, int zeile, DateOnly datum, int stunde) =>
        RaumZuordnenVertretungDialog.Zeigen(owner, datum, stunde, uid, zeile);

    /// <summary>Raumvorschlag als Auswahlliste. Rückgabe: Raum-Id, 0 = Raum löschen, null = abgebrochen.</summary>
    public static int? RaumWahl(IWin32Window owner, Vertretung v, string titel, out bool zusaetzlich, bool zusatzErlaubt)
    {
        zusaetzlich = false;
        using var f = new Dialog(titel, 620, 420);
        var g = new DataGridView { Left = 10, Top = 34, Width = 600, Height = 300 };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        foreach (var (n, k, b) in new[] { ("R", "Raum", 70), ("A", "Ausweichraum", 90), ("S", "Ausw. Stammraum", 105), ("B", "besetzt durch", 120), ("G", "Raumgruppen", 90), ("Ga", "Gang", 60), ("K", "Kap.-Diff.", 60) })
            g.Columns[g.Columns.Add(n, k)].Width = b;
        var besetzte = new CheckBox { Text = "Auch besetzte Räume anzeigen", Left = 10, Top = 8, AutoSize = true };
        var zusatz = new CheckBox { Text = "Zusätzlichen Raum zuordnen", Left = 260, Top = 8, AutoSize = true, Visible = zusatzErlaubt };
        void Fuellen()
        {
            g.Rows.Clear();
            var i = g.Rows.Add("(kein Raum)");
            g.Rows[i].Tag = 0;
            foreach (var r in VertretungsVorschlag.Raeume(AppZustand.D, v, besetzte.Checked))
            {
                var n = g.Rows.Add(r.Raum.Kurzname, r.Ausweichraum ? "ja" : "", r.AusweichStammraum ? "ja" : "", r.BesetztDurch ?? "", r.Raumgruppen, r.Gang, r.KapazitaetDifferenz == 0 ? "" : r.KapazitaetDifferenz.ToString("+0;-0"));
                g.Rows[n].Tag = r.Raum.Id;
                if (r.BesetztDurch != null) g.Rows[n].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
            if (g.Rows.Count > 1) g.CurrentCell = g.Rows[1].Cells[0];
        }
        besetzte.CheckedChanged += (_, _) => Fuellen();
        g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) f.DialogResult = DialogResult.OK; };
        f.Controls.AddRange(new Control[] { besetzte, zusatz, g });
        Fuellen();
        if (f.ShowDialog(owner) != DialogResult.OK || g.CurrentRow?.Tag is not int id) return null;
        zusaetzlich = zusatz.Checked;
        return id;
    }

    private static void Ausfuehren(string text, Action<Schuldatei> a)
    {
        try { AppZustand.Doc.Aendern(text, AenderungsArt.Vertretung, a); }
        catch (Exception ex) { Ui.Fehler(ex); }
    }
}

/// <summary>
/// Auswahldialoge des Fensters „Sondereinsätze“ wie im Handbuch: „Auswahl“ (Kurzname, Langname, bei Lehrern Klasse(n); Mehrfachauswahl mit
/// Strg; Filter Lehrer / Klassenlehrer / Lehrer im Unterricht; Alle, Inverse, Markierte) und „Klassenkonferenz“.
/// </summary>
public static class SondereinsatzAuswahl
{
    /// <summary>Lehrkräfte, die in den Klassen unterrichten (Unterrichte und Klassenlehrer).</summary>
    public static List<int> LehrerDerKlassen(Schuldatei d, IReadOnlyCollection<int> klassen) =>
        d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.KlassenIds.Intersect(klassen).Any() && z.LehrerId.HasValue).Select(z => z.LehrerId!.Value)
            .Concat(d.Klassen.Where(k => klassen.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds)).Distinct().ToList();

    /// <param name="rot">Elemente, die rot angezeigt werden (z. B. zu diesem Zeitpunkt im Unterricht bzw. besetzt).</param>
    public static List<Element>? Waehlen(IWin32Window owner, ElementTyp typ, IReadOnlyCollection<int> vorauswahl, IReadOnlyCollection<int> klassen, Func<int, bool>? rot = null)
    {
        var d = AppZustand.D;
        var lehrer = typ == ElementTyp.Lehrer;
        using var f = new Dialog("Auswahl", lehrer ? 300 : 280, 420);
        f.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        f.Ok.Text = "Ok";
        var g = new DataGridView { Left = 6, Top = 6, Width = f.ClientSize.Width - 12, Height = lehrer ? 290 : 322, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = true;
        g.RowHeadersVisible = false;
        g.Columns[g.Columns.Add("N", "Kurzname")].Width = 70;
        g.Columns[g.Columns.Add("L", "Langname")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var klassenAlle = klassen.Count > 0 ? klassen : d.Klassen.Select(k => k.Id).ToList();
        string KlassenVon(Element e) => string.Join(", ", Stammdaten.Sortiert(d.Klassen).Where(k => k.KlassenlehrerIds.Contains(e.Id)).Select(k => k.Kurzname));
        if (lehrer) g.Columns[g.Columns.Add("K", "Klasse(n)")].Width = 70;
        foreach (var e in Stammdaten.Sortiert(d, typ).Where(x => !x.Ignorieren))
        {
            var i = lehrer ? g.Rows.Add(e.Kurzname, e is Lehrer l ? l.VollerName : e.Langname, KlassenVon(e)) : g.Rows.Add(e.Kurzname, e.Langname);
            g.Rows[i].Tag = e;
            if (rot?.Invoke(e.Id) == true) g.Rows[i].DefaultCellStyle.ForeColor = Color.Red;
        }
        var vor = vorauswahl.ToHashSet();
        f.Shown += (_, _) =>
        {
            g.ClearSelection();
            foreach (DataGridViewRow r in g.Rows) r.Selected = vor.Contains(((Element)r.Tag!).Id);
        };
        f.Controls.Add(g);
        var y = g.Bottom + 6;
        if (lehrer)
        {
            // Filter: Lehrer / Klassenlehrer (Klassen) / Lehrer im Unterricht (Klassen)
            var kText = AppZustand.Doc.Index.KKlassen(klassenAlle);
            f.Controls.Add(new Label { Text = "Filter", Left = 8, Top = y + 4, Width = 50, Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
            var filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 64, Top = y, Width = f.ClientSize.Width - 72, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            filter.Items.AddRange(new object[] { "Lehrer", $"Klassenlehrer ({kText})", $"Lehrer im Unterricht ({kText})" });
            filter.SelectedIndex = 0;
            var kls = d.Klassen.Where(k => klassenAlle.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds).ToHashSet();
            var imUnterricht = LehrerDerKlassen(d, klassenAlle).ToHashSet();
            filter.SelectedIndexChanged += (_, _) =>
            {
                g.CurrentCell = null;
                foreach (DataGridViewRow r in g.Rows)
                {
                    var id = ((Element)r.Tag!).Id;
                    r.Visible = filter.SelectedIndex switch { 1 => kls.Contains(id), 2 => imUnterricht.Contains(id), _ => true };
                    if (!r.Visible) r.Selected = false;
                }
            };
            f.Controls.Add(filter);
            y += 30;
        }
        var nurMarkierte = false;
        void Knopf(string text, int x, Action a)
        {
            var b = new Button { Text = text, Left = x, Top = y, Width = 80, Height = 24, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            b.Click += (_, _) => a();
            f.Controls.Add(b);
        }
        Knopf("Alle", 8, () => { foreach (DataGridViewRow r in g.Rows) if (r.Visible) r.Selected = true; });
        Knopf("Inverse", 94, () => { foreach (DataGridViewRow r in g.Rows) if (r.Visible) r.Selected = !r.Selected; });
        Knopf("Markierte", 180, () =>
        {
            nurMarkierte = !nurMarkierte;
            g.CurrentCell = null;
            foreach (DataGridViewRow r in g.Rows) r.Visible = !nurMarkierte || r.Selected;
        });
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return g.Rows.Cast<DataGridViewRow>().Where(r => r.Selected).Select(r => (Element)r.Tag!).ToList();
    }

    /// <summary>„Klassenkonferenz“: Für welche Klasse? – zeigt sofort die Lehrkräfte, die in dieser Klasse unterrichten.</summary>
    public static (int Klasse, List<int> Lehrer)? Klassenkonferenz(IWin32Window owner, int? klasse)
    {
        var d = AppZustand.D;
        using var f = new Dialog("Klassenkonferenz", 280, 400);
        f.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        f.Ok.Text = "Ok";
        f.Controls.Add(new Label { Text = "Für welche Klasse soll die Klassenkonferenz angelegt werden?", Left = 8, Top = 6, Width = 264, Height = 32 });
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 40, Width = 150 };
        var klassen = Stammdaten.Sortiert(d.Klassen).Where(k => !k.Ignorieren).ToList();
        foreach (var k in klassen) cb.Items.Add(k.Kurzname);
        var g = new DataGridView { Left = 8, Top = 70, Width = 264, Height = 280, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        g.Columns[g.Columns.Add("N", "Kurzname")].Width = 70;
        g.Columns[g.Columns.Add("L", "Langname")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var lehrer = new List<int>();
        cb.SelectedIndexChanged += (_, _) =>
        {
            g.Rows.Clear();
            if (cb.SelectedIndex < 0) return;
            var ids = LehrerDerKlassen(d, new[] { klassen[cb.SelectedIndex].Id }).ToHashSet();
            var liste = Stammdaten.Sortiert(d.Lehrer).Where(l => ids.Contains(l.Id)).ToList();
            lehrer = liste.Select(l => l.Id).ToList();
            foreach (var l in liste) g.Rows.Add(l.Kurzname, l.Langname);
        };
        cb.SelectedIndex = klasse is int kid ? Math.Max(0, klassen.FindIndex(k => k.Id == kid)) : klassen.Count > 0 ? 0 : -1;
        f.Controls.AddRange(new Control[] { cb, g });
        if (f.ShowDialog(owner) != DialogResult.OK || cb.SelectedIndex < 0) return null;
        return (klassen[cb.SelectedIndex].Id, lehrer);
    }
}

/// <summary>Klausur anlegen bzw. bearbeiten.</summary>
public static class KlausurDialog
{
    public static void Neu(IWin32Window owner, DateOnly datum, int stunde, TagesEintrag? e)
    {
        var k = new Klausur
        {
            Datum = datum, StundeVon = stunde, StundeBis = stunde, KlassenIds = e?.KlassenIds.ToList() ?? new(), LehrerIds = e?.LehrerIds.ToList() ?? new(),
            RaumIds = e?.RaumIds.ToList() ?? new(), UnterrichtId = e?.Unterricht?.Id, Name = e is null ? "Klausur" : $"Klausur {AppZustand.Doc.Index.KFach(e.FachId)}".Trim(),
        };
        if (!Zeigen(owner, k, true)) return;
        try
        {
            AppZustand.Doc.Aendern("Klausur anlegen", AenderungsArt.Vertretung, d =>
            {
                k.Id = d.NeueId();
                d.Klausuren.Add(k);
                Vertretungen.Aktualisieren(d, k.Datum);
            });
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    public static bool Zeigen(IWin32Window owner, Klausur k) => Zeigen(owner, k, false);

    /// <summary>
    /// „Neue Klausur“ wie im Handbuch: links Name und Text, rechts Datum, Stunde von und Stunde bis; darunter die Reiter „Kurse“
    /// (Unterricht, Klassen) und „Lehrer/Räume“ mit den Schaltflächen Lehrer, Lehrer aus Kurs, Räume und Raum aus Kurs – in den
    /// Auswahllisten sind zum Zeitpunkt der Klausur unterrichtende Lehrer bzw. besetzte Räume rot.
    /// </summary>
    public static bool Zeigen(IWin32Window owner, Klausur k, bool neu)
    {
        var d = AppZustand.D;
        var ix = AppZustand.Doc.Index;
        using var f = new Dialog(neu ? "Neue Klausur" : "Klausur", 640, 404);
        f.Ok.Text = "Ok";
        var name = new TextBox { Text = k.Name, Left = 10, Top = 12, Width = 280 };
        var tx = new TextBox { Text = k.Text, Left = 10, Top = 40, Width = 280 };
        Steuerelemente.Textverlauf.Anbinden(tx, "Klausur:Text");
        f.Controls.AddRange(new Control[] { name, new Label { Text = "Name", Left = 298, Top = 15, AutoSize = true }, tx, new Label { Text = "Text", Left = 298, Top = 43, AutoSize = true } });
        var dat = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = k.Datum.ToDateTime(TimeOnly.MinValue), Left = 380, Top = 12, Width = 120 };
        var von = ElementListe.Stunde(k.StundeVon);
        von.SetBounds(380, 40, 120, 23);
        var bis = ElementListe.Stunde(k.StundeBis);
        bis.SetBounds(380, 68, 120, 23);
        f.Controls.AddRange(new Control[]
        {
            dat, new Label { Text = "Datum", Left = 506, Top = 15, AutoSize = true }, von, new Label { Text = "Stunde von", Left = 506, Top = 43, AutoSize = true },
            bis, new Label { Text = "Stunde bis", Left = 506, Top = 71, AutoSize = true },
        });
        // Statistik-Kennzeichen wie im Handbuch (rechts unter „Stunde bis“): geht auf die Klausurzeilen der Vertretungsliste über
        var statistik = new TextBox { Text = k.Statistik, Left = 380, Top = 96, Width = 120 };
        f.Controls.AddRange(new Control[] { statistik, new Label { Text = "Statistik-Kennzeichen", Left = 506, Top = 99, AutoSize = true } });
        von.ValueChanged += (_, _) => { if (bis.Value < von.Value) bis.Value = von.Value; };
        bis.ValueChanged += (_, _) => { if (bis.Value < von.Value) von.Value = bis.Value; };

        var tabs = new TabControl { Left = 10, Top = 128, Width = 420, Height = 178 };
        var kurse = new TabPage("Kurse");
        var lr = new TabPage("Lehrer/Räume");
        tabs.TabPages.AddRange(new[] { kurse, lr });
        f.Controls.Add(tabs);
        // Reiter „Kurse“: der Unterricht, aus dem die Klausur entstand, und die Klassen
        var u = k.UnterrichtId is int uid ? d.Unterrichte.FirstOrDefault(x => x.Id == uid) : null;
        var kg = new DataGridView { Left = 4, Top = 4, Width = 404, Height = 70 };
        Ui.RasterGestalten(kg, false);
        kg.RowHeadersVisible = false;
        foreach (var (n, h, w) in new[] { ("U", "Unterricht", 70), ("Fa", "Fa.", 60), ("Le", "Le.", 90), ("St", "Studenten", 90) }) kg.Columns[kg.Columns.Add(n, h)].Width = w;
        if (u != null) kg.Rows.Add(u.Id, string.Join(",", u.FachIds.Select(x => ix.KFach(x))), string.Join(",", u.LehrerIds.Select(ix.KLehrer)), "");
        kurse.Controls.Add(kg);
        kurse.Controls.Add(new Label { Text = "Klasse(n):", Left = 4, Top = 86, AutoSize = true });
        var kl = new ElementWahl(ElementTyp.Klasse, k.KlassenIds) { Left = 80, Top = 82, Width = 324 };
        kurse.Controls.Add(kl);
        var ent = new CheckBox { Text = "Kollidierender Unterricht der Klassen entfällt", Checked = k.UnterrichtEntfaellt, Left = 4, Top = 114, AutoSize = true };
        kurse.Controls.Add(ent);
        // Reiter „Lehrer/Räume“: Aufsicht und Räume
        var lehrer = k.LehrerIds.ToList();
        var raeume = k.RaumIds.ToList();
        var lg = new DataGridView { Left = 4, Top = 4, Width = 404, Height = 140 };
        Ui.RasterGestalten(lg, false);
        lg.RowHeadersVisible = false;
        lg.Columns[lg.Columns.Add("L", "Lehrer")].Width = 190;
        lg.Columns[lg.Columns.Add("R", "Räume")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        void LehrerRaeume()
        {
            lg.Rows.Clear();
            for (var i = 0; i < Math.Max(1, Math.Max(lehrer.Count, raeume.Count)); i++)
                lg.Rows.Add(i < lehrer.Count ? ix.KLehrer(lehrer[i]) : "", i < raeume.Count ? ix.KRaum(raeume[i]) : "");
        }
        LehrerRaeume();
        lr.Controls.Add(lg);
        var akt = new CheckBox { Text = "Aktiv", Checked = k.Aktiv, Left = 14, Top = 314, AutoSize = true };
        f.Controls.Add(akt);

        // belegt zum Zeitpunkt der Klausur (rot in den Auswahllisten)
        HashSet<int> Belegt(bool raum)
        {
            var tp = Tagesplan.Berechnen(d, DateOnly.FromDateTime(dat.Value));
            var res = new HashSet<int>();
            for (var s = (int)von.Value; s <= (int)Math.Max(von.Value, bis.Value); s++)
                foreach (var e in tp.Stunde(s).Where(e => e.Findetstatt)) res.UnionWith(raum ? e.RaumIds : e.LehrerIds);
            return res;
        }
        Button Knopf(string text, int y, Action a)
        {
            var b = new Button { Text = text, Left = 446, Top = y + 24, Width = 160, Height = 26 };
            b.Click += (_, _) => { a(); tabs.SelectedTab = lr; LehrerRaeume(); };
            f.Controls.Add(b);
            return b;
        }
        var bL = Knopf("Lehrer", 126, () =>
        {
            var belegt = Belegt(false);
            var w = SondereinsatzAuswahl.Waehlen(f, ElementTyp.Lehrer, lehrer, kl.Ids, belegt.Contains);
            if (w != null) lehrer = w.Select(x => x.Id).ToList();
        });
        var bLk = Knopf("Lehrer aus Kurs", 156, () => { if (u != null) lehrer = u.LehrerIds.ToList(); });
        var bR = Knopf("Räume", 192, () =>
        {
            var belegt = Belegt(true);
            var w = SondereinsatzAuswahl.Waehlen(f, ElementTyp.Raum, raeume, Array.Empty<int>(), belegt.Contains);
            if (w != null) raeume = w.Select(x => x.Id).ToList();
        });
        var bRk = Knopf("Raum aus Kurs", 222, () =>
        {
            if (u is null) return;
            var tp = Tagesplan.Berechnen(d, DateOnly.FromDateTime(dat.Value));
            var r = tp.Stunde((int)von.Value).Where(e => (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == u.Id).SelectMany(e => e.RaumIds).Distinct().ToList();
            if (r.Count == 0) r = u.Zeilen.Select(z => z.StammraumId ?? z.FachraumId).OfType<int>().Distinct().ToList();
            raeume = r;
        });
        bLk.Enabled = bRk.Enabled = u != null;
        if (k.Importiert)
        {
            // aus UntPlanWeb übernommene Prüfung: nur Lehrkraft und Raum änderbar, keine Verlegung
            foreach (var c in new Control[] { name, dat, von, bis, kl, ent, tx }) c.Enabled = false;
            f.Text = "Klausur (aus UntPlanWeb – nur Lehrer und Raum änderbar)";
        }
        // Frage bei belegtem Raum (wie im Handbuch Kap. 11.2): nicht verplanen, mit Kollision, ohne Kollision (Raumvertretung)
        f.FormClosing += (_, ev) =>
        {
            if (f.DialogResult != DialogResult.OK || raeume.Count == 0) return;
            var probe = new Klausur
            {
                Id = k.Id, Datum = DateOnly.FromDateTime(dat.Value), StundeVon = (int)von.Value, StundeBis = (int)Math.Max(von.Value, bis.Value), KlassenIds = kl.Ids,
                RaumIds = raeume, UnterrichtId = k.UnterrichtId, UnterrichtEntfaellt = ent.Checked,
            };
            var belegt = Klausurraeume.Belegungen(d, probe);
            if (belegt.Count == 0) { k.RaumVerdraengen = false; return; }
            var text = "Kollision! " + string.Join("\n", belegt.GroupBy(b => b.RaumId).Select(gr =>
                $"Der Raum {ix.KRaum(gr.Key)} ist bereits verplant: " + string.Join(", ", gr.Select(b => $"{b.Stunde}. Std. {ix.KKlassen(b.Eintrag.KlassenIds)} {ix.KFach(b.Eintrag.FachId)}".Trim()).Distinct()) + "."));
            var w = OptionenDialog.Fragen(f, "Frage", text, new[] { "Raum nicht verplanen", "Raum mit Kollision verplanen", "Raum ohne Kollision verplanen (erzeugt Raumvertretung)" }, k.RaumVerdraengen ? 2 : 0);
            if (w is null) { ev.Cancel = true; return; }
            if (w == 0) raeume = raeume.Except(belegt.Select(b => b.RaumId)).ToList();
            k.RaumVerdraengen = w == 2;
        };
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        k.Statistik = statistik.Text.Trim();
        if (k.Importiert)
        {
            k.LehrerIds = lehrer;
            k.RaumIds = raeume;
            k.Aktiv = akt.Checked;
            return true;
        }
        k.Name = name.Text;
        k.Datum = DateOnly.FromDateTime(dat.Value);
        var alteSpanne = k.StundeBis - k.StundeVon;
        k.StundeVon = (int)von.Value;
        k.StundeBis = (int)Math.Max(von.Value, bis.Value);
        k.KlassenIds = kl.Ids;
        k.LehrerIds = lehrer;
        k.RaumIds = raeume;
        k.UnterrichtEntfaellt = ent.Checked;
        // wie im Handbuch: umfasst eine neue Klausur mehr Stunden als die gewählte, wird gefragt, ob kollidierender Unterricht entfällt
        if (neu && k.StundeBis - k.StundeVon > alteSpanne)
            k.UnterrichtEntfaellt = Ui.Frage("Sollen eventuell kollidierende Unterrichte aufgrund der Klausur entfallen?");
        k.Aktiv = akt.Checked;
        k.Text = tx.Text;
        return true;
    }
}

/// <summary>Veranstaltung (Exkursion, Klassenfahrt, Schulveranstaltung) eingeben.</summary>
public static class VeranstaltungDialog
{
    public static bool Zeigen(IWin32Window owner, Veranstaltung v)
    {
        var d = AppZustand.D;
        using var f = new Dialog("Veranstaltung", 480, 426);
        var y = 14;
        var tx = Ui.Zeile(f, "Bezeichnung:", new TextBox { Text = v.Text }, ref y, 140, 300);
        var von = Ui.Zeile(f, "von:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = v.Von.ToDateTime(TimeOnly.MinValue) }, ref y, 140, 110);
        var sv = ElementListe.Stunde(v.StundeVon);
        sv.Left = von.Right + 60;
        sv.Top = von.Top;
        f.Controls.Add(new Label { Text = "ab Std.", Left = von.Right + 12, Top = von.Top + 3, AutoSize = true });
        f.Controls.Add(sv);
        var bis = Ui.Zeile(f, "bis:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = v.Bis.ToDateTime(TimeOnly.MinValue) }, ref y, 140, 110);
        var sb = ElementListe.Stunde(Math.Min(v.StundeBis, d.Zeitraster.StundenProTag));
        sb.Left = bis.Right + 60;
        sb.Top = bis.Top;
        f.Controls.Add(new Label { Text = "bis Std.", Left = bis.Right + 12, Top = bis.Top + 3, AutoSize = true });
        f.Controls.Add(sb);
        von.ValueChanged += (_, _) => { if (bis.Value < von.Value) bis.Value = von.Value; };
        // Schnellauswahl (+): alle bzw. nach Abteilung/Jahrgang/Gruppe – bezogen auf die im Dialog eingetragenen Werte
        ElementWahl? kl = null, kt = null, le = null, ra = null;
        Veranstaltung Stand() => new()
        {
            Von = DateOnly.FromDateTime(von.Value), Bis = DateOnly.FromDateTime(bis.Value), StundeVon = (int)sv.Value,
            StundeBis = (int)sb.Value >= d.Zeitraster.StundenProTag ? 99 : (int)sb.Value,
            KlassenIds = kl?.Ids ?? new(), KlassenteilIds = kt?.Ids ?? new(), LehrerIds = le?.Ids ?? new(), RaumIds = ra?.Ids ?? new(),
        };
        Func<IEnumerable<ElementGruppe>> Gruppen(ElementTyp typ) => () => VeranstaltungsAuswahl.Gruppen(d, typ, Stand());
        kl = Ui.Zeile(f, "Klasse (entfällt):", new ElementWahl(ElementTyp.Klasse, v.KlassenIds, Gruppen(ElementTyp.Klasse)), ref y, 140, 300);
        kt = Ui.Zeile(f, "Kl-Teil (parallel):", new ElementWahl(ElementTyp.Klasse, v.KlassenteilIds, Gruppen(ElementTyp.Klasse)), ref y, 140, 300);
        le = Ui.Zeile(f, "Lehrer:", new ElementWahl(ElementTyp.Lehrer, v.LehrerIds, Gruppen(ElementTyp.Lehrer)), ref y, 140, 300);
        ra = Ui.Zeile(f, "Räume:", new ElementWahl(ElementTyp.Raum, v.RaumIds, Gruppen(ElementTyp.Raum)), ref y, 140, 300);
        var ausStunden = Ui.Zeile(f, "", new Button { Text = "Alle aus den Stunden übernehmen", AutoSize = true }, ref y, 140);
        ausStunden.Click += (_, _) =>
        {
            // Klassen, Lehrkräfte und Räume der regulären Stunden, die in die Veranstaltung fallen
            var e = VeranstaltungsAuswahl.AusStunden(d, Stand());
            if (e.Leer) { Ui.Info("In den Stunden der Veranstaltung findet kein passender Unterricht statt."); return; }
            var teil = kt.Ids.ToHashSet();
            kl.Hinzufuegen(e.KlassenIds.Where(k => !teil.Contains(k)));
            le.Hinzufuegen(e.LehrerIds);
            ra.Hinzufuegen(e.RaumIds);
        };
        var gr = Ui.Zeile(f, "Absenzgrund:", ElementListe.Combo(ElementTyp.Abwesenheitsgrund, v.GrundId), ref y, 140, 300);
        var sg = Ui.Zeile(f, "Schülergruppe:", new TextBox { Text = v.Schuelergruppe }, ref y, 140, 150);
        var z = Ui.Zeile(f, "", new CheckBox { Text = "Stunden der Lehrer positiv zählen", Checked = v.Zaehlen, AutoSize = true }, ref y, 140);
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        v.Text = tx.Text;
        v.Von = DateOnly.FromDateTime(von.Value);
        v.Bis = DateOnly.FromDateTime(bis.Value);
        v.StundeVon = (int)sv.Value;
        v.StundeBis = (int)sb.Value >= d.Zeitraster.StundenProTag ? 99 : (int)sb.Value;
        v.KlassenIds = kl.Ids;
        v.KlassenteilIds = kt.Ids.Except(v.KlassenIds).ToList();
        v.LehrerIds = le.Ids;
        v.RaumIds = ra.Ids;
        v.GrundId = ElementListe.Id(gr);
        v.Schuelergruppe = sg.Text;
        v.Zaehlen = z.Checked;
        return true;
    }
}

/// <summary>Eingabe einer Absenz (Lehrer, Klasse, Raum).</summary>
public static class AbwesenheitDialog
{
    public static bool Zeigen(IWin32Window owner, Abwesenheit a)
    {
        var d = AppZustand.D;
        var typ = a.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        using var f = new Dialog(a.Art switch { AbwesenheitsArt.Lehrer => "Lehrer-Absenz", AbwesenheitsArt.Klasse => "Klassen-Absenz", _ => "Raumsperrung" }, 450, a.Art == AbwesenheitsArt.Klasse ? 300 : 270);
        var y = 14;
        var element = Ui.Zeile(f, Ui.TypName(typ) + ":", ElementListe.Combo(typ, a.ElementId == 0 ? null : a.ElementId, false), ref y, 130, 280);
        var von = Ui.Zeile(f, "von:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.Von.ToDateTime(TimeOnly.MinValue) }, ref y, 130, 110);
        var sv = ElementListe.Stunde(a.StundeVon);
        sv.Left = von.Right + 60;
        sv.Top = von.Top;
        f.Controls.Add(new Label { Text = "ab Std.", Left = von.Right + 12, Top = von.Top + 3, AutoSize = true });
        f.Controls.Add(sv);
        var bis = Ui.Zeile(f, "bis:", new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.Bis.ToDateTime(TimeOnly.MinValue) }, ref y, 130, 110);
        var sb = ElementListe.Stunde(Math.Min(a.StundeBis, d.Zeitraster.StundenProTag));
        sb.Left = bis.Right + 60;
        sb.Top = bis.Top;
        f.Controls.Add(new Label { Text = "bis Std.", Left = bis.Right + 12, Top = bis.Top + 3, AutoSize = true });
        f.Controls.Add(sb);
        von.ValueChanged += (_, _) => { if (bis.Value < von.Value) bis.Value = von.Value; };
        var grund = Ui.Zeile(f, "Absenzgrund:", ElementListe.Combo(ElementTyp.Abwesenheitsgrund, a.GrundId), ref y, 130, 280);
        var text = Ui.Zeile(f, "Text:", new TextBox { Text = a.Text }, ref y, 130, 280);
        ElementWahl? begleit = null;
        if (a.Art == AbwesenheitsArt.Klasse) begleit = Ui.Zeile(f, "Begleitlehrer:", new ElementWahl(ElementTyp.Lehrer, a.BegleitLehrerIds), ref y, 130, 280);
        if (f.ShowDialog(owner) != DialogResult.OK || ElementListe.Id(element) is not int el) return false;
        a.ElementId = el;
        a.Von = DateOnly.FromDateTime(von.Value);
        a.Bis = DateOnly.FromDateTime(bis.Value);
        a.StundeVon = (int)sv.Value;
        a.StundeBis = (int)sb.Value >= d.Zeitraster.StundenProTag ? 99 : (int)sb.Value;
        a.GrundId = ElementListe.Id(grund);
        a.Text = text.Text;
        if (begleit != null) a.BegleitLehrerIds = begleit.Ids;
        if (string.IsNullOrEmpty(a.Benutzer)) a.Benutzer = AppZustand.Benutzer;
        return true;
    }
}

/// <summary>Tagestexte (Nachrichten zum Tag) für Schüler, Lehrer oder bestimmte Klassen.</summary>
public static class TagestextDialog
{
    private static readonly string[] Arten = { "Alle", "Klassen", "Lehrer", "Nur Lehrer" };

    public static void Zeigen(IWin32Window owner, DateOnly datum)
    {
        var d = AppZustand.D;
        using var f = new Dialog($"Nachrichten zum Tag – {Format.Datum(datum)}", 720, 360);
        var g = new DataGridView { Left = 10, Top = 10, Width = 700, Height = 300 };
        Ui.RasterGestalten(g, true);
        g.AllowUserToAddRows = true;
        g.AllowUserToDeleteRows = true;
        // Spalten wie im Handbuch: Art | Elemente | gültig bis [Tage] | Tagestext | Webportal
        var art = new DataGridViewComboBoxColumn { Name = "A", HeaderText = "Art", Width = 90, FlatStyle = FlatStyle.Flat };
        art.Items.AddRange(Arten);
        g.Columns.Add(art);
        g.Columns[g.Columns.Add("E", "Elemente")].Width = 110;
        g.Columns[g.Columns.Add("T", "Tagestext")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        g.Columns[g.Columns.Add("G", "gültig bis [Tage]")].Width = 90;
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "P", HeaderText = "Webportal", Width = 64 });
        g.Columns["G"]!.DisplayIndex = 2;
        var ix = AppZustand.Doc.Index;
        foreach (var t in d.Tagestexte.Where(t => t.Datum == datum))
        {
            var a = t.NurLehrer ? TagestextArt.NurLehrer : t.Art;
            var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            var zr = g.Rows.Add(Arten[(int)a], string.Join(",", t.ElementIds.Select(i => ix.K(typ, i))), t.Text, t.GueltigTage, t.Portal);
            g.Rows[zr].Tag = t;
        }
        g.DefaultValuesNeeded += (_, e) => { e.Row.Cells["A"].Value = Arten[0]; e.Row.Cells["G"].Value = 1; e.Row.Cells["P"].Value = true; };
        f.Controls.Add(g);
        if (f.ShowDialog(owner) != DialogResult.OK) return;
        var neu = new List<Tagestext>();
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow || string.IsNullOrWhiteSpace(r.Cells["T"].Value?.ToString())) continue;
            var a = (TagestextArt)Math.Max(0, Array.IndexOf(Arten, r.Cells["A"].Value?.ToString() ?? "Alle"));
            var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            var ids = (r.Cells["E"].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => d.AlleElemente(typ).FirstOrDefault(e => e.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id).Where(i => i.HasValue).Select(i => i!.Value).ToList();
            var text = r.Cells["T"].Value!.ToString()!;
            // formatierte Texte (Dialog „Tagestexte“) behalten ihre Formatierung, solange der Text unverändert ist
            var alt = r.Tag as Tagestext;
            var formatiert = alt != null && alt.Text == text && alt.TextHtml.Length > 0;
            neu.Add(new Tagestext
            {
                Datum = datum, Art = a, Text = text, ElementIds = ids, TextRtf = formatiert ? alt!.TextRtf : "", TextHtml = formatiert ? alt!.TextHtml : "",
                GueltigTage = int.TryParse(r.Cells["G"].Value?.ToString(), out var gt) ? Math.Max(1, gt) : 1, Portal = r.Cells["P"].Value is true,
            });
        }
        AppZustand.Doc.Aendern("Nachrichten zum Tag", AenderungsArt.Vertretung, x =>
        {
            x.Tagestexte.RemoveAll(t => t.Datum == datum);
            x.Tagestexte.AddRange(neu);
        });
    }
}

/// <summary>
/// Einstellungen der Vertretungsplanung wie im Handbuch (Dialog „Einstellungen“, Zweig „Vertretungsplanung“): links der Baum mit
/// Vtr-Zähler, Sonstiges, Farben und Bezeichnungen sowie Vertretungszeitraster und Vertretungsvorschlag, rechts die gewählte Seite.
/// </summary>
public static class VertretungsEinstellungenDialog
{
    private static readonly string[] RegelText = { "", "E", "EVA" };

    /// <summary>Seiten im Baum (Reihenfolge wie im Handbuch, danach die zusätzlichen Seiten).</summary>
    private static readonly string[] Reihenfolge = { "Vtr-Zähler", "Absenzkopf", "Sonstiges", "Farben und Bezeichnungen", "Vertretungsnachrichten", "Vertretungszeitraster", "Vertretungsvorschlag" };

    /// <summary>Kursiv: lokal (am Arbeitsplatz) gespeicherte Einstellung – sie steht nicht in der Schuldatei.</summary>
    private static readonly Font Lokal = new(Ui.Schrift, FontStyle.Italic);

    /// <summary>Selbsttest: Knoten des Baums (Text, kursiv) des zuletzt geöffneten Dialogs.</summary>
    internal static IReadOnlyList<(string Text, bool Kursiv)> LetzteKnoten { get; private set; } = Array.Empty<(string, bool)>();

    /// <param name="seite">0 = Vertretungszeitraster, 1 = Vtr-Zähler, 2 = Vertretungsvorschlag, 3 = Absenzkopf, 4 = Vertretungsnachrichten; ohne Angabe die erste Seite (Vtr-Zähler).</param>
    public static bool Zeigen(IWin32Window owner, int seite = -1)
    {
        using var f = Erzeugen(seite, out var uebernehmen);
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        uebernehmen();
        return true;
    }

    /// <summary>Selbsttest: Dialog nicht modal öffnen (Ok übernimmt die Einstellungen).</summary>
    internal static Form Anzeigen(IWin32Window owner, int seite)
    {
        var f = Erzeugen(seite, out var uebernehmen);
        f.FormClosed += (_, _) => { if (f.DialogResult == DialogResult.OK) uebernehmen(); };
        f.Show(owner);
        return f;
    }

    private static Dialog Erzeugen(int seite, out Action uebernehmen)
    {
        var d = AppZustand.D;
        var e = System.Text.Json.JsonSerializer.Deserialize<VertretungsEinstellungen>(System.Text.Json.JsonSerializer.Serialize(d.Vertretungseinstellungen))!;
        var klassenRaster = d.Klassen.ToDictionary(k => k.Id, k => new Dictionary<int, VertretungsRegel>(k.VertretungsZeitraster));
        var f = new Dialog("Einstellungen", 856, 470);
        f.Ok.Text = "Ok";
        var baum = new TreeView { Left = 8, Top = 8, Width = 192, Height = 410, HideSelection = false, ShowLines = true };
        var host = new Panel { Left = 206, Top = 8, Width = 642, Height = 410, BackColor = Color.FromArgb(232, 240, 228) };
        f.Controls.Add(baum);
        f.Controls.Add(host);
        f.Controls.Add(new Label { Text = "Kursiv = lokal gespeicherte Einstellungen (.ini Dateien)", Left = 8, Top = 438, AutoSize = true, Font = new Font(Ui.Schrift, FontStyle.Italic) });
        var seiten = new Dictionary<string, Panel>();
        Panel Seite(string name)
        {
            var p = new Panel { Dock = DockStyle.Fill, Visible = false, AutoScroll = true, BackColor = host.BackColor, Name = name };
            seiten[name] = p;
            host.Controls.Add(p);
            return p;
        }

        // --- Vertretungszeitraster ---------------------------------------------------------------
        var tz = Seite("Vertretungszeitraster");
        var klasse = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 10, Width = 200 };
        klasse.Items.Add("(gesamte Schule)");
        foreach (var k in Stammdaten.Sortiert(d.Klassen)) klasse.Items.Add(k);
        klasse.Format += (_, ev) => ev.Value = ev.ListItem is Klasse k ? k.Kurzname : ev.ListItem?.ToString();
        klasse.SelectedIndex = 0;
        var raster = new DataGridView { Left = 10, Top = 42, Width = 620, Height = 300 };
        Ui.RasterGestalten(raster, false);
        raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        raster.Columns[raster.Columns.Add("S", "Std.")].Width = 50;
        for (var t = 0; t < d.Zeitraster.TageProWoche; t++) raster.Columns[raster.Columns.Add("T" + t, Planungsdienst.Tagname(d, t))].Width = 80;
        Dictionary<int, VertretungsRegel> Aktuell() => klasse.SelectedItem is Klasse kk ? klassenRaster[kk.Id] : e.Zeitraster;
        void RasterFuellen()
        {
            raster.Rows.Clear();
            var r = Aktuell();
            for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
            {
                var werte = new object[1 + d.Zeitraster.TageProWoche];
                werte[0] = s;
                for (var t = 0; t < d.Zeitraster.TageProWoche; t++) werte[1 + t] = r.TryGetValue(t * 100 + s, out var w) ? RegelText[(int)w] : "";
                raster.Rows.Add(werte);
            }
        }
        raster.CellClick += (_, ev) =>
        {
            if (ev.RowIndex < 0 || ev.ColumnIndex < 1) return;
            var key = (ev.ColumnIndex - 1) * 100 + ev.RowIndex + 1;
            var r = Aktuell();
            var neu = (VertretungsRegel)(((int)(r.TryGetValue(key, out var w) ? w : VertretungsRegel.Vertreten) + 1) % 3);
            if (neu == VertretungsRegel.Vertreten) r.Remove(key); else r[key] = neu;
            raster.Rows[ev.RowIndex].Cells[ev.ColumnIndex].Value = RegelText[(int)neu];
        };
        raster.CellFormatting += (_, ev) =>
        {
            if (ev.ColumnIndex < 1 || ev.CellStyle is null) return;
            ev.CellStyle.BackColor = (ev.Value as string) switch { "E" => Color.FromArgb(255, 210, 210), "EVA" => Color.FromArgb(255, 240, 190), _ => Color.White };
        };
        klasse.SelectedIndexChanged += (_, _) => RasterFuellen();
        tz.Controls.Add(new Label { Text = "Zeitraster für:", Left = 10, Top = 13, AutoSize = true });
        tz.Controls.Add(klasse);
        tz.Controls.Add(raster);
        tz.Controls.Add(new Label
        {
            Text = "Klick wechselt: leer = vertreten · E = Unterricht entfällt bei Absenz des Lehrers · EVA = eigenverantwortliches Arbeiten.\nEin Klassenzeitraster hat Vorrang vor dem Zeitraster der Schule.",
            Left = 10, Top = 350, Width = 620, Height = 40, ForeColor = Color.DimGray,
        });
        RasterFuellen();

        // --- Vtr-Zähler (wie im Handbuch: Was zählen, Zeitraum, Wie zählen, Nicht zählen, Statt-Vertretungen und Klausuren) ------
        var tzae = Seite("Vtr-Zähler");
        var z = e.Zaehler;
        GroupBox Gruppe(string text, int x, int y0, int w, int h)
        {
            var g = new GroupBox { Text = text, Left = x, Top = y0, Width = w, Height = h };
            tzae.Controls.Add(g);
            return g;
        }
        CheckBox Haken(Control g, string text, bool an, int y0) { var c = new CheckBox { Text = text, Checked = an, Left = 10, Top = y0, AutoSize = true }; g.Controls.Add(c); return c; }
        RadioButton Option(Control g, string text, bool an, int y0) { var r = new RadioButton { Text = text, Checked = an, Left = 10, Top = y0, AutoSize = true }; g.Controls.Add(r); return r; }
        var gWas = Gruppe("Was zählen", 8, 6, 290, 150);
        var c1 = Haken(gWas, "Vertretungen", z.VertretungenZaehlen, 20);
        var c2 = Haken(gWas, "Sondereins.", z.SondereinsaetzeZaehlen, 41);
        var c3 = Haken(gWas, "Entfälle", z.EntfaelleZaehlen, 62);
        var c4 = Haken(gWas, "Freisetzungen", z.FreisetzungenZaehlen, 83);
        var c6 = Haken(gWas, "Betreuungen halb zählen", z.BetreuungenHalb, 104);
        var c5 = Haken(gWas, "Pausenaufsichten", z.PausenaufsichtenZaehlen, 125);
        var gZeit = Gruppe("Zeitraum", 310, 6, 300, 150);
        var halbjahr = z.Zeitraum == ZaehlZeitraum.Halbjahr;
        var rTag = Option(gZeit, "Tag", z.Zeitraum == ZaehlZeitraum.Tag, 18);
        var rWoche = Option(gZeit, "Woche", z.Zeitraum == ZaehlZeitraum.Woche, 38);
        var rMonat = Option(gZeit, "Monat", z.Zeitraum == ZaehlZeitraum.Monat, 58);
        var rJahr = Option(gZeit, "Jahr", z.Zeitraum == ZaehlZeitraum.Schuljahr, 78);
        var rZeitraum = Option(gZeit, "Zeitraum", z.Zeitraum is ZaehlZeitraum.Benutzerdefiniert or ZaehlZeitraum.Halbjahr, 98);
        // ein früher gewähltes Halbjahr erscheint als selbst gewählter Zeitraum (bleibt erhalten, solange die Daten nicht geändert werden)
        var (hjVon, hjBis) = halbjahr ? Zaehlung.Zeitraum(d, AppZustand.Datum) : (z.ZeitraumVon ?? AppZustand.Datum, z.ZeitraumBis ?? AppZustand.Datum);
        var zVon = new DateTimePicker { Format = DateTimePickerFormat.Short, Left = 10, Top = 122, Width = 110, Value = hjVon.ToDateTime(TimeOnly.MinValue) };
        var zBis = new DateTimePicker { Format = DateTimePickerFormat.Short, Left = 130, Top = 122, Width = 110, Value = hjBis.ToDateTime(TimeOnly.MinValue) };
        gZeit.Controls.AddRange(new Control[] { zVon, zBis });
        void ZeitraumFreigeben() => zVon.Enabled = zBis.Enabled = rZeitraum.Checked;
        rZeitraum.CheckedChanged += (_, _) => ZeitraumFreigeben();
        ZeitraumFreigeben();
        var gWie = Gruppe("Wie zählen", 8, 162, 290, 110);
        var wAnzahl = Option(gWie, "Anzahl", z.WieZaehlen == ZaehlArt.Anzahl, 20);
        var wWerte = Option(gWie, "Werte", z.WieZaehlen == ZaehlArt.Werte, 41);
        var wHhMm = Option(gWie, "Stunden (HH:MM)", z.WieZaehlen == ZaehlArt.StundenHhMm, 62);
        var wReal = Option(gWie, "Realstunden", z.WieZaehlen == ZaehlArt.Realstunden, 83);
        var gNicht = Gruppe("Nicht zählen", 310, 162, 300, 70);
        var kn = new TextBox { Text = z.NichtZaehlenKennzeichen, Left = 10, Top = 20, Width = 34 };
        gNicht.Controls.Add(kn);
        gNicht.Controls.Add(new Label { Text = "Statistik-Kennzeichen", Left = 52, Top = 23, AutoSize = true });
        var bezahltAlt = z.BezahltKennzeichen.Length > 0 && z.BezahltKennzeichen == z.NichtZaehlenKennzeichen;
        var kb = Haken(gNicht, "bezahlte Vertretungen", bezahltAlt, 44);
        kb.Left = 52;
        var tip = new ToolTip();
        tip.SetToolTip(kb, $"Vertretungen mit diesem Statistik-Kennzeichen gelten als bezahlt (Vertretungsvorschlag, Vertretungsabrechnung)." +
                           (z.BezahltKennzeichen.Length > 0 && !bezahltAlt ? $"\nKennzeichen für bezahlte Vertretungen bisher: „{z.BezahltKennzeichen}“." : ""));
        void BezahltFreigeben() => kb.Enabled = kn.Text.Trim().Length > 0;
        kn.TextChanged += (_, _) => BezahltFreigeben();
        BezahltFreigeben();
        var gStatt = Gruppe("Statt-Vertretungen und Klausuren", 310, 238, 300, 64);
        var c8 = Haken(gStatt, "Statt-Vertretungen nicht zählen", z.StattVertretungNeutral, 18);
        var c9 = Haken(gStatt, "Klausuren neutral zählen", z.KlausurenNeutral, 39);
        var c7 = new CheckBox { Text = "Entfallszählung nur für Absenzen mit Absenzgrund", Checked = z.EntfallNurMitGrund, Left = 14, Top = 312, AutoSize = true };
        tzae.Controls.Add(c7);
        var y = 12;

        // --- Vertretungsvorschlag ----------------------------------------------------------------
        var tv = Seite("Vertretungsvorschlag");
        var vo = e.Vorschlag;
        y = 12;
        NumericUpDown Zahl(string text, int wert, int max = 99) => Ui.Zeile(tv, text, new NumericUpDown { Minimum = 0, Maximum = max, Value = Math.Clamp(wert, 0, max) }, ref y, 300, 70);
        tv.Controls.Add(new Label { Text = "Bonus (je höher, desto eher wird der Lehrer vorgeschlagen):", Left = 10, Top = y, AutoSize = true, Font = Ui.SchriftFett });
        y += 22;
        var nFrei = Zahl("Freisetzung (Klasse des Lehrers abwesend)", vo.Freisetzung);
        var nBer = Zahl("Bereitschaft", vo.Bereitschaft);
        var nKl = Zahl("Lehrer unterrichtet die Klasse", vo.HatKlasse);
        var nFa = Zahl("Lehrer unterrichtet das Fach", vo.HatFach);
        tv.Controls.Add(new Label { Text = "Schlechtpunkte (Faktoren):", Left = 10, Top = y, AutoSize = true, Font = Ui.SchriftFett });
        y += 22;
        var nStd = Zahl("je Unterrichtsstunde am Tag", vo.UnterrichtsstundenAmTag);
        var nMe = Zahl("je Merkerstufe (Abstand zum eigenen Unterricht)", vo.MerkerFaktor);
        var nSp = Zahl("je Stufe Sperrvermerk", vo.SperreFaktor);
        var nZa = Zahl("je Punkt im Vertretungszähler", vo.ZaehlerFaktor);
        tv.Controls.Add(new Label { Text = "Anzeige:", Left = 10, Top = y, AutoSize = true, Font = Ui.SchriftFett });
        y += 22;
        var nRm = Zahl("rot, wenn Merker größer als", vo.RotWennMerkerGroesser, 9);
        var nNa = Zahl("nicht anzeigen, wenn Merker größer als", vo.NichtAnzeigenWennMerkerGroesser, 9);
        var nRz = Zahl("rot, wenn Zähler größer als", vo.RotWennZaehlerGroesser);
        var cGes = new CheckBox { Text = "Gesperrte (Zeitwunsch -3) nicht anzeigen", Checked = vo.GesperrteNichtAnzeigen, AutoSize = true, Left = 400, Top = 34 };
        var cTeil = new CheckBox { Text = "Klasse auch für Klassenteil", Checked = vo.KlasseAuchFuerKlassenteil, AutoSize = true, Left = 400, Top = 58 };
        var cRg = new CheckBox { Text = "Raumgewicht 4 bei Vorziehungen", Checked = vo.Raumgewicht4Beachten, AutoSize = true, Left = 400, Top = 82 };
        var cSp = new CheckBox { Text = "Vorziehung nur aus späterem Unterricht", Checked = vo.NurSpaeterenUnterricht, AutoSize = true, Left = 400, Top = 106 };
        var cWo = new CheckBox { Text = "Vorziehung nur aus derselben Woche", Checked = vo.NurDieselbeWoche, AutoSize = true, Left = 400, Top = 130 };
        var vz = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 400, Top = 176, Width = 230 };
        vz.Items.AddRange(new object[] { "gemäß Vertretungszeitraster", "aus Randstunden", "alle Verlegungen" });
        vz.SelectedIndex = (int)vo.Vorziehungen;
        tv.Controls.AddRange(new Control[] { cGes, cTeil, cRg, cSp, cWo, new Label { Text = "Vorziehungen:", Left = 400, Top = 158, AutoSize = true }, vz });

        // --- Sonstiges ---------------------------------------------------------------------------
        var ta = Seite("Sonstiges");
        y = 12;
        var eva = Ui.Zeile(ta, "Raum für EVA:", ElementListe.Combo(ElementTyp.Raum, e.EvaRaumId), ref y, 260, 200);
        var lrp = Ui.Zeile(ta, "", new CheckBox { Text = "Lehrer-Raum-Prinzip (Raum des Vertreters verwenden)", Checked = e.LehrerRaumPrinzip, AutoSize = true }, ref y, 10);
        var fpa = Ui.Zeile(ta, "", new CheckBox { Text = "Freisetzungen erzeugen Pausenaufsicht-Vertretungen", Checked = e.FreisetzungPausenaufsicht, AutoSize = true }, ref y, 10);
        var mad = Ui.Zeile(ta, "", new CheckBox { Text = "Vertretungsplanung mit aktuellem Datum starten", Checked = e.MitAktuellemDatumStarten, AutoSize = true }, ref y, 10);
        var mag = Ui.Zeile(ta, "Mehrarbeitsgrenze je Monat (Vollzeit):", new NumericUpDown { Minimum = 0, Maximum = 20, Value = Math.Clamp(d.Einstellungen.Mehrarbeitsgrenze, 0, 20) }, ref y, 260, 70);
        var eab = Ui.Zeile(ta, "Entfall sinnvoll ab Jahrgang:", new TextBox { Text = d.Einstellungen.EntfallAbJahrgang }, ref y, 260, 70);
        ta.Controls.Add(new Label
        {
            Text = "NRW: Mehrarbeit ist vergütungsfähig, wenn im Kalendermonat mehr als die Mehrarbeitsgrenze (bei Teilzeit anteilig)\ngeleistet wurde – dann sind alle Stunden vergütungsfähig. Minderstunden (Entfall/Freisetzung) werden verrechnet.",
            Left = 10, Top = y + 4, Width = 620, Height = 40, ForeColor = Color.DimGray,
        });
        y += 50;
        // Tageslisten (Handbuch Kap. 13.3) – lokal am Arbeitsplatz gespeichert (kursiv)
        var lokal = AppZustand.Einstellungen;
        var tlPdf = Ui.Zeile(ta, "", new CheckBox { Text = "Tageslisten automatisch als PDF ausgeben", Checked = lokal.TageslistenPdf, AutoSize = true, Font = Lokal }, ref y, 10);
        var tlName = Ui.Zeile(ta, "", new CheckBox { Text = "Benutzerdefinierte Dateinamen", Checked = lokal.TageslistenEigeneDateinamen, AutoSize = true, Font = Lokal }, ref y, 10);
        var tlOrdner = new TextBox { Text = lokal.TageslistenPdfOrdner, Left = 150, Top = y, Width = 380, Font = Lokal };
        var tlWahl = new Button { Text = "…", Left = 534, Top = y - 1, Width = 30, Height = 24 };
        tlWahl.Click += (_, _) =>
        {
            using var fb = new FolderBrowserDialog { Description = "Ordner für die PDF-Tageslisten", UseDescriptionForTitle = true, SelectedPath = tlOrdner.Text };
            if (fb.ShowDialog(f) == DialogResult.OK) tlOrdner.Text = fb.SelectedPath;
        };
        ta.Controls.AddRange(new Control[] { new Label { Text = "PDF-Ordner:", Left = 10, Top = y + 3, AutoSize = true, Font = Lokal }, tlOrdner, tlWahl });
        void PdfFreigeben() => tlName.Enabled = tlOrdner.Enabled = tlWahl.Enabled = tlPdf.Checked;
        tlPdf.CheckedChanged += (_, _) => PdfFreigeben();
        PdfFreigeben();

        // --- Absenzkopf (Handbuch Kap. 13.2.9 und 13.2.10): Absenz/Vertretung je Elementart und globale Einstellungen ---------------
        var tk = Seite("Absenzkopf");
        var dr = AppZustand.Einstellungen.Vertretungsdruck;
        var gAk = new GroupBox { Text = "Absenzkopf", Left = 10, Top = 8, Width = 300, Height = 140 };
        tk.Controls.Add(gAk);
        gAk.Controls.Add(new Label { Text = "Absenz", Left = 92, Top = 18, AutoSize = true });
        gAk.Controls.Add(new Label { Text = "Vertretung", Left = 172, Top = 18, AutoSize = true });
        CheckBox AkHaken(string text, bool an, int x, int y0, int w = 18) { var c = new CheckBox { Text = text, Checked = an, Left = x, Top = y0, Width = w + (text.Length > 0 ? 120 : 0), Height = 20 }; gAk.Controls.Add(c); return c; }
        gAk.Controls.Add(new Label { Text = "Lehrer", Left = 10, Top = 42, AutoSize = true });
        var akAbsL = AkHaken("", dr.AbwesendeLehrer, 104, 40);
        var akVtrL = AkHaken("", dr.VertretungLehrer, 192, 40);
        var akGrund = AkHaken("mit Absenzgrund", dr.LehrerMitAbsenzgrund, 104, 62);
        gAk.Controls.Add(new Label { Text = "Klassen", Left = 10, Top = 88, AutoSize = true });
        var akAbsK = AkHaken("", dr.AbwesendeKlassen, 104, 86);
        var akVtrK = AkHaken("", dr.VertretungKlassen, 192, 86);
        gAk.Controls.Add(new Label { Text = "Räume", Left = 10, Top = 110, AutoSize = true });
        var akAbsR = AkHaken("", dr.BlockierteRaeume, 104, 108);
        var akVtrR = AkHaken("", dr.VertretungRaeume, 192, 108);
        var gAkG = new GroupBox { Text = "Global", Left = 320, Top = 8, Width = 300, Height = 230 };
        tk.Controls.Add(gAkG);
        var akWert = new Label { Text = dr.AbsenzkopfGroesse.ToString(), Left = 10, Top = 20, Width = 40 };
        var akRegler = new TrackBar { Minimum = 25, Maximum = 200, SmallChange = 5, LargeChange = 25, TickFrequency = 25, Value = Math.Clamp(dr.AbsenzkopfGroesse, 25, 200), Left = 6, Top = 40, Width = 286, Height = 40 };
        akRegler.ValueChanged += (_, _) => akWert.Text = akRegler.Value.ToString();
        gAkG.Controls.AddRange(new Control[] { akWert, new Label { Text = "Schriftgröße in %", Left = 180, Top = 20, Width = 110, TextAlign = ContentAlignment.TopRight }, akRegler });
        CheckBox GHaken(Control p, string text, bool an, int y0) { var c = new CheckBox { Text = text, Checked = an, Left = 10, Top = y0, AutoSize = true }; p.Controls.Add(c); return c; }
        var akRahmen = GHaken(gAkG, "Ausdruck mit Rahmen", dr.AbsenzkopfRahmen, 84);
        var akVa = GHaken(gAkG, "Absenzkopf nicht für Veranstaltungen", dr.AbsenzkopfNichtVeranstaltungen, 106);
        var gLang = new GroupBox { Text = "Langname", Left = 8, Top = 132, Width = 284, Height = 88 };
        gAkG.Controls.Add(gLang);
        var akLangL = GHaken(gLang, "Lehrer", dr.AbsenzkopfLangLehrer, 18);
        var akLangK = GHaken(gLang, "Klassen", dr.AbsenzkopfLangKlassen, 40);
        var akLangR = GHaken(gLang, "Räume", dr.AbsenzkopfLangRaeume, 62);
        tk.Controls.Add(new Label
        {
            Text = "Der Absenzkopf zeigt im Ausdruck die abwesenden Lehrer, Klassen und Räume sowie (Spalte „Vertretung“) die betroffenen Elemente.\n" +
                   "Absenzgründe mit „N. in Abs.kopf“ erscheinen nicht. Die Einstellungen gelten auch im Seitenlayout.",
            Left = 10, Top = 248, Width = 610, Height = 48, ForeColor = Color.DimGray,
        });

        // --- Vertretungsnachrichten: Vorlage der Nachricht an die Lehrkräfte ---------------------------------------------------
        var tn = Seite("Vertretungsnachrichten");
        var na = e.Nachrichten ??= new NachrichtenEinstellungen();
        y = 12;
        var nBetreff = Ui.Zeile(tn, "Betreff:", new TextBox { Text = na.Betreff }, ref y, 110, 500);
        tn.Controls.Add(new Label { Text = "Text:", Left = 10, Top = y + 3, AutoSize = true });
        var nText = new TextBox { Text = na.Text.Replace("\n", "\r\n"), Left = 120, Top = y, Width = 500, Height = 130, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        tn.Controls.Add(nText);
        y += 138;
        var nZeile = Ui.Zeile(tn, "Je Vertretung:", new TextBox { Text = na.Zeile }, ref y, 110, 500);
        var nEntf = Ui.Zeile(tn, "", new CheckBox { Text = "Entfälle an die betroffene Lehrkraft melden", Checked = na.EntfaelleMelden, AutoSize = true }, ref y, 120);
        var nAbw = Ui.Zeile(tn, "", new CheckBox { Text = "Abwesende Lehrkräfte über ihren vertretenen Unterricht informieren", Checked = na.AbwesendeInformieren, AutoSize = true }, ref y, 120);
        var nNur = Ui.Zeile(tn, "", new CheckBox { Text = "Nur seit der letzten Nachricht geänderte Vertretungen senden", Checked = lokal.NachrichtenNurGeaenderte, AutoSize = true, Font = Lokal }, ref y, 120);
        tn.Controls.Add(new Label
        {
            Text = "Platzhalter: {Name}, {Kurzname}, {Datum}, {Zeilen}, {Anzahl} – je Vertretung: {Tag}, {Datum}, {Stunde}, {Art}, {Klassen}, {Fach}, {Raum}, {Lehrer}, {Text}.\n" +
                   "Versand: Vertretungsplanung › Ausgabe › Vertretungsnachrichten (E-Mail-Adresse in den Stammdaten der Lehrer).",
            Left = 10, Top = y + 4, Width = 620, Height = 44, ForeColor = Color.DimGray,
        });

        // --- Farben und Bezeichnungen ------------------------------------------------------------
        var tb = Seite("Farben und Bezeichnungen");
        var bg = new DataGridView { Left = 10, Top = 10, Width = 620, Height = 390 };
        Ui.RasterGestalten(bg, true);
        bg.Columns[bg.Columns.Add("A", "Vertretungsart")].ReadOnly = true;
        bg.Columns[0].Width = 180;
        bg.Columns[bg.Columns.Add("S", "Standard")].ReadOnly = true;
        bg.Columns[1].Width = 160;
        bg.Columns[bg.Columns.Add("B", "Bezeichnung")].Width = 160;
        bg.Columns[bg.Columns.Add("F", "Farbe")].ReadOnly = true;
        bg.Columns[3].Width = 80;
        foreach (var art in Enum.GetValues<VertretungsArt>())
        {
            var r = bg.Rows.Add(art.ToString(), Vertretungsplan.ArtText(art), e.Bezeichnungen.GetValueOrDefault(art.ToString(), ""), "");
            bg.Rows[r].Tag = art;
            if (e.Farben.TryGetValue(art.ToString(), out var fa)) bg.Rows[r].Cells[3].Style.BackColor = Ui.Farbe(fa, Color.White);
        }
        bg.CellDoubleClick += (_, ev) =>
        {
            if (ev.RowIndex < 0 || ev.ColumnIndex != 3 || bg.Rows[ev.RowIndex].Tag is not VertretungsArt art) return;
            using var cd = new ColorDialog { FullOpen = true };
            if (cd.ShowDialog(f) == DialogResult.OK)
            {
                e.Farben[art.ToString()] = cd.Color.ToArgb() & 0xFFFFFF;
                bg.Rows[ev.RowIndex].Cells[3].Style.BackColor = cd.Color;
            }
            else if (Ui.Frage("Farbe entfernen?"))
            {
                e.Farben.Remove(art.ToString());
                bg.Rows[ev.RowIndex].Cells[3].Style.BackColor = Color.White;
            }
        };
        tb.Controls.Add(bg);

        // Baum wie im Handbuch: Vertretungsplanung mit den Unterseiten. Kursiv = nur lokal gespeicherte Einstellungen (alle Steuerelemente
        // der Seite in kursiver Schrift); Seiten mit lokalen und gemeinsamen Einstellungen zeigen die lokalen kursiv.
        var wurzel = baum.Nodes.Add("Vertretungsplanung");
        static IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Alle(x)));
        foreach (var name in Reihenfolge)
        {
            var knoten = wurzel.Nodes.Add(name, name);
            var eingaben = Alle(seiten[name]).Where(c => c is CheckBox or TextBox or ComboBox or NumericUpDown or DateTimePicker or TrackBar or DataGridView or RadioButton).ToList();
            if (eingaben.Count > 0 && eingaben.All(c => ReferenceEquals(c.Font, Lokal) || c.Font.Italic)) knoten.NodeFont = Lokal;
        }
        wurzel.Expand();
        LetzteKnoten = wurzel.Nodes.Cast<TreeNode>().Select(n => (n.Text, n.NodeFont?.Italic == true)).ToList();
        baum.AfterSelect += (_, ev) =>
        {
            var name = ev.Node?.Parent is null ? Reihenfolge[0] : ev.Node.Text;
            foreach (var (n, p) in seiten) p.Visible = n == name;
        };
        baum.BeforeCollapse += (_, ev) => ev.Cancel = true;
        var start = seite switch { 0 => "Vertretungszeitraster", 2 => "Vertretungsvorschlag", 3 => "Absenzkopf", 4 => "Vertretungsnachrichten", _ => "Vtr-Zähler" };
        baum.SelectedNode = wurzel.Nodes[start];

        uebernehmen = () =>
        {
            z.VertretungenZaehlen = c1.Checked;
            z.SondereinsaetzeZaehlen = c2.Checked;
            z.EntfaelleZaehlen = c3.Checked;
            z.FreisetzungenZaehlen = c4.Checked;
            z.PausenaufsichtenZaehlen = c5.Checked;
            z.BetreuungenHalb = c6.Checked;
            z.EntfallNurMitGrund = c7.Checked;
            z.StattVertretungNeutral = c8.Checked;
            z.KlausurenNeutral = c9.Checked;
            var von = DateOnly.FromDateTime(zVon.Value);
            var bis = DateOnly.FromDateTime(zBis.Value);
            if (rTag.Checked) z.Zeitraum = ZaehlZeitraum.Tag;
            else if (rWoche.Checked) z.Zeitraum = ZaehlZeitraum.Woche;
            else if (rMonat.Checked) z.Zeitraum = ZaehlZeitraum.Monat;
            else if (rJahr.Checked) z.Zeitraum = ZaehlZeitraum.Schuljahr;
            else if (!(halbjahr && von == hjVon && bis == hjBis))
            {
                z.Zeitraum = ZaehlZeitraum.Benutzerdefiniert;
                z.ZeitraumVon = von <= bis ? von : bis;
                z.ZeitraumBis = von <= bis ? bis : von;
            }
            z.WieZaehlen = wWerte.Checked ? ZaehlArt.Werte : wHhMm.Checked ? ZaehlArt.StundenHhMm : wReal.Checked ? ZaehlArt.Realstunden : ZaehlArt.Anzahl;
            var kennz = kn.Text.Trim();
            z.NichtZaehlenKennzeichen = kennz;
            // ein Kennzeichen wie im Handbuch: „bezahlte Vertretungen“ macht es zugleich zum Kennzeichen bezahlter Vertretungen
            if (kb.Checked && kennz.Length > 0) z.BezahltKennzeichen = kennz;
            else if (bezahltAlt || kennz.Length > 0 && z.BezahltKennzeichen == kennz) z.BezahltKennzeichen = "";
            vo.Freisetzung = (int)nFrei.Value;
            vo.Bereitschaft = (int)nBer.Value;
            vo.HatKlasse = (int)nKl.Value;
            vo.HatFach = (int)nFa.Value;
            vo.UnterrichtsstundenAmTag = (int)nStd.Value;
            vo.MerkerFaktor = (int)nMe.Value;
            vo.SperreFaktor = (int)nSp.Value;
            vo.ZaehlerFaktor = (int)nZa.Value;
            vo.RotWennMerkerGroesser = (int)nRm.Value;
            vo.NichtAnzeigenWennMerkerGroesser = (int)nNa.Value;
            vo.RotWennZaehlerGroesser = (int)nRz.Value;
            vo.GesperrteNichtAnzeigen = cGes.Checked;
            vo.KlasseAuchFuerKlassenteil = cTeil.Checked;
            vo.Raumgewicht4Beachten = cRg.Checked;
            vo.NurSpaeterenUnterricht = cSp.Checked;
            vo.NurDieselbeWoche = cWo.Checked;
            vo.Vorziehungen = (VorziehungsModus)vz.SelectedIndex;
            e.EvaRaumId = ElementListe.Id(eva);
            e.LehrerRaumPrinzip = lrp.Checked;
            e.FreisetzungPausenaufsicht = fpa.Checked;
            e.MitAktuellemDatumStarten = mad.Checked;
            foreach (DataGridViewRow r in bg.Rows)
                if (r.Tag is VertretungsArt art)
                {
                    var t = r.Cells[2].Value?.ToString()?.Trim() ?? "";
                    if (t.Length == 0) e.Bezeichnungen.Remove(art.ToString()); else e.Bezeichnungen[art.ToString()] = t;
                }
            na.Betreff = nBetreff.Text;
            na.Text = nText.Text.Replace("\r\n", "\n");
            na.Zeile = nZeile.Text;
            na.EntfaelleMelden = nEntf.Checked;
            na.AbwesendeInformieren = nAbw.Checked;
            // lokal gespeichert (einstellungen.json, nicht in der Schuldatei)
            lokal.TageslistenPdf = tlPdf.Checked;
            lokal.TageslistenEigeneDateinamen = tlName.Checked;
            lokal.TageslistenPdfOrdner = tlOrdner.Text.Trim();
            lokal.NachrichtenNurGeaenderte = nNur.Checked;
            // Absenzkopf (Ansichtseinstellungen des Vertretungsdrucks)
            dr.AbwesendeLehrer = akAbsL.Checked;
            dr.VertretungLehrer = akVtrL.Checked;
            dr.LehrerMitAbsenzgrund = akGrund.Checked;
            dr.AbwesendeKlassen = akAbsK.Checked;
            dr.VertretungKlassen = akVtrK.Checked;
            dr.BlockierteRaeume = akAbsR.Checked;
            dr.VertretungRaeume = akVtrR.Checked;
            dr.AbsenzkopfGroesse = akRegler.Value;
            dr.AbsenzkopfRahmen = akRahmen.Checked;
            dr.AbsenzkopfNichtVeranstaltungen = akVa.Checked;
            dr.AbsenzkopfLangLehrer = akLangL.Checked;
            dr.AbsenzkopfLangKlassen = akLangK.Checked;
            dr.AbsenzkopfLangRaeume = akLangR.Checked;
            lokal.Speichern();
            var grenze = (int)mag.Value;
            var ab = eab.Text.Trim();
            AppZustand.Doc.Aendern("Einstellungen Vertretungsplanung", AenderungsArt.Vertretung, x =>
            {
                x.Vertretungseinstellungen = e;
                foreach (var k in x.Klassen) if (klassenRaster.TryGetValue(k.Id, out var r)) k.VertretungsZeitraster = r;
                x.Einstellungen.Mehrarbeitsgrenze = grenze;
                x.Einstellungen.EntfallAbJahrgang = ab;
                foreach (var t in x.Vertretungen.Select(v => v.Datum).Where(t => t >= DateOnly.FromDateTime(DateTime.Today)).Distinct().ToList()) Vertretungen.Aktualisieren(x, t);
            });
        };
        return f;
    }
}
