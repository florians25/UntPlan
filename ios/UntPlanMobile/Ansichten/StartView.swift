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
    /// Sprechtag (Lehrkräfte und Administration: Termine verwalten; Erziehungsberechtigte: buchen, annehmen und absagen)
    case sprechtag(Int)
    /// Entschuldigungen (Klassenleitung: Abwesenheiten der eigenen Klassen; Fachlehrkraft: eigene Fehlstunden)
    case entschuldigungen
    /// Klassenleitung: Übersicht der eigenen Klasse(n) mit den Abwesenheiten
    case klassenleitung
    /// Klassenleitung: Abwesenheiten der eigenen Klasse(n) direkt (wie in der Vorlage mit Zahl der offenen)
    case klassenAbwesenheiten
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
    /// Klassenleitung: Zahl der offenen Abwesenheiten (letzte 30 Tage) für den Startpunkt „Abwesenheiten“
    @State private var offeneAbwesenheiten = 0
    /// Geöffneter Sprechtag (als Blatt wie die Stundendetails)
    @State private var sprechtagWahl: SprechtagWahl? = nil
    @Environment(\.horizontalSizeClass) private var groessenklasse
    /// Navigationspfad der Startseite (Startargumente für Bildschirmfotos öffnen darüber Termine bzw. Abwesenheiten)
    @State private var startPfad = NavigationPath()

    /// Sprechtag, der als Blatt gezeigt wird.
    struct SprechtagWahl: Identifiable {
        let id: Int
        let titel: String
    }

    /// iPhone: Seitenrand 16 pt (iPad 18 pt, mittig höchstens 736 pt breit).
    private var rand: CGFloat { groessenklasse == .compact ? 16 : 18 }

    var body: some View {
        NavigationStack(path: $startPfad) {
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
            .task {
                await laden()
                startzielOeffnen()
            }
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
                case .klassenleitung:
                    KlassenleitungView()
                        .environmentObject(zustand)
                case .klassenAbwesenheiten:
                    KlassenAbwesenheitenStart()
                        .environmentObject(zustand)
                case .termine:
                    MeineTermineView(heute: heute)
                        .environmentObject(zustand)
                case .abwesenheiten:
                    EigeneAbwesenheitenView()
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
            // Vorlage nachgemessen: 26 pt fett, darunter 24 pt bis zur Tageskarte
            .font(.system(size: 26, weight: .bold))
            .tracking(-0.3)
            .foregroundStyle(Farben.titelGrau)
            .lineLimit(2)
            .minimumScaleFactor(0.7)
            .padding(.top, 2)
            .padding(.bottom, 24)
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
                // Vorlage: Wochentag und Monat 13 pt halbfett, Tageszahl 34 pt
                Text(Datumsformat.anzeige(h.datum, "EEEE"))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Farben.tinte2)
                Text(Datumsformat.anzeige(h.datum, "d"))
                    .font(.system(size: 34, weight: .medium))
                    .foregroundStyle(Farben.heute)
                    .padding(.top, 2)
                Text(Datumsformat.anzeige(h.datum, "MMMM"))
                    .font(.system(size: 13, weight: .semibold))
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
            .frame(minWidth: groessenklasse == .compact ? 72 : 84, alignment: .leading)

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
        // Vorlage: 21 pt seitlich, 17 pt oben, Karte mindestens 152 pt hoch
        .padding(.horizontal, groessenklasse == .compact ? 21 : 22)
        .padding(.top, 17)
        .padding(.bottom, 18)
        .frame(minHeight: 152, alignment: .top)
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
            // Original-Widget: „Heute“ bzw. „Morgen“ (sonst Wochentag) | Anmeldename
            (Text(istHeute ? "Heute" : Datumsformat.istMorgen(h.datum) ? "Morgen" : Datumsformat.anzeige(h.datum, "EEEE"))
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(Farben.tinte2)
             + Text("  |  ")
                .font(.system(size: 12))
                .foregroundColor(Farben.gedaempft.opacity(0.6))
             + Text(zustand.benutzer?.login ?? "Mein Stundenplan")
                .font(.system(size: 13, weight: .medium))
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
                    .padding(.bottom, 13)
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
            if zustand.entschuldigungsrechte?.klassenleitung == true {
                // „Klassenleitung“ entfällt: die Klassenleitung arbeitet über „Abwesenheiten“
                liste.append(Listeneintrag(ziel: .klassenAbwesenheiten, titel: "Abwesenheiten", symbol: "chair", zahl: offeneAbwesenheiten))
            }
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
        for t in (mitSprechtag ? sprechtage : []) {
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
                        Listenzeile(symbol: e.symbol, titel: e.titel, untertitel: e.untertitel, zahl: e.zahl > 0 ? e.zahl : nil, gross: true)
                    }
                    .buttonStyle(ZeilenKnopfStil())
                } else {
                    NavigationLink(value: e.ziel) {
                        Listenzeile(symbol: e.symbol, titel: e.titel, untertitel: e.untertitel, zahl: e.zahl > 0 ? e.zahl : nil, gross: true)
                    }
                    .buttonStyle(ZeilenKnopfStil())
                }
                if eintrag.offset < liste.count - 1 {
                    Zeilentrenner()
                }
            }
        }
    }

    /// Sprechtage für Lehrkräfte, Administration und Erziehungsberechtigte laden – der Server entscheidet, was sichtbar ist.
    /// Schüler*innen haben keinen Zugriff auf den Sprechtag.
    private var mitSprechtag: Bool {
        guard let b = zustand.benutzer else { return false }
        return b.rolle.lowercased() != "schueler"
    }

    /// Nur mit Startargumenten (Debug-Builds, Bildschirmfotos): „Meine Termine“ bzw. „Meine Abwesenheiten“ öffnen.
    private func startzielOeffnen() {
        guard Startsteuerung.aktiv, startPfad.isEmpty else { return }
        switch Startsteuerung.ansicht {
        case "termine":
            startPfad.append(Startziel.termine)
        case "abwesenheiten":
            startPfad.append(zustand.istFamilie ? Startziel.familieAbwesenheiten : Startziel.abwesenheiten)
        default:
            break
        }
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
        let rechte = await zustand.entschuldigungsrechteLaden(neu: true)
        if rechte.klassenleitung {
            let offen = try? await api.entschuldigungsAbwesenheiten(von: Datumsformat.iso(Datumsformat.tage(-30, nach: Date())),
                                                                    bis: Datumsformat.iso(Date()), klasse: nil, status: "offen")
            offeneAbwesenheiten = offen?.count ?? 0
        }
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

    /// Original-Widget: geänderte Stunde grün hinterlegt mit grünem Rand, geänderte Werte als grüne Plakette
    private var istAenderung: Bool { !karte.istEntfall && ["geaendert", "offen"].contains(karte.status) && !karte.istPruefung }

    var body: some View {
        let stil = Kartenstil(karte)
        let z = kartenzeilen(karte, typ: typ)
        // Reihenfolge wie im Original-Widget: Lehrkraft (bzw. Klasse), Fach, Raum
        let erste = z.first ?? []
        let letzte = z.count > 1 ? z[1] : []
        VStack(alignment: .leading, spacing: 1) {
            if !erste.isEmpty {
                zeile(erste, stil: stil).font(.system(size: 13, weight: .semibold))
            }
            Text(karte.fach.isEmpty ? karte.titel : karte.fach)
                .font(.system(size: 13, weight: erste.isEmpty ? .semibold : .regular))
                .strikethrough(stil.durchgestrichen)
            if !letzte.isEmpty {
                zeile(letzte, stil: stil).font(.system(size: 13))
            }
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
            .font(.system(size: 13).monospacedDigit())
            .foregroundStyle(Farben.tinte2)
            .frame(minWidth: 52, alignment: .trailing)
        }
        .padding(.top, 3)
        .padding(.bottom, 5)
        .padding(.trailing, 6)
        .padding(.horizontal, karte.istEntfall || istAenderung ? 8 : 0)
        .background {
            if karte.istEntfall {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Farben.entfallHintergrund)
                    .overlay {
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(Farben.entfallRand, lineWidth: 1.5)
                    }
            } else if istAenderung {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Farben.widgetAenderungFlaeche)
                    .overlay {
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(Farben.widgetAenderungRand, lineWidth: 2)
                    }
            }
        }
        .contentShape(Rectangle())
    }

    @ViewBuilder
    private func zeile(_ werte: [Wert], stil: Kartenstil) -> some View {
        if stil.durchgestrichen {
            Text(werteKurz(werte)).strikethrough(true)
        } else if istAenderung, werte.contains(where: { $0.geaendert }) {
            // geänderte Werte als grüne Plakette (wie „C126“ im Original-Widget), übrige normal daneben
            HStack(spacing: 4) {
                ForEach(Array(werte.enumerated()), id: \.offset) { e in
                    if e.element.geaendert && !e.element.k.isEmpty {
                        Text(e.element.k)
                            .foregroundStyle(Farben.widgetPlaketteText)
                            .padding(.horizontal, 4)
                            .background(RoundedRectangle(cornerRadius: 4, style: .continuous).fill(Farben.widgetAenderungRand))
                    } else if !e.element.k.isEmpty {
                        Text(e.element.k)
                    }
                }
            }
        } else {
            werteText(werte, offen: werte == karte.lehrer && istOffeneVertretung(karte))
        }
    }
}

/// „Meine Termine“ wie in der Vorlage: Kopf mit Zurück, Titel und „bis <Schuljahresende>“, rechts Filter (Art) und Suche;
/// die Termine nach Datum gruppiert („Mo., 07.12.2026“), je Termin eine dunkle Karte mit Symbol links (Prüfung, Veranstaltung,
/// Sondereinsatz) und den Zeilen Fach (Langname mit Kürzel) bzw. Anlass, Titel, Thema, Klassen, Räume mit Langname und Zeit.
/// Langnamen von Fach und Raum kommen aus den Stammdaten der App (Elemente). Ältere Server ohne /termine: die Termine der
/// Startseite (nächste zwei Wochen).
struct MeineTermineView: View {
    let heute: Heute?
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var liste: Terminliste? = nil
    @State private var faecher: [String: String] = [:]
    @State private var raeume: [String: String] = [:]
    @State private var fehler: String? = nil
    @State private var laedt = false
    @State private var art = ""
    @State private var suche = ""
    @State private var sucheOffen = false

    private var rand: CGFloat { groessenklasse == .compact ? 16 : 18 }

    /// Filter nach Art (leer = alle)
    private static let arten: [(wert: String, titel: String)] = [
        ("", "Alle Termine"), ("klausur", "Prüfungen"), ("veranstaltung", "Veranstaltungen"), ("sondereinsatz", "Sondereinsätze")
    ]

    private var gefiltert: [MeinTermin] {
        let alle = liste?.termine ?? []
        let nachArt = art.isEmpty ? alle : alle.filter { art == "klausur" ? $0.istPruefung : $0.art.lowercased() == art }
        let s = suche.trimmingCharacters(in: .whitespaces)
        guard !s.isEmpty else { return nachArt }
        return nachArt.filter { t in
            ([ueberschrift(t)] + zeilen(t).map(\.text) + t.lehrer).contains { $0.localizedCaseInsensitiveContains(s) }
        }
    }

    /// Termine nach Datum (Beginn) gruppiert, in der Reihenfolge der Daten.
    private var gruppen: [Datumsgruppe<MeinTermin>] {
        Datumsgruppe.bilden(gefiltert.sorted { ($0.von, $0.stundeVon ?? 0) < ($1.von, $1.stundeVon ?? 0) }, datum: \.von)
    }

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 12) {
                if sucheOffen {
                    Suchleiste(text: $suche, platzhalter: "Termine durchsuchen")
                }
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if liste != nil {
                    if gruppen.isEmpty {
                        Text(leertext)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.top, 8)
                    }
                    ForEach(gruppen) { g in
                        VStack(alignment: .leading, spacing: 12) {
                            Datumsgruppenkopf(text: Datumsgruppenkopf.text(g.datum))
                            ForEach(g.eintraege) { t in
                                Listenkarte(symbol: symbol(t), titel: ueberschrift(t), zeilen: zeilen(t))
                            }
                        }
                    }
                } else if laedt {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, rand)
            // Vorlage 33da91d4: erste Datumsgruppe (Versalhöhe) 151,7 pt unter dem oberen Rand
            .padding(.top, 28.7)
            .padding(.bottom, 28)
            .zentriert(maxBreite: 736)
        }
        .scrollIndicators(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .refreshable { await laden() }
        .navigationTitle("Meine Termine")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .toolbar {
            ToolbarItem(placement: .principal) {
                Listentitel(titel: "Meine Termine",
                            untertitel: (liste?.bis ?? "").isEmpty ? nil : "bis \(Datumsformat.anzeige(liste?.bis ?? "", "dd.MM.yyyy"))")
            }
        }
        // Wie in der Vorlage zwei getrennte runde Knöpfe: Filter (Trichter, grau) und Suche
        .zweiKnoepfe(links: filterKnopf, rechts: suchKnopf)
        .task {
            if liste == nil {
                await laden()
                await stammdatenLaden()
            }
        }
    }

    private var filterKnopf: some View {
        Menu {
            Picker("Art", selection: $art) {
                ForEach(Self.arten, id: \.wert) { a in
                    Text(a.titel).tag(a.wert)
                }
            }
        } label: {
            Glasknopf(breite: 32) {
                // Vorlage 33da91d4: Trichter (Umriss, 64 × 64 px) in #8997a4; gewählter Filter gefüllt
                Trichter(gefuellt: !art.isEmpty)
                    .foregroundStyle(Color(hexWert: 0x8997A4))
            }
        }
        .accessibilityLabel("Filter")
    }

    private var suchKnopf: some View {
        Button {
            withAnimation(.easeOut(duration: 0.2)) {
                sucheOffen.toggle()
                if !sucheOffen { suche = "" }
            }
        } label: {
            // Vorlage: Lupe 63 × 63 px, kräftiger Strich, #f3f3f3
            Glasknopf(breite: 32) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 17.5, weight: .medium))
                    .foregroundStyle(Color(hexWert: 0xF3F3F3))
            }
        }
        .accessibilityLabel(sucheOffen ? "Suche schließen" : "Termine durchsuchen")
    }

    private var leertext: String {
        if !suche.trimmingCharacters(in: .whitespaces).isEmpty { return "Kein Termin passt zu „\(suche)“." }
        if !art.isEmpty { return "Keine Termine dieser Art." }
        return (liste?.bis ?? "").isEmpty ? "Keine Termine in den nächsten zwei Wochen." : "Keine Termine bis zum Ende des Schuljahrs."
    }

    // MARK: Inhalt einer Karte

    private func symbol(_ t: MeinTermin) -> String {
        if t.istPruefung { return "list.bullet.clipboard" }
        switch t.art.lowercased() {
        case "veranstaltung": return "person.3"
        case "sondereinsatz": return "calendar.badge.clock"
        default: return "calendar"
        }
    }

    /// Fachname mit Kürzel („Politik (PK)“) bei Prüfungen, sonst der Titel bzw. die Art.
    private func ueberschrift(_ t: MeinTermin) -> String {
        if t.istPruefung && !t.fach.isEmpty {
            if let name = faecher[t.fach.uppercased()], !name.isEmpty, name.caseInsensitiveCompare(t.fach) != .orderedSame {
                return "\(name) (\(t.fach))"
            }
            return t.fach
        }
        if !t.titel.isEmpty { return t.titel }
        return artName(t)
    }

    private func artName(_ t: MeinTermin) -> String {
        if t.istPruefung { return "Prüfung" }
        switch t.art.lowercased() {
        case "veranstaltung": return "Veranstaltung"
        case "sondereinsatz": return "Sondereinsatz"
        default: return t.art.isEmpty ? "Termin" : t.art
        }
    }

    /// Zeilen unter der Überschrift: Titel (bei Prüfungen mit Fach), Thema, Klassen, Räume mit Langname, Zeit.
    private func zeilen(_ t: MeinTermin) -> [Listenkartenzeile] {
        var z: [Listenkartenzeile] = []
        let kopf = ueberschrift(t)
        if t.istPruefung && !t.fach.isEmpty && !t.titel.isEmpty && t.titel != kopf {
            z.append(Listenkartenzeile(text: t.titel))
        }
        let thema = t.text.trimmingCharacters(in: .whitespacesAndNewlines)
        if !thema.isEmpty && thema != t.titel { z.append(Listenkartenzeile(text: thema)) }
        if !t.klassen.isEmpty { z.append(Listenkartenzeile(text: t.klassen.joined(separator: ", "))) }
        let raumtext = t.raeume.filter { !$0.isEmpty }.map { r -> String in
            if let name = raeume[r.uppercased()], !name.isEmpty, name.caseInsensitiveCompare(r) != .orderedSame { return "\(r) (\(name))" }
            return r
        }
        if !raumtext.isEmpty { z.append(Listenkartenzeile(text: raumtext.joined(separator: ", "))) }
        let zeit = zeittext(t)
        if !zeit.isEmpty { z.append(Listenkartenzeile(text: zeit, farbe: Farben.tinte2)) }
        if t.status == "vorgemerkt" { z.append(Listenkartenzeile(text: "Vorgemerkt (noch nicht übernommen)", farbe: Farben.orange)) }
        return z
    }

    /// „3.–4. Stunde · 09:35 – 11:05“, mehrtägig „bis Fr., 16.10.2026“.
    private func zeittext(_ t: MeinTermin) -> String {
        var teile: [String] = []
        if !t.bis.isEmpty && t.bis.prefix(10) != t.von.prefix(10) {
            teile.append("bis \(Datumsformat.anzeige(t.bis, "EE, dd.MM.yyyy"))")
        } else if let v = t.stundeVon, v > 0 {
            let b = max(t.stundeBis ?? v, v)
            teile.append(v == b ? "\(v). Stunde" : "\(v).–\(b). Stunde")
            if !t.beginn.isEmpty && !t.ende.isEmpty { teile.append("\(t.beginn) – \(t.ende)") }
        }
        return teile.joined(separator: " · ")
    }

    // MARK: Laden

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            liste = try await zustand.api.termine()
            fehler = nil
        } catch {
            // Älterer Server ohne /termine: die Termine der Startseite (nächste zwei Wochen)
            let ersatz = Terminliste(bis: "", termine: (heute?.termine ?? []).map(MeinTermin.init))
            var alterServer = false
            if let a = error as? ApiFehler {
                switch a {
                case .server, .antwortUnlesbar: alterServer = true
                default: break
                }
            }
            if alterServer {
                fehler = nil
            } else if let text = zustand.fehlertext(error) {
                fehler = text
            }
            if liste == nil { liste = ersatz }
        }
    }

    /// Langnamen von Fächern und Räumen aus den Stammdaten (Elemente).
    private func stammdatenLaden() async {
        guard let e = try? await zustand.api.elemente() else { return }
        var f: [String: String] = [:]
        for x in e.faecher where !x.kurz.isEmpty { f[x.kurz.uppercased()] = x.name }
        var r: [String: String] = [:]
        for x in e.raeume where !x.kurz.isEmpty { r[x.kurz.uppercased()] = x.name }
        faecher = f
        raeume = r
    }
}

/// Unterseite der Startliste: Hausaufgaben, Aufsichten oder Änderungen.
struct StartlisteView: View {
    let ziel: Startziel
    let heute: Heute?
    let antippen: (Karte) -> Void
    @EnvironmentObject var zustand: AppZustand

    private var titel: String {
        switch ziel {
        case .termine: return "Meine Termine"
        case .abwesenheiten, .familieAbwesenheiten: return "Meine Abwesenheiten"
        case .hausaufgaben: return "Hausaufgaben"
        case .aufsichten: return "Pausenaufsichten"
        case .aenderungen: return "Änderungen heute"
        case .sprechtag: return "Sprechtag"
        case .entschuldigungen: return "Entschuldigungen"
        case .klassenleitung: return "Klassenleitung"
        case .klassenAbwesenheiten: return "Abwesenheiten"
        }
    }

    var body: some View {
        List {
            if let h = heute {
                switch ziel {
                case .hausaufgaben: hausaufgaben(h)
                case .aufsichten: aufsichten(h)
                case .aenderungen: aenderungen(h)
                case .termine, .abwesenheiten, .familieAbwesenheiten, .sprechtag, .entschuldigungen, .klassenleitung, .klassenAbwesenheiten: EmptyView()
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
                } else if a.status == "offen" {
                    Plakette(text: "Offen", farbe: Farben.gruen)
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
