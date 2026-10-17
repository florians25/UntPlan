// Fenster „Kalender“ wie im Desktop: Kalenderwoche und Datum, Woche mit Kennzahlen (Offen, Abw. Kl., Freis., Bl. Räume, Raum-V.,
// Tagestexte, Koll. Lehrer/Räume – Klick wählt den Tag), Reiter Tagestexte (direkte Eingabe) und Kalendertag (Normaler Schultag,
// Feiertag, Unterrichtsfrei mit Grund, Nur diese Stunden unterrichtsfrei, Notiz). Dazu die Datumsauswahl im Menüband.
import { h, leeren } from '../../kern/dom.js';
import { api } from '../../kern/api.js';
import { an } from '../../kern/ereignisse.js';
import { zustand, elemente, darfSchreiben } from '../../kern/zustand.js';
import { betrifft } from '../../kern/ereignisse.js';
import { fehlerMelden, hinweis } from '../../kern/dialog.js';
import { symbol } from '../../kern/symbole.js';
import { vpfad, planungsdatum, datumSetzen, blaettern, tagKurz, datumText, datumLesen, montag, plusTage } from './kern.js';
import { reiter, listenTabelle } from './steuer.js';

/** Datumsauswahl im Menüband (Gruppe „Kalender“): ◂◂ ◂ [Mo 02.11.2026] ▸ ▸▸ – Strg+Bild↑ / Strg+Bild↓ blättern. */
export function datumsElement() {
  const text = h('span', { class: 'vt-rb-text' });
  const eingabe = h('input', { type: 'date', class: 'vt-rb-datum', title: 'Planungsdatum wählen' });
  const knopf = (t, titel, f) => h('button', { type: 'button', class: 'vt-rb-pfeil', title: titel, onclick: () => f().catch?.(fehlerMelden) }, t);
  const el = h('div', { class: 'vt-rb-datumsauswahl', title: 'Planungsdatum: ◂ ▸ blättern (Strg+Bild↑ / Strg+Bild↓), ◂◂ ▸▸ eine Woche' },
    h('div', { class: 'vt-rb-zeile' }, knopf('◂◂', 'Eine Woche zurück', () => blaettern(-1, true)), knopf('◂', 'Vorheriger Unterrichtstag (Strg+Bild↑)', () => blaettern(-1)),
      h('label', { class: 'vt-rb-mitte' }, symbol('kalender'), text, eingabe),
      knopf('▸', 'Nächster Unterrichtstag (Strg+Bild↓)', () => blaettern(1)), knopf('▸▸', 'Eine Woche weiter', () => blaettern(1, true))),
    h('div', { class: 'vt-rb-unter' }, 'Datum'));
  const zeigen = () => {
    const d = planungsdatum();
    text.textContent = d ? `${tagKurz(d)} ${datumText(d)}` : '–';
    eingabe.value = d || '';
    el.classList.toggle('aus', !zustand.dokument);
  };
  eingabe.addEventListener('change', () => { if (eingabe.value) datumSetzen(eingabe.value); });
  const ab = [an('vertretung.datum', zeigen), an('dokument', zeigen)];
  // beim Neuzeichnen des Menübands verschwindet das alte Element; Abmelden, sobald es nicht mehr im Dokument hängt
  const pruefen = setInterval(() => { if (!el.isConnected && el.dataset.gesehen) { ab.forEach((x) => x()); clearInterval(pruefen); } else if (el.isConnected) el.dataset.gesehen = '1'; }, 5000);
  zeigen();
  return el;
}

const TAGESTEXT_ARTEN = ['Alle', 'Klassen', 'Lehrer', 'Nur Lehrer'];

function kalenderFenster(f, p) {
  const schreiben = darfSchreiben();
  let daten = null;
  const kw = h('input', { class: 'eingabe vt-kw', type: 'number', min: 1, max: 53, title: 'Kalenderwoche' });
  const datum = h('input', { class: 'eingabe', type: 'date', title: 'Datum' });
  const typ = h('span', { class: 'leise' });
  f.inhalt.append(h('div', { class: 'vt-kopf' }, h('label', null, 'Kalenderwoche '), kw, datum, typ));
  kw.addEventListener('change', () => {
    const d = planungsdatum();
    if (!d || !kw.value) return;
    const diff = Number(kw.value) - daten.kw;
    if (diff) datumSetzen(montag(plusTage(d, diff * 7)));
  });
  datum.addEventListener('change', () => { if (datum.value) datumSetzen(datum.value); });

  const oben = h('div', { class: 'vt-kal-oben' });
  const unten = h('div', { class: 'vt-kal-unten' });
  f.inhalt.append(oben, unten);
  const zahl = (k) => (w) => (w[k] ? String(w[k]) : '');
  const rot = (k) => (w, td) => { if (w[k] > 0) td.classList.add('vt-rot'); };
  const woche = listenTabelle(oben, {
    spalten: [
      { titel: 'Datum', breite: 54, wert: (w) => w.text }, { titel: 'Tag', breite: 34, wert: (w) => w.tag },
      { titel: 'Offen', breite: 46, klasse: 'rechts', wert: zahl('offen'), zelle: rot('offen') }, { titel: 'Abw.Kl.', breite: 50, klasse: 'rechts', wert: zahl('abwKl') },
      { titel: 'Freis.', breite: 46, klasse: 'rechts', wert: zahl('freis') }, { titel: 'Bl.Räume', breite: 58, klasse: 'rechts', wert: zahl('blRaeume') },
      { titel: 'Raum-V.', breite: 52, klasse: 'rechts', wert: zahl('raumV') }, { titel: 'Tagestexte', breite: 66, klasse: 'rechts', wert: zahl('tagestexte') },
      { titel: 'Koll. Lehrer', breite: 72, klasse: 'rechts', wert: zahl('kollL'), zelle: rot('kollL') }, { titel: 'Koll. Räume', breite: 72, klasse: 'rechts', wert: zahl('kollR'), zelle: rot('kollR') },
      { titel: '', wert: (w) => w.frei },
    ],
    zeilenKlasse: (w) => `${w.unterrichtstag ? '' : 'vt-frei'} ${w.datum === planungsdatum() ? 'vt-heute' : ''}`,
    beiWahl: (w) => { if (w.datum !== planungsdatum()) datumSetzen(w.datum); },
  });

  // ---- Tagestexte ----
  const texteFlaeche = h('div', { class: 'vt-texte' });
  const tagFlaeche = h('div', { class: 'vt-kaltag' });
  const tabs = reiter(['Tagestexte (0)', 'Kalendertag'], (i) => { texteFlaeche.hidden = i !== 0; tagFlaeche.hidden = i !== 1; f.parameterSetzen({ reiter: i }); }, p.reiter ?? 0);
  unten.append(tabs.el, texteFlaeche, tagFlaeche);
  texteFlaeche.hidden = tabs.index !== 0;
  tagFlaeche.hidden = tabs.index !== 1;

  function textZeile(t) {
    const bis = h('input', { class: 'eingabe', value: t ? datumText(t.bis) : '', placeholder: datumText(planungsdatum()), title: 'Gültig bis', disabled: !schreiben });
    const text = h('input', { class: 'eingabe', value: t?.text || '', placeholder: t ? '' : 'Neuer Tagestext …', title: t?.formatiert ? 'Formatierter Tagestext (Formatierung bleibt erhalten, solange der Text nicht geändert wird)' : 'Tagestext', disabled: !schreiben });
    const portal = h('input', { type: 'checkbox', checked: t ? t.portal : true, title: 'Webportal', disabled: !schreiben });
    const art = h('select', { class: 'eingabe', disabled: !schreiben }, TAGESTEXT_ARTEN.map((a) => h('option', { value: a, selected: a === (t?.art || 'Alle') }, a)));
    const elem = h('input', { class: 'eingabe', value: t?.elemente || '', title: 'Elemente (Klassen bzw. Lehrkräfte, Kurznamen)', disabled: !schreiben });
    const loeschen = h('button', { type: 'button', class: 'wz-knopf', title: 'Tagestext löschen', disabled: !schreiben || !t }, symbol('loeschen'));
    const tr = h('tr', { class: t?.formatiert ? 'fett' : '' }, h('td', null, bis), h('td', null, text), h('td', { class: 'mitte' }, portal), h('td', null, art), h('td', null, elem), h('td', null, loeschen));
    tr.dataset.start = t?.start || '';
    for (const x of [bis, text, elem]) x.addEventListener('change', () => texteSpeichern());
    for (const x of [portal, art]) x.addEventListener('change', () => texteSpeichern());
    loeschen.addEventListener('click', () => { tr.remove(); texteSpeichern(); });
    tr.werte = () => ({ start: tr.dataset.start || null, bis: bis.value ? datumLesen(bis.value, planungsdatum()) : null, text: text.value.trim(), portal: portal.checked, art: art.value, elemente: elem.value });
    return tr;
  }
  const texteKoerper = h('tbody');
  texteFlaeche.append(h('table', { class: 'vt-eingabetabelle' }, h('thead', null, h('tr', null, ['Gültig bis', 'Tagestext', 'Webportal', 'Art', 'Elemente', ''].map((t) => h('th', null, t)))), texteKoerper));
  let speichertTexte = false;
  async function texteSpeichern() {
    if (speichertTexte) return;
    speichertTexte = true;
    try {
      const texte = [...texteKoerper.children].map((tr) => tr.werte()).filter((t) => t.text);
      daten = await api.put(vpfad('/tagestexte'), { datum: planungsdatum(), texte }, { quelle: f.id });
      anzeigen();
    } catch (e) { fehlerMelden(e); } finally { speichertTexte = false; }
  }

  // ---- Kalendertag ----
  const radio = (wert, text) => h('label', { class: 'vt-radio' }, h('input', { type: 'radio', name: `kt-${f.id}`, value: wert, disabled: !schreiben }), ` ${text}`);
  const rNormal = radio('normal', 'Normaler Schultag');
  const rFeiertag = radio('feiertag', 'Feiertag');
  const rFrei = radio('frei', 'Unterrichtsfrei');
  const rStunden = radio('stunden', 'Nur diese Stunden unterrichtsfrei');
  const grund = h('select', { class: 'eingabe', disabled: !schreiben });
  const stunden = h('input', { class: 'eingabe', placeholder: 'z. B. 6-8 oder 1,5', disabled: !schreiben });
  const notiz = h('input', { class: 'eingabe', placeholder: 'Notiz des Vertretungsplaners (wird nicht gedruckt)', disabled: !schreiben });
  const titel = h('div', { class: 'vt-kaltitel' });
  tagFlaeche.append(h('div', { class: 'vt-kalrahmen' }, titel, rNormal, rFeiertag, h('div', { class: 'vt-zeile' }, rFrei, grund), h('div', { class: 'vt-zeile' }, rStunden, stunden), h('div', { class: 'vt-zeile' }, h('label', null, 'Notiz'), notiz)));
  const artWert = () => tagFlaeche.querySelector(`input[name="kt-${f.id}"]:checked`)?.value || 'normal';
  async function kalendertagSpeichern() {
    daten = await api.post(vpfad('/kalendertag'), { datum: planungsdatum(), art: artWert(), grund: grund.value, stunden: stunden.value, notiz: notiz.value }, { quelle: f.id });
    anzeigen();
    hinweis('Kalendertag gespeichert.', 'ok', 1500);
  }
  tagFlaeche.addEventListener('change', (e) => { if (e.target.matches('input[type=radio], select')) kalendertagSpeichern().catch(fehlerMelden); });
  stunden.addEventListener('focus', () => { rStunden.firstChild.checked = true; });
  stunden.addEventListener('change', () => kalendertagSpeichern().catch(fehlerMelden));
  notiz.addEventListener('change', () => kalendertagSpeichern().catch(fehlerMelden));

  async function anzeigen() {
    if (!daten) return;
    kw.value = daten.kw;
    datum.value = daten.datum;
    typ.textContent = `${daten.wochentyp}-Woche${daten.unterrichtstag ? '' : ' · kein Unterrichtstag'}`;
    woche.setzen(daten.woche);
    const i = daten.woche.findIndex((w) => w.datum === daten.datum);
    if (i >= 0) woche._markieren?.();
    leeren(texteKoerper).append(...daten.tagestexte.map(textZeile), textZeile(null));
    tabs.texte([`Tagestexte (${daten.tagestexte.length})`, 'Kalendertag']);
    titel.textContent = daten.titel;
    const gruende = await elemente('abwesenheitsgrund');
    leeren(grund).append(h('option', { value: '' }, 'Kein Grund'), ...gruende.map((g) => h('option', { value: g.k, selected: g.k === daten.kalendertag.grund }, g.l ? `${g.k} – ${g.l}` : g.k)));
    for (const r of [rNormal, rFeiertag, rFrei, rStunden]) r.firstChild.checked = r.firstChild.value === daten.kalendertag.art;
    stunden.value = daten.kalendertag.stunden;
    notiz.value = daten.kalendertag.notiz;
    f.titel(`Kalender – ${daten.titel}`);
  }
  async function laden() {
    const d = planungsdatum();
    if (!d) return;
    daten = await api.get(vpfad(`/kalender?datum=${d}`));
    await anzeigen();
  }
  f.an('vertretung.datum', () => laden().catch(fehlerMelden));
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Einstellungen') && !f.el.contains(document.activeElement)) laden().catch(() => {}); });
  f.werkzeug({ text: 'Heute', symbol: 'kalender', titel: 'Heute', aktion: () => import('./kern.js').then((k) => k.datumSetzen(k.isoAus(new Date()))) });
  f.werkzeug({ text: '', symbol: 'pfeil_runter', nurSymbol: true, titel: 'Nächster Unterrichtstag', aktion: () => blaettern(1) });
  laden().catch(fehlerMelden);
}

export const fenster = {
  'vertretung.kalender': {
    titel: 'Kalender', symbol: 'kalender', breite: 620, hoehe: 520, einzig: () => 'kalender',
    erneutGeoeffnet: (f, p) => { if (p?.reiter !== undefined) f.inhalt.querySelectorAll('.vt-reiter button')[p.reiter]?.click(); },
    erstellen: kalenderFenster,
  },
};
