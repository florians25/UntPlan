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
    /// Gewählter Reiter der Tab-Leiste.
    @Published var reiter: AppReiter = .start
    /// Plan, den der Stundenplan öffnen soll („Im Stundenplan anzeigen“ aus den Stundendetails).
    @Published var planAnfrage: PlanAuswahl? = nil
    /// Kurzbefehl vom App-Symbol, den der Stundenplan noch ausführen soll (Mein Stundenplan heute bzw. Planauswahl).
    @Published var schnellaktion: Schnellaktion? = nil

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

    /// Schüler*innen und Eltern sehen keine Pläne von Lehrkräften.
    var darfLehrerplaene: Bool { istKollegium }

    /// Plan im Reiter „Stundenplan“ öffnen.
    func planZeigen(_ plan: PlanAuswahl) {
        planAnfrage = plan
        reiter = .stundenplan
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
        reiter = .start
        planAnfrage = nil
        schnellaktion = nil
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

    private func uebernehmen(_ s: Sitzung) {
        sitzung = s
        ungelesen = s.ungelesen
        phase = .angemeldet
    }
}
