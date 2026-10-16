import SwiftUI
import UIKit

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
    /// Eintragskategorien der Klassenbucheinträge (vom Server, sonst Vorgabe)
    @Published var vermerkArten: [String] = ["Vermerk", "Ordnungsmaßnahme", "Elterngespräch", "Lob", "Material vergessen", "Hausaufgabe fehlt"]
    /// Abwesenheitsgründe (beim ersten Bedarf geladen)
    @Published var gruende: [Abwesenheitsgrund] = []
    /// Räume nach einer Raumänderung in den Details (nil = wie auf der Karte)
    @Published var raeume: [Wert]? = nil
    /// Kurze Meldung unten im Blatt (z. B. „Raum C197 hinzugefügt“)
    @Published var hinweis: String? = nil
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

    /// Angezeigte Räume der Stunde (nach einer Raumänderung die neuen).
    var aktuelleRaeume: [Wert] { raeume ?? karte.raeume }

    /// Raum buchen bzw. ändern: eigene Stunde mit Unterricht, nicht entfallen, ab heute.
    var raumAenderbar: Bool {
        guard darf, karte.unterricht != nil, !karte.istEntfall, let tag = Datumsformat.datum(karte.datum) else { return false }
        return tag >= Datumsformat.kalender.startOfDay(for: Date())
    }

    /// Erfasste Fehlzeit einer Schülerin bzw. eines Schülers in dieser Stunde.
    func fehlzeit(_ s: Schueler) -> Fehlzeiteintrag? {
        anwesenheit?.fehlzeiten.first { $0.schuelerId == s.id }
    }

    /// In einer früheren Stunde desselben Tages abwesend, hier (noch) nicht eingetragen?
    func inVorstundeAbwesend(_ s: Schueler) -> Bool {
        fehlzeit(s) == nil && (anwesenheit?.vorstunden.contains { $0.schuelerId == s.id } ?? false)
    }

    /// Beginn und Ende des Blocks (HH:mm).
    var blockBeginn: String {
        let b = anwesenheit?.beginn ?? ""
        return b.isEmpty ? karte.beginn : b
    }

    var blockEnde: String {
        let e = anwesenheit?.ende ?? ""
        return e.isEmpty ? karte.ende : e
    }

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
        await vermerkeLaden()
    }

    func vermerkeLaden() async {
        var liste: [Vermerk] = []
        for klasse in klassen {
            if let v = try? await api.vermerke(klasse: klasse, von: karte.datum, bis: karte.datum) {
                liste.append(contentsOf: v.vermerke)
                if !v.arten.isEmpty { vermerkArten = v.arten }
            }
        }
        vermerke = liste
    }

    func gruendeLaden() async {
        guard gruende.isEmpty else { return }
        if let g = try? await api.abwesenheitsgruende() { gruende = g }
    }

    /// Bezeichnung eines Abwesenheitsgrunds (Kürzel → Name).
    func grundName(_ kurz: String) -> String {
        guard !kurz.isEmpty else { return "" }
        return gruende.first { $0.kurz.caseInsensitiveCompare(kurz) == .orderedSame }?.anzeige ?? kurz
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

    // MARK: Anwesenheit und Abwesenheiten

    /// „yyyy-MM-ddTHH:mm:ss“ für den Abwesenheitsdialog.
    static func zeitpunkt(_ d: Date) -> String {
        Datumsformat.anzeige(d, "yyyy-MM-dd'T'HH:mm:ss")
    }

    /// Datum der Stunde mit Uhrzeit (HH:mm).
    func zeit(_ hhmm: String) -> Date {
        let tag = Datumsformat.datum(karte.datum) ?? Date()
        let teile = hhmm.split(separator: ":").compactMap { Int($0) }
        guard teile.count >= 2 else { return tag }
        return Datumsformat.kalender.date(bySettingHour: teile[0], minute: teile[1], second: 0, of: tag) ?? tag
    }

    /// Wischgeste nach rechts: anwesend – die Abwesenheit in dieser Stunde wird entfernt.
    func anwesendSetzen(_ s: Schueler) async throws {
        guard let u = karte.unterricht, fehlzeit(s) != nil else { return }
        try await api.abwesenheitEntfernen(AbwesenheitEntfernenDaten(datum: karte.datum, stunde: karte.von, unterricht: u, schuelerIds: [s.id]))
        await anwesenheitLaden()
    }

    /// Wischgeste nach links: abwesend für den ganzen Block (minutengenau von Beginn bis Ende).
    func abwesendSetzen(_ s: Schueler) async throws {
        guard let u = karte.unterricht, fehlzeit(s) == nil else { return }
        try await api.abwesenheitErfassen(AbwesenheitZeitraumDaten(
            schuelerIds: [s.id], von: Self.zeitpunkt(zeit(blockBeginn)), bis: Self.zeitpunkt(zeit(blockEnde)),
            grund: nil, text: nil, datum: karte.datum, stunde: karte.von, unterricht: u))
        await anwesenheitLaden()
    }

    /// Abwesenheit erfassen bzw. ändern (Abwesenheitsdialog). `ersetzen`: Fehlzeit-IDs der bisherigen Abwesenheit.
    func abwesenheitSpeichern(_ s: Schueler, von: Date, bis: Date, grund: String, text: String, eltern: Bool, ersetzen: [Int]?) async throws {
        guard let u = karte.unterricht else { return }
        try await api.abwesenheitErfassen(AbwesenheitZeitraumDaten(
            schuelerIds: [s.id], von: Self.zeitpunkt(von), bis: Self.zeitpunkt(bis),
            grund: grund, text: text, datum: karte.datum, stunde: karte.von, unterricht: u,
            ersetzen: (ersetzen ?? []).isEmpty ? nil : ersetzen, elternInformiert: eltern))
        await anwesenheitLaden()
    }

    /// Abwesenheit löschen: ganze Abwesenheit (Klassenleitung, IDs) bzw. die Abwesenheit in dieser Stunde.
    func abwesenheitLoeschen(_ s: Schueler, ids: [Int]?) async throws {
        if let ids, !ids.isEmpty, anwesenheit?.verwalten == true {
            try await api.abwesenheitenLoeschen(ids)
        } else if let u = karte.unterricht {
            try await api.abwesenheitEntfernen(AbwesenheitEntfernenDaten(datum: karte.datum, stunde: karte.von, unterricht: u, schuelerIds: [s.id]))
        }
        await anwesenheitLaden()
    }

    /// Leiste „Abwesenheiten kontrollieren“: die erfassten Fehlzeiten unverändert speichern – die Stunde gilt als kontrolliert.
    func kontrolleBestaetigen() async throws {
        guard let u = karte.unterricht, let liste = anwesenheit else { return }
        let eintraege = liste.fehlzeiten.map { f -> AnwesenheitEintragDaten in
            let minutengenau = f.art != "fehlt" && !f.beginn.isEmpty && !f.ende.isEmpty
            return AnwesenheitEintragDaten(schuelerId: f.schuelerId, art: f.art, minuten: f.minuten,
                                           bemerkung: f.text.isEmpty ? nil : f.text, grund: f.grund.isEmpty ? nil : f.grund,
                                           von: minutengenau ? f.beginn : nil, bis: minutengenau ? f.ende : nil)
        }
        try await api.anwesenheitSpeichern(AnwesenheitDaten(datum: karte.datum, stunde: karte.von, unterricht: u, eintraege: eintraege))
        await anwesenheitLaden()
    }

    /// Neuer Klassenbucheintrag zu den gewählten Schüler*innen (je Person ein Eintrag) bzw. zur Klasse.
    func vermerkAnlegen(schueler: [Schueler], art: String, text: String) async throws {
        let zeit = karte.beginn.isEmpty ? nil : karte.beginn
        if schueler.isEmpty {
            for klasse in klassen.prefix(1) {
                try await api.vermerkAnlegen(VermerkNeuDaten(datum: karte.datum, klasse: klasse, schuelerId: nil, art: art, text: text, zeit: zeit))
            }
        } else {
            for s in schueler {
                let klasse = s.klasse.isEmpty ? (klassen.first ?? "") : s.klasse
                try await api.vermerkAnlegen(VermerkNeuDaten(datum: karte.datum, klasse: klasse, schuelerId: s.id, art: art, text: text, zeit: zeit))
            }
        }
        await vermerkeLaden()
    }

    // MARK: Raumbuchung

    /// Raum ersetzen, hinzufügen oder löschen (wie in der PWA); liefert die Meldung für die Anzeige.
    func raumAendern(aktion: String, raum: String, alt: String?) async throws -> String {
        guard let u = karte.unterricht else { return "" }
        let antwort = try await api.raumAendern(RaumAenderungDaten(
            datum: karte.datum, stundeVon: karte.von, stundeBis: karte.bis, unterricht: u,
            klassen: klassen, aktion: aktion, raum: raum, alt: alt))
        let original = karte.raeume.map { $0.alt.flatMap { $0.isEmpty ? nil : $0 } ?? $0.k }.filter { !$0.isEmpty }
        if antwort.zurueckgesetzt {
            raeume = nil
        } else {
            func enthaelt(_ liste: [String], _ x: String) -> Bool { liste.contains { $0.caseInsensitiveCompare(x) == .orderedSame } }
            var weg = original.filter { !enthaelt(antwort.raeume, $0) }
            var neu: [Wert] = []
            for r in antwort.raeume {
                if enthaelt(original, r) {
                    neu.append(Wert(k: r))
                } else if !weg.isEmpty {
                    neu.append(Wert(k: r, alt: weg.removeFirst(), neu: true))
                } else {
                    neu.append(Wert(k: r, alt: nil, neu: true))
                }
            }
            for o in weg { neu.append(Wert(k: "", alt: o, neu: true)) }
            raeume = neu
        }
        switch aktion {
        case "loeschen": return "Raum \(raum) gelöscht"
        case "hinzufuegen": return "Raum \(raum) hinzugefügt"
        default: return "\(alt ?? "") durch \(raum) ersetzt"
        }
    }
}

/// Unterseiten der Stundendetails.
enum Detailseite: Hashable {
    case text
    case info
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
        /// Lehrstoff: eine einfache Textfläche wie in der Vorlage
        case lehrstoff

        var titel: String {
            switch self {
            case .schueler: return "Notizen für Schüler*innen"
            case .lehrer: return "Notizen für Lehrkräfte"
            case .lehrstoff: return "Lehrstoff"
            }
        }

        var platzhalter: String {
            switch self {
            case .schueler: return "Notiz, die Schüler*innen sehen"
            case .lehrer: return "Notiz nur für Lehrkräfte"
            case .lehrstoff: return "Hier klicken um Lehrstoff einzutragen."
            }
        }

        var leer: String {
            switch self {
            case .lehrstoff: return "Noch kein Lehrstoff eingetragen."
            default: return "Keine Notiz."
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
    /// Teil des Textbereichs, den die Tastatur verdeckt – das Blatt bleibt fest, nur der Text rückt darüber
    @State private var verdeckt: CGFloat = 0
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
            let wert: String
            switch art {
            case .schueler: wert = daten.notizSchueler
            case .lehrer: wert = daten.notizLehrer
            case .lehrstoff: wert = daten.eintrag?.thema ?? ""
            }
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
                GeometryReader { geo in
                    TextEditor(text: $text)
                        .font(.system(size: 16))
                        .foregroundStyle(Farben.tinte)
                        .scrollContentBackground(.hidden)
                        .focused($fokus)
                        .padding(.horizontal, 15)
                        .padding(.top, 8)
                        .overlay(alignment: .topLeading) {
                            if text.isEmpty {
                                Text(art.platzhalter)
                                    .font(.system(size: 16))
                                    .foregroundStyle(Farben.gedaempft)
                                    .padding(.horizontal, 20)
                                    .padding(.top, 16)
                                    .allowsHitTesting(false)
                            }
                        }
                        .padding(.bottom, verdeckt)
                        .frame(width: geo.size.width, height: geo.size.height, alignment: .top)
                        .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillChangeFrameNotification)) { meldung in
                            guard let rahmen = meldung.userInfo?[UIResponder.keyboardFrameEndUserInfoKey] as? CGRect else { return }
                            let unten = geo.frame(in: .global).maxY
                            verdeckt = max(0, min(unten - rahmen.minY, geo.size.height - 80))
                        }
                        .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                            verdeckt = 0
                        }
                }
                .ignoresSafeArea(.keyboard, edges: .bottom)
            } else {
                ScrollView {
                    Text(text.isEmpty ? art.leer : text)
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
            switch art {
            case .schueler: try await daten.speichern(notizSchueler: wert)
            case .lehrer: try await daten.speichern(notiz: wert)
            case .lehrstoff: try await daten.speichern(thema: wert)
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
    @State private var suche = ""

    private func schuelerName(_ id: Int?) -> String? {
        guard let id else { return nil }
        return daten.anwesenheit?.schueler.first { $0.id == id }?.anzeige
    }

    /// Überschrift eines Eintrags: Name der Schülerin bzw. des Schülers, sonst die Klasse.
    private func gruppe(_ v: Vermerk) -> String {
        if let name = schuelerName(v.schuelerId) { return name }
        return v.klasse.isEmpty ? "Klasse" : "Klasse \(v.klasse)"
    }

    private struct Gruppe: Identifiable {
        let name: String
        var eintraege: [Vermerk]
        var id: String { name }
    }

    /// Einträge nach Name gruppiert (Reihenfolge wie geliefert), gefiltert nach der Suche.
    private var gruppen: [Gruppe] {
        let begriff = suche.trimmingCharacters(in: .whitespacesAndNewlines)
        var liste: [Gruppe] = []
        for v in daten.vermerke {
            let name = gruppe(v)
            if !begriff.isEmpty {
                let treffer = [name, v.art, v.text, v.lehrer].contains { $0.localizedCaseInsensitiveContains(begriff) }
                if !treffer { continue }
            }
            if let i = liste.firstIndex(where: { $0.name == name }) {
                liste[i].eintraege.append(v)
            } else {
                liste.append(Gruppe(name: name, eintraege: [v]))
            }
        }
        return liste
    }

    /// Vorlage: Kopf mit Zurück und Titel mittig; die Suche steht als eigene Zeile unter dem Kopf (nicht daneben gequetscht).
    var body: some View {
        DetailSeitenRahmen(titel: "Klassenbucheinträge", untertitel: stundenUntertitel(daten.karte)) {
            VStack(spacing: 0) {
                suchfeld
                    .padding(.horizontal, 20)
                    .padding(.top, 2)
                    .padding(.bottom, 8)
                liste
            }
        }
    }

    private var suchfeld: some View {
        HStack(spacing: 7) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 15, weight: .regular))
                .foregroundStyle(Farben.gedaempft)
            TextField("Suchen", text: $suche)
                .font(.system(size: 16))
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.search)
            if !suche.isEmpty {
                Button {
                    suche = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gedaempft)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Suche löschen")
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 38)
        .background(Farben.knopf, in: Capsule())
    }

    private var liste: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 0) {
                if daten.vermerke.isEmpty {
                    Text(daten.geladen ? "Keine Klassenbucheinträge an diesem Tag." : "Wird geladen …")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gedaempft)
                        .padding(.horizontal, 20)
                        .padding(.top, 12)
                } else if gruppen.isEmpty {
                    Text("Keine Treffer.")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gedaempft)
                        .padding(.horizontal, 20)
                        .padding(.top, 12)
                }
                ForEach(gruppen) { g in
                    Text(g.name)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .padding(.horizontal, 20)
                        .padding(.top, 14)
                        .padding(.bottom, 6)
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 0.5)
                        .padding(.horizontal, 20)
                    ForEach(g.eintraege) { v in
                        eintrag(v)
                    }
                }
            }
            .padding(.bottom, 24)
        }
        .scrollIndicators(.hidden)
        .scrollDismissesKeyboard(.interactively)
    }

    private func eintrag(_ v: Vermerk) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            let wann = [v.datum.isEmpty ? "" : Datumsformat.anzeige(v.datum, "EE, dd.MM.yyyy"), v.zeit, v.lehrer]
                .filter { !$0.isEmpty }.joined(separator: ", ")
            if !wann.isEmpty {
                Label(wann, systemImage: "calendar")
            }
            if !v.art.isEmpty {
                Label(v.art, systemImage: "info.circle")
            }
            if !v.text.isEmpty {
                Text(v.text)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .font(.system(size: 15))
        .foregroundStyle(Farben.tinte)
        .labelStyle(EintragLabelStil())
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 20)
        .padding(.vertical, 12)
        .accessibilityElement(children: .combine)
    }
}

/// Symbol klein und grau vor dem Text (Datum, Art eines Klassenbucheintrags).
private struct EintragLabelStil: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 5) {
            configuration.icon
                .font(.system(size: 13, weight: .light))
                .foregroundStyle(Farben.gedaempft)
            configuration.title
        }
    }
}

// MARK: - Schüler*innen (Anwesenheit)

/// Vorlage (Seite „Schüler*innen“): Kopf mit Zurück, Titel und Datum mittig; darunter das Segment
/// Alle | Abwesend | Anwesend und die Leiste „✓ Abwesenheiten wurden kontrolliert“ (bzw. zum Bestätigen der Kontrolle),
/// Abschnitte nach Anfangsbuchstaben mit A–Z-Leiste rechts.
/// Jede Zeile: grüner Haken-Kreis (anwesend) bzw. rote Uhr (abwesend), Name, darunter „Abwesend (verspätet)“ bzw.
/// „Abwesend (fehlt)“ oder der Hinweis „War in Vorstunde abwesend!“, rechts Buch mit Plus (neuer Klassenbucheintrag)
/// und Uhr (Abwesenheiten der Person). Wischen von links nach rechts: anwesend (grün), von rechts nach links: abwesend (rot).
struct SchuelerSeite: View {
    enum Filter: Hashable {
        case alle
        case abwesend
        case anwesend
    }

    /// Vorlage für den Klassenbucheintrag (vorausgewählte Schüler*innen).
    struct Eintragsvorlage: Identifiable {
        let id = UUID()
        let schueler: [Schueler]
    }

    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var filter: Filter = .alle
    @State private var speichert: Int? = nil
    @State private var kontrolliert = false
    @State private var fehler: String? = nil
    @State private var eintrag: Eintragsvorlage? = nil
    @State private var abwesenheitenVon: Schueler? = nil

    private var liste: Anwesenheitsliste? { daten.anwesenheit }

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
        case .abwesend: return alle.filter { daten.fehlzeit($0) != nil }
        case .anwesend: return alle.filter { daten.fehlzeit($0) == nil }
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
        }
        .sheet(item: $eintrag) { v in
            NeuerKlassenbucheintragView(vorauswahl: v.schueler)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .navigationDestination(item: $abwesenheitenVon) { s in
            SchuelerAbwesenheitenView(schueler: s)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private var inhalt: some View {
        let alle = liste?.schueler ?? []
        let abwesend = alle.filter { daten.fehlzeit($0) != nil }.count
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
            kontrollLeiste
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
                                        .listRowInsets(EdgeInsets(top: 0, leading: 16, bottom: 0, trailing: 26))
                                        .listRowBackground(Farben.blatt)
                                        .swipeActions(edge: .leading, allowsFullSwipe: true) {
                                            if bearbeitbar && daten.fehlzeit(s) != nil {
                                                Button {
                                                    Task { await ausfuehren(s) { try await daten.anwesendSetzen(s) } }
                                                } label: {
                                                    Label("Anwesend", systemImage: "checkmark.circle")
                                                }
                                                .tint(Farben.gruen)
                                            }
                                        }
                                        .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                            if bearbeitbar && daten.fehlzeit(s) == nil {
                                                Button {
                                                    Task { await ausfuehren(s) { try await daten.abwesendSetzen(s) } }
                                                } label: {
                                                    Label("Abwesend", systemImage: "clock")
                                                }
                                                .tint(Farben.entfallRand)
                                            }
                                        }
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

    /// Leiste unter dem Segment: „✓ Abwesenheiten wurden kontrolliert“; noch nicht kontrolliert: antippen zum Bestätigen.
    @ViewBuilder
    private var kontrollLeiste: some View {
        if let ab = kontrolleAb {
            let zeit = daten.karte.beginn.isEmpty ? "" : " - \(daten.karte.beginn)"
            leiste(Text("Kontrolle der Abwesenheiten ist ab \(Datumsformat.anzeige(ab, "dd.MM.yyyy"))\(zeit) möglich"))
        } else if liste?.kontrolliert == true {
            leiste(Text("✓ Abwesenheiten wurden kontrolliert"))
                .accessibilityLabel("Abwesenheiten wurden kontrolliert")
        } else if bearbeitbar {
            Button {
                Task { await kontrolleBestaetigen() }
            } label: {
                leiste(HStack(spacing: 6) {
                    if kontrolliert {
                        ProgressView()
                            .controlSize(.mini)
                    } else {
                        Image(systemName: "checkmark.circle")
                    }
                    Text("Abwesenheiten kontrollieren")
                }
                .foregroundStyle(Farben.akzent))
            }
            .buttonStyle(.plain)
            .disabled(kontrolliert)
            .accessibilityHint("Bestätigt, dass die Abwesenheiten dieser Stunde kontrolliert wurden")
        } else if liste?.darf == false {
            leiste(Text("Nur Ansicht – die Stunde gehört nicht zu Ihrem Unterricht."))
        }
    }

    private func leiste<V: View>(_ inhalt: V) -> some View {
        inhalt
            .font(.system(size: 13, weight: .regular))
            .foregroundStyle(Farben.tinte2)
            .multilineTextAlignment(.center)
            .lineLimit(2)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 7)
            .padding(.horizontal, 12)
            .background(Farben.kontrollLeiste)
    }

    private func untertitel(_ s: Schueler, _ f: Fehlzeiteintrag?) -> (text: String, warnung: Bool)? {
        if let f { return (f.statustext, false) }
        if daten.inVorstundeAbwesend(s) { return ("War in Vorstunde abwesend!", true) }
        var teile: [String] = []
        let dienste = daten.dienste[s.id] ?? []
        if !dienste.isEmpty { teile.append(dienste.joined(separator: ", ")) }
        if teile.isEmpty, (liste?.schueler.contains { $0.klasse != s.klasse }) == true, !s.klasse.isEmpty {
            teile.append(s.klasse)
        }
        return teile.isEmpty ? nil : (teile.joined(separator: " · "), false)
    }

    private func zeile(_ s: Schueler) -> some View {
        let f = daten.fehlzeit(s)
        let hatVermerk = daten.vermerke.contains { $0.schuelerId == s.id }
        return HStack(spacing: 14) {
            Group {
                if speichert == s.id {
                    ProgressView()
                } else if f != nil {
                    Image(systemName: "clock")
                        .foregroundStyle(Farben.entfallRand)
                } else {
                    Image(systemName: "checkmark.circle")
                        .foregroundStyle(Farben.gruen)
                }
            }
            .font(.system(size: 22, weight: .light))
            .frame(width: 28, height: 28)
            .accessibilityLabel(f == nil ? "Anwesend" : "Abwesend")

            VStack(alignment: .leading, spacing: 2) {
                Text(s.anzeige)
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                    .truncationMode(.tail)
                if let u = untertitel(s, f) {
                    Text(u.text)
                        .font(.system(size: 14, weight: u.warnung ? .medium : .regular))
                        .foregroundStyle(u.warnung ? Farben.entfallRand : Farben.gedaempft)
                        .lineLimit(1)
                        .truncationMode(.tail)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Button {
                eintrag = Eintragsvorlage(schueler: [s])
            } label: {
                BuchPlusSymbol(vorhanden: hatVermerk)
            }
            .buttonStyle(.borderless)
            .accessibilityLabel(hatVermerk ? "Klassenbucheintrag vorhanden, neuer Klassenbucheintrag" : "Neuer Klassenbucheintrag")
            Button {
                abwesenheitenVon = s
            } label: {
                Image(systemName: "clock")
                    .font(.system(size: 22, weight: .light))
                    .foregroundStyle(Farben.tinte2)
                    .frame(width: 30, height: 30)
            }
            .buttonStyle(.borderless)
            .accessibilityLabel("Abwesenheiten von \(s.anzeige)")
        }
        .frame(minHeight: 56)
        .contentShape(Rectangle())
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

    private func ausfuehren(_ s: Schueler, _ aktion: () async throws -> Void) async {
        speichert = s.id
        defer { speichert = nil }
        do {
            try await aktion()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }

    private func kontrolleBestaetigen() async {
        kontrolliert = true
        defer { kontrolliert = false }
        do {
            try await daten.kontrolleBestaetigen()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Buch mit Plus (neuer Klassenbucheintrag); ist schon ein Eintrag vorhanden, das Buch ohne Plus.
struct BuchPlusSymbol: View {
    var vorhanden: Bool = false

    var body: some View {
        Image(systemName: "book.closed")
            .font(.system(size: 21, weight: .light))
            .foregroundStyle(Farben.tinte2)
            .overlay(alignment: .bottomTrailing) {
                if !vorhanden {
                    Image(systemName: "plus.circle")
                        .font(.system(size: 11, weight: .regular))
                        .foregroundStyle(Farben.tinte2)
                        .background(Circle().fill(Farben.blatt).padding(1))
                        .offset(x: 4, y: 3)
                }
            }
            .frame(width: 30, height: 30)
    }
}
