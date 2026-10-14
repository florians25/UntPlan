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

  const mo = montag(datum);
  const fr = plusTage(mo, 4);
  const liste = el[LISTE[typ]];
  const pos = liste.findIndex(x => x.kurz.toLowerCase() === String(element).toLowerCase());
  seite.innerHTML = `
    <div class="planleiste mb-3 nicht-drucken">
      <div class="links">
        ${erlaubt.length > 1 ? `<div class="btn-group btn-group-sm" role="group" aria-label="Planart">${erlaubt.map(t => `<button type="button" data-typ="${t}" class="btn btn-outline-primary ${t === typ ? 'active' : ''}">${TYPEN[t]}</button>`).join('')}</div>` : ''}
        <div class="element-pill">
          <button type="button" class="rundknopf" data-element="-1" title="Vorheriges Element (Alt+←)" ${pos <= 0 ? 'disabled' : ''}>${icon('links')}</button>
          <span data-wahl></span>
          <button type="button" class="rundknopf" data-element="1" title="Nächstes Element (Alt+→)" ${pos < 0 || pos >= liste.length - 1 ? 'disabled' : ''}>${icon('rechts')}</button>
        </div>
      </div>
      <div class="mitte">
        <button type="button" class="rundknopf" data-woche="-7" title="Vorherige Woche">${icon('links')}</button>
        <label class="zeitraum" title="Datum wählen">${datumKurz(mo)} – ${datumKurz(fr)}${fr.slice(0, 4)}<small data-wochenart></small>
          <input type="date" value="${datum}" data-datum aria-label="Datum"></label>
        <button type="button" class="rundknopf" data-woche="7" title="Nächste Woche">${icon('rechts')}</button>
      </div>
      <div class="rechts"><button type="button" class="heute-pill" data-woche="0">Heute</button></div>
    </div>
    <div data-inhalt>${lade()}</div>`;

  seite.querySelectorAll('[data-typ]').forEach(b => b.onclick = () => {
    const t = b.dataset.typ;
    kontext.navigieren(ziel(t, t === ich.typ && ich.element ? ich.element : el[LISTE[t]][0]?.kurz, datum));
  });
  seite.querySelector('[data-wahl]').replaceWith(elementWahl({
    eintraege: liste, wert: element, platzhalter: `${TYPEN[typ]} wählen`,
    beiWahl: x => kontext.navigieren(ziel(typ, x, datum)),
  }));
  const blaettern = n => { const x = liste[pos + n]; if (x) kontext.navigieren(ziel(typ, x.kurz, datum)); };
  seite.querySelectorAll('[data-element]').forEach(b => b.onclick = () => blaettern(Number(b.dataset.element)));
  seite.querySelectorAll('[data-woche]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.woche);
    kontext.navigieren(ziel(typ, element, n === 0 ? heuteIso() : plusTage(mo, n)));
  });
  const datumFeld = seite.querySelector('[data-datum]');
  datumFeld.onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(typ, element, ev.target.value)); };
  datumFeld.onclick = () => { try { datumFeld.showPicker(); } catch { /* ältere Browser */ } };
  const tasten = ev => {
    if (!document.body.contains(seite) || !location.hash.startsWith('#/plan')) { document.removeEventListener('keydown', tasten); return; }
    if (!ev.altKey || ev.target.closest('input, textarea, select')) return;
    if (ev.key === 'ArrowLeft' || ev.key === 'ArrowRight') { ev.preventDefault(); document.removeEventListener('keydown', tasten); blaettern(ev.key === 'ArrowLeft' ? -1 : 1); }
  };
  document.addEventListener('keydown', tasten);

  const istAktuell = kontext.aktuell();
  const plan = await holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  let gewaehlt = Math.max(0, plan.tage.findIndex(t => t.datum === datum));
  if (plan.woche) seite.querySelector('[data-wochenart]').textContent = `${plan.woche}-Woche`;

  const zeichnen = () => {
    const erster = plan.tage[0], letzter = plan.tage[plan.tage.length - 1];
    inhalt.innerHTML = `
      <div class="d-flex flex-wrap align-items-baseline gap-2 mb-2">
        <h2 class="h5 mb-0">${e(TYPEN[typ])} ${e(plan.element)}${langname(plan) ? ` <span class="text-body-secondary fw-normal">– ${e(langname(plan))}</span>` : ''}</h2>
        <span class="text-body-secondary small d-none d-print-inline">${erster ? `${datumKurz(erster.datum)} – ${datumKurz(letzter.datum)}${letzter.datum.slice(0, 4)}` : ''}</span>
      </div>
      ${tagAuswahl(plan, gewaehlt)}
      ${wochenGitter(plan, { gewaehlterTag: gewaehlt })}`;
    inhalt.querySelectorAll('[data-tagwahl]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.tagwahl); zeichnen(); });
    kartenBinden(inhalt, plan.karten, kontext);
  };
  zeichnen();
}
