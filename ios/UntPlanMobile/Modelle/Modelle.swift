import Foundation

// Modelle der JSON-API von UntPlanWeb (/api/app, camelCase). Alle Felder werden nachsichtig gelesen.

// MARK: - Sitzung

/// GET /api/app/sitzung
struct Sitzung: Decodable, Sendable {
    var eingerichtet: Bool
    var schule: Schule?
    var paket: Paketstand?
    var schuljahr: Schuljahr?
    var benutzer: SitzungsBenutzer?
    var ungelesen: Int

    enum CodingKeys: String, CodingKey { case eingerichtet, schule, paket, schuljahr, benutzer, ungelesen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        eingerichtet = c.wahr(.eingerichtet)
        schule = c.optional(.schule)
        paket = c.optional(.paket)
        schuljahr = c.optional(.schuljahr)
        benutzer = c.optional(.benutzer)
        ungelesen = c.zahl(.ungelesen) ?? 0
    }
}

struct Schule: Decodable, Sendable {
    var name: String
    var schulId: String?

    enum CodingKeys: String, CodingKey { case name, schulId }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = c.text(.name)
        schulId = c.textOderNil(.schulId)
    }
}

struct Paketstand: Decodable, Sendable {
    var vorhanden: Bool
    var revision: String?
    var empfangen: String?

    enum CodingKeys: String, CodingKey { case vorhanden, revision, empfangen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        vorhanden = c.wahr(.vorhanden)
        revision = c.textOderNil(.revision)
        empfangen = c.textOderNil(.empfangen)
    }
}

struct Schuljahr: Decodable, Sendable {
    var von: String
    var bis: String

    enum CodingKeys: String, CodingKey { case von, bis }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        von = c.text(.von)
        bis = c.text(.bis)
    }
}

struct SitzungsBenutzer: Decodable, Sendable {
    var id: Int
    var login: String
    var anzeigename: String
    var rolle: String
    var rollenname: String
    var element: String
    var kollegium: Bool
    var admin: Bool
    var nachrichten: Bool
    var passwortAendern: Bool
    /// Planart des eigenen Plans: "klasse" oder "lehrer"
    var typ: String
    /// Schüler- bzw. Elternkonto (Seite „Meine Abwesenheiten“)
    var familie: Bool
    /// Fehlzeitenverwaltung (Klassenleitung, Schulleitung, Administration)
    var fehlzeiten: Bool

    enum CodingKeys: String, CodingKey {
        case id, login, anzeigename, rolle, rollenname, element, kollegium, admin, nachrichten, passwortAendern, typ, familie, fehlzeiten
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        login = c.text(.login)
        anzeigename = c.text(.anzeigename)
        rolle = c.text(.rolle)
        rollenname = c.text(.rollenname)
        element = c.text(.element)
        kollegium = c.wahr(.kollegium)
        admin = c.wahr(.admin)
        nachrichten = c.wahr(.nachrichten)
        passwortAendern = c.wahr(.passwortAendern)
        typ = c.text(.typ)
        familie = c.wahr(.familie)
        fehlzeiten = c.wahr(.fehlzeiten)
    }

    var name: String { anzeigename.isEmpty ? login : anzeigename }
}

// MARK: - Plan

/// Angezeigter Wert (Klasse, Lehrkraft, Raum); alt = ursprünglicher Wert, neu = geändert.
struct Wert: Decodable, Hashable, Sendable {
    var k: String
    var alt: String?
    var neu: Bool

    enum CodingKeys: String, CodingKey { case k, alt, neu }

    init(k: String, alt: String? = nil, neu: Bool = false) {
        self.k = k
        self.alt = alt
        self.neu = neu
    }

    init(from decoder: Decoder) throws {
        // Nachsichtig: auch ein einfacher Text (z. B. "INF13") wird als Wert gelesen
        if let c = try? decoder.container(keyedBy: CodingKeys.self) {
            k = c.text(.k)
            alt = c.textOderNil(.alt)
            neu = c.wahr(.neu)
            return
        }
        let einzeln = try decoder.singleValueContainer()
        if let s = try? einzeln.decode(String.self) {
            k = s
        } else if let i = try? einzeln.decode(Int.self) {
            k = String(i)
        } else {
            k = ""
        }
        alt = nil
        neu = false
    }

    /// Wurde der Wert geändert (neu oder mit abweichendem altem Wert)?
    var geaendert: Bool {
        if neu { return true }
        if let alt, !alt.isEmpty, alt != k { return true }
        return false
    }
}

/// Stundenkarte. Status: regulaer, geaendert, offen, entfall, abwesend, klausur, bereitschaft, buchung, vorgemerkt.
struct Karte: Decodable, Identifiable, Sendable {
    var id = UUID()
    var datum: String
    var von: Int
    var bis: Int
    var beginn: String
    var ende: String
    var status: String
    var fach: String
    var fachAlt: String?
    var fachName: String
    var farbe: String
    var klassen: [Wert]
    /// Koppelklassen (nur im Klassenplan): anwesend {k:"INF13"}, fehlend {k:"", alt:"INF13", neu:true}
    var mitklassen: [Wert]
    var lehrer: [Wert]
    var raeume: [Wert]
    var art: String
    var text: String
    /// Offene Vertretung: die Stunde ist noch keiner Lehrkraft zugeteilt (auch bei Status „abwesend“).
    var vertretungOffen: Bool
    var vertretung: Int?
    var unterricht: Int?
    var gruppe: String
    var notizSchueler: String
    var notizLehrer: String
    var hausaufgabeFaellig: Bool
    var zusaetzlich: Bool
    var abwesendeKlassen: [String]
    var abwesendText: String
    /// Stunde findet online statt (Klassenbuch) – Karte grün mit Plakette „Online“.
    var online: Bool
    /// Die eingeteilte Lehrkraft ist abwesend (z. B. Bereitschaft der abwesenden Lehrkraft) – falls der Server es liefert.
    var lehrerAbwesend: Bool
    /// Bereitschaftsstunde (falls der Server es ausdrücklich liefert; sonst aus Status bzw. Fach abgeleitet).
    var bereitschaft: Bool

    enum CodingKeys: String, CodingKey {
        case datum, von, bis, beginn, ende, status, fach, fachAlt, fachName, farbe, klassen, lehrer, raeume, art, text
        case vertretung, unterricht, gruppe, notizSchueler, notizLehrer, hausaufgabeFaellig, zusaetzlich, abwesendeKlassen, abwesendText, online
        case lehrerAbwesend, bereitschaft, bereitschaftAbwesend, standbyAbsent, mitklassen, vertretungOffen
    }

    /// Karte ohne Server-Antwort (Veranstaltung aus den Tagesdaten, geteilte Blöcke).
    init(datum: String, von: Int, bis: Int, beginn: String, ende: String, status: String,
         fach: String = "", fachName: String = "", farbe: String = "",
         klassen: [Wert] = [], lehrer: [Wert] = [], raeume: [Wert] = [], art: String = "", text: String = "") {
        self.datum = datum
        self.von = von
        self.bis = max(bis, von)
        self.beginn = beginn
        self.ende = ende
        self.status = status
        self.fach = fach
        self.fachAlt = nil
        self.fachName = fachName
        self.farbe = farbe
        self.klassen = klassen
        self.mitklassen = []
        self.vertretungOffen = false
        self.lehrer = lehrer
        self.raeume = raeume
        self.art = art
        self.text = text
        self.vertretung = nil
        self.unterricht = nil
        self.gruppe = ""
        self.notizSchueler = ""
        self.notizLehrer = ""
        self.hausaufgabeFaellig = false
        self.zusaetzlich = false
        self.abwesendeKlassen = []
        self.abwesendText = ""
        self.online = false
        self.lehrerAbwesend = false
        self.bereitschaft = false
    }

    /// Teil eines Blocks (z. B. an einer Pausenaufsicht geteilt) – mit eigener Kennung.
    func teil(von: Int, bis: Int, beginn: String, ende: String) -> Karte {
        var k = self
        k.id = UUID()
        k.von = von
        k.bis = max(bis, von)
        k.beginn = beginn
        k.ende = ende
        return k
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        datum = String(c.text(.datum).prefix(10))
        let stunde = c.zahl(.von) ?? 0
        von = stunde
        bis = max(c.zahl(.bis) ?? 0, stunde)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        // Status robust erkennen (Groß-/Kleinschreibung, Leerzeichen), z. B. „Klausur“ → „klausur“
        let rohStatus = (c.textOderNil(.status) ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        status = rohStatus.isEmpty ? "regulaer" : rohStatus
        fach = c.text(.fach)
        fachAlt = c.textOderNil(.fachAlt)
        fachName = c.text(.fachName)
        farbe = c.text(.farbe)
        klassen = c.liste(.klassen)
        mitklassen = c.liste(.mitklassen)
        vertretungOffen = c.wahr(.vertretungOffen)
        lehrer = c.liste(.lehrer)
        raeume = c.liste(.raeume)
        art = c.text(.art)
        text = c.text(.text)
        vertretung = c.zahl(.vertretung)
        unterricht = c.zahl(.unterricht)
        gruppe = c.text(.gruppe)
        notizSchueler = c.text(.notizSchueler)
        notizLehrer = c.text(.notizLehrer)
        hausaufgabeFaellig = c.wahr(.hausaufgabeFaellig)
        zusaetzlich = c.wahr(.zusaetzlich)
        abwesendeKlassen = c.texte(.abwesendeKlassen)
        abwesendText = c.text(.abwesendText)
        online = c.wahr(.online)
        lehrerAbwesend = c.wahr(.lehrerAbwesend) || c.wahr(.bereitschaftAbwesend) || c.wahr(.standbyAbsent)
        bereitschaft = c.wahr(.bereitschaft)
    }

    var istEntfall: Bool { status == "entfall" }
    var istAenderung: Bool { status == "geaendert" || status == "offen" }
    var istPruefung: Bool {
        let a = art.lowercased()
        return status == "klausur" || status == "pruefung" || status == "prüfung" || a.hasPrefix("klausur") || a.hasPrefix("prüfung")
    }

    /// Veranstaltung bzw. Termin (Status oder Art) – wie im Kachelbau auch eine geänderte Stunde ohne Unterricht und Fach,
    /// die nur einen Text trägt.
    var istVeranstaltung: Bool {
        if status == "veranstaltung" || status == "termin" { return true }
        if art.lowercased().hasPrefix("veranstaltung") && !istPruefung { return true }
        return status == "geaendert" && unterricht == nil && fach.isEmpty && !text.isEmpty
    }

    /// Dezente graue Pille im Kopf der Stundendetails (wie im Web): „Prüfung“ bzw. „Veranstaltung“, nie bei Entfall.
    var statusPille: String? {
        guard !istEntfall else { return nil }
        if istPruefung { return "Prüfung" }
        if istVeranstaltung { return "Veranstaltung" }
        return nil
    }

    /// Raumbuchung (eigene Karte oder gebuchter Raum für eine Stunde) – nie als Vertretung darstellen.
    var istRaumbuchung: Bool {
        let a = art.lowercased()
        return status == "buchung" || a.hasPrefix("raumbuchung") || a.hasPrefix("raumänderung (vorgemerkt)")
    }

    /// Raumbuchungen und Klausuren erscheinen nie als Vertretung (keine grüne Farbe, kein Vertretungstext).
    var keineVertretung: Bool { istRaumbuchung || istPruefung }

    /// Offene Vertretung (Feld vertretungOffen, Status „offen“ oder Lehrkraft ohne neuen Wert mit altem Wert); nie bei Entfall.
    var istOffeneVertretung: Bool {
        guard !istEntfall else { return false }
        return vertretungOffen || status == "offen" || lehrer.contains { $0.k.isEmpty && !($0.alt ?? "").isEmpty }
    }

    /// Kürzel der Lehrkräfte einer offenen Vertretung (alter Wert; fehlt er, „???“).
    var offeneLehrkraefte: [String] {
        var liste = lehrer.filter { $0.k.isEmpty && ($0.neu || !($0.alt ?? "").isEmpty) }
            .map { ($0.alt ?? "").isEmpty ? "???" : ($0.alt ?? "") }
        if liste.isEmpty && vertretungOffen { liste = ["???"] }
        return liste
    }

    /// Bezeichnung für Listen: Fachname, sonst Kürzel bzw. Art.
    var titel: String {
        if !fachName.isEmpty { return fachName }
        if !fach.isEmpty { return fach }
        return art
    }
}

struct Aufsicht: Decodable, Identifiable, Sendable {
    var id = UUID()
    var datum: String
    var nachStunde: Int
    var beginn: String
    var ende: String
    var gang: String
    var gangName: String
    /// regulaer, vertreten, vertretung
    var status: String
    var lehrer: String
    var alt: String?

    enum CodingKeys: String, CodingKey { case datum, nachStunde, beginn, ende, gang, gangName, status, lehrer, alt }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        datum = String(c.text(.datum).prefix(10))
        nachStunde = c.zahl(.nachStunde) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        gang = c.text(.gang)
        gangName = c.text(.gangName)
        // Status robust erkennen (Groß-/Kleinschreibung, Leerzeichen), z. B. „Klausur“ → „klausur“
        let rohStatus = (c.textOderNil(.status) ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        status = rohStatus.isEmpty ? "regulaer" : rohStatus
        lehrer = c.text(.lehrer)
        alt = c.textOderNil(.alt)
    }
}

/// Veranstaltung an einem Tag (ganztägig oder in bestimmten Stunden).
struct Ereignis: Decodable, Identifiable, Sendable {
    var id = UUID()
    var titel: String
    var art: String
    var von: Int
    var bis: Int
    var ganztaegig: Bool
    var klassen: [String]
    var lehrer: [String]
    var raeume: [String]

    enum CodingKeys: String, CodingKey { case titel, art, von, bis, ganztaegig, klassen, lehrer, raeume }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        titel = c.text(.titel)
        art = c.text(.art)
        von = c.zahl(.von) ?? 0
        bis = c.zahl(.bis) ?? 0
        ganztaegig = c.wahr(.ganztaegig)
        klassen = c.texte(.klassen)
        lehrer = c.texte(.lehrer)
        raeume = c.texte(.raeume)
    }
}

/// Kopf eines Tages im Wochenplan.
struct Tageskopf: Decodable, Identifiable, Sendable {
    var datum: String
    var wochentag: String
    var woche: String
    var frei: Bool
    var freiGrund: String?
    var tagesaktuell: Bool
    var hinweise: [String]
    var ereignisse: [Ereignis]
    var freiArt: String?
    var freiVon: String?
    var freiBis: String?

    var id: String { datum }

    enum CodingKeys: String, CodingKey { case datum, wochentag, woche, frei, freiGrund, tagesaktuell, hinweise, ereignisse, freiArt, freiVon, freiBis }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        datum = String(c.text(.datum).prefix(10))
        wochentag = c.text(.wochentag)
        woche = c.text(.woche)
        frei = c.wahr(.frei)
        freiGrund = c.textOderNil(.freiGrund)
        tagesaktuell = c.wahr(.tagesaktuell)
        hinweise = c.texte(.hinweise)
        ereignisse = c.liste(.ereignisse)
        freiArt = c.textOderNil(.freiArt)
        freiVon = c.textOderNil(.freiVon)
        freiBis = c.textOderNil(.freiBis)
    }
}

/// Stunde im Zeitraster.
struct Zeit: Decodable, Identifiable, Sendable {
    var nr: Int
    var bezeichnung: String
    var beginn: String
    var ende: String

    var id: Int { nr }

    enum CodingKeys: String, CodingKey { case nr, bezeichnung, beginn, ende }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        nr = c.zahl(.nr) ?? 0
        bezeichnung = c.text(.bezeichnung)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
    }
}

/// GET /api/app/plan?typ=…&element=…&datum=…
struct Wochenplan: Decodable, Sendable {
    var typ: String
    var element: String
    var name: String
    var montag: String
    var woche: String
    var zeitraster: [Zeit]
    var tage: [Tageskopf]
    var karten: [Karte]
    var aufsichten: [Aufsicht]

    enum CodingKeys: String, CodingKey { case typ, element, name, montag, woche, zeitraster, tage, karten, aufsichten }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        typ = c.text(.typ)
        element = c.text(.element)
        name = c.text(.name)
        montag = String(c.text(.montag).prefix(10))
        woche = c.text(.woche)
        zeitraster = c.liste(.zeitraster)
        tage = c.liste(.tage)
        karten = c.liste(.karten)
        aufsichten = c.liste(.aufsichten)
    }
}

// MARK: - Startseite

/// Termin der nächsten zwei Wochen.
struct Termin: Decodable, Identifiable, Sendable {
    var id = UUID()
    var art: String
    var titel: String
    var von: String
    var bis: String
    var stundeVon: Int?
    var stundeBis: Int?
    var klassen: [String]

    enum CodingKeys: String, CodingKey { case art, titel, von, bis, stundeVon, stundeBis, klassen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        art = c.text(.art)
        titel = c.text(.titel)
        von = c.text(.von)
        bis = c.text(.bis)
        stundeVon = c.zahl(.stundeVon)
        stundeBis = c.zahl(.stundeBis)
        klassen = c.texte(.klassen)
    }
}

/// Klassenbucheintrag mit Hausaufgabe (Startseite von Klassenkonten).
struct Hausaufgabe: Decodable, Identifiable, Sendable {
    var id: Int
    var datum: String
    var stunde: Int
    var fach: String
    var lehrer: String
    var thema: String
    var hausaufgabe: String
    var faellig: String?

    enum CodingKeys: String, CodingKey { case id, datum, stunde, fach, lehrer, thema, hausaufgabe, faellig }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? Int.random(in: 1_000_000...9_999_999)
        datum = c.text(.datum)
        stunde = c.zahl(.stunde) ?? 0
        fach = c.text(.fach)
        lehrer = c.text(.lehrer)
        thema = c.text(.thema)
        hausaufgabe = c.text(.hausaufgabe)
        faellig = c.textOderNil(.faellig)
    }
}

/// GET /api/app/heute
struct Heute: Decodable, Sendable {
    var paket: Bool
    var datum: String
    var wochentag: String
    var woche: String
    var jetzt: String
    var element: String?
    var typ: String
    var karten: [Karte]
    var aufsichten: [Aufsicht]
    var hinweise: [String]
    var ereignisse: [Ereignis]
    var nachrichten: [String]
    var termine: [Termin]
    var aenderungen: Int
    var ungelesen: Int
    var offeneStunden: Int
    var hausaufgaben: [Hausaufgabe]
    var meldungen: [Meldung]

    enum CodingKeys: String, CodingKey {
        case paket, datum, wochentag, woche, jetzt, element, typ, karten, aufsichten, hinweise, ereignisse, nachrichten
        case termine, aenderungen, ungelesen, offeneStunden, hausaufgaben, meldungen
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        paket = c.wahr(.paket)
        datum = String(c.text(.datum).prefix(10))
        wochentag = c.text(.wochentag)
        woche = c.text(.woche)
        jetzt = c.text(.jetzt)
        element = c.textOderNil(.element)
        typ = c.text(.typ)
        karten = c.liste(.karten)
        aufsichten = c.liste(.aufsichten)
        hinweise = c.texte(.hinweise)
        ereignisse = c.liste(.ereignisse)
        nachrichten = c.texte(.nachrichten)
        termine = c.liste(.termine)
        aenderungen = c.zahl(.aenderungen) ?? 0
        ungelesen = c.zahl(.ungelesen) ?? 0
        offeneStunden = c.zahl(.offeneStunden) ?? 0
        hausaufgaben = c.liste(.hausaufgaben)
        meldungen = c.liste(.meldungen)
    }
}

// MARK: - Stammdaten

/// Klasse, Lehrkraft oder Raum aus /api/app/elemente.
struct Planelement: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
    }
}

struct Fach: Decodable, Identifiable, Sendable {
    var kurz: String
    var name: String
    var farbe: String

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, farbe }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        farbe = c.text(.farbe)
    }
}

struct Ferien: Decodable, Identifiable, Sendable {
    var id = UUID()
    var name: String
    var von: String
    var bis: String
    var feiertag: Bool

    enum CodingKeys: String, CodingKey { case name, von, bis, feiertag }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = c.text(.name)
        von = c.text(.von)
        bis = c.text(.bis)
        feiertag = c.wahr(.feiertag)
    }
}

/// GET /api/app/elemente
struct Elemente: Decodable, Sendable {
    var klassen: [Planelement]
    var lehrer: [Planelement]
    var raeume: [Planelement]
    var faecher: [Fach]
    var ferien: [Ferien]

    enum CodingKeys: String, CodingKey { case klassen, lehrer, raeume, faecher, ferien }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        klassen = c.liste(.klassen)
        lehrer = c.liste(.lehrer)
        raeume = c.liste(.raeume)
        faecher = c.liste(.faecher)
        ferien = c.liste(.ferien)
    }
}

// MARK: - Mitteilungen

struct Anhang: Decodable, Identifiable, Sendable {
    var id: Int
    var name: String
    var typ: String
    var groesse: Int

    enum CodingKeys: String, CodingKey { case id, name, typ, groesse }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        name = c.text(.name)
        typ = c.text(.typ)
        groesse = c.zahl(.groesse) ?? 0
    }
}

/// GET /api/app/mitteilungen?ordner=…
struct Mitteilung: Decodable, Identifiable, Sendable {
    var id: Int
    var absenderId: Int
    var absender: String
    var betreff: String
    var text: String
    var erstellt: String
    var wichtig: Bool
    /// Zeitpunkt des Lesens; nil = ungelesen (nur im Posteingang aussagekräftig)
    var gelesen: String?
    var empfaenger: [String]
    var anhaenge: [Anhang]

    enum CodingKeys: String, CodingKey { case id, absenderId, absender, betreff, text, erstellt, wichtig, gelesen, empfaenger, anhaenge }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        absenderId = c.zahl(.absenderId) ?? 0
        absender = c.text(.absender)
        betreff = c.text(.betreff)
        text = c.text(.text)
        erstellt = c.text(.erstellt)
        wichtig = c.wahr(.wichtig)
        gelesen = c.textOderNil(.gelesen)
        empfaenger = c.texte(.empfaenger)
        anhaenge = c.liste(.anhaenge)
    }

    var ungelesen: Bool { gelesen == nil }
}

// MARK: - Profil

/// GET /api/app/profil
struct Profil: Decodable, Sendable {
    var login: String
    var anzeigename: String
    var email: String
    var rolle: String
    var element: String
    var letzteAnmeldung: String?
    var vorherigeAnmeldung: String?
    var ical: String
    var kollegium: Bool

    enum CodingKeys: String, CodingKey { case login, anzeigename, email, rolle, element, letzteAnmeldung, vorherigeAnmeldung, ical, kollegium }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        login = c.text(.login)
        anzeigename = c.text(.anzeigename)
        email = c.text(.email)
        rolle = c.text(.rolle)
        element = c.text(.element)
        letzteAnmeldung = c.textOderNil(.letzteAnmeldung)
        vorherigeAnmeldung = c.textOderNil(.vorherigeAnmeldung)
        ical = c.text(.ical)
        kollegium = c.wahr(.kollegium)
    }
}

// MARK: - Meldungen

/// Eigene Meldung an die Planung (GET /api/app/meldungen), z. B. Abwesenheit (typ "absenz").
struct Meldung: Decodable, Identifiable, Sendable {
    var id: String
    var typ: String
    var erstellt: String
    var von: String?
    var bis: String?
    var stundeVon: Int?
    var stundeBis: Int?
    var titel: String?
    var grund: String?
    var text: String?
    var status: String

    enum CodingKeys: String, CodingKey { case id, typ, erstellt, von, bis, stundeVon, stundeBis, titel, grund, text, status }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.textOderNil(.id) ?? UUID().uuidString
        typ = c.text(.typ)
        erstellt = c.text(.erstellt)
        von = c.textOderNil(.von)
        bis = c.textOderNil(.bis)
        stundeVon = c.zahl(.stundeVon)
        stundeBis = c.zahl(.stundeBis)
        titel = c.textOderNil(.titel)
        grund = c.textOderNil(.grund)
        text = c.textOderNil(.text)
        status = c.text(.status)
    }

    var istAbwesenheit: Bool { typ == "absenz" }
}

// MARK: - Klassenbuch einer Stunde

/// Klassenbucheintrag einer Stunde (Lehrstoff, Notizen, online).
struct Klassenbucheintrag: Decodable, Sendable {
    var id: Int
    var datum: String
    var stunde: Int
    var thema: String
    var hausaufgabe: String
    var notiz: String
    var notizSchueler: String
    var online: Bool
    var lehrform: String
    var anmerkung: String
    var anwesenheit: String?
    var lehrer: String
    var faellig: String?

    enum CodingKeys: String, CodingKey { case id, datum, stunde, thema, hausaufgabe, notiz, notizSchueler, online, lehrform, anmerkung, anwesenheit, lehrer, faellig }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        datum = c.text(.datum)
        stunde = c.zahl(.stunde) ?? 0
        thema = c.text(.thema)
        hausaufgabe = c.text(.hausaufgabe)
        notiz = c.text(.notiz)
        notizSchueler = c.text(.notizSchueler)
        online = c.wahr(.online)
        lehrform = c.text(.lehrform)
        anmerkung = c.text(.anmerkung)
        anwesenheit = c.textOderNil(.anwesenheit)
        lehrer = c.text(.lehrer)
        faellig = c.textOderNil(.faellig)
    }
}

/// GET /api/app/klassenbuch/stunde
struct KlassenbuchStunde: Decodable, Sendable {
    var eintrag: Klassenbucheintrag?
    var darf: Bool

    enum CodingKeys: String, CodingKey { case eintrag, darf }

    init(eintrag: Klassenbucheintrag?, darf: Bool) {
        self.eintrag = eintrag
        self.darf = darf
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        eintrag = c.optional(.eintrag)
        darf = c.wahr(.darf)
    }
}

/// Hausaufgabe aus der Liste einer Stunde.
struct Hausaufgabeneintrag: Decodable, Identifiable, Sendable {
    var id: Int
    var datum: String
    var fach: String
    var lehrer: String
    var text: String
    var faellig: String?
    var bemerkung: String
    var erledigt: Bool

    enum CodingKeys: String, CodingKey { case id, datum, fach, lehrer, text, faellig, bemerkung, erledigt }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? Int.random(in: 1_000_000...9_999_999)
        datum = c.text(.datum)
        fach = c.text(.fach)
        lehrer = c.text(.lehrer)
        text = c.text(.text)
        faellig = c.textOderNil(.faellig)
        bemerkung = c.text(.bemerkung)
        erledigt = c.wahr(.erledigt)
    }
}

/// GET /api/app/klassenbuch/hausaufgabenliste
struct HausaufgabenDerStunde: Decodable, Sendable {
    var liste: [Hausaufgabeneintrag]
    var faellig: [Hausaufgabeneintrag]
    var darf: Bool

    enum CodingKeys: String, CodingKey { case liste, faellig, darf }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        liste = c.liste(.liste)
        faellig = c.liste(.faellig)
        darf = c.wahr(.darf)
    }
}

struct Schueler: Decodable, Identifiable, Hashable, Sendable {
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

    /// „Name Vorname“ wie in der Vorlage.
    var anzeige: String { [name, vorname].filter { !$0.isEmpty }.joined(separator: " ") }
}

/// Erfasste Fehlzeit einer Schülerin/eines Schülers in der Stunde (art: fehlt, verspaetet, vorzeitig).
/// Minutengenau: beginn/ende im Block; vonDatum/vonZeit bis bisDatum/bisZeit = Zeitraum der ganzen Abwesenheit.
struct Fehlzeiteintrag: Decodable, Sendable {
    var id: Int
    var schuelerId: Int
    var art: String
    var minuten: Int
    var status: String
    var bemerkung: String
    var grund: String
    var stunden: [Int]
    var beginn: String
    var ende: String
    var vonDatum: String
    var vonZeit: String
    var bisDatum: String
    var bisZeit: String
    var text: String

    enum CodingKeys: String, CodingKey {
        case id, schuelerId, art, minuten, status, bemerkung, grund, stunden, beginn, ende, vonDatum, vonZeit, bisDatum, bisZeit, text
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        schuelerId = c.zahl(.schuelerId) ?? 0
        art = c.textOderNil(.art) ?? "fehlt"
        minuten = c.zahl(.minuten) ?? 0
        status = c.text(.status)
        bemerkung = c.text(.bemerkung)
        grund = c.text(.grund)
        stunden = c.liste(.stunden)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        vonDatum = String(c.text(.vonDatum).prefix(10))
        vonZeit = c.text(.vonZeit)
        bisDatum = String(c.text(.bisDatum).prefix(10))
        bisZeit = c.text(.bisZeit)
        text = c.text(.text)
    }

    /// „Abwesend (verspätet)“, „Abwesend (fehlt)“ bzw. „Abwesend (vorzeitig)“ wie in der Vorlage.
    var statustext: String {
        switch art {
        case "verspaetet": return "Abwesend (verspätet)"
        case "vorzeitig": return "Abwesend (vorzeitig)"
        default: return "Abwesend (fehlt)"
        }
    }
}

/// Fehlzeit einer früheren Stunde desselben Tages („War in Vorstunde abwesend!“).
struct Vorstundenfehlzeit: Decodable, Sendable {
    var schuelerId: Int
    var stunde: Int
    var art: String

    enum CodingKeys: String, CodingKey { case schuelerId, stunde, art }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schuelerId = c.zahl(.schuelerId) ?? 0
        stunde = c.zahl(.stunde) ?? 0
        art = c.text(.art)
    }
}

/// GET /api/app/klassenbuch/anwesenheit (nur Kollegium)
struct Anwesenheitsliste: Decodable, Sendable {
    var schueler: [Schueler]
    var fehlzeiten: [Fehlzeiteintrag]
    var geprueft: String?
    var darf: Bool
    /// Fehlzeiten früherer Stunden desselben Tages, die in dieser Stunde (noch) nicht eingetragen sind
    var vorstunden: [Vorstundenfehlzeit]
    /// Klassenleitung: Zeiträume über den Tag hinaus und Status im Abwesenheitsdialog
    var verwalten: Bool
    /// Beginn und Ende des Blocks (HH:mm)
    var beginn: String
    var ende: String

    enum CodingKeys: String, CodingKey { case schueler, fehlzeiten, geprueft, darf, vorstunden, verwalten, beginn, ende }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schueler = c.liste(.schueler)
        fehlzeiten = c.liste(.fehlzeiten)
        geprueft = c.textOderNil(.geprueft)
        darf = c.wahr(.darf)
        vorstunden = c.liste(.vorstunden)
        verwalten = c.wahr(.verwalten)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
    }

    /// Wurden die Abwesenheiten der Stunde bereits kontrolliert?
    var kontrolliert: Bool { !(geprueft ?? "").isEmpty }
}

/// Eintrag beim Speichern der Anwesenheit (nur Abweichungen).
struct AnwesenheitEintragDaten: Encodable, Sendable {
    var schuelerId: Int
    var art: String
    var minuten: Int
    var bemerkung: String?
    var grund: String?
    /// Minutengenau abwesend von–bis (HH:mm) im Block
    var von: String? = nil
    var bis: String? = nil
}

/// POST /api/app/klassenbuch/abwesenheit – Abwesenheitsdialog (minutengenau von–bis, „yyyy-MM-ddTHH:mm:ss“).
struct AbwesenheitZeitraumDaten: Encodable, Sendable {
    var schuelerIds: [Int]
    var von: String
    var bis: String
    var grund: String?
    var text: String?
    var status: String? = nil
    var datum: String?
    var stunde: Int?
    var unterricht: Int?
    var ersetzen: [Int]? = nil
    var elternInformiert: Bool? = nil
}

/// POST /api/app/klassenbuch/abwesenheit/loeschen – Abwesenheit in dieser Stunde entfernen (anwesend).
struct AbwesenheitEntfernenDaten: Encodable, Sendable {
    var datum: String
    var stunde: Int
    var unterricht: Int
    var schuelerIds: [Int]
}

/// POST /api/app/abwesenheiten/loeschen
struct IdsDaten: Encodable, Sendable {
    var ids: [Int]
}

/// POST /api/app/klassenbuch/vermerke – neuer Klassenbucheintrag (zur Klasse oder zu einer Schülerin bzw. einem Schüler).
struct VermerkNeuDaten: Encodable, Sendable {
    var datum: String
    var klasse: String
    var schuelerId: Int?
    var art: String
    var text: String
    var zeit: String?
}

/// Abwesenheitsgrund (GET /api/app/abwesenheitsgruende), gespeichert wird das Kürzel.
struct Abwesenheitsgrund: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var kurz: String
    var name: String
    var art: String
    var entschuldigt: Bool
    var aktiv: Bool

    enum CodingKeys: String, CodingKey { case id, kurz, name, art, entschuldigt, aktiv }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        kurz = c.text(.kurz)
        name = c.text(.name)
        art = c.text(.art)
        entschuldigt = c.wahr(.entschuldigt)
        aktiv = (try? c.decodeIfPresent(Bool.self, forKey: .aktiv)) ?? true
    }

    var anzeige: String { name.isEmpty ? kurz : name }
}

/// Abwesenheit als Zeitraum (Klassenleitung: GET /api/app/abwesenheiten/zeitraeume; Familie: /api/app/familie/abwesenheiten).
struct Abwesenheitszeitraum: Decodable, Identifiable, Sendable {
    var schluessel: String
    var schuelerId: Int
    var schueler: String
    var klasse: String
    var vonDatum: String
    var vonZeit: String
    var bisDatum: String
    var bisZeit: String
    var art: String
    var minuten: Int
    var grund: String
    var status: String
    var text: String
    var ids: [Int]
    var verwalten: Bool
    var elternInformiert: Bool

    var id: String { schluessel }

    enum CodingKeys: String, CodingKey {
        case schluessel, schuelerId, schueler, klasse, vonDatum, vonZeit, bisDatum, bisZeit, art, minuten, grund, status, text, ids, verwalten, elternInformiert
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schuelerId = c.zahl(.schuelerId) ?? 0
        vonDatum = String(c.text(.vonDatum).prefix(10))
        vonZeit = c.text(.vonZeit)
        schluessel = c.textOderNil(.schluessel) ?? "\(schuelerId):\(vonDatum):\(vonZeit)"
        schueler = c.text(.schueler)
        klasse = c.text(.klasse)
        bisDatum = String(c.text(.bisDatum).prefix(10))
        bisZeit = c.text(.bisZeit)
        art = c.text(.art)
        minuten = c.zahl(.minuten) ?? 0
        grund = c.text(.grund)
        status = c.textOderNil(.status) ?? "offen"
        text = c.text(.text)
        ids = c.liste(.ids)
        verwalten = c.wahr(.verwalten)
        elternInformiert = c.wahr(.elternInformiert)
    }
}

/// Kind eines Eltern- bzw. Schülerkontos (GET /api/app/familie/kinder).
struct Kind: Decodable, Identifiable, Hashable, Sendable {
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

/// GET /api/app/familie/abwesenheiten
struct FamilieAbwesenheiten: Decodable, Sendable {
    var von: String
    var bis: String
    var zeilen: [Abwesenheitszeitraum]

    enum CodingKeys: String, CodingKey { case von, bis, zeilen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        von = String(c.text(.von).prefix(10))
        bis = String(c.text(.bis).prefix(10))
        zeilen = c.liste(.zeilen)
    }
}

/// Freier buchbarer Raum (GET /api/app/meldungen/freieraeume).
struct FreierRaum: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String
    var raumart: String
    var kapazitaet: Int?
    var urspruenglich: Bool

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, raumart, kapazitaet, urspruenglich }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        raumart = c.text(.raumart)
        kapazitaet = c.zahl(.kapazitaet)
        urspruenglich = c.wahr(.urspruenglich)
    }
}

/// POST /api/app/mobil/raum – Raum einer eigenen Stunde ersetzen, hinzufügen oder löschen.
struct RaumAenderungDaten: Encodable, Sendable {
    var datum: String
    var stundeVon: Int
    var stundeBis: Int
    var unterricht: Int?
    var klassen: [String]
    /// ersetzen, hinzufuegen oder loeschen
    var aktion: String
    var raum: String
    var alt: String?
    var titel: String = "Raumbuchung"
}

/// Antwort auf eine Raumänderung: neue Raumliste der Stunde.
struct RaumAenderungAntwort: Decodable, Sendable {
    var raeume: [String]
    var zurueckgesetzt: Bool

    enum CodingKeys: String, CodingKey { case raeume, zurueckgesetzt }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        raeume = c.texte(.raeume)
        zurueckgesetzt = c.wahr(.zurueckgesetzt)
    }
}

/// PUT /api/app/klassenbuch/anwesenheit
struct AnwesenheitDaten: Encodable, Sendable {
    var datum: String
    var stunde: Int
    var unterricht: Int
    var eintraege: [AnwesenheitEintragDaten]
}

/// PUT /api/app/klassenbuch/stunde – Lehrstoff und Notizen einer Stunde.
struct KlassenbuchDaten: Encodable, Sendable {
    var datum: String
    var stunde: Int
    var unterricht: Int
    var thema: String
    var hausaufgabe: String
    var faellig: String?
    var notiz: String
    var notizSchueler: String?
    var lehrform: String?
    var anmerkung: String?
    var lehrer: String?
    var online: Bool? = nil
}

/// POST /api/app/klassenbuch/hausaufgabenliste
struct HausaufgabeNeuDaten: Encodable, Sendable {
    var datum: String
    var stunde: Int
    var unterricht: Int
    var text: String
    var faellig: String?
    var bemerkung: String
}

/// PUT /api/app/klassenbuch/hausaufgabenliste/{id} (nil = unverändert)
struct HausaufgabeAenderDaten: Encodable, Sendable {
    var text: String?
    var faellig: String?
    var bemerkung: String?
    var erledigt: Bool?
}

/// Klassenbucheintrag (Vermerk) zur Klasse oder zu einzelnen Schüler*innen.
struct Vermerk: Decodable, Identifiable, Sendable {
    var id: Int
    var datum: String
    var klasse: String
    var schuelerId: Int?
    var art: String
    var text: String
    var lehrer: String
    var zeit: String

    enum CodingKeys: String, CodingKey { case id, datum, klasse, schuelerId, art, text, lehrer, zeit }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? Int.random(in: 1_000_000...9_999_999)
        datum = c.text(.datum)
        klasse = c.text(.klasse)
        schuelerId = c.zahl(.schuelerId)
        art = c.text(.art)
        text = c.text(.text)
        lehrer = c.text(.lehrer)
        zeit = c.text(.zeit)
    }
}

/// GET /api/app/klassenbuch/vermerke (nur Kollegium)
struct Vermerkliste: Decodable, Sendable {
    var vermerke: [Vermerk]
    /// Eintragskategorien (z. B. Vermerk, Ordnungsmaßnahme, Lob)
    var arten: [String]

    enum CodingKeys: String, CodingKey { case vermerke, arten }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        vermerke = c.liste(.vermerke)
        arten = c.texte(.arten)
    }
}

/// Empfänger einer neuen Mitteilung (id z. B. „benutzer:12“, „klasse:5a“, „gruppe:kollegium“).
struct Empfaenger: Decodable, Identifiable, Hashable, Sendable {
    var id: String
    var name: String
    var art: String

    enum CodingKeys: String, CodingKey { case id, name, art }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.text(.id)
        name = c.text(.name)
        art = c.text(.art)
    }
}

/// POST /api/app/mitteilungen
struct MitteilungNeuDaten: Encodable, Sendable {
    var empfaenger: [String]
    var betreff: String
    var text: String
    var wichtig: Bool
}

/// Zuordnung eines Klassendienstes (z. B. Klassensprecher*in) zu einer Schülerin bzw. einem Schüler.
struct Klassendienstzuordnung: Decodable, Sendable {
    var schuelerId: Int
    var dienst: String

    enum CodingKeys: String, CodingKey { case schuelerId, dienst }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schuelerId = c.zahl(.schuelerId) ?? 0
        dienst = c.text(.dienst)
    }
}

/// GET /api/app/klassendienste (nur Kollegium)
struct Klassendienstliste: Decodable, Sendable {
    var zuordnungen: [Klassendienstzuordnung]

    enum CodingKeys: String, CodingKey { case zuordnungen }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        zuordnungen = c.liste(.zuordnungen)
    }
}

// MARK: - Allgemeine Antworten

struct OkAntwort: Decodable, Sendable {
    var ok: Bool

    enum CodingKeys: String, CodingKey { case ok }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        ok = c.wahr(.ok)
    }
}

/// Antwort, deren Inhalt nicht gebraucht wird.
struct BeliebigeAntwort: Decodable, Sendable {
    init(from decoder: Decoder) throws {}
}

struct Fehlerantwort: Decodable, Sendable {
    var fehler: String?

    enum CodingKeys: String, CodingKey { case fehler }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        fehler = c.textOderNil(.fehler)
    }
}
