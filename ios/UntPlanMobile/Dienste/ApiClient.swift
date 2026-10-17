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
        // Einzelne unlesbare Einträge überspringen statt die ganze Liste zu verwerfen
        let liste: [Vielleicht<Mitteilung>] = try await anfrage("GET", "/mitteilungen", parameter: parameter)
        return liste.compactMap(\.wert)
    }

    /// Mögliche Empfänger einer neuen Mitteilung (Personen, Gruppen, Klassen, Listen).
    func empfaenger() async throws -> [Empfaenger] {
        let liste: [Vielleicht<Empfaenger>] = try await anfrage("GET", "/mitteilungen/empfaenger")
        return liste.compactMap(\.wert)
    }

    /// POST /mitteilungen – neue Mitteilung senden (Antwort: neue Kennung).
    @discardableResult
    func mitteilungSenden(_ daten: MitteilungNeuDaten) async throws -> Int? {
        let antwort: IdAntwort = try await anfrage("POST", "/mitteilungen", koerper: try encoder.encode(daten))
        return antwort.id
    }

    /// POST /mitteilungen/entwurf – Entwurf anlegen bzw. ändern (für Anhänge vor dem Senden); Antwort: Kennung.
    func entwurfSpeichern(_ daten: MitteilungNeuDaten) async throws -> Int {
        let antwort: IdAntwort = try await anfrage("POST", "/mitteilungen/entwurf", koerper: try encoder.encode(daten))
        guard let id = antwort.id else { throw ApiFehler.antwortUnlesbar }
        return id
    }

    /// POST /mitteilungen/{id}/senden – Entwurf (mit Anhängen) senden.
    func entwurfSenden(_ id: Int) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/mitteilungen/\(id)/senden")
    }

    /// POST /mitteilungen/{id}/anhaenge – Datei als multipart/form-data an einen eigenen Entwurf hängen.
    func anhangHochladen(mitteilung id: Int, name: String, typ: String, daten: Data) async throws {
        let grenze = "UntPlan-\(UUID().uuidString)"
        var koerper = Data()
        let sicher = name.replacingOccurrences(of: "\"", with: "")
        koerper.append(Data("--\(grenze)\r\n".utf8))
        koerper.append(Data("Content-Disposition: form-data; name=\"datei\"; filename=\"\(sicher)\"\r\n".utf8))
        koerper.append(Data("Content-Type: \(typ.isEmpty ? "application/octet-stream" : typ)\r\n\r\n".utf8))
        koerper.append(daten)
        koerper.append(Data("\r\n--\(grenze)--\r\n".utf8))
        var req = URLRequest(url: try url("/mitteilungen/\(id)/anhaenge", [:]))
        req.httpMethod = "POST"
        req.timeoutInterval = 120
        req.setValue("multipart/form-data; boundary=\(grenze)", forHTTPHeaderField: "Content-Type")
        req.httpBody = koerper
        _ = try await rohAnfrage(req, pfad: "/mitteilungen/\(id)/anhaenge")
    }

    /// GET /mitteilungen/anhang/{id} – Inhalt eines Anhangs (nur Absender und Empfänger*innen).
    func anhangLaden(_ id: Int) async throws -> Data {
        var req = URLRequest(url: try url("/mitteilungen/anhang/\(id)", [:]))
        req.httpMethod = "GET"
        req.timeoutInterval = 120
        return try await rohAnfrage(req, pfad: "/mitteilungen/anhang/\(id)")
    }

    /// GET /mitteilungen/personen?typ=kollegium|individuell – Personen mit Rollen und Filtern für die Empfängerauswahl.
    /// Ältere Server ohne diesen Endpunkt: die bisherige Empfängerliste (Kolleg*innen = Lehrkräfte) und die eigenen Listen.
    func personen(typ: String) async throws -> Personenauswahl {
        do {
            return try await anfrage("GET", "/mitteilungen/personen", parameter: ["typ": typ])
        } catch ApiFehler.nichtAngemeldet {
            throw ApiFehler.nichtAngemeldet
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            let alle = try await empfaenger()
            let listen = (try? await mitteilungslisten()) ?? []
            let personen = alle.filter { typ == "kollegium" ? $0.kategorie == "lehrkraefte" || ($0.istPerson && $0.kategorie == "gruppen")
                                                           : $0.istPerson }
            let gruppen = typ == "kollegium" ? [] : alle.filter { !$0.istPerson }
            let klassen = Array(Set(alle.compactMap(\.klasse))).sorted { $0.localizedStandardCompare($1) == .orderedAscending }
            return Personenauswahl(personen: personen, gruppen: gruppen,
                                   filter: .init(listen: listen.map { l in
                                       var neu = l
                                       if !neu.id.hasPrefix("liste:") { neu.id = "liste:" + neu.id }
                                       return neu
                                   }, klassen: klassen))
        }
    }

    /// GET /mitteilungen/{id} – eine Mitteilung (markiert nicht als gelesen).
    func mitteilung(_ id: Int) async throws -> Mitteilung {
        try await anfrage("GET", "/mitteilungen/\(id)")
    }

    /// GET /mitteilungen/{id}/antwort – Vorschlag (Empfänger, „AW: …“, zitierter Text).
    func antwortvorlage(_ id: Int, alle: Bool = false) async throws -> Antwortvorlage {
        try await anfrage("GET", "/mitteilungen/\(id)/antwort", parameter: alle ? ["alle": "true"] : [:])
    }

    /// POST /mitteilungen/{id}/bestaetigen – Lesebestätigung senden.
    func lesebestaetigungSenden(_ id: Int) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/mitteilungen/\(id)/bestaetigen")
    }

    /// GET /mitteilungen/listen – eigene Verteilerlisten (Filter „Liste“ der Empfängerauswahl).
    func mitteilungslisten() async throws -> [Mitteilungsliste] {
        let liste: [Vielleicht<Mitteilungsliste>] = try await anfrage("GET", "/mitteilungen/listen")
        return liste.compactMap(\.wert)
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

    /// POST /passwort – bisheriges und neues Passwort.
    func passwortAendern(alt: String, neu: String) async throws {
        let _: OkAntwort = try await anfrage("POST", "/passwort", inhalt: ["alt": alt, "neu": neu])
    }

    /// Eigene Meldungen (Abwesenheiten, Raumbuchungen …).
    func meldungen() async throws -> [Meldung] {
        let liste: [Vielleicht<Meldung>] = try await anfrage("GET", "/meldungen")
        return liste.compactMap(\.wert)
    }

    // MARK: Klassenbuch einer Stunde

    /// `einzelstunde`: nur diese Stunde eines Blocks (nil = ganzer Block).
    func klassenbuchStunde(datum: String, stunde: Int, unterricht: Int, lehrer: String?, einzelstunde: Int? = nil) async throws -> KlassenbuchStunde {
        var parameter = ["datum": datum, "stunde": String(stunde), "unterricht": String(unterricht)]
        if let lehrer, !lehrer.isEmpty { parameter["lehrer"] = lehrer }
        if let einzelstunde { parameter["einzelstunde"] = String(einzelstunde) }
        return try await anfrage("GET", "/klassenbuch/stunde", parameter: parameter)
    }

    func hausaufgabenDerStunde(datum: String, stunde: Int, bis: Int, unterricht: Int, fach: String, klassen: [String],
                               einzelstunde: Int? = nil) async throws -> HausaufgabenDerStunde {
        var parameter = ["datum": datum, "stunde": String(stunde), "bis": String(bis), "unterricht": String(unterricht)]
        if let einzelstunde { parameter["einzelstunde"] = String(einzelstunde) }
        if !fach.isEmpty { parameter["fach"] = fach }
        if !klassen.isEmpty { parameter["klassen"] = klassen.joined(separator: ",") }
        return try await anfrage("GET", "/klassenbuch/hausaufgabenliste", parameter: parameter)
    }

    func anwesenheit(datum: String, stunde: Int, unterricht: Int, einzelstunde: Int? = nil) async throws -> Anwesenheitsliste {
        var parameter = ["datum": datum, "stunde": String(stunde), "unterricht": String(unterricht)]
        if let einzelstunde { parameter["einzelstunde"] = String(einzelstunde) }
        return try await anfrage("GET", "/klassenbuch/anwesenheit", parameter: parameter)
    }

    /// PUT /klassenbuch/anwesenheit – ersetzt die Fehlzeiten der Stunde und markiert sie als kontrolliert.
    func anwesenheitSpeichern(_ daten: AnwesenheitDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("PUT", "/klassenbuch/anwesenheit", koerper: try encoder.encode(daten))
    }

    func klassenbuchSpeichern(_ daten: KlassenbuchDaten) async throws -> Klassenbucheintrag {
        try await anfrage("PUT", "/klassenbuch/stunde", koerper: try encoder.encode(daten))
    }

    func hausaufgabeAnlegen(_ daten: HausaufgabeNeuDaten) async throws -> Hausaufgabeneintrag {
        try await anfrage("POST", "/klassenbuch/hausaufgabenliste", koerper: try encoder.encode(daten))
    }

    func hausaufgabeAendern(_ id: Int, _ daten: HausaufgabeAenderDaten) async throws -> Hausaufgabeneintrag {
        try await anfrage("PUT", "/klassenbuch/hausaufgabenliste/\(id)", koerper: try encoder.encode(daten))
    }

    func hausaufgabeLoeschen(_ id: Int) async throws {
        let _: OkAntwort = try await anfrage("DELETE", "/klassenbuch/hausaufgabenliste/\(id)")
    }

    func vermerke(klasse: String, von: String, bis: String) async throws -> Vermerkliste {
        try await anfrage("GET", "/klassenbuch/vermerke", parameter: ["klasse": klasse, "von": von, "bis": bis])
    }

    /// POST /klassenbuch/vermerke – neuer Klassenbucheintrag.
    func vermerkAnlegen(_ daten: VermerkNeuDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/klassenbuch/vermerke", koerper: try encoder.encode(daten))
    }

    // MARK: Abwesenheiten (Kollegium)

    /// POST /klassenbuch/abwesenheit – Abwesenheit minutengenau erfassen bzw. ersetzen (Abwesenheitsdialog, Wischgeste).
    func abwesenheitErfassen(_ daten: AbwesenheitZeitraumDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/klassenbuch/abwesenheit", koerper: try encoder.encode(daten))
    }

    /// POST /klassenbuch/abwesenheit/loeschen – Abwesenheit in dieser Stunde entfernen (anwesend).
    func abwesenheitEntfernen(_ daten: AbwesenheitEntfernenDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/klassenbuch/abwesenheit/loeschen", koerper: try encoder.encode(daten))
    }

    /// POST /abwesenheiten/loeschen – ganze Abwesenheiten (Fehlzeit-IDs) löschen (Klassenleitung).
    func abwesenheitenLoeschen(_ ids: [Int]) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/abwesenheiten/loeschen", koerper: try encoder.encode(IdsDaten(ids: ids)))
    }

    /// GET /abwesenheitsgruende?art=abwesenheit
    func abwesenheitsgruende() async throws -> [Abwesenheitsgrund] {
        let liste: [Vielleicht<Abwesenheitsgrund>] = try await anfrage("GET", "/abwesenheitsgruende", parameter: ["art": "abwesenheit"])
        return liste.compactMap(\.wert).filter(\.aktiv)
    }

    /// GET /abwesenheiten/zeitraeume – Abwesenheiten einer Klasse als Zeiträume (nur Klassenleitung).
    func abwesenheitszeitraeume(klasse: String, von: String, bis: String) async throws -> [Abwesenheitszeitraum] {
        let liste: [Vielleicht<Abwesenheitszeitraum>] = try await anfrage("GET", "/abwesenheiten/zeitraeume",
                                                                            parameter: ["klasse": klasse, "von": von, "bis": bis])
        return liste.compactMap(\.wert)
    }

    // MARK: Entschuldigungen (Fachlehrkraft und Klassenleitung)

    /// GET /entschuldigungen/einstellungen – Rechte des Kontos (ältere Server ohne Endpunkt: nichts erlaubt).
    func entschuldigungsrechte() async throws -> Entschuldigungsrechte {
        try await anfrage("GET", "/entschuldigungen/einstellungen")
    }

    /// GET /entschuldigungen/fehlzeiten – Fehlstunden aus dem eigenen Unterricht (Fachlehrer-Sicht).
    func fehlstunden(von: String?, bis: String?, klasse: String?, status: String? = nil, nurOffene: Bool = false) async throws -> Fehlstundenliste {
        var p: [String: String] = [:]
        if let von, !von.isEmpty { p["von"] = von }
        if let bis, !bis.isEmpty { p["bis"] = bis }
        if let klasse, !klasse.isEmpty { p["klasse"] = klasse }
        if let status, !status.isEmpty { p["status"] = status }
        if nurOffene { p["nurOffene"] = "true" }
        return try await anfrage("GET", "/entschuldigungen/fehlzeiten", parameter: p)
    }

    /// POST /entschuldigungen/fehlzeiten – Fachlehrkraft entschuldigt eine oder mehrere Stunden.
    func fehlstundenEntschuldigen(_ daten: FehlstundenEntschuldigenDaten) async throws -> EntschuldigungsAntwort {
        try await anfrage("POST", "/entschuldigungen/fehlzeiten", koerper: try encoder.encode(daten))
    }

    /// GET /entschuldigungen/abwesenheiten – Abwesenheiten der eigenen Klassen (Klassenleitungs-Sicht).
    func entschuldigungsAbwesenheiten(von: String?, bis: String?, klasse: String?, status: String? = nil) async throws -> [EntschuldigungsAbwesenheit] {
        var p: [String: String] = [:]
        if let von, !von.isEmpty { p["von"] = von }
        if let bis, !bis.isEmpty { p["bis"] = bis }
        if let klasse, !klasse.isEmpty { p["klasse"] = klasse }
        if let status, !status.isEmpty { p["status"] = status }
        let liste: [Vielleicht<EntschuldigungsAbwesenheit>] = try await anfrage("GET", "/entschuldigungen/abwesenheiten", parameter: p)
        return liste.compactMap(\.wert)
    }

    /// GET /entschuldigungen/abwesenheit?ids=… – eine Abwesenheit mit ihren Stunden (Detail).
    func entschuldigungsAbwesenheit(ids: [Int]) async throws -> EntschuldigungsAbwesenheit {
        try await anfrage("GET", "/entschuldigungen/abwesenheit", parameter: ["ids": ids.map(String.init).joined(separator: ",")])
    }

    /// POST /entschuldigungen/abwesenheit – Klassenleitung entschuldigt bzw. ändert ganze Abwesenheiten.
    func abwesenheitEntschuldigen(_ daten: AbwesenheitEntschuldigenDaten) async throws -> EntschuldigungsAntwort {
        try await anfrage("POST", "/entschuldigungen/abwesenheit", koerper: try encoder.encode(daten))
    }

    /// POST /entschuldigungen/zuruecknehmen – Klassenleitung nimmt Entschuldigungen zurück (alle Stunden wieder offen).
    func entschuldigungZuruecknehmen(ids: [Int]) async throws -> EntschuldigungsAntwort {
        try await anfrage("POST", "/entschuldigungen/zuruecknehmen", koerper: try encoder.encode(IdsDaten(ids: ids)))
    }

    // MARK: Meine Abwesenheiten (Schüler*innen und Eltern)

    func familieKinder() async throws -> [Kind] {
        let liste: [Vielleicht<Kind>] = try await anfrage("GET", "/familie/kinder")
        return liste.compactMap(\.wert)
    }

    /// Bezeichnungen der Abwesenheitsgründe (Kürzel → Name).
    func familieGruende() async throws -> [String: String] {
        try await anfrage("GET", "/familie/gruende")
    }

    func familieAbwesenheiten(schueler: Int?, von: String?, bis: String?, status: String?) async throws -> FamilieAbwesenheiten {
        var parameter: [String: String] = [:]
        if let schueler { parameter["schueler"] = String(schueler) }
        if let von, !von.isEmpty { parameter["von"] = von }
        if let bis, !bis.isEmpty { parameter["bis"] = bis }
        if let status, !status.isEmpty { parameter["status"] = status }
        return try await anfrage("GET", "/familie/abwesenheiten", parameter: parameter)
    }

    // MARK: Raumbuchung aus den Stundendetails

    /// GET /meldungen/freieraeume – freie buchbare Räume der Stunde.
    func freieRaeume(datum: String, von: Int, bis: Int, unterricht: Int?) async throws -> [FreierRaum] {
        var parameter = ["datum": datum, "von": String(von), "bis": String(bis)]
        if let unterricht { parameter["unterricht"] = String(unterricht) }
        let liste: [Vielleicht<FreierRaum>] = try await anfrage("GET", "/meldungen/freieraeume", parameter: parameter)
        return liste.compactMap(\.wert).filter { !$0.kurz.isEmpty }
    }

    /// POST /mobil/raum – Raum ersetzen, hinzufügen oder löschen; liefert die neue Raumliste.
    func raumAendern(_ daten: RaumAenderungDaten) async throws -> RaumAenderungAntwort {
        try await anfrage("POST", "/mobil/raum", koerper: try encoder.encode(daten))
    }

    /// Klassendienste einer Klasse im Zeitraum (z. B. Klassensprecher*in), je Schüler*in.
    func klassendienste(klasse: String, von: String, bis: String) async throws -> Klassendienstliste {
        try await anfrage("GET", "/klassendienste", parameter: ["klasse": klasse, "von": von, "bis": bis])
    }

    // MARK: Sprechtag

    /// GET /sprechtag/start – Sprechtage für die Startseite (Lehrkräfte bzw. Erziehungsberechtigte).
    func sprechtagStart() async throws -> [SprechtagKarte] {
        let liste: [Vielleicht<SprechtagKarte>] = try await anfrage("GET", "/sprechtag/start")
        return liste.compactMap(\.wert).filter { $0.id > 0 }
    }

    /// GET /sprechtag/{id}/lehrkraft – eigenes Zeitraster mit Terminen.
    func sprechtagLehrkraft(_ id: Int) async throws -> SprechtagLehrkraftDaten {
        try await anfrage("GET", "/sprechtag/\(id)/lehrkraft")
    }

    /// GET /sprechtag/{id}/eltern – Kinder, Lehrkräfte, freie Zeitfenster und eigene Termine.
    func sprechtagEltern(_ id: Int) async throws -> SprechtagElternDaten {
        try await anfrage("GET", "/sprechtag/\(id)/eltern")
    }

    /// POST /sprechtag/{id}/buchen
    func sprechtagBuchen(_ id: Int, _ daten: SprechtagBuchDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/sprechtag/\(id)/buchen", koerper: try encoder.encode(daten))
    }

    /// POST /sprechtag/{id}/termine/{tid}/absagen – Termin mit (optionalem) Grund absagen; die andere Seite wird benachrichtigt.
    func sprechtagTerminAbsagen(_ id: Int, termin: Int, grund: String?) async throws {
        let text = (grund ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let _: BeliebigeAntwort = try await anfrage("POST", "/sprechtag/\(id)/termine/\(termin)/absagen",
                                                    koerper: try encoder.encode(SprechtagAbsageDaten(grund: text.isEmpty ? nil : text)))
    }

    /// PUT /sprechtag/{id}/sperren – Zeitfenster der eigenen Lehrkraft sperren bzw. freigeben.
    func sprechtagSperren(_ id: Int, beginne: [String], gesperrt: Bool) async throws {
        let _: BeliebigeAntwort = try await anfrage("PUT", "/sprechtag/\(id)/sperren",
                                                    koerper: try encoder.encode(SprechtagSperrDaten(beginne: beginne, gesperrt: gesperrt)))
    }

    // MARK: Push (Vorbereitung für Sofort-Mitteilungen über APNs)

    /// POST /push/geraet – Geräte-Token für Push-Mitteilungen beim Server hinterlegen (Endpunkt ist Server-Aufgabe;
    /// fehlt er, wird der Fehler vom Aufrufer ignoriert).
    func pushGeraetMelden(_ daten: PushGeraetDaten) async throws {
        let _: BeliebigeAntwort = try await anfrage("POST", "/push/geraet", koerper: try encoder.encode(daten))
    }

    /// DELETE /push/geraet?token=… – Token beim Abmelden entfernen.
    func pushGeraetAbmelden(token: String) async throws {
        let _: BeliebigeAntwort = try await anfrage("DELETE", "/push/geraet", parameter: ["token": token])
    }

    // MARK: Cookies (Sitzung je Konto)

    /// Cookies des aktuellen Servers.
    func cookies() -> [HTTPCookie] {
        guard let url = URL(string: serverAdresse), let speicher = session.configuration.httpCookieStorage else { return [] }
        return speicher.cookies(for: url) ?? []
    }

    /// Cookies setzen (gesicherte Sitzung eines Kontos).
    func cookiesSetzen(_ liste: [HTTPCookie]) {
        guard let speicher = session.configuration.httpCookieStorage else { return }
        for cookie in liste { speicher.setCookie(cookie) }
    }

    /// Entfernt die Cookies des Servers (nach dem Abmelden bzw. vor dem Wechsel in ein anderes Konto).
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

    /// Anfrage ohne JSON-Auswertung (Hochladen, Herunterladen): liefert die Rohdaten oder wirft den Fehler des Servers.
    private func rohAnfrage(_ anfrage: URLRequest, pfad: String) async throws -> Data {
        var req = anfrage
        req.setValue("1", forHTTPHeaderField: "X-UntPlanWeb")
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
        guard let http = ergebnis.1 as? HTTPURLResponse else { throw ApiFehler.antwortUnlesbar }
        if !(200..<300).contains(http.statusCode) {
            if http.statusCode == 401 { throw ApiFehler.nichtAngemeldet }
            let meldung = (try? decoder.decode(Fehlerantwort.self, from: ergebnis.0))?.fehler
            throw ApiFehler.server(meldung ?? "Fehler \(http.statusCode) vom Server.")
        }
        return ergebnis.0
    }

    private func anfrage<T: Decodable>(_ methode: String, _ pfad: String, parameter: [String: String] = [:],
                                       inhalt: [String: String]? = nil) async throws -> T {
        let koerper: Data? = try inhalt.map { try encoder.encode($0) }
        return try await anfrage(methode, pfad, parameter: parameter, koerper: koerper)
    }

    private func anfrage<T: Decodable>(_ methode: String, _ pfad: String, parameter: [String: String] = [:],
                                       koerper: Data?) async throws -> T {
        var req = URLRequest(url: try url(pfad, parameter))
        req.httpMethod = methode
        // Schutzkopf von UntPlanWeb (ohne ihn lehnt der Server ändernde Aufrufe ab)
        req.setValue("1", forHTTPHeaderField: "X-UntPlanWeb")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let koerper {
            req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            req.httpBody = koerper
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
        // Leere Antwort (z. B. 204): als leeres Objekt lesen, damit OkAntwort und BeliebigeAntwort nicht scheitern
        let inhalt = daten.isEmpty ? Data("{}".utf8) : daten
        do {
            return try decoder.decode(T.self, from: inhalt)
        } catch {
            throw ApiFehler.antwortUnlesbar
        }
    }
}
