// Bilder der Schüler*innen (Leitfaden Klassenleitung, Kapitel 5): Wo Schülerkacheln mit Platzhalter bzw. Initialen erscheinen –
// Abwesenheitskontrolle in den Stundendetails (.sd-kachel[data-sid] .sd-avatar) und Sitzplan (.sp-karte[data-karte] .sp-ava,
// .sd-kachel[data-sid] .sp-ava) –, ersetzt ein Beobachter den Platzhalter durch das hochgeladene Bild. Welche Schüler*innen ein Bild
// haben, wird einmal geladen (bilderNeu() nach dem Hochladen).
import { holen } from './ui.js';

let ids = null;
let laden = null;
let stempel = Date.now();

/** Nach dem Hochladen bzw. Import: Liste und Bilder neu laden. */
export function bilderNeu() {
  ids = null; laden = null; stempel = Date.now();
  beobachter?.disconnect();
  beobachter = null;
  beobachten();
}

function idsHolen() {
  if (ids) return Promise.resolve(ids);
  laden ??= holen('/api/app/schueler/bilder').then(l => (ids = new Set(l.map(String)))).catch(() => (ids = new Set()));
  return laden;
}

const ZIELE = '.sd-kachel[data-sid] .sd-avatar:not([data-bild]), .sp-karte[data-karte] .sp-ava:not([data-bild])';

function anwenden(wurzel, liste) {
  const elemente = [...(wurzel.matches?.(ZIELE) ? [wurzel] : []), ...(wurzel.querySelectorAll?.(ZIELE) ?? [])];
  for (const el of elemente) {
    const halter = el.closest('[data-sid], [data-karte]');
    const id = halter?.dataset.sid ?? halter?.dataset.karte;
    el.dataset.bild = '0';
    if (!id || !liste.has(id)) continue;
    el.dataset.bild = '1';
    const ziel = el.querySelector('.sp-ava') ?? el;
    ziel.dataset.bild = '1';
    ziel.innerHTML = `<img class="sb-kachelbild" src="/api/app/schueler/${encodeURIComponent(id)}/bild?v=${stempel}" alt="" loading="lazy">`;
  }
}

let beobachter = null;
/** Beobachter starten (einmal nach der Anmeldung, nur für das Kollegium). */
export function beobachten(wurzel = document.body) {
  if (beobachter || !window.MutationObserver) return;
  beobachter = { disconnect() {} };
  idsHolen().then(liste => {
    if (!liste.size) { beobachter = null; return; }
    anwenden(wurzel, liste);
    beobachter = new MutationObserver(m => { for (const x of m) for (const n of x.addedNodes) if (n.nodeType === 1) anwenden(n, liste); });
    beobachter.observe(wurzel, { childList: true, subtree: true });
  });
}
