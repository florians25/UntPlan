// Termin-Modul (Menü „Buchen“) im neuen Design, Abläufe wie in der Vorlage:
//  • Raum- / Ressourcenliste (Kapazität, „sofort buchen“ bzw. „reservieren“ mit Bestätigung durch die Raumadministration), Raumwechsel
//  • Neue Aktivität: zuerst der Zeitbereich (Datum, Beginnzeit, Endzeit, ganztägig → Weiter), dann Lehrkraft, Klasse(n), Räume, Fach, Von – Bis,
//    Abteilung, Menge, Verantwortliche*r, Art, Teilnehmer, Kostenstelle, Bemerkung, Anmerkung, E-Mail-Adresse, Benachrichtigungen → Speichern
//  • Zusätzliche Stunden (aus Mein Unterricht, Tagesunterricht Lehrkraft, Meine Buchungen | Bearbeiten): Zeitbereich, Lehrkraft- und
//    Klassenkonflikte zulassen, Räume → Suchen → Beginnzeit anklicken → Buchen (Recht „Stundenplanänderung“)
//  • Terminsuche (Verabredungen): Lehrkräfte, Klassen, Räume wählen → Suchen → freie Zeiten grün → Klick → „Veranstaltung am …“ → Buchen
//    (Standarddauer und Zeitbereich aus Administration | Buchungsbeschränkungen; Feiertage und Ferien werden angezeigt)
//  • Buchung bearbeiten (Meine Buchungen | Bearbeiten): Details ändern, stornieren, zusätzliche Stunden, Schüler*innen der Schülergruppe, Berichte
//  • Neuer Unterricht (Unterricht | Mein Unterricht | Neuer Unterricht) und Buchungsbeschränkungen (Administration)
import { e, holen, senden, api, toast, bestaetigen, heuteIso, plusTage, montag, herunterladen, WOCHENTAGE, minuten } from '../ui.js';
import { kbDialog } from '../kbdialog.js';
import { cssLaden } from './kbeinstellungen.js';

/** Eigene Stile (einmal je Seite). */
function stileLaden(kontext = null) {
  maskenKontext = kontext;
  cssLaden();
  if (document.querySelector('link[data-termin-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = 'css/termin.css';
  l.dataset.terminCss = '1';
  document.head.append(l);
}

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
/** „2026-11-18 16:19“ → „18.11.2026 16:19“ */
const zeitstempel = t => { const [d, z] = String(t ?? '').split(' '); return d ? `${datumDe(d)}${z ? ` ${z}` : ''}` : ''; };
const hhmm = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
/** Kurzdatum wie in der Vorlage: „16.2.“ */
const tagPunkt = iso => `${Number(iso.slice(8, 10))}.${Number(iso.slice(5, 7))}.`;
const tagIndex = iso => (new Date(iso + 'T12:00').getDay() + 6) % 7;
const haken = (attr, text, an, typ = 'checkbox', name = '') => `<label class="pe-haken"><input type="${typ}" ${name ? `name="${name}"` : ''} ${attr} ${an ? 'checked' : ''}><span class="pe-kreis" aria-hidden="true"></span><span>${text}</span></label>`;
const pfeld = (label, html, klasse = '') => `<label class="pe-feld rb-feld ${klasse}"><span class="pe-feld-label">${e(label)}</span>${html}</label>`;
/** Kontext der aktuellen Maske: der Titel der Maske steht wie in der Vorlage einmal oben (Seitenkopf), ohne zusätzliche Kopfknöpfe. */
let maskenKontext = null;
const rahmen = (titel, inhalt, fuss) => { maskenKontext?.titel(titel); return `<div class="rb tm"><section class="rb-karte">${inhalt}</section>${fuss ? `<div class="rb-fuss tm-fuss">${fuss}</div>` : ''}</div>`; };
/** Zweispaltige Maske wie in der Vorlage: [links, rechts] je Zeile (rechts optional), Text = Zeile über die ganze Breite. */
const maske = (zeilen, attr = '') => `<div class="tm-maske" ${attr}>${zeilen.filter(Boolean).map(z => Array.isArray(z)
  ? `<div class="tm-l">${z[0] ?? ''}</div>${z[1] ? `<div class="tm-r">${z[1]}</div>` : ''}` : `<div class="tm-voll">${z}</div>`).join('')}</div>`;
const auswahl = (liste, wert, leerText = '<Auswahl>') => `<option value="">${e(leerText)}</option>${liste.map(x => {
  const [w, t, z] = Array.isArray(x) ? x : [x, x];
  return `<option value="${e(w)}" ${String(w) === String(wert ?? '') ? 'selected' : ''} ${z ? `data-zweit="${e(z)}"` : ''}>${e(t)}</option>`;
}).join('')}`;
/** Arten einer Aktivität (Feld „Art“). */
export const AKTIVITAETSARTEN = ['Unterricht', 'Aktivität', 'Veranstaltung', 'Besprechung', 'Konferenz', 'Sprechstunde', 'Förderunterricht', 'Exkursion', 'Prüfung', 'Bereitschaft', 'Sonstiges'];
const ueberlappt = (liste, a, b) => (liste ?? []).some(x => x.von < b && x.bis > a);

async function pdfLaden(id) {
  const r = await fetch(`/api/app/buchen/${id}/beleg.pdf`, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
  if (!r.ok) throw new Error('PDF konnte nicht erstellt werden.');
  herunterladen(`buchung-${id}.pdf`, await r.blob(), 'application/pdf');
}

/** Felder „Zusatzangaben“ (Neue Aktivität, Veranstaltung, Buchung bearbeiten, Neuer Unterricht) – einzeln, die Maske ordnet sie wie in der Vorlage an. */
function zusatzFelder(z, el, opt) {
  return {
    abteilung: pfeld('Abteilung', `<select class="form-select" data-zu="abteilung">${auswahl(opt.abteilungen ?? [], z.abteilung)}</select>`),
    menge: pfeld('Menge', `<input type="number" min="0" class="form-control" data-zu="menge" value="${z.menge ?? 0}">`),
    verantwortlich: pfeld('Verantwortlicher', `<select class="form-select" data-zu="verantwortlich">${auswahl(el.lehrer.map(l => [l.kurz, l.name ? `${l.kurz} (${l.name})` : l.kurz]), z.verantwortlich)}</select>`),
    art: pfeld('Art', `<select class="form-select" data-zu="aktivitaetsart">${auswahl(AKTIVITAETSARTEN, z.aktivitaetsart)}</select>`),
    teilnehmer: pfeld('Teilnehmer', `<input type="number" min="0" class="form-control" data-zu="teilnehmer" value="${z.teilnehmer ?? 0}">`),
    kostenstelle: pfeld('Kostenstelle', `<input class="form-control" maxlength="100" data-zu="kostenstelle" value="${e(z.kostenstelle ?? '')}">`),
  };
}
const zusatzBinden = (wurzel, z) => wurzel.querySelectorAll('[data-zu]').forEach(f => f.addEventListener(f.tagName === 'SELECT' || f.type === 'checkbox' ? 'change' : 'input', () => {
  const n = f.dataset.zu;
  z[n] = f.type === 'checkbox' ? f.checked : f.type === 'number' ? Number(f.value) || 0 : f.value;
}));
/** Bemerkung, Anmerkung, E-Mail Adresse – je eine Zeile wie in der Vorlage. */
const textFelder = z => [
  pfeld('Bemerkung (für alle Benutzer sichtbar)', `<input class="form-control" maxlength="1000" data-zu="bemerkung" value="${e(z.bemerkung ?? '')}">`, 'breit'),
  pfeld('Anmerkung (dieses Feld wird in zukünftigen Versionen entfernt)', `<input class="form-control" maxlength="1000" data-zu="anmerkung" value="${e(z.anmerkung ?? '')}">`, 'breit'),
  pfeld('E-Mail Adresse', `<input type="email" class="form-control" maxlength="200" data-zu="email" value="${e(z.email ?? '')}">`, 'breit')];
/** „Von - Bis“ wie in der Vorlage: zwei Datumsfelder unter einer Beschriftung, daneben das Kalendersymbol (Periodizität). */
const vonBis = (von, bis, periode = '') => `<div class="tm-vonbis">${pfeld('Von - Bis', von)}${pfeld('', bis, 'tm-ohne-label')}${periode}</div>`;
const zusatzDaten = z => ({ abteilung: z.abteilung || '', verantwortlich: z.verantwortlich || '', aktivitaetsart: z.aktivitaetsart || '', teilnehmer: Number(z.teilnehmer) || 0,
  kostenstelle: z.kostenstelle || '', menge: Number(z.menge) || 0, email: z.email || '', emailBenachrichtigung: !!z.emailBenachrichtigung, anmerkung: z.anmerkung || '' });

/** Liste gewählter Elemente als Chips (rot: zur gleichen Zeit schon belegt, gelb: Raum darf nur reserviert werden). */
const chips = (liste, konflikt = new Set(), reserv = new Set()) => liste.length ? `<div class="tm-chips">${liste.map(x => `<span class="tm-chip ${konflikt.has(x) ? 'konflikt' : reserv.has(x) ? 'reserv' : ''}"><i class="bi bi-check2" aria-hidden="true"></i>${e(x)}</span>`).join('')}</div>` : '';
const LEGENDE = `<div class="tm-legende"><span><i class="tm-farbe konflikt"></i>Farbig gekennzeichnete Elemente haben zur gleichen Zeit schon eine andere Aktivität</span>
  <span><i class="tm-farbe reserv"></i>Farbig gekennzeichnete Räume dürfen nur reserviert werden</span></div>`;

// ------------------------------------------------------------------ Raum- / Ressourcenliste

export async function raumliste(seite, kontext) {
  stileLaden(kontext);
  const opt = await holen('/api/app/buchen/optionen');
  kontext.titel('Raum- / Ressourcenliste', '<a class="nl-knopf" href="#/buchen/ressourcen"><i class="bi bi-box-seam"></i>Ressourcenübersicht</a>');
  const raeume = opt.raeume;
  const ressourcen = opt.ressourcen;
  seite.innerHTML = `<div class="tm">
    <div class="nl-leiste"><label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Räume und Ressourcen suchen"></label>
      <span class="ms-auto d-flex gap-2"><button type="button" class="nl-knopf" data-csv><i class="bi bi-filetype-csv"></i>Bericht</button></span></div>
    <h3 class="rb-abschnitt">Räume</h3>
    <div class="nl-tabelle"><table><thead><tr><th>Raum</th><th>Langname</th><th>Raumart</th><th class="text-end">Kapazität</th><th>Gebäude</th><th>Abteilung</th><th>Raumgruppe</th><th>Ressourcen</th><th>Buchung</th><th></th></tr></thead><tbody>
      ${raeume.map(r => `<tr data-name="${e([r.kurz, r.name, r.raumart, r.gebaeude, r.abteilung, ...(r.ressourcen ?? [])].join(' ').toLowerCase())}">
        <td><span class="rb-kurz">${e(r.kurz)}</span></td><td>${e(r.name)}</td><td>${e(r.raumart)}</td><td class="text-end">${r.kapazitaet || ''}</td><td>${e(r.gebaeude)}</td><td>${e(r.abteilung)}</td>
        <td>${e(r.raumgruppe)}</td><td class="nl-unter">${e((r.ressourcen ?? []).join(', '))}</td>
        <td>${!r.buchbar ? '<span class="nl-chip grau">nicht buchbar</span>' : r.nurReservieren ? '<span class="nl-chip gelb">reservieren</span>' : '<span class="nl-chip gruen">sofort buchen</span>'}</td>
        <td class="text-end text-nowrap">${r.buchbar && opt.raumBuchen !== false ? `<a class="nl-knopf klein" href="#/buchen/raum/${heuteIso()}/1/1/${encodeURIComponent(r.kurz)}">Raum buchen</a>` : ''}
          <a class="nl-icon sichtbar" href="#/plan/raum/${encodeURIComponent(r.kurz)}" title="Raumplan" aria-label="Raumplan"><i class="bi bi-calendar3-week"></i></a></td></tr>`).join('')}
    </tbody></table></div>
    <h3 class="rb-abschnitt">Ressourcen</h3>
    <div class="nl-tabelle"><table><thead><tr><th>Ressource</th><th>Art</th><th>Raum / Standort</th><th class="text-end">Anzahl</th><th>Bemerkung</th><th></th></tr></thead><tbody>
      ${ressourcen.length ? ressourcen.map(r => `<tr data-name="${e([r.name, r.art, r.raum, r.standort, r.bemerkung].join(' ').toLowerCase())}"><td><span class="rb-kurz">${e(r.name)}</span></td><td>${e(r.art)}</td>
        <td>${e(r.raum || r.standort || '')}</td><td class="text-end">${r.anzahl}</td><td class="nl-unter">${e(r.bemerkung)}</td>
        <td class="text-end">${!r.raum && opt.ressourcenBuchen !== false ? '<a class="nl-knopf klein" href="#/buchen/ressource">Ressource buchen</a>' : ''}</td></tr>`).join('')
        : '<tr><td colspan="6" class="nl-unter">Keine Ressourcen angelegt.</td></tr>'}
    </tbody></table></div></div>`;
  seite.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    seite.querySelectorAll('tbody tr[data-name]').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  };
  seite.querySelector('[data-csv]').onclick = () => herunterladen('raumliste.csv', [
    'Raum;Langname;Raumart;Kapazität;Gebäude;Abteilung;Raumgruppe;Ressourcen;Buchung',
    ...raeume.map(r => [r.kurz, r.name, r.raumart, r.kapazitaet, r.gebaeude, r.abteilung, r.raumgruppe, (r.ressourcen ?? []).join(', '),
      !r.buchbar ? 'nicht buchbar' : r.nurReservieren ? 'reservieren' : 'sofort buchen'].map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}

// ------------------------------------------------------------------ Raumwechsel (bestehende Stunde in einen anderen Raum)

export async function raumwechsel(seite, kontext, [datumParam]) {
  stileLaden(kontext);
  const ich = kontext.ich;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  kontext.titel('Raumwechsel');
  if (!ich.element) { seite.innerHTML = rahmen('Raumwechsel', '<p class="rb-leer">Der Raumwechsel steht Lehrkräften für ihre eigenen Stunden zur Verfügung.</p>'); return; }
  const plan = await holen(`/api/app/plan?typ=lehrer&element=${encodeURIComponent(ich.element)}&datum=${datum}`);
  const stunden = (plan.karten ?? []).filter(k => k.datum === datum && k.unterricht && !['entfall', 'buchung', 'veranstaltung', 'vorgemerkt'].includes(k.status));
  seite.innerHTML = rahmen('Raumwechsel', `
    <div class="rb-zeile">${pfeld('Datum', `<input type="date" class="form-control" data-datum value="${datum}">`)}</div>
    ${stunden.length ? `<table class="pe-tabelle rb-ergebnis tm-abstand"><thead><tr><th>Stunde</th><th>Fach</th><th>Klasse</th><th>Raum</th><th></th></tr></thead><tbody>
      ${stunden.map((k, i) => `<tr><td>${k.von === k.bis ? `${k.von}.` : `${k.von}.–${k.bis}.`}</td><td>${e(k.fach ?? '')}</td><td>${e((k.klassen ?? []).map(x => x.k ?? x).join(', '))}</td>
        <td>${e((k.raeume ?? []).map(x => x.k ?? x).join(', '))}</td><td class="text-end"><button type="button" class="pe-knopf klein" data-stunde="${i}">Raum wechseln</button></td></tr>`).join('')}
    </tbody></table>` : '<p class="rb-leer tm-abstand">An diesem Tag haben Sie keine Stunden.</p>'}`);
  seite.querySelector('[data-datum]').onchange = ev => kontext.navigieren(`#/buchen/raumwechsel/${ev.target.value}`);
  seite.querySelectorAll('[data-stunde]').forEach(b => b.onclick = async () => {
    const k = stunden[Number(b.dataset.stunde)];
    const { karteDialog } = await import('../stundendetails.js');
    karteDialog(k, { ...kontext, liste: stunden, typ: 'lehrer', nachSpeichern: () => kontext.navigieren(location.hash, true) });
  });
}

// ------------------------------------------------------------------ Neue Aktivität (zuerst der Termin, dann die Beteiligten)

export async function neueAktivitaet(seite, kontext) {
  stileLaden(kontext);
  const [opt, el] = await Promise.all([holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const raster = opt.zeitraster.filter(z => z.nr > 0 && z.beginn && z.ende);
  const heute = heuteIso();
  kontext.titel('Neue Aktivität');
  if (opt.buchungenGesperrt && !opt.admin) { seite.innerHTML = rahmen('Neue Aktivität', '<p class="rb-leer">Buchungen sind derzeit gesperrt.</p>'); return; }
  const start = opt.naechsterTag && opt.naechsterTag > heute ? opt.naechsterTag : heute;
  const z = { datum: start, bis: start, wiederholung: 'woechentlich', stundeVon: raster[0]?.nr ?? 1, stundeBis: raster[0]?.nr ?? 1 };
  const d = { lehrer: opt.ich || '', klassen: [], raeume: [], fach: '', benachrichtigen: false, sofort: true,
    zu: { verantwortlich: opt.ich || '', aktivitaetsart: 'Unterricht', menge: 0, teilnehmer: 0, emailBenachrichtigung: true } };
  const buchbar = opt.raeume.filter(r => r.buchbar);
  const reserv = new Set(opt.raeume.filter(r => r.nurReservieren).map(r => r.kurz));
  let schritt = 'zeit', gespeichert = null, konflikt = new Set(), belegt = new Set(), periode = false;
  const zeitText = () => {
    const a = raster.find(r => r.nr === z.stundeVon), b = raster.find(r => r.nr === z.stundeBis);
    return `${datumDe(z.datum)}${z.bis > z.datum ? ` – ${datumDe(z.bis)}` : ''}, ${a?.beginn ?? ''} – ${b?.ende ?? ''}`;
  };
  const zeitDaten = () => ({ wiederholung: z.bis > z.datum ? z.wiederholung : 'einmalig', datum: z.datum, von: z.datum, bis: z.bis, wochentage: [tagIndex(z.datum)], stundeVon: z.stundeVon, stundeBis: z.stundeBis });

  function zeichnen() { if (schritt === 'zeit') zeitZeichnen(); else if (schritt === 'buchen') buchenZeichnen(); else erfolgZeichnen(); window.scrollTo(0, 0); }

  function zeitZeichnen() {
    const ganz = z.stundeVon === raster[0]?.nr && z.stundeBis === raster[raster.length - 1]?.nr;
    seite.innerHTML = rahmen('Neue Aktivität', `
      <h3 class="rb-abschnitt">Zeitbereich</h3>
      <div class="rb-zeitbereich"><div class="rb-zeit">
        <div class="rb-zeile">${pfeld('Datum', `<input type="date" class="form-control" data-z="datum" value="${z.datum}" min="${heute}">`)}</div>
        <div class="rb-zeile">
          ${pfeld('Beginnzeit', `<select class="form-select" data-z="stundeVon">${raster.map(r => `<option value="${r.nr}" ${r.nr === z.stundeVon ? 'selected' : ''}>${e(r.beginn)}</option>`).join('')}</select>`)}
          ${pfeld('Endzeit', `<select class="form-select" data-z="stundeBis">${raster.map(r => `<option value="${r.nr}" ${r.nr === z.stundeBis ? 'selected' : ''}>${e(r.ende)}</option>`).join('')}</select>`)}
          <button type="button" class="pe-knopf rb-ganztaegig ${ganz ? 'an' : ''}" data-ganztaegig aria-pressed="${ganz}">ganztägig</button>
        </div></div></div>`,
      '<button type="button" class="pe-knopf primaer" data-weiter>Weiter</button><button type="button" class="pe-knopf" data-abbrechen>Abbrechen</button>');
    seite.querySelectorAll('[data-z]').forEach(f => f.onchange = () => {
      const n = f.dataset.z;
      z[n] = n.startsWith('stunde') ? Number(f.value) : f.value;
      if (n === 'stundeVon' && z.stundeBis < z.stundeVon) z.stundeBis = z.stundeVon;
      if (n === 'stundeBis' && z.stundeBis < z.stundeVon) z.stundeVon = z.stundeBis;
      if (n === 'datum') z.bis = z.datum;
      if (n.startsWith('stunde')) zeitZeichnen();
    });
    seite.querySelector('[data-ganztaegig]').onclick = () => { z.stundeVon = raster[0].nr; z.stundeBis = raster[raster.length - 1].nr; zeitZeichnen(); };
    seite.querySelector('[data-abbrechen]').onclick = () => kontext.navigieren('#/buchen/meine');
    seite.querySelector('[data-weiter]').onclick = async ev => {
      const knopf = ev.currentTarget; knopf.disabled = true;
      try {
        const t = await senden('/api/app/buchen/termine', zeitDaten());
        if (!t.termine.length) throw new Error('Am gewählten Tag ist kein Unterricht.');
        schritt = 'buchen'; zeichnen();
      } catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
  }

  /** Konflikte: Lehrkraft bzw. Klassen haben zur gleichen Zeit schon eine Aktivität, Räume sind belegt. */
  async function pruefen() {
    try {
      const v = await senden('/api/app/buchen/verfuegbarkeit', { von: z.datum, bis: z.datum, lehrer: [d.lehrer].filter(Boolean), klassen: d.klassen, raeume: d.raeume });
      const a = raster.find(r => r.nr === z.stundeVon), b = raster.find(r => r.nr === z.stundeBis);
      const [va, vb] = [minuten(a?.beginn), minuten(b?.ende)];
      const tag = v.tage[0];
      konflikt = new Set();
      belegt = new Set();
      for (const [k, liste] of Object.entries(tag?.belegt ?? {})) if (ueberlappt(liste, va, vb)) { const x = k.split(':')[1]; (k.startsWith('raum:') ? belegt : konflikt).add(x); }
    } catch { konflikt = new Set(); belegt = new Set(); }
    const ziel = seite.querySelector('[data-auswahl]');
    if (!ziel) return;
    seite.querySelector('[data-lehrer-anzeige]').classList.toggle('konflikt', konflikt.has(d.lehrer));
    ziel.querySelector('[data-klassen-chips]').innerHTML = chips(d.klassen, konflikt);
    ziel.querySelector('[data-raeume-chips]').innerHTML = chips(d.raeume, belegt, reserv);
    sofortSetzen();
  }
  /** „Buchung sofort bestätigen“: für reservierbare Räume nur mit Verwaltungsrecht. */
  function sofortSetzen() {
    const c = seite.querySelector('[data-sofort]');
    if (!c) return;
    const nurRes = d.raeume.some(r => reserv.has(r));
    c.disabled = !opt.admin;
    if (!opt.admin) { d.sofort = !nurRes; c.checked = d.sofort; }
  }

  function buchenZeichnen() {
    // Maske wie in der Vorlage: Lehrkraft | Klasse, Räume, Legende, Fach, Von - Bis, Abteilung, Menge | Verantwortlicher, Art,
    // Teilnehmer | Kostenstelle, Bemerkung, Anmerkung, E-Mail Adresse, Benachrichtigungen → Speichern, Zurück
    const f = zusatzFelder(d.zu, el, opt);
    seite.innerHTML = rahmen('Neue Aktivität', `
      <h3 class="rb-abschnitt">Buchen</h3>
      ${maske([
        [pfeld('Lehrkraft', `<select class="form-select tm-lehrer" data-d="lehrer" data-lehrer-anzeige>${auswahl(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), d.lehrer, 'keine')}</select>`),
          `<div class="tm-spalte">${pfeld('Klasse', `<select multiple class="form-select" data-d="klassen" data-platzhalter="Bitte wählen Sie">${el.klassen.map(x => `<option value="${e(x.kurz)}" ${d.klassen.includes(x.kurz) ? 'selected' : ''}>${e(x.kurz)}</option>`).join('')}</select>`)}
          <div data-klassen-chips></div></div>`],
        [`<div class="tm-spalte">${pfeld('Räume', `<select multiple class="form-select" data-d="raeume" data-platzhalter="Bitte wählen Sie">${buchbar.map(r => `<option value="${e(r.kurz)}" ${d.raeume.includes(r.kurz) ? 'selected' : ''} data-zweit="${e(r.name)}">${e(r.kurz)}</option>`).join('')}</select>`)}
          <div data-raeume-chips></div></div>`],
        LEGENDE,
        [pfeld('Fach', `<select class="form-select" data-d="fach">${auswahl(el.faecher.map(x => [x.kurz, x.kurz, x.name]), d.fach)}</select>`)],
        vonBis(`<input type="date" class="form-control" data-z2="datum" value="${z.datum}" disabled>`, `<input type="date" class="form-control" data-z2="bis" value="${z.bis}" min="${z.datum}" aria-label="Bis">`,
          `<button type="button" class="nl-icon sichtbar ${periode ? 'an' : ''}" data-periode title="Periodizität" aria-label="Periodizität" aria-expanded="${periode}"><i class="bi bi-calendar3"></i></button>`),
        periode ? [pfeld('Periodizität', `<select class="form-select" data-z2="wiederholung">${[['woechentlich', 'Jede Woche'], ['taeglich', 'Täglich'], ['2wochen', 'Jede 2. Woche']].map(([w, t]) => `<option value="${w}" ${w === z.wiederholung ? 'selected' : ''}>${t}</option>`).join('')}</select>`)] : null,
        [f.abteilung],
        [f.menge, f.verantwortlich],
        [f.art],
        [f.teilnehmer, f.kostenstelle],
        ...textFelder(d.zu),
        `<div class="rb-haken tm-haken">
          ${haken('data-zu="emailBenachrichtigung"', 'E-Mail-Benachrichtigung bei Statusänderung', d.zu.emailBenachrichtigung)}
          ${haken('data-d="benachrichtigen"', 'Lehrkraft der Aktivität benachrichtigen', d.benachrichtigen)}
          ${haken('data-sofort', 'Buchung sofort bestätigen', d.sofort)}
        </div>`,
      ], 'data-auswahl')}`, '<button type="button" class="pe-knopf primaer" data-speichern>Speichern</button><button type="button" class="pe-knopf" data-zurueck>Zurück</button>');
    seite.querySelector('[data-periode]').onclick = () => { periode = !periode; buchenZeichnen(); };
    seite.querySelectorAll('[data-d]').forEach(f => f.addEventListener('change', () => {
      const n = f.dataset.d;
      d[n] = f.multiple ? [...f.selectedOptions].map(o => o.value) : f.type === 'checkbox' ? f.checked : f.value;
      if (['lehrer', 'klassen', 'raeume'].includes(n)) pruefen();
    }));
    seite.querySelectorAll('[data-z2]').forEach(f => f.onchange = () => { z[f.dataset.z2] = f.value; if (z.bis < z.datum) z.bis = z.datum; });
    zusatzBinden(seite, d.zu);
    seite.querySelector('[data-sofort]').onchange = ev => { d.sofort = ev.target.checked; };
    seite.querySelector('[data-zurueck]').onclick = () => { schritt = 'zeit'; zeichnen(); };
    seite.querySelector('[data-speichern]').onclick = async ev => {
      const knopf = ev.currentTarget; knopf.disabled = true;
      try {
        const t = await senden('/api/app/buchen/termine', zeitDaten());
        if (!t.termine.length) throw new Error('Im gewählten Zeitraum gibt es keinen Unterrichtstag.');
        const zu = zusatzDaten(d.zu);
        gespeichert = await senden('/api/app/buchen', {
          art: 'aktivitaet', termine: t.termine.map(x => ({ datum: x.datum, stundeVon: x.stundeVon, stundeBis: x.stundeBis, raum: d.raeume[0] || null })),
          weitereRaeume: d.raeume.slice(1), ressourcen: [], klassen: d.klassen, lehrer: d.lehrer || '', fach: d.fach,
          titel: (d.zu.bemerkung || '').slice(0, 200) || d.zu.aktivitaetsart || 'Aktivität', text: d.zu.bemerkung || '', zusatz: zu,
          benachrichtigen: d.benachrichtigen && !!d.lehrer, sofortBestaetigen: d.sofort,
        });
        schritt = 'fertig'; zeichnen();
      } catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
    pruefen();
  }

  function erfolgZeichnen() {
    erfolgsansicht(seite, kontext, gespeichert, raster, () => { gespeichert = null; schritt = 'zeit'; zeichnen(); });
  }
  zeichnen();
}

/** „Erfolgreiche Buchung {Nr.}“ mit den Terminen und ihrem Status (Bestätigt bzw. Nicht bestätigt). */
function erfolgsansicht(seite, kontext, g, raster, neu) {
  const b = g.buchungen;
  const x = b[0];
  const zeit = t => t.beginn ? `${t.beginn} – ${t.ende}` : (() => { const a = raster.find(r => r.nr === t.stundeVon), c = raster.find(r => r.nr === t.stundeBis); return a && c ? `${a.beginn} – ${c.ende}` : `${t.stundeVon}.–${t.stundeBis}.`; })();
  seite.innerHTML = rahmen(`Erfolgreiche Buchung ${x.id}`, `
    <dl class="rb-angaben"><dt>Klasse</dt><dd>${e(x.klassen.join(', ') || '–')}</dd><dt>Lehrkraft</dt><dd>${e(x.alleLehrer.join(', ') || '–')}</dd><dt>Fach</dt><dd>${e(x.fach || '–')}</dd>
      ${x.alleRaeume.length ? `<dt>Raum</dt><dd>${e(x.alleRaeume.join(', '))}</dd>` : ''}</dl>
    <table class="pe-tabelle rb-ergebnis"><thead><tr><th>Datum</th><th>Zeit</th><th>Status</th></tr></thead>
      <tbody>${b.map(t => `<tr><td>${e(datumDe(t.datum))}</td><td>${e(zeit(t))}</td><td><span class="rb-status ${t.bestaetigt ? '' : 'tm-offen'}">${e(t.statusText)}</span></td></tr>`).join('')}</tbody></table>
    ${g.hinweise?.length ? `<p class="rb-leer">${e(g.hinweise.join(' '))}</p>` : ''}`,
    `<button type="button" class="pe-knopf" data-pdf>PDF</button><a class="pe-knopf" href="#/buchen/buchung/${x.id}">Bearbeiten</a><a class="pe-knopf" href="#/buchen/meine">Meine Buchungen</a>
     <button type="button" class="pe-knopf primaer" data-neu>Schließen</button>`);
  seite.querySelector('[data-pdf]').onclick = () => pdfLaden(x.id).catch(err => kontext.fehler(err));
  seite.querySelector('[data-neu]').onclick = neu;
}

// ------------------------------------------------------------------ Zusätzliche Stunden

/**
 * Zusätzliche Stunden zu einem Unterricht aus UntPlan (u{Nummer}), einem neuen Unterricht (e{Nummer}) bzw. einer Buchung (b{Nummer}):
 * „Zusätzliche Stunde: Zeitbereich“ (Tag bzw. erweiterte Suche mit Zeitraum, Beginn-/Endzeit, Räume, Konflikte zulassen) → Suchen →
 * „Zusätzliche Stunde“ (Auswahlkriterien, Tabelle der beteiligten Elemente je Tag, Beginnzeit anklicken) → Buchen.
 */
export async function zusaetzlicheStunden(seite, kontext, [quelleParam]) {
  stileLaden(kontext);
  const m = /^([ueb])(\d+)$/.exec(quelleParam ?? '');
  kontext.titel('Zusätzliche Stunde: Zeitbereich');
  if (!m) { seite.innerHTML = rahmen('Zusätzliche Stunde', '<p class="rb-leer">Wählen Sie den Unterricht unter Unterricht | Mein Unterricht bzw. die Buchung unter Meine Buchungen.</p>'); return; }
  const [opt, q, el] = await Promise.all([holen('/api/app/buchen/optionen'), holen(`/api/app/buchen/stundenquelle?${m[1]}=${m[2]}`), kontext.elemente()]);
  if (!opt.stundenplanaenderung) {
    seite.innerHTML = rahmen('Zusätzliche Stunde', '<p class="rb-leer">Sie benötigen dafür das Recht „Stundenplanänderung“. Wenden Sie sich für die Freigabe an Ihre Administration.</p>');
    return;
  }
  const raster = opt.zeitraster.filter(z => z.nr > 0 && z.beginn && z.ende);
  const heute = heuteIso();
  const start = opt.naechsterTag && opt.naechsterTag > heute ? opt.naechsterTag : heute;
  const z = { datum: start, bis: start, erweitert: false, stundeVon: raster[0]?.nr ?? 1, stundeBis: raster[0]?.nr ?? 1, raeume: q.raum ? [q.raum] : [],
    lehrerKonflikte: false, klassenKonflikte: false };
  const buchbar = opt.raeume.filter(r => r.buchbar);
  let schritt = 'zeit', v = null, tageWahl = new Set(), raumWahl = new Set(), gespeichert = null;
  const laenge = () => z.stundeBis - z.stundeVon;
  const kopf = () => `<dl class="rb-angaben tm-kopf"><dt>Klasse</dt><dd>${e(q.klassen.join(', ') || '–')}</dd><dt>Lehrkraft</dt><dd>${e(q.lehrer.join(', ') || '–')}</dd>
    <dt>Fach</dt><dd>${e(q.fach || '–')}</dd>${q.gruppe ? `<dt>Gruppe</dt><dd>${e(q.gruppe)}</dd>` : ''}</dl>`;
  const zeitVon = nr => minuten(raster.find(r => r.nr === nr)?.beginn);
  const zeitBis = nr => minuten(raster.find(r => r.nr === nr)?.ende);

  function zeichnen() { if (schritt === 'zeit') zeitZeichnen(); else if (schritt === 'ergebnis') ergebnisZeichnen(); else erfolgsansicht(seite, kontext, gespeichert, raster, () => { gespeichert = null; schritt = 'zeit'; zeichnen(); }); }

  function zeitZeichnen() {
    seite.innerHTML = rahmen('Zusätzliche Stunde: Zeitbereich', `
      ${kopf()}
      <h3 class="rb-abschnitt">Tag</h3>
      <div class="rb-zeile">
        ${pfeld(z.erweitert ? 'Von' : 'Datum', `<input type="date" class="form-control" data-z="datum" value="${z.datum}" min="${heute}">`)}
        ${z.erweitert ? pfeld('Bis', `<input type="date" class="form-control" data-z="bis" value="${z.bis}" min="${z.datum}">`) : ''}
        <button type="button" class="pe-knopf" data-erweitert>${z.erweitert ? '<< Einfache Suche' : 'Erweiterte Suche >>'}</button>
      </div>
      <h3 class="rb-abschnitt">Uhrzeit und Dauer</h3>
      <div class="rb-zeile">
        ${pfeld('Beginnzeit', `<select class="form-select" data-z="stundeVon">${raster.map(r => `<option value="${r.nr}" ${r.nr === z.stundeVon ? 'selected' : ''}>${e(r.beginn)}</option>`).join('')}</select>`)}
        ${pfeld('Endzeit', `<select class="form-select" data-z="stundeBis">${raster.map(r => `<option value="${r.nr}" ${r.nr === z.stundeBis ? 'selected' : ''}>${e(r.ende)}</option>`).join('')}</select>`)}
      </div>
      <div class="rb-zeile tm-abstand">${pfeld('Räume', `<select multiple class="form-select" data-raeume data-platzhalter="Bitte wählen Sie">${buchbar.map(r => `<option value="${e(r.kurz)}" ${z.raeume.includes(r.kurz) ? 'selected' : ''} data-zweit="${e(r.name)}">${e(r.kurz)}</option>`).join('')}</select>`)}</div>
      <div class="rb-haken tm-haken tm-abstand">
        ${haken('data-k="lehrerKonflikte"', 'Lehrkraftkonflikte zulassen', z.lehrerKonflikte)}
        ${haken('data-k="klassenKonflikte"', 'Klassenkonflikte zulassen', z.klassenKonflikte)}
      </div>`, '<button type="button" class="pe-knopf primaer" data-suchen>Suchen</button><button type="button" class="pe-knopf" data-abbrechen>Abbrechen</button>');
    seite.querySelectorAll('[data-z]').forEach(f => f.onchange = () => {
      const n = f.dataset.z;
      z[n] = n.startsWith('stunde') ? Number(f.value) : f.value;
      if (n === 'stundeVon' && z.stundeBis < z.stundeVon) z.stundeBis = z.stundeVon;
      if (n === 'stundeBis' && z.stundeBis < z.stundeVon) z.stundeVon = z.stundeBis;
      if (z.bis < z.datum || !z.erweitert) z.bis = z.datum;
      if (n.startsWith('stunde')) zeitZeichnen();
    });
    seite.querySelector('[data-erweitert]').onclick = () => { z.erweitert = !z.erweitert; z.bis = z.erweitert ? plusTage(z.datum, 6) : z.datum; zeitZeichnen(); };
    seite.querySelector('[data-raeume]').onchange = ev => { z.raeume = [...ev.target.selectedOptions].map(o => o.value); };
    seite.querySelectorAll('[data-k]').forEach(c => c.onchange = () => { z[c.dataset.k] = c.checked; });
    seite.querySelector('[data-abbrechen]').onclick = () => history.back();
    seite.querySelector('[data-suchen]').onclick = async ev => {
      const knopf = ev.currentTarget; knopf.disabled = true;
      try { await suchen(); schritt = 'ergebnis'; zeichnen(); }
      catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
  }

  async function suchen() {
    v = await senden('/api/app/buchen/verfuegbarkeit', { von: z.datum, bis: z.bis, lehrer: q.lehrer, klassen: q.klassen, raeume: z.raeume, ohneBuchung: null });
    raumWahl = new Set(z.raeume);
    tageWahl = new Set(v.tage.filter(t => !t.frei && t.datum >= heute && passt(t)).map(t => t.datum));
  }
  /** Ist der Tag zur gewählten Zeit für alle Elemente frei (zugelassene Konflikte zählen nicht)? */
  const status = (t, schluessel) => {
    if (t.frei) return 'frei-tag';
    const besetzt = ueberlappt(t.belegt[schluessel], zeitVon(z.stundeVon), zeitBis(z.stundeBis));
    if (!besetzt) return 'frei';
    if ((schluessel.startsWith('lehrer:') && z.lehrerKonflikte) || (schluessel.startsWith('klasse:') && z.klassenKonflikte)) return 'zugelassen';
    return 'belegt';
  };
  const passt = t => !t.frei && [...q.lehrer.map(l => 'lehrer:' + l), ...q.klassen.map(k => 'klasse:' + k), ...[...raumWahl].map(r => 'raum:' + r)].every(k => status(t, k) !== 'belegt');
  const zelle = (t, k) => {
    const s = status(t, k);
    return s === 'frei-tag' ? `<td class="rb-tag tm-feiertag" title="${e(t.grund || 'unterrichtsfrei')}">–</td>`
      : `<td class="rb-tag"><span class="tm-zelle ${s}">${s === 'frei' ? 'frei' : s === 'zugelassen' ? 'Konflikt' : 'belegt'}</span></td>`;
  };

  function ergebnisZeichnen() {
    const a = raster.find(r => r.nr === z.stundeVon), b = raster.find(r => r.nr === z.stundeBis);
    const tage = v.tage;
    const lehrerName = k => el.lehrer.find(l => l.kurz === k)?.name ?? '';
    const zeilen = [
      ...q.lehrer.map(l => ({ key: 'lehrer:' + l, name: l, lang: lehrerName(l), wahl: false })),
      ...q.klassen.map(k => ({ key: 'klasse:' + k, name: k, lang: `Klasse ${k}`, wahl: false })),
      ...z.raeume.map(r => ({ key: 'raum:' + r, name: r, lang: opt.raeume.find(x => x.kurz === r)?.name ?? '', wahl: true })),
    ];
    // Beginnzeiten des Rasters: grün, wenn an allen gewählten Tagen alle Elemente frei sind
    const beginnFrei = nr => {
      const bis = nr + laenge();
      if (!raster.some(r => r.nr === bis)) return false;
      const alt = [z.stundeVon, z.stundeBis];
      [z.stundeVon, z.stundeBis] = [nr, bis];
      const ok = tage.filter(t => !t.frei && t.datum >= heute).some(passt);
      [z.stundeVon, z.stundeBis] = alt;
      return ok;
    };
    seite.innerHTML = rahmen('Zusätzliche Stunde', `
      ${kopf()}
      <div class="rb-kriterienzeile"><span>Auswahlkriterien</span><b>${e(datumDe(z.datum))}${z.bis > z.datum ? ` – ${e(datumDe(z.bis))}` : ''}, ${e(a?.beginn ?? '')} - ${e(b?.ende ?? '')}</b>
        ${z.lehrerKonflikte ? '<small>Lehrkraftkonflikte zugelassen</small>' : ''}${z.klassenKonflikte ? '<small>Klassenkonflikte zugelassen</small>' : ''}</div>
      <h3 class="rb-abschnitt">Beginnzeit</h3>
      <div class="tm-beginn" role="radiogroup" aria-label="Beginnzeit">${raster.map(r => { const frei = beginnFrei(r.nr); return `<button type="button" role="radio" class="tm-beginnzeit ${r.nr === z.stundeVon ? 'an' : ''} ${frei ? 'frei' : 'belegt'}"
        aria-checked="${r.nr === z.stundeVon}" data-beginn="${r.nr}" ${raster.some(x => x.nr === r.nr + laenge()) ? '' : 'disabled'}><b>${e(r.beginn)}</b><small>${r.nr}. Std.</small></button>`; }).join('')}</div>
      <div class="rb-tabelle-huelle tm-abstand"><table class="pe-tabelle rb-tabelle"><thead>
        <tr><th>Auswahl</th><th>Name</th><th>Langname</th>${tage.map(t => `<th class="rb-tag" title="${e(t.wochentag)}">${e(tagPunkt(t.datum))}</th>`).join('')}</tr>
        <tr><th></th><th></th><th></th>${tage.map(t => `<th class="rb-tag">${t.frei || t.datum < heute ? '' : haken(`data-tag="${t.datum}"`, '', tageWahl.has(t.datum))}</th>`).join('')}</tr></thead><tbody>
        ${zeilen.map(x => `<tr><td>${x.wahl ? haken(`data-raum="${e(x.name)}"`, '', raumWahl.has(x.name)) : ''}</td><td><span class="rb-kurz">${e(x.name)}</span></td><td>${e(x.lang)}</td>
          ${tage.map(t => zelle(t, x.key)).join('')}</tr>`).join('')}
      </tbody></table></div>
      <div class="tm-legende"><span><i class="tm-farbe frei"></i>frei</span><span><i class="tm-farbe konflikt"></i>belegt</span><span><i class="tm-farbe reserv"></i>Konflikt zugelassen</span><span><i class="tm-farbe feiertag"></i>Feiertag / unterrichtsfrei</span></div>`,
      '<button type="button" class="pe-knopf primaer" data-buchen>Buchen</button><button type="button" class="pe-knopf" data-zurueck>Zurück</button>');
    seite.querySelectorAll('[data-beginn]').forEach(k => k.onclick = () => {
      const nr = Number(k.dataset.beginn);
      const n = laenge();
      z.stundeVon = nr; z.stundeBis = nr + n;
      tageWahl = new Set(tage.filter(t => !t.frei && t.datum >= heute && passt(t)).map(t => t.datum));
      ergebnisZeichnen();
    });
    seite.querySelectorAll('[data-tag]').forEach(c => c.onchange = () => { if (c.checked) tageWahl.add(c.dataset.tag); else tageWahl.delete(c.dataset.tag); });
    seite.querySelectorAll('[data-raum]').forEach(c => c.onchange = () => { if (c.checked) raumWahl.add(c.dataset.raum); else raumWahl.delete(c.dataset.raum); ergebnisZeichnen(); });
    seite.querySelector('[data-zurueck]').onclick = () => { schritt = 'zeit'; zeichnen(); };
    seite.querySelector('[data-buchen]').onclick = async ev => {
      const tageListe = tage.filter(t => tageWahl.has(t.datum));
      if (!tageListe.length) return kontext.fehler(new Error('Bitte mindestens einen Tag auswählen.'));
      const blockiert = tageListe.filter(t => !passt(t));
      if (blockiert.length) return kontext.fehler(new Error(`Am ${blockiert.map(t => datumDe(t.datum)).join(', ')} ist zur gewählten Zeit nicht alles frei.`));
      const knopf = ev.currentTarget; knopf.disabled = true;
      try {
        const raeume = [...raumWahl];
        gespeichert = await senden('/api/app/buchen', {
          art: q.quelle === 'b' ? 'aktivitaet' : 'unterricht',
          termine: tageListe.map(t => ({ datum: t.datum, stundeVon: z.stundeVon, stundeBis: z.stundeBis, raum: raeume[0] || null })), weitereRaeume: raeume.slice(1),
          ressourcen: [], unterrichtId: q.quelle === 'e' ? q.id : null, planUnterricht: q.quelle === 'u' ? q.id : null, quellBuchung: q.quelle === 'b' ? q.id : null,
          lehrerKonflikteZulassen: z.lehrerKonflikte, klassenKonflikteZulassen: z.klassenKonflikte, titel: q.titel,
        });
        toast(gespeichert.buchungen.length === 1 ? 'Zusätzliche Stunde gebucht.' : `${gespeichert.buchungen.length} zusätzliche Stunden gebucht.`);
        schritt = 'fertig'; zeichnen();
      } catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
  }
  zeichnen();
}

// ------------------------------------------------------------------ Terminsuche (Verabredungen)

export async function terminsuche(seite, kontext, [wocheParam]) {
  stileLaden(kontext);
  const [opt, el] = await Promise.all([holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const beschr = opt.beschraenkungen ?? { dauer: 45, beginn: '07:00', ende: '18:00' };
  const heute = heuteIso();
  const mo = montag(/^\d{4}-\d{2}-\d{2}$/.test(wocheParam ?? '') ? wocheParam : (opt.naechsterTag > heute ? opt.naechsterTag : heute));
  kontext.titel('Suche nach freien Terminen');
  const zustand = terminsucheZustand;
  zustand.dauer ||= beschr.dauer;
  const buchbar = opt.raeume.filter(r => r.buchbar);
  const zb = minuten(beschr.beginn), ze = minuten(beschr.ende);
  const schritt = 15;
  let v = null;

  const liste = (n, titel, eintraege) => `<label class="tm-liste"><span>${e(titel)}</span><select multiple size="10" class="form-select" data-nativ data-liste="${n}" aria-label="${e(titel)}">
    ${eintraege.map(([k, t]) => `<option value="${e(k)}" ${zustand[n].includes(k) ? 'selected' : ''} title="${e(t)}">${e(k)}</option>`).join('')}</select></label>`;
  seite.innerHTML = rahmen('Suche nach freien Terminen', `
    <div class="rb-zeile">
      ${pfeld('Dauer [Min.]', `<input type="number" min="5" max="600" step="5" class="form-control" data-dauer value="${zustand.dauer}">`)}
      ${haken('data-klassenlehrer', 'Lehrkraft der Klasse bei Klassenselektion selektieren', zustand.klassenlehrer)}
    </div>
    <div class="tm-listen">
      ${liste('lehrer', 'Lehrkraftliste', el.lehrer.map(l => [l.kurz, l.name]))}
      ${liste('klassen', 'Klassen', el.klassen.map(k => [k.kurz, k.name]))}
      ${liste('raeume', 'Räume', buchbar.map(r => [r.kurz, r.name]))}
    </div>
    <div class="tm-woche">
      <button type="button" class="pe-knopf klein" data-woche="-7" aria-label="Vorherige Woche"><i class="bi bi-chevron-left"></i></button>
      <b>${e(datumDe(mo))} – ${e(datumDe(plusTage(mo, 6)))}</b>
      <button type="button" class="pe-knopf klein" data-woche="7" aria-label="Nächste Woche"><i class="bi bi-chevron-right"></i></button>
    </div>
    <div data-raster>${rasterHtml()}</div>`, '<button type="button" class="pe-knopf primaer" data-suchen>Suchen</button>');

  function frei(t, a) {
    if (t.frei || t.datum < heute) return false;
    const b = a + zustand.dauer;
    if (b > ze) return false;
    const schluessel = [...zustand.lehrer.map(x => 'lehrer:' + x), ...zustand.klassen.map(x => 'klasse:' + x), ...zustand.raeume.map(x => 'raum:' + x)];
    return schluessel.every(k => !ueberlappt(t.belegt[k], a, b));
  }
  function rasterHtml() {
    const stunden = [];
    for (let h = Math.floor(zb / 60); h * 60 < ze; h++) stunden.push(h);
    const tage = v?.tage ?? Array.from({ length: 7 }, (_, i) => ({ datum: plusTage(mo, i), wochentag: WOCHENTAGE[i], frei: false, belegt: {} }));
    return `<div class="tm-raster-huelle"><table class="tm-raster"><thead><tr><th>Wochentag</th><th>Datum</th>${stunden.map(h => `<th colspan="${60 / schritt}">${String(h).padStart(2, '0')}:00</th>`).join('')}</tr></thead><tbody>
      ${tage.map(t => `<tr><th>${e(t.wochentag)}</th><th>${e(datumDe(t.datum).slice(0, 6))}</th>
        ${t.frei && v ? `<td colspan="${stunden.length * 60 / schritt}" class="tm-feiertag">${e(t.grund || 'unterrichtsfrei')}</td>`
          : stunden.flatMap(h => Array.from({ length: 60 / schritt }, (_, i) => h * 60 + i * schritt)).map(a => {
            const imBereich = a >= zb && a < ze;
            const gruen = v && imBereich && frei(t, a);
            return `<td class="${!imBereich ? 'aus' : gruen ? 'frei' : ''}" ${gruen ? `data-slot="${t.datum}|${a}" title="${e(hhmm(a))}" tabindex="0" role="button"` : ''}></td>`;
          }).join('')}</tr>`).join('')}
    </tbody></table></div>
    ${v ? '<div class="tm-legende"><span><i class="tm-farbe frei"></i>frei</span><span><i class="tm-farbe feiertag"></i>Feiertag / Ferien / unterrichtsfrei</span></div>' : ''}`;
  }
  function rasterBinden() {
    seite.querySelectorAll('[data-slot]').forEach(c => {
      const los = () => { const [datum, a] = c.dataset.slot.split('|'); veranstaltung(datum, Number(a)); };
      c.onclick = los;
      c.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); los(); } };
    });
  }
  async function suchen() {
    if (!zustand.lehrer.length && !zustand.klassen.length && !zustand.raeume.length) throw new Error('Bitte mindestens eine Lehrkraft, Klasse oder einen Raum wählen.');
    v = await senden('/api/app/buchen/verfuegbarkeit', { von: mo, bis: plusTage(mo, 6), lehrer: zustand.lehrer, klassen: zustand.klassen, raeume: zustand.raeume });
    seite.querySelector('[data-raster]').innerHTML = rasterHtml();
    rasterBinden();
  }
  seite.querySelectorAll('[data-liste]').forEach(s => s.onchange = () => {
    const n = s.dataset.liste;
    zustand[n] = [...s.selectedOptions].map(o => o.value);
    if (n === 'klassen' && zustand.klassenlehrer) {
      const leitung = el.klassen.filter(k => zustand.klassen.includes(k.kurz)).flatMap(k => k.leitung ?? []);
      zustand.lehrer = [...new Set([...zustand.lehrer, ...leitung])];
      seite.querySelectorAll('[data-liste=lehrer] option').forEach(o => { o.selected = zustand.lehrer.includes(o.value); });
    }
  });
  seite.querySelector('[data-dauer]').onchange = ev => { zustand.dauer = Math.max(5, Number(ev.target.value) || beschr.dauer); if (v) { seite.querySelector('[data-raster]').innerHTML = rasterHtml(); rasterBinden(); } };
  seite.querySelector('[data-klassenlehrer]').onchange = ev => { zustand.klassenlehrer = ev.target.checked; };
  seite.querySelectorAll('[data-woche]').forEach(b => b.onclick = () => kontext.navigieren(`#/buchen/terminsuche/${plusTage(mo, Number(b.dataset.woche))}`));
  seite.querySelector('[data-suchen]').onclick = async ev => {
    const knopf = ev.currentTarget; knopf.disabled = true;
    try { await suchen(); } catch (err) { kontext.fehler(err); } finally { knopf.disabled = false; }
  };
  if (zustand.lehrer.length || zustand.klassen.length || zustand.raeume.length) suchen().catch(() => {});

  /** Dialog „Veranstaltung am …“: Zusatzinformationen zur Verabredung → Buchen. */
  function veranstaltung(datum, a) {
    const t = v.tage.find(x => x.datum === datum);
    // Beginnzeiten im freien Abschnitt um den angeklickten Zeitpunkt (5-Minuten-Schritte)
    const beginne = [];
    for (let x = Math.max(zb, a - 120); x <= Math.min(ze - zustand.dauer, a + 240); x += 5) if (frei(t, x)) beginne.push(x);
    const zu = { verantwortlich: opt.ich || zustand.lehrer[0] || '', emailBenachrichtigung: false };
    const zf = zusatzFelder(zu, el, opt);
    const w = { beginn: a, dauer: zustand.dauer, fach: '', benachrichtigen: false, sofort: !zustand.raeume.some(r => opt.raeume.find(x => x.kurz === r)?.nurReservieren) || opt.admin };
    const box = kbDialog({
      titel: `Veranstaltung am ${datumDe(datum)}`, breite: 'm', klasse: 'tm-dialog',
      inhalt: `<div class="tm">
        <dl class="rb-angaben"><dt>Klasse</dt><dd>${e(zustand.klassen.join(', ') || '–')}</dd><dt>Lehrkraft</dt><dd>${e(zustand.lehrer.join(', ') || '–')}</dd><dt>Raum</dt><dd>${e(zustand.raeume.join(', ') || '–')}</dd></dl>
        ${maske([
          [pfeld('Beginnzeit', `<select class="form-select" data-w="beginn">${beginne.map(x => `<option value="${x}" ${x === a ? 'selected' : ''}>${hhmm(x)}</option>`).join('')}</select>`),
            pfeld('Dauer [Min.]', `<input type="number" min="5" step="5" class="form-control" data-w="dauer" value="${w.dauer}">`)],
          [pfeld('Fach', `<select class="form-select" data-w="fach">${auswahl(el.faecher.map(f => [f.kurz, f.kurz, f.name]), '')}</select>`)],
          [zf.abteilung, zf.verantwortlich],
          [zf.art],
          ...textFelder(zu),
          `<div class="rb-haken tm-haken">
            ${haken('data-w="benachrichtigen"', 'Lehrkraft der Aktivität benachrichtigen', false)}
            ${haken('data-zu="emailBenachrichtigung"', 'E-Mail-Benachrichtigung bei Statusänderung', false)}
            ${haken(`data-w="sofort" ${opt.admin ? '' : 'disabled'}`, 'Buchung sofort bestätigen', w.sofort)}
          </div>`])}</div>`,
      // wie in der Vorlage: Buchen links, Abbrechen rechts daneben
      fuss: '<button type="button" class="nl-knopf nw-primaer" data-buchen>Buchen</button><button type="button" class="nl-knopf" data-kbd-zu>Abbrechen</button>',
    });
    zusatzBinden(box, zu);
    box.querySelectorAll('[data-w]').forEach(f => f.onchange = () => { const n = f.dataset.w; w[n] = f.type === 'checkbox' ? f.checked : n === 'fach' ? f.value : Number(f.value); });
    box.querySelector('[data-buchen]').onclick = async ev => {
      const knopf = ev.currentTarget; knopf.disabled = true;
      try {
        const beginn = hhmm(w.beginn), ende = hhmm(w.beginn + Math.max(5, w.dauer));
        if (!frei(t, w.beginn) && w.dauer === zustand.dauer) throw new Error('Zu dieser Zeit sind nicht alle Elemente frei.');
        const tt = await senden('/api/app/buchen/termine', { wiederholung: 'einmalig', datum, beginn, ende });
        const x = tt.termine[0];
        if (!x) throw new Error('An diesem Tag kann nicht gebucht werden.');
        const g = await senden('/api/app/buchen', {
          art: 'aktivitaet', termine: [{ datum, stundeVon: x.stundeVon, stundeBis: x.stundeBis, raum: zustand.raeume[0] || null }], weitereRaeume: zustand.raeume.slice(1),
          beginn, ende, ressourcen: [], klassen: zustand.klassen, lehrer: zustand.lehrer[0] || '', weitereLehrer: zustand.lehrer.slice(1), fach: w.fach,
          titel: (zu.bemerkung || '').slice(0, 200) || 'Verabredung', text: zu.bemerkung || '', zusatz: zusatzDaten(zu), benachrichtigen: w.benachrichtigen && zustand.lehrer.length > 0,
          sofortBestaetigen: w.sofort,
        });
        box.schliessen();
        toast(`Verabredung gebucht (Buchung ${g.buchungen[0].id}).${g.hinweise?.length ? ' ' + g.hinweise.join(' ') : ''}`);
        await suchen();
      } catch (err) { kontext.fehler(err); knopf.disabled = false; }
    };
  }
}
/** Auswahl der Terminsuche (bleibt während der Sitzung erhalten). */
const terminsucheZustand = { lehrer: [], klassen: [], raeume: [], dauer: 0, klassenlehrer: false };

// ------------------------------------------------------------------ Buchung bearbeiten (Meine Buchungen | Bearbeiten)

export async function buchungBearbeiten(seite, kontext, [idParam]) {
  stileLaden(kontext);
  const id = Number(idParam);
  const [x, opt, el] = await Promise.all([holen(`/api/app/buchen/${id}/details`), holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const b = x.buchung;
  const raster = opt.zeitraster.filter(z => z.nr > 0);
  const heute = heuteIso();
  kontext.titel(`Buchung ${b.id} - ${b.artName}`);
  const d = { lehrer: b.lehrer, weitereLehrer: [...b.weitereLehrer], klassen: [...b.klassen], fach: b.fach, titel: b.titel, text: b.text, zu: { ...b.zusatz, bemerkung: b.text } };
  const schueler = new Set(x.schueler.filter(s => s.gewaehlt).map(s => s.id));
  const zeit = t => t.beginn ? `${t.beginn} - ${t.ende}` : `${t.stundeVon}.–${t.stundeBis}. Std.`;
  const aktiv = x.termine.filter(t => t.status === 'aktiv');
  const ressource = b.art === 'ressource';
  const zf = zusatzFelder(d.zu, el, opt);
  const aus = x.darf ? '' : 'disabled';
  const raumSpalte = b.alleRaeume.join(', ') || 'Raum';
  // Maske wie in der Vorlage: „Buchen“, Status, Termine (Bearbeiten/Stornieren | Datum | Zeit | Raum mit Status), „Zusätzliche Stunden“,
  // Aufgegeben/Geändert/Bestätigt am … von …, darunter dieselben Felder wie bei „Neue Aktivität“
  seite.innerHTML = rahmen(`Buchung ${b.id} - ${b.artName}`, `
    <h3 class="rb-abschnitt">Buchen</h3>
    <p class="tm-status">Status: <b>${e(b.statusText)}</b></p>
    <table class="pe-tabelle rb-ergebnis"><thead><tr><th></th><th>Datum</th><th>Zeit</th><th>${e(raumSpalte)}</th></tr></thead><tbody>
      ${x.termine.map(t => `<tr class="${t.status === 'storniert' ? 'tm-storniert' : ''}"><td class="text-nowrap">${x.darf && t.status === 'aktiv' ? '<button type="button" class="nl-icon sichtbar" data-zu-details title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>' : ''}
          ${x.darf && t.status === 'aktiv' && (t.datum >= heute || opt.admin)
          ? `<button type="button" class="nl-icon sichtbar rot" data-storno="${t.id}" title="Termin stornieren" aria-label="Termin stornieren"><i class="bi bi-x-lg"></i></button>` : ''}
          ${x.bestaetigen && t.status === 'aktiv' && !t.bestaetigt ? `<button type="button" class="nl-icon" data-bestaetigen="${t.id}" title="Bestätigen" aria-label="Bestätigen"><i class="bi bi-check2-circle"></i></button>` : ''}</td>
        <td>${e(datumDe(t.datum))}</td><td>${e(zeit(t))}</td><td><span class="rb-status ${t.status !== 'aktiv' ? 'tm-aus' : t.bestaetigt ? '' : 'tm-offen'}">${e(t.statusText)}</span></td></tr>`).join('')}
    </tbody></table>
    <div class="tm-knoepfe">
      ${!ressource && x.stundenplanaenderung && x.darf ? `<a class="pe-knopf" href="#/buchen/zusatz/b${b.id}"><i class="bi bi-calendar-plus me-2"></i>Zusätzliche Stunden</a>` : ''}
      ${x.darf && aktiv.length ? '<button type="button" class="pe-knopf rot" data-storno-alle>Buchung stornieren</button>' : ''}
      ${x.bestaetigen && aktiv.some(t => !t.bestaetigt) ? '<button type="button" class="pe-knopf" data-bestaetigen-alle>Alle Termine bestätigen</button>' : ''}
    </div>
    <dl class="rb-angaben tm-abstand"><dt>Aufgegeben am</dt><dd>${e(zeitstempel(b.erstellt))} von ${e(b.absender)}</dd>
      ${b.geaendert ? `<dt>Geändert am</dt><dd>${e(zeitstempel(b.geaendert))} von ${e(b.geaendertVon)}</dd>` : ''}
      ${b.bestaetigt ? `<dt>Bestätigt am</dt><dd>${e(zeitstempel(b.bestaetigt))}${b.bestaetigtVon ? ` von ${e(b.bestaetigtVon)}` : ''}</dd>` : ''}
      ${b.uebertragen ? `<dt>Übernommen</dt><dd>${e(b.uebertragen)} von UntPlan</dd>` : ''}</dl>
    ${ressource ? '' : `<div id="tm-details" data-details>${maske([
      [pfeld('Lehrkraft', `<select class="form-select" data-d="lehrer" ${aus}>${auswahl(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), d.lehrer, 'keine')}</select>`),
        pfeld('Klasse', `<select multiple class="form-select" data-d="klassen" data-platzhalter="Bitte wählen Sie" ${aus}>${el.klassen.map(k => `<option value="${e(k.kurz)}" ${d.klassen.includes(k.kurz) ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)],
      [pfeld('Fach', `<select class="form-select" data-d="fach" ${aus}>${auswahl(el.faecher.map(f => [f.kurz, f.kurz, f.name]), d.fach)}</select>`)],
      [zf.abteilung],
      [zf.menge, zf.verantwortlich],
      [zf.art],
      [zf.teilnehmer, zf.kostenstelle],
      ...textFelder(d.zu),
      `<div class="rb-haken">${haken('data-zu="emailBenachrichtigung"', 'E-Mail-Benachrichtigung bei Statusänderung', d.zu.emailBenachrichtigung)}</div>`,
    ])}</div>
    ${x.darf ? `<div class="tm-knoepfe">${haken('data-serie', `Für alle kommenden Termine der Serie übernehmen`, true)}<button type="button" class="pe-knopf primaer" data-speichern>Speichern</button></div>` : ''}`}
    ${ressource ? '' : `<h3 class="rb-abschnitt">Schüler*innen</h3>
    ${x.schueler.length ? `<div class="tm-schueler">${x.schueler.map(s => haken(`data-schueler="${s.id}"`, `${e(s.name)} <small class="nl-unter">${e(s.klasse)}</small>`, s.gewaehlt)).join('')}</div>
      <div class="tm-knoepfe"><span class="nl-unter" data-anzahl>${schueler.size} ausgewählt</span><button type="button" class="pe-knopf" data-alle-schueler>Alle</button>
        <button type="button" class="pe-knopf primaer" data-schueler-speichern>Schüler*innen speichern</button></div>` : '<p class="rb-leer">Keine Schüler*innen verfügbar.</p>'}`}
    <h3 class="rb-abschnitt">Berichte</h3>
    <div class="tm-knoepfe"><button type="button" class="pe-knopf" data-pdf><i class="bi bi-filetype-pdf me-2"></i>Buchungsbeleg (PDF)</button>
      ${ressource ? '' : '<button type="button" class="pe-knopf" data-teilnehmer><i class="bi bi-filetype-csv me-2"></i>Schülerliste (CSV)</button>'}
      <button type="button" class="pe-knopf" data-termine-csv><i class="bi bi-filetype-csv me-2"></i>Terminliste (CSV)</button></div>`,
    '<button type="button" class="pe-knopf" data-zurueck>Zurück</button>');

  const neuLaden = () => kontext.navigieren(`#/buchen/buchung/${id}`, true);
  seite.querySelector('[data-zurueck]').onclick = () => kontext.navigieren('#/buchen/meine');
  seite.querySelectorAll('[data-zu-details]').forEach(k => k.onclick = () => seite.querySelector('[data-details]')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  seite.querySelectorAll('[data-storno]').forEach(k => k.onclick = async () => {
    if (!await bestaetigen('Termin stornieren', 'Diesen Termin der Buchung stornieren?', 'Stornieren')) return;
    try { await api(`/api/app/buchen/${k.dataset.storno}`, { methode: 'DELETE' }); toast('Storniert.'); neuLaden(); } catch (err) { kontext.fehler(err); }
  });
  seite.querySelector('[data-storno-alle]')?.addEventListener('click', async () => {
    if (!await bestaetigen('Buchung stornieren', aktiv.length > 1 ? `Die gesamte Buchung (${aktiv.length} Termine, vergangene ausgenommen) stornieren?` : 'Die Buchung stornieren?', 'Stornieren')) return;
    try {
      const erster = aktiv.find(t => t.datum >= heute) ?? aktiv[0];
      const r = await api(`/api/app/buchen/${erster.id}?serie=true`, { methode: 'DELETE' });
      toast(`${r.storniert} ${r.storniert === 1 ? 'Termin' : 'Termine'} storniert.`); neuLaden();
    } catch (err) { kontext.fehler(err); }
  });
  seite.querySelectorAll('[data-bestaetigen]').forEach(k => k.onclick = async () => {
    try { await senden(`/api/app/buchen/${k.dataset.bestaetigen}/bestaetigen`, {}); toast('Bestätigt.'); neuLaden(); } catch (err) { kontext.fehler(err); }
  });
  seite.querySelector('[data-bestaetigen-alle]')?.addEventListener('click', async () => {
    try { const r = await senden(`/api/app/buchen/${b.id}/bestaetigen?serie=true`, {}); toast(`${r.bestaetigt} Termine bestätigt.`); neuLaden(); } catch (err) { kontext.fehler(err); }
  });
  const details = seite.querySelector('[data-details]');
  if (details) {
    details.querySelectorAll('[data-d]').forEach(f => f.addEventListener(f.tagName === 'SELECT' ? 'change' : 'input', () => {
      d[f.dataset.d] = f.multiple ? [...f.selectedOptions].map(o => o.value) : f.value;
    }));
    zusatzBinden(details, d.zu);
  }
  seite.querySelector('[data-speichern]')?.addEventListener('click', async ev => {
    const knopf = ev.currentTarget; knopf.disabled = true;
    try {
      await senden(`/api/app/buchen/${b.id}`, { klassen: d.klassen, lehrer: d.lehrer, weitereLehrer: d.weitereLehrer, fach: d.fach, titel: d.titel, text: d.zu.bemerkung ?? '',
        zusatz: zusatzDaten(d.zu), serie: seite.querySelector('[data-serie]')?.checked ?? true }, 'PUT');
      toast('Gespeichert.'); neuLaden();
    } catch (err) { kontext.fehler(err); knopf.disabled = false; }
  });
  seite.querySelectorAll('[data-schueler]').forEach(c => c.onchange = () => {
    if (c.checked) schueler.add(Number(c.dataset.schueler)); else schueler.delete(Number(c.dataset.schueler));
    seite.querySelector('[data-anzahl]').textContent = `${schueler.size} ausgewählt`;
  });
  seite.querySelector('[data-alle-schueler]')?.addEventListener('click', () => {
    seite.querySelectorAll('[data-schueler]').forEach(c => { c.checked = true; schueler.add(Number(c.dataset.schueler)); });
    seite.querySelector('[data-anzahl]').textContent = `${schueler.size} ausgewählt`;
  });
  seite.querySelector('[data-schueler-speichern]')?.addEventListener('click', async () => {
    try { const r = await senden(`/api/app/buchen/${b.id}/schueler`, { ids: [...schueler] }, 'PUT'); toast(`${r.schueler.length} Schüler*innen in der Schülergruppe.`); }
    catch (err) { kontext.fehler(err); }
  });
  seite.querySelector('[data-pdf]').onclick = () => pdfLaden(b.id).catch(err => kontext.fehler(err));
  const csv = zeilen => zeilen.map(z => z.map(w => `"${String(w ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');
  seite.querySelector('[data-teilnehmer]')?.addEventListener('click', () => herunterladen(`buchung-${b.id}-schueler.csv`,
    csv([['Name', 'Klasse'], ...x.schueler.filter(s => schueler.has(s.id)).map(s => [s.name, s.klasse])])));
  seite.querySelector('[data-termine-csv]').onclick = () => herunterladen(`buchung-${b.id}-termine.csv`,
    csv([['Datum', 'Zeit', 'Räume', 'Status'], ...x.termine.map(t => [datumDe(t.datum), zeit(t), t.alleRaeume.join(', '), t.statusText])]));
  void raster;
}

// ------------------------------------------------------------------ Neuer Unterricht (Unterricht | Mein Unterricht | Neuer Unterricht)

export async function neuerUnterricht(seite, kontext) {
  stileLaden(kontext);
  const [opt, el] = await Promise.all([holen('/api/app/buchen/optionen'), kontext.elemente()]);
  const ich = kontext.ich;
  const heute = heuteIso();
  kontext.titel('Neuer Unterricht');
  const d = { lehrer: ich.element || opt.ich || '', klassen: [], fach: '', von: heute, bis: kontext.schuljahr?.bis || plusTage(heute, 120), wochenstunden: 0, jahresstunden: 0,
    benachrichtigen: true, zu: { verantwortlich: ich.element || '', aktivitaetsart: 'Unterricht', emailBenachrichtigung: true } };
  const zf = zusatzFelder(d.zu, el, opt);
  // Maske wie in der Vorlage: Lehrkraft | Klasse, Fach, Von - Bis, Wochenstunden | Jahresstunden, Abteilung, Verantwortlicher, Art,
  // Bemerkung, Anmerkung, E-Mail Adresse, Benachrichtigungen → Speichern, Zurück
  seite.innerHTML = rahmen('Neuer Unterricht', `
    <h3 class="rb-abschnitt">Buchen</h3>
    ${maske([
      [pfeld('Lehrkraft', `<select class="form-select" data-d="lehrer">${auswahl(el.lehrer.map(l => [l.kurz, l.kurz, l.name]), d.lehrer, 'Bitte wählen Sie')}</select>`),
        pfeld('Klasse', `<select multiple class="form-select" data-d="klassen" data-platzhalter="Bitte wählen Sie">${el.klassen.map(k => `<option value="${e(k.kurz)}">${e(k.kurz)}</option>`).join('')}</select>`)],
      [pfeld('Fach', `<select class="form-select" data-d="fach">${auswahl(el.faecher.map(f => [f.kurz, f.kurz, f.name]), '')}</select>`)],
      vonBis(`<input type="date" class="form-control" data-d="von" value="${d.von}">`, `<input type="date" class="form-control" data-d="bis" value="${d.bis}" aria-label="Bis">`),
      [pfeld('Wochenstunden', `<input type="number" min="0" max="40" class="form-control" data-d="wochenstunden" value="0">`),
        pfeld('Jahresstunden', `<input type="number" min="0" max="2000" class="form-control" data-d="jahresstunden" value="0">`)],
      [zf.abteilung],
      [zf.verantwortlich],
      [zf.art],
      ...textFelder(d.zu),
      `<div class="rb-haken tm-haken">
        ${haken('data-zu="emailBenachrichtigung"', 'E-Mail-Benachrichtigung bei Statusänderung', true)}
        ${haken('data-d="benachrichtigen"', 'Lehrkraft der Aktivität benachrichtigen', true)}
      </div>`])}`, '<button type="button" class="pe-knopf primaer" data-speichern>Speichern</button><button type="button" class="pe-knopf" data-zurueck>Zurück</button>');
  seite.querySelectorAll('[data-d]').forEach(f => f.addEventListener('change', () => {
    const n = f.dataset.d;
    d[n] = f.multiple ? [...f.selectedOptions].map(o => o.value) : f.type === 'checkbox' ? f.checked : f.type === 'number' ? Number(f.value) || 0 : f.value;
  }));
  zusatzBinden(seite, d.zu);
  seite.querySelector('[data-zurueck]').onclick = () => history.back();
  seite.querySelector('[data-speichern]').onclick = async ev => {
    const knopf = ev.currentTarget; knopf.disabled = true;
    try {
      const u = await senden('/api/app/buchen/unterricht', { lehrer: d.lehrer || null, fach: d.fach, klassen: d.klassen, gruppe: d.gruppe || '', text: d.zu.bemerkung || '',
        wochenstunden: d.wochenstunden, benachrichtigen: d.benachrichtigen,
        zusatz: { von: d.von, bis: d.bis, jahresstunden: d.jahresstunden, abteilung: d.zu.abteilung || '', verantwortlich: d.zu.verantwortlich || '', aktivitaetsart: d.zu.aktivitaetsart || '',
          anmerkung: d.zu.anmerkung || '', email: d.zu.email || '', emailBenachrichtigung: !!d.zu.emailBenachrichtigung } });
      toast('Unterricht angelegt. Planen Sie jetzt die zusätzlichen Stunden ein.');
      kontext.navigieren(`#/buchen/zusatz/e${u.id}`);
    } catch (err) { kontext.fehler(err); knopf.disabled = false; }
  };
}

// ------------------------------------------------------------------ Administration | Buchungsbeschränkungen

export async function beschraenkungen(seite, kontext) {
  stileLaden(kontext);
  kontext.titel('Buchungsbeschränkungen');
  const x = await holen('/api/app/buchen/beschraenkungen');
  const darf = kontext.ich.admin;
  seite.innerHTML = rahmen('Buchungsbeschränkungen', `
    <div class="rb-formular">
      ${pfeld('Standarddauer einer Verabredung [Min.]', `<input type="number" min="5" max="600" step="5" class="form-control" name="dauer" value="${x.dauer}" ${darf ? '' : 'disabled'}>`)}
      <div class="rb-zeile tm-breit">${pfeld('Verabredungen ab', `<input type="time" class="form-control" name="beginn" value="${e(x.beginn)}" step="300" ${darf ? '' : 'disabled'}>`)}
        ${pfeld('Verabredungen bis', `<input type="time" class="form-control" name="ende" value="${e(x.ende)}" step="300" ${darf ? '' : 'disabled'}>`)}</div>
    </div>`, darf ? '<button type="button" class="pe-knopf primaer" data-speichern>Speichern</button>' : '');
  seite.querySelector('[data-speichern]')?.addEventListener('click', async () => {
    const w = n => seite.querySelector(`[name=${n}]`).value;
    try { await senden('/api/app/buchen/beschraenkungen', { dauer: Number(w('dauer')) || 45, beginn: w('beginn'), ende: w('ende'), nurUnterrichtstage: true }, 'PUT'); toast('Gespeichert.'); }
    catch (err) { kontext.fehler(err); }
  });
}
