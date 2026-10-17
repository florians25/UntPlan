// Modul „Veranstaltungen“ (Vertretungsmodus): Fenster „Veranst. / Lehrer“ wie im Desktop – VA-Nr., Klasse, Kl-Teil, Lehrer, Raum, Von,
// ab St., Bis, bis St., Grund, Text, zählen, Schülergruppe. Eingabe direkt in der Tabelle („Alle Lehrkräfte“, „Alle Klassen außer 5a“),
// Ersatzlehrkräfte statt „???“, Schnellauswahl (alle, nach Abteilung, Jahrgang, Gruppe …), alle aus den Stunden bzw. aus der gewählten
// Stunde übernehmen, Kopieren auf das eingestellte Datum.
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, elemente, darfSchreiben, modusSetzen } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { frage, hinweis, fehlerMelden, menue, formular } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';
import { planungsdatum, datumText, datumLesen } from './vertretung/kern.js';
import { zeitraumAuswahl } from './vertretung/steuer.js';

const FELDER = [['K', 'Klassen (entfällt)', 'klassen'], ['KT', 'Kl-Teil (parallel)', 'klassen'], ['L', 'Lehrkräfte', 'lehrer'], ['R', 'Räume', 'raeume']];
const vapfad = (p = '') => api.dok(`/veranstaltungen${p}`);

function veranstaltungenFenster(f, p) {
  const schreiben = darfSchreiben();
  let kopie = null;
  let letzteStunde = null;
  let maus = { x: 200, y: 200 };
  const zr = zeitraumAuswahl(f, { art: p.zeitraum || 'tag', beiAenderung: () => { f.parameterSetzen({ zeitraum: zr.art }); laden(); } });
  f.inhalt.append(h('div', { class: 'vt-kopf' }, h('span', { class: 'vt-gruppe' }, h('span', { class: 'vt-gtitel' }, 'Von-Bis'), zr.el)));

  const ed = (z) => schreiben && !!z;
  const tabelle = new Tabelle(f.inhalt, {
    spalten: [
      { schluessel: 'nr', titel: 'VA-Nr.', breite: 56, art: 'NurLesen', klasse: (z) => (z.offen ? 'vt-rot' : null) },
      { schluessel: 'k', titel: 'Klasse', breite: 110, art: 'ElementListe', refTyp: 'klasse', editierbarFuer: ed },
      { schluessel: 'kt', titel: 'Kl-Teil', breite: 80, art: 'ElementListe', refTyp: 'klasse', editierbarFuer: ed },
      { schluessel: 'l', titel: 'Lehrer', breite: 120, art: 'ElementListe', refTyp: 'lehrer', editierbarFuer: ed, farbe: (z) => (z.offen ? '#e12828' : null), klasse: (z) => (z.offen ? 'vt-weiss' : null) },
      { schluessel: 'r', titel: 'Raum', breite: 70, art: 'ElementListe', refTyp: 'raum', editierbarFuer: ed },
      { schluessel: 'v', titel: 'Von TT.MM.', breite: 96, editierbarFuer: ed },
      { schluessel: 'sv', titel: 'ab St.', breite: 50, art: 'Ganzzahl', editierbarFuer: ed },
      { schluessel: 'b', titel: 'Bis TT.MM.', breite: 96, editierbarFuer: ed },
      { schluessel: 'sb', titel: 'bis St.', breite: 50, art: 'Ganzzahl', editierbarFuer: ed },
      { schluessel: 'g', titel: 'Grund', breite: 64, art: 'Element', refTyp: 'abwesenheitsgrund', editierbarFuer: ed },
      { schluessel: 't', titel: 'Text', breite: 180, editierbarFuer: ed },
      { schluessel: 'z', titel: 'zählen', breite: 50, art: 'Bool', editierbarFuer: ed },
      { schluessel: 'sg', titel: 'Schülergruppe', breite: 120, art: 'Element', refTyp: 'schuelergruppe', editierbarFuer: ed },
    ],
    sortierbar: false,
    leerText: 'Keine Veranstaltungen',
    neuText: schreiben ? 'Neue Veranstaltung …' : undefined,
    zeilenKlasse: (z) => (z.vergangen ? 'leise' : ''),
    neu: schreiben ? async (s, wert) => { const r = await feld(null, s.schluessel, wert); if (r?.id) tabelle.waehle(r.id); return null; } : undefined,
    aendern: schreiben ? async (z, s, wert) => { await feld(z.id, s.schluessel, wert); return null; } : undefined,
    optionen: async (s) => {
      if (s.refTyp === 'schuelergruppe') return (await api.get(vapfad('/schuelergruppen'))).map((x) => ({ k: x, l: '' }));
      const liste = await elemente(s.refTyp);
      return s.art === 'ElementListe' ? [{ k: s.refTyp === 'lehrer' ? 'Alle Lehrkräfte' : s.refTyp === 'raum' ? 'Alle Räume' : 'Alle Klassen', l: 'alle (wählbaren) Elemente' }, ...liste] : liste;
    },
    beiAuswahl: (_, z) => { nrEl.value = z?.id || ''; if (z?.offen) hinweis(z.offen, 'warnung', 4000); },
    kontextmenue: (zeilen, spalte) => { schnellMenue(FELDER.find((x) => x[0].toLowerCase() === spalte?.schluessel)?.[0] || null, maus.x, maus.y); return []; },
    beiEntf: () => loeschen(),
  });
  tabelle.el.addEventListener('contextmenu', (e) => { maus = { x: e.clientX, y: e.clientY }; }, true);

  async function feld(id, schluessel, wert) {
    const F = schluessel.toUpperCase();
    let w = wert;
    if (F === 'V' || F === 'B') w = datumText(datumLesen(wert, zr.von));
    const r = await api.post(vapfad(id ? `/${id}` : ''), { feld: F, wert: w, datum: zr.von }, { quelle: f.id });
    await laden();
    return r;
  }
  const aktuell = () => tabelle.aktiveZeile();

  async function schnell(feldname, ids, ersetzen = false) {
    const z = aktuell();
    const r = await api.post(vapfad(`/schnell${z ? `/${z.id}` : ''}`), { feld: feldname, ids, ersetzen, datum: zr.von }, { quelle: f.id });
    if (r.hinweis) hinweis(r.hinweis, 'info');
    await laden();
    if (r.id) tabelle.waehle(r.id);
  }
  async function gruppenEintraege(feldname) {
    const z = aktuell();
    const gruppen = await api.get(vapfad(`/gruppen?feld=${feldname}${z ? `&va=${z.id}` : ''}`));
    const e = gruppen.filter((g) => !g.bereich).map((g) => ({ text: `${g.name}  (${g.anzahl})`, aktion: () => schnell(feldname, g.ids) }));
    const bereiche = [...new Set(gruppen.filter((g) => g.bereich).map((g) => g.bereich))];
    for (const b of bereiche) e.push({ text: `Nach ${b}`, menue: gruppen.filter((g) => g.bereich === b).map((g) => ({ text: `${g.name}  (${g.anzahl})`, aktion: () => schnell(feldname, g.ids) })) });
    return e.length ? e : [{ text: '(keine Elemente)', deaktiviert: true }];
  }
  async function schnellMenue(feldname, x, y) {
    if (!schreiben) return;
    const z = aktuell();
    const eintraege = [];
    if (feldname) {
      eintraege.push(...await gruppenEintraege(feldname), null, { text: `${FELDER.find((q) => q[0] === feldname)[1]} leeren`, deaktiviert: !z, aktion: () => schnell(feldname, [], true) });
    } else {
      for (const [k, text, sym] of FELDER) eintraege.push({ text, symbol: sym, menue: await gruppenEintraege(k) });
    }
    eintraege.push(null, { text: 'Alle aus den Stunden der Veranstaltung übernehmen', symbol: 'unterricht', deaktiviert: !z, aktion: ausStunden });
    eintraege.push({ text: letzteStunde ? `Alle aus gewählter Stunde übernehmen (U-Nr. ${letzteStunde.u})` : 'Alle aus gewählter Stunde übernehmen (zuerst eine Stunde wählen)', deaktiviert: !letzteStunde, aktion: ausStunde });
    menue(x, y, eintraege);
  }
  async function ausStunden() {
    const z = aktuell();
    if (!z) { hinweis('Bitte zuerst eine Veranstaltung wählen.', 'warnung'); return; }
    const r = await api.post(vapfad(`/${z.id}/ausstunden`), {}, { quelle: f.id });
    if (r.hinweis) hinweis(r.hinweis, 'info');
    await laden();
  }
  async function ausStunde() {
    if (!letzteStunde) return;
    const r = await api.post(vapfad('/ausstunde'), { unterricht: letzteStunde.u, datum: letzteStunde.datum, stunde: letzteStunde.stunde, va: aktuell()?.id }, { quelle: f.id });
    if (r.hinweis) hinweis(r.hinweis, 'info');
    await laden();
    if (r.id) tabelle.waehle(r.id);
  }
  async function loeschen() {
    const ids = tabelle.ausgewaehlte().map((z) => z.id);
    if (!ids.length || !(await frage(`${ids.length} Veranstaltung(en) löschen?`, { gefahr: true, ja: 'Löschen', nein: 'Abbrechen' }))) return;
    await api.post(vapfad('/loeschen'), { ids }, { quelle: f.id });
    await laden();
  }

  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'neu', titel: 'Neue Veranstaltung (Dialog)', aktion: async () => {
      const w = await formular('Neue Veranstaltung', [
        { name: 'T', titel: 'Text', wert: 'Exkursion' }, { name: 'K', titel: 'Klassen', hinweis: 'Kurznamen, „Alle Klassen“, „Alle Klassen außer 5a“' }, { name: 'L', titel: 'Lehrkräfte' }, { name: 'R', titel: 'Räume' },
        { name: 'V', titel: 'Von', wert: datumText(zr.von) }, { name: 'SV', titel: 'ab Stunde', art: 'zahl', wert: 1 }, { name: 'B', titel: 'Bis', wert: datumText(zr.von) }, { name: 'SB', titel: 'bis Stunde', art: 'zahl', wert: zustand.grunddaten?.zeitraster?.stunden?.length || 8 },
      ]);
      if (!w) return;
      const r = await api.post(vapfad(''), { feld: 'T', wert: w.T || 'Veranstaltung', datum: datumLesen(w.V, zr.von) }, { quelle: f.id });
      for (const k of ['B', 'SV', 'SB', 'K', 'L', 'R']) if (w[k]) await api.post(vapfad(`/${r.id}`), { feld: k, wert: k === 'B' ? datumText(datumLesen(w[k], zr.von)) : w[k] }, { quelle: f.id });
      await laden();
      tabelle.waehle(r.id);
    } });
    f.werkzeug({ text: 'Löschen', symbol: 'loeschen', nurSymbol: true, titel: 'Löschen (Entf)', aktion: loeschen });
    f.trenner();
    f.werkzeug({ text: 'Kopieren', symbol: 'kopieren', nurSymbol: true, titel: 'Veranstaltung kopieren', aktion: () => { kopie = aktuell()?.id; if (kopie) hinweis(`Veranstaltung ${kopie} kopiert – „Einfügen“ legt sie am eingestellten Datum an.`, 'info'); } });
    f.werkzeug({ text: 'Einfügen', symbol: 'import', nurSymbol: true, titel: 'Veranstaltung einfügen – am eingestellten Datum', aktion: async () => {
      if (!kopie) { hinweis('Bitte zuerst eine Veranstaltung markieren und kopieren.', 'warnung'); return; }
      const r = await api.post(vapfad(`/${kopie}/kopieren`), { datum: zr.von }, { quelle: f.id });
      await laden();
      tabelle.waehle(r.id);
    } });
    f.trenner();
    const hinzu = f.werkzeug({ text: 'Hinzufügen', symbol: 'plus', titel: 'Schnell hinzufügen: alle Lehrkräfte, Klassen, Räume, nach Abteilung/Jahrgang/Gruppe oder alle aus Stunde/Unterricht', aktion: () => { const r = hinzu.getBoundingClientRect(); return schnellMenue(null, r.left, r.bottom + 2); } });
    f.werkzeug({ text: 'Aus den Stunden', symbol: 'unterricht', nurSymbol: true, titel: 'Alle aus den Stunden der Veranstaltung übernehmen (Klassen, Lehrkräfte, Räume)', aktion: ausStunden });
  }
  f.werkzeug({ text: 'Vertretungen', symbol: 'vertretung', nurSymbol: true, titel: 'Vertretungen', aktion: () => fensterverwaltung.oeffnen('vertretung.liste', {}) });

  const nrEl = h('input', { class: 'eingabe vt-nr', title: 'VA-Nr. eingeben und Enter: zur Veranstaltung springen' });
  f.fuss.append(h('span', null, 'VA-Nr. '), nrEl);
  nrEl.addEventListener('keydown', async (e) => {
    if (e.key !== 'Enter' || !Number(nrEl.value)) return;
    if (!tabelle.waehle(Number(nrEl.value))) hinweis(`Die Veranstaltung Nr. ${nrEl.value} liegt nicht im gewählten Zeitraum.`, 'warnung');
  });

  let ladeNr = 0;
  async function laden() {
    if (!zr.von) return;
    const n = ++ladeNr;
    const d = await api.get(vapfad(`?von=${zr.von}&bis=${zr.bis}`));
    if (n !== ladeNr) return;
    tabelle.daten(d.zeilen.map((z) => ({ ...z, k: z.k.replace(/\n/g, ' '), kt: z.kt.replace(/\n/g, ' '), l: z.l.replace(/\n/g, ' '), r: z.r.replace(/\n/g, ' ') })));
  }
  f.an('vertretung.auswahl', (a) => { if (a?.zeile?.u && a.zeile.datum) letzteStunde = { u: a.zeile.u, datum: a.zeile.datum, stunde: a.zeile.stunde }; });
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stammdaten')) laden().catch(() => {}); });
  laden().catch(fehlerMelden);
  void planungsdatum;
}

export default {
  id: 'veranstaltungen',
  fenster: {
    'veranstaltungen.liste': { titel: 'Veranst. / Lehrer', symbol: 'veranstaltung', breite: 1000, hoehe: 340, einzig: () => 'veranstaltungen', erstellen: veranstaltungenFenster },
  },
  befehle: {
    'veranstaltungen.oeffnen': () => {
      if (!zustand.dokument) return null;
      if (zustand.modus !== 'vertretung') { modusSetzen('vertretung'); try { localStorage.setItem('studio.modus', 'vertretung'); } catch { /* privat */ } }
      return fensterverwaltung.oeffnen('veranstaltungen.liste', {});
    },
  },
  ribbon: [
    { id: 'vertretung.veranstaltungen', reiter: 'Vertretungsplanung', gruppe: 'Ereignisse', text: 'Veranstaltungen', symbol: 'veranstaltung', braucht: 'dokument', befehl: 'veranstaltungen.oeffnen' },
  ],
};
