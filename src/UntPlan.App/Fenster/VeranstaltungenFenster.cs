using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Veranst. / Lehrer: Exkursionen, Klassenfahrten, Schulveranstaltungen. Spalten wie im Vorbild (VA-Nr., Klasse, Kl-Teil, Lehrer, Raum,
/// Von, ab St., Bis, bis St., Grund, Text, zählen, Schülergruppe). Mehrere Klassen/Lehrer mit Komma oder in der Auswahlliste mit
/// Strg-Klick, Elemente aus dem Element-Rollup per Drag &amp; Drop, Strg+C / Strg+V kopiert eine Veranstaltung auf das eingestellte Datum.
/// Ist eine Lehrkraft wegen einer Absenz entfernt worden, steht im Feld „Lehrer“ rot „???“, bis eine Ersatzlehrkraft eingetragen ist.
/// Schnellauswahl („Hinzufügen“ bzw. Kontextmenü einer Spalte): alle Lehrkräfte/Klassen/Räume, nach Abteilung, Jahrgang, Gruppe …,
/// alles aus den Stunden der Veranstaltung oder aus der gewählten Stunde übernehmen.
/// </summary>
public sealed class VeranstaltungenFenster : MdiFenster
{
    private static Veranstaltung? _zwischenablage;
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly NumericUpDown _nr = new() { Width = 70, Maximum = int.MaxValue, Minimum = 0 };
    private readonly ZeitraumAuswahl _zeitraum;
    private bool _laedt;
    private static readonly string[] AuswahlSpalten = { "K", "KT", "L", "R", "SG" };

    public VeranstaltungenFenster()
    {
        Text = "Veranst. / Lehrer";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("veranstaltung");
        Size = new Size(980, 330);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Veranstaltung (Dialog)", "neu", Neu));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Veranstaltung kopieren (Strg+C)", "kopieren", KopierenMerken));
        ts.Items.Add(Knopf("Veranstaltung einfügen (Strg+V) – am eingestellten Datum", "import", Einfuegen));
        ts.Items.Add(Knopf("Element-Rollup", "rollup", () => ElementRollup.Zeigen(_g.CurrentCell?.OwningColumn.Name switch
        {
            "L" => ElementTyp.Lehrer, "R" => ElementTyp.Raum, _ => ElementTyp.Klasse,
        })));
        ts.Items.Add(Knopf("Vertretungen", "vertretung", () => HauptFenster.Oeffnen("vertr", () => new VertretungenFenster())));
        ts.Items.Add(new ToolStripSeparator());
        // Schnellauswahl: alle Klassen/Lehrkräfte/Räume, nach Abteilung, Jahrgang, Gruppe … bzw. alles aus den Stunden übernehmen
        var schnell = new ToolStripDropDownButton("Hinzufügen", Symbole.Get("plus", 16))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = "Schnell hinzufügen: alle Lehrkräfte, Klassen, Räume, nach Abteilung/Jahrgang/Gruppe oder alle aus Stunde/Unterricht",
        };
        schnell.DropDownItems.Add("…");
        schnell.DropDownOpening += (_, _) =>
        {
            schnell.DropDownItems.Clear();
            schnell.DropDownItems.AddRange(SchnellMenue(null).ToArray());
        };
        ts.Items.Add(schnell);
        ts.Items.Add(Knopf("Alle aus den Stunden der Veranstaltung übernehmen (Klassen, Lehrkräfte, Räume)", "unterricht", AusStundenUebernehmen));

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        foreach (var (n, k, b) in new[]
        {
            ("Nr", "VA-Nr.", 50), ("K", "Klasse", 70), ("KT", "Kl-Teil", 60), ("L", "Lehrer", 80), ("R", "Raum", 55), ("V", "Von TT.MM.", 100), ("SV", "ab St.", 48),
            ("B", "Bis TT.MM.", 100), ("SB", "bis St.", 48),
        })
            _g.Columns.Add(n is "V" or "B" ? DatumSpalte.Neu(n, k, b) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b, ReadOnly = n == "Nr" });
        _g.Columns.Add(SuchKombi.Spalte("G", "Grund", 60, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 140 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Z", HeaderText = "zählen", Width = 46 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "SG", HeaderText = "Schülergruppe", Width = 140 });
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var n in new[] { "K", "KT", "L", "R" }) _g.Columns[n]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        SuchKombi.Aktivieren(_g, (zelle, text) => D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname == text) is { } g ? new ListenStil(Color.White, Color.Black, g.Langname) : null);
        _g.CellEndEdit += ZelleGeaendert;
        // während der Eingabe aufgeschobenen Neuaufbau nach dem Verlassen der Zelle nachholen
        _g.CellEndEdit += (_, _) => { if (_neuaufbauAusstehend) NachZellwechsel(() => { if (_neuaufbauAusstehend) Neuaufbau(); }); };
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _g.Columns[e.ColumnIndex].Name == "Z") _g.EndEdit(); };
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0) Bearbeiten(); };
        _g.CellFormatting += Formatieren;
        _g.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _g.CurrentCell is not { } c || c.RowIndex != e.RowIndex || c.ColumnIndex != e.ColumnIndex) return;
            if (AuswahlSpalten.Contains(_g.Columns[e.ColumnIndex].Name) && !_g.IsCurrentCellInEditMode) MehrfachAuswahl.PfeilZeichnen(e);
        };
        _g.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Left || !AuswahlSpalten.Contains(_g.Columns[e.ColumnIndex].Name)) return;
            if (MehrfachAuswahl.AufPfeil(_g, e)) AuswahlZeigen(e.RowIndex, e.ColumnIndex);
        };
        _g.KeyDown += Taste;
        // Kontextmenü: Schnellauswahl für die angeklickte Spalte (Klasse, Kl-Teil, Lehrer, Raum)
        _g.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!_g.IsCurrentCellInEditMode) _g.CurrentCell = _g.Rows[e.RowIndex].Cells[e.ColumnIndex];
            var m = new ContextMenuStrip { Font = Ui.Schrift };
            m.Items.AddRange(SchnellMenue(FeldVon(_g.Columns[e.ColumnIndex].Name)).ToArray());
            m.Closed += (_, _) => BeginInvoke(m.Dispose);
            m.Show(_g, _g.PointToClient(Cursor.Position));
        };
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        AppZustand.VertretungGewaehlt += VertretungGewaehlt;
        FormClosed += (_, _) => { AppZustand.StundeGewaehlt -= StundeGewaehlt; AppZustand.VertretungGewaehlt -= VertretungGewaehlt; };
        // Drag & Drop aus dem Element-Rollup
        _g.AllowDrop = true;
        _g.DragOver += (_, e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string s && s.StartsWith("elemente:") ? DragDropEffects.Copy : DragDropEffects.None;
        _g.DragDrop += Fallenlassen;

        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _zeitraum = new ZeitraumAuswahl(_von, _bis, "Veranstaltungen");
        _zeitraum.Geaendert += () => { if (!_laedt) Neuaufbau(); };
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(4, 2, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        var gruppe = new GroupBox { Text = "Von-Bis", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), MinimumSize = new Size(0, 50) };
        gruppe.Controls.Add(_zeitraum);
        _zeitraum.Location = new Point(6, 18);
        kopf.Controls.Add(gruppe);
        var unten = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(16, 3, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        unten.Controls.Add(new Label { Text = "VA-Nr.", AutoSize = true, Padding = new Padding(0, 5, 6, 0) });
        unten.Controls.Add(_nr);
        _nr.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Springen((int)_nr.Value); } };
        _g.SelectionChanged += (_, _) => { if (!_laedt && !_nr.Focused && _g.CurrentRow?.Tag is Veranstaltung v) _nr.Value = Math.Min(_nr.Maximum, v.Id); };
        Controls.Add(_g);
        Controls.Add(kopf);
        Controls.Add(unten);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || _zeitraum.Art == Zeitraumart.VonBis) return;
        _von.Value = datum.ToDateTime(TimeOnly.MinValue);
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private static string Datum(DateOnly d) => d.ToString("dd.MM.yyyy");

    /// <summary>Lehrer-Feld: an Tagen mit entfernter Lehrkraft ohne Ersatz „???“ an ihrer Stelle.</summary>
    private string LehrerText(Veranstaltung v)
    {
        var tag = OffenerTag(v);
        var trenner = Trenner(v.LehrerIds.Count);
        return tag is DateOnly t ? VeranstaltungsLogik.LehrerKurztext(D, v, t, trenner, "???") : VeranstaltungsAuswahl.Kurztext(D, ElementTyp.Lehrer, v.LehrerIds, v.Von, v.Bis, trenner);
    }

    /// <summary>Wenige Elemente untereinander, viele (z. B. 40 Klassen) fortlaufend – sonst würde die Zeile Dutzende Zeilen hoch.</summary>
    private static string Trenner(int anzahl) => anzahl > 6 ? ", " : ",\n";

    /// <summary>Klassen/Räume kompakt: „Alle Klassen“, „Alle Klassen außer 5a“ bzw. Liste (wieder einlesbar, siehe <see cref="VeranstaltungsAuswahl.Lesen"/>).</summary>
    private string Feldtext(Veranstaltung v, ElementTyp typ, List<int> ids) => VeranstaltungsAuswahl.Kurztext(D, typ, ids, v.Von, v.Bis, Trenner(ids.Count));

    /// <summary>Erster angezeigter Tag, an dem die Veranstaltung einen offenen Platz hat.</summary>
    private DateOnly? OffenerTag(Veranstaltung v) =>
        v.Ersatz.Where(e => e.ErsatzId is null && e.Datum >= Von && e.Datum <= Bis && v.LehrerIds.Contains(e.LehrerId)).Select(e => (DateOnly?)e.Datum).OrderBy(x => x).FirstOrDefault();

    private bool _neuaufbauAusstehend;
    /// <summary>Nach dem Anlegen: diese Veranstaltung wählen.</summary>
    private int? _springeZu;

    private static string? Schluessel(DataGridViewRow r) => r.Tag is Veranstaltung v ? "VA" + v.Id : "neu";

    /// <summary>
    /// Liste neu aufbauen; aktuelle Veranstaltung (VA-Nr.), Spalte und Bildlauf bleiben erhalten. Während einer Eingabe wird
    /// nicht neu aufgebaut, sondern beim Verlassen der Zelle nachgeholt.
    /// </summary>
    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode)
        {
            _neuaufbauAusstehend = true;
            return;
        }
        _neuaufbauAusstehend = false;
        var stand = Steuerelemente.TabellenStand.Merken(_g, Schluessel);
        var springe = _springeZu;
        _springeZu = null;
        _laedt = true;
        _g.Rows.Clear();
        foreach (var v in D.Veranstaltungen.Where(v => v.Bis >= Von && v.Von <= Bis).OrderBy(v => v.Von).ThenBy(v => v.Id))
        {
            var r = _g.Rows.Add(v.Id, Feldtext(v, ElementTyp.Klasse, v.KlassenIds), Feldtext(v, ElementTyp.Klasse, v.KlassenteilIds), LehrerText(v),
                Feldtext(v, ElementTyp.Raum, v.RaumIds), Datum(v.Von), v.StundeVon, Datum(v.Bis), v.StundeBis >= 99 ? D.Zeitraster.StundenProTag.ToString() : v.StundeBis.ToString(),
                D.Abwesenheitsgruende.FirstOrDefault(g => g.Id == v.GrundId)?.Kurzname ?? "", v.Text, v.Zaehlen, v.Schuelergruppe);
            _g.Rows[r].Tag = v;
        }
        var neu = _g.Rows.Add("", "", "", "", "", "", "", "", "", "", "", false, "");
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(255, 228, 190);
        if (springe is int sp) stand.AktuellSetzen("VA" + sp);
        stand.Wiederherstellen(_g, Schluessel, false);
        _laedt = false;
    }

    private void Formatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _g.Rows[e.RowIndex].Tag is not Veranstaltung v) return;
        var name = _g.Columns[e.ColumnIndex].Name;
        if (name == "L" && OffenerTag(v) is DateOnly t)
        {
            // Lehrkraft fehlt: Feld rot, bis eine Ersatzlehrkraft eingetragen ist
            e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = Color.FromArgb(225, 40, 40);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Color.White;
            e.CellStyle.Font = Ui.SchriftFett;
            _g.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = $"Am {t:dd.MM.yyyy} wurde eine Lehrkraft entfernt – Ersatzlehrkraft statt ??? eintragen.";
        }
        if (name == "Nr" && OffenerTag(v) != null) e.CellStyle.ForeColor = Color.Red;
        if (v.Bis < AppZustand.Datum && name != "L") e.CellStyle.ForeColor = Color.Gray;
    }

    private void Taste(object? sender, KeyEventArgs e)
    {
        if (_g.IsCurrentCellInEditMode) return;
        if (e.Control && e.KeyCode == Keys.C) { KopierenMerken(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.V) { Einfuegen(); e.Handled = true; }
        else if ((e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) && _g.CurrentCell is { } c && AuswahlSpalten.Contains(c.OwningColumn.Name))
        {
            AuswahlZeigen(c.RowIndex, c.ColumnIndex);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete && e.Control) { Loeschen(); e.Handled = true; }
    }

    /// <summary>Auswahlliste der Spalte (Klassen/Lehrer/Räume mehrfach mit Strg-Klick, Schülergruppe einfach).</summary>
    private void AuswahlZeigen(int zeile, int spalte)
    {
        var name = _g.Columns[spalte].Name;
        List<MehrfachAuswahl.Eintrag> eintraege;
        if (name == "SG")
        {
            // bestehende Schülergruppen aus den Unterrichten; eine neue Schülergruppe wird einfach eingetippt
            eintraege = VeranstaltungsLogik.Schuelergruppen(D).Select(s => new MehrfachAuswahl.Eintrag(s, "")).ToList();
            if (eintraege.Count == 0) { Ui.Info("Es gibt noch keine Schülergruppen. Eine neue Schülergruppe kann direkt eingetippt werden."); return; }
        }
        else
        {
            var typ = name switch { "L" => ElementTyp.Lehrer, "R" => ElementTyp.Raum, _ => ElementTyp.Klasse };
            eintraege = Stammdaten.Sortiert(D, typ).Where(x => !x.Ignorieren).Select(x => new MehrfachAuswahl.Eintrag(x.Kurzname, x is Lehrer l ? l.VollerName : x.Langname)).ToList();
        }
        var aktuell = (_g.Rows[zeile].Cells[spalte].Value?.ToString() ?? "").Split(new[] { ',', '\n', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        // gespeicherte Veranstaltung: Auswahl aus den Ids (die Zelle zeigt ggf. „Alle Lehrkräfte“ bzw. „Alle Klassen außer …“)
        if (_g.Rows[zeile].Tag is Veranstaltung va && name is "K" or "KT" or "L" or "R")
        {
            var offenTag = name == "L" ? OffenerTag(va) : null;
            var (typ, ids) = name switch
            {
                "K" => (ElementTyp.Klasse, va.KlassenIds),
                "KT" => (ElementTyp.Klasse, va.KlassenteilIds),
                "R" => (ElementTyp.Raum, va.RaumIds),
                _ => (ElementTyp.Lehrer, offenTag is DateOnly ot ? va.LehrerAm(ot) : va.LehrerIds),
            };
            aktuell = ids.Select(i => Ix.K(typ, i)).Concat(offenTag is DateOnly ot2 ? Enumerable.Repeat("???", va.OffenAm(ot2).Count) : Enumerable.Empty<string>()).ToArray();
        }
        if (name == "SG") aktuell = new[] { _g.Rows[zeile].Cells[spalte].Value?.ToString() ?? "" };
        MehrfachAuswahl.Zeigen(_g, zeile, spalte, eintraege, aktuell, name != "SG", gewaehlt =>
        {
            if (IsDisposed || zeile >= _g.Rows.Count) return;
            // offene Plätze (???) bleiben erhalten, damit neue Lehrkräfte als Ersatz eingetragen werden
            var offen = name == "L" && aktuell.Contains("???") ? new[] { "???" } : Array.Empty<string>();
            var wert = string.Join(",", gewaehlt.Concat(offen));
            _laedt = true;
            _g.Rows[zeile].Cells[spalte].Value = wert;
            _laedt = false;
            ZelleGeaendert(_g, new DataGridViewCellEventArgs(spalte, zeile));
        });
    }

    // ---------------------------------------------------------------- Schnellauswahl

    /// <summary>Zuletzt in einem Stundenplan bzw. in der Vertretungsliste gewählte Stunde (zum Übernehmen ihrer Elemente).</summary>
    private (Unterricht U, Planstunde? P)? _letzteStunde;

    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle) => _letzteStunde = (u, p);

    private void VertretungGewaehlt(Vertretung? v)
    {
        if (v?.UnterrichtId is not int uid || !Ix.Unterrichte.TryGetValue(uid, out var u)) return;
        var tag = Kalender.TagIndex(D, v.Datum);
        _letzteStunde = (u, D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == uid && x.Tag == tag && x.Stunde == v.Stunde));
    }

    private static VeranstaltungsAuswahl.Feld? FeldVon(string spalte) => spalte switch
    {
        "K" => VeranstaltungsAuswahl.Feld.Klassen,
        "KT" => VeranstaltungsAuswahl.Feld.Klassenteil,
        "L" => VeranstaltungsAuswahl.Feld.Lehrer,
        "R" => VeranstaltungsAuswahl.Feld.Raeume,
        _ => null,
    };

    private static string FeldText(VeranstaltungsAuswahl.Feld f) => f switch
    {
        VeranstaltungsAuswahl.Feld.Klassen => "Klassen (entfällt)",
        VeranstaltungsAuswahl.Feld.Klassenteil => "Kl-Teil (parallel)",
        VeranstaltungsAuswahl.Feld.Lehrer => "Lehrkräfte",
        _ => "Räume",
    };

    private Veranstaltung? AktuelleVeranstaltung => _g.CurrentRow?.Tag as Veranstaltung;

    /// <summary>
    /// Menü der Schnellauswahl: mit Feld (Kontextmenü einer Spalte) die Gruppen dieses Feldes direkt, sonst je Feld ein Untermenü;
    /// dazu „Alle aus den Stunden der Veranstaltung“ und „Aus gewählter Stunde“.
    /// </summary>
    private List<ToolStripItem> SchnellMenue(VeranstaltungsAuswahl.Feld? feld)
    {
        var va = AktuelleVeranstaltung;
        var res = new List<ToolStripItem>();
        if (feld is VeranstaltungsAuswahl.Feld f)
        {
            res.AddRange(GruppenEintraege(f, va));
            var leeren = new ToolStripMenuItem($"{FeldText(f)} leeren") { Enabled = va != null && Ids(va, f).Count > 0 };
            leeren.Click += (_, _) => Schnell(f, Array.Empty<int>(), true);
            res.Add(new ToolStripSeparator());
            res.Add(leeren);
        }
        else
            foreach (var x in new[] { VeranstaltungsAuswahl.Feld.Klassen, VeranstaltungsAuswahl.Feld.Klassenteil, VeranstaltungsAuswahl.Feld.Lehrer, VeranstaltungsAuswahl.Feld.Raeume })
            {
                var m = new ToolStripMenuItem(FeldText(x), Symbole.Get(x switch
                {
                    VeranstaltungsAuswahl.Feld.Lehrer => "lehrer", VeranstaltungsAuswahl.Feld.Raeume => "raeume", _ => "klassen",
                }, 16));
                m.DropDownItems.AddRange(GruppenEintraege(x, va).ToArray());
                res.Add(m);
            }
        res.Add(new ToolStripSeparator());
        var aus = new ToolStripMenuItem("Alle aus den Stunden der Veranstaltung übernehmen", Symbole.Get("unterricht", 16)) { Enabled = va != null };
        aus.Click += (_, _) => AusStundenUebernehmen();
        res.Add(aus);
        var st = _letzteStunde;
        var ausStunde = new ToolStripMenuItem(st is { } s ? $"Alle aus gewählter Stunde übernehmen (U-Nr. {s.U.Id}: {Ix.KKlassen(s.U.KlassenIds.ToList())})" : "Alle aus gewählter Stunde übernehmen (zuerst eine Stunde im Stundenplan wählen)")
        {
            Enabled = st != null,
        };
        ausStunde.Click += (_, _) => AusStundeUebernehmen();
        res.Add(ausStunde);
        return res;
    }

    private static List<int> Ids(Veranstaltung va, VeranstaltungsAuswahl.Feld f) => f switch
    {
        VeranstaltungsAuswahl.Feld.Klassen => va.KlassenIds,
        VeranstaltungsAuswahl.Feld.Klassenteil => va.KlassenteilIds,
        VeranstaltungsAuswahl.Feld.Lehrer => va.LehrerIds,
        _ => va.RaumIds,
    };

    /// <summary>Gruppen eines Feldes: „Alle …“ und Gruppen ohne Bereich direkt, die übrigen je Bereich (Abteilung, Jahrgang …) in einem Untermenü.</summary>
    private List<ToolStripItem> GruppenEintraege(VeranstaltungsAuswahl.Feld f, Veranstaltung? va)
    {
        var res = new List<ToolStripItem>();
        var gruppen = VeranstaltungsAuswahl.Gruppen(D, VeranstaltungsAuswahl.TypVon(f), va);
        ToolStripMenuItem Eintrag(ElementGruppe g)
        {
            var name = g.Bereich.Length > 0 && g.Name.StartsWith(g.Bereich + " ") ? g.Name[(g.Bereich.Length + 1)..] : g.Name;
            var m = new ToolStripMenuItem($"{name}  ({g.Ids.Count})") { ToolTipText = string.Join(", ", g.Ids.Take(40).Select(i => Ix.K(g.Typ, i))) + (g.Ids.Count > 40 ? " …" : "") };
            m.Click += (_, _) => Schnell(f, g.Ids);
            return m;
        }
        foreach (var g in gruppen.Where(g => g.Bereich.Length == 0)) res.Add(Eintrag(g));
        foreach (var b in gruppen.Where(g => g.Bereich.Length > 0).GroupBy(g => g.Bereich))
        {
            var m = new ToolStripMenuItem("Nach " + b.Key);
            m.DropDownItems.AddRange(b.Select(Eintrag).ToArray<ToolStripItem>());
            res.Add(m);
        }
        if (res.Count == 0) res.Add(new ToolStripMenuItem("(keine Elemente)") { Enabled = false });
        return res;
    }

    /// <summary>Elemente in ein Feld der aktuellen Veranstaltung übernehmen (in der leeren Zeile: neue Veranstaltung am eingestellten Datum).</summary>
    internal void Schnell(VeranstaltungsAuswahl.Feld feld, IReadOnlyList<int> ids, bool ersetzen = false)
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        var alt = AktuelleVeranstaltung;
        var v = alt is null ? new Veranstaltung { Von = Von, Bis = Von, StundeVon = 1, StundeBis = 99 } : Kopie(alt);
        var n = VeranstaltungsAuswahl.Uebernehmen(v, feld, ids, ersetzen);
        if (n == 0 && !ersetzen) { Ui.Info("Diese Elemente sind bereits eingetragen."); return; }
        if (alt is null && v.KlassenIds.Count == 0 && v.KlassenteilIds.Count == 0 && v.LehrerIds.Count == 0 && v.RaumIds.Count == 0) return;
        Speichern(v, alt);
    }

    /// <summary>Alle Klassen, Lehrkräfte und Räume der Stunden übernehmen, die in die Veranstaltung fallen.</summary>
    internal void AusStundenUebernehmen()
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        if (AktuelleVeranstaltung is not { } alt) { Ui.Info("Bitte zuerst eine Veranstaltung wählen."); return; }
        var e = VeranstaltungsAuswahl.AusStunden(D, alt);
        if (e.Leer) { Ui.Info("In den Stunden der Veranstaltung findet kein passender Unterricht statt."); return; }
        var v = Kopie(alt);
        if (VeranstaltungsAuswahl.AllesUebernehmen(v, e) == 0) { Ui.Info("Alle Elemente der Stunden sind bereits eingetragen."); return; }
        Speichern(v, alt);
    }

    /// <summary>Klassen, Lehrkräfte und Räume der zuletzt im Stundenplan bzw. in der Vertretungsliste gewählten Stunde übernehmen.</summary>
    internal void AusStundeUebernehmen(Unterricht? u = null, Planstunde? p = null)
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        if (u is null && _letzteStunde is { } st) (u, p) = st;
        if (u is null) { Ui.Info("Bitte zuerst im Stundenplan eine Stunde wählen."); return; }
        var alt = AktuelleVeranstaltung;
        var v = alt is null ? new Veranstaltung { Von = Von, Bis = Von, StundeVon = p?.Stunde ?? 1, StundeBis = p?.Stunde ?? 99 } : Kopie(alt);
        if (VeranstaltungsAuswahl.AllesUebernehmen(v, VeranstaltungsAuswahl.AusUnterricht(D, u, p)) == 0) { Ui.Info("Alle Elemente der Stunde sind bereits eingetragen."); return; }
        Speichern(v, alt);
    }

    /// <summary>Selbsttest: Veranstaltung wählen (Zeile) und Auswahlliste einer Spalte öffnen.</summary>
    internal void SelbsttestWaehlen(int id, string spalte = "K")
    {
        foreach (DataGridViewRow r in _g.Rows)
            if (r.Tag is Veranstaltung x && x.Id == id) _g.CurrentCell = r.Cells[spalte];
    }

    /// <summary>Selbsttest: angezeigter Text einer Zelle der Veranstaltung.</summary>
    internal string SelbsttestZelltext(int id, string spalte) =>
        _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is Veranstaltung x && x.Id == id)?.Cells[spalte].FormattedValue?.ToString() ?? "";

    /// <summary>Selbsttest: leere Zeile (neue Veranstaltung) wählen.</summary>
    internal void SelbsttestNeueZeile() => _g.CurrentCell = _g.Rows[_g.Rows.Count - 1].Cells["K"];

    internal void SelbsttestAuswahlZeigen(string spalte)
    {
        if (_g.CurrentCell is { } c) AuswahlZeigen(c.RowIndex, _g.Columns[spalte]!.Index);
    }

    /// <summary>Selbsttest: Einträge des Schnellauswahl-Menüs (Toolbar).</summary>
    internal List<string> SelbsttestMenue(VeranstaltungsAuswahl.Feld? feld) =>
        SchnellMenue(feld).OfType<ToolStripMenuItem>().SelectMany(m => m.DropDownItems.Count > 0
            ? m.DropDownItems.OfType<ToolStripMenuItem>().Select(x => $"{m.Text} > {x.Text}").Prepend(m.Text ?? "") : new[] { m.Text ?? "" }).ToList();

    private void Fallenlassen(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.Text) is not string s || ElementRollup.Text2Typ(s, out var typ, out var namen) is null) return;
        var p = _g.PointToClient(new Point(e.X, e.Y));
        var hit = _g.HitTest(p.X, p.Y);
        var zeile = hit.RowIndex >= 0 ? hit.RowIndex : _g.Rows.Count - 1;
        var spalteName = hit.ColumnIndex >= 0 ? _g.Columns[hit.ColumnIndex].Name : "";
        // Spalte passend zum Elementtyp (Klassen in Klasse bzw. Kl-Teil, Lehrer in Lehrer, Räume in Raum)
        spalteName = typ switch
        {
            ElementTyp.Klasse => spalteName == "KT" ? "KT" : "K",
            ElementTyp.Lehrer => "L",
            ElementTyp.Raum => "R",
            _ => "",
        };
        if (spalteName.Length == 0) { Ui.Info("Diese Elemente können hier nicht abgelegt werden."); return; }
        var spalte = _g.Columns[spalteName]!.Index;
        var vorhanden = (_g.Rows[zeile].Cells[spalte].Value?.ToString() ?? "").Split(new[] { ',', '\n', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        _laedt = true;
        _g.Rows[zeile].Cells[spalte].Value = string.Join(",", vorhanden.Concat(namen).Distinct(StringComparer.OrdinalIgnoreCase));
        _laedt = false;
        _g.CurrentCell = _g.Rows[zeile].Cells[spalte];
        ZelleGeaendert(_g, new DataGridViewCellEventArgs(spalte, zeile));
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || e.RowIndex >= _g.Rows.Count) return;
        var zeile = _g.Rows[e.RowIndex];
        var alt = zeile.Tag as Veranstaltung;
        var start = Von;
        var v = alt is null ? new Veranstaltung { Von = start, Bis = start, StundeVon = 1, StundeBis = 99 } : Kopie(alt);
        var wert = zeile.Cells[e.ColumnIndex].Value is bool bw ? (bw ? "1" : "") : zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var ersatz = new List<int>();
        try
        {
            switch (_g.Columns[e.ColumnIndex].Name)
            {
                case "T": v.Text = wert; break;
                case "V": v.Von = Ui.DatumLesen(wert, v.Von); if (v.Bis < v.Von || alt is null) v.Bis = v.Von; break;
                case "B": v.Bis = Ui.DatumLesen(wert, v.Bis); if (v.Bis < v.Von) v.Von = v.Bis; break;
                case "SV": v.StundeVon = wert.Length == 0 ? 1 : Math.Clamp(int.Parse(wert), 1, Math.Max(1, D.Zeitraster.StundenProTag)); break;
                case "SB": v.StundeBis = wert.Length == 0 || int.Parse(wert) >= D.Zeitraster.StundenProTag ? 99 : Math.Max(1, int.Parse(wert)); break;
                case "K": v.KlassenIds = VeranstaltungsAuswahl.Lesen(D, ElementTyp.Klasse, wert, v.Von, v.Bis); v.KlassenteilIds.RemoveAll(v.KlassenIds.Contains); break;
                case "KT": v.KlassenteilIds = VeranstaltungsAuswahl.Lesen(D, ElementTyp.Klasse, wert, v.Von, v.Bis); v.KlassenIds.RemoveAll(v.KlassenteilIds.Contains); break;
                case "L":
                    var namen = VeranstaltungsAuswahl.Lesen(D, ElementTyp.Lehrer, wert.Replace("???", ""), v.Von, v.Bis);
                    if (alt != null && OffenerTag(alt) is DateOnly tag)
                    {
                        // offene Plätze: neu eingetragene Lehrkräfte sind Ersatzlehrkräfte (nur für die Tage mit entfernter Lehrkraft)
                        var bisher = alt.LehrerAm(tag);
                        ersatz = namen.Where(n => !bisher.Contains(n) && !alt.LehrerIds.Contains(n)).ToList();
                        if (ersatz.Count == 0) v.LehrerIds = namen.Union(alt.Ersatz.Where(x => x.ErsatzId is null).Select(x => x.LehrerId)).Where(n => namen.Contains(n) || alt.LehrerIds.Contains(n)).Distinct().ToList();
                    }
                    else v.LehrerIds = namen;
                    break;
                case "R": v.RaumIds = VeranstaltungsAuswahl.Lesen(D, ElementTyp.Raum, wert, v.Von, v.Bis); break;
                case "G": v.GrundId = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
                case "Z": v.Zaehlen = wert == "1"; break;
                case "SG": v.Schuelergruppe = wert; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            Ui.Info(ex is FormatException { Message: { Length: > 0 } m } && !m.StartsWith("Input") && !m.StartsWith("The input") ? m : $"Die Eingabe \"{wert}\" ist ungültig.");
            NachZellwechsel(Neuaufbau);
            return;
        }
        if (alt is null && string.IsNullOrWhiteSpace(v.Text) && v.KlassenIds.Count == 0 && v.KlassenteilIds.Count == 0 && v.LehrerIds.Count == 0) return;
        if (ersatz.Count > 0 && alt != null)
        {
            var id = alt.Id;
            var tage = alt.Ersatz.Where(x => x.ErsatzId is null && x.Datum >= Von && x.Datum <= Bis).OrderBy(x => x.Datum).ToList();
            // mehr neue Lehrkräfte als offene Plätze (z. B. „Alle“ gewählt): die übrigen nehmen zusätzlich an der Veranstaltung teil
            var zusaetzlich = ersatz.Skip(tage.Count).ToList();
            Aendern("Ersatzlehrkraft für Veranstaltung", AenderungsArt.Vertretung, d =>
            {
                var i = 0;
                foreach (var x in tage)
                    VeranstaltungsLogik.ErsatzSetzen(d, id, x.Datum, x.LehrerId, ersatz[Math.Min(i++, ersatz.Count - 1)]);
                if (zusaetzlich.Count > 0 && d.Veranstaltungen.FirstOrDefault(x => x.Id == id) is { } va)
                {
                    VeranstaltungsAuswahl.Uebernehmen(va, VeranstaltungsAuswahl.Feld.Lehrer, zusaetzlich);
                    Vertretungen.Aktualisieren(d, va.Von, va.Bis);
                }
            });
            NachZellwechsel(Neuaufbau);
            return;
        }
        Speichern(v, alt);
    }

    private void Speichern(Veranstaltung v, Veranstaltung? alt)
    {
        Aendern(alt is null ? "Veranstaltung" : "Veranstaltung geändert", AenderungsArt.Vertretung, d =>
        {
            if (alt != null) d.Veranstaltungen.RemoveAll(x => x.Id == alt.Id);
            else v.Id = d.NeueId();
            d.Veranstaltungen.Add(v);
            if (alt is null) _springeZu = v.Id;
            var von = alt is null || v.Von < alt.Von ? v.Von : alt.Von;
            var bis = alt is null || v.Bis > alt.Bis ? v.Bis : alt.Bis;
            Vertretungen.Aktualisieren(d, von, bis);
        });
        NachZellwechsel(Neuaufbau);
    }

    /// <summary>Kurznamen bzw. „Alle …“ / „Alle … außer …“ lesen.</summary>
    internal static List<int> Kurznamen(ElementTyp typ, string text) => VeranstaltungsAuswahl.Lesen(AppZustand.D, typ, text);

    private static Veranstaltung Kopie(Veranstaltung v) => System.Text.Json.JsonSerializer.Deserialize<Veranstaltung>(System.Text.Json.JsonSerializer.Serialize(v))!;

    private void KopierenMerken()
    {
        if (_g.CurrentRow?.Tag is not Veranstaltung v) return;
        _zwischenablage = Kopie(v);
    }

    /// <summary>Strg+V: kopierte Veranstaltung am eingestellten Datum einfügen.</summary>
    private void Einfuegen()
    {
        if (_zwischenablage is null) { Ui.Info("Bitte zuerst eine Veranstaltung markieren und mit Strg+C kopieren."); return; }
        var neu = VeranstaltungsLogik.Kopieren(_zwischenablage, Von);
        Speichern(neu, null);
    }

    private void Springen(int nr)
    {
        if (D.Veranstaltungen.FirstOrDefault(x => x.Id == nr) is not { } va) { Ui.Info($"Die Veranstaltung Nr. {nr} gibt es nicht."); return; }
        if (va.Bis < Von || va.Von > Bis)
        {
            _zeitraum.ArtSetzen(Zeitraumart.VonBis);
            _laedt = true;
            _von.Value = va.Von.ToDateTime(TimeOnly.MinValue);
            _bis.Value = va.Bis.ToDateTime(TimeOnly.MinValue);
            _laedt = false;
            Neuaufbau();
        }
        foreach (DataGridViewRow r in _g.Rows) if (r.Tag is Veranstaltung x && x.Id == nr) _g.CurrentCell = r.Cells[1];
    }

    private void Neu()
    {
        var v = new Veranstaltung { Von = Von, Bis = Von, Text = "Exkursion" };
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Speichern(v, null);
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Veranstaltung alt) return;
        var v = Kopie(alt);
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Speichern(v, alt);
    }

    private void Loeschen()
    {
        var liste = _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Veranstaltung>().Distinct().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Veranstaltung(en) löschen?")) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Veranstaltung löschen", AenderungsArt.Vertretung, d =>
        {
            d.Veranstaltungen.RemoveAll(v => ids.Contains(v.Id));
            Vertretungen.Aktualisieren(d, liste.Min(v => v.Von), liste.Max(v => v.Bis));
        });
    }
}
