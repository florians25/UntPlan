# UntPlan – Benutzerhandbuch

Das Handbuch folgt dem Arbeitsablauf einer Stundenplanerstellung: Schuldaten → Stammdaten → Unterricht → automatische Planung → manuelles Planen → Stundenpläne → Vertretungsplanung → Module.
In der Anwendung erreichen Sie die passende Hilfeseite jederzeit mit **F1**.

> Die Bildschirmfotos stammen aus dem automatischen Selbsttest (`UntPlan.exe --selbsttest=…`) mit der Beispielschule. Sie wurden unter Wine aufgenommen, deshalb sehen Schriften und Fenstertitel unter Windows etwas anders aus.

## Inhalt

1. [Erste Schritte](#1-erste-schritte)
2. [Schuldaten](#2-schuldaten)
3. [Stammdaten](#3-stammdaten)
4. [Unterricht](#4-unterricht)
5. [Automatisches Verplanen](#5-automatisches-verplanen)
6. [Manuelles Planen](#6-manuelles-planen)
7. [Stundenpläne](#7-stundenpläne)
8. [Vertretungsplanung](#8-vertretungsplanung)
9. [Module](#9-module)
10. [Import, Export und WebPortal](#10-import-export-und-webportal)
11. [Tastenkürzel](#11-tastenkürzel)

---

## 1. Erste Schritte

### Programmoberfläche

![Arbeitsbereich](bilder/01_Arbeitsbereich.png)

| Bereich | Beschreibung |
|---|---|
| Schnellzugriff (oben links) | Neu, Öffnen, Speichern, Rückgängig, Wiederholen, Drucken, Seitenansicht, Element-Rollup |
| Menüband | Registerkarten **Start**, **Dateneingabe**, **Planung**, **Stundenpläne**, **Kursplanung**, **Vertretungsplanung**, **Module**; links das Menü **Datei** |
| Arbeitsbereich | beliebig viele Fenster, die Sie verschieben und in der Größe ändern können; *Start → Standard-Fenstergruppe* stellt die abgebildete Anordnung her |
| Eingabeassistent (rechts) | Schritt-für-Schritt-Anleitung der Ersteingabe; ein- und ausblenden unter *Start → Hilfethemen* |
| Statuszeile | aktive Periode sowie Anzahl der Klassen, Lehrer und Unterrichte |

### Neue Schuldatei

*Datei → Neu*: Geben Sie den Schulnamen ein und wählen Sie die NRW-Schulform. UntPlan übernimmt dabei die NRW-Ferien und Feiertage sowie den NRW-Fächerkatalog (inkl. Bereitschaft BER).
*Datei → Beispielschule öffnen* lädt ein vollständiges Gymnasium zum Ausprobieren.

Eine Schuldatei (`*.untplan`) enthält alle Daten eines Schuljahres. Beim Speichern legt UntPlan automatisch eine Sicherung `*.bak` an.
Jede Änderung lässt sich mit **Strg+Z** rückgängig machen (50 Schritte).

---

## 2. Schuldaten

### Schuljahr und Einstellungen

*Start → Einstellungen*: Schulname, Schulnummer, Schulform, Region, Schuljahr von/bis und Wochenperiodizität (2 = A/B-Wochen).
Weitere Karteikarten:

- **Werte:** Wertrechnung, tagesgenau.
- **Vertretungsplanung:**
  - Mehrarbeitsgrenze, Standard 3 Stunden je Kalendermonat.
  - Entfall ab Jahrgang, Standard EF.
  - Titel des Vertretungsplans.
- **Diverses**

![Einstellungen](bilder/14_Einstellungen.png)

### Ferien

*Start → Kalender/Ferien*: Mit **NRW-Ferien und Feiertage eintragen** übernehmen Sie alle amtlichen Termine. Weitere freie Tage, z. B. bewegliche Ferientage, ziehen Sie mit gedrückter Maustaste im Jahreskalender auf.
Grün dargestellt sind Ferien, rot Feiertage und grau Wochenenden.

![Ferien](bilder/15_Ferien.png)

### Zeitraster

*Dateneingabe → Zeitraster*:

- Tage pro Woche, Stunden pro Tag, letzte Vormittagsstunde.
- Beginn und Ende jeder Stunde.
- Belegung jeder Stunde: Vormittag, Nachmittag oder leer (Mittagspause).

Auf der Karteikarte **Pausen** tragen Sie die Pausen mit Dauer ein. Ist **Aufsicht** markiert, plant das Modul Pausenaufsichten diese Pause.

![Zeitraster](bilder/13_Zeitraster.png)

---

## 3. Stammdaten

Stammdaten sind die Grundelemente der Planung. Für jedes Element gibt es ein Stammdatenfenster mit **Rasteransicht** (Tabelle) und **Formularansicht** (Karteikarten).

- **Neues Element:** in die letzte, leere Zeile schreiben.
- **Umbenennen:** Doppelklick auf den Kurznamen. Alle Verweise werden mitgeführt.
- **Löschen:** Entf bzw. Schaltfläche. UntPlan zeigt, wo das Element verwendet wird.
- **Felder:** Ziehen Sie ein Feld per Drag & Drop aus der Formularansicht in die Tabelle, um es anzuzeigen; mit Strg+Ziehen blenden Sie es aus. Alternativ wählen Sie die Spalten unter *Felder der Ansicht*.
- **Ansichten:** Spaltenauswahl, Breiten und Sortierung lassen sich unter einem Namen speichern.
- **Sortieren:** mit bis zu drei Kriterien; *Umreihen* speichert die Reihenfolge dauerhaft.
- **Serienänderung:** mehrere Zeilen markieren und einem Feld für alle denselben Wert geben.
- **Kommentare:** über die rechte Maustaste. Kommentierte Felder tragen ein rotes Dreieck.

![Klassen](bilder/02_Klassen.png)

### Räume

![Räume](bilder/04_Raeume.png)

- **Ausweichraum:** Ist der gewünschte Raum belegt, wird der Ausweichraum verwendet. Mehrere Ausweichräume können einen **Ring** bilden (R101 → R102 → … → R101). Die Optimierung nutzt dann jeden freien Raum des Rings.
- **Wanderklassen:** Klassen ohne festen Stammraum erhalten einen freien Raum aus einem Ausweichraum-Ring.
- **Raumgewicht 0–4:** 0 bedeutet, der Unterricht darf auch ohne Raum stattfinden; 4 bedeutet, der Raum ist unverzichtbar (z. B. Sporthalle).
- **Raumgruppen:** gleichwertige Fachräume (EDV1, EDV2). Als Fachraum im Unterricht wählt die Optimierung einen freien Raum der Gruppe.
- **Raum sperren:** im Zeitwunschfenster den Wert **−3** eintragen.

### Klassen

Stammraum, Jahrgang, Klassenlehrer, Hauptfächer pro Tag und hintereinander, Mittagspause (min./max.), max. Nachmittage, Stunden pro Tag.
Die **Kernzeit** (Zeitwunsch +3) markiert Stunden, die unbedingt belegt sein sollen.

### Lehrer

![Lehrer](bilder/03_Lehrer.png)

- **Wochensoll, Ermäßigungen und Lehrbefähigung (Fächer):** relevant für die Wertrechnung und den Lehrervorschlag.
- **Planung:** Hohlstunden min.–max., Stunden pro Tag, max. Nachmittage, Mittagspause.
- **Vertretung:** max. Vertretungen pro Woche/Monat, „keine Vertretung“.
- **Pausenaufsichten:** Aufsichten min./max., „keine Aufsicht“.

### Fächer

![Fächer](bilder/05_Faecher.png)

Fachraum oder Fachraumgruppe, Hauptfach, Werte-Faktor, Doppelstunden, „mehrmals pro Tag“, „Randstunde“, „kein Nachmittag“, Farbe.

**Bereitschaft:** Ein Fach mit dem Merkmal *Bereitschaft* (im NRW-Katalog: **BER**) ist kein echter Unterricht.

- Bereitschaftsstunden werden wie Unterricht verplant.
- In dieser Stunde steht der Lehrer für Vertretungen zur Verfügung und wird im Vertretungsvorschlag bevorzugt.
- Bereitschaftsvertretungen zählen **nicht** als Mehrarbeit.

### Zeitwünsche

![Zeitwünsche](bilder/06_Zeitwuensche.png)

| Wert | Bedeutung |
|---|---|
| −3 | gesperrt (Stunde darf nicht belegt werden) |
| −2, −1 | möglichst nicht |
| 0 | neutral |
| +1, +2 | gern |
| +3 | unbedingt (Kernzeit) |

Wählen Sie den Wert oben und überstreichen Sie die Stunden mit gedrückter Maustaste. Ein Klick auf den Tagesnamen belegt den ganzen Tag, die Spalten **Vorm.** und **Nachm.** belegen nur den Vor- bzw. Nachmittag.

Im unteren Teil werden **unbestimmte Zeitwünsche** eingegeben, z. B. „1 Vormittag frei, Gewicht −2“: Der Lehrer möchte einen freien Vormittag, egal welchen.

---

## 4. Unterricht

![Unterricht](bilder/07_Unterricht.png)

*Dateneingabe → Klassen → Unterricht*:

- **Spalten:** U-Nr. (wird automatisch vergeben), Wst. (Wochenstunden), Lehrer, Fach, Klasse(n), Fachraum, Stammraum, Doppelstunden, Block, Text.
- **Raumvorschlag:** Fachraum (aus dem Fach) und Stammraum (aus der Klasse) werden automatisch vorgeschlagen.
- **Spalte Nvpl.:** zeigt die nicht verplanten Stunden. Ziehen Sie eine Zahl in einen Stundenplan, um die Stunde dort zu verplanen.

Unterrichtsformen:

- **Kopplung:** Mehrere Lehrer und Klassen unterrichten gleichzeitig, z. B. Religion KR/ER/PP für 6a und 6b. Fügen Sie dafür Kopplungszeilen hinzu; mit **⊞** blenden Sie die Zeilen ein und aus.
- **Doppelstunde:** Feld Doppelstd. min–max, z. B. `1-1`.
- **Block:** z. B. `3` für Dreierblöcke.
- **Band:** Unterrichte mit gleichem Band (z. B. Kursschiene `EF-1`) werden immer parallel gelegt.
- **A/B-Woche, befristet von–bis, Werte-Faktor:** in der Formularansicht.

**Lehrervorschlag:** Für die markierte Kopplungszeile schlägt UntPlan Lehrer mit passender Lehrbefähigung und freiem Deputat vor.

---

## 5. Automatisches Verplanen

### Gewichtung

![Gewichtung](bilder/12_Gewichtung.png)

*Planung → Gewichtung*: 26 Kriterien in den Karteikarten Lehrer, Klassen, Fächer, Hauptfächer, Räume, Stundenverteilung und Zeitwünsche. Jedes Kriterium hat ein Gewicht von 0 (unwichtig) bis 5 (extrem wichtig).

- *Gewichtungs-Verteilung* zeigt die Gewichte im Verhältnis zueinander.
- *Analyse* nennt die Kriterien, die mit den aktuellen Daten schwer zu erfüllen sind.

Tipp: Verwenden Sie die Stufen 4 und 5 sparsam.

### Optimierung

*Planung → Optimierung*:

| Strategie | Verwendung |
|---|---|
| A – Schnelloptimierung | erster Überblick, wenige Sekunden bis Minuten |
| B – Normaloptimierung | Standard |
| D – Intensivoptimierung | mehrere Pläne mit Tauschoptimierung |
| E – Nächtliche Optimierung | lange Laufzeit, viele Pläne |

Optional stellen Sie ein:

- Anzahl der Pläne.
- Optimierungsschritte 1–9.
- Start- und Erhöhungsprozentsatz für schrittweises Verplanen.

Nach dem Lauf erscheinen alle Pläne mit **Bewertungszahl** und Kennzahlen (nicht verplant, Hohlstunden, Zeitwunschverletzungen …). Kleiner ist besser. Der beste Plan ist gelb markiert und wird mit **OK** übernommen.
Fixierte Stunden (*) bleiben unverändert.

Zur Mittagspause: Mit *fix* ist eine Stunde immer frei. Mit *flexibel* bleibt innerhalb des Bereichs (z. B. 6.–7. Stunde) mindestens eine Stunde frei.

### Diagnose

![Diagnose](bilder/11_Diagnose.png)

*Planung → Diagnose*: Die Karteikarte **Eingabedaten** prüft vor der Optimierung, z. B. Lehrer mit mehr Stunden als verfügbaren Zeiten, Unterricht ohne Lehrer oder Raum und Soll-Ist-Differenzen.
Die Karteikarte **Stundenplan** listet die Verstöße des aktuellen Plans. Rechts stehen Beschreibung und Einzelfälle; ein Doppelklick öffnet den betroffenen Stundenplan.

---

## 6. Manuelles Planen

![Klassenplan](bilder/08_Stundenplan_Klasse.png)

### Verplanen, Verschieben, Tauschen

Ziehen Sie eine Stunde aus der Liste *Nicht verplant* oder aus dem Plan auf ein Feld. Während des Ziehens sind die Felder eingefärbt:

| Farbe | Bedeutung |
|---|---|
| grün | Verplanung ohne Kollision möglich |
| rot | kollisionsfrei, aber Vorgabe verletzt (Sperrung, negativer Zeitwunsch) |
| violett | kollisionsfrei, aber kein passender Raum frei |
| grün hinterlegte Stunde | Tausch möglich |
| ohne Farbe | Kollision |

Wird auf eine belegte Stunde gezogen, fragt UntPlan nach **Tauschen** oder **Verdrängen** (die behindernde Stunde wird entplant).

- **Fixieren** (*): Die Stunde wird von der Optimierung nicht verändert.
- **Entplanen** (Entf): Die Stunde wandert zurück in die Liste der nicht verplanten Stunden.
- **Tauschvorschläge:** Für die markierte Stunde zeigt UntPlan alle kollisionsfreien Tausche, sortiert nach **Gewinn** (Verbesserung der Bewertungszahl). Ein Doppelklick führt den Tausch aus.

### Raumzuordnung

*Raum zuordnen* listet alle Räume mit ihrem Status (frei, belegt, Ausweichraum, Raumgruppe). Ist der gewählte Raum belegt, haben Sie drei Möglichkeiten:

- **Verdrängen:** Der andere Unterricht verliert den Raum.
- **Kollision zulassen.**
- **Räume tauschen.**

In der **Übersicht** (Raumplan quer) verlegen Sie Räume per Drag & Drop.

![Übersicht](bilder/10_Uebersicht.png)

---

## 7. Stundenpläne

- **Planarten:** Klassen-, Lehrer-, Raum- und Fachpläne, jeweils normal oder groß.
- **Auswahl:** oben im Fenster. Mit **Synchronisation** folgt der Plan der Auswahl in anderen Fenstern.
- **Woche:** Mit *Woche* sehen Sie den Plan einer bestimmten Kalenderwoche mit eingetragenen Vertretungen.
- **Stundenlupe:** zeigt unten alle Details der markierten Stunde.
- **Formate:** In den *Stundenplaneinstellungen* legen Sie Schriftgröße, Farben, Zeitanzeige und die angezeigten Felder fest. Im Grafik-Editor *Stpl-Stunde* positionieren Sie die Felder einer Stunde frei, mit Hilfslinien.
- **Ausgabe:** Druck (mit Druckauswahl mehrerer Elemente), HTML für die Homepage, Info-Stundenplan (alle Pläne als Webseiten), iCal-Kalender je Lehrer.

![Lehrerplan](bilder/09_Stundenplan_Lehrer.png)

---

## 8. Vertretungsplanung

### Abwesenheiten

![Abwesenheiten](bilder/17_Abwesenheiten.png)

*Vertretungsplanung → Abwesenheiten*: Geben Sie Lehrer-, Klassen- und Raumabwesenheiten mit Datum, Stunde von/bis, Grund und Text ein.

- **Klassenabwesenheit:** Begleitlehrer gelten ebenfalls als abwesend. Der Unterricht der Klasse wird zur **Freisetzung**; die freigesetzten Lehrer stehen für Vertretungen zur Verfügung (Minusstunden).
- Mit dem Speichern erzeugt UntPlan sofort die **offenen Vertretungen** (rot `???`).

### Vertretungsfenster

![Vertretungen](bilder/16_Vertretungen.png)

Arbeitsablauf:

1. Tag wählen. Oben sehen Sie Wochentyp, Anzahl offener Vertretungen sowie abwesende Lehrer und Klassen.
2. Offene Vertretung markieren. Der **Vertretungsvorschlag** zeigt alle freien Lehrer mit Punkten und Kriterien:
   - Bereitschaft, freigesetzt, kennt die Klasse, Fachlehrer.
   - Lage: Hohlstunde, anschließend, verlängert den Tag, sonst frei.
   - Vertretungen in Woche, Monat und Jahr, Saldo.
3. Mit Doppelklick oder **Vertreter zuweisen** übernehmen. Alternativ:
   - **Entfall** – UntPlan schlägt ihn ab Jahrgang EF in Randstunden vor.
   - **Betreuung**, **Raumvertretung** (Liste freier und frei gewordener Räume), **Verlegung** (mit Prüfung, ob Lehrer und Klasse frei sind).
   - **Sondereinsatz**, **Klausur**.
4. **Automatische Vertretung des Tages** plant alle offenen Vertretungen nach dem Vorschlag.

Die Spalten **Vertreter**, **Fach**, **Raum** und **Text** können Sie direkt überschreiben.

- **Bereitschaft:** Lehrer mit Bereitschaftsstunde stehen grün ganz oben; ihre Vertretung ist keine Mehrarbeit.
- **statt-Vertretung:** Ein freigesetzter Lehrer vertritt.
- **Vertretungsgewichtung** (Menüband): Hier legen Sie fest, wie stark jedes Kriterium im Vorschlag zählt (0–5).
- **Pausenaufsicht-Vertretungen:** Fällt eine Aufsicht durch Abwesenheit aus, erscheint sie auf der gleichnamigen Karteikarte. Dort wählen Sie einen Vertreter.
- **Tagestext:** Nachrichten zum Tag für alle oder nur für Lehrer.

### Pausenaufsichten

![Pausenaufsichten](bilder/18_Pausenaufsichten.png)

Zeilen = Aufsichtsbereich (Gang) × Aufsichtspause, Spalten = Tage. Geben Sie Lehrer-Kürzel ein oder lassen Sie **Pausenaufsichten optimieren** die Einteilung vornehmen. Berücksichtigt werden:

- Anwesenheit des Lehrers vor oder nach der Pause.
- Zeitwünsche.
- Minimal- und Maximalwerte der Lehrer.

Farben: rot = Bedarf nicht gedeckt, violett = Lehrer eigentlich nicht verfügbar.

### Statistik und NRW-Mehrarbeit

![Statistik](bilder/19_Statistik.png)

Je Lehrer zeigt die Statistik Vertretungen, Betreuungen, Sondereinsätze, Pausenaufsichten, Mehrarbeit, Entfälle, Freisetzungen, Saldo und **vergütungsfähige Mehrarbeit nach NRW-Regel**:
Werden in einem Kalendermonat mehr als 3 Stunden Mehrarbeit geleistet (Grenze einstellbar, Minderstunden werden verrechnet), sind alle Stunden vergütungsfähig.
Die Liste lässt sich drucken oder als CSV exportieren.

### Vertretungsplan ausgeben

- **Druck:** Datei → Drucken im Vertretungsfenster.
- **HTML:** Schüler- und Lehrerversion, die Lehrerversion mit abwesenden Lehrern und Pausenaufsichten.
- **Monitor:** Vollbild für Info-Bildschirme mit heute und dem nächsten Schultag, automatischem Seitenwechsel und Aktualisierung. ESC beendet.

![Monitor](bilder/27_Monitor.png)

---

## 9. Module

### Unterrichtsplanung und Wertrechnung

![Wertrechnung](bilder/20_Wertrechnung.png)

- **Lehrer Soll/Ist:** Soll = Wochensoll − Ermäßigungen. Ist = Summe der Werte (Wochenstunden × Werte-Faktor, anteilig bei A/B-Wochen und befristetem Unterricht) + Wertkorrektur. Wochensoll und Wertkorrektur sind direkt editierbar.
- **Unterrichts-Matrix:** Klassen × Fächer mit Lehrern und Stunden.
- **Stundentafel:** Soll-Stunden je Jahrgang und Fach mit Abgleich gegen den eingegebenen Unterricht.

![Stundentafel](bilder/22_Stundentafel.png)

### Kursplanung (Oberstufe)

![Kursplanung](bilder/23_Kursplanung.png)

- **Kurse:** Unterrichte mit Kursname, Band und Teilnehmerzahl.
- **Kurswahlen:** Schüler × Kurse zum Ankreuzen.
- **Kollisionsmatrix:** Anzahl der Schüler mit beiden Kursen. Rot bedeutet Kollision im selben Band.
- **Bandbildung:** Die Kurse werden automatisch auf möglichst wenige Bänder verteilt, ohne Schüler- oder Lehrerkollision. *Bänder übernehmen* schreibt das Ergebnis in den Unterricht.

### Perioden

Verschiedene Stundenpläne für Abschnitte des Schuljahres, z. B. ab dem 2. Halbjahr. Neue Perioden kopieren den Plan der aktuellen Periode.

### Abteilungsplanung

Exportieren Sie eine Abteilungsdatei, planen Sie die Abteilung getrennt und führen Sie das Ergebnis wieder zusammen. Elemente und Unterrichte werden über das Feld *Abteilung* zugeordnet.

---

## 10. Import, Export und WebPortal

| Funktion | Format |
|---|---|
| Lehrer-, Klassen-, Schülerimport | CSV (z. B. aus SchILD-NRW), Spalten werden anhand der Überschriften erkannt |
| Umstieg aus anderen Programmen | GPU-Dateien (GPU001–GPU006) |
| Export | GPU, HTML-Stundenpläne, Info-Stundenplan, Vertretungsplan, iCal, CSV |

**WebPortal** (*Module → Web-Portal*): Tragen Sie Adresse, Schul-ID und API-Schlüssel ein. **Synchronisieren** lädt Stundenplan, Vertretungen, Tagestexte und Ferien hoch und übernimmt Krankmeldungen aus dem Portal als Abwesenheiten.
Die Schnittstelle ist in [SYNC_API.md](SYNC_API.md) beschrieben.

---

## 11. Tastenkürzel

| Taste | Funktion |
|---|---|
| F1 | Hilfe zum aktiven Fenster |
| Strg+N / Strg+O / Strg+S | Neu / Öffnen / Speichern |
| Strg+Z / Strg+Y | Rückgängig / Wiederholen |
| Strg+P | Drucken |
| Entf | Element löschen bzw. Stunde entplanen |
| F | markierte Stunde fixieren / Fixierung lösen |
| Doppelklick auf Kurzname | Umbenennen |
| Strg+Ziehen eines Feldes | Feld aus der Rasteransicht entfernen |
