// Modul „Optimierung“: Optimierung als Hintergrundauftrag auf dem Server (Steuerdaten wie im Desktop, Fortschritt, Abbrechen,
// Ergebnisvergleich mit Kennzahlen und Planvorschau, Übernehmen), Stundenplan-Diagnose (Eingabedaten / Stundenplan mit den
// konkreten Fällen) und Gewichtung (Regler 0–5 je Karteikarte, Gewichtungs-Verteilung, Analyse).
import { h, leeren, entprellen } from '../kern/dom.js';
import { api, einstellungLesen, einstellungSchreiben } from '../kern/api.js';
import { zustand, darfSchreiben, elemente } from '../kern/zustand.js';
import { an, betrifft } from '../kern/ereignisse.js';
import { dialog, frage, hinweis, fehlerMelden, laden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { Tabelle } from '../kern/tabelle.js';
import { Stundenplanraster } from '../kern/raster.js';
import { befehl } from '../kern/modulsystem.js';

const KENNZAHLEN = [
  ['bewertung', 'Bewertung'], ['nichtVerplant', 'Nicht verplante'], ['hohlstundenKlassen', 'Hohlstunden'], ['kernzeitverletzungen', 'Kernzeitverl.'],
  ['fachZweimalTag', 'Fach 2x/Tag'], ['doppelstundenfehler', 'Doppelst.-Fehler'], ['zeitwunschverletzungen', 'Zeitwünsche'], ['hohlstundenLehrer', 'Lehrer-Hohlst.'],
  ['ohneRaum', 'Kein Raum'], ['kollisionen', 'Kollisionen'],
];

const zahl = (n) => (typeof n === 'number' ? n.toLocaleString('de-DE') : n ?? '');

// =============================================================================================
// Diagnose

function diagnoseFenster(f) {
  let daten = null;
  let rubrik = 'eingabedaten';
  let punkt = null;
  const reiter = h('div', { class: 'op-reiter' });
  const liste = h('div', { class: 'op-liste', tabindex: '0' });
  const titel = h('div', { class: 'op-titel' }, 'Art der Diagnose');
  const beschreibung = h('div', { class: 'op-beschreibung' });
  const faelleBox = h('div', { class: 'op-faelle' });
  const links = h('div', { class: 'op-links' }, reiter, liste);
  const rechts = h('div', { class: 'op-rechts' }, titel, beschreibung, faelleBox);
  f.inhalt.append(h('div', { class: 'op-diagnose' }, links, rechts));

  const faelle = new Tabelle(faelleBox, {
    spalten: [
      { schluessel: 'element', titel: 'Element', breite: 90, art: 'NurLesen' },
      { schluessel: 'u', titel: 'U-Nr', breite: 54, art: 'NurLesen', ausrichtung: 'rechts' },
      { schluessel: 'zeit', titel: 'Zeit', breite: 70, art: 'NurLesen' },
      { schluessel: 'detail', titel: 'Detail', breite: 420, art: 'NurLesen' },
    ],
    id: (z) => z.nr,
    zustandSchluessel: 'optimierung.diagnose.faelle',
    leerText: 'Keine Fälle',
    beiDoppelklick: (z) => planZeigen(z),
  });

  function reiterZeichnen() {
    leeren(reiter);
    for (const [k, t] of [['eingabedaten', 'Eingabedaten'], ['stundenplan', 'Stundenplan']]) {
      const anzahl = (daten?.[k] || []).reduce((a, p) => a + p.anzahl, 0);
      reiter.append(h('button', { type: 'button', class: k === rubrik ? 'aktiv' : '', onclick: () => { rubrik = k; punkt = null; zeichnen(); } }, t, daten ? h('span', { class: 'op-anz' }, String(anzahl)) : null));
    }
  }

  function zeichnen() {
    reiterZeichnen();
    leeren(liste);
    const punkte = daten?.[rubrik] || [];
    liste.append(h('div', { class: 'op-zeile kopf' }, h('span', null, 'Diagnose'), h('span', { class: 'z' }, 'Gew.'), h('span', { class: 'z' }, 'Anz.')));
    const gruppen = [...new Set(punkte.map((p) => p.gruppe))];
    if (!punkte.includes(punkt)) punkt = punkte.find((p) => p.anzahl > 0) || null;
    if (!punkte.length) liste.append(h('div', { class: 'op-leer' }, rubrik === 'stundenplan' ? 'Keine Verstöße im Stundenplan.' : 'Keine Auffälligkeiten.'));
    for (const g of gruppen) {
      const ps = punkte.filter((p) => p.gruppe === g);
      liste.append(h('div', { class: 'op-zeile gruppe' }, h('span', null, g), h('span', null, ''), h('span', { class: 'z' }, String(ps.reduce((a, p) => a + p.anzahl, 0)))));
      for (const p of ps) {
        const el = h('div', { class: `op-zeile punkt${p.anzahl === 0 ? ' leer' : p.gewicht >= 5 ? ' schwer' : ''}${p === punkt ? ' gewaehlt' : ''}`, tabindex: '-1' },
          h('span', null, p.titel), h('span', { class: 'z' }, String(p.gewicht)), h('span', { class: 'z' }, String(p.anzahl)));
        el.addEventListener('click', () => { punkt = p; zeichnen(); });
        liste.append(el);
      }
    }
    details();
  }

  function details() {
    if (!punkt) { titel.textContent = 'Art der Diagnose'; beschreibung.textContent = ''; faelle.daten([]); return; }
    titel.textContent = `${punkt.rubrik} | ${punkt.gruppe} | ${punkt.titel}`;
    beschreibung.textContent = punkt.beschreibung;
    faelle.daten(punkt.faelle.map((x, i) => ({ ...x, nr: i, u: x.u ?? '' })));
  }

  function planZeigen(z = faelle.aktiveZeile()) {
    if (!z) { hinweis('Bitte einen Fall wählen.', 'warnung'); return; }
    if (z.typ && z.id) befehl('stundenplan.oeffnen', { typ: z.typ, element: z.id });
    else if (z.klasse) befehl('stundenplan.oeffnen', { typ: 'klasse', element: z.klasse });
    else if (z.lehrer) befehl('stundenplan.oeffnen', { typ: 'lehrer', element: z.lehrer });
    else hinweis('Zu diesem Fall gibt es keinen Stundenplan.', 'info');
  }

  async function neu() {
    const weg = laden(f.inhalt);
    try {
      daten = await api.get(api.dok('/optimierung/diagnose'));
      if (punkt) punkt = (daten[rubrik] || []).find((p) => p.titel === punkt.titel && p.gruppe === punkt.gruppe) || null;
      zeichnen();
      const k = daten.kennzahlen || {};
      f.fuss.replaceChildren(h('span', null, `Bewertung ${zahl(k.bewertung)}`), h('span', null, `${zahl(k.nichtVerplant)} nicht verplant`), h('span', null, `${zahl(k.kollisionen)} Kollisionen`),
        h('span', null, `${zahl(daten.planstunden)} Planstunden`), h('span', { class: 'rechts' }, daten.periode || ''));
    } finally { weg(); }
  }

  f.werkzeug({ text: 'Aktualisieren', symbol: 'diagnose', aktion: neu });
  f.werkzeug({ text: 'Betroffenen Stundenplan anzeigen', symbol: 'stundenplan', aktion: () => planZeigen() });
  f.trenner();
  f.werkzeug({ text: 'Gewichtung', symbol: 'statistik', aktion: () => gewichtungDialog() });
  f.werkzeug({ text: 'Optimierung', symbol: 'optimierung', aktion: () => optimierungStarten() });
  liste.addEventListener('keydown', (e) => {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const punkte = (daten?.[rubrik] || []);
    const i = punkte.indexOf(punkt);
    const n = punkte[Math.max(0, Math.min(punkte.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
    if (n) { punkt = n; zeichnen(); liste.querySelector('.gewaehlt')?.scrollIntoView({ block: 'nearest' }); }
  });
  const spaeter = entprellen(() => neu().catch(() => {}), 800);
  f.an('daten', (e) => { if (betrifft(e, 'Stammdaten', 'Unterricht', 'Stundenplan', 'Einstellungen')) spaeter(); });
  return neu();
}

// =============================================================================================
// Gewichtung

async function gewichtungDialog() {
  const g = await api.get(api.dok('/optimierung/gewichtung'));
  const werte = Object.fromEntries(g.regler.map((r) => [r.schluessel, r.wert]));
  const karten = [...g.karteikarten, 'Gewichtungs-Verteilung', 'Analyse'];
  let karte = karten[0];
  const schreiben = darfSchreiben();
  const links = h('div', { class: 'op-karten' });
  const rechts = h('div', { class: 'op-regler' });
  const inhalt = h('div', { class: 'op-gewichtung' }, links, rechts);

  function zeichnen() {
    leeren(links);
    for (const k of karten) links.append(h('button', { type: 'button', class: k === karte ? 'aktiv' : '', onclick: () => { karte = k; zeichnen(); } }, k));
    leeren(rechts);
    if (karte === 'Gewichtungs-Verteilung') {
      const anzahl = [0, 0, 0, 0, 0, 0];
      for (const v of Object.values(werte)) anzahl[Math.max(0, Math.min(5, v))]++;
      const max = Math.max(1, ...anzahl);
      rechts.append(h('div', { class: 'op-verteilung' }, anzahl.map((n, i) => h('div', { class: 'op-saeule' },
        h('span', { class: 'n' }, String(n)), h('div', { class: 'balken', style: { height: `${(n / max) * 100}%` } }), h('span', { class: 'z' }, String(i))))),
      h('div', { class: 'op-achse' }, h('span', null, 'unwichtig'), h('span', null, 'extrem wichtig')),
      h('p', { class: 'pl-leise' }, 'Ziel: wenige Regler auf 4 und 5 (glockenförmige Verteilung).'));
      return;
    }
    if (karte === 'Analyse') {
      const tab = h('tbody', null, h('tr', null, h('td', { colspan: 3, class: 'pl-leise' }, 'Wird berechnet …')));
      rechts.append(h('table', { class: 'pl-tabelle op-analyse' }, h('thead', null, h('tr', null, h('th', null, 'Regler'), h('th', null, 'Gewicht'), h('th', null, 'Verstöße im aktuellen Plan'))), tab));
      api.get(api.dok('/optimierung/gewichtung/analyse')).then((a) => {
        leeren(tab);
        for (const r of a) {
          const w = werte[r.schluessel];
          tab.append(h('tr', null, h('td', null, r.text), h('td', null, `${w} (${g.ziffern[w]?.text || ''})`), h('td', { class: 'z' }, r.verstoesse === null ? '–' : zahl(r.verstoesse))));
        }
      }).catch(fehlerMelden);
      return;
    }
    rechts.append(h('div', { class: 'op-skala' }, h('span', null, 'unwichtig'), h('span', null, 'extrem wichtig')));
    for (const r of g.regler.filter((x) => x.karteikarte === karte)) {
      const anzeige = h('b', { class: 'op-wert' }, String(werte[r.schluessel]));
      const regler = h('input', { type: 'range', min: 0, max: 5, step: 1, value: werte[r.schluessel], disabled: !schreiben, 'aria-label': r.text, dataset: { schluessel: r.schluessel } });
      regler.addEventListener('input', () => { werte[r.schluessel] = Number(regler.value); anzeige.textContent = regler.value; anzeige.title = g.ziffern[werte[r.schluessel]].text; });
      anzeige.title = g.ziffern[werte[r.schluessel]].text;
      rechts.append(h('label', { class: 'op-reglerzeile' }, h('span', { class: 'text' }, r.text), regler, anzeige,
        r.standard !== werte[r.schluessel] ? h('span', { class: 'op-standard', title: 'Voreinstellung' }, `(${r.standard})`) : null));
    }
    rechts.append(h('p', { class: 'pl-leise' }, 'Verwenden Sie die Prioritäten 4 und 5 nicht zu häufig. Zwischen 4 und 5 ist ein (sehr) großer Unterschied – stellen Sie Regler zunächst maximal auf 4.'));
  }

  const speichern = async () => {
    await api.put(api.dok('/optimierung/gewichtung'), { werte });
    hinweis('Gewichtung gespeichert.', 'ok', 2000);
  };
  zeichnen();
  const knoepfe = schreiben
    ? [{ text: 'Voreinstellung', wert: 'standard' }, { text: 'Übernehmen', wert: 'uebernehmen' }, { text: 'OK', wert: 'ok', primaer: true }, { text: 'Abbrechen', wert: null }]
    : [{ text: 'Schließen', wert: null, primaer: true }];
  const p = dialog({
    titel: 'Gewichtung', inhalt, knoepfe,
    pruefen: async (w) => {
      if (w === 'standard') { for (const r of g.regler) werte[r.schluessel] = r.standard; zeichnen(); return false; }
      if (w === 'uebernehmen') { await speichern().catch(fehlerMelden); return false; }
      if (w === 'ok') { try { await speichern(); } catch (e) { fehlerMelden(e); return false; } }
      return true;
    },
  });
  const box = [...document.querySelectorAll('.dialog-hintergrund .dialog')].pop();
  if (box) { box.style.width = 'min(820px, 96vw)'; box.style.maxWidth = 'calc(100vw - 24px)'; }
  await p;
}

// =============================================================================================
// Optimierung: Steuerdaten → Hintergrundauftrag → Ergebnis

const STANDARD = { strategie: 'B', anzahlPlaene: 3, schritte: 2, startProzent: 30, erhoehungProzent: 20, aehnlichkeit: 0, bedingtFixieren: false, nurGewuenschteFreieTage: false, doppelstundenBesonders: false, nurNichtVerplante: false, zuordnen: false };

async function steuerdatenDialog() {
  const [strategien, gemerkt] = await Promise.all([api.get(api.dok('/optimierung/strategien')), einstellungLesen('optimierung.optionen')]);
  const o = { ...STANDARD, ...(gemerkt || {}) };
  const zahlFeld = (name, min, max) => h('input', { class: 'eingabe op-zahl', type: 'number', min, max, value: o[name], name });
  const haken = (name, text) => h('label', { class: 'op-haken' }, h('input', { type: 'checkbox', name, checked: !!o[name] }), ` ${text}`);
  const strategie = h('select', { class: 'eingabe', name: 'strategie' }, strategien.map((s) => h('option', { value: s.wert, selected: s.wert === o.strategie }, s.text)));
  const plaene = zahlFeld('anzahlPlaene', 1, 20), schritte = zahlFeld('schritte', 1, 9), aehnlich = zahlFeld('aehnlichkeit', 0, 4);
  const start = zahlFeld('startProzent', 5, 100), erhoehung = zahlFeld('erhoehungProzent', 5, 100);
  const bedingt = haken('bedingtFixieren', 'Plan bedingt fixieren');
  const cd = h('fieldset', { class: 'op-gruppe' }, h('legend', null, 'Für Strategie C und D'),
    h('label', { class: 'op-zeile2' }, start, ' Start-Prozentsatz'), h('label', { class: 'op-zeile2' }, erhoehung, ' Erhöhungs-Prozentsatz'));
  const inhalt = h('div', { class: 'op-steuerdaten' },
    h('div', { class: 'op-spalte' },
      h('fieldset', { class: 'op-gruppe' }, h('legend', null, 'Ablauf der Optimierung'),
        h('label', { class: 'op-block' }, 'Optimierungs-Strategie (A, B, …)', strategie),
        h('label', { class: 'op-zeile2' }, plaene, ' Je Serie der Opt.: Anzahl Pläne (1-20)'),
        h('label', { class: 'op-zeile2' }, schritte, ' Optimierungsschritte je Plan (1-9)')),
      h('fieldset', { class: 'op-gruppe' }, h('legend', null, 'Lehrer-Zuordnung'), haken('zuordnen', 'Unterricht ohne Lehrer vorher automatisch zuordnen'))),
    h('div', { class: 'op-spalte' },
      h('label', { class: 'op-zeile2' }, aehnlich, ' Ähnlichkeit zu Vorplan (0-4, 0 = nicht ähnlich, 4 = sehr ähnlich)'),
      bedingt, haken('nurGewuenschteFreieTage', 'Lehrer nur gewünschte freie Tage'), haken('doppelstundenBesonders', 'Doppelstunden besonders optimieren'),
      haken('nurNichtVerplante', 'Nur nicht verplante Stunden setzen'), cd),
    h('p', { class: 'op-warnung' }, 'Prüfen Sie vor den ersten Optimierungsläufen Ihre Eingabedaten mit der Diagnose. Fixierte Stunden bleiben immer stehen. „Plan bedingt fixieren“ verlegt vorhandene Stunden nur, wenn es nicht anders geht. Die Optimierung läuft auf dem Server im Hintergrund – Sie können währenddessen weiterarbeiten.'));
  const umschalten = () => {
    const c = ['C', 'D'].includes(strategie.value);
    for (const el of cd.querySelectorAll('input')) el.disabled = !c;
    cd.classList.toggle('aus', !c);
    aehnlich.disabled = bedingt.querySelector('input').checked;
  };
  strategie.addEventListener('change', umschalten);
  bedingt.querySelector('input').addEventListener('change', umschalten);
  umschalten();
  const p = dialog({ titel: 'Steuerdaten zur Optimierung', inhalt, knoepfe: [{ text: 'Optimierung starten', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
  const box = [...document.querySelectorAll('.dialog-hintergrund .dialog')].pop();
  if (box) box.style.width = 'min(700px, 96vw)';
  if (!(await p)) return null;
  const werte = { strategie: strategie.value };
  for (const el of inhalt.querySelectorAll('input[name]')) werte[el.name] = el.type === 'checkbox' ? el.checked : Math.round(Number(el.value) || 0);
  einstellungSchreiben('optimierung.optionen', werte);
  return werte;
}

async function optimierungStarten() {
  if (!zustand.dokument) return;
  const lauf = await api.get(api.dok('/optimierung/auftrag')).catch(() => null);
  if (lauf?.status === 'laeuft') { fensterverwaltung.oeffnen('optimierung.lauf', {}); hinweis('Die Optimierung läuft bereits.', 'info'); return; }
  if (!darfSchreiben()) { if (lauf?.status !== 'keiner') fensterverwaltung.oeffnen('optimierung.lauf', {}); else hinweis('Zum Optimieren fehlt die Berechtigung.', 'warnung'); return; }
  const o = await steuerdatenDialog();
  if (!o) return;
  const { zuordnen, ...optionen } = o;
  const a = await api.post(api.dok('/optimierung/start'), { optionen, zuordnen });
  if (a.zugeordnet) hinweis(`${a.zugeordnet} Kopplungszeilen wurde vor der Optimierung ein Lehrer zugeordnet.`, 'info', 5000);
  const f = fensterverwaltung.oeffnen('optimierung.lauf', {});
  f?.def.erneutGeoeffnet?.(f, {});
}

function laufFenster(f) {
  let auftrag = null;
  let gewaehlt = null;
  let timer = 0;
  let vergleich = null;
  const kopf = h('div', { class: 'op-lauf-kopf' });
  const balken = h('div', { class: 'op-balken' }, h('div', { class: 'op-balken-innen' }));
  const ergebnis = h('div', { class: 'op-ergebnis' });
  const vergleichBox = h('div', { class: 'op-vergleich', hidden: true });
  f.inhalt.append(h('div', { class: 'op-lauf' }, kopf, balken, ergebnis, vergleichBox));
  const schreiben = darfSchreiben();

  const kAbbrechen = f.werkzeug({ text: 'Abbrechen', symbol: 'schliessen', aktion: async () => { auftrag = await api.post(api.dok('/optimierung/auftrag/abbrechen')); zeichnen(); } });
  const kUebernehmen = f.werkzeug({ text: 'Plan übernehmen', symbol: 'beispiel', aktion: () => uebernehmen() });
  const kVergleich = f.werkzeug({ text: 'Vergleich', symbol: 'nebeneinander', titel: 'Ergebnisvergleich: aktueller Plan und gewählter Plan nebeneinander', aktion: () => vergleichUmschalten() });
  f.trenner();
  const kNeu = f.werkzeug({ text: 'Neue Optimierung', symbol: 'optimierung', aktion: () => optimierungStarten() });
  const kVerwerfen = f.werkzeug({ text: 'Verwerfen', symbol: 'loeschen', titel: 'Ergebnisse verwerfen', aktion: async () => { await api.loeschen(api.dok('/optimierung/auftrag')); auftrag = null; gewaehlt = null; zeichnen(); } });
  f.werkzeug({ text: 'Diagnose', symbol: 'diagnose', aktion: () => befehl('optimierung.diagnose') });

  function zeichnen() {
    const a = auftrag;
    const laeuft = a?.status === 'laeuft';
    if (a?.ergebnisse?.length && (!gewaehlt || !a.ergebnisse.some((e) => e.nummer === gewaehlt))) gewaehlt = a.ergebnisse[0].nummer;
    kAbbrechen.disabled = !laeuft || !schreiben;
    kUebernehmen.disabled = !schreiben || !a?.ergebnisse?.length || !gewaehlt || laeuft;
    kVergleich.disabled = !a?.ergebnisse?.length || !gewaehlt;
    kNeu.disabled = laeuft || !schreiben;
    kVerwerfen.disabled = laeuft || !a || a.status === 'keiner' || !schreiben;
    balken.hidden = !a || a.status === 'keiner';
    balken.classList.toggle('laeuft', laeuft);
    balken.firstChild.style.width = `${Math.round((a?.anteil || 0) * 1000) / 10}%`;
    if (!a || a.status === 'keiner') {
      kopf.replaceChildren(h('div', { class: 'op-gross' }, 'Keine Optimierung'), h('p', null, 'Mit „Neue Optimierung“ die Steuerdaten wählen und den Lauf starten. Er läuft auf dem Server im Hintergrund.'));
      leeren(ergebnis);
      return;
    }
    const statusText = { laeuft: 'Optimierung läuft …', fertig: 'OK', abgebrochen: 'Abgebrochen', fehler: 'Fehler', uebernommen: `Plan ${a.uebernommen} übernommen` }[a.status] || a.status;
    kopf.replaceChildren(
      h('div', { class: `op-gross ${a.status}` }, statusText),
      h('div', { class: 'op-info' },
        h('div', null, `${a.strategie}, Schritte ${a.optionen.schritte}, ${a.plaene} Pläne`),
        h('div', null, laeuft ? `${a.phase} · ${Math.round(a.anteil * 100)} % · ${a.dauer.toLocaleString('de-DE')} s` : `${a.phase || ''} · Dauer ${a.dauer.toLocaleString('de-DE')} s`),
        h('div', { class: 'pl-leise' }, `gestartet von ${a.benutzer} um ${new Date(a.start).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`),
        a.fehler ? h('div', { class: 'fehlertext' }, a.fehler) : null,
        a.dokumentGeaendert && a.ergebnisse && a.status === 'fertig' ? h('div', { class: 'op-hinweis' }, 'Das Dokument wurde seit dem Start geändert – beim Übernehmen gehen diese Stundenplanänderungen verloren.') : null));
    leeren(ergebnis);
    if (!a.ergebnisse?.length) return;
    const besser = (k, wert) => (a.aktuell?.[k] ?? 0) - wert;
    const tbody = h('tbody');
    const zeile = (name, k, extra, nummer) => {
      const tr = h('tr', { class: nummer === null ? 'aktuell' : nummer === gewaehlt ? 'gewaehlt' : '' },
        h('td', null, name), KENNZAHLEN.map(([s]) => {
          const v = k[s];
          const d = nummer === null ? 0 : besser(s, v);
          return h('td', { class: `z${d > 0 ? ' gruen' : d < 0 ? ' rot' : ''}`, title: nummer === null || !d ? '' : `${d > 0 ? 'besser' : 'schlechter'} um ${zahl(Math.abs(d))}` }, zahl(v));
        }), h('td', { class: 'z' }, extra.abw), h('td', { class: 'z' }, extra.dauer));
      if (nummer !== null) {
        tr.addEventListener('click', () => { gewaehlt = nummer; zeichnen(); if (vergleich) vergleich.neu(); });
        tr.addEventListener('dblclick', () => { gewaehlt = nummer; uebernehmen(); });
      }
      tbody.append(tr);
    };
    zeile('Akt. Plan', a.aktuell || {}, { abw: '', dauer: '' }, null);
    for (const e of a.ergebnisse) zeile(`Plan ${e.nummer}${a.uebernommen === e.nummer ? ' ✓' : ''}`, e.kennzahlen, { abw: zahl(e.abweichungen), dauer: `${e.dauer.toLocaleString('de-DE')} s` }, e.nummer);
    ergebnis.append(
      h('div', { class: 'op-rollen' }, h('table', { class: 'pl-tabelle op-kennzahlen' },
        h('thead', null, h('tr', null, h('th', null, 'Plan'), KENNZAHLEN.map(([, t]) => h('th', null, t)), h('th', { title: 'Stunden an anderer Stelle als im aktuellen Plan' }, 'Abweich.'), h('th', null, 'Dauer'))), tbody)),
      h('p', { class: 'pl-leise' }, 'Je niedriger die Bewertungszahl, umso weniger Vorgaben sind verletzt. Der beste Plan ist vorausgewählt (grün = besser als der aktuelle Plan). Doppelklick übernimmt den Plan.'));
  }

  async function abfragen() {
    clearTimeout(timer);
    try {
      auftrag = await api.get(api.dok('/optimierung/auftrag'));
      zeichnen();
      f.titel(auftrag.status === 'laeuft' ? `Optimierung – ${Math.round(auftrag.anteil * 100)} %` : 'Optimierung');
    } catch (e) { if (e.status !== 401) console.warn(e); }
    if (auftrag?.status === 'laeuft') timer = setTimeout(abfragen, 600);
    else if (auftrag?.status === 'fertig' && !f._gemeldet) { f._gemeldet = true; hinweis('Optimierung abgeschlossen – Ergebnis prüfen und Plan übernehmen.', 'ok', 5000); }
  }

  async function uebernehmen(trotz = false) {
    if (!gewaehlt || !schreiben) return;
    try {
      const erg = await api.post(api.dok('/optimierung/auftrag/uebernehmen'), { nummer: gewaehlt, trotzAenderung: trotz });
      hinweis(`Plan ${erg.nummer} übernommen (${zahl(erg.planstunden)} Stunden) – Strg+Z macht es rückgängig.`, 'ok', 4000);
      await abfragen();
      vergleich?.neu();
    } catch (e) {
      if (e.status === 409 && (await frage(`${e.message}\n\nTrotzdem übernehmen?`, { gefahr: true, ja: 'Übernehmen', nein: 'Abbrechen' }))) return uebernehmen(true);
      if (e.status !== 409) throw e;
    }
  }

  function vergleichUmschalten() {
    if (vergleich) { vergleich.weg(); vergleich = null; vergleichBox.hidden = true; kVergleich.classList.remove('gedrueckt'); return; }
    vergleich = vergleichAufbauen();
    vergleichBox.hidden = false;
    kVergleich.classList.add('gedrueckt');
  }

  function vergleichAufbauen() {
    let typ = f.parameter.vergleichTyp || 'klasse';
    let element = f.parameter.vergleichElement || null;
    const typWahl = h('select', { class: 'eingabe' }, [['klasse', 'Klasse'], ['lehrer', 'Lehrkraft'], ['raum', 'Raum']].map(([w, t]) => h('option', { value: w, selected: w === typ }, t)));
    const elWahl = h('select', { class: 'eingabe' });
    const lA = h('div', { class: 'op-plan-titel' }, 'Aktueller Plan');
    const lB = h('div', { class: 'op-plan-titel' });
    const boxA = h('div', { class: 'op-plan' }), boxB = h('div', { class: 'op-plan' });
    const rA = new Stundenplanraster(boxA, { typ, kompakt: true });
    const rB = new Stundenplanraster(boxB, { typ, kompakt: true });
    leeren(vergleichBox).append(h('div', { class: 'op-vergleich-kopf' }, typWahl, elWahl, h('span', { class: 'pl-leise' }, 'Unterschiede sind im rechten Plan markiert.')),
      h('div', { class: 'op-plaene' }, h('div', null, lA, boxA), h('div', null, lB, boxB)));
    async function liste() {
      const l = await elemente(typ);
      leeren(elWahl).append(...l.map((e) => h('option', { value: e.id, selected: e.id === element }, e.l ? `${e.k} – ${e.l}` : e.k)));
      if (!element || !l.some((e) => e.id === element)) element = l[0]?.id ?? null;
      elWahl.value = String(element);
    }
    async function neu() {
      if (!element || !gewaehlt) return;
      f.parameterSetzen({ vergleichTyp: typ, vergleichElement: element });
      const [akt, erg] = await Promise.all([api.get(api.dok(`/stundenplan/${typ}/${element}`)), api.get(api.dok(`/optimierung/auftrag/plan/${gewaehlt}/${typ}/${element}`))]);
      lB.textContent = `Plan ${gewaehlt}`;
      rA.o.typ = rB.o.typ = typ;
      rA.setzen(akt);
      rB.setzen(erg);
      // Unterschiede markieren: Felder, deren Unterrichte sich gegenüber dem aktuellen Plan geändert haben
      const k = (p) => new Map(p.zellen.map((z) => [`${z.tag}:${z.stunde}`, z.eintraege.map((e) => e.u).sort().join(',')]));
      const ka = k(akt), kb = k(erg);
      for (const [key, feld] of rB.felder) feld.classList.toggle('op-anders', (ka.get(key) || '') !== (kb.get(key) || ''));
    }
    typWahl.addEventListener('change', async () => { typ = typWahl.value; element = null; await liste(); neu().catch(fehlerMelden); });
    elWahl.addEventListener('change', () => { element = Number(elWahl.value); neu().catch(fehlerMelden); });
    liste().then(neu).catch(fehlerMelden);
    return { neu: () => neu().catch(fehlerMelden), weg: () => { rA.entfernen(); rB.entfernen(); leeren(vergleichBox); } };
  }

  f.beimSchliessen(() => { clearTimeout(timer); vergleich?.weg(); });
  f.an('daten', (e) => { if (!auftrag || auftrag.status !== 'laeuft') { if (betrifft(e, 'Stundenplan')) abfragen(); } });
  f._abfragen = abfragen;
  zeichnen();
  return abfragen();
}

// =============================================================================================

export default {
  id: 'optimierung',
  css: ['css/module/optimierung.css', 'css/module/planung.css'],
  fenster: {
    'optimierung.diagnose': { titel: 'Stundenplan-Diagnose', symbol: 'diagnose', breite: 940, hoehe: 500, einzig: () => 'diagnose', erstellen: diagnoseFenster },
    'optimierung.lauf': {
      titel: 'Optimierung', symbol: 'optimierung', breite: 900, hoehe: 440, einzig: () => 'lauf',
      erneutGeoeffnet: (f) => { f._gemeldet = false; f._abfragen?.(); },
      erstellen: laufFenster,
    },
  },
  befehle: {
    'optimierung.diagnose': () => (zustand.dokument ? fensterverwaltung.oeffnen('optimierung.diagnose', {}) : null),
    'optimierung.gewichtung': () => gewichtungDialog(),
    'optimierung.starten': () => optimierungStarten(),
    'optimierung.ergebnis': () => fensterverwaltung.oeffnen('optimierung.lauf', {}),
  },
  ribbon: [
    { id: 'start.diagnose', reiter: 'Start', gruppe: 'Planungswerkzeuge', text: 'Diagnose', symbol: 'diagnose', befehl: 'optimierung.diagnose', braucht: 'dokument', hinweis: 'Stundenplan-Diagnose: Eingabedaten und Verstöße im Stundenplan' },
    { id: 'start.gewichtung', reiter: 'Start', gruppe: 'Planungswerkzeuge', text: 'Gewichtung', symbol: 'statistik', befehl: 'optimierung.gewichtung', braucht: 'dokument', hinweis: 'Gewichtung der Optimierungsziele (0 unwichtig … 5 extrem wichtig)' },
    { id: 'start.optimierung', reiter: 'Start', gruppe: 'Planungswerkzeuge', text: 'Optimierung', symbol: 'optimierung', befehl: 'optimierung.starten', braucht: 'dokument', hinweis: 'Automatische Verplanung (läuft im Hintergrund)' },
    { id: 'planung.diagnose', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Diagnose', symbol: 'diagnose', gross: true, befehl: 'optimierung.diagnose', braucht: 'dokument' },
    { id: 'planung.gewichtung', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Gewichtung', symbol: 'statistik', gross: true, befehl: 'optimierung.gewichtung', braucht: 'dokument' },
    { id: 'planung.optimierung', reiter: 'Planung', gruppe: 'Werkzeuge', text: 'Optimierung', symbol: 'optimierung', gross: true, befehl: 'optimierung.starten', braucht: 'dokument' },
  ],
  start() {
    // nach dem Öffnen eines Dokuments: läuft dort gerade eine Optimierung, das Fortschrittsfenster zeigen
    an('dokument', async (d) => {
      if (!d) return;
      try {
        const a = await api.get(api.dok('/optimierung/auftrag'));
        if (a.status === 'laeuft') fensterverwaltung.oeffnen('optimierung.lauf', {});
      } catch { /* ohne Hinweis */ }
    });
  },
};
