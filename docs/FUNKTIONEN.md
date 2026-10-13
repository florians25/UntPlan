# Funktionsumfang von UntPlan

Gegenüberstellung typischer Module einer Stundenplan-Software (wie sie u. a. Untis anbietet)
mit dem Stand von UntPlan 0.1. ✅ = vorhanden · 🟡 = Grundfunktion vorhanden · 🔜 = geplant

| Bereich | Funktion | Stand |
|---|---|---|
| **Stammdaten** | Lehrkräfte (Kürzel, Deputat, Anrechnungen, Lehrbefähigungen, Sperrzeiten, Bereitschaft, Tageshöchststunden) | ✅ |
| | Klassen/Stufen (Jahrgang, Klassenleitung, Stammraum, Sperrzeiten, Hohlstunden-Regel) | ✅ |
| | Fächer (NRW-Fächerkatalog, Farben, Fachraumbedarf, Hauptfach, max. Std./Tag) | ✅ |
| | Räume (Typ, Kapazität, Sperrzeiten, Auslastung) | ✅ |
| | Zeitraster (5/6 Tage, frei definierbare Stunden, Mittagspause), Pausen | ✅ |
| | Ferien NRW, gesetzliche Feiertage NRW (berechnet), bewegliche Ferientage, Jahreskalender | ✅ |
| **Unterrichtsplanung** | Stundentafel je Jahrgang mit Soll/Ist-Abgleich | ✅ |
| | Unterrichtsverteilung (Lehrereinsatz) mit Kopplungen, Teamteaching, Teilgruppen, Halbjahr | ✅ |
| | Unterricht aus Stundentafel erzeugen, Lehrkräfte automatisch vorschlagen | ✅ |
| | Deputatsübersicht (Pflicht, Anrechnung, Soll, Ist, verplant) | ✅ |
| | Gymnasiale Oberstufe (APO-GOSt): EF/Q1/Q2, GK/LK/ZK/VTF/PJK, Schienen/Bänder, Kollisionsprüfung | ✅ |
| | Kurswahlen der Schülerinnen und Schüler, automatische Blockbildung | 🔜 |
| **Stundenplan** | Automatische Erstellung (Heuristik + Simulated Annealing) im Hintergrund | ✅ |
| | Harte Regeln: Kollisionen Lehrer/Klasse/Raum, Sperrzeiten, Fachraum-Kapazität, Schienen parallel | ✅ |
| | Weiche Regeln: Hohlstunden (Klassen/Lehrkräfte), Doppelstunden, Fachverteilung, Randstunden, Mittagspause, Tagesbelastung | ✅ |
| | Fixieren von Stunden, Teiloptimierung je Klasse, bestehenden Plan weiterentwickeln | ✅ |
| | Manuelle Bearbeitung per Drag & Drop mit Live-Konfliktanzeige, Raumzuweisung | ✅ |
| | Ansichten Klasse/Lehrkraft/Raum, Druck/PDF einzeln oder gesammelt | ✅ |
| | Diagnose (Konflikte, Warnungen, Hinweise mit Sprung zur Ursache) | ✅ |
| | Rückgängig/Wiederholen (100 Schritte) | ✅ |
| | Periodenpläne / Wochen mit A/B-Rhythmus | 🔜 |
| **Pausenaufsichten** | Bereiche, Bedarf, faire automatische Verteilung, Druck | ✅ |
| | Aufsichtsvertretung bei Abwesenheit | ✅ |
| **Vertretungsplanung** | Abwesenheiten Lehrkräfte/Klassen/Räume (stundengenau, mehrtägig, Begleitlehrkräfte) | ✅ |
| | Automatische Ermittlung betroffener Stunden, Freisetzungen bei Klassenabwesenheit | ✅ |
| | Bewertete Vertretungsvorschläge (Bereitschaft, Freisetzung, Klassenkenntnis, Fach, Randstunde, Wochenlast, Teilzeit) | ✅ |
| | Vertretung, Entfall (Randstunden, ab Jahrgang), EVA, Mitbetreuung, Raumänderung, Verlegung, Sondereinsatz | ✅ |
| | Automatische Vertretungsplanung für einen Tag | ✅ |
| | Nachrichten zum Tag (öffentlich / intern) | ✅ |
| | Vertretungsplan: Aushang/PDF, Lehrerversion, CSV, Monitor-/Infoscreen-Ansicht | ✅ |
| **Statistik** | Mehrarbeit je Kalendermonat (NRW-Grenze konfigurierbar), Minderarbeit, CSV für Abrechnung | ✅ |
| | Ausfall-/Vertretungsstatistik nach Zeitraum | ✅ |
| **Austausch** | Import Schild-NRW (Lehrkräfte, Klassen; CSV) | ✅ |
| | Import/Export GPU-Austauschformat (Umstieg von Untis) | 🟡 (Kernfelder) |
| | Export Unterrichtsverteilung (CSV), iCal je Lehrkraft, PDF aller Pläne | ✅ |
| **Web-Portal** | Sync-API, Referenzserver, Krankmeldung über Portal, Übernahme in UntPlan | ✅ |
| | Vollständiges Portal mit Anmeldung/Rollen, Apps | 🔜 |
| **Weitere Module** | Klassenbuch, Sprechtage, Raumbuchung, Prüfungs-/Klausurplanung | 🔜 |

## NRW-Besonderheiten

- Schulformen nach SchulG NRW inkl. Sekundarschule, Gesamtschule, PRIMUS, Berufskolleg
- Pflichtstunden je Schulform (VO zu § 93 Abs. 2 SchulG) als Vorgabe
- G9-Jahrgänge 5–10 + EF/Q1/Q2; APO-GOSt-Kursarten mit Regelstunden
- Ferienordnung NRW 2025/26–2026/27 (+ Sommer 2027) und alle gesetzlichen Feiertage NRW
- Entfall in der Sek I standardmäßig nicht vorgeschlagen (verlässliche Schule)
- Mehrarbeitsauswertung nach Kalendermonat mit der Schwelle „mehr als 3 Stunden“
- 6-stellige Schulnummer, Schild-NRW-Importe, Fächerkürzel der amtlichen Schulstatistik
- Datenschutz: Abwesenheitsgründe werden nicht veröffentlicht oder synchronisiert

Alle Vorgaben sind Voreinstellungen und können schulindividuell angepasst werden.
Rechtliche Angaben bitte mit der aktuellen Erlasslage (BASS) abgleichen.
