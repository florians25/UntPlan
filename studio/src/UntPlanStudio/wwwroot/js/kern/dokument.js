// Geöffnetes Arbeitsdokument: Öffnen/Schließen, Stand (Titel, Rückgängig), Ereignisverbindung (Server-Sent Events).
import { api } from './api.js';
import { an, melden } from './ereignisse.js';
import { zustand, elementeVerwerfen } from './zustand.js';
import { entprellen } from './dom.js';

let quelle = null; // EventSource
let wiederverbinden = 0;

/** Öffnet ein Dokument (Id oder bereits geladener Stand). */
export async function dokumentOeffnen(idOderStand) {
  const stand = typeof idOderStand === 'string' ? await api.get(`/api/dokumente/${idOderStand}`) : idOderStand;
  if (zustand.dokument?.id !== stand.id) trennen();
  zustand.dokument = stand;
  zustand.version = stand.version;
  zustand.grunddaten = await api.get(`/api/dokumente/${stand.id}/grunddaten`);
  try { sessionStorage.setItem('studio.dokument', stand.id); } catch { /* privat */ }
  verbinden();
  melden('dokument', stand);
  melden('stand', stand);
  return stand;
}

export async function dokumentSchliessen() {
  if (!zustand.dokument) return;
  const id = zustand.dokument.id;
  trennen();
  zustand.dokument = null;
  zustand.grunddaten = null;
  try { sessionStorage.removeItem('studio.dokument'); } catch { /* privat */ }
  melden('dokument', null);
  melden('stand', null);
  try { await api.post(`/api/dokumente/${id}/schliessen`); } catch { /* bereits geschlossen */ }
}

/** Stand neu laden (entprellt) – nach Änderungen, Speichern, Rückgängig. */
export const standAktualisieren = entprellen(async () => {
  if (!zustand.dokument) return;
  try {
    const stand = await api.get(api.dok());
    if (stand.id !== zustand.dokument?.id) return;
    zustand.dokument = stand;
    melden('stand', stand);
  } catch { /* nächster Versuch beim nächsten Ereignis */ }
}, 250);

/** Neuer Änderungsstand gesehen (aus einer API-Antwort oder einem Server-Ereignis). */
function versionGesehen({ version, bereich, quelle: q, benutzer }) {
  if (!zustand.dokument || !(version > zustand.version)) return;
  zustand.version = version;
  if (bereich === 'Stammdaten' || bereich === 'Alles') elementeVerwerfen();
  if (bereich === 'Alles' || bereich === 'Einstellungen') {
    api.get(api.dok('/grunddaten')).then((g) => { zustand.grunddaten = g; melden('grunddaten', g); }).catch(() => {});
  }
  melden('daten', { version, bereich, quelle: q, benutzer });
  standAktualisieren();
}

an('version', versionGesehen);

function verbinden() {
  if (!zustand.dokument || quelle || typeof EventSource === 'undefined') return;
  const id = zustand.dokument.id;
  quelle = new EventSource(`/api/dokumente/${id}/ereignisse`);
  quelle.addEventListener('hallo', () => {
    wiederverbinden = 0;
    zustand.verbunden = true;
    melden('verbindung', true);
  });
  quelle.addEventListener('aenderung', (e) => {
    const d = JSON.parse(e.data);
    versionGesehen({ version: d.version, bereich: d.bereich, quelle: null, benutzer: d.benutzer });
    if (d.benutzer && d.benutzer !== zustand.benutzer?.name) melden('meldung', { text: `${d.benutzer}: ${d.beschreibung}`, art: 'info' });
  });
  quelle.addEventListener('gespeichert', () => standAktualisieren());
  quelle.addEventListener('geschlossen', (e) => {
    const d = JSON.parse(e.data);
    if (d.benutzer !== zustand.benutzer?.name) melden('meldung', { text: `Das Dokument wurde von ${d.benutzer || 'jemand anderem'} geschlossen.`, art: 'warnung' });
  });
  quelle.onerror = () => {
    zustand.verbunden = false;
    melden('verbindung', false);
    if (quelle && quelle.readyState === EventSource.CLOSED) {
      quelle = null;
      const warten = Math.min(30000, 1000 * 2 ** wiederverbinden++);
      setTimeout(() => { if (zustand.dokument?.id === id) verbinden(); }, warten);
    }
  };
}

function trennen() {
  if (quelle) quelle.close();
  quelle = null;
  zustand.verbunden = false;
  melden('verbindung', false);
}

// ---- Befehle rund ums Dokument (Schnellzugriff, Tastatur) ----

export async function speichern() {
  if (!zustand.dokument) return;
  const stand = await api.post(api.dok('/speichern'));
  zustand.dokument = stand;
  melden('stand', stand);
  melden('meldung', { text: 'Gespeichert.', art: 'ok' });
}

export async function rueckgaengig() {
  if (!zustand.dokument?.kannRueckgaengig) return;
  const stand = await api.post(api.dok('/rueckgaengig'));
  zustand.dokument = stand;
  melden('stand', stand);
}

export async function wiederholen() {
  if (!zustand.dokument?.kannWiederholen) return;
  const stand = await api.post(api.dok('/wiederholen'));
  zustand.dokument = stand;
  melden('stand', stand);
}

export function herunterladen() {
  if (!zustand.dokument) return;
  const a = document.createElement('a');
  a.href = api.dok('/herunterladen');
  a.download = '';
  document.body.append(a);
  a.click();
  a.remove();
}
