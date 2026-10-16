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
/// Ab iOS 26 die Systemleiste (Liquid Glass), davor eine eigene schwebende Glaskapsel.
@MainActor
struct AppTabView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var reiter: AppReiter = .start

    var body: some View {
        if #available(iOS 26.0, *) {
            systemLeiste
        } else {
            eigeneLeiste
        }
    }

    // MARK: - iOS 26: Systemleiste

    private var systemLeiste: some View {
        TabView(selection: $reiter) {
            StartView()
                .tint(Farben.akzent)
                .tabItem { Label(AppReiter.start.titel, systemImage: AppReiter.start.symbol) }
                .tag(AppReiter.start)
            StundenplanView()
                .tint(Farben.akzent)
                .tabItem {
                    Label {
                        Text(AppReiter.stundenplan.titel)
                    } icon: {
                        Leistensymbole.kalender(tag: Leistensymbole.heutigerTag)
                    }
                }
                .tag(AppReiter.stundenplan)
            MitteilungenView()
                .tint(Farben.akzent)
                .tabItem { Label(AppReiter.mitteilungen.titel, systemImage: AppReiter.mitteilungen.symbol) }
                .badge(zustand.ungelesen)
                .tag(AppReiter.mitteilungen)
            ProfilView()
                .tint(Farben.akzent)
                .tabItem { Label(AppReiter.profil.titel, systemImage: AppReiter.profil.symbol) }
                .tag(AppReiter.profil)
        }
        // aktiver Reiter dunkel bzw. weiß wie in der Vorlage
        .tint(Farben.leisteAktiv)
    }

    // MARK: - Bis iOS 18: eigene Glaskapsel

    private var eigeneLeiste: some View {
        GeometryReader { geo in
            // Bei eingeblendeter Tastatur wächst der Abstand unten stark: dann Leiste ausblenden
            let tastatur = geo.safeAreaInsets.bottom > 120
            let unten = tastatur ? 0 : geo.safeAreaInsets.bottom
            // wie .tabbar: max(10px, Sicherheitsabstand - 14px) über dem Rand
            let abstand = max(10, unten - 14)
            let einzug = tastatur ? 0 : max(0, abstand + GlasTabLeiste.hoehe - unten + 8)
            TabView(selection: $reiter) {
                StartView()
                    .ohneSystemleiste(einzug: einzug)
                    .tag(AppReiter.start)
                StundenplanView()
                    .ohneSystemleiste(einzug: einzug)
                    .tag(AppReiter.stundenplan)
                MitteilungenView()
                    .ohneSystemleiste(einzug: einzug)
                    .tag(AppReiter.mitteilungen)
                ProfilView()
                    .ohneSystemleiste(einzug: einzug)
                    .tag(AppReiter.profil)
            }
            .overlay(alignment: .bottom) {
                GlasTabLeiste(auswahl: $reiter, ungelesen: zustand.ungelesen)
                    .padding(.bottom, abstand)
                    .frame(maxHeight: .infinity, alignment: .bottom)
                    .ignoresSafeArea(.container, edges: .bottom)
                    .opacity(tastatur ? 0 : 1)
                    .allowsHitTesting(!tastatur)
            }
        }
    }
}

private extension View {
    /// Systemleiste ausblenden und unten Platz für die schwebende Kapsel lassen.
    func ohneSystemleiste(einzug: CGFloat) -> some View {
        self
            .safeAreaInset(edge: .bottom, spacing: 0) {
                Color.clear.frame(height: einzug)
            }
            .toolbar(.hidden, for: .tabBar)
    }
}
