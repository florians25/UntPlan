import SwiftUI

/// „Meine Abwesenheiten“ für Schüler*innen und Erziehungsberechtigte (Vorlage): Kopf mit Zurück, Titel und Zeitraum,
/// rechts der Filter (Zeitraum, Status, bei mehreren Kindern das Kind). Je Abwesenheit eine Karte: Datum groß
/// („15. Okt. - 16. Okt.“), Uhrzeit, Grund und Status (entschuldigt grün, offen orange, nicht entschuldigt rot).
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
            LazyVStack(spacing: 12) {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                }
                if let daten {
                    if daten.zeilen.isEmpty {
                        Text(filter.status.isEmpty ? "Keine Abwesenheiten im gewählten Zeitraum." : "Keine Abwesenheiten mit diesem Status.")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.top, 8)
                    }
                    ForEach(daten.zeilen) { z in
                        Abwesenheitskarte(zeile: z, grund: gruendeName(z.grund))
                    }
                } else if laedt {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 40)
                }
            }
            .padding(.horizontal, rand)
            .padding(.top, 8)
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
                VStack(spacing: 1) {
                    Text("Meine Abwesenheiten")
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte2)
                    if let daten, !daten.von.isEmpty {
                        Text("\(Datumsformat.anzeige(daten.von, "dd.MM.yyyy")) - \(Datumsformat.anzeige(daten.bis, "dd.MM.yyyy"))")
                            .font(.system(size: 11.5))
                            .foregroundStyle(Farben.gedaempft)
                    }
                }
                .lineLimit(1)
                .accessibilityElement(children: .combine)
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
        .sheet(isPresented: $zeigeFilter) {
            AbwesenheitsfilterView(filter: $filter, kinder: kinder) {
                Task { await laden() }
            }
            .presentationDetents([.medium, .large])
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

/// Karte einer Abwesenheit wie in der Vorlage.
struct Abwesenheitskarte: View {
    let zeile: Abwesenheitszeitraum
    let grund: String

    /// „15. Okt. - 16. Okt.“ bzw. „8. Okt.“
    private var titel: String {
        let von = Datumsformat.anzeige(zeile.vonDatum, "d. MMM")
        if zeile.bisDatum.isEmpty || zeile.bisDatum == zeile.vonDatum { return von }
        return "\(von) - \(Datumsformat.anzeige(zeile.bisDatum, "d. MMM"))"
    }

    /// „7:45 - 21:00“ bzw. über mehrere Tage „Do., 07:45 - Fr., 21:00“.
    private var zeit: String {
        if zeile.bisDatum.isEmpty || zeile.bisDatum == zeile.vonDatum {
            return "\(Detailmass.uhrzeit(zeile.vonZeit)) - \(Detailmass.uhrzeit(zeile.bisZeit))"
        }
        return "\(Datumsformat.anzeige(zeile.vonDatum, "EE")), \(zeile.vonZeit) - \(Datumsformat.anzeige(zeile.bisDatum, "EE")), \(zeile.bisZeit)"
    }

    private var status: (text: String, symbol: String, farbe: Color) {
        switch zeile.status {
        case "entschuldigt": return ("entschuldigt", "checkmark.circle", Farben.gruen)
        case "unentschuldigt": return ("nicht entschuldigt", "xmark.circle", Farben.entfallRand)
        default: return ("offen", "clock.badge.questionmark", Farben.orange)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(titel)
                .font(.system(size: 19, weight: .regular))
                .foregroundStyle(Farben.tinte)
                .padding(.bottom, 2)
            zeileMitSymbol("clock", zeit, farbe: Farben.tinte2)
            let info = [grund, zeile.text].filter { !$0.isEmpty }.joined(separator: " – ")
            if !info.isEmpty {
                zeileMitSymbol("info.circle", info, farbe: Farben.tinte2)
            }
            zeileMitSymbol(status.symbol, status.text, farbe: Farben.tinte2, symbolFarbe: status.farbe)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 12)
        .padding(.vertical, 12)
        .background(Farben.flaeche, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    private func zeileMitSymbol(_ symbol: String, _ text: String, farbe: Color, symbolFarbe: Color? = nil) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Image(systemName: symbol)
                .font(.system(size: 12, weight: .regular))
                .foregroundStyle(symbolFarbe ?? Farben.gedaempft)
            Text(text)
                .font(.system(size: 14))
                .foregroundStyle(farbe)
                .lineLimit(3)
        }
    }
}

/// Filter-Blatt: Kind (bei mehreren), Zeitraum von–bis und Status.
struct AbwesenheitsfilterView: View {
    @Binding var filter: Abwesenheitsfilter
    let kinder: [Kind]
    let anwenden: () -> Void
    @Environment(\.dismiss) private var schliessen
    @State private var entwurf = Abwesenheitsfilter()

    var body: some View {
        NavigationStack {
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
            .navigationTitle("Filter")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { schliessen() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Anwenden") {
                        filter = entwurf
                        anwenden()
                        schliessen()
                    }
                }
            }
            .onAppear { entwurf = filter }
        }
    }
}
