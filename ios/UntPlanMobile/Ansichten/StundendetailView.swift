import SwiftUI

/// Details einer Stunde (Blatt beim Antippen einer Karte) genau nach Vorlage:
/// Kopf mit rundem X links, in der Mitte „Do. | 05.11.26 ⌄“ und die Uhrzeit, rechts ⋮ („Im Stundenplan anzeigen“);
/// darunter links bündig Klasse | Gruppe, Fach, Raum und Lehrkraft (nur Text), eine Trennlinie und die weiße Liste ohne
/// Trennlinien zwischen den Menüpunkten (Vertretungstext, Findet online statt, Lehrstoff, Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen).
/// Unterseiten öffnen sich im selben Blatt; die Blattgröße bleibt dabei gleich.
struct StundendetailView: View {
    let karten: [Karte]
    let typ: String
    var element: String = ""
    @EnvironmentObject var zustand: AppZustand
    /// Gewählter Reiter (bei mehreren Stunden zur selben Zeit)
    @State private var auswahl: Int

    init(karte: Karte, typ: String, element: String = "") {
        self.init(karten: [karte], start: 0, typ: typ, element: element)
    }

    init(karten: [Karte], start: Int, typ: String, element: String = "") {
        self.karten = karten
        self.typ = typ
        self.element = element
        _auswahl = State(initialValue: min(max(start, 0), max(karten.count - 1, 0)))
    }

    var body: some View {
        NavigationStack {
            if karten.indices.contains(auswahl) {
                StundendetailInhalt(karte: karten[auswahl], typ: typ, karten: karten, auswahl: $auswahl)
                    .id(karten[auswahl].id)
            } else {
                ContentUnavailableView("Keine Stunde", systemImage: "calendar")
                    .toolbar(.hidden, for: .navigationBar)
            }
        }
        .environmentObject(zustand)
        .background(Farben.blatt.ignoresSafeArea())
        // Tastatur: Unterseiten mit Textfeldern weichen ihr selbst aus (Systemverhalten, nichts Manuelles); beim
        // Schließen des Blatts wird der Fokus abgegeben, damit keine Tastatur-Einrückung hängen bleibt
        .onDisappear { Tastatur.schliessen() }
    }
}

/// Inhalt der Stundendetails für eine Stunde; bei mehreren Stunden derselben Zeit stehen die Reiter
/// in einer eigenen Zeile zwischen Kopf und Kopfblock (sie überlappen nichts).
struct StundendetailInhalt: View {
    let karte: Karte
    let typ: String
    let karten: [Karte]
    @Binding var auswahl: Int
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen
    @StateObject private var daten: Stundendaten
    @State private var onlineFehler: String? = nil
    /// Raumliste („Ersetzen“ bzw. „Raum hinzufügen“) aus der Raumzeile
    @State private var raumZiel: RaumZiel? = nil

    init(karte: Karte, typ: String, karten: [Karte], auswahl: Binding<Int>) {
        self.karte = karte
        self.typ = typ
        self.karten = karten
        _auswahl = auswahl
        _daten = StateObject(wrappedValue: Stundendaten(karte: karte))
    }

    private var kollegium: Bool { zustand.istKollegium }
    private var mitUnterricht: Bool { karte.unterricht != nil }
    private var istVeranstaltung: Bool { karte.istVeranstaltung }
    /// Klassenbuch, Hausaufgaben usw. werden noch geladen (Ladebalken unter dem Kopf).
    private var laedt: Bool { mitUnterricht && !daten.geladen }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() },
                      laedt: karten.count <= 1 && laedt, mitte: {
                datumsMenue
            }, rechts: {
                weitereAktionen
            })
            if karten.count > 1 {
                reiterleiste
                    .overlay(alignment: .bottom) { Ladebalken(aktiv: laedt) }
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopfblock
                        .padding(.horizontal, Detailmass.rand)
                        .padding(.top, karten.count > 1 ? 12 : 10)
                        .padding(.bottom, 12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(karte.istEntfall ? Farben.detailEntfallFlaeche : Color.clear)
                    if karte.istEntfall {
                        // Original: roter Balken über die volle Breite mit „ENTFÄLLT“
                        Text("ENTFÄLLT")
                            .font(.system(size: 17))
                            .foregroundStyle(.white)
                            .frame(maxWidth: .infinity)
                            .frame(height: 27)
                            .background(Farben.detailEntfallBalken)
                            .accessibilityLabel("Entfällt")
                    } else {
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 1)
                    }
                    zeilen
                        .padding(.top, 8)
                        .padding(.bottom, 24)
                }
            }
            .scrollIndicators(.hidden)
        }
        .background(Farben.blatt.ignoresSafeArea())
        // Hauptseite ohne Textfelder: bleibt beim Ein- und Ausblenden der Tastatur (z. B. beim Zurückkehren aus einer
        // Notiz) unverändert stehen
        .ignoresSafeArea(.keyboard, edges: .bottom)
        .toolbar(.hidden, for: .navigationBar)
        .navigationDestination(for: Detailseite.self) { seite in
            ziel(seite)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .navigationDestination(item: $raumZiel) { z in
            RaumSeite(ziel: z)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .overlay(alignment: .bottom) {
            if let hinweis = daten.hinweis {
                Text(hinweis)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(.white)
                    .lineLimit(2)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 10)
                    .background(Color.black.opacity(0.78), in: Capsule())
                    .padding(.bottom, 28)
                    .transition(.opacity)
                    .task(id: hinweis) {
                        try? await Task.sleep(nanoseconds: 2_400_000_000)
                        withAnimation { daten.hinweis = nil }
                    }
            }
        }
        .task { await daten.laden(kollegium: kollegium) }
        .environmentObject(daten)
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { onlineFehler != nil }, set: { if !$0 { onlineFehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(onlineFehler ?? "")
        }
    }

    // MARK: - Kopf (Datum, Uhrzeit, ⋮)

    private var datumstitel: String {
        "\(Datumsformat.anzeige(karte.datum, "EE")) | \(Datumsformat.anzeige(karte.datum, "dd.MM.yy"))"
    }

    /// „7:45 - 11:05“ (Stunden ohne führende Null wie im Original).
    private var zeittitel: String {
        karte.beginn.isEmpty ? stundentext : "\(Detailmass.uhrzeit(karte.beginn)) - \(Detailmass.uhrzeit(karte.ende))"
    }

    /// „Do. | 05.11.26 ⌄“ mit Uhrzeit darunter; das Menü nennt Tag und Stunde (und wechselt zwischen gleichzeitigen Stunden).
    private var datumsMenue: some View {
        Menu {
            Section(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")) {
                Button {} label: {
                    Label(karte.beginn.isEmpty ? stundentext : "\(stundentext) · \(karte.beginn) - \(karte.ende)", systemImage: "clock")
                }
                .disabled(true)
            }
            if karten.count > 1 {
                Section("Stunden zur selben Zeit") {
                    ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                        Button {
                            auswahl = eintrag.offset
                        } label: {
                            Label(reitertitel(eintrag.element), systemImage: eintrag.offset == auswahl ? "checkmark" : "book.closed")
                        }
                    }
                }
            }
        } label: {
            Blatttitel(titel: datumstitel, untertitel: zeittitel, pfeil: kollegium || karten.count > 1)
                .contentShape(Rectangle())
        }
        .accessibilityLabel("\(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")), \(zeittitel)")
    }

    @ViewBuilder
    private var weitereAktionen: some View {
        if !planziele.isEmpty {
            Menu {
                Section("Im Stundenplan anzeigen") {
                    ForEach(planziele) { plan in
                        Button {
                            zustand.planZeigen(plan)
                            schliessen()
                        } label: {
                            Label(plan.anzeige, image: plan.bild)
                        }
                    }
                }
            } label: {
                Rundknopf(symbol: "ellipsis", gewicht: .medium, groesse: 18, drehung: 90)
            }
            .accessibilityLabel("Weitere Aktionen")
        }
    }

    // MARK: - Reiter

    private func reitertitel(_ k: Karte) -> String {
        if !k.fach.isEmpty { return k.fach }
        if !k.text.isEmpty { return k.text }
        return k.art.isEmpty ? "Stunde" : k.art
    }

    /// Reiterleiste in eigener Zeile unter dem Kopf genau nach Vorlage (133): hellgraue Zeile, waagrecht scrollbare
    /// Text-Reiter (Regular, aktiv Medium), nur unter dem aktiven eine orange Unterstreichung etwas breiter als der Text;
    /// der aktive Reiter wird beim Wechsel in den sichtbaren Bereich gerollt.
    private var reiterleiste: some View {
        ScrollViewReader { leser in
            ScrollView(.horizontal) {
                HStack(spacing: 8) {
                    ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                        let gewaehlt = eintrag.offset == auswahl
                        Button {
                            auswahl = eintrag.offset
                        } label: {
                            Text(reitertitel(eintrag.element))
                                .font(.system(size: 16, weight: gewaehlt ? .medium : .regular))
                                .strikethrough(eintrag.element.istEntfall)
                                .foregroundStyle(gewaehlt ? Farben.detailText : Farben.tinte2)
                                .lineLimit(1)
                                .padding(.horizontal, 6)
                                .padding(.top, 9)
                                .padding(.bottom, 10)
                                .overlay(alignment: .bottom) {
                                    if gewaehlt {
                                        Capsule()
                                            .fill(Farben.akzent)
                                            .frame(height: 3)
                                    }
                                }
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .id(eintrag.offset)
                        .accessibilityAddTraits(gewaehlt ? .isSelected : [])
                    }
                }
                .padding(.horizontal, Detailmass.rand - 6)
            }
            .scrollIndicators(.hidden)
            .onAppear { leser.scrollTo(auswahl, anchor: .center) }
            .onChange(of: auswahl) { _, neu in
                withAnimation(.easeInOut(duration: 0.2)) { leser.scrollTo(neu, anchor: .center) }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.reiterGrund)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Farben.trennlinie)
                .frame(height: 0.5)
        }
    }

    /// „Prüfung“ bzw. „Veranstaltung“ als dezente graue Pille (wie im Web: normale Schreibweise, 13 pt).
    private func statuspille(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 13, weight: .regular))
            .foregroundStyle(Farben.pilleText)
            .lineLimit(1)
            .padding(.horizontal, 10)
            .padding(.vertical, 2.5)
            .background(Farben.pilleFlaeche, in: Capsule())
            .accessibilityLabel(text)
    }

    private var textTitel: String {
        karte.keineVertretung || karte.status == "regulaer" ? "Information zur Stunde" : "Vertretungstext"
    }

    /// Infotext einer Veranstaltung (Zeile „Information“).
    private var veranstaltungsInfo: String { Kachelbau.veranstaltungsInfo(karte) }

    private var stundentext: String {
        karte.von == karte.bis ? "\(karte.von). Stunde" : "\(karte.von).–\(karte.bis). Stunde"
    }

    /// Räume (auch der ursprüngliche), Klassen und Lehrkräfte, deren Plan das Konto sehen darf.
    private var planziele: [PlanAuswahl] {
        var ergebnis: [PlanAuswahl] = []
        func hinzu(_ typ: String, _ werte: [Wert]) {
            for w in werte {
                for k in [w.alt ?? "", w.k] where !k.isEmpty {
                    let plan = PlanAuswahl(typ: typ, element: k, name: k)
                    if !ergebnis.contains(where: { $0.id == plan.id }) { ergebnis.append(plan) }
                }
            }
        }
        hinzu("raum", daten.aktuelleRaeume)
        if kollegium {
            hinzu("klasse", karte.klassen)
            hinzu("lehrer", karte.lehrer)
        } else if let eigene = zustand.benutzer?.element, !eigene.isEmpty {
            hinzu("klasse", karte.klassen.filter { $0.k.caseInsensitiveCompare(eigene) == .orderedSame })
        }
        return ergebnis
    }

    // MARK: - Kopfblock

    /// „Klasse | Klasse | Gruppe“. Veranstaltung: nur die Klassen – die Lehrkräfte stehen einmal unten in ihrer Zeile.
    private var titelzeile: String {
        var teile = ohneDoppelte(karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k })
        if !karte.gruppe.isEmpty { teile.append(karte.gruppe) }
        if teile.isEmpty {
            return istVeranstaltung ? "" : karte.titel
        }
        return teile.joined(separator: " | ")
    }

    /// Werte ohne Leere und ohne Doppelte (Groß-/Kleinschreibung egal), Reihenfolge bleibt.
    private func ohneDoppelte(_ werte: [String]) -> [String] {
        var ergebnis: [String] = []
        for w in werte where !w.isEmpty && !ergebnis.contains(where: { $0.caseInsensitiveCompare(w) == .orderedSame }) {
            ergebnis.append(w)
        }
        return ergebnis
    }

    /// Veranstaltung: jede Lehrkraft bzw. jeder Raum nur einmal.
    private func einmal(_ werte: [Wert]) -> [Wert] {
        guard istVeranstaltung else { return werte }
        var ergebnis: [Wert] = []
        for w in werte {
            let t = w.k.isEmpty ? (w.alt ?? "") : w.k
            if t.isEmpty || ergebnis.contains(where: { ($0.k.isEmpty ? ($0.alt ?? "") : $0.k).caseInsensitiveCompare(t) == .orderedSame }) { continue }
            ergebnis.append(w)
        }
        return ergebnis
    }

    /// Offene Vertretung: Lehrkraft ohne neuen Wert (Karte „offen“ bzw. Kürzel leer und alter Wert gesetzt).
    private var lehrerOffen: Bool { karte.istOffeneVertretung }

    /// Kopfzeile „Klasse | Gruppe“: eigene Klasse(n), anwesende Mitklassen als Text, fehlende durchgestrichen;
    /// Trennstrich grau. Veranstaltung ohne Klassen: keine Kopfzeile (Titel steht in der nächsten Zeile).
    private var titelBausteine: [Wertbaustein] {
        var teile: [Wertbaustein] = []
        guard !karte.mitklassen.isEmpty else {
            if titelzeile.isEmpty { return [] }
            for teil in titelzeile.components(separatedBy: " | ") {
                if !teile.isEmpty { teile.append(.trenner) }
                teile.append(.text(teil))
            }
            return teile
        }
        func hinzu(_ b: Wertbaustein) {
            if !teile.isEmpty { teile.append(.trenner) }
            teile.append(b)
        }
        for w in karte.klassen {
            let t = w.k.isEmpty ? (w.alt ?? "") : w.k
            if !t.isEmpty { hinzu(.text(t)) }
        }
        for w in karte.mitklassen {
            if !w.k.isEmpty {
                hinzu(.text(w.k))
            } else if let alt = w.alt, !alt.isEmpty {
                hinzu(.weg(alt))
            }
        }
        if !karte.gruppe.isEmpty { hinzu(.text(karte.gruppe)) }
        return teile
    }

    /// Kopfblock genau nach Vorlage (Original 117/118): „Klasse | Gruppe“ und Fach groß (22 pt, schwarz), Raum und
    /// Lehrkraft 17 pt grau, je Zeile 31 pt hoch. Nur Text, keine Plaketten und keine Durchstreichung des Ganzen
    /// (Entfall zeigt der rote Balken): Änderungen als „alt → neu“ mit durchgestrichenem altem Wert, fehlende Werte
    /// durchgestrichen. Jede Angabe steht in genau einer Zeile, die sich bei vielen Werten waagrecht scrollen lässt.
    private var kopfblock: some View {
        let fach = istVeranstaltung ? (karte.text.isEmpty ? karte.art : karte.text) : (karte.fach.isEmpty ? karte.art : karte.fach)
        // Pfeil „alt → neu“ nur beim Raum; bei Lehrkräften und Fach trennt „|“ (z. B. „LAN | ILK“)
        let raeume = Wertzeile.bausteine(einmal(daten.aktuelleRaeume), durchgestrichen: false, offenAlsPlakette: false, pfeil: true)
        let lehrer = Wertzeile.bausteine(einmal(karte.lehrer), durchgestrichen: Kachelbau.istBereitschaftAbwesend(karte),
                                         offenAlsPlakette: lehrerOffen)
        var fachteile: [Wertbaustein] = []
        if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach, !istVeranstaltung {
            fachteile.append(.weg(alt))
            fachteile.append(.trenner)
        }
        fachteile.append(.text(fach))
        return VStack(alignment: .leading, spacing: 0) {
            if !titelBausteine.isEmpty {
                Wertzeile(bausteine: titelBausteine, schrift: Detailmass.gross, farbe: Farben.detailText)
                    .frame(height: Detailmass.zeile)
            }
            if !fach.isEmpty && !(istVeranstaltung && fach.caseInsensitiveCompare(titelzeile) == .orderedSame) {
                Wertzeile(bausteine: fachteile, schrift: Detailmass.gross, farbe: Farben.detailText)
                    .frame(height: Detailmass.zeile)
            }
            if let pille = karte.statusPille {
                statuspille(pille)
                    .padding(.top, 2)
                    .padding(.bottom, 4)
            }
            if !raeume.isEmpty {
                Wertzeile(bausteine: raeume, schrift: Detailmass.klein)
                    .frame(height: Detailmass.zeile)
            }
            if !lehrer.isEmpty {
                Wertzeile(bausteine: lehrer, schrift: Detailmass.klein)
                    .frame(height: Detailmass.zeile)
            }
            if !karte.abwesendeKlassen.isEmpty {
                Wertzeile(bausteine: [.text("\(karte.abwesendeKlassen.joined(separator: ", ")) – \(karte.abwesendText.isEmpty ? "abwesend" : karte.abwesendText)")],
                          schrift: Detailmass.klein)
                    .frame(height: Detailmass.zeile)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    // MARK: - Liste

    /// Reihenfolge wie in der Vorlage: „Findet online statt“ ganz oben, Raum, Vertretungstext bzw. Information, Lehrstoff,
    /// Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen.
    private var zeilen: some View {
        VStack(spacing: 0) {
            if kollegium && mitUnterricht && !istVeranstaltung {
                onlineZeile
            }
            if kollegium && daten.raumAenderbar && !istVeranstaltung {
                RaumZeile(raeume: daten.aktuelleRaeume, ziel: $raumZiel)
            }
            if istVeranstaltung {
                if !veranstaltungsInfo.isEmpty {
                    link(.info, Detailzeile(symbol: "SymbolInfo", titel: "Information", untertitel: veranstaltungsInfo))
                }
            } else if !karte.text.isEmpty {
                // Information zur Stunde mit dem Info-Zeichen (info.svg), Vertretungstext mit dem Notizblatt
                link(.text, Detailzeile(symbol: textTitel == "Information zur Stunde" ? "SymbolInfo" : "SymbolNotiz",
                                        titel: textTitel, untertitel: karte.text))
            }
            if mitUnterricht && (kollegium || !(daten.eintrag?.thema ?? "").isEmpty) {
                let thema = daten.eintrag?.thema ?? ""
                link(.lehrstoff, Detailzeile(symbol: "SymbolLehrstoff", titel: "Lehrstoff", untertitel: thema) {
                    if !thema.isEmpty {
                        Image(systemName: "checkmark.circle")
                            .font(.system(size: 24, weight: .light))
                            .foregroundStyle(Farben.gedaempft)
                            .padding(.trailing, 4)
                            .accessibilityLabel("Lehrstoff eingetragen")
                    }
                })
            }
            if kollegium && mitUnterricht {
                link(.schueler, Detailzeile(symbol: "SymbolSchueler", titel: "Schüler*innen") {
                    if daten.anwesenheit != nil || daten.geladen {
                        SchuelerPlakette(zahl: daten.abwesendAnzahl, kontrolliert: daten.anwesenheit?.kontrolliert == true)
                            .padding(.trailing, 4)
                    }
                })
            }
            if mitUnterricht {
                link(.hausaufgaben, Detailzeile(symbol: "SymbolHausaufgaben", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgaben.map { $0.liste.count } ?? (daten.geladen ? 0 : nil)))
            }
            if kollegium && !daten.klassen.isEmpty && !istVeranstaltung {
                link(.klassenbuch, Detailzeile(symbol: "SymbolKlassenbuch", titel: "Klassenbucheinträge",
                                               zahl: daten.geladen ? daten.vermerke.count : nil))
            }
            if (kollegium && mitUnterricht) || !daten.notizSchueler.isEmpty {
                link(.notizSchueler, Detailzeile(symbol: "SymbolNotiz", titel: "Notizen für Schüler*innen", untertitel: daten.notizSchueler))
            }
            // Notizen für Lehrkräfte sehen (und bearbeiten) nur Lehrkräfte
            if kollegium && (mitUnterricht || !daten.notizLehrer.isEmpty) {
                link(.notizLehrer, Detailzeile(symbol: "SymbolNotiz", titel: "Notizen für Lehrkräfte", untertitel: daten.notizLehrer))
            }
            if karte.hausaufgabeFaellig {
                Detailzeile(symbol: "SymbolHausaufgaben", titel: "Zu dieser Stunde ist eine Hausaufgabe fällig", pfeil: false)
            }
        }
    }

    /// Menüpunkt ohne Trennlinie (wie in der Vorlage).
    private func link<R: View>(_ seite: Detailseite, _ inhalt: Detailzeile<R>) -> some View {
        NavigationLink(value: seite) {
            inhalt
        }
        .buttonStyle(ZeilenKnopfStil())
    }

    /// „Findet online statt“ – speicherbar für Lehrkräfte mit Schreibrecht (optimistisch, bei Fehler zurück).
    private var onlineZeile: some View {
        Detailzeile(symbol: "SymbolOnline", titel: "Findet online statt", pfeil: false) {
            Toggle("Findet online statt", isOn: Binding(
                get: { daten.istOnline },
                set: { neu in
                    Task {
                        do {
                            try await daten.onlineSetzen(neu)
                        } catch {
                            onlineFehler = zustand.fehlertext(error)
                        }
                    }
                }))
                .labelsHidden()
                .tint(Farben.gruen)
                .disabled(!daten.darf || !daten.geladen)
        }
    }

    @ViewBuilder
    private func ziel(_ seite: Detailseite) -> some View {
        switch seite {
        case .text:
            DetailTextSeite(titel: textTitel, text: karte.text, karte: karte)
        case .info:
            DetailTextSeite(titel: "Information", text: veranstaltungsInfo, karte: karte)
        case .lehrstoff:
            NotizSeite(art: .lehrstoff)
        case .schueler:
            SchuelerSeite()
        case .hausaufgaben:
            HausaufgabenSeite()
        case .klassenbuch:
            KlassenbuchSeite()
        case .notizSchueler:
            NotizSeite(art: .schueler)
        case .notizLehrer:
            NotizSeite(art: .lehrer)
        }
    }
}
