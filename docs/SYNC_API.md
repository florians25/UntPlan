# WebPortal-Synchronisation – Schnittstelle `untplan-sync/1`

UntPlan (Desktop) ist **führend**: Stammdaten, Stundenplan und Vertretungen werden nur in der Desktop-Anwendung bearbeitet.
Das WebPortal zeigt sie an und schickt **Meldungen** (z. B. Krankmeldungen von Lehrkräften) zurück.

Eine lauffähige Referenzimplementierung liegt in `src/UntPlan.Portal`. Sie wird mit `dotnet run --project src/UntPlan.Portal` gestartet.
Die Schulen und Schlüssel stehen in `appsettings.json`.

## Ablauf

1. In UntPlan unter **Module → Web-Portal** die Portal-Adresse, die Schul-ID und den API-Schlüssel eintragen.
2. **Synchronisieren** führt drei Schritte aus:
   - Es lädt den Snapshot hoch (`PUT …/snapshot`). Das Portal antwortet mit einer neuen Revision.
   - Es holt die offenen Meldungen ab (`GET …/meldungen`). Abwesenheitsmeldungen werden als Lehrer-Abwesenheiten übernommen; daraus entstehen automatisch offene Vertretungen.
   - Es quittiert die übernommenen Meldungen (`POST …/meldungen/quittieren`).

## Authentifizierung

| Rolle | Verfahren |
|---|---|
| Desktop-Anwendung | Header `Authorization: Bearer <ApiSchluessel>` |
| Lehrkraft (Meldung) | Feld `pin` (MeldeSchluessel der Schule) im Formular/JSON |
| Öffentlich | Vertretungsplan in der Schülerversion, wenn `Oeffentlich: true` ist |

Schlüssel werden in konstanter Zeit verglichen. Im Produktivbetrieb läuft das Portal ausschließlich über HTTPS, z. B. hinter einem Reverse-Proxy.

## Endpunkte (Basis `/api/v1`)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/status` | `{ name, version, schema }` |
| PUT | `/schulen/{id}/snapshot` | Snapshot hochladen → `{ ok, revision, meldung }` |
| GET | `/schulen/{id}/snapshot` | zuletzt hochgeladener Snapshot (mit API-Schlüssel) |
| GET | `/schulen/{id}/meldungen?seit=0` | `{ meldungen: [Meldung] }` (nur unquittierte) |
| POST | `/schulen/{id}/meldungen/quittieren` | `{ ids: ["…"] }` → `{ ok, quittiert }` |
| POST | `/schulen/{id}/meldungen` | Meldung einer Lehrkraft (JSON oder Formular) |

Fehler werden als `{ "ok": false, "meldung": "…" }` mit HTTP 400/401/404 beantwortet.

## Snapshot

JSON-Objekt mit Feldnamen in camelCase:

```json
{
  "schema": "untplan-sync/1",
  "erzeugt": "2026-10-13T07:12:00",
  "basisRevision": 41,
  "schule": { "name": "…", "schulnummer": "199999", "schulform": "Gymnasium", "bundesland": "NRW" },
  "zeitraster": { "tage": 5, "stunden": [ { "nr": 1, "beginn": "07:55", "ende": "08:40" } ] },
  "lehrer":   [ { "kuerzel": "FIS", "name": "Michael Fischer", "email": "" } ],
  "klassen":  [ { "name": "5a", "jahrgang": "5", "klassenleitung": ["FIS"] } ],
  "raeume":   [ { "kuerzel": "R101", "name": "Klassenraum 101" } ],
  "faecher":  [ { "kuerzel": "D", "name": "Deutsch", "farbe": "#F28C82" } ],
  "stundenplan": [ { "tag": 0, "stunde": 1, "fach": "D", "klassen": ["5a"], "lehrer": ["FIS"], "raum": "R101", "gruppe": "", "woche": "" } ],
  "vertretungen": [ { "datum": "2026-10-13", "stunde": 3, "art": "Vertretung", "klassen": ["6b"], "fach": "PH", "lehrer": "KRU", "vertreter": "WEB", "raum": "PH1", "text": "" } ],
  "nachrichten": [ { "datum": "2026-10-13", "text": "…", "intern": false } ],
  "freieTage":   [ { "name": "Herbstferien", "von": "2026-10-17", "bis": "2026-10-31", "feiertag": false } ]
}
```

Bedeutung einzelner Felder:

- `tag`: 0 ist der erste Schultag des Zeitrasters.
- `woche`: `""` für jede Woche, sonst `"A"` oder `"B"`.
- `vertretungen`: umfasst die nächsten `TageVoraus` Tage (Einstellung im Desktop, Standard 7).
- `art`: Vertretung, Entfall, Raum-Vtr., Verlegung, statt-Vertretung, Betreuung, Sondereinsatz, Freisetzung, Tausch oder Klausur.

## Meldung

```json
{ "id": "…", "typ": "abwesenheit", "erstellt": "2026-10-13T06:40:00", "absender": "KRU", "lehrerKuerzel": "KRU",
  "von": "2026-10-13", "bis": "2026-10-14", "stundeVon": null, "stundeBis": null, "grund": "Krankheit", "text": "" }
```

- `typ = "abwesenheit"`: Daraus wird eine Lehrer-Abwesenheit. Der Grund wird über Kurz- oder Langname den Abwesenheitsgründen zugeordnet.
- `typ = "nachricht"`: Daraus wird ein Tagestext „nur für Lehrer“.

Meldungen mit unbekanntem Kürzel werden nicht übernommen und bleiben im Portal offen.
