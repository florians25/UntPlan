import Foundation
import SwiftUI
import UIKit

/// Sitzungszustand der App (angemeldet, Schule, Benutzer, ungelesene Mitteilungen).
@MainActor
final class AppZustand: ObservableObject {
    enum Phase {
        case startet
        case abgemeldet
        case angemeldet
    }

    @Published var phase: Phase = .startet
    @Published var sitzung: Sitzung?
    @Published var ungelesen = 0
    /// Gewählter Reiter der Tab-Leiste (die App startet im Stundenplan).
    @Published var reiter: AppReiter = .stundenplan
    /// Zählt hoch, wenn der bereits gewählte Reiter „Stundenplan“ erneut angetippt wird (zurück zur heutigen Woche).
    @Published var stundenplanHeute = 0
    /// Zählt hoch, wenn sich der Plan geändert hat (z. B. Raumbuchung in den Stundendetails) – der Stundenplan lädt neu.
    @Published var planGeaendert = 0
    /// Zuletzt gespeicherte Notiz einer Stunde – der Stundenplan passt die Karte sofort an (Info-Zeichen) und lädt neu.
    @Published var notizAenderung: NotizAenderung? = nil
    /// Plan, den der Stundenplan öffnen soll („Im Stundenplan anzeigen“ aus den Stundendetails).
    @Published var planAnfrage: PlanAuswahl? = nil
    /// Kurzbefehl vom App-Symbol, den der Stundenplan noch ausführen soll (Mein Stundenplan heute bzw. Planauswahl).
    @Published var schnellaktion: Schnellaktion? = nil
    /// Tag (yyyy-MM-dd), den der Stundenplan im eigenen Plan öffnen soll (angetippte Mitteilung über eine Planänderung).
    @Published var tagAnfrage: String? = nil
    /// Angemeldete Konten (mehrere Server bzw. Benutzer gleichzeitig)
    @Published var konten: [Konto] = []
    /// Zählt bei jedem Kontowechsel hoch – die Oberfläche wird für das neue Konto frisch aufgebaut
    @Published var kontoStand = 0

    /// Rechte für Entschuldigungen (Fachlehrkraft bzw. Klassenleitung); nil = noch nicht geladen.
    @Published var entschuldigungsrechte: Entschuldigungsrechte? = nil

    let api = ApiClient.shared

    /// Rechte für Entschuldigungen laden (nur Kollegium; ohne Endpunkt bzw. bei Fehlern: nichts erlaubt).
    @discardableResult
    func entschuldigungsrechteLaden(neu: Bool = false) async -> Entschuldigungsrechte {
        if !neu, let r = entschuldigungsrechte { return r }
        guard istKollegium else { return Entschuldigungsrechte() }
        let r = (try? await api.entschuldigungsrechte()) ?? Entschuldigungsrechte()
        entschuldigungsrechte = r
        return r
    }

    nonisolated init() {}

    var schulname: String {
        let name = sitzung?.schule?.name ?? ""
        return name.isEmpty ? "UntPlan" : name
    }

    var benutzer: SitzungsBenutzer? { sitzung?.benutzer }

    /// Kollegium (Lehrkräfte, Schulleitung, Administration).
    var istKollegium: Bool {
        guard let b = benutzer else { return false }
        return b.kollegium && !["schueler", "eltern"].contains(b.rolle.lowercased())
    }

    /// Schüler*innen und Erziehungsberechtigte („Meine Abwesenheiten“).
    var istFamilie: Bool {
        guard let b = benutzer else { return false }
        return b.familie || ["schueler", "eltern"].contains(b.rolle.lowercased())
    }

    /// Reiter wählen; erneutes Antippen des Stundenplans führt zur heutigen Woche zurück.
    func reiterWaehlen(_ neu: AppReiter) {
        if neu == reiter && neu == .stundenplan {
            stundenplanHeute += 1
        }
        if neu != reiter { reiter = neu }
    }

    /// Schüler*innen und Eltern sehen keine Pläne von Lehrkräften.
    var darfLehrerplaene: Bool { istKollegium }

    /// Mitteilungen bei Änderungen im eigenen Plan: nur Lehrkräfte mit eigenem Lehrerplan.
    var darfPlanmitteilungen: Bool { istKollegium && Planmitteilungen.berechtigt(benutzer) }

    /// Eigenen Plan auf Änderungen prüfen (beim Öffnen bzw. im Vordergrund) und die Hintergrund-Prüfung anmelden.
    /// Fragt beim ersten Mal nach der Erlaubnis für Mitteilungen und meldet das Gerät für Push-Mitteilungen an
    /// (APNs; wirkt erst mit Push-Berechtigung in Xcode und dem Server-Endpunkt, sonst bleibt es bei der Prüfung).
    func planPruefen() async {
        guard phase == .angemeldet, darfPlanmitteilungen, Planmitteilungen.aktiv else { return }
        await Planmitteilungen.erlaubnisAnfragen()
        UIApplication.shared.registerForRemoteNotifications()
        _ = await Planpruefer.shared.pruefen()
        Planmitteilungen.planen()
    }

    // MARK: - Konten

    /// Aktives Konto.
    var aktivesKonto: Konto? {
        guard let id = Kontenspeicher.aktivId else { return nil }
        return konten.first { $0.id == id }
    }

    /// Konto übernehmen bzw. aktualisieren (Anzeigename, Schule, Rolle) und speichern.
    private func kontoMerken(server: String, login: String, merken: Bool, sitzung s: Sitzung?) -> Konto {
        var konto = konten.first { $0.gleich(server: server, login: login) } ?? Konto(server: server, login: login, merken: merken)
        konto.merken = merken
        konto.zuletzt = Date()
        if let s {
            if let b = s.benutzer {
                konto.anzeigename = b.anzeigename
                konto.rollenname = b.rollenname
            }
            if let name = s.schule?.name, !name.isEmpty { konto.schulname = name }
        }
        if let i = konten.firstIndex(where: { $0.id == konto.id }) {
            konten[i] = konto
        } else {
            konten.append(konto)
        }
        Kontenspeicher.speichern(konten)
        return konto
    }

    /// Angaben des aktiven Kontos nach dem Laden der Sitzung auffrischen.
    private func aktivesKontoAuffrischen(_ s: Sitzung) {
        guard let k = aktivesKonto else { return }
        guard k.anzeigename != (s.benutzer?.anzeigename ?? k.anzeigename)
                || k.rollenname != (s.benutzer?.rollenname ?? k.rollenname)
                || (!(s.schule?.name ?? "").isEmpty && k.schulname != s.schule?.name) else { return }
        _ = kontoMerken(server: k.server, login: k.login, merken: k.merken, sitzung: s)
    }

    /// Oberfläche für ein anderes Konto zurücksetzen (Reiter, Anfragen, zwischengespeicherte Kacheln).
    private func oberflaecheZuruecksetzen() {
        ungelesen = 0
        reiter = .stundenplan
        planAnfrage = nil
        schnellaktion = nil
        tagAnfrage = nil
        notizAenderung = nil
        Kachelspeicher.leeren()
    }

    /// In ein anderes angemeldetes Konto wechseln, ohne abzumelden: Sitzung des bisherigen Kontos sichern, die des
    /// neuen wiederherstellen; ist sie abgelaufen, mit gemerktem Passwort neu anmelden, sonst die Anmeldemaske zeigen.
    @discardableResult
    func wechseln(zu konto: Konto) async -> Bool {
        if let id = Kontenspeicher.aktivId, id == konto.id, phase == .angemeldet { return true }
        if let id = Kontenspeicher.aktivId, phase == .angemeldet { Kontenspeicher.sitzungSichern(fuer: id) }
        api.cookiesLoeschen()
        api.serverAdresse = konto.server
        api.cookiesLoeschen()
        UserDefaults.standard.set(konto.login, forKey: ApiClient.loginSchluessel)
        UserDefaults.standard.set(konto.merken, forKey: ApiClient.merkenSchluessel)
        Kontenspeicher.sitzungWiederherstellen(fuer: konto.id)
        Kontenspeicher.aktivId = konto.id
        sitzung = nil
        oberflaecheZuruecksetzen()
        // Während des Wechsels „Verbindung wird hergestellt …“ statt der Daten des vorigen Kontos
        phase = .startet
        do {
            var s = try await api.sitzung()
            if s.benutzer == nil, let passwort = Schluesselbund.lesen(konto: konto.id) {
                try await api.anmelden(login: konto.login, passwort: passwort)
                s = try await api.sitzung()
            }
            if s.benutzer != nil {
                Kontenspeicher.sitzungSichern(fuer: konto.id)
                _ = kontoMerken(server: konto.server, login: konto.login, merken: konto.merken, sitzung: s)
                kontoStand += 1
                entschuldigungsrechte = nil
                uebernehmen(s)
                pushTokenMelden()
                return true
            }
        } catch {
            // Server nicht erreichbar oder Sitzung abgelaufen: Anmeldemaske (mit Server und Benutzer des Kontos)
        }
        phase = .abgemeldet
        return false
    }

    /// Konto vom Gerät entfernen (Passwort und Sitzung löschen). Das aktive Konto wird abgemeldet; gibt es weitere
    /// Konten, wechselt die App in das nächste.
    func entfernen(_ konto: Konto) async {
        if konto.id == Kontenspeicher.aktivId {
            await abmelden()
            return
        }
        Schluesselbund.loeschen(konto: konto.id)
        Kontenspeicher.sitzungVergessen(fuer: konto.id)
        konten.removeAll { $0.id == konto.id }
        Kontenspeicher.speichern(konten)
    }

    /// Geräte-Token für Push-Mitteilungen (falls vorhanden) beim Server des aktiven Kontos hinterlegen.
    func pushTokenMelden() {
        guard phase == .angemeldet else { return }
        Push.tokenMelden()
    }

    /// Plan im Reiter „Stundenplan“ öffnen.
    func planZeigen(_ plan: PlanAuswahl) {
        planAnfrage = plan
        reiter = .stundenplan
    }

    /// Notiz einer Stunde gespeichert (nil = unverändert): Karte im Plan sofort anpassen und den Plan neu laden.
    func notizGespeichert(_ karte: Karte, schueler: String?, lehrer: String?) {
        guard let u = karte.unterricht else { return }
        notizAenderung = NotizAenderung(datum: karte.datum, unterricht: u, von: karte.von, bis: karte.bis,
                                        schueler: schueler, lehrer: lehrer)
    }

    /// Beim Start: bestehende Sitzung prüfen, sonst mit gemerktem Passwort neu anmelden.
    func starten() async {
        Kontenspeicher.migrieren()
        konten = Kontenspeicher.laden()
        if let aktiv = aktivesKonto {
            // Sitzung des aktiven Kontos wiederherstellen (falls das System die Sitzungs-Cookies verworfen hat)
            if api.serverAdresse != aktiv.server { api.serverAdresse = aktiv.server }
            if api.cookies().isEmpty { Kontenspeicher.sitzungWiederherstellen(fuer: aktiv.id) }
        }
        guard !api.serverAdresse.isEmpty else {
            phase = .abgemeldet
            return
        }
        do {
            let s = try await api.sitzung()
            if s.benutzer != nil {
                aktivesKontoAuffrischen(s)
                uebernehmen(s)
                return
            }
            let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
            let konto = Schluesselbund.konto(login: login, server: api.serverAdresse)
            if !login.isEmpty, let passwort = Schluesselbund.lesen(konto: konto) {
                try await api.anmelden(login: login, passwort: passwort)
                let neu = try await api.sitzung()
                if neu.benutzer != nil {
                    Kontenspeicher.sitzungSichern(fuer: konto)
                    aktivesKontoAuffrischen(neu)
                    uebernehmen(neu)
                    return
                }
            }
        } catch {
            // Server nicht erreichbar oder Anmeldung abgelaufen: Anmeldemaske zeigen
        }
        phase = .abgemeldet
    }

    /// Anmelden – erstes Konto oder ein weiteres („Konto hinzufügen“): die Sitzung des bisherigen Kontos bleibt
    /// gesichert und kann ohne erneute Anmeldung wieder gewählt werden. Schlägt die Anmeldung fehl, bleibt das
    /// bisherige Konto aktiv.
    func anmelden(server: String, login: String, passwort: String, merken: Bool) async throws {
        let adresse = ApiClient.adresseNormalisieren(server)
        guard !adresse.isEmpty else { throw ApiFehler.keineServerAdresse }
        guard URL(string: adresse)?.host != nil else { throw ApiFehler.ungueltigeAdresse }
        let benutzername = login.trimmingCharacters(in: .whitespacesAndNewlines)
        let vorherAdresse = api.serverAdresse
        let vorherId = phase == .angemeldet ? Kontenspeicher.aktivId : nil
        if let vorherId { Kontenspeicher.sitzungSichern(fuer: vorherId) }
        api.cookiesLoeschen()
        api.serverAdresse = adresse
        api.cookiesLoeschen()
        let s: Sitzung
        do {
            try await api.anmelden(login: benutzername, passwort: passwort)
            s = try await api.sitzung()
            guard s.benutzer != nil else { throw ApiFehler.nichtAngemeldet }
        } catch {
            // Bisheriges Konto wiederherstellen
            api.cookiesLoeschen()
            api.serverAdresse = vorherAdresse
            if let vorherId { Kontenspeicher.sitzungWiederherstellen(fuer: vorherId) }
            throw error
        }

        UserDefaults.standard.set(benutzername, forKey: ApiClient.loginSchluessel)
        UserDefaults.standard.set(merken, forKey: ApiClient.merkenSchluessel)
        let konto = kontoMerken(server: adresse, login: benutzername, merken: merken, sitzung: s)
        if merken {
            Schluesselbund.speichern(passwort, konto: konto.id)
        } else {
            Schluesselbund.loeschen(konto: konto.id)
        }
        Kontenspeicher.sitzungSichern(fuer: konto.id)
        let neuesKonto = Kontenspeicher.aktivId != konto.id || phase != .angemeldet
        Kontenspeicher.aktivId = konto.id
        if neuesKonto {
            oberflaecheZuruecksetzen()
            kontoStand += 1
            entschuldigungsrechte = nil
        }
        uebernehmen(s)
        pushTokenMelden()
    }

    /// Aktives Konto abmelden und vom Gerät entfernen; gibt es weitere Konten, in das nächste wechseln.
    func abmelden() async {
        if let token = Push.token { try? await api.pushGeraetAbmelden(token: token) }
        try? await api.abmelden()
        api.cookiesLoeschen()
        let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
        let id = Kontenspeicher.aktivId ?? Schluesselbund.konto(login: login, server: api.serverAdresse)
        Schluesselbund.loeschen(konto: id)
        Kontenspeicher.sitzungVergessen(fuer: id)
        konten.removeAll { $0.id == id }
        Kontenspeicher.speichern(konten)
        Kontenspeicher.aktivId = nil
        sitzung = nil
        oberflaecheZuruecksetzen()
        Planmitteilungen.zuruecksetzen()
        if let naechstes = konten.sorted(by: { ($0.zuletzt ?? .distantPast) > ($1.zuletzt ?? .distantPast) }).first {
            phase = .startet
            await wechseln(zu: naechstes)
        } else {
            phase = .abgemeldet
        }
    }

    /// Sitzung neu laden (Schulname, ungelesene Mitteilungen).
    func aktualisieren() async {
        do {
            let s = try await api.sitzung()
            if s.benutzer == nil {
                phase = .abgemeldet
            } else {
                aktivesKontoAuffrischen(s)
                uebernehmen(s)
            }
        } catch {
            _ = fehlertext(error)
        }
    }

    /// Fehlertext für die Anzeige; bei abgelaufener Sitzung zurück zur Anmeldung (dann nil).
    func fehlertext(_ fehler: Error) -> String? {
        if let f = fehler as? ApiFehler, case .nichtAngemeldet = f {
            sitzung = nil
            phase = .abgemeldet
            return nil
        }
        if fehler is CancellationError { return nil }
        if let url = fehler as? URLError, url.code == .cancelled { return nil }
        return fehler.localizedDescription
    }

    /// Nur geänderte Werte veröffentlichen: jede Zuweisung an @Published lässt alle beobachtenden Ansichten neu rechnen.
    private func uebernehmen(_ s: Sitzung) {
        sitzung = s
        if ungelesen != s.ungelesen { ungelesen = s.ungelesen }
        if phase != .angemeldet { phase = .angemeldet }
    }
}

/// Gespeicherte Notiz einer Stunde (für die sofortige Anzeige des Info-Zeichens im Plan).
struct NotizAenderung: Equatable {
    let id = UUID()
    let datum: String
    let unterricht: Int
    let von: Int
    let bis: Int
    /// Neue Notiz für Schüler*innen bzw. Lehrkräfte (nil = unverändert)
    let schueler: String?
    let lehrer: String?
}
