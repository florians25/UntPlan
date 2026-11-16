UntPlan – Stunden- und Vertretungsplanung für Schulen in NRW.

**Installation:** `UntPlan-Setup-*.exe` herunterladen und ausführen (Windows 10/11, 64 Bit, keine .NET-Installation nötig).
Ohne Installation: `UntPlan-portabel.zip` entpacken und `UntPlan.exe` starten.


## Neu in dieser Version

### Desktop
- Vertretung ohne Lehrkraft („+++“): Es wird kein automatischer Text „Vertr. o. Lehrkr.“ mehr ins Feld „Text“ eingetragen bzw. in der Spalte „Text“ gezeigt; ein früher eingesetzter Standardtext verschwindet beim erneuten Setzen. Eigene Texte bleiben.
- Betreuung: „Raum des Betreuungslehrers übernehmen“ ist standardmäßig nicht mehr angehakt.
- Verlegungen merken sich den Raum der Zielstunde (Soll-Raum); der Vertretungsplan zeigt ihn als bisherigen Raum – nur eine spätere Raumänderung erscheint als Raumänderung.
- Sondereinsatz (zusätzliche Stunde): kollidiert er mit vorhandenem Unterricht der Lehrkraft bzw. Klasse, fragt UntPlan nach – auflisten der betroffenen Stunden, dann „Auflösen (Vertretung/Entfall)“, „Unterricht belassen“ oder „Abbrechen“. Der ursprüngliche Unterricht wird nicht mehr ungefragt zur offenen Vertretung.
- Eingabefelder in Tabellen werden nie mehr schwarz – weder das Feld noch der Rand darum (auch nicht mit Textvorschlag im Feld).

### Klassenbuch – Rechte
- Jede Lehrkraft trägt Abwesenheiten und Verspätungen in allen Stunden ihres Plans ein (eigene Klassen und Vertretungen) und kann dort auch Fehlzeiten ändern bzw. löschen, die eine andere Lehrkraft eingetragen hat.

### Sprechtag (neu, ersetzt das bisherige Modul)
- Übersicht „Sprechtage“ mit Karten je Schuljahr (Phase, Datum, Anmeldezeitraum; Löschen, Duplizieren, Bearbeiten) und „Neuer Sprechtag“ mit Auswahl des Schuljahres. Das Info-Symbol am Schuljahr erklärt die acht Phasen: Entwurf → Verwaltung LK → 1. Anmeldephase → 1. Anmeldephase beendet → 2. Anmeldephase → Anmeldung geschlossen → In Durchführung → Abgeschlossen.
- Konfiguration in drei Schritten, laufend als Entwurf gespeichert: 1. Name, Dauer der Zeitblöcke, Online-Sprechtag; 2. Abschnitte (auch an mehreren Tagen) und die beiden Anmeldephasen (priorisiert/allgemein); 3. Rechte der Lehrkräfte (Räume wählen, Abwesenheiten, Termine bearbeiten & absagen, Termine buchen) und Buchungsregeln (Blöcke bis zur Pause, höchstens Blöcke je Schüler*in und Lehrkraft, gesperrte Blöcke zwischen zwei Terminen). „Veröffentlichen“ prüft alles und benachrichtigt die Lehrkräfte.
- Verwaltung für die Administration: Lehrkräfte mit Suche und Filtern „Kein Raum“/„Keine Termine“, je Abschnitt Raumwahl (Gebäude | Raum) und „+“ für Einladung senden, Termin buchen, Abwesenheit eintragen. Lehrkräfte sehen „Meine Termine“.
- Abwesenheiten sagen überschneidende Termine automatisch ab (mit Benachrichtigung). Einladungen ohne Uhrzeit – die Eltern wählen selbst; Terminanfragen mit fester Uhrzeit nehmen sie an oder lehnen sie ab („Terminiert“ → „Versendet“ → „Akzeptiert“).
- Erziehungsberechtigte: in der 1. Anmeldephase nur mit Einladung, ab der 2. frei bei allen Lehrkräften ihres Kindes; Terminanfragen, „Meine Termine“ nach Datum, Zeit ändern und Absagen. Schüler*innen haben keinen Zugriff mehr.
- Erinnerungen an fehlende Räume vor der 1. Anmeldephase, Versand der Einladungen zu deren Beginn, Hinweis auf ungenutzte Einladungen zur 2. Phase; unbeantwortete Anfragen werden beim Abschluss abgesagt.
- Terminpläne als PDF wie bisher: Drucken-Knopf im Kopf – Administration Gesamtplan (mit bzw. ohne freie Zeilen zum spontanen Eintragen) und je Lehrkraft über „+“ → „Terminplan drucken (PDF)“, Lehrkräfte ihr eigener Plan, Erziehungsberechtigte „Meine Termine“ als PDF.
- Hinweis: Sprechtage aus dem bisherigen Modul werden nicht übernommen.

### Web
- Raumsperren (Administration → Raumsperren): einzelne Räume, Raumgruppen, alle Räume, einzelne bzw. alle Ressourcen für Buchungen sperren – kontinuierlich (ohne Unterbrechung von Beginn bis Ende) oder regelmäßig im Zeitbereich an jedem bzw. einem Wochentag; mit Text, „Speichern & Neu“. Gesperrte Räume gelten bei der Suche als belegt und können nicht gebucht werden; im Raumplan steht im gesperrten Zeitraum nur eine graue Karte „Gesperrt“ (bzw. der Text der Sperre) über die ganze Breite, ohne Uhrzeiten.
- Buchen → Buchungen ent-/sperren: Buchungen für alle Benutzer sperren (die Administration bucht weiterhin).
- Durchstreichung nur noch bei Entfall: Fach, Raum und Klassen einer Stunde, die stattfindet (z. B. Lehrkraft abwesend), sind nicht mehr durchgestrichen – das Label der Lehrkraft bleibt durchgestrichen, solange keine Vertretung gefunden ist; auf dem Mac keine doppelte Linie.
- Nicht bestätigte Raumbuchungen (in UntPlanWeb gebucht, in UntPlan noch nicht übernommen) wie in der Vorlage: gestrichelte graue Karte mit grauem Balken und grünem „?“ oben rechts, Lehrkraft fett, darunter die Bezeichnung; Details mit Pille „Zusätzlicher Unterricht“, „Buchung (…)“ und „Information zur Stunde“.
- Stundenplan: Die Durchstreichung bei Entfall bzw. Abwesenheit sitzt jetzt genau auf halber Höhe der Großbuchstaben (eigene Linie statt Browser-Durchstreichung, die je nach Schrift gut ½ Pixel zu tief lag).
- Sprechtag: alle Größen (Schriften, Knöpfe, Felder, Karten, Abstände) auf das Maß der übrigen Seiten verkleinert; Platzhalter-Lehrkräfte ohne Vornamen und ohne Konto (z. B. „EVA“) stehen nicht mehr in der Verwaltung, im Gesamtplan und bei den Raum-Erinnerungen.
- Seitenleiste animiert wie in der Vorlage: Untermenü öffnen – das Menü gleitet nach links hinaus, das Untermenü kommt von rechts; zurück – das Hauptmenü kommt von links, die Punkte blenden nacheinander ein; der orange Aktiv-Balken gleitet zum neuen Punkt. Kein erneutes Einblenden mehr bei jedem Seitenwechsel.
- Anwesenheit: Die Schülerkacheln kennen nur noch anwesend/abwesend – Verspätung bzw. früher gegangen erscheint als anwesend (ohne Minuten, ohne eigene Farbe, Kachel bleibt gleich hoch) und steht nur unten in der Liste; „×“ macht daraus eine Abwesenheit.
- Vertretung ohne Lehrkraft: kein Info-Symbol und kein Vertretungstext „Vertr. o. Lehrkr.“ mehr (auch nicht aus älteren Daten) – nur von Hand eingegebene Texte erscheinen.
- Raum buchen: Knöpfe (Zurück, Weiter, Speichern …) gleich groß und rechts.
- Raum buchen wie in der Vorlage (im Grunddesign): „Buchung: Suche nach freien Räumen“ mit Zeitbereich (Einmalig, Täglich, Jede Woche … , Datum bzw. Zeitraum, Beginn- und Endzeit, „ganztägig“) und Auswahlkriterien (Ressourcenart, Raumart, Abteilung, Gebäude, Raumgruppe, Kapazität, maximale Belegung) → „Freie Räume“ als Tabelle mit „frei“ je Termin → „Buchung“ (Lehrkraft, Fach, Klassen, Titel, Anmerkung, Benachrichtigung, Hinweis auf Überschneidungen) → „Erfolgreiche Buchung“ mit PDF-Beleg und „Schließen“.
- Stundenplan: Bei mehreren Räumen steht der Raum mit Label vorne („[A103], C103“) – im Web und in der App.
- Stundendetails: Der Raumbereich (Ersetzen, Löschen, „+“) erscheint wieder nur in eigenen Stunden bzw. für die Administration in allen Stunden.
- Konferenz/Kopplung: Betreut eine Lehrkraft eine abwesende Kollegin und nimmt selbst regulär teil, steht sie auch im eigenen Plan zweimal da (schlicht und als Label).
- Handy: Die Weboberfläche erscheint wie am Rechner (Desktop-Ansicht mit Seitenleiste und ganzer Woche, Breite 1024 px, Zoomen möglich) – keine eigene Handy-Formatierung mehr.
- Stundenplan: Das Wochenraster reicht bis zum Seitenende – auf hohen Bildschirmen werden die Stunden entsprechend höher (mindestens wie bisher).
- Sprechtag: Übersicht, Konfiguration, Verwaltung und Elternansicht nutzen die volle Seitenbreite.
- Nach einem Update auf dem Server lädt die Seite beim nächsten Seitenwechsel automatisch neu – ein offener Tab zeigt nie mehr alten Code bzw. alte Stile (z. B. noch automatisches Speichern oder schmale Einstellungsseiten).
- Notizen und Link zum Online-Unterricht speichern nur über „Speichern“ (kein automatisches Speichern).
- Raum ersetzen/hinzufügen füllt das ganze Detailfenster wie in der Vorlage: eigener Kopf „HN011 ersetzen“ mit X (ohne Stundenkopf und Reiter); die Filter „Gebäude“ und „Abteilung“ sind immer da (Abteilung ohne gepflegte Angaben aus dem Kürzel abgeleitet).
- Verlegungen: der Raum der Zielstunde ist der Soll-Raum – kein Raum-Label mehr gegenüber dem Raum der Ursprungsstunde (in Web, App und Vertretungsplan); nur eine spätere Raumänderung der Zielstunde erscheint als Raumänderung.
- Stundendetails → Details: Notizen und Link zum Online-Unterricht wieder mit „Abbrechen“ und „Speichern“ in einem kleinen Kasten rechts unter dem Feld (wie in der Vorlage); Esc verwirft, beim Link speichert auch Enter.
- Stundendetails → Details: die Räume der Stunde stehen immer da (auch bei vergangenen Stunden); Ersetzen, Löschen und „+“ wie bisher nur, wo eine Raumänderung erlaubt ist.
- Lehrstoff direkt auf der Seite noch näher an der Vorlage: beim Überfahren Rahmen und Textcursor, das Eingabefeld erscheint an derselben Stelle.
- Unterstrich der Reiter oben abgerundet und unten gerade, Leiste am aktiven Menüpunkt zur Innenseite abgerundet.
- Startseite „Mein nächster Unterricht“ auf kleineren Monitoren: keine abgeschnittene Spalte „Klassenbuch“ mehr – der Lehrstoff nimmt nur den Restplatz und endet mit „…“ (vollständig im Hover), schmalere Abstände, unter 1300 px ohne den langen Fachnamen; im Notfall waagrecht scrollbar.
- Kopf der Stundendetails enger wie in der Vorlage: kleinere Abstände um die Trennstriche, dichtere Laufweite, Reiter „Klassenbuch“/„Details“ direkt nebeneinander.
- Seitenleiste: Menütexte grau (#67747e) und Medium statt dunkel und fett, wie in der Vorlage.
- Tag - Klassen/Lehrkräfte/Räume wie in der Vorlage: je Element eine Spalte mit senkrechter Zeitachse und denselben Karten wie im Wochenplan (waagrecht scrollbar); oben links blendet ein runder Knopf leere Spalten aus bzw. ein („Leere Spalten anzeigen“), die Wahl bleibt gespeichert.
- Nebeneinanderstehende Knöpfe sind immer gleich groß (z. B. „Filter zurücksetzen“ neben „Raum buchen“, „Export“ neben „Neue Buchung“).
- Gekürzte Texte auf den Karten, die beim Überfahren vollständig erscheinen, sind währenddessen leicht grau hinterlegt.
- Einstellungsseiten nutzen die volle Seitenbreite; der Haken sitzt genau mittig im orangen Kreis (auch bei allen übrigen Häkchen).
- Stundenplan-Menü wie in der Vorlage: kein „Vertretungsplan“ mehr (die Seite bleibt über die Übersicht erreichbar); dafür „Einstellungen“ (Stundenplaneinstellungen, Administration) direkt im Stundenplanmodul statt unter Administration.
- Zugriff ent-/sperren: derselbe Schalter wie „Findet online statt“ (grau aus, orange an).
- Stundendetails → Details: Notizen für Schüler*innen bzw. Lehrkräfte und der Link zum Online-Unterricht („Url hier eingeben“) ohne eigene Speichern/Abbrechen-Box – beim Bearbeiten nur orange umrandet, gespeichert wird beim Verlassen des Feldes (Esc verwirft).
- Lehrstoff direkt auf der Seite eingeben: „Klicken Sie hier, um den Lehrstoff einzugeben.“ bzw. Klick auf den Lehrstoff öffnet ein Eingabefeld mit „Speichern“ und „Abbrechen“ (Enter speichert, Esc bricht ab); der Stift öffnet weiterhin den Dialog mit Lehrstoff aus Vorwochen.
- Pausenaufsichten immer automatisch: Blöcke laufen über die Pause durch, die Aufsicht steht dort schmal daneben, sonst über die ganze Breite – die Auswahl „Pausenaufsichten“ im Menü „⋮“ entfällt.
- Seitenleiste wie in der Vorlage: Titel mittig neben dem Logo, Schulname in normaler Schreibweise einzeilig darunter, Jahr und Menüpunkte auf derselben Höhe.
- Stundendetails bei Entfall bzw. fremdem Unterricht (nicht Lehrkraft dieses Unterrichts): nur „Details“ und „Schüler*innen“ – Schüler*innen im Unterricht im neuen Design (Nr., Familienname, Vorname, Geschlecht, Klasse), E-Mail-Adressen zum Aufklappen, „Berichte“ (Drucken, CSV-Export).
- Raum ersetzen bzw. hinzufügen in den Stundendetails wie in der Vorlage: graue Suchpille „Suchen Sie einen Raum“, Filter-Chips „Gebäude“ und „Abteilung“ (gewählt dunkel, Auswahlmenü dunkel), nur freie Räume – je Zeile graue Scheibe mit Raumsymbol, Kürzel, Gebäude als dunkle Pille und Auswahlkreis rechts.
- Grunddesign auch beim Sprechtag und überall sonst: alle Kontrollkästchen und Schalter der Web-Oberfläche sind runde orange Häkchen, Auswahlpunkte orange gefüllt. Sprechtag: dünn umrandete Kästen ohne Symbol; „Sprechtag anlegen/bearbeiten“ mit schwebenden Labels an allen Feldern (auch Von/Bis der Zeitblöcke).
- Alle Einstellungsseiten im Grunddesign der Stundenplaneinstellungen: Klassenbuch-, Unterricht- und Noten-Einstellungen sowie Ansichtseinstellungen, Verspätungen & Fehlzeiten, Prüfungssperren, Zuordnungsgruppen, Monitor-Darstellung und „Quelle der Schülerdaten“ – unterstrichene Reiter, dünn umrandete Kästen ohne Symbol, Beschriftung links und Eingabe rechts, runde orange Häkchen statt Schalter, runde orange Auswahlpunkte, Felder mit schwebendem Label und „Speichern“ als Pille.
- Veranstaltung mit Termin: vertritt eine Teilnehmerin zusätzlich eine abwesende Kollegin, steht sie schlicht UND als Label auf der Karte („SN, [SN]“) – vorher fiel der schlichte Eintrag weg.
- Stundenplan → Menü „⋮“: neuer Abschnitt „Stundenplanformat“ – „Standard“ oder eines der angelegten Formate wählen; die Wahl gilt sofort und bleibt im Browser gespeichert.
- Stundenplaneinstellungen wie in der Vorlage: Reiter „Allgemein | Stundenplanformate | Sichtbarkeit einschränken | iCal“ mit orange unterstrichenem aktiven Reiter. „Allgemein“: Häkchen „Links zu Videokonferenzen in Stundendetails“ und je ein Kasten Klassen-, Lehrkraft-, Schüler- und Raum-Stundenplan mit dem Format (Schüler-Format gilt für Schüler*innen und Erziehungsberechtigte). „Stundenplanformate“: Liste mit Zähler, Sortierung nach Name und Knopf „Neu“; Zeile anklicken öffnet das Panel „Neues Stundenplanformat“ bzw. „Stundenplanformat bearbeiten“ mit runden orangen Häkchen, Zeitraster/tatsächliche Uhrzeit, Stundenname, Kärtchen-Elementen zum Ziehen (Pillen-Auswahl, Kurz-/Langname, X, „+“), Tagquadraten mit „Alle Tage des Zeitrasters“, Beginn-/Endzeit, Zusatzinformationen (Beginn-/Endzeit, Benutzer), Aktivitäten (auch nicht bestätigte Buchungen, externe Kalender) und Hervorhebungen.
- Sprechtag anlegen (Administration): neue Optionen „Abwesenheitseingabe durch die Lehrkraft erlauben“ (aus = nur die Administration sperrt Zeitfenster) und „Parallele Termine“ (Familien dürfen sich überschneidende Termine buchen, z. B. Geschwister bei verschiedenen Lehrkräften).
- Neu: Administration → Stundenplaneinstellungen mit Stundenplanformaten. Ein Format legt fest, was auf den Stundenkärtchen steht (1.–5. Element: Klasse, Lehrkraft, Fach, Raum, Notizen für Schüler*innen – je Kurzname, Langname oder „Langname (Kurzname)“), welche Tage und Uhrzeiten der Plan zeigt, ob Beginn-/Endzeit und Stundennummer erscheinen, ob die Stundendetails geöffnet werden können, welche Aktivitäten (entfallener Unterricht, Bereitschaften, Sprechstunden, Raumbuchungen) erscheinen und was hervorgehoben wird (Änderungen, Prüfungen, Entfälle, Symbole, Fachfarben). Unter „Allgemein“ wird je Plan (Klassen, Lehrkräfte, Räume) das Format gewählt – wahlweise nur für Schüler*innen und Erziehungsberechtigte oder auch für das Kollegium. Gilt im Web und in der App; ohne Format bleibt die Standarddarstellung.
- Sprechtag für Erziehungsberechtigte: „Termine wählen“ als Raster wie in der Vorlage – je Kind Zeitfenster (Zeilen) × Lehrkräfte (Spalten), freies Feld antippen bucht, grüner Haken = eigener Termin (antippen: absagen); daneben die gebuchten Termine. Umschaltbar „Alle Lehrkräfte“ / „Einladungen“ und zurück auf die Liste „Lehrkräfte“.
- Sprechtag „Gespräche wünschen“: Einladung mit Priorität – Knöpfe „Erwünscht“ und „Verpflichtend“, Spalte „Priorität“ in der Liste; Eltern sehen „Gespräch erwünscht“ bzw. „Gespräch verpflichtend“ (rot) bei der Lehrkraft (auch in der App), die Mitteilung nennt das verpflichtende Gespräch.
- Seitenleiste: Menüpunkte wie in der Vorlage – Text grau (#67747e) statt dunkel, Symbole etwas kleiner, Text 2 px weiter links, aktiver Punkt im Orange der Vorlage (#fe6033).
- Startseite „Heute“: Schriftgrößen und Farben wie in der Vorlage nachgemessen – Titel etwas kleiner, Datum hellgrau (#999), Anmeldung/Planaktualisierung und Nachrichten in 12 px dunkelgrau (#333), Kartenüberschriften etwas kleiner.
- Raumbuchung: bucht eine Lehrkraft für ihre Stunde einen anderen Raum, wird der ursprüngliche Raum in dieser Stunde frei und steht in der Buchung (Raumsuche, Belegung, freie Räume, Prüfungen) sofort wieder zur Verfügung.
- Durchgestrichene Labels (z. B. abwesende Lehrkraft, entfernter Raum) wie in der Vorlage: Linie genau mittig durch die Kürzel im Label.
- Startseite „Heute“: Lehrstoff in einer eigenen Spalte (neben Status und Klassenbuch).
- Menüpunkt „Übersicht“ nur noch für Schulleitung und Administration, nicht für Lehrkräfte.

### iOS
- Sprechtag neu (wie im Web): Erziehungsberechtigte sehen den Hinweis zur Phase, Terminanfragen (Termin buchen bzw. Annehmen/Ablehnen) und „Meine Termine“ nach Datum mit Raum, Absagen und Zeit ändern; ab der 2. Anmeldephase freie Buchung je Kind. Lehrkräfte: „Meine Termine“ je Abschnitt mit Raumwahl, „+“ für Einladung, Termin und Abwesenheit sowie Bearbeiten. Administration: Lehrkräfte mit Suche und Filtern.
- Startseite in den Größen der Vorlage: Schulname 26 pt fett, Tageskarte mit 21 pt Rand und mindestens 152 pt Höhe (Wochentag und Monat 13 pt halbfett, Tageszahl 34 pt, Stunden und Uhrzeiten 13 pt, 13 pt Abstand zwischen den Stunden), Liste mit 17-pt-Titeln und größeren Symbolen. Der Punkt „Klassenleitung“ entfällt – die Klassenleitung arbeitet über „Abwesenheiten“.
- Raum ersetzen/hinzufügen in denselben Größen wie die Raumsuche der Planauswahl: Suchfeld 44 pt, Filter „Abteilung“ als 42-pt-Kapsel mit Pfeil, Raumzeilen mit 44-pt-Scheibe, Kürzel 20 pt und Raumname als graues Badge; auch die Filter bei den Abwesenheiten (Zeitbereich, Status) sind 42 pt hoch.
- Schülerliste (Anwesenheitskontrolle) wie im Vorlage-Video: nach links wischen setzt „abwesend“ (rot), ein zweites Mal nach links während der laufenden Stunde „verspätet“ (gelbes Uhr-Symbol, „gekommen um …“); daneben „Verspätet“ bzw. „Entschuldigen“ als weitere Wischaktion. Nach rechts wischen setzt wieder „anwesend“.
- Startseite (Klassenleitung): eigener Punkt „Abwesenheiten“ mit der Zahl der offenen Abwesenheiten; die Liste startet wie in der Vorlage gefiltert auf „Offen“ (Menü „Status wählen“: Alle, Offen, Unentschuldigt, Entschuldigt). Im Dialog öffnet „Entschuldigungsstatus“ die Seite „Entschuldigungsstatus wählen“ (gewählter Status mit orangem Haken); unten steht der Abwesenheitsgrund.
- Raum ersetzen/hinzufügen wie in der Vorlage: Titel „R1BK ersetzen“, Suchfeld, Kapsel „Abteilung“, je Raum eine graue Scheibe mit Raumsymbol und das Kürzel.
- Klassenleitung → Abwesenheiten wie in der Vorlage: Suchfeld, Filter „Zeitbereich“ und „Status“, Gruppen „Offen“, „Unentschuldigt“, „Entschuldigt“; je Zeile Avatar, Name, Klasse, Datum und Zeit, Grund und rechts ein roter bzw. grüner Rand. „Abwesenheit bearbeiten“ mit START/ENDE, Zeile „Entschuldigungsstatus“ und „Text für Entschuldigung“.
- Start → „Klassenleitung“: Übersicht der eigenen Klasse mit „Abwesenheiten“ und der Zahl der unentschuldigten Abwesenheiten. Die Abwesenheiten stehen als Karten (Datum und Zeit, Art, Status) unter UNENTSCHULDIGT bzw. ENTSCHULDIGT; „nicht entschuldigt“ rutscht rot markiert nach unten, „entschuldigt“ ist grün markiert. Antippen öffnet die Entschuldigung.
- „Bündeln“: mehrere Abwesenheiten einer Person zu einer gebündelten Fehlzeit zusammenfassen, die dann als Ganzes entschuldigt oder nicht entschuldigt wird. Zeitraum über den Filter oben rechts (30, 90 oder 180 Tage).
- Schülerliste: Wischen nach rechts/links mit den Wischaktionen des Systems – das Scrollen der Liste und das seitliche Blättern zwischen Stunden hängen nicht mehr.
- Stunden im Plan lassen sich immer antippen: kleine Fingerbewegungen beim Tippen gelten nicht mehr als Wischen und sperren das Öffnen nicht.
- Kopplungen und Konferenzen: die Lehrkräfte stehen in einer Zeile (kein Umbruch); passt die Liste nicht, stehen so viele ganze Kürzel wie möglich und dahinter sauber „…“ (nie ein halb abgeschnittenes Kürzel oder Label); mehrere Klassen stehen nebeneinander in einer Zeile; im eigenen Lehrerplan steht auch das eigene Kürzel.
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
