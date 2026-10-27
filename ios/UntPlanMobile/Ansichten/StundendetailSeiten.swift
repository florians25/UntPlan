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
    /// Gewählte Einzelstunde einer Doppel- bzw. Blockstunde (nil = ganzer Block): Lehrstoff, Notizen, Online,
    /// Hausaufgaben und Abwesenheiten gelten dann nur für diese Stunde.
    @Published var einzelstunde: Int? = nil
    /// Stunden des Blocks für die Auswahl (vom Server; bleibt beim Wechsel des Bereichs erhalten)
    @Published var blockStunden: [Einzelstundeninfo] = []

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

    /// Doppel- bzw. Blockstunde, deren Stunden einzeln eingetragen werden können (Server liefert die Stunden des Blocks).
    var hatEinzelstunden: Bool { karte.bis > karte.von && blockStunden.count > 1 }

    /// Plakette der Zeile „Hausaufgaben“ in den Details: offene (unerledigte) Hausaufgaben dieser Stunde und die zu ihr
    /// fälligen; vor dem Laden genügt der Hinweis der Karte („Hausaufgabe fällig“).
    var hausaufgabenAnzahl: Int? {
        guard let h = hausaufgaben else { return geladen ? 0 : (karte.hausaufgabeFaellig ? 1 : nil) }
        var gesehen = Set<Int>()
        return (h.liste + h.faellig).filter { !$0.erledigt && gesehen.insert($0.id).inserted }.count
    }

    /// Blockeintrag, der in der Einzelstunde nur lesend (geerbt) gilt.
    var geerbterEintrag: Klassenbucheintrag? { einzelstunde == nil ? nil : klassenbuch?.blockEintrag }

    /// Zeiten der gewählten Einzelstunde (nil = ganzer Block).
    var gewaehlteStunde: Einzelstundeninfo? {
        guard let einzelstunde else { return nil }
        return blockStunden.first { $0.stunde == einzelstunde }
    }

    /// „3. Stunde“ bzw. „3.–4. Stunde“ für den gewählten Bereich.
    var bereichText: String {
        if let einzelstunde { return "\(einzelstunde). Stunde" }
        return karte.von == karte.bis ? "\(karte.von). Stunde" : "\(karte.von).–\(karte.bis). Stunde"
    }

    /// Bereich wechseln (ganzer Block bzw. eine Stunde): Zwischenstände verwerfen und alles für den Bereich neu laden.
    func bereichWaehlen(_ stunde: Int?, kollegium: Bool) async {
        guard stunde != einzelstunde else { return }
        einzelstunde = stunde
        notizSchuelerGespeichert = nil
        notizLehrerGespeichert = nil
        onlineLinkGespeichert = nil
        online = nil
        hausaufgaben = nil
        anwesenheit = nil
        geladen = false
        await laden(kollegium: kollegium)
    }

    /// Darf das Konto Lehrstoff, Notizen und Hausaufgaben dieser Stunde bearbeiten?
    var darf: Bool { klassenbuch?.darf == true || hausaufgaben?.darf == true }

    /// Zuletzt gespeicherte Notizen (gelten sofort, auch wenn sie geleert wurden)
    @Published var notizSchuelerGespeichert: String? = nil
    @Published var notizLehrerGespeichert: String? = nil

    /// In der Einzelstunde nur der eigene Eintrag (die Notiz der Karte enthält Block und alle Stunden).
    var notizSchueler: String {
        if let n = notizSchuelerGespeichert { return n }
        if let n = eintrag?.notizSchueler, !n.isEmpty { return n }
        return einzelstunde == nil ? karte.notizSchueler : ""
    }

    var notizLehrer: String {
        if let n = notizLehrerGespeichert { return n }
        if let n = eintrag?.notiz, !n.isEmpty { return n }
        return einzelstunde == nil ? karte.notizLehrer : ""
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
        if !b.isEmpty { return b }
        if let s = gewaehlteStunde, !s.beginn.isEmpty { return s.beginn }
        return karte.beginn
    }

    var blockEnde: String {
        let e = anwesenheit?.ende ?? ""
        if !e.isEmpty { return e }
        if let s = gewaehlteStunde, !s.ende.isEmpty { return s.ende }
        return karte.ende
    }

    /// Lädt alle Teile gleichzeitig (statt nacheinander): Klassenbuch, Hausaufgaben und – für Lehrkräfte – Anwesenheit,
    /// Klassendienste und Klassenbucheinträge.
    func laden(kollegium: Bool) async {
        defer { geladen = true }
        guard let u = karte.unterricht else { return }
        let api = self.api
        let datum = karte.datum
        let von = karte.von
        let bis = karte.bis
        let fach = karte.fach
        let klassenliste = klassen
        let lehrer = lehrerHinweis
        let einzeln = einzelstunde
        @Sendable func holeKlassenbuch() async -> KlassenbuchStunde? {
            try? await api.klassenbuchStunde(datum: datum, stunde: von, unterricht: u, lehrer: lehrer, einzelstunde: einzeln)
        }
        @Sendable func holeHausaufgaben() async -> HausaufgabenDerStunde? {
            try? await api.hausaufgabenDerStunde(datum: datum, stunde: von, bis: bis, unterricht: u, fach: fach, klassen: klassenliste,
                                                 einzelstunde: einzeln)
        }
        @Sendable func holeAnwesenheit() async -> Anwesenheitsliste? {
            guard kollegium else { return nil }
            return try? await api.anwesenheit(datum: datum, stunde: von, unterricht: u, einzelstunde: einzeln)
        }
        async let kb = holeKlassenbuch()
        async let hw = holeHausaufgaben()
        async let anw = holeAnwesenheit()
        @Sendable func holeDienste() async {
            guard kollegium else { return }
            await self.diensteLaden()
        }
        @Sendable func holeVermerke() async {
            guard kollegium else { return }
            await self.vermerkeLaden()
        }
        async let dienstListe: Void = holeDienste()
        async let vermerkListe: Void = holeVermerke()
        let (neuKlassenbuch, neuHausaufgaben, neuAnwesenheit) = await (kb, hw, anw)
        // Bereich inzwischen gewechselt: veraltete Antwort verwerfen (das neue Laden läuft bereits)
        guard einzeln == einzelstunde else { return }
        if let neuKlassenbuch {
            klassenbuch = neuKlassenbuch
            if !neuKlassenbuch.einzelstunden.isEmpty { blockStunden = neuKlassenbuch.einzelstunden }
        }
        if let neuHausaufgaben { hausaufgaben = neuHausaufgaben }
        if let neuAnwesenheit { anwesenheit = neuAnwesenheit }
        _ = await (dienstListe, vermerkListe)
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
                                                         fach: karte.fach, klassen: klassen, einzelstunde: einzelstunde) {
            hausaufgaben = h
        }
    }

    func anwesenheitLaden() async {
        guard let u = karte.unterricht else { return }
        if let a = try? await api.anwesenheit(datum: karte.datum, stunde: karte.von, unterricht: u, einzelstunde: einzelstunde) {
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
                   notiz: String? = nil, notizSchueler: String? = nil, online: Bool? = nil, onlineLink: String? = nil) async throws {
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
                                     online: online,
                                     onlineLink: onlineLink,
                                     einzelstunde: einzelstunde)
        let neu = try await api.klassenbuchSpeichern(daten)
        var stand = klassenbuch ?? KlassenbuchStunde(eintrag: nil, darf: true)
        stand.eintrag = neu
        klassenbuch = stand
        if let notizSchueler { notizSchuelerGespeichert = notizSchueler }
        if let notiz { notizLehrerGespeichert = notiz }
        if let onlineLink { onlineLinkGespeichert = onlineLink }
    }

    /// Zuletzt gespeicherter Link zum Online-Unterricht (gilt sofort, auch wenn der Server ihn – noch – nicht zurückgibt).
    @Published var onlineLinkGespeichert: String? = nil

    /// Link zum Online-Unterricht (gespeichert, aus dem Klassenbuch bzw. von der Karte).
    var onlineLink: String {
        if let l = onlineLinkGespeichert { return l }
        if let l = eintrag?.onlineLink, !l.isEmpty { return l }
        return karte.onlineLink
    }

    /// Link zum Online-Unterricht speichern (die Stunde bleibt online).
    func onlineLinkSetzen(_ link: String) async throws {
        let wert = link.trimmingCharacters(in: .whitespacesAndNewlines)
        try await speichern(online: true, onlineLink: wert)
    }

    /// „Findet online statt“ – optimistisch umschalten, bei Fehler zurück.
    @Published var online: Bool? = nil

    /// In der Einzelstunde gilt ohne eigenen Eintrag der Blockeintrag.
    var istOnline: Bool {
        if let online { return online }
        if let e = eintrag { return e.online }
        if einzelstunde != nil { return klassenbuch?.blockEintrag?.online ?? karte.online }
        return karte.online
    }

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

    /// Neue Hausaufgabe; „erledigt“ nimmt der Server beim Anlegen nicht an – dann gleich danach setzen.
    func hausaufgabeAnlegen(text: String, faellig: Date?, bemerkung: String, erledigt: Bool = false) async throws {
        guard let u = karte.unterricht else { return }
        let neu = try await api.hausaufgabeAnlegen(HausaufgabeNeuDaten(datum: karte.datum, stunde: karte.von, unterricht: u, text: text,
                                                                       faellig: faellig.map { Datumsformat.iso($0) }, bemerkung: bemerkung,
                                                                       einzelstunde: einzelstunde))
        if erledigt {
            _ = try await api.hausaufgabeAendern(neu.id, HausaufgabeAenderDaten(text: nil, faellig: nil, bemerkung: nil, erledigt: true))
        }
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
        try await api.abwesenheitEntfernen(AbwesenheitEntfernenDaten(datum: karte.datum, stunde: karte.von, unterricht: u, schuelerIds: [s.id],
                                                                         einzelstunde: einzelstunde))
        await anwesenheitLaden()
    }

    /// Wischgeste nach links: abwesend für den ganzen Block (minutengenau von Beginn bis Ende).
    func abwesendSetzen(_ s: Schueler) async throws {
        guard let u = karte.unterricht, fehlzeit(s) == nil else { return }
        try await api.abwesenheitErfassen(AbwesenheitZeitraumDaten(
            schuelerIds: [s.id], von: Self.zeitpunkt(zeit(blockBeginn)), bis: Self.zeitpunkt(zeit(blockEnde)),
            grund: nil, text: nil, datum: karte.datum, stunde: karte.von, unterricht: u, einzelstunde: einzelstunde))
        await anwesenheitLaden()
    }

    /// Abwesenheit erfassen bzw. ändern (Abwesenheitsdialog). `ersetzen`: Fehlzeit-IDs der bisherigen Abwesenheit.
    func abwesenheitSpeichern(_ s: Schueler, von: Date, bis: Date, grund: String, text: String, eltern: Bool, ersetzen: [Int]?) async throws {
        guard let u = karte.unterricht else { return }
        try await api.abwesenheitErfassen(AbwesenheitZeitraumDaten(
            schuelerIds: [s.id], von: Self.zeitpunkt(von), bis: Self.zeitpunkt(bis),
            grund: grund, text: text, datum: karte.datum, stunde: karte.von, unterricht: u,
            ersetzen: (ersetzen ?? []).isEmpty ? nil : ersetzen, elternInformiert: eltern, einzelstunde: einzelstunde))
        await anwesenheitLaden()
    }

    /// Abwesenheit löschen: ganze Abwesenheit (Klassenleitung, IDs) bzw. die Abwesenheit in dieser Stunde.
    func abwesenheitLoeschen(_ s: Schueler, ids: [Int]?) async throws {
        if let ids, !ids.isEmpty, anwesenheit?.verwalten == true {
            try await api.abwesenheitenLoeschen(ids)
        } else if let u = karte.unterricht {
            try await api.abwesenheitEntfernen(AbwesenheitEntfernenDaten(datum: karte.datum, stunde: karte.von, unterricht: u, schuelerIds: [s.id],
                                                                         einzelstunde: einzelstunde))
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
        try await api.anwesenheitSpeichern(AnwesenheitDaten(datum: karte.datum, stunde: karte.von, unterricht: u, eintraege: eintraege,
                                                            einzelstunde: einzelstunde))
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
        let original = karte.raeume.map { w in (w.alt ?? "").isEmpty ? w.k : (w.alt ?? "") }.filter { !$0.isEmpty }
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
enum Detailseite: Hashable, Identifiable {
    var id: Self { self }

    case text
    case info
    case lehrstoff
    case schueler
    case hausaufgaben
    case klassenbuch
    case notizSchueler
    case notizLehrer
    /// Prüfung (Zeile mit dem Fach als Untertitel, Vorlage 140)
    case pruefung
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
            case .lehrstoff: return "Text hier eingeben"
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
    /// Lehrstoff aus den Vorwochen (Liste unter dem Eingabefeld, Antippen übernimmt den Text)
    @State private var vorstunden: [LehrstoffVorstunde] = []
    @FocusState private var fokus: Bool
    @Environment(\.dismiss) private var systemZurueck
    @Environment(\.blattSchliessen) private var blattSchliessen
    @Environment(\.unterblatt) private var unterblatt

    /// Als Unterblatt über den Stundendetails: mit dessen Animation schließen, sonst zurück.
    private func zurueck() {
        if unterblatt, let blattSchliessen { blattSchliessen() } else { systemZurueck() }
    }

    /// Bearbeiten dürfen nur Lehrkräfte mit Schreibrecht; Schüler*innen sehen ihre Notizen nur lesend.
    private var bearbeitbar: Bool { zustand.istKollegium && daten.darf && daten.karte.unterricht != nil }

    /// Notizen für Lehrkräfte sind für Schüler*innen und Eltern nie sichtbar.
    private var sichtbar: Bool { art != .lehrer || zustand.istKollegium }

    /// Inhalt des Blockeintrags, der in der gewählten Einzelstunde mit gilt (nur lesend).
    private var geerbt: String {
        guard let b = daten.geerbterEintrag else { return "" }
        switch art {
        case .schueler: return b.notizSchueler
        case .lehrer: return sichtbar ? b.notiz : ""
        case .lehrstoff: return b.thema
        }
    }

    /// Vorlage (Seite „Notizen“): X links, Titel mittig grau, darunter ein großer weißer Textbereich.
    var body: some View {
        // Einzelstunde eines Blocks: Untertitel nennt die Stunde, darüber (nur lesend) der geerbte Blockeintrag
        DetailSeitenRahmen(titel: art.titel, untertitel: daten.einzelstunde == nil ? nil : "Nur \(daten.bereichText)",
                           symbol: "xmark", zurueck: { Task { await schliessen() } }) {
            VStack(spacing: 0) {
                if !geerbt.isEmpty {
                    GeerbterBlockText(text: geerbt)
                }
                // Auch künftige Stunden sind ohne Datumsgrenze bearbeitbar (beliebig weit im Voraus)
                if art == .lehrstoff && bearbeitbar {
                    lehrstoffbereich
                } else {
                    textbereich
                }
            }
        } rechts: {
            if bearbeitbar {
                SichernKnopf(speichert: speichert, aktiv: text != anfang) { Task { await sichern() } }
            }
        }
        .onAppear {
            let wert: String
            switch art {
            case .schueler: wert = daten.notizSchueler
            case .lehrer: wert = sichtbar ? daten.notizLehrer : ""
            case .lehrstoff: wert = daten.eintrag?.thema ?? ""
            }
            text = wert
            anfang = wert
        }
        // Fokus beim Verlassen abgeben: die Tastatur schließt, das Blatt kehrt sicher in seine Größe zurück
        .onDisappear { fokus = false }
        .task {
            guard art == .lehrstoff, bearbeitbar, let u = daten.karte.unterricht else { return }
            let von = daten.einzelstunde ?? daten.karte.von
            let bis = daten.einzelstunde ?? daten.karte.bis
            if let liste = try? await daten.api.lehrstoffVorstunden(datum: daten.karte.datum, stunde: von, bis: bis, unterricht: u,
                                                                    lehrer: daten.lehrerHinweis) {
                vorstunden = liste
            }
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
        fokus = false
        zurueck()
    }

    /// Lehrstoff wie in der Vorlage 555deb0d: einzeiliges (mitwachsendes) Feld „Text hier eingeben“ (17 pt, oranger Cursor),
    /// darunter eine 1-pt-Linie und der Lehrstoff der Vorwochen (Symbol 24 pt, Text einzeilig 16 pt, darunter grau
    /// „Mo., 12.10.2026, 12:15 - 13:00“; Zeilen 56 pt). Antippen übernimmt den Text in das Feld.
    private var lehrstoffbereich: some View {
        VStack(spacing: 0) {
            TextField("", text: $text, prompt: Text(art.platzhalter).foregroundStyle(Farben.titelGrau), axis: .vertical)
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
                .tint(Farben.akzent)
                .lineLimit(1...8)
                .focused($fokus)
                .padding(.horizontal, 16)
                .padding(.vertical, 17)
                .frame(minHeight: 54.67)
                .frame(maxWidth: .infinity, alignment: .leading)
            Rectangle()
                .fill(Farben.detailLinie)
                .frame(height: 1)
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(vorstunden) { v in
                        Button {
                            text = v.thema
                            fokus = true
                        } label: {
                            HStack(spacing: 18) {
                                Listensymbol(name: "SymbolLehrstoff", groesse: 24, einpassen: true)
                                VStack(alignment: .leading, spacing: 3.2) {
                                    Text(v.thema.replacingOccurrences(of: "\n", with: " "))
                                        .font(.system(size: 16))
                                        .foregroundStyle(Farben.detailGrau)
                                        .lineLimit(1)
                                        .truncationMode(.tail)
                                    Text(Self.datumszeile(v))
                                        .font(.system(size: 16))
                                        .foregroundStyle(Farben.titelGrau)
                                        .lineLimit(1)
                                }
                                Spacer(minLength: 0)
                            }
                            .padding(.leading, Detailmass.zeilenRand)
                            .padding(.trailing, 16)
                            .frame(minHeight: 56)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(OhneTippStil())
                        .accessibilityHint("Übernimmt diesen Lehrstoff")
                    }
                }
                .padding(.top, 9.3)
            }
            .scrollDismissesKeyboard(.interactively)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(Farben.blatt)
    }

    /// „Mo., 12.10.2026, 12:15 - 13:00“
    static func datumszeile(_ v: LehrstoffVorstunde) -> String {
        let tag = Datumsformat.anzeige(v.datum, "EE, dd.MM.yyyy")
        guard !v.beginn.isEmpty else { return tag }
        return "\(tag), \(Detailmass.uhrzeit(v.beginn)) - \(Detailmass.uhrzeit(v.ende))"
    }

    @ViewBuilder
    private var textbereich: some View {
        Group {
            if bearbeitbar {
                // Ohne eigene Tastatur-Verschiebung: der Textbereich weicht der Tastatur über den Sicherheitsbereich aus
                // und füllt nach dem Schließen wieder die ganze Seite; Wischen nach unten schließt die Tastatur
                TextEditor(text: $text)
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.tinte)
                    .scrollContentBackground(.hidden)
                    .scrollDismissesKeyboard(.interactively)
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
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
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
            case .schueler:
                try await daten.speichern(notizSchueler: wert)
                // Info-Zeichen auf der Karte sofort zeigen bzw. entfernen (Plan lokal anpassen und neu laden); bei einer
                // Einzelstunde setzt der Server die Notiz der Karte zusammen („4. Std.: …“) – dann nur neu laden
                zustand.notizGespeichert(daten.karte, schueler: daten.einzelstunde == nil ? wert : nil, lehrer: nil)
            case .lehrer:
                try await daten.speichern(notiz: wert)
                zustand.notizGespeichert(daten.karte, schueler: nil, lehrer: daten.einzelstunde == nil ? wert : nil)
            case .lehrstoff:
                try await daten.speichern(thema: wert)
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

/// Geerbter Blockeintrag über dem Textbereich einer Einzelstunde (nur lesend).
struct GeerbterBlockText: View {
    let text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text("Gilt für den ganzen Block")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(Farben.gedaempft)
            Text(text)
                .font(.system(size: 15))
                .foregroundStyle(Farben.tinte2)
                .lineLimit(4)
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(Farben.knopf, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .padding(.horizontal, 15)
        .padding(.top, 10)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Link zum Online-Unterricht

/// Zeile unter „Findet online statt“ (Lehrkräfte mit Schreibrecht): Link zum Online-Unterricht eingeben und sichern,
/// rechts Öffnen bzw. Sichern. Schüler*innen sehen stattdessen „Online-Unterricht beitreten“.
struct OnlineLinkZeile: View {
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.openURL) private var oeffneURL
    @State private var eingabe = ""
    @State private var anfang = ""
    @State private var speichert = false
    @State private var fehler: String? = nil
    @FocusState private var fokus: Bool

    private var geaendert: Bool {
        eingabe.trimmingCharacters(in: .whitespacesAndNewlines) != anfang.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var body: some View {
        HStack(spacing: 12) {
            Listensymbol(name: "link")
            TextField("Link zum Online-Unterricht", text: $eingabe)
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
                .keyboardType(.URL)
                .textContentType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .focused($fokus)
                .onSubmit { Task { await sichern() } }
            if speichert {
                ProgressView()
                    .frame(width: 31, height: 31)
            } else if geaendert {
                Button {
                    Task { await sichern() }
                } label: {
                    Image(systemName: "checkmark")
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(.white)
                        .frame(width: 31, height: 31)
                        .background(Farben.akzent, in: Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Link sichern")
            } else if let url = Karte.onlineURL(eingabe) {
                Button {
                    oeffneURL(url)
                } label: {
                    Image(systemName: "arrow.up.right.square")
                        .font(.system(size: 20, weight: .light))
                        .foregroundStyle(Farben.gruen)
                        .frame(width: 31, height: 31)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Online-Unterricht öffnen")
            }
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, 22)
        .padding(.vertical, 8)
        .frame(minHeight: 52)
        .onAppear {
            eingabe = daten.onlineLink
            anfang = daten.onlineLink
        }
        .onChange(of: fokus) { _, neu in
            // Feld verlassen: geänderten Link sichern
            if !neu && geaendert { Task { await sichern() } }
        }
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    private func sichern() async {
        guard geaendert, !speichert else { return }
        let wert = eingabe.trimmingCharacters(in: .whitespacesAndNewlines)
        if !wert.isEmpty && Karte.onlineURL(wert) == nil {
            fehler = "Bitte einen gültigen Link eingeben (z. B. https://…)."
            return
        }
        speichert = true
        defer { speichert = false }
        do {
            try await daten.onlineLinkSetzen(wert)
            eingabe = wert
            anfang = wert
            fokus = false
            withAnimation { daten.hinweis = wert.isEmpty ? "Link entfernt" : "Link gespeichert" }
            zustand.planGeaendert += 1
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

// MARK: - Hausaufgaben

/// Hausaufgaben einer Stunde wie im Original (Vorlagen ffb86f46, 39e2d5a6, a36f419e): Kopf „Hausaufgaben“ mit Datum und
/// Uhrzeit, darunter zwei Filter-Pills (Stunden, Status) mit Menü; ohne Treffer der leere Zustand „Keine Hausaufgaben“.
struct HausaufgabenSeite: View {
    enum Stundenfilter: Hashable { case alle, aktuell, vergangen }
    enum Statusfilter: Hashable { case alle, offen, erledigt }

    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @State private var formular: HausaufgabeFormular.Vorlage? = nil
    /// Nur lesen (fremde bzw. nicht bearbeitbare Hausaufgabe): Seite „Hausaufgabe“
    @State private var ansicht: Hausaufgabeneintrag? = nil
    @State private var fehler: String? = nil
    /// Vorgabe wie im Original: „Aktuelle Stunde“ und „Unerledigte“
    @State private var stunden: Stundenfilter = .aktuell
    @State private var status: Statusfilter = .offen

    private var bearbeitbar: Bool { daten.hausaufgaben?.darf == true }

    var body: some View {
        DetailSeitenRahmen(titel: "Hausaufgaben", untertitel: daten.einzelstunde == nil ? stundenUntertitel(daten.karte) : "Nur \(daten.bereichText)",
                           laedt: !daten.geladen, titelStil: .fett) {
            VStack(spacing: 0) {
                filterleiste
                if gefiltert.isEmpty {
                    leer
                    Spacer(minLength: 0)
                } else {
                    hausaufgabenliste
                }
            }
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
        // iPhone: randloses Blatt wie die Stundendetails; iPad: Formblatt (Größenklasse im Blatt ist kompakt)
        .detailPraesentation(item: $formular, breit: Geraet.istPad, art: .formular) { vorlage in
            HausaufgabeFormular(vorlage: vorlage) { text, faellig, bemerkung, erledigt in
                if let e = vorlage.eintrag {
                    try await daten.hausaufgabeAendern(e.id, text: text, faellig: faellig, bemerkung: bemerkung,
                                                       erledigt: erledigt == e.erledigt ? nil : erledigt)
                } else {
                    try await daten.hausaufgabeAnlegen(text: text, faellig: faellig, bemerkung: bemerkung, erledigt: erledigt)
                }
            }
            .environmentObject(zustand)
        }
        .detailPraesentation(item: $ansicht, breit: Geraet.istPad, art: .formular) { ha in
            HausaufgabeAnsicht(eintrag: ha)
        }
        .alert("Fehler", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    // MARK: Filter

    /// „Aktuelle Stunde“: in dieser Stunde aufgegeben oder zu ihr fällig (Vorlage 4dd89173: aufgegeben 01.10., fällig in
    /// der Stunde am 08.10.); „Vergangene Stunden“: in früheren Stunden aufgegeben und zu dieser Stunde fällig. Mehr
    /// liefert der Server für eine Stunde nicht.
    private var gefiltert: [Hausaufgabeneintrag] {
        guard let h = daten.hausaufgaben else { return [] }
        let vergangen = h.faellig.filter { f in !h.liste.contains { $0.id == f.id } }
        let aktuell = h.liste + vergangen
        let quelle: [Hausaufgabeneintrag]
        switch stunden {
        case .alle, .aktuell: quelle = aktuell
        case .vergangen: quelle = vergangen
        }
        return quelle.filter { ha in
            switch status {
            case .alle: return true
            case .offen: return !ha.erledigt
            case .erledigt: return ha.erledigt
            }
        }
    }

    private var stundenText: String {
        switch stunden {
        case .alle: return "Alle"
        case .aktuell: return "Aktuelle Stunde"
        case .vergangen: return "Vergangene Stunden"
        }
    }

    private var statusText: String {
        switch status {
        case .alle: return "Alle"
        case .offen: return "Unerledigte"
        case .erledigt: return "Erledigt"
        }
    }

    /// Vorlage (3 px/pt): Pills 42 pt hoch, 2 pt unter dem Kopf, 16 pt vom Rand, 9 pt Abstand
    private var filterleiste: some View {
        HStack(spacing: 9) {
            Menu {
                Section("Stunden wählen") {
                    Toggle("Alle", isOn: gewaehlt($stunden, .alle))
                }
                Section {
                    Toggle("Aktuelle Stunde", isOn: gewaehlt($stunden, .aktuell))
                    Toggle("Vergangene Stunden", isOn: gewaehlt($stunden, .vergangen))
                }
            } label: {
                FilterPill(text: stundenText)
            }
            Menu {
                Section("Status wählen") {
                    Toggle("Alle", isOn: gewaehlt($status, .alle))
                }
                Section {
                    Toggle("Unerledigte", isOn: gewaehlt($status, .offen))
                    Toggle("Erledigt", isOn: gewaehlt($status, .erledigt))
                }
            } label: {
                FilterPill(text: statusText)
            }
            Spacer(minLength: 0)
        }
        .buttonStyle(.plain)
        .padding(.horizontal, Detailmass.rand)
        .padding(.top, 2)
    }

    private func gewaehlt<T: Equatable>(_ wahl: Binding<T>, _ wert: T) -> Binding<Bool> {
        Binding(get: { wahl.wrappedValue == wert }, set: { if $0 { wahl.wrappedValue = wert } })
    }

    // MARK: Leerer Zustand

    /// Vorlage: Kreis Ø 50 pt (#2c2c2e) 41 pt unter den Pills mit Datenbank-Fragezeichen 36 pt (#dcdfe5), 16 pt darunter
    /// „Keine Hausaufgaben“ 22 pt fett (#f3f5f7), wieder 16 pt darunter „Derzeit gibt es hier noch nichts“ 15 pt (#dcdfe5)
    private var leer: some View {
        VStack(spacing: 0) {
            LeerSymbol()
                .frame(width: 50, height: 50)
                .background(Farben.leerKreis, in: Circle())
            Text("Keine Hausaufgaben")
                .font(.system(size: 22, weight: .bold))
                .foregroundStyle(Farben.detailText)
                .padding(.top, 16)
            Text("Derzeit gibt es hier noch nichts")
                .font(.system(size: 15))
                .foregroundStyle(Farben.detailGrau)
                .padding(.top, 16)
        }
        .multilineTextAlignment(.center)
        .frame(maxWidth: .infinity)
        .padding(.top, 41)
        .padding(.horizontal, Detailmass.rand)
        .accessibilityElement(children: .combine)
    }

    // MARK: Liste

    /// Abschnitte nach Fälligkeitsdatum (ohne Fälligkeit: Aufgabedatum), älteste zuerst.
    private var abschnitte: [(datum: String, eintraege: [Hausaufgabeneintrag])] {
        let gruppen = Dictionary(grouping: gefiltert) { ha in (ha.faellig?.isEmpty == false ? ha.faellig! : ha.datum) }
        return gruppen.keys.sorted().map { (datum: $0, eintraege: gruppen[$0] ?? []) }
    }

    private func eigen(_ ha: Hausaufgabeneintrag) -> Bool {
        daten.hausaufgaben?.liste.contains { $0.id == ha.id } == true
    }

    /// Vorlage 4dd89173: Abschnittskopf „Do., 08.10.2026“ 15 pt grau (#3f525e) 39 pt unter den Pills, 3 pt darunter eine
    /// Linie 1 pt (#dbdfe5) 16 pt vom Rand; Zeilen ohne Trennlinien. Eigene Hausaufgaben: nach links wischen löscht.
    private var hausaufgabenliste: some View {
        List {
            ForEach(Array(abschnitte.enumerated()), id: \.element.datum) { index, abschnitt in
                Section {
                    ForEach(abschnitt.eintraege) { ha in
                        Button {
                            // Bearbeiten (auch erledigt setzen) aus der Stunde, in der sie aufgegeben wurde, und aus der
                            // Stunde, in der sie fällig ist (der Server prüft beides); löschen nur in der eigenen Stunde
                            if bearbeitbar {
                                formular = HausaufgabeFormular.Vorlage(eintrag: ha, stundenDatum: ha.datum.isEmpty ? daten.karte.datum : ha.datum)
                            } else {
                                ansicht = ha
                            }
                        } label: {
                            zeile(ha)
                        }
                        .buttonStyle(OhneTippStil())
                        .listRowInsets(EdgeInsets())
                        .listRowSeparator(.hidden)
                        .listRowBackground(Farben.blatt)
                        .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                            if bearbeitbar && eigen(ha) {
                                Button(role: .destructive) {
                                    Task { await ausfuehren { try await daten.hausaufgabeLoeschen(ha.id) } }
                                } label: {
                                    Label("Löschen", systemImage: "trash")
                                }
                            }
                        }
                    }
                } header: {
                    abschnittKopf(abschnitt.datum)
                        .padding(.top, index == 0 ? 39 : 24)
                        .listRowInsets(EdgeInsets())
                }
            }
        }
        .listStyle(.plain)
        .listSectionSeparator(.hidden)
        .scrollContentBackground(.hidden)
        .background(Farben.blatt)
        .environment(\.defaultMinListRowHeight, 1)
        .environment(\.defaultMinListHeaderHeight, 1)
        .refreshable { await daten.hausaufgabenLaden() }
    }

    private func abschnittKopf(_ datum: String) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(Datumsformat.anzeige(datum, "EE, dd.MM.yyyy"))
                .font(.system(size: 15))
                .foregroundStyle(Farben.detailGrau)
                .textCase(nil)
            Farben.detailLinie
                .frame(height: 1)
        }
        .padding(.horizontal, Detailmass.rand)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.blatt)
    }

    /// Vorlage 4dd89173: Hausaufgabensymbol 20,67 pt grün (#007d64, dunkel #1fc0a0) ab 17,67 pt; Text 17 pt fast schwarz
    /// ab 48,33 pt (Zeilen 22,33 pt), darunter „Aufgabedatum - Fälligkeitsdatum“ 16 pt grau; Pfeil 9,33 × 17,33 pt
    /// 23,3 pt vom Rand, alles mittig zur Zeile.
    private func zeile(_ ha: Hausaufgabeneintrag) -> some View {
        HStack(spacing: 0) {
            Listensymbol(name: "SymbolHausaufgaben", groesse: 20.67, einpassen: true)
                .foregroundStyle(Farben.gruen)
                .frame(width: 20.67)
                .padding(.leading, 17.67)
                .padding(.trailing, 9.66)
            VStack(alignment: .leading, spacing: 0) {
                Text(ha.text.isEmpty ? "Ohne Text" : ha.text)
                    .font(.system(size: 17))
                    .lineSpacing(2)
                    .foregroundStyle(Farben.detailText)
                    .multilineTextAlignment(.leading)
                Text(zeitraum(ha))
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.detailGrau)
                    .padding(.top, 1.5)
                // Doppel-/Blockstunde: gilt die Hausaufgabe für den ganzen Block oder nur für eine Stunde?
                if daten.hatEinzelstunden {
                    Text(ha.einzelstunde.map { "Nur \($0). Stunde" } ?? "Ganzer Block")
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.detailGrau)
                        .padding(.top, 2)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.right")
                .font(.system(size: 19.3, weight: .thin))
                .foregroundStyle(Farben.detailSymbol)
                .padding(.leading, 8)
                .padding(.trailing, 22.5)
        }
        .padding(.vertical, 8)
        .frame(maxWidth: .infinity)
        .background(Farben.blatt)
        .contentShape(Rectangle())
    }

    private func zeitraum(_ ha: Hausaufgabeneintrag) -> String {
        let von = Datumsformat.anzeige(ha.datum, "dd.MM.yyyy")
        guard let f = ha.faellig, !f.isEmpty else { return von }
        return "\(von) - \(Datumsformat.anzeige(f, "dd.MM.yyyy"))"
    }

    private func ausfuehren(_ aktion: () async throws -> Void) async {
        do {
            try await aktion()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Filter-Pill der Hausaufgaben (Vorlagen ffb86f46 dunkel, 4dd89173 hell): 42 pt hohe Kapsel, dunkel Fläche #dbdee4 mit
/// 1 pt Rand #818f9c und fast schwarzem Text, hell Fläche #323c45 mit Rand #5e7382 und weißem Text; Text 15 pt ab 16 pt, Pfeil 10,67 × 6,33 pt 7,3 pt dahinter, 17,5 pt
/// vor dem rechten Rand.
struct FilterPill: View {
    let text: String

    var body: some View {
        HStack(spacing: 7.3) {
            Text(text)
                .font(.system(size: 15))
                .lineLimit(1)
            Image(systemName: "chevron.down")
                .font(.system(size: 12.5, weight: .medium))
        }
        .foregroundStyle(Farben.filterText)
        .padding(.leading, 16)
        .padding(.trailing, 17.5)
        .frame(height: 42)
        .background(Capsule().fill(Farben.filterFlaeche))
        .overlay(Capsule().strokeBorder(Farben.filterRand, lineWidth: 1))
        .contentShape(Capsule())
    }
}

/// Datenbank mit Fragezeichen (leerer Zustand): Zylinderstapel mit Kreisfragezeichen unten rechts, zusammen 36 × 36 pt.
private struct LeerSymbol: View {
    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            Image(systemName: "cylinder.split.1x2")
                .resizable()
                .fontWeight(.light)
                .scaledToFit()
                .frame(width: 28, height: 32)
                .frame(width: 36, height: 36, alignment: .topLeading)
            Image(systemName: "questionmark.circle")
                .resizable()
                .fontWeight(.medium)
                .scaledToFit()
                .frame(width: 19, height: 19)
                .padding(1.5)
                .background(Circle().fill(Farben.leerKreis))
                .offset(x: 1.5, y: 1.5)
        }
        .frame(width: 36, height: 36)
        .foregroundStyle(Farben.detailGrau)
        .accessibilityHidden(true)
    }
}

/// Trennlinie der Formulare (Vorlagen f32d5ffe/fbcdb9e9): 1 pt, hell #e7e7e8, dunkel #38383a, 16 pt vom Rand.
struct Formtrenner: View {
    var body: some View {
        Farben.formLinie
            .frame(height: 1)
            .padding(.horizontal, Detailmass.rand)
    }
}

/// Neue bzw. geänderte Hausaufgabe wie im Original (Vorlage f32d5ffe): X, Titel, oranger Haken; Zeile „Text“ öffnet ein
/// eigenes Blatt, darunter Bemerkung (mit Hinweis), Fälligkeitsdatum und „Erledigt“.
struct HausaufgabeFormular: View {
    struct Vorlage: Identifiable {
        let id = UUID()
        let eintrag: Hausaufgabeneintrag?
        let stundenDatum: String
    }

    private enum Unterblatt: String, Identifiable {
        case text, datum
        var id: String { rawValue }
    }

    let vorlage: Vorlage
    let sichern: (String, Date, String, Bool) async throws -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }
    @State private var text = ""
    @State private var faellig = Date()
    @State private var bemerkung = ""
    @State private var erledigt = false
    @State private var unterblatt: Unterblatt? = nil
    @State private var speichert = false
    @State private var fehler: String? = nil

    private var stunde: Date { Datumsformat.datum(vorlage.stundenDatum) ?? Date() }

    var body: some View {
        NavigationStack {
            DetailSeitenRahmen(titel: vorlage.eintrag == nil ? "Neue Hausaufgabe" : "Hausaufgabe", untertitel: nil,
                               symbol: "xmark", zurueck: { schliessen() }, titelStil: .fett) {
                ScrollView {
                    VStack(spacing: 0) {
                        Formtrenner()
                            .padding(.top, 1)
                        Button { unterblatt = .text } label: { textZeile }
                            .buttonStyle(OhneTippStil())
                        Formtrenner()
                        bemerkungsfeld
                        hinweis
                        Formtrenner()
                        Button { unterblatt = .datum } label: { datumZeile }
                            .buttonStyle(OhneTippStil())
                        Formtrenner()
                        erledigtZeile
                        Formtrenner()
                        if let fehler {
                            Text(fehler)
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.entfallRand)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, Detailmass.rand)
                                .padding(.top, 12)
                        }
                    }
                }
                .scrollDismissesKeyboard(.interactively)
            } rechts: {
                // Wie im Original schon ohne Text orange; ohne Text weist das Sichern darauf hin
                SichernKnopf(speichert: speichert, aktiv: true, glas: true) {
                    Task { await speichern() }
                }
            }
            .onAppear {
                if let e = vorlage.eintrag {
                    text = e.text
                    bemerkung = e.bemerkung
                    erledigt = e.erledigt
                    faellig = e.faellig.flatMap { Datumsformat.datum($0) } ?? Datumsformat.tage(7, nach: stunde)
                } else {
                    faellig = Datumsformat.tage(7, nach: stunde)
                }
            }
        }
        .detailPraesentation(item: $unterblatt, breit: Geraet.istPad, art: .formular) { blatt in
            switch blatt {
            case .text:
                HausaufgabeTextBlatt(text: $text)
            case .datum:
                HausaufgabeDatumBlatt(datum: $faellig, ab: stunde)
            }
        }
    }

    /// Vorlage: Zeile 71 pt, „Text“ 15 pt hell, darunter der Text bzw. „Text hier eingeben“ 15 pt grau (Grundlinien 20 pt
    /// auseinander), Pfeil wie in der Detailliste 24,7 pt vom Rand
    private var textZeile: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 1.7) {
                Text("Text")
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.detailText)
                Text(text.isEmpty ? "Text hier eingeben" : text.replacingOccurrences(of: "\n", with: " "))
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.titelGrau)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
            Spacer(minLength: 0)
            formPfeil
        }
        .padding(.top, 16)
        .padding(.bottom, 17.4)
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, Detailmass.rand + 7.5)
        .contentShape(Rectangle())
    }

    /// Vorlage: „Bemerkung“ 15 pt grau 6 pt eingerückt, Grundlinie 35 pt unter der Linie; Feld bis zum Hinweis ≈ 90 pt
    private var bemerkungsfeld: some View {
        TextField("", text: $bemerkung, prompt: Text("Bemerkung").foregroundStyle(Farben.titelGrau), axis: .vertical)
            .font(.system(size: 15))
            .foregroundStyle(Farben.detailText)
            .lineLimit(1...8)
            .padding(.top, 19.7)
            .padding(.leading, Detailmass.rand + 5.67)
            .padding(.trailing, Detailmass.rand)
            .frame(maxWidth: .infinity, minHeight: 90.6, alignment: .topLeading)
    }

    /// Vorlage: ⓘ und Hinweis 13 pt (#dbdfe5) in einem Absatz (zweite Zeile beginnt unter dem Zeichen), Zeilen 18 pt
    private var hinweis: some View {
        Text("\(Image(systemName: "info.circle")) Bemerkung ist nicht sichtbar für Schüler*innen und Erziehungsberechtigte")
            .font(.system(size: 13))
            .lineSpacing(2.5)
            .foregroundStyle(Farben.detailGrau)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, Detailmass.rand)
            .padding(.bottom, 12.2)
    }

    /// Vorlage: Zeile 52,33 pt, „Fälligkeitsdatum“ 17 pt, Datum 13,7 pt grau 33 pt vor dem Pfeil
    private var datumZeile: some View {
        HStack(spacing: 0) {
            Text("Fälligkeitsdatum")
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
            Spacer(minLength: 12)
            Text(Datumsformat.anzeige(faellig, "dd.MM.yyyy"))
                .font(.system(size: 13.7))
                .foregroundStyle(Farben.detailGrau)
                .padding(.trailing, 32)
            formPfeil
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, Detailmass.rand + 7.5)
        .frame(height: 52.33)
        .contentShape(Rectangle())
    }

    /// Vorlage: Zeile 52,33 pt, „Erledigt“ 17 pt, Schalter 2 pt vor dem Ende der Linie
    private var erledigtZeile: some View {
        HStack(spacing: 0) {
            Text("Erledigt")
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
            Spacer(minLength: 12)
            Toggle("Erledigt", isOn: $erledigt)
                .labelsHidden()
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, Detailmass.rand + 2)
        .frame(height: 52.33)
    }

    private var formPfeil: some View {
        Image(systemName: "chevron.right")
            .font(.system(size: 23, weight: .thin))
            .foregroundStyle(Farben.detailSymbol)
    }

    private func speichern() async {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else {
            fehler = "Bitte einen Text eingeben."
            return
        }
        speichert = true
        defer { speichert = false }
        do {
            try await sichern(t, faellig, bemerkung.trimmingCharacters(in: .whitespacesAndNewlines), erledigt)
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Blatt „Text“ der Hausaufgabe (Vorlage ef7f726b): X, Titel, Haken (grau, bis Text da ist); Eingabe 17 pt mit grauem
/// Platzhalter „Text hier eingeben“ 16 pt vom Rand, 17 pt unter dem Kopf; Tastatur gleich offen.
struct HausaufgabeTextBlatt: View {
    @Binding var text: String
    @State private var entwurf = ""
    @FocusState private var fokus: Bool
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Text", symbol: "xmark", zurueck: { schliessen() }) {
            ScrollView {
                TextField("", text: $entwurf, prompt: Text("Text hier eingeben").foregroundStyle(Farben.titelGrau), axis: .vertical)
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.detailText)
                    .focused($fokus)
                    .padding(.horizontal, Detailmass.rand)
                    .padding(.top, 17.15)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollDismissesKeyboard(.interactively)
        } rechts: {
            SichernKnopf(speichert: false, aktiv: !entwurf.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && entwurf != text,
                         glas: true) {
                text = entwurf
                schliessen()
            }
        }
        .onAppear {
            entwurf = text
            fokus = true
        }
    }
}

/// Blatt „Fälligkeitsdatum“: Kalender ab dem Tag der Stunde, Haken übernimmt das Datum.
struct HausaufgabeDatumBlatt: View {
    @Binding var datum: Date
    let ab: Date
    @State private var entwurf = Date()
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Fälligkeitsdatum", symbol: "xmark", zurueck: { schliessen() }) {
            ScrollView {
                DatePicker("Fälligkeitsdatum", selection: $entwurf, in: min(ab, entwurf)..., displayedComponents: .date)
                    .datePickerStyle(.graphical)
                    .labelsHidden()
                    .tint(Farben.akzent)
                    .environment(\.locale, Locale(identifier: "de_DE"))
                    .padding(.horizontal, Detailmass.rand)
                    .padding(.top, 8)
            }
        } rechts: {
            SichernKnopf(speichert: false, aktiv: true, glas: true) {
                datum = entwurf
                schliessen()
            }
        }
        .onAppear { entwurf = datum }
    }
}

/// Hausaufgabe nur lesen (Vorlage 67283917): Zurück, Titel „Hausaufgabe“; Zeilen „Text“ (15 pt, Wert 15 pt dunkel,
/// Grundlinien 20 pt auseinander), „Aufgabedatum“ und „Fälligkeitsdatum“ (17 pt, Datum 13,7 pt grau 32,7 pt vom Rand),
/// jeweils mit Trennlinie.
struct HausaufgabeAnsicht: View {
    let eintrag: Hausaufgabeneintrag
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Hausaufgabe", zurueck: { schliessen() }, titelStil: .fett) {
            ScrollView {
                VStack(spacing: 0) {
                    Formtrenner()
                        .padding(.top, 1)
                    VStack(alignment: .leading, spacing: 1.7) {
                        Text("Text")
                            .font(.system(size: 15))
                        Text(eintrag.text)
                            .font(.system(size: 15))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .foregroundStyle(Farben.detailText)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.top, 16)
                    .padding(.bottom, 17.4)
                    .padding(.horizontal, Detailmass.rand)
                    Formtrenner()
                    datumZeile("Aufgabedatum", eintrag.datum)
                    Formtrenner()
                    if let f = eintrag.faellig, !f.isEmpty {
                        datumZeile("Fälligkeitsdatum", f)
                        Formtrenner()
                    }
                    if !eintrag.bemerkung.isEmpty {
                        VStack(alignment: .leading, spacing: 1.7) {
                            Text("Bemerkung")
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.detailText)
                            Text(eintrag.bemerkung)
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.detailGrau)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.top, 16)
                        .padding(.bottom, 17.4)
                        .padding(.horizontal, Detailmass.rand)
                        Formtrenner()
                    }
                }
            }
        }
    }

    private func datumZeile(_ titel: String, _ datum: String) -> some View {
        HStack(spacing: 12) {
            Text(titel)
                .font(.system(size: 17))
                .foregroundStyle(Farben.detailText)
            Spacer(minLength: 0)
            Text(Datumsformat.anzeige(datum, "dd.MM.yyyy"))
                .font(.system(size: 13.7))
                .foregroundStyle(Farben.detailGrau)
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, Detailmass.rand + 16.5)
        .frame(height: 52.33)
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
        DetailSeitenRahmen(titel: "Klassenbucheinträge", untertitel: stundenUntertitel(daten.karte), laedt: !daten.geladen) {
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
/// und Uhr (Abwesenheiten der Person). Wischen von links nach rechts: anwesend (grün), von rechts nach links: abwesend (rot)
/// bzw. bei abwesenden Schüler*innen „Entschuldigen“ (runde Pill, öffnet den Entschuldigungsdialog).
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
    /// Entschuldigungsdialog (Wischen nach links bei abwesenden Schüler*innen)
    @State private var entschuldigung: Entschuldigungsauftrag? = nil
    @State private var rechte = Entschuldigungsrechte()

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
        DetailSeitenRahmen(titel: "Schüler*innen", untertitel: daten.einzelstunde == nil ? stundenUntertitel(daten.karte)
                               : "Nur \(daten.bereichText)" + (daten.blockBeginn.isEmpty ? "" : ", \(daten.blockBeginn) - \(daten.blockEnde)"),
                           laedt: liste == nil) {
            inhalt
        }
        .detailPraesentation(item: $eintrag, breit: Geraet.istPad, art: .formular) { v in
            NeuerKlassenbucheintragView(vorauswahl: v.schueler)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .navigationDestination(item: $abwesenheitenVon) { s in
            SchuelerAbwesenheitenView(schueler: s)
                .environmentObject(daten)
                .environmentObject(zustand)
                .environment(\.unterblatt, false)
        }
        .detailPraesentation(item: $entschuldigung, breit: Geraet.istPad, art: .formular) { a in
            EntschuldigenView(auftrag: a) {
                Task { await daten.anwesenheitLaden() }
            }
            .environmentObject(zustand)
        }
        .task { rechte = await zustand.entschuldigungsrechteLaden() }
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
            Segmentschalter(auswahl: $filter, [
                (Filter.alle, "Alle (\(alle.count))"),
                (Filter.abwesend, "Abwesend (\(abwesend))"),
                (Filter.anwesend, "Anwesend (\(alle.count - abwesend))"),
            ])
            .padding(.horizontal, 14)
            .padding(.top, 2)
            .padding(.bottom, 8)
            kontrollLeiste
            if liste == nil {
                // Lädt noch (Ladebalken unter dem Kopf)
                Color.clear
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    List {
                        ForEach(gruppen) { gruppe in
                            Section {
                                ForEach(gruppe.liste) { s in
                                    // Wischen wie im Original: nach rechts anwesend (grün, Haken), nach links abwesend
                                    // (rot, Uhr); die Farbe läuft über die ganze Zeilenbreite
                                    // – bei abwesenden Schüler*innen nach links: „Entschuldigen“ (Klassenleitung bzw. Fachlehrkraft)
                                    let fehlzeit = daten.fehlzeit(s)
                                    let abwesend = fehlzeit != nil
                                    WischZeile(rechtsMoeglich: bearbeitbar && abwesend,
                                               linksMoeglich: abwesend ? darfEntschuldigen(s) : bearbeitbar,
                                               rechts: { Task { await ausfuehren(s) { try await daten.anwesendSetzen(s) } } },
                                               links: {
                                                   if let f = fehlzeit {
                                                       entschuldigung = Entschuldigungsauftrag(
                                                           ziel: .fehlzeit(id: f.id, klasse: s.klasse, datum: daten.karte.datum),
                                                           name: s.anzeige, klasse: s.klasse)
                                                   } else {
                                                       Task { await ausfuehren(s) { try await daten.abwesendSetzen(s) } }
                                                   }
                                               },
                                               linksAktion: abwesend ? .entschuldigen : .abwesend) {
                                        zeile(s)
                                            .padding(.leading, 16)
                                            .padding(.trailing, 26)
                                    }
                                    .listRowInsets(EdgeInsets())
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
                    .font(.system(size: 18))
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
        .frame(minHeight: 60)
        .contentShape(Rectangle())
    }

    /// Entschuldigen: Klassenleitung der Klasse oder Fachlehrkraft dieser Stunde (Modul aktiv).
    private func darfEntschuldigen(_ s: Schueler) -> Bool {
        rechte.leitet(s.klasse) || (rechte.fachlehrer && liste?.darf == true)
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

// MARK: - Wischzeile (An-/Abwesenheit, Entschuldigen)

/// Beschriftung einer Wischrichtung (Symbol, Text, Farbe der runden Pill).
struct Wischaktion {
    var symbol: String
    var text: String
    var farbe: Color

    static let anwesend = Wischaktion(symbol: "checkmark.circle", text: "Anwesend", farbe: Farben.gruen)
    static let abwesend = Wischaktion(symbol: "clock", text: "Abwesend", farbe: Farben.entfallRand)
    static let entschuldigen = Wischaktion(symbol: "checkmark.seal", text: "Entschuldigen", farbe: Farben.entschuldigen)
}

/// Zeile mit Wischgeste wie im Original: nach rechts → anwesend (grüne Fläche mit Haken), nach links → abwesend (rote
/// Fläche mit Uhr) bzw. – bei abwesenden Schüler*innen und in Abwesenheitslisten – „Entschuldigen“. Ab 80 pt löst das
/// Loslassen aus; beim Erreichen der Schwelle und beim Auslösen gibt es eine Haptik. Senkrechtes Scrollen der Liste
/// bleibt frei (Richtung wird zu Beginn festgelegt); in die gesperrte Richtung gibt die Zeile nur mit Widerstand nach.
/// Für VoiceOver gibt es dieselben Aktionen.
struct WischZeile<Inhalt: View>: View {
    typealias Aktion = Wischaktion

    let rechtsMoeglich: Bool
    let linksMoeglich: Bool
    let rechts: () -> Void
    let links: () -> Void
    var rechtsAktion: Aktion = .anwesend
    var linksAktion: Aktion = .abwesend
    let inhalt: Inhalt

    @State private var versatz: CGFloat = 0
    @State private var waagrecht: Bool? = nil
    @State private var ueberSchwelle = false
    @State private var ausgeloest = 0
    @GestureState private var zieht = false
    private let schwelle: CGFloat = 80

    init(rechtsMoeglich: Bool, linksMoeglich: Bool, rechts: @escaping () -> Void, links: @escaping () -> Void,
         rechtsAktion: Aktion = .anwesend, linksAktion: Aktion = .abwesend,
         @ViewBuilder inhalt: () -> Inhalt) {
        self.rechtsMoeglich = rechtsMoeglich
        self.linksMoeglich = linksMoeglich
        self.rechts = rechts
        self.links = links
        self.rechtsAktion = rechtsAktion
        self.linksAktion = linksAktion
        self.inhalt = inhalt()
    }

    var body: some View {
        inhalt
            .background(Farben.blatt)
            .offset(x: versatz)
            .background { hintergrund }
            .clipped()
            .simultaneousGesture(geste)
            .onChange(of: zieht) { _, neu in
                // Geste abgebrochen (z. B. von der Liste übernommen): zurückfedern
                guard !neu else { return }
                waagrecht = nil
                ueberSchwelle = false
                if versatz != 0 { withAnimation(.spring(response: 0.3, dampingFraction: 0.86)) { versatz = 0 } }
            }
            .sensoryFeedback(.selection, trigger: ueberSchwelle)
            .sensoryFeedback(.success, trigger: ausgeloest)
            .accessibilityActions {
                if rechtsMoeglich { Button(rechtsAktion.text) { rechts() } }
                if linksMoeglich { Button(linksAktion.text) { links() } }
            }
    }

    /// Hinter der Zeile erscheint – wie bei den neueren, runden Pills der App – eine voll abgerundete Kapsel, die mit dem
    /// Wischweg wächst (grün „Anwesend“ mit Haken bzw. rot „Abwesend“ mit Uhr; gesperrte Richtung grau). Ab der Schwelle
    /// wird die Kapsel kräftiger und das Symbol größer.
    @ViewBuilder
    private var hintergrund: some View {
        if versatz > 0 {
            HStack(spacing: 0) {
                wischPill(symbol: rechtsAktion.symbol, text: rechtsAktion.text,
                          farbe: rechtsMoeglich ? rechtsAktion.farbe : Farben.zahlPlakette, breite: versatz)
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if versatz < 0 {
            HStack(spacing: 0) {
                Spacer(minLength: 0)
                wischPill(symbol: linksAktion.symbol, text: linksAktion.text,
                          farbe: linksMoeglich ? linksAktion.farbe : Farben.zahlPlakette, breite: -versatz)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    /// Runde Kapsel im freigelegten Bereich (8 pt Abstand zu Rand und Zeile); Text erst, wenn genug Platz ist.
    private func wischPill(symbol: String, text: String, farbe: Color, breite: CGFloat) -> some View {
        let innen = max(breite - 12, 0)
        return HStack(spacing: 6) {
            Image(systemName: symbol)
                .font(.system(size: 20, weight: .regular))
                .scaleEffect(ueberSchwelle ? 1.15 : 1)
            if innen > CGFloat(text.count) * 8.6 + 44 {
                Text(text)
                    .font(.system(size: 15, weight: .medium))
                    .lineLimit(1)
                    .fixedSize()
                    .transition(.opacity)
            }
        }
        .foregroundStyle(.white)
        .frame(width: innen)
        .frame(maxHeight: .infinity)
        .background(farbe.opacity(ueberSchwelle ? 1 : 0.85), in: Capsule())
        .clipShape(Capsule())
        .padding(.vertical, 6)
        .padding(.horizontal, 6)
        .opacity(innen > 4 ? 1 : 0)
        .accessibilityHidden(true)
    }

    private var geste: some Gesture {
        DragGesture(minimumDistance: 14)
            .updating($zieht) { _, status, _ in status = true }
            .onChanged { wert in
                let dx = wert.translation.width
                let dy = wert.translation.height
                if waagrecht == nil {
                    waagrecht = abs(dx) > abs(dy) * 1.3
                }
                guard waagrecht == true else { return }
                let erlaubt = dx > 0 ? rechtsMoeglich : linksMoeglich
                versatz = erlaubt ? dx : dx / 6
                let neu = erlaubt && abs(dx) > schwelle
                if neu != ueberSchwelle {
                    withAnimation(.easeOut(duration: 0.12)) { ueberSchwelle = neu }
                }
            }
            .onEnded { _ in
                let war = waagrecht == true
                waagrecht = nil
                let ausloesen = war && ueberSchwelle
                let nachRechts = versatz > 0
                ueberSchwelle = false
                withAnimation(.spring(response: 0.3, dampingFraction: 0.86)) { versatz = 0 }
                guard ausloesen else { return }
                ausgeloest += 1
                if nachRechts { rechts() } else { links() }
            }
    }
}
