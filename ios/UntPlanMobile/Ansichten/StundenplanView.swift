import SwiftUI

/// Gewählter Plan (Klasse, Lehrkraft oder Raum); nil = eigener Plan.
struct PlanAuswahl: Hashable {
    var typ: String
    var element: String
    var name: String
}

/// Stundenplan als Wochenraster mit Woche vor/zurück.
struct StundenplanView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var datum = Date()
    @State private var auswahl: PlanAuswahl? = nil
    @State private var plan: Wochenplan? = nil
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil
    @State private var zeigeAuswahl = false

    private var montag: Date { Datumsformat.montag(datum) }

    private var typ: String {
        if let auswahl { return auswahl.typ }
        let t = zustand.benutzer?.typ ?? ""
        return t.isEmpty ? "klasse" : t
    }

    private var element: String {
        auswahl?.element ?? zustand.benutzer?.element ?? ""
    }

    private var ladeSchluessel: String {
        "\(typ)|\(element)|\(Datumsformat.iso(montag))"
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                wochenleiste
                Divider()
                inhalt
            }
            .background(Color(.systemBackground))
            .navigationTitle(titel)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Heute") { datum = Date() }
                        .disabled(Datumsformat.iso(montag) == Datumsformat.iso(Datumsformat.montag(Date())))
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        zeigeAuswahl = true
                    } label: {
                        Image(systemName: "person.2.crop.square.stack")
                    }
                    .accessibilityLabel("Plan wählen")
                }
            }
            .sheet(item: $gewaehlteKarte) { karte in
                StundendetailView(karte: karte, typ: typ)
                    .presentationDetents([.medium, .large])
            }
            .sheet(isPresented: $zeigeAuswahl) {
                PlanAuswahlView(auswahl: $auswahl)
                    .environmentObject(zustand)
            }
            .task(id: ladeSchluessel) { await laden() }
        }
    }

    private var titel: String {
        if let name = plan?.name, !name.isEmpty { return name }
        if let auswahl { return auswahl.name.isEmpty ? auswahl.element : auswahl.name }
        return "Stundenplan"
    }

    // MARK: - Woche vor/zurück

    private var wochenleiste: some View {
        HStack {
            Button {
                datum = Datumsformat.tage(-7, nach: montag)
            } label: {
                Image(systemName: "chevron.left")
                    .font(.headline)
                    .frame(width: 44, height: 36)
            }
            .accessibilityLabel("Vorherige Woche")
            Spacer()
            VStack(spacing: 1) {
                Text(wochentext)
                    .font(.subheadline.weight(.semibold))
                Text(zeitraumtext)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button {
                datum = Datumsformat.tage(7, nach: montag)
            } label: {
                Image(systemName: "chevron.right")
                    .font(.headline)
                    .frame(width: 44, height: 36)
            }
            .accessibilityLabel("Nächste Woche")
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
    }

    private var wochentext: String {
        var s = "KW \(Datumsformat.kalenderwoche(montag))"
        if let w = plan?.woche, !w.isEmpty {
            s += w.count == 1 ? " · \(w)-Woche" : " · \(w)"
        }
        return s
    }

    private var zeitraumtext: String {
        let ende = Datumsformat.tage(4, nach: montag)
        return "\(Datumsformat.anzeige(montag, "d. MMM")) – \(Datumsformat.anzeige(ende, "d. MMM yyyy"))"
    }

    // MARK: - Inhalt

    @ViewBuilder
    private var inhalt: some View {
        if let fehler {
            VStack {
                Fehlerhinweis(text: fehler)
                Button("Erneut versuchen") { Task { await laden() } }
                    .padding(.top, 8)
                Spacer()
            }
            .padding(16)
        } else if element.isEmpty && auswahl == nil {
            ContentUnavailableView("Kein eigener Plan",
                                   systemImage: "calendar.badge.exclamationmark",
                                   description: Text("Ihrem Konto ist kein Plan zugeordnet. Wählen Sie oben rechts eine Klasse, eine Lehrkraft oder einen Raum."))
        } else if let plan {
            WochenrasterView(plan: plan, typ: typ) { gewaehlteKarte = $0 }
                .opacity(laedt ? 0.5 : 1)
                .simultaneousGesture(
                    DragGesture(minimumDistance: 40)
                        .onEnded { wert in
                            guard abs(wert.translation.width) > abs(wert.translation.height) * 1.5 else { return }
                            if wert.translation.width < -60 {
                                datum = Datumsformat.tage(7, nach: montag)
                            } else if wert.translation.width > 60 {
                                datum = Datumsformat.tage(-7, nach: montag)
                            }
                        }
                )
        } else {
            ProgressView("Plan wird geladen …")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func laden() async {
        guard !element.isEmpty else { return }
        laedt = true
        defer { laedt = false }
        do {
            let neu = try await zustand.api.plan(typ: typ, element: element, datum: montag)
            plan = neu
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

// MARK: - Wochenraster

/// Raster mit Tagesspalten und Zeilen je Stunde; Karten über mehrere Stunden reichen über mehrere Zeilen.
struct WochenrasterView: View {
    let plan: Wochenplan
    let typ: String
    let antippen: (Karte) -> Void

    private let zeilenHoehe: CGFloat = 62
    private let randBreite: CGFloat = 34

    /// Zeile je Stundennummer
    private var zeilen: [Int: Int] {
        var z: [Int: Int] = [:]
        for (i, zeit) in plan.zeitraster.enumerated() { z[zeit.nr] = i }
        return z
    }

    /// Ohne Zeitraster die Stunden aus den Karten ableiten.
    private var zeilenAnzahl: Int {
        if !plan.zeitraster.isEmpty { return plan.zeitraster.count }
        return max(plan.karten.map(\.bis).max() ?? 8, 8)
    }

    private var tage: [Tageskopf] { plan.tage }

    var body: some View {
        GeometryReader { geo in
            let spalten = max(tage.count, 1)
            let breite = max(44, (geo.size.width - randBreite) / CGFloat(spalten))
            VStack(spacing: 0) {
                tageskoepfe(breite: breite)
                Divider()
                ScrollView(.vertical) {
                    HStack(alignment: .top, spacing: 0) {
                        zeitspalte
                        ForEach(tage) { tag in
                            tagesspalte(tag, breite: breite)
                        }
                    }
                    .padding(.bottom, 12)
                }
                .background(
                    LinearGradient(colors: [Farben.rasterOben, Farben.rasterUnten], startPoint: .top, endPoint: .bottom)
                )
            }
        }
    }

    private func tageskoepfe(breite: CGFloat) -> some View {
        HStack(spacing: 0) {
            Color.clear.frame(width: randBreite, height: 1)
            ForEach(tage) { tag in
                let heute = Datumsformat.istHeute(tag.datum)
                VStack(spacing: 1) {
                    Text(String(tag.wochentag.prefix(2)))
                        .font(.caption.weight(.semibold))
                    Text(Datumsformat.anzeige(tag.datum, "d.M."))
                        .font(.caption2)
                    Circle()
                        .fill(tag.hinweise.isEmpty && tag.ereignisse.isEmpty ? Color.clear : Farben.akzent)
                        .frame(width: 4, height: 4)
                }
                .foregroundStyle(heute ? Farben.akzent : Color.primary)
                .frame(width: breite)
                .padding(.vertical, 4)
            }
        }
        .background(Color(.systemBackground))
    }

    private var zeitspalte: some View {
        VStack(spacing: 0) {
            if plan.zeitraster.isEmpty {
                ForEach(1...zeilenAnzahl, id: \.self) { nr in
                    zeitzelle(nr: nr, beginn: "")
                }
            } else {
                ForEach(plan.zeitraster) { zeit in
                    zeitzelle(nr: zeit.nr, beginn: zeit.beginn)
                }
            }
        }
        .frame(width: randBreite)
    }

    private func zeitzelle(nr: Int, beginn: String) -> some View {
        VStack(spacing: 1) {
            Text("\(nr)")
                .font(.system(size: 13, weight: .bold))
            Text(beginn)
                .font(.system(size: 9))
        }
        .foregroundStyle(Farben.gedaempft)
        .frame(width: randBreite, height: zeilenHoehe)
    }

    private func zeile(fuer nr: Int) -> Int {
        if let z = zeilen[nr] { return z }
        return min(max(nr - 1, 0), zeilenAnzahl - 1)
    }

    private func tagesspalte(_ tag: Tageskopf, breite: CGFloat) -> some View {
        let karten = plan.karten.filter { $0.datum == tag.datum }
        let platziert = Rasteranordnung.anordnen(karten)
        let hoehe = zeilenHoehe * CGFloat(zeilenAnzahl)
        let heute = Datumsformat.istHeute(tag.datum)
        return ZStack(alignment: .topLeading) {
            // Zeilenlinien
            VStack(spacing: 0) {
                ForEach(0..<zeilenAnzahl, id: \.self) { _ in
                    Rectangle()
                        .fill(Color.clear)
                        .frame(height: zeilenHoehe)
                        .overlay(alignment: .bottom) {
                            Rectangle().fill(Farben.linie).frame(height: 0.5)
                        }
                }
            }
            if tag.frei {
                ferienfeld(tag)
                    .frame(width: breite - 4, height: hoehe - 4)
                    .offset(x: 2, y: 2)
            } else {
                ForEach(platziert) { p in
                    let spurBreite = (breite - 4) / CGFloat(p.spuren)
                    let oben = CGFloat(zeile(fuer: p.karte.von)) * zeilenHoehe
                    let unten = CGFloat(zeile(fuer: p.karte.bis) + 1) * zeilenHoehe
                    Button {
                        antippen(p.karte)
                    } label: {
                        StundenKarteView(karte: p.karte, typ: typ)
                    }
                    .buttonStyle(.plain)
                    .frame(width: max(spurBreite - 2, 10), height: max(unten - oben - 4, 10))
                    .offset(x: 2 + CGFloat(p.spur) * spurBreite, y: oben + 2)
                }
            }
        }
        .frame(width: breite, height: hoehe, alignment: .topLeading)
        .background(heute ? Farben.akzent.opacity(0.07) : Color.clear)
        .overlay(alignment: .leading) {
            Rectangle().fill(Farben.linie).frame(width: 0.5)
        }
    }

    private func ferienfeld(_ tag: Tageskopf) -> some View {
        RoundedRectangle(cornerRadius: 4, style: .continuous)
            .fill(Farben.ferienHintergrund)
            .overlay(
                RoundedRectangle(cornerRadius: 4, style: .continuous)
                    .strokeBorder(Farben.ferienLinie, lineWidth: 1)
            )
            .overlay {
                Text(tag.freiGrund ?? "frei")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Farben.ferienTinte)
                    .multilineTextAlignment(.center)
                    .padding(4)
            }
    }
}

/// Karte mit Spur (nebeneinander liegende Stunden teilen sich die Spaltenbreite).
struct PlatzierteKarte: Identifiable {
    let karte: Karte
    let spur: Int
    let spuren: Int
    var id: UUID { karte.id }
}

enum Rasteranordnung {
    /// Überlappende Karten eines Tages auf Spuren verteilen.
    static func anordnen(_ karten: [Karte]) -> [PlatzierteKarte] {
        let sortiert = karten.sorted { ($0.von, $0.bis) < ($1.von, $1.bis) }
        var ergebnis: [PlatzierteKarte] = []
        var gruppe: [(karte: Karte, spur: Int)] = []
        var spurEnden: [Int] = []
        var gruppenEnde = Int.min

        func abschliessen() {
            let anzahl = max(spurEnden.count, 1)
            for eintrag in gruppe {
                ergebnis.append(PlatzierteKarte(karte: eintrag.karte, spur: eintrag.spur, spuren: anzahl))
            }
            gruppe = []
            spurEnden = []
        }

        for k in sortiert {
            if !gruppe.isEmpty && k.von > gruppenEnde { abschliessen() }
            if let frei = spurEnden.firstIndex(where: { $0 < k.von }) {
                spurEnden[frei] = k.bis
                gruppe.append((karte: k, spur: frei))
            } else {
                spurEnden.append(k.bis)
                gruppe.append((karte: k, spur: spurEnden.count - 1))
            }
            gruppenEnde = max(gruppenEnde, k.bis)
        }
        abschliessen()
        return ergebnis
    }
}

// MARK: - Planauswahl

/// Auswahl eines anderen Plans (Klassen, Lehrkräfte, Räume aus /api/app/elemente).
struct PlanAuswahlView: View {
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen
    @Binding var auswahl: PlanAuswahl?
    @State private var elemente: Elemente? = nil
    @State private var suche = ""
    @State private var fehler: String? = nil

    var body: some View {
        NavigationStack {
            List {
                if let fehler {
                    Fehlerhinweis(text: fehler)
                        .listRowInsets(EdgeInsets())
                }
                Section {
                    Button {
                        auswahl = nil
                        schliessen()
                    } label: {
                        Label("Mein Stundenplan", systemImage: "person.crop.circle")
                    }
                }
                if let elemente {
                    abschnitt("Klassen", typ: "klasse", liste: elemente.klassen)
                    abschnitt("Lehrkräfte", typ: "lehrer", liste: elemente.lehrer)
                    abschnitt("Räume", typ: "raum", liste: elemente.raeume)
                } else if fehler == nil {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                }
            }
            .searchable(text: $suche, prompt: "Suchen")
            .navigationTitle("Plan wählen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Schließen") { schliessen() }
                }
            }
            .task { await laden() }
        }
    }

    @ViewBuilder
    private func abschnitt(_ titel: String, typ: String, liste: [Planelement]) -> some View {
        let gefiltert = liste.filter { e in
            suche.isEmpty || e.kurz.localizedCaseInsensitiveContains(suche) || e.name.localizedCaseInsensitiveContains(suche)
        }
        if !gefiltert.isEmpty {
            Section(titel) {
                ForEach(gefiltert) { e in
                    Button {
                        auswahl = PlanAuswahl(typ: typ, element: e.kurz, name: e.name.isEmpty ? e.kurz : "\(e.kurz) – \(e.name)")
                        schliessen()
                    } label: {
                        HStack {
                            Text(e.kurz)
                                .fontWeight(.semibold)
                                .foregroundStyle(Color.primary)
                            if !e.name.isEmpty && e.name != e.kurz {
                                Text(e.name)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            if auswahl?.typ == typ && auswahl?.element == e.kurz {
                                Image(systemName: "checkmark")
                                    .foregroundStyle(Farben.akzent)
                            }
                        }
                    }
                }
            }
        }
    }

    private func laden() async {
        do {
            elemente = try await zustand.api.elemente()
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}
