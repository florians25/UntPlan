import SwiftUI

/// UntPlan für iOS – spricht direkt die JSON-API von UntPlanWeb an.
@main
struct UntPlanMobileApp: App {
    /// Kurzbefehle am App-Symbol (Heute, Stundenplan, Mitteilungen, Plan suchen)
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var zustand = AppZustand()
    @StateObject private var favoriten = Favoriten()

    var body: some Scene {
        WindowGroup {
            HauptView()
                .environmentObject(zustand)
                .environmentObject(favoriten)
                .tint(Farben.akzent)
        }
    }
}
