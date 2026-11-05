using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Vorbild „Fixierter/Ignorierter Unterricht“: Karteikarten „Fixiert (n%)“ und „Ignoriert (n%)“ mit allen Unterrichten, die (von der
/// Optimierung) nicht verschoben bzw. nicht berücksichtigt werden; die Spalten zeigen, welche Ebene bzw. welches Stammdaten-Element
/// das verursacht (Unterricht, Gruppe, Klasse, Lehrer, Raum, StammRaum, Fach, Stunde). „Löschen“ hebt die Fixierung des markierten
/// Feldes auf; Spalten über das Kontextmenü der Überschriftszeile ein-/ausblenden (Standard: Spalten mit mindestens einem Eintrag).
/// </summary>
public sealed class FixierterUnterrichtFenster : MdiFenster
{
    private readonly TabControl _reiter = new() { Dock = DockStyle.Top, Height = 22 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly HashSet<string> _ausgeblendet = new();
    private readonly HashSet<string> _eingeblendet = new();

    private static readonly (string Name, string Text)[] Ebenen =
    {
        ("UF", "Unterricht"), ("GF", "Gruppe"), ("KF", "Klasse"), ("LF", "Lehrer"), ("RF", "Raum"), ("SF", "StammRaum"), ("FF", "Fach"), ("STF", "Stunde"),
    };

    public FixierterUnterrichtFenster()
    {
        Text = "Fixierter/Ignorierter Unterricht";
        HilfeThema = "Unterricht";
        Icon = SymbolIcon("fixieren");
        Size = new Size(760, 420);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Löschen\nLöscht den markierten Bereich.", "loeschen", Loeschen));
        ts.Items.Add(Knopf("Unterricht", "unterricht", () =>
        {
            if (_g.CurrentRow?.Tag is int u) UnterrichtFenster.Zeigen(ElementTyp.Unterricht, null);
        }));
        _reiter.TabPages.Add(new TabPage("Fixiert"));
        _reiter.TabPages.Add(new TabPage("Ignoriert"));
        _reiter.SelectedIndexChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _g.AllowUserToAddRows = false;
        _g.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
            Ui.ZellHaken(e, Color.ForestGreen, 2.2f);
        };
        _g.ColumnHeaderMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) KopfMenue(); };
        _g.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { Loeschen(); e.Handled = true; } };
        Controls.Add(_g);
        Controls.Add(_reiter);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private bool Ignoriert => _reiter.SelectedIndex == 1;

    /// <summary>Ursachen (je Spalte) einer Fixierung bzw. eines Ignorierens für einen Unterricht.</summary>
    private Dictionary<string, string> Ursachen(Unterricht u, bool ignoriert)
    {
        var ix = Ix;
        var r = new Dictionary<string, string>();
        bool Wert(Element? e) => e is not null && (ignoriert ? e.Ignorieren : e.Fix);
        string Namen(IEnumerable<Element?> el) => string.Join(",", el.Where(Wert).Select(e => e!.Kurzname).Distinct());
        if (ignoriert ? u.Ignorieren : u.Fix) r["UF"] = "✔";
        var gruppen = u.Zeilen.Select(z => z.UGruppeId ?? u.UGruppeId).Append(u.UGruppeId).Where(g => g.HasValue).Select(g => D.UGruppen.FirstOrDefault(x => x.Id == g)).Where(g => g is not null && (ignoriert ? g.Ignorieren : g.Fix)).Select(g => g!.Kurzname).Distinct().ToList();
        if (gruppen.Count > 0) r["GF"] = string.Join(",", gruppen);
        var k = Namen(u.KlassenIds.Select(id => (Element?)ix.Element(ElementTyp.Klasse, id)));
        if (k.Length > 0) r["KF"] = k;
        var l = Namen(u.LehrerIds.Select(id => (Element?)ix.Element(ElementTyp.Lehrer, id)));
        if (l.Length > 0) r["LF"] = l;
        var raeume = D.Stundenplan.Where(p => p.UnterrichtId == u.Id).SelectMany(p => p.Raeume).Where(x => x.HasValue).Select(x => (Element?)ix.Element(ElementTyp.Raum, x!.Value))
            .Concat(u.Zeilen.Select(z => z.FachraumId).Where(x => x.HasValue).Select(x => (Element?)ix.Element(ElementTyp.Raum, x!.Value)));
        var rf = Namen(raeume);
        if (rf.Length > 0) r["RF"] = rf;
        var sf = Namen(u.Zeilen.Select(z => z.StammraumId).Where(x => x.HasValue).Select(x => (Element?)ix.Element(ElementTyp.Raum, x!.Value)));
        if (sf.Length > 0) r["SF"] = sf;
        var ff = Namen(u.Zeilen.Select(z => z.FachId).Where(x => x.HasValue).Select(x => (Element?)ix.Element(ElementTyp.Fach, x!.Value)));
        if (ff.Length > 0) r["FF"] = ff;
        if (!ignoriert && D.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Fixiert)) r["STF"] = "✔";
        return r;
    }

    private void Neuaufbau()
    {
        if (IsDisposed) return;
        var ign = Ignoriert;
        var zeilen = D.Unterrichte.Select(u => (U: u, R: Ursachen(u, ign))).Where(x => x.R.Count > 0).OrderBy(x => x.U.Id).ToList();
        // Prozentangaben in den Karteireitern wie im Vorbild „Fixiert (36%)“
        var alle = Math.Max(1, D.Unterrichte.Count);
        _reiter.TabPages[0].Text = $"Fixiert ({D.Unterrichte.Count(u => Ursachen(u, false).Count > 0) * 100 / alle}%)";
        _reiter.TabPages[1].Text = $"Ignoriert ({D.Unterrichte.Count(u => Ursachen(u, true).Count > 0) * 100 / alle}%)";
        var pos = (_g.CurrentCell?.RowIndex ?? -1, _g.CurrentCell?.ColumnIndex ?? 0);
        _g.Columns.Clear();
        _g.Rows.Clear();
        foreach (var (n, t, w) in new[] { ("U", "U-Nr", 44), ("L", "Lehrer", 60), ("F", "Fach", 52), ("K", "Klasse(n)", 90) })
            _g.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        _g.Columns["U"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var (n, t) in Ebenen)
        {
            if (ign && n == "STF") continue;
            var c = new DataGridViewTextBoxColumn { Name = n, HeaderText = t + (ign ? " ign." : " fix."), Width = 84, SortMode = DataGridViewColumnSortMode.NotSortable };
            // Standard: nur Spalten mit mindestens einem Eintrag
            c.Visible = _eingeblendet.Contains(n) || !_ausgeblendet.Contains(n) && zeilen.Any(z => z.R.ContainsKey(n));
            _g.Columns.Add(c);
        }
        foreach (var (u, r) in zeilen)
        {
            var werte = new List<object?> { u.Id, Ix.KLehrer(u.Zeilen.FirstOrDefault()?.LehrerId), Ix.KFach(u.Zeilen.FirstOrDefault()?.FachId), Ix.KKlassen(u.KlassenIds) };
            werte.AddRange(Ebenen.Where(e => !(ign && e.Name == "STF")).Select(e => (object?)r.GetValueOrDefault(e.Name, "")));
            var i = _g.Rows.Add(werte.ToArray());
            _g.Rows[i].Tag = u.Id;
            _g.Rows[i].Cells["U"].Style.BackColor = Ui.Erste;
        }
        if (pos.Item1 >= 0 && pos.Item1 < _g.Rows.Count && pos.Item2 < _g.Columns.Count && _g.Columns[pos.Item2].Visible) _g.CurrentCell = _g.Rows[pos.Item1].Cells[pos.Item2];
    }

    private void KopfMenue()
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        foreach (DataGridViewColumn c in _g.Columns)
        {
            var spalte = c;
            var it = new ToolStripMenuItem(c.HeaderText) { Checked = c.Visible, CheckOnClick = true };
            it.Click += (_, _) =>
            {
                spalte.Visible = it.Checked;
                if (it.Checked) { _eingeblendet.Add(spalte.Name); _ausgeblendet.Remove(spalte.Name); }
                else { _ausgeblendet.Add(spalte.Name); _eingeblendet.Remove(spalte.Name); }
            };
            cm.Items.Add(it);
        }
        cm.Show(Cursor.Position);
    }

    /// <summary>„Löschen“: Fixierung (bzw. Ignorieren) des markierten Feldes aufheben – beim Unterricht, der Gruppe, den Elementen oder den Stunden.</summary>
    private void Loeschen()
    {
        if (_g.CurrentCell is not { } zelle || _g.Rows[zelle.RowIndex].Tag is not int uid) return;
        var spalte = _g.Columns[zelle.ColumnIndex].Name;
        var text = zelle.Value as string ?? "";
        if (text.Length == 0) return;
        var ign = Ignoriert;
        if (!Ui.LoeschenBestaetigen(1, ign ? "Ignorieren-Kennzeichen" : "Fixierung", ign ? "Ignorieren-Kennzeichen" : "Fixierungen", text)) return;
        var namen = text.Split(',').Select(x => x.Trim()).ToHashSet();
        void Element(Element e) { if (ign) e.Ignorieren = false; else e.Fix = false; }
        Aendern(ign ? "Ignorieren aufheben" : "Fixierung löschen", spalte is "STF" ? AenderungsArt.Stundenplan : spalte is "UF" ? AenderungsArt.Unterricht : AenderungsArt.Stammdaten, d =>
        {
            var u = d.Unterrichte.First(x => x.Id == uid);
            switch (spalte)
            {
                case "UF": if (ign) u.Ignorieren = false; else u.Fix = false; break;
                case "GF": foreach (var g in d.UGruppen.Where(g => namen.Contains(g.Kurzname))) { if (ign) g.Ignorieren = false; else g.Fix = false; } break;
                case "KF": foreach (var e in d.Klassen.Where(e => namen.Contains(e.Kurzname))) Element(e); break;
                case "LF": foreach (var e in d.Lehrer.Where(e => namen.Contains(e.Kurzname))) Element(e); break;
                case "RF" or "SF": foreach (var e in d.Raeume.Where(e => namen.Contains(e.Kurzname))) Element(e); break;
                case "FF": foreach (var e in d.Faecher.Where(e => namen.Contains(e.Kurzname))) Element(e); break;
                case "STF": foreach (var p in d.Stundenplan.Where(p => p.UnterrichtId == uid)) p.Fixiert = false; break;
            }
        });
        Neuaufbau();
    }
}
