import SwiftUI

/// Sprechtag (Elternsprechtag) – von der Startseite aus.
/// - Lehrkräfte: eigene Zeitfenster (gebucht mit Schüler*in und Buchenden, frei, gesperrt); gebuchte Termine absagen
///   (mit Grund, die Eltern werden benachrichtigt), freie Fenster sperren bzw. wieder freigeben.
/// - Erziehungsberechtigte: eigene Termine (absagen), je Kind die Lehrkräfte mit freien Zeiten – buchen im Anmeldezeitraum.
struct SprechtagView: View {
    let id: Int
    let titel: String
    @EnvironmentObject var zustand: AppZustand
    @State private var lehrkraft: SprechtagLehrkraftDaten? = nil
    @State private var eltern: SprechtagElternDaten? = nil
    @State private var fehler: String? = nil
    @State private var geladen = false
    @State private var filter: Filter = .alle
    @State private var absage: Absage? = nil
    @State private var grund = ""
    @State private var arbeitet: String? = nil
    @State private var meldung: String? = nil
    @State private var buchung: Buchungsziel? = nil

    enum Filter: Hashable {
        case alle
        case gebucht
        case frei
    }

    /// Abzusagender Termin.
    struct Absage: Identifiable {
        let id: Int
        let text: String
    }

    /// Erziehungsberechtigte (sonst Lehrkraft-Sicht).
    private var istEltern: Bool {
        !zustand.istKollegium && (zustand.benutzer?.rolle.lowercased() ?? "") == "eltern"
    }

    private var info: SprechtagInfo? { istEltern ? eltern?.sprechtag : lehrkraft?.sprechtag }

    /// Änderungen möglich (nicht vorbei, nicht abgesagt).
    private var aenderbar: Bool {
        guard let info else { return false }
        return !info.abgesagt && info.phase != "vorbei"
    }

    var body: some View {
        List {
            if let fehler {
                Fehlerhinweis(text: fehler)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            }
            if let meldung {
                Text(meldung)
                    .font(.system(size: 14))
                    .foregroundStyle(Farben.gruen)
                    .listRowBackground(Farben.flaeche)
            }
            if let info {
                kopf(info)
            }
            if istEltern {
                if let eltern { elternInhalt(eltern) }
            } else if let lehrkraft {
                lehrkraftInhalt(lehrkraft)
            }
            if !geladen && fehler == nil {
                ProgressView()
                    .frame(maxWidth: .infinity)
                    .listRowBackground(Color.clear)
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle(info?.titel ?? titel)
        .navigationBarTitleDisplayMode(.large)
        .toolbar(.visible, for: .navigationBar)
        .refreshable { await laden() }
        .task { await laden() }
        .navigationDestination(item: $buchung) { ziel in
            SprechtagBuchenView(sprechtagId: id, ziel: ziel) { text in
                meldung = text
                Task { await laden() }
            }
            .environmentObject(zustand)
        }
        .alert("Termin absagen?", isPresented: Binding(get: { absage != nil }, set: { if !$0 { absage = nil } }), presenting: absage) { a in
            TextField("Grund (optional)", text: $grund)
            Button("Absagen", role: .destructive) {
                Task { await absagen(a) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { a in
            Text(istEltern ? "\(a.text)\nDie Lehrkraft wird benachrichtigt." : "\(a.text)\nDie Erziehungsberechtigten werden benachrichtigt.")
        }
    }

    // MARK: - Kopf

    @ViewBuilder
    private func kopf(_ info: SprechtagInfo) -> some View {
        Section {
            VStack(alignment: .leading, spacing: 4) {
                Text(info.datumText.isEmpty ? Datumsformat.anzeige(info.datum, "EEEE, d. MMMM yyyy") : info.datumText)
                    .font(.system(size: 16, weight: .medium))
                    .foregroundStyle(Farben.tinte)
                if !info.beginn.isEmpty {
                    Text("\(info.beginn) – \(info.ende) Uhr" + (info.dauer > 0 ? " · je \(info.dauer) Minuten" : ""))
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
                if let raum = lehrkraft?.lehrer?.raum, !raum.isEmpty, !istEltern {
                    Text("Raum \(raum)")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
                HStack(spacing: 6) {
                    Plakette(text: info.phaseText, farbe: info.abgesagt ? Farben.entfallRand : (info.phase == "anmeldung" ? Farben.gruen : Farben.gedaempft))
                    if !istEltern, let l = lehrkraft?.lehrer, !l.abgesagt.isEmpty {
                        Plakette(text: "Teilnahme abgesagt", farbe: Farben.entfallRand)
                    }
                }
                .padding(.top, 2)
                if info.abgesagt {
                    Text("Der Sprechtag wurde abgesagt." + (info.absageGrund.isEmpty ? "" : " Grund: \(info.absageGrund)"))
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.entfallRand)
                }
                if !info.hinweis.isEmpty {
                    Text(info.hinweis)
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                        .padding(.top, 2)
                }
            }
            .padding(.vertical, 4)
            .listRowBackground(Farben.flaeche)
        }
    }

    // MARK: - Lehrkraft

    @ViewBuilder
    private func lehrkraftInhalt(_ d: SprechtagLehrkraftDaten) -> some View {
        let gebucht = d.zeitfenster.filter(\.gebucht)
        let frei = d.zeitfenster.filter(\.frei)
        let liste: [SprechtagZeitfenster] = {
            switch filter {
            case .alle: return d.zeitfenster
            case .gebucht: return gebucht
            case .frei: return frei
            }
        }()
        Section {
            Picker("Anzeige", selection: $filter) {
                Text("Alle (\(d.zeitfenster.count))").tag(Filter.alle)
                Text("Gebucht (\(gebucht.count))").tag(Filter.gebucht)
                Text("Frei (\(frei.count))").tag(Filter.frei)
            }
            .pickerStyle(.segmented)
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: 4, leading: 0, bottom: 4, trailing: 0))
        }
        Section {
            if liste.isEmpty {
                Text(d.zeitfenster.isEmpty ? "Keine Zeitfenster." : "Keine passenden Zeitfenster.")
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.gedaempft)
                    .listRowBackground(Farben.flaeche)
            }
            ForEach(liste) { f in
                fensterZeile(f)
                    .listRowBackground(Farben.flaeche)
                    .swipeActions(edge: .trailing) {
                        if aenderbar {
                            if let t = f.termin {
                                Button(role: .destructive) {
                                    absageAnfragen(t)
                                } label: {
                                    Label("Absagen", systemImage: "xmark.circle")
                                }
                            } else if f.gesperrt {
                                Button {
                                    Task { await sperren(f, gesperrt: false) }
                                } label: {
                                    Label("Freigeben", systemImage: "lock.open")
                                }
                                .tint(Farben.gruen)
                            } else {
                                Button {
                                    Task { await sperren(f, gesperrt: true) }
                                } label: {
                                    Label("Sperren", systemImage: "lock")
                                }
                                .tint(Farben.gedaempft)
                            }
                        }
                    }
            }
        } header: {
            Text("Meine Termine")
        } footer: {
            if aenderbar && !d.zeitfenster.isEmpty {
                Text("Nach links wischen: freie Zeiten sperren bzw. freigeben.")
            }
        }
    }

    private func fensterZeile(_ f: SprechtagZeitfenster) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(f.beginn)–\(f.ende)")
                .font(.system(size: 15, weight: .medium).monospacedDigit())
                .foregroundStyle(f.gebucht ? Farben.tinte : Farben.tinte2)
                .frame(width: 98, alignment: .leading)
            VStack(alignment: .leading, spacing: 2) {
                if let t = f.termin {
                    Text(t.schueler?.anzeige.isEmpty == false ? (t.schueler?.anzeige ?? "") : "Termin")
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                    let zusatz = [t.schueler?.klasse ?? "", t.gebuchtVon.isEmpty ? "" : "gebucht von \(t.gebuchtVon)"].filter { !$0.isEmpty }
                    if !zusatz.isEmpty {
                        Text(zusatz.joined(separator: " · "))
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if !t.bemerkung.isEmpty {
                        Text(t.bemerkung)
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                    }
                } else if f.gesperrt {
                    Label("gesperrt", systemImage: "lock")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gedaempft)
                } else {
                    Text("frei")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gruen)
                }
            }
            Spacer(minLength: 4)
            if arbeitet == "f\(f.beginn)" {
                ProgressView()
            } else if let t = f.termin, aenderbar {
                Button("Absagen", role: .destructive) { absageAnfragen(t) }
                    .font(.system(size: 14, weight: .medium))
                    .buttonStyle(.borderless)
            }
        }
        .padding(.vertical, 2)
    }

    private func absageAnfragen(_ t: SprechtagTermin) {
        grund = ""
        let wer = t.schueler?.anzeige ?? ""
        absage = Absage(id: t.id, text: "\(t.beginn) Uhr" + (wer.isEmpty ? "" : " · \(wer)"))
    }

    // MARK: - Erziehungsberechtigte

    @ViewBuilder
    private func elternInhalt(_ d: SprechtagElternDaten) -> some View {
        Section {
            if d.termine.isEmpty {
                Text("Noch keine Termine gebucht.")
                    .font(.system(size: 15))
                    .foregroundStyle(Farben.gedaempft)
                    .listRowBackground(Farben.flaeche)
            }
            ForEach(d.termine.sorted { $0.beginn < $1.beginn }) { t in
                HStack(alignment: .top, spacing: 12) {
                    Text("\(t.beginn)–\(t.ende)")
                        .font(.system(size: 15, weight: .medium).monospacedDigit())
                        .foregroundStyle(Farben.tinte)
                        .frame(width: 98, alignment: .leading)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(t.lehrerName.isEmpty ? t.lehrer : t.lehrerName)
                            .font(.system(size: 15, weight: .medium))
                            .foregroundStyle(Farben.tinte)
                        Text([t.schueler?.anzeige ?? "", t.raum.isEmpty ? "" : "Raum \(t.raum)"].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                    }
                    Spacer(minLength: 4)
                    if aenderbar {
                        Button("Absagen", role: .destructive) {
                            grund = ""
                            absage = Absage(id: t.id, text: "\(t.beginn) Uhr bei \(t.lehrerName.isEmpty ? t.lehrer : t.lehrerName)")
                        }
                        .font(.system(size: 14, weight: .medium))
                        .buttonStyle(.borderless)
                    }
                }
                .padding(.vertical, 2)
                .listRowBackground(Farben.flaeche)
            }
        } header: {
            Text("Meine Termine")
        } footer: {
            if !d.buchbar && aenderbar {
                if info?.phase == "einladung" {
                    Text("Termine können erst im Anmeldezeitraum gebucht werden.")
                } else {
                    Text("Termine können nur im Anmeldezeitraum gebucht werden.")
                }
            }
        }
        ForEach(d.kinder) { kind in
            Section {
                if kind.lehrkraefte.isEmpty {
                    Text("Keine Lehrkräfte am Sprechtag.")
                        .font(.system(size: 15))
                        .foregroundStyle(Farben.gedaempft)
                        .listRowBackground(Farben.flaeche)
                }
                ForEach(kind.lehrkraefte) { l in
                    lehrkraftZeile(l, kind: kind, buchbar: d.buchbar)
                        .listRowBackground(Farben.flaeche)
                }
            } header: {
                Text(kind.klasse.isEmpty ? kind.anzeige : "\(kind.anzeige) · \(kind.klasse)")
            }
        }
    }

    @ViewBuilder
    private func lehrkraftZeile(_ l: SprechtagKindLehrkraft, kind: SprechtagKind, buchbar: Bool) -> some View {
        let kannBuchen = buchbar && l.termin == nil && !l.abgesagt && !l.zeitfenster.isEmpty
        let inhalt = HStack(alignment: .center, spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(l.anzeige)
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    if l.eingeladen {
                        Plakette(text: "Eingeladen", farbe: Farben.akzent)
                    }
                }
                let unten = [l.faecher.joined(separator: ", "), l.klassenleitung ? "Klassenleitung" : "", l.raum.isEmpty ? "" : "Raum \(l.raum)"]
                    .filter { !$0.isEmpty }
                if !unten.isEmpty {
                    Text(unten.joined(separator: " · "))
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(2)
                }
                if l.eingeladen && !l.einladungText.isEmpty {
                    Text(l.einladungText)
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(3)
                }
            }
            Spacer(minLength: 4)
            if let t = l.termin {
                Text("\(t.beginn) Uhr")
                    .font(.system(size: 14, weight: .medium).monospacedDigit())
                    .foregroundStyle(Farben.gruen)
            } else if l.abgesagt {
                Text("nimmt nicht teil")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.gedaempft)
            } else if buchbar && l.zeitfenster.isEmpty {
                Text("ausgebucht")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.gedaempft)
            } else if kannBuchen {
                Text("\(l.zeitfenster.count) frei")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                Image(systemName: "chevron.right")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
        if kannBuchen {
            Button {
                buchung = Buchungsziel(kind: kind, lehrkraft: l)
            } label: {
                inhalt
            }
            .buttonStyle(.plain)
        } else {
            inhalt
        }
    }

    // MARK: - Laden und Aktionen

    private func laden() async {
        do {
            if istEltern {
                eltern = try await zustand.api.sprechtagEltern(id)
            } else {
                lehrkraft = try await zustand.api.sprechtagLehrkraft(id)
            }
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
        geladen = true
    }

    private func absagen(_ a: Absage) async {
        arbeitet = "t\(a.id)"
        defer { arbeitet = nil }
        do {
            try await zustand.api.sprechtagTerminAbsagen(id, termin: a.id, grund: grund)
            meldung = "Termin \(a.text) abgesagt."
            grund = ""
            await laden()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }

    private func sperren(_ f: SprechtagZeitfenster, gesperrt: Bool) async {
        arbeitet = "f\(f.beginn)"
        defer { arbeitet = nil }
        do {
            try await zustand.api.sprechtagSperren(id, beginne: [f.beginn], gesperrt: gesperrt)
            await laden()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Lehrkraft eines Kindes, bei der gebucht werden soll.
struct Buchungsziel: Hashable, Identifiable {
    let kind: SprechtagKind
    let lehrkraft: SprechtagKindLehrkraft

    var id: String { "\(kind.id)|\(lehrkraft.kurz)" }
}

/// Freie Zeiten einer Lehrkraft (Erziehungsberechtigte): Antippen bucht nach Rückfrage; Überschneidungen mit eigenen
/// Terminen sind gekennzeichnet.
struct SprechtagBuchenView: View {
    let sprechtagId: Int
    let ziel: Buchungsziel
    let gebucht: (String) -> Void
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var zurueck
    @State private var auswahl: SprechtagFreiesFenster? = nil
    @State private var bucht = false
    @State private var fehler: String? = nil

    var body: some View {
        List {
            if let fehler {
                Fehlerhinweis(text: fehler)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            }
            Section {
                ForEach(ziel.lehrkraft.zeitfenster) { f in
                    Button {
                        auswahl = f
                    } label: {
                        HStack {
                            Text("\(f.beginn)–\(f.ende) Uhr")
                                .font(.system(size: 16).monospacedDigit())
                                .foregroundStyle(Farben.tinte)
                            Spacer()
                            if f.konflikt {
                                Label("Überschneidung", systemImage: "exclamationmark.triangle")
                                    .font(.system(size: 13))
                                    .foregroundStyle(Farben.orange)
                            }
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .disabled(bucht)
                    .listRowBackground(Farben.flaeche)
                }
            } header: {
                Text("Freie Zeiten für \(ziel.kind.anzeige)")
            } footer: {
                if ziel.lehrkraft.zeitfenster.contains(where: \.konflikt) {
                    Text("„Überschneidung“: Zu dieser Zeit haben Sie bereits einen anderen Termin.")
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Farben.grund.ignoresSafeArea())
        .navigationTitle(ziel.lehrkraft.anzeige)
        .navigationBarTitleDisplayMode(.inline)
        .overlay {
            if bucht { ProgressView() }
        }
        .confirmationDialog(auswahl.map { "Termin um \($0.beginn) Uhr buchen?" } ?? "",
                            isPresented: Binding(get: { auswahl != nil }, set: { if !$0 { auswahl = nil } }),
                            titleVisibility: .visible, presenting: auswahl) { f in
            Button("Buchen") {
                Task { await buchen(f) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { f in
            Text("\(ziel.lehrkraft.anzeige) · \(ziel.kind.anzeige) · \(f.beginn)–\(f.ende) Uhr")
        }
    }

    private func buchen(_ f: SprechtagFreiesFenster) async {
        bucht = true
        defer { bucht = false }
        do {
            try await zustand.api.sprechtagBuchen(sprechtagId, SprechtagBuchDaten(lehrer: ziel.lehrkraft.kurz, schuelerId: ziel.kind.id, beginn: f.beginn))
            gebucht("Termin um \(f.beginn) Uhr bei \(ziel.lehrkraft.anzeige) gebucht.")
            zurueck()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}
