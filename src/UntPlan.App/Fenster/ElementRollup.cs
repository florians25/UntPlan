using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Element-Rollup: kleines Fenster mit den Elementen eines Typs (Name, Langname, Raum). Mehrere Elemente mit Umschalt- bzw.
/// Strg-Klick markieren und mit gedrückter linker Maustaste in ein Feld ziehen (z. B. Klassen in die Spalte „Klasse“ der
/// Veranstaltungen). Gezogen wird der Text „elemente:Typ:Kurz1,Kurz2,…“.
/// </summary>
public sealed class ElementRollup : Form
{
    private static ElementRollup? _instanz;
    private static readonly (string Text, ElementTyp Typ)[] Typen =
    {
        ("Klasse", ElementTyp.Klasse), ("Lehrer", ElementTyp.Lehrer), ("Raum", ElementTyp.Raum), ("Fach", ElementTyp.Fach), ("Raumgruppe", ElementTyp.Raumgruppe),
    };
    private readonly ComboBox _typ = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private Point _start;

    public static void Zeigen() => Zeigen(null);

    /// <summary>Rollup zeigen und auf den Elementtyp stellen.</summary>
    public static void Zeigen(ElementTyp? typ)
    {
        if (_instanz is null || _instanz.IsDisposed) { _instanz = new ElementRollup(); _instanz.Show(AppZustand.Haupt); }
        if (typ is ElementTyp t && Array.FindIndex(Typen, x => x.Typ == t) is var i and >= 0) _instanz._typ.SelectedIndex = i;
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
        Text = "Element-Rollup";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Size = new Size(260, 360);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Math.Max(0, AppZustand.Haupt.Right - 320), AppZustand.Haupt.Top + 160);
        _typ.Items.AddRange(Typen.Select(x => (object)x.Text).ToArray());
        _typ.SelectedIndexChanged += (_, _) => Fuellen();
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        _g.Columns[_g.Columns.Add("N", "Name")].Width = 50;
        _g.Columns[_g.Columns.Add("L", "Langname")].Width = 130;
        _g.Columns[_g.Columns.Add("R", "Raum")].Width = 50;
        _g.MouseDown += (_, e) => _start = e.Location;
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (Math.Abs(e.X - _start.X) < 6 && Math.Abs(e.Y - _start.Y) < 6)) return;
            var namen = _g.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Cells[0].Value?.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (namen.Count == 0) return;
            _g.DoDragDrop($"elemente:{Typen[Math.Max(0, _typ.SelectedIndex)].Typ}:{string.Join(",", namen)}", DragDropEffects.Copy);
        };
        Controls.Add(_g);
        Controls.Add(_typ);
        _typ.SelectedIndex = 0;
    }

    private void Fuellen()
    {
        var typ = Typen[Math.Max(0, _typ.SelectedIndex)].Typ;
        var ix = AppZustand.Doc.Index;
        _g.Rows.Clear();
        _g.Columns["R"]!.Visible = typ is ElementTyp.Klasse or ElementTyp.Lehrer;
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ).Where(e => !e.Ignorieren))
        {
            var raum = e switch { Klasse k => ix.KRaum(k.StammraumId), Lehrer l => ix.KRaum(l.StammraumId), _ => "" };
            _g.Rows.Add(e.Kurzname, e is Lehrer le ? le.VollerName : e.Langname, raum);
        }
    }
}
