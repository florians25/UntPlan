namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Tabelle mit dem einheitlichen Eingabeverhalten aller Tabellen (<see cref="Tabellenbedienung"/>): Enter übernimmt die Eingabe
/// geprüft und springt zum nächsten änderbaren Feld, Escape verwirft die Eingabe und stellt den vorherigen Wert wieder her.
/// </summary>
public class BleibendeTabelle : DataGridView
{
    protected override bool ProcessDialogKey(Keys keyData) =>
        Tabellenbedienung.Bearbeitungstaste(this, keyData) || base.ProcessDialogKey(keyData);
}
