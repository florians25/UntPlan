// Dialoge (modal), Formulare, Kontextmenüs, kurze Hinweise.
import { h, leeren } from './dom.js';
import { symbol } from './symbole.js';
import { melden } from './ereignisse.js';

/**
 * Allgemeiner modaler Dialog. inhalt: Element oder Text. knoepfe: [{ text, wert, primaer, gefahr }].
 * Liefert ein Promise mit dem Wert des gedrückten Knopfes (Escape/Schließen = null). pruefen(wert) kann false liefern, um offen zu bleiben.
 */
export function dialog({ titel, inhalt, knoepfe = [{ text: 'OK', wert: true, primaer: true }], breite, pruefen }) {
  return new Promise((fertig) => {
    const vorher = document.activeElement;
    const zu = (wert) => {
      hg.remove();
      document.removeEventListener('keydown', taste, true);
      vorher?.focus?.();
      fertig(wert);
    };
    const druecken = async (k) => {
      if (pruefen && k.wert !== null && k.wert !== false && (await pruefen(k.wert)) === false) return;
      zu(k.wert);
    };
    const knopfLeiste = h('div', { class: 'd-knoepfe' }, knoepfe.map((k) =>
      h('button', { class: `knopf${k.primaer ? ' primaer' : ''}${k.gefahr ? ' gefahr' : ''}`, type: 'button', onclick: () => druecken(k) }, k.text)));
    const box = h('div', { class: 'dialog', role: 'dialog', 'aria-modal': 'true', style: breite ? { width: breite } : null },
      h('div', { class: 'd-titel' }, h('span', { class: 'text' }, titel || ''), h('button', { type: 'button', title: 'Schließen', onclick: () => zu(null) }, symbol('schliessen'))),
      h('div', { class: 'd-inhalt' }, typeof inhalt === 'string' ? inhalt.split('\n').map((z) => h('p', null, z)) : inhalt),
      knopfLeiste);
    const hg = h('div', { class: 'dialog-hintergrund' }, box);
    const taste = (e) => {
      if (e.key === 'Escape') { e.stopPropagation(); zu(null); }
      if (e.key === 'Enter' && !(e.target instanceof HTMLTextAreaElement) && !(e.target instanceof HTMLButtonElement)) {
        const p = knoepfe.find((k) => k.primaer);
        if (p) { e.preventDefault(); druecken(p); }
      }
    };
    document.addEventListener('keydown', taste, true);
    document.body.append(hg);
    (box.querySelector('input, select, textarea') || knopfLeiste.querySelector('.primaer') || knopfLeiste.lastChild)?.focus();
  });
}

export const meldung = (text, titel = 'UntPlan Studio') => dialog({ titel, inhalt: text });

export const frage = (text, { titel = 'Bitte bestätigen', ja = 'Ja', nein = 'Nein', gefahr = false } = {}) =>
  dialog({ titel, inhalt: text, knoepfe: [{ text: ja, wert: true, primaer: !gefahr, gefahr }, { text: nein, wert: false }] }).then((w) => w === true);

/**
 * Formular-Dialog. felder: [{ name, titel, art: 'text'|'zahl'|'passwort'|'auswahl'|'bool'|'textfeld', wert, optionen: [{wert, text}], pflicht, hinweis }]
 * Liefert ein Objekt { name: wert } oder null.
 */
export async function formular(titel, felder, { okText = 'OK', breite } = {}) {
  const eingaben = {};
  const raster = h('div', { class: 'formular' });
  for (const f of felder) {
    let el;
    if (f.art === 'auswahl') {
      el = h('select', { class: 'eingabe', name: f.name }, (f.optionen || []).map((o) => h('option', { value: o.wert, selected: String(o.wert) === String(f.wert ?? '') }, o.text)));
    } else if (f.art === 'bool') {
      el = h('input', { type: 'checkbox', name: f.name, checked: !!f.wert });
    } else if (f.art === 'textfeld') {
      el = h('textarea', { class: 'eingabe', name: f.name, rows: 4 }, f.wert ?? '');
    } else {
      el = h('input', { class: 'eingabe', name: f.name, type: f.art === 'passwort' ? 'password' : f.art === 'zahl' ? 'number' : 'text', value: f.wert ?? '', autocomplete: f.art === 'passwort' ? 'new-password' : 'off' });
    }
    eingaben[f.name] = el;
    raster.append(h('label', { for: f.name }, f.titel), el);
    if (f.hinweis) raster.append(h('div', { class: 'hinweis' }, f.hinweis));
  }
  const werte = () => Object.fromEntries(Object.entries(eingaben).map(([k, el]) => [k, el.type === 'checkbox' ? el.checked : el.value]));
  const ok = await dialog({
    titel, inhalt: raster, breite,
    knoepfe: [{ text: okText, wert: true, primaer: true }, { text: 'Abbrechen', wert: null }],
    pruefen: () => {
      for (const f of felder) {
        const el = eingaben[f.name];
        el.classList.toggle('ungueltig', !!f.pflicht && !String(el.value).trim());
      }
      return !felder.some((f) => f.pflicht && !String(eingaben[f.name].value).trim());
    },
  });
  return ok ? werte() : null;
}

/** Datei auswählen (Promise mit File oder null). */
export function dateiWaehlen(accept = '') {
  return new Promise((fertig) => {
    const input = h('input', { type: 'file', accept, style: { display: 'none' } });
    input.addEventListener('change', () => { fertig(input.files?.[0] || null); input.remove(); });
    input.addEventListener('cancel', () => { fertig(null); input.remove(); });
    document.body.append(input);
    input.click();
  });
}

// ---------------------------------------------------------------------------------------------
// Kontextmenü / Aufklappmenü

let offenesMenue = null;

/**
 * Zeigt ein Menü an Position (x, y). eintraege: [{ text, symbol, aktion, deaktiviert, haken, taste, menue }, null = Trenner, { titel }]
 */
export function menue(x, y, eintraege) {
  menueSchliessen();
  const el = h('div', { class: 'menue', role: 'menu' });
  for (const e of eintraege) {
    if (!e) { el.append(h('div', { class: 'm-trenner' })); continue; }
    if (e.titel) { el.append(h('div', { class: 'm-titel' }, e.titel)); continue; }
    const knopf = h('button', { class: 'm-eintrag', type: 'button', role: 'menuitem', disabled: !!e.deaktiviert },
      e.haken !== undefined ? h('span', { class: 'haken' }, e.haken ? '✓' : '') : (e.symbol ? symbol(e.symbol) : h('span', { class: 'haken' })),
      h('span', null, e.text),
      e.taste ? h('span', { class: 'taste' }, e.taste) : null,
      e.menue ? h('span', { class: 'taste' }, '▸') : null);
    knopf.addEventListener('click', (ev) => {
      ev.stopPropagation();
      if (e.menue) {
        const r = knopf.getBoundingClientRect();
        menue(r.right, r.top, e.menue);
        return;
      }
      menueSchliessen();
      Promise.resolve().then(() => e.aktion?.()).catch((f) => fehlerMelden(f));
    });
    el.append(knopf);
  }
  document.body.append(el);
  const r = el.getBoundingClientRect();
  el.style.left = `${Math.max(4, Math.min(x, innerWidth - r.width - 4))}px`;
  el.style.top = `${Math.max(4, Math.min(y, innerHeight - r.height - 4))}px`;
  offenesMenue = el;
  setTimeout(() => {
    document.addEventListener('pointerdown', ausserhalb, true);
    document.addEventListener('keydown', escape, true);
  });
  el.querySelector('.m-eintrag:not(:disabled)')?.focus();
  return el;
}

function ausserhalb(e) {
  if (offenesMenue && !offenesMenue.contains(e.target)) menueSchliessen();
}
function escape(e) {
  if (e.key === 'Escape') { e.stopPropagation(); menueSchliessen(); }
  if (offenesMenue && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
    e.preventDefault();
    const l = [...offenesMenue.querySelectorAll('.m-eintrag:not(:disabled)')];
    const i = l.indexOf(document.activeElement);
    l[(i + (e.key === 'ArrowDown' ? 1 : -1) + l.length) % l.length]?.focus();
  }
}

export function menueSchliessen() {
  offenesMenue?.remove();
  offenesMenue = null;
  document.removeEventListener('pointerdown', ausserhalb, true);
  document.removeEventListener('keydown', escape, true);
}

// ---------------------------------------------------------------------------------------------
// Hinweise (Toast) und Fehler

let hinweisBereich = null;

/** Kurzer Hinweis unten rechts (art: 'info'|'ok'|'warnung'|'fehler'); erscheint zusätzlich in der Statuszeile. */
export function hinweis(text, art = 'info', dauer = 3500) {
  melden('meldung', { text, art });
  if (!hinweisBereich) { hinweisBereich = h('div', { class: 'hinweise' }); document.body.append(hinweisBereich); }
  const el = h('div', { class: `h-eintrag ${art}` }, text);
  hinweisBereich.append(el);
  setTimeout(() => el.remove(), dauer);
}

/** Fehler (ApiFehler oder Exception) als Hinweis zeigen. */
export function fehlerMelden(f) {
  console.error(f);
  if (f?.status === 401) return;
  hinweis(f?.message || String(f), 'fehler', 6000);
}

/** Ladeanzeige über einem Element; liefert eine Funktion zum Entfernen. */
export function laden(el) {
  const l = h('div', { class: 'ladeanzeige' });
  el.append(l);
  return () => l.remove();
}

export { leeren };
