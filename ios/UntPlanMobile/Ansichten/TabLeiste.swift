import SwiftUI

/// Reiter der unteren Leiste.
enum AppReiter: Hashable, CaseIterable {
    case start
    case stundenplan
    case mitteilungen
    case profil

    var titel: String {
        switch self {
        case .start: return "Start"
        case .stundenplan: return "Stundenplan"
        case .mitteilungen: return "Mitteilungen"
        case .profil: return "Profil"
        }
    }

    /// SF Symbol (Stundenplan zeichnet ein eigenes Kalenderblatt mit Tageszahl).
    var symbol: String {
        switch self {
        case .start: return "building.columns"
        case .stundenplan: return "calendar"
        case .mitteilungen: return "envelope"
        case .profil: return "person"
        }
    }
}

/// Beschriftung der Systemleiste (ab iOS 26) so weit möglich wie in der Vorlage: 12,5 pt kräftig.
enum Leistenaussehen {
    @MainActor
    static func anwenden() {
        let schrift = UIFont.systemFont(ofSize: 12.5, weight: .semibold)
        let eintrag = UITabBarItem.appearance()
        eintrag.setTitleTextAttributes([.font: schrift], for: .normal)
        eintrag.setTitleTextAttributes([.font: schrift], for: .selected)
    }
}

/// Zeichen für die Systemleiste, die sich nicht als SF Symbol finden.
enum Leistensymbole {
    static var heutigerTag: Int {
        Datumsformat.kalender.component(.day, from: Date())
    }

    /// Bereits gezeichnete Kalenderblätter je Tageszahl (ImageRenderer ist teuer; die Leiste fragt bei jedem Zeichnen).
    @MainActor private static var kalenderBilder: [Int: Image] = [:]

    /// Kalenderblatt mit Tageszahl als Vorlagenbild (wird von der Leiste eingefärbt).
    @MainActor
    static func kalender(tag: Int) -> Image {
        if let fertig = kalenderBilder[tag] { return fertig }
        let bild = kalenderZeichnen(tag: tag)
        kalenderBilder[tag] = bild
        return bild
    }

    @MainActor
    private static func kalenderZeichnen(tag: Int) -> Image {
        // Vorlage 6a2e7724 (3 px/pt): Kalenderblatt 72 × 72 px (vorher 68 px)
        vorlage(KalenderSymbol(inhalt: .tag(tag), groesse: 27.6, strich: 1.75), ersatz: "calendar")
    }

    /// Schulgebäude mit Fahne, Briefumschlag und Person wie in der Vorlage (6a2e7724, 3 px/pt): Umrisse 1,67 pt, Größe wie
    /// gemessen (Schule 24 × 24 pt, Umschlag 22,67 × 16,33 pt, Person 17,33 × 19,33 pt).
    @MainActor static let schule: Image = vorlage(Leistenschule(), ersatz: "building.columns")
    @MainActor static let umschlag: Image = vorlage(Leistenumschlag(), ersatz: "envelope")
    @MainActor static let person: Image = vorlage(Leistenperson(), ersatz: "person")

    @MainActor
    private static func vorlage<V: View>(_ ansicht: V, ersatz: String) -> Image {
        let renderer = ImageRenderer(content: ansicht.foregroundStyle(Color.black))
        renderer.scale = 3
        if let bild = renderer.cgImage {
            return Image(decorative: bild, scale: 3).renderingMode(.template)
        }
        return Image(systemName: ersatz)
    }
}

/// Strichstärke der Leistensymbole (Vorlage: 5 px bei 3 px/pt)
private let leistenStrich: CGFloat = 1.67

/// Schulgebäude mit Fahne (24 × 24 pt): Mittelbau mit rundem Giebel und Rundfenster, Seitenflügel mit Fenstern, Tür.
struct Leistenschule: View {
    var body: some View {
        ZStack {
            Path { p in
                // Fahnenmast und Fahne
                p.move(to: CGPoint(x: 12, y: 6.4))
                p.addLine(to: CGPoint(x: 12, y: 0.6))
                p.addLine(to: CGPoint(x: 16.3, y: 0.6))
                p.addLine(to: CGPoint(x: 16.3, y: 3.8))
                p.addLine(to: CGPoint(x: 12, y: 3.8))
                // Mittelbau mit rundem Giebel
                p.move(to: CGPoint(x: 7.5, y: 23.2))
                p.addLine(to: CGPoint(x: 7.5, y: 11))
                p.addArc(center: CGPoint(x: 12, y: 11), radius: 4.5, startAngle: .degrees(180), endAngle: .degrees(0), clockwise: false)
                p.addLine(to: CGPoint(x: 16.5, y: 23.2))
                // Seitenflügel
                p.move(to: CGPoint(x: 7.5, y: 13.35))
                p.addLine(to: CGPoint(x: 2, y: 13.35))
                p.addQuadCurve(to: CGPoint(x: 0.8, y: 14.6), control: CGPoint(x: 0.8, y: 13.35))
                p.addLine(to: CGPoint(x: 0.8, y: 23.2))
                p.addLine(to: CGPoint(x: 23.2, y: 23.2))
                p.addLine(to: CGPoint(x: 23.2, y: 14.6))
                p.addQuadCurve(to: CGPoint(x: 22, y: 13.35), control: CGPoint(x: 23.2, y: 13.35))
                p.addLine(to: CGPoint(x: 16.5, y: 13.35))
                // Tür
                p.move(to: CGPoint(x: 10.5, y: 23.2))
                p.addLine(to: CGPoint(x: 10.5, y: 19.5))
                p.addArc(center: CGPoint(x: 12, y: 19.5), radius: 1.5, startAngle: .degrees(180), endAngle: .degrees(0), clockwise: false)
                p.addLine(to: CGPoint(x: 13.5, y: 23.2))
                // Rundfenster
                p.addEllipse(in: CGRect(x: 10.85, y: 9.65, width: 2.3, height: 2.3))
            }
            .stroke(style: StrokeStyle(lineWidth: leistenStrich, lineCap: .round, lineJoin: .round))
            // Fenster der Seitenflügel
            Path { p in
                for x in [3.0, 19.0] {
                    p.addRoundedRect(in: CGRect(x: x, y: 16, width: 2, height: 1.15), cornerSize: CGSize(width: 0.3, height: 0.3))
                    p.addRoundedRect(in: CGRect(x: x, y: 19.35, width: 2.1, height: 1.6), cornerSize: CGSize(width: 0.5, height: 0.5))
                }
            }
            .fill()
        }
        .frame(width: 24, height: 24)
    }
}

/// Briefumschlag (22,67 × 16,33 pt): Rahmen mit Ecken 2 pt, Lasche bis 8,3 pt unter die Oberkante.
struct Leistenumschlag: View {
    var body: some View {
        let s = leistenStrich
        ZStack {
            RoundedRectangle(cornerRadius: 2.2, style: .continuous)
                .inset(by: s / 2)
                .stroke(lineWidth: s)
            Path { p in
                p.move(to: CGPoint(x: 1.9, y: 1.6))
                p.addLine(to: CGPoint(x: 11.33, y: 8.4))
                p.addLine(to: CGPoint(x: 20.77, y: 1.6))
            }
            .stroke(style: StrokeStyle(lineWidth: s, lineCap: .round, lineJoin: .round))
        }
        .frame(width: 22.67, height: 16.33)
    }
}

/// Person (17,33 × 19,33 pt): Kopf (Ring, Radius 4,17 pt) und Schultern als offener Bogen.
struct Leistenperson: View {
    var body: some View {
        let s = leistenStrich
        Path { p in
            p.addEllipse(in: CGRect(x: 8.67 - 4.17, y: s / 2, width: 8.33, height: 8.33))
            // Schultern: obere Hälfte einer Ellipse (Mitte unten), Enden senkrecht
            p.move(to: CGPoint(x: s / 2, y: 19.33))
            p.addCurve(to: CGPoint(x: 8.67, y: 11.9), control1: CGPoint(x: s / 2, y: 15.2), control2: CGPoint(x: 4.3, y: 11.9))
            p.addCurve(to: CGPoint(x: 17.33 - s / 2, y: 19.33), control1: CGPoint(x: 13.0, y: 11.9), control2: CGPoint(x: 17.33 - s / 2, y: 15.2))
        }
        .stroke(style: StrokeStyle(lineWidth: s, lineCap: .butt, lineJoin: .round))
        .frame(width: 17.33, height: 19.33)
    }
}

/// Schwebende Glaskapsel als untere Leiste (vor iOS 26) genau nach Vorlage (108/116): Glaskapsel 20 pt vom Rand,
/// je Reiter Symbol über Text, der aktive Reiter als hellere Kapsel (ragt etwas in die Nachbarn), Text 12 pt.
struct GlasTabLeiste: View {
    static let hoehe: CGFloat = 60

    @Binding var auswahl: AppReiter
    let ungelesen: Int
    @Namespace private var raum

    var body: some View {
        HStack(spacing: 0) {
            ForEach(AppReiter.allCases, id: \.self) { reiter in
                Button {
                    withAnimation(.spring(response: 0.32, dampingFraction: 0.82)) {
                        auswahl = reiter
                    }
                } label: {
                    eintrag(reiter)
                }
                .buttonStyle(LeistenKnopfStil())
                .accessibilityLabel(reiter.titel)
                .accessibilityAddTraits(auswahl == reiter ? .isSelected : [])
            }
        }
        .padding(.horizontal, 6)
        .frame(height: Self.hoehe)
        .frame(maxWidth: 460)
        .glas(Capsule())
        .padding(.horizontal, 20)
    }

    private func eintrag(_ reiter: AppReiter) -> some View {
        let aktiv = auswahl == reiter
        return VStack(spacing: 3) {
            symbol(reiter)
                .frame(width: 30, height: 25)
                .overlay(alignment: .topTrailing) {
                    if reiter == .mitteilungen && ungelesen > 0 {
                        Text(ungelesen > 99 ? "99+" : "\(ungelesen)")
                            .font(.system(size: 11, weight: .medium))
                            .monospacedDigit()
                            .foregroundStyle(.white)
                            .padding(.horizontal, 4)
                            .frame(minWidth: 18, minHeight: 18)
                            .background(Farben.heute, in: Capsule())
                            .offset(x: 9, y: -6)
                    }
                }
            // Wie in der Vorlage (108/116/s-03): Beschriftung 12,5 pt kräftig
            Text(reiter.titel)
                .font(.system(size: 12.5, weight: .semibold))
                .tracking(-0.15)
                .lineLimit(1)
                .minimumScaleFactor(0.75)
                .padding(.horizontal, 2)
        }
        .foregroundStyle(aktiv ? Farben.leisteAktiv : Farben.leisteText)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            if aktiv {
                Capsule()
                    .fill(Color(hell: 0xDCDFE4, dunkel: 0xFFFFFF, deckkraftHell: 1, deckkraftDunkel: 0.18))
                    .overlay {
                        Capsule().strokeBorder(Color(hell: 0xFFFFFF, dunkel: 0xFFFFFF, deckkraftHell: 0.7, deckkraftDunkel: 0.12), lineWidth: 0.5)
                    }
                    .padding(.vertical, 4)
                    .padding(.horizontal, -5)
                    .matchedGeometryEffect(id: "aktiverReiter", in: raum)
            }
        }
        .contentShape(Capsule())
    }

    @ViewBuilder
    private func symbol(_ reiter: AppReiter) -> some View {
        if reiter == .stundenplan {
            KalenderSymbol(inhalt: .tag(Leistensymbole.heutigerTag), groesse: 26, strich: 1.7)
        } else {
            Image(systemName: reiter.symbol)
                .font(.system(size: 21, weight: .regular))
        }
    }
}

/// Leichtes Eindrücken beim Antippen (wie .tab:active { scale(.92) }).
private struct LeistenKnopfStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.92 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}

/// Schwebende Kapsel oben mittig (iPad vor iOS 26): nur Text, aktiver Reiter orange auf hellem Feld.
struct KopfTabLeiste: View {
    static let hoehe: CGFloat = 40

    @Binding var auswahl: AppReiter
    let ungelesen: Int
    @Namespace private var raum

    var body: some View {
        HStack(spacing: 2) {
            ForEach(AppReiter.allCases, id: \.self) { reiter in
                Button {
                    withAnimation(.spring(response: 0.32, dampingFraction: 0.82)) {
                        auswahl = reiter
                    }
                } label: {
                    eintrag(reiter)
                }
                .buttonStyle(LeistenKnopfStil())
                .accessibilityLabel(reiter == .mitteilungen && ungelesen > 0 ? "\(reiter.titel), \(ungelesen) ungelesen" : reiter.titel)
                .accessibilityAddTraits(auswahl == reiter ? .isSelected : [])
            }
        }
        .padding(3)
        .frame(height: Self.hoehe)
        .glas(Capsule())
    }

    private func eintrag(_ reiter: AppReiter) -> some View {
        let aktiv = auswahl == reiter
        return HStack(spacing: 5) {
            Text(reiter.titel)
                .font(.system(size: 14.5, weight: .medium))
                .lineLimit(1)
            if reiter == .mitteilungen && ungelesen > 0 {
                Text(ungelesen > 99 ? "99+" : "\(ungelesen)")
                    .font(.system(size: 10.5, weight: .medium))
                    .monospacedDigit()
                    .foregroundStyle(.white)
                    .padding(.horizontal, 4)
                    .frame(minWidth: 16, minHeight: 16)
                    .background(Farben.heute, in: Capsule())
            }
        }
        .foregroundStyle(aktiv ? Farben.akzent : Farben.leisteText)
        .padding(.horizontal, 13)
        .frame(maxHeight: .infinity)
        .background {
            if aktiv {
                Capsule()
                    .fill(Farben.reiterAktiv)
                    .matchedGeometryEffect(id: "aktiverKopfReiter", in: raum)
            }
        }
        .contentShape(Capsule())
    }
}
