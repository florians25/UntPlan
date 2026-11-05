using UntPlan.App.Druck;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Element-Rollup wie im Vorbild: Fenstertitel = Elementart (z. B. „Raum“), links eine senkrechte Leiste mit dem Filter und den
/// Elementarten, darüber die Filterzeile (z. B. „R“ für alle Räume, die mit R beginnen), Spalten je Art (Lehrer: Name, Nachname,
/// Text, Beschr.; Raum: Name, Langname, Ausweichraum, Raumgew.), weitere Spalten über das Kontextmenü der rechten Maustaste.
/// Mehrere Elemente mit Umschalt- bzw. Strg-Klick markieren und mit gedrückter linker Maustaste in ein Feld ziehen (z. B. Klassen in
/// die Spalte „Klasse“ der Veranstaltungen). Gezogen wird der Text „elemente:Typ:Kurz1,Kurz2,…“.
/// </summary>
public sealed class ElementRollup : Form
{
    private static ElementRollup? _instanz;
    private static readonly (string Text, ElementTyp Typ, string Symbol)[] Typen =
    {
        ("Klasse", ElementTyp.Klasse, "klassen"), ("Lehrer", ElementTyp.Lehrer, "lehrer"), ("Raum", ElementTyp.Raum, "raeume"), ("Fach", ElementTyp.Fach, "faecher"),
        ("Raumgruppe", ElementTyp.Raumgruppe, "raeume"),
    };
    /// <summary>Alle möglichen Spalten: Schlüssel, Überschrift, Breite, Standard je Art.</summary>
    private static readonly (string Name, string Text, int Breite)[] Spalten =
    {
        ("N", "Name", 50), ("L", "Langname", 120), ("T", "Text", 80), ("B", "Beschr.", 70), ("R", "Raum", 50), ("A", "Ausweichraum", 80), ("G", "Raumgew.", 60), ("K", "Kap.", 40),
    };
    private static readonly Dictionary<ElementTyp, HashSet<string>> Sichtbar = new()
    {
        [ElementTyp.Klasse] = new() { "N", "L", "R" },
        [ElementTyp.Lehrer] = new() { "N", "L", "T", "B" },
        [ElementTyp.Raum] = new() { "N", "L", "A", "G" },
        [ElementTyp.Fach] = new() { "N", "L", "R" },
        [ElementTyp.Raumgruppe] = new() { "N", "L" },
    };
    private readonly ToolStrip _arten = new() { Dock = DockStyle.Left, LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow, GripStyle = ToolStripGripStyle.Hidden, AutoSize = false, Width = 26, Padding = new Padding(1) };
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, BackColor = Color.FromArgb(255, 255, 200), Visible = false, PlaceholderText = "Filter" };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private int _typIndex;
    private Point _start;

    public static void Zeigen() => Zeigen(null);

    /// <summary>Rollup zeigen und auf den Elementtyp stellen.</summary>
    public static void Zeigen(ElementTyp? typ)
    {
        if (_instanz is null || _instanz.IsDisposed) { _instanz = new ElementRollup(); _instanz.Show(AppZustand.Haupt); }
        if (typ is ElementTyp t && Array.FindIndex(Typen, x => x.Typ == t) is var i and >= 0) _instanz.TypWaehlen(i);
        _instanz.Activate();
    }

    public static string? Text2Typ(string s, out ElementTyp typ, out List<string> namen)
    {
        typ = ElementTyp.Klasse;
        namen = new();
        if (!s.StartsWith("elemente:")) return null;
        var teile = s.Split(':', 3);
        if (teile.Length < 3 || !Enum.TryParse(teile[1], out typ)) return null;
        namen = teile[2].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        return teile[2];
    }

    private ElementRollup()
    {
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Size = new Size(320, 380);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Math.Max(0, AppZustand.Haupt.Right - 380), AppZustand.Haupt.Top + 160);
        var filterKnopf = new ToolStripButton(Symbole.Get("filter", 16)) { ToolTipText = "Filter", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        filterKnopf.CheckedChanged += (_, _) => { _filter.Visible = filterKnopf.Checked; if (!filterKnopf.Checked) _filter.Text = ""; else _filter.Focus(); };
        _arten.Items.Add(filterKnopf);
        for (var i = 0; i < Typen.Length; i++)
        {
            var n = i;
            _arten.Items.Add(new ToolStripButton(Symbole.Get(Typen[i].Symbol, 16)) { ToolTipText = Typen[i].Text, DisplayStyle = ToolStripItemDisplayStyle.Image, Tag = i }.Mit(b => b.Click += (_, _) => TypWaehlen(n)));
        }
        _filter.TextChanged += (_, _) => Fuellen();
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        foreach (var (n, t, w) in Spalten) _g.Columns[_g.Columns.Add(n, t)].Width = w;
        _g.MouseDown += (_, e) => _start = e.Location;
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (Math.Abs(e.X - _start.X) < 6 && Math.Abs(e.Y - _start.Y) < 6)) return;
            var namen = _g.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Cells[0].Value?.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (namen.Count == 0) return;
            _g.DoDragDrop($"elemente:{Typen[_typIndex].Typ}:{string.Join(",", namen)}", DragDropEffects.Copy);
        };
        // Vorbild: weitere Spalten über das Kontextmenü der rechten Maustaste
        _g.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) SpaltenMenue(e.Location); };
        Controls.Add(_g);
        Controls.Add(_filter);
        Controls.Add(_arten);
        TypWaehlen(0);
    }

    private void TypWaehlen(int i)
    {
        _typIndex = Math.Clamp(i, 0, Typen.Length - 1);
        Text = Typen[_typIndex].Text;
        foreach (ToolStripItem it in _arten.Items) if (it is ToolStripButton b && b.Tag is int n) b.Checked = n == _typIndex;
        Fuellen();
    }

    private void SpaltenMenue(Point pt)
    {
        var typ = Typen[_typIndex].Typ;
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        foreach (var (n, t, _) in Spalten.Skip(1))
        {
            var name = n;
            var it = new ToolStripMenuItem(Ueberschrift(name, typ, t)) { Checked = Sichtbar[typ].Contains(n), CheckOnClick = true };
            it.Click += (_, _) => { if (it.Checked) Sichtbar[typ].Add(name); else Sichtbar[typ].Remove(name); Fuellen(); };
            cm.Items.Add(it);
        }
        cm.Show(_g, pt);
    }

    private static string Ueberschrift(string spalte, ElementTyp typ, string text) => spalte == "L" && typ == ElementTyp.Lehrer ? "Nachname" : text;

    private void Fuellen()
    {
        var typ = Typen[_typIndex].Typ;
        var ix = AppZustand.Doc.Index;
        _g.Rows.Clear();
        foreach (DataGridViewColumn c in _g.Columns)
        {
            c.Visible = c.Name == "N" || Sichtbar[typ].Contains(c.Name);
            c.HeaderText = Ueberschrift(c.Name, typ, Spalten.First(x => x.Name == c.Name).Text);
        }
        var filter = _filter.Text.Trim();
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ).Where(e => !e.Ignorieren))
        {
            if (filter.Length > 0 && !e.Kurzname.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase)) continue;
            var raum = e switch { Klasse k => ix.KRaum(k.StammraumId), Lehrer l => ix.KRaum(l.StammraumId), Fach f => ix.KRaum(f.FachraumId), _ => "" };
            var r = e as Raum;
            _g.Rows.Add(e.Kurzname, e is Lehrer le ? le.Langname : e.Langname, e.Text, e.Beschreibung, raum,
                r is null ? "" : ix.KRaum(r.AusweichraumId), r is null ? "" : r.Raumgewicht, r is null || r.Kapazitaet == 0 ? "" : r.Kapazitaet);
        }
    }
}
