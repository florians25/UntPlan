namespace UntPlan.App;

/// <summary>Inhalte der eingebauten Hilfe (F1). Aufbau entspricht dem Benutzerhandbuch (docs/HANDBUCH.md).</summary>
public static class Handbuch
{
    public sealed record Thema(string Titel, string Text, params string[] Stichworte);

    public static readonly List<(string Kapitel, List<Thema> Themen)> Kapitel = new()
    {
        ("Erste Schritte", new()
        {
            new("Willkommen",
                "UntPlan ist eine Windows-Anwendung für Stundenplanung und Vertretungsplanung an Schulen in Nordrhein-Westfalen.\n" +
                "Die Bedienung folgt dem bekannten Aufbau: Menüband (Ribbon) mit den Registerkarten Start, Dateneingabe, Planung, Stundenpläne, Kursplanung, Vertretungsplanung und Module; " +
                "darunter der Arbeitsbereich mit beliebig vielen Fenstern (Stammdaten, Zeitwünsche, Unterricht, Stundenpläne …).\n" +
                "Alle Daten einer Schule werden in einer Datei *.up gespeichert. Beim Speichern wird automatisch eine Sicherung *.bak angelegt.\n" +
                "Tipp: Über Datei | Beispielschule öffnen erhalten Sie ein vollständiges NRW-Gymnasium (Klassen 5–7, EF mit Kursbändern, Bereitschaften) zum Ausprobieren.",
                "Start", "Begrüßung"),
            new("Eingabeassistent",
                "Der Eingabeassistent (Start | Assistent) führt Schritt für Schritt durch die Ersteingabe: Schuldaten → Zeitraster → Ferien → Räume → Klassen → Lehrer → Fächer → Unterricht → Optimierung → Stundenpläne.\n" +
                "Jeder Schritt öffnet das passende Fenster. Die Reihenfolge entspricht der empfohlenen Arbeitsweise, weil z. B. beim Unterricht bereits Lehrer, Klassen und Fächer vorhanden sein müssen.",
                "Assistent"),
            new("Hilfe (F1)", "Mit F1 wird in jedem Fenster das passende Kapitel dieser Hilfe angezeigt. Links wählen Sie Kapitel und Themen.", "F1"),
            new("Rückgängig / Wiederholen", "Jede Änderung kann mit Strg+Z rückgängig gemacht und mit Strg+Y wiederholt werden (50 Schritte). Die Schaltflächen befinden sich in der Schnellzugriffsleiste oben links.", "Undo"),
        }),
        ("Schuldaten", new()
        {
            new("Schuldaten",
                "Unter Start | Einstellungen | Schuldaten geben Sie Schulname, Schulnummer, Schulform (NRW: GY, GE, RS, HS, SK, PS, BK, FÖ), Region und Schuljahr ein.\n" +
                "Das Schuljahr bestimmt Anfang und Ende für Kalender, Ferien, Vertretungsplanung und Wertrechnung. Mit 'Wochenperiodizität 2' werden A/B-Wochen verwendet.",
                "Schuljahr", "Einstellungen"),
            new("Ferien",
                "Im Fenster Ferien werden Ferien und Feiertage verwaltet. 'NRW-Ferien und Feiertage eintragen' übernimmt die amtlichen Termine für NRW einschließlich der beweglichen Feiertage.\n" +
                "Weitere freie Tage (bewegliche Ferientage, pädagogische Tage) geben Sie direkt im Jahreskalender ein: mit gedrückter Maustaste den Zeitraum überstreichen und eine Bezeichnung vergeben.\n" +
                "Ferien sind für Vertretungsplanung, Kalender und die tagesgenaue Wertrechnung relevant.",
                "Feiertage", "Kalender"),
            new("Zeitraster",
                "Im Zeitraster legen Sie die Anzahl Tage pro Woche (z. B. Mo–Fr), die Stunden pro Tag sowie die Uhrzeiten jeder Stunde fest.\n" +
                "In der Zeile 'Belegung' wird jede Stunde als Vormittag, Nachmittag oder Mittagspause markiert. Die Mittagspause kann fix (bestimmte Stunde) oder flexibel (Bereich, z. B. 6.–7. Stunde) definiert werden.\n" +
                "Pausen zwischen den Stunden werden mit Bezeichnung und Dauer eingetragen; ist 'Aufsicht' angehakt, erscheint die Pause in der Pausenaufsichtsplanung.\n" +
                "Achtung: Wird das Zeitraster nachträglich verkleinert, werden Stunden außerhalb des Rasters entplant.",
                "Stundenzeiten", "Pausen", "Mittagspause"),
        }),
        ("Stammdaten", new()
        {
            new("Stammdaten",
                "Stammdaten sind Räume, Klassen, Lehrer, Fächer, Gänge (Aufsichtsbereiche), Abteilungen, Abwesenheitsgründe und Schüler.\n" +
                "Jedes Element hat einen eindeutigen Kurznamen (max. 20 Zeichen) und einen Langnamen. Neue Elemente geben Sie in der letzten (leeren) Zeile ein.\n" +
                "Die Stammdatenfenster haben eine Rasteransicht (Tabelle) und eine Formularansicht (Karteikarten mit allen Feldern eines Elements). Umschalten über die Schaltfläche in der Werkzeugleiste.\n" +
                "Felder werden per Drag&Drop aus der Formularansicht in die Tabelle gezogen und mit Strg+Ziehen wieder ausgeblendet; über 'Felder der Ansicht' können Sie die Spalten auch auswählen.\n" +
                "Tipp: Mit Doppelklick auf den Kurznamen ändern Sie den Namen. Alle Verweise (Unterricht, Stundenplan) werden automatisch angepasst.",
                "Rasteransicht", "Formularansicht", "Kurzname"),
            new("Räume",
                "Für jeden Raum können Ausweichraum, Raumgewicht, Kapazität und Gang angegeben werden.\n" +
                "Ausweichraum: Ist der gewünschte Raum belegt, wird der Ausweichraum versucht. Ausweichräume können einen Ring bilden (R1 → R2 → R3 → R1): Die Optimierung nutzt dann jeden freien Raum des Rings.\n" +
                "Raumgewicht (0–4): 0 = Raum ist nicht wichtig, Unterricht kann auch ohne Raum stattfinden; 4 = Raum ist sehr wichtig (z. B. Sporthalle, Fachraum).\n" +
                "Raumgruppen fassen gleichwertige Räume zusammen (z. B. EDV1, EDV2). Wird im Unterricht eine Raumgruppe als Fachraum angegeben, wählt die Optimierung einen freien Raum der Gruppe.\n" +
                "Wanderklassen: Klassen ohne Stammraum erhalten jeweils einen freien Raum aus dem Ausweichraum-Ring.\n" +
                "Ein Raum wird zu bestimmten Zeiten gesperrt, indem im Zeitwunschfenster der Wert −3 eingetragen wird.",
                "Ausweichraum", "Raumgewicht", "Raumgruppe", "Wanderklasse"),
            new("Klassen",
                "Wichtige Felder: Stammraum (Klassenraum), Jahrgang (5–10, EF, Q1, Q2), Klassenlehrer, Hauptfächer pro Tag (max. Anzahl Hauptfachstunden pro Tag), Hauptfächer hintereinander, " +
                "Mittagspause (min./max. Länge), max. Nachmittage und Stunden pro Tag (min.–max.).\n" +
                "Kernzeit: Mit Zeitwunsch +3 werden Stunden markiert, die unbedingt belegt werden sollen (keine Randstunde frei). Die Optimierung bevorzugt diese Zeiten.",
                "Klasse", "Kernzeit", "Klassenraum"),
            new("Lehrer",
                "Lehrer haben Wochensoll, Ermäßigungen (Anrechnungsstunden), Lehrbefähigung (Fächer), Stammraum und Status.\n" +
                "Planungsrelevant: Hohlstunden (min.–max.), Stunden pro Tag (min.–max.), max. Nachmittage, Mittagspause.\n" +
                "Für die Vertretungsplanung: max. Vertretungen pro Woche/Monat, 'keine Vertretung'; für Pausenaufsichten: Aufsichten min./max. und 'keine Aufsicht'.",
                "Lehrer", "Hohlstunden", "Lehrbefähigung"),
            new("Fächer",
                "Fächer können einen Fachraum bzw. eine Fachraumgruppe besitzen, als Hauptfach gekennzeichnet werden und einen Werte-Faktor für die Wertrechnung haben.\n" +
                "Weitere Merkmale: 'mehrmals pro Tag erlaubt', 'Randstunde bevorzugt', 'kein Nachmittag', Doppelstunden (min.–max.).\n" +
                "Bereitschaft: Fächer mit dem Merkmal 'Bereitschaft' (Standard: Kurzname BER) sind keine echten Unterrichte. Eine Bereitschaftsstunde wird wie Unterricht verplant; " +
                "der Lehrer steht in dieser Stunde für Vertretungen zur Verfügung. Bereitschaftsvertretungen zählen nicht als Mehrarbeit.",
                "Fach", "Bereitschaft", "Hauptfach"),
            new("Zeitwünsche",
                "Bestimmte Zeitwünsche werden im Zeitwunschfenster für Klassen, Lehrer, Räume und Fächer eingegeben: −3 = gesperrt, −2/−1 = möglichst nicht, 0 = neutral, +1/+2 = gerne, +3 = unbedingt (Kernzeit).\n" +
                "Wert oben wählen und mit der Maus über die Stunden streichen. Klick auf den Tag belegt den ganzen Tag, die Spalten Vorm./Nachm. alle Vormittage bzw. Nachmittage.\n" +
                "Unbestimmte Zeitwünsche (z. B. 'ein freier Vormittag', 'zwei Nachmittage frei') werden im unteren Teil eingegeben: Anzahl, Tage/Vormittage/Nachmittage, Gewicht −1 bis −3.\n" +
                "Tipp: Die Option 'Synchronisieren' zeigt immer das Element, das in einem anderen Fenster ausgewählt ist.",
                "Zeitwunsch", "gesperrt", "unbestimmt"),
            new("Ansichten und Sortierung",
                "Jedes Stammdatenfenster speichert beliebig viele Ansichten (Spaltenauswahl, Breiten, Sortierung). Über das Auswahlfeld in der Werkzeugleiste wird die Ansicht gewechselt.\n" +
                "Sortieren: Klick auf den Spaltenkopf oder 'Sortieren' mit bis zu drei Kriterien. 'Umreihen' speichert die aktuelle Reihenfolge dauerhaft.\n" +
                "Serienänderung: Mehrere Zeilen markieren, Feld wählen und neuen Wert für alle markierten Elemente setzen.",
                "Ansicht", "Sortierung", "Serienänderung"),
            new("Kommentare",
                "Zu jedem Feld eines Elements kann ein Kommentar erfasst werden (rechte Maustaste | Kommentar). Kommentierte Felder zeigen ein rotes Dreieck.\n" +
                "Kommentare haben Kategorien (z. B. Hinweis, Wichtig, Erledigt). Die Kommentar-Übersicht listet alle Kommentare der Datei.",
                "Kommentar"),
            new("Drucken und Seitenlayout",
                "Alle Listen und Stundenpläne werden über Datei | Drucken bzw. Druckvorschau ausgegeben. Im Seitenlayout legen Sie Hoch-/Querformat, Ränder, Kopf- und Fußzeile fest.\n" +
                "Stundenpläne können zusätzlich als HTML-Seite (für Homepage oder Info-Bildschirm) und – über einen PDF-Drucker – als PDF ausgegeben werden.",
                "Druck", "Seitenlayout", "PDF"),
        }),
        ("Unterricht", new()
        {
            new("Unterricht",
                "Der Unterricht verbindet Lehrer, Fach, Klasse(n) und Räume mit einer Wochenstundenzahl. Jeder Unterricht erhält eine eindeutige Unterrichtsnummer (U-Nr.).\n" +
                "Das Unterrichtsfenster zeigt den Unterricht einer Klasse oder eines Lehrers (Auswahl oben). In der Rasteransicht geben Sie Wst., Lehrer, Fach, Klasse(n), Fachraum und Stammraum ein; die Formularansicht zeigt alle Felder.\n" +
                "Fachraum und Stammraum werden automatisch vorgeschlagen (Fachraum des Fachs, Stammraum der Klasse bzw. des Lehrers).\n" +
                "Die Spalte 'Nvpl' zeigt die noch nicht verplanten Stunden; von dort kann eine Stunde per Drag&Drop direkt in einen Stundenplan gezogen werden.",
                "Unterrichtsnummer", "Wochenstunden", "Nvpl"),
            new("Kopplungen",
                "Eine Kopplung ist ein Unterricht mit mehreren Kopplungszeilen, die gleichzeitig stattfinden – z. B. Religion: KR (Lehrer A), ER (Lehrer B) und PP (Lehrer C) für die Klassen 6a und 6b.\n" +
                "Neue Kopplungszeile: im Unterricht die Schaltfläche 'Kopplungszeile hinzufügen'. Mit dem Plus-Zeichen vor der Zeile werden die Kopplungszeilen ein- und ausgeblendet.",
                "Kopplung", "gekoppelt"),
            new("Doppelstunden und Blöcke",
                "Im Feld Doppelstunden wird die gewünschte Anzahl angegeben, z. B. 1–1 (genau eine Doppelstunde) oder 0–2. Blöcke (Feld 'Block') legen größere zusammenhängende Einheiten fest, z. B. 3 für Dreierblöcke.\n" +
                "Kursbänder: Unterrichte mit gleichem 'Band' werden immer gemeinsam (parallel) verplant – typisch für die gymnasiale Oberstufe.",
                "Doppelstunde", "Block", "Band"),
        }),
        ("Automatische Planung", new()
        {
            new("Gewichtung",
                "In der Gewichtung (Planung | Gewichtung) legen Sie fest, wie wichtig die einzelnen Kriterien für die Optimierung sind (0 = unwichtig bis 5 = sehr wichtig). Karteikarten: Lehrer, Klassen, Fächer, Hauptfächer, Räume, Stundenverteilung, Zeitwünsche.\n" +
                "Die Grafik 'Verteilung' zeigt, wie sich die Gewichte zueinander verhalten. 'Analyse' prüft, welche Kriterien bei den aktuellen Daten problematisch sind.",
                "Gewichtung"),
            new("Optimierung",
                "Die Optimierung (Planung | Optimierung) verplant alle nicht fixierten Stunden. Strategien:\n" +
                "A – Schnelloptimierung (wenige Minuten), B – Normaloptimierung, D – Intensivoptimierung (mehrere Pläne, Tauschoptimierung), E – Nächtliche Optimierung (lange Laufzeit, viele Pläne).\n" +
                "Optional: nur Schritte 1–9 (Verplanung / Tauschoptimierung / Raumoptimierung), Start- und Erhöhungsprozentsatz für schrittweises Verplanen, Anzahl Pläne.\n" +
                "Nach der Optimierung werden alle berechneten Pläne mit Bewertungszahl angezeigt (kleiner = besser). Der beste Plan ist markiert und wird mit OK übernommen.\n" +
                "Achtung: Fixierte Stunden (*) werden nicht verändert.",
                "Strategie", "Bewertungszahl", "Optimierung"),
            new("Diagnose",
                "Die Stundenplan-Diagnose zeigt in zwei Karteikarten: 'Eingabedaten' (Probleme vor der Optimierung, z. B. Lehrer mit mehr Stunden als verfügbar, fehlende Räume, Kopplungen mit Zeitsperren) und " +
                "'Stundenplan' (Verstöße im aktuellen Plan, z. B. Kollisionen, Hohlstunden, verletzte Zeitwünsche).\n" +
                "Rechts stehen die Beschreibung und die einzelnen Fälle. Doppelklick öffnet den betroffenen Stundenplan.",
                "Diagnose", "Kollision"),
        }),
        ("Manuelles Planen", new()
        {
            new("Stundenpläne",
                "Stundenpläne werden für Klassen, Lehrer, Räume, Fächer und Schüler angezeigt (Stundenpläne | Klasse/Lehrer/Raum …). Im Kopf wählen Sie das Element, über 'Synchronisieren' folgt der Plan der Auswahl in anderen Fenstern.\n" +
                "Unter dem Plan stehen die nicht verplanten Stunden ('Stapel'). Die Stundenlupe zeigt alle Details zur markierten Stunde.\n" +
                "Formate: Über 'Stundenplaneinstellungen' legen Sie Felder, Schriftgrößen und Farben fest. Im Grafik-Editor 'Stpl-Stunde' werden die Felder einer Stunde frei positioniert (mit Hilfslinien).",
                "Stundenplan", "Stundenlupe", "Format"),
            new("Verplanen und Verschieben",
                "Ziehen Sie eine Stunde mit der Maus aus dem Stapel oder aus dem Plan auf eine neue Position. Beim Ziehen werden die Felder eingefärbt:\n" +
                "grün = Stunde ist möglich, rot = Kollision (Lehrer, Klasse oder Raum belegt), violett = Zeitwunsch verletzt / gesperrt.\n" +
                "Wird eine Stunde auf eine belegte Position gezogen, schlägt UntPlan Tauschen oder Verdrängen vor. Mit F (oder der Schaltfläche) wird eine Stunde fixiert (*), Entf entplant sie.\n" +
                "Tauschvorschläge: Für die markierte Stunde werden alle möglichen Tausche mit ihrem Gewinn (Verbesserung der Bewertung) angezeigt.",
                "verplanen", "tauschen", "fixieren", "entplanen", "Tauschvorschläge"),
            new("Raumzuordnung",
                "Über 'Raum zuordnen' erhalten Sie die Liste aller Räume mit ihrem Status (frei, belegt, Ausweichraum). Bei belegtem Raum kann der andere Unterricht verdrängt, eine Kollision erzeugt oder die Räume getauscht werden.\n" +
                "In der Übersicht (Raumplan quer) können Räume per Drag&Drop umgeplant werden.",
                "Raum zuordnen", "verdrängen"),
            new("Perioden",
                "Mit Perioden können verschiedene Stundenpläne für Zeitabschnitte des Schuljahres gelten (z. B. ab dem 2. Halbjahr). Jede Periode hat einen eigenen Plan; die aktive Periode wird oben gewählt.",
                "Periode", "Mehrwochen", "Halbjahr"),
        }),
        ("Vertretungsplanung", new()
        {
            new("Vertretungsplanung",
                "Das Vertretungsfenster zeigt alle Vertretungen eines Tages. Datum oben wählen oder mit den Pfeilen blättern.\n" +
                "Arbeitsablauf: 1. Abwesenheiten eingeben → 2. UntPlan erzeugt automatisch die offenen Vertretungen (rot '???') → 3. im Vertretungsvorschlag einen Lehrer wählen (Doppelklick) oder Entfall/Betreuung/Raumvertretung/Verlegung setzen → 4. Vertretungsplan drucken bzw. veröffentlichen.\n" +
                "Der Vertretungsvorschlag bewertet alle freien Lehrer nach der Vertretungsgewichtung: Bereitschaft, freigesetzt (Klasse abwesend), kennt die Klasse, Fachlehrer, Hohlstunde, Anschluss an eigenen Unterricht, Anzahl bisheriger Vertretungen, Saldo, Zeitwünsche.\n" +
                "Bereitschaft: Lehrer mit Bereitschaftsstunde erscheinen im Vorschlag grün hinterlegt ganz oben; diese Vertretungen zählen nicht als Mehrarbeit.\n" +
                "Entfall: Für Klassen ab dem eingestellten Jahrgang (Standard EF) schlägt UntPlan bei Randstunden einen Entfall vor.\n" +
                "Weitere Arten: Raumvertretung (anderer Raum), Verlegung (Unterricht auf andere Stunde), Betreuung, Sondereinsatz, Klausur, statt-Vertretung (freigesetzter Lehrer vertritt).\n" +
                "Automatische Vertretung: plant alle offenen Vertretungen des Tages nach dem Vorschlag.\n" +
                "Tipp: Die Spalten Vertreter, Fach, Raum und Text können direkt überschrieben werden.",
                "Vertretung", "Vertretungsvorschlag", "Entfall", "Betreuung", "Verlegung", "Raumvertretung"),
            new("Abwesenheiten",
                "Abwesenheiten werden für Lehrer (Krankheit, Fortbildung …), Klassen (Exkursion, Klassenfahrt) und Räume (Sperrung) mit Zeitraum von Datum/Stunde bis Datum/Stunde eingegeben.\n" +
                "Bei Klassenabwesenheiten können Begleitlehrer angegeben werden; sie gelten ebenfalls als abwesend. Der Unterricht der abwesenden Klasse wird als 'Freisetzung' geführt – " +
                "die frei gewordenen Lehrer stehen für Vertretungen zur Verfügung (Minusstunden im Saldo).",
                "Abwesenheit", "Freisetzung", "Klassenfahrt"),
            new("Pausenaufsichten",
                "Im Fenster Pausenaufsichten wird für jeden Aufsichtsbereich (Gang) und jede Aufsichtspause eingetragen, welche Lehrer Aufsicht haben. 'Optimieren' verteilt die Aufsichten automatisch " +
                "unter Berücksichtigung von Anwesenheit (Unterricht vor/nach der Pause), Zeitwünschen sowie Min./Max.-Werten der Lehrer.\n" +
                "Fehlt eine Aufsicht wegen Abwesenheit, erscheint sie im Vertretungsfenster unter 'Pausenaufsicht-Vertretungen'.",
                "Pausenaufsicht", "Gang", "Aufsicht"),
            new("Statistik",
                "Die Vertretungsstatistik zeigt je Lehrer Vertretungen, Betreuungen, Sondereinsätze, Pausenaufsichten, Mehrarbeit, Entfälle, Freisetzungen und den Saldo im gewählten Zeitraum.\n" +
                "NRW-Regel Mehrarbeit: Mehrarbeit wird vergütet, wenn im Kalendermonat mehr als 3 Unterrichtsstunden (Grenze einstellbar) geleistet wurden – dann alle Stunden; Minderstunden (Freisetzungen) werden verrechnet.\n" +
                "Die Liste kann gedruckt oder als CSV-Datei (z. B. für die Bezirksregierung / Abrechnung) exportiert werden.",
                "Statistik", "Mehrarbeit", "Abrechnung"),
            new("Vertretungsplan",
                "Der Vertretungsplan wird als Druck, HTML-Seite (Schüler- und Lehrerversion) und als Monitoransicht (Vollbild für Info-Bildschirme, automatischer Seitenwechsel, ESC beendet) ausgegeben.\n" +
                "Tagestexte (Nachrichten zum Tag) erscheinen oben im Vertretungsplan; Texte 'nur für Lehrer' nur in der Lehrerversion.",
                "Monitor", "Aushang", "Tagestext"),
        }),
        ("Module", new()
        {
            new("Unterrichtsplanung",
                "Die Wertrechnung vergleicht für jeden Lehrer Soll (Wochensoll − Ermäßigungen) mit Ist (Summe der Werte seiner Unterrichte). Der Wert ist Wochenstunden × Werte-Faktor, anteilig bei A/B-Wochen und befristetem Unterricht.\n" +
                "Die Unterrichts-Matrix zeigt Klassen × Fächer mit Lehrern und Stunden. In der Stundentafel werden die Soll-Stunden je Jahrgang eingegeben und mit dem Unterricht abgeglichen.\n" +
                "Lehrervorschlag: Im Unterrichtsfenster schlägt UntPlan geeignete Lehrer vor (Lehrbefähigung, freies Deputat, kennt Klasse).",
                "Wertrechnung", "Stundentafel", "Matrix", "Soll", "Ist"),
            new("Kursplanung",
                "Für die gymnasiale Oberstufe werden Kurse (Unterrichte mit Kursname), Schüler und Kurswahlen verwaltet. Die Kollisionsmatrix zeigt, wie viele Schüler zwei Kurse gleichzeitig gewählt haben.\n" +
                "Die Bandbildung ordnet die Kurse automatisch Bändern (Schienen) zu, sodass möglichst kein Schüler zwei Kurse im selben Band hat und kein Lehrer doppelt belegt ist. 'Bänder übernehmen' schreibt das Ergebnis in den Unterricht.",
                "Kurs", "Kurswahl", "Band", "Schiene", "Oberstufe"),
            new("Abteilungsplanung",
                "Große Schulen (z. B. Berufskollegs) können die Planung auf Abteilungen verteilen: Abteilungsdatei exportieren, getrennt planen, wieder zusammenführen. Elemente und Unterrichte werden über das Feld Abteilung zugeordnet.",
                "Abteilung"),
            new("Import und Export",
                "Import: Lehrer, Klassen und Schüler aus CSV (z. B. aus SchILD-NRW) sowie Stammdaten und Unterricht aus GPU-Dateien (Umstieg aus anderen Planungsprogrammen).\n" +
                "Export: GPU-Dateien, HTML-Stundenpläne, Info-Stundenplan, Vertretungsplan, iCal-Kalender je Lehrer.",
                "Import", "Export", "CSV", "GPU", "SchILD"),
            new("WebPortal-Synchronisation",
                "Über Module | WebPortal werden Stundenplan, Vertretungen und Tagestexte an ein WebPortal übertragen (HTTPS, API-Schlüssel). Abwesenheitsmeldungen aus dem Portal (z. B. Krankmeldungen) werden abgeholt und als Abwesenheiten übernommen.\n" +
                "Die Schnittstelle ist in docs/SYNC_API.md beschrieben.",
                "Portal", "Synchronisation", "Web"),
        }),
    };
}
