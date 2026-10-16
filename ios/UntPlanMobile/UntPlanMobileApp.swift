import SwiftUI

/// UntPlan für iOS – spricht direkt die JSON-API von UntPlanWeb an.
@main
struct UntPlanMobileApp: App {
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
