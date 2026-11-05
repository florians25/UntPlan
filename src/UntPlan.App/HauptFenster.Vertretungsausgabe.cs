using UntPlan.App.Dialoge;
using UntPlan.App.Fenster;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App;

/// <summary>Vertretungsausgabe: Fenster „Formate / Vertretung“, Schnelldruck der Tageslisten, Import der Vertretungsplanung.</summary>
public sealed partial class HauptFenster
{
    /// <summary>Fenster „Formate / Vertretung“ (Schaltfläche „Vertretungsformate“, Kontextmenü des Vertretungsfensters).</summary>
    public VertretungsformateFenster VertretungsformateOeffnen()
    {
        if (!AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        return (VertretungsformateFenster)FensterOeffnen("vformate")!;
    }

    /// <summary>„Ausgabe ▸ Tageslisten“: alle Vertretungsformate mit dem Kennzeichen „Tagesliste“ für das Datum des Kalenders drucken.</summary>
    private void TageslistenDrucken() =>
        Druck.VertretungsplanDruck.Tageslisten(this, AppZustand.Datum, Vertretungsformate.Tageslisten(AppZustand.Einstellungen.VertretungsformateListe));

    /// <summary>„Ausgabe ▸ Druck Klassen / Druck Lehrer“: Vertretungsplan des Kalendertages (Seitenlayout mit Vorschau).</summary>
    private void VertretungsplanDrucken(bool lehrer) =>
        Druck.VertretungsplanDruck.Drucken(this, AppZustand.Datum, AppZustand.Datum, lehrer, true);

    /// <summary>„Datei ▸ Import/Export ▸ Import Vertretungsplanung“ (Handbuch Kap. 16.7).</summary>
    private void VertretungsimportStarten()
    {
        if (AppZustand.Doc.Schreibgeschuetzt) { Ui.Info("Die Datei ist schreibgeschützt geöffnet."); return; }
        VertretungsimportDialog.Zeigen(this);
    }
}
