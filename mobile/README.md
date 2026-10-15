# UntPlanMobile

**Der Stundenplan fürs Telefon – als installierbare App (PWA) zu UntPlanWeb.**
UntPlanMobile zeigt Stundenpläne, Vertretungen, Ferien und Termine in der vertrauten Untis-Optik, führt das Klassenbuch
der eigenen Stunden und bringt die Mitteilungen aus UntPlanWeb aufs Telefon. Die App hat keine eigene Datenbank: Sie
holt alles von einer laufenden UntPlanWeb-Instanz, angemeldet wird mit dem Konto von UntPlanWeb.

| Klassenplan | Lehrerplan | Tagesansicht | Stundendetails |
|---|---|---|---|
| ![Klassenplan](docs/bilder/klassenplan.png) | ![Lehrerplan](docs/bilder/lehrerplan.png) | ![Tagesansicht](docs/bilder/tagesansicht.png) | ![Stundendetails](docs/bilder/stundendetails.png) |

| Anwesenheit | Ferien | Startseite | Mitteilungen | Dunkel |
|---|---|---|---|---|
| ![Anwesenheit](docs/bilder/anwesenheit.png) | ![Ferien](docs/bilder/ferien.png) | ![Startseite](docs/bilder/startseite.png) | ![Mitteilungen](docs/bilder/mitteilungen.png) | ![Dunkel](docs/bilder/dunkel.png) |

## Funktionen

- **Stundenplan** für den eigenen Plan, Klassen, Lehrkräfte und Räume:
  - Zeitproportionales Raster mit Pausen, Jetzt-Linie und Öffnen an der aktuellen Uhrzeit.
  - Kacheln mit Fachfarbe als Balken: Änderungen grün mit Plakette am geänderten Wert, offene Vertretungen mit
    durchgestrichenem Kürzel, Entfall rot und durchgestrichen, Klausuren gelb, Veranstaltungen grün gerahmt,
    Bereitschaften gestrichelt, Pausenaufsichten als Pillen in der Pause.
  - Im eigenen Lehrerplan laufen abgegebene Stunden „ohne mich“ weiter (Eckklammern).
  - Ferien und Feiertage als blaues Feld mit Name und Zeitraum, ganztägige Termine als Pille über der ersten Stunde.
  - Parallele Stunden mit Zählkarte, Tippen öffnet den Tag mit allen Stunden nebeneinander. Gekoppelte Stunden mit
    unterschiedlichen Fächern sind eigene Kacheln; nur bei gleichem Fach und Raum stehen alle Lehrkräfte und Klassen auf einer.
  - Mehrere Lehrkräfte (auch bei Veranstaltungen) stehen durch „ | “ getrennt; nur diese Leiste rollt waagerecht, der Rest der
    Kachel bleibt stehen. Zu lange Werte und Plaketten enden mit „…“.
  - Info-Zeichen unten rechts bei Notizen (für Schüler*innen bzw. Lehrkräfte) und Vertretungstext, Hausaufgabenzeichen in der
    Stunde, zu der eine Hausaufgabe fällig ist.
  - Wischen zwischen Wochen bzw. Tagen (Nachbarwochen vorgeladen), Tagesansicht, „Gehe zu Datum“, Legende.
  - Auswahl mit Kategorien, Buchstabenleiste und Suche; Klassenkonten sehen nur die eigene Klasse und Räume.
- **Stundendetails** mit Sprung in die Pläne von Klasse, Lehrkraft und Raum. Für die eigenen Stunden das **Klassenbuch**:
  - Lehrstoff (Unterrichtsinhalt), Hausaufgaben mit Fälligkeit und Bemerkung (Hausaufgabenliste von UntPlanWeb),
    *Notizen für Schüler*innen* (Klassenkonten lesen sie) und *Notizen für Lehrkräfte*.
  - **Raum buchen**: freie buchbare Räume der Stunde, ein Tippen verlegt die Stunde – die Raumänderung steht sofort im Plan.
  - Klassenbucheinträge (Vermerke zur Klasse) und Bemerkungen zu einzelnen Schüler*innen.
  - Anwesenheit: alle anwesend vorbelegt, Antippen oder Wischen setzt „abwesend“ bzw. „verspätet“ (mit Ankunftszeit).
  - Hausaufgaben, die zu einer Stunde fällig sind, tragen ein Zeichen auf der Kachel.
- **Startseite:** die nächsten Stunden des eigenen Plans, Termine und Klausuren der nächsten zwei Wochen, eigene
  Abwesenheitsmeldungen; für Klassenleitungen die Fehlzeiten der Klasse mit „Entschuldigen“ und „Ablehnen“.
- **Mitteilungen:** Posteingang und Gesendet, Lesen, Antworten, Schreiben (Empfänger, Gruppen und Klassen wie in
  UntPlanWeb), Löschen.
- **Profil:** Erscheinungsbild Automatisch/Hell/Dunkel, Zeiten auf den Karten, entfallene Stunden ein-/ausblenden,
  Daten aktualisieren, Fassung, Abmelden.
- **Offline:** Der Service Worker hält die App und die zuletzt geladenen Wochen vor. Jede neue Fassung erneuert den
  Cache auf den Geräten automatisch (Prüfwert über alle Dateien der Oberfläche).

Nicht in der App (nur in UntPlanWeb): Entwürfe und Anhänge bei Mitteilungen, Abwesenheitsgründe und
„Erziehungsberechtigte benachrichtigt“, Onlineunterricht, mehrere Profile auf einem Gerät. Klassenkonten sehen in UntPlanWeb keine Fehlzeiten einzelner Schüler*innen – „Meine Abwesenheiten“ bleibt
dort leer.

## Installation mit Docker

UntPlanMobile läuft neben UntPlanWeb, als eigener Container auf **Port 8081**. Die Datei `docker-compose.yml` startet beide:

```bash
mkdir untplan && cd untplan
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/mobile/docker-compose.yml
docker compose up -d
```

- UntPlanWeb: `http://<server>:8080` – Schule einrichten, UntPlan verbinden, Konten anlegen (siehe [UntPlanWeb](../web/README.md)).
- UntPlanMobile: `http://<server>:8081` – mit einem Konto von UntPlanWeb anmelden.

**Ausprobieren mit Beispieldaten:** In `docker-compose.yml` bei UntPlanWeb `UNTPLANWEB_DEMO: "1"` und
`UNTPLANWEB_ADMIN_PASSWORT: "admin12345"` setzen. Konten für Lehrkräfte und Klassen legt UntPlanWeb unter
*Administration → Benutzer → Konten aus Stammdaten* an.

**Läuft UntPlanWeb schon** (z. B. mit der Compose-Datei aus `web/`), genügt UntPlanMobile allein:

```bash
docker run -d --name untplanmobile -p 8081:8081 -e UNTPLANWEB_URL=http://<server>:8080 ghcr.io/florians25/untplanmobile
```

**Selbst bauen** (Wurzel des UntPlan-Repositorys): `docker build -f mobile/Dockerfile -t untplanmobile .`

### Als App installieren (HTTPS)

Service Worker und „Zum Home-Bildschirm“ funktionieren nur über HTTPS (oder `localhost`). Im Betrieb gehört
UntPlanMobile deshalb hinter einen Reverse-Proxy mit Zertifikat, zum Beispiel Caddy:

```
plan.meine-schule.de {
    reverse_proxy untplanweb:8080
}
app.meine-schule.de {
    reverse_proxy untplanmobile:8081
}
```

UntPlanMobile wertet `X-Forwarded-Proto` und `X-Forwarded-Host` aus und setzt das Sitzungscookie dann als `Secure`.

### Einstellungen (Umgebungsvariablen)

| Variable | Bedeutung | Standard |
|---|---|---|
| `UNTPLANWEB_URL` | Adresse von UntPlanWeb aus Sicht des Containers | `http://untplanweb:8080` (Image), `http://localhost:8080` (Entwicklung) |
| `UNTPLANWEB_PUBLIC_URL` | Adresse von UntPlanWeb aus Sicht des Browsers (für die Einmalanmeldung) | gleicher Rechnername, Port 8080 |
| `UNTPLANMOBILE_ZEITZONE` | Zeitzone der Schule (heutiger Tag, aktuelle Woche) | `Europe/Berlin` |
| `ASPNETCORE_HTTP_PORTS` | Port im Container | `8081` |

## Verbindung zu UntPlanWeb

UntPlanMobile spricht ausschließlich die JSON-API von UntPlanWeb (`/api/app/*`) an:

- **Anmeldung:** `POST /api/login` gibt Benutzername und Passwort an `POST /api/app/anmelden` weiter. Das Sitzungscookie
  von UntPlanWeb bekommt der Browser als eigenes Cookie `untplanmobile` (HttpOnly, SameSite=Lax); UntPlanMobile reicht
  es bei jedem Aufruf als `untplanweb` weiter und führt Erneuerungen und Abmeldungen nach. Ändernde Aufrufe tragen den
  Kopf `X-UntPlanWeb: 1`, den UntPlanWeb verlangt. Ohne gültige Sitzung antworten alle Endpunkte mit HTTP 401, die
  App zeigt dann die Anmeldung.
- **Rechte** prüft allein UntPlanWeb (z. B. Lehrerpläne nicht für Klassenkonten, Klassenbuch nur für die eigene Stunde);
  dessen Fehlermeldungen kommen unverändert in der App an.

| UntPlanMobile | Aufgabe | UntPlanWeb |
|---|---|---|
| `GET /` | App-Hülle mit Startdaten (`window.TT_BOOT`: eigener Plan, Stammdaten, aktuelle Woche) | `sitzung`, `elemente`, `plan` |
| `GET /api/mobile?action=meta` | eigener Plan, Klassen/Lehrkräfte/Räume, Stundenzeiten, Langnamen | `sitzung`, `elemente` |
| `GET /api/mobile?action=week&date=…&type=mine\|class\|teacher\|room&id=…` | eine Woche als Kacheln | `plan`, `klassenbuch/klasse` |
| `POST /api/login` (`action=login\|logout`) | An- und Abmelden | `anmelden`, `abmelden` |
| `GET/POST /api/classbook` | Lehrstoff, Hausaufgaben, Notizen für Schüler*innen und Lehrkräfte, Klassenbucheinträge, Bemerkungen, Anwesenheit | `klassenbuch/stunde`, `klassenbuch/hausaufgabenliste`, `klassenbuch/anwesenheit`, `klassenbuch/vermerke`, `klassenbuch/klasse` |
| `GET/POST /api/rooms` (`action=free\|book`) | freie buchbare Räume einer Stunde, Raum buchen | `meldungen/freieraeume`, `meldungen/raumbuchung` |
| `GET/POST /api/home` | Startseite, Termine, Abwesenheiten, Fehlzeiten der Klasse, Entschuldigen | `heute`, `meldungen`, `klassenbuch/fehlzeiten` |
| `GET/POST /api/messages` | Posteingang, Gesendet, Lesen, Schreiben, Löschen | `mitteilungen` |
| `GET /api/accounts` | Profilwechsel (nicht unterstützt, leere Liste) | – |
| `GET /sw.js`, `/manifest.webmanifest`, `/assets/…` | Service Worker (mit Fassung), Manifest, Oberfläche | – |
| `GET /gesundheit` | Gesundheitsprüfung (Docker) | – |

**Übersetzung der Karten:** Die Stundenkarten von UntPlanWeb werden in die Kacheln der Oberfläche übersetzt
(`Dienste/Kachelbau.cs`):

| UntPlanWeb | Kachel |
|---|---|
| `regulaer` | `normal` |
| `geaendert`, `vorgemerkt` | `change` (geänderte Werte mit `…Orig` und `changed`) |
| `offen` | `change` mit `openSub` (alter Wert durchgestrichen) |
| `abwesend` (Lehrer-/Raumplan) | `change` mit dem eigenen Kürzel als `teacherOrig` (läuft ohne mich) |
| `entfall` | `cancelled` |
| `klausur` | `klausur` |
| `bereitschaft` | `standby` (Stunden mit gehaltener Vertretung entfallen) |
| `veranstaltung`, Teilnahme ohne Fach | `absence` mit `overlay` (grün gerahmtes Feld) |
| `buchung` | `event` |
| freier Tag (`ferien`, `feiertag`, `frei`) | `holiday` mit `holidayName` und `holidayRange` |
| Pausenaufsicht | `supervision` (`_supAbsent` vertreten, `_supSub` übernommen) |

Für die Zeiträume der Ferienfelder liefert UntPlanWeb unter `/api/app/elemente` zusätzlich die Liste `ferien`.

## Entwicklung

```bash
# UntPlanWeb mit Beispieldaten (Port 5190)
cd web && UNTPLANWEB_DEMO=1 UNTPLANWEB_ADMIN_PASSWORT=admin12345 dotnet run --project src/UntPlanWeb --urls http://localhost:5190
# UntPlanMobile dagegen (Port 5191)
cd mobile && UNTPLANWEB_URL=http://localhost:5190 dotnet run --project src/UntPlanMobile --urls http://localhost:5191
# Tests (starten UntPlanWeb im Testprozess)
dotnet test mobile/tests/UntPlanMobile.Tests
```

Aufbau:

- `src/UntPlanMobile`: ASP.NET Core 8 (Minimal-APIs) ohne eigene Datenbank.
  - `Dienste/UntPlanWebVerbindung.cs`: Aufrufe an UntPlanWeb mit durchgereichtem Sitzungscookie.
  - `Dienste/Kachelbau.cs`: Wochenplan → Kacheln; `Dienste/Wochendienst.cs`: Wochen, Stammdaten, Startdaten.
  - `Api/`: die Endpunkte der Oberfläche (Stundenplan, Anmeldung, Klassenbuch, Startseite, Mitteilungen).
  - `Vorlagen/index.html`, `Vorlagen/sw.js`: App-Hülle und Service Worker; der Server setzt Symbolsammlung,
    Startdaten (mit CSP-Nonce) und Fassung ein.
  - `wwwroot/`: die Oberfläche ohne Build-Schritt – `app.js`, `app.css`, Symbole und Manifest.
- `tests/UntPlanMobile.Tests`: Übersetzung der Karten und Ende-zu-Ende-Tests gegen UntPlanWeb mit dem Beispielpaket.

Die Oberfläche stammt aus einer Stundenplan-PWA mit PHP-Backend und ist nahezu unverändert übernommen. Angepasst sind
die Endpunkte (`api/mobile` usw. statt der PHP-Dateien), der Name, Langnamen und Kategorien aus UntPlanWeb, ein
Mindestabstand für den Streifen über der ersten Stunde (Schulen mit Fünf-Minuten-Wechseln) und das Ausblenden der
Funktionen, die UntPlanWeb nicht anbietet. Die Druckseite der Vorlage entfällt – gedruckt wird in UntPlanWeb.

UntPlanMobile gehört weder zur Desktop-Solution `UntPlan.sln` noch zu `web/UntPlanWeb.sln`, sondern hat die eigene
Solution `UntPlanMobile.sln` und einen eigenen CI-Ablauf (`.github/workflows/mobile.yml`). Dieser testet und
veröffentlicht das Image `ghcr.io/florians25/untplanmobile`.

> Hinweis: Ein neues GHCR-Paket ist zunächst privat. Damit `docker pull` ohne Anmeldung funktioniert, stellen Sie es einmalig auf öffentlich:
> GitHub → Packages → untplanmobile → Package settings → Change visibility.

## Einmalanmeldung (SSO) über UntPlanWeb

Beim ersten Aufruf leitet UntPlanMobile automatisch zu UntPlanWeb weiter. Wer dort schon angemeldet ist, landet ohne
Passworteingabe direkt im eigenen Plan. Sonst erscheint die Anmeldeseite von UntPlanWeb, danach geht es zurück in die App.
Die Anmeldung mit Benutzername und Passwort in der App bleibt als Alternative erhalten.

- UntPlanWeb gibt nur einen Einmal-Code heraus (2 Minuten gültig), den UntPlanMobile serverseitig einlöst; Passwörter
  laufen nie über die Mobil-App.
- Erlaubte Rücksprungadressen: `UNTPLANWEB_MOBILE_URL` bei UntPlanWeb (kommagetrennt, z. B. `https://m.meine-schule.de`).
  Ohne Angabe ist nur derselbe Rechnername (anderer Port) erlaubt.
- Liegen beide hinter verschiedenen Adressen (Reverse-Proxy), bei UntPlanMobile `UNTPLANWEB_PUBLIC_URL` und bei UntPlanWeb
  `UNTPLANWEB_MOBILE_URL` setzen.
