# UntPlan Web-Portal – Sync-API (Version 1)

Die Desktop-Anwendung **UntPlan** ist die führende Datenquelle. Das Web-Portal veröffentlicht
Stunden- und Vertretungspläne (Schüler, Eltern, Lehrkräfte, Infobildschirme) und sammelt
Meldungen ein (z. B. Krankmeldungen von Lehrkräften), die in UntPlan übernommen werden.

```
 UntPlan Desktop ──PUT snapshot──▶  Web-Portal  ◀── Browser / App (lesen)
        ▲                               │
        └──GET meldungen / quittieren───┘  ◀── Lehrkraft meldet sich krank
```

Ein lauffähiger Referenzserver liegt unter `server/portal-server.mjs` (`npm run portal`).

## Grundlagen

- Basis-URL: `https://<portal>/api/v1`
- Format: JSON, UTF-8
- Authentifizierung der Desktop-Anwendung: `Authorization: Bearer <API-Schlüssel>` (je Schule)
- Fehler: HTTP-Status ≥ 400 mit `{ "meldung": "Beschreibung" }`
- Schul-ID: frei wählbar, empfohlen ist die 6-stellige NRW-Schulnummer

## Endpunkte

### `GET /status`
Erreichbarkeit/Version. Antwort: `{ "name": "…", "version": "0.1.0" }`

### `PUT /schulen/{schulId}/snapshot` 🔒
Überträgt den vollständigen Stand (Schema `untplan-sync/1`, siehe `src/core/sync.ts`).

| Feld | Inhalt |
|---|---|
| `schema` | `"untplan-sync/1"` |
| `erzeugt` | ISO-Zeitstempel |
| `basisRevision` | zuletzt bestätigte Revision (für spätere Konfliktprüfung) |
| `schule` | Name, Schulnummer, Schulform, Schuljahr, Ort |
| `zeitraster` | Tage und Stunden mit Uhrzeiten |
| `lehrer` | Kürzel, Name, dienstliche E-Mail (nur aktive Lehrkräfte) |
| `klassen`, `raeume`, `faecher` | Stammdaten (Kürzel/Namen) |
| `stundenplan` | Regelplan: Tag, Stunde, Fach, Klassen, Lehrkräfte, Raum, Gruppe |
| `vertretungen` | Vertretungen der nächsten *n* Unterrichtstage (Einstellung „Tage voraus“) |
| `nachrichten` | Nachrichten zum Tag (`intern: true` = nur Lehrkräfte) |
| `freieTage` | Ferien, Feiertage, bewegliche Ferientage |

Antwort: `{ "ok": true, "revision": 42 }`

**Nicht übertragen** werden Abwesenheitsgründe (Gesundheitsdaten), Notizen, Deputate,
Anrechnungen und Mehrarbeitsdaten.

### `GET /schulen/{schulId}/meldungen?seit={seq}` 🔒
Offene (nicht quittierte) Meldungen aus dem Portal.

```json
{ "meldungen": [ { "id": "m7", "typ": "abwesenheit", "erstellt": "…", "absender": "MUE",
  "lehrerKuerzel": "MUE", "von": "2026-10-14", "bis": "2026-10-15", "grund": "Krankheit", "text": "" } ] }
```

### `POST /schulen/{schulId}/meldungen/quittieren` 🔒
`{ "ids": ["m7"] }` – markiert Meldungen als verarbeitet.

### Öffentliche bzw. Portal-interne Endpunkte
- `GET /schulen/{schulId}/vertretungen?datum=YYYY-MM-DD&klasse=5a`
- `GET /schulen/{schulId}/stundenplan?klasse=5a` bzw. `?lehrer=MUE`
- `POST /schulen/{schulId}/meldungen` – Meldung einreichen (im Produktivbetrieb nur nach Anmeldung,
  z. B. über LOGINEO NRW / SSO; im Referenzserver ungeschützt zu Demozwecken)

## Ablauf einer Synchronisation (Desktop)

1. Paket mit `buildSyncPayload()` erzeugen
2. `PUT snapshot` → neue Revision speichern (`sync.letzteRevision`)
3. `GET meldungen` → Meldungen in UntPlan anzeigen, nach Bestätigung übernehmen
   (`uebernehmeMeldungen()` legt Abwesenheiten an)
4. `POST meldungen/quittieren`

Optional automatisch alle 5 Minuten, solange UntPlan geöffnet ist.

## Ausbaustufen (geplant)

- Inkrementelle Übertragung (nur Änderungen seit `basisRevision`) und Push-Benachrichtigungen
- Rollen & Anmeldung im Portal (Schüler, Eltern, Lehrkräfte, Schulleitung), Anbindung LOGINEO NRW
- Persönliche Stundenpläne/iCal-Abos je Lehrkraft und Klasse
- Raumbuchung und Sprechstundenbuchung über das Portal
- Mehrere Vertretungsplaner gleichzeitig (Sperrkonzept/Konfliktauflösung je Datensatz)
