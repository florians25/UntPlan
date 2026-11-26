namespace UntPlanWeb.Dienste;

/// <summary>Textbausteine der Schulsimulation: Lehrstoff je Fach, Hausaufgaben, Klassenbucheinträge, Krankmeldungen und Entschuldigungen.</summary>
public sealed partial class Schulsimulation
{
    /// <summary>Lehrstoff je Fach (Kurzname): Sekundarstufe I und Oberstufe. Unbekannte Fächer: allgemeine Themen.</summary>
    public static readonly Dictionary<string, (string[] SekI, string[] SekII)> Themen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["D"] = (new[]
        {
            "Kurzgeschichten: Merkmale und Aufbau", "Inhaltsangabe zu einer Kurzgeschichte", "Satzglieder bestimmen", "Kommasetzung bei Nebensätzen",
            "Argumentieren: These, Argument, Beispiel", "Erörterung: Gliederung erstellen", "Balladen: Der Zauberlehrling", "Sachtexte erschließen",
            "Rechtschreibung: das und dass", "Jugendbuch: Lektüre und Figurenkonstellation", "Gedichtanalyse: Metrum und Reim", "Bericht und Beschreibung",
            "Zeitformen des Verbs", "Diskussion: Handyverbot an Schulen", "Lesetagebuch besprechen", "Wortarten wiederholen",
        }, new[]
        {
            "Analyse eines Sachtextes", "Faust I: Gelehrtentragödie", "Lyrik der Romantik", "Erörterung literarischer Texte",
            "Kafka: Die Verwandlung", "Sprachwandel und Jugendsprache", "Drama: Aufbau nach Freytag", "Klausurvorbereitung: Gedichtvergleich",
            "Medien und Manipulation", "Epochenüberblick Aufklärung", "Szenenanalyse", "Besprechung der Klausur",
        }),
        ["M"] = (new[]
        {
            "Brüche addieren und subtrahieren", "Dezimalzahlen runden", "Prozentrechnung: Grundwert, Prozentwert", "Lineare Funktionen: Steigung",
            "Gleichungen mit einer Variablen", "Flächeninhalt von Dreiecken", "Satz des Pythagoras", "Terme vereinfachen",
            "Zuordnungen: proportional und antiproportional", "Kreisumfang und Kreisfläche", "Binomische Formeln", "Lineare Gleichungssysteme",
            "Quadratische Funktionen: Scheitelpunkt", "Wahrscheinlichkeit: Baumdiagramme", "Volumen von Prismen", "Übungsstunde zur Klassenarbeit",
        }, new[]
        {
            "Ableitungsregeln", "Extrempunkte und Wendepunkte", "Kurvendiskussion", "Integralrechnung: Stammfunktionen",
            "Exponentialfunktionen und Wachstum", "Vektoren: Geraden im Raum", "Binomialverteilung", "Hypothesentests",
            "Flächen zwischen Graphen", "Steckbriefaufgaben", "Wiederholung für die Klausur", "Besprechung der Klausur",
        }),
        ["E"] = (new[]
        {
            "Simple past and present perfect", "Unit 3: Vocabulary", "Reading: A day in London", "Writing an email to a friend",
            "Going to future", "If clauses type 1", "Describing pictures", "Listening comprehension",
            "Adverbs of frequency", "Comparison of adjectives", "Role play: At the station", "Mediation exercise",
        }, new[]
        {
            "The American Dream", "Globalisation and its effects", "Shakespeare: Romeo and Juliet", "Analysing political speeches",
            "Writing a comment", "Dystopian fiction: Brave New World", "Post-colonial Britain", "Mediation: German text into English",
            "Film analysis: camera angles", "Exam training: analysis", "Discussion: social media", "Feedback on the written exam",
        }),
        ["F"] = (new[]
        {
            "Les articles et les noms", "Le présent des verbes en -er", "Unité 2: Vocabulaire", "Se présenter",
            "Le passé composé avec avoir", "Les nombres jusqu'à 100", "Décrire sa famille", "Dialogue: au marché",
        }, new[]
        {
            "La France et l'Europe", "Le subjonctif", "Analyse d'un texte littéraire", "La banlieue", "Médiation", "Préparation à l'examen",
        }),
        ["L"] = (new[]
        {
            "Lektion 8: Vokabeln und Grammatik", "Ablativus absolutus", "AcI übersetzen", "Deklinationen wiederholen",
            "Übersetzung: Caesar", "Partizipien", "Stammformen der Verben", "Römischer Alltag",
        }, new[] { "Cicero: Reden", "Ovid: Metamorphosen", "Stilmittel bestimmen", "Übersetzungsübung", "Seneca: Briefe", "Klausurvorbereitung" }),
        ["S"] = (new[] { "Los verbos regulares", "Unidad 2: Vocabulario", "Ser y estar", "En el restaurante", "El pretérito indefinido", "Describir la ciudad" },
            new[] { "La España actual", "El subjuntivo", "Análisis de un texto", "América Latina", "Mediación", "Preparación del examen" }),
        ["BI"] = (new[]
        {
            "Aufbau der Zelle", "Mikroskopieren: Zwiebelhaut", "Fotosynthese", "Verdauung beim Menschen", "Ökosystem Wald",
            "Sinnesorgane: das Auge", "Wirbeltiere im Vergleich", "Blut und Blutkreislauf", "Immunsystem", "Genetik: Mendelsche Regeln",
        }, new[] { "Enzyme und Enzymkinetik", "Proteinbiosynthese", "Neurobiologie: Aktionspotential", "Evolutionstheorien", "Ökologie: Populationsdynamik", "Klausurvorbereitung" }),
        ["CH"] = (new[]
        {
            "Stoffe und ihre Eigenschaften", "Trennverfahren", "Atombau nach Bohr", "Periodensystem der Elemente", "Chemische Reaktionen",
            "Säuren und Laugen", "Versuch: Verbrennung", "Ionenbindung", "Salze", "Sicherheitsbelehrung im Fachraum",
        }, new[] { "Reaktionsgeschwindigkeit", "Chemisches Gleichgewicht", "Redoxreaktionen", "Organische Chemie: Alkane", "Elektrochemie", "Klausurvorbereitung" }),
        ["PH"] = (new[]
        {
            "Geschwindigkeit und Bewegung", "Kräfte und ihre Wirkung", "Elektrischer Stromkreis", "Optik: Reflexion und Brechung", "Dichte",
            "Energieformen", "Ohmsches Gesetz", "Hebelgesetz", "Magnetismus", "Schülerversuch: Reihenschaltung",
        }, new[] { "Kinematik", "Impulserhaltung", "Elektrisches Feld", "Induktion", "Wellenoptik", "Quantenphysik: Photoeffekt" }),
        ["IF"] = (new[] { "Algorithmen und Ablaufdiagramme", "Programmieren mit Scratch", "Daten und Datenschutz", "Tabellenkalkulation", "Binärzahlen", "Netzwerke" },
            new[] { "Objektorientierte Modellierung", "Rekursion", "Sortierverfahren", "Datenbanken und SQL", "Automaten und Grammatiken", "Projektarbeit" }),
        ["EK"] = (new[] { "Orientierung auf der Erde", "Klimazonen", "Landwirtschaft in Deutschland", "Vulkane und Erdbeben", "Städte im Wandel", "Tourismus" },
            new[] { "Globale Disparitäten", "Stadtentwicklung", "Wirtschaftsräume", "Klimawandel", "Raumanalyse", "Klausurvorbereitung" }),
        ["GE"] = (new[]
        {
            "Steinzeit", "Das alte Ägypten", "Leben im Mittelalter", "Die Französische Revolution", "Industrialisierung",
            "Der Erste Weltkrieg", "Weimarer Republik", "Quellenanalyse", "Nationalsozialismus", "Die Teilung Deutschlands",
        }, new[] { "Deutsche Einheit 1871", "Imperialismus", "Ende der Weimarer Republik", "Der Kalte Krieg", "Erinnerungskultur", "Klausurvorbereitung" }),
        ["PK"] = (new[] { "Demokratie in der Schule", "Kinderrechte", "Wahlen in Deutschland", "Medien und Meinung", "Die Europäische Union", "Der Bundestag" },
            new[] { "Grundgesetz", "Parteien", "Soziale Marktwirtschaft", "Internationale Politik" }),
        ["WI"] = (new[] { "Bedürfnisse und Güter", "Markt und Preis", "Taschengeld und Konsum", "Berufsorientierung" }, new[] { "Konjunktur", "Geldpolitik" }),
        ["SW"] = (new[] { "Familie im Wandel", "Rollen und Normen" }, new[] { "Sozialstruktur", "Globalisierung", "Wirtschaftspolitik", "Partizipation", "Ungleichheit", "Klausurvorbereitung" }),
        ["PL"] = (new[] { "Was ist Glück?" }, new[] { "Erkenntnistheorie", "Ethik nach Kant", "Utilitarismus", "Staatsphilosophie: Hobbes", "Freiheit und Determinismus", "Klausurvorbereitung" }),
        ["PPL"] = (new[] { "Freundschaft", "Gerechtigkeit", "Umgang mit Konflikten", "Werte und Normen", "Mensch und Natur", "Glück und Sinn" }, Array.Empty<string>()),
        ["KR"] = (new[] { "Die Bibel", "Abraham", "Jesus von Nazareth", "Kirchenjahr", "Weltreligionen: Judentum", "Gewissen und Verantwortung" },
            new[] { "Gottesfrage", "Christliche Ethik", "Kirche heute" }),
        ["ER"] = (new[] { "Schöpfung und Verantwortung", "Martin Luther", "Gleichnisse", "Diakonie", "Weltreligionen: Islam", "Feste im Kirchenjahr" },
            new[] { "Theodizee", "Ethik und Medizin", "Kirche und Staat" }),
        ["KU"] = (new[] { "Farbenlehre", "Perspektive zeichnen", "Collage", "Porträt", "Druckgrafik", "Werkbetrachtung", "Plastisches Gestalten" },
            new[] { "Bildanalyse", "Architektur", "Fotografie", "Malerei des Expressionismus" }),
        ["MU"] = (new[] { "Notenwerte und Takt", "Instrumentenkunde", "Rhythmusübungen", "Musik und Bild", "Popmusik analysieren", "Singen im Klassenverband" },
            new[] { "Musik der Romantik", "Formenlehre", "Filmmusik", "Komposition" }),
        ["SP"] = (new[] { "Ballspiele: Basketball", "Geräteturnen", "Leichtathletik: Sprint", "Ausdauerlauf", "Volleyball: Pritschen", "Schwimmen", "Fußball: Passspiel", "Tischtennis", "Badminton" },
            new[] { "Volleyball: Spielsysteme", "Fitness und Trainingslehre", "Basketball: Taktik", "Leichtathletik: Weitsprung" }),
        ["NW"] = (new[] { "Wasser im Alltag", "Pflanzen untersuchen", "Wetterbeobachtung", "Experimentieren mit Luft", "Magnetismus entdecken" }, Array.Empty<string>()),
        ["GL"] = (new[] { "Leben in der Gemeinde", "Kinderrechte weltweit", "Europa entdecken", "Wirtschaft im Alltag" }, Array.Empty<string>()),
        ["SU"] = (new[] { "Jahreszeiten", "Verkehrserziehung", "Unser Körper", "Tiere im Wald" }, Array.Empty<string>()),
        ["KL"] = (new[] { "Klassenrat", "Organisatorisches und Termine", "Wahl der Klassensprecher", "Klassenfahrt planen", "Sozialtraining", "Lernen lernen" }, Array.Empty<string>()),
        ["FÖ"] = (new[] { "Förderung Lesen", "Förderung Rechtschreibung", "Förderung Rechnen", "Individuelle Lernzeit" }, Array.Empty<string>()),
    };

    public static readonly string[] AllgemeineThemen =
    {
        "Einführung in das neue Thema", "Erarbeitung mit Arbeitsblatt", "Gruppenarbeit und Präsentation", "Übung und Vertiefung",
        "Wiederholung der letzten Stunde", "Besprechung der Hausaufgaben", "Partnerarbeit", "Ergebnissicherung",
    };

    /// <summary>Zusätze zum Lehrstoff (Teil der Stunde, z. B. „Fortsetzung“), leer = keiner.</summary>
    public static readonly string[] ThemenZusatz =
    {
        "", "", "", "", "", "", "Fortsetzung: ", "Übung: ", "Einführung: ", "Wiederholung: ", "Vertiefung: ",
    };

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

    public static readonly string[] Aufgabenhinweise =
    {
        "Für die {k} liegen Arbeitsblätter in meinem Fach im Lehrerzimmer.",
        "Die {k} kann im Buch weiterarbeiten, die Aufgaben stehen im Klassenbuch.",
        "Aufgaben für die {k} lade ich gleich in den Kursordner hoch.",
        "In der {k} sollte nach Möglichkeit nichts ausfallen, die Klassenarbeit steht bald an.",
        "Material für die {k} schicke ich morgen früh per Mitteilung.",
        "Die {k} kann die Übungsaufgaben aus der letzten Stunde bearbeiten.",
    };
}
