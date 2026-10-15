using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

public enum Tageszeit
{
    Leer = 0,
    Vormittag = 1,
    Nachmittag = 2,
}

public sealed class Stundenzeit
{
    public int Nr { get; set; }
    public string Bezeichnung { get; set; } = "";
    public TimeOnly Beginn { get; set; }
    public TimeOnly Ende { get; set; }
}

public sealed class Pause
{
    /// <summary>Pause liegt nach dieser Stunde (zwischen Stunde und Stunde+1).</summary>
    public int NachStunde { get; set; }
    public string Bezeichnung { get; set; } = "";
    public int DauerMinuten { get; set; }
    /// <summary>Pausenaufsicht erforderlich.</summary>
    public bool Aufsicht { get; set; } = true;
}

/// <summary>Zeitraster: Tage pro Woche, Stunden pro Tag, Vormittag/Nachmittag/leer, Zeiten, Pausen, Mittagspause.</summary>
public sealed class Zeitraster
{
    public int TageProWoche { get; set; } = 5;
    public int StundenProTag { get; set; } = 10;
    /// <summary>0 = Montag.</summary>
    public int ErsterSchultag { get; set; }
    /// <summary>Nummer der ersten Stunde (0 oder 1).</summary>
    public int ErsteStundennummer { get; set; } = 1;
    public int MaxNachmittagsstunden { get; set; } = 4;
    public List<Stundenzeit> Zeiten { get; set; } = new();
    /// <summary>Tageszeit je Tag/Stunde, Schlüssel Tag*100+Stunde. Fehlt ein Eintrag gilt Standard (Vormittag bis Grenze).</summary>
    public Dictionary<int, Tageszeit> Belegung { get; set; } = new();
    /// <summary>Letzte Vormittagsstunde (Vormittags-/Nachmittagsgrenze).</summary>
    public int LetzteVormittagsstunde { get; set; } = 6;
    public List<Pause> Pausen { get; set; } = new();
    /// <summary>Flexible Mittagspause: früheste und späteste Stunde, die als Mittagspause dienen kann (0 = keine).</summary>
    public int MittagspauseVon { get; set; }
    public int MittagspauseBis { get; set; }
    public int MaxKlassenGleicheMittagspause { get; set; }
    public int NormalstundeMinuten { get; set; } = 45;

    public Tageszeit Art(int tag, int stunde)
    {
        if (tag < 0 || tag >= TageProWoche || stunde < 1 || stunde > StundenProTag) return Tageszeit.Leer;
        if (Belegung.TryGetValue(tag * 100 + stunde, out var t)) return t;
        return stunde <= LetzteVormittagsstunde ? Tageszeit.Vormittag : Tageszeit.Nachmittag;
    }

    public bool Verfuegbar(int tag, int stunde) => Art(tag, stunde) != Tageszeit.Leer;

    public Stundenzeit? Zeit(int stunde) => Zeiten.FirstOrDefault(z => z.Nr == stunde);

    public string StundenLabel(int stunde) => (stunde - 1 + ErsteStundennummer).ToString();
}

public sealed class Ferien
{
    public int Id { get; set; }
    public string Kurzname { get; set; } = "";
    public string Langname { get; set; } = "";
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public bool Feiertag { get; set; }
    /// <summary>Wochenbuchstabe der ersten Woche nach den Ferien (A, B, …), leer = Zyklus läuft weiter.</summary>
    public string WocheDanach { get; set; } = "";
    /// <summary>Wochennummerierung: die Ferienwoche zählt bei der Nummerierung der Schulwochen (A/B) mit.</summary>
    public bool Wochennummerierung { get; set; }
}

public sealed class Einstellungen
{
    public string Schulname { get; set; } = "";
    public string Schulname2 { get; set; } = "";
    public string Adresse { get; set; } = "";
    public string Schulnummer { get; set; } = "";
    public string Land { get; set; } = "Deutschland";
    public string Region { get; set; } = "Nordrhein-Westfalen";
    public string Schulform { get; set; } = "GY";
    public DateOnly SchuljahrVon { get; set; } = new(2026, 9, 2);
    public DateOnly SchuljahrBis { get; set; } = new(2027, 7, 16);
    /// <summary>Wochenperiodizität (A/B-Wochen) aktiv.</summary>
    public int Wochenperiodizitaet { get; set; } = 1;
    /// <summary>Buchstabe der ersten Schulwoche (A, B, …).</summary>
    public string ErsteSchulwoche { get; set; } = "A";
    public bool TagesgenaueWertrechnung { get; set; }
    /// <summary>NRW: Mehrarbeit vergütungsfähig, wenn im Kalendermonat mehr als diese Anzahl Stunden.</summary>
    public int Mehrarbeitsgrenze { get; set; } = 3;
    /// <summary>Entfall wird in Randstunden ab diesem Jahrgang vorgeschlagen (Sek I: verlässliche Schule).</summary>
    public string EntfallAbJahrgang { get; set; } = "EF";
    /// <summary>Unterrichtsänderungen automatisch in Tochterperioden übernehmen (sofern dort unverändert).</summary>
    public bool UnterrichtInFolgeperioden { get; set; } = true;
    /// <summary>Automatische Bereitschaftsplanung: Bereitschaften nur als Doppelstunden verplanen.</summary>
    public bool BereitschaftNurDoppelstunden { get; set; }
    public string Vertretungsplantitel { get; set; } = "Vertretungsplan";
    public Dictionary<string, int> Vertretungsgewichtung { get; set; } = new();
    public PortalEinstellungen Portal { get; set; } = new();
    public string Farbschema { get; set; } = "Blau";
}

public sealed class PortalEinstellungen
{
    public string Adresse { get; set; } = "";
    public string SchulId { get; set; } = "";
    public string ApiSchluessel { get; set; } = "";
    public int TageVoraus { get; set; } = 7;
    /// <summary>Keine Tagesbegrenzung: alles bis zum Schuljahresende übertragen.</summary>
    public bool OhneTagesbegrenzung { get; set; }
    public DateTime? LetzteSynchronisation { get; set; }
    public int Revision { get; set; }
    /// <summary>Anfangsdatum für Unterricht, Vertretungen und Absenzen aktivieren (sonst ab heute).</summary>
    public bool AnfangsdatumAktiv { get; set; }
    public DateOnly? Anfangsdatum { get; set; }
    public bool IgnoriertenUnterrichtUebertragen { get; set; }
    public bool FerienNichtExportieren { get; set; }
    public bool LehrerEmail { get; set; }
    public bool LehrerAbsenzen { get; set; } = true;
    public bool Absenzgruende { get; set; }
    /// <summary>Schülerliste (Vorname, Name, Klasse) für Klassenbuch und Fehlzeiten übertragen.</summary>
    public bool SchuelerExportieren { get; set; }
    /// <summary>Letzter erfolgreicher Export bzw. Import je Teil (stammdaten, unterricht, vertretungen, absenzen, studenten, kurse).</summary>
    public Dictionary<string, DateTime> LetzteUebertragung { get; set; } = new();
    /// <summary>Stundenplanänderungen aus UntPlanWeb: Pausenaufsichten vor Prüfungen außer Betracht lassen.</summary>
    public bool PausenaufsichtVorPruefungIgnorieren { get; set; }
    /// <summary>Stundenplanänderungen aus UntPlanWeb: Pausenaufsichten nach Prüfungen außer Betracht lassen.</summary>
    public bool PausenaufsichtNachPruefungIgnorieren { get; set; } = true;
    /// <summary>Für wie viele Tage in die Zukunft Stundenplanänderungen standardmäßig importiert werden.</summary>
    public int StplAenderungenTage { get; set; } = 14;
    /// <summary>Bereits übernommene bzw. ignorierte Stundenplanänderungen aus UntPlanWeb (werden nicht erneut importiert).</summary>
    public List<StplAenderungsEintrag> StplAenderungen { get; set; } = new();
}

/// <summary>Bearbeitete Stundenplanänderung aus UntPlanWeb (Buchung, Aktivität, Prüfung).</summary>
public sealed class StplAenderungsEintrag
{
    public string Id { get; set; } = "";
    /// <summary>true = übernommen, false = ignoriert.</summary>
    public bool Uebernommen { get; set; }
    public DateTime Zeitpunkt { get; set; } = DateTime.Now;
    public string Benutzer { get; set; } = "";
    /// <summary>Die Rückmeldung selbst (für die Anzeige „Bereits durchgeführte/ignorierte Änderungen“).</summary>
    public UntPlan.Web.Contracts.WebRueckmeldung Meldung { get; set; } = new();
}

// Abwesenheiten, Vertretungen, Veranstaltungen usw.: siehe Vertretungsmodell.cs

public sealed class Kommentar
{
    public int Id { get; set; }
    public ElementTyp ElementTyp { get; set; }
    public int ElementId { get; set; }
    public string Feld { get; set; } = "";
    /// <summary>Kategorie 1–8 (Farbe frei definierbar).</summary>
    public int Kategorie { get; set; } = 1;
    public string Text { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
}

/// <summary>Gespeicherte Ansicht eines Stammdaten-/Unterrichtsfensters (eingeblendete Spalten).</summary>
public sealed class Ansicht
{
    public string Fenster { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Spalten { get; set; } = new();
    public List<Sortierkriterium> Sortierung { get; set; } = new();
}

public sealed class Sortierkriterium
{
    public string Feld { get; set; } = "";
    public bool Absteigend { get; set; }
}

public sealed class StundentafelZeile
{
    public string Jahrgang { get; set; } = "";
    public int FachId { get; set; }
    public decimal Stunden { get; set; }
}

public sealed class KommentarKategorie
{
    public int Nr { get; set; }
    public string Name { get; set; } = "";
    public int Farbe { get; set; }
}

/// <summary>Wurzelobjekt einer UntPlan-Schuldatei (*.up).</summary>
public sealed class Schuldatei
{
    public const int AktuelleVersion = 1;
    public int Version { get; set; } = AktuelleVersion;
    public Einstellungen Einstellungen { get; set; } = new();
    public Zeitraster Zeitraster { get; set; } = new();
    public List<Ferien> Ferien { get; set; } = new();
    public List<Klasse> Klassen { get; set; } = new();
    public List<Lehrer> Lehrer { get; set; } = new();
    public List<Raum> Raeume { get; set; } = new();
    public List<Fach> Faecher { get; set; } = new();
    public List<Raumgruppe> Raumgruppen { get; set; } = new();
    public List<Gang> Gaenge { get; set; } = new();
    public List<Abteilung> Abteilungen { get; set; } = new();
    public List<Abwesenheitsgrund> Abwesenheitsgruende { get; set; } = new();
    public List<Schueler> Schueler { get; set; } = new();
    /// <summary>Nur ältere Dateien: Unterricht vor der Umstellung auf Perioden (wird beim Laden übernommen).</summary>
    [JsonPropertyName("Unterrichte")] public List<Unterricht>? UnterrichteAlt { get; set; }
    public List<Periode> Perioden { get; set; } = new();
    public List<UGruppe> UGruppen { get; set; } = new();
    public int AktivePeriodeId { get; set; }
    public Dictionary<string, int> Gewichtung { get; set; } = new();
    public List<Abwesenheit> Abwesenheiten { get; set; } = new();
    /// <summary>Schülerabwesenheiten (Fehlzeiten aus dem Klassenbuch von UntPlanWeb, Import „Studenten*“).</summary>
    public List<SchuelerAbwesenheit> SchuelerAbwesenheiten { get; set; } = new();
    /// <summary>Klassenbuch je Unterricht (Lehrstoff, Hausaufgaben) aus UntPlanWeb, Import „Kurse“ – nur lesend.</summary>
    public List<KlassenbuchEintrag> Klassenbuch { get; set; } = new();
    public List<Vertretung> Vertretungen { get; set; } = new();
    /// <summary>Nur ältere Dateien (Pausenaufsichten vor der Umstellung auf Perioden).</summary>
    [JsonPropertyName("Pausenaufsichten")] public List<Pausenaufsicht>? PausenaufsichtenAlt { get; set; }
    public List<Aufsichtsbedarf> Aufsichtsbedarf { get; set; } = new();
    public List<Tagestext> Tagestexte { get; set; } = new();
    public List<Kalendertag> Kalender { get; set; } = new();
    public List<Veranstaltung> Veranstaltungen { get; set; } = new();
    public List<Vormerkung> Vormerkungen { get; set; } = new();
    public List<Klausur> Klausuren { get; set; } = new();
    public List<Bereitschaftsbedarf> Bereitschaftsbedarf { get; set; } = new();
    public VertretungsEinstellungen Vertretungseinstellungen { get; set; } = new();
    public List<Kommentar> Kommentare { get; set; } = new();
    public List<KommentarKategorie> KommentarKategorien { get; set; } = new();
    public List<Ansicht> Ansichten { get; set; } = new();
    public List<StundentafelZeile> Stundentafel { get; set; } = new();
    public List<StundenplanFormat> Formate { get; set; } = new();
    /// <summary>
    /// Ansichtseinstellungen der Oberfläche (Fensteranordnungen je Modus, Tabellen- und Spaltenformate, Vertretungsansichten,
    /// Druckformate …) – werden von der Desktop-App gefüllt und beim Laden wiederhergestellt. null = ältere Datei ohne Ansicht.
    /// </summary>
    public JsonObject? Ansichtseinstellungen { get; set; }
    public int NaechsteId { get; set; } = 1;

    public int NeueId() => NaechsteId++;

    [JsonIgnore]
    public Periode AktivePeriode
    {
        get
        {
            if (Perioden.Count == 0) Perioden.Add(new Periode { Id = NeueId(), Name = "Periode 1", Von = Einstellungen.SchuljahrVon });
            return Perioden.FirstOrDefault(p => p.Id == AktivePeriodeId) ?? Perioden[0];
        }
    }

    /// <summary>Stundenplan der aktiven Periode.</summary>
    [JsonIgnore] public List<Planstunde> Stundenplan => AktivePeriode.Plan;

    /// <summary>Unterricht der aktiven Periode.</summary>
    [JsonIgnore] public List<Unterricht> Unterrichte => AktivePeriode.Unterrichte ??= new();

    /// <summary>Pausenaufsichten der aktiven Periode.</summary>
    [JsonIgnore] public List<Pausenaufsicht> Pausenaufsichten => AktivePeriode.Pausenaufsichten ??= new();

    /// <summary>Unterricht aller Perioden (für Stammdatenpflege, z. B. beim Löschen von Elementen).</summary>
    [JsonIgnore] public IEnumerable<Unterricht> AlleUnterrichte => Perioden.SelectMany(p => p.Unterrichte ?? new());

    /// <summary>Pausenaufsichten aller Perioden.</summary>
    [JsonIgnore] public IEnumerable<Pausenaufsicht> AllePausenaufsichten => Perioden.SelectMany(p => p.Pausenaufsichten ?? new());

    /// <summary>Periode, die an einem Datum gilt (innerste, d. h. zuletzt beginnende Periode, die das Datum abdeckt).</summary>
    public Periode PeriodeAm(DateOnly datum)
    {
        var p = Perioden.Where(x => x.Deckt(datum)).OrderByDescending(x => x.Von).ThenByDescending(x => x.Id).FirstOrDefault()
                ?? Perioden.Where(x => x.Von <= datum).OrderByDescending(x => x.Von).FirstOrDefault();
        return p ?? Perioden.OrderBy(x => x.Von).FirstOrDefault() ?? AktivePeriode;
    }

    public IEnumerable<Element> AlleElemente(ElementTyp typ) => typ switch
    {
        ElementTyp.Klasse => Klassen,
        ElementTyp.Lehrer => Lehrer,
        ElementTyp.Raum => Raeume,
        ElementTyp.Fach => Faecher,
        ElementTyp.Raumgruppe => Raumgruppen,
        ElementTyp.Gang => Gaenge,
        ElementTyp.Abteilung => Abteilungen,
        ElementTyp.Abwesenheitsgrund => Abwesenheitsgruende,
        ElementTyp.Schueler => Schueler,
        _ => Enumerable.Empty<Element>(),
    };
}
