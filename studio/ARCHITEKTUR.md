# UntPlan Studio (Beta) – Architektur

UntPlan Studio bringt die Desktop-Anwendung (`src/UntPlan.App`) als Browser-Anwendung. Die **gesamte Fachlogik bleibt in
`src/UntPlan.Core`** – das Studio ist eine dünne Server-Schicht (ASP.NET Core 8, REST/JSON) plus eine Oberfläche aus
Vanilla-JS-ES-Modulen ohne Build-Schritt. Desktop, UntPlanWeb (`web/`), PWA (`mobile/`) und iOS bleiben unverändert;
Schuldateien (`.up`) sind zwischen Desktop und Studio austauschbar.

```
Browser (wwwroot)                                  Server (UntPlanStudio)                         UntPlan.Core
┌───────────────────────────────┐   REST/JSON    ┌───────────────────────────────────────┐      ┌──────────────────┐
│ Shell: Titelleiste, Ribbon,   │ ─────────────▶ │ Api/<Modul>Api.cs  (IStudioModul)      │ ───▶ │ Dokument, Model, │
│ Fenster, Statuszeile          │                │   └─ Arbeitsdokument (Sperre, Undo)    │      │ Stammdaten,      │
│ Module: js/module/<modul>.js  │ ◀───────────── │ Dokumentdienst (Ablage, Autosave)      │      │ Planung, …       │
│ Komponenten: Tabelle, Raster  │  SSE-Ereignisse│ Benutzerdienst, Benutzereinstellungen  │      └──────────────────┘
└───────────────────────────────┘                └───────────────────────────────────────┘
```

## Ordnerstruktur

```
studio/
  ARCHITEKTUR.md, README.md
  Dockerfile, Dockerfile.dockerignore        Container (Build-Kontext = Repository-Wurzel)
  docker-compose.yml                         HTTP auf Port 8090
  docker-compose.https.yml + Caddyfile       eigene Domain (STUDIO_DOMAIN), HTTPS über Caddy
  docker-compose.gemeinsam.yml + Caddyfile.gemeinsam   UntPlanWeb + Studio hinter einem Caddy
  UntPlanStudio.sln
  src/UntPlanStudio/
    Program.cs                 Aufbau: Dienste, Anmeldung (Cookie), Middleware, Routengruppen, Module
    Grundlagen/                StudioKonfiguration (Umgebungsvariablen), IStudioModul/StudioRouten, StudioFehler,
                               Fehlerbehandlung, Typnamen (Elementtyp ↔ Route)
    Dienste/                   Arbeitsdokument, Dokumentdienst, Autospeicherung, Benutzerdienst, Benutzereinstellungen,
                               Felder (Stammdaten-Spalten, aus dem Desktop übernommen), Unterrichtsspalten (dto.)
    Api/
      Modulregister.cs         ◀ EINZIGE Registrierungsstelle der Server-Module
      AnmeldungApi.cs, BenutzerApi.cs, EinstellungenApi.cs, DokumenteApi.cs     Fundament
      StammdatenApi.cs, UnterrichtApi.cs, StundenplanApi.cs                     erste Fachmodule
    wwwroot/
      index.html, favicon.svg
      css/basis.css shell.css fenster.css tabelle.css raster.css dialog.css     Kern-Stile
      css/module/<modul>.css                                                    Stile je Modul (optional)
      js/app.js                Start: Anmeldung → Shell → Module → letztes Dokument
      js/kern/                 api, zustand, ereignisse, dokument, dom, symbole, dialog, auswahl,
                               tabelle, raster, fenster, ribbon, modulsystem, shell
      js/module/register.js    ◀ EINZIGE Registrierungsstelle der Oberflächen-Module
      js/module/<modul>.js     geplant, datei, start, stammdaten, unterricht, stundenplan
  tests/UntPlanStudio.Tests/   xUnit + WebApplicationFactory (API, JS-Syntax per Node, Verweise)
  tests/smoke/smoke.cjs        Playwright-Smoke (Anmelden, Datei öffnen, Fenster, Bildschirmfotos)
```

## Server

### Arbeitsdokumente

* Ablage im Datenordner (`STUDIO_DATEN`): `dokumente/<id>.up` (Schuldatei wie im Desktop) + `<id>.json` (Name, Besitzer,
  Herkunft). Die Id besteht aus 16 Hex-Zeichen (wird geprüft – keine Pfade aus Anfragen).
* Geöffnete Dokumente hält `Dokumentdienst` im Speicher (`Arbeitsdokument` mit dem Core-`Dokument`). Ein Dokument ist für
  alle angemeldeten Benutzer dasselbe Objekt → Änderungen sehen alle sofort (Grundlage für MultiUser).
* **Alle Zugriffe auf Core-Daten nur über `Arbeitsdokument`** – UntPlan.Core ist nicht threadsicher:
  * `LesenAsync(doc => …)` – unter der Dokumentsperre lesen und **in DTOs umwandeln** (keine Core-Objekte herausgeben).
  * `AendernAsync(benutzer, beschreibung, AenderungsArt, d => …, doc => ergebnis)` – führt die Änderung über
    `Dokument.Aendern` aus (Rückgängig-Stufe, Abgleich der Vertretungen wie im Desktop), erhöht `Version`, meldet ein
    Ereignis und liefert unter derselben Sperre das Ergebnis (z. B. die geänderte Tabellenzeile).
  * `AusfuehrenAsync(benutzer, doc => (ergebnis, art?, text))` – für Sonderfälle (Rückgängig, Optimierungsergebnis übernehmen …).
* Rückgängig/Wiederholen kommen aus dem Core (50 Stufen, `POST …/rueckgaengig`, `…/wiederholen`).
* `Autospeicherung` speichert geänderte Dokumente alle `STUDIO_AUTOSPEICHERN_SEKUNDEN` (Standard 30) und beim Beenden;
  lange unbenutzte Dokumente ohne Ereignis-Abonnenten werden entladen (`STUDIO_LEERLAUF_MINUTEN`).
* Fehlermeldungen des Core (`InvalidOperationException`, `FormatException`) werden als HTTP 400 `{ "fehler": "…" }`
  gemeldet – wie im Desktop als Hinweis. Eigene Fehler: `throw StudioFehler.Ungueltig(...)`, `.NichtGefunden(...)`, `.Verboten()`.
* Lange Berechnungen (z. B. Optimierung) nicht unter der Sperre laufen lassen: Daten unter `LesenAsync` kopieren
  (`Dokument.Klonen`), rechnen, Ergebnis per `AendernAsync` übernehmen.

### Ereignisse (Aktualisierung zwischen Fenstern und Benutzern)

`GET /api/dokumente/{dok}/ereignisse` (Server-Sent Events): `hallo`, `aenderung` (`{ version, bereich, beschreibung, benutzer, zeit }`),
`gespeichert`, `geschlossen`. Zusätzlich trägt jede Antwort eines Dokument-Endpunkts die Kopfzeilen `X-Dokument-Version`
und `X-Dokument-Bereich`. `bereich` = `AenderungsArt` des Core: `Stammdaten`, `Unterricht`, `Stundenplan`, `Vertretung`,
`Einstellungen`, `Alles`.

### Anmeldung und Rechte

Cookie-Anmeldung (`POST /api/anmeldung`), Benutzer in `benutzer.json` (Passwort-Hash PBKDF2 / ASP.NET Core Identity).
Admin-Konto beim ersten Start aus `STUDIO_ADMIN_BENUTZER`/`STUDIO_ADMIN_PASSWORT`. Rollen: `Admin`, `Planer`, `Leser`
(Leser: keine ändernden Anfragen, serverseitig erzwungen). Jede ändernde Anfrage braucht die Kopfzeile
`X-UntPlan-Studio: 1` (Schutz gegen Cross-Site-Anfragen; die Oberfläche setzt sie automatisch).

## API-Konventionen

* Präfix `/api`; alles außer `/api/system`, `/api/sitzung`, `/api/anmeldung`, `/api/abmeldung` verlangt Anmeldung.
* Dokumentbezogen immer unter `/api/dokumente/{dok}/<modul>/…` – im Modul über `routen.Dokument.MapGroup("/<modul>")`;
  Handler bekommen das Dokument einfach als Parameter `Arbeitsdokument a` (Bindung über `{dok}`, 404 bei unbekannter Id).
* JSON in camelCase, Enums als Text. Datumswerte ISO (`2026-10-14`), Zeiten `HH:mm`.
* **Eingaben als Text wie im Desktop**: Tabellenwerte werden als Text geschickt und mit der Desktop-Logik geparst
  (Bereich `1-2`, Liste `5a,5b`, Bool `X`, Dezimal `25,5`, Elemente per Kurzname). Antworten liefern die Anzeige-Texte.
* Elementtypen in Routen klein: `klasse`, `lehrer`, `raum`, `fach`, `raumgruppe`, `gang`, `abteilung`, `abwesenheitsgrund`, `schueler`.
* Tabellen-Antworten: `{ titel, felder|spalten: [...], zeilen|unterrichte: [...] }` mit Werten als Text-Arrays in Spaltenreihenfolge.
* Ändernde Endpunkte liefern das geänderte Objekt in derselben Form wie die Liste (damit die Oberfläche nur diese Zeile ersetzt).
* Mehrere Änderungen in EINER Rückgängig-Stufe: eigener Sammel-Endpunkt (Beispiel `POST …/stammdaten/{typ}/aendern`).
* Positionsangaben im Stundenplan: `tag` (0 = erster Schultag), `stunde` (ab 1) wie im Core.

### Vorhandene Endpunkte

| Modul | Endpunkte |
|---|---|
| anmeldung | `GET /api/system`, `GET /api/sitzung`, `POST /api/anmeldung`, `POST /api/abmeldung`, `GET /api/ich`, `POST /api/ich/passwort` |
| benutzer (Admin) | `GET/POST /api/benutzer`, `DELETE /api/benutzer/{name}`, `POST /api/benutzer/{name}/passwort`, `GET /api/benutzer/rollen` |
| einstellungen | `GET/PUT/DELETE /api/einstellungen/{schluessel}` (JSON je Benutzer; Schlüssel mit Modulpräfix, z. B. `vertretung.ansicht`) |
| dokumente | `GET /api/dokumente` (Liste + zuletzt), `POST /api/dokumente/neu`, `POST /api/dokumente/beispiel`, `POST /api/dokumente/hochladen` (multipart `datei`: .up oder BAL); je Dokument: `GET ""` (Stand), `GET /grunddaten` (Zeitraster, Perioden), `POST /speichern`, `GET /herunterladen`, `POST /schliessen`, `DELETE ""`, `PUT /name`, `POST /rueckgaengig`, `POST /wiederholen`, `POST /bal-import` (multipart + Optionen), `GET /ereignisse` |
| stammdaten | `GET /stammdaten/{typ}`, `GET /stammdaten/{typ}/felder`, `POST /stammdaten/{typ}` (`{kurzname, werte}`), `PATCH /stammdaten/{typ}/{id}` (`{feld, wert}`), `POST /stammdaten/{typ}/aendern` (`{aenderungen:[{id,feld,wert}]}`), `POST /stammdaten/{typ}/loeschen` (`{ids}`), `DELETE /stammdaten/{typ}/{id}`, `GET /stammdaten/{typ}/verwendung?ids=`, `GET /elemente?typen=` (Auswahllisten `{k,l}`) |
| unterricht | `GET /unterricht?sicht=klasse|lehrer|fach|alle&element=`, `GET /unterricht/spalten`, `POST /unterricht` (`{sicht, element, werte}`), `PATCH /unterricht/{uid}` (`{feld, wert, zeile}`), `POST /unterricht/{uid}/zeilen`, `DELETE /unterricht/{uid}/zeilen/{nr}`, `POST /unterricht/{uid}/kopieren`, `POST /unterricht/loeschen` |
| stundenplan | `GET /stundenplan/{typ}/{id}` (Zellen mit Einträgen je Kopplungszeile, nicht verplante Stunden), `GET /stundenplan/zielstatus?unterricht=&tag=&stunde=`, `POST /stundenplan/verplanen`, `POST /stundenplan/verschieben`, `POST /stundenplan/entplanen`, `POST /stundenplan/fixieren` |
| optimierung | `GET /optimierung/diagnose?rubrik=`, `GET /optimierung/kennzahlen`, `GET/PUT /optimierung/gewichtung` (`{werte}`), `GET /optimierung/gewichtung/analyse`, `GET /optimierung/strategien`, `POST /optimierung/start` (`{optionen, zuordnen}` → Hintergrundauftrag auf `Dokument.Klonen`), `GET /optimierung/auftrag` (Status/Fortschritt/Ergebnisse, Abfrage ~0,6 s), `POST /optimierung/auftrag/abbrechen`, `POST /optimierung/auftrag/uebernehmen` (`{nummer, trotzAenderung}`; 409 wenn das Dokument inzwischen geändert wurde), `DELETE /optimierung/auftrag`, `GET /optimierung/auftrag/plan/{nummer}/{typ}/{id}` (Ergebnisvergleich) |
| planung | `GET /planung/zielstatus?unterricht=&tag=&stunde=&einzeln=&laenge=` (`{status, gruende}` je Feld), `POST /planung/verschieben` (Block bzw. `einzeln`; `aktion` auto/entplanen/kollision – ohne Aktion bei Behinderung `{ok:false, frage}`), `POST /planung/verplanen` (`{unterricht, tag, stunde, laenge, aktion}`), `POST /planung/entplanen` / `POST /planung/fixieren` (`{stunden:[{unterricht,tag,stunde}]}`, Mehrfachauswahl), `POST /planung/alle-entplanen`, `POST /planung/alle-fixieren`, `GET/POST /planung/raum` (Raumliste bzw. zuordnen/löschen mit `umfang`, `loesung`), `GET /planung/tausch` (Tauschvorschläge), `POST /planung/tauschen` |
| zeitwuensche | `GET /zeitwuensche/{typ}` (Elemente mit Anzahl), `GET/PUT /zeitwuensche/{typ}/{id}` (`{aenderungen:[{tag,stunde,wert}]}` bzw. `{alleLoeschen}`), `PUT /zeitwuensche/{typ}/{id}/unbestimmte` – typ: klasse, lehrer, raum, fach, unterricht |

## Oberfläche

* **Kein Build-Schritt**, keine CDN-Abhängigkeit: ES-Module direkt aus `wwwroot`. Schrift = Systemschrift
  (Regular 400 / Medium 500). Farben als CSS-Variablen in `css/basis.css`; `html[data-modus="vertretung"]` schaltet auf Grün.
* `js/kern/` – wiederverwendbare Bausteine (bitte nutzen statt eigene zu bauen):
  * `api.js` – `api.get/post/put/patch/loeschen/hochladen`, `api.dok('/pfad')`, `einstellungLesen/einstellungSchreiben`.
    Option `{ quelle: fenster.id }` bei Änderungen, damit das auslösende Fenster sein eigenes `daten`-Ereignis überspringen kann.
  * `ereignisse.js` – `an(name, f)`, `melden(name, daten)`, `betrifft(ereignis, 'Stammdaten', …)`. Wichtige Ereignisse:
    `dokument`, `stand`, `daten`, `modus`, `element` (Fensterverknüpfung), `meldung`, `grunddaten`.
  * `zustand.js` – `zustand` (Benutzer, Dokument, Grunddaten, Modus), `elemente(typ)` (gecachte Auswahllisten), `darfSchreiben()`.
  * `dokument.js` – Öffnen/Schließen, Speichern, Rückgängig/Wiederholen, Ereignisverbindung.
  * `fenster.js` – Fenster-Manager. Fenstertyp: `{ titel, symbol, symbolFuer(p), breite, hoehe, einzig(p), erstellen(f, p) }`.
    Im Fenster: `f.titel()`, `f.werkzeug({text, symbol, aktion})`, `f.trenner()`, `f.inhalt`, `f.fuss`, `f.an(ereignis, f)`
    (wird beim Schließen abgemeldet), `f.parameterSetzen({...})` (fließt in Fensteranordnungen), `f.beimSchliessen()`.
    Fenster sind verschiebbar, in der Größe änderbar, minimier-/maximierbar, an Rand/Ecken andockbar, anordenbar;
    Anordnungen werden je Dokument automatisch und auf Wunsch benannt gespeichert.
  * `tabelle.js` – virtualisierte Tabelle (tausende Zeilen): Spalten sortieren/ein-/ausblenden/verschieben/Breite
    (gespeichert über `zustandSchluessel`), Inline-Bearbeitung mit Auswahllisten (Kurz-/Langname), Mehrfachauswahl,
    Tastatur wie im Desktop, Kopieren/Einfügen als Tabulator-Text, Neuanlage-Zeile, gruppierte Zeilen (Kopplungen).
  * `raster.js` – Stundenplan-Raster (Blöcke, Kopplungen nebeneinander, gesperrte Zeiten, Drag & Drop mit Zielstatus).
  * `dialog.js` – `dialog`, `meldung`, `frage`, `formular`, `dateiWaehlen`, `menue` (Kontextmenü), `hinweis`, `fehlerMelden`, `laden`.
  * `auswahl.js` – Vorschlagsliste für Eingabefelder (auch Listen „5a,5b“).
  * `symbole.js` – eigene SVG-Symbole (`symbol('lehrer')`); neue Symbole dort ergänzen.
  * `ribbon.js` – Reiter/Gruppen-Reihenfolge wie im Desktop (`REITER`); Einträge kommen aus den Modulen.
* Ein Oberflächen-Modul exportiert `{ id, css, fenster, befehle, ribbon, start }` (siehe Kopf von `js/kern/modulsystem.js`).
  Ribbon-Eintrag: `{ id, reiter, gruppe, text, symbol, gross, befehl, args, menue, hinweis, taste, braucht: 'dokument'|'schreiben'|'admin', gedrueckt }`.
  **Gleiche `id` ersetzt einen früheren Eintrag** – so lösen echte Module die Platzhalter aus `geplant.js` ab, ohne diese Datei zu ändern.

## Neues Modul anlegen (konfliktarm)

1. Server: `studio/src/UntPlanStudio/Api/<Modul>Api.cs`
   ```csharp
   public sealed class VertretungApi : IStudioModul
   {
       public string Name => "vertretung";
       public void DiensteRegistrieren(IServiceCollection d) { /* optional */ }
       public void EndpunkteRegistrieren(StudioRouten r)
       {
           var g = r.Dokument.MapGroup("/vertretung");
           g.MapGet("/tag/{datum}", (Arbeitsdokument a, DateOnly datum) => a.LesenAsync(doc => /* DTO */));
           g.MapPost("/absenzen", (Arbeitsdokument a, NeueAbsenz n, ClaimsPrincipal p) =>
               a.AendernAsync(p.Benutzername(), "Absenz angelegt", AenderungsArt.Vertretung, d => { /* Core */ }, doc => /* DTO */));
       }
   }
   ```
   Eigene Dienste/Hilfsklassen in `Dienste/<Modul>*.cs`. Eine Zeile in `Api/Modulregister.cs` (Erweiterungsstelle).
2. Oberfläche: `wwwroot/js/module/<modul>.js` (Fenstertypen `'<modul>.<fenster>'`, Befehle `'<modul>.<befehl>'`,
   Ribbon-Einträge mit den ids aus der Tabelle unten), optional `wwwroot/css/module/<modul>.css` (Klassen mit Modulpräfix).
   Zwei Zeilen in `wwwroot/js/module/register.js` (Import + Liste).
3. Tests: `studio/tests/UntPlanStudio.Tests/<Modul>Tests.cs` (`IClassFixture<StudioFabrik>`, Hilfen in `StudioFabrik.cs`).
   Die Syntax aller JS-Module und das Modulregister werden automatisch geprüft.
4. Benutzereinstellungen mit Präfix `<modul>.`; CSS-Klassen mit Präfix; keine Änderungen an Kern-Dateien nötig –
   fehlt im Kern etwas Allgemeines, klein und rückwärtskompatibel ergänzen.

## Menüband-Ids der Platzhalter (`js/module/geplant.js`)

| Modul (vorgeschlagen) | ids |
|---|---|
| optimierung | `start.diagnose`, `start.gewichtung`, `start.optimierung`, `planung.diagnose`, `planung.gewichtung`, `planung.optimierung` |
| planung | `planung.tausch`, `planung.raum`, `planung.entplanen` |
| einstellungen | `start.perioden`, `start.einstellungen`, `start.ferien`, `eingabe.ugruppen`, `eingabe.zeitraster`, `eingabe.ferien`, `eingabe.schuljahr`, `module.perioden`, `module.ugruppen` |
| zeitwuensche | `eingabe.zeitwuensche`, `planung.uzeitwuensche` |
| vertretung | `vertretung.kalender`, `vertretung.absenzen`, `vertretung.vertretungen`, `vertretung.sondereinsaetze`, `vertretung.bereitschaften`, `vertretung.planungsdialog`, `vertretung.vormerkungen`, `vertretung.formate`, `vertretung.einstellungen`, `eingabe.bereitschaft`, `module.bereitschaft` |
| veranstaltungen | `vertretung.veranstaltungen` |
| pausenaufsicht | `start.pausenaufsichten`, `vertretung.pausenaufsichten` |
| abrechnung | `start.wertrechnung`, `eingabe.stundentafel`, `vertretung.statistik`, `module.sollist`, `module.matrix`, `module.stundentafel` |
| druck | `start.infostundenplan`, `start.berichte`, `stundenplaene.drucken`, `stundenplaene.html`, `stundenplaene.ical`, `vertretung.ausgabe` |
| uebersicht | `stundenplaene.uebersicht.klasse`, `…lehrer`, `…raum` |
| untplanweb | `start.untplanweb`, `module.untplanweb` |
| austausch | `eingabe.importexport`, `kurs.import` |
| kursplanung | `start.kursplanung`, `kurs.kurse` |
| kommentare | `eingabe.kommentare` |
| multiuser | `start.multiuser` |

Der Vertretungsmodus (`start.vertretungsmodus`, Strg+Umschalt+V) ist im Modul `start` umgesetzt; der Reiter
„Vertretungsplanung“ erscheint nur im Vertretungsmodus (`modus: 'vertretung'` in `REITER`).

## Leistung (große Schulen)

* Server: Lesen unter der Dokumentsperre, `DatenIndex` des Core (Wörterbücher), Antworten als kompakte Text-Arrays,
  Antwortkomprimierung (Brotli/Gzip). Test `GrosseSchuleBleibtSchnell`: 150 Lehrkräfte, 80 Klassen, 3200 Unterrichte –
  alle Unterrichte ≈ 0,1 s, Lehrkräfte-Tabelle ≈ 0,35 s, Lehrerplan < 0,05 s, Wert ändern ≈ 0,05 s.
* Browser: Tabellen rendern nur die sichtbaren Zeilen; Fenster aktualisieren sich gezielt nach `bereich` des Ereignisses.
* `Dokument.Aendern` serialisiert für die Rückgängig-Stufe das ganze Dokument – Sammeländerungen deshalb in EINEM Aufruf.
