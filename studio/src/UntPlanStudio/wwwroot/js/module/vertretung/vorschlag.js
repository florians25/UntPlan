// Fenster „Vertretungsvorschlag“ wie im Desktop: folgt der markierten Vertretung. Kopf „30.8. Mo-1 Se/Mu/05D“ (aufklappbar),
// Optionen (Stundenblock, Dislozierungen ignorieren, Dauervertretung …), Reiter Vertretungsvorschläge / Betreuungen / Räume mit
// Anzahlen, unten aufklappbar die Vorziehungen. Übernehmen per Doppelklick, Enter oder Knopf; „Weiter zur nächsten offenen“.
import { h, leeren } from '../../kern/dom.js';
import { api } from '../../kern/api.js';
import { darfSchreiben } from '../../kern/zustand.js';
import { betrifft, melden } from '../../kern/ereignisse.js';
import { hinweis, fehlerMelden } from '../../kern/dialog.js';
import { vpfad, mitRueckfragen, aktuelleAuswahl } from './kern.js';
import { reiter, listenTabelle } from './steuer.js';

/** Merker zur Stunde wie im Vorbild: 0–3 weiß, 4–8 zunehmend rosa, 9 rot; Bereitschaft (Fachkürzel) grün. */
function merkerFarbe(m) {
  if (!m || m === 'x') return null;
  const n = Number(m);
  if (Number.isNaN(n)) return '#c8ebc8';
  if (n >= 9) return '#ff3737';
  if (n < 4) return null;
  const g = 217 - (n - 4) * 17;
  return `rgb(255,${g},${g})`;
}

/** Spalte „Stundenplan“: drei Stunden, die Vertretungsstunde gestrichelt markiert, Entfall rot durchgestrichen. */
function stundenplanSpalten() {
  return [0, 1, 2].map((i) => ({
    titel: i === 0 ? 'Stundenplan' : '', breite: 50, klasse: 'vt-mini',
    wert: (k) => k.s?.[i]?.text ?? '',
    zelle: (k, td) => {
      const z = k.s?.[i];
      if (!z) return;
      if (z.farbe && !z.entfallen) td.style.background = z.farbe;
      if (z.schrift) td.style.color = z.schrift;
      td.classList.toggle('entfallen', z.entfallen);
      td.classList.toggle('ziel', z.ziel);
    },
  }));
}

const zaehlerZelle = (k, td) => { td.style.background = Number(k.zaehlerWert) > 0 ? '#ff8080' : '#fcfc81'; };

function vorschlagFenster(f) {
  const schreiben = darfSchreiben();
  let auswahl = aktuelleAuswahl();
  let daten = null;
  let weiter = true;
  let alle = false;
  const kopf = h('button', { type: 'button', class: 'vt-klapp' }, h('span', { class: 'pfeil' }, '▼'), h('span', { class: 'text' }, 'Keine Vertretung markiert'));
  const details = h('div', { class: 'vt-details', hidden: true });
  const opt = { block: h('input', { type: 'checkbox' }), disloz: h('input', { type: 'checkbox' }), dauer: h('input', { type: 'checkbox' }), nahe: h('input', { type: 'checkbox' }), besetzt: h('input', { type: 'checkbox' }), zusatz: h('input', { type: 'checkbox' }) };
  const L = (el, text, titel, cls = '') => h('label', { title: titel, class: cls }, el, ` ${text}`);
  const optionen = h('div', { class: 'vt-optionen' },
    L(opt.block, 'Stundenblock', 'Vorschlag für alle Stunden des Blocks (Doppelstunde)'),
    L(opt.disloz, 'Dislozierungen ignorieren', 'Auch Lehrkräfte vorschlagen, deren angrenzender Unterricht in einem anderen Gebäude liegt'),
    L(opt.dauer, 'Dauervertretung', 'Vertreter für alle Stunden dieses Unterrichts während der Absenz übernehmen'),
    L(opt.nahe, 'Nur in der Nähe', 'Nur gekoppelte Lehrkräfte, gleicher Gang oder gleiches Stockwerk', 'nur-1'),
    L(opt.besetzt, 'Auch besetzte Räume anzeigen', '', 'nur-2'), L(opt.zusatz, 'Zusätzlichen Raum zuordnen', 'Den gewählten Raum zusätzlich zum bestehenden Raum zuordnen', 'nur-2'));
  const tabs = reiter(['Vertretungsvorschläge (0)', 'Betreuungen (0)', 'Räume (0)'], (i) => zeigen(i));
  const flaeche = h('div', { class: 'vt-vorschlag-flaeche' });
  const vorzKopf = h('button', { type: 'button', class: 'vt-klapp unten' }, h('span', { class: 'pfeil' }, '▲'), h('span', { class: 'text' }, 'Vorziehungen (0)'));
  const vorzFlaeche = h('div', { class: 'vt-vorz', hidden: true });
  f.inhalt.append(h('div', { class: 'vt-vorschlag' }, kopf, details, optionen, tabs.el, flaeche, vorzKopf, vorzFlaeche));
  kopf.addEventListener('click', () => { details.hidden = !details.hidden; kopf.firstChild.textContent = details.hidden ? '▼' : '▲'; });
  vorzKopf.addEventListener('click', () => { vorzFlaeche.hidden = !vorzFlaeche.hidden; vorzKopf.firstChild.textContent = vorzFlaeche.hidden ? '▲' : '▼'; });
  for (const k of ['block', 'disloz', 'nahe', 'besetzt']) opt[k].addEventListener('change', () => laden());

  const kandidaten = listenTabelle(flaeche, {
    leerText: 'Keine Vorschläge',
    spalten: [
      { titel: 'Name', breite: 76, wert: (k) => k.name, zelle: (k, td) => { td.title = k.lang + (k.lage ? ` – ${k.lage}` : ''); td.classList.toggle('vt-rot', k.rot); td.classList.toggle('fett', k.benachrichtigen); } },
      ...stundenplanSpalten(),
      { titel: 'Merker zur Stunde', breite: 70, wert: (k) => k.merker, zelle: (k, td) => { const c = merkerFarbe(k.merker); if (c) td.style.background = c; } },
      { titel: 'Vertr.', breite: 52, klasse: 'rechts', wert: (k) => k.vertr }, { titel: 'Entfall', breite: 52, klasse: 'rechts', wert: (k) => k.entf },
      { titel: 'Zähler', breite: 52, klasse: 'rechts', wert: (k) => k.zaehler, zelle: zaehlerZelle },
      { titel: 'Zeitwunsch', breite: 40, klasse: 'mitte', wert: (k) => k.zw }, { titel: 'Jahressaldo', breite: 56, klasse: 'rechts', wert: (k) => k.jahr },
      { titel: 'Kennzeichen', breite: 40, klasse: 'mitte', wert: (k) => (k.kennz ? 'K' : ''), zelle: (k, td) => { if (k.kennz) { td.title = k.kennzText; td.classList.add('vt-kennz'); } } },
      { titel: 'reg. Bereitschaft', breite: 40, klasse: 'mitte', wert: (k) => k.regB }, { titel: 'Dep.', breite: 56, klasse: 'rechts', wert: (k) => k.dep },
      { titel: 'Fach', breite: 38, klasse: 'mitte vt-haken', wert: (k) => (k.fach ? '✔' : '') }, { titel: 'Klasse', breite: 44, klasse: 'mitte vt-haken', wert: (k) => k.klasse },
      { titel: 'Std./Tag', breite: 48, klasse: 'mitte', wert: (k) => k.stdTag }, { titel: 'Woche', breite: 46, klasse: 'mitte', wert: (k) => k.woche },
      { titel: 'Monat', breite: 46, klasse: 'mitte', wert: (k) => k.monat }, { titel: 'Pkt.', breite: 40, klasse: 'rechts', wert: (k) => k.punkte },
      { titel: 'Kriterien', wert: (k) => k.kriterien },
    ],
    beiDoppelklick: () => uebernehmen(),
  });
  const betreuungen = listenTabelle(flaeche, {
    leerText: 'Keine Betreuungen möglich',
    spalten: [
      { titel: 'Name', breite: 76, wert: (k) => k.name, zelle: (k, td) => { td.title = k.lang; } }, ...stundenplanSpalten(),
      { titel: 'Raum', breite: 50, wert: (k) => k.raum }, { titel: 'Klasse', breite: 56, wert: (k) => k.klasse }, { titel: 'Fach', breite: 46, wert: (k) => k.fach },
      { titel: 'Zähler', breite: 52, klasse: 'rechts', wert: (k) => k.zaehler, zelle: zaehlerZelle }, { titel: 'Zeitwunsch', breite: 40, klasse: 'mitte', wert: (k) => k.zw },
      { titel: 'Jahressaldo', breite: 56, klasse: 'rechts', wert: (k) => k.jahr }, { titel: 'Kennzeichen', breite: 40, klasse: 'mitte', wert: (k) => (k.kennz ? 'K' : '') },
      { titel: 'Kopplung', breite: 50, klasse: 'mitte vt-haken', wert: (k) => (k.kopplung ? '✔' : '') }, { titel: 'Nähe', breite: 110, wert: (k) => k.naehe },
      { titel: 'Betr. heute', breite: 60, klasse: 'mitte', wert: (k) => k.heute },
    ],
    beiDoppelklick: () => betreuung(),
  });
  const raeume = listenTabelle(flaeche, {
    leerText: 'Keine Räume',
    zeilenKlasse: (r) => (r.besetzt ? 'vt-besetzt' : r.ar || r.sr ? 'vt-ausweich' : ''),
    spalten: [
      { titel: 'Rm', breite: 70, wert: (r) => r.name, zelle: (r, td) => { td.title = r.lang; } }, { titel: 'Kap', breite: 44, klasse: 'rechts', wert: (r) => r.kap },
      { titel: 'Ausw-Rm.', breite: 56, klasse: 'mitte vt-haken', wert: (r) => (r.ar ? '✔' : '') }, { titel: 'Ausw-Sr.', breite: 56, klasse: 'mitte vt-haken', wert: (r) => (r.sr ? '✔' : '') },
      { titel: 'Besetzt', breite: 56, klasse: 'mitte', wert: (r) => (r.gesperrt ? 'gesperrt' : r.besetzt ? 'X' : '') }, { titel: 'Raumgruppen', breite: 90, wert: (r) => r.rg },
      { titel: 'Gang', breite: 60, wert: (r) => r.gang }, { titel: 'Belegung', wert: (r) => r.belegung },
    ],
    beiDoppelklick: () => raum(),
  });
  const vorziehungen = listenTabelle(vorzFlaeche, {
    leerText: 'Keine Vorziehungen möglich',
    spalten: [
      { titel: 'Name', breite: 70, wert: (v) => v.lehrer }, { titel: 'Tag', breite: 76, wert: (v) => v.tag }, { titel: 'Std.', breite: 40, klasse: 'mitte', wert: (v) => String(v.stunde) },
      { titel: 'Klasse', breite: 60, wert: (v) => v.klassen }, { titel: 'Fach', breite: 56, wert: (v) => v.fach }, { titel: 'Raum', breite: 50, wert: (v) => v.raum },
      { titel: 'Art', wert: (v) => v.art, zelle: (v, td) => { td.title = v.beschreibung; } },
    ],
    beiDoppelklick: () => vorziehen(),
  });
  const tabellen = [kandidaten, betreuungen, raeume];
  function zeigen(i) {
    tabellen.forEach((t, j) => { t.el.hidden = j !== i; });
    optionen.classList.toggle('reiter-1', i === 1);
    optionen.classList.toggle('reiter-2', i === 2);
    tabellen[i].el.focus();
  }
  zeigen(0);

  // ---- Aktionen ----
  const ids = () => (auswahl?.ids?.includes(auswahl.id) ? auswahl.ids : auswahl ? [auswahl.id] : []);
  async function nachAktion(warOffen) {
    await laden();
    if (warOffen && weiter && daten && !daten.offen && !daten.raumOffen) melden('vertretung.naechsteOffene');
  }
  async function uebernehmen() {
    const k = kandidaten.aktiv;
    if (!k || !auswahl || !schreiben) return;
    const warOffen = !!daten?.offen;
    const r = await mitRueckfragen(vpfad(`/vorschlag/${auswahl.id}/uebernehmen`), { lehrer: k.lehrer, ids: ids(), block: opt.block.checked && !opt.block.disabled, dauer: opt.dauer.checked && !opt.dauer.disabled }, { quelle: f.id });
    if (r) await nachAktion(warOffen);
  }
  async function betreuung() {
    const k = betreuungen.aktiv;
    if (!k || !auswahl || !schreiben) return;
    const warOffen = !!daten?.offen;
    await mitRueckfragen(vpfad(`/vorschlag/${auswahl.id}/betreuung`), { lehrer: k.lehrer, ids: ids(), block: opt.block.checked }, { quelle: f.id });
    await nachAktion(warOffen);
  }
  async function raum() {
    const r = raeume.aktiv;
    if (!r || !auswahl || !schreiben) return;
    const warOffen = !!daten?.raumOffen;
    const erg = await mitRueckfragen(vpfad(`/vorschlag/${auswahl.id}/raum`), { raum: r.raum, zusatz: opt.zusatz.checked, ids: ids(), block: opt.block.checked }, { quelle: f.id });
    if (erg) await nachAktion(warOffen);
  }
  async function vorziehen() {
    const v = vorziehungen.aktiv;
    if (!v || !auswahl || !schreiben) return;
    await api.post(vpfad(`/vorschlag/${auswahl.id}/vorziehen`), { datum: v.datum, stunde: v.stunde, unterricht: v.u }, { quelle: f.id });
    hinweis('Vorziehung: Unterricht in die offene Stunde verlegt.', 'ok', 2500);
    await nachAktion(true);
  }

  if (schreiben) {
    f.werkzeug({ text: 'Übernehmen', symbol: 'beispiel', titel: 'Vertreter übernehmen (Doppelklick, Enter)', aktion: () => [uebernehmen, betreuung, raum][tabs.index]() });
    f.werkzeug({ text: 'Entfall', symbol: 'loeschen', titel: 'Entfall', aktion: async () => { if (!auswahl) return; await mitRueckfragen(vpfad('/entfall'), { ids: ids() }, { quelle: f.id }); await nachAktion(true); } });
    f.werkzeug({ text: 'Vertr. o. Lehrkr.', symbol: 'plus', titel: 'Vertretung ohne Lehrkraft', aktion: async () => { if (!auswahl) return; await api.post(vpfad('/ohnelehrer'), { ids: ids() }, { quelle: f.id }); await nachAktion(true); } });
    f.trenner();
  }
  const wk = f.werkzeug({ text: 'Weiter zur nächsten offenen', symbol: 'pfeil_runter', nurSymbol: true, titel: 'Nach dem Zuweisen automatisch die nächste offene Vertretung wählen', aktion: () => { weiter = !weiter; wk.classList.toggle('gedrueckt', weiter); } });
  wk.classList.add('gedrueckt');
  const ak = f.werkzeug({ text: 'Alle Lehrer', symbol: 'lehrer', titel: 'Auch gesperrte und weit entfernte Lehrer anzeigen', aktion: () => { alle = !alle; ak.classList.toggle('gedrueckt', alle); laden(); } });
  f.werkzeug({ text: 'Einstellungen', symbol: 'einstellungen', nurSymbol: true, titel: 'Einstellungen Vertretungsvorschlag', aktion: () => import('./weitere.js').then((w) => w.einstellungenDialog(2)).then(() => laden()) });

  let ladeNr = 0;
  async function laden() {
    const n = ++ladeNr;
    if (!auswahl?.id) {
      daten = null;
      kopf.lastChild.textContent = 'Keine Vertretung markiert';
      details.textContent = 'Bitte im Vertretungsfenster oder im tagesaktuellen Stundenplan eine Vertretung markieren.';
      tabellen.forEach((t) => t.setzen([]));
      vorziehungen.setzen([]);
      zaehlen();
      return;
    }
    const q = new URLSearchParams({ block: opt.block.checked, alle, disloz: opt.disloz.checked, nahe: opt.nahe.checked, besetzt: opt.besetzt.checked });
    let d;
    try { d = await api.get(vpfad(`/vorschlag/${auswahl.id}?${q}`)); } catch (e) { if (e.status === 404) { auswahl = null; return laden(); } throw e; }
    if (n !== ladeNr) return undefined;
    daten = d;
    const mehr = auswahl.ids?.length > 1 ? `   (+${auswahl.ids.length - 1} weitere markiert)` : '';
    kopf.lastChild.textContent = d.kopf + mehr;
    leeren(details).append(...d.details.split('\n').map((z) => h('div', null, z)));
    opt.block.disabled = !d.optionen.block;
    opt.disloz.disabled = !d.optionen.disloz;
    opt.dauer.disabled = !d.optionen.dauer;
    kandidaten.setzen(d.kandidaten);
    betreuungen.setzen(d.betreuungen);
    raeume.setzen(d.raeume);
    vorziehungen.setzen(d.vorziehungen);
    zaehlen();
    return undefined;
  }
  function zaehlen() {
    tabs.texte([`Vertretungsvorschläge (${kandidaten.zeilen.length})`, `Betreuungen (${betreuungen.zeilen.length})`, `Räume (${raeume.zeilen.length})`]);
    vorzKopf.lastChild.textContent = `Vorziehungen (${vorziehungen.zeilen.length})`;
  }
  f.an('vertretung.auswahl', (a) => { auswahl = a; laden().catch(fehlerMelden); });
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stundenplan', 'Stammdaten', 'Einstellungen')) laden().catch(() => {}); });
  laden().catch(fehlerMelden);
}

export const fenster = {
  'vertretung.vorschlag': { titel: 'Vertretungsvorschlag', symbol: 'vertretung', breite: 760, hoehe: 560, einzig: () => 'vorschlag', erstellen: vorschlagFenster },
};
