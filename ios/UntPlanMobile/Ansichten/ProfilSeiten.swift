import SwiftUI

// MARK: - Kontaktdaten

/// Kontaktdaten und Konto (Benutzer, E-Mail, Plan, Anmeldungen) sowie das Kalender-Abo.
struct KontaktdatenView: View {
    let profil: Profil?
    let name: String
    @EnvironmentObject var zustand: AppZustand

    var body: some View {
        List {
            Section("Konto") {
                if !name.isEmpty {
                    LabeledContent("Name", value: name)
                }
                LabeledContent("Benutzer", value: profil?.login ?? zustand.benutzer?.login ?? "")
                let rolle = zustand.benutzer?.rollenname ?? profil?.rolle ?? ""
                if !rolle.isEmpty {
                    LabeledContent("Rolle", value: rolle)
                }
                let element = profil?.element ?? zustand.benutzer?.element ?? ""
                if !element.isEmpty {
                    LabeledContent("Plan", value: element)
                }
                if let email = profil?.email, !email.isEmpty {
                    LabeledContent("E-Mail", value: email)
                }
            }

            if profil?.letzteAnmeldung != nil || profil?.vorherigeAnmeldung != nil {
                Section("Anmeldungen") {
                    if let letzte = profil?.letzteAnmeldung, !letzte.isEmpty {
                        LabeledContent("Letzte Anmeldung", value: Datumsformat.anzeige(letzte, "d.M.yyyy, HH:mm"))
                    }
                    if let vorherige = profil?.vorherigeAnmeldung, !vorherige.isEmpty {
                        LabeledContent("Davor", value: Datumsformat.anzeige(vorherige, "d.M.yyyy, HH:mm"))
                    }
                }
            }

            if let ical = profil?.ical, let url = URL(string: ical) {
                Section {
                    if let abo = URL(string: ical.replacingOccurrences(of: "https://", with: "webcal://").replacingOccurrences(of: "http://", with: "webcal://")) {
                        Link(destination: abo) {
                            Label("Kalender abonnieren", systemImage: "calendar.badge.plus")
                        }
                    }
                    ShareLink(item: url) {
                        Label("Kalenderadresse teilen", systemImage: "square.and.arrow.up")
                    }
                } header: {
                    Text("Kalender")
                } footer: {
                    Text("Die Stunden der nächsten vier Wochen als Kalender-Abo (iCal).")
                }
            }
        }
        .navigationTitle("Kontaktdaten")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
    }
}

// MARK: - Passwort ändern

/// Passwort ändern über POST /api/app/passwort.
struct PasswortView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var zurueck
    @State private var alt = ""
    @State private var neu = ""
    @State private var wiederholung = ""
    @State private var speichert = false
    @State private var fehler: String? = nil
    @State private var erfolg = false

    private var bereit: Bool {
        !alt.isEmpty && !neu.isEmpty && neu == wiederholung && !speichert
    }

    var body: some View {
        Form {
            Section {
                SecureField("Bisheriges Passwort", text: $alt)
                    .textContentType(.password)
            }
            Section {
                SecureField("Neues Passwort", text: $neu)
                    .textContentType(.newPassword)
                SecureField("Neues Passwort wiederholen", text: $wiederholung)
                    .textContentType(.newPassword)
            } footer: {
                if !wiederholung.isEmpty && neu != wiederholung {
                    Text("Die neuen Passwörter stimmen nicht überein.")
                        .foregroundStyle(Farben.entfallRand)
                }
            }
            if let fehler {
                Section {
                    Text(fehler)
                        .foregroundStyle(Farben.entfallRand)
                }
            }
            Section {
                Button {
                    Task { await aendern() }
                } label: {
                    HStack {
                        Spacer()
                        if speichert {
                            ProgressView()
                        } else {
                            Text("Passwort ändern")
                                .fontWeight(.medium)
                        }
                        Spacer()
                    }
                }
                .disabled(!bereit)
            }
        }
        .navigationTitle("Passwort ändern")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .alert("Passwort geändert", isPresented: $erfolg) {
            Button("OK") { zurueck() }
        } message: {
            Text("Ihr neues Passwort gilt ab sofort.")
        }
    }

    private func aendern() async {
        speichert = true
        defer { speichert = false }
        do {
            try await zustand.api.passwortAendern(alt: alt, neu: neu)
            // Gemerktes Passwort im Schlüsselbund nachziehen
            let login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
            let konto = Schluesselbund.konto(login: login, server: zustand.api.serverAdresse)
            if !login.isEmpty, Schluesselbund.lesen(konto: konto) != nil {
                Schluesselbund.speichern(neu, konto: konto)
            }
            fehler = nil
            alt = ""
            neu = ""
            wiederholung = ""
            erfolg = true
        } catch {
            fehler = zustand.fehlertext(error)
        }
    }
}

// MARK: - Was gibt es Neues

struct NeuigkeitenView: View {
    private struct Punkt: Identifiable {
        let symbol: String
        let titel: String
        let text: String
        var id: String { titel }
    }

    private let punkte: [Punkt] = [
        Punkt(symbol: "rectangle.split.3x1", titel: "Wochen blättern", text: "Im Stundenplan folgt die Woche dem Finger und gleitet beim Loslassen zur nächsten bzw. vorherigen Woche."),
        Punkt(symbol: "rectangle.portrait", titel: "Tagesansicht", text: "Ein Tipp auf den Tageskopf zeigt den Tag über die volle Breite – Wischen wechselt zum Vor- bzw. Folgetag."),
        Punkt(symbol: "list.bullet.rectangle", titel: "Neue Stundendetails", text: "Lehrstoff, Hausaufgaben, Schüler*innen, Klassenbucheinträge und Notizen direkt aus der Stunde."),
        Punkt(symbol: "door.left.hand.open", titel: "Im Stundenplan anzeigen", text: "Aus den Stundendetails den Plan eines Raums, einer Klasse oder Lehrkraft öffnen."),
        Punkt(symbol: "ipad", titel: "Für das iPad", text: "Leiste oben in der Mitte, Inhalte mittig statt über die ganze Breite."),
    ]

    var body: some View {
        List {
            ForEach(punkte) { punkt in
                HStack(alignment: .top, spacing: 14) {
                    Image(systemName: punkt.symbol)
                        .font(.system(size: 20, weight: .light))
                        .foregroundStyle(Farben.akzent)
                        .frame(width: 30)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(punkt.titel)
                            .font(.system(size: 16, weight: .medium))
                            .foregroundStyle(Farben.tinte)
                        Text(punkt.text)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                .padding(.vertical, 4)
            }
        }
        .navigationTitle("Was gibt es Neues")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
    }
}

// MARK: - Über

/// Schule, Server, Schuljahr, Datenstand und App-Version.
struct UeberView: View {
    @EnvironmentObject var zustand: AppZustand

    var body: some View {
        List {
            Section("Schule") {
                LabeledContent("Name", value: zustand.schulname)
                LabeledContent("Server", value: zustand.api.serverAdresse)
                if let sj = zustand.sitzung?.schuljahr {
                    LabeledContent("Schuljahr", value: "\(Datumsformat.anzeige(sj.von, "d.M.yyyy")) – \(Datumsformat.anzeige(sj.bis, "d.M.yyyy"))")
                }
                if let empfangen = zustand.sitzung?.paket?.empfangen, !empfangen.isEmpty {
                    LabeledContent("Datenstand", value: Datumsformat.anzeige(empfangen, "d.M.yyyy, HH:mm"))
                }
            }
            Section("App") {
                LabeledContent("Version", value: ProfilView.version)
            }
        }
        .navigationTitle("Über")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
    }
}
