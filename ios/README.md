# UntPlan für iOS

Native iOS-App (SwiftUI) zu **UntPlanWeb**. Sie zeigt Startseite, Stundenplan, Mitteilungen und Profil im Stil von
UntPlanMobile (PWA), spricht aber direkt die JSON-API von UntPlanWeb unter `/api/app` an. Eine eigene Datenbank hat
die App nicht; angemeldet wird mit dem Konto von UntPlanWeb.

## Funktionen

- **Start**: Schulname, „Infos zum Tag“ als wischbare Karten (Nachrichten blau, Veranstaltungen grün, Hinweise),
  Tageskarte mit den nächsten Stunden, Pausenaufsichten, Termine der nächsten zwei Wochen und Hausaufgaben.
- **Stundenplan**: Wochenraster mit Tagesspalten, Woche vor/zurück (auch per Wischen), Karten mit Fach,
  Lehrkraft/Klasse und Raum, Farbbalken in Fachfarbe. Entfall rot und durchgestrichen, Änderungen grün,
  Raumänderungen grün wie Änderungen, nur Prüfungen gelb, Ferien blau. Darstellung wie in UntPlanMobile (`Dienste/Kachelbau.swift`): abgegebene Stunden und
  Bereitschaften abwesender Lehrkräfte mit Eckklammern und durchgestrichenem Kürzel, Veranstaltungen als hellgrünes Feld
  über den ganzen Zeitraum, Pausenaufsichten als Pillen in den Pausen, vergangene farbige Stunden ohne Rahmen.
  Tagesansicht mit großen Karten (Uhrzeit, Fach-Langname, Status, Hinweis). Antippen öffnet die Stundendetails
  (mehrere Stunden derselben Zeit als Reiter; auf dem iPad als Formblatt mit fester Größe). Andere Pläne (Klassen, Lehrkräfte, Räume)
  lassen sich oben rechts wählen, soweit das Konto sie sehen darf.
- **Mitteilungen**: Posteingang und Gesendet, Lesen (wird als gelesen markiert), Löschen per Wischen,
  Zähler ungelesener Mitteilungen in der Tab-Leiste.
- **Profil**: Kontodaten, Server, Kalender-Abo (iCal) und Abmelden. Für Lehrkräfte der Schalter
  „Mitteilungen bei Planänderungen“.
- **Mitteilungen bei Planänderungen** (nur Lehrkräfte, `Dienste/Planmitteilungen.swift`): Beim Öffnen und im
  Hintergrund (BGAppRefreshTask, frühestens alle 30 Minuten) vergleicht die App den eigenen Plan der aktuellen und
  nächsten Woche mit dem zuletzt gesehenen Stand und meldet Entfall, Vertretung, Raumänderung, neue Stunden und
  Prüfungen als lokale Mitteilung (ab vier Änderungen gebündelt). Antippen öffnet den Tag im Stundenplan.

## Öffnen in Xcode

1. `ios/UntPlanMobile.xcodeproj` in Xcode 15 oder neuer öffnen.
   Alternativ mit [XcodeGen](https://github.com/yonaskolb/XcodeGen) im Ordner `ios/` `xcodegen generate` ausführen.
2. Ziel **UntPlanMobile** wählen, unter *Signing & Capabilities* ein **Team** eintragen
   (bei Bedarf die Bundle-ID `de.untplan.mobile` anpassen, falls sie im eigenen Team schon vergeben ist).
3. Simulator oder Gerät mit iOS 17 oder neuer wählen und starten (⌘R).
4. Für die Mitteilungen bei Planänderungen: unter *Signing & Capabilities* die Fähigkeit **Background Modes** mit
   **Background fetch** prüfen bzw. hinzufügen. `UntPlanMobile/Info.plist` (per `INFOPLIST_FILE` mit der generierten
   Info.plist zusammengeführt) enthält bereits `UIBackgroundModes` (`fetch`) und `BGTaskSchedulerPermittedIdentifiers`
   (`de.untplan.mobile.planpruefung`). Testen im Debugger: App in den Hintergrund, pausieren und
   `e -l objc -- (void)[[BGTaskScheduler sharedScheduler] _simulateLaunchForTaskWithIdentifier:@"de.untplan.mobile.planpruefung"]`
   ausführen.

## Anmelden

Beim ersten Start fragt die App nach

- **Server-Adresse** – die Adresse von UntPlanWeb, z. B. `plan.meine-schule.de` (ohne `https://` wird es ergänzt;
  läuft UntPlanWeb unter einem Unterpfad, diesen mit angeben),
- **Benutzername** und **Passwort** des UntPlanWeb-Kontos.

Die Server-Adresse wird in den Einstellungen der App gespeichert, das Passwort nur bei „Passwort merken“ im
Schlüsselbund des Geräts. Die Sitzung läuft über das Anmelde-Cookie von UntPlanWeb; ist es abgelaufen, meldet sich
die App mit dem gemerkten Passwort selbst wieder an.

## Hinweise

- UntPlanWeb muss per **HTTPS** erreichbar sein. iOS blockiert unverschlüsselte Verbindungen (App Transport Security);
  für Tests im eigenen Netz lässt sich in Xcode eine Ausnahme (`NSAppTransportSecurity`) ergänzen.
- Alle ändernden Aufrufe senden den Kopf `X-UntPlanWeb: 1`, den UntPlanWeb als Schutz verlangt.
- Das App-Symbol ist ein Platzhalter: Eine Grafik mit 1024 × 1024 Pixeln in
  `UntPlanMobile/Assets.xcassets/AppIcon.appiconset` ablegen.

## Genutzte Endpunkte

| Methode | Pfad | Zweck |
|---|---|---|
| POST | `/api/app/anmelden` | Anmeldung `{login, passwort}`, setzt das Cookie |
| POST | `/api/app/abmelden` | Abmelden |
| GET | `/api/app/sitzung` | Schule, Benutzer, ungelesene Mitteilungen |
| GET | `/api/app/heute` | Startseite (Stunden, Nachrichten, Termine …) |
| GET | `/api/app/plan?typ=&element=&datum=` | Wochenplan (Zeitraster, Tage, Karten) |
| GET | `/api/app/elemente` | Klassen, Lehrkräfte, Räume für die Planauswahl |
| GET | `/api/app/mitteilungen?ordner=` | Posteingang bzw. `gesendet` |
| POST | `/api/app/mitteilungen/{id}/gelesen` | Als gelesen markieren |
| DELETE | `/api/app/mitteilungen/{id}` | Mitteilung löschen |
| GET | `/api/app/profil` | Profil und iCal-Adresse |

## Aufbau

```
ios/
├── UntPlanMobile.xcodeproj
├── project.yml                  XcodeGen-Alternative
└── UntPlanMobile/
    ├── UntPlanMobileApp.swift   Einstieg
    ├── Modelle/                 Codable-Modelle der API, nachsichtige Dekodierung
    ├── Dienste/                 API-Client, Sitzungszustand, Schlüsselbund, Datumsformate
    ├── Ansichten/               Anmeldung, Start, Stundenplan, Details, Mitteilungen, Profil
    └── Assets.xcassets          App-Symbol (Platzhalter), Akzentfarbe Orange
```
