import SwiftUI

/// Ansicht des Stundenplans (⋮-Menü „Ansicht“): ein Tag, zwei bzw. drei Tage oder die ganze Woche.
enum Planansicht: String, CaseIterable, Identifiable {
    case tag
    case zweiTage
    case dreiTage
    case woche

    var id: String { rawValue }

    var titel: String {
        switch self {
        case .tag: return "Tag"
        case .zweiTage: return "2 Tage"
        case .dreiTage: return "3 Tage"
        case .woche: return "Woche"
        }
    }

    /// Spalten-Symbol (Vektorgrafik in Assets.xcassets)
    var bild: String {
        switch self {
        case .tag: return "AnsichtTag"
        case .zweiTage: return "AnsichtZweiTage"
        case .dreiTage: return "AnsichtDreiTage"
        case .woche: return "AnsichtWoche"
        }
    }

    /// Zahl der Tage je Seite (Woche: alle Tage der Woche)
    var tage: Int {
        switch self {
        case .tag: return 1
        case .zweiTage: return 2
        case .dreiTage: return 3
        case .woche: return 7
        }
    }
}

/// Stundenplan als Wochenraster nach Uhrzeit: oben Konto-Knopf, Planname als Menü, (i) und ⋮,
/// darunter KW/Monat und Tage, links die Zeitspalte. Wischen blättert wie Seiten zur nächsten bzw.
/// vorherigen Woche (bzw. Tagen); die Ansicht folgt dem Finger, gleitet mit Feder-Animation weiter oder zurück.
/// ⋮-Menü wie in der Vorlage: „Gehe zu Datum“ (Kalender als Popover), Ansicht Tag / 2 Tage / 3 Tage / Woche, Hilfe.
struct StundenplanView: View {
    @EnvironmentObject var zustand: AppZustand
    @EnvironmentObject var favoriten: Favoriten
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var datum = Date()
    @State private var auswahl: PlanAuswahl? = nil
    /// Geladene Wochen (Schlüssel: Planart|Element|Montag)
    @State private var plaene: [String: Wochenplan] = [:]
    /// Stand je geladener Woche (zählt bei jedem Neuladen hoch) – das Raster rechnet nur bei neuem Stand neu
    @State private var staende: [String: Int] = [:]
    @State private var laedt = false
    @State private var fehler: String? = nil
    /// Geöffnete Stundendetails (mit allen Stunden derselben Zeit als Reiter)
    @State private var detail: Detailauswahl? = nil
    @State private var gewaehlteAufsicht: Aufsicht? = nil
    @State private var zeigeAuswahl = false
    @State private var zeigeInfo = false
    @State private var zeigeHilfe = false
    @State private var zeigeDatum = false

    // Blättern
    @State private var verschiebung: CGFloat = 0
    @State private var seitenBreite: CGFloat = 0
    @State private var richtung: Wischrichtung? = nil
    @State private var blaettert = false
    /// Ziel eines Sprungs über mehrere Seiten („Heute“, „Gehe zu Datum“): ersetzt während der Animation die Nachbarseite
    @State private var sprungZiel: Date? = nil
    @GestureState private var zieht = false
    /// Sperrt das Öffnen von Stunden während und kurz nach einer Wisch- bzw. Scrollbewegung (nicht beobachtet)
    @State private var tippsperre = Tippsperre()
    /// Gewählte Ansicht (bleibt gespeichert)
    @AppStorage("planansicht") private var ansicht: Planansicht = .woche

    private enum Wischrichtung {
        case waagrecht
        case senkrecht
    }

    private let feder = Animation.spring(response: 0.36, dampingFraction: 0.88)

    private var istBreit: Bool { groessenklasse == .regular }

    /// Tagesweise Ansicht (Tag, 2 Tage, 3 Tage) statt der Woche.
    private var tageModus: Bool { ansicht != .woche }

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

    /// Erster angezeigter Tag in der tageweisen Ansicht (Wochenende nur, wenn der Plan es enthält).
    private var tag: Date { ersterSchultag(Datumsformat.kalender.startOfDay(for: datum), richtung: 1) }

    /// Mitte der aktuellen Seite: Montag (Wochenansicht) bzw. erster Tag (tageweise Ansicht).
    private var mitte: Date { tageModus ? tag : montag }

    /// Seitenanfang für ein Datum (Montag bzw. Schultag).
    private func seitenanfang(_ d: Date) -> Date {
        tageModus ? ersterSchultag(Datumsformat.kalender.startOfDay(for: d), richtung: 1) : Datumsformat.montag(d)
    }

    /// Seiten: vorherige, aktuelle, nächste Woche bzw. Tage (oder Sprungziel).
    private var seiten: [Date] {
        var links = nachbar(-1)
        var rechts = nachbar(1)
        if let sprungZiel {
            let ziel = seitenanfang(sprungZiel)
            if ziel < mitte { links = ziel } else if ziel > mitte { rechts = ziel }
        }
        return [links, mitte, rechts]
    }

    /// Ist der Tag ein Schultag der Anzeige? Wochenende nur, wenn der geladene Plan den Tag enthält.
    private func istAnzeigetag(_ d: Date) -> Bool {
        let wochentag = Datumsformat.kalender.component(.weekday, from: d) // 1 = Sonntag, 7 = Samstag
        guard wochentag == 1 || wochentag == 7 else { return true }
        let iso = Datumsformat.iso(d)
        return plaene[schluessel(Datumsformat.montag(d))]?.tage.contains(where: { $0.datum == iso }) == true
    }

    /// Der Tag selbst oder der nächste Schultag in der Richtung.
    private func ersterSchultag(_ d: Date, richtung: Int) -> Date {
        var t = d
        for _ in 0..<3 where !istAnzeigetag(t) {
            t = Datumsformat.tage(richtung, nach: t)
        }
        return t
    }

    /// Angezeigte Tage einer Seite ab ihrem ersten Tag (tageweise Ansicht).
    private func tageDerSeite(_ start: Date) -> [Date] {
        var liste: [Date] = []
        var t = ersterSchultag(start, richtung: 1)
        while liste.count < ansicht.tage {
            liste.append(t)
            t = ersterSchultag(Datumsformat.tage(1, nach: t), richtung: 1)
        }
        return liste
    }

    /// Nächste bzw. vorherige Woche oder Tage (Wochenende nur, wenn der Plan es enthält).
    private func nachbar(_ schritt: Int) -> Date {
        guard tageModus else { return Datumsformat.tage(7 * schritt, nach: montag) }
        if schritt > 0 {
            let letzter = tageDerSeite(tag).last ?? tag
            return ersterSchultag(Datumsformat.tage(1, nach: letzter), richtung: 1)
        }
        var t = tag
        for _ in 0..<ansicht.tage {
            t = ersterSchultag(Datumsformat.tage(-1, nach: t), richtung: -1)
        }
        return t
    }

    /// Ansicht wechseln (⋮-Menü bzw. Antippen eines Tages): Woche → Tage beginnt mit heute (in der Woche) bzw. Montag.
    private func ansichtWechseln(_ neu: Planansicht, tag neuerTag: Date? = nil) {
        guard !blaettert else { return }
        withAnimation(.easeInOut(duration: 0.25)) {
            if let neuerTag {
                datum = neuerTag
            } else if ansicht == .woche && neu != .woche {
                let heuteInWoche = Datumsformat.montag(Date()) == montag
                datum = heuteInWoche ? Date() : montag
            }
            ansicht = neu
        }
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
                    // Kräftiger oranger Ladebalken direkt unter der Kopfzeile, solange der Plan lädt
                    .overlay(alignment: .top) {
                        Ladebalken(aktiv: laedt)
                    }
            }
            .background(Farben.leiste.ignoresSafeArea())
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.hidden, for: .navigationBar)
            .toolbar { leiste }
            .sheet(item: $detail) { d in
                StundendetailView(karten: d.karten, start: d.start, typ: typ, element: element)
                    .environmentObject(zustand)
                    .detailBlatt(breit: istBreit)
            }
            .sheet(item: $gewaehlteAufsicht) { a in
                AufsichtDetailView(aufsicht: a)
                    .environmentObject(zustand)
                    .detailBlatt(breit: istBreit)
            }
            .sheet(isPresented: $zeigeAuswahl) {
                PlanAuswahlView(auswahl: $auswahl)
                    .environmentObject(zustand)
                    .environmentObject(favoriten)
                    .iPhoneBlatt()
            }
            .sheet(isPresented: $zeigeInfo) {
                PlanInfoView(titel: titel, plan: aktuellerPlan)
                    .iPhoneBlatt()
            }
            .sheet(isPresented: $zeigeHilfe) {
                PlanInfoView(titel: "Hilfe", plan: aktuellerPlan, hilfe: true)
                    .iPhoneBlatt()
            }
            .task(id: ladeSchluessel) { await laden() }
            .onAppear {
                planAnfrageUebernehmen()
                schnellaktionUebernehmen()
            }
            .onChange(of: zustand.planAnfrage) { _, _ in planAnfrageUebernehmen() }
            .onChange(of: zustand.schnellaktion) { _, _ in schnellaktionUebernehmen() }
            // Reiter „Stundenplan“ erneut angetippt: zurück zur heutigen Woche
            .onChange(of: zustand.stundenplanHeute) { _, _ in zurHeutigenWoche() }
            // Plan geändert (z. B. Raumbuchung in den Stundendetails): neu laden
            .onChange(of: zustand.planGeaendert) { _, _ in Task { await laden(neu: true) } }
            // Notiz in den Stundendetails gespeichert: Info-Zeichen sofort zeigen, dann den Plan neu laden
            .onChange(of: zustand.notizAenderung) { _, neu in
                guard let neu else { return }
                notizUebernehmen(neu)
                Task { await laden() }
            }
        }
    }

    /// Gespeicherte Notiz in alle geladenen Wochen übernehmen (Karten derselben Stunde); der neue Stand zeichnet das
    /// Raster mit Info-Zeichen sofort neu.
    private func notizUebernehmen(_ n: NotizAenderung) {
        for (schluessel, plan) in plaene {
            var neu = plan
            var geaendert = false
            for i in neu.karten.indices {
                let k = neu.karten[i]
                guard k.datum == n.datum, k.unterricht == n.unterricht, k.von <= n.bis, k.bis >= n.von else { continue }
                if let s = n.schueler { neu.karten[i].notizSchueler = s }
                if let l = n.lehrer { neu.karten[i].notizLehrer = l }
                geaendert = true
            }
            if geaendert {
                plaene[schluessel] = neu
                staende[schluessel, default: 0] += 1
            }
        }
    }

    /// Kurzbefehl vom App-Symbol: „Mein Stundenplan“ (aktuelle Woche) bzw. „Plan suchen“ (Planauswahl).
    private func schnellaktionUebernehmen() {
        guard let aktion = zustand.schnellaktion else { return }
        zustand.schnellaktion = nil
        detail = nil
        gewaehlteAufsicht = nil
        switch aktion {
        case .stundenplan:
            auswahl = nil
            ansicht = .woche
            datum = Date()
        case .suche:
            zeigeAuswahl = true
        default:
            break
        }
    }

    /// „Im Stundenplan anzeigen“ aus den Stundendetails.
    private func planAnfrageUebernehmen() {
        guard let neu = zustand.planAnfrage else { return }
        zustand.planAnfrage = nil
        detail = nil
        gewaehlteAufsicht = nil
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
        // (i) und ⋮ wie in der Vorlage nebeneinander (iPhone und iPad)
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

    /// Planname mit Pfeil: ein Tipp öffnet direkt die Planauswahl (Mein Stundenplan, Favoriten, Suche, Listen).
    private var plantitel: some View {
        Button {
            zeigeAuswahl = true
        } label: {
            HStack(spacing: 4) {
                Text(titel)
                    .font(.system(size: istBreit ? 15 : 17, weight: .medium))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Image(systemName: "chevron.down")
                    .font(.system(size: istBreit ? 10 : 12, weight: .medium))
            }
            .foregroundStyle(Farben.titelGrau)
            .padding(.vertical, 4)
            .padding(.horizontal, 8)
            .frame(maxWidth: istBreit ? 360 : 200)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Stundenplan auswählen, aktuell \(titel)")
    }

    /// ⋮-Menü wie in der Vorlage: „Gehe zu Datum“, Abschnitt „Ansicht“ (Tag, 2 Tage, 3 Tage, Woche mit Häkchen), Hilfe.
    private var menue: some View {
        Menu {
            Button {
                zeigeDatum = true
            } label: {
                Label("Gehe zu Datum", image: "GeheZuDatum")
            }
            Section("Ansicht") {
                Picker("Ansicht", selection: Binding(get: { ansicht }, set: { ansichtWechseln($0) })) {
                    ForEach(Planansicht.allCases) { a in
                        Label(a.titel, image: a.bild)
                            .tag(a)
                    }
                }
                .pickerStyle(.inline)
                .labelsHidden()
            }
            Section {
                if let auswahl {
                    Button {
                        favoriten.umschalten(auswahl)
                    } label: {
                        Label(favoriten.enthaelt(auswahl) ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen",
                              systemImage: favoriten.enthaelt(auswahl) ? "star.slash" : "star")
                    }
                }
                Button {
                    Task { await laden(neu: true) }
                } label: {
                    Label("Aktualisieren", systemImage: "arrow.clockwise")
                }
                Button {
                    zeigeHilfe = true
                } label: {
                    Label("Hilfe", systemImage: "questionmark.circle")
                }
            }
        } label: {
            Glasknopf {
                Image(systemName: "ellipsis")
                    .font(.system(size: 17, weight: .medium))
                    .rotationEffect(.degrees(90))
            }
        }
        .accessibilityLabel("Menü")
        .popover(isPresented: $zeigeDatum, arrowEdge: .top) {
            GeheZuDatumView(start: tageModus ? tag : datum) { gewaehlt in
                zeigeDatum = false
                springen(zu: gewaehlt)
            }
            .presentationCompactAdaptation(.popover)
        }
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
            // Das Raster läuft wie im Original bis zum unteren Rand hinter der Tab-Leiste weiter; damit auch die letzte
            // Stunde über die Leiste gescrollt werden kann, bekommt das Raster unten den verdeckten Bereich als Abstand.
            GeometryReader { aussen in
                blaetterflaeche(untenFrei: aussen.safeAreaInsets.bottom)
                    .ignoresSafeArea(.container, edges: .bottom)
            }
        }
    }

    /// Drei Wochen nebeneinander; sichtbar ist die mittlere. Die Ansicht folgt dem Finger.
    private func blaetterflaeche(untenFrei: CGFloat) -> some View {
        GeometryReader { geo in
            let w = geo.size.width
            HStack(spacing: 0) {
                ForEach(seiten, id: \.self) { m in
                    seite(m, untenFrei: untenFrei)
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
            if tippsperre.wischt { tippsperre.bewegungEnde(nachlauf: 0.15) }
            if !blaettert && verschiebung != 0 {
                withAnimation(feder) { verschiebung = 0 }
            }
        }
    }

    @ViewBuilder
    private func seite(_ m: Date, untenFrei: CGFloat) -> some View {
        if tageModus {
            let tage = tageDerSeite(m)
            let wochen = ohneDoppelte(tage.map { Datumsformat.montag($0) })
            let geladen = wochen.compactMap { plaene[schluessel($0)] }
            if geladen.count == wochen.count, let p = zusammengefuehrt(geladen) {
                raster(p, nurTage: tage.map { Datumsformat.iso($0) },
                       stand: wochen.reduce(0) { $0 + (staende[schluessel($1)] ?? 0) }, untenFrei: untenFrei)
            } else {
                Platzhalterwoche(montag: Datumsformat.montag(m))
            }
        } else if let p = plaene[schluessel(m)] {
            raster(p, nurTage: nil, stand: staende[schluessel(m)] ?? 0, untenFrei: untenFrei)
        } else {
            Platzhalterwoche(montag: m)
        }
    }

    /// Wochen zu einem Plan zusammenführen (Ansicht über das Wochenende hinweg, z. B. Fr. und Mo.).
    private func zusammengefuehrt(_ wochen: [Wochenplan]) -> Wochenplan? {
        guard var p = wochen.first else { return nil }
        for w in wochen.dropFirst() {
            p.tage += w.tage
            p.karten += w.karten
            p.aufsichten += w.aufsichten
        }
        return p
    }

    private func ohneDoppelte(_ liste: [Date]) -> [Date] {
        var ergebnis: [Date] = []
        for d in liste where !ergebnis.contains(d) { ergebnis.append(d) }
        return ergebnis
    }

    private func raster(_ p: Wochenplan, nurTage: [String]?, stand: Int, untenFrei: CGFloat) -> some View {
        WochenrasterView(plan: p, typ: p.typ.isEmpty ? typ : p.typ, element: element,
                         tippsperre: tippsperre,
                         nurTage: nurTage,
                         stand: stand,
                         untenFrei: untenFrei,
                         heuteAntippen: {
                             if tageModus { ansichtWechseln(.woche) } else { heute() }
                         },
                         tagAntippen: { iso in
                             if ansicht == .tag {
                                 ansichtWechseln(.woche)
                             } else if let d = Datumsformat.datum(iso) {
                                 ansichtWechseln(.tag, tag: d)
                             }
                         },
                         aufsichtAntippen: { gewaehlteAufsicht = $0 },
                         antippen: { karten, start in
                             guard !karten.isEmpty else { return }
                             detail = Detailauswahl(karten: karten, start: min(max(start, 0), karten.count - 1))
                         })
        // Beim Wischen ändert sich nur die Verschiebung: das Raster wird dann nicht neu berechnet
        .equatable()
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
                    // Ab jetzt ist es eine Wisch- bzw. Scrollbewegung: kein Antippen einer Stunde auslösen
                    tippsperre.wischt = true
                }
                if richtung == .waagrecht {
                    verschiebung = wert.translation.width
                }
            }
            .onEnded { wert in
                let war = richtung
                richtung = nil
                // Kurzes Nachlaufen (Senkrecht: Schwung des Scrollens, solange keine Scrollphase gemeldet wird)
                tippsperre.bewegungEnde(nachlauf: war == .senkrecht ? 0.35 : 0.15)
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
        let neuesDatum = ziel ?? nachbar(schritt)
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
        springen(zu: Date())
    }

    /// Zu einem Datum springen (Heute, „Gehe zu Datum“): gleitet zur Seite mit diesem Tag.
    private func springen(zu ziel: Date) {
        let anfang = seitenanfang(ziel)
        guard Datumsformat.iso(anfang) != Datumsformat.iso(mitte) else { return }
        blaettern(anfang > mitte ? 1 : -1, ziel: tageModus ? anfang : ziel)
    }

    /// Erneutes Antippen des Reiters „Stundenplan“: Details schließen, Wochenansicht der aktuellen Woche.
    private func zurHeutigenWoche() {
        detail = nil
        gewaehlteAufsicht = nil
        if tageModus {
            withAnimation(.easeInOut(duration: 0.25)) {
                ansicht = .woche
                datum = Date()
            }
        } else {
            heute()
        }
    }

    private func laden(neu: Bool = false) async {
        let t = typ
        let e = element
        guard !e.isEmpty else { return }
        let m = montag
        func k(_ d: Date) -> String { "\(t)|\(e)|\(Datumsformat.iso(d))" }
        laedt = true
        do {
            let neuerPlan = try await zustand.api.plan(typ: t, element: e, datum: m)
            plaene[k(m)] = neuerPlan
            staende[k(m), default: 0] += 1
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        laedt = false
        // Nachbarwochen vorab und gleichzeitig laden, damit beim Wischen (und in der Ansicht über zwei Wochen) sofort
        // etwas zu sehen ist
        if Task.isCancelled { return }
        let api = zustand.api
        let naechste = Datumsformat.tage(7, nach: m)
        let vorige = Datumsformat.tage(-7, nach: m)
        let holeNaechste = neu || plaene[k(naechste)] == nil
        let holeVorige = neu || plaene[k(vorige)] == nil
        @Sendable func hole(_ datum: Date, _ noetig: Bool) async -> Wochenplan? {
            guard noetig else { return nil }
            return try? await api.plan(typ: t, element: e, datum: datum)
        }
        async let planNaechste = hole(naechste, holeNaechste)
        async let planVorige = hole(vorige, holeVorige)
        let (pn, pv) = await (planNaechste, planVorige)
        if Task.isCancelled { return }
        for (datum, plan) in [(naechste, pn), (vorige, pv)] {
            guard let plan else { continue }
            plaene[k(datum)] = plan
            staende[k(datum), default: 0] += 1
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
                        .font(.system(size: 18, weight: .medium))
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

/// Info (i) bzw. Hilfe (⋮-Menü): Bedienung (nur Hilfe), Legende der Kartenfarben und Angaben zur Woche.
struct PlanInfoView: View {
    let titel: String
    let plan: Wochenplan?
    var hilfe: Bool = false
    @Environment(\.dismiss) private var schliessen

    var body: some View {
        NavigationStack {
            List {
                if hilfe {
                    Section("Bedienung") {
                        hilfezeile("hand.draw", "Waagrecht wischen: nächste bzw. vorherige Woche (in der Tagesansicht die nächsten Tage).")
                        hilfezeile("hand.tap", "Stunde antippen: Details mit Lehrstoff, Schüler*innen, Hausaufgaben und Notizen.")
                        hilfezeile("calendar.day.timeline.left", "Tag im Kopf antippen: Tagesansicht; erneut antippen: zurück zur Woche.")
                        hilfezeile("arrow.uturn.backward", "„KW“ links oben bzw. den Reiter „Stundenplan“ erneut antippen: zurück zur heutigen Woche.")
                        hilfezeile("rectangle.split.3x1", "⋮ → Ansicht: Tag, 2 Tage, 3 Tage oder Woche.")
                        hilfezeile("calendar", "⋮ → Gehe zu Datum: Tag im Kalender wählen und „Auswählen“ tippen; „Heute“ springt zum heutigen Tag.")
                        hilfezeile("chevron.down", "Planname oben antippen: anderen Stundenplan (Klasse, Lehrkraft, Raum) wählen.")
                    }
                    Section("Schüler*innen (Lehrkräfte)") {
                        hilfezeile("checkmark.circle", "Von links nach rechts wischen: anwesend.")
                        hilfezeile("clock", "Von rechts nach links wischen: abwesend; „War in Vorstunde abwesend!“ weist auf Fehlzeiten der Vorstunde hin.")
                        hilfezeile("book.closed", "Buch: neuer Klassenbucheintrag; Uhr: Abwesenheiten der Person bearbeiten.")
                    }
                }
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
                    legende(Farben.entfallHintergrund, rand: Farben.entfallRand, "Entfall (durchgestrichen)")
                    legende(Farben.pruefungHintergrund, rand: Farben.pruefungRand, "Klausur bzw. Prüfung")
                    legende(Farben.aenderungHintergrund, rand: nil, "Veranstaltung (über den ganzen Zeitraum)")
                    HStack(spacing: 12) {
                        Eckklammern(laenge: 8, radius: 3)
                            .stroke(Farben.eckklammer, lineWidth: 1.4)
                            .frame(width: 34, height: 22)
                        Text("Stunde läuft ohne die Lehrkraft (abgegeben bzw. abwesend, Kürzel durchgestrichen)")
                    }
                    HStack(spacing: 12) {
                        RoundedRectangle(cornerRadius: 4, style: .continuous)
                            .strokeBorder(Farben.bereitschaftRand, style: StrokeStyle(lineWidth: 1.5, dash: [4, 2]))
                            .frame(width: 34, height: 22)
                        Text("Bereitschaft")
                    }
                    HStack(spacing: 12) {
                        HStack(spacing: 0) {
                            RoundedRectangle(cornerRadius: 1.5, style: .continuous)
                                .fill(Farben.aufsichtBalken)
                                .frame(width: 3, height: 10)
                                .padding(.leading, 3)
                            Spacer(minLength: 0)
                        }
                        .frame(width: 34, height: 14)
                        .background(Farben.karte, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
                        .overlay {
                            RoundedRectangle(cornerRadius: 3, style: .continuous)
                                .strokeBorder(Farben.linie, lineWidth: 0.5)
                        }
                        Text("Pausenaufsicht")
                    }
                    legende(Farben.ferienHintergrund, rand: nil, "Ferien, Feiertag oder unterrichtsfrei")
                    HStack(spacing: 12) {
                        Infozeichen()
                            .frame(width: 34)
                        Text("Hinweis bzw. Notiz zur Stunde")
                    }
                    HStack(spacing: 12) {
                        Hausaufgabenzeichen()
                            .frame(width: 34)
                        Text("Hausaufgabe fällig")
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

    private func hilfezeile(_ symbol: String, _ text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Image(systemName: symbol)
                .font(.system(size: 16, weight: .regular))
                .foregroundStyle(Farben.akzent)
                .frame(width: 34)
            Text(text)
                .font(.system(size: 15))
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

/// Auswahl für die Stundendetails: alle Stunden derselben Zeit (Reiter), geöffnet bei `start`.
struct Detailauswahl: Identifiable {
    let id = UUID()
    let karten: [Karte]
    let start: Int
}

/// Höhen im Raster: Abstand oben, Beginn des Rasters, Punkte je Minute.
/// Pausen mit Aufsicht werden bei Bedarf gedehnt, damit die Aufsicht vollständig in die Lücke passt.
struct Rastermass {
    /// Zusätzliche Höhe in einer Pause (beginn…ende in Minuten; bei beginn == ende ein Sprung an dieser Stelle).
    struct Dehnung {
        let beginn: Int
        let ende: Int
        let extra: CGFloat
    }

    let oben: CGFloat
    let start: Int
    let proMinute: CGFloat
    var dehnungen: [Dehnung] = []

    /// Oberkante bei dieser Minute (was hier beginnt).
    func y(_ minute: Int) -> CGFloat {
        position(minute, ende: false)
    }

    /// Unterkante bei dieser Minute (was hier endet) – unterscheidet sich nur an Sprüngen ohne Pausenlänge.
    func yEnde(_ minute: Int) -> CGFloat {
        position(minute, ende: true)
    }

    private func position(_ minute: Int, ende: Bool) -> CGFloat {
        var wert = oben + CGFloat(minute - start) * proMinute
        for d in dehnungen {
            if d.ende > d.beginn {
                if minute >= d.ende {
                    wert += d.extra
                } else if minute > d.beginn {
                    wert += d.extra * CGFloat(minute - d.beginn) / CGFloat(d.ende - d.beginn)
                }
            } else if minute > d.beginn || (minute == d.beginn && !ende) {
                wert += d.extra
            }
        }
        return wert
    }

    /// Dehnungen für die Pausen der Aufsichten: jede Lücke mindestens `mindestens` Punkte hoch.
    static func dehnungen(_ aufsichten: [Kachel], proMinute: CGFloat, mindestens: CGFloat) -> [Dehnung] {
        var extra: [Int: (ende: Int, extra: CGFloat)] = [:]
        for a in aufsichten {
            let luecke = CGFloat(max(0, a.ende - a.beginn)) * proMinute
            let noetig = mindestens - luecke
            guard noetig > 0.5 else { continue }
            if let alt = extra[a.beginn], alt.extra >= noetig { continue }
            extra[a.beginn] = (max(a.beginn, a.ende), noetig)
        }
        return extra.keys.sorted().map { Dehnung(beginn: $0, ende: extra[$0]?.ende ?? $0, extra: extra[$0]?.extra ?? 0) }
    }
}

/// Raster mit Tagesspalten; Höhe nach Uhrzeit (1,2704 pt je Minute wie in der PWA), Pausen als Lücken.
/// Kacheln, Spuren und Zählstreifen kommen aus `Kachelbau` (Logik der PWA).
struct WochenrasterView: View {
    let plan: Wochenplan
    let typ: String
    var element: String = ""
    /// Sperre für das Antippen während bzw. kurz nach Wischen und Scrollen
    var tippsperre: Tippsperre = Tippsperre()
    var laedt: Bool = false
    /// Tageweise Ansicht: nur diese Tage (yyyy-MM-dd), Spalten über die volle Breite (nil = ganze Woche).
    var nurTage: [String]? = nil
    /// Stand der geladenen Woche (für den Vergleich, ob neu gezeichnet werden muss)
    var stand: Int = 0
    /// Vom unteren Rand verdeckter Bereich (Tab-Leiste, Home-Balken): das Raster läuft darunter weiter
    var untenFrei: CGFloat = 0
    /// Antippen von „KW / Monat“ (Woche: zur aktuellen Woche, Tag: zurück zur Woche).
    var heuteAntippen: (() -> Void)? = nil
    /// Antippen eines Tageskopfs (Tagesansicht öffnen bzw. schließen).
    var tagAntippen: ((String) -> Void)? = nil
    /// Antippen einer Pausenaufsicht.
    var aufsichtAntippen: ((Aufsicht) -> Void)? = nil
    /// Antippen einer Stunde: alle Stunden derselben Zeit und die angetippte.
    let antippen: ([Karte], Int) -> Void

    @Environment(\.horizontalSizeClass) private var groessenklasse

    /// iPhone (schmale Breite): schmale Zeitspalte und kleinere Schrift, damit fünf Tage lesbar nebeneinander passen.
    private var kompakt: Bool { groessenklasse == .compact }

    // Maße aus app.css (iPhone: Zeitspalte 38 pt)
    private var rinnenBreite: CGFloat { kompakt ? 38 : 45 }
    private var tagesLuecke: CGFloat { kompakt ? 2 : 3 }
    /// Höhe je Minute – Woche und Tag gleich (Raster wie im Original).
    private let proMinute: CGFloat = 1.2704
    private let abstandUnten: CGFloat = 28

    private var tage: [Tageskopf] {
        guard let nurTage else { return plan.tage }
        return nurTage.compactMap { iso in plan.tage.first { $0.datum == iso } }
    }

    private var montagDatum: Date {
        Datumsformat.datum(plan.montag) ?? Datumsformat.datum(tage.first?.datum ?? "") ?? Date()
    }

    var body: some View {
        let zeiten = Rasterzeit.aus(plan)
        let start = zeiten.first?.beginn ?? 480
        let ende = zeiten.last?.ende ?? 960
        let jetzt = Date()
        // Kacheln je Tag aus dem Zwischenspeicher: gleiche Daten (Stand) liefern dieselben Kacheln mit denselben
        // Kennungen – SwiftUI behält die Kartenansichten, statt sie bei jedem Zeichnen neu aufzubauen
        let minute = Int(jetzt.timeIntervalSinceReferenceDate / 60)
        let basis = "\(typ)|\(element)|\(plan.montag)|\(stand)|\(nurTage?.joined(separator: ",") ?? "w")|\(minute)"
        let kacheln: [String: [Kachel]] = Dictionary(
            tage.map { tag in
                (tag.datum, Kachelspeicher.kacheln(basis + "|" + tag.datum) {
                    Kachelbau.tag(tag, plan: plan, raster: zeiten, typ: typ, element: element, jetzt: jetzt)
                })
            },
            uniquingKeysWith: { erste, _ in erste })
        let vorErster = kacheln.values.contains { liste in liste.contains { $0.vorErsterStunde } }
        let pausenAufsichten = kacheln.values.flatMap { $0 }.filter { $0.art == .aufsicht && $0.inPause && !$0.vorErsterStunde }
        let mass = Rastermass(oben: vorErster ? 34 : 15, start: start, proMinute: proMinute,
                              dehnungen: Rastermass.dehnungen(pausenAufsichten, proMinute: proMinute,
                                                              mindestens: nurTage == nil ? 24 : 30))
        GeometryReader { geo in
            let spalten = max(tage.count, 1)
            let rand: CGFloat = nurTage == nil ? 0 : 3
            let spaltenBreite = max(kompakt ? 34 : 40, (geo.size.width - rinnenBreite - rand) / CGFloat(spalten))
            let hoehe = mass.y(ende) + abstandUnten + untenFrei
            VStack(spacing: 0) {
                // Kopf (KW/Monat und Tage) steht fest über dem scrollenden Raster und wird nie gestaucht
                wochenkopf(spaltenBreite: spaltenBreite, kacheln: kacheln)
                    .fixedSize(horizontal: false, vertical: true)
                    .layoutPriority(1)
                    .zIndex(1)
                ScrollViewReader { leser in
                ScrollView(.vertical) {
                    ZStack(alignment: .topLeading) {
                        // Anker knapp über der 1. Stunde (bzw. über den Pillen vor der 1. Stunde)
                        VStack(spacing: 0) {
                            Color.clear.frame(height: vorErster ? 0 : max(0, mass.y(start) - 8))
                            Color.clear.frame(width: 1, height: 1).id(Self.ersteStundeAnker)
                            Spacer(minLength: 0)
                        }
                        .accessibilityHidden(true)
                        zeitspalte(zeiten, mass: mass, breite: geo.size.width)
                        ForEach(Array(tage.enumerated()), id: \.element.id) { eintrag in
                            tagesspalte(eintrag.element, kacheln: kacheln[eintrag.element.datum] ?? [], zeiten: zeiten,
                                        mass: mass, ende: ende, breite: spaltenBreite - tagesLuecke, hoehe: hoehe)
                                .offset(x: rinnenBreite + CGFloat(eintrag.offset) * spaltenBreite + tagesLuecke / 2)
                        }
                        jetztLinie(mass: mass, start: start, ende: ende, spaltenBreite: spaltenBreite, breite: geo.size.width)
                    }
                    .frame(width: geo.size.width, height: hoehe, alignment: .topLeading)
                    .opacity(laedt ? 0.55 : 1)
                    .animation(.easeOut(duration: 0.18), value: laedt)
                }
                .scrollIndicators(.hidden)
                .modifier(Scrollsperre(sperre: tippsperre))
                .background(
                    LinearGradient(colors: [Farben.rasterOben, Farben.rasterUnten], startPoint: .top, endPoint: .bottom)
                        .ignoresSafeArea(edges: .bottom)
                )
                .onAppear {
                    // Beim Öffnen so scrollen, dass die 1. Stunde vollständig sichtbar ist
                    DispatchQueue.main.async {
                        leser.scrollTo(Self.ersteStundeAnker, anchor: .top)
                    }
                }
                }
            }
        }
    }

    private static let ersteStundeAnker = "ersteStunde"

    // MARK: Kopf mit KW, Monat und Tagen (.weekhead)

    @ViewBuilder
    private func wochenkopf(spaltenBreite: CGFloat, kacheln: [String: [Kachel]]) -> some View {
        if let nurTage, nurTage.count == 1, let tag = tage.first {
            tageskopf(tag, iso: tag.datum, spaltenBreite: spaltenBreite)
        } else {
            HStack(alignment: .bottom, spacing: 0) {
                kwFeld
                ForEach(tage) { tag in
                    let heute = Datumsformat.istHeute(tag.datum)
                    VStack(spacing: kompakt ? 2 : 3) {
                        Text(Datumsformat.anzeige(tag.datum, "EE"))
                            .font(.system(size: kompakt ? 12 : 12.5, weight: .regular))
                            .foregroundStyle(heute ? Farben.heute : Farben.tinte2)
                        Text(Datumsformat.anzeige(tag.datum, "d"))
                            .font(.system(size: kompakt ? 17 : 18, weight: .medium))
                            .tracking(-0.36)
                            .foregroundStyle(heute ? Farben.heute : Farben.tinte)
                    }
                    .lineLimit(1)
                    .frame(width: spaltenBreite)
                    .contentShape(Rectangle())
                    .onTapGesture { tagAntippen?(tag.datum) }
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.isButton)
                    .accessibilityHint("Tagesansicht öffnen")
                }
            }
            .padding(.top, 2)
            .padding(.bottom, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Farben.leiste)
        }
    }

    private var kwFeld: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text("KW \(Datumsformat.kalenderwoche(montagDatum))")
                .font(.system(size: 12, weight: .regular))
                .foregroundStyle(Farben.tinte2)
            Text(Wochenkopf.monat(montagDatum))
                .font(.system(size: 18, weight: .medium))
                .tracking(-0.18)
                .foregroundStyle(Farben.tinte)
        }
        .lineLimit(1)
        .minimumScaleFactor(0.6)
        .padding(.leading, kompakt ? 4 : 6)
        .frame(width: rinnenBreite, alignment: .leading)
        .contentShape(Rectangle())
        .onTapGesture { heuteAntippen?() }
        .accessibilityAddTraits(.isButton)
        .accessibilityHint(nurTage == nil ? "Zur aktuellen Woche" : "Zurück zur Wochenansicht")
    }

    /// Kopf der Tagesansicht wie der Wochenkopf: links „KW / Monat“, darüber mittig in der Tagesspalte „Fr.“ und groß
    /// die Tageszahl (heute orange). Ein Tipp führt zurück zur Woche.
    private func tageskopf(_ tag: Tageskopf, iso: String, spaltenBreite: CGFloat) -> some View {
        let heute = Datumsformat.istHeute(iso)
        return HStack(alignment: .bottom, spacing: 0) {
            kwFeld
            VStack(spacing: kompakt ? 2 : 3) {
                Text(Datumsformat.anzeige(iso, "EE"))
                    .font(.system(size: kompakt ? 12 : 12.5, weight: .regular))
                    .foregroundStyle(heute ? Farben.heute : Farben.tinte2)
                Text(Datumsformat.anzeige(iso, "d"))
                    .font(.system(size: kompakt ? 17 : 18, weight: .medium))
                    .tracking(-0.36)
                    .foregroundStyle(heute ? Farben.heute : Farben.tinte)
            }
            .lineLimit(1)
            .frame(width: spaltenBreite)
            .contentShape(Rectangle())
            .onTapGesture { tagAntippen?(iso) }
            .accessibilityElement(children: .combine)
            .accessibilityLabel(Datumsformat.anzeige(iso, "EEEE, d. MMMM"))
            .accessibilityAddTraits(.isButton)
            .accessibilityHint("Zurück zur Wochenansicht")
        }
        .padding(.top, 2)
        .padding(.bottom, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.leiste)
    }

    // MARK: Zeitspalte und Linien (.slotrow)

    private func zeitspalte(_ zeiten: [Rasterzeit], mass: Rastermass, breite: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(zeiten.enumerated()), id: \.element.id) { eintrag in
                let z = eintrag.element
                let oben = mass.y(z.beginn)
                let unten = mass.yEnde(z.ende)
                let naechste: Rasterzeit? = eintrag.offset + 1 < zeiten.count ? zeiten[eintrag.offset + 1] : nil
                let pauseDanach = naechste == nil || (naechste?.beginn ?? 0) > z.ende

                linie(breite: breite)
                    .offset(x: rinnenBreite, y: oben)
                Text(Rasterzeit.uhrzeit(z.beginn))
                    .font(.system(size: kompakt ? 10.5 : 12, weight: .medium).monospacedDigit())
                    .tracking(kompakt ? 0 : 0.12)
                    .foregroundStyle(Farben.rinne)
                    .fixedSize()
                    .offset(x: kompakt ? 4 : 8, y: oben + 2)
                if unten - oben > 36 {
                    Text("\(z.nr).")
                        .font(.system(size: kompakt ? 14.5 : 16, weight: .regular))
                        .foregroundStyle(Farben.tinte2)
                        .fixedSize()
                        .offset(x: kompakt ? 11 : 14, y: oben + (unten - oben) / 2 - 10)
                }
                if pauseDanach {
                    linie(breite: breite)
                        .offset(x: rinnenBreite, y: unten)
                    Text(Rasterzeit.uhrzeit(z.ende))
                        .font(.system(size: kompakt ? 10.5 : 12, weight: .medium).monospacedDigit())
                        .tracking(kompakt ? 0 : 0.12)
                        .foregroundStyle(Farben.rinne)
                        .fixedSize()
                        .frame(height: 15, alignment: .bottom)
                        .offset(x: kompakt ? 4 : 8, y: unten - 3 - 15)
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

    private func tagesspalte(_ tag: Tageskopf, kacheln: [Kachel], zeiten: [Rasterzeit], mass: Rastermass,
                             ende: Int, breite: CGFloat, hoehe: CGFloat) -> some View {
        let belegung = Kachelbau.anordnen(kacheln, spaltenBreite: breite, tagesansicht: nurTage?.count == 1)
        let vorErste = belegung.lagen.filter { $0.kachel.vorErsterStunde }.map(\.kachel.id)
        return ZStack(alignment: .topLeading) {
            if tag.frei {
                ferienfeld(tag)
                    // Wie im Original bis zum unteren Rand (auch hinter der Tab-Leiste)
                    .frame(width: breite, height: max(hoehe - mass.y(mass.start) - 1, 20))
                    .offset(y: mass.y(mass.start) + 1)
            } else {
                ForEach(belegung.lagen) { lage in
                    let r = rechteck(lage, mass: mass, breite: breite, vorErste: vorErste)
                    kachelKnopf(lage.kachel, alle: kacheln, breite: r.width, hoehe: r.height)
                        .frame(width: r.width, height: r.height)
                        .offset(x: r.minX, y: r.minY)
                        .zIndex(lage.kachel.feld ? 5 : (lage.kachel.inPause ? 6 : 2))
                }
                ForEach(belegung.streifen) { st in
                    let r = streifenRechteck(st, zeiten: zeiten, mass: mass, breite: breite)
                    Button {
                        guard !tippsperre.gesperrt else { return }
                        oeffnen(st.kacheln.first ?? st.vorne, alle: kacheln, vorne: st.vorne)
                    } label: {
                        ZaehlstreifenView(streifen: st)
                    }
                    .buttonStyle(KartenKnopfStil())
                    .frame(width: r.width, height: r.height)
                    .offset(x: r.minX, y: r.minY)
                    .zIndex(3)
                }
            }
        }
        .frame(width: breite, height: hoehe, alignment: .topLeading)
    }

    @ViewBuilder
    private func kachelKnopf(_ k: Kachel, alle: [Kachel], breite: CGFloat, hoehe: CGFloat) -> some View {
        let ansicht = KachelView(kachel: k, gross: nurTage != nil, breite: breite, hoehe: hoehe)
        if k.antippbar {
            Button {
                // Nur ein echtes Antippen öffnet die Stunde – nicht das Ende einer Wisch- oder Scrollbewegung
                guard !tippsperre.gesperrt else { return }
                if let a = k.aufsicht {
                    aufsichtAntippen?(a)
                } else {
                    oeffnen(k, alle: alle, vorne: nil)
                }
            } label: {
                ansicht
            }
            .buttonStyle(KartenKnopfStil())
        } else {
            ansicht
        }
    }

    /// Stundendetails öffnen: alle Stunden derselben Zeit als Reiter, die angetippte ausgewählt.
    private func oeffnen(_ k: Kachel, alle: [Kachel], vorne: Kachel?) {
        let karten = Kachelbau.parallele(vorne ?? k, in: alle)
        let index = karten.firstIndex { $0.id == k.karte?.id } ?? 0
        antippen(karten, index)
    }

    /// Lage einer Kachel in Punkten.
    private func rechteck(_ lage: Kachellage, mass: Rastermass, breite: CGFloat, vorErste: [UUID]) -> CGRect {
        let k = lage.kachel
        var x = breite * lage.links
        let bereich = breite * lage.breite
        let n = CGFloat(max(1, lage.spuren))
        var w = (bereich - (n - 1) * lage.luecke) / n
        x += CGFloat(lage.spur) * (w + lage.luecke)
        if lage.mitZaehler { w -= 14 }
        var y: CGFloat
        var h: CGFloat
        if k.vorErsterStunde {
            // Pille über dem Raster (Aufsicht vor der ersten Stunde, ganztägiger Termin); mehrere nebeneinander
            let anzahl = CGFloat(max(1, vorErste.count))
            let i = CGFloat(vorErste.firstIndex(of: k.id) ?? 0)
            w = (breite - (anzahl - 1) * 2) / anzahl
            x = i * (w + 2)
            // Direkt über der Linie der 1. Stunde (im freien Streifen vor ihr), nie nach oben in den Tageskopf
            h = 22
            y = max(3, mass.y(mass.start) - h - 4)
        } else if k.inPause {
            // Aufsicht: mittig in der (bei Bedarf gedehnten) Pause, vollständig sichtbar
            let oben = mass.yEnde(k.beginn)
            let unten = k.ende > k.beginn ? mass.yEnde(k.ende) : mass.y(k.ende)
            let luecke = max(0, unten - oben)
            h = max(16, min(nurTage == nil ? 22 : 26, luecke - 2))
            y = oben + max(1, (luecke - h) / 2)
        } else {
            y = mass.y(k.beginn) + 1
            h = max(16, mass.yEnde(k.ende) - mass.y(k.beginn) - 1)
        }
        return CGRect(x: x, y: y, width: max(w, 8), height: h)
    }

    private func streifenRechteck(_ st: Zaehlstreifen, zeiten: [Rasterzeit], mass: Rastermass, breite: CGFloat) -> CGRect {
        let beginn = zeiten.first(where: { $0.nr == st.von })?.beginn ?? st.kacheln.map(\.beginn).min() ?? mass.start
        let ende = zeiten.first(where: { $0.nr == st.bis })?.ende ?? st.kacheln.map(\.ende).max() ?? (beginn + 45)
        let y = mass.y(beginn) + 1
        return CGRect(x: breite - 12, y: y, width: 12, height: max(16, mass.yEnde(ende) - mass.y(beginn) - 1))
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
        RoundedRectangle(cornerRadius: 6, style: .continuous)
            .fill(Farben.ferienHintergrund)
            .overlay {
                // Bezeichnung groß (16 pt), mehrzeilig und zentriert – bricht um statt abgeschnitten zu werden
                VStack(spacing: 3) {
                    Image(systemName: "lock.fill")
                        .font(.system(size: 12, weight: .medium))
                        .padding(.bottom, 2)
                    Text(freiTitel(tag))
                        .font(.system(size: 16, weight: .medium))
                        .multilineTextAlignment(.center)
                        .lineLimit(nil)
                        .fixedSize(horizontal: false, vertical: true)
                    if let grund = tag.freiGrund, !grund.isEmpty, grund.lowercased() != freiTitel(tag).lowercased() {
                        Text(grund)
                            .font(.system(size: 16, weight: .medium))
                            .multilineTextAlignment(.center)
                            .lineLimit(nil)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let von = tag.freiVon, let bis = tag.freiBis, !von.isEmpty, !bis.isEmpty {
                        Text(von == bis ? Datumsformat.anzeige(von, "dd.MM.") : "\(Datumsformat.anzeige(von, "dd.MM.")) bis \(Datumsformat.anzeige(bis, "dd.MM."))")
                            .font(.system(size: 13))
                            .multilineTextAlignment(.center)
                            .lineLimit(2)
                            .minimumScaleFactor(0.8)
                            .padding(.top, 4)
                    }
                }
                .foregroundStyle(Farben.ferienTinte)
                .padding(.horizontal, 3)
            }
    }

    // MARK: Jetzt-Linie

    private func jetztLinie(mass: Rastermass, start: Int, ende: Int, spaltenBreite: CGFloat, breite: CGFloat) -> some View {
        TimelineView(.periodic(from: .now, by: 60)) { kontext in
            let minute = Datumsformat.kalender.component(.hour, from: kontext.date) * 60
                + Datumsformat.kalender.component(.minute, from: kontext.date)
            let heuteIndex = tage.firstIndex(where: { $0.datum == Datumsformat.iso(kontext.date) })
            if let heuteIndex, minute >= start, minute <= ende {
                let hoehe = mass.y(minute)
                ZStack(alignment: .topLeading) {
                    Rectangle()
                        .fill(Farben.jetztLinie)
                        .frame(width: max(breite - rinnenBreite, 0), height: 1.5)
                        .offset(x: rinnenBreite, y: hoehe - 0.75)
                    Capsule()
                        .fill(Farben.jetztLinie)
                        .frame(width: spaltenBreite - tagesLuecke, height: 3)
                        .offset(x: rinnenBreite + CGFloat(heuteIndex) * spaltenBreite + tagesLuecke / 2, y: hoehe - 1.5)
                }
                .allowsHitTesting(false)
            }
        }
    }
}

/// Sperre für das Antippen von Stunden: während einer Wisch- bzw. Scrollbewegung und kurz danach (Nachlauf) öffnet ein
/// Antippen keine Details. Referenztyp ohne Beobachtung – Änderungen zeichnen nichts neu.
final class Tippsperre {
    var wischt = false
    var scrollt = false
    private var bis = Date.distantPast

    var gesperrt: Bool { wischt || scrollt || Date() < bis }

    /// Bewegung beendet: noch `nachlauf` Sekunden gesperrt.
    func bewegungEnde(nachlauf: TimeInterval) {
        wischt = false
        bis = max(bis, Date().addingTimeInterval(nachlauf))
    }

    /// Scrollen beendet (Scrollphase wieder ruhend).
    func scrollEnde() {
        scrollt = false
        bis = max(bis, Date().addingTimeInterval(0.15))
    }
}

/// Meldet ab iOS 18 die Scrollphase des Rasters an die Tippsperre (auch der Schwung nach dem Loslassen zählt).
struct Scrollsperre: ViewModifier {
    let sperre: Tippsperre

    #if compiler(>=6.0)
    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) {
            content.onScrollPhaseChange { _, neu in
                if neu == .idle {
                    sperre.scrollEnde()
                } else {
                    sperre.scrollt = true
                }
            }
        } else {
            content
        }
    }
    #else
    func body(content: Content) -> some View {
        content
    }
    #endif
}

/// Zwischenspeicher der gebauten Kacheln je Tag (Schlüssel: Plan, Stand, angezeigte Tage, Minute). Das Bauen der Kacheln
/// ist teuer und erzeugt neue Kennungen; beim Blättern und erneuten Zeichnen werden die vorhandenen wiederverwendet.
enum Kachelspeicher {
    private static var speicher: [String: [Kachel]] = [:]

    static func kacheln(_ schluessel: String, bauen: () -> [Kachel]) -> [Kachel] {
        if let fertig = speicher[schluessel] { return fertig }
        let neu = bauen()
        if speicher.count > 160 { speicher.removeAll(keepingCapacity: true) }
        speicher[schluessel] = neu
        return neu
    }
}

/// Raster nur neu berechnen, wenn sich Woche, Planart, Element, angezeigte Tage oder Stand ändern
/// (nicht bei jeder Verschiebung während des Wischens).
extension WochenrasterView: Equatable {
    static func == (l: WochenrasterView, r: WochenrasterView) -> Bool {
        l.plan.montag == r.plan.montag && l.typ == r.typ && l.element == r.element && l.nurTage == r.nurTage
            && l.stand == r.stand && l.laedt == r.laedt && l.untenFrei == r.untenFrei
    }
}

/// Karten reagieren beim Antippen ohne Animation und ohne Hervorhebung.
struct KartenKnopfStil: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
    }
}

/// Details einer Pausenaufsicht im gleichen Aufbau wie alle Stundendetails: Blattkopf mit X, Datum | Zeit und ⋮;
/// Kopfblock mit Ort, „Pausenaufsicht“, Zeitraum und Lehrkraft (nur Text; Vertretung: alt durchgestrichen → neu);
/// darunter nur „Im Stundenplan anzeigen“ für die beteiligten Lehrkräfte.
struct AufsichtDetailView: View {
    let aufsicht: Aufsicht
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen

    private var ort: String {
        !aufsicht.gangName.isEmpty ? aufsicht.gangName : (!aufsicht.gang.isEmpty ? aufsicht.gang : "Aufsicht")
    }

    private var zeit: String {
        if aufsicht.beginn.isEmpty { return aufsicht.nachStunde <= 0 ? "vor der 1. Stunde" : "nach der \(aufsicht.nachStunde). Stunde" }
        return aufsicht.ende.isEmpty ? aufsicht.beginn : "\(aufsicht.beginn) - \(aufsicht.ende)"
    }

    private var pause: String {
        aufsicht.nachStunde <= 0 ? "vor der 1. Stunde" : "nach der \(aufsicht.nachStunde). Stunde"
    }

    private var neueLehrkraft: String {
        aufsicht.lehrer == "???" ? "" : aufsicht.lehrer
    }

    /// Lehrkraft-Zeile (nur Text): regulär das Kürzel; Vertretung „alt | neu“ (alt durchgestrichen); offen alt durchgestrichen.
    private var lehrkraft: [Wertbaustein] {
        let alt = aufsicht.alt ?? ""
        switch aufsicht.status {
        case "vertreten", "vertretung":
            // Wie bei Stundenkarten: abwesende Lehrkraft durchgestrichen, daneben die Vertretung (offen: nur durchgestrichen)
            var teile: [Wertbaustein] = []
            if neueLehrkraft.isEmpty {
                if !alt.isEmpty { teile.append(.offen(alt)) }
            } else {
                if !alt.isEmpty && alt != neueLehrkraft {
                    teile.append(.weg(alt))
                    teile.append(.trenner)
                }
                teile.append(.neu(neueLehrkraft))
            }
            return teile
        default:
            return neueLehrkraft.isEmpty ? [] : [.text(neueLehrkraft)]
        }
    }

    private var planziele: [PlanAuswahl] {
        guard zustand.istKollegium && zustand.darfLehrerplaene else { return [] }
        var liste: [PlanAuswahl] = []
        for k in [aufsicht.alt ?? "", neueLehrkraft] where !k.isEmpty {
            let plan = PlanAuswahl(typ: "lehrer", element: k, name: k)
            if !liste.contains(where: { $0.id == plan.id }) { liste.append(plan) }
        }
        return liste
    }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() }, mitte: {
                Blatttitel(titel: "\(Datumsformat.anzeige(aufsicht.datum, "EE")) | \(Datumsformat.anzeige(aufsicht.datum, "dd.MM.yy"))",
                           untertitel: zeit)
            }, rechts: {
                if !planziele.isEmpty {
                    Menu {
                        Section("Im Stundenplan anzeigen") {
                            ForEach(planziele) { plan in
                                Button {
                                    zustand.planZeigen(plan)
                                    schliessen()
                                } label: {
                                    Label(plan.anzeige, image: "SymbolLehrer")
                                }
                            }
                        }
                    } label: {
                        Rundknopf(symbol: "ellipsis", gewicht: .medium, groesse: 18, drehung: 90)
                    }
                    .accessibilityLabel("Weitere Aktionen")
                }
            })
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        Wertzeile(bausteine: [.text(ort)], schrift: Detailmass.gross, farbe: Farben.detailText)
                            .frame(height: Detailmass.zeile)
                        Wertzeile(bausteine: [.text("Pausenaufsicht")], schrift: Detailmass.gross, farbe: Farben.detailText)
                            .frame(height: Detailmass.zeile)
                        Wertzeile(bausteine: [.text(aufsicht.beginn.isEmpty ? pause : "\(zeit) · \(pause)")], schrift: Detailmass.klein)
                            .frame(height: Detailmass.zeile)
                        if !lehrkraft.isEmpty {
                            Wertzeile(bausteine: lehrkraft, schrift: Detailmass.klein)
                                .frame(height: Detailmass.zeile)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, Detailmass.rand)
                    .padding(.top, 10)
                    .padding(.bottom, 12)
                    .accessibilityElement(children: .combine)
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 1)
                    VStack(spacing: 0) {
                        if !aufsicht.gang.isEmpty && aufsicht.gang != ort {
                            Detailzeile(symbol: "mappin.and.ellipse", titel: "Ort", untertitel: aufsicht.gang, pfeil: false)
                        }
                        ForEach(planziele) { plan in
                            Button {
                                zustand.planZeigen(plan)
                                schliessen()
                            } label: {
                                Detailzeile(symbol: "SymbolLehrer", titel: "Im Stundenplan anzeigen", untertitel: plan.anzeige)
                            }
                            .buttonStyle(ZeilenKnopfStil())
                        }
                    }
                    .padding(.top, 8)
                    .padding(.bottom, 24)
                }
            }
            .scrollIndicators(.hidden)
        }
        .background(Farben.blatt.ignoresSafeArea())
    }
}

extension View {
    /// Blatt mit fester Größe: auf dem iPad als Formblatt (feste Größe, ändert sich weder beim Inhalt noch beim Wechsel
    /// in Unterseiten), auf dem iPhone als großes Blatt.
    @ViewBuilder
    func festesBlatt(breit: Bool) -> some View {
        if breit {
            if #available(iOS 18.0, *) {
                self.presentationSizing(.form)
            } else {
                // iOS 17: Blätter erscheinen auf dem iPad als Formblatt mit fester Größe
                self
            }
        } else {
            self.iPhoneBlatt()
        }
    }
}
