// Modul „Unterrichtsplanung / Wertrechnung“ und „Vertretungsstatistik“ (Desktop: WertrechnungFenster mit Lehrer Soll/Ist,
// Unterrichts-Matrix und Stundentafel; StatistikFenster mit Abrechnung, Einzelnachweis, Mehrarbeit NRW, Monatsabrechnung,
// Entfalltage, Vertretungs-Übersicht und Wochenwerte – als Tabelle, HTML-Dokument und CSV).
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { formular, laden, fehlerMelden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { dateiHerunterladen } from './austausch.js';

const REITER = [['werte', 'Lehrer: Soll / Ist'], ['matrix', 'Unterrichts-Matrix'], ['tafel', 'Stundentafel']];

function reiterLeiste(f, aktiv, wechseln) {
  const leiste = h('div', { class: 'abr-reiter' }, REITER.map(([id, text]) => h('button', { type: 'button', class: id === aktiv ? 'aktiv' : '', onclick: () => wechseln(id) }, text)));
  f.inhalt.append(leiste);
  return leiste;
}

function wertrechnungFenster(f, p) {
  f.titel('Unterrichtsplanung / Wertrechnung');
  const schreiben = darfSchreiben();
  let reiter = p.tab || 'werte';
  let matrixArt = 2;
  const leiste = reiterLeiste(f, reiter, (id) => { reiter = id; f.parameterSetzen({ tab: id }); aufbauen(); });
  const werkzeug = h('div', { class: 'abr-werkzeug' });
  const flaeche = h('div', { class: 'abr-flaeche' });
  const summe = h('div', { class: 'abr-summe' });
  f.inhalt.append(werkzeug, flaeche, summe);
  let tabellen = [];

  function leeren() { for (const t of tabellen) t.entfernen?.(); tabellen = []; flaeche.replaceChildren(); werkzeug.replaceChildren(); summe.textContent = ''; }

  async function werte() {
    const d = await api.get(api.dok('/abrechnung/werte'));
    const t = new Tabelle(flaeche, {
      zustandSchluessel: 'tabelle.abrechnung.werte',
      beiDoppelklick: (z) => fensterverwaltung.oeffnen('stundenplan', { typ: 'lehrer', element: z.id }),
      aendern: schreiben ? async (z, s, wert) => { const neu = await api.patch(api.dok(`/abrechnung/werte/${z.id}`), { feld: s.schluessel, wert }, { quelle: f.id }); anzeigenWerte(neu); return null; } : undefined,
      spalten: d.spalten.map((titel, i) => {
        const schluessel = i === 2 ? 'wochensoll' : i === 6 ? 'wertkorrektur' : `s${i}`;
        const edit = schreiben && (i === 2 || i === 6);
        return {
          schluessel, titel, breite: [65, 150, 80, 80, 60, 60, 90, 70, 50, 60, 200][i], art: edit ? 'Dezimal' : 'NurLesen', editierbar: edit, wert: (z) => z.werte[i],
          farbe: i === 7 ? (z) => (z.differenz > 0.5 ? '#ffdcc8' : z.differenz < -0.5 ? '#dce6ff' : '#e1f5e1') : edit ? () => '#ffffee' : undefined,
        };
      }),
    });
    tabellen.push(t);
    const anzeigenWerte = (x) => { t.daten(x.zeilen); summe.textContent = x.summe; };
    anzeigenWerte(d);
  }

  async function matrix() {
    const art = h('select', { class: 'eingabe' }, ['Lehrer', 'Stunden', 'Lehrer + Stunden'].map((t, i) => h('option', { value: i, selected: i === matrixArt }, t)));
    art.addEventListener('change', () => { matrixArt = Number(art.value); aufbauen(); });
    werkzeug.append(h('span', null, 'Anzeige:'), art, h('span', { class: 'leise' }, 'Doppelklick öffnet den Unterricht der Klasse.'));
    const d = await api.get(api.dok(`/abrechnung/matrix?art=${matrixArt}`));
    const t = new Tabelle(flaeche, {
      beiDoppelklick: (z) => fensterverwaltung.oeffnen('unterricht', { sicht: 'klasse', element: z.id }),
      spalten: d.spalten.map((titel, i) => ({ schluessel: `m${i}`, titel, breite: i === 0 ? 60 : matrixArt === 1 ? 44 : 76, art: 'NurLesen', editierbar: false, wert: (z) => z.werte[i] })),
    });
    tabellen.push(t);
    t.daten(d.zeilen);
    summe.textContent = `${d.zeilen.length} Klassen · ${d.spalten.length - 2} Fächer`;
  }

  async function tafel() {
    if (schreiben) {
      werkzeug.append(
        h('button', { class: 'knopf', type: 'button', onclick: async () => {
          const w = await formular('Stundentafel', [{ name: 'jahrgang', titel: 'Jahrgang (z. B. 5, 6, EF, Q1)', pflicht: true }]);
          if (w) { await api.post(api.dok('/abrechnung/stundentafel/jahrgang'), w, { quelle: f.id }).catch(fehlerMelden); aufbauen(); }
        } }, 'Jahrgang hinzufügen …'),
        h('button', { class: 'knopf', type: 'button', onclick: async () => { await api.post(api.dok('/abrechnung/stundentafel/nrw'), {}, { quelle: f.id }).catch(fehlerMelden); aufbauen(); } }, 'NRW-Jahrgänge eintragen'));
    }
    werkzeug.append(h('span', { class: 'leise' }, 'Wochenstunden je Jahrgang und Fach. Rechts: Abweichungen zum eingegebenen Unterricht.'));
    const d = await api.get(api.dok('/abrechnung/stundentafel'));
    const links = h('div', { class: 'abr-links' });
    const rechts = h('div', { class: 'abr-rechts' }, h('div', { class: 'abr-titel' }, 'Abgleich Stundentafel ↔ Unterricht'));
    flaeche.append(links, rechts);
    const t = new Tabelle(links, {
      id: (z) => z.jahrgang,
      aendern: schreiben ? async (z, s, wert) => { await api.put(api.dok('/abrechnung/stundentafel'), { jahrgang: z.jahrgang, fach: s.fach, wert }, { quelle: f.id }); aufbauen(); return null; } : undefined,
      spalten: [{ schluessel: 'jahrgang', titel: 'Jahrgang', breite: 70, art: 'NurLesen', editierbar: false },
        ...d.faecher.map((x, i) => ({ schluessel: `f${x.id}`, fach: x.id, titel: x.kurz, breite: 44, art: schreiben ? 'Dezimal' : 'NurLesen', editierbar: schreiben, wert: (z) => z.werte[i] })),
        { schluessel: 'summe', titel: 'Summe', breite: 60, art: 'NurLesen', editierbar: false }],
    });
    t.daten(d.zeilen);
    const a = new Tabelle(rechts, {
      id: (z) => `${z.klasse}|${z.fach}`, zeilenKlasse: (z) => (z.zuWenig ? 'abr-zuwenig' : 'abr-zuviel'),
      spalten: [['klasse', 'Klasse', 70], ['fach', 'Fach', 70], ['soll', 'Soll', 55], ['ist', 'Ist', 55], ['differenz', 'Differenz', 70]].map(([schluessel, titel, breite]) => ({ schluessel, titel, breite, art: 'NurLesen', editierbar: false })),
    });
    a.daten(d.abgleich);
    tabellen.push(t, a);
    summe.textContent = `${d.zeilen.length} Jahrgänge · ${d.abgleich.length} Abweichungen`;
  }

  async function aufbauen() {
    for (const [i, b] of [...leiste.children].entries()) b.classList.toggle('aktiv', REITER[i][0] === reiter);
    leeren();
    const weg = laden(f.inhalt);
    try { await ({ werte, matrix, tafel })[reiter](); } catch (e) { fehlerMelden(e); } finally { weg(); }
  }
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Stammdaten', 'Unterricht', 'Stundenplan', 'Einstellungen')) aufbauen(); });
  aufbauen();
}

// ---------------------------------------------------------------------------------------------- Vertretungsstatistik

function statistikFenster(f) {
  f.titel('Vertretungsstatistik');
  const jetzt = new Date();
  const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const von = h('input', { class: 'eingabe', type: 'date', value: iso(new Date(jetzt.getFullYear(), jetzt.getMonth(), 1)) });
  const bis = h('input', { class: 'eingabe', type: 'date', value: iso(new Date(jetzt.getFullYear(), jetzt.getMonth() + 1, 0)) });
  const bericht = h('select', { class: 'eingabe', style: { width: '250px' } });
  const lehrer = h('select', { class: 'eingabe', style: { width: '100px' } });
  const zeitraum = h('select', { class: 'eingabe', title: 'Zeitraum' }, ['Zeitraum …', 'Aktueller Monat', 'Vormonat', 'Aktuelle Woche', 'Schuljahr'].map((t, i) => h('option', { value: i }, t)));
  f.werkzeuge.append(bericht, h('span', null, 'Lehrer'), lehrer, h('span', null, 'von'), von, h('span', null, 'bis'), bis, zeitraum);
  f.trenner();
  const q = (alle = false) => `bericht=${bericht.value}&von=${von.value}&bis=${bis.value}${lehrer.value ? `&lehrer=${lehrer.value}` : ''}${alle ? '&alle=true' : ''}`;
  f.werkzeug({ text: 'HTML für alle Lehrer', symbol: 'web', titel: 'Dokument (HTML), je Lehrkraft eine Seite', aktion: () => window.open(api.dok(`/abrechnung/statistik.html?${q(true)}`), '_blank') });
  f.werkzeug({ text: 'HTML', symbol: 'web', titel: 'Dokument (HTML) zum Drucken', aktion: () => window.open(api.dok(`/abrechnung/statistik.html?${q()}`), '_blank') });
  f.werkzeug({ text: 'CSV', symbol: 'export', titel: 'Export CSV', aktion: () => dateiHerunterladen(api.dok(`/abrechnung/statistik.csv?${q()}`), 'Statistik.csv') });
  const summe = h('div', { class: 'abr-summe gross' });
  let tabelle = null;
  let mitLehrer = [];

  zeitraum.addEventListener('change', () => {
    const d = new Date();
    const i = Number(zeitraum.value);
    if (i === 1) { von.value = iso(new Date(d.getFullYear(), d.getMonth(), 1)); bis.value = iso(new Date(d.getFullYear(), d.getMonth() + 1, 0)); }
    if (i === 2) { von.value = iso(new Date(d.getFullYear(), d.getMonth() - 1, 1)); bis.value = iso(new Date(d.getFullYear(), d.getMonth(), 0)); }
    if (i === 3) { const m = new Date(d); m.setDate(d.getDate() - ((d.getDay() + 6) % 7)); const s = new Date(m); s.setDate(m.getDate() + 6); von.value = iso(m); bis.value = iso(s); }
    if (i === 4) { const j = d.getMonth() >= 7 ? d.getFullYear() : d.getFullYear() - 1; von.value = `${j}-08-01`; bis.value = `${j + 1}-07-31`; }
    zeitraum.value = '0';
    aufbauen();
  });
  for (const el of [von, bis, bericht, lehrer]) el.addEventListener('change', aufbauen);

  async function aufbauen() {
    lehrer.disabled = !mitLehrer.includes(Number(bericht.value));
    const weg = laden(f.inhalt);
    try {
      const b = await api.get(api.dok(`/abrechnung/statistik?${q()}`));
      tabelle?.el.remove();
      summe.remove();
      tabelle = new Tabelle(f.inhalt, {
        id: (z) => z.i,
        zeilenKlasse: (z) => (z.markierung ? `abr-${z.markierung}` : ''),
        beiDoppelklick: (z) => { if (b.nr === 0 && z.lehrer) { lehrer.value = z.lehrer; bericht.value = '1'; aufbauen(); } },
        spalten: b.kopf.map((titel, i) => ({ schluessel: `c${i}`, titel, breite: b.breiten[i], art: 'NurLesen', editierbar: false, wert: (z) => z.werte[i] })),
      });
      tabelle.daten(b.zeilen.map((z, i) => ({ ...z, i })));
      summe.replaceChildren(...b.summe.split('\n').map((z) => h('div', null, z)));
      f.inhalt.append(summe);
      f.titel(`Vertretungsstatistik – ${bericht.selectedOptions[0]?.textContent || ''}`);
    } catch (e) { fehlerMelden(e); } finally { weg(); }
  }

  (async () => {
    try {
      const [berichte, el] = await Promise.all([api.get(api.dok('/abrechnung/berichte')), api.get(api.dok('/elemente?typen=lehrer'))]);
      mitLehrer = berichte.filter((x) => x.lehrer).map((x) => x.nr);
      bericht.replaceChildren(...berichte.map((x) => h('option', { value: x.nr }, x.text)));
      lehrer.replaceChildren(...(el.lehrer || []).map((l) => h('option', { value: l.id }, l.k)));
      await aufbauen();
    } catch (e) { fehlerMelden(e); }
  })();
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Vertretung', 'Stammdaten')) aufbauen(); });
}

const wert = (tab) => () => { if (zustand.dokument) fensterverwaltung.oeffnen('abrechnung.wertrechnung', { tab }); };

export default {
  id: 'abrechnung',
  css: ['css/module/abrechnung.css'],
  fenster: {
    'abrechnung.wertrechnung': { titel: 'Unterrichtsplanung / Wertrechnung', symbol: 'statistik', breite: 1060, hoehe: 560, einzig: () => 'wert', erneutGeoeffnet: (fe, p) => fe.parameterSetzen(p), erstellen: wertrechnungFenster },
    'abrechnung.statistik': { titel: 'Vertretungsstatistik', symbol: 'statistik', breite: 1040, hoehe: 560, einzig: () => 'stat', erstellen: statistikFenster },
  },
  befehle: {
    'abrechnung.werte': wert('werte'),
    'abrechnung.matrix': wert('matrix'),
    'abrechnung.stundentafel': wert('tafel'),
    'abrechnung.statistik': () => { if (zustand.dokument) fensterverwaltung.oeffnen('abrechnung.statistik', {}); },
  },
  ribbon: [
    { id: 'start.wertrechnung', reiter: 'Start', gruppe: 'Module', text: 'Unterrichtsplanung', symbol: 'statistik', befehl: 'abrechnung.werte', braucht: 'dokument', hinweis: 'Unterrichtsplanung und Wertrechnung (Soll/Ist, Matrix, Stundentafel)' },
    { id: 'eingabe.stundentafel', reiter: 'Dateneingabe', gruppe: 'Spezielle Daten', text: 'Stundentafel', symbol: 'statistik', befehl: 'abrechnung.stundentafel', braucht: 'dokument' },
    { id: 'vertretung.statistik', reiter: 'Vertretungsplanung', gruppe: 'Vertretungsplanung', text: 'Vertretungs-statistik', symbol: 'statistik', gross: true, befehl: 'abrechnung.statistik', braucht: 'dokument', hinweis: 'Vertretungsabrechnung, Zähler, Mehrarbeit NRW, Berichte' },
    { id: 'module.sollist', reiter: 'Module', gruppe: 'Unterrichtsplanung und Wertrechnung', text: 'Lehrer Soll/Ist', symbol: 'statistik', gross: true, befehl: 'abrechnung.werte', braucht: 'dokument' },
    { id: 'module.matrix', reiter: 'Module', gruppe: 'Unterrichtsplanung und Wertrechnung', text: 'Unterrichts-Matrix', symbol: 'uebersicht', gross: true, befehl: 'abrechnung.matrix', braucht: 'dokument' },
    { id: 'module.stundentafel', reiter: 'Module', gruppe: 'Unterrichtsplanung und Wertrechnung', text: 'Stundentafel', symbol: 'unterricht', gross: true, befehl: 'abrechnung.stundentafel', braucht: 'dokument' },
    { id: 'module.statistik', reiter: 'Module', gruppe: 'Unterrichtsplanung und Wertrechnung', text: 'Vertretungs-statistik', symbol: 'statistik', gross: true, befehl: 'abrechnung.statistik', braucht: 'dokument' },
  ],
};
