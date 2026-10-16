import SwiftUI

/// Werte (Lehrkräfte, Klassen, Räume) als Text: alter Wert durchgestrichen, geänderter Wert grün.
func werteText(_ werte: [Wert], trenner: String = ", ") -> Text {
    var ergebnis = Text("")
    for (i, w) in werte.enumerated() {
        if i > 0 { ergebnis = ergebnis + Text(trenner) }
        if let alt = w.alt, !alt.isEmpty, alt != w.k {
            ergebnis = ergebnis + Text(alt).strikethrough(true, color: Farben.entfallRand)
            if !w.k.isEmpty { ergebnis = ergebnis + Text(" ") }
        }
        if w.geaendert {
            ergebnis = ergebnis + Text(w.k).foregroundColor(Farben.gruen).bold()
        } else {
            ergebnis = ergebnis + Text(w.k)
        }
    }
    return ergebnis
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
                .font(.title3.weight(.bold))
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

/// Kleine farbige Plakette (Änderung, Entfall, Prüfung …).
struct Plakette: View {
    let text: String
    let farbe: Color

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 10, weight: .bold))
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(farbe, in: Capsule())
            .lineLimit(1)
    }
}

/// Runder Kreis mit Initialen.
struct Initialen: View {
    let name: String
    var groesse: CGFloat = 40

    var body: some View {
        Text(Self.kuerzel(name))
            .font(.system(size: groesse * 0.38, weight: .semibold))
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
                        .font(.system(size: b * 0.36, weight: .semibold))
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                case .stern:
                    Image(systemName: "star")
                        .font(.system(size: b * 0.34, weight: .semibold))
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
                .frame(width: 40, height: 40)
                .glas(Circle())
                .contentShape(Circle())
        }
    }
}

/// Person mit zwei Pfeilen (Konto wechseln), nachgebaut aus SF Symbols.
struct KontoSymbol: View {
    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            Image(systemName: "person")
                .font(.system(size: 17, weight: .regular))
                .offset(x: -4, y: -3)
            Image(systemName: "arrow.up.arrow.down")
                .font(.system(size: 9.5, weight: .semibold))
                .offset(x: 3, y: 2)
        }
        .frame(width: 26, height: 26)
    }
}

/// Menü „Konto wechseln“: aktuelles Konto und Anmeldung mit einem anderen Konto (meldet ab).
struct KontoMenue<Beschriftung: View>: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var bestaetigen = false
    @ViewBuilder let beschriftung: () -> Beschriftung

    var body: some View {
        Menu {
            if let b = zustand.benutzer {
                Section(zustand.schulname) {
                    Button {} label: {
                        Label(b.rollenname.isEmpty ? b.login : "\(b.login) · \(b.rollenname)", systemImage: "checkmark")
                    }
                    .disabled(true)
                }
            }
            Button {
                bestaetigen = true
            } label: {
                Label("Mit anderem Konto anmelden", systemImage: "person.badge.plus")
            }
        } label: {
            beschriftung()
        }
        .accessibilityLabel("Konto wechseln")
        .confirmationDialog("Konto wechseln?", isPresented: $bestaetigen, titleVisibility: .visible) {
            Button("Abmelden und anderes Konto anmelden", role: .destructive) {
                Task { await zustand.abmelden() }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Sie werden abgemeldet; ein gemerktes Passwort wird vom Gerät entfernt.")
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
