import QuickLook
import SwiftUI

/// Mitteilungen nach Vorlage (109/142, nachgemessen bei 2,288 px/pt – iPhone 16 Pro, 402 pt): oben links der runde Glasknopf
/// „Konto wechseln“, mittig der Titel „Mitteilungen“ (18 pt Medium, #CBCFD4), rechts die runden Glasknöpfe Suche und „+“
/// (Menü „An wen soll die Mitteilung gesendet werden?“ mit „Kolleg*innen“ und „Individuell“), 12 pt Abstand; darunter die
/// Reiter „Posteingang | Gesendet | Entwürfe“ (17,5 pt Medium, gewählt weiß, sonst #CCD0D5) und die Liste (Avatar 44 pt mit
/// Initiale, Absender, Betreff, Vorschau, Büroklammer bei Anhängen).
struct MitteilungenView: View {
    enum Ordner: String, CaseIterable, Identifiable {
        case eingang
        case gesendet
        case entwuerfe

        var id: String { rawValue }

        var titel: String {
            switch self {
            case .eingang: return "Posteingang"
            case .gesendet: return "Gesendet"
            case .entwuerfe: return "Entwürfe"
            }
        }

        var symbol: String {
            switch self {
            case .eingang: return "tray"
            case .gesendet: return "paperplane"
            case .entwuerfe: return "doc"
            }
        }

        /// Parameter "ordner" der API (nil = Posteingang)
        var apiWert: String? {
            switch self {
            case .eingang: return nil
            case .gesendet: return "gesendet"
            case .entwuerfe: return "entwuerfe"
            }
        }
    }

    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var ordner: Ordner = .eingang
    @State private var liste: [Mitteilung] = []
    @State private var geladen = false
    @State private var fehler: String? = nil
    @State private var suche = ""
    @State private var sucheOffen = false
    /// Neue Mitteilung bzw. Antwort (Blatt mit Empfängerauswahl und Editor)
    @State private var verfassen: Verfassen? = nil
    /// Geöffnete Mitteilung (Detail)
    @State private var pfad: [Int] = []
    /// Liste wird (neu) geladen – Ladebalken unter der Kopfzeile
    @State private var laedt = false

    private var gefiltert: [Mitteilung] {
        guard !suche.isEmpty else { return liste }
        return liste.filter {
            $0.betreff.localizedCaseInsensitiveContains(suche) || $0.text.localizedCaseInsensitiveContains(suche) ||
                $0.absender.localizedCaseInsensitiveContains(suche) || $0.empfaenger.contains { $0.localizedCaseInsensitiveContains(suche) }
        }
    }

    private var rand: CGFloat { groessenklasse == .compact ? 16 : 20 }

    /// Farben nach Vorlage (dunkel nachgemessen; hell wie die übrigen Seiten).
    static let titelFarbe = Color(hell: 0x5D727F, dunkel: 0xCBCFD4)
    static let reiterAktiv = Color(hell: 0x253746, dunkel: 0xFFFFFF)
    static let reiterInaktiv = Color(hell: 0x6B7A85, dunkel: 0xCCD0D5)

    var body: some View {
        NavigationStack(path: $pfad) {
            VStack(spacing: 0) {
                kopf
                if sucheOffen {
                    Suchleiste(text: $suche, platzhalter: "Mitteilungen durchsuchen")
                        .transition(.opacity)
                }
                inhalt
            }
            .background(Farben.leiste.ignoresSafeArea())
            .toolbar(.hidden, for: .navigationBar)
            .task(id: ordner) { await laden() }
            .navigationDestination(for: Int.self) { id in
                if let m = liste.first(where: { $0.id == id }) {
                    MitteilungDetailView(mitteilung: m, gesendet: ordner != .eingang, gelesen: { gelesenMarkiert($0) },
                                         antworten: { vorlage in verfassen = vorlage },
                                         geloescht: { entfernt($0) })
                        .environmentObject(zustand)
                } else {
                    ContentUnavailableView("Mitteilung nicht gefunden", systemImage: "envelope")
                }
            }
            // Am iPhone dasselbe randlose 90-%-Blatt wie die Stundendetails
            .detailPraesentation(item: $verfassen, breit: groessenklasse == .regular, art: .formular) { v in
                MitteilungVerfassenView(start: v) {
                    if ordner != .eingang { Task { await laden() } }
                }
                .environmentObject(zustand)
            }
        }
    }

    // MARK: - Kopf (Vorlagen 109/142)

    /// Knöpfe direkt am oberen sicheren Rand (Vorlage: Oberkante 62 pt = sicherer Rand), Reiter 21,5 pt darunter,
    /// Liste 20 pt unter den Reitern.
    private var kopf: some View {
        VStack(alignment: .leading, spacing: 21.5) {
            ZStack {
                Text("Mitteilungen")
                    .font(.system(size: 18, weight: .medium))
                    .foregroundStyle(Self.titelFarbe)
                    .lineLimit(1)
                    .accessibilityAddTraits(.isHeader)
                HStack(spacing: 12) {
                    KontoMenue {
                        Glasrund { KontoSymbol() }
                    }
                    Spacer(minLength: 8)
                    sucheKnopf
                    neuKnopf
                }
            }
            ordnerReiter
                .padding(.leading, 24 - rand)
        }
        .padding(.horizontal, rand)
        .padding(.top, 0)
        .padding(.bottom, 20)
        .background(Farben.leiste)
        .overlay(alignment: .bottom) {
            Ladebalken(aktiv: laedt || !geladen)
        }
    }

    /// Suche: runder Glasknopf mit Lupe (Glyphe ≈ 20,5 pt); blendet das Suchfeld ein bzw. aus.
    private var sucheKnopf: some View {
        Button {
            withAnimation(.easeOut(duration: 0.2)) {
                sucheOffen.toggle()
                if !sucheOffen { suche = "" }
            }
        } label: {
            Glasrund {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 19, weight: .regular))
                    .foregroundStyle(Farben.tinte)
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(sucheOffen ? "Suche schließen" : "Mitteilungen durchsuchen")
    }

    /// „Neu“: Menü „An wen soll die Mitteilung gesendet werden?“ – Kolleg*innen bzw. Individuell. Schüler*innen und
    /// Eltern schreiben nur individuell (an die erlaubten Lehrkräfte).
    @ViewBuilder
    private var neuKnopf: some View {
        if zustand.istKollegium {
            Menu {
                Section("An wen soll die Mitteilung gesendet werden?") {
                    Button("Kolleg*innen") { verfassen = Verfassen(typ: .kollegium) }
                    Button("Individuell") { verfassen = Verfassen(typ: .individuell) }
                }
            } label: {
                Glasrund {
                    Image(systemName: "plus")
                        .font(.system(size: 21, weight: .light))
                        .foregroundStyle(Farben.tinte)
                }
            }
            .tint(Color.primary)
            .accessibilityLabel("Neue Mitteilung")
        } else {
            Button {
                verfassen = Verfassen(typ: .individuell)
            } label: {
                Glasrund {
                    Image(systemName: "plus")
                        .font(.system(size: 21, weight: .light))
                        .foregroundStyle(Farben.tinte)
                }
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Neue Mitteilung")
        }
    }

    /// Reiter „Posteingang  Gesendet  Entwürfe“ nebeneinander (17,5 pt Medium, ≈ 27,5 pt Abstand); der gewählte weiß.
    private var ordnerReiter: some View {
        HStack(spacing: 27.5) {
            ForEach(Ordner.allCases) { o in
                Button {
                    guard ordner != o else { return }
                    ordner = o
                } label: {
                    Text(o.titel)
                        .font(.system(size: 17.5, weight: .medium))
                        .foregroundStyle(ordner == o ? Self.reiterAktiv : Self.reiterInaktiv)
                        .lineLimit(1)
                        .fixedSize()
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(ordner == o ? .isSelected : [])
            }
            Spacer(minLength: 0)
        }
    }

    // MARK: - Liste

    private var inhalt: some View {
        List {
            if let fehler {
                Fehlerhinweis(text: fehler)
                    .listRowInsets(EdgeInsets(top: 8, leading: rand, bottom: 8, trailing: rand))
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
            }
            ForEach(gefiltert) { m in
                Button {
                    pfad.append(m.id)
                } label: {
                    Mitteilungszeile(mitteilung: m, gesendet: ordner != .eingang)
                        .contentShape(Rectangle())
                }
                .buttonStyle(ZeilenKnopfStil())
                .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: 0, trailing: 0))
                .listRowBackground(Farben.flaeche)
                .listRowSeparatorTint(Farben.trennlinie)
                .alignmentGuide(.listRowSeparatorLeading) { _ in Mitteilungszeile.textLinks }
                .swipeActions(edge: .trailing) {
                    Button(role: .destructive) {
                        Task { await loeschen(m) }
                    } label: {
                        Label("Löschen", systemImage: "trash")
                    }
                }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(Farben.leiste)
        .environment(\.defaultMinListRowHeight, 0)
        .overlay {
            if geladen && gefiltert.isEmpty && fehler == nil {
                ContentUnavailableView(suche.isEmpty ? "Keine Mitteilungen" : "Keine Treffer", systemImage: "tray",
                                       description: Text(leertext))
            }
        }
        .refreshable { await laden() }
    }

    private var leertext: String {
        if !suche.isEmpty { return "Keine Mitteilung passt zu „\(suche)“." }
        switch ordner {
        case .eingang: return "Ihr Posteingang ist leer."
        case .gesendet: return "Noch nichts gesendet."
        case .entwuerfe: return "Keine Entwürfe."
        }
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            let neu = try await zustand.api.mitteilungen(ordner: ordner.apiWert)
            liste = neu
            fehler = nil
            if ordner == .eingang {
                zustand.ungelesen = neu.filter(\.ungelesen).count
            }
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        geladen = true
    }

    private func gelesenMarkiert(_ id: Int) {
        if let i = liste.firstIndex(where: { $0.id == id }), liste[i].gelesen == nil {
            liste[i].gelesen = Datumsformat.iso(Date())
            zustand.ungelesen = max(0, zustand.ungelesen - 1)
        }
    }

    /// Im Detail gelöscht: aus der Liste entfernen und zurück.
    private func entfernt(_ id: Int) {
        if let m = liste.first(where: { $0.id == id }), ordner == .eingang && m.ungelesen {
            zustand.ungelesen = max(0, zustand.ungelesen - 1)
        }
        pfad.removeAll()
        liste.removeAll { $0.id == id }
    }

    private func loeschen(_ m: Mitteilung) async {
        do {
            try await zustand.api.mitteilungLoeschen(m.id)
            liste.removeAll { $0.id == m.id }
            if ordner == .eingang && m.ungelesen {
                zustand.ungelesen = max(0, zustand.ungelesen - 1)
            }
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Anzeige des Datums in der Liste: heute mit Uhrzeit, sonst Tag und Monat (z. B. „27. Nov.“).
func mitteilungsdatum(_ erstellt: String) -> String {
    guard let d = Datumsformat.datum(erstellt) else { return erstellt }
    if Datumsformat.kalender.isDateInToday(d) && erstellt.count >= 16 {
        return Datumsformat.anzeige(d, "HH:mm")
    }
    return Datumsformat.anzeige(d, "d. MMM")
}

/// „19.01.26 | 07:24“ im Detail (Vorlage 145).
func mitteilungsdatumLang(_ erstellt: String) -> String {
    guard let d = Datumsformat.datum(erstellt) else { return erstellt }
    let tag = Datumsformat.anzeige(d, "dd.MM.yy")
    guard erstellt.count >= 16 else { return tag }
    let zeit = String(erstellt.dropFirst(11).prefix(5))
    return "\(tag) | \(zeit)"
}

/// Zeile nach Vorlage 109/142 (nachgemessen): Zeile 92,7 pt hoch, Fläche #1C1C1E; grauer Avatar-Kreis 44 pt (#878E99,
/// Initiale 17,5 pt weiß) 32 pt vom Rand und 12,7 pt unter der Oberkante; Text 8,5 pt daneben (x = 84,8 pt): Absender,
/// Betreff und Vorschau je 16 pt Regular #E8E8EB im Zeilenabstand 22,7 pt; rechts das Datum 15,5 pt (17,4 pt vom Rand) und
/// bei Anhängen die Büroklammer. Ungelesen: oranger Punkt am Avatar und Absender in Medium.
struct Mitteilungszeile: View {
    let mitteilung: Mitteilung
    let gesendet: Bool

    private var name: String {
        if gesendet {
            if !mitteilung.empfaengerText.isEmpty { return "An: " + mitteilung.empfaengerText }
            return mitteilung.empfaenger.isEmpty ? "Ohne Empfänger" : "An: " + mitteilung.empfaenger.joined(separator: ", ")
        }
        return mitteilung.absender.isEmpty ? "Unbekannt" : mitteilung.absenderKuerzel
    }

    private var ungelesen: Bool { !gesendet && mitteilung.ungelesen }

    static let avatarLinks: CGFloat = 32.3
    static let avatarGroesse: CGFloat = 44
    static let textAbstand: CGFloat = 8.5
    static var textLinks: CGFloat { avatarLinks + avatarGroesse + textAbstand }
    static let textFarbe = Color(hell: 0x253746, dunkel: 0xE8E8EB)
    static let avatarFarbe = Color(hexWert: 0x878E99)

    private var buchstabe: String {
        let quelle = gesendet ? (mitteilung.empfaenger.first ?? "?") : mitteilung.absenderKuerzel
        return quelle.first.map { String($0).uppercased() } ?? "?"
    }

    var body: some View {
        HStack(alignment: .top, spacing: Self.textAbstand) {
            Text(buchstabe)
                .font(.system(size: 17.5))
                .foregroundStyle(.white)
                .frame(width: Self.avatarGroesse, height: Self.avatarGroesse)
                .background(Self.avatarFarbe, in: Circle())
                .overlay(alignment: .topLeading) {
                    if ungelesen {
                        Circle()
                            .fill(Farben.akzent)
                            .frame(width: 12, height: 12)
                            .overlay(Circle().stroke(Farben.flaeche, lineWidth: 2))
                            .offset(x: -2, y: -2)
                    }
                }
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3.6) {
                HStack(alignment: .firstTextBaseline) {
                    Text(name)
                        .font(.system(size: 16, weight: ungelesen ? .medium : .regular))
                        .foregroundStyle(Self.textFarbe)
                        .lineLimit(1)
                    Spacer(minLength: 6)
                    Text(mitteilungsdatum(mitteilung.erstellt))
                        .font(.system(size: 15.5))
                        .foregroundStyle(Self.textFarbe)
                        .lineLimit(1)
                        .fixedSize()
                }
                HStack(spacing: 6) {
                    if mitteilung.wichtig {
                        Image(systemName: "exclamationmark.circle.fill")
                            .foregroundStyle(Farben.entfallRand)
                    }
                    Text(mitteilung.betreff.isEmpty ? "(ohne Betreff)" : mitteilung.betreff)
                        .foregroundStyle(Self.textFarbe)
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    if !mitteilung.anhaenge.isEmpty {
                        Image(systemName: "paperclip")
                            .font(.system(size: 17, weight: .light))
                            .foregroundStyle(Farben.gedaempft)
                            .accessibilityLabel("Mit Anhang")
                    }
                }
                .font(.system(size: 16))
                Text(mitteilung.text.replacingOccurrences(of: "\n", with: " "))
                    .font(.system(size: 16))
                    .foregroundStyle(Self.textFarbe)
                    .lineLimit(1)
            }
            .padding(.top, 1.7)
        }
        .padding(.leading, Self.avatarLinks)
        .padding(.trailing, 17.4)
        .padding(.top, 12.7)
        .padding(.bottom, 13.7)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Detail (Vorlage 145)

/// Eine Mitteilung lesen: Kopf mit rundem Zurück-Glasknopf und Trennlinie; „Von NEU“ und rechts „19.01.26 | 07:24“,
/// der Betreff groß, der Text, Anhänge als Kapseln (PDF-Symbol und Dateiname, antippen öffnet die Vorschau); unten rechts
/// eine Glasgruppe mit Antworten und Löschen. Wird beim Öffnen als gelesen markiert.
struct MitteilungDetailView: View {
    let mitteilung: Mitteilung
    let gesendet: Bool
    let gelesen: (Int) -> Void
    let antworten: (Verfassen) -> Void
    let geloescht: (Int) -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var zurueck
    /// Vollständige Mitteilung aus GET /mitteilungen/{id} (nil = Eintrag der Liste)
    @State private var detail: Mitteilung? = nil
    @State private var laedtAnhang: Int? = nil
    @State private var vorschau: URL? = nil
    @State private var fehler: String? = nil
    @State private var loeschenFragen = false
    @State private var bestaetigt: String? = nil
    @State private var arbeitet = false

    private var m: Mitteilung { detail ?? mitteilung }

    private var darfAntworten: Bool { !gesendet && m.darfAntworten && m.absenderId != 0 }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button {
                    zurueck()
                } label: {
                    Rundknopf(symbol: "chevron.left", gewicht: .regular, groesse: 19)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Zurück")
                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.top, 0)
            .padding(.bottom, 10.5)
            .overlay(alignment: .bottom) {
                Rectangle()
                    .fill(Self.trennFarbe)
                    .frame(height: 1)
            }
            .overlay(alignment: .bottom) {
                Ladebalken(aktiv: arbeitet)
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopfzeile
                    Text(m.betreff.isEmpty ? "(ohne Betreff)" : m.betreff)
                        .font(.system(size: 20))
                        .foregroundStyle(Self.weiss)
                        .padding(.top, 8)
                        .textSelection(.enabled)
                    if m.wichtig {
                        Plakette(text: "Wichtig", farbe: Farben.entfallRand)
                            .padding(.top, 8)
                    }
                    Text(m.text)
                        .font(.system(size: 15))
                        .foregroundStyle(Self.weiss)
                        .lineSpacing(2)
                        .textSelection(.enabled)
                        .padding(.top, 17)
                    if !m.anhaenge.isEmpty {
                        VStack(spacing: 10) {
                            ForEach(m.anhaenge) { a in
                                Anhangpille(anhang: a, laedt: laedtAnhang == a.id) {
                                    Task { await oeffnen(a) }
                                }
                            }
                        }
                        .padding(.top, 42)
                    }
                    if !gesendet && m.lesebestaetigung {
                        lesebestaetigung
                            .padding(.top, 24)
                    }
                    if let fehler {
                        Fehlerhinweis(text: fehler)
                            .padding(.top, 20)
                    }
                }
                .padding(.horizontal, 24)
                .padding(.top, 24)
                .padding(.bottom, 110)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(Farben.blatt.ignoresSafeArea())
        // Glasgruppe 28 pt vom rechten und unteren Bildschirmrand (Vorlage 145)
        .overlay(alignment: .bottomTrailing) {
            aktionen
                .padding(.trailing, 28)
                .padding(.bottom, 28)
                .ignoresSafeArea(.container, edges: .bottom)
        }
        .toolbar(.hidden, for: .navigationBar)
        .quickLookPreview($vorschau)
        .confirmationDialog("Mitteilung löschen?", isPresented: $loeschenFragen, titleVisibility: .visible) {
            Button("Löschen", role: .destructive) { Task { await loeschen() } }
            Button("Abbrechen", role: .cancel) {}
        }
        .task {
            if let neu = try? await zustand.api.mitteilung(mitteilung.id) { detail = neu }
            guard !gesendet, mitteilung.ungelesen else { return }
            do {
                try await zustand.api.alsGelesenMarkieren(mitteilung.id)
                gelesen(mitteilung.id)
            } catch {
                _ = zustand.fehlertext(error)
            }
        }
    }

    /// Farben der Detailseite (Vorlage 145, dunkel nachgemessen).
    static let weiss = Color(hell: 0x253746, dunkel: 0xFEFDFF)
    static let grau = Color(hell: 0x6B7A85, dunkel: 0xCACCD0)
    static let trennFarbe = Color(hell: 0xE4E7EA, dunkel: 0x353B42)

    /// „Von  NEU“ links, „19.01.26 | 07:24“ rechts (gesendet: „An …“), 15 pt.
    private var kopfzeile: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(gesendet ? "An" : "Von")
                .foregroundStyle(Self.grau)
            Text(gesendet ? (m.empfaengerText.isEmpty ? m.empfaenger.joined(separator: ", ") : m.empfaengerText) : m.absenderKuerzel)
                .foregroundStyle(Self.weiss)
                .lineLimit(2)
            Spacer(minLength: 8)
            Text(mitteilungsdatumLang(m.erstellt))
                .foregroundStyle(Self.grau)
                .lineLimit(1)
                .fixedSize()
        }
        .font(.system(size: 15))
        .accessibilityElement(children: .combine)
    }

    /// Lesebestätigung angefordert: bestätigen bzw. Zeitpunkt der eigenen Bestätigung.
    @ViewBuilder
    private var lesebestaetigung: some View {
        let zeitpunkt = bestaetigt ?? m.bestaetigt
        if let zeitpunkt, !zeitpunkt.isEmpty {
            Label("Lesen bestätigt am \(mitteilungsdatumLang(zeitpunkt))", systemImage: "checkmark.circle")
                .font(.system(size: 15))
                .foregroundStyle(Farben.gedaempft)
        } else {
            Button {
                Task { await bestaetigen() }
            } label: {
                Label("Lesebestätigung senden", systemImage: "envelope.open")
                    .font(.system(size: 16, weight: .medium))
                    .foregroundStyle(Farben.tinte)
                    .padding(.horizontal, 16)
                    .frame(height: 44)
                    .liquidGlas(Capsule())
                    .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            .accessibilityHint("Der Absender hat eine Lesebestätigung angefordert")
        }
    }

    /// Glasgruppe unten rechts: Antworten (nur wenn erlaubt) und Löschen.
    private var aktionen: some View {
        Glasgruppe {
            if darfAntworten {
                Glasgruppenknopf(symbol: "arrowshape.turn.up.left", text: "Antworten", farbe: Self.weiss) {
                    Task { await antworte() }
                }
            }
            Glasgruppenknopf(symbol: "trash", text: "Löschen", farbe: Self.weiss) {
                loeschenFragen = true
            }
        }
    }

    private func antworte() async {
        arbeitet = true
        defer { arbeitet = false }
        var start = Verfassen(typ: .antwort)
        start.antwortAuf = m.id
        if let v = try? await zustand.api.antwortvorlage(m.id) {
            start.empfaenger = v.empfaenger
            start.betreff = v.betreff
            start.text = v.text
            if let id = v.antwortAuf { start.antwortAuf = id }
        }
        if start.empfaenger.isEmpty {
            start.empfaenger = [Empfaenger(id: "benutzer:\(m.absenderId)", name: m.absenderKuerzel)]
        }
        if start.betreff.isEmpty {
            start.betreff = m.betreff.hasPrefix("AW:") ? m.betreff : "AW: " + m.betreff
        }
        antworten(start)
    }

    private func bestaetigen() async {
        do {
            try await zustand.api.lesebestaetigungSenden(m.id)
            bestaetigt = Datumsformat.anzeige(Date(), "yyyy-MM-dd HH:mm")
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }

    private func loeschen() async {
        arbeitet = true
        defer { arbeitet = false }
        do {
            try await zustand.api.mitteilungLoeschen(m.id)
            geloescht(m.id)
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }

    /// Anhang laden, als Datei ablegen und in der Vorschau öffnen (Teilen und Sichern über die Vorschau).
    private func oeffnen(_ a: Anhang) async {
        guard laedtAnhang == nil else { return }
        laedtAnhang = a.id
        defer { laedtAnhang = nil }
        do {
            let daten = try await zustand.api.anhangLaden(a.id)
            let ordner = FileManager.default.temporaryDirectory.appendingPathComponent("Anhaenge/\(a.id)", isDirectory: true)
            try FileManager.default.createDirectory(at: ordner, withIntermediateDirectories: true)
            let name = a.name.isEmpty ? "Anhang" : a.name.replacingOccurrences(of: "/", with: "_")
            let datei = ordner.appendingPathComponent(name)
            try daten.write(to: datei, options: .atomic)
            fehler = nil
            vorschau = datei
        } catch {
            fehler = zustand.fehlertext(error) ?? "Der Anhang konnte nicht geladen werden."
        }
    }
}

/// Anhang als Kapsel mit feinem Rand (Vorlage 145): 45 pt hoch, Rand 1 pt #82888F, Symbol 18,4 pt vom Rand, Dateiname
/// 15 pt 10,5 pt daneben.
struct Anhangpille: View {
    let anhang: Anhang
    var laedt: Bool = false
    var entfernen: (() -> Void)? = nil
    let antippen: () -> Void

    private var symbol: String {
        if anhang.istPdf { return "doc.richtext" }
        if anhang.istBild { return "photo" }
        return "doc"
    }

    var body: some View {
        HStack(spacing: 12) {
            Button(action: antippen) {
                HStack(spacing: 10.5) {
                    Group {
                        if laedt {
                            ProgressView()
                        } else {
                            Image(systemName: symbol)
                                .font(.system(size: 19, weight: .light))
                                .foregroundStyle(Color(hell: 0x48606F, dunkel: 0xE7E6EB))
                        }
                    }
                    .frame(width: 20, height: 22)
                    Text(anhang.name.isEmpty ? "Anhang" : anhang.name)
                        .font(.system(size: 15))
                        .foregroundStyle(MitteilungDetailView.weiss)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Spacer(minLength: 4)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            if let entfernen {
                Button(action: entfernen) {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 19))
                        .foregroundStyle(Farben.gedaempft)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Anhang entfernen")
            }
        }
        .padding(.leading, 18.4)
        .padding(.trailing, 14)
        .frame(height: 45)
        .overlay {
            Capsule().strokeBorder(Color(hell: 0xB7C0C7, dunkel: 0x82888F), lineWidth: 1)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Anhang \(anhang.name)")
    }
}

// MARK: - Glas

/// Runder Glasknopf (44 pt) mit Liquid Glass (ab iOS 26, davor Milchglas) wie die Knöpfe der Blattköpfe.
struct Glasrund<Inhalt: View>: View {
    @ViewBuilder let inhalt: () -> Inhalt

    var body: some View {
        inhalt()
            .foregroundStyle(Farben.tinte2)
            .frame(width: Detailmass.knopf, height: Detailmass.knopf)
            .liquidGlas(Circle())
            .contentShape(Circle())
    }
}

/// Mehrere runde Symbolknöpfe in einer gemeinsamen Glas-Kapsel (Vorlagen 144/145): Antworten | Löschen,
/// Kamera | Foto | Datei, Lesebestätigung | Antwort nicht erlaubt. Nachgemessen: 48 pt hoch, je Knopf 54 pt breit,
/// Symbolmitten 58 pt auseinander (zwei Knöpfe 112 pt, drei 170 pt breit), kein zusätzlicher Rand.
struct Glasgruppe<Inhalt: View>: View {
    @ViewBuilder let inhalt: () -> Inhalt

    var body: some View {
        HStack(spacing: 4) {
            inhalt()
        }
        .frame(height: 48)
        .liquidGlas(Capsule())
    }
}

/// Symbolknopf in einer Glasgruppe; `aktiv` hebt ihn orange hervor (Schalter).
struct Glasgruppenknopf: View {
    let symbol: String
    let text: String
    var aktiv: Bool = false
    var zusatz: String? = nil
    /// Symbolfarbe (Vorlage 144: #D6DCE1; Detail 145: weiß)
    var farbe: Color = Color(hell: 0x48606F, dunkel: 0xD6DCE1)
    let aktion: () -> Void

    var body: some View {
        Button(action: aktion) {
            Image(systemName: symbol)
                .font(.system(size: 22, weight: .light))
                .foregroundStyle(aktiv ? Farben.akzent : farbe)
                .overlay(alignment: .bottomTrailing) {
                    if let zusatz {
                        Image(systemName: zusatz)
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(aktiv ? Farben.akzent : farbe)
                            .background(Circle().fill(Farben.blatt).padding(1))
                            .offset(x: 5, y: 4)
                    }
                }
                .frame(width: 54, height: 48)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text)
        .accessibilityAddTraits(aktiv ? .isSelected : [])
    }
}
