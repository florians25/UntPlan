namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Kopieren nur als reiner Text – programmweit für alle Tabellen (Strg+C, Strg+Einfg) und formatierte Textfelder
/// (Kopieren, Ausschneiden, Einfügen ohne Formatierung): keine RTF-/HTML-Formate, keine Objekte, keine ausgeblendeten Spalten
/// oder Zusatzdaten – nur die sichtbaren Zellwerte, getrennt durch Tabulator und Zeilenumbruch.
/// </summary>
public static class NurText
{
    /// <summary>Text der markierten Zellen (nur sichtbare Spalten und Zeilen, in Anzeigereihenfolge).</summary>
    public static string TabellenText(DataGridView g)
    {
        var zellen = g.SelectedCells.Cast<DataGridViewCell>()
            .Where(c => c.RowIndex >= 0 && c.ColumnIndex >= 0 && c.OwningColumn.Visible && c.OwningRow.Visible).ToList();
        if (zellen.Count == 0 && g.CurrentCell is { } cur && cur.OwningColumn.Visible) zellen.Add(cur);
        if (zellen.Count == 0) return "";
        var spalten = zellen.Select(c => c.OwningColumn).Distinct().OrderBy(c => c.DisplayIndex).ToList();
        var markiert = zellen.ToHashSet();
        var zeilen = new List<string>();
        foreach (var zeile in zellen.Select(c => c.RowIndex).Distinct().OrderBy(i => i))
            zeilen.Add(string.Join("\t", spalten.Select(s => g.Rows[zeile].Cells[s.Index] is var z && markiert.Contains(z) ? ZellText(z) : "")));
        return string.Join("\r\n", zeilen);
    }

    /// <summary>Angezeigter Wert einer Zelle als einzeiliger Text (Kontrollkästchen „x“, Bilder leer).</summary>
    public static string ZellText(DataGridViewCell z)
    {
        if (z is DataGridViewImageCell) return "";
        object? wert;
        try { wert = z.FormattedValue; } catch (Exception) { wert = z.Value; }
        var text = wert switch
        {
            null => "",
            bool b => b ? "x" : "",
            CheckState cs => cs == CheckState.Checked ? "x" : "",
            Image => "",
            _ => wert.ToString() ?? "",
        };
        return Einzeilig(text);
    }

    /// <summary>Zeilenumbrüche und Tabulatoren im Zellinhalt zu Leerzeichen (die Tabellenstruktur bleibt erhalten).</summary>
    public static string Einzeilig(string text) =>
        string.Join(" ", text.Replace("\r\n", "\n").Split('\n', '\r', '\t').Select(t => t.Trim()).Where(t => t.Length > 0));

    /// <summary>Reinen Text in die Zwischenablage legen (ausschließlich Unicode-Text).</summary>
    public static bool Kopieren(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var versuch = 0; versuch < 3; versuch++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException) { System.Threading.Thread.Sleep(40); }
        }
        return false;
    }

    /// <summary>Text aus der Zwischenablage (nur Text, ohne Formatierung).</summary>
    public static string? Einfuegetext()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }

    private static bool IstKopieren(KeyEventArgs e) => (e.Control && !e.Alt && e.KeyCode is Keys.C or Keys.Insert) && !e.Shift;

    /// <summary>Tabelle: Strg+C bzw. Strg+Einfg kopiert die markierten Zellen als reinen Text (eigene Fensterbelegung hat Vorrang).</summary>
    public static void Tabelle(DataGridView g)
    {
        g.KeyDown += (_, e) =>
        {
            if (e.Handled || !IstKopieren(e) || g.IsCurrentCellInEditMode) return;
            Kopieren(TabellenText(g));
            e.Handled = e.SuppressKeyPress = true;
        };
    }

    /// <summary>Formatiertes Textfeld: Kopieren, Ausschneiden und Einfügen nur als reiner Text.</summary>
    public static void Textfeld(RichTextBox t)
    {
        t.KeyDown += (_, e) =>
        {
            if (e.Handled) return;
            var ausschneiden = (e.Control && e.KeyCode == Keys.X) || (e.Shift && !e.Control && e.KeyCode == Keys.Delete);
            var einfuegen = (e.Control && e.KeyCode == Keys.V) || (e.Shift && !e.Control && e.KeyCode == Keys.Insert);
            if (IstKopieren(e) || ausschneiden)
            {
                if (t.SelectionLength > 0) Kopieren(t.SelectedText);
                if (ausschneiden && !t.ReadOnly && t.SelectionLength > 0) t.SelectedText = "";
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (einfuegen)
            {
                if (!t.ReadOnly && Einfuegetext() is { } text) t.SelectedText = text.Replace("\r\n", "\n");
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }
}
