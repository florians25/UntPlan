// Stundenpläne (Klasse, Lehrkraft, Raum) als zeitbasiertes Wochenraster.
import { e, icon, holen, heuteIso, plusTage, montag, datumKurz } from '../ui.js';
import { wochenGitter, tagAuswahl, legende, kartenBinden } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';

const TYPEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', raum: 'Raum' };
const LISTE = { klasse: 'klassen', lehrer: 'lehrer', raum: 'raeume' };

/** Langname ohne vorangestelltes Kürzel („7a – Klasse 7a“ → „Klasse 7a“). */
function langname(plan) {
  let n = (plan.name || '').trim();
  if (n.toLowerCase().startsWith(plan.element.toLowerCase())) n = n.slice(plan.element.length).replace(/^\s*[–-]\s*/, '');
  return n && n !== plan.element ? n : '';
}

export async function zeigen(seite, kontext, [typParam, elementParam, datumParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const erlaubt = ich.kollegium ? ['klasse', 'lehrer', 'raum'] : ['klasse', 'raum'];
  let typ = erlaubt.includes(typParam) ? typParam : ich.typ;
  let element = elementParam;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  if (!element) {
    if (typ === ich.typ && ich.element) element = ich.element;
    else element = el[LISTE[typ]][0]?.kurz;
  }
  const ziel = (t, x, d) => `#/plan/${t}/${encodeURIComponent(x ?? '')}/${d}`;
  if (!element) {
    kontext.titel('Stundenplan');
    seite.innerHTML = '<div class="karte"><div class="leer">Noch keine Stammdaten vorhanden.</div></div>';
    return;
  }

  kontext.titel(`Stundenplan ${element}`, `<button class="knopf flach rund" data-drucken title="Drucken">${icon('drucken')}</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();

  seite.innerHTML = `
    <div class="plan-steuerung">
      ${erlaubt.length > 1 ? `<div class="segment" role="tablist">${erlaubt.map(t => `<button data-typ="${t}" class="${t === typ ? 'aktiv' : ''}">${TYPEN[t]}</button>`).join('')}</div>` : ''}
      <span data-wahl></span>
      <span class="abstand" style="flex:1"></span>
      <div class="segment">
        <button data-woche="-7" title="Vorherige Woche">${icon('links', 'icon-klein')}</button>
        <button data-woche="0">Heute</button>
        <button data-woche="7" title="Nächste Woche">${icon('rechts', 'icon-klein')}</button>
      </div>
      <input type="date" value="${datum}" data-datum aria-label="Datum" style="width:auto">
    </div>
    <div data-inhalt><div class="lade">Plan wird geladen …</div></div>`;

  seite.querySelectorAll('[data-typ]').forEach(b => b.onclick = () => {
    const t = b.dataset.typ;
    const x = t === ich.typ && ich.element ? ich.element : el[LISTE[t]][0]?.kurz;
    kontext.navigieren(ziel(t, x, datum));
  });
  seite.querySelector('[data-wahl]').replaceWith(elementWahl({
    eintraege: el[LISTE[typ]], wert: element, platzhalter: `${TYPEN[typ]} wählen`,
    beiWahl: x => kontext.navigieren(ziel(typ, x, datum)),
  }));
  seite.querySelectorAll('[data-woche]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.woche);
    kontext.navigieren(ziel(typ, element, n === 0 ? heuteIso() : plusTage(montag(datum), n)));
  });
  seite.querySelector('[data-datum]').onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(typ, element, ev.target.value)); };

  const istAktuell = kontext.aktuell();
  const plan = await holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  let gewaehlt = Math.max(0, plan.tage.findIndex(t => t.datum === datum));

  const zeichnen = () => {
    const erster = plan.tage[0], letzter = plan.tage[plan.tage.length - 1];
    inhalt.innerHTML = `
      <div class="plan-titel"><h2>${e(TYPEN[typ])} ${e(plan.element)}${langname(plan) ? ` <span class="gedimmt" style="font-weight:500">${e(langname(plan))}</span>` : ''}</h2>
        <span class="gedimmt">${erster ? `${datumKurz(erster.datum)} – ${datumKurz(letzter.datum)}${letzter.datum.slice(0, 4)}` : ''}${plan.woche ? ` · ${e(plan.woche)}-Woche` : ''}</span></div>
      ${tagAuswahl(plan, gewaehlt)}
      ${wochenGitter(plan, { gewaehlterTag: gewaehlt })}
      ${legende(typ === 'lehrer')}`;
    inhalt.querySelectorAll('[data-tagwahl]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.tagwahl); zeichnen(); });
    kartenBinden(inhalt, plan.karten, kontext);
  };
  zeichnen();
}
