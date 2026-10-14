# UntPlanWeb

**Stundenplan, Vertretungsplan und Rückmeldungen im Browser – der Webbegleiter zu UntPlan.**
UntPlan (Desktop) überträgt Stammdaten, Stundenpläne und Vertretungen an UntPlanWeb. Lehrkräfte, Schulleitung und Klassen sehen dort
ihre Pläne auf PC, Tablet und Smartphone. Abwesenheiten, Raumbuchungen und Klausuren, die im Browser eingetragen werden, holt
UntPlan beim nächsten Datenaustausch ab.

![Stundenplan](docs/bilder/stundenplan.png)

## Oberfläche

Aufbau und Bedienung folgen dem gewohnten WebUntis-Prinzip, mit eigener Gestaltung (eigene Linien-Icons aus Bootstrap Icons,
Systemschrift, keine fremden Grafiken):

- **Navigation links:** weiße Leiste mit Logo, Schulname und Schuljahr-Pille; Menüpunkte mit Linien-Icons, aktiver Punkt orange
  mit Balken. Bereiche wie *Stundenplan*, *Unterricht*, *Klassenbuch*, *Melden & Buchen* und *Administration* öffnen ein Untermenü
  mit rundem Zurück-Knopf. Unten Benutzer mit Avatar, *Abmelden* und *Einklappen* (nur Symbole). Auf dem Telefon klappt die
  Navigation über das Menüsymbol in der Kopfleiste aus.
- **Inhalt** auf hellgrauem Grund mit weißen Bereichen, Pillen-Knöpfen, Reitern mit oranger Unterstreichung, Filterleisten über Listen,
  Dialogen und Hinweisen unten mittig. Alle Farben und Maße als CSS-Variablen (`wwwroot/css/app.css`), Druckansichten bleiben erhalten.

| Anmeldung | Stundendetails | Telefon: Menü |
|---|---|---|
| ![Anmeldung](docs/bilder/anmeldung.png) | ![Stundendetails](docs/bilder/stundendetails.png) | ![Menü auf dem Telefon](docs/bilder/menue-telefon.png) |

## Funktionen

Seiten (je nach Rolle):

| Bereich | Seiten |
|---|---|
| **Heute** | nächster Unterrichtstag mit Stunden, Nachrichten zum Tag, Mitteilungen, Prüfungen, Hausaufgaben bzw. offene Stunden, Termine, eigene Meldungen |
| **Übersicht** | Kennzahlen, Änderungen dieser und der nächsten Woche im eigenen Plan, anstehende Prüfungen und Hausaufgaben |
| **Mitteilungen** | Posteingang, Gesendet, neue Mitteilung (Liste links, Lesebereich rechts) |
| **Stundenplan** | Mein Stundenplan, Klasse, Lehrkraft, Raum, Tag - Klassen, Tag - Lehrkräfte, Tag - Räume, Vertretungsplan (Klassenkonten: Mein Stundenplan, Weitere Stundenpläne) |
| **Unterricht** | Meine Unterrichte (Periode, Termine, Räume, CSV), Offene Stunden (ohne Lehrstoff, zum Nachtragen), Prüfungen |
| **Klassenbuch** | Übersicht (Woche, Fehlzeiten, Vermerke, Lehrstoff), Klassenbucheinträge, Hausaufgaben, Abwesenheiten, Fehlzeiten, Berichte |
| **Melden & Buchen** | Abwesenheit melden, Raum buchen, Prüfung eintragen, Meine Meldungen |
| **Administration** | Übersicht & Synchronisation, Benutzer, Rückmeldungen, Nachrichten zum Tag, Monitore, Protokoll |

Im Einzelnen:

- **Stundenpläne** für Klassen, Lehrkräfte und Räume:
  - Raster nach Unterrichtsstunden mit Uhrzeiten und Jetzt-Linie; gleichzeitige Stunden stehen nebeneinander.
    Pausenzeilen (mit Aufsichten) nur dort, wo in der Woche wirklich eine Pause mit Aufsicht liegt.
  - Weiße Stundenkarten mit Beginn/Ende, Akzentbalken in der Fachfarbe aus UntPlan und den bekannten Kennzeichen:
    Änderungen grün hinterlegt, geänderte Werte als grüne Kürzelbox, Entfall rot und durchgestrichen, Prüfungen gelb.
  - **Vertretung ohne Lehrkraft** („+++“ in UntPlan): Änderung, bei der die bisherige Lehrkraft durchgestrichen in der grünen Plakette steht
    (kein „???“ oder „+++“); ohne eigenen Vertretungstext steht „Vertr. o. Lehrkraft“ als Text.
  - **Kopplungen**: mehrere Lehrkräfte desselben Unterrichts in einer Stunde stehen auf einer Karte (Fächer, Lehrkräfte, Räume zusammengefasst).
  - **Fachwechsel**: die ursprüngliche Stunde erscheint als Entfall, die neue Stunde als grüne Karte daneben.
  - **Veranstaltungen** als grüne Zusatzkarten mit Klassen, teilnehmenden Lehrkräften (gekürzt „BEC, BRA, FIS +12“, vollständig in den Details)
    und Raum; Veranstaltungen mit Raum stehen auch im Raumplan und in *Tag - Räume*.
  - Ferien, Feiertage und freie Tage als hellblaue Fläche mit Schloss; Elementwahl mit Vor/Zurück (auch Alt+←/→), Woche mittig, „Heute“, Legende.
  - Stundendetails mit den Reitern *Klassenbuch* (Schülerkacheln, Lehrstoff, Hausaufgaben, Einträge, Abwesende) und *Details*;
    von dort eine Prüfung eintragen oder einen freien Raum buchen („vorgemerkt“, bis UntPlan übernimmt).
  - Bereitschaften, Raumbuchungen und Vormerkungen; Druck, Telefonansicht mit Tagesauswahl.
- **Tagesübersicht** (Kollegium): alle Klassen, Lehrkräfte oder Räume eines Tages auf einen Blick.
- **Vertretungsplan** als Schüler- oder Lehrerversion; die Schülerversion zeigt keine Abwesenheitsgründe, Bereitschaften oder Aufsichten.
  Aufeinanderfolgende Stunden werden zusammengefasst („3–4“).
- **Monitore:** Vertretungsplan für Info-Bildschirme über einen geheimen Link (ohne Anmeldung), mit Uhr, automatischem Bildlauf und minütlicher Aktualisierung.
- **Melden & Buchen** (Lehrkräfte): Abwesenheit melden, freien Raum buchen (nur Räume „buchbar (Web)“), Prüfung eintragen. Die Meldungen gehen
  als Rückmeldungen an UntPlan; Buchungen erscheinen sofort als „vorgemerkt“.
- **Klassenbuch:**
  - Wochenübersicht je Klasse mit Lehrstoff, Hausaufgabe, Anwesenheitsprüfung und Fehlenden je Stunde, Vermerke je Tag.
  - Anwesenheit in den Stundendetails (alle anwesend vorbelegt, abwesend/verspätet). Dafür überträgt UntPlan die Schülerliste.
  - **Abwesenheiten:** Liste der Schülerabwesenheiten mit Filter (Zeitraum, Klasse, Status), Entschuldigen durch Klassenleitung bzw. Schulleitung,
    CSV; Reiter *Meine Abwesenheiten* mit den eigenen Abwesenheitsmeldungen der Lehrkraft.
  - **Fehlzeiten:** Summen je Schüler (Stunden, Tage, entschuldigt, unentschuldigt, offen, Verspätungen), ganze Fehltage in einem Schritt.
  - **Klassenbucheinträge:** Vermerke (Lob, Ordnungsmaßnahme, Elterngespräch …) über alle oder eine Klasse, mit Filter nach Art.
  - **Hausaufgaben** nach Fälligkeit, Filter nach Klasse und Lehrkraft; Klassenkonten zusätzlich mit Reiter *Lehrstoff*.
  - **Berichte:** Fehlzeitenbericht (je Klasse oder alle) und Lehrstoffbericht einer Klasse – Vorschau, Druck und CSV.
  - Klassenkonten sehen Hausaufgaben und Lehrstoff, aber keine Fehlzeiten, Notizen oder Vermerke.
- **Prüfungen:** Klausuren aus UntPlan und in UntPlanWeb vorgemerkte Prüfungen, Filter nach Zeitraum, Klasse und Lehrkraft, CSV.
- **Mitteilungen** zwischen Kollegium, Schulleitung und Klassen (Gruppen, Klassen, einzelne Benutzer).
- **Kalenderabo (iCal)** des eigenen Stundenplans inkl. Vertretungen.
- **Administration:** Verbindung mit UntPlan (Schul-ID, API-Schlüssel), Paket-Upload, Benutzer (auch automatisch aus den Stammdaten),
  Rückmeldungen, Nachrichten zum Tag, Monitore, Protokoll.
- **Mobil-App:** [UntPlanMobile](../mobile/README.md) bringt Stundenplan, Klassenbuch und Mitteilungen als installierbare
  App aufs Telefon. Sie läuft als eigener Container (Port 8081) und holt ihre Daten über die JSON-API von UntPlanWeb.

| Heute | Übersicht | Lehrerplan |
|---|---|---|
| ![Heute](docs/bilder/heute.png) | ![Übersicht](docs/bilder/uebersicht.png) | ![Lehrerplan](docs/bilder/lehrerplan.png) |

| Vertretungsplan | Tag - Räume | Abwesenheiten |
|---|---|---|
| ![Vertretungsplan](docs/bilder/vertretungsplan.png) | ![Tag - Räume](docs/bilder/tagesuebersicht.png) | ![Abwesenheiten](docs/bilder/abwesenheiten.png) |

### Weitere Endpunkte der App-Schnittstelle

Zusätzlich zu Plan, Vertretungen, Klassenbuch und Mitteilungen (alle unter `/api/app`, Anmeldung per Sitzungscookie):

| Aufruf | Zweck |
|---|---|
| `GET /api/app/unterricht?lehrer=&klasse=` | Unterrichte der gültigen Periode (Kollegium) |
| `GET /api/app/pruefungen?von=&bis=&klasse=&lehrer=` | Prüfungen (Klassenkonten: nur eigene Klasse) |
| `GET /api/app/hausaufgaben?von=&bis=&klasse=&lehrer=` | Hausaufgaben nach Fälligkeit |
| `GET /api/app/klassenbucheintraege?von=&bis=&klasse=&art=` | Vermerke über alle oder eine Klasse |
| `GET /api/app/abwesenheiten?von=&bis=&klasse=&status=` | Schülerabwesenheiten, Stunden eines Tages zusammengefasst |
| `GET /api/app/berichte/fehlzeiten?klasse=&von=&bis=&format=csv` | Fehlzeitenbericht (JSON oder CSV) |
| `GET /api/app/berichte/lehrstoff?klasse=&von=&bis=&format=csv` | Lehrstoffbericht (JSON oder CSV) |
| `GET /api/app/klassenbuch/offen?tage=` | eigene Stunden ohne Lehrstoff (Standard 14 Tage) |

## Installation mit Docker

Voraussetzung: ein Server oder NAS mit Docker (Linux, amd64 oder arm64). Die Datenbank (SQLite) liegt im Container-Volume, ein eigener Datenbankserver ist nicht nötig.

```bash
mkdir untplanweb && cd untplanweb
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/docker-compose.yml
docker compose up -d
```

Dann im Browser `http://<server>:8080` öffnen. Der Einrichtungsassistent legt die Schule und das Administrationskonto an und zeigt
**Schul-ID** und **API-Schlüssel** für UntPlan an.

**Mit HTTPS** (empfohlen, automatisches Let's-Encrypt-Zertifikat über Caddy). Voraussetzung: ein DNS-Name, der auf den Server zeigt, und die Ports 80 und 443:

```bash
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/docker-compose.https.yml
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/Caddyfile
UNTPLANWEB_DOMAIN=plan.meine-schule.de docker compose -f docker-compose.https.yml up -d
```

**Ausprobieren mit Beispieldaten:**

```bash
docker run --rm -p 8080:8080 -e UNTPLANWEB_DEMO=1 -e UNTPLANWEB_ADMIN_PASSWORT=admin12345 ghcr.io/florians25/untplanweb
```

Anmeldung als `admin` / `admin12345`. Die Beispielwoche wird automatisch auf die aktuelle Woche verschoben.

**Selbst bauen** (Wurzel des UntPlan-Repositorys): `docker build -f web/Dockerfile -t untplanweb .`

### Einstellungen (Umgebungsvariablen)

| Variable | Bedeutung | Standard |
|---|---|---|
| `UNTPLANWEB_DATEN` | Datenordner (Datenbank, Schlüssel) | `/data` |
| `UNTPLANWEB_ZEITZONE` | Zeitzone der Schule | `Europe/Berlin` |
| `UNTPLANWEB_ADMIN_PASSWORT` | legt beim ersten Start den Benutzer `admin` an (statt Assistent) | – |
| `UNTPLANWEB_SCHUL_ID` | feste Schul-ID | aus dem Schulnamen |
| `UNTPLANWEB_API_SCHLUESSEL` | fester API-Schlüssel für UntPlan | wird erzeugt |
| `UNTPLANWEB_DEMO` | `1` = Beispieldaten laden, solange noch keine Daten vorhanden sind | – |
| `UNTPLANWEB_MOBILE_URL` | erlaubte Adresse(n) von UntPlanMobile für die Einmalanmeldung (kommagetrennt) | gleicher Rechnername |

### Sicherung und Update

- **Sicherung:** das Volume `untplanweb-daten` sichern, zum Beispiel `docker run --rm -v untplanweb_untplanweb-daten:/data -v $PWD:/b alpine tar czf /b/untplanweb.tgz /data`.
- **Update:** `docker compose pull && docker compose up -d`. Die Datenbank wird beim Start automatisch migriert.

## UntPlan verbinden

1. In UntPlanWeb: *Administration → Übersicht & Synchronisation*. Dort stehen Adresse, Schul-ID und API-Schlüssel (der Schlüssel kann neu erzeugt werden).
2. In UntPlan: *UntPlanWeb → Einstellungen*. Adresse, Schul-ID und API-Schlüssel eintragen und *Verbindung prüfen*.
3. *Exportieren* überträgt Stammdaten, Unterricht und Vertretungen. Das geht auch über den UntPlanWeb-Knopf in der Schnellzugriffsleiste.
4. *Importieren* holt Abwesenheiten, Buchungen und Klausuren ab. Übernommene Meldungen werden in UntPlanWeb als „übernommen“ angezeigt.
5. *Administration → Benutzer → Konten aus Stammdaten* legt Konten für alle Lehrkräfte und Klassen an.

Die Schnittstelle (Schema `untplanweb/1`) ist in `src/UntPlan.Web.Contracts` definiert und wird von UntPlan und UntPlanWeb gemeinsam verwendet:

| Aufruf | Zweck |
|---|---|
| `GET /api/v1/status` | Version, Schema, aktuelle Revision |
| `PUT /api/v1/schulen/{schulId}/untplanweb` | Paket übertragen (ganz oder nur einzelne Teile); veraltete Revision → 409 |
| `GET /api/v1/schulen/{schulId}/rueckmeldungen` | offene Rückmeldungen abholen |
| `POST /api/v1/schulen/{schulId}/rueckmeldungen/quittieren` | übernommene Rückmeldungen quittieren (`["id", …]`) |
| `GET /api/v1/schulen/{schulId}/fehlzeiten?ab=…&bis=…` | im Klassenbuch erfasste Schüler-Fehlzeiten (UntPlan: Import „Studenten*“) |
| `GET /api/v1/schulen/{schulId}/klassenbuch?ab=…&bis=…` | Lehrstoff und Hausaufgaben je Stunde, ohne interne Notizen (UntPlan: Import „Kurse“) |

Die Authentifizierung erfolgt über `Authorization: Bearer <API-Schlüssel>`.

## Rollen

| Rolle | sieht | darf |
|---|---|---|
| Administration | alles | Benutzer, Synchronisation, Monitore, Nachrichten |
| Schulleitung | alles (Lehrerversion) | Nachrichten zum Tag, Mitteilungen an alle |
| Lehrkraft | alle Pläne, Lehrerversion | melden, buchen, Klassenbuch für eigene Stunden |
| Klasse / Schüler | eigene Klasse, Räume, Schülerversion | Mitteilungen an Klassenleitung und Schulleitung |

## Sicherheit

- Passwörter sind mit PBKDF2 gehasht (ASP.NET Core Identity). Nach 5 Fehlversuchen wird die Anmeldung für 5 Minuten gesperrt.
- Sitzungscookie mit `HttpOnly` und `SameSite=Strict`. Ändernde Aufrufe brauchen einen eigenen Kopf (Schutz vor CSRF).
- Content-Security-Policy ohne Fremdquellen. Die Seite lädt keine externen Schriften, Skripte oder Tracker.
- Der API-Schlüssel wird nur als SHA-256-Hash gespeichert.
- Für den Betrieb im Internet HTTPS verwenden (siehe oben).

## Entwicklung

```bash
cd web
dotnet run --project src/UntPlanWeb          # http://localhost:5000, Daten in ./daten
UNTPLANWEB_DEMO=1 UNTPLANWEB_ADMIN_PASSWORT=admin12345 dotnet run --project src/UntPlanWeb
dotnet test tests/UntPlanWeb.Tests           # inkl. Ende-zu-Ende-Test mit dem Exportcode von UntPlan
```

Aufbau:

- `src/UntPlanWeb`: ASP.NET Core 8 (Minimal-APIs) mit SQLite und eigenen Migrationen.
- `wwwroot`: Oberfläche ohne Build-Schritt im WebUntis-Stil (eigenes `css/app.css` mit CSS-Variablen über Bootstrap 5.3, Bootstrap Icons lokal eingebunden, keine CDN-Abhängigkeit), Vanilla-JS-Module je Seite unter `js/ansichten`.
- `demo/beispiel-paket.json`: Beispielpaket, mit UntPlan.Core aus der Beispielschule erzeugt.

UntPlanWeb gehört nicht zur Desktop-Solution `UntPlan.sln` und hat einen eigenen CI-Ablauf (`.github/workflows/web.yml`).
Dieser testet und veröffentlicht das Image `ghcr.io/florians25/untplanweb`. Der Desktop-Build bleibt davon unberührt.

> Hinweis: Ein neues GHCR-Paket ist zunächst privat. Damit `docker pull` ohne Anmeldung funktioniert, stellen Sie es einmalig auf öffentlich:
> GitHub → Packages → untplanweb → Package settings → Change visibility.
