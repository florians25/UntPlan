// Sprechtag im Grunddesign:
// - Übersicht „Sprechtage“ (#/sprechtag): Karten je Schuljahr mit Phase, Datum und Anmeldezeitraum; Administration: „Neuer Sprechtag“
//   (Schuljahr wählen), Löschen, Duplizieren, Bearbeiten; Info-Symbol am Schuljahr öffnet die Statusinformationen (Phasen).
// - Konfiguration (#/sprechtag/<id>/bearbeiten): 1. Allgemeine Informationen, 2. Zeiträume festlegen (Abschnitte, Anmeldephasen),
//   3. Buchungsregeln und Rechte – Eingaben werden laufend als Entwurf gespeichert; „Veröffentlichen“ startet die Phase „Verwaltung LK“.
// - Verwaltung (Administration): Lehrkräfte mit Suche und Filtern (Alle, Kein Raum, Keine Termine), je Abschnitt Raum und „+“-Menü
//   (Einladung senden, Termin buchen, Abwesenheit eintragen); „Meine Termine“ für Lehrkräfte.
// - Erziehungsberechtigte: Hinweis zur Phase, Terminanfragen und Einladungen, „Meine Termine“ nach Datum, freie Buchung ab der 2. Anmeldephase.
// - startkarte(): Hinweis „Sprechtag“ auf der Startseite.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, fehler, herunterladen } from '../ui.js';
import { cssLaden } from './kbeinstellungen.js';

const BASIS = '/api/app/sprechtag';

/** PDF vom Server laden und speichern (Terminplan, Gesamtplan, Elternplan). */
async function pdf(pfad, name) {
  try {
    const r = await fetch(`${BASIS}${pfad}`, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
    if (!r.ok) throw new Error((await r.text().catch(() => '')) || 'PDF konnte nicht erstellt werden.');
    herunterladen(name, await r.blob(), 'application/pdf');
  } catch (err) { fehler(err); }
}

function stileLaden() {
  cssLaden();
  if (document.querySelector('link[data-sprechtag-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/sprechtag.css', import.meta.url).href;
  l.dataset.sprechtagCss = '1';
  document.head.append(l);
}

const bi = n => `<i class="bi bi-${n}" aria-hidden="true"></i>`;
const pille = (phase, text) => `<span class="st-pille ${e(phase)}">${e(text)}</span>`;
const PHASEN_TEXT = [
  ['entwurf', 'Entwurf', 'Der Sprechtag ist angelegt, aber noch unvollständig konfiguriert. Er kann jederzeit weiter bearbeitet werden.'],
  ['verwaltung', 'Verwaltung LK', 'Zeitraum für Lehrkräfte. Diese bestätigen ihre Räume, tragen Abwesenheiten ein und senden Einladungen an Erziehungsberechtigte.'],
  ['phase1', '1. Anmeldephase', 'Exklusiver Zeitraum für eingeladene Erziehungsberechtigte, um Termine zu buchen oder zu bestätigen. Lehrkräfte können weiterhin Termine koordinieren.'],
  ['phase1_beendet', '1. Anmeldephase beendet', 'Die priorisierte Anmeldephase ist abgeschlossen. Bis zum Start der allgemeinen Anmeldephase können keine Einladungen verschickt werden und Erziehungsberechtigte nur Termine aus bestehenden Einladungen buchen. Lehrkräfte können weiterhin Termine koordinieren. Bestehende Termine können weiterhin bestätigt oder abgesagt werden.'],
  ['phase2', '2. Anmeldephase', 'Offene Buchungsphase für alle Erziehungsberechtigten, um eigenständig Termine bei Lehrkräften zu buchen.'],
  ['geschlossen', 'Anmeldung geschlossen', 'Die Anmeldephase ist beendet. Erziehungsberechtigte können keine neuen Termine mehr buchen oder bestehende verschieben. Absagen sind weiterhin möglich.'],
  ['durchfuehrung', 'In Durchführung', 'Der Sprechtag findet aktuell statt.'],
  ['abgeschlossen', 'Abgeschlossen', 'Der Sprechtag ist beendet und im System archiviert.'],
];
const statusPille = x => pille(x.status === 'bestaetigt' ? 'gruen' : ['abgelehnt', 'abgesagt'].includes(x.status) ? 'rot' : '', x.statusText);
const minuten = t => { const [h, m] = String(t || '0:0').split(':').map(Number); return h * 60 + m; };
const zeit = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
const name = s => `${s.name} ${s.vorname}`.trim();

export async function zeigen(seite, kontext, teile = []) {
  stileLaden();
  kontext.titel('');
  const [a, b] = teile;
  const id = Number(a);
  if (!id) return uebersicht(seite, kontext);
  if (b === 'bearbeiten') return konfiguration(seite, kontext, id, teile[2]);
  const d = await holen(`${BASIS}/${id}`);
  if (d.rolle === 'eltern') return eltern(seite, kontext, d);
  if (d.rolle === 'admin' && !d.sprechtag.veroeffentlicht) return kontext.navigieren(`#/sprechtag/${id}/bearbeiten`, true);
  return verwaltung(seite, kontext, d);
}

// ================================================================== Seitenpanel und Menü

/** Panel von rechts (Statusinformationen, Abwesenheit, Einladung, Termin); liefert { rumpf, schliessen }. */
function seitenpanel({ titel, inhalt, knoepfe = '', beimOeffnen }) {
  document.querySelector('.st-panel-grund')?.remove();
  document.querySelector('.st-panel')?.remove();
  const grund = document.createElement('div');
  grund.className = 'st-panel-grund';
  const p = document.createElement('aside');
  p.className = 'st-panel';
  p.setAttribute('role', 'dialog');
  p.setAttribute('aria-label', titel);
  p.innerHTML = `<div class="st-panel-kopf"><h2>${e(titel)}</h2><button type="button" class="st-panel-zu" data-zu aria-label="Schließen">${bi('x-lg')}</button></div>
    <div class="st-panel-rumpf">${inhalt}</div>${knoepfe ? `<div class="st-panel-fuss">${knoepfe}</div>` : ''}`;
  const zu = () => { grund.remove(); p.remove(); document.removeEventListener('keydown', taste); };
  const taste = ev => { if (ev.key === 'Escape') zu(); };
  grund.onclick = zu;
  p.querySelector('[data-zu]').onclick = zu;
  document.addEventListener('keydown', taste);
  document.body.append(grund, p);
  const ergebnis = { panel: p, schliessen: zu };
  beimOeffnen?.(p, ergebnis);
  p.querySelector('input:not([type=checkbox]), select, textarea')?.focus();
  return ergebnis;
}

function statusinformationen() {
  seitenpanel({
    titel: 'Statusinformationen',
    inhalt: `<p class="st-panel-unter"><b>Die Phasen werden nacheinander durchlaufen. Sobald eine Phase beendet ist, startet die nächste:</b></p>
      <dl class="st-phasen">${PHASEN_TEXT.map(([k, t, text]) => `<dt>${pille(k, t)}</dt><dd>${e(text)}</dd>`).join('')}</dl>`,
  });
}

/** Kleines Menü an einem Knopf (z. B. „+“). */
function menue(knopf, eintraege) {
  document.querySelector('.st-menue')?.remove();
  if (!eintraege.length) return;
  const m = document.createElement('div');
  m.className = 'st-menue';
  m.setAttribute('role', 'menu');
  m.innerHTML = eintraege.map(([t], i) => `<button type="button" role="menuitem" data-i="${i}">${e(t)}</button>`).join('');
  document.body.append(m);
  const r = knopf.getBoundingClientRect();
  m.style.top = `${r.bottom + window.scrollY + 4}px`;
  m.style.left = `${Math.max(8, r.right + window.scrollX - m.offsetWidth)}px`;
  const zu = () => { m.remove(); document.removeEventListener('click', aussen, true); };
  const aussen = ev => { if (!m.contains(ev.target)) zu(); };
  setTimeout(() => document.addEventListener('click', aussen, true));
  m.querySelectorAll('[data-i]').forEach(b => { b.onclick = () => { zu(); eintraege[Number(b.dataset.i)][1](); }; });
  m.querySelector('button')?.focus();
}

const feld = (label, html, klasse = '') => `<label class="st-feld ${klasse}"><span>${e(label)}</span>${html}</label>`;
const schalter = (attr, an, label) => `<label class="sd-schalter st-gross-schalter"><input type="checkbox" ${attr} ${an ? 'checked' : ''} aria-label="${e(label)}"><span class="sd-schalter-spur" aria-hidden="true"></span></label>`;

// ================================================================== Übersicht

async function uebersicht(seite, kontext) {
  const d = await holen(BASIS);
  const jahre = [...new Set(d.sprechtage.map(t => t.schuljahr))];
  const karte = t => `<article class="st-karte" tabindex="0" data-oeffnen="${t.id}" aria-label="${e(t.name)} öffnen">
      <div>${pille(t.phase, t.phaseText)}</div>
      <h3>${e(t.name || 'Ohne Namen')}</h3>
      <div class="st-karte-zeile">Datum: ${e(t.datumText || '-')}</div>
      <div class="st-karte-zeile">Anmeldung: ${e(t.anmeldungText || '-')}</div>
      <span class="st-karte-pfeil">${bi('arrow-right')}</span>
      ${d.admin ? `<div class="st-karte-knoepfe">
        <button type="button" class="st-symbol" data-loeschen="${t.id}" title="Löschen" aria-label="${e(t.name)} löschen">${bi('trash3')}</button>
        <button type="button" class="st-symbol" data-duplizieren="${t.id}" title="Duplizieren" aria-label="${e(t.name)} duplizieren">${bi('plus-square')}</button>
        ${t.bearbeitbar ? `<button type="button" class="st-symbol" data-bearbeiten="${t.id}" title="Bearbeiten" aria-label="${e(t.name)} bearbeiten">${bi('pencil')}</button>` : ''}
      </div>` : ''}
    </article>`;
  seite.innerHTML = `<div class="st">
    <div class="st-seitenkopf"><h1>Sprechtage</h1>${d.admin ? `<button type="button" class="nl-hinzu" data-neu>${icon('plus')}Neuer Sprechtag</button>` : ''}</div>
    <div class="st-innen">
      ${jahre.length ? jahre.map(j => `<h2 class="st-jahr">${e(j)} <button type="button" class="st-info" data-status title="Statusinformationen" aria-label="Statusinformationen">${bi('info-circle')}</button></h2>
        <div class="st-karten">${d.sprechtage.filter(t => t.schuljahr === j).map(karte).join('')}</div>`).join('')
        : `<div class="st-leer">${d.admin ? 'Noch kein Sprechtag angelegt. Legen Sie mit „Neuer Sprechtag“ den ersten an.' : 'Zurzeit ist kein Sprechtag veröffentlicht.'}</div>`}
    </div></div>`;
  seite.querySelectorAll('[data-status]').forEach(b => { b.onclick = statusinformationen; });
  seite.querySelectorAll('[data-oeffnen]').forEach(k => {
    const auf = ev => { if (!ev.target.closest('button')) kontext.navigieren(`#/sprechtag/${k.dataset.oeffnen}`); };
    k.onclick = auf;
    k.onkeydown = ev => { if (ev.key === 'Enter' && ev.target === k) auf(ev); };
  });
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => { b.onclick = () => kontext.navigieren(`#/sprechtag/${b.dataset.bearbeiten}/bearbeiten`); });
  seite.querySelectorAll('[data-duplizieren]').forEach(b => {
    b.onclick = async () => {
      try { const t = await senden(`${BASIS}/${b.dataset.duplizieren}/duplizieren`, {}); toast('Sprechtag dupliziert.'); kontext.navigieren(`#/sprechtag/${t.id}/bearbeiten`); }
      catch (err) { fehler(err); }
    };
  });
  seite.querySelectorAll('[data-loeschen]').forEach(b => {
    b.onclick = async () => {
      const t = d.sprechtage.find(x => x.id === Number(b.dataset.loeschen));
      if (!await bestaetigen('Sprechtag löschen', `„${t.name}“ wird unwiderruflich gelöscht.${t.veroeffentlicht ? ' Erziehungsberechtigte mit Terminen bzw. Einladungen werden über die Absage benachrichtigt.' : ''}`)) return;
      try { await api(`${BASIS}/${t.id}`, { methode: 'DELETE' }); toast('Sprechtag gelöscht.'); uebersicht(seite, kontext); }
      catch (err) { fehler(err); }
    };
  });
  seite.querySelector('[data-neu]')?.addEventListener('click', () => {
    dialog({
      titel: 'Schuljahr auswählen',
      inhalt: `<p>Wählen Sie das Schuljahr für den neuen Sprechtag aus. Hinweis: Das Schuljahr kann nach dem Anlegen nicht mehr geändert werden.</p>
        <label class="form-label small mb-1" for="st-sj">Schuljahr</label>
        <select class="form-select" id="st-sj">${d.schuljahre.map(s => `<option>${e(s.name)}</option>`).join('')}</select>`,
      knoepfe: [{ text: 'Abbrechen' }, {
        text: 'Sprechtag anlegen', art: 'haupt',
        aktion: async box => {
          const t = await senden(BASIS, { schuljahr: box.querySelector('#st-sj').value });
          kontext.navigieren(`#/sprechtag/${t.id}/bearbeiten`);
        },
      }],
    });
  });
}

// ================================================================== Kopf

function kopf(t, { reiter = '', rechts = '', unter, pilleAn = true } = {}) {
  return `<div class="st-kopf ${reiter ? '' : 'ohne-reiter'}">
    <a class="st-zurueck" href="#/sprechtag" title="Zurück" aria-label="Zurück zu den Sprechtagen">${bi('arrow-left')}</a>
    <div class="st-kopf-text"><h1>${e(t.name || 'Neuer Sprechtag')}${pilleAn ? ` ${pille(t.phase, t.phaseText)}` : ''}</h1>
      ${unter ?? `<div class="st-kopf-unter">${e(t.schuljahrText)}</div>`}${reiter}</div>
    ${rechts ? `<div class="st-kopf-rechts">${rechts}</div>` : ''}
  </div>`;
}

// ================================================================== Konfiguration

const REITER = [['allgemein', '1. Allgemeine Informationen'], ['zeitraeume', '2. Zeiträume festlegen'], ['regeln', '3. Buchungsregeln und Rechte']];

async function konfiguration(seite, kontext, id, reiterParam) {
  let t = await holen(`${BASIS}/${id}`).then(x => x.sprechtag);
  if (!t.bearbeitbar) {
    toast('Der Sprechtag kann nicht mehr bearbeitet werden (Termine vorhanden bzw. Anmeldung begonnen).', true);
    return kontext.navigieren(`#/sprechtag/${id}`, true);
  }
  let k = structuredClone(t.konfig);
  let reiter = REITER.some(r => r[0] === reiterParam) ? reiterParam : 'allgemein';
  let timer = null;
  let status = '';

  const teil = (wert, i) => String(wert || '').split('T')[i] || '';
  const allgemein = () => `
    <h2 class="st-abschnitt-titel">Sprechtagsnamen festlegen</h2>
    <p class="st-abschnitt-text">Vergeben Sie hier die Bezeichnung für den neuen Sprechtag. Detaillierte Zeiträume, Buchungsregeln und Rechte stellen Sie in den nachfolgenden Schritten ein.</p>
    ${feld('Sprechtagsname', `<input type="text" maxlength="80" data-k="name" value="${e(k.name)}">`, 'schmal')}
    <hr class="st-trenner">
    <h2 class="st-abschnitt-titel">Dauer der Zeitblöcke festlegen</h2>
    <p class="st-abschnitt-text">Legen Sie fest, wie viele Minuten für einen einzelnen Termin eingeplant werden. Die Dauer gilt einheitlich als Taktung für den gesamten Sprechtag.</p>
    ${feld('Dauer der Zeitblöcke (Min)', `<input type="number" min="3" max="120" data-k="dauer" value="${e(k.dauer)}">`, 'schmal')}
    <hr class="st-trenner">
    <div class="st-recht"><div><h2 class="st-abschnitt-titel mt-0">Online-Sprechtag</h2>
      <p>Aktivieren Sie diese Option, wenn der Sprechtag vollständig online stattfinden soll. Eine physische Raumbuchung ist dann nicht erforderlich. Die notwendigen Online-Meeting-Links müssen von der Administration oder den Lehrkräften direkt im jeweiligen Termin hinterlegt werden.</p></div>
      ${schalter('data-k="online"', k.online, 'Online-Sprechtag')}</div>`;

  const zeitraeume = () => `
    <h2 class="st-abschnitt-titel">Sprechtag in Abschnitte gliedern</h2>
    <p class="st-abschnitt-text">Legen Sie fest, wann Ihr Sprechtag stattfindet – auch für verschiedene Tage möglich. Die Zeitblöcke ergeben sich aus der zuvor definierten Länge. Raumzuweisungen und Pausen passen Sie später an.</p>
    ${k.abschnitte.map((a, i) => `<section class="st-box">
      <h4>${i + 1}. Sprechtagsabschnitt</h4>
      ${k.abschnitte.length > 1 ? `<button type="button" class="st-box-loeschen" data-abschnitt-weg="${i}" title="Abschnitt löschen" aria-label="${i + 1}. Abschnitt löschen">${bi('trash3')}</button>` : ''}
      <div class="st-raster">
        ${feld('Abschnittsname', `<input type="text" maxlength="80" data-a="${i}" data-f="name" value="${e(a.name)}">`)}
        ${feld('Datum', `<input type="date" data-a="${i}" data-f="datum" value="${e(a.datum)}" min="${e(t.schuljahrVon)}" max="${e(t.schuljahrBis)}">`)}
        ${feld('Startzeit', `<input type="time" data-a="${i}" data-f="von" value="${e(a.von)}">`)}
        ${feld('Endzeit', `<input type="time" data-a="${i}" data-f="bis" value="${e(a.bis)}">`)}
      </div></section>`).join('')}
    <button type="button" class="st-plus-breit" data-abschnitt-neu>${bi('plus-lg')} Sprechtagsabschnitt</button>
    <hr class="st-trenner">
    <h2 class="st-abschnitt-titel">Anmeldephasen festlegen</h2>
    <p class="st-abschnitt-text">Eine gestaffelte Freigabe ermöglicht die Priorisierung dringender Anliegen. Lehrkräfte können in allen Phasen Termine für Erziehungsberechtigte verwalten.</p>
    ${[['phase1', '1. Priorisierte Anmeldephase', 'In dieser Phase buchen ausschließlich Erziehungsberechtigte, die eine priorisierte Einladung erhalten haben (z. B. auf Initiative einer Lehrkraft).'],
       ['phase2', '2. Allgemeine Anmeldephase', 'Nach Abschluss der ersten Phase werden alle verbleibenden Termine für alle Erziehungsberechtigten freigegeben.']].map(([p, titel, text]) => `<section class="st-box">
      <h4>${titel}</h4><p>${text}</p>
      <div class="st-raster phase">
        ${feld('Startdatum', `<input type="date" data-p="${p}Von" data-i="0" value="${e(teil(k[`${p}Von`], 0))}">`)}
        ${feld('Startzeit', `<input type="time" data-p="${p}Von" data-i="1" value="${e(teil(k[`${p}Von`], 1))}">`)}
        ${feld('Enddatum', `<input type="date" data-p="${p}Bis" data-i="0" value="${e(teil(k[`${p}Bis`], 0))}">`)}
        ${feld('Endzeit', `<input type="time" data-p="${p}Bis" data-i="1" value="${e(teil(k[`${p}Bis`], 1))}">`)}
      </div></section>`).join('')}`;

  const RECHTE = [
    ['raeumeWaehlen', 'Eigene Räume wählen', 'Erlaubt Lehrkräften, ihre Räume für den Sprechtag selbst festzulegen.'],
    ['abwesenheitenEintragen', 'Abwesenheiten eintragen', 'Erlaubt Lehrkräften, eigene Abwesenheiten für den Sprechtag zu erfassen.'],
    ['termineBearbeiten', 'Bestehende Termine bearbeiten & absagen', 'Erlaubt Lehrkräften, bereits erstellte oder gebuchte Termine (auch von Admins) zu verschieben, anzupassen oder abzusagen.'],
    ['termineBuchen', 'Termine für Erziehungsberechtigte buchen', 'Erlaubt Lehrkräften, verbindliche Termine mit festem Zeitslot direkt im Namen von Erziehungsberechtigten anzulegen (z. B. nach telefonischer Absprache).'],
  ];
  const REGELN = [
    ['bloeckeBisPause', 'Zeitblöcke am Stück bis zur Pause', 'Anzahl der aufeinanderfolgenden Zeitblöcke, bevor für die Lehrkraft automatisch eine Pause (d.h. ein freier Zeitblock) eingeplant wird.', 'Blöcke bis Pause (optional)', 'z.B. 4'],
    ['maxBloeckeProSchueler', 'Maximale Zeitblöcke pro Schüler*innen pro Lehrkraft', 'Anzahl der maximalen Zeitblöcke, die für eine*n Schüler*in bei einer einzelnen Lehrkraft gebucht werden dürfen.', 'Max. Blöcke pro Schüler*in (optional)', 'z.B. 2'],
    ['gesperrteBloecke', 'Anzahl gesperrter Zeitblöcke zwischen zwei Terminen', 'Anzahl der Zeitblöcke, die zwischen zwei Terminen gesperrt bleiben, damit Erziehungsberechtigte genug Zeit für einen Raumwechsel haben.', 'Gesperrte Blöcke (optional)', 'z.B. 2'],
  ];
  const regeln = () => `
    <h2 class="st-abschnitt-titel">Berechtigungen für Lehrkräfte</h2>
    <p class="st-abschnitt-text">Legen Sie fest, welche Einstellungen und Buchungsfunktionen Lehrkräfte selbst verwalten dürfen.</p>
    ${RECHTE.map(([key, titel, text]) => `<div class="st-recht"><div><h5>${titel}</h5><p>${text}</p></div>${schalter(`data-recht="${key}"`, k.rechte[key], titel)}</div>`).join('')}
    <hr class="st-trenner">
    <h2 class="st-abschnitt-titel">Buchungsregeln</h2>
    <p class="st-abschnitt-text">Legen Sie die zeitlichen Limits für den Buchungsprozess fest. Sie sichern automatische Pausen zwischen Terminen.</p>
    ${REGELN.map(([key, titel, text, label, ph]) => `<div class="st-recht"><div><h5>${titel}</h5><p>${text}</p></div>
      ${feld(label, `<input type="number" min="1" max="50" data-regel="${key}" value="${e(k.regeln[key] ?? '')}" placeholder="${ph}">`)}</div>`).join('')}
    ${t.fehler.length ? `<div class="st-fehler"><b>Zum Veröffentlichen fehlt noch:</b><ul>${t.fehler.map(f => `<li>${e(f)}</li>`).join('')}</ul></div>` : ''}`;

  const zeichnen = () => {
    const ri = REITER.findIndex(r => r[0] === reiter);
    seite.innerHTML = `<div class="st st-weiss">
      ${kopf(t, { reiter: `<nav class="pe-reiter" role="tablist">${REITER.map(([key, text], i) => `<button type="button" role="tab" class="${key === reiter ? 'an' : i > ri ? 'aus' : ''}" aria-selected="${key === reiter}" data-reiter="${key}">${text}</button>`).join('')}</nav>` })}
      <div class="st-konfig">
        ${t.veroeffentlicht ? '<div class="st-fehler mt-0 mb-3">Der Sprechtag ist veröffentlicht. Änderungen setzen ihn wieder auf „Entwurf“ zurück – bitte danach erneut veröffentlichen.</div>' : ''}
        ${reiter === 'allgemein' ? allgemein() : reiter === 'zeitraeume' ? zeitraeume() : regeln()}
        <div class="st-fuss"><span class="st-fuss-status" data-status>${e(status)}</span>
          ${reiter === 'regeln'
            ? `<button type="button" class="st-knopf primaer" data-veroeffentlichen ${t.veroeffentlicht && !t.fehler.length ? 'disabled' : ''}>Veröffentlichen</button>`
            : '<button type="button" class="st-knopf primaer" data-weiter>Weiter</button>'}</div>
      </div></div>`;
    binden();
  };

  const statusSetzen = s => { status = s; const el = seite.querySelector('[data-status]'); if (el) el.textContent = s; };
  const speichern = async () => {
    clearTimeout(timer);
    timer = null;
    try {
      t = await senden(`${BASIS}/${id}`, k, 'PUT');
      statusSetzen('Als Entwurf gespeichert');
      const pl = seite.querySelector('.st-kopf h1 .st-pille');
      if (pl) pl.outerHTML = pille(t.phase, t.phaseText);
      const h = seite.querySelector('.st-kopf h1');
      if (h?.firstChild) h.firstChild.textContent = `${t.name || 'Neuer Sprechtag'} `;
    } catch (err) { statusSetzen(''); fehler(err); }
  };
  const spaeter = () => { statusSetzen('Wird gespeichert …'); clearTimeout(timer); timer = setTimeout(speichern, 600); };

  const binden = () => {
    seite.querySelectorAll('[data-reiter]').forEach(b => {
      b.onclick = async () => {
        if (timer) await speichern();
        reiter = b.dataset.reiter;
        history.replaceState(null, '', `#/sprechtag/${id}/bearbeiten/${reiter}`);
        zeichnen();
      };
    });
    seite.querySelectorAll('[data-k]').forEach(i => {
      i.addEventListener(i.type === 'checkbox' ? 'change' : 'input', () => {
        const f = i.dataset.k;
        k[f] = i.type === 'checkbox' ? i.checked : i.type === 'number' ? Number(i.value) || 0 : i.value;
        spaeter();
      });
    });
    seite.querySelectorAll('[data-a]').forEach(i => {
      i.addEventListener('input', () => { k.abschnitte[Number(i.dataset.a)][i.dataset.f] = i.value; spaeter(); });
    });
    seite.querySelectorAll('[data-p]').forEach(i => {
      i.addEventListener('input', () => {
        const key = i.dataset.p;
        const teile = [teil(k[key], 0), teil(k[key], 1)];
        teile[Number(i.dataset.i)] = i.value;
        if (teile[0] && !teile[1]) teile[1] = key.endsWith('Von') ? '08:00' : '18:00';
        k[key] = teile[0] ? `${teile[0]}T${teile[1]}` : '';
        if (!i.dataset.i || i.dataset.i === '0') { const z = seite.querySelector(`[data-p="${key}"][data-i="1"]`); if (z && !z.value) z.value = teile[1]; }
        spaeter();
      });
    });
    seite.querySelectorAll('[data-recht]').forEach(i => { i.onchange = () => { k.rechte[i.dataset.recht] = i.checked; spaeter(); }; });
    seite.querySelectorAll('[data-regel]').forEach(i => { i.oninput = () => { k.regeln[i.dataset.regel] = i.value ? Number(i.value) : null; spaeter(); }; });
    seite.querySelector('[data-abschnitt-neu]')?.addEventListener('click', () => {
      const letzter = k.abschnitte[k.abschnitte.length - 1];
      k.abschnitte.push({ name: `${k.abschnitte.length + 1}. Sprechtagsabschnitt`, datum: letzter?.datum ?? '', von: letzter?.bis ?? '13:00', bis: '18:00' });
      zeichnen();
      speichern();
    });
    seite.querySelectorAll('[data-abschnitt-weg]').forEach(b => {
      b.onclick = () => { k.abschnitte.splice(Number(b.dataset.abschnittWeg), 1); zeichnen(); speichern(); };
    });
    seite.querySelector('[data-weiter]')?.addEventListener('click', async () => {
      if (timer) await speichern();
      reiter = REITER[REITER.findIndex(r => r[0] === reiter) + 1][0];
      history.replaceState(null, '', `#/sprechtag/${id}/bearbeiten/${reiter}`);
      zeichnen();
      window.scrollTo(0, 0);
    });
    seite.querySelector('[data-veroeffentlichen]')?.addEventListener('click', async ev => {
      ev.currentTarget.disabled = true;
      try {
        if (timer) await speichern();
        await senden(`${BASIS}/${id}/veroeffentlichen`, {});
        toast('Sprechtag veröffentlicht – die Lehrkräfte wurden benachrichtigt.');
        kontext.navigieren(`#/sprechtag/${id}`);
      } catch (err) {
        fehler(err);
        t = await holen(`${BASIS}/${id}`).then(x => x.sprechtag);
        zeichnen();
      }
    });
  };

  zeichnen();
}

// ================================================================== Verwaltung (Administration) und „Meine Termine“ (Lehrkraft)

async function verwaltung(seite, kontext, d) {
  const t = d.sprechtag;
  const admin = d.rolle === 'admin';
  let suche = '';
  let filter = 'alle';
  const offen = new Set();
  const laufend = !['abgeschlossen'].includes(t.phase);

  const neu = async () => { d = await holen(`${BASIS}/${t.id}`); zeichnen(); };

  const raumWahl = (l, i) => {
    if (t.konfig.online) return '';
    const r = l.raeume[i] || '';
    if (!d.rechte.raeume || !laufend) return r ? `<span class="st-raum-text">${e(r)}</span>` : '';
    const gruppen = {};
    for (const x of d.raumListe) (gruppen[x.gebaeude || 'Ohne Gebäude'] ??= []).push(x);
    const bekannt = d.raumListe.some(x => x.kurz === r);
    return `<label class="st-raum"><span class="visually-hidden">Raum für ${e(t.abschnitte[i].name)}</span>
      <select data-raum="${e(l.kurz)}" data-abschnitt="${i}"><option value="">Gebäude | Raum</option>
        ${r && !bekannt ? `<option selected>${e(r)}</option>` : ''}
        ${Object.entries(gruppen).map(([g, rs]) => `<optgroup label="${e(g)}">${rs.map(x => `<option value="${e(x.kurz)}" ${x.kurz === r ? 'selected' : ''} title="${e(x.name)}">${e(x.kurz)}</option>`).join('')}</optgroup>`).join('')}
      </select></label>`;
  };

  const eintraege = (l, i) => {
    const liste = [
      ...l.termine.filter(x => x.abschnitt === i && x.beginn).map(x => ({ von: x.beginn, html: `<div class="st-eintrag">${statusPille(x)}
        <span class="st-eintrag-zeit">${e(x.beginn)} – ${e(x.ende)} Uhr</span>
        <span class="st-eintrag-name">${e(name(x.schueler))}<small>${e(x.schueler.klasse)}</small></span>
        <button type="button" class="st-symbol" data-termin="${x.id}" data-lehrer="${e(l.kurz)}" title="Bearbeiten" aria-label="Termin bearbeiten">${bi('pencil')}</button></div>` })),
      ...l.abwesenheiten.filter(x => x.abschnitt === i).map(x => ({ von: x.beginn, html: `<div class="st-eintrag">${pille('', 'Abwesend')}
        <span class="st-eintrag-zeit">${e(x.beginn)} – ${e(x.ende)} Uhr</span><span class="st-eintrag-name">${e(x.anmerkung)}</span>
        ${d.rechte.abwesenheiten && laufend ? `<button type="button" class="st-symbol" data-abw="${x.id}" data-lehrer="${e(l.kurz)}" title="Bearbeiten" aria-label="Abwesenheit bearbeiten">${bi('pencil')}</button>` : ''}</div>` })),
    ];
    return liste.sort((a, b) => a.von.localeCompare(b.von)).map(x => x.html).join('');
  };

  const abschnitte = l => t.abschnitte.map((a, i) => `<div class="st-abschnitt">
      <div class="st-abschnitt-kopf"><b>${e(a.name)}</b>
        <span class="st-meta">${bi('calendar')} ${e(a.datumText)}</span><span class="st-meta">${bi('clock')} ${e(a.von)} – ${e(a.bis)} Uhr</span>
        <span class="st-abschnitt-rechts">${raumWahl(l, i)}${laufend ? `<button type="button" class="st-plus" data-plus="${e(l.kurz)}" data-abschnitt="${i}" title="Hinzufügen" aria-label="Hinzufügen">${bi('plus-lg')}</button>` : ''}</span></div>
      ${eintraege(l, i)}
    </div>`).join('') + (l.termine.some(x => x.status === 'einladung') ? `<div class="st-abschnitt"><div class="st-einladungen-titel">Einladungen (Uhrzeit wählen die Erziehungsberechtigten)</div>
      ${l.termine.filter(x => x.status === 'einladung').map(x => `<div class="st-eintrag">${statusPille(x)}<span class="st-eintrag-name">${e(name(x.schueler))}<small>${e(x.schueler.klasse)}${x.verpflichtend ? ' · verpflichtend' : ''}</small></span>
        <button type="button" class="st-symbol" data-zurueckziehen="${x.id}" title="Einladung zurückziehen" aria-label="Einladung zurückziehen">${bi('x-lg')}</button></div>`).join('')}</div>` : '');

  const passend = l => (!suche || `${l.kurz} ${l.name}`.toLowerCase().includes(suche.toLowerCase()))
    && (filter === 'alle' || (filter === 'raum' ? l.keinRaum : l.keineTermine));

  const zeichnen = () => {
    const druck = (admin && t.bearbeitbar ? `<a class="st-rund" href="#/sprechtag/${t.id}/bearbeiten" title="Bearbeiten" aria-label="Sprechtag bearbeiten">${bi('pencil')}</a>` : '')
      + `<button type="button" class="st-rund" data-drucken title="Drucken (PDF)" aria-label="Drucken (PDF)">${bi('printer')}</button>`;
    if (admin) {
      const liste = d.lehrkraefte.filter(passend);
      seite.innerHTML = `<div class="st">${kopf(t, { rechts: druck, unter: `<div class="st-kopf-unter">${e(t.datumText)} · ${pille(t.phase, t.phaseText)}</div>`, pilleAn: false })}
        <div class="st-innen">
          <h2 class="st-titel">Verwaltung</h2>
          <p class="st-text">Hier verwalten Sie Abwesenheiten, Räume und Termine der Lehrkräfte.${t.konfig.online ? ' Dies ist ein Online-Sprechtag: Meeting-Links werden direkt im jeweiligen Termin hinterlegt.' : ' Für Präsenz-Termine muss der jeweiligen Lehrkraft vorab ein Raum zugewiesen sein.'} Bereits gebuchte Termine können hier bearbeitet, abgesagt oder neu angelegt werden.</p>
          <div class="st-werkzeuge">
            <label class="st-suche">${bi('search')}<input type="search" placeholder="Lehrkräfte suchen" aria-label="Lehrkräfte suchen" data-suche value="${e(suche)}"></label>
            <div class="st-filter" role="group" aria-label="Filter">${[['alle', 'Alle'], ...(t.konfig.online ? [] : [['raum', 'Kein Raum']]), ['termine', 'Keine Termine']].map(([k, text]) => `<button type="button" class="${filter === k ? 'an' : ''}" data-filter="${k}" aria-pressed="${filter === k}">${text}</button>`).join('')}</div>
          </div>
          <div class="st-liste">${liste.map(l => `<section class="st-lk ${offen.has(l.kurz) ? 'offen' : ''}">
              <button type="button" class="st-lk-kopf" data-auf="${e(l.kurz)}" aria-expanded="${offen.has(l.kurz)}"><b>${e(l.titel)}</b>
                ${l.keinRaum ? '<span class="st-chip">Kein Raum</span>' : ''}${l.keineTermine ? '<span class="st-chip">Keine Termine</span>' : ''}${bi('chevron-down')}</button>
              <div class="st-lk-rumpf" ${offen.has(l.kurz) ? '' : 'hidden'}>${abschnitte(l)}</div>
            </section>`).join('') || '<div class="st-leer">Keine Lehrkräfte gefunden.</div>'}</div>
        </div></div>`;
    } else {
      const l = d.lehrkraefte[0];
      seite.innerHTML = `<div class="st">${kopf(t, { rechts: druck, unter: `<div class="st-kopf-unter">${e(t.datumText)} · ${pille(t.phase, t.phaseText)}</div>`, pilleAn: false })}
        <div class="st-innen">
          <h2 class="st-titel">Meine Termine</h2>
          <p class="st-text">Hier sehen Sie Ihre Termine und Abwesenheiten für den Sprechtag.${t.konfig.online ? ' Dies ist ein Online-Sprechtag. Je nach Berechtigung müssen den Einladungen Meeting-Links hinzugefügt werden.' : ''}</p>
          ${l ? `<div class="st-karte-weiss">${abschnitte(l)}</div>` : '<div class="st-leer">Ihrem Konto ist keine Lehrkraft zugeordnet.</div>'}
        </div></div>`;
    }
    binden();
  };

  const binden = () => {
    seite.querySelector('[data-suche]')?.addEventListener('input', ev => {
      suche = ev.target.value;
      const pos = ev.target.selectionStart;
      zeichnen();
      const i = seite.querySelector('[data-suche]');
      i.focus();
      i.setSelectionRange(pos, pos);
    });
    seite.querySelectorAll('[data-filter]').forEach(b => { b.onclick = () => { filter = b.dataset.filter; zeichnen(); }; });
    seite.querySelectorAll('[data-auf]').forEach(b => {
      b.onclick = () => { const k = b.dataset.auf; if (offen.has(k)) offen.delete(k); else offen.add(k); zeichnen(); };
    });
    seite.querySelector('[data-drucken]')?.addEventListener('click', ev => {
      const datei = `sprechtag-${t.id}`;
      menue(ev.currentTarget, admin ? [
        ['Gesamtplan (PDF)', () => pdf(`/${t.id}/plan.pdf`, `${datei}.pdf`)],
        ['Gesamtplan mit freien Zeilen (PDF)', () => pdf(`/${t.id}/plan.pdf?leer=true`, `${datei}-alle.pdf`)],
      ] : [
        ['Mein Terminplan (PDF)', () => pdf(`/${t.id}/plan.pdf`, `${datei}.pdf`)],
        ['Terminplan mit freien Zeilen (PDF)', () => pdf(`/${t.id}/plan.pdf?leer=true`, `${datei}-alle.pdf`)],
      ]);
    });
    seite.querySelectorAll('[data-raum]').forEach(s => {
      s.onchange = async () => {
        try { await senden(`${BASIS}/${t.id}/raum`, { lehrer: s.dataset.raum, abschnitt: Number(s.dataset.abschnitt), raum: s.value }, 'PUT'); toast('Raum gespeichert.'); await neu(); }
        catch (err) { fehler(err); }
      };
    });
    seite.querySelectorAll('[data-plus]').forEach(b => {
      b.onclick = () => {
        const l = d.lehrkraefte.find(x => x.kurz === b.dataset.plus);
        const i = Number(b.dataset.abschnitt);
        const punkte = [];
        if (!['phase1_beendet', 'geschlossen', 'durchfuehrung'].includes(t.phase)) punkte.push(['Einladung senden', () => einladungPanel(l)]);
        if (d.rechte.buchen && t.phase !== 'durchfuehrung') punkte.push(['Termin buchen', () => terminPanel(l, i)]);
        if (d.rechte.abwesenheiten) punkte.push(['Abwesenheit eintragen', () => abwesenheitPanel(l, i)]);
        if (admin) punkte.push(['Terminplan drucken (PDF)', () => pdf(`/${t.id}/plan.pdf?lehrer=${encodeURIComponent(l.kurz)}&leer=true`, `sprechtag-${t.id}-${l.kurz.toLowerCase()}.pdf`)]);
        menue(b, punkte);
      };
    });
    seite.querySelectorAll('[data-abw]').forEach(b => {
      b.onclick = () => {
        const l = d.lehrkraefte.find(x => x.kurz === b.dataset.lehrer);
        const a = l.abwesenheiten.find(x => x.id === Number(b.dataset.abw));
        abwesenheitPanel(l, a.abschnitt, a);
      };
    });
    seite.querySelectorAll('[data-termin]').forEach(b => {
      b.onclick = () => {
        const l = d.lehrkraefte.find(x => x.kurz === b.dataset.lehrer);
        terminBearbeiten(l, l.termine.find(x => x.id === Number(b.dataset.termin)));
      };
    });
    seite.querySelectorAll('[data-zurueckziehen]').forEach(b => {
      b.onclick = async () => {
        if (!await bestaetigen('Einladung zurückziehen', 'Die Einladung wird zurückgezogen; die Erziehungsberechtigten werden benachrichtigt, falls sie sie bereits erhalten haben.', 'Zurückziehen')) return;
        try { await senden(`${BASIS}/${t.id}/termin/${b.dataset.zurueckziehen}/absagen`, {}); toast('Einladung zurückgezogen.'); await neu(); }
        catch (err) { fehler(err); }
      };
    });
  };

  const kopfMeta = (l, i) => {
    const a = t.abschnitte[i];
    return `<div class="st-panel-meta"><span class="st-meta">${bi('calendar')} ${e(a.datumText)}</span><span class="st-meta">${bi('clock')} ${e(a.von)} – ${e(a.bis)} Uhr</span></div>
      <div class="st-panel-zeile"><span>${e(a.name)}</span><span>${e(t.konfig.online ? 'Online' : l.raeume[i] || '')}</span></div>`;
  };
  const zeitOptionen = (i, wahl) => {
    const a = t.abschnitte[i];
    const res = [];
    for (let m = minuten(a.von); m <= minuten(a.bis); m += t.konfig.dauer) res.push(zeit(m));
    return res.map(z => `<option ${z === wahl ? 'selected' : ''}>${z}</option>`).join('');
  };

  function abwesenheitPanel(l, i, alt = null) {
    const a = t.abschnitte[i];
    const p = seitenpanel({
      titel: alt ? 'Abwesenheit bearbeiten' : 'Abwesenheit eintragen',
      inhalt: `<p class="st-panel-unter">Zeitslot als Pause oder Abwesenheit sperren.${admin ? ` <b>${e(l.titel)}</b>` : ''}</p>${kopfMeta(l, i)}
        <div class="st-panel-raster">
          ${feld('Startzeit', `<select data-von>${zeitOptionen(i, alt?.beginn ?? a.von)}</select>`)}
          ${feld('Endzeit', `<select data-bis>${zeitOptionen(i, alt?.ende ?? zeit(Math.min(minuten(a.von) + t.konfig.dauer, minuten(a.bis))))}</select>`)}
          <div class="ganz">${feld('Anmerkungen', `<textarea maxlength="255" data-anm>${e(alt?.anmerkung ?? '')}</textarea>`)}<span class="st-zaehler" data-zaehler>${(alt?.anmerkung ?? '').length}/255</span></div>
        </div>
        <p class="small text-secondary mb-0">Termine der Lehrkraft in diesem Zeitraum werden automatisch abgesagt; die Beteiligten werden benachrichtigt.</p>`,
      knoepfe: `${alt ? '<button type="button" class="st-knopf klein links" data-weg>Löschen</button>' : ''}<button type="button" class="st-knopf primaer klein" data-ok>Abwesenheit speichern</button>`,
      beimOeffnen: (el, pn) => {
        const anm = el.querySelector('[data-anm]');
        anm.oninput = () => { el.querySelector('[data-zaehler]').textContent = `${anm.value.length}/255`; };
        el.querySelector('[data-ok]').onclick = async () => {
          try {
            await senden(`${BASIS}/${t.id}/abwesenheit`, { id: alt?.id ?? null, lehrer: l.kurz, abschnitt: i, beginn: el.querySelector('[data-von]').value, ende: el.querySelector('[data-bis]').value, anmerkung: anm.value });
            pn.schliessen(); toast('Abwesenheit gespeichert.'); await neu();
          } catch (err) { fehler(err); }
        };
        el.querySelector('[data-weg]')?.addEventListener('click', async () => {
          try { await api(`${BASIS}/${t.id}/abwesenheit/${alt.id}`, { methode: 'DELETE' }); pn.schliessen(); toast('Abwesenheit gelöscht.'); await neu(); }
          catch (err) { fehler(err); }
        });
      },
    });
    return p;
  }

  async function schuelerLaden(l) { return holen(`${BASIS}/${t.id}/schueler?lehrer=${encodeURIComponent(l.kurz)}`); }

  async function einladungPanel(l) {
    let liste;
    try { liste = await schuelerLaden(l); } catch (err) { return fehler(err); }
    seitenpanel({
      titel: 'Einladung senden',
      inhalt: `<p class="st-panel-unter">Die Erziehungsberechtigten wählen die Uhrzeit selbst; gebuchte Termine sind sofort bestätigt.${admin ? ` <b>${e(l.titel)}</b>` : ''}${['entwurf', 'verwaltung'].includes(t.phase) ? ' Versand an die Erziehungsberechtigten mit Beginn der 1. Anmeldephase.' : ''}</p>
        <label class="st-suche">${bi('search')}<input type="search" placeholder="Schüler*innen suchen" data-filter-sch></label>
        <div class="st-schueler-liste">${liste.map(s => `<label class="${s.vorhanden ? 'aus' : ''}" data-zeile="${e(`${name(s)} ${s.klasse}`.toLowerCase())}"><input type="checkbox" class="form-check-input m-0" value="${s.id}" ${s.vorhanden ? 'disabled' : ''}>${e(name(s))}<small>${e(s.klasse)}${s.vorhanden ? ' · bereits eingeladen' : ''}</small></label>`).join('') || '<div class="p-3 text-secondary">Keine Schüler*innen im Unterricht dieser Lehrkraft.</div>'}</div>
        ${feld('Nachricht (optional)', '<textarea maxlength="1000" data-text></textarea>')}
        <div class="st-recht"><div><h5>Verpflichtend</h5><p>Das Gespräch ist aus Sicht der Lehrkraft verpflichtend.</p></div>${schalter('data-pflicht', false, 'Verpflichtend')}</div>`,
      knoepfe: '<button type="button" class="st-knopf primaer klein" data-ok>Einladung senden</button>',
      beimOeffnen: (el, pn) => {
        el.querySelector('[data-filter-sch]').oninput = ev => {
          const q = ev.target.value.toLowerCase();
          el.querySelectorAll('[data-zeile]').forEach(z => { z.hidden = q && !z.dataset.zeile.includes(q); });
        };
        el.querySelector('[data-ok]').onclick = async () => {
          const ids = [...el.querySelectorAll('.st-schueler-liste input:checked')].map(i => Number(i.value));
          try {
            const r = await senden(`${BASIS}/${t.id}/einladung`, { lehrer: l.kurz, schuelerIds: ids, text: el.querySelector('[data-text]').value, verpflichtend: el.querySelector('[data-pflicht]').checked });
            pn.schliessen(); toast(`${r.anzahl} ${r.anzahl === 1 ? 'Einladung' : 'Einladungen'} angelegt.`); await neu();
          } catch (err) { fehler(err); }
        };
      },
    });
  }

  async function freieZeiten(l, i, ausser) {
    return holen(`${BASIS}/${t.id}/frei?lehrer=${encodeURIComponent(l.kurz)}&abschnitt=${i}${ausser ? `&ausser=${ausser}` : ''}`);
  }

  async function terminPanel(l, i) {
    let liste, frei;
    try { [liste, frei] = await Promise.all([schuelerLaden(l), freieZeiten(l, i)]); } catch (err) { return fehler(err); }
    seitenpanel({
      titel: 'Termin buchen',
      inhalt: `<p class="st-panel-unter">Verbindlicher Termin mit festem Zeitslot (z. B. nach telefonischer Absprache). Die Erziehungsberechtigten nehmen ihn an oder lehnen ihn ab.${admin ? ` <b>${e(l.titel)}</b>` : ''}</p>${kopfMeta(l, i)}
        <div class="st-panel-raster">
          <div class="ganz">${feld('Schüler*in', `<select data-kind><option value="">Bitte wählen</option>${liste.map(s => `<option value="${s.id}">${e(name(s))} (${e(s.klasse)})</option>`).join('')}</select>`)}</div>
          ${feld('Startzeit', `<select data-von>${frei.map(z => `<option>${z.von}</option>`).join('') || '<option value="">Keine freie Zeit</option>'}</select>`)}
          ${feld('Dauer', `<select data-bloecke>${[1, 2, 3].map(n => `<option value="${n}">${n * t.konfig.dauer} Minuten</option>`).join('')}</select>`)}
          ${t.konfig.online ? `<div class="ganz">${feld('Meeting-Link', '<input type="url" maxlength="500" placeholder="https://…" data-link>')}</div>` : ''}
          <div class="ganz">${feld('Anmerkung (optional)', '<textarea maxlength="1000" data-anm></textarea>')}</div>
        </div>`,
      knoepfe: '<button type="button" class="st-knopf primaer klein" data-ok>Termin buchen</button>',
      beimOeffnen: (el, pn) => {
        el.querySelector('[data-ok]').onclick = async () => {
          const von = el.querySelector('[data-von]').value;
          try {
            await senden(`${BASIS}/${t.id}/termin`, {
              lehrer: l.kurz, abschnitt: i, beginn: von, ende: zeit(minuten(von) + Number(el.querySelector('[data-bloecke]').value) * t.konfig.dauer),
              schuelerId: Number(el.querySelector('[data-kind]').value) || 0, anmerkung: el.querySelector('[data-anm]').value, link: el.querySelector('[data-link]')?.value ?? '',
            });
            pn.schliessen(); toast('Termin angelegt.'); await neu();
          } catch (err) { fehler(err); }
        };
      },
    });
  }

  async function terminBearbeiten(l, x) {
    const darf = d.rechte.bearbeiten && laufend;
    let frei = [];
    if (darf) try { frei = await freieZeiten(l, x.abschnitt, x.id); } catch { frei = []; }
    const dauer = minuten(x.ende) - minuten(x.beginn);
    seitenpanel({
      titel: 'Termin',
      inhalt: `${kopfMeta(l, x.abschnitt)}
        <p class="mb-3">${statusPille(x)} <b class="ms-2">${e(name(x.schueler))}</b> <span class="text-secondary">${e(x.schueler.klasse)}</span>${admin ? ` · ${e(l.titel)}` : ''}</p>
        <div class="st-panel-raster">
          ${feld('Startzeit', `<select data-von ${darf ? '' : 'disabled'}><option>${e(x.beginn)}</option>${frei.filter(z => z.von !== x.beginn).map(z => `<option>${z.von}</option>`).join('')}</select>`)}
          ${feld('Endzeit', `<input type="text" value="${e(x.ende)}" disabled data-ende>`)}
          ${t.konfig.online ? `<div class="ganz">${feld('Meeting-Link', `<input type="url" maxlength="500" placeholder="https://…" data-link value="${e(x.link)}" ${darf ? '' : 'disabled'}>`)}</div>` : ''}
          <div class="ganz">${feld('Anmerkung', `<textarea maxlength="1000" data-anm ${darf ? '' : 'disabled'}>${e(x.anmerkung)}</textarea>`)}</div>
        </div>`,
      knoepfe: darf ? '<button type="button" class="st-knopf klein links" data-absagen>Termin absagen</button><button type="button" class="st-knopf primaer klein" data-ok>Speichern</button>' : '',
      beimOeffnen: (el, pn) => {
        const von = el.querySelector('[data-von]');
        von.onchange = () => { el.querySelector('[data-ende]').value = zeit(minuten(von.value) + dauer); };
        el.querySelector('[data-ok]')?.addEventListener('click', async () => {
          try {
            await senden(`${BASIS}/${t.id}/termin/${x.id}`, { abschnitt: x.abschnitt, beginn: von.value, ende: zeit(minuten(von.value) + dauer), anmerkung: el.querySelector('[data-anm]').value, link: el.querySelector('[data-link]')?.value ?? null }, 'PUT');
            pn.schliessen(); toast('Termin gespeichert.'); await neu();
          } catch (err) { fehler(err); }
        });
        el.querySelector('[data-absagen]')?.addEventListener('click', async () => {
          if (!await bestaetigen('Termin absagen', 'Der Termin wird abgesagt; die Erziehungsberechtigten werden benachrichtigt.', 'Absagen')) return;
          try { await senden(`${BASIS}/${t.id}/termin/${x.id}/absagen`, {}); pn.schliessen(); toast('Termin abgesagt.'); await neu(); }
          catch (err) { fehler(err); }
        });
      },
    });
  }

  zeichnen();
}

// ================================================================== Erziehungsberechtigte

async function eltern(seite, kontext, d) {
  const t = d.sprechtag;
  const neu = async () => { d = await holen(`${BASIS}/${t.id}`); zeichnen(); };
  const kind = id => d.kinder.find(k => k.id === id);

  const ekarte = (x, { knoepfe = '', klick = false, pilleAn = false } = {}) => `<article class="st-ekarte ${klick ? 'klickbar' : ''}" ${klick ? `data-termin="${x.id}" tabindex="0"` : ''}>
      <b>${e(x.lehrerTitel)}</b>${pilleAn ? statusPille(x) : ''}
      <span class="unter">${e(name(x.schueler))}</span>
      ${x.beginn ? `<span class="zeit">${e(x.beginn)} – ${e(x.ende)} Uhr</span>` : ''}
      ${x.beginn ? (t.konfig.online ? (x.link ? `<span class="unter"><a href="${e(x.link)}" target="_blank" rel="noopener">Online-Meeting öffnen</a></span>` : '<span class="unter">Online (Link folgt)</span>') : `<span class="unter">${e(x.raum || 'Unbekannter Raum')}</span>`) : ''}
      ${x.verpflichtend ? '<span class="unter">Verpflichtend</span>' : ''}
      ${x.anmerkung ? `<span class="unter">${e(x.anmerkung)}</span>` : ''}
      ${knoepfe ? `<div class="knoepfe">${knoepfe}</div>` : ''}
    </article>`;

  const zeichnen = () => {
    const tage = {};
    for (const x of d.termine) (tage[x.datum] ??= { text: `${x.wochentag}, ${x.datumText}`, liste: [] }).liste.push(x);
    const offen = !['geschlossen', 'durchfuehrung', 'abgeschlossen'].includes(t.phase);
    const druck = d.termine.length ? `<button type="button" class="st-rund" data-elternplan title="Meine Termine als PDF" aria-label="Meine Termine als PDF">${bi('printer')}</button>` : '';
    seite.innerHTML = `<div class="st">${kopf(t, { rechts: druck, unter: `<div class="st-kopf-unter">${e(t.datumText)}</div>`, pilleAn: false })}
      <div class="st-innen"><div class="st-eltern">
        ${d.hinweis ? `<div class="st-banner">${bi('info-circle-fill')}<div><b>${e(d.hinweis)}</b>${t.phase === 'phase1' ? '<p>Derzeit können Termine nur mit einer Einladung gebucht werden. Die freie Buchung ist ab der nächsten Anmeldephase möglich.</p>' : ''}</div></div>` : ''}
        ${d.einladungen.length || d.anfragen.length ? `<h2 class="st-gruppe">Terminanfragen</h2>
          ${d.einladungen.map(x => ekarte(x, { knoepfe: offen ? `<button type="button" class="st-knopf primaer klein" data-einladung="${x.id}">Termin buchen</button>` : '' })).join('')}
          ${d.anfragen.map(x => ekarte(x, { knoepfe: offen ? `<button type="button" class="st-knopf klein" data-ablehnen="${x.id}">Ablehnen</button><button type="button" class="st-knopf primaer klein" data-annehmen="${x.id}">Annehmen</button>` : '' })).join('')}` : ''}
        <h2 class="st-gruppe">Meine Termine</h2><p class="st-gruppe-text">Ihre bestätigten Gesprächstermine.</p>
        ${Object.values(tage).map(g => `<div class="st-tag">${e(g.text)}</div>${g.liste.map(x => ekarte(x, { klick: true, pilleAn: true })).join('')}`).join('') || '<div class="st-leer">Noch keine bestätigten Termine.</div>'}
        ${d.freiBuchbar ? `<h2 class="st-gruppe">Termin buchen</h2><p class="st-gruppe-text">Wählen Sie eine Lehrkraft Ihres Kindes.</p>
          ${d.kinder.map(k => `<div class="st-tag">${e(name(k))} · ${e(k.klasse)}</div>
            ${k.lehrkraefte.map(l => `<article class="st-ekarte klickbar" data-frei="${e(l.kurz)}" data-kind="${k.id}" tabindex="0"><b>(${e(l.kurz)}) ${e(l.name)}</b>
              <span class="unter">${e([l.klassenleitung ? 'Klassenleitung' : '', ...l.faecher].filter(Boolean).join(', '))}</span></article>`).join('') || '<div class="st-leer">Keine Lehrkräfte gefunden.</div>'}`).join('')}` : ''}
      </div></div></div>`;
    binden();
  };

  const binden = () => {
    seite.querySelector('[data-elternplan]')?.addEventListener('click', () => pdf(`/${t.id}/elternplan.pdf`, `sprechtag-${t.id}-termine.pdf`));
    seite.querySelectorAll('[data-einladung]').forEach(b => {
      b.onclick = () => { const x = d.einladungen.find(y => y.id === Number(b.dataset.einladung)); buchenPanel(x.lehrer, x.lehrerTitel, x.schueler.id, x.id); };
    });
    seite.querySelectorAll('[data-frei]').forEach(k => {
      const auf = () => { const l = kind(Number(k.dataset.kind)).lehrkraefte.find(x => x.kurz === k.dataset.frei); buchenPanel(l.kurz, `(${l.kurz}) ${l.name}`, Number(k.dataset.kind), null); };
      k.onclick = auf;
      k.onkeydown = ev => { if (ev.key === 'Enter') auf(); };
    });
    for (const [attr, pfad, text] of [['annehmen', 'annehmen', 'Termin angenommen.'], ['ablehnen', 'ablehnen', 'Termin abgelehnt.']])
      seite.querySelectorAll(`[data-${attr}]`).forEach(b => {
        b.onclick = async () => {
          try { await senden(`${BASIS}/${t.id}/termin/${b.dataset[attr]}/${pfad}`, {}); toast(text); await neu(); }
          catch (err) { fehler(err); }
        };
      });
    seite.querySelectorAll('.st-ekarte[data-termin]').forEach(k => {
      const auf = ev => { if (!ev.target.closest('a')) terminPanel(d.termine.find(x => x.id === Number(k.dataset.termin))); };
      k.onclick = auf;
      k.onkeydown = ev => { if (ev.key === 'Enter') auf(ev); };
    });
  };

  /** Freie Zeiten wählen und buchen (Einladung bzw. freie Buchung) oder Zeit eines Termins ändern. */
  async function buchenPanel(lehrer, titel, kindId, einladungId, termin = null) {
    let abschnitt = termin?.abschnitt ?? 0;
    let wahl = null;
    const laden = () => holen(`${BASIS}/${t.id}/frei?lehrer=${encodeURIComponent(lehrer)}&abschnitt=${abschnitt}&schuelerId=${kindId}${termin ? `&ausser=${termin.id}` : ''}`);
    let frei;
    try { frei = await laden(); } catch (err) { return fehler(err); }
    const k = kind(kindId);
    seitenpanel({
      titel: termin ? 'Termin ändern' : 'Termin buchen',
      inhalt: `<p class="st-panel-unter"><b>${e(titel)}</b> · ${e(k ? name(k) : '')}</p>
        ${t.abschnitte.length > 1 ? `<div class="mb-3">${feld('Abschnitt', `<select data-abschnitt>${t.abschnitte.map(a => `<option value="${a.index}" ${a.index === abschnitt ? 'selected' : ''}>${e(a.name)} – ${e(a.wochentag)}, ${e(a.datumText)}, ${e(a.von)}–${e(a.bis)} Uhr</option>`).join('')}</select>`)}</div>`
          : `<div class="st-panel-meta"><span class="st-meta">${bi('calendar')} ${e(t.abschnitte[0].datumText)}</span><span class="st-meta">${bi('clock')} ${e(t.abschnitte[0].von)} – ${e(t.abschnitte[0].bis)} Uhr</span></div>`}
        <div class="st-feld"><span>Freie Zeiten</span><div class="st-zeiten" data-zeiten></div></div>`,
      knoepfe: `<button type="button" class="st-knopf primaer klein" data-ok disabled>${termin ? 'Termin ändern' : 'Termin buchen'}</button>`,
      beimOeffnen: (el, pn) => {
        const zeiten = () => {
          el.querySelector('[data-zeiten]').innerHTML = frei.map(z => `<button type="button" data-z="${z.von}" class="${z.von === wahl ? 'an' : ''}">${z.von}</button>`).join('')
            || '<span class="st-zeiten-leer">Keine freien Zeiten in diesem Abschnitt.</span>';
          el.querySelectorAll('[data-z]').forEach(b => { b.onclick = () => { wahl = b.dataset.z; el.querySelector('[data-ok]').disabled = false; zeiten(); }; });
        };
        zeiten();
        el.querySelector('[data-abschnitt]')?.addEventListener('change', async ev => {
          abschnitt = Number(ev.target.value); wahl = null; el.querySelector('[data-ok]').disabled = true;
          try { frei = await laden(); } catch (err) { fehler(err); frei = []; }
          zeiten();
        });
        el.querySelector('[data-ok]').onclick = async () => {
          try {
            if (termin) await senden(`${BASIS}/${t.id}/termin/${termin.id}`, { abschnitt, beginn: wahl }, 'PUT');
            else await senden(`${BASIS}/${t.id}/buchen`, { lehrer, abschnitt, beginn: wahl, schuelerId: kindId, einladungId });
            pn.schliessen(); toast(termin ? 'Termin geändert.' : 'Termin gebucht.'); await neu();
          } catch (err) { fehler(err); }
        };
      },
    });
  }

  function terminPanel(x) {
    seitenpanel({
      titel: 'Gesprächstermin',
      inhalt: `<p class="st-panel-unter"><b>${e(x.lehrerTitel)}</b> · ${e(name(x.schueler))}</p>
        <div class="st-panel-meta"><span class="st-meta">${bi('calendar')} ${e(x.wochentag)}, ${e(x.datumText)}</span><span class="st-meta">${bi('clock')} ${e(x.beginn)} – ${e(x.ende)} Uhr</span></div>
        <div class="st-panel-zeile"><span>${e(x.abschnittName)}</span><span>${t.konfig.online ? (x.link ? `<a href="${e(x.link)}" target="_blank" rel="noopener">Online-Meeting öffnen</a>` : 'Online') : e(x.raum || 'Unbekannter Raum')}</span></div>
        ${x.anmerkung ? `<p>${e(x.anmerkung)}</p>` : ''}`,
      knoepfe: `<button type="button" class="st-knopf klein links" data-absagen>Termin absagen</button>${d.aendern ? '<button type="button" class="st-knopf primaer klein" data-aendern>Zeit ändern</button>' : ''}`,
      beimOeffnen: (el, pn) => {
        el.querySelector('[data-absagen]').onclick = async () => {
          if (!await bestaetigen('Termin absagen', 'Der Termin wird abgesagt; die Lehrkraft wird benachrichtigt.', 'Absagen')) return;
          try { await senden(`${BASIS}/${t.id}/termin/${x.id}/absagen`, {}); pn.schliessen(); toast('Termin abgesagt.'); await neu(); }
          catch (err) { fehler(err); }
        };
        el.querySelector('[data-aendern]')?.addEventListener('click', () => { pn.schliessen(); buchenPanel(x.lehrer, x.lehrerTitel, x.schueler.id, null, x); });
      },
    });
  }

  zeichnen();
}

// ================================================================== Startseite

/** Hinweiskarte(n) „Sprechtag“ auf der Startseite (nach „Letzte Anmeldung“). */
export async function startkarte(wurzel) {
  if (!wurzel) return;
  let karten = [];
  try { karten = await holen(`${BASIS}/start`); } catch { return; }
  if (!karten.length || !wurzel.isConnected) return;
  stileLaden();
  const html = karten.map(k => `<section class="heute-karte st-start">
      <span class="st-start-sym">${icon('aktive')}</span>
      <div><a href="#/sprechtag/${k.id}" class="st-start-titel">${e(k.name)}</a> <span class="st-start-datum">${e(k.datumText)}</span> ${pille(k.phase, k.phaseText)}
        <div class="st-start-text">${e(k.text)}</div></div>
      <a class="ms-auto st-start-link" href="#/sprechtag/${k.id}">Zum Sprechtag</a></section>`).join('');
  const anmeldung = wurzel.querySelector('.heute-anmeldung');
  if (anmeldung) anmeldung.insertAdjacentHTML('afterend', html);
  else wurzel.insertAdjacentHTML('afterbegin', html);
}
