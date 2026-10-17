# UntPlan Studio (Beta)

> **Beta / experimentell.** UntPlan Studio bringt die Desktop-Anwendung UntPlan in den Browser. Es ist ein zusätzliches
> Werkzeug – Desktop-Anwendung, UntPlanWeb (`web/`), PWA (`mobile/`) und iOS-App laufen unverändert weiter.
> Bitte wichtige Schuldateien zusätzlich sichern (Datei → Herunterladen bzw. mit der Desktop-Anwendung).

Die Oberfläche ist an die Windows-Anwendung angelehnt (Titelleiste mit Schnellzugriff, Menüband mit den Reitern Start,
Dateneingabe, Planung, Stundenpläne, Vertretungsplanung …, Fenster-Arbeitsbereich mit verschiebbaren und andockbaren
Unterfenstern, Tabellen mit den Spalten des Desktops, Stundenplan-Raster, Statuszeile). Alle Berechnungen laufen auf dem
Server mit derselben Fachlogik (`src/UntPlan.Core`); Schuldateien (`.up`) sind zwischen Desktop und Studio austauschbar.

**Stand dieser Ausbaustufe:** Anmeldung und Benutzer, Schuldateien (neu, Beispielschule, hochladen `.up`/BAL, speichern,
herunterladen, BAL-Import, Rückgängig/Wiederholen, Autospeicherung), Stammdaten (Klassen, Lehrkräfte, Räume, Fächer,
Raumgruppen, Gänge, Abteilungen, Abwesenheitsgründe, Schüler), Unterricht (inkl. Kopplungen), Stundenpläne je Klasse,
Lehrkraft, Raum, Fach mit manuellem Planen per Drag & Drop, Fensteranordnungen, Stundenplan-/Vertretungsmodus.
Einstellungen der Schuldatei (Schuldaten, Zeitraster mit Pausen, Kalender/Ferien mit NRW-Vorschlag, Perioden,
Unterrichtsgruppen), Druck als PDF direkt vom Server (Stundenpläne im Schwarz-Weiß-Layout des Desktop-Drucks, Vertretungsplan
nach Seitenlayout, Übersichtspläne, Berichte; dazu HTML, Info-Stundenplan, iCalendar), Übersichtspläne quer, eigene Stundenplanformate.
Ausgegraute Knöpfe im Menüband folgen in weiteren Ausbaustufen (siehe `ARCHITEKTUR.md`).

## Lokal starten

Voraussetzung: .NET 8 SDK.

```bash
cd studio/src/UntPlanStudio
STUDIO_ADMIN_PASSWORT=geheim123 ASPNETCORE_URLS=http://127.0.0.1:5501 dotnet run
```

Dann <http://127.0.0.1:5501> öffnen und mit `admin` / `geheim123` anmelden. Die Daten liegen im Ordner `daten/` neben
dem Projekt (oder unter `STUDIO_DATEN`). Ohne `STUDIO_ADMIN_PASSWORT` wird beim ersten Start ein zufälliges Passwort
erzeugt und im Protokoll ausgegeben.

Tests:

```bash
dotnet test studio/tests/UntPlanStudio.Tests -c Release      # API-Tests, Syntaxprüfung aller JS-Module (Node), Verweise
# Oberflächen-Smoke-Test (Server muss laufen, Playwright + Chromium nötig):
NODE_PATH=$(npm root -g) STUDIO_PASSWORT=geheim123 node studio/tests/smoke/smoke.cjs
NODE_PATH=$(npm root -g) STUDIO_PASSWORT=geheim123 node studio/tests/smoke/druck.cjs   # Einstellungen, Druck, Übersicht, Formate
```

## Container

Das Image wird vom GitHub-Workflow `.github/workflows/studio.yml` als `ghcr.io/florians25/untplanstudio` gebaut
(amd64 und arm64). Selbst bauen (Build-Kontext = Repository-Wurzel):

```bash
docker build -f studio/Dockerfile -t untplanstudio .
```

| Datei | Zweck |
|---|---|
| `docker-compose.yml` | HTTP auf Port 8090 (z. B. hinter einem vorhandenen Reverse-Proxy) |
| `docker-compose.https.yml` + `Caddyfile` | eigene Domain mit HTTPS (Let's Encrypt über Caddy) |
| `docker-compose.gemeinsam.yml` + `Caddyfile.gemeinsam` | UntPlanWeb **und** Studio auf einem Server, je eigene Domain, ein Caddy |

```bash
cd studio
# einfach
STUDIO_ADMIN_PASSWORT=bitte-aendern docker compose up -d
# eigene Domain
STUDIO_DOMAIN=studio.meine-schule.de STUDIO_ADMIN_PASSWORT=bitte-aendern docker compose -f docker-compose.https.yml up -d
# zusammen mit UntPlanWeb (statt web/docker-compose.https.yml)
UNTPLANWEB_DOMAIN=plan.meine-schule.de STUDIO_DOMAIN=studio.meine-schule.de STUDIO_ADMIN_PASSWORT=bitte-aendern \
  docker compose -f docker-compose.gemeinsam.yml up -d
```

Der Container läuft ohne Root-Rechte (Benutzer `app`), alle Daten liegen im Volume `/data`
(Schuldateien, Benutzer, Schlüssel für das Anmeldecookie). Sicherung = dieses Volume sichern.

## Umgebungsvariablen

| Variable | Standard | Bedeutung |
|---|---|---|
| `STUDIO_DATEN` | `./daten` (Container: `/data`) | Datenordner |
| `STUDIO_ADMIN_BENUTZER` | `admin` | Name des Admin-Kontos beim ersten Start |
| `STUDIO_ADMIN_PASSWORT` | – (zufällig, im Protokoll) | Passwort des Admin-Kontos beim ersten Start |
| `STUDIO_AUTOSPEICHERN_SEKUNDEN` | `30` | Takt der Autospeicherung geänderter Dokumente |
| `STUDIO_LEERLAUF_MINUTEN` | `120` | unbenutzte Dokumente werden danach gespeichert und entladen |
| `STUDIO_MAX_UPLOAD_MB` | `64` | Höchstgröße beim Hochladen |
| `STUDIO_SICHERE_COOKIES` | automatisch | `1` = Anmeldecookie nur über HTTPS (hinter Caddy empfohlen) |
| `STUDIO_DOMAIN` | – | nur Compose/Caddy: Domain des Studios |
| `ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_URLS` | `8080` im Container | Port bzw. Adresse |

## Bedienung (Kurzüberblick)

* **Datei** (links im Menüband): Neu, Öffnen (Liste der Schuldateien auf dem Server, Datei hochladen per Auswahl oder
  Ziehen), Speichern, Herunterladen (.up), BAL-Import, Schließen, Konto, Benutzer (Admin).
* **Fenster**: Titelleiste ziehen, an den linken/rechten Rand oder in eine Ecke ziehen zum Andocken, an den oberen Rand
  zum Maximieren; Start → Fenster: überlappend, nebeneinander, untereinander, Fensteranordnungen speichern/laden.
  Die Anordnung wird je Schuldatei gemerkt.
* **Tabellen**: Enter/F2 oder Tippen bearbeitet, Tab/Pfeile wechseln die Zelle, Entf leert, Leertaste setzt Haken,
  Strg+C/Strg+V kopiert/fügt ein (auch aus Tabellenkalkulationen), Umschalt/Strg+Klick markiert mehrere Zeilen,
  Rechtsklick auf die Kopfzeile blendet Spalten ein/aus, Kopfzeile ziehen verschiebt Spalten.
* **Stundenplan**: Stunden per Drag & Drop verschieben oder tauschen; nicht verplante Stunden unten in den Plan ziehen.
  Grün = frei, rot = Vorgabe verletzt, violett = kein Raum frei, Rahmen = Tausch möglich. Rechtsklick: fixieren, entplanen.
* **Tastatur**: Strg+S Speichern, Strg+Z/Strg+Y Rückgängig/Wiederholen, Strg+Umschalt+V Vertretungsmodus.

Für Entwickler: Aufbau, API-Konventionen und „Neues Modul anlegen“ stehen in [`ARCHITEKTUR.md`](ARCHITEKTUR.md).
