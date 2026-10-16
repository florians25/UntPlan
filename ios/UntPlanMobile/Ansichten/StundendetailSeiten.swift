import SwiftUI

// MARK: - Daten einer Stunde

/// Klassenbuch, Hausaufgaben, Anwesenheit und Klassenbucheinträge einer Stunde (für die Stundendetails).
/// Lädt nur, was die API für das Konto liefert; Fehler einzelner Teile lassen die Zeile ohne Zahl.
@MainActor
final class Stundendaten: ObservableObject {
    let karte: Karte
    let api = ApiClient.shared

    @Published var klassenbuch: KlassenbuchStunde? = nil
    @Published var hausaufgaben: HausaufgabenDerStunde? = nil
    @Published var anwesenheit: Anwesenheitsliste? = nil
    @Published var vermerke: [Vermerk] = []
    /// Klassendienste am Tag der Stunde je Schüler-ID (z. B. „Klassensprecher*in“)
    @Published var dienste: [Int: [String]] = [:]
    @Published var geladen = false

    nonisolated init(karte: Karte) {
        self.karte = karte
    }

    /// Klassen der Stunde (bei Änderung der ursprüngliche Wert, falls der neue leer ist).
    var klassen: [String] {
        karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
    }

    /// Lehrkraft der Karte für das Klassenbuch (nur bei genau einer Lehrkraft).
    var lehrerHinweis: String? {
        guard karte.lehrer.count == 1 else { return nil }
        let k = karte.lehrer[0].k
        return k.isEmpty ? nil : k
    }

    var eintrag: Klassenbucheintrag? { klassenbuch?.eintrag }

    /// Darf das Konto Lehrstoff, Notizen und Hausaufgaben dieser Stunde bearbeiten?
    var darf: Bool { klassenbuch?.darf == true || hausaufgaben?.darf == true }

    var notizSchueler: String {
        if let n = eintrag?.notizSchueler, !n.isEmpty { return n }
        return karte.notizSchueler
    }

    var notizLehrer: String {
        if let n = eintrag?.notiz, !n.isEmpty { return n }
        return karte.notizLehrer
    }

    var abwesendAnzahl: Int { anwesenheit?.fehlzeiten.count ?? 0 }

    func laden(kollegium: Bool) async {
        defer { geladen = true }
        guard let u = karte.unterricht else { return }
        if let k = try? await api.klassenbuchStunde(datum: karte.datum, stunde: karte.von, unterricht: u, lehrer: lehrerHinweis) {
            klassenbuch = k
        }
        await hausaufgabenLaden()
        guard kollegium else { return }
        await anwesenheitLaden()
        await diensteLaden()
        var liste: [Vermerk] = []
        for klasse in klassen {
            if let v = try? await api.vermerke(klasse: klasse, von: karte.datum, bis: karte.datum) {
                liste.append(contentsOf: v.vermerke)
            }
        }
        vermerke = liste
    }

    func hausaufgabenLaden() async {
        guard let u = karte.unterricht else { return }
        if let h = try? await api.hausaufgabenDerStunde(datum: karte.datum, stunde: karte.von, bis: karte.bis, unterricht: u,
                                                         fach: karte.fach, klassen: klassen) {
            hausaufgaben = h
        }
    }

    func anwesenheitLaden() async {
        guard let u = karte.unterricht else { return }
        if let a = try? await api.anwesenheit(datum: karte.datum, stunde: karte.von, unterricht: u) {
            anwesenheit = a
        }
    }

    func diensteLaden() async {
        var ergebnis: [Int: [String]] = [:]
        for klasse in klassen {
            guard let d = try? await api.klassendienste(klasse: klasse, von: karte.datum, bis: karte.datum) else { continue }
            for z in d.zuordnungen where !z.dienst.isEmpty {
                var liste = ergebnis[z.schuelerId] ?? []
                if !liste.contains(z.dienst) { liste.append(z.dienst) }
                ergebnis[z.schuelerId] = liste
            }
        }
        dienste = ergebnis
    }

    /// Lehrstoff bzw. Notizen speichern (nil = unverändert lassen).
    func speichern(thema: String? = nil, lehrform: String? = nil, anmerkung: String? = nil,
                   notiz: String? = nil, notizSchueler: String? = nil, online: Bool? = nil) async throws {
        guard let u = karte.unterricht else { return }
        let e = eintrag
        // Das alte Hausaufgabenfeld unverändert mitschicken, damit die Hausaufgabenliste nicht angeglichen wird
        let daten = KlassenbuchDaten(datum: karte.datum, stunde: karte.von, unterricht: u,
                                     thema: thema ?? e?.thema ?? "",
                                     hausaufgabe: e?.hausaufgabe ?? "",
                                     faellig: e?.faellig.map { String($0.prefix(10)) },
                                     notiz: notiz ?? e?.notiz ?? "",
                                     notizSchueler: notizSchueler,
                                     lehrform: lehrform,
                                     anmerkung: anmerkung,
                                     lehrer: lehrerHinweis,
                                     online: online)
        let neu = try await api.klassenbuchSpeichern(daten)
        klassenbuch = KlassenbuchStunde(eintrag: neu, darf: klassenbuch?.darf ?? true)
    }

    /// „Findet online statt“ – optimistisch umschalten, bei Fehler zurück.
    @Published var online: Bool? = nil

    var istOnline: Bool { online ?? eintrag?.online ?? karte.online }

    func onlineSetzen(_ wert: Bool) async throws {
        let vorher = istOnline
        online = wert
        do {
            try await speichern(online: wert)
            online = nil
        } catch {
            online = vorher
            throw error
        }
    }

    func hausaufgabeAnlegen(text: String, faellig: Date?, bemerkung: String) async throws {
        guard let u = karte.unterricht else { return }
        _ = try await api.hausaufgabeAnlegen(HausaufgabeNeuDaten(datum: karte.datum, stunde: karte.von, unterricht: u, text: text,
                                                                 faellig: faellig.map { Datumsformat.iso($0) }, bemerkung: bemerkung))
        await hausaufgabenLaden()
    }

    func hausaufgabeAendern(_ id: Int, text: String? = nil, faellig: Date? = nil, bemerkung: String? = nil, erledigt: Bool? = nil) async throws {
        _ = try await api.hausaufgabeAendern(id, HausaufgabeAenderDaten(text: text, faellig: faellig.map { Datumsformat.iso($0) },
                                                                       bemerkung: bemerkung, erledigt: erledigt))
        await hausaufgabenLaden()
    }

    func hausaufgabeLoeschen(_ id: Int) async throws {
        try await api.hausaufgabeLoeschen(id)
        await hausaufgabenLaden()
    }

    /// Schüler*in als abwesend bzw. anwesend markieren; die übrigen erfassten Fehlzeiten bleiben erhalten.
    func abwesenheitUmschalten(_ schueler: Schueler) async throws {
        guard let u = karte.unterricht, let liste = anwesenheit else { return }
        var eintraege = liste.fehlzeiten.filter { $0.schuelerId != schueler.id }.map {
            AnwesenheitEintragDaten(schuelerId: $0.schuelerId, art: $0.art, minuten: $0.minuten,
                                    bemerkung: $0.bemerkung.isEmpty ? nil : $0.bemerkung, grund: $0.grund.isEmpty ? nil : $0.grund)
        }
        let warAbwesend = liste.fehlzeiten.contains { $0.schuelerId == schueler.id }
        if !warAbwesend {
            eintraege.append(AnwesenheitEintragDaten(schuelerId: schueler.id, art: "fehlt", minuten: 0, bemerkung: nil, grund: nil))
        }
        try await api.anwesenheitSpeichern(AnwesenheitDaten(datum: karte.datum, stunde: karte.von, unterricht: u, eintraege: eintraege))
        await anwesenheitLaden()
    }
}

/// Unterseiten der Stundendetails.
enum Detailseite: Hashable {
    case text
    case lehrstoff
    case schueler
    case hausaufgaben
    case klassenbuch
    case notizSchueler
    case notizLehrer
}

/// Untertitel der Unterseiten: Datum und Uhrzeit der Stunde.
func stundenUntertitel(_ karte: Karte) -> String {
    var s = Datumsformat.anzeige(karte.datum, "dd.MM.yyyy")
    if !karte.beginn.isEmpty { s += ", \(karte.beginn) - \(karte.ende)" }
    return s
}

// MARK: - Text (Vertretungstext bzw. Hinweis)

struct DetailTextSeite: View {
    let titel: String
    let text: String
    let karte: Karte

    var body: some View {
        DetailSeitenRahmen(titel: titel, untertitel: stundenUntertitel(karte)) {
            ScrollView {
                Text(text.isEmpty ? "Kein Text." : text)
                    .font(.system(size: 16))
                    .foregroundStyle(text.isEmpty ? Farben.gedaempft : Farben.tinte)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(20)
            }
        }
    }
}

// MARK: - Notizen (bearbeitbar für Lehrkräfte)

struct NotizSeite: View {
    enum Art {
        case schueler
        case lehrer

        var titel: String {
            switch self {
            case .schueler: return "Notizen für Schüler*innen"
            case .lehrer: return "Notizen für Lehrkräfte"
            }
        }
    }

    let art: Art
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var text = ""
    @State private var anfang = ""
    @State private var speichert = false
    @State private var fehler: String? = nil
    @FocusState private var fokus: Bool
    @Environment(\.dismiss) private var zurueck

    private var bearbeitbar: Bool { daten.darf && daten.karte.unterricht != nil }

    /// Vorlage (Seite „Notizen“): X links, Titel mittig grau, darunter ein großer weißer Textbereich.
    var body: some View {
        DetailSeitenRahmen(titel: art.titel, symbol: "xmark", zurueck: { Task { await schliessen() } }) {
            textbereich
        } rechts: {
            if bearbeitbar {
                SichernKnopf(speichert: speichert, aktiv: text != anfang) { Task { await sichern() } }
            }
        }
        .onAppear {
            let wert = art == .schueler ? daten.notizSchueler : daten.notizLehrer
            text = wert
            anfang = wert
        }
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    /// Schließen: geänderte Notiz vorher sichern (bei Fehler bleibt die Seite offen).
    private func schliessen() async {
        if bearbeitbar && text != anfang {
            let ok = await sichern()
            guard ok else { return }
        }
        zurueck()
    }

    @ViewBuilder
    private var textbereich: some View {
        Group {
            if bearbeitbar {
                TextEditor(text: $text)
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.tinte)
                    .scrollContentBackground(.hidden)
                    .focused($fokus)
                    .padding(.horizontal, 15)
                    .padding(.top, 8)
                    .overlay(alignment: .topLeading) {
                        if text.isEmpty {
                            Text(art == .schueler ? "Notiz, die Schüler*innen sehen" : "Notiz nur für Lehrkräfte")
                                .font(.system(size: 16))
                                .foregroundStyle(Farben.gedaempft)
                                .padding(.horizontal, 20)
                                .padding(.top, 16)
                                .allowsHitTesting(false)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    Text(text.isEmpty ? "Keine Notiz." : text)
                        .font(.system(size: 16))
                        .foregroundStyle(text.isEmpty ? Farben.gedaempft : Farben.tinte)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(20)
                }
            }
        }
        .background(Farben.blatt)
    }

    @discardableResult
    private func sichern() async -> Bool {
        speichert = true
        defer { speichert = false }
        do {
            let wert = text.trimmingCharacters(in: .whitespacesAndNewlines)
            if art == .schueler {
                try await daten.speichern(notizSchueler: wert)
            } else {
                try await daten.speichern(notiz: wert)
            }
            text = wert
            anfang = wert
            fokus = false
            return true
        } catch {
            fehler = zustand.fehlertext(error)
            return false
        }
    }
}

// MARK: - Lehrstoff

struct LehrstoffSeite: View {
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var thema = ""
    @State private var lehrform = ""
    @State private var anmerkung = ""
    @State private var gespeichert = ["", "", ""]
    @State private var speichert = false
    @State private var fehler: String? = nil

    private var bearbeitbar: Bool { daten.darf && daten.karte.unterricht != nil }

    private var geaendert: Bool { [thema, lehrform, anmerkung] != gespeichert }

    var body: some View {
        DetailSeitenRahmen(titel: "Lehrstoff", untertitel: stundenUntertitel(daten.karte)) {
            formular
        } rechts: {
            if bearbeitbar {
                SichernKnopf(speichert: speichert, aktiv: geaendert) { Task { await sichern() } }
            }
        }
        .onAppear {
            thema = daten.eintrag?.thema ?? ""
            lehrform = daten.eintrag?.lehrform ?? ""
            anmerkung = daten.eintrag?.anmerkung ?? ""
            gespeichert = [thema, lehrform, anmerkung]
        }
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private var formular: some View {
        Form {
            if bearbeitbar {
                Section("Lehrstoff") {
                    TextField("Thema der Stunde", text: $thema, axis: .vertical)
                        .lineLimit(3...10)
                }
                Section("Lehrform") {
                    TextField("z. B. Gruppenarbeit", text: $lehrform)
                }
                Section("Anmerkung") {
                    TextField("Anmerkung", text: $anmerkung, axis: .vertical)
                        .lineLimit(2...6)
                }
            } else {
                Section("Lehrstoff") {
                    Text(thema.isEmpty ? "Noch kein Lehrstoff eingetragen." : thema)
                        .foregroundStyle(thema.isEmpty ? Farben.gedaempft : Farben.tinte)
                        .textSelection(.enabled)
                }
                if !lehrform.isEmpty {
                    Section("Lehrform") { Text(lehrform) }
                }
                if !anmerkung.isEmpty {
                    Section("Anmerkung") { Text(anmerkung) }
                }
            }
            if let e = daten.eintrag, !e.hausaufgabe.isEmpty {
                Section("Hausaufgabe (Klassenbuch)") {
                    Text(e.hausaufgabe)
                }
            }
        }
    }

    private func sichern() async {
        speichert = true
        defer { speichert = false }
        do {
            thema = thema.trimmingCharacters(in: .whitespacesAndNewlines)
            lehrform = lehrform.trimmingCharacters(in: .whitespacesAndNewlines)
            anmerkung = anmerkung.trimmingCharacters(in: .whitespacesAndNewlines)
            try await daten.speichern(thema: thema, lehrform: lehrform, anmerkung: anmerkung)
            gespeichert = [thema, lehrform, anmerkung]
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

// MARK: - Hausaufgaben

struct HausaufgabenSeite: View {
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var formular: HausaufgabeFormular.Vorlage? = nil
    @State private var fehler: String? = nil

    private var bearbeitbar: Bool { daten.hausaufgaben?.darf == true }

    var body: some View {
        DetailSeitenRahmen(titel: "Hausaufgaben", untertitel: stundenUntertitel(daten.karte)) {
            hausaufgabenliste
        } rechts: {
            if bearbeitbar {
                Button {
                    formular = HausaufgabeFormular.Vorlage(eintrag: nil, stundenDatum: daten.karte.datum)
                } label: {
                    Rundknopf(symbol: "plus", gewicht: .regular, groesse: 16)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Neue Hausaufgabe")
            }
        }
        .sheet(item: $formular) { vorlage in
            HausaufgabeFormular(vorlage: vorlage) { text, faellig, bemerkung in
                if let id = vorlage.eintrag?.id {
                    try await daten.hausaufgabeAendern(id, text: text, faellig: faellig, bemerkung: bemerkung)
                } else {
                    try await daten.hausaufgabeAnlegen(text: text, faellig: faellig, bemerkung: bemerkung)
                }
            }
            .environmentObject(zustand)
        }
        .alert("Fehler", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private var hausaufgabenliste: some View {
        List {
            Section {
                let liste = daten.hausaufgaben?.liste ?? []
                if liste.isEmpty {
                    Text("Keine Hausaufgaben aufgegeben.")
                        .foregroundStyle(Farben.gedaempft)
                }
                ForEach(liste) { ha in
                    zeile(ha)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            if bearbeitbar { formular = HausaufgabeFormular.Vorlage(eintrag: ha, stundenDatum: daten.karte.datum) }
                        }
                        .swipeActions(edge: .trailing) {
                            if bearbeitbar {
                                Button(role: .destructive) {
                                    Task { await ausfuehren { try await daten.hausaufgabeLoeschen(ha.id) } }
                                } label: {
                                    Label("Löschen", systemImage: "trash")
                                }
                            }
                        }
                }
                if bearbeitbar {
                    Button {
                        formular = HausaufgabeFormular.Vorlage(eintrag: nil, stundenDatum: daten.karte.datum)
                    } label: {
                        Label("Neue Hausaufgabe", systemImage: "plus.circle.fill")
                    }
                }
            } header: {
                Text("In dieser Stunde aufgegeben")
            }
            let faellig = daten.hausaufgaben?.faellig ?? []
            if !faellig.isEmpty {
                Section("Zu dieser Stunde fällig") {
                    ForEach(faellig) { ha in
                        zeile(ha)
                    }
                }
            }
        }
        .listStyle(.plain)
        .refreshable { await daten.hausaufgabenLaden() }
    }

    private func zeile(_ ha: Hausaufgabeneintrag) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Button {
                Task { await ausfuehren { try await daten.hausaufgabeAendern(ha.id, erledigt: !ha.erledigt) } }
            } label: {
                Image(systemName: ha.erledigt ? "checkmark.circle.fill" : "circle")
                    .font(.system(size: 20))
                    .foregroundStyle(ha.erledigt ? Farben.gruen : Farben.gedaempft)
            }
            .buttonStyle(.borderless)
            .disabled(!bearbeitbar)
            .accessibilityLabel(ha.erledigt ? "Erledigt" : "Nicht erledigt")
            VStack(alignment: .leading, spacing: 3) {
                Text(ha.text)
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.tinte)
                if let f = ha.faellig, !f.isEmpty {
                    Text("fällig \(Datumsformat.anzeige(f, "EE d.M.yyyy"))")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(Farben.heute)
                }
                if !ha.bemerkung.isEmpty {
                    Text(ha.bemerkung)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                }
                if !ha.fach.isEmpty || !ha.lehrer.isEmpty {
                    Text([ha.fach, ha.lehrer].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.gedaempft)
                }
            }
        }
        .padding(.vertical, 2)
    }

    private func ausfuehren(_ aktion: () async throws -> Void) async {
        do {
            try await aktion()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Formular für eine neue bzw. geänderte Hausaufgabe (Text, Fälligkeit, Bemerkung).
struct HausaufgabeFormular: View {
    struct Vorlage: Identifiable {
        let id = UUID()
        let eintrag: Hausaufgabeneintrag?
        let stundenDatum: String
    }

    let vorlage: Vorlage
    let sichern: (String, Date?, String) async throws -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen
    @State private var text = ""
    @State private var mitFaellig = true
    @State private var faellig = Date()
    @State private var bemerkung = ""
    @State private var speichert = false
    @State private var fehler: String? = nil

    private var stunde: Date { Datumsformat.datum(vorlage.stundenDatum) ?? Date() }

    var body: some View {
        NavigationStack {
            Form {
                Section("Hausaufgabe") {
                    TextField("Was ist zu tun?", text: $text, axis: .vertical)
                        .lineLimit(3...8)
                }
                Section {
                    Toggle("Fälligkeit", isOn: $mitFaellig)
                    if mitFaellig {
                        DatePicker("Fällig am", selection: $faellig, in: stunde..., displayedComponents: .date)
                            .environment(\.locale, Locale(identifier: "de_DE"))
                    }
                }
                Section("Bemerkung") {
                    TextField("Bemerkung", text: $bemerkung, axis: .vertical)
                        .lineLimit(1...4)
                }
                if let fehler {
                    Section {
                        Text(fehler)
                            .foregroundStyle(Farben.entfallRand)
                    }
                }
            }
            .navigationTitle(vorlage.eintrag == nil ? "Neue Hausaufgabe" : "Hausaufgabe")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { schliessen() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    if speichert {
                        ProgressView()
                    } else {
                        Button("Sichern") { Task { await speichern() } }
                            .fontWeight(.semibold)
                            .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                }
            }
            .onAppear {
                if let e = vorlage.eintrag {
                    text = e.text
                    bemerkung = e.bemerkung
                    if let f = e.faellig, let d = Datumsformat.datum(f) {
                        faellig = d
                        mitFaellig = true
                    } else {
                        mitFaellig = false
                    }
                } else {
                    faellig = Datumsformat.tage(7, nach: stunde)
                }
            }
        }
        .presentationDetents([.medium, .large])
    }

    private func speichern() async {
        speichert = true
        defer { speichert = false }
        do {
            try await sichern(text.trimmingCharacters(in: .whitespacesAndNewlines), mitFaellig ? faellig : nil,
                              bemerkung.trimmingCharacters(in: .whitespacesAndNewlines))
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

// MARK: - Klassenbucheinträge

struct KlassenbuchSeite: View {
    @EnvironmentObject var daten: Stundendaten

    private func schuelerName(_ id: Int?) -> String? {
        guard let id else { return nil }
        return daten.anwesenheit?.schueler.first { $0.id == id }?.anzeige
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Klassenbucheinträge", untertitel: stundenUntertitel(daten.karte)) {
            liste
        }
    }

    private var liste: some View {
        List {
            if daten.vermerke.isEmpty {
                Text("Keine Klassenbucheinträge an diesem Tag.")
                    .foregroundStyle(Farben.gedaempft)
            }
            ForEach(daten.vermerke) { v in
                VStack(alignment: .leading, spacing: 3) {
                    HStack {
                        Text(v.art.isEmpty ? "Vermerk" : v.art)
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundStyle(Farben.tinte)
                        Spacer()
                        Text([v.zeit, v.lehrer].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.system(size: 12))
                            .foregroundStyle(Farben.gedaempft)
                    }
                    if let name = schuelerName(v.schuelerId) {
                        Text(name)
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Farben.tinte2)
                    } else if !v.klasse.isEmpty {
                        Text("Klasse \(v.klasse)")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if !v.text.isEmpty {
                        Text(v.text)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                    }
                }
                .padding(.vertical, 2)
            }
        }
        .listStyle(.plain)
    }
}

// MARK: - Schüler*innen (Anwesenheit)

/// Vorlage (Seite „Schüler*innen“): Kopf mit Zurück, Titel und Datum mittig, rechts Klassendienste; darunter das Segment
/// Alle | Abwesend | Anwesend, der Hinweis zur Kontrolle, Abschnitte nach Anfangsbuchstaben mit A–Z-Leiste rechts.
/// Jede Zeile: grüner Haken-Kreis (antippen = abwesend), Name, darunter Klassendienst, rechts Buch und Uhr.
struct SchuelerSeite: View {
    enum Filter: Hashable {
        case alle
        case abwesend
        case anwesend
    }

    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var filter: Filter = .alle
    @State private var speichert: Int? = nil
    @State private var fehler: String? = nil

    private var liste: Anwesenheitsliste? { daten.anwesenheit }

    private func fehlzeit(_ s: Schueler) -> Fehlzeiteintrag? {
        liste?.fehlzeiten.first { $0.schuelerId == s.id }
    }

    /// Kontrolle erst ab dem Tag der Stunde möglich.
    private var kontrolleAb: Date? {
        guard let tag = Datumsformat.datum(daten.karte.datum) else { return nil }
        return tag > Datumsformat.kalender.startOfDay(for: Date()) ? tag : nil
    }

    private var bearbeitbar: Bool { liste?.darf == true && kontrolleAb == nil }

    private var gefiltert: [Schueler] {
        let alle = liste?.schueler ?? []
        switch filter {
        case .alle: return alle
        case .abwesend: return alle.filter { fehlzeit($0) != nil }
        case .anwesend: return alle.filter { fehlzeit($0) == nil }
        }
    }

    private struct Gruppe: Identifiable {
        let buchstabe: String
        var liste: [Schueler]
        var id: String { buchstabe }
    }

    private var gruppen: [Gruppe] {
        var ergebnis: [Gruppe] = []
        for s in gefiltert {
            let b = (s.name.isEmpty ? s.vorname : s.name).first.map { String($0).uppercased() } ?? "#"
            if let letzte = ergebnis.last, letzte.buchstabe == b {
                ergebnis[ergebnis.count - 1].liste.append(s)
            } else {
                ergebnis.append(Gruppe(buchstabe: b, liste: [s]))
            }
        }
        return ergebnis
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Schüler*innen", untertitel: stundenUntertitel(daten.karte)) {
            inhalt
        } rechts: {
            diensteMenue
        }
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private var inhalt: some View {
        let alle = liste?.schueler ?? []
        let abwesend = alle.filter { fehlzeit($0) != nil }.count
        return VStack(spacing: 0) {
            Picker("Filter", selection: $filter) {
                Text("Alle (\(alle.count))").tag(Filter.alle)
                Text("Abwesend (\(abwesend))").tag(Filter.abwesend)
                Text("Anwesend (\(alle.count - abwesend))").tag(Filter.anwesend)
            }
            .pickerStyle(.segmented)
            .padding(.horizontal, 14)
            .padding(.top, 2)
            .padding(.bottom, 8)
            if let hinweis = hinweistext {
                Text(hinweis)
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(Farben.tinte2)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
                    .padding(.horizontal, 12)
                    .background(Farben.infoFeld)
            }
            if liste == nil {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    List {
                        ForEach(gruppen) { gruppe in
                            Section {
                                ForEach(gruppe.liste) { s in
                                    zeile(s)
                                        .listRowInsets(EdgeInsets(top: 0, leading: 16, bottom: 0, trailing: 30))
                                        .listRowBackground(Farben.blatt)
                                }
                            } header: {
                                Text(gruppe.buchstabe)
                                    .font(.system(size: 12, weight: .regular))
                                    .foregroundStyle(Farben.gedaempft)
                            }
                            .id(gruppe.buchstabe)
                        }
                    }
                    .listStyle(.plain)
                    .scrollContentBackground(.hidden)
                    .background(Farben.blatt)
                    .overlay(alignment: .trailing) {
                        buchstabenleiste(proxy)
                    }
                    .refreshable { await daten.anwesenheitLaden() }
                }
            }
        }
    }

    /// Klassendienste des Tages (Symbol oben rechts).
    @ViewBuilder
    private var diensteMenue: some View {
        let alle = liste?.schueler ?? []
        let mitDienst = alle.filter { !(daten.dienste[$0.id] ?? []).isEmpty }
        Menu {
            Section("Klassendienste") {
                if mitDienst.isEmpty {
                    Button {} label: { Text("Keine Klassendienste eingetragen") }
                        .disabled(true)
                }
                ForEach(mitDienst) { s in
                    Button {} label: {
                        Label("\(s.anzeige) – \((daten.dienste[s.id] ?? []).joined(separator: ", "))", systemImage: "person")
                    }
                    .disabled(true)
                }
            }
        } label: {
            Rundknopf(symbol: "pencil", gewicht: .light, groesse: 15)
        }
        .accessibilityLabel("Klassendienste")
    }

    private var hinweistext: String? {
        if let ab = kontrolleAb {
            let zeit = daten.karte.beginn.isEmpty ? "" : " - \(daten.karte.beginn)"
            return "Kontrolle der Abwesenheiten ist ab \(Datumsformat.anzeige(ab, "dd.MM.yyyy"))\(zeit) möglich"
        }
        if let geprueft = liste?.geprueft, !geprueft.isEmpty {
            return "Abwesenheiten geprüft (\(Datumsformat.anzeige(geprueft, "d.M.yyyy, HH:mm")))"
        }
        if liste?.darf == false {
            return "Nur Ansicht – die Stunde gehört nicht zu Ihrem Unterricht."
        }
        return nil
    }

    private func untertitel(_ s: Schueler, _ f: Fehlzeiteintrag?) -> String? {
        var teile: [String] = []
        let dienste = daten.dienste[s.id] ?? []
        if !dienste.isEmpty { teile.append(dienste.joined(separator: ", ")) }
        if let f { teile.append(fehlzeittext(f)) }
        if teile.isEmpty, (liste?.schueler.contains { $0.klasse != s.klasse }) == true, !s.klasse.isEmpty {
            teile.append(s.klasse)
        }
        return teile.isEmpty ? nil : teile.joined(separator: " · ")
    }

    private func zeile(_ s: Schueler) -> some View {
        let f = fehlzeit(s)
        let verspaetet = f?.art == "verspaetet" || f?.art == "vorzeitig"
        let hatVermerk = daten.vermerke.contains { $0.schuelerId == s.id }
        return HStack(spacing: 12) {
            Button {
                Task { await umschalten(s) }
            } label: {
                Group {
                    if speichert == s.id {
                        ProgressView()
                    } else if let f {
                        Image(systemName: f.art == "fehlt" ? "xmark.circle" : "clock.badge.exclamationmark")
                            .foregroundStyle(f.art == "fehlt" ? Farben.entfallRand : Farben.orange)
                    } else {
                        Image(systemName: "checkmark.circle")
                            .foregroundStyle(Farben.gruen)
                    }
                }
                .font(.system(size: 21, weight: .light))
                .frame(width: 28, height: 28)
            }
            .buttonStyle(.borderless)
            .disabled(!bearbeitbar || speichert != nil)
            .accessibilityLabel(f == nil ? "Anwesend" : "Abwesend")
            .accessibilityHint(bearbeitbar ? "Doppeltippen zum Umschalten" : "")

            VStack(alignment: .leading, spacing: 2) {
                Text(s.anzeige)
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if let u = untertitel(s, f) {
                    Text(u)
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            Image(systemName: hatVermerk ? "book.closed.fill" : "book.closed")
                .font(.system(size: 18, weight: .light))
                .foregroundStyle(hatVermerk ? Farben.tinte2 : Farben.gedaempft)
                .accessibilityLabel(hatVermerk ? "Klassenbucheintrag vorhanden" : "Kein Klassenbucheintrag")
            Image(systemName: verspaetet ? "clock.fill" : "clock")
                .font(.system(size: 18, weight: .light))
                .foregroundStyle(verspaetet ? Farben.orange : Farben.gedaempft)
                .accessibilityLabel(verspaetet ? "Verspätung bzw. vorzeitig gegangen" : "Pünktlich")
        }
        .frame(minHeight: 50)
    }

    private func fehlzeittext(_ f: Fehlzeiteintrag) -> String {
        var teile: [String] = []
        switch f.art {
        case "verspaetet": teile.append("verspätet \(f.minuten) min")
        case "vorzeitig": teile.append("vorzeitig gegangen \(f.minuten) min")
        default: teile.append("abwesend")
        }
        if !f.grund.isEmpty { teile.append(f.grund) }
        if !f.bemerkung.isEmpty { teile.append(f.bemerkung) }
        return teile.joined(separator: " · ")
    }

    /// Buchstabenleiste A–Z am rechten Rand.
    private func buchstabenleiste(_ proxy: ScrollViewProxy) -> some View {
        VStack(spacing: 1) {
            ForEach(gruppen) { gruppe in
                Button {
                    withAnimation { proxy.scrollTo(gruppe.buchstabe, anchor: .top) }
                } label: {
                    Text(gruppe.buchstabe)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(Farben.tinte2)
                        .frame(width: 16, height: 14)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.trailing, 3)
        .accessibilityHidden(true)
    }

    private func umschalten(_ s: Schueler) async {
        speichert = s.id
        defer { speichert = nil }
        do {
            try await daten.abwesenheitUmschalten(s)
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}
