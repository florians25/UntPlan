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

    /// <summary>Schlüssel der Fenster der Fenstergruppe „Ausgabe“ (danach der Langname der Vertretungsansicht).</summary>
    private const string AusgabeSchluessel = "vausgabe:";

    /// <summary>Vertretungsansichten der Fenstergruppe „Ausgabe“: eine für die Schüler, eine für die Lehrer (Handbuch Kap. 3.9.1).</summary>
    internal static readonly string[] AusgabeAnsichten = { "Druck - Klasse", "Druck - Lehrer" };

    /// <summary>
    /// Schaltfläche „Ausgabe“ wie im Handbuch (Kap. 3.9.1): zeigt die vorgefertigten Vertretungsansichten für die Ausgabe als Fenstergruppe –
    /// links die Schülerliste („Druck - Klasse“), rechts die Lehrerliste („Druck - Lehrer“); gedruckt wird über „Seitenansicht“ des
    /// jeweiligen Fensters. Die Ansicht des Arbeitsfensters „Vertretungen“ bleibt unverändert.
    /// </summary>
    internal List<VertretungenFenster> AusgabeFenstergruppe()
    {
        if (!AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        AlleSchliessen();
        var mdi = Controls.OfType<MdiClient>().First().ClientSize;
        var res = new List<VertretungenFenster>();
        for (var i = 0; i < AusgabeAnsichten.Length; i++)
        {
            if (FensterOeffnen(AusgabeSchluessel + AusgabeAnsichten[i]) is not VertretungenFenster f) continue;
            f.WindowState = FormWindowState.Normal;
            f.Bounds = new Rectangle(i * mdi.Width / 2, 0, mdi.Width / 2, mdi.Height);
            res.Add(f);
        }
        res.FirstOrDefault()?.Activate();
        return res;
    }

    /// <summary>Vertretungsfenster mit fester Ansicht (Fenstergruppe „Ausgabe“); die gemerkte Ansicht des Arbeitsfensters bleibt.</summary>
    private Form AusgabeFenster(string ansicht) => Oeffnen(AusgabeSchluessel + ansicht, () =>
    {
        var e = AppZustand.Einstellungen;
        var vorher = e.Vertretungsansicht;
        var f = new VertretungenFenster();
        f.AnsichtSetzen(ansicht);
        if (e.Vertretungsansicht != vorher) { e.Vertretungsansicht = vorher; e.Speichern(); }
        return f;
    });

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
