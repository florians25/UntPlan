// Modul „Start“: Übersicht (Klassen, Lehrer, Räume, Fächer), Moduswechsel Stundenplan/Vertretung, Fensterfunktionen
// (anordnen, Fensteranordnungen speichern/laden, alle schließen).
import { zustand, modusSetzen } from '../kern/zustand.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { einstellungLesen, einstellungSchreiben } from '../kern/api.js';
import { menue, formular, frage, hinweis } from '../kern/dialog.js';

const ANORDNUNGEN = 'fenster.anordnungen';

function elementMenue(typ, mehrzahl) {
  const e = [
    { text: 'Stammdaten', symbol: typ === 'lehrer' ? 'lehrer' : typ === 'raum' ? 'raeume' : typ === 'fach' ? 'faecher' : 'klassen', befehl: 'stammdaten.oeffnen', args: { typ } },
  ];
  if (typ !== 'raum') e.push({ text: 'Unterricht', symbol: 'unterricht', befehl: 'unterricht.oeffnen', args: { sicht: typ } });
  e.push(null, { text: `${mehrzahl}plan`, symbol: 'stundenplan', befehl: 'stundenplan.oeffnen', args: { typ } });
  return e;
}

async function anordnungenMenue() {
  const knopf = document.activeElement;
  const r = knopf?.getBoundingClientRect?.() || { left: 200, bottom: 160 };
  const alle = (await einstellungLesen(ANORDNUNGEN)) || {};
  const namen = Object.keys(alle).sort();
  menue(r.left, r.bottom + 2, [
    { text: 'Aktuelle Anordnung speichern …', symbol: 'speichern', aktion: anordnungSpeichern },
    null,
    namen.length ? { titel: 'Laden' } : { text: 'Noch keine gespeicherten Anordnungen', deaktiviert: true },
    ...namen.map((n) => ({ text: n, symbol: 'anordnung', aktion: () => fensterverwaltung.anordnungLaden(alle[n]) })),
    namen.length ? null : undefined,
    namen.length ? { text: 'Löschen', symbol: 'loeschen', menue: namen.map((n) => ({ text: n, aktion: () => anordnungLoeschen(n) })) } : undefined,
  ].filter((x) => x !== undefined));
}

async function anordnungSpeichern() {
  const w = await formular('Fensteranordnung speichern', [{ name: 'name', titel: 'Name', pflicht: true, hinweis: 'Gleicher Name überschreibt eine vorhandene Anordnung.' }], { okText: 'Speichern' });
  if (!w) return;
  const alle = (await einstellungLesen(ANORDNUNGEN)) || {};
  alle[w.name.trim()] = fensterverwaltung.anordnung();
  await einstellungSchreiben(ANORDNUNGEN, alle);
  hinweis(`Fensteranordnung „${w.name}“ gespeichert.`, 'ok');
}

async function anordnungLoeschen(name) {
  if (!(await frage(`Fensteranordnung „${name}“ löschen?`))) return;
  const alle = (await einstellungLesen(ANORDNUNGEN)) || {};
  delete alle[name];
  await einstellungSchreiben(ANORDNUNGEN, alle);
}

/** Standard-Fenstergruppe wie im Desktop: Unterricht links, Klassenplan rechts. */
function standardFenster() {
  fensterverwaltung.alleSchliessen();
  fensterverwaltung.oeffnen('unterricht', { sicht: 'klasse' }, { andock: 'links' });
  fensterverwaltung.oeffnen('stundenplan', { typ: 'klasse' }, { andock: 'rechts' });
}

function modusUmschalten() {
  const neu = zustand.modus === 'vertretung' ? 'stundenplan' : 'vertretung';
  modusSetzen(neu);
  try { localStorage.setItem('studio.modus', neu); } catch { /* privat */ }
  hinweis(neu === 'vertretung' ? 'Vertretungsmodus' : 'Stundenplanmodus', 'info', 1500);
}

export default {
  id: 'start',
  befehle: {
    'fenster.anordnen': ({ art }) => fensterverwaltung.anordnen(art),
    'fenster.alleSchliessen': () => fensterverwaltung.alleSchliessen(),
    'fenster.anordnungen': anordnungenMenue,
    'fenster.standard': standardFenster,
    'modus.umschalten': modusUmschalten,
  },
  ribbon: [
    { id: 'start.klassen', reiter: 'Start', gruppe: 'Übersicht', text: 'Klassen', symbol: 'klassen', gross: true, braucht: 'dokument', menue: elementMenue('klasse', 'Klassen') },
    { id: 'start.lehrer', reiter: 'Start', gruppe: 'Übersicht', text: 'Lehrer', symbol: 'lehrer', gross: true, braucht: 'dokument', menue: elementMenue('lehrer', 'Lehrer') },
    { id: 'start.raeume', reiter: 'Start', gruppe: 'Übersicht', text: 'Räume', symbol: 'raeume', gross: true, braucht: 'dokument', menue: elementMenue('raum', 'Raum') },
    { id: 'start.faecher', reiter: 'Start', gruppe: 'Übersicht', text: 'Fächer', symbol: 'faecher', gross: true, braucht: 'dokument', menue: elementMenue('fach', 'Fach') },
    { id: 'start.vertretungsmodus', reiter: 'Start', gruppe: 'Module', text: 'Vertretungs-planung', symbol: 'vertretung', gross: true, befehl: 'modus.umschalten', taste: 'Strg+Umschalt+V',
      gedrueckt: () => zustand.modus === 'vertretung', hinweis: 'Vertretungsmodus ein/aus: eigene Reiter, grüne Oberfläche' },
    { id: 'start.fenster.standard', reiter: 'Start', gruppe: 'Fenster', text: 'Standard-Fenstergruppe', symbol: 'fenster', befehl: 'fenster.standard', braucht: 'dokument' },
    { id: 'start.fenster.anordnungen', reiter: 'Start', gruppe: 'Fenster', text: 'Fensteranordnungen', symbol: 'anordnung', befehl: 'fenster.anordnungen', hinweis: 'Eigene Fensteranordnungen speichern, laden und löschen' },
    { id: 'start.fenster.alle', reiter: 'Start', gruppe: 'Fenster', text: 'Alle schließen', symbol: 'schliessen', befehl: 'fenster.alleSchliessen' },
    { id: 'start.fenster.ueberlappend', reiter: 'Start', gruppe: 'Fenster', text: 'Überlappend', symbol: 'ueberlappend', befehl: 'fenster.anordnen', args: { art: 'ueberlappend' } },
    { id: 'start.fenster.nebeneinander', reiter: 'Start', gruppe: 'Fenster', text: 'Nebeneinander', symbol: 'nebeneinander', befehl: 'fenster.anordnen', args: { art: 'nebeneinander' } },
    { id: 'start.fenster.untereinander', reiter: 'Start', gruppe: 'Fenster', text: 'Untereinander', symbol: 'untereinander', befehl: 'fenster.anordnen', args: { art: 'untereinander' } },
    { id: 'vertretung.modus', reiter: 'Vertretungsplanung', gruppe: 'Modus', text: 'Vertretungs-planung', symbol: 'vertretung', gross: true, befehl: 'modus.umschalten',
      gedrueckt: () => zustand.modus === 'vertretung', hinweis: 'Zurück zur Stundenplanung (Strg+Umschalt+V)' },
  ],
  start() {
    let m = 'stundenplan';
    try { m = localStorage.getItem('studio.modus') || m; } catch { /* privat */ }
    if (m === 'vertretung') modusSetzen('vertretung');
  },
};
