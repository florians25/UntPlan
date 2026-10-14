// Elementwahl: Auswahlknopf mit aufklappender Suchliste (Kürzel und Langname, Tastatursteuerung).
import { e } from './ui.js';

/** eintraege: [{ kurz, name }]; wert: aktuelles Kürzel; beiWahl(kurz). Liefert ein DOM-Element. */
export function elementWahl({ eintraege, wert, platzhalter = 'Auswählen …', beiWahl }) {
  const wurzel = document.createElement('div');
  wurzel.className = 'position-relative elementwahl';
  const aktuell = eintraege.find(x => x.kurz.toLowerCase() === String(wert ?? '').toLowerCase());
  wurzel.innerHTML = `<button class="btn btn-outline-secondary bg-white text-body dropdown-toggle wahl-knopf" type="button" aria-expanded="false">
      ${aktuell ? `<span class="fw-semibold">${e(aktuell.kurz)}</span>${aktuell.name && aktuell.name !== aktuell.kurz ? ` <span class="text-body-secondary">${e(aktuell.name)}</span>` : ''}` : `<span class="text-body-secondary">${e(platzhalter)}</span>`}
    </button>
    <div class="wahl-menue bg-white border rounded shadow">
      <div class="p-2 border-bottom"><input type="search" class="form-control form-control-sm" placeholder="Suchen" aria-label="Suchen"></div>
      <div class="liste list-group list-group-flush" role="listbox"></div>
    </div>`;
  const knopf = wurzel.querySelector('button');
  const menue = wurzel.querySelector('.wahl-menue');
  const eingabe = menue.querySelector('input');
  const liste = menue.querySelector('.liste');
  let markiert = 0, gefiltert = [];

  const zeichnen = () => {
    const f = eingabe.value.trim().toLowerCase();
    gefiltert = !f ? eintraege : [
      ...eintraege.filter(x => x.kurz.toLowerCase().startsWith(f)),
      ...eintraege.filter(x => !x.kurz.toLowerCase().startsWith(f) && (x.kurz + ' ' + (x.name ?? '')).toLowerCase().includes(f)),
    ];
    markiert = Math.min(markiert, Math.max(0, gefiltert.length - 1));
    liste.innerHTML = gefiltert.length ? gefiltert.map((x, i) => `<button type="button" role="option" class="list-group-item list-group-item-action border-0 py-1 ${i === markiert ? 'active' : ''}" data-i="${i}">
        <b>${e(x.kurz)}</b><span>${e(x.name && x.name !== x.kurz ? x.name : '')}</span></button>`).join('')
      : '<div class="px-3 py-2 small text-body-secondary">Kein Treffer</div>';
    liste.querySelector('.active')?.scrollIntoView({ block: 'nearest' });
  };
  const offen = () => menue.classList.contains('show');
  const schliessen = () => { menue.classList.remove('show'); knopf.setAttribute('aria-expanded', 'false'); document.removeEventListener('mousedown', aussen, true); };
  const aussen = ev => { if (!wurzel.contains(ev.target)) schliessen(); };
  const waehlen = x => { schliessen(); if (x) beiWahl(x.kurz); };

  knopf.onclick = () => {
    if (offen()) return schliessen();
    menue.classList.add('show');
    menue.style.left = '0';
    const r = menue.getBoundingClientRect();
    if (r.right > window.innerWidth - 8) menue.style.left = `${window.innerWidth - 8 - r.right}px`;
    knopf.setAttribute('aria-expanded', 'true');
    eingabe.value = '';
    markiert = Math.max(0, eintraege.indexOf(aktuell));
    zeichnen();
    eingabe.focus();
    document.addEventListener('mousedown', aussen, true);
  };
  eingabe.oninput = () => { markiert = 0; zeichnen(); };
  eingabe.onkeydown = ev => {
    if (ev.key === 'ArrowDown') { markiert = Math.min(gefiltert.length - 1, markiert + 1); zeichnen(); ev.preventDefault(); }
    else if (ev.key === 'ArrowUp') { markiert = Math.max(0, markiert - 1); zeichnen(); ev.preventDefault(); }
    else if (ev.key === 'Enter') { waehlen(gefiltert[markiert]); ev.preventDefault(); }
    else if (ev.key === 'Escape') { schliessen(); knopf.focus(); }
  };
  liste.onclick = ev => { const b = ev.target.closest('[data-i]'); if (b) waehlen(gefiltert[Number(b.dataset.i)]); };
  return wurzel;
}
