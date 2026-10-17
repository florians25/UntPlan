// Raumwahl – eine Komponente für alle Raumauswahlen:
//  • raumwahl(input, …): durchsuchbare Auswahlliste über einem Textfeld mit Raumkürzeln (mehrere durch Komma) – Prüfung erstellen bzw.
//    editieren (Stundendetails), Melden → Prüfung eintragen. Die Liste hängt als „Portal“ am obersten Behälter (Dialog, Fenster bzw.
//    Seite), ist fest positioniert und an das Feld geheftet: öffnet nach unten, bei Platzmangel nach oben, folgt beim Scrollen im
//    Dialog und wird nie vom Dialog abgeschnitten. Tastatur: ↓/↑ wählen, Enter übernimmt, Esc schließt (nur die Liste), Tab verlässt.
//  • raumListe(ziel, …): eingebettete Auswahlliste mit Suche und Filtern (Gebäude, Abteilung, Kapazität, nur freie) – Raumbuchung in
//    den Stundendetails (Raum ersetzen bzw. hinzufügen) und Raumschritt im Buchungsmodul.
//  • raumZeile(r, …): gemeinsame Listenzeile – Kürzel (Medium), Langname grau, Kapazität als kleines Symbol, rechts Status-Pill frei/belegt.
// Belegung aus Plan, Prüfungen, Terminen, Vormerkungen, Sperren und Buchungen über GET /api/app/buchen/raumstatus.
import { e, holen } from './ui.js';

function stileLaden() {
  if (document.querySelector('link[data-raumwahl-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../css/raumwahl.css', import.meta.url).href;
  l.dataset.raumwahlCss = '1';
  document.head.append(l);
}

// Stile gleich beim Laden des Moduls (auch Status-Pills in Listen, die nur raumZeile bzw. die Klassen nutzen)
stileLaden();

const TRENNER = /[,;]+/;
const tokens = wert => String(wert || '').split(TRENNER).map(x => x.trim()).filter(Boolean);
const gleich = (a, b) => a.localeCompare(b, 'de', { sensitivity: 'accent' }) === 0;
const STATUS_ART = { unterricht: 'Unterricht', klausur: 'Prüfung', vorgemerkt: 'vorgemerkt', buchung: 'Buchung', termin: 'Termin', gesperrt: 'gesperrt' };
const GRUPPEN = ['Raum der Stunde', 'Buchbare Räume', 'Weitere Räume'];
const KAPAZITAETEN = [10, 15, 20, 25, 30, 40, 60, 100];
let zaehler = 0;

/** Belegung eines Raums als Text (Tooltip). */
export const belegtText = r => (r.belegt ?? []).map(b => `${b.stunde}. Std.: ${STATUS_ART[b.art] ?? b.art}${b.text ? ` – ${b.text}` : ''}`).join('\n');

/**
 * Eine Listenzeile. r: { kurz, name, kapazitaet, frei, belegt } bzw. eigener Status (status: { klasse, text, titel }).
 * optionen: { i, id, gewaehlt, aktiv, art: 'haken' | 'radio', aus (nicht wählbar), zusatz (HTML unter dem Namen) }
 */
export function raumZeile(r, { i = 0, id = '', gewaehlt = false, aktiv = false, art = 'haken', aus = false, zusatz = '', status = null, tab = false } = {}) {
  const st = status ?? (r.frei ? { klasse: 'frei', text: 'frei' } : { klasse: 'belegt', text: 'belegt', titel: belegtText(r) });
  const name = r.name && r.name !== r.kurz ? r.name : '';
  return `<div class="rw-opt ${st.klasse} ${aktiv ? 'aktiv' : ''} ${gewaehlt ? 'gewaehlt' : ''} ${aus ? 'aus' : ''}" role="option" ${tab && !aus ? 'tabindex="0"' : ''} ${id ? `id="${id}"` : ''} data-i="${i}" data-kurz="${e(r.kurz)}"
      aria-selected="${gewaehlt}" ${aus ? 'aria-disabled="true"' : ''} ${st.titel ? `title="${e(st.titel)}"` : ''}>
    <span class="rw-wahl ${art}" aria-hidden="true">${art === 'haken' && gewaehlt ? '<i class="bi bi-check2"></i>' : ''}</span>
    <span class="rw-text"><span class="rw-kurz">${e(r.kurz)}</span>${name ? `<span class="rw-name">${e(name)}</span>` : ''}${zusatz}</span>
    ${r.kapazitaet ? `<span class="rw-kap" title="${r.kapazitaet} Plätze"><i class="bi bi-people" aria-hidden="true"></i>${r.kapazitaet}</span>` : '<span class="rw-kap"></span>'}
    <span class="rw-status ${st.klasse}">${e(st.text)}</span></div>`;
}

/** Behälter für das Portal: oberster Dialog bzw. das Bootstrap-Fenster (dessen Fokusfalle greift sonst) oder die Seite. */
const behaelterVon = el => el.closest('.kbd-overlay') ?? el.closest('.modal') ?? el.closest('dialog') ?? document.body;

/** Nächster scrollender Vorfahre (Dialoginhalt): Liste schließen, sobald das Feld dort aus dem sichtbaren Bereich scrollt. */
function scrollVorfahre(el) {
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
    const o = getComputedStyle(p).overflowY;
    if ((o === 'auto' || o === 'scroll') && p.scrollHeight > p.clientHeight) return p;
  }
  return null;
}

/**
 * Raumwahl an ein Textfeld binden.
 * @param {HTMLInputElement} input Textfeld mit Raumkürzeln (Komma-getrennt)
 * @param {{ wurzel: Element, felder?: { datum: string, von: string, bis: string }, unterricht?: number|null }} optionen
 *   wurzel: Formular bzw. Dialog mit den Feldern für Datum und Stunden (Namen in felder)
 */
export function raumwahl(input, { wurzel, felder = { datum: 'datum', von: 'von', bis: 'bis' }, unterricht = null } = {}) {
  if (!input || input.dataset.raumwahl) return null;
  stileLaden();
  input.dataset.raumwahl = '1';
  input.removeAttribute('list');
  input.autocomplete = 'off';
  const id = `rw-${++zaehler}`;
  const huelle = document.createElement('div');
  huelle.className = 'rw';
  input.before(huelle);
  huelle.append(input);
  huelle.insertAdjacentHTML('beforeend', `<i class="bi bi-chevron-down rw-pfeil" aria-hidden="true"></i>
    <div class="rw-hinweis d-none" data-rw-hinweis role="status"></div>`);
  // Liste als Portal (wird beim Öffnen in den Behälter gehängt)
  const pop = document.createElement('div');
  pop.className = 'rw-pop';
  pop.dataset.rwPop = id;
  pop.innerHTML = `<div class="rw-kopf"><span class="rw-zahl" data-rw-zahl></span>
      <label class="rw-schalter"><input type="checkbox" data-rw-nurfrei><span class="rw-spur" aria-hidden="true"></span>Nur freie</label></div>
    <div class="rw-liste" role="listbox" id="${id}-liste" aria-label="Räume"></div>`;
  input.setAttribute('role', 'combobox');
  input.setAttribute('aria-autocomplete', 'list');
  input.setAttribute('aria-expanded', 'false');
  input.setAttribute('aria-controls', `${id}-liste`);
  if (!input.placeholder) input.placeholder = 'Raum suchen (Kürzel oder Name)';
  const liste = pop.querySelector('.rw-liste');
  const hinweis = huelle.querySelector('[data-rw-hinweis]');
  const nurFrei = pop.querySelector('[data-rw-nurfrei]');

  let raeume = null, schluessel = '', laedt = null, getippt = false, aktiv = -1, sichtbar = [], scroller = null;
  const feld = n => wurzel?.querySelector(`[name="${n}"]`)?.value ?? '';
  const zeit = () => {
    const von = Number(feld(felder.von)) || 1;
    return { datum: feld(felder.datum), von, bis: Math.max(von, Number(feld(felder.bis)) || von) };
  };

  async function laden() {
    const z = zeit();
    if (!z.datum) return [];
    const k = `${z.datum}|${z.von}|${z.bis}`;
    if (k === schluessel && raeume) return raeume;
    if (k === schluessel && laedt) return laedt;
    schluessel = k;
    raeume = null;
    laedt = holen(`/api/app/buchen/raumstatus?datum=${z.datum}&von=${z.von}&bis=${z.bis}${unterricht ? `&unterricht=${unterricht}` : ''}`)
      .catch(() => [])
      .then(r => { if (schluessel === k) { raeume = r; laedt = null; } return r; });
    return laedt;
  }

  const anfrage = () => {
    if (!getippt) return '';
    const teile = input.value.split(TRENNER);
    return (teile[teile.length - 1] ?? '').trim().toLowerCase();
  };

  function zeichnen() {
    if (!raeume) { liste.innerHTML = '<div class="rw-leer"><span class="rw-laedt" aria-hidden="true"></span>Räume werden geladen …</div>'; pop.querySelector('[data-rw-zahl]').textContent = ''; return; }
    const q = anfrage();
    const gewaehlt = tokens(input.value);
    const passt = r => !q || r.kurz.toLowerCase().includes(q) || (r.name || '').toLowerCase().includes(q);
    // Treffer am Anfang des Kürzels zuerst (innerhalb der Gruppen bleibt die Reihenfolge des Servers)
    const rang = r => (q && r.kurz.toLowerCase().startsWith(q) ? 0 : 1);
    const treffer = raeume.filter(r => passt(r) && (!nurFrei.checked || r.frei || gewaehlt.some(g => gleich(g, r.kurz))));
    const gruppe = r => r.eigen ? 0 : r.buchbar ? 1 : 2;
    sichtbar = treffer.map((r, i) => ({ r, i })).sort((a, b) => gruppe(a.r) - gruppe(b.r) || rang(a.r) - rang(b.r) || a.i - b.i).map(x => x.r);
    const freiAnzahl = raeume.filter(r => r.frei).length;
    pop.querySelector('[data-rw-zahl]').innerHTML = `<b>${freiAnzahl}</b> von ${raeume.length} Räumen frei`;
    let letzte = -1;
    liste.innerHTML = sichtbar.map((r, i) => {
      const g = gruppe(r);
      const kopf = g !== letzte ? `<div class="rw-gruppe" role="presentation">${GRUPPEN[g]}</div>` : '';
      letzte = g;
      return kopf + raumZeile(r, { i, id: `${id}-o${i}`, gewaehlt: gewaehlt.some(x => gleich(x, r.kurz)), aktiv: i === aktiv });
    }).join('') || `<div class="rw-leer"><i class="bi bi-search" aria-hidden="true"></i>${q ? `Kein Raum passt zu „${e(q)}“.` : nurFrei.checked ? 'Zu dieser Zeit ist kein Raum frei.' : 'Keine Räume.'}</div>`;
    input.setAttribute('aria-activedescendant', aktiv >= 0 && sichtbar[aktiv] ? `${id}-o${aktiv}` : '');
    platzieren();
  }

  /** An das Feld heften: unten, wenn dort Platz ist (bzw. mehr als oben), sonst oben; Höhe passt sich dem Platz an. */
  function platzieren() {
    if (!offen()) return;
    const r = input.getBoundingClientRect();
    const sr = scroller?.getBoundingClientRect();
    // Feld aus dem sichtbaren Bereich gescrollt (Seite oder Dialoginhalt): schließen
    if (!input.isConnected || r.bottom < 0 || r.top > innerHeight || (sr && (r.bottom < sr.top + 4 || r.top > sr.bottom - 4))) { schliessen(); return; }
    const vw = document.documentElement.clientWidth, vh = innerHeight, rand = 8, abstand = 4;
    const breite = Math.min(Math.max(r.width, 340), vw - 2 * rand);
    pop.style.width = `${breite}px`;
    pop.style.left = `${Math.max(rand, Math.min(r.left, vw - rand - breite))}px`;
    const kopf = pop.querySelector('.rw-kopf').offsetHeight || 36;
    const unten = vh - r.bottom - abstand - rand, oben = r.top - abstand - rand;
    const gewuenscht = Math.min(320, liste.scrollHeight || 320) + kopf + 2;
    const nachOben = unten < Math.min(gewuenscht, 240) && oben > unten;
    const platz = nachOben ? oben : unten;
    liste.style.maxHeight = `${Math.max(96, Math.min(320, platz - kopf - 2))}px`;
    pop.classList.toggle('oben', nachOben);
    if (nachOben) { pop.style.top = ''; pop.style.bottom = `${vh - r.top + abstand}px`; }
    else { pop.style.bottom = ''; pop.style.top = `${r.bottom + abstand}px`; }
  }
  const beimScrollen = ev => { if (!pop.contains(ev.target)) requestAnimationFrame(platzieren); };
  const aussen = ev => { if (!pop.contains(ev.target) && !huelle.contains(ev.target)) { schliessen(); pruefen(); } };

  async function oeffnen() {
    if (!offen()) {
      behaelterVon(huelle).append(pop);
      scroller = scrollVorfahre(huelle);
      huelle.classList.add('offen');
      input.setAttribute('aria-expanded', 'true');
      window.addEventListener('scroll', beimScrollen, true);
      window.addEventListener('resize', platzieren);
      document.addEventListener('mousedown', aussen, true);
      document.addEventListener('touchstart', aussen, true);
    }
    zeichnen();
    await laden();
    if (offen()) zeichnen();
  }
  function schliessen() {
    if (!offen()) return;
    pop.remove();
    huelle.classList.remove('offen');
    window.removeEventListener('scroll', beimScrollen, true);
    window.removeEventListener('resize', platzieren);
    document.removeEventListener('mousedown', aussen, true);
    document.removeEventListener('touchstart', aussen, true);
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
    aktiv = -1;
    getippt = false;
  }
  const offen = () => pop.isConnected;

  function waehlen(r) {
    if (!r) return;
    let t = tokens(input.value);
    const vorhanden = t.findIndex(x => gleich(x, r.kurz));
    if (vorhanden >= 0 && !(getippt && anfrage())) t.splice(vorhanden, 1);   // erneut gewählt: entfernen
    else {
      if (getippt && anfrage() && !input.value.trim().match(/[,;]$/)) t = t.slice(0, -1);   // angefangene Eingabe ersetzen
      if (!t.some(x => gleich(x, r.kurz))) t.push(r.kurz);
    }
    input.value = t.join(', ');
    input.dispatchEvent(new Event('change', { bubbles: true }));
    schliessen();
    pruefen();
  }

  function markieren(i) {
    if (!sichtbar.length) return;
    aktiv = (i + sichtbar.length) % sichtbar.length;
    liste.querySelectorAll('.rw-opt').forEach(o => o.classList.toggle('aktiv', Number(o.dataset.i) === aktiv));
    const el = liste.querySelector(`[data-i="${aktiv}"]`);
    el?.scrollIntoView({ block: 'nearest' });
    input.setAttribute('aria-activedescendant', el ? el.id : '');
  }

  /** Hinweis unter dem Feld, wenn ein gewählter Raum zur gewählten Zeit belegt bzw. unbekannt ist. */
  async function pruefen() {
    const t = tokens(input.value);
    if (!t.length) { hinweis.classList.add('d-none'); return; }
    const liste2 = await laden();
    if (!liste2.length) { hinweis.classList.add('d-none'); return; }
    const belegt = t.map(x => liste2.find(r => gleich(r.kurz, x))).filter(r => r && !r.frei);
    const unbekannt = t.filter(x => !liste2.some(r => gleich(r.kurz, x)));
    const teile = [
      ...belegt.map(r => `${r.kurz} ist zur gewählten Zeit belegt${r.belegt[0]?.text ? ` (${r.belegt[0].text})` : ''}.`),
      ...unbekannt.map(x => `Raum „${x}“ ist unbekannt.`),
    ];
    hinweis.innerHTML = teile.length ? `<i class="bi bi-exclamation-circle" aria-hidden="true"></i>${e(teile.join(' '))}` : '';
    hinweis.classList.toggle('d-none', !teile.length);
  }

  input.addEventListener('focus', () => { if (!offen()) oeffnen(); });
  input.addEventListener('click', () => { if (!offen()) oeffnen(); });
  input.addEventListener('input', () => { getippt = true; aktiv = -1; if (!offen()) oeffnen(); else zeichnen(); if (sichtbar.length && anfrage()) markieren(0); });
  input.addEventListener('keydown', ev => {
    if (ev.key === 'ArrowDown') { ev.preventDefault(); if (!offen()) oeffnen().then(() => markieren(0)); else markieren(aktiv + 1); }
    else if (ev.key === 'ArrowUp') { ev.preventDefault(); if (offen()) markieren(aktiv - 1); }
    else if (ev.key === 'Enter' && offen()) { ev.preventDefault(); if (aktiv >= 0) waehlen(sichtbar[aktiv]); else schliessen(); }
    else if (ev.key === 'Escape' && offen()) { ev.preventDefault(); ev.stopPropagation(); schliessen(); }
    else if (ev.key === 'Tab') schliessen();
  });
  input.addEventListener('blur', () => setTimeout(() => { if (document.activeElement !== input && !pop.contains(document.activeElement)) { schliessen(); pruefen(); } }, 150));
  huelle.querySelector('.rw-pfeil').addEventListener('mousedown', ev => { ev.preventDefault(); if (offen()) schliessen(); else { input.focus(); oeffnen(); } });
  // Klick in die Liste: Feld behält den Fokus
  pop.addEventListener('mousedown', ev => { if (!ev.target.closest('.rw-schalter')) ev.preventDefault(); });
  liste.addEventListener('click', ev => { const o = ev.target.closest('.rw-opt'); if (o) waehlen(sichtbar[Number(o.dataset.i)]); });
  liste.addEventListener('mousemove', ev => { const o = ev.target.closest('.rw-opt'); if (o && Number(o.dataset.i) !== aktiv) { aktiv = Number(o.dataset.i); liste.querySelectorAll('.rw-opt').forEach(x => x.classList.toggle('aktiv', x === o)); } });
  nurFrei.addEventListener('change', () => { zeichnen(); input.focus(); });
  // Datum oder Stunden geändert: Belegung neu laden
  wurzel?.addEventListener('change', ev => {
    const n = ev.target?.name;
    if (n && [felder.datum, felder.von, felder.bis].includes(n)) { schluessel = ''; raeume = null; pruefen(); if (offen()) oeffnen(); }
  });
  pruefen();
  return { aktualisieren: () => { schluessel = ''; raeume = null; pruefen(); }, schliessen };
}

// ------------------------------------------------------------------ Eingebettete Liste mit Filtern

const sortiertDe = l => [...new Set(l.filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de', { numeric: true }));

/**
 * Eingebettete Raumliste mit Suche und Filtern (Gebäude, Abteilung, Raumart, Kapazität, nur freie) und Einzelauswahl.
 * raeume: [{ kurz, name, kapazitaet, gebaeude, abteilung, raumart, frei, belegt, eigen? }] (bzw. eigener status je Raum über status(r))
 * optionen: { gewaehlt, beiWahl(raum|null), nurFrei (Vorgabe true), status(r) → { klasse, text, titel }, waehlbar(r), zusatz(r) → HTML,
 *             leerText, ohne: Text für „ohne Raum“-Zeile, filter: gemerkter Filterzustand (Objekt wird fortgeschrieben), titel }
 * Liefert { zeichnen, filter, setzeRaeume(liste) }.
 */
export function raumListe(ziel, optionen = {}) {
  stileLaden();
  // ohne gepflegte Abteilungen: Abteilung aus dem Kürzel ableiten (Buchstaben am Anfang, z. B. „CH“ aus „CH1“) – wie bisher
  const vorbereiten = l => (l.some(r => r.abteilung) ? l : l.map(r => ({ ...r, abteilung: (r.kurz.match(/^[A-Za-zÄÖÜäöü]+/)?.[0] ?? '').toUpperCase() })));
  let raeume = vorbereiten(optionen.raeume ?? []);
  const f = optionen.filter ?? {};
  f.suche ??= ''; f.gebaeude ??= ''; f.abteilung ??= ''; f.raumart ??= ''; f.kapazitaet ??= ''; f.nurFrei ??= optionen.nurFrei ?? true;
  let gewaehlt = optionen.gewaehlt ?? null;
  const frei = r => optionen.status ? optionen.status(r).klasse === 'frei' : r.frei;
  const waehlbar = r => optionen.waehlbar ? optionen.waehlbar(r) : true;
  const id = `rl-${++zaehler}`;
  const pille = (n, titel, werte, text = x => x) => werte.length > 1 || f[n]
    ? `<label class="nl-pille rw-pille ${f[n] ? 'aktiv' : ''}"><select data-rwf="${n}" aria-label="${e(titel)}"><option value="">${e(titel)}</option>${werte.map(w => `<option value="${e(w)}" ${String(w) === String(f[n]) ? 'selected' : ''}>${e(text(w))}</option>`).join('')}</select></label>` : '';

  function geruest() {
    const maxKap = Math.max(0, ...raeume.map(r => Number(r.kapazitaet) || 0));
    ziel.innerHTML = `<div class="rw-einbettung" id="${id}">
      ${optionen.titel ? `<h3 class="rw-titel">${e(optionen.titel)}</h3>` : ''}
      <div class="rw-filter" role="group" aria-label="Filter">
        <label class="nl-suche rw-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-rwf="suche" value="${e(f.suche)}" placeholder="Raum suchen" aria-label="Raum suchen"></label>
        ${pille('gebaeude', 'Gebäude', sortiertDe(raeume.map(r => r.gebaeude)))}
        ${pille('abteilung', 'Abteilung', sortiertDe(raeume.map(r => r.abteilung)))}
        ${pille('raumart', 'Raumart', sortiertDe(raeume.map(r => r.raumart)))}
        ${maxKap ? pille('kapazitaet', 'Kapazität', KAPAZITAETEN.filter(k => k <= maxKap), k => `ab ${k} Plätzen`) : ''}
        <label class="rw-schalter rw-nurfrei"><input type="checkbox" data-rwf="nurFrei" ${f.nurFrei ? 'checked' : ''}><span class="rw-spur" aria-hidden="true"></span>${e(optionen.nurFreiText ?? 'Nur freie')}</label>
        <button type="button" class="rw-zuruecksetzen" data-rw-reset hidden>Filter zurücksetzen</button>
      </div>
      <div class="rw-zahlzeile"><span data-rw-zahl></span></div>
      <div class="rw-liste rw-eingebettet" role="listbox" aria-label="Räume" data-rw-liste></div></div>`;
    ziel.querySelectorAll('[data-rwf]').forEach(x => x.addEventListener(x.type === 'search' ? 'input' : 'change', () => {
      const n = x.dataset.rwf;
      f[n] = x.type === 'checkbox' ? x.checked : x.type === 'search' ? x.value.trim() : x.value;
      x.closest('.rw-pille')?.classList.toggle('aktiv', !!f[n]);
      zeichnen();
    }));
    ziel.querySelector('[data-rw-reset]').onclick = () => {
      Object.assign(f, { suche: '', gebaeude: '', abteilung: '', raumart: '', kapazitaet: '', nurFrei: optionen.nurFrei ?? true });
      geruest(); zeichnen();
    };
    const l = ziel.querySelector('[data-rw-liste]');
    l.addEventListener('click', ev => {
      const o = ev.target.closest('.rw-opt');
      if (!o || o.classList.contains('aus')) return;
      gewaehlt = o.dataset.kurz || null;
      zeichnen();
      optionen.beiWahl?.(gewaehlt);
    });
    l.addEventListener('keydown', ev => {
      const alle = [...l.querySelectorAll('.rw-opt:not(.aus)')];
      const i = alle.indexOf(document.activeElement);
      if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') { ev.preventDefault(); alle[Math.max(0, Math.min(alle.length - 1, i + (ev.key === 'ArrowDown' ? 1 : -1)))]?.focus(); }
      else if ((ev.key === 'Enter' || ev.key === ' ') && i >= 0) { ev.preventDefault(); alle[i].click(); }
    });
  }

  function zeichnen() {
    const q = f.suche.toLowerCase();
    const passt = r => (!q || `${r.kurz} ${r.name ?? ''} ${r.raumart ?? ''}`.toLowerCase().includes(q))
      && (!f.gebaeude || r.gebaeude === f.gebaeude) && (!f.abteilung || r.abteilung === f.abteilung) && (!f.raumart || r.raumart === f.raumart)
      && (!f.kapazitaet || (Number(r.kapazitaet) || 0) >= Number(f.kapazitaet)) && (!f.nurFrei || frei(r) || r.kurz === gewaehlt);
    const sichtbar = raeume.filter(passt);
    const freiAnzahl = raeume.filter(frei).length;
    const gefiltert = !!(f.suche || f.gebaeude || f.abteilung || f.raumart || f.kapazitaet);
    ziel.querySelector('[data-rw-reset]').hidden = !gefiltert;
    ziel.querySelector('[data-rw-zahl]').innerHTML = `${sichtbar.length === raeume.length ? `<b>${raeume.length}</b> ${raeume.length === 1 ? 'Raum' : 'Räume'}` : `<b>${sichtbar.length}</b> von ${raeume.length} Räumen`} · ${freiAnzahl} frei`;
    const l = ziel.querySelector('[data-rw-liste]');
    const ohne = optionen.ohne ? `<div class="rw-opt frei ${!gewaehlt ? 'gewaehlt' : ''}" role="option" tabindex="0" data-kurz="" aria-selected="${!gewaehlt}">
        <span class="rw-wahl radio" aria-hidden="true"></span><span class="rw-text"><span class="rw-kurz">${e(optionen.ohne)}</span></span><span class="rw-kap"></span><span></span></div>` : '';
    l.innerHTML = ohne + (sichtbar.map((r, i) => raumZeile(r, { i, art: 'radio', gewaehlt: r.kurz === gewaehlt, aus: !waehlbar(r), status: optionen.status?.(r) ?? null, zusatz: optionen.zusatz?.(r) ?? '', tab: true })).join('')
      || `<div class="rw-leer gross"><i class="bi bi-door-closed" aria-hidden="true"></i><span>${e(gefiltert || f.nurFrei
        ? (f.nurFrei && !gefiltert ? (optionen.leerText ?? 'Zu dieser Zeit ist kein passender Raum frei.') : 'Kein Raum passt zu den Filtern.') : 'Keine Räume.')}</span>
        ${gefiltert || f.nurFrei ? `<button type="button" class="nl-knopf" data-rw-alle>${f.nurFrei && !gefiltert ? 'Auch belegte zeigen' : 'Filter zurücksetzen'}</button>` : ''}</div>`);
    l.querySelector('[data-rw-alle]')?.addEventListener('click', () => {
      if (f.nurFrei && !gefiltert) { f.nurFrei = false; ziel.querySelector('[data-rwf=nurFrei]').checked = false; zeichnen(); }
      else ziel.querySelector('[data-rw-reset]').click();
    });
  }

  geruest();
  zeichnen();
  return {
    zeichnen, filter: f,
    get gewaehlt() { return gewaehlt; },
    setzeRaeume(l) { raeume = vorbereiten(l); geruest(); zeichnen(); },
    fokus() { ziel.querySelector('[data-rwf=suche]')?.focus({ preventScroll: true }); },
  };
}

export { tokens as raumTokens };
