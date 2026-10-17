import SwiftUI

/// Wurzelansicht: Anmeldung oder die App mit unterer Tab-Leiste.
@MainActor
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
                // Neuer Aufbau je Konto: beim Kontowechsel starten alle Reiter frisch (keine Daten des vorigen Kontos)
                AppTabView()
                    .id(zustand.kontoStand)
            }
        }
        .task {
            if zustand.phase == .startet { await zustand.starten() }
        }
        .onChange(of: szene) { _, neu in
            if neu == .active && zustand.phase == .angemeldet {
                Task {
                    await zustand.aktualisieren()
                    // Lehrkräfte: eigenen Plan auf Änderungen prüfen (Mitteilung bei Entfall, Vertretung, Raumänderung …)
                    await zustand.planPruefen()
                }
            }
            if neu == .background && zustand.darfPlanmitteilungen {
                Planmitteilungen.planen()
            }
        }
        // Eigene Änderung in der App (z. B. Raum in den Stundendetails ersetzt): Stand übernehmen, nicht melden
        // Kontowechsel: Plan des neuen Kontos prüfen (eigener Stand je Konto)
        .onChange(of: zustand.kontoStand) { _, _ in
            guard zustand.phase == .angemeldet else { return }
            Task { await zustand.planPruefen() }
        }
        .onChange(of: zustand.planGeaendert) { _, _ in
            guard zustand.darfPlanmitteilungen else { return }
            Task { _ = await Planpruefer.shared.pruefen(melden: false, erzwingen: true) }
        }
        // Angetippte Mitteilung über eine Planänderung: Tag im eigenen Plan öffnen
        .onReceive(NotificationCenter.default.publisher(for: Planmitteilungen.tippBenachrichtigung)) { _ in
            tagOeffnen()
        }
        // Kurzbefehle am App-Symbol: sofort bzw. nach der Anmeldung ausführen
        .onReceive(NotificationCenter.default.publisher(for: Schnellaktion.benachrichtigung)) { _ in
            schnellaktionAusfuehren()
        }
        .onChange(of: zustand.phase) { _, neu in
            schnellaktionAusfuehren()
            tagOeffnen()
            // Nach der Anmeldung bzw. beim Start einmal den eigenen Plan prüfen (Lehrkräfte) und das Gerät für
            // Push-Mitteilungen beim Server hinterlegen
            if neu == .angemeldet {
                zustand.pushTokenMelden()
                Task { await zustand.planPruefen() }
            }
        }
        .onAppear {
            schnellaktionAusfuehren()
            tagOeffnen()
        }
    }

    private func tagOeffnen() {
        guard zustand.phase == .angemeldet, let tag = Planmitteilungen.ausstehenderTag else { return }
        Planmitteilungen.ausstehenderTag = nil
        zustand.tagAnfrage = tag
        zustand.reiter = .stundenplan
    }

    private func schnellaktionAusfuehren() {
        guard zustand.phase == .angemeldet, let aktion = Schnellaktion.ausstehend else { return }
        Schnellaktion.ausstehend = nil
        switch aktion {
        case .heute:
            zustand.reiter = .start
        case .mitteilungen:
            zustand.reiter = .mitteilungen
        case .stundenplan, .suche:
            zustand.schnellaktion = aktion
            zustand.reiter = .stundenplan
        }
    }
}

/// Tab-Leiste: Start, Stundenplan, Mitteilungen, Profil.
/// Ab iOS 26 die Systemleiste (Liquid Glass; auf dem iPad schwebend oben, auf dem iPhone unten),
/// davor eine eigene schwebende Glaskapsel – auf dem iPad oben mittig, auf dem iPhone unten.
@MainActor
struct AppTabView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.horizontalSizeClass) private var groessenklasse
    /// Bereits geöffnete Reiter (eigene Leiste: erst beim ersten Öffnen laden, danach Zustand behalten)
    @State private var besucht: Set<AppReiter> = []

    private var istBreit: Bool { groessenklasse == .regular }

    init() {
        // Systemleiste (iOS 26): kräftigere Beschriftung – muss vor dem Anlegen der Leiste gesetzt sein
        if #available(iOS 26.0, *) {
            Leistenaussehen.anwenden()
        }
    }

    /// Auswahl des Reiters über AppZustand: erneutes Antippen von „Stundenplan“ führt zur heutigen Woche zurück.
    private var reiterBindung: Binding<AppReiter> {
        Binding(get: { zustand.reiter }, set: { zustand.reiterWaehlen($0) })
    }

    var body: some View {
        if #available(iOS 26.0, *) {
            systemLeiste
        } else {
            eigeneLeiste
        }
    }

    // MARK: - iOS 26: Systemleiste

    private var systemLeiste: some View {
        TabView(selection: reiterBindung) {
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
        // iPad: aktiver Reiter orange wie in der Vorlage; iPhone: dunkel bzw. weiß
        .tint(istBreit ? Farben.akzent : Farben.leisteAktiv)
    }

    // MARK: - Bis iOS 18: eigene Glaskapsel

    @ViewBuilder
    private func inhalt(_ reiter: AppReiter) -> some View {
        switch reiter {
        case .start: StartView()
        case .stundenplan: StundenplanView()
        case .mitteilungen: MitteilungenView()
        case .profil: ProfilView()
        }
    }

    /// Alle geöffneten Reiter liegen übereinander, sichtbar ist nur der gewählte (Zustand bleibt erhalten).
    private var reiterStapel: some View {
        ZStack {
            ForEach(AppReiter.allCases, id: \.self) { reiter in
                let aktiv = zustand.reiter == reiter
                if aktiv || besucht.contains(reiter) {
                    inhalt(reiter)
                        .opacity(aktiv ? 1 : 0)
                        .allowsHitTesting(aktiv)
                        .accessibilityHidden(!aktiv)
                        .zIndex(aktiv ? 1 : 0)
                }
            }
        }
        .onAppear { besucht.insert(zustand.reiter) }
        .onChange(of: zustand.reiter) { _, neu in besucht.insert(neu) }
    }

    @ViewBuilder
    private var eigeneLeiste: some View {
        if istBreit {
            // iPad: Kapsel oben mittig in der Zeile der Navigationsleiste (links Konto, rechts Knöpfe)
            reiterStapel
                .overlay(alignment: .top) {
                    KopfTabLeiste(auswahl: reiterBindung, ungelesen: zustand.ungelesen)
                        .padding(.top, 5)
                }
        } else {
            untereLeiste
        }
    }

    private var untereLeiste: some View {
        GeometryReader { geo in
            // Bei eingeblendeter Tastatur wächst der Abstand unten stark: dann Leiste ausblenden
            let tastatur = geo.safeAreaInsets.bottom > 120
            let unten = tastatur ? 0 : geo.safeAreaInsets.bottom
            // wie .tabbar: max(10px, Sicherheitsabstand - 14px) über dem Rand
            let abstand = max(10, unten - 14)
            let einzug = tastatur ? 0 : max(0, abstand + GlasTabLeiste.hoehe - unten + 8)
            reiterStapel
                .safeAreaInset(edge: .bottom, spacing: 0) {
                    Color.clear.frame(height: einzug)
                }
                .overlay(alignment: .bottom) {
                    GlasTabLeiste(auswahl: reiterBindung, ungelesen: zustand.ungelesen)
                        .padding(.bottom, abstand)
                        .frame(maxHeight: .infinity, alignment: .bottom)
                        .ignoresSafeArea(.container, edges: .bottom)
                        .opacity(tastatur ? 0 : 1)
                        .allowsHitTesting(!tastatur)
                }
        }
    }
}
