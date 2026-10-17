// Modul „Planung“: manuelles Planen im Stundenplanfenster wie im Desktop – Blöcke/Doppelstunden verschieben und setzen
// (mit „Soll behindernder Unterricht entplant werden?“), Mehrfachauswahl (entplanen, fixieren), Kontextmenüs, Raum zuordnen /
// löschen (Raumliste), Tauschvorschläge, alle Stunden entplanen bzw. fixieren.
// Das Stundenplanfenster (stundenplan.js) nutzt die hier exportierten Funktionen; die Menüband-Einträge planung.* stehen unten.
import { h, leeren } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { dialog, frage, hinweis, fehlerMelden, laden, meldung } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { befehl } from '../kern/modulsystem.js';
import { symbol } from '../kern/symbole.js';

// ---------------------------------------------------------------------------------------------
// Hilfen

/** Dialog breiter als der Standard (Raumliste, Tauschvorschläge). */
function breit(versprechen, breite) {
  const box = [...document.querySelectorAll('.dialog-hintergrund .dialog')].pop();
  if (box) { box.style.maxWidth = 'calc(100vw - 24px)'; box.style.width = breite; box.classList.add('pl-dialog'); }
  return versprechen;
}

/**
 * Ändernde Anfrage mit Rückfrage wie im Desktop: Antwortet der Server mit { ok:false, frage }, wird gefragt
 * „Soll behindernder Unterricht entplant werden?“ – Ja = entplanen, Nein = trotzdem (Kollision), Abbrechen = nichts.
 */
export async function mitRueckfrage(pfad, daten, quelle) {
  let antwort = await api.post(api.dok(pfad), daten, { quelle });
  if (antwort?.ok === false && antwort.frage) {
    const wahl = await dialog({
      titel: 'Behindernder Unterricht',
      inhalt: h('div', { class: 'pl-frage' }, h('pre', null, antwort.frage),
        h('p', { class: 'pl-leise' }, 'Ja: behindernden Unterricht entplanen (er erscheint unter „nicht verplant“) · Nein: trotzdem ausführen – die Kollision bleibt bestehen')),
      knoepfe: [{ text: 'Ja', wert: 'entplanen', primaer: true }, { text: 'Nein', wert: 'kollision' }, { text: 'Abbrechen', wert: null }],
      breite: '560px',
    });
    if (!wahl) return null;
    antwort = await api.post(api.dok(pfad), { ...daten, aktion: wahl }, { quelle });
  }
  return antwort;
}

/** Stunde bzw. Block (Drag & Drop im Raster) verschieben oder nicht verplante Stunden setzen. */
export async function ablegen(d, quelle) {
  if (d.tag !== undefined) {
    const erg = await mitRueckfrage('/planung/verschieben', { unterricht: d.u, tag: d.tag, stunde: d.stunde, zielTag: d.zielTag, zielStunde: d.zielStunde, einzeln: !!d.einzeln }, quelle);
    if (!erg || erg.unveraendert) return false;
    hinweis(erg.art === 'tausch' ? 'Stunden getauscht – Strg+Z macht es rückgängig.' : 'Stunde verschoben – Strg+Z macht es rückgängig.', 'ok', 2000);
    return true;
  }
  const erg = await mitRueckfrage('/planung/verplanen', { unterricht: d.u, tag: d.zielTag, stunde: d.zielStunde, laenge: d.laenge || 1 }, quelle);
  if (!erg) return false;
  hinweis((d.laenge || 1) > 1 ? 'Block verplant – Strg+Z macht es rückgängig.' : 'Stunde verplant – Strg+Z macht es rückgängig.', 'ok', 2000);
  return true;
}

/** Zielstatus mit Gründen für das Ziehen (verplante Stunde bzw. nicht verplanter Block). */
export function zielstatus(d) {
  const q = new URLSearchParams({ unterricht: d.u });
  if (d.tag !== undefined) { q.set('tag', d.tag); q.set('stunde', d.stunde); if (d.einzeln) q.set('einzeln', 'true'); } else q.set('laenge', d.laenge || 1);
  return api.get(api.dok(`/planung/zielstatus?${q}`));
}

/** Markierte Stunden entplanen (Mehrfachauswahl, eine Rückgängig-Stufe). */
export async function entplanen(stunden, quelle) {
  if (!stunden.length) { hinweis('Bitte zuerst eine Stunde im Stundenplan anklicken.', 'warnung'); return false; }
  const fixiert = stunden.some((s) => s.e?.fix);
  if (fixiert && !(await frage('Unter den gewählten Stunden sind fixierte Stunden. Fixierte Stunden entplanen?'))) return false;
  await api.post(api.dok('/planung/entplanen'), { stunden: stunden.map(ref), auchFixierte: fixiert }, { quelle });
  hinweis(stunden.length > 1 ? `${stunden.length} Stunden entplant.` : 'Stunde entplant.', 'ok', 2000);
  return true;
}

/** Markierte Stunden fixieren bzw. lösen (Ziel nach der ersten Stunde wie im Desktop). */
export async function fixieren(stunden, quelle) {
  if (!stunden.length) { hinweis('Bitte zuerst eine Stunde im Stundenplan anklicken.', 'warnung'); return false; }
  const ziel = !stunden[0].e?.fix;
  await api.post(api.dok('/planung/fixieren'), { stunden: stunden.map(ref), fixieren: ziel }, { quelle });
  hinweis(ziel ? 'Fixiert.' : 'Fixierung gelöst.', 'ok', 1500);
  return true;
}

const ref = (s) => ({ unterricht: s.u, tag: s.tag, stunde: s.stunde });

/** Stundenplanfenster, auf das sich Menüband-Befehle beziehen: das aktive, sonst das zuletzt benutzte. */
export function planFenster() {
  const aktiv = fensterverwaltung.aktiv();
  if (aktiv?.typ === 'stundenplan' && aktiv.planung) return aktiv;
  const alle = fensterverwaltung.alle().filter((f) => f.typ === 'stundenplan' && f.planung);
  return alle.sort((a, b) => Number(b.el.style.zIndex) - Number(a.el.style.zIndex))[0] || null;
}

function gewaehlteStunde(hinweisText = 'Bitte zuerst eine Stunde im Stundenplan anklicken.') {
  const f = planFenster();
  const a = f?.planung.raster.auswahl();
  if (!a) {
    if (!f) befehl('stundenplan.oeffnen', { typ: 'klasse' });
    meldung(hinweisText);
    return null;
  }
  return { f, a };
}

// ---------------------------------------------------------------------------------------------
// Raum zuordnen / löschen

const UMFANG = ['Unterrichtsstunde', 'Stundenblock', 'Alle Stunden eines Unterr.'];
let umfangMerken = 1;
let nurFreiMerken = false;

/** Dialog „Raum zuordnen / löschen“ für eine verplante Stunde { u, tag, stunde }. Liefert true, wenn etwas geändert wurde. */
export async function raumDialog(stunde, quelle) {
  let daten = null;
  let zeile = 0;
  let umfang = umfangMerken;
  let geaendert = false;
  let raumAuswahl = null;
  const schreiben = darfSchreiben();

  const info = h('div', { class: 'pl-info' });
  const radios = UMFANG.map((t, i) => h('label', null, h('input', { type: 'radio', name: 'pl-umfang', value: i, checked: i === umfang }), ` ${t}`));
  const lang = h('input', { type: 'checkbox' });
  const nurFrei = h('input', { type: 'checkbox', checked: nurFreiMerken });
  const nurWunsch = h('input', { type: 'checkbox' });
  const nurKap = h('input', { type: 'checkbox' });
  const suche = h('input', { class: 'eingabe', type: 'search', placeholder: 'Raum suchen …' });
  const links = h('tbody');
  const rechts = h('tbody');
  const zuordnenKnopf = h('button', { class: 'knopf', type: 'button', title: 'Markierten Raum zuordnen (Doppelklick, Enter)', disabled: !schreiben }, '←');
  const loeschenKnopf = h('button', { class: 'knopf', type: 'button', title: 'Raum der markierten Kopplungszeile löschen (Entf)', disabled: !schreiben }, symbol('loeschen'));
  const rechtsTabelle = h('table', { class: 'pl-tabelle pl-raeume', tabindex: '0' },
    h('thead', null, h('tr', null, ['Rm.', 'Kap.', 'Ausw.-Rm.', 'Stammr.', 'Besetzt', 'Besetzt durch', 'Raumgruppe', 'Gang', 'Wo.-Bel.', 'Std.frei', 'Kap.Diff.'].map((t) => h('th', null, t)))),
    rechts);
  const inhalt = h('div', { class: 'pl-raum' },
    h('div', { class: 'pl-kopf' }, info,
      h('fieldset', { class: 'pl-umfang' }, h('legend', null, 'Anwenden auf'), radios),
      h('div', { class: 'pl-filter' },
        h('label', null, lang, ' Langname anzeigen'), h('label', null, nurFrei, ' Nur freie Räume'),
        h('label', null, nurWunsch, ' Nur Wunschräume / Raumgruppe'), h('label', null, nurKap, ' Nur ausreichende Kapazität'), suche)),
    h('div', { class: 'pl-mitte' },
      h('div', { class: 'pl-links' }, h('div', { class: 'pl-titel' }, 'Auswahl derzeitiger Unterricht:'),
        h('div', { class: 'pl-rollen' }, h('table', { class: 'pl-tabelle', tabindex: '0' },
          h('thead', null, h('tr', null, ['Unt.', 'Lehrer', 'Fach', 'Raum', 'Klasse(n)', 'Wunschraum', 'U-Gruppe'].map((t) => h('th', null, t)))), links))),
      h('div', { class: 'pl-pfeile' }, zuordnenKnopf, loeschenKnopf),
      h('div', { class: 'pl-rechts' }, h('div', { class: 'pl-titel' }, 'Mögliche Räume:'), h('div', { class: 'pl-rollen' }, rechtsTabelle))),
    h('div', { class: 'pl-leise' }, 'grün = Wunschraum bzw. Raumgruppe · rot = besetzt (in mindestens einer Stunde) · grau = gesperrt · Std.frei = freie Stunden von den betroffenen Stunden'));

  const name = (r) => (lang.checked && r.lang ? r.lang : r.kurz);

  function zeichnen() {
    const d = daten;
    info.replaceChildren(
      h('div', null, h('b', null, `Unterricht: ${d.unterricht.id}`), `: ${d.unterricht.text}`),
      h('div', null, `Stunden: ${d.stunden.length}  ${d.stunden.map((s) => s.text).join(', ')}`),
      h('div', null, `Raum: ${[...new Set(d.stunden.map((s) => s.raum).filter(Boolean))].join(', ') || '—'}`));
    leeren(links);
    d.zeilen.forEach((z) => {
      const tr = h('tr', { class: z.nr === zeile ? 'gewaehlt' : '', onclick: () => { zeile = z.nr; neu(); } },
        h('td', { class: 'pl-unr' }, z.nr === 0 ? String(d.unterricht.id) : ''), h('td', null, z.lehrer), h('td', null, z.fach), h('td', null, z.raum),
        h('td', null, z.klassen), h('td', null, z.wunschraum), h('td', null, z.ugruppe));
      links.append(tr);
    });
    leeren(rechts);
    const f = suche.value.trim().toLowerCase();
    for (const r of d.raeume) {
      if (nurFrei.checked && r.frei < r.gesamt) continue;
      if (nurWunsch.checked && !r.wunsch) continue;
      if (nurKap.checked && r.kapDiff !== null && r.kapDiff < 0) continue;
      if (f && !`${r.kurz} ${r.lang} ${r.raumgruppen}`.toLowerCase().includes(f)) continue;
      const kl = r.gesperrt > 0 && r.frei === 0 ? 'gesperrt' : r.besetzt ? 'besetzt' : r.wunsch ? 'wunsch' : '';
      const tr = h('tr', { class: `${kl}${raumAuswahl === r.id ? ' gewaehlt' : ''}`, dataset: { id: r.id } },
        h('td', null, name(r)), h('td', { class: 'z' }, r.kap > 0 ? String(r.kap) : ''), h('td', { class: 'm' }, r.ausweich ? '✓' : ''), h('td', { class: 'm' }, r.stamm ? '✓' : ''),
        h('td', { class: 'm ok' }, r.besetzt ? '✓' : ''), h('td', null, r.besetztDurch), h('td', null, r.raumgruppen), h('td', null, r.gang),
        h('td', { class: 'z' }, String(r.wochenbelegung)), h('td', { class: `z${r.frei < r.gesamt ? ' rot' : ''}` }, String(r.frei)),
        h('td', { class: `z${r.kapDiff !== null && r.kapDiff < 0 ? ' rot' : ''}` }, r.kapDiff === null ? '' : (r.kapDiff > 0 ? `+${r.kapDiff}` : String(r.kapDiff))));
      tr.addEventListener('click', () => { raumAuswahl = r.id; for (const x of rechts.children) x.classList.toggle('gewaehlt', x === tr); });
      tr.addEventListener('dblclick', () => { raumAuswahl = r.id; zuordnen().catch(fehlerMelden); });
      rechts.append(tr);
    }
  }

  async function neu() {
    const q = new URLSearchParams({ unterricht: stunde.u, tag: stunde.tag, stunde: stunde.stunde, zeile, umfang });
    const weg = laden(inhalt);
    try { daten = await api.get(api.dok(`/planung/raum?${q}`)); zeile = daten.zeile; zeichnen(); } finally { weg(); }
  }

  async function senden(raum, extra = {}) {
    let antwort = await api.post(api.dok('/planung/raum'), { unterricht: stunde.u, tag: stunde.tag, stunde: stunde.stunde, zeile, umfang, raum, ...extra }, { quelle });
    if (antwort?.ok === false) {
      if (antwort.art === 'gesperrt') {
        if (!(await frage(antwort.frage))) return;
        antwort = await api.post(api.dok('/planung/raum'), { unterricht: stunde.u, tag: stunde.tag, stunde: stunde.stunde, zeile, umfang, raum, ...extra, trotzGesperrt: true }, { quelle });
        if (antwort?.ok === false && antwort.art === 'belegt') return senden(raum, { ...extra, trotzGesperrt: true });
      } else {
        const opt = antwort.optionen.map((o, i) => h('label', { class: 'pl-option' }, h('input', { type: 'radio', name: 'pl-loesung', value: o.wert, checked: i === 0 }), ` ${o.text}`));
        const ok = await dialog({
          titel: 'Raum nicht frei',
          inhalt: h('div', null, h('pre', { class: 'pl-frage' }, antwort.frage), h('fieldset', { class: 'pl-umfang' }, h('legend', null, 'Optionen'), opt)),
          knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }],
        });
        if (!ok) return;
        const loesung = opt.map((l) => l.querySelector('input')).find((i) => i.checked)?.value || 'kollision';
        return senden(raum, { ...extra, loesung });
      }
    }
    if (antwort?.raeume) { daten = antwort; geaendert = true; zeichnen(); }
  }

  async function zuordnen() {
    if (!schreiben) return;
    if (!raumAuswahl) { hinweis('Bitte rechts einen Raum markieren.', 'warnung'); return; }
    await senden(raumAuswahl);
  }

  zuordnenKnopf.addEventListener('click', () => zuordnen().catch(fehlerMelden));
  loeschenKnopf.addEventListener('click', () => senden(null).catch(fehlerMelden));
  radios.forEach((l) => l.querySelector('input').addEventListener('change', (e) => { umfang = umfangMerken = Number(e.target.value); neu().catch(fehlerMelden); }));
  for (const c of [lang, nurWunsch, nurKap]) c.addEventListener('change', zeichnen);
  nurFrei.addEventListener('change', () => { nurFreiMerken = nurFrei.checked; zeichnen(); });
  suche.addEventListener('input', zeichnen);
  rechtsTabelle.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); zuordnen().catch(fehlerMelden); }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const zeilen = [...rechts.children];
      const i = zeilen.findIndex((x) => x.classList.contains('gewaehlt'));
      const n = zeilen[Math.max(0, Math.min(zeilen.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
      if (n) { n.click(); n.scrollIntoView({ block: 'nearest' }); }
    }
  });
  inhalt.addEventListener('keydown', (e) => { if (e.key === 'Delete' && e.target.tagName !== 'INPUT') { e.preventDefault(); senden(null).catch(fehlerMelden); } });

  await neu();
  await breit(dialog({ titel: 'Raum zuordnen / löschen', inhalt, knoepfe: [{ text: 'Schließen', wert: true, primaer: true }] }), 'min(1180px, 96vw)');
  return geaendert;
}

// ---------------------------------------------------------------------------------------------
// Tauschvorschläge

/** Tauschvorschläge mit Gewinn (Verbesserung der Bewertung) für eine Stunde; „Tauschen“ führt den gewählten Tausch aus. */
export async function tauschDialog(stunde, quelle) {
  const liste = h('tbody');
  const inhalt = h('div', { class: 'pl-tausch' },
    h('div', { class: 'pl-rollen' }, h('table', { class: 'pl-tabelle' }, h('thead', null, h('tr', null, h('th', null, 'Gewinn'), h('th', null, 'Tausch'))), liste)),
    h('div', { class: 'pl-leise' }, 'Gewinn = Verbesserung der Bewertungszahl (positiv = besser). Angeboten werden nur Täusche, die keine neuen Kollisionen erzeugen.'));
  let gewaehlt = null;
  let vorschlaege = [];
  const p = breit(dialog({
    titel: 'Tauschvorschläge', inhalt,
    knoepfe: [{ text: 'Tauschen', wert: 'tauschen', primaer: true }, { text: 'Abbrechen', wert: null }],
    pruefen: () => { if (!gewaehlt) { hinweis('Bitte einen Tausch auswählen.', 'warnung'); return false; } return true; },
  }), 'min(820px, 96vw)');
  const weg = laden(inhalt);
  try {
    vorschlaege = await api.get(api.dok(`/planung/tausch?unterricht=${stunde.u}&tag=${stunde.tag}&stunde=${stunde.stunde}`));
  } catch (e) { fehlerMelden(e); } finally { weg(); }
  if (!vorschlaege.length) liste.append(h('tr', null, h('td', { colspan: 2, class: 'pl-leise' }, 'Für diese Stunde wurden keine Tauschmöglichkeiten gefunden.')));
  for (const v of vorschlaege) {
    const tr = h('tr', null, h('td', { class: `z ${v.gewinn > 0 ? 'gruen' : v.gewinn < 0 ? 'rot' : ''}` }, v.gewinn > 0 ? `+${v.gewinn}` : String(v.gewinn)), h('td', null, v.beschreibung));
    tr.addEventListener('click', () => { gewaehlt = v; for (const x of liste.children) x.classList.toggle('gewaehlt', x === tr); });
    tr.addEventListener('dblclick', () => { gewaehlt = v; inhalt.closest('.dialog')?.querySelector('.knopf.primaer')?.click(); });
    liste.append(tr);
  }
  if (vorschlaege.length) liste.firstChild.click();
  const wahl = await p;
  if (wahl !== 'tauschen' || !gewaehlt) return false;
  if (!darfSchreiben()) return false;
  await api.post(api.dok('/planung/tauschen'), { a: gewaehlt.a, b: gewaehlt.b }, { quelle });
  hinweis('Stunden getauscht – Strg+Z macht es rückgängig.', 'ok', 2500);
  return true;
}

// ---------------------------------------------------------------------------------------------
// Kontextmenü und Tastatur im Stundenplanfenster

/**
 * Einträge des Kontextmenüs einer Stunde (wie im Desktop). ctx = { f, raster, typ, element(), neuLaden(), schreiben }.
 */
export function kontextEintraege(ctx, e, tag, stunde) {
  const { raster, schreiben } = ctx;
  const markiert = raster.markierte();
  const mehrere = raster.markiert.size > 1;
  const eintraege = [{ titel: `U-Nr. ${e.u} · ${e.fach} ${e.klassen}` }];
  if (mehrere) {
    eintraege.push(
      { text: `${markiert.length} markierte Stunden entplanen`, symbol: 'loeschen', taste: 'Entf', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => entplanen(markiert, ctx.f.id)) },
      { text: 'Markierte Stunden fixieren / lösen', symbol: 'fixieren', taste: 'F', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => fixieren(markiert, ctx.f.id)) },
      { text: 'Markierung aufheben', taste: 'Esc', aktion: () => raster.markierungAufheben() },
      null);
  }
  const a = raster.auswahl() || { u: e.u, tag, stunde, e };
  eintraege.push(
    { text: 'Unterricht bearbeiten …', symbol: 'unterricht', aktion: () => befehl('unterricht.oeffnen', { sicht: ctx.typ === 'raum' || ctx.typ === 'fach' ? 'alle' : ctx.typ, element: ctx.typ === 'raum' || ctx.typ === 'fach' ? undefined : ctx.element() }) },
    { text: 'Ganzen Block markieren', aktion: () => blockMarkieren(ctx, a) },
    { text: 'Alle Stunden des Unterrichts markieren', aktion: () => raster.alleMarkieren((x) => x.u === e.u) },
    { text: e.fix ? 'Fixierung lösen' : 'Unterr.-Std. fixieren', symbol: 'fixieren', taste: 'F', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => fixieren(mehrere ? markiert : [{ ...a, e }], ctx.f.id)) },
    { text: 'Raum zuordnen / löschen …', symbol: 'raeume', aktion: () => aktion(ctx, () => raumDialog(a, ctx.f.id)) },
    { text: 'Tauschvorschläge …', symbol: 'tausch', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => tauschDialog(a, ctx.f.id)) },
    { text: 'Stunde vom Unterricht entplanen', symbol: 'loeschen', taste: mehrere ? undefined : 'Entf', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => entplanen([{ u: a.u, tag: a.tag, stunde: a.stunde, e }], ctx.f.id)) },
    { text: 'Zeitwünsche des Unterrichts', symbol: 'zeitwunsch', aktion: () => befehl('zeitwuensche.oeffnen', { typ: 'unterricht', element: e.u }) },
    null,
    { text: 'Alle Stunden markieren', taste: 'Strg+A', aktion: () => raster.alleMarkieren() },
  );
  return eintraege;
}

/** Kontextmenü auf einem leeren Feld. */
export function feldEintraege(ctx) {
  const { raster, schreiben } = ctx;
  const markiert = raster.markierte();
  return [
    raster.markiert.size ? { text: `${markiert.length} markierte Stunden entplanen`, symbol: 'loeschen', deaktiviert: !schreiben, aktion: () => aktion(ctx, () => entplanen(markiert, ctx.f.id)) } : null,
    raster.markiert.size ? { text: 'Markierung aufheben', aktion: () => raster.markierungAufheben() } : null,
    { text: 'Alle Stunden markieren', taste: 'Strg+A', aktion: () => raster.alleMarkieren() },
    null,
    { text: 'Diagnose', symbol: 'diagnose', aktion: () => befehl('optimierung.diagnose') },
    { text: 'Zeitwünsche', symbol: 'zeitwunsch', aktion: () => befehl('zeitwuensche.oeffnen', { typ: ctx.typ, element: ctx.element() }) },
  ].filter((x, i, l) => x !== null || (i > 0 && l[i - 1] !== null));
}

function blockMarkieren(ctx, a) {
  const stunden = new Set(a.stunden || [a.stunde]);
  // zusammenhängende Stunden desselben Unterrichts am Tag (Block im Sinne des Desktops)
  const plan = ctx.raster.plan;
  const belegt = new Set(plan.zellen.filter((z) => z.tag === a.tag && z.eintraege.some((x) => x.u === a.u)).map((z) => z.stunde));
  for (let s = Math.min(...stunden) - 1; belegt.has(s); s--) stunden.add(s);
  for (let s = Math.max(...stunden) + 1; belegt.has(s); s++) stunden.add(s);
  ctx.raster.alleMarkieren(() => false);
  for (const el of ctx.raster.gitter.querySelectorAll('.r-eintrag')) {
    const d = el._daten;
    if (d.e.u === a.u && d.tag === a.tag && d.stunden.some((s) => stunden.has(s))) ctx.raster._markieren(el, true);
  }
  ctx.raster.o.beiMarkierung?.(ctx.raster.markierte());
}

async function aktion(ctx, f) {
  const geaendert = await f();
  if (geaendert) await ctx.neuLaden();
}

/** Tastatur im Raster: Entf entplanen, F fixieren, Strg+A alle markieren, Esc Markierung aufheben. Liefert true, wenn behandelt. */
export function taste(ctx, ev) {
  const { raster, schreiben } = ctx;
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'a') { raster.alleMarkieren(); return true; }
  if (ev.key === 'Escape' && raster.markiert.size) { raster.markierungAufheben(); return true; }
  if (ev.ctrlKey || ev.metaKey || ev.altKey || !schreiben || !ctx.planen()) return false;
  if (ev.key === 'Delete') { aktion(ctx, () => entplanen(raster.markierte(), ctx.f.id)).catch(fehlerMelden); return true; }
  if (ev.key === 'f' || ev.key === 'F') { aktion(ctx, () => fixieren(raster.markierte(), ctx.f.id)).catch(fehlerMelden); return true; }
  return false;
}

// ---------------------------------------------------------------------------------------------
// Menüband-Befehle

async function befehlTausch() {
  const g = gewaehlteStunde('Tauschvorschläge erhalten Sie im Stundenplanfenster: bitte zuerst eine Stunde anklicken.');
  if (g && (await tauschDialog(g.a, g.f.id))) await g.f.planung.neuLaden();
}

async function befehlRaum() {
  const g = gewaehlteStunde();
  if (g && (await raumDialog(g.a, g.f.id))) await g.f.planung.neuLaden();
}

async function befehlAlleEntplanen() {
  const n = zustand.dokument?.zaehler?.planstunden;
  if (!(await frage(`Alle nicht fixierten Stunden entplanen?${n ? `\n(${n} Stunden im Plan)` : ''}`, { gefahr: true, ja: 'Entplanen', nein: 'Abbrechen' }))) return;
  const erg = await api.post(api.dok('/planung/alle-entplanen'), { fixierteBehalten: true });
  hinweis(`${erg.entplant} Stunden entplant${erg.verbleibend ? `, ${erg.verbleibend} fixierte bleiben` : ''} – Strg+Z macht es rückgängig.`, 'ok', 4000);
}

async function befehlAlleFixieren({ fixieren: fix = true } = {}) {
  await api.post(api.dok('/planung/alle-fixieren'), { fixieren: fix });
  hinweis(fix ? 'Alle Stunden fixiert.' : 'Alle Fixierungen aufgehoben.', 'ok', 2500);
}

export default {
  id: 'planung',
  css: ['css/module/planung.css'],
  befehle: {
    'planung.tausch': befehlTausch,
    'planung.raum': befehlRaum,
    'planung.alleEntplanen': befehlAlleEntplanen,
    'planung.alleFixieren': befehlAlleFixieren,
  },
  ribbon: [
    { id: 'planung.tausch', reiter: 'Planung', gruppe: 'Manuelles Planen', text: 'Tauschvorschläge', symbol: 'tausch', gross: true, befehl: 'planung.tausch', braucht: 'schreiben', hinweis: 'Tauschvorschläge für die gewählte Stunde im Stundenplanfenster' },
    { id: 'planung.raum', reiter: 'Planung', gruppe: 'Manuelles Planen', text: 'Raum zuordnen', symbol: 'raeume', gross: true, befehl: 'planung.raum', braucht: 'dokument', hinweis: 'Raum der gewählten Stunde zuordnen bzw. löschen' },
    { id: 'planung.entplanen', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Alle Stunden entplanen', symbol: 'loeschen', befehl: 'planung.alleEntplanen', braucht: 'schreiben' },
    { id: 'planung.fixieren', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Fixierungen', symbol: 'fixieren', braucht: 'schreiben',
      menue: [
        { text: 'Alle Stunden fixieren', symbol: 'fixieren', befehl: 'planung.alleFixieren', args: { fixieren: true } },
        { text: 'Alle Fixierungen aufheben', symbol: 'fixieren', befehl: 'planung.alleFixieren', args: { fixieren: false } },
      ] },
    { id: 'start.planen', reiter: 'Start', gruppe: 'Planungswerkzeuge', text: 'Planen', symbol: 'stundenplan', gross: true, braucht: 'dokument',
      menue: [
        { text: 'Stunden mit der Hand verplanen', symbol: 'stundenplan', befehl: 'stundenplan.oeffnen', args: { typ: 'klasse' } },
        { text: 'Alle Stunden entplanen', symbol: 'loeschen', befehl: 'planung.alleEntplanen', braucht: 'schreiben' },
        { text: 'Alle Stunden fixieren', symbol: 'fixieren', befehl: 'planung.alleFixieren', args: { fixieren: true }, braucht: 'schreiben' },
        { text: 'Alle Fixierungen aufheben', symbol: 'fixieren', befehl: 'planung.alleFixieren', args: { fixieren: false }, braucht: 'schreiben' },
        null,
        { text: 'Tauschvorschläge', symbol: 'tausch', befehl: 'planung.tausch', braucht: 'schreiben' },
      ] },
  ],
};

