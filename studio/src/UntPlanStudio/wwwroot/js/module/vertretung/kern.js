// Gemeinsame Bausteine der Vertretungsplanung: Planungsdatum (wie im Menüband des Desktops), Auswahl der aktiven Vertretung
// (Fensterverknüpfung zwischen Vertretungsliste, Vorschlag, Absenzen und tagesaktuellen Plänen), API-Pfade, Rückfragen und Datumshilfen.
import { api } from '../../kern/api.js';
import { an, melden } from '../../kern/ereignisse.js';
import { zustand } from '../../kern/zustand.js';
import { dialog, hinweis, fehlerMelden } from '../../kern/dialog.js';
import { h } from '../../kern/dom.js';

export const TAGE = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

/** Pfad unterhalb der Vertretungsplanung des geöffneten Dokuments. */
export const vpfad = (p = '') => api.dok(`/vertretung${p}`);

// ---------------------------------------------------------------------------------------------
// Datum

export function isoAus(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
export const dateAus = (iso) => new Date(`${iso}T12:00:00`);
export const plusTage = (iso, n) => { const d = dateAus(iso); d.setDate(d.getDate() + n); return isoAus(d); };
export const montag = (iso) => { const d = dateAus(iso); return plusTage(iso, -((d.getDay() + 6) % 7)); };
export const tagKurz = (iso) => TAGE[dateAus(iso).getDay()];
/** „02.11.2026“ */
export const datumText = (iso) => (iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '');
/** „Mo 02.11.26“ wie die Datumsspalten des Desktops */
export const tagText = (iso) => (iso ? `${tagKurz(iso)} ${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(2, 4)}` : '');
export const kurzText = (iso) => (iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.` : '');

/** Datumseingabe lesen wie im Desktop: „2.11.“, „02.11.26“, „2.11.2026“, „+1“/„-1“ (Tage), ISO, leer = Basis. */
export function datumLesen(text, basis) {
  const t = String(text ?? '').trim().replace(/^(Mo|Di|Mi|Do|Fr|Sa|So)\s+/i, '');
  if (!t) return basis;
  if (/^\d{4}-\d{2}-\d{2}$/.test(t)) return t;
  if (/^[+-]\d+$/.test(t)) return plusTage(basis, Number(t));
  const m = t.match(/^(\d{1,2})\.(\d{1,2})\.?(\d{2,4})?$/);
  if (!m) throw new Error(`Das Datum „${text}“ ist ungültig.`);
  let jahr = m[3] ? Number(m[3]) : dateAus(basis).getFullYear();
  if (jahr < 100) jahr += 2000;
  const d = new Date(jahr, Number(m[2]) - 1, Number(m[1]), 12);
  if (d.getMonth() !== Number(m[2]) - 1) throw new Error(`Das Datum „${text}“ ist ungültig.`);
  return isoAus(d);
}

let datum = null;
let tageswechselLaeuft = null;

/** Aktuelles Planungsdatum (ISO). */
export const planungsdatum = () => datum;

function speicherSchluessel() { return `vertretung.datum.${zustand.dokument?.id || ''}`; }

/**
 * Planungsdatum setzen: alle verknüpften Fenster folgen (Ereignis „vertretung.datum“). Im Vertretungsmodus werden wie im Desktop beim
 * Tageswechsel die Vertretungen aus Absenzen, Veranstaltungen und Klausuren berechnet (nur wenn sich etwas ändert).
 */
export async function datumSetzen(neu, { aktualisieren = true } = {}) {
  if (!neu) return;
  const gleich = neu === datum;
  datum = neu;
  try { sessionStorage.setItem(speicherSchluessel(), neu); } catch { /* privat */ }
  if (aktualisieren && zustand.modus === 'vertretung' && zustand.dokument && zustand.benutzer?.darfSchreiben) await tageswechsel(neu, neu);
  if (!gleich) melden('vertretung.datum', neu);
}

/** Vertretungen eines Zeitraums aktualisieren (Probeberechnung auf dem Server, Änderung nur bei Bedarf). */
export async function tageswechsel(von, bis) {
  const schluessel = `${von}/${bis}`;
  if (tageswechselLaeuft?.schluessel === schluessel) return tageswechselLaeuft.p;
  const p = api.post(vpfad('/aktualisieren'), { von, bis }).catch((e) => { if (e?.status !== 403) fehlerMelden(e); return null; });
  tageswechselLaeuft = { schluessel, p };
  try { return await p; } finally { if (tageswechselLaeuft?.p === p) tageswechselLaeuft = null; }
}

/** Startdatum beim Öffnen eines Dokuments: zuletzt benutztes Datum der Sitzung bzw. heute / nächster Unterrichtstag. */
export async function datumInitialisieren() {
  if (!zustand.dokument) { datum = null; return; }
  let gemerkt = null;
  try { gemerkt = sessionStorage.getItem(speicherSchluessel()); } catch { /* privat */ }
  const start = gemerkt || (await api.get(vpfad('/start'))).datum;
  await datumSetzen(start, { aktualisieren: false });
  melden('vertretung.datum', datum);
}

export async function blaettern(richtung, woche = false) {
  if (!datum) return;
  const r = await api.get(vpfad(`/blaettern?datum=${datum}&richtung=${richtung}&woche=${woche}`));
  await datumSetzen(r.datum);
}

// ---------------------------------------------------------------------------------------------
// Auswahl der aktiven Vertretung (Fensterverknüpfung)

let auswahl = null;

/** Auswahl melden: { id (Vtr-Nr.), ids (alle markierten), zeile (Kennzeichen m der Liste), quelle }. */
export function auswahlMelden(a) {
  auswahl = a;
  melden('vertretung.auswahl', a);
}
export const aktuelleAuswahl = () => auswahl;

an('dokument', () => { auswahl = null; datumInitialisieren().catch(() => {}); });

// ---------------------------------------------------------------------------------------------
// Rückfragen wie im Desktop: Server antwortet { frage: { schluessel, text, optionen } }, die Anfrage wird mit der Antwort wiederholt.

export async function rueckfrage(f) {
  const knoepfe = f.optionen.map((o, i) => ({ text: o, wert: i, primaer: i === 0 }));
  if (f.optionen.length > 2) knoepfe.push({ text: 'Abbrechen', wert: null });
  const inhalt = h('div', { class: 'vt-frage' }, f.text.split('\n').map((z) => (z ? h('p', null, z) : h('br'))));
  return dialog({ titel: 'UntPlan - Frage', inhalt, knoepfe, breite: f.optionen.some((o) => o.length > 20) ? '560px' : undefined });
}

/** POST mit Rückfragen; liefert die Antwort oder null (abgebrochen). */
export async function mitRueckfragen(pfad, daten = {}, optionen = {}) {
  const antworten = { ...(daten.antworten || {}) };
  for (let i = 0; i < 8; i++) {
    const r = await api.post(pfad, { ...daten, antworten }, optionen);
    if (!r?.frage) {
      if (r?.hinweis) hinweis(r.hinweis, 'info', 5000);
      return r;
    }
    const w = await rueckfrage(r.frage);
    if (w === null || w === undefined) return null;
    antworten[r.frage.schluessel] = w;
  }
  return null;
}

/** Kleine Statusmeldung im Fuß eines Fensters. */
export function status(el, text, art = '') {
  el.textContent = text || '';
  el.className = `vt-status ${art}`;
}
