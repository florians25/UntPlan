// Modul „Stundenplan“: Stundenplanfenster je Klasse, Lehrkraft, Raum oder Fach (Wochenraster, Blöcke, Kopplungen nebeneinander).
// Manuelles Planen per Drag & Drop: Stunden/Blöcke verschieben/tauschen, nicht verplante Blöcke setzen, Mehrfachauswahl,
// Kontextmenü wie im Desktop (fixieren, entplanen, Raum zuordnen, Tauschvorschläge …) – Planungslogik in planung.js.
import { h, leeren } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Stundenplanraster, TYP_UNTERRICHT } from '../kern/raster.js';
import { menue, hinweis, fehlerMelden, laden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { befehl } from '../kern/modulsystem.js';
import { elementAuswahl } from './unterricht.js';
import * as planung from './planung.js';

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
    mehrfach: true,
    beiZiehenStart: (d) => planung.zielstatus(d),
    beiAblegen: (d) => ablegen(d).catch(fehlerMelden),
    beiDoppelklick: (e) => fensterverwaltung.oeffnen('unterricht', { sicht: typ === 'raum' || typ === 'fach' ? 'alle' : typ, element: typ === 'raum' || typ === 'fach' ? undefined : element }),
    kontextmenue: (e, tag, stunde, ev) => menue(ev.clientX, ev.clientY, planung.kontextEintraege(ctx, e, tag, stunde)),
    feldKontextmenue: (tag, stunde, ev) => menue(ev.clientX, ev.clientY, planung.feldEintraege(ctx)),
    beiMarkierung: () => fussZeigen(),
  });
  f.inhalt.append(nv);
  // Kontext für planung.js und Menüband-Befehle (Tauschvorschläge, Raum zuordnen beziehen sich auf die gewählte Stunde)
  const ctx = { f, raster, typ, schreiben, element: () => element, planen: () => planen, neuLaden: () => laden_() };
  f.planung = ctx;
  f.beimSchliessen(() => raster.entfernen());
  raster.el.addEventListener('keydown', (ev) => { if (planung.taste(ctx, ev)) { ev.preventDefault(); ev.stopPropagation(); } });

  async function ablegen(d) {
    if (await planung.ablegen(d, f.id)) await laden_();
  }

  let fussBasis = [];
  function fussZeigen() {
    const n = raster.markiert.size ? raster.markierte().length : 0;
    f.fuss.replaceChildren(...fussBasis, ...(n ? [h('span', { class: 'pl-markiert' }, `${n} Stunden markiert`)] : []));
  }

  async function laden_() {
    if (!element) { raster.setzen(null); return; }
    const weg = laden(f.inhalt);
    try {
      const plan = await api.get(api.dok(`/stundenplan/${typ}/${element}`));
      f.titel(`${TYPEN[typ][0]} ${plan.element.kurz}${plan.element.lang ? ` (${plan.element.lang})` : ''} / Stundenplan`);
      raster.setzen(plan);
      leeren(nv);
      // nicht verplante Stunden je offenem Block (Doppelstunde = 2 Std.), gleiche Blocklängen zusammengefasst
      for (const n of plan.nichtVerplant) {
        const bloecke = n.bloecke?.length ? n.bloecke : Array(n.anzahl).fill(1);
        const laengen = [...new Set(bloecke)].sort((a, b) => b - a);
        for (const laenge of laengen) {
          const anzahl = bloecke.filter((b) => b === laenge).length;
          const el = h('span', { class: `nv${laenge > 1 ? ' block' : ''}`, draggable: planen ? 'true' : null, title: `U-Nr. ${n.u}: ${n.anzahl} Stunde(n) nicht verplant${laenge > 1 ? ` – Block mit ${laenge} Stunden` : ''}` },
            h('b', null, `${anzahl}×`), laenge > 1 ? h('i', null, ` ${laenge} Std.`) : null, ` ${n.text}`);
          el.addEventListener('dragstart', (ev) => { ev.dataTransfer.setData(TYP_UNTERRICHT, JSON.stringify({ u: n.u, laenge })); raster.ziehenBeginnt({ u: n.u, laenge }); });
          el.addEventListener('dragend', () => raster.ziehenEndet());
          el.addEventListener('contextmenu', (ev) => {
            ev.preventDefault();
            menue(ev.clientX, ev.clientY, [
              { titel: `U-Nr. ${n.u}` },
              { text: 'Unterricht bearbeiten …', symbol: 'unterricht', aktion: () => fensterverwaltung.oeffnen('unterricht', { sicht: typ === 'raum' || typ === 'fach' ? 'alle' : typ, element: typ === 'raum' || typ === 'fach' ? undefined : element }) },
              { text: 'Zeitwünsche des Unterrichts', symbol: 'zeitwunsch', aktion: () => befehl('zeitwuensche.oeffnen', { typ: 'unterricht', element: n.u }) },
            ]);
          });
          nv.append(el);
        }
      }
      const summe = plan.zellen.reduce((a, z) => a + new Set(z.eintraege.map((e) => e.u)).size, 0);
      fussBasis = [h('span', null, `${summe} Stunden verplant`), plan.nichtVerplant.length ? h('span', null, `${plan.nichtVerplant.reduce((a, n) => a + n.anzahl, 0)} nicht verplant`) : null,
        h('span', { class: 'rechts' }, plan.periode)].filter(Boolean);
      fussZeigen();
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
  if (schreiben) {
    f.trenner();
    const planAktion = async (fn) => { if (await fn()) await laden_(); };
    f.werkzeug({ text: '', symbol: 'fixieren', nurSymbol: true, titel: 'Unterr.-Std. fixieren / lösen (F)', aktion: () => planAktion(() => planung.fixieren(raster.markierte(), f.id)) });
    f.werkzeug({ text: '', symbol: 'raeume', nurSymbol: true, titel: 'Raum zuordnen / löschen', aktion: () => { const a = raster.auswahl(); return a ? planAktion(() => planung.raumDialog(a, f.id)) : hinweis('Bitte zuerst eine Stunde anklicken.', 'warnung'); } });
    f.werkzeug({ text: '', symbol: 'tausch', nurSymbol: true, titel: 'Tauschvorschläge', aktion: () => { const a = raster.auswahl(); return a ? planAktion(() => planung.tauschDialog(a, f.id)) : hinweis('Bitte zuerst eine Stunde anklicken.', 'warnung'); } });
    f.werkzeug({ text: '', symbol: 'loeschen', nurSymbol: true, titel: 'Stunde entplanen (Entf)', aktion: () => planAktion(() => planung.entplanen(raster.markierte(), f.id)) });
  }
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
