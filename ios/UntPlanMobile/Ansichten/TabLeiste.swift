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
        let renderer = ImageRenderer(content:
            KalenderSymbol(inhalt: .tag(tag), groesse: 26, strich: 1.7)
                .foregroundStyle(Color.black)
        )
        renderer.scale = 3
        if let bild = renderer.cgImage {
            return Image(decorative: bild, scale: 3).renderingMode(.template)
        }
        return Image(systemName: "calendar")
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
