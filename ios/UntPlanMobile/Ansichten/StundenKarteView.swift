import SwiftUI

/// Kachel im Raster (Maße und Farben aus app.css, .card): Balken links in Fachfarbe, darunter die Zeilen.
/// Darstellung wie in UntPlanMobile:
/// - Vertretung grün mit grünem Rahmen, Entfall rosa mit rotem Rahmen und durchgestrichen, Klausur gelb.
/// - Vergangene Stunden: nur der farbige Rahmen entfällt (im Dunkeln die Fläche), reguläre Stunden werden leicht grau.
/// - Abgegebene Stunde und Bereitschaft der abwesenden Lehrkraft: durchsichtig mit feinen Eckklammern, Kürzel durchgestrichen,
///   neue Lehrkraft als grüne Plakette.
/// - Veranstaltung: hellgrünes Feld über den ganzen Zeitraum, oben Klassen (Klassenplan: Lehrkräfte) fett, darunter der Anlass.
/// - Aufsicht: schmale Pille in der Pause mit lila Balken; Vertretung (übernommen oder abgegeben) grün wie eine
///   Vertretungskarte, abwesendes Kürzel durchgestrichen, Vertretung als grüne Plakette.
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
    /// iPhone: Schrift 12 pt bei 14 pt Zeilenabstand wie im Original; iPad 12,4/14,3 (Woche wie Tag).
    private var kompakt: Bool { groessenklasse == .compact }
    private var schrift: CGFloat { kompakt ? 12 : 12.4 }
    /// Zeilenabstand einen Hauch enger als die Vorlage (14/14,3), damit untereinander stehende Plaketten dichter liegen.
    private var zeilenHoehe: CGFloat { kompakt ? 13.5 : 13.8 }

    // Balken und Textbeginn (Vorlage nachgemessen: Balken 4 pt breit, 4 pt vom Rand; Text 4 pt rechts vom Balken)
    static let balkenRand: CGFloat = 4
    static let balkenBreite: CGFloat = 4
    static let textAbstand: CGFloat = 4
    static var textLinks: CGFloat { balkenRand + balkenBreite + textAbstand }
    static let textOben: CGFloat = 4

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
        .shadow(color: a.schatten == .voll ? Farben.kartenSchatten : (a.schatten == .flach ? Farben.kartenSchattenFlach : .clear),
                radius: a.schatten == .keiner ? 0 : 1, y: a.schatten == .keiner ? 0 : 1)
        .shadow(color: a.schatten == .voll ? Farben.kartenSchattenWeich : .clear, radius: a.schatten == .voll ? 3 : 0, y: a.schatten == .voll ? 2 : 0)
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
            Eckklammern()
                .stroke(Farben.eckklammer, style: StrokeStyle(lineWidth: 1.6, lineCap: .round, lineJoin: .round))
                .padding(0.8)
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
                let unten = Self.textOben + CGFloat(eintrag.offset + 1) * zeilenHoehe
                let frei: CGFloat = breiteZeichen > 0 && unten > zeichenOben + 1 ? breiteZeichen + Self.zeichenRand - 1 : 0
                // Erste Zeile (oberer Wert) Semibold, alle weiteren Regular
                KachelzeileView(zeile: eintrag.element, aussehen: a, schrift: schrift, oben: eintrag.offset == 0,
                                titelFarbe: kachel.art == .termin && eintrag.offset == 0 ? Farben.terminTitel : nil,
                                zeilenHoehe: zeilenHoehe)
                    .padding(.trailing, frei)
                    .frame(height: zeilenHoehe, alignment: .leading)
            }
        }
    }

    /// Kantenlänge der Zeichen unten rechts (Hausaufgabe, Kürzung).
    static let zeichenGroesse: CGFloat = 14.5
    /// Kantenlänge des Info-Zeichens (Vorlage nachgemessen: abgerundetes Feld in Kartengrau ≈ 14,5 pt, darin die graue
    /// Scheibe mit ausgespartem „i“).
    static let infoGroesse: CGFloat = 14.5
    /// Kantenlänge des Online-Zeichens (online_lesson.svg, flach, ohne Feld).
    static let onlineGroesse: CGFloat = 15
    /// Abstand der Zeichen zum unteren und rechten Kartenrand (Vorlage: Info-Feld ≈ 3 pt vom Rand).
    static let zeichenRand: CGFloat = 3

    /// Höhe der Zeichen unten rechts.
    private var zeichenHoehe: CGFloat {
        let mitInfo = kachel.info && kachel.art != .termin
        let mitHausaufgabe = kachel.hausaufgabe && kachel.art != .bereitschaft && (!mitInfo || breite >= 96)
        if gekuerzt { return Self.zeichenGroesse }
        var h: CGFloat = 0
        if mitOnline { h = max(h, Self.onlineGroesse) }
        if mitHausaufgabe { h = max(h, Self.zeichenGroesse) }
        if mitInfo { h = max(h, Self.infoGroesse) }
        return h > 0 ? h : Self.zeichenGroesse
    }

    /// Videosymbol „findet online statt“ (unten rechts, vor Hausaufgabe und Info; auf schmalen Karten nur ohne weitere Zeichen).
    private var mitOnline: Bool {
        guard kachel.online && !gekuerzt else { return false }
        let weitere = (kachel.info && kachel.art != .termin) || (kachel.hausaufgabe && kachel.art != .bereitschaft)
        return !weitere || breite >= 80
    }

    /// Breite der Zeichen unten rechts (0 = keine) – wie in `zeichen(_:)`.
    private var zeichenBreite: CGFloat {
        let mitInfo = kachel.info && kachel.art != .termin
        let mitHausaufgabe = kachel.hausaufgabe && kachel.art != .bereitschaft
        if gekuerzt { return Self.zeichenGroesse }
        var summe: CGFloat = 0
        if mitOnline { summe += Self.onlineGroesse }
        if mitHausaufgabe && (!mitInfo || breite >= 96) { summe += (summe > 0 ? 2 : 0) + Self.zeichenGroesse }
        if mitInfo { summe += (summe > 0 ? 2 : 0) + Self.infoGroesse }
        return summe
    }

    // MARK: - Zeichen unten rechts (Info, Hausaufgabe, Kürzung)

    @ViewBuilder
    private func zeichen(_ a: Kachelaussehen) -> some View {
        let mitInfo = kachel.info && kachel.art != .termin
        let mitHausaufgabe = kachel.hausaufgabe && kachel.art != .bereitschaft
        if gekuerzt {
            Kuerzungszeichen(gruen: kachel.art == .aenderung || kachel.art == .abgegeben)
        } else if mitInfo || mitHausaufgabe || mitOnline {
            HStack(alignment: .center, spacing: 2) {
                if mitOnline {
                    Onlinezeichen(groesse: Self.onlineGroesse)
                }
                if mitHausaufgabe && (!mitInfo || breite >= 96) {
                    Hausaufgabenzeichen(groesse: Self.zeichenGroesse)
                }
                if mitInfo {
                    Infozeichen(groesse: Self.infoGroesse)
                }
            }
        }
    }

    // MARK: - Pille (Aufsicht, ganztägiger Termin)

    private func pille(_ a: Kachelaussehen) -> some View {
        HStack(spacing: 6) {
            RoundedRectangle(cornerRadius: 2, style: .continuous)
                .fill(a.balken ?? Farben.aufsichtBalken)
                .frame(width: 4)
                .padding(.vertical, 3)
            // Immer der Ortsname – bei Vertretung kein Kürzel-Label auf der Karte (Details beim Antippen)
            Text(kachel.titel)
                .font(.system(size: gross ? 13.5 : 12.4, weight: .medium))
                .foregroundStyle(a.text)
                .lineLimit(1)
                .truncationMode(.tail)
            // Tagesansicht: keine Uhrzeiten auf der Aufsichtskarte (die Pause zeigt das Raster selbst)
            if gross && kachel.art != .aufsicht {
                Text("\(Rasterzeit.uhrzeit(kachel.beginn))–\(Rasterzeit.uhrzeit(kachel.ende))")
                    .font(.system(size: 12.5).monospacedDigit())
                    .foregroundStyle(a.text.opacity(0.75))
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            // Vertretung (übernommen bzw. abgegeben): grünes „…“ ganz rechts
            if kachel.art == .aufsicht && kachel.aufsichtsstand != .regulaer {
                Kuerzungszeichen(gruen: true, waagrecht: true)
                    .layoutPriority(1)
            }
        }
        .padding(.leading, 4)
        .padding(.trailing, 3)
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
        case .abgegeben: return a.lehrer.isEmpty || a.lehrer == "???" ? "Vertretung offen" : "Vertretung: \(a.lehrer)"
        case .vertretung: return (a.alt ?? "").isEmpty ? "Vertretung" : "Vertretung für \(a.alt ?? "")"
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

    /// Alle Teile nebeneinander ohne Umbruch. Kürzbar: jeder Teil darf mit „…“ enden, vordere Teile haben Vorrang.
    @ViewBuilder
    private func teileReihe(kuerzbar: Bool) -> some View {
        let anzahl = zeile.teile.count
        HStack(spacing: 0) {
            ForEach(Array(zeile.teile.enumerated()), id: \.offset) { eintrag in
                HStack(spacing: 0) {
                    teil(eintrag.element, kuerzbar: kuerzbar)
                    if !eintrag.element.trenner.isEmpty {
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

    /// Deckkraft des grauen Balkens vergangener Stunden (Original: #78828a auf #313d44 bzw. #d8dfe3 auf #f2f4f5).
    static let balkenVergangen: Double = 0.5

    init(_ k: Kachel, dunkel: Bool) {
        let v = k.vergangen
        let fach = Color(hex: k.balkenHex) ?? Farben.karteBalken
        // Vergangen (Original, nachgemessen hell und dunkel): grauer Balken zur Hälfte deckend (die Fläche scheint
        // durch – auf getönten Karten wirkt er getönt); Schrift hell etwas weicher, dunkel unverändert
        balken = v ? Farben.karteBalken : fach
        balkenDeckkraft = v ? Self.balkenVergangen : 1
        text = v ? Farben.karteTextVergangen : Farben.karteText
        switch k.art {
        case .normal:
            hintergrund = v ? Farben.karteVergangen : Farben.karte
            schatten = v ? .flach : .voll
        case .aenderung:
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        case .entfall:
            farbigeKarte(flaeche: Farben.entfallHintergrund, vergangeneFlaeche: Farben.entfallVergangen, rand: Farben.entfallRand,
                         vergangen: v, dunkel: dunkel, schatten: false)
            text = v ? Farben.karteTextVergangen : Farben.entfallText
            durchgestrichen = true
        case .klausur:
            farbigeKarte(flaeche: Farben.pruefungHintergrund, vergangeneFlaeche: Farben.pruefungVergangen, rand: Farben.pruefungRand,
                         vergangen: v, dunkel: dunkel, schatten: false)
        case .termin:
            hintergrund = Farben.veranstaltungHintergrund
            rand = Farben.terminRand
            randBreite = 1.5
            schatten = .keiner
        case .abgegeben:
            hintergrund = .clear
            schatten = .keiner
            ecken = true
            radius = 0
            text = Farben.karteText
            balkenDeckkraft = 1
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
            text = v ? Farben.gedaempft : Farben.karteText
        case .veranstaltung:
            // Wie die grüne Vertretungskarte: hell #e3fff2 mit 2-pt-Rahmen #019169, dunkel dunkelgrün mit türkisem Rahmen;
            // vergangen fällt nur der Rahmen weg
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        case .ganztagTermin:
            hintergrund = Farben.veranstaltungHintergrund
            rand = Farben.terminRand
            randBreite = 1
            schatten = .keiner
            balken = Farben.aenderungRand
            text = Farben.terminTitel
        case .aufsicht:
            balken = Farben.aufsichtBalken
            balkenDeckkraft = 1
            switch k.aufsichtsstand {
            case .regulaer:
                hintergrund = v ? Farben.karteVergangen : Farben.karte
                schatten = .flach
            case .vertretung, .abgegeben:
                // Vertretung (übernommen bzw. abgegeben) wie eine Vertretungskarte: nicht verblasst, grüne Fläche mit
                // grünem Rahmen; Kürzel stehen als Plakette bzw. durchgestrichen in der Pille
                farbigeKarte(flaeche: Farben.aenderungHintergrund, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: false)
                randBreite = 1.5
                text = Farben.aenderungText
            }
        }
        // Reine Raumänderung: wie eine Änderung/Vertretung (grüne Fläche mit grünem Rahmen), der neue Raum als grüne
        // Plakette – nie gelb/braun (das ist ausschließlich Prüfungen bzw. Klausuren vorbehalten)
        if k.raumAenderung && (k.art == .aenderung || k.art == .normal) {
            farbigeKarte(flaeche: Farben.aenderungHintergrund, vergangeneFlaeche: Farben.aenderungVergangen, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: true)
            text = v ? Farben.karteTextVergangen : Farben.aenderungText
        }
    }

    /// Farbige Kachel (Vertretung, Raumänderung, Entfall, Klausur, Veranstaltung) wie im Original: künftig farbige Fläche
    /// mit Rahmen. Vergangen hell: farbige Fläche ohne Rahmen; vergangen dunkel: neutrale Schieferfläche, der farbige
    /// Rahmen bleibt. Plaketten bleiben grün.
    private mutating func farbigeKarte(flaeche: Color, vergangeneFlaeche: Color? = nil, rand farbe: Color, vergangen: Bool, dunkel: Bool,
                                       schatten mitSchatten: Bool) {
        // Vergangen: Deckkraft des Balkens bleibt wie gesetzt (grau halb deckend; Aufsicht: lila voll)
        if !vergangen { balkenDeckkraft = 1 }
        if vergangen && dunkel {
            hintergrund = Farben.karteVergangen
            rand = farbe
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

/// Zählstreifen (.parcount): Zahl der Stunden hinter der vorderen Kachel – rot bei Entfall, grün bei Vertretung, sonst grau.
struct ZaehlstreifenView: View {
    let streifen: Zaehlstreifen

    var body: some View {
        let farben = farbe
        Text("\(streifen.kacheln.count)")
            .font(.system(size: 11, weight: .medium))
            .foregroundStyle(farben.text)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(farben.flaeche, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
            .overlay {
                if let rand = farben.rand {
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .strokeBorder(rand, lineWidth: 1.5)
                }
            }
            .accessibilityLabel("\(streifen.kacheln.count) weitere Stunden")
            .accessibilityAddTraits(.isButton)
    }

    private var farbe: (flaeche: Color, text: Color, rand: Color?) {
        if streifen.entfall { return (Farben.entfallRand, .white, nil) }
        if streifen.aenderung { return (Farben.zaehlerAenderungFlaeche, Farben.zaehlerAenderungText, Farben.aenderungRand) }
        return (Farben.zaehlerFlaeche, Farben.zaehlerText, nil)
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

    var body: some View {
        Image(systemName: "ellipsis")
            .font(.system(size: 8, weight: .medium))
            .rotationEffect(.degrees(waagrecht ? 0 : 90))
            .foregroundStyle(gruen ? Farben.gruenSchrift : Farben.infoScheibe)
            .frame(width: 14.5, height: 14.5)
            .background(gruen ? Farben.gruen : Farben.infoFeld, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

/// Hinterlegter Wert (.badge), an der Vorlage nachgemessen (Raum „A103“: 13,7 pt hoch bei 14 pt Zeile, links/rechts
/// je ≈ 2,2 pt, oben/unten je ≈ 2,6 pt um die Versalhöhe): Höhe knapp unter dem Zeilenabstand, damit untereinander
/// stehende Plaketten nicht aneinanderstoßen; Radius 2, grün.
struct Plakettenwert: View {
    let text: String
    var farbe: Color = Farben.gruen
    var durchgestrichen: Bool = false
    /// Darf der Text innerhalb der Plakette mit „…“ gekürzt werden (sonst volle Breite)?
    var kuerzbar: Bool = false
    /// Zeilenabstand der Karte: die Plakette bleibt 0,8 pt darunter (höchstens 13,7 pt wie im Original)
    var zeilenHoehe: CGFloat = 14

    var body: some View {
        // Schriftstärke wie die Zeile (oben Semibold, sonst Regular)
        Text(text)
            .strikethrough(durchgestrichen, color: Farben.gruenSchrift)
            .foregroundStyle(Farben.gruenSchrift)
            .lineLimit(1)
            .truncationMode(.tail)
            .padding(.horizontal, 2.2)
            .frame(height: min(13.7, zeilenHoehe - 0.8))
            .background(farbe, in: RoundedRectangle(cornerRadius: 2, style: .continuous))
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
