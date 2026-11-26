namespace UntPlanWeb.Dienste;

/// <summary>Textbausteine der Schulsimulation: Lehrstoff je Fach, Hausaufgaben, Klassenbucheinträge, Krankmeldungen und Entschuldigungen.</summary>
public sealed partial class Schulsimulation
{
    public static readonly string[] Lehrformen = { "", "", "", "Einzelarbeit", "Partnerarbeit", "Gruppenarbeit", "Unterrichtsgespräch", "Stationenlernen", "Lehrervortrag" };

    public static readonly string[] Hausaufgaben =
    {
        "Arbeitsblatt fertigstellen", "Buch S. {s} Nr. {a} und {b}", "Buch S. {s} Nr. {a}", "Text lesen und die Fragen 1 bis 3 beantworten",
        "Merksätze ins Heft übertragen", "Aufgaben aus der Stunde zu Ende bearbeiten", "Wiederholung für den Test",
        "Kurze Zusammenfassung schreiben", "Material für die nächste Stunde mitbringen", "Recherche zum Referatsthema",
    };

    public static readonly string[] HausaufgabenSprachen =
    {
        "Vokabeln Unit {a} lernen", "Workbook p. {s} ex. {a} and {b}", "Write a short text (about 100 words)", "Vokabeln der Lektion lernen", "Text lesen und unbekannte Wörter notieren",
    };

    /// <summary>Klassenbucheinträge zu einzelnen Schüler*innen bzw. zur Klasse: Art, Text ({n} = Vorname).</summary>
    public static readonly (string Art, string Text, bool Schueler, double Gewicht)[] Vermerke =
    {
        ("Hausaufgabe fehlt", "{n} hat die Hausaufgabe nicht angefertigt.", true, 5),
        ("Hausaufgabe fehlt", "Hausaufgabe nicht vorgelegt, wird nachgereicht.", true, 3),
        ("Material vergessen", "{n} hat das Arbeitsheft vergessen.", true, 4),
        ("Material vergessen", "Sportzeug vergessen.", true, 1),
        ("Material vergessen", "Kein Taschenrechner dabei.", true, 1),
        ("Lob", "{n} hat sehr engagiert mitgearbeitet.", true, 2),
        ("Lob", "Sehr gute Präsentation der Gruppenergebnisse.", true, 1),
        ("Vermerk", "{n} hat den Unterricht wiederholt durch Zwischenrufe gestört.", true, 3),
        ("Vermerk", "{n} hat trotz Ermahnung das Handy benutzt.", true, 2),
        ("Vermerk", "Die Klasse war heute sehr unruhig, Arbeitsauftrag nicht beendet.", false, 1.5),
        ("Vermerk", "Sehr konzentrierte Arbeitsatmosphäre in der Klasse.", false, 1),
        ("Elterngespräch", "Telefonat mit den Eltern von {n} wegen der Mitarbeit.", true, 0.6),
        ("Erzieherische Maßnahme", "{n} muss die versäumte Aufgabe in der Pause nacharbeiten.", true, 0.4),
    };

    /// <summary>Hinweise an die Klasse für die nächste Stunde (je Fach, "" = allgemein).</summary>
    public static readonly Dictionary<string, string[]> Hinweise = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = new[] { "Bitte das Arbeitsheft mitbringen.", "Bitte die Mappe vollständig mitbringen, ich sammle sie ein.", "Bitte das Buch mitbringen." },
        ["M"] = new[] { "Bitte Taschenrechner und Geodreieck mitbringen.", "Morgen kurzer Test zu den letzten Hausaufgaben." },
        ["D"] = new[] { "Bitte die Lektüre mitbringen.", "Bitte bis morgen das nächste Kapitel der Lektüre lesen." },
        ["E"] = new[] { "Bitte das Workbook mitbringen.", "Vocabulary test tomorrow." },
        ["F"] = new[] { "Bitte das Wörterbuch mitbringen." },
        ["EK"] = new[] { "Bitte den Atlas mitbringen." },
        ["SP"] = new[] { "Bei gutem Wetter sind wir draußen, bitte passende Kleidung mitbringen.", "Bitte Hallenschuhe nicht vergessen." },
        ["KU"] = new[] { "Bitte Farbkasten und Pinsel mitbringen." },
        ["BI"] = new[] { "Wir mikroskopieren, bitte pünktlich im Biologieraum sein." },
        ["IF"] = new[] { "Bitte die Zugangsdaten für die Rechner mitbringen." },
    };

    public static readonly string[] Notizen = { "Arbeitsblätter für die nächste Stunde kopieren", "Test nächste Woche angekündigt", "Raum war nicht aufgeschlossen, 5 Minuten Verspätung", "Beamer defekt" };

    // ------------------------------------------------------------------------------------------ Krankmeldungen der Lehrkräfte

    /// <summary>Art einer Abwesenheit der Lehrkraft: Gewicht, Dauer in Schultagen (min, max), Teil des Tages (Arzttermin), Betreff und Text.</summary>
    public sealed record Ausfallart(string Id, double Gewicht, int Min, int Max, bool Teil, string[] Saetze);

    public static readonly Ausfallart[] Ausfallarten =
    {
        new("erkaeltung", 30, 1, 3, false, new[] { "mich hat eine Erkältung erwischt", "ich habe mir eine starke Erkältung eingefangen", "ich liege mit Halsschmerzen und Fieber im Bett", "ich bin stark erkältet und habe kaum Stimme" }),
        new("infekt", 16, 3, 6, false, new[] { "ich habe einen grippalen Infekt", "mein Arzt hat mich wegen eines fieberhaften Infekts krankgeschrieben", "ich bin an einer Grippe erkrankt" }),
        new("magen", 12, 1, 2, false, new[] { "ich habe mir einen Magen-Darm-Infekt eingefangen", "mir geht es seit heute Nachmittag sehr schlecht, vermutlich Magen-Darm" }),
        new("kind", 10, 1, 2, false, new[] { "mein Sohn ist krank und ich muss ihn betreuen", "meine Tochter hat Fieber und kann nicht in die Kita", "ich muss mein krankes Kind betreuen" }),
        new("migraene", 6, 1, 1, false, new[] { "ich habe eine starke Migräne", "ich habe eine heftige Migräneattacke" }),
        new("arzt", 8, 1, 1, true, new[] { "ich habe einen Facharzttermin, der sich nicht verschieben ließ", "ich habe einen dringenden Arzttermin", "ich muss zu einer Untersuchung ins Krankenhaus" }),
        new("ruecken", 5, 2, 5, false, new[] { "ich habe mir den Rücken verrenkt und kann mich kaum bewegen", "ich habe einen Hexenschuss" }),
        new("lang", 3, 10, 30, false, new[] { "ich muss mich leider einer Operation unterziehen", "ich bin für längere Zeit krankgeschrieben", "nach meinem Bandscheibenvorfall bin ich vorerst krankgeschrieben" }),
    };

    public static readonly string[] Anreden = { "Liebes Vertretungsteam,", "Hallo zusammen,", "Liebe Kolleginnen und Kollegen,", "Hallo,", "Liebe Schulleitung,", "Guten Tag," };
    public static readonly string[] AnredenAbend = { "Guten Abend,", "Liebes Vertretungsteam,", "Hallo zusammen," };
    public static readonly string[] AnredenMorgen = { "Guten Morgen,", "Guten Morgen zusammen,", "Hallo," };
    public static readonly string[] Gruesse = { "Viele Grüße", "Liebe Grüße", "Beste Grüße", "Danke und viele Grüße", "Herzliche Grüße", "Viele Grüße und danke fürs Organisieren" };

}
