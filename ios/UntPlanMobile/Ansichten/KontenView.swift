import SwiftUI

/// Kontenauswahl (Profilwechsel oben links): alle angemeldeten Konten – auch verschiedener Server – mit Haken am aktiven.
/// Antippen wechselt ohne Abmelden (Sitzung und Passwort liegen je Konto im Schlüsselbund); „Konto hinzufügen“ meldet
/// ein weiteres Konto an, Wischen bzw. „Entfernen“ meldet ein Konto ab und löscht es vom Gerät.
struct KontenauswahlView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }
    @State private var entfernenKonto: Konto? = nil

    private var aktivId: String? { Kontenspeicher.aktivId }

    /// Aktives Konto zuerst, danach zuletzt genutzte.
    private var sortiert: [Konto] {
        zustand.konten.sorted { a, b in
            if a.id == aktivId { return true }
            if b.id == aktivId { return false }
            return (a.zuletzt ?? .distantPast) > (b.zuletzt ?? .distantPast)
        }
    }

    var body: some View {
        NavigationStack {
            // Kopf wie alle Blätter: runder Glasknopf X links, Titel mittig
            DetailSeitenRahmen(titel: "Konten", untertitel: nil, symbol: "xmark", zurueck: { schliessen() }) {
            List {
                Section {
                    ForEach(sortiert) { konto in
                        Button {
                            waehlen(konto)
                        } label: {
                            zeile(konto)
                        }
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                entfernenKonto = konto
                            } label: {
                                Label("Entfernen", systemImage: "trash")
                            }
                        }
                        .contextMenu {
                            Button(role: .destructive) {
                                entfernenKonto = konto
                            } label: {
                                Label(konto.id == aktivId ? "Abmelden und entfernen" : "Konto entfernen", systemImage: "trash")
                            }
                        }
                    }
                } header: {
                    Text("Angemeldete Konten")
                } footer: {
                    Text("Wechseln Sie ohne Abmelden zwischen Ihren Konten – auch an verschiedenen Schulen.")
                }

                Section {
                    NavigationLink {
                        AnmeldungView(hinzufuegen: true) { schliessen() }
                            .environmentObject(zustand)
                    } label: {
                        Label("Konto hinzufügen", systemImage: "person.badge.plus")
                            .foregroundStyle(Farben.tinte)
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Farben.grund)
            }
            .confirmationDialog(entfernenKonto.map { "\($0.login) entfernen?" } ?? "",
                                isPresented: Binding(get: { entfernenKonto != nil }, set: { if !$0 { entfernenKonto = nil } }),
                                titleVisibility: .visible) {
                if let konto = entfernenKonto {
                    Button(konto.id == aktivId ? "Abmelden und entfernen" : "Entfernen", role: .destructive) {
                        entfernen(konto)
                    }
                }
                Button("Abbrechen", role: .cancel) { entfernenKonto = nil }
            } message: {
                Text("Das Konto wird auf diesem Gerät abgemeldet; ein gemerktes Passwort wird gelöscht.")
            }
        }
        .tint(Farben.akzent)
    }

    private func zeile(_ konto: Konto) -> some View {
        HStack(spacing: 12) {
            Initialen(name: konto.name, groesse: 38)
            VStack(alignment: .leading, spacing: 2) {
                Text(konto.name)
                    .font(.system(size: 16, weight: konto.id == aktivId ? .medium : .regular))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                Text([konto.name != konto.login ? konto.login : "", konto.rollenname, konto.schulname.isEmpty ? konto.serverKurz : konto.schulname]
                    .filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                    .lineLimit(2)
            }
            Spacer(minLength: 6)
            if konto.id == aktivId {
                Image(systemName: "checkmark")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Farben.akzent)
                    .accessibilityLabel("Aktives Konto")
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
    }

    private func waehlen(_ konto: Konto) {
        guard konto.id != aktivId || zustand.phase != .angemeldet else {
            schliessen()
            return
        }
        schliessen()
        let z = zustand
        Task { await z.wechseln(zu: konto) }
    }

    private func entfernen(_ konto: Konto) {
        let aktiv = konto.id == aktivId
        entfernenKonto = nil
        if aktiv { schliessen() }
        let z = zustand
        Task { await z.entfernen(konto) }
    }
}
