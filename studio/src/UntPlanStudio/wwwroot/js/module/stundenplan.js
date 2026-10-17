// Modul „Stundenplan“: Stundenplanfenster je Klasse, Lehrkraft, Raum oder Fach (Wochenraster, Blöcke, Kopplungen nebeneinander).
// Manuelles Planen per Drag & Drop: Stunden verschieben/tauschen, nicht verplante Stunden setzen, Kontextmenü (fixieren, entplanen).
import { h, leeren } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Stundenplanraster, TYP_UNTERRICHT } from '../kern/raster.js';
import { menue, hinweis, fehlerMelden, laden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { elementAuswahl } from './unterricht.js';

const TYPEN = { klasse: ['Klasse', 'klassen'], lehrer: ['Lehrkraft', 'lehrer'], raum: ['Raum', 'raeume'], fach: ['Fach', 'faecher'] };

function stundenplanFenster(f, p) {
  const typ = TYPEN[p.typ] ? p.typ : 'klasse';
  let element = p.element ?? null;
  let verknuepft = p.verknuepft !== false;
  const schreiben = darfSchreiben();
  let planen = schreiben && p.planen !== false;

  const nv = h('div', { class: 'nicht-verplant', title: 'Nicht verplante Stunden – in den Plan ziehen' });
  const raster = new Stundenplanraster(f.inhalt, {
    typ,
    kompakt: !!p.kompakt,
    ziehen: planen,
    beiZiehenStart: (d) => api.get(api.dok(`/stundenplan/zielstatus?unterricht=${d.u}${d.tag !== undefined ? `&tag=${d.tag}&stunde=${d.stunde}` : ''}`)),
    beiAblegen: (d) => ablegen(d).catch(fehlerMelden),
    beiDoppelklick: (e) => fensterverwaltung.oeffnen('unterricht', { sicht: typ === 'raum' ? 'alle' : typ, element: typ === 'raum' ? undefined : element }),
    kontextmenue: (e, tag, stunde, ev) => menue(ev.clientX, ev.clientY, [
      { titel: `U-Nr. ${e.u} · ${e.fach} ${e.klassen}` },
      { text: e.fix ? 'Fixierung aufheben' : 'Stunde fixieren', symbol: 'fixieren', deaktiviert: !schreiben, aktion: () => aendern('/stundenplan/fixieren', { unterricht: e.u, tag, stunde, fixieren: !e.fix }) },
      { text: 'Stunde entplanen', symbol: 'loeschen', deaktiviert: !schreiben || e.fix, aktion: () => aendern('/stundenplan/entplanen', { unterricht: e.u, tag, stunde }) },
      null,
      { text: 'Unterricht anzeigen', symbol: 'unterricht', aktion: () => fensterverwaltung.oeffnen('unterricht', { sicht: 'alle' }) },
    ]),
  });
  f.inhalt.append(nv);

  async function aendern(pfad, daten) {
    await api.post(api.dok(pfad), daten, { quelle: f.id });
    await laden_();
  }

  async function ablegen(d) {
    if (d.tag !== undefined) {
      if (d.tag === d.zielTag && d.stunde === d.zielStunde) return;
      await aendern('/stundenplan/verschieben', { unterricht: d.u, tag: d.tag, stunde: d.stunde, zielTag: d.zielTag, zielStunde: d.zielStunde, tauschen: true });
      hinweis('Stunde verschoben – Strg+Z macht es rückgängig.', 'ok', 2000);
    } else {
      await aendern('/stundenplan/verplanen', { unterricht: d.u, tag: d.zielTag, stunde: d.zielStunde });
      hinweis('Stunde verplant – Strg+Z macht es rückgängig.', 'ok', 2000);
    }
  }

  async function laden_() {
    if (!element) { raster.setzen(null); return; }
    const weg = laden(f.inhalt);
    try {
      const plan = await api.get(api.dok(`/stundenplan/${typ}/${element}`));
      f.titel(`${TYPEN[typ][0]} ${plan.element.kurz}${plan.element.lang ? ` (${plan.element.lang})` : ''} / Stundenplan`);
      raster.setzen(plan);
      leeren(nv);
      for (const n of plan.nichtVerplant) {
        const el = h('span', { class: 'nv', draggable: planen ? 'true' : null, title: `U-Nr. ${n.u}: ${n.anzahl} Stunde(n) nicht verplant` }, h('b', null, `${n.anzahl}×`), ` ${n.text}`);
        el.addEventListener('dragstart', (ev) => { ev.dataTransfer.setData(TYP_UNTERRICHT, JSON.stringify({ u: n.u })); raster.ziehenBeginnt({ u: n.u }); });
        el.addEventListener('dragend', () => raster.ziehenEndet());
        nv.append(el);
      }
      const summe = plan.zellen.reduce((a, z) => a + new Set(z.eintraege.map((e) => e.u)).size, 0);
      f.fuss.replaceChildren(...[h('span', null, `${summe} Stunden verplant`), plan.nichtVerplant.length ? h('span', null, `${plan.nichtVerplant.reduce((a, n) => a + n.anzahl, 0)} nicht verplant`) : null,
        h('span', { class: 'rechts' }, plan.periode)].filter(Boolean));
    } finally { weg(); }
  }

  // ---- Werkzeugleiste ----
  const vk = f.werkzeug({ text: '', symbol: 'verknuepfen', nurSymbol: true, titel: 'Fensterverknüpfung: folgt der Auswahl in anderen Fenstern', aktion: () => { verknuepft = !verknuepft; vk.classList.toggle('gedrueckt', verknuepft); f.parameterSetzen({ verknuepft }); } });
  vk.classList.toggle('gedrueckt', verknuepft);
  if (schreiben) {
    const pl = f.werkzeug({ text: 'Planen', symbol: 'tausch', titel: 'Manuelles Planen per Drag & Drop', aktion: () => {
      planen = !planen;
      pl.classList.toggle('gedrueckt', planen);
      raster.o.ziehen = planen;
      f.parameterSetzen({ planen });
      laden_();
    } });
    pl.classList.toggle('gedrueckt', planen);
  }
  const kp = f.werkzeug({ text: '', symbol: 'untereinander', nurSymbol: true, titel: 'Kompakte Darstellung', aktion: () => {
    raster.o.kompakt = !raster.o.kompakt;
    raster.el.classList.toggle('kompakt', raster.o.kompakt);
    kp.classList.toggle('gedrueckt', raster.o.kompakt);
    f.parameterSetzen({ kompakt: raster.o.kompakt });
    raster.setzen(raster.plan);
  } });
  kp.classList.toggle('gedrueckt', !!p.kompakt);
  if (typ !== 'raum') f.werkzeug({ text: 'Unterricht', symbol: 'unterricht', aktion: () => fensterverwaltung.oeffnen('unterricht', { sicht: typ, element }) });

  let auswahl = null;
  const elementSetzen = (id) => { element = id; f.parameterSetzen({ element }); auswahl?.setzen(id); laden_().catch(fehlerMelden); };
  (async () => {
    auswahl = await elementAuswahl(f, typ, element, elementSetzen);
    if (!element && auswahl.liste.length) { element = auswahl.liste[0].id; auswahl.setzen(element); f.parameterSetzen({ element }); }
    await laden_();
  })().catch(fehlerMelden);

  f.an('daten', (e) => {
    if (e.quelle === f.id) return;
    if (betrifft(e, 'Stammdaten')) auswahl?.neuLaden();
    if (betrifft(e, 'Stundenplan', 'Unterricht', 'Stammdaten')) laden_().catch(() => {});
  });
  f.an('element', (e) => { if (verknuepft && e.quelle !== f.id && e.typ === typ && e.id !== element) elementSetzen(e.id); });
  f.an('grunddaten', () => laden_().catch(() => {}));
}

const oeffnen = ({ typ = 'klasse', element, kompakt } = {}) => (zustand.dokument ? fensterverwaltung.oeffnen('stundenplan', { typ, element, kompakt }) : null);

const e = (id, reiter, gruppe, text, typ, gross = true, extra = {}) => ({ id, reiter, gruppe, text, symbol: TYPEN[typ][1], gross, befehl: 'stundenplan.oeffnen', args: { typ, ...extra }, braucht: 'dokument' });

export default {
  id: 'stundenplan',
  fenster: {
    stundenplan: {
      titel: 'Stundenplan', symbol: 'stundenplan', breite: 720, hoehe: 520,
      erstellen: stundenplanFenster,
    },
  },
  befehle: { 'stundenplan.oeffnen': oeffnen },
  ribbon: [
    e('stundenplaene.klasse', 'Stundenpläne', 'Einzelpläne', 'Klassenplan', 'klasse'),
    e('stundenplaene.lehrer', 'Stundenpläne', 'Einzelpläne', 'Lehrerplan', 'lehrer'),
    e('stundenplaene.raum', 'Stundenpläne', 'Einzelpläne', 'Raumplan', 'raum'),
    e('stundenplaene.fach', 'Stundenpläne', 'Einzelpläne', 'Fachplan', 'fach'),
    e('stundenplaene.klein.klasse', 'Stundenpläne', 'Formate', 'Klassenplan kompakt', 'klasse', false, { kompakt: true }),
    e('stundenplaene.klein.lehrer', 'Stundenpläne', 'Formate', 'Lehrerplan kompakt', 'lehrer', false, { kompakt: true }),
    e('stundenplaene.klein.raum', 'Stundenpläne', 'Formate', 'Raumplan kompakt', 'raum', false, { kompakt: true }),
    { ...e('planung.planen', 'Planung', 'Manuelles Planen', 'Planen', 'klasse'), symbol: 'stundenplan', hinweis: 'Stundenplan öffnen und Stunden per Drag & Drop verplanen' },
    e('planung.klassen', 'Planung', 'Stundenpläne', 'Klassen', 'klasse', false),
    e('planung.lehrer', 'Planung', 'Stundenpläne', 'Lehrer', 'lehrer', false),
    e('planung.raeume', 'Planung', 'Stundenpläne', 'Räume', 'raum', false),
    e('vertretung.plan.klasse', 'Vertretungsplanung', 'Stundenpläne', 'Klassenplan', 'klasse', false),
    e('vertretung.plan.lehrer', 'Vertretungsplanung', 'Stundenpläne', 'Lehrerplan', 'lehrer', false),
    e('vertretung.plan.raum', 'Vertretungsplanung', 'Stundenpläne', 'Raumplan', 'raum', false),
  ],
};
