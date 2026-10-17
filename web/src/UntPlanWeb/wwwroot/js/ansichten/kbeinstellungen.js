// Klassenbuch | Einstellungen (Recht „Klassenbuch-Einstellungen“) im modernen Design: Reiter Hauptseite, Allgemein, Unterricht und Noten
// als weiße Karten mit Schaltern, Auswahlfeldern und Hilfetexten; unten eine feste Leiste mit „Speichern“. Unterricht | Einstellungen
// (#/kbeinstellungen/unterricht) zeigt denselben Reiter „Unterricht“ (Prüfungen). Ohne Recht sind alle Felder nur lesbar.
// Leere Zahlenfelder bedeuten „unbegrenzt“ (Standard) – so bleiben Lehrstoff, Notizen und „Findet online statt“ ohne Wochengrenze.
import { e, holen, senden, toast, lade, WOCHENTAGE } from '../ui.js';

/** Stile der Einstellungs- und Stammdatenseiten (einmal nachladen). */
export function cssLaden() {
  if (document.querySelector('link[data-kbe-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/kbeinstellungen.css', import.meta.url).href;
  l.dataset.kbeCss = '';
  document.head.append(l);
}

const REITER = [
  ['hauptseite', 'Hauptseite', 'layout-text-window-reverse'],
  ['allgemein', 'Allgemein', 'sliders2'],
  ['unterricht', 'Unterricht', 'journal-check'],
  ['noten', 'Noten', 'award'],
];

const STATUS = [['offen', 'offen'], ['entschuldigt', 'entschuldigt'], ['unentschuldigt', 'nicht entschuldigt']];

export async function zeigen(seite, kontext, [reiterParam]) {
  cssLaden();
  const nurUnterricht = reiterParam === 'unterricht' && location.hash.startsWith('#/kbeinstellungen/unterricht');
  let reiter = REITER.some(r => r[0] === reiterParam) ? reiterParam : 'hauptseite';
  const [d, noten] = await Promise.all([holen('/api/app/klassenbuch/einstellungen'), holen('/api/app/notenschemen').catch(() => ({ schemen: [], gesamt: null }))]);
  let gespeichert = structuredClone(d.einstellungen);
  let w = structuredClone(d.einstellungen);
  const darf = d.darf;
  const aus = darf ? '' : 'disabled';
  const geaendert = () => JSON.stringify(w) !== JSON.stringify(gespeichert);

  // ---------------------------------------------------------------- Bausteine

  const id = n => `kbe-${n}`;
  const hilfe = t => t ? `<p class="kbe-hilfe">${t}</p>` : '';
  const zeile = (label, inhalt, text = '', fuer = '') => `<div class="kbe-zeile"><div class="kbe-label">${fuer ? `<label for="${fuer}">${e(label)}</label>` : `<span>${e(label)}</span>`}</div>
    <div class="kbe-wert">${inhalt}${hilfe(text)}</div></div>`;
  const schalter = (n, label, text = '') => `<div class="kbe-zeile kbe-schalterzeile"><label class="kbe-schalter" for="${id(n)}">
      <input type="checkbox" role="switch" id="${id(n)}" data-feld="${n}" ${w[n] ? 'checked' : ''} ${aus}><span class="kbe-schiene" aria-hidden="true"></span>
      <span class="kbe-schalter-text">${e(label)}</span></label>${hilfe(text)}</div>`;
  const radios = (n, liste, zusatz = {}) => `<div class="kbe-radios" role="radiogroup">${liste.map(([v, t]) => `<label class="kbe-radio">
      <input type="radio" name="${id(n)}" data-feld="${n}" value="${v}" ${w[n] === v ? 'checked' : ''} ${aus}><span>${e(t)}</span>${zusatz[v] ?? ''}</label>`).join('')}</div>`;
  const zahl = (n, { min = 0, max = 3650, leer = '', einheit = '', breit = false } = {}) => `<span class="kbe-zahl ${breit ? 'breit' : ''}"><input type="number" class="form-control" id="${id(n)}" data-feld="${n}" data-zahl
      min="${min}" max="${max}" value="${w[n] ?? ''}" ${leer ? `placeholder="${e(leer)}" data-leer` : ''} ${aus}>${einheit ? `<span>${e(einheit)}</span>` : ''}</span>`;
  const datum = n => `<input type="date" class="form-control kbe-datum" id="${id(n)}" data-feld="${n}" value="${e(w[n] ?? '')}" ${aus}>`;
  const auswahl = (n, liste) => `<select class="form-select kbe-auswahl" id="${id(n)}" data-feld="${n}" ${aus}>${liste.map(([v, t]) => `<option value="${e(v)}" ${String(w[n]) === String(v) ? 'selected' : ''}>${e(t)}</option>`).join('')}</select>`;
  const text = (n, rows = 3) => `<textarea class="form-control" id="${id(n)}" data-feld="${n}" rows="${rows}" maxlength="2000" ${aus}>${e(w[n] ?? '')}</textarea>`;
  const gruende = [['', '– kein Standardgrund –'], ...d.gruende.map(g => [g.kurz, `${g.name} (${g.kurz})${g.entschuldigt ? ' – gilt als entschuldigt' : ''}`])];
  const bereich = (von, bis) => `<span class="kbe-bereich">${datum(von)}<span>bis</span>${datum(bis)}</span>`;
  const karte = (titel, symbol, inhalt, rechts = '') => `<section class="kbe-karte"><header><span class="kbe-symbol"><i class="bi bi-${symbol}" aria-hidden="true"></i></span><h2>${e(titel)}</h2>${rechts}</header><div class="kbe-rumpf">${inhalt}</div></section>`;

  // ---------------------------------------------------------------- Reiter

  const hauptseite = () => `
    <p class="nl-hinweis"><i class="bi bi-info-circle"></i>Diese Einstellungen gelten für die Hauptseite des Klassenbuchs in den Stundendetails, in der Fachlehrkräfte Abwesenheiten und Lehrstoff eintragen.</p>
    ${karte('Schülerliste', 'people', `
      ${zeile('Sortierung der Schülerliste', radios('sortierung', [['nachname', 'Nach Nachname'], ['vorname', 'Nach Vorname']]))}
      ${zeile('Anordnung', radios('richtung', [['spalten', 'Von oben nach unten'], ['zeilen', 'Von links nach rechts']]), 'Gilt als Vorgabe; Lehrkräfte können die Ansicht über das Zahnrad für sich ändern.')}
      ${schalter('nachKlasse', 'Sortierung der Schüler*innen nach Klasse', 'Bei gekoppeltem Unterricht mehrerer Klassen zuerst nach Klasse sortieren.')}`)}
    ${karte('Abwesenheiten', 'person-x', `
      ${schalter('abwesenheitNurInStunde', 'Abwesenheitseintrag darf nur in der Unterrichtsstunde erfolgen', 'Fachlehrkräfte tragen Abwesenheiten nur während der Stunde ein; Klassenleitung und Administration sind ausgenommen.')}
      ${zeile('Vorgabe für das Ende einer Abwesenheit', radios('abwesenheitEnde', [['stunde', 'Stundenende'], ['tag', 'Tagesende'], ['zeit', 'Fixer Zeitpunkt']],
        { zeit: `<input type="time" class="form-control kbe-zeit" data-feld="abwesenheitEndeZeit" value="${e(w.abwesenheitEndeZeit)}" aria-label="Fixer Zeitpunkt" ${aus} ${w.abwesenheitEnde === 'zeit' ? '' : 'hidden'}>` }),
        'Stundenende: In den folgenden Stunden erscheint die Abwesenheit mit dem Knopf „Abwesenheit verlängern“, solange die Schülerin bzw. der Schüler fehlt. Tagesende bzw. fixer Zeitpunkt: Die Abwesenheit gilt gleich für die folgenden Stunden und wird dort verkürzt, sobald die Schülerin bzw. der Schüler wieder da ist.')}
      ${schalter('direkteEingabe', 'Direkte Abwesenheitseingabe über das Häkchen', 'Ein Klick auf das Häkchen in der Schülerkachel trägt die Abwesenheit sofort ein (mit dem Standardgrund für „abwesend“).')}
      ${schalter('alleAbwesenheitenDesTages', 'Alle Abwesenheiten des Tages anzeigen', 'Zeigt in den Stundendetails auch die Abwesenheiten aus späteren Stunden des Tages.')}`)}
    ${karte('Anzeige', 'eye', `
      ${zeile('Klassenbucheinträge anzeigen ab', radios('eintraegeAb', [['woche', 'Beginn der aktuellen Woche'], ['schuljahr', 'Beginn des Schuljahres'], ['datum', 'Fixes Datum'], ['tage', 'Tage zurück']],
        { datum: `<span class="kbe-nachsatz" ${w.eintraegeAb === 'datum' ? '' : 'hidden'} data-bei="eintraegeAb:datum">${datum('eintraegeAbDatum')}</span>`,
          tage: `<span class="kbe-nachsatz" ${w.eintraegeAb === 'tage' ? '' : 'hidden'} data-bei="eintraegeAb:tage">${zahl('eintraegeTage', { max: 366, einheit: 'Tage' })}</span>` }))}
      ${schalter('faelligeHausaufgaben', 'Fällige, unerledigte Hausaufgaben anzeigen')}`)}`;

  const allgemein = () => `
    ${karte('Eingaben', 'pencil-square', `
      ${schalter('abwesenheitskontrolle', 'Abwesenheitskontrolle', 'Stunden ohne Kontrolle der Abwesenheiten gelten als offen. Mit einer eingetragenen Abwesenheit gilt die Stunde als kontrolliert; sind alle anwesend, bestätigt die Lehrkraft die Kontrolle in den Stundendetails.')}
      ${zeile('Erlaubter Datumsbereich für Eingaben', bereich('datumVon', 'datumBis'), 'Leer = keine Einschränkung.')}
      ${zeile('Eingabe bis Tage in die Vergangenheit', zahl('tageVergangenheit', { leer: 'unbegrenzt', einheit: 'Tage' }), '', id('tageVergangenheit'))}
      ${zeile('Eingabe bis Tage in die Zukunft', zahl('tageZukunft', { leer: 'unbegrenzt', einheit: 'Tage' }),
        'Leer = unbegrenzt (Standard). Die Abwesenheitskontrolle über „Abwesenheiten kontrollieren“ bleibt immer möglich.', id('tageZukunft'))}
      ${zeile('Abwesenheiten und Klassenbucheinträge dürfen nach … nicht mehr gelöscht werden', zahl('loeschsperre', { max: d.loeschsperreHoechstens, einheit: 'Stunden' }),
        `0 = jederzeit. Höchstens ${d.loeschsperreHoechstens} Stunden; die Administration kann immer löschen.`, id('loeschsperre'))}`)}
    ${karte('Offene Stunden', 'hourglass-split', `
      ${zeile('Offene Stunden anzeigen ab', radios('offenAb', [['woche', 'Beginn der aktuellen Woche'], ['schuljahr', 'Beginn des Schuljahres'], ['datum', 'Fixes Datum'], ['tage', 'Tage zurück']],
        { datum: `<span class="kbe-nachsatz" ${w.offenAb === 'datum' ? '' : 'hidden'} data-bei="offenAb:datum">${datum('offenAbDatum')}</span>`,
          tage: `<span class="kbe-nachsatz" ${w.offenAb === 'tage' ? '' : 'hidden'} data-bei="offenAb:tage">${zahl('offenTage', { max: 366, einheit: 'Tage' })}</span>` }),
        'Vorgabe der Seite „Offene Stunden“; Lehrkräfte können den Zeitraum dort für sich ändern.')}
      ${zeile('Offene Stunden anzeigen für', auswahl('offenFuer', [['beide', 'Abwesenheitskontrolle und Lehrstoff'], ['kontrolle', 'Abwesenheitskontrolle'], ['lehrstoff', 'Lehrstoff']]), '', id('offenFuer'))}`)}
    ${karte('Fehlzeiten', 'clock-history', `
      ${zeile('Fehlstunde zählt ab', `<input class="form-control kbe-kurz" id="${id('fehlstundeAb')}" data-feld="fehlstundeAb" value="${e(w.fehlstundeAb)}" ${aus}>`, 'Minuten (z. B. 20) oder Prozent der Stunde (z. B. 50%).', id('fehlstundeAb'))}
      ${zeile('Fehltag zählt ab', `<input class="form-control kbe-kurz" id="${id('fehltagAb')}" data-feld="fehltagAb" value="${e(w.fehltagAb)}" ${aus}>`, 'Minuten oder Prozent des Unterrichtstages; wichtig für die Berichte.', id('fehltagAb'))}
      ${zeile('Max. Fehlminuten pro Unterrichtseinheit', zahl('maxFehlminuten', { max: 600, einheit: 'Minuten' }), '0 = ohne Grenze.', id('maxFehlminuten'))}
      ${schalter('pruefungenIgnorieren', 'Prüfungen für Fehlzeiten ignorieren')}`)}
    ${karte('Lehrstoff', 'journal-text', `
      ${zeile('Lehrstoffeintragung bis Tage in die Vergangenheit', zahl('lehrstoffTage', { leer: 'unbegrenzt', einheit: 'Tage' }), 'Leer = unbegrenzt (Standard).', id('lehrstoffTage'))}
      ${schalter('lehrstoffZukunft', 'Lehrstoffeintragung für Stunden in der Zukunft erlauben')}
      ${schalter('lehrstoffBlock', 'Lehrstoff für alle Stunden eines Blocks eintragen', 'Bei Doppel- und Mehrfachstunden gilt der Lehrstoff der ersten Stunde für den ganzen Block.')}`)}
    ${karte('Abwesenheitsgründe und Entschuldigungen', 'envelope-paper', `
      ${schalter('statusMitGrund', 'Entschuldigungsstatus immer mit Abwesenheitsgrund ändern', 'Der Status folgt dem Abwesenheitsgrund (Stammdaten → Abwesenheitsgründe, „gilt als entschuldigt“).')}
      ${schalter('grundTextGesperrt', 'Eingabe von Abwesenheitsgrund und Text nicht erlauben', 'Fachlehrkräfte können dann weder Grund noch Text eintragen; es gilt der Standardgrund.')}
      ${zeile('Standardgrund für abwesende Schüler*innen', auswahl('grundAbwesend', gruende), '', id('grundAbwesend'))}
      ${zeile('Standardgrund für später kommende Schüler*innen', auswahl('grundSpaeter', gruende), '', id('grundSpaeter'))}
      ${zeile('Standardgrund für früher gehende Schüler*innen', auswahl('grundFrueher', gruende), '', id('grundFrueher'))}
      ${zeile('Standardgrund bei Abwesenheitsmeldung durch die Schüler*innen', auswahl('grundMeldung', gruende), 'Standardgründe werden automatisch eingetragen und lassen sich ändern.', id('grundMeldung'))}
      ${zeile('Standard-Entschuldigungsstatus', auswahl('standardStatus', STATUS), '', id('standardStatus'))}
      ${zeile('Automatische Entschuldigung nach', `${zahl('autoTage', { leer: 'aus', einheit: 'Tagen' })}${auswahl('autoStatus', STATUS.slice(1))}`, 'Offene Abwesenheiten erhalten nach so vielen Tagen automatisch diesen Status (einmal täglich). Leer = aus.', id('autoTage'))}
      ${zeile('Nummerierung von Entschuldigungen', auswahl('nummerierung', [['klassenuebergreifend', 'Klassenübergreifend'], ['klasse', 'Je Klasse'], ['schueler', 'Je Schüler*in'], ['keine', 'Keine Nummerierung']]), '', id('nummerierung'))}
      ${zeile('Entschuldigungstext vor Unterschrift', text('textVorUnterschrift'), '', id('textVorUnterschrift'))}
      ${zeile('Entschuldigungstext nach Unterschrift', text('textNachUnterschrift'), 'Beide Textblöcke erscheinen auf den Entschuldigungsberichten.', id('textNachUnterschrift'))}
      ${zeile('Entschuldigungsschreiben für Schüler*innen', auswahl('entschuldigungsschreiben', [['sichtbar', 'sichtbar'], ['ausgeblendet', 'ausgeblendet']]), '', id('entschuldigungsschreiben'))}`)}
    ${karte('Fachlehrer-Entschuldigungen', 'person-check', `
      ${schalter('fachlehrerEntschuldigungen', 'Fachlehrer-Entschuldigungen aktivieren', 'Lehrkräfte entschuldigen einzelne Fehlstunden aus ihrem Unterricht unter Klassenbuch → Fehlzeiten bzw. in der App (Recht „Fehlstunden im eigenen Unterricht entschuldigen“). Gilt ohne Einschränkung für alle Jahrgänge.')}
      ${schalter('fachlehrerJahrgangEinschraenken', 'Einschränkung auf Jahrgangsstufen', 'Nur Fehlstunden von Klassen in diesen Jahrgangsstufen. Die Stammdaten der Klassen müssen eine Jahrgangsstufe enthalten.')}
      ${zeile('Jahrgangsstufen von – bis', `<span class="kbe-bereich">${zahl('fachlehrerJahrgangVon', { max: 20, leer: '–' })}<span>bis</span>${zahl('fachlehrerJahrgangBis', { max: 20, leer: '–' })}</span>`)}
      <div class="kbe-info"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i><div>
        <b>Deaktivieren während des Schuljahres:</b> Bitte vorher alle Abwesenheiten von der Klassenleitung kontrollieren und bestätigen lassen. Ein einmal gesetzter Status bleibt erhalten; Abwesenheiten mit unterschiedlichen Status der Stunden bleiben „offen“ (mehrdeutig). Wir empfehlen, die Funktion nicht im laufenden Schuljahr zu deaktivieren.</div></div>`)}
    ${karte('Automatische Erinnerung bei offenen Stunden', 'bell', `
      ${schalter('erinnerung', 'Automatische Erinnerung über offene Stunden des aktuellen Schuljahres')}
      ${zeile('Versand jeden', auswahl('erinnerungTag', WOCHENTAGE.map((t, i) => [i + 1, t])), '', id('erinnerungTag'))}
      <div class="kbe-info"><i class="bi bi-info-circle" aria-hidden="true"></i><div>
        <b>Wann gilt eine Stunde als offen?</b> Nach „Offene Stunden anzeigen für“: fehlender Lehrstoff, fehlende Abwesenheitskontrolle oder beides.<br>
        <b>Wer erhält die Erinnerung?</b> Die für die Stunde verantwortliche Lehrkraft als Mitteilung – mit einem Link, der direkt zur Stunde führt (in der mobilen App lässt sich der Link nicht direkt öffnen).</div></div>`,
      darf ? '<button type="button" class="nl-knopf ms-auto" data-erinnern><i class="bi bi-send"></i> Jetzt senden</button>' : '')}`;

  const unterrichtReiter = () => `
    ${karte('Prüfungen', 'clipboard-check', `
      ${zeile('Erlaubter Datumsbereich für Prüfungseingaben', bereich('pruefungVon', 'pruefungBis'), 'Leer = keine Einschränkung.')}
      ${schalter('raumkonflikt', 'Raumkonflikt bei Prüfung zulassen', 'Prüfungen dürfen dann auch in Räumen eingetragen werden, die zu dieser Zeit belegt sind.')}
      ${zeile('Prüfungsübernahme nach UntPlan bis Tage in die Zukunft', zahl('uebernahmeTage', { leer: 'unbegrenzt', einheit: 'Tage' }),
        'Prüfungen werden wie Buchungen an UntPlan übergeben; spätere Prüfungen folgen, sobald sie im Zeitraum liegen.', id('uebernahmeTage'))}
      ${schalter('nurVertretungsrelevant', 'Nur vertretungsrelevante Prüfungen nach UntPlan übernehmen', 'Vertretungsrelevant sind Prüfungen mit Raum oder zugewiesener Lehrkraft (Aufsicht).')}`)}`;

  const notenReiter = () => {
    const gesamt = noten.schemen.find(s => s.id === noten.gesamt);
    return `
    ${karte('Notenschemen', 'award', `
      <p class="kbe-text">Falls das verwendete Notenschema noch nicht vorhanden ist, legt die Administration unter Stammdaten → Notenschemen eigene Schemen an, z. B. „1 – 5“ oder „+, ~, -“.
        Ohne Werte bei den Noten werden für dieses Schema keine Durchschnitte berechnet.</p>
      ${noten.schemen.length ? `<ul class="kbe-schemen">${noten.schemen.map(s => `<li><b>${e(s.name)}</b><span>${s.noten.map(n => e(n.text)).join(' · ')}</span>
        ${s.mitDurchschnitt ? '<span class="nl-chip gruen">mit Durchschnitt</span>' : '<span class="nl-chip grau">ohne Werte</span>'}${s.aktiv ? '' : '<span class="nl-chip grau">inaktiv</span>'}</li>`).join('')}</ul>`
        : '<p class="kbe-text text-body-secondary">Es sind noch keine Notenschemen angelegt.</p>'}
      ${zeile('Gesamtnotenschema', `<span class="kbe-text">${gesamt ? e(gesamt.name) : 'nicht gewählt'}</span>`)}`,
      kontext.ich.admin ? '<a class="nl-knopf ms-auto" href="#/stammdaten/notenschemen"><i class="bi bi-pencil"></i> Verwalten</a>' : '')}
    ${karte('Prüfungsarten', 'clipboard-data', `<p class="kbe-text">Prüfungsarten mit Höchstzahlen je Schüler*in, Farben und Notenschema pflegt die Administration unter Stammdaten → Prüfungsarten.
        Hat eine Prüfungsart kein Notenschema, wählt die Lehrkraft beim Anlegen der Prüfung ein individuelles Schema.</p>`,
      kontext.ich.admin ? '<a class="nl-knopf ms-auto" href="#/stammdaten/pruefungsarten"><i class="bi bi-pencil"></i> Verwalten</a>' : '')}`;
  };

  const INHALT = { hauptseite, allgemein, unterricht: unterrichtReiter, noten: notenReiter };

  // ---------------------------------------------------------------- Zeichnen und Binden

  const zeichnen = () => {
    kontext.titel(nurUnterricht ? 'Unterricht-Einstellungen' : 'Klassenbuch-Einstellungen');
    seite.innerHTML = `<div class="kbe">
      ${nurUnterricht ? '' : `<nav class="kbe-reiter" role="tablist" aria-label="Kategorien">${REITER.map(([r, t, s]) => `<button type="button" role="tab" aria-selected="${r === reiter}" class="${r === reiter ? 'an' : ''}" data-reiter="${r}"><i class="bi bi-${s}" aria-hidden="true"></i>${t}</button>`).join('')}</nav>`}
      ${darf ? '' : '<p class="nl-hinweis"><i class="bi bi-lock"></i>Die Einstellungen ändert die Administration (Recht „Klassenbuch-Einstellungen“). Sie sehen die aktuellen Werte.</p>'}
      <div class="kbe-inhalt" role="tabpanel">${INHALT[nurUnterricht ? 'unterricht' : reiter]()}</div>
      ${darf && reiter !== 'noten' ? `<div class="kbe-leiste"><span class="kbe-stand" data-stand>${geaendert() ? 'Nicht gespeicherte Änderungen' : 'Alle Änderungen gespeichert'}</span>
        <button type="button" class="nl-knopf" data-verwerfen ${geaendert() ? '' : 'disabled'}>Verwerfen</button>
        <button type="button" class="nl-knopf nw-primaer" data-speichern ${geaendert() ? '' : 'disabled'}>Speichern</button></div>` : ''}
    </div>`;
    binden();
  };

  const standAnzeigen = () => {
    const g = geaendert();
    const st = seite.querySelector('[data-stand]');
    if (st) st.textContent = g ? 'Nicht gespeicherte Änderungen' : 'Alle Änderungen gespeichert';
    seite.querySelectorAll('[data-speichern], [data-verwerfen]').forEach(b => { b.disabled = !g; });
  };

  const binden = () => {
    seite.querySelectorAll('[data-reiter]').forEach(b => b.onclick = () => {
      reiter = b.dataset.reiter;
      history.replaceState(null, '', `#/kbeinstellungen/${reiter}`);
      zeichnen();
    });
    seite.querySelectorAll('[data-feld]').forEach(f => {
      const n = f.dataset.feld;
      const lesen = () => {
        if (f.type === 'checkbox') w[n] = f.checked;
        else if (f.type === 'radio') { if (f.checked) w[n] = f.value; }
        else if (f.dataset.zahl !== undefined) w[n] = f.value === '' ? (f.dataset.leer !== undefined ? null : 0) : Number(f.value);
        else if (n === 'erinnerungTag') w[n] = Number(f.value);
        else w[n] = f.value;
        // abhängige Felder (fixer Zeitpunkt, Datum, Tage) ein- bzw. ausblenden
        if (f.type === 'radio') {
          seite.querySelectorAll(`[data-bei^="${n}:"]`).forEach(x => { x.hidden = x.dataset.bei !== `${n}:${w[n]}`; });
          if (n === 'abwesenheitEnde') seite.querySelector('[data-feld="abwesenheitEndeZeit"]').hidden = w[n] !== 'zeit';
        }
        standAnzeigen();
      };
      f.addEventListener('change', lesen);
      if (f.tagName === 'TEXTAREA' || f.type === 'number' || f.type === 'text') f.addEventListener('input', lesen);
    });
    seite.querySelector('[data-verwerfen]')?.addEventListener('click', () => { w = structuredClone(gespeichert); zeichnen(); });
    seite.querySelector('[data-speichern]')?.addEventListener('click', async ev => {
      ev.currentTarget.disabled = true;
      try {
        gespeichert = await senden('/api/app/klassenbuch/einstellungen', w, 'PUT');
        w = structuredClone(gespeichert);
        toast('Einstellungen gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); standAnzeigen(); }
    });
    seite.querySelector('[data-erinnern]')?.addEventListener('click', async ev => {
      const b = ev.currentTarget;
      b.disabled = true;
      try {
        const r = await senden('/api/app/klassenbuch/einstellungen/erinnerung', {});
        toast(r.lehrkraefte ? `Erinnerung an ${r.lehrkraefte} ${r.lehrkraefte === 1 ? 'Lehrkraft' : 'Lehrkräfte'} gesendet (${r.stunden} offene Stunden).` : 'Keine offenen Stunden – es wurde keine Erinnerung gesendet.');
      } catch (err) { toast(err.message, true); }
      b.disabled = false;
    });
  };

  seite.innerHTML = lade();
  zeichnen();
  // ungespeicherte Änderungen beim Verlassen nicht stillschweigend verwerfen
  const warnen = ev => { if (!document.body.contains(seite) || !seite.querySelector('.kbe')) { window.removeEventListener('beforeunload', warnen); return; } if (geaendert()) { ev.preventDefault(); ev.returnValue = ''; } };
  window.addEventListener('beforeunload', warnen);
}
