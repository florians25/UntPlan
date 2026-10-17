// Modul „Import/Export“ (Desktop: Datei → Import/Export, Module → Abteilungsplanung): Schild-NRW-CSV (Lehrer, Klassen),
// Schüler mit Kurswahlen, GPU-Dateien (Import/Export), Unterrichtsverteilung, Info-Stundenplan, Lehrer-Kalender (iCal),
// UntPlanWeb-Paket, Abteilungsdatei exportieren/zusammenführen sowie Schuldatei (.up) und BAL – alles in einem Fenster.
import { h } from '../kern/dom.js';
import { symbol } from '../kern/symbole.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { meldung, hinweis, laden, fehlerMelden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { befehl } from '../kern/modulsystem.js';

/** Dateiname aus Content-Disposition (filename* bevorzugt). */
function dateiname(cd, standard) {
  const stern = /filename\*=UTF-8''([^;]+)/i.exec(cd || '');
  if (stern) { try { return decodeURIComponent(stern[1]); } catch { /* weiter */ } }
  const einfach = /filename="?([^";]+)"?/i.exec(cd || '');
  return einfach ? einfach[1] : standard;
}

/** Datei von der API laden und im Browser speichern (Fehler des Servers kommen als Meldung statt als Seite). */
export async function dateiHerunterladen(pfad, standard = 'Datei') {
  const r = await api.roh(pfad);
  const blob = await r.blob();
  const url = URL.createObjectURL(blob);
  const a = h('a', { href: url, download: dateiname(r.headers.get('Content-Disposition'), standard), style: { display: 'none' } });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

/** Eine oder mehrere Dateien wählen (Promise mit File[]; leer = abgebrochen). */
export function dateienWaehlen(accept = '', mehrere = false) {
  return new Promise((fertig) => {
    const input = h('input', { type: 'file', accept, multiple: mehrere, style: { display: 'none' } });
    input.addEventListener('change', () => { fertig([...(input.files || [])]); input.remove(); });
    input.addEventListener('cancel', () => { fertig([]); input.remove(); });
    document.body.append(input);
    input.click();
  });
}

/** Mehrere Dateien im Feld „datei“ hochladen. */
export function dateienHochladen(pfad, dateien, felder = {}) {
  const fd = new FormData();
  for (const d of dateien) fd.append('datei', d, d.name);
  for (const [k, v] of Object.entries(felder)) fd.append(k, String(v));
  return api.post(pfad, fd);
}

function berichtText(b) {
  return [b.text || `${b.art}: ${b.neu} neu, ${b.aktualisiert} aktualisiert`, ...(b.hinweise || []).slice(0, 40)].join('\n');
}

async function csvImport(art, titel) {
  const [datei] = await dateienWaehlen('.csv,.txt,text/csv,text/plain');
  if (!datei) return null;
  const b = await dateienHochladen(api.dok(`/austausch/csv/${art}`), [datei]);
  meldung(berichtText(b), titel);
  return b;
}

async function gpuImport() {
  const dateien = await dateienWaehlen('.txt,.TXT,.zip', true);
  if (!dateien.length) return null;
  const b = await dateienHochladen(api.dok('/austausch/gpu'), dateien);
  meldung(berichtText(b), 'GPU-Import');
  return b;
}

function austauschFenster(f) {
  f.titel('Import/Export');
  const inhalt = h('div', { class: 'ax-inhalt' });
  const protokoll = h('div', { class: 'ax-protokoll leise' }, 'Hier erscheint das Ergebnis des letzten Imports.');
  f.inhalt.append(inhalt);
  const schreiben = darfSchreiben();
  let info = { abteilungen: [], lehrer: [], zaehler: {} };

  const aktion = (sym, text, hinweisText, f2, nurSchreiben = false) =>
    h('button', { class: 'knopf ax-knopf', type: 'button', title: hinweisText || text, disabled: nurSchreiben && !schreiben, onclick: async () => {
      const weg = laden(inhalt);
      try {
        const b = await f2();
        if (b && typeof b === 'object' && 'neu' in b) protokoll.replaceChildren(...berichtText(b).split('\n').map((z) => h('div', null, z)));
      } catch (e) { fehlerMelden(e); } finally { weg(); }
    } }, symbol(sym), h('span', null, text));

  const karte = (titel, ...kinder) => h('section', { class: 'ax-karte' }, h('h3', null, titel), ...kinder);

  function zeichnen() {
    const lehrerWahl = h('select', { class: 'eingabe', title: 'Lehrkraft für den Kalenderexport' }, info.lehrer.map((l) => h('option', { value: l.id }, `${l.kurz} – ${l.lang}`)));
    const heute = new Date().toISOString().slice(0, 10);
    const plus = new Date(Date.now() + 27 * 86400000).toISOString().slice(0, 10);
    const von = h('input', { class: 'eingabe', type: 'date', value: heute });
    const bis = h('input', { class: 'eingabe', type: 'date', value: plus });
    const abteilung = h('select', { class: 'eingabe' }, info.abteilungen.length
      ? info.abteilungen.map((a) => h('option', { value: a.id }, `${a.kurz}${a.lang ? ` – ${a.lang}` : ''} (${a.unterrichte} Unterrichte)`))
      : h('option', { value: '' }, 'keine Abteilungen angelegt'));
    const z = info.zaehler || {};
    inhalt.replaceChildren(
      karte('Schuldatei',
        h('p', { class: 'leise' }, `${info.schulname || ''} · ${z.lehrer ?? 0} Lehrkräfte, ${z.klassen ?? 0} Klassen, ${z.schueler ?? 0} Schüler, ${z.unterrichte ?? 0} Unterrichte, ${z.stunden ?? 0} verplante Stunden`),
        h('div', { class: 'ax-reihe' },
          aktion('herunterladen', 'Schuldatei herunterladen (.up)', 'Aktuellen Stand als UntPlan-Schuldatei speichern (mit der Desktop-Anwendung austauschbar)', () => befehl('datei.herunterladen')),
          aktion('hochladen', 'Schuldatei hochladen …', 'Eine .up- oder BAL-Datei als neue Schuldatei auf den Server laden', () => befehl('datei.hochladen'), true),
          aktion('bal', 'BAL-Datei importieren …', 'Stundenplan aus einer WILLI2-BAL-Datei in diese Schuldatei übernehmen', () => befehl('datei.balImport'), true))),
      karte('Import',
        h('div', { class: 'ax-reihe' },
          aktion('import', 'Schild-NRW: Lehrer (CSV) …', 'Lehrkräfte aus einer Schild-NRW-Exportdatei anlegen bzw. aktualisieren', () => csvImport('lehrer', 'Import Lehrer'), true),
          aktion('import', 'Schild-NRW: Klassen (CSV) …', 'Klassen aus einer Schild-NRW-Exportdatei anlegen bzw. aktualisieren', () => csvImport('klassen', 'Import Klassen'), true),
          aktion('import', 'Schüler mit Kurswahlen (CSV) …', 'Schülerliste (Name, Vorname, Klasse, Kurse) übernehmen', () => csvImport('schueler', 'Import Schüler'), true),
          aktion('import', 'GPU-Dateien (Umstieg) …', 'GPU001–GPU006 (mehrere Dateien oder ein ZIP) übernehmen', gpuImport, true))),
      karte('Export',
        h('div', { class: 'ax-reihe' },
          aktion('export', 'GPU-Dateien (ZIP)', 'GPU001–GPU006 im Latin-1-Format', () => dateiHerunterladen(api.dok('/austausch/gpu'), 'GPU.zip')),
          aktion('export', 'Unterrichtsverteilung (CSV)', 'Alle Unterrichte mit Kopplungszeilen als CSV', () => dateiHerunterladen(api.dok('/austausch/unterricht.csv'), 'Unterrichtsverteilung.csv')),
          aktion('web', 'Info-Stundenplan (HTML, ZIP)', 'Statische HTML-Seiten aller Pläne', () => dateiHerunterladen(api.dok('/austausch/infostundenplan.zip'), 'Info-Stundenplan.zip')),
          aktion('web', 'UntPlanWeb-Paket (JSON)', 'Paket im Schema untplanweb/1 als Datei (z. B. für einen manuellen Upload)', () => dateiHerunterladen(api.dok('/austausch/untplanweb.json'), 'untplanweb.json'))),
        h('div', { class: 'ax-reihe ax-ical' }, h('span', null, 'Lehrer-Kalender (iCal):'), lehrerWahl, h('span', null, 'von'), von, h('span', null, 'bis'), bis,
          aktion('kalender', 'Herunterladen', 'Stundenplan der Lehrkraft als iCalendar-Datei (.ics)', () => {
            if (!lehrerWahl.value) throw new Error('Bitte eine Lehrkraft wählen.');
            return dateiHerunterladen(api.dok(`/austausch/ical/${lehrerWahl.value}?von=${von.value}&bis=${bis.value}`), 'Stundenplan.ics');
          }))),
      karte('Abteilungsplanung',
        h('p', { class: 'leise' }, 'Abteilungsdatei: alle Stammdaten, aber nur Unterrichte der Abteilung beweglich (andere fixiert). Nach der Bearbeitung wieder zusammenführen.'),
        h('div', { class: 'ax-reihe' }, abteilung,
          aktion('export', 'Abteilungsdatei exportieren', 'Schuldatei der Abteilung herunterladen', () => {
            if (!abteilung.value) throw new Error('Bitte zuerst unter Dateneingabe → Abteilungen eine Abteilung anlegen.');
            return dateiHerunterladen(api.dok(`/austausch/abteilung/${abteilung.value}`), 'Abteilung.up');
          }),
          aktion('import', 'Abteilungsdatei zusammenführen …', 'Unterrichte und Stunden der Abteilung aus einer Abteilungsdatei übernehmen', async () => {
            if (!abteilung.value) throw new Error('Bitte eine Abteilung wählen.');
            const [datei] = await dateienWaehlen('.up,.untplan,application/json');
            if (!datei) return null;
            const b = await dateienHochladen(api.dok(`/austausch/abteilung/${abteilung.value}`), [datei]);
            hinweis(b.text, 'ok');
            return b;
          }, true))),
      karte('Protokoll', protokoll));
  }

  async function laden_() {
    try { info = await api.get(api.dok('/austausch')); } catch (e) { fehlerMelden(e); }
    zeichnen();
  }
  f.an('daten', (e) => { if (betrifft(e, 'Stammdaten')) laden_(); });
  laden_();
}

const oeffnen = () => { if (zustand.dokument) fensterverwaltung.oeffnen('austausch', {}); };

export default {
  id: 'austausch',
  css: ['css/module/austausch.css'],
  fenster: {
    austausch: { titel: 'Import/Export', symbol: 'import', breite: 820, hoehe: 600, einzig: () => 'austausch', erstellen: austauschFenster },
  },
  befehle: {
    'austausch.oeffnen': oeffnen,
    'austausch.schueler': () => csvImport('schueler', 'Import Schüler'),
    'austausch.lehrer': () => csvImport('lehrer', 'Import Lehrer'),
    'austausch.klassen': () => csvImport('klassen', 'Import Klassen'),
    'austausch.gpu': gpuImport,
  },
  ribbon: [
    {
      id: 'eingabe.importexport', reiter: 'Dateneingabe', gruppe: 'Hilfsmittel', text: 'Import/Export', symbol: 'import', braucht: 'dokument', hinweis: 'Import und Export (CSV, GPU, iCal, Info-Stundenplan, Abteilungsdatei …)',
      menue: [
        { text: 'Import/Export …', symbol: 'import', befehl: 'austausch.oeffnen' },
        null,
        { text: 'Schild-NRW: Lehrer importieren (CSV) …', symbol: 'import', befehl: 'austausch.lehrer', braucht: 'schreiben' },
        { text: 'Schild-NRW: Klassen importieren (CSV) …', symbol: 'import', befehl: 'austausch.klassen', braucht: 'schreiben' },
        { text: 'Schüler importieren (CSV) …', symbol: 'import', befehl: 'austausch.schueler', braucht: 'schreiben' },
        { text: 'GPU-Dateien importieren (Umstieg) …', symbol: 'import', befehl: 'austausch.gpu', braucht: 'schreiben' },
        { text: 'BAL-Datei (WILLI2) importieren …', symbol: 'bal', befehl: 'datei.balImport', braucht: 'schreiben' },
        null,
        { text: 'GPU-Dateien exportieren', symbol: 'export', aktion: () => dateiHerunterladen(api.dok('/austausch/gpu'), 'GPU.zip') },
        { text: 'Unterrichtsverteilung (CSV)', symbol: 'export', aktion: () => dateiHerunterladen(api.dok('/austausch/unterricht.csv'), 'Unterrichtsverteilung.csv') },
      ],
    },
    { id: 'kurs.import', reiter: 'Kursplanung', gruppe: 'Kursplanung', text: 'Schüler importieren', symbol: 'import', gross: true, befehl: 'austausch.schueler', braucht: 'schreiben', hinweis: 'Schüler mit Kurswahlen aus einer CSV-Datei übernehmen' },
  ],
};
