// Modul „Zeitwünsche“: Raster-Editor für die Zeitwünsche von Klassen, Lehrkräften, Räumen, Fächern und Unterrichten
// (+3 auf jeden Fall Unterricht … −3 keinesfalls Unterricht, Farben wie im Desktop). Wert wählen und Stunden mit gedrückter
// Maustaste (oder dem Finger) überstreichen; Klick auf „Tage“/„Vorm.“/„Nachm.“ setzt den ganzen (Halb-)Tag.
// Darunter die unbestimmten Zeitwünsche (z. B. „1 freier Tag, absolute Sperrung“).
import { h, leeren } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft, melden } from '../kern/ereignisse.js';
import { fehlerMelden, frage, laden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { befehl } from '../kern/modulsystem.js';

export const TYPEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', raum: 'Raum', fach: 'Fach', unterricht: 'Unterricht' };
const WERTE = [3, 2, 1, 0, -1, -2, -3];
const TEXT = {
  3: '+3 auf jeden Fall Unterricht (Kernzeit)', 2: '+2 eigentlich schon', 1: '+1 eher ja', 0: '0 kein Wunsch',
  '-1': '-1 lieber nicht', '-2': '-2 eigentlich nicht', '-3': '-3 keinesfalls Unterricht (absolute Sperrung)',
};
const UNBESTIMMT = [[-3, 'Freihalten, absolute Sperrung (-3)'], [-2, 'Freihalten, wichtig (-2)'], [-1, 'Freihalten, schwacher Wunsch (-1)']];
const wertText = (w) => (w > 0 ? `+${w}` : w === 0 ? '' : String(w));

function zeitwunschFenster(f, p) {
  let typ = TYPEN[p.typ] ? p.typ : 'lehrer';
  let element = p.element ?? null;
  let wert = Number.isInteger(p.wert) ? p.wert : -3;
  let verknuepft = p.verknuepft !== false;
  let daten = null;
  let liste = [];
  const schreiben = darfSchreiben();

  // ---- Werkzeugleiste: Typ, Element (◂ ▸), Verknüpfung, Werte +3 … −3, Löschen, Stundenplan ----
  const typWahl = h('select', { class: 'eingabe zw-typ', title: 'Art' }, Object.entries(TYPEN).map(([k, t]) => h('option', { value: k, selected: k === typ }, t)));
  const elWahl = h('select', { class: 'eingabe zw-element', title: 'Element wählen' });
  const blaettern = (d) => {
    const i = liste.findIndex((e) => e.id === element);
    const n = liste[Math.max(0, Math.min(liste.length - 1, i + d))];
    if (n) setzen(typ, n.id);
  };
  f.werkzeuge.append(typWahl, h('button', { class: 'wz-knopf', type: 'button', title: 'Vorheriges Element', onclick: () => blaettern(-1) }, '◂'), elWahl,
    h('button', { class: 'wz-knopf', type: 'button', title: 'Nächstes Element', onclick: () => blaettern(1) }, '▸'));
  const vk = f.werkzeug({ text: '', symbol: 'verknuepfen', nurSymbol: true, titel: 'Fensterverknüpfung: folgt der Auswahl in anderen Fenstern', aktion: () => { verknuepft = !verknuepft; vk.classList.toggle('gedrueckt', verknuepft); f.parameterSetzen({ verknuepft }); } });
  vk.classList.toggle('gedrueckt', verknuepft);
  f.trenner();
  const wertKnoepfe = WERTE.map((w) => {
    const k = h('button', { class: `wz-knopf zw-wert zw-w${w}`, type: 'button', title: TEXT[w], disabled: !schreiben, dataset: { wert: w } }, w > 0 ? `+${w}` : String(w));
    k.addEventListener('click', () => wertWaehlen(w));
    f.werkzeuge.append(k);
    return k;
  });
  f.trenner();
  const kLoeschen = f.werkzeug({ text: '', symbol: 'loeschen', nurSymbol: true, titel: 'Alle Zeitwünsche löschen', aktion: async () => {
    if (!daten?.anzahl || !(await frage(`Alle Zeitwünsche von ${daten.element.kurz} löschen?`))) return;
    await speichern({ alleLoeschen: true });
  } });
  kLoeschen.disabled = !schreiben;
  f.werkzeug({ text: 'Stundenplan', symbol: 'stundenplan', titel: 'Stundenplan des Elements', aktion: () => {
    if (typ !== 'unterricht' && typ !== 'fach' && element) befehl('stundenplan.oeffnen', { typ, element });
    else if (typ === 'fach' && element) befehl('stundenplan.oeffnen', { typ: 'fach', element });
  } });

  // ---- Inhalt ----
  const name = h('div', { class: 'zw-name' });
  const raster = h('div', { class: 'zw-raster', tabindex: '0' });
  const hilfe = h('div', { class: 'zw-hilfe' }, 'Wert oben wählen, dann die Stunden mit gedrückter Maustaste überstreichen. Klick auf „Tage“/„Vorm.“/„Nachm.“ setzt den ganzen (Halb-)Tag. Tasten 3 2 1 0 und Umschalt+1 2 3 (= −1 −2 −3) wählen den Wert.');
  const unbestimmtBox = h('div', { class: 'zw-unbestimmt' });
  f.inhalt.append(h('div', { class: 'zw-flaeche' }, name, raster, hilfe, unbestimmtBox));

  function wertWaehlen(w) {
    wert = w;
    for (const k of wertKnoepfe) k.classList.toggle('gedrueckt', Number(k.dataset.wert) === w);
    f.parameterSetzen({ wert });
  }

  // ---- Malen ----
  let malen = null; // { werte: Map("t:s" → w) }
  function zelleSetzen(td, w, verfuegbar) {
    td.textContent = wertText(w);
    td.className = `zw-zelle zw-w${w}${verfuegbar ? '' : ' zw-aus'}`;
  }
  function stundenVon(spalte) {
    const n = daten.stunden.length;
    if (spalte < n) return [daten.stunden[spalte].nr];
    if (spalte === n) return daten.stunden.map((s) => s.nr);
    if (spalte === n + 1) return daten.stunden.filter((s) => s.vormittag).map((s) => s.nr);
    return daten.stunden.filter((s) => !s.vormittag).map((s) => s.nr);
  }
  function malenAuf(td) {
    if (!malen || !td?.dataset?.tag) return;
    const t = Number(td.dataset.tag);
    for (const s of stundenVon(Number(td.dataset.spalte))) {
      malen.werte.set(`${t}:${s}`, wert);
      const z = raster.querySelector(`td[data-tag="${t}"][data-spalte="${daten.stunden.findIndex((x) => x.nr === s)}"]`);
      if (z) zelleSetzen(z, wert, daten.verfuegbar[t][s - 1]);
    }
  }
  raster.addEventListener('pointerdown', (e) => {
    const td = e.target.closest('td[data-tag]');
    if (!td || !schreiben || e.button !== 0) return;
    e.preventDefault();
    raster.focus({ preventScroll: true });
    malen = { werte: new Map() };
    raster.setPointerCapture(e.pointerId);
    malenAuf(td);
  });
  raster.addEventListener('pointermove', (e) => {
    if (!malen) return;
    malenAuf(document.elementFromPoint(e.clientX, e.clientY)?.closest?.('td[data-tag]'));
  });
  const malenEnde = () => {
    if (!malen) return;
    const aenderungen = [...malen.werte].map(([k, w]) => { const [tag, stunde] = k.split(':').map(Number); return { tag, stunde, wert: w }; })
      .filter((a) => daten.werte[a.tag][a.stunde - 1] !== a.wert);
    malen = null;
    if (aenderungen.length) speichern({ aenderungen }).catch((e) => { fehlerMelden(e); neu(); });
  };
  raster.addEventListener('pointerup', malenEnde);
  raster.addEventListener('pointercancel', malenEnde);
  raster.addEventListener('keydown', (e) => {
    const m = /^(?:Digit|Numpad)([0-3])$/.exec(e.code || '');
    if (!m || !schreiben || e.ctrlKey || e.metaKey || e.altKey) return;
    const n = Number(m[1]);
    wertWaehlen(e.shiftKey && n ? -n : n);
    e.preventDefault();
  });

  function zeichnen() {
    const d = daten;
    name.textContent = d ? `${TYPEN[typ]} ${d.element.kurz}${d.element.lang ? ` (${d.element.lang})` : ''}` : '';
    f.titel(`Zeitwünsche / ${TYPEN[typ]}${d ? ` ${d.element.kurz}` : ''}`);
    leeren(raster);
    if (!d) return;
    const kopf = h('tr', null, h('th', { class: 'zw-ecke' }),
      d.stunden.map((s) => h('th', { class: s.vormittag ? 'zw-vm' : 'zw-nm', title: s.vormittag ? 'Vormittag' : 'Nachmittag' }, s.label)),
      ['Tage', 'Vorm.', 'Nachm.'].map((t) => h('th', { class: 'zw-ganz' }, t)));
    const rumpf = h('tbody');
    d.tage.forEach((t) => {
      const tr = h('tr', null, h('th', { class: 'zw-tag' }, t.lang));
      d.stunden.forEach((s, j) => {
        const td = h('td', { dataset: { tag: t.nr, spalte: j }, title: `${t.lang}, ${s.label}. Stunde` });
        zelleSetzen(td, d.werte[t.nr][s.nr - 1], d.verfuegbar[t.nr][s.nr - 1]);
        tr.append(td);
      });
      ['ganzen Tag', 'Vormittag', 'Nachmittag'].forEach((x, i) => tr.append(h('td', { class: 'zw-zelle zw-ganzzelle', dataset: { tag: t.nr, spalte: d.stunden.length + i }, title: `${x} setzen` })));
      rumpf.append(tr);
    });
    raster.append(h('table', { class: 'zw-tabelle' }, h('thead', null, kopf), rumpf));
    unbestimmtZeichnen();
    f.fuss.replaceChildren(h('span', null, `${d.anzahl} Stunden mit Zeitwunsch`), h('span', { class: 'rechts' }, TEXT[wert]));
  }

  // ---- unbestimmte Zeitwünsche ----
  function unbestimmtZeichnen() {
    leeren(unbestimmtBox);
    if (!daten?.unbestimmte) { unbestimmtBox.hidden = true; return; }
    unbestimmtBox.hidden = false;
    const zeilen = daten.unbestimmte.map((u) => ({ ...u }));
    const tbody = h('tbody');
    const speichernU = () => api.put(api.dok(`/zeitwuensche/${typ}/${element}/unbestimmte`), { liste: zeilen.map(({ bereich, anzahl, wunsch }) => ({ bereich, anzahl, wunsch })) }, { quelle: f.id })
      .then((d) => { daten = d; zeichnen(); }).catch((e) => { fehlerMelden(e); neu(); });
    zeilen.forEach((u, i) => {
      const bereich = h('select', { class: 'eingabe', disabled: !schreiben }, ['Tage', 'Vormittage', 'Nachmittage'].map((b) => h('option', { selected: b === u.bereich }, b)));
      const anzahl = h('input', { class: 'eingabe zw-anzahl', type: 'number', min: 1, max: 7, value: u.anzahl, disabled: !schreiben });
      const wunsch = h('select', { class: 'eingabe', disabled: !schreiben }, UNBESTIMMT.map(([w, t]) => h('option', { value: w, selected: w === u.wunsch }, t)));
      bereich.addEventListener('change', () => { u.bereich = bereich.value; speichernU(); });
      anzahl.addEventListener('change', () => { u.anzahl = Math.max(1, Math.min(7, Number(anzahl.value) || 1)); speichernU(); });
      wunsch.addEventListener('change', () => { u.wunsch = Number(wunsch.value); speichernU(); });
      tbody.append(h('tr', null, h('td', null, bereich), h('td', null, anzahl), h('td', null, wunsch),
        h('td', null, schreiben ? h('button', { class: 'knopf klein', type: 'button', title: 'Zeile löschen', onclick: () => { zeilen.splice(i, 1); speichernU(); } }, '✕') : null)));
    });
    unbestimmtBox.append(h('div', { class: 'zw-utitel' }, 'Zusätzliche, unbestimmte Zeitwünsche',
      schreiben ? h('button', { class: 'knopf klein', type: 'button', onclick: () => { zeilen.push({ bereich: 'Tage', anzahl: 1, wunsch: -3 }); speichernU(); } }, '+ Hinzufügen') : null),
    h('table', { class: 'pl-tabelle zw-utabelle' }, h('thead', null, h('tr', null, h('th', null, 'Bereich'), h('th', null, 'Anzahl'), h('th', null, 'Zeitwunsch'), h('th', null, ''))), tbody));
    if (!zeilen.length) tbody.append(h('tr', null, h('td', { colspan: 4, class: 'pl-leise' }, 'Keine – z. B. „1 Tag, Freihalten (-3)“ für einen freien Tag in beliebiger Lage.')));
  }

  // ---- Laden/Speichern ----
  async function listeLaden() {
    liste = await api.get(api.dok(`/zeitwuensche/${typ}`));
    leeren(elWahl).append(...liste.map((e) => h('option', { value: e.id }, `${e.n || e.f ? '● ' : ''}${typ === 'unterricht' ? `${e.k} ${e.l}` : e.l ? `${e.k} – ${e.l}` : e.k}`)));
    if (!liste.some((e) => e.id === element)) element = liste[0]?.id ?? null;
    if (element !== null) elWahl.value = String(element);
  }

  async function neu() {
    if (element === null) { daten = null; zeichnen(); return; }
    const weg = laden(f.inhalt);
    try { daten = await api.get(api.dok(`/zeitwuensche/${typ}/${element}`)); zeichnen(); } finally { weg(); }
  }

  async function speichern(aenderung) {
    daten = await api.put(api.dok(`/zeitwuensche/${typ}/${element}`), aenderung, { quelle: f.id });
    zeichnen();
    const eintrag = liste.find((e) => e.id === element);
    if (eintrag && Boolean(eintrag.n) !== Boolean(daten.anzahl)) { eintrag.n = daten.anzahl; const opt = elWahl.querySelector(`option[value="${element}"]`); if (opt) opt.textContent = `${daten.anzahl || eintrag.f ? '● ' : ''}${opt.textContent.replace(/^● /, '')}`; }
  }

  async function setzen(t, id, { melde = true } = {}) {
    const typWechsel = t !== typ;
    typ = t;
    element = id ?? null;
    typWahl.value = typ;
    if (typWechsel || !liste.length) await listeLaden();
    else if (element !== null) elWahl.value = String(element);
    f.parameterSetzen({ typ, element });
    await neu();
    if (melde && element !== null && typ !== 'unterricht') melden('element', { typ, id: element, quelle: f.id });
  }

  typWahl.addEventListener('change', () => setzen(typWahl.value, null).catch(fehlerMelden));
  elWahl.addEventListener('change', () => setzen(typ, Number(elWahl.value)).catch(fehlerMelden));
  f.an('element', (e) => { if (verknuepft && e.quelle !== f.id && TYPEN[e.typ] && (e.typ !== typ || e.id !== element)) setzen(e.typ, e.id, { melde: false }).catch(() => {}); });
  f.an('daten', (e) => {
    if (e.quelle === f.id) return;
    if (betrifft(e, 'Stammdaten', 'Unterricht', 'Einstellungen')) listeLaden().then(neu).catch(() => {});
  });
  f.an('grunddaten', () => neu().catch(() => {}));
  f._setzen = (t, id) => setzen(t, id, { melde: false });

  wertWaehlen(wert);
  return (async () => { await listeLaden(); f.parameterSetzen({ typ, element }); await neu(); })();
}

const oeffnen = ({ typ = 'lehrer', element } = {}) => {
  if (!zustand.dokument) return null;
  const f = fensterverwaltung.oeffnen('zeitwuensche', { typ, element });
  return f;
};

export default {
  id: 'zeitwuensche',
  css: ['css/module/zeitwuensche.css', 'css/module/planung.css'],
  fenster: {
    zeitwuensche: {
      titel: 'Zeitwünsche', symbol: 'zeitwunsch', breite: 720, hoehe: 440,
      einzig: () => 'zeitwuensche',
      erneutGeoeffnet: (f, p) => { if (p?.typ) f._setzen?.(p.typ, p.element ?? null)?.catch?.(fehlerMelden); },
      erstellen: zeitwunschFenster,
    },
  },
  befehle: { 'zeitwuensche.oeffnen': oeffnen },
  ribbon: [
    { id: 'eingabe.zeitwuensche', reiter: 'Dateneingabe', gruppe: 'Zeit', text: 'Zeitwünsche', symbol: 'zeitwunsch', gross: true, befehl: 'zeitwuensche.oeffnen', args: { typ: 'lehrer' }, braucht: 'dokument',
      hinweis: 'Zeitwünsche der Lehrkräfte, Klassen, Räume und Fächer (+3 … −3)' },
    { id: 'planung.uzeitwuensche', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Unterrichtszeitwünsche', symbol: 'zeitwunsch', befehl: 'zeitwuensche.oeffnen', args: { typ: 'unterricht' }, braucht: 'dokument' },
  ],
};

