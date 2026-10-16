// Elementwahl: Auswahlknopf mit aufklappender Suchliste (Kürzel und Langname, Tastatursteuerung).
import { e } from './ui.js';

/** eintraege: [{ kurz, name }]; wert: aktuelles Kürzel; beiWahl(kurz). Liefert ein DOM-Element. */
export function elementWahl({ eintraege, wert, platzhalter = 'Auswählen …', beiWahl, info = x => (x?.name && x.name !== x.kurz ? x.name : '') }) {
  const wurzel = document.createElement('div');
  wurzel.className = 'position-relative elementwahl';
  const aktuell = eintraege.find(x => x.kurz.toLowerCase() === String(wert ?? '').toLowerCase());
  // Knopf wie im Original: Kürzel mit Info-Symbol; beim Zeigen auf das Symbol erscheint z. B. „Klassenlehrkraft: KM“
  const infoText = aktuell ? info(aktuell) : '';
  wurzel.innerHTML = `<button class="btn btn-outline-secondary bg-white text-body dropdown-toggle wahl-knopf" type="button" aria-expanded="false" aria-label="${e(aktuell ? `${aktuell.kurz}${infoText ? ' – ' + infoText : ''}` : platzhalter)}">
      ${aktuell ? `<span>${e(aktuell.kurz)}</span>${infoText ? `<span class="wahl-info" data-tip="${e(infoText)}"><img src="img/wu/exact/info-dropdown-dark.svg" alt=""></span>` : ''}` : `<span class="text-body-secondary">${e(platzhalter)}</span>`}
    </button>
    <div class="wahl-menue bg-white border rounded shadow">
      <div class="p-2 border-bottom"><input type="search" class="form-control form-control-sm" placeholder="Suchen" aria-label="Suchen"></div>
      <label class="wahl-alle-schalter"><span>Alle anzeigen</span><input type="checkbox" role="switch" data-alle-schalter></label>
      <div class="liste list-group list-group-flush" role="listbox"></div>
    </div>
    <div class="wahl-alle" role="listbox" aria-label="Alle Elemente"></div>`;
  const knopf = wurzel.querySelector('button');
  const menue = wurzel.querySelector('.wahl-menue');
  const eingabe = menue.querySelector('input');
  const liste = menue.querySelector('.liste');
  const alle = wurzel.querySelector('.wahl-alle');
  const schalter = menue.querySelector('[data-alle-schalter]');
  let markiert = 0, gefiltert = [];
  try { schalter.checked = localStorage.getItem('untplan:wahl-alle') === '1'; } catch { /* privat */ }
  // „Alle anzeigen“: alle Elemente als Pillen in einem breiten Feld neben der Liste (schnelle Wahl ohne Scrollen)
  const alleZeichnen = () => {
    if (!schalter.checked || !offen()) { alle.classList.remove('show'); return; }
    const f = eingabe.value.trim().toLowerCase();
    const l = !f ? eintraege : eintraege.filter(x => (x.kurz + ' ' + (x.name ?? '')).toLowerCase().includes(f));
    alle.innerHTML = l.map(x => `<button type="button" class="wahl-pille ${x === aktuell ? 'aktiv' : ''}" data-k="${e(x.kurz)}" title="${e(info(x) || x.name || x.kurz)}">${e(x.kurz)}</button>`).join('');
    alle.classList.add('show');
    const m = menue.getBoundingClientRect(), w = wurzel.getBoundingClientRect();
    alle.style.left = `${m.right - w.left + 8}px`;
    alle.style.width = `${Math.max(280, window.innerWidth - m.right - 24)}px`;
  };
  schalter.onchange = () => { try { localStorage.setItem('untplan:wahl-alle', schalter.checked ? '1' : '0'); } catch { /* privat */ } alleZeichnen(); eingabe.focus(); };
  alle.onclick = ev => { const b = ev.target.closest('[data-k]'); if (b) waehlen(eintraege.find(x => x.kurz === b.dataset.k)); };

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
  const schliessen = () => { menue.classList.remove('show'); alle.classList.remove('show'); knopf.setAttribute('aria-expanded', 'false'); document.removeEventListener('mousedown', aussen, true); };
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
    alleZeichnen();
    eingabe.focus();
    document.addEventListener('mousedown', aussen, true);
  };
  eingabe.oninput = () => { markiert = 0; zeichnen(); alleZeichnen(); };
  eingabe.onkeydown = ev => {
    if (ev.key === 'ArrowDown') { markiert = Math.min(gefiltert.length - 1, markiert + 1); zeichnen(); ev.preventDefault(); }
    else if (ev.key === 'ArrowUp') { markiert = Math.max(0, markiert - 1); zeichnen(); ev.preventDefault(); }
    else if (ev.key === 'Enter') { waehlen(gefiltert[markiert]); ev.preventDefault(); }
    else if (ev.key === 'Escape') { schliessen(); knopf.focus(); }
  };
  liste.onclick = ev => { const b = ev.target.closest('[data-i]'); if (b) waehlen(gefiltert[Number(b.dataset.i)]); };
  return wurzel;
}
