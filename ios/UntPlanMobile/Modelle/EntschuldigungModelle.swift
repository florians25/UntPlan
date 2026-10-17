import Foundation

// Entschuldigungen (Fachlehrer-Entschuldigungen und Entschuldigungen der Klassenleitung) – App-API unter
// /api/app/entschuldigungen. Alle Felder werden nachsichtig gelesen (fehlend, anders getypt oder null = Standardwert).

/// Status einer Entschuldigung: „offen“, „entschuldigt“ oder „unentschuldigt“ (Anzeige „nicht entschuldigt“).
enum Entschuldigungsstatus: String, CaseIterable, Identifiable, Sendable {
    case offen
    case entschuldigt
    case unentschuldigt

    var id: String { rawValue }

    /// Status aus dem Server-Text (unbekannt = offen).
    init(text: String) {
        switch text.trimmingCharacters(in: .whitespaces).lowercased() {
        case "entschuldigt": self = .entschuldigt
        case "unentschuldigt", "nicht entschuldigt", "nichtentschuldigt": self = .unentschuldigt
        default: self = .offen
        }
    }

    var anzeige: String {
        switch self {
        case .offen: return "Offen"
        case .entschuldigt: return "Entschuldigt"
        case .unentschuldigt: return "Nicht entschuldigt"
        }
    }
}

/// GET /entschuldigungen/einstellungen – was darf das Konto?
struct Entschuldigungsrechte: Decodable, Sendable {
    var aktiv: Bool
    var fachlehrer: Bool
    var klassenleitung: Bool
    var klassen: [String]

    enum CodingKeys: String, CodingKey { case aktiv, fachlehrer, klassenleitung, klassen }

    init(aktiv: Bool = false, fachlehrer: Bool = false, klassenleitung: Bool = false, klassen: [String] = []) {
        self.aktiv = aktiv
        self.fachlehrer = fachlehrer
        self.klassenleitung = klassenleitung
        self.klassen = klassen
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        aktiv = c.wahr(.aktiv)
        fachlehrer = c.wahr(.fachlehrer)
        klassenleitung = c.wahr(.klassenleitung)
        klassen = c.texte(.klassen)
    }

    /// Klassenleitung dieser Klasse (Groß-/Kleinschreibung egal)?
    func leitet(_ klasse: String) -> Bool {
        klassenleitung && klassen.contains { $0.caseInsensitiveCompare(klasse) == .orderedSame }
    }

    /// Darf das Konto überhaupt etwas entschuldigen (Menüpunkt auf der Startseite)?
    var irgendwas: Bool { fachlehrer || klassenleitung }
}

/// Entschuldigung genau einer Stunde (wer, Text, Datum).
struct Stundenentschuldigung: Decodable, Sendable, Hashable {
    var status: String
    var text: String
    var lehrer: String
    /// „fachlehrer“ bzw. „klassenleitung“
    var rolle: String
    var datum: String
    var zeit: String

    enum CodingKeys: String, CodingKey { case status, text, lehrer, rolle, datum, zeit }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        status = c.text(.status)
        text = c.text(.text)
        lehrer = c.text(.lehrer)
        rolle = c.text(.rolle)
        datum = String(c.text(.datum).prefix(10))
        zeit = c.text(.zeit)
    }

    var stand: Entschuldigungsstatus { Entschuldigungsstatus(text: status) }
}

/// Entschuldigungstext einer anderen Stunde derselben Abwesenheit („zuletzt eingetragene Texte“).
struct Entschuldigungstext: Decodable, Sendable, Hashable {
    var stunde: Int
    var fach: String
    var lehrer: String
    var status: String
    var text: String
    var zeit: String
    var rolle: String

    enum CodingKeys: String, CodingKey { case stunde, fach, lehrer, status, text, zeit, rolle }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        stunde = c.zahl(.stunde) ?? 0
        fach = c.text(.fach)
        lehrer = c.text(.lehrer)
        status = c.text(.status)
        text = c.text(.text)
        zeit = c.text(.zeit)
        rolle = c.text(.rolle)
    }
}

/// Fehlstunde aus dem eigenen Unterricht (Fachlehrer-Sicht).
struct Fehlstunde: Decodable, Identifiable, Sendable, Hashable {
    var id: Int
    var schuelerId: Int
    var schueler: String
    var klasse: String
    var datum: String
    var stunde: Int
    var beginn: String
    var ende: String
    var fach: String
    var lehrer: String
    var art: String
    var fehlminuten: Int
    var grund: String
    var status: String
    var abwesenheitText: String
    var entschuldigung: Stundenentschuldigung?
    var texte: [Entschuldigungstext]
    var darf: Bool
    var gesperrt: Bool
    var hinweis: String

    enum CodingKeys: String, CodingKey {
        case id, schuelerId, schueler, klasse, datum, stunde, beginn, ende, fach, lehrer, art, fehlminuten, grund, status
        case abwesenheitText, entschuldigung, texte, darf, gesperrt, hinweis
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        schuelerId = c.zahl(.schuelerId) ?? 0
        schueler = c.text(.schueler)
        klasse = c.text(.klasse)
        datum = String(c.text(.datum).prefix(10))
        stunde = c.zahl(.stunde) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        fach = c.text(.fach)
        lehrer = c.text(.lehrer)
        art = c.textOderNil(.art) ?? "fehlt"
        fehlminuten = c.zahl(.fehlminuten) ?? 0
        grund = c.text(.grund)
        status = c.textOderNil(.status) ?? "offen"
        abwesenheitText = c.text(.abwesenheitText)
        entschuldigung = c.optional(.entschuldigung)
        texte = c.liste(.texte)
        darf = c.wahr(.darf)
        gesperrt = c.wahr(.gesperrt)
        hinweis = c.text(.hinweis)
    }

    var stand: Entschuldigungsstatus { Entschuldigungsstatus(text: status) }

    /// „Mo., 12.10.2026, 9:50 - 10:35“
    var zeitraum: String {
        var t = Datumsformat.anzeige(datum, "EE, dd.MM.yyyy")
        if !beginn.isEmpty { t += ", \(Detailmass.uhrzeit(beginn)) - \(Detailmass.uhrzeit(ende))" }
        return t
    }
}

/// GET /entschuldigungen/fehlzeiten
struct Fehlstundenliste: Decodable, Sendable {
    var klassen: [String]
    var darf: Bool
    var zeilen: [Fehlstunde]

    enum CodingKeys: String, CodingKey { case klassen, darf, zeilen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        klassen = c.texte(.klassen)
        darf = c.wahr(.darf)
        zeilen = c.liste(.zeilen)
    }
}

/// Stunde einer Abwesenheit mit dem Stand der Fachlehrer-Entschuldigung (Klassenleitungs-Sicht).
struct Abwesenheitsstunde: Decodable, Identifiable, Sendable, Hashable {
    var id: Int
    var datum: String
    var stunde: Int
    var beginn: String
    var ende: String
    var fach: String
    var lehrer: String
    var status: String
    var entschuldigung: Stundenentschuldigung?

    enum CodingKeys: String, CodingKey { case id, datum, stunde, beginn, ende, fach, lehrer, status, entschuldigung }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        datum = String(c.text(.datum).prefix(10))
        stunde = c.zahl(.stunde) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        fach = c.text(.fach)
        lehrer = c.text(.lehrer)
        status = c.textOderNil(.status) ?? "offen"
        entschuldigung = c.optional(.entschuldigung)
    }

    var stand: Entschuldigungsstatus { Entschuldigungsstatus(text: status) }
}

/// Abwesenheit (Zeitraum) der eigenen Klassen mit Stand der Fachlehrer-Entschuldigungen.
struct EntschuldigungsAbwesenheit: Decodable, Identifiable, Sendable, Hashable {
    var schluessel: String
    var ids: [Int]
    var schuelerId: Int
    var schueler: String
    var klasse: String
    var vonDatum: String
    var vonZeit: String
    var bisDatum: String
    var bisZeit: String
    var art: String
    var grund: String
    var status: String
    var mehrdeutig: Bool
    var text: String
    var fehlstunden: Int
    var nachweis: String
    var entschuldigtAm: String
    var lehrerGesamt: Int
    var lehrerBearbeitet: Int
    var darf: Bool
    var stunden: [Abwesenheitsstunde]

    var id: String { schluessel }

    enum CodingKeys: String, CodingKey {
        case schluessel, ids, schuelerId, schueler, klasse, vonDatum, vonZeit, bisDatum, bisZeit, art, grund, status
        case mehrdeutig, text, fehlstunden, nachweis, entschuldigtAm, lehrerGesamt, lehrerBearbeitet, darf, stunden
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        ids = c.liste(.ids)
        schuelerId = c.zahl(.schuelerId) ?? 0
        vonDatum = String(c.text(.vonDatum).prefix(10))
        vonZeit = c.text(.vonZeit)
        schluessel = c.textOderNil(.schluessel) ?? "\(schuelerId):\(ids.first ?? 0):\(vonDatum)"
        schueler = c.text(.schueler)
        klasse = c.text(.klasse)
        bisDatum = String(c.text(.bisDatum).prefix(10))
        bisZeit = c.text(.bisZeit)
        art = c.textOderNil(.art) ?? "fehlt"
        grund = c.text(.grund)
        status = c.textOderNil(.status) ?? "offen"
        mehrdeutig = c.wahr(.mehrdeutig)
        text = c.text(.text)
        fehlstunden = c.zahl(.fehlstunden) ?? 0
        nachweis = c.text(.nachweis)
        entschuldigtAm = String(c.text(.entschuldigtAm).prefix(10))
        lehrerGesamt = c.zahl(.lehrerGesamt) ?? 0
        lehrerBearbeitet = c.zahl(.lehrerBearbeitet) ?? 0
        darf = c.wahr(.darf)
        stunden = c.liste(.stunden)
    }

    var stand: Entschuldigungsstatus { Entschuldigungsstatus(text: status) }

    /// „13.10.2026, 8:00 - 13:15“ bzw. über mehrere Tage „13.10.2026 8:00 - 14.10.2026 13:15“.
    var zeitraum: String {
        let von = Datumsformat.anzeige(vonDatum, "dd.MM.yyyy")
        if bisDatum.isEmpty || bisDatum == vonDatum {
            return "\(von), \(Detailmass.uhrzeit(vonZeit)) - \(Detailmass.uhrzeit(bisZeit))"
        }
        return "\(von) \(Detailmass.uhrzeit(vonZeit)) - \(Datumsformat.anzeige(bisDatum, "dd.MM.yyyy")) \(Detailmass.uhrzeit(bisZeit))"
    }

    /// „2/3 Lehrer bearbeitet“ (nur wenn Fachlehrkräfte beteiligt sind).
    var bearbeitetText: String? {
        lehrerGesamt > 0 ? "\(lehrerBearbeitet)/\(lehrerGesamt) Lehrer bearbeitet" : nil
    }

    /// Stunden, deren Fachlehrer-Status sich vom gewählten Status unterscheidet (werden überschrieben).
    func ueberschreibt(_ neu: Entschuldigungsstatus) -> Int {
        stunden.filter { s in
            guard let e = s.entschuldigung else { return false }
            return e.stand != .offen && e.stand != neu
        }.count
    }
}

/// POST /entschuldigungen/fehlzeiten – Fachlehrkraft entschuldigt eine oder mehrere Stunden.
struct FehlstundenEntschuldigenDaten: Encodable, Sendable {
    var ids: [Int]
    var status: String
    var text: String?
    var datum: String?
}

/// POST /entschuldigungen/abwesenheit – Klassenleitung entschuldigt ganze Abwesenheiten.
struct AbwesenheitEntschuldigenDaten: Encodable, Sendable {
    var ids: [Int]
    var status: String
    var text: String?
    var datum: String?
    var nachweis: String?
}

/// Antwort der Entschuldigungs-Endpunkte (alle Felder optional).
struct EntschuldigungsAntwort: Decodable, Sendable {
    var geaendert: Int
    var ueberschrieben: Int
    var gesperrt: [Int]

    enum CodingKeys: String, CodingKey { case geaendert, ueberschrieben, gesperrt }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        geaendert = c.zahl(.geaendert) ?? 0
        ueberschrieben = c.zahl(.ueberschrieben) ?? 0
        gesperrt = c.liste(.gesperrt)
    }
}

/// Art des Nachweises (Klassenleitung).
enum Entschuldigungsnachweis: String, CaseIterable, Identifiable, Sendable {
    case keiner = ""
    case attest
    case schriftlich
    case muendlich
    case telefonisch
    case email

    var id: String { rawValue }

    var anzeige: String {
        switch self {
        case .keiner: return "Kein Nachweis"
        case .attest: return "Attest"
        case .schriftlich: return "Schriftlich"
        case .muendlich: return "Mündlich"
        case .telefonisch: return "Telefonisch"
        case .email: return "E-Mail"
        }
    }
}
