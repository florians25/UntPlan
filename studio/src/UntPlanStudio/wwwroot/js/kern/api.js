// Zugriff auf die REST-API des Studios (/api/...). Fehler kommen als ApiFehler mit der Meldung des Servers.
import { melden } from './ereignisse.js';
import { zustand } from './zustand.js';

export class ApiFehler extends Error {
  constructor(status, meldung) {
    super(meldung);
    this.status = status;
  }
}

async function anfrage(methode, pfad, body, optionen = {}) {
  const kopf = { 'X-UntPlan-Studio': '1', Accept: 'application/json' };
  let inhalt;
  if (body instanceof FormData) inhalt = body;
  else if (body !== undefined) {
    kopf['Content-Type'] = 'application/json';
    inhalt = JSON.stringify(body);
  }
  let antwort;
  try {
    antwort = await fetch(pfad, { method: methode, headers: kopf, body: inhalt, credentials: 'same-origin' });
  } catch {
    throw new ApiFehler(0, 'Keine Verbindung zum Server.');
  }
  const version = antwort.headers.get('X-Dokument-Version');
  if (version !== null) {
    melden('version', { version: Number(version), bereich: antwort.headers.get('X-Dokument-Bereich') || 'Alles', quelle: optionen.quelle ?? null });
  }
  if (antwort.status === 401 && !pfad.endsWith('/anmeldung')) {
    melden('abgemeldet');
    throw new ApiFehler(401, 'Bitte erneut anmelden.');
  }
  if (optionen.roh) {
    if (!antwort.ok) throw new ApiFehler(antwort.status, await fehlertext(antwort));
    return antwort;
  }
  const text = await antwort.text();
  let daten = null;
  if (text) {
    try { daten = JSON.parse(text); } catch { daten = text; }
  }
  if (!antwort.ok) throw new ApiFehler(antwort.status, daten?.fehler || `Fehler ${antwort.status}`);
  return daten;
}

async function fehlertext(antwort) {
  try { return (await antwort.json()).fehler || `Fehler ${antwort.status}`; } catch { return `Fehler ${antwort.status}`; }
}

export const api = {
  get: (pfad, o) => anfrage('GET', pfad, undefined, o),
  post: (pfad, body, o) => anfrage('POST', pfad, body ?? {}, o),
  put: (pfad, body, o) => anfrage('PUT', pfad, body, o),
  patch: (pfad, body, o) => anfrage('PATCH', pfad, body, o),
  loeschen: (pfad, o) => anfrage('DELETE', pfad, undefined, o),
  roh: (pfad) => anfrage('GET', pfad, undefined, { roh: true }),

  /** Datei hochladen (multipart, Feld "datei") mit weiteren Formularfeldern. */
  hochladen(pfad, datei, felder = {}, o) {
    const fd = new FormData();
    fd.append('datei', datei, datei.name);
    for (const [k, v] of Object.entries(felder)) fd.append(k, String(v));
    return anfrage('POST', pfad, fd, o);
  },

  /** Pfad unterhalb des geöffneten Dokuments: api.dok('/stammdaten/lehrer') → /api/dokumente/<id>/stammdaten/lehrer */
  dok(pfad = '') {
    if (!zustand.dokument) throw new ApiFehler(0, 'Es ist kein Dokument geöffnet.');
    return `/api/dokumente/${zustand.dokument.id}${pfad}`;
  },
};

/** Benutzereinstellung lesen (null, wenn nicht vorhanden). */
export async function einstellungLesen(schluessel) {
  try { return (await api.get(`/api/einstellungen/${encodeURIComponent(schluessel)}`)).wert ?? null; } catch { return null; }
}

/** Benutzereinstellung speichern (Fehler werden nur protokolliert). */
export async function einstellungSchreiben(schluessel, wert) {
  try { await api.put(`/api/einstellungen/${encodeURIComponent(schluessel)}`, wert); } catch (e) { console.warn('Einstellung nicht gespeichert', schluessel, e); }
}
