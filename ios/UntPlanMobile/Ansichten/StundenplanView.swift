import SwiftUI

/// Stundenplan als Wochenraster nach Uhrzeit: oben Konto-Knopf, Planname als Menü, (i) und ⋮,
/// darunter KW/Monat und Tage, links die Zeitspalte. Wischen blättert wie Seiten zur nächsten bzw.
/// vorherigen Woche (die Ansicht folgt dem Finger, gleitet mit Feder-Animation weiter oder zurück).
struct StundenplanView: View {
    @EnvironmentObject var zustand: AppZustand
    @EnvironmentObject var favoriten: Favoriten
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var datum = Date()
    @State private var auswahl: PlanAuswahl? = nil
    /// Geladene Wochen (Schlüssel: Planart|Element|Montag)
    @State private var plaene: [String: Wochenplan] = [:]
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil
    @State private var zeigeAuswahl = false
    @State private var zeigeInfo = false

    // Blättern
    @State private var verschiebung: CGFloat = 0
    @State private var seitenBreite: CGFloat = 0
    @State private var richtung: Wischrichtung? = nil
    @State private var blaettert = false
    /// Ziel eines Sprungs über mehrere Wochen („Heute“): ersetzt während der Animation die Nachbarseite
    @State private var sprungZiel: Date? = nil
    @GestureState private var zieht = false

    private enum Wischrichtung {
        case waagrecht
        case senkrecht
    }

    private let feder = Animation.spring(response: 0.36, dampingFraction: 0.88)

    private var istBreit: Bool { groessenklasse == .regular }

    private var montag: Date { Datumsformat.montag(datum) }

    private var typ: String {
        if let auswahl { return auswahl.typ }
        let t = zustand.benutzer?.typ ?? ""
        return t.isEmpty ? "klasse" : t
    }

    private var element: String {
        auswahl?.element ?? zustand.benutzer?.element ?? ""
    }

    private func schluessel(_ montag: Date) -> String {
        "\(typ)|\(element)|\(Datumsformat.iso(montag))"
    }

    private var ladeSchluessel: String { schluessel(montag) }

    private var aktuellerPlan: Wochenplan? { plaene[schluessel(montag)] }

    /// Montage der drei Seiten: vorherige, aktuelle, nächste Woche (bzw. Sprungziel).
    private var seiten: [Date] {
        var links = Datumsformat.tage(-7, nach: montag)
        var rechts = Datumsformat.tage(7, nach: montag)
        if let sprungZiel {
            let ziel = Datumsformat.montag(sprungZiel)
            if ziel < montag { links = ziel } else if ziel > montag { rechts = ziel }
        }
        return [links, montag, rechts]
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                if istBreit {
                    plantitel
                        .padding(.top, 2)
                        .padding(.bottom, 6)
                }
                inhalt
            }
            .background(Farben.leiste.ignoresSafeArea())
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.hidden, for: .navigationBar)
            .toolbar { leiste }
            .sheet(item: $gewaehlteKarte) { karte in
                StundendetailView(karte: karte, typ: typ)
                    .presentationDetents([.medium, .large])
            }
            .sheet(isPresented: $zeigeAuswahl) {
                PlanAuswahlView(auswahl: $auswahl)
                    .environmentObject(zustand)
                    .environmentObject(favoriten)
            }
            .sheet(isPresented: $zeigeInfo) {
                PlanInfoView(titel: titel, plan: aktuellerPlan)
                    .presentationDetents([.medium, .large])
            }
            .task(id: ladeSchluessel) { await laden() }
            .onAppear { planAnfrageUebernehmen() }
            .onChange(of: zustand.planAnfrage) { _, _ in planAnfrageUebernehmen() }
        }
    }

    /// „Im Stundenplan anzeigen“ aus den Stundendetails.
    private func planAnfrageUebernehmen() {
        guard let neu = zustand.planAnfrage else { return }
        zustand.planAnfrage = nil
        gewaehlteKarte = nil
        if neu.typ == "lehrer" && !zustand.darfLehrerplaene { return }
        auswahl = neu
    }

    private var titel: String {
        if let auswahl { return auswahl.anzeige }
        if !element.isEmpty { return element }
        if let name = aktuellerPlan?.name, !name.isEmpty { return name }
        return "Mein Stundenplan"
    }

    // MARK: - Leiste: Konto links, Planname (iPhone) in der Mitte, (i) und ⋮ rechts

    @ToolbarContentBuilder
    private var leiste: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            KontoKnopf()
        }
        if !istBreit {
            ToolbarItem(placement: .principal) {
                plantitel
            }
        }
        ToolbarItemGroup(placement: .topBarTrailing) {
            Button {
                zeigeInfo = true
            } label: {
                Glasknopf {
                    Image(systemName: "info.circle")
                        .font(.system(size: 18, weight: .regular))
                }
            }
            .accessibilityLabel("Informationen zum Plan")
            menue
        }
    }

    /// Planname mit Pfeil als Menü (Mein Stundenplan, Favoriten, Auswahl).
    private var plantitel: some View {
        Menu {
            Button {
                auswahl = nil
            } label: {
                Label("Mein Stundenplan", systemImage: auswahl == nil ? "checkmark" : "calendar")
            }
            let favs = favoriten.liste.filter { $0.typ != "lehrer" || zustand.darfLehrerplaene }
            if !favs.isEmpty {
                Section("Favoriten") {
                    ForEach(favs) { fav in
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
            if let auswahl {
                Button {
                    favoriten.umschalten(auswahl)
                } label: {
                    Label(favoriten.enthaelt(auswahl) ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen",
                          systemImage: favoriten.enthaelt(auswahl) ? "star.slash" : "star")
                }
            }
        } label: {
            HStack(spacing: 4) {
                Text(titel)
                    .font(.system(size: istBreit ? 15 : 17, weight: .medium))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Image(systemName: "chevron.down")
                    .font(.system(size: istBreit ? 10 : 12, weight: .semibold))
            }
            .foregroundStyle(Farben.titelGrau)
            .padding(.vertical, 4)
            .padding(.horizontal, 8)
            .contentShape(Rectangle())
        }
        .accessibilityLabel("Stundenplan auswählen, aktuell \(titel)")
    }

    private var menue: some View {
        Menu {
            Button {
                heute()
            } label: {
                Label("Heute", systemImage: "calendar.circle")
            }
            .disabled(Datumsformat.iso(montag) == Datumsformat.iso(Datumsformat.montag(Date())))
            Button {
                blaettern(-1)
            } label: {
                Label("Vorherige Woche", systemImage: "chevron.left")
            }
            Button {
                blaettern(1)
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
            Glasknopf {
                Image(systemName: "ellipsis")
                    .font(.system(size: 17, weight: .semibold))
                    .rotationEffect(.degrees(90))
            }
        }
        .accessibilityLabel("Menü")
    }

    // MARK: - Inhalt

    @ViewBuilder
    private var inhalt: some View {
        if let fehler, aktuellerPlan == nil {
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
                Text("Ihrem Konto ist kein Plan zugeordnet. Wählen Sie oben eine Klasse oder einen Raum.")
            } actions: {
                Button("Stundenplan auswählen") { zeigeAuswahl = true }
                    .buttonStyle(.borderedProminent)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Farben.rasterOben)
        } else {
            blaetterflaeche
        }
    }

    /// Drei Wochen nebeneinander; sichtbar ist die mittlere. Die Ansicht folgt dem Finger.
    private var blaetterflaeche: some View {
        GeometryReader { geo in
            let w = geo.size.width
            HStack(spacing: 0) {
                ForEach(seiten, id: \.self) { m in
                    seite(m)
                        .frame(width: w)
                }
            }
            .frame(width: w * 3, height: geo.size.height, alignment: .leading)
            .offset(x: -w + verschiebung)
            .onAppear { seitenBreite = w }
            .onChange(of: w) { _, neu in seitenBreite = neu }
        }
        .clipped()
        .contentShape(Rectangle())
        .simultaneousGesture(wischen)
        .onChange(of: zieht) { _, neu in
            // Geste abgebrochen (z. B. vom senkrechten Scrollen übernommen): zurückfedern
            guard !neu else { return }
            richtung = nil
            if !blaettert && verschiebung != 0 {
                withAnimation(feder) { verschiebung = 0 }
            }
        }
    }

    @ViewBuilder
    private func seite(_ m: Date) -> some View {
        if let p = plaene[schluessel(m)] {
            WochenrasterView(plan: p, typ: p.typ.isEmpty ? typ : p.typ, heuteAntippen: { heute() }) { gewaehlteKarte = $0 }
        } else {
            Platzhalterwoche(montag: m)
        }
    }

    private var wischen: some Gesture {
        DragGesture(minimumDistance: 12)
            .updating($zieht) { _, status, _ in status = true }
            .onChanged { wert in
                guard !blaettert else { return }
                if richtung == nil {
                    let dx = abs(wert.translation.width)
                    let dy = abs(wert.translation.height)
                    guard dx > 8 || dy > 8 else { return }
                    richtung = dx > dy * 1.2 ? .waagrecht : .senkrecht
                }
                if richtung == .waagrecht {
                    verschiebung = wert.translation.width
                }
            }
            .onEnded { wert in
                let war = richtung
                richtung = nil
                guard war == .waagrecht, !blaettert else { return }
                let w = max(seitenBreite, 1)
                let dx = wert.translation.width
                let voraus = wert.predictedEndTranslation.width
                if dx < -w * 0.25 || voraus < -w * 0.55 {
                    blaettern(1)
                } else if dx > w * 0.25 || voraus > w * 0.55 {
                    blaettern(-1)
                } else {
                    withAnimation(feder) { verschiebung = 0 }
                }
            }
    }

    /// Eine Woche vor (1) bzw. zurück (-1) oder zu einem Ziel; gleitet mit Feder-Animation zur neuen Seite.
    private func blaettern(_ schritt: Int, ziel: Date? = nil) {
        guard !blaettert, schritt != 0 else { return }
        let neuesDatum = ziel ?? Datumsformat.tage(7 * schritt, nach: montag)
        let w = seitenBreite
        guard w > 0 else {
            datum = neuesDatum
            return
        }
        blaettert = true
        sprungZiel = ziel
        withAnimation(feder) {
            verschiebung = schritt > 0 ? -w : w
        } completion: {
            var t = Transaction()
            t.disablesAnimations = true
            withTransaction(t) {
                datum = neuesDatum
                sprungZiel = nil
                verschiebung = 0
            }
            blaettert = false
        }
    }

    private func heute() {
        let heute = Date()
        let ziel = Datumsformat.montag(heute)
        guard Datumsformat.iso(ziel) != Datumsformat.iso(montag) else { return }
        blaettern(ziel > montag ? 1 : -1, ziel: heute)
    }

    private func laden() async {
        let t = typ
        let e = element
        guard !e.isEmpty else { return }
        let m = montag
        func k(_ d: Date) -> String { "\(t)|\(e)|\(Datumsformat.iso(d))" }
        laedt = true
        do {
            let neu = try await zustand.api.plan(typ: t, element: e, datum: m)
            plaene[k(m)] = neu
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        laedt = false
        // Nachbarwochen vorab laden, damit beim Wischen sofort etwas zu sehen ist
        for nachbar in [Datumsformat.tage(7, nach: m), Datumsformat.tage(-7, nach: m)] where plaene[k(nachbar)] == nil {
            if Task.isCancelled { return }
            if let p = try? await zustand.api.plan(typ: t, element: e, datum: nachbar) {
                plaene[k(nachbar)] = p
            }
        }
    }
}

/// Seite einer noch nicht geladenen Woche: Kopf mit KW und Tagen, darunter ein Ladekreis.
struct Platzhalterwoche: View {
    let montag: Date

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .bottom, spacing: 0) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("KW \(Datumsformat.kalenderwoche(montag))")
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.tinte2)
                    Text(Wochenkopf.monat(montag))
                        .font(.system(size: 18, weight: .bold))
                        .foregroundStyle(Farben.tinte)
                }
                .padding(.leading, 6)
                Spacer()
            }
            .lineLimit(1)
            .padding(.top, 2)
            .padding(.bottom, 8)
            .background(Farben.leiste)
            ZStack {
                LinearGradient(colors: [Farben.rasterOben, Farben.rasterUnten], startPoint: .top, endPoint: .bottom)
                ProgressView()
            }
        }
    }
}

/// Hilfen für den Wochenkopf.
enum Wochenkopf {
    /// Kurzer Monatsname ohne Punkt, z. B. „Okt“.
    static func monat(_ datum: Date) -> String {
        var s = Datumsformat.anzeige(datum, "LLL")
        if s.hasSuffix(".") { s.removeLast() }
        return s
    }
}

/// Info (i): Legende der Kartenfarben und Angaben zur Woche.
struct PlanInfoView: View {
    let titel: String
    let plan: Wochenplan?
    @Environment(\.dismiss) private var schliessen

    var body: some View {
        NavigationStack {
            List {
                if let plan {
                    Section("Plan") {
                        LabeledContent("Plan", value: plan.name.isEmpty ? titel : plan.name)
                        LabeledContent("Kalenderwoche", value: "\(Datumsformat.kalenderwoche(Datumsformat.datum(plan.montag) ?? Date()))")
                        if !plan.woche.isEmpty {
                            LabeledContent("Woche", value: plan.woche.count == 1 ? "\(plan.woche)-Woche" : plan.woche)
                        }
                    }
                }
                Section("Legende") {
                    legende(Farben.karte, rand: nil, "Unterricht wie geplant")
                    legende(Farben.aenderungHintergrund, rand: Farben.aenderungRand, "Vertretung bzw. Änderung (grün hinterlegt: geänderter Wert)")
                    legende(Farben.karte, rand: Farben.raumRand, "Raumänderung")
                    legende(Farben.entfallHintergrund, rand: Farben.entfallRand, "Entfall (durchgestrichen)")
                    legende(Farben.pruefungHintergrund, rand: Farben.pruefungRand, "Prüfung")
                    legende(Farben.ferienHintergrund, rand: nil, "Ferien, Feiertag oder unterrichtsfrei")
                    HStack(spacing: 12) {
                        Infozeichen()
                            .frame(width: 34)
                        Text("Hinweis bzw. Notiz zur Stunde")
                    }
                    HStack(spacing: 12) {
                        Rectangle()
                            .fill(Farben.jetztLinie)
                            .frame(width: 34, height: 2)
                        Text("Aktuelle Uhrzeit")
                    }
                }
            }
            .navigationTitle(titel)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { schliessen() }
                }
            }
        }
    }

    private func legende(_ farbe: Color, rand: Color?, _ text: String) -> some View {
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 3, style: .continuous)
                .fill(farbe)
                .overlay {
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .strokeBorder(rand ?? Farben.linie, lineWidth: rand == nil ? 0.5 : 1.5)
                }
                .frame(width: 34, height: 22)
            Text(text)
        }
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
    /// Antippen von „KW / Monat“ springt zur aktuellen Woche.
    var heuteAntippen: (() -> Void)? = nil
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
            .contentShape(Rectangle())
            .onTapGesture { heuteAntippen?() }
            .accessibilityAddTraits(.isButton)
            .accessibilityHint("Zur aktuellen Woche")
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
        Wochenkopf.monat(montagDatum)
    }

    // MARK: Zeitspalte und Linien (.slotrow)

    private func zeitspalte(_ zeiten: [Rasterzeit], start: Int, breite: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(zeiten.enumerated()), id: \.element.id) { eintrag in
                let z = eintrag.element
                let oben = y(z.beginn, start: start)
                let unten = y(z.ende, start: start)
                let naechste: Rasterzeit? = eintrag.offset + 1 < zeiten.count ? zeiten[eintrag.offset + 1] : nil
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
                    let von = ereignis.ganztaegig ? start : (zeiten.first(where: { $0.nr == ereignis.von })?.beginn ?? start)
                    let bis = ereignis.ganztaegig ? ende : (zeiten.first(where: { $0.nr == max(ereignis.bis, ereignis.von) })?.ende ?? ende)
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
        let vonZeit = zeiten.first(where: { $0.nr == karte.von })
        let bisZeit = zeiten.first(where: { $0.nr == karte.bis })
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

    /// Art des freien Tages als Überschrift („Ferien“, „Feiertag“ …).
    private func freiTitel(_ tag: Tageskopf) -> String {
        switch tag.freiArt {
        case "ferien": return "Ferien"
        case "feiertag": return "Feiertag"
        case "wochenende": return "Wochenende"
        default: return "Unterrichtsfrei"
        }
    }

    /// Ferien und Feiertage: hellblaue Spalte über den ganzen Tag mit Schloss (Vorlage: „Ferien / pädagogischer Tag“).
    private func ferienfeld(_ tag: Tageskopf) -> some View {
        Rectangle()
            .fill(Farben.ferienHintergrund)
            .overlay {
                VStack(spacing: 2) {
                    Image(systemName: "lock.fill")
                        .font(.system(size: 10, weight: .semibold))
                        .padding(.bottom, 2)
                    Text(freiTitel(tag))
                        .font(.system(size: 12, weight: .semibold))
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                    if let grund = tag.freiGrund, !grund.isEmpty, grund.lowercased() != freiTitel(tag).lowercased() {
                        Text(grund)
                            .font(.system(size: 12))
                            .multilineTextAlignment(.center)
                            .lineLimit(4)
                            .minimumScaleFactor(0.7)
                    }
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
            let heuteIndex = tage.firstIndex(where: { $0.datum == Datumsformat.iso(kontext.date) })
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
