using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Veranst. / Lehrer: Exkursionen, Klassenfahrten, Schulveranstaltungen. Spalten wie in Untis (VA-Nr., Klasse, Kl-Teil, Lehrer, Raum,
/// Von, ab St., Bis, bis St., Grund, Text, zählen, Schülergruppe). Mehrere Klassen/Lehrer mit Komma oder in der Auswahlliste mit
/// Strg-Klick, Elemente aus dem Element-Rollup per Drag &amp; Drop, Strg+C / Strg+V kopiert eine Veranstaltung auf das eingestellte Datum.
/// Ist eine Lehrkraft wegen einer Absenz entfernt worden, steht im Feld „Lehrer“ rot „???“, bis eine Ersatzlehrkraft eingetragen ist.
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

        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = true;
        foreach (var (n, k, b) in new[]
        {
            ("Nr", "VA-Nr.", 50), ("K", "Klasse", 70), ("KT", "Kl-Teil", 60), ("L", "Lehrer", 80), ("R", "Raum", 55), ("V", "Von TT.MM.", 100), ("SV", "ab St.", 42),
            ("B", "Bis TT.MM.", 100), ("SB", "bis St.", 42),
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
        return tag is DateOnly t ? VeranstaltungsLogik.LehrerText(D, v, t, ",\n", "???") : string.Join(",\n", v.LehrerIds.Select(Ix.KLehrer));
    }

    /// <summary>Erster angezeigter Tag, an dem die Veranstaltung einen offenen Platz hat.</summary>
    private DateOnly? OffenerTag(Veranstaltung v) =>
        v.Ersatz.Where(e => e.ErsatzId is null && e.Datum >= Von && e.Datum <= Bis && v.LehrerIds.Contains(e.LehrerId)).Select(e => (DateOnly?)e.Datum).OrderBy(x => x).FirstOrDefault();

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 1);
        _laedt = true;
        _g.Rows.Clear();
        foreach (var v in D.Veranstaltungen.Where(v => v.Bis >= Von && v.Von <= Bis).OrderBy(v => v.Von).ThenBy(v => v.Id))
        {
            var r = _g.Rows.Add(v.Id, Ix.KKlassen(v.KlassenIds).Replace(",", ",\n"), Ix.KKlassen(v.KlassenteilIds).Replace(",", ",\n"), LehrerText(v),
                string.Join(",", v.RaumIds.Select(Ix.KRaum)), Datum(v.Von), v.StundeVon, Datum(v.Bis), v.StundeBis >= 99 ? D.Zeitraster.StundenProTag.ToString() : v.StundeBis.ToString(),
                D.Abwesenheitsgruende.FirstOrDefault(g => g.Id == v.GrundId)?.Kurzname ?? "", v.Text, v.Zaehlen, v.Schuelergruppe);
            _g.Rows[r].Tag = v;
        }
        var neu = _g.Rows.Add("", "", "", "", "", "", "", "", "", "", "", false, "");
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(255, 228, 190);
        _laedt = false;
        if (pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[Math.Clamp(pos.Item2, 0, _g.Columns.Count - 1)];
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
                case "K": v.KlassenIds = Kurznamen(ElementTyp.Klasse, wert); v.KlassenteilIds.RemoveAll(v.KlassenIds.Contains); break;
                case "KT": v.KlassenteilIds = Kurznamen(ElementTyp.Klasse, wert); v.KlassenIds.RemoveAll(v.KlassenteilIds.Contains); break;
                case "L":
                    var namen = Kurznamen(ElementTyp.Lehrer, wert.Replace("???", ""));
                    if (alt != null && OffenerTag(alt) is DateOnly tag)
                    {
                        // offene Plätze: neu eingetragene Lehrkräfte sind Ersatzlehrkräfte (nur für die Tage mit entfernter Lehrkraft)
                        var bisher = alt.LehrerAm(tag);
                        ersatz = namen.Where(n => !bisher.Contains(n) && !alt.LehrerIds.Contains(n)).ToList();
                        if (ersatz.Count == 0) v.LehrerIds = namen.Union(alt.Ersatz.Where(x => x.ErsatzId is null).Select(x => x.LehrerId)).Where(n => namen.Contains(n) || alt.LehrerIds.Contains(n)).Distinct().ToList();
                    }
                    else v.LehrerIds = namen;
                    break;
                case "R": v.RaumIds = Kurznamen(ElementTyp.Raum, wert); break;
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
            Aendern("Ersatzlehrkraft für Veranstaltung", AenderungsArt.Vertretung, d =>
            {
                var i = 0;
                foreach (var x in tage)
                    VeranstaltungsLogik.ErsatzSetzen(d, id, x.Datum, x.LehrerId, ersatz[Math.Min(i++, ersatz.Count - 1)]);
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
            var von = alt is null || v.Von < alt.Von ? v.Von : alt.Von;
            var bis = alt is null || v.Bis > alt.Bis ? v.Bis : alt.Bis;
            Vertretungen.Aktualisieren(d, von, bis);
        });
        NachZellwechsel(Neuaufbau);
    }

    internal static List<int> Kurznamen(ElementTyp typ, string text)
    {
        var alle = AppZustand.D.AlleElemente(typ).ToList();
        var res = new List<int>();
        foreach (var k in text.Split(new[] { ',', ' ', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            res.Add(alle.FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"{Ui.TypName(typ)} {k} unbekannt"));
        return res.Distinct().ToList();
    }

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
