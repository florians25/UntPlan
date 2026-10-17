import Foundation

/// Ein angemeldetes Konto (Server und Benutzer). Mehrere Konten bleiben gleichzeitig angemeldet: Passwort (falls
/// gemerkt) und Sitzungs-Cookies liegen je Konto im Schlüsselbund, gewechselt wird ohne Abmelden.
struct Konto: Codable, Identifiable, Hashable, Sendable {
    /// „login@server“ – zugleich der Name im Schlüsselbund
    var id: String
    var server: String
    var login: String
    var anzeigename: String = ""
    var schulname: String = ""
    var rollenname: String = ""
    var merken: Bool = true
    var zuletzt: Date? = nil

    init(server: String, login: String, merken: Bool) {
        self.id = Schluesselbund.konto(login: login, server: server)
        self.server = server
        self.login = login
        self.merken = merken
    }

    /// Name für die Anzeige.
    var name: String { anzeigename.isEmpty ? login : anzeigename }

    /// Server ohne Schema, z. B. „plan.schule.de“.
    var serverKurz: String {
        URL(string: server)?.host ?? server.replacingOccurrences(of: "https://", with: "").replacingOccurrences(of: "http://", with: "")
    }

    /// Gleiches Konto (Server gleich, Benutzername ohne Rücksicht auf Groß-/Kleinschreibung)?
    func gleich(server s: String, login l: String) -> Bool {
        server.caseInsensitiveCompare(s) == .orderedSame && login.caseInsensitiveCompare(l) == .orderedSame
    }
}

/// Sitzungs-Cookie, wie es je Konto gesichert wird.
struct GesichertesCookie: Codable, Sendable {
    var name: String
    var wert: String
    var domain: String
    var pfad: String
    var ablauf: Date?
    var sicher: Bool

    init(_ c: HTTPCookie) {
        name = c.name
        wert = c.value
        domain = c.domain
        pfad = c.path
        ablauf = c.expiresDate
        sicher = c.isSecure
    }

    var cookie: HTTPCookie? {
        var eigenschaften: [HTTPCookiePropertyKey: Any] = [
            .name: name,
            .value: wert,
            .domain: domain,
            .path: pfad.isEmpty ? "/" : pfad,
        ]
        if let ablauf { eigenschaften[.expires] = ablauf }
        if sicher { eigenschaften[.secure] = "TRUE" }
        return HTTPCookie(properties: eigenschaften)
    }
}

/// Liste der Konten (UserDefaults) und Sitzungen je Konto (Schlüsselbund).
enum Kontenspeicher {
    static let listeSchluessel = "konten"
    static let aktivSchluessel = "aktivesKonto"

    static func laden() -> [Konto] {
        guard let daten = UserDefaults.standard.data(forKey: listeSchluessel),
              let liste = try? JSONDecoder().decode([Konto].self, from: daten) else { return [] }
        return liste
    }

    static func speichern(_ liste: [Konto]) {
        guard let daten = try? JSONEncoder().encode(liste) else { return }
        UserDefaults.standard.set(daten, forKey: listeSchluessel)
    }

    static var aktivId: String? {
        get { UserDefaults.standard.string(forKey: aktivSchluessel) }
        set { UserDefaults.standard.set(newValue, forKey: aktivSchluessel) }
    }

    /// Bisherige Einzelanmeldung (Server und Benutzer in den UserDefaults) als erstes Konto übernehmen.
    static func migrieren() {
        guard laden().isEmpty else { return }
        let server = UserDefaults.standard.string(forKey: ApiClient.adresseSchluessel) ?? ""
        let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
        guard !server.isEmpty, !login.isEmpty else { return }
        let merken = UserDefaults.standard.object(forKey: ApiClient.merkenSchluessel) as? Bool ?? true
        let konto = Konto(server: server, login: login, merken: merken)
        speichern([konto])
        aktivId = konto.id
    }

    /// Cookies des aktuellen Servers für dieses Konto sichern.
    static func sitzungSichern(fuer id: String) {
        let cookies = ApiClient.shared.cookies().map { GesichertesCookie($0) }
        guard !cookies.isEmpty, let daten = try? JSONEncoder().encode(cookies) else { return }
        Schluesselbund.sitzungSpeichern(daten, konto: id)
    }

    /// Gesicherte Cookies des Kontos wieder setzen (abgelaufene werden übergangen).
    static func sitzungWiederherstellen(fuer id: String) {
        guard let daten = Schluesselbund.sitzungLesen(konto: id),
              let liste = try? JSONDecoder().decode([GesichertesCookie].self, from: daten) else { return }
        let jetzt = Date()
        ApiClient.shared.cookiesSetzen(liste.filter { ($0.ablauf ?? .distantFuture) > jetzt }.compactMap(\.cookie))
    }

    static func sitzungVergessen(fuer id: String) {
        Schluesselbund.sitzungLoeschen(konto: id)
    }
}
