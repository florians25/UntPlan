// Elementwahl (Planwahl): Auswahlknopf mit dem einheitlichen Auswahlmenü (Suche, Kürzel mit Langname als zweite Zeile, Tastatur)
// und dem Schalter „Alle anzeigen“ (alle Elemente als Pillen neben der Liste für die schnelle Wahl ohne Scrollen).
import { e } from './ui.js';
import { menueOeffnen, menueSchliessen } from './auswahl.js';

/** eintraege: [{ kurz, name }]; wert: aktuelles Kürzel; beiWahl(kurz). Liefert ein DOM-Element. */
export function elementWahl({ eintraege, wert, platzhalter = 'Auswählen …', beiWahl, info = x => (x?.name && x.name !== x.kurz ? x.name : '') }) {
  const wurzel = document.createElement('div');
  wurzel.className = 'position-relative elementwahl';
  const aktuell = eintraege.find(x => x.kurz.toLowerCase() === String(wert ?? '').toLowerCase());
  // Knopf wie im Original: Kürzel mit Info-Symbol; beim Zeigen auf das Symbol erscheint z. B. „Klassenlehrkraft: KM“
  const infoText = aktuell ? info(aktuell) : '';
  wurzel.innerHTML = `<button class="btn btn-outline-secondary bg-white text-body dropdown-toggle wahl-knopf" type="button" aria-haspopup="listbox" aria-expanded="false" aria-label="${e(aktuell ? `${aktuell.kurz}${infoText ? ' – ' + infoText : ''}` : platzhalter)}">
      ${aktuell ? `<span>${e(aktuell.kurz)}</span>${infoText ? `<span class="wahl-info" data-tip="${e(infoText)}"><img src="img/wu/exact/info-dropdown-dark.svg" alt=""></span>` : ''}` : `<span class="text-body-secondary">${e(platzhalter)}</span>`}
    </button>`;
  const knopf = wurzel.querySelector('button');
  const liste = eintraege.map(x => ({ wert: x.kurz, text: x.kurz, zweit: x.name && x.name !== x.kurz ? x.name : '', element: x }));
  let offen = false;

  const oeffnen = suche => {
    let schalter, pillen, filter = suche ?? '';
    offen = true;
    knopf.setAttribute('aria-expanded', 'true');
    // „Alle anzeigen“: alle Elemente als Pillen in einem breiten Feld neben dem Menü
    const pillenZeichnen = st => {
      pillen?.remove();
      pillen = null;
      if (!schalter.checked) return;
      const f = filter.trim().toLowerCase();
      const l = !f ? eintraege : eintraege.filter(x => (x.kurz + ' ' + (x.name ?? '')).toLowerCase().includes(f));
      pillen = document.createElement('div');
      pillen.className = 'au-pillen';
      pillen.setAttribute('role', 'listbox');
      pillen.setAttribute('aria-label', 'Alle Elemente');
      pillen.innerHTML = l.map(x => `<button type="button" class="wahl-pille ${x === aktuell ? 'aktiv' : ''}" data-k="${e(x.kurz)}" title="${e(info(x) || x.name || x.kurz)}">${e(x.kurz)}</button>`).join('')
        || '<span class="small text-body-secondary px-2">Kein Treffer</span>';
      pillen.addEventListener('mousedown', ev => ev.preventDefault());
      pillen.addEventListener('click', ev => { const b = ev.target.closest('[data-k]'); if (b) { st.schliessen(); beiWahl(b.dataset.k); } });
      // im Menü (absolut daneben), damit Klicks nicht als „außerhalb“ gelten
      st.menue.append(pillen);
      const m = st.menue.getBoundingClientRect();
      pillen.style.width = `${Math.max(280, window.innerWidth - m.right - 24)}px`;
    };
    const st = menueOeffnen(knopf, liste, {
      gewaehlt: aktuell ? aktuell.kurz : null, suche, platzhalter: 'Suchen', klasse: 'au-elementwahl', breite: 300,
      zusatz: (box, st) => {
        box.innerHTML = '<label class="wahl-alle-schalter"><span>Alle anzeigen</span><input type="checkbox" role="switch" data-alle-schalter></label>';
        schalter = box.querySelector('input');
        try { schalter.checked = localStorage.getItem('untplan:wahl-alle') === '1'; } catch { /* privat */ }
        schalter.onchange = () => { try { localStorage.setItem('untplan:wahl-alle', schalter.checked ? '1' : '0'); } catch { /* privat */ } pillenZeichnen(st); st.suchfeld.focus(); };
        requestAnimationFrame(() => pillenZeichnen(st));
      },
      beiFilter: t => { filter = t; pillenZeichnen(st); },
      beiWahl: x => beiWahl(x.wert),
      beiSchliessen: () => { offen = false; pillen?.remove(); knopf.setAttribute('aria-expanded', 'false'); },
    });
  };
  knopf.addEventListener('click', () => (offen ? menueSchliessen() : oeffnen('')));
  knopf.addEventListener('keydown', ev => {
    if (offen || ev.ctrlKey || ev.metaKey || ev.altKey) return;
    if (['ArrowDown', 'Enter', ' '].includes(ev.key)) { ev.preventDefault(); oeffnen(''); }
    else if (ev.key.length === 1 && /\S/.test(ev.key)) { ev.preventDefault(); oeffnen(ev.key); }
  });
  return wurzel;
}
