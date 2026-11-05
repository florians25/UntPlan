using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Totaldiagnose wie im Handbuch (4.4.7): oben Datum (Woche) mit Schulwoche/Kalenderwoche, Auswahl „Klasse(n)“ / „Lehrer“, „Aktualisieren“,
/// „Druck“, „Punkte total“ und „Nicht verplant total“; darunter je Element Punkte, Nvp. und „Die 3 schlechtesten Stunden“ (Stunde, Punkte)
/// sowie „Grund“. Ein Klick auf eine Punktezahl zeigt den Grund und synchronisiert einen Stundenplan auf diese Stunde.
/// </summary>
public sealed class TotaldiagnoseFenster : MdiFenster
{
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Short, Left = 6, Top = 6, Width = 104 };
    private readonly ComboBox _art = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 122, Top = 6, Width = 80 };
    private readonly Label _woche = new() { Left = 6, Top = 34, Width = 110, Height = 32 };
    private readonly Label _summen = new() { Left = 122, Top = 34, Width = 150, Height = 32 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill, Name = "Totaldiagnose" };
    private List<TotaldiagnoseZeile> _zeilen = new();

    public TotaldiagnoseFenster()
    {
        Text = "Totaldiagnose";
        HilfeThema = "Diagnose";
        Icon = SymbolIcon("diagnose");
        Size = new Size(640, 360);
        Positionieren();
        var kopf = new Panel { Dock = DockStyle.Top, Height = 72 };
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _art.Items.AddRange(new object[] { "Klasse(n)", "Lehrer" });
        _art.SelectedIndex = 0;
        var akt = new Button { Text = "Aktualisieren", Left = 290, Top = 5, Width = 96, Height = 25 };
        var druck = new Button { Text = "Druck", Left = 290, Top = 36, Width = 96, Height = 25 };
        akt.Click += (_, _) => Neuaufbau();
        druck.Click += (_, _) => ListenDruck.DruckenTabelle(this, $"Totaldiagnose – {_art.Text}", _g, true);
        _datum.ValueChanged += (_, _) => Neuaufbau();
        _art.SelectedIndexChanged += (_, _) => Neuaufbau();
        kopf.Controls.AddRange(new Control[] { _datum, _art, _woche, _summen, akt, druck });
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.AllowUserToAddRows = false;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.MultiSelect = false;
        foreach (var (name, text, b) in new[]
                 {
                     ("E", "", 50), ("P", "Punkte", 60), ("N", "Nvp.", 40), ("S1", "1.", 50), ("P1", "Punkte", 56), ("S2", "2.", 50), ("P2", "Punkte", 56),
                     ("S3", "3.", 50), ("P3", "Punkte", 56), ("G", "Grund", 160),
                 })
        {
            var c = _g.Columns[_g.Columns.Add(name, text)];
            c.Width = b;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        _g.Columns["S1"]!.ToolTipText = "Die 3 schlechtesten Stunden";
        _g.CellClick += (_, e) => Klick(e.RowIndex, e.ColumnIndex);
        Controls.Add(_g);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 18, Text = "Die 3 schlechtesten Stunden", Padding = new Padding(196, 2, 0, 0), BackColor = Ui.Kopf });
        Controls.Add(kopf);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art != AenderungsArt.Vertretung) Neuaufbau();
    }

    private ElementTyp Typ => _art.SelectedIndex == 1 ? ElementTyp.Lehrer : ElementTyp.Klasse;

    private void Neuaufbau()
    {
        var datum = DateOnly.FromDateTime(_datum.Value);
        var woche = Diagnose.FuerWoche(D, datum);
        _woche.Text = $"Schulwoche: {Kalender.Schulwoche(D, datum)}\nKalenderwoche: {System.Globalization.ISOWeek.GetWeekOfYear(_datum.Value)}";
        using (new Dialoge.WarteCursor()) _zeilen = Totaldiagnose.Berechnen(woche, Typ);
        var wix = new DatenIndex(woche);
        var nvp = _zeilen.ToDictionary(z => z.ElementId, z => wix.UnterrichteVon(Typ, z.ElementId).Sum(u => wix.NichtVerplant(u)));
        _summen.Text = $"Punkte total: {_zeilen.Sum(z => z.Punkte):N0}\nNicht verplant total: {woche.Unterrichte.Where(u => !u.Ignorieren).Sum(u => wix.NichtVerplant(u))}";
        _g.Rows.Clear();
        foreach (var z in _zeilen)
        {
            var s = z.Stunden;
            string Std(int i) => i < s.Count ? $"{Planungsdienst.Tagname(woche, s[i].Tag, true)}-{s[i].Stunde}" : "";
            string Pkt(int i) => i < s.Count ? s[i].Punkte.ToString("N0") : "";
            var r = _g.Rows.Add(z.Name, z.Punkte.ToString("N0"), nvp[z.ElementId] > 0 ? nvp[z.ElementId].ToString() : "", Std(0), Pkt(0), Std(1), Pkt(1), Std(2), Pkt(2), s.Count > 0 ? s[0].Grund : "");
            _g.Rows[r].Tag = z;
        }
    }

    private void Klick(int zeile, int spalte)
    {
        if (zeile < 0 || spalte < 0 || _g.Rows[zeile].Tag is not TotaldiagnoseZeile z) return;
        var name = _g.Columns[spalte].Name;
        var i = name is "S1" or "P1" ? 0 : name is "S2" or "P2" ? 1 : name is "S3" or "P3" ? 2 : -1;
        if (i < 0 || i >= z.Stunden.Count) return;
        var s = z.Stunden[i];
        _g.Rows[zeile].Cells["G"].Value = s.Grund;
        // begleitender Stundenplan synchronisiert auf diese Stunde
        if (!Application.OpenForms.OfType<StundenplanFenster>().Any()) StundenplanFenster.Neu(z.Typ, z.ElementId, false);
        AppZustand.Waehlen(z.Typ, z.ElementId, this);
        AppZustand.ZelleWaehlen(new PlanZelle(s.Tag, s.Stunde), z.Typ, z.ElementId, this);
        Activate();
    }
}

/// <summary>
/// CCC-Analyse (Handbuch 4.4.8): Symbolleiste (Analyse starten, Abbrechen, Anzeige bzw. Druck der Liste der Lehrerteams), oben „Unterricht:
/// Nr (n/m)“ und „Untersuchte Variationen“, in der Mitte die Ketten (Wst, Unterrichte mit kritischer Lehrkraft in Klammern), unten die
/// CCC-Lupe (Unt., Wst, Klassen, Lehrer – der gewählte Unterricht hellblau, die Gründe der Unverträglichkeit rot).
/// </summary>
public sealed class CccFenster : MdiFenster
{
    private readonly Label _unterricht = new() { Left = 6, Top = 6, Width = 180, Text = "Unterricht:" };
    private readonly Label _variationen = new() { Left = 8, Top = 18, Width = 300 };
    private readonly Label _hinweis = new() { Left = 6, Top = 30, Width = 300, Height = 32, ForeColor = Color.Firebrick };
    private readonly DataGridView _ketten = new() { Dock = DockStyle.Fill, Name = "Ketten" };
    private readonly DataGridView _lupe = new() { Dock = DockStyle.Fill, Name = "Lupe" };
    private readonly ToolStripButton _start, _abbrechen;
    private CancellationTokenSource? _cts;
    private CccErgebnis? _erg;

    public CccFenster()
    {
        Text = "CCC (Critical-Conflict-Chain) Analyse von Kopplungen";
        HilfeThema = "Diagnose";
        Icon = SymbolIcon("diagnose");
        Size = new Size(860, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        _start = Knopf("Analyse starten", "aktualisieren", Starten);
        _abbrechen = Knopf("Abbrechen", "loeschen", () => _cts?.Cancel());
        ts.Items.Add(Knopf("Anzeige der Liste der Lehrerteams", "lehrer", () => Lehrerteamliste.Zeigen(this, false)));
        ts.Items.Add(_abbrechen);
        ts.Items.Add(_start);
        ts.Items.Add(Knopf("Druck der Liste der Lehrerteams", "drucken", () => Lehrerteamliste.Zeigen(this, true)));
        var oben = new Panel { Dock = DockStyle.Top, Height = 64 };
        var var = new GroupBox { Text = "Untersuchte Variationen", Left = 200, Top = 0, Width = 320, Height = 44 };
        var.Controls.Add(_variationen);
        oben.Controls.AddRange(new Control[] { _unterricht, var, _hinweis });
        foreach (var g in new[] { _ketten, _lupe })
        {
            Ui.RasterGestalten(g, false);
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.CellSelect;
            g.MultiSelect = false;
        }
        _ketten.CellClick += (_, e) => LupeZeigen(e.RowIndex, e.ColumnIndex);
        _lupe.CellFormatting += LupeFormatieren;
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        split.Panel1.Controls.Add(_ketten);
        split.Panel2.Controls.Add(_lupe);
        Controls.Add(split);
        Controls.Add(oben);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(120, split.Height / 2);
        Shown += (_, _) => Starten();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosed(e);
    }

    private async void Starten()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _start.Enabled = false;
        _abbrechen.Enabled = true;
        var daten = Dokument.Klonen(D);
        var prog = new Progress<(int UnterrichtId, int Erledigt, int Gesamt)>(p => _unterricht.Text = $"Unterricht: {p.UnterrichtId} ({p.Erledigt}/{p.Gesamt})");
        try
        {
            var ct = _cts.Token;
            _erg = await Task.Run(() => CccAnalyse.Analysieren(daten, 30, prog, ct));
            Anzeigen();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Ui.Fehler(ex); }
        finally
        {
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed) { _start.Enabled = true; _abbrechen.Enabled = false; }
        }
    }

    private void Anzeigen()
    {
        if (_erg is null || IsDisposed) return;
        _variationen.Text = $"Unterricht: {_erg.Unterrichte}   Kopplungen: {_erg.Kopplungen}   Total: {_erg.Kombinationen:N0}";
        var unmoeglich = _erg.Ketten.Count(k => k.Wochenstunden > _erg.VerfuegbareStunden);
        _hinweis.Text = unmoeglich > 0 ? $"{unmoeglich} Kette(n) mit mehr Wochenstunden als verfügbaren Stunden ({_erg.VerfuegbareStunden}) – restlose Verplanung unmöglich." : "";
        var ix = Ix;
        var spalten = _erg.Ketten.Select(k => k.UnterrichtIds.Count).DefaultIfEmpty(0).Max();
        _ketten.Columns.Clear();
        _ketten.Columns.Add("Wst", "Wst");
        _ketten.Columns[0].Width = 40;
        for (var i = 0; i < spalten; i++) _ketten.Columns[_ketten.Columns.Add($"U{i}", i == 0 ? "Unterricht" : "")].Width = 76;
        foreach (var k in _erg.Ketten)
        {
            var werte = new object[spalten + 1];
            werte[0] = k.Wochenstunden;
            for (var i = 0; i < k.UnterrichtIds.Count; i++)
                werte[i + 1] = k.Kritisch.TryGetValue(k.UnterrichtIds[i], out var l) ? $"{k.UnterrichtIds[i]} ({ix.KLehrer(l)})" : k.UnterrichtIds[i].ToString();
            var r = _ketten.Rows.Add(werte);
            _ketten.Rows[r].Tag = k;
            if (k.Wochenstunden > _erg.VerfuegbareStunden) _ketten.Rows[r].Cells[0].Style.BackColor = Color.FromArgb(240, 190, 200);
        }
        if (_ketten.Rows.Count > 0) LupeZeigen(0, 1);
    }

    private Unterricht? _gewaehlt;

    /// <summary>CCC-Lupe: alle Unterrichte der Kette mit Klassen und Lehrkräften; Unverträglichkeiten mit dem angeklickten Unterricht rot.</summary>
    private void LupeZeigen(int zeile, int spalte)
    {
        if (zeile < 0 || _ketten.Rows[zeile].Tag is not Konfliktkette k) return;
        var i = Math.Max(0, spalte - 1);
        if (i >= k.UnterrichtIds.Count) i = 0;
        var unts = k.UnterrichtIds.Select(id => Ix.Unterrichte.GetValueOrDefault(id)).Where(u => u != null).Select(u => u!).ToList();
        _gewaehlt = unts.FirstOrDefault(u => u.Id == k.UnterrichtIds[i]);
        var maxK = unts.Select(u => u.KlassenIds.Count()).DefaultIfEmpty(1).Max();
        var maxL = unts.Select(u => u.LehrerIds.Count()).DefaultIfEmpty(1).Max();
        _lupe.Columns.Clear();
        _lupe.Columns.Add("Unt", "Unt.");
        _lupe.Columns.Add("Wst", "Wst");
        for (var c = 0; c < Math.Max(1, maxK); c++) _lupe.Columns[_lupe.Columns.Add($"K{c}", c == 0 ? "Klassen" : "")].Width = 40;
        for (var c = 0; c < Math.Max(1, maxL); c++) _lupe.Columns[_lupe.Columns.Add($"L{c}", c == 0 ? "Lehrer" : "")].Width = 52;
        _lupe.Columns[0].Width = 44;
        _lupe.Columns[1].Width = 36;
        foreach (var u in unts)
        {
            var werte = new List<object> { u.Id, u.Wochenstunden };
            var kl = u.KlassenIds.ToList();
            var le = u.LehrerIds.ToList();
            for (var c = 0; c < Math.Max(1, maxK); c++) werte.Add(c < kl.Count ? Ix.K(ElementTyp.Klasse, kl[c]) : "");
            for (var c = 0; c < Math.Max(1, maxL); c++) werte.Add(c < le.Count ? Ix.KLehrer(le[c]) : "");
            var r = _lupe.Rows.Add(werte.ToArray());
            _lupe.Rows[r].Tag = u;
        }
    }

    private void LupeFormatieren(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gewaehlt is null || _lupe.Rows[e.RowIndex].Tag is not Unterricht u) return;
        var name = _lupe.Columns[e.ColumnIndex].Name;
        if (u.Id == _gewaehlt.Id)
        {
            if (name is "Unt" or "Wst") e.CellStyle!.BackColor = Color.FromArgb(190, 225, 250);
            return;
        }
        var wert = e.Value?.ToString() ?? "";
        if (wert.Length == 0) return;
        // rot: gemeinsame Klasse bzw. gemeinsame Lehrkraft mit dem gewählten Unterricht (Grund der Unverträglichkeit)
        if (name.StartsWith('K') && _gewaehlt.KlassenIds.Any(k => Ix.K(ElementTyp.Klasse, k) == wert) && !Klassenkollision.Erlaubt(u, _gewaehlt)) e.CellStyle!.BackColor = Color.FromArgb(240, 170, 170);
        if (name.StartsWith('L') && _gewaehlt.LehrerIds.Any(l => Ix.KLehrer(l) == wert)) e.CellStyle!.BackColor = Color.FromArgb(240, 170, 170);
    }
}

/// <summary>
/// Liste „Lehrerteams“ (Handbuch 4.4.8.3.1 / 7.8.1): Gesamtzahl der Teams, je Team die Unterrichte (U-Nr, Wst, Fach, Klassen) und die
/// gemeinsam gesperrten Stunden; am Ende je Lehrkraft die Teams, in die sie eingebunden ist.
/// </summary>
public static class Lehrerteamliste
{
    public static List<string[]> Zeilen(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var teams = CccAnalyse.Lehrerteams(d);
        var zr = d.Zeitraster;
        var res = new List<string[]> { new[] { $"Anzahl Lehrerteams: {teams.Count}", "", "", "", "", "" } };
        foreach (var t in teams)
        {
            var gesperrt = Enumerable.Range(0, zr.TageProWoche).SelectMany(tag => Enumerable.Range(1, zr.StundenProTag)
                .Where(s => zr.Verfuegbar(tag, s) && CccAnalyse.TeamZeitwunsch(ix, t, tag, s) <= -3).Select(s => $"{Planungsdienst.Tagname(d, tag, true)}-{s}")).ToList();
            var erste = true;
            foreach (var uid in t.UnterrichtIds)
            {
                var u = ix.Unterrichte[uid];
                res.Add(new[]
                {
                    erste ? t.Nr.ToString() : "", erste ? string.Join(", ", t.LehrerIds.Select(ix.KLehrer)) : "", uid.ToString(), u.Wochenstunden.ToString(),
                    string.Join(",", u.FachIds.Select(f => ix.KFach(f))), ix.KKlassen(u.KlassenIds),
                });
                erste = false;
            }
            if (gesperrt.Count > 0) res.Add(new[] { "", "gesperrt (-3):", "", "", string.Join(" ", gesperrt), "" });
        }
        res.Add(new[] { "", "", "", "", "", "" });
        res.Add(new[] { "Lehrerteam", "", "", "", "", "" });
        foreach (var (l, nr) in CccAnalyse.TeamsJeLehrer(teams, ix)) res.Add(new[] { ix.KLehrer(l), string.Join(", ", nr) + ",", "", "", "", "" });
        return res;
    }

    public static void Zeigen(IWin32Window owner, bool drucken)
    {
        var zeilen = Zeilen(AppZustand.D);
        var kopf = new[] { "Team", "Lehrer", "U-Nr", "Wst", "Fach", "Klasse(n)" };
        var breiten = new[] { 50, 160, 50, 40, 140, 160 };
        if (drucken) { ListenDruck.Drucken(owner, "Lehrerteams", kopf, breiten, zeilen, true); return; }
        using var d = new Dialoge.Dialog("Lehrerteams", 640, 460);
        var g = new DataGridView { Left = 8, Top = 8, Width = 624, Height = 404, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        for (var i = 0; i < kopf.Length; i++) g.Columns[g.Columns.Add($"c{i}", kopf[i])].Width = breiten[i];
        foreach (var z in zeilen) g.Rows.Add(z);
        d.Controls.Add(g);
        d.Ok.Text = "Druck";
        d.Ok.DialogResult = DialogResult.None;
        d.Ok.Click += (_, _) => ListenDruck.Drucken(d, "Lehrerteams", kopf, breiten, zeilen, true);
        d.Abbrechen.Text = "Schließen";
        d.ShowDialog(owner);
    }
}
