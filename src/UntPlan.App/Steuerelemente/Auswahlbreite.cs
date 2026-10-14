using System.Runtime.CompilerServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Programmweit: Jede Auswahlliste (auch in Tabellenzellen und Symbolleisten) öffnet so breit, dass Kurz- und Langnamen
/// vollständig lesbar sind. Neue Fenster werden beim nächsten Leerlauf erfasst, später hinzugefügte Steuerelemente über ControlAdded.
/// </summary>
public static class Auswahlbreite
{
    private static readonly ConditionalWeakTable<Control, object> Erfasst = new();
    private static bool _aktiv;

    public static void Einschalten()
    {
        if (_aktiv) return;
        _aktiv = true;
        Application.Idle += (_, _) =>
        {
            foreach (Form f in Application.OpenForms) Erfassen(f);
        };
    }

    private static void OhneHover(DataGridViewComboBoxColumn c)
    {
        c.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        c.DisplayStyleForCurrentCellOnly = false;
    }

    private static void Erfassen(Control c)
    {
        if (Erfasst.TryGetValue(c, out _)) return;
        Erfasst.Add(c, new object());
        switch (c)
        {
            case ComboBox cb:
                cb.DropDown += (_, _) => { if (!ListenSuche.IstEingerichtet(cb)) ListenSuche.BreiteSetzen(cb, langname: ListenSuche.Langname); };
                break;
            case DataGridView g:
                g.EditingControlShowing += (_, e) => { if (e.Control is ComboBox cb) Erfassen(cb); };
                // Auswahlspalten wie Untis ohne Pfeil/Hover-Hervorhebung – die Liste erscheint erst beim Bearbeiten
                foreach (var spalte in g.Columns.OfType<DataGridViewComboBoxColumn>()) OhneHover(spalte);
                g.ColumnAdded += (_, e) => { if (e.Column is DataGridViewComboBoxColumn cc) OhneHover(cc); };
                break;
            case ToolStrip ts:
                foreach (var item in ts.Items.OfType<ToolStripComboBox>()) Erfassen(item.ComboBox);
                ts.ItemAdded += (_, e) => { if (e.Item is ToolStripComboBox tc) Erfassen(tc.ComboBox); };
                break;
        }
        foreach (Control k in c.Controls) Erfassen(k);
        c.ControlAdded += (_, e) => { if (e.Control != null) Erfassen(e.Control); };
    }
}
