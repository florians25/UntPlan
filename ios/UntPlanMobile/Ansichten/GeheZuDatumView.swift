import SwiftUI

/// „Gehe zu Datum“ als Popover am ⋮-Knopf (Vorlage): oben links „Heute“, rechts „Auswählen“, darunter der Monatskalender
/// mit Mo–So; heute orange, der gewählte Tag orange im hellen Kreis.
struct GeheZuDatumView: View {
    let start: Date
    let auswaehlen: (Date) -> Void
    @State private var wahl = Date()

    private var kalender: Calendar { Datumsformat.kalender }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button("Heute") {
                    auswaehlen(Date())
                }
                .font(.system(size: 17, weight: .regular))
                .foregroundStyle(Farben.tinte)
                Spacer(minLength: 20)
                Button("Auswählen") {
                    auswaehlen(wahl)
                }
                .font(.system(size: 17, weight: .medium))
                .foregroundStyle(Farben.tinte)
            }
            .padding(.horizontal, 20)
            .padding(.top, 16)
            .padding(.bottom, 4)
            DatePicker("Datum", selection: $wahl, displayedComponents: .date)
                .datePickerStyle(.graphical)
                .labelsHidden()
                .tint(Farben.akzent)
                .environment(\.locale, Locale(identifier: "de_DE"))
                .environment(\.calendar, kalender)
                .padding(.horizontal, 8)
                .padding(.bottom, 8)
        }
        .frame(width: 340)
        .onAppear { wahl = start }
    }
}
