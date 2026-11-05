using UntPlan.Core;

namespace UntPlan.App;

/// <summary>
/// Gemeinsames Bedienverhalten aller Masken (Runde 3 B): einheitliche Rückfrage vor dem Löschen, Rückgängig/Wiederherstellen mit
/// Hinweis in der Statuszeile, Hinweis auf ungültige Eingaben (Statuszeile und Tooltip an der Zelle, ohne Meldungsfenster).
/// </summary>
public static partial class Ui
{
    /// <summary>Erste Zeile jeder Rückfrage vor dem Löschen – in allen Masken gleich (auch bei Mehrfachauswahl).</summary>
    public const string LoeschenFrageText = "Wollen Sie die markierten Daten wirklich löschen?";

    /// <summary>Titel der Rückfrage vor dem Löschen.</summary>
    public const string LoeschenTitel = "UntPlan - Löschen";

    /// <summary>
    /// Einheitliche Rückfrage vor dem Löschen: „Wollen Sie die markierten Daten wirklich löschen?“, darunter was gelöscht wird
    /// („1 Absenz (HUGO)“ bzw. „3 Absenzen“) und ein optionaler Zusatz (Folgen des Löschens). Nichts wird ohne diese Rückfrage gelöscht.
    /// </summary>
    /// <param name="anzahl">Anzahl der markierten Datensätze (0 = nichts zu löschen, keine Rückfrage).</param>
    /// <param name="einzahl">Bezeichnung in der Einzahl (z. B. „Absenz“).</param>
    /// <param name="mehrzahl">Bezeichnung in der Mehrzahl (z. B. „Absenzen“).</param>
    /// <param name="name">Name des einzelnen Datensatzes (nur bei einem Datensatz angezeigt).</param>
    /// <param name="zusatz">Hinweis auf Folgen (z. B. „Daraus entstandene Vertretungen werden entfernt.“).</param>
    public static bool LoeschenBestaetigen(int anzahl, string einzahl, string mehrzahl, string? name = null, string? zusatz = null, IWin32Window? owner = null)
    {
        if (anzahl <= 0) return false;
        var was = anzahl == 1 ? $"1 {einzahl}{(string.IsNullOrWhiteSpace(name) ? "" : $" ({name})")}" : $"{anzahl} {mehrzahl}";
        var text = $"{LoeschenFrageText}\n\n{was}" + (string.IsNullOrWhiteSpace(zusatz) ? "" : $"\n\n{zusatz}");
        return Frage(text, LoeschenTitel, owner);
    }

    /// <summary>Letzter Hinweis in der Statuszeile (Selbsttest).</summary>
    public static string LetzterHinweis { get; private set; } = "";

    /// <summary>Kurze Rückmeldung links in der Statuszeile des Hauptfensters.</summary>
    public static void Statushinweis(string text)
    {
        LetzterHinweis = text;
        try { AppZustand.Haupt?.StatusMelden(text); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Rückgängig (Strg+Z) bzw. Wiederherstellen (Strg+Y) für die Schuldatei – in jeder Maske gleich, mit Hinweis in der Statuszeile.
    /// Eine laufende Zelleingabe wird vorher verworfen (sie gehört nicht zur Historie).
    /// </summary>
    public static void Rueckgaengig(bool wiederherstellen = false)
    {
        var doc = AppZustand.Doc;
        try
        {
            if (!wiederherstellen)
            {
                if (!doc.KannRueckgaengig) { Statushinweis("Es gibt keine Änderung, die rückgängig gemacht werden kann."); return; }
                var text = doc.RueckgaengigText;
                doc.Rueckgaengig();
                Statushinweis($"Rückgängig: {text}");
            }
            else
            {
                if (!doc.KannWiederholen) { Statushinweis("Es gibt keine Änderung, die wiederhergestellt werden kann."); return; }
                var text = doc.WiederholenText;
                doc.Wiederholen();
                Statushinweis($"Wiederhergestellt: {text}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            Fehler(ex);
        }
    }

    private static ToolTip? _eingabeTip;

    /// <summary>
    /// Ungültige Eingabe in einer Tabelle bzw. einem Feld: Hinweis in der Statuszeile und als Tooltip an der Zelle (wie im Vorbild,
    /// kein Meldungsfenster). Die Eingabe bleibt stehen und kann korrigiert oder mit Esc verworfen werden.
    /// </summary>
    public static void EingabeHinweis(Control? ort, string text)
    {
        Statushinweis(text);
        if (ort is null || ort.IsDisposed || !ort.IsHandleCreated || !ort.Visible) return;
        try
        {
            var punkt = new Point(4, ort.Height);
            if (ort is DataGridView g && g.CurrentCell is { } c && c.RowIndex >= 0 && c.ColumnIndex >= 0)
            {
                var r = g.GetCellDisplayRectangle(c.ColumnIndex, c.RowIndex, false);
                if (!r.IsEmpty) punkt = new Point(r.Left, r.Bottom + 2);
            }
            _eingabeTip ??= new ToolTip { ToolTipIcon = ToolTipIcon.Warning, ToolTipTitle = "Ungültige Eingabe" };
            _eingabeTip.Show(text, ort, punkt, 4000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException) { }
    }
}
