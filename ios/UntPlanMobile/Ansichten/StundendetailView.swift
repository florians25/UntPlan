import SwiftUI

/// Details einer Stunde (Blatt beim Antippen einer Karte) genau nach Vorlage:
/// Kopf mit rundem X links, in der Mitte „Do. | 05.11.26 ⌄“ und die Uhrzeit, rechts ⋮ („Im Stundenplan anzeigen“);
/// darunter links bündig Klasse | Gruppe, Fach, Raum und Lehrkraft, eine Trennlinie und die weiße Liste
/// (Vertretungstext, Findet online statt, Lehrstoff, Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen).
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

    init(karte: Karte, typ: String, karten: [Karte], auswahl: Binding<Int>) {
        self.karte = karte
        self.typ = typ
        self.karten = karten
        _auswahl = auswahl
        _daten = StateObject(wrappedValue: Stundendaten(karte: karte))
    }

    private var kollegium: Bool { zustand.istKollegium }
    private var mitUnterricht: Bool { karte.unterricht != nil }
    private var istVeranstaltung: Bool { karte.status == "veranstaltung" || karte.status == "termin" }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() }, mitte: {
                datumsMenue
            }, rechts: {
                weitereAktionen
            })
            if karten.count > 1 {
                reiterleiste
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopfblock
                        .padding(.horizontal, 20)
                        .padding(.top, karten.count > 1 ? 14 : 8)
                        .padding(.bottom, 16)
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 0.5)
                    zeilen
                        .padding(.top, 6)
                        .padding(.bottom, 24)
                }
            }
            .scrollIndicators(.hidden)
        }
        .background(Farben.blatt.ignoresSafeArea())
        .toolbar(.hidden, for: .navigationBar)
        .navigationDestination(for: Detailseite.self) { seite in
            ziel(seite)
                .environmentObject(daten)
                .environmentObject(zustand)
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

    private var zeittitel: String {
        karte.beginn.isEmpty ? stundentext : "\(karte.beginn) - \(karte.ende)"
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
            Blatttitel(titel: datumstitel, untertitel: zeittitel, pfeil: true)
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
                            Label(plan.anzeige, systemImage: plan.typ == "raum" ? "door.left.hand.open" : (plan.typ == "lehrer" ? "person" : "person.3"))
                        }
                    }
                }
            } label: {
                Rundknopf(symbol: "ellipsis", gewicht: .semibold, groesse: 15, drehung: 90)
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

    /// Reiterleiste in eigener Zeile unter dem Kopf – überlappt weder Titel noch Kopfblock.
    private var reiterleiste: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 22) {
                ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                    let gewaehlt = eintrag.offset == auswahl
                    Button {
                        auswahl = eintrag.offset
                    } label: {
                        Text(reitertitel(eintrag.element))
                            .font(.system(size: 15, weight: gewaehlt ? .semibold : .regular))
                            .strikethrough(eintrag.element.istEntfall)
                            .foregroundStyle(gewaehlt ? Farben.tinte : Farben.gedaempft)
                            .lineLimit(1)
                            .padding(.vertical, 9)
                            .overlay(alignment: .bottom) {
                                Rectangle()
                                    .fill(gewaehlt ? Farben.tinte : Color.clear)
                                    .frame(height: 2)
                            }
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(gewaehlt ? .isSelected : [])
                }
            }
            .padding(.horizontal, 20)
        }
        .scrollIndicators(.hidden)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Farben.trennlinie)
                .frame(height: 0.5)
        }
    }

    private var textTitel: String {
        karte.keineVertretung || karte.status == "regulaer" ? "Hinweis" : "Vertretungstext"
    }

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
        hinzu("raum", karte.raeume)
        if kollegium {
            hinzu("klasse", karte.klassen)
            hinzu("lehrer", karte.lehrer)
        } else if let eigene = zustand.benutzer?.element, !eigene.isEmpty {
            hinzu("klasse", karte.klassen.filter { $0.k.caseInsensitiveCompare(eigene) == .orderedSame })
        }
        return ergebnis
    }

    // MARK: - Kopfblock

    /// „Klasse | Klasse | Gruppe“ (Veranstaltung: Klassen bzw. Lehrkräfte).
    private var titelzeile: String {
        var teile = karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
        if !karte.gruppe.isEmpty { teile.append(karte.gruppe) }
        if teile.isEmpty {
            if istVeranstaltung { return karte.lehrer.map(\.k).filter { !$0.isEmpty }.joined(separator: " | ") }
            return karte.titel
        }
        return teile.joined(separator: " | ")
    }

    /// Offene Vertretung: Lehrkraft ohne neuen Wert (Karte „offen“ bzw. Kürzel leer und alter Wert gesetzt).
    private var lehrerOffen: Bool { karte.istOffeneVertretung }

    /// Kopfzeile mit Koppelklassen: eigene Klasse(n), anwesende Mitklassen als Text, fehlende durchgestrichen im grünen Label.
    private var titelBausteine: [Wertbaustein]? {
        guard !karte.mitklassen.isEmpty else { return nil }
        var teile: [Wertbaustein] = []
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
                hinzu(karte.istEntfall ? .weg(alt) : .offen(alt))
            }
        }
        if !karte.gruppe.isEmpty { hinzu(.text(karte.gruppe)) }
        return teile.isEmpty ? nil : teile
    }

    /// Kopfblock nach Vorlage, links bündig mit 20 pt Rand: Klasse | Gruppe (17 pt), Fach (17 pt), Raum und Lehrkraft
    /// (13 pt grau); Änderungen als „alt → neu“ mit durchgestrichenem altem Wert.
    private var kopfblock: some View {
        let stil = Kartenstil(karte)
        let fach = istVeranstaltung ? (karte.text.isEmpty ? karte.art : karte.text) : (karte.fach.isEmpty ? karte.art : karte.fach)
        let raeume = Wertzeile.bausteine(karte.raeume, durchgestrichen: stil.durchgestrichen, offenAlsPlakette: false)
        let lehrer = Wertzeile.bausteine(karte.lehrer, durchgestrichen: stil.durchgestrichen || Kachelbau.istBereitschaftAbwesend(karte),
                                         offenAlsPlakette: lehrerOffen)
        return VStack(alignment: .leading, spacing: 0) {
            if let titelBausteine {
                Wertzeile(bausteine: titelBausteine, schrift: 17, farbe: Farben.tinte)
                    .padding(.bottom, 4)
            } else if !titelzeile.isEmpty {
                Text(titelzeile)
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.tinte)
                    .strikethrough(stil.durchgestrichen, color: Farben.tinte)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
                    .padding(.bottom, 4)
            }
            if !fach.isEmpty && !(istVeranstaltung && fach.caseInsensitiveCompare(titelzeile) == .orderedSame) {
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach {
                        Text(alt)
                            .strikethrough(true)
                            .foregroundStyle(Farben.gedaempft)
                        Text("→")
                            .foregroundStyle(Farben.gedaempft)
                    }
                    Text(fach)
                        .strikethrough(stil.durchgestrichen, color: Farben.tinte)
                }
                .font(.system(size: 17))
                .foregroundStyle(Farben.tinte)
                .padding(.bottom, 8)
            }
            if !raeume.isEmpty {
                Wertzeile(bausteine: raeume)
                    .padding(.bottom, 7)
            }
            if !lehrer.isEmpty {
                Wertzeile(bausteine: lehrer)
            }
            if !karte.abwesendeKlassen.isEmpty {
                Text("\(karte.abwesendeKlassen.joined(separator: ", ")) – \(karte.abwesendText.isEmpty ? "abwesend" : karte.abwesendText)")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.entfallRand)
                    .padding(.top, 7)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    // MARK: - Liste

    private var zeilen: some View {
        VStack(spacing: 0) {
            if !karte.text.isEmpty && !istVeranstaltung {
                link(.text, Detailzeile(symbol: "doc.text", titel: textTitel, untertitel: karte.text))
            }
            if kollegium && mitUnterricht {
                onlineZeile
                DetailTrenner()
            }
            if mitUnterricht && (kollegium || !(daten.eintrag?.thema ?? "").isEmpty) {
                link(.lehrstoff, Detailzeile(symbol: "doc.text", titel: "Lehrstoff", untertitel: daten.eintrag?.thema))
            }
            if kollegium && mitUnterricht {
                link(.schueler, Detailzeile(symbol: "person.3", titel: "Schüler*innen",
                                            zahl: daten.anwesenheit != nil ? daten.abwesendAnzahl : (daten.geladen ? 0 : nil)))
            }
            if mitUnterricht {
                link(.hausaufgaben, Detailzeile(symbol: "book", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgaben.map { $0.liste.count } ?? (daten.geladen ? 0 : nil)))
            }
            if kollegium && !daten.klassen.isEmpty && !istVeranstaltung {
                link(.klassenbuch, Detailzeile(symbol: "book.closed", titel: "Klassenbucheinträge",
                                               zahl: daten.geladen ? daten.vermerke.count : nil))
            }
            if (kollegium && mitUnterricht) || !daten.notizSchueler.isEmpty {
                link(.notizSchueler, Detailzeile(symbol: "doc.text", titel: "Notizen für Schüler*innen", untertitel: daten.notizSchueler))
            }
            if kollegium && (mitUnterricht || !daten.notizLehrer.isEmpty) {
                link(.notizLehrer, Detailzeile(symbol: "doc.text", titel: "Notizen für Lehrkräfte", untertitel: daten.notizLehrer))
            }
            if karte.hausaufgabeFaellig {
                Detailzeile(symbol: "exclamationmark.circle", titel: "Zu dieser Stunde ist eine Hausaufgabe fällig", pfeil: false)
            }
            if !daten.geladen && mitUnterricht {
                ProgressView()
                    .padding(.top, 14)
            }
        }
    }

    private func link<R: View>(_ seite: Detailseite, _ inhalt: Detailzeile<R>) -> some View {
        VStack(spacing: 0) {
            NavigationLink(value: seite) {
                inhalt
            }
            .buttonStyle(ZeilenKnopfStil())
            DetailTrenner()
        }
    }

    /// „Findet online statt“ – speicherbar für Lehrkräfte mit Schreibrecht (optimistisch, bei Fehler zurück).
    private var onlineZeile: some View {
        Detailzeile(symbol: "video", titel: "Findet online statt", pfeil: false) {
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
        case .lehrstoff:
            LehrstoffSeite()
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
