import Foundation

// Modelle des Sprechtags (Elternsprechtag) – /api/app/sprechtag/…, alle Felder nachsichtig gelesen.
// GET /sprechtag/{id} liefert je Rolle andere Daten (Erziehungsberechtigte: Kinder, Einladungen, Anfragen und Termine;
// Lehrkraft bzw. Administration: Lehrkräfte mit Räumen, Terminen und Abwesenheiten). Schüler*innen haben keinen Zugriff.

/// Status eines Sprechtags (Feld `status`); fehlt es, aus der Phase abgeleitet.
/// angekuendigt (Verwaltung LK, Anmeldung beginnt erst später) · anmeldung · geschlossen
enum Sprechtagstatus {
    /// Status nachsichtig lesen: Groß-/Kleinschreibung, Umlaute („angekündigt“) und ältere Server (nur `phase`).
    static func lesen(_ roh: String, phase: String) -> String {
        let s = roh.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            .replacingOccurrences(of: "ü", with: "ue")
        if s.hasPrefix("angek") { return "angekuendigt" }
        if !s.isEmpty { return s }
        switch phase {
        case "einladung", "verwaltung": return "angekuendigt"
        case "phase1", "phase1_beendet", "phase2": return "anmeldung"
        default: return phase
        }
    }
}

/// Hinweiskarte der Startseite (GET /api/app/sprechtag/start bzw. Feld `sprechtage` in /api/app/heute).
struct SprechtagKarte: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var name: String
    var datum: String
    var datumText: String
    var beginn: String
    var ende: String
    var ort: String
    /// entwurf, verwaltung, phase1, phase1_beendet, phase2, geschlossen, durchfuehrung …
    var phase: String
    /// Phase in Worten (z. B. „2. Anmeldephase“); leer bei älteren Servern
    var phaseText: String
    /// angekuendigt, anmeldung, geschlossen (siehe `Sprechtagstatus`)
    var status: String
    var text: String
    var anmeldungVonText: String
    var anmeldungBisText: String
    var buchbar: Bool
    var online: Bool
    var termine: Int
    var einladungen: Int
    /// Kompakte Karte (neuere Server): eltern, lehrer bzw. admin
    var rolle: String
    /// „Fr 04.12.2026“ und „14:00–19:00“
    var tagText: String
    var zeitText: String
    /// Gebuchte Termine, freie Zeitblöcke (nur Lehrkraft) und offene Einladungen bzw. Anfragen (Erziehungsberechtigte)
    var gebucht: Int
    var frei: Int?
    var offen: Int
    /// Der Sprechtag findet heute statt.
    var heute: Bool
    /// Eltern: gebuchte Termine; Lehrkraft: die nächsten Termine
    var eintraege: [SprechtagStartTermin]
    var weitere: Int
    /// Beschriftung des Knopfs („Termine wählen“, „Termine verwalten“ …)
    var knopf: String

    enum CodingKeys: String, CodingKey {
        case id, name, datum, datumText, beginn, ende, ort, phase, phaseText, status, text, termine, einladungen
        case anmeldungVon, anmeldungVonText, anmeldungBis, anmeldungBisText, buchbar, online
        case rolle, tagText, zeitText, gebucht, frei, offen, heute, eintraege, weitere, knopf
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        name = c.text(.name)
        datum = String(c.text(.datum).prefix(10))
        datumText = c.text(.datumText)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        ort = c.text(.ort)
        phase = c.text(.phase).lowercased()
        phaseText = c.text(.phaseText)
        status = Sprechtagstatus.lesen(c.text(.status), phase: phase)
        text = c.text(.text)
        anmeldungVonText = c.textOderNil(.anmeldungVonText) ?? Datumsformat.anzeige(c.text(.anmeldungVon), "dd.MM.yyyy HH:mm")
        anmeldungBisText = c.textOderNil(.anmeldungBisText) ?? Datumsformat.anzeige(c.text(.anmeldungBis), "dd.MM.yyyy HH:mm")
        buchbar = c.wahr(.buchbar) || status == "anmeldung"
        online = c.wahr(.online)
        termine = c.zahl(.termine) ?? 0
        einladungen = c.zahl(.einladungen) ?? 0
        rolle = c.text(.rolle)
        tagText = c.text(.tagText)
        zeitText = c.text(.zeitText)
        gebucht = c.zahl(.gebucht) ?? termine
        frei = c.zahl(.frei)
        offen = c.zahl(.offen) ?? 0
        heute = c.wahr(.heute)
        eintraege = c.liste(.eintraege)
        weitere = c.zahl(.weitere) ?? 0
        knopf = c.text(.knopf)
    }

    /// Zeile unter dem Titel: „Fr 04.12.2026 · 14:00–19:00 Uhr“.
    var tagUndZeit: String {
        let tag = !tagText.isEmpty ? tagText : (!datumText.isEmpty ? datumText : Datumsformat.anzeige(datum, "EE dd.MM.yyyy"))
        let zeit = !zeitText.isEmpty ? zeitText : (beginn.isEmpty ? "" : (ende.isEmpty ? beginn : "\(beginn)–\(ende)"))
        return [tag, zeit.isEmpty ? "" : "\(zeit) Uhr", online ? "online" : ""].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    /// Statuszeile ohne Wiederholung der Phase; heute mit Liste: der nächste Termin steht markiert in der Liste.
    var statuszeile: String {
        if heute, let erster = eintraege.first, erster.id > 0, text.contains("nächster") {
            return "Heute · \(gebucht) \(gebucht == 1 ? "Termin" : "Termine")"
        }
        if !text.isEmpty { return text }
        return angekuendigt && !anmeldungVonText.isEmpty ? "Anmeldung ab \(anmeldungVonText)" : phaseText
    }

    /// Beschriftung des Knopfs (ältere Server liefern keine).
    var knopfText: String {
        if !knopf.isEmpty { return knopf }
        return rolle == "eltern" || buchbar ? (buchbar ? "Termine wählen" : "Termine ansehen") : "Termine verwalten"
    }

    /// Titel der Zeile auf der Startseite.
    var titel: String { name.isEmpty ? "Sprechtag" : name }

    /// Angekündigt: freigegeben, eine Buchung ist aber noch nicht möglich.
    var angekuendigt: Bool { status == "angekuendigt" }

    /// Untertitel auf der Startseite. Angekündigt: der Text des Servers (enthält Datum, Zeit, Ort und Anmeldebeginn),
    /// sonst Datum und Text.
    var untertitel: String {
        let tag = datumText.isEmpty ? Datumsformat.anzeige(datum, "EE d.M.yyyy") : datumText
        if angekuendigt {
            if !text.isEmpty { return text }
            let zeit = beginn.isEmpty ? "" : (ende.isEmpty ? beginn : "\(beginn)–\(ende) Uhr")
            var teile = [(phaseText.isEmpty ? "Angekündigt" : phaseText) + ": " + [tag, zeit].filter { !$0.isEmpty }.joined(separator: ", ")]
            if !ort.isEmpty { teile.append(ort) }
            if !anmeldungVonText.isEmpty { teile.append("Anmeldung ab \(anmeldungVonText)") }
            return teile.joined(separator: " · ")
        }
        return [tag, text].filter { !$0.isEmpty }.joined(separator: " · ")
    }
}

/// Termin auf der Startkarte (Eltern: Lehrkraft und Raum; Lehrkraft: Schüler*in und Klasse).
struct SprechtagStartTermin: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var datum: String
    var tagText: String
    var beginn: String
    var ende: String
    var status: String
    var lehrerName: String
    var raum: String
    var schueler: String
    var vorname: String
    var klasse: String

    enum CodingKeys: String, CodingKey {
        case id, datum, tagText, beginn, ende, status, lehrerName, raum, schueler, vorname, klasse
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        datum = String(c.text(.datum).prefix(10))
        tagText = c.text(.tagText)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        status = c.text(.status)
        lehrerName = c.text(.lehrerName)
        raum = c.text(.raum)
        schueler = c.text(.schueler)
        vorname = c.text(.vorname)
        klasse = c.text(.klasse)
    }
}

// MARK: - Sprechtag (Kopfdaten)

/// Abschnitt eines Sprechtags (z. B. Nachmittag) mit Datum und Zeitraum.
struct SprechtagAbschnitt: Decodable, Identifiable, Hashable, Sendable {
    var index: Int
    var name: String
    /// yyyy-MM-dd
    var datum: String
    /// dd.MM.yyyy
    var datumText: String
    var wochentag: String
    var von: String
    var bis: String

    var id: Int { index }

    enum CodingKeys: String, CodingKey { case index, name, datum, datumText, wochentag, von, bis }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        index = c.zahl(.index) ?? 0
        name = c.text(.name)
        datum = String(c.text(.datum).prefix(10))
        datumText = c.text(.datumText)
        wochentag = c.text(.wochentag)
        von = c.text(.von)
        bis = c.text(.bis)
    }

    /// Anzeige des Abschnitts in Auswahllisten: „Nachmittag – Freitag, 20.11.2026, 14:00–18:00 Uhr“.
    var langText: String {
        let tag = [wochentag, datumText].filter { !$0.isEmpty }.joined(separator: ", ")
        let zeit = von.isEmpty ? "" : "\(von)–\(bis) Uhr"
        let rest = [tag, zeit].filter { !$0.isEmpty }.joined(separator: ", ")
        return [name, rest].filter { !$0.isEmpty }.joined(separator: " – ")
    }
}

/// Konfiguration eines Sprechtags (nur die für die App nötigen Teile).
struct SprechtagKonfig: Decodable, Hashable, Sendable {
    var online: Bool
    /// Dauer eines Zeitblocks in Minuten
    var dauer: Int

    enum CodingKeys: String, CodingKey { case online, dauer }

    init(online: Bool = false, dauer: Int = 10) {
        self.online = online
        self.dauer = dauer
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        online = c.wahr(.online)
        let d = c.zahl(.dauer) ?? 10
        dauer = d > 0 ? d : 10
    }
}

/// Kopfdaten eines Sprechtags (Feld `sprechtag` in GET /sprechtag/{id}).
struct SprechtagDaten: Decodable, Hashable, Sendable {
    var id: Int
    var name: String
    var phase: String
    var phaseText: String
    var datumText: String
    var konfig: SprechtagKonfig
    var abschnitte: [SprechtagAbschnitt]

    enum CodingKeys: String, CodingKey { case id, name, phase, phaseText, datumText, konfig, abschnitte }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        name = c.text(.name)
        phase = c.text(.phase).lowercased()
        phaseText = c.text(.phaseText)
        datumText = c.text(.datumText)
        konfig = c.optional(.konfig) ?? SprechtagKonfig()
        abschnitte = c.liste(.abschnitte)
    }

    var titel: String { name.isEmpty ? "Sprechtag" : name }
    var online: Bool { konfig.online }
    var dauer: Int { konfig.dauer }

    /// Änderungen möglich (nicht abgeschlossen, nicht im Entwurf).
    var laufend: Bool { phase != "abgeschlossen" && phase != "entwurf" }

    func abschnitt(_ index: Int) -> SprechtagAbschnitt? {
        abschnitte.first { $0.index == index }
    }
}

// MARK: - Termine

/// Schüler*in eines Termins bzw. einer Einladung.
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

    /// „Name Vorname“ wie in der Weboberfläche.
    var anzeige: String { [name, vorname].filter { !$0.isEmpty }.joined(separator: " ") }
}

/// Termin, Einladung bzw. Terminanfrage am Sprechtag.
/// status: einladung (ohne Uhrzeit), angefragt, bestaetigt, abgelehnt, abgesagt
struct SprechtagTermin: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var lehrer: String
    var lehrerName: String
    /// „(KÜRZEL) Name“
    var lehrerTitel: String
    /// Abschnitt (-1 = ohne Uhrzeit, z. B. Einladung)
    var abschnitt: Int
    var abschnittName: String
    var datum: String
    var datumText: String
    var wochentag: String
    var beginn: String
    var ende: String
    var schueler: SprechtagSchueler?
    var art: String
    var status: String
    var statusText: String
    var verpflichtend: Bool
    var link: String
    var anmerkung: String
    var grund: String
    var raum: String
    var online: Bool

    enum CodingKeys: String, CodingKey {
        case id, lehrer, lehrerName, lehrerTitel, abschnitt, abschnittName, datum, datumText, wochentag, beginn, ende
        case schueler, art, status, statusText, verpflichtend, link, anmerkung, grund, raum, online
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        lehrer = c.text(.lehrer)
        lehrerName = c.text(.lehrerName)
        let titel = c.text(.lehrerTitel)
        lehrerTitel = titel.isEmpty ? (lehrerName.isEmpty ? lehrer : "(\(lehrer)) \(lehrerName)") : titel
        abschnitt = c.zahl(.abschnitt) ?? -1
        abschnittName = c.text(.abschnittName)
        datum = String(c.text(.datum).prefix(10))
        datumText = c.text(.datumText)
        wochentag = c.text(.wochentag)
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        schueler = c.optional(.schueler)
        art = c.text(.art)
        status = c.text(.status).lowercased()
        statusText = c.text(.statusText)
        verpflichtend = c.wahr(.verpflichtend)
        link = Karte.linkText(c.textOderNil(.link))
        anmerkung = c.text(.anmerkung)
        grund = c.text(.grund)
        raum = c.text(.raum)
        online = c.wahr(.online)
    }

    var hatZeit: Bool { !beginn.isEmpty && abschnitt >= 0 }
    var istEinladung: Bool { status == "einladung" }
    var bestaetigt: Bool { status == "bestaetigt" }

    /// „14:00 – 14:10 Uhr“
    var zeitText: String {
        guard !beginn.isEmpty else { return "" }
        return ende.isEmpty ? "\(beginn) Uhr" : "\(beginn) – \(ende) Uhr"
    }

    var schuelerName: String { schueler?.anzeige ?? "" }

    /// Statustext des Servers, sonst aus dem Status abgeleitet.
    var statusAnzeige: String {
        if !statusText.isEmpty { return statusText }
        switch status {
        case "einladung": return "Eingeladen"
        case "angefragt": return "Versendet"
        case "bestaetigt": return "Akzeptiert"
        case "abgelehnt": return "Abgelehnt"
        default: return "Abgesagt"
        }
    }
}

/// Freie Zeit einer Lehrkraft (GET /sprechtag/{id}/frei).
struct SprechtagZeit: Decodable, Identifiable, Hashable, Sendable {
    var von: String
    var bis: String

    var id: String { von }

    enum CodingKeys: String, CodingKey { case von, bis }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        von = c.text(.von)
        bis = c.text(.bis)
    }
}

// MARK: - Erziehungsberechtigte

/// Lehrkraft eines Kindes (Eltern-Sicht).
struct SprechtagKindLehrkraft: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String
    var faecher: [String]
    var klassenleitung: Bool

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, faecher, klassenleitung }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        // Fächer als Liste oder als Text
        let liste = c.texte(.faecher)
        faecher = liste.isEmpty ? c.text(.faecher).split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty } : liste
        klassenleitung = c.wahr(.klassenleitung)
    }

    /// „(KÜRZEL) Name“
    var titel: String { name.isEmpty ? kurz : "(\(kurz)) \(name)" }

    /// „Klassenleitung, Mathematik, Physik“
    var unterzeile: String { ([klassenleitung ? "Klassenleitung" : ""] + faecher).filter { !$0.isEmpty }.joined(separator: ", ") }
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

    /// „Name Vorname“ wie in der Weboberfläche.
    var anzeige: String { [name, vorname].filter { !$0.isEmpty }.joined(separator: " ") }
}

// MARK: - Lehrkräfte und Administration

/// Rechte der Lehrkräfte am Sprechtag (Administration: alle).
struct SprechtagRechte: Decodable, Hashable, Sendable {
    var raeume: Bool
    var abwesenheiten: Bool
    var bearbeiten: Bool
    var buchen: Bool
    var einladen: Bool

    enum CodingKeys: String, CodingKey { case raeume, abwesenheiten, bearbeiten, buchen, einladen }

    init() {
        raeume = false
        abwesenheiten = false
        bearbeiten = false
        buchen = false
        einladen = false
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        raeume = c.wahr(.raeume)
        abwesenheiten = c.wahr(.abwesenheiten)
        bearbeiten = c.wahr(.bearbeiten)
        buchen = c.wahr(.buchen)
        // Einladen ist für alle Lehrkräfte freigegeben (fehlt das Feld, gilt es als erlaubt)
        einladen = c.contains(.einladen) ? c.wahr(.einladen) : true
    }
}

/// Abwesenheit (gesperrter Zeitraum) einer Lehrkraft in einem Abschnitt.
struct SprechtagAbwesenheit: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var abschnitt: Int
    var beginn: String
    var ende: String
    var anmerkung: String

    enum CodingKeys: String, CodingKey { case id, abschnitt, beginn, ende, anmerkung }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        abschnitt = c.zahl(.abschnitt) ?? 0
        beginn = c.text(.beginn)
        ende = c.text(.ende)
        anmerkung = c.text(.anmerkung)
    }
}

/// Lehrkraft mit Räumen (je Abschnitt), Terminen, Einladungen und Abwesenheiten.
struct SprechtagLehrkraft: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String
    /// „(KÜRZEL) Name“
    var titel: String
    /// Raum je Abschnitt (Index wie `abschnitte`)
    var raeume: [String]
    var keinRaum: Bool
    var keineTermine: Bool
    var termine: [SprechtagTermin]
    var abwesenheiten: [SprechtagAbwesenheit]

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, titel, raeume, keinRaum, keineTermine, termine, abwesenheiten }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        let t = c.text(.titel)
        titel = t.isEmpty ? (name.isEmpty ? kurz : "(\(kurz)) \(name)") : t
        raeume = c.texte(.raeume)
        keinRaum = c.wahr(.keinRaum)
        keineTermine = c.wahr(.keineTermine)
        termine = c.liste(.termine)
        abwesenheiten = c.liste(.abwesenheiten)
    }

    func raum(_ abschnitt: Int) -> String {
        abschnitt >= 0 && abschnitt < raeume.count ? raeume[abschnitt] : ""
    }

    /// Einladungen ohne Uhrzeit (die Erziehungsberechtigten wählen sie selbst).
    var einladungen: [SprechtagTermin] { termine.filter(\.istEinladung) }
}

/// Raum zur Auswahl (Lehrkraft bzw. Administration).
struct SprechtagRaum: Decodable, Identifiable, Hashable, Sendable {
    var kurz: String
    var name: String
    var gebaeude: String

    var id: String { kurz }

    enum CodingKeys: String, CodingKey { case kurz, name, gebaeude }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kurz = c.text(.kurz)
        name = c.text(.name)
        gebaeude = c.text(.gebaeude)
    }
}

/// Schüler*in der Lehrkraft (GET /sprechtag/{id}/schueler); vorhanden = bereits eingeladen bzw. mit Termin.
struct SprechtagSchuelerWahl: Decodable, Identifiable, Hashable, Sendable {
    var id: Int
    var vorname: String
    var name: String
    var klasse: String
    var vorhanden: Bool

    enum CodingKeys: String, CodingKey { case id, vorname, name, klasse, vorhanden }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.zahl(.id) ?? 0
        vorname = c.text(.vorname)
        name = c.text(.name)
        klasse = c.text(.klasse)
        vorhanden = c.wahr(.vorhanden)
    }

    var anzeige: String { [name, vorname].filter { !$0.isEmpty }.joined(separator: " ") }
}

// MARK: - GET /sprechtag/{id}

/// Sprechtag mit den Daten je Rolle (`rolle`: eltern, lehrer, admin).
struct SprechtagAnsicht: Decodable, Sendable {
    var rolle: String
    var sprechtag: SprechtagDaten?

    // Erziehungsberechtigte
    var hinweis: String
    var freiBuchbar: Bool
    /// Zeit bestätigter Termine ändern (1. und 2. Anmeldephase)
    var aendern: Bool
    var kinder: [SprechtagKind]
    var einladungen: [SprechtagTermin]
    var anfragen: [SprechtagTermin]
    var termine: [SprechtagTermin]

    // Lehrkraft bzw. Administration
    var rechte: SprechtagRechte
    var lehrkraefte: [SprechtagLehrkraft]
    var raumListe: [SprechtagRaum]

    enum CodingKeys: String, CodingKey {
        case rolle, sprechtag, hinweis, freiBuchbar, aendern, kinder, einladungen, anfragen, termine, rechte, lehrkraefte, raumListe
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        rolle = c.text(.rolle).lowercased()
        sprechtag = c.optional(.sprechtag)
        hinweis = c.text(.hinweis)
        freiBuchbar = c.wahr(.freiBuchbar)
        aendern = c.wahr(.aendern)
        kinder = c.liste(.kinder)
        einladungen = c.liste(.einladungen)
        anfragen = c.liste(.anfragen)
        termine = c.liste(.termine)
        rechte = c.optional(.rechte) ?? SprechtagRechte()
        lehrkraefte = c.liste(.lehrkraefte)
        raumListe = c.liste(.raumListe)
    }

    var istEltern: Bool { rolle == "eltern" }
    var istAdmin: Bool { rolle == "admin" }

    func lehrkraft(_ kurz: String) -> SprechtagLehrkraft? {
        lehrkraefte.first { $0.kurz.caseInsensitiveCompare(kurz) == .orderedSame }
    }
}

/// Antwort von POST /sprechtag/{id}/einladung.
struct SprechtagAnzahlAntwort: Decodable, Sendable {
    var anzahl: Int

    enum CodingKeys: String, CodingKey { case anzahl }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        anzahl = c.zahl(.anzahl) ?? 0
    }
}

// MARK: - Anfragen

/// POST /sprechtag/{id}/buchen (Erziehungsberechtigte; mit `einladungId` aus einer Einladung)
struct SprechtagBuchDaten: Encodable, Sendable {
    var lehrer: String
    var abschnitt: Int
    var beginn: String
    var schuelerId: Int
    var einladungId: Int?
}

/// POST /sprechtag/{id}/termin/{tid}/absagen
struct SprechtagAbsageDaten: Encodable, Sendable {
    var grund: String?
}

/// PUT /sprechtag/{id}/termin/{tid} – Zeit (Erziehungsberechtigte nur diese), Anmerkung und Meeting-Link ändern.
struct SprechtagAenderDaten: Encodable, Sendable {
    var abschnitt: Int
    var beginn: String
    var ende: String?
    var anmerkung: String?
    var link: String?
}

/// PUT /sprechtag/{id}/raum
struct SprechtagRaumDaten: Encodable, Sendable {
    var lehrer: String?
    var abschnitt: Int
    var raum: String
}

/// POST /sprechtag/{id}/abwesenheit (ohne `id` neu, sonst ändern)
struct SprechtagAbwesenheitDaten: Encodable, Sendable {
    var id: Int?
    var lehrer: String?
    var abschnitt: Int
    var beginn: String
    var ende: String
    var anmerkung: String
}

/// POST /sprechtag/{id}/einladung
struct SprechtagEinladungDaten: Encodable, Sendable {
    var lehrer: String?
    var schuelerIds: [Int]
    var text: String?
    var verpflichtend: Bool
}

/// POST /sprechtag/{id}/termin – Terminanfrage mit fester Uhrzeit.
struct SprechtagTerminDaten: Encodable, Sendable {
    var lehrer: String?
    var abschnitt: Int
    var beginn: String
    var ende: String
    var schuelerId: Int
    var anmerkung: String?
    var link: String?
    var verpflichtend: Bool
}

// MARK: - Zeitrechnung

/// Uhrzeiten „HH:mm“ als Minuten und zurück.
enum Sprechtagzeit {
    static func minuten(_ hhmm: String) -> Int {
        let teile = hhmm.split(separator: ":")
        guard teile.count >= 2, let h = Int(teile[0]), let m = Int(teile[1]) else { return 0 }
        return h * 60 + m
    }

    static func text(_ minuten: Int) -> String {
        String(format: "%02d:%02d", minuten / 60, minuten % 60)
    }

    /// Zeiten im Takt der Zeitblöcke von `von` bis einschließlich `bis`.
    static func raster(von: String, bis: String, dauer: Int) -> [String] {
        let start = minuten(von)
        let ende = minuten(bis)
        guard dauer > 0, ende > start else { return von.isEmpty ? [] : [von] }
        return stride(from: start, through: ende, by: dauer).map { text($0) }
    }
}
