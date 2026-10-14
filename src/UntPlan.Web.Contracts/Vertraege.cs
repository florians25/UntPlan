using System.Text.Json;
using System.Text.Json.Serialization;

namespace UntPlan.Web.Contracts;

/// <summary>
/// Schnittstelle UntPlan → UntPlanWeb (Schema "untplanweb/1"). Die Desktop-Anwendung ist führend und überträgt ein
/// vollständiges Paket (Stammdaten, Perioden, Regelstundenplan, tagesaktuelle Pläne, Vertretungen, Termine).
/// UntPlanWeb liefert Rückmeldungen (Absenzmeldungen, Nachrichten, Raumbuchungen), die UntPlan übernimmt.
/// Alle Datumsangaben im Format yyyy-MM-dd, Uhrzeiten HH:mm, Bezeichner sind die Kurznamen bzw. stabile Ids.
/// </summary>
public static class UntPlanWebSchema
{
    public const string Schema = "untplanweb/1";

    /// <summary>REST-Endpunkte relativ zur Basisadresse von UntPlanWeb (Authentifizierung: Bearer-API-Schlüssel je Schule).</summary>
    public static class Pfade
    {
        public const string Status = "api/v1/status";
        public static string Paket(string schule) => $"api/v1/schulen/{Uri.EscapeDataString(schule)}/untplanweb";
        public static string Rueckmeldungen(string schule) => $"api/v1/schulen/{Uri.EscapeDataString(schule)}/rueckmeldungen";
        public static string Quittieren(string schule) => $"api/v1/schulen/{Uri.EscapeDataString(schule)}/rueckmeldungen/quittieren";
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

public static class WebTeile
{
    public const string Stammdaten = "stammdaten";
    public const string Unterricht = "unterricht";
    public const string Vertretungen = "vertretungen";

    /// <summary>JSON-Eigenschaften des Pakets je Teil (für das Zusammenführen auf dem Server).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Felder = new Dictionary<string, string[]>
    {
        [Stammdaten] = new[] { "schule", "zeitraster", "elemente", "ferien" },
        [Unterricht] = new[] { "perioden", "pausenaufsichten" },
        [Vertretungen] = new[] { "tage", "vertretungen", "absenzen", "termine", "tagestexte", "von", "bis" },
    };
}

public sealed record WebPaket
{
    public string Schema { get; init; } = UntPlanWebSchema.Schema;
    public DateTime Erzeugt { get; init; }
    /// <summary>Fortlaufende Revision je Schule (UntPlanWeb lehnt ältere Revisionen ab).</summary>
    public int Revision { get; init; }
    public string Programmversion { get; init; } = "";
    public WebSchule Schule { get; init; } = new();
    public WebZeitraster Zeitraster { get; init; } = new();
    public List<WebPeriode> Perioden { get; init; } = new();
    public WebElemente Elemente { get; init; } = new();
    /// <summary>Tagesaktuelle Pläne im übertragenen Zeitraum (Regelplan + Vertretungen aufgelöst).</summary>
    public List<WebTag> Tage { get; init; } = new();
    public List<WebVertretung> Vertretungen { get; init; } = new();
    public List<WebAbsenz> Absenzen { get; init; } = new();
    public List<WebTermin> Termine { get; init; } = new();
    public List<WebTagestext> Tagestexte { get; init; } = new();
    public List<WebFerien> Ferien { get; init; } = new();
    public List<WebAufsicht> Pausenaufsichten { get; init; } = new();
    /// <summary>
    /// Übertragene Teile: "stammdaten" (Schule, Zeitraster, Elemente, Ferien), "unterricht" (Perioden mit Unterricht und
    /// Stundenplan, Pausenaufsichten), "vertretungen" (Tage, Vertretungen, Absenzen, Termine, Tagestexte).
    /// Leer = vollständiges Paket. UntPlanWeb ersetzt nur die übertragenen Teile und behält die übrigen.
    /// </summary>
    public List<string> Teile { get; init; } = new();
    /// <summary>Übertragener Zeitraum der tagesaktuellen Daten.</summary>
    public string Von { get; init; } = "";
    public string Bis { get; init; } = "";
}

public sealed record WebSchule
{
    public string Name { get; init; } = "";
    public string Schulnummer { get; init; } = "";
    public string Schulform { get; init; } = "";
    public string Bundesland { get; init; } = "NRW";
    public string SchuljahrVon { get; init; } = "";
    public string SchuljahrBis { get; init; } = "";
    /// <summary>Anzahl Wochen im Wochenzyklus (1 = jede Woche, 2 = A/B …).</summary>
    public int Wochenperiodizitaet { get; init; } = 1;
}

public sealed record WebZeitraster
{
    public int TageProWoche { get; init; }
    public List<WebStundenzeit> Stunden { get; init; } = new();
    public List<WebPause> Pausen { get; init; } = new();
}

public sealed record WebStundenzeit(int Nr, string Bezeichnung, string Beginn, string Ende);
public sealed record WebPause(int NachStunde, string Bezeichnung, int Minuten);

public sealed record WebElemente
{
    public List<WebKlasse> Klassen { get; init; } = new();
    public List<WebLehrer> Lehrer { get; init; } = new();
    public List<WebRaum> Raeume { get; init; } = new();
    public List<WebElement> Faecher { get; init; } = new();
    public List<WebElement> Gaenge { get; init; } = new();
    /// <summary>Schülerinnen und Schüler (nur wenn in UntPlan freigegeben) – für Klassenbuch und Fehlzeiten.</summary>
    public List<WebSchueler> Schueler { get; init; } = new();
}

public sealed record WebRaum : WebElement
{
    /// <summary>In UntPlanWeb von Lehrkräften buchbar (Stammdaten in UntPlan).</summary>
    public bool Buchbar { get; init; }
    public string Raumart { get; init; } = "";
    public int Kapazitaet { get; init; }
}

public sealed record WebSchueler(int Id, string Vorname, string Name, string Klasse);

public record WebElement
{
    public int Id { get; init; }
    public string Kurz { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Farbe als #RRGGBB (leer = keine).</summary>
    public string Farbe { get; init; } = "";
}

public sealed record WebKlasse : WebElement
{
    public string Jahrgang { get; init; } = "";
    public List<string> Klassenleitung { get; init; } = new();
    public string Stammraum { get; init; } = "";
}

public sealed record WebLehrer : WebElement
{
    public string Vorname { get; init; } = "";
    /// <summary>Nur übertragen, wenn die Schule dies freigibt.</summary>
    public string? Email { get; init; }
    public List<string> Faecher { get; init; } = new();
}

public sealed record WebPeriode
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Von { get; init; } = "";
    public string Bis { get; init; } = "";
    public int? MutterId { get; init; }
    public List<WebUnterricht> Unterricht { get; init; } = new();
    public List<WebPlanstunde> Stundenplan { get; init; } = new();
}

public sealed record WebUnterricht(int Id, int Wochenstunden, List<WebZeile> Zeilen, string Wochen, string Kurs, string Text);
public sealed record WebZeile(string Fach, string Lehrer, List<string> Klassen, string Gruppe);
public sealed record WebPlanstunde(int Unterricht, int Tag, int Stunde, List<string> Raeume);

/// <summary>Ein Tag mit allen Stunden (nach Vertretungen), je Stunde ein Eintrag pro Unterricht/Kopplungszeile.</summary>
public sealed record WebTag
{
    public string Datum { get; init; } = "";
    public string Woche { get; init; } = "";
    public bool Unterrichtstag { get; init; }
    public List<WebStunde> Stunden { get; init; } = new();
}

public sealed record WebStunde
{
    public int Stunde { get; init; }
    public int? Unterricht { get; init; }
    public string Fach { get; init; } = "";
    public List<string> Klassen { get; init; } = new();
    public List<string> Lehrer { get; init; } = new();
    public List<string> Raeume { get; init; } = new();
    /// <summary>regulaer, vertreten, offen, entfall, zusaetzlich</summary>
    public string Status { get; init; } = "regulaer";
    public int? Vertretung { get; init; }
    public string Text { get; init; } = "";
}

public sealed record WebVertretung
{
    public int Id { get; init; }
    public string Datum { get; init; } = "";
    public int Stunde { get; init; }
    public string Art { get; init; } = "";
    public List<string> Klassen { get; init; } = new();
    public string Lehrer { get; init; } = "";
    public string Vertreter { get; init; } = "";
    public string Fach { get; init; } = "";
    public string NeuesFach { get; init; } = "";
    public string Raum { get; init; } = "";
    public string NeuerRaum { get; init; } = "";
    public string VertrVon { get; init; } = "";
    public string Nach { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>Nur in der Lehrerversion anzeigen (z. B. Pausenaufsichten).</summary>
    public bool NurLehrer { get; init; }
}

public sealed record WebAbsenz
{
    /// <summary>lehrer, klasse, raum</summary>
    public string Art { get; init; } = "";
    public string Element { get; init; } = "";
    public string Von { get; init; } = "";
    public string Bis { get; init; } = "";
    public int StundeVon { get; init; }
    public int StundeBis { get; init; }
    /// <summary>Absenzgrund – nur wenn nicht vertraulich und freigegeben.</summary>
    public string? Grund { get; init; }
}

public sealed record WebTermin
{
    /// <summary>veranstaltung, klausur, sondereinsatz</summary>
    public string Art { get; init; } = "";
    public string Titel { get; init; } = "";
    public string Von { get; init; } = "";
    public string Bis { get; init; } = "";
    public int StundeVon { get; init; }
    public int StundeBis { get; init; }
    public List<string> Klassen { get; init; } = new();
    public List<string> Lehrer { get; init; } = new();
    public List<string> Raeume { get; init; } = new();
}

public sealed record WebTagestext(string Datum, int GueltigTage, string Text, string Fuer, List<string> Elemente);
public sealed record WebFerien(string Name, string Von, string Bis, bool Feiertag);
public sealed record WebAufsicht(string Gang, int Tag, int NachStunde, string Lehrer, string Wochen);

/// <summary>Rückmeldung aus UntPlanWeb an UntPlan.</summary>
public sealed record WebRueckmeldung
{
    public string Id { get; init; } = "";
    /// <summary>absenz (Krankmeldung einer Lehrkraft), nachricht, raumbuchung, klausur</summary>
    public string Typ { get; init; } = "";
    public DateTime Erstellt { get; init; }
    public string Absender { get; init; } = "";
    public string? Lehrer { get; init; }
    public string? Von { get; init; }
    public string? Bis { get; init; }
    public int? StundeVon { get; init; }
    public int? StundeBis { get; init; }
    public string? Raum { get; init; }
    public string? Grund { get; init; }
    public string? Text { get; init; }
    /// <summary>Titel (Klausur: Name, Raumbuchung: Anlass).</summary>
    public string? Titel { get; init; }
    /// <summary>Beteiligte Klassen (Kurznamen), z. B. bei Klausur oder Raumbuchung.</summary>
    public List<string> Klassen { get; init; } = new();
    /// <summary>Räume (Kurznamen); bei einer Raumbuchung genügt Raum.</summary>
    public List<string> Raeume { get; init; } = new();
    /// <summary>Weitere Lehrkräfte (z. B. Klausuraufsichten).</summary>
    public List<string> WeitereLehrer { get; init; } = new();
}

/// <summary>Arten der Rückmeldungen.</summary>
public static class WebRueckmeldungsTypen
{
    public const string Absenz = "absenz";
    public const string Nachricht = "nachricht";
    public const string Raumbuchung = "raumbuchung";
    public const string Klausur = "klausur";
}

public sealed record WebAntwort(bool Ok, int Revision, string? Meldung);
