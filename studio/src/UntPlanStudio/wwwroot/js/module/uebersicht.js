// Modul „Übersichtspläne“: Klassen-, Lehrer- und Raum-Übersicht quer wie im Desktop – alle Elemente als Zeilen, Tage × Stunden
// als Spalten. Schnell auch bei großen Schulen: kompakte Server-Antwort, es werden nur die sichtbaren Zeilen gezeichnet.
// Drag & Drop: Stunde innerhalb der Zeile verschieben/tauschen; im Raum-Übersichtsplan in eine andere Raumzeile = Raumverlegung.
// Server: Api/UebersichtApi.cs (/uebersicht/{typ}), Druck: /druck/uebersicht (PDF).
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft, melden } from '../kern/ereignisse.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { hinweis, fehlerMelden, laden } from '../kern/dialog.js';
import { druckUrl, vorschau, herunterladen } from './druck.js';

const TITEL = { klasse: 'Klassen', lehrer: 'Lehrer', raum: 'Räume', fach: 'Fächer' };
const SYMBOL = { klasse: 'klassen', lehrer: 'lehrer', raum: 'raeume', fach: 'faecher' };
const ZH = 22;        // Zeilenhöhe
const NAME = 64;      // Breite der Namensspalte
const KOPF = 36;      // Kopf: Tage + Stunden
const TYP_UE = 'application/x-untplan-uebersicht';

/** Textfarbe passend zur Hintergrundfarbe. */
function textfarbe(hex) {
  if (!hex) return '';
  const n = parseInt(hex.slice(1), 16);
  const l = (0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255)) / 255;
  return l < 0.55 ? '#fff' : '#1f2933';
}

function uebersichtFenster(f, p) {
  const typ = TITEL[p.typ] ? p.typ : 'klasse';
  let inhalt = p.inhalt || 'Standard';
  let daten = null;
  let filterText = '';
  let sichtbar = [];   // Indizes der Elemente nach Filter
  let spaltenBreite = 18;
  const schreiben = darfSchreiben();
  f.titel(`${TITEL[typ]} Übersicht quer`);

  const kopf = h('div', { class: 'ue-kopf' });
  const platz = h('div', { class: 'ue-platz' });
  const koerper = h('div', { class: 'ue-koerper' }, platz);
  const marke = h('div', { class: 'ue-ziel', hidden: true });
  platz.append(marke);
  f.inhalt.append(h('div', { class: 'ue-rahmen' }, kopf, koerper));

  // ---- Werkzeuge ----
  const wahl = h('select', { class: 'eingabe', title: 'Inhalt der Zellen' },
    [['Standard', 'Standard'], ['Fach', 'Fach'], ['Lehrer', 'Lehrkräfte'], ['Klasse', 'Klassen'], ['Raum', 'Räume']].map(([w, t]) => h('option', { value: w, selected: w === inhalt }, `Inhalt: ${t}`)));
  wahl.addEventListener('change', () => { inhalt = wahl.value; f.parameterSetzen({ inhalt }); laden_().catch(fehlerMelden); });
  const filter = h('input', { class: 'eingabe', type: 'search', placeholder: 'Filtern …', style: { width: '120px' } });
  filter.addEventListener('input', () => { filterText = filter.value.trim().toLowerCase(); filtern(); });
  f.werkzeuge.append(wahl, filter);
  f.trenner();
  f.werkzeug({ text: 'Drucken', symbol: 'drucken', aktion: () => vorschau(druckUrl('/uebersicht', { typ, inhalt }), `${TITEL[typ]} Übersicht`) });
  f.werkzeug({ text: 'PDF', symbol: 'herunterladen', nurSymbol: true, titel: 'Als PDF herunterladen', aktion: () => herunterladen(druckUrl('/uebersicht', { typ, inhalt })) });
  f.werkzeuge.append(h('span', { class: 'leise ue-hinweis' }, schreiben
    ? (typ === 'raum' ? 'Raumverlegung: Stunde in eine andere Raumzeile ziehen; in der Zeile verschieben/tauschen.' : 'Stunde per Drag & Drop innerhalb der Zeile verschieben/tauschen.')
    : ''));

  const spalten = () => daten.zeitraster.tage.length * daten.zeitraster.stunden.length;

  function masse() {
    const breite = koerper.clientWidth - NAME - 2;
    spaltenBreite = Math.max(18, Math.floor(breite / Math.max(1, spalten())));
    const gesamt = NAME + spaltenBreite * spalten();
    platz.style.width = `${gesamt}px`;
    platz.style.height = `${sichtbar.length * ZH}px`;
    zeichneKopf(gesamt);
  }

  function zeichneKopf(gesamt) {
    const z = daten.zeitraster;
    const n = z.stunden.length;
    const innen = h('div', { class: 'ue-kopf-innen', style: { width: `${gesamt}px` } }, h('div', { class: 'ue-ecke', style: { width: `${NAME}px` } }, `${sichtbar.length}`));
    z.tage.forEach((t, i) => {
      innen.append(h('div', { class: 'ue-tag', style: { left: `${NAME + i * n * spaltenBreite}px`, width: `${n * spaltenBreite}px` } }, spaltenBreite * n > 120 ? t.lang : t.kurz,
        h('div', { class: 'ue-stunden' }, z.stunden.map((s) => h('span', { style: { width: `${spaltenBreite}px` } }, s.label)))));
    });
    kopf.replaceChildren(innen);
    innen.style.transform = `translateX(${-koerper.scrollLeft}px)`;
  }

  function filtern() {
    sichtbar = daten.elemente.map((e, i) => i).filter((i) => !filterText || daten.elemente[i].k.toLowerCase().includes(filterText) || (daten.elemente[i].l || '').toLowerCase().includes(filterText));
    masse();
    zeichnen(true);
  }

  let bereich = [-1, -1];
  function zeichnen(neu) {
    const von = Math.max(0, Math.floor(koerper.scrollTop / ZH) - 5);
    const bis = Math.min(sichtbar.length, Math.ceil((koerper.scrollTop + koerper.clientHeight) / ZH) + 5);
    if (!neu && von === bereich[0] && bis === bereich[1]) return;
    bereich = [von, bis];
    for (const el of [...platz.querySelectorAll('.ue-zeile')]) el.remove();
    const n = daten.zeitraster.stunden.length;
    const tage = daten.zeitraster.tage.length;
    const frag = document.createDocumentFragment();
    for (let r = von; r < bis; r++) {
      const i = sichtbar[r];
      const el = daten.elemente[i];
      const zeile = h('div', { class: `ue-zeile${r % 2 ? ' wechsel' : ''}`, style: { top: `${r * ZH}px`, height: `${ZH}px` }, dataset: { i } },
        h('div', { class: 'ue-name', style: { width: `${NAME}px` }, title: el.l || el.k, dataset: { i } }, el.k));
      for (let t = 1; t < tage; t++) zeile.append(h('div', { class: 'ue-taglinie', style: { left: `${NAME + t * n * spaltenBreite}px` } }));
      for (const [tag, stunde, u, text, titel, farbe, fix] of daten.zellen[i]) {
        const z = h('div', {
          class: `ue-zelle${fix ? ' fix' : ''}`, draggable: schreiben && !fix ? 'true' : null, title: titel,
          style: { left: `${NAME + (tag * n + stunde - 1) * spaltenBreite + 1}px`, width: `${spaltenBreite - 2}px`, background: farbe || '', color: textfarbe(farbe) },
          dataset: { u, tag, stunde, i },
        }, (fix ? '*' : '') + text);
        zeile.append(z);
      }
      frag.append(zeile);
    }
    platz.append(frag);
  }

  koerper.addEventListener('scroll', () => {
    const innen = kopf.firstChild;
    if (innen) innen.style.transform = `translateX(${-koerper.scrollLeft}px)`;
    zeichnen(false);
  });
  const ro = new ResizeObserver(() => { if (daten) { masse(); zeichnen(true); } });
  ro.observe(koerper);
  f.beimSchliessen(() => ro.disconnect());

  // ---- Auswahl und Fensterverknüpfung ----
  platz.addEventListener('click', (ev) => {
    const name = ev.target.closest('.ue-name');
    const zelle = ev.target.closest('.ue-zelle');
    const i = Number((name || zelle)?.dataset.i);
    if (Number.isNaN(i)) return;
    for (const x of platz.querySelectorAll('.ue-name.aktiv')) x.classList.remove('aktiv');
    platz.querySelector(`.ue-name[data-i="${i}"]`)?.classList.add('aktiv');
    melden('element', { typ, id: daten.elemente[i].id, quelle: f.id });
  });
  platz.addEventListener('dblclick', (ev) => {
    const el = ev.target.closest('.ue-name, .ue-zelle');
    if (!el) return;
    fensterverwaltung.oeffnen('stundenplan', { typ, element: daten.elemente[Number(el.dataset.i)].id });
  });

  // ---- Drag & Drop ----
  let gezogen = null;
  function ziel(ev) {
    const r = platz.getBoundingClientRect();
    const x = ev.clientX - r.left - NAME;
    const y = ev.clientY - r.top;
    const zeile = Math.floor(y / ZH);
    const spalte = Math.floor(x / spaltenBreite);
    if (x < 0 || zeile < 0 || zeile >= sichtbar.length || spalte < 0 || spalte >= spalten()) return null;
    const n = daten.zeitraster.stunden.length;
    return { zeile, i: sichtbar[zeile], tag: Math.floor(spalte / n), stunde: (spalte % n) + 1 };
  }
  platz.addEventListener('dragstart', (ev) => {
    const z = ev.target.closest('.ue-zelle');
    if (!z) return;
    gezogen = { u: Number(z.dataset.u), tag: Number(z.dataset.tag), stunde: Number(z.dataset.stunde), i: Number(z.dataset.i) };
    ev.dataTransfer.setData(TYP_UE, JSON.stringify(gezogen));
    ev.dataTransfer.effectAllowed = 'move';
  });
  platz.addEventListener('dragover', (ev) => {
    if (!gezogen) return;
    const zl = ziel(ev);
    const ok = zl && (zl.i === gezogen.i || (typ === 'raum' && zl.tag === gezogen.tag && zl.stunde === gezogen.stunde));
    marke.hidden = !zl;
    if (zl) {
      const n = daten.zeitraster.stunden.length;
      Object.assign(marke.style, { top: `${zl.zeile * ZH}px`, left: `${NAME + (zl.tag * n + zl.stunde - 1) * spaltenBreite}px`, width: `${spaltenBreite}px`, height: `${ZH}px` });
      marke.classList.toggle('ungueltig', !ok);
    }
    if (ok) { ev.preventDefault(); ev.dataTransfer.dropEffect = 'move'; }
  });
  platz.addEventListener('dragend', () => { gezogen = null; marke.hidden = true; });
  platz.addEventListener('drop', (ev) => {
    ev.preventDefault();
    marke.hidden = true;
    const g = gezogen;
    gezogen = null;
    const zl = ziel(ev);
    if (!g || !zl) return;
    let anfrage;
    if (zl.i !== g.i) {
      if (typ !== 'raum') return;
      anfrage = api.post(api.dok('/uebersicht/raum'), { unterricht: g.u, tag: g.tag, stunde: g.stunde, alterRaum: daten.elemente[g.i].id, neuerRaum: daten.elemente[zl.i].id }, { quelle: f.id })
        .then(() => hinweis(`Raum ${daten.elemente[g.i].k} → ${daten.elemente[zl.i].k} – Strg+Z macht es rückgängig.`, 'ok', 2500));
    } else {
      if (zl.tag === g.tag && zl.stunde === g.stunde) return;
      anfrage = api.post(api.dok('/stundenplan/verschieben'), { unterricht: g.u, tag: g.tag, stunde: g.stunde, zielTag: zl.tag, zielStunde: zl.stunde, tauschen: true }, { quelle: f.id })
        .then(() => hinweis('Stunde verschoben – Strg+Z macht es rückgängig.', 'ok', 2000));
    }
    anfrage.then(() => laden_()).catch(fehlerMelden);
  });

  async function laden_() {
    const weg = laden(f.inhalt);
    try {
      daten = await api.get(api.dok(`/uebersicht/${typ}?inhalt=${inhalt}`));
      const anzahl = daten.zellen.reduce((a, z) => a + z.length, 0);
      f.fuss.replaceChildren(h('span', null, `${daten.elemente.length} ${TITEL[typ]}`), h('span', null, `${anzahl} belegte Stunden`), h('span', { class: 'rechts' }, daten.periode));
      filtern();
    } finally { weg(); }
  }

  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Stundenplan', 'Unterricht', 'Stammdaten')) laden_().catch(() => {}); });
  f.an('grunddaten', () => laden_().catch(() => {}));
  f.an('element', (e) => {
    if (e.quelle === f.id || e.typ !== typ || !daten) return;
    const r = sichtbar.findIndex((i) => daten.elemente[i].id === e.id);
    if (r < 0) return;
    if (r * ZH < koerper.scrollTop || r * ZH > koerper.scrollTop + koerper.clientHeight - ZH) koerper.scrollTop = r * ZH - koerper.clientHeight / 2;
    zeichnen(true);
    for (const x of platz.querySelectorAll('.ue-name.aktiv')) x.classList.remove('aktiv');
    platz.querySelector(`.ue-name[data-i="${sichtbar[r]}"]`)?.classList.add('aktiv');
  });
  laden_().catch(fehlerMelden);
}

const oeffnen = ({ typ = 'klasse', inhalt } = {}) => (zustand.dokument ? fensterverwaltung.oeffnen('uebersicht', { typ, inhalt }) : null);
const e = (id, reiter, gruppe, text, typ, gross = true) => ({ id, reiter, gruppe, text, symbol: 'uebersicht', gross, befehl: 'uebersicht.oeffnen', args: { typ }, braucht: 'dokument', hinweis: `${TITEL[typ]} Übersicht quer: alle ${TITEL[typ]} mit Tagen und Stunden` });

export default {
  id: 'uebersicht',
  css: ['css/module/uebersicht.css'],
  fenster: {
    uebersicht: {
      titel: 'Übersicht', symbol: 'uebersicht', symbolFuer: (p) => SYMBOL[p.typ] || 'uebersicht', breite: 1180, hoehe: 560,
      einzig: (p) => p.typ || 'klasse', erstellen: uebersichtFenster,
    },
  },
  befehle: { 'uebersicht.oeffnen': oeffnen },
  ribbon: [
    e('stundenplaene.uebersicht.klasse', 'Stundenpläne', 'Übersichtspläne', 'Klassen Übersicht', 'klasse'),
    e('stundenplaene.uebersicht.lehrer', 'Stundenpläne', 'Übersichtspläne', 'Lehrer Übersicht', 'lehrer'),
    e('stundenplaene.uebersicht.raum', 'Stundenpläne', 'Übersichtspläne', 'Räume Übersicht', 'raum'),
  ],
};
