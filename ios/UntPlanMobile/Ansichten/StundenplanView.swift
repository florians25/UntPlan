import SwiftUI

/// Stundenplan als Wochenraster nach Uhrzeit (wie die PWA): Kopfzeile mit Planwechsel, Planname und Menü,
/// darunter KW/Monat und Tage, links die Zeitspalte. Wischen wechselt die Woche.
struct StundenplanView: View {
    @EnvironmentObject var zustand: AppZustand
    @EnvironmentObject var favoriten: Favoriten
    @State private var datum = Date()
    @State private var auswahl: PlanAuswahl? = nil
    @State private var plan: Wochenplan? = nil
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil
    @State private var zeigeAuswahl = false

    private var montag: Date { Datumsformat.montag(datum) }

    private var typ: String {
        if let auswahl { return auswahl.typ }
        let t = zustand.benutzer?.typ ?? ""
        return t.isEmpty ? "klasse" : t
    }

    private var element: String {
        auswahl?.element ?? zustand.benutzer?.element ?? ""
    }

    private var ladeSchluessel: String {
        "\(typ)|\(element)|\(Datumsformat.iso(montag))"
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                kopfleiste
                inhalt
            }
            .background(Farben.leiste.ignoresSafeArea())
            .toolbar(.hidden, for: .navigationBar)
            .sheet(item: $gewaehlteKarte) { karte in
                StundendetailView(karte: karte, typ: typ)
                    .presentationDetents([.medium, .large])
            }
            .sheet(isPresented: $zeigeAuswahl) {
                PlanAuswahlView(auswahl: $auswahl)
                    .environmentObject(zustand)
                    .environmentObject(favoriten)
            }
            .task(id: ladeSchluessel) { await laden() }
        }
    }

    private var titel: String {
        if let auswahl { return auswahl.anzeige }
        if let name = plan?.name, !name.isEmpty { return name }
        if !element.isEmpty { return element }
        return "Mein Stundenplan"
    }

    // MARK: - Kopfleiste (.topbar)

    private var kopfleiste: some View {
        HStack(spacing: 8) {
            Menu {
                Button {
                    auswahl = nil
                } label: {
                    Label("Mein Stundenplan", systemImage: auswahl == nil ? "checkmark" : "calendar")
                }
                if !favoriten.liste.isEmpty {
                    Section("Favoriten") {
                        ForEach(favoriten.liste) { fav in
                            Button {
                                auswahl = fav
                            } label: {
                                Label(fav.anzeige, systemImage: auswahl?.id == fav.id ? "checkmark" : fav.symbol)
                            }
                        }
                    }
                }
                Divider()
                Button {
                    zeigeAuswahl = true
                } label: {
                    Label("Stundenplan auswählen …", systemImage: "magnifyingglass")
                }
            } label: {
                PlanwechselSymbol()
                    .frame(width: 44, height: 44)
                    .glas(Circle())
                    .contentShape(Circle())
            }
            .accessibilityLabel("Plan wechseln")

            Button {
                zeigeAuswahl = true
            } label: {
                HStack(spacing: 5) {
                    Text(titel)
                        .font(.system(size: 17.5, weight: .medium))
                        .tracking(-0.09)
                        .lineLimit(1)
                        .truncationMode(.tail)
                    Image(systemName: "chevron.down")
                        .font(.system(size: 13, weight: .semibold))
                }
                .foregroundStyle(Farben.titelGrau)
                .padding(.vertical, 6)
                .padding(.horizontal, 4)
                .frame(maxWidth: .infinity)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Stundenplan auswählen, aktuell \(titel)")

            Menu {
                Button {
                    datum = Date()
                } label: {
                    Label("Heute", systemImage: "calendar.circle")
                }
                .disabled(Datumsformat.iso(montag) == Datumsformat.iso(Datumsformat.montag(Date())))
                Button {
                    datum = Datumsformat.tage(-7, nach: montag)
                } label: {
                    Label("Vorherige Woche", systemImage: "chevron.left")
                }
                Button {
                    datum = Datumsformat.tage(7, nach: montag)
                } label: {
                    Label("Nächste Woche", systemImage: "chevron.right")
                }
                Divider()
                Button {
                    Task { await laden() }
                } label: {
                    Label("Aktualisieren", systemImage: "arrow.clockwise")
                }
            } label: {
                Image(systemName: "ellipsis")
                    .font(.system(size: 19, weight: .semibold))
                    .rotationEffect(.degrees(90))
                    .foregroundStyle(Farben.tinte2)
                    .frame(width: 44, height: 44)
                    .glas(Circle())
                    .contentShape(Circle())
            }
            .accessibilityLabel("Menü")
        }
        .padding(.horizontal, 12)
        .padding(.top, 6)
        .padding(.bottom, 4)
        .background(Farben.leiste)
    }

    // MARK: - Inhalt

    @ViewBuilder
    private var inhalt: some View {
        if let fehler, plan == nil {
            VStack {
                Fehlerhinweis(text: fehler)
                Button("Erneut versuchen") { Task { await laden() } }
                    .padding(.top, 8)
                Spacer()
            }
            .padding(16)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Farben.rasterOben)
        } else if element.isEmpty && auswahl == nil {
            ContentUnavailableView {
                Label("Kein eigener Plan", systemImage: "calendar.badge.exclamationmark")
            } description: {
                Text("Ihrem Konto ist kein Plan zugeordnet. Wählen Sie oben eine Klasse, eine Lehrkraft oder einen Raum.")
            } actions: {
                Button("Stundenplan auswählen") { zeigeAuswahl = true }
                    .buttonStyle(.borderedProminent)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Farben.rasterOben)
        } else if let plan {
            WochenrasterView(plan: plan, typ: typ, laedt: laedt) { gewaehlteKarte = $0 }
                .simultaneousGesture(
                    DragGesture(minimumDistance: 40)
                        .onEnded { wert in
                            guard abs(wert.translation.width) > abs(wert.translation.height) * 1.5 else { return }
                            if wert.translation.width < -60 {
                                datum = Datumsformat.tage(7, nach: montag)
                            } else if wert.translation.width > 60 {
                                datum = Datumsformat.tage(-7, nach: montag)
                            }
                        }
                )
        } else {
            ProgressView("Plan wird geladen …")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Farben.rasterOben)
        }
    }

    private func laden() async {
        guard !element.isEmpty else { return }
        laedt = true
        defer { laedt = false }
        do {
            let neu = try await zustand.api.plan(typ: typ, element: element, datum: montag)
            plan = neu
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Person mit zwei Pfeilen (Plan wechseln), nachgebaut aus SF Symbols.
struct PlanwechselSymbol: View {
    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            Image(systemName: "person")
                .font(.system(size: 18, weight: .regular))
                .offset(x: -4, y: -3)
            Image(systemName: "arrow.up.arrow.down")
                .font(.system(size: 10, weight: .semibold))
                .offset(x: 3, y: 2)
        }
        .foregroundStyle(Farben.tinte2)
        .frame(width: 26, height: 26)
    }
}

// MARK: - Wochenraster

/// Stunde des Zeitrasters in Minuten seit Mitternacht.
struct Rasterzeit: Identifiable {
    let nr: Int
    let beginn: Int
    let ende: Int

    var id: Int { nr }

    static func minuten(_ text: String) -> Int? {
        let teile = text.split(separator: ":")
        guard teile.count >= 2, let h = Int(teile[0]), let m = Int(teile[1].prefix(2)) else { return nil }
        return h * 60 + m
    }

    static func uhrzeit(_ minuten: Int) -> String {
        String(format: "%02d:%02d", minuten / 60, minuten % 60)
    }
}

/// Raster mit Tagesspalten; Höhe nach Uhrzeit (1,2704 pt je Minute wie in der PWA), Pausen als Lücken.
struct WochenrasterView: View {
    let plan: Wochenplan
    let typ: String
    var laedt: Bool = false
    let antippen: (Karte) -> Void

    // Maße aus app.css
    private let rinnenBreite: CGFloat = 45
    private let tagesLuecke: CGFloat = 3
    private let proMinute: CGFloat = 1.2704
    private let abstandOben: CGFloat = 15
    private let abstandUnten: CGFloat = 28

    /// Zeitraster aus dem Plan; ohne Raster 45-Minuten-Stunden ab 8 Uhr.
    private var raster: [Rasterzeit] {
        let aus = plan.zeitraster.compactMap { z -> Rasterzeit? in
            guard let b = Rasterzeit.minuten(z.beginn), let e = Rasterzeit.minuten(z.ende), e > b else { return nil }
            return Rasterzeit(nr: z.nr, beginn: b, ende: e)
        }
        .sorted { $0.beginn < $1.beginn }
        if !aus.isEmpty { return aus }
        let anzahl = max(plan.karten.map(\.bis).max() ?? 8, 8)
        return (1...anzahl).map { nr in
            let b = 480 + (nr - 1) * 50
            return Rasterzeit(nr: nr, beginn: b, ende: b + 45)
        }
    }

    private var tage: [Tageskopf] { plan.tage }

    private var montagDatum: Date {
        Datumsformat.datum(plan.montag) ?? Datumsformat.datum(tage.first?.datum ?? "") ?? Date()
    }

    var body: some View {
        let zeiten = raster
        let start = zeiten.first?.beginn ?? 480
        let ende = zeiten.last?.ende ?? 960
        GeometryReader { geo in
            let spalten = max(tage.count, 1)
            let spaltenBreite = max(40, (geo.size.width - rinnenBreite) / CGFloat(spalten))
            let hoehe = abstandOben + CGFloat(ende - start) * proMinute + abstandUnten
            VStack(spacing: 0) {
                wochenkopf(spaltenBreite: spaltenBreite)
                ScrollView(.vertical) {
                    ZStack(alignment: .topLeading) {
                        zeitspalte(zeiten, start: start, breite: geo.size.width)
                        ForEach(Array(tage.enumerated()), id: \.element.id) { eintrag in
                            tagesspalte(eintrag.element, zeiten: zeiten, start: start, ende: ende, breite: spaltenBreite - tagesLuecke)
                                .offset(x: rinnenBreite + CGFloat(eintrag.offset) * spaltenBreite)
                        }
                        jetztLinie(start: start, ende: ende, spaltenBreite: spaltenBreite, breite: geo.size.width)
                    }
                    .frame(width: geo.size.width, height: hoehe, alignment: .topLeading)
                    .opacity(laedt ? 0.55 : 1)
                    .animation(.easeOut(duration: 0.18), value: laedt)
                }
                .scrollIndicators(.hidden)
                .background(
                    LinearGradient(colors: [Farben.rasterOben, Farben.rasterUnten], startPoint: .top, endPoint: .bottom)
                        .ignoresSafeArea(edges: .bottom)
                )
            }
        }
    }

    private func y(_ minute: Int, start: Int) -> CGFloat {
        abstandOben + CGFloat(minute - start) * proMinute
    }

    // MARK: Kopf mit KW, Monat und Tagen (.weekhead)

    private func wochenkopf(spaltenBreite: CGFloat) -> some View {
        HStack(alignment: .bottom, spacing: 0) {
            VStack(alignment: .leading, spacing: 3) {
                Text("KW \(Datumsformat.kalenderwoche(montagDatum))")
                    .font(.system(size: 12, weight: .regular))
                    .foregroundStyle(Farben.tinte2)
                Text(monatstext)
                    .font(.system(size: 18, weight: .bold))
                    .tracking(-0.18)
                    .foregroundStyle(Farben.tinte)
            }
            .lineLimit(1)
            .minimumScaleFactor(0.7)
            .padding(.leading, 6)
            .frame(width: rinnenBreite, alignment: .leading)
            ForEach(tage) { tag in
                let heute = Datumsformat.istHeute(tag.datum)
                VStack(spacing: 3) {
                    Text(Datumsformat.anzeige(tag.datum, "EE"))
                        .font(.system(size: 12.5, weight: .regular))
                        .foregroundStyle(heute ? Farben.heute : Farben.tinte2)
                    Text(Datumsformat.anzeige(tag.datum, "d"))
                        .font(.system(size: 18, weight: .bold))
                        .tracking(-0.36)
                        .foregroundStyle(heute ? Farben.heute : Farben.tinte)
                }
                .lineLimit(1)
                .frame(width: spaltenBreite - tagesLuecke)
                .padding(.trailing, tagesLuecke)
                .accessibilityElement(children: .combine)
            }
        }
        .padding(.top, 2)
        .padding(.bottom, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.leiste)
    }

    private var monatstext: String {
        var s = Datumsformat.anzeige(montagDatum, "LLL")
        if s.hasSuffix(".") { s.removeLast() }
        return s
    }

    // MARK: Zeitspalte und Linien (.slotrow)

    private func zeitspalte(_ zeiten: [Rasterzeit], start: Int, breite: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(zeiten.enumerated()), id: \.element.id) { eintrag in
                let z = eintrag.element
                let oben = y(z.beginn, start: start)
                let unten = y(z.ende, start: start)
                let naechste = eintrag.offset + 1 < zeiten.count ? zeiten[eintrag.offset + 1] : nil
                let pauseDanach = naechste == nil || (naechste?.beginn ?? 0) > z.ende

                linie(breite: breite)
                    .offset(x: rinnenBreite, y: oben)
                Text(Rasterzeit.uhrzeit(z.beginn))
                    .font(.system(size: 12, weight: .medium).monospacedDigit())
                    .tracking(0.12)
                    .foregroundStyle(Farben.rinne)
                    .fixedSize()
                    .offset(x: 8, y: oben + 2)
                Text("\(z.nr).")
                    .font(.system(size: 16, weight: .regular))
                    .foregroundStyle(Farben.tinte2)
                    .fixedSize()
                    .offset(x: 14, y: oben + 21)
                if pauseDanach {
                    linie(breite: breite)
                        .offset(x: rinnenBreite, y: unten)
                    Text(Rasterzeit.uhrzeit(z.ende))
                        .font(.system(size: 12, weight: .medium).monospacedDigit())
                        .tracking(0.12)
                        .foregroundStyle(Farben.rinne)
                        .fixedSize()
                        .frame(height: 15, alignment: .bottom)
                        .offset(x: 8, y: unten - 3 - 15)
                }
            }
        }
        .accessibilityHidden(true)
    }

    private func linie(breite: CGFloat) -> some View {
        Rectangle()
            .fill(Farben.linie)
            .frame(width: max(breite - rinnenBreite, 0), height: 1)
    }

    // MARK: Tagesspalte

    private func tagesspalte(_ tag: Tageskopf, zeiten: [Rasterzeit], start: Int, ende: Int, breite: CGFloat) -> some View {
        let karten = plan.karten.filter { $0.datum == tag.datum }
        let platziert = Rasteranordnung.anordnen(karten)
        let jetzt = Date()
        return ZStack(alignment: .topLeading) {
            if tag.frei {
                ferienfeld(tag)
                    .frame(width: breite, height: max(y(ende, start: start) - y(start, start: start) - 2, 20))
                    .offset(y: y(start, start: start) + 1)
            } else {
                // Veranstaltungen als durchgehendes Band hinter den Stunden
                ForEach(tag.ereignisse) { ereignis in
                    let von = ereignis.ganztaegig ? start : (zeiten.first { $0.nr == ereignis.von }?.beginn ?? start)
                    let bis = ereignis.ganztaegig ? ende : (zeiten.first { $0.nr == max(ereignis.bis, ereignis.von) }?.ende ?? ende)
                    if bis > von {
                        veranstaltungsband(ereignis)
                            .frame(width: breite, height: CGFloat(bis - von) * proMinute)
                            .offset(y: y(von, start: start))
                    }
                }
                ForEach(platziert) { p in
                    let lage = zeitraum(p.karte, zeiten: zeiten)
                    let spurBreite = breite / CGFloat(p.spuren)
                    let oben = y(lage.beginn, start: start) + 1
                    let hoehe = max(CGFloat(lage.ende - lage.beginn) * proMinute - 2, 16)
                    Button {
                        antippen(p.karte)
                    } label: {
                        StundenKarteView(karte: p.karte, typ: typ, vergangen: istVergangen(p.karte, jetzt: jetzt))
                    }
                    .buttonStyle(KartenKnopfStil())
                    .frame(width: max(spurBreite - (p.spuren > 1 ? 2 : 0), 10), height: hoehe)
                    .offset(x: CGFloat(p.spur) * spurBreite, y: oben)
                }
            }
        }
        .frame(width: breite, alignment: .topLeading)
    }

    /// Beginn und Ende einer Karte in Minuten (eigene Uhrzeit, sonst aus dem Raster).
    private func zeitraum(_ karte: Karte, zeiten: [Rasterzeit]) -> (beginn: Int, ende: Int) {
        let vonZeit = zeiten.first { $0.nr == karte.von }
        let bisZeit = zeiten.first { $0.nr == karte.bis }
        let beginn = Rasterzeit.minuten(karte.beginn) ?? vonZeit?.beginn ?? (zeiten.first?.beginn ?? 480)
        var ende = Rasterzeit.minuten(karte.ende) ?? bisZeit?.ende ?? (beginn + 45)
        if ende <= beginn { ende = beginn + 45 }
        return (beginn, ende)
    }

    private func istVergangen(_ karte: Karte, jetzt: Date) -> Bool {
        let endeText = karte.ende.isEmpty ? "23:59" : karte.ende
        guard let ende = Datumsformat.datum("\(karte.datum) \(endeText)") else { return false }
        return ende < jetzt
    }

    private func veranstaltungsband(_ ereignis: Ereignis) -> some View {
        Rectangle()
            .fill(Farben.veranstaltungBand)
            .overlay(alignment: .top) {
                Text(ereignis.titel.isEmpty ? ereignis.art : ereignis.titel)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Farben.ferienTinte)
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .padding(4)
            }
            .accessibilityLabel(ereignis.titel.isEmpty ? ereignis.art : ereignis.titel)
    }

    /// Ferien und Feiertage: blaues Feld über den ganzen Tag (.card--holiday).
    private func ferienfeld(_ tag: Tageskopf) -> some View {
        RoundedRectangle(cornerRadius: 6, style: .continuous)
            .fill(Farben.ferienHintergrund)
            .overlay {
                VStack(spacing: 8) {
                    Image(systemName: "sun.max")
                        .font(.system(size: 16, weight: .semibold))
                    Text(tag.freiGrund ?? "frei")
                        .font(.system(size: 13, weight: .bold))
                        .multilineTextAlignment(.center)
                        .lineLimit(4)
                        .minimumScaleFactor(0.7)
                    if let von = tag.freiVon, let bis = tag.freiBis, !von.isEmpty, !bis.isEmpty {
                        Text("\(Datumsformat.anzeige(von, "d.M.")) – \(Datumsformat.anzeige(bis, "d.M."))")
                            .font(.system(size: 11.5))
                            .lineLimit(1)
                            .minimumScaleFactor(0.7)
                    }
                }
                .foregroundStyle(Farben.ferienTinte)
                .padding(.horizontal, 3)
            }
    }

    // MARK: Jetzt-Linie

    private func jetztLinie(start: Int, ende: Int, spaltenBreite: CGFloat, breite: CGFloat) -> some View {
        TimelineView(.periodic(from: .now, by: 60)) { kontext in
            let minute = Datumsformat.kalender.component(.hour, from: kontext.date) * 60
                + Datumsformat.kalender.component(.minute, from: kontext.date)
            let heuteIndex = tage.firstIndex { $0.datum == Datumsformat.iso(kontext.date) }
            if let heuteIndex, minute >= start, minute <= ende {
                let hoehe = y(minute, start: start)
                ZStack(alignment: .topLeading) {
                    Rectangle()
                        .fill(Farben.jetztLinie)
                        .frame(width: max(breite - rinnenBreite, 0), height: 1.5)
                        .offset(x: rinnenBreite, y: hoehe - 0.75)
                    Capsule()
                        .fill(Farben.jetztLinie)
                        .frame(width: spaltenBreite - tagesLuecke, height: 3)
                        .offset(x: rinnenBreite + CGFloat(heuteIndex) * spaltenBreite, y: hoehe - 1.5)
                }
                .allowsHitTesting(false)
            }
        }
    }
}

/// Karte gibt beim Antippen leicht nach.
private struct KartenKnopfStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
    }
}

/// Karte mit Spur (nebeneinander liegende Stunden teilen sich die Spaltenbreite).
struct PlatzierteKarte: Identifiable {
    let karte: Karte
    let spur: Int
    let spuren: Int
    var id: UUID { karte.id }
}

enum Rasteranordnung {
    /// Überlappende Karten eines Tages auf Spuren verteilen.
    static func anordnen(_ karten: [Karte]) -> [PlatzierteKarte] {
        let sortiert = karten.sorted { ($0.von, $0.bis) < ($1.von, $1.bis) }
        var ergebnis: [PlatzierteKarte] = []
        var gruppe: [(karte: Karte, spur: Int)] = []
        var spurEnden: [Int] = []
        var gruppenEnde = Int.min

        func abschliessen() {
            let anzahl = max(spurEnden.count, 1)
            for eintrag in gruppe {
                ergebnis.append(PlatzierteKarte(karte: eintrag.karte, spur: eintrag.spur, spuren: anzahl))
            }
            gruppe = []
            spurEnden = []
        }

        for k in sortiert {
            if !gruppe.isEmpty && k.von > gruppenEnde { abschliessen() }
            if let frei = spurEnden.firstIndex(where: { $0 < k.von }) {
                spurEnden[frei] = k.bis
                gruppe.append((karte: k, spur: frei))
            } else {
                spurEnden.append(k.bis)
                gruppe.append((karte: k, spur: spurEnden.count - 1))
            }
            gruppenEnde = max(gruppenEnde, k.bis)
        }
        abschliessen()
        return ergebnis
    }
}
