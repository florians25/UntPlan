using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Fenster „Formate / Stammdaten“ (Handbuch 2.3.4, Menüpunkt „Formate | Stammdatenformate“): Liste aller Stammdatenformate mit den
/// Spalten Name, Langname, Standard, im Menu, Art und Geschützt. Symbolleiste: Neu (Vorlage ist das aktivierte Format), Format öffnen,
/// Format löschen. „Standard“ wird beim Öffnen des Stammdatenfensters geladen, „im Menu“ nimmt das Format ins Stammdaten-Menü auf.
/// </summary>
public sealed class StammdatenformateFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill, Name = "Formate" };
    private bool _laden;

    private static readonly ElementTyp[] Arten =
    {
        ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Fach, ElementTyp.Raum, ElementTyp.Raumgruppe, ElementTyp.Gang, ElementTyp.Abteilung,
        ElementTyp.Abwesenheitsgrund, ElementTyp.Schueler,
    };

    public StammdatenformateFenster()
    {
        Text = "Formate / Stammdaten";
        HilfeThema = "Stammdaten";
        Icon = SymbolIcon("rollup");
        Size = new Size(520, 340);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neu\nNeues Format (Vorlage: das aktivierte Format)", "neu", Neu));
        ts.Items.Add(Knopf("Format öffnen", "vorschau", Oeffnen));
        ts.Items.Add(Knopf("Löschen\nFormat löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, false);
        _g.RowHeadersVisible = false;
        _g.AllowUserToAddRows = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 90 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Langname", HeaderText = "Langname", Width = 120 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Standard", HeaderText = "Standard", Width = 60 });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ImMenu", HeaderText = "im Menu", Width = 60 });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Art", HeaderText = "Art", Width = 90, ReadOnly = true });
        _g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Geschuetzt", HeaderText = "Geschützt", Width = 64 });
        foreach (DataGridViewColumn c in _g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _g.CurrentCellDirtyStateChanged += (_, _) => { if (_g.CurrentCell is DataGridViewCheckBoxCell) _g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _g.CellValueChanged += WertGeaendert;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == _g.Columns["Art"]!.Index) Oeffnen(); };
        _g.DataError += (_, e) => e.ThrowException = false;
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art) { if (art == AenderungsArt.Einstellungen) Neuaufbau(); }

    private static ElementTyp? TypVon(Ansicht a) =>
        a.Fenster.StartsWith("Stammdaten.") && Enum.TryParse<ElementTyp>(a.Fenster["Stammdaten.".Length..], out var t) ? t : null;

    private void Neuaufbau()
    {
        _laden = true;
        var akt = _g.CurrentRow?.Tag as Ansicht;
        _g.Rows.Clear();
        foreach (var a in D.Ansichten.Where(a => TypVon(a) is not null).OrderBy(a => Array.IndexOf(Arten, TypVon(a)!.Value)).ThenBy(a => a.Name))
        {
            var i = _g.Rows.Add(a.Name, a.Langname, a.Standard, a.ImMenu, Ui.TypName(TypVon(a)!.Value), a.Geschuetzt);
            _g.Rows[i].Tag = a;
            _g.Rows[i].Cells["Name"].ReadOnly = a.Geschuetzt;
            if (a == akt) _g.CurrentCell = _g.Rows[i].Cells[0];
        }
        _laden = false;
    }

    private void WertGeaendert(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laden || e.RowIndex < 0 || _g.Rows[e.RowIndex].Tag is not Ansicht a) return;
        var spalte = _g.Columns[e.ColumnIndex].Name;
        var wert = _g[e.ColumnIndex, e.RowIndex].Value;
        var fenster = a.Fenster;
        var name = a.Name;
        if (spalte == "Name")
        {
            var neu = (wert as string ?? "").Trim();
            if (neu.Length == 0 || D.Ansichten.Any(x => x.Fenster == fenster && x.Name == neu && x != a)) { Neuaufbau(); Steuerelemente.Tabellenbedienung.Ungueltig(_g, e.RowIndex, e.ColumnIndex, neu, $"Der Name \"{neu}\" ist ungültig bzw. bereits vergeben."); return; }
        }
        Aendern("Stammdatenformat", AenderungsArt.Einstellungen, d =>
        {
            var x = d.Ansichten.FirstOrDefault(v => v.Fenster == fenster && v.Name == name);
            if (x is null) return;
            switch (spalte)
            {
                case "Name": x.Name = ((string)wert!).Trim(); break;
                case "Langname": x.Langname = (wert as string ?? "").Trim(); break;
                case "ImMenu": x.ImMenu = wert is true; break;
                case "Geschuetzt": x.Geschuetzt = wert is true; break;
                case "Standard":
                    // je Art genau ein Standardformat
                    if (wert is true) foreach (var y in d.Ansichten.Where(v => v.Fenster == fenster)) y.Standard = false;
                    x.Standard = wert is true;
                    break;
            }
        });
        BeginInvoke(Neuaufbau);
    }

    private void Neu()
    {
        var vorlage = _g.CurrentRow?.Tag as Ansicht;
        var typ = vorlage is null ? ElementTyp.Klasse : TypVon(vorlage)!.Value;
        var fenster = $"Stammdaten.{typ}";
        var basis = vorlage?.Name ?? Ui.TypName(typ)[..Math.Min(3, Ui.TypName(typ).Length)];
        var name = basis + "-1";
        for (var i = 2; D.Ansichten.Any(a => a.Fenster == fenster && a.Name == name); i++) name = $"{basis}-{i}";
        var spalten = vorlage?.Spalten.ToList() ?? Felder.Fuer(typ).Where(f => f.Standard).Select(f => f.Schluessel).ToList();
        Aendern("Neues Stammdatenformat", AenderungsArt.Einstellungen, d => d.Ansichten.Add(new Ansicht
        {
            Fenster = fenster, Name = name, Langname = vorlage?.Langname ?? Ui.TypName(typ), Spalten = spalten,
            Sortierung = vorlage?.Sortierung.Select(s => new Sortierkriterium { Feld = s.Feld, Absteigend = s.Absteigend }).ToList() ?? new(),
            Langnamen = vorlage?.Langnamen.ToList() ?? new(), Schrift = vorlage?.Schrift ?? "",
        }));
        Neuaufbau();
    }

    private void Oeffnen()
    {
        if (_g.CurrentRow?.Tag is not Ansicht a || TypVon(a) is not ElementTyp t) return;
        StammdatenFenster.MitFormatOeffnen(t, a.Name);
    }

    private void Loeschen()
    {
        if (_g.CurrentRow?.Tag is not Ansicht a) return;
        if (a.Geschuetzt) { Ui.Info($"Das Format \"{a.Name}\" ist geschützt."); return; }
        if (!Ui.LoeschenBestaetigen(1, "Format", "Formate", a.Name)) return;
        var (fenster, name) = (a.Fenster, a.Name);
        Aendern("Stammdatenformat löschen", AenderungsArt.Einstellungen, d => d.Ansichten.RemoveAll(x => x.Fenster == fenster && x.Name == name));
        Neuaufbau();
    }
}
