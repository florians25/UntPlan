import SwiftUI

/// Planart in der Auswahl (Klassen, Lehrkräfte, Räume aus /api/app/elemente).
enum Plankategorie: String, Hashable, CaseIterable, Identifiable {
    case klasse
    case lehrer
    case raum

    var id: String { rawValue }

    var titel: String {
        switch self {
        case .klasse: return "Klassen"
        case .lehrer: return "Lehrkräfte"
        case .raum: return "Räume"
        }
    }

    var einzahl: String {
        switch self {
        case .klasse: return "Klasse"
        case .lehrer: return "Lehrkraft"
        case .raum: return "Raum"
        }
    }

    /// Vektorgrafik (Assets.xcassets) für die Zeilen unter „Stundenpläne“ und die Favoriten.
    var bild: String { PlanAuswahl.bild(rawValue) }

    func liste(_ e: Elemente) -> [Planelement] {
        switch self {
        case .klasse: return e.klassen
        case .lehrer: return e.lehrer
        case .raum: return e.raeume
        }
    }
}

/// Planauswahl als Blatt (nach Vorlage): Schließen, Titel, „Bearbeiten“; darunter oben das Suchfeld (filtert live),
/// „Mein Stundenplan“, Favoriten mit Stern, Stundenpläne nach Art. Favoriten lassen sich im Bearbeiten-Modus entfernen und umsortieren.
struct PlanAuswahlView: View {
    @EnvironmentObject var zustand: AppZustand
    @EnvironmentObject var favoriten: Favoriten
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }
    @Binding var auswahl: PlanAuswahl?
    @State private var elemente: Elemente? = nil
    @State private var suche = ""
    @State private var fehler: String? = nil
    @State private var bearbeiten: EditMode = .inactive
    @State private var pfad: [Plankategorie] = []

    private var imBearbeiten: Bool { bearbeiten == .active }

    /// Schüler*innen und Eltern sehen keine Pläne von Lehrkräften. Klassen (und Räume) zeigt die Auswahl ihnen genau
    /// dann, wenn der Server sie in /api/app/elemente liefert (die Schule kann andere Klassenpläne für Schüler*innen
    /// freigeben); leere Listen blenden die Planart für sie aus.
    private var kategorien: [Plankategorie] {
        Plankategorie.allCases.filter { k in
            guard k != .lehrer || zustand.darfLehrerplaene else { return false }
            guard !zustand.istKollegium, let elemente else { return true }
            return !k.liste(elemente).isEmpty
        }
    }

    var body: some View {
        NavigationStack(path: $pfad) {
            List {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                        .auswahlZeile(oben: 4, unten: 8)
                }
                if suche.isEmpty {
                    hauptebene
                } else {
                    suchergebnisse
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
            .environment(\.editMode, $bearbeiten)
            .background(Farben.blatt.ignoresSafeArea())
            .safeAreaInset(edge: .top, spacing: 0) {
                VStack(spacing: 0) {
                    kopf
                    Suchleiste(text: $suche, platzhalter: "Stundenplan suchen")
                }
                .overlay(alignment: .bottom) {
                    Ladebalken(aktiv: elemente == nil && fehler == nil)
                }
            }
            .scrollDismissesKeyboard(.immediately)
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: Plankategorie.self) { kategorie in
                ElementlisteView(kategorie: kategorie, elemente: elemente, auswahl: auswahl) { plan in
                    waehlen(plan)
                }
                .environmentObject(favoriten)
            }
        }
        .background(Farben.blatt.ignoresSafeArea())
        .task { await laden() }
    }

    // MARK: - Kopf (.pk__top)

    private var kopf: some View {
        HStack(spacing: 12) {
            Button {
                schliessen()
            } label: {
                // Liquid Glass wie in den übrigen Kopfzeilen (ab iOS 26 interaktiv, davor Milchglas mit feinem Rand)
                Rundknopf(symbol: "xmark", gewicht: .regular, groesse: 19)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Schließen")

            Text("Stundenplan auswählen")
                .font(.system(size: 17, weight: .medium))
                .foregroundStyle(Color(hell: 0x5D727F, dunkel: 0xC9D1D8))
                .lineLimit(1)
                .minimumScaleFactor(0.8)

            Spacer(minLength: 4)

            Button {
                withAnimation {
                    bearbeiten = imBearbeiten ? .inactive : .active
                }
            } label: {
                Text(imBearbeiten ? "Fertig" : "Bearbeiten")
                    .font(.system(size: 17, weight: imBearbeiten ? .medium : .regular))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                    .fixedSize()
                    .padding(.horizontal, 14)
                    .frame(height: 44)
                    .liquidGlas(Capsule())
                    .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            .disabled(favoriten.liste.isEmpty && !imBearbeiten)
            .opacity(favoriten.liste.isEmpty && !imBearbeiten ? 0.5 : 1)
        }
        .padding(.horizontal, 16)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .background(Farben.blatt)
    }

    // MARK: - Hauptebene

    @ViewBuilder
    private var hauptebene: some View {
        Button {
            waehlen(nil)
        } label: {
            HStack(spacing: 14) {
                AuswahlAvatar {
                    KalenderSymbol(inhalt: .stern, groesse: 24, strich: 1.6)
                }
                Text("Mein Stundenplan")
                    .font(.system(size: 21))
                    .foregroundStyle(Farben.tinte)
                Spacer(minLength: 4)
                if auswahl == nil {
                    Image(systemName: "checkmark")
                        .font(.system(size: 17, weight: .medium))
                        .foregroundStyle(Farben.akzent)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .auswahlZeile(oben: 4, unten: 10)

        if !favoriten.liste.isEmpty {
            Abschnittszeile(text: "Favoriten")
            ForEach(favoriten.liste) { fav in
                HStack(spacing: 14) {
                    Button {
                        waehlen(fav)
                    } label: {
                        HStack(spacing: 14) {
                            AuswahlAvatar {
                                Image(fav.bild)
                                    .renderingMode(.template)
                                    .resizable()
                                    .scaledToFit()
                                    .frame(width: 26, height: 26)
                            }
                            Text(fav.anzeige)
                                .font(.system(size: 21))
                                .foregroundStyle(Farben.tinte)
                                .lineLimit(1)
                            Spacer(minLength: 4)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .disabled(imBearbeiten)
                    if !imBearbeiten {
                        Sternknopf(aktiv: true) { favoriten.umschalten(fav) }
                    }
                }
                .auswahlZeile(oben: 8, unten: 8)
                .accessibilityElement(children: .contain)
            }
            .onDelete { favoriten.entfernen($0) }
            .onMove { favoriten.verschieben($0, nach: $1) }
        }

        Abschnittszeile(text: "Stundenpläne")
        ForEach(kategorien) { kategorie in
            Button {
                pfad.append(kategorie)
            } label: {
                HStack(spacing: 16) {
                    Image(kategorie.bild)
                        .renderingMode(.template)
                        .resizable()
                        .scaledToFit()
                        // Symbol schwarz bzw. weiß je nach Modus (nicht grau)
                        .foregroundStyle(Color.primary)
                        .frame(width: 28, height: 28)
                        .frame(width: 34, height: 30)
                    Text(kategorie.titel)
                        .font(.system(size: 19))
                        .foregroundStyle(Farben.tinte)
                    Spacer(minLength: 4)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 18, weight: .regular))
                        .foregroundStyle(Farben.pfeil)
                        .frame(width: 20)
                }
                .padding(.vertical, 15)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(imBearbeiten)
            .auswahlZeile(oben: 0, unten: 0)
        }

    }

    // MARK: - Suche über alle Arten

    @ViewBuilder
    private var suchergebnisse: some View {
        if let elemente {
            let treffer: [Suchtreffer] = kategorien.flatMap { k in
                k.liste(elemente).filter { passt($0) }.map { Suchtreffer(kategorie: k, element: $0) }
            }
            if treffer.isEmpty {
                Text("Keine Treffer für „\(suche)“.")
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.gedaempft)
                    .frame(maxWidth: .infinity)
                    .auswahlZeile(oben: 30, unten: 30)
            } else {
                ForEach(treffer) { t in
                    Elementzeile(element: t.element, kategorie: t.kategorie, zeigeArt: true,
                                 gewaehlt: auswahl?.typ == t.kategorie.rawValue && auswahl?.element == t.element.kurz) {
                        waehlen(PlanAuswahl(typ: t.kategorie.rawValue, element: t.element.kurz, name: t.element.name))
                    }
                    .environmentObject(favoriten)
                    .auswahlZeile(oben: 0, unten: 0)
                }
            }
        }
    }

    private func passt(_ e: Planelement) -> Bool {
        e.kurz.localizedCaseInsensitiveContains(suche) || e.name.localizedCaseInsensitiveContains(suche)
    }

    private func waehlen(_ plan: PlanAuswahl?) {
        auswahl = plan
        schliessen()
    }

    private func laden() async {
        do {
            elemente = try await zustand.api.elemente()
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

// MARK: - Liste einer Planart

/// Alle Klassen, Lehrkräfte oder Räume, nach Anfangsbuchstaben gruppiert, mit Stern für Favoriten.
struct ElementlisteView: View {
    let kategorie: Plankategorie
    let elemente: Elemente?
    let auswahl: PlanAuswahl?
    let waehlen: (PlanAuswahl) -> Void
    @EnvironmentObject var favoriten: Favoriten
    @Environment(\.dismiss) private var zurueck
    @State private var suche = ""

    private var gefiltert: [Planelement] {
        guard let elemente else { return [] }
        let liste = kategorie.liste(elemente)
        if suche.isEmpty { return liste }
        return liste.filter { $0.kurz.localizedCaseInsensitiveContains(suche) || $0.name.localizedCaseInsensitiveContains(suche) }
    }

    /// Gruppe nach Anfangsbuchstaben (.picker-letter).
    private struct Gruppe: Identifiable {
        let buchstabe: String
        var liste: [Planelement]
        var id: String { buchstabe }
    }

    private var gruppen: [Gruppe] {
        let sortiert = gefiltert.sorted { $0.kurz.localizedStandardCompare($1.kurz) == .orderedAscending }
        var ergebnis: [Gruppe] = []
        for e in sortiert {
            let b = e.kurz.first.map { String($0).uppercased() } ?? "#"
            if let letzte = ergebnis.last, letzte.buchstabe == b {
                ergebnis[ergebnis.count - 1].liste.append(e)
            } else {
                ergebnis.append(Gruppe(buchstabe: b, liste: [e]))
            }
        }
        return ergebnis
    }

    /// Register am rechten Rand nur ohne Suche und bei mehreren Gruppen (wie in den Kontakten).
    private var mitRegister: Bool { suche.isEmpty && gruppen.count > 1 }

    var body: some View {
        ScrollViewReader { leser in
            List {
                if elemente == nil {
                    // Lädt noch (Ladebalken unter dem Suchfeld)
                    EmptyView()
                } else if gruppen.isEmpty {
                    Text(suche.isEmpty ? "Keine Einträge." : "Keine Treffer für „\(suche)“.")
                        .font(.system(size: 16))
                        .foregroundStyle(Farben.gedaempft)
                        .frame(maxWidth: .infinity)
                        .auswahlZeile(oben: 30, unten: 30)
                }
                ForEach(gruppen) { gruppe in
                    Section {
                        ForEach(gruppe.liste) { e in
                            Elementzeile(element: e, kategorie: kategorie, zeigeArt: false,
                                         gewaehlt: auswahl?.typ == kategorie.rawValue && auswahl?.element == e.kurz) {
                                waehlen(PlanAuswahl(typ: kategorie.rawValue, element: e.kurz, name: e.name))
                            }
                            .auswahlZeile(oben: 0, unten: 0, rechts: mitRegister ? Alphabetregister.breite + 6 : 14)
                        }
                    } header: {
                        Text(gruppe.buchstabe)
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Farben.gedaempft)
                            .padding(.leading, 2)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .id(Alphabetregister.kennung(gruppe.buchstabe))
                }
            }
            // Alphabet-Register am rechten Rand: Antippen oder Ziehen springt zum Buchstaben
            .overlay(alignment: .trailing) {
                if mitRegister {
                    Alphabetregister(vorhanden: gruppen.map(\.buchstabe)) { buchstabe in
                        leser.scrollTo(Alphabetregister.kennung(buchstabe), anchor: .top)
                    }
                    .padding(.vertical, 8)
                    .transition(.opacity)
                }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(Farben.blatt.ignoresSafeArea())
        .safeAreaInset(edge: .top, spacing: 0) {
            VStack(spacing: 0) {
                kopf
                Suchleiste(text: $suche, platzhalter: "\(kategorie.titel) suchen")
            }
            .overlay(alignment: .bottom) {
                Ladebalken(aktiv: elemente == nil)
            }
        }
        .scrollDismissesKeyboard(.immediately)
        .toolbar(.hidden, for: .navigationBar)
    }

    private var kopf: some View {
        HStack(spacing: 8) {
            Button {
                zurueck()
            } label: {
                Rundknopf(symbol: "chevron.left", gewicht: .regular, groesse: 19)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Zurück")
            Text(kategorie.titel)
                .font(.system(size: 17, weight: .medium))
                .foregroundStyle(Color(hell: 0x5D727F, dunkel: 0xC9D1D8))
                .frame(maxWidth: .infinity)
            Color.clear.frame(width: 44, height: 44)
        }
        .padding(.horizontal, 16)
        .padding(.top, 16)
        .padding(.bottom, 10)
        .background(Farben.blatt)
    }
}

// MARK: - Bausteine

/// Treffer der Suche über alle Planarten.
struct Suchtreffer: Identifiable {
    let kategorie: Plankategorie
    let element: Planelement
    var id: String { "\(kategorie.rawValue)|\(element.kurz)" }
}

/// Zeile eines Elements (.picker-item): runder Avatar mit Kürzel, Name, Haken, Stern.
struct Elementzeile: View {
    let element: Planelement
    let kategorie: Plankategorie
    let zeigeArt: Bool
    let gewaehlt: Bool
    let antippen: () -> Void
    @EnvironmentObject var favoriten: Favoriten

    var body: some View {
        let plan = PlanAuswahl(typ: kategorie.rawValue, element: element.kurz, name: element.name)
        HStack(spacing: 13) {
            Button(action: antippen) {
                HStack(spacing: 13) {
                    Text(String(element.kurz.prefix(4)))
                        .font(.system(size: 13, weight: .medium))
                        .tracking(0.26)
                        .foregroundStyle(.white)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                        .padding(.horizontal, 3)
                        .frame(width: 42, height: 42)
                        .background(Farben.avatar, in: Circle())
                    VStack(alignment: .leading, spacing: 4) {
                        Text(element.kurz)
                            .font(.system(size: 19))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(1)
                        let zusatz = [zeigeArt ? kategorie.einzahl : "", element.name != element.kurz ? element.name : ""]
                            .filter { !$0.isEmpty }
                            .joined(separator: " · ")
                        if !zusatz.isEmpty {
                            Text(zusatz)
                                .font(.system(size: 12.5))
                                .foregroundStyle(Color(hell: 0x40525E, dunkel: 0xC9D1D8))
                                .lineLimit(1)
                                .padding(.horizontal, 10)
                                .padding(.vertical, 3)
                                .background(Color(hell: 0xD3DAE0, dunkel: 0x3A3A3E), in: Capsule())
                        }
                    }
                    Spacer(minLength: 4)
                    if gewaehlt {
                        Image(systemName: "checkmark")
                            .font(.system(size: 17, weight: .medium))
                            .foregroundStyle(Farben.akzent)
                    }
                }
                .padding(.vertical, 9)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            Sternknopf(aktiv: favoriten.enthaelt(plan)) { favoriten.umschalten(plan) }
        }
    }
}

/// Großer runder grauer Avatar (52 pt in der PWA, 44 pt am Muster).
struct AuswahlAvatar<Inhalt: View>: View {
    @ViewBuilder let inhalt: () -> Inhalt

    var body: some View {
        inhalt()
            .foregroundStyle(.white)
            .frame(width: 44, height: 44)
            .background(Farben.avatar, in: Circle())
    }
}

/// Gelber Stern zum An- und Abwählen eines Favoriten.
struct Sternknopf: View {
    let aktiv: Bool
    let umschalten: () -> Void

    var body: some View {
        Button(action: umschalten) {
            Image(systemName: aktiv ? "star.fill" : "star")
                .font(.system(size: 22, weight: .regular))
                .foregroundStyle(aktiv ? Color(hexWert: 0xFFCC00) : Farben.pfeil)
                .frame(width: 36, height: 36)
                .contentShape(Rectangle())
        }
        .buttonStyle(.borderless)
        .accessibilityLabel(aktiv ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen")
    }
}

/// Überschrift eines Abschnitts in der Auswahl (.pk__section).
struct Abschnittszeile: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 15))
            .foregroundStyle(Farben.abschnitt)
            .frame(maxWidth: .infinity, alignment: .leading)
            .auswahlZeile(oben: 10, unten: 4)
            .accessibilityAddTraits(.isHeader)
            .moveDisabled(true)
            .deleteDisabled(true)
    }
}

/// Suchfeld als Kapsel oben im Blatt (unter dem Titel, über Favoriten und Listen); filtert bei jeder Eingabe.
struct Suchleiste: View {
    @Binding var text: String
    let platzhalter: String
    @FocusState private var fokus: Bool

    var body: some View {
        HStack(spacing: 9) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 19, weight: .regular))
                .foregroundStyle(Farben.gedaempft)
            TextField(platzhalter, text: $text)
                .font(.system(size: 17))
                .foregroundStyle(Farben.tinte)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.search)
                .focused($fokus)
            if !text.isEmpty {
                Button {
                    text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 18))
                        .foregroundStyle(Farben.abschnitt)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Suche leeren")
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(Farben.suchfeld, in: Capsule())
        .overlay {
            Capsule().strokeBorder(Farben.tinte.opacity(0.10), lineWidth: 1)
        }
        .contentShape(Capsule())
        .onTapGesture { fokus = true }
        .padding(.horizontal, 16)
        .padding(.top, 2)
        .padding(.bottom, 10)
        .background(Farben.blatt)
    }
}

private extension View {
    /// Listenzeile ohne Trennlinie und Hintergrund, Einzug 16 pt (iPhone-Vorlage).
    func auswahlZeile(oben: CGFloat, unten: CGFloat, rechts: CGFloat = 14) -> some View {
        self
            .listRowInsets(EdgeInsets(top: oben, leading: 16, bottom: unten, trailing: rechts))
            .listRowSeparator(.hidden)
            .listRowBackground(Color.clear)
    }
}

// MARK: - Alphabet-Register

/// Register A–Z am rechten Rand einer Liste wie in den Kontakten: Antippen oder Ziehen über die Buchstaben springt zur
/// Gruppe des Buchstabens (fehlt er, zur nächsten folgenden, sonst zur letzten davor). „#“ steht oben, wenn es Gruppen
/// mit Ziffern oder Zeichen gibt (z. B. Klassen „10a“). Beim Wechsel des Buchstabens gibt es eine leichte Rückmeldung.
struct Alphabetregister: View {
    /// Vorhandene Gruppen in Listenreihenfolge (Anfangsbuchstaben in Großschreibung).
    let vorhanden: [String]
    let springen: (String) -> Void
    @State private var aktiv: String? = nil
    @State private var rueckmeldung = 0

    static let breite: CGFloat = 22
    static let buchstaben: [String] = (65...90).map { String(UnicodeScalar(UInt8($0))) }

    /// Kennung der Gruppe in der Liste (für `ScrollViewReader.scrollTo`).
    static func kennung(_ buchstabe: String) -> String { "register-" + buchstabe }

    private static func istBuchstabe(_ b: String) -> Bool { buchstaben.contains(b) }

    /// Einträge des Registers: „#“ (nur bei Gruppen ohne Buchstaben A–Z) und A–Z.
    private var eintraege: [String] {
        (vorhanden.contains { !Self.istBuchstabe($0) } ? ["#"] : []) + Self.buchstaben
    }

    /// Gruppe, zu der ein Eintrag springt.
    private func ziel(_ eintrag: String) -> String? {
        if eintrag == "#" { return vorhanden.first { !Self.istBuchstabe($0) } ?? vorhanden.first }
        if vorhanden.contains(eintrag) { return eintrag }
        let buchstabenGruppen = vorhanden.filter(Self.istBuchstabe)
        if let folgend = buchstabenGruppen.first(where: { $0 > eintrag }) { return folgend }
        return buchstabenGruppen.last ?? vorhanden.last
    }

    var body: some View {
        GeometryReader { geo in
            let liste = eintraege
            let zelle = min(16, max(geo.size.height / CGFloat(max(liste.count, 1)), 9))
            let hoehe = zelle * CGFloat(liste.count)
            let oben = (geo.size.height - hoehe) / 2
            VStack(spacing: 0) {
                ForEach(liste, id: \.self) { eintrag in
                    Text(eintrag)
                        .font(.system(size: min(11.5, zelle * 0.78), weight: .medium))
                        .foregroundStyle(Farben.akzent)
                        .frame(width: Self.breite, height: zelle)
                        .scaleEffect(aktiv == eintrag ? 1.25 : 1)
                }
            }
            .frame(width: Self.breite, height: hoehe)
            .background {
                Capsule()
                    .fill(Farben.knopf.opacity(aktiv == nil ? 0 : 0.9))
                    .padding(.vertical, -4)
            }
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0, coordinateSpace: .local)
                    .onChanged { wert in
                        let index = Int((wert.location.y / zelle).rounded(.down))
                        let eintrag = liste[min(max(index, 0), liste.count - 1)]
                        guard eintrag != aktiv else { return }
                        aktiv = eintrag
                        rueckmeldung += 1
                        if let ziel = ziel(eintrag) { springen(ziel) }
                    }
                    .onEnded { _ in
                        withAnimation(.easeOut(duration: 0.2)) { aktiv = nil }
                    }
            )
            .position(x: geo.size.width - Self.breite / 2 - 2, y: oben + hoehe / 2)
        }
        .frame(width: Self.breite + 4)
        .sensoryFeedback(.selection, trigger: rueckmeldung)
        .accessibilityElement()
        .accessibilityLabel("Register")
        .accessibilityValue(aktiv ?? "")
        .accessibilityHint("Nach oben oder unten wischen, um zu einem Buchstaben zu springen")
        .accessibilityAdjustableAction { richtung in
            let liste = eintraege
            let jetzt = aktiv.flatMap { liste.firstIndex(of: $0) } ?? -1
            let neu = richtung == .increment ? min(jetzt + 1, liste.count - 1) : max(jetzt - 1, 0)
            aktiv = liste[neu]
            if let ziel = ziel(liste[neu]) { springen(ziel) }
        }
    }
}
