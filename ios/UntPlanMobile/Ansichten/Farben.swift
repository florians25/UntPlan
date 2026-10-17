import SwiftUI

extension Color {
    /// Farbe aus "#RRGGBB" (leer oder ungültig = nil).
    init?(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let wert = UInt32(s, radix: 16) else { return nil }
        self.init(hexWert: wert)
    }

    init(hexWert: UInt32, deckkraft: Double = 1) {
        self.init(.sRGB,
                  red: Double((hexWert >> 16) & 0xFF) / 255,
                  green: Double((hexWert >> 8) & 0xFF) / 255,
                  blue: Double(hexWert & 0xFF) / 255,
                  opacity: deckkraft)
    }

    /// Farbe, die sich nach hellem oder dunklem Erscheinungsbild richtet.
    init(hell: UInt32, dunkel: UInt32, deckkraftHell: Double = 1, deckkraftDunkel: Double = 1) {
        self.init(uiColor: UIColor { merkmale in
            let dunkelModus = merkmale.userInterfaceStyle == .dark
            let wert = dunkelModus ? dunkel : hell
            return UIColor(red: CGFloat((wert >> 16) & 0xFF) / 255,
                           green: CGFloat((wert >> 8) & 0xFF) / 255,
                           blue: CGFloat(wert & 0xFF) / 255,
                           alpha: CGFloat(dunkelModus ? deckkraftDunkel : deckkraftHell))
        })
    }
}

/// Farben wie in UntPlanWeb und UntPlanMobile (PWA, app.css), jeweils hell und dunkel.
enum Farben {
    // Akzente
    static let akzent = Color(hexWert: 0xF56B2A)
    /// Ladebalken: kräftiges Orange
    static let ladebalken = Color(hexWert: 0xFF6A13)
    static let akzentSanft = Color(hell: 0xFFF1E2, dunkel: 0x3A2414)
    static let orange = Color(hexWert: 0xF07C00)
    static let heute = Color(hexWert: 0xEF5A2A)
    static let jetztLinie = Color(hexWert: 0xE65D35)
    static let rot = Color(hexWert: 0xF40041)

    // Text
    static let tinte = Color(hell: 0x253746, dunkel: 0xF2F4F6)
    static let tinte2 = Color(hell: 0x48606F, dunkel: 0xC9D1D8)
    static let gedaempft = Color(hell: 0x6B7A85, dunkel: 0x8D949C)
    static let rinne = Color(hell: 0x65717A, dunkel: 0x9AA1A8)
    static let titelGrau = Color(hell: 0x5D727F, dunkel: 0x93A6B2)
    /// Großer Text im Kopfblock der Stundendetails (Original: fast schwarz)
    static let detailText = Color(hell: 0x1F2328, dunkel: 0xF2F4F6)
    /// Grauer Text in den Stundendetails (Raum, Lehrkraft, Untertitel) – eine Stufe dunkler als `gedaempft`
    static let detailGrau = Color(hell: 0x55636E, dunkel: 0xA9B0B8)
    /// Trennstrich „|“ im Kopfblock (Original: hellgrau)
    static let detailTrenner = Color(hell: 0x9AA4AD, dunkel: 0x6E7881)
    static let abschnitt = Color(hexWert: 0x8B9AA5)
    static let pfeil = Color(hell: 0x9AA7B1, dunkel: 0x6E7881)

    // Flächen
    static let leiste = Color(hell: 0xFFFFFF, dunkel: 0x000000)
    static let grund = Color(hell: 0xEEF0F3, dunkel: 0x000000)
    static let flaeche = Color(hell: 0xFFFFFF, dunkel: 0x1C1C1E)
    static let blatt = Color(hell: 0xFFFFFF, dunkel: 0x1C1C1E)
    static let knopf = Color(hell: 0xF1F3F5, dunkel: 0x2A2A2E)
    static let suchfeld = Color(hell: 0xF1F3F5, dunkel: 0x1C1C1E)
    static let avatar = Color(hexWert: 0x8B9AA5)
    static let zahlPlakette = Color(hell: 0x7B8A96, dunkel: 0x636B73)

    // Linien
    static let linie = Color(hell: 0xCCD3D9, dunkel: 0x34343A)
    static let trennlinie = Color(hell: 0xE4E7EA, dunkel: 0x38383A)
    static let rasterOben = Color(hell: 0xF2F4F7, dunkel: 0x1C1C1E)
    static let rasterUnten = Color(hell: 0xE7E9EF, dunkel: 0x1C1C1E)

    // Karten im Wochenraster
    static let karte = Color(hell: 0xFFFFFF, dunkel: 0x5E7383)
    static let karteVergangen = Color(hell: 0xF2F4F5, dunkel: 0x323D45)
    static let karteBalken = Color(hell: 0xBFC7D2, dunkel: 0xBFC7CE)
    static let karteText = Color(hell: 0x253746, dunkel: 0xF4F6F8)
    static let kartenSchatten = Color(hell: 0x1F2F3D, dunkel: 0x000000, deckkraftHell: 0.30, deckkraftDunkel: 0)
    static let kartenSchattenWeich = Color(hell: 0x1F2F3D, dunkel: 0x000000, deckkraftHell: 0.22, deckkraftDunkel: 0)

    // Änderung grün
    static let aenderungHintergrund = Color(hell: 0xE3FFF2, dunkel: 0x005840)
    static let aenderungVergangen = Color(hell: 0xE4FFF2, dunkel: 0x323D45)
    static let aenderungRand = Color(hell: 0x019169, dunkel: 0x20C0A2)
    static let gruen = Color(hell: 0x0B7D65, dunkel: 0x22C0A1)
    static let gruenSchrift = Color(hell: 0xFFFFFF, dunkel: 0x05201A)

    // Entfall rot
    static let entfallHintergrund = Color(hell: 0xFFF3F2, dunkel: 0x81012F)
    static let entfallVergangen = Color(hell: 0xFFF4F3, dunkel: 0x323D45)
    static let entfallRand = Color(hell: 0xE00047, dunkel: 0xDF0047)

    // Prüfung
    static let pruefungHintergrund = Color(hell: 0xFFF6D5, dunkel: 0x4A3F22)
    static let pruefungVergangen = Color(hell: 0xFEF7D7, dunkel: 0x323D45)
    static let pruefungRand = Color(hell: 0xA1512C, dunkel: 0xD9962A)

    // Veranstaltung, Ferien
    static let veranstaltungHintergrund = Color(hell: 0xDFFEF5, dunkel: 0x054035)
    static let veranstaltungBand = Color(hell: 0xD5EBFE, dunkel: 0x8A9BAF)
    static let ferienHintergrund = Color(hell: 0xD5EBFE, dunkel: 0x6B83C4)
    static let ferienLinie = Color(hell: 0xB3D6F6, dunkel: 0x6A82BF)
    static let ferienTinte = Color(hell: 0x2B3F52, dunkel: 0x161A24)

    /// Runde Pill „Entschuldigen“ beim Wischen (Schüler- und Abwesenheitslisten)
    static let entschuldigen = Color(hell: 0x2E7BCF, dunkel: 0x3A86D6)

    // Marker oben rechts auf den Karten (wie im Web: Prüfung gelber Kreis, Entfall rotes Dreieck, Änderung/Zusatz grün)
    static let markerGelb = Color(hell: 0xF7C600, dunkel: 0xF2C200)
    static let markerRot = Color(hell: 0xF03E3E, dunkel: 0xE5484D)
    static let markerGruen = Color(hell: 0x1F9D55, dunkel: 0x22A866)

    // Info-Zeichen auf den Karten
    static let infoFeld = Color(hell: 0xF2F4F6, dunkel: 0x2B3138)
    static let infoScheibe = Color(hell: 0x828E9B, dunkel: 0x9AA6B2)
    static let infoZeichen = Color(hell: 0xFFFFFF, dunkel: 0x2B3138)

    // Köpfe der Infokarten
    static let nachrichtKopf = Color(hexWert: 0x4C9BD8)
    static let veranstaltungKopf = Color(hexWert: 0x3FAE6A)
    static let hinweisKopf = Color(hexWert: 0xF07C00)

    // Untere Leiste (Glas)
    static let leisteText = Color(hell: 0x2B333A, dunkel: 0xC9CED3)
    static let leisteAktiv = Color(hell: 0x253746, dunkel: 0xFFFFFF)
    /// Helles Feld hinter dem aktiven Reiter der oberen Kapsel (iPad)
    static let reiterAktiv = Color(hell: 0xF4ECE7, dunkel: 0xFFFFFF, deckkraftHell: 0.95, deckkraftDunkel: 0.13)

    // Segment-Schalter und Reiter (Vorlage seite-8, 133)
    static let segmentGrund = Color(hell: 0xEDEFF2, dunkel: 0x2C2C2E)
    static let segmentAktiv = Color(hell: 0xFFFFFF, dunkel: 0x636366)
    static let segmentSchatten = Color(hell: 0x1F2F3D, dunkel: 0x000000, deckkraftHell: 0.16, deckkraftDunkel: 0.35)
    /// Statuspille „Prüfung“/„Veranstaltung“ im Kopf der Stundendetails (Web: #e6e9ed / #4a5662)
    static let pilleFlaeche = Color(hell: 0xE6E9ED, dunkel: 0x3A3F45)
    static let pilleText = Color(hell: 0x4A5662, dunkel: 0xD3D9DF)
    /// Statuspille „PRÜFUNG“/„VERANSTALTUNG“ rechts neben dem Fach (Vorlage 140: graue Kapsel, weiße Großbuchstaben)
    static let statusPilleFlaeche = Color(hell: 0x8E99A3, dunkel: 0x6E7881)
    /// Zeile der Reiter (mehrere Stunden zur selben Zeit) in den Stundendetails
    static let reiterGrund = Color(hell: 0xF3F5F7, dunkel: 0x242427)
    /// Unterstreichung des aktiven Reiters: dunkelgrau im hellen, weiß im dunklen Modus
    static let reiterStrich = Color(hell: 0x3A4550, dunkel: 0xFFFFFF)

    // Raumbuchung (gelb-orangener Rand). Raumänderungen erscheinen grün wie eine Änderung (aenderungHintergrund/-Rand).
    static let raumRand = Color(hell: 0xE9A23B, dunkel: 0xF0B04A)
    /// Kopfblock der Stundendetails bei Entfall (Original: hellgrau #dfe1e6) und roter Balken „ENTFÄLLT“
    static let detailEntfallFlaeche = Color(hell: 0xDFE1E6, dunkel: 0x2C2F36)
    static let detailEntfallBalken = Color(hexWert: 0xFF0000)
    // Abwesenheit auf der Startseite (graue Fläche)
    static let abwesendFlaeche = Color(hell: 0x868F99, dunkel: 0x4A5058)
    /// Zahl der Abwesenden in den Stundendetails (Vorlage: dunkelrote Scheibe)
    static let abwesendRot = Color(hell: 0xB0063A, dunkel: 0x8E0B3A)
    /// Leiste „Abwesenheiten wurden kontrolliert“ (Schüler*innen)
    static let kontrollLeiste = Color(hell: 0xE8EDF1, dunkel: 0x323D47)
    /// Felder im Abwesenheitsdialog (Grund, Text)
    static let eingabeFeld = Color(hell: 0xF1F3F5, dunkel: 0x323D47)

    // Kacheln im Raster (app.css: .card--event, .corners, .card--supervision, .card--standby, Zählkarte)
    static let karteTextVergangen = Color(hell: 0x3D4F59, dunkel: 0xF4F6F8)
    static let aenderungText = Color(hell: 0x253746, dunkel: 0xEAFFF8)
    static let entfallText = Color(hell: 0x253746, dunkel: 0xFFFFFF)
    static let entfallPlakettenText = Color(hell: 0x48606F, dunkel: 0xFFFFFF)
    static let terminRand = Color(hell: 0x7FD4B6, dunkel: 0x7FD4B6)
    static let terminTitel = Color(hell: 0x00654C, dunkel: 0xF4F6F8)
    static let eckklammer = Color(hell: 0x5F7180, dunkel: 0x8D949C)
    static let aufsichtBalken = Color(hell: 0xD379E9, dunkel: 0xE08CFF)
    static let bereitschaftBalken = Color(hell: 0x01FFFF, dunkel: 0x3EE6FF)
    static let bereitschaftRand = Color(hell: 0x5F7180, dunkel: 0xC3CCD4)
    static let bereitschaftRandVergangen = Color(hell: 0xB7C2CB, dunkel: 0x4A5560)
    static let bereitschaftText = Color(hell: 0x5D727F, dunkel: 0xCFD6DC)
    static let kartenSchattenFlach = Color(hell: 0x1F2F3D, dunkel: 0x000000, deckkraftHell: 0.30, deckkraftDunkel: 0.30)
    static let veranstaltungFlaeche = Color(hell: 0xE0FDF3, dunkel: 0x22352F)
    static let veranstaltungRand = Color(hell: 0xA9DCCB, dunkel: 0x3D6457)
    static let zaehlerFlaeche = Color(hell: 0xF4F6F7, dunkel: 0x323D45)
    static let zaehlerText = Color(hell: 0x3F5666, dunkel: 0xF2F4F6)
    static let zaehlerAenderungFlaeche = Color(hell: 0xE3FFF2, dunkel: 0x20C0A2)
    static let zaehlerAenderungText = Color(hell: 0x00654C, dunkel: 0xEAFFF7)

    /// Fachfarbe für den Balken, sonst Grau.
    static func fach(_ hex: String) -> Color {
        Color(hex: hex) ?? karteBalken
    }
}

/// Darstellung einer Stundenkarte je nach Status (Werte aus app.css).
struct Kartenstil {
    var hintergrund: Color
    var rand: Color?
    var randBreite: CGFloat
    var schatten: Bool
    var durchgestrichen: Bool
    var plakette: String?
    var plaketteFarbe: Color

    /// Farbe der Plakette für geänderte Werte auf der Karte: grün bei Vertretung und Raumänderung, sonst gelb-orange (Buchung, Prüfung).
    var wertFarbe: Color = Farben.gruen

    init(_ karte: Karte, vergangen: Bool = false) {
        // Klausuren und Raumbuchungen nie als Vertretung (grün) darstellen
        if karte.istPruefung && karte.status != "entfall" && karte.status != "abwesend" {
            self.init(hintergrund: vergangen ? Farben.pruefungVergangen : Farben.pruefungHintergrund,
                      rand: Farben.pruefungRand, schatten: false, durchgestrichen: false,
                      plakette: "Prüfung", plaketteFarbe: Farben.pruefungRand)
            return
        }
        if karte.istRaumbuchung && ["buchung", "geaendert", "offen", "regulaer"].contains(karte.status) {
            self.init(hintergrund: vergangen ? Farben.karteVergangen : Farben.karte,
                      rand: Farben.raumRand, schatten: true, durchgestrichen: false,
                      plakette: "Raumbuchung", plaketteFarbe: Farben.raumRand)
            return
        }
        // Online-Unterricht: keine eigene Farbe und kein Label (nur das Online-Zeichen)
        if Kachelbau.istBereitschaftAbwesend(karte) {
            self.init(hintergrund: Color.clear, rand: Farben.eckklammer, schatten: false, durchgestrichen: false,
                      plakette: "Lehrkraft abwesend", plaketteFarbe: Farben.gedaempft)
            return
        }
        switch karte.status {
        case "veranstaltung":
            self.init(hintergrund: Farben.veranstaltungFlaeche,
                      rand: vergangen ? nil : Farben.veranstaltungRand, schatten: false, durchgestrichen: false,
                      plakette: "Veranstaltung", plaketteFarbe: Farben.gruen)
            randBreite = vergangen ? 0 : 1
        case "entfall":
            self.init(hintergrund: vergangen ? Farben.entfallVergangen : Farben.entfallHintergrund,
                      rand: Farben.entfallRand, schatten: false, durchgestrichen: true,
                      plakette: "Entfall", plaketteFarbe: Farben.entfallRand)
        case "geaendert" where Kartenstil.nurRaumGeaendert(karte):
            // Raumänderung: grün wie eine Änderung (nie gelb/braun – das bleibt Prüfungen vorbehalten), neuer Raum als grüne Plakette
            self.init(hintergrund: vergangen ? Farben.aenderungVergangen : Farben.aenderungHintergrund,
                      rand: Farben.aenderungRand, schatten: true, durchgestrichen: false,
                      plakette: "Raumänderung", plaketteFarbe: Farben.gruen)
        case "geaendert":
            self.init(hintergrund: vergangen ? Farben.aenderungVergangen : Farben.aenderungHintergrund,
                      rand: Farben.aenderungRand, schatten: true, durchgestrichen: false,
                      plakette: "Änderung", plaketteFarbe: Farben.gruen)
        case "offen":
            self.init(hintergrund: vergangen ? Farben.aenderungVergangen : Farben.aenderungHintergrund,
                      rand: Farben.aenderungRand, schatten: true, durchgestrichen: false,
                      plakette: "Offen", plaketteFarbe: Farben.gruen)
        case "klausur":
            self.init(hintergrund: vergangen ? Farben.pruefungVergangen : Farben.pruefungHintergrund,
                      rand: Farben.pruefungRand, schatten: false, durchgestrichen: false,
                      plakette: "Prüfung", plaketteFarbe: Farben.pruefungRand)
        case "abwesend":
            self.init(hintergrund: Farben.karteVergangen, rand: Farben.gedaempft, schatten: false, durchgestrichen: false,
                      plakette: "Abgegeben", plaketteFarbe: Farben.gedaempft)
        case "vorgemerkt":
            self.init(hintergrund: Farben.akzentSanft, rand: Farben.akzent, schatten: false, durchgestrichen: false,
                      plakette: "Vorgemerkt", plaketteFarbe: Farben.akzent)
        case "buchung":
            self.init(hintergrund: vergangen ? Farben.karteVergangen : Farben.karte, rand: Farben.raumRand, schatten: true, durchgestrichen: false,
                      plakette: "Raumbuchung", plaketteFarbe: Farben.raumRand)
        case "bereitschaft":
            self.init(hintergrund: Color.clear, rand: Farben.karteBalken, schatten: false, durchgestrichen: false,
                      plakette: "Bereitschaft", plaketteFarbe: Farben.gedaempft)
        default:
            self.init(hintergrund: vergangen ? Farben.karteVergangen : Farben.karte, rand: nil, schatten: true, durchgestrichen: false,
                      plakette: nil, plaketteFarbe: Farben.gruen)
        }
    }

    /// Nur der Raum wurde geändert (Lehrkraft, Klasse und Fach wie geplant).
    static func nurRaumGeaendert(_ karte: Karte) -> Bool {
        guard karte.raeume.contains(where: { $0.geaendert }) else { return false }
        if karte.lehrer.contains(where: { $0.geaendert }) || karte.klassen.contains(where: { $0.geaendert }) { return false }
        if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach { return false }
        return true
    }

    private init(hintergrund: Color, rand: Color?, schatten: Bool, durchgestrichen: Bool, plakette: String?, plaketteFarbe: Color) {
        self.hintergrund = hintergrund
        self.rand = rand
        self.randBreite = rand == nil ? 0 : 2
        self.schatten = schatten
        self.durchgestrichen = durchgestrichen
        self.plakette = plakette
        self.plaketteFarbe = plaketteFarbe
        self.wertFarbe = ["Raumbuchung", "Prüfung"].contains(plakette ?? "") ? plaketteFarbe : Farben.gruen
    }
}
