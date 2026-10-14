// Stundenpläne (Klasse, Lehrkraft, Raum) als Wochenraster nach Uhrzeit.
import { e, icon, holen, heuteIso, plusTage, montag, datumKurz, lade } from '../ui.js';
import { wochenGitter, tagAuswahl, kartenBinden } from '../plangitter.js';
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
  const typ = erlaubt.includes(typParam) ? typParam : ich.typ;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  let element = elementParam;
  if (!element) element = typ === ich.typ && ich.element ? ich.element : el[LISTE[typ]][0]?.kurz;
  const ziel = (t, x, d) => `#/plan/${t}/${encodeURIComponent(x ?? '')}/${d}`;
  if (!element) {
    kontext.titel('Stundenplan');
    seite.innerHTML = '<div class="alert alert-secondary">Noch keine Stammdaten vorhanden.</div>';
    return;
  }

  kontext.titel('Stundenplan', `<button class="btn btn-outline-secondary btn-sm" data-drucken>${icon('drucken', 'me-1')}Drucken</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();

  seite.innerHTML = `
    <div class="d-flex flex-wrap align-items-center gap-2 mb-3 nicht-drucken">
      ${erlaubt.length > 1 ? `<div class="btn-group" role="group" aria-label="Planart">${erlaubt.map(t => `<button type="button" data-typ="${t}" class="btn btn-outline-primary ${t === typ ? 'active' : ''}">${TYPEN[t]}</button>`).join('')}</div>` : ''}
      <span data-wahl></span>
      <div class="ms-md-auto d-flex gap-2">
        <div class="btn-group" role="group" aria-label="Woche">
          <button type="button" class="btn btn-outline-secondary" data-woche="-7" title="Vorherige Woche">${icon('links')}</button>
          <button type="button" class="btn btn-outline-secondary" data-woche="0">Heute</button>
          <button type="button" class="btn btn-outline-secondary" data-woche="7" title="Nächste Woche">${icon('rechts')}</button>
        </div>
        <input type="date" class="form-control" style="width:auto" value="${datum}" data-datum aria-label="Datum">
      </div>
    </div>
    <div data-inhalt>${lade()}</div>`;

  seite.querySelectorAll('[data-typ]').forEach(b => b.onclick = () => {
    const t = b.dataset.typ;
    kontext.navigieren(ziel(t, t === ich.typ && ich.element ? ich.element : el[LISTE[t]][0]?.kurz, datum));
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
      <div class="d-flex flex-wrap align-items-baseline gap-2 mb-2">
        <h2 class="h5 mb-0">${e(TYPEN[typ])} ${e(plan.element)}${langname(plan) ? ` <span class="text-body-secondary fw-normal">– ${e(langname(plan))}</span>` : ''}</h2>
        <span class="text-body-secondary small">${erster ? `${datumKurz(erster.datum)} – ${datumKurz(letzter.datum)}${letzter.datum.slice(0, 4)}` : ''}${plan.woche ? ` · ${e(plan.woche)}-Woche` : ''}</span>
      </div>
      ${tagAuswahl(plan, gewaehlt)}
      ${wochenGitter(plan, { gewaehlterTag: gewaehlt })}`;
    inhalt.querySelectorAll('[data-tagwahl]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.tagwahl); zeichnen(); });
    kartenBinden(inhalt, plan.karten, kontext);
  };
  zeichnen();
}
