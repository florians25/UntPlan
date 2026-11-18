import SwiftUI

// MARK: - Daten und Aktionen

/// Daten eines Sprechtags (GET /sprechtag/{id}) und Aktionen für alle Ansichten des Blatts. Nach jeder Aktion wird neu
/// geladen, damit Termine, Einladungen und Abwesenheiten überall stimmen.
@MainActor
final class SprechtagModell: ObservableObject {
    let id: Int
    let api = ApiClient.shared

    @Published var ansicht: SprechtagAnsicht? = nil
    @Published var fehler: String? = nil
    /// Kurze Erfolgsmeldung oben im Blatt (z. B. „Termin gebucht.“)
    @Published var meldung: String? = nil
    @Published var laedt = false
    @Published var geladen = false
    /// Laufende Aktion (Kennung), z. B. „a12“ für eine Terminanfrage
    @Published var arbeitet: String? = nil
    weak var zustand: AppZustand?

    init(id: Int) {
        self.id = id
    }

    var sprechtag: SprechtagDaten? { ansicht?.sprechtag }

    func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            ansicht = try await api.sprechtag(id)
            fehler = nil
        } catch is CancellationError {
            // Ansicht geschlossen bzw. neu geladen
        } catch {
            if let text = fehlertext(error) { fehler = text }
        }
        geladen = true
    }

    /// Fehlertext wie in der übrigen App (nil = abgemeldet).
    func fehlertext(_ error: Error) -> String? {
        if let zustand { return zustand.fehlertext(error) }
        return error.localizedDescription
    }

    /// Aktion ausführen und danach neu laden. Die Aktion liefert die Erfolgsmeldung. Ergebnis: nil = erfolgreich, sonst der
    /// Fehlertext (leer, wenn die Sitzung abgelaufen ist). `fehlerAnzeigen`: Fehler zusätzlich oben im Blatt zeigen
    /// (Formulare zeigen ihn selbst).
    @discardableResult
    func ausfuehren(_ kennung: String, fehlerAnzeigen: Bool = true, _ aktion: (ApiClient) async throws -> String) async -> String? {
        arbeitet = kennung
        defer { arbeitet = nil }
        meldung = nil
        do {
            let text = try await aktion(api)
            meldung = text.isEmpty ? nil : text
            fehler = nil
            await laden()
            return nil
        } catch is CancellationError {
            return ""
        } catch {
            let text = fehlertext(error) ?? ""
            if fehlerAnzeigen && !text.isEmpty { fehler = text }
            return text
        }
    }
}

// MARK: - Sprechtag (Einstieg)

/// Sprechtag (Elternsprechtag) – von der Startseite aus als Blatt (iPhone randlos und 90 % hoch, iPad festes Detailblatt).
/// Lädt GET /sprechtag/{id} und verzweigt nach der Rolle:
/// - Erziehungsberechtigte: Hinweis zur Phase, Terminanfragen und Einladungen (buchen, annehmen, ablehnen), „Meine Termine“
///   nach Datum (Details mit Absagen und Zeit ändern) und ab der 2. Anmeldephase die freie Buchung je Kind und Lehrkraft.
/// - Lehrkräfte: „Meine Termine“ je Abschnitt mit Raum, „+“-Menü (Einladung senden, Termin buchen, Abwesenheit eintragen),
///   Terminen und Abwesenheiten (Antippen bearbeitet) und darunter die Einladungen ohne Uhrzeit.
/// - Administration: Liste der Lehrkräfte (Suche, Filter Alle | Kein Raum | Keine Termine), Antippen zeigt dieselbe Ansicht
///   für die gewählte Lehrkraft. Anlegen und Konfiguration bleiben der Weboberfläche vorbehalten.
/// - Schüler*innen haben keinen Zugriff.
struct SprechtagView: View {
    let id: Int
    let titel: String
    /// Karte von der Startseite – Rückfall, falls die Details nicht geladen werden können
    var karte: SprechtagKarte?
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @StateObject private var modell: SprechtagModell

    init(id: Int, titel: String, karte: SprechtagKarte? = nil) {
        self.id = id
        self.titel = titel
        self.karte = karte
        _modell = StateObject(wrappedValue: SprechtagModell(id: id))
    }

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt (bzw. zurück).
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    /// Schüler*innen sehen den Sprechtag nicht.
    private var nurSchueler: Bool {
        (zustand.benutzer?.rolle.lowercased() ?? "") == "schueler"
    }

    private var untertitel: String? {
        let text = modell.sprechtag?.datumText ?? karte?.datumText ?? ""
        return text.isEmpty ? nil : text
    }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() },
                      laedt: modell.laedt || (!modell.geladen && !nurSchueler)) {
                Blatttitel(titel: modell.sprechtag?.titel ?? titel, untertitel: untertitel)
            } rechts: {
                menue
            }
            inhalt
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .background(Farben.grund.ignoresSafeArea())
        .toolbar(.hidden, for: .navigationBar)
        .task {
            modell.zustand = zustand
            if !nurSchueler && !modell.geladen { await modell.laden() }
        }
    }

    private var menue: some View {
        Menu {
            Button {
                Task { await modell.laden() }
            } label: {
                Label("Aktualisieren", systemImage: "arrow.clockwise")
            }
        } label: {
            Rundknopf(symbol: "ellipsis", gewicht: .medium, groesse: 18, drehung: 90)
        }
        // Symbole im Menü schwarz bzw. weiß je nach Modus
        .tint(Color.primary)
        .accessibilityLabel("Menü")
    }

    @ViewBuilder
    private var inhalt: some View {
        if nurSchueler {
            ScrollView {
                SprechtagInfobanner(titel: "Der Sprechtag steht Erziehungsberechtigten und Lehrkräften zur Verfügung.",
                                    text: "Termine buchen Ihre Erziehungsberechtigten.")
                    .padding(16)
            }
        } else if let a = modell.ansicht, a.sprechtag != nil {
            if a.istEltern {
                SprechtagElternInhalt(modell: modell)
            } else if a.istAdmin {
                SprechtagAdminInhalt(modell: modell)
            } else {
                SprechtagLehrkraftInhalt(modell: modell, kurz: a.lehrkraefte.first?.kurz ?? "")
            }
        } else {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    SprechtagMeldungen(modell: modell)
                    if modell.fehler != nil, let karte {
                        kartenUebersicht(karte)
                    }
                }
                .padding(16)
            }
            .refreshable { await modell.laden() }
        }
    }

    /// Ankündigung aus der Startkarte (ohne Details).
    private func kartenUebersicht(_ k: SprechtagKarte) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if !k.phaseText.isEmpty {
                SprechtagPille.phase(k.phase, k.phaseText)
            }
            Text(k.titel)
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(Farben.tinte)
            if !k.datumText.isEmpty {
                SprechtagMeta(symbol: "calendar", text: k.datumText)
            }
            if !k.text.isEmpty {
                Text(k.text)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .sprechtagKarte()
    }
}

// MARK: - Erziehungsberechtigte

/// Auftrag für das Buchungsblatt: Einladung (mit `einladungId`), freie Buchung oder Zeit eines Termins ändern (`termin`).
struct SprechtagBuchAuftrag: Hashable {
    let lehrer: String
    let titel: String
    let kindId: Int
    let kindName: String
    var einladungId: Int? = nil
    var termin: SprechtagTermin? = nil

    var id: String { "\(lehrer)|\(kindId)|\(einladungId ?? 0)|\(termin?.id ?? 0)" }
}

/// Blätter der Eltern-Ansicht.
enum SprechtagElternBlatt: Identifiable {
    case buchen(SprechtagBuchAuftrag)
    case termin(SprechtagTermin)

    var id: String {
        switch self {
        case .buchen(let a): return "b" + a.id
        case .termin(let t): return "t\(t.id)"
        }
    }
}

/// Eltern-Ansicht: Infobanner, Terminanfragen, Meine Termine (nach Datum) und – ab der 2. Anmeldephase – Termin buchen.
struct SprechtagElternInhalt: View {
    @ObservedObject var modell: SprechtagModell
    @EnvironmentObject var zustand: AppZustand
    @State private var blatt: SprechtagElternBlatt? = nil

    /// Ein Tag in „Meine Termine“.
    private struct Tag: Identifiable {
        let id: String
        let text: String
        var termine: [SprechtagTermin]
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                SprechtagMeldungen(modell: modell)
                if let a = modell.ansicht, let t = a.sprechtag {
                    inhalt(a, t)
                }
            }
            .padding(16)
            .padding(.bottom, 20)
            .zentriert(maxBreite: 700)
        }
        .refreshable { await modell.laden() }
        .detailPraesentation(item: $blatt, breit: Geraet.istPad, art: .formular) { b in
            NavigationStack {
                switch b {
                case .buchen(let auftrag):
                    SprechtagBuchenBlatt(modell: modell, auftrag: auftrag)
                case .termin(let termin):
                    SprechtagElternTerminBlatt(modell: modell, termin: termin)
                }
            }
            .environmentObject(zustand)
        }
    }

    /// Buchung, Annehmen und Ablehnen sind bis zum Ende der Anmeldung möglich.
    private func offen(_ t: SprechtagDaten) -> Bool {
        !["geschlossen", "durchfuehrung", "abgeschlossen"].contains(t.phase)
    }

    @ViewBuilder
    private func inhalt(_ a: SprechtagAnsicht, _ t: SprechtagDaten) -> some View {
        if !a.hinweis.isEmpty {
            SprechtagInfobanner(titel: a.hinweis,
                                text: t.phase == "phase1"
                                    ? "Derzeit können Termine nur mit einer Einladung gebucht werden. Die freie Buchung ist ab der nächsten Anmeldephase möglich."
                                    : "")
        }
        if !a.einladungen.isEmpty || !a.anfragen.isEmpty {
            SprechtagGruppentitel(titel: "Terminanfragen")
            ForEach(a.einladungen) { x in
                einladungKarte(x, t: t)
            }
            ForEach(a.anfragen) { x in
                anfrageKarte(x, t: t)
            }
        }
        SprechtagGruppentitel(titel: "Meine Termine", text: "Ihre bestätigten Gesprächstermine.")
        let tage = gruppiert(a.termine)
        if tage.isEmpty {
            SprechtagLeer(text: "Noch keine bestätigten Termine.")
        }
        ForEach(tage) { tag in
            Text(tag.text)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(Farben.tinte2)
                .padding(.top, 4)
            ForEach(tag.termine) { x in
                terminKarte(x, t: t)
            }
        }
        if a.freiBuchbar {
            SprechtagGruppentitel(titel: "Termin buchen", text: "Wählen Sie eine Lehrkraft Ihres Kindes.")
            if a.kinder.isEmpty {
                SprechtagLeer(text: "Für Ihr Konto sind keine Kinder hinterlegt.")
            }
            ForEach(a.kinder) { kind in
                Text(kind.klasse.isEmpty ? kind.anzeige : "\(kind.anzeige) · \(kind.klasse)")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(Farben.tinte2)
                    .padding(.top, 4)
                if kind.lehrkraefte.isEmpty {
                    SprechtagLeer(text: "Keine Lehrkräfte gefunden.")
                }
                ForEach(kind.lehrkraefte) { l in
                    Button {
                        blatt = .buchen(SprechtagBuchAuftrag(lehrer: l.kurz, titel: l.titel, kindId: kind.id, kindName: kind.anzeige))
                    } label: {
                        HStack(spacing: 10) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(l.titel)
                                    .font(.system(size: 16, weight: .semibold))
                                    .foregroundStyle(Farben.tinte)
                                if !l.unterzeile.isEmpty {
                                    Text(l.unterzeile)
                                        .font(.system(size: 14))
                                        .foregroundStyle(Farben.gedaempft)
                                        .lineLimit(2)
                                }
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            SprechtagPfeil()
                        }
                        .sprechtagKarte()
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .disabled(!offen(t))
                }
            }
        }
    }

    /// Termine nach Datum, Reihenfolge wie vom Server (Datum, Beginn).
    private func gruppiert(_ liste: [SprechtagTermin]) -> [Tag] {
        var tage: [Tag] = []
        for x in liste {
            let schluessel = x.datum.isEmpty ? x.datumText : x.datum
            if let i = tage.firstIndex(where: { $0.id == schluessel }) {
                tage[i].termine.append(x)
            } else {
                tage.append(Tag(id: schluessel, text: SprechtagText.tag(x), termine: [x]))
            }
        }
        return tage
    }

    /// Kopf einer Karte: „(KÜRZEL) Name“, darunter das Kind grau.
    private func kartenKopf(_ x: SprechtagTermin, pille: Bool) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(x.lehrerTitel)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(Farben.tinte)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if pille {
                    SprechtagPille.status(x)
                }
            }
            if !x.schuelerName.isEmpty {
                Text(x.schuelerName)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
    }

    private func zusatz(_ x: SprechtagTermin) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            if x.verpflichtend {
                Text("Verpflichtend")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(SprechtagFarben.braun)
            }
            if !x.anmerkung.isEmpty {
                Text(x.anmerkung)
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// Einladung (ohne Uhrzeit): „Termin buchen“ öffnet das Buchungsblatt mit der Einladung.
    private func einladungKarte(_ x: SprechtagTermin, t: SprechtagDaten) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            kartenKopf(x, pille: false)
            zusatz(x)
            if offen(t) {
                SprechtagKnopf(titel: "Termin buchen") {
                    blatt = .buchen(SprechtagBuchAuftrag(lehrer: x.lehrer, titel: x.lehrerTitel, kindId: x.schueler?.id ?? 0,
                                                         kindName: x.schuelerName, einladungId: x.id))
                }
            }
        }
        .sprechtagKarte()
    }

    /// Terminanfrage mit fester Uhrzeit: Ablehnen bzw. Annehmen.
    private func anfrageKarte(_ x: SprechtagTermin, t: SprechtagDaten) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            kartenKopf(x, pille: false)
            VStack(alignment: .leading, spacing: 3) {
                let tag = SprechtagText.tag(x)
                if !tag.isEmpty {
                    SprechtagMeta(symbol: "calendar", text: tag)
                }
                SprechtagMeta(symbol: "clock", text: x.zeitText)
                SprechtagOrt(termin: x, online: t.online)
            }
            zusatz(x)
            if offen(t) {
                HStack(spacing: 10) {
                    SprechtagKnopf(titel: "Ablehnen", stil: .zweit, laedt: modell.arbeitet == "n\(x.id)") {
                        Task { await beantworten(x, annehmen: false) }
                    }
                    SprechtagKnopf(titel: "Annehmen", laedt: modell.arbeitet == "j\(x.id)") {
                        Task { await beantworten(x, annehmen: true) }
                    }
                }
                .disabled(modell.arbeitet != nil)
            }
        }
        .sprechtagKarte()
    }

    /// Bestätigter Termin: Antippen öffnet die Details (Absagen, Zeit ändern).
    private func terminKarte(_ x: SprechtagTermin, t: SprechtagDaten) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 6) {
                kartenKopf(x, pille: true)
                VStack(alignment: .leading, spacing: 3) {
                    Text(x.zeitText)
                        .font(.system(size: 15, weight: .medium).monospacedDigit())
                        .foregroundStyle(Farben.tinte)
                    SprechtagOrt(termin: x, online: t.online)
                }
                zusatz(x)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            SprechtagPfeil()
        }
        .sprechtagKarte()
        .contentShape(Rectangle())
        // Kein Knopf um die Karte, damit der Link zum Online-Meeting antippbar bleibt
        .onTapGesture { blatt = .termin(x) }
        .accessibilityAddTraits(.isButton)
    }

    private func beantworten(_ x: SprechtagTermin, annehmen: Bool) async {
        let sid = modell.id
        await modell.ausfuehren((annehmen ? "j" : "n") + "\(x.id)") { api in
            try await api.sprechtagAnfrageBeantworten(sid, termin: x.id, annehmen: annehmen)
            return annehmen ? "Termin angenommen." : "Termin abgelehnt."
        }
    }
}

/// Details eines bestätigten Termins (Erziehungsberechtigte): Termin absagen (mit Rückfrage), Zeit ändern.
struct SprechtagElternTerminBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let termin: SprechtagTermin
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var absagenFragen = false
    @State private var grund = ""
    @State private var fehler: String? = nil

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Gesprächstermin", untertitel: termin.lehrerTitel, symbol: "xmark", zurueck: { schliessen() }) {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(termin.lehrerTitel)
                            .font(.system(size: 17, weight: .semibold))
                            .foregroundStyle(Farben.tinte)
                        if !termin.schuelerName.isEmpty {
                            Text(termin.schuelerName)
                                .font(.system(size: 14))
                                .foregroundStyle(Farben.gedaempft)
                        }
                    }
                    VStack(alignment: .leading, spacing: 6) {
                        let tag = SprechtagText.tag(termin)
                        if !tag.isEmpty {
                            SprechtagMeta(symbol: "calendar", text: tag)
                        }
                        SprechtagMeta(symbol: "clock", text: termin.zeitText)
                        if !termin.abschnittName.isEmpty {
                            SprechtagMeta(symbol: "square.grid.2x2", text: termin.abschnittName)
                        }
                        SprechtagOrt(termin: termin, online: modell.sprechtag?.online ?? termin.online)
                    }
                    SprechtagPille.status(termin)
                    if !termin.anmerkung.isEmpty {
                        Text(termin.anmerkung)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let fehler, !fehler.isEmpty {
                        Fehlerhinweis(text: fehler)
                    }
                    VStack(spacing: 10) {
                        if modell.ansicht?.aendern == true {
                            NavigationLink {
                                SprechtagBuchenBlatt(modell: modell,
                                                     auftrag: SprechtagBuchAuftrag(lehrer: termin.lehrer, titel: termin.lehrerTitel,
                                                                                   kindId: termin.schueler?.id ?? 0,
                                                                                   kindName: termin.schuelerName, termin: termin),
                                                     wurzel: false, fertig: { schliessen() })
                            } label: {
                                SprechtagKnopfBeschriftung(titel: "Zeit ändern", stil: .primaer, breit: true)
                            }
                            .buttonStyle(.plain)
                        }
                        SprechtagKnopf(titel: "Termin absagen", stil: .gefahr, breit: true, laedt: modell.arbeitet == "x\(termin.id)") {
                            grund = ""
                            absagenFragen = true
                        }
                        .disabled(modell.arbeitet != nil)
                    }
                    .padding(.top, 6)
                }
                .padding(16)
            }
        }
        .alert("Termin absagen?", isPresented: $absagenFragen) {
            TextField("Grund (optional)", text: $grund)
            Button("Absagen", role: .destructive) {
                Task { await absagen() }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Der Termin wird abgesagt; die Lehrkraft wird benachrichtigt.")
        }
    }

    private func absagen() async {
        let sid = modell.id
        let tid = termin.id
        let g = grund
        let ergebnis = await modell.ausfuehren("x\(tid)", fehlerAnzeigen: false) { api in
            try await api.sprechtagTerminAbsagen(sid, termin: tid, grund: g)
            return "Termin abgesagt."
        }
        if let ergebnis {
            fehler = ergebnis
        } else {
            schliessen()
        }
    }
}

/// Freie Zeiten wählen und buchen (Einladung bzw. freie Buchung) oder die Zeit eines Termins ändern: Abschnitt (bei
/// mehreren), freie Zeiten als Chips, darunter „Termin buchen“ bzw. „Termin ändern“.
struct SprechtagBuchenBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let auftrag: SprechtagBuchAuftrag
    /// Wurzel des Blatts (X schließt), sonst Unterseite (Zurück)
    var wurzel: Bool = true
    /// Nach dem Buchen (Unterseite: schließt das ganze Blatt)
    var fertig: (() -> Void)? = nil
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var abschnitt: Int
    @State private var frei: [SprechtagZeit] = []
    @State private var wahl: String? = nil
    @State private var laedt = false
    @State private var speichert = false
    @State private var fehler: String? = nil

    init(modell: SprechtagModell, auftrag: SprechtagBuchAuftrag, wurzel: Bool = true, fertig: (() -> Void)? = nil) {
        _modell = ObservedObject(wrappedValue: modell)
        self.auftrag = auftrag
        self.wurzel = wurzel
        self.fertig = fertig
        let start = auftrag.termin.map { $0.abschnitt >= 0 ? $0.abschnitt : 0 } ?? (modell.sprechtag?.abschnitte.first?.index ?? 0)
        _abschnitt = State(initialValue: start)
    }

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    /// Wurzel: X schließt das Blatt; Unterseite: Zurück (Standard des Rahmens).
    private var zurueckAktion: (() -> Void)? {
        guard wurzel else { return nil }
        return { schliessen() }
    }

    private var aendern: Bool { auftrag.termin != nil }
    private var abschnitte: [SprechtagAbschnitt] { modell.sprechtag?.abschnitte ?? [] }

    var body: some View {
        DetailSeitenRahmen(titel: aendern ? "Termin ändern" : "Termin buchen", untertitel: auftrag.titel,
                           symbol: wurzel ? "xmark" : "chevron.left", zurueck: zurueckAktion, laedt: laedt) {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(auftrag.titel)
                            .font(.system(size: 17, weight: .semibold))
                            .foregroundStyle(Farben.tinte)
                        if !auftrag.kindName.isEmpty {
                            Text(auftrag.kindName)
                                .font(.system(size: 14))
                                .foregroundStyle(Farben.gedaempft)
                        }
                    }
                    if abschnitte.count > 1 {
                        SprechtagFeld(titel: "Abschnitt") {
                            Picker("Abschnitt", selection: $abschnitt) {
                                ForEach(abschnitte) { a in
                                    Text(a.langText).tag(a.index)
                                }
                            }
                            .pickerStyle(.menu)
                            .labelsHidden()
                            .tint(Farben.tinte)
                        }
                    } else if let a = abschnitte.first {
                        HStack(spacing: 14) {
                            SprechtagMeta(symbol: "calendar", text: [a.wochentag, a.datumText].filter { !$0.isEmpty }.joined(separator: ", "))
                            SprechtagMeta(symbol: "clock", text: "\(a.von) – \(a.bis) Uhr")
                        }
                    }
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Freie Zeiten")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Farben.tinte2)
                        if frei.isEmpty {
                            Text(laedt ? "Freie Zeiten werden geladen …" : "Keine freien Zeiten in diesem Abschnitt.")
                                .font(.system(size: 14))
                                .foregroundStyle(Farben.gedaempft)
                        } else {
                            Fliesslayout(abstand: 8) {
                                ForEach(frei) { z in
                                    Button {
                                        wahl = z.von
                                    } label: {
                                        SprechtagZeitchip(text: z.von, aktiv: wahl == z.von)
                                    }
                                    .buttonStyle(.plain)
                                    .accessibilityLabel("\(z.von) Uhr")
                                    .accessibilityAddTraits(wahl == z.von ? .isSelected : [])
                                }
                            }
                        }
                    }
                    if let fehler, !fehler.isEmpty {
                        Fehlerhinweis(text: fehler)
                    }
                    SprechtagKnopf(titel: aendern ? "Termin ändern" : "Termin buchen", breit: true, laedt: speichert) {
                        Task { await buchen() }
                    }
                    .disabled(wahl == nil || speichert)
                    .padding(.top, 4)
                }
                .padding(16)
            }
        }
        .task { await freiLaden() }
        .onChange(of: abschnitt) { _, _ in
            wahl = nil
            Task { await freiLaden() }
        }
    }

    private func freiLaden() async {
        laedt = true
        defer { laedt = false }
        do {
            frei = try await modell.api.sprechtagFrei(modell.id, lehrer: auftrag.lehrer, abschnitt: abschnitt,
                                                      schuelerId: auftrag.kindId, ausser: auftrag.termin?.id)
            fehler = nil
        } catch is CancellationError {
        } catch {
            frei = []
            fehler = zustand.fehlertext(error)
        }
    }

    private func buchen() async {
        guard let w = wahl else { return }
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let a = abschnitt
        let l = auftrag.lehrer
        let k = auftrag.kindId
        let e = auftrag.einladungId
        let tid = auftrag.termin?.id
        let ergebnis = await modell.ausfuehren("buchen", fehlerAnzeigen: false) { api in
            if let tid {
                try await api.sprechtagTerminAendern(sid, termin: tid, SprechtagAenderDaten(abschnitt: a, beginn: w))
                return "Termin auf \(w) Uhr geändert."
            }
            try await api.sprechtagBuchen(sid, SprechtagBuchDaten(lehrer: l, abschnitt: a, beginn: w, schuelerId: k, einladungId: e))
            return "Termin um \(w) Uhr gebucht."
        }
        if let ergebnis {
            fehler = ergebnis
            // Zeit inzwischen vergeben: freie Zeiten neu laden
            await freiLaden()
            if !ergebnis.isEmpty { fehler = ergebnis }
        } else if let fertig {
            fertig()
        } else {
            schliessen()
        }
    }
}

// MARK: - Administration

/// Verwaltung (Administration): Lehrkräfte mit Suche und Filter; Antippen zeigt die Abschnitte der Lehrkraft.
struct SprechtagAdminInhalt: View {
    @ObservedObject var modell: SprechtagModell
    @EnvironmentObject var zustand: AppZustand
    @State private var suche = ""
    @State private var filter: Filter = .alle

    enum Filter: Hashable {
        case alle
        case keinRaum
        case keineTermine
    }

    var body: some View {
        VStack(spacing: 0) {
            if let a = modell.ansicht, let t = a.sprechtag {
                VStack(spacing: 0) {
                    Suchleiste(text: $suche, platzhalter: "Lehrkräfte suchen")
                    Segmentschalter(auswahl: $filter, filterEintraege(a, t))
                        .padding(.horizontal, 16)
                        .padding(.bottom, 8)
                }
                .background(Farben.blatt)
                ScrollView {
                    VStack(alignment: .leading, spacing: 12) {
                        SprechtagMeldungen(modell: modell)
                        kopf(t)
                        let liste = gefiltert(a)
                        if liste.isEmpty {
                            SprechtagLeer(text: "Keine Lehrkräfte gefunden.")
                        } else {
                            Zeilengruppe {
                                ForEach(Array(liste.enumerated()), id: \.element.id) { eintrag in
                                    NavigationLink {
                                        SprechtagLehrkraftSeite(modell: modell, kurz: eintrag.element.kurz)
                                            .environmentObject(zustand)
                                    } label: {
                                        zeile(eintrag.element, online: t.online)
                                    }
                                    .buttonStyle(ZeilenKnopfStil())
                                    if eintrag.offset < liste.count - 1 {
                                        Rectangle()
                                            .fill(Farben.trennlinie)
                                            .frame(height: 0.5)
                                            .padding(.horizontal, 16)
                                    }
                                }
                            }
                        }
                    }
                    .padding(16)
                    .padding(.bottom, 20)
                    .zentriert(maxBreite: 700)
                }
                .scrollDismissesKeyboard(.interactively)
                .refreshable { await modell.laden() }
            }
        }
    }

    private func filterEintraege(_ a: SprechtagAnsicht, _ t: SprechtagDaten) -> [(Filter, String)] {
        var liste: [(Filter, String)] = [(.alle, "Alle (\(a.lehrkraefte.count))")]
        if !t.online {
            liste.append((.keinRaum, "Kein Raum (\(a.lehrkraefte.filter(\.keinRaum).count))"))
        }
        liste.append((.keineTermine, "Keine Termine (\(a.lehrkraefte.filter(\.keineTermine).count))"))
        return liste
    }

    private func gefiltert(_ a: SprechtagAnsicht) -> [SprechtagLehrkraft] {
        let q = suche.trimmingCharacters(in: .whitespaces).lowercased()
        return a.lehrkraefte.filter { l in
            let passt = q.isEmpty || "\(l.kurz) \(l.name)".lowercased().contains(q)
            switch filter {
            case .alle: return passt
            case .keinRaum: return passt && l.keinRaum
            case .keineTermine: return passt && l.keineTermine
            }
        }
    }

    private func kopf(_ t: SprechtagDaten) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("Verwaltung")
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(Farben.tinte)
                Spacer(minLength: 4)
                if !t.phaseText.isEmpty {
                    SprechtagPille.phase(t.phase, t.phaseText)
                }
            }
            Text("Hier verwalten Sie Abwesenheiten, Räume und Termine der Lehrkräfte."
                 + (t.online ? " Dies ist ein Online-Sprechtag: Meeting-Links werden direkt im jeweiligen Termin hinterlegt."
                    : " Für Präsenz-Termine muss der jeweiligen Lehrkraft vorab ein Raum zugewiesen sein."))
                .font(.system(size: 14))
                .foregroundStyle(Farben.tinte2)
                .fixedSize(horizontal: false, vertical: true)
            if t.phase == "entwurf" {
                SprechtagInfobanner(titel: "Der Sprechtag ist noch nicht veröffentlicht.",
                                    text: "Konfiguration und Veröffentlichung erfolgen in der Weboberfläche.")
                    .padding(.top, 4)
            }
        }
    }

    private func zeile(_ l: SprechtagLehrkraft, online: Bool) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text(l.titel)
                    .font(.system(size: 16))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if (l.keinRaum && !online) || l.keineTermine {
                    HStack(spacing: 6) {
                        if l.keinRaum && !online {
                            SprechtagPille.grau("Kein Raum")
                        }
                        if l.keineTermine {
                            SprechtagPille.grau("Keine Termine")
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            let anzahl = l.termine.filter(\.hatZeit).count
            if anzahl > 0 {
                ZahlPlakette(zahl: anzahl)
                    .scaleEffect(0.85)
            }
            SprechtagPfeil()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .frame(minHeight: 50)
        .contentShape(Rectangle())
    }
}

/// Abschnitte einer gewählten Lehrkraft (Administration) als Unterseite im selben Blatt.
struct SprechtagLehrkraftSeite: View {
    @ObservedObject var modell: SprechtagModell
    let kurz: String

    var body: some View {
        DetailSeitenRahmen(titel: modell.ansicht?.lehrkraft(kurz)?.titel ?? kurz, untertitel: modell.sprechtag?.titel, laedt: modell.laedt) {
            SprechtagLehrkraftInhalt(modell: modell, kurz: kurz, admin: true)
                .background(Farben.grund)
        }
    }
}

// MARK: - Lehrkraft

/// Blätter der Lehrkraft-Ansicht (je mit dem Kürzel der Lehrkraft).
enum SprechtagLehrkraftBlatt: Identifiable {
    case termin(String, SprechtagTermin)
    case abwesenheit(String, Int, SprechtagAbwesenheit?)
    case einladung(String)
    case terminNeu(String, Int)

    var id: String {
        switch self {
        case .termin(let l, let t): return "t\(l)|\(t.id)"
        case .abwesenheit(let l, let i, let a): return "a\(l)|\(i)|\(a?.id ?? 0)"
        case .einladung(let l): return "e\(l)"
        case .terminNeu(let l, let i): return "n\(l)|\(i)"
        }
    }
}

/// „Meine Termine“ einer Lehrkraft: je Abschnitt Kopf mit Datum und Zeit, Raum, „+“-Menü und die Termine bzw.
/// Abwesenheiten nach Uhrzeit; darunter die Einladungen ohne Uhrzeit (zurückziehen).
struct SprechtagLehrkraftInhalt: View {
    @ObservedObject var modell: SprechtagModell
    let kurz: String
    /// Administration (Ansicht einer gewählten Lehrkraft)
    var admin: Bool = false
    @EnvironmentObject var zustand: AppZustand
    @State private var blatt: SprechtagLehrkraftBlatt? = nil
    @State private var zurueckziehen: SprechtagTermin? = nil

    /// Termin oder Abwesenheit in einem Abschnitt.
    private struct Eintrag: Identifiable {
        let id: String
        let von: String
        var termin: SprechtagTermin? = nil
        var abwesenheit: SprechtagAbwesenheit? = nil
    }

    /// Räume eines Gebäudes für die Auswahl.
    private struct Gebaeude: Identifiable {
        let id: String
        var raeume: [SprechtagRaum]
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                SprechtagMeldungen(modell: modell)
                if let a = modell.ansicht, let t = a.sprechtag {
                    kopf(t)
                    if let l = a.lehrkraft(kurz) {
                        if t.abschnitte.isEmpty {
                            SprechtagLeer(text: "Für diesen Sprechtag sind keine Abschnitte festgelegt.")
                        }
                        ForEach(t.abschnitte) { ab in
                            abschnittKarte(a, t, l, ab)
                        }
                        if !l.einladungen.isEmpty {
                            einladungen(l, t)
                        }
                    } else {
                        SprechtagLeer(text: "Ihrem Konto ist keine Lehrkraft zugeordnet.")
                    }
                }
            }
            .padding(16)
            .padding(.bottom, 20)
            .zentriert(maxBreite: 700)
        }
        .refreshable { await modell.laden() }
        .detailPraesentation(item: $blatt, breit: Geraet.istPad, art: .formular) { b in
            NavigationStack {
                switch b {
                case .termin(let l, let termin):
                    SprechtagTerminBearbeitenBlatt(modell: modell, lehrer: l, termin: termin, admin: admin)
                case .abwesenheit(let l, let i, let alt):
                    SprechtagAbwesenheitBlatt(modell: modell, lehrer: l, abschnitt: i, alt: alt, admin: admin)
                case .einladung(let l):
                    SprechtagEinladungBlatt(modell: modell, lehrer: l, admin: admin)
                case .terminNeu(let l, let i):
                    SprechtagTerminAnlegenBlatt(modell: modell, lehrer: l, abschnitt: i, admin: admin)
                }
            }
            .environmentObject(zustand)
        }
        .confirmationDialog("Einladung zurückziehen?",
                            isPresented: Binding(get: { zurueckziehen != nil }, set: { if !$0 { zurueckziehen = nil } }),
                            titleVisibility: .visible, presenting: zurueckziehen) { x in
            Button("Zurückziehen", role: .destructive) {
                Task { await einladungZurueckziehen(x) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { _ in
            Text("Die Einladung wird zurückgezogen; die Erziehungsberechtigten werden benachrichtigt, falls sie sie bereits erhalten haben.")
        }
    }

    private func kopf(_ t: SprechtagDaten) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(admin ? "Termine und Abwesenheiten" : "Meine Termine")
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(Farben.tinte)
                Spacer(minLength: 4)
                if !t.phaseText.isEmpty {
                    SprechtagPille.phase(t.phase, t.phaseText)
                }
            }
            Text((admin ? "Termine und Abwesenheiten der Lehrkraft für den Sprechtag." : "Hier sehen Sie Ihre Termine und Abwesenheiten für den Sprechtag.")
                 + (t.online ? " Dies ist ein Online-Sprechtag. Je nach Berechtigung müssen den Terminen Meeting-Links hinzugefügt werden." : ""))
                .font(.system(size: 14))
                .foregroundStyle(Farben.tinte2)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Abschnitt

    private func abschnittKarte(_ a: SprechtagAnsicht, _ t: SprechtagDaten, _ l: SprechtagLehrkraft, _ ab: SprechtagAbschnitt) -> some View {
        let liste = eintraege(l, ab.index)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 8) {
                VStack(alignment: .leading, spacing: 5) {
                    Text(ab.name.isEmpty ? "Abschnitt \(ab.index + 1)" : ab.name)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(Farben.tinte)
                    HStack(spacing: 14) {
                        SprechtagMeta(symbol: "calendar", text: ab.datumText)
                        SprechtagMeta(symbol: "clock", text: "\(ab.von) – \(ab.bis) Uhr")
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                plusMenue(a, t, l, ab)
            }
            raum(a, t, l, ab)
            if !liste.isEmpty {
                Rectangle()
                    .fill(Farben.trennlinie)
                    .frame(height: 0.5)
            }
            ForEach(liste) { e in
                eintragZeile(e, a: a, t: t, l: l)
            }
        }
        .sprechtagKarte()
    }

    private func eintraege(_ l: SprechtagLehrkraft, _ abschnitt: Int) -> [Eintrag] {
        let termine = l.termine.filter { $0.abschnitt == abschnitt && $0.hatZeit }
            .map { Eintrag(id: "t\($0.id)", von: $0.beginn, termin: $0) }
        let abwesend = l.abwesenheiten.filter { $0.abschnitt == abschnitt }
            .map { Eintrag(id: "a\($0.id)", von: $0.beginn, abwesenheit: $0) }
        return (termine + abwesend).sorted { $0.von < $1.von }
    }

    @ViewBuilder
    private func eintragZeile(_ e: Eintrag, a: SprechtagAnsicht, t: SprechtagDaten, l: SprechtagLehrkraft) -> some View {
        if let x = e.termin {
            Button {
                blatt = .termin(l.kurz, x)
            } label: {
                zeileInhalt(pille: x.statusAnzeige, zeit: x.zeitText,
                            name: x.schuelerName.isEmpty ? x.anmerkung : x.schuelerName,
                            zusatz: [x.schueler?.klasse ?? "", x.verpflichtend ? "verpflichtend" : ""].filter { !$0.isEmpty }.joined(separator: " · "),
                            pfeil: true)
            }
            .buttonStyle(.plain)
        } else if let x = e.abwesenheit {
            let darf = a.rechte.abwesenheiten && t.laufend
            Button {
                blatt = .abwesenheit(l.kurz, x.abschnitt, x)
            } label: {
                zeileInhalt(pille: "Abwesend", zeit: "\(x.beginn) – \(x.ende) Uhr", name: x.anmerkung, zusatz: "", pfeil: darf)
            }
            .buttonStyle(.plain)
            .disabled(!darf)
        }
    }

    private func zeileInhalt(pille: String, zeit: String, name: String, zusatz: String, pfeil: Bool) -> some View {
        HStack(alignment: .center, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    SprechtagPille.grau(pille)
                    Text(zeit)
                        .font(.system(size: 14, weight: .medium).monospacedDigit())
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                }
                if !name.isEmpty {
                    Text(name)
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
                if !zusatz.isEmpty {
                    Text(zusatz)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(1)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if pfeil {
                SprechtagPfeil()
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
    }

    // MARK: Raum

    @ViewBuilder
    private func raum(_ a: SprechtagAnsicht, _ t: SprechtagDaten, _ l: SprechtagLehrkraft, _ ab: SprechtagAbschnitt) -> some View {
        let aktuell = l.raum(ab.index)
        if t.online {
            SprechtagMeta(symbol: "video", text: "Online-Sprechtag")
        } else if a.rechte.raeume && t.laufend {
            Menu {
                Button {
                    Task { await raumSetzen(l, ab.index, "") }
                } label: {
                    if aktuell.isEmpty {
                        Label("Kein Raum", systemImage: "checkmark")
                    } else {
                        Text("Kein Raum")
                    }
                }
                ForEach(gebaeude(a.raumListe)) { g in
                    Section(g.id) {
                        ForEach(g.raeume) { r in
                            Button {
                                Task { await raumSetzen(l, ab.index, r.kurz) }
                            } label: {
                                let text = r.name.isEmpty || r.name == r.kurz ? r.kurz : "\(r.kurz) – \(r.name)"
                                if r.kurz == aktuell {
                                    Label(text, systemImage: "checkmark")
                                } else {
                                    Text(text)
                                }
                            }
                        }
                    }
                }
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "door.left.hand.open")
                        .font(.system(size: 13))
                    Text(aktuell.isEmpty ? "Gebäude | Raum" : aktuell)
                        .font(.system(size: 14, weight: .medium))
                        .lineLimit(1)
                    if modell.arbeitet == "r\(l.kurz)|\(ab.index)" {
                        ProgressView()
                            .controlSize(.mini)
                    } else {
                        Image(systemName: "chevron.up.chevron.down")
                            .font(.system(size: 11, weight: .medium))
                    }
                }
                .foregroundStyle(aktuell.isEmpty ? Farben.gedaempft : Farben.tinte)
                .padding(.horizontal, 12)
                .frame(height: 34)
                .background(Farben.eingabeFeld, in: Capsule())
            }
            .tint(Color.primary)
            .disabled(modell.arbeitet != nil)
            .accessibilityLabel("Raum für \(ab.name): \(aktuell.isEmpty ? "kein Raum" : aktuell)")
        } else {
            SprechtagMeta(symbol: "door.left.hand.open", text: aktuell.isEmpty ? "Kein Raum" : "Raum \(aktuell)")
        }
    }

    private func gebaeude(_ raeume: [SprechtagRaum]) -> [Gebaeude] {
        var liste: [Gebaeude] = []
        for r in raeume {
            let name = r.gebaeude.isEmpty ? "Ohne Gebäude" : r.gebaeude
            if let i = liste.firstIndex(where: { $0.id == name }) {
                liste[i].raeume.append(r)
            } else {
                liste.append(Gebaeude(id: name, raeume: [r]))
            }
        }
        return liste
    }

    private func raumSetzen(_ l: SprechtagLehrkraft, _ abschnitt: Int, _ raum: String) async {
        guard raum != l.raum(abschnitt) else { return }
        let sid = modell.id
        let lehrer = l.kurz
        await modell.ausfuehren("r\(lehrer)|\(abschnitt)") { api in
            try await api.sprechtagRaum(sid, SprechtagRaumDaten(lehrer: lehrer, abschnitt: abschnitt, raum: raum))
            return raum.isEmpty ? "Raum entfernt." : "Raum \(raum) gespeichert."
        }
    }

    // MARK: „+“-Menü

    @ViewBuilder
    private func plusMenue(_ a: SprechtagAnsicht, _ t: SprechtagDaten, _ l: SprechtagLehrkraft, _ ab: SprechtagAbschnitt) -> some View {
        let einladen = a.rechte.einladen && !["phase1_beendet", "geschlossen", "durchfuehrung"].contains(t.phase)
        let buchen = a.rechte.buchen && t.phase != "durchfuehrung"
        let abwesenheit = a.rechte.abwesenheiten
        if t.laufend && (einladen || buchen || abwesenheit) {
            Menu {
                if einladen {
                    Button {
                        blatt = .einladung(l.kurz)
                    } label: {
                        Label("Einladung senden", systemImage: "envelope")
                    }
                }
                if buchen {
                    Button {
                        blatt = .terminNeu(l.kurz, ab.index)
                    } label: {
                        Label("Termin buchen", systemImage: "calendar.badge.plus")
                    }
                }
                if abwesenheit {
                    Button {
                        blatt = .abwesenheit(l.kurz, ab.index, nil)
                    } label: {
                        Label("Abwesenheit eintragen", systemImage: "clock.badge.xmark")
                    }
                }
            } label: {
                Image(systemName: "plus")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(Color.white)
                    .frame(width: 34, height: 34)
                    .background(Farben.akzent, in: Circle())
                    .contentShape(Circle())
            }
            .tint(Color.primary)
            .accessibilityLabel("Hinzufügen")
        }
    }

    // MARK: Einladungen

    private func einladungen(_ l: SprechtagLehrkraft, _ t: SprechtagDaten) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Einladungen")
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(Farben.tinte)
            Text("Die Uhrzeit wählen die Erziehungsberechtigten.")
                .font(.system(size: 13))
                .foregroundStyle(Farben.gedaempft)
            ForEach(l.einladungen) { x in
                HStack(spacing: 10) {
                    SprechtagPille.grau(x.statusAnzeige)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(x.schuelerName)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(1)
                        let zusatz = [x.schueler?.klasse ?? "", x.verpflichtend ? "verpflichtend" : ""].filter { !$0.isEmpty }
                        if !zusatz.isEmpty {
                            Text(zusatz.joined(separator: " · "))
                                .font(.system(size: 13))
                                .foregroundStyle(Farben.gedaempft)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    if t.laufend {
                        if modell.arbeitet == "z\(x.id)" {
                            ProgressView()
                        } else {
                            Button {
                                zurueckziehen = x
                            } label: {
                                Image(systemName: "xmark")
                                    .font(.system(size: 14, weight: .medium))
                                    .foregroundStyle(Farben.gedaempft)
                                    .frame(width: 34, height: 34)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .disabled(modell.arbeitet != nil)
                            .accessibilityLabel("Einladung zurückziehen")
                        }
                    }
                }
            }
        }
        .sprechtagKarte()
    }

    private func einladungZurueckziehen(_ x: SprechtagTermin) async {
        let sid = modell.id
        let tid = x.id
        await modell.ausfuehren("z\(tid)") { api in
            try await api.sprechtagTerminAbsagen(sid, termin: tid, grund: nil)
            return "Einladung zurückgezogen."
        }
    }
}

// MARK: - Formulare der Lehrkraft

/// Kopf eines Formulars: Abschnitt mit Datum, Zeit und Raum (bzw. Online).
private struct SprechtagAbschnittKopf: View {
    let abschnitt: SprechtagAbschnitt?
    let raum: String
    let online: Bool

    var body: some View {
        if let a = abschnitt {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 14) {
                    SprechtagMeta(symbol: "calendar", text: [a.wochentag, a.datumText].filter { !$0.isEmpty }.joined(separator: ", "))
                    SprechtagMeta(symbol: "clock", text: "\(a.von) – \(a.bis) Uhr")
                }
                HStack {
                    Text(a.name)
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                    Spacer(minLength: 8)
                    Text(online ? "Online" : (raum.isEmpty ? "Kein Raum" : raum))
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
                .padding(12)
                .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
        }
    }
}

/// Termin bearbeiten (Lehrkraft bzw. Administration): Startzeit aus den freien Zeiten, Link (online), Anmerkung;
/// „Termin absagen“ nur mit dem Recht „Termine bearbeiten“.
struct SprechtagTerminBearbeitenBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let lehrer: String
    let termin: SprechtagTermin
    var admin: Bool = false
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var beginn: String
    @State private var link: String
    @State private var anmerkung: String
    @State private var frei: [String] = []
    @State private var laedt = false
    @State private var speichert = false
    @State private var fehler: String? = nil
    @State private var absagenFragen = false
    @State private var grund = ""

    init(modell: SprechtagModell, lehrer: String, termin: SprechtagTermin, admin: Bool = false) {
        _modell = ObservedObject(wrappedValue: modell)
        self.lehrer = lehrer
        self.termin = termin
        self.admin = admin
        _beginn = State(initialValue: termin.beginn)
        _link = State(initialValue: termin.link)
        _anmerkung = State(initialValue: termin.anmerkung)
    }

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var online: Bool { modell.sprechtag?.online ?? termin.online }

    /// Bearbeiten erlaubt (Recht, laufender Sprechtag, aktiver Termin).
    private var darf: Bool {
        (modell.ansicht?.rechte.bearbeiten ?? false) && (modell.sprechtag?.laufend ?? false)
            && ["angefragt", "bestaetigt"].contains(termin.status)
    }

    /// Dauer bleibt beim Verschieben erhalten.
    private var dauer: Int {
        max(Sprechtagzeit.minuten(termin.ende) - Sprechtagzeit.minuten(termin.beginn), modell.sprechtag?.dauer ?? 10)
    }

    private var ende: String { Sprechtagzeit.text(Sprechtagzeit.minuten(beginn) + dauer) }

    private var zeiten: [String] {
        Array(Set(frei + [termin.beginn])).filter { !$0.isEmpty }.sorted()
    }

    private var geaendert: Bool {
        beginn != termin.beginn || anmerkung != termin.anmerkung || (online && link != termin.link)
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Termin", untertitel: termin.schuelerName, symbol: "xmark", zurueck: { schliessen() }, laedt: laedt) {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    SprechtagAbschnittKopf(abschnitt: modell.sprechtag?.abschnitt(termin.abschnitt), raum: termin.raum, online: online)
                    HStack(spacing: 8) {
                        SprechtagPille.status(termin)
                        Text(termin.schuelerName)
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundStyle(Farben.tinte)
                        if let k = termin.schueler?.klasse, !k.isEmpty {
                            Text(k)
                                .font(.system(size: 14))
                                .foregroundStyle(Farben.gedaempft)
                        }
                    }
                    if admin {
                        Text(termin.lehrerTitel)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                    HStack(spacing: 12) {
                        SprechtagFeld(titel: "Startzeit") {
                            Picker("Startzeit", selection: $beginn) {
                                ForEach(zeiten, id: \.self) { z in
                                    Text(z).tag(z)
                                }
                            }
                            .pickerStyle(.menu)
                            .labelsHidden()
                            .tint(Farben.tinte)
                            .disabled(!darf)
                        }
                        SprechtagFeld(titel: "Endzeit") {
                            Text(ende)
                                .font(.system(size: 15).monospacedDigit())
                                .foregroundStyle(Farben.tinte2)
                        }
                    }
                    if online {
                        SprechtagFeld(titel: "Meeting-Link") {
                            TextField("https://…", text: $link)
                                .keyboardType(.URL)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                                .font(.system(size: 15))
                                .disabled(!darf)
                        }
                        if let url = Karte.onlineURL(link) {
                            Link(destination: url) {
                                Label("Online-Meeting öffnen", systemImage: "video")
                                    .font(.system(size: 14, weight: .medium))
                                    .foregroundStyle(Farben.akzent)
                            }
                        }
                    }
                    SprechtagFeld(titel: "Anmerkung") {
                        TextField("Anmerkung", text: $anmerkung, axis: .vertical)
                            .font(.system(size: 15))
                            .lineLimit(2...8)
                            .padding(.vertical, 10)
                            .disabled(!darf)
                    }
                    if !termin.grund.isEmpty {
                        Text("Grund: \(termin.grund)")
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if let fehler, !fehler.isEmpty {
                        Fehlerhinweis(text: fehler)
                    }
                    if darf {
                        VStack(spacing: 10) {
                            SprechtagKnopf(titel: "Speichern", breit: true, laedt: speichert) {
                                Task { await sichern() }
                            }
                            .disabled(!geaendert || speichert)
                            SprechtagKnopf(titel: "Termin absagen", stil: .gefahr, breit: true, laedt: modell.arbeitet == "x\(termin.id)") {
                                grund = ""
                                absagenFragen = true
                            }
                            .disabled(speichert || modell.arbeitet != nil)
                        }
                        .padding(.top, 4)
                    }
                }
                .padding(16)
            }
            .scrollDismissesKeyboard(.interactively)
        }
        .task {
            guard darf else { return }
            laedt = true
            defer { laedt = false }
            frei = ((try? await modell.api.sprechtagFrei(modell.id, lehrer: lehrer, abschnitt: termin.abschnitt, ausser: termin.id)) ?? []).map(\.von)
        }
        .alert("Termin absagen?", isPresented: $absagenFragen) {
            TextField("Grund (optional)", text: $grund)
            Button("Absagen", role: .destructive) {
                Task { await absagen() }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Der Termin wird abgesagt; die Erziehungsberechtigten werden benachrichtigt.")
        }
    }

    private func sichern() async {
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let tid = termin.id
        let daten = SprechtagAenderDaten(abschnitt: termin.abschnitt, beginn: beginn, ende: ende,
                                         anmerkung: anmerkung.trimmingCharacters(in: .whitespacesAndNewlines),
                                         link: online ? link.trimmingCharacters(in: .whitespacesAndNewlines) : nil)
        let ergebnis = await modell.ausfuehren("s\(tid)", fehlerAnzeigen: false) { api in
            try await api.sprechtagTerminAendern(sid, termin: tid, daten)
            return "Termin gespeichert."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }

    private func absagen() async {
        let sid = modell.id
        let tid = termin.id
        let g = grund
        let ergebnis = await modell.ausfuehren("x\(tid)", fehlerAnzeigen: false) { api in
            try await api.sprechtagTerminAbsagen(sid, termin: tid, grund: g)
            return "Termin abgesagt."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }
}

/// Abwesenheit eintragen bzw. bearbeiten: Start und Ende im Takt der Zeitblöcke, Anmerkung (höchstens 255 Zeichen);
/// Speichern bzw. Löschen.
struct SprechtagAbwesenheitBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let lehrer: String
    let abschnitt: Int
    let alt: SprechtagAbwesenheit?
    var admin: Bool = false
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var beginn: String
    @State private var ende: String
    @State private var anmerkung: String
    @State private var speichert = false
    @State private var fehler: String? = nil
    @State private var loeschenFragen = false

    private static let maxZeichen = 255

    init(modell: SprechtagModell, lehrer: String, abschnitt: Int, alt: SprechtagAbwesenheit?, admin: Bool = false) {
        _modell = ObservedObject(wrappedValue: modell)
        self.lehrer = lehrer
        self.abschnitt = abschnitt
        self.alt = alt
        self.admin = admin
        let a = modell.sprechtag?.abschnitt(abschnitt)
        let dauer = modell.sprechtag?.dauer ?? 10
        let von = a?.von ?? ""
        let bis = a?.bis ?? ""
        let vorEnde = von.isEmpty ? "" : Sprechtagzeit.text(min(Sprechtagzeit.minuten(von) + dauer, Sprechtagzeit.minuten(bis)))
        _beginn = State(initialValue: alt?.beginn ?? von)
        _ende = State(initialValue: alt?.ende ?? vorEnde)
        _anmerkung = State(initialValue: alt?.anmerkung ?? "")
    }

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var lehrkraft: SprechtagLehrkraft? { modell.ansicht?.lehrkraft(lehrer) }
    private var abschnittDaten: SprechtagAbschnitt? { modell.sprechtag?.abschnitt(abschnitt) }

    private var zeiten: [String] {
        guard let a = abschnittDaten else { return [beginn, ende].filter { !$0.isEmpty } }
        let raster = Sprechtagzeit.raster(von: a.von, bis: a.bis, dauer: modell.sprechtag?.dauer ?? 10)
        return Array(Set(raster + [beginn, ende])).filter { !$0.isEmpty }.sorted()
    }

    private var gueltig: Bool {
        !beginn.isEmpty && !ende.isEmpty && Sprechtagzeit.minuten(beginn) < Sprechtagzeit.minuten(ende)
    }

    var body: some View {
        DetailSeitenRahmen(titel: alt == nil ? "Abwesenheit eintragen" : "Abwesenheit bearbeiten",
                           untertitel: admin ? lehrkraft?.titel : abschnittDaten?.name, symbol: "xmark", zurueck: { schliessen() }) {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Text("Zeitslot als Pause oder Abwesenheit sperren.")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                    SprechtagAbschnittKopf(abschnitt: abschnittDaten, raum: lehrkraft?.raum(abschnitt) ?? "",
                                           online: modell.sprechtag?.online ?? false)
                    HStack(spacing: 12) {
                        SprechtagFeld(titel: "Startzeit") {
                            Picker("Startzeit", selection: $beginn) {
                                ForEach(zeiten, id: \.self) { z in
                                    Text(z).tag(z)
                                }
                            }
                            .pickerStyle(.menu)
                            .labelsHidden()
                            .tint(Farben.tinte)
                        }
                        SprechtagFeld(titel: "Endzeit") {
                            Picker("Endzeit", selection: $ende) {
                                ForEach(zeiten, id: \.self) { z in
                                    Text(z).tag(z)
                                }
                            }
                            .pickerStyle(.menu)
                            .labelsHidden()
                            .tint(Farben.tinte)
                        }
                    }
                    VStack(alignment: .trailing, spacing: 4) {
                        SprechtagFeld(titel: "Anmerkungen") {
                            TextField("Anmerkungen", text: $anmerkung, axis: .vertical)
                                .font(.system(size: 15))
                                .lineLimit(3...8)
                                .padding(.vertical, 10)
                        }
                        Text("\(anmerkung.count)/\(Self.maxZeichen)")
                            .font(.system(size: 12).monospacedDigit())
                            .foregroundStyle(anmerkung.count >= Self.maxZeichen ? Farben.entfallRand : Farben.gedaempft)
                    }
                    Text("Termine der Lehrkraft in diesem Zeitraum werden automatisch abgesagt; die Beteiligten werden benachrichtigt.")
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.gedaempft)
                        .fixedSize(horizontal: false, vertical: true)
                    if let fehler, !fehler.isEmpty {
                        Fehlerhinweis(text: fehler)
                    }
                    VStack(spacing: 10) {
                        SprechtagKnopf(titel: "Abwesenheit speichern", breit: true, laedt: speichert) {
                            Task { await sichern() }
                        }
                        .disabled(!gueltig || speichert)
                        if alt != nil {
                            SprechtagKnopf(titel: "Löschen", stil: .gefahr, breit: true) {
                                loeschenFragen = true
                            }
                            .disabled(speichert)
                        }
                    }
                    .padding(.top, 4)
                }
                .padding(16)
            }
            .scrollDismissesKeyboard(.interactively)
        }
        .onChange(of: anmerkung) { _, neu in
            if neu.count > Self.maxZeichen { anmerkung = String(neu.prefix(Self.maxZeichen)) }
        }
        .confirmationDialog("Abwesenheit löschen?", isPresented: $loeschenFragen, titleVisibility: .visible) {
            Button("Löschen", role: .destructive) {
                Task { await loeschen() }
            }
            Button("Abbrechen", role: .cancel) {}
        }
    }

    private func sichern() async {
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let daten = SprechtagAbwesenheitDaten(id: alt?.id, lehrer: lehrer, abschnitt: abschnitt, beginn: beginn, ende: ende,
                                              anmerkung: anmerkung.trimmingCharacters(in: .whitespacesAndNewlines))
        let ergebnis = await modell.ausfuehren("abw", fehlerAnzeigen: false) { api in
            try await api.sprechtagAbwesenheitSpeichern(sid, daten)
            return "Abwesenheit gespeichert."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }

    private func loeschen() async {
        guard let aid = alt?.id else { return }
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let ergebnis = await modell.ausfuehren("abw", fehlerAnzeigen: false) { api in
            try await api.sprechtagAbwesenheitLoeschen(sid, abwesenheit: aid)
            return "Abwesenheit gelöscht."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }
}

/// Einladung senden: Schüler*innen der Lehrkraft wählen (Suche), Nachricht (optional), verpflichtend.
struct SprechtagEinladungBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let lehrer: String
    var admin: Bool = false
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var liste: [SprechtagSchuelerWahl] = []
    @State private var gewaehlt: Set<Int> = []
    @State private var suche = ""
    @State private var text = ""
    @State private var verpflichtend = false
    @State private var laedt = false
    @State private var geladen = false
    @State private var speichert = false
    @State private var fehler: String? = nil

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var gefiltert: [SprechtagSchuelerWahl] {
        let q = suche.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return liste }
        return liste.filter { "\($0.anzeige) \($0.klasse)".lowercased().contains(q) }
    }

    /// Vor der 1. Anmeldephase werden Einladungen erst mit deren Beginn versendet.
    private var spaeterVersand: Bool {
        ["entwurf", "verwaltung"].contains(modell.sprechtag?.phase ?? "")
    }

    var body: some View {
        DetailSeitenRahmen(titel: "Einladung senden", untertitel: admin ? modell.ansicht?.lehrkraft(lehrer)?.titel : nil,
                           symbol: "xmark", zurueck: { schliessen() }, laedt: laedt) {
            VStack(spacing: 0) {
                Suchleiste(text: $suche, platzhalter: "Schüler*innen suchen")
                ScrollView {
                    VStack(alignment: .leading, spacing: 14) {
                        Text("Die Erziehungsberechtigten wählen die Uhrzeit selbst; gebuchte Termine sind sofort bestätigt."
                             + (spaeterVersand ? " Versand an die Erziehungsberechtigten mit Beginn der 1. Anmeldephase." : ""))
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                            .fixedSize(horizontal: false, vertical: true)
                        if geladen && liste.isEmpty {
                            SprechtagLeer(text: "Keine Schüler*innen im Unterricht dieser Lehrkraft.")
                        } else {
                            VStack(spacing: 0) {
                                ForEach(gefiltert) { s in
                                    schuelerZeile(s)
                                    Rectangle()
                                        .fill(Farben.trennlinie)
                                        .frame(height: 0.5)
                                        .padding(.leading, 46)
                                }
                            }
                            .background(Farben.eingabeFeld.opacity(0.5), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }
                        SprechtagFeld(titel: "Nachricht (optional)") {
                            TextField("Nachricht", text: $text, axis: .vertical)
                                .font(.system(size: 15))
                                .lineLimit(3...8)
                                .padding(.vertical, 10)
                        }
                        Toggle(isOn: $verpflichtend) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Verpflichtend")
                                    .font(.system(size: 15, weight: .medium))
                                    .foregroundStyle(Farben.tinte)
                                Text("Das Gespräch ist aus Sicht der Lehrkraft verpflichtend.")
                                    .font(.system(size: 13))
                                    .foregroundStyle(Farben.gedaempft)
                            }
                        }
                        .tint(Farben.akzent)
                        if let fehler, !fehler.isEmpty {
                            Fehlerhinweis(text: fehler)
                        }
                        SprechtagKnopf(titel: gewaehlt.isEmpty ? "Einladung senden" : "Einladung senden (\(gewaehlt.count))",
                                       breit: true, laedt: speichert) {
                            Task { await senden() }
                        }
                        .disabled(gewaehlt.isEmpty || speichert)
                    }
                    .padding(16)
                }
                .scrollDismissesKeyboard(.interactively)
            }
        }
        .onChange(of: text) { _, neu in
            if neu.count > 1000 { text = String(neu.prefix(1000)) }
        }
        .task {
            guard !geladen else { return }
            laedt = true
            defer { laedt = false }
            do {
                liste = try await modell.api.sprechtagSchueler(modell.id, lehrer: lehrer)
                fehler = nil
            } catch is CancellationError {
                return
            } catch {
                fehler = zustand.fehlertext(error)
            }
            geladen = true
        }
    }

    private func schuelerZeile(_ s: SprechtagSchuelerWahl) -> some View {
        let an = gewaehlt.contains(s.id)
        return Button {
            if an { gewaehlt.remove(s.id) } else { gewaehlt.insert(s.id) }
        } label: {
            HStack(spacing: 12) {
                Image(systemName: s.vorhanden ? "checkmark.circle" : (an ? "checkmark.circle.fill" : "circle"))
                    .font(.system(size: 20))
                    .foregroundStyle(s.vorhanden ? Farben.gedaempft : (an ? Farben.akzent : Farben.gedaempft))
                    .frame(width: 24)
                VStack(alignment: .leading, spacing: 1) {
                    Text(s.anzeige)
                        .font(.system(size: 15))
                        .foregroundStyle(s.vorhanden ? Farben.gedaempft : Farben.tinte)
                        .lineLimit(1)
                    Text(s.vorhanden ? "\(s.klasse) · bereits eingeladen" : s.klasse)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(1)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(s.vorhanden)
        .accessibilityAddTraits(an ? .isSelected : [])
    }

    private func senden() async {
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let nachricht = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let daten = SprechtagEinladungDaten(lehrer: lehrer, schuelerIds: Array(gewaehlt).sorted(),
                                            text: nachricht.isEmpty ? nil : nachricht, verpflichtend: verpflichtend)
        let ergebnis = await modell.ausfuehren("einladen", fehlerAnzeigen: false) { api in
            let n = try await api.sprechtagEinladen(sid, daten)
            return n == 1 ? "1 Einladung angelegt." : "\(n) Einladungen angelegt."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }
}

/// Termin buchen (Lehrkraft bzw. Administration): verbindlicher Termin mit fester Uhrzeit (z. B. nach telefonischer
/// Absprache); die Erziehungsberechtigten nehmen ihn an oder lehnen ihn ab.
struct SprechtagTerminAnlegenBlatt: View {
    @ObservedObject var modell: SprechtagModell
    let lehrer: String
    let abschnitt: Int
    var admin: Bool = false
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var schueler: [SprechtagSchuelerWahl] = []
    @State private var frei: [SprechtagZeit] = []
    @State private var kind = 0
    @State private var beginn = ""
    @State private var bloecke = 1
    @State private var link = ""
    @State private var anmerkung = ""
    @State private var verpflichtend = false
    @State private var laedt = false
    @State private var geladen = false
    @State private var speichert = false
    @State private var fehler: String? = nil

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var online: Bool { modell.sprechtag?.online ?? false }
    private var dauer: Int { modell.sprechtag?.dauer ?? 10 }
    private var ende: String { beginn.isEmpty ? "" : Sprechtagzeit.text(Sprechtagzeit.minuten(beginn) + bloecke * dauer) }
    private var gueltig: Bool { kind > 0 && !beginn.isEmpty }

    var body: some View {
        DetailSeitenRahmen(titel: "Termin buchen", untertitel: admin ? modell.ansicht?.lehrkraft(lehrer)?.titel : nil,
                           symbol: "xmark", zurueck: { schliessen() }, laedt: laedt) {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Text("Verbindlicher Termin mit festem Zeitslot (z. B. nach telefonischer Absprache). Die Erziehungsberechtigten nehmen ihn an oder lehnen ihn ab.")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                        .fixedSize(horizontal: false, vertical: true)
                    SprechtagAbschnittKopf(abschnitt: modell.sprechtag?.abschnitt(abschnitt),
                                           raum: modell.ansicht?.lehrkraft(lehrer)?.raum(abschnitt) ?? "", online: online)
                    SprechtagFeld(titel: "Schüler*in") {
                        Picker("Schüler*in", selection: $kind) {
                            Text("Bitte wählen").tag(0)
                            ForEach(schueler) { s in
                                Text(s.klasse.isEmpty ? s.anzeige : "\(s.anzeige) (\(s.klasse))").tag(s.id)
                            }
                        }
                        .pickerStyle(.menu)
                        .labelsHidden()
                        .tint(Farben.tinte)
                    }
                    HStack(spacing: 12) {
                        SprechtagFeld(titel: "Startzeit") {
                            if frei.isEmpty {
                                Text(geladen ? "Keine freie Zeit" : "…")
                                    .font(.system(size: 15))
                                    .foregroundStyle(Farben.gedaempft)
                            } else {
                                Picker("Startzeit", selection: $beginn) {
                                    ForEach(frei) { z in
                                        Text(z.von).tag(z.von)
                                    }
                                }
                                .pickerStyle(.menu)
                                .labelsHidden()
                                .tint(Farben.tinte)
                            }
                        }
                        SprechtagFeld(titel: "Dauer") {
                            Picker("Dauer", selection: $bloecke) {
                                ForEach(1...3, id: \.self) { n in
                                    Text("\(n * dauer) Minuten").tag(n)
                                }
                            }
                            .pickerStyle(.menu)
                            .labelsHidden()
                            .tint(Farben.tinte)
                        }
                    }
                    if !ende.isEmpty {
                        SprechtagMeta(symbol: "clock", text: "\(beginn) – \(ende) Uhr")
                    }
                    if online {
                        SprechtagFeld(titel: "Meeting-Link") {
                            TextField("https://…", text: $link)
                                .keyboardType(.URL)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                                .font(.system(size: 15))
                        }
                    }
                    SprechtagFeld(titel: "Anmerkung (optional)") {
                        TextField("Anmerkung", text: $anmerkung, axis: .vertical)
                            .font(.system(size: 15))
                            .lineLimit(2...8)
                            .padding(.vertical, 10)
                    }
                    Toggle(isOn: $verpflichtend) {
                        Text("Verpflichtend")
                            .font(.system(size: 15, weight: .medium))
                            .foregroundStyle(Farben.tinte)
                    }
                    .tint(Farben.akzent)
                    if let fehler, !fehler.isEmpty {
                        Fehlerhinweis(text: fehler)
                    }
                    SprechtagKnopf(titel: "Termin buchen", breit: true, laedt: speichert) {
                        Task { await anlegen() }
                    }
                    .disabled(!gueltig || speichert)
                }
                .padding(16)
            }
            .scrollDismissesKeyboard(.interactively)
        }
        .onChange(of: anmerkung) { _, neu in
            if neu.count > 1000 { anmerkung = String(neu.prefix(1000)) }
        }
        .task {
            guard !geladen else { return }
            laedt = true
            defer { laedt = false }
            let api = modell.api
            let sid = modell.id
            do {
                async let s = api.sprechtagSchueler(sid, lehrer: lehrer)
                async let f = api.sprechtagFrei(sid, lehrer: lehrer, abschnitt: abschnitt)
                let (liste, zeiten) = try await (s, f)
                schueler = liste
                frei = zeiten
                if beginn.isEmpty { beginn = zeiten.first?.von ?? "" }
                fehler = nil
            } catch is CancellationError {
                return
            } catch {
                fehler = zustand.fehlertext(error)
            }
            geladen = true
        }
    }

    private func anlegen() async {
        speichert = true
        defer { speichert = false }
        let sid = modell.id
        let text = anmerkung.trimmingCharacters(in: .whitespacesAndNewlines)
        let meeting = link.trimmingCharacters(in: .whitespacesAndNewlines)
        let daten = SprechtagTerminDaten(lehrer: lehrer, abschnitt: abschnitt, beginn: beginn, ende: ende, schuelerId: kind,
                                         anmerkung: text.isEmpty ? nil : text, link: online && !meeting.isEmpty ? meeting : nil,
                                         verpflichtend: verpflichtend)
        let ergebnis = await modell.ausfuehren("anlegen", fehlerAnzeigen: false) { api in
            try await api.sprechtagTerminAnlegen(sid, daten)
            return "Termin angelegt."
        }
        if let ergebnis { fehler = ergebnis } else { schliessen() }
    }
}

// MARK: - Bausteine

/// Farben des Sprechtags (Phasen- und Statuspillen, Infobanner) wie in der Weboberfläche, jeweils hell und dunkel.
enum SprechtagFarben {
    static let blau = Color(hell: 0x2F5BEA, dunkel: 0x8EA8FF)
    static let blauFlaeche = Color(hell: 0xE8EFFF, dunkel: 0x1D2747)
    static let braun = Color(hell: 0xA5481A, dunkel: 0xF3B48C)
    static let braunFlaeche = Color(hell: 0xFDF2D9, dunkel: 0x3B2A16)
    static let gruen = Color(hell: 0x16794A, dunkel: 0x74D7A6)
    static let gruenFlaeche = Color(hell: 0xE2F6EC, dunkel: 0x12392A)
}

/// Texte für Termine.
enum SprechtagText {
    /// „Freitag, 20.11.26“
    static func tag(_ x: SprechtagTermin) -> String {
        let kurz = x.datum.isEmpty ? x.datumText : Datumsformat.anzeige(x.datum, "dd.MM.yy")
        return [x.wochentag, kurz].filter { !$0.isEmpty }.joined(separator: ", ")
    }
}

/// Pille (Phase bzw. Status): kleine Kapsel mit Schrift in der Kennfarbe.
struct SprechtagPille: View {
    let text: String
    let schrift: Color
    let flaeche: Color

    var body: some View {
        Text(text)
            .font(.system(size: 12.5, weight: .medium))
            .foregroundStyle(schrift)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 9)
            .padding(.vertical, 3)
            .background(flaeche, in: Capsule())
    }

    /// Phase: Entwurf blau, Verwaltung LK und Anmeldephasen orange-braun, Durchführung grün, sonst grau.
    static func phase(_ phase: String, _ text: String) -> SprechtagPille {
        switch phase {
        case "entwurf":
            return SprechtagPille(text: text, schrift: SprechtagFarben.blau, flaeche: SprechtagFarben.blauFlaeche)
        case "verwaltung", "phase1", "phase1_beendet", "phase2":
            return SprechtagPille(text: text, schrift: SprechtagFarben.braun, flaeche: SprechtagFarben.braunFlaeche)
        case "durchfuehrung":
            return SprechtagPille(text: text, schrift: SprechtagFarben.gruen, flaeche: SprechtagFarben.gruenFlaeche)
        default:
            return grau(text)
        }
    }

    /// Status eines Termins: bestätigt grün, sonst grau.
    static func status(_ x: SprechtagTermin) -> SprechtagPille {
        x.bestaetigt ? SprechtagPille(text: x.statusAnzeige, schrift: SprechtagFarben.gruen, flaeche: SprechtagFarben.gruenFlaeche)
            : grau(x.statusAnzeige)
    }

    static func grau(_ text: String) -> SprechtagPille {
        SprechtagPille(text: text, schrift: Farben.pilleText, flaeche: Farben.pilleFlaeche)
    }
}

/// Blaues Infobanner: linker blauer Balken, i-Symbol, Titel fett und optional ein Text.
struct SprechtagInfobanner: View {
    let titel: String
    var text: String = ""

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "info.circle.fill")
                .font(.system(size: 18))
                .foregroundStyle(SprechtagFarben.blau)
            VStack(alignment: .leading, spacing: 4) {
                Text(titel)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Farben.tinte)
                    .fixedSize(horizontal: false, vertical: true)
                if !text.isEmpty {
                    Text(text)
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 12)
        .padding(.leading, 16)
        .padding(.trailing, 12)
        .background(SprechtagFarben.blauFlaeche)
        .overlay(alignment: .leading) {
            Rectangle()
                .fill(SprechtagFarben.blau)
                .frame(width: 4)
        }
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

/// Überschrift einer Gruppe mit optionalem Untertitel.
struct SprechtagGruppentitel: View {
    let titel: String
    var text: String = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(titel)
                .font(.system(size: 20, weight: .semibold))
                .foregroundStyle(Farben.tinte)
                .accessibilityAddTraits(.isHeader)
            if !text.isEmpty {
                Text(text)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.top, 8)
    }
}

/// Symbol mit Text (Kalender, Uhr, Raum …).
struct SprechtagMeta: View {
    let symbol: String
    let text: String

    var body: some View {
        if !text.isEmpty {
            Label {
                Text(text)
                    .lineLimit(1)
            } icon: {
                Image(systemName: symbol)
                    .foregroundStyle(Farben.gedaempft)
            }
            .font(.system(size: 14))
            .foregroundStyle(Farben.tinte2)
        }
    }
}

/// Ort eines Termins: Raum bzw. „Unbekannter Raum“; online der Link zum Meeting (bzw. „Online (Link folgt)“).
struct SprechtagOrt: View {
    let termin: SprechtagTermin
    let online: Bool

    var body: some View {
        if online {
            if let url = Karte.onlineURL(termin.link) {
                Link(destination: url) {
                    Label("Online-Meeting öffnen", systemImage: "video")
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(Farben.akzent)
                }
            } else {
                SprechtagMeta(symbol: "video", text: "Online (Link folgt)")
            }
        } else {
            SprechtagMeta(symbol: "door.left.hand.open", text: termin.raum.isEmpty ? "Unbekannter Raum" : termin.raum)
        }
    }
}

/// Pfeil rechts in antippbaren Karten.
struct SprechtagPfeil: View {
    var body: some View {
        Image(systemName: "chevron.right")
            .font(.system(size: 15, weight: .regular))
            .foregroundStyle(Farben.gedaempft)
    }
}

/// Grauer Hinweis für leere Bereiche.
struct SprechtagLeer: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 14))
            .foregroundStyle(Farben.gedaempft)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(14)
            .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

/// Fehler bzw. Erfolgsmeldung des Modells oben in der Ansicht.
struct SprechtagMeldungen: View {
    @ObservedObject var modell: SprechtagModell

    var body: some View {
        if modell.fehler != nil || modell.meldung != nil {
            VStack(spacing: 8) {
                if let fehler = modell.fehler {
                    Fehlerhinweis(text: fehler)
                }
                if let meldung = modell.meldung {
                    Label(meldung, systemImage: "checkmark.circle.fill")
                        .font(.subheadline)
                        .foregroundStyle(Farben.gruen)
                        .padding(12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                }
            }
        }
    }
}

/// Eingabefeld mit Beschriftung darüber (graue Fläche).
struct SprechtagFeld<Inhalt: View>: View {
    let titel: String
    let inhalt: Inhalt

    init(titel: String, @ViewBuilder inhalt: () -> Inhalt) {
        self.titel = titel
        self.inhalt = inhalt()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(titel)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(Farben.tinte2)
            inhalt
                .foregroundStyle(Farben.tinte)
                .padding(.horizontal, 12)
                .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Beschriftung eines Knopfs: orange (primär), orange umrandet (zweit) bzw. rot umrandet (Gefahr).
struct SprechtagKnopfBeschriftung: View {
    enum Stil {
        case primaer
        case zweit
        case gefahr
    }

    let titel: String
    var stil: Stil = .primaer
    var breit: Bool = false
    var laedt: Bool = false

    private var schrift: Color {
        switch stil {
        case .primaer: return Color.white
        case .zweit: return Farben.akzent
        case .gefahr: return Farben.entfallRand
        }
    }

    var body: some View {
        HStack(spacing: 8) {
            if laedt {
                ProgressView()
                    .tint(schrift)
                    .controlSize(.small)
            }
            Text(titel)
                .font(.system(size: 15, weight: .semibold))
                .lineLimit(1)
        }
        .foregroundStyle(schrift)
        .padding(.horizontal, 18)
        .frame(maxWidth: breit ? .infinity : nil)
        .frame(height: breit ? 46 : 38)
        .background {
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(stil == .primaer ? Farben.akzent : Farben.flaeche)
        }
        .overlay {
            if stil != .primaer {
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(schrift.opacity(0.6), lineWidth: 1)
            }
        }
        .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}

/// Knopf im Stil der Weboberfläche (siehe `SprechtagKnopfBeschriftung`); gesperrt halb durchsichtig.
struct SprechtagKnopf: View {
    let titel: String
    var stil: SprechtagKnopfBeschriftung.Stil = .primaer
    var breit: Bool = false
    var laedt: Bool = false
    let aktion: () -> Void
    @Environment(\.isEnabled) private var aktiviert

    var body: some View {
        Button(action: aktion) {
            SprechtagKnopfBeschriftung(titel: titel, stil: stil, breit: breit, laedt: laedt)
        }
        .buttonStyle(.plain)
        .opacity(aktiviert || laedt ? 1 : 0.45)
    }
}

/// Freie Zeit als Chip; gewählt orange gefüllt.
struct SprechtagZeitchip: View {
    let text: String
    let aktiv: Bool

    var body: some View {
        Text(text)
            .font(.system(size: 15, weight: .medium).monospacedDigit())
            .foregroundStyle(aktiv ? Color.white : Farben.tinte)
            .padding(.horizontal, 14)
            .frame(height: 36)
            .background(aktiv ? Farben.akzent : Farben.eingabeFeld, in: Capsule())
            .overlay {
                Capsule().strokeBorder(aktiv ? Color.clear : Farben.linie, lineWidth: 0.5)
            }
            .contentShape(Capsule())
    }
}

extension View {
    /// Weiße Karte des Sprechtags (abgerundet, volle Breite).
    func sprechtagKarte() -> some View {
        padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

/// Einfaches Fließlayout: Kinder nebeneinander, bei Platzmangel in der nächsten Zeile.
struct Fliesslayout: Layout {
    var abstand: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        anordnen(breite: proposal.width ?? .infinity, subviews: subviews).groesse
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let punkte = anordnen(breite: bounds.width, subviews: subviews).punkte
        for (i, kind) in subviews.enumerated() where i < punkte.count {
            kind.place(at: CGPoint(x: bounds.minX + punkte[i].x, y: bounds.minY + punkte[i].y), proposal: .unspecified)
        }
    }

    private func anordnen(breite: CGFloat, subviews: Subviews) -> (punkte: [CGPoint], groesse: CGSize) {
        var punkte: [CGPoint] = []
        var x: CGFloat = 0
        var y: CGFloat = 0
        var zeile: CGFloat = 0
        var weiteste: CGFloat = 0
        for kind in subviews {
            let g = kind.sizeThatFits(.unspecified)
            if x > 0 && x + g.width > breite {
                x = 0
                y += zeile + abstand
                zeile = 0
            }
            punkte.append(CGPoint(x: x, y: y))
            x += g.width
            weiteste = max(weiteste, x)
            x += abstand
            zeile = max(zeile, g.height)
        }
        return (punkte, CGSize(width: weiteste, height: y + zeile))
    }
}

// MARK: - Startseite

/// Karte „Sprechtag“ der Startseite im Stil der `Listenkarte` („Meine Termine“): Symbol, Name, Tag und Zeit, eine Statuszeile
/// (z. B. „Anmeldung bis 02.12. 18:00 · 3 Termine gebucht“), die eigenen Termine (Eltern: Lehrkraft und Raum; Lehrkraft:
/// Schüler*in und Klasse; heute der nächste markiert) und ein Knopf („Termine wählen“ bzw. „Termine verwalten“).
struct SprechtagStartkarte: View {
    let karte: SprechtagKarte
    let oeffnen: () -> Void

    private var eltern: Bool { karte.rolle == "eltern" }
    /// Heute steht der nächste Termin markiert oben in der Liste (die Statuszeile nennt ihn dann nicht).
    private var naechsterMarkiert: Bool { karte.heute && karte.statuszeile != karte.text }
    private var mehrereTage: Bool { Set(karte.eintraege.map(\.datum)).count > 1 }
    private var mehrereKinder: Bool { Set(karte.eintraege.map(\.schueler)).count > 1 }
    /// Gefüllter Knopf, außer Eltern können (noch) nichts buchen.
    private var hauptknopf: Bool { !eltern || karte.buchbar }

    private var punktFarbe: Color {
        if karte.heute { return Farben.gruen }
        if karte.offen > 0 || karte.status == "anmeldung" { return Farben.akzent }
        return Farben.gedaempft
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Button(action: oeffnen) {
                HStack(alignment: .center, spacing: 0) {
                    Image(systemName: karte.angekuendigt ? "megaphone" : "person.2")
                        .font(.system(size: 25, weight: .light))
                        .foregroundStyle(Farben.tinte2)
                        .frame(width: 56)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 4.75) {
                        Text(karte.titel)
                            .font(.system(size: 17))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(2)
                            .padding(.bottom, -1.9)
                        Text(karte.tagUndZeit)
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.tinte2)
                            .lineLimit(1)
                            .minimumScaleFactor(0.85)
                        HStack(alignment: .firstTextBaseline, spacing: 6) {
                            Circle()
                                .fill(punktFarbe)
                                .frame(width: 7, height: 7)
                                .alignmentGuide(.firstTextBaseline) { d in d[.bottom] + 1 }
                                .accessibilityHidden(true)
                            Text(karte.statuszeile)
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.tinte)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.leading, 1)
                }
                .padding(.trailing, 14)
                .padding(.top, 16.2)
                .padding(.bottom, 12)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityElement(children: .combine)

            if !karte.eintraege.isEmpty {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(karte.eintraege.enumerated()), id: \.element.id) { eintrag in
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 0.5)
                        zeile(eintrag.element, naechster: naechsterMarkiert && eintrag.offset == 0)
                    }
                    if karte.weitere > 0 {
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 0.5)
                        Text(karte.weitere == 1 ? "+ 1 weiterer Termin" : "+ \(karte.weitere) weitere Termine")
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                            .padding(.vertical, 9)
                    }
                }
                .padding(.leading, 57)
                .padding(.trailing, 14)
            }

            Button(action: oeffnen) {
                Text(karte.knopfText)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(hauptknopf ? Color.white : Farben.akzent)
                    .frame(maxWidth: .infinity)
                    .frame(height: 40)
                    .background(hauptknopf ? Farben.akzent : Farben.akzentSanft, in: Capsule())
                    .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 14)
            .padding(.top, 6)
            .padding(.bottom, 14)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
    }

    /// Termin: Uhrzeit, Lehrkraft (Eltern) bzw. Schüler*in (Lehrkraft), rechts Raum bzw. Hinweis.
    private func zeile(_ t: SprechtagStartTermin, naechster: Bool) -> some View {
        let wer = eltern ? t.lehrerName : t.schueler
        let unter = eltern ? (mehrereKinder ? t.vorname : "") : t.klasse
        return HStack(alignment: .firstTextBaseline, spacing: 10) {
            VStack(alignment: .leading, spacing: 1) {
                if mehrereTage {
                    Text(t.tagText)
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.gedaempft)
                }
                Text(t.beginn)
                    .font(.system(size: 15, weight: .medium))
                    .monospacedDigit()
                    .foregroundStyle(naechster ? Farben.gruen : Farben.tinte)
            }
            .frame(width: mehrereTage ? 86 : 48, alignment: .leading)
            VStack(alignment: .leading, spacing: 1) {
                Text(wer.isEmpty ? "–" : wer)
                    .font(.system(size: 15))
                    .foregroundStyle(t.status == "angefragt" ? Farben.tinte2 : Farben.tinte)
                    .lineLimit(1)
                if !unter.isEmpty {
                    Text(unter)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.gedaempft)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            VStack(alignment: .trailing, spacing: 3) {
                if naechster {
                    Text("Als Nächstes")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Farben.gruen)
                } else if !eltern && t.status == "angefragt" {
                    Text("Angefragt")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Farben.gedaempft)
                }
                if eltern && !t.raum.isEmpty {
                    Label(t.raum, systemImage: "mappin.and.ellipse")
                        .labelStyle(.titleAndIcon)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(1)
                }
            }
        }
        .padding(.vertical, 9)
        .accessibilityElement(children: .combine)
    }
}
