using System.Text.Json.Serialization;

namespace UntPlanMobile.Dienste;

// ---------------------------------------------------------------------------------------------
// Antworten von UntPlanWeb (/api/app/plan, /api/app/elemente). Die Felder entsprechen dem JSON,
// das UntPlanWeb liefert; gelesen wird ohne Rücksicht auf Groß- und Kleinschreibung.
// ---------------------------------------------------------------------------------------------

/// <summary>Angezeigter Wert (Klasse, Lehrkraft, Raum); Alt = ursprünglicher Wert, Neu = geändert.</summary>
public sealed record PlanWert(string K, string? Alt = null, bool Neu = false);

/// <summary>Stundenkarte aus UntPlanWeb (Status regulaer, geaendert, offen, entfall, abwesend, klausur, bereitschaft, buchung, vorgemerkt, veranstaltung).</summary>
public sealed record PlanKarte(
    string Datum, int Von, int Bis, string Beginn, string Ende, string Status,
    string Fach, string? FachAlt, string FachName, string Farbe,
    List<PlanWert> Klassen, List<PlanWert> Lehrer, List<PlanWert> Raeume,
    string Art, string Text, int? Vertretung, int? Unterricht, string Gruppe = "",
    string? NotizSchueler = null, string? NotizLehrer = null, bool HausaufgabeFaellig = false,
    bool LehrerAbwesend = false, bool Bereitschaft = false);

/// <summary>Pausenaufsicht im Lehrerplan. Status regulaer, vertreten (fällt für mich weg), vertretung (übernommen).</summary>
public sealed record PlanAufsicht(string Datum, int NachStunde, string Beginn, string Ende, string Gang, string GangName, string Status, string Lehrer, string? Alt);

public sealed record PlanEreignis(string Titel, string Art, int Von, int Bis, bool Ganztaegig, List<string> Klassen, List<string> Lehrer, List<string> Raeume);

public sealed record PlanTag(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, bool Tagesaktuell,
    List<string> Hinweise, List<PlanEreignis> Ereignisse, string? FreiArt = null);

public sealed record PlanZeit(int Nr, string Bezeichnung, string Beginn, string Ende);

public sealed record Wochenplan(string Typ, string Element, string Name, string Montag, string Woche, List<PlanZeit> Zeitraster,
    List<PlanTag> Tage, List<PlanKarte> Karten, List<PlanAufsicht> Aufsichten);

public sealed record Ferienzeit(string Name, string Von, string Bis, bool Feiertag);

public sealed record ElementKlasse(string Kurz, string Name, string? Jahrgang, List<string>? Leitung);
public sealed record ElementLehrkraft(string Kurz, string Name);
public sealed record ElementRaum(string Kurz, string Name);
public sealed record ElementFach(string Kurz, string Name, string? Farbe);

/// <summary>Stammdaten aus /api/app/elemente.</summary>
public sealed record Elemente(List<ElementKlasse> Klassen, List<ElementLehrkraft> Lehrer, List<ElementRaum> Raeume, List<ElementFach> Faecher,
    List<PlanZeit>? Stunden, List<Ferienzeit>? Ferien)
{
    public static readonly Elemente Leer = new(new(), new(), new(), new(), new(), new());
}

/// <summary>Angemeldeter Benutzer aus /api/app/sitzung.</summary>
public sealed record Sitzungsbenutzer(int Id, string Login, string Anzeigename, string Rolle, string Rollenname, string Element, bool Kollegium, bool Admin, string Typ);

// ---------------------------------------------------------------------------------------------
// Kachel im Format der Stundenplan-Oberfläche (app.js). Nicht gesetzte Felder werden weggelassen.
// ---------------------------------------------------------------------------------------------

public sealed record KoppEintrag(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("state")] string Zustand,
    [property: JsonPropertyName("for")] string? Fuer = null,
    /// <summary>Offene Vertretung (noch keine Lehrkraft eingeteilt): Kürzel durchgestrichen in der grünen Plakette.</summary>
    [property: JsonPropertyName("open")] bool? Offen = null);

public sealed record KlassenEintrag(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string Zustand = "ok");

/// <summary>
/// Eine Kachel im Raster. blockType: normal, change, cancelled, klausur, event, absence (Veranstaltung),
/// holiday, standby (Bereitschaft), supervision (Pausenaufsicht).
/// </summary>
public sealed class Kachel
{
    [JsonPropertyName("lesson")] public int Stunde { get; set; }
    [JsonPropertyName("span")] public int Dauer { get; set; } = 1;
    [JsonPropertyName("date")] public string Datum { get; set; } = "";
    [JsonPropertyName("class")] public string Klasse { get; set; } = "";
    [JsonPropertyName("classOrig")] public string KlasseAlt { get; set; } = "";
    [JsonPropertyName("teacher")] public string Lehrkraft { get; set; } = "";
    [JsonPropertyName("teacherOrig")] public string LehrkraftAlt { get; set; } = "";
    [JsonPropertyName("room")] public string Raum { get; set; } = "";
    [JsonPropertyName("roomOrig")] public string RaumAlt { get; set; } = "";
    /// <summary>Neu hinzugekommene bzw. gebuchte Räume (nur diese stehen hervorgehoben, die übrigen als eigene Labels daneben).</summary>
    [JsonPropertyName("roomNew")] public string? RaumNeu { get; set; }
    [JsonPropertyName("subject")] public string Fach { get; set; } = "";
    [JsonPropertyName("subjectOrig")] public string FachAlt { get; set; } = "";
    [JsonPropertyName("subjectLong")] public string? FachLang { get; set; }
    [JsonPropertyName("blockType")] public string Art { get; set; } = "normal";
    [JsonPropertyName("changed")] public List<string> Geaendert { get; set; } = new();
    [JsonPropertyName("info")] public string Info { get; set; } = "";
    [JsonPropertyName("type")] public string Typ { get; set; } = "";
    [JsonPropertyName("color")] public string? Farbe { get; set; }
    [JsonPropertyName("openSub")] public bool Offen { get; set; }
    /// <summary>
    /// Offene Vertretung einer abwesenden Lehrkraft (Status offen/abwesend oder „+++“): Es ist noch keine Lehrkraft eingeteilt,
    /// die bisherige steht immer durchgestrichen in der grünen Plakette – auf Karten und im Kopf der Stundendetails.
    /// </summary>
    [JsonPropertyName("teacherOpen")] public bool? LehrkraftOffen { get; set; }
    /// <summary>Zusatzstunde (zusätzlicher Unterricht): Plakette „Zusätzliche Unterrichtsstunde“ in den Details.</summary>
    [JsonPropertyName("extra")] public bool? Zusatz { get; set; }
    /// <summary>Klasse abwesend (Freisetzung): Klasse durchgestrichen, Kachel grün; im Lehrerplan wie eine abgegebene Stunde.</summary>
    [JsonPropertyName("classAbsent")] public bool? KlasseAbwesend { get; set; }
    /// <summary>Offene Raumvertretung: noch kein Raum eingeteilt, bisheriger Raum durchgestrichen.</summary>
    [JsonPropertyName("openRoom")] public bool? RaumOffen { get; set; }
    [JsonPropertyName("overlay")] public bool? Feld { get; set; }
    [JsonPropertyName("fullday")] public bool? Ganztaegig { get; set; }
    [JsonPropertyName("holiday")] public bool? Ferien { get; set; }
    [JsonPropertyName("holidayName")] public string? FerienName { get; set; }
    [JsonPropertyName("holidayRange")] public string? FerienZeitraum { get; set; }
    [JsonPropertyName("from")] public string? Von { get; set; }
    [JsonPropertyName("to")] public string? Bis { get; set; }
    [JsonPropertyName("_supervision")] public bool? Aufsicht { get; set; }
    [JsonPropertyName("_pause")] public bool? Pause { get; set; }
    [JsonPropertyName("_pausePre")] public bool? VorErsterStunde { get; set; }
    [JsonPropertyName("_supSub")] public bool? AufsichtUebernommen { get; set; }
    [JsonPropertyName("_supAbsent")] public bool? AufsichtAbgegeben { get; set; }
    /// <summary>Bereitschaft einer abwesenden Lehrkraft: Eckklammern, Kürzel durchgestrichen (kein Entfall).</summary>
    [JsonPropertyName("standbyAbsent")] public bool? BereitschaftAbwesend { get; set; }
    /// <summary>Lehrkraft abwesend (eigene Stunde wird von anderen gehalten): Eckklammern statt Entfall.</summary>
    [JsonPropertyName("teacherAbsent")] public bool? LehrerAbwesend { get; set; }
    [JsonPropertyName("teacherList")] public List<KoppEintrag>? Kopplung { get; set; }
    [JsonPropertyName("classList")] public List<KlassenEintrag>? Klassenliste { get; set; }
    /// <summary>Unterricht in UntPlanWeb – Schlüssel fürs Klassenbuch (auch als entryId, wie in der Vorlage).</summary>
    [JsonPropertyName("entryId")] public int? Eintrag { get; set; }
    [JsonPropertyName("unterricht")] public int? Unterricht { get; set; }
    [JsonPropertyName("hasHw")] public bool? Hausaufgabe { get; set; }
    /// <summary>Notizen zur Stunde (Info-Symbol unten rechts auf der Kachel).</summary>
    [JsonPropertyName("hasNote")] public bool? Notiz { get; set; }
    [JsonPropertyName("noteStudents")] public string? NotizSchueler { get; set; }
    [JsonPropertyName("noteTeachers")] public string? NotizLehrer { get; set; }

    public Kachel Kopie()
    {
        var k = (Kachel)MemberwiseClone();
        k.Geaendert = new List<string>(Geaendert);
        return k;
    }
}
