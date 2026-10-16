import SwiftUI

/// Eintrag der Infokarten „Infos zum Tag“.
struct Tagesinfo: Identifiable {
    enum Art {
        case nachricht
        case veranstaltung
        case hinweis
    }

    let id = UUID()
    let art: Art
    let titel: String
    let text: String
}

/// Ziele der Liste auf der Startseite.
enum Startziel: Hashable {
    case termine
    case hausaufgaben
    case aufsichten
    case aenderungen
}

/// Startseite nach Vorlage: großer Schulname, wischbare Infokarten, Tageskarte mit den nächsten Stunden,
/// darunter eine weiße Liste (Termine, Hausaufgaben, Aufsichten, Änderungen).
struct StartView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var heute: Heute? = nil
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil
    @State private var alleStunden = false

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    schulname
                    if let fehler {
                        Fehlerhinweis(text: fehler)
                            .padding(.bottom, 16)
                    }
                    if let heute {
                        if heute.paket {
                            infokarten(heute)
                            tageskarte(heute)
                                .padding(.bottom, 24)
                            startliste(heute)
                        } else {
                            Fehlerhinweis(text: "Es wurden noch keine Daten aus UntPlan übertragen.")
                        }
                    } else if laedt {
                        ProgressView()
                            .frame(maxWidth: .infinity)
                            .padding(.top, 40)
                    }
                }
                .padding(.horizontal, 18)
                .padding(.bottom, 28)
            }
            .scrollIndicators(.hidden)
            .background(Farben.grund.ignoresSafeArea())
            .refreshable { await laden() }
            .task { await laden() }
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: Startziel.self) { ziel in
                StartlisteView(ziel: ziel, heute: heute) { gewaehlteKarte = $0 }
            }
            .sheet(item: $gewaehlteKarte) { karte in
                StundendetailView(karte: karte, typ: heute?.typ ?? "klasse")
                    .presentationDetents([.medium, .large])
            }
        }
    }

    // MARK: - Schulname (.schoolname)

    private var schulname: some View {
        Text(zustand.schulname)
            .font(.system(size: 34, weight: .heavy))
            .tracking(-0.75)
            .foregroundStyle(Farben.tinte2)
            .lineLimit(2)
            .minimumScaleFactor(0.7)
            .padding(.top, 52)
            .padding(.bottom, 20)
            .accessibilityAddTraits(.isHeader)
    }

    // MARK: - Infos zum Tag (.newsstrip)

    private func infos(_ h: Heute) -> [Tagesinfo] {
        var liste: [Tagesinfo] = []
        for n in h.nachrichten {
            // "Betreff: Text" in Titel und Text teilen
            if let bereich = n.range(of: ": "), n.distance(from: n.startIndex, to: bereich.lowerBound) <= 60 {
                liste.append(Tagesinfo(art: .nachricht, titel: String(n[..<bereich.lowerBound]), text: String(n[bereich.upperBound...])))
            } else {
                liste.append(Tagesinfo(art: .nachricht, titel: "Nachricht zum Tag", text: n))
            }
        }
        for e in h.ereignisse {
            var text = e.ganztaegig ? "Ganztägig" : (e.von == e.bis ? "\(e.von). Stunde" : "\(e.von).–\(e.bis). Stunde")
            if !e.klassen.isEmpty { text += " · " + e.klassen.joined(separator: ", ") }
            liste.append(Tagesinfo(art: .veranstaltung, titel: e.titel.isEmpty ? e.art : e.titel, text: text))
        }
        for hinweis in h.hinweise {
            liste.append(Tagesinfo(art: .hinweis, titel: "Hinweis", text: hinweis))
        }
        return liste
    }

    @ViewBuilder
    private func infokarten(_ h: Heute) -> some View {
        let liste = infos(h)
        if !liste.isEmpty {
            ScrollView(.horizontal) {
                HStack(alignment: .top, spacing: 12) {
                    ForEach(liste) { info in
                        Infokarte(info: info)
                            .frame(maxHeight: .infinity, alignment: .top)
                            .containerRelativeFrame(.horizontal) { breite, _ in
                                liste.count == 1 ? breite - 36 : breite * 0.86
                            }
                    }
                }
                .scrollTargetLayout()
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 2)
                .padding(.bottom, 24)
            }
            .scrollIndicators(.hidden)
            .scrollTargetBehavior(.viewAligned)
            .scrollClipDisabled()
            .contentMargins(.horizontal, 18, for: .scrollContent)
            .padding(.horizontal, -18)
        }
    }

    // MARK: - Tageskarte (.daycard)

    /// Stunden des Tages; heute nur die noch kommenden.
    private func naechsteStunden(_ h: Heute) -> [Karte] {
        let sortiert = h.karten.sorted { ($0.von, $0.bis) < ($1.von, $1.bis) }
        guard Datumsformat.istHeute(h.datum), !h.jetzt.isEmpty else { return sortiert }
        return sortiert.filter { $0.ende.isEmpty || $0.ende >= h.jetzt }
    }

    private func tageskarte(_ h: Heute) -> some View {
        let stunden = naechsteStunden(h)
        let istHeute = Datumsformat.istHeute(h.datum)
        let sichtbar = alleStunden ? stunden : Array(stunden.prefix(2))
        let rest = Array(stunden.dropFirst(sichtbar.count))
        return HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 0) {
                Text(Datumsformat.anzeige(h.datum, "EEEE"))
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte2)
                Text(Datumsformat.anzeige(h.datum, "d"))
                    .font(.system(size: 42, weight: .bold))
                    .foregroundStyle(Farben.heute)
                    .padding(.top, -1)
                Text(Datumsformat.anzeige(h.datum, "MMMM"))
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(Farben.heute)
                if !h.woche.isEmpty {
                    Text(h.woche.count == 1 ? "\(h.woche)-Woche" : h.woche)
                        .font(.system(size: 14.5, weight: .semibold))
                        .foregroundStyle(Color(hell: 0x20364F, dunkel: 0xD7E4F7))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(Color(hell: 0xD7E4F7, dunkel: 0x20364F), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        .padding(.top, 10)
                }
            }
            .lineLimit(1)
            .minimumScaleFactor(0.7)
            .frame(width: 92, alignment: .leading)

            VStack(alignment: .leading, spacing: 0) {
                (Text(istHeute ? "Heute" : Datumsformat.anzeige(h.datum, "EEEE"))
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(Farben.tinte2)
                 + Text("  |  ")
                    .font(.system(size: 13))
                    .foregroundColor(Farben.gedaempft.opacity(0.6))
                 + Text(planname(h))
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(Farben.tinte2))
                    .lineLimit(1)
                    .padding(.bottom, 8)

                if stunden.isEmpty {
                    keineStunden(h)
                } else {
                    ForEach(sichtbar) { karte in
                        Button {
                            gewaehlteKarte = karte
                        } label: {
                            NaechsteStunde(karte: karte, typ: h.typ)
                        }
                        .buttonStyle(GedruecktStil())
                        .padding(.bottom, 10)
                    }
                    if !rest.isEmpty || alleStunden && stunden.count > 2 {
                        Button {
                            withAnimation(.easeInOut(duration: 0.2)) { alleStunden.toggle() }
                        } label: {
                            HStack(spacing: 8) {
                                if !rest.isEmpty {
                                    HStack(spacing: 3) {
                                        ForEach(rest.prefix(6)) { karte in
                                            Capsule()
                                                .fill(Farben.fach(karte.farbe))
                                                .frame(width: 4, height: 13)
                                        }
                                    }
                                    Text(rest.count == 1 ? "+1 Stunde" : "+\(rest.count) Stunden")
                                } else {
                                    Text("Weniger anzeigen")
                                }
                            }
                            .font(.system(size: 14.5))
                            .foregroundStyle(Farben.tinte2)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.top, 16)
        .padding(.horizontal, 16)
        .padding(.bottom, 14)
        .alsKarte()
    }

    private func planname(_ h: Heute) -> String {
        "Mein Stundenplan"
    }

    private func keineStunden(_ h: Heute) -> some View {
        VStack(spacing: 3) {
            Image(systemName: "cup.and.saucer")
                .font(.system(size: 28, weight: .light))
                .foregroundStyle(Farben.tinte2)
            Text("Keine Stunden")
                .font(.system(size: 16, weight: .bold))
                .foregroundStyle(Farben.tinte)
                .padding(.top, 5)
            Text(h.karten.isEmpty ? "Heute ist frei." : "Heute keine weiteren Stunden.")
                .font(.system(size: 14.5))
                .foregroundStyle(Farben.tinte2)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 14)
    }

    // MARK: - Liste (.rows)

    private struct Listeneintrag: Identifiable {
        let ziel: Startziel
        let titel: String
        let symbol: String
        let zahl: Int
        var id: Startziel { ziel }
    }

    private func eintraege(_ h: Heute) -> [Listeneintrag] {
        var liste: [Listeneintrag] = [
            Listeneintrag(ziel: .termine, titel: "Meine Termine", symbol: "calendar.badge.clock", zahl: h.termine.count)
        ]
        if !h.hausaufgaben.isEmpty || h.typ == "klasse" {
            liste.append(Listeneintrag(ziel: .hausaufgaben, titel: "Hausaufgaben", symbol: "book.closed", zahl: h.hausaufgaben.count))
        }
        if !h.aufsichten.isEmpty {
            liste.append(Listeneintrag(ziel: .aufsichten, titel: "Pausenaufsichten", symbol: "figure.walk", zahl: h.aufsichten.count))
        }
        if h.aenderungen > 0 {
            liste.append(Listeneintrag(ziel: .aenderungen, titel: "Änderungen heute", symbol: "arrow.triangle.2.circlepath", zahl: h.aenderungen))
        }
        return liste
    }

    private func startliste(_ h: Heute) -> some View {
        let liste = eintraege(h)
        return VStack(spacing: 0) {
            ForEach(Array(liste.enumerated()), id: \.element.id) { eintrag in
                let e = eintrag.element
                NavigationLink(value: e.ziel) {
                    HStack(spacing: 13) {
                        Image(systemName: e.symbol)
                            .font(.system(size: 21, weight: .light))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(width: 30)
                        Text(e.titel)
                            .font(.system(size: 17))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(1)
                        Spacer(minLength: 4)
                        if e.zahl > 0 {
                            ZahlPlakette(zahl: e.zahl)
                        }
                        Image(systemName: "chevron.right")
                            .font(.system(size: 16, weight: .regular))
                            .foregroundStyle(Farben.gedaempft)
                            .padding(.leading, 4)
                    }
                    .padding(.horizontal, 16)
                    .frame(minHeight: 54)
                    .contentShape(Rectangle())
                }
                .buttonStyle(ZeilenStil())
                .overlay(alignment: .bottom) {
                    if eintrag.offset < liste.count - 1 {
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 0.5)
                            .padding(.leading, 59)
                            .padding(.trailing, 16)
                    }
                }
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        .alsKarte()
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            let neu = try await zustand.api.heute()
            heute = neu
            zustand.ungelesen = neu.ungelesen
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

// MARK: - Bausteine der Startseite

/// Infokarte mit farbigem Kopf (.newscard): Nachricht blau mit Megafon, Veranstaltung grün, Hinweis orange.
struct Infokarte: View {
    let info: Tagesinfo

    private var kopfFarbe: Color {
        switch info.art {
        case .nachricht: return Farben.nachrichtKopf
        case .veranstaltung: return Farben.veranstaltungKopf
        case .hinweis: return Farben.hinweisKopf
        }
    }

    private var symbol: String {
        switch info.art {
        case .nachricht: return "megaphone"
        case .veranstaltung: return "person.3"
        case .hinweis: return "info.circle"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Image(systemName: symbol)
                    .font(.system(size: 24, weight: .regular))
                Spacer()
                Text("neu")
                    .font(.system(size: 14, weight: .medium))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 2)
                    .overlay {
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.9), lineWidth: 1.5)
                    }
            }
            .foregroundStyle(.white)
            .padding(.horizontal, 18)
            .frame(height: 58)
            .background(kopfFarbe)

            VStack(alignment: .leading, spacing: 6) {
                Text(info.titel)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(2)
                Text(info.text)
                    .font(.system(size: 16))
                    .lineSpacing(3)
                    .foregroundStyle(Farben.tinte2)
                    .lineLimit(3)
            }
            .padding(.top, 16)
            .padding(.horizontal, 18)
            .padding(.bottom, 18)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .background(Farben.flaeche)
        .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        .weicherSchatten()
        .accessibilityElement(children: .combine)
    }
}

/// Zeile der Tageskarte (.nextrow): Balken in Fachfarbe, Klasse bzw. Lehrkraft, Raum, Fach; rechts Beginn und Ende.
struct NaechsteStunde: View {
    let karte: Karte
    let typ: String

    var body: some View {
        let stil = Kartenstil(karte)
        let zeilen = kartenzeilen(karte, typ: typ).filter { !$0.isEmpty }
        VStack(alignment: .leading, spacing: 1) {
            ForEach(Array(zeilen.enumerated()), id: \.offset) { eintrag in
                zeile(eintrag.element, stil: stil)
                    .font(.system(size: 15, weight: eintrag.offset == 0 ? .semibold : .regular))
            }
            Text(karte.fach.isEmpty ? karte.titel : karte.fach)
                .font(.system(size: 15, weight: zeilen.isEmpty ? .semibold : .regular))
                .strikethrough(stil.durchgestrichen)
        }
        .lineLimit(1)
        .foregroundStyle(Farben.tinte)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.leading, 14)
        .padding(.trailing, 62)
        .overlay(alignment: .leading) {
            Capsule()
                .fill(Farben.fach(karte.farbe))
                .frame(width: 4)
        }
        .overlay(alignment: .trailing) {
            VStack(alignment: .trailing, spacing: 0) {
                Text(karte.beginn.isEmpty ? "\(karte.von)." : karte.beginn)
                Spacer(minLength: 2)
                Text(karte.ende.isEmpty ? "\(karte.bis)." : karte.ende)
            }
            .font(.system(size: 14.5).monospacedDigit())
            .foregroundStyle(Farben.tinte2)
            .frame(minWidth: 52, alignment: .trailing)
        }
        .padding(.top, 3)
        .padding(.bottom, 5)
        .padding(.trailing, 6)
        .padding(.horizontal, karte.istEntfall ? 8 : 0)
        .background {
            if karte.istEntfall {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Farben.entfallHintergrund)
                    .overlay {
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(Farben.entfallRand, lineWidth: 1.5)
                    }
            }
        }
        .contentShape(Rectangle())
    }

    private func zeile(_ werte: [Wert], stil: Kartenstil) -> Text {
        if stil.durchgestrichen {
            return Text(werteKurz(werte)).strikethrough(true)
        }
        return werteText(werte)
    }
}

/// Unterseite der Startliste: Termine, Hausaufgaben, Aufsichten oder Änderungen.
struct StartlisteView: View {
    let ziel: Startziel
    let heute: Heute?
    let antippen: (Karte) -> Void

    private var titel: String {
        switch ziel {
        case .termine: return "Meine Termine"
        case .hausaufgaben: return "Hausaufgaben"
        case .aufsichten: return "Pausenaufsichten"
        case .aenderungen: return "Änderungen heute"
        }
    }

    var body: some View {
        List {
            if let h = heute {
                switch ziel {
                case .termine: termine(h)
                case .hausaufgaben: hausaufgaben(h)
                case .aufsichten: aufsichten(h)
                case .aenderungen: aenderungen(h)
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle(titel)
        .navigationBarTitleDisplayMode(.large)
        .toolbar(.visible, for: .navigationBar)
    }

    @ViewBuilder
    private func termine(_ h: Heute) -> some View {
        if h.termine.isEmpty {
            leer("Keine Termine in den nächsten zwei Wochen.")
        }
        ForEach(h.termine) { t in
            HStack(spacing: 12) {
                VStack(spacing: 0) {
                    Text(Datumsformat.anzeige(t.von, "d"))
                        .font(.system(size: 22, weight: .bold))
                    Text(Datumsformat.anzeige(t.von, "MMM"))
                        .font(.system(size: 12, weight: .semibold))
                }
                .foregroundStyle(Farben.heute)
                .frame(width: 44)
                VStack(alignment: .leading, spacing: 2) {
                    Text(t.titel.isEmpty ? t.art : t.titel)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(Farben.tinte)
                    Text(termintext(t))
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                }
            }
            .padding(.vertical, 4)
            .listRowBackground(Farben.flaeche)
        }
    }

    private func termintext(_ t: Termin) -> String {
        var teile: [String] = []
        if !t.art.isEmpty && !t.titel.isEmpty { teile.append(t.art) }
        if !t.bis.isEmpty && t.bis.prefix(10) != t.von.prefix(10) {
            teile.append("bis \(Datumsformat.anzeige(t.bis, "d.M."))")
        }
        if let v = t.stundeVon {
            let b = t.stundeBis ?? v
            teile.append(v == b ? "\(v). Stunde" : "\(v).–\(b). Stunde")
        }
        if !t.klassen.isEmpty { teile.append(t.klassen.joined(separator: ", ")) }
        return teile.joined(separator: " · ")
    }

    @ViewBuilder
    private func hausaufgaben(_ h: Heute) -> some View {
        if h.hausaufgaben.isEmpty {
            leer("Keine Hausaufgaben eingetragen.")
        }
        ForEach(h.hausaufgaben) { ha in
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(ha.fach)
                        .font(.system(size: 16, weight: .bold))
                        .foregroundStyle(Farben.tinte)
                    Spacer()
                    if let f = ha.faellig, !f.isEmpty {
                        Text("fällig \(Datumsformat.anzeige(f, "EE d.M."))")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundStyle(Farben.heute)
                    }
                }
                Text(ha.hausaufgabe)
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte2)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.vertical, 4)
            .listRowBackground(Farben.flaeche)
        }
    }

    @ViewBuilder
    private func aufsichten(_ h: Heute) -> some View {
        ForEach(h.aufsichten) { a in
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(a.gangName.isEmpty ? a.gang : a.gangName)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(Farben.tinte)
                        .strikethrough(a.status == "vertreten", color: Farben.entfallRand)
                    Text("nach der \(a.nachStunde). Stunde · \(a.beginn) – \(a.ende)")
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                }
                Spacer()
                if a.status == "vertretung" {
                    Plakette(text: "Vertretung", farbe: Farben.gruen)
                } else if a.status == "vertreten" {
                    Plakette(text: "Vertreten", farbe: Farben.entfallRand)
                }
            }
            .padding(.vertical, 4)
            .listRowBackground(Farben.flaeche)
        }
    }

    @ViewBuilder
    private func aenderungen(_ h: Heute) -> some View {
        let geaendert = h.karten
            .filter { $0.istEntfall || $0.istAenderung || $0.istPruefung || $0.zusaetzlich }
            .sorted { ($0.von, $0.bis) < ($1.von, $1.bis) }
        if geaendert.isEmpty {
            leer("Heute keine Änderungen.")
        }
        ForEach(geaendert) { karte in
            Button {
                antippen(karte)
            } label: {
                Stundenzeile(karte: karte, typ: h.typ)
            }
            .buttonStyle(.plain)
            .listRowInsets(EdgeInsets())
            .listRowBackground(Farben.flaeche)
        }
    }

    private func leer(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 15))
            .foregroundStyle(Farben.gedaempft)
            .listRowBackground(Farben.flaeche)
    }
}

/// Listenzeile einer Stunde: Uhrzeit, Farbbalken, Fach mit Lehrkraft/Klasse, Raum und Plakette.
struct Stundenzeile: View {
    let karte: Karte
    let typ: String

    var body: some View {
        let stil = Kartenstil(karte)
        let zeilen = kartenzeilen(karte, typ: typ)
        HStack(spacing: 12) {
            VStack(alignment: .trailing, spacing: 1) {
                Text(karte.beginn.isEmpty ? "\(karte.von)." : karte.beginn)
                    .font(.subheadline.weight(.semibold).monospacedDigit())
                Text(karte.von == karte.bis ? "\(karte.von). Std." : "\(karte.von).–\(karte.bis).")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            .frame(width: 46, alignment: .trailing)
            Capsule()
                .fill(Farben.fach(karte.farbe))
                .frame(width: 4, height: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(karte.titel)
                    .font(.subheadline.weight(.semibold))
                    .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                    .lineLimit(1)
                let unten = zeilen.filter { !$0.isEmpty }
                if !unten.isEmpty {
                    unten.dropFirst().reduce(werteText(unten[0])) { $0 + Text(" · ") + werteText($1) }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            if let p = stil.plakette {
                Plakette(text: p, farbe: stil.plaketteFarbe)
            }
            Image(systemName: "chevron.right")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
        .foregroundStyle(Farben.tinte)
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .contentShape(Rectangle())
    }
}

/// Zeile der weißen Liste: beim Drücken leicht grau hinterlegt.
private struct ZeilenStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Farben.trennlinie : Color.clear)
    }
}

/// Leichtes Eindrücken (.nextrow:active { scale(.985) }).
private struct GedruecktStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.985 : 1)
            .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
    }
}
