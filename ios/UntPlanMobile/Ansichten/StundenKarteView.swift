import SwiftUI

/// Kachel im Raster (Maße und Farben aus app.css, .card): Balken links in Fachfarbe, darunter die Zeilen.
/// Darstellung wie in UntPlanMobile:
/// - Vertretung grün mit grünem Rahmen, Entfall rosa mit rotem Rahmen und durchgestrichen, Klausur gelb.
/// - Vergangene Stunden: nur der farbige Rahmen entfällt (im Dunkeln die Fläche), reguläre Stunden werden leicht grau;
///   hell ist der Balken links halb deckend.
/// - Abgegebene Stunde und Bereitschaft der abwesenden Lehrkraft: durchsichtig mit feinen Eckklammern, Kürzel durchgestrichen,
///   neue Lehrkraft als grüne Plakette.
/// - Veranstaltung: hellgrünes Feld über den ganzen Zeitraum, oben Klassen (Klassenplan: Lehrkräfte) fett, darunter der Anlass.
/// - Aufsicht: schmale Pille in der Pause mit lila Balken, darauf immer nur der Ort; Vertretung (übernommen: grün,
///   abgegeben: durchsichtig mit Eckklammern) mit dem grünen „…“ ganz rechts – keine Kürzel, keine Plaketten (wer vertritt,
///   zeigen die Details); entfallene Aufsicht wie eine entfallene Stunde (rosa, roter Rahmen, Ort durchgestrichen).
/// - Erste Zeile Semibold (auch in Plaketten), weitere Zeilen Regular; Info-Zeichen (info.svg), 4 pt vom Rand unten rechts.
/// Maße wie im Original (nachgemessen an den Vorlagen, Woche und Tag gleich): Balken 4 pt breit, oben, links und unten je
/// 4 pt vom Kartenrand; Text 4 pt rechts vom Balken (Glyphe ≈ 12,8 pt von der Kante) und 4 pt unter der Oberkante; Schrift
/// 12 pt bei 14 pt Zeilenabstand (iPad 12,4/14,3). Jede Zeile ist einzeilig – was nicht passt, endet mit „…“, auch
/// innerhalb grüner Plaketten; Zeilen auf Höhe der Zeichen unten rechts (Info, Hausaufgabe) lassen dafür Platz.
/// In der Tagesansicht (gross) dieselbe Karte ohne zusätzliche Angaben, nur breiter.
struct KachelView: View {
    let kachel: Kachel
    var gross: Bool = false
    var breite: CGFloat = 100
    var hoehe: CGFloat = 60

    @Environment(\.colorScheme) private var schema
    @Environment(\.horizontalSizeClass) private var groessenklasse

    private var dunkel: Bool { schema == .dark }
    /// iPhone: Schrift 12,3 pt bei 14,33 pt Zeilenabstand wie im Original (Vorlage in voller Auflösung, 3 px/pt: Versalhöhe
    /// 26 px, Zeilenabstand 43 px); iPad 12,4/14,3 (Woche wie Tag).
    private var kompakt: Bool { groessenklasse == .compact }
    private var schrift: CGFloat { kompakt ? 12.3 : 12.4 }
    /// Zeilenabstand wie im Original nachgemessen (iPhone 33 px bei 2,34 px/pt ≈ 14 pt, iPad 14,3 pt) – die Labels darin
    /// 13,7 pt hoch (etwas größer als in Sprint 26, dort 12,7 pt bei 13,5 pt Zeilenabstand).
    private var zeilenHoehe: CGFloat { kompakt ? 14.33 : 14.3 }

    // Balken und Textbeginn (Vorlage nachgemessen: Balken 4 pt breit, 4 pt vom Rand; Text 4 pt rechts vom Balken)
    static let balkenRand: CGFloat = 4
    static let balkenBreite: CGFloat = 4
    /// Abgeglichen mit Bildschirmfotos der App (175/176) gegen das Original (173, gleicher Maßstab 2,289 px/pt): mit 4 pt
    /// stand die Schrift 0,44 pt (1 px) weiter rechts als im Original – daher 3,56 pt (Glyphe „B“ 13,11 pt von der Kante)
    static let textAbstand: CGFloat = 3.56
    static var textLinks: CGFloat { balkenRand + balkenBreite + textAbstand }
    static let textOben: CGFloat = 4
    /// Untere Zeilen (ab der zweiten) sitzen um diesen Betrag höher (nur optisch, ohne Einfluss auf das Layout).
    static let untereZeilenHub: CGFloat = 0

    var body: some View {
        let a = Kachelaussehen(kachel, dunkel: dunkel)
        Group {
            if kachel.art == .aufsicht || kachel.art == .ganztagTermin {
                pille(a)
            } else {
                karte(a)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(barrierefreiText)
        .accessibilityAddTraits(kachel.antippbar ? .isButton : [])
    }

    // MARK: - Karte

    private func karte(_ a: Kachelaussehen) -> some View {
        ZStack(alignment: .topLeading) {
            if let balken = a.balken {
                RoundedRectangle(cornerRadius: 2, style: .continuous)
                    .fill(balken)
                    .opacity(a.balkenDeckkraft)
                    .frame(width: Self.balkenBreite)
                    .padding(.vertical, Self.balkenRand)
                    .padding(.leading, Self.balkenRand)
                    .frame(maxHeight: .infinity, alignment: .topLeading)
            }
            inhalt(a)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(a.hintergrund, in: RoundedRectangle(cornerRadius: a.radius, style: .continuous))
        .clipShape(RoundedRectangle(cornerRadius: a.radius, style: .continuous))
        // Die ganze Fläche ist antippbar – auch bei durchsichtigen Karten (vergangen, abgegeben, Bereitschaft)
        .contentShape(Rectangle())
        .overlay { rahmen(a) }
        // Hell: feine Kante seitlich und unten, künftige Stunden mit weichem Schatten nach unten (Vorlage nachgemessen);
        // dunkel unverändert
        .background {
            if !dunkel && a.schatten != .keiner {
                Kartenschatten(radius: a.radius, voll: a.schatten == .voll)
            }
        }
        // Dunkel ohne Schatten (Vorlagen 4cc11c37/e450e6b4: Rastergrund direkt neben der Karte)
        // Zeichen unten rechts erst nach dem Kartenschatten: flach, ohne eigenen Schatten (auch auf durchsichtigen
        // Karten), innerhalb der Karte 4 pt vom unteren und rechten Rand; auf sehr kleinen Karten verkleinert bzw.
        // ausgeblendet, damit nichts über Rand oder Balken hinausragt
        .overlay(alignment: .bottomTrailing) {
            let faktor = zeichenFaktor
            if faktor > 0 {
                zeichen(a)
                    .scaleEffect(faktor, anchor: .bottomTrailing)
                    .padding(.trailing, Self.zeichenRand)
                    .padding(.bottom, Self.zeichenRand)
                    .allowsHitTesting(false)
            }
        }
        // Oben rechts nie ein Zeichen (ausdrücklicher Wunsch): die Server-Marker zeigt nur das Web
    }

    /// Verkleinerung der Zeichen unten rechts, falls die Karte zu niedrig bzw. zu schmal ist (0 = ausblenden).
    /// Oben bleibt der Rand von 4 pt frei, links reichen die Zeichen nie bis an den Balken.
    private var zeichenFaktor: CGFloat {
        let b = zeichenBreite
        guard b > 0 else { return 0 }
        let platzHoch = hoehe - 2 * Self.zeichenRand
        let platzBreit = breite - Self.zeichenRand - (Self.balkenRand + Self.balkenBreite + 2)
        let f = min(1, platzHoch / max(zeichenHoehe, 1), platzBreit / b)
        return f < 0.6 ? 0 : f
    }

    @ViewBuilder
    private func rahmen(_ a: Kachelaussehen) -> some View {
        if a.ecken {
            // Vorlage 178 (2,289 px/pt): Schenkel ≈ 7,5 pt, Linie ≈ 0,7 pt, Rundung ≈ 1,5 pt, an der Kartenkante
            Eckklammern(laenge: 7.5, radius: 2.5)
                .stroke(Farben.eckklammer, style: StrokeStyle(lineWidth: 0.7, lineCap: .round, lineJoin: .round))
                .padding(0.35)
        } else if let rand = a.rand {
            if a.gestrichelt {
                RoundedRectangle(cornerRadius: a.radius, style: .continuous)
                    .strokeBorder(rand, style: StrokeStyle(lineWidth: a.randBreite, dash: [5, 3]))
            } else {
                RoundedRectangle(cornerRadius: a.radius, style: .continuous)
                    .strokeBorder(rand, lineWidth: a.randBreite)
            }
        }
    }

    /// Wie viele Zeilen passen in die Kachel?
    private var platzZeilen: Int {
        max(1, Int((hoehe - Self.textOben - 4) / zeilenHoehe))
    }

    private var gekuerzt: Bool {
        kachel.art != .bereitschaft && kachel.art != .veranstaltung && kachel.zeilen.count > platzZeilen
    }

    /// Sichtbare Zeilen: passen nicht alle, bleibt die erste Zeile stehen und Zeilen mit wichtigen Änderungen gehen vor
    /// (Raumänderung mit Plakette, Kopplung mit abwesender bzw. vertretener Lehrkraft, fehlende Koppelklasse); die
    /// Reihenfolge bleibt erhalten.
    private var sichtbareZeilen: [Kachelzeile] {
        let alle = kachel.zeilen
        guard kachel.art != .veranstaltung, alle.count > platzZeilen else { return alle }
        var gewaehlt: Set<Int> = [0]
        for i in alle.indices.dropFirst() where gewaehlt.count < platzZeilen && Self.wichtig(alle[i]) {
            gewaehlt.insert(i)
        }
        for i in alle.indices where gewaehlt.count < platzZeilen {
            gewaehlt.insert(i)
        }
        return alle.indices.filter { gewaehlt.contains($0) }.map { alle[$0] }
    }

    private static func wichtig(_ z: Kachelzeile) -> Bool {
        switch z.schluessel {
        case "raum":
            return z.teile.contains { $0.stil == .plakette }
        case "lehrer", "klasse":
            return z.teile.count > 1 && z.teile.contains { $0.stil != .text }
        default:
            return false
        }
    }

    @ViewBuilder
    private func inhalt(_ a: Kachelaussehen) -> some View {
        if kachel.art == .bereitschaft {
            Text(kachel.titel)
                .font(.system(size: schrift))
                .foregroundStyle(a.text)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .minimumScaleFactor(0.8)
                .padding(4)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            // Woche wie Tag: dieselben Zeilen, in der Tagesansicht nur breiter (keine zusätzlichen Angaben)
            zeilenBlock(a)
                .padding(.leading, Self.textLinks)
                .padding(.top, Self.textOben)
                .padding(.trailing, 3)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }

    private func zeilenBlock(_ a: Kachelaussehen) -> some View {
        let sichtbar = sichtbareZeilen
        // Zeichen unten rechts (4 pt vom Rand): Zeilen auf ihrer Höhe lassen rechts Platz, damit nichts verdeckt wird
        let breiteZeichen = zeichenBreite
        let zeichenOben = hoehe - Self.zeichenRand - zeichenHoehe
        return VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(sichtbar.enumerated()), id: \.offset) { eintrag in
                let oben = Self.textOben + CGFloat(eintrag.offset) * zeilenHoehe
                let unten = oben + zeilenHoehe
                let freiUnten: CGFloat = breiteZeichen > 0 && unten > zeichenOben + 1 ? breiteZeichen + Self.zeichenRand - 1 : 0
                let frei = freiUnten
                // Erste Zeile (oberer Wert) Semibold, alle weiteren Regular
                KachelzeileView(zeile: eintrag.element, aussehen: a, schrift: schrift, oben: eintrag.offset == 0,
                                titelFarbe: kachel.art == .termin && eintrag.offset == 0 ? Farben.terminTitel : nil,
                                zeilenHoehe: zeilenHoehe)
                    .padding(.trailing, frei)
                    .frame(height: zeilenHoehe, alignment: .leading)
                    // Untere Zeilen (Labels) einen Hauch höher, die erste Zeile bleibt
                    .offset(y: eintrag.offset == 0 ? 0 : -Self.untereZeilenHub)
            }
        }
    }

    /// Kantenlänge der Zeichen unten rechts (Hausaufgabe, Kürzung).
    static let zeichenGroesse: CGFloat = 14
    /// Kantenlänge des Info-Zeichens (Vorlage nachgemessen: abgerundetes Feld in Kartengrau ≈ 14,5 pt, darin die graue
    /// Scheibe mit ausgespartem „i“).
    static let infoGroesse: CGFloat = 14
    /// Kantenlänge des Online-Zeichens (online_lesson.svg, flach, ohne Feld).
    static let onlineGroesse: CGFloat = 15
    /// Abstand der Zeichen zum unteren und rechten Kartenrand (Vorlage in voller Auflösung: Feld 14 pt, 4 pt vom Rand).
    static let zeichenRand: CGFloat = 4

    /// Zeichen unten rechts.
    enum Eckzeichen: Hashable {
        case online
        case hausaufgabe
        case info

        var groesse: CGFloat {
            switch self {
            case .online: return KachelView.onlineGroesse
            case .info: return KachelView.infoGroesse
            default: return KachelView.zeichenGroesse
            }
        }
    }

    /// Sichtbare Zeichen unten rechts in der Anzeigereihenfolge Online, Hausaufgabe, Info (ein Verlegungszeichen gibt es in
    /// iOS nicht). Reicht die Breite nicht für alle (links bleibt der Balken frei), fällt zuerst die Hausaufgabe weg, dann das
    /// Online-Zeichen; Info bleibt am längsten stehen.
    private var eckzeichen: [Eckzeichen] {
        guard !gekuerzt else { return [] }
        var vorrang: [Eckzeichen] = []
        if kachel.info && kachel.art != .termin { vorrang.append(.info) }
        if kachel.online { vorrang.append(.online) }
        if kachel.hausaufgabe && kachel.art != .bereitschaft { vorrang.append(.hausaufgabe) }
        let platz = max(1, Int((breite - (Self.balkenRand + Self.balkenBreite + 2) - Self.zeichenRand + 2) / (Self.zeichenGroesse + 2)))
        let gewaehlt = Array(vorrang.prefix(platz))
        let reihenfolge: [(Eckzeichen) -> Bool] = [
            { if case .online = $0 { return true }; return false },
            { if case .hausaufgabe = $0 { return true }; return false },
            { if case .info = $0 { return true }; return false }
        ]
        return reihenfolge.compactMap { passt in gewaehlt.first(where: passt) }
    }

    /// Höhe der Zeichen unten rechts.
    private var zeichenHoehe: CGFloat {
        if gekuerzt { return Self.zeichenGroesse }
        return eckzeichen.map(\.groesse).max() ?? Self.zeichenGroesse
    }

    /// Breite der Zeichen unten rechts (0 = keine) – wie in `zeichen(_:)`.
    private var zeichenBreite: CGFloat {
        if gekuerzt { return Self.zeichenGroesse }
        let liste = eckzeichen
        guard !liste.isEmpty else { return 0 }
        return liste.map(\.groesse).reduce(0, +) + CGFloat(liste.count - 1) * 2
    }

    // MARK: - Zeichen unten rechts (Online, Hausaufgabe, Info, Kürzung)

    @ViewBuilder
    private func zeichen(_ a: Kachelaussehen) -> some View {
        if gekuerzt {
            Kuerzungszeichen(gruen: kachel.art == .aenderung || kachel.art == .abgegeben)
        } else {
            let liste = eckzeichen
            if !liste.isEmpty {
                HStack(alignment: .center, spacing: 2) {
                    ForEach(liste, id: \.self) { z in
                        switch z {
                        case .online: Onlinezeichen(groesse: z.groesse)
                        case .hausaufgabe: Hausaufgabenzeichen(groesse: z.groesse)
                        case .info: Infozeichen(groesse: z.groesse)
                        }
                    }
                }
            }
        }
    }

    // MARK: - Pille (Aufsicht, ganztägiger Termin)

    /// Pille (Aufsicht, ganztägiger Termin) nach Vorlage 178/179 (2,289 px/pt): Balken 3,5 pt breit, 4,4 pt vom Rand,
    /// oben und unten 0,7 pt Luft; Ort 11,5 pt Semibold 4,7 pt hinter dem Balken; Vertretung: grünes „⋮“-Feld 3,9 pt vom
    /// rechten Rand. Vor der 1. Stunde eine schmale Pille (10 pt hoch, Schrift 8 pt, grünes Feld 14 × 7,9 pt).
    private func pille(_ a: Kachelaussehen) -> some View {
        let schmal = kachel.vorErsterStunde
        return HStack(spacing: 4.7) {
            RoundedRectangle(cornerRadius: 1.75, style: .continuous)
                .fill(a.balken ?? Farben.aufsichtBalken)
                .opacity(a.balkenDeckkraft)
                .frame(width: 3.5)
                .padding(.vertical, schmal ? 1 : 0.7)
            // Immer nur der Ortsname (keine Kürzel, keine Plaketten); entfallene Aufsicht durchgestrichen
            Text(kachel.titel)
                .font(.system(size: schmal ? 8 : (gross ? 13.5 : 11.5), weight: .semibold))
                .strikethrough(a.durchgestrichen, color: a.text)
                .foregroundStyle(a.text)
                .lineLimit(1)
                .truncationMode(.tail)
            // Tagesansicht: keine Uhrzeiten auf der Aufsichtskarte (die Pause zeigt das Raster selbst)
            if gross && kachel.art != .aufsicht && !schmal {
                Text("\(Rasterzeit.uhrzeit(kachel.beginn))–\(Rasterzeit.uhrzeit(kachel.ende))")
                    .font(.system(size: 12.5).monospacedDigit())
                    .foregroundStyle(a.text.opacity(0.75))
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            // Vertretung (übernommen oder abgegeben): ganz rechts das grüne „⋮“ – Details beim Antippen
            if kachel.art == .aufsicht && (kachel.aufsichtsstand == .vertretung || kachel.aufsichtsstand == .abgegeben) {
                Kuerzungszeichen(gruen: true, waagrecht: schmal, breite: 14, hoehe: schmal ? 7.9 : 14)
                    .layoutPriority(1)
            }
        }
        .padding(.leading, 4.4)
        .padding(.trailing, schmal ? 3.9 : 3.9)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(a.hintergrund, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
        .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
        .contentShape(Rectangle())
        .overlay { rahmen(a) }
        .shadow(color: a.schatten == .keiner ? .clear : Farben.kartenSchattenFlach, radius: a.schatten == .keiner ? 0 : 1.5, y: a.schatten == .keiner ? 0 : 1)
    }

    private var aufsichtHinweis: String? {
        guard let a = kachel.aufsicht else { return nil }
        switch kachel.aufsichtsstand {
        case .abgegeben:
            guard a.lehrer.isEmpty || a.lehrer == "???" else { return "Vertretung: \(a.lehrer)" }
            return (a.grund ?? "").isEmpty ? "Vertretung offen" : "Vertretung offen, \(a.grund ?? "")"
        case .vertretung: return (a.alt ?? "").isEmpty ? "Vertretung" : "Vertretung für \(a.alt ?? "")"
        case .entfall: return (a.grund ?? "").isEmpty ? "Aufsicht entfällt" : "Aufsicht entfällt, \(a.grund ?? "")"
        case .regulaer: return nil
        }
    }

    private var barrierefreiText: String {
        var teile: [String] = []
        switch kachel.art {
        case .entfall: teile.append("Entfall")
        case .aenderung: teile.append("Änderung")
        case .abgegeben: teile.append("Abgegeben")
        case .bereitschaftAbwesend: teile.append("Bereitschaft, Lehrkraft abwesend")
        case .veranstaltung: teile.append("Veranstaltung")
        case .aufsicht: teile.append("Pausenaufsicht")
        case .klausur: teile.append("Klausur")
        default: break
        }
        teile.append(contentsOf: kachel.zeilen.map(\.text))
        if kachel.online { teile.append("findet online statt") }
        if let v = kachel.karte?.verlegungText { teile.append("verlegt: \(v)") }
        teile.append("\(Rasterzeit.uhrzeit(kachel.beginn)) bis \(Rasterzeit.uhrzeit(kachel.ende))")
        if let h = aufsichtHinweis { teile.append(h) }
        return teile.joined(separator: ", ")
    }
}

/// Eine Zeile der Kachel: Text, grüne Plaketten, durchgestrichene Werte. Immer genau eine Zeile – was nicht passt,
/// endet mit „…“ (vollständig in den Details): reiner Text wird am Ende gekürzt; bei Teilen kürzt zuerst der letzte
/// Teil, Plaketten kürzen ihren Text innerhalb der grünen Fläche.
struct KachelzeileView: View {
    let zeile: Kachelzeile
    let aussehen: Kachelaussehen
    let schrift: CGFloat
    /// Erste (obere) Zeile der Karte: Medium, sonst Regular
    var oben: Bool = false
    var titelFarbe: Color? = nil
    /// Zeilenabstand der Karte (Höhe der Plaketten bleibt knapp darunter)
    var zeilenHoehe: CGFloat = 14

    var body: some View {
        Group {
            if zeile.nurText && !aussehen.durchgestrichen {
                Text(zeile.text)
                    .lineLimit(1)
                    .truncationMode(.tail)
            } else {
                ViewThatFits(in: .horizontal) {
                    teileReihe(kuerzbar: false)
                    teileReihe(kuerzbar: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        // Obere Zeile (auch die grüne Plakette darin) Semibold (ausdrücklicher Wunsch), weitere Zeilen Regular
        .font(.system(size: schrift, weight: oben ? .semibold : .regular))
        .foregroundStyle(titelFarbe ?? aussehen.text)
    }

    /// Abstand zwischen einem Label und dem nächsten Wert (statt Komma bzw. Leerzeichen).
    static let labelAbstand: CGFloat = 3

    /// Erscheint der Teil als Label (grüne Fläche)? Auf entfallenen Karten gibt es keine Labels.
    private func istLabel(_ t: Kachelteil) -> Bool {
        !aussehen.durchgestrichen && (t.stil == .plakette || t.stil == .offen)
    }

    /// Trenner nach dem Teil: zwischen zwei schlichten Werten wie gehabt (Komma), neben einem Label nur ein kleiner
    /// Abstand – kein Komma.
    private func labelNachbar(_ i: Int) -> Bool {
        let teile = zeile.teile
        guard i + 1 < teile.count else { return false }
        return istLabel(teile[i]) || istLabel(teile[i + 1])
    }

    /// Alle Teile nebeneinander ohne Umbruch. Kürzbar: jeder Teil darf mit „…“ enden, vordere Teile haben Vorrang.
    @ViewBuilder
    private func teileReihe(kuerzbar: Bool) -> some View {
        let anzahl = zeile.teile.count
        HStack(spacing: 0) {
            ForEach(Array(zeile.teile.enumerated()), id: \.offset) { eintrag in
                HStack(spacing: 0) {
                    teil(eintrag.element, kuerzbar: kuerzbar)
                        // Label vorn: Fläche ragt nach links, die Schrift bleibt bündig mit den anderen Zeilen
                        .padding(.leading, eintrag.offset == 0 && istLabel(eintrag.element) ? -Plakettenwert.vorsprung : 0)
                    if labelNachbar(eintrag.offset) {
                        Color.clear
                            .frame(width: Self.labelAbstand, height: 1)
                    } else if !eintrag.element.trenner.isEmpty {
                        Text(eintrag.element.trenner)
                            .strikethrough(aussehen.durchgestrichen)
                            .fixedSize()
                    }
                }
                .layoutPriority(Double(anzahl - eintrag.offset))
            }
        }
        .lineLimit(1)
        .fixedSize(horizontal: !kuerzbar, vertical: false)
        .frame(maxWidth: kuerzbar ? .infinity : nil, alignment: .leading)
    }

    @ViewBuilder
    private func teil(_ t: Kachelteil, kuerzbar: Bool) -> some View {
        if aussehen.durchgestrichen {
            // Entfall: alles durchgestrichen, Plaketten ohne grüne Fläche
            Text(t.text)
                .strikethrough(true)
                .foregroundStyle(t.stil == .plakette ? Farben.entfallPlakettenText : aussehen.text)
                .truncationMode(.tail)
        } else {
            switch t.stil {
            case .text:
                Text(t.text)
                    .truncationMode(.tail)
            case .plakette:
                Plakettenwert(text: t.text, kuerzbar: kuerzbar, zeilenHoehe: zeilenHoehe)
            case .weg:
                // Wegfallender Wert (z. B. vertretene Lehrkraft): durchgestrichen, ohne grüne Fläche
                Text(t.text)
                    .strikethrough(true)
                    .truncationMode(.tail)
            case .offen:
                // Offene Vertretung: abwesende Lehrkraft durchgestrichen in grüner Plakette
                Plakettenwert(text: t.text, durchgestrichen: true, kuerzbar: kuerzbar, zeilenHoehe: zeilenHoehe)
            }
        }
    }
}

// MARK: - Aussehen

/// Fläche, Rahmen, Schatten und Schrift einer Kachel – Werte aus app.css (hell und dunkel).
struct Kachelaussehen {
    enum Schatten {
        case voll
        case flach
        case keiner
    }

    var hintergrund: Color = Farben.karte
    var rand: Color? = nil
    var randBreite: CGFloat = 2
    var gestrichelt = false
    var radius: CGFloat = 3
    var schatten: Schatten = .voll
    var ecken = false
    var text: Color = Farben.karteText
    var durchgestrichen = false
    var balken: Color? = Farben.karteBalken
    var balkenDeckkraft: Double = 1

    init(_ k: Kachel, dunkel: Bool) {
        let v = k.vergangen
        let fach = Color(hex: k.balkenHex) ?? Farben.karteBalken
        // Der Balken links bleibt in der Fachfarbe; vergangene Stunden mit halb deckendem Balken wie in den Vorlagen
        // (hell #d9dee2 auf #f3f5f7, dunkel #78828a auf #323c45). Schrift vergangener Stunden weicher (hell #3f525e,
        // dunkel #dbdfe5)
        balken = fach
        balkenDeckkraft = v ? 0.5 : 1
        text = v ? Farben.karteTextVergangen : Farben.karteText
        switch k.art {
        case .normal:
            hintergrund = v ? Farben.karteVergangen : Farben.karte
            schatten = v ? .flach : .voll
        case .aenderung:
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangenerRand: Farben.aenderungRandVergangen, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        case .entfall:
            // Wie in der Vorlage 3af53475 mit Kartenschatten (hell), Fläche #fff3f3, Rand 2 pt #df0046
            farbigeKarte(flaeche: Farben.entfallHintergrund, vergangeneFlaeche: Farben.entfallVergangen, rand: Farben.entfallRand, vergangenerRand: Farben.entfallRandVergangen,
                         vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.entfallText
            durchgestrichen = true
        case .klausur:
            // Wie in der Vorlage mit Schatten (dunkel ohnehin ohne)
            farbigeKarte(flaeche: Farben.pruefungHintergrund, vergangeneFlaeche: Farben.pruefungVergangen, rand: Farben.pruefungRand, vergangenerRand: Farben.pruefungRandVergangen,
                         vergangen: v, dunkel: dunkel, schatten: true)
        case .termin:
            hintergrund = Farben.veranstaltungHintergrund
            rand = Farben.terminRand
            randBreite = 1.5
            schatten = .keiner
        case .abgegeben:
            // Vorlagen 178/180: keine Fläche, kein Rahmen, nur die feinen Eckklammern; Balken und Schrift wie bei anderen
            // (vergangenen) Stunden
            hintergrund = .clear
            schatten = .keiner
            ecken = true
            radius = 0
            text = v ? Farben.karteTextVergangen : Farben.karteText
        case .bereitschaft:
            hintergrund = v ? .clear : Farben.karte
            rand = v ? Farben.bereitschaftRandVergangen : Farben.bereitschaftRand
            gestrichelt = true
            radius = 5
            schatten = .keiner
            balken = nil
            text = v ? Farben.gedaempft : Farben.bereitschaftText
        case .bereitschaftAbwesend:
            hintergrund = .clear
            schatten = .keiner
            ecken = true
            balken = Farben.bereitschaftBalken
            text = v ? Farben.karteTextVergangen : Farben.karteText
        case .veranstaltung:
            // Wie die grüne Vertretungskarte: hell #e3fff2 mit 2-pt-Rahmen #019169, dunkel dunkelgrün mit türkisem Rahmen;
            // vergangen fällt nur der Rahmen weg
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangenerRand: Farben.aenderungRandVergangen, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        case .ganztagTermin:
            hintergrund = Farben.veranstaltungHintergrund
            rand = Farben.terminRand
            randBreite = 1
            schatten = .keiner
            balken = Farben.aenderungRand
            text = Farben.terminTitel
        case .aufsicht:
            // Aufsicht wie eine Stundenkarte mit abwesender Lehrkraft (gleiche Logik und Optik); der Text verblasst auch
            // nach dem Ende der Aufsicht nicht
            balken = Farben.aufsichtBalken
            balkenDeckkraft = 1
            text = Farben.karteText
            switch k.aufsichtsstand {
            case .regulaer:
                hintergrund = v ? Farben.karteVergangen : Farben.karte
                schatten = .flach
            case .vertretung:
                // Eingesprungen: grüne Änderungskarte, abwesende Lehrkraft durchgestrichen, Vertretung als grüne Plakette
                farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen,
                             rand: Farben.aenderungRand, vergangenerRand: Farben.aenderungRandVergangen, vergangen: v, dunkel: dunkel, schatten: false)
                randBreite = 1.5
                text = Farben.aenderungText
            case .abgegeben:
                // Eigene Aufsicht übernimmt jemand anderes: wie die abgegebene Stunde durchsichtig mit Eckklammern
                hintergrund = .clear
                schatten = .keiner
                ecken = true
                radius = 0
            case .entfall:
                // Entfallene Aufsicht: wie eine entfallene Stunde (rosa, roter Rahmen, Ort durchgestrichen); Balken bleibt lila
                farbigeKarte(flaeche: Farben.entfallHintergrund, vergangeneFlaeche: Farben.entfallVergangen,
                             rand: Farben.entfallRand, vergangenerRand: Farben.entfallRandVergangen, vergangen: v, dunkel: dunkel, schatten: false)
                randBreite = 1.5
                text = v ? Farben.karteTextVergangen : Farben.entfallText
                durchgestrichen = true
            }
        }
        // Reine Raumänderung: wie eine Änderung/Vertretung (grüne Fläche mit grünem Rahmen), der neue Raum als grüne
        // Plakette – nie gelb/braun (das ist ausschließlich Prüfungen bzw. Klausuren vorbehalten)
        if k.raumAenderung && (k.art == .aenderung || k.art == .normal) {
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangenerRand: Farben.aenderungRandVergangen, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        }
    }

    /// Farbige Kachel (Vertretung, Raumänderung, Entfall, Klausur, Veranstaltung) wie im Original: künftig farbige Fläche
    /// mit Rahmen. Vergangen hell: farbige Fläche ohne Rahmen; vergangen dunkel: neutrale Schieferfläche, der farbige
    /// Rahmen bleibt. Plaketten bleiben grün.
    private mutating func farbigeKarte(flaeche: Color, vergangeneFlaeche: Color? = nil, rand farbe: Color,
                                       vergangenerRand: Color? = nil, vergangen: Bool, dunkel: Bool,
                                       schatten mitSchatten: Bool) {
        if vergangen && dunkel {
            // Dunkel vergangen: Schieferfläche, Rahmen in der dunkleren Vergangen-Farbe (grün #007d64, gelb #c57d00, rot #df0046)
            hintergrund = Farben.karteVergangen
            rand = vergangenerRand ?? farbe
            schatten = .keiner
        } else if vergangen {
            hintergrund = vergangeneFlaeche ?? flaeche
            rand = nil
            schatten = .flach
        } else {
            hintergrund = flaeche
            rand = farbe
            schatten = mitSchatten ? .voll : .keiner
        }
    }
}

// MARK: - Bausteine

/// Feine Eckklammern (.corners): die Stunde läuft, aber ohne die betrachtete Lehrkraft.
struct Eckklammern: Shape {
    var laenge: CGFloat = 13
    var radius: CGFloat = 4

    func path(in r: CGRect) -> Path {
        var p = Path()
        let l = min(laenge, r.width / 2, r.height / 2)
        let rd = min(radius, l)
        // oben links
        p.move(to: CGPoint(x: r.minX, y: r.minY + l))
        p.addLine(to: CGPoint(x: r.minX, y: r.minY + rd))
        p.addQuadCurve(to: CGPoint(x: r.minX + rd, y: r.minY), control: CGPoint(x: r.minX, y: r.minY))
        p.addLine(to: CGPoint(x: r.minX + l, y: r.minY))
        // oben rechts
        p.move(to: CGPoint(x: r.maxX - l, y: r.minY))
        p.addLine(to: CGPoint(x: r.maxX - rd, y: r.minY))
        p.addQuadCurve(to: CGPoint(x: r.maxX, y: r.minY + rd), control: CGPoint(x: r.maxX, y: r.minY))
        p.addLine(to: CGPoint(x: r.maxX, y: r.minY + l))
        // unten rechts
        p.move(to: CGPoint(x: r.maxX, y: r.maxY - l))
        p.addLine(to: CGPoint(x: r.maxX, y: r.maxY - rd))
        p.addQuadCurve(to: CGPoint(x: r.maxX - rd, y: r.maxY), control: CGPoint(x: r.maxX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.maxX - l, y: r.maxY))
        // unten links
        p.move(to: CGPoint(x: r.minX + l, y: r.maxY))
        p.addLine(to: CGPoint(x: r.minX + rd, y: r.maxY))
        p.addQuadCurve(to: CGPoint(x: r.minX, y: r.maxY - rd), control: CGPoint(x: r.minX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.minX, y: r.maxY - l))
        return p
    }
}

/// Schatten einer Karte im hellen Modus, an der Vorlage in voller Auflösung (3 px/pt, nach sRGB umgerechnet) nachgemessen
/// und angepasst: Farbe #1e2b33, zwei Lagen – 40 % mit 2 pt Weichzeichnung und 2 pt Versatz nach unten, dazu 20 % mit
/// 4 pt Weichzeichnung und 5 pt Versatz. Ergibt seitlich ≈ 26 % direkt an der Kante, nach ≈ 5 pt ausgelaufen; oben nur ein
/// Hauch (≈ 8 %); unten ≈ 34 % bei 2 pt, Ausläufer bis ≈ 12 pt. Vergangene Stunden (Vorlage 671170ed in voller
/// Auflösung): eine kurze Lage (68 %, 0,9 pt, Versatz 1,2 pt). Die Schatten werfen zwei deckende Flächen hinter der Karte – sie addieren sich nicht
/// wie verkettete `.shadow`-Modifikatoren.
struct Kartenschatten: View {
    let radius: CGFloat
    /// Künftige Stunde (voller Schatten) statt vergangener (kurzer Schatten)
    let voll: Bool

    var body: some View {
        let form = RoundedRectangle(cornerRadius: radius, style: .continuous)
        ZStack {
            if voll {
                form.fill(Color.white)
                    .shadow(color: Farben.kartenKante.opacity(0.20), radius: 4, x: 0, y: 5)
                form.fill(Color.white)
                    .shadow(color: Farben.kartenKante.opacity(0.40), radius: 2, x: 0, y: 2)
            } else {
                form.fill(Color.white)
                    .shadow(color: Farben.kartenKante.opacity(0.68), radius: 0.9, x: 0, y: 1.2)
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

/// Zählstreifen (.parcount): Zahl der Stunden hinter der vorderen Kachel – rot bei Entfall, grün bei Vertretung, sonst grau.
struct ZaehlstreifenView: View {
    let streifen: Zaehlstreifen
    @Environment(\.colorScheme) private var schema

    /// Vorlage 178/179 (2,289 px/pt): Streifen 12 pt breit, Ecken ≈ 1,5 pt, Fläche in der Farbe der verdeckten Stunde –
    /// Entfall #df0046, Vertretung #00916d (Rahmenfarbe der grünen Karte), Prüfung #9f532c, jeweils mit weißer Zahl;
    /// sonst wie eine Karte (weiß bzw. vergangen #f3f5f7) mit grauer Zahl (#3f525e); Zahl 11 pt mittig; Kartenschatten.
    var body: some View {
        let farben = farbe
        let form = RoundedRectangle(cornerRadius: 1.5, style: .continuous)
        Text("\(streifen.kacheln.count)")
            .font(.system(size: 11, weight: .regular))
            .foregroundStyle(farben.text)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(farben.flaeche, in: form)
            // Hell wie in der Vorlage mit demselben Schatten wie die Karten (dunkel ohne)
            .background {
                if schema != .dark { Kartenschatten(radius: 1.5, voll: !streifen.vergangen) }
            }
            .accessibilityLabel("\(streifen.kacheln.count) weitere Stunden")
            .accessibilityAddTraits(.isButton)
    }

    /// Vorlagen 178 (hell) und 180 (dunkel, vergangene Woche): farbige Streifen immer in der kräftigen Farbe (auch
    /// vergangen) – hell mit weißer, dunkel mit dunkler Zahl (#f03064 / #1fc0a0 / #ffb728).
    private var farbe: (flaeche: Color, text: Color) {
        let v = streifen.vergangen
        let ziffer = Color(hell: 0xFFFFFF, dunkel: 0x10161D)
        if streifen.entfall { return (Farben.entfallRand, ziffer) }
        if streifen.aenderung { return (Farben.aenderungRand, ziffer) }
        if streifen.klausur { return (Farben.pruefungRand, ziffer) }
        return (v ? Farben.karteVergangen : Farben.karte, v ? Farben.karteTextVergangen : Farben.karteText)
    }
}

/// Online-Zeichen auf der Karte: die Stunde findet online statt (online_lesson.svg, flach und grau, ohne Feld –
/// kein zusätzliches Raum-Label). Steht unten rechts nach derselben Logik wie Info und Hausaufgabe.
struct Onlinezeichen: View {
    var groesse: CGFloat = KachelView.onlineGroesse

    var body: some View {
        Image("KartenOnline")
            .renderingMode(.original)
            .resizable()
            .interpolation(.high)
            .scaledToFit()
            .frame(width: groesse, height: groesse)
            .accessibilityIgnoresInvertColors(true)
            .accessibilityHidden(true)
    }
}

/// Kürzungszeichen (.card__cut): drei Punkte – auf Vertretungen grün. `waagrecht`: „…“ (Vertretung auf der Aufsichtskarte).
struct Kuerzungszeichen: View {
    var gruen = false
    var waagrecht = false
    /// Feldgröße (Vorlage 178: 13,5–14 pt Quadrat; schmale Pille vor der 1. Stunde 14 × 7,9 pt)
    var breite: CGFloat = KachelView.zeichenGroesse
    var hoehe: CGFloat = KachelView.zeichenGroesse
    @Environment(\.colorScheme) private var schema

    var body: some View {
        Group {
            if schema == .dark {
                Image(systemName: "ellipsis")
                    .font(.system(size: 8, weight: .medium))
                    .rotationEffect(.degrees(waagrecht ? 0 : 90))
            } else {
                // Hell wie in der Vorlage: drei Punkte (Ø 1,67 pt, Abstand 3 pt von Mitte zu Mitte) in #808e9b
                let punkt = Circle().frame(width: 1.67, height: 1.67)
                if waagrecht {
                    HStack(spacing: 1.33) { punkt; punkt; punkt }
                } else {
                    VStack(spacing: 1.33) { punkt; punkt; punkt }
                }
            }
        }
        .foregroundStyle(gruen ? Farben.gruenSchrift : Farben.infoScheibe)
        .frame(width: breite, height: hoehe)
        .background(gruen ? Farben.gruen : Farben.infoFeld, in: RoundedRectangle(cornerRadius: 2, style: .continuous))
    }
}

/// Hinterlegter Wert (.badge), am Original nachgemessen (iPhone, 2,34 px/pt): „LN“ 47 × 32 px = 20,1 × 13,7 pt,
/// „A103“ 72 × 32 px = 30,8 × 13,7 pt bei 12 pt Schrift und 14,1 pt Zeilenabstand; Innenabstand links/rechts 2,2 pt,
/// Versalhöhe mittig; Radius ≈ 2 pt (Ecke 3 px). iPad (161: „ASL“ 20 px bei 1,46 px/pt) ebenso 13,7 pt.
/// Untereinander stehende Labels bleiben durch den Zeilenabstand 0,3 pt (iPad 0,6 pt) getrennt.
struct Plakettenwert: View {
    /// Innenabstand links und rechts der Schrift in der Fläche
    static let innen: CGFloat = 1.9
    static let innenRechts: CGFloat = 2.2
    /// Steht das Label vorn, beginnt die Fläche so weit vor dem Textanfang der übrigen Zeilen (Original 173 gegen App
    /// 176: Fläche 9,8 pt von der Kartenkante, Label-Schrift 0,5 pt vor der normalen Schrift)
    static let vorsprung: CGFloat = 1.85
    /// Ecken: an vier Labels der 3-px/pt-Vorlagen (LN, A103, GIL, WE) nachgemessen – Verlauf der Kantenglättung passt
    /// zu einer kontinuierlichen Rundung (gemessen ≈ 2,85 pt, auf Wunsch leicht zurückgenommen auf 2,4 pt)
    static let radius: CGFloat = 2.4
    let text: String
    var farbe: Color = Farben.gruen
    var durchgestrichen: Bool = false
    /// Darf der Text innerhalb der Plakette mit „…“ gekürzt werden (sonst volle Breite)?
    var kuerzbar: Bool = false
    /// Zeilenabstand der Karte: die Plakette bleibt mindestens 0,3 pt darunter (höchstens 13,7 pt wie im Original)
    var zeilenHoehe: CGFloat = 14

    var body: some View {
        // Schriftstärke wie die Zeile (oben Semibold, sonst Regular)
        // Vorlagen in voller Auflösung (4cc11c37 „LN“/„A103“, 171 „GIL“, dc1bcf55 „WE“; 3 px/pt): Fläche 14 pt hoch
        // (13,67–14,33), Ecken 1,33–1,5 pt, gleiche Schriftgröße wie der Text (Versalhöhe 8,67 pt), Schrift mittig
        // (0,15 pt höher). Die Schrift bleibt bündig mit den anderen Zeilen; die Fläche beginnt 2,15 pt vor dem
        // Schrift-Ursprung und endet 2,45 pt hinter der Schrift (Glyphenabstand links 2,3–3, rechts 3–3,3 pt).
        Text(text)
            .strikethrough(durchgestrichen, color: Farben.gruenSchrift)
            .foregroundStyle(Farben.gruenSchrift)
            .lineLimit(1)
            .truncationMode(.tail)
            .offset(y: -0.15)
            .padding(.leading, Self.innen)
            .padding(.trailing, Self.innenRechts)
            .frame(height: min(14, zeilenHoehe - 0.33))
            .background(farbe, in: RoundedRectangle(cornerRadius: Self.radius, style: .continuous))
            .fixedSize(horizontal: !kuerzbar, vertical: true)
    }
}

/// Info-Zeichen (.card__info) wie im Original nachgemessen: abgerundetes Feld in Kartengrau (hell #f3f5f7, dunkel
/// #313d44, auch auf grünen, roten und gelben Karten), darin eine gefüllte graue Scheibe (#818e9c) mit ausgespartem
/// Serifen-„i“; flach, ohne Schatten. Immer als Originalbild – keine Einfärbung und keine Umkehr durch „Farben umkehren“.
struct Infozeichen: View {
    var groesse: CGFloat = KachelView.infoGroesse

    var body: some View {
        Image("KartenInfo")
            .renderingMode(.original)
            .resizable()
            .interpolation(.high)
            .scaledToFit()
            .frame(width: groesse, height: groesse)
            .accessibilityIgnoresInvertColors(true)
            .accessibilityHidden(true)
    }
}

/// Zeichen „Hausaufgabe fällig“ auf den Karten (tt-homework-icon.svg mit weißem Feld, flach; dunkel eigene Variante).
struct Hausaufgabenzeichen: View {
    var groesse: CGFloat = KachelView.zeichenGroesse

    var body: some View {
        Image("KartenHausaufgabe")
            .resizable()
            .interpolation(.high)
            .scaledToFit()
            .frame(width: groesse, height: groesse)
            .accessibilityHidden(true)
    }
}
