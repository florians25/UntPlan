import SwiftUI

/// Anmeldung mit Server-Adresse, Benutzername und Passwort. Als „Konto hinzufügen“ (eingebettet in die Kontenauswahl)
/// bleibt das bisherige Konto angemeldet; in der Anmeldemaske stehen die übrigen gespeicherten Konten zum Wechsel bereit.
struct AnmeldungView: View {
    /// Weiteres Konto hinzufügen (ohne eigenen NavigationStack, Server vorbelegt, Benutzer leer)
    var hinzufuegen: Bool = false
    /// Nach erfolgreicher Anmeldung (z. B. Kontenauswahl schließen)
    var fertig: (() -> Void)? = nil
    @EnvironmentObject var zustand: AppZustand
    @State private var server = UserDefaults.standard.string(forKey: ApiClient.adresseSchluessel) ?? ""
    @State private var login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
    @State private var passwort = ""
    @State private var merken = UserDefaults.standard.object(forKey: ApiClient.merkenSchluessel) as? Bool ?? true
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var wechselt: String? = nil
    @FocusState var fokus: Feld?

    enum Feld {
        case server
        case login
        case passwort
    }

    private var bereit: Bool {
        !server.trimmingCharacters(in: .whitespaces).isEmpty && !login.trimmingCharacters(in: .whitespaces).isEmpty && !passwort.isEmpty
    }

    var body: some View {
        if hinzufuegen {
            formular
                .navigationTitle("Konto hinzufügen")
                .navigationBarTitleDisplayMode(.inline)
                .onAppear {
                    // Weiteres Konto: gleicher Server vorbelegt, Benutzer und Passwort leer
                    login = ""
                    passwort = ""
                    merken = true
                }
        } else {
            NavigationStack {
                formular
                    .navigationBarTitleDisplayMode(.inline)
            }
        }
    }

    /// Gespeicherte Konten außer dem vorbelegten (Wechsel ohne erneute Anmeldung, sofern die Sitzung noch gilt).
    private var andereKonten: [Konto] {
        zustand.konten.filter { !$0.gleich(server: ApiClient.adresseNormalisieren(server), login: login.trimmingCharacters(in: .whitespaces)) }
    }

    private var formular: some View {
            Form {
                if !hinzufuegen {
                Section {
                    VStack(spacing: 10) {
                        Image(systemName: "calendar")
                            .font(.system(size: 34, weight: .medium))
                            .foregroundStyle(.white)
                            .frame(width: 72, height: 72)
                            .background(Farben.akzent, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                        Text("UntPlan")
                            .font(.largeTitle.weight(.medium))
                        Text("Stundenplan, Vertretungen und Mitteilungen Ihrer Schule")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                    .listRowBackground(Color.clear)
                }
                }

                if !hinzufuegen && !andereKonten.isEmpty {
                    Section {
                        ForEach(andereKonten) { konto in
                            Button {
                                Task {
                                    wechselt = konto.id
                                    await zustand.wechseln(zu: konto)
                                    wechselt = nil
                                }
                            } label: {
                                HStack(spacing: 12) {
                                    Initialen(name: konto.name, groesse: 34)
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(konto.rollenname.isEmpty ? konto.login : "\(konto.login) · \(konto.rollenname)")
                                            .foregroundStyle(Farben.tinte)
                                        Text(konto.schulname.isEmpty ? konto.serverKurz : konto.schulname)
                                            .font(.footnote)
                                            .foregroundStyle(.secondary)
                                    }
                                    Spacer()
                                    if wechselt == konto.id { ProgressView() }
                                }
                            }
                            .disabled(wechselt != nil)
                        }
                    } header: {
                        Text("Gespeicherte Konten")
                    }
                }

                Section {
                    TextField("plan.meine-schule.de", text: $server)
                        .keyboardType(.URL)
                        .textContentType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($fokus, equals: .server)
                        .submitLabel(.next)
                        .onSubmit { fokus = .login }
                } header: {
                    Text("Server-Adresse")
                } footer: {
                    Text("Die Adresse von UntPlanWeb Ihrer Schule.")
                }

                Section("Anmeldung") {
                    TextField("Benutzername", text: $login)
                        .textContentType(.username)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($fokus, equals: .login)
                        .submitLabel(.next)
                        .onSubmit { fokus = .passwort }
                    SecureField("Passwort", text: $passwort)
                        .textContentType(.password)
                        .focused($fokus, equals: .passwort)
                        .submitLabel(.go)
                        .onSubmit { Task { await anmelden() } }
                    Toggle("Passwort merken", isOn: $merken)
                        .tint(Farben.akzent)
                }

                Section {
                    Button {
                        Task { await anmelden() }
                    } label: {
                        HStack {
                            Spacer()
                            if laedt {
                                ProgressView()
                                    .tint(.white)
                            } else {
                                Text("Anmelden")
                                    .fontWeight(.medium)
                            }
                            Spacer()
                        }
                        .padding(.vertical, 4)
                        .foregroundStyle(.white)
                    }
                    .listRowBackground(bereit ? Farben.akzent : Farben.akzent.opacity(0.45))
                    .disabled(!bereit || laedt)
                } footer: {
                    if let fehler {
                        Text(fehler)
                            .foregroundStyle(Farben.entfallRand)
                            .font(.footnote)
                    } else if hinzufuegen {
                        Text("Das bisherige Konto bleibt angemeldet; Sie können jederzeit über den Profilwechsel oben links zurückwechseln.")
                            .font(.footnote)
                    }
                }
            }
    }

    private func anmelden() async {
        guard bereit, !laedt else { return }
        laedt = true
        fehler = nil
        defer { laedt = false }
        do {
            try await zustand.anmelden(server: server, login: login, passwort: passwort, merken: merken)
            passwort = ""
            fertig?()
        } catch {
            fehler = error.localizedDescription
        }
    }
}
