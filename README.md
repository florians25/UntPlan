# UntPlan

**Stundenplan- und Vertretungsplanung für Schulen in Nordrhein-Westfalen** – als Windows-Desktopanwendung,
mit vorbereiteter Synchronisation zu einem Web-Portal.

UntPlan deckt den kompletten Planungszyklus einer Schule ab: Stammdaten → Stundentafel →
Unterrichtsverteilung → automatische Stundenplanerstellung → Pausenaufsichten → tägliche
Vertretungsplanung → Aushang/Monitor → Statistik & Mehrarbeit → Veröffentlichung im Web-Portal.

Funktionsübersicht: [docs/FUNKTIONEN.md](docs/FUNKTIONEN.md) · Portal-Schnittstelle: [docs/SYNC_API.md](docs/SYNC_API.md)

## Highlights

- **Automatischer Stundenplan**: konfliktfrei unter Berücksichtigung von Sperrzeiten, Kopplungen,
  Schienen/Bändern der Oberstufe, Doppelstunden, Fachräumen, Hohlstunden und Tagesbelastung –
  läuft im Hintergrund, fixierte Stunden bleiben erhalten. Nachbearbeitung per Drag & Drop mit Live-Konfliktprüfung.
- **Vertretungsplanung**: Abwesenheiten erfassen, betroffene Stunden werden automatisch ermittelt,
  bewertete Vorschläge (Bereitschaft, Freisetzung durch Klassenfahrt, Klassen-/Fachkenntnis, Wochenlast),
  Entfall/EVA/Mitbetreuung/Raumänderung/Verlegung, Ein-Klick-Automatik für den ganzen Tag.
- **NRW-spezifisch**: Schulformen, Pflichtstunden, G9 + EF/Q1/Q2, APO-GOSt-Kursarten,
  Ferien und Feiertage NRW, Schild-NRW-Import, Mehrarbeitsauswertung je Kalendermonat.
- **Umstieg leicht gemacht**: Import des GPU-Austauschformats (Untis-Export) für Stammdaten, Unterricht und Stundenplan.
- **Ausgaben**: PDF-Pläne (Klasse/Lehrkraft/Raum), Vertretungsplan (Schüler-/Lehrerversion),
  Infobildschirm-Modus, CSV, iCal.
- **Web-Portal-Sync**: definierte REST-API, mitgelieferter Referenzserver inkl. Krankmeldung über das Portal.
- **Lokal & datensparsam**: alle Daten in einer Schuldatei (`*.untplan`), atomisches Speichern mit Sicherungskopie (`.bak`).

## Installation (Windows)

Fertige Installer entstehen automatisch über GitHub Actions (Workflow **Build**, Artefakt `UntPlan-Windows`):

- `UntPlan-Setup-<version>.exe` – Installer (Benutzer- oder Systeminstallation, deutsch)
- `UntPlan-Portable-<version>.exe` – ohne Installation, z. B. vom USB-Stick

Selbst bauen (unter Windows):

```bash
npm ci
npm run dist:win     # Ergebnis in release/
```

## Entwicklung

Voraussetzungen: Node.js 22

```bash
npm ci
npm run dev          # Vite-Dev-Server + Electron
npm test             # Unit-Tests (Planer, Vertretung, NRW-Daten, Import/Export, Portal)
npm run typecheck
npm run portal       # Web-Portal-Referenzserver auf http://localhost:8787 (API-Schlüssel "demo")
```

Zum Ausprobieren: **Datei › Neu… › Beispielschule** lädt ein fiktives Gymnasium (9 Klassen, 28 Lehrkräfte,
Stundentafel und Unterrichtsverteilung). Danach unter *Stundenplan › Automatisch planen* einen Plan erzeugen.

### Architektur

```
src/core/       Fachlogik (rein TypeScript, ohne UI – auch im Portal wiederverwendbar)
  model.ts        Datenmodell der Schuldatei
  nrw.ts          NRW-Vorgaben (Schulformen, Pflichtstunden, Ferien, Feiertage, Kursarten)
  planer.ts       automatische Stundenplanerstellung
  pruefung.ts     Konflikt-/Plausibilitätsprüfung
  vertretung.ts   Vertretungsplanung
  aufsicht.ts     Pausenaufsichten
  statistik.ts    Deputat, Mehrarbeit, Ausfall, Stundentafel-Abgleich
  austausch.ts    CSV/Schild-NRW, GPU, iCal
  sync*.ts        Web-Portal-Datenformat
src/main/       Electron-Hauptprozess (Fenster, Menü, Dateien, Druck/PDF, Portal-HTTP)
src/renderer/   Oberfläche (React)
server/         Web-Portal-Referenzserver (Node.js, ohne Abhängigkeiten)
tests/          Vitest
```

## Hinweise

- Rechtliche Vorgaben (Pflichtstunden, Ferien, Mehrarbeitsgrenze) sind als änderbare Voreinstellungen
  hinterlegt; bitte mit der aktuellen Erlasslage abgleichen.
- „Untis“ ist eine Marke ihres jeweiligen Inhabers. UntPlan ist ein eigenständiges Produkt und steht
  in keiner Verbindung dazu; der GPU-Import dient ausschließlich der Datenübernahme.
