# UntPlanWeb – Schnittstelle und Vorbereitung

UntPlanWeb ist das webbasierte Gegenstück zu UntPlan: Stunden- und Vertretungspläne im Browser und in Apps für
Schülerinnen und Schüler, Lehrkräfte und Eltern, dazu Krankmeldungen, Nachrichten und Raumbuchungen. UntPlanWeb wird
in einem **eigenen Repository** entwickelt. Dieses Dokument beschreibt alles, was UntPlan dafür bereitstellt.

## Architektur

```
UntPlan (Desktop, führend)  ──PUT  Paket untplanweb/1──▶  UntPlanWeb (Server + Weboberfläche)
                            ◀─GET  Rückmeldungen ───────   (Krankmeldungen, Nachrichten, Raumbuchungen)
                            ──POST Quittieren ─────────▶
```

* UntPlan bleibt die **führende Quelle**. UntPlanWeb speichert jeweils das zuletzt übertragene Paket (und darf ältere
  Revisionen ablehnen: HTTP 409).
* Rückmeldungen aus UntPlanWeb werden in UntPlan geprüft und übernommen (Krankmeldung → Lehrer-Absenz, Nachricht →
  Tagestext für das Kollegium) und anschließend quittiert.

## Gemeinsame Verträge: `src/UntPlan.Web.Contracts`

Die Klassenbibliothek **UntPlan.Web.Contracts** (net8.0, keine Abhängigkeiten) enthält alle Datenverträge. UntPlanWeb
bindet sie unverändert ein – als Git-Submodul/Projektverweis oder als NuGet-Paket (`dotnet pack src/UntPlan.Web.Contracts`).

| Typ | Inhalt |
|---|---|
| `WebPaket` | Schema, Revision, Schule, Zeitraster, Perioden (Unterricht + Regelstundenplan), Elemente, tagesaktuelle Pläne (`Tage`), Vertretungen, Absenzen, Termine (Veranstaltungen, Klausuren), Tagestexte, Ferien, Pausenaufsichten |
| `WebTag` / `WebStunde` | aufgelöster Plan je Datum: Fach, Klassen, Lehrer, Räume, Status `regulaer`, `vertreten`, `offen`, `entfall`, `zusaetzlich`, `frei` |
| `WebVertretung` | Vertretungsplanzeile wie im Aushang (Art, (Lehrer), Vertreter, Fach, Raum, Vertr. von, (Le.) nach, Text, `nurLehrer`) |
| `WebRueckmeldung` | `absenz`, `nachricht`, `raumbuchung` |
| `WebAntwort` | `ok`, `revision`, `meldung` |

JSON: camelCase, Datum `yyyy-MM-dd`, Uhrzeit `HH:mm`, Nullwerte werden weggelassen (`UntPlanWebSchema.Json`).

## REST-Endpunkte (`UntPlanWebSchema.Pfade`)

Authentifizierung: `Authorization: Bearer <API-Schlüssel der Schule>`, nur über HTTPS.

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `api/v1/status` | Name, Version, Schema |
| PUT | `api/v1/schulen/{schule}/untplanweb` | `WebPaket` übertragen → `WebAntwort` (409 bei veralteter Revision) |
| GET | `api/v1/schulen/{schule}/rueckmeldungen` | offene `WebRueckmeldung[]` |
| POST | `api/v1/schulen/{schule}/rueckmeldungen/quittieren` | `string[]` Ids → übernommen |

Der Referenzserver `src/UntPlan.Portal` implementiert diese Endpunkte (zusätzlich zum älteren Schema `untplan-sync/1`,
siehe `SYNC_API.md`) und dient UntPlanWeb als Vorlage und für Tests (`tests/UntPlan.Core.Tests/UntPlanWebTests.cs`).

## Datenschutz (DSGVO)

`WebExportOptionen` steuert, was übertragen wird:

* E-Mail-Adressen der Lehrkräfte nur mit Freigabe (`LehrerEmail`).
* Lehrer-Absenzen nur als Zeitraum (`LehrerAbsenzen`), Absenzgründe nur mit Freigabe (`Absenzgruende`);
  **als vertraulich markierte Gründe werden nie übertragen**.
* Tagestexte nur, wenn „Portal“ angehakt ist; Vertretungszeilen mit `nurLehrer` gehören nicht in die Schülerversion.
* Übertragen wird nur ein Zeitfenster (`TageZurueck` … `TageVoraus`) der tagesaktuellen Daten.

## In UntPlan

*Datei → UntPlanWeb*: „An UntPlanWeb übertragen“, „Rückmeldungen abholen und übernehmen“, „Paket als Datei exportieren
(JSON)“ und „Verbindung einstellen…“ (Adresse, Schul-Id, API-Schlüssel – gemeinsam mit dem Web-Portal).

## Empfehlungen für das UntPlanWeb-Repository

* ASP.NET Core (Minimal API) + Datenbank (PostgreSQL/SQL Server) für Pakete, Benutzer, Rückmeldungen.
* Rollen: Schüler/Eltern (Klassenplan, Vertretungen der Klasse), Lehrkräfte (eigener Plan, Lehrerversion, Krankmeldung),
  Schulleitung/Admin (Freigaben, API-Schlüssel).
* Ansichten aus `WebPaket.Tage` erzeugen (bereits aufgelöst – keine Planlogik im Web nötig), Regelplan aus `Perioden`.
* Kompatibilität: neue Felder nur ergänzen; inkompatible Änderungen erhalten ein neues Schema (`untplanweb/2`).
