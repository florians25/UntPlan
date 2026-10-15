# UntPlanWeb

**Stundenplan, Vertretungsplan und Rückmeldungen im Browser – der Webbegleiter zu UntPlan.**
UntPlan (Desktop) überträgt Stammdaten, Stundenpläne und Vertretungen an UntPlanWeb. Lehrkräfte, Schulleitung und Klassen sehen dort
ihre Pläne auf PC, Tablet und Smartphone. Abwesenheiten, Raumbuchungen und Klausuren, die im Browser eingetragen werden, holt
UntPlan beim nächsten Datenaustausch ab.

![Stundenplan](docs/bilder/stundenplan.png)

## Oberfläche

Aufbau, Format und Bedienung entsprechen WebUntis – Startseite *Heute*, Stundenplan, Klassenbuch und Administration sehen aus und
funktionieren wie dort (abgeglichen mit Screenshots des Originals). Die Icons sind die Original-Icons aus dem Paket des Kollegen
(`wwwroot/img/wu`: Navigation, Klassenbuch, Status, Info, Hausaufgabe, alte Klassenbuch-Symbole wie Stift, Besen, Beginn/Ende);
`wwwroot/js/symbole.js` ordnet sie den Menüpunkten zu und zeichnet sie als Maske (grau, aktiv orange). Kleine Bedienelemente ohne
Original-Icon (Pfeile, Papierkorb) kommen aus Bootstrap Icons. Logo (Uhr mit orangen Zeigern) und Produktname bleiben „UntPlanWeb“
– kein WebUntis-Schriftzug, kein Fremdlogo. Schrift: **Nunito Sans** (SIL OFL, lokal unter `wwwroot/lib/nunito-sans`).

- **Navigation links:** weiße Leiste mit Logo, Schulname und Schuljahr-Pille; Menüpunkte mit den Original-Icons, aktiver Punkt
  orange auf hellorangem, rechts abgerundetem Grund mit orangem Balken links. Bereiche wie *Mitteilungen*, *Stundenplan*, *Unterricht*, *Klassenbuch*, *Melden & Buchen*, *Stammdaten* und
  *Administration* öffnen ein Untermenü mit rundem Zurück-Knopf. Unten Benutzer mit Avatar und Kontomenü (*Profil*, *Konto wechseln*,
  *Weiteres Konto hinzufügen*), *Abmelden* und Einklappen. Auf dem Telefon klappt die Navigation über das Menüsymbol aus.
- **Heute** (Startseite): Datum, letzte Anmeldung und letzte Planaktualisierung, Nachrichten zum Tag (mit RSS-Symbol, wenn aktiviert),
  Hinweis auf offene Stunden und *Mein Unterricht heute* als Tabelle.
- **Stundenplan:** im eigenen Plan Woche „12. 10. - 18. 10. 2026“ mit Pfeilen, in anderen Plänen Elementwahl als Pille mit
  Info-Symbol (Hinweis z. B. „Klassenlehrkraft: KM“) und Datumsbereich als Pille; ein Klick öffnet den Kalender mit Spalte KW,
  gestrichenen Wochenenden und rosa markierter Woche. Weißer *Heute*-Knopf rechts, keine A/B-Woche im Kopf. Zeitspalte mit
  Beginn/Ende und Stundennummer, Jetzt-Linie, schwebende Legende (Entfall ▲, Prüfung ●, Änderung ■ – anklickbar zum Ausblenden)
  mit Menü „⋮“ (*Symbole anzeigen*, *Uhrzeiten anzeigen*, *Abwesenheiten*, *Drucken*, *Öffentlichen Link kopieren*) und Hilfe-Knopf.
  Stundenkarten weiß und leicht abgerundet, ohne Hover-Effekt, Beginn und Ende klein links in der Karte. Stundendetails öffnen sich
  als Bereich rechts neben der Navigation.
- **Listen** (Offene Stunden, Abwesenheiten, Benutzer …) mit weißer Werkzeugleiste oben (Suche mit Element-Pille, Filter, Zeitraum),
  Auswahlkreisen, Pillen-Knöpfen und Sammelaktionen. Alle Farben und Maße als CSS-Variablen (`wwwroot/css/app.css`), Druckansichten bleiben.

| Anmeldung | Stundendetails | Telefon: Menü |
|---|---|---|
| ![Anmeldung](docs/bilder/anmeldung.png) | ![Stundendetails](docs/bilder/stundendetails.png) | ![Menü auf dem Telefon](docs/bilder/menue-telefon.png) |

| Heute | Offene Stunden | Klassenplan |
|---|---|---|
| ![Heute](docs/bilder/heute.png) | ![Offene Stunden](docs/bilder/offene-stunden.png) | ![Klassenplan](docs/bilder/klassenplan.png) |

| Telefon: Heute | Telefon: Stundenplan | Monitor |
|---|---|---|
| ![Heute auf dem Telefon](docs/bilder/heute-telefon.png) | ![Stundenplan auf dem Telefon](docs/bilder/stundenplan-telefon.png) | ![Monitor](docs/bilder/monitor.png) |

## Funktionen

Seiten (je nach Rolle):

| Bereich | Seiten |
|---|---|
| **Heute** | Startseite wie oben; die Startseite kann in der Administration auf *Übersicht* oder *Mein Stundenplan* umgestellt werden |
| **Übersicht** | Kennzahlen, Änderungen dieser und der nächsten Woche im eigenen Plan, anstehende Prüfungen und Hausaufgaben |
| **Mitteilungen** | Posteingang, Gesendet, Entwürfe, Empfängerlisten; neue Mitteilung mit Anhängen, Antworten, Weiterleiten, Drucken |
| **Stundenplan** | Mein Stundenplan, Klasse, Lehrkraft, Raum, Tag - Klassen, Tag - Lehrkräfte, Tag - Räume, Vertretungsplan (Klassenkonten: Mein Stundenplan, Weitere Stundenpläne) |
| **Unterricht** | Meine Unterrichte (Periode, Termine, Räume, CSV), Offene Stunden, Prüfungen |
| **Klassenbuch** | Übersicht, Offene Stunden, Klassenbucheinträge, Hausaufgaben, Klassendienste, Abwesenheiten, Fehlzeiten, Befreiungen, Sitzplan, Berichte |
| **Melden & Buchen** | Abwesenheit melden, Raum buchen, Prüfung eintragen, Meine Meldungen |
| **Stammdaten** (Administration) | Klassen, Lehrkräfte, Fächer, Räume, Schüler*innen (aus UntPlan, lesend), Abwesenheitsgründe, Klassendienste, Versionen (Übertragungsverlauf) |
| **Administration** | Übersicht & Synchronisation, Nachrichten zum Tag, Freie Tage & Ferien, Import, Export, Aktive Benutzer, Rechte und Rollen, Benutzer, Zuordnungsgruppen, Rückmeldungen, Prüfungssperren, Ansichtseinstellungen, Monitore, Protokoll |

Im Einzelnen:

- **Stundenpläne** für Klassen, Lehrkräfte und Räume:
  - Zeitraster mit Beginn/Ende je Stunde und Jetzt-Linie; gleichzeitige Stunden stehen nebeneinander, Aufsichten als schmale Zeilen in den Pausen.
  - Stundenkarten in hellen Statusfarben mit Akzentbalken in der Fachfarbe aus UntPlan: Änderungen grün (geänderte Werte als grüne
    Kürzelbox), Entfall rot und durchgestrichen mit Dreieck, Prüfungen gelb mit Punkt, Veranstaltungen grün mit Plus.
  - **Vertretung ohne Lehrkraft** („+++“ in UntPlan): Änderung, bei der die bisherige Lehrkraft durchgestrichen in der grünen Plakette steht
    (kein „???“ oder „+++“); ohne eigenen Vertretungstext steht „Vertr. o. Lehrkraft“ als Text.
  - **Kopplungen**: gekoppelte Stunden mit unterschiedlichen Fächern (z. B. KR und ER) oder in unterschiedlichen Räumen sind einzelne
    Karten. Nur bei gleichem Fach **und** gleichem Raum stehen mehrere Lehrkräfte bzw. Klassen auf einer Karte – dann mit allen
    beteiligten Klassen und Lehrkräften auf der Karte und in den Details (auch im Lehrerplan der beteiligten Lehrkräfte).
  - **Lehrkraft abwesend**: Im Plan der abwesenden Lehrkraft sind Stunden, die andere übernehmen, grau gestrichelt mit dem eigenen
    Kürzel durchgestrichen in der Plakette (Hinweis „abwesend …“ über dem Tag) – nur ein echter Entfall bleibt rot.
  - **Symbole unten rechts**: Info-Symbol bei Notizen für Schüler*innen/Lehrkräfte oder Vertretungstext (Text als Hinweis), Hausaufgaben-
    Symbol in der Stunde, zu der eine Hausaufgabe fällig ist.
  - **Fachwechsel**: die ursprüngliche Stunde erscheint als Entfall, die neue Stunde als grüne Karte daneben.
  - **Veranstaltungen** als Zusatzkarten mit Klassen, teilnehmenden Lehrkräften (gekürzt „BEC, BRA, FIS +12“, vollständig in den Details)
    und Raum; Veranstaltungen mit Raum stehen auch im Raumplan und in *Tag - Räume*.
  - Ferien, Feiertage und freie Tage (auch die in der Administration angelegten) als Fläche mit Schloss; Elementwahl mit Vor/Zurück (auch Alt+←/→).
  - Stundendetails wie im Original: Kopf „Klassen | Unterricht | Fach“ und „Tag | Datum | Zeit | Raum | Lehrkraft“, Reiter
    *Klassenbuch* und *Details*.
    - *Klassenbuch*: Schülerkacheln mit Foto-Platzhalter und Statuspunkt (Alle auswählen, Auswahl löschen, abwesend, verspätet,
      Klassenbucheintrag), Lehrstoff mit Stift, *Abwesende Schüler*innen* als alte Liste (grau gestreift: Auswahl, Stift, Von,
      Beginnzeit, Bis, Endzeit, Grund, Status, Text) mit *Verlängern* (Fehlen aus der Vorstunde in diese Stunde übernehmen) und
      Symbolen „auf Tagesbeginn“/„bis Stundenende“, Hausaufgaben (Liste mit *Neue Hausaufgabe*, Fälligkeit – vorbelegt eine Woche
      später –, Bemerkung, erledigt, löschen; fällige Hausaufgaben der Stunde), Klassenbucheinträge mit „+“, Klassendienste mit
      Besen, *Ticket erstellen* (Mitteilung an Schulleitung/Administration); unten die Statusleiste „Lehrstoffeintragung vorhanden“ und
      „Abwesenheiten wurden kontrolliert“ (sonst Knöpfe *Lehrstoff eingeben*, *Abwesenheiten kontrollieren*).
    - *Details*: nur „Findet online statt“, Lehrstoff und Vertretungstext (wenn vorhanden), Hausaufgaben, *Notizen für Schüler*innen*
      und *Notizen für Lehrkräfte*; unten *Vorherige Stunde*, *Nächste Stunde*, *Prüfung erstellen* (Prüfungssperren werden beachtet)
      und *Raum buchen*. Gebuchter Raum und Prüfung stehen sofort als Änderung bzw. Prüfung (gelb) im Plan.
  - Bereitschaften, Raumbuchungen und Vormerkungen; Druck, Telefonansicht mit Tagesauswahl.
- **Tagesübersicht** (Kollegium): alle Klassen, Lehrkräfte oder Räume eines Tages auf einen Blick.
- **Vertretungsplan** als Schüler- oder Lehrerversion; die Schülerversion zeigt keine Abwesenheitsgründe, Bereitschaften oder Aufsichten.
  Aufeinanderfolgende Stunden werden zusammengefasst („3–4“).
- **Monitore:** Vertretungsplan für Info-Bildschirme über einen geheimen Link (ohne Anmeldung), mit Uhr, automatischem Bildlauf,
  einstellbarer Aktualisierung, Nachrichten in der Kopfzeile oder als Laufschrift, optional nur öffentliche Nachrichten.
- **Nachrichten zum Tag** mit Betreff, Zeitraum oder Wochentag, Reihenfolge, Zielgruppen, Anzeige auf Startseite, Monitor und Mobil-App,
  „öffentlich“ für den RSS-Feed (`/rss/nachrichten.xml`, in den Ansichtseinstellungen einschaltbar).
- **Melden & Buchen** (Lehrkräfte): Abwesenheit melden, freien Raum buchen (nur Räume „buchbar (Web)“), Prüfung eintragen. Die Meldungen gehen
  als Rückmeldungen an UntPlan; Buchungen erscheinen sofort als „vorgemerkt“.
- **Klassenbuch:**
  - Wochenübersicht je Klasse mit Lehrstoff, Hausaufgabe, Anwesenheitsprüfung und Fehlenden je Stunde, Vermerke je Tag.
  - **Offene Stunden:** eigene oder alle Stunden ohne Abwesenheitskontrolle bzw. Lehrstoff (Zeitraum wählbar), *Prüfen* und *Eingeben*
    öffnen die Stundendetails, Mitteilung an die ausgewählten Lehrkräfte, Bericht.
  - Anwesenheit in den Stundendetails (alle anwesend vorbelegt, abwesend/verspätet). Dafür überträgt UntPlan die Schülerliste.
  - **Abwesenheiten:** neu anlegen (mehrere Tage/Stunden, Grund), bearbeiten, löschen, zusammenfassen, entschuldigen, zuletzt verwendete
    Texte; Filter nach Zeitraum, Klasse, Grund und Status, CSV; Reiter *Meine Abwesenheiten*.
  - **Fehlzeiten:** Summen je Schüler (Stunden, Tage, entschuldigt, unentschuldigt, offen, Verspätungen), ganze Fehltage in einem Schritt.
  - **Klassendienste:** Dienste (z. B. Klassenordner, Tafeldienst) je Schüler und Zeitraum, Wochenansicht, automatische Verteilung.
  - **Befreiungen:** Schüler*innen von einem Fach befreien; Befreite sind in den Stundendetails markiert.
  - **Sitzplan** je Klasse (Raster, Schüler per Auswahl setzen).
  - **Klassenbucheinträge**, **Hausaufgaben** wie bisher; Klassenkonten zusätzlich mit Reiter *Lehrstoff*.
  - **Berichte:** 18 Berichte in Gruppen (Fehlzeiten, Abwesenheiten je Schüler/Klasse/Grund, Entschuldigungsschreiben, Lehrstoff,
    Hausaufgaben, Klassenbucheinträge, offene Stunden, Klassendienste, Befreiungen …) – Vorschau, Druck und CSV.
- **Prüfungen:** Klausuren aus UntPlan und in UntPlanWeb vorgemerkte Prüfungen, Filter nach Zeitraum, Klasse und Lehrkraft, CSV.
- **Mitteilungen** zwischen Kollegium, Schulleitung und Klassen (Gruppen, Klassen, einzelne Benutzer, eigene Empfängerlisten),
  Entwürfe und Anhänge (bis 5 Dateien à 10 MB).
- **Profil:** Name, E-Mail, Sprache, Benachrichtigungen, Passwort (Regeln aus *Rechte und Rollen*), Kalenderabo (iCal).
- **Konto wechseln:** mehrere Konten auf einem Gerät merken und ohne neue Anmeldung wechseln (Gerätecookie, 30 Tage).
- **Administration:** Verbindung mit UntPlan (Schul-ID, API-Schlüssel), Paket-Upload, Benutzer (auch automatisch aus den Stammdaten,
  CSV-Import mit Vorschau, Sammelaktionen Sperren/Entsperren/Löschen/E-Mail übernehmen/Inaktive sperren, Bericht), Freie Tage,
  Export (CSV), Aktive Benutzer, Passwortregeln, Zuordnung der Schulleitung, Prüfungssperren, Ansichtseinstellungen (Startseite, RSS),
  Rückmeldungen, Nachrichten zum Tag, Monitore, Protokoll.
- **Mobil-App:** [UntPlanMobile](../mobile/README.md) bringt Stundenplan, Klassenbuch und Mitteilungen als installierbare
  App aufs Telefon. Sie läuft als eigener Container (Port 8081) und holt ihre Daten über die JSON-API von UntPlanWeb.

| Mein Stundenplan | Übersicht | Lehrerplan |
|---|---|---|
| ![Stundenplan](docs/bilder/stundenplan.png) | ![Übersicht](docs/bilder/uebersicht.png) | ![Lehrerplan](docs/bilder/lehrerplan.png) |

| Vertretungsplan | Tag - Räume | Abwesenheiten |
|---|---|---|
| ![Vertretungsplan](docs/bilder/vertretungsplan.png) | ![Tag - Räume](docs/bilder/tagesuebersicht.png) | ![Abwesenheiten](docs/bilder/abwesenheiten.png) |

| Administration: Benutzer |
|---|
| ![Administration](docs/bilder/administration.png) |

### Weitere Endpunkte der App-Schnittstelle

Zusätzlich zu Plan, Vertretungen, Klassenbuch und Mitteilungen (alle unter `/api/app`, Anmeldung per Sitzungscookie):

| Aufruf | Zweck |
|---|---|
| `GET /api/app/unterricht?lehrer=&klasse=` | Unterrichte der gültigen Periode (Kollegium) |
| `GET /api/app/pruefungen?von=&bis=&klasse=&lehrer=` | Prüfungen (Klassenkonten: nur eigene Klasse) |
| `GET /api/app/hausaufgaben?von=&bis=&klasse=&lehrer=` | Hausaufgaben nach Fälligkeit |
| `GET /api/app/klassenbucheintraege?von=&bis=&klasse=&art=` | Vermerke über alle oder eine Klasse |
| `GET /api/app/abwesenheiten?von=&bis=&klasse=&status=` | Schülerabwesenheiten, Stunden eines Tages zusammengefasst |
| `POST/PUT /api/app/abwesenheiten`, `POST …/loeschen`, `POST …/zusammenfassen`, `GET …/texte` | Abwesenheiten anlegen, ändern, löschen, zusammenfassen, letzte Texte |
| `GET /api/app/berichte/fehlzeiten?klasse=&von=&bis=&format=csv` | Fehlzeitenbericht (JSON oder CSV) |
| `GET /api/app/berichte/lehrstoff?klasse=&von=&bis=&format=csv` | Lehrstoffbericht (JSON oder CSV) |
| `GET /api/app/berichte/liste`, `GET /api/app/berichte/erstellen/{art}?…&format=csv` | alle Berichtsarten, Bericht erstellen |
| `GET /api/app/klassenbuch/offen?tage=` | eigene Stunden ohne Lehrstoff (Standard 14 Tage) |
| `GET/POST /api/app/klassenbuch/hausaufgabenliste`, `PUT/DELETE …/{id}` | Hausaufgaben einer Stunde (Text, Fälligkeit, Bemerkung, erledigt) und die in der Stunde fälligen |
| `POST /api/app/klassenbuch/abwesenheit/verlaengern` | Abwesenheiten aus der Vorstunde bis Stundenende bzw. bis zum Tagesbeginn verlängern |
| `GET /api/app/start` | Daten der Startseite *Heute* |
| `GET /api/app/klassenbuch/stunden?von=&bis=&lehrer=&klasse=` | offene Stunden (Abwesenheitskontrolle/Lehrstoff) |
| `PUT /api/app/klassenbuch/online` | Stunde „findet online statt“ |
| `GET /api/app/abwesenheitsgruende` | Abwesenheitsgründe |
| `GET/POST /api/app/klassendienste`, `POST …/woche`, `POST …/automatisch`, `PUT/DELETE …/{id}` | Klassendienste |
| `GET/POST /api/app/befreiungen`, `PUT/DELETE …/{id}`, `GET /api/app/klassenbuch/befreit` | Befreiungen |
| `GET/PUT /api/app/sitzplan/{klasse}` | Sitzplan |
| `POST /api/app/mitteilungen/entwurf`, `POST …/{id}/senden`, `…/listen`, `…/{id}/anhaenge`, `GET /api/app/mitteilungen/anhang/{id}` | Entwürfe, Empfängerlisten, Anhänge |
| `GET /api/app/konten`, `POST …/{id}/wechseln`, `DELETE …/{id}` | gemerkte Konten auf diesem Gerät |
| `GET /api/app/stammdaten/{art}` | Stammdaten lesend (Administration) |
| `/api/app/admin/gruende`, `…/dienstarten`, `…/freietage`, `…/einstellungen/{bereich}`, `…/benutzer/aktion`, `…/import/pruefen`, `…/import/uebernehmen` | Verwaltung (Administration) |
| `GET /rss/nachrichten.xml` | öffentliche Nachrichten zum Tag als RSS (wenn eingeschaltet) |

## Installation mit Docker

Voraussetzung: ein Server oder NAS mit Docker (Linux, amd64 oder arm64). Standardmäßig liegt die Datenbank (SQLite) im Container-Volume, ein eigener Datenbankserver ist nicht nötig.
Für größere bzw. produktive Installationen kann stattdessen MariaDB oder MySQL verwendet werden (siehe [Installation mit MariaDB/MySQL](#installation-mit-mariadbmysql)).

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
| `UNTPLANWEB_DB` | Datenbank: `sqlite` oder `mysql` (auch `mariadb`) | `sqlite` |
| `UNTPLANWEB_DB_CONNECTION` | Verbindung zu MySQL/MariaDB: `Server=…;Port=3306;Database=…;User ID=…;Password=…` oder `mysql://benutzer:passwort@host:3306/datenbank` | – |
| `UNTPLANWEB_DB_WARTEN` | Sekunden, die beim Start auf den Datenbankserver gewartet wird | `60` |
| `UNTPLANWEB_UPDATE_PRUEFEN` | `0` = nicht nach neuen Versionen suchen | an |
| `UNTPLANWEB_UPDATE_URL` | Quelle der neuesten Version (GitHub-API `…/releases/latest`, `…/releases`, `…/tags`, `ghcr://besitzer/image` oder eigene Adresse mit `{"version":"…"}` bzw. Text) | `https://api.github.com/repos/florians25/UntPlan/releases/latest` |
| `UNTPLANWEB_UPDATE_TAG_PRAEFIX` | nur Tags mit diesem Präfix berücksichtigen (z. B. `web-v`) | – |
| `UNTPLANWEB_UPDATE_MODUS` | „Update installieren“: `aus`, `watchtower` oder `datei` | `watchtower`, wenn eine Watchtower-Adresse gesetzt ist, sonst `aus` |
| `UNTPLANWEB_UPDATE_WATCHTOWER_URL`, `…_TOKEN` | HTTP-API von Watchtower, z. B. `http://watchtower:8080/v1/update`, und deren Token | – |
| `UNTPLANWEB_MOBILE_INTERN_URL` | Adresse von UntPlanMobile aus Sicht des Servers (Versionsanzeige in der Administration), z. B. `http://untplanmobile:8081` | – |

### Installation mit MariaDB/MySQL

SQLite bleibt der Standard (und wird für die Tests verwendet). Für produktive Installationen mit vielen Benutzer*innen oder
vorhandener Datenbank-Infrastruktur kann UntPlanWeb MariaDB (ab 10.5) oder MySQL (ab 8.0.13) verwenden. Die Datei
`docker-compose.mariadb.yml` startet UntPlanWeb zusammen mit einem MariaDB-Dienst (eigenes Volume, Healthcheck; UntPlanWeb
startet erst, wenn die Datenbank bereit ist):

```bash
mkdir untplanweb && cd untplanweb
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/docker-compose.mariadb.yml
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/update.sh && chmod +x update.sh
cat > .env <<'EOF'
MARIADB_PASSWORT=ein-langes-passwort
MARIADB_ROOT_PASSWORT=ein-anderes-langes-passwort
WATCHTOWER_TOKEN=ein-zufaelliger-token
EOF
docker compose -f docker-compose.mariadb.yml up -d
```

Tipp: `export COMPOSE_FILE=docker-compose.mariadb.yml` (oder `COMPOSE_FILE=…` in `.env`) erspart das `-f` bei allen weiteren Befehlen.

Eigener Datenbankserver statt Container: `UNTPLANWEB_DB=mysql` und
`UNTPLANWEB_DB_CONNECTION=Server=db.schule.local;Database=untplanweb;User ID=untplanweb;Password=…` setzen. Die Datenbank
muss existieren (Zeichensatz `utf8mb4`), der Benutzer braucht alle Rechte darauf. Tabellen und Migrationen legt UntPlanWeb beim
Start selbst an. Ein Passwort mit `;` in Anführungszeichen setzen (`Password="a;b"`) oder die Form `mysql://…` mit URL-Kodierung
verwenden. Das Volume `/data` bleibt auch mit MySQL nötig (Schlüssel der Anmelde-Cookies, Sicherungen, Update-Anforderungen).

Technik: Die Befehle und Migrationen sind im SQLite-Dialekt geschrieben; eine schlanke Provider-Abstraktion
(`Daten/Dialekt.cs`, Paket MySqlConnector) übersetzt die Unterschiede zentral – `AUTOINCREMENT`/`AUTO_INCREMENT`,
`ON CONFLICT … DO UPDATE`/`ON DUPLICATE KEY UPDATE`, `INSERT OR REPLACE/IGNORE`, `datetime('now')`, `last_insert_rowid()`,
`COLLATE NOCASE`, Texttypen mit Index (`VARCHAR(255)`) bzw. ohne (`LONGTEXT`), Fremdschlüssel. Booleans werden als 0/1
gespeichert, `LIMIT` ist gleich. Alle Tests laufen mit SQLite; mit `UNTPLANWEB_TEST_MYSQL` zusätzlich gegen MySQL/MariaDB
(siehe [Entwicklung](#entwicklung)).

### Update

Ein Update ist immer ein neues Container-Image. Die Datenbank-Migrationen laufen beim Start automatisch (SQLite und MySQL);
eine Sicherung vorher wird dringend empfohlen – bei MySQL/MariaDB schließen Schemaänderungen Transaktionen implizit ab.

```bash
./update.sh                       # Sicherung, docker compose pull, docker compose up -d
# oder von Hand:
docker compose exec untplanweb dotnet UntPlanWeb.dll --sichern      # SQLite → /data/sicherungen
docker compose pull && docker compose up -d
```

`update.sh` (im Ordner mit der Compose-Datei aufrufen, für MariaDB mit `COMPOSE_FILE=docker-compose.mariadb.yml`) sichert
SQLite per `--sichern` bzw. MariaDB per `mariadb-dump` nach `./sicherungen`, lädt die neuen Images und startet neu.

**Updateanzeige:** Unter *Administration → Übersicht* zeigt die Karte *Version & Updates* die laufende Version, die Datenbank
mit Schemastand und die neueste veröffentlichte Version (Standard: GitHub-Releases von florians25/UntPlan, alle 6 Stunden geprüft;
abschaltbar mit `UNTPLANWEB_UPDATE_PRUEFEN=0`, andere Quelle mit `UNTPLANWEB_UPDATE_URL`). Ist ein Update verfügbar, erscheint
oben ein Hinweis. Passt die Nummerierung der Releases nicht zu der des Images, `UNTPLANWEB_UPDATE_URL=ghcr://florians25/untplanweb`
(Tags des öffentlichen Images) oder einen Tag-Präfix verwenden.

**Update aus der Oberfläche** (nur Administration; Aufruf `POST /api/app/admin/update/installieren` mit Kopf `X-UntPlanWeb: 1`).
Vorher wird SQLite automatisch nach `/data/sicherungen` gesichert (die letzten fünf bleiben). Zwei Wege:

- **Watchtower** (empfohlen): Die Compose-Dateien enthalten einen optionalen Dienst `watchtower` (Profil `updater`), der nur auf
  Anforderung und nur Container mit dem Label `com.centurylinklabs.watchtower.enable=true` aktualisiert (nicht MariaDB):
  `docker compose --profile updater up -d`, Token in `.env` (`WATCHTOWER_TOKEN`), bei UntPlanWeb
  `UNTPLANWEB_UPDATE_WATCHTOWER_URL=http://watchtower:8080/v1/update` und `UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN` setzen
  (in `docker-compose.mariadb.yml` bereits vorbelegt). Watchtower erhält Zugriff auf den Docker-Socket – nur mit eigenem, langem Token betreiben.
- **Anforderungsdatei** (`UNTPLANWEB_UPDATE_MODUS=datei`, ohne Docker-Socket im Container): Der Knopf legt
  `/data/update/angefordert.json` an; ein cron-Eintrag auf dem Server führt dann das Update aus:
  `* * * * * cd /opt/untplanweb && ./update.sh --wenn-angefordert >> update.log 2>&1`.

Ohne Updater zeigt die Karte den Befehl `docker compose pull && docker compose up -d`.

### Datenmigration

Bestehende SQLite-Daten lassen sich nach MySQL/MariaDB übernehmen. Der Befehl migriert beide Datenbanken auf den aktuellen
Schemastand und kopiert alle Tabellen in einer Transaktion (IDs, Passwort-Hashes und Anhänge bleiben erhalten). Die Zieldatenbank
muss leer sein (sonst `--ueberschreiben`); die SQLite-Datei bleibt als Sicherung liegen.

Umstieg einer Docker-Installation (im selben Ordner, damit das Volume `untplanweb-daten` mit der SQLite-Datei weiterverwendet wird):

```bash
docker compose down                                                   # SQLite-Installation stoppen (Volume bleibt)
curl -O https://raw.githubusercontent.com/florians25/UntPlan/main/web/docker-compose.mariadb.yml
# .env mit MARIADB_PASSWORT, MARIADB_ROOT_PASSWORT, WATCHTOWER_TOKEN anlegen (siehe oben)
docker compose -f docker-compose.mariadb.yml run --rm untplanweb --migrieren-nach-mysql
docker compose -f docker-compose.mariadb.yml up -d
```

Ohne Docker: `UNTPLANWEB_DATEN=/pfad/zum/datenordner UNTPLANWEB_DB_CONNECTION="Server=…;Database=…;User ID=…;Password=…" dotnet UntPlanWeb.dll --migrieren-nach-mysql [--quelle datei.db] [--ueberschreiben]`,
danach UntPlanWeb mit `UNTPLANWEB_DB=mysql` starten. Der Ablauf ist durch Tests abgedeckt (`DatenbankTests`: SQLite → SQLite immer,
SQLite → MySQL/MariaDB über den Kommandozeilenbefehl, wenn `UNTPLANWEB_TEST_MYSQL` gesetzt ist).

**Sicherung des Volumes** (z. B. zusätzlich): `docker run --rm -v untplanweb_untplanweb-daten:/data -v $PWD:/b alpine tar czf /b/untplanweb.tgz /data`.

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
| Administration | alles | Benutzer, Synchronisation, Stammdaten, Einstellungen, Monitore, Nachrichten |
| Schulleitung | alles (Lehrerversion) | Nachrichten zum Tag, Mitteilungen an alle |
| Lehrkraft | alle Pläne, Lehrerversion | melden, buchen, Klassenbuch für eigene Stunden |
| Klasse / Schüler | eigene Klasse, Räume, Schülerversion | Mitteilungen an Klassenleitung und Schulleitung |

## Sicherheit

- Passwörter sind mit PBKDF2 gehasht (ASP.NET Core Identity). Nach 5 Fehlversuchen wird die Anmeldung für 5 Minuten gesperrt.
  Mindestlänge und Zeichenregeln für Passwörter sind unter *Administration → Rechte und Rollen* einstellbar.
- Gemerkte Konten („Konto wechseln“) sind an einen zufälligen Gerätecookie gebunden, der nur als Hash gespeichert wird.
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
# zusätzlich gegen MySQL/MariaDB (Benutzer mit CREATE/DROP DATABASE); ohne Variable werden diese Tests übersprungen:
UNTPLANWEB_TEST_MYSQL="Server=127.0.0.1;User ID=root;Password=…" dotnet test tests/UntPlanWeb.Tests
# alle Tests gegen MySQL/MariaDB:
UNTPLANWEB_TEST_MYSQL="…" UNTPLANWEB_TEST_ALLE_MIT_MYSQL=1 dotnet test tests/UntPlanWeb.Tests
```

Aufbau:

- `src/UntPlanWeb`: ASP.NET Core 8 (Minimal-APIs) mit SQLite (oder MySQL/MariaDB über `Daten/Dialekt.cs`) und eigenen Migrationen (`Daten/Db.cs`, nur anhängen).
- `wwwroot`: Oberfläche ohne Build-Schritt im WebUntis-Stil (eigenes `css/app.css` mit CSS-Variablen über Bootstrap 5.3, Original-Icons unter `img/wu` (Zuordnung in `js/symbole.js`), Bootstrap Icons und Nunito Sans lokal eingebunden, keine CDN-Abhängigkeit), Vanilla-JS-Module je Seite unter `js/ansichten`.
- `demo/beispiel-paket.json`: Beispielpaket, mit UntPlan.Core aus der Beispielschule erzeugt.

UntPlanWeb gehört nicht zur Desktop-Solution `UntPlan.sln` und hat einen eigenen CI-Ablauf (`.github/workflows/web.yml`).
Dieser testet und veröffentlicht das Image `ghcr.io/florians25/untplanweb`. Der Desktop-Build bleibt davon unberührt.

> Hinweis: Ein neues GHCR-Paket ist zunächst privat. Damit `docker pull` ohne Anmeldung funktioniert, stellen Sie es einmalig auf öffentlich:
> GitHub → Packages → untplanweb → Package settings → Change visibility.
