import SwiftUI

/// Karte einer Stunde im Wochenraster (Maße aus app.css, .card):
/// grauer Balken links, darunter drei Zeilen – Lehrkraft bzw. Klasse (halbfett), Fach, Raum.
/// Geänderte Werte stehen hinterlegt (Vertretung grün, Raumänderung/Buchung gelb-orange), bei Entfall ist alles durchgestrichen.
/// Mehrere Räume stehen als einzelne Labels nebeneinander. In der Tagesansicht (gross) größer und mit mehr Text.
struct StundenKarteView: View {
    let karte: Karte
    let typ: String
    var vergangen: Bool = false
    var gross: Bool = false

    private var schrift: CGFloat { gross ? 15 : 13 }
    private var zeilenHoehe: CGFloat { gross ? 18.5 : 14.3 }

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
                if gross {
                    zusatzText(stil)
                }
            }
            .foregroundStyle(Farben.karteText)
            .padding(.leading, gross ? 14 : 12)
            .padding(.top, gross ? 6 : 4.5)
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
        .overlay(alignment: .topTrailing) {
            if gross, let p = stil.plakette {
                Plakette(text: p, farbe: stil.plaketteFarbe)
                    .padding(5)
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
        /// Räume: jeder Raum ein eigenes Label
        case raeume([Wert])
        case fach
    }

    /// Lehrkraft (bzw. Klasse) – Fach – Raum (bzw. Lehrkraft); leere Zeilen entfallen.
    private var zeilen: [Kartenzeile] {
        let paar = kartenzeilen(karte, typ: typ)
        var liste: [Kartenzeile] = []
        if let erste = paar.first, !erste.isEmpty { liste.append(.werte(erste)) }
        if !karte.fach.isEmpty || !karte.art.isEmpty || !(karte.fachAlt ?? "").isEmpty { liste.append(.fach) }
        if paar.count > 1, !paar[1].isEmpty {
            // Im Klassen- und Lehrerplan ist die zweite Zeile der Raum
            liste.append(typ == "raum" ? .werte(paar[1]) : .raeume(paar[1]))
        }
        return liste
    }

    @ViewBuilder
    private func zeile(_ z: Kartenzeile, stil: Kartenstil) -> some View {
        switch z {
        case .werte(let werte):
            werteZeile(werte, stil: stil)
        case .raeume(let werte):
            raumZeile(werte, stil: stil)
        case .fach:
            fachZeile(stil)
        }
    }

    @ViewBuilder
    private func fachZeile(_ stil: Kartenstil) -> some View {
        let fach = karte.fach.isEmpty ? karte.art : karte.fach
        let mitName = gross && !karte.fachName.isEmpty && karte.fachName != fach
        if stil.durchgestrichen {
            Text(fach.isEmpty ? (karte.fachAlt ?? "") : fach)
                .strikethrough(true)
                .lineLimit(1)
        } else if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach, !fach.isEmpty, !karte.keineVertretung {
            Plakettenwert(text: fach, farbe: stil.wertFarbe)
        } else if fach.isEmpty, let alt = karte.fachAlt {
            Text(alt)
                .strikethrough(true)
                .lineLimit(1)
        } else if mitName {
            (Text(fach) + Text("  ") + Text(karte.fachName).foregroundColor(Farben.tinte2))
                .lineLimit(1)
        } else {
            Text(fach)
                .lineLimit(1)
        }
    }

    /// Hervorhebung eines geänderten Werts (bei Klausuren/Raumbuchungen nur für Räume, nie grün).
    private func hervorheben(_ w: Wert, raum: Bool) -> Bool {
        guard w.geaendert else { return false }
        return raum || !karte.keineVertretung
    }

    @ViewBuilder
    private func werteZeile(_ werte: [Wert], stil: Kartenstil) -> some View {
        if stil.durchgestrichen {
            Text(werte.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }.joined(separator: ", "))
                .strikethrough(true)
                .lineLimit(1)
        } else if !werte.contains(where: { hervorheben($0, raum: false) }) {
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
                    } else if hervorheben(w, raum: false) {
                        Plakettenwert(text: w.k, farbe: stil.wertFarbe)
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

    /// Räume als einzelne Labels: ein hinzugefügter bzw. neuer Raum steht allein im Änderungs-Label,
    /// unveränderte Räume bleiben eigene Labels (kein Zusammenfassen mit Komma).
    @ViewBuilder
    private func raumZeile(_ werte: [Wert], stil: Kartenstil) -> some View {
        HStack(spacing: 5) {
            ForEach(Array(werte.enumerated()), id: \.offset) { eintrag in
                let w = eintrag.element
                if stil.durchgestrichen {
                    Text(w.k.isEmpty ? (w.alt ?? "") : w.k).strikethrough(true)
                } else if w.k.isEmpty, let alt = w.alt, !alt.isEmpty {
                    Text(alt).strikethrough(true)
                } else if hervorheben(w, raum: true) {
                    Plakettenwert(text: w.k, farbe: stil.wertFarbe)
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

    /// Tagesansicht: Uhrzeit und Hinweistext unter den Zeilen.
    @ViewBuilder
    private func zusatzText(_ stil: Kartenstil) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            if !karte.beginn.isEmpty {
                Text("\(karte.beginn) – \(karte.ende)")
                    .font(.system(size: 12.5).monospacedDigit())
                    .foregroundStyle(Farben.tinte2)
            }
            let hinweis = [karte.text, karte.abwesendText, karte.notizSchueler].first { !$0.isEmpty } ?? ""
            if !hinweis.isEmpty {
                Text(hinweis)
                    .font(.system(size: 12.5))
                    .foregroundStyle(Farben.tinte2)
                    .lineLimit(3)
            }
        }
        .padding(.top, 4)
        .padding(.trailing, 20)
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

/// Hinterlegter Wert (.badge): Polster 0,33/2,27, Radius 2, Schrift mittel; Vertretung grün, Raum gelb-orange.
struct Plakettenwert: View {
    let text: String
    var farbe: Color = Farben.gruen

    var body: some View {
        Text(text)
            .fontWeight(.medium)
            .foregroundStyle(Farben.gruenSchrift)
            .padding(.horizontal, 2.27)
            .padding(.top, 0.33)
            .background(farbe, in: RoundedRectangle(cornerRadius: 2, style: .continuous))
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
