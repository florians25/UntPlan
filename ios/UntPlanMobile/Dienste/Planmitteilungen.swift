import Foundation
import BackgroundTasks
import UIKit
import UserNotifications

/// Lokale Mitteilungen, sobald sich etwas im eigenen Stundenplan geändert hat – nur für angemeldete Lehrkräfte
/// (nie für Schüler*innen und Eltern).
///
/// Ablauf: Beim Öffnen bzw. Zurückkehren in den Vordergrund und im Hintergrund (BGAppRefreshTask, etwa alle 30–60 min,
/// das System entscheidet über den genauen Zeitpunkt) lädt die App den eigenen Plan der aktuellen und der nächsten Woche,
/// vergleicht ihn mit dem zuletzt gespeicherten Stand (Entfall, Vertretung, Raumänderung, neue Stunde, Prüfung …) und
/// sendet je Änderung – ab vier Änderungen gebündelt – eine lokale Mitteilung („Mo 3. Std. D 7a: Raum R105 → R201“).
/// Ein Tipp auf die Mitteilung öffnet den Tag im Stundenplan.
enum Planmitteilungen {
    /// Kennung der Hintergrund-Aufgabe (muss in Info.plist unter BGTaskSchedulerPermittedIdentifiers stehen).
    static let kennung = "de.untplan.mobile.planpruefung"
    /// Schalter im Profil (Standard: an).
    static let schalterSchluessel = "planmitteilungenAktiv"
    /// Zuletzt gesehener Stand des eigenen Plans (JSON in den UserDefaults).
    static let standSchluessel = "planmitteilungenStand"
    /// Datum (yyyy-MM-dd) im userInfo der Mitteilung.
    static let datumSchluessel = "datum"
    /// Meldung an die Oberfläche: eine Mitteilung wurde angetippt (Tag im Plan öffnen).
    static let tippBenachrichtigung = Notification.Name("UntPlanPlanmitteilung")
    /// Noch nicht geöffneter Tag aus einer angetippten Mitteilung (z. B. beim Kaltstart).
    static var ausstehenderTag: String? = nil
    /// Frühester Abstand bis zur nächsten Hintergrund-Prüfung.
    static let abstand: TimeInterval = 30 * 60

    static var aktiv: Bool {
        UserDefaults.standard.object(forKey: schalterSchluessel) as? Bool ?? true
    }

    /// Nur Lehrkräfte mit eigenem Lehrerplan (Kollegium, nicht Schüler*innen bzw. Eltern).
    static func berechtigt(_ benutzer: SitzungsBenutzer?) -> Bool {
        guard let b = benutzer else { return false }
        let rolle = b.rolle.lowercased()
        guard b.kollegium, rolle != "schueler", rolle != "eltern" else { return false }
        return b.typ.lowercased() == "lehrer" && !b.element.isEmpty
    }

    // MARK: Hintergrund-Aufgabe

    /// Beim Start registrieren (vor dem Ende von didFinishLaunching).
    static func registrieren() {
        BGTaskScheduler.shared.register(forTaskWithIdentifier: kennung, using: nil) { aufgabe in
            guard let auffrischen = aufgabe as? BGAppRefreshTask else {
                aufgabe.setTaskCompleted(success: false)
                return
            }
            bearbeiten(auffrischen)
        }
    }

    /// Nächste Hintergrund-Prüfung frühestens in 30 Minuten anmelden.
    static func planen() {
        guard aktiv else { return }
        let anfrage = BGAppRefreshTaskRequest(identifier: kennung)
        anfrage.earliestBeginDate = Date(timeIntervalSinceNow: abstand)
        do {
            try BGTaskScheduler.shared.submit(anfrage)
        } catch {
            // Simulator, Hintergrundaktualisierung ausgeschaltet oder zu viele Anfragen: beim nächsten Öffnen erneut
        }
    }

    /// Hintergrund-Prüfung abbestellen (Schalter aus, Abmelden).
    static func abbestellen() {
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: kennung)
    }

    private static func bearbeiten(_ aufgabe: BGAppRefreshTask) {
        let arbeit = Task {
            let ergebnis = await Planpruefer.shared.pruefen()
            if ergebnis != .nichtBerechtigt { planen() }
            aufgabe.setTaskCompleted(success: ergebnis != .fehler)
        }
        aufgabe.expirationHandler = { arbeit.cancel() }
    }

    // MARK: Erlaubnis

    /// Erlaubnis für Mitteilungen erfragen (nur beim ersten Mal fragt das System nach).
    @discardableResult
    static func erlaubnisAnfragen() async -> Bool {
        let zentrale = UNUserNotificationCenter.current()
        let einstellungen = await zentrale.notificationSettings()
        switch einstellungen.authorizationStatus {
        case .authorized, .provisional, .ephemeral:
            return true
        case .notDetermined:
            return (try? await zentrale.requestAuthorization(options: [.alert, .sound, .badge])) ?? false
        case .denied:
            return false
        @unknown default:
            return false
        }
    }

    /// Hat die Person Mitteilungen in den Einstellungen ausgeschaltet?
    static func verweigert() async -> Bool {
        await UNUserNotificationCenter.current().notificationSettings().authorizationStatus == .denied
    }

    private static func erlaubt() async -> Bool {
        switch await UNUserNotificationCenter.current().notificationSettings().authorizationStatus {
        case .authorized, .provisional, .ephemeral: return true
        default: return false
        }
    }

    // MARK: Senden und Antippen

    /// Je Änderung eine Mitteilung; ab vier Änderungen eine gebündelte Mitteilung.
    static func senden(_ liste: [Planaenderung]) async {
        guard !liste.isEmpty, await erlaubt() else { return }
        let zentrale = UNUserNotificationCenter.current()
        if liste.count <= 3 {
            for a in liste {
                let inhalt = UNMutableNotificationContent()
                inhalt.title = "Stundenplan geändert"
                inhalt.body = a.text
                inhalt.sound = .default
                inhalt.threadIdentifier = "planaenderungen"
                inhalt.userInfo = [datumSchluessel: a.datum]
                let anfrage = UNNotificationRequest(identifier: "planaenderung-\(a.datum)-\(a.von)-\(UUID().uuidString)",
                                                    content: inhalt, trigger: nil)
                try? await zentrale.add(anfrage)
            }
        } else {
            let inhalt = UNMutableNotificationContent()
            inhalt.title = "\(liste.count) Änderungen im Stundenplan"
            var zeilen = liste.prefix(4).map(\.text)
            if liste.count > 4 { zeilen.append("+ \(liste.count - 4) weitere") }
            inhalt.body = zeilen.joined(separator: "\n")
            inhalt.sound = .default
            inhalt.threadIdentifier = "planaenderungen"
            inhalt.userInfo = [datumSchluessel: liste[0].datum]
            let anfrage = UNNotificationRequest(identifier: "planaenderungen-\(UUID().uuidString)", content: inhalt, trigger: nil)
            try? await zentrale.add(anfrage)
        }
    }

    /// Angetippte Mitteilung: Tag merken und die Oberfläche benachrichtigen.
    static func tagOeffnen(_ datum: String) {
        ausstehenderTag = datum
        NotificationCenter.default.post(name: tippBenachrichtigung, object: nil)
    }

    /// Beim Abmelden: gespeicherten Stand löschen, Hintergrund-Prüfung abbestellen.
    static func zuruecksetzen() {
        UserDefaults.standard.removeObject(forKey: standSchluessel)
        abbestellen()
    }
}

// MARK: - Prüfung

/// Lädt den eigenen Plan, vergleicht ihn mit dem gespeicherten Stand und meldet Änderungen. Läuft nie doppelt.
actor Planpruefer {
    static let shared = Planpruefer()

    enum Ergebnis: Equatable {
        case erledigt(Int)
        case nichtBerechtigt
        case fehler
    }

    private var laeuft = false

    /// - Parameter melden: false = nur den Stand übernehmen (z. B. nach einer eigenen Raumänderung in der App).
    func pruefen(melden: Bool = true) async -> Ergebnis {
        guard Planmitteilungen.aktiv else { return .nichtBerechtigt }
        guard !laeuft else { return .erledigt(0) }
        laeuft = true
        defer { laeuft = false }
        let api = ApiClient.shared
        guard !api.serverAdresse.isEmpty else { return .nichtBerechtigt }
        do {
            guard let benutzer = try await angemeldeterBenutzer(api) else { return .nichtBerechtigt }
            guard Planmitteilungen.berechtigt(benutzer) else { return .nichtBerechtigt }
            let jetzt = Date()
            let montag = Datumsformat.montag(jetzt)
            async let diese = api.plan(typ: "lehrer", element: benutzer.element, datum: montag)
            async let naechste = api.plan(typ: "lehrer", element: benutzer.element, datum: Datumsformat.tage(7, nach: montag))
            let plaene = try await [diese, naechste]
            try Task.checkCancellation()
            let neu = Planstand(besitzer: "\(api.serverAdresse)|\(benutzer.login)|\(benutzer.element)", plaene: plaene)
            let alt = Planstand.laden()
            neu.speichern()
            guard melden, let alt, alt.besitzer == neu.besitzer else { return .erledigt(0) }
            let aenderungen = Planvergleich.aenderungen(alt: alt, neu: neu, jetzt: jetzt)
            await Planmitteilungen.senden(aenderungen)
            return .erledigt(aenderungen.count)
        } catch {
            return .fehler
        }
    }

    /// Bestehende Sitzung, sonst mit gemerktem Passwort neu anmelden (wie beim Start der App).
    private func angemeldeterBenutzer(_ api: ApiClient) async throws -> SitzungsBenutzer? {
        if let sitzung = try? await api.sitzung(), let b = sitzung.benutzer { return b }
        try Task.checkCancellation()
        let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
        guard !login.isEmpty,
              let passwort = Schluesselbund.lesen(konto: Schluesselbund.konto(login: login, server: api.serverAdresse))
        else { return nil }
        try await api.anmelden(login: login, passwort: passwort)
        return try await api.sitzung().benutzer
    }
}

// MARK: - Gespeicherter Stand

/// Eine Stunde des eigenen Plans, auf das Vergleichbare reduziert.
struct Stundeneintrag: Codable, Equatable {
    var datum: String
    var von: Int
    var bis: Int
    var ende: String
    var status: String
    var fach: String
    var klassen: [String]
    var raeume: [String]
    var lehrer: [String]
    var pruefung: Bool
    var raumbuchung: Bool
    var unterricht: Int?

    init(_ k: Karte) {
        datum = k.datum
        von = k.von
        bis = k.bis
        ende = k.ende
        status = k.status
        fach = k.fach
        klassen = k.klassen.map(\.k).filter { !$0.isEmpty }.sorted()
        raeume = k.raeume.map(\.k).filter { !$0.isEmpty }.sorted()
        lehrer = k.lehrer.map(\.k).filter { !$0.isEmpty }.sorted()
        pruefung = k.istPruefung
        raumbuchung = k.istRaumbuchung
        unterricht = k.unterricht
    }

    /// Stunden, die verglichen werden (keine Bereitschaften, Veranstaltungen und Vormerkungen).
    static func relevant(_ k: Karte) -> Bool {
        if k.istPruefung { return true }
        guard !k.istVeranstaltung else { return false }
        return ["regulaer", "geaendert", "offen", "entfall", "abwesend", "klausur", "buchung"].contains(k.status)
    }

    /// Tag und Stunde
    var platz: String { "\(datum)|\(von)" }

    /// Tag, Stunde und Unterricht (ohne Unterrichtsnummer: Fach und Klassen).
    var schluessel: String {
        platz + "|" + (unterricht.map { "u\($0)" } ?? "f\(fach)|\(klassen.joined(separator: ","))")
    }

    var entfaellt: Bool { status == "entfall" || status == "abwesend" }
}

/// Stand des eigenen Plans (aktuelle und nächste Woche).
struct Planstand: Codable {
    var besitzer: String
    /// Enthaltene Tage (yyyy-MM-dd) – verglichen werden nur Tage, die in beiden Ständen vorkommen
    var tage: [String]
    var stunden: [String: Stundeneintrag]

    init(besitzer: String, plaene: [Wochenplan]) {
        self.besitzer = besitzer
        tage = plaene.flatMap { $0.tage.filter { !$0.frei }.map(\.datum) }
        var stunden: [String: Stundeneintrag] = [:]
        for plan in plaene {
            for karte in plan.karten where Stundeneintrag.relevant(karte) {
                let eintrag = Stundeneintrag(karte)
                if stunden[eintrag.schluessel] == nil { stunden[eintrag.schluessel] = eintrag }
            }
        }
        self.stunden = stunden
    }

    static func laden() -> Planstand? {
        guard let daten = UserDefaults.standard.data(forKey: Planmitteilungen.standSchluessel) else { return nil }
        return try? JSONDecoder().decode(Planstand.self, from: daten)
    }

    func speichern() {
        guard let daten = try? JSONEncoder().encode(self) else { return }
        UserDefaults.standard.set(daten, forKey: Planmitteilungen.standSchluessel)
    }
}

// MARK: - Vergleich

/// Eine gemeldete Änderung.
struct Planaenderung: Equatable {
    var datum: String
    var von: Int
    var text: String
}

enum Planvergleich {
    /// Änderungen zwischen zwei Ständen – nur heute (noch nicht vorbei) und künftig, nur Tage beider Stände.
    static func aenderungen(alt: Planstand, neu: Planstand, jetzt: Date) -> [Planaenderung] {
        let heute = Datumsformat.iso(jetzt)
        let minute = Datumsformat.kalender.component(.hour, from: jetzt) * 60 + Datumsformat.kalender.component(.minute, from: jetzt)
        let gemeinsameTage = Set(alt.tage).intersection(neu.tage)
        let dieseWoche = Datumsformat.iso(Datumsformat.montag(jetzt))
        let naechsteWoche = Datumsformat.iso(Datumsformat.tage(7, nach: Datumsformat.montag(jetzt)))

        func vergleichbar(_ e: Stundeneintrag) -> Bool {
            guard gemeinsameTage.contains(e.datum), e.datum >= heute else { return false }
            if e.datum == heute, let ende = Rasterzeit.minuten(e.ende), ende <= minute { return false }
            return true
        }
        func zeile(_ e: Stundeneintrag, _ beschreibung: String) -> Planaenderung {
            var tag = Datumsformat.anzeige(e.datum, "EE").replacingOccurrences(of: ".", with: "")
            // Nächste Woche (und später) mit Datum, damit „Mo“ eindeutig bleibt
            if e.datum >= naechsteWoche || e.datum < dieseWoche {
                tag += " " + Datumsformat.anzeige(e.datum, "d.M.")
            }
            let stunde = e.bis > e.von ? "\(e.von).–\(e.bis). Std." : "\(e.von). Std."
            let wer = [e.fach, e.klassen.joined(separator: ", ")].filter { !$0.isEmpty }.joined(separator: " ")
            let kopf = wer.isEmpty ? "\(tag) \(stunde)" : "\(tag) \(stunde) \(wer)"
            return Planaenderung(datum: e.datum, von: e.von, text: "\(kopf): \(beschreibung)")
        }

        var ergebnis: [(sortierung: String, aenderung: Planaenderung)] = []
        func melden(_ e: Stundeneintrag, _ beschreibung: String?) {
            guard let beschreibung else { return }
            ergebnis.append((e.datum + "|" + String(format: "%03d", e.von), zeile(e, beschreibung)))
        }

        let altListe = alt.stunden.filter { vergleichbar($0.value) }
        let neuListe = neu.stunden.filter { vergleichbar($0.value) }

        // Gleiche Stunde in beiden Ständen
        for (schluessel, n) in neuListe {
            if let a = altListe[schluessel] { melden(a, beschreibung(alt: a, neu: n)) }
        }
        // Ohne Gegenstück: am selben Platz (Tag und Stunde) genau eine alte und eine neue → als Änderung vergleichen
        let nurAlt = altListe.filter { neuListe[$0.key] == nil }.map(\.value)
        let nurNeu = neuListe.filter { altListe[$0.key] == nil }.map(\.value)
        let altJePlatz = Dictionary(grouping: nurAlt, by: \.platz)
        let neuJePlatz = Dictionary(grouping: nurNeu, by: \.platz)
        for (platz, neue) in neuJePlatz {
            if neue.count == 1, let alte = altJePlatz[platz], alte.count == 1 {
                melden(alte[0], beschreibung(alt: alte[0], neu: neue[0]))
            } else {
                for n in neue { melden(n, beschreibungNeu(n)) }
            }
        }
        for (platz, alte) in altJePlatz {
            if alte.count == 1, let neue = neuJePlatz[platz], neue.count == 1 { continue }
            for a in alte where !a.entfaellt { melden(a, "nicht mehr im Plan") }
        }
        return ergebnis.sorted { $0.sortierung < $1.sortierung }.map(\.aenderung)
    }

    private static func liste(_ werte: [String]) -> String {
        werte.isEmpty ? "–" : werte.joined(separator: ", ")
    }

    /// Was hat sich an einer Stunde geändert? nil = nichts Meldenswertes.
    static func beschreibung(alt a: Stundeneintrag, neu n: Stundeneintrag) -> String? {
        if n.status == "entfall" && a.status != "entfall" { return "Entfall" }
        if n.status == "abwesend" && a.status != "abwesend" {
            return n.lehrer.isEmpty ? "wird vertreten (Vertretung offen)" : "Vertretung durch \(liste(n.lehrer))"
        }
        var teile: [String] = []
        if a.entfaellt && !n.entfaellt { teile.append("findet wieder statt") }
        if n.pruefung && !a.pruefung { teile.append("Prüfung") }
        if !n.fach.isEmpty && n.fach != a.fach { teile.append("Fach \(a.fach.isEmpty ? "–" : a.fach) → \(n.fach)") }
        if n.raeume != a.raeume && !n.raumbuchung { teile.append("Raum \(liste(a.raeume)) → \(liste(n.raeume))") }
        if n.klassen != a.klassen { teile.append("Klasse \(liste(a.klassen)) → \(liste(n.klassen))") }
        if n.lehrer != a.lehrer && !n.entfaellt { teile.append("Lehrkraft \(liste(a.lehrer)) → \(liste(n.lehrer))") }
        if teile.isEmpty && n.status != a.status && !n.raumbuchung && !a.raumbuchung {
            if n.status == "regulaer" {
                teile.append("wieder wie geplant")
            } else if n.status == "geaendert" || n.status == "offen" {
                teile.append("Vertretung")
            }
        }
        return teile.isEmpty ? nil : teile.joined(separator: ", ")
    }

    /// Neue Stunde im Plan (Vertretung, Prüfung, zusätzliche Stunde).
    static func beschreibungNeu(_ n: Stundeneintrag) -> String? {
        let raum = n.raeume.isEmpty ? "" : " in \(liste(n.raeume))"
        if n.pruefung { return "Prüfung" + raum }
        switch n.status {
        case "entfall": return "Entfall"
        case "abwesend": return n.lehrer.isEmpty ? "wird vertreten (Vertretung offen)" : "Vertretung durch \(liste(n.lehrer))"
        case "geaendert", "offen": return "Vertretung" + raum
        case "buchung": return nil
        default: return "neue Stunde" + raum
        }
    }
}

// MARK: - Antippen einer Mitteilung

extension AppDelegate: UNUserNotificationCenterDelegate {
    /// Auch bei geöffneter App als Banner zeigen.
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .list, .sound])
    }

    /// Antippen öffnet den Tag im Stundenplan.
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        if let datum = response.notification.request.content.userInfo[Planmitteilungen.datumSchluessel] as? String {
            DispatchQueue.main.async { Planmitteilungen.tagOeffnen(datum) }
        }
        completionHandler()
    }
}
