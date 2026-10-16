import SwiftUI

/// Details einer Stunde (Sheet beim Antippen einer Karte).
struct StundendetailView: View {
    let karte: Karte
    let typ: String
    @Environment(\.dismiss) private var schliessen

    var body: some View {
        let stil = Kartenstil(karte)
        NavigationStack {
            List {
                Section {
                    kopf(stil)
                }
                Section("Zeit") {
                    LabeledContent("Datum", value: Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy"))
                    LabeledContent("Stunde", value: stundentext)
                    if !karte.beginn.isEmpty {
                        LabeledContent("Uhrzeit", value: "\(karte.beginn) – \(karte.ende)")
                    }
                }
                Section("Unterricht") {
                    if !karte.lehrer.isEmpty {
                        LabeledContent((karte.lehrer.count > 1 ? "Lehrkräfte" : "Lehrkraft") as String) { werteText(karte.lehrer, trenner: " | ") }
                    }
                    if !karte.klassen.isEmpty {
                        LabeledContent((karte.klassen.count > 1 ? "Klassen" : "Klasse") as String) { werteText(karte.klassen, trenner: " | ") }
                    }
                    if !karte.raeume.isEmpty {
                        LabeledContent((karte.raeume.count > 1 ? "Räume" : "Raum") as String) { werteText(karte.raeume, trenner: " | ") }
                    }
                    if !karte.gruppe.isEmpty {
                        LabeledContent("Gruppe", value: karte.gruppe)
                    }
                    if !karte.art.isEmpty {
                        LabeledContent("Art", value: karte.art)
                    }
                }
                if !karte.abwesendeKlassen.isEmpty {
                    Section("Abwesend") {
                        Text(karte.abwesendeKlassen.joined(separator: ", "))
                            .strikethrough(true, color: Farben.entfallRand)
                        if !karte.abwesendText.isEmpty {
                            Text(karte.abwesendText)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                if !karte.text.isEmpty {
                    Section("Hinweis") {
                        Text(karte.text)
                    }
                }
                if karte.hausaufgabeFaellig {
                    Section {
                        Label("Zu dieser Stunde ist eine Hausaufgabe fällig.", systemImage: "book.closed.fill")
                    }
                }
                if !karte.notizSchueler.isEmpty {
                    Section("Notiz") {
                        Text(karte.notizSchueler)
                    }
                }
                if !karte.notizLehrer.isEmpty {
                    Section("Notiz für Lehrkräfte") {
                        Text(karte.notizLehrer)
                    }
                }
            }
            .navigationTitle("Stunde")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { schliessen() }
                }
            }
        }
    }

    private var stundentext: String {
        karte.von == karte.bis ? "\(karte.von). Stunde" : "\(karte.von).–\(karte.bis). Stunde"
    }

    private func kopf(_ stil: Kartenstil) -> some View {
        HStack(alignment: .top, spacing: 12) {
            RoundedRectangle(cornerRadius: 2)
                .fill(Farben.fach(karte.farbe))
                .frame(width: 5, height: 48)
            VStack(alignment: .leading, spacing: 4) {
                Text(karte.titel)
                    .font(.title3.weight(.bold))
                    .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                if !karte.fach.isEmpty && karte.fach != karte.titel {
                    Text(karte.fach)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach {
                    Text("statt \(alt)")
                        .font(.subheadline)
                        .strikethrough(true, color: Farben.entfallRand)
                        .foregroundStyle(.secondary)
                }
                HStack(spacing: 6) {
                    if let p = stil.plakette {
                        Plakette(text: p, farbe: stil.plaketteFarbe)
                    }
                    if karte.zusaetzlich && stil.plakette != "Zusätzlich" {
                        Plakette(text: "Zusätzlicher Unterricht", farbe: Farben.gruen)
                    }
                }
            }
        }
        .padding(.vertical, 4)
    }
}
