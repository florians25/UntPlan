namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Tabelle, in der Enter bzw. Tab während einer Eingabe die Eingabe übernimmt, ohne dass die aktuelle Zeile wechselt (Enter
/// springt sonst in die nächste Zeile – unter einem Filter verschwindet dann die gerade bearbeitete Zeile unerwartet). Escape
/// verwirft die Eingabe und stellt den vorherigen Wert wieder her.
/// </summary>
public class BleibendeTabelle : DataGridView
{
    protected override bool ProcessDialogKey(Keys keyData)
    {
        var taste = keyData & Keys.KeyCode;
        if (IsCurrentCellInEditMode && taste == Keys.Enter && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0)
        {
            // offene Auswahlliste: markierten Eintrag übernehmen (die Listensuche setzt ihn beim KeyDown), dann Eingabe beenden
            if (EditingControl is ComboBox { DroppedDown: true } cb) cb.DroppedDown = false;
            EndEdit();
            return true;
        }
        if (IsCurrentCellInEditMode && taste == Keys.Escape)
        {
            if (EditingControl is ComboBox { DroppedDown: true } cb) cb.DroppedDown = false;
            CancelEdit();
            EndEdit();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}
