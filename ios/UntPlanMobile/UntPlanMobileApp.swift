import SwiftUI

/// UntPlan für iOS – spricht direkt die JSON-API von UntPlanWeb an.
@main
struct UntPlanMobileApp: App {
    @StateObject private var zustand = AppZustand()

    var body: some Scene {
        WindowGroup {
            HauptView()
                .environmentObject(zustand)
                .tint(Farben.akzent)
        }
    }
}
