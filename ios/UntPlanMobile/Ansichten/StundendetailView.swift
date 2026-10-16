import SwiftUI

/// Details einer Stunde (Blatt beim Antippen einer Karte) nach Vorlage:
/// oben X, Datum und Uhrzeit, ⋮ („Im Stundenplan anzeigen“); darunter Klasse | Gruppe, Fach, Räume, Lehrkräfte;
/// dann Zeilen mit Symbolen (Vertretungstext, Lehrstoff, Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen).
/// Es erscheint nur, was die API für das Konto liefert; Lehrkräfte können Lehrstoff, Notizen und Hausaufgaben bearbeiten.
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
            }
        }
        .environmentObject(zustand)
        .presentationBackground(Farben.blatt)
    }
}

/// Inhalt der Stundendetails für eine Stunde; oben bei mehreren Stunden derselben Zeit die Reiter (z. B. „GF  GF  MB“).
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
            if karten.count > 1 {
                reiterleiste
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopf
                        .padding(.horizontal, 20)
                        .padding(.top, karten.count > 1 ? 14 : 6)
                        .padding(.bottom, 14)
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 0.5)
                    zeilen
                        .padding(.top, 4)
                        .padding(.bottom, 20)
                }
            }
        }
        .background(Farben.blatt.ignoresSafeArea())
        .navigationTitle("")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { leiste }
        .navigationDestination(for: Detailseite.self) { seite in
            ziel(seite)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .task { await daten.laden(kollegium: kollegium) }
        .environmentObject(daten)
    }

    // MARK: - Reiter

    private func reitertitel(_ k: Karte) -> String {
        if !k.fach.isEmpty { return k.fach }
        if !k.text.isEmpty { return k.text }
        return k.art.isEmpty ? "Stunde" : k.art
    }

    /// Reiterleiste in eigener Zeile unter der Kopfleiste – überlappt den Titel nie.
    private var reiterleiste: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 22) {
                ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                    let gewaehlt = eintrag.offset == auswahl
                    Button {
                        auswahl = eintrag.offset
                    } label: {
                        Text(reitertitel(eintrag.element))
                            .font(.system(size: 16, weight: gewaehlt ? .semibold : .regular))
                            .strikethrough(eintrag.element.istEntfall)
                            .foregroundStyle(gewaehlt ? Farben.tinte : Farben.tinte2)
                            .lineLimit(1)
                            .padding(.vertical, 10)
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
        .background(Farben.infoFeld.opacity(0.6))
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Farben.trennlinie)
                .frame(height: 0.5)
        }
    }

    // MARK: - Leiste

    @ToolbarContentBuilder
    private var leiste: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            Button {
                schliessen()
            } label: {
                Glasknopf {
                    Image(systemName: "xmark")
                        .font(.system(size: 16, weight: .regular))
                }
            }
            .accessibilityLabel("Schließen")
        }
        ToolbarItem(placement: .principal) {
            Leistentitel(titel: "\(Datumsformat.anzeige(karte.datum, "EE")) | \(Datumsformat.anzeige(karte.datum, "dd.MM.yy"))",
                         untertitel: karte.beginn.isEmpty ? stundentext : "\(karte.beginn) - \(karte.ende)")
        }
        ToolbarItem(placement: .topBarTrailing) {
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
                    Glasknopf {
                        Image(systemName: "ellipsis")
                            .font(.system(size: 16, weight: .semibold))
                            .rotationEffect(.degrees(90))
                    }
                }
                .accessibilityLabel("Weitere Aktionen")
            }
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

    // MARK: - Kopf

    /// „Klasse | Gruppe“ (Veranstaltung: Klassen bzw. Anlass).
    private var titelzeile: String {
        var teile = karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
        if !karte.gruppe.isEmpty { teile.append(karte.gruppe) }
        if teile.isEmpty {
            if istVeranstaltung { return karte.lehrer.map(\.k).filter { !$0.isEmpty }.joined(separator: " | ") }
            return karte.titel
        }
        return teile.joined(separator: " | ")
    }

    /// Kopf nach Vorlage: „Klasse | Gruppe“, Fach, Raum, Lehrkraft – in normaler Schrift, Änderungen als „alt → neu“.
    private var kopf: some View {
        let stil = Kartenstil(karte)
        let fach = istVeranstaltung ? (karte.text.isEmpty ? karte.art : karte.text) : (karte.fach.isEmpty ? karte.art : karte.fach)
        return VStack(alignment: .leading, spacing: 6) {
            if !titelzeile.isEmpty {
                Text(titelzeile)
                    .font(.system(size: 18))
                    .foregroundStyle(Farben.tinte)
                    .strikethrough(stil.durchgestrichen, color: Farben.tinte)
                    .textSelection(.enabled)
            }
            if !fach.isEmpty {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach {
                        Text(alt)
                            .strikethrough(true)
                            .foregroundStyle(Farben.tinte2)
                        Text("→")
                            .foregroundStyle(Farben.gedaempft)
                    }
                    Text(fach)
                        .strikethrough(stil.durchgestrichen, color: Farben.tinte)
                }
                .font(.system(size: 18))
                .foregroundStyle(Farben.tinte)
            }
            if !karte.fachName.isEmpty && karte.fachName != fach && !istVeranstaltung {
                Text(karte.fachName)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
            }
            if !karte.raeume.isEmpty {
                pfeilText(karte.raeume, durchgestrichen: stil.durchgestrichen)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
            }
            if !karte.lehrer.isEmpty {
                pfeilText(karte.lehrer, durchgestrichen: stil.durchgestrichen || Kachelbau.istBereitschaftAbwesend(karte))
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
            }
            let plaketten = plakettenListe(stil)
            if !plaketten.isEmpty {
                HStack(spacing: 6) {
                    ForEach(Array(plaketten.enumerated()), id: \.offset) { p in
                        Plakette(text: p.element.0, farbe: p.element.1)
                    }
                }
                .padding(.top, 2)
            }
            if !karte.abwesendeKlassen.isEmpty {
                Text("\(karte.abwesendeKlassen.joined(separator: ", ")) – \(karte.abwesendText.isEmpty ? "abwesend" : karte.abwesendText)")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.entfallRand)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func plakettenListe(_ stil: Kartenstil) -> [(String, Color)] {
        var liste: [(String, Color)] = []
        if istVeranstaltung {
            liste.append((karte.status == "termin" ? "Termin" : "Veranstaltung", Farben.gruen))
            return liste
        }
        if let p = stil.plakette, p != "Zusätzlich" { liste.append((p, stil.plaketteFarbe)) }
        if Kachelbau.istZusatzstunde(karte) && !karte.keineVertretung {
            liste.append(("Zusätzliche Unterrichtsstunde", Farben.gruen))
        }
        return liste
    }

    /// Werte mit Änderung als „alt → neu“ (alter Wert durchgestrichen).
    private func pfeilText(_ werte: [Wert], durchgestrichen: Bool) -> Text {
        var ergebnis = Text("")
        for (i, w) in werte.enumerated() {
            if i > 0 { ergebnis = ergebnis + Text(" | ") }
            if durchgestrichen {
                ergebnis = ergebnis + Text(w.k.isEmpty ? (w.alt ?? "") : w.k).strikethrough(true)
                continue
            }
            if let alt = w.alt, !alt.isEmpty, alt != w.k {
                ergebnis = ergebnis + Text(alt).strikethrough(true)
                if !w.k.isEmpty { ergebnis = ergebnis + Text(" → ") }
            }
            if !w.k.isEmpty {
                ergebnis = ergebnis + Text(w.k)
            }
        }
        return ergebnis
    }

    // MARK: - Zeilen

    private var zeilen: some View {
        VStack(spacing: 0) {
            if kollegium && mitUnterricht {
                onlineZeile
                Zeilentrenner()
            }
            if !karte.text.isEmpty && !istVeranstaltung {
                link(.text, Listenzeile(symbol: "doc.text", titel: textTitel, untertitel: karte.text))
            }
            if mitUnterricht && (kollegium || !(daten.eintrag?.thema ?? "").isEmpty) {
                link(.lehrstoff, Listenzeile(symbol: "text.book.closed", titel: "Lehrstoff", untertitel: daten.eintrag?.thema))
            }
            if kollegium && mitUnterricht {
                link(.schueler, Listenzeile(symbol: "person.3", titel: "Schüler*innen",
                                            zahl: daten.anwesenheit == nil ? nil : daten.abwesendAnzahl))
            }
            if mitUnterricht {
                link(.hausaufgaben, Listenzeile(symbol: "book.pages", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgaben == nil ? nil : daten.hausaufgaben?.liste.count))
            }
            if kollegium && !daten.klassen.isEmpty && !istVeranstaltung {
                link(.klassenbuch, Listenzeile(symbol: "book.closed", titel: "Klassenbucheinträge",
                                               zahl: daten.geladen ? daten.vermerke.count : nil))
            }
            if (kollegium && mitUnterricht) || !daten.notizSchueler.isEmpty {
                link(.notizSchueler, Listenzeile(symbol: "note.text", titel: "Notizen für Schüler*innen", untertitel: daten.notizSchueler))
            }
            if kollegium && (mitUnterricht || !daten.notizLehrer.isEmpty) {
                link(.notizLehrer, Listenzeile(symbol: "note.text.badge.plus", titel: "Notizen für Lehrkräfte", untertitel: daten.notizLehrer))
            }
            if karte.hausaufgabeFaellig {
                Listenzeile(symbol: "exclamationmark.circle", titel: "Zu dieser Stunde ist eine Hausaufgabe fällig", pfeil: false)
            }
            if !daten.geladen && mitUnterricht {
                ProgressView()
                    .padding(.top, 14)
            }
        }
    }

    private func link(_ seite: Detailseite, _ inhalt: Listenzeile) -> some View {
        VStack(spacing: 0) {
            NavigationLink(value: seite) {
                inhalt
            }
            .buttonStyle(ZeilenKnopfStil())
            Zeilentrenner()
        }
    }

    /// „Findet online statt“ – speicherbar für Lehrkräfte mit Schreibrecht (optimistisch, bei Fehler zurück).
    private var onlineZeile: some View {
        HStack(spacing: 13) {
            Image(systemName: "video")
                .font(.system(size: 18, weight: .light))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 28)
            Text("Findet online statt")
                .font(.system(size: 16))
                .foregroundStyle(Farben.tinte)
            Spacer(minLength: 4)
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
        .padding(.horizontal, 16)
        .frame(minHeight: 50)
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { onlineFehler != nil }, set: { if !$0 { onlineFehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(onlineFehler ?? "")
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
