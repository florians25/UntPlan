import SwiftUI

/// Profil: Konto, Server, Kalender-Abo und Abmelden.
struct ProfilView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var profil: Profil? = nil
    @State private var fehler: String? = nil
    @State private var bestaetigen = false

    private var name: String {
        if let p = profil, !p.anzeigename.isEmpty { return p.anzeigename }
        return zustand.benutzer?.name ?? ""
    }

    var body: some View {
        NavigationStack {
            List {
                Section {
                    HStack(spacing: 14) {
                        Initialen(name: name, groesse: 56)
                        VStack(alignment: .leading, spacing: 3) {
                            Text(name)
                                .font(.title3.weight(.bold))
                            Text(profil?.rolle ?? zustand.benutzer?.rollenname ?? "")
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                    }
                    .padding(.vertical, 6)
                }

                if let fehler {
                    Fehlerhinweis(text: fehler)
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                }

                Section("Konto") {
                    LabeledContent("Benutzer", value: profil?.login ?? zustand.benutzer?.login ?? "")
                    let element = profil?.element ?? zustand.benutzer?.element ?? ""
                    if !element.isEmpty {
                        LabeledContent("Plan", value: element)
                    }
                    if let email = profil?.email, !email.isEmpty {
                        LabeledContent("E-Mail", value: email)
                    }
                    if let letzte = profil?.letzteAnmeldung, !letzte.isEmpty {
                        LabeledContent("Letzte Anmeldung", value: Datumsformat.anzeige(letzte, "d.M.yyyy, HH:mm"))
                    }
                    if let vorherige = profil?.vorherigeAnmeldung, !vorherige.isEmpty {
                        LabeledContent("Davor", value: Datumsformat.anzeige(vorherige, "d.M.yyyy, HH:mm"))
                    }
                }

                Section("Schule") {
                    LabeledContent("Name", value: zustand.schulname)
                    LabeledContent("Server", value: zustand.api.serverAdresse)
                    if let sj = zustand.sitzung?.schuljahr {
                        LabeledContent("Schuljahr", value: "\(Datumsformat.anzeige(sj.von, "d.M.yyyy")) – \(Datumsformat.anzeige(sj.bis, "d.M.yyyy"))")
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

                Section {
                    Button(role: .destructive) {
                        bestaetigen = true
                    } label: {
                        HStack {
                            Spacer()
                            Text("Abmelden")
                                .fontWeight(.semibold)
                            Spacer()
                        }
                    }
                } footer: {
                    Text("UntPlan für iOS \(Self.version)")
                        .frame(maxWidth: .infinity)
                        .padding(.top, 8)
                }
            }
            .navigationTitle("Profil")
            .refreshable { await laden() }
            .task { await laden() }
            .confirmationDialog("Wirklich abmelden?", isPresented: $bestaetigen, titleVisibility: .visible) {
                Button("Abmelden", role: .destructive) {
                    Task { await zustand.abmelden() }
                }
                Button("Abbrechen", role: .cancel) {}
            } message: {
                Text("Ein gemerktes Passwort wird vom Gerät entfernt.")
            }
        }
    }

    private static var version: String {
        let info = Bundle.main.infoDictionary
        let v = info?["CFBundleShortVersionString"] as? String ?? "1.0"
        let b = info?["CFBundleVersion"] as? String ?? "1"
        return "\(v) (\(b))"
    }

    private func laden() async {
        do {
            profil = try await zustand.api.profil()
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}
