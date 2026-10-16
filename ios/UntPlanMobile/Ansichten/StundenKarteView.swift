import SwiftUI

/// Karte einer Stunde im Wochenraster: Farbbalken in Fachfarbe, Fach, darunter Lehrkraft/Klasse und Raum.
struct StundenKarteView: View {
    let karte: Karte
    let typ: String

    var body: some View {
        let stil = Kartenstil(karte)
        let zeilen = kartenzeilen(karte, typ: typ)
        HStack(spacing: 0) {
            Rectangle()
                .fill(Farben.fach(karte.farbe))
                .frame(width: 3)
                .padding(.vertical, 3)
                .padding(.leading, 2)
            VStack(alignment: .leading, spacing: 1) {
                fachText(stil)
                    .font(.system(size: 12, weight: .bold))
                ForEach(Array(zeilen.enumerated()), id: \.offset) { eintrag in
                    if !eintrag.element.isEmpty {
                        zeile(eintrag.element, stil: stil)
                            .font(.system(size: 10))
                    }
                }
                Spacer(minLength: 0)
                symbole
            }
            .lineLimit(1)
            .minimumScaleFactor(0.7)
            .foregroundStyle(Farben.tinte)
            .padding(.leading, 4)
            .padding(.trailing, 2)
            .padding(.vertical, 3)
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(stil.hintergrund)
        .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
        .overlay {
            if let rand = stil.rand {
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .strokeBorder(rand, lineWidth: 1)
            }
        }
        .shadow(color: Color.black.opacity(0.15), radius: 1.5, y: 1)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(barrierefreiText(stil))
    }

    private func fachText(_ stil: Kartenstil) -> Text {
        let fach = karte.fach.isEmpty ? karte.art : karte.fach
        var t = Text("")
        if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach {
            t = t + Text(alt).strikethrough(true, color: Farben.entfallRand) + Text(" ")
            t = t + Text(fach).foregroundColor(Farben.gruen)
        } else {
            t = t + Text(fach).strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
        }
        return t
    }

    private func zeile(_ werte: [Wert], stil: Kartenstil) -> Text {
        if stil.durchgestrichen {
            return Text(werteKurz(werte)).strikethrough(true, color: Farben.entfallRand)
        }
        return werteText(werte)
    }

    @ViewBuilder
    private var symbole: some View {
        if karte.hausaufgabeFaellig || !karte.notizSchueler.isEmpty || !karte.notizLehrer.isEmpty {
            HStack(spacing: 3) {
                Spacer(minLength: 0)
                if karte.hausaufgabeFaellig {
                    Image(systemName: "book.closed.fill")
                }
                if !karte.notizSchueler.isEmpty || !karte.notizLehrer.isEmpty {
                    Image(systemName: "info.circle.fill")
                }
            }
            .font(.system(size: 9))
            .foregroundStyle(Farben.gedaempft)
        }
    }

    private func barrierefreiText(_ stil: Kartenstil) -> String {
        var teile = [karte.titel]
        teile.append(contentsOf: kartenzeilen(karte, typ: typ).map(werteKurz).filter { !$0.isEmpty })
        if let p = stil.plakette { teile.append(p) }
        return teile.joined(separator: ", ")
    }
}
