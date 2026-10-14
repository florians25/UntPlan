// Elementwahl: Knopf mit aufklappender Suchliste (Kürzel und Langname, Tastatursteuerung).
import { e, icon } from './ui.js';

/**
 * eintraege: [{ kurz, name }]; wert: aktuelles Kürzel; beiWahl(kurz).
 * Liefert ein DOM-Element.
 */
export function elementWahl({ eintraege, wert, platzhalter = 'Auswählen …', beiWahl }) {
  const wurzel = document.createElement('div');
  wurzel.className = 'wahl';
  const aktuell = eintraege.find(x => x.kurz.toLowerCase() === String(wert ?? '').toLowerCase());
  wurzel.innerHTML = `<button class="knopf wahl-knopf" type="button" aria-haspopup="listbox">
      <span>${aktuell ? `<b>${e(aktuell.kurz)}</b>${aktuell.name && aktuell.name !== aktuell.kurz ? ` <span class="gedimmt">${e(aktuell.name)}</span>` : ''}` : `<span class="gedimmt">${e(platzhalter)}</span>`}</span>
      ${icon('unten', 'icon-klein')}</button>`;
  const knopf = wurzel.querySelector('button');
  let liste = null, markiert = 0, gefiltert = [];

  const schliessen = () => {
    liste?.remove(); liste = null;
    document.removeEventListener('mousedown', aussen, true);
  };
  const aussen = ev => { if (!wurzel.contains(ev.target)) schliessen(); };
  const waehlen = x => { schliessen(); if (x) beiWahl(x.kurz); };

  const zeichnen = filter => {
    const f = filter.trim().toLowerCase();
    gefiltert = !f ? eintraege : [
      ...eintraege.filter(x => x.kurz.toLowerCase().startsWith(f)),
      ...eintraege.filter(x => !x.kurz.toLowerCase().startsWith(f) && (x.kurz + ' ' + (x.name ?? '')).toLowerCase().includes(f)),
    ];
    markiert = Math.min(markiert, Math.max(0, gefiltert.length - 1));
    const box = liste.querySelector('.wahl-eintraege');
    box.innerHTML = gefiltert.length ? gefiltert.map((x, i) => `<button type="button" role="option" class="wahl-eintrag ${i === markiert ? 'markiert' : ''}" data-i="${i}">
        <b>${e(x.kurz)}</b><span>${e(x.name && x.name !== x.kurz ? x.name : '')}</span></button>`).join('')
      : '<div class="leer klein">Kein Treffer</div>';
    box.querySelector('.markiert')?.scrollIntoView({ block: 'nearest' });
  };

  knopf.onclick = () => {
    if (liste) return schliessen();
    liste = document.createElement('div');
    liste.className = 'wahl-liste';
    liste.innerHTML = `<input type="search" placeholder="Suchen (Kürzel oder Name)" aria-label="Suchen"><div class="wahl-eintraege" role="listbox"></div>`;
    wurzel.append(liste);
    // nicht über den rechten Bildschirmrand hinaus
    const r = liste.getBoundingClientRect();
    if (r.right > window.innerWidth - 8) liste.style.left = `${Math.min(0, window.innerWidth - 8 - r.right)}px`;
    const eingabe = liste.querySelector('input');
    markiert = Math.max(0, eintraege.indexOf(aktuell));
    zeichnen('');
    eingabe.focus();
    eingabe.oninput = () => { markiert = 0; zeichnen(eingabe.value); };
    eingabe.onkeydown = ev => {
      if (ev.key === 'ArrowDown') { markiert = Math.min(gefiltert.length - 1, markiert + 1); zeichnen(eingabe.value); ev.preventDefault(); }
      else if (ev.key === 'ArrowUp') { markiert = Math.max(0, markiert - 1); zeichnen(eingabe.value); ev.preventDefault(); }
      else if (ev.key === 'Enter') { waehlen(gefiltert[markiert]); ev.preventDefault(); }
      else if (ev.key === 'Escape') { schliessen(); knopf.focus(); ev.stopPropagation(); }
    };
    liste.querySelector('.wahl-eintraege').onclick = ev => {
      const b = ev.target.closest('[data-i]');
      if (b) waehlen(gefiltert[Number(b.dataset.i)]);
    };
    document.addEventListener('mousedown', aussen, true);
  };
  return wurzel;
}
