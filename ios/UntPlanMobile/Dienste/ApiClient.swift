import Foundation

/// Fehler beim Zugriff auf UntPlanWeb.
enum ApiFehler: LocalizedError {
    case keineServerAdresse
    case ungueltigeAdresse
    case nichtAngemeldet
    case nichtErreichbar(String)
    case server(String)
    case antwortUnlesbar

    var errorDescription: String? {
        switch self {
        case .keineServerAdresse: return "Bitte die Adresse des Servers eingeben."
        case .ungueltigeAdresse: return "Die Server-Adresse ist ungültig."
        case .nichtAngemeldet: return "Bitte erneut anmelden."
        case .nichtErreichbar(let grund): return "Der Server ist nicht erreichbar. \(grund)"
        case .server(let meldung): return meldung
        case .antwortUnlesbar: return "Die Antwort des Servers konnte nicht gelesen werden."
        }
    }
}

/// Zugriff auf die JSON-API von UntPlanWeb (Präfix /api/app). Die Sitzung läuft über das Anmelde-Cookie.
final class ApiClient: @unchecked Sendable {
    static let shared = ApiClient()

    /// Schlüssel in den UserDefaults
    static let adresseSchluessel = "serverAdresse"
    static let loginSchluessel = "benutzerLogin"
    static let merkenSchluessel = "passwortMerken"

    private let session: URLSession
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()

    init() {
        let konfig = URLSessionConfiguration.default
        konfig.httpCookieStorage = HTTPCookieStorage.shared
        konfig.httpCookieAcceptPolicy = .always
        konfig.httpShouldSetCookies = true
        konfig.requestCachePolicy = .reloadIgnoringLocalCacheData
        konfig.timeoutIntervalForRequest = 20
        session = URLSession(configuration: konfig)
    }

    /// Basis-Adresse von UntPlanWeb, z. B. https://plan.schule.de
    var serverAdresse: String {
        get { UserDefaults.standard.string(forKey: Self.adresseSchluessel) ?? "" }
        set { UserDefaults.standard.set(newValue, forKey: Self.adresseSchluessel) }
    }

    /// Ergänzt https:// und entfernt abschließende Schrägstriche.
    static func adresseNormalisieren(_ eingabe: String) -> String {
        var s = eingabe.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.isEmpty { return s }
        let klein = s.lowercased()
        if !klein.hasPrefix("http://") && !klein.hasPrefix("https://") { s = "https://" + s }
        while s.hasSuffix("/") { s.removeLast() }
        return s
    }

    // MARK: - Endpunkte

    func sitzung() async throws -> Sitzung {
        try await anfrage("GET", "/sitzung")
    }

    func anmelden(login: String, passwort: String) async throws {
        let _: OkAntwort = try await anfrage("POST", "/anmelden", inhalt: ["login": login, "passwort": passwort])
    }

    func abmelden() async throws {
        let _: OkAntwort = try await anfrage("POST", "/abmelden")
    }

    func heute() async throws -> Heute {
        try await anfrage("GET", "/heute")
    }

    /// Wochenplan eines Elements (typ: klasse, lehrer, raum) für die Woche des Datums.
    func plan(typ: String, element: String, datum: Date) async throws -> Wochenplan {
        try await anfrage("GET", "/plan", parameter: ["typ": typ, "element": element, "datum": Datumsformat.iso(datum)])
    }

    func elemente() async throws -> Elemente {
        try await anfrage("GET", "/elemente")
    }

    /// Ordner: nil = Posteingang, "gesendet", "entwuerfe"
    func mitteilungen(ordner: String? = nil) async throws -> [Mitteilung] {
        var parameter: [String: String] = [:]
        if let ordner { parameter["ordner"] = ordner }
        return try await anfrage("GET", "/mitteilungen", parameter: parameter)
    }

    func alsGelesenMarkieren(_ id: Int) async throws {
        let _: OkAntwort = try await anfrage("POST", "/mitteilungen/\(id)/gelesen")
    }

    func mitteilungLoeschen(_ id: Int) async throws {
        let _: OkAntwort = try await anfrage("DELETE", "/mitteilungen/\(id)")
    }

    func profil() async throws -> Profil {
        try await anfrage("GET", "/profil")
    }

    /// Entfernt die Cookies des Servers (nach dem Abmelden).
    func cookiesLoeschen() {
        guard let url = URL(string: serverAdresse), let speicher = session.configuration.httpCookieStorage else { return }
        for cookie in speicher.cookies(for: url) ?? [] {
            speicher.deleteCookie(cookie)
        }
    }

    // MARK: - Anfrage

    private func url(_ pfad: String, _ parameter: [String: String]) throws -> URL {
        let basis = serverAdresse
        guard !basis.isEmpty else { throw ApiFehler.keineServerAdresse }
        guard var teile = URLComponents(string: basis + "/api/app" + pfad) else { throw ApiFehler.ungueltigeAdresse }
        if !parameter.isEmpty {
            teile.queryItems = parameter.keys.sorted().map { URLQueryItem(name: $0, value: parameter[$0]) }
        }
        guard let url = teile.url else { throw ApiFehler.ungueltigeAdresse }
        return url
    }

    private func anfrage<T: Decodable>(_ methode: String, _ pfad: String, parameter: [String: String] = [:],
                                       inhalt: [String: String]? = nil) async throws -> T {
        var req = URLRequest(url: try url(pfad, parameter))
        req.httpMethod = methode
        // Schutzkopf von UntPlanWeb (ohne ihn lehnt der Server ändernde Aufrufe ab)
        req.setValue("1", forHTTPHeaderField: "X-UntPlanWeb")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let inhalt {
            req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            req.httpBody = try encoder.encode(inhalt)
        }

        let ergebnis: (Data, URLResponse)
        do {
            ergebnis = try await session.data(for: req)
        } catch let fehler as URLError where fehler.code == .cancelled {
            throw CancellationError()
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            throw ApiFehler.nichtErreichbar(error.localizedDescription)
        }
        let daten = ergebnis.0
        guard let http = ergebnis.1 as? HTTPURLResponse else { throw ApiFehler.antwortUnlesbar }

        if !(200..<300).contains(http.statusCode) {
            let meldung = (try? decoder.decode(Fehlerantwort.self, from: daten))?.fehler
            if http.statusCode == 401 && pfad != "/anmelden" { throw ApiFehler.nichtAngemeldet }
            throw ApiFehler.server(meldung ?? "Fehler \(http.statusCode) vom Server.")
        }
        do {
            return try decoder.decode(T.self, from: daten)
        } catch {
            throw ApiFehler.antwortUnlesbar
        }
    }
}
