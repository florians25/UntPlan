import Foundation
import SwiftUI

/// Gewählter Plan (Klasse, Lehrkraft oder Raum); nil = eigener Plan.
struct PlanAuswahl: Hashable, Codable, Identifiable {
    var typ: String
    var element: String
    var name: String

    var id: String { "\(typ)|\(element)" }

    /// Kurzer Anzeigename (Kürzel), z. B. „FIS4A“.
    var anzeige: String { element.isEmpty ? name : element }

    /// Vektorgrafik je Planart (Assets.xcassets, als Vorlage eingefärbt).
    var bild: String { PlanAuswahl.bild(typ) }

    static func bild(_ typ: String) -> String {
        switch typ {
        case "lehrer": return "SymbolLehrer"
        case "raum": return "SymbolRaum"
        case "schueler": return "SymbolSchuelerPlan"
        default: return "SymbolKlasse"
        }
    }
}

/// Favorisierte Pläne, lokal in den UserDefaults gespeichert (Reihenfolge wie vom Benutzer sortiert).
@MainActor
final class Favoriten: ObservableObject {
    nonisolated static let schluessel = "planFavoriten"

    @Published private(set) var liste: [PlanAuswahl]

    nonisolated init() {
        _liste = Published(initialValue: Favoriten.gespeichert())
    }

    /// Liest die gespeicherten Favoriten.
    nonisolated static func gespeichert() -> [PlanAuswahl] {
        guard let daten = UserDefaults.standard.data(forKey: schluessel),
              let gelesen = try? JSONDecoder().decode([PlanAuswahl].self, from: daten) else { return [] }
        return gelesen
    }

    func enthaelt(_ plan: PlanAuswahl) -> Bool {
        liste.contains { $0.id == plan.id }
    }

    func enthaelt(typ: String, element: String) -> Bool {
        liste.contains { $0.typ == typ && $0.element == element }
    }

    /// Stern antippen: hinzufügen oder entfernen.
    func umschalten(_ plan: PlanAuswahl) {
        if let i = liste.firstIndex(where: { $0.id == plan.id }) {
            liste.remove(at: i)
        } else {
            liste.append(plan)
        }
        speichern()
    }

    func entfernen(_ indizes: IndexSet) {
        liste.remove(atOffsets: indizes)
        speichern()
    }

    func verschieben(_ von: IndexSet, nach: Int) {
        liste.move(fromOffsets: von, toOffset: nach)
        speichern()
    }

    private func speichern() {
        if let daten = try? JSONEncoder().encode(liste) {
            UserDefaults.standard.set(daten, forKey: Self.schluessel)
        }
    }
}
