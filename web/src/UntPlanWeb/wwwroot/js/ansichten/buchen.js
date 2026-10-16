// Buchen: Raum buchen, Ressource buchen und Neue Aktivität als mehrstufiger Assistent (Termin → Kriterien → Freie Räume → Details →
// Speichern) mit Periodizität (Zeitraum, Wiederholung, Wochentage, maximale Belegung, je Termin anderer Raum), Meine Buchungen
// (Filter, Suche, Storno einzeln bzw. Serie, CSV), Raumübersicht (freie Stunden anklickbar), Ressourcenübersicht (Woche – Ressourcen)
// und Neuer Unterricht mit zusätzlichen Stunden. Verwaltung (Ressourcen, Raumdaten): buchungsverwaltung.js.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, alarm, heuteIso, plusTage, montag, herunterladen, WOCHENTAGE_KURZ } from '../ui.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const tagKurz = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' });
const tagLang = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const stdText = (v, b) => v === b ? `${v}. Std.` : `${v}.–${b}. Std.`;
const tagIndex = iso => (new Date(iso + 'T12:00').getDay() + 6) % 7;
const csv = zeilen => zeilen.map(z => z.map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');

/** Eigene Stile des Buchungsmoduls (einmal je Seite). */
function stileLaden() {
  if (document.querySelector('link[data-buchen-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = 'css/buchen.css';
  l.dataset.buchenCss = '1';
  document.head.append(l);
}

const ARTEN = {
  raum: { titel: 'Raum buchen', schritte: ['Termin', 'Kriterien', 'Freie Räume', 'Details', 'Speichern'] },
  ressource: { titel: 'Ressource buchen', schritte: ['Termin', 'Kriterien', 'Freie Ressourcen', 'Details', 'Speichern'] },
  aktivitaet: { titel: 'Neue Aktivität', schritte: ['Termin', 'Details', 'Raum', 'Speichern'] },
  unterricht: { titel: 'Zusätzliche Stunden', schritte: ['Termin', 'Raum', 'Speichern'] },
};
const ART_NAME = { raum: 'Raumbuchung', ressource: 'Ressourcenbuchung', aktivitaet: 'Aktivität', unterricht: 'Zusätzlicher Unterricht' };
const ART_CHIP = { raum: 'gruen', ressource: 'lila', aktivitaet: 'gelb', unterricht: 'grau' };
const WIEDERHOLUNG = [['einmalig', 'Einmalig'], ['taeglich', 'Täglich'], ['woechentlich', 'Jede Woche'], ['2wochen', 'Jede 2. Woche'], ['3wochen', 'Jede 3. Woche'], ['4wochen', 'Jede 4. Woche']];

export async function zeigen(seite, kontext, [bereich, ...rest]) {
  stileLaden();
  switch (bereich) {
    case 'ressource': return assistent(seite, kontext, 'ressource', rest);
    case 'aktivitaet': return assistent(seite, kontext, 'aktivitaet', rest);
    case 'stunden': return assistent(seite, kontext, 'unterricht', rest);
    case 'meine': return meineBuchungen(seite, kontext, rest);
    case 'raeume': return raumuebersicht(seite, kontext, rest);
    case 'ressourcen': return ressourcenuebersicht(seite, kontext, rest);
    case 'unterricht': return eigeneUnterrichte(seite, kontext);
    case 'verwaltung': case 'raumdaten': return (await import('./buchungsverwaltung.js')).zeigen(seite, kontext, bereich);
    default: return assistent(seite, kontext, 'raum', rest);
  }
}

// ------------------------------------------------------------------ Bausteine

const feld = (beschriftung, html, klasse = '') => `<label class="bu-feld ${klasse}"><span>${e(beschriftung)}</span>${html}</label>`;
const optionen = (liste, wert, leerText) => `${leerText != null ? `<option value="">${e(leerText)}</option>` : ''}${liste.map(x => {
  const [w, t, z] = Array.isArray(x) ? x : [x, x];
  return `<option value="${e(w)}" ${String(w) === String(wert ?? '') ? 'selected' : ''} ${z ? `data-zweit="${e(z)}"` : ''}>${e(t)}</option>`;
}).join('')}`;
const stundenOptionen = (raster, wert) => raster.map(z => `<option value="${z.nr}" ${z.nr === wert ? 'selected' : ''} data-zweit="${e(z.beginn)}–${e(z.ende)}">${z.nr}. Stunde</option>`).join('');

/** Zusammenfassung des Zeitbereichs als Text („Mi 04.11.2026 · 3.–4. Std.“ bzw. „jede 2. Woche Mi, Fr · 04.11.–18.12.2026 · …“). */
function zeitText(z) {
  const zeit = z.modus === 'uhrzeit' ? `${z.beginn}–${z.ende} Uhr` : stdText(z.stundeVon, z.stundeBis);
  if (z.wiederholung === 'einmalig') return `${tagKurz(z.datum)} · ${zeit}`;
  const w = WIEDERHOLUNG.find(x => x[0] === z.wiederholung)?.[1] ?? '';
  const tage = z.wiederholung === 'taeglich' ? '' : ` ${z.wochentage.slice().sort().map(t => WOCHENTAGE_KURZ[t]).join(', ')}`;
  return `${w}${tage} · ${datumDe(z.von)} – ${datumDe(z.bis)} · ${zeit}`;
}

function zeitDaten(z) {
  const d = { wiederholung: z.wiederholung, wochentage: z.wochentage };
  if (z.wiederholung === 'einmalig') d.datum = z.datum; else { d.von = z.von; d.bis = z.bis; }
  if (z.modus === 'uhrzeit') { d.beginn = z.beginn; d.ende = z.ende; } else { d.stundeVon = z.stundeVon; d.stundeBis = z.stundeBis; }
  return d;
}

/** Schritte-Leiste: erledigte Schritte sind anklickbar (zurück). */
function schritteLeiste(schritte, aktiv) {
  return `<ol class="bu-schritte" aria-label="Schritte">${schritte.map((s, i) => `<li class="${i < aktiv ? 'fertig' : i === aktiv ? 'aktiv' : ''}">
      <button type="button" data-schritt="${i}" ${i < aktiv ? '' : 'disabled'} ${i === aktiv ? 'aria-current="step"' : ''}>
        <span class="bu-nr">${i < aktiv ? '<i class="bi bi-check-lg"></i>' : i + 1}</span><span class="bu-sname">${e(s)}</span></button></li>`).join('')}</ol>`;
}

// ------------------------------------------------------------------ Assistent

async function assistent(seite, kontext, art, rest) {
  const def = ARTEN[art];
  const [opt, el] = await Promise.all([holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const raster = opt.zeitraster.filter(z => z.nr > 0);
  const ich = kontext.ich;
  const heute = heuteIso();
  let unterricht = null;
  if (art === 'unterricht') {
    unterricht = (await holen('/api/app/buchen/unterricht')).find(u => String(u.id) === rest[0]);
    if (!unterricht) { kontext.titel(def.titel); seite.innerHTML = alarm('warning', 'Der Unterricht wurde nicht gefunden.'); return; }
  }
  kontext.titel(art === 'unterricht' ? `Zusätzliche Stunden – ${unterricht.fach} ${unterricht.klassen.join(', ')}` : def.titel,
    `<a class="nl-knopf" href="#/buchen/meine"><i class="bi bi-list-ul"></i>Meine Buchungen</a>`);

  // Vorbelegung aus der Raumübersicht: #/buchen/raum/{datum}/{von}/{bis}/{raum}
  const [pDatum, pVon, pBis, pRaum] = rest;
  const vorbelegt = art === 'raum' && /^\d{4}-\d{2}-\d{2}$/.test(pDatum ?? '');
  const startDatum = vorbelegt ? pDatum : heute;
  const z = {
    wiederholung: 'einmalig', datum: startDatum, von: startDatum, bis: plusTage(startDatum, 56), wochentage: [Math.min(tagIndex(startDatum), 4)],
    modus: 'stunden', stundeVon: Number(pVon) || raster[0]?.nr || 1, stundeBis: Number(pBis) || Number(pVon) || raster[0]?.nr || 1,
    beginn: '13:30', ende: '14:15',
  };
  const zustand = {
    schritt: 0, zeit: z,
    kriterien: { ressourcenarten: [], raumart: '', kapazitaetMin: '', abteilung: '', gebaeude: '', raumgruppe: '', maxBelegung: 0 },
    suche: null, raum: vorbelegt ? pRaum : null, ersatz: {}, ressourcen: new Map(), termine: null,
    details: { klassen: [], lehrer: ich.element && opt.ich ? opt.ich : '', fach: '', titel: '', text: '', benachrichtigen: false },
    hinweise: [], gespeichert: null,
  };
  const S = def.schritte;
  const name = i => S[i];

  seite.innerHTML = '<div class="bu-assistent"><div data-leiste></div><div data-inhalt></div></div>';
  const leiste = seite.querySelector('[data-leiste]');
  const inhalt = seite.querySelector('[data-inhalt]');
  leiste.addEventListener('click', ev => {
    const b = ev.target.closest('[data-schritt]');
    if (b && !b.disabled) { zustand.schritt = Number(b.dataset.schritt); zeichnen(); }
  });

  const gehe = async i => { zustand.schritt = i; await zeichnen(); window.scrollTo(0, 0); };

  async function zeichnen() {
    leiste.innerHTML = zustand.gespeichert ? '' : schritteLeiste(S, zustand.schritt);
    if (zustand.gespeichert) return erfolg();
    const n = name(zustand.schritt);
    if (n === 'Termin') return terminSchritt();
    if (n === 'Kriterien') return kriterienSchritt();
    if (n === 'Freie Räume') return raumSchritt(false);
    if (n === 'Raum') return raumSchritt(true);
    if (n === 'Freie Ressourcen') return ressourcenSchritt();
    if (n === 'Details') return detailsSchritt();
    return speichernSchritt();
  }

  const knopfleiste = (zurueck, weiter, weiterText = 'Weiter', weiterSymbol = 'arrow-right') => `<div class="bu-knopfleiste">
      ${zurueck ? '<button type="button" class="nl-knopf bu-gross" data-zurueck><i class="bi bi-arrow-left"></i>Zurück</button>' : '<span></span>'}
      ${weiter ? `<button type="button" class="nl-hinzu" data-weiter>${e(weiterText)}<i class="bi bi-${weiterSymbol}"></i></button>` : ''}</div>`;
  const knoepfeVerbinden = (zurueck, weiter) => {
    inhalt.querySelector('[data-zurueck]')?.addEventListener('click', zurueck ?? (() => gehe(zustand.schritt - 1)));
    const w = inhalt.querySelector('[data-weiter]');
    if (w && weiter) w.addEventListener('click', async () => {
      w.disabled = true;
      try { await weiter(); } catch (err) { kontext.fehler(err); } finally { w.disabled = false; }
    });
  };

  // ---------------------------------------------------------------- Schritt: Termin

  function terminSchritt() {
    const einmalig = z.wiederholung === 'einmalig';
    const tage = WOCHENTAGE_KURZ.slice(0, opt.tageProWoche);
    inhalt.innerHTML = `
      ${unterricht ? unterrichtKopf(unterricht) : ''}
      <section class="bu-karte">
        <header><i class="bi bi-calendar-event"></i><h2>Zeitbereich</h2></header>
        <div class="bu-zeile">
          <div class="bu-segment" role="radiogroup" aria-label="Wiederholung">${WIEDERHOLUNG.map(([w, t]) => `<button type="button" role="radio" aria-checked="${w === z.wiederholung}" class="${w === z.wiederholung ? 'an' : ''}" data-wdh="${w}">${t}</button>`).join('')}</div>
        </div>
        <div class="bu-raster">
          ${einmalig ? feld('Datum', `<input type="date" class="form-control" data-z="datum" value="${z.datum}" min="${heute}">`)
            : `${feld('Von', `<input type="date" class="form-control" data-z="von" value="${z.von}" min="${heute}">`)}${feld('Bis', `<input type="date" class="form-control" data-z="bis" value="${z.bis}" min="${heute}">`)}`}
          <div class="bu-feld"><span>Zeit</span><div class="bu-segment klein" role="radiogroup" aria-label="Zeitangabe">
            <button type="button" role="radio" class="${z.modus === 'stunden' ? 'an' : ''}" aria-checked="${z.modus === 'stunden'}" data-modus="stunden">Stunden</button>
            <button type="button" role="radio" class="${z.modus === 'uhrzeit' ? 'an' : ''}" aria-checked="${z.modus === 'uhrzeit'}" data-modus="uhrzeit">Uhrzeit</button></div></div>
          ${z.modus === 'stunden'
            ? `${feld('Von Stunde', `<select class="form-select" data-z="stundeVon">${stundenOptionen(raster, z.stundeVon)}</select>`)}${feld('Bis Stunde', `<select class="form-select" data-z="stundeBis">${stundenOptionen(raster, z.stundeBis)}</select>`)}`
            : `${feld('Beginn', `<input type="time" class="form-control" data-z="beginn" value="${z.beginn}" step="300">`)}${feld('Ende', `<input type="time" class="form-control" data-z="ende" value="${z.ende}" step="300">`)}`}
        </div>
        ${einmalig || z.wiederholung === 'taeglich' ? '' : `<div class="bu-feld"><span>Wochentage</span><div class="bu-tage">${tage.map((t, i) => `<button type="button" class="bu-tag ${z.wochentage.includes(i) ? 'an' : ''}" aria-pressed="${z.wochentage.includes(i)}" data-tag="${i}">${t}</button>`).join('')}</div></div>`}
        <div class="bu-vorschau" data-vorschau></div>
      </section>
      ${knopfleiste(false, true)}`;
    inhalt.querySelectorAll('[data-wdh]').forEach(b => b.onclick = () => {
      if (z.wiederholung === 'einmalig' && b.dataset.wdh !== 'einmalig') { z.von = z.datum; z.wochentage = [Math.min(tagIndex(z.datum), opt.tageProWoche - 1)]; if (z.bis <= z.von) z.bis = plusTage(z.von, 56); }
      if (b.dataset.wdh === 'einmalig' && z.wiederholung !== 'einmalig') z.datum = z.von;
      z.wiederholung = b.dataset.wdh; zustand.suche = null; terminSchritt();
    });
    inhalt.querySelectorAll('[data-modus]').forEach(b => b.onclick = () => { z.modus = b.dataset.modus; zustand.suche = null; terminSchritt(); });
    inhalt.querySelectorAll('[data-tag]').forEach(b => b.onclick = () => {
      const t = Number(b.dataset.tag);
      z.wochentage = z.wochentage.includes(t) ? z.wochentage.filter(x => x !== t) : [...z.wochentage, t];
      b.classList.toggle('an'); b.setAttribute('aria-pressed', String(z.wochentage.includes(t)));
      zustand.suche = null; vorschau();
    });
    inhalt.querySelectorAll('[data-z]').forEach(f => f.addEventListener('change', () => {
      const k = f.dataset.z;
      z[k] = k.startsWith('stunde') ? Number(f.value) : f.value;
      if (k === 'stundeVon' && z.stundeBis < z.stundeVon) { z.stundeBis = z.stundeVon; const b = inhalt.querySelector('[data-z=stundeBis]'); if (b) b.value = String(z.stundeBis); }
      if (k === 'von' && z.bis < z.von) { z.bis = plusTage(z.von, 56); inhalt.querySelector('[data-z=bis]').value = z.bis; }
      zustand.suche = null; vorschau();
    }));
    knoepfeVerbinden(null, async () => {
      const t = await vorschau(true);
      if (!t?.termine.length) throw new Error('Im gewählten Zeitbereich gibt es keinen Unterrichtstag.');
      zustand.termine = t.termine.map(x => ({ ...x }));
      await gehe(1);
    });
    vorschau();
  }

  let vorschauNr = 0;
  async function vorschau(pflicht = false) {
    const ziel = inhalt.querySelector('[data-vorschau]');
    const nr = ++vorschauNr;
    try {
      const t = await senden('/api/app/buchen/termine', zeitDaten(z));
      if (nr !== vorschauNr || !ziel) return t;
      const n = t.termine.length;
      ziel.innerHTML = `<div class="bu-vorschau-kopf"><i class="bi bi-calendar-check"></i><b>${n} ${n === 1 ? 'Termin' : 'Termine'}</b>
          ${t.beginn ? `<span class="nl-unter">${e(t.beginn)}–${e(t.ende)} Uhr</span>` : ''}
          ${t.uebersprungen.length ? `<span class="nl-chip grau" title="${e(t.uebersprungen.map(tagKurz).join(', '))}">${t.uebersprungen.length} unterrichtsfreie Tage ausgelassen</span>` : ''}</div>
        <div class="nl-chips">${t.termine.slice(0, 24).map(x => `<span class="nl-tag">${e(tagKurz(x.datum))}</span>`).join('')}${n > 24 ? `<span class="nl-tag">+${n - 24}</span>` : ''}</div>`;
      return t;
    } catch (err) {
      if (ziel && nr === vorschauNr) ziel.innerHTML = `<div class="bu-fehler"><i class="bi bi-exclamation-circle"></i>${e(err.message)}</div>`;
      if (pflicht) throw err;
      return null;
    }
  }

  // ---------------------------------------------------------------- Schritt: Kriterien

  function kriterienSchritt() {
    const k = zustand.kriterien;
    const serie = z.wiederholung !== 'einmalig';
    const artenWahl = opt.ressourcenarten.length ? feld('Ressourcenarten', `<select multiple class="form-select" data-k="ressourcenarten">${opt.ressourcenarten.map(a => `<option value="${a.id}" ${k.ressourcenarten.includes(a.id) ? 'selected' : ''}>${e(a.name)}</option>`).join('')}</select>`, 'breit')
      : `<p class="nl-unter bu-breit">Es sind noch keine Ressourcenarten angelegt.${opt.admin ? ' <a href="#/buchen/verwaltung">Ressourcen verwalten</a>' : ''}</p>`;
    const auswahl = (titel, schluessel, liste) => liste.length ? feld(titel, `<select class="form-select" data-k="${schluessel}">${optionen(liste, k[schluessel], 'Alle')}</select>`) : '';
    inhalt.innerHTML = `
      <div class="bu-zusammenfassung"><i class="bi bi-calendar-event"></i>${e(zeitText(z))}</div>
      <section class="bu-karte">
        <header><i class="bi bi-funnel"></i><h2>Auswahlkriterien</h2><span class="nl-unter ms-auto">optional</span></header>
        <div class="bu-raster">
          ${artenWahl}
          ${art === 'raum' ? `${auswahl('Raumart', 'raumart', opt.raumarten)}
            ${feld('Kapazität (mind. Plätze)', `<input type="number" class="form-control" min="0" max="999" data-k="kapazitaetMin" value="${e(k.kapazitaetMin)}" placeholder="beliebig">`)}
            ${auswahl('Abteilung', 'abteilung', opt.abteilungen)}${auswahl('Gebäude', 'gebaeude', opt.gebaeude)}${auswahl('Raumgruppe', 'raumgruppe', opt.raumgruppen)}` : ''}
          ${serie ? feld('Maximale Belegung (%)', `<input type="number" class="form-control" min="0" max="100" step="5" data-k="maxBelegung" value="${k.maxBelegung}">
            <small class="nl-unter">${art === 'raum' ? 'Auch Räume zeigen, die an höchstens so vielen Terminen belegt sind – für diese Termine wählen Sie dann einen anderen Raum.' : 'Auch Ressourcen zeigen, die an höchstens so vielen Terminen ausgebucht sind (diese Termine entfallen).'}</small>`) : ''}
        </div>
      </section>
      ${knopfleiste(true, true, 'Suchen', 'search')}`;
    inhalt.querySelectorAll('[data-k]').forEach(f => f.addEventListener('change', () => {
      const s = f.dataset.k;
      k[s] = s === 'ressourcenarten' ? [...f.selectedOptions].map(o => Number(o.value)) : s === 'maxBelegung' ? Math.max(0, Math.min(100, Number(f.value) || 0)) : f.value;
      zustand.suche = null;
    }));
    knoepfeVerbinden(null, async () => { await suchen(); await gehe(2); });
  }

  const kriterienDaten = () => {
    const k = zustand.kriterien;
    return { ressourcenarten: k.ressourcenarten, raumart: k.raumart || null, kapazitaetMin: Number(k.kapazitaetMin) || null, abteilung: k.abteilung || null,
      gebaeude: k.gebaeude || null, raumgruppe: k.raumgruppe || null, maxBelegung: z.wiederholung === 'einmalig' ? 0 : k.maxBelegung };
  };

  async function suchen() {
    const ressourcen = art === 'ressource';
    // Aktivität und zusätzlicher Unterricht: alle buchbaren Räume mit Verfügbarkeit (Raum ist optional)
    const kriterien = art === 'aktivitaet' || art === 'unterricht' ? { maxBelegung: 100 } : kriterienDaten();
    zustand.suche = await senden(`/api/app/buchen/suche/${ressourcen ? 'ressourcen' : 'raeume'}`, { zeit: zeitDaten(z), kriterien });
    zustand.termine = (zustand.suche.termine ?? []).map(t => t.termin ?? t);
    if (ressourcen) {
      for (const id of [...zustand.ressourcen.keys()]) if (!zustand.suche.ressourcen.some(r => r.id === id)) zustand.ressourcen.delete(id);
    } else if (zustand.raum && !zustand.suche.raeume.some(r => r.kurz.toLowerCase() === zustand.raum.toLowerCase())) zustand.raum = art === 'raum' ? null : zustand.raum;
    zustand.ersatz = {};
  }

  function kriterienChips() {
    const k = zustand.kriterien;
    const chips = [];
    if (art !== 'aktivitaet' && art !== 'unterricht') {
      k.ressourcenarten.forEach(id => chips.push(opt.ressourcenarten.find(a => a.id === id)?.name));
      if (k.raumart) chips.push(k.raumart);
      if (Number(k.kapazitaetMin)) chips.push(`≥ ${k.kapazitaetMin} Plätze`);
      [k.abteilung, k.gebaeude, k.raumgruppe].filter(Boolean).forEach(x => chips.push(x));
      if (z.wiederholung !== 'einmalig' && k.maxBelegung) chips.push(`max. ${k.maxBelegung} % belegt`);
    }
    return `<div class="bu-zusammenfassung"><i class="bi bi-calendar-event"></i><span>${e(zeitText(z))}</span>
      <span class="nl-unter">${zustand.termine?.length ?? 0} ${zustand.termine?.length === 1 ? 'Termin' : 'Termine'}</span>
      ${chips.filter(Boolean).map(c => `<span class="nl-chip grau">${e(c)}</span>`).join('')}</div>`;
  }

  // ---------------------------------------------------------------- Schritt: Freie Räume / Raum (optional)

  async function raumSchritt(optional) {
    if (!zustand.suche) { inhalt.innerHTML = '<div class="u-laedt"></div>'; await suchen(); }
    const s = zustand.suche;
    const n = s.termine.length;
    const gewaehlt = zustand.raum?.toLowerCase();
    const zeilen = s.raeume.map(r => {
      const frei = n - r.belegt.length;
      const balken = `<span class="bu-balken" title="${frei} von ${n} Terminen frei"><i style="width:${Math.round(frei * 100 / n)}%"></i></span>`;
      return `<tr class="${r.kurz.toLowerCase() === gewaehlt ? 'nl-gewaehlt' : ''}" data-raum="${e(r.kurz)}" data-name="${e(`${r.kurz} ${r.name} ${r.raumart} ${r.gebaeude} ${r.abteilung} ${r.raumgruppe} ${r.ressourcen.join(' ')}`.toLowerCase())}">
        <td class="nl-wahl"><input type="radio" name="bu-raum" class="nl-rund" value="${e(r.kurz)}" ${r.kurz.toLowerCase() === gewaehlt ? 'checked' : ''} aria-label="Raum ${e(r.kurz)} wählen"></td>
        <td><div class="nl-name">${e(r.kurz)}</div><div class="nl-unter">${e(r.name !== r.kurz ? r.name : '')}</div></td>
        <td>${r.raumart ? `<span class="nl-tag">${e(r.raumart)}</span>` : ''}</td>
        <td class="text-end">${r.kapazitaet || '–'}</td>
        <td class="nl-unter">${e([r.gebaeude, r.abteilung, r.raumgruppe].filter(Boolean).join(' · '))}</td>
        <td><div class="nl-chips">${r.ressourcen.map(x => `<span class="nl-chip grau">${e(x)}</span>`).join('')}</div></td>
        <td class="text-nowrap">${r.belegt.length ? `${balken}<span class="nl-unter">${frei}/${n} frei · ${r.prozent} % belegt</span>` : `<span class="nl-chip gruen"><i class="bi bi-check-lg"></i>${n === 1 ? 'frei' : `an allen ${n} Terminen frei`}</span>`}</td></tr>`;
    }).join('');
    inhalt.innerHTML = `
      ${unterricht ? unterrichtKopf(unterricht) : ''}
      ${kriterienChips()}
      ${s.uebersprungen.length ? `<p class="nl-hinweis"><i class="bi bi-info-circle"></i>${s.uebersprungen.length} unterrichtsfreie Tage im Zeitraum werden ausgelassen.</p>` : ''}
      <div class="nl-leiste">
        <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Raum suchen" aria-label="Raum suchen"></label>
        <span class="nl-zahl"><b>${s.raeume.length}</b> ${s.raeume.length === 1 ? 'Raum' : 'Räume'} gefunden</span>
      </div>
      <div class="nl-tabelle bu-tabelle">
        ${optional ? `<div class="nl-kopf"><label class="bu-ohne"><input type="radio" name="bu-raum" class="nl-rund" value="" ${!gewaehlt ? 'checked' : ''}> Ohne Raum buchen</label></div>` : ''}
        ${s.raeume.length ? `<table><thead><tr><th class="nl-wahl"></th><th>Raum</th><th>Raumart</th><th class="text-end">Plätze</th><th>Gebäude · Abteilung · Gruppe</th><th>Ressourcen</th><th>Verfügbarkeit</th></tr></thead>
          <tbody>${zeilen}</tbody></table>` : `<div class="p-3">${leer(art === 'raum' ? 'Kein passender Raum ist frei. Ändern Sie Termin oder Kriterien – bei Serien hilft die maximale Belegung.' : 'Kein buchbarer Raum ist frei.', 'door-closed')}</div>`}
      </div>
      <div data-ersatz></div>
      ${knopfleiste(true, true)}`;
    const ersatzZeichnen = () => {
      const ziel = inhalt.querySelector('[data-ersatz]');
      const r = s.raeume.find(x => x.kurz.toLowerCase() === zustand.raum?.toLowerCase());
      if (!r || !r.belegt.length) { ziel.innerHTML = ''; return; }
      ziel.innerHTML = `<section class="bu-karte mt-3"><header><i class="bi bi-arrow-left-right"></i><h2>Raum ${e(r.kurz)} ist an ${r.belegt.length} ${r.belegt.length === 1 ? 'Termin' : 'Terminen'} belegt</h2></header>
        <p class="nl-unter mb-2">Wählen Sie für diese Termine einen anderen freien Raum${optional ? ', buchen Sie ohne Raum' : ''} oder lassen Sie den Termin aus.</p>
        <div class="nl-tabelle"><table><thead><tr><th>Termin</th><th>Zeit</th><th>Anderer Raum</th></tr></thead><tbody>
        ${r.belegt.map(i => { const t = s.termine[i]; const frei = t.freieRaeume; return `<tr><td class="text-nowrap"><b>${e(tagKurz(t.termin.datum))}</b></td><td class="nl-unter">${stdText(t.termin.stundeVon, t.termin.stundeBis)}</td>
          <td><select class="form-select" data-ersatz-i="${i}">${optionen(frei.map(x => [x, x, opt.raeume.find(y => y.kurz === x)?.name ?? '']), zustand.ersatz[i] ?? '', optional ? 'Ohne Raum' : 'Termin auslassen')}</select></td></tr>`; }).join('')}
        </tbody></table></div></section>`;
      ziel.querySelectorAll('[data-ersatz-i]').forEach(sel => sel.onchange = () => { zustand.ersatz[sel.dataset.ersatzI] = sel.value; });
    };
    inhalt.querySelectorAll('input[name=bu-raum]').forEach(x => x.onchange = () => {
      zustand.raum = x.value || null; zustand.ersatz = {};
      inhalt.querySelectorAll('tbody tr[data-raum]').forEach(tr => tr.classList.toggle('nl-gewaehlt', tr.dataset.raum === x.value));
      ersatzZeichnen();
    });
    inhalt.querySelectorAll('tbody tr[data-raum]').forEach(tr => tr.addEventListener('click', ev => {
      if (ev.target.closest('input')) return;
      const r = tr.querySelector('input'); r.checked = true; r.dispatchEvent(new Event('change'));
    }));
    inhalt.querySelector('[data-suche]').oninput = ev => {
      const t = ev.target.value.trim().toLowerCase();
      inhalt.querySelectorAll('tbody tr[data-raum]').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
    };
    ersatzZeichnen();
    knoepfeVerbinden(null, async () => {
      if (!optional && !zustand.raum) throw new Error('Bitte einen Raum wählen.');
      const termine = gewaehlteTermine();
      if (!termine.length) throw new Error('Es bleibt kein Termin übrig – bitte für belegte Termine einen anderen Raum wählen.');
      await gehe(zustand.schritt + 1);
    });
  }

  /** Termine mit Raum je Termin (Ersatzraum für belegte Termine; ohne Ersatz: auslassen bzw. ohne Raum). */
  function gewaehlteTermine() {
    const s = zustand.suche;
    if (art === 'ressource') {
      const wahl = [...zustand.ressourcen.entries()];
      return s.termine.filter((t, i) => wahl.every(([id, menge]) => (s.ressourcen.find(r => r.id === id)?.verfuegbar[i] ?? 0) >= menge))
        .map(t => ({ datum: t.datum, stundeVon: t.stundeVon, stundeBis: t.stundeBis, raum: null }));
    }
    if (!s) return (zustand.termine ?? []).map(t => ({ datum: t.datum, stundeVon: t.stundeVon, stundeBis: t.stundeBis, raum: null }));
    const optional = art !== 'raum';
    const r = s.raeume.find(x => x.kurz.toLowerCase() === zustand.raum?.toLowerCase());
    return s.termine.map((t, i) => {
      const belegt = r?.belegt.includes(i);
      const raum = !r ? null : belegt ? (zustand.ersatz[i] || null) : r.kurz;
      return { datum: t.termin.datum, stundeVon: t.termin.stundeVon, stundeBis: t.termin.stundeBis, raum };
    }).filter(t => optional || t.raum);
  }

  // ---------------------------------------------------------------- Schritt: Freie Ressourcen

  async function ressourcenSchritt() {
    if (!zustand.suche) { inhalt.innerHTML = '<div class="u-laedt"></div>'; await suchen(); }
    const s = zustand.suche;
    const n = s.termine.length;
    inhalt.innerHTML = `
      ${kriterienChips()}
      <div class="nl-tabelle bu-tabelle">${s.ressourcen.length ? `<table><thead><tr><th class="nl-wahl"></th><th>Ressource</th><th>Art</th><th class="text-end">Anzahl</th><th>Menge</th><th>Verfügbarkeit</th></tr></thead><tbody>
        ${s.ressourcen.map(r => { const an = zustand.ressourcen.has(r.id); const maxMenge = Math.max(1, r.verfuegbar.reduce((m, x) => Math.max(m, x), 0)); return `<tr class="${an ? 'nl-gewaehlt' : ''}" data-res="${r.id}">
          <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${r.id}" ${an ? 'checked' : ''} aria-label="${e(r.name)} wählen"></td>
          <td><div class="nl-name">${e(r.name)}</div>${r.bemerkung ? `<div class="nl-unter">${e(r.bemerkung)}</div>` : ''}</td>
          <td>${r.art ? `<span class="nl-tag">${e(r.art)}</span>` : ''}</td><td class="text-end">${r.anzahl}</td>
          <td><input type="number" class="form-control bu-menge" min="1" max="${maxMenge}" value="${zustand.ressourcen.get(r.id) ?? 1}" data-menge="${r.id}" ${r.anzahl > 1 ? '' : 'disabled'} aria-label="Menge"></td>
          <td class="text-nowrap">${r.belegt.length ? `<span class="nl-unter">an ${n - r.belegt.length} von ${n} Terminen verfügbar</span>` : `<span class="nl-chip gruen"><i class="bi bi-check-lg"></i>${r.frei} verfügbar</span>`}</td></tr>`; }).join('')}
        </tbody></table>` : `<div class="p-3">${leer(opt.ressourcen.some(r => !r.raum) ? 'Keine passende Ressource ist frei.' : 'Es sind noch keine mobilen Ressourcen angelegt.', 'box-seam')}</div>`}</div>
      <div data-auslassen></div>
      ${knopfleiste(true, true)}`;
    const auslassen = () => {
      const ziel = inhalt.querySelector('[data-auslassen]');
      const bleiben = gewaehlteTermine().length;
      ziel.innerHTML = zustand.ressourcen.size && bleiben < n ? `<p class="nl-hinweis mt-3"><i class="bi bi-info-circle"></i>${n - bleiben} ${n - bleiben === 1 ? 'Termin wird' : 'Termine werden'} ausgelassen, weil die gewählte Menge dort nicht verfügbar ist.</p>` : '';
    };
    inhalt.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => {
      const id = Number(c.dataset.wahl);
      if (c.checked) zustand.ressourcen.set(id, Number(inhalt.querySelector(`[data-menge="${id}"]`).value) || 1); else zustand.ressourcen.delete(id);
      c.closest('tr').classList.toggle('nl-gewaehlt', c.checked);
      auslassen();
    });
    inhalt.querySelectorAll('[data-menge]').forEach(f => f.onchange = () => {
      const id = Number(f.dataset.menge);
      f.value = String(Math.max(1, Math.min(Number(f.max), Number(f.value) || 1)));
      if (zustand.ressourcen.has(id)) zustand.ressourcen.set(id, Number(f.value));
      auslassen();
    });
    auslassen();
    knoepfeVerbinden(null, async () => {
      if (!zustand.ressourcen.size) throw new Error('Bitte mindestens eine Ressource wählen.');
      if (!gewaehlteTermine().length) throw new Error('Die Auswahl ist an keinem Termin verfügbar.');
      await gehe(zustand.schritt + 1);
    });
  }

  // ---------------------------------------------------------------- Schritt: Details

  function detailsSchritt() {
    const d = zustand.details;
    const aktivitaet = art === 'aktivitaet';
    inhalt.innerHTML = `
      ${kriterienChips()}
      <section class="bu-karte">
        <header><i class="bi bi-pencil-square"></i><h2>${aktivitaet ? 'Aktivität' : 'Angaben zur Buchung'}</h2><span class="nl-unter ms-auto">alle Angaben optional</span></header>
        <div class="bu-raster">
          ${feld('Titel / Anlass', `<input class="form-control" maxlength="200" data-d="titel" value="${e(d.titel)}" placeholder="${aktivitaet ? 'z. B. Förderunterricht, Exkursion' : 'z. B. Projektarbeit'}">`, 'breit')}
          ${feld('Klasse(n)', `<select multiple class="form-select" data-d="klassen">${el.klassen.map(k => `<option value="${e(k.kurz)}" ${d.klassen.includes(k.kurz) ? 'selected' : ''} data-zweit="${e(k.name !== k.kurz ? k.name : '')}">${e(k.kurz)}</option>`).join('')}</select>`)}
          ${feld('Lehrkraft', `<select class="form-select" data-d="lehrer">${optionen(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), d.lehrer, 'keine')}</select>`)}
          ${feld('Fach', `<select class="form-select" data-d="fach">${optionen(el.faecher.map(f => [f.kurz, f.kurz, f.name]), d.fach, 'kein Fach')}</select>`)}
          ${feld('Bemerkung (für alle sichtbar)', `<textarea class="form-control" rows="2" maxlength="1000" data-d="text">${e(d.text)}</textarea>`, 'breit')}
          ${aktivitaet ? `<label class="bu-schalter breit"><input type="checkbox" class="form-check-input" data-d="benachrichtigen" ${d.benachrichtigen ? 'checked' : ''}>
            <span>Lehrkraft der Aktivität benachrichtigen <small class="nl-unter">(Mitteilung im Posteingang)</small></span></label>` : ''}
        </div>
        <div data-hinweise></div>
      </section>
      ${knopfleiste(true, true)}`;
    const hinweise = async () => {
      const ziel = inhalt.querySelector('[data-hinweise]');
      try {
        const h = await senden('/api/app/buchen/hinweise', { termine: gewaehlteTermine(), lehrer: d.lehrer || null, klassen: d.klassen });
        zustand.hinweise = h;
        ziel.innerHTML = h.length ? `<div class="bu-warnung"><i class="bi bi-exclamation-triangle"></i><div><b>Hinweis – zur gleichen Zeit schon eingeplant</b><ul>${h.slice(0, 8).map(x => `<li>${e(x)}</li>`).join('')}${h.length > 8 ? `<li>… und ${h.length - 8} weitere</li>` : ''}</ul></div></div>` : '';
      } catch { ziel.innerHTML = ''; }
    };
    inhalt.querySelectorAll('[data-d]').forEach(f => f.addEventListener(f.tagName === 'TEXTAREA' || f.type === 'text' || f.tagName === 'INPUT' && f.type !== 'checkbox' ? 'input' : 'change', () => {
      const k = f.dataset.d;
      d[k] = k === 'klassen' ? [...f.selectedOptions].map(o => o.value) : f.type === 'checkbox' ? f.checked : f.value;
      if (k === 'klassen' || k === 'lehrer') hinweise();
    }));
    hinweise();
    knoepfeVerbinden(null, () => gehe(zustand.schritt + 1));
  }

  // ---------------------------------------------------------------- Schritt: Speichern

  function speichernSchritt() {
    const termine = gewaehlteTermine();
    const d = zustand.details;
    const u = unterricht;
    const raeume = [...new Set(termine.map(t => t.raum).filter(Boolean))];
    const res = [...zustand.ressourcen.entries()].map(([id, m]) => { const r = zustand.suche?.ressourcen?.find(x => x.id === id); return `${m > 1 ? `${m}× ` : ''}${r?.name ?? id}`; });
    const zeile = (t, w) => w ? `<div class="bu-zf"><span>${e(t)}</span><b>${w}</b></div>` : '';
    inhalt.innerHTML = `
      <section class="bu-karte">
        <header><i class="bi bi-clipboard-check"></i><h2>Zusammenfassung</h2><span class="nl-chip ${ART_CHIP[art]} ms-auto">${ART_NAME[art]}</span></header>
        <div class="bu-zf-raster">
          ${zeile('Titel', e(d.titel || (u ? u.gruppe || 'Zusätzlicher Unterricht' : ART_NAME[art])))}
          ${zeile('Zeitbereich', e(zeitText(z)))}
          ${zeile(termine.length === 1 ? 'Termin' : `${termine.length} Termine${termine.length > 1 ? ' (Serie)' : ''}`, `<div class="nl-chips">${termine.slice(0, 16).map(t => `<span class="nl-tag" title="${e(stdText(t.stundeVon, t.stundeBis))}${t.raum ? ` · ${e(t.raum)}` : ''}">${e(tagKurz(t.datum))}${raeume.length > 1 && t.raum ? ` · ${e(t.raum)}` : ''}</span>`).join('')}${termine.length > 16 ? `<span class="nl-tag">+${termine.length - 16}</span>` : ''}</div>`)}
          ${zeile(raeume.length > 1 ? 'Räume' : 'Raum', e(raeume.join(', ')) || (art === 'ressource' ? '' : '<span class="nl-unter">ohne Raum</span>'))}
          ${zeile('Ressourcen', e(res.join(', ')))}
          ${zeile('Klasse(n)', e((u ? u.klassen : d.klassen).join(', ')))}
          ${zeile('Lehrkraft', e(u ? u.lehrer : d.lehrer))}
          ${zeile('Fach', e(u ? u.fach : d.fach))}
          ${zeile('Bemerkung', e(d.text))}
          ${art === 'aktivitaet' && d.benachrichtigen && d.lehrer ? zeile('Benachrichtigung', `Mitteilung an ${e(d.lehrer)}`) : ''}
        </div>
        ${zustand.hinweise.length && art !== 'unterricht' ? `<div class="bu-warnung"><i class="bi bi-exclamation-triangle"></i><div>${zustand.hinweise.map(e).join('<br>')}<br><small>Die Buchung ist trotzdem möglich.</small></div></div>` : ''}
        <p class="nl-unter mt-2 mb-0"><i class="bi bi-info-circle me-1"></i>Die Buchung erscheint sofort in „Meine Buchungen“ und in den Stundenplänen${art === 'ressource' ? '' : ' und wird an UntPlan übermittelt'}.</p>
      </section>
      ${knopfleiste(true, true, 'Speichern', 'check-lg')}`;
    knoepfeVerbinden(null, async () => {
      const daten = {
        art, termine, ressourcen: [...zustand.ressourcen.entries()].map(([id, menge]) => ({ id, menge })),
        klassen: u ? [] : d.klassen, lehrer: u ? null : (d.lehrer || ''), fach: u ? null : d.fach, titel: d.titel, text: d.text,
        benachrichtigen: art === 'aktivitaet' && d.benachrichtigen, unterrichtId: u?.id ?? null,
        beginn: z.modus === 'uhrzeit' ? z.beginn : null, ende: z.modus === 'uhrzeit' ? z.ende : null,
      };
      zustand.gespeichert = await senden('/api/app/buchen', daten);
      toast(zustand.gespeichert.buchungen.length === 1 ? 'Gebucht.' : `${zustand.gespeichert.buchungen.length} Termine gebucht.`);
      await zeichnen();
    });
  }

  function erfolg() {
    const g = zustand.gespeichert;
    const b = g.buchungen;
    const raeume = [...new Set(b.map(x => x.raum).filter(Boolean))];
    inhalt.innerHTML = `<section class="bu-karte bu-erfolg">
      <div class="bu-erfolg-symbol"><i class="bi bi-check-lg"></i></div>
      <h2>${b.length === 1 ? 'Die Buchung ist gespeichert.' : `Serie mit ${b.length} Terminen gespeichert.`}</h2>
      <p class="nl-unter">${e(b[0].titel)} · ${e(tagKurz(b[0].datum))}${b.length > 1 ? ` – ${e(tagKurz(b[b.length - 1].datum))}` : ''} · ${stdText(b[0].stundeVon, b[0].stundeBis)}${raeume.length ? ` · ${raeume.length > 1 ? 'Räume' : 'Raum'} ${e(raeume.join(', '))}` : ''}</p>
      ${g.hinweise?.length ? alarm('warning', g.hinweise.join(' ')) : ''}
      <div class="bu-knopfleiste mitte"><a class="nl-knopf bu-gross" href="#/buchen/meine"><i class="bi bi-list-ul"></i>Meine Buchungen</a>
        ${b[0].raum ? `<a class="nl-knopf bu-gross" href="#/plan/raum/${encodeURIComponent(b[0].raum)}/${b[0].datum}"><i class="bi bi-calendar3-week"></i>Raumplan</a>` : ''}
        <button type="button" class="nl-hinzu" data-neu><i class="bi bi-plus-lg"></i>Weitere Buchung</button></div></section>`;
    inhalt.querySelector('[data-neu]').onclick = () => kontext.navigieren(location.hash.split('/').slice(0, art === 'unterricht' ? 4 : 3).join('/'), true);
  }

  await zeichnen();
  // Vorbelegung aus der Raumübersicht: gleich die freien Räume mit dem gewählten Raum zeigen
  if (vorbelegt && zustand.raum) {
    try {
      const t = await vorschau(true);
      if (t?.termine.length) { zustand.termine = t.termine; await suchen(); await gehe(2); }
    } catch (err) { kontext.fehler(err); }
  }
}

function unterrichtKopf(u) {
  return `<div class="bu-zusammenfassung"><i class="bi bi-easel"></i><b>${e(u.fach)}</b><span>${e(u.klassen.join(', ') || 'ohne Klasse')}</span><span class="nl-tag">${e(u.lehrer)}</span>
    ${u.gruppe ? `<span class="nl-unter">${e(u.gruppe)}</span>` : ''}<span class="nl-chip grau ms-auto">${u.gebuchteStunden} Stunden gebucht</span></div>`;
}

// ------------------------------------------------------------------ Meine Buchungen

async function meineBuchungen(seite, kontext, [vonParam, bisParam, alleParam]) {
  const ich = kontext.ich;
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -7);
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(heuteIso(), 180);
  const alle = ich.admin && alleParam === 'alle';
  const ziel = (v = von, b = bis, a = alle) => `#/buchen/meine/${v}/${b}${a ? '/alle' : ''}`;
  kontext.titel('Meine Buchungen', '', '');
  const liste = await holen(`/api/app/buchen/liste?von=${von}&bis=${bis}${alle ? '&alle=true' : ''}`);
  const heute = heuteIso();
  const filter = { suche: '', art: '', status: 'aktiv' };
  const serien = liste.reduce((m, b) => (b.serie ? m.set(b.serie, (m.get(b.serie) ?? 0) + 1) : m), new Map());
  kontext.titel(alle ? 'Alle Buchungen' : 'Meine Buchungen', '', `${liste.filter(b => b.status === 'aktiv').length}`);
  const pille = (html, aktiv = false) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}">${html}</label>`;
  seite.innerHTML = `
    <div class="nl-leiste">
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Buchungen durchsuchen"></label>
      ${pille(`<span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}">`)}
      ${pille(`<span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${bis}">`)}
      ${pille(`<select data-art aria-label="Art"><option value="">Art</option>${Object.entries(ART_NAME).map(([k, t]) => `<option value="${k}">${t}</option>`).join('')}</select>`)}
      ${pille(`<select data-status aria-label="Status"><option value="aktiv" selected>Aktiv</option><option value="storniert">Storniert</option><option value="">Alle</option></select>`)}
      ${ich.admin ? `<label class="bu-schalter"><input type="checkbox" class="form-check-input" data-alle ${alle ? 'checked' : ''}><span>Alle Buchungen</span></label>` : ''}
      <span class="ms-auto d-flex gap-2">
        <button type="button" class="nl-knopf" data-csv title="Als CSV herunterladen"><i class="bi bi-filetype-csv"></i>Export</button>
        <a class="nl-hinzu" href="#/buchen/raum"><i class="bi bi-plus-lg"></i>Neue Buchung</a>
      </span>
    </div>
    <div class="nl-zahlen" data-zahlen></div>
    <div class="nl-tabelle bu-tabelle" data-tabelle></div>`;
  const sichtbar = () => liste.filter(b => (!filter.art || b.art === filter.art) && (!filter.status || b.status === filter.status)
    && (!filter.suche || [b.titel, b.text, b.raum, b.klassen.join(' '), b.lehrer, b.fach, b.absender, b.artName, b.ressourcen.map(r => r.name).join(' '), datumDe(b.datum)].join(' ').toLowerCase().includes(filter.suche)));
  const zeichnen = () => {
    const l = sichtbar();
    const aktiv = liste.filter(b => b.status === 'aktiv');
    seite.querySelector('[data-zahlen]').innerHTML = `<span class="nl-zahl"><b>${aktiv.length}</b> aktiv</span>
      <span class="nl-zahl gelb"><b>${aktiv.filter(b => b.datum >= heute).length}</b> kommend</span>
      <span class="nl-zahl lila"><b>${new Set(aktiv.filter(b => b.serie).map(b => b.serie)).size}</b> Serien</span>
      <span class="nl-zahl rot"><b>${liste.filter(b => b.status === 'storniert').length}</b> storniert</span>`;
    seite.querySelector('[data-tabelle]').innerHTML = l.length ? `<table><thead><tr><th>Datum</th><th>Zeit</th><th>Art</th><th>Raum / Ressourcen</th><th>Klassen</th><th>Lehrkraft</th><th>Titel</th><th>Status</th><th></th></tr></thead><tbody>
      ${l.map(b => { const darf = (ich.admin || b.benutzerId === ich.id) && b.status === 'aktiv' && (ich.admin || b.datum >= heute); return `<tr class="${b.status === 'storniert' ? 'bu-storniert' : ''} ${b.datum < heute ? 'bu-vergangen' : ''}">
        <td class="text-nowrap"><div class="nl-name">${e(tagKurz(b.datum))}</div>${b.serie ? `<div class="nl-unter" title="Serie ${e(b.serie)}"><i class="bi bi-arrow-repeat"></i> Serie · ${serien.get(b.serie)} Termine</div>` : ''}</td>
        <td class="text-nowrap"><div>${stdText(b.stundeVon, b.stundeBis)}</div><div class="nl-unter">${e(b.beginn)}–${e(b.ende)}</div></td>
        <td><span class="nl-chip ${ART_CHIP[b.art] ?? 'grau'}">${e(b.artName)}</span></td>
        <td>${b.raum ? `<a class="nl-name" href="#/plan/raum/${encodeURIComponent(b.raum)}/${b.datum}">${e(b.raum)}</a>` : ''}<div class="nl-chips">${b.ressourcen.map(r => `<span class="nl-chip grau">${r.menge > 1 ? `${r.menge}× ` : ''}${e(r.name)}</span>`).join('')}</div></td>
        <td>${e(b.klassen.join(', '))}</td>
        <td>${b.lehrer ? `<span class="nl-tag">${e(b.lehrer)}</span>` : ''}${b.fach ? ` <span class="nl-unter">${e(b.fach)}</span>` : ''}</td>
        <td class="nl-text"><div>${e(b.titel)}</div>${b.text ? `<div class="nl-unter">${e(b.text)}</div>` : ''}${alle || b.benutzerId !== ich.id ? `<div class="nl-unter">gebucht von ${e(b.absender)}</div>` : ''}</td>
        <td>${b.status === 'storniert' ? '<span class="nl-chip rot">storniert</span>' : b.uebertragen ? '<span class="nl-chip gruen" title="von UntPlan übernommen">übernommen</span>' : '<span class="nl-chip grau">aktiv</span>'}</td>
        <td class="text-end text-nowrap">${darf ? `<button type="button" class="nl-icon" data-storno="${b.id}" title="Termin stornieren" aria-label="Termin stornieren"><i class="bi bi-x-circle"></i></button>
          ${b.serie ? `<button type="button" class="nl-icon rot" data-serie="${b.id}" title="Ganze Serie stornieren (kommende Termine)" aria-label="Serie stornieren"><i class="bi bi-trash"></i></button>` : ''}` : ''}</td></tr>`; }).join('')}
      </tbody></table>` : `<div class="p-3">${leer(liste.length ? 'Keine Buchungen für diesen Filter.' : 'Im gewählten Zeitraum gibt es keine Buchungen.', 'calendar-x')}</div>`;
    seite.querySelectorAll('[data-storno], [data-serie]').forEach(k => k.onclick = async () => {
      const serie = !!k.dataset.serie;
      const b = liste.find(x => String(x.id) === (k.dataset.storno ?? k.dataset.serie));
      if (!await bestaetigen(serie ? 'Serie stornieren' : 'Termin stornieren', serie ? `Alle kommenden Termine der Serie „${b.titel}“ stornieren?` : `Termin am ${tagLang(b.datum)} (${stdText(b.stundeVon, b.stundeBis)}) stornieren?`, 'Stornieren')) return;
      try {
        const r = await api(`/api/app/buchen/${b.id}${serie ? '?serie=true' : ''}`, { methode: 'DELETE' });
        toast(r.storniert === 1 ? 'Termin storniert.' : `${r.storniert} Termine storniert.`);
        kontext.navigieren(ziel(), true);
      } catch (err) { kontext.fehler(err); }
    });
  };
  seite.querySelector('[data-suche]').oninput = ev => { filter.suche = ev.target.value.trim().toLowerCase(); zeichnen(); };
  seite.querySelector('[data-art]').onchange = ev => { filter.art = ev.target.value; ev.target.closest('.nl-pille').classList.toggle('aktiv', !!filter.art); zeichnen(); };
  seite.querySelector('[data-status]').onchange = ev => { filter.status = ev.target.value; zeichnen(); };
  seite.querySelectorAll('[data-von], [data-bis]').forEach(f => f.onchange = () => kontext.navigieren(ziel(seite.querySelector('[data-von]').value || von, seite.querySelector('[data-bis]').value || bis)));
  seite.querySelector('[data-alle]')?.addEventListener('change', ev => kontext.navigieren(ziel(von, bis, ev.target.checked)));
  seite.querySelector('[data-csv]').onclick = () => herunterladen(`buchungen-${von}-${bis}.csv`, csv([
    ['Datum', 'Wochentag', 'Stunde von', 'Stunde bis', 'Beginn', 'Ende', 'Art', 'Raum', 'Ressourcen', 'Klassen', 'Lehrkraft', 'Fach', 'Titel', 'Bemerkung', 'Status', 'Serie', 'Gebucht von', 'Erstellt'],
    ...sichtbar().map(b => [datumDe(b.datum), b.wochentag, b.stundeVon, b.stundeBis, b.beginn, b.ende, b.artName, b.raum, b.ressourcen.map(r => `${r.menge > 1 ? `${r.menge}x ` : ''}${r.name}`).join(', '),
      b.klassen.join(', '), b.lehrer, b.fach, b.titel, b.text, b.status, b.serie, b.absender, b.erstellt])]));
  zeichnen();
}

// ------------------------------------------------------------------ Raumübersicht

const BELEGT_TEXT = { unterricht: 'Unterricht', klausur: 'Prüfung', termin: 'Termin', buchung: 'Buchung', vorgemerkt: 'vorgemerkt', gesperrt: 'gesperrt' };

async function raumuebersicht(seite, kontext, [datumParam, tageParam]) {
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const datum = iso.test(datumParam ?? '') ? datumParam : heuteIso();
  const woche = tageParam === 'woche';
  const start = woche ? montag(datum) : datum;
  const opt = await holen('/api/app/buchen/optionen');
  const k = raumFilter;
  const q = new URLSearchParams({ datum: start, tage: woche ? String(opt.tageProWoche) : '1' });
  for (const [n, w] of Object.entries(k)) if (w) q.set(n, w);
  const ziel = (d = datum, w = woche) => `#/buchen/raeume/${d}${w ? '/woche' : ''}`;
  kontext.titel('Raumübersicht', `<a class="nl-knopf" href="#/buchen/raum"><i class="bi bi-plus-lg"></i>Raum buchen</a>`);
  const pille = (html, aktiv = false) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}">${html}</label>`;
  const sel = (n, titel, liste) => liste.length ? pille(`<select data-f="${n}" aria-label="${titel}">${optionen(liste, k[n], titel)}</select>`, !!k[n]) : '';
  seite.innerHTML = `
    <div class="nl-leiste">
      <div class="bu-datumwahl"><button type="button" class="nl-knopf" data-schieben="-1" aria-label="zurück"><i class="bi bi-chevron-left"></i></button>
        ${pille(`<input type="date" data-datum value="${datum}">`)}<button type="button" class="nl-knopf" data-schieben="1" aria-label="weiter"><i class="bi bi-chevron-right"></i></button>
        <button type="button" class="nl-knopf" data-heute>Heute</button></div>
      <div class="bu-segment klein"><button type="button" class="${woche ? '' : 'an'}" data-ansicht="tag">Tag</button><button type="button" class="${woche ? 'an' : ''}" data-ansicht="woche">Woche</button></div>
      ${sel('raumart', 'Raumart', opt.raumarten)}${sel('gebaeude', 'Gebäude', opt.gebaeude)}${sel('abteilung', 'Abteilung', opt.abteilungen)}${sel('raumgruppe', 'Raumgruppe', opt.raumgruppen)}
      ${opt.ressourcenarten.length ? pille(`<select data-f="ressourcenarten" aria-label="Ressourcenart">${optionen(opt.ressourcenarten.map(a => [String(a.id), a.name]), k.ressourcenarten, 'Ressourcenart')}</select>`, !!k.ressourcenarten) : ''}
      ${pille(`<input type="number" min="0" max="999" placeholder="Plätze" data-f="kapazitaet" value="${e(k.kapazitaet)}" style="width:80px" aria-label="mindestens Plätze">`, !!k.kapazitaet)}
    </div>
    <div class="bu-legende">${Object.entries(BELEGT_TEXT).map(([a, t]) => `<span><i class="bu-zelle ${a}"></i>${t}</span>`).join('')}<span><i class="bu-zelle frei"></i>frei – anklicken zum Buchen</span></div>
    <div data-raster><div class="u-laedt"></div></div>`;
  seite.querySelectorAll('[data-schieben]').forEach(b => b.onclick = () => kontext.navigieren(ziel(plusTage(datum, Number(b.dataset.schieben) * (woche ? 7 : 1)))));
  seite.querySelector('[data-heute]').onclick = () => kontext.navigieren(ziel(heuteIso()));
  seite.querySelector('[data-datum]').onchange = ev => ev.target.value && kontext.navigieren(ziel(ev.target.value));
  seite.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => kontext.navigieren(ziel(datum, b.dataset.ansicht === 'woche')));
  seite.querySelectorAll('[data-f]').forEach(f => f.onchange = () => { raumFilter[f.dataset.f] = f.value; kontext.navigieren(ziel(), true); });
  const u = await holen(`/api/app/buchen/raumuebersicht?${q}`);
  const raster = u.zeitraster.filter(z => z.nr > 0);
  const heute = heuteIso();
  const tage = u.tage;
  const kopf = `<tr><th class="bu-raumkopf" rowspan="${woche ? 2 : 1}">Raum</th>${woche ? tage.map(t => `<th colspan="${raster.length}" class="bu-tagkopf ${t.frei ? 'frei' : ''}">${e(tagKurz(t.datum))}</th>`).join('') + '</tr><tr>' : ''}
    ${tage.map(t => raster.map((z, j) => `<th class="bu-stdkopf ${woche ? 'klein' : ''} ${woche && j === 0 ? 'tagstart' : ''}" title="${e(z.beginn)}–${e(z.ende)}">${z.nr}${woche ? '' : `<small>${e(z.beginn)}</small>`}</th>`).join('')).join('')}</tr>`;
  const zelle = (r, t, z, start) => {
    const s = start && woche ? ' tagstart' : '';
    if (t.frei) return `<td class="bu-zelle tagfrei${s}" title="${e(t.grund || 'unterrichtsfrei')}"></td>`;
    const b = (r.belegung[t.datum] ?? []).find(x => x.stunde === z.nr);
    if (b) return `<td class="bu-zelle ${e(b.art)}${s}" title="${e(BELEGT_TEXT[b.art] ?? b.art)}: ${e(b.text)}">${woche ? '' : `<span>${e(b.text)}</span>`}</td>`;
    if (t.datum < heute) return `<td class="bu-zelle vergangen${s}"></td>`;
    return `<td class="bu-zelle frei${s}"><button type="button" data-buchen="${e(r.kurz)}|${t.datum}|${z.nr}" title="${e(r.kurz)} am ${e(tagKurz(t.datum))}, ${z.nr}. Stunde buchen" aria-label="${e(r.kurz)} ${z.nr}. Stunde buchen"><i class="bi bi-plus"></i></button></td>`;
  };
  seite.querySelector('[data-raster]').innerHTML = u.raeume.length ? `<div class="bu-uebersicht ${woche ? 'woche' : ''}"><table><thead>${kopf}</thead><tbody>
    ${u.raeume.map(r => `<tr><th class="bu-raumname"><b>${e(r.kurz)}</b><small>${e([r.raumart, r.kapazitaet ? `${r.kapazitaet} Pl.` : ''].filter(Boolean).join(' · '))}</small></th>
      ${tage.map(t => raster.map((z, j) => zelle(r, t, z, j === 0)).join('')).join('')}</tr>`).join('')}</tbody></table></div>`
    : leer('Kein buchbarer Raum passt zu den Filtern.', 'door-closed');
  seite.querySelectorAll('[data-buchen]').forEach(b => b.onclick = () => {
    const [raum, d, st] = b.dataset.buchen.split('|');
    kontext.navigieren(`#/buchen/raum/${d}/${st}/${st}/${encodeURIComponent(raum)}`);
  });
}
/** Filter der Raumübersicht (bleibt während der Sitzung erhalten). */
const raumFilter = { raumart: '', gebaeude: '', abteilung: '', raumgruppe: '', ressourcenarten: '', kapazitaet: '' };

// ------------------------------------------------------------------ Ressourcenübersicht (Woche – Ressourcen)

async function ressourcenuebersicht(seite, kontext, [datumParam, artenParam]) {
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const datum = montag(iso.test(datumParam ?? '') ? datumParam : heuteIso());
  const arten = (artenParam && artenParam !== '-' ? artenParam.split(',').map(Number).filter(Boolean) : []);
  const ziel = (d = datum, a = arten) => `#/buchen/ressourcen/${d}/${a.length ? a.join(',') : '-'}`;
  kontext.titel('Ressourcenübersicht', `<a class="nl-knopf" href="#/buchen/ressource"><i class="bi bi-plus-lg"></i>Ressource buchen</a>`);
  const [u, artListe] = await Promise.all([holen(`/api/app/buchen/ressourcenuebersicht?datum=${datum}${arten.length ? `&arten=${arten.join(',')}` : ''}`), holen('/api/app/buchen/ressourcenarten')]);
  const heute = heuteIso();
  seite.innerHTML = `
    <div class="nl-leiste">
      <div class="bu-datumwahl"><button type="button" class="nl-knopf" data-schieben="-7" aria-label="Woche zurück"><i class="bi bi-chevron-left"></i></button>
        <label class="nl-pille"><input type="date" data-datum value="${datum}"></label><button type="button" class="nl-knopf" data-schieben="7" aria-label="Woche weiter"><i class="bi bi-chevron-right"></i></button>
        <button type="button" class="nl-knopf" data-heute>Diese Woche</button></div>
      <span class="nl-unter">Woche ${e(datumDe(u.tage[0].datum))} – ${e(datumDe(u.tage[u.tage.length - 1].datum))}</span>
    </div>
    <div class="bu-uebersicht-rahmen">
      <div data-tabelle></div>
      <aside class="bu-arten bu-karte"><header><i class="bi bi-funnel"></i><h2>Ressourcenarten</h2></header>
        ${artListe.length ? `<div class="bu-artenliste">${artListe.map(a => `<label class="bu-schalter"><input type="checkbox" class="form-check-input" value="${a.id}" ${arten.includes(a.id) ? 'checked' : ''}><span>${e(a.name)}</span></label>`).join('')}</div>
          <div class="d-flex gap-2 mt-2"><button type="button" class="nl-hinzu bu-klein" data-anwenden>Anwenden</button><button type="button" class="nl-knopf" data-alle>Alle Ressourcen</button></div>` : '<p class="nl-unter mb-0">Keine Ressourcenarten angelegt.</p>'}
      </aside>
    </div>`;
  seite.querySelectorAll('[data-schieben]').forEach(b => b.onclick = () => kontext.navigieren(ziel(plusTage(datum, Number(b.dataset.schieben)))));
  seite.querySelector('[data-heute]').onclick = () => kontext.navigieren(ziel(montag(heuteIso())));
  seite.querySelector('[data-datum]').onchange = ev => ev.target.value && kontext.navigieren(ziel(montag(ev.target.value)));
  seite.querySelector('[data-anwenden]')?.addEventListener('click', () => kontext.navigieren(ziel(datum, [...seite.querySelectorAll('.bu-artenliste input:checked')].map(x => Number(x.value)))));
  seite.querySelector('[data-alle]')?.addEventListener('click', () => kontext.navigieren(ziel(datum, [])));
  seite.querySelector('[data-tabelle]').innerHTML = u.ressourcen.length ? `<div class="nl-tabelle bu-tabelle"><table class="bu-woche"><thead><tr><th>Ressource</th>
      ${u.tage.map(t => `<th class="${t.datum === heute ? 'heute' : ''}">${e(tagKurz(t.datum))}</th>`).join('')}</tr></thead><tbody>
    ${u.ressourcen.map(r => `<tr><th><div class="nl-name">${e(r.name)}</div><div class="nl-unter">${e([r.art, r.mobil ? `mobil · ${r.anzahl}×` : `fest in ${r.raum}`].filter(Boolean).join(' · '))}</div></th>
      ${u.tage.map(t => t.frei ? `<td class="bu-tagfrei nl-unter">${e(t.grund || 'frei')}</td>` : `<td>${(r.belegung[t.datum] ?? []).map(x => `<div class="bu-eintrag ${r.mobil ? '' : 'fest'}" title="${e(x.text)}${x.absender ? ` (${e(x.absender)})` : ''}">
          <b>${stdText(x.von, x.bis)}</b>${r.mobil && x.menge > 1 ? ` <span class="nl-tag">${x.menge}×</span>` : ''} <span>${e(x.text)}</span>${x.absender ? ` <small>${e(x.absender)}</small>` : ''}</div>`).join('') || '<span class="bu-frei">frei</span>'}</td>`).join('')}</tr>`).join('')}
    </tbody></table></div>` : `<div class="nl-tabelle p-3">${leer('Keine Ressourcen für diese Auswahl.', 'box-seam')}</div>`;
}

// ------------------------------------------------------------------ Neuer Unterricht

async function eigeneUnterrichte(seite, kontext) {
  const [liste, el] = await Promise.all([holen('/api/app/buchen/unterricht'), kontext.elemente()]);
  const ich = kontext.ich;
  kontext.titel('Neuer Unterricht', `<a class="nl-knopf" href="#/unterricht"><i class="bi bi-easel"></i>Mein Unterricht</a>`, `${liste.length}`);
  seite.innerHTML = `
    <div class="nl-leiste"><p class="nl-hinweis mb-0"><i class="bi bi-info-circle"></i>Legen Sie hier Unterricht an, der nicht im Stundenplan aus UntPlan steht, und verplanen Sie danach seine Stunden über „Zusätzliche Stunden“.</p>
      <button type="button" class="nl-hinzu ms-auto" data-neu><i class="bi bi-plus-lg"></i>Neuer Unterricht</button></div>
    <div class="nl-tabelle bu-tabelle">${liste.length ? `<table><thead><tr><th>Fach</th><th>Klassen</th><th>Lehrkraft</th><th>Bezeichnung</th><th class="text-end">Wst</th><th class="text-end">gebucht</th><th></th></tr></thead><tbody>
      ${liste.map(u => `<tr><td><span class="nl-tag">${e(u.fach)}</span></td><td><div class="nl-name">${e(u.klassen.join(', ') || '–')}</div></td><td>${e(u.lehrer)}</td>
        <td class="nl-text">${e(u.gruppe)}${u.text ? `<div class="nl-unter">${e(u.text)}</div>` : ''}</td><td class="text-end">${u.wochenstunden || ''}</td><td class="text-end">${u.gebuchteStunden} Std.</td>
        <td class="text-end text-nowrap"><a class="nl-knopf" href="#/buchen/stunden/${u.id}"><i class="bi bi-calendar-plus"></i>Zusätzliche Stunden</a>
          ${ich.admin || u.benutzerId === ich.id ? `<button type="button" class="nl-icon rot" data-weg="${u.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button>` : ''}</td></tr>`).join('')}
      </tbody></table>` : `<div class="p-3">${leer('Noch kein Unterricht in UntPlanWeb angelegt.', 'easel')}</div>`}</div>`;
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Unterricht löschen', 'Den Unterricht löschen? Seine kommenden zusätzlichen Stunden werden storniert.')) return;
    try { await api(`/api/app/buchen/unterricht/${b.dataset.weg}`, { methode: 'DELETE' }); toast('Gelöscht.'); kontext.navigieren('#/buchen/unterricht', true); } catch (err) { kontext.fehler(err); }
  });
  seite.querySelector('[data-neu]').onclick = () => dialog({
    titel: 'Neuer Unterricht', breit: true,
    inhalt: `<div class="row g-3">
      <div class="col-sm-6"><label class="form-label">Lehrkraft</label><select class="form-select" name="lehrer">${optionen(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), ich.element || '', 'bitte wählen')}</select></div>
      <div class="col-sm-6"><label class="form-label">Fach</label><select class="form-select" name="fach">${optionen(el.faecher.map(f => [f.kurz, f.kurz, f.name]), '', 'bitte wählen')}</select></div>
      <div class="col-sm-8"><label class="form-label">Klasse(n)</label><select class="form-select" name="klassen" multiple>${el.klassen.map(k => `<option value="${e(k.kurz)}">${e(k.kurz)}</option>`).join('')}</select></div>
      <div class="col-sm-4"><label class="form-label">Wochenstunden</label><input type="number" class="form-control" name="wochenstunden" min="0" max="40" value="1"></div>
      <div class="col-12"><label class="form-label">Bezeichnung / Schülergruppe</label><input class="form-control" name="gruppe" maxlength="100" placeholder="z. B. Förderkurs Mathematik"></div>
      <div class="col-12"><label class="form-label">Text</label><textarea class="form-control" name="text" rows="2" maxlength="1000"></textarea></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Anlegen', art: 'haupt', aktion: async box => {
      const w = n => box.querySelector(`[name=${n}]`);
      const u = await senden('/api/app/buchen/unterricht', { lehrer: w('lehrer').value || null, fach: w('fach').value, klassen: [...w('klassen').selectedOptions].map(o => o.value),
        gruppe: w('gruppe').value, text: w('text').value, wochenstunden: Number(w('wochenstunden').value) || 0 });
      toast('Unterricht angelegt – jetzt zusätzliche Stunden verplanen.');
      kontext.navigieren(`#/buchen/stunden/${u.id}`);
    } }],
  });
}
