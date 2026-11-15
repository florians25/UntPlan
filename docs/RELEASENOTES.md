UntPlan – Stunden- und Vertretungsplanung für Schulen in NRW.

**Installation:** `UntPlan-Setup-*.exe` herunterladen und ausführen (Windows 10/11, 64 Bit, keine .NET-Installation nötig).
Ohne Installation: `UntPlan-portabel.zip` entpacken und `UntPlan.exe` starten.


## Neu in dieser Version

### Desktop
- Sondereinsatz (zusätzliche Stunde): kollidiert er mit vorhandenem Unterricht der Lehrkraft bzw. Klasse, fragt UntPlan nach – auflisten der betroffenen Stunden, dann „Auflösen (Vertretung/Entfall)“, „Unterricht belassen“ oder „Abbrechen“. Der ursprüngliche Unterricht wird nicht mehr ungefragt zur offenen Vertretung.
- Eingabefelder in Tabellen werden nie mehr schwarz – weder das Feld noch der Rand darum (auch nicht mit Textvorschlag im Feld).

### Klassenbuch – Rechte
- Jede Lehrkraft trägt Abwesenheiten und Verspätungen in allen Stunden ihres Plans ein (eigene Klassen und Vertretungen) und kann dort auch Fehlzeiten ändern bzw. löschen, die eine andere Lehrkraft eingetragen hat.

### Web
- Seitenleiste: Menüpunkte wie in der Vorlage – Text grau (#67747e) statt dunkel, Symbole etwas kleiner, Text 2 px weiter links, aktiver Punkt im Orange der Vorlage (#fe6033).
- Startseite „Heute“: Schriftgrößen und Farben wie in der Vorlage nachgemessen – Titel etwas kleiner, Datum hellgrau (#999), Anmeldung/Planaktualisierung und Nachrichten in 12 px dunkelgrau (#333), Kartenüberschriften etwas kleiner.
- Raumbuchung: bucht eine Lehrkraft für ihre Stunde einen anderen Raum, wird der ursprüngliche Raum in dieser Stunde frei und steht in der Buchung (Raumsuche, Belegung, freie Räume, Prüfungen) sofort wieder zur Verfügung.
- Durchgestrichene Labels (z. B. abwesende Lehrkraft, entfernter Raum) wie in der Vorlage: Linie genau mittig durch die Kürzel im Label.
- Startseite „Heute“: Lehrstoff in einer eigenen Spalte (neben Status und Klassenbuch).
- Menüpunkt „Übersicht“ nur noch für Schulleitung und Administration, nicht für Lehrkräfte.

### iOS
- Startseite (Klassenleitung): eigener Punkt „Abwesenheiten“ mit der Zahl der offenen Abwesenheiten; die Liste startet wie in der Vorlage gefiltert auf „Offen“ (Menü „Status wählen“: Alle, Offen, Unentschuldigt, Entschuldigt). Im Dialog öffnet „Entschuldigungsstatus“ die Seite „Entschuldigungsstatus wählen“ (gewählter Status mit orangem Haken); unten steht der Abwesenheitsgrund.
- Raum ersetzen/hinzufügen wie in der Vorlage: Titel „R1BK ersetzen“, Suchfeld, Kapsel „Abteilung“, je Raum eine graue Scheibe mit Raumsymbol und das Kürzel.
- Klassenleitung → Abwesenheiten wie in der Vorlage: Suchfeld, Filter „Zeitbereich“ und „Status“, Gruppen „Offen“, „Unentschuldigt“, „Entschuldigt“; je Zeile Avatar, Name, Klasse, Datum und Zeit, Grund und rechts ein roter bzw. grüner Rand. „Abwesenheit bearbeiten“ mit START/ENDE, Zeile „Entschuldigungsstatus“ und „Text für Entschuldigung“.
- Start → „Klassenleitung“: Übersicht der eigenen Klasse mit „Abwesenheiten“ und der Zahl der unentschuldigten Abwesenheiten. Die Abwesenheiten stehen als Karten (Datum und Zeit, Art, Status) unter UNENTSCHULDIGT bzw. ENTSCHULDIGT; „nicht entschuldigt“ rutscht rot markiert nach unten, „entschuldigt“ ist grün markiert. Antippen öffnet die Entschuldigung.
- „Bündeln“: mehrere Abwesenheiten einer Person zu einer gebündelten Fehlzeit zusammenfassen, die dann als Ganzes entschuldigt oder nicht entschuldigt wird. Zeitraum über den Filter oben rechts (30, 90 oder 180 Tage).
- Schülerliste: Wischen nach rechts/links mit den Wischaktionen des Systems – das Scrollen der Liste und das seitliche Blättern zwischen Stunden hängen nicht mehr.
- Stunden im Plan lassen sich immer antippen: kleine Fingerbewegungen beim Tippen gelten nicht mehr als Wischen und sperren das Öffnen nicht.
- Kopplungen und Konferenzen: die Lehrkräfte stehen wie in der Vorlage in einer Zeile und werden am Kartenrand abgeschnitten (kein Umbruch, kein „…“); mehrere Klassen stehen nebeneinander in einer Zeile; im eigenen Lehrerplan steht auch das eigene Kürzel.
- Vertritt eine Lehrkraft, die selbst an der Zusatzstunde bzw. Konferenz teilnimmt, eine Kollegin (z. B. SN für CS), steht sie zweimal auf der Karte: schlicht für die eigene Teilnahme und als grünes Label für die Vertretung.
- Online-Stunden: „Online“ steht ausgeschrieben als Label auf der Karte (nie mit „…“ gekürzt).
- Stunden sind immer antippbar (eine abgebrochene Wischbewegung sperrt das Antippen nicht mehr).
- Schülerliste wie in der Vorlage: Trennlinie unter dem Buchstaben statt zwischen den Zeilen, Verspätung mit gelbem Symbol und „gekommen um 10:35 (fehlt)“ bzw. „gegangen um … (fehlt)“.

### Klassenbuch – Verspätung
- Jede unterrichtende Lehrkraft kann in ihrer Stunde eine Abwesenheit in eine Verspätung (bzw. „früher gegangen“) umwandeln – bisher meldete die App „Nur die Klassenleitung oder Schulleitung kann dies ändern.“

### iOS – mehrere Räume
- Mehrere Räume wie in der Vorlage durch Komma getrennt („C009, C025“, hinzugekommener Raum grün); im Raumplan steht die Lehrkraft direkt unter der Klasse (Klasse, Lehrkraft, Fach, Raum).

### Desktop – Eingabe
- Textvorschläge (zuletzt verwendete Texte) erscheinen nicht mehr als Liste, sondern direkt im Feld: der Rest des passenden Textes steht blau markiert hinter der Eingabe, Tab übernimmt ihn, Weitertippen, Entf oder Esc verwerfen ihn. Behebt das schwarze Feld unter der Eingabe.
- Absenzen: Reiter „Lehrer | Klassen | Räume“ so groß wie im Vertretungsvorschlag.

### Web – Klassenbuch
- Verspätung bzw. früher gegangen an der Schülerkarte als eigene Plakette unter Bild und Name: nur Symbol und Minuten in der Statusfarbe (gelb verspätet, lila früher gegangen, rot als Fehlstunde gewertet), mit Abstand zu Name und Umschalter.
- Kennzahlen oben (Klassenbuch, Übersicht) einen Tick weniger fett.

### Web – Stundenplan-Karten
- Kartentext nachgemessen: erste Zeile ohne zusätzliche Kontur (weniger fett), Lehrkraft und Raum in Regular statt Medium, Zeilenabstände wie in der Vorlage (erste Zeile 2px näher an den übrigen; mit Label wie „HA“ bleibt der volle Abstand zur nächsten Zeile).

### Desktop – Absenzen und Vertretungen wie im Vorbild
- Kopf nachgemessen: Rahmen „Von-Bis“ mit Datum TT.MM.JJJJ, Schloss und Bis-Datum, darunter Wochentag und kleine Pfeile; Kalenderknopf rechts neben dem Rahmen, bei den Absenzen darunter die Anzahl der Tage.
- Absenzen: Reiter „Lehrer | Klassen | Räume“ als klassische Karteireiter auf farbigem Band, Fenstertitel „Absenzen / Standard“.
- Vertretungen: „Klasse(n)“ als klassisches Auswahlfeld ohne Rahmen, „Ansicht einschränken auf...“ mit Kästchen untereinander; „Veranstaltungen zusammenfassen“ jetzt im Menü Einstellungen.
- Tabellen ohne Zeilenköpfe, vertiefter Rahmen, Überschrift der aktuellen Spalte hell orange, zu lange Überschriften werden abgeschnitten statt umbrochen.

### Desktop – Stundenplan
- Doppelstunden und Blöcke werden auch in der Stundenplanung getrennt, wenn eine Stunde keinen Raum oder einen anderen Raum hat (gilt auch für Ziehen und „Ganzen Block markieren“).

### Desktop – sicheres Bedienen wie im Handbuch
- Alle Auswahllisten in jeder Maske gleich: Kurz- und Langname, öffnet nach unten, Tippen springt sofort, Enter/Tab übernimmt, Esc stellt den alten Wert her, ungültige Eingabe mit Hinweis, Mausrad verstellt nichts.
- Einheitliches Eingabeverhalten in allen Tabellen: Enter/Tab/Esc/F2/Entf/Einfg gleich, Einfügen auch aus Tabellenkalkulationen, Rückgängig/Wiederherstellen überall mit Hinweis, dieselbe Lösch-Rückfrage, Position bleibt nach dem Neuaufbau erhalten.
- Abläufe der Handbücher geprüft und angeglichen: Vorziehung als Zeile „Verlegung“ mit roten Pfeilen, „Ausgabe“ öffnet Schüler- und Lehrerliste, Koppeln/Entkoppeln-Dialoge, „Kl,Le“, Module | Vertretungsplanung, Berichte | Vertretungs-Abrechnung.
- Robustheit: ungültige Werte (Text in Zahlenfeldern, zu große Zahlen, unbekannte Kürzel) werden mit deutschem Hinweis abgelehnt statt still übernommen oder verworfen; Sicherungskopie bei beschädigten Einstellungen.

### iOS
- Details: Kopf (X, Datum, ⋮) wieder sichtbar; Stunden von heute und morgen werden im Hintergrund vorgeladen und öffnen sofort aktuell; schnelleres Ein- und Ausblenden.

## Vorherige Version

### Desktop – Funktionen wie im Handbuch
- Planungsdialog (Stecktafel mit Klassen, Lehrern und Räumen, Lupe, Tauschketten, Drag & Drop) und Planungs-Stundenplan; Planungsdialog der Vertretungsplanung mit Entfall, Verlegen und Sondereinsätzen.
- Stammdaten und Unterricht: alle Kennzeichen des Handbuchs mit Wirkung in Optimierung und Diagnose, Unterrichtsfolgen, Unterrichtsvergleich, Koppeln per Drag & Drop, Stammdatenformate, UND-Filter.
- Vertretungen: eigene Formate (Format speichern / speichern als), Formate / Vertretung mit Tageslisten, Sortierkriterien, Von-Bis mit Schloss, „Zu zählende farbig“.
- Vertretungsvorschlag: Stundenplan-Spalte mit so vielen Stunden wie Platz ist, Vormerkungen, Bereitschafts-Zähler, Pausenaufsichten, zweistufige Vorziehungen.
- Ausgabe: Seitenlayout mit Berichts-Einstellungen und Ziehen per Maus, Absenzkopf, Vertretungsnachrichten, Import der Vertretungsplanung aus einer zweiten Datei.
- Optimierung mit Begleitfenster und Lehrerzuordnung, Totaldiagnose, CCC-Analyse, Lehrerteams, Vergleichsmodus, weitere Einstellungsseiten, Stundenplan-Seitenlayout mit mehreren Plänen pro Seite, Element-Rollup zum Andocken.
- Menüband verkleinert sich bei schmalen Fenstern statt abzuschneiden.

### UntPlanWeb
- Seitenleiste immer 280 px breit; Schriftschnitte wie im Original (Menüpunkte und Namen Demi, Fließtext Regular); Überfahren in allen Menüs hellorange.

### iOS
- Parallele Stunden werden beim Öffnen geladen und lassen sich direkt wischen; Details fahren ohne Zucken ein.
- Entfall-Kopf blaugrau, Startseiten-Widget mit grün hinterlegten Änderungen.
