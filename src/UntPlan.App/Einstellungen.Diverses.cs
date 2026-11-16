namespace UntPlan.App;

/// <summary>
/// Einstellungen | Diverses (Sichern, Stundenplan) und Logging und Crash Reporting – lokal gespeichert (nicht in der Schuldatei).
/// </summary>
public sealed partial class Benutzereinstellungen
{
    // ---- Sichern ----
    /// <summary>Anzahl der Sicherungsgenerationen (Save1 = neueste … SaveN = älteste) im Ordner „Sicherungsdateien“.</summary>
    public int SicherungsGenerationen { get; set; } = 5;
    /// <summary>„Automatische Sicherung nur bei geänderten Daten“ – keine identischen Save-Dateien.</summary>
    public bool SicherungNurBeiAenderung { get; set; } = true;
    /// <summary>Bei der automatischen Sicherung zusätzlich die geöffnete Datei selbst speichern.</summary>
    public bool SicherungInGeoeffneteDatei { get; set; } = true;
    /// <summary>„Mit dem aktuellen Datum starten“: Fenster mit Datumsauswahl beginnen mit dem Tagesdatum (sonst mit dem zuletzt verwendeten Datum).</summary>
    public bool MitAktuellemDatumStarten { get; set; } = true;
    /// <summary>Zuletzt verwendetes Datum (beim Beenden gemerkt).</summary>
    public DateOnly? LetztesDatum { get; set; }

    // ---- Stundenplan ----
    /// <summary>„Datumssynchronisation“: Datumswechsel in einem Stundenplan stellt alle offenen Stundenpläne auf dieses Datum.</summary>
    public bool Datumssynchronisation { get; set; } = true;
    /// <summary>„Bei Periodenwechsel Stundenplan auf Periodenbeginn stellen“.</summary>
    public bool PeriodenwechselPeriodenbeginn { get; set; }
    /// <summary>„Wochenanzeige“: Kalenderwochen statt Schulwochen anzeigen.</summary>
    public bool WochenanzeigeKalenderwochen { get; set; }

    // ---- Übersichtspläne ----
    /// <summary>Gespeicherte Filter der Übersichtspläne: Planart → Filtername → Kurznamen („Arist,Callas“).</summary>
    public Dictionary<string, Dictionary<string, string>> Uebersichtsfilter { get; set; } = new();
    /// <summary>Übersichtsplan Lehrer: Wochenstunden (verplant/offen) in der Zeilenbeschriftung.</summary>
    public bool UebersichtWochenstunden { get; set; }
    /// <summary>Übersichtsplan Klassen: Stammraum in der Zeilenbeschriftung.</summary>
    public bool UebersichtStammraum { get; set; }

    // ---- Logging und Crash Reporting ----
    /// <summary>Bei einem Programmfehler einen Absturzbericht im Benutzerordner ablegen.</summary>
    public bool AbsturzberichteSpeichern { get; set; } = true;
    /// <summary>Bei einem Absturz die ungespeicherten Daten als Wiederherstellungskopie sichern.</summary>
    public bool AbsturzWiederherstellungskopie { get; set; } = true;
    /// <summary>Beim nächsten Start melden, wenn UntPlan nicht ordnungsgemäß beendet wurde.</summary>
    public bool AbsturzHinweisBeimStart { get; set; } = true;
    /// <summary>Anzahl aufbewahrter Absturzberichte.</summary>
    public int AbsturzberichteBehalten { get; set; } = 20;
}
