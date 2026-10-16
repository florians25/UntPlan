import Foundation
import SwiftUI

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

    let api = ApiClient.shared

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
    /// Fragt beim ersten Mal nach der Erlaubnis für Mitteilungen.
    func planPruefen() async {
        guard phase == .angemeldet, darfPlanmitteilungen, Planmitteilungen.aktiv else { return }
        await Planmitteilungen.erlaubnisAnfragen()
        _ = await Planpruefer.shared.pruefen()
        Planmitteilungen.planen()
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
        guard !api.serverAdresse.isEmpty else {
            phase = .abgemeldet
            return
        }
        do {
            let s = try await api.sitzung()
            if s.benutzer != nil {
                uebernehmen(s)
                return
            }
            let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
            let konto = Schluesselbund.konto(login: login, server: api.serverAdresse)
            if !login.isEmpty, let passwort = Schluesselbund.lesen(konto: konto) {
                try await api.anmelden(login: login, passwort: passwort)
                let neu = try await api.sitzung()
                if neu.benutzer != nil {
                    uebernehmen(neu)
                    return
                }
            }
        } catch {
            // Server nicht erreichbar oder Anmeldung abgelaufen: Anmeldemaske zeigen
        }
        phase = .abgemeldet
    }

    func anmelden(server: String, login: String, passwort: String, merken: Bool) async throws {
        let adresse = ApiClient.adresseNormalisieren(server)
        guard !adresse.isEmpty else { throw ApiFehler.keineServerAdresse }
        guard URL(string: adresse)?.host != nil else { throw ApiFehler.ungueltigeAdresse }
        let benutzername = login.trimmingCharacters(in: .whitespacesAndNewlines)
        api.serverAdresse = adresse
        try await api.anmelden(login: benutzername, passwort: passwort)
        let s = try await api.sitzung()
        guard s.benutzer != nil else { throw ApiFehler.nichtAngemeldet }

        UserDefaults.standard.set(benutzername, forKey: ApiClient.loginSchluessel)
        UserDefaults.standard.set(merken, forKey: ApiClient.merkenSchluessel)
        let konto = Schluesselbund.konto(login: benutzername, server: adresse)
        if merken {
            Schluesselbund.speichern(passwort, konto: konto)
        } else {
            Schluesselbund.loeschen(konto: konto)
        }
        uebernehmen(s)
    }

    func abmelden() async {
        try? await api.abmelden()
        api.cookiesLoeschen()
        let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
        Schluesselbund.loeschen(konto: Schluesselbund.konto(login: login, server: api.serverAdresse))
        sitzung = nil
        ungelesen = 0
        reiter = .stundenplan
        planAnfrage = nil
        schnellaktion = nil
        tagAnfrage = nil
        Planmitteilungen.zuruecksetzen()
        phase = .abgemeldet
    }

    /// Sitzung neu laden (Schulname, ungelesene Mitteilungen).
    func aktualisieren() async {
        do {
            let s = try await api.sitzung()
            if s.benutzer == nil {
                phase = .abgemeldet
            } else {
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
