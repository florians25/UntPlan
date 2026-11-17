import SwiftUI

/// „Meine Abwesenheiten“ für Schüler*innen und Erziehungsberechtigte im Grunddesign von „Meine Termine“: Kopf mit Zurück,
/// Titel und Zeitraum, rechts der Filter (Zeitraum, Status, bei mehreren Kindern das Kind). Die Abwesenheiten nach Datum
/// gruppiert („Do., 15.10.2026“), je Abwesenheit eine dunkle Karte mit dem Status-Symbol links (entschuldigt grün, offen
/// orange, nicht entschuldigt rot) und den Zeilen Art, Uhrzeit, Grund, Bemerkung (nur wenn vorhanden) und Status.
struct MeineAbwesenheitenView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var daten: FamilieAbwesenheiten? = nil
    @State private var gruende: [String: String] = [:]
    @State private var kinder: [Kind] = []
    @State private var filter = Abwesenheitsfilter()
    @State private var zeigeFilter = false
    @State private var fehler: String? = nil
    @State private var laedt = false
    @Environment(\.horizontalSizeClass) private var groessenklasse

    private var rand: CGFloat { groessenklasse == .compact ? 16 : 18 }

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 12) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if let daten {
                    // Oben bewusst keine Box mit Summen bzw. Statistik (der Server liefert summen weiter) – nur die Liste
                    if daten.zeilen.isEmpty {
                        Text(filter.status.isEmpty ? "Keine Abwesenheiten im gewählten Zeitraum." : "Keine Abwesenheiten mit diesem Status.")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.top, 8)
                    }
                    ForEach(Datumsgruppe.bilden(daten.zeilen, datum: \.vonDatum)) { g in
                        VStack(alignment: .leading, spacing: 12) {
                            Datumsgruppenkopf(text: Datumsgruppenkopf.text(g.datum))
                            ForEach(g.eintraege) { z in
                                Abwesenheitskarte(zeile: z, grund: gruendeName(z.grund))
                            }
                        }
                    }
                } else if laedt {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, rand)
            .padding(.top, 4)
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
                Listentitel(titel: "Meine Abwesenheiten",
                            untertitel: daten.map { $0.von.isEmpty ? "" : "\(Datumsformat.anzeige($0.von, "dd.MM.yyyy")) - \(Datumsformat.anzeige($0.bis, "dd.MM.yyyy"))" })
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    zeigeFilter = true
                } label: {
                    Glasknopf {
                        Image(systemName: filter.aktiv ? "line.3.horizontal.decrease.circle.fill" : "line.3.horizontal.decrease")
                            .font(.system(size: 17, weight: .regular))
                    }
                }
                .accessibilityLabel("Filter")
            }
        }
        .blattPraesentation(isPresented: $zeigeFilter, breit: groessenklasse == .regular) {
            AbwesenheitsfilterView(filter: $filter, kinder: kinder) {
                Task { await laden() }
            }
        }
        .task {
            if daten == nil {
                vorbelegen()
                await stammdatenLaden()
                await laden()
            }
        }
    }

    private func gruendeName(_ kurz: String) -> String {
        guard !kurz.isEmpty else { return "" }
        return gruende.first { $0.key.caseInsensitiveCompare(kurz) == .orderedSame }?.value ?? kurz
    }

    /// Zeitraum: ganzes Schuljahr (falls bekannt), sonst die Vorgabe des Servers.
    private func vorbelegen() {
        guard filter.von == nil, let sj = zustand.sitzung?.schuljahr,
              let von = Datumsformat.datum(sj.von), let bis = Datumsformat.datum(sj.bis) else { return }
        filter.von = von
        filter.bis = bis
    }

    private func stammdatenLaden() async {
        if let g = try? await zustand.api.familieGruende() { gruende = g }
        if let k = try? await zustand.api.familieKinder() {
            kinder = k
            if filter.kind == nil { filter.kind = k.first?.id }
        }
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            daten = try await zustand.api.familieAbwesenheiten(
                schueler: filter.kind, von: filter.von.map { Datumsformat.iso($0) }, bis: filter.bis.map { Datumsformat.iso($0) },
                status: filter.status)
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Filter der Abwesenheiten: Kind, Zeitraum und Status (leer = alle).
struct Abwesenheitsfilter: Equatable {
    var kind: Int? = nil
    var von: Date? = nil
    var bis: Date? = nil
    var status = ""

    var aktiv: Bool { !status.isEmpty }
}

/// Karte einer Abwesenheit im Grunddesign von „Meine Termine“: Status-Symbol links, Überschrift (Abwesenheit, Verspätung bzw.
/// vorzeitig gegangen), darunter Uhrzeit (mehrtägig mit Datum), Grund, Bemerkung (nur wenn vorhanden) und der Status.
struct Abwesenheitskarte: View {
    let zeile: Abwesenheitszeitraum
    let grund: String

    private var mehrtaegig: Bool { !zeile.bisDatum.isEmpty && zeile.bisDatum.prefix(10) != zeile.vonDatum.prefix(10) }

    /// „Abwesenheit“, „Verspätung (12 min)“ bzw. „Vorzeitig gegangen (20 min)“.
    private var titel: String {
        switch zeile.art {
        case "verspaetet": return zeile.minuten > 0 ? "Verspätung (\(zeile.minuten) min)" : "Verspätung"
        case "vorzeitig": return zeile.minuten > 0 ? "Vorzeitig gegangen (\(zeile.minuten) min)" : "Vorzeitig gegangen"
        default: return mehrtaegig ? "Abwesenheit (mehrere Tage)" : "Abwesenheit"
        }
    }

    /// „07:45 - 13:00“ bzw. über mehrere Tage „Do., 15.10., 07:45 - Fr., 16.10., 21:00“, dahinter die Fehlstunden.
    private var zeit: String {
        let stunden = zeile.fehlstunden > 0 ? " (\(zeile.fehlstunden) \(zeile.fehlstunden == 1 ? "Fehlstunde" : "Fehlstunden"))" : ""
        if !mehrtaegig {
            return "\(Detailmass.uhrzeit(zeile.vonZeit)) - \(Detailmass.uhrzeit(zeile.bisZeit))" + stunden
        }
        return "\(Datumsformat.anzeige(zeile.vonDatum, "EE, dd.MM.")), \(zeile.vonZeit) - \(Datumsformat.anzeige(zeile.bisDatum, "EE, dd.MM.")), \(zeile.bisZeit)" + stunden
    }

    private var status: (text: String, symbol: String, farbe: Color) {
        // einzelne Stunden entschuldigt (z. B. von Fachlehrkräften), andere nicht: „teilweise entschuldigt“ mit Anzahl
        if zeile.statusGemischt {
            return ("teilweise entschuldigt (\(zeile.entschuldigt)/\(zeile.fehlstunden))", "circle.lefthalf.filled", Farben.orange)
        }
        switch zeile.status {
        case "entschuldigt": return ("entschuldigt", "checkmark.circle", Farben.gruen)
        case "unentschuldigt": return ("nicht entschuldigt", "xmark.circle", Farben.entfallRand)
        default: return ("offen", "clock.badge.questionmark", Farben.orange)
        }
    }

    /// Zeilen unter der Überschrift: Uhrzeit, Grund, Bemerkung (nur wenn vorhanden), Status.
    private var zeilen: [Listenkartenzeile] {
        var z = [Listenkartenzeile(text: zeit)]
        if !grund.isEmpty { z.append(Listenkartenzeile(text: grund)) }
        let bemerkung = zeile.bemerkung.trimmingCharacters(in: .whitespacesAndNewlines)
        if !bemerkung.isEmpty { z.append(Listenkartenzeile(text: bemerkung, farbe: Farben.tinte2)) }
        z.append(Listenkartenzeile(text: status.text, farbe: status.farbe))
        return z
    }

    var body: some View {
        Listenkarte(symbol: status.symbol, symbolFarbe: status.farbe, titel: titel, zeilen: zeilen)
    }
}

/// „Meine Abwesenheiten“ für Lehrkräfte: die eigenen gemeldeten Abwesenheiten im Grunddesign von „Meine Termine“ – nach
/// Datum gruppiert, je Meldung eine dunkle Karte mit Symbol links (Status) und den Zeilen Zeitraum, Grund, Bemerkung, Status.
struct EigeneAbwesenheitenView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var meldungen: [Meldung]? = nil
    @State private var fehler: String? = nil

    private var rand: CGFloat { groessenklasse == .compact ? 16 : 18 }

    private var gruppen: [Datumsgruppe<Meldung>] {
        Datumsgruppe.bilden((meldungen ?? []).filter(\.istAbwesenheit)) { $0.von ?? $0.erstellt }
    }

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 12) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if meldungen != nil {
                    if gruppen.isEmpty {
                        Text("Keine Abwesenheiten gemeldet.")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.top, 8)
                    }
                    ForEach(gruppen) { g in
                        VStack(alignment: .leading, spacing: 12) {
                            Datumsgruppenkopf(text: Datumsgruppenkopf.text(g.datum))
                            ForEach(g.eintraege) { m in
                                karte(m)
                            }
                        }
                    }
                } else if fehler == nil {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, rand)
            .padding(.top, 4)
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
        let statusText = offen ? "Offen" : (m.status == "quittiert" ? "Bestätigt" : m.status)
        var zeilen = [Listenkartenzeile(text: zeitraum(m))]
        let grund = (m.grund ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !grund.isEmpty { zeilen.append(Listenkartenzeile(text: grund)) }
        // Bemerkung bzw. Text als eigene Zeile – nur wenn vorhanden
        let bemerkung = (m.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !bemerkung.isEmpty { zeilen.append(Listenkartenzeile(text: bemerkung, farbe: Farben.tinte2)) }
        zeilen.append(Listenkartenzeile(text: statusText, farbe: offen ? Farben.orange : Farben.gruen))
        return Listenkarte(symbol: offen ? "clock.badge.questionmark" : "checkmark.circle", symbolFarbe: offen ? Farben.orange : Farben.gruen,
                           titel: "Abwesenheit", zeilen: zeilen)
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

/// Filter-Blatt: Kind (bei mehreren), Zeitraum von–bis und Status.
struct AbwesenheitsfilterView: View {
    @Binding var filter: Abwesenheitsfilter
    let kinder: [Kind]
    let anwenden: () -> Void
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }
    @State private var entwurf = Abwesenheitsfilter()

    var body: some View {
        NavigationStack {
            DetailSeitenRahmen(titel: "Filter", untertitel: nil, symbol: "xmark", zurueck: { schliessen() }) {
            Form {
                if kinder.count > 1 {
                    Section("Kind") {
                        Picker("Kind", selection: Binding(get: { entwurf.kind ?? kinder.first?.id ?? 0 }, set: { entwurf.kind = $0 })) {
                            ForEach(kinder) { k in
                                Text(k.anzeige).tag(k.id)
                            }
                        }
                    }
                }
                Section("Zeitraum") {
                    DatePicker("Von", selection: Binding(get: { entwurf.von ?? Datumsformat.tage(-90, nach: Date()) }, set: { entwurf.von = $0 }),
                               displayedComponents: .date)
                    DatePicker("Bis", selection: Binding(get: { entwurf.bis ?? Date() }, set: { entwurf.bis = $0 }),
                               displayedComponents: .date)
                }
                Section("Status") {
                    Picker("Status", selection: $entwurf.status) {
                        Text("Alle").tag("")
                        Text("Offen").tag("offen")
                        Text("Entschuldigt").tag("entschuldigt")
                        Text("Nicht entschuldigt").tag("unentschuldigt")
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
            }
            .environment(\.locale, Locale(identifier: "de_DE"))
            .scrollContentBackground(.hidden)
            .background(Farben.grund)
            } rechts: {
                SichernKnopf(speichert: false, aktiv: true, beschriftung: "Anwenden") {
                    filter = entwurf
                    anwenden()
                    schliessen()
                }
            }
            .onAppear { entwurf = filter }
        }
    }
}
