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
                    .ignoresSafeArea(.keyboard, edges: .bottom)
                    .frame(width: Detailblatt.breite, height: Detailblatt.hoehe)
                    .presentationSizing(.fitted)
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            } else {
                // iOS 17: Formblatt des Systems (540 × 620 pt), nur die Ecken angleichen
                self
                    .ignoresSafeArea(.keyboard, edges: .bottom)
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            }
        } else {
            self
                .ignoresSafeArea(.keyboard, edges: .bottom)
                .presentationDetents([.large])
                .presentationDragIndicator(.visible)
                .presentationCornerRadius(Detailblatt.radius)
                .presentationBackground(Farben.blatt)
        }
    }
}

// MARK: - Maße der Stundendetails (Original 117/118, iPhone)

enum Detailmass {
    /// Linker Rand von Kopfblock und Liste
    static let rand: CGFloat = 17
    /// Zeilenhöhe im Kopfblock (Grundlinienabstand im Original ≈ 31 pt)
    static let zeile: CGFloat = 31
    /// „Klasse | Gruppe“ und Fach
    static let gross: CGFloat = 22
    /// Raum und Lehrkraft
    static let klein: CGFloat = 17
    /// Durchmesser der runden Glasknöpfe im Kopf
    static let knopf: CGFloat = 44

    /// „07:45“ → „7:45“ (ohne führende Null wie im Original).
    static func uhrzeit(_ text: String) -> String {
        text.count == 5 && text.hasPrefix("0") ? String(text.dropFirst()) : text
    }
}

// MARK: - Kopf des Blatts

/// Runder Glasknopf (44 pt) im Kopf des Blatts: X, Zurück, ⋮ …
/// Ab iOS 26 echtes Liquid Glass (interaktiv), davor ein Milchglas-Kreis mit feinem Rand und weichem Schatten.
struct Rundknopf: View {
    let symbol: String
    var gewicht: Font.Weight = .light
    var groesse: CGFloat = 16
    var drehung: Double = 0

    var body: some View {
        glas(
            Image(systemName: symbol)
                .font(.system(size: groesse, weight: gewicht))
                .rotationEffect(.degrees(drehung))
                .foregroundStyle(Farben.tinte2)
                .frame(width: Detailmass.knopf, height: Detailmass.knopf)
        )
        .contentShape(Circle())
    }

    @ViewBuilder
    private func glas<V: View>(_ inhalt: V) -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26.0, *) {
            inhalt.glassEffect(.regular.interactive(), in: Circle())
        } else {
            milchglas(inhalt)
        }
        #else
        milchglas(inhalt)
        #endif
    }

    private func milchglas<V: View>(_ inhalt: V) -> some View {
        inhalt
            .background {
                Circle()
                    .fill(.ultraThinMaterial)
                    .overlay {
                        Circle()
                            .strokeBorder(Color(hell: 0xFFFFFF, dunkel: 0xFFFFFF, deckkraftHell: 0.8, deckkraftDunkel: 0.16), lineWidth: 0.5)
                    }
                    .shadow(color: Color(hell: 0x141C24, dunkel: 0x000000, deckkraftHell: 0.14, deckkraftDunkel: 0.4), radius: 6, y: 3)
            }
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
                .padding(.horizontal, Detailmass.knopf + 12)
            HStack(spacing: 0) {
                Button(action: aktion) {
                    Rundknopf(symbol: symbol, gewicht: .light, groesse: 20)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(symbolText)
                Spacer(minLength: 0)
                rechts
            }
        }
        .padding(.horizontal, 15)
        .padding(.top, 15)
        .padding(.bottom, 9)
        .frame(minHeight: 68)
        .background(Farben.blatt)
    }
}

extension Blattkopf where Rechts == EmptyView {
    init(symbol: String, symbolText: String, aktion: @escaping () -> Void, @ViewBuilder mitte: () -> Mitte) {
        self.init(symbol: symbol, symbolText: symbolText, aktion: aktion, mitte: mitte, rechts: { EmptyView() })
    }
}

/// Titel in der Mitte des Kopfs wie im Original: „Do. | 08.10.26“ 17 pt grau, darunter die Uhrzeit 13 pt grau.
struct Blatttitel: View {
    let titel: String
    var untertitel: String? = nil
    var pfeil: Bool = false

    var body: some View {
        VStack(spacing: 1) {
            HStack(spacing: 3) {
                Text(titel)
                    .font(.system(size: 17, weight: .regular))
                    .foregroundStyle(Farben.titelGrau)
                if pfeil {
                    Image(systemName: "chevron.down")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Farben.titelGrau)
                }
            }
            if let untertitel, !untertitel.isEmpty {
                Text(untertitel)
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.titelGrau)
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
        // Das Blatt bleibt fest: die Tastatur verschiebt bzw. staucht es nicht (Textfelder scrollen innen)
        .ignoresSafeArea(.keyboard, edges: .bottom)
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
                .frame(width: Detailmass.knopf, height: Detailmass.knopf)
        } else {
            Button(action: aktion) {
                Image(systemName: "checkmark")
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(aktiv ? Color.white : Farben.gedaempft)
                    .frame(width: Detailmass.knopf, height: Detailmass.knopf)
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

/// Graue runde Zahl (Original: Kreis 31 pt, schiefergrau) mit weißer Ziffer – auch bei 0.
struct GrauePlakette: View {
    let zahl: Int

    var body: some View {
        Text("\(zahl)")
            .font(.system(size: 17))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 8)
            .frame(minWidth: 31, minHeight: 31)
            .background(Farben.zahlPlakette, in: Capsule())
    }
}

/// Zeile der Detailliste wie im Original (ohne Trennlinien): Symbol links grau in Linienstil (≈ 30 pt), Titel 19 pt,
/// optional grauer Untertitel 17 pt (einzeilig), rechts runde Zahl und dünner grauer Pfeil. Mindesthöhe 56 pt.
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
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .font(.system(size: 25, weight: .ultraLight))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 30, height: 30)
            VStack(alignment: .leading, spacing: 3) {
                Text(titel)
                    .font(.system(size: 19))
                    .foregroundStyle(Farben.detailText)
                    .lineLimit(1)
                if let untertitel, !untertitel.isEmpty {
                    Text(untertitel.replacingOccurrences(of: "\n", with: " "))
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(1)
                        .truncationMode(.tail)
                }
            }
            Spacer(minLength: 6)
            rechts
            if let zahl {
                GrauePlakette(zahl: zahl)
                    .padding(.trailing, 4)
            }
            if pfeil {
                Image(systemName: "chevron.right")
                    .font(.system(size: 20, weight: .ultraLight))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, 22)
        .padding(.vertical, 10)
        .frame(minHeight: 56)
        .contentShape(Rectangle())
    }
}

extension Detailzeile where Rechts == EmptyView {
    init(symbol: String, titel: String, untertitel: String? = nil, zahl: Int? = nil, pfeil: Bool = true) {
        self.init(symbol: symbol, titel: titel, untertitel: untertitel, zahl: zahl, pfeil: pfeil, rechts: { EmptyView() })
    }
}

// MARK: - Werte im Kopf (Raum, Lehrkraft)

/// Baustein einer Wertzeile im Kopf der Details.
enum Wertbaustein: Hashable {
    case text(String)
    case weg(String)
    case pfeil
    case trenner
    case offen(String)
    /// Neuer Wert (z. B. Vertretung) – im Detail normaler Text
    case neu(String)
}

/// Wertzeile im Kopf der Details – nur Text, keine Plaketten: Änderungen als „alt → neu“ (alt durchgestrichen),
/// fehlende Werte (abwesende Lehrkraft bzw. Klasse) durchgestrichen. Immer genau eine Zeile; bei vielen Werten
/// lässt sie sich waagrecht scrollen.
struct Wertzeile: View {
    let bausteine: [Wertbaustein]
    var schrift: CGFloat = 13
    var farbe: Color = Farben.gedaempft

    var body: some View {
        EinzeilenScroller {
            HStack(spacing: 4) {
                ForEach(Array(bausteine.enumerated()), id: \.offset) { eintrag in
                    baustein(eintrag.element)
                }
            }
            .font(.system(size: schrift))
            .foregroundStyle(farbe)
        }
    }

    @ViewBuilder
    private func baustein(_ b: Wertbaustein) -> some View {
        switch b {
        case .text(let t), .neu(let t):
            Text(t)
        case .weg(let t), .offen(let t):
            Text(t)
                .strikethrough(true)
        case .pfeil:
            Text("→")
        case .trenner:
            Text("|")
                .foregroundStyle(Farben.detailTrenner)
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
            } else if w.k.isEmpty && w.neu && offenAlsPlakette {
                // Offene Vertretung ohne bekannte Lehrkraft
                teil.append(.offen("???"))
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

/// Genau eine Zeile: passt der Inhalt nicht in die Breite, lässt er sich waagrecht scrollen (statt umzubrechen).
struct EinzeilenScroller<Inhalt: View>: View {
    let inhalt: Inhalt

    init(@ViewBuilder inhalt: () -> Inhalt) {
        self.inhalt = inhalt()
    }

    var body: some View {
        ScrollView(.horizontal) {
            inhalt
                .lineLimit(1)
                .fixedSize(horizontal: true, vertical: false)
        }
        .scrollIndicators(.hidden)
        .scrollBounceBehavior(.basedOnSize, axes: .horizontal)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
