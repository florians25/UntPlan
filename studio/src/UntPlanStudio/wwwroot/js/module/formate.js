// Modul „Stundenplanformate“: eigene Formate wie im Formatdialog der Desktop-Anwendung (Allgemein, Auswahl (Bereich), Layout 1,
// Layout 2, HTML, Stundenplan-Stunde mit grafischem Feld-Editor). Die Formate liegen in der Schuldatei und werden in der
// Druckauswahl gewählt. Server: Api/FormateApi.cs (/formate).
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { formular, frage, hinweis, fehlerMelden, laden } from '../kern/dialog.js';

const TAGE = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const TYPEN = [['Klasse', 'Klassen'], ['Lehrer', 'Lehrer'], ['Raum', 'Räume'], ['Fach', 'Fächer']];
const opt = (liste) => liste.map((x) => (Array.isArray(x) ? { wert: x[0], text: x[1] } : { wert: x, text: String(x) }));

// Felder je Reiter: [schluessel, titel, art, optionen]
const REITER = {
  Allgemein: [
    ['name', 'Formatname', 'text'], ['typ', 'Planart', 'auswahl', opt(TYPEN)], ['gross', 'Format groß (eine Seite je Plan)', 'bool'],
    ['grundform', 'Grundform', 'grundform'], ['schriftgroesse', 'Schriftgröße', 'zahl'],
    ['farbe', 'Farbe nach', 'auswahl', opt([['Fach', 'Fach'], ['Lehrer', 'Lehrer'], ['Klasse', 'Klasse'], ['Raum', 'Raum'], ['Keine', 'keine']])],
    ['langname', 'Langnamen anzeigen', 'bool'], ['zeitenAnzeigen', 'Uhrzeiten anzeigen', 'bool'], ['einzelstunden', 'Einzelstunden (nicht zusammenfassen)', 'bool'],
    ['pausenAnzeigen', 'Pausen anzeigen (Bildschirm)', 'bool'], ['pausenbezeichnung', 'Pausenzeilen im Druck', 'bool'], ['klein', 'Kleiner Stundenplan (nur Fachkürzel)', 'bool'],
    ['zellenBreite', 'Zellenbreite (0 = automatisch)', 'zahl'], ['zellenHoehe', 'Zellenhöhe (0 = automatisch)', 'zahl'],
    ['stundenplanlupeAnzeigen', 'Stundenplanlupe anzeigen', 'bool'], ['standardformat', 'Stpl-Stunde: Standardformat', 'bool'],
  ],
  'Auswahl (Bereich)': [
    ['vonStunde', 'Von Stunde', 'zahl'], ['bisStunde', 'Bis Stunde (0 = letzte)', 'zahl'],
    ['vonTag', 'Von Tag', 'auswahl', opt(TAGE.map((t, i) => [i, t]))], ['bisTag', 'Bis Tag', 'auswahl', opt([[-1, 'bis zum letzten Schultag'], ...TAGE.map((t, i) => [i, t])])],
    ['zeitraum', 'Zeitraum', 'auswahl', opt([['Kalenderwoche', 'Kalenderwoche'], ['GanzesSchuljahr', 'Ganzes Schuljahr'], ['EinTag', '1 Tag']])],
    ['minutModus', 'Minut-Modus', 'bool'], ['ganzenTagZeigen', 'Ganzen Tag zeigen', 'bool'],
  ],
  'Layout 1': [
    ['ueberschrift', 'Überschrift', 'gestaltung'], ['besetzteStunden', 'Besetzte Stunden', 'gestaltung'], ['freieStunden', 'Freie Stunden', 'gestaltung'],
    ['linieVor', 'Linie vor folgenden Stunden', 'text'], ['doppelstrichVor', 'Doppelstrich vor Stunden', 'text'], ['dickerStrichVor', 'Dicker Strich vor Stunden', 'text'],
    ['trennstrichVormNachm', 'Trennstrich Vorm. / Nachm.', 'bool'], ['trennstrichTage', 'Trennstrich zwischen Tagen', 'bool'],
  ],
  'Layout 2': [
    ['kombinierteKlassen', 'Komb. Klassen (2a,2b → 2ab)', 'bool'], ['alleKlassenEinFeld', 'Alle Klassen in einem Feld', 'bool'],
    ['kopplungenMitPunkt', 'Kopplungen mit Punkt', 'bool'], ['fixierungMitStern', 'Fixierung mit Stern', 'bool'], ['kollisionTrennen', 'Kollisionen trennen', 'bool'],
    ['stundenfelderSortieren', 'Stundenfelder sortieren', 'bool'], ['elementeVerbinden', 'Elemente verbinden', 'bool'],
    ['absenzenAnzeigen', 'Absenzen anzeigen', 'bool'], ['keinAbsenzgrund', 'Kein Absenzgrund', 'bool'], ['nurTagesabsenzen', 'Nur Tagesabsenzen', 'bool'],
    ['nurVertretungsstunden', 'Nur Vertretungsstunden', 'bool'], ['entfaelleDurchstreichen', 'Entfälle durchstreichen', 'bool'], ['mitVertretungen', 'Mit Vertretungen', 'bool'],
    ['dragDropZeitwunschFarben', 'DragDrop: Farben laut Zeitwünsche', 'bool'], ['dragDropMehrereUnterrichte', 'DragDrop: mehrere Unterrichte', 'bool'], ['dragDropTauschketten', 'DragDrop: Tauschketten', 'bool'],
    ['leereZeilenNichtDrucken', 'Leere Zeilen nicht drucken', 'bool'], ['leereSpalteNichtDrucken', 'Leere Spalte nicht drucken', 'bool'],
    ['schwarzWeissDruck', 'Schwarz-Weiß-Druck', 'bool'], ['eineUeberschriftJeBlatt', 'Eine Überschrift je Blatt', 'bool'], ['autosizeLupe', 'Autosize Lupe', 'bool'],
    ['hauptklassenGetrennt', 'Hauptklassen getrennt', 'bool'],
  ],
  HTML: [
    ['htmlTitel', 'Überschrift der HTML-Seiten', 'text'], ['htmlIndexseite', 'Indexseite erzeugen', 'bool'], ['htmlMitFarben', 'Mit Fachfarben', 'bool'],
    ['htmlIndexLangnamen', 'Index mit Langnamen', 'bool'],
  ],
  'Stundenplan-Stunde': [
    ['felderHorizontalZentrieren', 'Felder horizontal zentrieren', 'bool'], ['felderVertikalZentrieren', 'Felder vertikal zentrieren', 'bool'],
    ['beginnEndzeit', 'Beginn-/Endzeit', 'bool'], ['namensArt', 'Namen', 'auswahl', opt([['Kurzname', 'Kurzname'], ['Langname', 'Langname'], ['Druckname', 'Druckname']])],
    ['aliasVerwenden', 'Alias verwenden', 'bool'], ['lehrerUndVertreter', 'Lehrer und Vertreter', 'bool'],
  ],
};

function editorFenster(f, p) {
  let daten = null;
  let aktuell = null;     // bearbeitete Kopie
  let alterName = null;
  let reiter = 'Allgemein';
  let geaendert = false;
  const schreiben = darfSchreiben();
  const liste = h('div', { class: 'fm-liste' });
  const reiterleiste = h('div', { class: 'es-reiter' });
  const seite = h('div', { class: 'fm-seite' });
  f.inhalt.append(h('div', { class: 'fm-rahmen' }, liste, h('div', { class: 'fm-rechts' }, reiterleiste, seite)));
  const status = h('span', { class: 'leise' });
  f.fuss.replaceChildren(status);
  const kSpeichern = f.werkzeug({ text: 'Speichern', symbol: 'speichern', aktion: () => speichern() });
  const kNeu = f.werkzeug({ text: 'Neu …', symbol: 'plus', aktion: () => kopieren(null) });
  const kKopie = f.werkzeug({ text: 'Kopieren …', symbol: 'kopieren', aktion: () => aktuell && kopieren(alterName || aktuell.name) });
  const kWeg = f.werkzeug({ text: 'Löschen', symbol: 'loeschen', aktion: () => loeschen() });
  f.trenner();
  f.werkzeug({ text: 'Probedruck', symbol: 'drucken', aktion: () => probedruck() });
  for (const k of [kSpeichern, kNeu, kKopie, kWeg]) k.disabled = !schreiben;

  const merken = () => { geaendert = true; status.textContent = 'Geändert – „Speichern“ übernimmt das Format in die Schuldatei.'; };

  function waehlen(fmt, alt) {
    aktuell = structuredClone(fmt);
    alterName = alt;
    geaendert = false;
    status.textContent = alt ? '' : 'Vorlage – „Speichern“ legt sie als eigenes Format an.';
    zeichnen();
  }

  function zeichneListe() {
    const eigene = daten.formate.map((x) => h('button', { type: 'button', class: `fm-eintrag${alterName === x.name ? ' aktiv' : ''}`, onclick: () => waehlen(x, x.name) },
      h('span', null, x.name), h('span', { class: 'leise' }, `${TYPEN.find((t) => t[0] === x.typ)?.[1] || x.typ}${x.gross ? ', groß' : ''}`)));
    const vorlagen = daten.vorlagen.map((x) => h('button', { type: 'button', class: `fm-eintrag vorlage${!alterName && aktuell?.name === x.name ? ' aktiv' : ''}`, onclick: () => waehlen(x, null) },
      h('span', null, x.name), h('span', { class: 'leise' }, 'Vorlage')));
    liste.replaceChildren(h('h3', null, 'Eigene Formate'), ...(eigene.length ? eigene : [h('p', { class: 'leise' }, 'Noch keine eigenen Formate.')]), h('h3', null, 'Vorlagen'), ...vorlagen);
  }

  function eingabe([k, titel, art, optionen]) {
    let el;
    if (art === 'bool') {
      el = h('input', { type: 'checkbox', checked: !!aktuell[k] });
      el.addEventListener('change', () => { aktuell[k] = el.checked; merken(); });
    } else if (art === 'auswahl' || art === 'grundform') {
      const o = art === 'grundform' ? daten.grundformen.map((g) => ({ wert: g.wert, text: g.text })) : optionen;
      el = h('select', { class: 'eingabe' }, o.map((x) => h('option', { value: x.wert, selected: String(x.wert) === String(aktuell[k]) }, x.text)));
      el.addEventListener('change', () => { aktuell[k] = typeof aktuell[k] === 'number' ? Number(el.value) : el.value; merken(); });
    } else if (art === 'gestaltung') {
      const g = aktuell[k] || (aktuell[k] = { raeumlich: 'Flach', grau: false });
      const r = h('select', { class: 'eingabe' }, ['Erhaben', 'Versenkt', 'Flach'].map((x) => h('option', { value: x, selected: g.raeumlich === x }, x === 'Flach' ? 'Eben' : `${x} (3D)`)));
      const grau = h('select', { class: 'eingabe' }, h('option', { value: '1', selected: g.grau }, 'grau'), h('option', { value: '0', selected: !g.grau }, 'weiß'));
      r.addEventListener('change', () => { g.raeumlich = r.value; merken(); });
      grau.addEventListener('change', () => { g.grau = grau.value === '1'; merken(); });
      for (const x of [r, grau]) x.disabled = !schreiben;
      return [h('label', null, titel), h('div', { class: 'fm-paar' }, r, grau)];
    } else {
      el = h('input', { class: 'eingabe', type: art === 'zahl' ? 'number' : 'text', value: aktuell[k] ?? '' });
      el.addEventListener('change', () => { aktuell[k] = art === 'zahl' ? Number(el.value) : el.value; merken(); });
    }
    el.disabled = !schreiben;
    return [h('label', null, titel), el];
  }

  function feldEditor() {
    const box = h('div', { class: 'fm-zelle', title: 'Felder verschieben (ziehen) und in der Größe ändern (Ecke unten rechts)' });
    const tab = h('table', { class: 'es-liste' });
    tab.append(h('tr', null, ['Feld', 'Zeile', 'Schrift %', 'Fett', 'Kursiv', 'Unterstr.', 'Ausrichtung', ''].map((t) => h('th', null, t))));
    const neuZeichnen = () => { seite.replaceChildren(...seitenInhalt()); };
    aktuell.felder.forEach((fe, i) => {
      const el = h('div', { class: 'fm-feld', style: feldStil(fe) }, `${fe.zeile}${fe.art}`, h('span', { class: 'fm-griff' }));
      if (schreiben) el.addEventListener('pointerdown', (ev) => ziehen(ev, fe, el, box, ev.target.classList.contains('fm-griff')));
      box.append(el);
      const art = h('select', { class: 'eingabe' }, daten.feldarten.map((a) => h('option', { value: a, selected: a === fe.art }, a)));
      art.addEventListener('change', () => { fe.art = art.value; merken(); neuZeichnen(); });
      const zahl = (k, min, max) => { const x = h('input', { class: 'eingabe', type: 'number', min, max, value: fe[k], style: { width: '64px' } }); x.addEventListener('change', () => { fe[k] = Number(x.value); merken(); neuZeichnen(); }); return x; };
      const bool = (k) => { const x = h('input', { type: 'checkbox', checked: !!fe[k] }); x.addEventListener('change', () => { fe[k] = x.checked; merken(); neuZeichnen(); }); return x; };
      const aus = h('select', { class: 'eingabe' }, ['links', 'mittig', 'rechts'].map((t, n) => h('option', { value: n, selected: fe.ausrichtung === n }, t)));
      aus.addEventListener('change', () => { fe.ausrichtung = Number(aus.value); merken(); neuZeichnen(); });
      const weg = h('button', { class: 'knopf klein gefahr', type: 'button', onclick: () => { aktuell.felder.splice(i, 1); merken(); neuZeichnen(); } }, 'Entfernen');
      const zeileEl = h('tr', null, h('td', null, art), h('td', null, zahl('zeile', 1, 20)), h('td', null, zahl('schriftProzent', 30, 300)),
        h('td', { class: 'mitte' }, bool('fett')), h('td', { class: 'mitte' }, bool('kursiv')), h('td', { class: 'mitte' }, bool('unterstrichen')), h('td', null, aus), h('td', null, weg));
      for (const x of zeileEl.querySelectorAll('input, select, button')) x.disabled = !schreiben;
      tab.append(zeileEl);
    });
    const neu = h('button', { class: 'knopf klein', type: 'button', disabled: !schreiben, onclick: () => {
      const belegt = aktuell.felder.map((x) => x.art);
      const art = ['Fach', 'Lehrer', 'Klasse', 'Raum', 'UNr', 'Text'].find((a) => !belegt.includes(a)) || 'Text';
      const n = aktuell.felder.length;
      aktuell.felder.push({ art, zeile: 1, x: (n % 2) * 0.5, y: Math.min(0.75, Math.floor(n / 2) * 0.25), b: 0.5, h: 0.25, schriftProzent: 100, fett: false, kursiv: false, unterstrichen: false, ausrichtung: 1 });
      merken();
      neuZeichnen();
    } }, 'Feld hinzufügen');
    return [h('h3', null, 'Inhalt (Felder) einer Unterrichtsstunde'),
      h('p', { class: 'leise' }, aktuell.standardformat ? 'Hinweis: „Stpl-Stunde: Standardformat“ ist eingeschaltet – dann werden alle beteiligten Elemente automatisch angezeigt und die Felder nicht verwendet.' : 'Die Felder bestimmen Lage und Schrift der Angaben in einer Stunde.'),
      box, tab, neu];
  }

  function feldStil(fe) {
    return {
      left: `${fe.x * 100}%`, top: `${fe.y * 100}%`, width: `${fe.b * 100}%`, height: `${fe.h * 100}%`,
      fontSize: `${Math.round(12 * fe.schriftProzent / 100)}px`, fontWeight: fe.fett ? '500' : '400', fontStyle: fe.kursiv ? 'italic' : 'normal',
      textDecoration: fe.unterstrichen ? 'underline' : 'none', justifyContent: ['flex-start', 'center', 'flex-end'][fe.ausrichtung ?? 1],
    };
  }

  function ziehen(ev, fe, el, box, groesse) {
    ev.preventDefault();
    const r = box.getBoundingClientRect();
    const start = { x: ev.clientX, y: ev.clientY, fx: fe.x, fy: fe.y, fb: fe.b, fh: fe.h };
    const runden = (v) => Math.round(v * 40) / 40;  // Raster 2,5 %
    const bewegen = (e) => {
      const dx = (e.clientX - start.x) / r.width;
      const dy = (e.clientY - start.y) / r.height;
      if (groesse) {
        fe.b = Math.max(0.05, Math.min(1 - fe.x, runden(start.fb + dx)));
        fe.h = Math.max(0.05, Math.min(1 - fe.y, runden(start.fh + dy)));
      } else {
        fe.x = Math.max(0, Math.min(1 - fe.b, runden(start.fx + dx)));
        fe.y = Math.max(0, Math.min(1 - fe.h, runden(start.fy + dy)));
      }
      Object.assign(el.style, feldStil(fe));
    };
    const ende = () => { document.removeEventListener('pointermove', bewegen); document.removeEventListener('pointerup', ende); merken(); };
    document.addEventListener('pointermove', bewegen);
    document.addEventListener('pointerup', ende);
  }

  function seitenInhalt() {
    const felder = REITER[reiter] || REITER.Allgemein;
    const teile = [h('div', { class: 'formular fm-formular' }, felder.map(eingabe))];
    if (reiter === 'Stundenplan-Stunde') teile.push(...feldEditor());
    if (reiter === 'Allgemein') teile.push(h('p', { class: 'leise' }, 'Im Druck wirken: Format groß/kompakt, Langnamen, Uhrzeiten, Einzelstunden, Pausenzeilen, Auswahl (Bereich) und Schwarz-Weiß-Druck (Layout 2). Die übrigen Angaben gelten für die Desktop-Anwendung und bleiben in der Schuldatei erhalten.'));
    return teile;
  }

  function zeichnen() {
    zeichneListe();
    reiterleiste.replaceChildren(...Object.keys(REITER).map((k) => h('button', { type: 'button', class: `es-reiterknopf${reiter === k ? ' aktiv' : ''}`, onclick: () => { reiter = k; zeichnen(); } }, k)));
    if (!aktuell) { seite.replaceChildren(h('p', { class: 'leise' }, 'Format links wählen oder mit „Neu …“ aus einer Vorlage anlegen.')); return; }
    f.titel(`Stundenplanformat – ${aktuell.name}`);
    seite.replaceChildren(...seitenInhalt());
  }

  async function speichern() {
    if (!aktuell) return;
    daten = await api.put(api.dok(`/formate/${encodeURIComponent(alterName || aktuell.name)}`), aktuell, { quelle: f.id });
    alterName = aktuell.name;
    const neu = daten.formate.find((x) => x.name === alterName);
    if (neu) aktuell = structuredClone(neu);
    geaendert = false;
    status.textContent = 'Gespeichert.';
    hinweis(`Format „${alterName}“ gespeichert.`, 'ok', 1500);
    zeichnen();
  }

  async function kopieren(name) {
    const quellen = [...daten.formate.map((x) => x.name), ...daten.vorlagen.map((x) => x.name)];
    const w = await formular(name ? 'Format kopieren' : 'Neues Format', [
      { name: 'quelle', titel: 'Vorlage', art: 'auswahl', wert: name || quellen[0], optionen: quellen.map((q) => ({ wert: q, text: q })) },
      { name: 'neu', titel: 'Name des neuen Formats', wert: name ? `${name} (Kopie)` : '', pflicht: true },
    ], { okText: 'Anlegen' });
    if (!w) return;
    daten = await api.post(api.dok('/formate/kopieren'), { name: w.quelle, neu: w.neu }, { quelle: f.id });
    const x = daten.formate.find((fo) => fo.name === w.neu.trim());
    if (x) waehlen(x, x.name); else zeichnen();
  }

  async function loeschen() {
    if (!alterName) return;
    if (!(await frage(`Format „${alterName}“ löschen?`))) return;
    daten = await api.loeschen(api.dok(`/formate/${encodeURIComponent(alterName)}`), { quelle: f.id });
    aktuell = null;
    alterName = null;
    zeichnen();
  }

  function probedruck() {
    if (!aktuell) return;
    if (geaendert || !alterName) { hinweis('Bitte das Format zuerst speichern.', 'warnung'); return; }
    const typ = aktuell.typ.toLowerCase();
    fensterverwaltung.oeffnen('druck.vorschau', { url: api.dok(`/druck/stundenplaene?typ=${typ}&format=${encodeURIComponent(alterName)}`), titel: `Probedruck ${alterName}` });
  }

  async function laden_() {
    const weg = laden(f.inhalt);
    try {
      daten = await api.get(api.dok('/formate'));
      const gew = (alterName && daten.formate.find((x) => x.name === alterName)) || (p.typ && daten.formate.find((x) => x.typ.toLowerCase() === p.typ)) || daten.formate[0];
      if (gew && !geaendert) waehlen(gew, gew.name); else zeichnen();
    } finally { weg(); }
  }
  f.an('daten', (e) => { if (e.quelle !== f.id && !geaendert && betrifft(e, 'Einstellungen')) laden_().catch(() => {}); });
  laden_().catch(fehlerMelden);
}

export default {
  id: 'formate',
  css: ['css/module/formate.css'],
  fenster: {
    'formate.editor': { titel: 'Stundenplanformate', symbol: 'einstellungen', breite: 1000, hoehe: 640, einzig: () => 'formate', erstellen: editorFenster },
  },
  befehle: { 'formate.oeffnen': (a) => (zustand.dokument ? fensterverwaltung.oeffnen('formate.editor', a) : null) },
  ribbon: [
    { id: 'stundenplaene.formate', reiter: 'Stundenpläne', gruppe: 'Formate', text: 'Stundenplan-formate', symbol: 'einstellungen', gross: true, befehl: 'formate.oeffnen', braucht: 'dokument', hinweis: 'Eigene Stundenplanformate (Formatdialog wie im Desktop)' },
  ],
};
