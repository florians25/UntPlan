import SwiftUI

/// Mitteilungen aus UntPlanWeb: Posteingang und Gesendet.
struct MitteilungenView: View {
    enum Ordner: String, CaseIterable, Identifiable {
        case eingang
        case gesendet

        var id: String { rawValue }

        var titel: String {
            switch self {
            case .eingang: return "Posteingang"
            case .gesendet: return "Gesendet"
            }
        }

        /// Parameter "ordner" der API (nil = Posteingang)
        var apiWert: String? {
            switch self {
            case .eingang: return nil
            case .gesendet: return "gesendet"
            }
        }
    }

    @EnvironmentObject var zustand: AppZustand
    @State private var ordner: Ordner = .eingang
    @State private var liste: [Mitteilung] = []
    @State private var geladen = false
    @State private var fehler: String? = nil
    @State private var suche = ""

    private var gefiltert: [Mitteilung] {
        guard !suche.isEmpty else { return liste }
        return liste.filter {
            $0.betreff.localizedCaseInsensitiveContains(suche) || $0.text.localizedCaseInsensitiveContains(suche) ||
                $0.absender.localizedCaseInsensitiveContains(suche)
        }
    }

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Picker("Ordner", selection: $ordner) {
                        ForEach(Ordner.allCases) { o in
                            Text(o.titel).tag(o)
                        }
                    }
                    .pickerStyle(.segmented)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 0, bottom: 4, trailing: 0))
                }
                if let fehler {
                    Fehlerhinweis(text: fehler)
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }
                Section {
                    ForEach(gefiltert) { m in
                        NavigationLink {
                            MitteilungDetailView(mitteilung: m, gesendet: ordner == .gesendet) { id in
                                gelesenMarkiert(id)
                            }
                        } label: {
                            Mitteilungszeile(mitteilung: m, gesendet: ordner == .gesendet)
                        }
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                Task { await loeschen(m) }
                            } label: {
                                Label("Löschen", systemImage: "trash")
                            }
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay {
                if geladen && liste.isEmpty && fehler == nil {
                    ContentUnavailableView("Keine Mitteilungen", systemImage: "tray",
                                           description: Text(ordner == .eingang ? "Ihr Posteingang ist leer." : "Noch nichts gesendet."))
                } else if !geladen {
                    ProgressView()
                }
            }
            .searchable(text: $suche, prompt: "Mitteilungen durchsuchen")
            .navigationTitle("Mitteilungen")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { KontoKnopf() }
            }
            .refreshable { await laden() }
            .task(id: ordner) { await laden() }
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

/// Anzeige des Datums: heute mit Uhrzeit, sonst Tag und Monat.
func mitteilungsdatum(_ erstellt: String) -> String {
    guard let d = Datumsformat.datum(erstellt) else { return erstellt }
    if Datumsformat.kalender.isDateInToday(d) && erstellt.count >= 16 {
        return Datumsformat.anzeige(d, "HH:mm")
    }
    return Datumsformat.anzeige(d, "d. MMM")
}

/// Zeile im Posteingang: Initialen, Absender, Betreff, Vorschau, Punkt für ungelesen.
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

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            ZStack(alignment: .topLeading) {
                Initialen(name: gesendet ? (mitteilung.empfaenger.first ?? "?") : mitteilung.absender)
                if ungelesen {
                    Circle()
                        .fill(Farben.akzent)
                        .frame(width: 11, height: 11)
                        .overlay(Circle().stroke(Color(.systemBackground), lineWidth: 2))
                        .offset(x: -3, y: -3)
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline) {
                    Text(name)
                        .font(.subheadline.weight(ungelesen ? .bold : .medium))
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    Text(mitteilungsdatum(mitteilung.erstellt))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                HStack(spacing: 4) {
                    if mitteilung.wichtig {
                        Image(systemName: "exclamationmark.circle.fill")
                            .foregroundStyle(Farben.entfallRand)
                    }
                    Text(mitteilung.betreff.isEmpty ? "(ohne Betreff)" : mitteilung.betreff)
                        .fontWeight(ungelesen ? .semibold : .regular)
                        .lineLimit(1)
                    if !mitteilung.anhaenge.isEmpty {
                        Image(systemName: "paperclip")
                            .foregroundStyle(.secondary)
                    }
                }
                .font(.subheadline)
                Text(mitteilung.text)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
        }
        .padding(.vertical, 4)
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
