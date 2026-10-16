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
            .background(Farben.entfallHintergrund, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
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

/// Weiße Karte mit abgerundeten Ecken (Startseite).
struct Kartenhintergrund: ViewModifier {
    func body(content: Content) -> some View {
        content
            .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .shadow(color: .black.opacity(0.06), radius: 6, y: 2)
    }
}

extension View {
    func alsKarte() -> some View {
        modifier(Kartenhintergrund())
    }
}
