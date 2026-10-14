using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

public enum ElementTyp
{
    Klasse,
    Lehrer,
    Raum,
    Fach,
    Raumgruppe,
    Gang,
    Abteilung,
    Abwesenheitsgrund,
    Schueler,
    Unterricht,
}

/// <summary>Gemeinsame Eigenschaften aller Stammdaten-Elemente (Kurzname, Langname, Zeitwünsche, …).</summary>
public abstract class Element
{
    public int Id { get; set; }
    /// <summary>Eindeutiger Kurzname (z. B. "TH1", "1a", "Gauss").</summary>
    public string Kurzname { get; set; } = "";
    public string Langname { get; set; } = "";
    public string Text { get; set; } = "";
    public string Beschreibung { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Statistik { get; set; } = "";
    /// <summary>Hintergrundfarbe als ARGB (0 = keine).</summary>
    public int Hintergrund { get; set; }
    public int Vordergrund { get; set; }
    public int? AbteilungId { get; set; }
    /// <summary>(X) fixiert – die Optimierung verändert die Stunden dieses Elements nicht.</summary>
    public bool Fix { get; set; }
    /// <summary>(I) ignorieren – Element wird bei Planung und Diagnose nicht berücksichtigt.</summary>
    public bool Ignorieren { get; set; }
    public Zeitwuensche Zeitwuensche { get; set; } = new();
    public List<UnbestimmterZeitwunsch> UnbestimmteZeitwuensche { get; set; } = new();
    /// <summary>Reihenfolge in Listen (Umreihen per Drag & Drop).</summary>
    public int Sortierung { get; set; }

    [JsonIgnore] public abstract ElementTyp Typ { get; }

    public override string ToString() => Kurzname;
}

public sealed class Raum : Element
{
    public override ElementTyp Typ => ElementTyp.Raum;
    /// <summary>Wird der Raum belegt, versucht die Optimierung den Ausweichraum (Ausweichraum-Ring).</summary>
    public int? AusweichraumId { get; set; }
    /// <summary>0 = nicht wichtig … 4 = extrem wichtig (Unterricht nur sinnvoll, wenn Raum oder Ausweichraum frei).</summary>
    public int Raumgewicht { get; set; } = 2;
    public int Kapazitaet { get; set; }
    /// <summary>Gang / Aufsichtsbereich, dem der Raum zugeordnet ist (Pausenaufsicht).</summary>
    public int? GangId { get; set; }
    public string Stockwerk { get; set; } = "";
    public string Raumart { get; set; } = "Klassenraum";
    /// <summary>In UntPlanWeb von Lehrkräften buchbar.</summary>
    public bool Buchbar { get; set; }
    /// <summary>Nächstgelegene Gänge (Pausenaufsicht), Komma-Liste mit Platzhaltern * und ?.</summary>
    public string Gaenge { get; set; } = "";

    public static string GewichtText(int g) => g switch
    {
        0 => "Nicht wichtig",
        1 => "Eher unwichtig",
        2 => "Mäßig wichtig",
        3 => "Sehr wichtig",
        _ => "Extrem wichtig",
    };
}

/// <summary>Raumgruppe (z. B. für Wanderklassen oder "EDV-Säle") – im Unterricht wie ein Raum verwendbar.</summary>
public sealed class Raumgruppe : Element
{
    public override ElementTyp Typ => ElementTyp.Raumgruppe;
    public List<int> RaumIds { get; set; } = new();
}

public sealed class Klasse : Element
{
    public override ElementTyp Typ => ElementTyp.Klasse;
    /// <summary>Klassenraum (Stammraum) – wird beim Unterricht automatisch übernommen.</summary>
    public int? StammraumId { get; set; }
    public int HauptfaecherProTag { get; set; }
    /// <summary>Max. Anzahl aufeinanderfolgender Hauptfachstunden.</summary>
    public int HauptfaecherFolge { get; set; }
    public Bereich Mittagspause { get; set; }
    public Bereich StundenProTag { get; set; }
    public List<int> KlassenlehrerIds { get; set; } = new();
    public string Jahrgang { get; set; } = "";
    public string Stufe { get; set; } = "";
    public int SchuelerMaennlich { get; set; }
    public int SchuelerWeiblich { get; set; }
    public string Klassengruppe { get; set; } = "";
    public DateOnly? VonDatum { get; set; }
    public DateOnly? BisDatum { get; set; }
    public int MaxNachmittage { get; set; }
    /// <summary>Klassenzeitraster der Vertretungsplanung (Schlüssel Tag*100+Stunde), überschreibt den globalen.</summary>
    public Dictionary<int, VertretungsRegel> VertretungsZeitraster { get; set; } = new();

    [JsonIgnore] public int Schuelerzahl => SchuelerMaennlich + SchuelerWeiblich;
}

public sealed class Ermaessigung
{
    public string Grund { get; set; } = "";
    public decimal Wert { get; set; }
    public string Text { get; set; } = "";
}

public sealed class Lehrer : Element
{
    public override ElementTyp Typ => ElementTyp.Lehrer;
    public string Vorname { get; set; } = "";
    public string Titel { get; set; } = "";
    public string Personalnummer { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telefon { get; set; } = "";
    public DateOnly? Geburtsdatum { get; set; }
    public string Geschlecht { get; set; } = "";
    public int? StammraumId { get; set; }
    /// <summary>Erlaubte Hohlstunden (Fenster-/Springstunden) pro Woche, z. B. 0-1.</summary>
    public Bereich Hohlstunden { get; set; }
    /// <summary>Stunden pro Tag (min-max), wenn der Lehrer an einem Tag unterrichtet, z. B. 3-7.</summary>
    public Bereich StundenProTag { get; set; }
    public Bereich Mittagspause { get; set; }
    public int MaxNachmittage { get; set; }
    /// <summary>Wochensoll (Unterrichtsverpflichtung, NRW: Pflichtstunden je Schulform).</summary>
    public decimal Wochensoll { get; set; }
    public List<Ermaessigung> Ermaessigungen { get; set; } = new();
    /// <summary>Lehrbefähigung (Fächer).</summary>
    public List<int> FaecherIds { get; set; } = new();
    public string Status { get; set; } = "";
    // Vertretungsplanung
    public int VertretungenMaxWoche { get; set; }
    public int VertretungenMaxMonat { get; set; }
    public bool KeineVertretung { get; set; }
    // Pausenaufsichten
    public int AufsichtenMin { get; set; }
    public int AufsichtenMax { get; set; }
    public bool KeineAufsicht { get; set; }
    /// <summary>Wertkorrektur / Übertrag aus Vorjahr (Wertrechnung).</summary>
    public decimal Wertkorrektur { get; set; }
    /// <summary>Sperrvermerk 0–9 für Vertretungen (9 = absolutes Vertretungsverbot).</summary>
    public int Sperrvermerk { get; set; }
    /// <summary>Maximale Pausenaufsichtsminuten pro Woche (0 = keine Aufsicht).</summary>
    public int PausenaufsichtMaxMinuten { get; set; } = 60;
    public int BereitschaftenMax { get; set; } = 2;
    public List<Wertkorrektur> Wertkorrekturen { get; set; } = new();
    /// <summary>Name in einer anderen Schule (MultiUser, externe Elemente).</summary>
    public string ExtName { get; set; } = "";
    public DateOnly? Eintritt { get; set; }
    public DateOnly? Austritt { get; set; }

    [JsonIgnore] public decimal SummeErmaessigungen => Ermaessigungen.Sum(e => e.Wert);
    [JsonIgnore] public decimal Soll => Wochensoll - SummeErmaessigungen;
    [JsonIgnore] public string VollerName => string.Join(' ', new[] { Titel, Vorname, Langname }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed class Fach : Element
{
    public override ElementTyp Typ => ElementTyp.Fach;
    /// <summary>Fachraum (wird bei neuem Unterricht automatisch eingetragen).</summary>
    public int? FachraumId { get; set; }
    public int? FachraumgruppeId { get; set; }
    public bool Hauptfach { get; set; }
    public string Fachgruppe { get; set; } = "";
    /// <summary>Werte-Faktor für die Wertrechnung (z. B. 1,0).</summary>
    public decimal WerteFaktor { get; set; } = 1m;
    /// <summary>(M) Fach darf mehrmals am Tag stattfinden.</summary>
    public bool MehrmalsProTag { get; set; }
    /// <summary>(R) Fach bevorzugt in Randstunden (z. B. Religion).</summary>
    public bool Randstunde { get; set; }
    /// <summary>(N) kein Nachmittagsunterricht.</summary>
    public bool KeinNachmittag { get; set; }
    /// <summary>(B) Bereitschaftsstunde – Unterricht in diesem Fach ist eine Vertretungsbereitschaft.</summary>
    public bool Bereitschaft { get; set; }
    /// <summary>Bereitschaftsstunden zählen zum Deputat (Ist-Wert).</summary>
    public bool BereitschaftImDeputat { get; set; }
    /// <summary>Entfälle dieses Faches zählen nicht.</summary>
    public bool EntfallNichtZaehlen { get; set; }
    /// <summary>Unterricht dieses Faches wird nicht vertreten (automatisch Entfall).</summary>
    public bool KeineVertretung { get; set; }
    public bool NichtDrucken { get; set; }
    /// <summary>(P) Keine Pausenaufsicht vor bzw. nach diesem Fach.</summary>
    public bool KeinePausenaufsicht { get; set; }
    /// <summary>Doppelstunden-Vorgabe für neue Unterrichte (z. B. Sport 1-1).</summary>
    public Bereich Doppelstunden { get; set; }
}

/// <summary>Gang bzw. Pausenaufsichtsbereich.</summary>
public sealed class Gang : Element
{
    public override ElementTyp Typ => ElementTyp.Gang;
    /// <summary>Anzahl Aufsichten je Pause in diesem Bereich.</summary>
    public int Bedarf { get; set; } = 1;
    public bool NurMaennlich { get; set; }
    public bool NurWeiblich { get; set; }
}

public sealed class Abteilung : Element
{
    public override ElementTyp Typ => ElementTyp.Abteilung;
    public int? LeiterId { get; set; }
}

public sealed class Abwesenheitsgrund : Element
{
    public override ElementTyp Typ => ElementTyp.Abwesenheitsgrund;
    /// <summary>Wird in der Statistik gezählt.</summary>
    public bool Statistikrelevant { get; set; } = true;
    /// <summary>Grund nicht veröffentlichen (z. B. Krankheit – Datenschutz).</summary>
    public bool Vertraulich { get; set; }
    /// <summary>Entfälle mit diesem Grund zählen negativ für den Lehrer.</summary>
    public bool EntfallZaehlen { get; set; }
    /// <summary>Freisetzungen (abwesende Klasse) mit diesem Grund zählen nicht negativ.</summary>
    public bool FreisetzungNichtZaehlen { get; set; }
    /// <summary>Nicht im Absenzkopf des Vertretungsplans drucken.</summary>
    public bool NichtImAbsenzkopf { get; set; }
}

public sealed class Schueler : Element
{
    public override ElementTyp Typ => ElementTyp.Schueler;
    public string Vorname { get; set; } = "";
    public int? KlasseId { get; set; }
    public string Geschlecht { get; set; } = "";
    /// <summary>Gewählte Kurse (Unterrichts-Nummern) – Kursplanung.</summary>
    public List<int> KurswahlIds { get; set; } = new();
}
