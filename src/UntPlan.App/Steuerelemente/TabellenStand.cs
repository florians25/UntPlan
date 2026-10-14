using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Stand einer Tabelle vor einem Neuaufbau: aktuelle Zeile (über einen fachlichen Schlüssel wie die Vtr-Nr.), Spalte,
/// markierte Zeilen und Bildlaufposition. Nach dem Neuaufbau wird derselbe Datensatz wieder aktuell, auch wenn sich die
/// Reihenfolge geändert hat; fehlt er, wird die Zeile an der bisherigen Position gewählt (siehe <see cref="Listenabgleich"/>).
/// </summary>
public sealed class TabellenStand
{
    private string? _aktuell;
    private int _spalte;
    private int _index = -1;
    private string? _erste;
    private int _ersteIndex;
    private int _horizontal;
    private readonly HashSet<string> _markiert = new();

    public string? Aktuell => _aktuell;

    /// <summary>Statt der bisherigen eine andere Zeile wählen (z. B. den gerade angelegten Datensatz).</summary>
    public void AktuellSetzen(string? schluessel) => _aktuell = schluessel;

    public static TabellenStand Merken(DataGridView g, Func<DataGridViewRow, string?> schluessel)
    {
        var s = new TabellenStand
        {
            _spalte = g.CurrentCell?.ColumnIndex ?? -1,
            _index = g.CurrentCell?.RowIndex ?? -1,
            _horizontal = g.HorizontalScrollingOffset,
        };
        if (g.CurrentRow is { } cur) s._aktuell = schluessel(cur);
        var erste = g.FirstDisplayedScrollingRowIndex;
        if (erste >= 0 && erste < g.Rows.Count)
        {
            s._ersteIndex = erste;
            s._erste = schluessel(g.Rows[erste]);
        }
        foreach (DataGridViewRow r in g.SelectedRows)
            if (schluessel(r) is { } k) s._markiert.Add(k);
        return s;
    }

    /// <summary>Stand wiederherstellen (Aufrufer unterdrückt währenddessen seine Auswahl-Ereignisse).</summary>
    public void Wiederherstellen(DataGridView g, Func<DataGridViewRow, string?> schluessel, bool markierung = true)
    {
        if (g.Rows.Count == 0) return;
        var keys = g.Rows.Cast<DataGridViewRow>().Select(r => schluessel(r) ?? "").ToList();
        var ziel = Listenabgleich.Zielzeile(keys, _aktuell, _index);
        if (ziel < 0) return;
        var spalte = _spalte >= 0 && _spalte < g.Columns.Count && g.Columns[_spalte].Visible ? _spalte
            : g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).Select(c => c.Index).DefaultIfEmpty(-1).First();
        try
        {
            if (spalte >= 0 && g.Rows[ziel].Visible) g.CurrentCell = g.Rows[ziel].Cells[spalte];
            if (markierung && g.SelectionMode == DataGridViewSelectionMode.FullRowSelect)
            {
                g.ClearSelection();
                foreach (DataGridViewRow r in g.Rows)
                    if (_markiert.Contains(keys[r.Index])) r.Selected = true;
                g.Rows[ziel].Selected = true;
            }
            var erste = Listenabgleich.ErsteSichtbare(keys, _erste, _ersteIndex, ziel, Math.Max(1, g.DisplayedRowCount(false)));
            if (erste >= 0 && g.Rows[erste].Visible && !g.Rows[erste].Frozen) g.FirstDisplayedScrollingRowIndex = erste;
            if (_horizontal > 0) g.HorizontalScrollingOffset = _horizontal;
        }
        catch (InvalidOperationException)
        {
            // Tabelle noch nicht sichtbar bzw. im Zellwechsel – Position ist dann unwichtig
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }
}
