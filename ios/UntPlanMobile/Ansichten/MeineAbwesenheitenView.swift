import SwiftUI

/// „Meine Abwesenheiten“ für Schüler*innen und Erziehungsberechtigte genau wie die Vorlage 82e54208 (Dunkelmodus, 3 px/pt):
/// Kopf mit rundem Zurück-Knopf, Mitte „Meine Abwesenheiten“ (15 pt semibold, grau) und darunter der Zeitraum (12 pt), rechts
/// ein runder Knopf mit Trichter (Filter). Darunter je Abwesenheit eine dunkelgraue Karte (16 pt Rand, 16 pt Abstand, neueste
/// zuerst, keine Datumsköpfe): Datum („15. Okt. - 16. Okt.“), Uhrzeit, Grund, Status.
struct MeineAbwesenheitenView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var daten: FamilieAbwesenheiten? = nil
    @State private var gruende: [String: String] = [:]
    @State private var kinder: [Kind] = []
    @State private var kind: Int? = nil
    @State private var zeigeFilter = false
    @State private var fehler: String? = nil
    @State private var laedt = false
    // Filter (bleiben gespeichert): Startzeitraum und „Nicht entschuldigte“
    @AppStorage("meineAbwesenheiten.startzeitraum") private var startzeitraum = Startzeitraum.schuljahr.rawValue
    @AppStorage("meineAbwesenheiten.nichtEntschuldigte") private var nichtEntschuldigte = false
    @AppStorage("meineAbwesenheiten.eigenVon") private var eigenVon = ""
    @AppStorage("meineAbwesenheiten.eigenBis") private var eigenBis = ""

    /// Zeitraum laut Startzeitraum (Schuljahr aus der Sitzung, sonst die Vorgabe des Servers).
    private var zeitraum: (von: String, bis: String)? {
        Startzeitraum.zeitraum(startzeitraum, schuljahr: zustand.sitzung?.schuljahr, eigenVon: eigenVon, eigenBis: eigenBis)
    }

    /// Neueste zuerst; mit „Nicht entschuldigte“ nur offene und nicht entschuldigte.
    private var zeilen: [Abwesenheitszeitraum] {
        (daten?.zeilen ?? []).filter { !nichtEntschuldigte || $0.status != "entschuldigt" }
            .sorted { ($0.vonDatum, $0.vonZeit) > ($1.vonDatum, $1.vonZeit) }
    }

    private var untertitel: String {
        let z = zeitraum ?? daten.map { ($0.von, $0.bis) }
        guard let z, !z.0.isEmpty else { return "" }
        return "\(Datumsformat.anzeige(z.0, "dd.MM.yyyy")) - \(Datumsformat.anzeige(z.1, "dd.MM.yyyy"))"
    }

    var body: some View {
        ScrollView {
            // Vorlage: Karten 16 pt vom Rand und 16 pt auseinander, erste Karte 26 pt unter dem Kopf
            LazyVStack(alignment: .leading, spacing: 16) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if daten != nil {
                    if zeilen.isEmpty {
                        Text(nichtEntschuldigte ? "Keine nicht entschuldigten Abwesenheiten im Zeitraum." : "Keine Abwesenheiten im Zeitraum.")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    ForEach(zeilen) { z in
                        Abwesenheitskarte(titel: Abwesenheitskarte.titel(von: z.vonDatum, bis: z.bisDatum), zeilen: kartenzeilen(z))
                    }
                } else if laedt {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 26)
            .padding(.bottom, 28)
            .zentriert(maxBreite: 736)
        }
        .scrollIndicators(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .refreshable { await laden() }
        .navigationTitle("Meine Abwesenheiten")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .toolbar {
            ToolbarItem(placement: .principal) {
                Listentitel(titel: "Meine Abwesenheiten", untertitel: untertitel)
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    zeigeFilter = true
                } label: {
                    Glasknopf { TrichterSymbol() }
                }
                .accessibilityLabel("Filter")
            }
        }
        .navigationDestination(isPresented: $zeigeFilter) {
            AbwesenheitsfilterView(startzeitraum: $startzeitraum, nichtEntschuldigte: $nichtEntschuldigte, eigenVon: $eigenVon, eigenBis: $eigenBis,
                                   kinder: kinder, kind: $kind)
        }
        .onChange(of: startzeitraum) { _, _ in Task { await laden() } }
        .onChange(of: eigenVon) { _, _ in Task { await laden() } }
        .onChange(of: eigenBis) { _, _ in Task { await laden() } }
        .onChange(of: kind) { _, _ in Task { await laden() } }
        .task {
            if daten == nil {
                await stammdatenLaden()
                await laden()
            }
        }
    }

    /// Wie die Vorlage nur Zeit und Art: Uhrzeit (bei Verspätung z. B. „7:45 - 7:57“), Abwesenheitsgrund, Text (nur wenn vorhanden),
    /// Status – ohne „Verspätung“ und Minuten (die stehen in den Berichten und Details).
    private func kartenzeilen(_ z: Abwesenheitszeitraum) -> [Abwesenheitskartenzeile] {
        var l = [Abwesenheitskartenzeile(symbol: "clock", text: Abwesenheitskarte.uhrzeit(vonDatum: z.vonDatum, vonZeit: z.vonZeit, bisDatum: z.bisDatum, bisZeit: z.bisZeit))]
        let grund = gruendeName(z.grund)
        if !grund.isEmpty { l.append(Abwesenheitskartenzeile(symbol: "info.circle", text: grund)) }
        let text = z.bemerkung.trimmingCharacters(in: .whitespacesAndNewlines)
        if !text.isEmpty { l.append(Abwesenheitskartenzeile(symbol: "text.bubble", text: text)) }
        l.append(Abwesenheitskarte.status(z.statusGemischt ? "offen" : z.status))
        return l
    }

    private func gruendeName(_ kurz: String) -> String {
        guard !kurz.isEmpty else { return "" }
        return gruende.first { $0.key.caseInsensitiveCompare(kurz) == .orderedSame }?.value ?? kurz
    }

    private func stammdatenLaden() async {
        if let g = try? await zustand.api.familieGruende() { gruende = g }
        if let k = try? await zustand.api.familieKinder() {
            kinder = k
            if kind == nil { kind = k.first?.id }
        }
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            let z = zeitraum
            daten = try await zustand.api.familieAbwesenheiten(schueler: kind, von: z?.von, bis: z?.bis, status: nil)
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Startzeitraum von „Meine Abwesenheiten“ (Vorlage afe1b12a); „eigen“ = über die Lupe gewählter Zeitraum.
enum Startzeitraum: String, CaseIterable, Identifiable {
    case woche1, wochen2, monat1, monate3, schuljahr, eigen

    var id: String { rawValue }

    var text: String {
        switch self {
        case .woche1: return "1 Woche"
        case .wochen2: return "2 Wochen"
        case .monat1: return "1 Monat"
        case .monate3: return "3 Monate"
        case .schuljahr: return "Aktuelles Schuljahr"
        case .eigen: return "Eigener Zeitraum"
        }
    }

    /// Von–bis (ISO): bis heute zurück bzw. das ganze Schuljahr bzw. der eigene Zeitraum.
    static func zeitraum(_ wert: String, schuljahr: Schuljahr?, eigenVon: String, eigenBis: String, heute: Date = Date()) -> (von: String, bis: String)? {
        let k = Datumsformat.kalender
        let h = Datumsformat.iso(heute)
        func zurueck(_ teil: Calendar.Component, _ n: Int) -> (String, String) {
            (Datumsformat.iso(k.date(byAdding: teil, value: -n, to: heute) ?? heute), h)
        }
        switch Startzeitraum(rawValue: wert) ?? .schuljahr {
        case .woche1: return zurueck(.day, 7)
        case .wochen2: return zurueck(.day, 14)
        case .monat1: return zurueck(.month, 1)
        case .monate3: return zurueck(.month, 3)
        case .eigen where !eigenVon.isEmpty && !eigenBis.isEmpty: return (eigenVon, eigenBis)
        default:
            guard let sj = schuljahr, !sj.von.isEmpty else { return nil }
            return (String(sj.von.prefix(10)), String(sj.bis.prefix(10)))
        }
    }
}

/// „Meine Abwesenheiten“ für Lehrkräfte: die eigenen gemeldeten Abwesenheiten im Grunddesign von „Meine Termine“ – nach
/// Datum gruppiert, je Meldung eine dunkle Karte mit Symbol links (Status) und den Zeilen Zeitraum, Grund, Bemerkung, Status.
struct EigeneAbwesenheitenView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var meldungen: [Meldung]? = nil
    @State private var fehler: String? = nil

    /// Neueste zuerst (wie die Vorlage), ohne Datumsköpfe.
    private var liste: [Meldung] {
        (meldungen ?? []).filter(\.istAbwesenheit).sorted { ($0.von ?? $0.erstellt) > ($1.von ?? $1.erstellt) }
    }

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 16) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if meldungen != nil {
                    if liste.isEmpty {
                        Text("Keine Abwesenheiten gemeldet.")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.top, 8)
                    }
                    ForEach(liste) { m in
                        karte(m)
                    }
                } else if fehler == nil {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 26)
            .padding(.bottom, 28)
            .zentriert(maxBreite: 736)
        }
        .scrollIndicators(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .refreshable { await laden() }
        .navigationTitle("Meine Abwesenheiten")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .toolbar {
            ToolbarItem(placement: .principal) {
                Listentitel(titel: "Meine Abwesenheiten")
            }
        }
        .task { if meldungen == nil { await laden() } }
    }

    private func karte(_ m: Meldung) -> some View {
        let offen = m.status == "offen"
        let von = m.von ?? m.erstellt
        var zeilen = [Abwesenheitskartenzeile(symbol: "clock", text: zeitraum(m))]
        let grund = (m.grund ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !grund.isEmpty { zeilen.append(Abwesenheitskartenzeile(symbol: "info.circle", text: grund)) }
        let bemerkung = (m.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !bemerkung.isEmpty { zeilen.append(Abwesenheitskartenzeile(symbol: "text.bubble", text: bemerkung)) }
        zeilen.append(offen ? Abwesenheitskartenzeile(symbol: "questionmark.circle", text: "offen", symbolFarbe: Farben.orange)
                            : Abwesenheitskartenzeile(symbol: "checkmark.circle", text: m.status == "quittiert" ? "bestätigt" : m.status, symbolFarbe: Farben.abwGruen))
        return Abwesenheitskarte(titel: Abwesenheitskarte.titel(von: von, bis: m.bis ?? von), zeilen: zeilen)
    }

    /// „ganztägig“ bzw. „2.–4. Stunde“, mehrtägig „bis Fr., 16.10.2026“.
    private func zeitraum(_ m: Meldung) -> String {
        let von = m.von ?? ""
        let bis = m.bis ?? von
        var teile: [String] = []
        if !bis.isEmpty && bis.prefix(10) != von.prefix(10) {
            teile.append("bis \(Datumsformat.anzeige(bis, "EE, dd.MM.yyyy"))")
        }
        if let v = m.stundeVon {
            let b = m.stundeBis ?? v
            teile.append(v == b ? "\(v). Stunde" : "\(v).–\(b). Stunde")
        } else if teile.isEmpty {
            teile.append("ganztägig")
        }
        return teile.joined(separator: " · ")
    }

    private func laden() async {
        do {
            meldungen = try await zustand.api.meldungen()
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
            if meldungen == nil { meldungen = [] }
        }
    }
}

/// Seite „Filter“ wie die Vorlage 039642d4: Zurück-Knopf und Titel „Filter“ (18 pt semibold), darunter durch 1 pt Linien
/// (#2A2A2C, 16 pt eingerückt) getrennte Zeilen von 69 pt: „Startzeitraum“ (17 pt) mit dem Wert (13 pt) und ›, „Nicht
/// entschuldigte“ mit Schalter; bei mehreren Kindern zusätzlich „Kind“.
struct AbwesenheitsfilterView: View {
    @Binding var startzeitraum: String
    @Binding var nichtEntschuldigte: Bool
    @Binding var eigenVon: String
    @Binding var eigenBis: String
    let kinder: [Kind]
    @Binding var kind: Int?

    private var wert: String {
        let s = Startzeitraum(rawValue: startzeitraum) ?? .schuljahr
        guard s == .eigen, !eigenVon.isEmpty else { return s.text }
        return "\(Datumsformat.anzeige(eigenVon, "dd.MM.yy")) - \(Datumsformat.anzeige(eigenBis, "dd.MM.yy"))"
    }

    var body: some View {
        VStack(spacing: 0) {
            Filterlinie()
            NavigationLink {
                StartzeitraumView(startzeitraum: $startzeitraum, eigenVon: $eigenVon, eigenBis: $eigenBis)
            } label: {
                HStack(spacing: 0) {
                    Text("Startzeitraum")
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.tinte)
                    Spacer(minLength: 12)
                    Text(wert)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.abwText)
                        .padding(.trailing, 26)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 17, weight: .light))
                        .foregroundStyle(Farben.abwSymbol)
                        .padding(.trailing, 9)
                }
                .frame(height: 68)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            Filterlinie()
            Toggle(isOn: $nichtEntschuldigte) {
                Text("Nicht entschuldigte")
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.tinte)
            }
            .tint(Farben.gruen)
            .frame(height: 68)
            Filterlinie()
            if kinder.count > 1 {
                Picker(selection: Binding(get: { kind ?? kinder.first?.id ?? 0 }, set: { kind = $0 })) {
                    ForEach(kinder) { k in Text(k.anzeige).tag(k.id) }
                } label: {
                    Text("Kind")
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.tinte)
                }
                .pickerStyle(.navigationLink)
                .frame(height: 68)
                Filterlinie()
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 16)
        .frame(maxWidth: 736)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle("Filter")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .principal) {
                Text("Filter")
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(Farben.tinte2)
            }
        }
    }
}

/// Trennlinie der Filterseite: 1 pt, #2A2A2C im Dunkelmodus.
struct Filterlinie: View {
    var body: some View {
        Rectangle().fill(Farben.abwLinie).frame(height: 1)
    }
}

/// Seite „Startzeitraum“ wie die Vorlage afe1b12a: Zurück-Knopf, Titel (18 pt semibold), rechts die Lupe; darunter ohne Linien
/// die Auswahl (17 pt, 52 pt Zeilenabstand) mit Häkchen rechts bei der gewählten Zeile. Die Lupe öffnet einen eigenen Zeitraum
/// (Von/Bis); er gilt, sobald beide Daten gewählt sind.
struct StartzeitraumView: View {
    @Binding var startzeitraum: String
    @Binding var eigenVon: String
    @Binding var eigenBis: String
    @State private var eigenZeigen = false
    @Environment(\.dismiss) private var schliessen

    private var auswahl: [Startzeitraum] { [.woche1, .wochen2, .monat1, .monate3, .schuljahr] + (startzeitraum == Startzeitraum.eigen.rawValue ? [.eigen] : []) }

    var body: some View {
        ScrollView {
            VStack(spacing: 0) {
                ForEach(auswahl) { s in
                    Button {
                        startzeitraum = s.rawValue
                        schliessen()
                    } label: {
                        HStack {
                            Text(s.text)
                                .font(.system(size: 17))
                                .foregroundStyle(Farben.tinte)
                            Spacer()
                            if startzeitraum == s.rawValue {
                                Image(systemName: "checkmark")
                                    .font(.system(size: 20, weight: .light))
                                    .foregroundStyle(Farben.tinte)
                                    .padding(.trailing, 2)
                                    .accessibilityLabel("ausgewählt")
                            }
                        }
                        .frame(height: 52)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                if eigenZeigen {
                    VStack(spacing: 0) {
                        Filterlinie()
                        DatePicker("Von", selection: datum($eigenVon, vorgabe: Datumsformat.tage(-30, nach: Date())), displayedComponents: .date)
                            .frame(height: 52)
                        DatePicker("Bis", selection: datum($eigenBis, vorgabe: Date()), displayedComponents: .date)
                            .frame(height: 52)
                    }
                    .font(.system(size: 17))
                    .foregroundStyle(Farben.tinte)
                    .environment(\.locale, Locale(identifier: "de_DE"))
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 16)
            .frame(maxWidth: 736)
            .frame(maxWidth: .infinity)
        }
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle("Startzeitraum")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .principal) {
                Text("Startzeitraum")
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(Farben.tinte2)
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    withAnimation { eigenZeigen.toggle() }
                } label: {
                    Glasknopf {
                        Image(systemName: "magnifyingglass")
                            .font(.system(size: 19, weight: .regular))
                    }
                }
                .accessibilityLabel("Eigener Zeitraum")
            }
        }
    }

    /// Datum als ISO-Text; Ändern wählt den eigenen Zeitraum.
    private func datum(_ text: Binding<String>, vorgabe: Date) -> Binding<Date> {
        Binding(get: { Datumsformat.datum(text.wrappedValue) ?? vorgabe }, set: {
            text.wrappedValue = Datumsformat.iso($0)
            if eigenVon.isEmpty { eigenVon = Datumsformat.iso(Datumsformat.tage(-30, nach: Date())) }
            if eigenBis.isEmpty { eigenBis = Datumsformat.iso(Date()) }
            startzeitraum = Startzeitraum.eigen.rawValue
        })
    }
}
