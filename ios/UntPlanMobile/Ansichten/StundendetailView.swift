import SwiftUI

/// Details einer Stunde (Blatt beim Antippen einer Karte) genau nach Vorlage:
/// Kopf mit rundem X links, in der Mitte „Do. | 05.11.26 ⌄“ und die Uhrzeit, rechts ⋮ („Im Stundenplan anzeigen“);
/// darunter links bündig Klasse | Gruppe, Fach, Raum und Lehrkraft (nur Text), eine Trennlinie und die weiße Liste ohne
/// Trennlinien zwischen den Menüpunkten (Vertretungstext, Findet online statt, Lehrstoff, Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen).
/// Unterseiten öffnen sich im selben Blatt; die Blattgröße bleibt dabei gleich.
struct StundendetailView: View {
    let karten: [Karte]
    let typ: String
    var element: String = ""
    @EnvironmentObject var zustand: AppZustand
    /// Gewählter Reiter (bei mehreren Stunden zur selben Zeit)
    @State private var auswahl: Int

    init(karte: Karte, typ: String, element: String = "") {
        self.init(karten: [karte], start: 0, typ: typ, element: element)
    }

    init(karten: [Karte], start: Int, typ: String, element: String = "") {
        self.karten = karten
        self.typ = typ
        self.element = element
        _auswahl = State(initialValue: min(max(start, 0), max(karten.count - 1, 0)))
    }

    var body: some View {
        NavigationStack {
            if karten.indices.contains(auswahl) {
                StundendetailInhalt(karte: karten[auswahl], typ: typ, karten: karten, auswahl: $auswahl)
                    .id(karten[auswahl].id)
            } else {
                ContentUnavailableView("Keine Stunde", systemImage: "calendar")
                    .toolbar(.hidden, for: .navigationBar)
            }
        }
        .environmentObject(zustand)
        .background(Farben.blatt.ignoresSafeArea())
        // Tastatur: Unterseiten mit Textfeldern weichen ihr selbst aus (Systemverhalten, nichts Manuelles); beim
        // Schließen des Blatts wird der Fokus abgegeben, damit keine Tastatur-Einrückung hängen bleibt
        .onDisappear { Tastatur.schliessen() }
    }
}

/// Inhalt der Stundendetails für eine Stunde; bei mehreren Stunden derselben Zeit stehen die Reiter
/// in einer eigenen Zeile zwischen Kopf und Kopfblock (sie überlappen nichts).
struct StundendetailInhalt: View {
    let karte: Karte
    let typ: String
    let karten: [Karte]
    @Binding var auswahl: Int
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @Environment(\.openURL) private var oeffneURL
    @StateObject private var daten: Stundendaten
    @State private var onlineFehler: String? = nil
    /// Raumliste („Ersetzen“ bzw. „Raum hinzufügen“) aus der Raumzeile
    @State private var raumZiel: RaumZiel? = nil

    init(karte: Karte, typ: String, karten: [Karte], auswahl: Binding<Int>) {
        self.karte = karte
        self.typ = typ
        self.karten = karten
        _auswahl = auswahl
        _daten = StateObject(wrappedValue: Stundendaten(karte: karte))
    }

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    private var kollegium: Bool { zustand.istKollegium }
    private var mitUnterricht: Bool { karte.unterricht != nil }
    private var istVeranstaltung: Bool { karte.istVeranstaltung }
    /// Klassenbuch, Hausaufgaben usw. werden noch geladen (Ladebalken unter dem Kopf).
    private var laedt: Bool { mitUnterricht && !daten.geladen }

    var body: some View {
        VStack(spacing: 0) {
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() },
                      laedt: karten.count <= 1 && laedt, mitte: {
                datumsMenue
            }, rechts: {
                weitereAktionen
            })
            if karten.count > 1 {
                reiterleiste
                    .overlay(alignment: .bottom) { Ladebalken(aktiv: laedt) }
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopfblock
                        .padding(.horizontal, Detailmass.rand)
                        .padding(.top, karten.count > 1 ? 12 : 10)
                        .padding(.bottom, 12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(karte.istEntfall ? Farben.detailEntfallFlaeche : Color.clear)
                    if karte.istEntfall {
                        // Original: roter Balken über die volle Breite mit „ENTFÄLLT“
                        Text("ENTFÄLLT")
                            .font(.system(size: 17))
                            .foregroundStyle(.white)
                            .frame(maxWidth: .infinity)
                            .frame(height: 27)
                            .background(Farben.detailEntfallBalken)
                            .accessibilityLabel("Entfällt")
                    } else {
                        Rectangle()
                            .fill(Farben.trennlinie)
                            .frame(height: 1)
                    }
                    zeilen
                        .padding(.top, 8)
                        .padding(.bottom, 24)
                }
            }
            .scrollIndicators(.hidden)
        }
        .background(Farben.blatt.ignoresSafeArea())
        // Hauptseite ohne Textfelder: bleibt beim Ein- und Ausblenden der Tastatur (z. B. beim Zurückkehren aus einer
        // Notiz) unverändert stehen
        .ignoresSafeArea(.keyboard, edges: .bottom)
        .toolbar(.hidden, for: .navigationBar)
        .navigationDestination(for: Detailseite.self) { seite in
            ziel(seite)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .navigationDestination(item: $raumZiel) { z in
            RaumSeite(ziel: z)
                .environmentObject(daten)
                .environmentObject(zustand)
        }
        .overlay(alignment: .bottom) {
            if let hinweis = daten.hinweis {
                Text(hinweis)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(.white)
                    .lineLimit(2)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 10)
                    .background(Color.black.opacity(0.78), in: Capsule())
                    .padding(.bottom, 28)
                    .transition(.opacity)
                    .task(id: hinweis) {
                        try? await Task.sleep(nanoseconds: 2_400_000_000)
                        withAnimation { daten.hinweis = nil }
                    }
            }
        }
        .task { await daten.laden(kollegium: kollegium) }
        .environmentObject(daten)
        .alert("Speichern fehlgeschlagen", isPresented: Binding(get: { onlineFehler != nil }, set: { if !$0 { onlineFehler = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(onlineFehler ?? "")
        }
    }

    // MARK: - Kopf (Datum, Uhrzeit, ⋮)

    private var datumstitel: String {
        "\(Datumsformat.anzeige(karte.datum, "EE")) | \(Datumsformat.anzeige(karte.datum, "dd.MM.yy"))"
    }

    /// „7:45 - 11:05“ (Stunden ohne führende Null wie im Original); bei gewählter Einzelstunde deren Zeit mit „3. Std.“.
    private var zeittitel: String {
        if mitBereichswahl, let nr = daten.einzelstunde, let s = daten.blockStunden.first(where: { $0.stunde == nr }) {
            return s.beginn.isEmpty ? "\(nr). Std." : "\(nr). Std. · \(Detailmass.uhrzeit(s.beginn)) - \(Detailmass.uhrzeit(s.ende))"
        }
        return karte.beginn.isEmpty ? stundentext : "\(Detailmass.uhrzeit(karte.beginn)) - \(Detailmass.uhrzeit(karte.ende))"
    }

    /// „Do. | 05.11.26 ⌄“ mit Uhrzeit darunter; das Menü nennt Tag und Stunde, wechselt zwischen gleichzeitigen Stunden
    /// und wählt bei Doppel- bzw. Blockstunden, ob Einträge für den ganzen Block oder nur eine Einzelstunde gelten.
    private var datumsMenue: some View {
        Menu {
            Section(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")) {
                Button {} label: {
                    Label(karte.beginn.isEmpty ? stundentext : "\(stundentext) · \(karte.beginn) - \(karte.ende)", systemImage: "clock")
                }
                .disabled(true)
            }
            if mitBereichswahl {
                Section("Einträge gelten für") {
                    Button {
                        bereichBinding.wrappedValue = 0
                    } label: {
                        Label("Ganzer Block (\(karte.von).–\(karte.bis). Stunde)",
                              systemImage: daten.einzelstunde == nil ? "checkmark" : "square.stack")
                    }
                    ForEach(daten.blockStunden) { s in
                        Button {
                            bereichBinding.wrappedValue = s.stunde
                        } label: {
                            Label(stundentitel(s, mitZeit: true) + (s.eintrag ? " •" : ""),
                                  systemImage: daten.einzelstunde == s.stunde ? "checkmark" : "clock")
                        }
                    }
                }
            }
            if karten.count > 1 {
                Section("Stunden zur selben Zeit") {
                    ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                        Button {
                            auswahl = eintrag.offset
                        } label: {
                            Label(reitertitel(eintrag.element), systemImage: eintrag.offset == auswahl ? "checkmark" : "book.closed")
                        }
                    }
                }
            }
        } label: {
            Blatttitel(titel: datumstitel, untertitel: zeittitel, pfeil: kollegium || karten.count > 1 || mitBereichswahl)
                .contentShape(Rectangle())
        }
        // Symbole im Menü schwarz bzw. weiß je nach Modus (nicht orange bzw. grau)
        .tint(Color.primary)
        .accessibilityLabel("\(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")), \(zeittitel)")
    }

    @ViewBuilder
    private var weitereAktionen: some View {
        if !planziele.isEmpty {
            Menu {
                Section("Im Stundenplan anzeigen") {
                    ForEach(planziele) { plan in
                        Button {
                            zustand.planZeigen(plan)
                            schliessen()
                        } label: {
                            Label(plan.anzeige, image: plan.bild)
                        }
                    }
                }
            } label: {
                Rundknopf(symbol: "ellipsis", gewicht: .medium, groesse: 18, drehung: 90)
            }
            .tint(Color.primary)
            .accessibilityLabel("Weitere Aktionen")
        }
    }

    // MARK: - Ganzer Block / Einzelstunde

    /// Doppel- bzw. Blockstunde: Lehrkräfte wählen im mittigen Menü (Titel mit ⌄), ob Einträge für den ganzen Block
    /// oder nur eine Stunde gelten.
    private var mitBereichswahl: Bool { kollegium && mitUnterricht && !istVeranstaltung && daten.hatEinzelstunden }

    /// „3. Std.“ bzw. mit Uhrzeit im Menü.
    private func stundentitel(_ s: Einzelstundeninfo, mitZeit: Bool) -> String {
        guard mitZeit, !s.beginn.isEmpty else { return "\(s.stunde). Std." }
        return "\(s.stunde). Stunde \(Detailmass.uhrzeit(s.beginn))–\(Detailmass.uhrzeit(s.ende))"
    }

    private var bereichBinding: Binding<Int> {
        Binding(get: { daten.einzelstunde ?? 0 }, set: { neu in
            Task { await daten.bereichWaehlen(neu == 0 ? nil : neu, kollegium: kollegium) }
        })
    }

    // MARK: - Reiter

    private func reitertitel(_ k: Karte) -> String {
        // Veranstaltung: die Bezeichnung steht nur unten in „Information zur Stunde“, nicht im Reiter (wie die PWA)
        if k.istVeranstaltung { return "Veranstaltung" }
        if !k.fach.isEmpty { return k.fach }
        if !k.anzeigeText.isEmpty { return k.anzeigeText }
        return k.anzeigeArt.isEmpty ? "Stunde" : k.anzeigeArt
    }

    /// Reiterleiste in eigener Zeile unter dem Kopf genau nach Vorlage (133): hellgraue Zeile, waagrecht scrollbare
    /// Text-Reiter (Regular, aktiv Medium), nur unter dem aktiven eine Unterstreichung (dunkelgrau bzw. im Dunkeln weiß)
    /// etwas breiter als der Text; der Reitertext wird nie durchgestrichen (auch nicht bei Entfall). Der aktive Reiter
    /// wird beim Wechsel in den sichtbaren Bereich gerollt.
    private var reiterleiste: some View {
        ScrollViewReader { leser in
            ScrollView(.horizontal) {
                HStack(spacing: 4) {
                    ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                        let gewaehlt = eintrag.offset == auswahl
                        Button {
                            auswahl = eintrag.offset
                        } label: {
                            // Größere Reiter: 17 pt, mindestens 64 pt breit, 44 pt hoch (bequem antippbar)
                            Text(reitertitel(eintrag.element))
                                .font(.system(size: 17, weight: gewaehlt ? .medium : .regular))
                                .foregroundStyle(gewaehlt ? Farben.detailText : Farben.tinte2)
                                .lineLimit(1)
                                .padding(.horizontal, 14)
                                .frame(minWidth: 64, minHeight: 44)
                                .overlay(alignment: .bottom) {
                                    if gewaehlt {
                                        Capsule()
                                            .fill(Farben.reiterStrich)
                                            .frame(height: 3)
                                            .padding(.horizontal, 8)
                                    }
                                }
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .id(eintrag.offset)
                        .accessibilityAddTraits(gewaehlt ? .isSelected : [])
                    }
                }
                .padding(.horizontal, Detailmass.rand - 10)
            }
            .scrollIndicators(.hidden)
            .onAppear { leser.scrollTo(auswahl, anchor: .center) }
            .onChange(of: auswahl) { _, neu in
                withAnimation(.easeInOut(duration: 0.2)) { leser.scrollTo(neu, anchor: .center) }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Farben.reiterGrund)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Farben.trennlinie)
                .frame(height: 0.5)
        }
    }

    /// Vertretungstext bzw. Information der Stunde (Zusatzstunde: nur manuell eingegebene Texte).
    private var detailText: String {
        karte.istZusatzstunde ? karte.zusatzText : karte.anzeigeText
    }

    /// Zeile „Verlegt“ bzw. „Hierher verlegt“: Ursprung und Ziel untereinander (fehlt die Gegenseite, nur der Titel).
    private func verlegungZeile(_ richtung: String) -> some View {
        HStack(spacing: 12) {
            Listensymbol(name: "arrow.right.square")
            VStack(alignment: .leading, spacing: 3) {
                Text(richtung == "von" ? "Verlegt" : "Hierher verlegt")
                    .font(.system(size: 19, weight: .regular))
                    .foregroundStyle(Farben.detailText)
                    .lineLimit(1)
                if let von = karte.verlegungVon?.text, !von.isEmpty, let nach = karte.verlegungNach?.text, !nach.isEmpty {
                    Text("Von: \(von)")
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.detailGrau)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                    Text("Nach: \(nach)")
                        .font(.system(size: 17))
                        .foregroundStyle(Farben.detailGrau)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
            Spacer(minLength: 6)
        }
        .padding(.leading, Detailmass.rand)
        .padding(.trailing, 22)
        .padding(.vertical, 10)
        .frame(minHeight: 56)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(karte.verlegungText.map { "Verlegung: \($0)" } ?? (richtung == "von" ? "Verlegt" : "Hierher verlegt"))
    }

    private var textTitel: String {
        karte.keineVertretung || karte.status == "regulaer" ? "Information zur Stunde" : "Vertretungstext"
    }

    /// Infotext einer Veranstaltung (Zeile „Information zur Stunde“) – nur hier steht die Bezeichnung der Veranstaltung.
    private var veranstaltungsInfo: String { Kachelbau.veranstaltungsInfo(karte) }

    /// Untertitel der Zeile „Prüfung“ (Vorlage 140: das Fach, sonst Text bzw. Art).
    private var pruefungsUntertitel: String {
        if !karte.fach.isEmpty { return karte.fach }
        if !karte.anzeigeText.isEmpty { return karte.anzeigeText }
        return karte.anzeigeArt
    }

    /// Angaben zur Prüfung für die Unterseite „Prüfung“.
    private var pruefungsText: String {
        var zeilen: [String] = []
        let art = karte.anzeigeArt.isEmpty ? "Prüfung" : karte.anzeigeArt
        zeilen.append(art)
        let fach = karte.fachName.isEmpty ? karte.fach : (karte.fach.isEmpty ? karte.fachName : "\(karte.fachName) (\(karte.fach))")
        if !fach.isEmpty { zeilen.append("Fach: \(fach)") }
        let klassen = ohneDoppelte(karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k })
        if !klassen.isEmpty { zeilen.append((klassen.count == 1 ? "Klasse: " : "Klassen: ") + klassen.joined(separator: ", ")) }
        let raeume = ohneDoppelte(daten.aktuelleRaeume.map(\.k))
        if !raeume.isEmpty { zeilen.append((raeume.count == 1 ? "Raum: " : "Räume: ") + raeume.joined(separator: ", ")) }
        let lehrer = ohneDoppelte(karte.lehrer.map(\.k))
        if !lehrer.isEmpty { zeilen.append("Lehrkraft: " + lehrer.joined(separator: ", ")) }
        zeilen.append("Zeit: \(stundentext)" + (karte.beginn.isEmpty ? "" : ", \(karte.beginn) - \(karte.ende)"))
        if !karte.anzeigeText.isEmpty && karte.anzeigeText != art { zeilen.append("\n" + karte.anzeigeText) }
        return zeilen.joined(separator: "\n")
    }

    private var stundentext: String {
        karte.von == karte.bis ? "\(karte.von). Stunde" : "\(karte.von).–\(karte.bis). Stunde"
    }

    /// Räume (auch der ursprüngliche), Klassen und Lehrkräfte, deren Plan das Konto sehen darf.
    private var planziele: [PlanAuswahl] {
        var ergebnis: [PlanAuswahl] = []
        func hinzu(_ typ: String, _ werte: [Wert]) {
            for w in werte {
                for k in [w.alt ?? "", w.k] where !k.isEmpty {
                    let plan = PlanAuswahl(typ: typ, element: k, name: k)
                    if !ergebnis.contains(where: { $0.id == plan.id }) { ergebnis.append(plan) }
                }
            }
        }
        hinzu("raum", daten.aktuelleRaeume)
        if kollegium {
            hinzu("klasse", karte.klassen)
            hinzu("lehrer", karte.lehrer)
        } else if let eigene = zustand.benutzer?.element, !eigene.isEmpty {
            hinzu("klasse", karte.klassen.filter { $0.k.caseInsensitiveCompare(eigene) == .orderedSame })
        }
        return ergebnis
    }

    // MARK: - Kopfblock

    /// „Klasse | Klasse | Gruppe“. Veranstaltung: nur die Klassen – die Lehrkräfte stehen einmal unten in ihrer Zeile.
    private var titelzeile: String {
        var teile = ohneDoppelte(karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k })
        if !karte.gruppe.isEmpty { teile.append(karte.gruppe) }
        if teile.isEmpty {
            return istVeranstaltung ? "" : karte.titel
        }
        return teile.joined(separator: " | ")
    }

    /// Werte ohne Leere und ohne Doppelte (Groß-/Kleinschreibung egal), Reihenfolge bleibt.
    private func ohneDoppelte(_ werte: [String]) -> [String] {
        var ergebnis: [String] = []
        for w in werte where !w.isEmpty && !ergebnis.contains(where: { $0.caseInsensitiveCompare(w) == .orderedSame }) {
            ergebnis.append(w)
        }
        return ergebnis
    }

    /// Veranstaltung: jede Lehrkraft bzw. jeder Raum nur einmal.
    private func einmal(_ werte: [Wert]) -> [Wert] {
        guard istVeranstaltung else { return werte }
        var ergebnis: [Wert] = []
        for w in werte {
            let t = w.k.isEmpty ? (w.alt ?? "") : w.k
            if t.isEmpty || ergebnis.contains(where: { ($0.k.isEmpty ? ($0.alt ?? "") : $0.k).caseInsensitiveCompare(t) == .orderedSame }) { continue }
            ergebnis.append(w)
        }
        return ergebnis
    }

    /// Fach einer Veranstaltung für die Fachzeile: nur ein eingetragenes Fach, nie die Bezeichnung der Veranstaltung.
    private var veranstaltungsFach: String {
        let f = karte.fach.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !f.isEmpty else { return "" }
        let bezeichnung = karte.anzeigeText.isEmpty ? karte.anzeigeArt : karte.anzeigeText
        return f.caseInsensitiveCompare(bezeichnung) == .orderedSame ? "" : f
    }

    /// Offene Vertretung: Lehrkraft ohne neuen Wert (Karte „offen“ bzw. Kürzel leer und alter Wert gesetzt).
    private var lehrerOffen: Bool { karte.istOffeneVertretung }

    /// Kopfzeile „Klasse | Gruppe“: eigene Klasse(n), anwesende Mitklassen als Text, fehlende durchgestrichen;
    /// Trennstrich grau. Veranstaltung ohne Klassen: keine Kopfzeile.
    private var titelBausteine: [Wertbaustein] {
        var teile: [Wertbaustein] = []
        guard !karte.mitklassen.isEmpty else {
            if titelzeile.isEmpty { return [] }
            for teil in titelzeile.components(separatedBy: " | ") {
                if !teile.isEmpty { teile.append(.trenner) }
                teile.append(.text(teil))
            }
            return teile
        }
        func hinzu(_ b: Wertbaustein) {
            if !teile.isEmpty { teile.append(.trenner) }
            teile.append(b)
        }
        for w in karte.klassen {
            let t = w.k.isEmpty ? (w.alt ?? "") : w.k
            if !t.isEmpty { hinzu(.text(t)) }
        }
        for w in karte.mitklassen {
            if !w.k.isEmpty {
                hinzu(.text(w.k))
            } else if let alt = w.alt, !alt.isEmpty {
                hinzu(.weg(alt))
            }
        }
        if !karte.gruppe.isEmpty { hinzu(.text(karte.gruppe)) }
        return teile
    }

    /// Kopfblock genau nach Vorlage (Original 117/118): „Klasse | Gruppe“ und Fach groß (22 pt, schwarz), Raum und
    /// Lehrkraft 17 pt grau, je Zeile 31 pt hoch. Nur Text, keine Plaketten und keine Durchstreichung des Ganzen
    /// (Entfall zeigt der rote Balken): Änderungen als „alt → neu“ mit durchgestrichenem altem Wert, fehlende Werte
    /// durchgestrichen. Jede Angabe steht in genau einer Zeile, die sich bei vielen Werten waagrecht scrollen lässt;
    /// sehr viele Werte (z. B. alle Lehrkräfte oder Klassen) werden auf „… +N“ gekürzt und lassen sich aufklappen.
    /// Veranstaltung: oben keine Bezeichnung (die steht nur unten in „Information zur Stunde“) – Kopf- und Fachzeile
    /// erscheinen nur, wenn Klassen bzw. ein Fach eingetragen sind, wie bei einer normalen Stunde.
    private var kopfblock: some View {
        let fach = istVeranstaltung ? veranstaltungsFach
                                    : (karte.fach.isEmpty ? karte.anzeigeArt : karte.fach)
        // Pfeil „alt → neu“ nur beim Raum; bei Lehrkräften und Fach trennt „|“ (z. B. „LAN | ILK“)
        let raeume = Wertzeile.bausteine(einmal(daten.aktuelleRaeume), durchgestrichen: false, offenAlsPlakette: false, pfeil: true)
        let lehrer = Wertzeile.bausteine(einmal(karte.lehrer), durchgestrichen: Kachelbau.istBereitschaftAbwesend(karte),
                                         offenAlsPlakette: lehrerOffen)
        var fachteile: [Wertbaustein] = []
        if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach, !istVeranstaltung {
            fachteile.append(.weg(alt))
            fachteile.append(.trenner)
        }
        fachteile.append(.text(fach))
        // Statuspille („PRÜFUNG“, „VERANSTALTUNG“) rechts neben dem Fach (Vorlage 140); ohne Fachzeile neben der Kopfzeile
        let fachSichtbar = !fach.isEmpty && !(istVeranstaltung && fach.caseInsensitiveCompare(titelzeile) == .orderedSame)
        var kopfteile = titelBausteine
        if let pille = karte.statusPille {
            if fachSichtbar {
                fachteile.append(.pille(pille))
            } else if !kopfteile.isEmpty {
                kopfteile.append(.pille(pille))
            }
        }
        // Veranstaltung ohne Fach und Klassen: oben gar nichts (auch keine allein stehende Pille)
        let pilleEinzeln = karte.statusPille != nil && !fachSichtbar && kopfteile.isEmpty && !istVeranstaltung
        return VStack(alignment: .leading, spacing: 0) {
            if !kopfteile.isEmpty {
                Wertzeile(bausteine: kopfteile, schrift: Detailmass.gross, farbe: Farben.detailText,
                          kuerzenAb: Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            if fachSichtbar {
                Wertzeile(bausteine: fachteile, schrift: Detailmass.gross, farbe: Farben.detailText)
                    .frame(height: Detailmass.zeile + 4)
            }
            if pilleEinzeln, let pille = karte.statusPille {
                Statuspille(text: pille)
                    .padding(.vertical, 4)
            }
            if !raeume.isEmpty {
                Wertzeile(bausteine: raeume, schrift: Detailmass.klein, kuerzenAb: Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            if !lehrer.isEmpty {
                Wertzeile(bausteine: lehrer, schrift: Detailmass.klein, kuerzenAb: Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            if !karte.abwesendeKlassen.isEmpty {
                Wertzeile(bausteine: [.text("\(karte.abwesendeKlassen.joined(separator: ", ")) – \(karte.abwesendText.isEmpty ? "abwesend" : karte.abwesendText)")],
                          schrift: Detailmass.klein)
                    .frame(height: Detailmass.zeile)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    // MARK: - Liste

    /// Reihenfolge wie in der Vorlage: „Findet online statt“ ganz oben, Raum, Vertretungstext bzw. Information, Lehrstoff,
    /// Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen.
    private var zeilen: some View {
        VStack(spacing: 0) {
            if kollegium && mitUnterricht && !istVeranstaltung {
                onlineZeile
                // „Findet online statt“: Link zum Online-Unterricht (Lehrkräfte mit Schreibrecht bearbeiten ihn)
                if daten.istOnline && daten.darf {
                    OnlineLinkZeile()
                }
            }
            if !istVeranstaltung && daten.istOnline && !(kollegium && daten.darf) {
                beitretenZeile
            }
            // Prüfung: eigene Zeile mit dem Fach als Untertitel (Vorlage 140)
            if karte.istPruefung && !karte.istEntfall {
                link(.pruefung, Detailzeile(symbol: "SymbolPruefung", titel: "Prüfung", untertitel: pruefungsUntertitel))
            }
            // Verlegung: Ursprung → Ziel mit Datum und Uhrzeit (ohne Verlegungszeichen)
            if let richtung = karte.verlegung, !istVeranstaltung {
                verlegungZeile(richtung)
            }
            if kollegium && daten.raumAenderbar && !istVeranstaltung {
                RaumZeile(raeume: daten.aktuelleRaeume, ziel: $raumZiel)
            }
            if istVeranstaltung {
                if !veranstaltungsInfo.isEmpty {
                    link(.info, Detailzeile(symbol: "SymbolInfo", titel: "Information zur Stunde", untertitel: veranstaltungsInfo))
                }
            } else if !detailText.isEmpty {
                // Information zur Stunde mit dem Info-Zeichen (info.svg), Vertretungstext mit dem Notizblatt; bloße
                // Art-Bezeichnungen wie „Sondereinsatz“ erscheinen nicht; Zusatzstunde: Plakette
                // „Zusätzl. Unterrichtsstunde“ im Kopf, dazu nur ein manuell eingegebener Vertretungstext
                link(.text, Detailzeile(symbol: textTitel == "Information zur Stunde" ? "SymbolInfo" : "SymbolNotiz",
                                        titel: textTitel, untertitel: detailText))
            }
            if mitUnterricht && (kollegium || !(daten.eintrag?.thema ?? "").isEmpty) {
                let thema = daten.eintrag?.thema ?? ""
                // Einzelstunde ohne eigenen Lehrstoff: der Lehrstoff des ganzen Blocks gilt (grau, nur lesend)
                let geerbt = daten.geerbterEintrag?.thema ?? ""
                link(.lehrstoff, Detailzeile(symbol: "SymbolLehrstoff", titel: "Lehrstoff",
                                             untertitel: thema.isEmpty && !geerbt.isEmpty ? "Block: \(geerbt)" : thema) {
                    if !thema.isEmpty {
                        Image(systemName: "checkmark.circle")
                            .font(.system(size: 21.5, weight: .light))
                            .foregroundStyle(Farben.gedaempft)
                            .padding(.trailing, 4)
                            .accessibilityLabel("Lehrstoff eingetragen")
                    }
                })
            }
            if kollegium && mitUnterricht {
                link(.schueler, Detailzeile(symbol: "SymbolSchueler", titel: "Schüler*innen") {
                    if daten.anwesenheit != nil || daten.geladen {
                        SchuelerPlakette(zahl: daten.abwesendAnzahl, kontrolliert: daten.anwesenheit?.kontrolliert == true)
                            .padding(.trailing, 4)
                    }
                })
            }
            if mitUnterricht {
                link(.hausaufgaben, Detailzeile(symbol: "SymbolHausaufgaben", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgaben.map { $0.liste.count } ?? (daten.geladen ? 0 : nil)))
            }
            if kollegium && !daten.klassen.isEmpty && !istVeranstaltung {
                link(.klassenbuch, Detailzeile(symbol: "SymbolKlassenbuch", titel: "Klassenbucheinträge",
                                               zahl: daten.geladen ? daten.vermerke.count : nil))
            }
            if (kollegium && mitUnterricht) || !daten.notizSchueler.isEmpty {
                link(.notizSchueler, Detailzeile(symbol: "SymbolNotiz", titel: "Notizen für Schüler*innen", untertitel: daten.notizSchueler))
            }
            // Notizen für Lehrkräfte sehen (und bearbeiten) nur Lehrkräfte
            if kollegium && (mitUnterricht || !daten.notizLehrer.isEmpty) {
                link(.notizLehrer, Detailzeile(symbol: "SymbolNotiz", titel: "Notizen für Lehrkräfte", untertitel: daten.notizLehrer))
            }
            if karte.hausaufgabeFaellig {
                Detailzeile(symbol: "SymbolHausaufgaben", titel: "Zu dieser Stunde ist eine Hausaufgabe fällig", pfeil: false)
            }
        }
    }

    /// Menüpunkt ohne Trennlinie (wie in der Vorlage).
    private func link<R: View>(_ seite: Detailseite, _ inhalt: Detailzeile<R>) -> some View {
        NavigationLink(value: seite) {
            inhalt
        }
        .buttonStyle(ZeilenKnopfStil())
    }

    /// „Findet online statt“ – speicherbar für Lehrkräfte mit Schreibrecht (optimistisch, bei Fehler zurück).
    private var onlineZeile: some View {
        Detailzeile(symbol: "SymbolOnline", titel: "Findet online statt", pfeil: false) {
            Toggle("Findet online statt", isOn: Binding(
                get: { daten.istOnline },
                set: { neu in
                    Task {
                        do {
                            try await daten.onlineSetzen(neu)
                        } catch {
                            onlineFehler = zustand.fehlertext(error)
                        }
                    }
                }))
                .labelsHidden()
                .tint(Farben.gruen)
                .disabled(!daten.darf || !daten.geladen)
        }
    }

    /// Schüler*innen (und Lehrkräfte ohne Schreibrecht): „Online-Unterricht beitreten“ öffnet den Link; ohne Link nur der Hinweis.
    @ViewBuilder
    private var beitretenZeile: some View {
        if let url = Karte.onlineURL(daten.onlineLink) {
            Button {
                oeffneURL(url)
            } label: {
                Detailzeile(symbol: "SymbolOnline", titel: "Online-Unterricht beitreten", untertitel: url.host ?? daten.onlineLink) {
                    Image(systemName: "arrow.up.right.square")
                        .font(.system(size: 20, weight: .light))
                        .foregroundStyle(Farben.gruen)
                        .padding(.trailing, 4)
                }
            }
            .buttonStyle(ZeilenKnopfStil())
            .accessibilityHint("Öffnet den Link zum Online-Unterricht")
        } else {
            Detailzeile(symbol: "SymbolOnline", titel: "Findet online statt", untertitel: "Noch kein Link hinterlegt", pfeil: false)
        }
    }

    @ViewBuilder
    private func ziel(_ seite: Detailseite) -> some View {
        switch seite {
        case .text:
            DetailTextSeite(titel: textTitel, text: detailText, karte: karte)
        case .pruefung:
            DetailTextSeite(titel: "Prüfung", text: pruefungsText, karte: karte)
        case .info:
            DetailTextSeite(titel: "Information zur Stunde", text: veranstaltungsInfo, karte: karte)
        case .lehrstoff:
            NotizSeite(art: .lehrstoff)
        case .schueler:
            SchuelerSeite()
        case .hausaufgaben:
            HausaufgabenSeite()
        case .klassenbuch:
            KlassenbuchSeite()
        case .notizSchueler:
            NotizSeite(art: .schueler)
        case .notizLehrer:
            NotizSeite(art: .lehrer)
        }
    }
}
