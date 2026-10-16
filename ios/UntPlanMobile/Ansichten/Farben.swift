import SwiftUI

extension Color {
    /// Farbe aus "#RRGGBB" (leer oder ungültig = nil).
    init?(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let wert = UInt32(s, radix: 16) else { return nil }
        self.init(hexWert: wert)
    }

    init(hexWert: UInt32) {
        self.init(red: Double((hexWert >> 16) & 0xFF) / 255,
                  green: Double((hexWert >> 8) & 0xFF) / 255,
                  blue: Double(hexWert & 0xFF) / 255)
    }
}

/// Farben wie in UntPlanWeb und UntPlanMobile (PWA).
enum Farben {
    static let akzent = Color(hexWert: 0xF56B2A)
    static let akzentSanft = Color(hexWert: 0xFFF1E2)
    static let tinte = Color(hexWert: 0x253746)
    static let gedaempft = Color(hexWert: 0x6B7A85)
    static let linie = Color(hexWert: 0xDCDEE2)
    static let rasterOben = Color(hexWert: 0xF2F4F7)
    static let rasterUnten = Color(hexWert: 0xE7E9EF)

    static let karte = Color.white
    static let karteVergangen = Color(hexWert: 0xF2F4F6)
    static let karteBalken = Color(hexWert: 0xBFC7D2)

    // Änderung grün
    static let aenderungHintergrund = Color(hexWert: 0xE3FFF2)
    static let aenderungRand = Color(hexWert: 0x019169)
    static let gruen = Color(hexWert: 0x0B7D65)

    // Entfall rot
    static let entfallHintergrund = Color(hexWert: 0xFFF3F2)
    static let entfallRand = Color(hexWert: 0xE00047)

    // Prüfung gelb
    static let pruefungHintergrund = Color(hexWert: 0xFFF6D5)
    static let pruefungRand = Color(hexWert: 0xA1512C)

    // Veranstaltung, Ferien
    static let veranstaltungHintergrund = Color(hexWert: 0xDFFEF5)
    static let ferienHintergrund = Color(hexWert: 0xD5EBFE)
    static let ferienLinie = Color(hexWert: 0xB3D6F6)
    static let ferienTinte = Color(hexWert: 0x2B3F52)

    // Köpfe der Infokarten
    static let nachrichtKopf = Color(hexWert: 0x3D7FC4)
    static let veranstaltungKopf = Color(hexWert: 0x0B9C7A)
    static let hinweisKopf = Color(hexWert: 0xF07C00)

    /// Fachfarbe für den Balken, sonst Grau.
    static func fach(_ hex: String) -> Color {
        Color(hex: hex) ?? karteBalken
    }
}

/// Darstellung einer Stundenkarte je nach Status.
struct Kartenstil {
    var hintergrund: Color
    var rand: Color?
    var durchgestrichen: Bool
    var plakette: String?
    var plaketteFarbe: Color

    init(_ karte: Karte) {
        switch karte.status {
        case "entfall":
            self.init(hintergrund: Farben.entfallHintergrund, rand: Farben.entfallRand, durchgestrichen: true,
                      plakette: "Entfall", plaketteFarbe: Farben.entfallRand)
        case "geaendert":
            self.init(hintergrund: Farben.aenderungHintergrund, rand: Farben.aenderungRand, durchgestrichen: false,
                      plakette: "Änderung", plaketteFarbe: Farben.gruen)
        case "offen":
            self.init(hintergrund: Farben.aenderungHintergrund, rand: Farben.aenderungRand, durchgestrichen: false,
                      plakette: "Offen", plaketteFarbe: Farben.gruen)
        case "klausur":
            self.init(hintergrund: Farben.pruefungHintergrund, rand: Farben.pruefungRand, durchgestrichen: false,
                      plakette: "Prüfung", plaketteFarbe: Farben.pruefungRand)
        case "abwesend":
            self.init(hintergrund: Farben.karteVergangen, rand: Farben.gedaempft, durchgestrichen: false,
                      plakette: "Abgegeben", plaketteFarbe: Farben.gedaempft)
        case "vorgemerkt":
            self.init(hintergrund: Farben.akzentSanft, rand: Farben.akzent, durchgestrichen: false,
                      plakette: "Vorgemerkt", plaketteFarbe: Farben.akzent)
        case "buchung":
            self.init(hintergrund: Farben.veranstaltungHintergrund, rand: Farben.aenderungRand, durchgestrichen: false,
                      plakette: "Buchung", plaketteFarbe: Farben.gruen)
        case "bereitschaft":
            self.init(hintergrund: Farben.karte, rand: Farben.karteBalken, durchgestrichen: false,
                      plakette: "Bereitschaft", plaketteFarbe: Farben.gedaempft)
        default:
            self.init(hintergrund: Farben.karte, rand: nil, durchgestrichen: false,
                      plakette: karte.zusaetzlich ? "Zusätzlich" : nil, plaketteFarbe: Farben.gruen)
        }
    }

    private init(hintergrund: Color, rand: Color?, durchgestrichen: Bool, plakette: String?, plaketteFarbe: Color) {
        self.hintergrund = hintergrund
        self.rand = rand
        self.durchgestrichen = durchgestrichen
        self.plakette = plakette
        self.plaketteFarbe = plaketteFarbe
    }
}
