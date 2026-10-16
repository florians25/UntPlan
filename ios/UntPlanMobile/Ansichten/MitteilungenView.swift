import SwiftUI

/// Mitteilungen nach Vorlage: oben links Konto, Titel mittig, rechts Lupe und Plus als runde Glasknöpfe;
/// darunter die Reiter Posteingang | Gesendet | Entwürfe als Text, dann die Liste (Avatar, Absender, Datum, Betreff, Vorschau).
struct MitteilungenView: View {
    enum Ordner: String, CaseIterable, Identifiable {
        case eingang
        case gesendet
        case entwuerfe

        var id: String { rawValue }

        var titel: String {
            switch self {
            case .eingang: return "Posteingang"
            case .gesendet: return "Gesendet"
            case .entwuerfe: return "Entwürfe"
            }
        }

        /// Parameter "ordner" der API (nil = Posteingang)
        var apiWert: String? {
            switch self {
            case .eingang: return nil
            case .gesendet: return "gesendet"
            case .entwuerfe: return "entwuerfe"
            }
        }
    }

    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    @State private var ordner: Ordner = .eingang
    @State private var liste: [Mitteilung] = []
    @State private var geladen = false
    @State private var fehler: String? = nil
    @State private var suche = ""
    @State private var sucheOffen = false
    @State private var neueMitteilung = false

    private var gefiltert: [Mitteilung] {
        guard !suche.isEmpty else { return liste }
        return liste.filter {
            $0.betreff.localizedCaseInsensitiveContains(suche) || $0.text.localizedCaseInsensitiveContains(suche) ||
                $0.absender.localizedCaseInsensitiveContains(suche)
        }
    }

    private var rand: CGFloat { groessenklasse == .compact ? 16 : 20 }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                reiter
                if sucheOffen {
                    Suchleiste(text: $suche, platzhalter: "Mitteilungen durchsuchen")
                        .transition(.opacity)
                }
                inhalt
            }
            .background(Farben.leiste.ignoresSafeArea())
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.hidden, for: .navigationBar)
            .toolbar { leiste }
            .task(id: ordner) { await laden() }
            .sheet(isPresented: $neueMitteilung) {
                MitteilungNeuView {
                    if ordner == .gesendet { Task { await laden() } }
                }
                .environmentObject(zustand)
            }
        }
    }

    // MARK: - Kopf

    @ToolbarContentBuilder
    private var leiste: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            KontoKnopf()
        }
        ToolbarItem(placement: .principal) {
            Text("Mitteilungen")
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(Farben.titelGrau)
                .lineLimit(1)
        }
        ToolbarItemGroup(placement: .topBarTrailing) {
            Button {
                withAnimation(.easeOut(duration: 0.2)) {
                    sucheOffen.toggle()
                    if !sucheOffen { suche = "" }
                }
            } label: {
                Glasknopf {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 18, weight: .regular))
                }
            }
            .accessibilityLabel("Suchen")
            Button {
                neueMitteilung = true
            } label: {
                Glasknopf {
                    Image(systemName: "plus")
                        .font(.system(size: 19, weight: .regular))
                }
            }
            .accessibilityLabel("Neue Mitteilung")
        }
    }

    /// Reiter als Text: aktiver Reiter hell bzw. dunkel und fett, die anderen grau.
    private var reiter: some View {
        HStack(spacing: 28) {
            ForEach(Ordner.allCases) { o in
                let aktiv = o == ordner
                Button {
                    ordner = o
                } label: {
                    Text(o.titel)
                        .font(.system(size: 17, weight: .semibold))
                        .foregroundStyle(aktiv ? Farben.tinte : Farben.gedaempft)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                        .padding(.vertical, 10)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(aktiv ? .isSelected : [])
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, rand + 4)
        .padding(.bottom, 6)
        .background(Farben.leiste)
    }

    // MARK: - Liste

    @ViewBuilder
    private var inhalt: some View {
        List {
            if let fehler {
                Fehlerhinweis(text: fehler)
                    .listRowInsets(EdgeInsets(top: 8, leading: rand, bottom: 8, trailing: rand))
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
            }
            ForEach(gefiltert) { m in
                NavigationLink {
                    MitteilungDetailView(mitteilung: m, gesendet: ordner != .eingang) { id in
                        gelesenMarkiert(id)
                    }
                } label: {
                    Mitteilungszeile(mitteilung: m, gesendet: ordner != .eingang)
                }
                .listRowInsets(EdgeInsets(top: 10, leading: rand + 20, bottom: 10, trailing: rand))
                .listRowBackground(Farben.flaeche)
                .listRowSeparatorTint(Farben.trennlinie)
                .swipeActions(edge: .trailing) {
                    Button(role: .destructive) {
                        Task { await loeschen(m) }
                    } label: {
                        Label("Löschen", systemImage: "trash")
                    }
                }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(Farben.leiste)
        .overlay {
            if geladen && gefiltert.isEmpty && fehler == nil {
                ContentUnavailableView(suche.isEmpty ? "Keine Mitteilungen" : "Keine Treffer", systemImage: "tray",
                                       description: Text(leertext))
            } else if !geladen {
                ProgressView()
            }
        }
        .refreshable { await laden() }
    }

    private var leertext: String {
        if !suche.isEmpty { return "Keine Mitteilung passt zu „\(suche)“." }
        switch ordner {
        case .eingang: return "Ihr Posteingang ist leer."
        case .gesendet: return "Noch nichts gesendet."
        case .entwuerfe: return "Keine Entwürfe."
        }
    }

    private func laden() async {
        do {
            let neu = try await zustand.api.mitteilungen(ordner: ordner.apiWert)
            liste = neu
            fehler = nil
            if ordner == .eingang {
                zustand.ungelesen = neu.filter(\.ungelesen).count
            }
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        geladen = true
    }

    private func gelesenMarkiert(_ id: Int) {
        if let i = liste.firstIndex(where: { $0.id == id }), liste[i].gelesen == nil {
            liste[i].gelesen = Datumsformat.iso(Date())
            zustand.ungelesen = max(0, zustand.ungelesen - 1)
        }
    }

    private func loeschen(_ m: Mitteilung) async {
        do {
            try await zustand.api.mitteilungLoeschen(m.id)
            liste.removeAll { $0.id == m.id }
            if ordner == .eingang && m.ungelesen {
                zustand.ungelesen = max(0, zustand.ungelesen - 1)
            }
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Anzeige des Datums: heute mit Uhrzeit, sonst Tag und Monat (z. B. „27. Nov.“).
func mitteilungsdatum(_ erstellt: String) -> String {
    guard let d = Datumsformat.datum(erstellt) else { return erstellt }
    if Datumsformat.kalender.isDateInToday(d) && erstellt.count >= 16 {
        return Datumsformat.anzeige(d, "HH:mm")
    }
    return Datumsformat.anzeige(d, "d. MMM")
}

/// Zeile nach Vorlage: grauer Avatar-Kreis mit Anfangsbuchstabe, Absender, Datum rechts, Betreff, Vorschau.
struct Mitteilungszeile: View {
    let mitteilung: Mitteilung
    let gesendet: Bool

    private var name: String {
        if gesendet {
            return mitteilung.empfaenger.isEmpty ? "Ohne Empfänger" : "An: " + mitteilung.empfaenger.joined(separator: ", ")
        }
        return mitteilung.absender.isEmpty ? "Unbekannt" : mitteilung.absender
    }

    private var ungelesen: Bool { !gesendet && mitteilung.ungelesen }

    private var buchstabe: String {
        let quelle = gesendet ? (mitteilung.empfaenger.first ?? "?") : mitteilung.absender
        return quelle.first.map { String($0).uppercased() } ?? "?"
    }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(buchstabe)
                .font(.system(size: 18, weight: .medium))
                .foregroundStyle(.white)
                .frame(width: 44, height: 44)
                .background(Farben.avatar, in: Circle())
                .overlay(alignment: .topLeading) {
                    if ungelesen {
                        Circle()
                            .fill(Farben.akzent)
                            .frame(width: 11, height: 11)
                            .overlay(Circle().stroke(Farben.flaeche, lineWidth: 2))
                            .offset(x: -2, y: -2)
                    }
                }
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(name)
                        .font(.system(size: 17, weight: ungelesen ? .semibold : .regular))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    Spacer(minLength: 6)
                    Text(mitteilungsdatum(mitteilung.erstellt))
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(1)
                        .fixedSize()
                }
                HStack(spacing: 4) {
                    if mitteilung.wichtig {
                        Image(systemName: "exclamationmark.circle.fill")
                            .foregroundStyle(Farben.entfallRand)
                    }
                    Text(mitteilung.betreff.isEmpty ? "(ohne Betreff)" : mitteilung.betreff)
                        .fontWeight(ungelesen ? .semibold : .regular)
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(1)
                    if !mitteilung.anhaenge.isEmpty {
                        Image(systemName: "paperclip")
                            .foregroundStyle(Farben.gedaempft)
                    }
                }
                .font(.system(size: 15))
                Text(mitteilung.text)
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.tinte2)
                    .lineLimit(1)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

/// Neue Mitteilung schreiben: Empfänger (aus /api/app/mitteilungen/empfaenger), Betreff, Text, wichtig.
struct MitteilungNeuView: View {
    let gesendet: () -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen
    @State private var alle: [Empfaenger] = []
    @State private var gewaehlt: Set<String> = []
    @State private var betreff = ""
    @State private var text = ""
    @State private var wichtig = false
    @State private var suche = ""
    @State private var sendet = false
    @State private var fehler: String? = nil

    private var treffer: [Empfaenger] {
        guard !suche.isEmpty else { return alle }
        return alle.filter { $0.name.localizedCaseInsensitiveContains(suche) }
    }

    private var bereit: Bool {
        !gewaehlt.isEmpty && !betreff.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Nachricht") {
                    TextField("Betreff", text: $betreff)
                    TextField("Text", text: $text, axis: .vertical)
                        .lineLimit(5...14)
                    Toggle("Wichtig", isOn: $wichtig)
                        .tint(Farben.akzent)
                }
                if let fehler {
                    Section {
                        Text(fehler)
                            .foregroundStyle(Farben.entfallRand)
                    }
                }
                Section {
                    TextField("Empfänger suchen", text: $suche)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    if alle.isEmpty && fehler == nil {
                        ProgressView()
                            .frame(maxWidth: .infinity)
                    }
                    ForEach(treffer) { e in
                        Button {
                            if gewaehlt.contains(e.id) { gewaehlt.remove(e.id) } else { gewaehlt.insert(e.id) }
                        } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(e.name)
                                        .foregroundStyle(Farben.tinte)
                                    if !e.art.isEmpty {
                                        Text(e.art)
                                            .font(.caption)
                                            .foregroundStyle(Farben.gedaempft)
                                    }
                                }
                                Spacer()
                                if gewaehlt.contains(e.id) {
                                    Image(systemName: "checkmark")
                                        .foregroundStyle(Farben.akzent)
                                }
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                } header: {
                    Text(gewaehlt.isEmpty ? "Empfänger" : "Empfänger (\(gewaehlt.count))")
                }
            }
            .navigationTitle("Neue Mitteilung")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { schliessen() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    if sendet {
                        ProgressView()
                    } else {
                        Button("Senden") { Task { await senden() } }
                            .fontWeight(.semibold)
                            .disabled(!bereit)
                    }
                }
            }
            .task { await laden() }
        }
    }

    private func laden() async {
        do {
            alle = try await zustand.api.empfaenger()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }

    private func senden() async {
        sendet = true
        defer { sendet = false }
        do {
            try await zustand.api.mitteilungSenden(MitteilungNeuDaten(empfaenger: Array(gewaehlt),
                                                                       betreff: betreff.trimmingCharacters(in: .whitespacesAndNewlines),
                                                                       text: text.trimmingCharacters(in: .whitespacesAndNewlines),
                                                                       wichtig: wichtig))
            gesendet()
            schliessen()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Eine Mitteilung lesen (wird beim Öffnen als gelesen markiert).
struct MitteilungDetailView: View {
    let mitteilung: Mitteilung
    let gesendet: Bool
    let gelesen: (Int) -> Void
    @EnvironmentObject var zustand: AppZustand

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack(spacing: 6) {
                    if mitteilung.wichtig {
                        Plakette(text: "Wichtig", farbe: Farben.entfallRand)
                    }
                    Text(mitteilungsdatum(mitteilung.erstellt))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Text(mitteilung.betreff.isEmpty ? "(ohne Betreff)" : mitteilung.betreff)
                    .font(.title2.weight(.bold))
                HStack(spacing: 12) {
                    Initialen(name: gesendet ? (mitteilung.empfaenger.first ?? "?") : mitteilung.absender, groesse: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        if !gesendet {
                            Text(mitteilung.absender)
                                .font(.subheadline.weight(.semibold))
                        }
                        if !mitteilung.empfaenger.isEmpty {
                            Text("An: " + mitteilung.empfaenger.joined(separator: ", "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .lineLimit(3)
                        }
                    }
                }
                Divider()
                Text(mitteilung.text)
                    .font(.body)
                    .textSelection(.enabled)
                if !mitteilung.anhaenge.isEmpty {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Anhänge")
                            .font(.subheadline.weight(.semibold))
                        ForEach(mitteilung.anhaenge) { a in
                            Label(a.name, systemImage: "paperclip")
                                .font(.subheadline)
                        }
                        Text("Anhänge können in UntPlanWeb geöffnet werden.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .navigationBarTitleDisplayMode(.inline)
        .task {
            guard !gesendet, mitteilung.ungelesen else { return }
            do {
                try await zustand.api.alsGelesenMarkieren(mitteilung.id)
                gelesen(mitteilung.id)
            } catch {
                _ = zustand.fehlertext(error)
            }
        }
    }
}
