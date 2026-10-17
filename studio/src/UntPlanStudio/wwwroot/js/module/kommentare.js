// Modul „Kommentare“ (Desktop: Kommentar-Dialog an Stammdaten und „Kommentar-Übersicht“): Übersicht aller Kommentare mit
// Kategorie (Farbe), Element und Feld; Anlegen (Element wählen), Bearbeiten, Löschen. Befehl 'kommentare.neu' für andere Module.
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben, elemente } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { dialog, frage, hinweis, fehlerMelden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';

const TYPEN = [['lehrer', 'Lehrkraft'], ['klasse', 'Klasse'], ['raum', 'Raum'], ['fach', 'Fach'], ['schueler', 'Schüler'], ['gang', 'Gang'], ['abteilung', 'Abteilung']];

/** Kommentar-Dialog; vorgabe: { typ, element, feld, kategorie, text } – liefert die Eingabe oder null. */
async function kommentarDialog(vorgabe = {}, titel = 'Kommentar') {
  const kategorien = await api.get(api.dok('/kommentare/kategorien'));
  const typ = h('select', { class: 'eingabe', disabled: !!vorgabe.id }, TYPEN.map(([w, t]) => h('option', { value: w, selected: w === (vorgabe.typ || 'lehrer') }, t)));
  const element = h('select', { class: 'eingabe', disabled: !!vorgabe.id });
  const feld = h('input', { class: 'eingabe', value: vorgabe.feld || '', placeholder: 'optional, z. B. Wochensoll' });
  const kat = h('select', { class: 'eingabe' }, kategorien.map((k) => h('option', { value: k.nr, selected: k.nr === (vorgabe.kategorie || 1), style: k.farbe ? { background: k.farbe } : null }, k.name)));
  const text = h('textarea', { class: 'eingabe', rows: 6 }, vorgabe.text || '');
  const fuellen = async () => {
    const liste = await elemente(typ.value);
    element.replaceChildren(...liste.map((e) => h('option', { value: e.id, selected: e.id === vorgabe.element }, e.l ? `${e.k} – ${e.l}` : e.k)));
  };
  typ.addEventListener('change', fuellen);
  await fuellen();
  const ok = await dialog({
    titel, breite: '520px',
    inhalt: h('div', { class: 'formular' }, h('label', null, 'Element'), h('div', { class: 'km-zeile' }, typ, element), h('label', null, 'Feld'), feld, h('label', null, 'Kategorie'), kat, h('label', null, 'Kommentar'), text),
    knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }],
    pruefen: () => { text.classList.toggle('ungueltig', !text.value.trim()); return !!text.value.trim() && !!element.value; },
  });
  return ok ? { typ: typ.value, element: Number(element.value), feld: feld.value, kategorie: Number(kat.value), text: text.value } : null;
}

async function kommentarNeu(vorgabe = {}) {
  if (!zustand.dokument) return null;
  const e = await kommentarDialog(vorgabe, 'Kommentar anlegen');
  if (!e) return null;
  const liste = await api.post(api.dok('/kommentare'), e);
  hinweis('Kommentar angelegt.', 'ok', 2000);
  return liste;
}

function uebersicht(f) {
  f.titel('Kommentar-Übersicht');
  const schreiben = darfSchreiben();
  let alle = [];
  const tabelle = new Tabelle(f.inhalt, {
    zustandSchluessel: 'tabelle.kommentare',
    leerText: 'Keine Kommentare vorhanden',
    beiDoppelklick: (z) => bearbeiten(z),
    beiEntf: () => loeschen(),
    spalten: [
      { schluessel: 'kategorieName', titel: 'Kategorie', breite: 110, farbe: (z) => z.farbe || null },
      { schluessel: 'typName', titel: 'Element', breite: 90 }, { schluessel: 'elementName', titel: 'Name', breite: 90 }, { schluessel: 'feld', titel: 'Feld', breite: 100 },
      { schluessel: 'text', titel: 'Kommentar', breite: 320 }, { schluessel: 'erstellt', titel: 'Erstellt', breite: 120 },
    ].map((s) => ({ ...s, art: 'NurLesen', editierbar: false })),
    kontextmenue: (zeilen) => [
      { text: 'Neu …', symbol: 'plus', aktion: neu, deaktiviert: !schreiben },
      { text: 'Bearbeiten …', symbol: 'einstellungen', aktion: () => bearbeiten(zeilen[0]), deaktiviert: !zeilen.length || !schreiben },
      { text: 'Löschen', symbol: 'loeschen', aktion: loeschen, deaktiviert: !zeilen.length || !schreiben },
      null,
      { text: 'Element öffnen', symbol: 'stammdaten', aktion: () => fensterverwaltung.oeffnen('stammdaten', { typ: zeilen[0]?.typ, element: zeilen[0]?.element }), deaktiviert: !zeilen.length },
    ],
  });
  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'plus', aktion: neu });
    f.werkzeug({ text: 'Bearbeiten', symbol: 'einstellungen', aktion: () => bearbeiten(tabelle.aktiveZeile()) });
    f.werkzeug({ text: 'Löschen', symbol: 'loeschen', aktion: loeschen });
  }
  const anzeigen = (liste) => { alle = liste; tabelle.daten(alle); f.fuss.replaceChildren(h('span', null, `${alle.length} Kommentare`)); };
  async function neu() { const l = await kommentarNeu(); if (l) anzeigen(l); }
  async function bearbeiten(z) {
    if (!z || !schreiben) return;
    const e = await kommentarDialog(z, 'Kommentar bearbeiten');
    if (e) anzeigen(await api.put(api.dok(`/kommentare/${z.id}`), e, { quelle: f.id }));
  }
  async function loeschen() {
    const ids = tabelle.ausgewaehlte().map((z) => z.id);
    if (!ids.length || !schreiben || !(await frage(`${ids.length} Kommentar(e) löschen?`, { ja: 'Löschen', gefahr: true }))) return;
    anzeigen(await api.post(api.dok('/kommentare/loeschen'), { ids }, { quelle: f.id }));
  }
  const laden_ = () => api.get(api.dok('/kommentare')).then(anzeigen).catch(fehlerMelden);
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Stammdaten')) laden_(); });
  laden_();
}

export default {
  id: 'kommentare',
  fenster: { kommentare: { titel: 'Kommentar-Übersicht', symbol: 'info', breite: 860, hoehe: 360, einzig: () => 'komm', erstellen: uebersicht } },
  befehle: {
    'kommentare.oeffnen': () => { if (zustand.dokument) fensterverwaltung.oeffnen('kommentare', {}); },
    'kommentare.neu': (a) => kommentarNeu(a),
  },
  ribbon: [
    { id: 'eingabe.kommentare', reiter: 'Dateneingabe', gruppe: 'Hilfsmittel', text: 'Kommentare', symbol: 'info', befehl: 'kommentare.oeffnen', braucht: 'dokument', hinweis: 'Kommentar-Übersicht' },
  ],
};
