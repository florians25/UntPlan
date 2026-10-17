using System.Text.Json.Serialization;

namespace UntPlanMobile.Dienste;

// ---------------------------------------------------------------------------------------------
// Antworten von UntPlanWeb (/api/app/plan, /api/app/elemente). Die Felder entsprechen dem JSON,
// das UntPlanWeb liefert; gelesen wird ohne Rücksicht auf Groß- und Kleinschreibung.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Angezeigter Wert (Klasse, Lehrkraft, Raum); Alt = ursprünglicher Wert, Neu = geändert,
/// Plus = hinzugekommen (neu, ohne alten Wert – z. B. zusätzlicher Raum): grünes Label mit „+“ davor.
/// </summary>
public sealed record PlanWert(string K, string? Alt = null, bool Neu = false, bool Plus = false);

/// <summary>Eine Seite einer Verlegung (ursprüngliche Stunde bzw. Zielstunde). Ende kann leer sein.</summary>
public sealed record PlanVerlegung(string Datum, string Beginn, string Ende = "", int Stunde = 0, int BisStunde = 0);

/// <summary>Stundenkarte aus UntPlanWeb (Status regulaer, geaendert, offen, entfall, abwesend, klausur, bereitschaft, buchung, vorgemerkt, veranstaltung).</summary>
public sealed record PlanKarte(
    string Datum, int Von, int Bis, string Beginn, string Ende, string Status,
    string Fach, string? FachAlt, string FachName, string Farbe,
    List<PlanWert> Klassen, List<PlanWert> Lehrer, List<PlanWert> Raeume,
    string Art, string Text, int? Vertretung, int? Unterricht, string Gruppe = "",
    string? NotizSchueler = null, string? NotizLehrer = null, bool HausaufgabeFaellig = false,
    bool LehrerAbwesend = false, bool Bereitschaft = false,
    // Koppelklassen (nur im Klassenplan): anwesend {k:"INF13"}, fehlend {k:"", alt:"INF13", neu:true}; klassen[] bleibt die eigene Klasse
    List<PlanWert>? Mitklassen = null,
    // offene Vertretung (noch keine Lehrkraft eingeteilt) – auch bei Status „abwesend“ im eigenen Lehrerplan
    bool VertretungOffen = false,
    // „Findet online statt“ mit Link zum Online-Unterricht (Klassenbuch der Stunde)
    bool Online = false, string? OnlineLink = null,
    // zusätzliche Unterrichtsstunde (nicht im Regelplan): Badge „Zusätzl. Unterrichtsstunde“, nie ein Vertretungstext
    bool Zusaetzlich = false,
    // Verlegung: "von" = ursprüngliche Stunde (Pfeil nach rechts), "nach" = Zielstunde (Pfeil nach links), sonst null
    string? Verlegung = null,
    // Verlegung: ursprüngliche Stunde und Zielstunde (Datum, Beginn, Ende); die Gegenseite kann fehlen
    PlanVerlegung? VerlegungVon = null, PlanVerlegung? VerlegungNach = null,
    // Zeichen oben rechts laut Server (Web); die App zeigt oben rechts nie ein Zeichen und beachtet das Feld nicht
    List<string>? Marker = null);

/// <summary>Pausenaufsicht im Lehrerplan. Status regulaer, vertreten (fällt für mich weg), vertretung (übernommen).</summary>
/// <summary>Pausenaufsicht. Status: regulaer, vertreten, vertretung, entfall (findet nicht statt; Grund z. B. „Abwesenheit“ bzw. Titel der Veranstaltung).</summary>
public sealed record PlanAufsicht(string Datum, int NachStunde, string Beginn, string Ende, string Gang, string GangName, string Status, string Lehrer, string? Alt,
    string? Grund = null);

public sealed record PlanEreignis(string Titel, string Art, int Von, int Bis, bool Ganztaegig, List<string> Klassen, List<string> Lehrer, List<string> Raeume);

public sealed record PlanTag(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, bool Tagesaktuell,
    List<string> Hinweise, List<PlanEreignis> Ereignisse, string? FreiArt = null);

public sealed record PlanZeit(int Nr, string Bezeichnung, string Beginn, string Ende);

/// <summary>
/// Wochenplan aus /api/app/plan. Gesperrt: Die Woche liegt außerhalb der Wochenbegrenzung der Rolle (UntPlanWeb setzt sie durch) –
/// dann ohne Stunden, mit Sperrhinweis.
/// </summary>
public sealed record Wochenplan(string Typ, string Element, string Name, string Montag, string Woche, List<PlanZeit> Zeitraster,
    List<PlanTag> Tage, List<PlanKarte> Karten, List<PlanAufsicht> Aufsichten, bool Gesperrt = false, string? Sperrhinweis = null,
    List<PlanAbwesenheit>? Abwesenheiten = null);

/// <summary>
/// Eigene Abwesenheit an einem Tag im eigenen Plan (/api/app/plan, Feld „abwesenheiten“; ältere Server: fehlt): Uhrzeiten HH:mm,
/// Grund, Art (fehlt, verspaetet, vorzeitig).
/// </summary>
public sealed record PlanAbwesenheit(string Datum, string Von, string Bis, string? Grund = null, string? Art = null);

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
    [property: JsonPropertyName("open")] bool? Offen = null,
    /// <summary>Hinzugekommene Lehrkraft: grünes Label mit „+“ davor.</summary>
    [property: JsonPropertyName("plus")] bool? Plus = null);

public sealed record KlassenEintrag(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string Zustand = "ok",
    /// <summary>Hinzugekommene Klasse: grünes Label mit „+“ davor.</summary>
    [property: JsonPropertyName("plus")] bool? Plus = null);

/// <summary>Eine Seite einer Verlegung im Format der Oberfläche (Datum, Beginn, Ende; Ende kann leer sein).</summary>
public sealed record KachelVerlegung(
    [property: JsonPropertyName("date")] string Datum,
    [property: JsonPropertyName("from")] string Beginn,
    [property: JsonPropertyName("to")] string Ende,
    /// <summary>Anzeige „09.10.2026 | 11:30 - 13:00“ (ohne Ende nur der Beginn).</summary>
    [property: JsonPropertyName("text")] string Text = "");

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
    /// <summary>Entfernte Räume neben verbleibenden (durchgestrichen in der grünen Plakette; die verbleibenden ohne Label).</summary>
    [JsonPropertyName("roomRemoved")] public string? RaumEntfernt { get; set; }
    [JsonPropertyName("subject")] public string Fach { get; set; } = "";
    [JsonPropertyName("subjectOrig")] public string FachAlt { get; set; } = "";
    [JsonPropertyName("subjectLong")] public string? FachLang { get; set; }
    /// <summary>Veranstaltung: eingetragenes Fach (subject trägt dort die Bezeichnung). Nur gesetzt, wenn es eines gibt –
    /// die Stundendetails zeigen oben nur Fach und Klassen, die Bezeichnung steht unten in „Information zur Stunde“.</summary>
    [JsonPropertyName("eventSubject")] public string? VeranstaltungFach { get; set; }
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
    /// <summary>Zusatzstunde (zusätzlicher Unterricht): Plakette „Zusätzl. Unterrichtsstunde“ in den Details, kein Vertretungstext.</summary>
    [JsonPropertyName("extra")] public bool? Zusatz { get; set; }
    /// <summary>Verlegung: "von" = ursprüngliche Stunde (Zeichen mit Pfeil nach rechts), "nach" = Zielstunde (Pfeil nach links).</summary>
    [JsonPropertyName("moved")] public string? Verlegung { get; set; }
    /// <summary>Verlegung: ursprüngliche Stunde (von) und Zielstunde (nach); fehlt die Gegenseite, nur das Zeichen.</summary>
    [JsonPropertyName("movedFrom")] public KachelVerlegung? VerlegungVon { get; set; }
    [JsonPropertyName("movedTo")] public KachelVerlegung? VerlegungNach { get; set; }
    /// <summary>Verlegung als Text, immer Ursprung → Ziel: „09.10.2026 | 11:30 - 13:00 → 13.10.2026 | 12:15 - 13:00“.</summary>
    [JsonPropertyName("movedText")] public string? VerlegungText { get; set; }
    /// <summary>Hinzugekommene Werte (ohne alten Wert): grünes Label mit „+“ davor (Komma-Listen).</summary>
    [JsonPropertyName("classPlus")] public string? KlassePlus { get; set; }
    [JsonPropertyName("teacherPlus")] public string? LehrkraftPlus { get; set; }
    [JsonPropertyName("roomPlus")] public string? RaumPlus { get; set; }
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
    /// <summary>Entfallene Pausenaufsicht (Entfall-Stil wie eine entfallene Stunde: rosa, Ort durchgestrichen).</summary>
    [JsonPropertyName("_supCancelled")] public bool? AufsichtEntfall { get; set; }
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
    /// <summary>„Findet online statt“: Videosymbol auf der Kachel; mit Link „Online-Unterricht beitreten“ in den Details.</summary>
    [JsonPropertyName("online")] public bool? Online { get; set; }
    [JsonPropertyName("onlineLink")] public string? OnlineLink { get; set; }

    public Kachel Kopie()
    {
        var k = (Kachel)MemberwiseClone();
        k.Geaendert = new List<string>(Geaendert);
        return k;
    }
}
