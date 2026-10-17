// Gemeinsame Modal-Komponente des Klassenbuchs: Alle Klassenbuch-Dialoge (Abwesenheit, Entschuldigung, Lehrstoff, Hausaufgabe,
// Klassenbucheintrag, Klassendienste, Befreiungen, Sitzplan, Berichte …) öffnen nach demselben Prinzip und mit derselben Grunddarstellung:
//  • Breiten-Stufen s (420 px), m (560 px), l (800 px), xl (1100 px) – auf schmalen Bildschirmen volle Breite mit 16 px Rand,
//  • Kopf mit Titel und X, Inhalt mit einheitlichen Abständen, Fuß rechts „Abbrechen“ / „Speichern“ (Löschen links),
//  • gleiche Animation (Einblenden, leichtes Hochgleiten), ESC und Klick auf den Hintergrund schließen den obersten Dialog,
//  • Fokus im Dialog, beim Schließen zurück zum auslösenden Element.
// Über offenen Bootstrap-Fenstern (Stundendetails) liegt der Dialog im Fenster, damit dessen Fokusfalle und ESC nicht greifen.
import { e, fehler } from './ui.js';

const BREITEN = { s: 'kbd-s', m: 'kbd-m', l: 'kbd-l', xl: 'kbd-xl' };
const KNOPF = { haupt: 'nl-knopf nw-primaer', gefahr: 'nl-knopf rot kbd-links' };
let zaehler = 0;

/** Breiten-Stufe aus einer Pixelangabe (ältere Aufrufe) bzw. „breit“. */
export function stufe(breite, breit = false) {
  if (BREITEN[breite]) return breite;
  if (typeof breite === 'number') return breite <= 440 ? 's' : breite <= 600 ? 'm' : breite <= 820 ? 'l' : 'xl';
  return breit ? 'l' : 'm';
}

/**
 * Dialog öffnen (gleiche Aufrufform wie ui.dialog): inhalt = HTML; knoepfe = [{ text, art: 'haupt'|'gefahr', aktion(box) → false = offen lassen }];
 * fuss = eigenes HTML für den Fuß (statt knoepfe); breite = 's'|'m'|'l'|'xl' (breit: true = 'l'); hintergrundSchliesst (Standard true).
 * Liefert das Fenster-Element (box) mit box.schliessen(), box.rumpf, box.fuss und box.geschlossen (Promise).
 */
export function kbDialog({ titel, inhalt = '', knoepfe = [], fuss = '', breite, breit = false, beimOeffnen, wurzel, hintergrundSchliesst = true, klasse = '' } = {}) {
  const id = `kbd-titel-${++zaehler}`;
  const ziel = wurzel ?? [...document.querySelectorAll('.modal.show')].pop() ?? document.body;
  const vorher = document.activeElement;
  const o = document.createElement('div');
  o.className = 'kbd-overlay';
  o.innerHTML = `<div class="kbd-fenster ${BREITEN[stufe(breite, breit)]} ${e(klasse)}" role="dialog" aria-modal="true" aria-labelledby="${id}">
      <header class="kbd-kopf"><h2 id="${id}">${e(titel)}</h2><button type="button" class="kbd-zu" data-kbd-zu title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg" aria-hidden="true"></i></button></header>
      <div class="kbd-rumpf">${inhalt}</div>
      ${knoepfe.length || fuss ? `<footer class="kbd-fuss">${fuss || knoepfe.map((k, i) => `<button type="button" class="${KNOPF[k.art] ?? 'nl-knopf'}" data-kbd-knopf="${i}">${k.text}</button>`).join('')}</footer>` : ''}
    </div>`;
  const box = o.firstElementChild;
  box.rumpf = box.querySelector('.kbd-rumpf');
  box.fuss = box.querySelector('.kbd-fuss');
  let fertig;
  box.geschlossen = new Promise(ok => { fertig = ok; });
  let offen = true;
  box.schliessen = () => {
    if (!offen) return;
    offen = false;
    o.classList.remove('an');
    const weg = () => { o.remove(); if (ziel === document.body && !document.querySelector('body > .kbd-overlay')) document.body.classList.remove('kbd-offen'); fertig(); };
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) weg(); else setTimeout(weg, 160);
    if (vorher instanceof HTMLElement && vorher.isConnected) setTimeout(() => vorher.focus({ preventScroll: true }), 0);
  };
  box.querySelectorAll('[data-kbd-zu]').forEach(b => b.onclick = () => box.schliessen());
  knoepfe.forEach((k, i) => {
    box.querySelector(`[data-kbd-knopf="${i}"]`).onclick = async ev => {
      const btn = ev.currentTarget;
      btn.disabled = true;
      try { if ((await k.aktion?.(box)) !== false) box.schliessen(); }
      catch (err) { fehler(err); }
      finally { btn.disabled = false; }
    };
  });
  // ESC: nur der oberste Dialog; nicht an darunterliegende Fenster weitergeben
  o.addEventListener('keydown', ev => {
    if (ev.key === 'Escape') { ev.stopPropagation(); ev.preventDefault(); box.schliessen(); return; }
    if (ev.key !== 'Tab') return;
    const fokus = [...box.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')].filter(x => !x.disabled && x.offsetParent !== null);
    if (!fokus.length) return;
    if (ev.shiftKey && document.activeElement === fokus[0]) { ev.preventDefault(); fokus.at(-1).focus(); }
    else if (!ev.shiftKey && document.activeElement === fokus.at(-1)) { ev.preventDefault(); fokus[0].focus(); }
  });
  // Hintergrund: nur wenn Drücken und Loslassen auf dem Hintergrund (kein Schließen beim Markieren von Text)
  let start = null;
  o.addEventListener('mousedown', ev => { start = ev.target; });
  o.addEventListener('click', ev => { if (hintergrundSchliesst && ev.target === o && start === o) box.schliessen(); start = null; });
  ziel.append(o);
  if (ziel === document.body) document.body.classList.add('kbd-offen');
  beimOeffnen?.(box);
  requestAnimationFrame(() => o.classList.add('an'));
  (box.rumpf.querySelector('textarea:not([readonly]):not([disabled]), input:not([type=checkbox]):not([type=radio]):not([readonly]):not([disabled]):not([type=hidden]), select:not([disabled])')
    ?? box.querySelector('.kbd-fuss .nw-primaer') ?? box.querySelector('.kbd-zu'))?.focus({ preventScroll: true });
  return box;
}

/** Rückfrage im Klassenbuch-Stil (Abbrechen / Löschen bzw. eigener Knopf). Ergebnis: bestätigt? */
export function kbBestaetigen(titel, text, knopf = 'Löschen') {
  return new Promise(ok => {
    let antwort = false;
    const box = kbDialog({ titel, breite: 's', inhalt: `<p class="mb-0 kbd-text">${e(text)}</p>`, knoepfe: [
      { text: 'Abbrechen' },
      { text: knopf, art: 'haupt', aktion: () => { antwort = true; } },
    ] });
    // Löschen in der Rückfrage rechts als roter Hauptknopf
    if (knopf === 'Löschen') box.querySelector('[data-kbd-knopf="1"]').className = 'nl-knopf kbd-gefahr';
    box.geschlossen.then(() => ok(antwort));
  });
}

/**
 * Bestehenden Inhalt mit eigener Knopfleiste (z. B. „.ad-knoepfe“ der Stundendetails) in den einheitlichen Fuß übernehmen:
 * Hauptknopf (Speichern) rechts außen, Abbrechen/Schließen links davon, Löschen ganz links.
 */
export function fussUebernehmen(box, auswahl = '.ad-knoepfe') {
  const leiste = box.rumpf.querySelector(auswahl);
  if (!leiste) return;
  let fuss = box.fuss;
  if (!fuss) { fuss = document.createElement('footer'); fuss.className = 'kbd-fuss'; box.append(fuss); box.fuss = fuss; }
  const knoepfe = [...leiste.querySelectorAll('button')];
  const haupt = b => b.classList.contains('sd-orange') || b.matches('[data-ad-speichern="1"], [data-ab-speichern], [data-klausur-speichern], [data-pv-speichern]')
    || (knoepfe.filter(x => x.matches('[data-ad-speichern]')).length === 1 && b.matches('[data-ad-speichern]'));
  const gefahr = b => b.classList.contains('rot') || b.matches('[data-ab-loeschen]');
  const zu = b => b.matches('[data-ad-zu]');
  const sortiert = [...knoepfe.filter(gefahr), ...knoepfe.filter(zu), ...knoepfe.filter(b => !gefahr(b) && !zu(b) && !haupt(b)), ...knoepfe.filter(b => haupt(b) && !gefahr(b))];
  for (const b of sortiert) {
    b.className = haupt(b) && !gefahr(b) ? 'nl-knopf nw-primaer' : gefahr(b) ? 'nl-knopf rot kbd-links' : 'nl-knopf';
    if (zu(b)) b.setAttribute('data-kbd-zu', '');
    fuss.append(b);
  }
  fuss.querySelectorAll('[data-kbd-zu]').forEach(b => { if (!b.onclick) b.onclick = () => box.schliessen(); });
  leiste.remove();
}
