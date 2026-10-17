import PhotosUI
import SwiftUI
import UIKit
import UniformTypeIdentifiers

// MARK: - Start einer neuen Mitteilung

/// Art der neuen Mitteilung („An wen soll die Mitteilung gesendet werden?“) bzw. Antwort.
enum Verfassenstyp: Hashable {
    case kollegium
    case individuell
    case antwort

    /// Parameter „typ“ von GET /mitteilungen/personen
    var apiTyp: String { self == .kollegium ? "kollegium" : "individuell" }

    /// Untertitel der Empfängerauswahl („Mitteilung an“ / „Kolleg*innen“).
    var auswahlTitel: String { self == .kollegium ? "Kolleg*innen" : "Individuell" }

    /// Titel des Editors.
    var editorTitel: String {
        switch self {
        case .kollegium: return "Mitteilung an Kolleg*innen"
        case .individuell: return "Individuelle Mitteilung"
        case .antwort: return "Antwort"
        }
    }
}

/// Startwerte des Blatts „Mitteilung verfassen“ (neu: erst die Empfängerauswahl; Antwort: gleich der Editor).
struct Verfassen: Identifiable {
    let id = UUID()
    var typ: Verfassenstyp
    var empfaenger: [Empfaenger] = []
    var betreff = ""
    var text = ""
    var antwortAuf: Int? = nil
}

/// Datei, die beim Senden an die Mitteilung gehängt wird (Kamera, Foto, Datei).
struct LokalerAnhang: Identifiable {
    let id = UUID()
    let name: String
    let typ: String
    let daten: Data

    var anzeige: Anhang { Anhang(id: id.hashValue, name: name, typ: typ, groesse: daten.count) }
}

// MARK: - Blatt: Empfängerauswahl und Editor

/// Neue Mitteilung im Blatt (Vorlagen 143/144): zuerst die Empfängerauswahl, nach dem Haken der Editor; die Zeile „An“
/// im Editor öffnet die Auswahl erneut. Mit Anhängen: Entwurf anlegen, Dateien hochladen, dann senden.
struct MitteilungVerfassenView: View {
    let start: Verfassen
    let gesendet: () -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    private enum Schritt { case auswahl, editor }

    @State private var schritt: Schritt
    @State private var empfaenger: [Empfaenger]
    @State private var betreff: String
    @State private var text: String
    @State private var lesebestaetigung = false
    @State private var antwortGesperrt = false
    @State private var anhaenge: [LokalerAnhang] = []
    @State private var personen: Personenauswahl? = nil
    @State private var ladefehler: String? = nil
    @State private var sendet = false
    @State private var fehler: String? = nil
    @State private var verwerfenFragen = false
    @State private var fotoAuswahl: [PhotosPickerItem] = []
    @State private var fotosOffen = false
    @State private var dateienOffen = false
    @State private var kameraOffen = false
    @FocusState private var fokus: Feld?

    private enum Feld { case betreff, text }

    init(start: Verfassen, gesendet: @escaping () -> Void) {
        self.start = start
        self.gesendet = gesendet
        _schritt = State(initialValue: start.typ == .antwort || !start.empfaenger.isEmpty ? .editor : .auswahl)
        _empfaenger = State(initialValue: start.empfaenger)
        _betreff = State(initialValue: start.betreff)
        _text = State(initialValue: start.text)
    }

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var bereit: Bool {
        !empfaenger.isEmpty && !betreff.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && (!text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !anhaenge.isEmpty)
    }

    private var hatInhalt: Bool {
        betreff != start.betreff || text != start.text || !anhaenge.isEmpty
    }

    var body: some View {
        ZStack {
            switch schritt {
            case .auswahl:
                EmpfaengerauswahlView(typ: start.typ, daten: personen, fehler: ladefehler, vorher: empfaenger,
                                      abbrechen: {
                                          if empfaenger.isEmpty && start.typ != .antwort {
                                              schliessen()
                                          } else {
                                              withAnimation(.easeInOut(duration: 0.2)) { schritt = .editor }
                                          }
                                      },
                                      fertig: { neu in
                                          empfaenger = neu
                                          withAnimation(.easeInOut(duration: 0.2)) { schritt = .editor }
                                      })
                    .transition(.opacity)
            case .editor:
                editor
                    .transition(.opacity)
            }
        }
        .background(Farben.blatt.ignoresSafeArea())
        .task { await personenLaden() }
    }

    private func personenLaden() async {
        guard personen == nil else { return }
        do {
            personen = try await zustand.api.personen(typ: start.typ.apiTyp)
            ladefehler = nil
        } catch {
            ladefehler = zustand.fehlertext(error) ?? "Die Empfänger konnten nicht geladen werden."
        }
    }

    // MARK: Editor (Vorlage 144)

    private var editor: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: {
                if hatInhalt { verwerfenFragen = true } else { schliessen() }
            }, laedt: sendet, mitte: {
                Mitteilungstitel(titel: start.typ.editorTitel)
            }, rechts: {
                sendenKnopf
            })
            Rectangle().fill(MitteilungDetailView.trennFarbe).frame(height: 1)
            anZeile
            Rectangle().fill(MitteilungDetailView.trennFarbe).frame(height: 1)
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    TextField("Betreff", text: $betreff)
                        .font(.system(size: 20))
                        .foregroundStyle(Farben.tinte)
                        .tint(Farben.akzent)
                        .focused($fokus, equals: .betreff)
                        .submitLabel(.next)
                        .onSubmit { fokus = .text }
                    TextField("Text hier eingeben", text: $text, axis: .vertical)
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.tinte)
                        .tint(Farben.akzent)
                        .lineLimit(6...)
                        .focused($fokus, equals: .text)
                    if !anhaenge.isEmpty {
                        VStack(spacing: 10) {
                            ForEach(anhaenge) { a in
                                Anhangpille(anhang: a.anzeige, entfernen: {
                                    anhaenge.removeAll { $0.id == a.id }
                                }) {}
                            }
                        }
                        .padding(.top, 10)
                    }
                    if let fehler {
                        Fehlerhinweis(text: fehler)
                    }
                }
                .padding(.horizontal, 16.5)
                .padding(.top, 24)
                .padding(.bottom, 24)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollDismissesKeyboard(.interactively)
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            werkzeuge
        }
        .onAppear {
            if fokus == nil { fokus = betreff.isEmpty ? .betreff : .text }
        }
        .confirmationDialog("Mitteilung verwerfen?", isPresented: $verwerfenFragen, titleVisibility: .visible) {
            Button("Als Entwurf sichern") { Task { await entwurfSichern() } }
            Button("Verwerfen", role: .destructive) { schliessen() }
            Button("Weiter bearbeiten", role: .cancel) {}
        }
        .photosPicker(isPresented: $fotosOffen, selection: $fotoAuswahl, maxSelectionCount: 5, matching: .images)
        .onChange(of: fotoAuswahl) { _, neu in
            guard !neu.isEmpty else { return }
            Task { await fotosUebernehmen(neu) }
        }
        .fileImporter(isPresented: $dateienOffen, allowedContentTypes: [.item], allowsMultipleSelection: true) { ergebnis in
            dateienUebernehmen(ergebnis)
        }
        .fullScreenCover(isPresented: $kameraOffen) {
            KameraAufnahme { bild in
                if let bild, let daten = bild.jpegData(compressionQuality: 0.85) {
                    anhaenge.append(LokalerAnhang(name: Self.dateiname("Foto", "jpg"), typ: "image/jpeg", daten: daten))
                }
                kameraOffen = false
            }
            .ignoresSafeArea()
        }
    }

    /// Senden: Papierflieger im runden Glasknopf (orange, sobald Empfänger, Betreff und Text vorhanden sind).
    @ViewBuilder
    private var sendenKnopf: some View {
        if sendet {
            ProgressView()
                .frame(width: Detailmass.knopf, height: Detailmass.knopf)
        } else {
            Button {
                Task { await senden() }
            } label: {
                Image(systemName: "paperplane")
                    .font(.system(size: 21, weight: .light))
                    .foregroundStyle(bereit ? Farben.akzent : Color(hell: 0x9AA5AE, dunkel: 0x616063))
                    .frame(width: Detailmass.knopf, height: Detailmass.knopf)
                    .liquidGlas(Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .disabled(!bereit)
            .accessibilityLabel("Senden")
        }
    }

    /// „An [ABB] >“ (Vorlage 144: Zeile 44 pt zwischen zwei Trennlinien, „An“ 15 pt, Kapsel 32,3 pt hoch #DDDEE4 mit
    /// 16,6 pt Innenabstand, Pfeil 26,7 pt vom rechten Rand) – antippen öffnet die Auswahl.
    private var anZeile: some View {
        Button {
            fokus = nil
            withAnimation(.easeInOut(duration: 0.2)) { schritt = .auswahl }
        } label: {
            HStack(spacing: 17) {
                Text("An")
                    .font(.system(size: 15))
                    .foregroundStyle(Color(hell: 0x48606F, dunkel: 0xE3E2E9))
                ScrollView(.horizontal) {
                    HStack(spacing: 8) {
                        if empfaenger.isEmpty {
                            Text("Empfänger wählen")
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.gedaempft)
                        }
                        ForEach(empfaenger) { e in
                            Text(e.kuerzel)
                                .font(.system(size: 15))
                                .foregroundStyle(Color(hell: 0x253746, dunkel: 0x0C1216))
                                .lineLimit(1)
                                .padding(.horizontal, 16.6)
                                .frame(height: 32.3)
                                .background(Color(hell: 0xE3E7EB, dunkel: 0xDDDEE4), in: Capsule())
                        }
                    }
                }
                .scrollIndicators(.hidden)
                .allowsHitTesting(empfaenger.count > 3)
                Image(systemName: "chevron.right")
                    .font(.system(size: 22, weight: .light))
                    .foregroundStyle(Color(hell: 0x48606F, dunkel: 0xC7CBD3))
            }
            .padding(.leading, 16.2)
            .padding(.trailing, 26.7)
            .frame(height: 44)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("An: " + (empfaenger.isEmpty ? "keine Empfänger" : empfaenger.map(\.kuerzel).joined(separator: ", ")))
        .accessibilityHint("Öffnet die Empfängerauswahl")
    }

    /// Über der Tastatur: links Lesebestätigung anfordern | Antwort nicht erlaubt, rechts Kamera | Foto | Datei.
    private var werkzeuge: some View {
        HStack {
            Glasgruppe {
                Glasgruppenknopf(symbol: "envelope", text: "Lesebestätigung anfordern", aktiv: lesebestaetigung,
                                 zusatz: "questionmark.circle") {
                    lesebestaetigung.toggle()
                    hinweisZeigen(lesebestaetigung ? "Lesebestätigung wird angefordert" : "Keine Lesebestätigung")
                }
                if start.typ != .antwort || zustand.istKollegium {
                    Glasgruppenknopf(symbol: "envelope", text: "Antwort nicht erlaubt", aktiv: antwortGesperrt,
                                     zusatz: "xmark.circle") {
                        antwortGesperrt.toggle()
                        hinweisZeigen(antwortGesperrt ? "Antworten nicht erlaubt" : "Antworten erlaubt")
                    }
                }
            }
            Spacer(minLength: 12)
            Glasgruppe {
                if UIImagePickerController.isSourceTypeAvailable(.camera) {
                    Glasgruppenknopf(symbol: "camera", text: "Foto aufnehmen") { kameraOffen = true }
                }
                Glasgruppenknopf(symbol: "photo.on.rectangle", text: "Foto auswählen") { fotosOffen = true }
                Glasgruppenknopf(symbol: "paperclip", text: "Datei anhängen") { dateienOffen = true }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 8)
        .padding(.bottom, 9)
        .overlay(alignment: .top) {
            if let hinweis {
                Text(hinweis)
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(Color.black.opacity(0.78), in: Capsule())
                    .offset(y: -44)
                    .transition(.opacity)
                    .allowsHitTesting(false)
            }
        }
    }

    @State private var hinweis: String? = nil

    private func hinweisZeigen(_ text: String) {
        withAnimation { hinweis = text }
        Task {
            try? await Task.sleep(nanoseconds: 1_600_000_000)
            if hinweis == text { withAnimation { hinweis = nil } }
        }
    }

    // MARK: Anhänge

    static func dateiname(_ anfang: String, _ endung: String) -> String {
        "\(anfang)_\(Datumsformat.anzeige(Date(), "yyyyMMdd_HHmmss")).\(endung)"
    }

    private func fotosUebernehmen(_ liste: [PhotosPickerItem]) async {
        for (i, eintrag) in liste.enumerated() {
            guard let daten = try? await eintrag.loadTransferable(type: Data.self) else { continue }
            // Als JPEG anhängen (HEIC u. a. öffnen nicht alle Empfänger)
            let jpeg = UIImage(data: daten)?.jpegData(compressionQuality: 0.85) ?? daten
            let name = Self.dateiname(liste.count > 1 ? "Foto\(i + 1)" : "Foto", "jpg")
            anhaenge.append(LokalerAnhang(name: name, typ: "image/jpeg", daten: jpeg))
        }
        fotoAuswahl = []
    }

    private func dateienUebernehmen(_ ergebnis: Result<[URL], Error>) {
        guard case .success(let urls) = ergebnis else {
            if case .failure(let f) = ergebnis { fehler = f.localizedDescription }
            return
        }
        for url in urls {
            let zugriff = url.startAccessingSecurityScopedResource()
            defer { if zugriff { url.stopAccessingSecurityScopedResource() } }
            guard let daten = try? Data(contentsOf: url) else {
                fehler = "„\(url.lastPathComponent)“ konnte nicht gelesen werden."
                continue
            }
            let typ = UTType(filenameExtension: url.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
            anhaenge.append(LokalerAnhang(name: url.lastPathComponent, typ: typ, daten: daten))
        }
    }

    // MARK: Senden

    private func daten(id: Int? = nil) -> MitteilungNeuDaten {
        MitteilungNeuDaten(id: id, empfaenger: empfaenger.map(\.id),
                           betreff: betreff.trimmingCharacters(in: .whitespacesAndNewlines),
                           text: text.trimmingCharacters(in: .whitespacesAndNewlines),
                           wichtig: false, lesebestaetigung: lesebestaetigung, antwortAuf: start.antwortAuf,
                           antwortErlaubt: antwortGesperrt ? false : nil)
    }

    private func senden() async {
        guard bereit else { return }
        sendet = true
        defer { sendet = false }
        fokus = nil
        do {
            if anhaenge.isEmpty {
                try await zustand.api.mitteilungSenden(daten())
            } else {
                let id = try await zustand.api.entwurfSpeichern(daten())
                for a in anhaenge {
                    try await zustand.api.anhangHochladen(mitteilung: id, name: a.name, typ: a.typ, daten: a.daten)
                }
                try await zustand.api.entwurfSenden(id)
            }
            gesendet()
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error) ?? "Die Mitteilung konnte nicht gesendet werden."
        }
    }

    private func entwurfSichern() async {
        sendet = true
        defer { sendet = false }
        do {
            let id = try await zustand.api.entwurfSpeichern(daten())
            for a in anhaenge {
                try await zustand.api.anhangHochladen(mitteilung: id, name: a.name, typ: a.typ, daten: a.daten)
            }
            gesendet()
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error) ?? "Der Entwurf konnte nicht gesichert werden."
        }
    }
}

// MARK: - Empfängerauswahl (Vorlage 143)

/// Empfängerauswahl als Blatt: X links, Haken rechts, Titel „Mitteilung an“ / „Kolleg*innen“; darunter waagrecht
/// scrollbare Filter-Kapseln (Ausgewählt, Liste, Klasse, Abteilung), „Alle auswählen“ und die Personen mit Auswahlkreis,
/// Avatar, Kürzel und Rollen-Kapseln; unten schwebt das gläserne Suchfeld.
struct EmpfaengerauswahlView: View {
    let typ: Verfassenstyp
    let daten: Personenauswahl?
    let fehler: String?
    /// Bisher gewählte Empfänger (bleiben erhalten, auch wenn sie nicht in der Liste stehen, z. B. bei einer Antwort)
    let vorher: [Empfaenger]
    let abbrechen: () -> Void
    let fertig: ([Empfaenger]) -> Void

    @State private var auswahl: Set<String>
    @State private var nurAusgewaehlt = false
    @State private var liste: Mitteilungsliste? = nil
    @State private var klasse: String? = nil
    @State private var abteilung: String? = nil
    @State private var suche = ""
    @FocusState private var sucheFokus: Bool

    init(typ: Verfassenstyp, daten: Personenauswahl?, fehler: String?, vorher: [Empfaenger],
         abbrechen: @escaping () -> Void, fertig: @escaping ([Empfaenger]) -> Void) {
        self.typ = typ
        self.daten = daten
        self.fehler = fehler
        self.vorher = vorher
        self.abbrechen = abbrechen
        self.fertig = fertig
        _auswahl = State(initialValue: Set(vorher.map(\.id)))
    }

    private static func gleich(_ a: String, _ b: String) -> Bool { a.caseInsensitiveCompare(b) == .orderedSame }

    private var personen: [Empfaenger] {
        (daten?.personen ?? []).sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    private func gefiltert(_ quelle: [Empfaenger]) -> [Empfaenger] {
        var l = quelle
        if nurAusgewaehlt { l = l.filter { auswahl.contains($0.id) } }
        if let liste {
            let mitglieder = Set(liste.mitglieder)
            l = l.filter { mitglieder.contains($0.id) }
        }
        if let klasse { l = l.filter { $0.alleKlassen.contains { Self.gleich($0, klasse) } } }
        if let abteilung { l = l.filter { $0.abteilungen.contains { Self.gleich($0, abteilung) } } }
        if !suche.isEmpty { l = l.filter { $0.passt(suche) } }
        return l
    }

    private var sichtbarePersonen: [Empfaenger] { gefiltert(personen) }

    /// Gruppen (nur „Individuell“): Listen wirken als Filter, deshalb hier nur Suche, Klasse und „Ausgewählt“.
    private var sichtbareGruppen: [Empfaenger] {
        var l = daten?.gruppen ?? []
        if nurAusgewaehlt { l = l.filter { auswahl.contains($0.id) } }
        if let klasse { l = l.filter { $0.alleKlassen.contains { Self.gleich($0, klasse) } } }
        if liste != nil || abteilung != nil { l = [] }
        if !suche.isEmpty { l = l.filter { $0.passt(suche) } }
        return l
    }

    private var sichtbar: [Empfaenger] { sichtbareGruppen + sichtbarePersonen }

    private var alleGewaehlt: Bool { !sichtbar.isEmpty && sichtbar.allSatisfy { auswahl.contains($0.id) } }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Abbrechen", aktion: abbrechen, laedt: daten == nil && fehler == nil, mitte: {
                Mitteilungstitel(titel: "Mitteilung an", untertitel: typ.auswahlTitel)
            }, rechts: {
                Button {
                    fertig(ergebnis())
                } label: {
                    Rundknopf(symbol: "checkmark", gewicht: .light, groesse: 21)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(auswahl.isEmpty ? "Fertig" : "Fertig, \(auswahl.count) ausgewählt")
            })
            filterleiste
            personenliste(inhalt: sichtbar)
        }
        .background(Farben.blatt.ignoresSafeArea())
        .safeAreaInset(edge: .bottom, spacing: 0) {
            suchfeld
        }
    }

    /// Ergebnis in der Reihenfolge der Liste; bisher gewählte, hier unbekannte Empfänger bleiben vorn erhalten.
    private func ergebnis() -> [Empfaenger] {
        let bekannt = (daten?.gruppen ?? []) + personen
        let ids = Set(bekannt.map(\.id))
        let alt = vorher.filter { auswahl.contains($0.id) && !ids.contains($0.id) }
        return alt + bekannt.filter { auswahl.contains($0.id) }
    }

    // MARK: Filter

    private var filterleiste: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 8) {
                Button {
                    withAnimation(.easeOut(duration: 0.15)) { nurAusgewaehlt.toggle() }
                } label: {
                    Filterkapsel(titel: auswahl.isEmpty ? "Ausgewählt" : "Ausgewählt (\(auswahl.count))", aktiv: nurAusgewaehlt, pfeil: false)
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(nurAusgewaehlt ? .isSelected : [])

                Menu {
                    Button("Alle") { liste = nil }
                    let listen = daten?.filter.listen ?? []
                    if listen.isEmpty {
                        Text("Keine Listen angelegt")
                    }
                    ForEach(listen) { l in
                        Button {
                            liste = l
                        } label: {
                            if liste?.id == l.id { Label(l.name, systemImage: "checkmark") } else { Text(l.name) }
                        }
                    }
                } label: {
                    Filterkapsel(titel: liste?.name ?? "Liste", aktiv: liste != nil, pfeil: true)
                }

                Menu {
                    Button("Alle") { klasse = nil }
                    ForEach(daten?.filter.klassen ?? [], id: \.self) { k in
                        Button {
                            klasse = k
                        } label: {
                            if klasse == k { Label(k, systemImage: "checkmark") } else { Text(k) }
                        }
                    }
                } label: {
                    Filterkapsel(titel: klasse ?? "Klasse", aktiv: klasse != nil, pfeil: true)
                }

                // Abteilung nur, wenn die Schule Abteilungen pflegt
                if !(daten?.filter.abteilungen ?? []).isEmpty {
                    Menu {
                        Button("Alle") { abteilung = nil }
                        ForEach(daten?.filter.abteilungen ?? [], id: \.self) { a in
                            Button {
                                abteilung = a
                            } label: {
                                if abteilung == a { Label(a, systemImage: "checkmark") } else { Text(a) }
                            }
                        }
                    } label: {
                        Filterkapsel(titel: abteilung ?? "Abteilung", aktiv: abteilung != nil, pfeil: true)
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 2)
        }
        .scrollIndicators(.hidden)
        .tint(Color.primary)
        .padding(.top, 8)
        .padding(.bottom, 10)
    }

    // MARK: Liste

    private func personenliste(inhalt: [Empfaenger]) -> some View {
        List {
            if let fehler {
                Fehlerhinweis(text: fehler)
                    .auswahlzeile()
            }
            if !inhalt.isEmpty {
                Button {
                    if alleGewaehlt {
                        auswahl.subtract(inhalt.map(\.id))
                    } else {
                        auswahl.formUnion(inhalt.map(\.id))
                    }
                } label: {
                    HStack(spacing: 18.8) {
                        Auswahlkreis(gewaehlt: alleGewaehlt)
                        Text("Alle auswählen")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                        Spacer(minLength: 0)
                    }
                    .padding(.vertical, 17)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .auswahlzeile()
            } else if daten != nil {
                Text(suche.isEmpty ? "Keine Empfänger." : "Keine Treffer für „\(suche)“.")
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.gedaempft)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 30)
                    .auswahlzeile()
            }
            ForEach(inhalt) { e in
                Personenzeile(empfaenger: e, gewaehlt: auswahl.contains(e.id)) {
                    if auswahl.contains(e.id) { auswahl.remove(e.id) } else { auswahl.insert(e.id) }
                }
                .auswahlzeile()
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .scrollDismissesKeyboard(.immediately)
        .environment(\.defaultMinListRowHeight, 0)
    }

    /// Gläsernes Suchfeld unten (Vorlage 143: 49 pt hoch, 26 pt von den Seiten, Unterkante 26,5 pt über dem
    /// Bildschirmrand; Lupe 22 pt vom Feldrand, „Suchen“ 17 pt knapp 5 pt daneben).
    private var suchfeld: some View {
        HStack(spacing: 5) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 20, weight: .regular))
                .foregroundStyle(Farben.tinte)
            TextField("Suchen", text: $suche)
                .font(.system(size: 17))
                .foregroundStyle(Farben.tinte)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.search)
                .focused($sucheFokus)
            if !suche.isEmpty {
                Button {
                    suche = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 18))
                        .foregroundStyle(Farben.gedaempft)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Suche leeren")
            }
        }
        .padding(.leading, 22)
        .padding(.trailing, 18)
        .frame(height: 49)
        .liquidGlas(Capsule())
        .contentShape(Capsule())
        .onTapGesture { sucheFokus = true }
        .padding(.horizontal, 26)
        .padding(.top, 6)
        // Am iPhone liegt das Feld etwas in den unteren sicheren Bereich hinein (wie im Original)
        .offset(y: 7.5)
    }
}

/// Filter-Kapsel mit Rand („Liste ⌄“), Vorlage 143: 42 pt hoch, 16 pt Innenabstand, Text 15 pt #E6E8EC, Pfeil 11 × 6,6 pt,
/// Rand 1 pt #888E95; 8 pt Abstand zwischen den Kapseln. Aktiv leicht hinterlegt.
private struct Filterkapsel: View {
    let titel: String
    let aktiv: Bool
    let pfeil: Bool

    var body: some View {
        HStack(spacing: 9) {
            Text(titel)
                .font(.system(size: 15))
                .lineLimit(1)
            if pfeil {
                Image(systemName: "chevron.down")
                    .font(.system(size: 12, weight: .medium))
            }
        }
        .foregroundStyle(Color(hell: 0x253746, dunkel: 0xE6E8EC))
        .padding(.horizontal, 16)
        .frame(height: 42)
        .background(aktiv ? Farben.knopf : Color.clear, in: Capsule())
        .overlay {
            Capsule().strokeBorder(aktiv ? Farben.tinte2 : Color(hell: 0xB7C0C7, dunkel: 0x888E95), lineWidth: 1)
        }
        .contentShape(Capsule())
    }
}

/// Runder Auswahlkreis (leer bzw. orange mit Haken).
struct Auswahlkreis: View {
    let gewaehlt: Bool

    var body: some View {
        ZStack {
            if gewaehlt {
                Circle().fill(Farben.akzent)
                Image(systemName: "checkmark")
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(.white)
            } else {
                Circle().strokeBorder(Color(hell: 0x9AA5AE, dunkel: 0x8F949E), lineWidth: 1.2)
            }
        }
        .frame(width: 24.5, height: 24.5)
        .accessibilityHidden(true)
    }
}

/// Zeile der Empfängerauswahl: Auswahlkreis, grauer Avatar mit Initialen, Kürzel und Rollen-Kapseln.
struct Personenzeile: View {
    let empfaenger: Empfaenger
    let gewaehlt: Bool
    let umschalten: () -> Void

    var body: some View {
        Button(action: umschalten) {
            HStack(spacing: 18.4) {
                Auswahlkreis(gewaehlt: gewaehlt)
                HStack(spacing: 8.7) {
                    Group {
                        if empfaenger.istPerson {
                            Text(empfaenger.initialen)
                                .font(.system(size: 17.5))
                                .lineLimit(1)
                                .minimumScaleFactor(0.6)
                        } else {
                            Image(systemName: empfaenger.id.hasPrefix("liste:") ? "list.bullet" : "person.3")
                                .font(.system(size: 18, weight: .light))
                        }
                    }
                    .foregroundStyle(.white)
                    .frame(width: Mitteilungszeile.avatarGroesse, height: Mitteilungszeile.avatarGroesse)
                    .background(Mitteilungszeile.avatarFarbe, in: Circle())
                    VStack(alignment: .leading, spacing: 4) {
                        Text(empfaenger.istPerson ? empfaenger.kuerzel : empfaenger.name)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(1)
                        let pills = empfaenger.pills
                        if !pills.isEmpty {
                            Umbruchlayout(abstand: 4.4, zeilenabstand: 4) {
                                ForEach(Array(pills.enumerated()), id: \.offset) { _, p in
                                    Rollenkapsel(text: p.text)
                                }
                            }
                        }
                    }
                    Spacer(minLength: 0)
                }
            }
            .padding(.vertical, 8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(gewaehlt ? .isSelected : [])
    }
}

/// Rollen-Kapsel („Lehrkraft“, „Klassenlehrkraft | DTI1A“), Vorlage 143: 20,6 pt hoch, Text 12,5 pt #07131C auf #7F8F9C,
/// 9,2 pt Innenabstand, 4,4 pt Abstand zur nächsten Kapsel.
struct Rollenkapsel: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 12.5))
            .foregroundStyle(Color(hell: 0x40525E, dunkel: 0x07131C))
            .lineLimit(1)
            .minimumScaleFactor(0.75)
            .padding(.horizontal, 9.2)
            .frame(height: 20.6)
            .background(Color(hell: 0xD3DAE0, dunkel: 0x7F8F9C), in: Capsule())
    }
}

/// Einfaches Umbruch-Layout: Kinder nebeneinander, bei Platzmangel in die nächste Zeile.
struct Umbruchlayout: Layout {
    var abstand: CGFloat = 8
    var zeilenabstand: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let breite = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var zeile: CGFloat = 0
        var maxX: CGFloat = 0
        for s in subviews {
            let g = s.sizeThatFits(ProposedViewSize(width: breite, height: nil))
            if x > 0 && x + g.width > breite {
                y += zeile + zeilenabstand
                x = 0
                zeile = 0
            }
            x += g.width + abstand
            zeile = max(zeile, g.height)
            maxX = max(maxX, min(x - abstand, breite))
        }
        return CGSize(width: maxX, height: y + zeile)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var zeile: CGFloat = 0
        for s in subviews {
            let g = s.sizeThatFits(ProposedViewSize(width: bounds.width, height: nil))
            if x > bounds.minX && x + g.width > bounds.maxX {
                y += zeile + zeilenabstand
                x = bounds.minX
                zeile = 0
            }
            s.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(width: min(g.width, bounds.width), height: g.height))
            x += g.width + abstand
            zeile = max(zeile, g.height)
        }
    }
}

private extension View {
    /// Listenzeile ohne Trennlinie und Hintergrund (Empfängerauswahl).
    func auswahlzeile() -> some View {
        self
            .listRowInsets(EdgeInsets(top: 0, leading: 18, bottom: 0, trailing: 16))
            .listRowSeparator(.hidden)
            .listRowBackground(Color.clear)
    }
}

/// Titel in der Mitte der Mitteilungsblätter (Vorlagen 143/144): 15,5 pt Regular #CDD1D4, Untertitel 11,5 pt – etwas
/// kleiner als der Titel der Stundendetails.
struct Mitteilungstitel: View {
    let titel: String
    var untertitel: String? = nil

    private static let farbe = Color(hell: 0x5D727F, dunkel: 0xCDD1D4)

    var body: some View {
        VStack(spacing: 1.5) {
            Text(titel)
                .font(.system(size: 15.5))
            if let untertitel, !untertitel.isEmpty {
                Text(untertitel)
                    .font(.system(size: 11.5))
            }
        }
        .foregroundStyle(Self.farbe)
        .lineLimit(1)
        .minimumScaleFactor(0.8)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Kamera

/// Foto mit der Kamera aufnehmen (UIImagePickerController); `fertig(nil)` bei Abbruch (der Aufrufer schließt die Ansicht).
struct KameraAufnahme: UIViewControllerRepresentable {
    let fertig: (UIImage?) -> Void

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ uiViewController: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Koordinator { Koordinator(self) }

    final class Koordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        let eltern: KameraAufnahme

        init(_ eltern: KameraAufnahme) {
            self.eltern = eltern
        }

        func imagePickerController(_ picker: UIImagePickerController, didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            eltern.fertig(info[.originalImage] as? UIImage)
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            eltern.fertig(nil)
        }
    }
}
