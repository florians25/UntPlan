import SwiftUI

// MARK: - Neuer Klassenbucheintrag

/// Formular „Neuer Klassenbucheintrag“ wie in der Vorlage: Kopf mit X, Titel und orangem Haken; darunter die Zeilen
/// „Schüler*innen (n)“ mit den Namen und „Eintragskategorie“ (je mit Pfeil) und ein Textfeld
/// „Hier klicken um Text einzutragen.“ Gespeichert wird je Schüler*in ein Eintrag (ohne Auswahl zur Klasse).
struct NeuerKlassenbucheintragView: View {
    let vorauswahl: [Schueler]
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }
    @State private var gewaehlt: Set<Int> = []
    @State private var kategorie = ""
    @State private var text = ""
    @State private var speichert = false
    @State private var fehler: String? = nil
    @FocusState private var fokus: Bool

    private var alle: [Schueler] { daten.anwesenheit?.schueler ?? vorauswahl }

    private var auswahl: [Schueler] { alle.filter { gewaehlt.contains($0.id) } }

    private var gueltig: Bool { !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !kategorie.isEmpty }

    var body: some View {
        NavigationStack {
            DetailSeitenRahmen(titel: "Neuer Klassenbucheintrag", symbol: "xmark", zurueck: { schliessen() }) {
                ScrollView {
                    VStack(spacing: 0) {
                        NavigationLink {
                            SchuelerAuswahlView(alle: alle, gewaehlt: $gewaehlt)
                        } label: {
                            Formularzeile(titel: "Schüler*innen (\(gewaehlt.count))",
                                          untertitel: auswahl.map(\.anzeige).joined(separator: ", "))
                        }
                        .buttonStyle(ZeilenKnopfStil())
                        Formulartrenner()
                        NavigationLink {
                            KategorieAuswahlView(arten: daten.vermerkArten, auswahl: $kategorie)
                        } label: {
                            Formularzeile(titel: "Eintragskategorie", untertitel: kategorie)
                        }
                        .buttonStyle(ZeilenKnopfStil())
                        Formulartrenner()
                        TextField("Hier klicken um Text einzutragen.", text: $text, axis: .vertical)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(4...12)
                            .focused($fokus)
                            .padding(.horizontal, 16)
                            .padding(.vertical, 14)
                            .frame(minHeight: 80, alignment: .topLeading)
                        Formulartrenner()
                        if let fehler {
                            Text(fehler)
                                .font(.system(size: 14))
                                .foregroundStyle(Farben.entfallRand)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(16)
                        }
                    }
                }
                .scrollDismissesKeyboard(.interactively)
            } rechts: {
                SichernKnopf(speichert: speichert, aktiv: gueltig) { Task { await sichern() } }
            }
        }
        .onAppear {
            if gewaehlt.isEmpty { gewaehlt = Set(vorauswahl.map(\.id)) }
            if kategorie.isEmpty { kategorie = daten.vermerkArten.first ?? "Vermerk" }
        }
    }

    private func sichern() async {
        speichert = true
        defer { speichert = false }
        do {
            try await daten.vermerkAnlegen(schueler: auswahl, art: kategorie,
                                           text: text.trimmingCharacters(in: .whitespacesAndNewlines))
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Zeile im Formular: Titel 16 pt, darunter grau die Auswahl, rechts der Pfeil.
struct Formularzeile: View {
    let titel: String
    var untertitel: String = ""
    var pfeil: Bool = true

    var body: some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(titel)
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if !untertitel.isEmpty {
                    Text(untertitel)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(1)
                        .truncationMode(.tail)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if pfeil {
                Image(systemName: "chevron.right")
                    .font(.system(size: 19, weight: .ultraLight))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .frame(minHeight: 48)
        .contentShape(Rectangle())
    }
}

/// Feine Linie zwischen den Formularzeilen.
struct Formulartrenner: View {
    var body: some View {
        Rectangle()
            .fill(Farben.trennlinie)
            .frame(height: 0.5)
            .padding(.horizontal, 16)
    }
}

/// Auswahl der Schüler*innen (mehrere) für einen Klassenbucheintrag.
struct SchuelerAuswahlView: View {
    let alle: [Schueler]
    @Binding var gewaehlt: Set<Int>
    @State private var suche = ""

    private var treffer: [Schueler] {
        let b = suche.trimmingCharacters(in: .whitespacesAndNewlines)
        return b.isEmpty ? alle : alle.filter { $0.anzeige.localizedCaseInsensitiveContains(b) }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Schüler*innen", untertitel: "\(gewaehlt.count) ausgewählt") {
            VStack(spacing: 0) {
                Suchleiste(text: $suche, platzhalter: "Suchen")
                List(treffer) { s in
                    Button {
                        if gewaehlt.contains(s.id) { gewaehlt.remove(s.id) } else { gewaehlt.insert(s.id) }
                    } label: {
                        HStack(spacing: 12) {
                            Image(systemName: gewaehlt.contains(s.id) ? "checkmark.circle.fill" : "circle")
                                .font(.system(size: 20, weight: .light))
                                .foregroundStyle(gewaehlt.contains(s.id) ? Farben.akzent : Farben.gedaempft)
                            Text(s.anzeige)
                                .font(.system(size: 16))
                                .foregroundStyle(Farben.tinte)
                                .lineLimit(1)
                            Spacer(minLength: 0)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .listRowBackground(Farben.blatt)
                    .accessibilityAddTraits(gewaehlt.contains(s.id) ? .isSelected : [])
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
            }
        }
    }
}

/// Auswahl der Eintragskategorie.
struct KategorieAuswahlView: View {
    let arten: [String]
    @Binding var auswahl: String
    @Environment(\.dismiss) private var zurueck

    var body: some View {
        DetailSeitenRahmen(titel: "Eintragskategorie") {
            List(arten, id: \.self) { art in
                Button {
                    auswahl = art
                    zurueck()
                } label: {
                    HStack {
                        Text(art)
                            .font(.system(size: 16))
                            .foregroundStyle(Farben.tinte)
                        Spacer(minLength: 0)
                        if art == auswahl {
                            Image(systemName: "checkmark")
                                .font(.system(size: 15, weight: .medium))
                                .foregroundStyle(Farben.akzent)
                        }
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .listRowBackground(Farben.blatt)
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
    }
}

// MARK: - Abwesenheiten einer Schülerin bzw. eines Schülers

/// Eintrag der Liste „Abwesenheiten“: Zeitraum, Grund, Text und die Fehlzeit-IDs (zum Ersetzen bzw. Löschen).
struct AbwesenheitsEintrag: Identifiable, Hashable {
    let id: String
    var vonDatum: String
    var vonZeit: String
    var bisDatum: String
    var bisZeit: String
    var grund: String
    var text: String
    var ids: [Int]
    var elternInformiert: Bool
    /// Fehlzeit dieser Stunde (Eintrag aus der Stunde ohne Zeitraum) – zum Entschuldigen
    var fehlzeitId: Int? = nil

    init(_ f: Fehlzeiteintrag, datum: String) {
        id = "stunde:\(f.schuelerId):\(f.id)"
        vonDatum = f.vonDatum.isEmpty ? datum : f.vonDatum
        vonZeit = f.vonZeit.isEmpty ? f.beginn : f.vonZeit
        bisDatum = f.bisDatum.isEmpty ? datum : f.bisDatum
        bisZeit = f.bisZeit.isEmpty ? f.ende : f.bisZeit
        grund = f.grund
        text = f.text
        ids = []
        elternInformiert = false
        fehlzeitId = f.id
    }

    init(_ z: Abwesenheitszeitraum) {
        id = z.schluessel
        vonDatum = z.vonDatum
        vonZeit = z.vonZeit
        bisDatum = z.bisDatum
        bisZeit = z.bisZeit
        grund = z.grund
        text = z.text
        ids = z.ids
        elternInformiert = z.elternInformiert
    }

    /// „8:00 - 11:57“ bzw. über mehrere Tage „Di., 08:00 - Mi., 11:57“.
    var zeitraum: String {
        if vonDatum == bisDatum || bisDatum.isEmpty {
            return "\(Detailmass.uhrzeit(vonZeit)) - \(Detailmass.uhrzeit(bisZeit))"
        }
        return "\(Datumsformat.anzeige(vonDatum, "EE")), \(vonZeit) - \(Datumsformat.anzeige(bisDatum, "EE")), \(bisZeit)"
    }

    /// „13.10.2026“ bzw. „13.10.2026 - 14.10.2026“.
    var datumstext: String {
        let von = Datumsformat.anzeige(vonDatum, "dd.MM.yyyy")
        if vonDatum == bisDatum || bisDatum.isEmpty { return von }
        return "\(von) - \(Datumsformat.anzeige(bisDatum, "dd.MM.yyyy"))"
    }
}

/// Vorlage „Abwesenheiten“: Kopf mit Zurück, Titel und Name, rechts „+“; je Abwesenheit Datum, Uhrzeit und Grund mit
/// Pfeil; unten das Suchfeld. Lehrkräfte sehen die Abwesenheit dieser Stunde, Klassenleitungen alle der letzten 30 Tage.
/// Nach links wischen öffnet den Entschuldigungsdialog (runde Pill „Entschuldigen“).
struct SchuelerAbwesenheitenView: View {
    let schueler: Schueler
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var zeitraeume: [Abwesenheitszeitraum] = []
    @State private var suche = ""
    @State private var bearbeiten: AbwesenheitsZiel? = nil
    @State private var entschuldigung: Entschuldigungsauftrag? = nil
    @State private var rechte = Entschuldigungsrechte()

    /// Ziel der Bearbeitung: neue Abwesenheit (eintrag nil) oder bestehende.
    struct AbwesenheitsZiel: Hashable, Identifiable {
        let id = UUID()
        let eintrag: AbwesenheitsEintrag?
    }

    private var eintraege: [AbwesenheitsEintrag] {
        var liste: [AbwesenheitsEintrag] = []
        let stunde = daten.fehlzeit(schueler)
        var stundeEnthalten = false
        for z in zeitraeume where z.schuelerId == schueler.id {
            if let f = stunde, z.ids.contains(f.id) { stundeEnthalten = true }
            liste.append(AbwesenheitsEintrag(z))
        }
        if let f = stunde, !stundeEnthalten {
            liste.insert(AbwesenheitsEintrag(f, datum: daten.karte.datum), at: 0)
        }
        let b = suche.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !b.isEmpty else { return liste }
        return liste.filter { e in
            [e.datumstext, e.zeitraum, daten.grundName(e.grund), e.text].contains { $0.localizedCaseInsensitiveContains(b) }
        }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Abwesenheiten", untertitel: schueler.anzeige) {
            VStack(spacing: 0) {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        if eintraege.isEmpty {
                            Text(suche.isEmpty ? "Keine Abwesenheit eingetragen." : "Keine Treffer.")
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.gedaempft)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 20)
                                .padding(.top, 14)
                        }
                        ForEach(eintraege) { e in
                            // Nach links wischen: runde Pill „Entschuldigen“ (Klassenleitung bzw. Fachlehrkraft)
                            WischZeile(rechtsMoeglich: false, linksMoeglich: entschuldigungsziel(e) != nil,
                                       rechts: {}, links: {
                                           if let z = entschuldigungsziel(e) {
                                               entschuldigung = Entschuldigungsauftrag(ziel: z, name: schueler.anzeige, klasse: schueler.klasse)
                                           }
                                       },
                                       linksAktion: .entschuldigen) {
                                Button {
                                    bearbeiten = AbwesenheitsZiel(eintrag: e)
                                } label: {
                                    zeile(e)
                                }
                                .buttonStyle(ZeilenKnopfStil())
                            }
                        }
                    }
                }
                .scrollIndicators(.hidden)
                Suchleiste(text: $suche, platzhalter: "Suchen")
                    .padding(.bottom, 6)
            }
        } rechts: {
            Button {
                bearbeiten = AbwesenheitsZiel(eintrag: nil)
            } label: {
                Rundknopf(symbol: "plus", gewicht: .light, groesse: 22)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Neue Abwesenheit")
        }
        .navigationDestination(item: $bearbeiten) { z in
            AbwesenheitBearbeitenView(schueler: schueler, eintrag: z.eintrag)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .detailPraesentation(item: $entschuldigung, breit: Geraet.istPad, art: .formular) { a in
            EntschuldigenView(auftrag: a) {
                Task {
                    await zeitraeumeLaden()
                    await daten.anwesenheitLaden()
                }
            }
            .environmentObject(zustand)
        }
        .task {
            rechte = await zustand.entschuldigungsrechteLaden()
            await daten.gruendeLaden()
            await zeitraeumeLaden()
        }
        .onChange(of: daten.anwesenheit?.fehlzeiten.count) { _, _ in
            Task { await zeitraeumeLaden() }
        }
    }

    private func zeile(_ e: AbwesenheitsEintrag) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .top, spacing: 12) {
                    Image(systemName: "clock")
                        .font(.system(size: 13, weight: .light))
                        .foregroundStyle(Farben.gedaempft)
                        .frame(width: 18)
                        .padding(.top, 2)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(e.datumstext)
                        Text(e.zeitraum)
                    }
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte)
                }
                let grund = [daten.grundName(e.grund), e.text].filter { !$0.isEmpty }.joined(separator: " – ")
                if !grund.isEmpty {
                    HStack(spacing: 12) {
                        Image(systemName: "info.circle")
                            .font(.system(size: 13, weight: .light))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(width: 18)
                        Text(grund)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(2)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.right")
                .font(.system(size: 19, weight: .ultraLight))
                .foregroundStyle(Farben.gedaempft)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Farben.trennlinie)
                .frame(height: 0.5)
                .padding(.horizontal, 16)
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    /// Was sich entschuldigen lässt: Klassenleitung die ganze Abwesenheit (Fehlzeit-IDs), Fachlehrkraft bzw. ein
    /// Eintrag nur aus dieser Stunde die Fehlzeit der Stunde (der Dialog sucht die passende Abwesenheit).
    private func entschuldigungsziel(_ e: AbwesenheitsEintrag) -> Entschuldigungsziel? {
        if rechte.leitet(schueler.klasse), !e.ids.isEmpty { return .ids(e.ids) }
        if let id = e.fehlzeitId ?? (e.ids.contains(daten.fehlzeit(schueler)?.id ?? -1) ? daten.fehlzeit(schueler)?.id : nil),
           rechte.leitet(schueler.klasse) || rechte.fachlehrer {
            return .fehlzeit(id: id, klasse: schueler.klasse, datum: daten.karte.datum)
        }
        return nil
    }

    /// Klassenleitung: Abwesenheiten der letzten 30 Tage (bis zum Tag der Stunde) als Zeiträume.
    private func zeitraeumeLaden() async {
        guard daten.anwesenheit?.verwalten == true || zustand.benutzer?.fehlzeiten == true else { return }
        let tag = Datumsformat.datum(daten.karte.datum) ?? Date()
        let von = Datumsformat.iso(Datumsformat.tage(-30, nach: tag))
        let bis = Datumsformat.iso(Datumsformat.tage(14, nach: tag))
        if let liste = try? await zustand.api.abwesenheitszeitraeume(klasse: schueler.klasse, von: von, bis: bis) {
            zeitraeume = liste.filter { $0.schuelerId == schueler.id }
        }
    }
}

// MARK: - Abwesenheit bearbeiten

/// Vorlage „Abwesenheit bearbeiten“: Kopf mit X, Titel, Papierkorb und Haken; Avatar mit Name und Klasse;
/// START und ENDE je Datum und Uhrzeit (minutengenau), ABWESENHEITSGRUND (Auswahl), TEXT FÜR ABWESENHEIT und der
/// Schalter „Erziehungsberechtigte benachrichtigt“. Lehrkräfte ohne Klassenleitung erfassen nur am Tag der Stunde.
struct AbwesenheitBearbeitenView: View {
    let schueler: Schueler
    let eintrag: AbwesenheitsEintrag?
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var zurueck
    @State private var von = Date()
    @State private var bis = Date()
    @State private var grund = ""
    @State private var text = ""
    @State private var eltern = false
    @State private var speichert = false
    @State private var fehler: String? = nil
    @State private var loeschenFragen = false
    @State private var bereit = false

    /// Klassenleitung: Zeitraum über den Tag hinaus, Benachrichtigung der Erziehungsberechtigten.
    private var verwalten: Bool { daten.anwesenheit?.verwalten == true }

    private var tag: Date { Datumsformat.datum(daten.karte.datum) ?? Date() }

    var body: some View {
        DetailSeitenRahmen(titel: eintrag == nil ? "Neue Abwesenheit" : "Abwesenheit bearbeiten", symbol: "xmark",
                           zurueck: { zurueck() }) {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopf
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 0.5)
                    zeitraum
                        .padding(.top, 14)
                    Feldtitel(text: "ABWESENHEITSGRUND")
                    NavigationLink {
                        GrundAuswahlView(gruende: daten.gruende, auswahl: $grund)
                    } label: {
                        HStack {
                            Text(grund.isEmpty ? "Kein Grund" : daten.grundName(grund))
                                .font(.system(size: 17))
                                .foregroundStyle(grund.isEmpty ? Farben.gedaempft : Farben.tinte)
                                .lineLimit(1)
                            Spacer(minLength: 0)
                            Image(systemName: "chevron.right")
                                .font(.system(size: 19, weight: .ultraLight))
                                .foregroundStyle(Farben.gedaempft)
                        }
                        .padding(.horizontal, 10)
                        .frame(height: 52)
                        .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .padding(.horizontal, 16)
                    Feldtitel(text: "TEXT FÜR ABWESENHEIT")
                    TextField("Text hier eingeben", text: $text, axis: .vertical)
                        .font(.system(size: 16))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(3...8)
                        .padding(10)
                        .frame(minHeight: 104, alignment: .topLeading)
                        .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                        .padding(.horizontal, 16)
                    Toggle(isOn: $eltern) {
                        Text("Erziehungsberechtigte benachrichtigt")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                    }
                    .tint(Farben.gruen)
                    .disabled(!verwalten)
                    .padding(.horizontal, 16)
                    .padding(.top, 18)
                    if !verwalten {
                        Text("Abwesenheiten über den Tag der Stunde hinaus und die Benachrichtigung trägt die Klassenleitung ein.")
                            .font(.system(size: 12))
                            .foregroundStyle(Farben.gedaempft)
                            .padding(.horizontal, 16)
                            .padding(.top, 6)
                    }
                    if let fehler {
                        Text(fehler)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.entfallRand)
                            .padding(16)
                    }
                }
                .padding(.bottom, 30)
            }
            .scrollDismissesKeyboard(.interactively)
        } rechts: {
            HStack(spacing: 6) {
                if eintrag != nil {
                    Button {
                        loeschenFragen = true
                    } label: {
                        Rundknopf(symbol: "trash", gewicht: .light, groesse: 18)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Abwesenheit löschen")
                }
                SichernKnopf(speichert: speichert, aktiv: bis > von) { Task { await sichern() } }
            }
        }
        .environment(\.locale, Locale(identifier: "de_DE"))
        .confirmationDialog("Abwesenheit löschen?", isPresented: $loeschenFragen, titleVisibility: .visible) {
            Button("Löschen", role: .destructive) { Task { await loeschen() } }
            Button("Abbrechen", role: .cancel) {}
        }
        .task {
            await daten.gruendeLaden()
        }
        .onAppear(perform: vorbelegen)
    }

    private var kopf: some View {
        HStack(spacing: 12) {
            Text(Initialen.kuerzel(schueler.anzeige))
                .font(.system(size: 17, weight: .regular))
                .foregroundStyle(.white)
                .frame(width: 44, height: 44)
                .background(Farben.avatar, in: Circle())
            VStack(alignment: .leading, spacing: 3) {
                Text(schueler.anzeige)
                    .font(.system(size: 19))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if !schueler.klasse.isEmpty {
                    Text(schueler.klasse)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(Farben.zahlPlakette.opacity(0.55), in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
    }

    /// START und ENDE: je eine Pille für Datum und Uhrzeit (minutengenau).
    private var zeitraum: some View {
        HStack(alignment: .top, spacing: 0) {
            VStack(alignment: .leading, spacing: 8) {
                Text("START")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                    .padding(.leading, 6)
                DatePicker("Startdatum", selection: $von, in: datumsbereich, displayedComponents: .date)
                    .labelsHidden()
                    .disabled(!verwalten)
                DatePicker("Startzeit", selection: $von, displayedComponents: .hourAndMinute)
                    .labelsHidden()
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            VStack(alignment: .leading, spacing: 8) {
                Text("ENDE")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                    .padding(.leading, 6)
                DatePicker("Enddatum", selection: $bis, in: datumsbereich, displayedComponents: .date)
                    .labelsHidden()
                    .disabled(!verwalten)
                DatePicker("Endzeit", selection: $bis, displayedComponents: .hourAndMinute)
                    .labelsHidden()
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 10)
    }

    /// Klassenleitung: bis zu 60 Tage; sonst nur der Tag der Stunde.
    private var datumsbereich: ClosedRange<Date> {
        if verwalten {
            return Datumsformat.tage(-60, nach: tag)...Datumsformat.tage(60, nach: tag)
        }
        let ende = Datumsformat.kalender.date(bySettingHour: 23, minute: 59, second: 0, of: tag) ?? tag
        return tag...ende
    }

    private func vorbelegen() {
        guard !bereit else { return }
        bereit = true
        if let e = eintrag {
            von = zeitpunkt(e.vonDatum, e.vonZeit, ersatz: daten.zeit(daten.blockBeginn))
            bis = zeitpunkt(e.bisDatum, e.bisZeit, ersatz: daten.zeit(daten.blockEnde))
            grund = e.grund
            text = e.text
            eltern = e.elternInformiert
        } else {
            von = daten.zeit(daten.blockBeginn)
            bis = daten.zeit(daten.blockEnde)
        }
    }

    private func zeitpunkt(_ datum: String, _ zeit: String, ersatz: Date) -> Date {
        guard let d = Datumsformat.datum(datum) else { return ersatz }
        let teile = zeit.split(separator: ":").compactMap { Int($0) }
        guard teile.count >= 2 else { return ersatz }
        return Datumsformat.kalender.date(bySettingHour: teile[0], minute: teile[1], second: 0, of: d) ?? ersatz
    }

    private func sichern() async {
        guard bis > von else {
            fehler = "Das Ende der Abwesenheit muss nach dem Beginn liegen."
            return
        }
        speichert = true
        defer { speichert = false }
        do {
            try await daten.abwesenheitSpeichern(schueler, von: von, bis: bis, grund: grund,
                                                 text: text.trimmingCharacters(in: .whitespacesAndNewlines),
                                                 eltern: eltern, ersetzen: eintrag?.ids)
            zurueck()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }

    private func loeschen() async {
        speichert = true
        defer { speichert = false }
        do {
            try await daten.abwesenheitLoeschen(schueler, ids: eintrag?.ids)
            zurueck()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Überschrift eines Felds in Großbuchstaben (START, ABWESENHEITSGRUND …).
struct Feldtitel: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 13))
            .foregroundStyle(Farben.tinte2)
            .padding(.horizontal, 24)
            .padding(.top, 18)
            .padding(.bottom, 6)
    }
}

/// Auswahl des Abwesenheitsgrunds.
struct GrundAuswahlView: View {
    let gruende: [Abwesenheitsgrund]
    @Binding var auswahl: String
    @Environment(\.dismiss) private var zurueck

    var body: some View {
        DetailSeitenRahmen(titel: "Abwesenheitsgrund") {
            List {
                zeile(kurz: "", name: "Kein Grund")
                ForEach(gruende) { g in
                    zeile(kurz: g.kurz, name: g.anzeige)
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
    }

    private func zeile(kurz: String, name: String) -> some View {
        Button {
            auswahl = kurz
            zurueck()
        } label: {
            HStack {
                Text(name)
                    .font(.system(size: 16))
                    .foregroundStyle(kurz.isEmpty ? Farben.gedaempft : Farben.tinte)
                Spacer(minLength: 0)
                if auswahl.caseInsensitiveCompare(kurz) == .orderedSame {
                    Image(systemName: "checkmark")
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(Farben.akzent)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .listRowBackground(Farben.blatt)
    }
}
