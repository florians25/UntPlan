// Gemeinsame Hilfen: Schnittstelle, Escaping, Symbole (Bootstrap Icons, Linienstil), Meldungen und Dialoge, Datumsfunktionen.

import { symbol, LOGO as BILDMARKE } from './symbole.js';

/** Bildmarke von UntPlanWeb (eigenes Zifferblatt-Symbol). */
export const LOGO = BILDMARKE;

const SYMBOLE = {
  heute: 'house', plan: 'calendar3-week', tag: 'grid-3x3', vertretung: 'arrow-left-right', mitteilung: 'envelope',
  klassenbuch: 'journal-text', melden: 'clipboard-plus', admin: 'gear', profil: 'person-circle', abmelden: 'box-arrow-right',
  menue: 'list', links: 'chevron-left', rechts: 'chevron-right', unten: 'chevron-down', drucken: 'printer', plus: 'plus-lg',
  loeschen: 'trash', haken: 'check-lg', x: 'x-lg', monitor: 'display', schluessel: 'key', hochladen: 'upload',
  herunterladen: 'download', info: 'info-circle', warnung: 'exclamation-triangle', benutzer: 'people', uhr: 'clock',
  raum: 'door-open', klausur: 'file-earmark-check', senden: 'send', suchen: 'search', aktualisieren: 'arrow-repeat',
  abwesend: 'person-x', hausaufgabe: 'pencil-square', kopieren: 'clipboard', kalender: 'calendar-event', stift: 'pencil',
  augen: 'eye', protokoll: 'list-ul', plaene: 'calendar-week', klasse: 'people', lehrkraft: 'person-workspace',
  tagklassen: 'calendar-range', taglehrer: 'person-lines-fill', tagraeume: 'building', schloss: 'lock-fill',
  // Ersatz aus Bootstrap Icons, falls symbole.js kein Original-Icon zuordnet
  dashboard: 'grid', uebersicht: 'card-list', mitteilungen: 'chat-square-text', posteingang: 'inbox', gesendet: 'send',
  stundenplan: 'calendar3', meinplan: 'calendar2-week', raumplan: 'door-closed', unterricht: 'easel', meineunterrichte: 'easel2',
  offen: 'hourglass-split', pruefung: 'file-earmark-text', klassenbuchnav: 'journal-bookmark', eintraege: 'journal-text',
  hausaufgaben: 'pencil', abwesenheiten: 'person-x', fehlzeiten: 'clock-history', berichte: 'file-earmark-bar-graph',
  meldenbuchen: 'clipboard-plus', verwaltung: 'gear', nachrichten: 'megaphone', zurueck: 'arrow-left', einklappen: 'chevron-double-left',
  csv: 'filetype-csv', filter: 'funnel', woche: 'calendar-week', vertretungen: 'arrow-left-right', synchron: 'arrow-repeat',
  aufsicht: 'eye', ruckmeldung: 'inbox',
};

/** Symbol: Original-Icons (symbole.js, img/wu), sonst Bootstrap Icons für kleine Bedienelemente. */
export function icon(name, klasse = '') {
  return symbol(name, klasse) ?? `<i class="bi bi-${SYMBOLE[name] ?? name} ${klasse === 'icon-klein' ? '' : klasse}" aria-hidden="true"></i>`;
}

export function e(wert) {
  return String(wert ?? '').replace(/[&<>"']/g, z => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[z]));
}

/** Leerzustand (mit Symbol), Ladeanzeige und Hinweisbox. */
export const leer = (text, symbol = 'inbox') => `<div class="u-leer"><i class="bi bi-${SYMBOLE[symbol] ?? symbol}" aria-hidden="true"></i>${e(text)}</div>`;
/** Einklappbarer Hinweis als (i)-Knopf in einer Filterzeile (statt eines breiten Hinweistexts); html: vertrauenswürdiger, fester Text. */
export const infoHinweis = (html, titel = 'Hinweis') => `<details class="nl-info"><summary title="${e(titel)}" aria-label="${e(titel)} anzeigen"><i class="bi bi-info-circle" aria-hidden="true"></i></summary><div class="nl-info-text" role="note">${html}</div></details>`;
// aufgeklappter (i)-Hinweis bleibt im Fenster (rechts am Rand nach links versetzt)
document.addEventListener('toggle', ev => {
  const d = ev.target;
  if (!(d instanceof HTMLDetailsElement) || !d.classList.contains('nl-info') || !d.open) return;
  const t = d.querySelector('.nl-info-text');
  t.style.left = '';
  const ueber = t.getBoundingClientRect().right - (window.innerWidth - 16);
  if (ueber > 0) t.style.left = `${-8 - ueber}px`;
}, true);
/** Platzhalter während des Ladens – bewusst ohne Animation (die Inhalte erscheinen direkt). */
export const lade = () => '<div class="u-laedt" role="status"><span class="visually-hidden">Wird geladen …</span></div>';
export const alarm = (art, text, html = false) => `<div class="alert alert-${art} py-2 px-3 small mb-3" role="alert">${html ? text : e(text)}</div>`;

// ------------------------------------------------------------------ Schnittstelle

export class ApiFehler extends Error {
  constructor(status, meldung) { super(meldung); this.status = status; }
}

export async function api(pfad, { methode = 'GET', daten, formular } = {}) {
  const optionen = { method: methode, headers: { 'X-UntPlanWeb': '1' }, credentials: 'same-origin' };
  if (formular) optionen.body = formular;
  else if (daten !== undefined) { optionen.headers['Content-Type'] = 'application/json'; optionen.body = JSON.stringify(daten); }
  let antwort;
  try { antwort = await fetch(pfad, optionen); }
  catch { throw new ApiFehler(0, 'Keine Verbindung zum Server.'); }
  const text = await antwort.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  if (!antwort.ok) {
    if (antwort.status === 401 && !pfad.endsWith('/anmelden')) window.dispatchEvent(new CustomEvent('untplan:abgemeldet'));
    throw new ApiFehler(antwort.status, json?.fehler ?? `Fehler ${antwort.status}`);
  }
  return json;
}

export const holen = pfad => api(pfad);
export const senden = (pfad, daten, methode = 'POST') => api(pfad, { methode, daten });

// ------------------------------------------------------------------ Meldungen und Dialoge

export function toast(text, fehler = false) {
  const leiste = document.getElementById('toasts');
  const t = document.createElement('div');
  t.className = `toast align-items-center ${fehler ? 'fehler' : ''}`;
  t.setAttribute('role', fehler ? 'alert' : 'status');
  t.innerHTML = `<div class="d-flex align-items-center"><div class="toast-body"><i class="bi bi-${fehler ? 'exclamation-circle' : 'check-circle'}" aria-hidden="true"></i>${e(text)}</div><button type="button" class="btn-close" data-bs-dismiss="toast" aria-label="Schließen"></button></div>`;
  leiste.append(t);
  const b = new bootstrap.Toast(t, { delay: fehler ? 6000 : 3000 });
  t.addEventListener('hidden.bs.toast', () => t.remove());
  b.show();
}

export function fehler(err) { toast(err?.message ?? String(err), true); }

const KNOPFART = { haupt: 'btn-primary', gefahr: 'btn-danger' };

/**
 * Bootstrap-Modal öffnen. inhalt: HTML; knoepfe: [{ text, art: 'haupt'|'gefahr', aktion(box) → false = offen lassen }].
 * Liefert das Inhaltselement (.modal-content); schliessen() schließt.
 */
export function dialog({ titel, inhalt, knoepfe = [], breit = false, beimOeffnen }) {
  const el = document.createElement('div');
  el.className = 'modal fade';
  el.tabIndex = -1;
  el.innerHTML = `<div class="modal-dialog modal-dialog-scrollable ${breit ? 'modal-lg' : ''}"><div class="modal-content">
      <div class="modal-header"><h5 class="modal-title">${e(titel)}</h5><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Schließen"></button></div>
      <div class="modal-body">${inhalt}</div>
      ${knoepfe.length ? `<div class="modal-footer">${knoepfe.map((k, i) => `<button type="button" class="btn ${KNOPFART[k.art] ?? 'btn-outline-secondary'}" data-knopf="${i}">${k.text}</button>`).join('')}</div>` : ''}
    </div></div>`;
  document.body.append(el);
  const modal = new bootstrap.Modal(el);
  const box = el.querySelector('.modal-content');
  box.schliessen = () => modal.hide();
  el.addEventListener('hidden.bs.modal', () => setTimeout(() => { modal.dispose(); el.remove(); }));
  el.addEventListener('shown.bs.modal', () => box.querySelector('.modal-body input:not([type=checkbox]), .modal-body select, .modal-body textarea, .modal-footer .btn-primary')?.focus());
  knoepfe.forEach((k, i) => {
    el.querySelector(`[data-knopf="${i}"]`).onclick = async ev => {
      const btn = ev.currentTarget;
      btn.disabled = true;
      try { if ((await k.aktion?.(box)) !== false) modal.hide(); }
      catch (err) { fehler(err); }
      finally { btn.disabled = false; }
    };
  });
  beimOeffnen?.(box);
  // Über einem bereits offenen Fenster (z. B. Stundendetails): Fenster und abgedunkelter Hintergrund darüber legen,
  // damit der Hintergrund wie bei allen Dialogen dunkel ist und nicht hinter dem ersten Fenster verschwindet.
  const offen = [...document.querySelectorAll('.modal.show')].filter(m => m !== el);
  if (offen.length) {
    const z = Math.max(1055, ...offen.map(m => Number(getComputedStyle(m).zIndex) || 0)) + 2;
    el.style.zIndex = z + 1;
    el.addEventListener('show.bs.modal', () => setTimeout(() => { const bd = [...document.querySelectorAll('.modal-backdrop')].pop(); if (bd) bd.style.zIndex = z; }), { once: true });
    el.addEventListener('shown.bs.modal', () => { const bd = [...document.querySelectorAll('.modal-backdrop')].pop(); if (bd) bd.style.zIndex = z; }, { once: true });
  }
  modal.show();
  return box;
}

export function bestaetigen(titel, text, knopf = 'Löschen') {
  return new Promise(ok => {
    let antwort = false;
    const box = dialog({ titel, inhalt: `<p class="mb-0">${e(text)}</p>`, knoepfe: [
      { text: 'Abbrechen' },
      { text: knopf, art: knopf === 'Löschen' ? 'gefahr' : 'haupt', aktion: () => { antwort = true; } },
    ] });
    box.closest('.modal').addEventListener('hidden.bs.modal', () => ok(antwort));
  });
}

/** Werte eines Formulars als Objekt (Checkboxen als bool, leere Zahlen als null). */
export function formularWerte(wurzel) {
  const daten = {};
  wurzel.querySelectorAll('[name]').forEach(f => {
    if (f.type === 'checkbox') daten[f.name] = f.checked;
    else if (f.type === 'number') daten[f.name] = f.value === '' ? null : Number(f.value);
    else daten[f.name] = f.value;
  });
  return daten;
}

// ------------------------------------------------------------------ Datum

export const WOCHENTAGE = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
export const WOCHENTAGE_KURZ = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

export function iso(d) {
  const z = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${z(d.getMonth() + 1)}-${z(d.getDate())}`;
}
export function ausIso(s) { const [j, m, t] = s.split('-').map(Number); return new Date(j, m - 1, t); }
export function heuteIso() { return iso(new Date()); }
export function plusTage(s, n) { const d = ausIso(s); d.setDate(d.getDate() + n); return iso(d); }
export function montag(s) { const d = ausIso(s); const t = (d.getDay() + 6) % 7; d.setDate(d.getDate() - t); return iso(d); }
export function datumKurz(s) { const d = ausIso(s); return `${d.getDate()}.${d.getMonth() + 1}.`; }
export function datumLang(s) { const d = ausIso(s); return `${WOCHENTAGE[(d.getDay() + 6) % 7]}, ${d.getDate()}.${d.getMonth() + 1}.${d.getFullYear()}`; }
export function minuten(hhmm) { if (!hhmm) return 0; const [h, m] = hhmm.split(':').map(Number); return h * 60 + m; }
export function jetztMinuten() { const d = new Date(); return d.getHours() * 60 + d.getMinutes(); }

export function initialen(text) {
  const teile = String(text || '?').trim().split(/\s+/);
  return (teile.length > 1 ? teile[0][0] + teile[teile.length - 1][0] : teile[0].slice(0, 2)).toUpperCase();
}

/** Text einer Rückmeldung für Listen. */
export function meldungText(m) {
  const std = m.stundeVon ? ` (${m.stundeVon}.${m.stundeBis && m.stundeBis !== m.stundeVon ? '–' + m.stundeBis + '.' : ''} Std.)` : '';
  switch (m.typ) {
    case 'absenz': return `Abwesenheit ${datumKurz(m.von)}${m.bis && m.bis !== m.von ? ' – ' + datumKurz(m.bis) : ''}${std}${m.grund ? ' · ' + m.grund : ''}`;
    case 'raumbuchung': return `Raumbuchung ${m.raum} am ${datumKurz(m.von)}${std}${m.titel ? ' · ' + m.titel : ''}`;
    case 'klausur': return `Klausur ${(m.klassen || []).join(', ')} am ${datumKurz(m.von)}${std}${m.titel ? ' · ' + m.titel : ''}`;
    case 'klausurloeschen': return `Prüfung löschen: ${(m.klassen || []).join(', ')} am ${datumKurz(m.von)}${std}${m.titel ? ' · ' + m.titel : ''}`;
    default: return `Nachricht ${m.von ? datumKurz(m.von) : ''}: ${m.text ?? ''}`;
  }
}

/** Datei im Browser speichern (z. B. CSV mit BOM für Excel). */
export function herunterladen(name, inhalt, typ = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(inhalt instanceof Blob ? inhalt : new Blob(['\uFEFF' + inhalt], { type: typ }));
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

/** Datumskachel für Listen (Tag und Wochentag). */
export function datumKachel(isoDatum) {
  const d = ausIso(isoDatum);
  return `<div class="u-datum ${isoDatum === heuteIso() ? 'heute' : ''}"><small>${WOCHENTAGE_KURZ[(d.getDay() + 6) % 7]}</small><b>${d.getDate()}</b><small>${['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'][d.getMonth()]}</small></div>`;
}

/**
 * Einheitliches Suchfeld (gemeinsame Komponente .u-suchfeld): großes graues Feld mit Lupe links, runden Ecken, Löschen-X rechts
 * (ergänzt auswahl.js automatisch) und dezentem Fokus-Ring. attr: zusätzliche Attribute als Text (z. B. 'data-suche'), klasse: Zusatzklassen.
 */
export const suchfeld = ({ platzhalter = 'Suchen', wert = '', attr = 'data-suche', label = '', klasse = '' } = {}) =>
  `<label class="u-suchfeld ${klasse}"><i class="bi bi-search" aria-hidden="true"></i><input type="search" ${attr} value="${e(wert)}" placeholder="${e(platzhalter)}" aria-label="${e(label || platzhalter)}" autocomplete="off"></label>`;

/**
 * Suchfeld mit Filter (gemeinsame Komponente .u-suchfilter, Muster der Hausaufgaben): graues, rundes Feld mit Lupe, Chips der gewählten
 * Filter, Eingabe, Löschen-X (ergänzt auswahl.js) und rechts – durch einen Strich getrennt – ein oder mehrere Dropdowns (native Selects,
 * die auswahl.js zur einheitlichen Auswahl mit Tastatur ↑ ↓ Enter Esc aufwertet). Ein gewählter Filter (Wert ≠ Standard) erscheint als dunkler
 * Chip mit x zum Zurücksetzen. Verhalten bindet suchfeldMitFilterBinden().
 * filter: [{ name, label, optionen: [[wert, text]], wert, standard = '', alle = 'Alle' (Text der Standard-Option; null = keine), attr }]
 * chips: [{ wert, text }] – zusätzliche entfernbare Chips vor der Eingabe (z. B. gewählte Lehrkräfte), Klick auf x → beiChipWeg(wert).
 */
export const suchfeldMitFilter = ({ platzhalter = 'Suchen', wert = '', attr = 'data-suche', label = '', klasse = '', filter = [], chips = [], zusatz = '' } = {}) =>
  `<div class="u-suchfilter ${filter.length > 1 ? 'mehrere' : ''} ${klasse}" style="--sf-n:${filter.length}" data-suchfilter>${icon('suchen')}${chips.map(c =>
    `<span class="u-sf-chip">${e(c.text)}<button type="button" data-chip-weg="${e(c.wert)}" aria-label="Filter ${e(c.text)} entfernen"><i class="bi bi-x" aria-hidden="true"></i></button></span>`).join('')}<input type="search" ${attr} value="${e(wert)}" placeholder="${e(platzhalter)}" aria-label="${e(label || platzhalter)}" autocomplete="off">${filter.map(f => {
    const standard = String(f.standard ?? '');
    const w = String(f.wert ?? standard);
    const optionen = [...(f.alle === null ? [] : [[standard, f.alle ?? 'Alle']]), ...f.optionen];
    const gewaehlt = w !== standard;
    return `<span class="u-trenn" aria-hidden="true"></span><span class="u-sf-filter ${gewaehlt ? 'gewaehlt' : ''}"><select data-sf="${e(f.name)}" ${f.attr ?? ''} data-standard="${e(standard)}" aria-label="${e(f.label ?? f.name)}">${optionen.map(([ow, ot]) =>
      `<option value="${e(ow)}" ${String(ow) === w ? 'selected' : ''}>${e(ot)}</option>`).join('')}</select><button type="button" class="u-sf-weg" data-sf-weg aria-label="Filter ${e(f.label ?? f.name)} zurücksetzen" ${gewaehlt ? '' : 'hidden'}><i class="bi bi-x" aria-hidden="true"></i></button></span>`;
  }).join('')}${zusatz}</div>`;

/**
 * Verhalten des Suchfelds mit Filter: Eingabe → beiSuche(text), Dropdown → beiFilter(name, wert), x am Filter-Chip setzt auf den Standard
 * zurück, x an einem Chip → beiChipWeg(wert), Rücktaste im leeren Feld entfernt den letzten Chip bzw. Filter, Klick auf die Fläche fokussiert.
 * Liefert { feld, input, wert(name), setzen(name, wert) }.
 */
export function suchfeldMitFilterBinden(wurzel, { beiSuche, beiFilter, beiChipWeg } = {}) {
  const feld = wurzel?.matches?.('[data-suchfilter]') ? wurzel : wurzel?.querySelector('[data-suchfilter]');
  if (!feld) return null;
  const input = feld.querySelector('input[type=search]');
  const selects = [...feld.querySelectorAll('select[data-sf]')];
  const markieren = sel => {
    const an = sel.value !== sel.dataset.standard;
    sel.closest('.u-sf-filter')?.classList.toggle('gewaehlt', an);
    const x = sel.closest('.u-sf-filter')?.querySelector('[data-sf-weg]');
    if (x) x.hidden = !an;
  };
  selects.forEach(sel => sel.addEventListener('change', () => { markieren(sel); beiFilter?.(sel.dataset.sf, sel.value, sel); }));
  feld.querySelectorAll('[data-sf-weg]').forEach(x => x.addEventListener('click', ev => {
    ev.stopPropagation();
    const sel = x.closest('.u-sf-filter').querySelector('select');
    sel.value = sel.dataset.standard;
    sel.dispatchEvent(new Event('change', { bubbles: true }));
  }));
  feld.querySelectorAll('[data-chip-weg]').forEach(b => b.addEventListener('click', ev => { ev.stopPropagation(); beiChipWeg?.(b.dataset.chipWeg); }));
  input?.addEventListener('input', () => beiSuche?.(input.value.trim()));
  input?.addEventListener('keydown', ev => {
    if (ev.key !== 'Backspace' || input.value) return;
    const chips = feld.querySelectorAll('[data-chip-weg]');
    const filterX = [...feld.querySelectorAll('[data-sf-weg]:not([hidden])')];
    if (chips.length && beiChipWeg) { ev.preventDefault(); beiChipWeg(chips[chips.length - 1].dataset.chipWeg); }
    else if (filterX.length) { ev.preventDefault(); filterX[filterX.length - 1].click(); input.focus(); }
  });
  feld.addEventListener('click', ev => { if (ev.target === feld || ev.target.matches('.bi-search, .u-sym, .u-trenn')) input?.focus(); });
  return {
    feld, input,
    wert: name => feld.querySelector(`select[data-sf="${name}"]`)?.value ?? '',
    setzen: (name, w) => { const sel = feld.querySelector(`select[data-sf="${name}"]`); if (sel) { sel.value = w; markieren(sel); } },
  };
}

/** Zeitraum als zwei Datums-Pillen (data-von, data-bis) neben dem Suchfeld mit Filter. */
export const zeitraumPillen = (von, bis) => ['Von', 'Bis'].map((t, i) =>
  `<label class="nl-pille u-sf-datum"><span class="visually-hidden">${t}</span><input type="date" data-${t.toLowerCase()} value="${e(i ? bis : von)}" aria-label="${t}"></label>`).join('');

/** Freitextsuche in einer gerenderten Liste: blendet Zeilen ohne Treffer aus (alle Wörter müssen vorkommen); Ergebnis: Anzahl sichtbarer Zeilen. */
export function zeilenFiltern(zeilen, text) {
  const woerter = String(text ?? '').toLowerCase().split(/\s+/).filter(Boolean);
  let n = 0;
  for (const z of zeilen) {
    const t = (z.dataset.suchtext ?? z.textContent).toLowerCase();
    z.hidden = woerter.length > 0 && !woerter.every(w => t.includes(w));
    if (!z.hidden) n++;
  }
  return n;
}

/**
 * Segment-Auswahl (gemeinsame Komponente .u-segment): Pillen in einer grauen Schiene, die gewählte weiß hervorgehoben.
 * optionen: [[wert, text, { symbol, titel, klasse }]]; attr: Attribut je Knopf (Wert steht in data-wert). Für wenige, gleichrangige Optionen
 * (Art der Anzeige, Rechte-Stufen) statt eines Dropdowns.
 */
export const segment = (optionen, wahl, { attr = 'data-segment', label = '', klasse = '', deaktiviert = false } = {}) =>
  `<div class="u-segment ${klasse}" role="group" ${label ? `aria-label="${e(label)}"` : ''}>${optionen.map(([w, t, o = {}]) =>
    `<button type="button" class="${String(w) === String(wahl) ? 'an' : ''} ${o.klasse ?? ''}" ${attr} data-wert="${e(w)}" aria-pressed="${String(w) === String(wahl)}" ${o.titel ? `title="${e(o.titel)}"` : ''} ${deaktiviert ? 'disabled' : ''}>${o.symbol ? `<i class="bi bi-${o.symbol}" aria-hidden="true"></i>` : ''}<span>${e(t)}</span></button>`).join('')}</div>`;

/** Gewählte Pille eines Segments setzen (ohne neu zu zeichnen). */
export function segmentSetzen(wurzel, wert) {
  wurzel?.querySelectorAll('button[data-wert]').forEach(b => { const an = b.dataset.wert === String(wert); b.classList.toggle('an', an); b.setAttribute('aria-pressed', String(an)); });
}

/** Feld in einer Filterleiste: Beschriftung über dem Steuerelement. */
export const filterFeld = (beschriftung, html) => `<label class="u-feld"><span>${e(beschriftung)}</span>${html}</label>`;

/** Bereich (Panel) mit Kopf. */
export const panel = (titel, inhalt, { symbol = '', rechts = '', klasse = '' } = {}) =>
  `<section class="u-panel ${klasse}"><header>${symbol ? icon(symbol) : ''}<h2>${e(titel)}</h2>${rechts ? `<div class="ms-auto d-flex gap-2 align-items-center">${rechts}</div>` : ''}</header>${inhalt}</section>`;

/** Stunden als Text („3.“ bzw. „3.–4.“). */
export const stundenText = (von, bis) => !von ? '' : von === bis || !bis ? `${von}.` : `${von}.–${bis}.`;

export function statusMarker(status) {
  return { offen: '<span class="u-chip gelb">offen</span>', quittiert: '<span class="u-chip gruen">übernommen</span>', abgelehnt: '<span class="u-chip rot">abgelehnt</span>' }[status] ?? `<span class="u-chip">${e(status)}</span>`;
}

/**
 * Lehrkraft als Label außerhalb der Karten (Stundendetails-Kopf, Startseite) – einheitlich mit den Plankarten:
 * offene Vertretung bzw. Vertretung ohne Lehrkraft ({ k: "", alt: "FIS" }) → altes Kürzel durchgestrichen im grünen Label;
 * eingeteilte Vertretung ({ k: "BEC", alt: "FIS" }) → „FIS“ grau durchgestrichen | „BEC“ als grünes Label; zusätzliche Lehrkraft → grünes Label.
 * inhalt(kuerzel) liefert den (ggf. verlinkten) Text; ohne inhalt wird nur escaped.
 */
export function lehrkraftLabel(w, inhalt = x => e(x), trenn = '|') {
  if (!w.k && w.alt) return `<span class="v-lbl weg">${inhalt(w.alt)}</span>`;
  if (!w.k) return w.neu ? '<span class="v-lbl">???</span>' : '';
  // Lehrkraft-Wechsel „LAN | ILK“, Raum/Fach „B125 → A300“ (trenn = '→')
  if (w.alt) return `<s class="v-alt">${inhalt(w.alt)}</s> <span class="v-trenn">${trenn}</span> <span class="v-lbl" data-hover="${e(`${w.alt} → ${w.k}`)}" data-hover-art="dunkel">${inhalt(w.k)}</span>`;
  if (w.neu) return `<span class="v-lbl">${inhalt(w.k)}</span>`;
  return inhalt(w.k);
}

// ------------------------------------------------------------------ Hover für abgeschnittene Inhalte

/**
 * Grauer Hover mit dem vollständigen Text, wenn ein Inhalt nicht auf seine Fläche passt (Plankarten mit abgeschnittenen Zeilen,
 * Veranstaltungstexte, mit „…“ gekürzte Zeilen in Listen und Tabellen) – nur wenn tatsächlich abgeschnitten, nur mit Maus.
 * Elemente mit eigenem Hover (data-hover) bzw. title behalten ihren Hinweis.
 */
const kuerzungsHover = (() => {
  if (typeof window === 'undefined' || !window.matchMedia?.('(hover: hover)').matches) return null;
  let tip = null, ziel = null, timer = 0;
  // Nur Text-Elemente messen: Karten und ihre Zeilen, Labels, Veranstaltungs-/Ferientexte, Plan-Hinweise – nie Raster-Container
  // (Plan-Gitter, Zeitspalte, Tageskopf, Spalten, Listen), deren Fläche bewusst beschnitten ist.
  const TEXT = '.std, .std .z1, .std .z2, .std .z3, .lbl, .v-lbl, .tt-ferien .inhalt, .tt-frei, .tt-ereignis, [data-kuerzung]';
  const RASTER = '.tt, .tt-achse, .tt-spalte, .tt-tag, .tt-ecke, .tt-leiste, .tt-ferien, .tt-liste, .eintrag, .tagplan, .tagplan-gitter, .tagplan-zelle, .tagplan-kopf, .tt-tagwahl, main, .modal-body, .modal-content';
  const abgeschnitten = el => {
    if (el.matches(RASTER)) return false;
    const s = getComputedStyle(el);
    if (s.display === 'inline') return false;
    const ellipse = s.textOverflow === 'ellipsis';
    const klammer = !!(s.webkitLineClamp && s.webkitLineClamp !== 'none');
    if (!ellipse && !klammer && !el.matches(TEXT)) return false;
    const x = (s.overflowX === 'hidden' || s.overflowX === 'clip' || ellipse) && el.scrollWidth > el.clientWidth + 1;
    const y = (s.overflowY === 'hidden' || s.overflowY === 'clip' || klammer) && el.scrollHeight > el.clientHeight + 2;
    return x || y;
  };
  const verbergen = () => { clearTimeout(timer); if (tip) tip.style.display = 'none'; ziel = null; };
  const zeigen = (el, text) => {
    if (!tip) { tip = document.createElement('div'); tip.className = 'u-kuerzung-hover'; tip.setAttribute('role', 'tooltip'); document.body.append(tip); }
    tip.textContent = text;
    tip.style.display = 'block';
    const r = el.getBoundingClientRect(), b = tip.offsetWidth, h = tip.offsetHeight;
    const links = Math.max(6, Math.min(window.innerWidth - b - 6, r.left));
    const oben = r.bottom + 6 + h > window.innerHeight - 6 ? Math.max(6, r.top - h - 6) : r.bottom + 6;
    tip.style.left = `${Math.round(links)}px`;
    tip.style.top = `${Math.round(oben)}px`;
  };
  document.addEventListener('mouseover', ev => {
    const t = ev.target;
    // Stundenkarten (.std): eigener dunkelgrauer Hover je gekürzter Zeile (plangitter.js) – hier keinen zweiten, hellen zeigen
    if (!(t instanceof Element) || t.closest('[data-hover], [data-hover-pillen], [title], input, textarea, select, .modal-footer, .u-kuerzung-hover, .std')) { if (ziel && !ziel.contains(t)) verbergen(); return; }
    if (ziel && ziel.contains(t)) return;
    // vom überfahrenen Element bis zu vier Ebenen nach oben das erste abgeschnittene suchen
    let el = t, treffer = null;
    for (let i = 0; el && i < 5 && el !== document.body; i++, el = el.parentElement) {
      const s = getComputedStyle(el);
      if (s.overflowY === 'auto' || s.overflowY === 'scroll' || s.position === 'fixed' || el.matches(RASTER)) break;
      if (abgeschnitten(el)) { treffer = el; break; }
    }
    if (!treffer) { verbergen(); return; }
    const text = (treffer.innerText || '').replace(/[ \t]+/g, ' ').replace(/\n\s*\n+/g, '\n').trim();
    if (!text || text.length > 600) { verbergen(); return; }
    verbergen();
    ziel = treffer;
    timer = setTimeout(() => { if (ziel === treffer) zeigen(treffer, text); }, 350);
  });
  document.addEventListener('scroll', verbergen, true);
  document.addEventListener('mousedown', verbergen, true);
  return { verbergen };
})();
export const hoverVerbergen = () => kuerzungsHover?.verbergen();

// ------------------------------------------------------------------ Formulare im Hoch- bzw. Querformat

/**
 * Auswahl Hoch-/Querformat vor dem Erstellen eines PDF-Formulars (auch aus einem offenen Dialog heraus). Liefert „hoch“ bzw. „quer“,
 * null bei Abbruch. Die letzte Wahl je Formular wird im Browser gemerkt.
 */
export function pdfAusrichtung(vorgabe = 'quer', schluessel = '') {
  let gemerkt = null;
  try { gemerkt = schluessel ? localStorage.getItem(`untplan:pdf:${schluessel}`) : null; } catch { /* privat */ }
  let wahl = gemerkt === 'hoch' || gemerkt === 'quer' ? gemerkt : vorgabe;
  return new Promise(ok => {
    const el = document.createElement('div');
    el.className = 'u-pdf-wahl';
    el.setAttribute('role', 'dialog');
    el.setAttribute('aria-modal', 'true');
    el.setAttribute('aria-label', 'Format wählen');
    el.innerHTML = `<div class="u-pdf-wahl-box">
        <div class="u-pdf-wahl-titel">Format des Formulars</div>
        <div class="u-pdf-wahl-optionen">${[['hoch', 'Hochformat'], ['quer', 'Querformat']].map(([w, t]) => `
          <button type="button" class="u-pdf-option ${w === wahl ? 'aktiv' : ''}" data-wahl="${w}" aria-pressed="${w === wahl}"><span class="u-pdf-blatt ${w}"><i></i><i></i><i></i></span>${t}${w === vorgabe ? '<small>empfohlen</small>' : ''}</button>`).join('')}</div>
        <div class="u-pdf-wahl-fuss"><button type="button" class="btn btn-outline-secondary" data-abbrechen>Abbrechen</button><button type="button" class="btn btn-primary" data-ok>PDF erstellen</button></div>
      </div>`;
    const fertig = w => {
      el.remove();
      document.removeEventListener('keydown', taste, true);
      if (w && schluessel) { try { localStorage.setItem(`untplan:pdf:${schluessel}`, w); } catch { /* privat */ } }
      ok(w);
    };
    const taste = ev => { if (ev.key === 'Escape') { ev.stopPropagation(); fertig(null); } else if (ev.key === 'Enter') { ev.preventDefault(); fertig(wahl); } };
    el.querySelectorAll('[data-wahl]').forEach(b => {
      b.onclick = () => { wahl = b.dataset.wahl; el.querySelectorAll('[data-wahl]').forEach(x => { x.classList.toggle('aktiv', x === b); x.setAttribute('aria-pressed', x === b); }); };
      b.ondblclick = () => fertig(b.dataset.wahl);
    });
    el.querySelector('[data-abbrechen]').onclick = () => fertig(null);
    el.querySelector('[data-ok]').onclick = () => fertig(wahl);
    el.addEventListener('mousedown', ev => { if (ev.target === el) fertig(null); });
    document.addEventListener('keydown', taste, true);
    document.body.append(el);
    el.querySelector('[data-ok]').focus();
  });
}

/** Adresse um die Ausrichtung ergänzen. */
export const mitAusrichtung = (url, ausrichtung) => `${url}${url.includes('?') ? '&' : '?'}ausrichtung=${ausrichtung}`;

// Links auf PDF-Formulare der Schnittstelle (…/plan.pdf, …?format=pdf): vor dem Öffnen Hoch-/Querformat wählen.
// Vorgabe über data-pdf="hoch|quer" am Link, sonst Querformat.
if (typeof document !== 'undefined') document.addEventListener('click', async ev => {
  const a = ev.target instanceof Element ? ev.target.closest('a[href]') : null;
  if (!a || ev.defaultPrevented || ev.button !== 0 || ev.ctrlKey || ev.metaKey || ev.shiftKey) return;
  const href = a.getAttribute('href') || '';
  if (!href.startsWith('/api/app/') || !/(\.pdf(\?|$))|[?&]format=pdf\b/.test(href) || /[?&]ausrichtung=/.test(href)) return;
  ev.preventDefault();
  const pfad = href.split('?')[0].replace(/\/\d+(?=\/|$)/g, '');
  const w = await pdfAusrichtung(a.dataset.pdf === 'hoch' ? 'hoch' : 'quer', pfad);
  if (!w) return;
  const ziel = mitAusrichtung(href, w);
  if (a.hasAttribute('download')) { const x = Object.assign(document.createElement('a'), { href: ziel, download: a.getAttribute('download') || '' }); document.body.append(x); x.click(); x.remove(); }
  else window.open(ziel, a.target || '_self', a.target === '_blank' ? 'noopener' : '');
});

/** Browserdruck (z. B. Stundenplan) im gewählten Format: setzt @page für diesen Druck. */
export async function druckenMitAusrichtung(vorgabe = 'quer', schluessel = 'druck') {
  const w = await pdfAusrichtung(vorgabe, schluessel);
  if (!w) return false;
  const st = document.createElement('style');
  st.textContent = `@page { size: A4 ${w === 'hoch' ? 'portrait' : 'landscape'}; }`;
  document.head.append(st);
  document.body.classList.toggle('druck-hoch', w === 'hoch');
  const weg = () => { st.remove(); document.body.classList.remove('druck-hoch'); window.removeEventListener('afterprint', weg); };
  window.addEventListener('afterprint', weg);
  window.print();
  setTimeout(weg, 1500);
  return true;
}
