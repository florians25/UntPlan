// Modul „Unterricht“: Unterrichtsfenster je Klasse, Lehrkraft, Fach oder alle Unterrichte – Spalten und Eingabe wie in der
// Desktop-Anwendung (U-Nr, Kl,Le, Nvpl.Std., Wst, Fach, Lehrer, Klasse(n), Fachraum, Stammraum, Dopp.Std., Block, Text …).
// Kopplungszeilen erscheinen als eigene Zeilen unter dem Unterricht.
import { h, leeren } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, elemente, darfSchreiben } from '../kern/zustand.js';
import { betrifft, melden } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { frage, hinweis, laden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';

const SICHTEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', fach: 'Fach', alle: 'Alle Unterrichte' };

/** Element-Auswahl mit Blättern (◂ ▸) für Fenster, die zu einem Element gehören (Unterricht, Stundenplan …). */
export async function elementAuswahl(f, typ, gewaehlt, beiWahl) {
  const liste = await elemente(typ);
  const sel = h('select', { class: 'eingabe', style: { minWidth: '150px' }, title: 'Element wählen' },
    liste.map((e) => h('option', { value: e.id, selected: e.id === gewaehlt }, e.l ? `${e.k} – ${e.l}` : e.k)));
  const wahl = (id) => { sel.value = String(id); beiWahl(Number(id)); };
  sel.addEventListener('change', () => wahl(sel.value));
  const blaettern = (d) => {
    const i = liste.findIndex((e) => String(e.id) === sel.value);
    const n = liste[Math.max(0, Math.min(liste.length - 1, i + d))];
    if (n) wahl(n.id);
  };
  const zurueck = h('button', { class: 'wz-knopf', type: 'button', title: 'Vorheriges Element', onclick: () => blaettern(-1) }, '◂');
  const vor = h('button', { class: 'wz-knopf', type: 'button', title: 'Nächstes Element', onclick: () => blaettern(1) }, '▸');
  f.werkzeuge.prepend(zurueck, sel, vor, h('span', { class: 'trenner' }));
  return {
    liste,
    setzen: (id) => { sel.value = String(id); },
    async neuLaden() {
      const neu = await elemente(typ);
      const wert = sel.value;
      leeren(sel).append(...neu.map((e) => h('option', { value: e.id }, e.l ? `${e.k} – ${e.l}` : e.k)));
      sel.value = wert;
    },
  };
}

function unterrichtFenster(f, p) {
  const sicht = SICHTEN[p.sicht] ? p.sicht : 'alle';
  let element = p.element ?? null;
  let alle = [];          // Unterrichte (vom Server)
  let verknuepft = true;
  const schreiben = darfSchreiben();

  const flach = () => alle.flatMap((u) => u.zeilen.map((werte, i) => ({ id: `${u.id}:${i}`, uid: u.id, zeile: i, erste: i === 0, anzahl: u.zeilen.length, werte, nvpl: u.nvpl, wst: u.wst })));

  const tabelle = new Tabelle(f.inhalt, {
    spalten: [],
    zustandSchluessel: 'tabelle.unterricht',
    gruppiert: (z) => z.uid,
    zeilenKlasse: (z) => (z.erste ? 'gruppe-anfang' : 'gruppe-folge'),
    leerText: sicht === 'alle' ? 'Kein Unterricht vorhanden' : 'Kein Unterricht – in der letzten Zeile neuen Unterricht eintragen',
    neuText: schreiben ? 'Neuer Unterricht …' : undefined,
    neu: schreiben ? async (s, wert) => {
      const u = await api.post(api.dok('/unterricht'), { sicht, element, werte: { [s.schluessel]: wert } }, { quelle: f.id });
      alle.push(u);
      anzeigen();
      tabelle.waehle(`${u.id}:0`, s.schluessel);
      hinweis(`Neuer Unterricht U-Nr. ${u.id} angelegt – Strg+Z macht die Anlage rückgängig.`, 'ok');
      return null;
    } : undefined,
    aendern: schreiben ? async (z, s, wert) => {
      ersetzen(await api.patch(api.dok(`/unterricht/${z.uid}`), { feld: s.schluessel, wert, zeile: z.zeile }, { quelle: f.id }));
    } : undefined,
    optionen: async (s) => (s.refTyp === 'raum' ? [...await elemente('raum'), ...await elemente('raumgruppe')] : elemente(s.refTyp)),
    beiAuswahl: (zeilen, aktiv) => { fussZeigen(); if (aktiv) melden('unterricht', { id: aktiv.uid, quelle: f.id }); },
    beiEntf: () => loeschen(),
    kontextmenue: () => [
      { text: 'Kopplungszeile hinzufügen', symbol: 'zeile_neu', aktion: zeileNeu, deaktiviert: !schreiben },
      { text: 'Kopplungszeile löschen', symbol: 'zeile_loeschen', aktion: zeileLoeschen, deaktiviert: !schreiben },
      { text: 'Unterricht kopieren', symbol: 'kopieren', aktion: kopieren, deaktiviert: !schreiben },
      { text: 'Unterricht löschen', symbol: 'loeschen', aktion: loeschen, deaktiviert: !schreiben },
    ],
  });

  function ersetzen(u) {
    const i = alle.findIndex((x) => x.id === u.id);
    if (i >= 0) alle[i] = u; else alle.push(u);
    anzeigen();
  }

  function anzeigen() {
    tabelle.daten(flach());
    fussZeigen();
  }

  function fussZeigen() {
    const wst = alle.reduce((a, u) => a + u.wst, 0), nv = alle.reduce((a, u) => a + u.nvpl, 0);
    f.fuss.replaceChildren(h('span', null, `${alle.length} Unterricht(e)`), h('span', null, `Wst: ${wst}`), h('span', null, `Nvpl.: ${nv}`),
      h('span', { class: 'rechts' }, schreiben ? 'Mehrere Klassen durch Komma trennen · weitere Lehrkraft = Kopplungszeile' : 'nur lesen'));
  }

  async function laden_() {
    if (sicht !== 'alle' && !element) { alle = []; anzeigen(); return; }
    const weg = laden(f.inhalt);
    try {
      const q = sicht === 'alle' ? '' : `?sicht=${sicht}&element=${element}`;
      const d = await api.get(api.dok(`/unterricht${q}`));
      f.titel(`${d.titel} / Unterricht`);
      if (!tabelle.spalten.length) {
        tabelle.spaltenSetzen(d.spalten.map((s, i) => ({
          schluessel: s.schluessel, titel: s.schluessel === 'KlLe' ? '⊞ Kl,Le' : s.titel, breite: s.breite, standard: s.standard, karteikarte: s.karte,
          art: s.bool ? 'Bool' : s.refTyp ? (s.liste ? 'ElementListe' : 'Element') : s.editierbar ? (s.schluessel === 'Wst' ? 'Ganzzahl' : 'Text') : 'NurLesen',
          ausrichtung: ['UNr', 'Nvpl', 'Wst', 'Jst', 'Wert'].includes(s.schluessel) ? 'rechts' : undefined,
          refTyp: s.refTyp, editierbar: s.editierbar && schreiben,
          editierbarFuer: (z) => s.editierbar && schreiben && (s.zeile || z.erste),
          wert: (z) => (s.zeile || z.erste ? z.werte[i] : ''),
          farbe: s.schluessel === 'Nvpl' ? (z) => (z.erste && z.nvpl > 0 ? '#fde7c4' : null) : undefined,
        })));
      }
      alle = d.unterrichte;
      anzeigen();
    } finally { weg(); }
  }

  const aktuell = () => tabelle.aktiveZeile();

  async function zeileNeu() {
    const z = aktuell();
    if (!z) return hinweis('Bitte zuerst den Unterricht wählen.', 'warnung');
    const u = await api.post(api.dok(`/unterricht/${z.uid}/zeilen`), {}, { quelle: f.id });
    ersetzen(u);
    tabelle.waehle(`${u.id}:${u.zeilen.length - 1}`, 'Lehrer');
    hinweis(`Kopplungszeile zu U-Nr. ${u.id} angelegt – jetzt die Lehrkraft eintragen.`, 'ok');
  }

  async function zeileLoeschen() {
    const z = aktuell();
    if (!z || z.anzahl <= 1) return hinweis('Bitte eine Kopplungszeile eines gekoppelten Unterrichts wählen.', 'warnung');
    ersetzen(await api.loeschen(api.dok(`/unterricht/${z.uid}/zeilen/${z.zeile}`), { quelle: f.id }));
  }

  async function kopieren() {
    const z = aktuell();
    if (!z) return;
    const u = await api.post(api.dok(`/unterricht/${z.uid}/kopieren`), {}, { quelle: f.id });
    alle.push(u);
    anzeigen();
    tabelle.waehle(`${u.id}:0`);
  }

  async function loeschen() {
    const ids = [...new Set(tabelle.ausgewaehlte().map((z) => z.uid))];
    if (!ids.length) return;
    if (!(await frage(`${ids.length} Unterricht(e) samt verplanter Stunden löschen?`, { ja: 'Löschen', gefahr: true }))) return;
    await api.post(api.dok('/unterricht/loeschen'), { ids }, { quelle: f.id });
    alle = alle.filter((u) => !ids.includes(u.id));
    anzeigen();
    hinweis(`${ids.length} Unterricht(e) gelöscht – Strg+Z macht das Löschen rückgängig.`, 'ok');
  }

  // ---- Werkzeugleiste ----
  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'plus', titel: 'Neuer Unterricht', aktion: () => { tabelle.waehle('__neu', 'Fach'); tabelle.fokus(); tabelle.bearbeiten(); } });
    f.werkzeug({ text: 'Kopplung', symbol: 'zeile_neu', titel: 'Kopplungszeile hinzufügen (weitere Lehrkraft/Gruppe)', aktion: zeileNeu });
    f.werkzeug({ text: '', symbol: 'zeile_loeschen', titel: 'Kopplungszeile löschen', aktion: zeileLoeschen, nurSymbol: true });
    f.werkzeug({ text: '', symbol: 'kopieren', titel: 'Unterricht kopieren', aktion: kopieren, nurSymbol: true });
    f.werkzeug({ text: '', symbol: 'loeschen', titel: 'Unterricht löschen', aktion: loeschen, nurSymbol: true });
    f.trenner();
  }
  f.werkzeug({ text: 'Spalten', symbol: 'spalten', titel: 'Spalten ein-/ausblenden', aktion: () => { const r = f.werkzeuge.getBoundingClientRect(); tabelle.spaltenMenue(r.left + 260, r.bottom); } });
  if (sicht !== 'alle') {
    f.werkzeug({ text: 'Stundenplan', symbol: 'stundenplan', aktion: () => fensterverwaltung.oeffnen('stundenplan', { typ: sicht, element }) });
    const vk = f.werkzeug({ text: '', symbol: 'verknuepfen', titel: 'Fensterverknüpfung: folgt der Auswahl in anderen Fenstern', nurSymbol: true, aktion: () => { verknuepft = !verknuepft; vk.classList.toggle('gedrueckt', verknuepft); } });
    vk.classList.add('gedrueckt');
  }

  let auswahl = null;
  const elementSetzen = (id) => { element = id; f.parameterSetzen({ element }); auswahl?.setzen(id); laden_(); };
  (async () => {
    if (sicht !== 'alle') {
      auswahl = await elementAuswahl(f, sicht, element, elementSetzen);
      if (!element && auswahl.liste.length) { element = auswahl.liste[0].id; auswahl.setzen(element); }
    }
    await laden_();
  })().catch((e) => hinweis(e.message, 'fehler'));

  f.an('daten', (e) => {
    if (e.quelle === f.id) return;
    if (betrifft(e, 'Stammdaten')) auswahl?.neuLaden();
    if (betrifft(e, 'Unterricht', 'Stundenplan', 'Stammdaten')) laden_();
  });
  f.an('element', (e) => { if (verknuepft && e.quelle !== f.id && e.typ === sicht && e.id !== element) elementSetzen(e.id); });
}

const oeffnen = ({ sicht = 'klasse', element } = {}) => (zustand.dokument ? fensterverwaltung.oeffnen('unterricht', { sicht, element }) : null);

const e = (id, text, sicht, gross = true) => ({ id, reiter: 'Dateneingabe', gruppe: 'Unterricht', text, symbol: 'unterricht', gross, befehl: 'unterricht.oeffnen', args: { sicht }, braucht: 'dokument' });

export default {
  id: 'unterricht',
  fenster: {
    unterricht: {
      titel: 'Unterricht', symbol: 'unterricht', breite: 980, hoehe: 480,
      einzig: (p) => p.sicht || 'alle',
      erneutGeoeffnet: () => {},
      erstellen: unterrichtFenster,
    },
  },
  befehle: { 'unterricht.oeffnen': oeffnen },
  ribbon: [
    e('eingabe.unterricht.klassen', 'Klassen', 'klasse'),
    e('eingabe.unterricht.lehrer', 'Lehrer', 'lehrer'),
    e('eingabe.unterricht.faecher', 'Fächer', 'fach', false),
    e('eingabe.unterricht.alle', 'Alle Unterrichte', 'alle', false),
  ],
};
