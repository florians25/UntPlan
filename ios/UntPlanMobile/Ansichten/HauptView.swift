import SwiftUI

/// Wurzelansicht: Anmeldung oder die App mit unterer Tab-Leiste.
struct HauptView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.scenePhase) private var szene

    var body: some View {
        Group {
            switch zustand.phase {
            case .startet:
                VStack(spacing: 12) {
                    ProgressView()
                    Text("Verbindung wird hergestellt …")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            case .abgemeldet:
                AnmeldungView()
            case .angemeldet:
                AppTabView()
            }
        }
        .task {
            if zustand.phase == .startet { await zustand.starten() }
        }
        .onChange(of: szene) { _, neu in
            if neu == .active && zustand.phase == .angemeldet {
                Task { await zustand.aktualisieren() }
            }
        }
    }
}

/// Untere Tab-Leiste: Start, Stundenplan, Mitteilungen, Profil.
struct AppTabView: View {
    enum Reiter: Hashable {
        case start
        case stundenplan
        case mitteilungen
        case profil
    }

    @EnvironmentObject var zustand: AppZustand
    @State private var reiter: Reiter = .start

    var body: some View {
        TabView(selection: $reiter) {
            StartView()
                .tabItem { Label("Start", systemImage: "house.fill") }
                .tag(Reiter.start)
            StundenplanView()
                .tabItem { Label("Stundenplan", systemImage: "calendar") }
                .tag(Reiter.stundenplan)
            MitteilungenView()
                .tabItem { Label("Mitteilungen", systemImage: "envelope.fill") }
                .badge(zustand.ungelesen)
                .tag(Reiter.mitteilungen)
            ProfilView()
                .tabItem { Label("Profil", systemImage: "person.crop.circle.fill") }
                .tag(Reiter.profil)
        }
        .tint(Farben.akzent)
    }
}
