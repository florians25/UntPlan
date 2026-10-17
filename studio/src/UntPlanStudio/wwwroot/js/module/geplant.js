// Platzhalter im Menüband für Funktionen der Desktop-Anwendung, die im Studio noch folgen (ausgegraut, Hinweis „folgt“).
// Ein echtes Modul registriert einen Eintrag mit DERSELBEN id – damit ersetzt es den Platzhalter an seiner Position.
// Diese Datei muss dafür NICHT geändert werden. Die ids sind in ARCHITEKTUR.md („Menüband-Ids“) aufgelistet.

const g = (id, reiter, gruppe, text, symbol, gross = false, modul = '') => ({ id, reiter, gruppe, text, symbol, gross, geplant: true, modul });

export default {
  id: 'geplant',
  ribbon: [
    // Start
    g('start.diagnose', 'Start', 'Planungswerkzeuge', 'Diagnose', 'diagnose', false, 'optimierung'),
    g('start.gewichtung', 'Start', 'Planungswerkzeuge', 'Gewichtung', 'statistik', false, 'optimierung'),
    g('start.optimierung', 'Start', 'Planungswerkzeuge', 'Optimierung', 'optimierung', false, 'optimierung'),
    g('start.perioden', 'Start', 'Planungswerkzeuge', 'Perioden', 'perioden', true, 'einstellungen'),
    g('start.einstellungen', 'Start', 'Schule', 'Einstellungen', 'einstellungen', true, 'einstellungen'),
    g('start.multiuser', 'Start', 'Schule', 'MultiUser', 'datenbank', true, 'multiuser'),
    g('start.infostundenplan', 'Start', 'Module', 'Info-Stundenplan', 'web', false, 'druck'),
    g('start.pausenaufsichten', 'Start', 'Module', 'Pausenaufsichten', 'pausenaufsicht', false, 'pausenaufsicht'),
    g('start.ferien', 'Start', 'Module', 'Kalender / Ferien', 'kalender', false, 'einstellungen'),
    g('start.wertrechnung', 'Start', 'Module', 'Unterrichtsplanung', 'statistik', false, 'abrechnung'),
    g('start.kursplanung', 'Start', 'Module', 'Kursplanung', 'schueler', false, 'kursplanung'),
    g('start.untplanweb', 'Start', 'Module', 'UntPlanWeb', 'web', false, 'untplanweb'),
    g('start.berichte', 'Start', 'Werkzeuge', 'Berichte', 'drucken', false, 'druck'),
    // Dateneingabe
    g('eingabe.ugruppen', 'Dateneingabe', 'Spezielle Daten', 'Unterrichtsgruppen', 'perioden', false, 'einstellungen'),
    g('eingabe.bereitschaft', 'Dateneingabe', 'Spezielle Daten', 'Bereitschaftsplanung', 'vertretung', false, 'vertretung'),
    g('eingabe.stundentafel', 'Dateneingabe', 'Spezielle Daten', 'Stundentafel', 'statistik', false, 'abrechnung'),
    g('eingabe.zeitwuensche', 'Dateneingabe', 'Zeit', 'Zeitwünsche', 'zeitwunsch', true, 'zeitwuensche'),
    g('eingabe.zeitraster', 'Dateneingabe', 'Zeit', 'Zeitraster', 'stundenplan', false, 'einstellungen'),
    g('eingabe.ferien', 'Dateneingabe', 'Zeit', 'Ferien', 'kalender', false, 'einstellungen'),
    g('eingabe.schuljahr', 'Dateneingabe', 'Zeit', 'Schuljahr', 'kalender', false, 'einstellungen'),
    g('eingabe.kommentare', 'Dateneingabe', 'Hilfsmittel', 'Kommentare', 'info', false, 'kommentare'),
    g('eingabe.importexport', 'Dateneingabe', 'Hilfsmittel', 'Import/Export', 'import', false, 'austausch'),
    // Planung
    g('planung.tausch', 'Planung', 'Manuelles Planen', 'Tauschvorschläge', 'tausch', true, 'planung'),
    g('planung.raum', 'Planung', 'Manuelles Planen', 'Raum zuordnen', 'raeume', true, 'planung'),
    g('planung.diagnose', 'Planung', 'Werkzeuge', 'Diagnose', 'diagnose', true, 'optimierung'),
    g('planung.gewichtung', 'Planung', 'Werkzeuge', 'Gewichtung', 'statistik', true, 'optimierung'),
    g('planung.optimierung', 'Planung', 'Werkzeuge', 'Optimierung', 'optimierung', true, 'optimierung'),
    g('planung.entplanen', 'Planung', 'Werkzeuge', 'Alle Stunden entplanen', 'loeschen', false, 'planung'),
    g('planung.uzeitwuensche', 'Planung', 'Werkzeuge', 'Unterrichtszeitwünsche', 'zeitwunsch', false, 'zeitwuensche'),
    // Stundenpläne
    g('stundenplaene.uebersicht.klasse', 'Stundenpläne', 'Übersichtspläne', 'Klassen Übersicht', 'uebersicht', true, 'uebersicht'),
    g('stundenplaene.uebersicht.lehrer', 'Stundenpläne', 'Übersichtspläne', 'Lehrer Übersicht', 'uebersicht', true, 'uebersicht'),
    g('stundenplaene.uebersicht.raum', 'Stundenpläne', 'Übersichtspläne', 'Räume Übersicht', 'uebersicht', true, 'uebersicht'),
    g('stundenplaene.drucken', 'Stundenpläne', 'Ausgabe', 'Drucken', 'drucken', true, 'druck'),
    g('stundenplaene.html', 'Stundenpläne', 'Ausgabe', 'Info-Stundenplan', 'web', true, 'druck'),
    g('stundenplaene.ical', 'Stundenpläne', 'Ausgabe', 'iCalendar', 'kalender', true, 'druck'),
    // Kursplanung
    g('kurs.kurse', 'Kursplanung', 'Kursplanung', 'Kurse & Bänder', 'schueler', true, 'kursplanung'),
    g('kurs.import', 'Kursplanung', 'Kursplanung', 'Schüler importieren', 'import', true, 'austausch'),
    // Vertretungsplanung (nur im Vertretungsmodus)
    g('vertretung.kalender', 'Vertretungsplanung', 'Kalender', 'Kalender', 'kalender', true, 'vertretung'),
    g('vertretung.absenzen', 'Vertretungsplanung', 'Vertretungsplanung', 'Absenzen', 'abwesenheit', true, 'vertretung'),
    g('vertretung.vertretungen', 'Vertretungsplanung', 'Vertretungsplanung', 'Vertretungen', 'vertretung', true, 'vertretung'),
    g('vertretung.ausgabe', 'Vertretungsplanung', 'Vertretungsplanung', 'Ausgabe', 'drucken', true, 'druck'),
    g('vertretung.statistik', 'Vertretungsplanung', 'Vertretungsplanung', 'Vertretungs-statistik', 'statistik', true, 'abrechnung'),
    g('vertretung.veranstaltungen', 'Vertretungsplanung', 'Ereignisse', 'Veranstaltungen', 'veranstaltung', false, 'veranstaltungen'),
    g('vertretung.sondereinsaetze', 'Vertretungsplanung', 'Ereignisse', 'Sondereinsätze', 'lehrer', false, 'vertretung'),
    g('vertretung.bereitschaften', 'Vertretungsplanung', 'Ereignisse', 'Bereitschaften', 'vertretung', false, 'vertretung'),
    g('vertretung.planungsdialog', 'Vertretungsplanung', 'Planung', 'Planungs-dialog', 'stundenplan', true, 'vertretung'),
    g('vertretung.vormerkungen', 'Vertretungsplanung', 'Planung', 'Vormerkungen', 'info', true, 'vertretung'),
    g('vertretung.pausenaufsichten', 'Vertretungsplanung', 'Planung', 'Pausen-aufsichten', 'pausenaufsicht', true, 'pausenaufsicht'),
    g('vertretung.formate', 'Vertretungsplanung', 'Einstellungen', 'Vertretungsformate', 'uebersicht', false, 'vertretung'),
    g('vertretung.einstellungen', 'Vertretungsplanung', 'Einstellungen', 'Einstellungen', 'einstellungen', false, 'vertretung'),
    // Module
    g('module.sollist', 'Module', 'Unterrichtsplanung und Wertrechnung', 'Lehrer Soll/Ist', 'statistik', true, 'abrechnung'),
    g('module.matrix', 'Module', 'Unterrichtsplanung und Wertrechnung', 'Unterrichts-Matrix', 'uebersicht', true, 'abrechnung'),
    g('module.stundentafel', 'Module', 'Unterrichtsplanung und Wertrechnung', 'Stundentafel', 'unterricht', true, 'abrechnung'),
    g('module.perioden', 'Module', 'Weitere Module', 'Mehrwochen / Perioden', 'perioden', true, 'einstellungen'),
    g('module.bereitschaft', 'Module', 'Weitere Module', 'Bereitschafts-planung', 'vertretung', true, 'vertretung'),
    g('module.ugruppen', 'Module', 'Weitere Module', 'Unterrichts-gruppen', 'perioden', true, 'einstellungen'),
    g('module.untplanweb', 'Module', 'Weitere Module', 'UntPlanWeb', 'web', true, 'untplanweb'),
  ],
};
