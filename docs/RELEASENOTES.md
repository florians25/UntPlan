UntPlan – Stunden- und Vertretungsplanung für Schulen in NRW.

**Installation:** `UntPlan-Setup-*.exe` herunterladen und ausführen (Windows 10/11, 64 Bit, keine .NET-Installation nötig).
Ohne Installation: `UntPlan-portabel.zip` entpacken und `UntPlan.exe` starten.


## Neu in dieser Version

### Desktop – Masken wie im Handbuch
- Menüband Vertretungsplanung neu geordnet: Kalender mit Datumsfeld darunter und Monatskalender, Menüs bei Absenzen, Vertretungen, Ausgabe und Statistik, neue Knöpfe Vertretungsformate, Info-Stundenplan und Einstellungen.
- Tastenkürzel im Vertretungsmodus: Strg+T heute, Strg+M nächster Schultag, Strg+G voriger Schultag.
- Neue Fenster Zeitraster und Klassenzeitraster (Vertretung/Entfall mit der Maus eintragen).
- Kalender mit Spalten Abw.Le., Vertr., Offen, PA-Vertr., Abw.Kl., Freis., Raum-Vtr., Kollisionen und einklappbarem Detailbereich.
- Absenzen: Absenzeingabe im Schuljahreskalender, „Absenz splitten“, Kopieren mit Strg+C/Strg+V, Rückfrage zum Zusammenfassen; Von-Bis mit Wochentag und kleinen Pfeilen.
- Kommentare an Absenzen und Veranstaltungen (farbiges Dreieck, Tooltip, Übersicht).
- Vertretungsvorschlag: Spalten Jahres-Zähler, Unt./Tag, Statistik, Sperrvermerk und Vorwoche (grün mit Haken), eigener Einstellungsdialog mit Gewichtung, Vorziehungen und Betreuungen überarbeitet.
- Seitenlayout des Vertretungsplans mit vollständiger Symbolleiste, Feldern per Drag&Drop, Sortierkriterien, Absenzkopf und HTML-Ausgabe.
- Klausuren, Vormerkungen und Sondereinsätze (inkl. Klassenkonferenz) neu aufgebaut.
- Einstellungen der Vertretungsplanung als Baum mit Vertretungszähler; neue Vertretungsstatistik mit Grafiken und Berichten (Abrechnung, Entfalltage, Vertretungs-Übersicht, Mehrarbeit), Wochenwerte und Vertretungs-Diagnose.
- Stammdaten mit Filterzeile, Formaten, Serienänderung und Karteikarten; Unterrichtsfenster (Spaltenfolge Lehrer, Fach, Klasse(n)), Diagnose, Gewichtung und Optimierung wie im Handbuch.
- Stundenplanfenster mit neuer Symbolleiste, Stundenplan-Einstellungen mit allen Karteikarten, Seitenlayout für Stundenpläne, neue Fenster Lehrer zuordnen, Formate / Stundenpläne und Fixierter/Ignorierter Unterricht.

### UntPlanWeb
- Entfall-Dreieck und Verlegungs-Marker abgerundet wie Vorlage, Seitenleiste und Stundendetails („Keine weiteren Details“) nach Vorlage.

## Vorherige Version


### Desktop
- Wichtige Korrektur: Wird eine Lehrkraft abwesend, deren Stunde schon eine Raumänderung hat (z. B. gesperrter Raum mit gewähltem Ausweichraum), erscheint die Stunde jetzt als offene Vertretung (bzw. Entfall laut Regel) – der gewählte Raum bleibt. Bestehende Dateien werden beim Öffnen korrigiert.
- Raumänderungen in Kopplungen gelten für alle Zeilen und Klassen; die Vertretungsliste fasst sie in einer Zeile mit allen Klassen zusammen, Pläne zeigen einen Block mit allen Klassen.
- Aufsichtsplanung im Vertretungsmodus: Das Fenster Pausenaufsichten zeigt die Woche des Planungsdatums; jede Änderung (andere Lehrkraft, offen „???“, entfällt „---“, Lehrer-Vorschlag mit Übernahme, offene Aufsichten der Woche besetzen) wird als Vertretung dieses Tages erfasst – der reguläre Aufsichtsplan bleibt unverändert.
- Kompakte Warnhinweise direkt neben der Maus (Fach 2x/Tag, fixierter Unterricht, gesperrter Raum) mit „nicht mehr anzeigen“.
- Neues Farbschema für Stunden- und Vertretungsmodus, kleine Stundenpläne mit fetten Tagen und Stunden.
- Klassenlehrkräfte per Dropdown (auch mehrere, durch Komma getrennt).

### UntPlanWeb und Apps
- Vorgemerkte Raumänderungen machen die Stunde zur grünen Änderungskarte.
- Entfallene Stunden und Stundendetails nach Vorlage; verlegte Stunden mit Pille „Zusätzliche Unterrichtsstunde“.
- iOS: Ladebild in den Stundendetails (flüssigeres Öffnen und Schließen), Fachfarbe als Balken bzw. unter dem gewählten Reiter, mehrere Klassenlehrkräfte, breitere Fenster auf dem iPad, kein Lehrer-Label bei Klassenabwesenheit oder Verlegung, schnellere Animationen.
