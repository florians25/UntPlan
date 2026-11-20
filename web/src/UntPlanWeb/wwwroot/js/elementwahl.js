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

/**
 * Elementwahl im Kopf des Wochenplans 1:1 wie im Original (Bilder 285–289):
 * Die Pille selbst ist das Suchfeld (Kürzel mit Info-Symbol; beim Fokus Rahmen und Textcursor, Tippen filtert die Liste,
 * ohne Treffer „Keine Ergebnisse“). Darunter ein schmales Menü mit dem Schalter „Alle anzeigen“ und der Liste der Kürzel
 * (gewähltes Element grau hinterlegt und beim Öffnen in Sicht, schmale Bildlaufleiste über der Liste).
 * „Alle anzeigen“ wird gemerkt und zeigt alle Elemente als Pillen in `raster` (unter dem Kopf, gewähltes Element dunkel).
 * eintraege: [{ kurz, name }]; wert: aktuelles Kürzel; beiWahl(kurz); raster: Element für die Pillen (optional).
 */
export function kopfWahl({ eintraege, wert, beiWahl, raster = null, bezeichnung = 'Element', info = x => (x?.name && x.name !== x.kurz ? x.name : '') }) {
  const norm = s => String(s ?? '').toLocaleLowerCase('de-DE');
  const aktuell = eintraege.find(x => norm(x.kurz) === norm(wert));
  const infoText = aktuell ? info(aktuell) : '';
  const id = `kw-${Math.random().toString(36).slice(2, 9)}`;
  const wurzel = document.createElement('div');
  wurzel.className = 'elementwahl kopfwahl';
  wurzel.innerHTML = `<div class="kw-pille">
      <span class="kw-wert"><span class="kw-kurz">${e(aktuell?.kurz ?? wert ?? '')}</span>${infoText ? `<span class="wahl-info" data-tip="${e(infoText)}"><img src="img/wu/exact/info-dropdown-dark.svg" alt=""></span>` : ''}</span>
      <input class="kw-eingabe" type="text" role="combobox" aria-expanded="false" aria-controls="${id}-liste" aria-autocomplete="list" autocomplete="off" spellcheck="false"
        aria-label="${e(`${bezeichnung} wählen: ${aktuell?.kurz ?? ''}${infoText ? ` – ${infoText}` : ''}`)}">
    </div>
    <div class="kw-menue" hidden>
      <label class="kw-alle"><span>Alle anzeigen</span><input type="checkbox" role="switch" data-alle-schalter></label>
      <div class="kw-liste" id="${id}-liste" role="listbox" aria-label="${e(bezeichnung)}"></div>
      <div class="kw-balken" aria-hidden="true"><i></i></div>
    </div>`;
  const eingabe = wurzel.querySelector('.kw-eingabe');
  const menue = wurzel.querySelector('.kw-menue');
  const liste = wurzel.querySelector('.kw-liste');
  const schalter = wurzel.querySelector('[data-alle-schalter]');
  const daumen = wurzel.querySelector('.kw-balken i');
  let sichtbar = [], aktiv = -1, offen = false;

  // „Alle anzeigen“ wie im Original gemerkt (gilt für alle Stundenpläne)
  const alleAn = () => { try { return localStorage.getItem('untplan:wahl-alle') === '1'; } catch { return false; } };
  schalter.checked = alleAn();
  const rasterZeichnen = () => {
    if (!raster) return;
    raster.hidden = !schalter.checked;
    raster.innerHTML = schalter.checked ? eintraege.map(x => `<button type="button" class="kw-chip${x === aktuell ? ' aktiv' : ''}" data-k="${e(x.kurz)}"${x === aktuell ? ' aria-current="true"' : ''}${info(x) ? ` title="${e(info(x))}"` : ''}>${e(x.kurz)}</button>`).join('') : '';
  };
  if (raster) raster.addEventListener('click', ev => { const b = ev.target.closest('[data-k]'); if (b) beiWahl(b.dataset.k); });
  rasterZeichnen();

  // schmale Bildlaufleiste über der Liste (wie im Original, nimmt keinen Platz weg)
  const balken = () => {
    const h = liste.clientHeight, s = liste.scrollHeight;
    daumen.parentElement.hidden = s <= h + 1;
    if (s <= h + 1) return;
    const dh = Math.max(24, h * h / s);
    daumen.style.height = `${dh}px`;
    daumen.style.transform = `translateY(${liste.offsetTop + (h - dh) * liste.scrollTop / (s - h)}px)`;
  };
  liste.addEventListener('scroll', balken, { passive: true });

  const sichtbarMachen = i => {
    const o = liste.querySelector(`[data-i="${i}"]`);
    if (!o) return;
    if (o.offsetTop < liste.scrollTop) liste.scrollTop = o.offsetTop;
    else if (o.offsetTop + o.offsetHeight > liste.scrollTop + liste.clientHeight) liste.scrollTop = o.offsetTop + o.offsetHeight - liste.clientHeight;
  };
  const zeichnen = () => {
    const f = norm(eingabe.value.trim());
    sichtbar = f ? eintraege.filter(x => norm(x.kurz).includes(f) || norm(x.name).includes(f)) : eintraege;
    liste.innerHTML = sichtbar.length
      ? sichtbar.map((x, i) => `<div class="kw-option${x === aktuell ? ' gewaehlt' : ''}${i === aktiv ? ' aktiv' : ''}" role="option" id="${id}-${i}" data-i="${i}" aria-selected="${x === aktuell}">${e(x.kurz)}</div>`).join('')
      : '<div class="kw-leer" role="presentation">Keine Ergebnisse</div>';
    eingabe.setAttribute('aria-activedescendant', aktiv >= 0 && sichtbar.length ? `${id}-${aktiv}` : '');
    balken();
  };
  const aktivSetzen = i => {
    if (!sichtbar.length) return;
    aktiv = Math.max(0, Math.min(sichtbar.length - 1, i));
    liste.querySelector('.kw-option.aktiv')?.classList.remove('aktiv');
    liste.querySelector(`[data-i="${aktiv}"]`)?.classList.add('aktiv');
    eingabe.setAttribute('aria-activedescendant', `${id}-${aktiv}`);
    sichtbarMachen(aktiv);
  };

  const oeffnen = () => {
    if (offen) return;
    offen = true;
    menue.hidden = false;
    wurzel.classList.add('offen');
    eingabe.setAttribute('aria-expanded', 'true');
    aktiv = -1;
    zeichnen();
    // beim Öffnen steht das gewählte Element in Sicht (unten, wenn es weiter hinten steht)
    sichtbarMachen(sichtbar.indexOf(aktuell));
    balken();
  };
  const schliessen = () => {
    if (!offen) return;
    offen = false;
    menue.hidden = true;
    wurzel.classList.remove('offen', 'tippt');
    eingabe.value = '';
    eingabe.setAttribute('aria-expanded', 'false');
    eingabe.removeAttribute('aria-activedescendant');
  };
  const waehlen = i => { const x = sichtbar[i]; if (!x) return; schliessen(); eingabe.blur(); beiWahl(x.kurz); };

  eingabe.addEventListener('mousedown', () => oeffnen());
  // Klick irgendwo auf die Pille (auch neben das schmale Eingabefeld): Fokus ins Feld und Menü öffnen
  wurzel.querySelector('.kw-pille').addEventListener('mousedown', ev => {
    if (ev.target === eingabe || ev.button !== 0) return;
    ev.preventDefault();
    eingabe.focus({ preventScroll: true });
    oeffnen();
  });
  eingabe.addEventListener('input', () => {
    wurzel.classList.toggle('tippt', eingabe.value !== '');
    if (!offen) oeffnen();
    aktiv = eingabe.value.trim() ? 0 : -1;
    liste.scrollTop = 0;
    zeichnen();
  });
  eingabe.addEventListener('keydown', ev => {
    const k = ev.key;
    if (k === 'ArrowDown' || k === 'ArrowUp') {
      ev.preventDefault();
      if (!offen) { oeffnen(); return; }
      const start = aktiv >= 0 ? aktiv : Math.max(-1, sichtbar.indexOf(aktuell));
      aktivSetzen(start + (k === 'ArrowDown' ? 1 : -1));
    } else if ((k === 'PageDown' || k === 'PageUp') && offen) { ev.preventDefault(); aktivSetzen((aktiv >= 0 ? aktiv : sichtbar.indexOf(aktuell)) + (k === 'PageDown' ? 10 : -10)); }
    else if (k === 'Enter') { ev.preventDefault(); if (!offen) oeffnen(); else if (aktiv >= 0) waehlen(aktiv); else if (sichtbar.length === 1) waehlen(0); }
    else if (k === 'Escape' && offen) { ev.preventDefault(); ev.stopPropagation(); schliessen(); }
    else if (k === 'Tab') schliessen();
  });
  // Klick außerhalb: das Feld verliert den Fokus und das Menü schließt (Klicks im Menü behalten den Fokus)
  eingabe.addEventListener('blur', ev => { if (!menue.contains(ev.relatedTarget)) schliessen(); });
  menue.addEventListener('mousedown', ev => ev.preventDefault());
  liste.addEventListener('click', ev => { const o = ev.target.closest('.kw-option'); if (o) waehlen(Number(o.dataset.i)); });
  liste.addEventListener('mousemove', ev => { const o = ev.target.closest('.kw-option'); if (o && Number(o.dataset.i) !== aktiv) { aktiv = Number(o.dataset.i); liste.querySelector('.kw-option.aktiv')?.classList.remove('aktiv'); o.classList.add('aktiv'); } });
  schalter.addEventListener('change', () => {
    try { localStorage.setItem('untplan:wahl-alle', schalter.checked ? '1' : '0'); } catch { /* privat */ }
    rasterZeichnen();
    eingabe.focus({ preventScroll: true });
  });
  return wurzel;
}
