// Gemeinsame Hilfen: Zugriff auf die Schnittstelle, Escaping, Symbole, Meldungen, Dialoge, Datumsfunktionen.

export const LOGO = `<svg viewBox="0 0 40 40" aria-hidden="true"><rect width="40" height="40" rx="10" fill="#125e7a"/><rect x="9" y="9" width="10" height="10" rx="2.5" fill="#fff"/><rect x="21" y="9" width="10" height="10" rx="2.5" fill="#fff" fill-opacity=".55"/><rect x="9" y="21" width="10" height="10" rx="2.5" fill="#fff" fill-opacity=".55"/><rect x="21" y="21" width="10" height="10" rx="2.5" fill="#4fd1a1"/></svg>`;

// Eigene Liniensymbole (24er Raster)
const PFADE = {
  heute: '<path d="M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1z"/>',
  plan: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4M7.5 14h2M11 14h2M14.5 14h2M7.5 17.5h2M11 17.5h2"/>',
  tag: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18M9 9v11M15 9v11"/>',
  vertretung: '<path d="M4 8h13l-3.5-3.5M20 16H7l3.5 3.5"/>',
  mitteilung: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3.5 7l8.5 6 8.5-6"/>',
  klassenbuch: '<path d="M5 5a2 2 0 0 1 2-2h11v15H7a2 2 0 0 0-2 2z"/><path d="M5 20a2 2 0 0 0 2 1h11v-3M9 7h5"/>',
  melden: '<rect x="5" y="4" width="14" height="17" rx="2"/><path d="M9 4V3h6v1M12 10v6M9 13h6"/>',
  admin: '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M9 12l2 2 4-4"/>',
  profil: '<circle cx="12" cy="8" r="4"/><path d="M4 21c1-4 4-6 8-6s7 2 8 6"/>',
  abmelden: '<path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 17l5-5-5-5M15 12H4"/>',
  menue: '<path d="M4 6h16M4 12h16M4 18h16"/>',
  links: '<path d="M15 6l-6 6 6 6"/>',
  rechts: '<path d="M9 6l6 6-6 6"/>',
  unten: '<path d="M6 9l6 6 6-6"/>',
  drucken: '<path d="M7 9V4h10v5M7 18H5a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="7" y="14" width="10" height="7" rx="1"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  loeschen: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6"/>',
  haken: '<path d="M5 12l5 5 9-10"/>',
  x: '<path d="M6 6l12 12M18 6L6 18"/>',
  monitor: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
  schluessel: '<circle cx="8" cy="15" r="4"/><path d="M11 12l9-9M17 6l3 3M14.5 8.5l2 2"/>',
  hochladen: '<path d="M12 16V4M7 9l5-5 5 5M4 20h16"/>',
  herunterladen: '<path d="M12 4v12M7 11l5 5 5-5M4 20h16"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  warnung: '<path d="M12 3l10 18H2z"/><path d="M12 10v4M12 17h.01"/>',
  benutzer: '<circle cx="9" cy="8" r="4"/><path d="M2 21c1-4 4-6 7-6s6 2 7 6M16 4a4 4 0 0 1 0 8M22 21c-.5-3-2-5-4.5-6"/>',
  uhr: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  raum: '<path d="M6 21V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v17M3 21h18M14.5 12h.01"/>',
  klausur: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M9 15l2 2 4-4"/>',
  senden: '<path d="M4 12l16-8-6 16-3-7z"/><path d="M11 13l9-9"/>',
  suchen: '<circle cx="11" cy="11" r="7"/><path d="M20 20l-4-4"/>',
  aktualisieren: '<path d="M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7"/>',
  abwesend: '<circle cx="10" cy="8" r="4"/><path d="M3 21c1-4 3.5-6 7-6M16 15l5 5M21 15l-5 5"/>',
  hausaufgabe: '<path d="M4 20l4-1 11-11-3-3L5 16z"/><path d="M14 6l3 3"/>',
  kopieren: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3"/>',
  kalender: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/><circle cx="12" cy="15.5" r="1.6"/>',
  stift: '<path d="M4 20l4-1 11-11-3-3L5 16z"/>',
  augen: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
};

export function icon(name, klasse = '') {
  return `<svg class="icon ${klasse}" viewBox="0 0 24 24" aria-hidden="true">${PFADE[name] ?? PFADE.info}</svg>`;
}

/** HTML-Escaping für alle dynamischen Werte. */
export function e(wert) {
  return String(wert ?? '').replace(/[&<>"']/g, z => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[z]));
}

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
  t.className = 'toast' + (fehler ? ' fehler' : '');
  t.textContent = text;
  leiste.append(t);
  setTimeout(() => t.remove(), fehler ? 6000 : 3200);
}

export function fehler(err) { toast(err?.message ?? String(err), true); }

/**
 * Dialog öffnen. inhalt: HTML; knoepfe: [{ text, art, aktion(dialog) → false = offen lassen }].
 * Liefert das Dialogelement; schliessen() schließt.
 */
export function dialog({ titel, inhalt, knoepfe = [], breit = false, beimOeffnen }) {
  const grund = document.createElement('div');
  grund.className = 'dialog-grund';
  grund.innerHTML = `<div class="dialog ${breit ? 'breit' : ''}" role="dialog" aria-modal="true" aria-label="${e(titel)}">
      <div class="dialog-kopf"><h2>${e(titel)}</h2><button class="knopf flach rund" data-zu aria-label="Schließen">${icon('x')}</button></div>
      <div class="dialog-inhalt">${inhalt}</div>
      ${knoepfe.length ? `<div class="dialog-fuss">${knoepfe.map((k, i) => `<button class="knopf ${k.art ?? ''}" data-knopf="${i}">${k.text}</button>`).join('')}</div>` : ''}
    </div>`;
  const schliessen = () => { grund.remove(); document.removeEventListener('keydown', taste); };
  const taste = ev => { if (ev.key === 'Escape') schliessen(); };
  document.addEventListener('keydown', taste);
  grund.addEventListener('mousedown', ev => { if (ev.target === grund) schliessen(); });
  grund.querySelector('[data-zu]').onclick = schliessen;
  const box = grund.querySelector('.dialog');
  box.schliessen = schliessen;
  knoepfe.forEach((k, i) => {
    grund.querySelector(`[data-knopf="${i}"]`).onclick = async ev => {
      const btn = ev.currentTarget;
      btn.disabled = true;
      try { if ((await k.aktion?.(box)) !== false) schliessen(); }
      catch (err) { fehler(err); }
      finally { btn.disabled = false; }
    };
  });
  document.body.append(grund);
  beimOeffnen?.(box);
  box.querySelector('input, select, textarea, button.haupt')?.focus();
  return box;
}

export function bestaetigen(titel, text, knopf = 'Löschen') {
  return new Promise(ok => {
    dialog({ titel, inhalt: `<p>${e(text)}</p>`, knoepfe: [
      { text: 'Abbrechen', aktion: () => ok(false) },
      { text: knopf, art: 'haupt', aktion: () => ok(true) },
    ] });
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
  return { offen: '<span class="marker gelb">wartet auf UntPlan</span>', quittiert: '<span class="marker gruen">übernommen</span>', abgelehnt: '<span class="marker rot">abgelehnt</span>' }[status] ?? `<span class="marker">${e(status)}</span>`;
}
