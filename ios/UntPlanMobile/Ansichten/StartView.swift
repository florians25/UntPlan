import SwiftUI

/// Eintrag der Infokarten „Infos zum Tag“.
struct Tagesinfo: Identifiable {
    enum Art {
        case nachricht
        case veranstaltung
        case hinweis
    }

    let id = UUID()
    let art: Art
    let titel: String
    let text: String
}

/// Startseite: Schulname, Infos zum Tag, Tageskarte mit den nächsten Stunden, Termine und Hausaufgaben.
struct StartView: View {
    @EnvironmentObject var zustand: AppZustand
    @State private var heute: Heute? = nil
    @State private var laedt = false
    @State private var fehler: String? = nil
    @State private var gewaehlteKarte: Karte? = nil

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
                    kopf
                    if let fehler {
                        Fehlerhinweis(text: fehler)
                    }
                    if let heute {
                        if heute.paket {
                            infosZumTag(heute)
                            tageskarte(heute)
                            if !heute.aufsichten.isEmpty { aufsichten(heute) }
                            if !heute.termine.isEmpty { termine(heute) }
                            if !heute.hausaufgaben.isEmpty { hausaufgaben(heute) }
                        } else {
                            Fehlerhinweis(text: "Es wurden noch keine Daten aus UntPlan übertragen.")
                        }
                    } else if laedt {
                        ProgressView()
                            .frame(maxWidth: .infinity)
                            .padding(.top, 40)
                    }
                }
                .padding(16)
            }
            .background(Color(.systemGroupedBackground))
            .refreshable { await laden() }
            .task { await laden() }
            .toolbar(.hidden, for: .navigationBar)
            .sheet(item: $gewaehlteKarte) { karte in
                StundendetailView(karte: karte, typ: heute?.typ ?? "klasse")
                    .presentationDetents([.medium, .large])
            }
        }
    }

    // MARK: - Kopf mit Schulname

    private var kopf: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(datumstext.uppercased())
                .font(.footnote.weight(.semibold))
                .foregroundStyle(Farben.akzent)
            Text(zustand.schulname)
                .font(.largeTitle.weight(.bold))
                .lineLimit(2)
                .minimumScaleFactor(0.6)
            if let name = zustand.benutzer?.name {
                Text("Hallo, \(name)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.top, 8)
    }

    private var datumstext: String {
        var s = Datumsformat.anzeige(Date(), "EEEE, d. MMMM")
        if let w = heute?.woche, !w.isEmpty { s += w.count == 1 ? " · \(w)-Woche" : " · \(w)" }
        return s
    }

    // MARK: - Infos zum Tag

    private func infos(_ h: Heute) -> [Tagesinfo] {
        var liste: [Tagesinfo] = []
        for n in h.nachrichten {
            // "Betreff: Text" in Titel und Text teilen
            if let bereich = n.range(of: ": "), n.distance(from: n.startIndex, to: bereich.lowerBound) <= 60 {
                liste.append(Tagesinfo(art: .nachricht, titel: String(n[..<bereich.lowerBound]), text: String(n[bereich.upperBound...])))
            } else {
                liste.append(Tagesinfo(art: .nachricht, titel: "Nachricht zum Tag", text: n))
            }
        }
        for e in h.ereignisse {
            var text = e.ganztaegig ? "Ganztägig" : (e.von == e.bis ? "\(e.von). Stunde" : "\(e.von).–\(e.bis). Stunde")
            if !e.klassen.isEmpty { text += " · " + e.klassen.joined(separator: ", ") }
            liste.append(Tagesinfo(art: .veranstaltung, titel: e.titel.isEmpty ? e.art : e.titel, text: text))
        }
        for hinweis in h.hinweise {
            liste.append(Tagesinfo(art: .hinweis, titel: "Hinweis", text: hinweis))
        }
        return liste
    }

    @ViewBuilder
    private func infosZumTag(_ h: Heute) -> some View {
        let liste = infos(h)
        if !liste.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                Abschnittstitel(text: "Infos zum Tag", zusatz: liste.count > 1 ? "\(liste.count)" : nil)
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(alignment: .top, spacing: 12) {
                        ForEach(liste) { info in
                            Infokarte(info: info)
                        }
                    }
                    .padding(.vertical, 4)
                    .padding(.horizontal, 2)
                }
            }
        }
    }

    // MARK: - Tageskarte

    /// Stunden des Tages; heute nur die noch kommenden.
    private func naechsteStunden(_ h: Heute) -> [Karte] {
        let sortiert = h.karten.sorted { ($0.von, $0.bis) < ($1.von, $1.bis) }
        guard Datumsformat.istHeute(h.datum), !h.jetzt.isEmpty else { return sortiert }
        return sortiert.filter { $0.ende.isEmpty || $0.ende >= h.jetzt }
    }

    private func tageskarte(_ h: Heute) -> some View {
        let stunden = naechsteStunden(h)
        let istHeute = Datumsformat.istHeute(h.datum)
        return VStack(alignment: .leading, spacing: 10) {
            Abschnittstitel(text: istHeute ? "Heute" : h.wochentag,
                            zusatz: h.aenderungen > 0 ? (h.aenderungen == 1 ? "1 Änderung" : "\(h.aenderungen) Änderungen") : nil)
            VStack(spacing: 0) {
                HStack {
                    Image(systemName: "calendar")
                        .foregroundStyle(Farben.akzent)
                    Text(Datumsformat.anzeige(h.datum, "EEEE, d. MMMM"))
                        .font(.subheadline.weight(.semibold))
                    Spacer()
                    if let element = h.element, !element.isEmpty {
                        Text(element)
                            .font(.caption.weight(.semibold))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .background(Farben.akzentSanft, in: Capsule())
                            .foregroundStyle(Farben.akzent)
                    }
                }
                .padding(14)
                Divider()
                if stunden.isEmpty {
                    Text(h.karten.isEmpty ? "Keine Stunden." : "Heute keine weiteren Stunden.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(14)
                } else {
                    ForEach(Array(stunden.enumerated()), id: \.element.id) { eintrag in
                        if eintrag.offset > 0 {
                            Divider().padding(.leading, 70)
                        }
                        Button {
                            gewaehlteKarte = eintrag.element
                        } label: {
                            Stundenzeile(karte: eintrag.element, typ: h.typ)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .alsKarte()
        }
    }

    // MARK: - Aufsichten, Termine, Hausaufgaben

    private func aufsichten(_ h: Heute) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Abschnittstitel(text: "Pausenaufsichten")
            VStack(spacing: 0) {
                ForEach(Array(h.aufsichten.enumerated()), id: \.element.id) { eintrag in
                    let a = eintrag.element
                    if eintrag.offset > 0 { Divider().padding(.leading, 14) }
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(a.gangName.isEmpty ? a.gang : a.gangName)
                                .font(.subheadline.weight(.semibold))
                                .strikethrough(a.status == "vertreten", color: Farben.entfallRand)
                            Text("nach der \(a.nachStunde). Stunde · \(a.beginn) – \(a.ende)")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        if a.status == "vertretung" {
                            Plakette(text: "Vertretung", farbe: Farben.gruen)
                        } else if a.status == "vertreten" {
                            Plakette(text: "Vertreten", farbe: Farben.entfallRand)
                        }
                    }
                    .padding(14)
                }
            }
            .alsKarte()
        }
    }

    private func termine(_ h: Heute) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Abschnittstitel(text: "Termine")
            VStack(spacing: 0) {
                ForEach(Array(h.termine.enumerated()), id: \.element.id) { eintrag in
                    let t = eintrag.element
                    if eintrag.offset > 0 { Divider().padding(.leading, 70) }
                    HStack(spacing: 12) {
                        VStack(spacing: 0) {
                            Text(Datumsformat.anzeige(t.von, "d."))
                                .font(.title3.weight(.bold))
                            Text(Datumsformat.anzeige(t.von, "MMM"))
                                .font(.caption2.weight(.semibold))
                                .textCase(.uppercase)
                        }
                        .foregroundStyle(Farben.akzent)
                        .frame(width: 44)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(t.titel.isEmpty ? t.art : t.titel)
                                .font(.subheadline.weight(.semibold))
                            Text(termintext(t))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                    }
                    .padding(14)
                }
            }
            .alsKarte()
        }
    }

    private func termintext(_ t: Termin) -> String {
        var teile: [String] = []
        if !t.art.isEmpty && !t.titel.isEmpty { teile.append(t.art) }
        if !t.bis.isEmpty && t.bis.prefix(10) != t.von.prefix(10) {
            teile.append("bis \(Datumsformat.anzeige(t.bis, "d.M."))")
        }
        if let v = t.stundeVon {
            let b = t.stundeBis ?? v
            teile.append(v == b ? "\(v). Stunde" : "\(v).–\(b). Stunde")
        }
        if !t.klassen.isEmpty { teile.append(t.klassen.joined(separator: ", ")) }
        return teile.joined(separator: " · ")
    }

    private func hausaufgaben(_ h: Heute) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Abschnittstitel(text: "Hausaufgaben")
            VStack(spacing: 0) {
                ForEach(Array(h.hausaufgaben.enumerated()), id: \.element.id) { eintrag in
                    let ha = eintrag.element
                    if eintrag.offset > 0 { Divider().padding(.leading, 14) }
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Text(ha.fach)
                                .font(.subheadline.weight(.bold))
                            Spacer()
                            if let f = ha.faellig, !f.isEmpty {
                                Text("fällig \(Datumsformat.anzeige(f, "EE d.M."))")
                                    .font(.caption.weight(.semibold))
                                    .foregroundStyle(Farben.akzent)
                            }
                        }
                        Text(ha.hausaufgabe)
                            .font(.subheadline)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .padding(14)
                }
            }
            .alsKarte()
        }
    }

    private func laden() async {
        laedt = true
        defer { laedt = false }
        do {
            let neu = try await zustand.api.heute()
            heute = neu
            zustand.ungelesen = neu.ungelesen
            fehler = nil
        } catch {
            if let text = zustand.fehlertext(error) { fehler = text }
        }
    }
}

/// Infokarte mit farbigem Kopf (Nachricht blau mit Megafon, Veranstaltung grün).
struct Infokarte: View {
    let info: Tagesinfo

    private var kopfFarbe: Color {
        switch info.art {
        case .nachricht: return Farben.nachrichtKopf
        case .veranstaltung: return Farben.veranstaltungKopf
        case .hinweis: return Farben.hinweisKopf
        }
    }

    private var symbol: String {
        switch info.art {
        case .nachricht: return "megaphone.fill"
        case .veranstaltung: return "person.2.fill"
        case .hinweis: return "info.circle.fill"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Image(systemName: symbol)
                Spacer()
                Text("neu")
                    .font(.caption2.weight(.bold))
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2)
                    .background(Color.white.opacity(0.25), in: Capsule())
            }
            .foregroundStyle(.white)
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(kopfFarbe)
            VStack(alignment: .leading, spacing: 4) {
                Text(info.titel)
                    .font(.subheadline.weight(.bold))
                    .lineLimit(2)
                Text(info.text)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(5)
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .frame(width: 270, height: 170, alignment: .top)
        .background(Color(.secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .shadow(color: .black.opacity(0.06), radius: 6, y: 2)
    }
}

/// Listenzeile einer Stunde: Uhrzeit, Farbbalken, Fach mit Lehrkraft/Klasse, Raum und Plakette.
struct Stundenzeile: View {
    let karte: Karte
    let typ: String

    var body: some View {
        let stil = Kartenstil(karte)
        let zeilen = kartenzeilen(karte, typ: typ)
        HStack(spacing: 12) {
            VStack(alignment: .trailing, spacing: 1) {
                Text(karte.beginn.isEmpty ? "\(karte.von)." : karte.beginn)
                    .font(.subheadline.weight(.semibold).monospacedDigit())
                Text(karte.von == karte.bis ? "\(karte.von). Std." : "\(karte.von).–\(karte.bis).")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            .frame(width: 46, alignment: .trailing)
            RoundedRectangle(cornerRadius: 2)
                .fill(Farben.fach(karte.farbe))
                .frame(width: 4, height: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(karte.titel)
                    .font(.subheadline.weight(.semibold))
                    .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                    .lineLimit(1)
                let unten = zeilen.filter { !$0.isEmpty }
                if !unten.isEmpty {
                    unten.dropFirst().reduce(werteText(unten[0])) { $0 + Text(" · ") + werteText($1) }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            if let p = stil.plakette {
                Plakette(text: p, farbe: stil.plaketteFarbe)
            }
            Image(systemName: "chevron.right")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(karte.istEntfall ? Farben.entfallHintergrund.opacity(0.6) : (karte.istAenderung ? Farben.aenderungHintergrund.opacity(0.6) : Color.clear))
        .contentShape(Rectangle())
    }
}
