// Gemeinsame Hilfen: Schnittstelle, Escaping, Symbole (Bootstrap Icons), Meldungen und Dialoge (Bootstrap), Datumsfunktionen.

/** Bildmarke: schlichtes Raster-Symbol. */
export const LOGO = `<svg class="logo" viewBox="0 0 32 32" aria-hidden="true"><rect x="1" y="1" width="30" height="30" rx="6" fill="#1d4f73"/><path d="M8 7h5v15H8zM19 7h5v15h-5z" fill="#fff"/><rect x="8" y="18" width="16" height="7" rx="3" fill="#fff"/><rect x="14" y="7" width="4" height="4" rx="1" fill="#5fbf96"/></svg>`;

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
};

export function icon(name, klasse = '') {
  return `<i class="bi bi-${SYMBOLE[name] ?? name} ${klasse === 'icon-klein' ? '' : klasse}" aria-hidden="true"></i>`;
}

export function e(wert) {
  return String(wert ?? '').replace(/[&<>"']/g, z => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[z]));
}

/** Leerzustand, Ladeanzeige und Hinweisbox in Bootstrap-Optik. */
export const leer = text => `<div class="text-center text-body-secondary py-4 small">${e(text)}</div>`;
export const lade = () => '<div class="d-flex justify-content-center py-5"><div class="spinner-border spinner-border-sm text-secondary" role="status"><span class="visually-hidden">Wird geladen …</span></div></div>';
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
  t.className = `toast align-items-center border-0 ${fehler ? 'text-bg-danger' : 'text-bg-dark'}`;
  t.setAttribute('role', fehler ? 'alert' : 'status');
  t.innerHTML = `<div class="d-flex"><div class="toast-body">${e(text)}</div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Schließen"></button></div>`;
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
    default: return `Nachricht ${m.von ? datumKurz(m.von) : ''}: ${m.text ?? ''}`;
  }
}

export function statusMarker(status) {
  return { offen: '<span class="badge text-bg-warning">offen</span>', quittiert: '<span class="badge text-bg-success">übernommen</span>', abgelehnt: '<span class="badge text-bg-danger">abgelehnt</span>' }[status] ?? `<span class="badge text-bg-secondary">${e(status)}</span>`;
}
