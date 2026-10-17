// Zustand der Oberfläche: Benutzer, geöffnetes Dokument, Grunddaten, Auswahllisten, Modus.
import { an, melden } from './ereignisse.js';

export const zustand = {
  /** Systeminfo { anwendung, version, beta } */
  system: null,
  /** angemeldeter Benutzer { name, anzeigename, rolle, darfSchreiben } */
  benutzer: null,
  /** Stand des geöffneten Dokuments (siehe DokumentStand im Server) oder null */
  dokument: null,
  /** Grunddaten { schulname, zeitraster, perioden, … } des geöffneten Dokuments */
  grunddaten: null,
  /** zuletzt bekannter Änderungsstand */
  version: 0,
  /** 'stundenplan' | 'vertretung' */
  modus: 'stundenplan',
  /** Ereignisverbindung zum Server steht */
  verbunden: false,
};

export const darfSchreiben = () => !!zustand.benutzer?.darfSchreiben;

// ---- Auswahllisten (Kurz-/Langname) je Elementtyp, zwischengespeichert bis zur nächsten Stammdatenänderung ----
let elementCache = null;

/** Elemente eines Typs als [{ id, k, l }] (typ: 'klasse', 'lehrer', 'raum', 'fach', 'raumgruppe', …). */
export async function elemente(typ) {
  if (!zustand.dokument) return [];
  if (!elementCache) {
    const { api } = await import('./api.js');
    elementCache = api.get(api.dok('/elemente')).catch((e) => { elementCache = null; throw e; });
  }
  const alle = await elementCache;
  return alle[typ] || [];
}

export function elementeVerwerfen() {
  elementCache = null;
}

an('dokument', () => elementeVerwerfen());

export function modusSetzen(modus) {
  if (zustand.modus === modus) return;
  zustand.modus = modus;
  document.documentElement.dataset.modus = modus;
  melden('modus', modus);
}
