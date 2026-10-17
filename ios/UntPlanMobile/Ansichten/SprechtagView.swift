import SwiftUI

/// Sprechtag (Elternsprechtag) – von der Startseite aus als Blatt (iPhone randlos und 90 % hoch, iPad festes Detailblatt).
/// Aufbau wie die übrigen Blätter: Kopf mit runden Glasknöpfen (X, ⋮), Titel mittig mit Datum und Zeitraum.
/// - Lehrkräfte: grauer Segmentschalter (Alle | Gebucht | Frei), darunter die Zeitfenster als gruppierte Liste – Uhrzeit
///   groß links, Status als farbiger Punkt mit Text, Schüler*in, Klasse und Buchende. Wischen bzw. langes Drücken:
///   gebuchte Termine absagen (mit Grund, die Eltern werden benachrichtigt), freie Zeiten sperren bzw. wieder freigeben.
/// - Erziehungsberechtigte: eigene Termine (absagen), je Kind Karten der Lehrkräfte mit den freien Zeiten als Chips –
///   Antippen bucht nach Rückfrage (nur im Anmeldezeitraum).
/// - Schüler*innen: dieselbe Familiensicht, nur lesend (falls die Schule den Sprechtag für sie sichtbar macht).
/// - Angekündigt (freigegeben, Anmeldung beginnt erst später): Hinweis mit Anmeldebeginn, Zeiten nur als Vorschau.
struct SprechtagView: View {
    let id: Int
    let titel: String
    /// Karte von der Startseite – Rückfall, falls die Details (noch) nicht geladen werden können (z. B. angekündigter
    /// Sprechtag ohne eigene Zeiten)
    var karte: SprechtagKarte? = nil
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @State private var lehrkraft: SprechtagLehrkraftDaten? = nil
    @State private var eltern: SprechtagElternDaten? = nil
    @State private var fehler: String? = nil
    @State private var geladen = false
    @State private var laedt = false
    @State private var filter: Filter = .alle
    @State private var absage: Absage? = nil
    @State private var grund = ""
    @State private var arbeitet: String? = nil
    @State private var meldung: String? = nil
    @State private var buchung: Buchungswahl? = nil
    @State private var sammel: Sammelaktion? = nil

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

    /// Freie Zeit einer Lehrkraft, die gebucht werden soll (Erziehungsberechtigte).
    struct Buchungswahl: Identifiable {
        let kind: SprechtagKind
        let lehrkraft: SprechtagKindLehrkraft
        let fenster: SprechtagFreiesFenster

        var id: String { "\(kind.id)|\(lehrkraft.kurz)|\(fenster.beginn)" }
    }

    /// Alle freien Zeiten sperren bzw. alle gesperrten freigeben (Lehrkraft, mit Rückfrage).
    struct Sammelaktion: Identifiable {
        let beginne: [String]
        let sperren: Bool

        var id: String { (sperren ? "s" : "f") + beginne.joined(separator: ",") }
    }

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt (bzw. zurück).
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    /// Familiensicht: Erziehungsberechtigte und Schüler*innen (sonst Lehrkraft-Sicht).
    private var istEltern: Bool {
        !zustand.istKollegium
    }

    /// Schüler*innen sehen den Sprechtag nur lesend (keine Buchung, keine Absage).
    private var nurLesen: Bool {
        istEltern && (zustand.benutzer?.rolle.lowercased() ?? "") != "eltern"
    }

    private var info: SprechtagInfo? { istEltern ? eltern?.sprechtag : lehrkraft?.sprechtag }

    /// Änderungen möglich (nicht vorbei, nicht abgesagt; nicht für Schüler*innen).
    private var aenderbar: Bool {
        guard let info, !nurLesen else { return false }
        return !info.abgesagt && info.phase != "vorbei" && info.status != "vorbei"
    }

    /// Untertitel im Kopf: „Do. 12.11.26 | 15:00 – 19:00“.
    private var zeitraum: String? {
        guard let info else { return nil }
        let tag = info.datum.isEmpty ? info.datumText : Datumsformat.anzeige(info.datum, "EE dd.MM.yy")
        let zeit = info.zeitenText
        let text = [tag, zeit].filter { !$0.isEmpty }.joined(separator: " | ")
        return text.isEmpty ? nil : text
    }

    /// Untertitel aus der Startkarte, solange keine Details vorliegen.
    private var kartenZeitraum: String? {
        guard let karte else { return nil }
        let tag = karte.datum.isEmpty ? karte.datumText : Datumsformat.anzeige(karte.datum, "EE dd.MM.yy")
        let zeit = karte.beginn.isEmpty ? "" : (karte.ende.isEmpty ? karte.beginn : "\(karte.beginn) – \(karte.ende)")
        let text = [tag, zeit].filter { !$0.isEmpty }.joined(separator: " | ")
        return text.isEmpty ? nil : text
    }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() }, laedt: laedt || !geladen) {
                Blatttitel(titel: info?.titel ?? titel, untertitel: zeitraum ?? kartenZeitraum)
            } rechts: {
                menue
            }
            if !istEltern, let d = lehrkraft, !d.zeitfenster.isEmpty {
                segment(d)
            }
            List {
                // Ohne Details, aber mit Startkarte (z. B. angekündigt, keine eigenen Zeiten): die Ankündigung statt des Fehlers
                if fehler != nil, info == nil, let karte {
                    kartenUebersicht(karte)
                } else if let fehler {
                    Section {
                        Fehlerhinweis(text: fehler)
                    }
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
                }
                if let meldung {
                    Section {
                        Label(meldung, systemImage: "checkmark.circle.fill")
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.gruen)
                    }
                    .listRowBackground(Farben.flaeche)
                }
                if let info {
                    uebersicht(info)
                }
                if istEltern {
                    if let eltern { elternInhalt(eltern) }
                } else if let lehrkraft {
                    lehrkraftInhalt(lehrkraft)
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Farben.grund)
            .refreshable { await laden() }
        }
        .background(Farben.blatt.ignoresSafeArea())
        .toolbar(.hidden, for: .navigationBar)
        .task { await laden() }
        .alert("Termin absagen?", isPresented: Binding(get: { absage != nil }, set: { if !$0 { absage = nil } }), presenting: absage) { a in
            TextField("Grund (optional)", text: $grund)
            Button("Absagen", role: .destructive) {
                Task { await absagen(a) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { a in
            Text(istEltern ? "\(a.text)\nDie Lehrkraft wird benachrichtigt." : "\(a.text)\nDie Erziehungsberechtigten werden benachrichtigt.")
        }
        .confirmationDialog(buchung.map { "Termin um \($0.fenster.beginn) Uhr buchen?" } ?? "",
                            isPresented: Binding(get: { buchung != nil }, set: { if !$0 { buchung = nil } }),
                            titleVisibility: .visible, presenting: buchung) { w in
            Button("Buchen") {
                Task { await buchen(w) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { w in
            Text("\(w.lehrkraft.anzeige) · \(w.kind.anzeige) · \(w.fenster.beginn)–\(w.fenster.ende) Uhr"
                 + (w.fenster.konflikt ? "\nZu dieser Zeit haben Sie bereits einen anderen Termin." : ""))
        }
        .confirmationDialog(sammel.map { $0.sperren ? "Alle freien Zeiten sperren?" : "Alle gesperrten Zeiten freigeben?" } ?? "",
                            isPresented: Binding(get: { sammel != nil }, set: { if !$0 { sammel = nil } }),
                            titleVisibility: .visible, presenting: sammel) { s in
            Button(s.sperren ? "\(s.beginne.count) Zeiten sperren" : "\(s.beginne.count) Zeiten freigeben", role: s.sperren ? ButtonRole.destructive : nil) {
                Task { await sperren(s.beginne, gesperrt: s.sperren) }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { s in
            Text(s.sperren ? "Gesperrte Zeiten können nicht gebucht werden." : "Die Zeiten können danach wieder gebucht werden.")
        }
    }

    // MARK: - Kopf

    private var menue: some View {
        Menu {
            Button {
                Task { await laden() }
            } label: {
                Label("Aktualisieren", systemImage: "arrow.clockwise")
            }
            if !istEltern, aenderbar, let d = lehrkraft {
                let frei = d.zeitfenster.filter(\.frei).map(\.beginn)
                let gesperrt = d.zeitfenster.filter(\.gesperrt).map(\.beginn)
                if !frei.isEmpty {
                    Button {
                        sammel = Sammelaktion(beginne: frei, sperren: true)
                    } label: {
                        Label("Alle freien Zeiten sperren", systemImage: "lock")
                    }
                }
                if !gesperrt.isEmpty {
                    Button {
                        sammel = Sammelaktion(beginne: gesperrt, sperren: false)
                    } label: {
                        Label("Alle gesperrten Zeiten freigeben", systemImage: "lock.open")
                    }
                }
            }
        } label: {
            Rundknopf(symbol: "ellipsis", gewicht: .medium, groesse: 18, drehung: 90)
        }
        // Symbole im Menü schwarz bzw. weiß je nach Modus
        .tint(Color.primary)
        .accessibilityLabel("Menü")
    }

    private func segment(_ d: SprechtagLehrkraftDaten) -> some View {
        let gebucht = d.zeitfenster.filter(\.gebucht).count
        let frei = d.zeitfenster.filter(\.frei).count
        return Segmentschalter(auswahl: $filter, [
            (Filter.alle, "Alle (\(d.zeitfenster.count))"),
            (Filter.gebucht, "Gebucht (\(gebucht))"),
            (Filter.frei, "Frei (\(frei))"),
        ])
        .padding(.horizontal, 14)
        .padding(.top, 2)
        .padding(.bottom, 8)
        .background(Farben.blatt)
    }

    // MARK: - Übersicht

    @ViewBuilder
    private func uebersicht(_ info: SprechtagInfo) -> some View {
        Section {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 8) {
                    Plakette(text: info.phaseText,
                             farbe: info.abgesagt ? Farben.entfallRand
                                 : (info.buchbar ? Farben.gruen : (info.angekuendigt ? Farben.akzent : Farben.gedaempft)))
                    if !istEltern, let l = lehrkraft?.lehrer, !l.abgesagt.isEmpty {
                        Plakette(text: "Teilnahme abgesagt", farbe: Farben.entfallRand)
                    }
                    Spacer(minLength: 4)
                    if !istEltern, let raum = lehrkraft?.lehrer?.raum, !raum.isEmpty {
                        Label("Raum \(raum)", systemImage: "door.left.hand.open")
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                            .lineLimit(1)
                    }
                }
                VStack(alignment: .leading, spacing: 2) {
                    Text(info.datumText.isEmpty ? Datumsformat.anzeige(info.datum, "EEEE, d. MMMM yyyy") : info.datumText)
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                    if !info.zeitenText.isEmpty {
                        Text("\(info.zeitenText) Uhr" + (info.dauer > 0 ? " · je \(info.dauer) Minuten" : ""))
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if !info.ort.isEmpty {
                        Text(info.ort)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if istEltern && !info.anmeldungBis.isEmpty && info.phase != "vorbei" && !info.abgesagt && !info.angekuendigt {
                        Text("Anmeldung bis \(Datumsformat.anzeige(info.anmeldungBis, "d.M.yyyy"))")
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                if info.angekuendigt && !info.abgesagt {
                    Label(info.ankuendigungText, systemImage: "megaphone")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
                if !istEltern, let d = lehrkraft, !d.zeitfenster.isEmpty {
                    HStack(spacing: 0) {
                        kennzahl(d.zeitfenster.filter(\.gebucht).count, "Gebucht", Farben.akzent)
                        kennzahl(d.zeitfenster.filter(\.frei).count, "Frei", Farben.gruen)
                        kennzahl(d.zeitfenster.filter(\.gesperrt).count, "Gesperrt", Farben.gedaempft)
                    }
                    .padding(.top, 2)
                }
                if info.abgesagt {
                    Label("Der Sprechtag wurde abgesagt." + (info.absageGrund.isEmpty ? "" : " Grund: \(info.absageGrund)"),
                          systemImage: "xmark.octagon")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.entfallRand)
                }
                if !info.hinweis.isEmpty {
                    Label(info.hinweis, systemImage: "info.circle")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
            }
            .padding(.vertical, 6)
            .listRowBackground(Farben.flaeche)
        }
    }

    /// Ankündigung aus der Startkarte (ohne Details).
    private func kartenUebersicht(_ k: SprechtagKarte) -> some View {
        Section {
            VStack(alignment: .leading, spacing: 10) {
                Plakette(text: k.angekuendigt ? "Angekündigt" : (k.buchbar ? "Anmeldung geöffnet" : "Sprechtag"),
                         farbe: k.buchbar ? Farben.gruen : (k.angekuendigt ? Farben.akzent : Farben.gedaempft))
                VStack(alignment: .leading, spacing: 2) {
                    Text(k.datumText.isEmpty ? Datumsformat.anzeige(k.datum, "EEEE, d. MMMM yyyy") : k.datumText)
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                    if !k.beginn.isEmpty {
                        Text(k.ende.isEmpty ? "\(k.beginn) Uhr" : "\(k.beginn) – \(k.ende) Uhr")
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                    if !k.ort.isEmpty {
                        Text(k.ort)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                if !k.text.isEmpty {
                    Label(k.text, systemImage: k.angekuendigt ? "megaphone" : "info.circle")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                } else if k.angekuendigt && !k.anmeldungVonText.isEmpty {
                    Label("Anmeldung ab \(k.anmeldungVonText)", systemImage: "megaphone")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
            }
            .padding(.vertical, 6)
            .listRowBackground(Farben.flaeche)
        }
    }

    private func kennzahl(_ zahl: Int, _ text: String, _ farbe: Color) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text("\(zahl)")
                .font(.system(size: 22, weight: .semibold).monospacedDigit())
                .foregroundStyle(Farben.tinte)
            Statusmarke(text: text, farbe: farbe)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    // MARK: - Lehrkraft

    @ViewBuilder
    private func lehrkraftInhalt(_ d: SprechtagLehrkraftDaten) -> some View {
        let liste: [SprechtagZeitfenster] = {
            switch filter {
            case .alle: return d.zeitfenster
            case .gebucht: return d.zeitfenster.filter(\.gebucht)
            case .frei: return d.zeitfenster.filter(\.frei)
            }
        }()
        Section {
            if liste.isEmpty {
                leer(d.zeitfenster.isEmpty ? "Keine Zeitfenster" : (filter == .gebucht ? "Noch keine Buchungen" : "Keine freien Zeiten"),
                     symbol: d.zeitfenster.isEmpty ? "calendar.badge.exclamationmark" : (filter == .gebucht ? "person.2.slash" : "clock.badge.xmark"),
                     text: d.zeitfenster.isEmpty ? "Für diesen Sprechtag sind Ihnen keine Zeiten zugeteilt."
                         : (filter == .gebucht ? "Sobald Eltern einen Termin buchen, erscheint er hier." : "Alle Zeiten sind gebucht oder gesperrt."))
            }
            ForEach(liste) { f in
                fensterZeile(f)
                    .listRowBackground(Farben.flaeche)
                    .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                        fensterAktionen(f)
                    }
                    .contextMenu {
                        fensterAktionen(f)
                    }
            }
        } header: {
            if !liste.isEmpty { Text("Zeitfenster") }
        } footer: {
            if aenderbar && !liste.isEmpty {
                Text("Nach links wischen oder lange drücken: Termine absagen, freie Zeiten sperren bzw. freigeben.")
            }
        }
    }

    @ViewBuilder
    private func fensterAktionen(_ f: SprechtagZeitfenster) -> some View {
        if aenderbar && arbeitet == nil {
            if let t = f.termin {
                Button(role: .destructive) {
                    absageAnfragen(t)
                } label: {
                    Label("Absagen", systemImage: "xmark.circle")
                }
            } else if f.gesperrt {
                Button {
                    Task { await sperren([f.beginn], gesperrt: false) }
                } label: {
                    Label("Freigeben", systemImage: "lock.open")
                }
                .tint(Farben.gruen)
            } else {
                Button {
                    Task { await sperren([f.beginn], gesperrt: true) }
                } label: {
                    Label("Sperren", systemImage: "lock")
                }
                .tint(Farben.gedaempft)
            }
        }
    }

    private func fensterZeile(_ f: SprechtagZeitfenster) -> some View {
        HStack(alignment: .center, spacing: 14) {
            Zeitspalte(beginn: f.beginn, ende: f.ende, aktiv: f.gebucht)
            VStack(alignment: .leading, spacing: 3) {
                if let t = f.termin {
                    Statusmarke(text: "Gebucht", farbe: Farben.akzent)
                    Text(t.schueler?.anzeige.isEmpty == false ? (t.schueler?.anzeige ?? "") : "Termin")
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(Farben.tinte)
                        .lineLimit(1)
                    let zusatz = [t.schueler?.klasse ?? "", t.gebuchtVon.isEmpty ? "" : "gebucht von \(t.gebuchtVon)"].filter { !$0.isEmpty }
                    if !zusatz.isEmpty {
                        Text(zusatz.joined(separator: " · "))
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                            .lineLimit(2)
                    }
                    if !t.bemerkung.isEmpty {
                        Text(t.bemerkung)
                            .font(.system(size: 13))
                            .italic()
                            .foregroundStyle(Farben.tinte2)
                            .lineLimit(3)
                    }
                } else if f.gesperrt {
                    Statusmarke(text: "Gesperrt", farbe: Farben.gedaempft, symbol: "lock.fill")
                    Text("Nicht buchbar")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.gedaempft)
                } else {
                    Statusmarke(text: "Frei", farbe: Farben.gruen)
                    Text("Noch nicht gebucht")
                        .font(.system(size: 14))
                        .foregroundStyle(Farben.tinte2)
                }
            }
            Spacer(minLength: 4)
            if arbeitet == "f\(f.beginn)" || (f.termin.map { arbeitet == "t\($0.id)" } ?? false) {
                ProgressView()
            }
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
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
                leer("Noch keine Termine", symbol: "calendar.badge.plus",
                     text: d.buchbar && !nurLesen ? "Tippen Sie unten bei einer Lehrkraft auf eine freie Zeit, um einen Termin zu buchen."
                         : "Gebuchte Termine erscheinen hier.")
            }
            ForEach(d.termine.sorted { $0.beginn < $1.beginn }) { t in
                elternTerminZeile(t)
                    .listRowBackground(Farben.flaeche)
                    .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                        if aenderbar && arbeitet == nil {
                            Button(role: .destructive) {
                                elternAbsageAnfragen(t)
                            } label: {
                                Label("Absagen", systemImage: "xmark.circle")
                            }
                        }
                    }
                    .contextMenu {
                        if aenderbar {
                            Button(role: .destructive) {
                                elternAbsageAnfragen(t)
                            } label: {
                                Label("Termin absagen", systemImage: "xmark.circle")
                            }
                        }
                    }
            }
        } header: {
            Text("Meine Termine")
        } footer: {
            if nurLesen {
                Text("Termine buchen Ihre Erziehungsberechtigten.")
            } else if !d.buchbar && aenderbar {
                if info?.angekuendigt == true, let von = info?.anmeldungVonText, !von.isEmpty {
                    Text("Termine können ab \(von) gebucht werden. Die Zeiten unten sind eine Vorschau.")
                } else if info?.phase == "einladung" || info?.angekuendigt == true {
                    Text("Termine können erst im Anmeldezeitraum gebucht werden.")
                } else {
                    Text("Termine können nur im Anmeldezeitraum gebucht werden.")
                }
            } else if aenderbar && !d.termine.isEmpty {
                Text(d.maxTermine > 0 ? "Nach links wischen oder lange drücken: Termin absagen. Höchstens \(d.maxTermine) Termine je Familie."
                     : "Nach links wischen oder lange drücken: Termin absagen.")
            }
        }
        if d.kinder.isEmpty {
            Section {
                leer(nurLesen ? "Keine Lehrkräfte" : "Keine Kinder", symbol: "person.crop.circle.badge.questionmark",
                     text: nurLesen ? "Für Ihr Konto sind keine Lehrkräfte für diesen Sprechtag hinterlegt."
                         : "Für Ihr Konto sind keine Kinder für diesen Sprechtag hinterlegt.")
            }
        }
        ForEach(d.kinder) { kind in
            Section {
                if kind.lehrkraefte.isEmpty {
                    leer("Keine Lehrkräfte", symbol: "person.2.slash", text: "Am Sprechtag nehmen keine Lehrkräfte von \(kind.anzeige) teil.")
                }
                ForEach(kind.lehrkraefte) { l in
                    lehrkraftKarte(l, kind: kind, buchbar: d.buchbar && aenderbar)
                        .listRowBackground(Farben.flaeche)
                }
            } header: {
                HStack(spacing: 8) {
                    Initialen(name: kind.anzeige, groesse: 22)
                    Text(kind.klasse.isEmpty ? kind.anzeige : "\(kind.anzeige) · \(kind.klasse)")
                }
            }
        }
    }

    private func elternTerminZeile(_ t: SprechtagElternTermin) -> some View {
        HStack(alignment: .center, spacing: 14) {
            Zeitspalte(beginn: t.beginn, ende: t.ende, aktiv: true)
            VStack(alignment: .leading, spacing: 3) {
                Statusmarke(text: "Gebucht", farbe: Farben.akzent)
                Text(t.lehrerName.isEmpty ? t.lehrer : t.lehrerName)
                    .font(.system(size: 16, weight: .medium))
                    .foregroundStyle(Farben.tinte)
                    .lineLimit(1)
                let zusatz = [t.schueler?.anzeige ?? "", t.online ? "Online" : (t.raum.isEmpty ? "" : "Raum \(t.raum)")].filter { !$0.isEmpty }
                if !zusatz.isEmpty {
                    Text(zusatz.joined(separator: " · "))
                        .font(.system(size: 13))
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(2)
                }
                if !t.bemerkung.isEmpty {
                    Text(t.bemerkung)
                        .font(.system(size: 13))
                        .italic()
                        .foregroundStyle(Farben.tinte2)
                        .lineLimit(3)
                }
                if t.online, let url = Karte.onlineURL(t.link) {
                    Link(destination: url) {
                        Label("Online-Termin öffnen", systemImage: "video")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Farben.akzent)
                    }
                }
            }
            Spacer(minLength: 4)
            if arbeitet == "t\(t.id)" {
                ProgressView()
            }
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    private func elternAbsageAnfragen(_ t: SprechtagElternTermin) {
        grund = ""
        absage = Absage(id: t.id, text: "\(t.beginn) Uhr bei \(t.lehrerName.isEmpty ? t.lehrer : t.lehrerName)")
    }

    /// Karte einer Lehrkraft: Name, Fächer, Raum; darunter der gebuchte Termin oder die freien Zeiten als Chips.
    private func lehrkraftKarte(_ l: SprechtagKindLehrkraft, kind: SprechtagKind, buchbar: Bool) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(alignment: .top, spacing: 11) {
                Initialen(name: l.anzeige, groesse: 34)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(l.anzeige)
                            .font(.system(size: 16, weight: .medium))
                            .foregroundStyle(Farben.tinte)
                            .lineLimit(1)
                        if l.eingeladen {
                            Plakette(text: "Eingeladen", farbe: Farben.akzent)
                        }
                    }
                    let unten = [l.faecher.joined(separator: ", "), l.klassenleitung ? "Klassenleitung" : "",
                                 l.online ? "Online" : (l.raum.isEmpty ? "" : "Raum \(l.raum)"), l.dauer > 0 ? "\(l.dauer) Min." : ""]
                        .filter { !$0.isEmpty }
                    if !unten.isEmpty {
                        Text(unten.joined(separator: " · "))
                            .font(.system(size: 13))
                            .foregroundStyle(Farben.tinte2)
                            .lineLimit(2)
                    }
                }
                Spacer(minLength: 0)
            }
            if l.eingeladen && !l.einladungText.isEmpty {
                Text(l.einladungText)
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
                    .lineLimit(4)
            }
            if let t = l.termin {
                Statusmarke(text: "Gebucht: \(t.beginn)–\(t.ende) Uhr", farbe: Farben.akzent)
            } else if l.abgesagt {
                Statusmarke(text: "Nimmt nicht teil", farbe: Farben.gedaempft)
            } else if l.zeitfenster.isEmpty || l.ausgebucht {
                Statusmarke(text: l.ausgebucht ? "Ausgebucht" : "Keine freien Zeiten", farbe: Farben.gedaempft)
            } else if buchbar {
                Fliesslayout(abstand: 6) {
                    ForEach(l.zeitfenster) { f in
                        Button {
                            buchung = Buchungswahl(kind: kind, lehrkraft: l, fenster: f)
                        } label: {
                            Zeitchip(fenster: f)
                        }
                        .buttonStyle(.plain)
                        .disabled(arbeitet != nil)
                        .accessibilityLabel("\(f.beginn) Uhr buchen" + (f.konflikt ? ", Überschneidung" : ""))
                    }
                }
                if l.zeitfenster.contains(where: \.konflikt) {
                    Label("Überschneidung mit einem Ihrer Termine", systemImage: "exclamationmark.triangle.fill")
                        .font(.system(size: 12))
                        .foregroundStyle(Farben.orange)
                }
            } else {
                Statusmarke(text: l.zeitfenster.count == 1 ? "1 freie Zeit" : "\(l.zeitfenster.count) freie Zeiten", farbe: Farben.gruen)
            }
        }
        .padding(.vertical, 6)
    }

    /// Leerer Zustand mit Symbol.
    private func leer(_ titel: String, symbol: String, text: String) -> some View {
        ContentUnavailableView {
            Label(titel, systemImage: symbol)
        } description: {
            Text(text)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 4)
    }

    // MARK: - Laden und Aktionen

    private func laden() async {
        laedt = true
        defer { laedt = false }
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

    private func sperren(_ beginne: [String], gesperrt: Bool) async {
        guard !beginne.isEmpty else { return }
        arbeitet = beginne.count == 1 ? "f\(beginne[0])" : "sammel"
        defer { arbeitet = nil }
        do {
            try await zustand.api.sprechtagSperren(id, beginne: beginne, gesperrt: gesperrt)
            if beginne.count > 1 {
                meldung = gesperrt ? "\(beginne.count) Zeiten gesperrt." : "\(beginne.count) Zeiten freigegeben."
            }
            await laden()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }

    private func buchen(_ w: Buchungswahl) async {
        arbeitet = "b\(w.id)"
        defer { arbeitet = nil }
        do {
            try await zustand.api.sprechtagBuchen(id, SprechtagBuchDaten(lehrer: w.lehrkraft.kurz, schuelerId: w.kind.id, beginn: w.fenster.beginn))
            meldung = "Termin um \(w.fenster.beginn) Uhr bei \(w.lehrkraft.anzeige) gebucht."
            await laden()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

// MARK: - Bausteine

/// Uhrzeit groß links in der Zeile, darunter klein das Ende.
private struct Zeitspalte: View {
    let beginn: String
    let ende: String
    var aktiv: Bool = true

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(beginn)
                .font(.system(size: 20, weight: .semibold).monospacedDigit())
                .foregroundStyle(aktiv ? Farben.tinte : Farben.tinte2)
            if !ende.isEmpty {
                Text("bis \(ende)")
                    .font(.system(size: 12).monospacedDigit())
                    .foregroundStyle(Farben.gedaempft)
            }
        }
        .lineLimit(1)
        .minimumScaleFactor(0.8)
        .frame(width: 62, alignment: .leading)
    }
}

/// Status als farbiger Punkt (bzw. Symbol) mit Text.
private struct Statusmarke: View {
    let text: String
    let farbe: Color
    var symbol: String? = nil

    var body: some View {
        HStack(spacing: 5) {
            if let symbol {
                Image(systemName: symbol)
                    .font(.system(size: 9, weight: .semibold))
            } else {
                Circle()
                    .frame(width: 7, height: 7)
            }
            Text(text)
                .font(.system(size: 13, weight: .medium))
                .lineLimit(1)
        }
        .foregroundStyle(farbe)
    }
}

/// Freie Zeit als Chip (Antippen bucht nach Rückfrage); Überschneidung orange mit Warnzeichen.
private struct Zeitchip: View {
    let fenster: SprechtagFreiesFenster

    var body: some View {
        let farbe = fenster.konflikt ? Farben.orange : Farben.gruen
        HStack(spacing: 3) {
            if fenster.konflikt {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 10))
            }
            Text(fenster.beginn)
                .font(.system(size: 14, weight: .medium).monospacedDigit())
        }
        .foregroundStyle(farbe)
        .padding(.horizontal, 11)
        .frame(height: 30)
        .background(farbe.opacity(0.12), in: Capsule())
        .overlay {
            Capsule().strokeBorder(farbe.opacity(0.35), lineWidth: 0.5)
        }
        .contentShape(Capsule())
    }
}

/// Einfaches Fließlayout: Kinder nebeneinander, bei Platzmangel in der nächsten Zeile.
struct Fliesslayout: Layout {
    var abstand: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        anordnen(breite: proposal.width ?? .infinity, subviews: subviews).groesse
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let punkte = anordnen(breite: bounds.width, subviews: subviews).punkte
        for (i, kind) in subviews.enumerated() where i < punkte.count {
            kind.place(at: CGPoint(x: bounds.minX + punkte[i].x, y: bounds.minY + punkte[i].y), proposal: .unspecified)
        }
    }

    private func anordnen(breite: CGFloat, subviews: Subviews) -> (punkte: [CGPoint], groesse: CGSize) {
        var punkte: [CGPoint] = []
        var x: CGFloat = 0
        var y: CGFloat = 0
        var zeile: CGFloat = 0
        var weiteste: CGFloat = 0
        for kind in subviews {
            let g = kind.sizeThatFits(.unspecified)
            if x > 0 && x + g.width > breite {
                x = 0
                y += zeile + abstand
                zeile = 0
            }
            punkte.append(CGPoint(x: x, y: y))
            x += g.width
            weiteste = max(weiteste, x)
            x += abstand
            zeile = max(zeile, g.height)
        }
        return (punkte, CGSize(width: weiteste, height: y + zeile))
    }
}
