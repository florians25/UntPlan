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

    /// Zeigt Text beim Antippen (alles außer der Ganztags-Pille).
    var antippbar: Bool { art != .ganztagTermin }

    var dauer: Int { max(1, bis - von + 1) }

    func ueberlappt(_ o: Kachel) -> Bool {
        von <= o.bis && o.von <= bis
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
    /// Platz rechts für den Zählstreifen
    var mitZaehler = false

    var id: UUID { kachel.id }
}

/// Zählstreifen rechts neben einer Kachel: so viele Stunden liegen dahinter (Wochenansicht, zu wenig Platz).
struct Zaehlstreifen: Identifiable {
    let id = UUID()
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
        let karten = plan.karten.filter { $0.datum == tag.datum }
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
            kacheln.append(contentsOf: aufsichten.map { aufsichtKachel($0, raster: raster) })
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

    /// Ursprungswerte geänderter Einträge (ohne Doppelte).
    private static func alte(_ w: [Wert]) -> [String] {
        var ergebnis: [String] = []
        for x in w where x.neu {
            if let a = x.alt, !a.isEmpty, !ergebnis.contains(where: { gleich($0, a) }) { ergebnis.append(a) }
        }
        return ergebnis
    }

    /// Eine Stundenkarte als Kachel (nil = nichts anzuzeigen).
    static func ausKarte(_ k: Karte, typ: String, ich: String, raster: [Rasterzeit]) -> Kachel? {
        let lage = zeitraum(k, raster: raster)
        var kachel = Kachel(karte: k, art: .normal, datum: k.datum, von: k.von, bis: max(k.bis, k.von),
                            beginn: lage.beginn, ende: lage.ende)
        kachel.balkenHex = k.farbe
        kachel.info = (!k.text.isEmpty && !istEntfaelltText(k.text)) || !k.notizSchueler.isEmpty || !k.notizLehrer.isEmpty
        kachel.hausaufgabe = k.hausaufgabeFaellig

        // Veranstaltung: Feld über den ganzen Zeitraum
        if k.status == "veranstaltung" || (k.status == "geaendert" && k.unterricht == nil && k.fach.isEmpty && !k.text.isEmpty) {
            kachel.art = .veranstaltung
            kachel.feld = true
            kachel.titel = k.text.isEmpty ? k.art : k.text
            kachel.zeilen = veranstaltungsZeilen(k, typ: typ, ich: ich, anlass: kachel.titel)
            kachel.info = false
            kachel.rang = 2
            return kachel.zeilen.isEmpty ? nil : kachel
        }

        // Bereitschaft
        if istBereitschaft(k) {
            let titel = k.fach.isEmpty ? "Bereitschaft" : k.fach
            kachel.titel = titel
            kachel.info = !k.text.isEmpty && !istEntfaelltText(k.text)
            kachel.hausaufgabe = false
            if istBereitschaftAbwesend(k) {
                kachel.art = .bereitschaftAbwesend
                var zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)], fett: true)]
                var lehrkraefte = k.lehrer.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
                if lehrkraefte.isEmpty && !ich.isEmpty && typ == "lehrer" { lehrkraefte = [ich] }
                if !lehrkraefte.isEmpty {
                    zeilen.append(Kachelzeile(schluessel: "lehrer", teile: liste(lehrkraefte, stil: .weg, trenner: ", ")))
                }
                kachel.zeilen = zeilen
                kachel.rang = 3
            } else {
                kachel.art = .bereitschaft
                kachel.zeilen = [Kachelzeile(schluessel: "fach", teile: [Kachelteil(text: titel)])]
            }
            return kachel
        }

        // Geänderte Werte
        var geaendert: Set<String> = []
        if k.klassen.contains(where: { $0.neu }) { geaendert.insert("klasse") }
        if k.lehrer.contains(where: { $0.neu }) { geaendert.insert("lehrer") }
        if k.raeume.contains(where: { $0.neu || (!($0.alt ?? "").isEmpty && !gleich($0.alt ?? "", $0.k)) }) { geaendert.insert("raum") }
        if let alt = k.fachAlt, !alt.isEmpty, !gleich(alt, k.fach) { geaendert.insert("fach") }

        let offen = (k.status == "offen" || k.status == "abwesend")
            && k.lehrer.contains { $0.neu && $0.k.isEmpty && !($0.alt ?? "").isEmpty }
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
        // Raumbuchung ohne weitere Änderung: keine Vertretung, die Kachel bleibt regulär
        if k.istRaumbuchung && (kachel.art == .aenderung || kachel.art == .termin) && geaendert.subtracting(["raum"]).isEmpty && !offen {
            kachel.art = .normal
            kachel.zusatz = false
        }
        // Online-Unterricht: grün wie eine Änderung, Plakette „Online“
        if k.online && kachel.art == .normal { kachel.art = .aenderung }
        if titel.isEmpty { titel = k.art }
        kachel.titel = titel

        // Im eigenen Plan: Stunde wird von jemand anderem übernommen (oder ist noch offen) → läuft ohne mich
        let jetztLehrer = werte(k.lehrer).joined(separator: ",").uppercased()
        let alteLehrer = alte(k.lehrer).joined(separator: ",").uppercased()
        let abgegebenAnMich = typ != "klasse" && geaendert.contains("lehrer") && !ich.isEmpty && alteLehrer == ich && jetztLehrer != ich
        let verlegtZiel = k.art.lowercased().contains("verleg") && kachel.art == .aenderung
        let alleKlassenAbwesend = !k.klassen.isEmpty && !k.abwesendeKlassen.isEmpty
            && werte(k.klassen).allSatisfy { kl in k.abwesendeKlassen.contains { gleich($0, kl) } }
        let freigegeben = typ != "klasse" && (offen || abgegebenAnMich || alleKlassenAbwesend) && !verlegtZiel
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
    private static func veranstaltungsZeilen(_ k: Karte, typ: String, ich: String, anlass: String) -> [Kachelzeile] {
        let alle = werte(k.lehrer)
        let klassen = werte(k.klassen)
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
        let raeume = werte(k.raeume)
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

        var ergebnis: [Kachelzeile] = []
        for (i, schluessel) in reihenfolge.enumerated() {
            var zeile = Kachelzeile(schluessel: schluessel, teile: [], fett: i == 0)
            var hatListe = false
            var geaendertWert = geaendert.contains(schluessel)
            switch schluessel {
            case "lehrer":
                if k.lehrer.count > 1 {
                    // Kopplung: jedes Kürzel mit eigenem Zustand
                    var teile: [Kachelteil] = []
                    for w in k.lehrer {
                        if w.neu && w.k.isEmpty {
                            if let a = w.alt, !a.isEmpty { teile.append(Kachelteil(text: a, stil: .weg)) }
                        } else if w.neu && !(w.alt ?? "").isEmpty {
                            teile.append(Kachelteil(text: w.k, stil: .plakette))
                        } else if !w.k.isEmpty {
                            teile.append(Kachelteil(text: w.k))
                        }
                    }
                    if teile.count > 1 {
                        for j in 0..<(teile.count - 1) { teile[j].trenner = ", " }
                        zeile.teile = teile
                        hatListe = true
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
            case "klasse":
                let namen = werte(k.klassen)
                let abwesend = k.abwesendeKlassen
                if namen.count > 1 || !abwesend.isEmpty {
                    var teile = namen.map { n -> Kachelteil in
                        Kachelteil(text: n, stil: abwesend.contains { gleich($0, n) } ? .weg : .text)
                    }
                    // Im Klassenplan steht nur, wer mit dabei ist – die eigene Klasse nur, wenn sie fehlt
                    if typ == "klasse" && !ich.isEmpty && namen.count > 1 {
                        teile = teile.filter { $0.text.uppercased() != ich || $0.stil == .weg }
                    }
                    if !teile.isEmpty {
                        for j in 0..<(teile.count - 1) { teile[j].trenner = ", " }
                        zeile.teile = teile
                        hatListe = namen.count > 1 || teile.contains { $0.stil == .weg }
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
                    // Mehrere Räume: jeder für sich; hervorgehoben nur, was neu hinzugekommen ist
                    let neue = k.raeume.filter { $0.neu && !$0.k.isEmpty && !gleich($0.alt ?? "", $0.k) }.map { $0.k.uppercased() }
                    let vorherGross = vorher.map { $0.uppercased() }
                    zeile.teile = jetzt.enumerated().map { j, r in
                        let neu = geaendertWert && (!neue.isEmpty ? neue.contains(r.uppercased())
                                                                  : (vorherGross.isEmpty || !vorherGross.contains(r.uppercased())))
                        return Kachelteil(text: r, stil: neu ? .plakette : .text, trenner: j < jetzt.count - 1 ? " " : "")
                    }
                    hatListe = true
                } else {
                    zeile.teile = wertTeile(jetzt: jetzt.joined(separator: ", "), vorher: vorher.joined(separator: ", "),
                                            geaendert: geaendertWert && art != .klausur || (geaendertWert && k.istRaumbuchung))
                }
                // Online-Stunde: Plakette „Online“ in der Raumzeile
                if k.online && art != .entfall && !jetzt.contains(where: { gleich($0, "Online") }) {
                    if let letzter = zeile.teile.indices.last { zeile.teile[letzter].trenner = " " }
                    zeile.teile.append(Kachelteil(text: "Online", stil: .plakette))
                    hatListe = true
                }
            }
            if zeile.teile.isEmpty || zeile.text.trimmingCharacters(in: .whitespaces).isEmpty { continue }

            // Vertretungskachel im Lehrerplan: das eigene Kürzel steht als Plakette (ich halte diese Stunde)
            if schluessel == letzte && !hatListe && art == .aenderung && typ == "lehrer" && !klassenAbwesend
                && !(!geaendert.isEmpty && geaendert.allSatisfy { $0 == "raum" })
                && zeile.text.uppercased() == ich && !ich.isEmpty {
                if zeile.teile.count == 1 && zeile.teile[0].stil == .text { zeile.teile[0].stil = .plakette }
                geaendertWert = true
            }
            // Die letzte Zeile entfällt, wenn sie nur das betrachtete Element wiederholt
            let wiederholt = !ich.isEmpty && zeile.text.uppercased() == ich && zeile.nurText && !hatListe
            if schluessel == letzte && wiederholt && !geaendertWert { continue }
            if typ == "raum" && schluessel == "raum" && wiederholt && !geaendertWert { continue }
            ergebnis.append(zeile)
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
    static func aufsichtKachel(_ a: Aufsicht, raster: [Rasterzeit]) -> Kachel {
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
        var e = ende ?? (b + 15)
        if e <= b { e = b + 15 }
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
        k.balkenHex = ""
        return k
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

    /// Kacheln einer Tagesspalte anordnen.
    /// - Wochenansicht: parallele Stunden nebeneinander, solange der Platz reicht (mind. 62 pt je Kachel); sonst steht vorne,
    ///   was stattfindet, und ein Zählstreifen zeigt die Zahl der verdeckten Stunden. Aufsichten unter einem Feld entfallen.
    /// - Tagesansicht: Feld links, betroffene Stunden rechts daneben; sonst alle nebeneinander.
    static func anordnen(_ alle: [Kachel], spaltenBreite: CGFloat, tagesansicht: Bool) -> Tagesbelegung {
        var belegung = Tagesbelegung()
        let pausen = alle.filter { $0.inPause }
        var stunden = alle.filter { !$0.inPause }

        // Spuren wie in der PWA: nach Rang, dann Beginn; zusammenhängende Ketten; in Spalten packen
        stunden.sort { a, b in
            if a.rang != b.rang { return a.rang < b.rang }
            if a.von != b.von { return a.von < b.von }
            return a.dauer > b.dauer
        }
        var spur: [UUID: Int] = [:]
        var spuren: [UUID: Int] = [:]
        var gruppen: [[Kachel]] = []
        for k in stunden {
            let treffer = gruppen.indices.filter { gi in gruppen[gi].contains { $0.ueberlappt(k) } }
            if treffer.isEmpty {
                gruppen.append([k])
                continue
            }
            var g = gruppen[treffer[0]]
            g.append(k)
            for gi in treffer.dropFirst() { g.append(contentsOf: gruppen[gi]) }
            for gi in treffer.dropFirst().reversed() { gruppen.remove(at: gi) }
            let reihenfolge = Dictionary(uniqueKeysWithValues: stunden.enumerated().map { ($1.id, $0) })
            g.sort { (reihenfolge[$0.id] ?? 0) < (reihenfolge[$1.id] ?? 0) }
            gruppen[treffer[0]] = g
        }
        for g in gruppen {
            var spalten: [[Kachel]] = []
            for k in g {
                if let s = spalten.firstIndex(where: { spalte in spalte.allSatisfy { !$0.ueberlappt(k) } }) {
                    spalten[s].append(k)
                    spur[k.id] = s
                } else {
                    spalten.append([k])
                    spur[k.id] = spalten.count - 1
                }
            }
            for k in g { spuren[k.id] = spalten.count }
        }

        let felder = stunden.filter { $0.feld }
        func aufsichtenIm(_ f: Kachel) -> [Kachel] {
            pausen.filter { !$0.vorErsterStunde && $0.art == .aufsicht && $0.beginn >= f.beginn && $0.ende <= f.ende }
        }

        if tagesansicht {
            var seitlich: [UUID: (spur: Int, spuren: Int)] = [:]
            var feldMitTreffern: Set<UUID> = []
            for f in felder {
                let treffer = stunden.filter { !$0.feld && $0.ueberlappt(f) }
                let aufs = aufsichtenIm(f)
                if treffer.isEmpty && aufs.isEmpty { continue }
                feldMitTreffern.insert(f.id)
                for (k, s) in gruppenSpuren(treffer) { seitlich[k] = s }
                for a in aufs { seitlich[a.id] = (0, 1) }
            }
            for k in stunden {
                var lage = Kachellage(kachel: k)
                if k.feld && feldMitTreffern.contains(k.id) {
                    lage = spurLage(lage, n: 2, i: 0, von: 0, bis: 1, luecke: 4)
                } else if let s = seitlich[k.id] {
                    lage = spurLage(lage, n: s.spuren, i: s.spur, von: 0.5, bis: 1, luecke: 4)
                } else {
                    lage = spurLage(lage, n: spuren[k.id] ?? 1, i: spur[k.id] ?? 0, von: 0, bis: 1, luecke: 4)
                }
                belegung.lagen.append(lage)
            }
            for p in pausen {
                var lage = Kachellage(kachel: p)
                if seitlich[p.id] != nil { lage = spurLage(lage, n: 1, i: 0, von: 0.5, bis: 1, luecke: 4) }
                belegung.lagen.append(lage)
            }
            return belegung
        }

        // Wochenansicht
        let passt = max(1, Int(spaltenBreite / 62))
        var verdraengt: [(karte: Kachel, vorne: Kachel)] = []
        var vorneIds: Set<UUID> = []
        // Veranstaltungsfeld: auf breiten Spalten (iPad) Feld links, entfallende Stunden und Aufsichten rechts daneben
        // (halbe Spalte); auf schmalen Spalten (iPhone) treten sie hinter das Feld zurück (Zählstreifen, Aufsichten ausgeblendet).
        var feldLinks: Set<UUID> = []
        var feldSeite: [UUID: (spur: Int, spuren: Int)] = [:]
        var feldVerdeckt: [UUID: Kachel] = [:]
        var verdeckteAufsichten: Set<UUID> = []
        for f in felder {
            let treffer = stunden.filter { !$0.feld && $0.ueberlappt(f) && feldSeite[$0.id] == nil && feldVerdeckt[$0.id] == nil }
            let aufs = aufsichtenIm(f)
            if passt >= 2 {
                if treffer.isEmpty && aufs.isEmpty { continue }
                feldLinks.insert(f.id)
                for (k, s) in gruppenSpuren(treffer) { feldSeite[k] = s }
                for a in aufs { feldSeite[a.id] = (0, 1) }
            } else {
                for t in treffer { feldVerdeckt[t.id] = f }
                for a in aufs { verdeckteAufsichten.insert(a.id) }
            }
        }
        for k in stunden {
            if feldLinks.contains(k.id) {
                belegung.lagen.append(spurLage(Kachellage(kachel: k), n: 2, i: 0, von: 0, bis: 1, luecke: 2))
                continue
            }
            if let s = feldSeite[k.id] {
                belegung.lagen.append(spurLage(Kachellage(kachel: k), n: s.spuren, i: s.spur, von: 0.5, bis: 1, luecke: 2))
                continue
            }
            if let f = feldVerdeckt[k.id] {
                verdraengt.append((k, f))
                vorneIds.insert(f.id)
                continue
            }
            let n = spuren[k.id] ?? 1
            let i = spur[k.id] ?? 0
            if n > passt && i > 0 && !k.feld {
                if let vorne = stunden.first(where: { (spur[$0.id] ?? 0) == 0 && $0.ueberlappt(k) }) {
                    verdraengt.append((k, vorne))
                    vorneIds.insert(vorne.id)
                    continue
                }
            }
            var lage = Kachellage(kachel: k)
            if n > 1 && n <= passt {
                lage = spurLage(lage, n: n, i: i, von: 0, bis: 1, luecke: 2)
            } else if n > 1 {
                let mehr = stunden.contains { $0.id != k.id && (spur[$0.id] ?? 0) > 0 && $0.ueberlappt(k) }
                if mehr { lage.mitZaehler = true }
            }
            belegung.lagen.append(lage)
        }
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
            if feldSeite[p.id] != nil {
                belegung.lagen.append(spurLage(Kachellage(kachel: p), n: 1, i: 0, von: 0.5, bis: 1, luecke: 2))
            } else {
                belegung.lagen.append(Kachellage(kachel: p))
            }
        }
        return belegung
    }

    /// Überlappende Kacheln innerhalb einer Liste durchnummerieren.
    private static func gruppenSpuren(_ liste: [Kachel]) -> [UUID: (spur: Int, spuren: Int)] {
        var gruppen: [[Kachel]] = []
        for k in liste.sorted(by: { ($0.von, -$0.dauer) < ($1.von, -$1.dauer) }) {
            if let gi = gruppen.firstIndex(where: { g in g.contains { $0.ueberlappt(k) } }) {
                gruppen[gi].append(k)
            } else {
                gruppen.append([k])
            }
        }
        var ergebnis: [UUID: (spur: Int, spuren: Int)] = [:]
        for g in gruppen {
            for (i, k) in g.enumerated() { ergebnis[k.id] = (i, g.count) }
        }
        return ergebnis
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
