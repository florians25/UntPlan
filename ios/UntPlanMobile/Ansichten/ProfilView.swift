import SwiftUI

/// Unterseiten des Profils.
enum Profilseite: Hashable {
    case kontaktdaten
    case passwort
    case neues
    case ueber
}

/// Profil nach Vorlage: großer Titel (Kürzel bzw. Plan), Kontokarte mit Konto-Wechsel,
/// Gruppen Kontaktdaten/Passwort, Neues/Über und Abmelden. Auf dem iPad mittig, höchstens 700 pt breit.
struct ProfilView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var profil: Profil? = nil
    @State private var fehler: String? = nil
    @State private var bestaetigen = false

    private var login: String { profil?.login ?? zustand.benutzer?.login ?? "" }

    private var name: String {
        if let p = profil, !p.anzeigename.isEmpty { return p.anzeigename }
        return zustand.benutzer?.name ?? login
    }

    /// Großer Titel: Kürzel bzw. eigener Plan, sonst der Anmeldename.
    private var grosserTitel: String {
        let element = profil?.element ?? zustand.benutzer?.element ?? ""
        return element.isEmpty ? login : element
    }

    private var rolle: String {
        let r = zustand.benutzer?.rollenname ?? ""
        return r.isEmpty ? (profil?.rolle ?? "") : r
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Text(grosserTitel)
                        .font(.system(size: 30, weight: .heavy))
                        .tracking(-0.6)
                        .foregroundStyle(Farben.titelGrau)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                        .padding(.top, 2)
                        .accessibilityAddTraits(.isHeader)

                    if let fehler {
                        Fehlerhinweis(text: fehler)
                    }

                    kontokarte

                    Zeilengruppe {
                        NavigationLink(value: Profilseite.kontaktdaten) {
                            Listenzeile(symbol: "person.text.rectangle", titel: "Kontaktdaten")
                        }
                        .buttonStyle(ZeilenKnopfStil())
                        Zeilentrenner()
                        NavigationLink(value: Profilseite.passwort) {
                            Listenzeile(symbol: "lock", titel: "Passwort ändern")
                        }
                        .buttonStyle(ZeilenKnopfStil())
                    }

                    Zeilengruppe {
                        NavigationLink(value: Profilseite.neues) {
                            Listenzeile(symbol: "megaphone", titel: "Was gibt es Neues")
                        }
                        .buttonStyle(ZeilenKnopfStil())
                        Zeilentrenner()
                        NavigationLink(value: Profilseite.ueber) {
                            Listenzeile(symbol: "info.circle", titel: "Über")
                        }
                        .buttonStyle(ZeilenKnopfStil())
                    }

                    Zeilengruppe {
                        Button {
                            bestaetigen = true
                        } label: {
                            Listenzeile(symbol: "rectangle.portrait.and.arrow.right", titel: "Abmelden")
                        }
                        .buttonStyle(ZeilenKnopfStil())
                    }

                    Text("UntPlan für iOS \(ProfilView.version)")
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.gedaempft)
                        .frame(maxWidth: .infinity)
                        .padding(.top, 4)
                }
                .padding(.horizontal, 18)
                .padding(.bottom, 28)
                .zentriert(maxBreite: 736)
            }
            .scrollIndicators(.hidden)
            .background(Farben.grund.ignoresSafeArea())
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(.hidden, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { KontoKnopf() }
            }
            .navigationDestination(for: Profilseite.self) { seite in
                switch seite {
                case .kontaktdaten: KontaktdatenView(profil: profil, name: name)
                case .passwort: PasswortView()
                case .neues: NeuigkeitenView()
                case .ueber: UeberView()
                }
            }
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

    /// Karte mit Avatar, Anmeldename, Rollen-Plakette und Konto-Wechsel (⌃⌄).
    private var kontokarte: some View {
        KontoMenue {
            HStack(spacing: 12) {
                Text(String(login.prefix(1)).uppercased())
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(Farben.avatar, in: Circle())
                VStack(alignment: .leading, spacing: 4) {
                    Text(login)
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    if !rolle.isEmpty {
                        Text(rolle)
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(.white)
                            .padding(.horizontal, 7)
                            .padding(.vertical, 2)
                            .background(Farben.zahlPlakette, in: Capsule())
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 4)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(Farben.gedaempft)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .alsKarte(radius: 20)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    static var version: String {
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
