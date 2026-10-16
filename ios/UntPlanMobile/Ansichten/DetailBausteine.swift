import SwiftUI
import UIKit

// MARK: - Wisch-Geste zum Zurückgehen

/// Bei verborgener Navigationsleiste (Detail-Unterseiten, Planauswahl) schaltet UIKit die Rand-Wischgeste zum Zurückgehen ab.
/// Mit eigenem Delegate bleibt sie aktiv – aber nur, wenn es eine Seite zum Zurückgehen gibt.
extension UINavigationController: UIGestureRecognizerDelegate {
    override open func viewDidLoad() {
        super.viewDidLoad()
        interactivePopGestureRecognizer?.delegate = self
    }

    public func gestureRecognizerShouldBegin(_ gestureRecognizer: UIGestureRecognizer) -> Bool {
        guard gestureRecognizer === interactivePopGestureRecognizer else { return true }
        return viewControllers.count > 1
    }
}

// MARK: - Blatt der Stundendetails

/// Feste Maße des Detailblatts auf dem iPad (wie in der Vorlage: mittig über dem Plan).
enum Detailblatt {
    static let breite: CGFloat = 540
    static let hoehe: CGFloat = 600
    static let radius: CGFloat = 24
}

extension View {
    /// Stundendetails als weißes Blatt mit großen Ecken: auf dem iPad fest 540 × 600 pt mittig (die Größe bleibt beim
    /// Wechsel in Unterseiten gleich), auf dem iPhone großes Blatt mit Griff.
    @ViewBuilder
    func detailBlatt(breit: Bool) -> some View {
        if breit {
            if #available(iOS 18.0, *) {
                self
                    .frame(width: Detailblatt.breite, height: Detailblatt.hoehe)
                    .presentationSizing(.fitted)
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            } else {
                // iOS 17: Formblatt des Systems (540 × 620 pt), nur die Ecken angleichen
                self
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            }
        } else {
            self
                .presentationDetents([.large])
                .presentationDragIndicator(.visible)
                .presentationCornerRadius(Detailblatt.radius)
                .presentationBackground(Farben.blatt)
        }
    }
}

// MARK: - Kopf des Blatts

/// Runder grauer Knopf (36 pt) im Kopf des Blatts: X, Zurück, ⋮ …
struct Rundknopf: View {
    let symbol: String
    var gewicht: Font.Weight = .light
    var groesse: CGFloat = 16
    var drehung: Double = 0

    var body: some View {
        Image(systemName: symbol)
            .font(.system(size: groesse, weight: gewicht))
            .rotationEffect(.degrees(drehung))
            .foregroundStyle(Farben.gedaempft)
            .frame(width: 36, height: 36)
            .background(Farben.knopf, in: Circle())
            .contentShape(Circle())
    }
}

/// Kopfzeile des Blatts: links ein runder Knopf, in der Mitte der (zweizeilige) Titel, rechts optional ein Knopf.
/// Der Titel steht immer genau mittig, unabhängig davon, ob rechts etwas steht.
struct Blattkopf<Mitte: View, Rechts: View>: View {
    let symbol: String
    let symbolText: String
    let aktion: () -> Void
    let mitte: Mitte
    let rechts: Rechts

    init(symbol: String, symbolText: String, aktion: @escaping () -> Void,
         @ViewBuilder mitte: () -> Mitte, @ViewBuilder rechts: () -> Rechts) {
        self.symbol = symbol
        self.symbolText = symbolText
        self.aktion = aktion
        self.mitte = mitte()
        self.rechts = rechts()
    }

    var body: some View {
        ZStack {
            mitte
                .frame(maxWidth: .infinity)
                .padding(.horizontal, 56)
            HStack(spacing: 0) {
                Button(action: aktion) {
                    Rundknopf(symbol: symbol, gewicht: .light, groesse: symbol == "chevron.left" ? 17 : 16)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(symbolText)
                Spacer(minLength: 0)
                rechts
            }
        }
        .padding(.horizontal, 14)
        .padding(.top, 14)
        .padding(.bottom, 8)
        .frame(minHeight: 58)
        .background(Farben.blatt)
    }
}

extension Blattkopf where Rechts == EmptyView {
    init(symbol: String, symbolText: String, aktion: @escaping () -> Void, @ViewBuilder mitte: () -> Mitte) {
        self.init(symbol: symbol, symbolText: symbolText, aktion: aktion, mitte: mitte, rechts: { EmptyView() })
    }
}

/// Titel in der Mitte des Kopfs: oben klein grau-schwarz, darunter klein grau (z. B. Datum und Uhrzeit).
struct Blatttitel: View {
    let titel: String
    var untertitel: String? = nil
    var pfeil: Bool = false

    var body: some View {
        VStack(spacing: 1) {
            HStack(spacing: 3) {
                Text(titel)
                    .font(.system(size: 14, weight: .regular))
                    .foregroundStyle(Farben.tinte2)
                if pfeil {
                    Image(systemName: "chevron.down")
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(Farben.tinte2)
                }
            }
            if let untertitel, !untertitel.isEmpty {
                Text(untertitel)
                    .font(.system(size: 11))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .lineLimit(1)
        .minimumScaleFactor(0.8)
        .accessibilityElement(children: .combine)
    }
}

/// Rahmen einer Unterseite im selben Blatt: Kopf mit Zurück (bzw. X), Titel mittig, darunter der Inhalt.
/// Die Navigationsleiste des Systems bleibt verborgen, damit das Blatt überall gleich aussieht.
struct DetailSeitenRahmen<Inhalt: View, Rechts: View>: View {
    let titel: String
    let untertitel: String?
    let symbol: String
    let zurueck: (() -> Void)?
    let inhalt: Inhalt
    let rechts: Rechts
    @Environment(\.dismiss) private var schliessen

    init(titel: String, untertitel: String? = nil, symbol: String = "chevron.left", zurueck: (() -> Void)? = nil,
         @ViewBuilder inhalt: () -> Inhalt, @ViewBuilder rechts: () -> Rechts) {
        self.titel = titel
        self.untertitel = untertitel
        self.symbol = symbol
        self.zurueck = zurueck
        self.inhalt = inhalt()
        self.rechts = rechts()
    }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: symbol, symbolText: symbol == "xmark" ? "Schließen" : "Zurück", aktion: {
                if let zurueck { zurueck() } else { schliessen() }
            }, mitte: {
                Blatttitel(titel: titel, untertitel: untertitel)
            }, rechts: {
                rechts
            })
            inhalt
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .background(Farben.blatt.ignoresSafeArea())
        .toolbar(.hidden, for: .navigationBar)
    }
}

extension DetailSeitenRahmen where Rechts == EmptyView {
    init(titel: String, untertitel: String? = nil, symbol: String = "chevron.left", zurueck: (() -> Void)? = nil,
         @ViewBuilder inhalt: () -> Inhalt) {
        self.init(titel: titel, untertitel: untertitel, symbol: symbol, zurueck: zurueck, inhalt: inhalt, rechts: { EmptyView() })
    }
}

/// Runder Knopf „Sichern“ (Haken) rechts im Kopf einer Unterseite.
struct SichernKnopf: View {
    let speichert: Bool
    let aktiv: Bool
    let aktion: () -> Void

    var body: some View {
        if speichert {
            ProgressView()
                .frame(width: 36, height: 36)
        } else {
            Button(action: aktion) {
                Image(systemName: "checkmark")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(aktiv ? Color.white : Farben.gedaempft)
                    .frame(width: 36, height: 36)
                    .background(aktiv ? Farben.akzent : Farben.knopf, in: Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .disabled(!aktiv)
            .accessibilityLabel("Sichern")
        }
    }
}

// MARK: - Zeilen der Liste

/// Graue runde Zahl (#8e8e93) mit weißer Ziffer – auch bei 0.
struct GrauePlakette: View {
    let zahl: Int

    var body: some View {
        Text("\(zahl)")
            .font(.system(size: 12, weight: .semibold))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .frame(minWidth: 22, minHeight: 22)
            .background(Color(hexWert: 0x8E8E93), in: Capsule())
    }
}

/// Zeile der Detailliste (weiß, ohne Gruppenhintergrund): Symbol links grau in Linienstil, Titel (und grauer Untertitel),
/// rechts Zahl und dünner grauer Pfeil. Höhe 52 pt.
struct Detailzeile<Rechts: View>: View {
    let symbol: String
    let titel: String
    var untertitel: String? = nil
    var zahl: Int? = nil
    var pfeil: Bool = true
    let rechts: Rechts

    init(symbol: String, titel: String, untertitel: String? = nil, zahl: Int? = nil, pfeil: Bool = true,
         @ViewBuilder rechts: () -> Rechts) {
        self.symbol = symbol
        self.titel = titel
        self.untertitel = untertitel
        self.zahl = zahl
        self.pfeil = pfeil
        self.rechts = rechts()
    }

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: symbol)
                .font(.system(size: 19, weight: .light))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 28, height: 28)
            VStack(alignment: .leading, spacing: 2) {
                Text(titel)
                    .font(.system(size: 15.5))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if let untertitel, !untertitel.isEmpty {
                    Text(untertitel)
                        .font(.system(size: 12.5))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
            }
            Spacer(minLength: 6)
            rechts
            if let zahl {
                GrauePlakette(zahl: zahl)
            }
            if pfeil {
                Image(systemName: "chevron.right")
                    .font(.system(size: 15, weight: .light))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.leading, 20)
        .padding(.trailing, 18)
        .padding(.vertical, 6)
        .frame(minHeight: 52)
        .contentShape(Rectangle())
    }
}

extension Detailzeile where Rechts == EmptyView {
    init(symbol: String, titel: String, untertitel: String? = nil, zahl: Int? = nil, pfeil: Bool = true) {
        self.init(symbol: symbol, titel: titel, untertitel: untertitel, zahl: zahl, pfeil: pfeil, rechts: { EmptyView() })
    }
}

/// Trennlinie der Detailliste, eingerückt bis zum Text (20 + 28 + 14 pt).
struct DetailTrenner: View {
    var body: some View {
        Rectangle()
            .fill(Farben.trennlinie)
            .frame(height: 0.5)
            .padding(.leading, 62)
    }
}

// MARK: - Werte im Kopf (Raum, Lehrkraft)

/// Grüne Plakette für die offene Vertretung: alte Lehrkraft durchgestrichen auf Grün (#0b7d65), weiße Schrift.
struct OffenPlakette: View {
    let text: String
    var schrift: CGFloat = 13

    var body: some View {
        Text(text)
            .font(.system(size: schrift, weight: .medium))
            .strikethrough(true, color: Farben.gruenSchrift)
            .foregroundStyle(Farben.gruenSchrift)
            .padding(.horizontal, 4)
            .padding(.vertical, 1)
            .background(Farben.gruen, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
            .lineLimit(1)
            .fixedSize()
            .accessibilityLabel("\(text), Vertretung offen")
    }
}

/// Baustein einer Wertzeile im Kopf der Details.
enum Wertbaustein: Hashable {
    case text(String)
    case weg(String)
    case pfeil
    case trenner
    case offen(String)
}

/// Wertzeile im Kopf der Details: „alt → neu“ (alt durchgestrichen); offene Vertretung als grüne Plakette.
struct Wertzeile: View {
    let bausteine: [Wertbaustein]
    var schrift: CGFloat = 13

    var body: some View {
        Fliesslayout(abstand: 4, zeilenAbstand: 3) {
            ForEach(Array(bausteine.enumerated()), id: \.offset) { eintrag in
                baustein(eintrag.element)
            }
        }
        .font(.system(size: schrift))
        .foregroundStyle(Farben.gedaempft)
    }

    @ViewBuilder
    private func baustein(_ b: Wertbaustein) -> some View {
        switch b {
        case .text(let t):
            Text(t)
                .fixedSize()
        case .weg(let t):
            Text(t)
                .strikethrough(true)
                .fixedSize()
        case .pfeil:
            Text("→")
                .fixedSize()
        case .trenner:
            Text("|")
                .fixedSize()
        case .offen(let t):
            OffenPlakette(text: t, schrift: schrift)
        }
    }

    /// Bausteine aus Werten: geänderte Werte als „alt → neu“, offene Lehrkraft (kein neuer Wert) als grüne Plakette.
    static func bausteine(_ werte: [Wert], durchgestrichen: Bool, offenAlsPlakette: Bool) -> [Wertbaustein] {
        var ergebnis: [Wertbaustein] = []
        for w in werte {
            var teil: [Wertbaustein] = []
            let alt = w.alt ?? ""
            if durchgestrichen {
                let t = w.k.isEmpty ? alt : w.k
                if !t.isEmpty { teil.append(.weg(t)) }
            } else if w.k.isEmpty && !alt.isEmpty {
                teil.append(offenAlsPlakette ? .offen(alt) : .weg(alt))
            } else {
                if !alt.isEmpty && alt != w.k {
                    teil.append(.weg(alt))
                    teil.append(.pfeil)
                }
                if !w.k.isEmpty { teil.append(.text(w.k)) }
            }
            guard !teil.isEmpty else { continue }
            if !ergebnis.isEmpty { ergebnis.append(.trenner) }
            ergebnis.append(contentsOf: teil)
        }
        return ergebnis
    }
}

/// Einfaches Fließlayout: Elemente nebeneinander, bei Platzmangel in die nächste Zeile (zeilenweise mittig ausgerichtet).
struct Fliesslayout: Layout {
    var abstand: CGFloat = 4
    var zeilenAbstand: CGFloat = 3

    private struct Zeile {
        var indizes: [Int] = []
        var breite: CGFloat = 0
        var hoehe: CGFloat = 0
    }

    private func zeilen(_ maxBreite: CGFloat, _ groessen: [CGSize]) -> [Zeile] {
        var ergebnis: [Zeile] = []
        var aktuell = Zeile()
        for (i, g) in groessen.enumerated() {
            let noetig = aktuell.indizes.isEmpty ? g.width : aktuell.breite + abstand + g.width
            if !aktuell.indizes.isEmpty && noetig > maxBreite {
                ergebnis.append(aktuell)
                aktuell = Zeile()
            }
            aktuell.breite = aktuell.indizes.isEmpty ? g.width : aktuell.breite + abstand + g.width
            aktuell.hoehe = max(aktuell.hoehe, g.height)
            aktuell.indizes.append(i)
        }
        if !aktuell.indizes.isEmpty { ergebnis.append(aktuell) }
        return ergebnis
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let groessen = subviews.map { $0.sizeThatFits(.unspecified) }
        let maxBreite = proposal.width ?? .infinity
        let liste = zeilen(maxBreite, groessen)
        let breite = liste.map(\.breite).max() ?? 0
        let hoehe = liste.map(\.hoehe).reduce(0, +) + CGFloat(max(liste.count - 1, 0)) * zeilenAbstand
        return CGSize(width: maxBreite.isFinite ? min(breite, maxBreite) : breite, height: hoehe)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let groessen = subviews.map { $0.sizeThatFits(.unspecified) }
        var y = bounds.minY
        for zeile in zeilen(bounds.width, groessen) {
            var x = bounds.minX
            for i in zeile.indizes {
                let g = groessen[i]
                subviews[i].place(at: CGPoint(x: x, y: y + (zeile.hoehe - g.height) / 2), anchor: .topLeading,
                                  proposal: ProposedViewSize(width: g.width, height: g.height))
                x += g.width + abstand
            }
            y += zeile.hoehe + zeilenAbstand
        }
    }
}
