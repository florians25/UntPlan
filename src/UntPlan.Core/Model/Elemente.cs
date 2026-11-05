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
    /// <summary>(m) markiert – Druck und Serienänderung lassen sich auf markierte Elemente beschränken.</summary>
    public bool Markiert { get; set; }
    /// <summary>(N) nicht drucken – für das Element werden keine Stundenpläne gedruckt (Fächer: auch nicht im Vertretungsplan).</summary>
    public bool NichtDrucken { get; set; }
    /// <summary>Name in einer anderen Schule (MultiUser, externe Elemente).</summary>
    public string ExtName { get; set; } = "";
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
    /// <summary>
    /// Disloz. – Dislozierung (Außenstelle): 1–9 mit Zeitstaffelung (Wegzeit in Stunden), A–E ohne Zeitstaffelung, leer = Hauptgebäude.
    /// Wechselt eine Lehrkraft oder Klasse ohne freie Stunde zwischen verschiedenen Dislozierungen, ist das ein Verstoß.
    /// </summary>
    public string Dislozierung { get; set; } = "";

    /// <summary>Normalisierte Dislozierungskennung (1–9, A–E) oder leer.</summary>
    public static string DislozierungNormal(string? s)
    {
        var t = (s ?? "").Trim().ToUpperInvariant();
        if (t.Length != 1) return "";
        var c = t[0];
        return c is >= '1' and <= '9' or >= 'A' and <= 'E' ? t : "";
    }

    /// <summary>
    /// Benötigte freie Stunden zwischen zwei Räumen: 0 = gleiche Dislozierung; Ziffern mit Zeitstaffelung = Differenz der Ziffern
    /// (Hauptgebäude zählt als 0), Buchstaben bzw. Buchstabe ↔ anderes Gebäude = 1.
    /// </summary>
    public static int Wegzeit(string? a, string? b)
    {
        a = DislozierungNormal(a);
        b = DislozierungNormal(b);
        if (a == b) return 0;
        int Ziffer(string x) => x.Length == 0 ? 0 : char.IsDigit(x[0]) ? x[0] - '0' : -1;
        var za = Ziffer(a);
        var zb = Ziffer(b);
        if (za >= 0 && zb >= 0) return Math.Max(1, Math.Abs(za - zb));
        return 1;
    }

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
    /// <summary>max. verschiedene Unt./Tag (0 = keine Vorgabe).</summary>
    public int MaxVerschiedeneProTag { get; set; }
    /// <summary>Hauptklasse (typengetrennte Klassenteile: gemeinsamer Plan).</summary>
    public int? HauptklasseId { get; set; }
    /// <summary>Stundentafel (Jahrgang der Stundentafel, Unterrichtsplanung und Wertrechnung); leer = Jahrgangsstufe der Klasse.</summary>
    public string Stundentafel { get; set; } = "";
    /// <summary>Vorjahresname der Klasse (Vorjahreslehrer, begleitender Aufstieg).</summary>
    public string Vorjahresname { get; set; } = "";
    /// <summary>(P) Hohlstunden zulässig (Pseudoklassen, Klassenteile).</summary>
    public bool HohlstundenZulaessig { get; set; }
    /// <summary>(F) Unterrichtstage nicht in Folge.</summary>
    public bool TageNichtInFolge { get; set; }
    /// <summary>(2) 2 Tage hintereinander: die Unterrichtstage müssen unmittelbar aufeinanderfolgen.</summary>
    public bool ZweiTageHintereinander { get; set; }
    /// <summary>(H) 1 Halbtag/Tag belegen: nicht vormittags UND nachmittags am selben Tag.</summary>
    public bool EinHalbtag { get; set; }
    /// <summary>(Y) Belegung beibehalten: vor der ersten und nach der letzten belegten Stunde des Halbtages gesperrt.</summary>
    public bool BelegungBeibehalten { get; set; }
    /// <summary>Klassenzeitraster der Stundenplanung (Schlüssel Tag*100+Stunde): Doppelstunden, Einzelstunden, Letzte Stunde „Nein“.</summary>
    public Dictionary<int, KlassenzeitrasterArt> Klassenzeitraster { get; set; } = new();

    [JsonIgnore] public int Schuelerzahl => SchuelerMaennlich + SchuelerWeiblich;

    /// <summary>Kennzeichen-Zeichenfolge (P, F, 2, H, Y).</summary>
    [JsonIgnore] public string Kennzeichen => string.Concat(
        HohlstundenZulaessig ? "P" : "", TageNichtInFolge ? "F" : "", ZweiTageHintereinander ? "2" : "", EinHalbtag ? "H" : "", BelegungBeibehalten ? "Y" : "");
}

/// <summary>Einstellung einer Stunde im Klassenzeitraster der Stundenplanung (kombinierbar).</summary>
[Flags]
public enum KlassenzeitrasterArt
{
    Keine = 0,
    /// <summary>Verplanung: bevorzugt Doppelstunden.</summary>
    Doppelstunden = 1,
    /// <summary>Verplanung: bevorzugt Einzelstunden.</summary>
    Einzelstunden = 2,
    /// <summary>Letzte Stunde „Nein“: der Unterricht der Klasse darf nach dieser Stunde nicht enden.</summary>
    NichtLetzte = 4,
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
    public DateOnly? Eintritt { get; set; }
    public DateOnly? Austritt { get; set; }
    /// <summary>Datum ab Status neu.</summary>
    public DateOnly? StatusNeuAb { get; set; }
    /// <summary>Neuer Status (gilt ab <see cref="StatusNeuAb"/>).</summary>
    public string StatusNeu { get; set; } = "";
    /// <summary>Stundensatz (NRW).</summary>
    public decimal Stundensatz { get; set; }
    public string Zusatztext1 { get; set; } = "";
    public string Zusatztext2 { get; set; } = "";
    /// <summary>Maximale Stundenzahl in Folge (0 = keine Vorgabe): danach wünscht die Lehrkraft eine Hohlstunde.</summary>
    public int MaxStundenFolge { get; set; }
    /// <summary>Max. Verweildauer pro Schultag in Stunden (verplante Stunden, Hohlstunden und Mittagspause), 0 = keine Vorgabe.</summary>
    public int MaxVerweildauer { get; set; }
    /// <summary>Tagesrand-Pause: freie Stunden zwischen letzter Stunde eines Tages und erster des Folgetages (0 = keine Vorgabe).</summary>
    public int TagesrandPause { get; set; }
    /// <summary>Lehrer Optimierungs-Kz.: 1–9 (Tausch nur mit gleichem Kz.) bzw. A–Z (Tausch nur mit anderem oder keinem Kz.).</summary>
    public string OptimierungsKz { get; set; } = "";
    /// <summary>(H) 1 Halbtag/Tag belegen.</summary>
    public bool EinHalbtag { get; set; }
    /// <summary>(Y) Belegung beibehalten.</summary>
    public bool BelegungBeibehalten { get; set; }
    /// <summary>(R) Nicht in 1. UND letzter Std.</summary>
    public bool NichtErsteUndLetzte { get; set; }
    /// <summary>besonders wichtig: keine Hohlstunden (A).</summary>
    public bool WichtigHohlstunden { get; set; }
    /// <summary>besonders wichtig: Mittagspause (B).</summary>
    public bool WichtigMittagspause { get; set; }
    /// <summary>besonders wichtig: Max. Stunden je Tag (C).</summary>
    public bool WichtigStundenProTag { get; set; }
    /// <summary>besonders wichtig: Max. Stunden-Folge (D).</summary>
    public bool WichtigStundenFolge { get; set; }

    /// <summary>Status an einem Datum (ab „Datum ab Status neu“ gilt der neue Status).</summary>
    public string StatusAm(DateOnly datum) => StatusNeuAb is DateOnly ab && datum >= ab && StatusNeu.Length > 0 ? StatusNeu : Status;

    /// <summary>
    /// Darf die Optimierung (variable Lehrerzuordnung) Unterricht dieser Lehrkraft mit Unterricht von <paramref name="anderer"/> tauschen?
    /// Ziffer: nur mit gleichem Kennzeichen; Buchstabe: nur mit anderem (oder keinem) Kennzeichen.
    /// </summary>
    public bool TauschErlaubt(Lehrer anderer)
    {
        bool Ok(string a, string b)
        {
            if (a.Length == 0) return true;
            return char.IsDigit(a[0]) ? a == b : a != b;
        }
        var x = OptimierungsKz.Trim().ToUpperInvariant();
        var y = anderer.OptimierungsKz.Trim().ToUpperInvariant();
        return Ok(x, y) && Ok(y, x);
    }

    /// <summary>Kennzeichen-Zeichenfolge wie in der Spalte „Kennzeichen“ (z. B. „HRA“).</summary>
    [JsonIgnore] public string Kennzeichen => string.Concat(
        EinHalbtag ? "H" : "", BelegungBeibehalten ? "Y" : "", NichtErsteUndLetzte ? "R" : "",
        WichtigHohlstunden ? "A" : "", WichtigMittagspause ? "B" : "", WichtigStundenProTag ? "C" : "", WichtigStundenFolge ? "D" : "");

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
    /// <summary>(P) Keine Pausenaufsicht vor bzw. nach diesem Fach.</summary>
    public bool KeinePausenaufsicht { get; set; }
    /// <summary>Doppelstunden-Vorgabe für neue Unterrichte (z. B. Sport 1-1).</summary>
    public Bereich Doppelstunden { get; set; }
    /// <summary>(F) Freifach – bevorzugt am Anfang oder Ende des (Halb-)Tages.</summary>
    public bool Freifach { get; set; }
    /// <summary>(G) nicht in Randstunden – bevorzugt in den mittleren Stunden des Halbtages.</summary>
    public bool NichtInRandstunden { get; set; }
    /// <summary>(D) Doppelstunden einhalten.</summary>
    public bool DoppelstundenEinhalten { get; set; }
    /// <summary>(E) Doppelstunde über *-Pausen erlaubt.</summary>
    public bool DoppelstundeUeberPause { get; set; }
    /// <summary>(S) Sprechstunde (Web).</summary>
    public bool Sprechstunde { get; set; }
    /// <summary>Art des Unterrichtes (statistische Zwecke).</summary>
    public string Art { get; set; } = "";
    /// <summary>Nachmittagsstunden (min-max) je Klasse.</summary>
    public Bereich Nachmittagsstunden { get; set; }
    /// <summary>Wochenstunden (min-max) je Klasse – Prüfung in der Diagnose.</summary>
    public Bereich WochenstundenBereich { get; set; }
    /// <summary>Fachfolge Lehrer: 1–9 = Folgestunden erwünscht, A–F = Folgestunden verhindern.</summary>
    public string FachfolgeLehrer { get; set; } = "";
    /// <summary>Fachfolge Klassen: 1–9 = Folgestunden erwünscht, A–F = Folgestunden verhindern.</summary>
    public string FachfolgeKlassen { get; set; } = "";

    /// <summary>Kennzeichen-Zeichenfolge (H, R, F, 2, G, D, E, P, S, N).</summary>
    [JsonIgnore] public string Kennzeichen => string.Concat(
        Hauptfach ? "H" : "", Randstunde ? "R" : "", Freifach ? "F" : "", MehrmalsProTag ? "2" : "", NichtInRandstunden ? "G" : "",
        DoppelstundenEinhalten ? "D" : "", DoppelstundeUeberPause ? "E" : "", KeinePausenaufsicht ? "P" : "", Sprechstunde ? "S" : "", KeinNachmittag ? "N" : "");
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
