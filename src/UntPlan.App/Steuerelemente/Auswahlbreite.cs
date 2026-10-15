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

    /// <summary>Kontrollkästchen in Tabellen im Normalzustand zeichnen (die Hervorhebung beim Überfahren übernimmt <see cref="ZellFarben"/> für alle Zellen gleich).</summary>
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

    /// <summary>Zelle unter der Maus je Tabelle.</summary>
    private static readonly ConditionalWeakTable<DataGridView, StrongBox<Point>> HoverZelle = new();

    private static Point Hover(DataGridView g) => HoverZelle.TryGetValue(g, out var b) ? b.Value : new Point(-1, -1);

    private static void HoverSetzen(DataGridView g, int spalte, int zeile)
    {
        var alt = Hover(g);
        if (alt.X == spalte && alt.Y == zeile) return;
        HoverZelle.AddOrUpdate(g, new StrongBox<Point>(new Point(spalte, zeile)));
        if (alt.X >= 0 && alt.Y >= 0 && alt.X < g.ColumnCount && alt.Y < g.RowCount) g.InvalidateCell(alt.X, alt.Y);
        if (spalte >= 0 && zeile >= 0) g.InvalidateCell(spalte, zeile);
    }

    /// <summary>
    /// Zellfarben: die Zelle unter der Maus wird hervorgehoben; im Vertretungsmodus wie Untis nicht bearbeitbare Felder grau,
    /// bearbeitbare hell und die aktuelle Zelle orange. Eigene Farben der Fenster (Vertretungsarten, Kollision …) bleiben erhalten.
    /// </summary>
    private static void ZellFarben(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView g || e.RowIndex < 0 || e.ColumnIndex < 0 || e.CellStyle is null) return;
        var zeile = g.Rows[e.RowIndex];
        var standard = e.CellStyle.BackColor == g.DefaultCellStyle.BackColor || e.CellStyle.BackColor == g.AlternatingRowsDefaultCellStyle.BackColor ||
                       e.CellStyle.BackColor == g.RowsDefaultCellStyle.BackColor || e.CellStyle.BackColor.IsEmpty;
        if (Ui.Vertretungsmodus && !g.ReadOnly)
        {
            if (standard && zeile.Cells[e.ColumnIndex].ReadOnly) e.CellStyle.BackColor = Ui.ZelleGesperrt;
            if (g.CurrentCell is { } c && c.RowIndex == e.RowIndex && c.ColumnIndex == e.ColumnIndex) e.CellStyle.SelectionBackColor = Ui.ZelleAktivVertretung;
        }
        var h = Hover(g);
        if (h.X == e.ColumnIndex && h.Y == e.RowIndex)
        {
            e.CellStyle.BackColor = standard || e.CellStyle.BackColor == Ui.ZelleGesperrt ? Ui.ZelleHover : Mischen(e.CellStyle.BackColor, Ui.ZelleHover);
            e.CellStyle.SelectionBackColor = Mischen(e.CellStyle.SelectionBackColor.IsEmpty ? g.DefaultCellStyle.SelectionBackColor : e.CellStyle.SelectionBackColor, Ui.ZelleHover);
        }
    }

    private static Color Mischen(Color a, Color b) => Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);

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
                g.CellFormatting += ZellFarben;
                g.CellMouseEnter += (_, e) => HoverSetzen(g, e.ColumnIndex, e.RowIndex);
                g.MouseLeave += (_, _) => HoverSetzen(g, -1, -1);
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
