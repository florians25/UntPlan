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

    /// Linienzeichen für die Zeilen unter „Stundenpläne“.
    var symbol: String {
        switch self {
        case .klasse: return "person.3"
        case .lehrer: return "person.2"
        case .raum: return "door.left.hand.open"
        }
    }

    func liste(_ e: Elemente) -> [Planelement] {
        switch self {
        case .klasse: return e.klassen
        case .lehrer: return e.lehrer
        case .raum: return e.raeume
        }
    }
}

/// Planauswahl als Blatt (nach Vorlage): Schließen, Titel, „Bearbeiten“; „Mein Stundenplan“, Favoriten mit Stern,
/// Stundenpläne nach Art; unten das Suchfeld. Favoriten lassen sich im Bearbeiten-Modus entfernen und umsortieren.
struct PlanAuswahlView: View {
    @EnvironmentObject var zustand: AppZustand
    @EnvironmentObject var favoriten: Favoriten
    @Environment(\.dismiss) private var schliessen
    @Binding var auswahl: PlanAuswahl?
    @State private var elemente: Elemente? = nil
    @State private var suche = ""
    @State private var fehler: String? = nil
    @State private var bearbeiten: EditMode = .inactive
    @State private var pfad: [Plankategorie] = []

    private var imBearbeiten: Bool { bearbeiten == .active }

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
            .safeAreaInset(edge: .top, spacing: 0) { kopf }
            .safeAreaInset(edge: .bottom, spacing: 0) {
                Suchleiste(text: $suche, platzhalter: "Stundenplan suchen")
            }
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: Plankategorie.self) { kategorie in
                ElementlisteView(kategorie: kategorie, elemente: elemente, auswahl: auswahl) { plan in
                    waehlen(plan)
                }
                .environmentObject(favoriten)
            }
        }
        .presentationBackground(Farben.blatt)
        .presentationCornerRadius(38)
        .task { await laden() }
    }

    // MARK: - Kopf (.pk__top)

    private var kopf: some View {
        HStack(spacing: 12) {
            Button {
                schliessen()
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 19, weight: .regular))
                    .foregroundStyle(Farben.tinte2)
                    .frame(width: 44, height: 44)
                    .glas(Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Schließen")

            Text("Stundenplan auswählen")
                .font(.system(size: 17, weight: .semibold))
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
                    .font(.system(size: 17, weight: imBearbeiten ? .semibold : .regular))
                    .foregroundStyle(Farben.tinte)
                    .padding(.horizontal, 16)
                    .frame(height: 44)
                    .glas(Capsule())
                    .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            .disabled(favoriten.liste.isEmpty && !imBearbeiten)
            .opacity(favoriten.liste.isEmpty && !imBearbeiten ? 0.5 : 1)
        }
        .padding(.horizontal, 16)
        .padding(.top, 16)
        .padding(.bottom, 10)
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
                        .font(.system(size: 17, weight: .semibold))
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
                                Image(systemName: fav.symbol)
                                    .font(.system(size: 19, weight: .regular))
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
        ForEach(Plankategorie.allCases) { kategorie in
            Button {
                pfad.append(kategorie)
            } label: {
                HStack(spacing: 16) {
                    Image(systemName: kategorie.symbol)
                        .font(.system(size: 22, weight: .light))
                        .foregroundStyle(Farben.titelGrau)
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

        if elemente == nil && fehler == nil {
            ProgressView()
                .frame(maxWidth: .infinity)
                .auswahlZeile(oben: 12, unten: 12)
        }
    }

    // MARK: - Suche über alle Arten

    @ViewBuilder
    private var suchergebnisse: some View {
        if let elemente {
            let treffer = Plankategorie.allCases.flatMap { k in
                k.liste(elemente).filter { passt($0) }.map { (kategorie: k, element: $0) }
            }
            if treffer.isEmpty {
                Text("Keine Treffer für „\(suche)“.")
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.gedaempft)
                    .frame(maxWidth: .infinity)
                    .auswahlZeile(oben: 30, unten: 30)
            } else {
                ForEach(treffer, id: \.element.id) { t in
                    Elementzeile(element: t.element, kategorie: t.kategorie, zeigeArt: true,
                                 gewaehlt: auswahl?.typ == t.kategorie.rawValue && auswahl?.element == t.element.kurz) {
                        waehlen(PlanAuswahl(typ: t.kategorie.rawValue, element: t.element.kurz, name: t.element.name))
                    }
                    .environmentObject(favoriten)
                    .auswahlZeile(oben: 0, unten: 0)
                }
            }
        } else {
            ProgressView()
                .frame(maxWidth: .infinity)
                .auswahlZeile(oben: 12, unten: 12)
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

    /// Gruppen nach Anfangsbuchstaben (.picker-letter).
    private var gruppen: [(buchstabe: String, liste: [Planelement])] {
        let sortiert = gefiltert.sorted { $0.kurz.localizedStandardCompare($1.kurz) == .orderedAscending }
        var ergebnis: [(buchstabe: String, liste: [Planelement])] = []
        for e in sortiert {
            let b = e.kurz.first.map { String($0).uppercased() } ?? "#"
            if let letzte = ergebnis.last, letzte.buchstabe == b {
                ergebnis[ergebnis.count - 1].liste.append(e)
            } else {
                ergebnis.append((buchstabe: b, liste: [e]))
            }
        }
        return ergebnis
    }

    var body: some View {
        List {
            if elemente == nil {
                ProgressView()
                    .frame(maxWidth: .infinity)
                    .auswahlZeile(oben: 20, unten: 20)
            } else if gruppen.isEmpty {
                Text(suche.isEmpty ? "Keine Einträge." : "Keine Treffer für „\(suche)“.")
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.gedaempft)
                    .frame(maxWidth: .infinity)
                    .auswahlZeile(oben: 30, unten: 30)
            }
            ForEach(gruppen, id: \.buchstabe) { gruppe in
                Section {
                    ForEach(gruppe.liste) { e in
                        Elementzeile(element: e, kategorie: kategorie, zeigeArt: false,
                                     gewaehlt: auswahl?.typ == kategorie.rawValue && auswahl?.element == e.kurz) {
                            waehlen(PlanAuswahl(typ: kategorie.rawValue, element: e.kurz, name: e.name))
                        }
                        .auswahlZeile(oben: 0, unten: 0)
                    }
                } header: {
                    Text(gruppe.buchstabe)
                        .font(.system(size: 13, weight: .bold))
                        .foregroundStyle(Farben.gedaempft)
                        .padding(.leading, 2)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(Farben.blatt.ignoresSafeArea())
        .safeAreaInset(edge: .top, spacing: 0) { kopf }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            Suchleiste(text: $suche, platzhalter: "\(kategorie.titel) suchen")
        }
        .toolbar(.hidden, for: .navigationBar)
    }

    private var kopf: some View {
        HStack(spacing: 8) {
            Button {
                zurueck()
            } label: {
                Image(systemName: "chevron.left")
                    .font(.system(size: 19, weight: .regular))
                    .foregroundStyle(Farben.tinte2)
                    .frame(width: 44, height: 44)
                    .glas(Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Zurück")
            Text(kategorie.titel)
                .font(.system(size: 17, weight: .semibold))
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
                        .font(.system(size: 13, weight: .bold))
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
                            .font(.system(size: 17, weight: .semibold))
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

/// Suchfeld als Kapsel unten im Blatt (.searchwrap).
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
        .padding(.horizontal, 15)
        .padding(.vertical, 13)
        .background(Farben.suchfeld, in: Capsule())
        .overlay {
            Capsule().strokeBorder(Farben.tinte.opacity(0.10), lineWidth: 1)
        }
        .contentShape(Capsule())
        .onTapGesture { fokus = true }
        .padding(.horizontal, 24)
        .padding(.top, 10)
        .padding(.bottom, 8)
        .background(Farben.blatt)
    }
}

private extension View {
    /// Listenzeile ohne Trennlinie und Hintergrund, Einzug wie in der Vorlage (18 pt).
    func auswahlZeile(oben: CGFloat, unten: CGFloat) -> some View {
        self
            .listRowInsets(EdgeInsets(top: oben, leading: 18, bottom: unten, trailing: 14))
            .listRowSeparator(.hidden)
            .listRowBackground(Color.clear)
    }
}
