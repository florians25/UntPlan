import Foundation

// Modelle des Sprechtags (Elternsprechtag) – /api/app/sprechtag/…, alle Felder nachsichtig gelesen.

/// Hinweiskarte der Startseite (GET /api/app/sprechtag/start).
struct SprechtagKarte: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var name: String
    var datum: String
    var datumText: String
    /// einladung, anmeldung, geschlossen, abgesagt …
    var phase: String
    var text: String
    var termine: Int
    var einladungen: Int

    enum CodingKeys: String, CodingKey { case id, name, datum, datumText, phase, text, termine, einladungen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        name = c.text(.name)
        datum = String(c.text(.datum).prefix(10))
        datumText = c.text(.datumText)
        phase = c.text(.phase).lowercased()
        text = c.text(.text)
        termine = c.zahl(.termine) ?? 0
        einladungen = c.zahl(.einladungen) ?? 0
    }

    /// Titel der Zeile auf der Startseite.
    var titel: String { name.isEmpty ? "Sprechtag" : name }
}

/// Kopfdaten eines Sprechtags.
struct SprechtagInfo: Decodable, Sendable {
    var id: Int
    var name: String
    var datum: String
    var datumText: String
    var beginn: String
    var ende: String
    var dauer: Int
    var phase: String
    var abgesagt: Bool
    var absageGrund: String
    var hinweis: String
    var anmeldungBis: String

    enum CodingKeys: String, CodingKey { case id, name, datum, datumText, beginn, ende, dauer, phase, abgesagt, absageGrund, hinweis, anmeldungBis }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        name = c.text(.name)
        datum = String(c.text(.datum).prefix(10))
        datumText = c.text(.datumText)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        dauer = c.zahl(.dauer) ?? 0
        phase = c.text(.phase).lowercased()
        // „abgesagt“ kommt je nach Stand als Wahrheitswert oder als Zeitpunkt/Text
        let roh = (c.textOderNil(.abgesagt) ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        abgesagt = (!roh.isEmpty && roh != "false" && roh != "0") || phase == "abgesagt"
        absageGrund = c.text(.absageGrund)
        hinweis = c.text(.hinweis)
        anmeldungBis = c.text(.anmeldungBis)
    }

    var titel: String { name.isEmpty ? "Sprechtag" : name }

    /// Phase in Worten.
    var phaseText: String {
        switch phase {
        case "einladung": return "Einladungsphase"
        case "anmeldung": return "Anmeldung geöffnet"
        case "geschlossen": return "Anmeldung geschlossen"
        case "vorbei": return "Vorbei"
        case "abgesagt": return "Abgesagt"
        case "vorbereitung": return "In Vorbereitung"
        default: return phase.capitalized
        }
    }
}

/// Schüler*in eines Termins.
struct SprechtagSchueler: Decodable, Hashable, Sendable {
    var id: Int
    var vorname: String
    var name: String
    var klasse: String

    enum CodingKeys: String, CodingKey { case id, vorname, name, klasse }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        vorname = c.text(.vorname)
        name = c.text(.name)
        klasse = c.text(.klasse)
    }

    var anzeige: String { [vorname, name].filter { !$0.isEmpty }.joined(separator: " ") }
}

/// Gebuchter Termin (Lehrkraft-Sicht).
struct SprechtagTermin: Decodable, Identifiable, Sendable {
    var id: Int
    var lehrer: String
    var lehrerName: String
    var beginn: String
    var ende: String
    var schueler: SprechtagSchueler?
    var bemerkung: String
    var gebuchtVon: String

    enum CodingKeys: String, CodingKey { case id, lehrer, lehrerName, beginn, ende, schueler, bemerkung, gebuchtVon }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        lehrer = c.text(.lehrer)
        lehrerName = c.text(.lehrerName)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        schueler = c.optional(.schueler)
        bemerkung = c.text(.bemerkung)
        gebuchtVon = c.text(.gebuchtVon)
    }
}

/// Zeitfenster im Raster einer Lehrkraft (Status: frei, gebucht, gesperrt).
struct SprechtagZeitfenster: Decodable, Identifiable, Sendable {
    var beginn: String
    var ende: String
    var status: String
    var termin: SprechtagTermin?

    var id: String { beginn }

    enum CodingKeys: String, CodingKey { case beginn, ende, status, termin }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        termin = c.optional(.termin)
        let roh = c.text(.status).lowercased()
        status = roh.isEmpty ? (termin == nil ? "frei" : "gebucht") : roh
    }

    var gebucht: Bool { status == "gebucht" || termin != nil }
    var gesperrt: Bool { status == "gesperrt" }
    var frei: Bool { !gebucht && !gesperrt }
}

/// Eigene Lehrkraft am Sprechtag.
struct SprechtagLehrkraftKopf: Decodable, Sendable {
    var kurz: String
    var name: String
    var raum: String
    var abgesagt: String
    var absageGrund: String

    enum CodingKeys: String, CodingKey { case kurz, name, raum, abgesagt, absageGrund }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        raum = c.text(.raum)
        let roh = c.text(.abgesagt)
        abgesagt = roh == "false" ? "" : roh
        absageGrund = c.text(.absageGrund)
    }
}

/// GET /api/app/sprechtag/{id}/lehrkraft
struct SprechtagLehrkraftDaten: Decodable, Sendable {
    var sprechtag: SprechtagInfo?
    var lehrer: SprechtagLehrkraftKopf?
    var zeitfenster: [SprechtagZeitfenster]

    enum CodingKeys: String, CodingKey { case sprechtag, lehrer, zeitfenster }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        sprechtag = c.optional(.sprechtag)
        lehrer = c.optional(.lehrer)
        zeitfenster = c.liste(.zeitfenster)
    }
}

/// Freies Zeitfenster einer Lehrkraft (Eltern-Sicht); konflikt = überschneidet sich mit einem eigenen Termin.
struct SprechtagFreiesFenster: Decodable, Identifiable, Hashable, Sendable {
    var beginn: String
    var ende: String
    var konflikt: Bool

    var id: String { beginn }

    enum CodingKeys: String, CodingKey { case beginn, ende, konflikt }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        konflikt = c.wahr(.konflikt)
    }
}

/// Kurzform eines gebuchten Termins bei einer Lehrkraft des Kindes.
struct SprechtagTerminKurz: Decodable, Hashable, Sendable {
    var id: Int
    var beginn: String
    var ende: String

    enum CodingKeys: String, CodingKey { case id, beginn, ende }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
    }
}

/// Lehrkraft eines Kindes (Eltern-Sicht).
struct SprechtagKindLehrkraft: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String
    var faecher: [String]
    var klassenleitung: Bool
    var raum: String
    var abgesagt: Bool
    var eingeladen: Bool
    var einladungText: String
    var termin: SprechtagTerminKurz?
    var zeitfenster: [SprechtagFreiesFenster]

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, faecher, klassenleitung, raum, abgesagt, eingeladen, einladungText, termin, zeitfenster }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        // Fächer als Liste oder als Text
        let liste = c.texte(.faecher)
        faecher = liste.isEmpty ? c.text(.faecher).split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty } : liste
        klassenleitung = c.wahr(.klassenleitung)
        raum = c.text(.raum)
        abgesagt = c.wahr(.abgesagt)
        eingeladen = c.wahr(.eingeladen)
        einladungText = c.text(.einladungText)
        termin = c.optional(.termin)
        zeitfenster = c.liste(.zeitfenster)
    }

    var anzeige: String { name.isEmpty ? kurz : name }
}

/// Kind mit seinen Lehrkräften (Eltern-Sicht).
struct SprechtagKind: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var vorname: String
    var name: String
    var klasse: String
    var lehrkraefte: [SprechtagKindLehrkraft]

    enum CodingKeys: String, CodingKey { case id, vorname, name, klasse, lehrkraefte }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        vorname = c.text(.vorname)
        name = c.text(.name)
        klasse = c.text(.klasse)
        lehrkraefte = c.liste(.lehrkraefte)
    }

    var anzeige: String { [vorname, name].filter { !$0.isEmpty }.joined(separator: " ") }
}

/// Termin der eigenen Familie (Eltern-Sicht).
struct SprechtagElternTermin: Decodable, Identifiable, Sendable {
    var id: Int
    var beginn: String
    var ende: String
    var lehrer: String
    var lehrerName: String
    var raum: String
    var schueler: SprechtagSchueler?
    var bemerkung: String
    var eigene: Bool

    enum CodingKeys: String, CodingKey { case id, beginn, ende, lehrer, lehrerName, raum, schueler, bemerkung, eigene }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        lehrer = c.text(.lehrer)
        lehrerName = c.text(.lehrerName)
        raum = c.text(.raum)
        schueler = c.optional(.schueler)
        bemerkung = c.text(.bemerkung)
        eigene = c.wahr(.eigene)
    }
}

/// GET /api/app/sprechtag/{id}/eltern
struct SprechtagElternDaten: Decodable, Sendable {
    var sprechtag: SprechtagInfo?
    var buchbar: Bool
    var kinder: [SprechtagKind]
    var termine: [SprechtagElternTermin]

    enum CodingKeys: String, CodingKey { case sprechtag, buchbar, kinder, termine }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        sprechtag = c.optional(.sprechtag)
        buchbar = c.wahr(.buchbar)
        kinder = c.liste(.kinder)
        termine = c.liste(.termine)
    }
}

/// POST /api/app/sprechtag/{id}/buchen
struct SprechtagBuchDaten: Encodable, Sendable {
    var lehrer: String
    var schuelerId: Int
    var beginn: String
}

/// POST /api/app/sprechtag/{id}/termine/{tid}/absagen
struct SprechtagAbsageDaten: Encodable, Sendable {
    var grund: String?
}

/// PUT /api/app/sprechtag/{id}/sperren
struct SprechtagSperrDaten: Encodable, Sendable {
    var beginne: [String]
    var gesperrt: Bool
}
