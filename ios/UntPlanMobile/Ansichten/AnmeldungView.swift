import SwiftUI

/// Anmeldung mit Server-Adresse, Benutzername und Passwort.
struct AnmeldungView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var server = UserDefaults.standard.string(forKey: ApiClient.adresseSchluessel) ?? ""
    @State private var login = UserDefaults.standard.string(forKey: ApiClient.loginSchluessel) ?? ""
    @State private var passwort = ""
    @State private var merken = UserDefaults.standard.object(forKey: ApiClient.merkenSchluessel) as? Bool ?? true
    @State private var laedt = false
    @State private var fehler: String? = nil
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
        NavigationStack {
            Form {
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
                    }
                }
            }
            .navigationBarTitleDisplayMode(.inline)
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
        } catch {
            fehler = error.localizedDescription
        }
    }
}
