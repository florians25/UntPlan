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
    case abwesenheiten
    /// Schüler*innen und Eltern: erfasste Abwesenheiten (Zeiträume mit Status)
    case familieAbwesenheiten
    case hausaufgaben
    case aufsichten
    case aenderungen
    /// Sprechtag (Lehrkräfte: eigene Termine; Erziehungsberechtigte: buchen und absagen)
    case sprechtag(Int)
    /// Entschuldigungen (Klassenleitung: Abwesenheiten der eigenen Klassen; Fachlehrkraft: eigene Fehlstunden)
    case entschuldigungen
}

/// Startseite nach Vorlage: großer Schulname, Infokarten, Tageskarte (Datum links, Tagesinhalt rechts),
/// darunter eine weiße Liste (Termine, Abwesenheiten, Hausaufgaben, Sprechtag, Änderungen). Die Pausenaufsichten stehen
/// im Stundenplan (nicht mehr in der Liste).
/// Auf dem iPad mittig mit höchstens 700 pt Breite.
struct StartView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var heute: Heute? = nil
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil
    @State private var alleStunden = false
    /// Ferien am angezeigten Tag bzw. ab dem nächsten Schultag (aus der Tagesantwort oder dem Wochenplan)
    @State private var ferien: Ferienzeit? = nil
    /// Sprechtage für die Startseite (Lehrkräfte bzw. Erziehungsberechtigte)
    @State private var sprechtage: [SprechtagKarte] = []
    /// Geöffneter Sprechtag (als Blatt wie die Stundendetails)
    @State private var sprechtagWahl: SprechtagWahl? = nil
    @Environment(\.horizontalSizeClass) private var groessenklasse

    /// Sprechtag, der als Blatt gezeigt wird.
    struct SprechtagWahl: Identifiable {
        let id: Int
        let titel: String
    }

    /// iPhone: Seitenrand 16 pt (iPad 18 pt, mittig höchstens 736 pt breit).
    private var rand: CGFloat { groessenklasse == .compact ? 16 : 18 }

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
                                .padding(.bottom, 20)
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
                .padding(.horizontal, rand)
                .padding(.bottom, 28)
                .zentriert(maxBreite: 736)
            }
            .scrollIndicators(.hidden)
            .background(Farben.grund.ignoresSafeArea())
            .refreshable { await laden() }
            .task { await laden() }
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.hidden, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { KontoKnopf() }
            }
            .navigationDestination(for: Startziel.self) { ziel in
                switch ziel {
                case .familieAbwesenheiten:
                    MeineAbwesenheitenView()
                        .environmentObject(zustand)
                case .entschuldigungen:
                    EntschuldigungenView()
                        .environmentObject(zustand)
                case .sprechtag(let id):
                    // Rückfall (sonst als Blatt): Kopf mit Zurück statt X über dieselbe Ansicht
                    SprechtagView(id: id, titel: sprechtage.first { $0.id == id }?.titel ?? "Sprechtag",
                                  karte: sprechtage.first { $0.id == id })
                        .environmentObject(zustand)
                default:
                    StartlisteView(ziel: ziel, heute: heute) { gewaehlteKarte = $0 }
                }
            }
            .detailPraesentation(item: $gewaehlteKarte, breit: groessenklasse == .regular) { karte in
                StundendetailView(karte: karte, typ: heute?.typ ?? "klasse", element: heute?.element ?? "")
                    .environmentObject(zustand)
            }
            // Sprechtag: iPhone randloses 90-%-Blatt, iPad festes Detailblatt (eigener Navigationsstapel im Blatt)
            .detailPraesentation(item: $sprechtagWahl, breit: groessenklasse == .regular) { wahl in
                NavigationStack {
                    SprechtagView(id: wahl.id, titel: wahl.titel, karte: sprechtage.first { $0.id == wahl.id })
                }
                .environmentObject(zustand)
            }
            .onChange(of: sprechtagWahl?.id) { _, neu in
                // Nach dem Schließen die Zahl der Termine auf der Startseite auffrischen
                if neu == nil { Task { await laden() } }
            }
        }
    }

    // MARK: - Schulname (.schoolname)

    private var schulname: some View {
        Text(zustand.schulname)
            .font(.system(size: groessenklasse == .compact ? 32 : 30, weight: .medium))
            .tracking(-0.6)
            .foregroundStyle(Farben.titelGrau)
            .lineLimit(2)
            .minimumScaleFactor(0.7)
            .padding(.top, 2)
            .padding(.bottom, 16)
            .accessibilityAddTraits(.isHeader)
    }

    // MARK: - Infos zum Tag (.newsstrip)

    /// Hinweis „abwesend …“ bzw. „Klasse abwesend …“ des Tages.
    private func abwesenheitsHinweis(_ h: Heute) -> String? {
        h.hinweise.first { hinweis in
            let klein = hinweis.lowercased()
            return klein.hasPrefix("abwesend") || klein.hasPrefix("klasse abwesend")
        }
    }

    /// Die Tageskarte zeigt statt der Stunden eine graue Fläche „Abwesend“.
    private func zeigtAbwesenheit(_ h: Heute) -> Bool {
        guard abwesenheitsHinweis(h) != nil else { return false }
        return naechsteStunden(h).allSatisfy { $0.status == "abwesend" || $0.istEntfall }
    }

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
        // Die eigene Abwesenheit steht bereits in der Tageskarte
        let abwesend = zeigtAbwesenheit(h) ? abwesenheitsHinweis(h) : nil
        for hinweis in h.hinweise where hinweis != abwesend {
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
                                liste.count == 1 ? breite - 2 * rand : breite * 0.86
                            }
                    }
                }
                .scrollTargetLayout()
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 2)
                .padding(.bottom, 20)
            }
            .scrollIndicators(.hidden)
            .scrollTargetBehavior(.viewAligned)
            .scrollClipDisabled()
            .contentMargins(.horizontal, rand, for: .scrollContent)
            .padding(.horizontal, -rand)
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
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 0) {
                Text(Datumsformat.anzeige(h.datum, "EEEE"))
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                Text(Datumsformat.anzeige(h.datum, "d"))
                    .font(.system(size: 38, weight: .medium))
                    .foregroundStyle(Farben.heute)
                    .padding(.top, -1)
                Text(Datumsformat.anzeige(h.datum, "MMMM"))
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(Farben.heute)
                if !h.woche.isEmpty {
                    Text(h.woche.count == 1 ? "\(h.woche)-Woche" : h.woche)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(Color(hell: 0x20364F, dunkel: 0xD7E4F7))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(Color(hell: 0xD7E4F7, dunkel: 0x20364F), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        .padding(.top, 8)
                }
            }
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .frame(minWidth: groessenklasse == .compact ? 64 : 84, alignment: .leading)

            if zeigtAbwesenheit(h) {
                abwesenheitsfeld(h)
            } else if naechsteStunden(h).isEmpty {
                // Keine (weiteren) Stunden: Fläche wie im Original („Genieße die Zeit!“), in den Ferien deren Name und
                // Zeitraum, außerhalb des Plan-Fensters der Sperrhinweis
                freiFeld(h)
            } else {
                stundenspalte(h)
            }
        }
        .padding(groessenklasse == .compact ? 14 : 16)
        .alsKarte()
    }

    /// Graue Fläche „Abwesend“ (Vorlage) mit Zeitraum und ggf. Grund.
    private func abwesenheitsfeld(_ h: Heute) -> some View {
        let hinweis = abwesenheitsHinweis(h) ?? ""
        var zusatz = hinweis
        for vorsilbe in ["Klasse abwesend", "abwesend"] where zusatz.lowercased().hasPrefix(vorsilbe.lowercased()) {
            zusatz = String(zusatz.dropFirst(vorsilbe.count)).trimmingCharacters(in: .whitespaces)
            break
        }
        return VStack(spacing: 3) {
            Text(hinweis.lowercased().hasPrefix("klasse") ? "Klasse abwesend" : "Abwesend")
                .font(.system(size: 13, weight: .medium))
            if !zusatz.isEmpty {
                Text(zusatz)
                    .font(.system(size: 12))
                    .opacity(0.9)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
            }
        }
        .foregroundStyle(.white)
        .padding(10)
        .frame(maxWidth: .infinity, minHeight: 82)
        .background(Farben.abwesendFlaeche, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    private func stundenspalte(_ h: Heute) -> some View {
        let stunden = naechsteStunden(h)
        let istHeute = Datumsformat.istHeute(h.datum)
        let sichtbar = alleStunden ? stunden : Array(stunden.prefix(2))
        let rest = Array(stunden.dropFirst(sichtbar.count))
        return VStack(alignment: .leading, spacing: 0) {
            (Text(istHeute ? "Heute" : Datumsformat.anzeige(h.datum, "EEEE"))
                .font(.system(size: 14, weight: .medium))
                .foregroundColor(Farben.tinte2)
             + Text("  |  ")
                .font(.system(size: 12))
                .foregroundColor(Farben.gedaempft.opacity(0.6))
             + Text("Mein Stundenplan")
                .font(.system(size: 14, weight: .medium))
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
                                    ForEach(rest.prefix(6)) { _ in
                                        Capsule()
                                            .fill(Farben.karteBalken)
                                            .frame(width: 4, height: 13)
                                    }
                                }
                                Text(rest.count == 1 ? "+1 Stunde" : "+\(rest.count) Stunden")
                            } else {
                                Text("Weniger anzeigen")
                            }
                        }
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// Fläche rechts in der Tageskarte, wenn keine Stunden anstehen (Vorlage 154).
    private func freiFeld(_ h: Heute) -> some View {
        let inhalt: (titel: String, text: String)
        if h.gesperrt {
            let hinweis = (h.sperrhinweis ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            inhalt = (Sperrhinweis.titel, hinweis.isEmpty ? Sperrhinweis.standardHinweis : hinweis)
        } else if let f = h.ferien ?? ferien {
            inhalt = (f.titel.isEmpty ? "Ferien" : f.titel, f.zeitraum)
        } else if h.karten.isEmpty {
            inhalt = ("Genieße die Zeit!", Datumsformat.istHeute(h.datum) ? "Heute ist kein Unterricht geplant." : "An diesem Tag ist kein Unterricht geplant.")
        } else {
            inhalt = ("Genieße die Zeit!", "Heute stehen keine weiteren Stunden an.")
        }
        return Hinweisfeld(titel: inhalt.titel, text: inhalt.text, minHoehe: 112)
    }

    private func keineStunden(_ h: Heute) -> some View {
        VStack(spacing: 3) {
            Image(systemName: "cup.and.saucer")
                .font(.system(size: 26, weight: .light))
                .foregroundStyle(Farben.tinte2)
            Text("Keine Stunden")
                .font(.system(size: 15, weight: .medium))
                .foregroundStyle(Farben.tinte)
                .padding(.top, 5)
            Text(h.gesperrt ? "Es werden hier keine Termine angezeigt." : (h.karten.isEmpty ? "Heute ist frei." : "Heute keine weiteren Stunden."))
                .font(.system(size: 13.5))
                .foregroundStyle(Farben.tinte2)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
    }

    // MARK: - Liste (.rows)

    private struct Listeneintrag: Identifiable {
        let ziel: Startziel
        let titel: String
        let symbol: String
        let zahl: Int
        var untertitel: String? = nil
        var id: Startziel { ziel }
    }

    private func eintraege(_ h: Heute) -> [Listeneintrag] {
        var liste: [Listeneintrag] = [
            Listeneintrag(ziel: .termine, titel: "Meine Termine", symbol: "calendar.badge.clock", zahl: 0)
        ]
        if zustand.istKollegium {
            liste.append(Listeneintrag(ziel: .abwesenheiten, titel: "Meine Abwesenheiten", symbol: "calendar.badge.exclamationmark", zahl: 0))
            if zustand.entschuldigungsrechte?.irgendwas == true {
                liste.append(Listeneintrag(ziel: .entschuldigungen, titel: "Entschuldigungen", symbol: "checkmark.seal", zahl: 0))
            }
        } else if zustand.istFamilie {
            liste.append(Listeneintrag(ziel: .familieAbwesenheiten, titel: "Meine Abwesenheiten", symbol: "calendar.badge.exclamationmark", zahl: 0))
        }
        if !h.hausaufgaben.isEmpty || h.typ == "klasse" {
            liste.append(Listeneintrag(ziel: .hausaufgaben, titel: "Hausaufgaben", symbol: "book.closed", zahl: h.hausaufgaben.count))
        }
        // Sprechtag statt Pausenaufsichten (die Aufsichten stehen im Stundenplan)
        // Auch angekündigte Sprechtage (noch keine Buchung möglich) erscheinen mit Datum, Ort und Anmeldebeginn
        for t in sprechtage {
            liste.append(Listeneintrag(ziel: .sprechtag(t.id), titel: t.titel, symbol: t.angekuendigt ? "megaphone" : "person.2",
                                       zahl: t.termine, untertitel: t.untertitel))
        }
        if h.aenderungen > 0 {
            liste.append(Listeneintrag(ziel: .aenderungen, titel: "Änderungen heute", symbol: "arrow.triangle.2.circlepath", zahl: h.aenderungen))
        }
        return liste
    }

    private func startliste(_ h: Heute) -> some View {
        let liste = eintraege(h)
        return Zeilengruppe {
            ForEach(Array(liste.enumerated()), id: \.element.id) { eintrag in
                let e = eintrag.element
                if case .sprechtag(let sid) = e.ziel {
                    Button {
                        sprechtagWahl = SprechtagWahl(id: sid, titel: e.titel)
                    } label: {
                        Listenzeile(symbol: e.symbol, titel: e.titel, untertitel: e.untertitel, zahl: e.zahl > 0 ? e.zahl : nil)
                    }
                    .buttonStyle(ZeilenKnopfStil())
                } else {
                    NavigationLink(value: e.ziel) {
                        Listenzeile(symbol: e.symbol, titel: e.titel, untertitel: e.untertitel, zahl: e.zahl > 0 ? e.zahl : nil)
                    }
                    .buttonStyle(ZeilenKnopfStil())
                }
                if eintrag.offset < liste.count - 1 {
                    Zeilentrenner()
                }
            }
        }
    }

    /// Sprechtage für alle Rollen laden – der Server entscheidet, was sichtbar ist (Lehrkräfte, Erziehungsberechtigte,
    /// Schüler*innen nur bei Freigabe durch die Schule).
    private var mitSprechtag: Bool {
        zustand.benutzer != nil
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        let api = zustand.api
        let sprechtagLaden = mitSprechtag
        @Sendable func holeSprechtage() async -> [SprechtagKarte]? {
            guard sprechtagLaden else { return [] }
            return try? await api.sprechtagStart()
        }
        async let sprechtagListe = holeSprechtage()
        await zustand.entschuldigungsrechteLaden(neu: true)
        do {
            let neu = try await api.heute()
            heute = neu
            zustand.ungelesen = neu.ungelesen
            fehler = nil
            if neu.ferien == nil {
                ferien = await ferienSuchen(neu)
            } else {
                ferien = nil
            }
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        let geladeneSprechtage = await sprechtagListe
        // Neuere Server liefern die Sprechtage auch in /heute (gleiche Karten); sonst /sprechtag/start
        if let ausHeute = heute?.sprechtage, !ausHeute.isEmpty || geladeneSprechtage == nil {
            sprechtage = ausHeute
        } else if let geladeneSprechtage {
            sprechtage = geladeneSprechtage
        }
    }

    /// Ferien für die Tageskarte aus dem Wochenplan: Liegt der angezeigte Tag bzw. der nächste Wochentag (Mo–Fr)
    /// ab diesem Tag in den Ferien, gelten deren Name und Zeitraum. Nur ohne Stunden und ohne Sperre (sonst nil).
    private func ferienSuchen(_ h: Heute) async -> Ferienzeit? {
        guard h.paket, !h.gesperrt, h.karten.isEmpty, let element = h.element, !element.isEmpty,
              let tag = Datumsformat.datum(h.datum) else { return nil }
        let typ = h.typ.isEmpty ? "klasse" : h.typ
        var tage: [Tageskopf] = []
        for versatz in [0, 7] {
            guard let d = Datumsformat.kalender.date(byAdding: .day, value: versatz, to: tag),
                  let plan = try? await zustand.api.plan(typ: typ, element: element, datum: d) else { continue }
            tage += plan.tage
        }
        // Erster Tag (ab dem angezeigten) im Plan: frei wegen Ferien → Ferien; sonst keine
        guard let erster = tage.filter({ $0.datum >= h.datum }).sorted(by: { $0.datum < $1.datum }).first,
              erster.frei, (erster.freiArt ?? "").lowercased() == "ferien" else { return nil }
        let grund = (erster.freiGrund ?? "").trimmingCharacters(in: .whitespaces)
        let titel = grund.isEmpty ? "Ferien" : grund
        return Ferienzeit(titel: titel, von: erster.freiVon ?? erster.datum, bis: erster.freiBis ?? "")
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
                    .font(.system(size: 18, weight: .medium))
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
                    .font(.system(size: 15, weight: eintrag.offset == 0 ? .medium : .regular))
            }
            Text(karte.fach.isEmpty ? karte.titel : karte.fach)
                .font(.system(size: 15, weight: zeilen.isEmpty ? .medium : .regular))
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
        return werteText(werte, offen: werte == karte.lehrer && istOffeneVertretung(karte))
    }
}

/// Unterseite der Startliste: Termine, Hausaufgaben, Aufsichten oder Änderungen.
struct StartlisteView: View {
    let ziel: Startziel
    let heute: Heute?
    let antippen: (Karte) -> Void
    @EnvironmentObject var zustand: AppZustand
    @State private var meldungen: [Meldung]? = nil
    @State private var fehler: String? = nil

    private var titel: String {
        switch ziel {
        case .termine: return "Meine Termine"
        case .abwesenheiten, .familieAbwesenheiten: return "Meine Abwesenheiten"
        case .hausaufgaben: return "Hausaufgaben"
        case .aufsichten: return "Pausenaufsichten"
        case .aenderungen: return "Änderungen heute"
        case .sprechtag: return "Sprechtag"
        case .entschuldigungen: return "Entschuldigungen"
        }
    }

    var body: some View {
        List {
            if let h = heute {
                switch ziel {
                case .termine: termine(h)
                case .abwesenheiten, .familieAbwesenheiten: abwesenheiten()
                case .hausaufgaben: hausaufgaben(h)
                case .aufsichten: aufsichten(h)
                case .aenderungen: aenderungen(h)
                case .sprechtag, .entschuldigungen: EmptyView()
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle(titel)
        .navigationBarTitleDisplayMode(.large)
        .toolbar(.visible, for: .navigationBar)
        .task {
            if ziel == .abwesenheiten { await meldungenLaden() }
        }
    }

    // MARK: Meine Abwesenheiten (eigene Meldungen vom Typ Abwesenheit)

    @ViewBuilder
    private func abwesenheiten() -> some View {
        if let fehler {
            Fehlerhinweis(text: fehler)
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
        }
        if let liste = meldungen?.filter(\.istAbwesenheit) {
            if liste.isEmpty {
                leer("Keine Abwesenheiten gemeldet.")
            }
            ForEach(liste) { m in
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text(abwesenheitszeitraum(m))
                            .font(.system(size: 17.5, weight: .medium))
                            .foregroundStyle(Farben.tinte)
                        Spacer()
                        Text(m.status == "offen" ? "Offen" : (m.status == "quittiert" ? "Bestätigt" : m.status))
                            .font(.system(size: 14, weight: .medium))
                            .foregroundStyle(m.status == "offen" ? Farben.heute : Farben.gruen)
                    }
                    let grund = (m.grund ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                    if !grund.isEmpty {
                        Text(grund)
                            .font(.system(size: 16))
                            .foregroundStyle(Farben.tinte2)
                    }
                    // Bemerkung bzw. Text als eigene Zeile – nur wenn vorhanden
                    let bemerkung = (m.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                    if !bemerkung.isEmpty {
                        Text(bemerkung)
                            .font(.system(size: 16))
                            .foregroundStyle(Farben.gedaempft)
                            .lineLimit(4)
                    }
                }
                .padding(.vertical, 4)
                .listRowBackground(Farben.flaeche)
            }
        } else if fehler == nil {
            ProgressView()
                .frame(maxWidth: .infinity)
                .listRowBackground(Farben.flaeche)
        }
    }

    private func abwesenheitszeitraum(_ m: Meldung) -> String {
        let von = m.von ?? ""
        let bis = m.bis ?? von
        var text = Datumsformat.anzeige(von, "EE d.M.yyyy")
        if !bis.isEmpty && bis.prefix(10) != von.prefix(10) {
            text += " – " + Datumsformat.anzeige(bis, "EE d.M.yyyy")
        }
        if let v = m.stundeVon {
            let b = m.stundeBis ?? v
            text += v == b ? ", \(v). Std." : ", \(v).–\(b). Std."
        }
        return text
    }

    private func meldungenLaden() async {
        do {
            meldungen = try await zustand.api.meldungen()
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
            if meldungen == nil { meldungen = [] }
        }
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
                        .font(.system(size: 22, weight: .medium))
                    Text(Datumsformat.anzeige(t.von, "MMM"))
                        .font(.system(size: 12, weight: .medium))
                }
                .foregroundStyle(Farben.heute)
                .frame(width: 44)
                VStack(alignment: .leading, spacing: 2) {
                    Text(t.titel.isEmpty ? t.art : t.titel)
                        .font(.system(size: 16, weight: .medium))
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
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                    Spacer()
                    if let f = ha.faellig, !f.isEmpty {
                        Text("fällig \(Datumsformat.anzeige(f, "EE d.M."))")
                            .font(.system(size: 13, weight: .medium))
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
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                        .strikethrough(a.status == "vertreten" || a.status == "entfall", color: Farben.entfallRand)
                    Text("nach der \(a.nachStunde). Stunde · \(a.beginn) – \(a.ende)")
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                }
                Spacer()
                if a.status == "vertretung" {
                    Plakette(text: "Vertretung", farbe: Farben.gruen)
                } else if a.status == "vertreten" {
                    Plakette(text: "Vertreten", farbe: Farben.entfallRand)
                } else if a.status == "entfall" {
                    Plakette(text: "Entfällt", farbe: Farben.entfallRand)
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
                    .font(.subheadline.weight(.medium).monospacedDigit())
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
                    .font(.subheadline.weight(.medium))
                    .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                    .lineLimit(1)
                let unten = zeilen.filter { !$0.isEmpty }
                if !unten.isEmpty {
                    unten.dropFirst().reduce(werteText(unten[0], offen: offen(unten[0]))) { $0 + Text(" · ") + werteText($1, offen: offen($1)) }
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
                .font(.caption.weight(.medium))
                .foregroundStyle(.tertiary)
        }
        .foregroundStyle(Farben.tinte)
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .contentShape(Rectangle())
    }

    private func offen(_ werte: [Wert]) -> Bool {
        werte == karte.lehrer && istOffeneVertretung(karte)
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
        // Keine Animation und keine Hervorhebung beim Antippen
        configuration.label
    }
}
