// Stundenpläne im Vertretungsmodus (tagesaktuell): Woche des Planungsdatums für Klasse, Lehrkraft, Raum oder Fach mit allen Vertretungen –
// Änderungen rot, Entfälle durchgestrichen, offene Stunden „???“. Drag & Drop wie im Desktop: Verlegen, Tauschen („Was wollen Sie …
// machen?“), Verdrängen; Vormerkungen in den Plan ziehen. Kontextmenü: Stunde entfallen lassen, Entfall rückgängig (auch Block),
// neben den Plan legen (Vormerkung), Text, Änderungen rückgängig, Vertretungsvorschlag, Sondereinsatz.
import { h, leeren } from '../../kern/dom.js';
import { api } from '../../kern/api.js';
import { zustand, darfSchreiben } from '../../kern/zustand.js';
import { betrifft, melden } from '../../kern/ereignisse.js';
import { menue, hinweis, fehlerMelden, laden as ladeAnzeige, formular } from '../../kern/dialog.js';
import { fensterverwaltung } from '../../kern/fenster.js';
import { elementAuswahl } from '../unterricht.js';
import { vpfad, planungsdatum, mitRueckfragen, auswahlMelden } from './kern.js';

export const TYP_VORMERKUNG = 'application/x-untplan-vormerkung';
const TYP_TAGESSTUNDE = 'application/x-untplan-tagesstunde';
const TYPEN = { klasse: ['Klasse', 'klassen'], lehrer: ['Lehrkraft', 'lehrer'], raum: ['Raum', 'raeume'], fach: ['Fach', 'faecher'] };

function planFenster(f, p) {
  const typ = TYPEN[p.typ] ? p.typ : 'klasse';
  let element = p.element ?? null;
  let verknuepft = p.verknuepft !== false;
  let kompakt = !!p.kompakt;
  let plan = null;
  const schreiben = darfSchreiben();

  const flaeche = h('div', { class: `raster vt-plan${kompakt ? ' kompakt' : ''}` });
  const gitter = h('div', { class: 'r-gitter' });
  flaeche.append(gitter);
  const kopf = h('div', { class: 'vt-planinfo' });
  f.inhalt.append(kopf, flaeche);
  const felder = new Map();

  function zeichnen() {
    leeren(gitter);
    felder.clear();
    if (!plan) return;
    const { stunden } = plan.zeitraster;
    const tage = plan.tage;
    gitter.style.setProperty('--tage', tage.length);
    gitter.style.gridTemplateRows = `30px repeat(${stunden.length}, minmax(${kompakt ? 30 : 48}px, auto))`;
    gitter.append(h('div', { class: 'r-ecke', style: { gridRow: '1', gridColumn: '1' } }));
    tage.forEach((t, i) => gitter.append(h('div', {
      class: `r-kopf${t.heute ? ' vt-heute' : ''}${t.unterrichtstag ? '' : ' vt-frei'}`, style: { gridRow: '1', gridColumn: String(i + 2) }, title: t.frei || '',
    }, h('span', null, `${t.kurz} ${t.text}`))));
    stunden.forEach((s, j) => {
      const pause = s.pauseDanach ? ' pause-danach' : '';
      gitter.append(h('div', { class: `r-stunde${pause}`, style: { gridRow: String(j + 2), gridColumn: '1' } }, h('span', { class: 'nr' }, s.label), s.beginn && !kompakt ? h('span', { class: 'zeit' }, `${s.beginn}–${s.ende}`) : null));
      tage.forEach((t, i) => {
        const feld = h('div', { class: `r-feld${pause}${t.unterrichtstag ? '' : ' gesperrt'}`, style: { gridRow: String(j + 2), gridColumn: String(i + 2) }, title: t.unterrichtstag ? '' : t.frei });
        feld.dataset.tag = i;
        feld.dataset.stunde = s.nr;
        ablageEinrichten(feld, t);
        feld.addEventListener('contextmenu', (ev) => { ev.preventDefault(); kontext(null, t, s.nr, ev); });
        felder.set(`${i}:${s.nr}`, feld);
        gitter.append(feld);
      });
    });
    for (const z of plan.zellen) {
      const t = plan.tage[z.tag];
      const j = stunden.findIndex((s) => s.nr === z.stunde);
      if (j < 0) continue;
      const block = h('div', { class: 'r-block', style: { gridColumn: String(z.tag + 2), gridRow: String(j + 2) } });
      for (const e of z.eintraege) block.append(eintrag(e, t, z.stunde));
      gitter.append(block);
    }
  }

  function eintrag(e, t, stunde) {
    const zeilen = typ === 'lehrer' ? [e.klassen, e.raum] : typ === 'raum' ? [e.klassen, e.lehrer] : [e.lehrer, e.raum];
    const alt = typ === 'lehrer' ? [null, e.raumAlt] : typ === 'raum' ? [null, e.lehrerAlt] : [e.lehrerAlt, e.raumAlt];
    const geaendert = e.status !== 'Regulaer';
    const el = h('div', {
      class: `r-eintrag vt-st-${e.status}${e.durch ? ' vt-durch' : ''}${geaendert ? ' vt-geaendert' : ''}${e.gekoppelt ? ' gekoppelt' : ''}`,
      draggable: schreiben && !e.durch && e.u && e.status !== 'Entfall' ? 'true' : null,
      title: [`${e.u ? `U-Nr. ${e.u}` : ''}${e.vtr ? ` · Vtr-Nr. ${e.vtr}` : ''}`, `${e.fach} – ${e.klassen} – ${e.lehrer || '???'}${e.raum ? ` – ${e.raum}` : ''}`,
        e.art ? `${e.art}${e.text ? `: ${e.text}` : ''}` : e.text, e.lehrerAlt ? `statt ${e.lehrerAlt}` : '', e.fix ? 'fixiert' : ''].filter(Boolean).join('\n'),
    },
    h('span', { class: `fach${e.fachAlt ? ' vt-rottext' : ''}` }, e.fach || e.art || '—'),
    h('span', { class: 'zeile2' }, ...wert(zeilen[0], alt[0], e)),
    kompakt ? null : h('span', { class: 'zeile3' }, ...wert(zeilen[1], alt[1], e)),
    e.fix ? h('span', { class: 'fix' }, '*') : null);
    if (e.farbe && !geaendert) el.style.background = `color-mix(in srgb, ${e.farbe} 55%, white)`;
    el.addEventListener('click', () => {
      gitter.querySelector('.r-eintrag.gewaehlt')?.classList.remove('gewaehlt');
      el.classList.add('gewaehlt');
      if (e.vtr) auswahlMelden({ id: e.vtr, ids: [e.vtr], zeile: { v: e.vtr, datum: t.datum, stunde, u: e.u }, quelle: f.id });
    });
    el.addEventListener('dblclick', () => { if (e.vtr) fensterverwaltung.oeffnen('vertretung.vorschlag', {}); });
    el.addEventListener('contextmenu', (ev) => { ev.preventDefault(); ev.stopPropagation(); kontext(e, t, stunde, ev); });
    el.addEventListener('dragstart', (ev) => {
      const d = { von: t.datum, stunde, u: e.u, zeile: typ === 'lehrer' ? e.zeile : null, laenge: 1 };
      ev.dataTransfer.setData(TYP_TAGESSTUNDE, JSON.stringify(d));
      ev.dataTransfer.effectAllowed = 'move';
      ziehenBeginnt(d);
    });
    el.addEventListener('dragend', () => ziele(null));
    return el;
  }
  function wert(neu, alt, e) {
    if (e.status === 'Offen' && !neu && (typ !== 'lehrer')) return [h('span', { class: 'vt-rottext' }, '???')];
    if (!alt) return [neu || ''];
    return [h('span', { class: 'vt-rottext' }, neu || '???'), h('span', { class: 'vt-alt' }, ` (${alt})`)];
  }

  // ---- Drag & Drop ----
  async function ziehenBeginnt(d) {
    setTimeout(() => flaeche.classList.add('zieht'));
    try {
      const m = await api.get(vpfad(`/plan/zielstatus?von=${d.von}&stunde=${d.stunde}&unterricht=${d.u}${d.zeile !== null && d.zeile !== undefined ? `&zeile=${d.zeile}` : ''}&laenge=${d.laenge}&woche=${plan.datum}`));
      ziele(m);
    } catch { /* ohne Einfärbung */ }
  }
  function ziele(matrix) {
    if (!matrix) flaeche.classList.remove('zieht');
    for (const [k, feld] of felder) {
      feld.classList.remove('ziel-Frei', 'ziel-VorgabeVerletzt', 'ziel-KeinRaum', 'ziel-TauschMoeglich', 'drueber');
      if (!matrix) continue;
      const [t, s] = k.split(':').map(Number);
      const st = matrix[t]?.[s - 1];
      if (st && st !== 'Kollision') feld.classList.add(`ziel-${st}`);
    }
  }
  function ablageEinrichten(feld, t) {
    const passt = (e) => e.dataTransfer.types.includes(TYP_TAGESSTUNDE) || e.dataTransfer.types.includes(TYP_VORMERKUNG);
    feld.addEventListener('dragover', (e) => { if (schreiben && passt(e) && t.unterrichtstag) { e.preventDefault(); feld.classList.add('drueber'); } });
    feld.addEventListener('dragleave', () => feld.classList.remove('drueber'));
    feld.addEventListener('drop', (e) => {
      e.preventDefault();
      feld.classList.remove('drueber');
      const vm = e.dataTransfer.getData(TYP_VORMERKUNG);
      const roh = e.dataTransfer.getData(TYP_TAGESSTUNDE);
      ziele(null);
      const stunde = Number(feld.dataset.stunde);
      if (vm) {
        api.post(vpfad(`/vormerkungen/${JSON.parse(vm).id}/verplanen`), { datum: t.datum, stunde }, { quelle: f.id })
          .then(() => { hinweis('Vormerkung verplant.', 'ok', 2000); return laden(); }).catch(fehlerMelden);
        return;
      }
      if (!roh) return;
      const d = JSON.parse(roh);
      mitRueckfragen(vpfad('/plan/verlegen'), { von: d.von, vonStunde: d.stunde, unterricht: d.u, zeile: d.zeile, laenge: d.laenge, ziel: t.datum, zielStunde: stunde }, { quelle: f.id })
        .then((r) => { if (r?.ok && r.text) hinweis(`${r.text} – Strg+Z macht es rückgängig.`, 'ok', 2500); return laden(); }).catch(fehlerMelden);
    });
  }

  // ---- Kontextmenü ----
  function kontext(e, t, stunde, ev) {
    const pos = e ? { datum: t.datum, stunde, unterricht: e.u, zeile: typ === 'lehrer' ? e.zeile : null } : null;
    const findet = e && !e.durch && ['Regulaer', 'Vertreten', 'Offen', 'Zusaetzlich'].includes(e.status);
    const liste = [{ titel: e ? `${t.kurz} ${t.text} ${stunde}. Std. · ${e.fach} ${e.klassen}` : `${t.kurz} ${t.text} ${stunde}. Std.` }];
    liste.push({ text: 'Stunde entfallen lassen', symbol: 'loeschen', deaktiviert: !schreiben || !findet || !e?.u, aktion: () => aendern('/plan/entfall', pos, true) });
    // Entfall rückgängig: angeklickte bzw. alle entfallenen Stunden der Zelle, bei Doppelstunden wahlweise der ganze Block
    const zelle = plan.zellen.find((z) => z.tag === plan.tage.indexOf(t) && z.stunde === stunde);
    const kandidaten = [];
    for (const x of [e, ...(zelle?.eintraege || [])]) for (const a of x?.aufhebbar || []) if (!kandidaten.some((k) => k.a.u === a.u && k.a.zeile === a.zeile)) kandidaten.push({ x, a });
    for (const { x, a } of kandidaten) {
      const name = kandidaten.length > 1 ? `Entfall rückgängig: ${x.fach} ${x.lehrerAlt || x.lehrer} ${x.klassen}` : 'Entfall rückgängig';
      const p2 = { datum: t.datum, stunde, unterricht: a.u, zeile: a.zeile };
      if (a.block.length > 1) {
        liste.push({ text: `${name} (Block ${a.block[0]}.–${a.block[a.block.length - 1]}. Stunde)`, symbol: 'rueckgaengig', deaktiviert: !schreiben, aktion: () => aendern('/plan/entfallrueckgaengig', { ...p2, block: true }) });
        liste.push({ text: `${name} (nur ${stunde}. Stunde)`, deaktiviert: !schreiben, aktion: () => aendern('/plan/entfallrueckgaengig', p2) });
      } else liste.push({ text: name, symbol: 'rueckgaengig', deaktiviert: !schreiben, aktion: () => aendern('/plan/entfallrueckgaengig', p2) });
    }
    liste.push({ text: findet && e?.u ? 'Sondereinsatz …' : 'Sondereinsatz …', symbol: 'neu', deaktiviert: !schreiben, aktion: () => import('./weitere.js').then((w) => w.sondereinsatzDialog({ datum: t.datum, stunde, klassen: typ === 'klasse' ? plan.element.kurz : '', lehrer: typ === 'lehrer' ? plan.element.kurz : '' })) });
    liste.push({ text: 'Text …', deaktiviert: !schreiben || !findet || !e?.u, aktion: async () => {
      const w = await formular('Text zur Stunde', [{ name: 'text', titel: 'Text', wert: e.text || '', hinweis: 'Wird im Stundenplan und im Vertretungsplan ausgegeben.' }]);
      if (w) await aendern('/plan/text', { ...pos, text: w.text });
    } });
    liste.push(null);
    if (findet && e?.u) liste.push({ text: 'Neben den Plan legen (Vormerkung)', symbol: 'rueckgaengig', deaktiviert: !schreiben, aktion: () => aendern('/plan/vormerken', pos) });
    if (e?.vtr) {
      liste.push({ text: 'Vertretungsvorschlag', symbol: 'vertretung', aktion: () => { auswahlMelden({ id: e.vtr, ids: [e.vtr], zeile: { v: e.vtr }, quelle: f.id }); fensterverwaltung.oeffnen('vertretung.vorschlag', {}); } });
      liste.push({ text: 'Änderungen rückgängig', symbol: 'rueckgaengig', deaktiviert: !schreiben, aktion: () => api.post(vpfad('/plan/aenderungenrueckgaengig'), { ids: [e.vtr] }, { quelle: f.id }).then(laden) });
    }
    if (typ === 'lehrer' && element) liste.push({ text: `Absenz für ${plan.element.kurz} am ${t.text} …`, symbol: 'abwesenheit', deaktiviert: !schreiben, aktion: async () => {
      const { absenzenSpeichern } = await import('./absenzen.js');
      await absenzenSpeichern('lehrer', [{ id: 0, element: plan.element.kurz, von: t.datum, bis: t.datum, sv: 1, sb: 99 }], [], f.id, 'Absenz eingeben');
      await laden();
    } });
    menue(ev.clientX, ev.clientY, liste.filter((x, i, a) => !(x === null && (i === a.length - 1 || a[i + 1] === null))));
  }
  async function aendern(pfad, daten, rueckfragen = false) {
    if (rueckfragen) await mitRueckfragen(vpfad(pfad), daten, { quelle: f.id });
    else await api.post(vpfad(pfad), daten, { quelle: f.id });
    await laden();
  }

  // ---- Werkzeugleiste ----
  const vk = f.werkzeug({ text: '', symbol: 'verknuepfen', nurSymbol: true, titel: 'Fensterverknüpfung: folgt der gewählten Vertretung bzw. dem gewählten Element', aktion: () => { verknuepft = !verknuepft; vk.classList.toggle('gedrueckt', verknuepft); f.parameterSetzen({ verknuepft }); } });
  vk.classList.toggle('gedrueckt', verknuepft);
  const kp = f.werkzeug({ text: '', symbol: 'untereinander', nurSymbol: true, titel: 'Kleiner Plan (kompakt)', aktion: () => { kompakt = !kompakt; flaeche.classList.toggle('kompakt', kompakt); kp.classList.toggle('gedrueckt', kompakt); f.parameterSetzen({ kompakt }); zeichnen(); } });
  kp.classList.toggle('gedrueckt', kompakt);
  f.werkzeug({ text: 'Vormerkungen', symbol: 'info', nurSymbol: true, titel: 'Vormerkungen (in den Plan ziehen)', aktion: () => fensterverwaltung.oeffnen('vertretung.vormerkungen', {}) });

  let ladeNr = 0;
  async function laden() {
    if (!element || !planungsdatum()) { plan = null; zeichnen(); return; }
    const n = ++ladeNr;
    const weg = ladeAnzeige(f.inhalt);
    try {
      const d = await api.get(vpfad(`/plan/${typ}/${element}?datum=${planungsdatum()}`));
      if (n !== ladeNr) return;
      plan = d;
      f.titel(`${TYPEN[typ][0]} ${d.element.kurz}${d.element.lang ? ` (${d.element.lang})` : ''} / Stundenplan (tagesaktuell)`);
      kopf.textContent = `Woche ${d.woche} · Tagesaktueller Stundenplan: Änderungen rot, Entfälle durchgestrichen${d.vormerkungen ? ` · ${d.vormerkungen} offene Vormerkung(en)` : ''}`;
      zeichnen();
      const offen = d.zellen.reduce((a, z) => a + z.eintraege.filter((e) => e.offen && !e.durch).length, 0);
      const entfall = d.zellen.reduce((a, z) => a + z.eintraege.filter((e) => e.status === 'Entfall').length, 0);
      f.fuss.replaceChildren(h('span', null, `${offen} offen`), h('span', null, `${entfall} Entfall`), h('span', { class: 'rechts' }, d.zeitraster ? '' : ''));
    } finally { weg(); }
  }

  let auswahl = null;
  const elementSetzen = (id) => { if (!id || id === element) return; element = id; f.parameterSetzen({ element }); auswahl?.setzen(id); laden().catch(fehlerMelden); };
  (async () => {
    auswahl = await elementAuswahl(f, typ, element, (id) => { element = id; f.parameterSetzen({ element }); laden().catch(fehlerMelden); melden('element', { typ, id, quelle: f.id }); });
    if (!element && auswahl.liste.length) { element = auswahl.liste[0].id; auswahl.setzen(element); f.parameterSetzen({ element }); }
    await laden();
  })().catch(fehlerMelden);

  f.an('vertretung.datum', () => laden().catch(fehlerMelden));
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stundenplan', 'Unterricht', 'Stammdaten')) laden().catch(() => {}); });
  f.an('element', (e) => { if (verknuepft && e.quelle !== f.id && e.typ === typ) elementSetzen(e.id); });
  f.an('vertretung.auswahl', (a) => {
    if (!verknuepft || a?.quelle === f.id || !a?.zeile) return;
    const z = a.zeile;
    if (typ === 'klasse' && z.klasse) elementSetzen(z.klasse);
    else if (typ === 'lehrer' && (p.rolle === 'vertreter' ? z.vertreter : z.lehrer)) elementSetzen(p.rolle === 'vertreter' ? z.vertreter : z.lehrer);
    else if (typ === 'raum' && z.raum) elementSetzen(z.raum);
  });
  void zustand;
}

export const fenster = {
  'vertretung.plan': {
    titel: 'Stundenplan (tagesaktuell)', symbol: 'stundenplan', breite: 720, hoehe: 520,
    symbolFuer: (p) => TYPEN[p.typ]?.[1] || 'stundenplan',
    erstellen: planFenster,
  },
};
