namespace UntPlanWeb.Dienste;

/// <summary>Textbausteine der Schulsimulation: Lehrstoff je Fach, Hausaufgaben, Klassenbucheinträge, Krankmeldungen und Entschuldigungen.</summary>
public sealed partial class Schulsimulation
{

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

    // Ausfallarten und Textbausteine der Krankmeldungen: siehe Schulsimulation.Krankmeldungen.cs

}
