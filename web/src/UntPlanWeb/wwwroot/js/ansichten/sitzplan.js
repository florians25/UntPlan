// Klassenbuch → Sitzplan (wie das Original-Modul „Sitzplan“, im nl-design):
// - Klassenbuch-Einstellungen als Seitenpanel (Zahnrad in den Stundendetails): Ansicht Liste/Sitzplan, Sortierung (Nachname/Vorname, nach Klasse),
//   Reihenfolge (von links nach rechts / von oben nach unten), Darstellung der Namen (Vorname zuerst, Kurzform), Klasse anzeigen (Mischgruppen).
// - Sitzplan-Editor als großes Fenster: Spalten/Reihen + „Anwenden“, Lehrerpult oben/unten, Tische entfernen (Gänge), Drag & Drop
//   (Maus, Touch, Stift über Pointer-Events; Tippen-und-Tippen bzw. Tastatur als Alternative), Zufällig verteilen, Alphabetisch füllen,
//   Leeren, Rückgängig, Drucken (PDF, S/W), Wirkungsbereich Klasse/Unterricht/Raum mit Rechteprüfung, Speichern/Löschen.
// - Sitzplan-Anordnung der Schülerkacheln in den Stundendetails (rasterHtml) und die Seite „Sitzplan“ einer Klasse (zeigen).
// Gültiger Plan einer Stunde (Server): Unterricht vor Raum vor Klasse.
import { e, holen, senden, toast, filterFeld, leer, pdfAusrichtung } from '../ui.js';

// ------------------------------------------------------------------ Stil und Einstellungen

export function cssLaden() {
  if (document.querySelector('link[data-sitzplan-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/sitzplan.css', import.meta.url).href;
  l.dataset.sitzplanCss = '';
  document.head.append(l);
}

const SCHLUESSEL = 'untplan:klassenbuch-einstellungen';
const VORGABE = { ansicht: 'liste', sortierung: 'name', nachKlasse: false, richtung: 'zeilen', namen: 'nachname', klasseZeigen: false };

/** Vorgabe der Schule (Klassenbuch | Einstellungen: Sortierung, Anordnung, nach Klasse) – gilt, solange das Gerät nichts anderes gespeichert hat. */
let schulVorgabe = {};
export function schulVorgabeSetzen(w) { schulVorgabe = { ...w }; }

/** Klassenbuch-Einstellungen dieses Geräts (Ansicht, Sortierung, Namensdarstellung). */
export function einstellungen() {
  try { return { ...VORGABE, ...schulVorgabe, ...JSON.parse(localStorage.getItem(SCHLUESSEL) || '{}') }; } catch { return { ...VORGABE, ...schulVorgabe }; }
}
export function einstellungenSpeichern(w) {
  try { localStorage.setItem(SCHLUESSEL, JSON.stringify(w)); } catch { /* privat */ }
}

const de = (a, b) => (a || '').localeCompare(b || '', 'de', { sensitivity: 'base' });

/** Schülerliste nach den Einstellungen sortieren (Kopie). */
export function sortieren(liste, w = einstellungen()) {
  const n = w.sortierung === 'vorname' ? (a, b) => de(a.vorname, b.vorname) || de(a.name, b.name) : (a, b) => de(a.name, b.name) || de(a.vorname, b.vorname);
  return liste.slice().sort((a, b) => (w.nachKlasse ? de(a.klasse, b.klasse) : 0) || n(a, b));
}

/** Zwei Namenszeilen [fett, normal] nach der Darstellung „Nachname Vorname“, „Vorname Nachname“ bzw. Kurzform „Vorname N.“. */
export function nameZeilen(s, w = einstellungen()) {
  if (w.namen === 'kurz') return [s.vorname || s.name, s.vorname && s.name ? `${s.name[0]}.` : ''];
  return w.namen === 'vorname' ? [s.vorname, s.name] : [s.name, s.vorname];
}
export const anzeigeName = (s, w = einstellungen()) => nameZeilen(s, w).filter(Boolean).join(' ');

/** Namens-HTML für die Schülerkacheln der Stundendetails (zwei Zeilen, optional Klasse). */
export function nameHtml(s, w = einstellungen()) {
  const [a, b] = nameZeilen(s, w);
  return `<span>${e(a)}</span><span>${e(b)}</span>${w.klasseZeigen && s.klasse ? `<span class="sp-kl">${e(s.klasse)}</span>` : ''}`;
}

const initialenVon = s => `${(s.vorname || '')[0] ?? ''}${(s.name || '')[0] ?? ''}`.toUpperCase() || '?';
const FARBEN = ['#e4ecf7', '#e6f3ec', '#f6ece2', '#efe7f6', '#f6e6ea', '#e3f1f3', '#f3f1e0'];
const farbe = id => FARBEN[Math.abs(Number(id) || 0) % FARBEN.length];

function karteHtml(s, w, extra = '') {
  const [a, b] = nameZeilen(s, w);
  return `<div class="sp-karte" data-karte="${s.id}" tabindex="0" role="button" aria-label="${e(anzeigeName(s, w))}${s.klasse ? `, Klasse ${e(s.klasse)}` : ''}" ${extra}>
    <span class="sp-ava" style="--ava:${farbe(s.id)}" aria-hidden="true">${e(initialenVon(s))}</span>
    <span class="sp-nm"><b>${e(a)}</b>${b ? `<span>${e(b)}</span>` : ''}${w.klasseZeigen && s.klasse ? `<span class="sp-kl">${e(s.klasse)}</span>` : ''}</span></div>`;
}

const BEREICHE = [['klasse', 'Klasse'], ['unterricht', 'Unterricht'], ['raum', 'Raum']];
const BEREICH_TEXT = {
  klasse: 'Gilt in jedem Unterricht der Klasse (nur Klassenleitung).',
  unterricht: 'Gilt nur in diesem Unterricht – der Klassen-Sitzplan wird hier nicht mehr angezeigt.',
  raum: 'Gilt in Ihren Stunden in diesem Raum (z. B. Fachraum).',
};
const OHNE_RECHT = {
  klasse: 'Einen Klassen-Sitzplan legt nur die Klassenleitung an (bei Unterricht mit einer Klasse).',
  unterricht: 'Nur für eigene Unterrichte.',
  raum: 'Nur für Räume eigener Unterrichte.',
};
export const bereichName = b => ({ klasse: 'Klasse', unterricht: 'Unterricht', raum: 'Raum' })[b] ?? '';

/** Overlay im Stundendetails-Dialog bzw. auf der Seite: Esc schließt nur das Overlay. */
function overlayWurzel(wurzel) { return wurzel ?? document.body; }

// ------------------------------------------------------------------ Klassenbuch-Einstellungen (Seitenpanel)

/**
 * Seitenpanel „Klassenbuch-Einstellungen“. Änderungen gelten sofort (onAendern) und werden auf dem Gerät gespeichert.
 * sitzplan: { darf, onOeffnen } – Knopf „Sitzplan bearbeiten“.
 */
export function einstellungenPanel({ wurzel, onAendern, sitzplan } = {}) {
  cssLaden();
  const ziel = overlayWurzel(wurzel);
  ziel.querySelector(':scope > .sp-panel')?.remove();
  ziel.querySelector(':scope > .sp-panel-hg')?.remove();
  let w = einstellungen();
  const beispiel = { id: 3, vorname: 'Emma', name: 'Schulz', klasse: '7a' };
  const hg = document.createElement('div');
  hg.className = 'sp-panel-hg';
  const p = document.createElement('aside');
  p.className = 'sp-panel';
  p.setAttribute('role', 'dialog');
  p.setAttribute('aria-modal', 'true');
  p.setAttribute('aria-labelledby', 'sp-panel-titel');
  const radio = (name, wert, text, hinweis = '') => `<label class="sp-wahl"><input type="radio" name="${name}" value="${wert}" ${w[name] === wert ? 'checked' : ''}><span>${text}${hinweis ? `<small>${hinweis}</small>` : ''}</span></label>`;
  const haken = (name, text, hinweis = '') => `<label class="sp-wahl"><input type="checkbox" name="${name}" ${w[name] ? 'checked' : ''}><span>${text}${hinweis ? `<small>${hinweis}</small>` : ''}</span></label>`;
  const vorschau = () => `<div class="sp-vorschau">${karteHtml(beispiel, w, 'tabindex="-1" aria-hidden="true"')}${karteHtml({ id: 1, vorname: 'Ben', name: 'Aydin', klasse: '7b' }, w, 'tabindex="-1" aria-hidden="true"')}</div>`;
  p.innerHTML = `
    <header><i class="bi bi-gear" aria-hidden="true"></i><h2 id="sp-panel-titel">Klassenbuch-Einstellungen</h2><button type="button" data-zu title="Schließen" aria-label="Schließen">×</button></header>
    <div class="sp-panel-rumpf">
      <fieldset><legend>Schüler*innen anzeigen als</legend>
        <div class="sp-seg" role="radiogroup" aria-label="Ansicht">
          <button type="button" role="radio" data-ansicht="liste" class="${w.ansicht !== 'sitzplan' ? 'an' : ''}" aria-checked="${w.ansicht !== 'sitzplan'}"><i class="bi bi-grid-3x3-gap"></i>Liste</button>
          <button type="button" role="radio" data-ansicht="sitzplan" class="${w.ansicht === 'sitzplan' ? 'an' : ''}" aria-checked="${w.ansicht === 'sitzplan'}"><i class="bi bi-easel"></i>Sitzplan</button>
        </div></fieldset>
      <fieldset><legend>Sortierung der Schülerliste</legend>
        ${radio('sortierung', 'name', 'Nach Familienname')}${radio('sortierung', 'vorname', 'Nach Vorname')}
        ${haken('nachKlasse', 'Schüler*innen nach Klasse sortieren', 'Praktisch bei Mischgruppen')}
        <div class="mt-2"></div>
        ${radio('richtung', 'zeilen', 'Von links nach rechts', 'Auch: Reihenfolge beim alphabetischen Füllen des Sitzplans')}${radio('richtung', 'spalten', 'Von oben nach unten')}</fieldset>
      <fieldset><legend>Darstellung der Namen</legend>
        ${radio('namen', 'nachname', 'Familienname vor Vorname')}${radio('namen', 'vorname', 'Vorname vor Familienname')}${radio('namen', 'kurz', 'Kurzform', 'Vorname und Anfangsbuchstabe, z. B. „Emma S.“')}
        ${haken('klasseZeigen', 'Klasse anzeigen', 'Klassenzugehörigkeit bei Mischgruppen')}
        <div data-vorschau>${vorschau()}</div></fieldset>
      <p class="sp-panel-hinweis">Die Einstellungen gelten auf diesem Gerät für alle Stunden.</p>
    </div>
    <footer>${sitzplan ? `<button type="button" class="sp-knopf primaer" data-sitzplan ${sitzplan.darf ? '' : 'disabled title="Keine Berechtigung für einen Sitzplan in dieser Stunde"'}><i class="bi bi-easel"></i>Sitzplan bearbeiten</button>` : ''}
      <button type="button" class="sp-knopf" data-zu>Schließen</button></footer>`;
  ziel.append(hg, p);
  const zu = () => { hg.remove(); p.remove(); };
  const aendern = () => { einstellungenSpeichern(w); p.querySelector('[data-vorschau]').innerHTML = vorschau(); onAendern?.(w); };
  hg.onclick = zu;
  p.querySelectorAll('[data-zu]').forEach(b => b.onclick = zu);
  p.addEventListener('keydown', ev => { if (ev.key === 'Escape') { ev.stopPropagation(); zu(); } });
  p.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => {
    w = { ...w, ansicht: b.dataset.ansicht };
    p.querySelectorAll('[data-ansicht]').forEach(x => { x.classList.toggle('an', x === b); x.setAttribute('aria-checked', String(x === b)); });
    aendern();
  });
  p.querySelectorAll('input').forEach(i => i.onchange = () => { w = { ...w, [i.name]: i.type === 'checkbox' ? i.checked : i.value }; aendern(); });
  p.querySelector('[data-sitzplan]')?.addEventListener('click', () => { zu(); sitzplan.onOeffnen?.(); });
  p.querySelector('[data-zu]').focus();
  return { schliessen: zu };
}

// ------------------------------------------------------------------ Daten einer Stunde

/** Kontext einer Stundenkarte für die Sitzplan-Endpunkte. */
export function kontextVon(k) {
  const kurz = l => (l ?? []).filter(x => x && (x.k ?? x) && !(x.alt && !x.k)).map(x => x.k ?? x);
  return { unterricht: k.unterricht, raeume: kurz(k.raeume), klassen: kurz(k.klassen), lehrer: kurz(k.lehrer).length === 1 ? kurz(k.lehrer)[0] : null };
}
const abfrage = kx => `unterricht=${kx.unterricht ?? ''}&raeume=${encodeURIComponent((kx.raeume ?? []).join(','))}&klassen=${encodeURIComponent((kx.klassen ?? []).join(','))}${kx.lehrer ? `&lehrer=${encodeURIComponent(kx.lehrer)}` : ''}`;

/** Sitzpläne einer Stunde laden: { kontext, schueler, gueltig, plaene, rechte }. */
export const stundeLaden = kx => holen(`/api/app/sitzplaene/stunde?${abfrage(kx)}`);

export const darfIrgendwas = d => !!(d?.rechte && (d.rechte.klasse || d.rechte.unterricht || d.rechte.raum));

// ------------------------------------------------------------------ Sitzplan-Anordnung (Stundendetails)

/**
 * Schülerkacheln in Sitzplan-Anordnung. kachel(s) liefert die Kachel der Stundendetails (Abwesenheit per Klick wie in der Liste).
 * Liefert '' ohne gültigen Plan.
 */
export function rasterHtml(d, schueler, kachel, w = einstellungen()) {
  const plan = d?.gueltig;
  if (!plan) return '';
  cssLaden();
  const nachId = new Map(schueler.map(s => [s.id, s]));
  const ohne = new Set(plan.ohneTisch ?? []);
  const belegung = plan.belegung ?? {};
  const hinten = plan.pult === 'hinten';
  const zellen = [];
  for (let dz = 0; dz < plan.zeilen; dz++) for (let ds = 0; ds < plan.spalten; ds++) {
    const z = hinten ? plan.zeilen - dz : dz + 1, sp = hinten ? plan.spalten - ds : ds + 1;
    const key = `${z}-${sp}`, s = nachId.get(belegung[key]);
    zellen.push(s ? kachel(s) : ohne.has(key) ? '<div class="sp-a-gang" aria-hidden="true"></div>' : `<div class="sp-a-leer" title="Reihe ${z}, Platz ${sp} – frei"></div>`);
  }
  const gesetzt = new Set(Object.values(belegung));
  const rest = sortieren(schueler.filter(s => !gesetzt.has(s.id)), w);
  const pult = '<div class="sp-pult"><i class="bi bi-easel2" aria-hidden="true"></i>Lehrerpult</div>';
  return `<div class="sp-anordnung" style="--spalten:${plan.spalten}"><div class="sp-anordnung-raum">${hinten ? '' : pult}
      <div class="sp-anordnung-raster">${zellen.join('')}</div>${hinten ? pult : ''}</div></div>
    <div class="sp-a-quelle">Sitzplan: ${e(bereichName(plan.bereich))}${plan.geaendert ? ` · geändert ${e(plan.geaendert.slice(8, 10))}.${e(plan.geaendert.slice(5, 7))}.${e(plan.geaendert.slice(0, 4))}` : ''}</div>
    ${rest.length ? `<div class="sp-a-ohne"><div class="sp-a-ohne-titel">Ohne Sitzplatz (${rest.length})</div><div class="sd-kacheln">${rest.map(kachel).join('')}</div></div>` : ''}`;
}

// ------------------------------------------------------------------ Druck

async function pdfOeffnen(daten) {
  const ausrichtung = await pdfAusrichtung('quer', 'sitzplan');
  if (!ausrichtung) return;
  daten = { ...daten, ausrichtung };
  const fenster = window.open('', '_blank');
  try {
    const r = await fetch('/api/app/sitzplaene/pdf', { method: 'POST', credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(daten) });
    if (!r.ok) throw new Error((await r.json().catch(() => null))?.fehler ?? `Fehler ${r.status}`);
    const url = URL.createObjectURL(await r.blob());
    if (fenster) fenster.location.href = url;
    else { const a = document.createElement('a'); a.href = url; a.download = 'sitzplan.pdf'; document.body.append(a); a.click(); a.remove(); }
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  } catch (err) { fenster?.close(); toast(err.message, true); }
}

// ------------------------------------------------------------------ Editor

/**
 * Sitzplan-Editor als großes Fenster.
 * d: Antwort von /api/app/sitzplaene/stunde (bzw. gleich aufgebaut für die Klassen-Seite).
 * kontext: { unterricht, raeume, klassen, lehrer, klasse }; titel/untertitel für Kopf und Druck; bereich: Vorauswahl.
 * onGespeichert() nach Speichern oder Löschen.
 */
export function editorOeffnen({ wurzel, d, kontext = {}, titel = 'Sitzplan', untertitel = '', bereich, onGespeichert } = {}) {
  cssLaden();
  const ziel = overlayWurzel(wurzel);
  const w = einstellungen();
  const schueler = sortieren(d.schueler ?? [], w);
  const nachId = new Map(schueler.map(s => [s.id, s]));
  const rechte = d.rechte ?? {};
  const erlaubt = b => !!rechte[b];
  const start = d.gueltig && erlaubt(d.gueltig.bereich) ? d.gueltig.bereich
    : bereich && erlaubt(bereich) ? bereich : ['unterricht', 'klasse', 'raum'].find(erlaubt) ?? 'unterricht';
  const vorlage = (d.gueltig ?? d.plaene?.[start] ?? null);

  const st = {
    zeilen: vorlage?.zeilen ?? 5, spalten: vorlage?.spalten ?? 6, pult: vorlage?.pult ?? 'vorne',
    belegung: new Map(Object.entries(vorlage?.belegung ?? {}).filter(([, id]) => nachId.has(id))),
    ohne: new Set(vorlage?.ohneTisch ?? []), bereich: start, tische: false, suche: '', gewaehlt: null, geaendert: false,
  };
  const verlauf = [];
  const merken = () => { verlauf.push({ zeilen: st.zeilen, spalten: st.spalten, pult: st.pult, belegung: [...st.belegung], ohne: [...st.ohne] }); if (verlauf.length > 60) verlauf.shift(); };
  const platzVon = id => { for (const [k, v] of st.belegung) if (v === id) return k; return null; };
  const imRaster = k => { const [z, s] = k.split('-').map(Number); return z >= 1 && z <= st.zeilen && s >= 1 && s <= st.spalten; };

  const o = document.createElement('div');
  o.className = 'sp-overlay';
  o.innerHTML = `<div class="sp-fenster" role="dialog" aria-modal="true" aria-labelledby="sp-titel">
    <header class="sp-kopf"><div><h2 id="sp-titel">${e(titel)}</h2>${untertitel ? `<div class="sp-unter">${e(untertitel)}</div>` : ''}</div>
      <button type="button" class="sp-x" data-zu title="Schließen" aria-label="Schließen">×</button></header>
    <div class="sp-leiste">
      <div class="sp-groesse">
        <label class="sp-zahl">Spalten <span class="sp-stepper"><button type="button" data-schritt="spalten:-1" aria-label="Eine Spalte weniger">−</button><input type="number" min="1" max="15" data-spalten value="${st.spalten}" aria-label="Spalten"><button type="button" data-schritt="spalten:1" aria-label="Eine Spalte mehr">+</button></span></label>
        <label class="sp-zahl">Reihen <span class="sp-stepper"><button type="button" data-schritt="zeilen:-1" aria-label="Eine Reihe weniger">−</button><input type="number" min="1" max="15" data-zeilen value="${st.zeilen}" aria-label="Reihen"><button type="button" data-schritt="zeilen:1" aria-label="Eine Reihe mehr">+</button></span></label>
        <button type="button" class="sp-knopf" data-anwenden>Anwenden</button>
      </div>
      <div class="sp-seg klein" role="radiogroup" aria-label="Lehrerpult" data-t-pult></div>
      <div class="sp-aktionen">
        <button type="button" class="sp-knopf" data-tische aria-pressed="false" title="Plätze ohne Tisch festlegen (Gänge, Lücken)"><i class="bi bi-layout-wtf"></i>Tische</button>
        <button type="button" class="sp-knopf" data-zufall title="Alle Schüler*innen zufällig verteilen"><i class="bi bi-shuffle"></i>Zufällig</button>
        <button type="button" class="sp-knopf" data-alpha title="Alphabetisch füllen (Reihenfolge laut Klassenbuch-Einstellungen)"><i class="bi bi-sort-alpha-down"></i>Alphabetisch</button>
        <button type="button" class="sp-knopf" data-leeren title="Alle Plätze leeren"><i class="bi bi-eraser"></i>Leeren</button>
        <button type="button" class="sp-knopf" data-zurueck title="Rückgängig (Strg+Z)"><i class="bi bi-arrow-counterclockwise"></i>Rückgängig</button>
        <button type="button" class="sp-knopf" data-drucken title="Als PDF drucken (Schwarz-Weiß)"><i class="bi bi-printer"></i>Drucken</button>
      </div>
    </div>
    <div class="sp-arbeit" data-arbeit><div class="sp-saal" data-t-raum></div></div>
    <section class="sp-ablage" data-ablage aria-label="Nicht zugeordnete Schüler*innen">
      <div class="sp-ablage-kopf"><h3>Nicht zugeordnet <span class="sp-anzahl" data-t-anzahl></span></h3>
        <label class="sp-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Name suchen …" aria-label="Schüler*in suchen"></label></div>
      <div class="sp-liste" data-t-liste></div>
    </section>
    <footer class="sp-fuss" data-t-fuss></footer>
  </div>`;
  ziel.append(o);
  const q = s => o.querySelector(s);

  // ---------------------------------------------------------------- Zeichnen

  function raumHtml() {
    const hinten = st.pult === 'hinten';
    const pult = '<div class="sp-pult"><i class="bi bi-easel2" aria-hidden="true"></i>Lehrerpult</div>';
    let zellen = '';
    for (let dz = 0; dz < st.zeilen; dz++) for (let ds = 0; ds < st.spalten; ds++) {
      const z = hinten ? st.zeilen - dz : dz + 1, sp = hinten ? st.spalten - ds : ds + 1;
      const k = `${z}-${sp}`, s = nachId.get(st.belegung.get(k));
      const nr = `<span class="sp-nr">${z}·${sp}</span>`;
      if (st.ohne.has(k)) zellen += `<div class="sp-sitz kein-tisch" data-platz="${k}" ${st.tische ? `role="button" tabindex="0" aria-label="Reihe ${z}, Platz ${sp}: kein Tisch – Tisch hinstellen"` : 'aria-hidden="true"'}>${st.tische ? '<i class="bi bi-plus-lg"></i>' : ''}</div>`;
      else if (s) zellen += `<div class="sp-sitz belegt" data-platz="${k}">${karteHtml(s, w, st.gewaehlt === s.id ? 'aria-pressed="true"' : '').replace('class="sp-karte"', `class="sp-karte ${st.gewaehlt === s.id ? 'gewaehlt' : ''}"`)}${nr}</div>`;
      else zellen += `<div class="sp-sitz leer" data-platz="${k}" role="button" tabindex="0" aria-label="Reihe ${z}, Platz ${sp}: frei${st.tische ? ' – Tisch entfernen' : ''}">${st.tische ? '<i class="bi bi-x-lg"></i>' : '<span class="sp-plus" aria-hidden="true">+</span>'}${nr}</div>`;
    }
    return `${hinten ? '' : pult}<div class="sp-raster ${st.tische ? 'sp-tischmodus' : ''}" style="--spalten:${st.spalten}">${zellen}</div>${hinten ? pult : ''}`;
  }

  function listeHtml() {
    const gesetzt = new Set(st.belegung.values());
    const frei = schueler.filter(s => !gesetzt.has(s.id));
    const such = st.suche.trim().toLowerCase();
    const gefiltert = such ? frei.filter(s => `${s.vorname} ${s.name} ${s.name} ${s.vorname} ${s.klasse}`.toLowerCase().includes(such)) : frei;
    q('[data-t-anzahl]').textContent = String(frei.length);
    if (!schueler.length) return '<span class="sp-hinweis">Für diese Stunde ist keine Schülerliste vorhanden.</span>';
    if (!frei.length) return '<span class="sp-hinweis"><i class="bi bi-check2-circle"></i> Alle Schüler*innen haben einen Platz.</span>';
    if (!gefiltert.length) return '<span class="sp-hinweis">Kein Treffer.</span>';
    return gefiltert.map(s => karteHtml(s, w, st.gewaehlt === s.id ? 'aria-pressed="true"' : '').replace('class="sp-karte"', `class="sp-karte ${st.gewaehlt === s.id ? 'gewaehlt' : ''}"`)).join('');
  }

  function fussHtml() {
    const vorhanden = d.plaene?.[st.bereich];
    const seg = BEREICHE.map(([b, t]) => `<button type="button" role="radio" data-bereich="${b}" class="${st.bereich === b ? 'an' : ''}" aria-checked="${st.bereich === b}" ${erlaubt(b) ? '' : `disabled title="${e(OHNE_RECHT[b])}"`}>${d.plaene?.[b] ? '<span class="sp-punkt" title="Plan gespeichert"></span>' : ''}${t}</button>`).join('');
    const info = !erlaubt(st.bereich) ? OHNE_RECHT[st.bereich]
      : `${BEREICH_TEXT[st.bereich]}${vorhanden ? ` <button type="button" data-laden>Gespeicherten Plan laden</button>` : ''}`;
    return `<div class="sp-bereich"><span>Wirkungsbereich</span><div class="sp-seg" role="radiogroup" aria-label="Wirkungsbereich">${seg}</div>
        <div class="sp-bereich-info">${info}</div></div>
      <div class="sp-rechts">
        ${vorhanden && erlaubt(st.bereich) ? '<button type="button" class="sp-knopf rot" data-loeschen><i class="bi bi-trash3"></i>Löschen</button>' : ''}
        <button type="button" class="sp-knopf" data-zu>Abbrechen</button>
        <button type="button" class="sp-knopf primaer" data-speichern ${erlaubt(st.bereich) ? '' : 'disabled'}><i class="bi bi-check2"></i>Speichern</button></div>`;
  }

  function zeichnen() {
    const arbeit = q('[data-arbeit]'), sx = arbeit.scrollLeft, sy = arbeit.scrollTop;
    q('[data-t-raum]').innerHTML = raumHtml();
    q('[data-t-liste]').innerHTML = listeHtml();
    q('[data-t-pult]').innerHTML = [['vorne', 'Pult oben'], ['hinten', 'Pult unten']].map(([v, t]) => `<button type="button" role="radio" data-pult="${v}" class="${st.pult === v ? 'an' : ''}" aria-checked="${st.pult === v}">${t}</button>`).join('');
    q('[data-t-fuss]').innerHTML = fussHtml();
    q('[data-spalten]').value = st.spalten;
    q('[data-zeilen]').value = st.zeilen;
    q('[data-zurueck]').disabled = !verlauf.length;
    q('[data-tische]').classList.toggle('an', st.tische);
    q('[data-tische]').setAttribute('aria-pressed', String(st.tische));
    arbeit.scrollLeft = sx; arbeit.scrollTop = sy;
  }
  const geaendert = () => { st.geaendert = true; zeichnen(); };

  // ---------------------------------------------------------------- Aktionen

  /** Schüler*in auf einen Platz setzen (null = zurück in „nicht zugeordnet“); belegter Zielplatz → tauschen. */
  function setzen(id, zielKey) {
    const von = platzVon(id);
    if (zielKey === von || (zielKey && st.ohne.has(zielKey))) return;
    merken();
    if (!zielKey) st.belegung.delete(von);
    else {
      const anderer = st.belegung.get(zielKey);
      if (von) { if (anderer) st.belegung.set(von, anderer); else st.belegung.delete(von); }
      st.belegung.set(zielKey, id);
    }
    st.gewaehlt = null;
    geaendert();
  }

  /** Freie Plätze mit Tisch in Füllreihenfolge (vorne beginnend; links→rechts bzw. oben→unten). */
  function plaetzeInReihenfolge() {
    const l = [];
    if (w.richtung === 'spalten') { for (let s = 1; s <= st.spalten; s++) for (let z = 1; z <= st.zeilen; z++) l.push(`${z}-${s}`); }
    else for (let z = 1; z <= st.zeilen; z++) for (let s = 1; s <= st.spalten; s++) l.push(`${z}-${s}`);
    return l.filter(k => !st.ohne.has(k));
  }
  function fuellen(liste) {
    merken();
    st.belegung.clear();
    const plaetze = plaetzeInReihenfolge();
    liste.slice(0, plaetze.length).forEach((s, i) => st.belegung.set(plaetze[i], s.id));
    if (liste.length > plaetze.length) toast(`${liste.length - plaetze.length} Schüler*innen haben keinen Platz – bitte Raster vergrößern.`, true);
    st.gewaehlt = null;
    geaendert();
  }

  function anwenden() {
    const z = Math.max(1, Math.min(15, Number(q('[data-zeilen]').value) || 1));
    const s = Math.max(1, Math.min(15, Number(q('[data-spalten]').value) || 1));
    if (z === st.zeilen && s === st.spalten) { zeichnen(); return; }
    merken();
    st.zeilen = z; st.spalten = s;
    let raus = 0;
    for (const k of [...st.belegung.keys()]) if (!imRaster(k)) { st.belegung.delete(k); raus++; }
    for (const k of [...st.ohne]) if (!imRaster(k)) st.ohne.delete(k);
    if (raus) toast(`${raus} Schüler*in${raus === 1 ? '' : 'nen'} wieder in „nicht zugeordnet“.`);
    geaendert();
  }

  function zurueck() {
    const v = verlauf.pop();
    if (!v) return;
    Object.assign(st, { zeilen: v.zeilen, spalten: v.spalten, pult: v.pult, belegung: new Map(v.belegung), ohne: new Set(v.ohne), gewaehlt: null });
    geaendert();
  }

  function planDaten() {
    return {
      bereich: st.bereich, unterricht: kontext.unterricht ?? null, raeume: kontext.raeume ?? [], klassen: kontext.klassen ?? [], lehrer: kontext.lehrer ?? null,
      klasse: kontext.klasse ?? null, raum: d.kontext?.raum ?? null,
      zeilen: st.zeilen, spalten: st.spalten, pult: st.pult, belegung: Object.fromEntries(st.belegung), ohneTisch: [...st.ohne],
    };
  }

  async function speichern(knopf) {
    knopf.disabled = true;
    try {
      const neu = await senden('/api/app/sitzplaene', planDaten(), 'PUT');
      d.plaene = { ...(d.plaene ?? {}), [st.bereich]: neu };
      st.geaendert = false;
      toast(`Sitzplan gespeichert – gilt für: ${bereichName(st.bereich)}.`);
      schliessen(true);
      onGespeichert?.(neu);
    } catch (err) { toast(err.message, true); knopf.disabled = false; }
  }

  async function loeschen() {
    if (!confirm(`Sitzplan für den Wirkungsbereich „${bereichName(st.bereich)}“ löschen?`)) return;
    const p = planDaten();
    const qs = `bereich=${p.bereich}&${abfrage(p)}${p.klasse ? `&klasse=${encodeURIComponent(p.klasse)}` : ''}${p.raum ? `&raum=${encodeURIComponent(p.raum)}` : ''}`;
    try {
      await senden(`/api/app/sitzplaene?${qs}`, undefined, 'DELETE');
      toast('Sitzplan gelöscht.');
      schliessen(true);
      onGespeichert?.(null);
    } catch (err) { toast(err.message, true); }
  }

  function schliessen(ohneFrage = false) {
    if (!ohneFrage && st.geaendert && !confirm('Änderungen am Sitzplan verwerfen?')) return;
    o.remove();
    document.removeEventListener('keydown', tasten, true);
  }

  // ---------------------------------------------------------------- Ereignisse

  o.addEventListener('click', ev => {
    const t = ev.target;
    if (t === o) return schliessen();
    if (t.closest('[data-zu]')) return schliessen();
    const schritt = t.closest('[data-schritt]');
    if (schritt) {
      const [feld, delta] = schritt.dataset.schritt.split(':');
      const inp = q(`[data-${feld}]`);
      inp.value = Math.max(1, Math.min(15, (Number(inp.value) || 1) + Number(delta)));
      return anwenden();
    }
    if (t.closest('[data-anwenden]')) return anwenden();
    const pult = t.closest('[data-pult]');
    if (pult) { if (st.pult !== pult.dataset.pult) { merken(); st.pult = pult.dataset.pult; geaendert(); } return; }
    if (t.closest('[data-tische]')) { st.tische = !st.tische; st.gewaehlt = null; zeichnen(); return; }
    if (t.closest('[data-zufall]')) {
      const l = schueler.slice();
      for (let i = l.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [l[i], l[j]] = [l[j], l[i]]; }
      return fuellen(l);
    }
    if (t.closest('[data-alpha]')) return fuellen(schueler);
    if (t.closest('[data-leeren]')) { if (st.belegung.size) { merken(); st.belegung.clear(); st.gewaehlt = null; geaendert(); } return; }
    if (t.closest('[data-zurueck]')) return zurueck();
    if (t.closest('[data-drucken]')) return pdfOeffnen({ ...planDaten(), titel, vornameZuerst: w.namen === 'vorname', kurz: w.namen === 'kurz', klasseZeigen: w.klasseZeigen });
    const b = t.closest('[data-bereich]');
    if (b && !b.disabled) { st.bereich = b.dataset.bereich; zeichnen(); return; }
    if (t.closest('[data-laden]')) {
      const p = d.plaene?.[st.bereich];
      if (!p) return;
      merken();
      Object.assign(st, { zeilen: p.zeilen, spalten: p.spalten, pult: p.pult, belegung: new Map(Object.entries(p.belegung ?? {}).filter(([, id]) => nachId.has(id))), ohne: new Set(p.ohneTisch ?? []) });
      geaendert();
      return;
    }
    if (t.closest('[data-loeschen]')) return loeschen();
    const sp = t.closest('[data-speichern]');
    if (sp) return speichern(sp);
    if (ziehen.unterdrueckeKlick) { ziehen.unterdrueckeKlick = false; return; }
    // Tippen statt Ziehen: Karte wählen, dann Platz bzw. Liste antippen
    const karte = t.closest('[data-karte]');
    const platz = t.closest('[data-platz]');
    if (st.tische && platz) {
      merken();
      const k = platz.dataset.platz;
      if (st.ohne.has(k)) st.ohne.delete(k); else if (!st.belegung.has(k)) st.ohne.add(k); else { verlauf.pop(); toast('Auf diesem Platz sitzt jemand – zuerst die Schülerin bzw. den Schüler entfernen.', true); return; }
      return geaendert();
    }
    if (karte) {
      const id = Number(karte.dataset.karte);
      if (st.gewaehlt !== null && st.gewaehlt !== id && platz) return setzen(st.gewaehlt, platz.dataset.platz);
      st.gewaehlt = st.gewaehlt === id ? null : id;
      return zeichnen();
    }
    if (platz && st.gewaehlt !== null) return setzen(st.gewaehlt, platz.dataset.platz);
    if (t.closest('[data-ablage]') && st.gewaehlt !== null && platzVon(st.gewaehlt)) return setzen(st.gewaehlt, null);
  });
  o.addEventListener('keydown', ev => {
    if (ev.key === 'Escape') { ev.stopPropagation(); ev.preventDefault(); if (st.gewaehlt !== null) { st.gewaehlt = null; zeichnen(); } else schliessen(); return; }
    if ((ev.key === 'Enter' || ev.key === ' ') && ev.target.matches('[data-karte], [data-platz][role=button]')) { ev.preventDefault(); ev.target.click(); restoreFokus(ev.target); }
    if (ev.key === 'Enter' && ev.target.matches('[data-zeilen], [data-spalten]')) { ev.preventDefault(); anwenden(); }
    if ((ev.key === 'Delete' || ev.key === 'Backspace') && ev.target.matches('.sp-sitz [data-karte]')) { ev.preventDefault(); setzen(Number(ev.target.dataset.karte), null); }
  });
  /** Nach dem Neuzeichnen den Fokus auf denselben Platz bzw. dieselbe Karte setzen (Tastaturbedienung). */
  function restoreFokus(el) {
    const sel = el.dataset.karte ? `[data-karte="${el.dataset.karte}"]` : el.dataset.platz ? `[data-platz="${el.dataset.platz}"]` : null;
    if (sel) requestAnimationFrame(() => (q(sel)?.matches('[tabindex]') ? q(sel) : q(`${sel} [data-karte]`))?.focus());
  }
  function tasten(ev) {
    if (!o.isConnected) return;
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'z' && !ev.target.matches('input')) { ev.preventDefault(); zurueck(); }
  }
  document.addEventListener('keydown', tasten, true);
  q('[data-suche]').addEventListener('input', ev => { st.suche = ev.target.value; q('[data-t-liste]').innerHTML = listeHtml(); });

  // ---------------------------------------------------------------- Drag & Drop (Pointer-Events: Maus, Touch, Stift)

  const ziehen = { unterdrueckeKlick: false };
  o.addEventListener('pointerdown', ev => {
    const karte = ev.target.closest('[data-karte]');
    if (!karte || st.tische || (ev.pointerType === 'mouse' && ev.button !== 0)) return;
    const id = Number(karte.dataset.karte);
    const r = karte.getBoundingClientRect();
    const dx = ev.clientX - r.left, dy = ev.clientY - r.top;
    const sx = ev.clientX, sy = ev.clientY;
    let geist = null, ziel = null, scrollTimer = null;
    const arbeit = q('[data-arbeit]');
    try { karte.setPointerCapture(ev.pointerId); } catch { /* ohne Capture */ }
    const markieren = el => { if (ziel === el) return; ziel?.classList.remove('ziel'); ziel = el; ziel?.classList.add('ziel'); };
    const bewegen = m => {
      if (!geist) {
        if (Math.hypot(m.clientX - sx, m.clientY - sy) < 6) return;
        geist = karte.cloneNode(true);
        geist.classList.add('sp-geist');
        geist.removeAttribute('data-karte');
        geist.style.width = `${r.width}px`;
        o.append(geist);
        karte.classList.add('sp-zieht');
      }
      geist.style.transform = `translate(${m.clientX - dx}px, ${m.clientY - dy}px)`;
      const unter = document.elementFromPoint(m.clientX, m.clientY);
      markieren(unter?.closest('.sp-sitz:not(.kein-tisch)') ?? unter?.closest('[data-ablage]') ?? null);
      // am Rand der Arbeitsfläche automatisch rollen
      clearInterval(scrollTimer);
      const a = arbeit.getBoundingClientRect(), rand = 40;
      const vy = m.clientY < a.top + rand && m.clientY > a.top - 10 ? -12 : m.clientY > a.bottom - rand && m.clientY < a.bottom + 10 ? 12 : 0;
      const vx = m.clientX < a.left + rand ? -12 : m.clientX > a.right - rand ? 12 : 0;
      if (vx || vy) scrollTimer = setInterval(() => { arbeit.scrollTop += vy; arbeit.scrollLeft += vx; }, 30);
    };
    const ende = u => {
      karte.removeEventListener('pointermove', bewegen);
      karte.removeEventListener('pointerup', ende);
      karte.removeEventListener('pointercancel', ende);
      clearInterval(scrollTimer);
      if (!geist) return; // Klick → Tippen-und-Tippen (click-Ereignis)
      geist.remove();
      karte.classList.remove('sp-zieht');
      ziel?.classList.remove('ziel');
      ziehen.unterdrueckeKlick = true;
      setTimeout(() => { ziehen.unterdrueckeKlick = false; }, 0);
      if (u.type === 'pointercancel' || !ziel) return;
      if (ziel.matches('[data-ablage]')) { if (platzVon(id)) setzen(id, null); }
      else setzen(id, ziel.dataset.platz);
    };
    karte.addEventListener('pointermove', bewegen);
    karte.addEventListener('pointerup', ende);
    karte.addEventListener('pointercancel', ende);
  });

  zeichnen();
  q('.sp-x').focus();
  return { schliessen };
}

// ------------------------------------------------------------------ Seite „Sitzplan“ einer Klasse

export async function zeigen(seite, kontext, [klasseParam]) {
  cssLaden();
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : eigene ?? el.klassen[0]?.kurz ?? '';
  if (!klasse) { kontext.titel('Sitzplan'); seite.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  const r = await holen(`/api/app/sitzplan/${encodeURIComponent(klasse)}`);
  const w = einstellungen();
  // gleich aufgebaut wie /api/app/sitzplaene/stunde
  const d = {
    kontext: { klasse, klassen: [klasse] }, schueler: r.schueler,
    gueltig: r.plan.vorhanden ? r.plan : null, plaene: { klasse: r.plan.vorhanden ? r.plan : null }, rechte: { klasse: r.verwalten },
  };
  const bearbeiten = () => editorOeffnen({ d, kontext: { klasse, klassen: [klasse] }, titel: `Sitzplan ${klasse}`, untertitel: `Klasse ${klasse} · gilt in jedem Unterricht der Klasse`, bereich: 'klasse', onGespeichert: () => kontext.navigieren(`#/sitzplan/${encodeURIComponent(klasse)}`) });
  kontext.titel(`Sitzplan ${klasse}`, `${r.verwalten ? '<button class="btn btn-primary" data-bearbeiten><i class="bi bi-easel me-1"></i>Sitzplan bearbeiten</button>' : ''}
    <button class="btn btn-outline-secondary" data-drucken ${r.plan.vorhanden ? '' : 'disabled'}><i class="bi bi-printer me-1"></i>Drucken</button>
    <button class="btn btn-outline-secondary" data-einstellungen title="Klassenbuch-Einstellungen" aria-label="Klassenbuch-Einstellungen"><i class="bi bi-gear"></i></button>`);
  const kachel = s => `<div class="sd-kachel da" data-sid="${s.id}"><span class="sd-avatar"><span class="sp-ava" style="--ava:${farbe(s.id)};width:100%;height:100%">${e(initialenVon(s))}</span></span><span class="sd-name">${nameHtml(s, w)}</span></div>`;
  seite.innerHTML = `<div class="sp-seite">
    <div class="sp-seite-leiste">${filterFeld('Klasse', `<select class="form-select" data-klasse>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}</div>
    ${!r.schueler.length ? leer('Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.', 'people')
      : d.gueltig ? rasterHtml(d, r.schueler, kachel, w)
      : `<div class="sp-a-hinweis"><i class="bi bi-easel"></i>Für die Klasse ${e(klasse)} ist noch kein Sitzplan hinterlegt.${r.verwalten ? ' <button type="button" class="sp-knopf primaer" data-bearbeiten2>Sitzplan erstellen</button>' : ''}</div>`}
    </div>`;
  seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(`#/sitzplan/${encodeURIComponent(ev.target.value)}`);
  document.querySelector('[data-bearbeiten]')?.addEventListener('click', bearbeiten);
  seite.querySelector('[data-bearbeiten2]')?.addEventListener('click', bearbeiten);
  document.querySelector('[data-drucken]')?.addEventListener('click', () => pdfOeffnen({ ...d.gueltig, bereich: 'klasse', klasse, klassen: [klasse], belegung: d.gueltig?.belegung ?? {}, titel: `Sitzplan ${klasse}`, vornameZuerst: w.namen === 'vorname', kurz: w.namen === 'kurz', klasseZeigen: w.klasseZeigen }));
  document.querySelector('[data-einstellungen]')?.addEventListener('click', () => einstellungenPanel({ onAendern: () => kontext.navigieren(location.hash), sitzplan: r.verwalten ? { darf: true, onOeffnen: bearbeiten } : null }));
}
