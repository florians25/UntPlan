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

/// Feste Maße des Detailblatts auf dem iPad (wie in der Vorlage: mittig über dem Plan, breiter als ein Formblatt).
enum Detailblatt {
    static let breite: CGFloat = 620
    /// Höhe auf dem iPad (etwas länger als ein Formblatt; die Breite bleibt 620 pt)
    static let hoehe: CGFloat = 740
    /// iPhone: volle Breite, 85 % der Höhe (Planauswahl, Formulare, Info …; nicht bis ganz oben)
    static let iPhoneAnteil: CGFloat = 0.85
    /// iPhone: Stunden- und Aufsichtsdetails randlos über die volle Breite, 90 % der Bildschirmhöhe
    static let iPhoneDetailAnteil: CGFloat = 0.90
    static let radius: CGFloat = 24

    /// Höhe aller Blätter auf dem iPhone: 85 % (auch unter iOS 26). Auf dem iPad (Formblatt) ignoriert das System die Stufe.
    static var iPhoneStufen: Set<PresentationDetent> { [.fraction(iPhoneAnteil)] }
}

/// Tastatur schließen (Fokus abgeben) – z. B. beim Schließen eines Blatts, damit keine Einrückung hängen bleibt.
enum Tastatur {
    @MainActor
    static func schliessen() {
        UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
    }
}

extension View {
    /// Blatt auf dem iPhone nur 85 % hoch (Planauswahl, Formulare, Info …); auf dem iPad bleibt das Formblatt.
    func iPhoneBlatt() -> some View {
        presentationDetents(Detailblatt.iPhoneStufen)
    }

    /// Stundendetails als weißes Blatt mit großen Ecken: auf dem iPad fest 620 × 640 pt mittig (die Größe bleibt beim
    /// Wechsel in Unterseiten und beim Ein-/Ausblenden der Tastatur gleich). Auf dem iPhone zeigt `detailPraesentation`
    /// stattdessen das randlose 90-%-Blatt (`Randblatt`); der Zweig für schmale Breiten bleibt als Rückfall.
    /// Die Tastatur verschiebt bzw. staucht das Blatt selbst nicht; Textfelder in Unterseiten weichen ihr innen aus.
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
                .presentationDetents(Detailblatt.iPhoneStufen)
                .presentationDragIndicator(.hidden)
                .presentationContentInteraction(.scrolls)
                .presentationCornerRadius(Detailblatt.radius)
                .presentationBackground(Farben.blatt)
        }
    }

}

// MARK: - Randloses Detailblatt (iPhone)

private struct BlattSchliessenSchluessel: EnvironmentKey {
    static let defaultValue: (@MainActor () -> Void)? = nil
}

extension EnvironmentValues {
    /// Schließt das randlose Detailblatt mit seiner Animation (nil = Systemblatt, dann `dismiss`).
    var blattSchliessen: (@MainActor () -> Void)? {
        get { self[BlattSchliessenSchluessel.self] }
        set { self[BlattSchliessenSchluessel.self] = newValue }
    }
}

extension View {
    /// Stunden- bzw. Aufsichtsdetails zeigen: auf dem iPad als festes Formblatt (`detailBlatt`), auf dem iPhone als
    /// randloses Blatt über die volle Breite und 90 % der Höhe. Die Systemblätter von iOS 26 schweben bei Teilhöhen mit
    /// Abstand zum Rand – deshalb zeichnet das iPhone-Blatt sich selbst (auf allen iOS-Versionen gleich).
    /// `art`: auf dem iPad das feste Detailblatt (620 × 740 pt) oder ein Formblatt des Systems (Formulare, Auswahl …);
    /// auf dem iPhone sehen beide gleich aus (Randblatt).
    func detailPraesentation<Item: Identifiable, Blatt: View>(item: Binding<Item?>, breit: Bool, art: Blattart = .detail,
                                                              @ViewBuilder blatt: @escaping (Item) -> Blatt) -> some View {
        modifier(DetailPraesentation(item: item, breit: breit, art: art, blatt: blatt))
    }

    /// Wie `detailPraesentation`, aber mit einem Schalter (Planauswahl, Info, Hilfe, Formulare, Kontenauswahl …): auf dem
    /// iPhone dasselbe randlose 90-%-Blatt wie die Stundendetails, auf dem iPad je nach `art`.
    func blattPraesentation<Blatt: View>(isPresented: Binding<Bool>, breit: Bool, art: Blattart = .formular,
                                         @ViewBuilder blatt: @escaping () -> Blatt) -> some View {
        detailPraesentation(item: Binding<Blattschalter?>(get: { isPresented.wrappedValue ? Blattschalter() : nil },
                                                          set: { isPresented.wrappedValue = $0 != nil }),
                            breit: breit, art: art) { _ in
            blatt()
        }
    }

    /// Auf dem iPad: festes Detailblatt bzw. Formblatt des Systems (mit gleichen Ecken und Hintergrund).
    @ViewBuilder
    func iPadBlatt(_ art: Blattart) -> some View {
        switch art {
        case .detail:
            detailBlatt(breit: true)
        case .formular:
            if #available(iOS 18.0, *) {
                self
                    .presentationSizing(.form)
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            } else {
                self
                    .presentationCornerRadius(Detailblatt.radius)
                    .presentationBackground(Farben.blatt)
            }
        }
    }
}

/// Größe eines Blatts auf dem iPad (auf dem iPhone sind alle Blätter gleich: randlos, volle Breite, 90 % hoch).
enum Blattart {
    /// Festes Detailblatt 620 × 740 pt (Stunden-, Aufsichtsdetails, Sprechtag)
    case detail
    /// Formblatt des Systems (Planauswahl, Info, Formulare, Kontenauswahl …)
    case formular
}

/// Platzhalter-Element für Blätter, die über einen Schalter (Bool) gezeigt werden.
struct Blattschalter: Identifiable {
    var id: Int { 0 }
}

/// Gerät: iPad (auch innerhalb eines Formblatts, wo die Größenklasse kompakt meldet).
enum Geraet {
    @MainActor
    static var istPad: Bool { UIDevice.current.userInterfaceIdiom == .pad }
}

private struct DetailPraesentation<Item: Identifiable, Blatt: View>: ViewModifier {
    @Binding var item: Item?
    let breit: Bool
    let art: Blattart
    let blatt: (Item) -> Blatt
    /// Auf dem iPhone gezeigtes Element: ohne Systemanimation präsentiert, das Blatt fährt selbst ein und aus.
    @State private var randElement: Item? = nil

    func body(content: Content) -> some View {
        content
            .sheet(item: Binding(get: { breit ? item : nil }, set: { item = $0 })) { element in
                blatt(element)
                    // Systemblatt: ein äußeres Randblatt darf hier nicht mitgeschlossen werden
                    .environment(\.blattSchliessen, nil)
                    .iPadBlatt(art)
            }
            .fullScreenCover(item: Binding(get: { randElement }, set: { neu in
                randElement = neu
                if neu == nil { item = nil }
            })) { element in
                Randblatt(schliessen: { item = nil }) {
                    blatt(element)
                }
                .presentationBackground(.clear)
            }
            .onChange(of: item?.id) { _, _ in abgleichen() }
            .onChange(of: breit) { _, _ in abgleichen() }
            .onAppear { abgleichen() }
    }

    /// Blatt auf dem iPhone ohne die Einblendung des Vollbilds zeigen bzw. entfernen.
    private func abgleichen() {
        let neu = breit ? nil : item
        guard neu?.id != randElement?.id else { return }
        var transaktion = Transaction()
        transaktion.disablesAnimations = true
        withTransaction(transaktion) { randElement = neu }
    }
}

/// Randloses Blatt (iPhone): volle Breite ohne seitlichen Abstand, 90 % der Bildschirmhöhe, oben große Ecken; dahinter
/// ein abgedunkelter Plan (Antippen schließt). Am Kopf nach unten ziehen schließt ebenfalls. Bei eingeblendeter Tastatur
/// bleibt die Oberkante stehen und das Blatt endet über der Tastatur (Textfelder bleiben sichtbar).
struct Randblatt<Inhalt: View>: View {
    let schliessen: () -> Void
    @ViewBuilder let inhalt: () -> Inhalt
    @State private var sichtbar = false
    @State private var zug: CGFloat = 0
    @State private var schliesst = false

    /// Höhe des Bereichs oben, an dem sich das Blatt nach unten ziehen lässt (Kopf mit X, Titel und ⋮).
    private let griffHoehe: CGFloat = 72

    var body: some View {
        GeometryReader { geo in
            let gesamt = geo.size.height + geo.safeAreaInsets.top + geo.safeAreaInsets.bottom
            let blattHoehe = gesamt * Detailblatt.iPhoneDetailAnteil
            // Oberkante fest bei 10 % der Bildschirmhöhe; unten endet das Blatt am sicheren Bereich (bzw. an der Tastatur)
            let innenHoehe = max(geo.size.height + geo.safeAreaInsets.top - (gesamt - blattHoehe), 120)
            ZStack(alignment: .bottom) {
                Color.black
                    .opacity(sichtbar ? 0.32 * Double(1 - min(zug / max(blattHoehe, 1), 1)) : 0)
                    .ignoresSafeArea()
                    .contentShape(Rectangle())
                    .onTapGesture { zuklappen(blattHoehe) }
                    .accessibilityHidden(true)
                inhalt()
                    .environment(\.blattSchliessen, { zuklappen(blattHoehe) })
                    .frame(maxWidth: .infinity)
                    .frame(height: innenHoehe, alignment: .top)
                    .clipShape(UnevenRoundedRectangle(topLeadingRadius: Detailblatt.radius, bottomLeadingRadius: 0,
                                                      bottomTrailingRadius: 0, topTrailingRadius: Detailblatt.radius,
                                                      style: .continuous))
                    .background(alignment: .top) {
                        UnevenRoundedRectangle(topLeadingRadius: Detailblatt.radius, bottomLeadingRadius: 0,
                                               bottomTrailingRadius: 0, topTrailingRadius: Detailblatt.radius,
                                               style: .continuous)
                            .fill(Farben.blatt)
                            .ignoresSafeArea(edges: .bottom)
                            .shadow(color: .black.opacity(0.12), radius: 10, y: -2)
                    }
                    .simultaneousGesture(ziehen(blattHoehe, kopfUnten: geo.size.height - innenHoehe + griffHoehe))
                    .offset(y: sichtbar ? zug : blattHoehe + 60)
                    .accessibilityAction(.escape) { zuklappen(blattHoehe) }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            .coordinateSpace(NamedCoordinateSpace.named(Self.raum))
        }
        .onAppear {
            withAnimation(.spring(response: 0.36, dampingFraction: 0.92)) { sichtbar = true }
        }
    }

    private static var raum: String { "randblatt" }

    /// Nach unten ziehen am Kopf des Blatts (nicht in der Liste, die selbst rollt). Gemessen im festen Koordinatenraum
    /// des Vollbilds, damit das mitwandernde Blatt die Strecke nicht verfälscht.
    private func ziehen(_ hoehe: CGFloat, kopfUnten: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 10, coordinateSpace: NamedCoordinateSpace.named(Self.raum))
            .onChanged { wert in
                guard !schliesst, wert.startLocation.y <= kopfUnten else { return }
                zug = max(0, wert.translation.height)
            }
            .onEnded { wert in
                guard !schliesst, wert.startLocation.y <= kopfUnten else { return }
                let weit = wert.translation.height > hoehe * 0.25
                let schnell = wert.predictedEndTranslation.height > hoehe * 0.45
                if weit || schnell {
                    zuklappen(hoehe)
                } else {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.9)) { zug = 0 }
                }
            }
    }

    /// Blatt ausfahren, danach die Präsentation ohne weitere Animation beenden.
    private func zuklappen(_ hoehe: CGFloat) {
        guard !schliesst else { return }
        schliesst = true
        Tastatur.schliessen()
        withAnimation(.easeIn(duration: 0.22)) {
            sichtbar = false
            zug = 0
        } completion: {
            schliessen()
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

    private func glas<V: View>(_ inhalt: V) -> some View {
        inhalt.liquidGlas(Circle())
    }
}

/// Liquid Glass für Knöpfe in Kopfzeilen (Kreis bzw. Kapsel): ab iOS 26 echtes, interaktives Glas, davor ein
/// Milchglas-Feld (ultraThinMaterial) mit feinem hellen Rand und weichem Schatten.
struct Liquidglas<Form: InsettableShape>: ViewModifier {
    let form: Form

    func body(content: Content) -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26.0, *) {
            content.glassEffect(.regular.interactive(), in: form)
        } else {
            milchglas(content)
        }
        #else
        milchglas(content)
        #endif
    }

    private func milchglas<V: View>(_ inhalt: V) -> some View {
        inhalt
            .background {
                form
                    .fill(.ultraThinMaterial)
                    .overlay {
                        form
                            .strokeBorder(Color(hell: 0xFFFFFF, dunkel: 0xFFFFFF, deckkraftHell: 0.8, deckkraftDunkel: 0.16), lineWidth: 0.5)
                    }
                    .shadow(color: Color(hell: 0x141C24, dunkel: 0x000000, deckkraftHell: 0.14, deckkraftDunkel: 0.4), radius: 6, y: 3)
            }
    }
}

extension View {
    /// Liquid Glass (ab iOS 26) bzw. Milchglas mit feinem Rand – wie die runden Knöpfe der Kopfzeilen.
    func liquidGlas<Form: InsettableShape>(_ form: Form) -> some View {
        modifier(Liquidglas(form: form))
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
    /// Lädt der Inhalt? Dann läuft unten am Kopf der orange Ladebalken.
    var laedt: Bool = false

    init(symbol: String, symbolText: String, aktion: @escaping () -> Void, laedt: Bool = false,
         @ViewBuilder mitte: () -> Mitte, @ViewBuilder rechts: () -> Rechts) {
        self.symbol = symbol
        self.symbolText = symbolText
        self.aktion = aktion
        self.laedt = laedt
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
        .overlay(alignment: .bottom) {
            Ladebalken(aktiv: laedt)
        }
    }
}

extension Blattkopf where Rechts == EmptyView {
    init(symbol: String, symbolText: String, aktion: @escaping () -> Void, laedt: Bool = false, @ViewBuilder mitte: () -> Mitte) {
        self.init(symbol: symbol, symbolText: symbolText, aktion: aktion, laedt: laedt, mitte: mitte, rechts: { EmptyView() })
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
    /// Lädt die Seite? Dann läuft unter dem Kopf der orange Ladebalken.
    let laedt: Bool
    @Environment(\.dismiss) private var schliessen

    init(titel: String, untertitel: String? = nil, symbol: String = "chevron.left", zurueck: (() -> Void)? = nil,
         laedt: Bool = false, @ViewBuilder inhalt: () -> Inhalt, @ViewBuilder rechts: () -> Rechts) {
        self.titel = titel
        self.untertitel = untertitel
        self.symbol = symbol
        self.zurueck = zurueck
        self.laedt = laedt
        self.inhalt = inhalt()
        self.rechts = rechts()
    }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: symbol, symbolText: symbol == "xmark" ? "Schließen" : "Zurück", aktion: {
                if let zurueck { zurueck() } else { schliessen() }
            }, laedt: laedt, mitte: {
                Blatttitel(titel: titel, untertitel: untertitel)
            }, rechts: {
                rechts
            })
            inhalt
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .background(Farben.blatt.ignoresSafeArea())
        // Keine eigene Tastatur-Logik: Textfelder weichen der Tastatur über den Sicherheitsbereich aus (System);
        // beim Verlassen der Seite wird der Fokus abgegeben, damit nichts verschoben bleibt
        .onDisappear { Tastatur.schliessen() }
        .toolbar(.hidden, for: .navigationBar)
    }
}

extension DetailSeitenRahmen where Rechts == EmptyView {
    init(titel: String, untertitel: String? = nil, symbol: String = "chevron.left", zurueck: (() -> Void)? = nil,
         laedt: Bool = false, @ViewBuilder inhalt: () -> Inhalt) {
        self.init(titel: titel, untertitel: untertitel, symbol: symbol, zurueck: zurueck, laedt: laedt, inhalt: inhalt, rechts: { EmptyView() })
    }
}

/// Runder Knopf „Sichern“ (Haken) rechts im Kopf einer Unterseite.
struct SichernKnopf: View {
    let speichert: Bool
    let aktiv: Bool
    var symbol: String = "checkmark"
    var beschriftung: String = "Sichern"
    let aktion: () -> Void

    var body: some View {
        if speichert {
            ProgressView()
                .frame(width: Detailmass.knopf, height: Detailmass.knopf)
        } else {
            Button(action: aktion) {
                Image(systemName: symbol)
                    .font(.system(size: 17, weight: .medium))
                    .foregroundStyle(aktiv ? Color.white : Farben.gedaempft)
                    .frame(width: Detailmass.knopf, height: Detailmass.knopf)
                    .background(aktiv ? Farben.akzent : Farben.knopf, in: Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .disabled(!aktiv)
            .accessibilityLabel(beschriftung)
        }
    }
}

// MARK: - Zeilen der Liste

/// Graue runde Zahl (Original: Kreis 31 pt, schiefergrau) mit weißer Ziffer – auch bei 0.
struct GrauePlakette: View {
    let zahl: Int

    var body: some View {
        // Einen Ticken kleiner als zuvor (31 pt / 17 pt)
        Text("\(zahl)")
            .font(.system(size: 15.5))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .frame(minWidth: 28, minHeight: 28)
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
            Listensymbol(name: symbol)
            VStack(alignment: .leading, spacing: 3) {
                // Menütext eine Stufe kräftiger (Regular statt Light), Untertitel etwas dunkler (Vorlage 140)
                Text(titel)
                    .font(.system(size: 19, weight: .regular))
                    .foregroundStyle(Farben.detailText)
                    .lineLimit(1)
                if let untertitel, !untertitel.isEmpty {
                    Text(untertitel.replacingOccurrences(of: "\n", with: " "))
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.detailGrau)
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
    /// Statuspille rechts neben dem Fach („PRÜFUNG“, „VERANSTALTUNG“) wie in Vorlage 140
    case pille(String)
}

/// Wertzeile im Kopf der Details – nur Text, keine Plaketten: Änderungen als „alt → neu“ (alt durchgestrichen),
/// fehlende Werte (abwesende Lehrkraft bzw. Klasse) durchgestrichen. Genau eine Zeile; bei vielen Werten lässt sie sich
/// waagrecht scrollen. Mit `kuerzenAb`: Sind es mehr Werte (z. B. alle Lehrkräfte oder alle Klassen einer
/// Veranstaltung), stehen nur die ersten und dahinter „… +N“; Antippen klappt die ganze Liste umbrechend auf (und
/// wieder zu).
struct Wertzeile: View {
    let bausteine: [Wertbaustein]
    var schrift: CGFloat = 13
    /// Raum und Lehrkraft eine Stufe dunkler als zuvor (Vorlage 140)
    var farbe: Color = Farben.detailGrau
    /// Ab mehr als so vielen Werten kürzen (nil = nie)
    var kuerzenAb: Int? = nil
    @State private var aufgeklappt = false

    /// Übliche Grenze im Kopf der Stundendetails: mehr als 6 Werte werden gekürzt.
    static let kuerzungsgrenze = 6

    /// Werte der Zeile: Bausteine zwischen den Trennstrichen (ohne Pille).
    private var gruppen: [[Wertbaustein]] {
        var ergebnis: [[Wertbaustein]] = [[]]
        for b in bausteine {
            switch b {
            case .trenner:
                ergebnis.append([])
            case .pille:
                continue
            default:
                ergebnis[ergebnis.count - 1].append(b)
            }
        }
        return ergebnis.filter { !$0.isEmpty }
    }

    private var pille: String? {
        for b in bausteine { if case .pille(let t) = b { return t } }
        return nil
    }

    private var gekuerzt: Bool {
        guard let grenze = kuerzenAb else { return false }
        return gruppen.count > grenze
    }

    var body: some View {
        if gekuerzt, let grenze = kuerzenAb {
            let alle = gruppen
            let sichtbar = max(grenze - 1, 1)
            if aufgeklappt {
                VStack(alignment: .leading, spacing: 4) {
                    Text(Self.fliesstext(alle, trenner: Farben.detailTrenner))
                        .font(.system(size: schrift))
                        .foregroundStyle(farbe)
                        .lineLimit(nil)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    if let pille { Statuspille(text: pille) }
                }
                .padding(.vertical, 3)
                .contentShape(Rectangle())
                .onTapGesture { withAnimation(.easeInOut(duration: 0.2)) { aufgeklappt = false } }
                .accessibilityAddTraits(.isButton)
                .accessibilityHint("Liste einklappen")
            } else {
                EinzeilenScroller {
                    HStack(spacing: 4) {
                        ForEach(Array(Self.verbunden(Array(alle.prefix(sichtbar))).enumerated()), id: \.offset) { eintrag in
                            baustein(eintrag.element)
                        }
                        Button {
                            withAnimation(.easeInOut(duration: 0.2)) { aufgeklappt = true }
                        } label: {
                            Text("… +\(alle.count - sichtbar)")
                                .foregroundStyle(Farben.detailGrau)
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("\(alle.count - sichtbar) weitere anzeigen")
                        if let pille { baustein(.pille(pille)) }
                    }
                    .font(.system(size: schrift))
                    .foregroundStyle(farbe)
                }
            }
        } else {
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
    }

    /// Gruppen wieder mit Trennstrichen verbinden.
    private static func verbunden(_ gruppen: [[Wertbaustein]]) -> [Wertbaustein] {
        var ergebnis: [Wertbaustein] = []
        for g in gruppen {
            if !ergebnis.isEmpty { ergebnis.append(.trenner) }
            ergebnis.append(contentsOf: g)
        }
        return ergebnis
    }

    /// Aufgeklappte Liste als ein umbrechender Text (durchgestrichene Werte bleiben durchgestrichen).
    private static func fliesstext(_ gruppen: [[Wertbaustein]], trenner: Color) -> AttributedString {
        var text = AttributedString()
        for (i, g) in gruppen.enumerated() {
            if i > 0 {
                var t = AttributedString(" | ")
                t.foregroundColor = trenner
                text.append(t)
            }
            for (j, b) in g.enumerated() {
                if j > 0 { text.append(AttributedString(" ")) }
                switch b {
                case .text(let w), .neu(let w):
                    text.append(AttributedString(w))
                case .weg(let w), .offen(let w):
                    var t = AttributedString(w)
                    t.strikethroughStyle = .single
                    text.append(t)
                case .pfeil:
                    text.append(AttributedString("→"))
                case .trenner, .pille:
                    break
                }
            }
        }
        return text
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
        case .pille(let t):
            Statuspille(text: t)
                .padding(.leading, 12)
        }
    }

    /// Bausteine aus Werten: geänderte Werte als „alt → neu“ (nur Räume, `pfeil`) bzw. „alt | neu“ (Lehrkräfte, Klassen),
    /// offene Lehrkraft (kein neuer Wert) durchgestrichen.
    static func bausteine(_ werte: [Wert], durchgestrichen: Bool, offenAlsPlakette: Bool, pfeil: Bool = false) -> [Wertbaustein] {
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
                    teil.append(pfeil ? .pfeil : .trenner)
                }
                // Hinzugekommener Wert (plus) mit „+“ davor, z. B. „+B070“
                if !w.k.isEmpty { teil.append(.text(w.plus ? "+" + w.k : w.k)) }
            }
            guard !teil.isEmpty else { continue }
            if !ergebnis.isEmpty { ergebnis.append(.trenner) }
            ergebnis.append(contentsOf: teil)
        }
        return ergebnis
    }
}

/// Statuspille genau nach Vorlage 140: Großbuchstaben, weiße Schrift auf grauer Kapsel (#8e99a3), rechts neben dem Fach.
struct Statuspille: View {
    let text: String

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 14, weight: .regular))
            .tracking(0.3)
            .foregroundStyle(.white)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 8.5)
            .padding(.vertical, 1.5)
            .background(Farben.statusPilleFlaeche, in: Capsule())
            .accessibilityLabel(text)
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

// MARK: - Symbole der Liste

/// Symbol links in der Detailliste – alle gleich stark: Vektorgrafiken aus Assets.xcassets (Name beginnt mit „Symbol“,
/// als Vorlage eingefärbt) werden in ihrer Grundgröße gezeichnet, sodass jede Linie 1,5 pt breit ist und die Zeichen
/// gleich groß wirken (≈ 24 pt); SF Symbols ohne Vektorgrafik in leichter Stärke mit gleicher Linienbreite. Grau in
/// einem festen Feld (Standard 30 pt), damit die Titel bündig stehen.
struct Listensymbol: View {
    let name: String
    var groesse: CGFloat = 30

    /// Grundgröße der Vektorgrafiken in pt (Zeichenfläche × Faktor, damit die Linie 1,5 pt breit wird).
    static func grundgroesse(_ name: String) -> CGSize {
        switch name {
        // 28er-Zeichenfläche mit 1,5er-Linien (Raum, Klasse, Lehrkraft, Schüler*innen, Hausaufgaben)
        case "SymbolRaum", "SymbolKlasse", "SymbolLehrer", "SymbolSchuelerPlan", "SymbolHausaufgaben":
            return CGSize(width: 28, height: 28)
        // Kamera: Zeichenfläche 22 mit 1,35er-Linien
        case "SymbolOnline":
            return CGSize(width: 24.4, height: 24.4)
        // Gruppe: 24 × 18 mit 1,5er-Linien
        case "SymbolSchueler":
            return CGSize(width: 24, height: 18)
        default:
            return CGSize(width: 24, height: 24)
        }
    }

    var body: some View {
        // Kleinere Felder (z. B. 26 pt in der Raumliste) verkleinern alles im selben Verhältnis
        let faktor = min(1, groesse / 30)
        Group {
            if name.hasPrefix("Symbol") {
                let g = Self.grundgroesse(name)
                Image(name)
                    .renderingMode(.template)
                    .resizable()
                    .scaledToFit()
                    .frame(width: g.width * faktor, height: g.height * faktor)
            } else {
                Image(systemName: name)
                    .font(.system(size: 21 * faktor, weight: .light))
                    .frame(width: 24 * faktor, height: 24 * faktor)
            }
        }
        .foregroundStyle(Farben.gedaempft)
        .frame(width: groesse, height: groesse)
        .accessibilityHidden(true)
    }
}

// MARK: - Schüler*innen-Plakette

/// Zahl der Abwesenden wie in der Vorlage: dunkelrote Scheibe (grau bei 0), oben rechts ein kleines Häkchen,
/// wenn die Abwesenheiten der Stunde kontrolliert wurden.
struct SchuelerPlakette: View {
    let zahl: Int
    let kontrolliert: Bool

    var body: some View {
        // Einen Ticken kleiner als zuvor (31 pt / 17 pt)
        Text("\(zahl)")
            .font(.system(size: 15.5))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .frame(minWidth: 28, minHeight: 28)
            .background(zahl > 0 ? Farben.abwesendRot : Farben.zahlPlakette, in: Capsule())
            .overlay(alignment: .topTrailing) {
                if kontrolliert {
                    Image(systemName: "checkmark")
                        .font(.system(size: 6.5, weight: .medium))
                        .foregroundStyle(.white)
                        .frame(width: 12, height: 12)
                        .background(Farben.zahlPlakette, in: Circle())
                        .overlay { Circle().strokeBorder(Farben.blatt, lineWidth: 1) }
                        .offset(x: 3, y: -3)
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(kontrolliert ? "\(zahl) abwesend, kontrolliert" : "\(zahl) abwesend")
    }
}

// MARK: - Raumbuchung in den Stundendetails

/// Ziel der Raumliste: Raum ersetzen (alt) bzw. zusätzlichen Raum hinzufügen.
struct RaumZiel: Hashable, Identifiable {
    /// ersetzen oder hinzufuegen
    let aktion: String
    let alt: String

    var id: String { "\(aktion)|\(alt)" }
}

/// Raumzeile wie in der PWA: jeder Raum als Chip (Tippen: „Ersetzen“ / „Löschen“), rechts „+“ für einen weiteren Raum.
struct RaumZeile: View {
    let raeume: [Wert]
    @Binding var ziel: RaumZiel?
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var fehler: String? = nil
    @State private var arbeitet = false

    /// Aktuelle Räume (ohne weggefallene).
    private var aktuelle: [String] {
        var liste: [String] = []
        for w in raeume where !w.k.isEmpty && !liste.contains(where: { $0.caseInsensitiveCompare(w.k) == .orderedSame }) {
            liste.append(w.k)
        }
        return liste
    }

    var body: some View {
        HStack(spacing: 12) {
            Listensymbol(name: "SymbolRaum")
            EinzeilenScroller {
                HStack(spacing: 8) {
                    if aktuelle.isEmpty {
                        Text("Kein Raum")
                            .font(.system(size: 17))
                            .foregroundStyle(Farben.gedaempft)
                    }
                    ForEach(aktuelle, id: \.self) { raum in
                        Menu {
                            Button {
                                ziel = RaumZiel(aktion: "ersetzen", alt: raum)
                            } label: {
                                Label("Ersetzen", systemImage: "arrow.left.arrow.right")
                            }
                            Button(role: .destructive) {
                                Task { await loeschen(raum) }
                            } label: {
                                Label("Löschen", systemImage: "trash")
                            }
                            .disabled(aktuelle.count < 2)
                        } label: {
                            Text(raum)
                                .font(.system(size: 17))
                                .foregroundStyle(Farben.detailText)
                                .lineLimit(1)
                                .padding(.horizontal, 12)
                                .padding(.vertical, 5)
                                .background(Farben.knopf, in: Capsule())
                        }
                        .tint(Color.primary)
                        .accessibilityLabel("Raum \(raum)")
                        .accessibilityHint("Ersetzen oder löschen")
                    }
                }
            }
            if arbeitet {
                ProgressView()
                    .frame(width: 31, height: 31)
            } else {
                Button {
                    ziel = RaumZiel(aktion: "hinzufuegen", alt: "")
                } label: {
                    Image(systemName: "plus")
                        .font(.system(size: 16, weight: .regular))
                        .foregroundStyle(Farben.tinte2)
                        .frame(width: 31, height: 31)
                        .background(Farben.knopf, in: Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Raum hinzufügen")
            }
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, 22)
        .padding(.vertical, 10)
        .frame(minHeight: 56)
        .alert("Nicht gespeichert", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private func loeschen(_ raum: String) async {
        arbeitet = true
        defer { arbeitet = false }
        do {
            let meldung = try await daten.raumAendern(aktion: "loeschen", raum: raum, alt: nil)
            withAnimation { daten.hinweis = meldung }
            zustand.planGeaendert += 1
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}
