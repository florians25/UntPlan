// Neuigkeiten: was sich in UntPlanWeb geändert hat (Entwicklungsnews), neueste zuerst.
// Gelesen wird je Benutzer (und Gerät) als Menge der gelesenen Einträge gemerkt – nicht mehr als „größter Schlüssel“: mehrere
// Einträge am selben Tag wurden sonst je nach Titel-Sortierung nie bzw. immer wieder als neu gezählt.
import { e, holen } from '../ui.js';

/** Früherer Speicher (größter gelesener Schlüssel „Datum|Titel“ je Gerät) – nur noch zur Übernahme. */
export const GELESEN = 'untplan:neuigkeiten-gelesen';
const speicher = login => `${GELESEN}:${login || '-'}`;
const schluessel = n => n.id ? String(n.id) : `${n.datum}|${n.titel}`;

/** Liste einmal je Seitenaufruf laden (keine veraltete Kopie aus dem HTTP-Cache). */
let ladung = null;
function liste() {
  ladung ??= fetch('/neuigkeiten.json', { cache: 'no-cache', credentials: 'same-origin' })
    .then(r => r.ok ? r.json() : [])
    .then(l => Array.isArray(l) ? l : [])
    .catch(() => { ladung = null; return []; });
  return ladung;
}

function gelesen(login, eintraege) {
  try {
    const roh = localStorage.getItem(speicher(login));
    if (roh) return new Set(JSON.parse(roh));
    // Übernahme des alten Speichers: alles bis zum damals neuesten Tag gilt als gelesen
    const alt = localStorage.getItem(GELESEN);
    if (alt) return new Set(eintraege.filter(n => n.datum < alt.slice(0, 10) || schluessel(n) === alt).map(schluessel));
  } catch { /* privat oder beschädigt */ }
  return new Set();
}

function merken(login, eintraege) {
  try { localStorage.setItem(speicher(login), JSON.stringify(eintraege.map(schluessel).slice(0, 300))); } catch { /* privat */ }
}

/** Anzahl ungelesener Einträge für die Zahl am Menüeintrag „Neuigkeiten“. */
export async function neueAnzahl(login) {
  const l = await liste();
  const g = gelesen(login, l);
  return l.filter(n => !g.has(schluessel(n))).length;
}

/** Zahl am Menüeintrag setzen bzw. entfernen (genau ein Element, keine Doppelungen). */
export function zahlSetzen(n) {
  const link = document.querySelector('[data-neuigkeiten]');
  if (!link) return;
  let z = link.querySelector('.nw-zahl');
  if (n <= 0) { z?.remove(); return; }
  if (!z) { z = document.createElement('span'); z.className = 'nw-zahl'; link.append(z); }
  z.textContent = n > 9 ? '9+' : String(n);
  z.title = `${n} ungelesene ${n === 1 ? 'Neuigkeit' : 'Neuigkeiten'}`;
}

export async function zeigen(seite, kontext) {
  kontext.titel('Neuigkeiten');
  const login = kontext.ich?.login;
  const l = await liste();
  const g = gelesen(login, l);
  const datum = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { day: '2-digit', month: 'long', year: 'numeric' });
  seite.innerHTML = l.length ? `<div class="nw-liste">${l.map(n => { const neu = !g.has(schluessel(n)); return `
    <article class="nw-eintrag ${neu ? 'neu' : ''}">
      <div class="nw-datum">${e(datum(n.datum))}${neu ? '<span class="nw-neu">Neu</span>' : ''}</div>
      <h3>${e(n.titel)}</h3>
      <ul>${(n.punkte || []).map(p => `<li>${e(p)}</li>`).join('')}</ul>
    </article>`; }).join('')}</div>` : '<div class="nw-liste"><div class="nw-eintrag">Keine Neuigkeiten.</div></div>';
  // beim Ansehen gelten alle Einträge als gelesen; die „Neu“-Marken bleiben bis zum nächsten Aufruf sichtbar
  merken(login, l);
  zahlSetzen(0);
}
