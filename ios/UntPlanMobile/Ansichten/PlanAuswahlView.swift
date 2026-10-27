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

/// Planauswahl als Blatt (Vorlage a59416d6): X, Titel; „Mein Stundenplan“, Favoriten mit Stern (Wischen entfernt),
/// „Stundenpläne“ nach Art; unten angeheftet das Suchfeld über alle Planarten (filtert live).
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

    @Environment(\.randblattUnten) private var untenFrei
    @FocusState private var suchfokus: Bool

    /// Abstand des Suchfelds zur Unterkante des Inhalts: Vorlage a59416d6 28 pt über dem Bildschirmrand (also 6 pt in den
    /// Bereich des Home-Indikators hinein, negativ); bei offener Tastatur 12 pt darüber (Vorlage 74e03318).
    private var suchfeldAbstand: CGFloat {
        if untenFrei > 60 || suchfokus { return 12 }
        return 28 - untenFrei
    }

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
            .environment(\.defaultMinListRowHeight, 1)
            .background(Farben.blatt.ignoresSafeArea())
            // Platz für das unten angeheftete Suchfeld
            .contentMargins(.bottom, 90, for: .scrollContent)
            .safeAreaInset(edge: .top, spacing: 0) {
                kopf
            }
            // Vorlage a59416d6: Suchfeld unten, 28 pt vom unteren Bildschirmrand (folgt der Tastatur)
            // Fokus (Vorlage 74e03318): Feld 12 pt über der Tastatur, 12 pt vom linken Rand, rechts daneben (12 pt Abstand)
            // ein runder Knopf X (48 pt, wie das Feld) – bricht die Suche ab und schließt die Tastatur
            .overlay(alignment: .bottom) {
                HStack(spacing: 12) {
                    Plansuche(text: $suche, fokus: $suchfokus)
                    if suchfokus {
                        Button {
                            suche = ""
                            suchfokus = false
                        } label: {
                            // X 17,33 × 17,33 pt (#f4f4f6) wie in den Glasknöpfen
                            Image(systemName: "xmark")
                                .font(.system(size: 21, weight: .light))
                                .foregroundStyle(Plansuche.lupe)
                                .frame(width: 48, height: 48)
                                .background(Plansuche.flaeche, in: Circle())
                                .overlay(Circle().strokeBorder(Plansuche.rand, lineWidth: 1))
                                .contentShape(Circle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Suche abbrechen")
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                    }
                }
                .padding(.horizontal, suchfokus ? 12 : 28)
                .padding(.bottom, suchfeldAbstand >= 0 ? suchfeldAbstand : 0)
                .offset(y: suchfeldAbstand < 0 ? -suchfeldAbstand : 0)
                .animation(.spring(response: 0.35, dampingFraction: 0.9), value: suchfokus)
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

    // MARK: - Kopf

    /// Vorlage a59416d6 (dunkel, 3 px/pt): X-Glasknopf 44 pt 16 pt vom Rand, Titel 17 pt halbfett #bfc8cf mittig.
    private var kopf: some View {
        Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() },
                  laedt: elemente == nil && fehler == nil) {
            Blatttitel(titel: "Stundenplan auswählen", stil: .fett)
        }
    }

    // MARK: - Hauptebene

    @ViewBuilder
    private var hauptebene: some View {
        Button {
            waehlen(nil)
        } label: {
            // Vorlage: Avatar 44 pt (#818f9c) 9 pt unter dem Kopf mit Kalender-Stern 30 × 26,67 pt weiß,
            // „Mein Stundenplan“ 20 pt #f3f5f7 8,5 pt daneben
            HStack(spacing: 8.5) {
                KalenderSymbol(inhalt: .stern, groesse: 32, strich: 1.7)
                    .foregroundStyle(.white)
                    .frame(width: 44, height: 44)
                    .background(Auswahlmass.avatarFlaeche, in: Circle())
                Text("Mein Stundenplan")
                    .font(.system(size: 20))
                    .foregroundStyle(Farben.detailText)
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
        .auswahlZeile(oben: 9, unten: 0)

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

        // Vorlage: „Stundenpläne“ 15 pt #dcdfe5 16,4 pt unter dem Avatar; Zeilen 52 pt: Symbol 20,67 pt (#818f9c) ab
        // 17,67 pt, Titel 17 pt #f3f5f7 ab 57,3 pt, Pfeil 7,67 × 14 pt 27 pt vom rechten Rand
        Text("Stundenpläne")
            .font(.system(size: 15))
            .foregroundStyle(Farben.detailGrau)
            .frame(maxWidth: .infinity, alignment: .leading)
            .auswahlZeile(oben: 16.4, unten: 3.7)
            .accessibilityAddTraits(.isHeader)
            .moveDisabled(true)
            .deleteDisabled(true)
        ForEach(kategorien) { kategorie in
            Button {
                pfad.append(kategorie)
            } label: {
                HStack(spacing: 0) {
                    Listensymbol(name: kategorie.bild, groesse: 20.67, einpassen: true)
                        .foregroundStyle(Farben.detailSymbol)
                        .frame(width: 20.67)
                        .padding(.leading, 17.67)
                        .padding(.trailing, 18)
                    Text(kategorie.titel)
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.detailText)
                    Spacer(minLength: 4)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 15.5, weight: .regular))
                        .foregroundStyle(Farben.detailSymbol)
                        .padding(.trailing, 26.5)
                }
                .frame(height: 52)
                .contentShape(Rectangle())
            }
            .buttonStyle(OhneTippStil())
            .disabled(imBearbeiten)
            .auswahlZeile(oben: 0, unten: 0, rechts: 0, links: 0)
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
                    // die Zeile bringt ihre Ränder selbst mit (16 pt links, Stern 28 pt vom rechten Rand)
                    .auswahlZeile(oben: 0, unten: 0, rechts: 0, links: 0)
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

/// Maße und Farben der Elementauswahl, an der Vorlage (Klassen, dunkel, 2,289 px/pt) nachgemessen; hell entsprechend.
enum Auswahlmass {
    /// Kopf: Zurück-Knopf 44 pt (16 pt vom Rand), Titel 17 pt Semibold #cad0d7 mittig auf Höhe des Knopfs
    static let titelFarbe = Color(hell: 0x5D727F, dunkel: 0xCAD0D7)
    /// Suchfeld: 44 pt hoch, volle Breite (16 pt Rand), Kapsel mit 1-pt-Rand #343436 auf #1b1a1d; Lupe 16 pt weiß
    /// (14 pt vom linken Rand), „Suchen“ 17 pt #ababad 10 pt hinter der Lupe
    static let suchHoehe: CGFloat = 44
    static let suchFlaeche = Color(hell: 0xFFFFFF, dunkel: 0x1B1A1D)
    static let suchRand = Color(hell: 0xCFD6DC, dunkel: 0x343436)
    static let lupe = Color(hell: 0x253746, dunkel: 0xFAFAFC)
    static let platzhalter = Color(hell: 0x8B9AA5, dunkel: 0xABABAD)
    /// Filter „Aktuelles Schuljahr ⌄“: 16 pt unter dem Suchfeld, 42 pt hoch, Kapsel #dadee5 mit 1-pt-Rand #818d97,
    /// Schrift 15 pt #0a0d13 (16 pt Innenabstand links), Pfeil 10 × 5 pt 7 pt hinter dem Text, rechts 19 pt
    static let filterHoehe: CGFloat = 42
    static let filterFlaeche = Color(hell: 0xE8ECF0, dunkel: 0xDADEE5)
    static let filterRand = Color(hell: 0xB4BEC7, dunkel: 0x818D97)
    static let filterText = Color(hell: 0x1C2833, dunkel: 0x0A0D13)
    /// Abschnitt „A“: 15 pt #e5e5e7, darunter (≈ 6,5 pt unter der Versalhöhe) eine 1-pt-Linie #383b43 über die volle Breite
    static let abschnittText = Color(hell: 0x40525E, dunkel: 0xE5E5E7)
    static let abschnittLinie = Color(hell: 0xD5DBE0, dunkel: 0x383B43)
    /// Zeile: runder Avatar 44 pt #828f9d mit weißem Symbol 24 pt, 8 pt Abstand, Name 20 pt Regular weiß, darunter (3 pt)
    /// Badge 19 pt hoch (Kapsel #828f9d, Schrift 13 pt #0b1620, 7 pt Innenabstand); Stern 20 pt #8f949e, Mitte 28 pt vom
    /// rechten Rand; Zeilenabstand ≈ 62 pt (mit Badge) bzw. 60 pt (ohne)
    static let avatar: CGFloat = 44
    static let avatarFlaeche = Color(hell: 0x8B9AA5, dunkel: 0x828F9D)
    static let name = Color(hell: 0x253746, dunkel: 0xFFFFFF)
    static let badgeFlaeche = Color(hell: 0xD3DAE0, dunkel: 0x828F9D)
    static let badgeText = Color(hell: 0x40525E, dunkel: 0x0B1620)
    static let stern = Color(hell: 0x9AA7B1, dunkel: 0x8F949E)
    /// Register rechts: nur die vorhandenen Anfangsbuchstaben, 10,5 pt Semibold #c9d0d2, 13,5 pt Zeilenabstand, Mitte 7,5 pt vom Rand
    static let register = Color(hell: 0x6B7A85, dunkel: 0xC9D0D2)
}

/// Suchfeld unten in der Planauswahl (Vorlage a59416d6): Kapsel 48 pt, 28 pt seitlich, Fläche #252527 mit 1-pt-Rand
/// #4a4a50 (hell weiß mit #cfd6dc), Lupe 16,33 pt (#f4f4f4) 20 pt vom Rand, „Stundenplan suchen“ 17 pt (#a7a7a8) 11 pt
/// dahinter; sucht über alle Planarten.
struct Plansuche: View {
    @Binding var text: String
    var fokus: FocusState<Bool>.Binding

    static let flaeche = Color(hell: 0xFFFFFF, dunkel: 0x252527)
    static let rand = Color(hell: 0xCFD6DC, dunkel: 0x4A4A50)
    static let lupe = Color(hell: 0x253746, dunkel: 0xF4F4F4)
    static let platzhalter = Color(hell: 0x8B9AA5, dunkel: 0xA7A7A8)

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 17.5, weight: .regular))
                .foregroundStyle(Self.lupe)
            TextField("", text: $text, prompt: Text("Stundenplan suchen").foregroundColor(Self.platzhalter))
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.search)
                .focused(fokus)
                .onSubmit { fokus.wrappedValue = false }
        }
        .padding(.leading, 20)
        .padding(.trailing, 14)
        .frame(height: 48)
        .background(Self.flaeche, in: Capsule())
        .overlay {
            Capsule().strokeBorder(Self.rand, lineWidth: 1)
        }
        .contentShape(Capsule())
        .onTapGesture { fokus.wrappedValue = true }
    }
}

/// Alle Klassen, Lehrkräfte oder Räume, nach Anfangsbuchstaben gruppiert, mit Stern für Favoriten (Vorlage „Klassen“):
/// Zurück, Titel, Suchfeld „Suchen“, bei Klassen der Filter „Aktuelles Schuljahr“, Abschnitte mit Linie, Zeilen mit rundem
/// Avatar, Name und grauem Badge (Klassen: „Klassenlehrkraft | ILK“; Lehrkräfte und Räume: der Name, falls vorhanden),
/// Register mit den vorhandenen Buchstaben am rechten Rand.
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
        return liste.filter {
            $0.kurz.localizedCaseInsensitiveContains(suche) || $0.name.localizedCaseInsensitiveContains(suche)
                || $0.leitung.contains { $0.localizedCaseInsensitiveContains(suche) }
        }
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

    /// Filter „Aktuelles Schuljahr“ nur bei Klassen (die Klassen wechseln mit dem Schuljahr).
    private var mitFilter: Bool { kategorie == .klasse }

    var body: some View {
        ScrollViewReader { leser in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0, pinnedViews: [.sectionHeaders]) {
                    if elemente != nil && gruppen.isEmpty {
                        Text(suche.isEmpty ? "Keine Einträge." : "Keine Treffer für „\(suche)“.")
                            .font(.system(size: 16))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 30)
                    }
                    ForEach(gruppen) { gruppe in
                        Section {
                            ForEach(gruppe.liste) { e in
                                Elementzeile(element: e, kategorie: kategorie, zeigeArt: false,
                                             gewaehlt: auswahl?.typ == kategorie.rawValue && auswahl?.element == e.kurz) {
                                    waehlen(PlanAuswahl(typ: kategorie.rawValue, element: e.kurz, name: e.name))
                                }
                            }
                        } header: {
                            Abschnittskopf(buchstabe: gruppe.buchstabe)
                                .id(Alphabetregister.kennung(gruppe.buchstabe))
                        }
                    }
                }
                // Vorlage: zwischen Filter und erstem Abschnitt ≈ 47 pt Luft (ohne Filter wie unter dem Suchfeld)
                .padding(.top, mitFilter ? 31 : 6)
                .padding(.bottom, 24)
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
        .scrollDismissesKeyboard(.immediately)
        .background(Farben.blatt.ignoresSafeArea())
        .safeAreaInset(edge: .top, spacing: 0) {
            VStack(alignment: .leading, spacing: 0) {
                kopf
                Elementsuche(text: $suche)
                if mitFilter {
                    Schuljahrfilter()
                        .padding(.horizontal, 16)
                        .padding(.top, 16)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Farben.blatt)
            .overlay(alignment: .bottom) {
                Ladebalken(aktiv: elemente == nil)
            }
        }
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
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(Auswahlmass.titelFarbe)
                .frame(maxWidth: .infinity)
            Color.clear.frame(width: 44, height: 44)
        }
        .padding(.horizontal, 16)
        .padding(.top, 16)
        .padding(.bottom, 11)
    }
}

/// Abschnittskopf der Elementliste: Buchstabe 15 pt, darunter die Linie über die volle Breite (16 pt Rand); bleibt beim
/// Rollen oben stehen.
struct Abschnittskopf: View {
    let buchstabe: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(buchstabe)
                .font(.system(size: 15))
                .foregroundStyle(Auswahlmass.abschnittText)
                .padding(.leading, 1)
            Rectangle()
                .fill(Auswahlmass.abschnittLinie)
                .frame(height: 1)
        }
        .padding(.horizontal, 16)
        .padding(.top, 4)
        .background(Farben.blatt)
        .accessibilityAddTraits(.isHeader)
    }
}

/// Suchfeld der Elementliste (Vorlage): Kapsel 44 pt mit feinem Rand, Lupe, „Suchen“; filtert bei jeder Eingabe.
struct Elementsuche: View {
    @Binding var text: String
    @FocusState private var fokus: Bool

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 18, weight: .regular))
                .foregroundStyle(Auswahlmass.lupe)
            TextField("", text: $text, prompt: Text("Suchen").foregroundColor(Auswahlmass.platzhalter))
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
        .padding(.leading, 14)
        .padding(.trailing, 12)
        .frame(height: Auswahlmass.suchHoehe)
        .background(Auswahlmass.suchFlaeche, in: Capsule())
        .overlay {
            Capsule().strokeBorder(Auswahlmass.suchRand, lineWidth: 1)
        }
        .contentShape(Capsule())
        .onTapGesture { fokus = true }
        .padding(.horizontal, 16)
    }
}

/// Filter „Aktuelles Schuljahr ⌄“ (Vorlage): helle Kapsel mit feinem Rand; die App kennt nur die Pläne des aktuellen
/// Schuljahrs, das Menü zeigt diese eine (gewählte) Möglichkeit.
struct Schuljahrfilter: View {
    var body: some View {
        Menu {
            Button {
            } label: {
                Label("Aktuelles Schuljahr", systemImage: "checkmark")
            }
        } label: {
            HStack(spacing: 7) {
                Text("Aktuelles Schuljahr")
                    .font(.system(size: 15))
                    .lineLimit(1)
                Image(systemName: "chevron.down")
                    .font(.system(size: 11, weight: .semibold))
            }
            .foregroundStyle(Auswahlmass.filterText)
            .padding(.leading, 16)
            .padding(.trailing, 18)
            .frame(height: Auswahlmass.filterHoehe)
            .background(Auswahlmass.filterFlaeche, in: Capsule())
            .overlay {
                Capsule().strokeBorder(Auswahlmass.filterRand, lineWidth: 1)
            }
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Schuljahr: Aktuelles Schuljahr")
    }
}

// MARK: - Bausteine

/// Treffer der Suche über alle Planarten.
struct Suchtreffer: Identifiable {
    let kategorie: Plankategorie
    let element: Planelement
    var id: String { "\(kategorie.rawValue)|\(element.kurz)" }
}

/// Zeile eines Elements (Vorlage „Klassen“): runder grauer Avatar mit dem Symbol der Planart, Name, darunter das graue
/// Badge (Klassen: „Klassenlehrkraft | ILK“, Lehrkräfte und Räume: der Name, sofern er vom Kürzel abweicht), Haken bei
/// der gewählten Zeile, Stern rechts.
struct Elementzeile: View {
    let element: Planelement
    let kategorie: Plankategorie
    let zeigeArt: Bool
    let gewaehlt: Bool
    let antippen: () -> Void
    @EnvironmentObject var favoriten: Favoriten

    /// Text des Badges; leer = kein Badge.
    static func badge(_ element: Planelement, kategorie: Plankategorie, zeigeArt: Bool) -> String {
        var info = ""
        if kategorie == .klasse {
            if !element.leitung.isEmpty {
                info = (element.leitung.count == 1 ? "Klassenlehrkraft" : "Klassenlehrkräfte") + " | "
                    + element.leitung.joined(separator: ",")
            }
        } else if !element.name.isEmpty && element.name.caseInsensitiveCompare(element.kurz) != .orderedSame {
            info = element.name
        }
        return [zeigeArt ? kategorie.einzahl : "", info].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    var body: some View {
        let plan = PlanAuswahl(typ: kategorie.rawValue, element: element.kurz, name: element.name)
        let badge = Self.badge(element, kategorie: kategorie, zeigeArt: zeigeArt)
        HStack(spacing: 0) {
            Button(action: antippen) {
                HStack(spacing: 8) {
                    Image(kategorie.bild)
                        .renderingMode(.template)
                        .resizable()
                        .scaledToFit()
                        .frame(width: 24, height: 24)
                        .foregroundStyle(.white)
                        .frame(width: Auswahlmass.avatar, height: Auswahlmass.avatar)
                        .background(Auswahlmass.avatarFlaeche, in: Circle())
                    VStack(alignment: .leading, spacing: 3) {
                        Text(element.kurz)
                            .font(.system(size: 20))
                            .foregroundStyle(Auswahlmass.name)
                            .lineLimit(1)
                        if !badge.isEmpty {
                            Text(badge)
                                .font(.system(size: 13))
                                .foregroundStyle(Auswahlmass.badgeText)
                                .lineLimit(1)
                                .padding(.horizontal, 7)
                                .frame(height: 19)
                                .background(Auswahlmass.badgeFlaeche, in: Capsule())
                        }
                    }
                    Spacer(minLength: 4)
                    if gewaehlt {
                        Image(systemName: "checkmark")
                            .font(.system(size: 17, weight: .medium))
                            .foregroundStyle(Farben.akzent)
                    }
                }
                .padding(.vertical, 8)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            let aktiv = favoriten.enthaelt(plan)
            Button {
                favoriten.umschalten(plan)
            } label: {
                Image(systemName: aktiv ? "star.fill" : "star")
                    .font(.system(size: 20, weight: .regular))
                    .foregroundStyle(aktiv ? Color(hexWert: 0xFFCC00) : Auswahlmass.stern)
                    .frame(width: 36, height: 36)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .accessibilityLabel(aktiv ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen")
        }
        // Stern-Mitte 28 pt vom rechten Rand (Vorlage)
        .padding(.leading, 16)
        .padding(.trailing, 10)
        .accessibilityElement(children: .contain)
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
    func auswahlZeile(oben: CGFloat, unten: CGFloat, rechts: CGFloat = 14, links: CGFloat = 16) -> some View {
        self
            .listRowInsets(EdgeInsets(top: oben, leading: links, bottom: unten, trailing: rechts))
            .listRowSeparator(.hidden)
            .listRowBackground(Color.clear)
    }
}

// MARK: - Alphabet-Register

/// Register am rechten Rand einer Liste wie in den Kontakten (Vorlage „Klassen“: nur die vorhandenen Anfangsbuchstaben,
/// z. B. „A B D F G H Z“): Antippen oder Ziehen über die Buchstaben springt zur Gruppe des Buchstabens. „#“ steht oben,
/// wenn es Gruppen mit Ziffern oder Zeichen gibt (z. B. Klassen „10a“). Beim Wechsel des Buchstabens gibt es eine leichte
/// Rückmeldung.
struct Alphabetregister: View {
    /// Vorhandene Gruppen in Listenreihenfolge (Anfangsbuchstaben in Großschreibung).
    let vorhanden: [String]
    let springen: (String) -> Void
    @State private var aktiv: String? = nil
    @State private var rueckmeldung = 0

    static let breite: CGFloat = 22
    static let buchstaben: [String] = (65...90).map { String(UnicodeScalar(UInt8($0))) }
    /// Zeilenabstand der Buchstaben (Vorlage 13,5 pt)
    static let zeile: CGFloat = 13.5

    /// Kennung der Gruppe in der Liste (für `ScrollViewReader.scrollTo`).
    static func kennung(_ buchstabe: String) -> String { "register-" + buchstabe }

    private static func istBuchstabe(_ b: String) -> Bool { buchstaben.contains(b) }

    /// Einträge des Registers: „#“ (nur bei Gruppen ohne Buchstaben A–Z) und die vorhandenen Buchstaben.
    private var eintraege: [String] {
        var liste: [String] = vorhanden.contains { !Self.istBuchstabe($0) } ? ["#"] : []
        for b in vorhanden where Self.istBuchstabe(b) && !liste.contains(b) { liste.append(b) }
        return liste
    }

    /// Gruppe, zu der ein Eintrag springt.
    private func ziel(_ eintrag: String) -> String? {
        if eintrag == "#" { return vorhanden.first { !Self.istBuchstabe($0) } ?? vorhanden.first }
        return vorhanden.contains(eintrag) ? eintrag : vorhanden.last
    }

    var body: some View {
        GeometryReader { geo in
            let liste = eintraege
            let zelle = min(Self.zeile, max(geo.size.height / CGFloat(max(liste.count, 1)), 9))
            let hoehe = zelle * CGFloat(liste.count)
            let oben = (geo.size.height - hoehe) / 2
            VStack(spacing: 0) {
                ForEach(liste, id: \.self) { eintrag in
                    Text(eintrag)
                        .font(.system(size: min(10.5, zelle * 0.78), weight: .semibold))
                        .foregroundStyle(Auswahlmass.register)
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
            // Mitte 7,5 pt vom rechten Rand (Vorlage)
            .position(x: geo.size.width - 7.5, y: oben + hoehe / 2)
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
