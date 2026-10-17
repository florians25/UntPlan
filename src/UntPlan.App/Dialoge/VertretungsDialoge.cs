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
        bool IstKind(DataGridViewRow r) => r.Tag is DataGridViewRow;
        List<DataGridViewRow> Kinder(DataGridViewRow kopf) => g.Rows.Cast<DataGridViewRow>().Where(r => ReferenceEquals(r.Tag, kopf)).ToList();
        int Zahl(DataGridViewRow r, string spalte, int ersatz) => int.TryParse(r.Cells[spalte].Value?.ToString(), out var v) ? v : ersatz;
        void KinderAufbauen(DataGridViewRow kopf)
        {
            if (kopf.IsNewRow || IstKind(kopf) || kopf.DataGridView is null) return;
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
            if (r.IsNewRow || IstKind(r)) return;
            var spalte = g.Columns[e.ColumnIndex].Name;
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
            if (e.Row is null || IstKind(e.Row)) return;
            var kinder = Kinder(e.Row);
            if (kinder.Count > 0) g.BeginInvoke(() => { foreach (var k in kinder.Where(k => k.DataGridView != null)) g.Rows.Remove(k); });
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
            foreach (var k in Kinder(erste)) k.Cells["D"].Value = erste.Cells["D"].Value;
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
            if (IstKind(r)) return;
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
        bKl.Click += (_, _) =>        bKl.Click += (_, _) =>
        {
            var i = Zeile();
            var w = AuswahlDialog.Mehrere(f, ElementTyp.Klasse, "Klassen des Sondereinsatzes", Ids(ElementTyp.Klasse, g.Rows[i].Cells["K"].Value));
            if (w != null) { g.Rows[i].Cells["K"].Value = string.Join(",", w.Select(x => x.Kurzname)); Uebertragen(i, "K"); }
        };
        bLe.Click += (_, _) =>
        {
            var i = Zeile();
            var w = AuswahlDialog.Mehrere(f, ElementTyp.Lehrer, "Lehrer des Sondereinsatzes", Ids(ElementTyp.Lehrer, g.Rows[i].Cells["L"].Value));
            if (w != null) { g.Rows[i].Cells["L"].Value = string.Join(",", w.Select(x => x.Kurzname)); Uebertragen(i, "L"); }
        };
        bKk.Click += (_, _) =>
        {
            // Klassenkonferenz: alle Lehrer, die die gewählten Klassen unterrichten
            var i = Zeile();
            var kl = Ids(ElementTyp.Klasse, g.Rows[i].Cells["K"].Value);
            if (kl.Count == 0)
            {
                var w = AuswahlDialog.Mehrere(f, ElementTyp.Klasse, "Klassenkonferenz für Klasse(n)", Array.Empty<int>());
                if (w is null || w.Count == 0) return;
                kl = w.Select(x => x.Id).ToList();
                g.Rows[i].Cells["K"].Value = string.Join(",", w.Select(x => x.Kurzname));
            }
            var lehrerIds = d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.KlassenIds.Intersect(kl).Any() && z.LehrerId.HasValue).Select(z => z.LehrerId!.Value)
                .Concat(d.Klassen.Where(k => kl.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds)).Distinct();
            g.Rows[i].Cells["L"].Value = string.Join(",", lehrerIds.Select(ix.KLehrer).OrderBy(x => x));
            if (string.IsNullOrWhiteSpace(g.Rows[i].Cells["T"].Value?.ToString())) g.Rows[i].Cells["T"].Value = "Klassenkonferenz " + ix.KKlassen(kl);
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
        if (!Zeigen(owner, k)) return;
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

    public static bool Zeigen(IWin32Window owner, Klausur k)
    {
        using var f = new Dialog("Klausur", 470, 340);
        var y = 14;
        var name = Ui.Zeile(f, "Name:", new TextBox { Text = k.Name }, ref y, 140, 290);
        var dat = Ui.Zeile(f, "Datum:", new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd, dd.MM.yyyy", Value = k.Datum.ToDateTime(TimeOnly.MinValue) }, ref y, 140, 150);
        var von = Ui.Zeile(f, "Stunde von:", ElementListe.Stunde(k.StundeVon), ref y, 140, 56);
        var bis = ElementListe.Stunde(k.StundeBis);
        bis.Left = von.Right + 40;
        bis.Top = von.Top;
        f.Controls.Add(new Label { Text = "bis", Left = von.Right + 10, Top = von.Top + 3, AutoSize = true });
        f.Controls.Add(bis);
        var kl = Ui.Zeile(f, "Klasse(n):", new ElementWahl(ElementTyp.Klasse, k.KlassenIds), ref y, 140, 290);
        var le = Ui.Zeile(f, "Aufsicht (Lehrer):", new ElementWahl(ElementTyp.Lehrer, k.LehrerIds), ref y, 140, 290);
        var ra = Ui.Zeile(f, "Räume:", new ElementWahl(ElementTyp.Raum, k.RaumIds), ref y, 140, 290);
        var ent = Ui.Zeile(f, "", new CheckBox { Text = "Kollidierender Unterricht der Klassen entfällt", Checked = k.UnterrichtEntfaellt, AutoSize = true }, ref y, 140);
        var akt = Ui.Zeile(f, "", new CheckBox { Text = "Aktiv", Checked = k.Aktiv, AutoSize = true }, ref y, 140);
        var tx = Ui.Zeile(f, "Text:", new TextBox { Text = k.Text }, ref y, 140, 290);
        if (k.Importiert)
        {
            // aus UntPlanWeb übernommene Prüfung: nur Lehrkraft und Raum änderbar, keine Verlegung
            foreach (var c in new Control[] { name, dat, von, bis, kl, ent, tx }) c.Enabled = false;
            f.Text = "Klausur (aus UntPlanWeb – nur Lehrer und Raum änderbar)";
        }
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        if (k.Importiert)
        {
            k.LehrerIds = le.Ids;
            k.RaumIds = ra.Ids;
            k.Aktiv = akt.Checked;
            return true;
        }
        k.Name = name.Text;
        k.Datum = DateOnly.FromDateTime(dat.Value);
        k.StundeVon = (int)von.Value;
        k.StundeBis = (int)Math.Max(von.Value, bis.Value);
        k.KlassenIds = kl.Ids;
        k.LehrerIds = le.Ids;
        k.RaumIds = ra.Ids;
        k.UnterrichtEntfaellt = ent.Checked;
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
        var art = new DataGridViewComboBoxColumn { Name = "A", HeaderText = "für", Width = 90, FlatStyle = FlatStyle.Flat };
        art.Items.AddRange(Arten);
        g.Columns.Add(art);
        g.Columns[g.Columns.Add("E", "Klassen/Lehrer")].Width = 110;
        g.Columns[g.Columns.Add("T", "Text")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        g.Columns[g.Columns.Add("G", "Tage")].Width = 45;
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "P", HeaderText = "Portal", Width = 50 });
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

/// <summary>Einstellungen der Vertretungsplanung: Vertretungszeitraster, Zähler, Vorschlag, Allgemein, Bezeichnungen und Farben.</summary>
public static class VertretungsEinstellungenDialog
{
    private static readonly string[] RegelText = { "", "E", "EVA" };

    public static bool Zeigen(IWin32Window owner, int seite = 0)
    {
        var d = AppZustand.D;
        var e = System.Text.Json.JsonSerializer.Deserialize<VertretungsEinstellungen>(System.Text.Json.JsonSerializer.Serialize(d.Vertretungseinstellungen))!;
        var klassenRaster = d.Klassen.ToDictionary(k => k.Id, k => new Dictionary<int, VertretungsRegel>(k.VertretungsZeitraster));
        using var f = new Dialog("Einstellungen Vertretungsplanung", 680, 520);
        var tabs = new TabControl { Left = 8, Top = 8, Width = 664, Height = 462 };
        f.Controls.Add(tabs);

        // --- Vertretungszeitraster ---------------------------------------------------------------
        var tz = new TabPage("Vertretungszeitraster");
        var klasse = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 10, Width = 200 };
        klasse.Items.Add("(gesamte Schule)");
        foreach (var k in Stammdaten.Sortiert(d.Klassen)) klasse.Items.Add(k);
        klasse.Format += (_, ev) => ev.Value = ev.ListItem is Klasse k ? k.Kurzname : ev.ListItem?.ToString();
        klasse.SelectedIndex = 0;
        var raster = new DataGridView { Left = 10, Top = 42, Width = 636, Height = 300 };
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
            Left = 10, Top = 350, Width = 636, Height = 40, ForeColor = Color.DimGray,
        });
        RasterFuellen();
        tabs.TabPages.Add(tz);

        // --- Vertretungszähler -------------------------------------------------------------------
        var tzae = new TabPage("Vertretungszähler");
        var z = e.Zaehler;
        var y = 12;
        var c1 = Ui.Zeile(tzae, "", new CheckBox { Text = "Vertretungen zählen (+)", Checked = z.VertretungenZaehlen, AutoSize = true }, ref y, 10);
        var c2 = Ui.Zeile(tzae, "", new CheckBox { Text = "Sondereinsätze zählen (+)", Checked = z.SondereinsaetzeZaehlen, AutoSize = true }, ref y, 10);
        var c3 = Ui.Zeile(tzae, "", new CheckBox { Text = "Entfälle zählen (–)", Checked = z.EntfaelleZaehlen, AutoSize = true }, ref y, 10);
        var c4 = Ui.Zeile(tzae, "", new CheckBox { Text = "Freisetzungen zählen (–)", Checked = z.FreisetzungenZaehlen, AutoSize = true }, ref y, 10);
        var c5 = Ui.Zeile(tzae, "", new CheckBox { Text = "Pausenaufsicht-Vertretungen zählen", Checked = z.PausenaufsichtenZaehlen, AutoSize = true }, ref y, 10);
        var c6 = Ui.Zeile(tzae, "", new CheckBox { Text = "Betreuungen zählen halb", Checked = z.BetreuungenHalb, AutoSize = true }, ref y, 10);
        var c7 = Ui.Zeile(tzae, "", new CheckBox { Text = "Entfälle nur zählen, wenn der Absenzgrund \"Entfall zählen\" gesetzt ist", Checked = z.EntfallNurMitGrund, AutoSize = true }, ref y, 10);
        var c8 = Ui.Zeile(tzae, "", new CheckBox { Text = "Statt-Vertretungen neutral (zählen nicht)", Checked = z.StattVertretungNeutral, AutoSize = true }, ref y, 10);
        var c9 = Ui.Zeile(tzae, "", new CheckBox { Text = "Klausuraufsichten neutral", Checked = z.KlausurenNeutral, AutoSize = true }, ref y, 10);
        var zr = Ui.Zeile(tzae, "Durchrechnungszeitraum:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 200, 160);
        zr.Items.AddRange(new object[] { "Woche", "Monat", "Halbjahr", "Schuljahr", "benutzerdefiniert" });
        zr.SelectedIndex = (int)z.Zeitraum;
        var wz = Ui.Zeile(tzae, "Wie zählen:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 200, 160);
        wz.Items.AddRange(new object[] { "Anzahl der Stunden", "Werte (Faktor des Unterrichts)" });
        wz.SelectedIndex = (int)z.WieZaehlen;
        var kn = Ui.Zeile(tzae, "Kennzeichen \"nicht zählen\":", new TextBox { Text = z.NichtZaehlenKennzeichen }, ref y, 200, 60);
        var kb = Ui.Zeile(tzae, "Kennzeichen \"bezahlt\":", new TextBox { Text = z.BezahltKennzeichen }, ref y, 200, 60);
        tabs.TabPages.Add(tzae);

        // --- Vertretungsvorschlag ----------------------------------------------------------------
        var tv = new TabPage("Vertretungsvorschlag");
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
        tabs.TabPages.Add(tv);

        // --- Allgemein ---------------------------------------------------------------------------
        var ta = new TabPage("Allgemein");
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
            Left = 10, Top = y + 10, Width = 636, Height = 40, ForeColor = Color.DimGray,
        });
        tabs.TabPages.Add(ta);

        // --- Bezeichnungen und Farben ------------------------------------------------------------
        var tb = new TabPage("Farben und Bezeichnungen");
        var bg = new DataGridView { Left = 10, Top = 10, Width = 636, Height = 410 };
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
        tabs.TabPages.Add(tb);
        tabs.SelectedIndex = Math.Clamp(seite, 0, tabs.TabPages.Count - 1);

        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        z.VertretungenZaehlen = c1.Checked;
        z.SondereinsaetzeZaehlen = c2.Checked;
        z.EntfaelleZaehlen = c3.Checked;
        z.FreisetzungenZaehlen = c4.Checked;
        z.PausenaufsichtenZaehlen = c5.Checked;
        z.BetreuungenHalb = c6.Checked;
        z.EntfallNurMitGrund = c7.Checked;
        z.StattVertretungNeutral = c8.Checked;
        z.KlausurenNeutral = c9.Checked;
        z.Zeitraum = (ZaehlZeitraum)zr.SelectedIndex;
        z.WieZaehlen = (ZaehlArt)wz.SelectedIndex;
        z.NichtZaehlenKennzeichen = kn.Text.Trim();
        z.BezahltKennzeichen = kb.Text.Trim();
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
        return true;
    }
}
