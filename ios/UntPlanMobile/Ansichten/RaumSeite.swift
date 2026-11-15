import SwiftUI

/// Raumliste der Raumbuchung (wie in der PWA): Kopf „C197 ersetzen“ bzw. „Raum hinzufügen“, darunter Suchfeld und Filter
/// nach Abteilung (Raumart), dann die freien buchbaren Räume der Stunde. Antippen bucht den Raum als Raumänderung
/// (POST /api/app/mobil/raum); danach zeigen die Details die neuen Räume und der Stundenplan lädt neu.
struct RaumSeite: View {
    let ziel: RaumZiel
    @EnvironmentObject var daten: Stundendaten
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var zurueck
    @State private var raeume: [FreierRaum]? = nil
    @State private var suche = ""
    @State private var abteilung = ""
    @State private var bucht: String? = nil
    @State private var fehler: String? = nil

    private var titel: String {
        ziel.aktion == "ersetzen" ? "\(ziel.alt) ersetzen" : "Raum hinzufügen"
    }

    /// Abteilungen (Raumarten) der freien Räume.
    private var abteilungen: [String] {
        let arten = Set((raeume ?? []).map(\.raumart).filter { !$0.isEmpty })
        return arten.sorted { $0.localizedCompare($1) == .orderedAscending }
    }

    private var treffer: [FreierRaum] {
        let b = suche.trimmingCharacters(in: .whitespacesAndNewlines)
        return (raeume ?? []).filter { r in
            (abteilung.isEmpty || r.raumart == abteilung)
                && (b.isEmpty || r.kurz.localizedCaseInsensitiveContains(b) || r.name.localizedCaseInsensitiveContains(b))
        }
    }

    var body: some View {
        DetailSeitenRahmen(titel: titel, untertitel: nil, laedt: raeume == nil) {
            VStack(spacing: 0) {
                Suchleiste(text: $suche, platzhalter: "Suchen")
                HStack {
                    Menu {
                        Button {
                            abteilung = ""
                        } label: {
                            Menuezeile(titel: "Alle Abteilungen", gewaehlt: abteilung.isEmpty)
                        }
                        ForEach(abteilungen, id: \.self) { a in
                            Button {
                                abteilung = a
                            } label: {
                                Menuezeile(titel: a, gewaehlt: a == abteilung)
                            }
                        }
                    } label: {
                        // Vorlage: Kapsel mit grauem Rand „Abteilung“ (gewählt: orange gefüllt mit dem Namen)
                        Text(abteilung.isEmpty ? "Abteilung" : abteilung)
                            .lineLimit(1)
                            .font(.system(size: 13, weight: .regular))
                            .foregroundStyle(abteilung.isEmpty ? Farben.tinte2 : Color.white)
                            .padding(.horizontal, 10)
                            .frame(height: 32)
                            .background(abteilung.isEmpty ? Color.clear : Farben.akzent, in: Capsule())
                            .overlay(Capsule().stroke(abteilung.isEmpty ? Farben.linie : Color.clear, lineWidth: 1))
                    }
                    .tint(Color.primary)
                    .disabled(abteilungen.isEmpty)
                    Spacer(minLength: 0)
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 8)
                liste
            }
        }
        .task { await laden() }
        .alert("Nicht gespeichert", isPresented: Binding(get: { fehler != nil }, set: { if !$0 { fehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(fehler ?? "")
        }
    }

    @ViewBuilder
    private var liste: some View {
        if let raeume {
            ScrollView {
                LazyVStack(spacing: 0) {
                    if treffer.isEmpty {
                        Text(raeume.isEmpty ? "Kein buchbarer Raum frei" : "Kein Raum gefunden")
                            .font(.system(size: 15))
                            .foregroundStyle(Farben.gedaempft)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 20)
                            .padding(.top, 12)
                    }
                    ForEach(treffer) { r in
                        Button {
                            Task { await buchen(r) }
                        } label: {
                            zeile(r)
                        }
                        .buttonStyle(OhneTippStil())
                        .disabled(bucht != nil)
                    }
                }
                .padding(.bottom, 24)
            }
            .scrollIndicators(.hidden)
            .scrollDismissesKeyboard(.interactively)
        } else {
            VStack(spacing: 10) {
                Text("Freie Räume werden gesucht …")
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.gedaempft)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    /// Zeile wie in der Vorlage: graue Scheibe mit weißem Raumsymbol, daneben nur das Kürzel; Trennlinie ab dem Text.
    private func zeile(_ r: FreierRaum) -> some View {
        HStack(spacing: 14) {
            Image("SymbolRaum")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: 17, height: 17)
                .foregroundStyle(.white)
                .frame(width: 32, height: 32)
                .background(Farben.raumScheibe, in: Circle())
            Text(r.kurz)
                .font(.system(size: 15))
                .foregroundStyle(Farben.detailText)
                .lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .leading)
            if bucht == r.kurz {
                ProgressView()
            }
        }
        .padding(.horizontal, 16)
        .frame(height: 44)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Farben.trennlinie).frame(height: 0.5).padding(.leading, 62).padding(.trailing, 16)
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityLabel([r.kurz, r.name != r.kurz ? r.name : "", r.urspruenglich ? "ursprünglicher Raum" : ""].filter { !$0.isEmpty }.joined(separator: ", "))
        .accessibilityHint("Raum buchen")
    }

    private func laden() async {
        guard raeume == nil, let u = daten.karte.unterricht else { return }
        do {
            let liste = try await zustand.api.freieRaeume(datum: daten.karte.datum, von: daten.karte.von, bis: daten.karte.bis, unterricht: u)
            // Räume, die schon zur Stunde gehören, stehen nicht zur Wahl
            let belegt = Set(daten.aktuelleRaeume.map { $0.k.uppercased() }.filter { !$0.isEmpty })
            raeume = liste.filter { !belegt.contains($0.kurz.uppercased()) }
        } catch {
            fehler = zustand.fehlertext(error)
            raeume = []
        }
    }

    private func buchen(_ r: FreierRaum) async {
        bucht = r.kurz
        defer { bucht = nil }
        do {
            let meldung = try await daten.raumAendern(aktion: ziel.aktion, raum: r.kurz,
                                                      alt: ziel.aktion == "ersetzen" ? ziel.alt : nil)
            zustand.planGeaendert += 1
            withAnimation { daten.hinweis = meldung }
            zurueck()
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

/// Menüeintrag mit Häkchen, wenn gewählt.
struct Menuezeile: View {
    let titel: String
    let gewaehlt: Bool

    var body: some View {
        if gewaehlt {
            Label(titel, systemImage: "checkmark")
        } else {
            Text(titel)
        }
    }
}
