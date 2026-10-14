using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Unterrichtsgruppen (Mehrwochenstundenplan): Name, Langname, Gültigkeit von–bis, aktive Wochen (A, B, …), Faktor
/// (Anteil der Schultage), Fix, Ignorieren und Unterbrechungen. Direkte Eingabe, unten immer eine leere Zeile.
/// </summary>
public sealed class UGruppenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private bool _laedt;

    private int Wochen => Math.Clamp(D.Einstellungen.Wochenperiodizitaet, 1, 16);

    public UGruppenFenster()
    {
        Text = "Unterrichtsgruppen";
        HilfeThema = "Unterricht";
        Icon = SymbolIcon("perioden");
        Size = new Size(860, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Unterrichtsgruppe löschen", "loeschen", Loeschen, false));
        ts.Items.Add(Knopf("Wochenperiodizität ändern…", "einstellungen", Periodizitaet, false));
        ts.Items.Add(new ToolStripLabel("   Unterbrechungen: z. B. \"01.12.-15.12.; 10.03.-20.03.\"") { ForeColor = Color.DimGray });
        Ui.RasterGestalten(_g, true);
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.CellEndEdit += ZelleGeaendert;
        _g.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _g.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _g.EndEdit(); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        if (_g.IsCurrentCellInEditMode) return;
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 0);
        _laedt = true;
        _g.Columns.Clear();
        _g.Rows.Clear();
        foreach (var (n, k, w) in new[] { ("N", "Name", 70), ("L", "Langname", 130), ("V", "von", 104), ("B", "bis", 104) })
            _g.Columns.Add(n is "V" or "B" ? Steuerelemente.DatumSpalte.Neu(n, k, w) : new DataGridViewTextBoxColumn { Name = n, HeaderText = k, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        for (var w = 0; w < Wochen; w++) _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "W" + w, HeaderText = ((char)('A' + w)).ToString(), Width = 28 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "F", HeaderText = "Faktor", Width = 55, ReadOnly = true });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "X", HeaderText = "Fix", Width = 35 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "I", HeaderText = "Ignore", Width = 45 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "U", HeaderText = "Unterbrechungen", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Z", HeaderText = "Unterrichte", Width = 70, ReadOnly = true });
        foreach (var ug in D.UGruppen)
        {
            var werte = new List<object?> { ug.Kurzname, ug.Langname, ug.Von is DateOnly v ? Format.Datum(v) : "", ug.Bis is DateOnly b ? Format.Datum(b) : "" };
            var aktiv = ug.Wochen.ToUpperInvariant();
            for (var w = 0; w < Wochen; w++) werte.Add(aktiv.Length == 0 || aktiv.Contains((char)('A' + w)));
            werte.Add(Kalender.UGruppeFaktor(D, ug).ToString("0.00"));
            werte.Add(ug.Fix);
            werte.Add(ug.Ignorieren);
            werte.Add(string.Join("; ", ug.Unterbrechungen.Select(z => $"{z.Von:dd.MM.}-{z.Bis:dd.MM.}")));
            werte.Add(D.AlleUnterrichte.Count(u => u.UGruppeId == ug.Id || u.Zeilen.Any(z => z.UGruppeId == ug.Id)));
            _g.Rows[_g.Rows.Add(werte.ToArray())].Tag = ug;
        }
        var neu = _g.Rows.Add();
        _g.Rows[neu].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 236);
        _laedt = false;
        if (pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count && pos.Item2 < _g.Columns.Count) _g.CurrentCell = _g.Rows[pos.Item1].Cells[pos.Item2];
    }

    private void ZelleGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0) return;
        var zeile = _g.Rows[e.RowIndex];
        var alt = zeile.Tag as UGruppe;
        string W(string n) => zeile.Cells[n].Value?.ToString()?.Trim() ?? "";
        if (alt is null && W("N").Length == 0) return;
        var ug = new UGruppe { Id = alt?.Id ?? 0 };
        try
        {
            ug.Kurzname = W("N").Length > 0 ? W("N") : alt?.Kurzname ?? "UG";
            ug.Langname = W("L");
            ug.Von = W("V").Length == 0 ? null : Ui.DatumLesen(W("V"), D.Einstellungen.SchuljahrVon);
            ug.Bis = W("B").Length == 0 ? null : Ui.DatumLesen(W("B"), D.Einstellungen.SchuljahrBis);
            var wochen = Enumerable.Range(0, Wochen).Where(w => zeile.Cells["W" + w].Value is true).Select(w => (char)('A' + w)).ToArray();
            ug.Wochen = wochen.Length == Wochen ? "" : new string(wochen);
            ug.Fix = zeile.Cells["X"].Value is true;
            ug.Ignorieren = zeile.Cells["I"].Value is true;
            ug.Unterbrechungen = W("U").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t =>
            {
                var teile = t.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var von = Ui.DatumLesen(teile[0], D.Einstellungen.SchuljahrVon);
                return new Zeitraum { Von = von, Bis = teile.Length > 1 ? Ui.DatumLesen(teile[1], von) : von };
            }).ToList();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            Ui.Info("Ungültige Eingabe (Datum TT.MM. bzw. TT.MM.JJJJ, Unterbrechungen \"von-bis; von-bis\").");
            BeginInvoke(Neuaufbau);
            return;
        }
        Aendern(alt is null ? "Unterrichtsgruppe anlegen" : "Unterrichtsgruppe ändern", AenderungsArt.Unterricht, d =>
        {
            if (alt is null) { ug.Id = d.NeueId(); d.UGruppen.Add(ug); }
            else d.UGruppen[d.UGruppen.FindIndex(x => x.Id == alt.Id)] = ug;
            Kalender.CacheLeeren();
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<UGruppe>().Distinct().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Unterrichtsgruppe(n) löschen? Zugeordneter Unterricht findet danach jede Woche statt.")) return;
        var ids = liste.Select(x => x.Id).ToHashSet();
        Aendern("Unterrichtsgruppe löschen", AenderungsArt.Unterricht, d =>
        {
            d.UGruppen.RemoveAll(x => ids.Contains(x.Id));
            foreach (var u in d.AlleUnterrichte)
            {
                if (u.UGruppeId is int g && ids.Contains(g)) u.UGruppeId = null;
                foreach (var z in u.Zeilen.Where(z => z.UGruppeId is int zg && ids.Contains(zg))) z.UGruppeId = null;
            }
            Kalender.CacheLeeren();
        });
    }

    private void Periodizitaet()
    {
        var t = Ui.Eingabe("Wochenperiodizität (1 = jede Woche gleich, 2 = A/B-Wochen, … bis 16):", "Wochenperiodizität", D.Einstellungen.Wochenperiodizitaet.ToString());
        if (t is null || !int.TryParse(t, out var n) || n < 1 || n > 16) return;
        Aendern("Wochenperiodizität", AenderungsArt.Alles, d => { d.Einstellungen.Wochenperiodizitaet = n; Kalender.CacheLeeren(); });
    }
}
