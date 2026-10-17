import SwiftUI
import UIKit

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
    @Environment(\.horizontalSizeClass) private var groessenklasse

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
                        .font(.system(size: groessenklasse == .compact ? 32 : 30, weight: .medium))
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

                    // Mitteilungen bei Änderungen im eigenen Plan (nur Lehrkräfte)
                    if zustand.darfPlanmitteilungen {
                        Zeilengruppe {
                            PlanmitteilungenSchalter()
                        }
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
                .padding(.horizontal, groessenklasse == .compact ? 16 : 18)
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
                Text(zustand.konten.count > 1
                     ? "Dieses Konto wird vom Gerät entfernt (ein gemerktes Passwort wird gelöscht); die App wechselt in Ihr nächstes Konto."
                     : "Ein gemerktes Passwort wird vom Gerät entfernt.")
            }
        }
    }

    /// Karte mit Avatar, Anmeldename, Rollen-Plakette und Konto-Wechsel (⌃⌄) – öffnet die Kontenauswahl.
    private var kontokarte: some View {
        KontoMenue {
            HStack(spacing: 12) {
                Text(String(login.prefix(1)).uppercased())
                    .font(.system(size: 17, weight: .medium))
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

/// Schalter „Mitteilungen bei Planänderungen“ (Profil, nur Lehrkräfte): an = beim Öffnen und im Hintergrund den eigenen
/// Plan prüfen und bei Entfall, Vertretung, Raumänderung, neuer Stunde oder Prüfung eine Mitteilung senden.
struct PlanmitteilungenSchalter: View {
    @EnvironmentObject var zustand: AppZustand
    @AppStorage(Planmitteilungen.schalterSchluessel) private var aktiv = true
    /// Mitteilungen in den iOS-Einstellungen ausgeschaltet
    @State private var verweigert = false
    @Environment(\.scenePhase) private var szene

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Toggle(isOn: $aktiv) {
                HStack(spacing: 13) {
                    Image(systemName: "bell.badge")
                        .font(.system(size: 19, weight: .light))
                        .foregroundStyle(Farben.gedaempft)
                        .frame(width: 28)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Mitteilungen bei Planänderungen")
                            .font(.system(size: 16))
                            .foregroundStyle(Farben.tinte)
                        Text("Entfall, Vertretung, Raumänderung, neue Stunde oder Prüfung im eigenen Plan")
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .frame(minHeight: 50)
            if aktiv && verweigert {
                Button {
                    if let url = URL(string: UIApplication.openNotificationSettingsURLString) {
                        UIApplication.shared.open(url)
                    }
                } label: {
                    Text("Mitteilungen sind in den iOS-Einstellungen ausgeschaltet. Einstellungen öffnen")
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.akzent)
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(.plain)
                .padding(.leading, 57)
                .padding(.trailing, 16)
                .padding(.bottom, 10)
            }
        }
        .task { verweigert = await Planmitteilungen.verweigert() }
        .onChange(of: szene) { _, neu in
            if neu == .active { Task { verweigert = await Planmitteilungen.verweigert() } }
        }
        .onChange(of: aktiv) { _, neu in
            if neu {
                Task {
                    await zustand.planPruefen()
                    verweigert = await Planmitteilungen.verweigert()
                }
            } else {
                Planmitteilungen.abbestellen()
            }
        }
    }
}
