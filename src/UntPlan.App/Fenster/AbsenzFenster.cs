using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Absenzen / Lehrer | Klassen | Räume (Reiter mit Anzahl wie im Vorbild). Direkte Eingabe in der Liste – Änderungen einer Zeile
/// werden beim Verlassen der Zeile (auch beim Reiter- oder Datumswechsel, beim Verlassen und Schließen des Fensters) übernommen.
/// Überlappt eine Lehrer-Absenz mit einer Veranstaltung, wird gefragt, ob die Lehrkraft aus der Veranstaltung entfernt werden soll.
/// </summary>
public sealed class AbwesenheitenFenster : MdiFenster
{
    private static readonly string[] Reiter = { "Lehrer", "Klassen", "Räume" };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Top, Height = 22 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    private readonly Steuerelemente.ZeitraumAuswahl _zeitraum;
    private readonly ToolStripMenuItem _vaZeigen = new("Veranstaltungen anzeigen") { CheckOnClick = true };
    private int _letzterReiter;
    private bool _laedt;
    private bool _neuaufbauAusstehend;

    // Entwurf der gerade bearbeiteten Zeile: Änderungen werden erst beim Verlassen der Zeile übernommen,
    // damit z. B. eine stundenweise Absenz nicht schon nach der ersten Eingabe Vertretungen auslöst.
    private Abwesenheit? _entwurf;
    private Abwesenheit? _entwurfAlt;
    private int _entwurfZeile = -1;
    private bool _entwurfBisGesetzt;
    /// <summary>Nach dem Speichern auf diese Absenz springen.</summary>
    private int? _springeZu;
    /// <summary>Fensterverknüpfung: in einem anderen Fenster gewähltes Element – seine Absenzen werden markiert.</summary>
    private (ElementTyp Typ, int Id)? _markiert;
    private bool _zuMarkierungSpringen;

    public AbwesenheitenFenster()
    {
        Text = "Absenzen / Lehrer";
        HilfeThema = "Abwesenheiten";
        Icon = SymbolIcon("abwesenheit");
        Size = new Size(780, 320);
        Positionieren();
        foreach (var t in Reiter) _tabs.TabPages.Add(t + " (0)");
        // Reiterwechsel: offenen Entwurf zuerst übernehmen (früher blieb die alte Liste unter dem neuen Reiter stehen)
        _tabs.Selecting += (_, _) => EntwurfSofortUebernehmen();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == _letzterReiter) return;
            _letzterReiter = _tabs.SelectedIndex;
            Text = "Absenzen / " + (Art switch { AbwesenheitsArt.Lehrer => "Lehrer", AbwesenheitsArt.Klasse => "Klasse", _ => "Raum" });
            Neuaufbau();
        };
        var ts = Werkzeugleiste();
        ts.Items.Add(VerknuepfenKnopf());
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Neue Absenz (Dialog)", "neu", Neu));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Absenz teilen", "tausch", Teilen));
        ts.Items.Add(Knopf("Absenz kopieren (andere Elemente)", "kopieren", Kopieren));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Veranstaltungen", "veranstaltung", () => HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster())));
        var einst = new ToolStripDropDownButton("Einstellungen", Symbole.Get("einstellungen", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Einstellungen" };
        _vaZeigen.Checked = AppZustand.Einstellungen.AbsenzenVeranstaltungenZeigen;
        _vaZeigen.CheckedChanged += (_, _) =>
        {
            AppZustand.Einstellungen.AbsenzenVeranstaltungenZeigen = _vaZeigen.Checked;
            AppZustand.Einstellungen.Speichern();
            EntwurfSofortUebernehmen();
            Neuaufbau();
        };
        einst.DropDownItems.Add(_vaZeigen);
        ts.Items.Add(einst);

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nr", HeaderText = "AbsNr.", Width = 55, ReadOnly = true });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("E", "Lehrer", 80, Array.Empty<string>()));
        foreach (var (n, k, b) in new[] { ("V", "Von TT.MM.", 108), ("SV", "ab St.", 45), ("B", "Bis TT.MM.", 108), ("SB", "bis St.", 45) })
            _g.Columns.Add(n is "V" or "B" ? Steuerelemente.DatumSpalte.Neu(n, k, b) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = b });
        _g.Columns.Add(Steuerelemente.SuchKombi.Spalte("G", "Grund", 70, D.Abwesenheitsgruende.Select(x => x.Kurzname)));
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "T", HeaderText = "Text", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Beg", HeaderText = "Begleitlehrer", Width = 100 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "O", HeaderText = "offen", Width = 45, ReadOnly = true });
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        Steuerelemente.SuchKombi.Aktivieren(_g, (zelle, text) =>
        {
            Element? el = zelle.OwningColumn.Name == "G" ? D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname == text) : D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname == text);
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el is Lehrer l ? l.VollerName : el.Langname);
        });
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellBeginEdit += (_, e) => { if (e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is Veranstaltung) e.Cancel = true; };
        _g.RowValidated += (_, e) => { if (e.RowIndex == _entwurfZeile) EntwurfUebernehmen(); };
        // Entwurf nicht verlieren: beim Verlassen des Fensters und beim Schließen sofort (nicht verzögert) speichern
        if (AppZustand.Haupt != null)
        {
            void Gewechselt(object? o, EventArgs e) { if (!IsDisposed && AppZustand.Haupt.ActiveMdiChild != this) EntwurfSofortUebernehmen(); }
            AppZustand.Haupt.MdiChildActivate += Gewechselt;
            FormClosed += (_, _) => AppZustand.Haupt.MdiChildActivate -= Gewechselt;
        }
        FormClosing += (_, _) => EntwurfSofortUebernehmen();
        _g.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            if (_g.Rows[e.RowIndex].Tag is Abwesenheit) Bearbeiten();
            else if (_g.Rows[e.RowIndex].Tag is Veranstaltung) HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster());
        };
        _g.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null) return;
            if (_g.Rows[e.RowIndex].Tag is Veranstaltung)
            {
                e.CellStyle.BackColor = Color.FromArgb(226, 240, 226);
                e.CellStyle.ForeColor = Color.FromArgb(40, 90, 40);
            }
        };
        _g.KeyDown += (_, e) =>
        {
            if (_g.IsCurrentCellInEditMode) return;
            if (e.KeyCode == Keys.Escape && _entwurf != null) { EntwurfVerwerfen(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter && _entwurf != null && e.Modifiers == Keys.Control) { EntwurfSofortUebernehmen(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.Control) { Loeschen(); e.Handled = true; }
            if (e.KeyCode == Keys.Insert) Neu();
        };
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Neue Absenz…", Symbole.Get("neu"), (_, _) => Neu());
        cm.Items.Add("Bearbeiten…", Symbole.Get("einstellungen"), (_, _) => Bearbeiten());
        cm.Items.Add("Absenz teilen…", Symbole.Get("tausch"), (_, _) => Teilen());
        cm.Items.Add("Absenz kopieren…", null, (_, _) => Kopieren());
        cm.Items.Add("Absenzen zusammenfassen", null, (_, _) => Zusammenfassen());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
        _g.ContextMenuStrip = cm;

        _von.Value = _bis.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _zeitraum = new Steuerelemente.ZeitraumAuswahl(_von, _bis, "Absenzen");
        _zeitraum.Geaendert += () =>
        {
            if (_laedt) return;
            EntwurfSofortUebernehmen();
            Neuaufbau();
            // Datumsauswahl gilt für alle verknüpften Fenster (Vertretungsliste, Stundenpläne, Menüband)
            if (Verknuepft && (Von == Bis || _zeitraum.Art != Steuerelemente.Zeitraumart.VonBis) && AppZustand.Datum != Von) AppZustand.DatumSetzen(Von);
        };
        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(4, 2, 0, 0), BackColor = Ui.Kopf, WrapContents = false };
        var gruppe = new GroupBox { Text = "Von-Bis", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), MinimumSize = new Size(0, 50) };
        gruppe.Controls.Add(_zeitraum);
        _zeitraum.Location = new Point(6, 18);
        kopf.Controls.Add(gruppe);

        Controls.Add(_g);
        Controls.Add(_tabs);
        Controls.Add(kopf);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        AppZustand.VertretungGewaehlt += VertretungGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
            AppZustand.VertretungGewaehlt -= VertretungGewaehlt;
        };
        Neuaufbau();
    }

    /// <summary>Fensterverknüpfung: Lehrer, Klasse bzw. Raum wurde in einem anderen Fenster gewählt – Reiter wechseln, Absenzen markieren.</summary>
    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        var art = typ switch { ElementTyp.Lehrer => 0, ElementTyp.Klasse => 1, ElementTyp.Raum => 2, _ => -1 };
        if (art < 0) return;
        Markieren(typ, id, art);
    }

    /// <summary>Fensterverknüpfung: Stunde in einem Stundenplan gewählt – Absenzen ihrer Lehrkraft (bzw. Klasse, Raum im jeweiligen Reiter) markieren.</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        int? id = Art switch
        {
            AbwesenheitsArt.Lehrer => (AppZustand.AuswahlLehrer is { Count: > 0 } l ? l : u.LehrerIds).Cast<int?>().FirstOrDefault(),
            AbwesenheitsArt.Klasse => (AppZustand.AuswahlKlassen is { Count: > 0 } k ? k : u.KlassenIds).Cast<int?>().FirstOrDefault(),
            _ => (AppZustand.AuswahlRaeume is { Count: > 0 } r ? r : p?.Raeume.Where(x => x.HasValue).Select(x => x!.Value).ToList() ?? new List<int>()).Cast<int?>().FirstOrDefault(),
        };
        if (id is int i) Markieren(Typ, i, _tabs.SelectedIndex);
    }

    /// <summary>Fensterverknüpfung: gewählte Vertretung – Absenz der vertretenen Lehrkraft (bzw. Klasse) markieren.</summary>
    private void VertretungGewaehlt(Vertretung? v)
    {
        if (v is null || !Verknuepft || IsDisposed) return;
        if (Art == AbwesenheitsArt.Klasse && v.KlassenIds.Count > 0) Markieren(ElementTyp.Klasse, v.KlassenIds[0], 1);
        else if (v.LehrerId is int l) Markieren(ElementTyp.Lehrer, l, 0);
    }

    private void Markieren(ElementTyp typ, int id, int reiter)
    {
        // laufende Eingaben nicht stören
        if (_g.IsCurrentCellInEditMode || _entwurf != null || ContainsFocus) return;
        if (_markiert == (typ, id) && _tabs.SelectedIndex == reiter) return;
        _markiert = (typ, id);
        _zuMarkierungSpringen = true;
        if (_tabs.SelectedIndex != reiter) _tabs.SelectedIndex = reiter; // baut neu auf
        else Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || !Verknuepft) return;
        EntwurfSofortUebernehmen();
        if (_zeitraum.Art != Steuerelemente.Zeitraumart.VonBis)
        {
            _laedt = true;
            _von.Value = datum.ToDateTime(TimeOnly.MinValue);
            _laedt = false;
        }
        Neuaufbau();
    }

    private AbwesenheitsArt Art => (AbwesenheitsArt)Math.Max(0, _tabs.SelectedIndex);

    private ElementTyp Typ => TypVon(Art);

    private static ElementTyp TypVon(AbwesenheitsArt a) => a switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };

    private DateOnly Von => DateOnly.FromDateTime(_von.Value);
    private DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    private int LetzteStunde => Math.Max(1, D.Zeitraster.StundenProTag);

    private static string TagText(DateOnly d) => $"{Planungsdienst.TageKurz[((int)d.DayOfWeek + 6) % 7]} {d:dd.MM.yy}";

    private static string? Schluessel(DataGridViewRow r) => r.Tag switch { Abwesenheit a => "A" + a.Id, Veranstaltung v => "VA" + v.Id, _ => "neu" };

    private void Neuaufbau()
    {
        // während der Eingabe bzw. mit offenem Entwurf nicht neu aufbauen (Eingaben gingen verloren) – später nachholen
        if (_g.IsCurrentCellInEditMode || _entwurf != null)
        {
            _neuaufbauAusstehend = true;
            return;
        }
        _neuaufbauAusstehend = false;
        // aktuelle Absenz (Nr.), Spalte und Bildlauf merken – nach dem Neuaufbau wiederherstellen
        var stand = Steuerelemente.TabellenStand.Merken(_g, Schluessel);
        if (_springeZu is int sp) stand.AktuellSetzen("A" + sp);
        _springeZu = null;
        _laedt = true;
        _g.Rows.Clear();
        var namen = (DataGridViewComboBoxColumn)_g.Columns["E"]!;
        namen.HeaderText = Art switch { AbwesenheitsArt.Lehrer => "Lehrer", AbwesenheitsArt.Klasse => "Klasse", _ => "Raum" };
        namen.Items.Clear();
        namen.Items.Add("");
        foreach (var el in Stammdaten.Sortiert(D, Typ)) namen.Items.Add(el.Kurzname);
        var gruende = (DataGridViewComboBoxColumn)_g.Columns["G"]!;
        gruende.Items.Clear();
        gruende.Items.Add("");
        foreach (var gr in D.Abwesenheitsgruende) gruende.Items.Add(gr.Kurzname);
        var (von, bis) = (Von, Bis);
        var typ = Typ;
        _g.Columns["Beg"]!.Visible = Art == AbwesenheitsArt.Klasse;
        foreach (var a in D.Abwesenheiten.Where(a => a.Art == Art && a.Bis >= von && a.Von <= bis).OrderBy(a => Ix.K(typ, a.ElementId), StringComparer.Create(Format.De, true)).ThenBy(a => a.Von))
        {
            var offen = D.Vertretungen.Count(v => v.AbwesenheitId == a.Id && v.OffenOderRaumOffen);
            var grund = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
            var r = _g.Rows.Add(a.Id, Ix.K(typ, a.ElementId), TagText(a.Von), a.StundeVon, TagText(a.Bis), Math.Min(a.StundeBis, LetzteStunde).ToString(), grund, a.Text,
                string.Join(", ", a.BegleitLehrerIds.Select(Ix.KLehrer)), offen == 0 ? "" : offen.ToString());
            _g.Rows[r].Tag = a;
            if (offen > 0) _g.Rows[r].Cells["O"].Style.ForeColor = Color.Firebrick;
            // Fensterverknüpfung: Absenzen des in einem anderen Fenster gewählten Elements hervorheben
            if (_markiert is { } m && m.Typ == typ && m.Id == a.ElementId) _g.Rows[r].DefaultCellStyle.BackColor = Ui.ZelleAktivVertretung;
        }
        // Veranstaltungen zur Information (Einstellungen „Veranstaltungen anzeigen“)
        if (_vaZeigen.Checked)
            foreach (var va in D.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= bis).OrderBy(v => v.Von))
            {
                var elemente = Art switch
                {
                    // viele Elemente kompakt („Alle Lehrkräfte“, „Alle Klassen außer 5a“)
                    AbwesenheitsArt.Lehrer => VeranstaltungsAuswahl.Kurztext(D, ElementTyp.Lehrer, va.LehrerIds, va.Von, va.Bis),
                    AbwesenheitsArt.Klasse => VeranstaltungsLogik.KlassenKurztext(D, va),
                    _ => VeranstaltungsAuswahl.Kurztext(D, ElementTyp.Raum, va.RaumIds, va.Von, va.Bis),
                };
                if (elemente.Length == 0) continue;
                var r = _g.Rows.Add($"VA {va.Id}", "", TagText(va.Von), va.StundeVon, TagText(va.Bis), va.StundeBis >= 99 ? LetzteStunde.ToString() : va.StundeBis.ToString(),
                    D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == va.GrundId)?.Kurzname ?? "", $"Veranstaltung: {elemente} {va.Text}".Trim(), "", "");
                _g.Rows[r].Tag = va;
                _g.Rows[r].ReadOnly = true;
            }
        // leere Eingabezeile
        var neu = _g.Rows.Add("", "", "", "", "", "", "", "", "", "");
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(255, 228, 190);
        _g.Rows[neu].Cells["Nr"].Value = "";
        _laedt = false;
        if (_zuMarkierungSpringen && _markiert is { } mk && _g.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag is Abwesenheit ab && ab.ElementId == mk.Id && mk.Typ == typ) is { } ziel)
            stand.AktuellSetzen("A" + ((Abwesenheit)ziel.Tag!).Id);
        _zuMarkierungSpringen = false;
        stand.Wiederherstellen(_g, Schluessel, false);
        for (var i = 0; i < 3; i++)
        {
            var art = (AbwesenheitsArt)i;
            _tabs.TabPages[i].Text = $"{Reiter[i]} ({D.Abwesenheiten.Count(a => a.Art == art && a.Bis >= von && a.Von <= bis)})";
        }
    }

    private List<Abwesenheit> Markierte() => _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Abwesenheit>().Distinct().ToList();

    /// <summary>Direkte Eingabe: geänderte Zelle in den Entwurf der Zeile übernehmen (gespeichert wird beim Zeilenwechsel).</summary>
    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        if (zeile.Tag is Veranstaltung) return;
        var wert = zeile.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        var alt = zeile.Tag as Abwesenheit;
        if (_entwurfZeile != e.RowIndex)
        {
            if (_entwurf != null) EntwurfUebernehmen();
            _entwurfAlt = alt;
            // neue Absenz: Standard ist das Planungsdatum bzw. der angezeigte Tag, 1. bis letzte Stunde
            var start = Von;
            _entwurf = alt is null
                ? new Abwesenheit { Art = Art, Von = start, Bis = start, StundeVon = 1, StundeBis = LetzteStunde, Benutzer = AppZustand.Benutzer }
                : Kopie(alt);
            _entwurfBisGesetzt = alt != null;
            _entwurfZeile = e.RowIndex;
        }
        var a = _entwurf!;
        var name = _g.Columns[e.ColumnIndex].Name;
        string? meldung = null;
        try
        {
            switch (name)
            {
                case "E":
                    var el = D.AlleElemente(Typ).FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase));
                    if (el is null && wert.Length > 0) meldung = $"{Ui.TypName(Typ)} \"{wert}\" ist nicht vorhanden.";
                    a.ElementId = el?.Id ?? 0;
                    break;
                case "V":
                    a.Von = Ui.DatumLesen(wert, a.Von);
                    // Bis nur mitziehen, solange es nicht selbst eingegeben wurde (früher ging ein zuerst eingegebenes Bis verloren)
                    if (a.Bis < a.Von || !_entwurfBisGesetzt) a.Bis = a.Von;
                    break;
                case "B":
                    a.Bis = Ui.DatumLesen(wert, a.Bis);
                    _entwurfBisGesetzt = true;
                    if (a.Bis < a.Von) a.Von = a.Bis;
                    break;
                case "SV": a.StundeVon = wert.Length == 0 ? 1 : Math.Clamp(int.Parse(wert), 1, LetzteStunde); break;
                case "SB": a.StundeBis = wert.Length == 0 ? LetzteStunde : Math.Clamp(int.Parse(wert), 1, LetzteStunde); break;
                case "G": a.GrundId = D.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id; break;
                case "Beg":
                    var teile = wert.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var unbekannt = teile.Where(k => !D.Lehrer.Any(l => l.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (unbekannt.Count > 0) meldung = $"Unbekannte Lehrer: {string.Join(", ", unbekannt)}";
                    a.BegleitLehrerIds = teile.Select(k => D.Lehrer.FirstOrDefault(l => l.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id).OfType<int>().Distinct().ToList();
                    break;
                case "T": a.Text = wert; break;
                default: return;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            meldung = $"Die Eingabe \"{wert}\" ist ungültig.";
        }
        EntwurfAnzeigen(zeile, a);
        // Meldungen erst nach dem Übernehmen in den Entwurf zeigen
        if (meldung != null) Ui.Info(meldung);
    }

    /// <summary>Entwurf in der Zeile anzeigen (normalisierte Werte, Kennzeichnung „nicht gespeichert“).</summary>
    private void EntwurfAnzeigen(DataGridViewRow zeile, Abwesenheit a)
    {
        _laedt = true;
        zeile.Cells["E"].Value = a.ElementId != 0 ? Ix.K(Typ, a.ElementId) : "";
        zeile.Cells["V"].Value = TagText(a.Von);
        zeile.Cells["B"].Value = TagText(a.Bis);
        zeile.Cells["SV"].Value = a.StundeVon.ToString();
        zeile.Cells["SB"].Value = Math.Min(a.StundeBis, LetzteStunde).ToString();
        zeile.Cells["G"].Value = D.Abwesenheitsgruende.FirstOrDefault(x => x.Id == a.GrundId)?.Kurzname ?? "";
        zeile.Cells["Nr"].Value = zeile.Tag is Abwesenheit ab ? $"{ab.Id} *" : "neu *";
        zeile.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 214);
        _laedt = false;
    }

    /// <summary>Zeile verlassen: Entwurf speichern (neue Absenz nur mit gewähltem Element). Verzögert, damit die Tabelle ihren Zeilenwechsel beendet.</summary>
    private void EntwurfUebernehmen() => EntwurfAbschliessen(false);

    /// <summary>Entwurf sofort speichern (Reiter-/Datumswechsel, Fenster verlassen oder schließen).</summary>
    private void EntwurfSofortUebernehmen()
    {
        if (_g.IsCurrentCellInEditMode) _g.EndEdit();
        EntwurfAbschliessen(true);
    }

    private void EntwurfAbschliessen(bool sofort)
    {
        if (_entwurf is null)
        {
            if (_neuaufbauAusstehend && !_g.IsCurrentCellInEditMode) NachZellwechsel(Neuaufbau);
            return;
        }
        var a = _entwurf;
        var alt = _entwurfAlt;
        _entwurf = null;
        _entwurfAlt = null;
        _entwurfZeile = -1;
        if (a.ElementId == 0)
        {
            // ohne Element keine Absenz – Eingaben verwerfen
            if (alt != null) Ui.Info("Ohne Element kann die Absenz nicht gespeichert werden – die Änderung wurde verworfen.");
            if (sofort || !IsHandleCreated) Neuaufbau(); else NachZellwechsel(Neuaufbau);
            return;
        }
        var text = alt is null ? "Absenz eingeben" : "Absenz geändert";
        if (sofort || !IsHandleCreated || IsDisposed) Speichern(new[] { a }, alt is null ? Array.Empty<Abwesenheit>() : new[] { alt }, text);
        else NachZellwechsel(() => Speichern(new[] { a }, alt is null ? Array.Empty<Abwesenheit>() : new[] { alt }, text));
    }

    private void EntwurfVerwerfen()
    {
        if (_entwurf is null) return;
        _entwurf = null;
        _entwurfAlt = null;
        _entwurfZeile = -1;
        Neuaufbau();
    }

    private static Abwesenheit Kopie(Abwesenheit a) => new()
    {
        Id = a.Id, Art = a.Art, ElementId = a.ElementId, Von = a.Von, Bis = a.Bis, StundeVon = a.StundeVon, StundeBis = a.StundeBis,
        GrundId = a.GrundId, Text = a.Text, BegleitLehrerIds = new List<int>(a.BegleitLehrerIds), Erstellt = a.Erstellt, Benutzer = a.Benutzer,
    };

    private void Neu()
    {
        EntwurfSofortUebernehmen();
        var a = new Abwesenheit { Art = Art, Von = Von, Bis = Von };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, Array.Empty<Abwesenheit>(), "Absenz eingeben");
    }

    private void Bearbeiten()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var a = Kopie(alt);
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, new[] { alt }, "Absenz geändert");
    }

    private void Teilen()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        if (alt.Von == alt.Bis) { Ui.Info("Eine eintägige Absenz kann nicht geteilt werden."); return; }
        var text = Ui.Eingabe($"Absenz {Format.Datum(alt.Von)} – {Format.Datum(alt.Bis)} teilen: zweiter Teil beginnt am (TT.MM.JJJJ):", "Absenz teilen", Format.Datum(alt.Von.AddDays(1)));
        if (text is null || !DateOnly.TryParse(text, Format.De, out var ab) || ab <= alt.Von || ab > alt.Bis) return;
        var a = Kopie(alt);
        a.Bis = ab.AddDays(-1);
        a.StundeBis = 99;
        var b = Kopie(alt);
        b.Id = 0;
        b.Von = ab;
        b.StundeVon = 1;
        Speichern(new[] { a, b }, new[] { alt }, "Absenz teilen");
    }

    private void Kopieren()
    {
        EntwurfSofortUebernehmen();
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var w = AuswahlDialog.Mehrere(this, TypVon(alt.Art), "Absenz für weitere Elemente übernehmen", Array.Empty<int>());
        if (w is null || w.Count == 0) return;
        var neu = w.Where(e => e.Id != alt.ElementId).Select(e => { var a = Kopie(alt); a.Id = 0; a.ElementId = e.Id; a.Erstellt = DateTime.Now; return a; }).ToArray();
        Speichern(neu, Array.Empty<Abwesenheit>(), "Absenz kopieren");
    }

    private void Zusammenfassen()
    {
        EntwurfSofortUebernehmen();
        var m = Markierte().OrderBy(a => a.Von).ToList();
        if (m.Count < 2 || m.Select(a => (a.Art, a.ElementId)).Distinct().Count() > 1) { Ui.Info("Bitte mindestens zwei Absenzen desselben Elements markieren."); return; }
        var a = Kopie(m[0]);
        a.Bis = m.Max(x => x.Bis);
        a.StundeBis = m.First(x => x.Bis == a.Bis).StundeBis;
        Speichern(new[] { a }, m.ToArray(), "Absenzen zusammenfassen");
    }

    private void Speichern(IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt, string text)
    {
        // Hinweis: eingeplante Vertreter, die durch die neue Absenz abwesend werden
        var betroffen = neu.SelectMany(a => Vertretungen.BetroffeneVertreter(D, a)).Distinct().ToList();
        List<Abwesenheit> gespeichert = new();
        if (!Aendern(text, AenderungsArt.Vertretung, d => gespeichert = AbsenzVerwaltung.Speichern(d, neu, alt))) return;
        if (gespeichert.Count == 1) _springeZu = gespeichert[0].Id;
        if (neu.Count > 0 && gespeichert.Count == 0 && alt.Count == 0) Ui.Info("Diese Absenz ist bereits vorhanden.");
        if (betroffen.Count > 0)
            Ui.Info("Achtung: Folgende Vertretungen waren bereits mit einer Lehrkraft geplant, die nun abwesend ist. " +
                "Sie sind wieder offen und müssen neu vertreten werden:\n\n" +
                string.Join("\n", betroffen.Take(20).Select(v => $"Vtr-Nr. {v.Id}: {Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7]} {Format.Datum(v.Datum)}, {v.Stunde}. Std., {Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)} – Vertreter {Ix.KLehrer(v.VertreterId)}")) +
                (betroffen.Count > 20 ? $"\n… und {betroffen.Count - 20} weitere" : ""), "Vertreter abwesend");
        VeranstaltungenPruefen(this, gespeichert, (t, a) => Aendern(t, AenderungsArt.Vertretung, a));
        // Lehrkraft wird abwesend: Verlegungen und Sondereinsätze im Absenzzeitraum rückgängig machen bzw. löschen?
        VerlegungsAbfrage.NachAbsenz(gespeichert, Aendern);
        Neuaufbau();
    }

    /// <summary>
    /// Lehrer-Absenzen, die mit einer Veranstaltung überlappen: wie im Vorbild nachfragen, ob die Lehrkraft aus der Veranstaltung
    /// entfernt werden soll (nur für die Tage der Absenz). Bei Ja bleibt ihr Platz als „?“ offen.
    /// </summary>
    internal static void VeranstaltungenPruefen(IWin32Window owner, IEnumerable<Abwesenheit> absenzen, Func<string, Action<Schuldatei>, bool> aendern)
    {
        var d = AppZustand.D;
        foreach (var a in absenzen.Where(a => a.Art == AbwesenheitsArt.Lehrer).ToList())
            foreach (var u in VeranstaltungsLogik.Ueberlappungen(d, a))
            {
                var frage = MessageBox.Show(owner, VeranstaltungsLogik.FrageText(d, a, u), "UntPlan - Frage", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (frage != DialogResult.Yes) continue;
                var vaId = u.Veranstaltung.Id;
                var tage = u.Tage.ToList();
                var absId = a.Id;
                if (aendern("Lehrer aus Veranstaltung entfernt", x => VeranstaltungsLogik.LehrerEntfernen(x, vaId, u.LehrerId, tage, absId)))
                    MessageBox.Show(owner, VeranstaltungsLogik.HinweisText(AppZustand.D, u), "UntPlan - Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
    }

    private void Loeschen()
    {
        EntwurfSofortUebernehmen();
        var liste = Markierte();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Absenz(en) löschen? Daraus entstandene Vertretungen werden entfernt.")) return;
        Speichern(Array.Empty<Abwesenheit>(), liste, "Absenz löschen");
    }
}
