// Modul „Datei“: Datei-Bereich (Backstage) – Neu, Öffnen (Server-Liste, Hochladen .up/BAL), Speichern, Herunterladen,
// BAL-Import in die geöffnete Datei, Schließen, Konto (Passwort, Abmelden), Benutzerverwaltung (Admin), Info.
import { h, leeren, datumDe, groesse } from '../kern/dom.js';
import { symbol } from '../kern/symbole.js';
import { api } from '../kern/api.js';
import { zustand } from '../kern/zustand.js';
import { an, melden } from '../kern/ereignisse.js';
import { dokumentOeffnen, dokumentSchliessen, speichern, herunterladen } from '../kern/dokument.js';
import { frage, formular, dateiWaehlen, hinweis, fehlerMelden, laden, meldung } from '../kern/dialog.js';

let bereich = null;

function schliessen() {
  bereich?.remove();
  bereich = null;
  document.removeEventListener('keydown', escape, true);
}
function escape(e) { if (e.key === 'Escape' && !document.querySelector('.dialog-hintergrund')) { e.stopPropagation(); schliessen(); } }

function backstage({ seite } = {}) {
  if (!seite) seite = zustand.dokument ? 'info' : 'oeffnen';
  if (!bereich) {
    bereich = h('div', { class: 'backstage', role: 'dialog', 'aria-label': 'Datei' });
    document.body.append(bereich);
    document.addEventListener('keydown', escape, true);
  }
  leeren(bereich);
  const d = zustand.dokument, schreiben = !!zustand.benutzer?.darfSchreiben;
  const knopf = (id, text, sym, aktion, deaktiviert = false) =>
    h('button', { type: 'button', class: id === seite ? 'aktiv' : '', disabled: deaktiviert, dataset: { seite: id }, onclick: aktion || (() => backstage({ seite: id })) }, symbol(sym), text);
  const nav = h('nav', null,
    h('button', { type: 'button', class: 'zurueck', title: 'Zurück (Esc)', onclick: schliessen, disabled: !d }, '←'),
    knopf('info', 'Informationen', 'info', null, !d),
    knopf('neu', 'Neu', 'neu', null, !schreiben),
    knopf('oeffnen', 'Öffnen', 'oeffnen'),
    knopf('speichern', 'Speichern', 'speichern', () => speichern().then(schliessen).catch(fehlerMelden), !d || !schreiben),
    knopf('herunterladen', 'Herunterladen (.up)', 'herunterladen', () => herunterladen(), !d),
    knopf('import', 'BAL-Import', 'bal', null, !d || !schreiben),
    knopf('schliessen', 'Schließen', 'schliessen', async () => { await dokumentSchliessen(); backstage({ seite: 'oeffnen' }); }, !d),
    h('hr'),
    knopf('konto', 'Konto', 'benutzer'),
    zustand.benutzer?.rolle === 'Admin' ? knopf('benutzer', 'Benutzer', 'datenbank') : null,
    knopf('ueber', 'Über', 'hilfe'));
  const inhalt = h('div', { class: 'seite' });
  bereich.append(nav, inhalt);
  const seiten = { info: seiteInfo, neu: seiteNeu, oeffnen: seiteOeffnen, import: seiteImport, konto: seiteKonto, benutzer: seiteBenutzer, ueber: seiteUeber };
  (seiten[seite] || seiteOeffnen)(inhalt);
}

// ---------------------------------------------------------------------------------------------

function seiteInfo(el) {
  const d = zustand.dokument;
  if (!d) return seiteOeffnen(el);
  const z = d.zaehler || {};
  el.append(h('h1', null, 'Informationen'),
    h('table', { class: 'dokumentliste', style: { maxWidth: '640px' } }, h('tbody', null,
      [['Dokument', d.name], ['Schule', d.schulname], ['Schuljahr', d.schuljahr], ['Aktive Periode', `${d.periode}${d.periodeFixiert ? ' (fixiert)' : ''}`],
        ['Herkunft', d.herkunft], ['Angelegt von', d.besitzer], ['Stand', d.geaendert ? 'ungespeicherte Änderungen (werden automatisch gespeichert)' : d.gespeichert ? `gespeichert ${datumDe(d.gespeichert)}` : 'gespeichert'],
        ['Inhalt', `${z.klassen} Klassen, ${z.lehrer} Lehrkräfte, ${z.raeume} Räume, ${z.faecher} Fächer, ${z.unterrichte} Unterrichte, ${z.planstunden} verplante Stunden, ${z.nichtVerplant} nicht verplant`],
        ['Geöffnet in', `${d.abonnenten} Fenster(n)/Sitzung(en)`]]
        .map(([k, v]) => h('tr', null, h('td', { class: 'leise', style: { width: '160px' } }, k), h('td', null, v ?? ''))))),
    h('p', null, h('button', { class: 'knopf', type: 'button', onclick: umbenennen }, 'Umbenennen …')));
}

async function umbenennen() {
  const w = await formular('Dokument umbenennen', [{ name: 'name', titel: 'Name', wert: zustand.dokument.name, pflicht: true }]);
  if (!w) return;
  const stand = await api.put(api.dok('/name'), { name: w.name });
  zustand.dokument = stand;
  melden('stand', stand);
  backstage({ seite: 'info' });
}

function seiteNeu(el) {
  el.append(h('h1', null, 'Neu'),
    h('div', { class: 'kacheln' },
      kachel('neu', 'Leere Schuldatei', 'Neue Schule mit Fächern und Zeitraster (NRW-Vorgaben).', async () => {
        const w = await formular('Neue Schuldatei', [
          { name: 'schulname', titel: 'Schulname', pflicht: true },
          { name: 'schulform', titel: 'Schulform', art: 'auswahl', wert: 'GY', optionen: [['GY', 'Gymnasium'], ['GE', 'Gesamtschule'], ['RS', 'Realschule'], ['HS', 'Hauptschule'], ['SK', 'Sekundarschule'], ['GS', 'Grundschule'], ['BK', 'Berufskolleg'], ['FS', 'Förderschule']].map(([wert, text]) => ({ wert, text })) },
        ], { okText: 'Anlegen' });
        if (!w) return;
        await oeffnenMit(() => api.post('/api/dokumente/neu', w));
      }),
      kachel('beispiel', 'Beispielschule', 'Fiktives Gymnasium mit verplantem Stundenplan zum Ausprobieren.', () => oeffnenMit(() => api.post('/api/dokumente/beispiel'))),
      kachel('hochladen', 'Datei hochladen', 'Schuldatei (.up) oder BAL-Datei vom Rechner übernehmen.', hochladen)));
}

function kachel(sym, titel, text, aktion) {
  return h('button', { class: 'kachel', type: 'button', onclick: () => Promise.resolve().then(aktion).catch(fehlerMelden) }, symbol(sym), h('b', null, titel), h('span', { class: 'klein' }, text));
}

async function oeffnenMit(f) {
  const weg = laden(bereich);
  try {
    const erg = await f();
    const stand = erg.dokument || erg;
    await dokumentOeffnen(stand);
    schliessen();
    if (erg.bericht) importBerichtZeigen(erg.bericht);
    else hinweis(`„${stand.name}“ geöffnet.`, 'ok');
  } finally { weg(); }
}

function importBerichtZeigen(b) {
  meldung([`BAL-Import abgeschlossen: ${b.neu} neu, ${b.aktualisiert} aktualisiert.`, ...(b.hinweise || []).slice(0, 30)].join('\n'), 'BAL-Import');
}

async function hochladen(datei) {
  datei = datei instanceof File ? datei : await dateiWaehlen('.up,.untplan,.txt,.bal,.csv,text/plain');
  if (!datei) return;
  await oeffnenMit(() => api.hochladen('/api/dokumente/hochladen', datei));
}

async function seiteOeffnen(el) {
  const ablage = h('div', { class: 'ablage' }, 'Schuldatei (.up) oder BAL-Datei hierher ziehen – oder ', h('button', { class: 'knopf klein', type: 'button', onclick: () => hochladen().catch(fehlerMelden) }, symbol('hochladen'), 'Datei auswählen …'));
  ablage.addEventListener('dragover', (e) => { e.preventDefault(); ablage.classList.add('ueber'); });
  ablage.addEventListener('dragleave', () => ablage.classList.remove('ueber'));
  ablage.addEventListener('drop', (e) => { e.preventDefault(); ablage.classList.remove('ueber'); const f = e.dataTransfer.files?.[0]; if (f) hochladen(f).catch(fehlerMelden); });
  const tabelle = h('table', { class: 'dokumentliste' });
  el.append(h('h1', null, 'Öffnen'), zustand.benutzer?.darfSchreiben ? ablage : null, h('h3', null, 'Schuldateien auf dem Server'), tabelle);
  const { dokumente, zuletzt } = await api.get('/api/dokumente');
  const sortiert = [...dokumente].sort((a, b) => (zuletzt.indexOf(a.id) + 1 || 999) - (zuletzt.indexOf(b.id) + 1 || 999));
  tabelle.append(h('thead', null, h('tr', null, h('th', null, 'Name'), h('th', null, 'Geändert'), h('th', null, 'Größe'), h('th', null, 'Herkunft'), h('th', null, ''))),
    h('tbody', null, sortiert.length ? sortiert.map((d) => h('tr', { class: 'zeile', dataset: { id: d.id }, onclick: (e) => { if (!e.target.closest('button')) oeffnenMit(() => api.get(`/api/dokumente/${d.id}`)).catch(fehlerMelden); } },
      h('td', null, h('div', { class: 'name' }, d.name, zuletzt.includes(d.id) ? h('span', { class: 'leise' }, '  · zuletzt verwendet') : null), h('div', { class: 'leise' }, d.besitzer)),
      h('td', null, datumDe(d.geaendert), d.ungespeichert ? h('span', { class: 'leise' }, ' (offen)') : null),
      h('td', null, groesse(d.groesse)),
      h('td', { class: 'leise' }, d.herkunft),
      h('td', { class: 'aktionen' },
        h('a', { class: 'knopf klein', href: `/api/dokumente/${d.id}/herunterladen`, title: 'Herunterladen (.up)' }, symbol('herunterladen')),
        zustand.benutzer?.darfSchreiben ? h('button', { class: 'knopf klein gefahr', type: 'button', title: 'Löschen', onclick: () => loeschen(d) }, symbol('loeschen')) : null)))
      : h('tr', null, h('td', { colspan: 5, class: 'leise' }, 'Noch keine Schuldateien vorhanden – legen Sie unter „Neu“ eine an oder laden Sie eine hoch.'))));
}

async function loeschen(d) {
  if (!(await frage(`Die Schuldatei „${d.name}“ endgültig vom Server löschen?`, { ja: 'Löschen', gefahr: true }))) return;
  if (zustand.dokument?.id === d.id) await dokumentSchliessen();
  await api.loeschen(`/api/dokumente/${d.id}`);
  hinweis(`„${d.name}“ gelöscht.`, 'ok');
  backstage({ seite: 'oeffnen' });
}

function seiteImport(el) {
  const optionen = [['zeitraster', 'Zeitraster'], ['stammdaten', 'Stammdaten'], ['unterrichtErsetzen', 'Unterricht und Stundenplan der aktiven Periode ersetzen'], ['stundenplan', 'Stundenplan'], ['pausenaufsichten', 'Pausenaufsichten'], ['zeitwuensche', 'Zeitwünsche']];
  const haken = Object.fromEntries(optionen.map(([k]) => [k, h('input', { type: 'checkbox', checked: true })]));
  el.append(h('h1', null, 'BAL-Import'),
    h('p', null, 'Übernimmt einen Stundenplan aus einer BAL-Datei (WILLI2-Stundenplan) in die geöffnete Schuldatei – wie in der Desktop-Anwendung. Der Import ist rückgängig zu machen (Strg+Z).'),
    h('div', { style: { display: 'flex', flexDirection: 'column', gap: '6px', margin: '12px 0 18px' } }, optionen.map(([k, t]) => h('label', { class: 'kontrolle' }, haken[k], t))),
    h('button', { class: 'knopf primaer', type: 'button', onclick: async () => {
      const datei = await dateiWaehlen('.txt,.bal,.csv,text/plain');
      if (!datei) return;
      const weg = laden(bereich);
      try {
        const felder = Object.fromEntries(Object.entries(haken).map(([k, el2]) => [k, el2.checked]));
        const b = await api.hochladen(api.dok('/bal-import'), datei, felder);
        schliessen();
        importBerichtZeigen(b);
      } catch (e) { fehlerMelden(e); } finally { weg(); }
    } }, symbol('bal'), 'BAL-Datei wählen und importieren …'));
}

function seiteKonto(el) {
  const b = zustand.benutzer;
  el.append(h('h1', null, 'Konto'),
    h('p', null, `Angemeldet als ${b.anzeigename} (${b.name}), Rolle ${b.rolle}.`),
    h('div', { style: { display: 'flex', gap: '8px' } },
      h('button', { class: 'knopf', type: 'button', onclick: async () => {
        const w = await formular('Passwort ändern', [{ name: 'alt', titel: 'Bisheriges Passwort', art: 'passwort', pflicht: true }, { name: 'neu', titel: 'Neues Passwort', art: 'passwort', pflicht: true, hinweis: 'mindestens 6 Zeichen' }]);
        if (!w) return;
        await api.post('/api/ich/passwort', w).then(() => hinweis('Passwort geändert.', 'ok')).catch(fehlerMelden);
      } }, symbol('einstellungen'), 'Passwort ändern …'),
      h('button', { class: 'knopf', type: 'button', onclick: async () => { await api.post('/api/abmeldung'); location.reload(); } }, symbol('abmelden'), 'Abmelden')));
}

async function seiteBenutzer(el) {
  const liste = h('table', { class: 'dokumentliste', style: { maxWidth: '760px' } });
  el.append(h('h1', null, 'Benutzer'), h('p', null, h('button', { class: 'knopf', type: 'button', onclick: async () => {
    const w = await formular('Neuer Benutzer', [
      { name: 'name', titel: 'Benutzername', pflicht: true }, { name: 'anzeigename', titel: 'Anzeigename' },
      { name: 'rolle', titel: 'Rolle', art: 'auswahl', wert: 'Planer', optionen: [{ wert: 'Planer', text: 'Planer (bearbeiten)' }, { wert: 'Leser', text: 'Leser (nur lesen)' }, { wert: 'Admin', text: 'Administrator' }] },
      { name: 'passwort', titel: 'Passwort', art: 'passwort', pflicht: true, hinweis: 'mindestens 6 Zeichen' }], { okText: 'Anlegen' });
    if (!w) return;
    await api.post('/api/benutzer', w).then(() => backstage({ seite: 'benutzer' })).catch(fehlerMelden);
  } }, symbol('plus'), 'Benutzer anlegen …')), liste);
  const benutzer = await api.get('/api/benutzer');
  liste.append(h('thead', null, h('tr', null, ['Benutzer', 'Rolle', 'Letzte Anmeldung', ''].map((t) => h('th', null, t)))),
    h('tbody', null, benutzer.map((b) => h('tr', null,
      h('td', null, h('div', { class: 'name' }, b.anzeigename), h('div', { class: 'leise' }, b.name)), h('td', null, b.rolle), h('td', null, datumDe(b.letzteAnmeldung)),
      h('td', { class: 'aktionen' },
        h('button', { class: 'knopf klein', type: 'button', onclick: async () => {
          const w = await formular(`Passwort für ${b.name}`, [{ name: 'passwort', titel: 'Neues Passwort', art: 'passwort', pflicht: true }]);
          if (w) await api.post(`/api/benutzer/${encodeURIComponent(b.name)}/passwort`, w).then(() => hinweis('Passwort gesetzt.', 'ok')).catch(fehlerMelden);
        } }, 'Passwort …'),
        h('button', { class: 'knopf klein gefahr', type: 'button', onclick: async () => {
          if (await frage(`Benutzer „${b.name}“ löschen?`, { ja: 'Löschen', gefahr: true })) await api.loeschen(`/api/benutzer/${encodeURIComponent(b.name)}`).then(() => backstage({ seite: 'benutzer' })).catch(fehlerMelden);
        } }, symbol('loeschen')))))));
}

function seiteUeber(el) {
  el.append(h('h1', null, 'Über UntPlan Studio'),
    h('p', null, `${zustand.system?.anwendung || 'UntPlan Studio (Beta)'} ${zustand.system?.version || ''}`),
    h('p', null, 'Die Desktop-Anwendung UntPlan als Browser-Anwendung. Alle Berechnungen (Stundenplanung, Optimierung, Vertretungsplanung …) laufen auf dem Server mit derselben Fachlogik wie im Desktop; Schuldateien (.up) sind zwischen beiden austauschbar.'),
    h('p', { class: 'leise' }, 'Beta: Noch nicht alle Funktionen der Desktop-Anwendung sind verfügbar. Ausgegraute Knöpfe im Menüband folgen in späteren Ausbaustufen.'));
}

an('dokument', () => { if (bereich && zustand.dokument) schliessen(); });

export default {
  id: 'datei',
  befehle: {
    'datei.backstage': (a) => backstage(a),
    'datei.hochladen': () => hochladen(),
    'datei.speichern': () => speichern(),
    'datei.herunterladen': () => herunterladen(),
    'datei.schliessen': () => dokumentSchliessen(),
    'datei.balImport': () => backstage({ seite: 'import' }),
  },
  ribbon: [
    { id: 'eingabe.bal', reiter: 'Dateneingabe', gruppe: 'Hilfsmittel', text: 'BAL-Import', symbol: 'bal', befehl: 'datei.balImport', braucht: 'schreiben', hinweis: 'Stundenplan aus einer WILLI2-BAL-Datei übernehmen' },
  ],
};
