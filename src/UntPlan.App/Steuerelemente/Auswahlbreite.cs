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
        Application.Idle += (_, _) => AlleErfassen();
    }

    /// <summary>Alle offenen Fenster sofort erfassen (sonst beim nächsten Leerlauf).</summary>
    public static void AlleErfassen()
    {
        if (!_aktiv) return;
        foreach (Form f in Application.OpenForms) Erfassen(f);
    }

    private static void OhneHover(DataGridViewComboBoxColumn c)
    {
        c.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        c.DisplayStyleForCurrentCellOnly = false;
    }

    /// <summary>Kontrollkästchen in Tabellen immer im Normalzustand zeichnen – wie Untis ohne Hervorhebung beim Überfahren.</summary>
    private static void KontrollkaestchenOhneHover(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Handled || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || sender is not DataGridView g ||
            g.Rows[e.RowIndex].Cells[e.ColumnIndex] is not DataGridViewCheckBoxCell) return;
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        if ((e.PaintParts & DataGridViewPaintParts.ContentForeground) != 0)
        {
            var zustand = e.FormattedValue switch
            {
                true or CheckState.Checked => System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal,
                CheckState.Indeterminate => System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal,
                _ => System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal,
            };
            var groesse = CheckBoxRenderer.GetGlyphSize(e.Graphics, zustand);
            var r = e.CellBounds;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(r.X + (r.Width - groesse.Width) / 2, r.Y + (r.Height - groesse.Height) / 2), zustand);
        }
        e.Handled = true;
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
                g.CellPainting += KontrollkaestchenOhneHover;
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
