// Einheitliche Auswahl (Dropdown) im neuen Design: aufklappendes Menü mit immer integrierter Suche (Suchfeld oben, Tastatur ↑ ↓ Pos1 Ende
// Enter Esc, Typeahead), Gruppen, Langname als zweite Zeile, Treffer hervorgehoben, Meldung bei leeren Treffern, optional Mehrfachauswahl
// mit Chips. Progressive Enhancement: Jedes native <select> bleibt als verstecktes Element für Werte, Formulare und bestehende
// change-Handler erhalten; die Komponente legt sich darüber und hält beide Seiten synchron (auch bei sel.value = …).
// Außerdem: Suchfelder (input[type=search]) einheitlich mit Lupe und Löschen-X.

const esc = s => String(s ?? '').replace(/[&<>"']/g, z => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[z]));
/** Vergleichstext: klein, ohne Akzente (é → e), Umlaute bleiben (ä findet ä und a). */
const norm = s => String(s ?? '').toLocaleLowerCase('de-DE').normalize('NFD').replace(/[̀-ͯ]/g, '');
const MAX_ZEILEN = 400;

/** Treffer im Text hervorheben (alle Suchbegriffe, ohne Groß-/Kleinschreibung). */
function markieren(text, begriffe) {
  const t = String(text ?? '');
  if (!begriffe.length) return esc(t);
  const n = norm(t);
  const marken = new Array(t.length).fill(false);
  for (const b of begriffe) {
    let i = n.indexOf(b);
    while (b && i >= 0) { for (let k = i; k < i + b.length && k < t.length; k++) marken[k] = true; i = n.indexOf(b, i + b.length); }
  }
  let html = '', offen = false;
  for (let i = 0; i < t.length; i++) {
    if (marken[i] && !offen) { html += '<mark>'; offen = true; }
    if (!marken[i] && offen) { html += '</mark>'; offen = false; }
    html += esc(t[i]);
  }
  return html + (offen ? '</mark>' : '');
}

// ------------------------------------------------------------------ Menü (gemeinsam für Select-Ersatz, Elementwahl und eigene Auswahlen)

let offenesMenue = null;

/** Das gerade offene Menü schließen (z. B. vor dem Navigieren). */
export function menueSchliessen() { offenesMenue?.schliessen(); }

/**
 * Menü unter `anker` öffnen.
 * eintraege: [{ wert, text, zweit?, gruppe?, deaktiviert?, suchtext? }]
 * optionen: { gewaehlt: Set der Werte, mehrfach, beiWahl(eintrag, an), beiSchliessen(), suche: Starttext, platzhalter, leerText,
 *             zusatz(menueEl, steuerung) → zusätzlicher Inhalt unter der Suche, beiFilter(text), breite, klasse }
 * Liefert die Steuerung { schliessen, zeichnen, menue, suchfeld }.
 */
export function menueOeffnen(anker, eintraege, optionen = {}) {
  offenesMenue?.schliessen();
  const { mehrfach = false, platzhalter = 'Suchen …', leerText = 'Keine Treffer' } = optionen;
  const gewaehlt = optionen.gewaehlt instanceof Set ? optionen.gewaehlt : new Set(optionen.gewaehlt != null ? [String(optionen.gewaehlt)] : []);
  const behaelter = anker.closest('.modal') ?? anker.closest('dialog') ?? document.body;
  const menue = document.createElement('div');
  menue.className = `au-menue ${optionen.klasse ?? ''}`;
  menue.setAttribute('role', 'dialog');
  menue.innerHTML = `<div class="au-suche"><i class="bi bi-search" aria-hidden="true"></i>
      <input type="search" data-nativ autocomplete="off" spellcheck="false" placeholder="${esc(platzhalter)}" aria-label="${esc(platzhalter)}" role="combobox" aria-expanded="true" aria-autocomplete="list">
      <button type="button" class="au-x" tabindex="-1" aria-label="Suche löschen" hidden><i class="bi bi-x-lg" aria-hidden="true"></i></button></div>
    <div class="au-zusatz"></div>
    <div class="au-liste" role="listbox" ${mehrfach ? 'aria-multiselectable="true"' : ''}></div>`;
  const eingabe = menue.querySelector('input');
  const x = menue.querySelector('.au-x');
  const liste = menue.querySelector('.au-liste');
  const listId = `au-liste-${Math.random().toString(36).slice(2, 9)}`;
  liste.id = listId;
  eingabe.setAttribute('aria-controls', listId);
  let sichtbar = [];
  let aktiv = -1;

  const filtern = () => {
    const begriffe = norm(eingabe.value).trim().split(/\s+/).filter(Boolean);
    const treffer = eintraege.filter(e => !begriffe.length || begriffe.every(b => norm(`${e.text} ${e.zweit ?? ''} ${e.gruppe ?? ''} ${e.suchtext ?? ''}`).includes(b)));
    // ohne Gruppen: Anfangstreffer zuerst (Typeahead)
    if (begriffe.length && !eintraege.some(e => e.gruppe)) {
      const vorn = treffer.filter(e => norm(e.text).startsWith(begriffe[0]));
      return { begriffe, liste: [...vorn, ...treffer.filter(e => !vorn.includes(e))] };
    }
    return { begriffe, liste: treffer };
  };

  const zeichnen = (aktivAufGewaehlt = false) => {
    const { begriffe, liste: treffer } = filtern();
    sichtbar = treffer.slice(0, MAX_ZEILEN);
    x.hidden = !eingabe.value;
    if (aktivAufGewaehlt) aktiv = sichtbar.findIndex(e => gewaehlt.has(String(e.wert)));
    if (aktiv < 0 || aktiv >= sichtbar.length || sichtbar[aktiv]?.deaktiviert) aktiv = sichtbar.findIndex(e => !e.deaktiviert);
    let gruppe = null, html = '';
    sichtbar.forEach((e, i) => {
      if ((e.gruppe ?? null) !== gruppe) { gruppe = e.gruppe ?? null; if (gruppe) html += `<div class="au-gruppe" role="presentation">${esc(gruppe)}</div>`; }
      const an = gewaehlt.has(String(e.wert));
      html += `<div class="au-option ${i === aktiv ? 'aktiv' : ''} ${an ? 'gewaehlt' : ''} ${e.deaktiviert ? 'deaktiviert' : ''}" role="option" id="${listId}-${i}" aria-selected="${an}" ${e.deaktiviert ? 'aria-disabled="true"' : ''} data-i="${i}">
        ${mehrfach ? '<span class="au-kasten" aria-hidden="true"></span>' : ''}
        <span class="au-text"><span class="au-haupt">${markieren(e.text, begriffe) || '&nbsp;'}</span>${e.zweit ? `<span class="au-zweit">${markieren(e.zweit, begriffe)}</span>` : ''}</span>
        ${!mehrfach && an ? '<i class="bi bi-check-lg au-haken" aria-hidden="true"></i>' : ''}</div>`;
    });
    if (!sichtbar.length) html = `<div class="au-leer"><i class="bi bi-search" aria-hidden="true"></i>${esc(leerText)}${eingabe.value ? ` für „${esc(eingabe.value)}“` : ''}</div>`;
    else if (treffer.length > sichtbar.length) html += `<div class="au-mehr">${treffer.length - sichtbar.length} weitere – bitte die Suche verfeinern</div>`;
    liste.innerHTML = html;
    eingabe.setAttribute('aria-activedescendant', aktiv >= 0 ? `${listId}-${aktiv}` : '');
    sichtbarMachen();
  };
  const sichtbarMachen = () => liste.querySelector('.au-option.aktiv')?.scrollIntoView({ block: 'nearest' });
  const aktivSetzen = i => {
    if (!sichtbar.length) return;
    i = Math.max(0, Math.min(sichtbar.length - 1, i));
    liste.querySelector('.au-option.aktiv')?.classList.remove('aktiv');
    aktiv = i;
    liste.querySelector(`[data-i="${i}"]`)?.classList.add('aktiv');
    eingabe.setAttribute('aria-activedescendant', `${listId}-${i}`);
    sichtbarMachen();
  };
  const schritt = (von, r) => { let i = von; do { i += r; } while (i >= 0 && i < sichtbar.length && sichtbar[i].deaktiviert); return i >= 0 && i < sichtbar.length ? i : von; };

  const waehlen = i => {
    const e = sichtbar[i];
    if (!e || e.deaktiviert) return;
    const w = String(e.wert);
    if (mehrfach) {
      const an = !gewaehlt.has(w);
      if (an) gewaehlt.add(w); else gewaehlt.delete(w);
      optionen.beiWahl?.(e, an);
      aktiv = i;
      zeichnen();
      eingabe.focus();
    } else {
      gewaehlt.clear(); gewaehlt.add(w);
      schliessen(true);
      optionen.beiWahl?.(e, true);
    }
  };

  const platzieren = () => {
    if (!anker.isConnected) return schliessen();
    const r = anker.getBoundingClientRect();
    if (r.bottom < 0 || r.top > window.innerHeight) return schliessen();
    const vw = document.documentElement.clientWidth, vh = window.innerHeight;
    const breite = Math.min(Math.max(r.width, optionen.breite ?? 240), vw - 16);
    menue.style.minWidth = `${breite}px`;
    menue.style.maxWidth = `${Math.max(breite, Math.min(480, vw - 16))}px`;
    const mw = menue.offsetWidth;
    menue.style.left = `${Math.max(8, Math.min(r.left, vw - 8 - mw))}px`;
    // öffnet nach unten; nur wenn unten kaum Platz ist, nach oben
    const unten = vh - r.bottom - 12, oben = r.top - 12;
    const nachOben = unten < 220 && oben > unten;
    const hoehe = Math.max(140, Math.min(380, (nachOben ? oben : unten) - 60));
    liste.style.maxHeight = `${hoehe}px`;
    menue.classList.toggle('oben', nachOben);
    if (nachOben) { menue.style.top = ''; menue.style.bottom = `${vh - r.top + 4}px`; }
    else { menue.style.bottom = ''; menue.style.top = `${r.bottom + 4}px`; }
  };

  const aussen = ev => { if (!menue.contains(ev.target) && !anker.contains(ev.target)) schliessen(); };
  const beimScrollen = ev => { if (!menue.contains(ev.target)) requestAnimationFrame(platzieren); };
  let zu = false;
  function schliessen(fokusZurueck = false) {
    if (zu) return;
    zu = true;
    document.removeEventListener('mousedown', aussen, true);
    document.removeEventListener('touchstart', aussen, true);
    window.removeEventListener('scroll', beimScrollen, true);
    window.removeEventListener('resize', platzieren);
    menue.remove();
    if (offenesMenue === steuerung) offenesMenue = null;
    optionen.beiSchliessen?.();
    if (fokusZurueck) anker.focus?.({ preventScroll: true });
  }

  eingabe.addEventListener('input', () => { aktiv = 0; zeichnen(); optionen.beiFilter?.(eingabe.value); });
  x.addEventListener('click', () => { eingabe.value = ''; aktiv = 0; zeichnen(true); optionen.beiFilter?.(''); eingabe.focus(); });
  eingabe.addEventListener('keydown', ev => {
    const k = ev.key;
    if (k === 'ArrowDown') { aktivSetzen(schritt(aktiv, 1)); ev.preventDefault(); }
    else if (k === 'ArrowUp') { aktivSetzen(schritt(aktiv, -1)); ev.preventDefault(); }
    else if (k === 'PageDown') { aktivSetzen(aktiv + 8); ev.preventDefault(); }
    else if (k === 'PageUp') { aktivSetzen(aktiv - 8); ev.preventDefault(); }
    else if ((k === 'Home' || k === 'End') && (ev.ctrlKey || !eingabe.value)) { aktivSetzen(k === 'Home' ? 0 : sichtbar.length - 1); ev.preventDefault(); }
    else if (k === 'Enter') { ev.preventDefault(); ev.stopPropagation(); if (aktiv >= 0) waehlen(aktiv); }
    else if (k === 'Escape') { ev.preventDefault(); ev.stopPropagation(); if (eingabe.value && mehrfach) { eingabe.value = ''; zeichnen(); } else schliessen(true); }
    else if (k === 'Tab') schliessen();
  });
  // Klick wählt; mousedown verhindert, dass das Suchfeld den Fokus verliert
  liste.addEventListener('mousedown', ev => ev.preventDefault());
  liste.addEventListener('click', ev => { const o = ev.target.closest('.au-option'); if (o) waehlen(Number(o.dataset.i)); });
  liste.addEventListener('mousemove', ev => { const o = ev.target.closest('.au-option:not(.deaktiviert)'); if (o && Number(o.dataset.i) !== aktiv) aktivSetzen(Number(o.dataset.i)); });

  const steuerung = { schliessen, zeichnen, menue, suchfeld: eingabe, platzieren, gewaehlt };
  optionen.zusatz?.(menue.querySelector('.au-zusatz'), steuerung);
  behaelter.append(menue);
  offenesMenue = steuerung;
  if (optionen.suche) eingabe.value = optionen.suche;
  zeichnen(!optionen.suche);
  platzieren();
  eingabe.focus({ preventScroll: true });
  document.addEventListener('mousedown', aussen, true);
  document.addEventListener('touchstart', aussen, true);
  window.addEventListener('scroll', beimScrollen, true);
  window.addEventListener('resize', platzieren);
  return steuerung;
}

/** Tasten, die eine geschlossene Auswahl öffnen; druckbare Zeichen starten die Suche (Typeahead). */
function oeffnenPerTaste(ev, oeffnen) {
  if (ev.altKey && ev.key === 'ArrowDown') { ev.preventDefault(); oeffnen(''); return; }
  if (ev.ctrlKey || ev.metaKey || ev.altKey) return;
  if (['ArrowDown', 'ArrowUp', 'Enter', ' ', 'F4'].includes(ev.key)) { ev.preventDefault(); oeffnen(''); }
  else if (ev.key.length === 1 && /\S/.test(ev.key)) { ev.preventDefault(); oeffnen(ev.key); }
}

// ------------------------------------------------------------------ Eigenständige Auswahl (ohne natives Select)

/**
 * Auswahlknopf mit Menü erzeugen.
 * { eintraege, wert (bzw. Array bei mehrfach), mehrfach, platzhalter, suchePlatzhalter, leerText, beiWahl(wert | werte), klasse, klein }
 * Liefert das Element; el.wert liefert/setzt den Wert, el.eintraegeSetzen(liste) tauscht die Einträge.
 */
export function auswahl({ eintraege = [], wert = null, mehrfach = false, platzhalter = 'Auswählen …', suchePlatzhalter, leerText, beiWahl, klasse = '', klein = false, label } = {}) {
  const wurzel = document.createElement('div');
  wurzel.className = `au ${klein ? 'au-sm' : ''} ${klasse}`;
  wurzel.innerHTML = `<button type="button" class="au-knopf" aria-haspopup="listbox" aria-expanded="false" ${label ? `aria-label="${esc(label)}"` : ''}><span class="au-wert"></span><i class="bi bi-chevron-down au-pfeil" aria-hidden="true"></i></button>`;
  const knopf = wurzel.firstElementChild;
  let liste = eintraege;
  let werte = new Set(mehrfach ? (wert ?? []).map(String) : wert != null && wert !== '' ? [String(wert)] : []);
  const anzeigen = () => anzeigeSetzen(knopf, liste, werte, mehrfach, platzhalter, w => { werte.delete(w); anzeigen(); beiWahl?.([...werte]); });
  const oeffnen = suche => {
    if (knopf.disabled) return;
    knopf.setAttribute('aria-expanded', 'true');
    wurzel.classList.add('offen');
    menueOeffnen(knopf, liste, {
      gewaehlt: werte, mehrfach, suche, platzhalter: suchePlatzhalter, leerText,
      beiWahl: () => { anzeigen(); beiWahl?.(mehrfach ? [...werte] : [...werte][0] ?? null); },
      beiSchliessen: () => { knopf.setAttribute('aria-expanded', 'false'); wurzel.classList.remove('offen'); },
    });
  };
  knopf.addEventListener('click', () => (wurzel.classList.contains('offen') ? menueSchliessen() : oeffnen('')));
  knopf.addEventListener('keydown', ev => { if (!wurzel.classList.contains('offen')) oeffnenPerTaste(ev, oeffnen); });
  Object.defineProperty(wurzel, 'wert', {
    get: () => (mehrfach ? [...werte] : [...werte][0] ?? null),
    set: v => { werte = new Set(mehrfach ? (v ?? []).map(String) : v != null && v !== '' ? [String(v)] : []); anzeigen(); },
  });
  wurzel.eintraegeSetzen = l => { liste = l; anzeigen(); };
  wurzel.deaktivieren = an => { knopf.disabled = !!an; };
  anzeigen();
  return wurzel;
}

/** Anzeige im Knopf: Text (bzw. Platzhalter) oder Chips bei Mehrfachauswahl. */
function anzeigeSetzen(knopf, eintraege, werte, mehrfach, platzhalter, entfernen) {
  const ziel = knopf.querySelector('.au-wert');
  const gew = eintraege.filter(e => werte.has(String(e.wert)));
  knopf.classList.toggle('leer', !gew.length);
  if (mehrfach) {
    if (!gew.length) { ziel.innerHTML = `<span class="au-platzhalter">${esc(platzhalter)}</span>`; return; }
    const zeigen = gew.slice(0, 3);
    ziel.innerHTML = zeigen.map(e => `<span class="au-chip">${esc(e.text)}<span class="au-chip-x" role="button" tabindex="-1" aria-label="${esc(e.text)} entfernen" data-weg="${esc(e.wert)}">×</span></span>`).join('')
      + (gew.length > zeigen.length ? `<span class="au-chip mehr">+${gew.length - zeigen.length}</span>` : '');
    ziel.querySelectorAll('[data-weg]').forEach(x => x.addEventListener('click', ev => { ev.stopPropagation(); entfernen(x.dataset.weg); }));
    return;
  }
  const e = gew[0];
  ziel.innerHTML = e ? `<span class="au-wert-text">${esc(e.text)}</span>` : `<span class="au-platzhalter">${esc(platzhalter)}</span>`;
  knopf.title = e?.zweit ? `${e.text} – ${e.zweit}` : '';
}

// ------------------------------------------------------------------ Natives <select> aufwerten

const PROTO = HTMLSelectElement.prototype;
const WERT = Object.getOwnPropertyDescriptor(PROTO, 'value');
const INDEX = Object.getOwnPropertyDescriptor(PROTO, 'selectedIndex');
/** Klassen des Selects, die nicht auf die Hülle übertragen werden. */
const NUR_SELECT = new Set(['form-select', 'form-select-sm', 'form-select-lg', 'form-control', 'form-control-sm', 'au-nativ']);

/** Einträge aus den Optionen eines Selects (Gruppe = optgroup, Zweitzeile = data-zweit bzw. title). */
function eintraegeAus(sel) {
  return [...sel.options].filter(o => !o.hidden).map(o => {
    const g = o.parentElement?.tagName === 'OPTGROUP' ? o.parentElement : null;
    const zweit = o.dataset.zweit ?? (o.title && o.title !== o.text ? o.title : '');
    return { wert: o.value, text: o.text, zweit, gruppe: g?.label ?? null, deaktiviert: o.disabled || !!g?.disabled, suchtext: o.dataset.suche ?? '', option: o };
  });
}

/** Ein natives Select durch die Auswahl ersetzen (das Select bleibt versteckt erhalten). */
export function selectAufwerten(sel) {
  if (sel.au || sel.hasAttribute('data-nativ') || sel.closest('[data-nativ-auswahl], .au-menue')) return sel.au;
  const mehrfach = sel.multiple;
  const wurzel = document.createElement('div');
  wurzel.innerHTML = `<button type="button" class="au-knopf" aria-haspopup="listbox" aria-expanded="false"><span class="au-wert"></span><i class="bi bi-chevron-down au-pfeil" aria-hidden="true"></i></button>`;
  const knopf = wurzel.firstElementChild;
  const leerOption = () => [...sel.options].find(o => o.value === '');

  const klassenUebernehmen = () => {
    const eigene = [...sel.classList].filter(k => !NUR_SELECT.has(k));
    wurzel.className = ['au', sel.classList.contains('form-select-sm') || sel.closest('.btn-group-sm, .input-group-sm') ? 'au-sm' : '', mehrfach ? 'au-mehrfach' : '',
      wurzel.classList.contains('offen') ? 'offen' : '', ...eigene].filter(Boolean).join(' ');
    for (const p of ['width', 'minWidth', 'maxWidth', 'flex', 'flexBasis', 'display', 'marginLeft', 'marginRight']) wurzel.style[p] = sel.style[p];
    wurzel.hidden = sel.hidden;
    knopf.disabled = sel.disabled;
    const label = sel.getAttribute('aria-label') || sel.labels?.[0]?.innerText?.trim() || sel.title || '';
    if (label) knopf.setAttribute('aria-label', label); else knopf.removeAttribute('aria-label');
  };
  const aktualisieren = () => {
    const eintraege = eintraegeAus(sel);
    const werte = new Set([...sel.options].filter(o => o.selected).map(o => o.value));
    // Leerwert-Option („Alle Rollen“, „Klasse“) als Platzhalter anzeigen
    const leer = leerOption();
    const platzhalter = leer?.text || sel.getAttribute('placeholder') || sel.dataset.platzhalter || 'Auswählen …';
    anzeigeSetzen(knopf, mehrfach ? eintraege.filter(e => e.wert !== '') : eintraege.filter(e => !(e.wert === '' && werte.has('') && leer)), werte, mehrfach, platzhalter,
      w => { const o = [...sel.options].find(x => x.value === w); if (o) { o.selected = false; melden(); } });
    // ausgewählte Leerwert-Option mit Text: Text grau als Platzhalter
    knopf.classList.toggle('leer', mehrfach ? !werte.size : !sel.value);
  };
  const melden = () => {
    aktualisieren();
    sel.dispatchEvent(new Event('input', { bubbles: true }));
    sel.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const oeffnen = suche => {
    if (sel.disabled) return;
    const eintraege = eintraegeAus(sel).filter(e => !(mehrfach && e.wert === ''));
    const werte = new Set([...sel.options].filter(o => o.selected).map(o => o.value));
    knopf.setAttribute('aria-expanded', 'true');
    wurzel.classList.add('offen');
    menueOeffnen(knopf, eintraege, {
      gewaehlt: werte, mehrfach, suche, platzhalter: sel.dataset.suche || 'Suchen …',
      breite: Number(sel.dataset.breite) || undefined,
      beiWahl: (e, an) => {
        if (mehrfach) e.option.selected = an;
        else { if (sel.value === e.wert && e.option.selected) { aktualisieren(); return; } WERT.set.call(sel, e.wert); }
        melden();
      },
      beiSchliessen: () => { knopf.setAttribute('aria-expanded', 'false'); wurzel.classList.remove('offen'); },
    });
  };

  knopf.addEventListener('click', () => (wurzel.classList.contains('offen') ? menueSchliessen() : oeffnen('')));
  knopf.addEventListener('keydown', ev => { if (!wurzel.classList.contains('offen')) oeffnenPerTaste(ev, oeffnen); });
  // Label-Klicks und focus() auf das versteckte Select landen bei der Auswahl
  sel.addEventListener('focus', () => knopf.focus());
  sel.addEventListener('click', () => { if (!wurzel.classList.contains('offen')) oeffnen(''); });
  sel.addEventListener('change', aktualisieren);
  sel.form?.addEventListener('reset', () => setTimeout(aktualisieren));
  // Programmgesteuerte Änderungen (sel.value = …, sel.selectedIndex = …) sofort anzeigen
  Object.defineProperty(sel, 'value', { configurable: true, get() { return WERT.get.call(this); }, set(v) { WERT.set.call(this, v); aktualisieren(); } });
  Object.defineProperty(sel, 'selectedIndex', { configurable: true, get() { return INDEX.get.call(this); }, set(v) { INDEX.set.call(this, v); aktualisieren(); } });
  new MutationObserver(m => { if (m.some(x => x.type === 'attributes' && x.target === sel)) klassenUebernehmen(); aktualisieren(); })
    .observe(sel, { childList: true, subtree: true, attributes: true, characterData: true, attributeFilter: ['selected', 'disabled', 'hidden', 'class', 'style', 'label', 'aria-label'] });

  sel.classList.add('au-nativ');
  sel.tabIndex = -1;
  sel.setAttribute('aria-hidden', 'true');
  sel.after(wurzel);
  klassenUebernehmen();
  aktualisieren();
  sel.au = { wurzel, knopf, aktualisieren, oeffnen };
  return sel.au;
}

// ------------------------------------------------------------------ Suchfelder: Lupe und Löschen-X

const LAYOUT = /^(m[sebtxy]?-|w-|mw-|flex-|order-|col|align-self)/;

/** Suchfeld vereinheitlichen: Lupe links, Löschen-X rechts (sobald Text eingegeben ist). */
export function suchfeldAufwerten(input) {
  if (input.au || input.hasAttribute('data-nativ') || input.closest('.au-menue, .input-group, [data-nativ-auswahl]')) return;
  let huelle = input.parentElement;
  // Container mit eigener Lupe (neues Design: .nl-suche, .u-suche, Raumsuche): nur das X ergänzen – oder nichts, wenn schon ein Leeren-Knopf da ist
  const eigeneLupe = huelle && [...huelle.children].some(k => k !== input && k.matches('.bi-search, .u-sym, svg, img') && (k.compareDocumentPosition(input) & Node.DOCUMENT_POSITION_FOLLOWING));
  if (eigeneLupe && huelle.querySelector('.u-leeren, [data-leeren]')) { input.au = { huelle }; return; }
  if (!eigeneLupe) {
    huelle = document.createElement('span');
    huelle.className = 'au-suchfeld';
    [...input.classList].filter(k => LAYOUT.test(k)).forEach(k => { huelle.classList.add(k); input.classList.remove(k); });
    for (const p of ['width', 'minWidth', 'maxWidth', 'flex']) { if (input.style[p]) { huelle.style[p] = input.style[p]; input.style[p] = ''; } }
    if (input.classList.contains('form-control-sm')) huelle.classList.add('au-sm');
    input.before(huelle);
    huelle.innerHTML = '<i class="bi bi-search au-lupe" aria-hidden="true"></i>';
    huelle.append(input);
  }
  huelle.classList.add('au-mit-x');
  const x = document.createElement('button');
  x.type = 'button';
  x.className = 'au-x';
  x.tabIndex = -1;
  x.setAttribute('aria-label', 'Suche löschen');
  x.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
  input.after(x);
  const zeigen = () => { x.hidden = !input.value; };
  input.addEventListener('input', zeigen);
  x.addEventListener('mousedown', ev => ev.preventDefault());
  x.addEventListener('click', () => {
    input.value = '';
    zeigen();
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('search', { bubbles: true }));
    input.focus();
  });
  input.addEventListener('keydown', ev => { if (ev.key === 'Escape' && input.value) { ev.stopPropagation(); x.click(); } });
  const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
  Object.defineProperty(input, 'value', { configurable: true, get() { return d.get.call(this); }, set(v) { d.set.call(this, v); zeigen(); } });
  zeigen();
  input.au = { huelle, x };
}

// ------------------------------------------------------------------ Automatisch für die ganze Oberfläche

/** Alle Selects und Suchfelder unterhalb von `wurzel` aufwerten. */
export function aufwerten(wurzel = document.body) {
  if (wurzel.nodeType !== 1) return;
  if (wurzel.tagName === 'SELECT') selectAufwerten(wurzel);
  else if (wurzel.tagName === 'INPUT' && wurzel.type === 'search') suchfeldAufwerten(wurzel);
  else {
    wurzel.querySelectorAll('select').forEach(selectAufwerten);
    wurzel.querySelectorAll('input[type=search]').forEach(suchfeldAufwerten);
  }
}

let beobachter = null;
/** Neue Inhalte (Ansichten, Dialoge) laufend aufwerten. */
export function beobachten(wurzel = document.body) {
  if (beobachter) return;
  aufwerten(wurzel);
  beobachter = new MutationObserver(liste => {
    for (const m of liste) {
      for (const n of m.addedNodes) {
        if (n.nodeType !== 1 || n.classList?.contains('au') || n.classList?.contains('au-menue')) continue;
        // verschobenes Select: Hülle mitnehmen
        if (n.tagName === 'SELECT' && n.au && n.nextElementSibling !== n.au.wurzel) n.after(n.au.wurzel);
        aufwerten(n);
      }
      for (const n of m.removedNodes) if (n.au?.wurzel && !n.isConnected && n.au.wurzel.isConnected && n.tagName === 'SELECT') n.au.wurzel.remove();
    }
  });
  beobachter.observe(wurzel, { childList: true, subtree: true });
  // Navigation (Hash-Wechsel) schließt ein offenes Menü
  window.addEventListener('hashchange', menueSchliessen);
}
