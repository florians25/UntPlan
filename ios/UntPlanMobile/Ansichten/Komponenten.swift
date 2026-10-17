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
                .frame(width: 34, height: 34)
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

/// Profilwechsel: öffnet die Kontenauswahl (mehrere Konten gleichzeitig angemeldet – wechseln ohne Abmelden,
/// Konto hinzufügen bzw. entfernen).
struct KontoMenue<Beschriftung: View>: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var zeigen = false
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @ViewBuilder let beschriftung: () -> Beschriftung

    var body: some View {
        Button {
            zeigen = true
        } label: {
            beschriftung()
        }
        .accessibilityLabel("Konto wechseln")
        .accessibilityHint("Öffnet die Kontenauswahl")
        // Am iPhone dasselbe randlose 90-%-Blatt wie die Stundendetails
        .blattPraesentation(isPresented: $zeigen, breit: groessenklasse == .regular) {
            KontenauswahlView()
                .environmentObject(zustand)
        }
    }
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

    var body: some View {
        HStack(spacing: 13) {
            Image(systemName: symbol)
                .font(.system(size: 19, weight: .light))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 28)
            VStack(alignment: .leading, spacing: 2) {
                Text(titel)
                    .font(.system(size: 16))
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
                    .font(.system(size: 15, weight: .regular))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 6)
        .frame(minHeight: 50)
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
