// Modulsystem der Oberfläche. Ein Modul (js/module/<modul>.js) exportiert als default:
//
// export default {
//   id: 'vertretung',                          // eindeutig
//   css: ['css/module/vertretung.css'],        // optional, wird automatisch geladen
//   fenster: { 'vertretung.liste': { titel, symbol, breite, hoehe, einzig(p), erstellen(fenster, p) } },
//   befehle: { 'vertretung.oeffnen': (args) => … },
//   ribbon: [ { id, reiter, gruppe, text, symbol, gross, befehl, args, menue, hinweis, taste, braucht, gedrueckt } ],
//   start() { … }                              // optional, nach dem Laden aller Module
// };
//
// Ribbon-Einträge mit gleicher id ersetzen frühere (Platzhalter aus geplant.js werden so von echten Modulen abgelöst,
// ohne geplant.js zu ändern). braucht: 'dokument' | 'schreiben' | 'admin'.
import { fenstertypRegistrieren } from './fenster.js';
import { fehlerMelden, hinweis } from './dialog.js';
import { zustand } from './zustand.js';

const befehle = new Map();
const eintraege = new Map();
const tasten = new Map();
export const geladeneModule = [];

export function befehlRegistrieren(name, f) {
  befehle.set(name, f);
}

/** Befehl ausführen (Fehler werden als Hinweis gezeigt). */
export async function befehl(name, args) {
  const f = befehle.get(name);
  if (!f) { hinweis(`Der Befehl „${name}“ ist noch nicht verfügbar.`, 'warnung'); return undefined; }
  try { return await f(args ?? {}); } catch (e) { fehlerMelden(e); return undefined; }
}

export function befehlVorhanden(name) { return befehle.has(name); }

export function ribbonEintrag(e) {
  if (!e.id) e.id = `${e.reiter}/${e.gruppe}/${e.text}`;
  const alt = eintraege.get(e.id);
  eintraege.set(e.id, { ...e, _reihenfolge: alt?._reihenfolge ?? eintraege.size });
  if (e.taste && (e.befehl || e.aktion)) tasten.set(normTaste(e.taste), e);
}

export function alleRibbonEintraege() {
  return [...eintraege.values()].sort((a, b) => (a.reihenfolge ?? a._reihenfolge) - (b.reihenfolge ?? b._reihenfolge));
}

/** Darf der Eintrag gerade benutzt werden? */
export function verfuegbar(e) {
  if (e.geplant) return false;
  const b = Array.isArray(e.braucht) ? e.braucht : e.braucht ? [e.braucht] : [];
  if (b.includes('dokument') && !zustand.dokument) return false;
  if (b.includes('schreiben') && (!zustand.dokument || !zustand.benutzer?.darfSchreiben)) return false;
  if (b.includes('admin') && zustand.benutzer?.rolle !== 'Admin') return false;
  return true;
}

export function eintragAusfuehren(e) {
  if (e.aktion) return Promise.resolve().then(() => e.aktion(e.args ?? {})).catch(fehlerMelden);
  if (e.befehl) return befehl(e.befehl, e.args);
  return undefined;
}

// ---- Tastenkürzel ('Strg+S', 'Strg+Umschalt+V', 'F5') ----

function normTaste(t) {
  return t.split('+').map((x) => x.trim().toLowerCase().replace('strg', 'ctrl').replace('umschalt', 'shift')).sort().join('+');
}

export function tasteRegistrieren(taste, aktion) {
  tasten.set(normTaste(taste), { aktion });
}

export function tasteBehandeln(ev) {
  const teile = [];
  if (ev.ctrlKey || ev.metaKey) teile.push('ctrl');
  if (ev.shiftKey) teile.push('shift');
  if (ev.altKey) teile.push('alt');
  teile.push(ev.key.length === 1 ? ev.key.toLowerCase() : ev.key.toLowerCase());
  const e = tasten.get(teile.sort().join('+'));
  if (!e || (e.braucht && !verfuegbar(e))) return false;
  ev.preventDefault();
  eintragAusfuehren(e);
  return true;
}

// ---- Laden ----

function cssLaden(pfad) {
  if (document.querySelector(`link[data-modul-css="${pfad}"]`)) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = pfad;
  l.dataset.modulCss = pfad;
  document.head.append(l);
}

export async function moduleLaden(module) {
  for (const m of module) {
    try {
      for (const c of m.css || []) cssLaden(c);
      for (const [name, def] of Object.entries(m.fenster || {})) fenstertypRegistrieren(name, def);
      for (const [name, f] of Object.entries(m.befehle || {})) befehlRegistrieren(name, f);
      for (const e of m.ribbon || []) ribbonEintrag(e);
      geladeneModule.push(m.id);
    } catch (e) {
      console.error(`Modul ${m?.id} konnte nicht geladen werden`, e);
    }
  }
  for (const m of module) {
    try { await m.start?.(); } catch (e) { console.error(`Start von Modul ${m?.id} fehlgeschlagen`, e); }
  }
}
