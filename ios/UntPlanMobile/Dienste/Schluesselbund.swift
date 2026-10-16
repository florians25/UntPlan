import Foundation
import Security

/// Passwort im Schlüsselbund des Geräts (nur wenn „Passwort merken“ gewählt ist).
enum Schluesselbund {
    private static let dienst = "de.untplan.mobile"

    static func speichern(_ passwort: String, konto: String) {
        loeschen(konto: konto)
        let eintrag: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: dienst,
            kSecAttrAccount as String: konto,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecValueData as String: Data(passwort.utf8)
        ]
        _ = SecItemAdd(eintrag as CFDictionary, nil)
    }

    static func lesen(konto: String) -> String? {
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
        return String(data: daten, encoding: .utf8)
    }

    static func loeschen(konto: String) {
        let abfrage: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: dienst,
            kSecAttrAccount as String: konto
        ]
        _ = SecItemDelete(abfrage as CFDictionary)
    }

    /// Kontoname aus Benutzer und Server
    static func konto(login: String, server: String) -> String {
        "\(login)@\(server)"
    }
}
