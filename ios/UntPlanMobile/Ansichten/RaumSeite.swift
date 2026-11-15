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
                // gleiche Größen wie die Raumsuche der Planauswahl: Suchfeld 44 pt, Filter 42 pt, Zeilen mit 44-pt-Scheibe
                Elementsuche(text: $suche)
                    .padding(.top, 4)
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
                        // wie der Filter „Aktuelles Schuljahr“ der Raumsuche: Kapsel 42 pt, 15 pt, Pfeil (gewählt: orange gefüllt)
                        HStack(spacing: 7) {
                            Text(abteilung.isEmpty ? "Abteilung" : abteilung)
                                .font(.system(size: 15))
                                .lineLimit(1)
                            Image(systemName: "chevron.down")
                                .font(.system(size: 11, weight: .semibold))
                        }
                        .foregroundStyle(abteilung.isEmpty ? Auswahlmass.filterText : Color.white)
                        .padding(.leading, 16)
                        .padding(.trailing, 18)
                        .frame(height: Auswahlmass.filterHoehe)
                        .background(abteilung.isEmpty ? Auswahlmass.filterFlaeche : Farben.akzent, in: Capsule())
                        .overlay(Capsule().strokeBorder(abteilung.isEmpty ? Auswahlmass.filterRand : Color.clear, lineWidth: 1))
                        .contentShape(Capsule())
                    }
                    .tint(Color.primary)
                    .disabled(abteilungen.isEmpty)
                    Spacer(minLength: 0)
                }
                .padding(.horizontal, 16)
                .padding(.top, 16)
                .padding(.bottom, 10)
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

    /// Zeile wie in der Raumsuche der Planauswahl: graue Scheibe 44 pt mit weißem Raumsymbol 24 pt, Kürzel 20 pt, darunter der
    /// Raumname als graues Badge (sofern er vom Kürzel abweicht); Trennlinie ab dem Text.
    private func zeile(_ r: FreierRaum) -> some View {
        HStack(spacing: 8) {
            Image("SymbolRaum")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: 24, height: 24)
                .foregroundStyle(.white)
                .frame(width: Auswahlmass.avatar, height: Auswahlmass.avatar)
                .background(Auswahlmass.avatarFlaeche, in: Circle())
            VStack(alignment: .leading, spacing: 3) {
                Text(r.kurz)
                    .font(.system(size: 20))
                    .foregroundStyle(Auswahlmass.name)
                    .lineLimit(1)
                let badge = r.urspruenglich ? "ursprünglicher Raum" : (r.name.isEmpty || r.name.caseInsensitiveCompare(r.kurz) == .orderedSame ? "" : r.name)
                if !badge.isEmpty {
                    Text(badge)
                        .font(.system(size: 13))
                        .foregroundStyle(Auswahlmass.badgeText)
                        .lineLimit(1)
                        .padding(.horizontal, 8)
                        .frame(height: 19.3)
                        .background(Auswahlmass.badgeFlaeche, in: Capsule())
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if bucht == r.kurz {
                ProgressView()
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .frame(minHeight: 60)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Farben.trennlinie).frame(height: 0.5).padding(.leading, 68).padding(.trailing, 16)
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
