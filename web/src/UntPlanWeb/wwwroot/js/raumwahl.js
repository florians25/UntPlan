// Raumwahl für Prüfungen (Stundendetails „Prüfung erstellen“, Melden → Prüfung eintragen): durchsuchbares Auswahlfeld über einem
// Textfeld mit Raumkürzeln (mehrere durch Komma). Freitextsuche nach Kürzel und Langname; je Raum Kapazität und frei/belegt zur
// gewählten Zeit (Belegung aus Plan, Prüfungen, Terminen, Vormerkungen, Sperren und Buchungen über GET /api/app/buchen/raumstatus);
// Räume der Stunde zuerst, dann buchbare, dann weitere – darin freie vor belegten. Tastatur: ↓/↑ wählen, Enter übernimmt,
// Esc schließt (nur die Liste, nicht den Dialog), Tab verlässt das Feld.
import { e, holen } from './ui.js';

function stileLaden() {
  if (document.querySelector('link[data-raumwahl-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../css/raumwahl.css', import.meta.url).href;
  l.dataset.raumwahlCss = '1';
  document.head.append(l);
}

const TRENNER = /[,;]+/;
const tokens = wert => String(wert || '').split(TRENNER).map(x => x.trim()).filter(Boolean);
const gleich = (a, b) => a.localeCompare(b, 'de', { sensitivity: 'accent' }) === 0;
const STATUS_ART = { unterricht: 'Unterricht', klausur: 'Prüfung', vorgemerkt: 'vorgemerkt', buchung: 'Buchung', termin: 'Termin', gesperrt: 'gesperrt' };
let zaehler = 0;

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
  huelle.insertAdjacentHTML('beforeend', `<div class="rw-pop d-none" data-rw-pop>
      <div class="rw-kopf"><span data-rw-zahl></span><label class="rw-nurfrei"><input type="checkbox" data-rw-nurfrei> Nur freie</label></div>
      <div class="rw-liste" role="listbox" id="${id}-liste" aria-label="Räume"></div></div>
    <div class="rw-hinweis d-none" data-rw-hinweis role="status"></div>`);
  input.setAttribute('role', 'combobox');
  input.setAttribute('aria-autocomplete', 'list');
  input.setAttribute('aria-expanded', 'false');
  input.setAttribute('aria-controls', `${id}-liste`);
  if (!input.placeholder) input.placeholder = 'Raum suchen (Kürzel oder Name)';
  const pop = huelle.querySelector('[data-rw-pop]');
  const liste = huelle.querySelector('.rw-liste');
  const hinweis = huelle.querySelector('[data-rw-hinweis]');
  const nurFrei = huelle.querySelector('[data-rw-nurfrei]');

  let raeume = null, schluessel = '', laedt = null, getippt = false, aktiv = -1, sichtbar = [];
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

  const statusText = r => r.frei ? 'frei' : 'belegt';
  const belegtTitel = r => r.belegt.map(b => `${b.stunde}. Std.: ${STATUS_ART[b.art] ?? b.art}${b.text ? ` – ${b.text}` : ''}`).join('\n');

  function zeichnen() {
    if (!raeume) { liste.innerHTML = '<div class="rw-leer">Räume werden geladen …</div>'; return; }
    const q = anfrage();
    const gewaehlt = tokens(input.value);
    const passt = r => !q || r.kurz.toLowerCase().includes(q) || (r.name || '').toLowerCase().includes(q);
    // Treffer am Anfang des Kürzels zuerst (innerhalb der Gruppen bleibt die Reihenfolge des Servers)
    const rang = r => (q && r.kurz.toLowerCase().startsWith(q) ? 0 : 1);
    const treffer = raeume.filter(r => passt(r) && (!nurFrei.checked || r.frei || gewaehlt.some(g => gleich(g, r.kurz))));
    const gruppe = r => r.eigen ? 0 : r.buchbar ? 1 : 2;
    sichtbar = treffer.map((r, i) => ({ r, i })).sort((a, b) => gruppe(a.r) - gruppe(b.r) || rang(a.r) - rang(b.r) || a.i - b.i).map(x => x.r);
    const freiAnzahl = raeume.filter(r => r.frei).length;
    huelle.querySelector('[data-rw-zahl]').textContent = `${freiAnzahl} von ${raeume.length} Räumen frei`;
    const TITEL = ['Raum der Stunde', 'Buchbare Räume', 'Weitere Räume'];
    let letzte = -1;
    liste.innerHTML = sichtbar.map((r, i) => {
      const g = gruppe(r);
      const kopf = g !== letzte ? `<div class="rw-gruppe" role="presentation">${TITEL[g]}</div>` : '';
      letzte = g;
      const an = gewaehlt.some(x => gleich(x, r.kurz));
      return `${kopf}<div class="rw-opt ${r.frei ? 'frei' : 'belegt'} ${i === aktiv ? 'aktiv' : ''}" role="option" id="${id}-o${i}" data-i="${i}" aria-selected="${an}"${r.frei ? '' : ` title="${e(belegtTitel(r))}"`}>
        <span class="rw-haken" aria-hidden="true">${an ? '<i class="bi bi-check2"></i>' : ''}</span>
        <span class="rw-kurz">${e(r.kurz)}</span><span class="rw-name">${e(r.name && r.name !== r.kurz ? r.name : '')}</span>
        ${r.kapazitaet ? `<span class="rw-kap" title="Kapazität">${r.kapazitaet} Plätze</span>` : '<span class="rw-kap"></span>'}
        <span class="rw-status ${r.frei ? 'frei' : 'belegt'}">${statusText(r)}${!r.frei && r.belegt[0]?.text ? `<small> · ${e(r.belegt[0].text)}</small>` : ''}</span></div>`;
    }).join('') || `<div class="rw-leer">${q ? `Kein Raum passt zu „${e(q)}“.` : 'Keine Räume.'}</div>`;
    input.setAttribute('aria-activedescendant', aktiv >= 0 && sichtbar[aktiv] ? `${id}-o${aktiv}` : '');
  }

  async function oeffnen() {
    // nach oben aufklappen, wenn unter dem Feld zu wenig Platz ist
    const r = input.getBoundingClientRect(), unten = innerHeight - r.bottom;
    pop.classList.toggle('oben', unten < 340 && r.top > unten);
    pop.classList.remove('d-none');
    input.setAttribute('aria-expanded', 'true');
    zeichnen();
    await laden();
    if (!pop.classList.contains('d-none')) zeichnen();
  }
  function schliessen() {
    pop.classList.add('d-none');
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
    aktiv = -1;
    getippt = false;
  }
  const offen = () => !pop.classList.contains('d-none');

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
    hinweis.textContent = teile.join(' ');
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
  input.addEventListener('blur', () => setTimeout(() => { if (!huelle.contains(document.activeElement)) { schliessen(); pruefen(); } }, 120));
  // Klick in die Liste: Feld behält den Fokus
  pop.addEventListener('mousedown', ev => { if (!ev.target.closest('[data-rw-nurfrei], .rw-nurfrei')) ev.preventDefault(); });
  liste.addEventListener('click', ev => { const o = ev.target.closest('.rw-opt'); if (o) waehlen(sichtbar[Number(o.dataset.i)]); });
  nurFrei.addEventListener('change', () => { zeichnen(); input.focus(); });
  // Datum oder Stunden geändert: Belegung neu laden
  wurzel?.addEventListener('change', ev => {
    const n = ev.target?.name;
    if (n && [felder.datum, felder.von, felder.bis].includes(n)) { schluessel = ''; raeume = null; pruefen(); if (offen()) oeffnen(); }
  });
  pruefen();
  return { aktualisieren: () => { schluessel = ''; raeume = null; pruefen(); } };
}

export { tokens as raumTokens };
