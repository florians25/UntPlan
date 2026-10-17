import Foundation
import Security

/// Schlüsselbund des Geräts – je Konto (Benutzer@Server) ein Eintrag: das Passwort (nur wenn „Passwort merken“
/// gewählt ist) und die Sitzungs-Cookies des Kontos (Wechsel zwischen mehreren Konten ohne Abmelden).
enum Schluesselbund {
    private static let dienst = "de.untplan.mobile"
    /// Sitzungs-Cookies je Konto
    private static let cookieDienst = "de.untplan.mobile.sitzung"

    static func speichern(_ passwort: String, konto: String) {
        datenSpeichern(Data(passwort.utf8), konto: konto, dienst: dienst)
    }

    static func lesen(konto: String) -> String? {
        guard let daten = datenLesen(konto: konto, dienst: dienst) else { return nil }
        return String(data: daten, encoding: .utf8)
    }

    static func loeschen(konto: String) {
        datenLoeschen(konto: konto, dienst: dienst)
    }

    // MARK: Sitzung je Konto

    static func sitzungSpeichern(_ daten: Data, konto: String) {
        datenSpeichern(daten, konto: konto, dienst: cookieDienst)
    }

    static func sitzungLesen(konto: String) -> Data? {
        datenLesen(konto: konto, dienst: cookieDienst)
    }

    static func sitzungLoeschen(konto: String) {
        datenLoeschen(konto: konto, dienst: cookieDienst)
    }

    /// Kontoname aus Benutzer und Server
    static func konto(login: String, server: String) -> String {
        "\(login)@\(server)"
    }

    // MARK: Allgemein

    private static func datenSpeichern(_ daten: Data, konto: String, dienst: String) {
        datenLoeschen(konto: konto, dienst: dienst)
        let eintrag: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: dienst,
            kSecAttrAccount as String: konto,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecValueData as String: daten
        ]
        _ = SecItemAdd(eintrag as CFDictionary, nil)
    }

    private static func datenLesen(konto: String, dienst: String) -> Data? {
        let abfrage: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: dienst,
            kSecAttrAccount as String: konto,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var ergebnis: AnyObject?
        let status = SecItemCopyMatching(abfrage as CFDictionary, &ergebnis)
        guard status == errSecSuccess, let daten = ergebnis as? Data else { return nil }
        return daten
    }

    private static func datenLoeschen(konto: String, dienst: String) {
        let abfrage: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: dienst,
            kSecAttrAccount as String: konto
        ]
        _ = SecItemDelete(abfrage as CFDictionary)
    }
}
