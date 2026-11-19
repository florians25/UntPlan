import SwiftUI

/// Werte (Lehrkräfte, Klassen, Räume) als Text: alter Wert durchgestrichen, geänderter Wert grün.
/// `offen`: Lehrkräfte einer offenen Vertretung – die alte Lehrkraft ohne neuen Wert steht durchgestrichen auf grüner Fläche.
func werteText(_ werte: [Wert], trenner: String = ", ", offen: Bool = false) -> Text {
    var ergebnis = Text("")
    for (i, w) in werte.enumerated() {
        if i > 0 { ergebnis = ergebnis + Text(trenner) }
        if offen, w.k.isEmpty, w.neu || !(w.alt ?? "").isEmpty {
            let alt = w.alt ?? ""
            ergebnis = ergebnis + offenText(alt.isEmpty ? "???" : alt)
            continue
        }
        if let alt = w.alt, !alt.isEmpty, alt.caseInsensitiveCompare(w.k) != .orderedSame {
            ergebnis = ergebnis + Text(alt).strikethrough(true, color: Farben.entfallRand)
            if !w.k.isEmpty { ergebnis = ergebnis + Text(" ") }
        }
        if w.geaendert {
            ergebnis = ergebnis + Text(w.k).foregroundColor(Farben.gruen).fontWeight(.medium)
        } else {
            ergebnis = ergebnis + Text(w.k)
        }
    }
    return ergebnis
}

/// Grün hinterlegter, durchgestrichener Text (offene Vertretung) – als Text, damit er sich mit anderen Texten verbinden lässt.
func offenText(_ text: String) -> Text {
    var a = AttributedString("\u{2009}\(text)\u{2009}")
    a[AttributeScopes.SwiftUIAttributes.BackgroundColorAttribute.self] = Farben.gruen
    a[AttributeScopes.SwiftUIAttributes.ForegroundColorAttribute.self] = Farben.gruenSchrift
    a[AttributeScopes.SwiftUIAttributes.StrikethroughStyleAttribute.self] = Text.LineStyle(pattern: .solid, color: Farben.gruenSchrift)
    return Text(a).fontWeight(.medium)
}

/// Offene Vertretung erkennen: Karte „offen“ bzw. Lehrkraft ohne neuen Wert mit gesetztem alten Wert (nicht bei Entfall).
func istOffeneVertretung(_ karte: Karte) -> Bool {
    karte.istOffeneVertretung
}

/// Werte als einfacher Text (ohne Hervorhebung).
func werteKurz(_ werte: [Wert]) -> String {
    werte.map(\.k).filter { !$0.isEmpty }.joined(separator: ", ")
}

/// Zeilen einer Karte je nach Planart: Klassenplan zeigt Lehrkraft und Raum, Lehrerplan Klasse und Raum, Raumplan Klasse und Lehrkraft.
func kartenzeilen(_ karte: Karte, typ: String) -> [[Wert]] {
    switch typ {
    case "lehrer": return [karte.klassen, karte.raeume]
    case "raum": return [karte.klassen, karte.lehrer]
    default: return [karte.lehrer, karte.raeume]
    }
}

/// Überschrift eines Abschnitts auf der Startseite.
struct Abschnittstitel: View {
    let text: String
    var zusatz: String?

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(text)
                .font(.title3.weight(.medium))
            Spacer()
            if let zusatz {
                Text(zusatz)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// Rote Fehlerzeile.
struct Fehlerhinweis: View {
    let text: String

    var body: some View {
        Label(text, systemImage: "exclamationmark.triangle.fill")
            .font(.subheadline)
            .foregroundStyle(Farben.entfallRand)
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(hell: 0xFFF3F2, dunkel: 0x3A1018), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

/// Kleines Kennzeichen (Änderung, Entfall, Zusätzliche Unterrichtsstunde …) wie in der Vorlage:
/// normale Schreibweise, dezent hinterlegt, Schrift in der Kennfarbe.
struct Plakette: View {
    let text: String
    let farbe: Color

    var body: some View {
        Text(text)
            .font(.system(size: 12.5, weight: .medium))
            .foregroundStyle(farbe)
            .padding(.horizontal, 7)
            .padding(.vertical, 2.5)
            .background(farbe.opacity(0.13), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
            .lineLimit(1)
    }
}

/// Runder Kreis mit Initialen.
struct Initialen: View {
    let name: String
    var groesse: CGFloat = 40

    var body: some View {
        Text(Self.kuerzel(name))
            .font(.system(size: groesse * 0.38, weight: .medium))
            .foregroundStyle(.white)
            .frame(width: groesse, height: groesse)
            .background(Farben.akzent, in: Circle())
    }

    /// "Muster Erika" wird zu "ME".
    static func kuerzel(_ name: String) -> String {
        let teile = name.split(whereSeparator: { $0 == " " || $0 == "-" }).filter { !$0.isEmpty }
        guard let erstes = teile.first?.first else { return "?" }
        var s = String(erstes)
        if teile.count > 1, let zweites = teile[1].first { s.append(zweites) }
        return s.uppercased()
    }
}

/// Weiße Karte mit abgerundeten Ecken und weichem Schatten (Startseite, wie .newscard/.daycard).
struct Kartenhintergrund: ViewModifier {
    var radius: CGFloat = 22

    func body(content: Content) -> some View {
        content
            .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: radius, style: .continuous))
            .weicherSchatten()
    }
}

/// Schatten 0 1 2 rgba(21,33,44,.05), 0 10 26 rgba(21,33,44,.08); im Dunkeln keiner.
struct WeicherSchatten: ViewModifier {
    func body(content: Content) -> some View {
        content
            .shadow(color: Color(hell: 0x15212C, dunkel: 0x000000, deckkraftHell: 0.05, deckkraftDunkel: 0), radius: 1, y: 1)
            .shadow(color: Color(hell: 0x15212C, dunkel: 0x000000, deckkraftHell: 0.08, deckkraftDunkel: 0), radius: 13, y: 10)
    }
}

/// Glasfläche wie in der PWA (Milchglas, Glanzkante, weicher Schatten).
struct Glasflaeche<Form: InsettableShape>: ViewModifier {
    let form: Form

    func body(content: Content) -> some View {
        content
            .background {
                form
                    .fill(.ultraThinMaterial)
                    .overlay {
                        form.fill(LinearGradient(colors: [Color(hell: 0xFFFFFF, dunkel: 0x3A3A3E, deckkraftHell: 0.55, deckkraftDunkel: 0.55),
                                                          Color(hell: 0xFFFFFF, dunkel: 0x1E1E21, deckkraftHell: 0.30, deckkraftDunkel: 0.45)],
                                                 startPoint: .top, endPoint: .bottom))
                    }
                    .overlay {
                        form.strokeBorder(Color(hell: 0xFFFFFF, dunkel: 0xFFFFFF, deckkraftHell: 0.75, deckkraftDunkel: 0.14), lineWidth: 0.5)
                    }
                    .shadow(color: Color(hell: 0x141C24, dunkel: 0x000000, deckkraftHell: 0.10, deckkraftDunkel: 0.35), radius: 9, y: 6)
            }
    }
}

extension View {
    func alsKarte(radius: CGFloat = 22) -> some View {
        modifier(Kartenhintergrund(radius: radius))
    }

    func weicherSchatten() -> some View {
        modifier(WeicherSchatten())
    }

    func glas<Form: InsettableShape>(_ form: Form) -> some View {
        modifier(Glasflaeche(form: form))
    }
}

/// Kalenderblatt als Linienzeichen mit Tageszahl oder Stern (Reiter „Stundenplan“, „Mein Stundenplan“).
struct KalenderSymbol: View {
    enum Inhalt {
        case tag(Int)
        case stern
    }

    let inhalt: Inhalt
    var groesse: CGFloat = 24
    var strich: CGFloat = 1.6

    var body: some View {
        let b = groesse
        ZStack(alignment: .top) {
            RoundedRectangle(cornerRadius: b * 0.16, style: .continuous)
                .strokeBorder(lineWidth: strich)
                .frame(width: b * 0.86, height: b * 0.78)
                .offset(y: b * 0.16)
            Rectangle()
                .frame(width: b * 0.86, height: strich)
                .offset(y: b * 0.36)
            HStack(spacing: b * 0.34) {
                Capsule().frame(width: strich, height: b * 0.2)
                Capsule().frame(width: strich, height: b * 0.2)
            }
            .offset(y: b * 0.06)
            Group {
                switch inhalt {
                case .tag(let tag):
                    Text("\(tag)")
                        .font(.system(size: b * 0.36, weight: .medium))
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                case .stern:
                    Image(systemName: "star")
                        .font(.system(size: b * 0.34, weight: .medium))
                }
            }
            .frame(width: b * 0.8, height: b * 0.5)
            .offset(y: b * 0.4)
        }
        .frame(width: b, height: b, alignment: .top)
    }
}

/// Graue runde Zahl-Plakette (Startseite, z. B. Abwesenheiten „1“).
struct ZahlPlakette: View {
    let zahl: Int

    var body: some View {
        Text("\(zahl)")
            .font(.system(size: 15, weight: .medium))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .frame(minWidth: 26, minHeight: 26)
            .background(Farben.zahlPlakette, in: Capsule())
    }
}

// MARK: - Obere Leiste, Konto, Listen

/// Runder Knopf oben links/rechts. In der Navigationsleiste zeichnet ab iOS 26 das System das Glas selbst,
/// davor (und außerhalb von Leisten) die eigene Glasfläche.
struct Glasknopf<Inhalt: View>: View {
    var inLeiste: Bool = true
    /// Breite in der Systemleiste (iOS 26): in einer Gruppe mit mehreren Knöpfen schmaler, damit die gemeinsame
    /// Glaskapsel so kompakt wird wie im Original (Vorlage 173: Zeichen 52 pt auseinander, Kapsel 96 pt breit)
    var breite: CGFloat = 34
    @ViewBuilder let inhalt: () -> Inhalt

    /// Ab iOS 26 zeichnet die Navigationsleiste das Glas selbst.
    private var systemGlas: Bool {
        if #available(iOS 26.0, *) { return inLeiste }
        return false
    }

    var body: some View {
        if systemGlas {
            inhalt()
                .foregroundStyle(Farben.tinte2)
                .frame(width: breite, height: 34)
                .contentShape(Rectangle())
        } else {
            inhalt()
                .foregroundStyle(Farben.tinte2)
                .frame(width: 44, height: 44)
                .glas(Circle())
                .contentShape(Circle())
        }
    }
}

/// Person mit Auf-/Ab-Pfeilen unten rechts (Profil wechseln) wie in der Vorlage: eine Vektorgrafik (ProfilWechsel)
/// mit durchgehend gleicher Strichstärke, die Pfeile (ab, auf) unten rechts neben der Schulter.
struct KontoSymbol: View {
    var groesse: CGFloat = 26

    var body: some View {
        Image("ProfilWechsel")
            .renderingMode(.template)
            .resizable()
            .scaledToFit()
            .frame(width: groesse, height: groesse)
            .foregroundStyle(Farben.gedaempft)
            .offset(x: 1)
            .accessibilityHidden(true)
    }
}

/// Profilwechsel wie im Original (Vorlage bc097b4b): ein Glasfeld oben links unter dem Profilknopf mit „Profil wechseln“
/// und der Liste der Konten (wechseln ohne Abmelden). „Konten verwalten …“ öffnet die bisherige Kontenauswahl
/// (Konto hinzufügen bzw. entfernen).
struct KontoMenue<Beschriftung: View>: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var zeigen = false
    @State private var verwalten = false
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @ViewBuilder let beschriftung: () -> Beschriftung

    var body: some View {
        Button {
            var t = Transaction()
            t.disablesAnimations = true
            withTransaction(t) { zeigen = true }
        } label: {
            beschriftung()
        }
        .accessibilityLabel("Profil wechseln")
        .accessibilityHint("Öffnet die Liste der Konten")
        .fullScreenCover(isPresented: $zeigen) {
            KontoPanel(schliessen: {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { zeigen = false }
            }, verwalten: {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { zeigen = false }
                verwalten = true
            })
            .environmentObject(zustand)
            .presentationBackground(.clear)
        }
        // Kontenverwaltung (hinzufügen, entfernen): am iPhone das randlose Blatt
        .blattPraesentation(isPresented: $verwalten, breit: groessenklasse == .regular) {
            KontenauswahlView()
                .environmentObject(zustand)
        }
    }
}

/// Glasfeld „Profil wechseln“ (Vorlage bc097b4b, 3 px/pt): 250 pt breit, 8,67 pt vom linken Rand, Oberkante am sicheren
/// Bereich (62 pt), höchstens 520 pt hoch, Ecken ≈ 34 pt, Milchglas mit 1-pt-Rand und weichem Schatten. Kopf „Profil
/// wechseln“ 14 pt grau (44 pt vom Rand); je Konto Name 17 pt, darunter die Schule 15 pt grau (umbrechend, Zeilen 18 pt),
/// links ein Haken für das aktive Konto (Mitte 27,7 pt) bzw. ein Schloss für Konten, die eine neue Anmeldung brauchen
/// (Mitte 55 pt); Text ab 79,67 pt; Zeilen ≈ 60 pt (einzeilige Schule). Antippen daneben schließt.
struct KontoPanel: View {
    let schliessen: () -> Void
    let verwalten: () -> Void
    @EnvironmentObject var zustand: AppZustand
    @State private var sichtbar = false
    @State private var inhaltHoehe: CGFloat = 0

    static let breite: CGFloat = 250
    static let links: CGFloat = 8.67
    static let maxHoehe: CGFloat = 520
    static let radius: CGFloat = 34

    private var aktivId: String? { Kontenspeicher.aktivId }

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .topLeading) {
                Color.black.opacity(0.001)
                    .ignoresSafeArea()
                    .contentShape(Rectangle())
                    .onTapGesture { zuklappen() }
                    .accessibilityHidden(true)
                panel(maxHoehe: min(Self.maxHoehe, geo.size.height - 16))
                    .padding(.leading, Self.links)
                    .scaleEffect(sichtbar ? 1 : 0.6, anchor: .topLeading)
                    .opacity(sichtbar ? 1 : 0)
            }
        }
        .onAppear {
            withAnimation(.spring(response: 0.32, dampingFraction: 0.86)) { sichtbar = true }
        }
        .accessibilityAction(.escape) { zuklappen() }
    }

    private func panel(maxHoehe: CGFloat) -> some View {
        let form = RoundedRectangle(cornerRadius: Self.radius, style: .continuous)
        return VStack(alignment: .leading, spacing: 0) {
            Text("Profil wechseln")
                .font(.system(size: 14))
                .foregroundStyle(Color(hell: 0x87878B, dunkel: 0x98989F))
                .padding(.leading, 44)
                .padding(.top, 19)
                .padding(.bottom, 15.5)
                .accessibilityAddTraits(.isHeader)
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(zustand.konten) { konto in
                        Button {
                            waehlen(konto)
                        } label: {
                            zeile(konto)
                        }
                        .buttonStyle(OhneTippStil())
                    }
                    Button {
                        verwalten()
                    } label: {
                        eintrag(name: "Konten verwalten …", schule: nil, symbol: "person.badge.plus", aktiv: false)
                    }
                    .buttonStyle(OhneTippStil())
                }
                .padding(.bottom, 10)
                .background(GeometryReader { g in
                    Color.clear.preference(key: KontoPanelHoehe.self, value: g.size.height)
                })
            }
            .scrollIndicators(.visible)
            .frame(height: min(max(inhaltHoehe, 60), maxHoehe - 46))
            .onPreferenceChange(KontoPanelHoehe.self) { inhaltHoehe = $0 }
        }
        .frame(width: Self.breite, alignment: .leading)
        .background(.regularMaterial, in: form)
        .overlay { form.strokeBorder(Color(hell: 0xFFFFFF, dunkel: 0xFFFFFF, deckkraftHell: 1, deckkraftDunkel: 0.16), lineWidth: 1) }
        .clipShape(form)
        .shadow(color: Color(hell: 0x1E2B33, dunkel: 0x000000, deckkraftHell: 0.16, deckkraftDunkel: 0.45), radius: 18, y: 8)
    }

    private func zeile(_ konto: Konto) -> some View {
        // Schloss: kein gemerktes Passwort – nach Ablauf der Sitzung ist eine neue Anmeldung nötig
        let anmelden = konto.id != aktivId && Schluesselbund.lesen(konto: konto.id) == nil
        let schule = konto.schulname.isEmpty ? konto.serverKurz : konto.schulname
        return eintrag(name: konto.name, schule: schule, symbol: anmelden ? "lock" : nil, aktiv: konto.id == aktivId)
            .contextMenu {
                Button(role: .destructive) {
                    verwalten()
                } label: {
                    Label("Konten verwalten …", systemImage: "person.2")
                }
            }
            .accessibilityLabel("\(konto.name), \(schule)\(konto.id == aktivId ? ", aktiv" : "")\(anmelden ? ", Anmeldung nötig" : "")")
    }

    private func eintrag(name: String, schule: String?, symbol: String?, aktiv: Bool) -> some View {
        HStack(alignment: .center, spacing: 0) {
            ZStack {
                if aktiv {
                    Image(systemName: "checkmark")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(Color.primary)
                }
            }
            .frame(width: 20)
            .padding(.leading, 17.7)
            ZStack {
                if let symbol {
                    Image(systemName: symbol)
                        .font(.system(size: 21, weight: .light))
                        .foregroundStyle(Color.primary)
                }
            }
            .frame(width: 28)
            .padding(.leading, 3.3)
            VStack(alignment: .leading, spacing: 2.9) {
                Text(name)
                    .font(.system(size: 17))
                    .foregroundStyle(Color.primary)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                if let schule, !schule.isEmpty {
                    Text(schule)
                        .font(.system(size: 15))
                        .foregroundStyle(Color(hell: 0x77787A, dunkel: 0x98989F))
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.leading, 10.7)
            Spacer(minLength: 16)
        }
        .padding(.vertical, 9.45)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
    }

    private func zuklappen() {
        withAnimation(.easeIn(duration: 0.16)) {
            sichtbar = false
        } completion: {
            schliessen()
        }
    }

    private func waehlen(_ konto: Konto) {
        let z = zustand
        let neu = konto.id != aktivId || z.phase != .angemeldet
        zuklappen()
        if neu { Task { await z.wechseln(zu: konto) } }
    }
}

private struct KontoPanelHoehe: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

/// Runder Glasknopf oben links (Konto wechseln) auf allen Hauptseiten.
struct KontoKnopf: View {
    var body: some View {
        KontoMenue {
            Glasknopf { KontoSymbol() }
        }
    }
}

/// Zeile einer weißen Gruppe: Symbol links, Titel (und Untertitel), Zahl, Pfeil.
struct Listenzeile: View {
    let symbol: String
    let titel: String
    var untertitel: String? = nil
    var zahl: Int? = nil
    var pfeil: Bool = true
    var titelFarbe: Color = Farben.tinte
    /// Startseite (Vorlage nachgemessen): Titel 17 pt, Symbol 22 pt, Text 55 pt vom Rand, Pfeil 17 pt
    var gross: Bool = false

    var body: some View {
        HStack(spacing: gross ? 11 : 13) {
            Image(systemName: symbol)
                .font(.system(size: gross ? 22 : 19, weight: .light))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 28)
            VStack(alignment: .leading, spacing: 2) {
                Text(titel)
                    .font(.system(size: gross ? 17 : 16))
                    .foregroundStyle(titelFarbe)
                    .lineLimit(1)
                if let untertitel, !untertitel.isEmpty {
                    Text(untertitel)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
            }
            Spacer(minLength: 4)
            if let zahl {
                ZahlPlakette(zahl: zahl)
                    .scaleEffect(0.85)
            }
            if pfeil {
                Image(systemName: "chevron.right")
                    .font(.system(size: gross ? 17 : 15, weight: .regular))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 6)
        .frame(minHeight: gross ? 51 : 50)
        .contentShape(Rectangle())
    }
}

/// Trennlinie zwischen Listenzeilen (eingerückt bis hinter das Symbol).
struct Zeilentrenner: View {
    var body: some View {
        Rectangle()
            .fill(Farben.trennlinie)
            .frame(height: 0.5)
            .padding(.leading, 57)
            .padding(.trailing, 16)
    }
}

/// Weiße Gruppe mit abgerundeten Ecken (Startseite, Profil).
struct Zeilengruppe<Inhalt: View>: View {
    @ViewBuilder let inhalt: () -> Inhalt

    var body: some View {
        VStack(spacing: 0) {
            inhalt()
        }
        .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .alsKarte(radius: 20)
    }
}

/// Menüpunkte der Details: ohne jede Tipp-Animation (keine Hervorhebung, keine Deckkraftänderung).
struct OhneTippStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .contentShape(Rectangle())
    }
}

/// Zeile einer weißen Gruppe: beim Drücken leicht grau hinterlegt.
struct ZeilenKnopfStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Farben.trennlinie : Color.clear)
    }
}

/// Zweizeiliger Titel für die Navigationsleiste (z. B. „Mi. | 04.11.26“ und Uhrzeit).
struct Leistentitel: View {
    let titel: String
    var untertitel: String? = nil

    var body: some View {
        VStack(spacing: 1) {
            Text(titel)
                .font(.system(size: 14, weight: .medium))
                .foregroundStyle(Farben.tinte2)
            if let untertitel, !untertitel.isEmpty {
                Text(untertitel)
                    .font(.system(size: 11))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .lineLimit(1)
        .accessibilityElement(children: .combine)
    }
}

extension View {
    /// Inhalt mittig mit höchstens dieser Breite (iPad: nicht über die ganze Breite).
    func zentriert(maxBreite: CGFloat = 700) -> some View {
        frame(maxWidth: maxBreite)
            .frame(maxWidth: .infinity)
    }
}

// MARK: - Segment-Schalter

/// Segment-Schalter wie im Original (Schüler*innen: Alle | Abwesend | Anwesend): grauer, voll abgerundeter Hintergrund,
/// der aktive Teil als helle Kapsel mit leichtem Schatten (gleitet beim Wechsel), Schrift 13 pt (aktiv Medium).
struct Segmentschalter<Wert: Hashable>: View {
    struct Eintrag {
        let wert: Wert
        let titel: String
    }

    @Binding var auswahl: Wert
    let eintraege: [Eintrag]
    @Namespace private var raum

    /// Einträge als (Wert, Titel).
    init(auswahl: Binding<Wert>, _ eintraege: [(Wert, String)]) {
        _auswahl = auswahl
        self.eintraege = eintraege.map { Eintrag(wert: $0.0, titel: $0.1) }
    }

    var body: some View {
        HStack(spacing: 0) {
            ForEach(Array(eintraege.enumerated()), id: \.offset) { _, eintrag in
                let aktiv = eintrag.wert == auswahl
                Button {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.86)) {
                        auswahl = eintrag.wert
                    }
                } label: {
                    Text(eintrag.titel)
                        .font(.system(size: 13, weight: aktiv ? .medium : .regular))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                        .padding(.horizontal, 6)
                        .frame(maxWidth: .infinity)
                        .frame(height: 28)
                        .background {
                            if aktiv {
                                Capsule()
                                    .fill(Farben.segmentAktiv)
                                    .shadow(color: Farben.segmentSchatten, radius: 2, y: 1)
                                    .matchedGeometryEffect(id: "aktivesSegment", in: raum)
                            }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(aktiv ? .isSelected : [])
            }
        }
        .padding(2)
        .background(Farben.segmentGrund, in: Capsule())
    }
}

// MARK: - Ladebalken

/// Kräftiger Fortschrittsbalken (4 pt, unbestimmt) direkt unter der Kopfzeile: beim Laden von Plan, Details und
/// Listen läuft ein orangefarbenes Segment von links nach rechts über eine blasse Spur. Nimmt keine Antippen an.
struct Ladebalken: View {
    let aktiv: Bool
    /// Kräftig und gut sichtbar (4 pt)
    var hoehe: CGFloat = 4

    var body: some View {
        ZStack {
            if aktiv {
                LadebalkenLauf(hoehe: hoehe)
                    .transition(.opacity)
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: hoehe)
        .animation(.easeInOut(duration: 0.2), value: aktiv)
        .allowsHitTesting(false)
        .accessibilityElement()
        .accessibilityLabel(aktiv ? "Wird geladen" : "")
        .accessibilityHidden(!aktiv)
    }
}

/// Laufendes Segment des Ladebalkens (Zeitachse statt Zustand – läuft ohne Neustart weiter, solange sichtbar).
private struct LadebalkenLauf: View {
    let hoehe: CGFloat
    private let dauer: Double = 1.35

    var body: some View {
        GeometryReader { geo in
            TimelineView(.animation) { kontext in
                let w = geo.size.width
                let t = kontext.date.timeIntervalSinceReferenceDate
                let p = t.truncatingRemainder(dividingBy: dauer) / dauer
                // Weicher Lauf (ease-in-out), das Segment wächst zur Mitte hin etwas
                let weich = p < 0.5 ? 2 * p * p : 1 - pow(-2 * p + 2, 2) / 2
                let segment = w * CGFloat(0.22 + 0.18 * sin(Double.pi * p))
                let x = -segment + (w + segment) * CGFloat(weich)
                ZStack(alignment: .leading) {
                    Rectangle()
                        .fill(Farben.ladebalken.opacity(0.22))
                    Capsule()
                        .fill(Farben.ladebalken)
                        .frame(width: max(segment, 1), height: hoehe)
                        .offset(x: x)
                }
                .frame(width: w, height: hoehe, alignment: .leading)
                .clipped()
            }
        }
        .frame(height: hoehe)
    }
}

/// Hinweisfläche nach Vorlage (Startseite „Genieße die Zeit!“, nachgemessen): flache, abgerundete Fläche (dunkel
/// #313d44, hell zartgrau), oben mittig das Kalender-Symbol mit Stern (24 pt), darunter der Titel (14 pt Medium) und
/// ein grauer, höchstens zweizeiliger Text (12 pt Regular), alles zentriert. Dient auch für Ferien, gesperrte Tage und
/// gesperrte Wochen im Stundenplan.
struct Hinweisfeld: View {
    let titel: String
    let text: String
    /// Mindesthöhe der Fläche (Startseite: wie die Tageskarte)
    var minHoehe: CGFloat = 0
    /// Farbe der Fläche (im Stundenplan hell weiß auf dem grauen Raster)
    var flaeche: Color = Hinweisfeld.standardFlaeche

    static let standardFlaeche = Color(hell: 0xEEF1F4, dunkel: 0x313D44)
    static let symbolFarbe = Color(hell: 0x5D727F, dunkel: 0xD9E3E9)
    static let titelFarbe = Color(hell: 0x253746, dunkel: 0xE1E3E5)
    static let textFarbe = Color(hell: 0x5D6B76, dunkel: 0xBDC5CC)

    var body: some View {
        VStack(spacing: 0) {
            KalenderSymbol(inhalt: .stern, groesse: 24, strich: 1.5)
                .foregroundStyle(Self.symbolFarbe)
                .frame(width: 24, height: 24)
                .accessibilityHidden(true)
            Text(titel)
                .font(.system(size: 14, weight: .medium))
                .foregroundStyle(Self.titelFarbe)
                .multilineTextAlignment(.center)
                .padding(.top, 9)
            if !text.isEmpty {
                Text(text)
                    .font(.system(size: 12))
                    .foregroundStyle(Self.textFarbe)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
                    .minimumScaleFactor(0.85)
                    .padding(.top, 8)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .padding(.horizontal, 14)
        .padding(.top, 20)
        .padding(.bottom, 21)
        .frame(maxWidth: .infinity, minHeight: minHoehe)
        .background(flaeche, in: RoundedRectangle(cornerRadius: 4, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Listen mit Datumsgruppen („Meine Termine“, „Meine Abwesenheiten“)

/// Kopf der Listen „Meine Termine“ bzw. „Meine Abwesenheiten“ wie in der Vorlage: Titel mittig (17 pt halbfett), darunter
/// der Zeitraum („bis 18.07.2027“) etwas kleiner.
struct Listentitel: View {
    let titel: String
    var untertitel: String? = nil

    var body: some View {
        // Vorlage 33da91d4 (3 px/pt): Titel 15 pt Semibold (Versalhöhe 32 px), darunter 12 pt
        VStack(spacing: 1) {
            Text(titel)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(Farben.tinte2)
            if let untertitel, !untertitel.isEmpty {
                Text(untertitel)
                    .font(.system(size: 12))
                    .foregroundStyle(Farben.tinte2)
            }
        }
        .lineLimit(1)
        .accessibilityElement(children: .combine)
    }
}

/// Datumsgruppe über den Karten („Mo., 07.12.2026“).
struct Datumsgruppenkopf: View {
    let text: String

    var body: some View {
        // Vorlage 33da91d4: 13 pt (Versalhöhe 27 px); Versalhöhe 19 pt unter der Karte davor, nächste Karte 19,3 pt
        // unter der Grundlinie (bei 12 pt Abstand in der Liste)
        Text(text)
            .font(.system(size: 13))
            .foregroundStyle(Farben.tinte2)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.leading, 1)
            .padding(.top, 3.8)
            .padding(.bottom, 4.2)
            .accessibilityAddTraits(.isHeader)
    }

    /// „Mo., 07.12.2026“ aus einem ISO-Datum.
    static func text(_ iso: String) -> String {
        Datumsformat.anzeige(iso, "EE, dd.MM.yyyy")
    }
}

/// Zeile einer Listenkarte (unter der Überschrift).
struct Listenkartenzeile {
    var text: String
    var farbe: Color? = nil
    var symbol: String? = nil
}

/// Karte der Listen „Meine Termine“ und „Meine Abwesenheiten“ wie in der Vorlage: dunkle, abgerundete Fläche, links mittig
/// ein Symbol (Mitte ≈ 28 pt vom Rand), ab ≈ 57 pt die Überschrift (20 pt) und darunter die Zeilen (17 pt).
struct Listenkarte: View {
    let symbol: String
    var symbolFarbe: Color = Farben.tinte2
    let titel: String
    var zeilen: [Listenkartenzeile] = []

    var body: some View {
        // Vorlage 33da91d4 (3 px/pt): Überschrift 17 pt (Versalhöhe 36 px), Zeilen 15 pt (32 px) im Grundlinienabstand
        // 22,67 pt, Versalhöhe der Überschrift 20,33 pt unter der Kartenoberkante, letzte Grundlinie 22 pt über der
        // Unterkante; Text ab 74,3 pt, Symbol mittig bei 44,5 pt; Ecken 8 pt
        HStack(alignment: .center, spacing: 0) {
            Image(systemName: symbol)
                .font(.system(size: 25, weight: .light))
                .foregroundStyle(symbolFarbe)
                .frame(width: 56)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4.75) {
                Text(titel)
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.tinte)
                    .padding(.bottom, -1.9)
                ForEach(Array(zeilen.enumerated()), id: \.offset) { eintrag in
                    let z = eintrag.element
                    HStack(alignment: .firstTextBaseline, spacing: 5) {
                        if let s = z.symbol {
                            Image(systemName: s)
                                .font(.system(size: 13))
                                .foregroundStyle(z.farbe ?? Farben.gedaempft)
                        }
                        Text(z.text)
                            .font(.system(size: 15))
                            .foregroundStyle(z.symbol == nil ? (z.farbe ?? Farben.tinte) : Farben.tinte)
                            .lineLimit(4)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.leading, 1)
        }
        .padding(.trailing, 14)
        .padding(.top, 16.2)
        .padding(.bottom, 18.4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

/// Zeile einer Abwesenheitskarte: Symbol (15 pt) und Text (15 pt).
struct Abwesenheitskartenzeile {
    var symbol: String
    var text: String
    var symbolFarbe: Color = Farben.abwSymbol
}

/// Karte von „Meine Abwesenheiten“ wie in der Vorlage 82e54208 (Dunkelmodus, 3 px/pt): dunkelgraue Fläche #1C1C1E mit 7 pt Ecken,
/// innen 16 pt links/rechts; oben das Datum (20 pt, „15. Okt. - 16. Okt.“), darunter je Zeile Symbol (15 pt, #838E9A bzw. Status-
/// farbe) und Text (15 pt, #DCDEE4) im Grundlinienabstand 21 pt: Uhrzeit, Grund bzw. Verspätung, Status. Kein Symbol links neben der Karte.
struct Abwesenheitskarte: View {
    let titel: String
    let zeilen: [Abwesenheitskartenzeile]

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(titel)
                .font(.system(size: 20))
                .foregroundStyle(Farben.tinte)
                .lineLimit(1)
                .padding(.bottom, 1)
            ForEach(Array(zeilen.enumerated()), id: \.offset) { e in
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    Image(systemName: e.element.symbol)
                        .font(.system(size: 15, weight: .light))
                        .foregroundStyle(e.element.symbolFarbe)
                        .frame(width: 15)
                        .accessibilityHidden(true)
                    Text(e.element.text)
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.abwText)
                        .lineLimit(3)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 16)
        .padding(.top, 15)
        .padding(.bottom, 15)
        .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    /// „15. Okt. - 16. Okt.“ bzw. „8. Okt.“
    static func titel(von: String, bis: String) -> String {
        let v = Datumsformat.anzeige(String(von.prefix(10)), "d. MMM")
        guard !bis.isEmpty, bis.prefix(10) != von.prefix(10) else { return v }
        return "\(v) - \(Datumsformat.anzeige(String(bis.prefix(10)), "d. MMM"))"
    }

    /// „7:45 - 21:00“ an einem Tag, „Do., 07:45 - Fr., 21:00“ über mehrere Tage.
    static func uhrzeit(vonDatum: String, vonZeit: String, bisDatum: String, bisZeit: String) -> String {
        if bisDatum.isEmpty || bisDatum.prefix(10) == vonDatum.prefix(10) {
            return "\(Detailmass.uhrzeit(vonZeit)) - \(Detailmass.uhrzeit(bisZeit))"
        }
        return "\(Datumsformat.anzeige(String(vonDatum.prefix(10)), "EE")), \(vonZeit) - \(Datumsformat.anzeige(String(bisDatum.prefix(10)), "EE")), \(bisZeit)"
    }

    /// Status-Zeile: entschuldigt (grüner Haken-Kreis), nicht entschuldigt (roter Kreuz-Kreis), sonst offen (oranges Fragezeichen).
    static func status(_ status: String) -> Abwesenheitskartenzeile {
        switch status {
        case "entschuldigt": return Abwesenheitskartenzeile(symbol: "checkmark.circle", text: "entschuldigt", symbolFarbe: Farben.abwGruen)
        case "unentschuldigt": return Abwesenheitskartenzeile(symbol: "xmark.circle", text: "nicht entschuldigt", symbolFarbe: Farben.entfallRand)
        default: return Abwesenheitskartenzeile(symbol: "questionmark.circle", text: "offen", symbolFarbe: Farben.orange)
        }
    }
}

/// Trichter wie im Kopf der Vorlage (Filter), als Linie gezeichnet.
struct TrichterSymbol: View {
    var body: some View {
        Path { p in
            p.move(to: CGPoint(x: 1, y: 2))
            p.addLine(to: CGPoint(x: 19, y: 2))
            p.addLine(to: CGPoint(x: 12, y: 10.5))
            p.addLine(to: CGPoint(x: 12, y: 17))
            p.addLine(to: CGPoint(x: 8, y: 19.5))
            p.addLine(to: CGPoint(x: 8, y: 10.5))
            p.closeSubpath()
        }
        .stroke(style: StrokeStyle(lineWidth: 1.3, lineCap: .round, lineJoin: .round))
        .frame(width: 20, height: 21)
        .accessibilityHidden(true)
    }
}

/// Einträge eines Tages in einer Liste mit Datumsgruppen.
struct Datumsgruppe<Eintrag>: Identifiable {
    let datum: String
    var eintraege: [Eintrag]
    var id: String { datum }

    /// Nach Datum (ISO, die ersten zehn Zeichen) gruppieren; Reihenfolge der Einträge bleibt.
    static func bilden(_ liste: [Eintrag], datum: (Eintrag) -> String) -> [Datumsgruppe<Eintrag>] {
        var ergebnis: [Datumsgruppe<Eintrag>] = []
        for e in liste {
            let d = String(datum(e).prefix(10))
            if let i = ergebnis.firstIndex(where: { $0.datum == d }) {
                ergebnis[i].eintraege.append(e)
            } else {
                ergebnis.append(Datumsgruppe(datum: d, eintraege: [e]))
            }
        }
        return ergebnis
    }
}

extension View {
    /// Zwei Knöpfe rechts in der Leiste: ab iOS 26 als getrennte runde Glasknöpfe (wie in den Vorlagen), davor zusammen.
    @ViewBuilder
    func zweiKnoepfe<A: View, B: View>(links: A, rechts: B) -> some View {
        if #available(iOS 26.0, *) {
            self.toolbar {
                ToolbarItem(placement: .topBarTrailing) { links }
                ToolbarSpacer(.fixed, placement: .topBarTrailing)
                ToolbarItem(placement: .topBarTrailing) { rechts }
            }
        } else {
            self.toolbar {
                ToolbarItemGroup(placement: .topBarTrailing) {
                    links
                    rechts
                }
            }
        }
    }
}

/// Trichter (Filter) wie in der Vorlage 33da91d4 (3 px/pt): 24-pt-Feld, Umriss 1,67 pt mit runden Ecken oben, schräge
/// Seiten bis zum Hals bei 12,33 pt, links der Hals bis 22 pt, rechts kürzer und schräg zurück zur linken Seite.
struct Trichter: View {
    var gefuellt = false

    var body: some View {
        let form = Path { p in
            p.move(to: CGPoint(x: 9.5, y: 22))
            p.addLine(to: CGPoint(x: 9.5, y: 12.33))
            p.addLine(to: CGPoint(x: 2.5, y: 4.4))
            p.addQuadCurve(to: CGPoint(x: 4.6, y: 1.85), control: CGPoint(x: 1.2, y: 1.85))
            p.addLine(to: CGPoint(x: 19.4, y: 1.85))
            p.addQuadCurve(to: CGPoint(x: 21.5, y: 4.4), control: CGPoint(x: 22.8, y: 1.85))
            p.addLine(to: CGPoint(x: 14.5, y: 12.33))
            p.addLine(to: CGPoint(x: 14.5, y: 16.9))
            p.addLine(to: CGPoint(x: 9.5, y: 20.8))
        }
        ZStack {
            if gefuellt { form.fill() }
            form.stroke(style: StrokeStyle(lineWidth: 1.67, lineCap: .round, lineJoin: .round))
        }
        .frame(width: 24, height: 24)
    }
}
