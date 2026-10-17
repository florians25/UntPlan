import SwiftUI

// Entschuldigungen in der App (Modul „Fachlehrer-Entschuldigungen“ und Entschuldigungen der Klassenleitung):
// - Klassenleitung: ganze Abwesenheiten der eigenen Klassen entschuldigen, ändern oder zurücknehmen (überschreibt die
//   Stunden der Fachlehrkräfte – mit Warnung), mit Stand „2/3 Lehrer bearbeitet“ und den Texten je Stunde.
// - Fachlehrkraft: einzelne Fehlstunden aus dem eigenen Unterricht entschuldigen (auch mehrere auf einmal); ist der
//   Status schon gesetzt, zeigt der Dialog nur noch den Text (gesperrt).
// Erreichbar über die Startseite („Entschuldigungen“), die Schülerliste der Stunde und die Abwesenheitsliste einer
// Person – jeweils durch Wischen nach links (runde Pill „Entschuldigen“) bzw. Antippen.

/// Was entschuldigt werden soll.
enum Entschuldigungsziel {
    /// Fehlstunden aus dem eigenen Unterricht (bereits geladen)
    case fehlstunden([Fehlstunde])
    /// Ganze Abwesenheit (bereits geladen, Klassenleitung)
    case abwesenheit(EntschuldigungsAbwesenheit)
    /// Fehlzeit-IDs einer Abwesenheit (Abwesenheitsliste) – der Dialog lädt die Abwesenheit
    case ids([Int])
    /// Fehlzeit einer Stunde (Schülerliste) – der Dialog sucht die Abwesenheit (Klassenleitung) bzw. die Fehlstunde
    case fehlzeit(id: Int, klasse: String, datum: String)
}

/// Auftrag für den Entschuldigungsdialog (als Blatt gezeigt).
struct Entschuldigungsauftrag: Identifiable {
    let id = UUID()
    let ziel: Entschuldigungsziel
    /// Name der Schülerin bzw. des Schülers (Kopf, solange noch geladen wird)
    var name: String = ""
    var klasse: String = ""
}

// MARK: - Dialog

/// Dialog „Entschuldigung“ im Stil von „Abwesenheit bearbeiten“: Kopf mit X, Titel und Haken (Klassenleitung zusätzlich
/// „Zurücknehmen“); Avatar mit Name und Klasse; ABWESENHEIT (Zeitraum, Grund, Stand der Lehrkräfte), STATUS als
/// Segment-Schalter, DATUM der Entschuldigung, NACHWEIS (Klassenleitung), TEXT; darunter die Fehlstunden mit den Texten
/// der Lehrkräfte (Klassenleitung) bzw. die Texte anderer Lehrkräfte derselben Abwesenheit (Fachlehrkraft).
struct EntschuldigenView: View {
    let auftrag: Entschuldigungsauftrag
    var fertig: () -> Void = {}
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    private enum Inhalt {
        case fach([Fehlstunde])
        case leitung(EntschuldigungsAbwesenheit)
    }

    @State private var inhalt: Inhalt? = nil
    @State private var laedt = true
    @State private var status: Entschuldigungsstatus = .entschuldigt
    @State private var datum = Date()
    @State private var nachweis: Entschuldigungsnachweis = .keiner
    @State private var text = ""
    @State private var speichert = false
    @State private var fehler: String? = nil
    @State private var zuruecknehmenFragen = false
    @State private var ueberschreibenFragen = false

    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    // MARK: Abgeleitet

    private var name: String {
        switch inhalt {
        case .fach(let l): return l.first?.schueler ?? auftrag.name
        case .leitung(let a): return a.schueler.isEmpty ? auftrag.name : a.schueler
        case nil: return auftrag.name
        }
    }

    private var klasse: String {
        switch inhalt {
        case .fach(let l): return l.first?.klasse ?? auftrag.klasse
        case .leitung(let a): return a.klasse.isEmpty ? auftrag.klasse : a.klasse
        case nil: return auftrag.klasse
        }
    }

    private var istLeitung: Bool {
        if case .leitung = inhalt { return true }
        return false
    }

    /// Fachlehrkraft: Status schon gesetzt bzw. keine Berechtigung → nur lesen. Klassenleitung: ohne Recht nur lesen.
    private var gesperrt: Bool {
        switch inhalt {
        case .fach(let l): return l.isEmpty || l.allSatisfy { $0.gesperrt || !$0.darf }
        case .leitung(let a): return !a.darf
        case nil: return true
        }
    }

    private var statusAuswahl: [(Entschuldigungsstatus, String)] {
        let liste: [Entschuldigungsstatus] = istLeitung ? [.offen, .entschuldigt, .unentschuldigt] : [.entschuldigt, .unentschuldigt]
        return liste.map { ($0, $0.anzeige) }
    }

    private var ueberschreibt: Int {
        if case .leitung(let a) = inhalt { return a.ueberschreibt(status) }
        return 0
    }

    /// Klassenleitung: gibt es etwas zurückzunehmen (Status bzw. Texte gesetzt)?
    private var zuruecknehmbar: Bool {
        guard case .leitung(let a) = inhalt, a.darf else { return false }
        return a.stand != .offen || a.stunden.contains { $0.stand != .offen || $0.entschuldigung != nil }
    }

    // MARK: Ansicht

    var body: some View {
        NavigationStack {
            DetailSeitenRahmen(titel: "Entschuldigung", untertitel: name.isEmpty ? nil : name, symbol: "xmark",
                               zurueck: { schliessen() }, laedt: laedt) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        kopf
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 0.5)
                        if inhalt != nil {
                            formular
                        } else if !laedt {
                            Text(fehler ?? "Für diese Abwesenheit ist keine Entschuldigung möglich.")
                                .font(.system(size: 15))
                                .foregroundStyle(Farben.gedaempft)
                                .padding(16)
                        }
                    }
                    .padding(.bottom, 30)
                }
                .scrollDismissesKeyboard(.interactively)
            } rechts: {
                HStack(spacing: 6) {
                    if zuruecknehmbar {
                        Button {
                            zuruecknehmenFragen = true
                        } label: {
                            Rundknopf(symbol: "arrow.uturn.backward", gewicht: .light, groesse: 18)
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Entschuldigung zurücknehmen")
                    }
                    SichernKnopf(speichert: speichert, aktiv: inhalt != nil && !gesperrt) {
                        if ueberschreibt > 0 {
                            ueberschreibenFragen = true
                        } else {
                            Task { await sichern() }
                        }
                    }
                }
            }
        }
        .environment(\.locale, Locale(identifier: "de_DE"))
        .confirmationDialog("Entschuldigung zurücknehmen?", isPresented: $zuruecknehmenFragen, titleVisibility: .visible) {
            Button("Zurücknehmen", role: .destructive) { Task { await zuruecknehmen() } }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Alle Stunden der Abwesenheit werden wieder offen; die Texte der Lehrkräfte werden entfernt.")
        }
        .confirmationDialog("Status überschreiben?", isPresented: $ueberschreibenFragen, titleVisibility: .visible) {
            Button("Überschreiben und sichern", role: .destructive) { Task { await sichern() } }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Der Entschuldigungsstatus von \(ueberschreibt == 1 ? "einer Fehlstunde" : "\(ueberschreibt) Fehlstunden") wird überschrieben.")
        }
        .task { await aufloesen() }
    }

    private var kopf: some View {
        HStack(spacing: 12) {
            Text(Initialen.kuerzel(name.isEmpty ? "?" : name))
                .font(.system(size: 17, weight: .regular))
                .foregroundStyle(.white)
                .frame(width: 44, height: 44)
                .background(Farben.avatar, in: Circle())
            VStack(alignment: .leading, spacing: 3) {
                Text(name.isEmpty ? "Schüler*in" : name)
                    .font(.system(size: 19))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                if !klasse.isEmpty {
                    Text(klasse)
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

    @ViewBuilder
    private var formular: some View {
        Feldtitel(text: "ABWESENHEIT")
        abwesenheitsblock
            .padding(.horizontal, 16)
        Feldtitel(text: "STATUS")
        Segmentschalter(auswahl: $status, statusAuswahl)
            .disabled(gesperrt)
            .opacity(gesperrt ? 0.6 : 1)
            .padding(.horizontal, 14)
        if ueberschreibt > 0 && !gesperrt {
            Label("Warnung: Der Entschuldigungsstatus \(ueberschreibt == 1 ? "einer Fehlstunde" : "von \(ueberschreibt) Fehlstunden") wird überschrieben.",
                  systemImage: "exclamationmark.triangle")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(Farben.entfallRand)
                .padding(.horizontal, 16)
                .padding(.top, 8)
        }
        Feldtitel(text: "DATUM")
        HStack {
            DatePicker("Datum der Entschuldigung", selection: $datum, in: ...Date(), displayedComponents: .date)
                .labelsHidden()
                .disabled(gesperrt)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 16)
        if istLeitung {
            Feldtitel(text: "NACHWEIS")
            Menu {
                Picker("Nachweis", selection: $nachweis) {
                    ForEach(Entschuldigungsnachweis.allCases) { n in
                        Text(n.anzeige).tag(n)
                    }
                }
            } label: {
                HStack {
                    Text(nachweis.anzeige)
                        .font(.system(size: 17))
                        .foregroundStyle(nachweis == .keiner ? Farben.gedaempft : Farben.tinte)
                    Spacer(minLength: 0)
                    Image(systemName: "chevron.up.chevron.down")
                        .font(.system(size: 14, weight: .regular))
                        .foregroundStyle(Farben.gedaempft)
                }
                .padding(.horizontal, 10)
                .frame(height: 52)
                .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .contentShape(Rectangle())
            }
            .tint(Color.primary)
            .disabled(gesperrt)
            .padding(.horizontal, 16)
        }
        Feldtitel(text: "TEXT")
        TextField(gesperrt ? "Kein Text" : "Text hier eingeben", text: $text, axis: .vertical)
            .font(.system(size: 16))
            .foregroundStyle(Farben.tinte)
            .lineLimit(3...8)
            .padding(10)
            .frame(minHeight: 104, alignment: .topLeading)
            .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .disabled(gesperrt)
            .padding(.horizontal, 16)
        if !istLeitung {
            Text("Der Text ist für Lehrkräfte sichtbar, die ebenfalls eine Fehlstunde in dieser Abwesenheit haben.")
                .font(.system(size: 12))
                .foregroundStyle(Farben.gedaempft)
                .padding(.horizontal, 16)
                .padding(.top, 6)
        }
        if let hinweis = sperrhinweis {
            Label(hinweis, systemImage: "lock")
                .font(.system(size: 13))
                .foregroundStyle(Farben.tinte2)
                .padding(.horizontal, 16)
                .padding(.top, 12)
        }
        if let fehler {
            Text(fehler)
                .font(.system(size: 14))
                .foregroundStyle(Farben.entfallRand)
                .padding(16)
        }
        stundenliste
    }

    private var sperrhinweis: String? {
        switch inhalt {
        case .fach(let l):
            guard gesperrt else { return nil }
            let h = l.map(\.hinweis).first { !$0.isEmpty }
            return h ?? "Der Status ist bereits gesetzt und kann nicht mehr geändert werden."
        case .leitung(let a):
            return a.darf ? nil : "Nur Ansicht – entschuldigen kann die Klassenleitung."
        case nil:
            return nil
        }
    }

    @ViewBuilder
    private var abwesenheitsblock: some View {
        switch inhalt {
        case .fach(let l):
            VStack(alignment: .leading, spacing: 4) {
                ForEach(l) { f in
                    Text([f.zeitraum, f.fach].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.tinte)
                }
                if let f = l.first, !f.abwesenheitText.isEmpty {
                    Text(f.abwesenheitText)
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
            }
        case .leitung(let a):
            VStack(alignment: .leading, spacing: 4) {
                Text(a.zeitraum)
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte)
                let info = [a.grund, a.text].filter { !$0.isEmpty }.joined(separator: " – ")
                if !info.isEmpty {
                    Text(info)
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
                HStack(spacing: 8) {
                    Entschuldigungsmarke(status: a.stand, mehrdeutig: a.mehrdeutig)
                    if let b = a.bearbeitetText {
                        Text(b)
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.gedaempft)
                    }
                }
                .padding(.top, 2)
            }
        case nil:
            EmptyView()
        }
    }

    /// Klassenleitung: die Fehlstunden mit Fach, Lehrkraft, Status und Text (wie die Seitenleiste im Web);
    /// Fachlehrkraft: Texte anderer Lehrkräfte derselben Abwesenheit, neueste zuerst.
    @ViewBuilder
    private var stundenliste: some View {
        switch inhalt {
        case .leitung(let a) where !a.stunden.isEmpty:
            Feldtitel(text: "FEHLSTUNDEN")
            VStack(spacing: 8) {
                ForEach(a.stunden) { s in
                    stundenkarte(zeit: "\(Datumsformat.anzeige(s.datum, "EE, dd.MM."))  \(Detailmass.uhrzeit(s.beginn)) - \(Detailmass.uhrzeit(s.ende))",
                                 wer: [s.fach, s.lehrer].filter { !$0.isEmpty }.joined(separator: " - "),
                                 status: s.stand, text: s.entschuldigung?.text ?? "")
                }
            }
            .padding(.horizontal, 16)
        case .fach(let l):
            let texte = l.flatMap(\.texte)
            let eigene = l.compactMap(\.entschuldigung).filter { !$0.text.isEmpty }
            if !eigene.isEmpty {
                Feldtitel(text: "ENTSCHULDIGUNG")
                VStack(spacing: 8) {
                    ForEach(Array(eigene.enumerated()), id: \.offset) { e in
                        stundenkarte(zeit: [e.element.lehrer, e.element.rolle == "klassenleitung" ? "Klassenleitung" : "",
                                            e.element.zeit].filter { !$0.isEmpty }.joined(separator: " · "),
                                     wer: "", status: e.element.stand, text: e.element.text)
                    }
                }
                .padding(.horizontal, 16)
            }
            if !texte.isEmpty {
                Feldtitel(text: "TEXTE ANDERER LEHRKRÄFTE")
                VStack(spacing: 8) {
                    ForEach(Array(texte.enumerated()), id: \.offset) { t in
                        stundenkarte(zeit: [t.element.lehrer, t.element.zeit].filter { !$0.isEmpty }.joined(separator: " · "),
                                     wer: [t.element.stunde > 0 ? "\(t.element.stunde). Std." : "", t.element.fach]
                                        .filter { !$0.isEmpty }.joined(separator: " "),
                                     status: Entschuldigungsstatus(text: t.element.status), text: t.element.text)
                    }
                }
                .padding(.horizontal, 16)
            }
        default:
            EmptyView()
        }
    }

    private func stundenkarte(zeit: String, wer: String, status: Entschuldigungsstatus, text: String) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .firstTextBaseline) {
                Text(zeit)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(Farben.tinte)
                Spacer(minLength: 6)
                Entschuldigungsmarke(status: status)
            }
            if !wer.isEmpty {
                Text(wer)
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.gedaempft)
            }
            if !text.isEmpty {
                Text(text)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.eingabeFeld, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    // MARK: Laden und Sichern

    private func aufloesen() async {
        defer { laedt = false }
        let rechte = await zustand.entschuldigungsrechteLaden()
        do {
            switch auftrag.ziel {
            case .fehlstunden(let l):
                uebernehmen(.fach(l))
            case .abwesenheit(let a):
                // Frisch laden (Stand der Fachlehrkräfte kann sich geändert haben); bei Fehlern der übergebene Stand
                let neu = (try? await zustand.api.entschuldigungsAbwesenheit(ids: a.ids)) ?? a
                uebernehmen(.leitung(neu))
            case .ids(let ids):
                uebernehmen(.leitung(try await zustand.api.entschuldigungsAbwesenheit(ids: ids)))
            case .fehlzeit(let id, let kl, let tag):
                if rechte.leitet(kl) {
                    let d = Datumsformat.datum(tag) ?? Date()
                    let liste = try await zustand.api.entschuldigungsAbwesenheiten(
                        von: Datumsformat.iso(Datumsformat.tage(-60, nach: d)), bis: Datumsformat.iso(Datumsformat.tage(14, nach: d)),
                        klasse: kl)
                    if let a = liste.first(where: { $0.ids.contains(id) || $0.stunden.contains { $0.id == id } }) {
                        uebernehmen(.leitung(a))
                        return
                    }
                }
                if rechte.fachlehrer {
                    let l = try await zustand.api.fehlstunden(von: tag, bis: tag, klasse: kl)
                    if let f = l.zeilen.first(where: { $0.id == id }) {
                        uebernehmen(.fach([f]))
                        return
                    }
                }
                fehler = "Für diese Fehlstunde ist keine Entschuldigung möglich."
            }
        } catch {
            fehler = zustand.fehlertext(error) ?? "Die Abwesenheit konnte nicht geladen werden."
        }
    }

    private func uebernehmen(_ neu: Inhalt) {
        inhalt = neu
        switch neu {
        case .fach(let l):
            let e = l.first?.entschuldigung
            let stand = l.first?.stand ?? .offen
            status = stand == .offen ? .entschuldigt : stand
            text = e?.text ?? ""
            if let d = e.flatMap({ Datumsformat.datum($0.datum) }) { datum = d }
        case .leitung(let a):
            status = a.stand == .offen ? .entschuldigt : a.stand
            nachweis = Entschuldigungsnachweis(rawValue: a.nachweis.lowercased()) ?? .keiner
            if let d = Datumsformat.datum(a.entschuldigtAm) { datum = d }
            // Eigener Text der Klassenleitung (falls schon entschuldigt)
            text = a.stunden.compactMap(\.entschuldigung).first { $0.rolle == "klassenleitung" && !$0.text.isEmpty }?.text ?? ""
        }
    }

    private func sichern() async {
        guard let inhalt else { return }
        speichert = true
        defer { speichert = false }
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        do {
            switch inhalt {
            case .fach(let l):
                let ids = l.filter { $0.darf && !$0.gesperrt }.map(\.id)
                let antwort = try await zustand.api.fehlstundenEntschuldigen(
                    FehlstundenEntschuldigenDaten(ids: ids, status: status.rawValue, text: t.isEmpty ? nil : t, datum: Datumsformat.iso(datum)))
                if !antwort.gesperrt.isEmpty && antwort.geaendert == 0 {
                    fehler = "Der Status wurde inzwischen gesetzt und kann nicht mehr geändert werden."
                    return
                }
            case .leitung(let a):
                _ = try await zustand.api.abwesenheitEntschuldigen(
                    AbwesenheitEntschuldigenDaten(ids: a.ids, status: status.rawValue, text: t.isEmpty ? nil : t,
                                                  datum: Datumsformat.iso(datum), nachweis: nachweis.rawValue))
            }
            fertig()
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error) ?? "Speichern fehlgeschlagen."
        }
    }

    private func zuruecknehmen() async {
        guard case .leitung(let a) = inhalt else { return }
        speichert = true
        defer { speichert = false }
        do {
            _ = try await zustand.api.entschuldigungZuruecknehmen(ids: a.ids)
            fertig()
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error) ?? "Zurücknehmen fehlgeschlagen."
        }
    }
}

/// Status als kleine Marke: entschuldigt grün, nicht entschuldigt rot, offen orange (mit „Mehrdeutig“).
struct Entschuldigungsmarke: View {
    let status: Entschuldigungsstatus
    var mehrdeutig: Bool = false

    private var farbe: Color {
        switch status {
        case .entschuldigt: return Farben.gruen
        case .unentschuldigt: return Farben.entfallRand
        case .offen: return Farben.orange
        }
    }

    var body: some View {
        Text(status.anzeige.uppercased() + (mehrdeutig && status == .offen ? " (MEHRDEUTIG)" : ""))
            .font(.system(size: 11, weight: .medium))
            .tracking(0.3)
            .foregroundStyle(farbe)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(farbe.opacity(0.13), in: Capsule())
    }
}

// MARK: - Liste (Startseite)

/// Seite „Entschuldigungen“: Klassenleitung – Abwesenheiten der eigenen Klassen; Fachlehrkraft – Fehlstunden aus dem
/// eigenen Unterricht (bei beiden Rechten oben umschaltbar). Filter „Offen | Alle“, Zeitraum die letzten 30 Tage.
/// Nach links wischen: runde Pill „Entschuldigen“; Antippen öffnet denselben Dialog. Fachlehrkräfte können über
/// „Auswählen“ mehrere Fehlstunden auf einmal entschuldigen.
struct EntschuldigungenView: View {
    enum Sicht: Hashable {
        case klassenleitung
        case fachlehrer
    }

    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var rechte: Entschuldigungsrechte? = nil
    @State private var sicht: Sicht = .klassenleitung
    @State private var nurOffene = true
    @State private var abwesenheiten: [EntschuldigungsAbwesenheit]? = nil
    @State private var fehlstunden: [Fehlstunde]? = nil
    @State private var fehler: String? = nil
    @State private var auftrag: Entschuldigungsauftrag? = nil
    @State private var auswahlModus = false
    @State private var auswahl: Set<Int> = []
    @State private var suche = ""

    private var von: Date { Datumsformat.tage(-30, nach: Date()) }

    var body: some View {
        VStack(spacing: 0) {
            if let r = rechte, r.fachlehrer && r.klassenleitung {
                Segmentschalter(auswahl: $sicht, [(Sicht.klassenleitung, "Klassenleitung"), (Sicht.fachlehrer, "Meine Stunden")])
                    .padding(.horizontal, 14)
                    .padding(.top, 4)
            }
            Segmentschalter(auswahl: $nurOffene, [(true, "Offen"), (false, "Alle")])
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
            liste
            if auswahlModus {
                auswahlLeiste
            } else {
                Suchleiste(text: $suche, platzhalter: "Suchen")
                    .padding(.bottom, 6)
            }
        }
        .background(Farben.blatt.ignoresSafeArea())
        .navigationTitle("Entschuldigungen")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .toolbar {
            ToolbarItem(placement: .principal) {
                VStack(spacing: 1) {
                    Text("Entschuldigungen")
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte2)
                    Text("\(Datumsformat.anzeige(von, "dd.MM.yyyy")) - \(Datumsformat.anzeige(Date(), "dd.MM.yyyy"))")
                        .font(.system(size: 11.5))
                        .foregroundStyle(Farben.gedaempft)
                }
                .lineLimit(1)
                .accessibilityElement(children: .combine)
            }
            if sicht == .fachlehrer {
                ToolbarItem(placement: .topBarTrailing) {
                    Button(auswahlModus ? "Fertig" : "Auswählen") {
                        withAnimation(.easeInOut(duration: 0.2)) {
                            auswahlModus.toggle()
                            auswahl = []
                        }
                    }
                    .font(.system(size: 16, weight: .regular))
                    .tint(Farben.akzent)
                }
            }
        }
        .detailPraesentation(item: $auftrag, breit: groessenklasse == .regular, art: .formular) { a in
            EntschuldigenView(auftrag: a) {
                auswahl = []
                auswahlModus = false
                Task { await laden() }
            }
            .environmentObject(zustand)
        }
        .refreshable { await laden() }
        .task {
            if rechte == nil {
                let r = await zustand.entschuldigungsrechteLaden(neu: true)
                rechte = r
                sicht = r.klassenleitung ? .klassenleitung : .fachlehrer
                await laden()
            }
        }
        .onChange(of: sicht) { _, _ in
            auswahlModus = false
            auswahl = []
            Task { await laden() }
        }
        .onChange(of: nurOffene) { _, _ in Task { await laden() } }
    }

    // MARK: Liste

    private func passt(_ texte: [String]) -> Bool {
        let b = suche.trimmingCharacters(in: .whitespacesAndNewlines)
        return b.isEmpty || texte.contains { $0.localizedCaseInsensitiveContains(b) }
    }

    @ViewBuilder
    private var liste: some View {
        ScrollView {
            LazyVStack(spacing: 0) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                        .padding(16)
                }
                if let r = rechte, !r.irgendwas {
                    leer("Entschuldigungen sind für dieses Konto nicht freigeschaltet.")
                } else if sicht == .klassenleitung {
                    if let liste = abwesenheiten?.filter({ passt([$0.schueler, $0.klasse, $0.grund, $0.text, $0.zeitraum]) }) {
                        if liste.isEmpty { leer(nurOffene ? "Keine offenen Abwesenheiten." : "Keine Abwesenheiten im Zeitraum.") }
                        ForEach(liste) { a in
                            WischZeile(rechtsMoeglich: false, linksMoeglich: a.darf,
                                       rechts: {}, links: { oeffnen(.abwesenheit(a), name: a.schueler, klasse: a.klasse) },
                                       linksAktion: .entschuldigen) {
                                Button {
                                    oeffnen(.abwesenheit(a), name: a.schueler, klasse: a.klasse)
                                } label: {
                                    abwesenheitsZeile(a)
                                }
                                .buttonStyle(ZeilenKnopfStil())
                            }
                        }
                    } else {
                        ProgressView().padding(.top, 40)
                    }
                } else {
                    if let liste = fehlstunden?.filter({ passt([$0.schueler, $0.klasse, $0.fach, $0.zeitraum]) }) {
                        if liste.isEmpty { leer(nurOffene ? "Keine offenen Fehlstunden." : "Keine Fehlstunden im Zeitraum.") }
                        ForEach(liste) { f in
                            let moeglich = f.darf && !f.gesperrt
                            WischZeile(rechtsMoeglich: false, linksMoeglich: moeglich && !auswahlModus,
                                       rechts: {}, links: { oeffnen(.fehlstunden([f]), name: f.schueler, klasse: f.klasse) },
                                       linksAktion: .entschuldigen) {
                                Button {
                                    if auswahlModus {
                                        guard moeglich else { return }
                                        if auswahl.contains(f.id) { auswahl.remove(f.id) } else { auswahl.insert(f.id) }
                                    } else {
                                        oeffnen(.fehlstunden([f]), name: f.schueler, klasse: f.klasse)
                                    }
                                } label: {
                                    fehlstundenZeile(f, moeglich: moeglich)
                                }
                                .buttonStyle(ZeilenKnopfStil())
                            }
                        }
                    } else {
                        ProgressView().padding(.top, 40)
                    }
                }
            }
        }
        .scrollIndicators(.hidden)
    }

    private func leer(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 15))
            .foregroundStyle(Farben.gedaempft)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 20)
            .padding(.top, 14)
    }

    private func abwesenheitsZeile(_ a: EntschuldigungsAbwesenheit) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(a.schueler)
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    if !a.klasse.isEmpty {
                        Text(a.klasse)
                            .font(.system(size: 12, weight: .medium))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                Text(a.zeitraum)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
                HStack(spacing: 8) {
                    Entschuldigungsmarke(status: a.stand, mehrdeutig: a.mehrdeutig)
                    if let b = a.bearbeitetText {
                        Text(b)
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.gedaempft)
                            .lineLimit(1)
                    }
                }
                .padding(.top, 1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.right")
                .font(.system(size: 19, weight: .ultraLight))
                .foregroundStyle(Farben.gedaempft)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Farben.trennlinie).frame(height: 0.5).padding(.horizontal, 16)
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    private func fehlstundenZeile(_ f: Fehlstunde, moeglich: Bool) -> some View {
        HStack(spacing: 12) {
            if auswahlModus {
                Image(systemName: auswahl.contains(f.id) ? "checkmark.circle.fill" : "circle")
                    .font(.system(size: 21, weight: .light))
                    .foregroundStyle(auswahl.contains(f.id) ? Farben.akzent : Farben.gedaempft)
                    .opacity(moeglich ? 1 : 0.35)
            }
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(f.schueler)
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    if !f.klasse.isEmpty {
                        Text(f.klasse)
                            .font(.system(size: 12, weight: .medium))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                Text([f.zeitraum, f.fach].filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.tinte2)
                HStack(spacing: 6) {
                    Entschuldigungsmarke(status: f.stand)
                    if f.gesperrt {
                        Image(systemName: "lock")
                            .font(.system(size: 11))
                            .foregroundStyle(Farben.gedaempft)
                            .accessibilityLabel("gesperrt")
                    }
                }
                .padding(.top, 1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if !auswahlModus {
                Image(systemName: "chevron.right")
                    .font(.system(size: 19, weight: .ultraLight))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Farben.trennlinie).frame(height: 0.5).padding(.horizontal, 16)
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(auswahl.contains(f.id) ? .isSelected : [])
    }

    /// Unten im Auswahlmodus: „Entschuldigen (n)“ als runde Pill.
    private var auswahlLeiste: some View {
        let gewaehlt = (fehlstunden ?? []).filter { auswahl.contains($0.id) }
        return Button {
            guard let erste = gewaehlt.first else { return }
            let gleich = gewaehlt.allSatisfy { $0.schuelerId == erste.schuelerId }
            oeffnen(.fehlstunden(gewaehlt), name: gleich ? erste.schueler : "\(gewaehlt.count) Fehlstunden", klasse: gleich ? erste.klasse : "")
        } label: {
            Label(gewaehlt.isEmpty ? "Fehlstunden auswählen" : "Entschuldigen (\(gewaehlt.count))", systemImage: "checkmark.seal")
                .font(.system(size: 16, weight: .medium))
                .foregroundStyle(.white)
                .padding(.horizontal, 22)
                .frame(height: 46)
                .background(gewaehlt.isEmpty ? Farben.zahlPlakette : Farben.entschuldigen, in: Capsule())
        }
        .buttonStyle(.plain)
        .disabled(gewaehlt.isEmpty)
        .padding(.vertical, 10)
    }

    private func oeffnen(_ ziel: Entschuldigungsziel, name: String, klasse: String) {
        auftrag = Entschuldigungsauftrag(ziel: ziel, name: name, klasse: klasse)
    }

    private func laden() async {
        let v = Datumsformat.iso(von)
        let b = Datumsformat.iso(Date())
        do {
            if sicht == .klassenleitung {
                abwesenheiten = try await zustand.api.entschuldigungsAbwesenheiten(von: v, bis: b, klasse: nil,
                                                                                  status: nurOffene ? "offen" : nil)
            } else {
                fehlstunden = try await zustand.api.fehlstunden(von: v, bis: b, klasse: nil, nurOffene: nurOffene).zeilen
            }
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
            if sicht == .klassenleitung, abwesenheiten == nil { abwesenheiten = [] }
            if sicht == .fachlehrer, fehlstunden == nil { fehlstunden = [] }
        }
    }
}
