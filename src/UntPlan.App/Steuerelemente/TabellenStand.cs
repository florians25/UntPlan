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

/// <summary>
/// Tabelle nach einer Änderung aktualisieren, ohne die ganze Liste neu zu laden: Werte bestehender Zeilen werden an Ort und
/// Stelle erneuert, weggefallene Zeilen entfernt und neue an ihrem sortierten Platz eingefügt (<see cref="Listenabgleich.Zusammenfuehren"/>).
/// Aktuelle Zeile und Zelle, Markierung, Bildlauf und Reihenfolge bleiben stehen – nichts verrutscht. Nur wenn neu sortiert werden
/// soll (Filter, Sortierung, Ansicht) bzw. keine Zeile erhalten bleibt, wird neu aufgebaut und der Stand per Schlüssel wiederhergestellt.
/// </summary>
public static class TabellenAbgleich
{
    /// <summary>Anzahl der Neuaufbauten bzw. Abgleiche (Selbsttest).</summary>
    public static int Neuaufbauten { get; private set; }
    public static int Abgleiche { get; private set; }

    /// <param name="g">Tabelle (der Aufrufer unterdrückt währenddessen seine Auswahl-Ereignisse).</param>
    /// <param name="neu">Gewünschte Zeilen in neuer Sortierung (auch Platzhalter wie die Eingabezeile „neu“).</param>
    /// <param name="schluessel">Fachlicher Schlüssel eines Eintrags (eindeutig).</param>
    /// <param name="zeilenSchluessel">Schlüssel einer angezeigten Zeile (aus ihrem Tag).</param>
    /// <param name="setzen">Füllt eine Zeile vollständig (Werte, Tag, Zeilenformat) – für bestehende und neue Zeilen.</param>
    /// <param name="neuSortieren">Reihenfolge laut <paramref name="neu"/> erzwingen (z. B. nach Klick auf eine Spaltenüberschrift).</param>
    /// <param name="aktuell">Nach dem Abgleich diese Zeile wählen (z. B. gerade angelegter Datensatz), null = bisherige.</param>
    /// <returns>true, wenn an Ort und Stelle abgeglichen wurde; false nach einem Neuaufbau.</returns>
    public static bool Anwenden<T>(DataGridView g, IReadOnlyList<T> neu, Func<T, string> schluessel, Func<DataGridViewRow, string?> zeilenSchluessel,
        Action<DataGridViewRow, T> setzen, bool neuSortieren = false, string? aktuell = null)
    {
        var alt = g.Rows.Cast<DataGridViewRow>().Select(r => zeilenSchluessel(r) ?? "").ToList();
        var ziel = neuSortieren && !alt.SequenceEqual(neu.Select(schluessel)) ? null : Listenabgleich.Zusammenfuehren(alt, neu, schluessel);
        var zielSchluessel = ziel?.Select(schluessel).ToHashSet();
        if (ziel is null || zielSchluessel is null || g.Columns.Count == 0 || (alt.Count > 0 && !alt.Any(zielSchluessel.Contains)))
        {
            Neuaufbau(g, neu, zeilenSchluessel, setzen, aktuell);
            return false;
        }
        Abgleiche++;
        var stand = TabellenStand.Merken(g, zeilenSchluessel);
        var ersteIndex = g.FirstDisplayedScrollingRowIndex;
        var ersteSchluessel = ersteIndex >= 0 && ersteIndex < alt.Count ? alt[ersteIndex] : null;
        var aktuellWeg = stand.Aktuell is { } ak && !zielSchluessel.Contains(ak);
        var zeilen = alt.ToList();
        try
        {
            // weggefallene Zeilen entfernen (von unten, damit die Indizes stimmen)
            for (var i = zeilen.Count - 1; i >= 0; i--)
            {
                if (zielSchluessel.Contains(zeilen[i])) continue;
                g.Rows.RemoveAt(i);
                zeilen.RemoveAt(i);
            }
            // verbliebene Zeilen stehen in derselben Reihenfolge wie im Ziel: Werte erneuern, neue Zeilen einfügen
            for (var i = 0; i < ziel.Count; i++)
            {
                var k = schluessel(ziel[i]);
                if (i < zeilen.Count && zeilen[i] == k)
                {
                    setzen(g.Rows[i], ziel[i]);
                    continue;
                }
                g.Rows.Insert(i, 1);
                zeilen.Insert(i, k);
                setzen(g.Rows[i], ziel[i]);
            }
        }
        catch (InvalidOperationException)
        {
            // Tabelle im Zellwechsel: sicher neu aufbauen
            Neuaufbau(g, neu, zeilenSchluessel, setzen, aktuell ?? stand.Aktuell);
            return false;
        }
        try
        {
            if (aktuell != null || aktuellWeg)
            {
                if (aktuell != null) stand.AktuellSetzen(aktuell);
                stand.Wiederherstellen(g, zeilenSchluessel, false);
            }
            else if (ersteSchluessel != null && zeilen.IndexOf(ersteSchluessel) is var erste and >= 0 && erste != g.FirstDisplayedScrollingRowIndex &&
                     g.Rows[erste].Visible && !g.Rows[erste].Frozen)
                // oberste sichtbare Zeile bleibt oben, auch wenn darüber Zeilen eingefügt bzw. entfernt wurden
                g.FirstDisplayedScrollingRowIndex = erste;
        }
        catch (InvalidOperationException) { }
        return true;
    }

    private static void Neuaufbau<T>(DataGridView g, IReadOnlyList<T> neu, Func<DataGridViewRow, string?> zeilenSchluessel, Action<DataGridViewRow, T> setzen, string? aktuell)
    {
        Neuaufbauten++;
        var stand = TabellenStand.Merken(g, zeilenSchluessel);
        if (aktuell != null) stand.AktuellSetzen(aktuell);
        g.Rows.Clear();
        if (neu.Count > 0) g.Rows.Add(neu.Count);
        for (var i = 0; i < neu.Count; i++) setzen(g.Rows[i], neu[i]);
        stand.Wiederherstellen(g, zeilenSchluessel, true);
    }
}
