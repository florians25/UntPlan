import SwiftUI

/// Karte einer Stunde im Wochenraster (Maße aus app.css, .card):
/// Balken in Fachfarbe links, darunter drei Zeilen – Lehrkraft bzw. Klasse (halbfett), Fach, Raum.
/// Geänderte Werte stehen grün hinterlegt, bei Entfall ist alles durchgestrichen.
struct StundenKarteView: View {
    let karte: Karte
    let typ: String
    var vergangen: Bool = false

    // .card__line: 12,4 px (am Muster eher 13 pt), Zeilenabstand 14,3 px
    private let schrift: CGFloat = 13
    private let zeilenHoehe: CGFloat = 14.3

    var body: some View {
        let stil = Kartenstil(karte, vergangen: vergangen)
        ZStack(alignment: .topLeading) {
            // Balken: links 4, oben 4,7, unten 4, Breite 3, Radius 2 – grau wie in der Vorlage
            RoundedRectangle(cornerRadius: 2, style: .continuous)
                .fill(Farben.karteBalken)
                .frame(width: 3)
                .padding(.top, 4.7)
                .padding(.bottom, 4)
                .padding(.leading, 4)
                .frame(maxHeight: .infinity, alignment: .topLeading)

            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(zeilen.enumerated()), id: \.offset) { eintrag in
                    zeile(eintrag.element, stil: stil)
                        .font(.system(size: schrift, weight: eintrag.offset == 0 ? .semibold : .regular))
                        .frame(height: zeilenHoehe, alignment: .leading)
                }
            }
            .foregroundStyle(Farben.karteText)
            .padding(.leading, 12)
            .padding(.top, 4.5)
            .padding(.trailing, 4)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(stil.hintergrund)
        .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
        .overlay {
            if let rand = stil.rand {
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .strokeBorder(rand, lineWidth: stil.randBreite)
            }
        }
        .overlay(alignment: .bottomTrailing) {
            zeichen
                .padding(2)
        }
        .shadow(color: stil.schatten ? Farben.kartenSchatten : .clear, radius: 1, y: 1)
        .shadow(color: stil.schatten ? Farben.kartenSchattenWeich : .clear, radius: 3, y: 2)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(barrierefreiText(stil))
        .accessibilityAddTraits(.isButton)
    }

    // MARK: - Zeilen

    private enum Kartenzeile {
        case werte([Wert])
        case fach
    }

    /// Lehrkraft (bzw. Klasse) – Fach – Raum (bzw. Lehrkraft); leere Zeilen entfallen.
    private var zeilen: [Kartenzeile] {
        let paar = kartenzeilen(karte, typ: typ)
        var liste: [Kartenzeile] = []
        if let erste = paar.first, !erste.isEmpty { liste.append(.werte(erste)) }
        if !karte.fach.isEmpty || !karte.art.isEmpty || !(karte.fachAlt ?? "").isEmpty { liste.append(.fach) }
        if paar.count > 1, !paar[1].isEmpty { liste.append(.werte(paar[1])) }
        return liste
    }

    @ViewBuilder
    private func zeile(_ z: Kartenzeile, stil: Kartenstil) -> some View {
        switch z {
        case .werte(let werte):
            werteZeile(werte, stil: stil)
        case .fach:
            fachZeile(stil)
        }
    }

    @ViewBuilder
    private func fachZeile(_ stil: Kartenstil) -> some View {
        let fach = karte.fach.isEmpty ? karte.art : karte.fach
        if stil.durchgestrichen {
            Text(fach.isEmpty ? (karte.fachAlt ?? "") : fach)
                .strikethrough(true)
                .lineLimit(1)
        } else if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach, !fach.isEmpty {
            Plakettenwert(text: fach)
        } else if fach.isEmpty, let alt = karte.fachAlt {
            Text(alt)
                .strikethrough(true)
                .lineLimit(1)
        } else {
            Text(fach)
                .lineLimit(1)
        }
    }

    @ViewBuilder
    private func werteZeile(_ werte: [Wert], stil: Kartenstil) -> some View {
        if stil.durchgestrichen {
            Text(werte.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }.joined(separator: ", "))
                .strikethrough(true)
                .lineLimit(1)
        } else if !werte.contains(where: { $0.geaendert }) {
            Text(werteKurz(werte))
                .lineLimit(1)
        } else {
            // Mit Plaketten: Kürzel einzeln, die ganze Zeile wird am Rand abgeschnitten
            HStack(spacing: 0) {
                ForEach(Array(werte.enumerated()), id: \.offset) { eintrag in
                    let w = eintrag.element
                    if eintrag.offset > 0 { Text(", ") }
                    if w.k.isEmpty, let alt = w.alt, !alt.isEmpty {
                        Text(alt).strikethrough(true)
                    } else if w.geaendert {
                        Plakettenwert(text: w.k)
                    } else {
                        Text(w.k)
                    }
                }
            }
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .frame(maxWidth: .infinity, alignment: .leading)
            .clipped()
        }
    }

    // MARK: - Info- und Hausaufgabenzeichen unten rechts

    @ViewBuilder
    private var zeichen: some View {
        let info = !karte.notizSchueler.isEmpty || !karte.notizLehrer.isEmpty || !karte.text.isEmpty
        if info || karte.hausaufgabeFaellig {
            HStack(spacing: 2) {
                if karte.hausaufgabeFaellig {
                    Image(systemName: "book.closed.fill")
                        .font(.system(size: 8))
                        .foregroundStyle(Farben.infoScheibe)
                        .frame(width: 14.5, height: 14.5)
                        .background(Farben.infoFeld, in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                }
                if info {
                    Infozeichen()
                }
            }
        }
    }

    private func barrierefreiText(_ stil: Kartenstil) -> String {
        var teile = [karte.titel]
        teile.append(contentsOf: kartenzeilen(karte, typ: typ).map(werteKurz).filter { !$0.isEmpty })
        if !karte.beginn.isEmpty { teile.append("\(karte.beginn) bis \(karte.ende)") }
        if let p = stil.plakette { teile.append(p) }
        return teile.joined(separator: ", ")
    }
}

/// Grün hinterlegter Wert (.badge): Polster 0,33/2,27, Radius 2, Schrift mittel.
struct Plakettenwert: View {
    let text: String

    var body: some View {
        Text(text)
            .fontWeight(.medium)
            .foregroundStyle(Farben.gruenSchrift)
            .padding(.horizontal, 2.27)
            .padding(.top, 0.33)
            .background(Farben.gruen, in: RoundedRectangle(cornerRadius: 2, style: .continuous))
            .lineLimit(1)
            .fixedSize()
    }
}

/// Info-Punkt (.card__info): helles Feld 14,5 pt, graue Scheibe mit „i“.
struct Infozeichen: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(Farben.infoFeld)
            Circle()
                .fill(Farben.infoScheibe)
                .padding(2.3)
            Text("i")
                .font(.system(size: 7.5, weight: .bold))
                .foregroundStyle(Farben.infoZeichen)
        }
        .frame(width: 14.5, height: 14.5)
    }
}
