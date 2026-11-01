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
    /// Richtung, aus der der Inhalt des neu gewählten Reiters hereingleitet (1 = von rechts, -1 = von links, 0 = keine)
    @State private var einflug: CGFloat = 0
    /// Gemeinsamer Raum für die Unterstreichung des aktiven Reiters (gleitet beim Wechsel zum neuen Reiter)
    @Namespace private var reiterRaum

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
                StundendetailInhalt(karte: karten[auswahl], typ: typ, karten: karten, auswahl: $auswahl,
                                    einflug: $einflug, reiterRaum: reiterRaum)
                    .id(karten[auswahl].id)
                    // Kopf und Reiter wechseln ohne Überblenden; der Inhalt gleitet selbst herein
                    .transition(.identity)
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
    @Binding var einflug: CGFloat
    let reiterRaum: Namespace.ID
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var systemSchliessen
    @Environment(\.blattSchliessen) private var blattSchliessen
    @Environment(\.blattBewegt) private var blattBewegt
    /// Systemblatt (iPad): Inhalt erst nach dem Einblenden aufbauen
    @State private var eingeblendet = false
    @Environment(\.openURL) private var oeffneURL
    @StateObject private var daten: Stundendaten
    @State private var onlineFehler: String? = nil
    /// Raumliste („Ersetzen“ bzw. „Raum hinzufügen“) aus der Raumzeile
    @State private var raumZiel: RaumZiel? = nil
    /// Als Unterblatt geöffnete Unterseite (iPhone)
    @State private var unterseite: Detailseite? = nil
    /// Waagrechtes Wischen zwischen parallelen Stunden: Richtung (zu Beginn festgelegt), Verschiebung des Inhalts
    @State private var wischWaagrecht: Bool? = nil
    @State private var wischVersatz: CGFloat = 0
    @State private var wechselt = false
    @State private var inhaltBreite: CGFloat = 0
    @GestureState private var wischt = false
    /// Hereingleiten nach einem Reiterwechsel (Anteil der Breite, 0 = an seinem Platz)
    @State private var einflugVersatz: CGFloat

    init(karte: Karte, typ: String, karten: [Karte], auswahl: Binding<Int>,
         einflug: Binding<CGFloat> = .constant(0), reiterRaum: Namespace.ID) {
        self.karte = karte
        self.typ = typ
        self.karten = karten
        _auswahl = auswahl
        _einflug = einflug
        self.reiterRaum = reiterRaum
        _daten = StateObject(wrappedValue: Stundendaten(karte: karte))
        _einflugVersatz = State(initialValue: einflug.wrappedValue)
        // Reiterwechsel im offenen Blatt: nicht erneut auf das Einblenden warten
        _eingeblendet = State(initialValue: einflug.wrappedValue != 0)
    }

    /// Randloses iPhone-Blatt mit eigener Animation schließen, sonst das Systemblatt.
    private func schliessen() {
        if let blattSchliessen { blattSchliessen() } else { systemSchliessen() }
    }

    /// Hinterlegte Fachfarbe der Stunde (leer bzw. Weiß = keine; nicht bei Entfall – dort steht der rote Balken).
    private var fachfarbe: Color? {
        let hex = karte.farbe.trimmingCharacters(in: .whitespaces)
        guard !karte.istEntfall, !hex.isEmpty, !["#FFFFFF", "FFFFFF", "#FFF"].contains(hex.uppercased()) else { return nil }
        return Color(hex: hex)
    }

    /// Ladebild statt Inhalt: solange geladen wird und während das Blatt ein- oder ausfährt (wie im Original –
    /// die schwere Liste wird erst danach aufgebaut, Öffnen und Schließen laufen dadurch flüssiger).
    private var zeigtSkelett: Bool {
        !daten.geladen || (blattBewegt ?? !eingeblendet)
    }

    private var kollegium: Bool { zustand.istKollegium }
    private var mitUnterricht: Bool { karte.unterricht != nil }
    private var istVeranstaltung: Bool { karte.istVeranstaltung }

    var body: some View {
        VStack(spacing: 0) {
            // Kein Ladebalken in den Details (Wunsch); solange geladen wird, zeigt das Blatt wie im Original nur das
            // runde X und darunter graue Platzhalterbalken (Detailskelett)
            Blattkopf(symbol: "xmark", symbolText: "Schließen", aktion: { schliessen() }, mitte: {
                datumsMenue
                    .opacity(zeigtSkelett ? 0 : 1)
            }, rechts: {
                weitereAktionen
                    .opacity(zeigtSkelett ? 0 : 1)
                    .allowsHitTesting(!zeigtSkelett)
            })
            if karten.count > 1 {
                reiterleiste
            }
            // Vorlage: Balken in der Fachfarbe direkt unter dem Kopf (4 pt) – nur wenn für das Fach eine Farbe hinterlegt ist
            if let farbe = fachfarbe, !zeigtSkelett {
                Rectangle()
                    .fill(farbe)
                    .frame(height: 4)
                    .accessibilityHidden(true)
            }
            if zeigtSkelett {
                Detailskelett()
                    .transition(.opacity)
            } else {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    // Vorlage: erste Zeile (Versalhöhe) 25 pt unter Kopf bzw. Reiterleiste, Trennlinie 22 pt unter der
                    // Grundlinie der letzten Zeile
                    kopfblock
                        .padding(.horizontal, Detailmass.rand)
                        .padding(.top, 16.5)
                        .padding(.bottom, 11.67)
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
                            .fill(Farben.detailLinie)
                            .frame(height: 1)
                    }
                    zeilen
                        .padding(.top, 11.5)
                        .padding(.bottom, 24)
                }
            }
            .scrollIndicators(.hidden)
            .scrollDisabled(wischWaagrecht == true)
            // Mehrere Stunden zur selben Zeit: waagrecht wischen wechselt den Reiter (der Inhalt folgt dem Finger)
            .offset(x: wischVersatz)
            .visualEffect { [einflugVersatz] inhalt, geometrie in
                inhalt.offset(x: einflugVersatz * geometrie.size.width)
            }
            .background {
                GeometryReader { g in
                    Color.clear
                        .onAppear { inhaltBreite = g.size.width }
                        .onChange(of: g.size.width) { _, neu in inhaltBreite = neu }
                }
            }
            .clipped()
            .simultaneousGesture(reiterWischen, including: karten.count > 1 ? .all : .subviews)
            .onChange(of: wischt) { _, neu in
                // Geste abgebrochen (z. B. vom senkrechten Scrollen übernommen): zurückfedern
                guard !neu else { return }
                wischWaagrecht = nil
                if !wechselt && wischVersatz != 0 {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.88)) { wischVersatz = 0 }
                }
            }
            .onAppear {
                guard einflugVersatz != 0 else { return }
                einflug = 0
                withAnimation(.spring(response: 0.32, dampingFraction: 0.9)) { einflugVersatz = 0 }
            }
            }
        }
        // Inhalt blendet über das Ladebild ein; zurück zum Ladebild (beim Schließen) ohne Überblenden
        .animation(zeigtSkelett ? nil : .easeOut(duration: 0.18), value: zeigtSkelett)
        .task {
            guard blattBewegt == nil, !eingeblendet else { return }
            // Systemblatt: nach dem Einblenden (≈ 0,35 s) den Inhalt aufbauen
            try? await Task.sleep(for: .milliseconds(350))
            eingeblendet = true
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
        // iPhone: Lehrstoff, Schüler*innen, Hausaufgaben … öffnen als Unterblatt über den Details (wie im Original,
        // 10 pt tiefer); iPad: Unterseite im festen Detailblatt
        .detailPraesentation(item: $unterseite, breit: false) { seite in
            NavigationStack {
                ziel(seite)
                    .environmentObject(daten)
                    .environmentObject(zustand)
                    .environment(\.unterblatt, true)
            }
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

    /// „Do. | 05.11.26 ⌄“ mit Uhrzeit darunter. Vorlage a0555239: das Glasmenü „Stundenauswahl“ (grauer Titel, Häkchen
    /// links) wählt bei Doppel- bzw. Blockstunden, ob Einträge für die ganze „Blockstunde“ oder nur eine Einzelstunde
    /// („11:30 - 12:15“) gelten; bei mehreren gleichzeitigen Stunden folgen diese. Ohne Auswahl kein Menü und kein Pfeil.
    @ViewBuilder
    private var datumsMenue: some View {
        if mitBereichswahl || karten.count > 1 {
            Menu {
                if mitBereichswahl {
                    Section("Stundenauswahl") {
                        Picker("Stundenauswahl", selection: bereichBinding) {
                            Text("Blockstunde").tag(0)
                            ForEach(daten.blockStunden) { s in
                                Text(zeitspanne(s)).tag(s.stunde)
                            }
                        }
                        .pickerStyle(.inline)
                        .labelsHidden()
                    }
                }
                if karten.count > 1 {
                    Section("Stunden zur selben Zeit") {
                        Picker("Stunden zur selben Zeit", selection: Binding(get: { auswahl }, set: { reiterWechseln($0) })) {
                            ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                                Text(reitertitel(eintrag.element)).tag(eintrag.offset)
                            }
                        }
                        .pickerStyle(.inline)
                        .labelsHidden()
                    }
                }
            } label: {
                Blatttitel(titel: datumstitel, untertitel: zeittitel, pfeil: true)
                    .contentShape(Rectangle())
            }
            // Häkchen im Menü schwarz bzw. weiß je nach Modus (nicht orange)
            .tint(Color.primary)
            .accessibilityLabel("\(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")), \(zeittitel)")
        } else {
            Blatttitel(titel: datumstitel, untertitel: zeittitel, pfeil: false)
                .accessibilityElement(children: .combine)
                .accessibilityLabel("\(Datumsformat.anzeige(karte.datum, "EEEE, d. MMMM yyyy")), \(zeittitel)")
        }
    }

    /// Zeile einer Einzelstunde in der Stundenauswahl: „11:30 - 12:15“ (ohne Zeiten „3. Stunde“).
    private func zeitspanne(_ s: Einzelstundeninfo) -> String {
        s.beginn.isEmpty ? "\(s.stunde). Stunde" : "\(Detailmass.uhrzeit(s.beginn)) - \(Detailmass.uhrzeit(s.ende))"
    }

    @ViewBuilder
    private var weitereAktionen: some View {
        // Auch bei Veranstaltungen (wie bei normalen Stunden) – ohne Platzhalter („---“, „???“)
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

    private var bereichBinding: Binding<Int> {
        Binding(get: { daten.einzelstunde ?? 0 }, set: { neu in
            Task { await daten.bereichWaehlen(neu == 0 ? nil : neu, kollegium: kollegium) }
        })
    }

    // MARK: - Reiter

    /// Waagrechtes Wischen mit Richtungssperre (wie beim Blättern im Plan): links → nächster Reiter, rechts → vorheriger;
    /// am ersten bzw. letzten Reiter federt der Inhalt gedämpft zurück.
    private var reiterWischen: some Gesture {
        DragGesture(minimumDistance: 12)
            .updating($wischt) { _, status, _ in status = true }
            .onChanged { wert in
                guard !wechselt else { return }
                if wischWaagrecht == nil {
                    let dx = abs(wert.translation.width)
                    let dy = abs(wert.translation.height)
                    guard dx > 8 || dy > 8 else { return }
                    wischWaagrecht = dx > dy * 1.3
                }
                guard wischWaagrecht == true else { return }
                let dx = wert.translation.width
                let hatNachbar = dx < 0 ? auswahl < karten.count - 1 : auswahl > 0
                wischVersatz = hatNachbar ? dx : dx * 0.25
            }
            .onEnded { wert in
                let war = wischWaagrecht
                wischWaagrecht = nil
                guard war == true, !wechselt else { return }
                let w = max(inhaltBreite, 1)
                let dx = wert.translation.width
                let voraus = wert.predictedEndTranslation.width
                if (dx < -w * 0.22 || voraus < -w * 0.5) && auswahl < karten.count - 1 {
                    reiterWechseln(auswahl + 1)
                } else if (dx > w * 0.22 || voraus > w * 0.5) && auswahl > 0 {
                    reiterWechseln(auswahl - 1)
                } else {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.88)) { wischVersatz = 0 }
                }
            }
    }

    /// Reiter wechseln (Antippen, Wischen, Menü): der Inhalt gleitet zur Seite hinaus, der neue von der anderen Seite
    /// herein; die Unterstreichung gleitet zum neuen Reiter.
    private func reiterWechseln(_ neu: Int) {
        guard neu != auswahl, karten.indices.contains(neu), !wechselt else { return }
        let vor: CGFloat = neu > auswahl ? 1 : -1
        guard inhaltBreite > 0 else {
            auswahl = neu
            return
        }
        wechselt = true
        withAnimation(.easeIn(duration: 0.14)) {
            wischVersatz = -vor * inhaltBreite
        } completion: {
            einflug = vor
            // Gleiche Kennung (kein neuer Inhalt): wieder an den Platz
            wischVersatz = 0
            wechselt = false
            withAnimation(.spring(response: 0.3, dampingFraction: 0.9)) { auswahl = neu }
        }
    }

    private func reitertitel(_ k: Karte) -> String {
        // Veranstaltung: die Bezeichnung steht nur unten in „Information zur Stunde“, nicht im Reiter (wie die PWA)
        if k.istVeranstaltung { return "Veranstaltung" }
        if k.istZusatzstunde { return "Zusätzliche Unterrichtsstunde" }
        if !k.fach.isEmpty { return k.fach }
        if !k.anzeigeText.isEmpty { return k.anzeigeText }
        return k.anzeigeArt.isEmpty ? "Stunde" : k.anzeigeArt
    }

    /// Reiterleiste in eigener Zeile unter dem Kopf genau nach Vorlage (133): hellgraue Zeile, waagrecht scrollbare
    /// Text-Reiter (Regular, aktiv Medium), nur unter dem aktiven eine Unterstreichung (dunkelgrau bzw. im Dunkeln weiß)
    /// etwas breiter als der Text; der Reitertext wird nie durchgestrichen (auch nicht bei Entfall). Der aktive Reiter
    /// wird beim Wechsel in den sichtbaren Bereich gerollt.
    private var reiterleiste: some View {
        // Vorlagen 167f94e6 (dunkel) und e40685da (hell): Leiste 50 pt (hell #f3f4f6, dunkel #323d45), Reiter 17 pt
        // Semibold (gewählt dunkel bzw. weiß, sonst grau), unter dem gewählten ein 4 pt dicker Strich bündig am
        // unteren Rand, je 8,3 pt breiter als der Text; Text 27,7 pt auseinander
        ScrollViewReader { leser in
            ScrollView(.horizontal) {
                HStack(spacing: 0) {
                    ForEach(Array(karten.enumerated()), id: \.offset) { eintrag in
                        let gewaehlt = eintrag.offset == auswahl
                        Button {
                            reiterWechseln(eintrag.offset)
                        } label: {
                            Text(reitertitel(eintrag.element))
                                .font(.system(size: 17, weight: .semibold))
                                .foregroundStyle(gewaehlt ? Farben.detailText : Farben.reiterText)
                                .lineLimit(1)
                                .padding(.horizontal, 13.83)
                                .frame(height: 50)
                                .overlay(alignment: .bottom) {
                                    if gewaehlt {
                                        Rectangle()
                                            .fill(Farben.reiterStrich)
                                            .frame(height: 4)
                                            .padding(.horizontal, 5.5)
                                            .matchedGeometryEffect(id: "aktiverDetailReiter", in: reiterRaum)
                                    }
                                }
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(OhneTippStil())
                        .id(eintrag.offset)
                        .accessibilityAddTraits(gewaehlt ? .isSelected : [])
                    }
                }
                .padding(.horizontal, 10.5)
            }
            .scrollIndicators(.hidden)
            .onAppear { leser.scrollTo(auswahl, anchor: .center) }
            .onChange(of: auswahl) { _, neu in
                withAnimation(.easeInOut(duration: 0.2)) { leser.scrollTo(neu, anchor: .center) }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .frame(height: 50)
        .background(Farben.reiterGrund)
    }

    /// Vertretungstext bzw. Information der Stunde (Zusatzstunde: nur manuell eingegebene Texte).
    private var detailText: String {
        karte.istZusatzstunde ? karte.zusatzText : karte.anzeigeText
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
                for k in [w.alt ?? "", w.k] where !k.isEmpty && !Kachelbau.platzhalter.contains(k) {
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
    @_optimize(none)
    private var titelzeile: String {
        Self.titelzeileBerechnen(klassen: karte.klassen, gruppe: karte.gruppe, veranstaltung: istVeranstaltung,
                                 titel: karte.titel)
    }

    /// Ohne Optimierung übersetzt (Absturz des Optimierers beim Archivieren, siehe `kopfzeile`).
    @_optimize(none)
    private static func titelzeileBerechnen(klassen: [Wert], gruppe: String, veranstaltung: Bool, titel: String) -> String {
        var teile: [String] = []
        for w in klassen {
            let t = anzeigename(w)
            if !t.isEmpty && !enthaelt(teile, t) { teile.append(t) }
        }
        if !gruppe.isEmpty { teile.append(gruppe) }
        if !teile.isEmpty { return teile.joined(separator: " | ") }
        if veranstaltung { return "" }
        return titel
    }

    /// Werte ohne Leere und ohne Doppelte (Groß-/Kleinschreibung egal), Reihenfolge bleibt.
    private func ohneDoppelte(_ werte: [String]) -> [String] {
        var ergebnis: [String] = []
        for w in werte where !w.isEmpty && !ergebnis.contains(where: { $0.caseInsensitiveCompare(w) == .orderedSame }) {
            ergebnis.append(w)
        }
        return ergebnis
    }

    /// Veranstaltung: jede Lehrkraft bzw. jeder Raum nur einmal; ohne Lehrkräfte kein Platzhalter („---“, „???“).
    private func einmal(_ werte: [Wert]) -> [Wert] {
        guard istVeranstaltung else { return werte }
        var ergebnis: [Wert] = []
        for w in werte {
            let t = w.k.isEmpty ? (w.alt ?? "") : w.k
            if t.isEmpty || Kachelbau.platzhalter.contains(t) || ergebnis.contains(where: { ($0.k.isEmpty ? ($0.alt ?? "") : $0.k).caseInsensitiveCompare(t) == .orderedSame }) { continue }
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

    /// Abwesende Klassen einer Stunde, die für die übrigen Klassen stattfindet (beim Entfall leer).
    private var abwesendeKlassenSichtbar: [String] {
        if karte.istEntfall { return [] }
        return karte.abwesendeKlassen
    }

    /// Kopfzeile „Klasse | Gruppe“: eigene Klasse(n), anwesende Mitklassen als Text, fehlende bzw. abwesende Klassen
    /// durchgestrichen; Trennstrich grau. Veranstaltung ohne Klassen: keine Kopfzeile.
    @_optimize(none)
    private var titelBausteine: [Wertbaustein] {
        Self.kopfzeile(klassen: karte.klassen, mitklassen: karte.mitklassen, gruppe: karte.gruppe,
                       abwesend: abwesendeKlassenSichtbar, titelzeile: titelzeile)
    }

    /// Baut die Kopfzeile auf. Ohne Optimierung übersetzt: der Optimierer (CopyPropagation) stürzte beim Archivieren an
    /// dieser Funktion ab; sie läuft nur einmal je Detailansicht, die Geschwindigkeit spielt keine Rolle.
    @_optimize(none)
    private static func kopfzeile(klassen: [Wert], mitklassen: [Wert], gruppe: String, abwesend: [String],
                                  titelzeile: String) -> [Wertbaustein] {
        var werte: [Wertbaustein] = []
        if mitklassen.isEmpty && abwesend.isEmpty {
            if !titelzeile.isEmpty {
                for teil in titelzeile.components(separatedBy: " | ") {
                    werte.append(.text(teil))
                }
            }
        } else {
            var gesehen: [String] = []
            for w in klassen {
                let t = anzeigename(w)
                if t.isEmpty || enthaelt(gesehen, t) { continue }
                gesehen.append(t)
                if enthaelt(abwesend, t) {
                    werte.append(.weg(t))
                } else {
                    werte.append(.text(t))
                }
            }
            for w in mitklassen {
                if !w.k.isEmpty {
                    werte.append(.text(w.k))
                } else if let alt = w.alt, !alt.isEmpty {
                    werte.append(.weg(alt))
                }
            }
            if !gruppe.isEmpty { werte.append(.text(gruppe)) }
        }
        var teile: [Wertbaustein] = []
        for w in werte {
            if !teile.isEmpty { teile.append(.trenner) }
            teile.append(w)
        }
        return teile
    }

    @_optimize(none)
    private static func anzeigename(_ w: Wert) -> String {
        if !w.k.isEmpty { return w.k }
        return w.alt ?? ""
    }

    @_optimize(none)
    private static func enthaelt(_ liste: [String], _ wert: String) -> Bool {
        for e in liste where e.caseInsensitiveCompare(wert) == .orderedSame { return true }
        return false
    }

    /// Kopfblock genau nach Vorlage (Original 117/118): „Klasse | Gruppe“ und Fach groß (22 pt, schwarz), Raum und
    /// Lehrkraft 17 pt grau, je Zeile 31 pt hoch. Nur Text, keine Plaketten und keine Durchstreichung des Ganzen
    /// (Entfall zeigt der rote Balken): Änderungen als „alt → neu“ mit durchgestrichenem altem Wert, fehlende Werte
    /// durchgestrichen. Jede Angabe steht in genau einer Zeile, die sich bei vielen Werten waagrecht scrollen lässt;
    /// sehr viele Werte (z. B. alle Lehrkräfte oder Klassen) werden auf „… +N“ gekürzt und lassen sich aufklappen.
    /// Veranstaltung: oben keine Bezeichnung (die steht nur unten in „Information zur Stunde“) – ganz oben wie bei einer normalen
    /// Stunde die beteiligten Klassen (Titelzeile), dann die Pille „VERANSTALTUNG“, Raum und Lehrkräfte; die Fachzeile nur mit
    /// eingetragenem Fach. Keine Fachänderung (jeder Block nennt nur sein eigenes Fach).
    private var kopfblock: some View {
        let fach = istVeranstaltung ? veranstaltungsFach
                                    : (karte.fach.isEmpty ? karte.anzeigeArt : karte.fach)
        // Pfeil „alt → neu“ nur beim Raum; bei Lehrkräften und Fach trennt „|“ (z. B. „LAN | ILK“)
        let raeume = Wertzeile.bausteine(einmal(daten.aktuelleRaeume), durchgestrichen: false, offenAlsPlakette: false, pfeil: true)
        let lehrer = Wertzeile.bausteine(einmal(karte.lehrer), durchgestrichen: Kachelbau.istBereitschaftAbwesend(karte),
                                         offenAlsPlakette: lehrerOffen)
        var fachteile: [Wertbaustein] = []
        // keine Fachänderung in den Details: eine Fachänderung sind zwei Blöcke (Entfall mit dem bisherigen, neue Stunde mit dem
        // neuen Fach) – jeder nennt nur sein eigenes Fach
        fachteile.append(.text(fach))
        // Statuspille („PRÜFUNG“, „VERANSTALTUNG“) rechts neben dem Fach (Vorlage 140); ohne Fachzeile neben der Kopfzeile
        let fachSichtbar = !fach.isEmpty && !(istVeranstaltung && fach.caseInsensitiveCompare(titelzeile) == .orderedSame)
        // Veranstaltung: die beteiligten Klassen ganz oben als Titelzeile (wie bei normalen Stunden), darunter die Pille, Raum
        // und Lehrkräfte; ohne Klassen keine Titelzeile
        let kopfteile = titelBausteine
        // Vorlagen: Prüfung rechts neben dem Fach; Veranstaltung und zusätzliche Unterrichtsstunde in eigener Zeile
        // unter der Kopfzeile (Zusatzstunde ohne Fachzeile – das Fach steht im Reiter)
        let pilleEigeneZeile = istVeranstaltung || karte.istZusatzstunde
        let fachZeile = fachSichtbar && !istVeranstaltung && !(karte.istZusatzstunde && karten.count > 1)
        if let pille = karte.statusPille, fachZeile, !pilleEigeneZeile {
            fachteile.append(.pille(pille))
        }
        let pilleEinzeln = karte.statusPille != nil && (pilleEigeneZeile || !fachZeile)
        return VStack(alignment: .leading, spacing: 0) {
            if !kopfteile.isEmpty {
                Wertzeile(bausteine: kopfteile, schrift: Detailmass.gross, farbe: Farben.detailText,
                          kuerzenAb: Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            if pilleEinzeln, pilleEigeneZeile, let pille = karte.statusPille {
                Statuspille(text: pille)
                    .frame(height: Detailmass.zeile)
            }
            if fachZeile {
                Wertzeile(bausteine: fachteile, schrift: Detailmass.gross, farbe: Farben.detailText)
                    .frame(height: Detailmass.zeile)
            }
            if pilleEinzeln, !pilleEigeneZeile, let pille = karte.statusPille {
                Statuspille(text: pille)
                    .frame(height: Detailmass.zeile)
            }
            if !raeume.isEmpty {
                Wertzeile(bausteine: raeume, schrift: Detailmass.klein, kuerzenAb: Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            if !lehrer.isEmpty {
                // Veranstaltung: alle Lehrkräfte in einer Zeile („BN | BO | …“), waagrecht scrollbar statt „… +N“
                Wertzeile(bausteine: lehrer, schrift: Detailmass.klein, kuerzenAb: istVeranstaltung ? nil : Wertzeile.kuerzungsgrenze)
                    .frame(minHeight: Detailmass.zeile)
            }
            // Entfall (alle Klassen abwesend): Hinweis „Klasse abwesend“ (mit Grund); findet die Stunde für die übrigen Klassen
            // statt, steht die abwesende Klasse oben durchgestrichen – hier nur ein Grund, falls angegeben
            if !karte.abwesendeKlassen.isEmpty && (karte.istEntfall || karte.abwesendText.contains("–")) {
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
                        // Vorlage: Kreis mit Haken 24 pt, wie die Symbole
                        Image(systemName: "checkmark.circle")
                            .font(.system(size: 23.5, weight: .light))
                            .foregroundStyle(Farben.detailSymbol)
                            .padding(.trailing, 4.5)
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
            if mitUnterricht || istVeranstaltung {
                link(.hausaufgaben, Detailzeile(symbol: "SymbolHausaufgaben", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgabenAnzahl))
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
        }
    }

    /// Menüpunkt ohne Trennlinie und ohne Tipp-Animation (wie in der Vorlage): iPhone als Unterblatt, iPad als Unterseite.
    @ViewBuilder
    private func link<R: View>(_ seite: Detailseite, _ inhalt: Detailzeile<R>) -> some View {
        if Geraet.istPad {
            NavigationLink(value: seite) {
                inhalt
            }
            .buttonStyle(OhneTippStil())
        } else {
            Button {
                unterseite = seite
            } label: {
                inhalt
            }
            .buttonStyle(OhneTippStil())
        }
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
            .buttonStyle(OhneTippStil())
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

/// Ladebild der Stundendetails nach Vorlage (iPhone, 3 px/pt): sechs Gruppen aus je drei abgerundeten Balken über die
/// volle Breite (16 pt Rand), Balken 14,67 pt hoch, in der Gruppe 25 pt Abstand, von Gruppe zu Gruppe 30 pt;
/// der erste Balken 13 pt unter dem Kopf.
struct Detailskelett: View {
    static let farbe = Color(hell: 0xDADCE1, dunkel: 0x495F6E)

    var body: some View {
        VStack(alignment: .leading, spacing: 15.33) {
            ForEach(0..<6, id: \.self) { _ in
                VStack(spacing: 10.33) {
                    ForEach(0..<3, id: \.self) { _ in
                        RoundedRectangle(cornerRadius: 4, style: .circular)
                            .fill(Self.farbe)
                            .frame(height: 14.67)
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 16)
        .padding(.top, 13)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(Farben.blatt)
        .accessibilityElement()
        .accessibilityLabel("Wird geladen")
    }
}
