// Fenster „Absenzen“ wie im Desktop: Reiter Lehrer / Klassen / Räume mit Anzahl, Zeitraum, direkte Eingabe in der Liste
// (AbsNr., Element, Von, ab St., Bis, bis St., Grund, Text, Begleitlehrer, offen). Änderungen einer Zeile werden erst beim Verlassen
// der Zeile übernommen (Zeilen-Commit, Strg+Enter sofort, Esc verwirft); neue Absenzen: Planungsdatum, 1. bis letzte Stunde.
import { h, entprellen } from '../../kern/dom.js';
import { api, einstellungLesen, einstellungSchreiben } from '../../kern/api.js';
import { elemente, darfSchreiben } from '../../kern/zustand.js';
import { betrifft } from '../../kern/ereignisse.js';
import { Tabelle } from '../../kern/tabelle.js';
import { frage, meldung, hinweis, fehlerMelden, formular, menue } from '../../kern/dialog.js';
import { fensterverwaltung } from '../../kern/fenster.js';
import { vpfad, tagText, datumLesen, planungsdatum, datumText } from './kern.js';
import { zeitraumAuswahl, reiter } from './steuer.js';

const ARTEN = [{ art: 'lehrer', titel: 'Lehrer', typ: 'lehrer' }, { art: 'klasse', titel: 'Klassen', typ: 'klasse' }, { art: 'raum', titel: 'Räume', typ: 'raum' }];

/** Absenzen speichern und die Rückfragen des Desktops stellen (Vertreter abwesend, Veranstaltung, Verlegungen, Sondereinsätze). */
export async function absenzenSpeichern(art, neu, alt, quelle, text) {
  const r = await api.post(vpfad('/absenzen/speichern'), { art, neu, alt, text }, { quelle });
  if (r.doppelt) hinweis('Diese Absenz ist bereits vorhanden.', 'warnung');
  if (r.betroffen?.length) {
    await meldung(`Achtung: Folgende Vertretungen waren bereits mit einer Lehrkraft geplant, die nun abwesend ist. Sie sind wieder offen und müssen neu vertreten werden:\n\n${r.betroffen.join('\n')}`, 'Vertreter abwesend');
  }
  for (const n of r.nachfragen || []) {
    if (!(await frage(n.text, { titel: 'UntPlan - Frage' }))) continue;
    await api.post(vpfad('/absenzen/nachfrage'), n, { quelle });
    if (n.art === 'veranstaltung' && n.hinweis) await meldung(n.hinweis, 'UntPlan - Hinweis');
  }
  return r;
}

function absenzFenster(f, p) {
  const schreiben = darfSchreiben();
  let artIndex = Math.max(0, ARTEN.findIndex((a) => a.art === p.art));
  let daten = null;
  let entwurf = null; // { id, zeile, neu, bisGesetzt }
  let markiert = null; // { typ, id }
  let vaZeigen = false;
  const art = () => ARTEN[artIndex];

  const tabs = reiter(ARTEN.map((a) => `${a.titel} (0)`), async (i) => {
    await entwurfUebernehmen();
    artIndex = i;
    f.parameterSetzen({ art: art().art });
    f.titel(`Absenzen / ${art().titel === 'Lehrer' ? 'Lehrer' : art().titel === 'Klassen' ? 'Klasse' : 'Raum'}`);
    spalten();
    laden();
  }, artIndex);
  const zr = zeitraumAuswahl(f, { art: p.zeitraum || 'tag', beiAenderung: async () => { await entwurfUebernehmen(); f.parameterSetzen({ zeitraum: zr.art }); laden(); } });
  f.inhalt.append(h('div', { class: 'vt-kopf' }, h('span', { class: 'vt-gruppe' }, h('span', { class: 'vt-gtitel' }, 'Von-Bis'), zr.el)), tabs.el);

  const letzte = () => daten?.letzteStunde || 8;
  const tabelle = new Tabelle(f.inhalt, {
    spalten: [],
    sortierbar: false,
    leerText: 'Keine Absenzen',
    neuText: schreiben ? 'Neue Absenz …' : undefined,
    neu: schreiben ? async (s, wert) => {
      await entwurfUebernehmen();
      const z = { id: `neu${Date.now()}`, nr: 'neu *', element: '', vonIso: zr.von, bisIso: zr.von, sv: '1', sb: String(letzte()), grund: '', text: '', begleit: '', offen: '', entwurf: true };
      entwurf = { id: z.id, neu: true, bisGesetzt: false, zeile: z };
      return anwenden(z, s.schluessel, wert);
    } : undefined,
    aendern: schreiben ? async (z, s, wert) => {
      if (z.va) return null;
      if (entwurf && entwurf.id !== z.id) await entwurfUebernehmen();
      if (!entwurf) entwurf = { id: z.id, neu: false, bisGesetzt: true, zeile: z };
      return anwenden({ ...entwurf.zeile }, s.schluessel, wert);
    } : undefined,
    optionen: async (s) => (s.refTyp === 'abwesenheitsgrund' ? elemente('abwesenheitsgrund') : elemente(s.refTyp)),
    zeilenKlasse: (z) => [z.va ? 'vt-va' : '', z.entwurf ? 'vt-entwurf' : '', markiert && !z.va && z.elementId === markiert.id && markiert.typ === art().typ ? 'vt-markiert' : ''].filter(Boolean).join(' '),
    beiAuswahl: (_, aktiv) => { if (entwurf && aktiv?.id !== entwurf.id) entwurfUebernehmen(); },
    beiDoppelklick: (z) => (z.va ? fensterverwaltung.oeffnen('veranstaltungen.liste', {}) : bearbeiten()),
    kontextmenue: () => [
      { text: 'Neue Absenz …', symbol: 'neu', deaktiviert: !schreiben, aktion: neuDialog },
      { text: 'Bearbeiten …', symbol: 'einstellungen', deaktiviert: !schreiben, aktion: bearbeiten },
      { text: 'Absenz teilen …', symbol: 'tausch', deaktiviert: !schreiben, aktion: teilen },
      { text: 'Absenz kopieren …', symbol: 'kopieren', deaktiviert: !schreiben, aktion: kopieren },
      { text: 'Absenzen zusammenfassen', deaktiviert: !schreiben, aktion: zusammenfassen },
      null,
      { text: 'Löschen', symbol: 'loeschen', deaktiviert: !schreiben, aktion: loeschen },
    ],
    beiEntf: () => loeschen(),
  });

  function spalten() {
    const t = art();
    tabelle.spaltenSetzen([
      { schluessel: 'nr', titel: 'AbsNr.', breite: 60, art: 'NurLesen' },
      { schluessel: 'element', titel: t.titel === 'Lehrer' ? 'Lehrer' : t.titel === 'Klassen' ? 'Klasse' : 'Raum', breite: 80, art: 'Element', refTyp: t.typ, editierbarFuer: (z) => !z.va },
      { schluessel: 'von', titel: 'Von TT.MM.', breite: 104, editierbarFuer: (z) => !z.va, wert: (z) => tagText(z.vonIso) },
      { schluessel: 'sv', titel: 'ab St.', breite: 50, art: 'Ganzzahl', editierbarFuer: (z) => !z.va },
      { schluessel: 'bis', titel: 'Bis TT.MM.', breite: 104, editierbarFuer: (z) => !z.va, wert: (z) => tagText(z.bisIso) },
      { schluessel: 'sb', titel: 'bis St.', breite: 50, art: 'Ganzzahl', editierbarFuer: (z) => !z.va },
      { schluessel: 'grund', titel: 'Grund', breite: 70, art: 'Element', refTyp: 'abwesenheitsgrund', editierbarFuer: (z) => !z.va },
      { schluessel: 'text', titel: 'Text', breite: 240, editierbarFuer: (z) => !z.va },
      ...(t.art === 'klasse' ? [{ schluessel: 'begleit', titel: 'Begleitlehrer', breite: 110, art: 'ElementListe', refTyp: 'lehrer', editierbarFuer: (z) => !z.va }] : []),
      { schluessel: 'offen', titel: 'offen', breite: 50, art: 'NurLesen', ausrichtung: 'rechts', klasse: (z) => (Number(z.offen) > 0 ? 'vt-rot' : null) },
    ]);
  }

  /** Eingabe in den Entwurf der Zeile übernehmen (gespeichert wird beim Zeilenwechsel). */
  function anwenden(z, feld, wert) {
    const w = String(wert ?? '').trim();
    const L = letzte();
    switch (feld) {
      case 'element': z.element = w.toUpperCase() === w ? w : w; break;
      case 'von': z.vonIso = datumLesen(w, z.vonIso); if (!entwurf.bisGesetzt || z.bisIso < z.vonIso) z.bisIso = z.vonIso; break;
      case 'bis': z.bisIso = datumLesen(w, z.bisIso); entwurf.bisGesetzt = true; if (z.bisIso < z.vonIso) z.vonIso = z.bisIso; break;
      case 'sv': z.sv = String(w ? Math.min(Math.max(1, Number(w) || 1), L) : 1); break;
      case 'sb': z.sb = String(w ? Math.min(Math.max(1, Number(w) || L), L) : L); break;
      case 'grund': z.grund = w; break;
      case 'text': z.text = w; break;
      case 'begleit': z.begleit = w; break;
      default: return null;
    }
    z.entwurf = true;
    z.nr = entwurf.neu ? 'neu *' : `${String(z.nr).replace(' *', '')} *`;
    entwurf.zeile = z;
    return z;
  }

  async function entwurfUebernehmen() {
    const e = entwurf;
    if (!e) return;
    entwurf = null;
    const z = e.zeile;
    if (!z.element) {
      if (!e.neu) hinweis('Ohne Element kann die Absenz nicht gespeichert werden – die Änderung wurde verworfen.', 'warnung');
      await laden();
      return;
    }
    try {
      await absenzenSpeichern(art().art, [{ id: e.neu ? 0 : Number(String(z.nr).replace(' *', '')), element: z.element, von: z.vonIso, bis: z.bisIso, sv: Number(z.sv), sb: Number(z.sb), grund: z.grund, text: z.text, begleit: z.begleit }],
        e.neu ? [] : [Number(String(z.nr).replace(' *', ''))], f.id, e.neu ? 'Absenz eingeben' : 'Absenz geändert');
    } catch (x) { fehlerMelden(x); }
    await laden();
  }

  const markierte = () => tabelle.ausgewaehlte().filter((z) => !z.va && !String(z.id).startsWith('neu')).map((z) => Number(String(z.nr).replace(' *', '')));

  async function formularAbsenz(titel, z) {
    const gruende = await elemente('abwesenheitsgrund');
    const w = await formular(titel, [
      { name: 'element', titel: art().titel === 'Lehrer' ? 'Lehrkraft' : art().titel === 'Klassen' ? 'Klasse' : 'Raum', wert: z?.element || '', pflicht: true },
      { name: 'von', titel: 'Von (TT.MM.JJJJ)', wert: datumText(z?.vonIso || zr.von) },
      { name: 'sv', titel: 'ab Stunde', art: 'zahl', wert: z?.sv || 1 },
      { name: 'bis', titel: 'Bis (TT.MM.JJJJ)', wert: datumText(z?.bisIso || zr.von) },
      { name: 'sb', titel: 'bis Stunde', art: 'zahl', wert: z?.sb || letzte() },
      { name: 'grund', titel: 'Grund', art: 'auswahl', wert: z?.grund || '', optionen: [{ wert: '', text: '' }, ...gruende.map((g) => ({ wert: g.k, text: g.l ? `${g.k} – ${g.l}` : g.k }))] },
      { name: 'text', titel: 'Text', wert: z?.text || '' },
      ...(art().art === 'klasse' ? [{ name: 'begleit', titel: 'Begleitlehrer', wert: z?.begleit || '', hinweis: 'Kurznamen, gelten ebenfalls als abwesend (z. B. Klassenfahrt).' }] : []),
    ]);
    if (!w) return null;
    const von = datumLesen(w.von, zr.von);
    return { element: w.element.trim(), von, bis: datumLesen(w.bis, von), sv: Number(w.sv) || 1, sb: Number(w.sb) || letzte(), grund: w.grund, text: w.text, begleit: w.begleit || '' };
  }
  async function neuDialog() {
    await entwurfUebernehmen();
    const a = await formularAbsenz('Neue Absenz');
    if (a) { await absenzenSpeichern(art().art, [{ id: 0, ...a }], [], f.id, 'Absenz eingeben'); await laden(); }
  }
  async function bearbeiten() {
    await entwurfUebernehmen();
    const z = tabelle.aktiveZeile();
    if (!z || z.va) return;
    const a = await formularAbsenz(`Absenz ${z.nr} bearbeiten`, z);
    if (a) { await absenzenSpeichern(art().art, [{ id: Number(z.nr), ...a }], [Number(z.nr)], f.id, 'Absenz geändert'); await laden(); }
  }
  async function teilen() {
    await entwurfUebernehmen();
    const z = tabelle.aktiveZeile();
    if (!z || z.va) return;
    if (z.vonIso === z.bisIso) { hinweis('Eine eintägige Absenz kann nicht geteilt werden.', 'warnung'); return; }
    const w = await formular('Absenz teilen', [{ name: 'ab', titel: 'Zweiter Teil beginnt am', wert: datumText(z.vonIso === z.bisIso ? z.vonIso : (await import('./kern.js')).plusTage(z.vonIso, 1)), pflicht: true }]);
    if (!w) return;
    await api.post(vpfad('/absenzen/teilen'), { id: Number(z.nr), ab: datumLesen(w.ab, z.vonIso) }, { quelle: f.id });
    await laden();
  }
  async function kopieren() {
    await entwurfUebernehmen();
    const z = tabelle.aktiveZeile();
    if (!z || z.va) return;
    const w = await formular('Absenz für weitere Elemente übernehmen', [{ name: 'elemente', titel: art().titel, pflicht: true, hinweis: 'Kurznamen, mehrere mit Komma.' }]);
    if (!w) return;
    await api.post(vpfad('/absenzen/kopieren'), { id: Number(z.nr), elemente: w.elemente }, { quelle: f.id });
    await laden();
  }
  async function zusammenfassen() {
    await entwurfUebernehmen();
    await api.post(vpfad('/absenzen/zusammenfassen'), { ids: markierte() }, { quelle: f.id });
    await laden();
  }
  async function loeschen() {
    await entwurfUebernehmen();
    const ids = markierte();
    if (!ids.length || !(await frage(`${ids.length} Absenz(en) löschen? Daraus entstandene Vertretungen werden entfernt.`, { gefahr: true, ja: 'Löschen', nein: 'Abbrechen' }))) return;
    await api.post(vpfad('/absenzen/loeschen'), { ids }, { quelle: f.id });
    await laden();
  }

  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'neu', titel: 'Neue Absenz (Dialog)', aktion: neuDialog });
    f.werkzeug({ text: 'Löschen', symbol: 'loeschen', nurSymbol: true, titel: 'Löschen (Strg+Entf)', aktion: loeschen });
    f.trenner();
    f.werkzeug({ text: 'Bearbeiten', symbol: 'einstellungen', nurSymbol: true, titel: 'Bearbeiten', aktion: bearbeiten });
    f.werkzeug({ text: 'Teilen', symbol: 'tausch', nurSymbol: true, titel: 'Absenz teilen', aktion: teilen });
    f.werkzeug({ text: 'Kopieren', symbol: 'kopieren', nurSymbol: true, titel: 'Absenz kopieren (andere Elemente)', aktion: kopieren });
    f.trenner();
  }
  f.werkzeug({ text: 'Veranstaltungen', symbol: 'veranstaltung', nurSymbol: true, titel: 'Veranstaltungen', aktion: () => fensterverwaltung.oeffnen('veranstaltungen.liste', {}) });
  const einst = f.werkzeug({ text: 'Einstellungen', symbol: 'einstellungen', nurSymbol: true, titel: 'Einstellungen', aktion: () => {
    const r = einst.getBoundingClientRect();
    menue(r.left, r.bottom + 2, [{ text: 'Veranstaltungen anzeigen', haken: vaZeigen, aktion: async () => { vaZeigen = !vaZeigen; await einstellungSchreiben('vertretung.absenzen.va', vaZeigen); laden(); } }]);
  } });

  tabelle.el.addEventListener('keydown', (e) => {
    if (tabelle.editor) return;
    if (e.key === 'Escape' && entwurf) { e.preventDefault(); e.stopImmediatePropagation(); entwurf = null; laden(); }
    else if (e.key === 'Enter' && e.ctrlKey && entwurf) { e.preventDefault(); e.stopImmediatePropagation(); entwurfUebernehmen(); }
    else if (e.key === 'Delete' && e.ctrlKey) { e.preventDefault(); e.stopImmediatePropagation(); loeschen(); }
    else if (e.key === 'Insert' && schreiben) { e.preventDefault(); neuDialog(); }
  }, true);
  f.beimSchliessen(() => { entwurfUebernehmen(); });
  f.an('fenster-aktiv', (x) => { if (x !== f && entwurf) entwurfUebernehmen(); });

  let ladeNr = 0;
  async function laden() {
    if (entwurf || !zr.von) return;
    const n = ++ladeNr;
    const d = await api.get(vpfad(`/absenzen?art=${art().art}&von=${zr.von}&bis=${zr.bis}&veranstaltungen=${vaZeigen}`));
    if (n !== ladeNr || entwurf) return;
    daten = d;
    const zeilen = d.zeilen.map((z) => ({ ...z, vonIso: z.von, bisIso: z.bis, sv: String(z.sv), sb: String(z.sb), offen: z.offen ? String(z.offen) : '', nr: String(z.nr) }));
    for (const v of d.veranstaltungen) zeilen.push({ id: v.id, va: v.nr, nr: `VA ${v.nr}`, element: '', vonIso: v.von, bisIso: v.bis, sv: String(v.sv), sb: String(v.sb), grund: v.grund, text: `Veranstaltung: ${v.elemente} ${v.text}`.trim(), begleit: '', offen: '' });
    tabelle.daten(zeilen);
    tabs.texte([`Lehrer (${d.anzahl.lehrer})`, `Klassen (${d.anzahl.klasse})`, `Räume (${d.anzahl.raum})`]);
    f.fuss.replaceChildren(h('span', null, `${d.zeilen.length} Absenz(en)`), h('span', null, `${d.zeilen.reduce((a, z) => a + z.offen, 0)} offene Vertretungen`),
      h('span', { class: 'rechts leise' }, 'Zeile verlassen speichert · Strg+Enter sofort · Esc verwirft'));
  }
  const entprelltLaden = entprellen(() => laden().catch(() => {}), 150);

  // Fensterverknüpfung: Absenzen der gewählten Lehrkraft (Klasse, Raum) hervorheben
  function markieren(typ, id) {
    if (entwurf || tabelle.editor) return;
    markiert = { typ, id };
    const i = ARTEN.findIndex((a) => a.typ === typ);
    if (i >= 0 && i !== artIndex) { tabs.waehlen(i); return; }
    tabelle._neuZeichnen();
    const z = tabelle.zeilen.find((x) => x.elementId === id);
    if (z && !f.el.contains(document.activeElement)) tabelle.waehle(z.id);
  }
  f.an('vertretung.auswahl', (a) => {
    if (!a?.zeile || f.el.contains(document.activeElement)) return;
    if (art().art === 'klasse' && a.zeile.klasse) markieren('klasse', a.zeile.klasse);
    else if (a.zeile.lehrer) markieren('lehrer', a.zeile.lehrer);
  });
  f.an('element', (e) => { if (e.quelle !== f.id && ['lehrer', 'klasse', 'raum'].includes(e.typ)) markieren(e.typ, e.id); });
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stammdaten', 'Stundenplan')) entprelltLaden(); });
  f.an('vertretung.datum', () => { if (entwurf) entwurfUebernehmen(); });

  spalten();
  einstellungLesen('vertretung.absenzen.va').then((w) => { vaZeigen = !!w; }).finally(() => laden().catch(fehlerMelden));
  f.titel(`Absenzen / ${art().titel === 'Lehrer' ? 'Lehrer' : art().titel === 'Klassen' ? 'Klasse' : 'Raum'}`);
  void planungsdatum;
}

export const fenster = {
  'vertretung.absenzen': { titel: 'Absenzen', symbol: 'abwesenheit', breite: 820, hoehe: 340, einzig: () => 'absenzen', erstellen: absenzFenster },
};
