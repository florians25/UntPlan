// Modul „Kursplanung“ (Desktop: KursplanungFenster): Kurse einer Klasse bzw. eines Jahrgangs (Kursname und Band änderbar),
// Kurswahlen der Schüler, Kollisionsmatrix, automatische Bandbildung mit Vorschlag und Übernahme der Bänder.
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { formular, frage, hinweis, laden, fehlerMelden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';

const REITER = [['kurse', 'Kurse'], ['wahlen', 'Kurswahlen'], ['matrix', 'Kollisionsmatrix'], ['baender', 'Bandbildung']];

function kursFenster(f, p) {
  f.titel('Kursplanung');
  const schreiben = darfSchreiben();
  let reiter = p.tab || 'kurse';
  let d = null;
  let vorschlag = null;
  const klasse = h('select', { class: 'eingabe', style: { width: '110px' } });
  f.werkzeuge.append(h('span', null, 'Klasse/Jahrgang:'), klasse);
  f.trenner();
  f.werkzeug({ text: 'Schüler', symbol: 'schueler', aktion: () => fensterverwaltung.oeffnen('stammdaten', { typ: 'schueler' }) });
  if (schreiben) f.werkzeug({ text: 'Kurswahlen zufällig füllen (Test)', symbol: 'neu', aktion: zufall });
  const leiste = h('div', { class: 'kp-reiter' });
  const flaeche = h('div', { class: 'kp-flaeche' });
  const status = h('div', { class: 'kp-status' });
  f.inhalt.append(leiste, flaeche, status);
  let tabelle = null;

  function leisteZeichnen() {
    leiste.replaceChildren(...REITER.map(([id, t]) => h('button', { type: 'button', class: id === reiter ? 'aktiv' : '', onclick: () => { reiter = id; f.parameterSetzen({ tab: id }); zeichnen(); } }, t)));
  }

  function zeichnen() {
    leisteZeichnen();
    tabelle?.entfernen?.();
    tabelle = null;
    flaeche.replaceChildren();
    if (!d) return;
    status.textContent = d.status;
    status.classList.toggle('kp-kollision', d.kollisionen > 0);
    if (reiter === 'kurse') {
      tabelle = new Tabelle(flaeche, {
        zustandSchluessel: 'tabelle.kursplanung.kurse',
        aendern: schreiben ? async (z, s, wert) => { d = await api.patch(api.dok(`/kursplanung/${d.klasse.id}/kurs/${z.id}`), { feld: s.schluessel, wert }, { quelle: f.id }); setTimeout(zeichnen); return null; } : undefined,
        spalten: [['id', 'U-Nr', 50], ['kursname', 'Kursname', 100, true], ['fach', 'Fach', 55], ['lehrer', 'Lehrer', 70], ['klassen', 'Klasse(n)', 80], ['wst', 'Wst.', 45], ['band', 'Band', 80, true], ['teilnehmer', 'Teilnehmer', 80], ['mw', 'm/w', 60]]
          .map(([schluessel, titel, breite, edit]) => ({ schluessel, titel, breite, art: edit && schreiben ? 'Text' : 'NurLesen', editierbar: !!edit && schreiben, farbe: edit && schreiben ? () => '#ffffee' : undefined })),
      });
      tabelle.daten(d.kurse);
    } else if (reiter === 'wahlen') {
      tabelle = new Tabelle(flaeche, {
        aendern: schreiben ? async (z, s, wert) => {
          d = await api.put(api.dok(`/kursplanung/${d.klasse.id}/wahl`), { schueler: z.id, kurs: s.kurs, gewaehlt: wert === 'X' || wert === 'true' || wert === 'x' }, { quelle: f.id });
          tabelle.daten(wahlZeilen());
          status.textContent = d.status;
          return null;
        } : undefined,
        spalten: [{ schluessel: 'name', titel: 'Schüler', breite: 170, art: 'NurLesen', editierbar: false }, { schluessel: 'anzahl', titel: 'Anz.', breite: 45, art: 'NurLesen', editierbar: false },
          ...d.kurse.map((k) => ({ schluessel: `k${k.id}`, kurs: k.id, titel: k.kursname, breite: 62, art: schreiben ? 'Bool' : 'NurLesen', editierbar: schreiben }))],
        leerText: 'Keine Schüler in dieser Klasse',
      });
      tabelle.daten(wahlZeilen());
    } else if (reiter === 'matrix') {
      tabelle = new Tabelle(flaeche, {
        spalten: [{ schluessel: 'kurs', titel: 'Kurs', breite: 120, art: 'NurLesen', editierbar: false },
          ...d.kurse.map((k, j) => ({ schluessel: `m${j}`, titel: k.kursname, breite: 52, art: 'NurLesen', editierbar: false, wert: (z) => (z.werte[j] > 0 ? z.werte[j] : ''),
            farbe: (z) => { if (z.werte[j] < 0) return '#dcdcdc'; const a = d.kurse[z.i]; return a.band && a.band === k.band ? (z.werte[j] > 0 ? '#f6c6c6' : '#e1f5e1') : null; } }))],
      });
      tabelle.daten(d.kurse.map((k, i) => ({ id: k.id, i, kurs: `${k.kursname} (${k.band})`, werte: d.matrix[i] })));
    } else {
      const max = h('input', { class: 'eingabe', type: 'number', min: 1, max: 30, value: 10, style: { width: '60px' } });
      flaeche.append(h('div', { class: 'kp-werkzeug' }, h('span', null, 'max. Bänder:'), max,
        h('button', { class: 'knopf', type: 'button', onclick: async () => {
          const weg = laden(f.inhalt);
          try { vorschlag = await api.post(api.dok(`/kursplanung/${d.klasse.id}/bandbildung`), { maxBaender: Number(max.value) || 10 }); status.textContent = vorschlag.text; baender(); } catch (e) { fehlerMelden(e); } finally { weg(); }
        } }, 'Bandbildung starten'),
        h('button', { class: 'knopf', type: 'button', disabled: !schreiben, onclick: uebernehmen }, 'Bänder übernehmen')));
      baender();
    }
  }

  function wahlZeilen() {
    return d.schueler.map((s) => {
      const z = { id: s.id, name: s.name, anzahl: s.wahlen.length };
      for (const k of d.kurse) z[`k${k.id}`] = s.wahlen.includes(k.id) ? 'X' : '';
      return z;
    });
  }

  function baender() {
    tabelle?.entfernen?.();
    const quelle = vorschlag ? vorschlag.baender : d.baender;
    const max = Math.max(0, ...quelle.map((b) => b.kurse.length));
    const huelle = h('div', { class: 'kp-baender' });
    flaeche.querySelector('.kp-baender')?.remove();
    flaeche.append(huelle);
    tabelle = new Tabelle(huelle, {
      id: (z) => z.band,
      spalten: [{ schluessel: 'band', titel: vorschlag ? 'Band (Vorschlag)' : 'Band (aktuell)', breite: 120, art: 'NurLesen', editierbar: false },
        ...Array.from({ length: max }, (_, i) => ({ schluessel: `k${i}`, titel: `Kurs ${i + 1}`, breite: 100, art: 'NurLesen', editierbar: false, wert: (z) => z.kurse[i] || '' })),
        { schluessel: 'schueler', titel: 'Schüler', breite: 70, art: 'NurLesen', editierbar: false }],
      leerText: 'Noch keine Bänder – „Bandbildung starten“',
    });
    tabelle.daten(quelle);
  }

  async function uebernehmen() {
    if (!vorschlag) { hinweis('Bitte zuerst die Bandbildung starten.', 'warnung'); return; }
    const w = await formular('Bänder übernehmen', [{ name: 'praefix', titel: 'Präfix für die Bandbezeichnung', wert: d.klasse.kurz, pflicht: true }]);
    if (!w) return;
    d = await api.post(api.dok(`/kursplanung/${d.klasse.id}/baender`), { praefix: w.praefix, zuordnung: vorschlag.zuordnung }, { quelle: f.id });
    vorschlag = null;
    hinweis('Bänder in den Unterricht übernommen.', 'ok');
    zeichnen();
  }

  async function zufall() {
    if (!d || !(await frage('Für alle Schüler der Klasse ohne Kurswahl zufällig Kurse wählen (je Fach höchstens ein Kurs)?'))) return;
    d = await api.post(api.dok(`/kursplanung/${d.klasse.id}/zufall`), {}, { quelle: f.id });
    zeichnen();
  }

  async function laden_() {
    if (!klasse.value) { d = null; zeichnen(); status.textContent = 'Keine Klassen vorhanden.'; return; }
    const weg = laden(f.inhalt);
    try { d = await api.get(api.dok(`/kursplanung/${klasse.value}`)); vorschlag = null; zeichnen(); } catch (e) { fehlerMelden(e); } finally { weg(); }
  }
  klasse.addEventListener('change', () => { f.parameterSetzen({ klasse: Number(klasse.value) }); laden_(); });

  async function start() {
    try {
      const liste = await api.get(api.dok('/kursplanung'));
      const alt = Number(klasse.value) || p.klasse;
      klasse.replaceChildren(...liste.map((k) => h('option', { value: k.id, selected: k.id === alt }, k.kurz)));
    } catch (e) { fehlerMelden(e); }
    await laden_();
  }
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Stammdaten', 'Unterricht')) start(); });
  start();
}

const oeffnen = () => { if (zustand.dokument) fensterverwaltung.oeffnen('kursplanung', {}); };

export default {
  id: 'kursplanung',
  css: ['css/module/kursplanung.css'],
  fenster: { kursplanung: { titel: 'Kursplanung', symbol: 'schueler', breite: 1080, hoehe: 580, einzig: () => 'kurs', erstellen: kursFenster } },
  befehle: { 'kursplanung.oeffnen': oeffnen },
  ribbon: [
    { id: 'start.kursplanung', reiter: 'Start', gruppe: 'Module', text: 'Kursplanung', symbol: 'schueler', befehl: 'kursplanung.oeffnen', braucht: 'dokument' },
    { id: 'kurs.kurse', reiter: 'Kursplanung', gruppe: 'Kursplanung', text: 'Kurse & Bänder', symbol: 'schueler', gross: true, befehl: 'kursplanung.oeffnen', braucht: 'dokument' },
  ],
};
