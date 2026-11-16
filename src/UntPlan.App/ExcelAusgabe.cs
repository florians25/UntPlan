using UntPlan.Core.Austausch;

namespace UntPlan.App;

/// <summary>
/// Fenster, deren Inhalt sich nach Microsoft Excel exportieren lässt. Wie im Handbuch („Export nach Microsoft Excel“) ist die
/// Schaltfläche „Export nach Excel“ in der Schnellzugriffsleiste nur aktiv, wenn das aktive Fenster das unterstützt.
/// </summary>
public interface IExcelExport
{
    /// <summary>Name der Arbeitsmappe bzw. des Blattes.</summary>
    string ExcelName { get; }

    /// <summary>Blätter mit den Zeilen des aktuellen Fensterinhalts.</summary>
    IReadOnlyList<Excel.Blatt> ExcelBlaetter();
}

/// <summary>Ausgabe nach Excel: echte .xlsx-Datei, wahlweise direkt geöffnet (wie „Exceldruck“) oder unter einem Namen gespeichert.</summary>
public static class ExcelAusgabe
{
    /// <summary>Inhalt eines Rasters (sichtbare Spalten in Anzeigereihenfolge, Kopfzeile fett) als Blatt.</summary>
    public static Excel.Blatt Blatt(string name, DataGridView g)
    {
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var zeilen = new List<Excel.Zeile>();
        if (spalten.Any(c => c.HeaderText.Length > 0)) zeilen.Add(new Excel.Zeile(spalten.Select(c => c.HeaderText).ToList(), true));
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow || !r.Visible) continue;
            var fett = r.DefaultCellStyle.Font?.Bold == true;
            zeilen.Add(new Excel.Zeile(spalten.Select(c =>
            {
                var zelle = r.Cells[c.Index];
                if (zelle.Tag is int[] w && w.Length >= 3) return $"{w[0]} / {w[1]} / {w[2]}";
                return zelle.Value switch { bool b => b ? "X" : "", _ => zelle.FormattedValue?.ToString() ?? "" };
            }).ToList(), fett));
        }
        return new Excel.Blatt(name, zeilen);
    }

    /// <summary>Direkt in Excel öffnen (temporäre .xlsx-Datei, geöffnet mit dem zugeordneten Programm).</summary>
    public static void Oeffnen(IExcelExport quelle)
    {
        if (Ui.FehlerProtokoll != null) return; // Selbsttest: kein externes Programm
        try
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"UntPlan_{Dateiname(quelle.ExcelName)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            File.WriteAllBytes(pfad, Excel.Arbeitsmappe(quelle.ExcelBlaetter()));
            HauptFenster.Starten(pfad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Fehler(ex); }
    }

    /// <summary>Als Excel-Arbeitsmappe speichern (Dateiauswahl).</summary>
    public static void Speichern(IWin32Window owner, IExcelExport quelle)
    {
        if (Ui.FehlerProtokoll != null) return;
        using var sfd = new SaveFileDialog { Filter = "Excel-Arbeitsmappe|*.xlsx", FileName = Dateiname(quelle.ExcelName) + ".xlsx", Title = "Als Excel-Datei speichern" };
        if (sfd.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(sfd.FileName, Excel.Arbeitsmappe(quelle.ExcelBlaetter()));
            Ui.Statushinweis($"Excel-Datei gespeichert: {sfd.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Fehler(ex); }
    }

    private static string Dateiname(string name)
    {
        var n = string.Concat((name ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        return n.Length > 0 ? n : "Liste";
    }
}
