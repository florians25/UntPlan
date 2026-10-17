// Buchen: Raum buchen, Ressource buchen, Neue Aktivität und zusätzliche Stunden als kurzer Assistent in drei Schritten
// (Termin → Raum bzw. Ressourcen mit Filtern direkt an der Liste → Angaben & buchen mit Live-Zusammenfassung) mit Periodizität
// (Zeitraum, Wiederholung, Wochentage, je Termin anderer Raum), Meine Buchungen (Kommend/Vergangen/Storniert, Suche, Serien
// zusammengefasst und aufklappbar, Storno einzeln oder Serie in einem Dialog, CSV), Raumübersicht (Belegungsraster Räume × Stunden bzw.
// Wochentage mit fester Kopfzeile/Raumspalte, Farben je Belegungsart, Hover-Details, Filtern, Zoom, Gruppen je Gebäude, auf dem Telefon
// Liste je Raum; freie Stunde anklicken → Schnellbuchung im Dialog, eigene Buchung anklicken → Storno), Ressourcenübersicht (Karten bzw.
// Liste mit Symbol je Art, Standort, Verfügbarkeit im gewählten Zeitraum, Belegungs-Zeitstrahl, Buchen direkt aus der Liste) und Neuer
// Unterricht mit zusätzlichen Stunden.
// Verwaltung (Ressourcen, Raumdaten): buchungsverwaltung.js. Raumliste: gemeinsame Komponente raumwahl.js.
import { e, holen, senden, api, toast, dialog, bestaetigen, leer, alarm, heuteIso, plusTage, montag, herunterladen, WOCHENTAGE_KURZ, suchfeldMitFilter, suchfeldMitFilterBinden, zeitraumPillen, segment, segmentSetzen, minuten, jetztMinuten } from '../ui.js';
import { kbDialog } from '../kbdialog.js';
import { raumListe } from '../raumwahl.js';

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
  raum: { titel: 'Raum buchen', schritte: ['Termin', 'Raum', 'Abschluss'] },
  ressource: { titel: 'Ressource buchen', schritte: ['Termin', 'Ressourcen', 'Abschluss'] },
  aktivitaet: { titel: 'Neue Aktivität', schritte: ['Termin', 'Raum', 'Abschluss'] },
  unterricht: { titel: 'Zusätzliche Stunden', schritte: ['Termin', 'Raum', 'Abschluss'] },
};
const SCHRITT_NAME = { Termin: 'Termin', Raum: 'Raum', Ressourcen: 'Ressourcen', Abschluss: 'Angaben & buchen' };
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
        <span class="bu-nr">${i < aktiv ? '<i class="bi bi-check-lg"></i>' : i + 1}</span><span class="bu-sname">${e(SCHRITT_NAME[s] ?? s)}</span></button></li>`).join('')}</ol>`;
}

/** Verfügbarkeit eines Raums über alle Termine als Status-Pill (frei / x von n frei / belegt). */
function raumStatus(r, termine) {
  const n = termine.length, b = r.belegt.length;
  if (!b) return { klasse: 'frei', text: n > 1 ? `alle ${n} frei` : 'frei' };
  const titel = `belegt: ${r.belegt.map(i => tagKurz(termine[i].termin.datum)).join(', ')}`;
  return b >= n ? { klasse: 'belegt', text: 'belegt', titel } : { klasse: 'teil', text: `${n - b} von ${n} frei`, titel };
}

/** Storno einer Buchung (einzeln bzw. Serie) mit Rückfrage im einheitlichen Dialog. Ergebnis: Anzahl stornierter Termine (0 = abgebrochen). */
export function stornoDialog(b, serienAnzahl = 0) {
  return new Promise(fertig => {
    let ergebnis = 0;
    const serie = !!b.serie && serienAnzahl > 1;
    const box = kbDialog({
      titel: serie ? 'Serie oder Termin stornieren' : 'Buchung stornieren', breite: 's',
      inhalt: `<div class="bu-storno-kopf"><b>${e(b.titel || b.artName || 'Buchung')}</b><span>${e(tagLang(b.datum))} · ${stdText(b.stundeVon, b.stundeBis)}${b.raum ? ` · ${e(b.raum)}` : ''}</span></div>
        ${serie ? `<div class="bu-storno-wahl" role="radiogroup" aria-label="Umfang">
          <label><input type="radio" name="bu-storno" value="termin" checked><span><b>Nur diesen Termin</b><small>${e(tagKurz(b.datum))}</small></span></label>
          <label><input type="radio" name="bu-storno" value="serie"><span><b>Alle kommenden Termine der Serie</b><small>Serie mit ${serienAnzahl} Terminen – vergangene bleiben erhalten</small></span></label></div>`
          : '<p class="mb-0 nl-unter">Der Termin wird storniert und aus den Plänen entfernt.</p>'}`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Stornieren', art: 'haupt', aktion: async fenster => {
        const ganze = fenster.querySelector('[name=bu-storno]:checked')?.value === 'serie';
        const r = await api(`/api/app/buchen/${b.id}${ganze ? '?serie=true' : ''}`, { methode: 'DELETE' });
        ergebnis = r.storniert || 1;
        toast(ergebnis === 1 ? 'Termin storniert.' : `${ergebnis} Termine storniert.`);
      } }],
    });
    box.querySelector('[data-kbd-knopf="1"]').className = 'nl-knopf kbd-gefahr';
    box.geschlossen.then(() => fertig(ergebnis));
  });
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
    `<a class="nl-knopf" href="#/buchen/raeume"><i class="bi bi-grid-3x3-gap"></i>Raumübersicht</a><a class="nl-knopf" href="#/buchen/meine"><i class="bi bi-list-ul"></i>Meine Buchungen</a>`);

  // Vorbelegung aus der Raum- bzw. Ressourcenübersicht: #/buchen/raum/{datum}/{von}/{bis}/{raum}, #/buchen/ressource/{datum}/{von}/{bis}/{id}
  const [pDatum, pVon, pBis, pRaum] = rest;
  const vorbelegt = (art === 'raum' || art === 'ressource') && /^\d{4}-\d{2}-\d{2}$/.test(pDatum ?? '');
  // Start am ersten Unterrichtstag ab heute (nicht am Wochenende oder in den Ferien)
  const startDatum = vorbelegt ? pDatum : (opt.naechsterTag && opt.naechsterTag > heute ? opt.naechsterTag : heute);
  const z = {
    wiederholung: 'einmalig', datum: startDatum, von: startDatum, bis: plusTage(startDatum, 56), wochentage: [Math.min(tagIndex(startDatum), 4)],
    modus: 'stunden', stundeVon: Number(pVon) || raster[0]?.nr || 1, stundeBis: Number(pBis) || Number(pVon) || raster[0]?.nr || 1,
    beginn: '13:30', ende: '14:15',
  };
  const zustand = {
    schritt: 0, zeit: z, raumFilter: {}, ressourcenFilter: { arten: new Set(), nurFrei: true },
    suche: null, raum: vorbelegt && art === 'raum' ? pRaum || null : null, ersatz: {}, ressourcen: new Map(), termine: null,
    details: { klassen: [], lehrer: ich.element && opt.ich ? opt.ich : '', fach: '', titel: '', text: '', benachrichtigen: false },
    hinweise: [], gespeichert: null,
  };
  const S = def.schritte;

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
    const n = S[zustand.schritt];
    if (n === 'Termin') return terminSchritt();
    if (n === 'Raum') return raumSchritt(art !== 'raum');
    if (n === 'Ressourcen') return ressourcenSchritt();
    return abschlussSchritt();
  }

  const knopfleiste = (zurueck, weiter, weiterText = 'Weiter', weiterSymbol = 'arrow-right') => `<div class="bu-knopfleiste">
      ${zurueck ? '<button type="button" class="nl-knopf bu-gross" data-zurueck><i class="bi bi-arrow-left"></i>Zurück</button>' : '<span></span>'}
      ${weiter ? `<button type="button" class="nl-hinzu bu-gross" data-weiter>${e(weiterText)}<i class="bi bi-${weiterSymbol}"></i></button>` : ''}</div>`;
  const knoepfeVerbinden = (zurueck, weiter) => {
    inhalt.querySelector('[data-zurueck]')?.addEventListener('click', zurueck ?? (() => gehe(zustand.schritt - 1)));
    const w = inhalt.querySelector('[data-weiter]');
    if (w && weiter) w.addEventListener('click', async () => {
      w.disabled = true;
      try { await weiter(); } catch (err) { kontext.fehler(err); } finally { if (w.isConnected) w.disabled = false; }
    });
  };

  // ---------------------------------------------------------------- Schritt 1: Termin

  function terminSchritt() {
    const einmalig = z.wiederholung === 'einmalig';
    const tage = WOCHENTAGE_KURZ.slice(0, opt.tageProWoche);
    inhalt.innerHTML = `
      ${unterricht ? unterrichtKopf(unterricht) : ''}
      <section class="bu-karte">
        <header><i class="bi bi-calendar-event"></i><h2>Wann?</h2></header>
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
        ${einmalig || z.wiederholung === 'taeglich' ? '' : `<div class="bu-feld bu-tagwahl"><span>Wochentage</span><div class="bu-tage">${tage.map((t, i) => `<button type="button" class="bu-tag ${z.wochentage.includes(i) ? 'an' : ''}" aria-pressed="${z.wochentage.includes(i)}" data-tag="${i}">${t}</button>`).join('')}</div></div>`}
        <div class="bu-vorschau" data-vorschau></div>
      </section>
      ${knopfleiste(false, true, art === 'ressource' ? 'Ressourcen wählen' : 'Raum wählen')}`;
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
      ziel.innerHTML = `<div class="bu-vorschau-kopf ${n ? '' : 'leer'}"><i class="bi bi-${n ? 'calendar-check' : 'calendar-x'}"></i><b>${n ? `${n} ${n === 1 ? 'Termin' : 'Termine'}` : 'Kein Unterrichtstag im gewählten Zeitbereich'}</b>
          ${t.beginn && n ? `<span class="nl-unter">${e(t.beginn)}–${e(t.ende)} Uhr</span>` : ''}
          ${t.uebersprungen.length ? `<span class="nl-chip grau" title="${e(t.uebersprungen.map(tagKurz).join(', '))}">${t.uebersprungen.length} unterrichtsfreie ${t.uebersprungen.length === 1 ? 'Tag' : 'Tage'} ausgelassen</span>` : ''}</div>
        ${n ? `<div class="nl-chips">${t.termine.slice(0, 24).map(x => `<span class="nl-tag">${e(tagKurz(x.datum))}</span>`).join('')}${n > 24 ? `<span class="nl-tag">+${n - 24}</span>` : ''}</div>` : ''}`;
      return t;
    } catch (err) {
      if (ziel && nr === vorschauNr) ziel.innerHTML = `<div class="bu-fehler"><i class="bi bi-exclamation-circle"></i>${e(err.message)}</div>`;
      if (pflicht) throw err;
      return null;
    }
  }

  // ---------------------------------------------------------------- Suche (alle buchbaren Räume bzw. Ressourcen mit Verfügbarkeit; gefiltert wird an der Liste)

  async function suchen() {
    const ressourcen = art === 'ressource';
    zustand.suche = await senden(`/api/app/buchen/suche/${ressourcen ? 'ressourcen' : 'raeume'}`, { zeit: zeitDaten(z), kriterien: { maxBelegung: 100 } });
    zustand.termine = (zustand.suche.termine ?? []).map(t => t.termin ?? t);
    if (ressourcen) {
      for (const id of [...zustand.ressourcen.keys()]) if (!zustand.suche.ressourcen.some(r => r.id === id && r.belegt.length < zustand.termine.length)) zustand.ressourcen.delete(id);
    } else if (zustand.raum) {
      const r = zustand.suche.raeume.find(x => x.kurz.toLowerCase() === zustand.raum.toLowerCase());
      zustand.raum = r && r.belegt.length < zustand.termine.length ? r.kurz : null;
    }
    zustand.ersatz = {};
  }

  /** Leiste mit dem gewählten Zeitbereich (anklickbar: zurück zum Termin). */
  const zeitLeiste = () => `<button type="button" class="bu-zusammenfassung" data-zum-termin title="Termin ändern"><i class="bi bi-calendar-event"></i><span>${e(zeitText(z))}</span>
      <span class="nl-unter">${zustand.termine?.length ?? 0} ${zustand.termine?.length === 1 ? 'Termin' : 'Termine'}</span><span class="bu-aendern">Ändern</span></button>`;
  const zeitLeisteVerbinden = () => inhalt.querySelector('[data-zum-termin]')?.addEventListener('click', () => gehe(0));

  // ---------------------------------------------------------------- Schritt 2: Raum (mit Filtern an der Liste)

  async function raumSchritt(optional) {
    if (!zustand.suche) { inhalt.innerHTML = '<div class="u-laedt"></div>'; await suchen(); }
    const s = zustand.suche;
    const n = s.termine.length;
    inhalt.innerHTML = `
      ${unterricht ? unterrichtKopf(unterricht) : ''}
      ${zeitLeiste()}
      ${s.uebersprungen.length ? `<p class="nl-hinweis"><i class="bi bi-info-circle"></i>${s.uebersprungen.length} unterrichtsfreie Tage im Zeitraum werden ausgelassen.</p>` : ''}
      <section class="bu-karte bu-raumkarte">
        <header><i class="bi bi-door-open"></i><h2>${optional ? 'Raum (optional)' : 'Raum wählen'}</h2>${n > 1 ? `<span class="nl-unter ms-auto">Verfügbarkeit über alle ${n} Termine</span>` : ''}</header>
        <div data-raumliste></div>
      </section>
      <div data-ersatz></div>
      ${knopfleiste(true, true)}`;
    zeitLeisteVerbinden();
    const ersatzZeichnen = () => {
      const ziel = inhalt.querySelector('[data-ersatz]');
      const r = s.raeume.find(x => x.kurz.toLowerCase() === zustand.raum?.toLowerCase());
      if (!r || !r.belegt.length) { ziel.innerHTML = ''; return; }
      ziel.innerHTML = `<section class="bu-karte"><header><i class="bi bi-arrow-left-right"></i><h2>${e(r.kurz)} ist an ${r.belegt.length} ${r.belegt.length === 1 ? 'Termin' : 'Terminen'} belegt</h2></header>
        <p class="nl-unter mb-2">Wählen Sie für diese Termine einen anderen freien Raum${optional ? ', buchen Sie ohne Raum' : ''} oder lassen Sie den Termin aus.</p>
        <div class="bu-ersatzliste">${r.belegt.map(i => { const t = s.termine[i]; const frei = t.freieRaeume; return `<div class="bu-ersatz"><span class="bu-ersatz-tag"><b>${e(tagKurz(t.termin.datum))}</b><small>${stdText(t.termin.stundeVon, t.termin.stundeBis)}</small></span>
          <select class="form-select" data-ersatz-i="${i}" aria-label="Anderer Raum am ${e(tagKurz(t.termin.datum))}">${optionen(frei.map(x => [x, x, opt.raeume.find(y => y.kurz === x)?.name ?? '']), zustand.ersatz[i] ?? '', optional ? 'Ohne Raum' : 'Termin auslassen')}</select></div>`; }).join('')}
        </div></section>`;
      ziel.querySelectorAll('[data-ersatz-i]').forEach(sel => sel.onchange = () => { zustand.ersatz[sel.dataset.ersatzI] = sel.value; });
    };
    raumListe(inhalt.querySelector('[data-raumliste]'), {
      raeume: s.raeume, gewaehlt: zustand.raum, filter: zustand.raumFilter,
      nurFreiText: n > 1 ? 'Nur an allen Terminen frei' : 'Nur freie',
      status: r => raumStatus(r, s.termine), waehlbar: r => r.belegt.length < n,
      zusatz: r => [r.raumart, ...(r.ressourcen ?? [])].filter(Boolean).length ? `<small>${e([r.raumart, ...(r.ressourcen ?? [])].filter(Boolean).join(' · '))}</small>` : '',
      ohne: optional ? 'Ohne Raum buchen' : null,
      leerText: n > 1 ? 'Kein Raum ist an allen Terminen frei – schalten Sie „Nur an allen Terminen frei“ aus und wählen Sie für belegte Termine einen anderen Raum.' : 'Zu dieser Zeit ist kein passender Raum frei.',
      beiWahl: kurz => { zustand.raum = kurz; zustand.ersatz = {}; ersatzZeichnen(); },
    });
    ersatzZeichnen();
    knoepfeVerbinden(null, async () => {
      if (!optional && !zustand.raum) throw new Error('Bitte einen Raum wählen.');
      if (!gewaehlteTermine().length) throw new Error('Es bleibt kein Termin übrig – bitte für belegte Termine einen anderen Raum wählen.');
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

  // ---------------------------------------------------------------- Schritt 2: Ressourcen (Arten-Filter an der Liste)

  async function ressourcenSchritt() {
    if (!zustand.suche) { inhalt.innerHTML = '<div class="u-laedt"></div>'; await suchen(); }
    const s = zustand.suche;
    const n = s.termine.length;
    const f = zustand.ressourcenFilter;
    const arten = [...new Set(s.ressourcen.map(r => r.art).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de'));
    inhalt.innerHTML = `
      ${zeitLeiste()}
      <section class="bu-karte">
        <header><i class="bi bi-box-seam"></i><h2>Ressourcen wählen</h2></header>
        <div class="rw-filter bu-resfilter" ${s.ressourcen.length ? '' : 'hidden'}>
          ${arten.map(a => `<button type="button" class="bu-filterchip ${f.arten.has(a) ? 'an' : ''}" aria-pressed="${f.arten.has(a)}" data-resart="${e(a)}">${e(a)}</button>`).join('')}
          <label class="rw-schalter ms-auto"><input type="checkbox" data-res-nurfrei ${f.nurFrei ? 'checked' : ''}><span class="rw-spur" aria-hidden="true"></span>${n > 1 ? 'Nur an allen Terminen verfügbar' : 'Nur verfügbare'}</label>
        </div>
        <div data-resliste></div>
      </section>
      <div data-auslassen></div>
      ${knopfleiste(true, true)}`;
    zeitLeisteVerbinden();
    const listeZeichnen = () => {
      const sichtbar = s.ressourcen.filter(r => (!f.arten.size || f.arten.has(r.art)) && (!f.nurFrei || !r.belegt.length || zustand.ressourcen.has(r.id)));
      inhalt.querySelector('[data-resliste]').innerHTML = sichtbar.length ? `<div class="bu-resliste">${sichtbar.map(r => {
        const an = zustand.ressourcen.has(r.id);
        const aus = r.belegt.length >= n;
        const maxMenge = Math.max(1, r.verfuegbar.reduce((m, x) => Math.max(m, x), 0));
        const status = !r.belegt.length ? `<span class="rw-status frei">${r.frei} verfügbar</span>` : aus ? '<span class="rw-status belegt">ausgebucht</span>' : `<span class="rw-status teil">${n - r.belegt.length} von ${n} Terminen</span>`;
        return `<div class="bu-res ${an ? 'gewaehlt' : ''} ${aus ? 'aus' : ''}" data-res="${r.id}">
          <label class="bu-res-wahl"><input type="checkbox" class="nl-rund" data-wahl="${r.id}" ${an ? 'checked' : ''} ${aus ? 'disabled' : ''} aria-label="${e(r.name)} wählen"></label>
          <div class="bu-res-text"><span class="bu-res-symbol ton-${resTon(r)}"><i class="bi bi-${e(ressourcenSymbol(r))}" aria-hidden="true"></i></span><span><b>${e(r.name)}</b><small>${e([r.art, r.standort ? `Standort: ${r.standort}` : '', r.bemerkung].filter(Boolean).join(' · '))}</small></span></div>
          <label class="bu-res-menge" title="Menge">${r.anzahl > 1 ? `<input type="number" class="form-control" min="1" max="${maxMenge}" value="${zustand.ressourcen.get(r.id) ?? 1}" data-menge="${r.id}" aria-label="Menge"><span>von ${r.anzahl}</span>` : ''}</label>
          ${status}</div>`;
      }).join('')}</div>`
        : `<div class="rw-leer gross"><i class="bi bi-box-seam" aria-hidden="true"></i><span>${opt.ressourcen.some(r => !r.raum) ? 'Keine passende Ressource ist verfügbar.' : 'Es sind noch keine mobilen Ressourcen angelegt.'}</span>
          ${s.ressourcen.length && (f.arten.size || f.nurFrei) ? '<button type="button" class="nl-knopf" data-res-alle>Alle zeigen</button>' : ''}
          ${opt.admin && !s.ressourcen.length ? '<a class="nl-knopf" href="#/buchen/verwaltung">Ressourcen verwalten</a>' : ''}</div>`;
      inhalt.querySelector('[data-res-alle]')?.addEventListener('click', () => { f.arten.clear(); f.nurFrei = false; ressourcenSchritt(); });
      inhalt.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => {
        const id = Number(c.dataset.wahl);
        if (c.checked) zustand.ressourcen.set(id, Number(inhalt.querySelector(`[data-menge="${id}"]`)?.value) || 1); else zustand.ressourcen.delete(id);
        c.closest('.bu-res').classList.toggle('gewaehlt', c.checked);
        auslassen();
      });
      inhalt.querySelectorAll('[data-menge]').forEach(x => x.onchange = () => {
        const id = Number(x.dataset.menge);
        x.value = String(Math.max(1, Math.min(Number(x.max), Number(x.value) || 1)));
        if (zustand.ressourcen.has(id)) zustand.ressourcen.set(id, Number(x.value));
        auslassen();
      });
      inhalt.querySelectorAll('.bu-res:not(.aus)').forEach(z2 => z2.addEventListener('click', ev => {
        if (ev.target.closest('input, label')) return;
        const c = z2.querySelector('[data-wahl]'); c.checked = !c.checked; c.dispatchEvent(new Event('change'));
      }));
    };
    const auslassen = () => {
      const ziel = inhalt.querySelector('[data-auslassen]');
      const bleiben = gewaehlteTermine().length;
      ziel.innerHTML = zustand.ressourcen.size && bleiben < n ? `<p class="nl-hinweis"><i class="bi bi-info-circle"></i>${n - bleiben} ${n - bleiben === 1 ? 'Termin wird' : 'Termine werden'} ausgelassen, weil die gewählte Menge dort nicht verfügbar ist.</p>` : '';
    };
    inhalt.querySelectorAll('[data-resart]').forEach(b => b.onclick = () => {
      const a = b.dataset.resart;
      if (f.arten.has(a)) f.arten.delete(a); else f.arten.add(a);
      b.classList.toggle('an', f.arten.has(a)); b.setAttribute('aria-pressed', String(f.arten.has(a)));
      listeZeichnen();
    });
    inhalt.querySelector('[data-res-nurfrei]').onchange = ev => { f.nurFrei = ev.target.checked; listeZeichnen(); };
    listeZeichnen();
    auslassen();
    knoepfeVerbinden(null, async () => {
      if (!zustand.ressourcen.size) throw new Error('Bitte mindestens eine Ressource wählen.');
      if (!gewaehlteTermine().length) throw new Error('Die Auswahl ist an keinem Termin verfügbar.');
      await gehe(zustand.schritt + 1);
    });
  }

  // ---------------------------------------------------------------- Schritt 3: Angaben & buchen (Formular links, Zusammenfassung rechts)

  function abschlussSchritt() {
    const d = zustand.details;
    const u = unterricht;
    const aktivitaet = art === 'aktivitaet';
    const termine = gewaehlteTermine();
    const raeume = [...new Set(termine.map(t => t.raum).filter(Boolean))];
    const res = [...zustand.ressourcen.entries()].map(([id, m]) => { const r = zustand.suche?.ressourcen?.find(x => x.id === id); return `${m > 1 ? `${m}× ` : ''}${r?.name ?? id}`; });
    const formular = u ? '' : `<section class="bu-karte">
        <header><i class="bi bi-pencil-square"></i><h2>${aktivitaet ? 'Aktivität' : 'Angaben'}</h2><span class="nl-unter ms-auto">optional</span></header>
        <div class="bu-raster zwei">
          ${feld('Titel / Anlass', `<input class="form-control" maxlength="200" data-d="titel" value="${e(d.titel)}" placeholder="${aktivitaet ? 'z. B. Förderunterricht, Exkursion' : 'z. B. Projektarbeit'}">`, 'breit')}
          ${feld('Klasse(n)', `<select multiple class="form-select" data-d="klassen" data-platzhalter="keine">${el.klassen.map(k => `<option value="${e(k.kurz)}" ${d.klassen.includes(k.kurz) ? 'selected' : ''} data-zweit="${e(k.name !== k.kurz ? k.name : '')}">${e(k.kurz)}</option>`).join('')}</select>`)}
          ${feld('Lehrkraft', `<select class="form-select" data-d="lehrer">${optionen(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), d.lehrer, 'keine')}</select>`)}
          ${feld('Fach', `<select class="form-select" data-d="fach">${optionen(el.faecher.map(f => [f.kurz, f.kurz, f.name]), d.fach, 'kein Fach')}</select>`)}
          ${feld('Bemerkung (für alle sichtbar)', `<textarea class="form-control" rows="2" maxlength="1000" data-d="text">${e(d.text)}</textarea>`, 'breit')}
          ${aktivitaet ? `<label class="bu-schalter breit"><input type="checkbox" class="form-check-input" data-d="benachrichtigen" ${d.benachrichtigen ? 'checked' : ''}>
            <span>Lehrkraft der Aktivität benachrichtigen <small class="nl-unter">(Mitteilung im Posteingang)</small></span></label>` : ''}
        </div></section>`;
    inhalt.innerHTML = `
      ${u ? unterrichtKopf(u) : ''}
      <div class="bu-abschluss ${u ? 'ohne-formular' : ''}">
        <div class="bu-abschluss-links">${formular}</div>
        <aside class="bu-karte bu-zf-karte">
          <header><i class="bi bi-clipboard-check"></i><h2>Zusammenfassung</h2><span class="nl-chip ${ART_CHIP[art]} ms-auto">${ART_NAME[art]}</span></header>
          <div class="bu-zf-raster" data-zf></div>
          <div data-hinweise></div>
          <p class="nl-unter bu-zf-info"><i class="bi bi-info-circle me-1"></i>Die Buchung erscheint sofort in „Meine Buchungen“ und in den Stundenplänen${art === 'ressource' ? '' : ' und wird an UntPlan übermittelt'}.</p>
        </aside>
      </div>
      ${knopfleiste(true, true, termine.length > 1 ? `${termine.length} Termine buchen` : 'Jetzt buchen', 'check-lg')}`;
    const zeile = (t, w) => w ? `<div class="bu-zf"><span>${e(t)}</span><div>${w}</div></div>` : '';
    const zfZeichnen = () => {
      inhalt.querySelector('[data-zf]').innerHTML = `
        ${zeile('Titel', e(d.titel || (u ? u.gruppe || 'Zusätzlicher Unterricht' : ART_NAME[art])))}
        ${zeile('Zeitbereich', e(zeitText(z)))}
        ${zeile(termine.length === 1 ? 'Termin' : `${termine.length} Termine (Serie)`, `<div class="nl-chips">${termine.slice(0, 12).map(t => `<span class="nl-tag" title="${e(stdText(t.stundeVon, t.stundeBis))}${t.raum ? ` · ${e(t.raum)}` : ''}">${e(tagKurz(t.datum))}${raeume.length > 1 && t.raum ? ` · ${e(t.raum)}` : ''}</span>`).join('')}${termine.length > 12 ? `<span class="nl-tag">+${termine.length - 12}</span>` : ''}</div>`)}
        ${zeile(raeume.length > 1 ? 'Räume' : 'Raum', e(raeume.join(', ')) || (art === 'ressource' ? '' : '<span class="nl-unter">ohne Raum</span>'))}
        ${zeile('Ressourcen', e(res.join(', ')))}
        ${zeile('Klasse(n)', e((u ? u.klassen : d.klassen).join(', ')))}
        ${zeile('Lehrkraft', e(u ? u.lehrer : d.lehrer))}
        ${zeile('Fach', e(u ? u.fach : d.fach))}
        ${zeile('Bemerkung', e(d.text))}
        ${aktivitaet && d.benachrichtigen && d.lehrer ? zeile('Benachrichtigung', `Mitteilung an ${e(d.lehrer)}`) : ''}`;
    };
    const hinweise = async () => {
      const ziel = inhalt.querySelector('[data-hinweise]');
      if (u) { ziel.innerHTML = ''; return; }
      try {
        const h = await senden('/api/app/buchen/hinweise', { termine, lehrer: d.lehrer || null, klassen: d.klassen });
        zustand.hinweise = h;
        ziel.innerHTML = h.length ? `<div class="bu-warnung"><i class="bi bi-exclamation-triangle"></i><div><b>Zur gleichen Zeit schon eingeplant</b><ul>${h.slice(0, 6).map(x => `<li>${e(x)}</li>`).join('')}${h.length > 6 ? `<li>… und ${h.length - 6} weitere</li>` : ''}</ul><small>Die Buchung ist trotzdem möglich.</small></div></div>` : '';
      } catch { ziel.innerHTML = ''; }
    };
    inhalt.querySelectorAll('[data-d]').forEach(f => f.addEventListener(f.tagName === 'TEXTAREA' || (f.tagName === 'INPUT' && f.type !== 'checkbox') ? 'input' : 'change', () => {
      const k = f.dataset.d;
      d[k] = k === 'klassen' ? [...f.selectedOptions].map(o => o.value) : f.type === 'checkbox' ? f.checked : f.value;
      zfZeichnen();
      if (k === 'klassen' || k === 'lehrer') hinweise();
    }));
    zfZeichnen();
    hinweise();
    knoepfeVerbinden(null, async () => {
      const daten = {
        art, termine, ressourcen: [...zustand.ressourcen.entries()].map(([id, menge]) => ({ id, menge })),
        klassen: u ? [] : d.klassen, lehrer: u ? null : (d.lehrer || ''), fach: u ? null : d.fach, titel: d.titel, text: d.text,
        benachrichtigen: aktivitaet && d.benachrichtigen, unterrichtId: u?.id ?? null,
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
        <button type="button" class="nl-hinzu bu-gross" data-neu><i class="bi bi-plus-lg"></i>Weitere Buchung</button></div></section>`;
    inhalt.querySelector('[data-neu]').onclick = () => kontext.navigieren(location.hash.split('/').slice(0, art === 'unterricht' ? 4 : 3).join('/'), true);
  }

  await zeichnen();
  // Vorbelegung aus der Raumübersicht: gleich die Raumliste mit dem gewählten Raum zeigen
  if (vorbelegt && zustand.raum) {
    try {
      const t = await vorschau(true);
      if (t?.termine.length) { zustand.termine = t.termine; const raum = zustand.raum; await suchen(); zustand.raum ??= null; if (!zustand.raum && raum) toast(`${raum} ist zu dieser Zeit nicht frei.`, true); await gehe(1); }
    } catch (err) { kontext.fehler(err); }
  }
  if (vorbelegt && art === 'ressource' && Number(pRaum)) {
    try {
      const t = await vorschau(true);
      if (t?.termine.length) {
        zustand.termine = t.termine;
        await suchen();
        const r = zustand.suche.ressourcen.find(x => x.id === Number(pRaum));
        if (r && r.belegt.length < zustand.termine.length) zustand.ressourcen.set(r.id, 1); else toast('Die Ressource ist zu dieser Zeit nicht verfügbar.', true);
        await gehe(1);
      }
    } catch (err) { kontext.fehler(err); }
  }
}

function unterrichtKopf(u) {
  return `<div class="bu-zusammenfassung statisch"><i class="bi bi-easel"></i><b>${e(u.fach)}</b><span>${e(u.klassen.join(', ') || 'ohne Klasse')}</span><span class="nl-tag">${e(u.lehrer)}</span>
    ${u.gruppe ? `<span class="nl-unter">${e(u.gruppe)}</span>` : ''}<span class="nl-chip grau ms-auto">${u.gebuchteStunden} Stunden gebucht</span></div>`;
}

// ------------------------------------------------------------------ Meine Buchungen

const ANSICHTEN = [['kommend', 'Kommend'], ['vergangen', 'Vergangen'], ['storniert', 'Storniert'], ['alle', 'Alle']];

async function meineBuchungen(seite, kontext, [vonParam, bisParam, alleParam]) {
  const ich = kontext.ich;
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(heuteIso(), 180);
  const alle = ich.admin && alleParam === 'alle';
  const ziel = (v = von, b = bis, a = alle) => `#/buchen/meine/${v}/${b}${a ? '/alle' : ''}`;
  kontext.titel(alle ? 'Alle Buchungen' : 'Meine Buchungen', '', '');
  const liste = await holen(`/api/app/buchen/liste?von=${von}&bis=${bis}${alle ? '&alle=true' : ''}`);
  const heute = heuteIso();
  const filter = meineFilter;
  const serien = liste.reduce((m, b) => (b.serie ? m.set(b.serie, (m.get(b.serie) ?? 0) + 1) : m), new Map());
  const offenSerien = new Set();
  const anzahl = a => liste.filter(b => passtAnsicht(b, a, heute)).length;
  kontext.titel(alle ? 'Alle Buchungen' : 'Meine Buchungen', `<a class="nl-knopf" href="#/buchen/raeume"><i class="bi bi-grid-3x3-gap"></i>Raumübersicht</a>`, `${anzahl('kommend')}`);
  seite.innerHTML = `
    <div class="u-sf-leiste bu-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Titel, Raum, Klasse …', wert: filter.suche, label: 'Buchungen durchsuchen', klasse: 'bu-meine-suche',
        filter: [{ name: 'art', label: 'Art', optionen: Object.entries(ART_NAME), wert: filter.art, alle: 'Alle Arten' }] })}
      ${zeitraumPillen(von, bis)}
      <div class="u-sf-rechts"><button type="button" class="nl-knopf" data-csv title="Als CSV herunterladen"><i class="bi bi-filetype-csv"></i>Export</button>
        <a class="nl-hinzu" href="#/buchen/raum"><i class="bi bi-plus-lg"></i>Neue Buchung</a></div>
    </div>
    <div class="nl-leiste bu-leiste2">
      <div class="bu-segment" role="radiogroup" aria-label="Ansicht">${ANSICHTEN.map(([w, t]) => `<button type="button" role="radio" aria-checked="${filter.ansicht === w}" class="${filter.ansicht === w ? 'an' : ''}" data-ansicht="${w}">${t}<span class="bu-anzahl">${anzahl(w)}</span></button>`).join('')}</div>
      ${ich.admin ? `<label class="rw-schalter ms-auto"><input type="checkbox" data-alle ${alle ? 'checked' : ''}><span class="rw-spur" aria-hidden="true"></span>Buchungen aller Lehrkräfte</label>` : ''}
    </div>
    <div data-liste></div>`;
  const sichtbar = () => liste.filter(b => passtAnsicht(b, filter.ansicht, heute) && (!filter.art || b.art === filter.art)
    && (!filter.suche || [b.titel, b.text, b.raum, b.klassen.join(' '), b.lehrer, b.fach, b.absender, b.artName, b.ressourcen.map(r => r.name).join(' '), datumDe(b.datum)].join(' ').toLowerCase().includes(filter.suche.toLowerCase())))
    .sort((a, b) => (filter.ansicht === 'vergangen' ? -1 : 1) * (a.datum.localeCompare(b.datum) || a.stundeVon - b.stundeVon));
  const darf = b => (ich.admin || b.benutzerId === ich.id) && b.status === 'aktiv' && (ich.admin || b.datum >= heute);
  const zeile = (b, kind = false) => `<div class="bu-eintrag-zeile ${b.status === 'storniert' ? 'storniert' : ''} ${b.datum < heute ? 'vergangen' : ''} ${kind ? 'kind' : ''}">
      <div class="bu-datum"><b>${e(new Date(b.datum + 'T12:00').getDate())}</b><span>${e(new Date(b.datum + 'T12:00').toLocaleDateString('de-DE', { month: 'short' }))}</span><small>${e(new Date(b.datum + 'T12:00').toLocaleDateString('de-DE', { weekday: 'short' }))}</small></div>
      <div class="bu-haupt">
        <div class="bu-titelzeile"><span class="bu-titel">${e(b.titel || b.artName)}</span><span class="nl-chip ${ART_CHIP[b.art] ?? 'grau'}">${e(b.artName)}</span></div>
        <div class="bu-meta"><span><i class="bi bi-clock"></i>${stdText(b.stundeVon, b.stundeBis)} · ${e(b.beginn)}–${e(b.ende)}</span>
          ${b.raum ? `<span><i class="bi bi-door-open"></i><a href="#/plan/raum/${encodeURIComponent(b.raum)}/${b.datum}">${e(b.raum)}</a></span>` : ''}
          ${b.ressourcen.length ? `<span><i class="bi bi-box-seam"></i>${e(b.ressourcen.map(r => `${r.menge > 1 ? `${r.menge}× ` : ''}${r.name}`).join(', '))}</span>` : ''}
          ${b.klassen.length ? `<span><i class="bi bi-people"></i>${e(b.klassen.join(', '))}</span>` : ''}
          ${b.lehrer ? `<span><i class="bi bi-person"></i>${e(b.lehrer)}${b.fach ? ` · ${e(b.fach)}` : ''}</span>` : ''}
          ${alle || b.benutzerId !== ich.id ? `<span class="nl-unter">gebucht von ${e(b.absender)}</span>` : ''}</div>
        ${b.text ? `<div class="bu-text">${e(b.text)}</div>` : ''}
      </div>
      <div class="bu-status">${b.status === 'storniert' ? '<span class="rw-status belegt">storniert</span>' : b.uebertragen ? '<span class="rw-status frei" title="von UntPlan übernommen">übernommen</span>' : b.datum < heute ? '<span class="rw-status">vergangen</span>' : '<span class="rw-status teil">gebucht</span>'}</div>
      <div class="bu-aktionen">${darf(b) ? `<button type="button" class="nl-knopf klein" data-storno="${b.id}"><i class="bi bi-x-circle"></i>Stornieren</button>` : ''}</div></div>`;
  const zeichnen = () => {
    const l = sichtbar();
    const box = seite.querySelector('[data-liste]');
    if (!l.length) {
      const nichts = !liste.length;
      box.innerHTML = `<div class="bu-leer"><i class="bi bi-calendar-x" aria-hidden="true"></i><b>${nichts ? 'Keine Buchungen im Zeitraum' : 'Keine Buchungen für diesen Filter'}</b>
        <span>${nichts ? `Zeitraum ${datumDe(von)} – ${datumDe(bis)}` : 'Ändern Sie Ansicht, Art oder Suche.'}</span>
        <div class="d-flex gap-2 mt-2">${nichts ? '' : '<button type="button" class="nl-knopf" data-reset>Filter zurücksetzen</button>'}<a class="nl-hinzu" href="#/buchen/raum"><i class="bi bi-plus-lg"></i>Raum buchen</a></div></div>`;
      box.querySelector('[data-reset]')?.addEventListener('click', () => { Object.assign(filter, { ansicht: 'alle', art: '', suche: '' }); kontext.navigieren(ziel(), true); });
      return;
    }
    // Serien zusammenfassen: erste sichtbare Buchung der Serie als Kopf, weitere aufklappbar
    const gesehen = new Map();
    l.forEach(b => { if (b.serie) gesehen.set(b.serie, [...(gesehen.get(b.serie) ?? []), b]); });
    const html = [];
    const fertig = new Set();
    let monat = '';
    for (const b of l) {
      if (b.serie && fertig.has(b.serie)) continue;
      const m = new Date(b.datum + 'T12:00').toLocaleDateString('de-DE', { month: 'long', year: 'numeric' });
      if (m !== monat) { monat = m; html.push(`<div class="bu-monat">${e(m)}</div>`); }
      const gruppe = b.serie ? gesehen.get(b.serie) : null;
      if (gruppe && gruppe.length > 1) {
        fertig.add(b.serie);
        const auf = offenSerien.has(b.serie);
        const kommend = gruppe.filter(x => x.status === 'aktiv' && x.datum >= heute);
        html.push(`<div class="bu-serie ${auf ? 'auf' : ''}">${zeile(b)}
          <div class="bu-serienkopf"><button type="button" class="bu-serie-knopf" data-serie-auf="${e(b.serie)}" aria-expanded="${auf}"><i class="bi bi-arrow-repeat"></i>Serie · ${gruppe.length === serien.get(b.serie) ? `${gruppe.length} Termine` : `${gruppe.length} von ${serien.get(b.serie)} Terminen`}
            · bis ${e(tagKurz(gruppe[gruppe.length - 1].datum))} · ${auf ? 'zuklappen' : 'alle zeigen'}<i class="bi bi-chevron-down bu-chevron"></i></button>
            ${kommend.length && darf(kommend[0]) ? `<button type="button" class="nl-knopf klein rot" data-serie-storno="${kommend[0].id}"><i class="bi bi-trash"></i>Serie stornieren</button>` : ''}</div>
          ${auf ? `<div class="bu-kinder">${gruppe.slice(1).map(x => zeile(x, true)).join('')}</div>` : ''}</div>`);
      } else html.push(zeile(b));
    }
    box.innerHTML = `<div class="bu-liste">${html.join('')}</div>`;
    box.querySelectorAll('[data-serie-auf]').forEach(k => k.onclick = () => { const s = k.dataset.serieAuf; if (offenSerien.has(s)) offenSerien.delete(s); else offenSerien.add(s); zeichnen(); });
    box.querySelectorAll('[data-storno], [data-serie-storno]').forEach(k => k.onclick = async () => {
      const b = liste.find(x => String(x.id) === (k.dataset.storno ?? k.dataset.serieStorno));
      const n = k.dataset.serieStorno ? await serieStornieren(b) : await stornoDialog(b, b.serie ? serien.get(b.serie) : 0);
      if (n) kontext.navigieren(ziel(), true);
    });
  };
  const serieStornieren = async b => {
    if (!await bestaetigen('Serie stornieren', `Alle kommenden Termine der Serie „${b.titel || b.artName}“ stornieren?`, 'Stornieren')) return 0;
    try { const r = await api(`/api/app/buchen/${b.id}?serie=true`, { methode: 'DELETE' }); toast(`${r.storniert} Termine storniert.`); return r.storniert || 1; }
    catch (err) { kontext.fehler(err); return 0; }
  };
  seite.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => {
    filter.ansicht = b.dataset.ansicht;
    seite.querySelectorAll('[data-ansicht]').forEach(x => { x.classList.toggle('an', x === b); x.setAttribute('aria-checked', String(x === b)); });
    zeichnen();
  });
  suchfeldMitFilterBinden(seite.querySelector('.bu-leiste'), {
    beiSuche: t => { filter.suche = t; zeichnen(); },
    beiFilter: (n, w) => { if (n === 'art') { filter.art = w; zeichnen(); } },
  });
  seite.querySelectorAll('[data-von], [data-bis]').forEach(f => f.onchange = () => kontext.navigieren(ziel(seite.querySelector('[data-von]').value || von, seite.querySelector('[data-bis]').value || bis)));
  seite.querySelector('[data-alle]')?.addEventListener('change', ev => kontext.navigieren(ziel(von, bis, ev.target.checked)));
  seite.querySelector('[data-csv]').onclick = () => herunterladen(`buchungen-${von}-${bis}.csv`, csv([
    ['Datum', 'Wochentag', 'Stunde von', 'Stunde bis', 'Beginn', 'Ende', 'Art', 'Raum', 'Ressourcen', 'Klassen', 'Lehrkraft', 'Fach', 'Titel', 'Bemerkung', 'Status', 'Serie', 'Gebucht von', 'Erstellt'],
    ...sichtbar().map(b => [datumDe(b.datum), b.wochentag, b.stundeVon, b.stundeBis, b.beginn, b.ende, b.artName, b.raum, b.ressourcen.map(r => `${r.menge > 1 ? `${r.menge}x ` : ''}${r.name}`).join(', '),
      b.klassen.join(', '), b.lehrer, b.fach, b.titel, b.text, b.status, b.serie, b.absender, b.erstellt])]));
  zeichnen();
}
/** Filter von „Meine Buchungen“ (bleibt während der Sitzung erhalten). */
const meineFilter = { ansicht: 'kommend', art: '', suche: '' };
function passtAnsicht(b, a, heute) {
  if (a === 'kommend') return b.status === 'aktiv' && b.datum >= heute;
  if (a === 'vergangen') return b.status === 'aktiv' && b.datum < heute;
  if (a === 'storniert') return b.status === 'storniert';
  return true;
}

// ------------------------------------------------------------------ Raumübersicht (Raumbelegung)

const BELEGT_TEXT = { unterricht: 'Unterricht', klausur: 'Prüfung', termin: 'Termin', buchung: 'Buchung', vorgemerkt: 'vorgemerkt', gesperrt: 'gesperrt' };
const ZUSTAND_TEXT = { ...BELEGT_TEXT, frei: 'frei', vergangen: 'vergangen', tagfrei: 'unterrichtsfrei' };
const ZOOM = [['kompakt', 'Kompakt', { symbol: 'zoom-out', titel: 'Kompakt: viele Räume auf einen Blick' }], ['normal', 'Normal', { titel: 'Normal' }],
  ['gross', 'Groß', { symbol: 'zoom-in', titel: 'Groß: mehr Text in den Zellen' }]];
/** Raumfilter, die der Server auswertet (die übrigen – Suche, Verfügbarkeit – filtern im Browser). */
const RAUM_SERVERFILTER = ['raumart', 'gebaeude', 'abteilung', 'raumgruppe', 'ressourcenarten', 'kapazitaet'];

/** Ansichtseinstellungen im Browser merken (Zoom, Karten/Liste) – ohne Speicher (privates Fenster) gilt die Vorgabe. */
const merken = (schluessel, wert) => { try { localStorage.setItem(`untplan:buchen:${schluessel}`, wert); } catch { /* ohne Speicher */ } };
const gemerkt = (schluessel, vorgabe) => { try { return localStorage.getItem(`untplan:buchen:${schluessel}`) || vorgabe; } catch { return vorgabe; } };
const SCHMAL = '(max-width: 767.98px)';

/** Hover-Details der Übersichten (Belegungsraster, Zeitstrahl): eine Karte am Element, auch per Tastatur (Fokus) und auf Touch (Antippen). */
const buHover = (() => {
  let el = null;
  const weg = () => { if (el) el.style.display = 'none'; };
  const zeigen = (anker, html) => {
    if (!el) { el = document.createElement('div'); el.className = 'bu-hover'; el.setAttribute('role', 'tooltip'); document.body.append(el); }
    el.innerHTML = html;
    el.style.display = 'block';
    const r = anker.getBoundingClientRect(), b = el.offsetWidth, h = el.offsetHeight;
    el.style.left = `${Math.round(Math.max(8, Math.min(window.innerWidth - b - 8, r.left + r.width / 2 - b / 2)))}px`;
    el.style.top = `${Math.round(r.top - h - 8 < 8 ? r.bottom + 8 : r.top - h - 8)}px`;
  };
  if (typeof window !== 'undefined') { document.addEventListener('scroll', weg, true); window.addEventListener('hashchange', weg); }
  return { zeigen, weg };
})();
/** Hover-Details für alle [data-info] in wurzel; inhalt(element) liefert HTML (oder nichts). */
function hoverVerbinden(wurzel, inhalt) {
  const zeigen = ev => {
    const z = ev.target.closest?.('[data-info]');
    if (!z || !wurzel.contains(z)) { buHover.weg(); return; }
    const html = inhalt(z);
    if (html) buHover.zeigen(z, html); else buHover.weg();
  };
  if (window.matchMedia?.('(hover: hover)').matches) wurzel.addEventListener('mouseover', zeigen);
  wurzel.addEventListener('mouseleave', buHover.weg);
  wurzel.addEventListener('focusin', zeigen);
  wurzel.addEventListener('focusout', buHover.weg);
  // Antippen einer belegten Stelle (ohne eigene Aktion) zeigt die Details
  wurzel.addEventListener('click', ev => { const z = ev.target.closest('[data-info]'); if (z && !z.closest('[data-buchen], [data-buchung], [data-res-buchen]')) zeigen(ev); else buHover.weg(); });
}

async function raumuebersicht(seite, kontext, [datumParam, tageParam]) {
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const [opt, el] = await Promise.all([holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const heute = heuteIso();
  const datum = iso.test(datumParam ?? '') ? datumParam : (opt.naechsterTag && opt.naechsterTag > heute ? opt.naechsterTag : heute);
  const woche = tageParam === 'woche';
  const start = woche ? montag(datum) : datum;
  const k = raumFilter;
  const q = new URLSearchParams({ datum: start, tage: woche ? String(opt.tageProWoche) : '1' });
  for (const n of RAUM_SERVERFILTER) if (k[n]) q.set(n, k[n]);
  const ziel = (d = datum, w = woche) => `#/buchen/raeume/${d}${w ? '/woche' : ''}`;
  kontext.titel('Raumübersicht', `<a class="nl-knopf" href="#/buchen/meine"><i class="bi bi-list-ul"></i>Meine Buchungen</a>${opt.raumBuchen === false ? '' : '<a class="nl-hinzu" href="#/buchen/raum"><i class="bi bi-plus-lg"></i>Raum buchen</a>'}`);
  let zoom = gemerkt('zoom', 'normal');
  const pille = (html, aktiv = false, klasse = '') => `<label class="nl-pille ${aktiv ? 'aktiv' : ''} ${klasse}">${html}</label>`;
  const sel = (n, titel, liste) => liste.length ? pille(`<select data-f="${n}" aria-label="${titel}">${optionen(liste, k[n], `${titel}: alle`)}</select>`, !!k[n]) : '';
  const filterAktiv = () => RAUM_SERVERFILTER.some(n => k[n]) || !!k.frei || !!k.suche;
  seite.innerHTML = `
    <div class="u-sf-leiste bu-ru-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Raum suchen', wert: k.suche, attr: 'data-raumsuche', label: 'Raum suchen', klasse: 'bu-raumsuche', filter: [
        ...(opt.gebaeude.length ? [{ name: 'gebaeude', label: 'Gebäude', optionen: opt.gebaeude.map(x => [x, x]), wert: k.gebaeude, alle: 'Alle Gebäude' }] : []),
        ...(opt.raumarten.length ? [{ name: 'raumart', label: 'Raumart', optionen: opt.raumarten.map(x => [x, x]), wert: k.raumart, alle: 'Alle Raumarten' }] : []),
        { name: 'frei', label: 'Verfügbarkeit', alle: 'Alle Räume', wert: woche && /^\d+$/.test(k.frei) ? 'irgend' : k.frei,
          optionen: [['irgend', 'Mit freien Stunden'], ...(woche ? [] : opt.zeitraster.filter(z => z.nr > 0).map(z => [String(z.nr), `Frei in der ${z.nr}. Std.`]))] }] })}
      <div class="u-sf-rechts">
        <div class="bu-datumwahl"><button type="button" class="nl-knopf rund" data-schieben="-1" aria-label="${woche ? 'Woche' : 'Tag'} zurück"><i class="bi bi-chevron-left"></i></button>
          ${pille(`<input type="date" data-datum value="${datum}" aria-label="Datum">`)}<button type="button" class="nl-knopf rund" data-schieben="1" aria-label="${woche ? 'Woche' : 'Tag'} weiter"><i class="bi bi-chevron-right"></i></button>
          <button type="button" class="nl-knopf" data-heute>Heute</button></div>
        ${segment([['tag', 'Tag', { symbol: 'calendar-day' }], ['woche', 'Woche', { symbol: 'calendar-week' }]], woche ? 'woche' : 'tag', { attr: 'data-ansicht', label: 'Zeitraum' })}
      </div>
      <button type="button" class="nl-knopf bu-filter-auf ${filterAktiv() ? 'aktiv' : ''}" data-filter-auf aria-expanded="false"><i class="bi bi-funnel"></i>Filter${filterAktiv() ? `<span class="bu-anzahl">${RAUM_SERVERFILTER.filter(n => k[n]).length + (k.frei ? 1 : 0) || ''}</span>` : ''}</button>
    </div>
    <div class="nl-leiste bu-filterleiste bu-ru-filter">
      ${sel('abteilung', 'Abteilung', opt.abteilungen)}${sel('raumgruppe', 'Raumgruppe', opt.raumgruppen)}
      ${opt.ressourcenarten.length ? pille(`<select data-f="ressourcenarten" aria-label="Ausstattung">${optionen(opt.ressourcenarten.map(a => [String(a.id), a.name]), k.ressourcenarten, 'Ausstattung: alle')}</select>`, !!k.ressourcenarten) : ''}
      ${pille(`<select data-f="kapazitaet" aria-label="Kapazität">${optionen([10, 15, 20, 25, 30, 40, 60].map(x => [String(x), `ab ${x} Plätzen`]), k.kapazitaet, 'Kapazität: alle')}</select>`, !!k.kapazitaet)}
      <button type="button" class="nl-knopf klein bu-filter-weg" data-filter-weg ${filterAktiv() ? '' : 'hidden'}><i class="bi bi-x-lg"></i>Filter zurücksetzen</button>
    </div>
    <div class="bu-ru-kopf"><div class="bu-ru-zahlen" data-zahlen aria-live="polite"></div>
      <div class="bu-legende" aria-label="Legende">${['unterricht', 'buchung', 'klausur', 'termin', 'vorgemerkt', 'gesperrt'].map(a => `<span><i class="bu-punkt ${a}"></i>${BELEGT_TEXT[a]}</span>`).join('')}<span><i class="bu-punkt frei"></i>frei – anklicken zum Buchen</span></div>
      <span class="bu-ru-zoom">${segment(ZOOM, zoom, { attr: 'data-zoom', label: 'Darstellung', klasse: 'klein' })}</span></div>
    <div data-raster><div class="u-laedt"></div></div>`;
  seite.querySelectorAll('[data-schieben]').forEach(b => b.onclick = () => kontext.navigieren(ziel(plusTage(datum, Number(b.dataset.schieben) * (woche ? 7 : 1)))));
  seite.querySelector('[data-heute]').onclick = () => kontext.navigieren(ziel(heuteIso()));
  seite.querySelector('[data-datum]').onchange = ev => ev.target.value && kontext.navigieren(ziel(ev.target.value));
  seite.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => kontext.navigieren(ziel(datum, b.dataset.wert === 'woche')));
  seite.querySelectorAll('[data-f]').forEach(f => f.onchange = () => { raumFilter[f.dataset.f] = f.value; kontext.navigieren(ziel(), true); });
  seite.querySelector('[data-filter-auf]').onclick = ev => {
    const auf = seite.querySelector('.bu-ru-filter').classList.toggle('offen');
    ev.currentTarget.setAttribute('aria-expanded', String(auf));
  };
  seite.querySelector('[data-filter-weg]').onclick = () => { Object.keys(raumFilter).forEach(x => { raumFilter[x] = ''; }); kontext.navigieren(ziel(), true); };

  const u = await holen(`/api/app/buchen/raumuebersicht?${q}`);
  const raster = u.zeitraster.filter(z => z.nr > 0);
  const tage = u.tage;
  const box = seite.querySelector('[data-raster]');
  if (!box) return;
  // aktuelle Stunde (heute) bzw. heutiger Tag (Woche) hervorheben
  const jetzt = jetztMinuten();
  const jetztStd = raster.find(z => minuten(z.beginn) <= jetzt && jetzt < minuten(z.ende))?.nr ?? null;
  const istJetzt = (t, z) => t.datum === heute && z.nr === jetztStd;

  // „Verfügbarkeit“: alle Räume, nur mit freien Stunden bzw. (Tag) frei in einer bestimmten Stunde
  if (woche && /^\d+$/.test(k.frei)) k.frei = 'irgend';

  /** Zustand einer Stunde: Belegung (Art, Text, Buchung), frei, vergangen bzw. unterrichtsfrei. */
  const slot = (r, t, z) => {
    if (t.frei) return { art: 'tagfrei', text: t.grund || 'unterrichtsfrei' };
    const b = (r.belegung[t.datum] ?? []).find(x => x.stunde === z.nr);
    if (b) return { art: b.art, text: b.text, buchung: b.buchung ?? null };
    return { art: t.datum < heute ? 'vergangen' : 'frei' };
  };
  /** Zusammenhängende gleiche Belegung eines Tages als Block (Projektarbeit 3.–4.); freie Stunden bleiben einzeln anklickbar. */
  const laeufe = (r, t) => {
    const liste = [];
    for (const z of raster) {
      const s = slot(r, t, z);
      const letzter = liste[liste.length - 1];
      if (letzter && s.art !== 'frei' && letzter.art === s.art && letzter.text === s.text && letzter.buchung === s.buchung) { letzter.bis = z.nr; letzter.n++; continue; }
      liste.push({ ...s, von: z.nr, bis: z.nr, n: 1, datum: t.datum, tag: t });
    }
    return liste;
  };
  const daten = u.raeume.map(r => {
    const tl = tage.map(t => laeufe(r, t));
    const frei = tl.flat().filter(x => x.art === 'frei').length;
    const offen = tl.flat().filter(x => x.art !== 'tagfrei' && x.art !== 'vergangen').reduce((s, x) => s + x.n, 0);
    return { r, tl, frei, offen, jetztFrei: tage.some(t => t.datum === heute && !t.frei && jetztStd && slot(r, t, { nr: jetztStd }).art === 'frei') };
  });
  const passt = d => {
    const such = (k.suche || '').toLowerCase();
    if (such && !`${d.r.kurz} ${d.r.name} ${d.r.raumart} ${d.r.gebaeude} ${d.r.abteilung}`.toLowerCase().includes(such)) return false;
    if (k.frei === 'irgend') return d.frei > 0;
    if (/^\d+$/.test(k.frei)) return slot(d.r, tage[0], { nr: Number(k.frei) }).art === 'frei';
    return true;
  };
  const infos = [];
  const info = x => { infos.push(x); return infos.length - 1; };
  const zeitVon = nr => raster.find(z => z.nr === nr);
  const infoHtml = i => {
    const x = infos[i];
    if (!x) return '';
    const a = x.art;
    const zeit = a === 'tagfrei' ? '' : ` · ${stdText(x.von, x.bis)} · ${e(zeitVon(x.von)?.beginn ?? '')}–${e(zeitVon(x.bis)?.ende ?? '')} Uhr`;
    const tipp = a === 'frei' ? 'Anklicken, um den Raum zu buchen' : x.buchung && x.datum >= heute ? 'Anklicken, um die Buchung zu stornieren' : '';
    const r = x.raum;
    return `<div class="bu-hover-kopf"><i class="bu-punkt ${e(a)}"></i><b>${e(ZUSTAND_TEXT[a] ?? a)}</b><span class="ms-auto">${e(r.kurz)}</span></div>
      <div class="bu-hover-zeit">${e(tagKurz(x.datum))}${zeit}</div>
      ${x.text ? `<div class="bu-hover-text">${e(x.text)}</div>` : ''}
      <div class="bu-hover-raum">${e([r.name !== r.kurz ? r.name : '', r.raumart, r.kapazitaet ? `${r.kapazitaet} Plätze` : '', r.gebaeude].filter(Boolean).join(' · '))}</div>
      ${tipp ? `<div class="bu-hover-tipp"><i class="bi bi-hand-index"></i>${tipp}</div>` : ''}`;
  };
  const freiZahl = d => `<span class="bu-frei-zahl ${d.frei === 0 ? 'null' : d.frei < d.offen / 3 ? 'wenig' : ''}" title="${d.frei} von ${d.offen} Stunden frei">${d.frei} frei</span>`;
  const raumMeta = r => [r.name !== r.kurz ? r.name : '', r.kapazitaet ? `${r.kapazitaet} Pl.` : '', r.raumart].filter(Boolean).join(' · ');

  /** Zelle (Tabelle) bzw. Chip (Liste) einer Belegung. */
  const zelle = (d, b, start) => {
    const r = d.r;
    const i = info({ ...b, raum: r });
    const s = start && woche ? ' tagstart' : '';
    const jetztK = b.n === 1 && istJetzt(b.tag, { nr: b.von }) ? ' jetzt' : '';
    if (b.art === 'frei') return `<td class="bu-zelle frei${s}${jetztK}" data-info="${i}" data-hover=""><button type="button" data-buchen="${e(r.kurz)}|${b.datum}|${b.von}" aria-label="${e(r.kurz)} am ${e(tagKurz(b.datum))}, ${b.von}. Stunde buchen"><i class="bi bi-plus-lg"></i></button></td>`;
    const span = b.n > 1 ? ` colspan="${b.n}"` : '';
    const text = woche || b.art === 'vergangen' ? '' : `<span class="bu-zt">${e(b.text ?? '')}</span>`;
    if (b.buchung && b.datum >= heute)
      return `<td class="bu-zelle ${e(b.art)}${s}"${span} data-info="${i}" data-hover=""><button type="button" class="bu-block bu-belegt-knopf" data-buchung="${b.buchung}|${e(r.kurz)}|${b.datum}|${b.von}|${b.bis}" aria-label="${e(BELEGT_TEXT[b.art] ?? b.art)}: ${e(b.text)} – stornieren">${text}</button></td>`;
    return `<td class="bu-zelle ${e(b.art)}${s}"${span} data-info="${i}" data-hover="">${b.art === 'vergangen' ? '' : `<div class="bu-block">${text}</div>`}</td>`;
  };
  const chip = (d, b) => {
    const r = d.r;
    const i = info({ ...b, raum: r });
    const nr = b.von === b.bis ? `${b.von}` : `${b.von}–${b.bis}`;
    const breit = `style="--n:${b.n}"`;
    if (b.art === 'frei') return `<button type="button" class="bu-chip frei" ${breit} data-info="${i}" data-buchen="${e(r.kurz)}|${b.datum}|${b.von}" aria-label="${e(r.kurz)} ${b.von}. Stunde buchen">${nr}</button>`;
    if (b.buchung && b.datum >= heute) return `<button type="button" class="bu-chip ${e(b.art)}" ${breit} data-info="${i}" data-buchung="${b.buchung}|${e(r.kurz)}|${b.datum}|${b.von}|${b.bis}" aria-label="${e(b.text)} – stornieren">${nr}</button>`;
    return `<button type="button" class="bu-chip ${e(b.art)}" ${breit} data-info="${i}" aria-label="${e(ZUSTAND_TEXT[b.art] ?? b.art)} ${nr}. Stunde">${b.art === 'tagfrei' ? e(b.text) : nr}</button>`;
  };

  const tabelle = (liste, gruppieren) => {
    const n = tage.length * raster.length;
    const kopf = woche
      ? `<tr class="bu-kopf1"><th class="bu-raumkopf" rowspan="2" scope="col">Raum<small>${liste.length} ${liste.length === 1 ? 'Raum' : 'Räume'}</small></th>${tage.map(t => `<th colspan="${raster.length}" class="bu-tagkopf ${t.frei ? 'frei' : ''} ${t.datum === heute ? 'heute' : ''}" scope="colgroup"><a href="${ziel(t.datum, false)}" title="Tagesansicht">${e(tagKurz(t.datum))}</a></th>`).join('')}</tr>
         <tr class="bu-kopf2">${tage.map(t => raster.map((z, j) => `<th class="bu-stdkopf ${j === 0 ? 'tagstart' : ''} ${istJetzt(t, z) ? 'jetzt' : ''}" title="${e(z.beginn)}–${e(z.ende)}" scope="col">${z.nr}</th>`).join('')).join('')}</tr>`
      : `<tr class="bu-kopf1"><th class="bu-raumkopf" scope="col">Raum<small>${liste.length} ${liste.length === 1 ? 'Raum' : 'Räume'}</small></th>${raster.map(z => `<th class="bu-stdkopf ${istJetzt(tage[0], z) ? 'jetzt' : ''} ${String(z.nr) === k.frei ? 'gewaehlt' : ''}" scope="col"><b>${z.nr}.</b><small>${e(z.beginn)}–${e(z.ende)}</small></th>`).join('')}</tr>`;
    let gruppe = null;
    const zeilen = liste.map(d => {
      const g = d.r.gebaeude || 'Ohne Gebäude';
      const kopfzeile = gruppieren && g !== gruppe ? `<tr class="bu-gruppe"><th colspan="${n + 1}" scope="rowgroup"><span><i class="bi bi-building"></i>${e(g)}<small>${liste.filter(x => (x.r.gebaeude || 'Ohne Gebäude') === g).length} Räume</small></span></th></tr>` : '';
      gruppe = g;
      return `${kopfzeile}<tr class="${d.frei ? '' : 'voll'}"><th class="bu-raumname" scope="row"><div class="bu-rn"><b>${e(d.r.kurz)}</b>${freiZahl(d)}</div><small>${e(raumMeta(d.r))}</small></th>
        ${d.tl.map(l => l.map((b, j) => zelle(d, b, b.von === raster[0]?.nr)).join('')).join('')}</tr>`;
    }).join('');
    return `<div class="bu-uebersicht ${woche ? 'woche' : 'tag'} zoom-${zoom}" style="--bu-n:${n}" tabindex="0" aria-label="Belegungsraster"><table>
      <colgroup><col class="bu-col-raum">${tage.map(() => raster.map(() => '<col>').join('')).join('')}</colgroup>
      <thead>${kopf}</thead><tbody>${zeilen}</tbody></table></div>`;
  };
  const liste = (liste, gruppieren) => {
    let gruppe = null;
    return `<div class="bu-ru-liste ${woche ? 'woche' : 'tag'}">${liste.map(d => {
      const g = d.r.gebaeude || 'Ohne Gebäude';
      const kopf = gruppieren && g !== gruppe ? `<div class="bu-ru-gruppe"><i class="bi bi-building"></i>${e(g)}</div>` : '';
      gruppe = g;
      return `${kopf}<article class="bu-ru-raum"><header><div><b>${e(d.r.kurz)}</b><small>${e(raumMeta(d.r))}</small></div>${freiZahl(d)}</header>
        ${tage.map((t, i) => `<div class="bu-ru-tag ${t.datum === heute ? 'heute' : ''}">${woche ? `<span class="bu-ru-tagname">${e(tagKurz(t.datum))}</span>` : ''}<div class="bu-ru-chips">${d.tl[i].map(b => chip(d, b)).join('')}</div></div>`).join('')}
        ${woche ? '' : (() => {
          // Tag: belegte Blöcke mit Text darunter (auf dem Telefon gibt es kein Überfahren)
          const belegt = d.tl[0].filter(b => !['frei', 'vergangen', 'tagfrei'].includes(b.art));
          return belegt.length ? `<ul class="bu-ru-belegt">${belegt.map(b => `<li><i class="bu-punkt ${e(b.art)}"></i><b>${stdText(b.von, b.bis)}</b><span>${e(b.text ?? '')}</span></li>`).join('')}</ul>` : '';
        })()}
      </article>`;
    }).join('')}</div>`;
  };

  const zeichnen = () => {
    if (!box.isConnected) return;
    infos.length = 0;
    const sichtbar = daten.filter(passt);
    const gebaeude = new Set(sichtbar.map(d => d.r.gebaeude || ''));
    const gruppieren = gebaeude.size > 1 && !k.gebaeude;
    if (gruppieren) sichtbar.sort((a, b) => (a.r.gebaeude || '￿').localeCompare(b.r.gebaeude || '￿', 'de'));
    const jetztFrei = sichtbar.filter(d => d.jetztFrei).length;
    seite.querySelector('[data-zahlen]').innerHTML = `<b>${sichtbar.length}</b> ${sichtbar.length === 1 ? 'Raum' : 'Räume'}${sichtbar.length < u.raeume.length ? ` von ${u.raeume.length}` : ''}
      ${tage.some(t => t.datum === heute) && jetztStd ? `<span class="bu-ru-jetzt"><i class="bi bi-clock"></i>${jetztFrei} jetzt frei (${jetztStd}. Std.)</span>` : ''}
      ${!woche && tage[0].frei ? `<span class="nl-chip grau">${e(tage[0].grund || 'unterrichtsfrei')}</span>` : ''}`;
    seite.querySelector('[data-filter-weg]').hidden = !filterAktiv();
    box.innerHTML = sichtbar.length ? (window.matchMedia(SCHMAL).matches ? liste(sichtbar, gruppieren) : tabelle(sichtbar, gruppieren))
      : `<div class="bu-leer"><i class="bi bi-door-closed" aria-hidden="true"></i><b>${u.raeume.length ? 'Kein Raum passt zu den Filtern.' : 'Kein buchbarer Raum passt zu den Filtern.'}</b>
        ${filterAktiv() ? '<button type="button" class="nl-knopf mt-2" data-filter-weg2>Filter zurücksetzen</button>' : ''}</div>`;
    box.querySelector('[data-filter-weg2]')?.addEventListener('click', () => seite.querySelector('[data-filter-weg]').click());
    // aktuelle Stunde in den sichtbaren Bereich holen
    const jetztKopf = box.querySelector('.bu-stdkopf.jetzt');
    const rahmen = box.querySelector('.bu-uebersicht');
    if (jetztKopf && rahmen && rahmen.scrollWidth > rahmen.clientWidth) rahmen.scrollLeft = Math.max(0, jetztKopf.offsetLeft - rahmen.clientWidth / 3);
  };
  box.addEventListener('click', ev => {
    const frei = ev.target.closest('[data-buchen]');
    if (frei) {
      const [raum, d, st] = frei.dataset.buchen.split('|');
      schnellbuchung(kontext, opt, el.klassen, u.raeume.find(x => x.kurz === raum), d, Number(st), raster, (r2, d2) => r2.belegung[d2] ?? []).then(ok => { if (ok) kontext.navigieren(ziel(), true); });
      return;
    }
    const gebucht = ev.target.closest('[data-buchung]');
    if (gebucht) {
      const [id, raum, d, von, bis] = gebucht.dataset.buchung.split('|');
      const eintrag = (u.raeume.find(x => x.kurz === raum)?.belegung[d] ?? []).find(x => x.stunde === Number(von));
      stornoDialog({ id: Number(id), titel: eintrag?.text ?? 'Buchung', datum: d, stundeVon: Number(von), stundeBis: Number(bis || von), raum }).then(n => { if (n) kontext.navigieren(ziel(), true); });
    }
  });
  hoverVerbinden(box, z => infoHtml(Number(z.dataset.info)));
  suchfeldMitFilterBinden(seite.querySelector('.bu-ru-leiste'), {
    beiSuche: t => { raumFilter.suche = t; zeichnen(); },
    beiFilter: (n, w) => { raumFilter[n] = w; if (n === 'frei') zeichnen(); else kontext.navigieren(ziel(), true); },
  });
  seite.querySelectorAll('[data-zoom]').forEach(b => b.onclick = () => {
    zoom = b.dataset.wert; merken('zoom', zoom);
    segmentSetzen(b.closest('.u-segment'), zoom);
    const t = box.querySelector('.bu-uebersicht');
    if (t) t.className = t.className.replace(/zoom-\w+/, `zoom-${zoom}`);
  });
  // Telefon ↔ Desktop: Liste je Raum bzw. Raster
  const mq = window.matchMedia(SCHMAL);
  const wechsel = () => { if (!box.isConnected) { mq.removeEventListener('change', wechsel); return; } zeichnen(); };
  mq.addEventListener('change', wechsel);
  zeichnen();
}
/** Filter der Raumübersicht (bleibt während der Sitzung erhalten). */
const raumFilter = { raumart: '', gebaeude: '', abteilung: '', raumgruppe: '', ressourcenarten: '', kapazitaet: '', frei: '', suche: '' };

/**
 * Schnellbuchung aus der Raumübersicht: Raum und Stunde sind vorbelegt, bis-Stunde wählbar (nur durchgehend freie Stunden), Titel,
 * Klassen, Bemerkung – „Buchen“ speichert direkt; „Serie / weitere Optionen“ öffnet den Assistenten mit derselben Vorbelegung.
 */
function schnellbuchung(kontext, opt, klassen, raum, datum, stunde, raster, belegungVon) {
  return new Promise(fertig => {
    let ok = false;
    const belegt = new Set(belegungVon(raum, datum).map(x => x.stunde));
    // bis-Stunde: ab der gewählten Stunde, solange der Raum frei ist
    const bisListe = [];
    for (const z of raster.filter(x => x.nr >= stunde)) { if (belegt.has(z.nr)) break; bisListe.push(z); }
    const box = kbDialog({
      titel: `${raum.kurz} buchen`, breite: 'm',
      inhalt: `<div class="bu-schnell-kopf"><span class="mf-symbol bu-symbol"><i class="bi bi-door-open"></i></span><div><b>${e(raum.kurz)}${raum.name && raum.name !== raum.kurz ? ` · ${e(raum.name)}` : ''}</b>
          <span>${e(tagLang(datum))}${raum.kapazitaet ? ` · ${raum.kapazitaet} Plätze` : ''}</span></div></div>
        <div class="bu-raster zwei">
          ${feld('Von Stunde', `<select class="form-select" name="von" disabled>${stundenOptionen(raster.filter(x => x.nr === stunde), stunde)}</select>`)}
          ${feld('Bis Stunde', `<select class="form-select" name="bis">${stundenOptionen(bisListe, stunde)}</select>`)}
          ${feld('Titel / Anlass', '<input class="form-control" name="titel" maxlength="200" placeholder="z. B. Projektarbeit">', 'breit')}
          ${feld('Klasse(n)', `<select class="form-select" name="klassen" multiple data-platzhalter="keine">${klassen.map(x => `<option value="${e(x.kurz)}" ${x.name && x.name !== x.kurz ? `data-zweit="${e(x.name)}"` : ''}>${e(x.kurz)}</option>`).join('')}</select>`, 'breit')}
          ${feld('Bemerkung', '<input class="form-control" name="text" maxlength="1000" placeholder="optional">', 'breit')}
        </div>`,
      fuss: `<a class="nl-knopf kbd-links" data-erweitert href="#/buchen/raum/${datum}/${stunde}/${stunde}/${encodeURIComponent(raum.kurz)}"><i class="bi bi-arrow-repeat"></i>Serie / weitere Optionen</a>
        <button type="button" class="nl-knopf" data-kbd-zu>Abbrechen</button><button type="button" class="nl-knopf nw-primaer" data-schnell-buchen><i class="bi bi-check-lg"></i>Buchen</button>`,
    });
    const w = n => box.querySelector(`[name=${n}]`);
    box.querySelector('[data-erweitert]').onclick = ev => { ev.preventDefault(); box.schliessen(); kontext.navigieren(`#/buchen/raum/${datum}/${stunde}/${w('bis').value}/${encodeURIComponent(raum.kurz)}`); };
    box.querySelector('[data-kbd-zu]').onclick = () => box.schliessen();
    box.querySelector('[data-schnell-buchen]').onclick = async ev => {
      const knopf = ev.currentTarget;
      knopf.disabled = true;
      try {
        await senden('/api/app/buchen', {
          art: 'raum', termine: [{ datum, stundeVon: stunde, stundeBis: Number(w('bis').value) || stunde, raum: raum.kurz }], ressourcen: [],
          klassen: [...w('klassen').selectedOptions].map(o => o.value), lehrer: opt.ich || '', fach: '', titel: w('titel').value, text: w('text').value,
          benachrichtigen: false, unterrichtId: null, beginn: null, ende: null,
        });
        toast(`${raum.kurz} gebucht.`);
        ok = true;
        box.schliessen();
      } catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
    box.geschlossen.then(() => fertig(ok));
    setTimeout(() => w('titel').focus(), 50);
  });
}

// ------------------------------------------------------------------ Ressourcenübersicht (Karten bzw. Liste mit Zeitstrahl)

const RES_SYMBOLE = [[/beamer|projektor/i, 'projector'], [/laptop|notebook/i, 'laptop'], [/ipad|tablet/i, 'tablet'], [/dokumentenkamera|visualizer/i, 'camera-video'],
  [/kamera/i, 'camera'], [/lautsprecher|audio|musikanlage/i, 'speaker'], [/mikro/i, 'mic'], [/kopfh/i, 'headphones'], [/\bvr\b|vr-/i, 'badge-vr'],
  [/tafel|display|monitor|bildschirm|fernseh/i, 'display'], [/drucker/i, 'printer'], [/roboter|lego/i, 'robot'], [/internet|wlan/i, 'wifi']];
/** Symbol einer Ressource: das ihrer Art, sonst aus Art und Name abgeleitet (Beamer → Projektor …). */
export const ressourcenSymbol = r => r.symbol || RES_SYMBOLE.find(([m]) => m.test(`${r.art ?? ''} ${r.name ?? ''}`))?.[1] || 'box-seam';
const RES_TOENE = ['orange', 'blau', 'lila', 'gruen', 'tuerkis', 'rot', 'gelb'];
const resTon = r => RES_TOENE[(r.artId ?? 0) % RES_TOENE.length];

/** Filter der Ressourcenübersicht (bleibt während der Sitzung erhalten). */
const resFilter = { arten: new Set(), suche: '', ort: '', nurFrei: false, von: 0, bis: 0 };

async function ressourcenuebersicht(seite, kontext, [datumParam, zweiter]) {
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const f = resFilter;
  const opt = await holen('/api/app/buchen/optionen');
  const heute = heuteIso();
  const datum = iso.test(datumParam ?? '') ? datumParam : (opt.naechsterTag && opt.naechsterTag > heute ? opt.naechsterTag : heute);
  const woche = zweiter === 'woche';
  if (zweiter && /^[\d,]+$/.test(zweiter)) { f.arten = new Set(zweiter.split(',').map(Number).filter(Boolean)); }   // ältere Links: Arten
  const raster = opt.zeitraster.filter(z => z.nr > 0);
  if (!raster.some(z => z.nr === f.von) || !raster.some(z => z.nr === f.bis) || f.bis < f.von) { f.von = raster[0]?.nr ?? 1; f.bis = raster[raster.length - 1]?.nr ?? f.von; }
  let ansicht = gemerkt('ressourcen', 'karten');
  const ziel = (d = datum, w = woche) => `#/buchen/ressourcen/${d}${w ? '/woche' : ''}`;
  kontext.titel('Ressourcenübersicht', `${opt.admin ? '<a class="nl-knopf" href="#/buchen/verwaltung"><i class="bi bi-gear"></i>Verwalten</a>' : ''}${opt.ressourcenBuchen === false ? '' : '<a class="nl-hinzu" href="#/buchen/ressource"><i class="bi bi-plus-lg"></i>Ressource buchen</a>'}`);
  const u = await holen(`/api/app/buchen/ressourcenuebersicht?datum=${datum}${woche ? '' : '&tage=1'}`);
  const tag = u.tage.find(t => t.datum === datum) ?? u.tage[0];
  const zeitVon = nr => raster.find(z => z.nr === nr);
  const ganzerTag = () => f.von === raster[0]?.nr && f.bis === raster[raster.length - 1]?.nr;
  const zrText = () => ganzerTag() ? 'ganzer Tag' : `${stdText(f.von, f.bis)} (${zeitVon(f.von)?.beginn ?? ''}–${zeitVon(f.bis)?.ende ?? ''})`;
  const stdWahl = (n, wert) => `<select data-zr="${n}" aria-label="${n === 'von' ? 'Von Stunde' : 'Bis Stunde'}">${raster.map(z => `<option value="${z.nr}" ${z.nr === wert ? 'selected' : ''}>${z.nr}.${n === 'von' ? ` ${e(z.beginn)}` : ` – ${e(z.ende)}`}</option>`).join('')}</select>`;

  // Arten (mit Symbol und Anzahl) für die Filter-Chips
  const arten = [...new Map(u.ressourcen.map(r => [r.artId ?? 0, { id: r.artId ?? 0, name: r.art || 'Ohne Art', symbol: ressourcenSymbol(r), ton: resTon(r) }])).values()]
    .sort((a, b) => a.name.localeCompare(b.name, 'de'));
  for (const a of [...f.arten]) if (!arten.some(x => x.id === a)) f.arten.delete(a);

  seite.innerHTML = `
    <div class="u-sf-leiste bu-ru-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Ressource oder Standort suchen', wert: f.suche, attr: 'data-res-suche', label: 'Ressourcen durchsuchen', klasse: 'bu-res-suche',
        filter: [{ name: 'ort', label: 'Standort', alle: 'Alle Standorte', wert: f.ort, optionen: [...new Set(u.ressourcen.map(r => r.mobil ? r.standort : `fest in ${r.raum}`).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de')).map(x => [x, x]) }] })}
      <div class="u-sf-rechts">
        <div class="bu-datumwahl"><button type="button" class="nl-knopf rund" data-schieben="${woche ? -7 : -1}" aria-label="${woche ? 'Woche' : 'Tag'} zurück"><i class="bi bi-chevron-left"></i></button>
          <label class="nl-pille"><input type="date" data-datum value="${datum}" aria-label="Datum"></label><button type="button" class="nl-knopf rund" data-schieben="${woche ? 7 : 1}" aria-label="${woche ? 'Woche' : 'Tag'} weiter"><i class="bi bi-chevron-right"></i></button>
          <button type="button" class="nl-knopf" data-heute>Heute</button></div>
        ${segment([['tag', 'Tag', { symbol: 'calendar-day' }], ['woche', 'Woche', { symbol: 'calendar-week' }]], woche ? 'woche' : 'tag', { attr: 'data-ansicht', label: 'Zeitraum' })}
        <label class="nl-pille bu-zr ${ganzerTag() ? '' : 'aktiv'}"><i class="bi bi-clock" aria-hidden="true"></i>${stdWahl('von', f.von)}<span aria-hidden="true">–</span>${stdWahl('bis', f.bis)}</label>
      </div>
    </div>
    <div class="nl-leiste bu-filterleiste bu-artzeile">
      <div class="bu-artchips" ${arten.length > 1 ? '' : 'hidden'}><button type="button" class="bu-filterchip ${f.arten.size ? '' : 'an'}" data-resart="" aria-pressed="${!f.arten.size}">Alle<span class="bu-anzahl">${u.ressourcen.length}</span></button>
      ${arten.map(a => `<button type="button" class="bu-filterchip ${f.arten.has(a.id) ? 'an' : ''}" data-resart="${a.id}" aria-pressed="${f.arten.has(a.id)}"><i class="bi bi-${e(a.symbol)}" aria-hidden="true"></i>${e(a.name)}<span class="bu-anzahl">${u.ressourcen.filter(r => (r.artId ?? 0) === a.id).length}</span></button>`).join('')}
</div>
      <div class="bu-artzeile-rechts"><label class="rw-schalter"><input type="checkbox" data-res-nurfrei ${f.nurFrei ? 'checked' : ''}><span class="rw-spur" aria-hidden="true"></span>Nur verfügbare</label>
        ${segment([['karten', 'Karten', { symbol: 'grid-3x2-gap' }], ['liste', 'Liste', { symbol: 'list-ul' }]], ansicht, { attr: 'data-darstellung', label: 'Darstellung' })}</div>
    </div>
    <div class="bu-ru-kopf"><div class="bu-ru-zahlen" data-zahlen aria-live="polite"></div>
      <div class="bu-legende" aria-label="Legende"><span><i class="bu-punkt rfrei"></i>verfügbar</span><span><i class="bu-punkt rteil"></i>teilweise gebucht</span><span><i class="bu-punkt rvoll"></i>ausgebucht bzw. Raum belegt</span><span><i class="bu-punkt zr"></i>gewählter Zeitraum</span></div></div>
    <div data-ressourcen></div>`;
  seite.querySelectorAll('[data-schieben]').forEach(b => b.onclick = () => kontext.navigieren(ziel(plusTage(datum, Number(b.dataset.schieben)))));
  seite.querySelector('[data-heute]').onclick = () => kontext.navigieren(ziel(heuteIso()));
  seite.querySelector('[data-datum]').onchange = ev => ev.target.value && kontext.navigieren(ziel(ev.target.value));
  seite.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => kontext.navigieren(ziel(datum, b.dataset.wert === 'woche')));

  const box = seite.querySelector('[data-ressourcen]');
  const gebucht = (r, d, st) => r.gebucht?.[d]?.[st] ?? 0;
  /** Verfügbare Menge im gewählten Zeitraum (kleinster Wert über die Stunden). */
  const verfuegbar = (r, d = datum) => Math.min(...raster.filter(z => z.nr >= f.von && z.nr <= f.bis).map(z => r.anzahl - gebucht(r, d, z.nr)));
  /** Freie Stunden des Tages als Blöcke [[von, bis], …] (mindestens ein Stück frei). */
  const freieBloecke = (r, d = datum) => {
    const bl = [];
    for (const z of raster) {
      if (r.anzahl - gebucht(r, d, z.nr) <= 0) continue;
      const l = bl[bl.length - 1];
      if (l && l[1] === raster[raster.indexOf(z) - 1]?.nr) l[1] = z.nr; else bl.push([z.nr, z.nr]);
    }
    return bl;
  };
  const blockText = bl => bl.map(([v, b]) => v === b ? `${v}.` : `${v}.–${b}.`).join(', ');
  /**
   * Zustand im gewählten Zeitraum. „Ganzer Tag“ (Vorgabe): ganztägig verfügbar, teilweise (freie Stunden) bzw. ausgebucht; für einen
   * bestimmten Zeitraum: verfügbare Menge über alle gewählten Stunden. v = buchbare Menge, wahl = vorgeschlagene Stunden zum Buchen.
   */
  const zustand = r => {
    if (tag?.frei) return { klasse: 'tagfrei', text: tag.grund || 'unterrichtsfrei', v: 0 };
    if (ganzerTag()) {
      const bl = freieBloecke(r);
      const voll = Math.max(0, verfuegbar(r));
      const wahl = bl[0] ?? [f.von, f.bis];
      const anzahlFrei = bl.reduce((s, [v, b]) => s + raster.filter(z => z.nr >= v && z.nr <= b).length, 0);
      if (voll >= r.anzahl) return { klasse: 'frei', text: r.mobil ? 'ganztägig verfügbar' : 'Raum ganztägig frei', v: voll, wahl, frei: 'ganztägig' };
      if (!bl.length) return { klasse: 'belegt', text: r.mobil ? 'ausgebucht' : 'Raum belegt', v: 0, wahl, frei: '' };
      return { klasse: 'teil', text: anzahlFrei >= raster.length ? 'teilweise gebucht' : `${anzahlFrei} von ${raster.length} Std. frei`, v: Math.max(1, voll), wahl, frei: blockText(bl) };
    }
    const v = Math.max(0, verfuegbar(r));
    const wahl = [f.von, f.bis];
    if (!r.mobil) return v > 0 ? { klasse: 'frei', text: 'Raum frei', v, wahl } : { klasse: 'belegt', text: 'Raum belegt', v, wahl };
    if (v >= r.anzahl) return { klasse: 'frei', text: r.anzahl > 1 ? `alle ${r.anzahl} verfügbar` : 'verfügbar', v, wahl };
    return v > 0 ? { klasse: 'teil', text: `${v} von ${r.anzahl} verfügbar`, v, wahl } : { klasse: 'belegt', text: 'ausgebucht', v, wahl };
  };
  const darfBuchen = (r, d) => r.mobil && opt.ressourcenBuchen !== false && d >= heute;
  const ort = r => r.mobil ? (r.standort ? `<span><i class="bi bi-geo-alt" aria-hidden="true"></i>${e(r.standort)}</span>` : '<span class="nl-unter"><i class="bi bi-geo-alt" aria-hidden="true"></i>kein Standort</span>')
    : `<span><i class="bi bi-door-closed" aria-hidden="true"></i>fest in ${e(r.raum)}</span>`;

  /** Belegungs-Zeitstrahl eines Tages: je Stunde ein Segment, Füllhöhe = gebuchter Anteil; gewählter Zeitraum umrahmt. */
  const strahl = (r, t) => `<div class="bu-strahl ${t.datum === datum ? 'gewaehlt' : ''}">${raster.map(z => {
    const g = gebucht(r, t.datum, z.nr);
    const q = t.frei ? 'tagfrei' : g <= 0 ? 'rfrei' : g >= r.anzahl ? 'rvoll' : 'rteil';
    const zr = t.datum === datum && !ganzerTag() && z.nr >= f.von && z.nr <= f.bis ? ' zr' : '';
    const klick = !t.frei && q !== 'rvoll' && darfBuchen(r, t.datum);
    const attr = `class="bu-seg ${q}${zr}${t.datum < heute ? ' vergangen' : ''}" data-info="${r.id}|${t.datum}|${z.nr}" style="--anteil:${t.frei ? 0 : Math.min(1, g / r.anzahl)}"`;
    return klick ? `<button type="button" ${attr} data-res-buchen="${r.id}|${t.datum}|${z.nr}|${z.nr}" aria-label="${e(r.name)} am ${e(tagKurz(t.datum))}, ${z.nr}. Stunde buchen"><i></i></button>`
      : `<span ${attr} data-hover=""><i></i></span>`;
  }).join('')}</div>`;
  const achse = () => `<div class="bu-strahl-achse" aria-hidden="true">${raster.map(z => `<span>${z.nr}</span>`).join('')}</div>`;
  const zeitstrahl = (r, nurTag = false) => woche && !nurTag
    ? `<div class="bu-strahl-woche">${u.tage.map(t => `<div class="bu-strahl-zeile ${t.datum === datum ? 'gewaehlt' : ''} ${t.datum === heute ? 'heute' : ''}"><a class="bu-strahl-tag" href="${ziel(t.datum, true)}" title="${e(tagLang(t.datum))} wählen">${e(WOCHENTAGE_KURZ[tagIndex(t.datum)])}</a>${strahl(r, t)}</div>`).join('')}
        <div class="bu-strahl-zeile achse"><span class="bu-strahl-tag"></span>${achse()}</div></div>`
    : `${strahl(r, tag)}${achse()}`;
  const tagesBuchungen = r => (r.belegung[datum] ?? []);
  const knopf = (r, z) => {
    if (z.klasse === 'tagfrei') return '';
    const [v, b] = z.wahl;
    if (!r.mobil) return opt.raumBuchen === false || datum < heute || z.v <= 0 ? '' : `<a class="nl-knopf klein" href="#/buchen/raum/${datum}/${v}/${b}/${encodeURIComponent(r.raum)}"><i class="bi bi-door-open"></i>Raum buchen</a>`;
    if (!darfBuchen(r, datum)) return '';
    return z.v > 0 ? `<button type="button" class="nl-hinzu bu-klein" data-res-buchen="${r.id}|${datum}|${v}|${b}"><i class="bi bi-plus-lg"></i>Buchen</button>`
      : `<button type="button" class="nl-knopf klein" data-res-buchen="${r.id}|${datum}|${v}|${b}" title="Anderen Zeitraum oder Tag wählen"><i class="bi bi-clock"></i>Andere Zeit</button>`;
  };
  const karte = r => {
    const z = zustand(r);
    const n = tagesBuchungen(r).length;
    return `<article class="bu-rk ${z.klasse}" data-res="${r.id}">
      <div class="bu-rk-kopf"><span class="bu-rk-symbol ton-${resTon(r)}"><i class="bi bi-${e(ressourcenSymbol(r))}" aria-hidden="true"></i></span>
        <div class="bu-rk-titel"><b>${e(r.name)}</b><small>${e(r.art || 'Ohne Art')}</small></div>
        <span class="rw-status ${z.klasse === 'tagfrei' ? '' : z.klasse}">${e(z.text)}</span></div>
      <div class="bu-rk-meta">${ort(r)}<span><i class="bi bi-stack" aria-hidden="true"></i>${r.anzahl} Stück</span>${tag?.frei ? '' : ganzerTag() ? (z.frei && z.frei !== 'ganztägig' ? `<span class="bu-rk-verf">frei: <b>${e(z.frei)}</b> Std.</span>` : '') : r.mobil ? `<span class="bu-rk-verf"><b>${z.v}</b>/${r.anzahl} frei · ${e(zrText())}</span>` : ''}</div>
      ${r.bemerkung ? `<p class="bu-rk-bem">${e(r.bemerkung)}</p>` : ''}
      <div class="bu-rk-strahl">${zeitstrahl(r)}</div>
      <div class="bu-rk-fuss"><span class="nl-unter">${tag?.frei ? '' : r.mobil ? (n ? `${n} ${n === 1 ? 'Buchung' : 'Buchungen'} am ${e(tagKurz(datum))}` : `Am ${e(tagKurz(datum))} nicht gebucht`) : `Belegung von ${e(r.raum)}`}</span>${knopf(r, z)}</div>
    </article>`;
  };
  const zeile = r => {
    const z = zustand(r);
    return `<div class="bu-rl-zeile ${z.klasse}" data-res="${r.id}" role="row">
      <span class="bu-rk-symbol ton-${resTon(r)}" role="cell"><i class="bi bi-${e(ressourcenSymbol(r))}" aria-hidden="true"></i></span>
      <div class="bu-rk-titel" role="cell"><b>${e(r.name)}</b><small>${e(r.art || 'Ohne Art')}</small></div>
      <div class="bu-rl-ort" role="cell">${ort(r)}</div>
      <div class="bu-rl-menge" role="cell">${tag?.frei || ganzerTag() ? '' : r.mobil ? `<b>${z.v}</b><span>/${r.anzahl}</span>` : ''}<span class="rw-status ${z.klasse === 'tagfrei' ? '' : z.klasse}">${e(z.text)}</span></div>
      <div class="bu-rl-strahl" role="cell">${zeitstrahl(r, true)}</div>
      <div class="bu-rl-aktion" role="cell">${knopf(r, z)}</div></div>`;
  };
  const infoHtml = el => {
    const [id, d, st] = el.dataset.info.split('|');
    const r = u.ressourcen.find(x => String(x.id) === id);
    const t = u.tage.find(x => x.datum === d);
    if (!r || !t) return '';
    const s = Number(st), z = zeitVon(s);
    if (t.frei) return `<div class="bu-hover-kopf"><b>${e(tagKurz(d))}</b></div><div class="bu-hover-text">${e(t.grund || 'unterrichtsfrei')}</div>`;
    const g = gebucht(r, d, s);
    const eintraege = (r.belegung[d] ?? []).filter(x => x.von <= s && x.bis >= s);
    const kopf = r.mobil ? (g <= 0 ? (r.anzahl > 1 ? `alle ${r.anzahl} verfügbar` : 'verfügbar') : g >= r.anzahl ? 'ausgebucht' : `${r.anzahl - g} von ${r.anzahl} verfügbar`) : g ? `Raum ${r.raum} belegt` : `Raum ${r.raum} frei`;
    return `<div class="bu-hover-kopf"><i class="bu-punkt ${g <= 0 ? 'rfrei' : g >= r.anzahl ? 'rvoll' : 'rteil'}"></i><b>${e(kopf)}</b><span class="ms-auto">${e(r.name)}</span></div>
      <div class="bu-hover-zeit">${e(tagKurz(d))} · ${s}. Std. · ${e(z?.beginn ?? '')}–${e(z?.ende ?? '')} Uhr</div>
      ${r.mobil && eintraege.length ? `<ul class="bu-hover-liste">${eintraege.map(x => `<li>${x.menge > 1 ? `<b>${x.menge}×</b> ` : ''}${e(x.text || 'Buchung')}${x.absender ? ` <small>${e(x.absender)}</small>` : ''}</li>`).join('')}</ul>` : ''}
      ${el.matches('[data-res-buchen]') ? '<div class="bu-hover-tipp"><i class="bi bi-hand-index"></i>Anklicken, um diese Stunde zu buchen</div>' : ''}`;
  };

  const zeichnen = () => {
    const such = f.suche.toLowerCase();
    const sichtbar = u.ressourcen.filter(r => (!f.arten.size || f.arten.has(r.artId ?? 0))
      && (!such || `${r.name} ${r.art} ${r.standort} ${r.raum} ${r.bemerkung}`.toLowerCase().includes(such))
      && (!f.ort || (r.mobil ? r.standort : `fest in ${r.raum}`) === f.ort)
      && (!f.nurFrei || zustand(r).v > 0));
    const frei = sichtbar.filter(r => zustand(r).v > 0).length;
    seite.querySelector('[data-zahlen]').innerHTML = u.ressourcen.length ? `<b>${sichtbar.length}</b> ${sichtbar.length === 1 ? 'Ressource' : 'Ressourcen'}${sichtbar.length < u.ressourcen.length ? ` von ${u.ressourcen.length}` : ''}
      ${tag?.frei ? `<span class="nl-chip grau">${e(tagKurz(datum))}: ${e(tag.grund || 'unterrichtsfrei')}</span>` : `<span class="bu-ru-jetzt"><i class="bi bi-check2-circle"></i>${frei} ${ganzerTag() ? 'mit freien Stunden' : 'verfügbar'} am ${e(tagKurz(datum))}${ganzerTag() ? '' : `, ${e(zrText())}`}</span>`}` : '';
    if (!u.ressourcen.length) {
      box.innerHTML = `<div class="bu-leer"><i class="bi bi-box-seam" aria-hidden="true"></i><b>Es sind noch keine Ressourcen angelegt.</b>
        <span>${opt.admin ? 'Legen Sie mobile Geräte wie Beamer, Laptopwagen oder Tablet-Koffer an – oder übernehmen Sie zum Start typische Beispiele.' : 'Die Administration legt buchbare Geräte wie Beamer oder Laptopwagen an.'}</span>
        ${opt.admin ? '<div class="d-flex flex-wrap justify-content-center gap-2 mt-2"><button type="button" class="nl-knopf" data-beispiele><i class="bi bi-magic"></i>Beispiel-Ressourcen anlegen</button><a class="nl-hinzu" href="#/buchen/verwaltung"><i class="bi bi-plus-lg"></i>Ressourcen verwalten</a></div>' : ''}</div>`;
      box.querySelector('[data-beispiele]')?.addEventListener('click', async ev => {
        ev.currentTarget.disabled = true;
        try { const r = await senden('/api/app/buchen/ressourcen/beispiele', {}); toast(`${r.angelegt} Beispiel-Ressourcen angelegt.`); kontext.navigieren(ziel(), true); } catch (err) { kontext.fehler(err); }
      });
      return;
    }
    box.innerHTML = !sichtbar.length
      ? `<div class="bu-leer"><i class="bi bi-funnel" aria-hidden="true"></i><b>Keine Ressource passt zu den Filtern.</b><button type="button" class="nl-knopf mt-2" data-res-alle>Alle zeigen</button></div>`
      : ansicht === 'liste'
        ? `<div class="bu-rl ${woche ? 'woche' : ''}" role="table" aria-label="Ressourcen"><div class="bu-rl-kopf" role="row"><span role="columnheader"></span><span role="columnheader">Ressource</span><span role="columnheader">Standort</span><span role="columnheader">Verfügbar</span><span role="columnheader">Belegung ${e(tagKurz(datum))}</span><span role="columnheader"></span></div>${sichtbar.map(zeile).join('')}</div>`
        : `<div class="bu-rkarten">${sichtbar.map(karte).join('')}</div>`;
    box.querySelector('[data-res-alle]')?.addEventListener('click', () => { f.arten.clear(); f.suche = ''; f.ort = ''; f.nurFrei = false; kontext.navigieren(ziel(), true); });
  };
  box.addEventListener('click', ev => {
    const k = ev.target.closest('[data-res-buchen]');
    if (!k) return;
    const [id, d, von, bis] = k.dataset.resBuchen.split('|');
    const r = u.ressourcen.find(x => String(x.id) === id);
    ressourceBuchen(kontext, opt, r, d, Number(von), Number(bis), raster).then(ok => { if (ok) kontext.navigieren(ziel(), true); });
  });
  hoverVerbinden(box, infoHtml);
  seite.querySelectorAll('[data-resart]').forEach(b => b.onclick = () => {
    const a = b.dataset.resart;
    if (a === '') f.arten.clear(); else if (f.arten.has(Number(a))) f.arten.delete(Number(a)); else f.arten.add(Number(a));
    seite.querySelectorAll('[data-resart]').forEach(x => { const an = x.dataset.resart === '' ? !f.arten.size : f.arten.has(Number(x.dataset.resart)); x.classList.toggle('an', an); x.setAttribute('aria-pressed', String(an)); });
    zeichnen();
  });
  seite.querySelector('[data-res-nurfrei]').onchange = ev => { f.nurFrei = ev.target.checked; zeichnen(); };
  suchfeldMitFilterBinden(seite.querySelector('.bu-ru-leiste'), {
    beiSuche: t => { f.suche = t; zeichnen(); },
    beiFilter: (n, w) => { if (n === 'ort') { f.ort = w; zeichnen(); } },
  });
  seite.querySelectorAll('[data-zr]').forEach(s => s.onchange = () => {
    f[s.dataset.zr] = Number(s.value);
    if (f.bis < f.von) { if (s.dataset.zr === 'von') f.bis = f.von; else f.von = f.bis; }
    seite.querySelector('[data-zr=von]').value = String(f.von); seite.querySelector('[data-zr=bis]').value = String(f.bis);
    seite.querySelector('.bu-zr').classList.toggle('aktiv', !ganzerTag());
    zeichnen();
  });
  seite.querySelectorAll('[data-darstellung]').forEach(b => b.onclick = () => { ansicht = b.dataset.wert; merken('ressourcen', ansicht); segmentSetzen(b.closest('.u-segment'), ansicht); zeichnen(); });
  zeichnen();
}

/**
 * Ressource direkt aus der Übersicht buchen: Datum, Stunden und Menge (verfügbare Menge wird live geprüft), Titel und Bemerkung –
 * „Serie / weitere Optionen“ öffnet den Assistenten mit derselben Vorbelegung.
 */
function ressourceBuchen(kontext, opt, r, datum, von, bis, raster) {
  return new Promise(fertig => {
    let ok = false, frei = 0;
    const box = kbDialog({
      titel: `${r.name} buchen`, breite: 'm',
      inhalt: `<div class="bu-schnell-kopf"><span class="bu-rk-symbol ton-${resTon(r)}"><i class="bi bi-${e(ressourcenSymbol(r))}"></i></span><div><b>${e(r.name)}</b>
          <span>${e([r.art, r.standort ? `Standort: ${r.standort}` : '', r.anzahl > 1 ? `${r.anzahl} Stück` : ''].filter(Boolean).join(' · '))}</span></div></div>
        <div class="bu-raster zwei">
          ${feld('Datum', `<input type="date" class="form-control" name="datum" value="${datum}" min="${heuteIso()}">`)}
          <div class="bu-feld"><span>Verfügbar</span><div class="bu-verf" data-verf aria-live="polite">…</div></div>
          ${feld('Von Stunde', `<select class="form-select" name="von">${stundenOptionen(raster, von)}</select>`)}
          ${feld('Bis Stunde', `<select class="form-select" name="bis">${stundenOptionen(raster, bis)}</select>`)}
          ${r.anzahl > 1 ? feld('Menge', `<input type="number" class="form-control" name="menge" min="1" max="${r.anzahl}" value="1">`) : ''}
          ${feld('Titel / Anlass', '<input class="form-control" name="titel" maxlength="200" placeholder="z. B. Referate 9b">', 'breit')}
          ${feld('Bemerkung', '<input class="form-control" name="text" maxlength="1000" placeholder="optional">', 'breit')}
        </div>`,
      fuss: `<a class="nl-knopf kbd-links" data-erweitert href="#/buchen/ressource"><i class="bi bi-arrow-repeat"></i>Serie / weitere Optionen</a>
        <button type="button" class="nl-knopf" data-kbd-zu>Abbrechen</button><button type="button" class="nl-knopf nw-primaer" data-res-speichern><i class="bi bi-check-lg"></i>Buchen</button>`,
    });
    const w = n => box.querySelector(`[name=${n}]`);
    const speichern = box.querySelector('[data-res-speichern]');
    let nr = 0;
    const pruefen = async () => {
      if (Number(w('bis').value) < Number(w('von').value)) w('bis').value = w('von').value;
      const ziel = box.querySelector('[data-verf]');
      const meine = ++nr;
      ziel.className = 'bu-verf'; ziel.textContent = '…';
      try {
        const s = await senden('/api/app/buchen/suche/ressourcen', { zeit: { wiederholung: 'einmalig', datum: w('datum').value, stundeVon: Number(w('von').value), stundeBis: Number(w('bis').value) }, kriterien: { maxBelegung: 100 } });
        if (meine !== nr) return;
        frei = s.ressourcen.find(x => x.id === r.id)?.frei ?? 0;
        ziel.classList.add(frei <= 0 ? 'belegt' : frei < r.anzahl ? 'teil' : 'frei');
        ziel.textContent = frei <= 0 ? 'ausgebucht' : r.anzahl > 1 ? `${frei} von ${r.anzahl}` : 'verfügbar';
        const m = w('menge');
        if (m) { m.max = String(Math.max(1, frei)); if (Number(m.value) > frei) m.value = String(Math.max(1, frei)); }
      } catch (err) {
        if (meine !== nr) return;
        frei = 0; ziel.classList.add('belegt'); ziel.textContent = err.message;
      }
      speichern.disabled = frei <= 0;
    };
    ['datum', 'von', 'bis'].forEach(n => w(n).addEventListener('change', pruefen));
    box.querySelector('[data-erweitert]').onclick = ev => { ev.preventDefault(); box.schliessen(); kontext.navigieren(`#/buchen/ressource/${w('datum').value}/${w('von').value}/${w('bis').value}/${r.id}`); };
    box.querySelector('[data-kbd-zu]').onclick = () => box.schliessen();
    speichern.onclick = async () => {
      speichern.disabled = true;
      try {
        await senden('/api/app/buchen', {
          art: 'ressource', termine: [{ datum: w('datum').value, stundeVon: Number(w('von').value), stundeBis: Number(w('bis').value), raum: null }],
          ressourcen: [{ id: r.id, menge: Number(w('menge')?.value) || 1 }], klassen: [], lehrer: opt.ich || '', fach: '', titel: w('titel').value, text: w('text').value,
          benachrichtigen: false, unterrichtId: null, beginn: null, ende: null,
        });
        toast(`${r.name} gebucht.`);
        ok = true;
        box.schliessen();
      } catch (err) { kontext.fehler(err); speichern.disabled = false; }
    };
    box.geschlossen.then(() => fertig(ok));
    pruefen();
    setTimeout(() => w('titel').focus(), 50);
  });
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
