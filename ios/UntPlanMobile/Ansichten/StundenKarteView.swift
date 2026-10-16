import SwiftUI

/// Kachel im Raster (Maße und Farben aus app.css, .card): Balken links in Fachfarbe, darunter die Zeilen.
/// Darstellung wie in UntPlanMobile:
/// - Vertretung grün mit grünem Rahmen, Entfall rosa mit rotem Rahmen und durchgestrichen, Klausur gelb.
/// - Vergangene Stunden: nur der farbige Rahmen entfällt (im Dunkeln die Fläche), reguläre Stunden werden leicht grau.
/// - Abgegebene Stunde und Bereitschaft der abwesenden Lehrkraft: durchsichtig mit feinen Eckklammern, Kürzel durchgestrichen,
///   neue Lehrkraft als grüne Plakette.
/// - Veranstaltung: hellgrünes Feld über den ganzen Zeitraum, oben Klassen (Klassenplan: Lehrkräfte) fett, darunter der Anlass.
/// - Aufsicht: schmale Pille in der Pause mit lila Balken.
/// In der Tagesansicht (gross) größer, mit Uhrzeit, Fach-Langname, Hinweis und Status daneben.
struct KachelView: View {
    let kachel: Kachel
    var gross: Bool = false
    var breite: CGFloat = 100
    var hoehe: CGFloat = 60

    @Environment(\.colorScheme) private var schema
    @Environment(\.horizontalSizeClass) private var groessenklasse

    private var dunkel: Bool { schema == .dark }
    /// iPhone-Woche: schmale Spalten, Schrift 11 pt (Tagesansicht bleibt groß).
    private var kompakt: Bool { groessenklasse == .compact && !gross }
    private var schrift: CGFloat { gross ? 15 : (kompakt ? 11 : 12.4) }
    private var zeilenHoehe: CGFloat { gross ? 19 : (kompakt ? 13 : 14.3) }

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
                    .frame(width: gross ? 4 : (kompakt ? 3 : 3.5))
                    .padding(.top, 4.7)
                    .padding(.bottom, 4)
                    .padding(.leading, kompakt ? 3 : 4)
                    .frame(maxHeight: .infinity, alignment: .topLeading)
            }
            inhalt(a)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(a.hintergrund, in: RoundedRectangle(cornerRadius: a.radius, style: .continuous))
        .clipShape(RoundedRectangle(cornerRadius: a.radius, style: .continuous))
        .overlay { rahmen(a) }
        .overlay(alignment: .bottomTrailing) {
            zeichen(a)
                .padding(2)
        }
        .shadow(color: a.schatten == .voll ? Farben.kartenSchatten : (a.schatten == .flach ? Farben.kartenSchattenFlach : .clear),
                radius: a.schatten == .keiner ? 0 : 1, y: a.schatten == .keiner ? 0 : 1)
        .shadow(color: a.schatten == .voll ? Farben.kartenSchattenWeich : .clear, radius: a.schatten == .voll ? 3 : 0, y: a.schatten == .voll ? 2 : 0)
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
        max(1, Int((hoehe - (gross ? 10 : 9)) / zeilenHoehe))
    }

    private var gekuerzt: Bool {
        kachel.art != .bereitschaft && kachel.art != .veranstaltung && kachel.zeilen.count > platzZeilen
    }

    @ViewBuilder
    private func inhalt(_ a: Kachelaussehen) -> some View {
        if kachel.art == .bereitschaft {
            Text(kachel.titel)
                .font(.system(size: gross ? 15 : 12.5))
                .foregroundStyle(a.text)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .minimumScaleFactor(0.8)
                .padding(4)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if gross && breite >= 300 && kachel.karte != nil {
            HStack(alignment: .top, spacing: 12) {
                zeilenBlock(a)
                    .frame(width: min(max(breite * 0.36, 130), 230), alignment: .topLeading)
                Rectangle()
                    .fill(a.text.opacity(0.15))
                    .frame(width: 0.5)
                    .padding(.vertical, 2)
                TagesDetails(kachel: kachel, textFarbe: a.text, hoehe: hoehe)
                    .frame(maxWidth: .infinity, alignment: .topLeading)
            }
            .padding(.leading, 14)
            .padding(.top, 6)
            .padding(.trailing, 22)
            .padding(.bottom, 4)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        } else {
            VStack(alignment: .leading, spacing: 0) {
                zeilenBlock(a)
                if gross && kachel.karte != nil && hoehe > CGFloat(kachel.zeilen.count) * zeilenHoehe + 30 {
                    TagesDetails(kachel: kachel, textFarbe: a.text, hoehe: hoehe - CGFloat(kachel.zeilen.count) * zeilenHoehe - 14, kompakt: true)
                        .padding(.top, 4)
                        .padding(.trailing, 16)
                }
            }
            .padding(.leading, gross ? 14 : (kompakt ? 9 : 11.5))
            .padding(.top, gross ? 6 : (kompakt ? 4 : 4.5))
            .padding(.trailing, kompakt ? 2 : 4)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }

    private func zeilenBlock(_ a: Kachelaussehen) -> some View {
        let anzahl = kachel.art == .veranstaltung ? kachel.zeilen.count : min(kachel.zeilen.count, platzZeilen)
        let sichtbar = Array(kachel.zeilen.prefix(anzahl))
        return VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(sichtbar.enumerated()), id: \.offset) { eintrag in
                KachelzeileView(zeile: eintrag.element, aussehen: a, schrift: schrift,
                                titelFarbe: kachel.art == .termin && eintrag.offset == 0 ? Farben.terminTitel : nil)
                    .frame(height: zeilenHoehe, alignment: .leading)
            }
        }
    }

    // MARK: - Zeichen unten rechts (Info, Hausaufgabe, Kürzung)

    @ViewBuilder
    private func zeichen(_ a: Kachelaussehen) -> some View {
        let mitInfo = kachel.info && kachel.art != .termin
        let mitHausaufgabe = kachel.hausaufgabe && kachel.art != .bereitschaft
        if gekuerzt && !gross {
            Kuerzungszeichen(gruen: kachel.art == .aenderung || kachel.art == .abgegeben)
        } else if mitInfo || mitHausaufgabe {
            HStack(spacing: 2) {
                if mitHausaufgabe && (!mitInfo || breite >= 96) {
                    Image(systemName: "book.closed.fill")
                        .font(.system(size: 8))
                        .foregroundStyle(Farben.infoScheibe)
                        .frame(width: 14.5, height: 14.5)
                        .background(Farben.infoFeld, in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                }
                if mitInfo {
                    Infozeichen()
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
            Text(kachel.titel)
                .font(.system(size: gross ? 13.5 : 12.4, weight: .semibold))
                .foregroundStyle(a.text)
                .lineLimit(1)
                .truncationMode(.tail)
            if gross {
                Text("\(Rasterzeit.uhrzeit(kachel.beginn))–\(Rasterzeit.uhrzeit(kachel.ende))")
                    .font(.system(size: 12.5).monospacedDigit())
                    .foregroundStyle(a.text.opacity(0.75))
                    .lineLimit(1)
                if let hinweis = aufsichtHinweis {
                    Text(hinweis)
                        .font(.system(size: 12.5))
                        .foregroundStyle(a.text.opacity(0.75))
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 0)
            if kachel.art == .aufsicht && kachel.aufsichtsstand != .regulaer {
                Image(systemName: "ellipsis")
                    .font(.system(size: 9, weight: .bold))
                    .rotationEffect(.degrees(90))
                    .foregroundStyle(Farben.gruenSchrift)
                    .frame(width: 15, height: 13)
                    .background(Farben.gruen, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
                    .padding(.trailing, 3)
            }
        }
        .padding(.leading, 4)
        .padding(.trailing, 3)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(a.hintergrund, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
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
        teile.append("\(Rasterzeit.uhrzeit(kachel.beginn)) bis \(Rasterzeit.uhrzeit(kachel.ende))")
        if let h = aufsichtHinweis { teile.append(h) }
        return teile.joined(separator: ", ")
    }
}

/// Eine Zeile der Kachel: Text, grüne Plaketten, durchgestrichene Werte. Zu lange Zeilen werden am Rand abgeschnitten.
struct KachelzeileView: View {
    let zeile: Kachelzeile
    let aussehen: Kachelaussehen
    let schrift: CGFloat
    var titelFarbe: Color? = nil

    var body: some View {
        Group {
            if zeile.nurText && !aussehen.durchgestrichen {
                Text(zeile.text)
                    .lineLimit(1)
                    .truncationMode(.tail)
            } else {
                HStack(spacing: 0) {
                    ForEach(Array(zeile.teile.enumerated()), id: \.offset) { eintrag in
                        teil(eintrag.element)
                        if !eintrag.element.trenner.isEmpty {
                            Text(eintrag.element.trenner)
                                .strikethrough(aussehen.durchgestrichen)
                        }
                    }
                }
                .lineLimit(1)
                .fixedSize(horizontal: true, vertical: false)
                .frame(maxWidth: .infinity, alignment: .leading)
                .clipped()
            }
        }
        .font(.system(size: schrift, weight: zeile.fett ? .semibold : .regular))
        .foregroundStyle(titelFarbe ?? aussehen.text)
    }

    @ViewBuilder
    private func teil(_ t: Kachelteil) -> some View {
        if aussehen.durchgestrichen {
            // Entfall: alles durchgestrichen, Plaketten ohne grüne Fläche
            Text(t.text)
                .strikethrough(true)
                .foregroundStyle(t.stil == .plakette ? Farben.entfallPlakettenText : aussehen.text)
        } else {
            switch t.stil {
            case .text:
                Text(t.text)
            case .plakette:
                Plakettenwert(text: t.text)
            case .weg:
                // Wegfallender Wert (z. B. vertretene Lehrkraft): durchgestrichen, ohne grüne Fläche
                Text(t.text)
                    .strikethrough(true)
            case .offen:
                // Offene Vertretung: abwesende Lehrkraft durchgestrichen in grüner Plakette
                Plakettenwert(text: t.text, durchgestrichen: true)
            }
        }
    }
}

/// Tagesansicht: Fach-Langname, Uhrzeit und Stunden, Status und Hinweis neben bzw. unter den Zeilen.
struct TagesDetails: View {
    let kachel: Kachel
    let textFarbe: Color
    var hoehe: CGFloat = 80
    var kompakt: Bool = false

    private var karte: Karte? { kachel.karte }

    private var langname: String? {
        guard let k = karte else { return nil }
        if kachel.art == .veranstaltung { return "Veranstaltung" }
        if !k.fachName.isEmpty && k.fachName.caseInsensitiveCompare(k.fach) != .orderedSame { return k.fachName }
        return nil
    }

    private var zeit: String {
        var s = "\(Rasterzeit.uhrzeit(kachel.beginn)) – \(Rasterzeit.uhrzeit(kachel.ende))"
        if kachel.art != .veranstaltung || kachel.von != kachel.bis {
            s += kachel.von == kachel.bis ? " · \(kachel.von). Stunde" : " · \(kachel.von).–\(kachel.bis). Stunde"
        }
        return s
    }

    private var status: (String, Color)? {
        guard let k = karte else { return nil }
        switch kachel.art {
        case .entfall:
            return (k.art.isEmpty || k.art.lowercased() == "entfall" ? "Entfall" : "Entfall · \(k.art)", Farben.entfallRand)
        case .aenderung:
            if k.online { return ("Findet online statt", Farben.gruen) }
            if kachel.zusatz { return ("Zusätzliche Unterrichtsstunde", Farben.gruen) }
            if Kartenstil.nurRaumGeaendert(k) { return ("Raumänderung", Farben.gruen) }
            return (k.art.isEmpty ? "Vertretung" : k.art, Farben.gruen)
        case .abgegeben:
            let neu = k.lehrer.map(\.k).filter { !$0.isEmpty }
            return (neu.isEmpty ? "Vertretung offen" : "Vertretung: \(neu.joined(separator: ", "))", Farben.gruen)
        case .bereitschaftAbwesend:
            return ("Lehrkraft abwesend", Farben.gedaempft)
        case .klausur:
            return (k.art.isEmpty ? "Klausur" : k.art, Farben.pruefungRand)
        case .termin:
            return (k.istRaumbuchung || k.status == "buchung" ? "Raumbuchung" : "Termin", Farben.gruen)
        default:
            return nil
        }
    }

    private var hinweise: [String] {
        guard let k = karte else { return [] }
        var liste: [String] = []
        if !k.text.isEmpty && kachel.art != .veranstaltung { liste.append(k.text) }
        if !k.abwesendeKlassen.isEmpty {
            liste.append("\(k.abwesendeKlassen.joined(separator: ", ")) – \(k.abwesendText.isEmpty ? "abwesend" : k.abwesendText)")
        }
        if !k.notizSchueler.isEmpty { liste.append(k.notizSchueler) }
        if !k.notizLehrer.isEmpty { liste.append(k.notizLehrer) }
        if k.hausaufgabeFaellig { liste.append("Hausaufgabe fällig") }
        return liste
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            if let langname, !kompakt {
                Text(langname)
                    .font(.system(size: 15, weight: .semibold))
                    .lineLimit(1)
            }
            Text(zeit)
                .font(.system(size: 13).monospacedDigit())
                .opacity(0.8)
                .lineLimit(1)
            if let status {
                Text(status.0)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(status.1)
                    .lineLimit(1)
            }
            if hoehe > (kompakt ? 50 : 70) {
                ForEach(Array(hinweise.enumerated()), id: \.offset) { eintrag in
                    Text(eintrag.element)
                        .font(.system(size: 13))
                        .opacity(0.85)
                        .lineLimit(hoehe > 120 ? 3 : 1)
                        .multilineTextAlignment(.leading)
                }
            }
        }
        .foregroundStyle(textFarbe)
        .frame(maxHeight: hoehe, alignment: .topLeading)
        .clipped()
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
        balken = fach
        balkenDeckkraft = v ? 0.7 : 1
        text = v ? Farben.karteTextVergangen : Farben.karteText
        switch k.art {
        case .normal:
            hintergrund = v ? Farben.karteVergangen : Farben.karte
            schatten = v ? .flach : .voll
        case .aenderung:
            farbigeKarte(flaeche: Farben.aenderungHintergrund, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: true)
            text = Farben.aenderungText
        case .entfall:
            farbigeKarte(flaeche: Farben.entfallHintergrund, rand: Farben.entfallRand, vergangen: v, dunkel: dunkel, schatten: false)
            text = Farben.entfallText
            durchgestrichen = true
        case .klausur:
            hintergrund = Farben.pruefungHintergrund
            rand = Farben.pruefungRand
            schatten = .keiner
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
            // Hellgrünes Feld (dunkel: dunkelgrün) mit feinem 1-pt-Rahmen; vergangen fällt nur der Rahmen weg
            farbigeKarte(flaeche: Farben.veranstaltungFlaeche, rand: Farben.veranstaltungRand, vergangen: v, dunkel: dunkel, schatten: false)
            randBreite = 1
            text = Farben.karteText
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
            case .vertretung:
                farbigeKarte(flaeche: Farben.aenderungHintergrund, rand: Farben.aenderungRand, vergangen: v, dunkel: dunkel, schatten: false)
                randBreite = 1.5
                text = Farben.aenderungText
            case .abgegeben:
                hintergrund = .clear
                schatten = .keiner
                ecken = true
                text = Farben.tinte2
            }
        }
    }

    /// Farbige Kachel (Vertretung, Entfall, Veranstaltung): künftig Fläche und Rahmen; vergangen fällt – hell wie dunkel –
    /// nur der farbige Rahmen weg. Fläche, Schriftfarbe und Fachbalken bleiben, nichts wird abgeblendet.
    private mutating func farbigeKarte(flaeche: Color, rand farbe: Color, vergangen: Bool, dunkel: Bool, schatten mitSchatten: Bool) {
        balkenDeckkraft = 1
        if vergangen {
            hintergrund = flaeche
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
            .font(.system(size: 11, weight: .bold))
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

/// Kürzungszeichen (.card__cut): drei Punkte – auf Vertretungen grün.
struct Kuerzungszeichen: View {
    var gruen = false

    var body: some View {
        Image(systemName: "ellipsis")
            .font(.system(size: 8, weight: .bold))
            .rotationEffect(.degrees(90))
            .foregroundStyle(gruen ? Farben.gruenSchrift : Farben.infoScheibe)
            .frame(width: 14.5, height: 14.5)
            .background(gruen ? Farben.gruen : Farben.infoFeld, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

/// Hinterlegter Wert (.badge): Polster 0,33/2,27, Radius 2, Schrift mittel, grün.
struct Plakettenwert: View {
    let text: String
    var farbe: Color = Farben.gruen
    var durchgestrichen: Bool = false

    var body: some View {
        Text(text)
            .fontWeight(.medium)
            .strikethrough(durchgestrichen, color: Farben.gruenSchrift)
            .foregroundStyle(Farben.gruenSchrift)
            .padding(.horizontal, 2.27)
            .padding(.top, 0.33)
            .background(farbe, in: RoundedRectangle(cornerRadius: 2, style: .continuous))
            .lineLimit(1)
            .fixedSize()
    }
}

/// Info-Punkt (.card__info): helles Feld 14,5 pt, graue Scheibe mit „i“.
struct Infozeichen: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(Farben.infoFeld)
            Circle()
                .fill(Farben.infoScheibe)
                .padding(2.3)
            Text("i")
                .font(.system(size: 7.5, weight: .bold))
                .foregroundStyle(Farben.infoZeichen)
        }
        .frame(width: 14.5, height: 14.5)
    }
}
