import SwiftUI

/// Details einer Stunde (Blatt beim Antippen einer Karte) nach Vorlage:
/// oben X, Datum und Uhrzeit, ⋮ („Im Stundenplan anzeigen“); darunter Klasse | Gruppe, Fach, Räume, Lehrkräfte;
/// dann Zeilen mit Symbolen (Vertretungstext, Lehrstoff, Schüler*innen, Hausaufgaben, Klassenbucheinträge, Notizen).
/// Es erscheint nur, was die API für das Konto liefert; Lehrkräfte können Lehrstoff, Notizen und Hausaufgaben bearbeiten.
struct StundendetailView: View {
    let karte: Karte
    let typ: String
    @EnvironmentObject var zustand: AppZustand
    @Environment(\.dismiss) private var schliessen
    @StateObject private var daten: Stundendaten

    init(karte: Karte, typ: String) {
        self.karte = karte
        self.typ = typ
        _daten = StateObject(wrappedValue: Stundendaten(karte: karte))
    }

    private var kollegium: Bool { zustand.istKollegium }
    private var mitUnterricht: Bool { karte.unterricht != nil }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    kopf
                        .padding(.horizontal, 20)
                        .padding(.top, 6)
                        .padding(.bottom, 14)
                    Rectangle()
                        .fill(Farben.trennlinie)
                        .frame(height: 0.5)
                    zeilen
                        .padding(.top, 4)
                        .padding(.bottom, 20)
                }
            }
            .background(Farben.blatt.ignoresSafeArea())
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { leiste }
            .navigationDestination(for: Detailseite.self) { seite in
                ziel(seite)
            }
            .task { await daten.laden(kollegium: kollegium) }
        }
        .environmentObject(daten)
        .environmentObject(zustand)
        .presentationBackground(Farben.blatt)
    }

    // MARK: - Leiste

    @ToolbarContentBuilder
    private var leiste: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            Button {
                schliessen()
            } label: {
                Glasknopf {
                    Image(systemName: "xmark")
                        .font(.system(size: 16, weight: .regular))
                }
            }
            .accessibilityLabel("Schließen")
        }
        ToolbarItem(placement: .principal) {
            Leistentitel(titel: "\(Datumsformat.anzeige(karte.datum, "EE")) | \(Datumsformat.anzeige(karte.datum, "dd.MM.yy"))",
                         untertitel: karte.beginn.isEmpty ? stundentext : "\(karte.beginn) - \(karte.ende)")
        }
        ToolbarItem(placement: .topBarTrailing) {
            if !planziele.isEmpty {
                Menu {
                    Section("Im Stundenplan anzeigen") {
                        ForEach(planziele) { plan in
                            Button {
                                zustand.planZeigen(plan)
                                schliessen()
                            } label: {
                                Label(plan.anzeige, systemImage: plan.typ == "raum" ? "door.left.hand.open" : (plan.typ == "lehrer" ? "person" : "person.3"))
                            }
                        }
                    }
                } label: {
                    Glasknopf {
                        Image(systemName: "ellipsis")
                            .font(.system(size: 16, weight: .semibold))
                            .rotationEffect(.degrees(90))
                    }
                }
                .accessibilityLabel("Weitere Aktionen")
            }
        }
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
        hinzu("raum", karte.raeume)
        if kollegium {
            hinzu("klasse", karte.klassen)
            hinzu("lehrer", karte.lehrer)
        } else if let eigene = zustand.benutzer?.element, !eigene.isEmpty {
            hinzu("klasse", karte.klassen.filter { $0.k.caseInsensitiveCompare(eigene) == .orderedSame })
        }
        return ergebnis
    }

    // MARK: - Kopf

    private var titelzeile: String {
        var teile = karte.klassen.map { $0.k.isEmpty ? ($0.alt ?? "") : $0.k }.filter { !$0.isEmpty }
        if !karte.gruppe.isEmpty { teile.append(karte.gruppe) }
        return teile.isEmpty ? karte.titel : teile.joined(separator: " | ")
    }

    private var kopf: some View {
        let stil = Kartenstil(karte)
        let fach = karte.fach.isEmpty ? karte.art : karte.fach
        return VStack(alignment: .leading, spacing: 5) {
            Text(titelzeile)
                .font(.system(size: 18))
                .foregroundStyle(Farben.tinte)
                .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                .textSelection(.enabled)
            if !fach.isEmpty {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    if let alt = karte.fachAlt, !alt.isEmpty, alt != karte.fach {
                        Text(alt)
                            .strikethrough(true, color: Farben.entfallRand)
                            .foregroundStyle(Farben.tinte2)
                        Image(systemName: "arrow.right")
                            .font(.system(size: 11))
                            .foregroundStyle(Farben.gedaempft)
                    }
                    Text(fach)
                        .strikethrough(stil.durchgestrichen, color: Farben.entfallRand)
                    if !karte.fachName.isEmpty && karte.fachName != fach {
                        Text(karte.fachName)
                            .font(.system(size: 14))
                            .foregroundStyle(Farben.tinte2)
                    }
                }
                .font(.system(size: 18))
                .foregroundStyle(Farben.tinte)
            }
            if !karte.raeume.isEmpty {
                Raumlabels(werte: karte.raeume, farbe: stil.wertFarbe, durchgestrichen: stil.durchgestrichen)
            }
            if !karte.lehrer.isEmpty {
                pfeilText(karte.lehrer, durchgestrichen: stil.durchgestrichen)
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.tinte2)
            }
            let plaketten = plakettenListe(stil)
            if !plaketten.isEmpty {
                HStack(spacing: 6) {
                    ForEach(Array(plaketten.enumerated()), id: \.offset) { p in
                        Plakette(text: p.element.0, farbe: p.element.1)
                    }
                }
                .padding(.top, 3)
            }
            if !karte.abwesendeKlassen.isEmpty {
                Text("\(karte.abwesendeKlassen.joined(separator: ", ")) – \(karte.abwesendText.isEmpty ? "abwesend" : karte.abwesendText)")
                    .font(.system(size: 13))
                    .foregroundStyle(Farben.entfallRand)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func plakettenListe(_ stil: Kartenstil) -> [(String, Color)] {
        var liste: [(String, Color)] = []
        if let p = stil.plakette { liste.append((p, stil.plaketteFarbe)) }
        if karte.zusaetzlich && !karte.keineVertretung && stil.plakette != "Zusätzlich" {
            liste.append(("Zusätzlicher Unterricht", Farben.gruen))
        }
        return liste
    }

    /// Werte mit Änderung als „alt → neu“ (alter Wert durchgestrichen).
    private func pfeilText(_ werte: [Wert], durchgestrichen: Bool) -> Text {
        var ergebnis = Text("")
        for (i, w) in werte.enumerated() {
            if i > 0 { ergebnis = ergebnis + Text(", ") }
            if durchgestrichen {
                ergebnis = ergebnis + Text(w.k.isEmpty ? (w.alt ?? "") : w.k).strikethrough(true, color: Farben.entfallRand)
                continue
            }
            if let alt = w.alt, !alt.isEmpty, alt != w.k {
                ergebnis = ergebnis + Text(alt).strikethrough(true, color: Farben.entfallRand)
                if !w.k.isEmpty { ergebnis = ergebnis + Text(" → ") }
            }
            if !w.k.isEmpty {
                ergebnis = ergebnis + Text(w.k)
            }
        }
        return ergebnis
    }

    // MARK: - Zeilen

    private var zeilen: some View {
        VStack(spacing: 0) {
            if kollegium && mitUnterricht {
                onlineZeile
                Zeilentrenner()
            }
            if !karte.text.isEmpty {
                link(.text, Listenzeile(symbol: "doc.text", titel: karte.keineVertretung ? "Hinweis" : "Vertretungstext", untertitel: karte.text))
            }
            if mitUnterricht && (kollegium || !(daten.eintrag?.thema ?? "").isEmpty) {
                link(.lehrstoff, Listenzeile(symbol: "text.book.closed", titel: "Lehrstoff", untertitel: daten.eintrag?.thema))
            }
            if kollegium && mitUnterricht {
                link(.schueler, Listenzeile(symbol: "person.3", titel: "Schüler*innen",
                                            zahl: daten.anwesenheit == nil ? nil : daten.abwesendAnzahl))
            }
            if mitUnterricht {
                link(.hausaufgaben, Listenzeile(symbol: "book.pages", titel: "Hausaufgaben",
                                                zahl: daten.hausaufgaben == nil ? nil : daten.hausaufgaben?.liste.count))
            }
            if kollegium && !daten.klassen.isEmpty {
                link(.klassenbuch, Listenzeile(symbol: "book.closed", titel: "Klassenbucheinträge",
                                               zahl: daten.geladen ? daten.vermerke.count : nil))
            }
            if (kollegium && mitUnterricht) || !daten.notizSchueler.isEmpty {
                link(.notizSchueler, Listenzeile(symbol: "note.text", titel: "Notizen für Schüler*innen", untertitel: daten.notizSchueler))
            }
            if kollegium && (mitUnterricht || !daten.notizLehrer.isEmpty) {
                link(.notizLehrer, Listenzeile(symbol: "note.text.badge.plus", titel: "Notizen für Lehrkräfte", untertitel: daten.notizLehrer))
            }
            if karte.hausaufgabeFaellig {
                Listenzeile(symbol: "exclamationmark.circle", titel: "Zu dieser Stunde ist eine Hausaufgabe fällig", pfeil: false)
            }
            if !daten.geladen && mitUnterricht {
                ProgressView()
                    .padding(.top, 14)
            }
        }
    }

    private func link(_ seite: Detailseite, _ inhalt: Listenzeile) -> some View {
        VStack(spacing: 0) {
            NavigationLink(value: seite) {
                inhalt
            }
            .buttonStyle(ZeilenKnopfStil())
            Zeilentrenner()
        }
    }

    /// „Findet online statt“ – nur Anzeige (die API bietet kein Feld zum Speichern).
    private var onlineZeile: some View {
        HStack(spacing: 13) {
            Image(systemName: "video")
                .font(.system(size: 18, weight: .light))
                .foregroundStyle(Farben.gedaempft)
                .frame(width: 28)
            Text("Findet online statt")
                .font(.system(size: 16))
                .foregroundStyle(Farben.tinte)
            Spacer(minLength: 4)
            Toggle("Findet online statt", isOn: .constant(daten.eintrag?.online ?? false))
                .labelsHidden()
                .disabled(true)
        }
        .padding(.horizontal, 16)
        .frame(minHeight: 50)
    }

    @ViewBuilder
    private func ziel(_ seite: Detailseite) -> some View {
        switch seite {
        case .text:
            DetailTextSeite(titel: karte.keineVertretung ? "Hinweis" : "Vertretungstext", text: karte.text, karte: karte)
        case .lehrstoff:
            LehrstoffSeite()
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

/// Räume als einzelne Labels: unveränderte Räume grau hinterlegt, ein neuer bzw. hinzugefügter Raum
/// allein im hervorgehobenen Änderungs-Label, ein ersetzter Raum durchgestrichen davor („C124 → Online“).
struct Raumlabels: View {
    let werte: [Wert]
    var farbe: Color = Farben.gruen
    var durchgestrichen: Bool = false

    var body: some View {
        HStack(spacing: 6) {
            ForEach(Array(werte.enumerated()), id: \.offset) { eintrag in
                let w = eintrag.element
                if durchgestrichen {
                    label(w.k.isEmpty ? (w.alt ?? "") : w.k, durchgestrichen: true)
                } else {
                    if let alt = w.alt, !alt.isEmpty, alt != w.k {
                        label(alt, durchgestrichen: true)
                        if !w.k.isEmpty {
                            Image(systemName: "arrow.right")
                                .font(.system(size: 10))
                                .foregroundStyle(Farben.gedaempft)
                        }
                    }
                    if !w.k.isEmpty {
                        if w.geaendert {
                            Text(w.k)
                                .font(.system(size: 13, weight: .medium))
                                .foregroundStyle(Farben.gruenSchrift)
                                .padding(.horizontal, 5)
                                .padding(.vertical, 1)
                                .background(farbe, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
                        } else {
                            label(w.k, durchgestrichen: false)
                        }
                    }
                }
            }
        }
        .lineLimit(1)
    }

    private func label(_ text: String, durchgestrichen: Bool) -> some View {
        Text(text)
            .font(.system(size: 13))
            .strikethrough(durchgestrichen, color: Farben.entfallRand)
            .foregroundStyle(Farben.tinte2)
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .background(Farben.infoFeld, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}
