// Modul „UntPlanWeb“ (Desktop: Fenster „UntPlanWeb“, „UntPlanWeb – Einstellungen“, „Stundenplanänderungen aus UntPlanWeb“,
// Klausur-Dialog): Export von Stammdaten, Stundenplan* und Vertretungen* mit Fortschritt, Import von Stpl.-Änderungen*,
// Absenzen*, Studenten* und Kurse, Anfangsdatum, Verbindungseinstellungen, Klausuren, importierte Listen und der optionale
// automatische Abgleich. Die Logik kommt unverändert aus UntPlan.Core (gleich wie im Desktop).
import { h } from '../kern/dom.js';
import { symbol } from '../kern/symbole.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { dialog, frage, hinweis, laden, fehlerMelden, meldung } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { dateiHerunterladen } from './austausch.js';

const TEILE = { stammdaten: 'stammdaten', unterricht: 'unterricht', vertretungen: 'vertretungen' };
const pause = (ms) => new Promise((r) => setTimeout(r, ms));
const iso = (d) => (typeof d === 'string' ? d.slice(0, 10) : d.toISOString().slice(0, 10));
const heute = () => iso(new Date());
const plusTage = (datum, n) => { const d = new Date(`${datum}T12:00:00`); d.setDate(d.getDate() + n); return iso(d); };

/** Auftrag bis zum Ende verfolgen; fortschritt(wert, text) wird laufend aufgerufen. */
async function verfolgen(auftrag, fortschritt) {
  let a = auftrag;
  while (!a.fertig) {
    fortschritt(a.wert, a.text);
    await pause(250);
    a = await api.get(api.dok(`/untplanweb/auftrag/${a.id}`));
  }
  fortschritt(a.wert, a.text);
  return a;
}

// ---------------------------------------------------------------------------------------------- Einstellungen

async function einstellungenDialog() {
  const e = await api.get(api.dok('/untplanweb/einstellungen'));
  const feld = (titel, el, hinweisText) => [h('label', null, titel), el, hinweisText ? h('div', { class: 'hinweis' }, hinweisText) : null];
  const schule = h('input', { class: 'eingabe', value: e.schulId });
  const server = h('input', { class: 'eingabe', value: e.server, placeholder: 'z. B. web.meine-schule.de' });
  const port = h('input', { class: 'eingabe', type: 'number', min: 1, max: 65535, value: e.port, style: { width: '90px' } });
  const https = h('input', { type: 'checkbox', checked: e.https });
  https.addEventListener('change', () => { if (https.checked && port.value === '80') port.value = '443'; else if (!https.checked && port.value === '443') port.value = '80'; });
  const schluessel = h('input', { class: 'eingabe', type: 'password', autocomplete: 'new-password', placeholder: e.apiSchluesselGesetzt ? '•••••••• (unverändert)' : '' });
  const tage = h('input', { class: 'eingabe', type: 'number', min: 1, max: 366, value: e.tageVoraus, style: { width: '90px' } });
  const unbegrenzt = h('input', { type: 'checkbox', checked: e.unbegrenzt });
  const sync = () => { tage.disabled = unbegrenzt.checked; };
  unbegrenzt.addEventListener('change', sync);
  sync();
  const option = (titel, wert) => { const c = h('input', { type: 'checkbox', checked: wert }); return { c, el: h('label', { class: 'kontrolle' }, c, titel) }; };
  const o = {
    ignoriert: option('Ignorierten Unterricht übertragen', e.ignoriertenUnterricht),
    ferien: option('Ferien nicht als Stammdaten exportieren', e.ferienNichtExportieren),
    email: option('E-Mail-Adressen der Lehrkräfte übertragen', e.lehrerEmail),
    absenzen: option('Lehrer-Absenzen übertragen (ohne Grund)', e.lehrerAbsenzen),
    gruende: option('Absenzgründe übertragen (außer vertrauliche)', e.absenzgruende),
    schueler: option('Schülerliste übertragen (Klassenbuch, Fehlzeiten)', e.schueler),
  };
  const ergebnis = h('div', { class: 'upw-pruefung leise' });
  const daten = () => ({
    schulId: schule.value.trim(), server: server.value.trim(), port: Number(port.value) || (https.checked ? 443 : 80), https: https.checked, pfad: e.pfad,
    apiSchluessel: schluessel.value.trim() || null, tageVoraus: Number(tage.value) || 7, unbegrenzt: unbegrenzt.checked,
    ignoriertenUnterricht: o.ignoriert.c.checked, ferienNichtExportieren: o.ferien.c.checked, lehrerEmail: o.email.c.checked,
    lehrerAbsenzen: o.absenzen.c.checked, absenzgruende: o.gruende.c.checked, schueler: o.schueler.c.checked,
  });
  const pruefen = h('button', { class: 'knopf', type: 'button', onclick: async () => {
    ergebnis.className = 'upw-pruefung leise';
    ergebnis.textContent = 'Verbindung wird geprüft …';
    pruefen.disabled = true;
    try {
      const r = await api.post(api.dok('/untplanweb/pruefen'), daten());
      ergebnis.textContent = r.text;
      ergebnis.className = `upw-pruefung ${r.ok ? 'ok' : 'fehler'}`;
    } catch (f) { ergebnis.textContent = f.message; ergebnis.className = 'upw-pruefung fehler'; } finally { pruefen.disabled = false; }
  } }, symbol('web'), 'Verbindung prüfen');
  const inhalt = h('div', { class: 'upw-einstellungen' },
    h('fieldset', null, h('legend', null, 'Verbindung'), h('div', { class: 'formular' },
      ...feld('Schul-ID', schule), ...feld('Server', server),
      ...feld('Port', h('div', { class: 'upw-zeile' }, port, h('label', { class: 'kontrolle' }, https, 'https'))),
      ...feld('API-Schlüssel', schluessel),
      ...feld('Tage voraus', h('div', { class: 'upw-zeile' }, tage, h('label', { class: 'kontrolle', title: 'Keine Tagesbegrenzung: Unterricht und Vertretungen bis zum Schuljahresende übertragen' }, unbegrenzt, 'unbegrenzt')))),
    h('div', { class: 'upw-zeile' }, pruefen), ergebnis),
    h('fieldset', null, h('legend', null, 'Exportoptionen'), ...Object.values(o).map((x) => x.el),
      h('p', { class: 'leise' }, 'Personenbezogene Daten werden nur übertragen, wenn sie hier ausdrücklich freigegeben sind.')));
  const ok = await dialog({ titel: 'UntPlanWeb – Einstellungen', inhalt, breite: '720px', knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
  if (!ok) return false;
  await api.put(api.dok('/untplanweb/einstellungen'), daten());
  hinweis('UntPlanWeb-Einstellungen gespeichert.', 'ok');
  return true;
}

// ---------------------------------------------------------------------------------------------- Hauptfenster

function hauptFenster(f) {
  f.titel('UntPlanWeb');
  const schreiben = darfSchreiben();
  let stand = null;
  let laeuft = false;
  let schulnameServer = null;

  const exportStatus = h('div', { class: 'upw-exportstatus' });
  const kopfRechts = h('fieldset', { class: 'upw-kopf' });
  const einstellungen = h('button', { class: 'knopf upw-gruen', type: 'button', title: 'Verbindung zu UntPlanWeb (Server, Port, Schul-ID, API-Schlüssel) und Exportoptionen', disabled: !schreiben,
    onclick: async () => { if (!laeuft && await einstellungenDialog().catch(fehlerMelden)) { schulnameServer = null; fehlerZeigen(null); await neu(true); } } }, 'Einstellungen');
  const kopfLinks = h('fieldset', { class: 'upw-kopf' }, einstellungen, exportStatus);

  const anfangAktiv = h('input', { type: 'checkbox', disabled: !schreiben });
  const datumWahl = h('select', { class: 'eingabe', title: 'Anfangsdatum aus Vorgaben wählen (Tagesdatum, nächster Montag, Schuljahresbeginn, Periodenbeginn)' });
  const anfang = h('input', { class: 'eingabe', type: 'date' });
  const zeilenEl = h('div', { class: 'upw-zeilen' });
  const statusZeile = h('div', { class: 'upw-status', hidden: true });
  const abgleichEl = h('fieldset', { class: 'upw-abgleich' });

  f.inhalt.append(h('div', { class: 'upw-fenster' },
    h('div', { class: 'upw-oben' }, kopfLinks, kopfRechts),
    h('fieldset', { class: 'upw-gruppe' }, h('legend', null, 'UntPlanWeb Import/Export'),
      h('div', { class: 'upw-anfang' }, h('label', { class: 'kontrolle' }, anfangAktiv, 'Anfangsdatum für Daten, die mit * markiert sind, aktivieren.'), datumWahl, anfang),
      zeilenEl, statusZeile),
    abgleichEl));

  f.werkzeug({ text: 'Stundenplanänderungen', symbol: 'stundenplan', titel: 'Stundenplanänderungen aus UntPlanWeb prüfen und übernehmen', aktion: () => fensterverwaltung.oeffnen('untplanweb.stpl', {}) });
  f.werkzeug({ text: 'Klausuren', symbol: 'kalender', titel: 'Klausuren (aus UntPlanWeb übernommene: nur Aufsicht, Raum und Aktiv änderbar)', aktion: () => fensterverwaltung.oeffnen('untplanweb.klausuren', {}) });
  f.trenner();
  f.werkzeug({ text: 'Paket als Datei', symbol: 'export', titel: 'Paket (Schema untplanweb/1) als JSON-Datei speichern', aktion: () => dateiHerunterladen(api.dok('/austausch/untplanweb.json'), 'untplanweb.json') });
  f.werkzeug({ text: 'Schülerabwesenheiten', symbol: 'schueler', aktion: () => fensterverwaltung.oeffnen('untplanweb.liste', { art: 'schuelerabwesenheiten' }) });
  f.werkzeug({ text: 'Klassenbuch', symbol: 'unterricht', aktion: () => fensterverwaltung.oeffnen('untplanweb.liste', { art: 'klassenbuch' }) });

  // ---- Statuszeile: Fortschritt bzw. Fehler ----
  function fortschrittZeigen(wert, text) {
    statusZeile.hidden = false;
    statusZeile.className = 'upw-status';
    statusZeile.replaceChildren(h('div', { class: 'upw-balken' }, h('div', { style: { width: `${Math.max(0, Math.min(100, wert))}%` } })), h('div', { class: 'upw-statustext' }, text));
  }
  function fehlerZeigen(titel, text = '') {
    if (!titel) { if (statusZeile.classList.contains('fehler')) statusZeile.hidden = true; return; }
    statusZeile.hidden = false;
    statusZeile.className = 'upw-status fehler';
    statusZeile.replaceChildren(h('div', { class: 'upw-fehlertitel' }, h('span', { class: 'upw-x' }, '×'), titel), h('div', { class: 'upw-statustext' }, text),
      h('a', { href: '#', onclick: (e) => { e.preventDefault(); einstellungen.click(); } }, 'Verbindungseinstellungen prüfen'));
  }

  // ---- kleines Fortschrittsfenster wie im Desktop („Unterrichtsbestandteile werden konvertiert (153 von 238)“) ----
  let popup = null;
  function popupZeigen(wert, text) {
    if (!popup) {
      popup = h('div', { class: 'upw-popup' }, h('div', { class: 'upw-popup-text' }), h('div', { class: 'upw-balken' }, h('div')));
      f.inhalt.append(popup);
    }
    popup.firstChild.textContent = text;
    popup.lastChild.firstChild.style.width = `${Math.max(0, Math.min(100, wert))}%`;
  }
  function popupWeg() { popup?.remove(); popup = null; }

  function sperren(an) {
    laeuft = an;
    for (const k of zeilenEl.querySelectorAll('button')) k.disabled = an || !schreiben;
    einstellungen.disabled = an || !schreiben;
    anfangAktiv.disabled = an || !schreiben;
    anfang.disabled = datumWahl.disabled = an || !schreiben || !stand?.anfangsdatumAktiv;
  }

  async function ausfuehren(teil, istExport) {
    if (laeuft) return;
    if (teil === 'stpl-aenderungen') { fensterverwaltung.oeffnen('untplanweb.stpl', {}); return; }
    if (!stand?.eingerichtet) { fehlerZeigen('Keine Verbindung eingerichtet', 'Bitte zuerst unter „Einstellungen“ Server, Port, Schul-ID und API-Schlüssel eintragen.'); return; }
    sperren(true);
    fehlerZeigen(null);
    let ergebnis = null;
    try {
      const auftrag = await api.post(api.dok(`/untplanweb/${istExport ? 'export' : 'import'}/${teil}`));
      const a = await verfolgen(auftrag, (w, t) => (w < 100 ? popupZeigen(w, t) : null));
      popupWeg();
      if (a.fehler) fehlerZeigen(a.fehlerTitel || 'UntPlanWeb-Übertragungsfehler!', a.fehler);
      else { fortschrittZeigen(100, a.text); ergebnis = a; }
    } catch (e) {
      popupWeg();
      fehlerZeigen('UntPlanWeb-Übertragungsfehler!', e.message);
    } finally {
      sperren(false);
      await neu();
    }
    if (ergebnis?.ergebnis) await meldung(ergebnis.ergebnis, istExport ? 'UntPlan – Frage' : 'UntPlanWeb');
    if (ergebnis?.danach) fensterverwaltung.oeffnen('untplanweb.liste', { art: ergebnis.danach });
  }

  function zeichnen() {
    const s = stand;
    exportStatus.textContent = s.exportStatus;
    exportStatus.title = s.exportHinweis;
    kopfRechts.replaceChildren(h('legend', null, schulnameServer || s.schulname), h('div', { title: s.adresse }, s.server), h('div', null, s.schulId));
    anfangAktiv.checked = s.anfangsdatumAktiv;
    anfang.value = iso(s.anfangsdatum);
    datumWahl.replaceChildren(h('option', { value: '' }, 'Datum wählen...'), ...s.datumVorgaben.map((v) => h('option', { value: iso(v.datum) }, `${v.text} (${new Date(`${iso(v.datum)}T12:00:00`).toLocaleDateString('de-DE')})`)));
    zeilenEl.replaceChildren(...s.zeilen.map((z) => {
      const titel = h('div', { class: 'upw-titel', title: z.hinweis }, z.titel);
      if (z.teil === 'studenten' || z.teil === 'kurse') {
        const art = z.teil === 'studenten' ? 'schuelerabwesenheiten' : 'klassenbuch';
        titel.classList.add('upw-link');
        titel.title += ' (Doppelklick: importierte Daten anzeigen)';
        titel.addEventListener('dblclick', () => fensterverwaltung.oeffnen('untplanweb.liste', { art }));
      }
      return h('div', { class: `upw-zeile-teil${z.export ? ' export' : ' import'}` },
        titel,
        h('div', { class: `upw-datum${z.teil === 'stpl-aenderungen' ? ' normal' : ''}`, title: z.zuletzt }, z.datum,
          z.teil === 'stpl-aenderungen' ? h('span', { class: 'upw-info', title: 'Raumänderungen, zusätzliche Unterrichte (Buchungen) und Prüfungen, die Lehrkräfte in UntPlanWeb erfasst haben, im Fenster „Stundenplanänderungen aus UntPlanWeb“ prüfen und übernehmen. Bereits übernommene oder ignorierte Änderungen werden nicht erneut importiert.' }, 'i') : null),
        h('button', { class: `knopf upw-knopf${z.teil === TEILE.vertretungen ? ' upw-vorgabe' : ''}`, type: 'button', title: z.zuletzt, disabled: !schreiben, dataset: { teil: z.teil },
          onclick: () => ausfuehren(z.teil, z.export) }, z.export ? 'Exportieren' : 'Importieren'));
    }));
    abgleichZeichnen(s.abgleich);
    sperren(laeuft || !!s.laufend);
    if (s.laufend && !laeuft) folgen(s.laufend);
  }

  // läuft eine Übertragung eines anderen Benutzers bzw. Fensters, wird sie mitverfolgt
  async function folgen(auftrag) {
    sperren(true);
    try {
      const a = await verfolgen(auftrag, fortschrittZeigen);
      if (a.fehler) fehlerZeigen(a.fehlerTitel, a.fehler);
    } catch { /* still */ } finally { sperren(false); }
  }

  function abgleichZeichnen(a) {
    const aktiv = h('input', { type: 'checkbox', checked: a.aktiv, disabled: !schreiben });
    const minuten = h('input', { class: 'eingabe', type: 'number', min: 5, max: 1440, value: a.minuten, style: { width: '70px' }, disabled: !schreiben });
    const vertretungen = h('input', { type: 'checkbox', checked: a.vertretungen, disabled: !schreiben });
    const rueck = h('input', { type: 'checkbox', checked: a.rueckmeldungen, disabled: !schreiben });
    const speichern = async () => {
      try { await api.put(api.dok('/untplanweb/abgleich'), { aktiv: aktiv.checked, minuten: Number(minuten.value) || 15, vertretungen: vertretungen.checked, rueckmeldungen: rueck.checked }); hinweis('Automatischer Abgleich gespeichert.', 'ok', 2000); } catch (e) { fehlerMelden(e); }
    };
    for (const el of [aktiv, minuten, vertretungen, rueck]) el.addEventListener('change', speichern);
    abgleichEl.replaceChildren(h('legend', null, 'Automatischer Abgleich (optional)'),
      h('div', { class: 'upw-zeile' },
        h('label', { class: 'kontrolle' }, aktiv, 'eingeschaltet, alle'), minuten, h('span', null, 'Minuten:'),
        h('label', { class: 'kontrolle', title: 'Krankmeldungen, Nachrichten, Buchungen, Prüfungen und Stornos übernehmen (bereits im Dialog bearbeitete werden übersprungen)' }, rueck, 'Rückmeldungen übernehmen'),
        h('label', { class: 'kontrolle' }, vertretungen, 'Vertretungen* exportieren'),
        h('button', { class: 'knopf', type: 'button', disabled: !schreiben, onclick: async () => {
          const weg = laden(f.inhalt);
          try { const e = await api.post(api.dok('/untplanweb/abgleich/jetzt')); hinweis(e.ergebnis, e.ergebnis.startsWith('Fehler') ? 'fehler' : 'ok', 5000); await neu(); } catch (e) { fehlerMelden(e); } finally { weg(); }
        } }, symbol('wiederholen'), 'Jetzt abgleichen')),
      h('div', { class: 'leise' }, a.zuletzt ? `Zuletzt: ${new Date(a.zuletzt).toLocaleString('de-DE')} – ${a.ergebnis}` : 'Der Server gleicht im Hintergrund ab, auch wenn kein Browser geöffnet ist.'));
  }

  async function neu(schulname = false) {
    try {
      stand = await api.get(api.dok('/untplanweb'));
      zeichnen();
      if (schulname && stand.eingerichtet && !schulnameServer) {
        api.get(api.dok('/untplanweb/schulname')).then((r) => { if (r.schulname) { schulnameServer = r.schulname; zeichnen(); } }).catch(() => {});
      }
    } catch (e) { fehlerMelden(e); }
  }

  async function anfangGeaendert(datum) {
    try {
      stand = await api.put(api.dok('/untplanweb/anfangsdatum'), { aktiv: anfangAktiv.checked, datum: datum || anfang.value || heute() }, { quelle: f.id });
      zeichnen();
    } catch (e) { fehlerMelden(e); }
  }
  anfangAktiv.addEventListener('change', () => anfangGeaendert());
  anfang.addEventListener('change', () => anfangGeaendert());
  datumWahl.addEventListener('change', () => { if (datumWahl.value) { anfang.value = datumWahl.value; anfangGeaendert(datumWahl.value); } });

  f.an('daten', (e) => { if (e.quelle !== f.id && !laeuft && betrifft(e, 'Einstellungen', 'Vertretung')) neu(); });
  neu(true);
}

// ---------------------------------------------------------------------------------------------- Stundenplanänderungen

function stplFenster(f) {
  f.titel('Stundenplanänderungen aus UntPlanWeb');
  const schreiben = darfSchreiben();
  const von = h('input', { class: 'eingabe', type: 'date', value: heute() });
  const bis = h('input', { class: 'eingabe', type: 'date', value: plusTage(heute(), 14) });
  const erledigte = h('input', { type: 'checkbox' });
  const status = h('span', { class: 'leise' });
  let einst = null;
  let geladen = false;

  f.werkzeug({ text: 'Neu laden', symbol: 'wiederholen', titel: 'Stundenplanänderungen neu vom Server laden', aktion: () => laden_(true) });
  f.werkzeug({ text: 'Erledigte', symbol: 'sortieren', titel: 'Bereits durchgeführte/ignorierte Änderungen ein-/ausblenden', aktion: () => { erledigte.checked = !erledigte.checked; laden_(false); } });
  f.werkzeug({ text: 'Einstellungen', symbol: 'einstellungen', titel: 'Einstellungen zum Import', aktion: einstellungen });
  f.werkzeuge.append(h('span', { class: 'upw-rechts' }, 'Von:', von, 'Bis:', bis, h('button', { class: 'knopf klein', type: 'button', onclick: () => laden_(true) }, 'Laden')));

  const tabelle = new Tabelle(f.inhalt, {
    zustandSchluessel: 'tabelle.untplanweb.stpl',
    leerText: 'Keine Stundenplanänderungen im gewählten Zeitraum',
    zeilenKlasse: (z) => (z.gruppe ? 'upw-datumzeile' : [z.erledigt ? 'upw-erledigt' : '', z.fehler ? 'upw-fehlerzeile' : z.art === 'Aktivitaet' ? 'upw-aktivitaet' : ''].join(' ').trim()),
    spalten: [
      ['id', '', 110], ['datumText', 'Datum', 85], ['zeit', 'Zeit', 90], ['stunde', 'Stunde', 50], ['artText', 'Art', 75], ['unterricht', 'Lv-Id', 50], ['klassenFach', 'Klasse(n) / Fach', 130],
      ['lehrer', 'Lehrer', 80], ['raeume', 'Räume', 80], ['wert', 'Wert', 44], ['statistik', 'Statistik', 60], ['text', 'Text', 260], ['ersteller', 'Buchungsersteller*in', 130], ['statusText', 'Status', 70],
    ].map(([schluessel, titel, breite]) => ({ schluessel, titel, breite, art: 'NurLesen', editierbar: false, wert: (z) => (z.gruppe && schluessel !== 'datumText' ? '' : z[schluessel] ?? '') })),
  });
  tabelle.o.sortierbar = false;

  const knopf = (text, aktion, primaer = false) => h('button', { class: `knopf${primaer ? ' primaer' : ''}`, type: 'button', disabled: !schreiben, onclick: () => Promise.resolve().then(aktion).catch(fehlerMelden) }, text);
  f.fuss.append(h('label', { class: 'kontrolle' }, erledigte, 'Bereits durchgeführte/ignorierte Änderungen anzeigen.'), status,
    h('span', { class: 'rechts upw-fussknoepfe' }, knopf('Buchungen markieren', () => markieren(false)), knopf('Prüfungen markieren', () => markieren(true)),
      knopf('Ignorieren', ignorieren), knopf('Übernehmen', uebernehmen, true)));
  erledigte.addEventListener('change', () => laden_(false));
  von.addEventListener('change', () => laden_(false));
  bis.addEventListener('change', () => laden_(false));

  function markieren(pruefungen) {
    tabelle.gewaehlt.clear();
    for (const z of tabelle.zeilen) if (!z.gruppe && !z.erledigt && (pruefungen ? z.art === 'Pruefung' : z.art !== 'Pruefung')) tabelle.gewaehlt.add(z.id);
    tabelle._neuZeichnen();
  }
  const gewaehlt = () => tabelle.ausgewaehlte().filter((z) => !z.gruppe && !z.erledigt).map((z) => z.id);

  async function uebernehmen() {
    const ids = gewaehlt();
    if (!ids.length) { await meldung('Bitte zuerst Änderungen markieren (z. B. „Buchungen markieren“).', 'Stundenplanänderungen aus UntPlanWeb'); return; }
    const r = await api.post(api.dok('/untplanweb/stpl/uebernehmen'), { ids }, { quelle: f.id });
    await laden_(false);
    status.textContent = r.text;
    hinweis(r.text, r.abgelehnt.length ? 'warnung' : 'ok', 6000);
  }
  async function ignorieren() {
    const ids = gewaehlt();
    if (!ids.length) return;
    const r = await api.post(api.dok('/untplanweb/stpl/ignorieren'), { ids }, { quelle: f.id });
    await laden_(false);
    status.textContent = r.text;
  }
  async function einstellungen() {
    if (!einst) return;
    const vor = h('input', { type: 'checkbox', checked: einst.vorPruefungIgnorieren });
    const nach = h('input', { type: 'checkbox', checked: einst.nachPruefungIgnorieren });
    const tage = h('input', { class: 'eingabe', type: 'number', min: 1, max: 365, value: einst.tage, style: { width: '70px' } });
    const ok = await dialog({ titel: 'Einstellungen', breite: '520px', inhalt: h('div', { class: 'upw-spalte' },
      h('label', { class: 'kontrolle' }, vor, 'Pausenaufsichten vor Prüfungen außer Betracht lassen'),
      h('label', { class: 'kontrolle' }, nach, 'Pausenaufsichten nach Prüfungen außer Betracht lassen'),
      h('label', { class: 'kontrolle' }, tage, 'Für wie viele Tage in die Zukunft sollen Stundenplanänderungen standardmäßig importiert werden')),
    knoepfe: [{ text: 'Ok', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
    if (!ok) return;
    einst = await api.put(api.dok('/untplanweb/stpl/einstellungen'), { vorPruefungIgnorieren: vor.checked, nachPruefungIgnorieren: nach.checked, tage: Number(tage.value) || 14 });
    bis.value = plusTage(von.value, einst.tage);
    laden_(false);
  }

  async function laden_(neuVomServer) {
    const weg = laden(f.inhalt);
    try {
      const d = await api.get(api.dok(`/untplanweb/stpl?von=${von.value}&bis=${bis.value}&erledigte=${erledigte.checked}&neu=${neuVomServer || !geladen}`));
      geladen = true;
      einst = d.einstellungen;
      if (!neuVomServer && bis.value === plusTage(heute(), 14) && d.einstellungen.tage !== 14 && von.value === heute()) bis.value = plusTage(von.value, d.einstellungen.tage);
      const zeilen = [];
      let letzter = null;
      for (const z of d.zeilen) {
        if (z.datumText !== letzter) { zeilen.push({ id: `datum-${z.datumText}`, gruppe: true, datumText: z.datumText }); letzter = z.datumText; }
        zeilen.push({ ...z, id: z.id, statusText: z.uebernommen === true ? '✔' : z.uebernommen === false ? 'ignoriert' : '' });
      }
      tabelle.daten(zeilen);
      status.textContent = d.status;
    } catch (e) { fehlerMelden(e); } finally { weg(); }
  }
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Vertretung', 'Einstellungen')) laden_(false); });
  laden_(true);
}

// ---------------------------------------------------------------------------------------------- Klausuren

async function klausurDialog(k) {
  const importiert = !!k.importiert;
  const feld = (titel, el) => [h('label', null, titel), el];
  const name = h('input', { class: 'eingabe', value: k.name ?? 'Klausur', disabled: importiert });
  const datum = h('input', { class: 'eingabe', type: 'date', value: iso(k.datum ?? heute()), disabled: importiert });
  const vonS = h('input', { class: 'eingabe', type: 'number', min: 1, value: k.von ?? 1, style: { width: '70px' }, disabled: importiert });
  const bisS = h('input', { class: 'eingabe', type: 'number', min: 1, value: k.bis ?? 1, style: { width: '70px' }, disabled: importiert });
  const klassen = h('input', { class: 'eingabe', value: k.klassen ?? '', placeholder: 'z. B. Q1,Q2', disabled: importiert });
  const lehrer = h('input', { class: 'eingabe', value: k.lehrer ?? '', placeholder: 'Kurznamen, z. B. MÜL,SCH' });
  const raeume = h('input', { class: 'eingabe', value: k.raeume ?? '' });
  const entfaellt = h('input', { type: 'checkbox', checked: k.unterrichtEntfaellt ?? true, disabled: importiert });
  const aktiv = h('input', { type: 'checkbox', checked: k.aktiv ?? true });
  const text = h('input', { class: 'eingabe', value: k.text ?? '', disabled: importiert });
  const ok = await dialog({
    titel: importiert ? 'Klausur (aus UntPlanWeb – nur Lehrer und Raum änderbar)' : 'Klausur', breite: '520px',
    inhalt: h('div', { class: 'formular' }, ...feld('Name:', name), ...feld('Datum:', datum), ...feld('Stunde von / bis:', h('div', { class: 'upw-zeile' }, vonS, 'bis', bisS)),
      ...feld('Klasse(n):', klassen), ...feld('Aufsicht (Lehrer):', lehrer), ...feld('Räume:', raeume),
      h('span'), h('label', { class: 'kontrolle' }, entfaellt, 'Kollidierender Unterricht der Klassen entfällt'), h('span'), h('label', { class: 'kontrolle' }, aktiv, 'Aktiv'), ...feld('Text:', text)),
    knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }],
  });
  if (!ok) return null;
  return { name: name.value, datum: datum.value, von: Number(vonS.value), bis: Number(bisS.value), klassen: klassen.value, lehrer: lehrer.value, raeume: raeume.value, unterrichtEntfaellt: entfaellt.checked, aktiv: aktiv.checked, text: text.value };
}

function klausurenFenster(f) {
  f.titel('Klausuren');
  const schreiben = darfSchreiben();
  const von = h('input', { class: 'eingabe', type: 'date', value: '' });
  const bis = h('input', { class: 'eingabe', type: 'date', value: '' });
  const tabelle = new Tabelle(f.inhalt, {
    zustandSchluessel: 'tabelle.untplanweb.klausuren',
    leerText: 'Keine Klausuren',
    zeilenKlasse: (z) => (z.importiert ? 'upw-importiert' : z.aktiv ? '' : 'upw-erledigt'),
    beiDoppelklick: (z) => bearbeiten(z),
    spalten: [['datumText', 'Datum', 85], ['stunde', 'Std.', 50], ['name', 'Name', 160], ['klassen', 'Klasse(n)', 100], ['lehrer', 'Aufsicht', 110], ['raeume', 'Räume', 100],
      ['aktivText', 'Aktiv', 50], ['herkunft', 'Herkunft', 100], ['text', 'Text', 200]].map(([schluessel, titel, breite]) => ({ schluessel, titel, breite, art: 'NurLesen', editierbar: false })),
    kontextmenue: (zeilen) => [
      { text: 'Bearbeiten …', symbol: 'einstellungen', aktion: () => bearbeiten(zeilen[0]), deaktiviert: !zeilen.length || !schreiben },
      { text: 'Löschen', symbol: 'loeschen', aktion: () => loeschen(zeilen[0]), deaktiviert: !zeilen.length || !schreiben },
    ],
  });
  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'plus', aktion: async () => { const d = await klausurDialog({}); if (d) { await api.post(api.dok('/untplanweb/klausuren'), d, { quelle: f.id }); laden_(); } } });
    f.werkzeug({ text: 'Bearbeiten', symbol: 'einstellungen', aktion: () => bearbeiten(tabelle.aktiveZeile()) });
    f.werkzeug({ text: 'Löschen', symbol: 'loeschen', aktion: () => loeschen(tabelle.aktiveZeile()) });
    f.trenner();
  }
  f.werkzeuge.append(h('span', { class: 'upw-rechts' }, 'von', von, 'bis', bis));
  von.addEventListener('change', laden_);
  bis.addEventListener('change', laden_);

  async function bearbeiten(z) {
    if (!z || !schreiben) return;
    const d = await klausurDialog(z);
    if (!d) return;
    await api.put(api.dok(`/untplanweb/klausuren/${z.id}`), d, { quelle: f.id });
    laden_();
  }
  async function loeschen(z) {
    if (!z || !(await frage(`Klausur „${z.name}“ am ${z.datumText} löschen?${z.buchungen ? '\nZur Prüfung gewordene Buchungen kehren zurück.' : ''}`, { ja: 'Löschen', gefahr: true }))) return;
    await api.loeschen(api.dok(`/untplanweb/klausuren/${z.id}`), { quelle: f.id });
    laden_();
  }
  async function laden_() {
    try {
      const liste = await api.get(api.dok(`/untplanweb/klausuren?${von.value ? `von=${von.value}&` : ''}${bis.value ? `bis=${bis.value}` : ''}`));
      tabelle.daten(liste.map((k) => ({ ...k, aktivText: k.aktiv ? 'X' : '', herkunft: k.importiert ? 'UntPlanWeb' : 'manuell' })));
      f.fuss.replaceChildren(h('span', null, `${liste.length} Klausuren`), h('span', { class: 'rechts leise' }, 'Doppelklick bearbeitet · aus UntPlanWeb übernommene Prüfungen: nur Aufsicht, Raum und Aktiv änderbar'));
    } catch (e) { fehlerMelden(e); }
  }
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Vertretung')) laden_(); });
  laden_();
}

// ---------------------------------------------------------------------------------------------- importierte Listen

const LISTEN = {
  schuelerabwesenheiten: {
    titel: 'Schülerabwesenheiten (UntPlanWeb)',
    spalten: [['datum', 'Datum', 85], ['stunde', 'Std.', 45], ['klasse', 'Klasse', 60], ['name', 'Name', 170], ['art', 'Art', 110], ['status', 'Status', 100], ['fach', 'Fach', 60], ['lehrer', 'Lehrer', 70], ['bemerkung', 'Bemerkung', 200]],
  },
  klassenbuch: {
    titel: 'Klassenbuch (UntPlanWeb)',
    spalten: [['datum', 'Datum', 85], ['stunde', 'Std.', 45], ['klassen', 'Klasse(n)', 80], ['fach', 'Fach', 60], ['lehrer', 'Lehrer', 70], ['lehrstoff', 'Lehrstoff', 220], ['hausaufgabe', 'Hausaufgabe', 200], ['faellig', 'fällig', 85], ['anwesenheit', 'Anwesenheit', 90]],
  },
};

function listenFenster(f, p) {
  const def = LISTEN[p.art] || LISTEN.schuelerabwesenheiten;
  f.titel(def.titel);
  const suche = h('input', { class: 'eingabe', type: 'search', placeholder: 'Suchen …', style: { width: '160px' } });
  const klasse = h('select', { class: 'eingabe', title: 'Nur Einträge dieser Klasse anzeigen' });
  f.werkzeuge.append(h('span', null, 'Klasse:'), klasse, h('span', null, 'Suche:'), suche, h('span', { class: 'upw-rechts leise' }, 'Importiert aus UntPlanWeb – Änderungen bitte dort vornehmen.'));
  const tabelle = new Tabelle(f.inhalt, { zustandSchluessel: `tabelle.untplanweb.${p.art}`, leerText: 'Keine importierten Einträge', spalten: def.spalten.map(([schluessel, titel, breite]) => ({ schluessel, titel, breite, art: 'NurLesen', editierbar: false })) });
  let alle = [];
  const anzeigen = () => {
    const s = suche.value.trim().toLowerCase();
    const k = klasse.value;
    const liste = alle.filter((z) => (!k || (z.klasse ?? z.klassen ?? '').split(',').includes(k)) && (!s || Object.values(z).some((v) => String(v).toLowerCase().includes(s))));
    tabelle.daten(liste);
    f.fuss.replaceChildren(h('span', null, `${liste.length} von ${alle.length} Einträgen`));
  };
  suche.addEventListener('input', anzeigen);
  klasse.addEventListener('change', anzeigen);
  (async () => {
    try {
      alle = (await api.get(api.dok(`/untplanweb/${p.art}`))).map((z, i) => ({ id: i, ...z }));
      const klassen = [...new Set(alle.flatMap((z) => (z.klasse ?? z.klassen ?? '').split(',')).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de', { numeric: true }));
      klasse.replaceChildren(h('option', { value: '' }, 'Alle'), ...klassen.map((x) => h('option', { value: x }, x)));
      anzeigen();
    } catch (e) { fehlerMelden(e); }
  })();
}

const oeffnen = () => { if (zustand.dokument) fensterverwaltung.oeffnen('untplanweb', {}); };

export default {
  id: 'untplanweb',
  css: ['css/module/untplanweb.css'],
  fenster: {
    untplanweb: { titel: 'UntPlanWeb', symbol: 'web', breite: 720, hoehe: 640, einzig: () => 'untplanweb', erstellen: hauptFenster },
    'untplanweb.stpl': { titel: 'Stundenplanänderungen aus UntPlanWeb', symbol: 'stundenplan', breite: 1080, hoehe: 480, einzig: () => 'stpl', erstellen: stplFenster },
    'untplanweb.klausuren': { titel: 'Klausuren', symbol: 'kalender', breite: 900, hoehe: 440, einzig: () => 'klausuren', erstellen: klausurenFenster },
    'untplanweb.liste': { titel: 'UntPlanWeb', symbol: 'schueler', breite: 900, hoehe: 460, einzig: (p) => p.art, erstellen: listenFenster },
  },
  befehle: {
    'untplanweb.oeffnen': oeffnen,
    'untplanweb.einstellungen': () => einstellungenDialog(),
    'untplanweb.stpl': () => fensterverwaltung.oeffnen('untplanweb.stpl', {}),
    'untplanweb.klausuren': () => fensterverwaltung.oeffnen('untplanweb.klausuren', {}),
  },
  ribbon: [
    {
      id: 'start.untplanweb', reiter: 'Start', gruppe: 'Module', text: 'UntPlanWeb', symbol: 'web', braucht: 'dokument', hinweis: 'Datenaustausch mit UntPlanWeb',
      menue: [
        { text: 'Datenaustausch mit UntPlanWeb …', symbol: 'web', befehl: 'untplanweb.oeffnen' },
        { text: 'Stundenplanänderungen aus UntPlanWeb …', symbol: 'stundenplan', befehl: 'untplanweb.stpl' },
        { text: 'Klausuren …', symbol: 'kalender', befehl: 'untplanweb.klausuren' },
        { text: 'Paket als Datei exportieren (JSON) …', symbol: 'export', aktion: () => dateiHerunterladen(api.dok('/austausch/untplanweb.json'), 'untplanweb.json') },
        { text: 'Einstellungen …', symbol: 'einstellungen', befehl: 'untplanweb.einstellungen', braucht: 'schreiben' },
      ],
    },
    { id: 'module.untplanweb', reiter: 'Module', gruppe: 'Weitere Module', text: 'UntPlanWeb', symbol: 'web', gross: true, befehl: 'untplanweb.oeffnen', braucht: 'dokument', hinweis: 'Datenaustausch mit UntPlanWeb' },
    { id: 'vertretung.untplanweb', reiter: 'Vertretungsplanung', gruppe: 'Vertretungsplanung', text: 'UntPlanWeb', symbol: 'web', gross: true, befehl: 'untplanweb.oeffnen', braucht: 'dokument', hinweis: 'Vertretungen an UntPlanWeb übertragen, Rückmeldungen übernehmen' },
  ],
};
