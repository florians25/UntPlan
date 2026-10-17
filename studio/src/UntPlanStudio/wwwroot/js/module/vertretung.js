// Modul „Vertretungsplanung“ (Vertretungsmodus) wie in der Desktop-Anwendung: Menüband „Vertretungsplanung“ mit Kalender und
// Datumsauswahl (Strg+Bild↑/↓), Absenzen, Vertretungen, Vorschlag, Automatik, Sondereinsätze, Bereitschaften, Planungsdialog,
// Vormerkungen, tagesaktuelle Stundenpläne, Vertretungsformate und Einstellungen; Bereitschaftsplanung (Dateneingabe/Module).
// Die Fenster liegen in js/module/vertretung/*.js, die Veranstaltungen im eigenen Modul veranstaltungen.js.
import { zustand } from '../kern/zustand.js';
import { an, melden } from '../kern/ereignisse.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { tasteRegistrieren } from '../kern/modulsystem.js';
import { modusSetzen } from '../kern/zustand.js';
import { blaettern, datumSetzen, isoAus, planungsdatum, aktuelleAuswahl, tageswechsel } from './vertretung/kern.js';
import { datumsElement, fenster as kalenderFenster } from './vertretung/kalender.js';
import { fenster as listeFenster, ANSICHTEN } from './vertretung/liste.js';
import { fenster as absenzFenster } from './vertretung/absenzen.js';
import { fenster as vorschlagFenster } from './vertretung/vorschlag.js';
import { fenster as planFenster } from './vertretung/plan.js';
import { fenster as weitereFenster, sondereinsatzDialog, automatikDialog, einstellungenDialog } from './vertretung/weitere.js';

/** Fenster der Vertretungsplanung öffnen (schaltet bei Bedarf in den Vertretungsmodus, wie im Desktop). */
function oeffnen(typ, p = {}, lage = {}) {
  if (!zustand.dokument) return null;
  if (zustand.modus !== 'vertretung') {
    modusSetzen('vertretung');
    try { localStorage.setItem('studio.modus', 'vertretung'); } catch { /* privat */ }
  }
  return fensterverwaltung.oeffnen(typ, p, lage);
}

function flaeche() {
  const el = document.querySelector('.flaeche');
  return { b: el?.clientWidth || 1400, h: el?.clientHeight || 800 };
}

/** Planungsdialog: Klassen- und Lehrerplan tagesaktuell nebeneinander (Drag & Drop: Verlegen, Tauschen). */
function planungsdialog() {
  const a = aktuelleAuswahl()?.zeile;
  oeffnen('vertretung.plan', { typ: 'klasse', element: a?.klasse ?? undefined }, { andock: 'links' });
  fensterverwaltung.oeffnen('vertretung.plan', { typ: 'lehrer', element: a?.lehrer ?? undefined }, { andock: 'rechts' });
}

/** Kleine Pläne nebeneinander am rechten Rand: Klasse, Lehrkraft, Vertreter und Raum der gewählten Vertretung. */
function kleinePlaene() {
  const a = aktuelleAuswahl()?.zeile || {};
  const { b, h } = flaeche();
  const plaene = [{ typ: 'klasse', element: a.klasse }, { typ: 'lehrer', element: a.lehrer }, { typ: 'lehrer', element: a.vertreter, rolle: 'vertreter' }, { typ: 'raum', element: a.raum }];
  plaene.forEach((p, i) => oeffnen('vertretung.plan', { ...p, element: p.element ?? undefined, kompakt: true }, { x: Math.max(0, b - (plaene.length - i) * 230), y: 0, b: 230, h: Math.min(h, 520) }));
}

/** Standardanordnung des Vertretungsmodus: Kalender, Absenzen, Vertretungen, Vertretungsvorschlag und tagesaktuelle Pläne. */
function standardanordnung() {
  if (!zustand.dokument) return;
  fensterverwaltung.alleSchliessen();
  const { b, h } = flaeche();
  const links = Math.max(380, Math.round(b * 0.27));
  const mitte = Math.round(b * 0.45);
  const oben = Math.round(h * 0.36);
  oeffnen('vertretung.kalender', {}, { x: 0, y: 0, b: links, h: oben });
  oeffnen('vertretung.absenzen', {}, { x: 0, y: oben, b: links, h: h - oben });
  oeffnen('vertretung.vorschlag', {}, { x: links, y: Math.round(h * 0.55), b: mitte, h: h - Math.round(h * 0.55) });
  oeffnen('vertretung.plan', { typ: 'klasse' }, { x: links + mitte, y: 0, b: b - links - mitte, h: Math.round(h / 2) });
  oeffnen('vertretung.plan', { typ: 'lehrer' }, { x: links + mitte, y: Math.round(h / 2), b: b - links - mitte, h: h - Math.round(h / 2) });
  oeffnen('vertretung.liste', {}, { x: links, y: 0, b: mitte, h: Math.round(h * 0.55) });
}

function formatOeffnen(ansicht) {
  oeffnen('vertretung.liste', { ansicht });
  melden('vertretung.ansicht', ansicht);
}

const R = 'Vertretungsplanung';
const D = 'dokument';

export default {
  id: 'vertretung',
  css: ['css/module/vertretung.css'],
  fenster: { ...kalenderFenster, ...listeFenster, ...absenzFenster, ...vorschlagFenster, ...planFenster, ...weitereFenster },
  befehle: {
    'vertretung.oeffnen': ({ fenster: f = 'vertretung.liste', ...p }) => oeffnen(f, p),
    'vertretung.kalender': () => oeffnen('vertretung.kalender'),
    'vertretung.absenzen': (p) => oeffnen('vertretung.absenzen', p),
    'vertretung.liste': (p) => oeffnen('vertretung.liste', p),
    'vertretung.vorschlag': () => oeffnen('vertretung.vorschlag'),
    'vertretung.automatik': () => automatikDialog(planungsdatum(), planungsdatum()),
    'vertretung.sondereinsatz': () => sondereinsatzDialog({}),
    'vertretung.bereitschaften': () => oeffnen('vertretung.bereitschaften'),
    'vertretung.bereitschaftsplanung': () => (zustand.dokument ? fensterverwaltung.oeffnen('vertretung.bereitschaftsplanung', {}) : null),
    'vertretung.vormerkungen': () => oeffnen('vertretung.vormerkungen'),
    'vertretung.planungsdialog': planungsdialog,
    'vertretung.kleinePlaene': kleinePlaene,
    'vertretung.plan': ({ typ = 'klasse', kompakt } = {}) => oeffnen('vertretung.plan', { typ, kompakt }),
    'vertretung.format': ({ ansicht }) => formatOeffnen(ansicht),
    'vertretung.standard': standardanordnung,
    'vertretung.einstellungen': ({ seite = 0 } = {}) => einstellungenDialog(seite),
    'vertretung.heute': () => datumSetzen(isoAus(new Date())),
    'vertretung.naechster': () => blaettern(1),
    'vertretung.vorheriger': () => blaettern(-1),
  },
  ribbon: [
    { id: 'vertretung.kalender', reiter: R, gruppe: 'Kalender', text: 'Kalender', symbol: 'kalender', gross: true, braucht: D, befehl: 'vertretung.kalender', menue: [
      { text: 'Kalender', symbol: 'kalender', befehl: 'vertretung.kalender' },
      { text: 'Heute', symbol: 'kalender', befehl: 'vertretung.heute' },
      { text: 'Nächster Unterrichtstag', symbol: 'pfeil_runter', befehl: 'vertretung.naechster', taste: 'Strg+Bild↓' },
      { text: 'Vorheriger Unterrichtstag', symbol: 'pfeil_runter', befehl: 'vertretung.vorheriger', taste: 'Strg+Bild↑' },
    ] },
    { id: 'vertretung.datum', reiter: R, gruppe: 'Kalender', text: 'Datum', gross: true, element: datumsElement },
    { id: 'vertretung.absenzen', reiter: R, gruppe: 'Vertretungsplanung', text: 'Absenzen', symbol: 'abwesenheit', gross: true, braucht: D, befehl: 'vertretung.absenzen', taste: 'Strg+M', menue: [
      { text: 'Absenzen', symbol: 'abwesenheit', befehl: 'vertretung.absenzen', taste: 'Strg+M' },
      { text: 'Absenzgründe', symbol: 'abwesenheit', befehl: 'stammdaten.oeffnen', args: { typ: 'abwesenheitsgrund' } },
    ] },
    { id: 'vertretung.vertretungen', reiter: R, gruppe: 'Vertretungsplanung', text: 'Vertretungen', symbol: 'vertretung', gross: true, braucht: D, befehl: 'vertretung.liste', taste: 'Strg+T', menue: [
      { text: 'Vertretungen', symbol: 'vertretung', befehl: 'vertretung.liste', taste: 'Strg+T' },
      { text: 'Vertretungsvorschlag', symbol: 'lehrer', befehl: 'vertretung.vorschlag', taste: 'Strg+G' },
      null,
      { text: 'Vertretungsautomatik …', symbol: 'optimierung', befehl: 'vertretung.automatik', braucht: 'schreiben' },
    ] },
    { id: 'vertretung.vorschlag', reiter: R, gruppe: 'Vertretungsplanung', text: 'Vorschlag', symbol: 'lehrer', braucht: D, befehl: 'vertretung.vorschlag', taste: 'Strg+G', hinweis: 'Vertretungsvorschlag' },
    { id: 'vertretung.automatik', reiter: R, gruppe: 'Vertretungsplanung', text: 'Automatik', symbol: 'optimierung', braucht: 'schreiben', befehl: 'vertretung.automatik', hinweis: 'Vertretungsautomatik' },
    { id: 'vertretung.sondereinsaetze', reiter: R, gruppe: 'Ereignisse', text: 'Sondereinsätze', symbol: 'lehrer', braucht: 'schreiben', befehl: 'vertretung.sondereinsatz' },
    { id: 'vertretung.bereitschaften', reiter: R, gruppe: 'Ereignisse', text: 'Bereitschaften', symbol: 'vertretung', braucht: D, befehl: 'vertretung.bereitschaften' },
    { id: 'vertretung.planungsdialog', reiter: R, gruppe: 'Planung', text: 'Planungs-dialog', symbol: 'stundenplan', gross: true, braucht: D, befehl: 'vertretung.planungsdialog', hinweis: 'Klassen- und Lehrerplan tagesaktuell nebeneinander (Drag & Drop: Verlegen, Tauschen)' },
    { id: 'vertretung.vormerkungen', reiter: R, gruppe: 'Planung', text: 'Vormerkungen', symbol: 'info', gross: true, braucht: D, befehl: 'vertretung.vormerkungen' },
    { id: 'vertretung.plan.klasse', reiter: R, gruppe: 'Stundenpläne', text: 'Klassenplan', symbol: 'klassen', braucht: D, befehl: 'vertretung.plan', args: { typ: 'klasse' }, hinweis: 'Klassenplan (tagesaktuell)' },
    { id: 'vertretung.plan.lehrer', reiter: R, gruppe: 'Stundenpläne', text: 'Lehrerplan', symbol: 'lehrer', braucht: D, befehl: 'vertretung.plan', args: { typ: 'lehrer' }, hinweis: 'Lehrerplan (tagesaktuell)' },
    { id: 'vertretung.plan.raum', reiter: R, gruppe: 'Stundenpläne', text: 'Raumplan', symbol: 'raeume', braucht: D, befehl: 'vertretung.plan', args: { typ: 'raum' }, hinweis: 'Raumplan (tagesaktuell)' },
    { id: 'vertretung.plan.klein', reiter: R, gruppe: 'Stundenpläne', text: 'Kleine Pläne', symbol: 'uebersicht', braucht: D, befehl: 'vertretung.kleinePlaene', menue: [
      { text: 'Kleine Pläne nebeneinander', symbol: 'uebersicht', befehl: 'vertretung.kleinePlaene' },
      null,
      { text: 'Kleiner Klassenplan', symbol: 'klassen', befehl: 'vertretung.plan', args: { typ: 'klasse', kompakt: true } },
      { text: 'Kleiner Lehrerplan', symbol: 'lehrer', befehl: 'vertretung.plan', args: { typ: 'lehrer', kompakt: true } },
      { text: 'Kleiner Raumplan', symbol: 'raeume', befehl: 'vertretung.plan', args: { typ: 'raum', kompakt: true } },
    ], hinweis: 'Kompakte Stundenpläne nebeneinander – folgen über die Fensterverknüpfung der gewählten Vertretung' },
    { id: 'vertretung.formate', reiter: R, gruppe: 'Einstellungen', text: 'Vertretungsformate', symbol: 'uebersicht', braucht: D, menue: [
      ...ANSICHTEN.map((a) => ({ text: a.replace(' - ', ' – '), symbol: a.startsWith('Lehrer') ? 'lehrer' : a.startsWith('Druck') ? 'drucken' : a === 'Pausenaufsichten' ? 'pausenaufsicht' : 'klassen', befehl: 'vertretung.format', args: { ansicht: a } })),
      null,
      { text: 'Standardanordnung der Fenster', symbol: 'anordnung', befehl: 'vertretung.standard' },
      { text: 'Eigene Fensteranordnungen …', symbol: 'anordnung', befehl: 'fenster.anordnungen' },
    ] },
    { id: 'vertretung.einstellungen', reiter: R, gruppe: 'Einstellungen', text: 'Einstellungen', symbol: 'einstellungen', braucht: D, befehl: 'vertretung.einstellungen', menue: [
      { text: 'Einstellungen Vertretungsplanung …', symbol: 'einstellungen', befehl: 'vertretung.einstellungen', args: { seite: 0 } },
      { text: 'Vertretungszeitraster …', symbol: 'stundenplan', befehl: 'vertretung.einstellungen', args: { seite: 0 } },
      { text: 'Vertretungszähler …', symbol: 'statistik', befehl: 'vertretung.einstellungen', args: { seite: 1 } },
      { text: 'Vertretungsvorschlag …', symbol: 'statistik', befehl: 'vertretung.einstellungen', args: { seite: 2 } },
      { text: 'Farben und Bezeichnungen …', symbol: 'einstellungen', befehl: 'vertretung.einstellungen', args: { seite: 4 } },
    ] },
    // Bereitschaftsplanung (Modul) in Dateneingabe und Module
    { id: 'eingabe.bereitschaft', reiter: 'Dateneingabe', gruppe: 'Spezielle Daten', text: 'Bereitschaftsplanung', symbol: 'vertretung', braucht: D, befehl: 'vertretung.bereitschaftsplanung' },
    { id: 'module.bereitschaft', reiter: 'Module', gruppe: 'Weitere Module', text: 'Bereitschafts-planung', symbol: 'vertretung', gross: true, braucht: D, befehl: 'vertretung.bereitschaftsplanung' },
  ],
  start() {
    const imModus = (f) => () => { if (zustand.dokument && zustand.modus === 'vertretung') f(); };
    tasteRegistrieren('Strg+PageUp', imModus(() => blaettern(-1)));
    tasteRegistrieren('Strg+PageDown', imModus(() => blaettern(1)));
    // Wechsel in den Vertretungsmodus: Vertretungen des Planungsdatums abgleichen (wie im Desktop beim Tageswechsel)
    an('modus', (m) => { if (m === 'vertretung' && zustand.dokument && planungsdatum() && zustand.benutzer?.darfSchreiben) tageswechsel(planungsdatum(), planungsdatum()); });
  },
};
