import SwiftUI
import UIKit
import UserNotifications

/// Kurzbefehle beim langen Drücken auf das App-Symbol (wie die „shortcuts“ der PWA):
/// Heute, Mein Stundenplan, Mitteilungen, Plan suchen.
enum Schnellaktion: String, CaseIterable {
    case heute
    case stundenplan
    case mitteilungen
    case suche

    /// Kennung des Kurzbefehls (UIApplicationShortcutItem.type).
    var typ: String { "de.untplan.mobile." + rawValue }

    var titel: String {
        switch self {
        case .heute: return "Heute"
        case .stundenplan: return "Mein Stundenplan"
        case .mitteilungen: return "Mitteilungen"
        case .suche: return "Plan suchen"
        }
    }

    var untertitel: String? {
        switch self {
        case .stundenplan: return "Heutige Woche"
        case .suche: return "Klasse, Lehrkraft oder Raum"
        default: return nil
        }
    }

    var symbol: String {
        switch self {
        case .heute: return "building.columns"
        case .stundenplan: return "calendar"
        case .mitteilungen: return "envelope"
        case .suche: return "magnifyingglass"
        }
    }

    var eintrag: UIApplicationShortcutItem {
        UIApplicationShortcutItem(type: typ, localizedTitle: titel, localizedSubtitle: untertitel,
                                  icon: UIApplicationShortcutIcon(systemImageName: symbol), userInfo: nil)
    }

    /// Meldung an die Oberfläche, dass ein Kurzbefehl gewählt wurde.
    static let benachrichtigung = Notification.Name("UntPlanSchnellaktion")

    /// Noch nicht ausgeführter Kurzbefehl (z. B. beim Kaltstart, bevor die Oberfläche bereit ist).
    static var ausstehend: Schnellaktion? = nil

    /// Kurzbefehl merken und die Oberfläche benachrichtigen.
    @discardableResult
    static func ausloesen(_ item: UIApplicationShortcutItem) -> Bool {
        guard let aktion = allCases.first(where: { $0.typ == item.type }) else { return false }
        ausstehend = aktion
        NotificationCenter.default.post(name: benachrichtigung, object: nil)
        return true
    }
}

/// App-Delegate: trägt die Kurzbefehle ein und reicht sie beim Kaltstart an die Szene weiter.
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        application.shortcutItems = Schnellaktion.allCases.map(\.eintrag)
        // Mitteilungen bei Planänderungen (Lehrkräfte): Antippen öffnet den Tag; Hintergrund-Prüfung registrieren
        UNUserNotificationCenter.current().delegate = self
        Planmitteilungen.registrieren()
        return true
    }

    func application(_ application: UIApplication, configurationForConnecting connectingSceneSession: UISceneSession,
                     options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        if let item = options.shortcutItem {
            Schnellaktion.ausloesen(item)
        }
        let konfiguration = UISceneConfiguration(name: nil, sessionRole: connectingSceneSession.role)
        konfiguration.delegateClass = SzenenDelegate.self
        return konfiguration
    }
}

/// Szenen-Delegate: Kurzbefehl bei laufender App.
final class SzenenDelegate: NSObject, UIWindowSceneDelegate {
    func windowScene(_ windowScene: UIWindowScene, performActionFor shortcutItem: UIApplicationShortcutItem,
                     completionHandler: @escaping (Bool) -> Void) {
        completionHandler(Schnellaktion.ausloesen(shortcutItem))
    }
}
