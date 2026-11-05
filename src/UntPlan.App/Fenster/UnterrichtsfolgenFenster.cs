using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Fenster „Unterrichtsfolge“ (Handbuch 3.7, Registerkarte Planung | Unterrichtsfolgen): Symbolleiste Neu / Löschen; Rahmen
/// „Unterrichtsfolge“ (Name, &lt;Löschen&gt;, Art: Fixe Fachfolge, Variable fixe Fachfolge, Gleichzeitiger Unterricht, Wochenfolge),
/// Rahmen „Unterricht“ (U-Nr, &lt;Dazu&gt;, &lt;Wegnehmen&gt;), „Anzeige von“ (UnterrichtsNr., Fach, U-Nr + Fach), darunter die Folgen
/// der gewählten Art (Name, U-Nr, U-Nr, …) und die Unterrichte der aktiven Folge. U-Nummern lassen sich auch per Doppelklick auf
/// die Unterrichtsnummer im Unterrichtsfenster eintragen. „In Kopplungen umwandeln“ macht gleichzeitige Unterrichte zu Kopplungen.
/// </summary>
public sealed class UnterrichtsfolgenFenster : MdiFenster
{
    private readonly TextBox _name = new() { Width = 80, Name = "FolgeName" };
    private readonly ComboBox _art = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Name = "FolgeArt" };
    private readonly TextBox _unr = new() { Width = 60, Name = "UNr" };
    private readonly RadioButton _anzNr = new() { Text = "UnterrichtsNr.", AutoSize = true, Checked = true };
    private readonly RadioButton _anzFach = new() { Text = "Fach", AutoSize = true };
    private readonly RadioButton _anzBeides = new() { Text = "U-Nr + Fach", AutoSize = true };
    private readonly DataGridView _folgen = new() { Dock = DockStyle.Fill, Name = "Folgen" };
    private readonly DataGridView _unterrichte = new() { Dock = DockStyle.Fill, Name = "Unterrichte" };
    private int _aktiv = -1;
    private bool _laden;

    public UnterrichtsfolgenFenster()
    {
        Text = "Unterrichtsfolge";
        HilfeThema = "Unterricht";
        Icon = SymbolIcon("unterricht");
        Size = new Size(560, 460);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neu\nNeue Unterrichtsfolge", "neu", Neu));
        ts.Items.Add(Knopf("Löschen\nUnterrichtsfolge löschen", "loeschen", FolgeLoeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Unterrichtsfolgen in Kopplungen umwandeln", "plus", InKopplungen, false));

        foreach (var a in Enum.GetValues<UnterrichtsfolgeArt>()) _art.Items.Add(Unterrichtsfolge.ArtText(a));
        _art.SelectedIndex = 0;

        var kopf = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4), WrapContents = true };
        var gFolge = new GroupBox { Text = "Unterrichtsfolge", Width = 450, Height = 52 };
        _name.SetBounds(10, 20, 80, 22);
        var loeschen = new Button { Text = "Löschen", Left = 100, Top = 19, Width = 80, Height = 24 };
        _art.SetBounds(190, 20, 170, 22);
        gFolge.Controls.AddRange(new Control[] { _name, loeschen, _art });
        var gUnt = new GroupBox { Text = "Unterricht", Width = 280, Height = 52 };
        _unr.SetBounds(10, 20, 60, 22);
        var dazu = new Button { Text = "Dazu", Left = 80, Top = 19, Width = 80, Height = 24 };
        var weg = new Button { Text = "Wegnehmen", Left = 168, Top = 19, Width = 96, Height = 24 };
        gUnt.Controls.AddRange(new Control[] { _unr, dazu, weg });
        var gAnz = new GroupBox { Text = "Anzeige von", Width = 330, Height = 46 };
        _anzNr.Location = new Point(10, 18);
        _anzFach.Location = new Point(130, 18);
        _anzBeides.Location = new Point(200, 18);
        gAnz.Controls.AddRange(new Control[] { _anzNr, _anzFach, _anzBeides });
        kopf.Controls.AddRange(new Control[] { gFolge, gUnt, gAnz });

        Ui.RasterGestalten(_folgen, false);
        _folgen.ReadOnly = true;
        _folgen.RowHeadersVisible = false;
        _folgen.AllowUserToAddRows = false;
        _folgen.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _folgen.MultiSelect = false;
        Ui.RasterGestalten(_unterrichte, false);
        _unterrichte.ReadOnly = true;
        _unterrichte.RowHeadersVisible = false;
        _unterrichte.AllowUserToAddRows = false;
        foreach (var (n, t, b) in new[] { ("UNr", "U-Nr", 50), ("Wst", "Wst", 40), ("Lehrer", "Lehrer", 90), ("Klasse", "Klasse", 90), ("Fach", "Fach", 80) })
            _unterrichte.Columns[_unterrichte.Columns.Add(n, t)].Width = b;
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_folgen);
        split.Panel2.Controls.Add(_unterrichte);

        loeschen.Click += (_, _) => FolgeLoeschen();
        dazu.Click += (_, _) => Dazu(null);
        weg.Click += (_, _) => Wegnehmen();
        _unr.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Dazu(null); e.SuppressKeyPress = true; } };
        _art.SelectionChangeCommitted += (_, _) => ArtGeaendert();
        _name.Leave += (_, _) => NameGeaendert();
        _name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { NameGeaendert(); e.SuppressKeyPress = true; } };
        foreach (var r in new[] { _anzNr, _anzFach, _anzBeides }) r.CheckedChanged += (_, _) => { if (r.Checked) Neuaufbau(); };
        _folgen.SelectionChanged += (_, _) =>
        {
            if (_laden || _folgen.CurrentRow?.Tag is not int i) return;
            _aktiv = i;
            Anzeigen();
        };
        UnterrichtFenster.UnterrichtsnummerGewaehlt += Doppelklick;
        FormClosed += (_, _) => UnterrichtFenster.UnterrichtsnummerGewaehlt -= Doppelklick;

        Controls.Add(split);
        Controls.Add(kopf);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = Math.Max(60, split.Height / 2);
        if (D.Unterrichtsfolgen.Count > 0) _aktiv = 0;
        Neuaufbau();
    }

    private List<Unterrichtsfolge> Folgen => D.Unterrichtsfolgen;
    private Unterrichtsfolge? Aktiv => _aktiv >= 0 && _aktiv < Folgen.Count ? Folgen[_aktiv] : null;

    protected override void Aktualisieren(AenderungsArt art) { if (art is AenderungsArt.Unterricht or AenderungsArt.Alles) Neuaufbau(); }

    private void Doppelklick(int unterrichtId) { if (!IsDisposed) Dazu(unterrichtId); }

    private string Zelle(int id)
    {
        var u = Ix.Unterrichte.GetValueOrDefault(id);
        var fach = u is null ? "?" : Ix.KFach(u.HauptfachId);
        return _anzFach.Checked ? fach : _anzBeides.Checked ? $"{id} {fach}" : id.ToString();
    }

    private void Neuaufbau()
    {
        _laden = true;
        var art = Aktiv?.Art ?? (UnterrichtsfolgeArt)Math.Max(0, _art.SelectedIndex);
        _folgen.Rows.Clear();
        _folgen.Columns.Clear();
        var max = Math.Max(5, Folgen.Where(f => f.Art == art).Select(f => f.UnterrichtIds.Count).DefaultIfEmpty(0).Max());
        _folgen.Columns[_folgen.Columns.Add("Name", "Name")].Width = 60;
        for (var i = 0; i < max; i++) _folgen.Columns[_folgen.Columns.Add("U" + i, "U-Nr")].Width = _anzNr.Checked ? 44 : 70;
        for (var fi = 0; fi < Folgen.Count; fi++)
        {
            var f = Folgen[fi];
            if (f.Art != art) continue;
            var werte = new object[] { f.Name }.Concat(f.UnterrichtIds.Select(id => (object)Zelle(id))).ToArray();
            var r = _folgen.Rows.Add(werte);
            _folgen.Rows[r].Tag = fi;
            if (fi == _aktiv) _folgen.CurrentCell = _folgen.Rows[r].Cells[0];
        }
        _laden = false;
        Anzeigen();
    }

    private void Anzeigen()
    {
        _laden = true;
        var f = Aktiv;
        _name.Text = f?.Name ?? "";
        if (f != null) _art.SelectedIndex = (int)f.Art;
        _unterrichte.Rows.Clear();
        foreach (var id in f?.UnterrichtIds ?? new List<int>())
        {
            var u = Ix.Unterrichte.GetValueOrDefault(id);
            _unterrichte.Rows.Add(id, u?.Wochenstunden.ToString() ?? "", u is null ? "" : string.Join(",", u.LehrerIds.Select(Ix.KLehrer)),
                u is null ? "" : Ix.KKlassen(u.KlassenIds), u is null ? "" : string.Join(",", u.FachIds.Select(x => Ix.KFach(x))));
        }
        _laden = false;
    }

    private void Neu()
    {
        var art = (UnterrichtsfolgeArt)Math.Max(0, _art.SelectedIndex);
        var name = "G1";
        for (var i = 2; Folgen.Any(f => f.Name == name); i++) name = "G" + i;
        Aendern("Neue Unterrichtsfolge", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen.Add(new Unterrichtsfolge { Name = name, Art = art }));
        _aktiv = Folgen.Count - 1;
        Neuaufbau();
        _unr.Focus();
    }

    private void FolgeLoeschen()
    {
        if (Aktiv is null) return;
        var idx = _aktiv;
        Aendern("Unterrichtsfolge löschen", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen.RemoveAt(idx));
        _aktiv = Math.Min(idx, Folgen.Count - 1);
        Neuaufbau();
    }

    private void ArtGeaendert()
    {
        if (_laden) return;
        var art = (UnterrichtsfolgeArt)_art.SelectedIndex;
        if (Aktiv is { } f && f.Art != art)
        {
            if (f.UnterrichtIds.Count > Unterrichtsfolge.MaxUnterrichte(art)) { Ui.Info($"Bei der {Unterrichtsfolge.ArtText(art)} sind maximal {Unterrichtsfolge.MaxUnterrichte(art)} Unterrichtsnummern möglich."); Anzeigen(); return; }
            var idx = _aktiv;
            Aendern("Art der Unterrichtsfolge", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen[idx].Art = art);
        }
        else if (Aktiv is null || Aktiv.Art != art)
        {
            // andere Art gewählt: Folgen dieser Art anzeigen
            _aktiv = Folgen.FindIndex(x => x.Art == art);
        }
        Neuaufbau();
    }

    private void NameGeaendert()
    {
        if (_laden || Aktiv is not { } f || f.Name == _name.Text.Trim()) return;
        var idx = _aktiv;
        var name = _name.Text.Trim();
        Aendern("Name der Unterrichtsfolge", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen[idx].Name = name);
        Neuaufbau();
    }

    private void Dazu(int? nummer)
    {
        if (nummer is null)
        {
            if (!int.TryParse(_unr.Text.Trim(), out var n)) return;
            nummer = n;
        }
        if (!Ix.Unterrichte.ContainsKey(nummer.Value)) { Ui.Info($"Unterricht \"{nummer}\" ist nicht vorhanden."); return; }
        if (Aktiv is null) Neu();
        if (Aktiv is not { } f || f.UnterrichtIds.Contains(nummer.Value)) return;
        if (f.UnterrichtIds.Count >= Unterrichtsfolge.MaxUnterrichte(f.Art)) { Ui.Info($"Bei der {Unterrichtsfolge.ArtText(f.Art)} sind maximal {Unterrichtsfolge.MaxUnterrichte(f.Art)} Unterrichtsnummern möglich."); return; }
        var idx = _aktiv;
        var nr = nummer.Value;
        Aendern("Unterrichtsfolge: Dazu", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen[idx].UnterrichtIds.Add(nr));
        _unr.Clear();
        Neuaufbau();
    }

    private void Wegnehmen()
    {
        if (Aktiv is not { } f) return;
        var nr = int.TryParse(_unr.Text.Trim(), out var n) ? n : _unterrichte.CurrentRow?.Cells[0].Value is int c ? c : -1;
        if (!f.UnterrichtIds.Contains(nr)) return;
        var idx = _aktiv;
        Aendern("Unterrichtsfolge: Wegnehmen", AenderungsArt.Unterricht, d => d.Unterrichtsfolgen[idx].UnterrichtIds.Remove(nr));
        _unr.Clear();
        Neuaufbau();
    }

    private void InKopplungen()
    {
        var anzahl = Folgen.Count(f => f.Art == UnterrichtsfolgeArt.Gleichzeitig);
        if (anzahl == 0) { Ui.Info("Es gibt keine Unterrichtsfolgen „Gleichzeitiger Unterricht“."); return; }
        if (!Ui.Frage($"{anzahl} Unterrichtsfolge(n) „Gleichzeitiger Unterricht“ in Kopplungen umwandeln?\n\nUnterrichtsgruppen werden in das Feld „Zeilen-Unterrichtsgruppe“ übertragen; verplante Stunden der hinzugekoppelten Unterrichte werden entplant.")) return;
        var n = 0;
        Aendern("Unterrichtsfolgen in Kopplungen umwandeln", AenderungsArt.Unterricht, d => n = Unterrichtswerkzeuge.GleichzeitigeInKopplungen(d));
        _aktiv = Folgen.Count > 0 ? 0 : -1;
        Neuaufbau();
        Ui.Info($"{n} Kopplung(en) erstellt.");
    }
}
