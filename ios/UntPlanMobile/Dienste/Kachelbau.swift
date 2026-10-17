import SwiftUI

// Übersetzt den Wochenplan (Karten, Tagesköpfe, Aufsichten) in Kacheln für das Raster – nach derselben Logik wie
// UntPlanMobile (Dienste/Kachelbau.cs und app.js): Art der Kachel, Zeilen mit Plaketten, Durchstreichungen,
// abgegebene Stunden (Eckklammern), Bereitschaften, Veranstaltungen als Feld, Pausenaufsichten als Pillen,
// vergangene Stunden und die Anordnung paralleler Stunden (Spuren, Zählstreifen).

// MARK: - Zeitraster

/// Stunde des Zeitrasters in Minuten seit Mitternacht.
struct Rasterzeit: Identifiable {
    let nr: Int
    let beginn: Int
    let ende: Int

    var id: Int { nr }

    static func minuten(_ text: String) -> Int? {
        let teile = text.split(separator: ":")
        guard teile.count >= 2, let h = Int(teile[0]), let m = Int(teile[1].prefix(2)) else { return nil }
        return h * 60 + m
    }

    static func uhrzeit(_ minuten: Int) -> String {
        String(format: "%02d:%02d", minuten / 60, minuten % 60)
    }

    /// Zeitraster aus dem Plan; ohne Raster 45-Minuten-Stunden ab 8 Uhr.
    static func aus(_ plan: Wochenplan) -> [Rasterzeit] {
        let aus = plan.zeitraster.compactMap { z -> Rasterzeit? in
            guard let b = minuten(z.beginn), let e = minuten(z.ende), e > b else { return nil }
            return Rasterzeit(nr: z.nr, beginn: b, ende: e)
        }
        .sorted { $0.beginn < $1.beginn }
        if !aus.isEmpty { return aus }
        let anzahl = max(plan.karten.map(\.bis).max() ?? 8, 8)
        return (1...anzahl).map { nr in
            let b = 480 + (nr - 1) * 50
            return Rasterzeit(nr: nr, beginn: b, ende: b + 45)
        }
    }
}

// MARK: - Kachel

/// Art einer Kachel (entspricht blockType der PWA).
enum Kachelart: Equatable {
    /// Unterricht wie geplant
    case normal
    /// Vertretung bzw. Änderung (grün)
    case aenderung
    /// Entfall (rosa, roter Rahmen, durchgestrichen)
    case entfall
    /// Klausur bzw. Prüfung (gelb)
    case klausur
    /// Raumbuchung bzw. Termin (hellgrün, dünner Rahmen)
    case termin
    /// Stunde läuft ohne die betrachtete Lehrkraft (durchsichtig, Eckklammern, Kürzel durchgestrichen)
    case abgegeben
    /// Bereitschaft (gestrichelter Rahmen)
    case bereitschaft
    /// Bereitschaft der abwesenden Lehrkraft (durchsichtig, Eckklammern, Kürzel durchgestrichen)
    case bereitschaftAbwesend
    /// Veranstaltung (hellgrünes Feld über den ganzen Zeitraum)
    case veranstaltung
    /// Ganztägiger Termin als Pille über der ersten Stunde
    case ganztagTermin
    /// Pausenaufsicht als Pille in der Pause
    case aufsicht
}

enum Aufsichtsstand {
    case regulaer
    /// eingesprungen (grün)
    case vertretung
    /// abgegeben (durchsichtig, Eckklammern)
    case abgegeben
}

/// Teil einer Kartenzeile: Text, grüne Plakette oder durchgestrichener (wegfallender) Wert.
struct Kachelteil: Hashable {
    enum Stil: Hashable {
        case text
        case plakette
        case weg
        /// Offene Vertretung: abwesende Lehrkraft durchgestrichen in grüner Plakette
        case offen
    }

    var text: String
    var stil: Stil = .text
    /// Trennzeichen nach dem Teil (bei Listen)
    var trenner: String = ""
}

/// Zeile einer Kachel (Klasse, Lehrkraft, Fach, Raum, Anlass).
struct Kachelzeile: Hashable {
    var schluessel: String
    var teile: [Kachelteil]
    var fett: Bool = false

    var text: String { teile.map { $0.text + $0.trenner }.joined() }
    var nurText: Bool { teile.allSatisfy { $0.stil == .text } }
}

/// Marker oben rechts auf der Karte (Feld `marker` der Karte, Reihenfolge vom Server).
enum Kachelmarker: String, Hashable {
    case verlegungVon
    case verlegungNach
    case entfall
    case pruefung
    case plus
    case aenderung

    /// Nachsichtig lesen (Groß-/Kleinschreibung, Umlaute, Bindestriche); Unbekanntes entfällt.
    static func lesen(_ roh: String) -> Kachelmarker? {
        let t = roh.lowercased().replacingOccurrences(of: "-", with: "").replacingOccurrences(of: "_", with: "")
            .replacingOccurrences(of: "ü", with: "ue").replacingOccurrences(of: "ä", with: "ae")
        switch t {
        case "verlegungvon", "von": return .verlegungVon
        case "verlegungnach", "nach": return .verlegungNach
        case "entfall": return .entfall
        case "pruefung", "klausur": return .pruefung
        case "plus", "zusatz": return .plus
        case "aenderung", "geaendert": return .aenderung
        default: return nil
        }
    }
}

/// Eine Kachel im Raster.
struct Kachel: Identifiable {
    var id = UUID()
    var karte: Karte?
    var aufsicht: Aufsicht?
    var art: Kachelart
    var datum: String
    var von: Int
    var bis: Int
    /// Beginn und Ende in Minuten (Aufsicht, Termin-Pille: nach Uhrzeit, sonst aus dem Raster)
    var beginn: Int
    var ende: Int
    /// Nach Uhrzeit gesetzt (Aufsicht, Termin-Pille)
    var inPause: Bool = false
    var vorErsterStunde: Bool = false
    /// Feld (Veranstaltung) – liegt vor den verdrängten Stunden
    var feld: Bool = false
    var vergangen: Bool = false
    var zeilen: [Kachelzeile] = []
    /// Balkenfarbe (Fachfarbe), nil = Grau
    var balkenHex: String = ""
    var info: Bool = false
    var hausaufgabe: Bool = false
    var aufsichtsstand: Aufsichtsstand = .regulaer
    /// Fach, Anlass bzw. Aufsichtsort
    var titel: String = ""
    var rang: Int = 1
    /// Zusatzstunde (Plakette in den Details)
    var zusatz: Bool = false
    /// Nur der Raum hat sich geändert (grüne Karte wie eine Änderung, neuer Raum als grüne Plakette – nie gelb wie eine Prüfung)
    var raumAenderung: Bool = false
    /// Stunde findet online statt: Online-Zeichen unten rechts
    var online: Bool = false
    /// Verlegung: „von“ (ursprüngliche Stunde, rotes Feld) bzw. „nach“ (Zielstunde, grünes Feld), Pfeil nach rechts
    var verlegung: String? = nil
    /// Marker oben rechts (untereinander) laut Server; nil = älterer Server ohne Marker (Verlegung dann unten rechts)
    var marker: [Kachelmarker]? = nil

    /// Zeigt Text beim Antippen (alles außer der Ganztags-Pille).
    var antippbar: Bool { art != .ganztagTermin }

    var dauer: Int { max(1, bis - von + 1) }

    /// Überlappen sich die Zeiträume (Minuten)? Aneinanderstoßende Blöcke (Ende = Beginn) überlappen nicht.
    func ueberlappt(_ o: Kachel) -> Bool {
        beginn < o.ende && o.beginn < ende
    }
}

/// Lage einer Kachel in der Tagesspalte (Anteile der Spaltenbreite).
struct Kachellage: Identifiable {
    var kachel: Kachel
    /// linker Rand und Breite als Bruchteil der Spalte, dazu Abzug in Punkten (Luft zwischen Spuren)
    var links: CGFloat = 0
    var breite: CGFloat = 1
    var luecke: CGFloat = 0
    var spur: Int = 0
    var spuren: Int = 1
    /// Breite in Spuren (dehnt sich über freie Spuren rechts daneben)
    var spanne: Int = 1
    /// Gleichzeitige Blöcke untereinander geteilt: Zahl der Blöcke und Platz von oben
    var stapel: Int = 1
    var stapelIndex: Int = 0
    /// Platz rechts für den Zählstreifen
    var mitZaehler = false

    var id: UUID { kachel.id }
}

/// Zählstreifen rechts neben einer Kachel: so viele Stunden liegen dahinter (Wochenansicht, zu wenig Platz).
struct Zaehlstreifen: Identifiable {
    /// Feste Kennung aus vorderer Kachel und Stunden: bleibt beim erneuten Anordnen gleich (keine neuen Ansichten)
    var id: String { "\(vorne.id.uuidString)-\(von)-\(bis)" }
    var von: Int
    var bis: Int
    var kacheln: [Kachel]
    var vorne: Kachel

    var entfall: Bool { kacheln.contains { $0.art == .entfall } }
    var aenderung: Bool { kacheln.contains { $0.art == .aenderung } }
}

/// Ergebnis für eine Tagesspalte.
struct Tagesbelegung {
    var lagen: [Kachellage] = []
    var streifen: [Zaehlstreifen] = []
}

// MARK: - Kachelbau

enum Kachelbau {
    /// Kacheln eines Tages.
    static func tag(_ tag: Tageskopf, plan: Wochenplan, raster: [Rasterzeit], typ: String, element: String, jetzt: Date) -> [Kachel] {
        guard !tag.frei, let erste = raster.first, let letzte = raster.last else { return [] }
        let ich = (plan.element.isEmpty ? element : plan.element).uppercased()
        var karten = plan.karten.filter { $0.datum == tag.datum }
        // Raumplan: Stunden, die aus diesem Raum genommen wurden (Raum nur noch als alter Wert), erscheinen nicht mehr
        if typ == "raum" && !ich.isEmpty {
            karten = karten.filter { !ausRaumGenommen($0, raum: ich) }
        }
        var kacheln = karten.compactMap { ausKarte($0, typ: typ, ich: ich, raster: raster) }
        // Bereitschaft im Lehrerplan: hält die Lehrkraft darin eine Vertretung, entfällt nur diese Stunde der Bereitschaft
        if typ == "lehrer" {
            var belegt = Set<Int>()
            for k in karten where ["geaendert", "klausur", "regulaer"].contains(k.status) && !istBereitschaft(k) {
                for s in k.von...max(k.bis, k.von) { belegt.insert(s) }
            }
            if !belegt.isEmpty { kacheln = bereitschaftenKuerzen(kacheln, belegt: belegt, raster: raster) }
        }

        // Veranstaltungen, die nicht schon als Karte (Teilnahme) im Plan stehen
        for e in tag.ereignisse {
            if karten.contains(where: { $0.text == e.titel && $0.von <= max(e.bis, e.von) && $0.bis >= e.von }) { continue }
            let von = max(e.von, erste.nr)
            let bis = min(max(e.bis, von), letzte.nr)
            let ganz = e.ganztaegig || e.von <= 0 || (von <= erste.nr && bis >= letzte.nr)
            let titel = e.titel.isEmpty ? e.art : e.titel
            if !ganz {
                let k = Karte(datum: tag.datum, von: von, bis: bis,
                              beginn: raster.first(where: { $0.nr == von }).map { Rasterzeit.uhrzeit($0.beginn) } ?? "",
                              ende: raster.first(where: { $0.nr == bis }).map { Rasterzeit.uhrzeit($0.ende) } ?? "",
                              status: "veranstaltung",
                              klassen: e.klassen.map { Wert(k: $0) }, lehrer: e.lehrer.map { Wert(k: $0) },
                              raeume: e.raeume.map { Wert(k: $0) }, art: e.art.isEmpty ? "Veranstaltung" : e.art, text: titel)
                if let v = ausKarte(k, typ: typ, ich: ich, raster: raster) { kacheln.append(v) }
                continue
            }
            // Findet an dem Tag Unterricht statt, steht der Termin als Pille über der ersten Stunde – sonst als Fläche über den Tag
            let unterricht = kacheln.contains { $0.art != .entfall && $0.art != .aufsicht && !$0.feld }
            let k = Karte(datum: tag.datum, von: erste.nr, bis: letzte.nr, beginn: Rasterzeit.uhrzeit(erste.beginn),
                          ende: Rasterzeit.uhrzeit(letzte.ende), status: "termin",
                          klassen: e.klassen.map { Wert(k: $0) }, lehrer: e.lehrer.map { Wert(k: $0) },
                          raeume: e.raeume.map { Wert(k: $0) }, art: "Termin", text: titel)
            var kachel = Kachel(karte: k, art: unterricht ? .ganztagTermin : .termin, datum: tag.datum,
                                von: erste.nr, bis: unterricht ? erste.nr : letzte.nr,
                                beginn: unterricht ? erste.beginn - 15 : erste.beginn,
                                ende: unterricht ? erste.beginn : letzte.ende)
            kachel.titel = titel
            kachel.inPause = unterricht
            kachel.vorErsterStunde = unterricht
            kachel.zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)], fett: true)]
            let wer = (e.lehrer + e.klassen).filter { !$0.isEmpty }
            if !unterricht && !wer.isEmpty {
                kachel.zeilen.append(Kachelzeile(schluessel: "klasse", teile: [Kachelteil(text: wer.joined(separator: ", "))]))
            }
            kachel.rang = 2
            kacheln.append(kachel)
        }

        // Pausenaufsichten: Blöcke über ihre Pause hinweg teilen, damit die Aufsicht in einer echten Lücke steht
        let aufsichten = plan.aufsichten.filter { $0.datum == tag.datum }
        if !aufsichten.isEmpty {
            let pausen = Set(aufsichten.map(\.nachStunde).filter { $0 >= 1 })
            kacheln = anPausenTeilen(kacheln, pausen: pausen, raster: raster)
            kacheln.append(contentsOf: aufsichten.map { aufsichtKachel($0, raster: raster, ich: ich) })
        }

        // Vergangene Stunden
        for i in kacheln.indices {
            kacheln[i].vergangen = istVergangen(kacheln[i], jetzt: jetzt)
        }
        return kacheln
    }

    // MARK: Karte → Kachel

    /// Bereitschaftsstunde (ausdrücklich, Status oder Fach „BER“ …).
    static func istBereitschaft(_ k: Karte) -> Bool {
        if k.bereitschaft || k.status == "bereitschaft" { return true }
        let f = k.fach.uppercased()
        if f == "BER" || f == "ZBER" || f.hasPrefix("BER ") || f == "BEREIT" { return true }
        if k.fachName.lowercased().contains("bereitschaft") { return true }
        return k.art.lowercased().contains("bereitschaft")
    }

    /// Bereitschaft, bei der die eingeteilte Lehrkraft abwesend ist.
    static func istBereitschaftAbwesend(_ k: Karte) -> Bool {
        guard istBereitschaft(k) else { return false }
        if k.lehrerAbwesend { return true }
        if k.status == "abwesend" || k.status == "entfall" || k.status == "offen" { return true }
        return !k.lehrer.isEmpty && k.lehrer.allSatisfy { $0.k.isEmpty && !($0.alt ?? "").isEmpty }
    }

    /// Steht der Raum nur noch als alter (weggefallener) Wert in der Stunde – die Stunde wurde aus dem Raum genommen?
    static func ausRaumGenommen(_ k: Karte, raum: String) -> Bool {
        guard !raum.isEmpty else { return false }
        if k.raeume.contains(where: { gleich($0.k, raum) }) { return false }
        return k.raeume.contains { gleich($0.alt ?? "", raum) }
    }

    private static let zusatzArten = ["sondereinsatz", "zusatz", "verlegung", "statt", "mitbetreuung"]

    static func istZusatzstunde(_ k: Karte) -> Bool {
        if k.zusaetzlich { return true }
        guard k.status == "geaendert", !k.fach.isEmpty else { return false }
        let a = k.art.lowercased()
        return zusatzArten.contains { a.contains($0) } || k.unterricht == nil
    }

    private static func gleich(_ a: String, _ b: String) -> Bool {
        a.caseInsensitiveCompare(b) == .orderedSame
    }

    private static func werte(_ w: [Wert]) -> [String] {
        w.map(\.k).filter { !$0.isEmpty }
    }

    /// Werte ohne Doppelte (Groß-/Kleinschreibung egal), Reihenfolge bleibt.
    private static func einmal(_ liste: [String]) -> [String] {
        var ergebnis: [String] = []
        for w in liste where !w.isEmpty && !ergebnis.contains(where: { gleich($0, w) }) { ergebnis.append(w) }
        return ergebnis
    }

    /// Abwesende Koppelklassen: Einträge ohne neuen Wert mit gesetztem alten Wert (ohne Doppelte).
    static func weggefalleneKlassen(_ k: Karte) -> [String] {
        var ergebnis: [String] = []
        for w in k.klassen + k.mitklassen where w.k.isEmpty {
            if let a = w.alt, !a.isEmpty, !ergebnis.contains(where: { gleich($0, a) }) { ergebnis.append(a) }
        }
        return ergebnis
    }

    /// Ursprungswerte geänderter Einträge (ohne Doppelte; ein „alter“ Wert gleich dem neuen zählt nicht).
    private static func alte(_ w: [Wert]) -> [String] {
        var ergebnis: [String] = []
        for x in w where x.neu || x.k.isEmpty {
            if let a = x.alt, !a.isEmpty, !gleich(a, x.k), !ergebnis.contains(where: { gleich($0, a) }) { ergebnis.append(a) }
        }
        return ergebnis
    }

    /// Vertretungsart einer reinen Raumänderung (nur der Raum ändert sich; Lehrkraft, Klasse und Fach bleiben).
    static func istRaumaenderungsArt(_ art: String) -> Bool {
        let a = art.lowercased()
        return ["raumänderung", "raumaenderung", "raumvertretung", "raum-vertretung", "raumwechsel", "raumtausch"]
            .contains { a.contains($0) }
    }

    /// Reine Raumänderung laut Vertretungsart: Lehrkräfte tragen dann kein Änderungs-Label (auch wenn ein Wert als „neu“
    /// gekennzeichnet ist), der Raum dagegen immer – offene bzw. entfernte Lehrkräfte bleiben, wie sie sind.
    static func raumaenderungNormalisiert(_ original: Karte) -> Karte {
        var k = original
        guard istRaumaenderungsArt(k.art), k.status != "entfall", !k.istOffeneVertretung else { return k }
        k.lehrer = k.lehrer.map { w in
            guard !w.k.isEmpty, w.ersetzt || w.plus || w.neu else { return w }
            return Wert(k: w.k)
        }
        return k
    }

    /// Raumplan: Stunden mit abwesender Lehrkraft erscheinen wie im Klassenplan als Änderung (grün) – nie als Entfall
    /// oder abgegebene Stunde. Ältere Antworten melden sie noch mit Status „abwesend“ (ohne `lehrerAbwesend`): mit
    /// Vertretung wird daraus „geaendert“ (abwesende Lehrkraft durchgestrichen, Vertretung als grüne Plakette), ohne
    /// Vertretung „offen“ (Kürzel durchgestrichen in der grünen Plakette).
    static func raumplanNormalisiert(_ original: Karte) -> Karte {
        var k = original
        guard !istBereitschaft(k) else { return k }
        k.lehrerAbwesend = false
        guard k.status == "abwesend" else { return k }
        let vertreten = k.lehrer.contains { $0.neu && !$0.k.isEmpty }
        let schonOffen = k.lehrer.contains { $0.k.isEmpty && !($0.alt ?? "").isEmpty }
        if vertreten {
            k.status = "geaendert"
        } else {
            if !schonOffen {
                k.lehrer = k.lehrer.map { w in
                    w.k.isEmpty ? w : Wert(k: "", alt: w.k, neu: true)
                }
            }
            if k.lehrer.isEmpty { k.vertretungOffen = true }
            k.status = "offen"
        }
        return k
    }

    /// Eine Stundenkarte als Kachel (nil = nichts anzuzeigen).
    static func ausKarte(_ original: Karte, typ: String, ich: String, raster: [Rasterzeit]) -> Kachel? {
        // Koppelklassen: abwesende Klassen kommen als {k:"", alt:"INF13"}. Im Plan der abwesenden Klasse selbst
        // erscheint die Stunde wie deren Abwesenheit (Klasse als abwesend vermerkt).
        var k = original
        if typ == "klasse" && !ich.isEmpty
            && k.klassen.contains(where: { $0.k.isEmpty && gleich($0.alt ?? "", ich) }) {
            k.klassen = k.klassen.map { w in
                w.k.isEmpty && gleich(w.alt ?? "", ich) ? Wert(k: w.alt ?? "", alt: nil, neu: false) : w
            }
            if !k.abwesendeKlassen.contains(where: { gleich($0, ich) }) { k.abwesendeKlassen.append(ich) }
        }
        if typ == "raum" { k = raumplanNormalisiert(k) }
        k = raumaenderungNormalisiert(k)
        // Eigener Plan einer Lehrkraft (Vertrag Karten 27, Abschnitt 9): die eigene Lehrkraft nie als Label – auch nicht als
        // Vertretung {k: ich, alt: andere, neu} (grüne Karte und Änderungs-Quadrat zeigen die Vertretung)
        if typ == "lehrer" && !ich.isEmpty {
            k.lehrer = k.lehrer.map { w in gleich(w.k, ich) ? Wert(k: w.k) : w }
        }
        let lage = zeitraum(k, raster: raster)
        var kachel = Kachel(karte: k, art: .normal, datum: k.datum, von: k.von, bis: max(k.bis, k.von),
                            beginn: lage.beginn, ende: lage.ende)
        kachel.balkenHex = k.farbe
        // Info-Zeichen nur bei echtem Text (nicht bei bloßer Art wie „Sondereinsatz“) bzw. Notizen
        kachel.info = (!k.anzeigeText.isEmpty && !istEntfaelltText(k.anzeigeText)) || !k.notizSchueler.isEmpty || !k.notizLehrer.isEmpty
        kachel.hausaufgabe = k.hausaufgabeFaellig
        kachel.online = k.online && !k.istEntfall && k.status != "abwesend"
        kachel.verlegung = k.verlegung
        // Server-Marker (oben rechts) zeigt nur das Web – iOS-Karten haben oben rechts nie ein Zeichen
        // Zusatzstunde: automatische Standardtexte erscheinen nicht – manuell eingegebene Vertretungstexte (und Notizen)
        // zeigen das Info-Zeichen
        if k.istZusatzstunde {
            kachel.info = !k.zusatzText.isEmpty || !k.notizSchueler.isEmpty || !k.notizLehrer.isEmpty
        }

        // Veranstaltung: Feld über den ganzen Zeitraum
        if k.status == "veranstaltung" || (k.status == "geaendert" && k.unterricht == nil && k.fach.isEmpty && !k.anzeigeText.isEmpty) {
            kachel.art = .veranstaltung
            kachel.feld = true
            kachel.online = false
            kachel.verlegung = nil
            kachel.titel = k.anzeigeText.isEmpty ? k.anzeigeArt : k.anzeigeText
            kachel.zeilen = veranstaltungsZeilen(k, typ: typ, ich: ich, anlass: kachel.titel)
            // Infotext der Veranstaltung (in den Details als Zeile „Information“): Info-Zeichen unten rechts
            kachel.info = !veranstaltungsInfo(k).isEmpty
            kachel.rang = 2
            return kachel.zeilen.isEmpty ? nil : kachel
        }

        // Bereitschaft
        if istBereitschaft(k) {
            let titel = k.fach.isEmpty ? "Bereitschaft" : k.fach
            kachel.titel = titel
            kachel.info = !k.anzeigeText.isEmpty && !istEntfaelltText(k.anzeigeText)
            kachel.hausaufgabe = false
            kachel.online = false
            kachel.verlegung = nil
            if istBereitschaftAbwesend(k) {
                kachel.art = .bereitschaftAbwesend
                var zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)], fett: true)]
                var lehrkraefte = k.lehrer.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
                if lehrkraefte.isEmpty && !ich.isEmpty && typ == "lehrer" { lehrkraefte = [ich] }
                if !lehrkraefte.isEmpty {
                    // Abwesende Lehrkraft als Plakette mit durchgestrichenem Kürzel
                    zeilen.append(Kachelzeile(schluessel: "lehrer", teile: liste(lehrkraefte, stil: .offen, trenner: ", ")))
                }
                kachel.zeilen = zeilen
                kachel.rang = 3
            } else {
                kachel.art = .bereitschaft
                kachel.zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)])]
            }
            return kachel
        }

        // Geänderte Werte – je Element (Klasse, Lehrkraft, Raum, Fach) nur, was sich laut Server tatsächlich geändert hat:
        // ersetzt (alt ≠ k), hinzugekommen (plus), entfernt bzw. offen (k leer, alt gesetzt). „neu“ mit alt == k zählt nicht.
        var geaendert: Set<String> = []
        if k.klassen.contains(where: \.geaendert) { geaendert.insert("klasse") }
        if k.lehrer.contains(where: \.geaendert) { geaendert.insert("lehrer") }
        if k.raeume.contains(where: \.geaendert) { geaendert.insert("raum") }
        if let alt = k.fachAlt, !alt.isEmpty, !gleich(alt, k.fach) { geaendert.insert("fach") }
        // Raumänderung ohne gekennzeichneten Wert (nur an der Art erkennbar): der Raum steht trotzdem als Plakette da
        if !geaendert.contains("raum") && !werte(k.raeume).isEmpty && istRaumaenderungsArt(k.art) {
            geaendert.insert("raum")
        }

        let offen = k.vertretungOffen || ((k.status == "offen" || k.status == "abwesend")
            && k.lehrer.contains { $0.neu && $0.k.isEmpty && !($0.alt ?? "").isEmpty })
        var titel = k.fach
        switch k.status {
        case "regulaer":
            kachel.art = .normal
        case "geaendert", "offen", "abwesend":
            kachel.art = .aenderung
            kachel.zusatz = istZusatzstunde(k)
        case "entfall":
            kachel.art = .entfall
        case "klausur":
            kachel.art = .klausur
            if titel.isEmpty { titel = k.text.isEmpty ? "Klausur" : k.text }
        case "buchung":
            kachel.art = .termin
            if !k.raeume.isEmpty { geaendert.insert("raum") }
            if titel.isEmpty { titel = !k.text.isEmpty && k.text != "Raumbuchung" ? k.text : "Raumbuchung" }
        case "vorgemerkt":
            kachel.art = k.art.lowercased().hasPrefix("klausur") ? .klausur : .aenderung
            if titel.isEmpty { titel = k.text.isEmpty ? k.art : k.text }
        default:
            kachel.art = k.istPruefung ? .klausur : .aenderung
        }
        if k.istPruefung && k.status != "entfall" && k.status != "abwesend" { kachel.art = .klausur }
        // Koppelklasse abwesend: im Plan der anderen Klasse grün wie eine Änderung
        if typ == "klasse" && kachel.art == .normal && !weggefalleneKlassen(k).isEmpty {
            kachel.art = .aenderung
            geaendert.insert("klasse")
        }
        // Raumbuchung ohne weitere Änderung: keine Vertretung, die Kachel bleibt regulär
        if k.istRaumbuchung && (kachel.art == .aenderung || kachel.art == .termin) && geaendert.subtracting(["raum"]).isEmpty && !offen {
            kachel.art = .normal
            kachel.zusatz = false
        }
        // Online-Unterricht: keine eigene Kartenfarbe und kein Raum-Label – nur das Online-Zeichen unten rechts
        // Reine Raumänderung (keine Raumbuchung, nicht online, keine offene Vertretung): grüne Karte wie eine Änderung
        kachel.raumAenderung = geaendert == ["raum"] && !offen && !k.istRaumbuchung && !kachel.zusatz
            && (kachel.art == .aenderung || kachel.art == .normal)
        // Bloße Art-Bezeichnungen (Sondereinsatz, zusätzlicher Unterricht) erscheinen nie als Titel
        if titel.isEmpty { titel = k.anzeigeArt }
        kachel.titel = titel

        // Im eigenen Plan: Stunde wird von jemand anderem übernommen (oder ist noch offen) → läuft ohne mich
        let jetztLehrer = werte(k.lehrer).joined(separator: ",").uppercased()
        let alteLehrer = alte(k.lehrer).joined(separator: ",").uppercased()
        let abgegebenAnMich = typ != "klasse" && geaendert.contains("lehrer") && !ich.isEmpty && alteLehrer == ich && jetztLehrer != ich
        let verlegtZiel = k.art.lowercased().contains("verleg") && kachel.art == .aenderung
        let alleKlassenAbwesend = !k.klassen.isEmpty && !k.abwesendeKlassen.isEmpty
            && werte(k.klassen).allSatisfy { kl in k.abwesendeKlassen.contains { gleich($0, kl) } }
        // Raumplan: offene Vertretung bleibt eine grüne Änderungskarte (Eckklammern nur im Plan der abwesenden Lehrkraft)
        let freigegeben = typ != "klasse" && ((offen && typ != "raum") || abgegebenAnMich || alleKlassenAbwesend) && !verlegtZiel
            && (kachel.art == .aenderung || kachel.art == .entfall || kachel.art == .normal)
        if freigegeben { kachel.art = .abgegeben }

        kachel.zeilen = zeilen(k, typ: typ, ich: ich, art: kachel.art, geaendert: geaendert, titel: titel,
                               klassenAbwesend: alleKlassenAbwesend)
        if kachel.zeilen.isEmpty { return nil }

        // Rang: vorne steht, was tatsächlich stattfindet
        let koppAlleWeg = k.lehrer.count > 1 && k.lehrer.allSatisfy { $0.neu && $0.k.isEmpty }
        if koppAlleWeg || kachel.art == .entfall {
            kachel.rang = 4
        } else if kachel.art == .abgegeben {
            kachel.rang = 3
        } else if kachel.art == .aenderung || !geaendert.isEmpty {
            kachel.rang = 0
        } else {
            kachel.rang = 1
        }
        return kachel
    }

    /// Infotext einer Veranstaltung: Text bzw. Art und Notizen (ohne Doppelte).
    static func veranstaltungsInfo(_ k: Karte) -> String {
        var teile: [String] = []
        for t in [k.anzeigeText.isEmpty ? k.anzeigeArt : k.anzeigeText, k.notizSchueler, k.notizLehrer] {
            let w = t.trimmingCharacters(in: .whitespacesAndNewlines)
            if !w.isEmpty && !teile.contains(where: { gleich($0, w) }) { teile.append(w) }
        }
        return teile.joined(separator: "\n")
    }

    private static func istEntfaelltText(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return t == "entfällt" || t == "entfällt." || t == "entfaellt" || t == "entfaellt."
    }

    private static func liste(_ werte: [String], stil: Kachelteil.Stil, trenner: String) -> [Kachelteil] {
        werte.enumerated().map { i, w in
            Kachelteil(text: w, stil: stil, trenner: i < werte.count - 1 ? trenner : "")
        }
    }

    /// Veranstaltung: oben, was man im eigenen Plan nicht schon weiß (Klassenplan: Lehrkräfte, sonst Klassen), darunter
    /// der Anlass, dann die übrigen Teilnehmenden und der Raum.
    /// Jede Angabe steht nur einmal (keine doppelten Lehrkräfte, Klassen oder Räume).
    private static func veranstaltungsZeilen(_ k: Karte, typ: String, ich: String, anlass: String) -> [Kachelzeile] {
        let alle = einmal(werte(k.lehrer))
        let klassen = einmal(werte(k.klassen))
        let oben = typ == "klasse" ? alle : klassen
        let unten = typ == "klasse"
            ? klassen.filter { $0.uppercased() != ich }
            : alle.filter { typ != "lehrer" || $0.uppercased() != ich }
        var zeilen: [Kachelzeile] = []
        if !oben.isEmpty {
            zeilen.append(Kachelzeile(schluessel: typ == "klasse" ? "lehrer" : "klasse",
                                      teile: [Kachelteil(text: oben.joined(separator: ", "))], fett: true))
        }
        if !anlass.isEmpty {
            zeilen.append(Kachelzeile(schluessel: "anlass", teile: [Kachelteil(text: anlass)], fett: oben.isEmpty))
        }
        if !unten.isEmpty {
            zeilen.append(Kachelzeile(schluessel: typ == "klasse" ? "klasse" : "lehrer",
                                      teile: [Kachelteil(text: unten.joined(separator: ", "))]))
        }
        let raeume = einmal(werte(k.raeume))
        if !raeume.isEmpty {
            zeilen.append(Kachelzeile(schluessel: "raum", teile: [Kachelteil(text: raeume.joined(separator: ", "))]))
        }
        return zeilen
    }

    /// Zeilen nach Vorlage – Klassenplan: Lehrkraft · Fach · Raum · Klasse; Lehrer- und Raumplan: Klasse · Fach · Raum · Lehrkraft.
    /// Die letzte Zeile entfällt, wenn sie nur das betrachtete Element wiederholt und sich nichts geändert hat.
    private static func zeilen(_ k: Karte, typ: String, ich: String, art: Kachelart, geaendert: Set<String>,
                               titel: String, klassenAbwesend: Bool) -> [Kachelzeile] {
        if art == .termin {
            var termin: [Kachelzeile] = []
            if !titel.isEmpty {
                termin.append(Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)]))
            }
            let lehrkraefte = werte(k.lehrer)
            if !lehrkraefte.isEmpty {
                termin.append(Kachelzeile(schluessel: "lehrer", teile: liste(lehrkraefte, stil: .text, trenner: ", ")))
            }
            let klassenNamen = werte(k.klassen)
            if !klassenNamen.isEmpty {
                termin.append(Kachelzeile(schluessel: "klasse", teile: liste(klassenNamen, stil: .text, trenner: ", ")))
            }
            let raumNamen = werte(k.raeume)
            if !raumNamen.isEmpty {
                termin.append(Kachelzeile(schluessel: "raum", teile: liste(raumNamen, stil: .plakette, trenner: " ")))
            }
            for i in termin.indices { termin[i].fett = i == 0 }
            return termin
        }
        let klasseOben = klassenAbwesend && typ == "klasse"
        let erste = typ == "klasse" ? "lehrer" : "klasse"
        let letzte = typ == "klasse" ? "klasse" : "lehrer"
        let reihenfolge = klasseOben ? ["klasse", "lehrer", "fach", "raum"] : [erste, "fach", "raum", letzte]
        // Offene Vertretung (Karte „offen“ bzw. Lehrkraft ohne neuen Wert, alter Wert gesetzt): alte Lehrkraft
        // immer durchgestrichen in grüner Plakette
        let lehrerOffen = art != .entfall && k.istOffeneVertretung

        var ergebnis: [Kachelzeile] = []
        for (i, schluessel) in reihenfolge.enumerated() {
            var zeile = Kachelzeile(schluessel: schluessel, teile: [], fett: i == 0)
            var hatListe = false
            var koppliste = false
            var geaendertWert = geaendert.contains(schluessel)
            switch schluessel {
            case "lehrer":
                if k.lehrer.count > 1 {
                    // Kopplung: immer alle Lehrkräfte, jedes Kürzel mit eigenem Zustand – auch im Plan der abwesenden
                    // Lehrkraft. Abwesend ohne Vertretung: durchgestrichen in der grünen Plakette (offen); abwesend mit
                    // Vertretung: durchgestrichen, daneben die Vertretung als grüne Plakette.
                    var teile: [Kachelteil] = []
                    func hinzu(_ t: Kachelteil) {
                        if teile.contains(where: { gleich($0.text, t.text) && $0.stil == t.stil }) { return }
                        teile.append(t)
                    }
                    let offenStil: Kachelteil.Stil = art == .entfall ? .weg : .offen
                    for w in k.lehrer {
                        let alt = w.alt ?? ""
                        if w.k.isEmpty {
                            if !alt.isEmpty {
                                hinzu(Kachelteil(text: alt, stil: offenStil))
                            } else if w.neu && k.vertretungOffen {
                                hinzu(Kachelteil(text: "???", stil: offenStil))
                            }
                        } else if w.neu && !alt.isEmpty && !gleich(alt, w.k) {
                            hinzu(Kachelteil(text: alt, stil: .weg))
                            hinzu(Kachelteil(text: w.k, stil: .plakette))
                        } else {
                            hinzu(Kachelteil(text: w.k))
                        }
                    }
                    // Wer vertreten wird, steht nicht zusätzlich als „offen“ da
                    let vertreten = teile.filter { $0.stil == .weg }.map { $0.text }
                    teile.removeAll { t in t.stil == .offen && vertreten.contains { gleich($0, t.text) } }
                    if teile.count > 1 {
                        // Abwesende und vertretende Lehrkraft durch „|“ getrennt (z. B. „LAN | ILK“), sonst Komma
                        for j in teile.indices {
                            guard j < teile.count - 1 else { teile[j].trenner = ""; continue }
                            let paar = teile[j].stil == .weg && teile[j + 1].stil == .plakette
                            teile[j].trenner = paar ? "\u{2009}|\u{2009}" : ", "
                        }
                        zeile.teile = teile
                        hatListe = true
                        koppliste = true
                    }
                }
                if !hatListe {
                    let jetzt = werte(k.lehrer).joined(separator: ", ")
                    let vorher = alte(k.lehrer).joined(separator: ", ")
                    if art == .abgegeben && !klassenAbwesend {
                        // Abgegebene Stunde: neue Lehrkraft als grüne Plakette, sonst das eigene Kürzel durchgestrichen
                        if !jetzt.isEmpty {
                            zeile.teile = [Kachelteil(text: jetzt, stil: .plakette)]
                        } else {
                            let weg = vorher.isEmpty ? (k.lehrer.compactMap { $0.alt }.first ?? ich) : vorher
                            if !weg.isEmpty { zeile.teile = [Kachelteil(text: weg, stil: .weg)] }
                        }
                        geaendertWert = true
                    } else {
                        zeile.teile = wertTeile(jetzt: jetzt, vorher: vorher, geaendert: geaendertWert)
                    }
                }
                if lehrerOffen && !koppliste {
                    for j in zeile.teile.indices where zeile.teile[j].stil == .weg {
                        zeile.teile[j].stil = .offen
                    }
                    if zeile.teile.isEmpty {
                        zeile.teile = liste(k.offeneLehrkraefte, stil: .offen, trenner: ", ")
                    }
                }
            case "klasse":
                // Eigene Klasse(n) und anwesende Koppelklassen (mitklassen) – ohne Doppelte
                var namen = werte(k.klassen)
                for m in werte(k.mitklassen) where !namen.contains(where: { gleich($0, m) }) { namen.append(m) }
                let abwesend = k.abwesendeKlassen
                // Abwesende Koppelklassen ({k:"", alt}) stehen durchgestrichen im grünen Label (wie abwesende Lehrkraft)
                let weg = weggefalleneKlassen(k)
                if namen.count > 1 || !abwesend.isEmpty || !weg.isEmpty {
                    var teile = namen.map { n -> Kachelteil in
                        Kachelteil(text: n, stil: abwesend.contains { gleich($0, n) } ? .weg : .text)
                    }
                    teile += weg.filter { w in !namen.contains { gleich($0, w) } }
                        .map { Kachelteil(text: $0, stil: art == .entfall ? .weg : .offen) }
                    // Im Klassenplan steht nur, wer mit dabei ist – die eigene Klasse nur, wenn sie fehlt
                    if typ == "klasse" && !ich.isEmpty && teile.count > 1 {
                        teile = teile.filter { $0.text.uppercased() != ich || $0.stil == .weg }
                    }
                    if !teile.isEmpty {
                        for j in 0..<(teile.count - 1) { teile[j].trenner = ", " }
                        zeile.teile = teile
                        hatListe = namen.count > 1 || teile.contains { $0.stil == .weg || $0.stil == .offen }
                    }
                } else {
                    zeile.teile = wertTeile(jetzt: namen.joined(separator: ", "), vorher: alte(k.klassen).joined(separator: ", "),
                                            geaendert: geaendertWert)
                }
            case "fach":
                let vorher = k.fachAlt ?? ""
                if titel.isEmpty && !vorher.isEmpty {
                    zeile.teile = [Kachelteil(text: vorher, stil: .weg)]
                } else if !titel.isEmpty {
                    zeile.teile = [Kachelteil(text: titel, stil: geaendertWert && art != .klausur ? .plakette : .text)]
                }
            default: // raum
                let jetzt = werte(k.raeume)
                let vorher = alte(k.raeume)
                if jetzt.count > 1 {
                    // Mehrere Räume: jeder für sich; hervorgehoben nur, was ersetzt bzw. hinzugekommen ist. Ist kein Raum
                    // gekennzeichnet (Raumänderung nur an der Art erkennbar), stehen alle Räume im Label.
                    let neue = k.raeume.filter { !$0.k.isEmpty && $0.geaendert }.map { $0.k.uppercased() }
                    let markiert = k.raeume.contains(where: \.geaendert)
                    zeile.teile = jetzt.enumerated().map { j, r in
                        let neu = geaendertWert && (markiert ? neue.contains(r.uppercased()) : true)
                        return Kachelteil(text: r, stil: neu ? .plakette : .text, trenner: j < jetzt.count - 1 ? " " : "")
                    }
                    hatListe = true
                } else {
                    // Raumänderung immer mit Plakette (neuer Raum grün) – auch wenn sich sonst nichts geändert hat. Wurde nur
                    // ein anderer Raum entfernt, bleibt der verbleibende Raum ohne Label.
                    let markiert = k.raeume.contains(where: \.geaendert)
                    let raumNeu = geaendertWert
                        && (jetzt.isEmpty || !markiert || k.raeume.contains { !$0.k.isEmpty && $0.geaendert })
                    zeile.teile = wertTeile(jetzt: jetzt.joined(separator: ", "), vorher: vorher.joined(separator: ", "),
                                            geaendert: raumNeu)
                    // Offene Raumvertretung (kein neuer Raum, bisheriger Raum gesetzt): durchgestrichen in der grünen Plakette
                    if jetzt.isEmpty && art != .entfall && k.status == "offen" {
                        for j in zeile.teile.indices where zeile.teile[j].stil == .weg { zeile.teile[j].stil = .offen }
                    }
                }
                // Entfernte Räume ({k:"", alt}) neben einem verbleibenden Raum: durchgestrichen dahinter
                if !jetzt.isEmpty && art != .entfall {
                    let entfernt = einmal(k.raeume.filter { $0.k.isEmpty }.compactMap { $0.alt }
                        .filter { a in !a.isEmpty && a != "???" && !jetzt.contains { gleich($0, a) } })
                    if !entfernt.isEmpty {
                        if let letzter = zeile.teile.indices.last { zeile.teile[letzter].trenner = " " }
                        zeile.teile += liste(entfernt, stil: .weg, trenner: " ")
                        hatListe = true
                    }
                }
            }
            if zeile.teile.isEmpty || zeile.text.trimmingCharacters(in: .whitespaces).isEmpty { continue }
            // Online-Stunde: keine Raumzeile (kein Raum-Label) – nur das Online-Zeichen unten auf der Karte
            if schluessel == "raum" && k.online && art != .entfall && k.status != "abwesend" { continue }

            // Eigener Plan (Lehrkraft): das eigene Kürzel bekommt nur dann ein Label, wenn es selbst geändert ist (z. B. ich
            // vertrete: alter Wert einer anderen Lehrkraft) – nie nur, weil sich an der Stunde etwas anderes geändert hat
            // Die letzte Zeile entfällt, wenn sie nur das betrachtete Element wiederholt
            let wiederholt = !ich.isEmpty && zeile.text.uppercased() == ich && zeile.nurText && !hatListe
            if schluessel == letzte && wiederholt && !geaendertWert { continue }
            if typ == "raum" && schluessel == "raum" && wiederholt && !geaendertWert { continue }
            ergebnis.append(zeile)
        }
        // Hinzugekommene Werte (plus): grünes Label mit „+“ davor, z. B. „+B070“
        if art != .entfall {
            let plusWerte: [String: [String]] = [
                "lehrer": k.lehrer.filter(\.plus).map(\.k),
                "klasse": (k.klassen + k.mitklassen).filter(\.plus).map(\.k),
                "raum": k.raeume.filter(\.plus).map(\.k)
            ]
            for i in ergebnis.indices {
                guard let plus = plusWerte[ergebnis[i].schluessel], !plus.isEmpty else { continue }
                for j in ergebnis[i].teile.indices {
                    let t = ergebnis[i].teile[j]
                    guard t.stil == .text || t.stil == .plakette, plus.contains(where: { gleich($0, t.text) }) else { continue }
                    ergebnis[i].teile[j].stil = .plakette
                    ergebnis[i].teile[j].text = "+" + t.text
                }
            }
        }
        // Die erste verbleibende Zeile ist fett
        for i in ergebnis.indices { ergebnis[i].fett = i == 0 }
        return ergebnis
    }

    /// Anzuzeigender Wert: fehlt der neue Wert, obwohl sich etwas geändert hat, steht der alte durchgestrichen da.
    private static func wertTeile(jetzt: String, vorher: String, geaendert: Bool) -> [Kachelteil] {
        if geaendert && jetzt.isEmpty && !vorher.isEmpty { return [Kachelteil(text: vorher, stil: .weg)] }
        let text = jetzt.isEmpty ? vorher : jetzt
        if text.isEmpty { return [] }
        return [Kachelteil(text: text, stil: geaendert ? .plakette : .text)]
    }

    // MARK: Aufsichten

    /// Pausenaufsicht als Pille in der Pause nach der Stunde (vor der ersten Stunde: über dem Raster).
    static func aufsichtKachel(_ a: Aufsicht, raster: [Rasterzeit], ich: String = "") -> Kachel {
        let erste = raster.first
        var beginn = Rasterzeit.minuten(a.beginn)
        var ende = Rasterzeit.minuten(a.ende)
        let vorErster = a.nachStunde <= 0
        if vorErster, let erste {
            beginn = erste.beginn - 15
            ende = erste.beginn
        }
        if beginn == nil, let i = raster.firstIndex(where: { $0.nr == a.nachStunde }) {
            beginn = raster[i].ende
            ende = i + 1 < raster.count ? raster[i + 1].beginn : raster[i].ende + 15
        }
        let b = beginn ?? (erste?.beginn ?? 480)
        var e = ende ?? b
        // Ohne Pausenlänge (Stunden direkt hintereinander) bleibt die Aufsicht ein Punkt – das Raster dehnt dort eine Lücke
        if e < b { e = b }
        let stunde = max(1, a.nachStunde)
        var k = Kachel(aufsicht: a, art: .aufsicht, datum: a.datum, von: stunde, bis: stunde, beginn: b, ende: e)
        k.inPause = true
        k.vorErsterStunde = vorErster
        k.titel = !a.gangName.isEmpty ? a.gangName : (!a.gang.isEmpty ? a.gang : "Aufsicht")
        k.zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: k.titel)], fett: true)]
        switch a.status {
        case "vertreten": k.aufsichtsstand = .abgegeben
        case "vertretung": k.aufsichtsstand = .vertretung
        default: k.aufsichtsstand = .regulaer
        }
        if let lehrer = aufsichtLehrerZeile(a, stand: k.aufsichtsstand, ich: ich) { k.zeilen.append(lehrer) }
        k.balkenHex = ""
        return k
    }

    /// Kürzelzeile einer vertretenen Aufsicht – wie bei einer Stundenkarte mit abwesender Lehrkraft: abwesendes Kürzel
    /// durchgestrichen, daneben (durch „|“ getrennt) die Vertretung als grüne Plakette; ohne Vertretung das abwesende
    /// Kürzel durchgestrichen in der grünen Plakette (offen). Reguläre Aufsicht: keine Zeile.
    static func aufsichtLehrerZeile(_ a: Aufsicht, stand: Aufsichtsstand, ich: String) -> Kachelzeile? {
        let lehrer = a.lehrer.trimmingCharacters(in: .whitespaces)
        let alt = (a.alt ?? "").trimmingCharacters(in: .whitespaces)
        let offen = lehrer.isEmpty || lehrer == "???"
        let abwesend: String
        let vertretung: String
        switch stand {
        case .regulaer:
            return nil
        case .abgegeben:
            // Eigene Aufsicht: abwesend bin ich (bzw. der gemeldete alte Wert), `lehrer` übernimmt
            abwesend = !alt.isEmpty ? alt : ich
            vertretung = offen ? "" : lehrer
        case .vertretung:
            // Eingesprungen: `alt` ist abwesend, ich (bzw. `lehrer`) übernehme
            abwesend = alt
            vertretung = offen ? ich : lehrer
        }
        var teile: [Kachelteil] = []
        if vertretung.isEmpty {
            if !abwesend.isEmpty { teile.append(Kachelteil(text: abwesend, stil: .offen)) }
        } else {
            if !abwesend.isEmpty && !gleich(abwesend, vertretung) {
                teile.append(Kachelteil(text: abwesend, stil: .weg, trenner: "\u{2009}|\u{2009}"))
            }
            teile.append(Kachelteil(text: vertretung, stil: .plakette))
        }
        return teile.isEmpty ? nil : Kachelzeile(schluessel: "lehrer", teile: teile)
    }

    /// Bereitschaftsblöcke um die Stunden kürzen, in denen die Lehrkraft Unterricht bzw. eine Vertretung hält.
    private static func bereitschaftenKuerzen(_ kacheln: [Kachel], belegt: Set<Int>, raster: [Rasterzeit]) -> [Kachel] {
        var ergebnis: [Kachel] = []
        for k in kacheln {
            guard k.art == .bereitschaft, (k.von...k.bis).contains(where: { belegt.contains($0) }) else {
                ergebnis.append(k)
                continue
            }
            var teilVon: Int? = nil
            func abschliessen(_ bis: Int) {
                guard let v = teilVon else { return }
                var teil = k
                teil.id = UUID()
                teil.von = v
                teil.bis = bis
                teil.beginn = raster.first(where: { $0.nr == v })?.beginn ?? k.beginn
                teil.ende = raster.first(where: { $0.nr == bis })?.ende ?? k.ende
                if let karte = k.karte {
                    teil.karte = karte.teil(von: v, bis: bis, beginn: Rasterzeit.uhrzeit(teil.beginn), ende: Rasterzeit.uhrzeit(teil.ende))
                }
                ergebnis.append(teil)
                teilVon = nil
            }
            for s in k.von...k.bis {
                if belegt.contains(s) {
                    abschliessen(s - 1)
                } else if teilVon == nil {
                    teilVon = s
                }
            }
            abschliessen(k.bis)
        }
        return ergebnis
    }

    /// Blöcke, die über die Pause einer Aufsicht hinweg laufen, dort teilen (Veranstaltungen bleiben durchgehend).
    private static func anPausenTeilen(_ kacheln: [Kachel], pausen: Set<Int>, raster: [Rasterzeit]) -> [Kachel] {
        guard !pausen.isEmpty else { return kacheln }
        var ergebnis: [Kachel] = []
        for k in kacheln {
            let schnitte = pausen.filter { $0 >= k.von && $0 < k.bis }.sorted()
            guard !k.inPause, !k.feld, k.dauer >= 2, !schnitte.isEmpty, k.art != .termin else {
                ergebnis.append(k)
                continue
            }
            var anfang = k.von
            for p in schnitte + [k.bis] {
                var teil = k
                teil.id = UUID()
                teil.von = anfang
                teil.bis = p
                let b = raster.first(where: { $0.nr == anfang })?.beginn ?? k.beginn
                let e = raster.first(where: { $0.nr == p })?.ende ?? k.ende
                teil.beginn = b
                teil.ende = e
                if let karte = k.karte {
                    teil.karte = karte.teil(von: anfang, bis: p, beginn: Rasterzeit.uhrzeit(b), ende: Rasterzeit.uhrzeit(e))
                }
                ergebnis.append(teil)
                anfang = p + 1
            }
        }
        return ergebnis
    }

    // MARK: Zeiten

    /// Beginn und Ende einer Karte in Minuten (aus dem Raster, sonst die eigene Uhrzeit).
    static func zeitraum(_ karte: Karte, raster: [Rasterzeit]) -> (beginn: Int, ende: Int) {
        let vonZeit = raster.first(where: { $0.nr == karte.von })
        let bisZeit = raster.first(where: { $0.nr == max(karte.bis, karte.von) })
        let beginn = vonZeit?.beginn ?? Rasterzeit.minuten(karte.beginn) ?? (raster.first?.beginn ?? 480)
        var ende = bisZeit?.ende ?? Rasterzeit.minuten(karte.ende) ?? (beginn + 45)
        if ende <= beginn { ende = beginn + 45 }
        return (beginn, ende)
    }

    private static func istVergangen(_ k: Kachel, jetzt: Date) -> Bool {
        switch k.art {
        case .normal, .aenderung, .entfall, .bereitschaft, .bereitschaftAbwesend, .aufsicht, .veranstaltung, .abgegeben:
            break
        default:
            return false
        }
        guard let tag = Datumsformat.datum(k.datum) else { return false }
        let ende = Datumsformat.kalender.date(byAdding: .minute, value: k.ende, to: Datumsformat.kalender.startOfDay(for: tag)) ?? tag
        return ende < jetzt
    }

    // MARK: - Anordnung

    /// Lage in einer Gruppe überlappender Kacheln: Spur (Spalte), Zahl der Spuren, Breite in Spuren (dehnt sich nach rechts
    /// über freie Spuren) bzw. senkrechter Stapel (gleiche Zeit, untereinander geteilt wie im Original).
    struct Spurlage: Equatable {
        var spur: Int = 0
        var spuren: Int = 1
        var spanne: Int = 1
        var stapel: Int = 1
        var stapelIndex: Int = 0
    }

    /// Mindesthöhe je Block, damit gleichzeitige Blöcke untereinander geteilt werden (drei Zeilen: 4 + 3 × 14 pt).
    static let stapelMindestHoehe: CGFloat = 46
    /// Mindestbreite einer Spur (Woche bzw. Tag); passen nicht alle Spuren, treten die übrigen hinter einen Zählstreifen.
    static let spurMindestBreiteWoche: CGFloat = 22
    static let spurMindestBreiteTag: CGFloat = 40

    /// Spuren wie in Kalender-Apps: Kacheln mit überlappender Zeit (Minuten, nicht Stundennummern) bilden zusammenhängende
    /// Gruppen; innerhalb einer Gruppe kommt jede Kachel in die erste Spur, in der sie mit nichts überlappt (vorne/links, was
    /// stattfindet: Rang, dann Beginn, dann längere Blöcke, dann Reihenfolge der Daten). Danach dehnt sich jede Kachel über
    /// freie Spuren rechts von ihr. Haben alle Blöcke einer Gruppe genau dieselbe Zeit (2–3 Blöcke, z. B. Kopplung WW/PK) und
    /// reicht die Höhe für je drei Zeilen, werden sie wie im Original untereinander geteilt. Nichts wird zusammengefasst oder
    /// ausgeblendet; das Ergebnis hängt nur von den Daten ab (stabil bei Vertretung, Entfall, Doppelstunden).
    static func spurenVerteilen(_ liste: [Kachel], hoehe: ((Kachel) -> CGFloat)? = nil) -> [UUID: Spurlage] {
        var ergebnis: [UUID: Spurlage] = [:]
        guard !liste.isEmpty else { return ergebnis }
        let reihenfolge = Dictionary(liste.enumerated().map { ($1.id, $0) }, uniquingKeysWith: { a, _ in a })
        func vorher(_ a: Kachel, _ b: Kachel) -> Bool {
            (reihenfolge[a.id] ?? 0) < (reihenfolge[b.id] ?? 0)
        }
        // Zusammenhängende Gruppen (transitiv überlappend) per Durchlauf nach Beginn
        let nachZeit = liste.sorted { a, b in
            if a.beginn != b.beginn { return a.beginn < b.beginn }
            if a.ende != b.ende { return a.ende > b.ende }
            return vorher(a, b)
        }
        var gruppen: [[Kachel]] = []
        var gruppenEnde = Int.min
        for k in nachZeit {
            if gruppen.isEmpty || k.beginn >= gruppenEnde {
                gruppen.append([k])
                gruppenEnde = k.ende
            } else {
                gruppen[gruppen.count - 1].append(k)
                gruppenEnde = max(gruppenEnde, k.ende)
            }
        }
        for g in gruppen {
            if g.count == 1 {
                ergebnis[g[0].id] = Spurlage()
                continue
            }
            let geordnet = g.sorted { a, b in
                if a.rang != b.rang { return a.rang < b.rang }
                if a.beginn != b.beginn { return a.beginn < b.beginn }
                if a.ende - a.beginn != b.ende - b.beginn { return a.ende - a.beginn > b.ende - b.beginn }
                return vorher(a, b)
            }
            // Gleiche Zeit: untereinander geteilt, wenn jeder Block mindestens drei Zeilen hoch bleibt
            if let hoehe, g.count <= 3, !g.contains(where: { $0.feld }),
               g.allSatisfy({ $0.beginn == g[0].beginn && $0.ende == g[0].ende }) {
                let n = CGFloat(g.count)
                if (hoehe(g[0]) - (n - 1) * 2) / n >= stapelMindestHoehe {
                    for (i, k) in geordnet.enumerated() {
                        ergebnis[k.id] = Spurlage(spur: 0, spuren: 1, spanne: 1, stapel: g.count, stapelIndex: i)
                    }
                    continue
                }
            }
            var spuren: [[Kachel]] = []
            var spurVon: [UUID: Int] = [:]
            for k in geordnet {
                if let s = spuren.firstIndex(where: { spur in spur.allSatisfy { !$0.ueberlappt(k) } }) {
                    spuren[s].append(k)
                    spurVon[k.id] = s
                } else {
                    spuren.append([k])
                    spurVon[k.id] = spuren.count - 1
                }
            }
            let n = spuren.count
            for k in g {
                let s = spurVon[k.id] ?? 0
                var spanne = 1
                while s + spanne < n && spuren[s + spanne].allSatisfy({ !$0.ueberlappt(k) }) { spanne += 1 }
                ergebnis[k.id] = Spurlage(spur: s, spuren: n, spanne: spanne)
            }
        }
        return ergebnis
    }

    /// Kacheln einer Tagesspalte anordnen.
    /// - Alle gleichzeitigen Blöcke (Kopplung, Gruppen, Vertretung neben Entfall, unterschiedliche Längen) stehen in Spuren
    ///   nebeneinander bzw. bei genau gleicher Zeit untereinander geteilt (`spurenVerteilen`). Nur wenn mehr Spuren nötig sind,
    ///   als bei der Mindestbreite in die Spalte passen, treten die hinteren hinter einen Zählstreifen.
    /// - Veranstaltungsfeld: auf breiten Spalten (iPad, Tagesansicht) Feld links, betroffene Stunden rechts daneben; auf schmalen
    ///   Spalten (iPhone-Woche) treten sie hinter das Feld zurück (Zählstreifen, Aufsichten unter dem Feld ausgeblendet).
    /// - `hoehe`: Höhe einer Kachel in Punkten (für das Teilen untereinander); nil = immer nebeneinander.
    static func anordnen(_ alle: [Kachel], spaltenBreite: CGFloat, tagesansicht: Bool,
                         hoehe: ((Kachel) -> CGFloat)? = nil) -> Tagesbelegung {
        var belegung = Tagesbelegung()
        let pausen = alle.filter { $0.inPause }
        let stunden = alle.filter { !$0.inPause }
        let luecke: CGFloat = tagesansicht ? 4 : 2
        let mindestBreite = tagesansicht ? spurMindestBreiteTag : spurMindestBreiteWoche

        func passen(_ breite: CGFloat) -> Int {
            max(1, Int((breite + luecke) / (mindestBreite + luecke)))
        }
        func aufsichtenIm(_ f: Kachel) -> [Kachel] {
            pausen.filter { !$0.vorErsterStunde && $0.art == .aufsicht && $0.beginn >= f.beginn && $0.ende <= f.ende }
        }

        // Veranstaltungsfelder
        let felder = stunden.filter { $0.feld }
        let feldDaneben = tagesansicht || spaltenBreite >= 124
        var feldLinks: Set<UUID> = []
        var seitlich: Set<UUID> = []
        var feldVerdeckt: [UUID: Kachel] = [:]
        var verdeckteAufsichten: Set<UUID> = []
        var seitlicheAufsichten: Set<UUID> = []
        for f in felder where !seitlich.contains(f.id) && feldVerdeckt[f.id] == nil {
            let treffer = stunden.filter { !$0.feld && $0.ueberlappt(f) && !seitlich.contains($0.id) && feldVerdeckt[$0.id] == nil }
            let aufs = aufsichtenIm(f)
            if treffer.isEmpty && aufs.isEmpty { continue }
            if feldDaneben {
                feldLinks.insert(f.id)
                for t in treffer { seitlich.insert(t.id) }
                for a in aufs { seitlicheAufsichten.insert(a.id) }
            } else {
                for t in treffer { feldVerdeckt[t.id] = f }
                for a in aufs { verdeckteAufsichten.insert(a.id) }
            }
        }

        var verdraengt: [(karte: Kachel, vorne: Kachel)] = []
        var vorneIds: Set<UUID> = []

        /// Spuren in einem Bereich (Anteile der Spalte) setzen; überzählige Spuren hinter einen Zählstreifen.
        func setzen(_ liste: [Kachel], von: CGFloat, bis: CGFloat) {
            let lagen = spurenVerteilen(liste, hoehe: hoehe)
            let grenze = passen(spaltenBreite * (bis - von))
            for k in liste {
                let s = lagen[k.id] ?? Spurlage()
                var lage = Kachellage(kachel: k)
                if s.spuren > grenze {
                    if s.spur >= grenze {
                        // Vorne steht die Kachel der ersten Spur (bzw. die erste sichtbare), die zur selben Zeit liegt
                        let vorne = liste.first { o in
                            let so = lagen[o.id] ?? Spurlage()
                            return so.spur == 0 && o.ueberlappt(k)
                        } ?? liste.first { o in (lagen[o.id]?.spur ?? 0) < grenze && o.ueberlappt(k) }
                        if let vorne {
                            verdraengt.append((k, vorne))
                            vorneIds.insert(vorne.id)
                            continue
                        }
                    }
                    lage = spurLage(lage, n: grenze, i: min(s.spur, grenze - 1), von: von, bis: bis, luecke: luecke)
                    lage.spanne = max(1, min(s.spanne, grenze - lage.spur))
                } else {
                    lage = spurLage(lage, n: s.spuren, i: s.spur, von: von, bis: bis, luecke: luecke)
                    lage.spanne = s.spanne
                    lage.stapel = s.stapel
                    lage.stapelIndex = s.stapelIndex
                }
                belegung.lagen.append(lage)
            }
        }

        // Feld links (halbe Spalte), betroffene Stunden rechts daneben in eigenen Spuren
        for f in felder where feldLinks.contains(f.id) {
            belegung.lagen.append(spurLage(Kachellage(kachel: f), n: 2, i: 0, von: 0, bis: 1, luecke: luecke))
        }
        let seitliche = stunden.filter { seitlich.contains($0.id) }
        if !seitliche.isEmpty { setzen(seitliche, von: 0.5, bis: 1) }
        // Hinter einem Feld verdeckte Stunden (schmale Spalten)
        for k in stunden {
            guard let f = feldVerdeckt[k.id] else { continue }
            verdraengt.append((k, f))
            vorneIds.insert(f.id)
        }
        // Alle übrigen Stunden (auch Felder ohne betroffene Stunden): Spuren über die ganze Spalte
        let uebrige = stunden.filter { !feldLinks.contains($0.id) && !seitlich.contains($0.id) && feldVerdeckt[$0.id] == nil }
        setzen(uebrige, von: 0, bis: 1)

        for i in belegung.lagen.indices where vorneIds.contains(belegung.lagen[i].kachel.id) {
            belegung.lagen[i].mitZaehler = true
        }
        // Je vorderer Kachel ein Streifen; überlappende Streifen verschmelzen
        var roh: [Zaehlstreifen] = []
        for (karte, vorne) in verdraengt {
            if let j = roh.firstIndex(where: { $0.vorne.id == vorne.id }) {
                roh[j].kacheln.append(karte)
                roh[j].von = min(roh[j].von, karte.von)
                roh[j].bis = max(roh[j].bis, karte.bis)
            } else {
                roh.append(Zaehlstreifen(von: karte.von, bis: karte.bis, kacheln: [karte], vorne: vorne))
            }
        }
        roh.sort { $0.von < $1.von }
        for st in roh {
            if let letzter = belegung.streifen.indices.last, st.von <= belegung.streifen[letzter].bis {
                belegung.streifen[letzter].bis = max(belegung.streifen[letzter].bis, st.bis)
                for k in st.kacheln where !belegung.streifen[letzter].kacheln.contains(where: { $0.id == k.id }) {
                    belegung.streifen[letzter].kacheln.append(k)
                }
            } else {
                belegung.streifen.append(st)
            }
        }
        for p in pausen where !verdeckteAufsichten.contains(p.id) {
            if seitlicheAufsichten.contains(p.id) {
                belegung.lagen.append(spurLage(Kachellage(kachel: p), n: 1, i: 0, von: 0.5, bis: 1, luecke: luecke))
            } else {
                belegung.lagen.append(Kachellage(kachel: p))
            }
        }
        return belegung
    }

    /// Lage einer Spur: Bereich von…bis (Anteile der Spalte), n Spuren, Luft dazwischen wird von der Breite abgezogen.
    private static func spurLage(_ lage: Kachellage, n: Int, i: Int, von: CGFloat, bis: CGFloat, luecke: CGFloat) -> Kachellage {
        var l = lage
        let anzahl = max(1, n)
        l.spur = i
        l.spuren = anzahl
        l.links = von
        l.breite = bis - von
        l.luecke = anzahl > 1 ? luecke : 0
        return l
    }

    /// Stunden, die zusammen mit dieser Kachel in derselben Zeit liegen (für die Reiter in den Details), nach Rang.
    static func parallele(_ k: Kachel, in alle: [Kachel]) -> [Karte] {
        let gruppe = alle.filter { !$0.inPause && $0.karte != nil && $0.ueberlappt(k) && $0.art != .ganztagTermin }
            .sorted { a, b in
                if a.rang != b.rang { return a.rang < b.rang }
                return a.von < b.von
            }
        let karten = gruppe.compactMap(\.karte)
        return karten.isEmpty ? k.karte.map { [$0] } ?? [] : karten
    }
}
