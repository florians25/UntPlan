// Infocenter: runder Knopf unten rechts (Glocke mit Zahl ungelesener Einträge), öffnet ein Panel mit Infos der Schule und
// Neuigkeiten zu UntPlanWeb als Karten (Kategorie, Datum, Titel, Text, Punkt für ungelesen). Gibt es Neues, erscheint kurz ein
// Hinweis am Knopf. Gelesen wird je Benutzer auf dem Server vermerkt (GET/POST /api/app/infocenter …).
import { e, holen, senden, fehler } from './ui.js';

const DATUM = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: 'long', year: 'numeric' });
export const datumText = iso => { const d = new Date(String(iso).slice(0, 10) + 'T12:00'); return Number.isNaN(d.getTime()) ? '' : DATUM.format(d); };
const KATEGORIE = { Wichtig: 'wichtig', Info: 'info', Update: 'update' };

let zustand = { eintraege: [], ungelesen: 0, darfVerwalten: false };
let login = null;
let timer = null;
const hoerer = new Set();

/** Ansichten (z. B. Neuigkeiten-Seite) über Änderungen informieren. */
export const beobachten = f => { hoerer.add(f); return () => hoerer.delete(f); };
export const daten = () => zustand;

/** Eine Karte im Infocenter bzw. auf der Seite „Neuigkeiten“. */
export function karteHtml(x, { werkzeuge = '' } = {}) {
  const text = x.text ? `<p class="ic-text">${e(x.text).replace(/\n/g, '<br>')}</p>` : '';
  const punkte = (x.punkte || []).length ? `<ul class="ic-punkte">${x.punkte.map(p => `<li>${e(p)}</li>`).join('')}</ul>` : '';
  return `<article class="ic-karte ${x.gelesen ? '' : 'neu'} ${x.wichtig ? 'wichtig' : ''}" data-schluessel="${e(x.schluessel)}">
    <div class="ic-meta"><span class="ic-kat ${KATEGORIE[x.kategorie] ?? ''}">${e(x.kategorie)}</span><span class="ic-datum">${e(datumText(x.datum))}</span>
      ${x.gelesen ? '' : '<span class="ic-punkt" title="ungelesen" aria-label="ungelesen"></span>'}${werkzeuge}</div>
    <h3>${e(x.titel)}</h3>${text}${punkte}</article>`;
}

function zahlSetzen() {
  const n = zustand.ungelesen;
  const z = document.querySelector('[data-ic-zahl]');
  if (z) { z.textContent = n > 9 ? '9+' : String(n); z.hidden = n <= 0; }
  document.querySelector('[data-ic-knopf]')?.setAttribute('aria-label', n ? `Infocenter – ${n} ungelesen` : 'Infocenter');
  // Zahl auch am Menüeintrag „Neuigkeiten“
  const link = document.querySelector('[data-neuigkeiten]');
  if (link) {
    let m = link.querySelector('.nw-zahl');
    if (n <= 0) m?.remove();
    else { if (!m) { m = document.createElement('span'); m.className = 'nw-zahl'; link.append(m); } m.textContent = n > 9 ? '9+' : String(n); }
  }
  hoerer.forEach(f => { try { f(zustand); } catch { /* Ansicht weg */ } });
}

function listeZeichnen() {
  const l = document.querySelector('[data-ic-liste]');
  if (!l) return;
  l.innerHTML = zustand.eintraege.length ? zustand.eintraege.slice(0, 30).map(x => karteHtml(x)).join('')
    : '<div class="ic-leer"><i class="bi bi-bell-slash" aria-hidden="true"></i><span>Keine Neuigkeiten</span></div>';
  document.querySelector('[data-ic-alle]')?.toggleAttribute('disabled', !zustand.ungelesen);
}

/** Einträge neu laden (z. B. nach dem Anlegen einer Info). */
export async function laden() {
  try {
    const d = await holen('/api/app/infocenter');
    zustand = { eintraege: d.eintraege ?? [], ungelesen: d.ungelesen ?? 0, darfVerwalten: !!d.darfVerwalten };
  } catch { return zustand; }
  zahlSetzen();
  listeZeichnen();
  return zustand;
}

/** Einträge als gelesen vermerken (ohne Liste: alle). */
export async function gelesen(schluessel) {
  const offen = schluessel ? schluessel.filter(s => zustand.eintraege.some(x => x.schluessel === s && !x.gelesen)) : null;
  if (offen && !offen.length) return;
  try { await senden('/api/app/infocenter/gelesen', offen ? { schluessel: offen } : { alle: true }); }
  catch (err) { fehler(err); return; }
  zustand.eintraege.forEach(x => { if (!offen || offen.includes(x.schluessel)) x.gelesen = true; });
  zustand.ungelesen = zustand.eintraege.filter(x => !x.gelesen).length;
  zahlSetzen();
  listeZeichnen();
}

/** Früher im Browser gemerkte gelesene Neuigkeiten einmalig auf den Server übernehmen. */
async function alteGelesenUebernehmen() {
  const merker = `untplan:infocenter-uebernommen:${login}`;
  try {
    if (localStorage.getItem(merker)) return;
    const roh = localStorage.getItem(`untplan:neuigkeiten-gelesen:${login || '-'}`);
    localStorage.setItem(merker, '1');
    if (!roh) return;
    const alt = new Set(JSON.parse(roh).map(s => 'n:' + s));
    const schluessel = zustand.eintraege.filter(x => x.art === 'neuigkeit' && !x.gelesen && alt.has(x.schluessel)).map(x => x.schluessel);
    if (schluessel.length) await gelesen(schluessel);
  } catch { /* privat oder beschädigt */ }
}

function oeffnen(auf) {
  const p = document.querySelector('[data-ic-panel]');
  const k = document.querySelector('[data-ic-knopf]');
  if (!p || !k) return;
  const offen = auf ?? p.hidden;
  p.hidden = !offen;
  k.setAttribute('aria-expanded', String(offen));
  document.querySelector('[data-ic-hinweis]')?.remove();
  if (offen) { listeZeichnen(); p.querySelector('.ic-panel-kopf button')?.focus(); }
}

/** Kurzer Hinweis am Knopf, wenn seit dem letzten Besuch etwas Neues dazugekommen ist. */
function hinweisZeigen() {
  const neu = zustand.eintraege.filter(x => !x.gelesen);
  if (!neu.length) return;
  const merker = `untplan:infocenter-hinweis:${login}`;
  let bekannt = [];
  try { bekannt = JSON.parse(localStorage.getItem(merker) || '[]'); } catch { /* privat */ }
  const frisch = neu.filter(x => !bekannt.includes(x.schluessel));
  try { localStorage.setItem(merker, JSON.stringify(neu.map(x => x.schluessel).slice(0, 200))); } catch { /* privat */ }
  if (!frisch.length || document.querySelector('[data-ic-hinweis]') || location.hash.startsWith('#/neuigkeiten')) return;
  const h = document.createElement('div');
  h.className = 'ic-hinweis';
  h.dataset.icHinweis = '';
  h.setAttribute('role', 'status');
  h.innerHTML = `<button type="button" class="ic-hinweis-text" data-ic-hinweis-oeffnen><b>${frisch.length === 1 ? 'Neu im Infocenter' : `${frisch.length} neue Einträge`}</b><span>${e(frisch[0].titel)}</span></button>
    <button type="button" class="ic-hinweis-zu" aria-label="Hinweis schließen" title="Schließen"><i class="bi bi-x-lg"></i></button>`;
  document.body.append(h);
  h.querySelector('[data-ic-hinweis-oeffnen]').onclick = () => oeffnen(true);
  h.querySelector('.ic-hinweis-zu').onclick = () => h.remove();
  setTimeout(() => h.remove(), 12000);
}

/** Knopf und Panel anlegen (bei jedem Aufbau der Hülle, z. B. nach Kontowechsel). */
export async function starten(kontext) {
  login = kontext.ich?.login ?? null;
  document.querySelectorAll('[data-ic-wurzel], [data-ic-hinweis]').forEach(x => x.remove());
  const w = document.createElement('div');
  w.className = 'ic-wurzel nicht-drucken';
  w.dataset.icWurzel = '';
  w.innerHTML = `<button type="button" class="ic-knopf" data-ic-knopf aria-haspopup="dialog" aria-expanded="false" aria-controls="ic-panel" title="Infocenter">
      <i class="bi bi-bell" aria-hidden="true"></i><span class="ic-zahl" data-ic-zahl hidden></span></button>
    <section class="ic-panel" id="ic-panel" data-ic-panel role="dialog" aria-label="Infocenter" hidden>
      <header class="ic-panel-kopf"><h2>Infocenter</h2>
        <button type="button" class="ic-link" data-ic-alle disabled>Alle als gelesen markieren</button>
        <button type="button" class="ic-zu" data-ic-zu aria-label="Infocenter schließen" title="Schließen"><i class="bi bi-x-lg"></i></button></header>
      <div class="ic-liste" data-ic-liste></div>
      <footer class="ic-panel-fuss"><a href="#/neuigkeiten" data-ic-seite>Alle Neuigkeiten</a><a href="#/neuigkeiten/verwalten" data-ic-verwalten hidden>Infos verwalten</a></footer>
    </section>`;
  document.body.append(w);
  w.querySelector('[data-ic-knopf]').onclick = ev => { ev.stopPropagation(); oeffnen(); };
  w.querySelector('[data-ic-zu]').onclick = () => oeffnen(false);
  w.querySelector('[data-ic-alle]').onclick = () => gelesen();
  w.querySelectorAll('.ic-panel-fuss a').forEach(a => a.onclick = () => oeffnen(false));
  w.querySelector('[data-ic-liste]').addEventListener('click', ev => {
    const k = ev.target.closest('.ic-karte.neu');
    if (k) gelesen([k.dataset.schluessel]);
  });
  if (!window.__infocenterHoerer) {
    window.__infocenterHoerer = true;
    document.addEventListener('click', ev => { if (!ev.target.closest('[data-ic-wurzel], [data-ic-hinweis], .modal')) oeffnen(false); });
    document.addEventListener('keydown', ev => { if (ev.key === 'Escape') oeffnen(false); });
  }
  await laden();
  w.querySelector('[data-ic-verwalten]').hidden = !zustand.darfVerwalten;
  await alteGelesenUebernehmen();
  hinweisZeigen();
  clearInterval(timer);
  timer = setInterval(async () => {
    if (document.hidden || !document.querySelector('[data-ic-wurzel]')) return;
    await laden();
    hinweisZeigen();
  }, 300000);
}
