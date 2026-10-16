namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Zeilen einer gefilterten Liste (Vertretungsfenster mit „Offene Vertretungen“, „Kollisionen“ oder Elementauswahl), die
/// nach einer Bearbeitung sichtbar bleiben, auch wenn sie nicht mehr zum Filter passen – bis eine andere Zeile gewählt wird
/// (wie im Vorbild: Vertreter eintragen, danach Text eingeben, erst beim Zeilenwechsel verschwindet die Zeile).
/// </summary>
public sealed class ZeilenBehalten
{
    private readonly HashSet<int> _ids = new();

    /// <summary>Werden gerade Zeilen trotz Filter gezeigt?</summary>
    public bool Aktiv => _ids.Count > 0;

    public IReadOnlyCollection<int> Ids => _ids;

    /// <summary>Bearbeitete Zeilen merken (0 = keine Vertretung, wird ignoriert).</summary>
    public void Merken(IEnumerable<int> ids)
    {
        foreach (var i in ids)
            if (i != 0) _ids.Add(i);
    }

    public bool Enthaelt(int id) => _ids.Contains(id);

    /// <summary>
    /// Der Benutzer hat eine Zeile gewählt. Liegt sie unter den behaltenen Zeilen (z. B. weitere Spalte derselben Zeile),
    /// bleibt alles, wie es ist. Sonst werden die behaltenen Zeilen freigegeben; liefert true, wenn die Liste dafür neu
    /// aufgebaut werden muss (behaltene Zeilen verschwinden unter dem Filter).
    /// </summary>
    public bool Zeilenwechsel(int? neueId)
    {
        if (_ids.Count == 0) return false;
        if (neueId is int i && _ids.Contains(i)) return false;
        _ids.Clear();
        return true;
    }

    public void Leeren() => _ids.Clear();
}

/// <summary>
/// Abgleich der angezeigten mit den neu berechneten Tabellenzeilen: Ändern sich nur Werte (dieselben Zeilen), wird an Ort
/// und Stelle aktualisiert, ohne dass Zeilen springen, Fokus, Bearbeitung oder Bildlauf verloren gehen. Sonst wird neu
/// aufgebaut und die bisherige Zeile (per Schlüssel), Spalte und Bildlaufposition wiederhergestellt.
/// </summary>
public static class Listenabgleich
{
    /// <summary>Enthalten beide Listen genau dieselben Schlüssel (Reihenfolge egal, keine Doppelten)?</summary>
    public static bool GleicheZeilen(IReadOnlyList<string> alt, IReadOnlyList<string> neu)
    {
        if (alt.Count != neu.Count) return false;
        var a = alt.ToHashSet();
        return a.Count == alt.Count && neu.All(a.Contains) && neu.Distinct().Count() == neu.Count;
    }

    /// <summary>
    /// Reihenfolge der neuen Zeilen: Sind es dieselben Zeilen wie bisher und soll nicht neu sortiert werden, bleibt die angezeigte
    /// Reihenfolge (eine bearbeitete Zeile springt nicht, z. B. wenn in der Lehreransicht nach dem Vertreter sortiert wird).
    /// Liefert null, wenn neu aufgebaut werden muss.
    /// </summary>
    public static List<T>? AmOrt<T>(IReadOnlyList<string> alt, IReadOnlyList<T> neu, Func<T, string> schluessel, bool neuSortieren = false)
    {
        var neuSchluessel = neu.Select(schluessel).ToList();
        if (!GleicheZeilen(alt, neuSchluessel)) return null;
        if (neuSortieren && !alt.SequenceEqual(neuSchluessel)) return null;
        var nachSchluessel = neu.ToDictionary(schluessel);
        return alt.Select(k => nachSchluessel[k]).ToList();
    }

    /// <summary>
    /// Zeile, die nach dem Neuaufbau aktuell sein soll: dieselbe Zeile (Schlüssel), sonst die Zeile an der bisherigen Position
    /// (also die nachfolgende; am Ende die letzte). -1 bei leerer Liste.
    /// </summary>
    public static int Zielzeile(IReadOnlyList<string> neu, string? aktuell, int alterIndex)
    {
        if (neu.Count == 0) return -1;
        if (aktuell != null)
        {
            var i = IndexVon(neu, aktuell);
            if (i >= 0) return i;
        }
        return alterIndex < 0 ? 0 : Math.Min(alterIndex, neu.Count - 1);
    }

    /// <summary>
    /// Erste sichtbare Zeile nach dem Neuaufbau: dieselbe Zeile wie vorher oben im Bild, sonst die bisherige Position –
    /// jeweils so, dass die aktuelle Zeile sichtbar bleibt (<paramref name="sichtbar"/> = Anzahl Zeilen im Bild).
    /// </summary>
    public static int ErsteSichtbare(IReadOnlyList<string> neu, string? ersteAlt, int alterErsterIndex, int zielzeile, int sichtbar)
    {
        if (neu.Count == 0) return -1;
        var erste = ersteAlt is null ? -1 : IndexVon(neu, ersteAlt);
        if (erste < 0) erste = Math.Clamp(alterErsterIndex, 0, neu.Count - 1);
        sichtbar = Math.Max(1, sichtbar);
        if (zielzeile >= 0)
        {
            if (zielzeile < erste) erste = zielzeile;
            else if (zielzeile >= erste + sichtbar) erste = zielzeile - sichtbar + 1;
        }
        return Math.Clamp(erste, 0, neu.Count - 1);
    }

    private static int IndexVon(IReadOnlyList<string> liste, string schluessel)
    {
        for (var i = 0; i < liste.Count; i++)
            if (liste[i] == schluessel) return i;
        return -1;
    }
}
